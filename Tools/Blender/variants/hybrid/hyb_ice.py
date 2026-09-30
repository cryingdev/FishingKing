"""
hybrid - the ice stage: frozen lake at blue hour (preset "ice"), retro16 craft + the kit's mood layer.

Back layer (opaque):
  * banded blue-hour sky from the preset (rose afterglow at the horizon -> lilac -> periwinkle), a low green
    aurora band kept inside the game crop (up <= ~24 px), the rose afterglow glow rings left of centre (no disc);
  * layered far silhouettes with aerial perspective: snowy range 2400 m (rose-lit snow on the afterglow side),
    snowy forested hills 700 m, hazy back conifer bands 190-260 m, the dark snowy spruce row on a snow bank
    at 160 m; the range / hills open into a valley and the near shore into a gap under the afterglow, so the
    glow reaches the far ice; rose rims on silhouette tops / left edges near the glow only;
  * the ice: depth bands of the preset (pale lilac far -> periwinkle near) joined by horizontal dash dithering,
    the far glazed ice mirrors the sky (glow rings + aurora, dash dithered) and faintly the far layers (exact
    mirror pass, strongly tinted towards the ice, break lines + dissolve); wind-blown snow patches, polished
    dark patches, wind streaks and thin cracks - all perspective-correct in world space, sparse in the calm
    central play area; soft cast shadows of the front props; a thin lilac mist on the far ice line;
  * THE HOLE: modelled in 3D and projected by the stage camera - a chopped slush funnel from the ice top
    (iceY) down to the water surface (y = 0) whose bottom ring is exactly the gameplay hole circle
    (holeX, holeZ, holeR at y = 0), so the dark open water is pixel-exact where Unity puts the float / line /
    splashes; chipped ice lumps around it. Open dark water only in the hole.
Front layer (transparent, solid objects only): red ice-fishing shanty with a warm lit window + stovepipe
(and a small far one), ice auger stuck in a drift right of the hole, yellow bucket with a snowy lid, slush
skimmer, wind-carved snow drifts framing the corners and sides. Cool key from above-left, pink rim on the
outer left/top silhouette, hue-shifted outlines (never ink).

Same camera (fk_persp.setup_camera(standH = 0.25)), 640x400, same gameplay layout as Data/stage_ice.json.
Outputs: _tmp/variants/hybrid/ice_back.png, ice_front.png, stage_ice.json (scratch: work/ice/)
Run: blender -b --python variants/hybrid/hyb_ice.py [-- back|front] [--period dawn|day|evening|night [--dry]]   (hyb_period.py)
"""
import os
import sys
import math
import json
import random
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import hyb_period as PER  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

SID = "ice"
# own scratch dir: render_exr() reads the module global WORK at call time, so parallel stage runs never collide
R.WORK = PER.work(SID, os.path.join(R.OUT, "work", SID))   # period runs: work/periods/ice_<p>/
os.makedirs(R.WORK, exist_ok=True)
with open(os.path.join(C.DATA, f"stage_{SID}.json"), encoding="utf-8") as _f:
    LAYOUT = json.load(_f)
STAND = float(LAYOUT["standH"])                      # 0.25
HX, HZ, HR = float(LAYOUT["holeX"]), float(LAYOUT["holeZ"]), float(LAYOUT["holeR"])
ICE_Y = float(LAYOUT["iceY"])                        # ice top (the gameplay water plane is y = 0)
W, H = P.W, P.H
G = R.grade_hex

_WB = R.PRESETS["ice"].water["bands"]
# waterTint = the play-area ice band (fish shadows are seen THROUGH that ice; splashes from the hole are pale
# slush of the same hue); waterDeep = the preset deep, also the darkest water painted in the hole.
# aurora: lowered so the whole band stays inside the game crop (crop top = 24.5 px above the horizon)
PR = PER.use_preset("ice", aurora=dict(up0=8, height=14, amp=2.5, amount=0.55), water=dict(tint=_WB[4]))

# ------------------------------------------------------------------ geometry anchors of the mood
GLOW_C, GLOW_R = R.sun_rc(PR)                   # rose afterglow centre (180, 91.5)
GAP_C = (140.0, 222.0)                          # gap in the near far shore under the afterglow
VAL_C = (128.0, 236.0)                          # valley in the hills / range under the afterglow
D_RANGE, D_HILL, D_BACK, D_SHORE = 2400.0, 700.0, 225.0, 160.0
WIND = math.radians(12.0)                       # wind from the left (drifts, patches, streaks lie along it)

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)
MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)
WB = PR.water["bands"]                          # 6 ice bands, far (horizon) -> near (angler)
# PER.const: a time-of-day period (periods/ice.py CONSTS) may change these; the native look uses the defaults
SNOW_CAP = PER.const("SNOW_CAP", "#dedcf2")     # brightest wind-packed snow on the ice
WSTEP = [PR.water["dark"]] + WB[::-1] + [SNOW_CAP]     # dark -> light ramp for +/- steps on the ice
REFL = PR.water["refl"]
RANGE = R.hazed(G(["#4a4672", "#625c88"]), D_RANGE, extra=-0.2)
RSNOW = R.hazed(G(PER.const("RSNOW", ["#c4c0e2", "#f2d6e0"])), D_RANGE, extra=-0.2)
HILL = R.hazed(G(["#26304c", "#3a4664"]), D_HILL)
HSNOW = R.hazed(G(["#a6aad2", "#cccce8"]), D_HILL)
FOREST = R.hazed(G(["#121a2c", "#1c2840", "#2c3a56"]), D_SHORE)
TSNOW = R.hazed(G(["#9aa2ce", "#d0d0ec"]), D_SHORE)
BACKF = R.hazed(G(["#222e48", "#34425e"]), D_BACK, extra=0.1)
BSNOW = R.hazed(G(["#b2b6dc"]), D_BACK, extra=0.1)
HOLE = [PR.water["deep"], "#1c2a54", "#2a3a6c", "#46568a"]     # deep -> reflection of the far slush wall
SLUSH = [PR.water["dark"], WB[3], WB[1], SNOW_CAP]             # funnel + lumps reuse the ice ramp
BACK_PAL = (SKY_PAL + MSKY_PAL + WSTEP + REFL + RANGE + RSNOW + HILL + HSNOW + FOREST + TSNOW + BACKF + BSNOW
            + HOLE)

SNOW = G(["#505c94", "#7480b6", "#98a0d0", "#bcbee2", "#d8d6ee"])
RED = G(["#48203a", "#76283a", "#a43c40", "#d0604e"])           # weathered red boards / auger handle
WINDOW = PER.const("WINDOW", ["#ffb85c", "#ffe2a4"])             # warm lit window (someone is inside)
METAL = G(["#343c5a", "#626e8c", "#a0acc6"])
YELLOW = G(["#7a5628", "#c49234", "#f0c850"])
OUTL = "#1e2040"                                 # darkest hue-shifted violet navy: outline fallback, never ink
FRONT_PAL = SNOW + RED + WINDOW + METAL + YELLOW + [OUTL]

