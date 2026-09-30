"""
retro16 - the lake stage in the classic 16-bit style.

Back layer (opaque): banded sky + far mountains + forested hills + detailed far-shore tree line with
a cabin and dock, then the water (row-banded, Bayer-dithered depth bands, perspective reflection of
the far shore broken by ripple scanlines, sparse sparkles/wave marks, central area kept calm).
Front layer (transparent): plank pier with posts and rope, bucket + tackle box, reed clumps with
cattails, notched lily pads with flowers, a moored clinker rowboat, reflections + ripples of all of it.

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
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

STAND = 1.0
W, H = P.W, P.H

# ------------------------------------------------------------------ curated palettes
SKY = ["#3c5ca8", "#4c70b8", "#6088c8", "#7ca2d6", "#9cbee0", "#bcd4e4", "#d8e6e6"]      # top -> horizon
WATER = ["#32568e", "#3c66a0", "#4878b2", "#5a8ec4", "#7ca2d6", "#9cbee0", "#bcd4e4"]    # deep -> light
CLOUD = ["#a8b4d8", "#d8e6e6", "#f4f6f0"]
MTN = ["#6c78b0", "#8c98c8", "#a8b4d8", "#bcd4e4"]
HILL = ["#35546a", "#4a6c7a", "#628a8c"]
FOREST = ["#1c3438", "#2a4c3c", "#3e6842", "#5e8a48", "#8cae58"]
EARTH = ["#3a3034", "#5e4a44"]
ROCK = ["#6e6e80", "#9a9aa8"]
ROOF = ["#7a3434", "#a44c44"]
WALL = ["#5e4a44", "#8a6446", "#b08a5c"]
INK = "#1c1622"
BACK_PAL = SKY + WATER + CLOUD + MTN + HILL + FOREST + EARTH + ROCK + ROOF + WALL + [INK]

WOOD = ["#3a2628", "#5e3e34", "#86583e", "#ae7c50", "#d0a26a"]
REED = ["#223a2e", "#3a5a34", "#5a7e3c", "#8aa452"]
LILY = ["#26503c", "#3a7444", "#5c9a48", "#8cc05a"]
FLOWER = ["#a04a70", "#e08aa8", "#f8d0dc"]
YELLOW = "#f0c848"
PAINT = ["#23404c", "#336070", "#4a8488"]
METAL = ["#4a5462", "#7c8898", "#b8c2cc"]
ROPE = "#c8b07c"
FRONT_PAL = WOOD + REED + LILY + FLOWER + [YELLOW] + PAINT + METAL + [ROPE, INK] + WATER

LB = Vector((-0.72, -0.32, 0.62)).normalized()   # key light for the scenery: low from the left


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


def y_for_row(row):
    """Water-plane distance seen at a canvas row (rows below the horizon)."""
    lo, hi = -9.0, 20000.0
    for _ in range(80):
        m = (lo + hi) / 2
        if rc((0, m, 0))[1] > row:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


def tagk(ob, kind, grp=None):
    ob["kind"] = kind
    if grp is not None:
        ob["grp"] = grp
    return ob


# ================================================================== BACK LAYER geometry
def back_scene(rnd):
    mt = R.m_tone
    # sky backdrop (replaced by the numpy gradient)
    tagk(R.vpoly("Sky", [(-20000, -3000), (20000, -3000), (20000, 9000), (-20000, 9000)], 4900, R.m_flat(SKY[3])), "sky")
    # ---- clouds: flat-bottomed cumulus built from many small blobs
    cm = mt(CLOUD, [0.5, 0.74], light=Vector((-0.45, -0.25, 0.86)).normalized(), noise=0.1, nscale=2.2, name="Cloud")
    for (cx, row, s, n) in ((-900, 40, 60, 9), (-150, 24, 80, 11), (700, 48, 55, 8), (1400, 30, 70, 9), (300, 70, 34, 6),
                            (-1500, 66, 40, 7)):
        y = 4000
        cz = z_for_row(row, y, cx)
        for k in range(n):
            u = (k / max(1, n - 1) - 0.5) * 2
            bx = cx + u * s * 1.6 + rnd.uniform(-0.2, 0.2) * s
            bz = cz + (1 - u * u) * s * 0.5 + rnd.uniform(0, 0.25) * s
            r = s * rnd.uniform(0.45, 0.75) * (1.1 - 0.4 * abs(u))
            tagk(R.ellipsoid("Cl", (bx, y + rnd.uniform(-30, 30), bz), (r * 1.25, r * 0.6, r * 0.78), cm, 14, 9), "cloud", grp=f"cl{cx}")
        # flat base
        tagk(R.ellipsoid("ClB", (cx, y + 40, cz - s * 0.05), (s * 2.2, s * 0.5, s * 0.25), cm, 16, 8), "cloud", grp=f"cl{cx}")
    # ---- far mountain range (3D ridge mesh, faceted, haze gradient to the base)
    mm = mt(MTN, [0.36, 0.56, 0.8], soft=[0.0, 0.0, 0.06], light=LB, wrap=0.5,
            grads=((2, 170, 0, 0.42),), name="Mtn")
    ridge_mesh("Mtn", rnd, y0=2500, depth=500, x0=-2600, x1=2600, step=34, hmax=250, hmin=90, mat=mm, seed=1.0)
    # ---- forested hills (hazy blue-green, noise clusters = tree canopy texture)
    hm = mt(HILL, [0.44, 0.7], light=LB, noise=0.24, nscale=0.06, ncoord="world", ndetail=1.0,
            grads=((2, 60, 0, 0.16),), name="Hill")
    _, hx, hz = ridge_mesh("Hill", rnd, y0=700, depth=300, x0=-900, x1=900, step=14, hmax=62, hmin=24, mat=hm, seed=4.0,
                           smooth=True)
    # tree-crown bumps along the crest and the near slope -> a forested, clustered silhouette
    bm_ = mt(HILL, [0.47, 0.72], light=LB, noise=0.12, nscale=1.5, ndetail=1.0, grads=((2, 60, 0, 0.16),), name="HillB")
    for k in range(420):
        x = rnd.uniform(-900, 900)
        zr = float(np.interp(x, hx, hz))
        v = -rnd.random() ** 1.5 * 0.7
        z = zr * (1 - abs(v)) ** 1.35 - 2.0
        r = rnd.uniform(4.5, 8.5)
        ob = C.add_prim("ico", "HB", bm_, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, 700 + v * 150, z + r * 0.3)) @ Matrix.Diagonal((r * 1.2, r, r, 1))
        C.set_smooth(ob)
        tagk(ob, "hill", "hills")
    # ---- far shore: bank + tree line + cabin
    bank_m = mt(FOREST[1:4], [0.5, 0.78], light=LB, noise=0.12, nscale=0.4, ncoord="world", name="Bank")
    verts, faces = [], []
    xs = np.arange(-420, 421, 6.0)
    for i, x in enumerate(xs):
        zt = 2.4 + 0.6 * math.sin(x * 0.05) + rnd.uniform(-0.2, 0.2)
        verts += [(x, 179.5, 0.4), (x, 181.5, zt), (x, 320, zt + 2)]
    for i in range(len(xs) - 1):
        a, b = i * 3, (i + 1) * 3
        faces += [(a, b, b + 1, a + 1), (a + 1, b + 1, b + 2, a + 2)]
    tagk(R.mesh_from("Bank", verts, faces, bank_m, smooth=False), "bank")
    earth = mt(EARTH, [0.6], light=LB, name="Earth")
    tagk(R.box("Cut", (0, 179.0, 0.2), (840, 1.2, 0.6), earth), "bank")
    # rocks at the waterline
    rm = mt(ROCK, [0.62], light=LB, name="Rock")
    for k in range(16):
        x = rnd.uniform(-130, 130)
        if 40 < x < 75:
            continue
        r = rnd.uniform(0.6, 1.4)
        ob = C.add_prim("ico", "Rock", rm, radius=r, location=(x, 178.8, 0.1), subdivisions=1)
        ob.matrix_world = ob.matrix_world @ Matrix.Diagonal((1.6, 1.0, 0.7, 1))
        tagk(ob, "rock")
    # trees
    fol = mt(FOREST, [0.3, 0.5, 0.68, 0.84], light=LB, noise=0.13, nscale=2.6, ndetail=1.0, name="Fol")
    pinem = mt(FOREST[:4], [0.34, 0.56, 0.78], light=LB, noise=0.1, nscale=3.0, ndetail=1.0, name="Pine")
    trunk = mt(EARTH, [0.55], light=LB, name="Trunk")
    gid = [0]

    def plant(x, y, h, pine):
        gid[0] += 1
        g = f"t{gid[0]}"
        if pine:
            conifer(x, y, 2.2, h, rnd, pinem, trunk, g)
        else:
            broadleaf(x, y, 2.2, h, rnd, fol, trunk, g)

    x = -150.0
    while x < 150:
        if not (44 < x < 72):
            plant(x, rnd.uniform(183, 190), rnd.uniform(7.5, 11.5), rnd.random() < 0.45)
        x += rnd.uniform(2.6, 4.6)
    for band, (ya, yb, dens, hmin, hmax) in enumerate(((195, 225, 0.5, 9, 14), (230, 300, 0.42, 11, 17))):
        x = -260.0
        while x < 260:
            plant(x, rnd.uniform(ya, yb), rnd.uniform(hmin, hmax), rnd.random() < 0.6)
            x += rnd.uniform(1.6, 3.4) / dens
    cabin(58.0, 188.0, 2.4, rnd)
    # water plane (colour replaced by the numpy depth bands)
    tagk(R.hpoly("Water", [(-8000, -40), (8000, -40), (8000, 179.6), (-8000, 179.6)], 0.0, R.m_flat(WATER[3]), 0.01), "water")


def ridge_mesh(name, rnd, y0, depth, x0, x1, step, hmax, hmin, mat, seed, smooth=False):
    """Mountain / hill ridge: grid in x (step) and y (5 rows), heights from summed sines + jitter."""
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
        t0 = 0.1 + 0.78 * k / tiers
        zc = z0 + h * t0
        r = h * 0.27 * (1 - 0.82 * k / tiers) * rnd.uniform(0.9, 1.1)
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


def broadleaf(x, y, z0, h, rnd, mat, trunk, grp):
    tagk(R.loft("Trunk", [(x, y, z0 - 0.5), (x + rnd.uniform(-0.2, 0.2), y, z0 + h * 0.5)], [h * 0.045, h * 0.03], trunk, 6), "tree", grp)
    cz = z0 + h * 0.64
    n = rnd.randint(7, 10)
    for k in range(n):
        u = rnd.uniform(-1, 1)
        v = rnd.uniform(-1, 1)
        w = rnd.uniform(-0.7, 1)
        r = h * rnd.uniform(0.13, 0.2)
        c = (x + u * h * 0.24, y + v * h * 0.15, cz + w * h * 0.2)
        ob = C.add_prim("ico", "Blob", mat, radius=1.0, location=(0, 0, 0), subdivisions=2)
        ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((r, r, r * 0.9, 1))
        C.set_smooth(ob)
        tagk(ob, "tree", grp)


def cabin(x, y, z, rnd):
    mt = R.m_tone
    logs = mt(WALL, [0.42, 0.72], light=LB, pats=((2, 0.75, 0.3, -0.22, "world"),), name="Logs")
    roof = mt(ROOF, [0.62], light=LB, pats=((0, 0.9, 0.25, -0.3, "world"),), name="Roof")
    g = "cabin"
    tagk(R.box("Walls", (x, y, z + 1.6), (7.0, 5.0, 3.2), logs), "cabin", g)
    # gable roof (prism) with overhang
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
    # small dock + boat
    planks = mt(WALL[1:], [0.7], light=LB, name="Dock")
    tagk(R.box("Dock", (x - 5.0, y - 5.0, 0.55), (2.2, 8.0, 0.25), planks), "dock", "dock")
    for dy in (-8.5, -5.0, -1.5):
        tagk(R.box("DPost", (x - 6.05, y + dy, 0.4), (0.25, 0.25, 1.4), R.m_tone(EARTH, [0.6], light=LB, name="Door")), "dock", "dock")
    tagk(R.box("Skiff", (x - 8.0, y - 6.5, 0.2), (1.3, 3.6, 0.45), mt(ROOF, [0.62], light=LB, name="Skiff"), bevel=0.3), "dock", "skiff")


# ================================================================== FRONT LAYER geometry
def front_scene(rnd):
    mt = R.m_tone
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
             "pier", f"pl{i}")
    # stringers along both sides
    beam = mt(WOOD[:4], [0.4, 0.6, 0.8], light=LB, noise=0.1, nscale=2, nvec=(1, 0.2, 4), name="Beam")
    for sx in (-1.34, 1.34):
        tagk(R.box("Stringer", (sx, -8.5, STAND - 0.12), (0.1, 19.2, 0.22), beam), "pier", f"st{sx}")
    # posts: end posts taller (mooring), side posts low
    postm = mt(WOOD[:4], [0.34, 0.55, 0.8], light=LB, noise=0.08, nscale=3, nvec=(1, 1, 0.15), name="Post")
    ropem = mt([WOOD[3], ROPE], [0.55], light=LB, pats=((2, 0.035, 0.45, -0.3, "world"),), name="Rope")
    posts = []
    for (px, py, top) in ((-1.38, 0.95, STAND + 0.62), (1.38, 0.95, STAND + 0.62), (-1.38, -4.0, STAND + 0.2),
                          (1.38, -4.0, STAND + 0.2), (-1.38, -9.0, STAND + 0.2), (1.38, -9.0, STAND + 0.2)):
        ob = R.loft("Post", [(px, py, -1.2), (px, py, top - 0.04), (px, py, top)], [0.11, 0.11, 0.085], postm, 12)
        posts.append(tagk(ob, "post", f"po{px}{py}"))
        if top > STAND + 0.4:
            posts.append(tagk(R.loft("RopeWrap", [(px, py, top - 0.33), (px, py, top - 0.18)], 0.122, ropem, 12), "post", f"po{px}{py}"))
    # rope from the right end post hanging down to the water
    rp = [(1.38, 0.95 + 0.1, STAND + 0.3), (1.45, 1.4, STAND - 0.1), (1.5, 1.7, 0.2), (1.55, 1.9, -0.1)]
    posts.append(tagk(R.loft("Rope", rp, 0.022, mt([WOOD[3], ROPE], [0.55], light=LB, name="Rope2"), 6), "post", "ropeline"))
    # bucket (galvanised) + tackle box on the deck, left of the angler
    metal = mt(METAL, [0.45, 0.76], light=LB, pats=((2, 0.12, 0.18, -0.25, "world"),), name="Metal")
    bx, by = -0.82, -1.25
    tagk(R.loft("Bucket", [(bx, by, STAND), (bx, by, STAND + 0.38)], [0.17, 0.2], metal, 16, caps=True), "prop", "bucket")
    tagk(R.hpoly("BWater", R.ellipse_pts(bx, by, 0.175, 0.175, 16), STAND + 0.39, R.m_flat(WATER[2]), 0.005), "prop", "bucketw")
    tagk(R.loft("Bail", [(bx - 0.2, by, STAND + 0.36), (bx - 0.12, by - 0.05, STAND + 0.55), (bx, by - 0.07, STAND + 0.6),
                         (bx + 0.12, by - 0.05, STAND + 0.55), (bx + 0.2, by, STAND + 0.36)], 0.012, R.m_flat(METAL[0]), 6), "prop", "bail")
    paint = mt(PAINT, [0.45, 0.72], light=LB, name="Paint")
    tagk(R.box("Tackle", (-0.72, -0.35, STAND + 0.1), (0.46, 0.26, 0.2), paint, bevel=0.015), "prop", "tackle")
    tagk(R.box("TLid", (-0.72, -0.35, STAND + 0.215), (0.47, 0.27, 0.035), mt(PAINT[1:], [0.6], light=LB, name="Paint2"), bevel=0.01), "prop", "tackle")
    tagk(R.loft("THandle", [(-0.84, -0.35, STAND + 0.23), (-0.8, -0.35, STAND + 0.29), (-0.64, -0.35, STAND + 0.29),
                            (-0.6, -0.35, STAND + 0.23)], 0.014, R.m_flat(METAL[1]), 6), "prop", "tackleh")
    tagk(R.box("Latch", (-0.72, -0.483, STAND + 0.17), (0.06, 0.01, 0.05), R.m_flat(METAL[2])), "prop", "tackle")
    # ---- reeds (clumps framing the sides, centre left open)
    reedm = mt(REED, [0.36, 0.56, 0.76], light=LB, noise=0.05, nscale=6, name="Reed")
    catm = mt(WOOD[1:4], [0.45, 0.74], light=LB, name="Cattail")
    refl = []
    for (cx, cy, n, spread, hmin, hmax) in ((-5.6, 3.0, 26, 0.75, 1.0, 1.9), (6.6, 5.2, 22, 0.8, 1.0, 1.8),
                                            (-12.5, 17.0, 20, 1.3, 1.5, 2.5), (14.5, 28.0, 18, 1.5, 1.8, 2.8)):
        refl += reed_clump(cx, cy, n, spread, hmin, hmax, rnd, reedm, catm)
    # ---- lily pads (groups near the reeds)
    padm = mt(LILY, [0.62, 0.76, 0.88], light=LB, name="Pad")
    petal = mt(FLOWER, [0.5, 0.74], light=Vector((-0.5, -0.3, 0.8)).normalized(), name="Petal")
    for (x, y, r, fl) in ((-3.3, 6.2, 0.5, True), (-2.5, 7.4, 0.36, False), (-4.3, 7.9, 0.42, False),
                          (4.4, 9.5, 0.5, False), (5.2, 10.6, 0.4, True), (3.7, 11.2, 0.34, False),
                          (-8.5, 18.0, 0.7, True), (-7.4, 19.6, 0.55, False), (-9.8, 20.4, 0.6, False),
                          (10.2, 24.0, 0.8, False), (11.4, 26.2, 0.7, True)):
        lily(x, y, r, rnd.uniform(0, 6.28), padm, petal if fl else None, rnd)
    # ---- moored clinker rowboat + stake
    refl += rowboat(-8.8, 10.5, math.radians(28), rnd)
    sx_, sy_ = -10.4, 13.0
    stake = tagk(R.loft("Stake", [(sx_, sy_, -0.6), (sx_, sy_, 0.8), (sx_, sy_, 0.86)], [0.075, 0.075, 0.05], postm, 8), "post", "stake")
    refl.append(stake)
    bow = Matrix.Translation((-8.8, 10.5, 0.0)) @ Matrix.Rotation(math.radians(28), 4, "Z") @ Vector((0, 1.6, 0.5))
    rope = [(bow.x + (sx_ - bow.x) * k / 6, bow.y + (sy_ - bow.y) * k / 6,
             bow.z + (0.66 - bow.z) * k / 6 - 0.3 * math.sin(math.pi * k / 6)) for k in range(7)]
    tagk(R.loft("MoorRope", rope, 0.016, mt(WOOD[2:4], [0.55], light=LB, name="Rope3"), 6), "post", "moor")
    return refl + posts


def reed_clump(cx, cy, n, spread, hmin, hmax, rnd, reedm, catm):
    objs = []
    depth = P.project((cx, cy, 0), STAND)[2]
    wmin = 1.25 * depth / P.F_PX          # never thinner than ~1.25 px on screen
    for i in range(n):
        x = cx + rnd.gauss(0, spread * 0.55)
        y = cy + rnd.gauss(0, spread * 0.35)
        h = rnd.uniform(hmin, hmax)
        g = f"reed{cx}_{i}"
        if rnd.random() < 0.3:
            # cattail: stem + brown head + spike
            lean = rnd.uniform(-0.12, 0.12)
            top = (x + lean * h, y, h)
            objs.append(tagk(R.loft("Stem", [(x, y, -0.2), (x + lean * h * 0.5, y, h * 0.5), top], max(0.014, wmin * 0.5), reedm, 6), "reed", g))
            t0, t1 = 0.68, 0.84
            a = (x + lean * h * t0, y, h * t0)
            b = (x + lean * h * t1, y, h * t1)
            objs.append(tagk(R.loft("Head", [a, b], [max(0.042, wmin), max(0.04, wmin)], catm, 8), "reed", g))
            objs.append(tagk(R.loft("Spike", [b, top], [max(0.008, wmin * 0.4), max(0.004, wmin * 0.3)], reedm, 4), "reed", g))
        else:
            # leaf blade: tapered flat-ish ribbon that bends over near the top
            ang = rnd.uniform(0, 6.28)
            bend = rnd.uniform(0.08, 0.35) * (1 if rnd.random() < 0.85 else 1.8)
            dx, dy = math.cos(ang), math.sin(ang) * 0.4
            pts, rads = [], []
            for k in range(7):
                t = k / 6
                off = bend * h * t * t
                pts.append((x + dx * off, y + dy * off, h * t * (1 - 0.25 * bend * t)))
                wv = max(0.036 * (1 - t * 0.85) + 0.004, wmin * (0.65 if t < 0.8 else 0.5))
                rads.append((wv, 0.012 * (1 - t) + 0.004))
            objs.append(tagk(R.loft("Blade", pts, rads, reedm, 6, up=(1, 0, 0)), "reed", g))
    return objs


def lily(x, y, r, rot, padm, petalm, rnd):
    n = 22
    verts = [(x, y, 0.04)]
    for i in range(n):
        a = rot + 2 * math.pi * i / n
        rr = r * (0.12 if i == 0 else 1.0) * rnd.uniform(0.97, 1.03)
        verts.append((x + rr * math.cos(a), y + rr * math.sin(a) * 1.0, 0.012))
    faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)]
    ob = R.mesh_from("Pad", verts, faces, padm, smooth=True)
    R._fix_normals(ob)
    tagk(ob, "pad", f"pad{x}{y}")
    if petalm is not None:
        g = f"fl{x}{y}"
        fx, fy = x + r * 0.25 * math.cos(rot + 2.5), y + r * 0.25 * math.sin(rot + 2.5)
        for k in range(7):
            a = 2 * math.pi * k / 7
            c = (fx + math.cos(a) * 0.07, fy + math.sin(a) * 0.07, 0.1)
            rotm = Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(math.radians(-35), 4, "Y")
            tagk(R.ellipsoid("Petal", c, (0.1, 0.045, 0.03), petalm, 10, 6, rot=rotm), "flower", g)
        tagk(R.ellipsoid("Stamen", (fx, fy, 0.15), (0.05, 0.05, 0.04), R.m_flat(YELLOW), 10, 6), "flower", g)


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
        u = -L / 2 + L * i / nu          # local y: stern (-) -> bow (+)
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
    # transom
    faces.append(tuple(range(nv, -1, -1)))
    hull = R.mesh_from("Hull", [tuple(M @ Vector(v)) for v in verts], faces, boat_material(), smooth=True)
    objs.append(tagk(hull, "boat", "boat"))
    wood = R.m_tone(WOOD[:4], [0.38, 0.6, 0.8], light=LB, name="BoatWood")
    # gunwale rail
    for sgn in (-1, 1):
        pts = []
        for i in range(nu + 1):
            base = i * (nv + 1) + (0 if sgn < 0 else nv)
            pts.append(M @ Vector(verts[base]))
        objs.append(tagk(R.loft("Gunwale", pts, 0.035, wood, 6), "boat", "boat"))
    # thwarts + floor boards + oars
    for uy, wdt in ((-0.75, 1.0), (0.25, 1.05), (1.0, 0.7)):
        objs.append(tagk(R.box("Thwart", tuple(M @ Vector((0, uy, 0.2))), (wdt, 0.22, 0.05), wood,
                               rot=Matrix.Rotation(rot, 4, "Z")), "boat", "boat"))
    for sgn in (-1, 1):
        a = M @ Vector((sgn * 0.25, -1.2, 0.05))
        b = M @ Vector((sgn * 0.35, 1.1, 0.28))
        objs.append(tagk(R.loft("Oar", [a, b], 0.03, wood, 6), "boat", "boat"))
        blade = M @ Vector((sgn * 0.24, -1.1, 0.07))
        objs.append(tagk(R.box("Blade", tuple(blade), (0.16, 0.5, 0.025), wood, rot=Matrix.Rotation(rot, 4, "Z")), "boat", "boat"))
    return objs


# ================================================================== numpy post: sky / water / reflections
def band_rows(rows, cols, bounds, widths):
    """Continuous colour per row: flat bands with linear (-> dithered) transitions around bounds."""
    lc = [R.s2l(R.hexrgb(c)) for c in cols]
    out = np.zeros((len(rows), 3))
    for n, r in enumerate(rows):
        c = lc[0]
        for i, (b, w) in enumerate(zip(bounds, widths)):
            t = min(1.0, max(0.0, (r - (b - w / 2)) / max(w, 1e-6)))
            c = c * (1 - t) + lc[i + 1] * t
        out[n] = c
    return R.l2s(out)


SKY_B = ([14, 30, 46, 61, 74, 85], [10, 10, 10, 8, 7, 5])
WATER_TOP2BOT = WATER[::-1]
WATER_B = ([111, 120, 136, 166, 218, 292], [3, 5, 7, 10, 14, 20])


def sky_rgb():
    return band_rows(np.arange(H), SKY, *SKY_B)


def water_rgb():
    return band_rows(np.arange(H), WATER_TOP2BOT, *WATER_B)


def water_index_rows(pal):
    """Palette index of the dominant water band per row."""
    idx = np.zeros(H, int)
    b = WATER_B[0]
    for r in range(H):
        k = sum(1 for x in b if r >= x)
        idx[r] = pal.index(WATER_TOP2BOT[k])
    return idx


def water_step(pal, i, d):
    """Move d steps along the water ramp (negative = darker)."""
    h = pal.hex[i]
    if h not in WATER:
        return i
    k = min(len(WATER) - 1, max(0, WATER.index(h) + d))
    return pal.index(WATER[k])


def mirror_pass(objs):
    """Render mirrored copies (reflection in the water plane) of objs only -> rgb, alpha."""
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
    ps = R.render_passes("mirror", want_ids=False)
    for c in copies:
        bpy.data.objects.remove(c, do_unlink=True)
    for ob in hidden:
        ob.hide_render = False
    return ps


def ripple_breaks(rows, cols, seed):
    """Reflection scanline pattern: every 5th row is broken by long runs of plain water, and rows
    are shifted by -1/0/+1 px in a slow wave (classic 16-bit water reflection)."""
    rng = np.random.RandomState(seed)
    brk = np.zeros((H, W), bool)
    shift = np.array([(0, 1, 1, 0, -1, -1)[r % 6] for r in range(H)])
    for r in range(H):
        if r % 5 == 4:
            c = 0
            while c < W:
                run = rng.randint(3, 14)
                if rng.rand() < 0.5:
                    brk[r, c:c + run] = True
                c += run + rng.randint(1, 6)
    return brk, shift


def draw_dash(img_idx, r, c0, n, v, mask=None):
    r = int(r)
    if r < 0 or r >= img_idx.shape[0]:
        return
    for c in range(int(c0), int(c0) + max(1, int(n))):
        if 0 <= c < img_idx.shape[1] and (mask is None or mask[r, c]):
            img_idx[r, c] = v


# ================================================================== render
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
    tree = kid == "tree"
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = np.repeat(sky_rgb()[:, None, :], W, 1)[sky]
    rgb[water] = np.repeat(water_rgb()[:, None, :], W, 1)[water]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid, passes=1)
    # tree-on-tree contour lines (never on the hills / sky behind)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=3.0, rel=0.015, steps=1)
    idx = np.where(tree | (kid == "cabin") | (kid == "dock"), lined, idx)
    # ---- reflections of the far shore (mirrored geometry), broken into ripple scanlines
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("tree", "bank", "cabin", "rock", "dock")]
    mp = mirror_pass(refl_objs)
    brk, shift = ripple_breaks(None, None, 7)
    wrow = water_index_rows(pal)
    ra = mp["a"].copy()
    ra[:, 1:-1] = (ra[:, :-2].astype(int) + ra[:, 1:-1] + ra[:, 2:]) >= 2
    lum = (mp["rgb"] * np.array([0.3, 0.55, 0.15])).sum(-1)
    out = idx.copy()
    for r in range(H):
        s = shift[r]
        a_row = np.roll(ra[r], s)
        l_row = np.roll(lum[r], s)
        m = water[r] & a_row & ~brk[r]
        mb = water[r] & a_row & brk[r]
        out[r, mb] = pal.index(WATER[3])
        if not m.any():
            continue
        cs = np.nonzero(m)[0]
        lv = l_row[cs]
        out[r, cs] = np.where(lv < 0.3, pal.index(WATER[0]), np.where(lv < 0.46, pal.index(WATER[1]), pal.index(WATER[2])))
    idx = out
    # ---- sparkles + wave marks (sparse, the middle stays calm)
    sp = np.zeros((H, W), bool)
    white = pal.index(CLOUD[2])
    rng = random.Random(11)
    shore_row = rc((0, 179.6, 0))[1]
    for k in range(520):
        y = 1.0 + 170 * rng.random() ** 1.8
        x = rng.uniform(-1, 1) * (y + 12) * 0.75
        c, r = rc((x, y, 0))
        if r <= shore_row + 2 or r >= H or c < 0 or c >= W:
            continue
        central = 190 < c < 450 and 140 < r < 330
        if central and rng.random() < 0.8:
            continue
        if y > 90 and rng.random() < 0.55:
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = max(2, int(round(520 / depth * rng.uniform(0.35, 0.9))))
        base = int(idx[int(r), int(c)])
        light = water_step(pal, base, +1)
        dark = water_step(pal, base, -1)
        draw_dash(idx, r, c, n, light, water)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), dark, water)
        sp[int(r), max(0, int(c)):int(c) + n] = True
    # sun glitter path (right third): white dashes along the line towards the sun
    for k in range(110):
        y = 3 + 175 * rng.random() ** 1.3
        x = 0.62 * (y + 10.5) + rng.gauss(0, 0.16) * (y + 10.5)
        c, r = rc((x, y, 0))
        if r <= shore_row + 1 or r >= H or c < 0 or c >= W:
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = max(1, int(round(520 / depth * rng.uniform(0.12, 0.35))))
        draw_dash(idx, r, c, n, white, water)
        sp[int(r), max(0, int(c)):int(c) + n] = True
    img = R.to_rgba(idx, pal)
    R.save_png(img, os.path.join(R.OUT, "lake_back.png"))
    print("R16 back colours", R.count_colours(img), "palette", len(pal))
    return pal


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
    idx = R.remove_specks(idx, 4, keep=(kid == "flower"))
    idx = R.despeckle(idx, ps["id"], protect=~solid | (kid == "flower"), passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    idx = R.outer_outline(idx, pal, lit_steps=1, dark_steps=2)
    # reflections of reeds / posts / boat on the water + ripple rings at their feet
    mp = mirror_pass(refl)
    brk, shift = ripple_breaks(None, None, 3)
    wrow = water_index_rows(pal)
    ref = np.full((H, W), -1, int)
    for r in range(H):
        a_row = np.roll(mp["a"][r], shift[r])
        m = a_row & ~brk[r] & (idx[r] < 0)
        ref[r, m] = pal.index(WATER[0]) if r > 200 else pal.index(WATER[1])
    # ripple dashes around each stem/post foot
    rng = random.Random(5)
    lightw = lambda r: water_step(pal, wrow[min(H - 1, max(0, int(r)))], +2)
    for ob in refl:
        if ob.get("kind") not in ("reed", "post"):
            continue
        bb = [ob.matrix_world @ Vector(v) for v in ob.bound_box]
        x = sum(v.x for v in bb) / 8
        y = sum(v.y for v in bb) / 8
        if rng.random() < 0.5:
            continue
        c, r = rc((x, y, 0))
        depth = P.project((x, y, 0), STAND)[2]
        n = max(2, int(520 / depth * 0.18))
        for k, (dr, dc, ln) in enumerate(((0, -n, n), (0, 1, n), (1, -n // 2 - 1, n // 2), (1, 2, n // 2))):
            rr = int(r + dr)
            if 0 <= rr < H:
                for cc in range(int(c + dc), int(c + dc + ln)):
                    if 0 <= cc < W and idx[rr, cc] < 0:
                        ref[rr, cc] = lightw(rr)
    idx = np.where(idx >= 0, idx, ref)
    img = R.to_rgba(idx, pal)
    R.save_png(img, os.path.join(R.OUT, "lake_front.png"))
    print("R16 front colours", R.count_colours(img), "palette", len(pal))


def main():
    C.reset_scene()
    rnd = random.Random(1234)
    which = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["back", "front"]
    if "back" in which:
        render_back(random.Random(77))
    if "front" in which:
        render_front(random.Random(31))
    layout = dict(id="lake", standH=STAND, widthPx=W, heightPx=H, ppu=P.PPU, camBack=P.CAM_BACK, camUp=P.CAM_UP,
                  pitch=P.PITCH, focalPx=P.F_PX, mode="shore", zNear=1.2, zFar=176, xLim=90,
                  depthZ=[0, 8, 20, 40, 140, 176], depthV=[1.2, 3, 6, 8, 6, 1],
                  waterTint="#3c66a0", waterDeep="#12264a", ambient="day", clouds=True, birds=True)
    with open(os.path.join(R.OUT, "lake.json"), "w", encoding="utf-8") as f:
        json.dump(layout, f, indent=1)
    print("R16 LAKE done")


if __name__ == "__main__":
    main()
