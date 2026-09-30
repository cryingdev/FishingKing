"""
hybrid - the lake stage: retro16 composition and craft + the "lake" preset mood (golden hour).

Back layer (opaque):
  * banded sky from the preset (deep blue -> coral -> amber), a pixel sun low over an OPEN BAY right of
    centre, concentric dithered glow rings, 3 thin stratus streaks with gold-lit undersides;
  * layered far silhouettes with aerial perspective (every ramp pushed towards the warm haze colour by
    its distance): distant range 2400 m (lightest) -> forested hills 700 m -> back tree bands 190-290 m ->
    the far-shore tree row 183-189 m (darkest, most saturated); the range and hills open into a valley
    under the sun; warm rims on the silhouette tops / right edges near the sun only;
  * water: retro16 depth bands joined by horizontal dash dithering (warm far -> calm teal-blue play area),
    the far water in the bay mirrors the sky exactly (glow rings included), EXACT mirrored reflections of
    every far layer (mirror pass, water-tinted, retro16 break lines + dissolve), sparse wave marks, a thin
    dash-dithered mist band on the far waterline, sparse short glitter dashes in the far third only;
  * the reflections / ripples / pad lines of the front objects (computed by the front pass).
Front layer (transparent, solid objects only): plank pier, posts + rope, bucket + tackle box, contact
shadow, reed clumps, hand-shaded lily pads (always lighter than the water), moored rowboat + stake.
Key light from the sun side (right, lifted), warm rim on the OUTER right/top silhouette of solid props.

Same camera (fk_persp.setup_camera(1.0)), 640x400, same gameplay layout as Data/stage_lake.json.
Outputs: _tmp/variants/hybrid/lake_back.png, lake_front.png, stage_lake.json
Run: blender -b --python variants/hybrid/hyb_lake.py [-- back|front] [--period dawn|day|evening|night [--dry]]   (hyb_period.py)
"""
import os
import sys
import math
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

PR = PER.use_preset("lake")
R.WORK = PER.work("lake", R.WORK)            # period runs: work/periods/lake_<p>/
STAND = 1.0
W, H = P.W, P.H
G = R.grade_hex

# ------------------------------------------------------------------ geometry anchors of the mood
SUN_C, SUN_R = R.sun_rc(PR)                     # sun column / row (442, 82)
GAP_C = (398.0, 492.0)                          # open bay: screen columns without far shore
VAL_C = (402.0, 486.0)                          # valley in the hills / range under the sun
D_RANGE, D_HILL, D_BACK1, D_BACK2, D_SHORE = 2410.0, 710.0, 215.0, 265.0, 196.0

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)
MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)
WB = PR.water["bands"]                          # 6 bands, far (horizon) -> near (pier)
WSTEP = [PR.water["dark"]] + WB[::-1] + [PR.sky[0][1]]   # dark -> light ramp for +/- steps
REFL = PR.water["refl"]
RANGE = R.hazed(["#4c4866", "#5e5876"], D_RANGE, extra=-0.2)
HILL = R.hazed(G(["#34505a", "#46645e", "#627c66"]), D_HILL)
FOREST = R.hazed(G(["#13252a", "#1b3331", "#274636", "#3c5c38", "#7c7a40"]), D_SHORE, extra=0.06)
BACKF = R.hazed(G(["#1a363c", "#26484a", "#345a54", "#48695e"]), D_BACK2)
EARTH = G(["#342a2e", "#5a4640"])
ROCK = R.hazed(G(["#4a5c64", "#72848a"]), D_SHORE)
ROOF = G(["#34282c", "#7e4034"])
WALL = G(["#56443e", "#80603e", "#a8845a"])
WINDOW = PER.const("WINDOW", "#ffb85c")           # one warm lit window (the cabin is lived in)
INK = "#1c1622"
GLIT = PR.glitter["cols"]
BACK_PAL = (SKY_PAL + MSKY_PAL + WB + [PR.water["dark"]] + REFL + RANGE + HILL + FOREST + BACKF + EARTH + ROCK
            + ROOF + WALL + [WINDOW, INK] + GLIT)

WOOD = G(["#34232a", "#553a36", "#7a5440", "#9e7250", "#c49464"])    # a notch darker than retro16: evening
REED = G(["#1f3630", "#34523a", "#52723e", "#8a9a4e"])
LILY = G(["#345e44", "#568e4c", "#76ac52", "#aad066"])   # lighter than retro16: pads must clear the water
FLOWER = G(["#a04a70", "#e08aa8", "#f8d0dc"])
YELLOW = "#f0c848"
PAINT = G(["#23404c", "#336070", "#4a8488"])
METAL = G(["#4a5462", "#7c8898", "#b8c2cc"])
ROPE = G("#c8b07c")
FRONT_PAL = WOOD + REED + LILY + FLOWER + [YELLOW] + PAINT + METAL + [ROPE, INK]

LB = Vector(PR.key_dir).normalized()          # one key light for the whole stage (the sun side, lifted)

# back-layer overlay codes produced by the front pass (applied relative to the local water band)
OV_DARK, OV_WM1, OV_RIPPLE, OV_PADLINE = 1, 2, 3, 4
_OV = {}


def rc(p):
    return R.rc(p, STAND)


def col_of(x, y):
    return rc((x, y, 0.0))[0]


def gap_dist(col):
    """Screen distance (px) from the open bay: < 0 inside the gap."""
    return max(GAP_C[0] - col, col - GAP_C[1])


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


