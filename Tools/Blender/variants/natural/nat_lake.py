"""
FishingKing - "natural" variant of the LAKE stage.

Writes (only) into Tools/Blender/_tmp/variants/natural/:
  lake_back.png   640x400 opaque: sky, far scenery, water (+ baked reflections, pier/reed shadows)
  lake_front.png  640x400 transparent: pier end, props, reeds, lily pads, rowboat, rocks
  lake.json       gameplay layout (same keys as Data/stage_lake.json)

Run: blender -b --python Tools/Blender/variants/natural/nat_lake.py
"""
import sys
import os
import math
import random
import json

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N  # noqa: E402
from nat_core import C, P, bpy, np, Vector, Matrix, Euler  # noqa: E402

STAND = 1.0
SS = 3
W, H = P.W, P.H
SHORE_Y = 180.0
VIS_TOP, VIS_BOT = (H - 270) // 2, (H + 270) // 2       # rows of the in-game 480x270 crop (65..335)
VIS_L, VIS_R = (W - 480) // 2, (W + 480) // 2
HOR = H / 2 - P.F_PX * math.tan(math.radians(P.PITCH))    # horizon row (top-down), ~89.5

# ----------------------------------------------------------------------------- palette (late morning, calm)
PAL = dict(
    sky=["#d6dcd6", "#cbd6d6", "#bccdd5", "#adc3d2", "#9fb9cf", "#93b0ca", "#88a7c4"],   # horizon -> top
    cloud=["#b3bec8", "#cbd3d8", "#e0e3e1", "#efede7"],
    mount="#6f817f", hills="#4c5e45",
    grass="#6a7444", mud="#6a5c49", stone="#a0967f",
    dec=["#5f7340", "#6c7c45", "#55683b", "#7a8550", "#687a4a"], willow="#7f8b52", under="#465636",
    con=["#3b5143", "#415846", "#384a3d"],
    bark="#584a3d", birch="#c3bdab",
    # 7 water bands, horizon (sky reflection) -> pier (deep teal); interpolated in OKLab
    water_keys=[(0.0, "#a9bcc0"), (0.3, "#88a3ab"), (0.62, "#62808a"), (1.0, "#3f5d66")],
    wood=["#8e7c65", "#857159", "#9a8a73"],
    deck_dark="#4a3d32", post="#6b5a48",
)


# ============================================================================ special materials
def sky_mat():
    """Screen-space sky bands (no dither): band edges wobble by low-frequency noise instead."""
    m = bpy.data.materials.new("NatSky")
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["Window"])
    hor = 0.5 + (P.F_PX * math.tan(math.radians(P.PITCH))) / H     # horizon in window y (bottom-up)
    u = nb.math("DIVIDE", nb.math("SUBTRACT", s[1], hor), 1.0 - hor)
    u = nb.math("POWER", nb.math("MAXIMUM", u, 0.0), 0.8)
    n = len(PAL["sky"])
    no = nb.node("ShaderNodeTexNoise")
    try:
        no.noise_dimensions = "2D"
    except Exception:
        pass
    nb.link(nb.combine(nb.math("MULTIPLY", s[0], 7.0), nb.math("MULTIPLY", u, 2.0), 0.0), no.inputs["Vector"])
    no.inputs["Scale"].default_value = 1.0
    no.inputs["Detail"].default_value = 1.0
    f = nb.math("ADD", nb.math("MULTIPLY", u, n), nb.math("MULTIPLY", nb.math("SUBTRACT", no.outputs["Fac"], 0.5), 0.8))
    f = nb.math("DIVIDE", f, n)
    col = nb.ramp(f, [(i / n, N.lin4(c)) for i, c in enumerate(PAL["sky"])])
    nb.output_emission(col, 1.0)
    return m


def water_cols(nbands):
    keys = PAL["water_keys"]
    out = []
    for i in range(nbands):
        u = i / (nbands - 1)
        for (u0, c0), (u1, c1) in zip(keys, keys[1:]):
            if u0 <= u <= u1:
                t = (u - u0) / (u1 - u0)
                lab = N.srgb2lab(N.hex2srgb(c0)) * (1 - t) + N.srgb2lab(N.hex2srgb(c1)) * t
                out.append(N.srgb2hex(N.lab2srgb(lab)))
                break
    return out


WATER_N = 7
WCOLS = water_cols(WATER_N)
WSHAD = [N.shade_hex(c, dl=-0.065, dc=0.9, cool=0.5) for c in WCOLS]
WLINE = N.shade_hex(WCOLS[0], dl=0.05, dc=0.8)          # sunlit waterline under the far bank
U_NEAR = 8.85
U_GAMMA = 0.8


def water_mat():
    """Placeholder bands by world distance + cast-shadow switch. compose_water re-bands in screen space;
    the exact band colours are only used as keys (water / shadowed water)."""
    m = bpy.data.materials.new("NatWater")
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    s = nb.sep(geo.outputs["Position"])
    D = nb.math("ADD", s[1], P.CAM_BACK)
    u = nb.math("POWER", nb.math("MINIMUM", nb.math("DIVIDE", U_NEAR, nb.math("MAXIMUM", D, 0.5)), 1.0), U_GAMMA)
    f = nb.math("MULTIPLY", u, 0.999)
    col = nb.ramp(f, [(i / WATER_N, N.lin4(c)) for i, c in enumerate(WCOLS)])
    sh = nb.ramp(f, [(i / WATER_N, N.lin4(c)) for i, c in enumerate(WSHAD)])
    L = N._light_value(nb, ao=0.0)
    mask = nb.math("LESS_THAN", L, 0.45)
    col = nb.mix(mask, col, sh)
    nb.output_emission(col, 1.0)
    return m


def bank_mat():
    """Grass bank with a mud/stone lip at the waterline (mirror-safe: uses |Z|)."""
    m = bpy.data.materials.new("NatBank")
    nb = C.NB(m)
    L = N._light_value(nb, ao=0.5, ao_dist=3.0)
    nz = N._noise(nb, 0.6, "world", detail=1.0)
    L = nb.math("MULTIPLY_ADD", nz, 0.2, nb.math("SUBTRACT", L, 0.3))
    geo = nb.node("ShaderNodeNewGeometry")
    z = nb.math("ABSOLUTE", nb.sep(geo.outputs["Position"])[2])
    nz2 = N._noise(nb, 0.35, "world", detail=1.0)
    lip = nb.math("LESS_THAN", nb.math("MULTIPLY_ADD", nz2, 0.5, z), 0.62)
    g = N.ramp_colours(nb, L, N.gen_ramp(PAL["grass"], 5))
    s = N.ramp_colours(nb, L, N.gen_ramp(PAL["stone"], 5, spread=0.8))
    col = nb.mix(lip, g, s)
    col = N._haze(nb, col)
    nb.output_emission(col, 1.0)
    return m