LB = Vector(PR.key_dir).normalized()           # one cool key light for the whole stage (above-left)
LS = Vector((-0.28, -0.2, 1.0)).normalized()    # "snow catches the sky" light for snow on the trees
LR = Vector(PER.const("LR", (-0.85, -0.1, 0.52))).normalized()   # afterglow-side light for the far range (rose snow)
LD = Vector(PER.const("LD", (-0.55, 0.25, 0.8))).normalized()    # the key a little lower for the drifts: windward lit, lee in shade

# back-layer overlay codes produced by the front pass
OV_SHADOW = 1
_OV = {}


def rc(p):
    return R.rc(p, STAND)


def col_of(x, y):
    return rc((x, y, 0.0))[0]


def gap_dist(col):
    """Screen distance (px) from the far-shore gap: < 0 inside the gap."""
    return max(GAP_C[0] - col, col - GAP_C[1])


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


def plane_xy(z0):
    """Per-pixel world (X, Y) where the view ray of each pixel centre hits the plane z = z0 (NaN above the
    horizon). The inverse of R.rc for a plane: world-space patterns become perspective-correct pixels."""
    cx, cy, cz = P.cam_pos(STAND)
    s, c = math.sin(math.radians(P.PITCH)), math.cos(math.radians(P.PITCH))
    xc = np.broadcast_to(((np.arange(W) + 0.5 - W / 2) / P.F_PX)[None, :], (H, W))
    yc = np.broadcast_to(((H / 2 - (np.arange(H) + 0.5)) / P.F_PX)[:, None], (H, W))
    dz = yc * c - s
    with np.errstate(divide="ignore", invalid="ignore"):
        t = (z0 - cz) / dz
    ok = (dz < -1e-9) & (t > 0)
    X = np.where(ok, cx + t * xc, np.nan)
    Y = np.where(ok, cy + t * (yc * s + c), np.nan)
    return X, Y


def _h2(ix, iy, seed):
    v = np.sin(ix * 127.1 + iy * 311.7 + seed * 74.7) * 43758.5453
    return v - np.floor(v)


def vnoise2(x, y, seed=0.0):
    ix, iy = np.floor(x), np.floor(y)
    fx, fy = x - ix, y - iy
    ux, uy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = _h2(ix, iy, seed), _h2(ix + 1, iy, seed)
    c, d = _h2(ix, iy + 1, seed), _h2(ix + 1, iy + 1, seed)
    return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy


def lod_noise(u, v, su, sv, seed, Y, base=16.0):
    """World-space value noise whose feature size doubles with distance in DISCRETE levels, cross-faded
    (a continuous scale factor of Y would warp the pattern into wedges radiating from the vanishing point)."""
    lf = np.log2(np.maximum(1.0, Y / base))
    k = np.floor(lf)
    f = lf - k
    out = np.zeros_like(u)
    for kk in np.unique(k):
        m = k == kk
        s0 = 2.0 ** kk
        n0 = vnoise2(u[m] / (su * s0), v[m] / (sv * s0), seed + kk)
        n1 = vnoise2(u[m] / (su * s0 * 2), v[m] / (sv * s0 * 2), seed + kk + 1)
        out[m] = n0 * (1 - f[m]) + n1 * f[m]
    return out


# ================================================================== BACK LAYER geometry
def back_scene(rnd):
    mt = R.m_tone
    # ---- distant snowy range (2400 m): rock + snow, the afterglow side (left) lit rose, a notch under the glow
    rm_ = mt(RANGE + RSNOW, [0.46, 0.64, 0.86], light=LR, grads=((2, 4.0, 58.0, 0.34, "world"),),
             noise=0.08, nscale=0.02, ncoord="world", ndetail=1.0, name="Range")
    ridge_mesh("Range", rnd, y0=D_RANGE, depth=600, x0=-2800, x1=2800, step=30, hmax=80, hmin=30, mat=rm_, seed=2.0,
               valley=(VAL_C[0] + 10, VAL_C[1] - 14, 30, 0.12), kind="range")
    # ---- snowy forested hills (700 m): snow on the lit slopes, dark forest patches, open valley under the glow
    hm = mt(HILL + HSNOW, [0.44, 0.6, 0.8], light=LB, noise=0.22, nscale=0.06, ncoord="world", ndetail=1.0,
            grads=((2, 0, 16, 0.14, "world"),), name="Hill")
    _, hx, hz = ridge_mesh("Hill", rnd, y0=D_HILL, depth=300, x0=-900, x1=900, step=14, hmax=19, hmin=10, mat=hm,
                           seed=4.0, smooth=True, valley=(VAL_C[0] - 12, VAL_C[1] + 12, 34, 0.0), kind="hill")
    bm_ = mt(HILL + HSNOW[:1], [0.5, 0.78], light=LS, noise=0.14, nscale=1.5, ndetail=1.0, name="HillB")
    for k in range(260):
        x = rnd.uniform(-900, 900)
        zr = float(np.interp(x, hx, hz))
        if zr < 4.0:
            continue
        v = -rnd.random() ** 1.5 * 0.7
        z = zr * (1 - abs(v)) ** 1.35 - 2.0
        r = rnd.uniform(3.0, 5.5)
        ob = C.add_prim("ico", "HB", bm_, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, D_HILL + v * 150, z + r * 0.3)) @ Matrix.Diagonal((r * 0.8, r, r * 1.4, 1))
        C.set_smooth(ob)
        tagk(ob, "hill", "hills")
    # ---- far shore: a snow bank left and right of the gap
    y0 = D_SHORE - 1.0
    xl = R.col_to_x(GAP_C[0], y0, STAND)
    xr = R.col_to_x(GAP_C[1], y0, STAND)
    bank_m = mt(TSNOW + [SNOW_CAP], [0.55, 0.86], light=LS, noise=0.1, nscale=0.4, ncoord="world", name="Bank")
    for (a, b, side) in ((-420.0, xl, -1), (xr, 420.0, 1)):
        verts, faces = [], []
        xs = np.arange(a, b + 0.01, 3.0)
        for x in xs:
            edge = (b - x) if side < 0 else (x - a)
            taper = min(1.0, max(0.12, edge / 9.0))
            zt = ICE_Y + (1.5 + 0.4 * math.sin(x * 0.05) + rnd.uniform(-0.15, 0.15)) * taper
            yb = 320.0 if edge > 6 else y0 + 2 + (320 - y0 - 2) * max(0.0, edge) / 6.0
            verts += [(x, y0, ICE_Y - 0.05), (x, y0 + 2.2, zt), (x, max(y0 + 3.5, yb), zt + 1.6 * taper)]
        for i in range(len(xs) - 1):
            p, q = i * 3, (i + 1) * 3
            faces += [(p, q, q + 1, p + 1), (p + 1, q + 1, q + 2, p + 2)]
        tagk(R.mesh_from("Bank", verts, faces, bank_m, smooth=False), "bank")
    # snowy spruces: dark needles, the snow sits on the upward faces (LS light + noise -> flecks)
    pinem = mt(FOREST + TSNOW, [0.3, 0.5, 0.8, 0.9], light=LS, noise=0.12, nscale=2.6, ndetail=1.0, bias=-0.04,
               name="Pine")
    pineb = mt(BACKF + BSNOW, [0.42, 0.8], light=LS, noise=0.1, nscale=3.0, ndetail=1.0, name="PineB")
    trunk = mt(FOREST[:2], [0.55], light=LB, name="Trunk")
    lumpm = mt(TSNOW + [SNOW_CAP], [0.5, 0.82], light=LS, noise=0.15, nscale=3.5, ndetail=1.0, name="SnowLump")
    gid = [0]

    def plant(x, y, h, back=False):
        gd = gap_dist(col_of(x, y))
        if gd < 3:
            return
        h *= min(1.0, max(0.45, gd / 26.0))               # trees shrink towards the points of the gap
        if h < 3.2:
            return                                        # sub-pixel crowns would leave bare trunk sticks
        gid[0] += 1
        conifer(x, y, ICE_Y + (1.3 if not back else 1.0), h, rnd, pineb if back else pinem, None if back else trunk, f"t{gid[0]}")

    # front row: dense snowy spruces 4.5-7 m, clumps and small clearings
    x = -170.0
    while x < 170:
        n = 1 if rnd.random() < 0.45 else rnd.randint(2, 3)
        for k in range(n):
            plant(x + k * rnd.uniform(1.4, 2.4), rnd.uniform(y0 + 3.0, y0 + 9.0), rnd.uniform(4.5, 7.0))
        x += (n - 1) * 1.9 + rnd.uniform(2.2, 4.6)
    # snow mounds along the bank (hide the trunks)
    x = -170.0
    while x < 170:
        if gap_dist(col_of(x, y0 + 2)) > 2:
            gid[0] += 1
            shrub(x, rnd.uniform(y0 + 2.0, y0 + 3.2), ICE_Y + 1.1, rnd.uniform(0.9, 1.8), rnd, lumpm, f"s{gid[0]}")
        x += rnd.uniform(1.6, 2.8)
    # back bands (6-9 m), bluer and hazier
    for (ya, yb, dens, hmin, hmax) in ((y0 + 26, y0 + 50, 0.55, 6.0, 8.5), (y0 + 55, y0 + 110, 0.45, 7.0, 9.5)):
        x = -300.0
        while x < 300:
            plant(x, rnd.uniform(ya, yb), rnd.uniform(hmin, hmax), back=True)
            x += rnd.uniform(1.6, 3.4) / dens
    ice_sheet(rnd)