# ================================================================== BACK LAYER geometry
def back_scene(rnd):
    mt = R.m_tone
    # ---- distant range (2400 m): 2 hazy tones, a deep notch under the sun
    rm_ = mt(RANGE, [0.62], light=LB, name="Range")
    ridge_mesh("Range", rnd, y0=D_RANGE, depth=600, x0=-2800, x1=2800, step=28, hmax=78, hmin=34, mat=rm_, seed=2.0,
               valley=(VAL_C[0] + 14, VAL_C[1] - 18, 26, 0.1), kind="range")
    # ---- forested hills (700 m, hazy): crest rows ~79-85, open valley under the sun
    hm = mt(HILL, [0.46, 0.74], light=LB, noise=0.2, nscale=0.08, ncoord="world", ndetail=1.0,
            grads=((2, 22, 0, 0.12),), name="Hill")
    _, hx, hz = ridge_mesh("Hill", rnd, y0=D_HILL, depth=300, x0=-900, x1=900, step=14, hmax=21, hmin=12, mat=hm,
                           seed=4.0, smooth=True, valley=(VAL_C[0] - 16, VAL_C[1] + 14, 34, 0.0), kind="hill")
    bm_ = mt(HILL, [0.48, 0.74], light=LB, noise=0.12, nscale=1.5, ndetail=1.0, grads=((2, 22, 0, 0.12),), name="HillB")
    for k in range(380):
        x = rnd.uniform(-900, 900)
        zr = float(np.interp(x, hx, hz))
        if zr < 4.0:
            continue
        v = -rnd.random() ** 1.5 * 0.7
        z = zr * (1 - abs(v)) ** 1.35 - 2.0
        r = rnd.uniform(3.5, 6.5)
        ob = C.add_prim("ico", "HB", bm_, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, D_HILL + v * 150, z + r * 0.3)) @ Matrix.Diagonal((r * 1.2, r, r * 0.9, 1))
        C.set_smooth(ob)
        tagk(ob, "hill", "hills")
    # ---- far shore: two banks (left shore + right headland) around the open bay
    xl = R.col_to_x(GAP_C[0], 180.0, STAND)
    xr = R.col_to_x(GAP_C[1], 180.0, STAND)
    bank_m = mt(FOREST[1:4], [0.5, 0.78], light=LB, noise=0.12, nscale=0.4, ncoord="world", name="Bank")
    for (a, b, side) in ((-420.0, xl, -1), (xr, 420.0, 1)):
        verts, faces = [], []
        xs = np.arange(a, b + 0.01, 3.0)
        for x in xs:
            edge = (b - x) if side < 0 else (x - a)
            taper = min(1.0, max(0.12, edge / 9.0))
            zt = (2.2 + 0.5 * math.sin(x * 0.05) + rnd.uniform(-0.2, 0.2)) * taper
            yb = 320.0 if edge > 6 else 181.5 + (320 - 181.5) * max(0.0, edge) / 6.0
            verts += [(x, 179.5, 0.2), (x, 181.5, zt), (x, max(183.0, yb), zt + 2 * taper)]
        for i in range(len(xs) - 1):
            p, q = i * 3, (i + 1) * 3
            faces += [(p, q, q + 1, p + 1), (p + 1, q + 1, q + 2, p + 2)]
        tagk(R.mesh_from("Bank", verts, faces, bank_m, smooth=False), "bank")
    earth = mt(EARTH, [0.6], light=LB, name="Earth")
    tagk(R.box("CutL", ((-420 + xl - 1.5) / 2, 179.0, 0.2), (xl + 420 - 1.5, 1.2, 0.6), earth), "bank")
    tagk(R.box("CutR", ((xr + 1.5 + 420) / 2, 179.0, 0.2), (420 - xr - 1.5, 1.2, 0.6), earth), "bank")
    rm = mt(ROCK, [0.62], light=LB, name="Rock")
    for k in range(16):
        x = rnd.uniform(-130, 130)
        if gap_dist(col_of(x, 179)) < 4 or -52 < x < -28:
            continue
        r = rnd.uniform(0.6, 1.3)
        ob = C.add_prim("ico", "Rock", rm, radius=r, location=(x, 178.8, 0.1), subdivisions=1)
        ob.matrix_world = ob.matrix_world @ Matrix.Diagonal((1.6, 1.0, 0.7, 1))
        tagk(ob, "rock")
    # backlit far shore: the ramps sit low (bias < 0) so most of the crown is shade; the lit olive-gold only
    # shows on the sun-facing (right / top) side
    fol = mt(FOREST, [0.3, 0.5, 0.7, 0.88], light=LB, noise=0.13, nscale=2.6, ndetail=1.0, bias=-0.05, name="Fol")
    pinem = mt(FOREST[:4], [0.34, 0.56, 0.82], light=LB, noise=0.1, nscale=3.0, ndetail=1.0, bias=-0.05, name="Pine")
    shrubm = mt(FOREST[:4], [0.36, 0.6, 0.86], light=LB, noise=0.16, nscale=3.5, ndetail=1.0, bias=-0.08, name="Shrub")
    folb = mt(BACKF, [0.3, 0.55, 0.8], light=LB, noise=0.12, nscale=2.4, ndetail=1.0, name="FolB")
    pineb = mt(BACKF[:3], [0.36, 0.64], light=LB, noise=0.1, nscale=3.0, ndetail=1.0, name="PineB")
    trunk = mt(EARTH, [0.55], light=LB, name="Trunk")
    gid = [0]

    def plant(x, y, h, pine, back=False):
        gd = gap_dist(col_of(x, y))
        if gd < 3:
            return
        h *= min(1.0, max(0.45, gd / 26.0))               # trees shrink towards the points of the bay
        gid[0] += 1
        g = f"t{gid[0]}"
        if pine:
            conifer(x, y, 2.2, h, rnd, pineb if back else pinem, trunk, g)
        else:
            broadleaf(x, y, 2.2, h, rnd, folb if back else fol, trunk, g, shrubm if not back else None)

    # front row: low (4.5-7 m), irregular spacing, clumps of 2-3 trees and small clearings
    x = -150.0
    while x < 150:
        if not (-54 < x < -27):
            n = 1 if rnd.random() < 0.55 else rnd.randint(2, 3)
            for k in range(n):
                plant(x + k * rnd.uniform(1.6, 2.6), rnd.uniform(183, 189), rnd.uniform(4.5, 7.0), rnd.random() < 0.42)
            x += (n - 1) * 2.1
        x += rnd.uniform(2.6, 5.2)
    # shrub hedge along the bank: hides trunks, breaks the crown-on-stick look
    x = -150.0
    while x < 150:
        if not (-47 < x < -33) and gap_dist(col_of(x, 182)) > 2:
            gid[0] += 1
            shrub(x, rnd.uniform(181.8, 183.2), 2.2, rnd.uniform(0.9, 2.0), rnd, shrubm, f"s{gid[0]}")
        x += rnd.uniform(1.4, 2.6)
    # back bands (6-10 m), bluer, lighter and hazier
    for band, (ya, yb, dens, hmin, hmax) in enumerate(((193, 215, 0.55, 6.0, 8.5), (218, 290, 0.45, 7.0, 10.0))):
        x = -260.0
        while x < 260:
            plant(x, rnd.uniform(ya, yb), rnd.uniform(hmin, hmax), rnd.random() < 0.6, back=True)
            x += rnd.uniform(1.6, 3.4) / dens
    cabin(-40.0, 188.0, 2.2, rnd)
    tagk(R.hpoly("Water", [(-9000, -40), (9000, -40), (9000, 3200), (-9000, 3200)], 0.0, R.m_flat(WB[3]), 0.01), "water")


