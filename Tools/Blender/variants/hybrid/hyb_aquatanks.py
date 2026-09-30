"""
hybrid - the aquarium's 5 tank levels (GameDatabase tank_0..tank_4), each a physically bigger tank in the same
retro16 golden-hour style as hyb_aquarium.py (whose palettes, pebble / floor / water painters and layout helpers
this module reuses by rebinding hyb_aquarium's layout globals per level; hyb_aquarium.py itself is untouched and
still renders the legacy single-tank aquarium_back/front.png).

  0 작은 어항        desk tank on the walnut sideboard   canvas  640x400  view h 270 (480x270 @16:9)
  1 중형 수조        wall tank on the cabinet (the legacy art's layout: glass 102..538 x 110..274, ledge row 284)
  2 대형 수조        large tank, 4-door cabinet           canvas  856x536  view h 360 (640x360)
  3 아쿠아리움       aquarium hall glass wall (slate hall, steel frame, viewing counter)
                                                         canvas 1024x640  view h 432 (768x432)
  4 황금 대수족관    golden grand aquarium (marble hall, gilded frame + cornice, pilasters, 5-door counter)
                                                         canvas 1280x800  view h 540 (960x540)

World <-> pixel for every level: the sprites are centred at world (0, 0), 16 px per unit:
col = W/2 + 16 x, row = H/2 - 16 y. The camera stays centred at (0, 0); the view height grows with the tank
(the canvas is drawn for aspects up to the PixelView limits: w <= h*640/270, h <= w*400/480).
Per level: aquarium_<n>_back.png (opaque), aquarium_<n>_front.png (frame, transparent), aquarium_tank_<n>.json
(water / glass / gravel / surface / ledge / decor slot / UI safe-area layout).
Outputs: _tmp/aquatanks/, preview sheet _tmp/aquatanks/levels_sheet.png; `-- install` copies into Assets.
Run: blender -b --python variants/hybrid/hyb_aquatanks.py [-- 0 1 2 3 4] [install]
"""
import os
import sys
import math
import json
import random
import shutil
import uuid
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hyb_aquarium as A  # noqa: E402  (importing renders nothing: palettes, L1 layout, painters)
import hyb_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402

OUTD = os.path.join(R.BL, "_tmp", "aquatanks")
os.makedirs(OUTD, exist_ok=True)
WORK0 = os.path.join(R.OUT, "work", "aquatanks")
PPU = 16
G = R.grade_hex
LB = A.LB
MT = R.m_tone

# ------------------------------------------------------------------ reference (level 1 = the legacy layout)
REF_GLASS = (102, 538, 110, 274)
REF_W, REF_H = 640, 400
ZTOP_REF = A.ztop                      # legacy gravel line (world z of the level-1 tank)
GTOP_REF = A.GTOP.copy()
REF_SHAFTS = [dict(s) for s in A.SHAFTS]
REF_GROUPS = dict(A.GROUPS)
REF_ZB = (REF_H / 2 - REF_GLASS[3]) / PPU          # -4.625
REF_ZT = (REF_H / 2 - REF_GLASS[2]) / PPU          # 5.625
REF_HW = (REF_GLASS[1] - REF_GLASS[0]) / 2 / PPU   # 13.625
AQ_SLOTS = [  # AquaTank.Slots (id, kind, col, row, unlock level, order) - canvas px of the legacy art
    ("light", "light", 320, 86, 0, 42), ("back_c", "back", 338, 261, 0, 3), ("mid_l", "mid", 314, 266, 0, 5),
    ("mid_r", "mid", 412, 268, 0, 6), ("front_l", "front", 124, 274, 0, 31), ("front_c", "front", 364, 274, 0, 31),
    ("back_l", "back", 196, 260, 1, 2), ("back_r", "back", 472, 263, 2, 4), ("mid_rr", "mid", 488, 268, 3, 7),
    ("front_r", "front", 516, 274, 4, 31)]
SLOT_MAX = dict(light=(48, 24), back=(128, 100), mid=(56, 52), front=(40, 40))

# ------------------------------------------------------------------ extra ramps (hall / grand)
STEEL = G(["#1e2432", "#2c3444", "#3e485a", "#546276", "#72829a", "#98a8bc"])
SLATE = G(["#1c2232", "#242c3e", "#2e384c", "#3a465a", "#48566a"])
CARPET = G(["#2a2232", "#382c40", "#48384e"])
MARBLE = G(["#8c7c6c", "#a89682", "#c2b096", "#d8c8aa", "#ece0c4"])
GOLDF = G(["#4a3220"]) + A.GOLD                     # gilded frame: a deep umber below the brass ramp
VELVET = G(["#4a1e28", "#6a2a34", "#8e3a40"])

STYLE = dict(
    home=dict(wall=A.WALL, rail=A.WOOD, floor=A.WOOD, frame=A.WOOD, panel=A.WOOD, knobs=True, window=True),
    hall=dict(wall=SLATE, rail=STEEL, floor=CARPET, frame=STEEL, panel=STEEL, knobs=False, window=False),
    grand=dict(wall=MARBLE, rail=GOLDF, floor=MARBLE, frame=GOLDF, panel=A.WOOD, knobs=True, window=False),
)


# ------------------------------------------------------------------ the levels (canvas px, top-down)
def level(n, key, name, style, W, H, vh, glass, post, rimt, hood_h, cab_h, doors, ledge, shafts, **kw):
    c0, c1, r0, r1 = glass
    L = dict(n=n, key=key, name=name, style=style, W=W, H=H, vh=vh, IN=(c0, c1, r0, r1), post=post)
    L["TANK"] = (c0 - post, c1 + post)
    L["RIM_R0"] = r0 - rimt
    L["BOT_R1"] = r1 + max(8, post)
    L["HOOD"] = (c0 - post - 4, c1 + post + 4, r0 - rimt - hood_h, r0 - rimt)
    T = L["BOT_R1"]
    cab = kw.get("cab", (c0 - post - 20, c1 + post + 20))
    L["CAB_TOP"] = (cab[0], cab[1], T, T + 6)
    L["CAB_BODY"] = (cab[0] + 4, cab[1] - 4, T + 6, T + cab_h - 6)
    L["CAB_PLINTH"] = (cab[0] + 10, cab[1] - 10, T + cab_h - 6, T + cab_h)
    L["FLOOR"] = T + cab_h
    L["doors"] = doors
    L["LEDGE_ROW"] = T + 2
    L["ledge"] = ledge
    L["nshafts"] = shafts
    L["view_top"] = (H - vh) // 2
    L["RAIL"] = (L["view_top"] - 31, L["view_top"] - 25)
    L.update(kw)
    return L


LEVELS = [
    level(0, "desk", "작은 어항", "home", 640, 400, 270, (176, 464, 142, 274), 6, 8, 14, 56, 3,
          dict(bags=[104, 138], tub=172, cooler=208, sponge=432, net=470, siphon=508), 2,
          cab=(76, 564), win=(94, 104), pot=40, pic=[(486, 110)], shelf=(480, 196)),
    level(1, "wall", "중형 수조", "home", 640, 400, 270, (102, 538, 110, 274), 6, 8, 14, 56, 3,
          dict(bags=[160, 196], tub=232, cooler=272, sponge=452, net=486, siphon=546), 3,
          cab=(76, 564), win=(20, 68), pot=40, pic=[(570, 90)], shelf=(562, 168)),
    level(2, "large", "대형 수조", "home", 856, 536, 360, (136, 720, 146, 370), 6, 8, 14, 56, 4,
          dict(bags=[194, 230], tub=266, cooler=306, sponge=634, net=668, siphon=728), 4,
          win=(54, 104), pot=82, pic=[(752, 126)], shelf=(744, 204)),
    level(3, "hall", "아쿠아리움", "hall", 1024, 640, 432, (160, 864, 172, 436), 12, 12, 16, 72, 4,
          dict(bags=[218, 254], tub=290, cooler=330, sponge=778, net=812, siphon=872), 5,
          pic=[(66, 196), (904, 196)], rivets=True),
    level(4, "grand", "황금 대수족관", "grand", 1280, 800, 540, (200, 1080, 214, 546), 14, 14, 22, 80, 5,
          dict(bags=[258, 294], tub=330, cooler=370, sponge=994, net=1028, siphon=1088), 6,
          pic=[(54, 236)], shelf=(1166, 300), pot=104, pilasters=[(134, 180), (1100, 1146)], rosettes=True),
]