def post_mat():
    """Weathered pier post: dark wet band + algae near the water (|Z| < 0.35)."""
    m = bpy.data.materials.new("NatPost")
    nb = C.NB(m)
    L = N._light_value(nb, ao=0.5, ao_dist=0.4)
    nz = N._noise(nb, 3.0, "world", detail=1.0, stretch=(1.0, 1.0, 0.25))
    L = nb.math("MULTIPLY_ADD", nz, 0.12, nb.math("SUBTRACT", L, 0.06))
    geo = nb.node("ShaderNodeNewGeometry")
    z = nb.math("ABSOLUTE", nb.sep(geo.outputs["Position"])[2])
    wet = nb.math("LESS_THAN", z, 0.38)
    a = N.ramp_colours(nb, L, N.gen_ramp(PAL["post"], 5))
    b = N.ramp_colours(nb, L, N.gen_ramp("#4d5040", 5))
    nb.output_emission(nb.mix(wet, a, b), 1.0)
    return m


def rock_mat(base, moss, wet, zwet, name):
    """Faceted rock: stone ramp, moss only on up-facing faces (normal.z > 0.7), a darker wet band at the
    waterline (|Z| < zwet, mirror-safe) and 1-2 dark crack lines (voronoi cell edges)."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    L = N._light_value(nb, ao=0.45, ao_dist=0.5)
    geo = nb.node("ShaderNodeNewGeometry")
    pos = geo.outputs["Position"]
    z = nb.math("ABSOLUTE", nb.sep(pos)[2])
    nz = nb.sep(geo.outputs["Normal"])[2]
    rk = N.gen_ramp(base, 4)
    a = N.ramp_colours(nb, L, rk)
    mo = N.ramp_colours(nb, L, N.gen_ramp(moss, 4))
    w = N.ramp_colours(nb, L, N.gen_ramp(wet, 4))
    is_moss = nb.math("GREATER_THAN", nz, 0.7)
    col = nb.mix(is_moss, a, mo)
    col = nb.mix(nb.math("LESS_THAN", z, zwet), col, w)
    vo = nb.node("ShaderNodeTexVoronoi")
    vo.voronoi_dimensions = "3D"
    vo.feature = "DISTANCE_TO_EDGE"
    nb.link(pos, vo.inputs["Vector"])
    vo.inputs["Scale"].default_value = 1.6
    crack = nb.math("MULTIPLY", nb.math("LESS_THAN", vo.outputs["Distance"], 0.022),
                    nb.math("GREATER_THAN", z, zwet + 0.03))
    col = nb.mix(crack, col, N.lin4(rk[0]))
    nb.output_emission(col, 1.0)
    m["ramp"] = rk
    m["cols"] = rk + N.gen_ramp(moss, 4) + N.gen_ramp(wet, 4)
    return m


def pad_mat(base, name="Pad"):
    """Lily pad: 3 tones chosen per FACE (attribute 'tone': 0 notch wedge, 1 base, 2 sun-side rim);
    a cast shadow (reeds, boat) drops the tone by one."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    rp = N.gen_ramp(base, 5)
    cols = [rp[1], rp[2], rp[4]]
    at = nb.node("ShaderNodeAttribute")
    at.attribute_type = "GEOMETRY"
    at.attribute_name = "tone"
    t = at.outputs["Fac"]
    L = N._light_value(nb, ao=0.0)
    t = nb.math("SUBTRACT", t, nb.math("LESS_THAN", L, 0.45))
    col = nb.ramp(nb.math("DIVIDE", t, 2.0), [(0.0, N.lin4(cols[0])), (0.25, N.lin4(cols[1])),
                                              (0.75, N.lin4(cols[2]))])
    nb.output_emission(col, 1.0)
    m["ramp"] = cols
    return m


# ============================================================================ builders
def mass(x):
    """Canopy height multiplier: 3-4 broad undulating masses along the far shore."""
    f = N.fbm2(np.array(x * 0.0105 + 3.1), np.array(4.2), 2, 17)
    s = float(np.clip((f - 0.36) / 0.3, 0, 1))
    return 0.72 + 0.55 * s * s * (3 - 2 * s)


def deciduous(B, rnd, x, y, z, h, leaf, bark, kind=None, trunk=True):
    kind = kind or rnd.choice(["round", "oval", "wide", "irregular", "irregular", "willow"])
    th = h * rnd.uniform(0.32, 0.45) * (1.0 if trunk else 0.55)
    r0 = max(h * 0.03, 0.18)
    lean = rnd.uniform(-0.08, 0.08)
    if trunk:
        B[bark].tube([(x, y, z - 0.5), (x + lean * th * 0.5, y, z + th * 0.5), (x + lean * th, y, z + th)],
                     [r0 * 1.25, r0, r0 * 0.7], 5)
    rx, rz = {"round": (0.33, 0.31), "oval": (0.22, 0.36), "wide": (0.42, 0.27), "irregular": (0.3, 0.3),
              "willow": (0.36, 0.3)}[kind]
    rx *= h * 1.3
    rz *= h
    cx, cz = x + lean * th, z + th + rz * 0.72
    subs = [(0.0, 0.0, 1.0)]
    if kind == "irregular":
        subs = [(rnd.uniform(-0.35, -0.15) * rx, rnd.uniform(-0.25, 0.05) * rz, rnd.uniform(0.6, 0.75)),
                (rnd.uniform(0.15, 0.35) * rx, rnd.uniform(-0.1, 0.2) * rz, rnd.uniform(0.6, 0.8)),
                (rnd.uniform(-0.1, 0.1) * rx, rnd.uniform(0.2, 0.4) * rz, rnd.uniform(0.5, 0.65))]
    for (ox, oz, sc) in subs:
        n = int(16 + 14 * sc)
        for _ in range(n):
            ph = rnd.uniform(0, 2 * math.pi)
            ct = rnd.uniform(-0.55, 1.0)
            st = math.sqrt(1 - ct * ct)
            k = rnd.uniform(0.5, 0.95)
            px = cx + ox + math.cos(ph) * st * rx * sc * k
            py = y + math.sin(ph) * st * rx * sc * k * 0.8
            pz = cz + oz + ct * rz * sc * k
            cr = rnd.uniform(0.22, 0.34) * (rx + rz) * 0.5 * (0.6 + 0.4 * sc)
            scl = (1.0, 1.0, 0.82)
            if kind == "willow" and ct < 0.1:
                scl = (0.75, 0.75, 1.6)
                pz -= cr * 0.6
            B[leaf].ico((px, py, pz), cr, 1, scl, 0.22, rnd)
    if trunk:
        for _ in range(rnd.randint(1, 3)):
            a = rnd.uniform(-1, 1)
            B[bark].tube([(cx, y, z + th * 0.8), (cx + a * rx * 0.55, y - 0.3, cz - rz * 0.25)],
                         [r0 * 0.55, r0 * 0.3], 4)


def conifer(B, rnd, x, y, z, h, leaf, bark, trunk=True):
    if trunk:
        B[bark].tube([(x, y, z - 0.5), (x, y, z + h * 0.92)], [max(h * 0.022, 0.15), 0.06], 4)
    n = rnd.randint(7, 10)
    rb = h * rnd.uniform(0.17, 0.23)
    z0 = 0.1 if trunk else 0.02
    for i in range(n):
        t = i / (n - 1)
        zi = z + h * (z0 + (0.86 - z0) * t)
        ri = rb * (1 - t) ** 0.85 + h * 0.03
        B[leaf].cone((x + rnd.uniform(-0.08, 0.08) * ri, y, zi), ri, h * 0.19, segs=12, jag=0.3, droop=0.4,
                     rnd=rnd, cap=False)
    B[leaf].cone((x, y, z + h * 0.84), h * 0.035, h * 0.16, segs=5, cap=False)


