"""
hybrid - the ocean stage: the old stage_ocean composition (the angler on the bow deck of a boat far out at
sea, an island on the left horizon, a far ship on the right) built in the hybrid craft + the "ocean"
preset mood (sunrise).

Back layer (opaque):
  * banded sky from the preset (azure zenith -> mauve -> pink -> peach-gold horizon), a pink-gold pixel sun
    just up over OPEN sea left of centre (col 230), concentric dithered glow rings, one stratus streak with
    a gold-lit underside;
  * layered far silhouettes with aerial perspective (every ramp pushed towards the warm peach haze by its
    distance): a faint far strip 4.4 km (right, lightest) -> the left island chain 3.3 km (hazy mauve,
    warm rim on its sun-facing end) -> a freighter 2 km (right of centre) -> the lighthouse island 1.15 km
    (right, darkest: rock, scrub, keeper's house, white tower with a red band and a still-lit lamp);
    the sun column stays clear of every silhouette;
  * water: retro16 depth bands joined by horizontal dash dithering (rose-silver horizon -> mauve ->
    periwinkle -> deep blue play area -> darker blue at the boat), the calm far water mirrors the sky
    exactly (glow rings included, deeper under the sun), EXACT mirrored reflections of the islands / ship
    (mirror pass, water-tinted, break lines + dissolve), long broken SWELL crests (light crest dash + darker
    face row; calm in the middle of the play area), sparse chop marks, a faint warm sheen + sparse short
    glitter dashes in the far part of the sun column only, a thin dash-dithered mist line on the horizon.
Front layer (transparent, solid objects only): the bow of the boat the angler stands on - teak deck planks
with dark caulking, margin board, white bulwarks, navy cap rail, stainless bow pulpit (2 rails +
stanchions), cooler with a blue lid, life ring on the pulpit, rope coil + bow cleat, a trolling rod in a
holder; contact shadow at the feet; key light from the sun side (left, low - lifted for readability), warm
rim on the OUTER left/top silhouette only, hue-shifted outline (no ink).

Same camera (fk_persp.setup_camera(1.7)), 640x400, same gameplay layout as Data/stage_ocean.json (only
waterTint / waterDeep / clouds / birds change; waterTint = the rendered play-area band so Unity's underwater
tint and the painted water agree).
Outputs: _tmp/variants/hybrid/ocean_back.png, ocean_front.png, stage_ocean.json (scratch: work/ocean/)
Run: blender -b --python variants/hybrid/hyb_ocean.py [-- back|front] [--period dawn|day|evening|night [--dry]]   (hyb_period.py)
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

SID = "ocean"
# own scratch dir for the EXR passes / overlay (other stages render in parallel into work/)
R.WORK = PER.work(SID, os.path.join(R.OUT, "work", SID))   # period runs: work/periods/ocean_<p>/
os.makedirs(R.WORK, exist_ok=True)

# the preset as is, except: waterTint = the play-area band that is actually painted (WB[4]) and the glitter
# column measured on the visible water (row1 = the waterline ~11 m out, so far=0.45 ends ~40 m out)
PR = PER.use_preset("ocean", water=dict(tint="#2c5c8a"), glitter=dict(row0=91, row1=240))
STAND = 1.7
W, H = P.W, P.H
G = R.grade_hex
HZ = R.HORIZON_ROW

# ------------------------------------------------------------------ geometry anchors of the mood
SUN_C, SUN_R = R.sun_rc(PR)                     # sun column / row (230, 83.5)
OPEN_C = (190.0, 272.0)                         # screen columns kept free of far silhouettes (sun column)
CX0, CY0, CW, CH = R.CROP
CALM_C = (CX0 + int(CW * 0.2), CX0 + int(CW * 0.8))   # middle 60 % of the crop: calm water for fish shadows
D_FAR2, D_FAR, D_SHIP, D_ISL = 4400.0, 3300.0, 2000.0, 1150.0

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)
MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)
WB = PR.water["bands"]                          # 6 bands, far (horizon) -> near (boat)
WSTEP = [PR.water["dark"]] + WB[::-1] + [PR.sky[0][1]]   # dark -> light ramp for +/- steps
REFL = PR.water["refl"]
FAR2 = R.hazed(["#4e4a68", "#5e5876"], D_FAR2, extra=-0.26)
FARI = R.hazed(G(["#34324e", "#46425e", "#5e5670"]), D_FAR, extra=-0.2)
ISL = R.hazed(G(["#1c2230", "#2a3638", "#3c4a40", "#5e6448"]), D_ISL)
ROCKI = R.hazed(G(["#2e2a36", "#4a4450", "#6e6264"]), D_ISL)
TOWER = R.hazed(G(["#6e6a84", "#c8bcbc", "#f4e6da"]), D_ISL, extra=-0.06)
BAND = R.hazed(G(["#6e2630", "#a8404a"]), D_ISL)
# PER.const: a time-of-day period (periods/ocean.py CONSTS) may change these; the native look uses the defaults
LAMP = PER.const("LAMP", "#ffd890")              # the lighthouse lamp is still on at sunrise
SHEEN = PER.const("SHEEN", 0.28)                 # the warm sheen under the sun column (0 = none: sun behind the boat)
SHIP = R.hazed(G(["#1e2030", "#343448", "#7e7a8c", "#c2b8bc"]), D_SHIP, extra=-0.12)
GLIT = PR.glitter["cols"]
BACK_PAL = (SKY_PAL + MSKY_PAL + WB + [PR.water["dark"]] + REFL + FAR2 + FARI + ISL + ROCKI + TOWER + BAND
            + [LAMP] + SHIP + GLIT)

WOOD = G(["#3a2626", "#62402f", "#8a5c3c", "#b07a4c", "#d4a068"])   # teak deck, dark caulking
HULL = G(["#6a6884", "#9e98ac", "#c8c0c4", "#ece2d8"])              # white bulwarks (cool shade, warm light)
BLUE = G(["#1c2c5a", "#2c4c90", "#4a78c0"])                         # navy cap rail, cooler lid
STEEL = G(["#565c6c", "#9aa0ae", "#dfe2e8"])                        # pulpit, cleat, holder, reel
RING = G(["#86282a", "#d04a32", "#f48a56"])                         # life ring
ROPE = G(["#6e5640", "#a88e64", "#d4bc8c"])
FRONT_PAL = WOOD + HULL + BLUE + STEEL + RING + ROPE

LB = Vector(PR.key_dir).normalized()          # one key light for the whole stage (the sun side, lifted)


def rc(p):
    return R.rc(p, STAND)


def col_of(x, y):
    return rc((x, y, 0.0))[0]


def xcol(col, y):
    return R.col_to_x(col, y, STAND)


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


# ================================================================== BACK LAYER geometry
def island_mesh(name, rnd, y0, depth, c0, c1, peaks, mat, kind, step_px=1.5, grp=None):
    """Island silhouette between screen columns c0..c1 at distance y0 (retro16 ridge mesh: 7 rows in depth,
    heights fall to the water front and back). Height profile = sum of humps peaks=[(col, m, width px)],
    a little jitter, ends tapered over ~6 px. Sinks 2 m below the water (no gap on the waterline)."""
    x0, x1 = xcol(c0, y0), xcol(c1, y0)
    step = (x1 - x0) / (c1 - c0) * step_px
    xs = np.arange(x0, x1 + step * 0.5, step)
    ridge = []
    for x in xs:
        c = col_of(x, y0)
        hgt = sum(ph * math.exp(-((c - pc) / pw) ** 2) for pc, ph, pw in peaks)
        hgt *= 1 + rnd.uniform(-0.07, 0.07)
        k = max(0.0, min(1.0, min(c - c0, c1 - c) / 6.0))
        ridge.append(hgt * k * k * (3 - 2 * k))
    rows = [-1.0, -0.55, -0.2, 0.0, 0.3, 0.7, 1.0]
    verts, faces = [], []
    for i, x in enumerate(xs):
        for j, v in enumerate(rows):
            fall = max(0.0, 1 - abs(v)) ** 1.35
            z = ridge[i] * fall + (rnd.uniform(-0.05, 0.05) * ridge[i] if 0 < abs(v) < 1 else 0)
            verts.append((x + rnd.uniform(-0.3, 0.3) * step * (1 if abs(v) < 1 else 0), y0 + v * depth * 0.5,
                          max(-2.0, z - 2.0)))
    nr = len(rows)
    for i in range(len(xs) - 1):
        for j in range(nr - 1):
            a = i * nr + j
            b = (i + 1) * nr + j
            faces.append((a, b, b + 1, a + 1))
    return tagk(R.mesh_from(name, verts, faces, mat, smooth=False), kind, grp), xs, np.array(ridge)


def back_scene(rnd):
    mt = R.m_tone
    # ---- faint far strip (4.4 km) behind the lighthouse island: 2 hazy tones
    island_mesh("Far2", rnd, D_FAR2, 500, 496, 660, [(560, 26, 34), (622, 20, 26)],
                mt(FAR2, [0.62], light=LB, name="Far2"), "far")
    # ---- left island chain (3.3 km): high island + a low tail that ends well before the sun column
    fm = mt(FARI, [0.5, 0.78], light=LB, noise=0.1, nscale=0.02, ncoord="world", ndetail=1.0, name="FarI")
    island_mesh("FarIsland", rnd, D_FAR, 700, 10, 190, [(112, 60, 15), (80, 30, 22), (142, 26, 17), (40, 22, 26),
                                                        (174, 9, 9)],
                fm, "island")
    # ---- freighter (2 km), broadside, bow to the right
    ship(xcol(392, D_SHIP), D_SHIP)
    # ---- lighthouse island (1.15 km): rocky hump with scrub, keeper's house and the tower
    im = mt(ISL, [0.3, 0.56, 0.82], light=LB, noise=0.14, nscale=0.06, ncoord="world", ndetail=1.0, name="Isl")
    _, ix, iz = island_mesh("Island", rnd, D_ISL, 260, 458, 668, [(520, 21, 34), (482, 10, 13), (596, 17, 30),
                                                                   (644, 12, 22)], im, "island")
    sm = mt(ISL[:3], [0.4, 0.75], light=LB, noise=0.14, nscale=0.9, ndetail=1.0, name="Scrub")
    for k in range(60):
        x = rnd.uniform(ix[0], ix[-1])
        zr = float(np.interp(x, ix, iz))
        if zr < 5.0 or abs(col_of(x, D_ISL) - 512) < 5:
            continue
        v = -rnd.random() ** 1.5 * 0.6
        z = zr * (1 - abs(v)) ** 1.35 - 2.0
        r = rnd.uniform(2.2, 4.0)
        ob = C.add_prim("ico", "Scrub", sm, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, D_ISL + v * 130, z + r * 0.25)) @ Matrix.Diagonal((r * 1.3, r, r * 0.8, 1))
        C.set_smooth(ob)
        tagk(ob, "scrub", "scrub")
    rk = mt(ROCKI, [0.45, 0.75], light=LB, name="Rock")
    for k in range(9):
        c = rnd.uniform(463, 486) if k < 5 else rnd.uniform(600, 640)
        x = xcol(c, D_ISL - 110)
        r = rnd.uniform(3.0, 5.5)
        ob = C.add_prim("ico", "Rock", rk, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, D_ISL - 110 + rnd.uniform(-20, 20), 0.2)) @ Matrix.Diagonal((r * 1.6, r, r * 0.8, 1))
        tagk(ob, "rock")
    xl = xcol(512, D_ISL)
    lighthouse(xl, D_ISL - 5, float(np.interp(xl, ix, iz)) - 4.5)      # on the crest line: base embedded
    xh = xcol(527, D_ISL)
    house(xh, D_ISL - 8, float(np.interp(xh, ix, iz)) - 4.5)
    tagk(R.hpoly("Water", [(-12000, -40), (12000, -40), (12000, 4950), (-12000, 4950)], 0.0, R.m_flat(WB[3]), 0.01),
         "water")


def ship(x, y):
    """Small freighter silhouette: raked hull, container stacks, bridge block + funnel aft."""
    mt = R.m_tone
    hull = mt(SHIP[:2], [0.62], light=LB, name="ShipHull")
    sup = mt(SHIP[1:], [0.3, 0.62], light=LB, name="ShipSup")
    L, B = 112.0, 16.0
    pts = [(x - L / 2, y - B / 2), (x + L / 2 - 14, y - B / 2), (x + L / 2 + 5, y), (x + L / 2 - 14, y + B / 2),
           (x - L / 2, y + B / 2)]
    tagk(R.hpoly("Hull", pts, 2.5, hull, th=11.0), "ship", "ship")
    tagk(R.box("Bridge", (x - L / 2 + 11, y, 14.5), (15, 14, 13), sup), "ship", "ship")
    tagk(R.box("Funnel", (x - L / 2 + 6, y, 23.5), (5, 5, 7), hull), "ship", "ship")
    for k, cx in enumerate((-22.0, -4.0, 14.0, 31.0)):
        tagk(R.box("Cont", (x + cx, y, 10.5), (16, 13, 5 + (2.5 if k in (1, 2) else 0)), sup if k % 2 else hull),
             "ship", "ship")
    tagk(R.box("Fore", (x + L / 2 - 8, y, 9.5), (6, 8, 3), sup), "ship", "ship")


def lighthouse(x, y, z0):
    mt = R.m_tone
    tw = mt(TOWER, [0.38, 0.66], light=LB, name="Tower")
    bd = mt(BAND, [0.5], light=LB, name="Band")
    dk = R.m_flat(ISL[0])
    g = "lighthouse"
    tagk(R.loft("Tower", [(x, y, z0 - 2.0), (x, y, z0 + 17.0)], [3.1, 2.3], tw, 12), "tower", g)
    tagk(R.loft("TBand", [(x, y, z0 + 7.5), (x, y, z0 + 11.0)], [2.8, 2.62], bd, 12), "tower", g)
    tagk(R.loft("Gallery", [(x, y, z0 + 17.0), (x, y, z0 + 17.9)], 3.3, dk, 12), "tower", g)
    tagk(R.loft("Lamp", [(x, y, z0 + 17.9), (x, y, z0 + 20.4)], 1.9, R.m_flat(LAMP), 12), "tower", g)
    tagk(R.loft("LRoof", [(x, y, z0 + 20.4), (x, y, z0 + 22.6)], [2.4, 0.35], dk, 12), "tower", g)


def house(x, y, z0):
    mt = R.m_tone
    g = "house"
    tagk(R.box("HWall", (x, y, z0 + 2.5), (8.0, 6.0, 5.0), mt(TOWER, [0.4, 0.7], light=LB, name="HWall")), "tower", g)
    v = [(x - 4.4, y - 3.4, z0 + 5.0), (x + 4.4, y - 3.4, z0 + 5.0), (x + 4.4, y + 3.4, z0 + 5.0),
         (x - 4.4, y + 3.4, z0 + 5.0), (x - 4.4, y, z0 + 7.6), (x + 4.4, y, z0 + 7.6)]
    f = [(0, 1, 5, 4), (3, 4, 5, 2), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]
    ob = R.mesh_from("HRoof", v, f, mt(BAND, [0.5], light=LB, name="HRoof"), smooth=False)
    R._fix_normals(ob)
    tagk(ob, "tower", g)


# ================================================================== FRONT LAYER geometry (the bow)
B = 2.45                  # half-beam at the gunwale (outer edge of the cap rail)
Y0, YT = -0.35, 1.25      # the bow curve starts at Y0 and closes in the stem at YT (fishable water starts at 1.6)
Y_AFT = -7.5              # the deck runs out of the picture (the camera is at y = -10.5)
GUN = STAND + 0.58        # top of the bulwark
FRONT_KINDS_SOLID = ["plank", "deck", "margin", "bulwark", "cap", "prop", "ring"]


def half_beam(y):
    if y <= Y0:
        return B
    t = min(1.0, (y - Y0) / (YT - Y0))
    return B * max(0.0, 1 - t ** 2.2) ** 0.55


def outline(y_from=Y_AFT, n_side=6, n_bow=26, inset=0.0):
    """Plan outline at the gunwale from the aft end of the left side, round the stem, to the aft end of the
    right side; offset `inset` m inwards along the 2D normal (clockwise path -> inward = (ty, -tx))."""
    left = [(-B, y) for y in np.linspace(y_from, Y0, n_side)[:-1]]
    for k in range(n_bow):
        t = k / (n_bow - 1)
        tt = 1 - (1 - t) ** 1.6                   # denser near the stem
        y = Y0 + tt * (YT - Y0)
        left.append((-half_beam(y), y))
    right = [(-x, y) for (x, y) in left[::-1]][1:]
    pts = left + right
    if not inset:
        return pts
    out = []
    n = len(pts)
    for i, (x, y) in enumerate(pts):
        ax, ay = pts[max(0, i - 1)]
        bx, by = pts[min(n - 1, i + 1)]
        tx, ty = bx - ax, by - ay
        ln = math.hypot(tx, ty) or 1.0
        out.append((x + ty / ln * inset, y - tx / ln * inset))
    return out


def rect_loft(name, pts2, z, hh, ht, mat):
    """Box-section tube along a horizontal path (half-height hh, half-thickness ht): walls / rails."""
    k = 1 / math.sqrt(2)
    return R.loft(name, [(x, y, z) for x, y in pts2], [(hh / k, ht / k)] * len(pts2), mat, segs=4, twist=math.pi / 4,
                  smooth=False)


def y_end_for(xabs, inset):
    """Largest y where the deck (outline inset by `inset`) is still at least xabs wide."""
    lo, hi = Y0, YT
    if half_beam(Y0) - inset < xabs:
        return None
    for _ in range(40):
        m = (lo + hi) / 2
        if half_beam(m) - inset * (1 + 1.2 * (m - Y0) / (YT - Y0)) >= xabs:
            lo = m
        else:
            hi = m
    return lo


PROPS = {}


def front_scene(rnd):
    mt = R.m_tone
    PROPS.clear()
    # ---- deck: dark base (shows as caulking in the gaps) + individual teak planks fore and aft
    tagk(R.hpoly("DeckBase", outline(inset=0.1), STAND - 0.06, R.m_flat(WOOD[0]), 0.04), "deck", "deck0")
    pw = 0.2
    n = int(2 * (B - 0.12) / pw)
    x0 = -n * pw / 2
    for i in range(n):
        xc = x0 + pw * (i + 0.5)
        ye = y_end_for(abs(xc) + pw / 2, 0.14)
        if ye is None:
            continue
        bias = rnd.uniform(-0.06, 0.06)
        pm = mt(WOOD[1:], [0.5, 0.7, 0.9], light=LB, bias=bias, noise=0.15, nscale=1.3, ncoord="object",
                nvec=(9.0, 0.35, 1.0), ndetail=1.0, name="Plank")
        tagk(R.box("Plank", (xc, (ye + Y_AFT) / 2, STAND - 0.025 + rnd.uniform(-0.004, 0.004)),
                   (pw - 0.03, ye - Y_AFT, 0.05), pm), "plank", f"pl{i}")
    # margin (covering) board along the bulwark: hides the stepped plank ends in the bow
    mb = mt(WOOD[1:4], [0.45, 0.75], light=LB, noise=0.08, nscale=2.0, name="Margin")
    tagk(rect_loft("Margin", outline(inset=0.24), STAND + 0.004, 0.012, 0.13, mb), "margin", "margin")
    # ---- bulwarks (white, inner face visible) + navy cap rail
    hm = mt(HULL, [0.3, 0.55, 0.8], light=LB, bias=-0.1, grads=((2, STAND, GUN, 0.24, "world"),), name="Bulwark")
    tagk(rect_loft("Bulwark", outline(inset=0.075), (STAND - 0.06 + GUN) / 2, (GUN - STAND + 0.06) / 2, 0.045, hm),
         "bulwark", "bulwark")
    cm = mt(BLUE, [0.42, 0.72], light=LB, name="Cap")
    tagk(rect_loft("Cap", outline(inset=0.06), GUN + 0.03, 0.035, 0.1, cm), "cap", "cap")
    # ---- stainless bow pulpit: top rail + mid rail + stanchions (thin: no outline)
    stl = mt(STEEL, [0.45, 0.78], light=LB, name="Steel")
    bow = [p for p in outline(y_from=Y0 - 1.15, n_side=3, n_bow=18, inset=0.1)]
    zt, zm = GUN + 0.74, GUN + 0.38
    rails = []
    rails.append(tagk(R.loft("TopRail", [(x, y, zt) for x, y in bow], 0.024, stl, 8), "rail", "pulpit"))
    rails.append(tagk(R.loft("MidRail", [(x, y, zm) for x, y in bow], 0.018, stl, 8), "rail", "pulpit"))
    nb = len(bow)
    for k in (0, nb // 4 - 1, nb // 2, nb - nb // 4, nb - 1):
        x, y = bow[k]
        rails.append(tagk(R.loft("Stanchion", [(x, y, GUN), (x, y, zt)], 0.022, stl, 8), "rail", "pulpit"))
    # ---- cooler (left, on the deck), blue lid
    rot = Matrix.Rotation(math.radians(-6), 4, "Z")
    cx, cy = -1.42, -0.62
    tagk(R.box("Cooler", (cx, cy, STAND + 0.22), (0.9, 0.52, 0.44), mt(HULL[1:], [0.4, 0.72], light=LB, name="Cool"),
               rot=rot, bevel=0.03), "prop", "cooler")
    tagk(R.box("Lid", (cx, cy, STAND + 0.475), (0.95, 0.56, 0.07), mt(BLUE, [0.4, 0.7], light=LB, name="Lid"),
               rot=rot, bevel=0.02), "prop", "cooler")
    for sx in (-1, 1):
        p = Matrix.Translation((cx, cy, 0)) @ rot @ Vector((sx * 0.47, 0, STAND + 0.33))
        tagk(R.box("Handle", tuple(p), (0.04, 0.2, 0.05), stl, rot=rot), "prop", "cooler")
    # ---- life ring hanging inside the starboard pulpit, facing aft (towards the angler)
    life_ring((2.0, Y0 - 0.98, GUN + 0.33), 0.33, 0.085, mt(RING, [0.4, 0.72], light=LB, name="Ring"),
              mt(HULL[1:], [0.4, 0.72], light=LB, name="RingW"))
    # ---- rope coil on the bow deck + bow cleat
    rm = mt(ROPE, [0.4, 0.7], light=LB, pats=((2, 0.03, 0.4, -0.2, "world"),), name="Rope")
    pts = []
    rcx, rcy = 0.78, 0.42
    for k in range(64):
        a = 2 * math.pi * 2.0 * k / 63
        r = 0.08 + 0.21 * k / 63
        pts.append((rcx + r * math.cos(a), rcy + 0.8 * r * math.sin(a), STAND + 0.03))
    ex, ey = pts[-1][0], pts[-1][1]
    clx, cly = 0.0, YT - 0.42
    for k in range(1, 7):
        t = k / 6
        pts.append((ex + (clx + 0.1 - ex) * t, ey + (cly - ey) * t, STAND + 0.03 + 0.04 * math.sin(math.pi * t)))
    tagk(R.loft("Coil", pts, 0.027, rm, 8), "prop", "coil")
    tagk(R.box("Cleat", (clx, cly, STAND + 0.05), (0.3, 0.07, 0.05), stl, bevel=0.015), "prop", "cleat")
    tagk(R.box("CleatB", (clx, cly, STAND + 0.02), (0.1, 0.06, 0.05), stl), "prop", "cleat")
    # ---- trolling rod in a holder on the port cap rail, leaning out over the water
    a = Vector((-2.36, -1.35, GUN - 0.25))
    b = Vector((-4.3, 1.55, GUN + 1.95))
    d = (b - a).normalized()
    tagk(R.loft("Holder", [tuple(a - d * 0.05), tuple(a + d * 0.3)], 0.045, stl, 8), "prop", "holder")
    rodm = mt(WOOD[:3], [0.55], light=LB, name="Rod")
    tagk(R.loft("Rod", [tuple(a), tuple(a + (b - a) * 0.5), tuple(b)], [0.026, 0.018, 0.011], rodm, 6), "rod", "rod")
    reel = a + d * 0.42 + Vector((0.07, 0.0, -0.05))
    tagk(R.ellipsoid("Reel", tuple(reel), (0.08, 0.07, 0.07), stl, segs=10, rings=6), "prop", "reel")
    return rails


def life_ring(c, rad, r, m_red, m_white):
    """Torus in a vertical plane facing aft (slightly turned to the centreline): 8 arcs, red / white."""
    c = Vector(c)
    u = Vector((1.0, -0.4, 0.0)).normalized()
    v = Vector((0.0, 0.0, 1.0))
    for s in range(8):
        pts = []
        for k in range(5):
            th = 2 * math.pi * (s + k / 4) / 8 + math.pi / 8
            pts.append(tuple(c + (u * math.cos(th) + v * math.sin(th)) * rad))
        tagk(R.loft("Ring", pts, r, m_red if s % 2 == 0 else m_white, 8, caps=True), "ring", "ring")
    # the lanyard to the stanchion
    top = c + v * (rad + r)
    tagk(R.loft("Lanyard", [tuple(top), tuple(top + Vector((0.1, 0.02, 0.22)))], 0.012, m_white, 5), "rail", "pulpit")


# ================================================================== back composite
# water depth zones (start row, end row) for WB[i] -> WB[i+1]; the play area (rows >= 140) stays calm deep blue
WATER_Z = [(92, 95), (97, 101), (104, 112), (122, 140), (205, 245)]
ROW_H, ROW_P = 91, 335             # horizon-side / boat-side reference rows for the dash length
BAND_H = 30                        # the band dash length starts longer (clean long dashes at the horizon)
LAM = 13.0                         # swell crest spacing (m)


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


def far_reflection(pal, idx, water, refl_objs):
    """EXACT reflection of the far silhouettes (mirror pass): the layers' own palette colours tinted towards
    the far water and darkened; runs >= 3 px; a lighter break line every third row (the same reflected colour
    pushed further towards the water); dissolving over the bottom 30 % (the swell breaks it up)."""
    mp = R.mirror_pass(refl_objs, tag=f"{SID}_farmirror")
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
        p = np.clip((frac[r] - 0.7) / 0.3, 0, 1)
        brk = R.run_noise(rng, W, 3, 8) < p
        keep = m & ~brk & water[r]
        out[r, keep] = lab[r, keep] - 1
        keepm[r] = keep
        if (r - int(np.median(top))) % 3 == 2:
            linem[r] = R.dash_mask(W, 0.45, 5, 16, rng) & keep
    body = keepm & ~linem
    out2, pal = R.recolour(out, pal, body, reflect_tint(WB[1], 0.28, 0.8), snap=0.03)
    out2, pal = R.recolour(out2, pal, linem, reflect_tint(WB[1], 0.58, 0.93), snap=0.03)
    return out2, pal, keepm


def horizon_sky(pal, idx, water, wband):
    """The calm far water mirrors the sky exactly (bands + glow rings, no disc) across the whole horizon,
    a few rows deeper under the sun; tinted towards the rose-silver far band, joined to the bands by dash
    dithering; retro16 light break lines (the local band) every third row."""
    rng = random.Random(21)
    rows = np.arange(H)[:, None]
    cols = np.arange(W)[None, :]
    r_end = 97.0 + 7.0 * np.exp(-((cols - SUN_C) / 60.0) ** 2)          # per column: end of the mirror band
    zone = water & (rows < r_end + 1)
    thr = R.dash_threshold(H, W, rng, 2, 7)
    m_idx, _ = R.quantize(MSKY_RGB, zone, pal, dither=True, thr=thr)
    fade = np.clip((rows - (r_end - 5.0)) / 5.0, 0, 1)                   # hand over to the bands
    use = zone & (R.dash_threshold(H, W, rng, 3, 9) >= fade)
    out = idx.copy()
    out[use] = m_idx[use]
    out, pal = R.recolour(out, pal, use, reflect_tint(WB[0], 0.24, 0.9), snap=0.022)
    for r in range(int(HZ), int(r_end.max()) + 1):
        if r % 3 == 1:
            bl = R.dash_mask(W, 0.35, 5, 16, rng) & use[r]
            out[r, bl] = wband[r, bl]
    return out, pal, use


def swell(pal, idx, water, avoid, wband):
    """Long swell crests every LAM m (world): a broken light crest dash (water +1) with a darker face row
    under it (water -1), gently wobbling, dashes growing towards the viewer. The middle of the play area
    keeps only a few faint crest dashes (calm water for casting + fish shadows)."""
    rng = random.Random(11)
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    dn1 = R.ramp_step(pal, WSTEP, wband, -1)
    ok = water & ~avoid
    out = idx.copy()
    cols = np.arange(W)
    calm = (cols >= CALM_C[0]) & (cols < CALM_C[1])
    y = 9.0
    ph = rng.uniform(0, 6.28)
    ncrest = 0
    while True:
        r0 = R.water_row(y, STAND)
        r1 = R.water_row(y + LAM, STAND)
        if r0 - r1 < 2.4 or r0 < 95:
            break
        amp = min(1.6, 0.16 * (r0 - r1))
        wob = amp * (0.65 * np.sin(cols / 41.0 + ph) + 0.35 * np.sin(cols / 13.0 + 2 * ph))
        rows_c = np.round(r0 + wob).astype(int)
        lmin, lmax = R.dash_len(r0, ROW_H, ROW_P)
        lmin, lmax = lmin * 2, lmax * 2 + 4
        central = calm & (r0 > 126)
        lit = R.dash_mask(W, 0.36 if r0 < 126 else 0.4, lmin, lmax, rng)
        lit &= ~central | R.dash_mask(W, 0.14, lmin, lmax, rng)
        face = R.dash_mask(W, 0.3, lmin, lmax, rng) & ~central & lit
        for c in np.flatnonzero(lit):
            r = rows_c[c]
            if 0 <= r < H and ok[r, c]:
                out[r, c] = up1[r, c]
        for c in np.flatnonzero(face):
            r = rows_c[c] + 1
            if 0 <= r < H and ok[r, c]:
                out[r, c] = dn1[r, c]
        y += LAM * (1 + 0.05 * ncrest)
        ph += 1.7
        ncrest += 1
    return out, ncrest


def chop(pal, idx, water, avoid, wband):
    """Sparse short chop marks: a light dash (water +1) with a darker dash one row below, 1 px right.
    Density is cut in the calm central play rectangle."""
    rng = random.Random(13)
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    dn1 = R.ramp_step(pal, WSTEP, wband, -1)
    ok = water & ~avoid
    for k in range(300):
        y = 2.0 + 260 * rng.random() ** 1.8
        x = rng.uniform(-1, 1) * (y + 14) * 0.8
        c, r = rc((x, y, 0))
        if r <= 95 or r >= H - 1 or c < 0 or c >= W or (r < 112 and rng.random() < 0.6):
            continue
        ri, ci = int(r), int(c)
        if not ok[ri, ci]:
            continue
        central = CALM_C[0] < c < CALM_C[1] and r > 126
        if central and rng.random() < 0.88:
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = min(12, max(2, int(round(520 / depth * rng.uniform(0.25, 0.55)))))
        draw_dash(idx, r, c, n, int(up1[ri, ci]), ok)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), int(dn1[min(H - 1, ri + 1), ci]), ok)
    return idx


def far_rims(idx, pal, kid):
    """Warm rim on the silhouette tops / SUN-FACING ends of the far layers, only near the sun (glow): left of
    the sun column the right edges face the sun, right of it the left edges."""
    lv, _ = R.glow_level(PR)
    prox = np.clip(lv / 0.35, 0, 1)
    rim = PR.rim
    cols = np.arange(W)[None, :]
    out, p2 = idx, pal
    for kinds in (("far",), ("island", "scrub", "rock"), ("ship",), ("tower",)):
        m = np.isin(kid, kinds)
        if not m.any():
            continue
        up_out = m & ~R.shift(m, 1, 0, False)
        r_out = m & ~R.shift(m, 0, -1, False)
        l_out = m & ~R.shift(m, 0, 1, False)
        side_out = np.where(cols < SUN_C, r_out, l_out)
        k = (up_out * 1.0 + side_out * 0.6).clip(0, 1) * prox
        out, p2 = R.blend_idx(out, p2, k > 0.55, rim["col"], 0.55, lighter=True, snap=0.025)
        out, p2 = R.blend_idx(out, p2, (k > 0.2) & (k <= 0.55), rim["col"], 0.3, lighter=True, snap=0.025)
    return out, p2


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    PER.hook("back_scene", rnd=rnd, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_back")
    kid = R.kind_map(ps)
    rows = np.arange(H)[:, None]
    sky = ~ps["a"]
    # rows under the true horizon that the (clipped) water plane leaves empty are water too
    fill = sky & (rows >= int(math.ceil(HZ)))
    kid = np.where(fill, "water", kid)
    sky &= ~fill
    water = kid == "water"
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = SKY_RGB[sky]
    rgb[fill] = R.hexrgb(WB[0])
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid | sky | water, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=3.0, rel=0.004, steps=1)
    idx = np.where(np.isin(kid, ["ship", "tower"]), lined, idx)
    idx, pal = far_rims(idx, pal, kid)
    # ---- water: depth bands with horizontal dash dithering
    wband = R.water_bands(pal, WB, WATER_Z, random.Random(5), BAND_H, ROW_P)
    idx[water] = wband[water]
    idx, pal, hzm = horizon_sky(pal, idx, water, wband)
    # ---- exact reflections of the far silhouettes
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("far", "island", "scrub", "rock", "ship", "tower")]
    idx, pal, farm = far_reflection(pal, idx, water, refl_objs)
    # ---- swell crests + chop (not over the reflections / the glitter column)
    g_am, g_br = R.glitter_masks(PR, water, random.Random(17), avoid=farm)
    idx, ncrest = swell(pal, idx, water, farm | g_am | g_br | hzm, wband)
    idx = chop(pal, idx, water, farm | g_am | g_br | hzm, wband)
    # ---- sun path: a faint warm sheen + sparse short glitter dashes, far part only
    g = PR.glitter
    r0 = g["row0"]
    r_end = r0 + g["far"] * (g["row1"] - r0)
    t = np.clip((rows - r0) / (r_end - r0), 0, 1)
    halfw = g["width"][0] + (g["width"][1] - g["width"][0]) * t
    cols = np.arange(W)[None, :]
    sheen = 0.75 * (1 - t) ** 1.5 * np.exp(-((cols - SUN_C) / (1.7 * halfw)) ** 2) * (rows < r_end)
    sm = water & (sheen > R.dash_threshold(H, W, random.Random(23), 2, 6)) & ~farm
    if SHEEN > 0:
        idx, pal = R.blend_idx(idx, pal, sm, GLIT[0], SHEEN, lighter=True, snap=0.025)
    idx[g_am] = pal.index(GLIT[0])
    idx[g_br] = pal.index(GLIT[1])
    # ---- thin mist line on the horizon (dash dithered, wisps)
    land = ~sky & ~water
    line = np.array([np.flatnonzero(water[:, c]).min() if water[:, c].any() else H for c in range(W)], float)
    ma = R.mist_amount(PR, line, land, water)
    thr = R.dash_threshold(H, W, random.Random(29), 2, 9)
    idx, pal = R.blend_idx(idx, pal, (ma > thr) & (ma > 0.08), PR.mist["col"], 0.5, snap=0.025)
    idx, pal = PER.hook("back_post", idx, pal, kid=kid, water=water, sky=sky, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "back")
    # ---- checks: colours, the painted play-area water vs Unity's waterTint, glitter extent
    crop = img[CY0:CY0 + CH, CX0:CX0 + CW]
    play = np.round(img[150:240, CALM_C[0]:CALM_C[1], :3] * 255).astype(int)
    keys, cnt = np.unique(play.reshape(-1, 3), axis=0, return_counts=True)
    top = keys[np.argmax(cnt)]
    top_hex = "#%02x%02x%02x" % tuple(top)
    gl = np.flatnonzero((g_am | g_br).any(1))
    print("HYB back colours", R.count_colours(img), "crop colours", R.count_colours(crop), "palette", len(pal))
    print("HYB water play-area mode", top_hex, "share", round(cnt.max() / cnt.sum(), 2), "L", round(R.lum(top_hex), 3),
          "| waterTint", PR.water["tint"], "L", round(R.lum(PR.water["tint"]), 3), "| waterDeep", PR.water["deep"],
          "agree", top_hex == PR.water["tint"])
    print("HYB glitter rows", int(gl.min()) if len(gl) else "-", "-", int(gl.max()) if len(gl) else "-",
          "px", int((g_am | g_br).sum()), "swell crests", ncrest)
    return pal


# ---------------------------------------------------------------- front post helpers
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


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    front_scene(rnd)
    PER.hook("front_scene", rnd=rnd, refl=None, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_front")
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    thin = (kid == "rail") | (kid == "rod")
    idx = R.remove_specks(idx, 3)
    idx = R.despeckle(idx, ps["id"], protect=~solid | thin, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    contact_shadow(idx, pal, kid)
    # warm rim on the OUTER sun-side (left / top) silhouette of the solid parts, before the outline
    solid_k = np.isin(kid, FRONT_KINDS_SOLID)
    idx, pal = R.rim_light(idx, pal, PR, mask=solid_k)
    # selective outline for the solid parts only (hue-shifted darker neighbour, never ink); rails / rod get none
    base = np.where(thin, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    idx = np.where(thin & (idx >= 0), idx, ol)
    idx, pal = PER.hook("front_post", idx, pal, kid=kid, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "front")
    print("HYB front colours", R.count_colours(img), "palette", len(pal),
          "rail L", round(R.lum(STEEL[1]), 3), round(R.lum(STEEL[2]), 3), "vs water L", round(R.lum(WB[4]), 3),
          round(R.lum(WB[5]), 3))


def main():
    C.reset_scene()
    which = PER.which()                      # back / front (+ --period <p> [--dry], see hyb_period)
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    PER.stage_json(SID, PR, clouds=False, birds=True)
    print("HYB OCEAN done")


if __name__ == "__main__":
    main()