def ridge_mesh(name, rnd, y0, depth, x0, x1, step, hmax, hmin, mat, seed, smooth=False, valley=None, kind=None):
    """Ridge: grid in x (step) and y (7 rows), heights from summed sines + jitter. valley = (col0, col1,
    soft px, floor): heights fall to `floor` * height between the two screen columns."""
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
    tagk(R.loft("Trunk", [(x, y, z0 - 0.5), (x, y, z0 + h * 0.3)], h * 0.035, trunk, 6), "tree", grp)
    tiers = rnd.randint(5, 7)
    for k in range(tiers):
        t0 = 0.06 + 0.8 * k / tiers
        zc = z0 + h * t0
        r = h * 0.25 * (1 - 0.8 * k / tiers) * rnd.uniform(0.88, 1.1)
        th = h * 0.26
        n = 12
        verts = [(x, y, zc + th)]
        rot = rnd.uniform(0, 6.28)
        for i in range(n):
            a = rot + 2 * math.pi * i / n
            rr = r * (1.0 if i % 2 == 0 else 0.62) * rnd.uniform(0.85, 1.1)
            dz = -r * 0.28 if i % 2 == 0 else 0.0
            verts.append((x + rr * math.cos(a), y + rr * math.sin(a), zc + dz))
        verts.append((x, y, zc - r * 0.1))
        faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)] + [(n + 1, 1 + (i + 1) % n, 1 + i) for i in range(n)]
        ob = R.mesh_from("Tier", verts, faces, mat, smooth=True)
        R._fix_normals(ob)
        tagk(ob, "tree", grp)


def broadleaf(x, y, z0, h, rnd, mat, trunk, grp, shrubm=None):
    """Irregular crown of 9-13 puffs (wide, lopsided, low crown base) - no lollipop on a stick."""
    tagk(R.loft("Trunk", [(x, y, z0 - 0.5), (x + rnd.uniform(-0.2, 0.2), y, z0 + h * 0.45)], [h * 0.04, h * 0.028], trunk, 6),
         "tree", grp)
    cz = z0 + h * rnd.uniform(0.56, 0.64)
    lean = rnd.uniform(-0.12, 0.12) * h
    wide = rnd.uniform(0.22, 0.32)
    n = rnd.randint(9, 13)
    for k in range(n):
        u = rnd.uniform(-1, 1)
        v = rnd.uniform(-1, 1)
        w = rnd.uniform(-0.8, 1)
        r = h * rnd.uniform(0.11, 0.19) * (1.15 - 0.35 * max(0.0, w))
        c = (x + u * h * wide + lean * max(0.0, w), y + v * h * 0.15, cz + w * h * 0.24)
        ob = C.add_prim("ico", "Blob", mat, radius=1.0, location=(0, 0, 0), subdivisions=2)
        ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((r * 1.1, r, r * 0.85, 1))
        C.set_smooth(ob)
        tagk(ob, "tree", grp)
    if shrubm is not None:
        shrub(x + rnd.uniform(-0.6, 0.6), y - 1.2, z0, h * rnd.uniform(0.26, 0.36), rnd, shrubm, grp + "s")


def shrub(x, y, z0, h, rnd, mat, grp):
    for k in range(rnd.randint(3, 5)):
        r = h * rnd.uniform(0.45, 0.7)
        c = (x + rnd.uniform(-1.2, 1.2) * h, y + rnd.uniform(-0.4, 0.4), z0 + r * 0.55 + rnd.uniform(0, 0.25) * h)
        ob = C.add_prim("ico", "Shrub", mat, radius=1.0, location=(0, 0, 0), subdivisions=2)
        ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((r * 1.35, r, r * 0.8, 1))
        C.set_smooth(ob)
        tagk(ob, "shrub", grp)


def cabin(x, y, z, rnd):
    mt = R.m_tone
    logs = mt(WALL, [0.42, 0.72], light=LB, pats=((2, 0.75, 0.3, -0.22, "world"),), name="Logs")
    roof = mt(ROOF, [0.5], light=LB, pats=((0, 0.9, 0.25, -0.3, "world"),), name="Roof")
    g = "cabin"
    tagk(R.box("Walls", (x, y, z + 1.6), (7.0, 5.0, 3.2), logs), "cabin", g)
    hw, hl, zr, top = 4.2, 3.2, z + 3.2, z + 5.6
    v = [(x - hw, y - hl, zr), (x + hw, y - hl, zr), (x + hw, y + hl, zr), (x - hw, y + hl, zr),
         (x - hw, y, top), (x + hw, y, top)]
    f = [(0, 1, 5, 4), (3, 4, 5, 2), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]
    ob = R.mesh_from("Roof", v, f, roof, smooth=False)
    R._fix_normals(ob)
    tagk(ob, "cabin", g)
    tagk(R.box("Gable", (x + 3.52, y, z + 4.1), (0.05, 4.4, 1.6), logs), "cabin", g)
    tagk(R.box("Chimney", (x - 2.2, y + 1.0, z + 5.2), (0.9, 0.9, 2.4), mt(ROCK, [0.6], light=LB, name="Stone")), "cabin", g)
    tagk(R.box("Window", (x - 1.4, y - 2.52, z + 1.8), (1.3, 0.1, 1.0), R.m_flat(WINDOW)), "cabin", g)
    tagk(R.box("Door", (x + 1.6, y - 2.52, z + 1.1), (1.0, 0.1, 2.0), mt(EARTH, [0.6], light=LB, name="Door")), "cabin", g)
    planks = mt(WALL[1:], [0.7], light=LB, name="Dock")
    tagk(R.box("Dock", (x + 5.0, y - 5.0, 0.55), (2.2, 8.0, 0.25), planks), "dock", "dock")
    for dy in (-8.5, -5.0, -1.5):
        tagk(R.box("DPost", (x + 6.05, y + dy, 0.4), (0.25, 0.25, 1.4), R.m_tone(EARTH, [0.6], light=LB, name="Door")), "dock", "dock")
    tagk(R.box("Skiff", (x + 8.0, y - 6.5, 0.2), (1.3, 3.6, 0.45), mt(ROOF, [0.5], light=LB, name="Skiff"), bevel=0.3), "dock", "skiff")


# ================================================================== FRONT LAYER geometry
# lily pads: all at |x| >= 6 m, next to the reeds and the boat (the central corridor stays free for fish shadows)
PADS = [(-6.4, 15.2, 0.62, True), (-7.5, 16.4, 0.5, False), (-6.2, 17.2, 0.4, False),
        (6.3, 11.6, 0.62, False), (7.5, 12.8, 0.72, True), (6.1, 13.7, 0.42, False),
        (-10.6, 20.4, 0.8, False), (-9.3, 22.0, 0.7, True), (-11.9, 22.6, 0.72, False),
        (11.0, 23.6, 0.9, False), (12.6, 25.6, 0.8, True), (10.0, 27.4, 0.62, False)]
PAD_INFO = []
FLOWERS = []
CLUMP_BASES = []