def rand_rot(rnd, tilt=0.5):
    return Euler((rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), rnd.uniform(0, 2 * math.pi))).to_matrix()


def rock(acc, ring_acc, rnd, x, y, r):
    """Faceted boulder: 2-4 intersecting, rotated, jittered icospheres (subdiv 1, flat shaded) half sunk in
    the water, plus a 1px light ripple ring (annulus on the water) around every lobe."""
    k = rnd.randint(2, 4)
    pm = px_m(y)
    for i in range(k):
        if i == 0:
            c = Vector((x, y, -0.14 * r))
            rr = r
            sc = Vector((rnd.uniform(1.05, 1.25), rnd.uniform(0.85, 1.0), rnd.uniform(0.8, 0.95)))
        else:
            a = rnd.uniform(0, 2 * math.pi)
            d = rnd.uniform(0.45, 0.7) * r
            c = Vector((x + math.cos(a) * d, y + math.sin(a) * d * 0.7, -0.25 * r + rnd.uniform(-0.05, 0.12) * r))
            rr = r * rnd.uniform(0.5, 0.75)
            sc = Vector((rnd.uniform(0.9, 1.2), rnd.uniform(0.8, 1.05), rnd.uniform(0.75, 1.0)))
        R = rand_rot(rnd, 0.35)
        ret = bmesh_ico(acc.bm, 1)
        for v in ret:
            o = v.co.copy()
            j = 1.0 + rnd.uniform(-0.25, 0.25) * 0.5
            o = Vector((o.x * sc.x, o.y * sc.y, o.z * sc.z)) * rr * j
            v.co = c + R @ o
        # water-plane cross-section of this lobe (approx. ellipse) -> ripple ring
        zc = c.z
        hz = rr * sc.z
        if ring_acc is not None and abs(zc) < hz:
            s = math.sqrt(max(1 - (zc / hz) ** 2, 0.05))
            ex, ey = rr * sc.x * s * 1.02, rr * sc.y * s * 1.02
            ang = math.atan2(R[1][0], R[0][0])
            annulus(ring_acc, c.x, c.y, ex, ey, ang, pm * 1.25)


def bmesh_ico(bm, sub):
    import bmesh as _bm
    return _bm.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)["verts"]


def annulus(acc, x, y, ex, ey, ang, w, z=0.004, segs=24):
    bm = acc.bm
    ca, sa = math.cos(ang), math.sin(ang)
    inner, outer = [], []
    for k in range(segs):
        t = 2 * math.pi * k / segs
        for lst, ad in ((inner, 0.0), (outer, w)):
            px, py = math.cos(t) * (ex + ad), math.sin(t) * (ey + ad)
            lst.append(bm.verts.new((x + px * ca - py * sa, y + px * sa + py * ca, z)))
    for k in range(segs):
        k2 = (k + 1) % segs
        bm.faces.new((inner[k], inner[k2], outer[k2], outer[k]))


def bush(B, rnd, x, y, z, h, leaf):
    for _ in range(rnd.randint(5, 8)):
        B[leaf].ico((x + rnd.uniform(-0.7, 0.7) * h, y + rnd.uniform(-0.3, 0.3) * h, z + rnd.uniform(0.15, 0.5) * h),
                    h * rnd.uniform(0.3, 0.45), 1, (1.2, 1, 0.75), 0.2, rnd)


def zc_of(y, z=0.0):
    return P.project((0.0, y, z), STAND)[2]


def px_m(y, z=0.0):
    """metres per output pixel at distance y."""
    return zc_of(y, z) / P.F_PX


def reed_clump(B, rnd, cx, cy, n, spread, hmin, hmax, green, straw, head, stalk, zb=-0.25):
    """Natural reed clump: a dense dark base mass, then fewer, wider (2.2 px at the base) tapered blades,
    60% of them arching outward from the clump centre; cattails on 15% of the stalks with a light spike.
    Positions are clamped to 0.8*spread (no strays). One green ramp (3 tones) + one straw accent."""
    pm = px_m(cy)
    lim = 0.8 * spread
    wbase = max(pm * 2.6, 0.04)
    wtip = pm * 1.0

    def pos(sx, sy):
        return (cx + max(-lim, min(lim, rnd.gauss(0, spread * sx))),
                cy + max(-lim * 0.6, min(lim * 0.6, rnd.gauss(0, spread * sy))))

    def blade(x, y, h, lean, droop, dirx, diry, w0, mat, seg=7):
        pts = []
        for k in range(seg + 1):
            t = k / seg
            pts.append((x + dirx * lean * t ** 1.6, y + diry * lean * t ** 1.6, zb + h * t - droop * t ** 3))
        ws = [w0 * (1 - t) + wtip * t for t in (k / seg for k in range(seg + 1))]
        ws[-1] = wtip * 0.6
        # width across the view; turned towards (lit) or away from (shadow) the sun per blade
        B[mat].ribbon(pts, ws, side="view", face=rnd.uniform(-0.9, 0.9))

    # dense dark base mass (short blades in the darkest tone)
    dark = green["dark"]
    for _ in range(rnd.randint(15, 20)):
        x, y = pos(0.4, 0.25)
        h = rnd.uniform(0.2, 0.3) * hmax
        a = rnd.uniform(0, 2 * math.pi)
        blade(x, y, h, rnd.uniform(0.05, 0.25) * h, 0.0, math.cos(a), math.sin(a) * 0.5, wbase * 1.3, dark, 3)
    for _ in range(n):
        x, y = pos(0.42, 0.28)
        dxc, dyc = x - cx, (y - cy) * 0.6
        d = math.hypot(dxc, dyc)
        if d < spread * 0.12:
            a = rnd.uniform(0, 2 * math.pi)
            dxc, dyc, d = math.cos(a), math.sin(a) * 0.5, 1.0
        dirx, diry = dxc / d, dyc / d
        centre = 1.0 - 0.35 * min(1.0, d / max(lim, 1e-3))
        h = rnd.uniform(hmin, hmax) * centre
        if rnd.random() < 0.6:
            lean = rnd.uniform(0.2, 0.42) * h
            droop = rnd.uniform(0.25, 0.5) * h
        else:
            lean = rnd.uniform(0.03, 0.14) * h
            droop = 0.0
        mat = straw if rnd.random() < 0.18 else green["main"]
        blade(x, y, h, lean, droop, dirx, diry, rnd.uniform(1.0, 1.35) * wbase, mat)
    # cattails on ~15% of stalks
    for _ in range(max(1, int(n * 0.15))):
        x, y = pos(0.3, 0.2)
        h = rnd.uniform(hmax * 0.85, hmax * 1.1)
        lx = rnd.uniform(-0.1, 0.1) * h
        B[stalk].tube([(x, y, zb), (x + lx * 0.5, y, zb + h * 0.5), (x + lx, y, zb + h)], max(pm * 0.55, 0.012), 4)
        hz = zb + h * 0.8
        hx = x + lx * 0.8
        B[head].uvsphere((hx, y - 0.01, hz), max(pm * 1.05, 0.035), (1, 1, 3.0), seg=8, rings=6)
        # the thin light flower spike above the head (the '1px lighter top')
        B[straw].tube([(hx, y - 0.01, hz + max(pm * 1.05, 0.035) * 2.6),
                       (x + lx * 0.95, y - 0.01, zb + h * 0.98)], max(pm * 0.6, 0.012), 4)