def ridge_mesh(name, rnd, y0, depth, x0, x1, step, hmax, hmin, mat, seed, smooth=False, valley=None, kind=None):
    """Ridge: grid in x (step) and y (7 rows), heights from summed sines + jitter. valley = (col0, col1,
    soft px, floor): heights fall to `floor` * height between the two screen columns (as hyb_lake)."""
    xs = np.arange(x0, x1 + step, step)
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    ridge = []
    f0 = 2 * math.pi / (0.15 * (x1 - x0))
    for x in xs:
        t = (math.sin(x * f0 + ph[0]) * 0.45 + math.sin(x * f0 * 2.7 + ph[1]) * 0.3
             + math.sin(x * f0 * 7.3 + ph[2]) * 0.15 + math.sin(x * f0 * 15.0 + ph[3]) * 0.1)
        hgt = hmin + (hmax - hmin) * (0.5 + 0.5 * t) + rnd.uniform(-0.05, 0.05) * hmax
        if valley:
            c = col_of(x, y0)
            c0, c1, soft, floor = valley
            d = max(c0 - c, c - c1)
            k = min(1.0, max(0.0, (d + soft) / soft))
            k = k * k * (3 - 2 * k)
            hgt = hgt * (floor + (1 - floor) * k)
        ridge.append(hgt)
    rows = [-1.0, -0.55, -0.2, 0.0, 0.3, 0.7, 1.0]
    verts, faces = [], []
    for i, x in enumerate(xs):
        for j, v in enumerate(rows):
            fall = max(0.0, 1 - abs(v)) ** 1.35
            z = ridge[i] * fall + (rnd.uniform(-0.06, 0.06) * ridge[i] if 0 < abs(v) < 1 else 0)
            verts.append((x + rnd.uniform(-0.3, 0.3) * step * (1 if abs(v) < 1 else 0), y0 + v * depth * 0.5, max(-2.0, z - 2.0)))
    nr = len(rows)
    for i in range(len(xs) - 1):
        for j in range(nr - 1):
            a = i * nr + j
            b = (i + 1) * nr + j
            faces.append((a, b, b + 1, a + 1))
    return tagk(R.mesh_from(name, verts, faces, mat, smooth=smooth), kind or name.lower()), xs, np.array(ridge)


def conifer(x, y, z0, h, rnd, mat, trunk, grp):
    """Snowy spruce: 5-7 star-shaped drooping tiers (as hyb_lake), narrower; the material puts the snow on
    the upward faces."""
    if trunk is not None:                               # far bands: no trunk (bare sticks under the tiers)
        tagk(R.loft("Trunk", [(x, y, z0 - 0.8), (x, y, z0 + h * 0.3)], h * 0.035, trunk, 6), "tree", grp)
    tiers = rnd.randint(5, 7)
    for k in range(tiers):
        t0 = 0.05 + 0.8 * k / tiers
        zc = z0 + h * t0
        r = h * 0.22 * (1 - 0.82 * k / tiers) * rnd.uniform(0.88, 1.1)
        th = h * 0.25
        n = 12
        verts = [(x, y, zc + th)]
        rot = rnd.uniform(0, 6.28)
        for i in range(n):
            a = rot + 2 * math.pi * i / n
            rr = r * (1.0 if i % 2 == 0 else 0.62) * rnd.uniform(0.85, 1.1)
            dz = -r * 0.3 if i % 2 == 0 else 0.0
            verts.append((x + rr * math.cos(a), y + rr * math.sin(a), zc + dz))
        verts.append((x, y, zc - r * 0.1))
        faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)] + [(n + 1, 1 + (i + 1) % n, 1 + i) for i in range(n)]
        ob = R.mesh_from("Tier", verts, faces, mat, smooth=True)
        R._fix_normals(ob)
        tagk(ob, "tree", grp)


def shrub(x, y, z0, h, rnd, mat, grp):
    for k in range(rnd.randint(2, 4)):
        r = h * rnd.uniform(0.45, 0.7)
        c = (x + rnd.uniform(-1.2, 1.2) * h, y + rnd.uniform(-0.4, 0.4), z0 + r * 0.3)
        ob = C.add_prim("ico", "SnowMound", mat, radius=1.0, location=(0, 0, 0), subdivisions=2)
        ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((r * 1.5, r, r * 0.7, 1))
        C.set_smooth(ob)
        tagk(ob, "bank", grp)


HOLE_OC = (HX, HZ - 0.2)                         # centre of the funnel top (shifted towards the viewer)
HOLE_RO = HR + 0.55                              # funnel top radius: near slope 0.2 m over 0.75 m (< view angle)


