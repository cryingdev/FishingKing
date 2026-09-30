"""
retro16 - the lake stage in the classic 16-bit style.

Back layer (opaque): banded sky + hazy mountain tips + forested hills + a low far-shore tree line
(front row of mixed broadleaf/conifer over a continuous shrub hedge, a bluer/lighter back band, cabin
and dock), then the water: 6 depth bands joined by HORIZONTAL DASH DITHERING (runs of the next band
colour, 2-3 px near the horizon growing to 8-16 px near the pier, density 0->100 % across each zone,
random offsets, no 4x4 grid), a 2-colour far-shore reflection (runs >= 3 px, light break lines every
third row, dissolving over its bottom 25 %), sparse wave marks, and the water reflections / ripples /
pad shadow lines of every front object (those live in the BACK layer so fish shadows draw over them).
Front layer (transparent, solid objects only): plank pier with posts and rope, bucket + tackle box, a
contact shadow at the angler's feet, reed clumps (arched blade fans, cattails, iris leaves; no outer
outline), hand-shaded notched lily pads with 5x4 flower stamps, a moored clinker rowboat.

Same camera (fk_persp.setup_camera(1.0)), same 640x400 canvas and gameplay layout as fk_stages.stage_lake.
Outputs: _tmp/variants/retro16/lake_back.png, lake_front.png, lake.json
Run: blender -b --python variants/retro16/r16_lake.py
"""
import os
import sys
import math
import json
import random
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import r16_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

STAND = 1.0
W, H = P.W, P.H

# ------------------------------------------------------------------ curated palettes (back: 32 colours)
SKY = ["#3c5ca8", "#6088c8", "#7ca2d6", "#9cbee0", "#bcd4e4", "#d8e6e6"]              # top -> horizon
# water: desaturated (-20 % chroma) and turned ~8 deg towards teal; deep -> light. No colour shared with SKY.
WATER = ["#22486b", "#2c577e", "#3a678f", "#4a789f", "#5d88ad", "#759cba", "#96b6ca"]
WBANDS = WATER[::-1][:6]                        # horizon -> pier (the darkest is only a 'water -1' tone)
WSTEP = WATER + ["#bcd4e4"]                     # ramp for +/- steps (sparkle cap above the lightest band)
REFL = ["#2c4a50", "#3a5e6a"]                   # reflection tones (dark teal-green, mid)
CLOUD = ["#a8b4d8", "#bcd4e4", "#d8e6e6"]
HILL = ["#4a6c7a", "#628a8c", "#7c9ea0"]
FOREST = ["#1c3438", "#2a4c3c", "#3e6842", "#5e8a48", "#8cae58"]
BACKF = ["#2c4a50", "#3a5e6a", "#4a787a", "#628a8c"]   # back tree band: 1-2 steps lighter and bluer
EARTH = ["#3a3034", "#5e4a44"]
ROCK = ["#4a6c7a", "#7c9ea0"]
ROOF = ["#3a3034", "#874035"]
WALL = ["#5e4a44", "#8a6446", "#b08a5c"]
INK = "#1c1622"
BACK_PAL = SKY + WATER + REFL + CLOUD + HILL + FOREST + BACKF + EARTH + ROCK + ROOF + WALL + [INK]

WOOD = ["#3a2628", "#5e3e34", "#86583e", "#ae7c50", "#d0a26a"]
REED = ["#223a2e", "#3a5a34", "#5a7e3c", "#8aa452"]
LILY = ["#26503c", "#3a7444", "#5c9a48", "#8cc05a"]
FLOWER = ["#a04a70", "#e08aa8", "#f8d0dc"]
YELLOW = "#f0c848"
PAINT = ["#23404c", "#336070", "#4a8488"]
METAL = ["#4a5462", "#7c8898", "#b8c2cc"]
ROPE = "#c8b07c"
FRONT_PAL = WOOD + REED + LILY + FLOWER + [YELLOW] + PAINT + METAL + [ROPE, INK]

# single key light for the whole stage: low from behind the viewer's left shoulder (no sun glitter)
LB = Vector((-0.72, -0.32, 0.62)).normalized()

# back-layer overlay codes produced by the front pass (applied relative to the local water band)
OV_DARK, OV_WM1, OV_RIPPLE, OV_PADLINE = 1, 2, 3, 4
_OV = {}