# ------------------------------------------------------------------ per-level state (bound into hyb_aquarium)
S = {}


def X(c):
    return (c - S["W"] / 2) / PPU


def Z(r):
    return (S["H"] / 2 - r) / PPU


def mx(x):
    """Level-1 tank x (world) -> this level's tank x."""
    return S["gcx"] + x * S["sx"]


def mz(z):
    """Level-1 tank z (world) -> this level's tank z (anchored at the glass bottom)."""
    return S["zb"] + (z - REF_ZB) * S["sy"]


def ztop_level(x):
    return S["zb"] + (ZTOP_REF((x - S["gcx"]) / S["sx"]) - REF_ZB) * S["sy"]


def bind(L):
    """Rebind hyb_aquarium's layout globals to level L (its painters read them at call time)."""
    W, H = L["W"], L["H"]
    c0, c1, r0, r1 = L["IN"]
    S.clear()
    S.update(L=L, W=W, H=H, gcx=((c0 + c1) / 2 - W / 2) / PPU, zb=(H / 2 - r1) / PPU)
    S["sx"] = (c1 - c0) / 2 / PPU / REF_HW
    S["sy"] = (r1 - r0) / PPU / (REF_ZT - REF_ZB)
    air = max(7, int(round(7 * S["sy"])))
    S["SURF_ROW"] = r0 + air
    A.W, A.H = W, H
    A.TANK_C0, A.TANK_C1 = L["TANK"]
    A.IN_C0, A.IN_C1, A.IN_R0, A.IN_R1 = c0, c1, r0, r1
    A.RIM_R0, A.BOT_R1 = L["RIM_R0"], L["BOT_R1"]
    A.SURF_ROW = S["SURF_ROW"]
    A.HOOD = L["HOOD"]
    A.CAB_TOP, A.CAB_BODY, A.CAB_PLINTH = L["CAB_TOP"], L["CAB_BODY"], L["CAB_PLINTH"]
    A.FLOOR_ROW = L["FLOOR"]
    if L.get("win"):
        wc, wr = L["win"]
        A.WIN_O = (wc, wc + 56, wr, wr + 112)
        A.WIN_I = (wc + 4, wc + 52, wr + 4, wr + 108)
        A.WIN_HOR, A.WIN_SUN_C = wr + 88, wc + 38
    else:
        A.WIN_O = (-999, -998, -999, -998)
    A.ztop = ztop_level
    A.GTOP = np.array([int(math.ceil((H / 2 - ztop_level(X(c + 0.5)) * PPU) - 0.5)) for c in range(W)])
    A.XLIM = (X(c0) + 0.15, X(c1) - 0.15)
    st = STYLE[L["style"]]
    A.SHADE = dict(wall=st["wall"], rail=st["rail"], floor=st["floor"], pilaster=MARBLE)
    grp = dict(REF_GROUPS)
    grp.update(wall=st["wall"], rail=st["rail"], floor=st["floor"], door=st["panel"], pilaster=MARBLE,
               pilcap=A.GOLD, plaque=STEEL, runner=VELVET)
    A.GROUPS = grp
    # water depth bands and lamp shafts, mapped from the legacy tank
    t = lambda r: S["SURF_ROW"] + (r - 117) / (274 - 117) * (r1 - S["SURF_ROW"])      # noqa: E731
    A.WATER_Z = [(int(round(t(a))), int(round(t(b)))) for a, b in [(124, 132), (146, 158), (172, 186), (202, 218),
                                                                    (234, 252)]]
    if L["n"] == 1:
        A.SHAFTS = [dict(s) for s in REF_SHAFTS]
    else:
        n = L["nshafts"]
        pat = [(26, 44, 0.6), (34, 58, 0.5), (24, 40, 0.65), (30, 50, 0.55)]
        sh = []
        for k in range(n):
            wt, wb, fade = pat[k % len(pat)]
            u = (k + 0.5) / n
            ct = c0 + u * (c1 - c0) - 2 * S["sx"]
            sh.append(dict(col=A.LAMP, amount=0.13, top=(ct, S["SURF_ROW"], wt * S["sy"]),
                           bottom=(ct + 36 * S["sy"], r1, wb * S["sy"]), fade=fade))
        A.SHAFTS = sh
    A._ST.clear()


# ------------------------------------------------------------------ geometry helpers (scaled from hyb_aquarium)
def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


def vallis_clump(x0, x1, n, y, hmin, hmax, rnd, mat, grp):
    for i in range(n):
        x = rnd.uniform(x0, x1)
        h = rnd.uniform(hmin, hmax)
        z0 = ztop_level(x) - 0.3
        h = min(h, Z(S["SURF_ROW"] + 5) - z0)
        dirx = rnd.choice((-1, 1)) * rnd.uniform(0.3, 1.0)
        tagk(A.blade("Vallis", x, y + rnd.uniform(-0.3, 0.3), z0, h, dirx, rnd.uniform(0.05, 0.22), rnd.uniform(-1, 1),
                     rnd.uniform(0.07, 0.1), 0.03, mat, nseg=14, xlim=A.XLIM), "plant", grp)


def tuft(x, y, n, spread, hmin, hmax, rnd, mat, kind, grp):
    for i in range(n):
        xx = x + rnd.gauss(0, spread * 0.35)
        z0 = ztop_level(xx) - 0.12
        dirx = (xx - x) / max(1e-3, spread) * 1.4 + rnd.uniform(-0.4, 0.4)
        tagk(A.blade("Grass", xx, y + rnd.uniform(-0.05, 0.05), z0, rnd.uniform(hmin, hmax), dirx,
                     rnd.uniform(0.15, 0.45), 0.0, 0.05, 0.028, mat, nseg=6, droop=0.2, xlim=A.XLIM), kind, grp)


def sword_plant(x, y, rnd, mat, k=1.0, tag="sword"):
    z0 = ztop_level(x) - 0.25
    kw = math.sqrt(k)
    for j in range(9):
        ang = math.radians(-62 + 124 * j / 8 + rnd.uniform(-7, 7))
        L = rnd.uniform(2.4, 3.9) * k * (1.0 - 0.25 * abs(math.sin(ang)))
        pts, ws = [], []
        for i in range(12):
            t = i / 11
            a = ang * (0.5 + 0.9 * t)
            xx = min(A.XLIM[1] - 0.1, max(A.XLIM[0] + 0.1, x + math.sin(a) * L * t))
            pts.append((xx, y + j * 0.03, z0 + math.cos(a) * L * t))
            ws.append(0.035 if t < 0.28 else (0.03 + 0.24 * max(0.0, math.sin(math.pi * (t - 0.28) / 0.72)) ** 0.8) * kw)
        tagk(A.ribbon("Sword", pts, ws, mat), "sword", f"{tag}{j}")


def ludwigia(x0, x1, y, rnd, mat, k=1.0, stems=6, tag="lud"):
    for s in range(stems):
        x = x0 + (x1 - x0) * s / (stems - 1) + rnd.uniform(-0.2, 0.2)
        z0 = ztop_level(x) - 0.2
        h = rnd.uniform(2.6, 4.3) * k
        lean = rnd.uniform(-0.25, 0.25)
        g = f"{tag}{s}"
        pts = [(x + lean * h * (i / 6) ** 2, y, z0 + h * i / 6) for i in range(7)]
        tagk(A.ribbon("Stem", pts, [0.03] * 7, mat), "red", g)
        z = z0 + 0.5
        while z < z0 + h - 0.1:
            t = (z - z0) / h
            cx = x + lean * h * t * t
            for sd in (-1, 1):
                rot = Matrix.Rotation(math.radians(-sd * (28 + 18 * t)), 4, "Y")
                ob = R.ellipsoid("Leaf", (cx + sd * 0.2, y - 0.04, z + 0.05), (0.24 - 0.06 * t, 0.04, 0.085), mat,
                                 segs=8, rings=5, rot=rot)
                tagk(ob, "red", g)
            z += 0.33


def rock(x, rx, rz, y, rnd, mat, sink=0.45):
    ob = C.add_prim("ico", "Rock", mat, radius=1.0, location=(0, 0, 0), subdivisions=2)
    for v in ob.data.vertices:
        co = v.co * (1.0 + rnd.uniform(-0.1, 0.1))
        if co.z > 0.55:
            co.z = 0.55 + (co.z - 0.55) * 0.5
        v.co = co
    zb = ztop_level(x)
    ob.matrix_world = Matrix.Translation((x, y, zb + rz * (1 - sink) - 0.1)) @ Matrix.Diagonal((rx, 0.8, rz, 1))
    C.set_smooth(ob, False)
    return tagk(ob, "rock", f"rock{x:.2f}")