def lily_pad(bank, rnd, x, y, r, mat, notch=0.3):
    """Notched pad as a centre fan + outer rim ring; face attribute 'tone' = 0 notch wedge, 1 base,
    2 sun-facing rim."""
    acc = bank[mat]
    bm = acc.bm
    lay = bm.faces.layers.float.get("tone") or bm.faces.layers.float.new("tone")
    n = 20
    a0 = rnd.uniform(0, 2 * math.pi)
    sun_a = math.atan2(N.SUN_DIR.y, N.SUN_DIR.x)
    z = 0.025
    c = bm.verts.new((x, y, z))
    inner, outer = [], []
    for k in range(n + 1):
        a = a0 + notch / 2 + (2 * math.pi - notch) * k / n
        rr = r * rnd.uniform(0.96, 1.03)
        up = 0.015 if rnd.random() < 0.3 else 0.0
        inner.append(bm.verts.new((x + math.cos(a) * rr * 0.72, y + math.sin(a) * rr * 0.72, z)))
        outer.append(bm.verts.new((x + math.cos(a) * rr, y + math.sin(a) * rr, z + up)))
    for k in range(n):
        am = a0 + notch / 2 + (2 * math.pi - notch) * (k + 0.5) / n
        edge = k == 0 or k == n - 1
        f1 = bm.faces.new((c, inner[k], inner[k + 1]))
        f1[lay] = 0.0 if edge else 1.0
        f2 = bm.faces.new((inner[k], outer[k], outer[k + 1], inner[k + 1]))
        lit = math.cos(am - sun_a) > 0.3
        f2[lay] = 0.0 if edge else (2.0 if lit else 1.0)


def water_lily(B, rnd, x, y, s, petal, centre):
    for k in range(8):
        a = 2 * math.pi * k / 8 + rnd.uniform(-0.2, 0.2)
        B[petal].ico((x + math.cos(a) * 0.07 * s, y + math.sin(a) * 0.07 * s, 0.08 * s), 0.06 * s, 1,
                     (1.5, 0.7, 0.6), 0.1, rnd, rot=a)
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.3
        B[petal].ico((x + math.cos(a) * 0.035 * s, y + math.sin(a) * 0.035 * s, 0.13 * s), 0.045 * s, 1,
                     (1.2, 0.7, 1.0), 0.1, rnd, rot=a)
    B[centre].ico((x, y, 0.15 * s), 0.035 * s, 1, (1, 1, 0.7))


def rowboat(B, rnd, x, y, yaw, L=3.3, beam=1.25, depth=0.48, hull=None, inner=None, rail=None, seat=None):
    ca, sa = math.cos(yaw), math.sin(yaw)

    def T(px, py, pz):          # boat local (px along length, py across) -> world
        return (x + px * ca - py * sa, y + px * sa + py * ca, pz)

    ns, nm = 16, 9

    def section(t, shrink=0.0):
        w = beam / 2 * (math.sin(math.pi * min(1.0, 0.18 + 0.82 * t)) ** 0.6) - shrink
        w = max(w, 0.01)
        sheer = 0.32 + 0.12 * t ** 3
        keel = -0.14 + 0.06 * t
        pts = []
        for j in range(nm):
            ph = -math.pi / 2 + math.pi * j / (nm - 1)
            yy = w * math.sin(ph)
            zz = sheer - (sheer - keel + depth * 0.25) * (math.cos(ph) ** 0.7) - shrink
            pts.append((yy, zz))
        return pts

    for mat, shrink in ((hull, 0.0), (inner, 0.035)):
        bm = B[mat].bm
        rings = []
        for i in range(ns + 1):
            t = i / ns
            px = -L / 2 + L * t
            rings.append([bm.verts.new(T(px, yy, zz)) for (yy, zz) in section(t, shrink)])
        for i in range(ns):
            for j in range(nm - 1):
                bm.faces.new((rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]))
        if shrink == 0.0:
            bm.faces.new(rings[0][::-1])
    for side in (-1, 1):
        pts = []
        for i in range(ns + 1):
            t = i / ns
            sec = section(t)
            yy, zz = sec[0] if side < 0 else sec[-1]
            pts.append(T(-L / 2 + L * t, yy, zz + 0.01))
        B[rail].tube(pts, 0.035, 5)
    for px in (-0.9, 0.05, 0.85):
        t = (px + L / 2) / L
        w = beam / 2 * (math.sin(math.pi * min(1.0, 0.18 + 0.82 * t)) ** 0.6) - 0.05
        B[seat].box(T(px, 0, 0.2), (0.22, 2 * w, 0.05), rot=(0, 0, yaw))
    B[rail].tube([T(-1.2, -0.25, 0.26), T(1.1, 0.12, 0.3)], 0.025, 5)
    B[seat].box(T(1.25, 0.15, 0.3), (0.45, 0.1, 0.02), rot=(0, 0, yaw + 0.15))


def pad_x_ok(x, y):
    """Keep pads >= ~150 px from the screen centre (open water for gameplay)."""
    return abs(x) >= 0.29 * (y + 10.5)


