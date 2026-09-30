"""
hybrid - the aquarium room: retro16 craft + the "lake" preset mood (golden hour), side view (ortho camera).

A cosy room at golden hour, key light from the upper left / front (window side), preset
use_preset("lake", shaft=..., key_dir=(-0.4, -0.3, 0.86)), rims on the LEFT / top outer silhouettes.

Back layer (opaque, behind the fish):
  * striped wallpaper in a warm-to-dusky gradient (window side lit, right side / ceiling in shade), picture
    rail + skirting board, faked floorboards, soft 16-bit drop shadows of the tank / cabinet / sill / frame;
  * a window (left) whose content is a crop of R.sky_rgb(PR): the golden-hour sky with the pixel sun, its
    glow rings and a stratus streak over a hazed hill line + dark tree line, the lake below mirroring the sky
    (R.sky_rgb(PR, mirror=True)) with a few glitter dashes; a soft window glow on the wall;
  * a wooden cabinet / stand with three raised-panel doors and brass knobs, a potted snake plant under the
    window, a shelf with a brass trophy and a framed fish print (a hybrid fish sprite on paper);
  * the tank interior: calm teal water in dash-dithered depth bands (lighter under the lamp), the lamp-lit
    air gap + meniscus line, vallisneria clumps hugging the sides (underwater haze: farther = closer to the
    water colour), driftwood with moss, rocks, a copper ludwigia + an amazon sword, hairgrass tufts, the air
    stone at the game's bubble source (-6.5, -3.2), hand-painted pebble gravel, and three warm lamp shafts
    (R.apply_shaft) with dust motes - water / decor only.
Front layer (transparent, in front of the fish): the walnut tank frame + lamp hood (m_tone, warm rim on
the outer left/top silhouette, hue-shifted outline - never ink), two hairgrass tufts in the bottom corners,
glass glints in two corners.

World <-> pixel: the game shows the 640x400 sprites centred at (0, 0), 16 px per unit:
col = 320 + 16 x, row = 200 - 16 y. Swim bounds / surfaceY come unchanged from Data/aquarium.json.
Outputs: _tmp/variants/hybrid/aquarium_back.png, aquarium_front.png, aquarium.json
Run: blender -b --python variants/hybrid/hyb_aquarium.py [-- back|front]
"""
import os
import sys
import math
import json
import random
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402

SID = "aquarium"
R.WORK = os.path.join(R.WORK, SID)            # scratch EXR passes of this piece only (parallel runs never collide)
os.makedirs(R.WORK, exist_ok=True)

W, H, PPU = 640, 400, 16
LAMP = "#ffe2a6"                               # warm tank lamp
SHAFTS = [dict(col=LAMP, amount=0.13, top=(176, 117, 26), bottom=(212, 274, 44), fade=0.6),      # one amount:
          dict(col=LAMP, amount=0.13, top=(318, 117, 34), bottom=(356, 274, 58), fade=0.5),      # the 3 shafts
          dict(col=LAMP, amount=0.13, top=(462, 117, 24), bottom=(496, 274, 40), fade=0.65)]     # share colours
PR = R.use_preset("lake", shaft=SHAFTS[1], key_dir=(-0.4, -0.3, 0.86),
                  rim=dict(dirs=dict(l=1.0, tl=0.85, t=0.55)))
G = R.grade_hex
LB = Vector(PR.key_dir).normalized()
with open(os.path.join(C.DATA, "aquarium.json"), encoding="utf-8") as _f:
    LAYOUT = json.load(_f)


def X(c):
    return (c - W / 2) / PPU


def Z(r):
    return (H / 2 - r) / PPU


def col_of(x):
    return W / 2 + x * PPU


def row_of(z):
    return H / 2 - z * PPU


# ------------------------------------------------------------------ layout (pixels, top-down)
TANK_C0, TANK_C1 = 96, 544                     # outer frame columns (x -14 .. 14)
IN_C0, IN_C1 = 102, 538                        # glass opening
RIM_R0, IN_R0 = 102, 110                       # top rim rows, inner top
IN_R1, BOT_R1 = 274, 282                       # inner bottom (bottom rim 274..282)
SURF_ROW = int(math.ceil(row_of(LAYOUT["surfaceY"]) - 0.5))   # first water row (centre below surfaceY): 117
HOOD = (92, 548, 88, 102)
CAB_TOP, CAB_BODY, CAB_PLINTH = (76, 564, 282, 288), (80, 560, 288, 332), (86, 554, 332, 338)
FLOOR_ROW = 338
WIN_O = (20, 76, 68, 180)                      # window outer frame
WIN_I = (24, 72, 72, 176)                      # glass
WIN_HOR = 156                                  # horizon row inside the window
WIN_SUN_C = 58                                 # sun column inside the window
PIC_O = (570, 624, 90, 130)
PIC_I = (574, 620, 94, 126)
SHELF = (562, 628, 168, 172)


def ztop(x):
    """Gravel top line (world z): gentle mounds, rising into the back corners."""
    a = abs(x)
    s = min(1.0, max(0.0, (a - 8.0) / 5.6))
    return -3.72 + 0.16 * math.sin(x * 0.55 + 0.7) + 0.08 * math.sin(x * 1.7 + 2.0) + 0.38 * s * s * (3 - 2 * s)


GTOP = np.array([int(math.ceil(row_of(ztop(X(c + 0.5))) - 0.5)) for c in range(W)])

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)
MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)
HILLW = R.hazed(G(["#34505a", "#46645e"]), 700.0)                  # the lake's hills, hazed
TREEW = R.hazed(G(["#13252a", "#274636"]), 196.0, extra=0.06)       # far tree line
GLIT = PR.glitter["cols"]
WALL = G(["#4a3a4a", "#604850", "#7a5a56", "#946e5c", "#ae8662", "#c89e6c"])   # dusk shade -> window-lit
WOOD = G(["#2e1e26", "#48302e", "#65423a", "#855a44", "#a47450", "#c49062"])   # walnut
GOLD = G(["#6a4a2a", "#a47c38", "#d4ae4e", "#f4dc88"])
POT = G(["#6a3428", "#9a5236", "#c47a4c"])
PAPER = G("#e6d6b4")
# tank water: calm teal, lighter under the lamp (far = top -> near = gravel), meniscus, lamp-lit air gap
WT = G(["#8ec6bc", "#78b4b6", "#65a2ae", "#5692a4", "#4a8298", "#40728a"])
WDARK = G("#34627a")
MENISC = G("#cfeede")
AIR = G("#d6d2ae")
WSTEP = [WDARK] + WT[::-1] + [MENISC]
WHZ = WT[2]                                    # underwater haze target


