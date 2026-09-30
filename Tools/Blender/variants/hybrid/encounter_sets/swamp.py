"""
Encounter set SWAMP (피라루쿠 arapaima; Docs/legends_rollout.md 4.3): brown-green murk, visibility ~2.2 m, surface lures.
The camera sits ~0.95 m under the surface and looks slightly up: the upper part of the window is the UNDERSIDE of the
surface (the ceiling layer: dull olive light, a dim amber Snell's-window glow overhead, duckweed / leaves / lily pads as
silhouettes) with the frog / popper floating on it; drowned trunks rise through the murk band into it, roots hang
down; below, the murk falls away to near black (where the eyes come up from). Built by hyb_encounter.py
(see encounter_sets/README.md).

  uw_swamp_bg.png       640x400 opaque  murk #4a4a2a (just under the surface) -> #2e3620 -> #1c2416 -> #10180e, banded +
                                        Bayer; far drowned trunks #1a2214 (and paler #262e1c further back) standing
                                        in a dash-dithered haze band at row HORIZON (= the projected floor horizon)
  uw_swamp_ceiling.png  768x120 alpha   the surface seen from below, pseudo-perspective (camera 0.95 m under it): top
                                        row = nearest (overhead), BOTTOM row = its far edge on the projected surface
                                        horizon; olive #6a6a3a, amber Snell glow #a89a5a / #8a8248 at the top, turning
                                        to a dark mirror #4a4a2a towards the far edge, which dissolves into the murk;
                                        silhouettes #1e2616: duckweed, fallen leaves, twigs, notched lily pads (near
                                        ones with a 1 px bright meniscus)
  uw_swamp_mid.png      768x200 alpha   drowned forest drawn in the set camera's vertical perspective: trunks
                                        #1a2412 .. #3a4228 (far ones paler #262e1c..) rise out of the murk to their own
                                        surface row (near = wide + high, far = thin + low), pierce it with a 1 px bright
                                        line and meet their dim mirror image above (the underside reflects); dead
                                        branches; root curtains hanging down from the surface; the bases dissolve
                                        into the murk; bottom row = the floor horizon
  uw_swamp_floor.png    768x120 alpha   dim leaf litter #1a2014 .. #2e3620 (floor ~2.1 m under the camera), only the
                                        near rows show, dissolving fast into the murk
  uw_swamp_fore.png     256x128 alpha   a big out-of-focus gnarled root for the bottom-left #0a0e08 (flat tones,
                                        dithered edge, a faint dim rim from the surface light)
  enc_frame_swamp.png    24x24          9-slice, 6 px border: dark root wood, 1 px #d8e070 inner line at px 3,
                                        firefly corner studs (yellow-green glow dots)
  lures: frog, popper (new; frame-1 tweaks here: the legs kick / the nose pops up)
"""
import math
import random
import bmesh
import bpy
import numpy as np
from mathutils import Vector, Matrix
import hyb_core as R
import fk_common as C
import hyb_encounter_kit as E

V = Vector
SET = "swamp"
PRESET = "swamp"
LURES = ["frog", "popper"]


# ------------------------------------------------------------------ lure frame-1 tweaks (billboards 16x8)
def _keep_frame(objs, fn):
    """Run a frame-1 tweak without changing the billboard framing: kit.lure_frames fits the camera to the mesh bounds,
    so two loose (never rendered) vertices pin the frame-0 bounds (keep the tweak inside them)."""
    x0, x1, z0, z1 = C.world_bounds(objs)
    fn(objs, (x0, x1, z0, z1))
    bpy.context.view_layer.update()
    ob = next(o for o in objs if o.type == "MESH")
    inv = ob.matrix_world.inverted()
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for p in ((x0, 0.0, z0), (x1, 0.0, z1)):
        bm.verts.new(inv @ V(p))
    bm.to_mesh(ob.data)
    bm.free()


def _about(p, ang_deg, axis="Y"):
    return Matrix.Translation(V(p)) @ Matrix.Rotation(math.radians(ang_deg), 4, axis) @ Matrix.Translation(-V(p))


def _tweak_frog(objs):
    """Frame 1: the legs kick (the back leg folds up at the hip, the front leg swings back)."""
    def fn(objs, b):
        for o in objs:
            if o.name.startswith("LegB"):
                o.matrix_world = _about((-0.6, 0.0, -0.2), 28) @ o.matrix_world
            elif o.name.startswith("LegF"):
                o.matrix_world = _about((0.5, 0.0, -0.3), -30) @ o.matrix_world
    _keep_frame(objs, fn)