# ============================================================================ the stage
def build(rnd):
    B = N.Bank()     # back layer
    F = N.Bank()     # front layer
    objs = {"sky": [], "water": [], "noref": []}
    # --- sky + clouds
    sky = C.poly_object("Sky", [(-9000, -600), (9000, -600), (9000, 4000), (-9000, 4000)], sky_mat(), 0.1, 4000.0)
    sky["layer"] = "back"
    sky.visible_shadow = False
    objs["sky"].append(sky)
    cm = N.lit("#f1efe8", n=4, ramp=PAL["cloud"], pos=[0.0, 0.3, 0.55, 0.85], ao=0.0, haze=0.45, name="Cloud")
    cb = N.Bank()
    for (cx, cz, L, th) in ((-950, 150, 420, 45), (-260, 118, 260, 32), (720, 165, 520, 55), (1350, 112, 240, 28),
                            (-1500, 330, 700, 50), (260, 365, 820, 55), (1250, 300, 560, 45)):
        nn = int(L / 22)
        for k in range(nn):
            u = (k + rnd.uniform(0, 1)) / nn
            env = math.sin(math.pi * u) ** 0.7
            x = cx - L / 2 + L * u
            r = th * rnd.uniform(0.35, 0.6) * (0.45 + 0.55 * env)
            z = cz + env * th * rnd.uniform(0.2, 0.75)
            vs = cb[cm].ico((x, 3000 + rnd.uniform(-40, 40), z), r, 2, (1.7, 0.7, 0.62), 0.1, rnd)
            for v in vs:
                if v.co.z < cz - th * 0.15:
                    v.co.z = cz - th * 0.15
    for ob in cb.build("back", shadow=False, prefix="Cloud"):
        objs["sky"].append(ob)
    # --- mountains (heightfield)
    mm = N.lit(PAL["mount"], tex=0.1, tex_scale=0.012, ao=0.0, name="Mount")
    acc = N.Acc()
    xs = [-2800 + 35 * i for i in range(161)]
    ys = [1900 + 60 * j for j in range(14)]

    def mz(x, y):
        r = 1.0 - abs(N.fbm2(x * 0.0012, y * 0.002, 4, 3) * 2 - 1)
        base = 40 + 260 * r ** 1.6 * (0.6 + 0.4 * math.sin(x * 0.0009 + 1.3))
        return float(base * min(1.0, (y - 1900) / 250.0 + 0.15))
    acc.quad_strip_grid(xs, ys, mz)
    acc.build("Mount", mm, smooth=True, layer="back", shadow=False)
    # --- far hills with forest canopy
    hm = N.lit(PAL["hills"], tex=0.1, tex_scale=0.25, ao=0.4, ao_dist=15.0, name="Hills")
    acc = N.Acc()
    xs = [-1000 + 5 * i for i in range(401)]
    ys = [330 + 12 * j for j in range(32)]

    def hz(x, y):
        k = min(1.0, (y - 330) / 90.0)
        base = 2 + (6 + 30 * N.fbm2(x * 0.004, y * 0.004, 3, 7) ** 1.2) * k
        lump = 3.5 * N.vnoise2(x * 0.16, y * 0.16, 11) * (0.4 + 0.6 * k)
        return float(base + lump)
    acc.quad_strip_grid(xs, ys, hz)
    acc.build("Hills", hm, smooth=True, layer="back")

    # --- far shore bank (heightfield)
    def edge(x):
        return SHORE_Y + 2.5 * math.sin(x * 0.021 + 0.7) + 3.0 * (N.fbm2(x * 0.03, 0.5, 3, 5) - 0.5)

    def bz(x, y):
        e = edge(x)
        if y < e:
            return float(-0.6 + (y - (e - 3.0)) * 0.2)
        rise = min(1.0, (y - e) / 6.0)
        return float(rise * (1.8 + 0.8 * N.fbm2(x * 0.02, y * 0.05, 2, 9)) + 0.25 * N.vnoise2(x * 0.3, y * 0.3, 2))
    acc = N.Acc()
    xs = [-260 + 1.5 * i for i in range(347)]
    ys = [SHORE_Y - 6 + k * 1.0 for k in range(16)] + [SHORE_Y + 10 + k * 5.0 for k in range(38)]
    acc.quad_strip_grid(xs, ys, bz)
    acc.build("Bank", bank_mat(), smooth=True, layer="back")
    # --- forest: one continuous canopy (overlapping trunkless crowns) in 3-4 masses, spruce spires above
    decm = [N.lit(c, tex=0.1, tex_scale=1.1, ao=0.55, ao_dist=2.5, name="Leaf") for c in PAL["dec"]]
    wilm = N.lit(PAL["willow"], tex=0.1, tex_scale=1.3, ao=0.5, ao_dist=2.5, name="Willow")
    conm = [N.lit(c, tex=0.1, tex_scale=1.4, ao=0.5, ao_dist=2.5, spread=1.1, name="Conif") for c in PAL["con"]]
    underm = N.lit(PAL["under"], tex=0.08, tex_scale=1.2, ao=0.6, ao_dist=2.0, bias=-0.08, name="Under")
    bark = N.lit(PAL["bark"], ao=0.3, name="Bark")
    birch = N.lit(PAL["birch"], ao=0.3, spread=0.8, name="Birch")
    placed = []

    def free(x, y, r):
        return all((x - a) ** 2 + (y - b) ** 2 > (r + c) ** 2 for a, b, c in placed)

    def plant(x0, x1, y0, y1, n, hmin, hmax, pcon, kinds=None):
        tries = 0
        while n > 0 and tries < n * 30:
            tries += 1
            x = rnd.uniform(x0, x1)
            y = rnd.uniform(y0, y1)
            e = edge(x)
            if y < e + 3.0:
                continue
            h = rnd.uniform(hmin, hmax) * mass(x)
            r = h * 0.22
            if not free(x, y, r * 0.35):
                continue
            placed.append((x, y, r * 0.35))
            z = bz(x, y) - 0.2
            if rnd.random() < pcon:
                conifer(B, rnd, x, y, z, h * 1.25, rnd.choice(conm), bark, trunk=False)
            else:
                kind = rnd.choice(kinds) if kinds else None
                is_birch = rnd.random() < 0.05 and y < SHORE_Y + 16
                leaf = wilm if kind == "willow" else rnd.choice(decm)
                deciduous(B, rnd, x, y, z, h, leaf, birch if is_birch else bark, kind, trunk=is_birch)
            n -= 1
    placed.append((52, SHORE_Y + 8, 9))    # boathouse clearing
    plant(-150, 150, SHORE_Y + 2, SHORE_Y + 14, 60, 5.0, 9.0, 0.22, ["round", "irregular", "willow", "wide", "oval"])
    plant(-170, 170, SHORE_Y + 12, SHORE_Y + 50, 120, 6.5, 10.5, 0.32)
    plant(-230, 230, SHORE_Y + 45, SHORE_Y + 150, 170, 8.0, 12.0, 0.42)
    for _ in range(14):      # spruce spires poking above the canopy masses
        x = rnd.uniform(-130, 130)
        y = rnd.uniform(SHORE_Y + 8, SHORE_Y + 40)
        if y > edge(x) + 4 and abs(x - 52) > 10:
            conifer(B, rnd, x, y, bz(x, y) - 0.2, rnd.uniform(14, 18) * (0.85 + 0.3 * (mass(x) - 0.72) / 0.55),
                    rnd.choice(conm), bark, trunk=False)
    for _ in range(170):     # dark understory shrubs (3-5 m) along the bank
        x = rnd.uniform(-160, 160)
        e = edge(x)
        if abs(x - 52) < 8:
            continue
        bush(B, rnd, x, e + rnd.uniform(0.8, 3.5), bz(x, e + 1.5) - 0.3, rnd.uniform(3.0, 5.0), underm)
    # boathouse on the far shore
    wall = N.lit("#857461", tex=0.06, tex_scale=2.0, ao=0.5, name="Wall")
    roof = N.lit("#5d5f59", ao=0.4, name="Roof")
    dark = N.lit("#3a332d", ao=0.2, name="Dark")
    bx, by = 52.0, edge(52.0) + 1.0
    B[wall].box((bx, by + 2.5, 1.9), (7.0, 5.0, 3.6))
    B[dark].box((bx, by - 0.02, 1.2), (3.2, 0.1, 2.2))
    for sgn in (-1, 1):
        B[roof].box((bx + sgn * 1.9, by + 2.5, 4.35), (4.3, 5.8, 0.18), rot=(0, sgn * math.radians(-32), 0))
    B[wall].box((bx - 5.5, by - 2.5, 0.25), (1.6, 7.0, 0.15))
    # --- water
    wat = C.mesh_object("Water", _plane(-6000, -40, 6000, 3000), water_mat())
    wat["layer"] = "back"
    wat.visible_shadow = False
    objs["water"].append(wat)
    objs["back"] = B.build("back", prefix="Far")

    # =================================================================== FRONT (near play area)
    wood = [N.lit(c, tex=0.05, tex_scale=5.0, tex_stretch=(0.18, 2.2, 2.2), ao=0.5, ao_dist=0.3,
                  name="Plank") for c in PAL["wood"]]
    deck_dark = N.lit(PAL["deck_dark"], ao=0.3, name="DeckDark")
    nail = N.flat("#2e2723", name="Nail")
    postm = post_mat()
    r2 = random.Random(3)
    top = STAND
    for x in (-1.08, 0.0, 1.08):
        F[deck_dark].box((x, -9.0, top - 0.2), (0.12, 20.2, 0.2))
    F[deck_dark].box((0, -9.0, top - 0.32), (2.5, 20.2, 0.03))
    # fascia / end board at the far end of the deck (reads as a 2px dark edge)
    F[deck_dark].box((0.0, 1.17, top - 0.12), (2.66, 0.1, 0.22))
    y = 1.12
    while y > -19.5:
        w = r2.uniform(0.18, 0.23)
        x0 = -1.3 + r2.uniform(-0.07, 0.03)
        x1 = 1.3 + r2.uniform(-0.03, 0.07)
        m = r2.choice(wood)
        F[m].box(((x0 + x1) / 2, y - w / 2, top - 0.035 + r2.uniform(-0.01, 0.006)), (x1 - x0, w - 0.05, 0.07),
                 rot=(r2.uniform(-0.01, 0.01), 0, r2.uniform(-0.008, 0.008)))
        for sx in (-1.08, 0.0, 1.08):     # nail heads over the stringers
            if r2.random() < 0.85:
                F[nail].box((sx + r2.uniform(-0.02, 0.02), y - w / 2, top + 0.002), (0.035, 0.06, 0.01))
        y -= w
    for (px, py, ph) in ((-1.4, 0.95, 0.62), (1.4, 0.95, 0.5), (-1.42, -2.8, 0.05), (1.42, -2.8, 0.05),
                         (-1.42, -6.6, 0.05), (1.42, -6.6, 0.05)):
        bm = F[postm]
        bm.tube([(px, py, -1.2), (px, py, top + ph - 0.04)], 0.12, 9)
        bm.uvsphere((px, py, top + ph - 0.04), 0.12, (1, 1, 0.35), seg=9, rings=4)
    # mooring rope: two thick wraps on the right post + a tail hanging into the water
    rope = N.lit("#8f8264", n=4, ao=0.6, ao_dist=0.06, name="Rope")
    for zz in (top + 0.22, top + 0.34):
        F[rope].tube([(1.4 + 0.15 * math.cos(a), 0.95 + 0.15 * math.sin(a), zz + 0.01 * math.sin(a))
                      for a in [i * 2 * math.pi / 14 for i in range(15)]], 0.036, 6)
    F[rope].tube([(1.52, 0.86, top + 0.22), (1.6, 0.8, top + 0.02), (1.66, 0.84, top - 0.45), (1.7, 0.92, -0.1)],
                 [0.032, 0.03, 0.028, 0.028], 6)
    # tackle box (rust red, warm accent): body, lid, seam, handle, latches
    tb = N.lit("#8a5a3c", ao=0.5, ao_dist=0.3, name="Tackle")
    seam = N.lit("#8a5a3c", ao=0.0, bias=-0.45, name="TackleSeam")
    metal = N.lit("#b9b4a6", n=3, ao=0.0, name="Latch")
    rot = (0, 0, 0.12)
    ca, sa = math.cos(0.12), math.sin(0.12)

    def tbp(lx, ly, lz):
        return (-0.82 + lx * ca - ly * sa, -0.32 + lx * sa + ly * ca, top + lz)
    F[tb].box(tbp(0, 0, 0.1), (0.46, 0.26, 0.2), rot=rot)
    F[seam].box(tbp(0, 0, 0.205), (0.47, 0.27, 0.018), rot=rot)
    F[tb].box(tbp(0, 0, 0.235), (0.47, 0.27, 0.045), rot=rot)
    F[dark].tube([tbp(-0.1, 0, 0.255), tbp(-0.1, 0, 0.3), tbp(0.1, 0, 0.3), tbp(0.1, 0, 0.255)], 0.013, 4)
    for lx in (-0.14, 0.14):
        F[metal].box(tbp(lx, -0.138, 0.2), (0.05, 0.012, 0.06), rot=rot)
    # galvanised bucket (muted, not the brightest thing) with a thin wire handle
    galv = N.lit("#7d8486", ao=0.5, ao_dist=0.3, spread=0.6, name="Galv")
    F[galv].tube([(0.88, -1.05, top), (0.88, -1.05, top + 0.36)], [0.16, 0.19], 12)
    F[dark].uvsphere((0.88, -1.05, top + 0.36), 0.17, (1, 1, 0.08), seg=12, rings=4)
    F[dark].tube([(0.88 - 0.19 * math.cos(a), -1.05 - 0.05 * math.sin(a), top + 0.36 + 0.14 * math.sin(a))
                  for a in [i * math.pi / 8 for i in range(9)]], 0.011, 4)
    # --- reeds, sedges, rocks
    gm = N.lit("#6f7c42", n=3, ao=0.45, ao_dist=0.5, name="Reed")
    green = dict(main=gm, dark=N.lit("#6f7c42", n=3, ao=0.45, ao_dist=0.5, bias=-0.62, name="ReedDark"))
    straw = N.lit("#a59c66", n=3, ao=0.3, ao_dist=0.5, name="Straw")
    head = N.lit("#5a4332", n=3, ao=0.3, name="Cattail")
    stalk = N.lit("#7b7f4e", n=3, ao=0.3, name="Stalk")
    for (cx, cy, n, sp, h0, h1) in ((-6.2, 2.8, 45, 1.6, 0.9, 2.0), (-4.6, 4.2, 15, 0.8, 0.6, 1.3),
                                    (7.0, 5.0, 40, 1.6, 0.9, 2.1), (8.8, 7.5, 20, 1.2, 0.7, 1.5),
                                    (-13.5, 18, 35, 2.4, 1.2, 2.4), (15.5, 30, 35, 2.6, 1.3, 2.6),
                                    (-19, 40, 25, 3.0, 1.2, 2.3)):
        reed_clump(F, rnd, cx, cy, n, sp, h0, h1, green, straw, head, stalk)
    rockm = rock_mat("#86827a", "#5f6746", "#514f48", 0.06, "Rock")
    FR = N.Bank()
    P2 = N.Bank()                     # things lying ON the water (excluded from the reflection pass)
    for (x, y, r) in ((-5.2, 2.2, 0.5), (-4.5, 2.6, 0.28), (-7.3, 3.7, 0.65),
                      (6.3, 4.2, 0.55), (8.2, 5.7, 0.38), (-12.4, 16.5, 0.9)):
        rock(FR[rockm], None, rnd, x, y, r)
    # --- lily pads: clusters only at the sides (|x| >= 0.29 (y + 10.5) -> >= 150 px from the screen centre)
    padm = [pad_mat(c, "Pad") for c in ("#5f7a40", "#6b8446", "#56703b")]
    petal = N.lit("#ebe7da", n=3, ao=0.3, spread=0.7, name="Petal")
    centre = N.lit("#d6b24a", n=3, ao=0.2, name="LilyC")
    flowers = 0
    pads_all = []
    for (cx, cy, n, sp, fl) in ((-6.0, 7.0, 6, 1.1, 1), (7.0, 9.5, 5, 1.1, 0), (-8.5, 16.0, 7, 1.7, 0),
                                (10.5, 20.0, 6, 1.7, 1), (-12.0, 27.0, 4, 1.4, 0), (-15.0, 32.0, 6, 2.0, 0),
                                (17.0, 42.0, 4, 1.6, 0)):
        pads = []
        for i in range(n):
            for _t in range(30):
                x = cx + rnd.gauss(0, sp * 0.55)
                y = cy + rnd.gauss(0, sp * 0.4)
                r = rnd.uniform(0.15, 0.6)
                if not pad_x_ok(x - math.copysign(r, x), y):
                    continue
                # allow some overlap, but not pads fully on top of each other
                if all((x - a) ** 2 + (y - b) ** 2 > ((r + c) * 0.6) ** 2 for a, b, c in pads):
                    break
            else:
                continue
            pads.append((x, y, r))
            lily_pad(P2, rnd, x, y, r, rnd.choice(padm), notch=rnd.uniform(0.18, 0.32))
        pads_all += pads
        for k in range(min(fl, len(pads))):
            if flowers >= 2:
                break
            x, y, r = max(pads, key=lambda p: p[2])
            water_lily(P2, rnd, x + r * 0.15, y - r * 0.5, 1.0, petal, centre)
            flowers += 1
    # --- moored rowboat (left)
    hull = N.lit("#5e6f69", tex=0.08, tex_scale=5.0, ao=0.4, ao_dist=0.4, name="Hull")
    inner = N.lit("#8c775b", tex=0.06, tex_scale=6.0, tex_stretch=(1.0, 0.25, 1.0), ao=0.6, ao_dist=0.5,
                  name="Inner")
    rail = N.lit("#6a5845", ao=0.4, name="Rail")
    seat = N.lit("#9a8468", ao=0.5, name="Seat")
    rowboat(F, rnd, -9.2, 10.8, math.radians(72), hull=hull, inner=inner, rail=rail, seat=seat)
    F[postm].tube([(-8.4, 7.4, -1.0), (-8.4, 7.4, 0.9)], 0.09, 7)
    F[rope].tube([(-8.4, 7.4, 0.7), (-8.6, 8.3, 0.15), (-8.85, 9.1, 0.4)], 0.02, 4)
    objs["front"] = F.build("front", prefix="Near") + FR.build("front", smooth=False, prefix="Rock")
    objs["front"] += P2.build("front", smooth=False, prefix="Pad")
    objs["pad_cols"] = [c for m in padm for c in m["ramp"]]
    objs["rock_cols"] = list(rockm["cols"])
    return objs