# ------------------------------------------------------------------ projection helpers
def rc(p):
    """World point -> (col, row) top-down pixel coords on the 640x400 canvas."""
    x, y, _ = P.project(p, STAND)
    return W / 2 + x, H / 2 - y


def z_for_row(row, y, x=0.0):
    lo, hi = -2000.0, 8000.0
    for _ in range(60):
        m = (lo + hi) / 2
        if rc((x, y, m))[1] > row:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


def world_z(depth):
    """Per-pixel world Z from the camera depth pass (stage camera)."""
    s, c = math.sin(math.radians(P.PITCH)), math.cos(math.radians(P.PITCH))
    yv = (H / 2 - (np.arange(H) + 0.5))[:, None]
    zc = np.where(depth < 1e8, depth, 0.0)
    yc = yv * zc / P.F_PX
    rz = -zc * s + yc * c
    return P.CAM_UP + STAND + rz


def tagk(ob, kind, grp=None):
    ob["kind"] = kind
    if grp is not None:
        ob["grp"] = grp
    return ob


# ================================================================== BACK LAYER geometry
def back_scene(rnd):
    mt = R.m_tone
    tagk(R.vpoly("Sky", [(-20000, -3000), (20000, -3000), (20000, 9000), (-20000, 9000)], 4900, R.m_flat(SKY[3])), "sky")
    # ---- clouds: flat-bottomed cumulus from many small blobs; kept high (rows < 50), outside the game crop
    cm = mt(CLOUD, [0.5, 0.74], light=Vector((-0.45, -0.25, 0.86)).normalized(), noise=0.1, nscale=2.2, name="Cloud")
    for (cx, row, s, n) in ((-900, 30, 55, 9), (-150, 18, 70, 11), (700, 36, 50, 8), (1400, 22, 60, 9), (300, 42, 30, 6),
                            (-1500, 40, 36, 7)):
        y = 4000
        cz = z_for_row(row, y, cx)
        for k in range(n):
            u = (k / max(1, n - 1) - 0.5) * 2
            bx = cx + u * s * 1.6 + rnd.uniform(-0.2, 0.2) * s
            bz = cz + (1 - u * u) * s * 0.5 + rnd.uniform(0, 0.25) * s
            r = s * rnd.uniform(0.45, 0.75) * (1.1 - 0.4 * abs(u))
            tagk(R.ellipsoid("Cl", (bx, y + rnd.uniform(-30, 30), bz), (r * 1.25, r * 0.6, r * 0.78), cm, 14, 9), "cloud", grp=f"cl{cx}")
        tagk(R.ellipsoid("ClB", (cx, y + 40, cz - s * 0.05), (s * 2.2, s * 0.5, s * 0.25), cm, 16, 8), "cloud", grp=f"cl{cx}")
    # (no mountains: at the required 105 m only purple slivers cleared the hills - the hills are the skyline)
    # ---- forested hills (hazy blue-green, crest at rows 72-78)
    hm = mt(HILL, [0.46, 0.74], light=LB, noise=0.2, nscale=0.08, ncoord="world", ndetail=1.0,
            grads=((2, 26, 0, 0.12),), name="Hill")
    _, hx, hz = ridge_mesh("Hill", rnd, y0=700, depth=300, x0=-900, x1=900, step=14, hmax=26, hmin=18, mat=hm, seed=4.0,
                           smooth=True)
    bm_ = mt(HILL, [0.48, 0.74], light=LB, noise=0.12, nscale=1.5, ndetail=1.0, grads=((2, 26, 0, 0.12),), name="HillB")
    for k in range(380):
        x = rnd.uniform(-900, 900)
        zr = float(np.interp(x, hx, hz))
        v = -rnd.random() ** 1.5 * 0.7
        z = zr * (1 - abs(v)) ** 1.35 - 2.0
        r = rnd.uniform(3.5, 6.5)
        ob = C.add_prim("ico", "HB", bm_, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, 700 + v * 150, z + r * 0.3)) @ Matrix.Diagonal((r * 1.2, r, r * 0.9, 1))
        C.set_smooth(ob)
        tagk(ob, "hill", "hills")
    # ---- far shore: bank + shrub hedge + tree line + cabin
    bank_m = mt(FOREST[1:4], [0.5, 0.78], light=LB, noise=0.12, nscale=0.4, ncoord="world", name="Bank")
    verts, faces = [], []
    xs = np.arange(-420, 421, 6.0)
    for i, x in enumerate(xs):
        zt = 2.2 + 0.5 * math.sin(x * 0.05) + rnd.uniform(-0.2, 0.2)
        verts += [(x, 179.5, 0.4), (x, 181.5, zt), (x, 320, zt + 2)]
    for i in range(len(xs) - 1):
        a, b = i * 3, (i + 1) * 3
        faces += [(a, b, b + 1, a + 1), (a + 1, b + 1, b + 2, a + 2)]
    tagk(R.mesh_from("Bank", verts, faces, bank_m, smooth=False), "bank")
    earth = mt(EARTH, [0.6], light=LB, name="Earth")
    tagk(R.box("Cut", (0, 179.0, 0.2), (840, 1.2, 0.6), earth), "bank")
    rm = mt(ROCK, [0.62], light=LB, name="Rock")
    for k in range(14):
        x = rnd.uniform(-130, 130)
        if 40 < x < 75:
            continue
        r = rnd.uniform(0.6, 1.3)
        ob = C.add_prim("ico", "Rock", rm, radius=r, location=(x, 178.8, 0.1), subdivisions=1)
        ob.matrix_world = ob.matrix_world @ Matrix.Diagonal((1.6, 1.0, 0.7, 1))
        tagk(ob, "rock")
    fol = mt(FOREST, [0.3, 0.5, 0.68, 0.84], light=LB, noise=0.13, nscale=2.6, ndetail=1.0, name="Fol")
    pinem = mt(FOREST[:4], [0.34, 0.56, 0.78], light=LB, noise=0.1, nscale=3.0, ndetail=1.0, name="Pine")
    shrubm = mt(FOREST[:4], [0.36, 0.6, 0.82], light=LB, noise=0.16, nscale=3.5, ndetail=1.0, bias=-0.04, name="Shrub")
    folb = mt(BACKF, [0.3, 0.55, 0.8], light=LB, noise=0.12, nscale=2.4, ndetail=1.0, name="FolB")
    pineb = mt(BACKF[:3], [0.36, 0.64], light=LB, noise=0.1, nscale=3.0, ndetail=1.0, name="PineB")
    trunk = mt(EARTH, [0.55], light=LB, name="Trunk")
    gid = [0]

    def plant(x, y, h, pine, back=False):
        gid[0] += 1
        g = f"t{gid[0]}"
        if pine:
            conifer(x, y, 2.2, h, rnd, pineb if back else pinem, trunk, g)
        else:
            broadleaf(x, y, 2.2, h, rnd, folb if back else fol, trunk, g, shrubm if not back else None)

    # front row: low (4.5-7 m), irregular spacing, clumps of 2-3 trees and small clearings
    x = -150.0
    while x < 150:
        if not (44 < x < 72):
            n = 1 if rnd.random() < 0.55 else rnd.randint(2, 3)
            for k in range(n):
                plant(x + k * rnd.uniform(1.6, 2.6), rnd.uniform(183, 189), rnd.uniform(4.5, 7.0), rnd.random() < 0.42)
            x += (n - 1) * 2.1
        x += rnd.uniform(2.6, 5.2)
    # shrub hedge along the bank: hides trunks, breaks the crown-on-stick look
    x = -150.0
    while x < 150:
        if not (50 < x < 66):
            gid[0] += 1
            shrub(x, rnd.uniform(181.8, 183.2), 2.2, rnd.uniform(0.9, 2.0), rnd, shrubm, f"s{gid[0]}")
        x += rnd.uniform(1.4, 2.6)
    # back bands (6-10 m), bluer and lighter
    for band, (ya, yb, dens, hmin, hmax) in enumerate(((193, 215, 0.55, 6.0, 8.5), (218, 290, 0.45, 7.0, 10.0))):
        x = -260.0
        while x < 260:
            plant(x, rnd.uniform(ya, yb), rnd.uniform(hmin, hmax), rnd.random() < 0.6, back=True)
            x += rnd.uniform(1.6, 3.4) / dens
    cabin(58.0, 188.0, 2.2, rnd)
    tagk(R.hpoly("Water", [(-8000, -40), (8000, -40), (8000, 179.6), (-8000, 179.6)], 0.0, R.m_flat(WATER[3]), 0.01), "water")


