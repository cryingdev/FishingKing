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
from nat_core import C, P, bpy, np, Vector, Matrix  # noqa: E402

STAND = 1.0
SS = 3
W, H = P.W, P.H
SHORE_Y = 180.0

# ----------------------------------------------------------------------------- palette (late morning, calm)
PAL = dict(
    sky=["#d6dcd6", "#cbd6d6", "#bccdd5", "#adc3d2", "#9fb9cf", "#93b0ca", "#88a7c4"],   # horizon -> top
    cloud=["#b3bec8", "#cbd3d8", "#e0e3e1", "#efede7"],
    mount="#6f817f", hills="#4c5e45",
    grass="#6a7444", grass2="#6a7043", mud="#6a5c49", stone="#a0967f",
    dec=["#5f7340", "#6c7c45", "#55683b", "#7a8550", "#687a4a"], willow="#7f8b52",
    con=["#3b5143", "#415846", "#384a3d"],
    bark="#584a3d", birch="#c3bdab",
    water_keys=[(0.0, "#a9bbbd"), (0.06, "#93a9b0"), (0.2, "#7892a0"), (0.42, "#617f8b"), (0.7, "#526f78"),
                (1.0, "#46626a")],
    wood=["#8e7c65", "#857159", "#9a8a73", "#7b6651", "#8a7862"],
    under="#3a3029", post="#6b5a48",
)


# ============================================================================ special materials
def sky_mat():
    m = bpy.data.materials.new("NatSky")
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["Window"])
    hor = 0.5 + (P.F_PX * math.tan(math.radians(P.PITCH))) / H     # horizon in window y (bottom-up)
    u = nb.math("DIVIDE", nb.math("SUBTRACT", s[1], hor), 1.0 - hor)
    u = nb.math("POWER", nb.math("MAXIMUM", u, 0.0), 0.8)
    n = len(PAL["sky"])
    f = nb.math("ADD", nb.math("MULTIPLY", u, n), nb.math("MULTIPLY", nb.math("SUBTRACT", N.bayer2(nb), 0.5), 0.6))
    f = nb.math("DIVIDE", f, n)
    col = nb.ramp(f, [(i / n, N.lin4(c)) for i, c in enumerate(PAL["sky"])])
    nb.output_emission(col, 1.0)
    return m


def water_cols(nbands=10):
    keys = PAL["water_keys"]
    out = []
    for i in range(nbands):
        u = (i + 0.5) / nbands
        for (u0, c0), (u1, c1) in zip(keys, keys[1:]):
            if u0 <= u <= u1:
                t = (u - u0) / (u1 - u0)
                lab = N.srgb2lab(N.hex2srgb(c0)) * (1 - t) + N.srgb2lab(N.hex2srgb(c1)) * t
                out.append(N.srgb2hex(N.lab2srgb(lab)))
                break
    return out


WATER_N = 10
WCOLS = water_cols(WATER_N)
WSHAD = [N.shade_hex(c, dl=-0.07, dc=0.9, cool=0.5) for c in WCOLS]
U_NEAR = 8.85            # horizontal camera distance to the water at the bottom image edge
U_GAMMA = 0.8


def water_u(D):
    """0 at the horizon -> 1 at the bottom edge (proportional to screen rows below the horizon)."""
    return np.clip(U_NEAR / np.maximum(D, 1e-3), 0, 1) ** U_GAMMA