def front_scene(rnd):
    mt = R.m_tone
    PAD_INFO.clear()
    FLOWERS.clear()
    CLUMP_BASES.clear()
    # ---- pier: individual planks with grain, dark base under the gaps
    tagk(R.box("DeckBase", (0, -8.5, STAND - 0.09), (2.62, 19.2, 0.12), R.m_flat(WOOD[0])), "pier", "pier0")
    nplank = 10
    pw = 2.6 / nplank
    for i in range(nplank):
        xc = -1.3 + pw * (i + 0.5)
        bias = rnd.uniform(-0.07, 0.07)
        pm = mt(WOOD[1:], [0.52, 0.72, 0.9], light=LB, bias=bias, noise=0.17, nscale=1.3,
                ncoord="object", nvec=(9.0, 0.35, 1.0), ndetail=1.0, name="Plank")
        y1 = 1.1 + rnd.uniform(-0.06, 0.04)
        tagk(R.box("Plank", (xc, (y1 - 18.0) / 2, STAND - 0.025 + rnd.uniform(-0.006, 0.006)), (pw - 0.028, y1 + 18.0, 0.05), pm),
             "plank", f"pl{i}")
    beam = mt(WOOD[:4], [0.4, 0.6, 0.8], light=LB, noise=0.1, nscale=2, nvec=(1, 0.2, 4), name="Beam")
    for sx in (-1.34, 1.34):
        tagk(R.box("Stringer", (sx, -8.5, STAND - 0.12), (0.1, 19.2, 0.22), beam), "pier", f"st{sx}")
    postm = mt(WOOD[:4], [0.34, 0.55, 0.8], light=LB, noise=0.08, nscale=3, nvec=(1, 1, 0.15), name="Post")
    ropem = mt([WOOD[3], ROPE], [0.55], light=LB, pats=((2, 0.035, 0.45, -0.3, "world"),), name="Rope")
    posts = []
    for (px, py, top) in ((-1.38, 0.95, STAND + 0.62), (1.38, 0.95, STAND + 0.62), (-1.38, -4.0, STAND + 0.2),
                          (1.38, -4.0, STAND + 0.2), (-1.38, -9.0, STAND + 0.2), (1.38, -9.0, STAND + 0.2)):
        ob = R.loft("Post", [(px, py, -1.2), (px, py, top - 0.04), (px, py, top)], [0.11, 0.11, 0.085], postm, 12)
        posts.append(tagk(ob, "post", f"po{px}{py}"))
        if top > STAND + 0.4:
            posts.append(tagk(R.loft("RopeWrap", [(px, py, top - 0.33), (px, py, top - 0.18)], 0.122, ropem, 12), "post", f"po{px}{py}"))
            CLUMP_BASES.append((px, py, 0.16))
    rp = [(1.38, 0.95 + 0.1, STAND + 0.3), (1.45, 1.4, STAND - 0.1), (1.5, 1.7, 0.2), (1.55, 1.9, -0.1)]
    posts.append(tagk(R.loft("Rope", rp, 0.022, mt([WOOD[3], ROPE], [0.55], light=LB, name="Rope2"), 6), "post", "ropeline"))
    # bucket + tackle box on the deck, left of the angler
    metal = mt(METAL, [0.45, 0.76], light=LB, pats=((2, 0.12, 0.18, -0.25, "world"),), name="Metal")
    bx, by = -0.82, -1.25
    tagk(R.loft("Bucket", [(bx, by, STAND), (bx, by, STAND + 0.38)], [0.17, 0.2], metal, 16, caps=True), "prop", "bucket")
    tagk(R.hpoly("BWater", R.ellipse_pts(bx, by, 0.175, 0.175, 16), STAND + 0.39, R.m_flat(PAINT[1]), 0.005), "prop", "bucketw")
    tagk(R.loft("Bail", [(bx - 0.2, by, STAND + 0.36), (bx - 0.12, by - 0.05, STAND + 0.55), (bx, by - 0.07, STAND + 0.6),
                         (bx + 0.12, by - 0.05, STAND + 0.55), (bx + 0.2, by, STAND + 0.36)], 0.012, R.m_flat(METAL[0]), 6), "prop", "bail")
    paint = mt(PAINT, [0.45, 0.72], light=LB, name="Paint")
    tagk(R.box("Tackle", (-0.72, -0.35, STAND + 0.1), (0.46, 0.26, 0.2), paint, bevel=0.015), "prop", "tackle")
    tagk(R.box("TLid", (-0.72, -0.35, STAND + 0.215), (0.47, 0.27, 0.035), mt(PAINT[1:], [0.6], light=LB, name="Paint2"), bevel=0.01), "prop", "tackle")
    tagk(R.loft("THandle", [(-0.84, -0.35, STAND + 0.23), (-0.8, -0.35, STAND + 0.29), (-0.64, -0.35, STAND + 0.29),
                            (-0.6, -0.35, STAND + 0.23)], 0.014, R.m_flat(METAL[1]), 6), "prop", "tackleh")
    tagk(R.box("Latch", (-0.72, -0.483, STAND + 0.17), (0.06, 0.01, 0.05), R.m_flat(METAL[2])), "prop", "tackle")
    # ---- reeds (clumps framing the sides, centre left open)
    catm = mt(WOOD[1:4], [0.5, 0.8], light=LB, name="Cattail")
    refl = []
    for (cx, cy, n, spread, hmin, hmax) in ((-5.6, 3.0, 46, 0.8, 1.0, 1.9), (6.6, 5.2, 40, 0.85, 1.0, 1.8),
                                            (-12.5, 17.0, 38, 1.4, 1.5, 2.5), (14.5, 28.0, 34, 1.6, 1.8, 2.8)):
        refl += reed_clump(cx, cy, n, spread, hmin, hmax, rnd, catm)
    # ---- lily pads (flat mask geometry; shaded by hand in post)
    padm = R.m_flat(LILY[2])
    for (x, y, r, fl) in PADS:
        rot = -math.pi / 2 + rnd.uniform(-0.9, 0.9)       # notch roughly towards the viewer so the V reads
        lily(x, y, r, rot, padm, rnd)
        if fl:
            FLOWERS.append((x + r * 0.3 * math.cos(rot + 2.6), y + r * 0.3 * math.sin(rot + 2.6)))
    # ---- moored clinker rowboat + stake
    refl += rowboat(-8.8, 10.5, math.radians(28), rnd)
    sx_, sy_ = -10.4, 13.0
    stake = tagk(R.loft("Stake", [(sx_, sy_, -0.6), (sx_, sy_, 0.8), (sx_, sy_, 0.86)], [0.075, 0.075, 0.05], postm, 8), "post", "stake")
    refl.append(stake)
    CLUMP_BASES.append((sx_, sy_, 0.12))
    bow = Matrix.Translation((-8.8, 10.5, 0.0)) @ Matrix.Rotation(math.radians(28), 4, "Z") @ Vector((0, 1.6, 0.5))
    rope = [(bow.x + (sx_ - bow.x) * k / 6, bow.y + (sy_ - bow.y) * k / 6,
             bow.z + (0.66 - bow.z) * k / 6 - 0.3 * math.sin(math.pi * k / 6)) for k in range(7)]
    tagk(R.loft("MoorRope", rope, 0.016, mt(WOOD[2:4], [0.55], light=LB, name="Rope3"), 6), "post", "moor")
    return refl + posts