def ridge_mesh(name, rnd, y0, depth, x0, x1, step, hmax, hmin, mat, seed, smooth=False):
    """Mountain / hill ridge: grid in x (step) and y (7 rows), heights from summed sines + jitter."""
    xs = np.arange(x0, x1 + step, step)
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    ridge = []
    f0 = 2 * math.pi / (0.15 * (x1 - x0))
    for x in xs:
        t = (math.sin(x * f0 + ph[0]) * 0.45 + math.sin(x * f0 * 2.7 + ph[1]) * 0.3
             + math.sin(x * f0 * 7.3 + ph[2]) * 0.15 + math.sin(x * f0 * 15.0 + ph[3]) * 0.1)
        ridge.append(hmin + (hmax - hmin) * (0.5 + 0.5 * t) + rnd.uniform(-0.05, 0.05) * hmax)
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
    return tagk(R.mesh_from(name, verts, faces, mat, smooth=smooth), name.lower()), xs, np.array(ridge)


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
    tagk(R.box("Gable", (x - 3.52, y, z + 4.1), (0.05, 4.4, 1.6), logs), "cabin", g)
    tagk(R.box("Chimney", (x + 2.2, y + 1.0, z + 5.2), (0.9, 0.9, 2.4), mt(ROCK, [0.6], light=LB, name="Stone")), "cabin", g)
    dark = R.m_flat(INK)
    tagk(R.box("Window", (x - 1.4, y - 2.52, z + 1.8), (1.3, 0.1, 1.0), dark), "cabin", g)
    tagk(R.box("Door", (x + 1.6, y - 2.52, z + 1.1), (1.0, 0.1, 2.0), mt(EARTH, [0.6], light=LB, name="Door")), "cabin", g)
    planks = mt(WALL[1:], [0.7], light=LB, name="Dock")
    tagk(R.box("Dock", (x - 5.0, y - 5.0, 0.55), (2.2, 8.0, 0.25), planks), "dock", "dock")
    for dy in (-8.5, -5.0, -1.5):
        tagk(R.box("DPost", (x - 6.05, y + dy, 0.4), (0.25, 0.25, 1.4), R.m_tone(EARTH, [0.6], light=LB, name="Door")), "dock", "dock")
    tagk(R.box("Skiff", (x - 8.0, y - 6.5, 0.2), (1.3, 3.6, 0.45), mt(ROOF, [0.5], light=LB, name="Skiff"), bevel=0.3), "dock", "skiff")


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
        # notch points roughly towards the viewer so the V reads
        rot = -math.pi / 2 + rnd.uniform(-0.9, 0.9)
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
    the tone inside each zone. Flat-shaded camera-facing ribbons -> one tone per blade, no highlight stripe."""
    return R.m_tone(REED, [0.25, 0.5, 0.75], light=LB, lam=0.4, bias=bias,
                    grads=((2, hz * 0.96, hz * 1.04, 0.5, "world"),), name="Reed")


def ribbon(name, pts, widths, mat, th=0.006):
    """Closed thin ribbon along pts whose broad faces always face the camera (width axis = t x Y), so an
    arched blade never twists edge-on at its drooping tip (a plain loft flips its frame there)."""
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
    a_tip = 0.52 * mpp                          # ~1 px at the tip (never thinner: the render is point-sampled)
    a_base = max(0.016, 0.95 * mpp)            # ~2 px at the base
    for i in range(n):
        bx, by, side = bases[i % nfan]
        g = f"reed{cx}_{i % nfan}"             # one group per fan: no contour lines inside a fan
        x = bx + rnd.gauss(0, 0.05)
        y = by + rnd.gauss(0, 0.05)
        h = rnd.uniform(hmin, hmax)
        mat = mats[rnd.randrange(3)]
        if i < ncat:
            # cattail: thin stem, a 2x5 px brown head, a short spike
            lean = rnd.uniform(-0.08, 0.08)
            top = (x + lean * h, y, h)
            objs.append(tagk(ribbon("Stem", [(x, y, -0.2), (x + lean * h * 0.5, y, h * 0.5), top], [a_tip] * 3, mat), "reed", g))
            hl = 5.0 * mpp
            t1 = 0.86
            t0 = t1 - hl / h
            a = (x + lean * h * t0, y, h * t0)
            b = (x + lean * h * t1, y, h * t1)
            objs.append(tagk(R.loft("Head", [a, b], [1.0 * mpp, 1.0 * mpp], catm, 8), "reed", g))
            objs.append(tagk(ribbon("Spike", [b, top], [0.5 * mpp, 0.5 * mpp], mat), "reed", g))
        elif rnd.random() < 0.6:
            # arched blade fanning out from the shared base, towards the fan's side
            dx = side * rnd.uniform(0.25, 1.0) if rnd.random() < 0.8 else -side * rnd.uniform(0.2, 0.6)
            bend = rnd.uniform(0.35, 0.8)
            objs.append(tagk(blade(x, y, h * rnd.uniform(0.65, 1.0), dx, rnd.uniform(-0.3, 0.3), bend,
                                   a_base, a_tip, mat), "reed", g))
        else:
            # near-upright blade
            dx = rnd.uniform(-1, 1)
            objs.append(tagk(blade(x, y, h, dx, rnd.uniform(-0.3, 0.3), rnd.uniform(0.04, 0.2),
                                   a_base, a_tip, mat, droop=0.3), "reed", g))
    # broad iris leaves at the base (a different, bluer green): straight swords, ~3 px wide
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
    g = f"pad{x}{y}"
    tagk(ob, "pad", g)
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


# ================================================================== numpy post: sky / water / reflections
def band_rows(rows, cols, bounds, widths):
    """Continuous colour per row: flat bands with linear (-> Bayer dithered) transitions around bounds."""
    lc = [R.s2l(R.hexrgb(c)) for c in cols]
    out = np.zeros((len(rows), 3))
    for n, r in enumerate(rows):
        c = lc[0]
        for i, (b, w) in enumerate(zip(bounds, widths)):
            t = min(1.0, max(0.0, (r - (b - w / 2)) / max(w, 1e-6)))
            c = c * (1 - t) + lc[i + 1] * t
        out[n] = c
    return R.l2s(out)


SKY_B = ([18, 36, 50, 58, 84], [10, 9, 7, 5, 3])   # rows 61-82 (the visible strip) are flat
# water depth zones (start row, end row) for WBANDS[i] -> WBANDS[i+1]; rows >= 120 stay within OKLab L 0.40-0.62
WATER_Z = [(107, 111), (113, 119), (129, 138), (165, 178), (221, 240)]
ROW_H, ROW_P = 106, 335            # horizon-side / pier-side reference rows for the dash length


def sky_rgb():
    return band_rows(np.arange(H), SKY, *SKY_B)


def dash_len(r):
    s = min(1.0, max(0.0, (r - ROW_H) / (ROW_P - ROW_H)))
    return int(round(2 + 6 * s)), int(round(3 + 13 * s))


def dash_mask(n, t, lmin, lmax, rng):
    """1-D mask of horizontal dashes (length lmin..lmax) covering ~t of the row, random offsets."""
    m = np.zeros(n, bool)
    if t <= 0.0:
        return m
    if t >= 1.0:
        m[:] = True
        return m
    c = -rng.randint(0, lmax * 3)
    while c < n:
        L = rng.randint(lmin, lmax)
        g = max(1, int(round(L * (1 - t) / t * rng.uniform(0.35, 1.65))))
        m[max(0, c):max(0, c + L)] = True
        c += L + g
    return m


def water_bands(pal, rng):
    """Per-pixel water band palette index with horizontal dash dithering between the bands."""
    wi = [pal.index(c) for c in WBANDS]
    out = np.zeros((H, W), int)
    for r in range(H):
        k = 0
        t = 0.0
        for i, (a, b) in enumerate(WATER_Z):
            if r >= b:
                k = i + 1
            elif r >= a:
                k = i
                t = (r - a + 0.5) / (b - a)
                break
        out[r] = wi[k]
        if t > 0 and k + 1 < len(wi):
            lmin, lmax = dash_len(r)
            out[r, dash_mask(W, t, lmin, lmax, rng)] = wi[k + 1]
    return out


def wstep(pal, i, d):
    """Move d steps along the water ramp (negative = darker); sparkles cap at #bcd4e4."""
    h = pal.hex[i]
    if h not in WSTEP:
        return i
    k = min(len(WSTEP) - 1, max(0, WSTEP.index(h) + d))
    return pal.index(WSTEP[k])