def _plane(x0, y0, x1, y1):
    import bmesh
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in ((x0, y0, 0), (x1, y0, 0), (x1, y1, 0), (x0, y1, 0))]
    bm.faces.new(vs)
    return bm


# ============================================================================ render
def row_of(p):
    """Screen row (from the TOP) of a world point."""
    _, py, _ = P.project(p, STAND)
    return H / 2 - py


def hash_runs(n, seed, vals, run=(2, 4), probs=None):
    """1D array of length n made of runs (length run[0]..run[1]) of values drawn from vals."""
    rs = np.random.RandomState(seed)
    out = np.zeros(n, dtype=float)
    i = 0
    while i < n:
        L = rs.randint(run[0], run[1] + 1)
        out[i:i + L] = vals[rs.choice(len(vals), p=probs)]
        i += L
    return out


def band_index(rows, cols):
    """Water band (0 = horizon .. WATER_N-1 = pier) per screen pixel: boundaries spaced like perspective
    (7 bands across the visible water), each boundary wobbling +-1..3 rows along x (smooth noise)."""
    b = np.zeros((len(rows), len(cols)), int)
    R = rows[:, None].astype(np.float64)
    for k in range(1, WATER_N):
        rk = HOR + (VIS_BOT - HOR) * (k / WATER_N) ** (1 / 0.8)
        amp = 1.0 + 2.0 * k / WATER_N
        nz = N.fbm2(cols / 38.0 + k * 5.3, np.full(cols.shape, k * 2.7), 2, 31 + k)
        off = np.round(amp * (2 * nz - 1) * 1.6)
        b += (R >= rk + off[None, :]).astype(int)
    return b


