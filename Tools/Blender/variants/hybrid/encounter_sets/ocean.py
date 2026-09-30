"""
Encounter set OCEAN (청새치 blue_marlin + 백상아리 great_white, one shared lurk; Docs/legends_rollout.md 4.5): deep
cobalt open water, no bottom. The kona runs 1.2 m under the surface and the camera sits 0.7 m below it looking up
(frame_at (0.36, 0.62)), so the upper part of the window is the UNDERSIDE of the surface (a bright blue-white Snell band
with wave facets, the angler's boat hull as a dark shadow up-left where the line comes from, its wake of bubbles);
below the lure the blue falls away into the depth fog the legends rise out of. Built by hyb_encounter.py (see
encounter_sets/README.md).

Rest camera (rollout 4.1): set (-1.3, -0.7, -3.0), the lure at the origin, the surface at y +1.2 (1.9 m above the
camera). The ceiling band is built so that its BOTTOM row is the 30 m point the code anchors it on ((cam.x, 1.2,
cam.z + 30)); the bg / mid "horizon" is the lure-depth point (cam.x, 0, cam.z + 30), ~7 px under the ceiling's edge.

  uw_ocean_bg.png       640x400 opaque  HORIZON 250: the overhead surface #8ad0f0 / #6ab0e0 / #4a90c8 with soft wave
                                        dashes (rows the ceiling layer does not reach: seen only when the camera looks
                                        up), #2a70b0 under the surface's far edge -> #1a5090 (the horizon) -> #0e3468 ->
                                        #081e44 -> #040e24 (the depth fog below), banded + Bayer; a luminous dash-dithered
                                        haze band at row HORIZON with faint distant baitfish specks; no terrain
  uw_ocean_ceiling.png  768x120 alpha   the surface seen from 1.9 m below, pseudo-perspective (top row = 2.6 m, BOTTOM
                                        row = the 30 m far edge): Snell band #8ad0f0 (overhead) -> #6ab0e0 -> #4a90c8 ->
                                        #3a80bc -> #2a70b0 (far), a brighter sun side up-left, a network of wave-facet
                                        net (#a8dcf4 / #d8f2ff near, dash-dithered lighter steps far); the BOAT HULL
                                        shadow #0a1a30 (keel line #06101f, lit bilge #132a4c, 1 px #bfe8ff meniscus)
                                        up-left at 3-5 m (at rest its stern half peeks into the top-left of the window
                                        where the line comes down; whole when the camera looks up) with a wake of
                                        bubbles trailing off to the far right; far rows step into the water and dissolve,
                                        the nearest rows dissolve into the bg's overhead surface
  uw_ocean_mid.png      768x200 alpha   open water: a distant swirling baitball (a hollow swirl of 2-3 px fish, core
                                        #1a4a80, body #245a98, flashing flanks #5a8ec8 / #b8dcf4) just above the horizon
                                        right of centre, a smaller fainter one far left, strays, sparse dark plankton
                                        specks; the feet dissolve into the haze band (bottom row = the lure-depth horizon)
  uw_ocean_fore.png     256x128 alpha   a large out-of-focus moon jelly for the bottom-left (#6aa0d0 at a 50 % dither =
                                        the "alpha 0.5" look, a lighter bell rim, the four gonad rings, fringe and frilly
                                        oral arms trailing off the bottom edge) - seen when the window goes full screen
  enc_frame_ocean.png    24x24          9-slice, 6 px border: navy porthole rim (ink #0a0e18, #1a2640 / #2a3a5a /
                                        #3a4e74), 1 px #7fd4ff inner line at px 3, brass rivet corner studs
  lures: kona (new; frame 1 = the skirt flares), jig (the kit's tweak)
"""
import math
import random
import numpy as np
from mathutils import Vector, Matrix
import bmesh
import bpy
import hyb_core as R
import fk_common as C
import hyb_encounter_kit as E

V = Vector
SET = "ocean"
PRESET = "ocean"
LURES = ["kona", "jig"]                       # both ocean legends' key lures