def ice_sheet(rnd):
    """Ice top at iceY with a chopped slush funnel down to the water plane y = 0. The funnel's bottom ring is
    the gameplay hole circle (holeX, holeZ, holeR) at y = 0; its near slope (~15 deg) is flatter than the view
    ray (~20 deg), so the whole water disc stays visible and projects exactly where Unity draws the tackle."""
    flat = R.m_flat(WB[3])
    s = 1.5
    Y1 = 3200.0
    for (x0, y0, x1, y1) in ((-9000, -40, HX - s, Y1), (HX + s, -40, 9000, Y1), (HX - s, -40, HX + s, HZ - s),
                             (HX - s, HZ + s, HX + s, Y1)):
        tagk(R.hpoly("Ice", [(x0, y0), (x1, y0), (x1, y1), (x0, y1)], ICE_Y - 0.01, flat, 0.02), "ice")
    n = 96
    verts, faces = [], []
    for i in range(n):
        a = 2 * math.pi * i / n
        dx, dy = math.cos(a), math.sin(a)
        k = s / max(abs(dx), abs(dy))
        verts += [(HOLE_OC[0] + HOLE_RO * dx, HOLE_OC[1] + HOLE_RO * dy, ICE_Y), (HX + dx * k, HZ + dy * k, ICE_Y)]
    for i in range(n):
        j = (i + 1) % n
        faces.append((2 * i, 2 * i + 1, 2 * j + 1, 2 * j))
    tagk(R.mesh_from("IceRing", verts, faces, flat, smooth=False), "ice")
    # funnel: J rings from the top circle (HOLE_OC, HOLE_RO, iceY) to the hole circle (HX, HZ, HR, 0)
    J = 7
    verts, faces = [], []
    for j in range(J + 1):
        t = j / J
        cx = HOLE_OC[0] + (HX - HOLE_OC[0]) * t
        cy = HOLE_OC[1] + (HZ - HOLE_OC[1]) * t
        r = HOLE_RO + (HR - HOLE_RO) * t
        z = ICE_Y * (1 - t) ** 1.2
        for i in range(n):
            a = 2 * math.pi * i / n
            verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
    for j in range(J):
        for i in range(n):
            i2 = (i + 1) % n
            faces.append((j * n + i, j * n + i2, (j + 1) * n + i2, (j + 1) * n + i))
    fm = R.m_tone(SLUSH, [0.42, 0.62, 0.86], light=LB, lam=0.7, bias=-0.05,
                  grads=((2, 0.0, ICE_Y, 0.42, "world"),), noise=0.1, nscale=9.0, ncoord="world", name="Slush")
    tagk(R.mesh_from("Funnel", verts, faces, fm, smooth=True), "rim")
    tagk(R.hpoly("HoleWater", R.ellipse_pts(HX, HZ, HR + 0.03, HR + 0.03, 64), -0.004, R.m_flat(HOLE[1]), 0.002), "hole")
    # chipped ice / slush lumps around the funnel top (fewer on the near side: never over the water)
    lm = R.m_tone(SLUSH[1:], [0.45, 0.78], light=LB, name="Chips")
    for k in range(46):
        a = rnd.uniform(0, 2 * math.pi)
        if math.sin(a) < -0.35 and rnd.random() < 0.65:
            continue
        rr = HOLE_RO + rnd.uniform(0.04, 0.5) * (1.0 if math.sin(a) > -0.35 else 1.4)
        x, y = HOLE_OC[0] + rr * math.cos(a), HOLE_OC[1] + rr * math.sin(a)
        sz = rnd.uniform(0.05, 0.15)
        ob = C.add_prim("ico", "Chip", lm, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, y, ICE_Y + sz * 0.15)) @ Matrix.Rotation(rnd.uniform(0, 6.28), 4, "Z") \
            @ Matrix.Diagonal((sz * 1.4, sz, sz * 0.55, 1))
        tagk(ob, "lump")


# ================================================================== FRONT LAYER geometry
DRIFTS = [  # (cx, cy, half-length along the wind, half-width, height)
    (-7.4, -0.6, 3.2, 1.7, 1.1), (-4.3, 2.4, 1.1, 0.6, 0.32), (6.9, 0.2, 2.8, 1.4, 0.95), (4.9, 3.5, 1.0, 0.62, 0.3),
    (-11.8, 10.5, 3.2, 1.5, 1.0), (-8.4, 16.2, 1.6, 0.7, 0.4), (15.8, 17.5, 3.4, 1.5, 1.1), (10.4, 12.0, 1.2, 0.55, 0.3),
    (-19.5, 31.0, 3.0, 1.2, 0.9), (21.5, 36.0, 3.5, 1.3, 1.0), (-3.4, -2.6, 1.0, 0.5, 0.25)]
SHANTY = (12.6, 27.0)
SHANTY2 = (-22.5, 55.0)


def front_scene(rnd):
    mt = R.m_tone
    objs = []
    snowm = mt(SNOW, [0.36, 0.56, 0.76, 0.93], light=LD, lam=1.4, bias=-0.36, noise=0.05, nscale=1.2, ndetail=1.0,
               name="Drift")
    for k, (cx, cy, a, b, h) in enumerate(DRIFTS):
        objs.append(drift(f"Drift{k}", cx, cy, a, b, h, rnd, snowm, f"dr{k}"))
    objs += shanty(SHANTY[0], SHANTY[1], 1.0, rnd, snowm, "sh1")
    objs += shanty(SHANTY2[0], SHANTY2[1], 0.9, rnd, snowm, "sh2", flip=True)
    # drift piled against the shanty (windward = left side) and in front of its door
    objs.append(drift("DriftSh", SHANTY[0] - 1.5, SHANTY[1] - 0.2, 0.9, 1.4, 0.55, rnd, snowm, "drsh"))
    objs.append(drift("DriftSh2", SHANTY[0] - 0.4, SHANTY[1] - 1.5, 1.2, 0.35, 0.25, rnd, snowm, "drsh2"))
    # ---- ice auger stuck in a small drift right of the hole
    metal = mt(METAL, [0.45, 0.76], light=LB, name="Metal")
    red = mt(RED[1:], [0.45, 0.78], light=LB, name="Red")
    ax, ay = 3.78, 2.5
    top = (ax + 0.2, ay - 0.05, ICE_Y + 1.62)
    objs.append(tagk(R.loft("AugerShaft", [(ax + 0.01, ay, ICE_Y + 0.04), top], 0.032, metal, 8), "prop", "auger"))
    helix = []
    for k in range(25):
        t = k / 24
        z = ICE_Y + 0.06 + 0.42 * t
        a = t * 4 * 2 * math.pi
        helix.append((ax + 0.2 * (z - ICE_Y + 0.2) / 1.82 + 0.085 * math.cos(a), ay + 0.085 * math.sin(a), z))
    objs.append(tagk(R.loft("Flight", helix, 0.022, metal, 6), "prop", "auger"))
    cz = ICE_Y + 1.12
    cxa = ax + 0.2 * (cz - ICE_Y + 0.2) / 1.82
    crank = [(cxa, ay, cz), (cxa - 0.22, ay - 0.02, cz + 0.06), (cxa - 0.22, ay - 0.02, cz + 0.2), (cxa + 0.01, ay, cz + 0.28)]
    objs.append(tagk(R.loft("Crank", crank, 0.026, metal, 6), "prop", "auger"))
    objs.append(tagk(R.loft("Grip", [(cxa - 0.22, ay - 0.02, cz + 0.07), (cxa - 0.22, ay - 0.02, cz + 0.2)], 0.04, red, 8),
                     "prop", "auger"))
    objs.append(tagk(R.loft("THandle", [(top[0] - 0.3, top[1], top[2]), (top[0] + 0.3, top[1], top[2])], 0.036, red, 8),
                     "prop", "auger"))
    objs.append(drift("DriftAug", ax + 0.1, ay + 0.1, 0.55, 0.4, 0.2, rnd, snowm, "draug"))
    # ---- yellow bucket (seat) with a snowy lid, left of the angler
    yel = mt(YELLOW, [0.42, 0.74], light=LB, pats=((2, 0.13, 0.14, -0.25, "world"),), name="Bucket")
    bx, by = -1.4, 0.8
    objs.append(tagk(R.loft("Bucket", [(bx, by, ICE_Y - 0.02), (bx, by, ICE_Y + 0.44)], [0.155, 0.18], yel, 16), "prop", "bucket"))
    objs.append(tagk(R.loft("Rim", [(bx, by, ICE_Y + 0.43), (bx, by, ICE_Y + 0.47)], 0.192, mt(YELLOW[1:], [0.6], light=LB, name="BRim"), 16),
                     "prop", "bucket"))
    objs.append(tagk(R.ellipsoid("Lid", (bx, by, ICE_Y + 0.47), (0.17, 0.17, 0.05), snowm), "prop", "bucket"))
    objs.append(tagk(R.loft("Bail", [(bx - 0.19, by, ICE_Y + 0.4), (bx - 0.13, by - 0.08, ICE_Y + 0.58), (bx, by - 0.11, ICE_Y + 0.63),
                                     (bx + 0.13, by - 0.08, ICE_Y + 0.58), (bx + 0.19, by, ICE_Y + 0.4)], 0.011, R.m_flat(METAL[0]), 6),
                     "prop", "bail"))
    return objs