def reed_material(hz, bias):
    """Two value zones: dark base below hz (REED 0/1), lighter tips above (REED 2/3); lambert picks
    the tone inside each zone. Flat-shaded camera-facing ribbons -> one tone per blade."""
    return R.m_tone(REED, [0.25, 0.5, 0.75], light=LB, lam=0.4, bias=bias,
                    grads=((2, hz * 0.96, hz * 1.04, 0.5, "world"),), name="Reed")


def ribbon(name, pts, widths, mat, th=0.006):
    """Closed thin ribbon along pts whose broad faces always face the camera (width axis = t x Y)."""
    P3 = [Vector(p) for p in pts]
    n = len(P3)
    verts, faces = [], []
    segs = 6
    for i, p in enumerate(P3):
        t = (P3[min(i + 1, n - 1)] - P3[max(i - 1, 0)]).normalized()
        a = t.cross(Vector((0, 1, 0)))
        if a.length < 1e-4:
            a = Vector((1, 0, 0))
        a.normalize()
        b = t.cross(a).normalized()
        for k in range(segs):
            ang = 2 * math.pi * k / segs
            verts.append(p + a * (math.cos(ang) * widths[i]) + b * (math.sin(ang) * th))
    for i in range(n - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append((i * segs + k, i * segs + k2, (i + 1) * segs + k2, (i + 1) * segs + k))
    ob = R.mesh_from(name, verts, faces, mat, smooth=False)
    R._fix_normals(ob)
    return ob


def blade(x, y, h, dirx, diry, bend, a0, a1, mat, droop=0.55, nseg=10):
    """Tapered ribbon that rises and arches over, the tip drooping. a0/a1: base/tip half-width (m)."""
    pts, ws = [], []
    for k in range(nseg):
        t = k / (nseg - 1)
        off = bend * h * 0.8 * t * t
        z = h * (t - bend * droop * t ** 3)
        pts.append((x + dirx * off, y + diry * off, z))
        ws.append(a0 * (1 - t) + a1 * t)
    return ribbon("Blade", pts, ws, mat)


def reed_clump(cx, cy, n, spread, hmin, hmax, rnd, catm):
    objs = []
    depth = P.project((cx, cy, 0), STAND)[2]
    mpp = depth / P.F_PX                       # metres per pixel at the clump
    hz = 0.4 * (hmin + hmax) / 2               # dark base zone: ~40 % of the height
    mats = [reed_material(hz, b) for b in (-0.06, 0.0, 0.05)]
    irism = R.m_tone(LILY[:3], [0.45, 0.75], light=LB, name="Iris")
    nfan = max(4, n // 7)
    bases = []
    for f in range(nfan):
        side = -1 if rnd.random() < 0.5 else 1
        bases.append((cx + rnd.gauss(0, spread * 0.6), cy + rnd.gauss(0, spread * 0.3), side))
        CLUMP_BASES.append((bases[-1][0], bases[-1][1], spread * 0.3))
    ncat = max(1, int(round(n * rnd.uniform(0.1, 0.15))))
    a_tip = 0.52 * mpp
    a_base = max(0.016, 0.95 * mpp)
    for i in range(n):
        bx, by, side = bases[i % nfan]
        g = f"reed{cx}_{i % nfan}"             # one group per fan: no contour lines inside a fan
        x = bx + rnd.gauss(0, 0.05)
        y = by + rnd.gauss(0, 0.05)
        h = rnd.uniform(hmin, hmax)
        mat = mats[rnd.randrange(3)]
        if i < ncat:
            lean = rnd.uniform(-0.08, 0.08)
            top = (x + lean * h, y, h)
            objs.append(tagk(ribbon("Stem", [(x, y, -0.2), (x + lean * h * 0.5, y, h * 0.5), top], [a_tip] * 3, mat), "reed", g))
            hl = 5.0 * mpp
            t1 = 0.86
            t0 = t1 - hl / h
            a = (x + lean * h * t0, y, h * t0)
            b = (x + lean * h * t1, y, h * t1)
            objs.append(tagk(R.loft("Head", [a, b], [1.0 * mpp, 1.0 * mpp], catm, 8), "cattail", g))
            objs.append(tagk(ribbon("Spike", [b, top], [0.5 * mpp, 0.5 * mpp], mat), "reed", g))
        elif rnd.random() < 0.6:
            dx = side * rnd.uniform(0.25, 1.0) if rnd.random() < 0.8 else -side * rnd.uniform(0.2, 0.6)
            bend = rnd.uniform(0.35, 0.8)
            objs.append(tagk(blade(x, y, h * rnd.uniform(0.65, 1.0), dx, rnd.uniform(-0.3, 0.3), bend,
                                   a_base, a_tip, mat), "reed", g))
        else:
            dx = rnd.uniform(-1, 1)
            objs.append(tagk(blade(x, y, h, dx, rnd.uniform(-0.3, 0.3), rnd.uniform(0.04, 0.2),
                                   a_base, a_tip, mat, droop=0.3), "reed", g))
    for k in range(rnd.randint(3, 5)):
        bx, by, side = bases[k % nfan]
        hh = rnd.uniform(0.4, 0.62) * hmin
        objs.append(tagk(blade(bx + rnd.gauss(0, 0.12), by - 0.12, hh, rnd.uniform(-0.6, 0.6), 0.0, rnd.uniform(0.06, 0.16),
                               max(0.03, 1.5 * mpp), 0.52 * mpp, irism, droop=0.1, nseg=6), "reed", f"iris{cx}_{k}"))
    return objs


def lily(x, y, r, rot, padm, rnd):
    n = 22
    verts = [(x, y, 0.03)]
    for i in range(n):
        a = rot + 2 * math.pi * i / n
        rr = r * (0.08 if i == 0 else 1.0) * rnd.uniform(0.97, 1.03)
        verts.append((x + rr * math.cos(a), y + rr * math.sin(a), 0.012))
    faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)]
    ob = R.mesh_from("Pad", verts, faces, padm, smooth=True)
    R._fix_normals(ob)
    tagk(ob, "pad", f"pad{x}{y}")
    PAD_INFO.append(dict(ob=ob, x=x, y=y, r=r, rot=rot))


def boat_material():
    """Clinker hull: painted teal outside (strake lines), bare wood inside (Backfacing)."""
    key = ("boat",)
    m = R._cached(key)
    if m is not None:
        return m
    R._use(PAINT + WOOD)
    m = bpy.data.materials.new("Boat")
    nb = R.NB(m)
    f = R._factor_lambert(nb, LB, 0.5)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["Object"])
    strake = nb.math("LESS_THAN", nb.math("FRACT", nb.math("DIVIDE", s[2], 0.13)), 0.22)
    fo = nb.math("MULTIPLY_ADD", strake, -0.25, f)
    outc = R.band_ramp(nb, fo, PAINT, [0.42, 0.7])
    inc = R.band_ramp(nb, f, WOOD[1:], [0.3, 0.55, 0.8])
    geo = nb.node("ShaderNodeNewGeometry")
    col = nb.mix(geo.outputs["Backfacing"], outc, inc)
    nb.output_emission(col, 1.0)
    R._mats[key] = m
    return m