def water_mat():
    m = bpy.data.materials.new("NatWater")
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    s = nb.sep(geo.outputs["Position"])
    D = nb.math("ADD", s[1], P.CAM_BACK)
    u = nb.math("POWER", nb.math("MINIMUM", nb.math("DIVIDE", U_NEAR, nb.math("MAXIMUM", D, 0.5)), 1.0), U_GAMMA)
    # wind streaks: long in x, thin in y; fade out towards the horizon (they would alias)
    nz = N._noise(nb, 1.0, "world", detail=1.0, stretch=(0.09, 0.8, 1.0))
    streak = nb.math("SUBTRACT", nb.math("GREATER_THAN", nz, 0.64), nb.math("LESS_THAN", nz, 0.34))
    fade = nb.math("MINIMUM", nb.math("MULTIPLY", u, 3.0), 1.0)
    f = nb.math("MULTIPLY", u, float(WATER_N))
    f = nb.math("ADD", f, nb.math("MULTIPLY", nb.math("MULTIPLY", streak, fade), 0.55))
    f = nb.math("ADD", f, nb.math("MULTIPLY", nb.math("SUBTRACT", N.bayer2(nb), 0.5), 0.55))
    f = nb.math("DIVIDE", f, float(WATER_N))
    col = nb.ramp(f, [(i / WATER_N, N.lin4(c)) for i, c in enumerate(WCOLS)])
    sh = nb.ramp(f, [(i / WATER_N, N.lin4(c)) for i, c in enumerate(WSHAD)])
    L = N._light_value(nb, ao=0.0)
    mask = nb.math("LESS_THAN", L, 0.6)
    col = nb.mix(mask, col, sh)
    nb.output_emission(col, 1.0)
    return m


def bank_mat():
    """Grass bank with a mud/stone lip at the waterline (mirror-safe: uses |Z|)."""
    m = bpy.data.materials.new("NatBank")
    nb = C.NB(m)
    L = N._light_value(nb, ao=0.5, ao_dist=3.0)
    nz = N._noise(nb, 0.9, "world", detail=2.0)
    L = nb.math("MULTIPLY_ADD", nz, 0.34, nb.math("SUBTRACT", L, 0.42))
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
    nz = N._noise(nb, 3.0, "world", detail=2.0, stretch=(1.0, 1.0, 0.25))
    L = nb.math("MULTIPLY_ADD", nz, 0.24, nb.math("SUBTRACT", L, 0.12))
    geo = nb.node("ShaderNodeNewGeometry")
    z = nb.math("ABSOLUTE", nb.sep(geo.outputs["Position"])[2])
    wet = nb.math("LESS_THAN", z, 0.38)
    a = N.ramp_colours(nb, L, N.gen_ramp(PAL["post"], 5))
    b = N.ramp_colours(nb, L, N.gen_ramp("#4d5040", 5))
    nb.output_emission(nb.mix(wet, a, b), 1.0)
    return m