def compose_water(back, refl):
    """Top-down arrays. Re-bands the water in screen space, bakes the rippled far-shore reflection, a
    continuous sunlit waterline and sparse short ripple dashes (hashed per row)."""
    out = back.copy()
    key = N.ckeys(back[..., :3])
    lit_keys = np.array([N.hexkey(c) for c in WCOLS])
    sh_keys = np.array([N.hexkey(c) for c in WSHAD])
    water = np.isin(key, lit_keys) | np.isin(key, sh_keys)
    shadow = np.isin(key, sh_keys)
    rows = np.arange(H)
    cols = np.arange(W).astype(np.float64)
    band = band_index(rows, cols)
    wl = np.array([N.hex2srgb(c) for c in WCOLS])
    ws = np.array([N.hex2srgb(c) for c in WSHAD])

    def band_rgb(bi, sh):
        bi = np.clip(bi, 0, WATER_N - 1)
        return np.where(sh[..., None], ws[bi], wl[bi])
    base = band_rgb(band, shadow)
    out[water, :3] = base[water]
    lab_w = N.arr_lab(out[..., :3])
    R = rows[:, None].astype(np.float64) * np.ones((1, W))
    u = np.clip((R - HOR) / (VIS_BOT - HOR), 0, 1)
    # --- reflection: per-row offsets in {-1, 0, +1} (runs of 2-4 rows), horizontal breaks
    off = hash_runs(H, 11, [-1, 0, 1], (2, 4), [0.3, 0.4, 0.3]).astype(int)
    off[rows < HOR + 12] = 0
    xs = np.clip(cols.astype(int)[None, :] + off[:, None], 0, W - 1)
    rr = refl[rows[:, None], xs]
    ra = rr[..., 3] > 0.5
    brk = N.fbm2(cols[None, :] / 9.0, R * 0.7, 2, 21)
    thr = 0.8 - 0.22 * np.clip(u * 3, 0, 1)
    keep = ra & (brk < thr) & water
    lab_r = N.arr_lab(rr[..., :3])
    lab_r[..., 0] *= 0.88
    lab_r[..., 1:] *= 0.75
    alpha = np.where(u < 0.1, 0.7, 0.5)
    lab = lab_w * (1 - alpha[..., None]) + lab_r * alpha[..., None]
    mixed = N.lab_arr(lab)
    out[keep, :3] = mixed[keep]
    # ripple breaks inside the reflection: one band lighter (sky catching the ripple)
    gap = ra & (~keep) & water & (brk > thr + 0.05)
    lighter = band_rgb(band - 1, shadow)
    out[gap, :3] = lighter[gap]
    # --- continuous sunlit waterline under the far bank (~85% coverage)
    top_edge = np.zeros_like(water)
    top_edge[1:] = (~water[:-1]) & water[1:]
    shore_zone = (R < row_of((0, SHORE_Y - 15, 0))) & (R > HOR)
    cov = hash_runs(W, 5, [1, 1, 1, 1, 1, 0], (3, 9)).astype(bool)
    wline = top_edge & shore_zone & cov[None, :]
    out[wline, :3] = N.hex2srgb(WLINE)
    # --- sparse ripple dashes, one band step lighter/darker, hashed per row
    used = keep | gap | wline
    dash_mask = np.zeros_like(water)
    for r in range(int(HOR) + 6, H):
        rs = np.random.RandomState(1000 + r)
        uu = float(np.clip((r - HOR) / (VIS_BOT - HOR), 0, 1))
        lmin, lmax = 1 + int(round(uu)), 3 + int(round(3 * uu))
        wr = np.where(water[r] & ~used[r])[0]
        if len(wr) == 0:
            continue
        nd = rs.poisson(0.015 * len(wr) / ((lmin + lmax) / 2))
        for _ in range(nd):
            c = int(wr[rs.randint(len(wr))])
            L = rs.randint(lmin, lmax + 1)
            if 150 < r < 300 and abs(c + L / 2 - W / 2) < 120:
                continue
            seg = slice(c, min(W, c + L))
            if not (water[r, seg].all() and not used[r, seg].any()):
                continue
            if dash_mask[max(r - 1, 0), max(c - 1, 0):c + L + 1].any():
                continue
            d = -1 if rs.rand() < 0.65 else 1
            bi = np.clip(band[r, seg] + d, 0, WATER_N - 1)
            col = np.where(shadow[r, seg][:, None], ws[bi], wl[bi])
            if d < 0 and band[r, c] == 0:
                col = np.tile(N.hex2srgb(WLINE), (len(bi), 1))
            out[r, seg, :3] = col
            dash_mask[r, seg] = True
    return out, water