def rowboat(cx, cy, rot, rnd):
    L, Wd = 3.3, 1.25
    objs = []
    M = Matrix.Translation((cx, cy, 0.0)) @ Matrix.Rotation(rot, 4, "Z") @ Matrix.Rotation(math.radians(3), 4, "Y")
    nu, nv = 16, 9
    verts, faces = [], []
    for i in range(nu + 1):
        u = -L / 2 + L * i / nu
        k = (u + L / 2) / L
        half = Wd / 2 * (max(0.0, 1 - max(0.0, (k - 0.55) / 0.45) ** 1.6) ** 0.55) * (0.72 + 0.28 * math.sin(min(1, k * 1.6) * math.pi / 2))
        half = max(half, 0.015)
        gun = 0.36 + 0.18 * max(0.0, k - 0.6) ** 2 * 4
        keel = -0.24 + 0.12 * max(0.0, k - 0.7) * 3
        for j in range(nv + 1):
            a = -math.pi / 2 + math.pi * j / nv
            verts.append((half * math.sin(a), u, gun - (gun - keel) * math.cos(a) ** 1.3))
    for i in range(nu):
        for j in range(nv):
            a = i * (nv + 1) + j
            b = (i + 1) * (nv + 1) + j
            faces.append((a, a + 1, b + 1, b))
    faces.append(tuple(range(nv, -1, -1)))
    hull = R.mesh_from("Hull", [tuple(M @ Vector(v)) for v in verts], faces, boat_material(), smooth=True)
    objs.append(tagk(hull, "boat", "boat"))
    wood = R.m_tone(WOOD[:4], [0.38, 0.6, 0.8], light=LB, name="BoatWood")
    for sgn in (-1, 1):
        pts = []
        for i in range(nu + 1):
            base = i * (nv + 1) + (0 if sgn < 0 else nv)
            pts.append(M @ Vector(verts[base]))
        objs.append(tagk(R.loft("Gunwale", pts, 0.035, wood, 6), "boat", "boat"))
    for uy, wdt in ((-0.75, 1.0), (0.25, 1.05), (1.0, 0.7)):
        objs.append(tagk(R.box("Thwart", tuple(M @ Vector((0, uy, 0.2))), (wdt, 0.22, 0.05), wood,
                               rot=Matrix.Rotation(rot, 4, "Z")), "boat", "boat"))
    for sgn in (-1, 1):
        a = M @ Vector((sgn * 0.25, -1.2, 0.05))
        b = M @ Vector((sgn * 0.35, 1.1, 0.28))
        objs.append(tagk(R.loft("Oar", [a, b], 0.03, wood, 6), "boat", "boat"))
        blade_ = M @ Vector((sgn * 0.24, -1.1, 0.07))
        objs.append(tagk(R.box("Blade", tuple(blade_), (0.16, 0.5, 0.025), wood, rot=Matrix.Rotation(rot, 4, "Z")), "boat", "boat"))
    return objs


# ================================================================== back composite
# water depth zones (start row, end row) for WB[i] -> WB[i+1]; the play area (rows >= 140) stays calm teal-blue
WATER_Z = [(107, 111), (113, 119), (129, 138), (165, 178), (221, 240)]
ROW_H, ROW_P = 106, 335            # horizon-side / pier-side reference rows for the dash length


def draw_dash(img_idx, r, c0, n, v, mask=None):
    r = int(r)
    if r < 0 or r >= img_idx.shape[0]:
        return
    for c in range(int(c0), int(c0) + max(1, int(n))):
        if 0 <= c < img_idx.shape[1] and (mask is None or mask[r, c]):
            img_idx[r, c] = v


def reflect_tint(ref_hex, k, dark):
    """Colour transform for reflections: towards the water colour by k, then darker (linear x dark)."""
    wl = R.s2l(R.hexrgb(ref_hex))

    def fn(c):
        return R.l2s((R.s2l(c) * (1 - k) + wl[None, :] * k) * dark)
    return fn


def far_reflection(pal, idx, water, wband, refl_objs):
    """EXACT reflection of every far layer (mirror pass) in the water: the layers' own palette colours,
    tinted towards the far water and darkened; runs >= 3 px; a light break line every third row (the
    local band shows through); dissolving over the bottom 25 % of its height."""
    mp = R.mirror_pass(refl_objs, tag="farmirror")
    rng = random.Random(7)
    ra = mp["a"] & water
    midx, _ = R.quantize(mp["rgb"], ra, pal, dither=False)
    lab = R.min_runs(np.where(ra, midx + 1, 0), 3)
    top = np.argmax(water, 0)
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
        p = np.clip((frac[r] - 0.75) / 0.25, 0, 1)
        brk = R.run_noise(rng, W, 3, 8) < p
        keep = m & ~brk & water[r]
        out[r, keep] = lab[r, keep] - 1
        keepm[r] = keep
        if (r - int(np.median(top))) % 3 == 2:
            linem[r] = R.dash_mask(W, 0.4, 6, 20, rng) & keep
    # break lines: the SAME reflected colour pushed further towards the water (a lighter ripple line),
    # never the raw band colour (that read as bright noise against the dark mirror image)
    body = keepm & ~linem
    out2, pal = R.recolour(out, pal, body, reflect_tint(WB[2], 0.25, 0.78), snap=0.03)
    out2, pal = R.recolour(out2, pal, linem, reflect_tint(WB[2], 0.55, 0.92), snap=0.03)
    return out2, pal, keepm


def bay_sky(pal, idx, water, wband, shore_row):
    """The far water of the open bay mirrors the sky exactly (bands + glow rings, no disc), tinted towards
    the far water, joined to the bands by dash dithering; retro16 light break lines every third row."""
    rng = random.Random(21)
    cols = np.arange(W)[None, :]
    zone = water & (np.arange(H)[:, None] < shore_row + 5) & (cols > GAP_C[0] - 24) & (cols < GAP_C[1] + 24)
    thr = R.dash_threshold(H, W, rng, 2, 7)
    m_idx, _ = R.quantize(MSKY_RGB, zone, pal, dither=True, thr=thr)
    rows = np.arange(H)[:, None]
    fade = np.clip((rows - (shore_row - 1)) / 6.0, 0, 1)            # hand over to the bands below the shore
    use = zone & (R.dash_threshold(H, W, rng, 3, 9) >= fade)
    out = idx.copy()
    out[use] = m_idx[use]
    out, pal = R.recolour(out, pal, use, reflect_tint(WB[0], 0.22, 0.9), snap=0.02)
    for r in range(int(R.HORIZON_ROW), int(shore_row) + 5):
        if r % 3 == 1:
            bl = R.dash_mask(W, 0.35, 5, 16, rng) & use[r]
            out[r, bl] = wband[r, bl]
    return out, pal, use