def wstep_arr(pal, arr, d):
    lut = np.arange(len(pal))
    for i in range(len(pal)):
        lut[i] = wstep(pal, i, d)
    return lut[arr]


def min_runs(lab, minlen=3):
    """Per row: merge label runs shorter than minlen into the left (or right) neighbour run."""
    out = lab.copy()
    for r in range(out.shape[0]):
        row = out[r]
        if not row.any():
            continue
        for _ in range(3):
            edges = np.flatnonzero(np.diff(row)) + 1
            starts = np.concatenate([[0], edges])
            ends = np.concatenate([edges, [len(row)]])
            changed = False
            for s, e in zip(starts, ends):
                if e - s < minlen:
                    if s > 0:
                        row[s:e] = row[s - 1]
                    elif e < len(row):
                        row[s:e] = row[e]
                    changed = True
            if not changed:
                break
    return out


def run_noise(rng, n, lmin, lmax):
    """Row of random values held constant over runs of lmin..lmax px (horizontal 'dash' noise)."""
    v = np.zeros(n)
    c = 0
    while c < n:
        L = rng.randint(lmin, lmax)
        v[c:c + L] = rng.random()
        c += L
    return v


def mirror_pass(objs, ids=False):
    """Render mirrored copies (reflection in the water plane) of objs only."""
    sc = bpy.context.scene
    hidden = []
    for ob in R.mesh_objects(False):
        if not ob.hide_render:
            hidden.append(ob)
            ob.hide_render = True
    Mz = Matrix.Diagonal((1.0, 1.0, -1.0, 1.0))
    copies = []
    for ob in objs:
        c = ob.copy()
        sc.collection.objects.link(c)
        c.matrix_world = Mz @ ob.matrix_world
        c.hide_render = False
        copies.append(c)
    bpy.context.view_layer.update()
    ps = R.render_passes("mirror", want_ids=ids)
    for c in copies:
        bpy.data.objects.remove(c, do_unlink=True)
    for ob in hidden:
        ob.hide_render = False
    return ps