# ------------------------------------------------------------------ lure frame-1 tweak (billboard 16x8)
def _pin_bounds(objs, box):
    """kit.lure_frames fits the camera to the mesh bounds: two loose (never rendered) vertices pin the frame-0 bounds so
    frame 1 is framed identically (the tweak stays inside them)."""
    x0, x1, z0, z1 = box
    ob = next(o for o in objs if o.type == "MESH" and o.name.startswith("Head"))
    inv = ob.matrix_world.inverted()
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for p in ((x0, 0.0, z0), (x1, 0.0, z1)):
        bm.verts.new(inv @ V(p))
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def _tweak_kona(objs):
    """Frame 1: the skirt flares - every strand swings outwards about the collar and pulls in shorter (an umbrella
    pulse), strongest on the outer strands; kept inside the frame-0 bounds."""
    box = C.world_bounds(objs)
    skirt = [o for o in objs if o.name.startswith("Skirt")]
    base = {o.name: o.matrix_world.copy() for o in skirt}
    piv = V((-0.2, 0.0, 0.0))
    for gain, k in ((30.0, 0.72), (24.0, 0.74), (18.0, 0.78), (12.0, 0.82)):
        for o in skirt:                                           # back to frame 0, then try this flare strength
            o.matrix_world = base[o.name]
        bpy.context.view_layer.update()
        for o in skirt:
            _, _, bz0, bz1 = C.world_bounds([o])
            ang = max(-20.0, min(20.0, (bz0 + bz1) / 2 * gain))
            M = (Matrix.Translation(piv) @ Matrix.Rotation(math.radians(ang), 4, "Y") @ Matrix.Scale(k, 4)
                 @ Matrix.Translation(-piv))
            o.matrix_world = M @ base[o.name]
        bpy.context.view_layer.update()
        nb = C.world_bounds(objs)
        if nb[0] >= box[0] - 1e-4 and nb[1] <= box[1] + 1e-4 and nb[2] >= box[2] - 1e-4 and nb[3] <= box[3] + 1e-4:
            break
    _pin_bounds(objs, box)


LURE_TWEAKS = {"kona": _tweak_kona}

# ------------------------------------------------------------------ palette (deep cobalt, sunrise light from above)
WATER = ["#2a70b0", "#1a5090", "#0e3468", "#081e44", "#040e24"]                 # up -> horizon -> depth fog
SPECK = ["#164680", "#3a78b8"]                                                   # distant baitfish: body, flash
SURF = ["#2a70b0", "#3a80bc", "#4a90c8", "#6ab0e0", "#8ad0f0"]                   # surface underside far -> overhead
WAVE = ["#a8dcf4", "#d8f2ff"]                                                    # wave-facet lines
HULL = ["#06101f", "#0a1a30", "#132a4c"]                                         # keel, hull, lit bilge
FOAM = ["#bfe8ff", "#f0fbff"]                                                    # meniscus / wake bubbles
BAIT = ["#1a4a80", "#245a98", "#5a8ec8", "#b8dcf4"]                              # baitball: core, body, flank, glint
BAIT_FAR = ["#1a4c88", "#1e5494"]
PLANK = ["#3a70a8"]
JELLY = ["#2e5a8a", "#4a7cb0", "#6aa0d0", "#9ac4e8", "#cfe6f8"]
FRAME = dict(ink="#0a0e18", dark="#1a2640", mid="#2a3a5a", lit="#3a4e74", line="#7fd4ff",
             brass=["#6a4a1a", "#c8962a", "#ffe08a"])
RIVET_STUD = [".sss.", "sHBBs", "sBBBd", "sBBdd", ".ddd."]                      # a round brass rivet (s/d, B, H)