def wet_mat(base, wet, zwet, name):
    """Textured flat-ish material with a darker wet band near the waterline (|Z| < zwet)."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    L = N._light_value(nb, ao=0.55, ao_dist=0.5)
    nz = N._noise(nb, 3.5, "world", detail=2.0)
    L = nb.math("MULTIPLY_ADD", nz, 0.3, nb.math("SUBTRACT", L, 0.15))
    geo = nb.node("ShaderNodeNewGeometry")
    z = nb.math("ABSOLUTE", nb.sep(geo.outputs["Position"])[2])
    w = nb.math("LESS_THAN", z, zwet)
    a = N.ramp_colours(nb, L, N.gen_ramp(base, 5))
    b = N.ramp_colours(nb, L, N.gen_ramp(wet, 5))
    nb.output_emission(nb.mix(w, a, b), 1.0)
    return m


# ============================================================================ builders
def deciduous(B, rnd, x, y, z, h, leaf, bark, kind=None):
    kind = kind or rnd.choice(["round", "oval", "wide", "irregular", "irregular", "willow"])
    th = h * rnd.uniform(0.32, 0.45)
    r0 = max(h * 0.03, 0.18)
    lean = rnd.uniform(-0.08, 0.08)
    B[bark].tube([(x, y, z - 0.5), (x + lean * th * 0.5, y, z + th * 0.5), (x + lean * th, y, z + th)],
                 [r0 * 1.25, r0, r0 * 0.7], 5)
    rx, rz = {"round": (0.33, 0.31), "oval": (0.22, 0.36), "wide": (0.42, 0.27), "irregular": (0.3, 0.3),
              "willow": (0.36, 0.3)}[kind]
    rx *= h
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
    # a few visible limbs under the crown
    for _ in range(rnd.randint(1, 3)):
        a = rnd.uniform(-1, 1)
        B[bark].tube([(cx, y, z + th * 0.8), (cx + a * rx * 0.55, y - 0.3, cz - rz * 0.25)], [r0 * 0.55, r0 * 0.3], 4)


def conifer(B, rnd, x, y, z, h, leaf, bark):
    B[bark].tube([(x, y, z - 0.5), (x, y, z + h * 0.92)], [max(h * 0.022, 0.15), 0.06], 4)
    n = rnd.randint(7, 10)
    rb = h * rnd.uniform(0.17, 0.23)
    for i in range(n):
        t = i / (n - 1)
        zi = z + h * (0.1 + 0.76 * t)
        ri = rb * (1 - t) ** 0.85 + h * 0.03
        B[leaf].cone((x + rnd.uniform(-0.08, 0.08) * ri, y, zi), ri, h * 0.19, segs=12, jag=0.3, droop=0.4,
                     rnd=rnd, cap=False)
    B[leaf].cone((x, y, z + h * 0.84), h * 0.035, h * 0.16, segs=5, cap=False)


def rock_lump(acc, rnd, x, y, r):
    """Irregular boulder: low-frequency lumps + flattened top, half sunk into the water."""
    vs = acc.ico((x, y, -0.18 * r), r, 2, (1.35, 1.05, 0.72), 0.0, None, rot=rnd.uniform(0, 3))
    ph = [rnd.uniform(0, 6.3) for _ in range(6)]
    for v in vs:
        d = v.co - Vector((x, y, -0.18 * r))
        k = 1.0 + 0.16 * math.sin(d.x * 5.1 / r + ph[0]) * math.sin(d.y * 4.3 / r + ph[1])             + 0.1 * math.sin(d.z * 7.0 / r + ph[2]) + rnd.uniform(-0.03, 0.03)
        d = d * k
        if d.z > 0.45 * r:
            d.z = 0.45 * r + (d.z - 0.45 * r) * 0.4
        v.co = Vector((x, y, -0.18 * r)) + d


def bush(B, rnd, x, y, z, h, leaf):
    for _ in range(rnd.randint(5, 9)):
        B[leaf].ico((x + rnd.uniform(-0.6, 0.6) * h, y + rnd.uniform(-0.3, 0.3) * h, z + rnd.uniform(0.25, 0.6) * h),
                    h * rnd.uniform(0.3, 0.45), 1, (1, 1, 0.8), 0.2, rnd)


def zc_of(y, z=0.0):
    return P.project((0.0, y, z), STAND)[2]


def px_m(y, z=0.0):
    """metres per output pixel at distance y."""
    return zc_of(y, z) / P.F_PX


def reed_clump(B, rnd, cx, cy, n, spread, hmin, hmax, mats, weights, cattail=0.3, head=None, stalk=None,
               zb=-0.25):
    pm = px_m(cy)
    wmin = pm * 1.2
    for _ in range(n):
        x = cx + max(-spread, min(spread, rnd.gauss(0, spread * 0.5)))
        y = cy + max(-spread * 0.7, min(spread * 0.7, rnd.gauss(0, spread * 0.35)))
        h = rnd.uniform(hmin, hmax)
        dx = (x - cx) / max(spread, 1e-3) * 0.5 + rnd.uniform(-0.45, 0.45)
        dy = rnd.uniform(-0.2, 0.2)
        lean = rnd.uniform(0.08, 0.38) * h
        droop = rnd.uniform(0.0, 0.35) * h if rnd.random() < 0.45 else 0.0
        pts = []
        for k in range(6):
            t = k / 5
            pts.append((x + dx * lean * t ** 1.5, y + dy * lean * t ** 1.5, zb + h * t - droop * t ** 3))
        w0 = max(wmin, rnd.uniform(0.03, 0.05))
        mat = rnd.choices(mats, weights)[0]
        B[mat].ribbon(pts, [w0 * (1 - 0.9 * (k / 5) ** 1.3) for k in range(6)], side=(1.0, rnd.uniform(-0.6, 0.6), 0))
    if head is not None:
        for _ in range(int(n * cattail)):
            x = cx + max(-spread * 0.6, min(spread * 0.6, rnd.gauss(0, spread * 0.4)))
            y = cy + max(-spread * 0.4, min(spread * 0.4, rnd.gauss(0, spread * 0.3)))
            h = rnd.uniform(hmin * 1.0, hmax * 1.08)
            lx = rnd.uniform(-0.12, 0.12) * h
            B[stalk].tube([(x, y, zb), (x + lx * 0.5, y, zb + h * 0.5), (x + lx, y, zb + h)],
                          max(pm * 0.55, 0.012), 4)
            hz = zb + h * rnd.uniform(0.72, 0.82)
            hx = x + lx * (hz - zb) / h
            B[head].uvsphere((hx, y - 0.01, hz), max(pm * 1.0, 0.035), (1, 1, 3.2), seg=8, rings=6)


def lily_pad(B, rnd, x, y, r, mat, rim=None):
    bm = B[mat].bm
    n = 18
    a0 = rnd.uniform(0, 2 * math.pi)
    c = bm.verts.new((x, y, 0.025))
    ring = []
    for k in range(n + 1):
        a = a0 + 0.22 + (2 * math.pi - 0.44) * k / n
        rr = r * rnd.uniform(0.95, 1.03)
        up = 0.02 + (0.04 if rnd.random() < 0.25 else 0.0)
        ring.append(bm.verts.new((x + math.cos(a) * rr, y + math.sin(a) * rr, 0.025 + up)))
    for k in range(n):
        bm.faces.new((c, ring[k], ring[k + 1]))
    if rim is not None:
        # curled reddish rim on part of the edge
        k0 = rnd.randint(0, n - 7)
        pts = []
        for k in range(k0, k0 + 6):
            a = a0 + 0.22 + (2 * math.pi - 0.44) * k / n
            pts.append((x + math.cos(a) * r * 1.01, y + math.sin(a) * r * 1.01, 0.05))
        B[rim].tube(pts, max(px_m(y) * 0.5, 0.018), 4)


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
        sheer = 0.32 + 0.12 * t ** 3                 # gunwale height above water, rising to the bow
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
        if shrink == 0.0:        # transom
            bm.faces.new(rings[0][::-1])
    # gunwale rails
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
    # oars resting inside
    B[rail].tube([T(-1.2, -0.25, 0.26), T(1.1, 0.12, 0.3)], 0.025, 5)
    B[seat].box(T(1.25, 0.15, 0.3), (0.45, 0.1, 0.02), rot=(0, 0, yaw + 0.15))


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
    # flat-bottomed, elongated fair-weather cumulus: many small clumps along a band, bases clipped flat
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
    mm = N.lit(PAL["mount"], tex=0.12, tex_scale=0.012, ao=0.0, name="Mount")
    acc = N.Acc()
    xs = [-2800 + 35 * i for i in range(161)]
    ys = [1900 + 60 * j for j in range(14)]

    def mz(x, y):
        r = 1.0 - abs(N.fbm2(x * 0.0012, y * 0.002, 4, 3) * 2 - 1)      # ridged
        base = 40 + 260 * r ** 1.6 * (0.6 + 0.4 * math.sin(x * 0.0009 + 1.3))
        return float(base * min(1.0, (y - 1900) / 250.0 + 0.15))
    acc.quad_strip_grid(xs, ys, mz)
    ob = acc.build("Mount", mm, smooth=True, layer="back", shadow=False)
    # --- far hills with forest canopy
    hm = N.lit(PAL["hills"], tex=0.2, tex_scale=0.25, ao=0.4, ao_dist=15.0, name="Hills")
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
    # --- forest
    decm = [N.lit(c, tex=0.2, tex_scale=1.1, ao=0.55, ao_dist=2.5, name="Leaf") for c in PAL["dec"]]
    wilm = N.lit(PAL["willow"], tex=0.2, tex_scale=1.3, ao=0.5, ao_dist=2.5, name="Willow")
    conm = [N.lit(c, tex=0.18, tex_scale=1.4, ao=0.5, ao_dist=2.5, spread=1.1, name="Conif") for c in PAL["con"]]
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
            h = rnd.uniform(hmin, hmax)
            r = h * 0.22
            if not free(x, y, r * 0.8):
                continue
            placed.append((x, y, r * 0.8))
            z = bz(x, y) - 0.2
            if rnd.random() < pcon:
                conifer(B, rnd, x, y, z, h * 1.25, rnd.choice(conm), bark)
            else:
                kind = rnd.choice(kinds) if kinds else None
                trunk = birch if rnd.random() < 0.12 else bark
                leaf = wilm if kind == "willow" else rnd.choice(decm)
                deciduous(B, rnd, x, y, z, h, leaf, trunk, kind)
            n -= 1
    # boathouse clearing
    placed.append((52, SHORE_Y + 8, 9))
    plant(-150, 150, SHORE_Y + 2, SHORE_Y + 14, 52, 5.0, 9.5, 0.25, ["round", "irregular", "willow", "wide", "oval"])
    plant(-170, 170, SHORE_Y + 12, SHORE_Y + 50, 110, 6.5, 10.5, 0.45)
    plant(-230, 230, SHORE_Y + 45, SHORE_Y + 150, 170, 8.0, 12.0, 0.6)
    for _ in range(12):      # a few tall emergent trees breaking the canopy line
        x = rnd.uniform(-130, 130)
        y = rnd.uniform(SHORE_Y + 8, SHORE_Y + 40)
        if y > edge(x) + 4 and abs(x - 52) > 10:
            conifer(B, rnd, x, y, bz(x, y) - 0.2, rnd.uniform(13, 17), rnd.choice(conm), bark)
    for _ in range(110):     # shrubs + reeds along the bank
        x = rnd.uniform(-150, 150)
        e = edge(x)
        if abs(x - 52) < 8:
            continue
        bush(B, rnd, x, e + rnd.uniform(0.3, 3.0), bz(x, e + 1.5), rnd.uniform(1.2, 3.4), rnd.choice(decm))
    # boathouse on the far shore
    wall = N.lit("#857461", tex=0.1, tex_scale=2.0, ao=0.5, name="Wall")
    roof = N.lit("#5d5f59", ao=0.4, name="Roof")
    dark = N.lit("#3a332d", ao=0.2, name="Dark")
    bx, by = 52.0, edge(52.0) + 1.0
    B[wall].box((bx, by + 2.5, 1.9), (7.0, 5.0, 3.6))
    B[dark].box((bx, by - 0.02, 1.2), (3.2, 0.1, 2.2))
    for sgn in (-1, 1):
        B[roof].box((bx + sgn * 1.9, by + 2.5, 4.35), (4.3, 5.8, 0.18), rot=(0, sgn * math.radians(-32), 0))
    B[wall].box((bx - 5.5, by - 2.5, 0.25), (1.6, 7.0, 0.15))     # small dock
    # --- water
    wat = C.mesh_object("Water", _plane(-6000, -40, 6000, 3000), water_mat())
    wat["layer"] = "back"
    wat.visible_shadow = False
    objs["water"].append(wat)
    objs["back"] = B.build("back", prefix="Far")

    # =================================================================== FRONT (near play area)
    wood = [N.lit(c, tex=0.13, tex_scale=5.0, tex_stretch=(0.18, 2.2, 2.2), ao=0.5, ao_dist=0.3,
                  name="Plank") for c in PAL["wood"]]
    under = N.lit(PAL["under"], ao=0.3, name="Under")
    postm = post_mat()
    r2 = random.Random(3)
    top = STAND
    for x in (-1.08, 0.0, 1.08):
        F[under].box((x, -9.0, top - 0.2), (0.12, 20.2, 0.2))
    F[under].box((0, -9.0, top - 0.32), (2.5, 20.2, 0.03))
    y = 1.12
    while y > -19.5:
        w = r2.uniform(0.18, 0.23)
        x0 = -1.3 + r2.uniform(-0.07, 0.03)
        x1 = 1.3 + r2.uniform(-0.03, 0.07)
        m = r2.choices(wood, [3, 3, 2, 1, 3])[0]
        F[m].box(((x0 + x1) / 2, y - w / 2, top - 0.035 + r2.uniform(-0.012, 0.008)), (x1 - x0, w - 0.022, 0.07),
                 rot=(r2.uniform(-0.012, 0.012), 0, r2.uniform(-0.01, 0.01)))
        y -= w
    for (px, py, ph) in ((-1.4, 0.95, 0.62), (1.4, 0.95, 0.5), (-1.42, -2.8, 0.05), (1.42, -2.8, 0.05),
                         (-1.42, -6.6, 0.05), (1.42, -6.6, 0.05)):
        bm = F[postm]
        bm.tube([(px, py, -1.2), (px, py, top + ph - 0.04)], 0.12, 9)
        bm.uvsphere((px, py, top + ph - 0.04), 0.12, (1, 1, 0.35), seg=9, rings=4)
    rope = N.lit("#b0a07a", tex=0.15, tex_scale=40.0, ao=0.4, name="Rope")
    for k in range(4):
        zz = top + 0.2 + k * 0.055
        F[rope].tube([(1.4 + 0.135 * math.cos(a), 0.95 + 0.135 * math.sin(a), zz + 0.01 * math.sin(a * 2))
                      for a in [i * 2 * math.pi / 12 for i in range(13)]], 0.028, 5)
    F[rope].tube([(1.4, 0.82, top + 0.24), (1.47, 0.72, top + 0.05), (1.52, 0.8, top + 0.2)], 0.024, 5)
    # tackle box + bucket
    tb = N.lit("#5a6a52", ao=0.5, ao_dist=0.3, name="Tackle")
    tb2 = N.lit("#687a5e", ao=0.5, ao_dist=0.3, name="Tackle2")
    F[tb].box((-0.82, -0.32, top + 0.11), (0.46, 0.26, 0.22), rot=(0, 0, 0.12))
    F[tb2].box((-0.82, -0.32, top + 0.235), (0.47, 0.27, 0.04), rot=(0, 0, 0.12))
    F[dark].tube([(-0.95, -0.34, top + 0.26), (-0.95, -0.34, top + 0.31), (-0.69, -0.3, top + 0.31),
                  (-0.69, -0.3, top + 0.26)], 0.012, 4)
    galv = N.lit("#9aa1a2", ao=0.5, ao_dist=0.3, spread=0.8, name="Galv")
    F[galv].tube([(0.88, -1.05, top), (0.88, -1.05, top + 0.36)], [0.16, 0.19], 12)
    F[dark].uvsphere((0.88, -1.05, top + 0.36), 0.17, (1, 1, 0.08), seg=12, rings=4)
    # --- reeds, sedges, rocks
    reed_mats = [N.lit(c, tex=0.1, tex_scale=3.0, ao=0.45, ao_dist=0.5, name="Reed")
                 for c in ("#6c7a3e", "#7d8956", "#55643a", "#a39a63", "#8a8a52")]
    head = N.lit("#5a4332", ao=0.3, name="Cattail")
    stalk = N.lit("#7b7f4e", ao=0.3, name="Stalk")
    rockm = wet_mat("#807a6c", "#4e4c44", 0.1, "Rock")
    mossm = wet_mat("#6f7352", "#4b4c3c", 0.1, "Moss")
    FR = N.Bank()
    for (cx, cy, n, sp, h0, h1) in ((-6.2, 2.8, 90, 1.6, 0.9, 2.0), (-4.6, 4.2, 30, 0.8, 0.6, 1.3),
                                    (7.0, 5.0, 80, 1.6, 0.9, 2.1), (8.8, 7.5, 40, 1.2, 0.7, 1.5),
                                    (-13.5, 18, 70, 2.4, 1.2, 2.4), (15.5, 30, 70, 2.6, 1.3, 2.6),
                                    (-19, 40, 50, 3.0, 1.2, 2.3)):
        reed_clump(F, rnd, cx, cy, n, sp, h0, h1, reed_mats, [3, 2, 2, 1.3, 1.2], 0.25, head, stalk)
    for (x, y, r, m) in ((-5.2, 2.2, 0.5, rockm), (-4.5, 2.6, 0.28, rockm), (-7.3, 3.7, 0.65, mossm),
                         (6.3, 4.2, 0.55, mossm), (8.2, 5.7, 0.38, rockm), (-12.4, 16.5, 0.9, rockm)):
        rock_lump(FR[m], rnd, x, y, r)
    # --- lily pads (clusters near the reeds / sides, open water in the middle)
    padm = [N.lit(c, tex=0.14, tex_scale=5.0, ao=0.35, ao_dist=0.3, bias=-0.24, name="Pad")
            for c in ("#5f7a40", "#6b8446", "#56703b", "#7b884c")]
    rimm = N.lit("#7a4c3d", ao=0.2, name="PadRim")
    P2 = N.Bank()
    petal = N.lit("#ebe7da", ao=0.3, spread=0.7, name="Petal")
    centre = N.lit("#d6b24a", ao=0.2, name="LilyC")
    for (cx, cy, n, sp, rmin, rmax, fl) in ((-4.0, 7.0, 6, 1.2, 0.28, 0.5, 1), (5.2, 9.5, 5, 1.1, 0.3, 0.5, 0),
                                            (-8.5, 16.0, 7, 1.8, 0.35, 0.6, 1), (10.5, 20.0, 6, 1.8, 0.35, 0.6, 1),
                                            (-2.0, 27.0, 3, 1.0, 0.35, 0.55, 0), (-15.0, 32.0, 6, 2.2, 0.4, 0.7, 0),
                                            (6.0, 42.0, 4, 1.6, 0.45, 0.7, 0)):
        pads = []
        for _ in range(n):
            for _t in range(20):
                x = cx + rnd.gauss(0, sp * 0.6)
                y = cy + rnd.gauss(0, sp * 0.45)
                r = rnd.uniform(rmin, rmax)
                if all((x - a) ** 2 + (y - b) ** 2 > (r + c) ** 2 * 0.8 for a, b, c in pads):
                    break
            pads.append((x, y, r))
            lily_pad(P2, rnd, x, y, r, rnd.choice(padm), rimm if rnd.random() < 0.3 else None)
        for k in range(fl):
            x, y, r = pads[k]
            water_lily(P2, rnd, x + r * 0.2, y - r * 0.9, 1.0, petal, centre)
    # --- moored rowboat (left)
    hull = N.lit("#5e6f69", tex=0.16, tex_scale=5.0, ao=0.4, ao_dist=0.4, name="Hull")
    inner = N.lit("#8c775b", tex=0.12, tex_scale=6.0, tex_stretch=(1.0, 0.25, 1.0), ao=0.6, ao_dist=0.5,
                  name="Inner")
    rail = N.lit("#6a5845", ao=0.4, name="Rail")
    seat = N.lit("#9a8468", ao=0.5, name="Seat")
    rowboat(F, rnd, -9.2, 10.8, math.radians(72), hull=hull, inner=inner, rail=rail, seat=seat)
    F[postm].tube([(-8.4, 7.4, -1.0), (-8.4, 7.4, 0.9)], 0.09, 7)
    F[rope].tube([(-8.4, 7.4, 0.7), (-8.6, 8.3, 0.15), (-8.85, 9.1, 0.4)], 0.02, 4)
    objs["front"] = F.build("front", prefix="Near") + FR.build("front", smooth=True, prefix="Rock")
    objs["front"] += P2.build("front", prefix="Pad")
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


def compose_water(back, refl, rnd):
    """Top-down arrays. Bakes far-shore/prop reflections (rippled, broken) and sparse glints into the water."""
    out = back.copy()
    q = np.round(back[..., :3] * 255).astype(np.int64)
    key = (q[..., 0] << 16) | (q[..., 1] << 8) | q[..., 2]
    wkeys = []
    for c in WCOLS + WSHAD:
        v = np.round(N.hex2srgb(c) * 255).astype(np.int64)
        wkeys.append((v[0] << 16) | (v[1] << 8) | v[2])
    water = np.isin(key, np.array(wkeys))
    rows = np.arange(H)[:, None].astype(np.float64)
    cols = np.arange(W)[None, :].astype(np.float64)
    hor = H / 2 - P.F_PX * math.tan(math.radians(P.PITCH))
    # screen-row -> u (0 horizon .. 1 bottom), roughly linear in rows below the horizon
    u = np.clip((rows - hor) / (H - hor), 0, 1) * np.ones((1, W))
    # --- reflection: ripple offsets per row + horizontal breaks
    amp = np.clip(u * 2.2, 0, 1.6)
    dx = np.round(amp * np.sin(rows * 1.9 + 0.7 * np.sin(rows * 0.37))).astype(int)
    xs = np.clip(cols.astype(int) + dx, 0, W - 1)
    rr = refl[np.arange(H)[:, None], xs]
    ra = rr[..., 3] > 0.5
    brk = N.fbm2(cols / 7.0, rows * 0.85, 2, 21)
    thr = 0.78 - 0.2 * np.clip(u * 3, 0, 1)
    keep = ra & (brk < thr) & water
    lab_r = N.arr_lab(rr[..., :3])
    lab_w = N.arr_lab(back[..., :3])
    lab_r[..., 0] = lab_r[..., 0] * 0.78
    lab_r[..., 1:] *= 0.75
    alpha = 0.72 - 0.35 * np.clip(u * 2.5, 0, 1)
    # quantise the mix to 3 levels so the reflection stays a few flat pixel colours
    alpha = np.round(alpha * 4) / 4
    lab = lab_w * (1 - alpha[..., None]) + lab_r * alpha[..., None]
    mixed = N.lab_arr(lab)
    out[keep, :3] = mixed[keep]
    # lighter ripple line where the reflection breaks (sky catching the ripple)
    gap = ra & (~keep) & water & (brk > thr + 0.05)
    lab_g = lab_w.copy()
    lab_g[..., 0] += 0.04
    out[gap, :3] = N.lab_arr(lab_g)[gap]
    # thin sunlit waterline under the far bank: first water pixel below a bank pixel
    above_bank = np.zeros_like(water)
    above_bank[1:] = (~water[:-1]) & water[1:]
    shore_zone = (rows < row_of((0, SHORE_Y - 15, 0))) & (rows > hor)
    wl = above_bank & shore_zone & (np.random.RandomState(4).rand(H, W) < 0.55)
    lab_l = lab_w.copy()
    lab_l[..., 0] += 0.07
    out[wl, :3] = N.lab_arr(lab_l)[wl]
    # --- sparse glints (short horizontal dashes), fewer in the gameplay middle
    rs = np.random.RandomState(7)
    n = 0
    tries = 0
    while n < 70 and tries < 5000:
        tries += 1
        r = int(hor + 2 + (H - hor - 2) * rs.rand() ** 1.8)
        c = int(rs.rand() * W)
        uu = (r - hor) / (H - hor)
        mid = abs(c - W / 2) < 170 and 150 < r < 300
        if mid and rs.rand() < 0.75:
            continue
        L = 1 + int(rs.rand() * (1 + 4 * uu))
        seg = water[r, c:c + L]
        if L < 1 or not seg.all():
            continue
        lab_h = N.arr_lab(out[r, c:c + L, :3])
        lab_h[..., 0] += 0.09 if rs.rand() < 0.7 else 0.16
        lab_h[..., 1:] *= 0.7
        out[r, c:c + L, :3] = N.lab_arr(lab_h)
        n += 1
    return out


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
    # ---- reflection pass: mirror everything but sky/water about z=0, mirror sun + ambient
    for o in front:
        o.visible_camera = True
    hide = objs["sky"] + objs["water"] + [o for o in allmesh if o.name.startswith("Pad")] +         [o for o in allmesh if o.name in ("Mount", "Hills")]
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
    bk = compose_water(bk, rf, rnd)
    C.save_pixels(bk[::-1], os.path.join(N.OUT, "lake_back.png"))
    C.save_pixels(front_arr, os.path.join(N.OUT, "lake_front.png"))
    C.save_pixels(refl, os.path.join(N.WORK, "lake_refl.png"))
    layout = dict(id="lake", standH=STAND, widthPx=W, heightPx=H, ppu=P.PPU, camBack=P.CAM_BACK, camUp=P.CAM_UP,
                  pitch=P.PITCH, focalPx=P.F_PX, depthZ=[0, 8, 20, 40, 140, 176], depthV=[1.2, 3, 6, 8, 6, 1],
                  mode="shore", zNear=1.2, zFar=176, xLim=90, waterTint=WCOLS[5], waterDeep="#16262b",
                  ambient="day", clouds=True, birds=True, style="natural")
    with open(os.path.join(N.OUT, "lake.json"), "w", encoding="utf-8") as f:
        json.dump(layout, f, indent=1)
    print("NAT lake done")


if __name__ == "__main__":
    render_all()