def draw_dash(img_idx, r, c0, n, v, mask=None):
    r = int(r)
    if r < 0 or r >= img_idx.shape[0]:
        return
    for c in range(int(c0), int(c0) + max(1, int(n))):
        if 0 <= c < img_idx.shape[1] and (mask is None or mask[r, c]):
            img_idx[r, c] = v


# ================================================================== render
def far_reflection(pal, idx, water, wband, refl_objs):
    """Far-shore reflection: 2 tones, runs >= 3 px, light break line every third row, dissolving
    (break density 0 -> 100 %) over the bottom 25 % of its height."""
    mp = mirror_pass(refl_objs)
    rng = random.Random(7)
    ra = mp["a"] & water
    lum = (mp["rgb"] * np.array([0.3, 0.55, 0.15])).sum(-1)
    thr = np.percentile(lum[ra], 50) if ra.any() else 0.3
    lab = np.where(ra, np.where(lum < thr, 1, 2), 0)
    lab = min_runs(lab, 3)
    # geometry of the reflection per column (smoothed): top = first water row, bottom = last reflected row
    top = np.argmax(water, 0)
    has = lab.any(0)
    bot = np.where(has, H - 1 - np.argmax(lab[::-1] > 0, 0), top)
    kern = np.ones(9) / 9
    botf = np.convolve(np.pad(bot.astype(float), 4, mode="edge"), kern, "valid")
    hgt = np.maximum(1.0, botf - top + 1)
    rows = np.arange(H)[:, None]
    frac = (rows - top[None, :]) / hgt[None, :]
    out = idx.copy()
    light = wstep_arr(pal, wband, +1)
    dk, md = pal.index(REFL[0]), pal.index(REFL[1])
    for r in range(H):
        m = lab[r] > 0
        if not m.any():
            continue
        # dissolve over the bottom 25 %
        p = np.clip((frac[r] - 0.75) / 0.25, 0, 1)
        brk = run_noise(rng, W, 3, 8) < p
        row = np.where(lab[r] == 1, dk, md)
        keep = m & ~brk
        out[r, keep] = row[keep]
        # light break line every third row (the local water band shows through: lighter than the reflection)
        if (r - int(np.median(top))) % 3 == 2:
            bl = dash_mask(W, 0.4, 6, 20, rng) & keep
            out[r, bl] = wband[r, bl]
    return out, lab > 0