# ------------------------------------------------------------------ geometry (rollout 4.1 ocean row)
F = 177.0
FAR = 30.0
CAM = (-1.3, -0.7, -3.0)      # rest camera (lure = origin)
SURF_Y = 1.2                  # the surface above the lure
CAM_D = SURF_Y - CAM[1]       # camera depth under the surface (1.9 m)
R0_CEIL = CAM_D * F / FAR     # ceiling band: bottom row offset over eye level (11.2 px)
R0_HZ = -CAM[1] * F / FAR     # the lure-depth horizon sits 4.1 px over eye level
SURF_GAP = int(round(R0_CEIL - R0_HZ))       # the surface's far edge sits ~7 px above the horizon row
BG_W, BG_H = 640, 400
HORIZON = 250                 # bg row on the projected lure-depth horizon (the haze band). High on the canvas: every
#                               row above the surface's far edge is surface (the ceiling covers 120 of them, the bg the
#                               rest when the camera looks up at the bite); 150 rows of depth fog below are plenty
REVIEW = dict(ray=("#c8ecff", 0.12 * 3, 380, 0), ceiling=(-64, HORIZON - SURF_GAP - 120), mid=(-64, HORIZON - 200),
              fore=(0, 400 - 128))

# ------------------------------------------------------------------ runtime look (the preview mock uses these; the
# EncounterSetDef row in the code carries the same values - Docs/legends_rollout.md 4.1)
PROFILE = dict(
    lure_at="mid",                            # the lure runs in open water, no floor
    lure_rest=(0.0, 0.0, 0.0), surface_y=SURF_Y, ceiling_y=SURF_Y,
    cam=CAM, frame_at=(0.36, 0.62),
    line_up=(-1.0, 1.2, -1.4),                # up to the boat
    drag_dir=(-0.6, 0.0, -0.8),
    clear="#081e44",
    rays=[("#c8ecff", 0.12, 150, -20), ("#c8ecff", 0.12, 236, -34), ("#c8ecff", 0.12, 318, -12)],   # 3 x uw_ray
    halo=None,                                # the kona has no halo (its bubble trail instead)
    snow_far="#4a80b8", snow_near="#b8e0ff",  # plankton
    line="#d0e8f8",                           # (no silt in open water: bubbles; "silt" left out on purpose)
    rim=("#bfe8ff", 0.45 * 0.62, [((0, 1), 1.0), ((-1, 1), 0.8), ((1, 1), 0.8)]),
    fore_x=-20,
    abyss="#081e44", fog_outline="#2a5a90", sun_mix=0.8, visibility=6.0,
    veil="#020a1a", veil_alpha=0.45,          # a plain hex (the marlin module mixes PROF["veil"] as a colour)
    frame_line="#7fd4ff",
)


# ============================================================================ uw_ocean_bg (640x400, opaque)
def make_bg():
    hz = HORIZON
    # above the horizon: the surface seen further overhead than the ceiling layer reaches (the Snell band, brightest at
    # the top; only shows when the camera looks up), then the water under the surface's far edge; below: the depth fog
    stops = [(0, SURF[4]), (hz - 132, SURF[4]), (hz - 104, SURF[3]), (hz - 66, SURF[2]), (hz - 34, WATER[0]),
             (hz - 2, WATER[1]), (hz + 36, WATER[2]), (hz + 100, WATER[3]), (hz + 140, WATER[4])]
    idx, pal = E.quant_rows(stops, BG_H, BG_W)
    img = R.to_rgba(idx, pal)
    rows = np.arange(BG_H)[:, None] + 0.5
    # the overhead surface (behind / above the ceiling layer): soft wave streaks, lighter and darker dashes
    cols = np.arange(BG_W)[None, :] + 0.5
    wav = E.fbm(cols / 26.0, rows / 5.0, 12.0)
    thd = R.dash_threshold(BG_H, BG_W, random.Random(8), 3, 14)
    zone = rows < hz - 112
    E.put(img, zone & (wav > 0.6) & (thd < (wav - 0.6) * 4.0), WAVE[0])
    E.put(img, zone & (wav < 0.36) & (thd < (0.36 - wav) * 3.0), SURF[3])
    # the horizon band: the brightest horizontal water, dash-dithered, a little above and below the horizon row
    band = np.exp(-((rows - (hz - 1)) / 7.0) ** 2) * 0.7
    hzm = (band > R.dash_threshold(BG_H, BG_W, random.Random(4), 3, 12)) & (rows > hz - 14) & (rows < hz + 12)
    E.put(img, hzm, WATER[0])
    # faint distant baitfish: loose elongated schools of 1-2 px specks in the band, a few flashing
    rng = random.Random(17)
    for (sx, sy, sw, sh, n) in ((70, hz + 3, 36, 4, 26), (205, hz - 4, 22, 3, 14), (360, hz + 6, 46, 5, 34),
                                (505, hz - 2, 28, 4, 20), (596, hz + 4, 18, 3, 10)):
        for _ in range(n):
            x = int(sx + rng.gauss(0, sw / 2.5))
            y = int(sy + rng.gauss(0, sh / 2.5))
            if 0 <= x < BG_W - 1 and 0 <= y < BG_H:
                flash = rng.random() < 0.12
                img[y, x, :3] = E.rgb(SPECK[1] if flash else SPECK[0])
                if not flash and rng.random() < 0.5:
                    img[y, x + 1, :3] = E.rgb(SPECK[0])
    img[..., 3] = 1.0
    return img