def uw(cols, f):
    """Underwater aerial perspective: farther decor -> closer to the water colour."""
    return [R.mixhex(c, WHZ, f) for c in cols]


PLANT = G(["#1d4636", "#2c6244", "#468048", "#76a452"])
PLANT_B = uw(PLANT[:3], 0.42)                  # back vallisneria
PLANT_M = uw(PLANT, 0.22)                      # sword, moss
PLANT_F = uw(PLANT, 0.05)                      # foreground hairgrass (+ the snake plant on the floor)
RED = uw(G(["#743626", "#aa582e", "#d88a42"]), 0.26)            # copper ludwigia (warm accent)
DRIFT = uw(G(["#35262a", "#563e36", "#7a5a44", "#9c7a58"]), 0.14)
ROCK = uw(G(["#44465a", "#5e6274", "#7c7e8c", "#9c9a9c"]), 0.14)
SAND = uw(G(["#6e5a4e", "#96785e", "#ba9c76", "#dac29a"]), 0.05)
GLASS = G(["#a6d2c8", "#e2f4ea"])

BACK_PAL = (SKY_PAL + MSKY_PAL + HILLW + TREEW + GLIT + WALL + WOOD + GOLD + POT + [PAPER] + WT
            + [WDARK, MENISC, AIR] + PLANT_B + PLANT_M + PLANT_F + RED + DRIFT + ROCK + SAND)
FRONT_PAL = WOOD + PLANT_F + GLASS
WIN_PAL = SKY_PAL + MSKY_PAL + HILLW + TREEW + GLIT
# 16-bit style sub-palettes: every kind is quantised with its own ramp only (no stray dither partners)
GROUPS = dict(wall=WALL, rail=WOOD, floor=WOOD, cab=WOOD, door=WOOD, shelf=WOOD, sill=WOOD, winframe=WOOD,
              knob=GOLD, trophy=GOLD, picframe=GOLD, pot=POT, leaf=PLANT_F, window=WIN_PAL, plant=PLANT_B,
              sword=PLANT_M, moss=PLANT_M, grass=PLANT_F, red=RED, wood=DRIFT, rock=ROCK, stone=ROCK, gravel=SAND,
              water=WT, picture=[PAPER])
LIT = dict(plant=[PLANT_B], sword=[PLANT_M], moss=[PLANT_M], grass=[PLANT_F], red=[RED], wood=[DRIFT], rock=[ROCK],
           stone=[ROCK], gravel=[SAND, ROCK])          # decor under a lamp shaft: one step up its own ramp(s)

_ST = {}


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


def pbox(name, rect, y, depth, mat, kind, grp=None, bevel=0.0):
    """Box from a pixel rect (c0, c1, r0, r1) with its front face at y."""
    c0, c1, r0, r1 = rect
    ob = R.box(name, (X((c0 + c1) / 2), y + depth / 2, Z((r0 + r1) / 2)), ((c1 - c0) / PPU, depth, (r1 - r0) / PPU),
               mat, bevel=bevel)
    return tagk(ob, kind, grp)


def pquad(name, rect, y, mat, kind, grp=None):
    c0, c1, r0, r1 = rect
    pts = [(X(c0), Z(r1)), (X(c1), Z(r1)), (X(c1), Z(r0)), (X(c0), Z(r0))]
    return tagk(C.poly_object(name, pts, mat, thickness=0.02, y=y), kind, grp)


# ------------------------------------------------------------------ plant geometry (camera-facing ribbons)
def ribbon(name, pts, widths, mat, th=0.006):
    """Closed thin ribbon along pts whose broad faces face the camera (width axis = t x Y)."""
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


def blade(name, x, y, z0, h, dirx, bend, sway, a0, a1, mat, nseg=12, droop=0.35, xlim=None):
    """Tapered camera-facing blade rising from z0, arching towards dirx; xlim=(x0, x1) keeps it inside the tank."""
    pts, ws = [], []
    for k in range(nseg):
        t = k / (nseg - 1)
        off = dirx * bend * h * 0.8 * t * t + sway * math.sin(t * math.pi * 1.3) * h * 0.06
        z = z0 + h * (t - bend * droop * t ** 3)
        xx = x + off
        if xlim is not None:
            xx = min(xlim[1], max(xlim[0], xx))
        pts.append((xx, y, z))
        ws.append(a0 * (1 - t) + a1 * t)
    return ribbon(name, pts, ws, mat)


XLIM = (X(IN_C0) + 0.15, X(IN_C1) - 0.15)       # plants never poke out of the glass


def vallis_clump(x0, x1, n, y, hmin, hmax, rnd, mat, grp):
    for i in range(n):
        x = rnd.uniform(x0, x1)
        h = rnd.uniform(hmin, hmax)
        z0 = ztop(x) - 0.3
        h = min(h, Z(SURF_ROW + 5) - z0)
        dirx = rnd.choice((-1, 1)) * rnd.uniform(0.3, 1.0)
        tagk(blade("Vallis", x, y + rnd.uniform(-0.3, 0.3), z0, h, dirx, rnd.uniform(0.05, 0.22), rnd.uniform(-1, 1),
                   rnd.uniform(0.07, 0.1), 0.03, mat, nseg=14, xlim=XLIM), "plant", grp)


def tuft(x, y, n, spread, hmin, hmax, rnd, mat, kind, grp):
    for i in range(n):
        xx = x + rnd.gauss(0, spread * 0.35)
        z0 = ztop(xx) - 0.12
        dirx = (xx - x) / max(1e-3, spread) * 1.4 + rnd.uniform(-0.4, 0.4)
        tagk(blade("Grass", xx, y + rnd.uniform(-0.05, 0.05), z0, rnd.uniform(hmin, hmax), dirx,
                   rnd.uniform(0.15, 0.45), 0.0, 0.05, 0.028, mat, nseg=6, droop=0.2, xlim=XLIM), kind, grp)