def _tweak_popper(objs):
    """Frame 1: the pop - the nose kicks up (about the body centre, so it stays inside the frame-0 bounds)."""
    def fn(objs, b):
        x0, x1, z0, z1 = b
        piv = ((x0 + x1) / 2, 0.0, (z0 + z1) / 2)
        for o in objs:
            o.matrix_world = _about(piv, -5) @ o.matrix_world
    _keep_frame(objs, fn)


LURE_TWEAKS = {"frog": _tweak_frog, "popper": _tweak_popper}

# ------------------------------------------------------------------ palette (brown-green murk, dusk)
MURK = ["#4a4a2a", "#3a4028", "#2e3620", "#1c2416", "#10180e"]           # under the surface -> deep (3a4028 = haze)
TRUNK_BG = "#1a2214"                                                      # far drowned trunks (bg)
TRUNK_BG_FAR = "#262e1c"                                                  # further back, paler
CEIL = ["#3e3e24", "#4a4a2a", "#5a5a32", "#6a6a3a", "#8a8248", "#a89a5a"]  # ceiling tones: mirror .. Snell glow
DEBRIS = "#1e2616"                                                        # floating silhouettes
MENISCUS = "#8a8248"
TRUNK = ["#1a2412", "#242e18", "#2e3820", "#3a4228"]                      # near trunks dark -> lit
TRUNK_FAR = ["#262e1c", "#2e3620", "#343c24"]                             # far trunks (near the murk)
ROOT = ["#161e10", "#222c16", "#303a20"]
LITTER = ["#141a10", "#1a2014", "#242a18", "#2e3620"]
FORE = ["#060a05", "#0a0e08", "#121a0e", "#26301c"]
FRAME = dict(ink="#080a05", dark="#1e160c", mid="#322614", lit="#4a3a20", line="#d8e070",
             fly=["#3e4a16", "#b8d040", "#f4ff9a"])

# ------------------------------------------------------------------ layout
BG_W, BG_H = 640, 400
HORIZON = 150             # bg row on the projected FLOOR horizon (floor 3.05 m under the surface, PROFILE floor_y)
SURF_GAP = 21             # at the rest camera the ceiling's far edge sits ~21 px above the floor horizon
CAM_D = 0.95              # camera depth under the surface (m) for the ceiling's pseudo-perspective
FLOOR_D = 2.1             # floor depth under the camera (m) for the floor layer
REVIEW = dict(ray=("#c8b478", 0.05 * 4, 470, 0), ceiling=(-64, HORIZON - SURF_GAP - 120), mid=(-64, HORIZON - 200),
              floor=(-64, HORIZON), fore=(0, 400 - 128))

# ------------------------------------------------------------------ runtime look (the preview mock uses these; the
# EncounterSetDef row in the code carries the same values - Docs/legends_rollout.md 4.1)
PROFILE = dict(
    lure_at="surface",                       # the lure floats: the surface line sits 0.05 above the lure (set y 0)
    lure_rest=(0.0, 0.0, 0.0), surface_y=0.05, floor_y=-3.0,
    cam=(-1.0, -0.9, -2.6), frame_at=(0.40, 0.72),
    line_up=(-1.4, 0.05, -1.2),              # along the surface
    drag_dir=(-0.5, 0.0, -0.85),
    clear="#10180e",
    rays=[("#c8b478", 0.05, 150, -20)],      # 1 x shared uw_ray
    halo=None,
    snow_far="#6a6a44", snow_near="#a8a070",
    line="#c8c098", silt="#8a7a50",
    rim=("#d8c888", 0.25 * 0.62, [((0, 1), 1.0)]),
    fore_x=-20,
    abyss="#10180e", fog_outline="#2e3a22", sun_mix=0.8, visibility=2.2,
    veil=("#080c06", 0.6), frame_line="#d8e070",
)