# ============================================================================ uw_ocean_ceiling (768x120) - the surface
CEIL_W, CEIL_H = 768, 120
HULL_C = (-2.9, 4.1)          # hull centre on the surface plane (x, distance), m: at rest its stern half peeks into
HULL_L, HULL_B = 5.4, 2.1     # the top-left of the window where the line comes down; whole when the camera looks up
HULL_A = math.radians(12.0)   # the bow points left and a little away


def _voronoi(xw, dw, cell, seed, jitter=0.85):
    """Jittered-grid Voronoi in world (x, d) -> label of the nearest seed (3x3 candidate cells)."""
    gx = np.floor(xw / cell)
    gd = np.floor(dw / cell)
    d1 = np.full(xw.shape, np.inf)
    lab = np.zeros(xw.shape, np.int64)
    for ox in (-1, 0, 1):
        for od in (-1, 0, 1):
            cx, cd = gx + ox, gd + od
            sx = (cx + 0.5 + (R._hash(cx * 17.0 + cd * 3.1, seed) - 0.5) * jitter) * cell
            sd = (cd + 0.5 + (R._hash(cx * 5.3 + cd * 29.0, seed + 7.0) - 0.5) * jitter) * cell
            dist = np.hypot(xw - sx, dw - sd)
            lid = (cx.astype(np.int64) + 5000) * 100003 + (cd.astype(np.int64) + 5000)
            closer = dist < d1
            lab = np.where(closer, lid, lab)
            d1 = np.where(closer, dist, d1)
    return lab