def sword_plant(x, y, rnd, mat):
    z0 = ztop(x) - 0.25
    for k in range(9):
        ang = math.radians(-62 + 124 * k / 8 + rnd.uniform(-7, 7))
        L = rnd.uniform(2.4, 3.9) * (1.0 - 0.25 * abs(math.sin(ang)))
        pts, ws = [], []
        for i in range(12):
            t = i / 11
            a = ang * (0.5 + 0.9 * t)
            pts.append((x + math.sin(a) * L * t, y + k * 0.03, z0 + math.cos(a) * L * t))
            ws.append(0.035 if t < 0.28 else 0.03 + 0.24 * max(0.0, math.sin(math.pi * (t - 0.28) / 0.72)) ** 0.8)
        tagk(ribbon("Sword", pts, ws, mat), "sword", f"sword{k}")


def ludwigia(x0, x1, y, rnd, mat):
    for s in range(6):
        x = x0 + (x1 - x0) * s / 5 + rnd.uniform(-0.2, 0.2)
        z0 = ztop(x) - 0.2
        h = rnd.uniform(2.6, 4.3)
        lean = rnd.uniform(-0.25, 0.25)
        g = f"lud{s}"
        pts = [(x + lean * h * (i / 6) ** 2, y, z0 + h * i / 6) for i in range(7)]
        tagk(ribbon("Stem", pts, [0.03] * 7, mat), "red", g)
        z = z0 + 0.5
        side = 1
        while z < z0 + h - 0.1:
            t = (z - z0) / h
            cx = x + lean * h * t * t
            for sd in (-1, 1):
                rot = Matrix.Rotation(math.radians(-sd * (28 + 18 * t)), 4, "Y")     # leaf tips point up-out
                ob = R.ellipsoid("Leaf", (cx + sd * 0.2, y - 0.04, z + 0.05), (0.24 - 0.06 * t, 0.04, 0.085), mat,
                                 segs=8, rings=5, rot=rot)
                tagk(ob, "red", g)
            z += 0.33
            side = -side


def rock(x, rx, rz, y, rnd, mat, sink=0.45):
    ob = C.add_prim("ico", "Rock", mat, radius=1.0, location=(0, 0, 0), subdivisions=2)
    for v in ob.data.vertices:
        co = v.co * (1.0 + rnd.uniform(-0.1, 0.1))
        if co.z > 0.55:
            co.z = 0.55 + (co.z - 0.55) * 0.5              # flattish tops (river stones)
        v.co = co
    zb = ztop(x)
    ob.matrix_world = Matrix.Translation((x, y, zb + rz * (1 - sink) - 0.1)) @ Matrix.Diagonal((rx, 0.8, rz, 1))
    C.set_smooth(ob, False)
    return tagk(ob, "rock", f"rock{x}")