def drift(name, cx, cy, a, b, h, rnd, mat, grp):
    """Wind-carved snow drift: elliptic footprint on the ice, gentle windward (left) slope rising to a crest
    near the lee end, steep lee face; small sastrugi ripples."""
    ca, sa = math.cos(WIND), math.sin(WIND)
    nr, na = 8, 48
    crest = 0.55

    def fsf(s_):
        return (0.5 - 0.5 * math.cos(math.pi * (s_ + 1) / (crest + 1))) if s_ < crest else \
            (0.5 + 0.5 * math.cos(math.pi * (s_ - crest) / (1 - crest)))
    verts = [(cx, cy, ICE_Y - 0.01 + h * (0.3 + 0.7 * fsf(0.0)))]
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    for k in range(1, nr + 1):
        t = k / nr
        for m in range(na):
            th = 2 * math.pi * m / na
            # irregular footprint (lobes) + small sastrugi ripples across the wind
            wob = 1 + 0.16 * math.sin(2 * th + ph[0]) + 0.09 * math.sin(3 * th + ph[1]) + 0.05 * math.sin(5 * th + ph[2])
            s_, v_ = t * math.cos(th), t * math.sin(th)
            z = h * (1 - t * t) ** 0.5 * (0.3 + 0.7 * fsf(s_)) * (1 + 0.07 * math.sin(s_ * 11 + ph[3]) * t)
            lx, ly = a * s_ * wob, b * v_ * wob
            verts.append((cx + lx * ca - ly * sa, cy + lx * sa + ly * ca, ICE_Y - 0.01 + (z if k < nr else 0.0)))
    faces = [(0, 1 + m, 1 + (m + 1) % na) for m in range(na)]
    for k in range(nr - 1):
        for m in range(na):
            m2 = (m + 1) % na
            faces.append((1 + k * na + m, 1 + (k + 1) * na + m, 1 + (k + 1) * na + m2, 1 + k * na + m2))
    ob = R.mesh_from(name, verts, faces, mat, smooth=True)
    R._fix_normals(ob)
    return tagk(ob, "drift", grp)


def shanty(x, y, sc, rnd, snowm, grp, flip=False):
    """Small ice-fishing shanty: red vertical boards, snowy gable roof (ridge along Y), door facing the camera,
    a warm lit window on the side facing the centre, stovepipe."""
    mt = R.m_tone
    objs = []
    w, d, h = 2.0 * sc, 2.4 * sc, 2.0 * sc
    z0 = ICE_Y
    boards = mt(RED, [0.3, 0.55, 0.8], light=LB, pats=((0, 0.3 * sc, 0.16, -0.26, "world"),), name="Boards")
    sideb = mt(RED, [0.3, 0.55, 0.8], light=LB, pats=((1, 0.3 * sc, 0.16, -0.26, "world"),), name="BoardsS")
    body = R.box("Shanty", (x, y, z0 + h / 2), (w, d, h), boards)
    objs.append(tagk(body, "prop", grp))
    sx = x + (w / 2 + 0.012) * (1 if flip else -1)        # side face towards the centre of the stage
    objs.append(tagk(R.box("Side", (sx, y, z0 + h / 2), (0.02, d - 0.02, h - 0.02), sideb), "prop", grp))
    # gable (front triangle) + roof slabs with snow
    yf = y - d / 2
    rz = z0 + h
    ridge = rz + 0.75 * sc
    gv = [(x - w / 2, yf - 0.01, rz), (x + w / 2, yf - 0.01, rz), (x, yf - 0.01, ridge),
          (x - w / 2, yf + 0.05, rz), (x + w / 2, yf + 0.05, rz), (x, yf + 0.05, ridge)]
    gob = R.mesh_from("Gable", gv, [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], boards, smooth=False)
    R._fix_normals(gob)
    objs.append(tagk(gob, "prop", grp))
    ov = 0.18 * sc
    for sgn in (-1, 1):
        # slab from the ridge down to the eave, snow-covered (thick, rounded look via the drift material)
        p0 = (x, y - d / 2 - ov, ridge + 0.02)
        e0 = (x + sgn * (w / 2 + ov), y - d / 2 - ov, rz - 0.12 * sc)
        e1 = (x + sgn * (w / 2 + ov), y + d / 2 + ov, rz - 0.12 * sc)
        p1 = (x, y + d / 2 + ov, ridge + 0.02)
        th = 0.16 * sc
        vv = [p0, e0, e1, p1] + [(q[0], q[1], q[2] + th) for q in (p0, e0, e1, p1)]
        ff = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
        rob = R.mesh_from("Roof", vv, ff, snowm, smooth=False)
        R._fix_normals(rob)
        objs.append(tagk(rob, "prop", grp))
    # door (front face) + lit windows
    door = mt(RED[:2], [0.6], light=LB, name="Door")
    dx = x + 0.38 * sc * (-1 if flip else 1)
    objs.append(tagk(R.box("Door", (dx, yf - 0.03, z0 + 0.8 * sc), (0.66 * sc, 0.04, 1.5 * sc), door), "prop", grp))
    objs.append(tagk(R.box("Knob", (dx - 0.22 * sc * (-1 if flip else 1), yf - 0.06, z0 + 0.8 * sc), (0.06, 0.03, 0.06),
                           R.m_flat(METAL[2])), "prop", grp))
    wm = R.m_tone(WINDOW, [0.5], lam=0.0, grads=((2, z0 + 1.15 * sc, z0 + 1.6 * sc, 1.0, "world"),), name="Win")
    wx = x - 0.5 * sc * (-1 if flip else 1)
    objs.append(tagk(R.box("WinF", (wx, yf - 0.03, z0 + 1.38 * sc), (0.42 * sc, 0.04, 0.4 * sc), wm), "prop", grp))
    objs.append(tagk(R.box("WinS", (sx + 0.02 * (1 if flip else -1), y + 0.2 * sc, z0 + 1.35 * sc), (0.04, 0.62 * sc, 0.46 * sc), wm),
                     "prop", grp))
    # stovepipe on the far roof slope
    pm = mt(METAL, [0.4, 0.75], light=LB, name="Pipe")
    px = x + 0.45 * sc * (1 if not flip else -1)
    py = y + 0.6 * sc
    objs.append(tagk(R.loft("Pipe", [(px, py, rz), (px, py, ridge + 0.7 * sc)], 0.075 * sc, pm, 10), "prop", grp))
    objs.append(tagk(R.loft("Cap", [(px, py, ridge + 0.7 * sc), (px, py, ridge + 0.78 * sc)], 0.12 * sc, pm, 10), "prop", grp))
    return objs