def wave_marks(pal, idx, water, avoid, wband):
    """Sparse wave marks: a light dash (water +1) with a darker dash (water -1) one row below, 1 px right.
    Density is cut in the central play rectangle and far away; no white glitter (light comes from behind-left)."""
    rng = random.Random(11)
    shore_row = rc((0, 179.6, 0))[1]
    for k in range(320):
        y = 1.0 + 170 * rng.random() ** 1.8
        x = rng.uniform(-1, 1) * (y + 12) * 0.75
        c, r = rc((x, y, 0))
        if r <= shore_row + 2 or r >= H or c < 0 or c >= W:
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
        base = int(wband[ri, ci])
        ok = water & ~avoid
        draw_dash(idx, r, c, n, wstep(pal, base, +1), ok)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), wstep(pal, base, -1), ok)
    return idx


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("lake_back")
    kinds = ps["kinds"]
    kid = np.vectorize(lambda i: kinds.get(int(i), ""))(ps["id"]) if ps["id"].size else None
    sky = (kid == "sky") | (~ps["a"])
    water = kid == "water"
    tree = (kid == "tree") | (kid == "shrub")
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = np.repeat(sky_rgb()[:, None, :], W, 1)[sky]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=3.0, rel=0.015, steps=1)
    idx = np.where(tree | (kid == "cabin") | (kid == "dock"), lined, idx)
    # ---- water: depth bands with horizontal dash dithering
    wband = water_bands(pal, random.Random(5))
    idx[water] = wband[water]
    # ---- far-shore reflection
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("tree", "shrub", "bank", "cabin", "rock", "dock")]
    idx, farm = far_reflection(pal, idx, water, wband, refl_objs)
    # ---- front-object reflections / ripples / pad lines (computed by the front pass)
    ov = _OV.get("ov")
    if ov is None:
        p = os.path.join(R.WORK, "front_overlay.npy")
        ov = np.load(p) if os.path.exists(p) else np.zeros((H, W), np.int8)
    idx = wave_marks(pal, idx, water, farm | (ov > 0), wband)
    wm = water
    idx[wm & (ov == OV_DARK)] = pal.index(REFL[0])
    for code, d in ((OV_WM1, -1), (OV_RIPPLE, +1), (OV_PADLINE, -2)):
        m = wm & (ov == code)
        idx[m] = wstep_arr(pal, wband[m], d)
    img = R.to_rgba(idx, pal)
    R.save_png(img, os.path.join(R.OUT, "lake_back.png"))
    print("R16 back colours", R.count_colours(img), "palette", len(pal))
    return pal