def driftwood(rnd, mat, mossm):
    y = 6.3
    limbs = [([(-9.8, -3.9), (-8.0, -3.35), (-6.2, -2.35), (-4.6, -1.15), (-3.4, 0.2), (-2.7, 1.5)],
              [0.34, 0.3, 0.26, 0.2, 0.13, 0.06]),
             ([(-6.1, -2.3), (-6.6, -0.9), (-7.4, 0.3), (-7.9, 1.05)], [0.16, 0.12, 0.08, 0.045]),
             ([(-4.4, -1.0), (-3.0, -1.12), (-1.7, -0.55), (-0.9, 0.05)], [0.14, 0.1, 0.07, 0.04]),
             ([(-8.3, -3.45), (-7.0, -3.95), (-5.7, -4.05)], [0.2, 0.15, 0.1])]
    for k, (pts, rads) in enumerate(limbs):
        p3 = [(px, y + 0.02 * k, pz) for px, pz in pts]
        tagk(R.loft("Drift", p3, [r * 1.3 for r in rads], mat, 10), "wood", "drift")
    # moss cushions on the upper side of the limbs
    for k in range(16):
        pts, rads = limbs[k % 3]
        i = rnd.randrange(len(pts) - 1)
        t = rnd.random()
        px = pts[i][0] + (pts[i + 1][0] - pts[i][0]) * t
        pz = pts[i][1] + (pts[i + 1][1] - pts[i][1]) * t
        r = rads[i] * (1 - t) + rads[i + 1] * t
        rr = rnd.uniform(0.12, 0.22)
        ob = C.add_prim("ico", "Moss", mossm, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((px + rnd.uniform(-0.1, 0.1), y - 0.25, pz + r * 0.7)) @ \
            Matrix.Diagonal((rr * 1.4, rr, rr * 0.8, 1))
        C.set_smooth(ob)
        tagk(ob, "moss", "moss")


# ================================================================== BACK LAYER geometry
def back_scene(rnd):
    mt = R.m_tone
    # ---- room: wall (lit near the window, shade to the right and under the ceiling), rail, skirting, floor
    # lit near the window, dusk shade to the right; a darker frieze above the picture rail; darker at the floor
    wall_m = mt(WALL, [0.2, 0.34, 0.48, 0.62, 0.76], soft=0.0, light=LB, lam=0.5, bias=0.4,
                grads=((0, -20.0, 20.0, -0.42), (2, 10.0, 10.5, -0.22), (2, -8.6, -6.5, 0.1)), name="Wall")
    pquad("Wall", (0, W, 0, FLOOR_ROW), 14.0, wall_m, "wall")
    rail_m = mt(WOOD[1:5], [0.3, 0.55, 0.8], light=LB, grads=((0, -20, 20, -0.2),), bias=0.1, name="Rail")
    pbox("Rail", (0, W, 34, 40), 13.5, 0.5, rail_m, "rail", bevel=0.05)
    pbox("Skirt", (0, W, 326, 338), 13.5, 0.5, rail_m, "rail", "skirt", bevel=0.05)
    pquad("Floor", (0, W, FLOOR_ROW, H), 1.0, R.m_flat(WOOD[3]), "floor")        # boards painted in post
    # ---- window: glass plane (replaced by the sky crop in post), frame, mullions, sill
    pquad("WinGlass", WIN_I, 13.9, R.m_flat(WALL[0]), "window")
    fr_m = mt(WOOD[2:], [0.45, 0.62, 0.8], light=LB, name="WinFrame")
    c0, c1, r0, r1 = WIN_O
    for nm, rect in (("WL", (c0, c0 + 4, r0, r1)), ("WR", (c1 - 4, c1, r0, r1)), ("WT", (c0, c1, r0, r0 + 4)),
                     ("WB", (c0, c1, r1 - 4, r1)), ("WMV", (47, 49, 72, 176)), ("WMH", (24, 72, 122, 124))):
        pbox(nm, rect, 13.4, 0.5, fr_m, "winframe", "win", bevel=0.04)
    pbox("Sill", (16, 80, 180, 185), 12.6, 1.4, mt(WOOD[2:], [0.5, 0.7, 0.86], light=LB, name="Sill"), "sill", "sill",
         bevel=0.05)
    # ---- potted snake plant under the window (floor, in front of the wall)
    potm = mt(POT, [0.45, 0.66], light=LB, name="Pot")
    px, pyy = -17.5, 11.0
    tagk(R.loft("Pot", [(px, pyy, Z(338)), (px, pyy, Z(312))], [0.78, 0.98], potm, 16), "pot", "pot")
    tagk(R.loft("PotRim", [(px, pyy, Z(312)), (px, pyy, Z(306))], [1.08, 1.08], potm, 16), "pot", "pot")
    snake = mt(PLANT_F, [0.35, 0.55, 0.78], light=LB, lam=0.9, grads=((2, Z(306), Z(250), 0.2),), name="Snake")
    for k in range(7):
        a = -1 + 2 * k / 6
        x = px + a * 0.55 + rnd.uniform(-0.1, 0.1)
        h = rnd.uniform(2.6, 3.8) * (1 - 0.3 * abs(a))
        tagk(blade("Snake", x, pyy - 0.6 + 0.05 * k, Z(308), h, a, rnd.uniform(0.1, 0.25), 0.0, 0.16, 0.03, snake,
                   nseg=8, droop=0.2), "leaf", f"snake{k}")
    # ---- shelf + brass trophy, framed fish print (right)
    shelf_m = mt(WOOD[1:5], [0.35, 0.6, 0.82], light=LB, name="Shelf")
    pbox("Shelf", SHELF, 12.8, 1.2, shelf_m, "shelf", "shelf", bevel=0.04)
    for bc in (570, 618):
        pbox("Bracket", (bc, bc + 3, 172, 180), 13.0, 1.0, shelf_m, "shelf", "shelf", bevel=0.02)
    gold_m = mt(GOLD, [0.4, 0.6, 0.82], light=LB, name="Gold")
    tx = X(600)
    zb = Z(SHELF[2])
    tagk(R.loft("TrophyBase", [(tx, 12.6, zb), (tx, 12.6, zb + 0.22)], [0.42, 0.36], gold_m, 16), "trophy", "trophy")
    tagk(R.loft("TrophyStem", [(tx, 12.6, zb + 0.22), (tx, 12.6, zb + 0.62)], [0.1, 0.08], gold_m, 12), "trophy", "trophy")
    tagk(R.loft("TrophyCup", [(tx, 12.6, zb + 0.62), (tx, 12.6, zb + 0.9), (tx, 12.6, zb + 1.25), (tx, 12.6, zb + 1.42)],
                [0.1, 0.34, 0.46, 0.5], gold_m, 16), "trophy", "trophy")
    for sd in (-1, 1):
        tagk(R.loft("Handle", [(tx + sd * 0.45, 12.5, zb + 1.3), (tx + sd * 0.72, 12.5, zb + 1.18),
                                (tx + sd * 0.62, 12.5, zb + 0.88), (tx + sd * 0.3, 12.5, zb + 0.8)], 0.05, gold_m, 6),
             "trophy", "trophy")
    pic_m = mt(GOLD[:3], [0.45, 0.7], light=LB, name="PicFrame")
    c0, c1, r0, r1 = PIC_O
    for nm, rect in (("PL", (c0, c0 + 4, r0, r1)), ("PR", (c1 - 4, c1, r0, r1)), ("PT", (c0, c1, r0, r0 + 4)),
                     ("PB", (c0, c1, r1 - 4, r1))):
        pbox(nm, rect, 13.2, 0.6, pic_m, "picframe", "pic", bevel=0.04)
    pquad("Print", PIC_I, 13.6, R.m_flat(PAPER), "picture")
    # ---- cabinet / stand: top board, carcass, plinth, three raised-panel doors, brass knobs
    top_m = mt(WOOD[1:], [0.3, 0.5, 0.7, 0.88], light=LB, noise=0.05, nscale=2.0, nvec=(0.1, 1.0, 3.0), name="CabTop")
    body_m = mt(WOOD[:5], [0.3, 0.5, 0.64, 0.84], light=LB, bias=-0.08, name="CabBody")
    pbox("CabTop", CAB_TOP, 1.8, 7.0, top_m, "cab", "cabtop", bevel=0.06)
    pbox("CabBody", CAB_BODY, 2.2, 6.0, body_m, "cab", "cabbody", bevel=0.04)
    pbox("Plinth", CAB_PLINTH, 2.6, 5.0, mt(WOOD[:3], [0.4, 0.75], light=LB, name="Plinth"), "cab", "plinth")
    door_m = mt(WOOD[:5], [0.3, 0.5, 0.64, 0.84], light=LB, name="Door")
    panel_m = mt(WOOD[1:], [0.3, 0.52, 0.7, 0.9], light=LB, bias=0.03, name="Panel")
    knob_m = mt(GOLD, [0.35, 0.6, 0.85], light=LB, name="Knob")
    for i, (a, b) in enumerate(((86, 238), (244, 396), (402, 554))):
        pbox("Door", (a, b, 292, 328), 1.9, 0.3, door_m, "door", f"door{i}", bevel=0.08)
        pbox("Panel", (a + 10, b - 10, 298, 322), 1.72, 0.2, panel_m, "door", f"panel{i}", bevel=0.14)
        kx = (b - 16) if i == 0 else ((a + 16) if i == 2 else (a + b) / 2)
        ob = C.add_prim("ico", "Knob", knob_m, radius=0.14, location=(X(kx), 1.6, Z(310)), subdivisions=2)
        C.set_smooth(ob)
        tagk(ob, "knob", f"knob{i}")
    # ---- tank interior: water plane (replaced by the bands in post), gravel bed, decor, plants
    pquad("TankWater", (IN_C0 - 2, IN_C1 + 2, IN_R0 - 2, IN_R1 + 2), 9.0, R.m_flat(WT[2]), "water")
    gpts = [(X(IN_C0 - 2), Z(IN_R1 + 2))] + [(X(c), ztop(X(c))) for c in range(IN_C0 - 2, IN_C1 + 3, 2)] + \
        [(X(IN_C1 + 2), Z(IN_R1 + 2))]
    tagk(C.poly_object("Gravel", gpts[::-1], R.m_flat(SAND[1]), thickness=0.1, y=5.0), "gravel")
    vallis_b = mt(PLANT_B, [0.36, 0.56], light=LB, lam=0.6, grads=((2, -3.6, 4.6, 0.32, "world"),), name="VallisB")
    vallis_clump(-13.5, -9.6, 20, 7.6, 4.8, 8.8, rnd, vallis_b, "vl")
    vallis_clump(9.2, 13.5, 22, 7.6, 4.6, 8.8, rnd, vallis_b, "vr")
    vallis_clump(-1.4, 1.6, 6, 7.8, 2.4, 4.4, rnd, vallis_b, "vm")
    rock_m = mt(ROCK, [0.34, 0.54, 0.76], light=LB, noise=0.1, nscale=2.5, ndetail=1.0, name="Rock")
    for (x, rx, rz, y) in ((-11.7, 1.35, 1.05, 6.6), (-9.7, 0.85, 0.58, 6.1), (4.4, 0.95, 0.62, 6.2),
                           (11.3, 1.45, 1.1, 6.8), (13.0, 0.75, 0.52, 6.1)):
        rock(x, rx, rz, y, rnd, rock_m)
    drift_m = mt(DRIFT, [0.34, 0.55, 0.78], light=LB, noise=0.14, nscale=3.0, ndetail=1.0, name="Drift")
    moss_m = mt(PLANT_M[1:], [0.45, 0.72], light=LB, noise=0.15, nscale=6.0, name="Moss")
    driftwood(rnd, drift_m, moss_m)
    ludwigia(1.4, 3.4, 6.9, rnd, mt(RED, [0.42, 0.68], light=LB, name="Lud"))
    sword_plant(7.4, 6.7, rnd, mt(PLANT_M, [0.36, 0.55, 0.75], light=LB, name="Sword"))
    tagk(R.box("AirStone", (-6.5, 4.9, ztop(-6.5) + 0.08), (0.8, 0.5, 0.3), rock_m, bevel=0.1), "stone", "stone")
    grass_m = mt(PLANT_F, [0.36, 0.58, 0.8], light=LB, lam=0.6, grads=((2, -3.9, -2.9, 0.34, "world"),), name="Grass")
    for k, x in enumerate((-12.4, -10.6, -8.6, -4.4, -2.4, 0.4, 5.9, 8.6, 10.4, 12.7)):
        tuft(x, 4.7, rnd.randint(5, 8), rnd.uniform(0.35, 0.6), 0.35, 0.95, rnd, grass_m, "grass", f"gt{k}")


# ================================================================== FRONT LAYER geometry
def front_scene(rnd):
    mt = R.m_tone
    frame_m = mt(WOOD, [0.2, 0.42, 0.6, 0.74, 0.88], light=LB, noise=0.05, nscale=1.6, nvec=(0.1, 1.0, 3.0),
                 name="Frame")
    post_m = mt(WOOD, [0.2, 0.42, 0.6, 0.74, 0.88], light=LB, noise=0.05, nscale=1.6, nvec=(3.0, 1.0, 0.1),
                name="Post")
    y = -1.0
    pbox("RimTop", (TANK_C0, TANK_C1, RIM_R0, IN_R0), y, 1.6, frame_m, "frame", "frame", bevel=0.06)
    pbox("RimBot", (TANK_C0, TANK_C1, IN_R1, BOT_R1), y, 1.6, frame_m, "frame", "frame", bevel=0.06)
    pbox("PostL", (TANK_C0, IN_C0, IN_R0 - 1, IN_R1 + 1), y + 0.02, 1.6, post_m, "frame", "frame", bevel=0.06)
    pbox("PostR", (IN_C1, TANK_C1, IN_R0 - 1, IN_R1 + 1), y + 0.02, 1.6, post_m, "frame", "frame", bevel=0.06)
    hood_m = mt(WOOD, [0.2, 0.42, 0.6, 0.74, 0.88], light=LB, noise=0.05, nscale=1.6, nvec=(0.1, 1.0, 3.0),
                bias=-0.04, name="Hood")
    pbox("Hood", HOOD, y - 0.2, 2.0, hood_m, "hood", "hood", bevel=0.1)
    pbox("HoodCap", (HOOD[0] - 2, HOOD[1] + 2, HOOD[2] - 2, HOOD[2] + 2), y - 0.3, 2.1, hood_m, "hood", "hood",
         bevel=0.04)
    grass_m = mt(PLANT_F, [0.36, 0.58, 0.8], light=LB, lam=0.6, grads=((2, -4.6, -3.0, 0.34, "world"),), name="GrassF")
    for k, (x, n) in enumerate(((-12.9, 7), (-11.7, 5), (12.2, 6), (13.0, 4))):
        for i in range(n):
            xx = x + rnd.gauss(0, 0.22)
            tagk(blade("GrassF", xx, -0.4 + rnd.uniform(-0.05, 0.05), Z(IN_R1) - 0.1, rnd.uniform(0.7, 1.5),
                       (xx - x) * 3.0 + rnd.uniform(-0.4, 0.4), rnd.uniform(0.2, 0.5), 0.0, 0.055, 0.03, grass_m,
                       nseg=7, droop=0.2), "plant", f"fg{k}")


# ================================================================== back composite
WATER_Z = [(124, 132), (146, 158), (172, 186), (202, 218), (234, 252)]
SHADE = dict(wall=WALL, rail=WOOD, floor=WOOD)


def window_rgb():
    """Window content: crop of the preset sky (sun + glow + streak), hazed hills, a tree line, the lake below
    mirroring the sky, a few glitter dashes. Full-size sRGB image (only the window rect is filled)."""
    out = np.zeros((H, W, 3), np.float32)
    c0, c1, r0, r1 = WIN_I
    sc, sr = R.sun_rc(PR)
    dc = int(round(sc - 0.5)) - WIN_SUN_C
    dr = 90 - WIN_HOR
    for r in range(r0, r1):
        s = r + dr
        src = SKY_RGB if r < WIN_HOR else MSKY_RGB
        out[r, c0:c1] = src[s, c0 + dc:c1 + dc]
    rng = random.Random(3)
    hill, tree = R.hexrgb(HILLW[0]), R.hexrgb(TREEW[0])
    hill_lit, tree_top = R.hexrgb(HILLW[1]), R.hexrgb(TREEW[1])
    for c in range(c0, c1):
        dx = c - WIN_SUN_C
        v = min(1.0, max(0.0, (abs(dx) - 5) / 14.0))
        hh = int(round((9.0 if dx < 0 else 4.0) * v * v * (3 - 2 * v) * (0.8 + 0.2 * math.sin(c * 0.45))))
        th = 0 if abs(dx) < 5 else int(round(1.5 + 1.2 * (0.5 + 0.5 * math.sin(c * 1.9 + math.sin(c * 0.7) * 2))))
        for k in range(hh):
            out[WIN_HOR - 1 - k, c] = hill_lit if (k == hh - 1 and abs(dx) < 16) else hill
            out[WIN_HOR + k, c] = tree_top                         # mirrored in the calm lake (darker)
        for k in range(th):
            out[WIN_HOR - 1 - k, c] = tree_top if (k == th - 1 and rng.random() < 0.3) else tree
            out[WIN_HOR + k, c] = tree
    # glitter: sparse short dashes under the sun, fading down
    for r in range(WIN_HOR + 2, r1 - 2):
        t = (r - WIN_HOR) / (r1 - WIN_HOR)
        if rng.random() < 0.35 + 0.4 * t:
            continue
        half = 3 + 6 * t
        x = WIN_SUN_C + rng.uniform(-half, half)
        L = rng.randint(1, 3)
        col = GLIT[1] if (t < 0.4 and rng.random() < 0.5) else GLIT[0]
        out[r, int(x):int(x) + L] = R.hexrgb(col)
    return out


def quantize_groups(rgb, kid, pal):
    """Quantise every kind with its own sub-palette (GROUPS); the rest with the full palette."""
    idx = np.full((H, W), -1, np.int64)
    solid = np.zeros((H, W), bool)
    done = np.zeros((H, W), bool)
    for k, cols in list(GROUPS.items()) + [(None, None)]:
        m = (kid == k) if k is not None else ~done
        if not m.any():
            continue
        rr, cc = np.nonzero(m)
        r0, r1, c0, c1 = rr.min(), rr.max() + 1, cc.min(), cc.max() + 1
        sp = R.Pal(cols) if cols is not None else pal
        i, s = R.quantize(rgb[r0:r1, c0:c1], m[r0:r1, c0:c1], sp, dither=True, ox=c0, oy=r0)
        lut = np.array([pal.index(h) for h in sp.hex])
        sub = m[r0:r1, c0:c1]
        idx[r0:r1, c0:c1][sub] = lut[i[sub]]
        solid[r0:r1, c0:c1][sub] = s[sub]
        done |= m
    return idx, solid


def reduce_region(idx, pal, mask, nmax):
    """Palette economy inside one region: merge the colour with the smallest (pixel count x OKLab distance to
    its nearest neighbour in the region) into that neighbour until <= nmax colours are left."""
    out = idx.copy()
    while True:
        u, cnt = np.unique(out[mask], return_counts=True)
        if len(u) <= nmax:
            return out
        lab = pal.lab[u]
        d = np.sqrt(((lab[:, None, :] - lab[None, :, :]) ** 2).sum(-1))
        np.fill_diagonal(d, 1e9)
        nn = d.argmin(1)
        cost = cnt * d[np.arange(len(u)), nn]
        j = int(cost.argmin())
        out[mask & (out == u[j])] = u[nn[j]]


def paint_gravel(idx, pal, gm, rnd):
    """Hand-painted gravel: 4 depth tones joined by dash dithering + 2x2 / 3x2 / 2x1 pebbles (lit top-left,
    dark bottom-right), mostly sand with some grey stones, darker deeper down."""
    rows = np.arange(H)[:, None]
    d = rows - GTOP[None, :]
    S = [pal.index(c) for c in SAND]
    Rk = [pal.index(c) for c in ROCK]
    rng = random.Random(9)
    base = np.full((H, W), S[0])
    base[d < 13] = S[1]
    base[d < 6] = S[2]
    base[d <= 0] = S[3]
    for dd, lo in ((5, S[1]), (6, S[2]), (12, S[0]), (13, S[1])):
        for r in range(H):
            m = (d[r] == dd) & R.dash_mask(W, 0.5, 2, 5, rng)
            base[r, m] = lo
    idx[gm] = base[gm]
    fams = (S, Rk)
    for r in range(int(GTOP[IN_C0:IN_C1].min()) - 1, IN_R1, 2):
        c = IN_C0 + rnd.randint(0, 2)
        while c < IN_C1:
            fam = fams[0] if rnd.random() < 0.88 else fams[1]
            dd = r - GTOP[c]
            k = 0 if dd < 5 else (1 if dd < 11 else 2)
            wdt = rnd.choice((2, 2, 3))
            hgt = 2 if rnd.random() < 0.72 else 1
            if rnd.random() < 0.75:
                lt, bs, dk = fam[max(0, 3 - k)], fam[max(0, 2 - k)], fam[max(0, 1 - k)]
                for j in range(hgt):
                    for i in range(wdt):
                        rr, cc = r + j, c + i
                        if not (0 <= rr < H and 0 <= cc < W and gm[rr, cc]):
                            continue
                        first = i == 0 and j == 0
                        last = i == wdt - 1 and j == hgt - 1
                        idx[rr, cc] = lt if first else (dk if last else bs)
            c += wdt + rnd.randint(1, 2)
    return idx


def paint_floor(idx, pal, fm, rnd):
    """Floorboards parallel to the wall, taller towards the viewer (pseudo-perspective): alternating board
    tones, a dark seam on top of each board, staggered butt joints; the window's sun patch (+1 step, Bayer
    edges) skewed towards the viewer; contact shade along the wall and under the cabinet."""
    Wd = [pal.index(c) for c in WOOD]
    cols = np.arange(W)
    r, hgt, si = FLOOR_ROW, 4, 0
    while r < H:
        b = min(H, r + hgt)
        tone = Wd[3] if si % 2 == 0 else Wd[2]
        for rr in range(r, b):
            idx[rr, fm[rr]] = tone
        idx[r, fm[r]] = Wd[1]
        x = rnd.randint(10, 150)
        while x < W:
            for rr in range(r, b):
                if fm[rr, x]:
                    idx[rr, x] = Wd[1]
            x += rnd.randint(120, 220)
        r, si, hgt = b, si + 1, min(16, hgt + 2)
    for rr in range(FLOOR_ROW + 1, H):
        t = (rr - FLOOR_ROW) / (H - FLOOR_ROW)
        a0, a1 = WIN_O[0] + 44 * t, WIN_O[1] + 8 + 90 * t
        m = fm[rr] & (cols >= a0) & (cols < a1)
        edge = ((cols < a0 + 5) | (cols >= a1 - 5)) & (R.bayer(1, W, 0, rr)[0] < 0.5)
        m &= ~edge & (idx[rr] != Wd[1])
        idx[rr, m] = R.ramp_step(pal, WOOD, idx[rr], +1)[m]
    shade = fm.copy()
    shade[FLOOR_ROW + 4:] = False
    shade[FLOOR_ROW + 2:FLOOR_ROW + 4] &= R.bayer(2, W, 0, FLOOR_ROW + 2) < 0.5
    idx[shade] = R.ramp_step(pal, WOOD, idx, -1)[shade]
    cab = fm.copy()
    cab[FLOOR_ROW + 3:] = False
    cab[:, :CAB_PLINTH[0] - 2] = False
    cab[:, CAB_PLINTH[1] + 6:] = False
    idx[cab] = R.ramp_step(pal, WOOD, idx, -1)[cab]
    return idx


def bevel_edges(idx, pal, ids, mask, ramp):
    """Hand bevel of flat boxes (sub-pixel geometric bevels vanish): per object, the top / left edge one step
    lighter on its ramp, the bottom / right edge one step darker; the two mixed corners keep the face tone."""
    n = R._nb4(ids, -1)
    lit = mask & ((n[..., 0] != ids) | (n[..., 2] != ids))
    dark = mask & ((n[..., 1] != ids) | (n[..., 3] != ids))
    up, dn = R.ramp_step(pal, ramp, idx, +1), R.ramp_step(pal, ramp, idx, -1)
    out = idx.copy()
    out[dark & ~lit] = dn[dark & ~lit]
    out[lit & ~dark] = up[lit & ~dark]
    return out


def drop_shadow(idx, pal, kid, caster, dy, dx, on=("wall", "rail")):
    sh = R.shift(caster, dy, dx, False) & ~caster & np.isin(kid, on)
    for k, ramp in SHADE.items():
        m = sh & (kid == k)
        idx[m] = R.ramp_step(pal, ramp, idx, -1)[m]
    return idx


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    C.ortho_camera(0, 0, W, H, PPU)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_back")
    kid = R.kind_map(ps)
    win = kid == "window"
    water = kid == "water"
    gravel = kid == "gravel"
    rows = np.arange(H)[:, None]
    cols = np.arange(W)[None, :]
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    wr = window_rgb()
    rgb[win] = wr[win]
    idx, solid = quantize_groups(rgb, kid, pal)
    idx = R.despeckle(idx, ps["id"], protect=~solid | win, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.35, steps=1)
    lk = ("plant", "wood", "rock", "sword", "red", "moss", "stone", "grass", "cab", "door", "knob", "pot", "leaf",
          "shelf", "trophy", "sill")
    idx = np.where(np.isin(kid, lk), lined, idx)
    idx = bevel_edges(idx, pal, ps["id"], np.isin(kid, ("door", "cab", "winframe", "sill", "shelf", "rail")), WOOD)
    idx = bevel_edges(idx, pal, ps["id"], kid == "picframe", GOLD)
    idx = reduce_region(idx, pal, win, 16)                              # the window: <= 16 sky / lake colours
    # ---- wallpaper stripes (one ramp step, 6 px wide; the frieze too); the horizontal light falloff
    #      steps only on stripe boundaries (every 6 px column group takes the tone of its centre column)
    wallm = kid == "wall"
    cc = np.clip((np.arange(W) // 6) * 6 + 3, 0, W - 1)
    snap = wallm & wallm[:, cc]
    idx[snap] = idx[:, cc][snap]
    stripe = wallm & ((cols // 6) % 2 == 1)
    idx[stripe] = R.ramp_step(pal, WALL, idx, -1)[stripe]
    # ---- soft window glow on the wall: +1 step within ~8 px of the frame, a 2 px Bayer hand-over
    c0, c1, r0, r1 = WIN_O
    dxw = np.maximum(np.maximum(c0 - cols, cols - (c1 - 1)), 0)
    dyw = np.maximum(np.maximum(r0 - rows, rows - (r1 + 5)), 0)
    dw = np.sqrt(dxw ** 2 + (dyw * 1.3) ** 2)
    glow = (kid == "wall") & (dw > 0) & ((dw < 8) | ((dw < 10) & (R.bayer(H, W) > 0.5)))
    idx[glow] = R.ramp_step(pal, WALL, idx, +1)[glow]
    # ---- floorboards
    idx = paint_floor(idx, pal, kid == "floor", random.Random(23))
    # ---- tank water: dash-dithered depth bands, lamp-lit air gap, meniscus, surface underside reflection
    wband = R.water_bands(pal, WT, WATER_Z, random.Random(5), -300, 700)
    under = water & (rows >= SURF_ROW)
    idx[under] = wband[under]
    idx[water & (rows < SURF_ROW)] = pal.index(AIR)
    idx[water & (rows == SURF_ROW)] = pal.index(MENISC)
    rng = random.Random(13)
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    for r, t in ((SURF_ROW + 1, 0.55), (SURF_ROW + 2, 0.3), (SURF_ROW + 4, 0.12)):
        m = water[r] & R.dash_mask(W, t, 3, 10, rng)
        idx[r, m] = up1[r, m]
    # ---- gravel: hand-painted pebbles
    idx = paint_gravel(idx, pal, gravel, random.Random(19))
    # ---- 16-bit drop shadows on the wall (light from the upper left / front)
    fa = _ST.get("front_a")
    if fa is None:
        p = os.path.join(R.WORK, "front_alpha.npy")
        fa = np.load(p) if os.path.exists(p) else np.zeros((H, W), bool)
    tank = fa | np.isin(kid, ("water", "gravel", "cab", "door", "knob", "plant", "rock", "wood", "moss", "sword",
                               "red", "stone", "grass"))
    idx = drop_shadow(idx, pal, kid, tank, 5, 6)
    idx = drop_shadow(idx, pal, kid, np.isin(kid, ("sill", "winframe")), 3, 2)
    idx = drop_shadow(idx, pal, kid, np.isin(kid, ("picframe", "picture", "shelf", "trophy")), 2, 3)
    idx = drop_shadow(idx, pal, kid, np.isin(kid, ("pot", "leaf")), 2, 4, on=("wall", "rail"))
    # ---- warm lamp shafts: translucent in the water (apply_shaft), decor in the shaft core one step lighter
    inside = np.zeros((H, W), bool)
    inside[SURF_ROW:IN_R1, IN_C0:IN_C1] = True
    amap = np.zeros((H, W))
    for k, s in enumerate(SHAFTS):
        prs = PR.copy(shaft=s)
        i2, pal = R.apply_shaft(idx, pal, prs, seed=5 + k)
        idx = np.where(inside & water & (rows > SURF_ROW), i2, idx)
        amap = np.maximum(amap, R.light_shaft(prs))
    # decor in the shaft: lit patch with horizontal dash edges (retro16 water-style, no checker on pebbles)
    core = inside & (amap > 0.3 + 0.35 * R.dash_threshold(H, W, random.Random(41), 2, 6))
    for k, ramps in LIT.items():
        m = core & (kid == k)
        for ramp in ramps:
            idx[m] = R.ramp_step(pal, ramp, idx, +1)[m]
    idx = reduce_region(idx, pal, water, 18)                            # water + shafts: <= 18 colours
    # ---- framed fish print: a hybrid fish sprite on paper, in colours the back layer already uses
    pic = kid == "picture"
    idx[pic] = pal.index(PAPER)
    used = R.Pal([pal.hex[i] for i in np.unique(idx[~pic])])
    for fid in ("golden_carp", "crucian_carp", "bluegill"):
        fp = os.path.join(R.OUT, "fish", f"{fid}_0.png")
        if not os.path.exists(fp):
            continue
        spr = R.load_png(fp)
        h, w = spr.shape[:2]
        pc0, pc1, pr0, pr1 = PIC_I
        if w > pc1 - pc0 - 4 or h > pr1 - pr0 - 4:
            continue
        x0, y0 = (pc0 + pc1 - w) // 2, (pr0 + pr1 - h) // 2
        m = spr[..., 3] > 0.5
        q, _ = R.quantize(spr[..., :3], m, used, dither=False)
        lut = np.array([pal.index(hx) for hx in used.hex])
        reg = idx[y0:y0 + h, x0:x0 + w]
        reg[m] = lut[q[m]]
        break
    img = R.to_rgba(idx, pal)
    R.save_png(img, os.path.join(R.OUT, f"{SID}_back.png"))
    if os.environ.get("AQ_DEBUG"):
        for k in np.unique(kid):
            m = kid == k
            print("HYB debug kind", repr(str(k)), "px", int(m.sum()), "colours", len(np.unique(idx[m])))
    wl = [round(R.lum(c), 3) for c in WT]
    swim = water & (rows >= row_of(LAYOUT["swimMaxY"]) - 8) & (rows <= row_of(LAYOUT["swimMinY"]) + 8)
    sl = R.oklab(img[swim][:, :3])[:, 0]
    print("HYB aquarium back colours", R.count_colours(img), "palette", len(pal), "window", len(np.unique(idx[win])),
          "water", len(np.unique(idx[water])), "water band L", wl,
          "swim-area water L min/mean/max", round(float(sl.min()), 3), round(float(sl.mean()), 3), round(float(sl.max()), 3))
    return img


# ================================================================== front composite
def glints(idx, pal):
    """Glass glints: two short '/' strokes in the top-left corner, one in the bottom-right corner."""
    hi, lo = pal.index(GLASS[1]), pal.index(GLASS[0])
    for (r, c, n, col) in ((127, 104, 13, hi), (128, 110, 9, lo), (126, 115, 4, lo), (271, 521, 7, lo), (271, 526, 4, hi)):
        for k in range(n):
            rr, cc = r - k, c + k
            if IN_R0 <= rr < IN_R1 and IN_C0 <= cc < IN_C1 and idx[rr, cc] < 0:
                idx[rr, cc] = col
    return idx


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    front_scene(rnd)
    C.ortho_camera(0, 0, W, H, PPU)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_front")
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    thin = kid == "plant"
    idx = R.despeckle(idx, ps["id"], protect=~solid | thin, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.08, steps=1)
    solid_k = np.isin(kid, ("frame", "hood"))
    idx = bevel_edges(idx, pal, ps["id"], solid_k, WOOD)       # lit top/left, dark bottom/right (glass opening too)
    idx, pal = R.rim_light(idx, pal, PR, mask=solid_k)
    base = np.where(thin, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    idx = np.where(thin & (idx >= 0), idx, ol)
    idx = glints(idx, pal)
    fa = (idx >= 0) & ~thin
    _ST["front_a"] = fa
    np.save(os.path.join(R.WORK, "front_alpha.npy"), fa)
    img = R.to_rgba(idx, pal)
    R.save_png(img, os.path.join(R.OUT, f"{SID}_front.png"))
    dark = min(R.lum(pal.hex[i]) for i in np.unique(idx[idx >= 0]))
    print("HYB aquarium front colours", R.count_colours(img), "palette", len(pal), "darkest L", round(dark, 3))
    return img


def write_json():
    d = dict(LAYOUT)
    d["waterTop"] = WT[0]
    d["waterDeep"] = WT[-1]
    with open(os.path.join(R.OUT, f"{SID}.json"), "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=1)
    same = [k for k in LAYOUT if k not in ("waterTop", "waterDeep") and LAYOUT[k] != d[k]]
    print("HYB aquarium json keys same", list(LAYOUT) == list(d), "gameplay diffs", same,
          "waterTop", d["waterTop"], "waterDeep", d["waterDeep"])
    return d


def main():
    C.reset_scene()
    which = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["back", "front"]
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    write_json()
    for f in (f"{SID}_back.png", f"{SID}_front.png"):
        p = os.path.join(R.OUT, f)
        if os.path.exists(p):
            a = R.load_png(p)
            g = R.load_png(os.path.join(C.SPRITES, "Stages", f))
            print("CHECK", f, a.shape[:2], "game", g.shape[:2], "colours", R.count_colours(a))
    print("HYB AQUARIUM done")


if __name__ == "__main__":
    main()