# ================================================================== back composite
# ice depth zones (start row, end row) for WB[i] -> WB[i+1]; the play area (rows ~160-250) is WB[4]
ICE_Z = [(95, 103), (108, 114), (119, 129), (140, 158), (248, 268)]
ROW_H, ROW_P = 92, 190             # dash length reference rows: long calm strokes on the ice (lake: 106, 335)


def reflect_tint(ref_hex, k, dark):
    """Colour transform for reflections: towards the ice colour by k, then darker (linear x dark)."""
    wl = R.s2l(R.hexrgb(ref_hex))

    def fn(c):
        return R.l2s((R.s2l(c) * (1 - k) + wl[None, :] * k) * dark)
    return fn


def far_reflection(pal, idx, ice, refl_objs):
    """Faint EXACT reflection of the far layers on the glazed far ice (mirror pass): the layers' own palette
    colours, pushed strongly towards the ice, runs >= 3 px, a lighter break line every third row, dissolving
    from 40 % of its height (ice is a dull mirror)."""
    mp = R.mirror_pass(refl_objs, tag=f"{SID}_farmirror")
    rng = random.Random(7)
    ra = mp["a"] & ice
    midx, _ = R.quantize(mp["rgb"], ra, pal, dither=False)
    lab = R.min_runs(np.where(ra, midx + 1, 0), 3)
    top = np.argmax(ice, 0)
    has = lab.any(0)
    bot = np.where(has, H - 1 - np.argmax(lab[::-1] > 0, 0), top)
    kern = np.ones(9) / 9
    botf = np.convolve(np.pad(bot.astype(float), 4, mode="edge"), kern, "valid")
    hgt = np.maximum(1.0, botf - top + 1)
    rows = np.arange(H)[:, None]
    frac = (rows - top[None, :]) / hgt[None, :]
    out = idx.copy()
    keepm = np.zeros((H, W), bool)
    linem = np.zeros((H, W), bool)
    for r in range(H):
        m = lab[r] > 0
        if not m.any():
            continue
        p = np.clip((frac[r] - 0.4) / 0.6, 0, 1)
        brk = R.run_noise(rng, W, 3, 8) < p
        keep = m & ~brk & ice[r]
        out[r, keep] = lab[r, keep] - 1
        keepm[r] = keep
        if (r - int(np.median(top))) % 3 == 2:
            linem[r] = R.dash_mask(W, 0.45, 6, 20, rng) & keep
    body = keepm & ~linem
    out2, pal = R.recolour(out, pal, body, reflect_tint(WB[1], 0.5, 0.86), snap=0.035)
    out2, pal = R.recolour(out2, pal, linem, reflect_tint(WB[1], 0.72, 0.95), snap=0.035)
    return out2, pal, keepm


def far_sky(pal, idx, ice, wband, shore_row):
    """The glazed far ice mirrors the sky (bands + afterglow rings + aurora), strongest in the gap under the
    glow, tinted towards the far ice, joined to the ice bands by dash dithering; light break lines every 3rd row."""
    rng = random.Random(21)
    cols = np.arange(W)[None, :]
    rows = np.arange(H)[:, None]
    in_gap = (cols > GAP_C[0] - 24) & (cols < GAP_C[1] + 24)
    zone = ice & (rows < np.where(in_gap, shore_row + 5, shore_row + 2))
    thr = R.dash_threshold(H, W, rng, 2, 7)
    m_idx, _ = R.quantize(MSKY_RGB, zone, pal, dither=True, thr=thr)
    fade = np.clip((rows - (shore_row - 1)) / np.where(in_gap, 6.0, 3.0), 0, 1)
    use = zone & (R.dash_threshold(H, W, rng, 3, 9) >= fade)
    out = idx.copy()
    out[use] = m_idx[use]
    out, pal = R.recolour(out, pal, use, reflect_tint(WB[0], 0.3, 0.92), snap=0.022)
    for r in range(int(R.HORIZON_ROW), int(shore_row) + 5):
        if r % 3 == 1:
            bl = R.dash_mask(W, 0.35, 5, 16, rng) & use[r]
            out[r, bl] = wband[r, bl]
    return out, pal, use


def far_rims(idx, pal, kid):
    """Rose rim on the silhouette tops / afterglow-side (left) edges of the far layers, only near the glow."""
    lv, _ = R.glow_level(PR)
    prox = np.clip(lv / 0.28, 0, 1)
    rim = PR.rim
    out, p2 = idx, pal
    for kinds, sw in ((("range",), 0.6), (("hill",), 0.6), (("tree", "bank"), 0.2)):
        # trees: tops only (a rim on every narrow spruce's left edge read as a row of pink sticks)
        m = np.isin(kid, kinds)
        up_out = m & ~R.shift(m, 1, 0, False)
        side_out = m & ~R.shift(m, 0, -1, False) if R.sun_side(PR) == "right" else m & ~R.shift(m, 0, 1, False)
        k = (up_out * 1.0 + side_out * sw).clip(0, 1) * prox
        out, p2 = R.blend_idx(out, p2, k > 0.55, rim["col"], 0.5, lighter=True, snap=0.03)
        out, p2 = R.blend_idx(out, p2, (k > 0.22) & (k <= 0.55), rim["col"], 0.28, lighter=True, snap=0.03)
    return out, p2


def step_on(pal, idx, mask, d):
    """Move masked pixels d steps along the ice ramp WSTEP (pixels not on the ramp stay)."""
    out = idx.copy()
    out[mask] = R.ramp_step(pal, WSTEP, idx, d)[mask]
    return out


def draw_px_line(mask, c0, r0, c1, r1):
    n = int(max(abs(c1 - c0), abs(r1 - r0))) + 1
    for k in range(n + 1):
        t = k / max(n, 1)
        c = int(math.floor(c0 + (c1 - c0) * t))
        r = int(math.floor(r0 + (r1 - r0) * t))
        if 0 <= r < H and 0 <= c < W:
            mask[r, c] = True