# ============================================================================ helpers
def _trunk_mask(h, w, x0, top, base, wid, lean, seed, knob=2.5):
    """A far trunk silhouette: a slightly leaning column, rough bark edge, a little flare at the base."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    t = np.clip((yy - top) / max(1.0, base - top), 0, 1)
    xc = x0 + lean * (base - yy)
    half = wid / 2 * (0.85 + 0.25 * t + 0.35 * np.clip((t - 0.85) / 0.15, 0, 1))
    rough = (E.fbm(xx / 5.0, yy / 14.0, seed) - 0.5) * knob
    return (np.abs(xx - xc) < half + rough) & (yy >= top) & (yy <= base)


def _pad(xx, yy, cx, cy, rx, ry, notch, seed):
    ang = np.arctan2((yy - cy) / ry, (xx - cx) / rx)
    e = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
    wob = 1 + 0.05 * np.sin(ang * 5 + seed)
    dang = np.abs((ang - notch + math.pi) % (2 * math.pi) - math.pi)
    return (e < wob ** 2) & ~((dang < 0.3) & (e > 0.05))


# ============================================================================ uw_swamp_bg (640x400, opaque)
def make_bg():
    h, w = BG_H, BG_W
    stops = [(0, MURK[0]), (124, MURK[0]), (150, MURK[2]), (200, MURK[3]), (285, MURK[4])]
    idx, pal = E.quant_rows(stops, h, w)
    img = R.to_rgba(idx, pal)
    th = R.bayer(h, w)
    rows = np.arange(h)[:, None] + 0.5
    rng = random.Random(21)
    # far drowned trunks: two depths, standing in the haze band, running up behind the ceiling
    far2 = np.zeros((h, w), bool)
    far1 = np.zeros((h, w), bool)
    top = HORIZON - SURF_GAP - 6                     # they reach the surface (the ceiling's dissolving far edge)
    for i, x in enumerate((22, 70, 131, 176, 238, 292, 345, 398, 452, 505, 560, 611)):
        x += rng.uniform(-10, 10)
        far2 |= _trunk_mask(h, w, x, top, HORIZON + 4, rng.uniform(2, 4), rng.uniform(-0.03, 0.03), 40 + i, 1.5)
    for i, x in enumerate((40, 104, 206, 268, 330, 418, 484, 540, 598)):
        x += rng.uniform(-8, 8)
        far1 |= _trunk_mask(h, w, x, top, HORIZON + 7, rng.uniform(3, 6), rng.uniform(-0.04, 0.04), 60 + i, 2.0)
        # a dead branch stub angling up from some trunks
        if rng.random() < 0.6:
            by = rng.uniform(HORIZON - 12, HORIZON - 4)
            L = rng.uniform(5, 10)
            sgn = rng.choice((-1, 1))
            for k in range(int(L)):
                yb, xb = int(by - k * 0.8), int(x + sgn * (4 + k))
                if 0 <= yb < h and 0 <= xb < w:
                    far1[yb, xb] = True
                    if k < L * 0.5 and yb + 1 < h:
                        far1[yb + 1, xb] = True
    fade2 = np.clip((rows - (HORIZON - 16)) / 18.0, 0, 1)
    fade1 = np.clip((rows - (HORIZON - 8)) / 14.0, 0, 1)
    E.put(img, far2 & (th >= fade2 + 0.12), TRUNK_BG_FAR)
    t1 = far1 & (th >= fade1)
    E.put(img, t1, TRUNK_BG)
    # haze band along the floor horizon (one step lighter murk, dash dithered)
    band = np.exp(-((rows - HORIZON) / 8.0) ** 2) * 0.55
    hz = (band > R.dash_threshold(h, w, random.Random(4), 3, 9)) & ~t1
    E.put(img, hz & (rows > HORIZON - 10) & (rows < HORIZON + 10), MURK[1])
    img[..., 3] = 1.0
    return img


# ============================================================================ uw_swamp_ceiling (768x120)
def make_ceiling():
    h, w = 120, 768
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    rows = yy[:, :1]
    d = E.ceiling_rows(h, CAM_D)                         # m to the surface point of each row (top ~1.4 m, bottom far)
    th = R.bayer(h, w)
    thr = 0.5 + (th - 0.5) * 0.45                        # narrow dithered transitions (hybrid bands)
    pal = R.Pal(CEIL + [DEBRIS])
    # base: olive light, a darker band towards the far edge (the mirror), the amber Snell glow overhead (top)
    tone = np.full((h, w), 3.0)
    t = rows / h
    tone -= np.clip((t - 0.52) / 0.3, 0, 1) * 1.0 + np.clip((t - 0.84) / 0.14, 0, 1) * 1.0
    e = ((xx - 440) / 200.0) ** 2 + ((yy + 22) / 78.0) ** 2           # the Snell glow (flattened ellipse, top)
    tone += np.clip((1.0 - e) / 0.35, 0, 1) + np.clip((0.5 - e) / 0.3, 0, 1)
    tone += (E.fbm(xx / 40.0, yy / 9.0, 3.0) - 0.5) * 0.5            # soft uneven light
    k = np.clip(np.floor(tone + thr - 0.5), 0, 5).astype(int)
    idx = np.array([pal.index(c) for c in CEIL])[k]
    deb = np.zeros((h, w), bool)
    men = np.zeros((h, w), bool)
    rng = random.Random(8)
    # duckweed: solid mats (dithered rims) with loose specks around them, flattened by the perspective
    patch = E.fbm(xx / 44.0, yy / (4.0 + yy * 0.06), 11.0)
    rnd = np.random.default_rng(3).random((h, w))
    deb |= patch > 0.75
    fringe = (patch > 0.68) & (patch <= 0.75)
    speck = fringe & (rnd < 0.16 * np.clip(3.2 / d, 0.45, 1.0))
    big = speck & (d < 2.6)
    deb |= speck | R.shift(big, 0, 1, False)
    # world-placed floaters, projected (the rest window shows rows ~30..120 = 1.9 m .. far): lily pads, leaves, twigs
    items = []
    for _ in range(18):
        items.append(("pad", math.exp(rng.uniform(math.log(1.7), math.log(9.0))), rng.uniform(0.13, 0.26)))
    for _ in range(50):
        items.append(("leaf", math.exp(rng.uniform(math.log(1.7), math.log(10.0))), rng.uniform(0.06, 0.11)))
    for _ in range(12):
        items.append(("twig", math.exp(rng.uniform(math.log(1.7), math.log(8.0))), rng.uniform(0.25, 0.6)))
    for kind, dist, s in sorted(items, key=lambda q: -q[1]):
        x = rng.uniform(-1.9, 1.9) * dist
        cy = (h - 1) - (CAM_D * 177.0 / dist - 1.6) + 0.5              # row of that distance (bottom row = far)
        cx = w / 2 + 177.0 * x / dist
        rx = 177.0 * s / dist
        sq = max(0.16, CAM_D / dist)                                    # perspective squash of a flat floater
        if cy < -rx * sq - 2 or rx < 0.8:
            continue
        if kind == "pad":
            m = _pad(xx, yy, cx, cy, rx, max(0.8, rx * sq), rng.uniform(-math.pi, math.pi), rng.random() * 9)
            deb |= m
            if dist < 3.2:                                              # bright meniscus under the near pads
                men |= R.dilate(m) & ~m & (yy > cy)
        elif kind == "leaf":
            a = rng.uniform(0, math.pi)
            ca, sa = math.cos(a), math.sin(a)
            u = ((xx - cx) * ca + (yy - cy) / sq * sa) / rx
            v = (-(xx - cx) * sa + (yy - cy) / sq * ca) / (rx * 0.4)
            deb |= (np.abs(v) < (1 - np.abs(u) ** 1.5)) & (np.abs(u) < 1)
        else:
            a = rng.uniform(0, math.pi)
            n = int(rx * 2) + 2
            for j in range(n):
                tt = j / max(1, n - 1) - 0.5
                px = int(round(cx + math.cos(a) * rx * 2 * tt))
                py = int(round(cy + math.sin(a) * rx * 2 * tt * sq + 0.8 * math.sin(tt * 6)))
                if 0 <= px < w and 0 <= py < h:
                    deb[py, px] = True
    # the far rows keep fewer (they are lost in the mirror); nothing hard near the far edge
    deb &= th < np.clip((h - 3 - rows) / 22.0, 0, 1)
    idx[men & ~deb] = pal.index(MENISCUS)
    idx[deb] = pal.index(DEBRIS)
    img = R.to_rgba(idx, pal)
    # the far edge dissolves into the murk (the bg shows under it)
    gone = np.broadcast_to(np.clip((rows - (h - 8)) / 8.0, 0, 1) > th, (h, w))
    img[gone] = 0.0
    return img


# ============================================================================ uw_swamp_mid (768x200)
# Drawn in the set camera's vertical perspective (not ortho): the camera is CAM_D under the surface and FLOOR_D above
# the floor, so a trunk / root at distance d meets the surface at row MID_EYE - CAM_D*F/d (the surface row), where
# MID_EYE is the eye-level row (the bottom row is the floor horizon, FLOOR_D*F/30 px under eye level). Each trunk is
# seen from the murk up to its surface row, where it meets its own dimmer mirror image (the underside of the surface
# reflects); roots hang down from their surface rows. Near = wide + high, far = thin + low.
MID_W, MID_H, F = 768, 200, 177.0
MID_EYE = MID_H - FLOOR_D * F / 30.0
REFL = ["#3a3e26", "#46482c"]                                   # dim mirror images of the trunks (on the olive)


def surf_row(d):
    return MID_EYE - CAM_D * F / d


def make_mid():
    h, w = MID_H, MID_W
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    th = R.bayer(h, w)
    pal = R.Pal(TRUNK + TRUNK_FAR + ROOT + REFL + MURK + [CEIL[2]])
    idx = np.full((h, w), -1, int)
    thd = R.dash_threshold(h, w, random.Random(6), 2, 6)
    rng = random.Random(3)
    # (mid column, distance m, trunk width m): near ones at the sides / window edges, far thin ones in the centre
    # (the rest window shows columns ~240..528; the lure floats near column ~356)
    trees = [(262, 2.9, 0.46), (505, 3.2, 0.5), (286 + 26, 6.2, 0.46), (462, 7.6, 0.52), (400, 11.5, 0.42),
             (336, 16.0, 0.40), (430, 22.0, 0.40), (366, 26.0, 0.34), (228, 9.5, 0.40), (548, 12.5, 0.40),
             (40, 3.0, 0.64), (132, 3.6, 0.56), (180, 6.8, 0.46), (86, 12.0, 0.36), (606, 5.8, 0.50),
             (662, 2.7, 0.60), (736, 3.4, 0.58), (700, 10.0, 0.40), (12, 16.0, 0.34), (760, 14.0, 0.34)]
    for i, (cx, d, wm) in enumerate(sorted(trees, key=lambda t: -t[1])):         # far first, near on top
        far = d > 11.0
        tones = TRUNK_FAR if far else TRUNK
        s = surf_row(d)
        wpx = max(2.0, F * wm / d)
        lean = rng.uniform(-0.05, 0.05)
        seed = 30.0 + i * 3.1
        t = np.clip((yy - s) / max(1.0, h - s), 0, 1)                           # 0 at the surface .. 1 at the bottom
        xc = cx + lean * (yy - s) + 1.2 * np.sin(yy / 23.0 + seed)
        half = wpx / 2 * (1.0 + 0.12 * t)
        rough = (E.fbm(xx / 4.0, yy / 12.0, seed) - 0.5) * min(5.0, 0.3 * wpx)
        body = (np.abs(xx - xc) < half + rough) & (yy >= s)
        # dead branches on the underwater part (thin, tapering, angled up towards the light or drooping)
        if not far:
            for j in range(rng.randint(1, 2)):
                rb = s + rng.uniform(10, 34)
                L = F * rng.uniform(0.5, 1.1) / d
                sgn = rng.choice((-1, 1))
                ang = math.radians(rng.uniform(-35, 25))
                th0 = max(0.8, wpx * 0.07)
                for k in range(int(L)):
                    u = k / max(1.0, L)
                    bx = cx + lean * (rb - s) + sgn * (wpx * 0.4 + k * math.cos(ang))
                    by = rb - k * math.sin(ang) + 0.004 * k * k
                    r = th0 * (1 - 0.7 * u)
                    body |= (((xx - bx) ** 2 + (yy - by) ** 2) < r * r + 0.3) & (yy >= s + 3)
        dissolve = np.clip((yy - (h - 30 + (8 if not far else -10))) / 24.0, 0, 1) + far * 0.25
        # cylinder shading (dull light from the surface, a little from the right): dark left, lit right rim, a step
        # lighter just under the surface; vertical bark streaks one step darker
        rel = (xx - xc) / np.maximum(half, 1.0)
        trunk_px = np.abs(xx - xc) < half + rough
        k = np.where(rel < -0.5, 0, np.where(rel < 0.45, 1, 2))
        if not far and wpx > 8:
            k = np.where(rel > 0.8, 3, k)
        bark = (E.fbm(xx / 2.5, yy / 16.0, seed + 5) > 0.6) & (rel > -0.5)
        k = np.where(bark, np.maximum(k - 1, 0), k)
        k = np.where(trunk_px, k, 0)                                         # branches: the dark tone
        k = np.minimum(k, len(tones) - 1)
        lut = np.array([pal.index(c) for c in tones])
        m = body & (th >= dissolve)
        step = body & (th < dissolve * 1.6) & ~(th < dissolve)
        idx[m] = lut[k][m]
        idx[step & m] = pal.index(MURK[2])
        # its mirror image above the surface row: dimmer, wobbling on the ripples, thinning out upwards
        Lr = min(0.9 * (h - s), 44.0)
        si = int(math.floor(s))
        for r in range(max(0, si - int(Lr)), si):
            src = 2 * si - 1 - r
            if not (0 <= src < h):
                continue
            q = (si - r) / Lr
            wob = int(round(2.0 * math.sin(r / 2.2 + seed) * min(1.0, q * 3)))
            row = np.roll(body[src] & (th[src] >= dissolve[src]), wob)
            keep = row & (thd[r] > q * 1.15 - 0.15) & (idx[r] < 0)
            shade = np.roll((xx[0] - xc[src]) / np.maximum(half[src], 1.0) > 0.3, wob)
            idx[r, keep] = pal.index(REFL[0])
            idx[r, keep & shade & (q < 0.5)] = pal.index(REFL[1])
    # root curtains: thin roots hanging from the surface (aerial roots of the trees above, runners of floating
    # mats), each entering the water at the surface row of its distance with a short dim mirror image above
    for cx, d, n, spread in ((250, 3.3, 9, 1.4), (520, 3.6, 8, 1.2), (316, 5.2, 6, 1.0), (440, 6.4, 5, 0.9),
                             (380, 9.0, 4, 0.8), (150, 4.2, 8, 1.3), (60, 3.4, 7, 1.2), (620, 4.8, 7, 1.1),
                             (700, 3.2, 8, 1.3), (190, 8.0, 4, 0.8), (580, 9.5, 4, 0.8)):
        s0 = surf_row(d)
        for j in range(n):
            x0 = cx + rng.uniform(-0.5, 0.5) * spread * F / d
            L = F * rng.uniform(0.3, 1.0) / d
            ph = rng.uniform(0, 6.28)
            thick = 2 if (d < 3.8 and j % 3 == 0) else 1
            y0 = int(math.floor(s0))
            for k in range(int(L)):
                u = k / max(1.0, L)
                y = y0 + k
                x = int(round(x0 + 1.4 * math.sin(k / 7.0 + ph) * u))
                if not (0 <= y < h and 0 <= x < w) or th[y, x] <= max(0.0, (u - 0.6) / 0.4):
                    continue
                for q in range(thick):
                    if x + q < w and idx[y, x + q] < 0:
                        idx[y, x + q] = pal.index(ROOT[2] if k < 3 else ROOT[1])
                ry = y0 - 1 - k                                     # the mirror image (short, dim)
                if k < L * 0.35 and 0 <= ry < h and idx[ry, x] < 0 and th[ry, x] > u / 0.35:
                    idx[ry, x] = pal.index(REFL[0])
    return R.to_rgba(idx, pal)


# ============================================================================ uw_swamp_floor (768x120)
def make_floor():
    h, w = 120, 768
    rows = np.arange(h)[:, None] + 0.5
    cols = np.arange(w)[None, :] + 0.5
    dist = FLOOR_D * 177.0 / (rows + 1.6)
    xw = (cols - w / 2) * dist / 177.0
    th = R.bayer(h, w)
    pal = R.Pal(LITTER)
    patch = E.fbm(xw / 1.6, dist / 1.6, 5.0)
    tone = 1.2 + (patch - 0.5) * 1.6 + np.clip((6.0 - dist) / 3.0, 0, 1) * 0.8
    thr = 0.5 + (th - 0.5) * 0.45
    k = np.clip(np.floor(tone + thr - 0.5), 0, 3).astype(int)
    rng = random.Random(14)
    for _ in range(70):                                  # leaves / twigs of the litter, projected
        d = math.exp(rng.uniform(math.log(3.1), math.log(9.0)))
        x = rng.uniform(-2.2, 2.2) * d
        s = rng.uniform(0.05, 0.1)
        rx = 177.0 * s / d
        if rx < 0.9:
            continue
        cy = FLOOR_D * 177.0 / d - 1.6
        cx = w / 2 + 177.0 * x / d
        sq = FLOOR_D / d * 1.3
        a = rng.uniform(0, math.pi)
        y0, y1 = int(max(0, cy - rx - 2)), int(min(h, cy + rx + 2))
        x0, x1 = int(max(0, cx - rx - 2)), int(min(w, cx + rx + 2))
        if y1 <= y0 or x1 <= x0:
            continue
        yy = np.arange(y0, y1)[:, None] + 0.5
        xx = np.arange(x0, x1)[None, :] + 0.5
        u = ((xx - cx) * math.cos(a) + (yy - cy) / sq * math.sin(a)) / rx
        v = (-(xx - cx) * math.sin(a) + (yy - cy) / sq * math.cos(a)) / (rx * 0.42)
        body = (np.abs(v) < (1 - np.abs(u) ** 1.5)) & (np.abs(u) < 1)
        sub = k[y0:y1, x0:x1]
        sub[body] = 3 if rng.random() < 0.5 else 0
    idx = np.array([pal.index(c) for c in LITTER])[k]
    img = R.to_rgba(idx, pal)
    # dissolving fast: only the near rows stay (the murk eats everything past a few metres), the nearest fade too
    fade = np.clip((70.0 - rows) / 40.0, 0, 1) + np.clip((rows - (h - 14)) / 14.0, 0, 1)
    img[np.broadcast_to(fade > th, (h, w))] = 0.0
    return img


# ============================================================================ uw_swamp_fore (256x128)
def make_fore():
    h, w = 128, 256
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    th = R.bayer(h, w)
    img = E.blank(h, w)
    body = np.zeros((h, w), bool)
    # a thick gnarled root arching from the left edge down to the bottom, plus two rootlets
    for (x0, y0, x1, y1, bulge, r0, r1, seed) in ((-10, 30, 210, 132, 44, 20, 9, 1.0), (40, 80, 120, 134, 10, 9, 4, 2.0),
                                                  (-6, 86, 60, 40, -14, 7, 3, 3.0)):
        n = 60
        for i in range(n + 1):
            t = i / n
            cx = x0 + (x1 - x0) * t
            cy = y0 + (y1 - y0) * t ** 1.6 - bulge * math.sin(math.pi * t)
            r = r0 + (r1 - r0) * t
            r *= 1 + 0.18 * math.sin(t * 17 + seed) + 0.12 * math.sin(t * 41 + seed * 3)      # knobby
            body |= (xx - cx) ** 2 + (yy - cy) ** 2 < r * r
    top = body & ~R.shift(body, 3, 0, False)
    E.put(img, body, FORE[1])
    E.put(img, body & (E.fbm(xx / 18.0, yy / 18.0, 6.0) > 0.58), FORE[0])
    E.put(img, top & (E.fbm(xx / 14.0, yy / 6.0, 2.0) > 0.4), FORE[2])
    E.put(img, body & ~R.shift(body, 1, 0, False) & (xx > 30), FORE[3])
    edge = R.dilate(body) & ~body & (th < 0.5)
    E.put(img, edge, FORE[1])
    return img


# ============================================================================ the set
def layers():
    return {"uw_swamp_bg": make_bg(), "uw_swamp_ceiling": make_ceiling(), "uw_swamp_floor": make_floor(),
            "uw_swamp_fore": make_fore(), "uw_swamp_mid": make_mid()}


FIREFLY = ["..s..", ".sBs.", "sBHBs", ".sBs.", "..s.."]      # a glowing firefly dot


def frame():
    """enc_frame_swamp: dark gnarled root wood, #d8e070 inner line, firefly corner studs."""
    img = E.make_frame(FRAME, [FRAME["fly"]] * 4, stud=FIREFLY)
    # knots in the corner blocks only (the stretched edge segments must stay uniform for 9-slicing)
    for (y, x) in ((5, 1), (1, 5), (18, 1), (22, 5), (1, 18), (5, 22), (22, 18), (18, 22)):
        img[y, x, :3] = E.rgb(FRAME["ink"])
    return img