# ---------------------------------------------------------------- front post helpers
FLOWER_STAMP = ["..L..",
                ".LML.",
                "LMYML",
                "DDDDD"]


def shade_pads(idx, pal, ps, pad_ids):
    """Hand-style pad shading: left half LILY[2], right half LILY[1], 1 px lit rim upper-left LILY[3],
    1-2 px vein from the notch LILY[0]. Returns the union pad mask."""
    allm = np.zeros(idx.shape, bool)
    for info in PAD_INFO:
        pid = info["ob"].pass_index
        m = ps["id"] == pid
        if not m.any():
            continue
        allm |= m
        cx, cy = rc((info["x"], info["y"], 0.02))
        cols = np.arange(W)[None, :]
        idx[m] = pal.index(LILY[2])
        idx[m & (cols >= int(round(cx)))] = pal.index(LILY[1])
        up = np.zeros_like(m)
        up[1:] = m[:-1]
        lf = np.zeros_like(m)
        lf[:, 1:] = m[:, :-1]
        rim = m & ((~up) | (~lf)) & (cols < cx - 1)      # top + left edge, left half only
        idx[rim] = pal.index(LILY[3])
        # vein: from the notch tip (pad centre) across, away from the notch
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
        for j, line in enumerate(FLOWER_STAMP):
            for i, ch in enumerate(line):
                if ch in cmap and 0 <= y0 + j < H and 0 <= x0 + i < W:
                    idx[y0 + j, x0 + i] = pal.index(cmap[ch])