def render_all():
    sc = N.new_scene()
    sun = N.add_sun()
    N.set_world()
    rnd = random.Random(20260928)
    objs = build(rnd)
    P.setup_camera(STAND)
    sc.render.film_transparent = False
    allmesh = [o for o in sc.objects if o.type == "MESH"]
    front = [o for o in allmesh if o.get("layer") == "front"]
    back = [o for o in allmesh if o.get("layer") != "front"]
    print("NAT objects", len(allmesh), "front", len(front))
    # ---- back layer: front objects invisible to the camera but still cast shadows on the water
    for o in front:
        o.visible_camera = False
    raw = N.render_ss("lake_back_ss.png", SS, W, H)
    back_arr, _ = N.mode_down(raw, SS, cover=0.0)
    back_arr[..., 3] = 1.0
    # ---- reflection pass: mirror everything but sky/water/on-water things, mirror sun + ambient
    for o in front:
        o.visible_camera = True
    hide = objs["sky"] + objs["water"] + [o for o in allmesh if o.name.startswith("Pad")] + \
        [o for o in allmesh if o.name in ("Mount", "Hills")]
    for o in hide:
        o.hide_render = True
    mirror = Matrix.Scale(-1, 4, (0, 0, 1))
    saved = {}
    for o in allmesh:
        if o in hide:
            continue
        saved[o.name] = o.matrix_world.copy()
        o.matrix_world = mirror @ o.matrix_world
    sun.rotation_euler = Vector((N.SUN_DIR.x, N.SUN_DIR.y, -N.SUN_DIR.z)).to_track_quat("Z", "Y").to_euler()
    N.set_world(flip=True)
    sc.render.film_transparent = True
    bpy.context.view_layer.update()
    raw = N.render_ss("lake_refl_ss.png", SS, W, H)
    refl, _ = N.mode_down(raw, SS, cover=0.4)
    for o in allmesh:
        if o.name in saved:
            o.matrix_world = saved[o.name]
    sun.rotation_euler = N.SUN_DIR.to_track_quat("Z", "Y").to_euler()
    N.set_world()
    for o in hide:
        o.hide_render = False
    # ---- front layer
    for o in back:
        o.hide_render = True
    for o in front:
        o.hide_render = False
    bpy.context.view_layer.update()
    raw = N.render_ss("lake_front_ss.png", SS, W, H)
    front_arr, _ = N.mode_down(raw, SS, cover=0.45)
    # ---- post (work top-down)
    bk = back_arr[::-1].copy()
    rf = refl[::-1].copy()
    key = N.ckeys(bk[..., :3])
    wkeys = np.array([N.hexkey(c) for c in WCOLS + WSHAD])
    land = ~np.isin(key, wkeys)
    bk = N.despeckle(bk, mask=land)
    bk, water = compose_water(bk, rf)
    n0 = N.count_colours(bk)
    bk = N.quantize(bk, 64, keep=WCOLS + WSHAD + PAL["sky"] + [WLINE])
    print("NAT back colours", n0, "->", N.count_colours(bk), "crop",
          N.count_colours(bk[VIS_TOP:VIS_BOT, VIS_L:VIS_R]))
    fr = front_arr[::-1].copy()
    fr = N.despeckle(fr)
    # 1px shadow on the water under each pad's far/right edge (sun from the left)
    fk = N.ckeys(fr[..., :3])
    pad = np.isin(fk, np.array([N.hexkey(c) for c in objs["pad_cols"]])) & (fr[..., 3] > 0.5)
    sh = np.zeros_like(pad)
    sh[:-1, 1:] = pad[1:, :-1]            # pixel above-right of a pad pixel
    sh &= fr[..., 3] < 0.5
    fr[sh, :3] = N.hex2srgb("#1c2f36")
    fr[sh, 3] = 0.45
    # 1px light ripple line where each rock meets the water (under its bottom edge, 1px wider each side)
    rock_px = np.isin(fk, np.array([N.hexkey(c) for c in objs["rock_cols"]])) & (fr[..., 3] > 0.5)
    empty = fr[..., 3] < 0.5
    bottom = np.zeros_like(rock_px)
    bottom[1:] = rock_px[:-1] & empty[1:]
    rip = (bottom | np.roll(bottom, 1, 1) | np.roll(bottom, -1, 1)) & empty
    rb = band_index(np.arange(H), np.arange(W).astype(np.float64))
    wl = np.array([N.hex2srgb(c) for c in WCOLS])
    fr[rip, :3] = wl[np.clip(rb - 2, 0, WATER_N - 1)][rip]
    fr[rip, 3] = 1.0
    n1 = N.count_colours(fr)
    fr = N.quantize(fr, 56, mask=(fr[..., 3] > 0.99))
    print("NAT front colours", n1, "->", N.count_colours(fr))
    C.save_pixels(bk[::-1], os.path.join(N.OUT, "lake_back.png"))
    C.save_pixels(fr[::-1], os.path.join(N.OUT, "lake_front.png"))
    C.save_pixels(refl, os.path.join(N.WORK, "lake_refl.png"))
    layout = dict(id="lake", standH=STAND, widthPx=W, heightPx=H, ppu=P.PPU, camBack=P.CAM_BACK, camUp=P.CAM_UP,
                  pitch=P.PITCH, focalPx=P.F_PX, depthZ=[0, 8, 20, 40, 140, 176], depthV=[1.2, 3, 6, 8, 6, 1],
                  mode="shore", zNear=1.2, zFar=176, xLim=90, waterTint=WCOLS[3], waterDeep="#16262b",
                  ambient="day", clouds=True, birds=True, style="natural")
    with open(os.path.join(N.OUT, "lake.json"), "w", encoding="utf-8") as f:
        json.dump(layout, f, indent=1)
    print("NAT lake done")


if __name__ == "__main__":
    render_all()