def wave_marks(pal, idx, water, avoid, wband):
    """Sparse wave marks: a light dash (water +1) with a darker dash (water -1) one row below, 1 px right.
    Density is cut in the central play rectangle and far away."""
    rng = random.Random(11)
    shore_row = rc((0, 179.6, 0))[1]
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    dn1 = R.ramp_step(pal, WSTEP, wband, -1)
    for k in range(320):
        y = 1.0 + 170 * rng.random() ** 1.8
        x = rng.uniform(-1, 1) * (y + 12) * 0.75
        c, r = rc((x, y, 0))
        if r <= shore_row + 2 or r >= H - 1 or c < 0 or c >= W:
            continue
        ri, ci = int(r), int(c)
        if avoid[ri, ci]:
            continue
        central = 190 < c < 450 and 140 < r < 330
        if central and rng.random() < 0.8:
            continue
        if y > 90 and rng.random() < 0.55:
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = min(16, max(2, int(round(520 / depth * rng.uniform(0.25, 0.6)))))
        ok = water & ~avoid
        draw_dash(idx, r, c, n, int(up1[ri, ci]), ok)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), int(dn1[min(H - 1, ri + 1), ci]), ok)
    return idx


def far_rims(idx, pal, kid):
    """Warm rim on the silhouette tops / sun-side edges of the far layers, only near the sun (glow)."""
    lv, _ = R.glow_level(PR)
    prox = np.clip(lv / 0.4, 0, 1)
    rim = PR.rim
    out, p2 = idx, pal
    for kinds in (("range",), ("hill",), ("tree", "shrub", "cabin")):
        m = np.isin(kid, kinds)
        up_out = m & ~R.shift(m, 1, 0, False)
        side_out = m & ~R.shift(m, 0, -1, False) if R.sun_side(PR) == "right" else m & ~R.shift(m, 0, 1, False)
        k = (up_out * 1.0 + side_out * 0.6).clip(0, 1) * prox
        out, p2 = R.blend_idx(out, p2, k > 0.55, rim["col"], 0.55, lighter=True, snap=0.025)
        out, p2 = R.blend_idx(out, p2, (k > 0.22) & (k <= 0.55), rim["col"], 0.3, lighter=True, snap=0.025)
    return out, p2


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    PER.hook("back_scene", rnd=rnd, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("lake_back")
    kid = R.kind_map(ps)
    sky = ~ps["a"]
    water = kid == "water"
    tree = (kid == "tree") | (kid == "shrub")
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = SKY_RGB[sky]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid | sky, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=3.0, rel=0.015, steps=1)
    idx = np.where(tree | (kid == "cabin") | (kid == "dock"), lined, idx)
    idx, pal = far_rims(idx, pal, kid)
    # ---- water: depth bands with horizontal dash dithering
    wband = R.water_bands(pal, WB, WATER_Z, random.Random(5), ROW_H, ROW_P)
    idx[water] = wband[water]
    shore_row = rc((0, 179.6, 0))[1]
    idx, pal, baym = bay_sky(pal, idx, water, wband, shore_row)
    # ---- exact reflections of the far layers
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("tree", "shrub", "bank", "cabin", "rock", "dock",
                                                                      "hill", "range")]
    idx, pal, farm = far_reflection(pal, idx, water, wband, refl_objs)
    # ---- front-object reflections / ripples / pad lines (computed by the front pass)
    ov = _OV.get("ov")
    if ov is None:
        p = os.path.join(R.WORK, "front_overlay.npy")
        ov = np.load(p) if os.path.exists(p) else np.zeros((H, W), np.int8)
    g_am, g_br = R.glitter_masks(PR, water, random.Random(17), avoid=(ov > 0))
    idx = wave_marks(pal, idx, water, farm | (ov > 0) | g_am | g_br, wband)
    idx[water & (ov == OV_DARK)] = pal.index(REFL[0])
    for code, d in ((OV_WM1, -1), (OV_RIPPLE, +1), (OV_PADLINE, -1)):
        m = water & (ov == code)
        idx[m] = R.ramp_step(pal, WSTEP, wband, d)[m]
    # ---- sun path: a faint warm sheen + sparse short glitter dashes, far third only
    rows = np.arange(H)[:, None]
    wc = np.flatnonzero(water[:, int(SUN_C)])
    r0 = wc.min()
    r_end = r0 + PR.glitter["far"] * (wc.max() - r0)
    t = np.clip((rows - r0) / (r_end - r0), 0, 1)
    halfw = PR.glitter["width"][0] + (PR.glitter["width"][1] - PR.glitter["width"][0]) * t
    cols = np.arange(W)[None, :]
    sheen = 0.75 * (1 - t) ** 1.5 * np.exp(-((cols - SUN_C) / (1.7 * halfw)) ** 2) * (rows < r_end)
    sm = water & (sheen > R.dash_threshold(H, W, random.Random(23), 2, 6)) & (ov == 0)
    idx, pal = R.blend_idx(idx, pal, sm, GLIT[0], 0.28, lighter=True, snap=0.025)
    idx[g_am] = pal.index(GLIT[0])
    idx[g_br] = pal.index(GLIT[1])
    # ---- thin mist band on the far waterline (dash dithered, wisps)
    land = ~sky & ~water
    line = np.array([np.flatnonzero(water[:, c]).min() if water[:, c].any() else H for c in range(W)], float)
    ma = R.mist_amount(PR, line, land, water)
    thr = R.dash_threshold(H, W, random.Random(29), 2, 9)
    idx, pal = R.blend_idx(idx, pal, (ma > thr) & (ma > 0.08), PR.mist["col"], 0.5, snap=0.025)
    idx, pal = PER.hook("back_post", idx, pal, kid=kid, water=water, sky=sky, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, "lake", "back")
    # readability check: the play-area water band vs a fish shadow drawn the game's way
    wl = R.lum(WB[4])
    print("HYB back colours", R.count_colours(img), "palette", len(pal), "water L", round(wl, 3))
    return pal


# ---------------------------------------------------------------- front post helpers
FLOWER_STAMP = ["..L..",
                ".LML.",
                "LMYML",
                "DDDDD"]