def make_ceiling():
    h, w = CEIL_H, CEIL_W
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    rows = yy[:, :1]
    d = E.ceiling_rows(h, CAM_D, F, R0_CEIL)              # m to the surface point of each row (top 2.6 m, bottom 30)
    dd = np.broadcast_to(d, (h, w))
    xw = (xx - w / 2) * dd / F
    th = R.bayer(h, w)
    thd = R.dash_threshold(h, w, random.Random(14), 2, 8)
    pal = R.Pal(SURF + WAVE + HULL + FOAM + WATER[:2])
    # base: the Snell band - brightest overhead (top rows), down to the water blue at the far edge (log distance)
    tone = np.interp(np.log(dd), np.log([2.6, 4.0, 8.0, 15.0, 26.0]), [4.4, 3.7, 2.6, 1.5, 0.4])
    tone = tone + (E.fbm(xw / 5.0, dd / 3.0, 2.0) - 0.5) * 0.9                 # wave groups: lighter / darker swathes
    tone = tone + 0.8 * np.exp(-((xw + 6.0) / 7.0) ** 2) * np.clip((10.0 - dd) / 6.0, 0, 1)   # the sun side (left)
    k = np.clip(np.floor(tone + 0.5 + (thd - 0.5) * 0.9), 0, 4).astype(int)
    lut = np.array([pal.index(c) for c in SURF])
    idx = lut[k]
    # wave facets: a warped cellular net (the borders of wave-sized Voronoi cells, stretched along the crests) - 1 px
    # light lines near, broken lighter dashes further out, gone in the far band
    wx = xw + (E.fbm(xw / 1.6, dd / 1.6, 5.0) - 0.5) * 1.1
    wd = dd + (E.fbm(xw / 1.6 + 9.0, dd / 1.6, 6.0) - 0.5) * 1.1
    lab = _voronoi(wx / 1.7, wd, 0.8, 21.0)
    lines = (lab != R.shift(lab, 0, -1, -1)) | (lab != R.shift(lab, -1, 0, -1))
    lines &= (R.shift(lab, 0, -1, -1) >= 0) & (R.shift(lab, -1, 0, -1) >= 0)
    lines &= th < np.clip((17.0 - dd) / 9.0, 0, 1) * np.clip((E.fbm(xw / 3.0, dd / 3.0, 44.0) - 0.28) / 0.2, 0, 1)
    far_l = lines & (dd >= 5.5) & (thd > 0.35)
    near_l = lines & (dd < 5.5) & (thd > 0.12)
    idx[far_l] = lut[np.minimum(k + 1, 4)][far_l]
    idx[near_l] = pal.index(WAVE[0])
    idx[near_l & (E.fbm(xw / 1.2, dd / 1.2, 41.0) > 0.64)] = pal.index(WAVE[1])
    # the boat hull (the angler's boat): an elongated shadow, pointed bow left, transom right, keel line
    ca, sa = math.cos(HULL_A), math.sin(HULL_A)
    rx, rd = xw - HULL_C[0], dd - HULL_C[1]
    u = (rx * ca + rd * sa) / (HULL_L / 2)
    v = (-rx * sa + rd * ca) / (HULL_B / 2)
    hw = np.where(u < 0, np.clip(1 - (-u) ** 1.7, 0, 1) ** 0.75, np.clip(1 - u ** 8, 0, 1) ** 0.5)
    hull = (np.abs(v) < hw) & (u > -1) & (u < 0.96)
    idx[hull] = pal.index(HULL[1])
    bilge = hull & (np.abs(v) > 0.7 * hw)
    idx[bilge & (th < 0.75)] = pal.index(HULL[2])
    keel = hull & ((v > 0) != R.shift(v > 0, -1, 0, False)) & (u < 0.9) & (u > -0.85)
    idx[keel] = pal.index(HULL[0])
    ring = R.dilate(hull) & ~hull
    idx[ring & (th < 0.8)] = pal.index(FOAM[0])
    idx[ring & (th < 0.12)] = pal.index(FOAM[1])
    # the wake: bubbles trailing from the transom, spreading and receding to the far right
    rng = random.Random(23)
    sx, sd = HULL_C[0] + ca * HULL_L / 2 * 0.95, HULL_C[1] + sa * HULL_L / 2 * 0.95
    dirx, dird = 0.84, 0.54
    for _ in range(420):
        s = rng.expovariate(1 / 4.5)
        if s > 18:
            continue
        spread = 0.18 + 0.08 * s
        lat = rng.gauss(0, spread)
        bx, bd = sx + dirx * s - dird * lat, sd + dird * s + dirx * lat
        if bd < 2.7:
            continue
        py = (h - 0.5 + R0_CEIL) - CAM_D * F / bd
        px = w / 2 + F * bx / bd
        yi, xi = int(py), int(px)
        if not (0 <= yi < h and 0 <= xi < w - 1) or rng.random() < s / 22.0:
            continue
        c = FOAM[1] if s < 3.0 else FOAM[0]
        idx[yi, xi] = pal.index(c)
        if s < 1.8 and rng.random() < 0.6:
            idx[yi, xi + 1] = pal.index(FOAM[0])
    # distance: step into the water blue (dash dithered); the far rows and the nearest rows dissolve
    haze = np.clip((dd - 15.0) / 12.0, 0, 1)
    idx[(haze * 1.5 > thd) & (haze < 0.7)] = pal.index(WATER[0])
    idx[(haze * 1.5 > thd) & (haze >= 0.7)] = pal.index(WATER[1])
    img = R.to_rgba(idx, pal)
    gone = np.clip((rows - (h - 10)) / 10.0, 0, 1) + np.clip((6.0 - rows) / 6.0, 0, 1)
    img[np.broadcast_to(gone > th, (h, w))] = 0.0
    return img