def ice_texture(pal, idx, ice, avoid):
    """Wind-blown snow patches (+1/+2), polished dark patches (-1), wind streaks (+1) and thin cracks (-2 with a
    lit lip) - evaluated in WORLD space on the ice plane (perspective-correct), sparse in the calm play area."""
    X, Y = plane_xy(ICE_Y)
    ok = ice & np.isfinite(X) & ~avoid
    Xs, Ys = np.nan_to_num(X), np.nan_to_num(Y, nan=1e4)
    ca, sa = math.cos(WIND), math.sin(WIND)
    u = Xs * ca + Ys * sa
    v = -Xs * sa + Ys * ca
    n1 = 0.65 * lod_noise(u, v, 4.5, 1.2, 1.0, Ys) + 0.35 * lod_noise(u, v, 1.7, 0.55, 20.0, Ys)
    centre = np.exp(-((Xs - 1.2) / 8.5) ** 2) * np.exp(-((Ys - 8.0) / 13.0) ** 2)
    rh = np.hypot(Xs - HOLE_OC[0], Ys - HOLE_OC[1])
    apron = np.exp(-((rh - (HOLE_RO + 0.55)) / 0.5) ** 2)       # trampled snow ring around the hole
    thr = 0.64 + 0.14 * centre - 0.34 * apron
    lvl = (ok & (n1 > thr)).astype(int) + (ok & (n1 > thr + 0.08)).astype(int)
    lvl = R.min_runs(lvl, 3)
    lvl[~ok] = 0
    n2 = lod_noise(u, v, 3.2, 0.8, 50.0, Ys)
    dark = ok & (lvl == 0) & (n2 > 0.78 + 0.1 * centre)
    dark = R.min_runs(dark.astype(int), 4).astype(bool) & ok
    out = step_on(pal, idx, lvl >= 1, +1)
    out = step_on(pal, out, lvl >= 2, +1)
    out = step_on(pal, out, dark, -1)
    rng = random.Random(41)
    # ---- wind streaks: long thin polished lines along the wind, broken into dashes
    streak = np.zeros((H, W), bool)
    for k in range(70):
        y = 1.5 + 70 * rng.random() ** 1.7
        x = rng.uniform(-1, 1) * (y * 0.72 + 7)
        if abs(x - 1.2) < 7 and y < 22 and rng.random() < 0.75:
            continue
        L = rng.uniform(2.0, 7.0) * (1 + y / 40)
        a = WIND + rng.uniform(-0.08, 0.08)
        seg = 0.0
        while seg < L:
            sl = rng.uniform(0.6, 1.6) * (1 + y / 30)
            p0 = rc((x + math.cos(a) * seg, y + math.sin(a) * seg, ICE_Y))
            p1 = rc((x + math.cos(a) * (seg + sl), y + math.sin(a) * (seg + sl), ICE_Y))
            draw_px_line(streak, p0[0], p0[1], p1[0], p1[1])
            seg += sl + rng.uniform(0.3, 1.2) * (1 + y / 30)
    streak &= ok & (lvl == 0)
    out = step_on(pal, out, streak, +1)
    # ---- cracks: random walks with branches, 1 px, 2 steps darker; near ones get a lit far lip
    crack = np.zeros((H, W), bool)

    def walk(x, y, hd, nseg, depth):
        for s in range(nseg):
            sl = rng.uniform(0.25, 0.8) * (1 + y / 14)
            hd += rng.gauss(0, 0.45)
            x2, y2 = x + math.cos(hd) * sl, y + math.sin(hd) * sl
            if math.hypot(x2 - HOLE_OC[0], y2 - HOLE_OC[1]) < HOLE_RO + 0.9 or y2 < -2:
                return
            p0, p1 = rc((x, y, ICE_Y)), rc((x2, y2, ICE_Y))
            draw_px_line(crack, p0[0], p0[1], p1[0], p1[1])
            if depth < 2 and rng.random() < 0.18:
                walk(x2, y2, hd + rng.choice((-1, 1)) * rng.uniform(0.6, 1.2), rng.randint(2, 5), depth + 1)
            x, y = x2, y2

    for k in range(16):
        y = 1.0 + 34 * rng.random() ** 1.4
        x = rng.uniform(-1, 1) * (y * 0.7 + 7)
        if abs(x - 1.2) < 6.5 and y < 20 and rng.random() < 0.7:
            continue
        walk(x, y, rng.uniform(0, math.pi), rng.randint(4, 9), 0)
    crack &= ok
    rows = np.arange(H)[:, None]
    lip = R.shift(crack, -1, 0, False) & ~crack & ok & (rows > 215)
    lip &= np.random.default_rng(3).random((H, W)) < 0.6
    out = step_on(pal, out, crack, -1)                     # subtle: one step, two only close to the angler
    out = step_on(pal, out, crack & (rows > 240), -1)
    out = step_on(pal, out, lip, +1)
    return out


def paint_hole(pal, idx, hole):
    """Dark open water in the hole: under the far slush wall its reflection (lighter band), the body, a few
    ripple dashes, darkest (waterDeep) towards the near lip where the view looks steeply into the water."""
    out = idx.copy()
    hi = [pal.index(c) for c in HOLE]
    rng = random.Random(9)
    for c in range(W):
        rs = np.flatnonzero(hole[:, c])
        if not len(rs):
            continue
        t0, t1 = rs.min(), rs.max()
        n = t1 - t0 + 1
        for r in rs:
            f = (r - t0) / max(1, n - 1)
            if r - t0 < 1:
                v = 3 if n > 4 else 2
            elif r - t0 < 3:
                v = 2
            elif f > 0.72:
                v = 0
            else:
                v = 1
            out[r, c] = hi[v]
    rows = np.flatnonzero(hole.any(1))
    if len(rows):
        for r in range(rows.min() + 4, rows.max() - 3, 3):
            m = R.dash_mask(W, 0.25, 3, 7, rng) & hole[r]
            out[r, m] = hi[2]
    return out


def contact_shadow(pal, idx, ice):
    """Angler's feet: a 14x3 px ellipse one step darker on the ice at the projected feet point."""
    fc, fr = rc((0, 0, STAND))
    cx, cy = int(round(fc - 0.5)), int(fr) + 1
    m = np.zeros((H, W), bool)
    for dy, hw in ((-1, 4), (0, 7), (1, 4)):
        for dx in range(-hw, hw):
            y, x = cy + dy, cx + dx
            if 0 <= y < H and 0 <= x < W:
                m[y, x] = True
    return step_on(pal, idx, m & ice, -1)