def driftwood(rnd, mat, mossm, k):
    y = 6.3
    limbs = [([(-9.8, -3.9), (-8.0, -3.35), (-6.2, -2.35), (-4.6, -1.15), (-3.4, 0.2), (-2.7, 1.5)],
              [0.34, 0.3, 0.26, 0.2, 0.13, 0.06]),
             ([(-6.1, -2.3), (-6.6, -0.9), (-7.4, 0.3), (-7.9, 1.05)], [0.16, 0.12, 0.08, 0.045]),
             ([(-4.4, -1.0), (-3.0, -1.12), (-1.7, -0.55), (-0.9, 0.05)], [0.14, 0.1, 0.07, 0.04]),
             ([(-8.3, -3.45), (-7.0, -3.95), (-5.7, -4.05)], [0.2, 0.15, 0.1])]
    limbs = [([(mx(px), mz(pz)) for px, pz in pts], [r * k for r in rads]) for pts, rads in limbs]
    for j, (pts, rads) in enumerate(limbs):
        p3 = [(px, y + 0.02 * j, pz) for px, pz in pts]
        tagk(R.loft("Drift", p3, [r * 1.3 for r in rads], mat, 10), "wood", "drift")
    for j in range(int(round(16 * max(1.0, k)))):
        pts, rads = limbs[j % 3]
        i = rnd.randrange(len(pts) - 1)
        t = rnd.random()
        px = pts[i][0] + (pts[i + 1][0] - pts[i][0]) * t
        pz = pts[i][1] + (pts[i + 1][1] - pts[i][1]) * t
        r = rads[i] * (1 - t) + rads[i + 1] * t
        rr = rnd.uniform(0.12, 0.22) * math.sqrt(k)
        ob = C.add_prim("ico", "Moss", mossm, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((px + rnd.uniform(-0.1, 0.1), y - 0.25, pz + r * 0.7)) @ \
            Matrix.Diagonal((rr * 1.4, rr, rr * 0.8, 1))
        C.set_smooth(ob)
        tagk(ob, "moss", "moss")


def pbox(name, rect, y, depth, mat, kind, grp=None, bevel=0.0):
    return A.pbox(name, rect, y, depth, mat, kind, grp, bevel)


def pquad(name, rect, y, mat, kind, grp=None):
    return A.pquad(name, rect, y, mat, kind, grp)


def bounds(n, a=0.2, b=0.76):
    return [a + (b - a) * i / (n - 2) for i in range(n - 1)] if n > 2 else [0.5]


# ================================================================== BACK LAYER geometry
def room(L, rnd):
    W, H = L["W"], L["H"]
    st = STYLE[L["style"]]
    FL = L["FLOOR"]
    hw = W / 2 / PPU
    rail_r = L["RAIL"]
    grads = ((0, -hw, hw, -0.42), (2, Z(rail_r[1]), Z(rail_r[1]) + 0.5, -0.22), (2, Z(FL), Z(FL - 34), 0.1))
    if L["style"] == "home":
        wall_m = MT(st["wall"], [0.2, 0.34, 0.48, 0.62, 0.76], soft=0.0, light=LB, lam=0.5, bias=0.4, grads=grads,
                    name="Wall")
    elif L["style"] == "hall":
        # the hall is dark; the wall picks up the tank's glow towards the glass
        wall_m = MT(SLATE, bounds(len(SLATE), 0.22, 0.7), light=LB, lam=0.35, bias=0.28,
                    grads=((2, Z(rail_r[1]), Z(rail_r[1]) + 0.5, -0.2), (2, Z(FL), Z(FL - 40), 0.08)),
                    name="WallHall")
    else:
        wall_m = MT(MARBLE, bounds(len(MARBLE), 0.24, 0.8), light=LB, lam=0.5, bias=0.42, grads=grads,
                    name="WallMarble")
    pquad("Wall", (0, W, 0, FL), 14.0, wall_m, "wall")
    rail_m = MT(st["rail"][1:5], [0.3, 0.55, 0.8], light=LB, grads=((0, -hw, hw, -0.2),), bias=0.1, name="Rail")
    pbox("Rail", (0, W, rail_r[0], rail_r[1]), 13.5, 0.5, rail_m, "rail", bevel=0.05)
    pbox("Skirt", (0, W, FL - 12, FL), 13.5, 0.5, rail_m, "rail", "skirt", bevel=0.05)
    if L["style"] == "grand":
        pbox("Frieze", (0, W, rail_r[1], rail_r[1] + 3), 13.45, 0.5, rail_m, "rail", "frieze", bevel=0.02)
    pquad("Floor", (0, W, FL, H), 1.0, R.m_flat(st["floor"][min(3, len(st["floor"]) - 1)]), "floor")
    # ---- window (home)
    if L.get("win"):
        pquad("WinGlass", A.WIN_I, 13.9, R.m_flat(A.WALL[0]), "window")
        fr_m = MT(A.WOOD[2:], [0.45, 0.62, 0.8], light=LB, name="WinFrame")
        c0, c1, r0, r1 = A.WIN_O
        for nm, rect in (("WL", (c0, c0 + 4, r0, r1)), ("WR", (c1 - 4, c1, r0, r1)), ("WT", (c0, c1, r0, r0 + 4)),
                         ("WB", (c0, c1, r1 - 4, r1)), ("WMV", (c0 + 27, c0 + 29, r0 + 4, r1 - 4)),
                         ("WMH", (c0 + 4, c1 - 4, r0 + 54, r0 + 56))):
            pbox(nm, rect, 13.4, 0.5, fr_m, "winframe", "win", bevel=0.04)
        pbox("Sill", (c0 - 4, c1 + 4, r1, r1 + 5), 12.6, 1.4, MT(A.WOOD[2:], [0.5, 0.7, 0.86], light=LB, name="Sill"),
             "sill", "sill", bevel=0.05)
    # ---- pilasters (grand): marble shafts with gilded capitals and bases, in front of the wall
    for i, (a, b) in enumerate(L.get("pilasters", [])):
        pm = MT(MARBLE, bounds(len(MARBLE), 0.26, 0.8), light=LB, bias=0.08, noise=0.06, nscale=2.0, name="Pilaster")
        pbox("Pilaster", (a, b, rail_r[1] + 3, FL - 12), 13.0, 0.8, pm, "pilaster", f"pil{i}", bevel=0.06)
        for j in range(3):
            fc = a + 8 + j * ((b - a - 16) / 2)
            pbox("Flute", (int(fc) - 1, int(fc) + 2, rail_r[1] + 22, FL - 30), 12.95, 0.2,
                 MT(MARBLE[:3], [0.4, 0.7], light=LB, name="Flute"), "pilaster", f"pil{i}f{j}", bevel=0.05)
        cm = MT(A.GOLD, [0.35, 0.6, 0.82], light=LB, name="PilCap")
        pbox("Capital", (a - 4, b + 4, rail_r[1] + 3, rail_r[1] + 15), 12.8, 1.0, cm, "pilcap", f"cap{i}", bevel=0.08)
        pbox("PBase", (a - 4, b + 4, FL - 26, FL - 12), 12.8, 1.0, cm, "pilcap", f"pb{i}", bevel=0.08)
    # ---- potted snake plant (home / grand)
    if L.get("pot") is not None:
        potm = MT(A.POT, [0.45, 0.66], light=LB, name="Pot")
        px, pyy = X(L["pot"]), 11.0
        tagk(R.loft("Pot", [(px, pyy, Z(FL)), (px, pyy, Z(FL - 26))], [0.78, 0.98], potm, 16), "pot", "pot")
        tagk(R.loft("PotRim", [(px, pyy, Z(FL - 26)), (px, pyy, Z(FL - 32))], [1.08, 1.08], potm, 16), "pot", "pot")
        snake = MT(A.PLANT_F, [0.35, 0.55, 0.78], light=LB, lam=0.9, grads=((2, Z(FL - 32), Z(FL - 88), 0.2),),
                   name="Snake")
        for k in range(7):
            a = -1 + 2 * k / 6
            x = px + a * 0.55 + rnd.uniform(-0.1, 0.1)
            h = rnd.uniform(2.6, 3.8) * (1 - 0.3 * abs(a))
            tagk(A.blade("Snake", x, pyy - 0.6 + 0.05 * k, Z(FL - 30), h, a, rnd.uniform(0.1, 0.25), 0.0, 0.16, 0.03,
                         snake, nseg=8, droop=0.2), "leaf", f"snake{k}")
    # ---- shelf + brass trophy
    if L.get("shelf"):
        sc, sr = L["shelf"]
        shelf_m = MT(A.WOOD[1:5], [0.35, 0.6, 0.82], light=LB, name="Shelf")
        pbox("Shelf", (sc, sc + 66, sr, sr + 4), 12.8, 1.2, shelf_m, "shelf", "shelf", bevel=0.04)
        for bc in (sc + 8, sc + 56):
            pbox("Bracket", (bc, bc + 3, sr + 4, sr + 12), 13.0, 1.0, shelf_m, "shelf", "shelf", bevel=0.02)
        gold_m = MT(A.GOLD, [0.4, 0.6, 0.82], light=LB, name="Gold")
        tx, zb = X(sc + 38), Z(sr)
        tagk(R.loft("TrophyBase", [(tx, 12.6, zb), (tx, 12.6, zb + 0.22)], [0.42, 0.36], gold_m, 16), "trophy", "trophy")
        tagk(R.loft("TrophyStem", [(tx, 12.6, zb + 0.22), (tx, 12.6, zb + 0.62)], [0.1, 0.08], gold_m, 12), "trophy",
             "trophy")
        tagk(R.loft("TrophyCup", [(tx, 12.6, zb + 0.62), (tx, 12.6, zb + 0.9), (tx, 12.6, zb + 1.25),
                                  (tx, 12.6, zb + 1.42)], [0.1, 0.34, 0.46, 0.5], gold_m, 16), "trophy", "trophy")
        for sd in (-1, 1):
            tagk(R.loft("Handle", [(tx + sd * 0.45, 12.5, zb + 1.3), (tx + sd * 0.72, 12.5, zb + 1.18),
                                    (tx + sd * 0.62, 12.5, zb + 0.88), (tx + sd * 0.3, 12.5, zb + 0.8)], 0.05, gold_m, 6),
                 "trophy", "trophy")
    # ---- framed fish prints (home / grand: gilt; hall: steel plaques)
    pr_cols = A.GOLD[:3] if L["style"] != "hall" else STEEL[1:4]
    pic_m = MT(pr_cols, [0.45, 0.7], light=LB, name="PicFrame")
    S["PICS"] = []
    for i, (pc, prr) in enumerate(L.get("pic", [])):
        c0, c1, r0, r1 = pc, pc + 54, prr, prr + 40
        for nm, rect in (("PL", (c0, c0 + 4, r0, r1)), ("PR", (c1 - 4, c1, r0, r1)), ("PT", (c0, c1, r0, r0 + 4)),
                         ("PB", (c0, c1, r1 - 4, r1))):
            pbox(nm, rect, 13.2, 0.6, pic_m, "picframe", f"pic{i}", bevel=0.04)
        pquad("Print", (c0 + 4, c1 - 4, r0 + 4, r1 - 4), 13.6, R.m_flat(A.PAPER), "picture", f"print{i}")
        S["PICS"].append((c0 + 4, c1 - 4, r0 + 4, r1 - 4))


def stand(L):
    """Cabinet (home, grand) or viewing counter (hall): top board = the ledge, carcass, plinth, doors / panels."""
    st = STYLE[L["style"]]
    top_m = MT(A.WOOD[1:], [0.3, 0.5, 0.7, 0.88], light=LB, noise=0.05, nscale=2.0, nvec=(0.1, 1.0, 3.0), name="CabTop")
    body_cols = A.WOOD[:5] if L["style"] != "hall" else STEEL[:5]
    body_m = MT(body_cols, [0.3, 0.5, 0.64, 0.84], light=LB, bias=-0.08, name="CabBody")
    pbox("CabTop", A.CAB_TOP, 1.8, 7.0, top_m, "cab", "cabtop", bevel=0.06)
    pbox("CabBody", A.CAB_BODY, 2.2, 6.0, body_m, "cab" if L["style"] != "hall" else "door", "cabbody", bevel=0.04)
    pl_cols = A.WOOD[:3] if L["style"] != "hall" else STEEL[:3]
    pbox("Plinth", A.CAB_PLINTH, 2.6, 5.0, MT(pl_cols, [0.4, 0.75], light=LB, name="Plinth"),
         "cab" if L["style"] != "hall" else "door", "plinth")
    pc = st["panel"]
    door_m = MT(pc[:5], [0.3, 0.5, 0.64, 0.84], light=LB, name="Door")
    panel_m = MT(pc[1:], [0.3, 0.52, 0.7, 0.9], light=LB, bias=0.03, name="Panel")
    knob_m = MT(A.GOLD, [0.35, 0.6, 0.85], light=LB, name="Knob")
    a0, a1 = A.CAB_PLINTH[0], A.CAB_PLINTH[1]
    n = L["doors"]
    dw = (a1 - a0 - 6 * (n - 1)) / n
    T = A.CAB_TOP[2]
    dr0, dr1 = T + 10, A.CAB_PLINTH[2] - 4
    for i in range(n):
        a = int(round(a0 + i * (dw + 6)))
        b = int(round(a0 + i * (dw + 6) + dw))
        pbox("Door", (a, b, dr0, dr1), 1.9, 0.3, door_m, "door", f"door{i}", bevel=0.08)
        pbox("Panel", (a + 10, b - 10, dr0 + 6, dr1 - 6), 1.72, 0.2, panel_m, "door", f"panel{i}", bevel=0.14)
        if st["knobs"]:
            kx = (b - 16) if i == 0 else ((a + 16) if i == n - 1 else (a + b) / 2)
            ob = C.add_prim("ico", "Knob", knob_m, radius=0.14, location=(X(kx), 1.6, Z((dr0 + dr1) / 2)),
                            subdivisions=2)
            C.set_smooth(ob)
            tagk(ob, "knob", f"knob{i}")
    if L["style"] == "grand":           # a gilt strip under the top board
        pbox("Trim", (A.CAB_BODY[0], A.CAB_BODY[1], T + 6, T + 9), 1.7, 6.2,
             MT(A.GOLD, [0.35, 0.6, 0.85], light=LB, name="Trim"), "knob", "trim", bevel=0.02)


def tank_interior(L, rnd):
    c0, c1, r0, r1 = L["IN"]
    sy, sx = S["sy"], S["sx"]
    pquad("TankWater", (c0 - 2, c1 + 2, r0 - 2, r1 + 2), 9.0, R.m_flat(A.WT[2]), "water")
    gpts = [(X(c0 - 2), Z(r1 + 2))] + [(X(c), ztop_level(X(c))) for c in range(c0 - 2, c1 + 3, 2)] + \
        [(X(c1 + 2), Z(r1 + 2))]
    tagk(C.poly_object("Gravel", gpts[::-1], R.m_flat(A.SAND[1]), thickness=0.1, y=5.0), "gravel")
    vallis_b = MT(A.PLANT_B, [0.36, 0.56], light=LB, lam=0.6, grads=((2, mz(-3.6), mz(4.6), 0.32, "world"),),
                  name="VallisB")
    cnt = lambda n: int(round(n * sx))          # noqa: E731
    vallis_clump(mx(-13.5), mx(-9.6), cnt(20), 7.6, 4.8 * sy, 8.8 * sy, rnd, vallis_b, "vl")
    vallis_clump(mx(9.2), mx(13.5), cnt(22), 7.6, 4.6 * sy, 8.8 * sy, rnd, vallis_b, "vr")
    vallis_clump(mx(-1.4), mx(1.6), cnt(6), 7.8, 2.4 * sy, 4.4 * sy, rnd, vallis_b, "vm")
    rock_m = MT(A.ROCK, [0.34, 0.54, 0.76], light=LB, noise=0.1, nscale=2.5 / max(1.0, sy), ndetail=1.0, name="Rock")
    rocks = [(-11.7, 1.35, 1.05, 6.6), (-9.7, 0.85, 0.58, 6.1), (4.4, 0.95, 0.62, 6.2), (11.3, 1.45, 1.1, 6.8),
             (13.0, 0.75, 0.52, 6.1)]
    if sx > 1.2:
        rocks += [(-0.2, 0.7, 0.45, 6.0), (8.9, 0.6, 0.4, 5.9)]
    for (x, rx, rz, y) in rocks:
        rock(mx(x), rx * sy, rz * sy, y, rnd, rock_m)
    drift_m = MT(A.DRIFT, [0.34, 0.55, 0.78], light=LB, noise=0.14, nscale=3.0 / max(1.0, sy), ndetail=1.0, name="Drift")
    moss_m = MT(A.PLANT_M[1:], [0.45, 0.72], light=LB, noise=0.15, nscale=6.0, name="Moss")
    driftwood(rnd, drift_m, moss_m, sy)
    lud_m = MT(A.RED, [0.42, 0.68], light=LB, name="Lud")
    ludwigia(mx(1.4), mx(3.4), 6.9, rnd, lud_m, sy, stems=max(4, cnt(6)))
    sw_m = MT(A.PLANT_M, [0.36, 0.55, 0.75], light=LB, name="Sword")
    sword_plant(mx(7.4), 6.7, rnd, sw_m, sy)
    if sx > 1.45:                      # the big tanks: a second sword plant and a copper stand on the left
        sword_plant(mx(-11.4), 7.0, rnd, sw_m, sy * 0.85, tag="swordb")
        ludwigia(mx(10.0), mx(11.4), 7.1, rnd, lud_m, sy * 0.9, stems=4, tag="ludb")
    S["bubble"] = (mx(-6.5), ztop_level(mx(-6.5)) + 0.52)
    tagk(R.box("AirStone", (mx(-6.5), 4.9, ztop_level(mx(-6.5)) + 0.08), (0.8, 0.5, 0.3), rock_m, bevel=0.1), "stone",
         "stone")
    grass_m = MT(A.PLANT_F, [0.36, 0.58, 0.8], light=LB, lam=0.6, grads=((2, mz(-3.9), mz(-2.9), 0.34, "world"),),
                 name="Grass")
    gx = [-12.4, -10.6, -8.6, -4.4, -2.4, 0.4, 5.9, 8.6, 10.4, 12.7]
    if sx > 1.2:
        gx += [-6.6, 2.6, 3.8, -1.2]
    for k, x in enumerate(gx):
        tuft(mx(x), 4.7, int(round(rnd.randint(5, 8) * math.sqrt(sx))), rnd.uniform(0.35, 0.6) * math.sqrt(sx),
             0.35 * sy, 0.95 * sy, rnd, grass_m, "grass", f"gt{k}")


def back_scene(L, rnd):
    room(L, rnd)
    stand(L)
    tank_interior(L, rnd)


# ================================================================== FRONT LAYER geometry
def front_scene(L, rnd):
    fr = STYLE[L["style"]]["frame"]
    b5 = [0.2, 0.42, 0.6, 0.74, 0.88] if len(fr) == 6 else bounds(len(fr))
    frame_m = MT(fr, b5, light=LB, noise=0.05, nscale=1.6, nvec=(0.1, 1.0, 3.0), name="Frame")
    post_m = MT(fr, b5, light=LB, noise=0.05, nscale=1.6, nvec=(3.0, 1.0, 0.1), name="Post")
    hood_m = MT(fr, b5, light=LB, noise=0.05, nscale=1.6, nvec=(0.1, 1.0, 3.0), bias=-0.04, name="Hood")
    y = -1.0
    TC0, TC1 = L["TANK"]
    c0, c1, r0, r1 = L["IN"]
    pbox("RimTop", (TC0, TC1, A.RIM_R0, r0), y, 1.6, frame_m, "frame", "frame", bevel=0.06)
    pbox("RimBot", (TC0, TC1, r1, A.BOT_R1), y, 1.6, frame_m, "frame", "frame", bevel=0.06)
    pbox("PostL", (TC0, c0, r0 - 1, r1 + 1), y + 0.02, 1.6, post_m, "frame", "frame", bevel=0.06)
    pbox("PostR", (c1, TC1, r0 - 1, r1 + 1), y + 0.02, 1.6, post_m, "frame", "frame", bevel=0.06)
    HD = A.HOOD
    pbox("Hood", HD, y - 0.2, 2.0, hood_m, "hood", "hood", bevel=0.1)
    pbox("HoodCap", (HD[0] - 2, HD[1] + 2, HD[2] - 2, HD[2] + 2), y - 0.3, 2.1, hood_m, "hood", "hood", bevel=0.04)
    if L["style"] == "grand":           # a moulded band along the cornice, gilt rosettes at the frame corners
        pbox("Band", (HD[0] + 6, HD[1] - 6, HD[3] - 8, HD[3] - 5), y - 0.35, 2.2, frame_m, "hood", "band", bevel=0.03)
        for (cx, cr) in ((TC0 + L["post"] / 2, A.RIM_R0 + 6), (TC1 - L["post"] / 2, A.RIM_R0 + 6),
                         (TC0 + L["post"] / 2, A.BOT_R1 - 7), (TC1 - L["post"] / 2, A.BOT_R1 - 7)):
            ob = C.add_prim("ico", "Rosette", hood_m, radius=0.36, location=(X(cx), y - 0.5, Z(cr)), subdivisions=2)
            C.set_smooth(ob)
            tagk(ob, "frame", f"ros{cx:.0f}{cr}")
    if L.get("rivets"):                  # hall: bolts along the steel frame
        rv = MT(STEEL[2:], [0.4, 0.62, 0.85], light=LB, name="Rivet")
        k = 0
        for cc in range(TC0 + 24, TC1 - 16, 48):
            for rr in ((A.RIM_R0 + r0) / 2, (r1 + A.BOT_R1) / 2):
                ob = C.add_prim("ico", "Rivet", rv, radius=0.12, location=(X(cc), y - 0.1, Z(rr)), subdivisions=1)
                C.set_smooth(ob)
                tagk(ob, "frame", f"rv{k}")
                k += 1
        for rr in range(r0 + 24, r1 - 16, 48):
            for cc in ((TC0 + c0) / 2, (c1 + TC1) / 2):
                ob = C.add_prim("ico", "Rivet", rv, radius=0.12, location=(X(cc), y - 0.1, Z(rr)), subdivisions=1)
                C.set_smooth(ob)
                tagk(ob, "frame", f"rv{k}")
                k += 1
    grass_m = MT(A.PLANT_F, [0.36, 0.58, 0.8], light=LB, lam=0.6, grads=((2, mz(-4.6), mz(-3.0), 0.34, "world"),),
                 name="GrassF")
    sq = math.sqrt(S["sx"])
    for k, (x, n) in enumerate(((-12.9, 7), (-11.7, 5), (12.2, 6), (13.0, 4))):
        x = mx(x)
        for i in range(int(round(n * sq))):
            xx = x + rnd.gauss(0, 0.22 * sq)
            tagk(A.blade("GrassF", xx, -0.4 + rnd.uniform(-0.05, 0.05), Z(r1) - 0.1, rnd.uniform(0.7, 1.5) * S["sy"],
                         (xx - x) * 3.0 / sq + rnd.uniform(-0.4, 0.4), rnd.uniform(0.2, 0.5), 0.0, 0.055, 0.03,
                         grass_m, nseg=7, droop=0.2), "plant", f"fg{k}")


# ================================================================== composites
WIN_PATCH = {}


def window_patch():
    """The legacy window content (sky crop, hills, lake, glitter) as a 104x48 patch, computed in L1 coords."""
    if "p" not in WIN_PATCH:
        full = A.window_rgb()
        c0, c1, r0, r1 = 24, 72, 72, 176
        WIN_PATCH["p"] = full[r0:r1, c0:c1].copy()
    return WIN_PATCH["p"]


def paint_tiles_floor(idx, pal, fm, ramp, checker):
    """Hall carpet / grand marble floor: rows growing towards the viewer; grand: a checker of two tones with
    seams, hall: a flat carpet with dash texture and a glow patch under the glass."""
    Hh, Ww = idx.shape
    FL = A.FLOOR_ROW
    rng = random.Random(29)
    ids = [pal.index(c) for c in ramp]
    r, hgt, k = FL, 4, 0
    while r < Hh:
        b = min(Hh, r + hgt)
        if checker:
            tw = 16 + 4 * hgt                      # tiles widen with the rows (towards the viewer)
            cols = np.arange(Ww)
            sk = (((cols - Ww // 2 + tw * 64) // tw) + k) % 2
            row = np.where(sk == 0, ids[2], ids[3])
            for rr in range(r, b):
                idx[rr, fm[rr]] = row[fm[rr]]
            idx[r, fm[r]] = ids[1]
        else:
            for rr in range(r, b):
                m = fm[rr]
                idx[rr, m] = ids[1]
                d = R.dash_mask(Ww, 0.18, 2, 5, rng)
                idx[rr, m & d] = ids[0]
        r, k, hgt = b, k + 1, min(18, hgt + 2)
    # contact shade along the wall / stand
    shade = fm.copy()
    shade[FL + 3:] = False
    idx[shade] = ids[0]
    return idx


def wall_pattern(idx, pal, kid, L):
    Hh, Ww = idx.shape
    cols = np.arange(Ww)[None, :]
    rows = np.arange(Hh)[:, None]
    wallm = kid == "wall"
    st = L["style"]
    ramp = STYLE[st]["wall"]
    if st == "home":
        cc = np.clip((np.arange(Ww) // 6) * 6 + 3, 0, Ww - 1)
        snap = wallm & wallm[:, cc]
        idx[snap] = idx[:, cc][snap]
        stripe = wallm & ((cols // 6) % 2 == 1)
        idx[stripe] = R.ramp_step(pal, ramp, idx, -1)[stripe]
    elif st == "hall":
        # large slate tiles 40x20 in running bond: dark joints, a lit top edge
        rr = rows - L["RAIL"][1]
        off = ((rr // 20) % 2) * 20
        joint = wallm & ((rr % 20 == 0) | (((cols + off) % 40) == 0))
        lit = wallm & (rr % 20 == 1) & ~joint
        idx[joint] = R.ramp_step(pal, ramp, idx, -1)[joint]
        idx[lit] = R.ramp_step(pal, ramp, idx, +1)[lit]
    else:
        # marble panels 64 px wide: a lit left moulding line, a shaded right one, a dado line
        ph = 64
        x0 = (Ww // 2) % ph
        lit = wallm & (((cols - x0) % ph) == 1)
        dk = wallm & (((cols - x0) % ph) == ph - 2)
        dado = wallm & ((rows == A.FLOOR_ROW - 60) | (rows == A.FLOOR_ROW - 58))
        idx[lit | dado] = R.ramp_step(pal, ramp, idx, +1)[lit | dado]
        idx[dk] = R.ramp_step(pal, ramp, idx, -1)[dk]
    return idx


def back_palette(L):
    st = STYLE[L["style"]]
    extra = []
    for k in ("wall", "rail", "floor", "frame", "panel"):
        extra += st[k]
    return R.Pal(A.BACK_PAL + extra + MARBLE + VELVET + STEEL)


def render_back(L, rnd):
    W, H = L["W"], L["H"]
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(L, rnd)
    C.ortho_camera(0, 0, W, H, PPU)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"aq{L['n']}_back")
    kid = R.kind_map(ps)
    win = kid == "window"
    water = kid == "water"
    gravel = kid == "gravel"
    rows = np.arange(H)[:, None]
    cols = np.arange(W)[None, :]
    pal = back_palette(L)
    rgb = ps["rgb"].copy()
    if L.get("win"):
        c0, c1, r0, r1 = A.WIN_I
        patch = np.zeros_like(rgb)
        patch[r0:r1, c0:c1] = window_patch()
        rgb[win] = patch[win]
    idx, solid = A.quantize_groups(rgb, kid, pal)
    idx = R.despeckle(idx, ps["id"], protect=~solid | win, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.35, steps=1)
    lk = ("plant", "wood", "rock", "sword", "red", "moss", "stone", "grass", "cab", "door", "knob", "pot", "leaf",
          "shelf", "trophy", "sill", "pilcap")
    idx = np.where(np.isin(kid, lk), lined, idx)
    st = STYLE[L["style"]]
    idx = A.bevel_edges(idx, pal, ps["id"], np.isin(kid, ("cab", "winframe", "sill", "shelf")), A.WOOD)
    idx = A.bevel_edges(idx, pal, ps["id"], kid == "door", st["panel"])
    idx = A.bevel_edges(idx, pal, ps["id"], kid == "rail", st["rail"])
    idx = A.bevel_edges(idx, pal, ps["id"], kid == "picframe", A.GOLD if L["style"] != "hall" else STEEL)
    idx = A.bevel_edges(idx, pal, ps["id"], kid == "pilaster", MARBLE)
    idx = A.bevel_edges(idx, pal, ps["id"], kid == "pilcap", A.GOLD)
    if win.any():
        idx = A.reduce_region(idx, pal, win, 16)
    idx = wall_pattern(idx, pal, kid, L)
    if L.get("win"):
        c0, c1, r0, r1 = A.WIN_O
        dxw = np.maximum(np.maximum(c0 - cols, cols - (c1 - 1)), 0)
        dyw = np.maximum(np.maximum(r0 - rows, rows - (r1 + 5)), 0)
        dw = np.sqrt(dxw ** 2 + (dyw * 1.3) ** 2)
        glow = (kid == "wall") & (dw > 0) & ((dw < 8) | ((dw < 10) & (R.bayer(H, W) > 0.5)))
        idx[glow] = R.ramp_step(pal, A.WALL, idx, +1)[glow]
    if L["style"] == "hall":             # the glass wall's glow on the slate around the frame
        TC0, TC1 = L["TANK"]
        dxw = np.maximum(np.maximum(TC0 - 4 - cols, cols - (TC1 + 3)), 0)
        dyw = np.maximum(A.HOOD[2] - 2 - rows, 0)
        dw = np.sqrt(dxw ** 2 + dyw ** 2)
        glow = (kid == "wall") & (dw > 0) & ((dw < 14) | ((dw < 20) & (R.bayer(H, W) > 0.5)))
        idx[glow] = R.ramp_step(pal, SLATE, idx, +1)[glow]
    fm = kid == "floor"
    if L["style"] == "home":
        idx = A.paint_floor(idx, pal, fm, random.Random(23))
    else:
        idx = paint_tiles_floor(idx, pal, fm, st["floor"], checker=L["style"] == "grand")
    # ---- tank water
    SR = A.SURF_ROW
    R.W, R.H = W, H
    try:
        wband = R.water_bands(pal, A.WT, A.WATER_Z, random.Random(5), -300, 700)
    finally:
        R.W, R.H = REF_W, REF_H
    under = water & (rows >= SR)
    idx[under] = wband[under]
    idx[water & (rows < SR)] = pal.index(A.AIR)
    idx[water & (rows == SR)] = pal.index(A.MENISC)
    rng = random.Random(13)
    up1 = R.ramp_step(pal, A.WSTEP, wband, +1)
    for r, t in ((SR + 1, 0.55), (SR + 2, 0.3), (SR + 4, 0.12)):
        m = water[r] & R.dash_mask(W, t, 3, 10, rng)
        idx[r, m] = up1[r, m]
    idx = A.paint_gravel(idx, pal, gravel, random.Random(19))
    fa = S.get("front_a")
    if fa is None:
        fa = np.zeros((H, W), bool)
    tank = fa | np.isin(kid, ("water", "gravel", "cab", "door", "knob", "plant", "rock", "wood", "moss", "sword",
                               "red", "stone", "grass"))
    A.SHADE = dict(wall=st["wall"], rail=st["rail"], floor=st["floor"])
    idx = A.drop_shadow(idx, pal, kid, tank, 5, 6)
    idx = A.drop_shadow(idx, pal, kid, np.isin(kid, ("sill", "winframe")), 3, 2)
    idx = A.drop_shadow(idx, pal, kid, np.isin(kid, ("picframe", "picture", "shelf", "trophy")), 2, 3)
    idx = A.drop_shadow(idx, pal, kid, np.isin(kid, ("pot", "leaf")), 2, 4, on=("wall", "rail"))
    idx = A.drop_shadow(idx, pal, kid, np.isin(kid, ("pilaster", "pilcap")), 3, 4, on=("wall",))
    # ---- warm lamp shafts
    c0, c1, r0, r1 = L["IN"]
    inside = np.zeros((H, W), bool)
    inside[SR:r1, c0:c1] = True
    amap = np.zeros((H, W))
    for k, s in enumerate(A.SHAFTS):
        prs = A.PR.copy(shaft=s)
        i2, pal = R.apply_shaft(idx, pal, prs, seed=5 + k)
        idx = np.where(inside & water & (rows > SR), i2, idx)
        amap = np.maximum(amap, R.light_shaft(prs, W, H))
    core = inside & (amap > 0.3 + 0.35 * R.dash_threshold(H, W, random.Random(41), 2, 6))
    for k, ramps in A.LIT.items():
        m = core & (kid == k)
        for ramp in ramps:
            idx[m] = R.ramp_step(pal, ramp, idx, +1)[m]
    idx = A.reduce_region(idx, pal, water, 18)
    # ---- framed fish prints
    pic = kid == "picture"
    idx[pic] = pal.index(A.PAPER)
    used = R.Pal([pal.hex[i] for i in np.unique(idx[~pic])])
    fish_ids = ["golden_carp", "crucian_carp", "bluegill", "piranha", "rainbow_trout"]
    for i, (pc0, pc1, pr0, pr1) in enumerate(S.get("PICS", [])):
        for fid in fish_ids[i:] + fish_ids[:i]:
            fp = os.path.join(R.OUT, "fish", f"{fid}_0.png")
            if not os.path.exists(fp):
                fp = os.path.join(C.SPRITES, "Fish", f"{fid}_0.png")
            if not os.path.exists(fp):
                continue
            spr = R.load_png(fp)
            h, w = spr.shape[:2]
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
    R.save_png(img, os.path.join(OUTD, f"aquarium_{L['n']}_back.png"))
    print("AQT back", L["n"], "size", (W, H), "colours", R.count_colours(img), "water", len(np.unique(idx[water])))
    return img


def glints(idx, pal, L):
    """Glass glints: '/' strokes in the top-left corner, two in the bottom-right corner (legacy offsets)."""
    c0, c1, r0, r1 = L["IN"]
    hi, lo = pal.index(A.GLASS[1]), pal.index(A.GLASS[0])
    k = max(1.0, S["sy"])
    for (dr, dc, n, col, top) in ((17, 2, 13, hi, True), (18, 8, 9, lo, True), (16, 13, 4, lo, True),
                                  (-3, -17, 7, lo, False), (-3, -12, 4, hi, False)):
        r = (r0 + dr) if top else (r1 + dr)
        c = (c0 + dc) if top else (c1 + dc)
        for j in range(int(round(n * k))):
            rr, cc = r - j, c + j
            if r0 <= rr < r1 and c0 <= cc < c1 and idx[rr, cc] < 0:
                idx[rr, cc] = col
    return idx


def render_front(L, rnd):
    W, H = L["W"], L["H"]
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    front_scene(L, rnd)
    C.ortho_camera(0, 0, W, H, PPU)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"aq{L['n']}_front")
    fr = STYLE[L["style"]]["frame"]
    pal = R.Pal(fr + A.PLANT_F + A.GLASS)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    thin = kid == "plant"
    idx = R.despeckle(idx, ps["id"], protect=~solid | thin, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.08, steps=1)
    solid_k = np.isin(kid, ("frame", "hood"))
    idx = A.bevel_edges(idx, pal, ps["id"], solid_k, fr)
    idx, pal = R.rim_light(idx, pal, A.PR, mask=solid_k)
    base = np.where(thin, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2, light=R.sun_side(A.PR))
    idx = np.where(thin & (idx >= 0), idx, ol)
    idx = glints(idx, pal, L)
    S["front_a"] = (idx >= 0) & ~thin
    img = R.to_rgba(idx, pal)
    R.save_png(img, os.path.join(OUTD, f"aquarium_{L['n']}_front.png"))
    print("AQT front", L["n"], "colours", R.count_colours(img))
    return img


# ================================================================== layout JSON
def wx(c):
    return round((c - S["W"] / 2) / PPU, 4)


def wy(r):
    return round((S["H"] / 2 - r) / PPU, 4)


def layout(L):
    W, H, vh = L["W"], L["H"], L["vh"]
    c0, c1, r0, r1 = L["IN"]
    SR = S["SURF_ROW"]
    GT = A.GTOP
    gmin, gmax = int(GT[c0:c1].min()), int(GT[c0:c1].max())
    gmid = int(round(np.median(GT[c0:c1])))
    surfaceY = round((H / 2 - (SR - 0.2)) / PPU, 2)
    s = vh / 270.0
    vw = int(round(vh * 16 / 9))
    vx0, vy0 = (W - vw) // 2, (H - vh) // 2
    # decor slots of this level (AquaTank ids / kinds / unlock levels), mapped from the legacy tank
    gx = (c1 - c0) / (REF_GLASS[1] - REF_GLASS[0])
    slots = []
    for sid, kind, col, row, lv, order in AQ_SLOTS:
        if lv > L["n"]:
            continue
        if kind == "light":
            c, r = (A.HOOD[0] + A.HOOD[1]) // 2, A.HOOD[2] - 2
        else:
            c = int(round(c0 + (col - REF_GLASS[0]) * gx))
            if kind == "front":
                r = r1
            else:
                r = int(GT[c] + round((row - GTOP_REF[col]) * S["sy"]))
        mw, mh = SLOT_MAX[kind]
        slots.append(dict(id=sid, kind=kind, col=c, row=r, x=wx(c), y=wy(r), level=lv, order=order, maxW=mw, maxH=mh))
    lr = L["LEDGE_ROW"]
    ly = wy(lr)
    lg = L["ledge"]
    dirt_h = int(round(28 * S["sy"] / 2)) * 2
    ui = lambda a, b, w, h: [int(round(a)), int(round(b)), int(round(a + w)), int(round(b + h))]   # noqa: E731
    d = dict(
        level=L["n"], key=L["key"], name=L["name"], style=L["style"],
        back=f"aquarium_{L['n']}_back", front=f"aquarium_{L['n']}_front",
        canvasW=W, canvasH=H, ppu=PPU,
        viewH=vh, viewW=vw, viewMaxW=int(math.floor(vh * 640 / 270)), viewMaxH=int(math.floor(vh * 400 / 270)),
        viewScale=round(s, 4), orthoSize=round(vh / 2 / PPU, 4), camX=0.0, camY=0.0,
        # legacy AquariumData keys (JsonUtility-compatible)
        swimMinX=round(wx(c0) + 1.025, 3), swimMaxX=round(wx(c1) - 1.025, 3),
        swimMinY=round(wy(gmid) + 1.1, 3), swimMaxY=round(surfaceY - 0.8, 3), surfaceY=surfaceY,
        waterTop=A.WT[0], waterDeep=A.WT[-1],
        # rects: [col0, row0, col1, row1] canvas px (top-down, end exclusive) + the same in world units
        frameRect=[L["TANK"][0], A.HOOD[2] - 2, L["TANK"][1], A.BOT_R1],
        glassRect=[c0, r0, c1, r1], glassL=wx(c0), glassR=wx(c1), glassT=wy(r0), glassB=wy(r1),
        glassW=c1 - c0, glassH=r1 - r0,
        waterRect=[c0, SR, c1, r1], surfaceRow=SR, surfaceFx=wy(SR),
        tankMinX=round(wx(c0) + 0.725, 3), tankMaxX=round(wx(c1) - 0.725, 3),
        gravelTopRow=[gmin, gmid, gmax], gravelTopY=wy(gmid), gravelBand=[c0, gmin, c1, r1],
        gravelRows=[int(v) for v in GT[c0:c1]],
        pelletRestY=round(wy(gmid) + 0.22, 3), gravelY=round(wy(gmid) + 0.1, 3),
        dirtRect=[c0, r1 - dirt_h, c1, r1], dirtH=dirt_h, dirtCentreY=wy(r1 - dirt_h / 2),
        bubble=[round(S["bubble"][0], 3), round(S["bubble"][1], 3)],
        hoodRect=list(A.HOOD), lightAt=[wx((A.HOOD[0] + A.HOOD[1]) / 2), round(wy(A.HOOD[2] - 2) + 0.75, 4)],
        glowAt=[wx((c0 + c1) / 2), wy((r0 + r1) / 2)],
        ledgeRow=lr, ledgeY=ly, ledgeRect=[L["CAB_TOP"][0], L["CAB_TOP"][2], L["CAB_TOP"][1], L["CAB_TOP"][3]],
        bagX=[wx(c) for c in lg["bags"]], bagCol=lg["bags"], bagCentreY=round(ly + 18 / PPU, 4),
        tubX=wx(lg["tub"]), coolerX=wx(lg["cooler"]), boxY=round(ly + 20 / PPU, 4),
        spongeX=wx(lg["sponge"]), netX=wx(lg["net"]), siphonX=wx(lg["siphon"]),
        ledgeCols=dict(tub=lg["tub"], cooler=lg["cooler"], sponge=lg["sponge"], net=lg["net"], siphon=lg["siphon"]),
        slots=slots,
        # UI safe areas at the 16:9 view (canvas px; UI canvas units / 2 x viewScale), all top-down
        viewRect=[vx0, vy0, vx0 + vw, vy0 + vh],
        uiTopBand=[vx0, vy0, vx0 + vw, vy0 + int(round(33 * s))],
        uiHint=ui(vx0 + vw / 2 - 150 * s, vy0 + 38 * s, 300 * s, 16 * s),
        uiIncome=ui(vx0 + 6 * s, vy0 + vh - 44 * s, 245 * s, 38 * s),
        uiButtons=ui(vx0 + vw - 178 * s, vy0 + vh - 37 * s, 171 * s, 30 * s),
    )
    return d


# ================================================================== preview sheet (layout overlaid)
def rect_outline(img, rect, col, dash=0):
    c0, r0, c1, r1 = [int(v) for v in rect]
    H, W = img.shape[:2]
    for c in range(max(0, c0), min(W, c1)):
        for r in (r0, r1 - 1):
            if 0 <= r < H and (not dash or (c // dash) % 2 == 0):
                img[r, c, :3] = col
                img[r, c, 3] = 1
    for r in range(max(0, r0), min(H, r1)):
        for c in (c0, c1 - 1):
            if 0 <= c < W and (not dash or (r // dash) % 2 == 0):
                img[r, c, :3] = col
                img[r, c, 3] = 1


def cross(img, c, r, col, n=3):
    H, W = img.shape[:2]
    for k in range(-n, n + 1):
        for (rr, cc) in ((r, c + k), (r + k, c)):
            if 0 <= rr < H and 0 <= cc < W:
                img[rr, cc, :3] = col
                img[rr, cc, 3] = 1


def preview(L, d, back, front):
    img = back.copy()
    R.over(img, front, 0, 0)
    ledge = [("feed_basic_sealed", d["bagCol"][0]), ("feed_premium_sealed", d["bagCol"][1]),
             ("feed_tub_closed", d["ledgeCols"]["tub"]), ("feed_cooler_closed", d["ledgeCols"]["cooler"]),
             ("clean_sponge_rest", d["ledgeCols"]["sponge"]), ("clean_net_rest", d["ledgeCols"]["net"]),
             ("clean_siphon_rest", d["ledgeCols"]["siphon"])]
    for nm, c in ledge:
        p = os.path.join(C.SPRITES, "World", nm + ".png")
        if os.path.exists(p):
            im = R.load_png(p)
            R.over(img, im, c - im.shape[1] // 2, d["ledgeRow"] - im.shape[0])
    red, yel, cyan, mag, wht, grn = (1, 0.2, 0.2), (1, 0.9, 0.2), (0.3, 1, 1), (1, 0.3, 1), (1, 1, 1), (0.3, 1, 0.3)
    rect_outline(img, d["viewRect"], wht)
    rect_outline(img, d["glassRect"], cyan)
    rect_outline(img, d["gravelBand"], yel, dash=3)
    rect_outline(img, d["dirtRect"], (0.8, 0.5, 0.2), dash=2)
    rect_outline(img, [d["glassRect"][0], d["surfaceRow"], d["glassRect"][2], d["surfaceRow"] + 1], (0.2, 0.5, 1))
    sw = [int(S["W"] / 2 + d["swimMinX"] * PPU), int(S["H"] / 2 - d["swimMaxY"] * PPU),
          int(S["W"] / 2 + d["swimMaxX"] * PPU), int(S["H"] / 2 - d["swimMinY"] * PPU)]
    rect_outline(img, sw, grn, dash=4)
    for k in ("uiTopBand", "uiHint", "uiIncome", "uiButtons"):
        rect_outline(img, d[k], red, dash=2)
    for s in d["slots"]:
        col = dict(light=yel, back=mag, mid=(1, 0.6, 0.2), front=cyan)[s["kind"]]
        cross(img, s["col"], s["row"], col, 3)
    rect_outline(img, [0, d["ledgeRow"], S["W"], d["ledgeRow"] + 1], (1, 0.5, 0), dash=6)
    return img


# ================================================================== main
def run_level(L):
    bind(L)
    R.WORK = os.path.join(WORK0, f"L{L['n']}")
    os.makedirs(R.WORK, exist_ok=True)
    front = render_front(L, random.Random(31))
    back = render_back(L, random.Random(77))
    d = layout(L)
    with open(os.path.join(OUTD, f"aquarium_tank_{L['n']}.json"), "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=1)
    pv = preview(L, d, back, front)
    R.save_png(pv, os.path.join(OUTD, f"preview_{L['n']}.png"))
    print("AQT level", L["n"], L["key"], "canvas", (L["W"], L["H"]), "view", (d["viewW"], d["viewH"]), "glass",
          d["glassRect"], "surf", d["surfaceRow"], "ledge", d["ledgeRow"], "slots", len(d["slots"]))
    return pv


def sheet():
    ims = []
    for n in range(5):
        p = os.path.join(OUTD, f"preview_{n}.png")
        if os.path.exists(p):
            ims.append(R.load_png(p))
    if not ims:
        return
    # every level at the SAME world scale (1 px = 1 px): the tanks' real size difference shows
    pad = 12
    Wt = sum(im.shape[1] for im in ims[:3]) + pad * 4
    row1 = max(im.shape[0] for im in ims[:3])
    row2 = max((im.shape[0] for im in ims[3:]), default=0)
    Wt = max(Wt, sum(im.shape[1] for im in ims[3:]) + pad * 3)
    out = np.zeros((row1 + row2 + pad * 3, Wt, 4), np.float32)
    out[..., :3] = 0.12
    out[..., 3] = 1
    x, y = pad, pad
    for i, im in enumerate(ims):
        if i == 3:
            x, y = pad, pad * 2 + row1
        out[y:y + im.shape[0], x:x + im.shape[1]] = im
        x += im.shape[1] + pad
    R.save_png(out, os.path.join(OUTD, "levels_sheet.png"))
    print("AQT sheet", out.shape)


def install():
    root = os.path.abspath(os.path.join(C.SPRITES, "..", ".."))
    st_dir = os.path.join(C.SPRITES, "Stages")
    tmpl = os.path.join(st_dir, "aquarium_back.png.meta")
    jtmpl = os.path.join(C.DATA, "aquarium.json.meta")
    for n in range(5):
        for part in ("back", "front"):
            src = os.path.join(OUTD, f"aquarium_{n}_{part}.png")
            dst = os.path.join(st_dir, f"aquarium_{n}_{part}.png")
            shutil.copyfile(src, dst)
            meta = dst + ".meta"
            if not os.path.exists(meta):
                with open(tmpl, encoding="utf-8") as f:
                    txt = f.read()
                txt = txt.replace(txt.split("guid: ")[1].split("\n")[0], uuid.uuid4().hex, 1)
                with open(meta, "w", encoding="utf-8", newline="\n") as f:
                    f.write(txt)
        src = os.path.join(OUTD, f"aquarium_tank_{n}.json")
        dst = os.path.join(C.DATA, f"aquarium_tank_{n}.json")
        shutil.copyfile(src, dst)
        meta = dst + ".meta"
        if not os.path.exists(meta):
            with open(jtmpl, encoding="utf-8") as f:
                txt = f.read()
            txt = txt.replace(txt.split("guid: ")[1].split("\n")[0], uuid.uuid4().hex, 1)
            with open(meta, "w", encoding="utf-8", newline="\n") as f:
                f.write(txt)
    print("AQT installed to", st_dir, "and", C.DATA, "(root", root, ")")


def main():
    C.reset_scene()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    lv = [int(a) for a in args if a.isdigit()] or list(range(5))
    window_patch()                       # legacy window content, computed with the level-1 globals
    for n in lv:
        run_level(LEVELS[n])
    sheet()
    if "install" in args:
        install()
    print("AQT done")


if __name__ == "__main__":
    main()