# ============================================================================ uw_ocean_mid (768x200) - open water
MID_W, MID_H = 768, 200


def _baitball(img, cx, cy, rx, ry, n, seed, cols, glints=True, shell=0.0):
    """A swirling ball of small fish seen from the side: 2-3 px horizontal dashes on circles around a vertical axis
    (front of the swirl lighter). shell 0 = dense at the core, 1 = the fish mill on a shell (a hollow swirl)."""
    rng = random.Random(seed)
    h, w = img.shape[:2]
    for _ in range(n):
        r = min(1.0, abs(rng.gauss(0, 0.5))) if rng.random() > shell else rng.uniform(0.62, 1.0)
        th = rng.uniform(0, 2 * math.pi)
        yv = rng.uniform(-1, 1) * math.sqrt(max(0.0, 1 - r * r * 0.6))
        x = cx + rx * r * math.cos(th) * (1 - 0.25 * abs(yv))
        y = cy + ry * yv
        front = math.sin(th) > 0
        ln = 3 if rng.random() < 0.4 else 2
        dirn = 1 if front else -1
        xi, yi = int(x), int(y)
        c = cols[1] if front else cols[0]
        if glints and front and rng.random() < 0.12:                  # flanks flashing as the ball turns
            c = cols[2]
        if glints and front and rng.random() < 0.025:
            c = cols[3]
        for j in range(ln):
            xx = xi + dirn * j
            if 0 <= xx < w and 0 <= yi < h:
                img[yi, xx, :3] = E.rgb(c)
                img[yi, xx, 3] = 1.0


def make_mid():
    h, w = MID_H, MID_W
    img = E.blank(h, w)
    rng = np.random.default_rng(3)
    rows = np.arange(h)[:, None] + 0.5
    # sparse plankton (static, dark specks only - they sit over the bright surface; the code adds the drifting flecks)
    sp = rng.random((h, w))
    E.put(img, (sp < 0.0022) & (rows < h - 12), PLANK[0])
    # the far, faint baitball on the left and a few strays
    _baitball(img, 148, 181, 13, 9, 150, 5, [BAIT_FAR[0], BAIT_FAR[1], BAIT_FAR[1], BAIT[2]], glints=False)
    # the main baitball, right of centre (the right third of the rest window), just above the horizon: a hollow swirl
    _baitball(img, 474, 172, 22, 15, 430, 7, BAIT, shell=0.75)
    _baitball(img, 474, 174, 11, 8, 90, 8, [BAIT[0], BAIT[0], BAIT[1], BAIT[2]])             # a looser core
    rr = random.Random(9)
    for _ in range(22):                                                                    # strays
        x, y = int(rr.gauss(474, 56)), int(rr.gauss(170, 14))
        if 0 <= x < w - 2 and 0 <= y < h - 8 and abs(x - 474) > 26:
            img[y, x:x + 2, :3] = E.rgb(BAIT[1])
            img[y, x:x + 2, 3] = 1.0
    # the feet dissolve into the horizon haze
    th = R.bayer(h, w)
    gone = np.broadcast_to(np.clip((rows - (h - 14)) / 12.0, 0, 1) > th, (h, w))
    img[gone] = 0.0
    return img