def hole_check(kid):
    """Rendered open water vs. the gameplay hole (holeX, holeZ, holeR at y = 0) projected analytically."""
    X0, Y0 = plane_xy(0.0)
    ana = np.nan_to_num(np.hypot(X0 - HX, Y0 - HZ), nan=99) < HR
    ren = kid == "hole"
    inter = (ana & ren).sum()
    union = (ana | ren).sum()
    ys, xs = np.nonzero(ren)
    gc, gr = rc((HX, HZ, 0.0))
    print("CHECK ice hole px rendered", int(ren.sum()), "analytic", int(ana.sum()), "IoU", round(inter / max(1, union), 3),
          "xor px", int((ana ^ ren).sum()), "centroid", round(xs.mean() + 0.5, 2), round(ys.mean() + 0.5, 2),
          "gameplay point", round(gc, 2), round(gr, 2), "rows", ys.min(), ys.max(), "cols", xs.min(), xs.max())


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    PER.hook("back_scene", rnd=rnd, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_back")
    kid = R.kind_map(ps)
    sky = ~ps["a"]
    ice = kid == "ice"
    hole = kid == "hole"
    tree = (kid == "tree") | (kid == "bank")
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = SKY_RGB[sky]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid | sky, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=3.0, rel=0.015, steps=1)
    idx = np.where(tree, lined, idx)
    idx, pal = far_rims(idx, pal, kid)
    # ---- ice: depth bands with horizontal dash dithering
    wband = R.water_bands(pal, WB, ICE_Z, random.Random(5), ROW_H, ROW_P)
    idx[ice] = wband[ice]
    shore_row = rc((0, D_SHORE - 1.0, ICE_Y))[1]
    idx, pal, skym = far_sky(pal, idx, ice, wband, shore_row)
    # ---- faint exact reflections of the far layers on the glazed far ice
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("tree", "bank", "hill", "range")]
    idx, pal, farm = far_reflection(pal, idx, ice, refl_objs)
    # ---- snow patches / polished patches / streaks / cracks (world space, calm centre)
    idx = ice_texture(pal, idx, ice, farm | skym)
    # ---- cast shadows of the front props (computed by the front pass) + the angler's contact shadow
    ov = _OV.get("ov")
    if ov is None:
        p = os.path.join(R.WORK, "front_overlay.npy")
        ov = np.load(p) if os.path.exists(p) else np.zeros((H, W), np.int8)
    idx = step_on(pal, idx, ice & (ov == OV_SHADOW), -1)
    idx = contact_shadow(pal, idx, ice)
    # ---- the hole: dark open water, pixel-exact on the gameplay hole
    idx = paint_hole(pal, idx, hole)
    # ---- thin lilac mist band on the far ice line (dash dithered, wisps)
    land = ~sky & ~ice
    line = np.array([np.flatnonzero(ice[:, c]).min() if ice[:, c].any() else H for c in range(W)], float)
    ma = R.mist_amount(PR, line, land, ice)
    thr = R.dash_threshold(H, W, random.Random(29), 2, 9)
    idx, pal = R.blend_idx(idx, pal, (ma > thr) & (ma > 0.08), PR.mist["col"], 0.5, snap=0.03)
    idx, pal = PER.hook("back_post", idx, pal, kid=kid, water=ice, sky=sky, ps=ps, pr=PR, stand=STAND, rc=rc, hole=hole)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "back")
    hole_check(kid)
    # play-area ice: the band Unity's waterTint must agree with
    x0, y0, cw, ch = R.CROP
    play = np.zeros((H, W), bool)
    play[165:300, 176:464] = True
    vals, cnt = np.unique(idx[play & ice], return_counts=True)
    mode = pal.hex[int(vals[np.argmax(cnt)])]
    print("HYB back colours", R.count_colours(img), "crop colours", R.count_colours(img[y0:y0 + ch, x0:x0 + cw]),
          "palette", len(pal), "play-area ice", mode, "L", round(R.lum(mode), 3), "share", round(cnt.max() / cnt.sum(), 2),
          "waterTint", PR.water["tint"], "L", round(R.lum(PR.water["tint"]), 3), "near ice", WB[5], "L", round(R.lum(WB[5]), 3))
    return pal


# ---------------------------------------------------------------- front post helpers
def shadow_pass(objs, tag):
    """Cast shadows of front objects on the ice: mesh copies sheared along the key light and flattened onto
    z = iceY (baked into the mesh data - object matrices cannot shear), rendered alone -> mask."""
    sc = bpy.context.scene
    hidden = [ob for ob in R.mesh_objects(False) if not ob.hide_render]
    for ob in hidden:
        ob.hide_render = True
    kx, ky = LB.x / LB.z, LB.y / LB.z
    Msh = Matrix(((1, 0, -kx, kx * ICE_Y), (0, 1, -ky, ky * ICE_Y), (0, 0, 0.001, ICE_Y * 0.999 + 0.004), (0, 0, 0, 1)))
    copies = []
    for ob in objs:
        me = ob.data.copy()
        me.transform(Msh @ ob.matrix_world)
        c = bpy.data.objects.new("Shd", me)
        sc.collection.objects.link(c)
        copies.append(c)
    bpy.context.view_layer.update()
    ps = R.render_passes(tag, want_ids=False)
    for c in copies:
        me = c.data
        bpy.data.objects.remove(c, do_unlink=True)
        bpy.data.meshes.remove(me)
    for ob in hidden:
        ob.hide_render = False
    return ps["a"]


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    objs = front_scene(rnd)
    PER.hook("front_scene", rnd=rnd, refl=objs, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_front")
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    idx = R.despeckle(idx, ps["id"], protect=~solid, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    # pink rim on the OUTER afterglow-side (left / top) silhouette, before the outline
    idx, pal = R.rim_light(idx, pal, PR, mask=(kid == "prop") | (kid == "drift"))
    # hue-shifted outline (never ink): props all round; drifts only below / at the sides (a contact line on
    # the ice - their bright tops read against the ice by value)
    prop = (kid == "prop") & (idx >= 0)
    drf = (kid == "drift") & (idx >= 0)
    ol_p = R.outer_outline(np.where(prop, idx, -1), pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    ol_d = R.outer_outline(np.where(drf, idx, -1), pal, lit_steps=1, dark_steps=2, top=False, light=R.sun_side(PR))
    empty = idx < 0
    new_p = empty & (ol_p >= 0) & ~prop
    new_d = empty & (ol_d >= 0) & ~drf & ~new_p
    idx = np.where(new_p, ol_p, idx)
    idx = np.where(new_d, ol_d, idx)
    # ---- back-layer overlay: cast shadows on the ice
    shd = shadow_pass(objs, f"{SID}_shadow")
    ov = np.where(shd, OV_SHADOW, 0).astype(np.int8)
    _OV["ov"] = ov
    np.save(os.path.join(R.WORK, "front_overlay.npy"), ov)
    idx, pal = PER.hook("front_post", idx, pal, kid=kid, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "front")
    lit = [R.lum(c) for c in SNOW[1:]]
    print("HYB front colours", R.count_colours(img), "palette", len(pal), "drift snow L", round(min(lit), 3), "-",
          round(max(lit), 3), "vs ice L", round(R.lum(WB[4]), 3), round(R.lum(WB[5]), 3))
    # nothing of the front layer may cover the open water (Unity draws float / ripples under the front layer)
    X0, Y0 = plane_xy(0.0)
    ana = np.nan_to_num(np.hypot(X0 - HX, Y0 - HZ), nan=99) < HR + 0.25
    print("CHECK ice front px over the hole (+0.25 m)", int((ana & (idx >= 0)).sum()))


def main():
    C.reset_scene()
    which = PER.which()                      # back / front (+ --period <p> [--dry], see hyb_period)
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    PER.stage_json(SID, PR, clouds=False, birds=None)
    print("HYB ICE done")


if __name__ == "__main__":
    main()