def shade_pads(idx, pal, ps):
    """Hand-style pad shading: sun-side half LILY[2], other half LILY[1], 1 px lit rim on the sun-facing
    top/side edge LILY[3], 1-2 px vein LILY[0]. Returns the union pad mask."""
    allm = np.zeros(idx.shape, bool)
    right = R.sun_side(PR) == "right"
    for info in PAD_INFO:
        pid = info["ob"].pass_index
        m = ps["id"] == pid
        if not m.any():
            continue
        allm |= m
        cx, cy = rc((info["x"], info["y"], 0.02))
        cols = np.arange(W)[None, :]
        lit_half = (cols >= int(round(cx))) if right else (cols < int(round(cx)))
        idx[m] = pal.index(LILY[1])
        idx[m & lit_half] = pal.index(LILY[2])
        up = np.zeros_like(m)
        up[1:] = m[:-1]
        sd = np.zeros_like(m)
        if right:
            sd[:, :-1] = m[:, 1:]
        else:
            sd[:, 1:] = m[:, :-1]
        rim = m & ((~up) | (~sd)) & lit_half & ((cols > cx + 1) if right else (cols < cx - 1))
        idx[rim] = pal.index(LILY[3])
        rot = info["rot"]
        r = info["r"]
        n = 5
        for k in range(1, n + 1):
            t = 0.12 + 0.5 * k / n
            vx, vy = rc((info["x"] - r * t * math.cos(rot), info["y"] - r * t * math.sin(rot), 0.02))
            vi, vj = int(vy), int(vx)
            if 0 <= vi < H and 0 <= vj < W and m[vi, vj]:
                idx[vi, vj] = pal.index(LILY[0])
    return allm


def stamp_flowers(idx, pal):
    cmap = {"L": FLOWER[2], "M": FLOWER[1], "D": FLOWER[0], "Y": YELLOW}
    for (fx, fy) in FLOWERS:
        c, r = rc((fx, fy, 0.05))
        x0, y0 = int(round(c)) - 2, int(round(r)) - 3
        for j, ln in enumerate(FLOWER_STAMP):
            for i, ch in enumerate(ln):
                if ch in cmap and 0 <= y0 + j < H and 0 <= x0 + i < W:
                    idx[y0 + j, x0 + i] = pal.index(cmap[ch])


def contact_shadow(idx, pal, kid):
    """14x3 px ellipse of WOOD[1] on the planks at the projected feet point."""
    fc, fr = rc((0, 0, STAND))
    cx, cy = int(round(fc - 0.5)), int(fr) + 1
    woods = [pal.index(c) for c in WOOD[2:]]
    for dy, hw in ((-1, 4), (0, 7), (1, 4)):
        for dx in range(-hw, hw):
            y, x = cy + dy, cx + dx
            if 0 <= y < H and 0 <= x < W and idx[y, x] in woods and kid[y, x] == "plank":
                idx[y, x] = pal.index(WOOD[1])


def front_reflections(refl):
    """Reflections of every front object standing in water -> back-layer overlay codes.
    2 tones (dark reed-teal / water -1), per-row +-1 px shift only for runs >= 4 px, dash breaks
    every 2nd/3rd row, fade over 60 % of the reflected height."""
    mp = R.mirror_pass(refl, ids=True, tag="frontmirror")
    rng = random.Random(3)
    a = mp["a"]
    ids = mp["id"]
    z = R.world_z(mp["depth"], STAND)
    zmin = {}
    for i in np.unique(ids[a]):
        zmin[int(i)] = float(np.min(z[a & (ids == i)]))
    zm = np.vectorize(lambda i: zmin.get(int(i), -1.0))(ids)
    frac = np.clip(z / np.minimum(zm, -1e-3), 0, 1)
    frac[~a] = 0
    lumv = (mp["rgb"] * np.array([0.3, 0.55, 0.15])).sum(-1)
    code = np.where(a, np.where(lumv < 0.36, OV_DARK, OV_WM1), 0)
    code[a & (z > -0.02)] = 0
    p = np.clip(frac / 0.6, 0, 1) ** 1.3
    shift = [(0, 1, 1, 0, -1, -1)[r % 6] for r in range(H)]
    out = np.zeros((H, W), np.int8)
    for r in range(H):
        row = code[r]
        if not row.any():
            continue
        brk = R.run_noise(rng, W, 2, 6) < p[r]
        row = np.where(brk, 0, row)
        if r % 5 in (0, 2):
            row = np.where(R.dash_mask(W, 0.45, 2, 6, rng), 0, row)
        m = row > 0
        edges = np.flatnonzero(np.diff(np.concatenate([[0], m.astype(int), [0]])))
        for s, e in zip(edges[::2], edges[1::2]):
            d = shift[r] if e - s >= 4 else 0
            s2, e2 = max(0, s + d), min(W, e + d)
            out[r, s2:e2] = row[s2 - d:e2 - d]
    return out


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    refl = front_scene(rnd)
    PER.hook("front_scene", rnd=rnd, refl=refl, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("lake_front")
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    thin = (kid == "reed") | (kid == "cattail")
    idx = R.despeckle(idx, ps["id"], protect=~solid | thin, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    padm = shade_pads(idx, pal, ps)
    contact_shadow(idx, pal, kid)
    stamp_flowers(idx, pal)
    # warm rim on the OUTER sun-side silhouette of solid props (posts, boat, bucket, tackle, pier edge);
    # computed before the outline so it sits on the object's own edge pixels
    solid_k = np.isin(kid, ["post", "boat", "prop", "pier", "plank"])
    idx, pal = R.rim_light(idx, pal, PR, mask=solid_k)
    # selective outline for solid props only (hue-shifted darker neighbour, never ink); reeds and pads get none
    noline = thin | padm
    base = np.where(noline, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    idx = np.where(noline & (idx >= 0), idx, ol)
    # ---- back-layer overlays: reflections, ripple dashes at stems / posts, 1 px dark water line under pads
    ov = front_reflections(refl)
    rng = random.Random(5)
    for (x, y, rad) in CLUMP_BASES:
        c, r = rc((x, y, 0))
        depth = P.project((x, y, 0), STAND)[2]
        half = rad * P.F_PX / depth
        n = max(2, int(520 / depth * 0.22))
        for dr, sc in ((0, 1.0), (1, 0.6), (2, 1.25)):
            rr = int(r + dr)
            if not 0 <= rr < H or rng.random() < 0.25:
                continue
            for side in (-1, 1):
                c0 = c + side * (half * sc + rng.uniform(1, 3)) - (n if side < 0 else 0)
                for cc in range(int(c0), int(c0 + n)):
                    if 0 <= cc < W:
                        ov[rr, cc] = OV_RIPPLE
    below = np.zeros_like(padm)
    below[1:] = padm[:-1]
    ov[below & ~padm] = OV_PADLINE
    _OV["ov"] = ov
    np.save(os.path.join(R.WORK, "front_overlay.npy"), ov)
    idx, pal = PER.hook("front_post", idx, pal, kid=kid, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, "lake", "front")
    pl = [R.lum(c) for c in LILY[1:]]
    print("HYB front colours", R.count_colours(img), "palette", len(pal), "pad L min", round(min(pl), 3),
          "vs water L", round(R.lum(WB[3]), 3), round(R.lum(WB[4]), 3))


def main():
    C.reset_scene()
    which = PER.which()                      # back / front (+ --period <p> [--dry], see hyb_period)
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    PER.stage_json("lake", PR, clouds=False, birds=True)
    print("HYB LAKE done")


if __name__ == "__main__":
    main()