# ============================================================================ uw_ocean_fore (256x128) - the moon jelly
def make_fore():
    h, w = 128, 256
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    img = E.blank(h, w)
    th = R.bayer(h, w)
    cx, cy, a, b = 92.0, 50.0, 76.0, 38.0
    ang = math.radians(-16)                                   # the bell tilts: swimming up and to the right
    ca, sa = math.cos(ang), math.sin(ang)
    u = ((xx - cx) * ca + (yy - cy) * sa) / a
    v = (-(xx - cx) * sa + (yy - cy) * ca) / b
    blur = (E.fbm(xx / 5.0, yy / 5.0, 3.0) - 0.5) * 0.08     # out of focus: a soft, wobbling edge
    margin = 0.28 + 0.05 * np.cos(u * math.pi * 7.0)          # the scalloped bell margin
    bell = (u ** 2 + (v / 1.0) ** 2 < 1.0 + blur) & (v < margin)
    # oral arms: four frilly ribbons hanging from under the bell centre, trailing down-left off the bottom edge
    arms = np.zeros((h, w), bool)
    for k, (ox, sw, ln) in enumerate(((-0.28, 7.0, 1.6), (-0.08, 8.0, 1.9), (0.12, 7.0, 1.7), (0.32, 6.0, 1.4))):
        t = np.clip(v - margin + 0.1, 0, None) / ln                            # 0 at the margin, 1 at the arm's end
        centre = ox - 0.35 * t ** 1.2 + 0.05 * np.sin(t * 9.0 + k)
        wid = (sw / a) * (1.0 - 0.5 * t) * (1.0 + 0.35 * np.sin(t * 23.0 + k * 2))
        arms |= (np.abs(u - centre) < wid) & (v > margin - 0.1) & (t < 1.0)
    # fringe tentacles: short fine lines hanging from the margin
    fringe = np.zeros((h, w), bool)
    for uu in np.linspace(-0.95, 0.95, 26):
        t = (v - (0.28 + 0.05 * math.cos(uu * math.pi * 7.0)))
        fringe |= (np.abs(u - uu - 0.1 * np.clip(t, 0, 1)) < 0.6 / a) & (t > 0) & (t < 0.28 + 0.1 * math.sin(uu * 13))
    # the four gonads: horseshoes open towards the bell rim, seen at an angle (the two near ones bigger)
    gon = np.zeros((h, w), bool)
    gon_far = np.zeros((h, w), bool)
    for gu, gv, s, near in ((-0.44, -0.16, 1.1, True), (-0.14, -0.42, 0.8, False), (0.18, -0.42, 0.8, False),
                            (0.47, -0.14, 1.05, True)):
        e = ((u - gu) / (0.15 * s)) ** 2 + ((v - gv) / (0.21 * s)) ** 2
        phi = math.atan2(gv + 0.3, gu)                              # the opening faces away from the bell centre
        a = np.arctan2(v - gv, u - gu)
        opening = np.abs(np.angle(np.exp(1j * (a - phi)))) < math.radians(48)
        ring = (e < 1.0) & (e > 0.4) & ~opening
        if near:
            gon |= ring
        else:
            gon_far |= ring
    # the "alpha 0.5" look: bell, arms and fringe at a 50 % checker dither; the rim, rings and a subumbrella band solid-ish
    rim = bell & ~R.shift(bell, 1, 0, False) | (bell & ~R.shift(bell, 0, 1, False) & (u < 0)) \
        | (bell & ~R.shift(bell, 0, -1, False) & (u > 0))
    sub = bell & (v > margin - 0.12)
    E.put(img, bell & (th < 0.5), JELLY[2])
    E.put(img, arms & (th < 0.5), JELLY[1])
    E.put(img, arms & (th < 0.19) & (E.fbm(xx / 3.0, yy / 3.0, 9.0) > 0.55), JELLY[3])
    E.put(img, fringe & (th < 0.62), JELLY[2])
    E.put(img, sub & (th < 0.75), JELLY[3])
    E.put(img, gon_far & (th < 0.62), JELLY[3])
    E.put(img, gon & (th < 0.8), JELLY[3])
    E.put(img, gon & (th < 0.25), JELLY[4])
    E.put(img, rim & (th < 0.8), JELLY[3])
    E.put(img, rim & (v < -0.6) & (u < -0.1) & (th < 0.5), JELLY[4])       # the light catches the top of the bell
    return img


# ============================================================================ the set
def layers():
    """name -> top-down RGBA: every uw_ocean_* layer (no floor: the bottom is 18-20 m down)."""
    return {"uw_ocean_bg": make_bg(), "uw_ocean_ceiling": make_ceiling(), "uw_ocean_mid": make_mid(),
            "uw_ocean_fore": make_fore()}


def frame():
    """enc_frame_ocean: a navy porthole rim, #7fd4ff inner line, brass rivet studs."""
    br = FRAME["brass"]
    return E.make_frame(FRAME, (br, br, br, br), stud=RIVET_STUD)