def contact_shadow(idx, pal, kid):
    """14x3 px ellipse of WOOD[1] on the planks at the projected feet point."""
    fc, fr = rc((0, 0, STAND))
    cx, cy = int(round(fc - 0.5)), int(fr) + 1          # just under the boot soles
    woods = [pal.index(c) for c in WOOD[2:]]
    for dy, hw in ((-1, 4), (0, 7), (1, 4)):
        for dx in range(-hw, hw):
            y, x = cy + dy, cx + dx
            if 0 <= y < H and 0 <= x < W and idx[y, x] in woods and kid[y, x] == "plank":
                idx[y, x] = pal.index(WOOD[1])


def front_reflections(refl, idx_front):
    """Reflections of every front object standing in water -> back-layer overlay codes.
    2 tones (dark reed-teal / water -1), per-row +-1 px shift only for runs >= 4 px, dash breaks
    every 2nd/3rd row, fade over 60 % of the reflected height."""
    mp = mirror_pass(refl, ids=True)
    rng = random.Random(3)
    a = mp["a"]
    ids = mp["id"]
    z = world_z(mp["depth"])
    zmin = {}
    for i in np.unique(ids[a]):
        zmin[int(i)] = float(np.min(z[a & (ids == i)]))
    zm = np.vectorize(lambda i: zmin.get(int(i), -1.0))(ids)
    frac = np.clip(z / np.minimum(zm, -1e-3), 0, 1)
    frac[~a] = 0
    lum = (mp["rgb"] * np.array([0.3, 0.55, 0.15])).sum(-1)
    code = np.where(a, np.where(lum < 0.36, OV_DARK, OV_WM1), 0)
    code[a & (z > -0.02)] = 0                    # the part above the water line is hidden by the object
    # fade: break density rises from 0 at the water line to 100 % at 60 % of the reflected height
    p = np.clip(frac / 0.6, 0, 1) ** 1.3
    shift = [(0, 1, 1, 0, -1, -1)[r % 6] for r in range(H)]
    out = np.zeros((H, W), np.int8)
    for r in range(H):
        row = code[r]
        if not row.any():
            continue
        brk = run_noise(rng, W, 2, 6) < p[r]
        row = np.where(brk, 0, row)
        if r % 5 in (0, 2):
            row = np.where(dash_mask(W, 0.45, 2, 6, rng), 0, row)
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
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("lake_front")
    pal = R.Pal(FRONT_PAL)
    kinds = ps["kinds"]
    kid = np.vectorize(lambda i: kinds.get(int(i), ""))(ps["id"])
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    idx = R.despeckle(idx, ps["id"], protect=~solid | (kid == "reed"), passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    padm = shade_pads(idx, pal, ps, None)
    # selective outline for solid props only: reeds and pads get none
    noline = (kid == "reed") | padm
    base = np.where(noline, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2)
    idx = np.where(noline & (idx >= 0), idx, ol)
    contact_shadow(idx, pal, kid)
    stamp_flowers(idx, pal)
    # ---- back-layer overlays: reflections, ripple dashes at stems / posts, 1 px dark water line under pads
    ov = front_reflections(refl, idx)
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
    img = R.to_rgba(idx, pal)
    R.save_png(img, os.path.join(R.OUT, "lake_front.png"))
    print("R16 front colours", R.count_colours(img), "palette", len(pal))


def main():
    C.reset_scene()
    which = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["back", "front"]
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    layout = dict(id="lake", standH=STAND, widthPx=W, heightPx=H, ppu=P.PPU, camBack=P.CAM_BACK, camUp=P.CAM_UP,
                  pitch=P.PITCH, focalPx=P.F_PX, mode="shore", zNear=1.2, zFar=176, xLim=90,
                  depthZ=[0, 8, 20, 40, 140, 176], depthV=[1.2, 3, 6, 8, 6, 1],
                  waterTint="#4a789f", waterDeep="#0f2742", ambient="day", clouds=True, birds=True)
    with open(os.path.join(R.OUT, "lake.json"), "w", encoding="utf-8") as f:
        json.dump(layout, f, indent=1)
    print("R16 LAKE done")


if __name__ == "__main__":
    main()
