"""
Encounter set ICE (철갑상어 sturgeon; Docs/legends_rollout.md 4.4): near-black water under the lake ice. The only light
is the column falling from the fishing hole straight down onto the worm; the ice ceiling hangs over the upper part of
the window (cracks, a pressure ridge, bubbles frozen into it), the rocky bottom fills the lower part. Built by
hyb_encounter.py (see encounter_sets/README.md).

Rest camera (rollout 4.1): set (-1.2, 0.45, -2.7), the worm on the floor (y 0), the ice underside at y +4.3, so the
camera is 0.45 m above the floor and 3.85 m under the ice. Both pseudo-perspective bands are built so that their FAR
edge is the 30 m point the code anchors them on (floor top row / ceiling bottom row), not the true eye-level horizon.

  uw_ice_bg.png       640x400 opaque  HORIZON 200: plain dark ice #1a2640 on the top rows (seen only when the camera
                                      looks up past the ceiling layer), water #1a2438 (just under the ice) -> #141d30 ->
                                      #0e1628 -> #0b1222 (horizon) -> #080e1c -> #04080f (below, = the clear colour),
                                      banded + Bayer; far bottom-slope silhouettes (the
                                      lake bed climbing to the shore on the left, a ridge with boulders and a dead snag
                                      on the right) dissolving into a dash-dithered haze band at row HORIZON
  uw_ice_ceiling.png  768x120 alpha   the ice underside seen from 3.85 m below: top row = nearest (4.8 m), BOTTOM row =
                                      the 30 m far edge. Blue-grey ice #1a2640 .. #2a3a5a (darker snow-covered patches
                                      #141e34, lighter clear "black ice" #34486a), dash-dithered frost texture, a broken
                                      jagged crack network (light leaking through, #2e4466 / #5a80a8 near), one pressure
                                      ridge (a band of jumbled lighter plates #2a3a5a / #34486a with dark joints) across
                                      the near-middle distance, clusters of flat bubbles frozen into the ice (#8aaccc
                                      discs, #4a6688 far rim, #dff4ff glint; near only - no 1 px "stars"); the far rows
                                      step into the water colour and dissolve, the top 5 rows dissolve into the bg
  uw_ice_ray.png       64x256 alpha   THE HOLE AND ITS LIGHT COLUMN (not a god-ray): the hole as a bright ellipse 48x10
                                      (#bfe8ff rim, #ffffff core, a 1-2 px lit ice wall under it) on the top rows, a
                                      vertical column widening 48 -> 60 px and fading down (baked alpha 0.48 .. 0.14,
                                      Bayer steps, vertical light fingers, a few motes), a flat light pool on the last
                                      rows (where it lands on the floor). PRE-COLOURED: draw it untinted (white, alpha 1)
  uw_ice_mid.png      768x200 alpha   boulders, a sunken dead tree (trunk, root plate, branches), a standing snag and
                                      tufts of dead winter weed, rendered in 3D (Blender, palette-ramp materials lit from
                                      the ice above, ortho 8 px/m) -> quantised #0a1120 .. #24344e, 1 px #2e4262 top rim;
                                      bottom row = the floor horizon; short (the ice edge is only ~25 px above it)
  uw_ice_floor.png    768x120 alpha   gravel and silt #141c2a .. #2a3448 (camera 0.45 m above it): silt patches, gravel,
                                      a few bigger stones with lit tops and contact shadows, sunken twigs; far rows
                                      dissolve into the haze, the nearest rows into the dark
  uw_ice_fore.png     256x128 alpha   a dark out-of-focus rock for the bottom-left, faint cold rim on its upper right
  enc_frame_ice.png    24x24          9-slice, 6 px border: ice blocks (lit #dff4ff, mid #6a90b8, dark #2a4060),
                                      1 px #bfefff inner line at px 3, frost-crystal corner studs
  lures: softworm, jig (the kit's tweaks; identical to the files the other sets write)
"""
import math
import random
import numpy as np
from mathutils import Vector, noise as mnoise
import hyb_core as R
import fk_common as C
import hyb_encounter_kit as E

V = Vector
SET = "ice"
PRESET = "ice"
LURES = ["softworm", "jig"]                   # the sturgeon's key lures (the kit already has both frame-1 tweaks)
LURE_TWEAKS = {}

# ------------------------------------------------------------------ palette (under the ice, blue-grey, near black)
WATER = ["#1a2438", "#141d30", "#0e1628", "#0b1222", "#080e1c", "#04080f"]      # just under the ice -> deep
SLOPE = ["#0c1526", "#0a1020", "#16223a"]                                        # far bed, near bed, lit top edge
ICE = ["#101828", "#141e34", "#1a2640", "#22304c", "#2a3a5a", "#34486a"]          # ice underside dark -> clear
CRACK = ["#2e4466", "#5a80a8"]                                                   # light leaking through cracks
RIDGE = ["#0c1322", "#2e4466"]                                                   # pressure-ridge rubble, lit joints
BUB = ["#4a6688", "#8aaccc", "#dff4ff"]                                          # frozen bubbles: edge, disc, glint
HOLE = ["#7aa4c8", "#bfe8ff", "#ffffff"]                                         # hole: ice wall, rim, core
COLUMN = "#bfe8ff"                                                               # the light column (alpha baked)
ROCK = ["#0a1120", "#101a2c", "#18243a", "#24344e"]                              # mid boulders dark -> lit top
ROCK_FAR = ["#0c1426", "#111b2e"]                                                # far boulders (near the haze)
WOOD = ["#0a0f1a", "#121a28", "#1c283a"]                                         # sunken dead wood
WEED = ["#0c1620", "#13222e", "#1d3240"]                                         # dead winter weed
ROCK_HI = "#2e4262"                                                              # 1 px top rim (light from the ice)
SILT = ["#070b14", "#0c111e", "#141c2a", "#1a2334", "#222c40", "#2a3448"]         # shadow .. stone top
FORE = ["#020409", "#05080f", "#0a1220", "#141e32", "#2a3a5a"]
FRAME = dict(ink="#0a1424", dark="#2a4060", mid="#6a90b8", lit="#dff4ff", line="#bfefff",
             frost=["#6a90b8", "#bfefff", "#ffffff"])
FROST_STUD = ["..H..", ".sBs.", "HBHBH", ".sBs.", "..H.."]                      # a frost crystal (s/d, B, H)

# ------------------------------------------------------------------ geometry (rollout 4.1 ice row)
F = 177.0                    # the encounter's focal length (px)
FAR = 30.0                   # the code anchors the bands on the 30 m points (floor top row / ceiling bottom row)
CAM = (-1.2, 0.45, -2.7)     # rest camera (lure = origin, floor y = 0)
ICE_Y = 4.3                  # the ice underside (and the hole) above the floor
CAM_H = CAM[1]               # camera height above the floor
CEIL_D = ICE_Y - CAM_H       # camera depth under the ice (3.85 m)
R0_FLOOR = CAM_H * F / FAR   # floor band: top row offset under eye level (2.66 px)
R0_CEIL = CEIL_D * F / FAR   # ceiling band: bottom row offset over eye level (22.7 px)
SURF_GAP = int(round(R0_CEIL + R0_FLOOR))    # the ice's far edge sits ~25 px above the floor horizon
BG_W, BG_H = 640, 400
HORIZON = 200                # bg row on the projected floor horizon (haze band). Lower than the cave's 150: the rows
#                              above the ice's far edge carry a plain dark-ice tone for when the camera looks up past
#                              the ceiling layer; below, the floor covers 120 rows and the rest is the dark


def rest_projector(frame_at=(0.33, 0.30), cam=CAM):
    """Pinhole of the preview / EncounterView rest camera (window 288x136 at (96, 10) of the 480x270 view, principal
    point at the window centre, f 177): set-frame point -> top-down view px. Used for the ray's rest placement."""
    wx, wy, ww, wh = 96, 10, 288, 136
    pp = (wx + ww / 2, wy + wh / 2)
    c = V(cam)
    dx, dy = frame_at[0] * ww - ww / 2, frame_at[1] * wh - wh / 2
    to = -c
    yaw = math.atan2(to.x, to.z) - math.atan2(dx, F)
    pitch = math.atan2(to.y, math.hypot(to.x, to.z)) - math.atan2(dy, F)
    fwd = V((math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch)))
    right = V((0, 1, 0)).cross(fwd).normalized()          # Unity-like (left-handed) set frame
    up = fwd.cross(right).normalized()

    def proj(p):
        q = V(p) - c
        z = q.dot(fwd)
        return pp[0] + F * q.dot(right) / z, pp[1] - F * q.dot(up) / z
    return proj


_P = rest_projector()
HOLE_PX = _P((0.0, ICE_Y, 0.0))            # ~(191, -153): the hole is far above the window at rest
FLOOR_PX = _P((0.0, 0.0, 0.0))             # ~(191, 105): the column lands on the worm ~258 px lower (sprite 256)

HY_REST = _P((CAM[0], 0.0, CAM[2] + FAR))[1]   # ~81: the floor horizon row of the rest view

# review sheet: the ray pre-coloured (white, alpha 1) over the worm; layer offsets on the 640x400 canvas (view -> canvas:
# x + 80, y + HORIZON - HY_REST)
REVIEW = dict(ray=("#ffffff", 1.0, int(round(HOLE_PX[0] + 80 - 32)), int(round(HOLE_PX[1] + HORIZON - HY_REST))),
              ceiling=(-64, HORIZON - SURF_GAP - 120), mid=(-64, HORIZON - 200), floor=(-64, HORIZON),
              fore=(0, 400 - 128))

# ------------------------------------------------------------------ runtime look (the preview mock uses these; the
# EncounterSetDef row in the code carries the same values - Docs/legends_rollout.md 4.1)
PROFILE = dict(
    lure_at="floor",                          # the worm rests on the bottom (set y 0 = the floor)
    lure_rest=(0.0, 0.04, 0.0), floor_y=0.0,
    surface_y=ICE_Y, ceiling_y=ICE_Y,         # the ice underside (the ceiling layer's far edge: (cam.x, 4.3, cam.z+30))
    cam=CAM, frame_at=(0.33, 0.30),
    line_up=(0.0, ICE_Y, 0.0),                # the line goes straight up to the hole
    drag_dir=(-0.45, 0.0, -0.9),
    clear="#04080f",
    # uw_ice_ray, pre-coloured (tint white, alpha 1), top centre on the projected hole (0, 4.3, 0); x from the right
    # edge / y of the REST camera for the generic preview (the code anchors it every frame: ray_anchor)
    rays=[("#ffffff", 1.0, int(round(480 - (HOLE_PX[0] - 32))), int(round(HOLE_PX[1])))],
    ray_anchor=(0.0, ICE_Y, 0.0), ray_floor=(0.0, 0.0, 0.0),
    halo="#bfe8ff", halo_alpha=0.25,
    snow_far="#3a5a7a", snow_near="#cfe8ff",  # ice motes
    line="#d8e8f8", silt="#8aa0b8",
    rim=("#cfefff", 0.4 * 0.62, [((0, 1), 1.0)]),
    fore_x=-20,
    abyss="#04080f", fog_outline="#2a4a6a", sun_mix=0.6, key_dir=(0.0, 1.0, 0.0), light_up=1.2, visibility=3.0,
    veil=("#02040a", 0.5), frame_line="#bfefff",
    entry=(1.9, 3.0, 0.0),                    # the surface mock: the line goes into the hole (stage_ice holeX / holeZ)
)


# ============================================================================ helpers
def _thr(h, w, k=0.45, ox=0, oy=0):
    """Bayer threshold squeezed towards 0.5: narrow dithered transitions (the hybrid 'banded' look)."""
    return 0.5 + (R.bayer(h, w, ox, oy) - 0.5) * k


def _voronoi(xw, dw, cell, seed, jitter=0.85):
    """Jittered-grid Voronoi in world (x, d): -> (label of the nearest seed, F2 - F1 in metres)."""
    gx = np.floor(xw / cell)
    gd = np.floor(dw / cell)
    d1 = np.full(xw.shape, np.inf)
    d2 = np.full(xw.shape, np.inf)
    lab = np.zeros(xw.shape, np.int64)
    for ox in (-1, 0, 1):
        for od in (-1, 0, 1):
            cx, cd = gx + ox, gd + od
            sx = (cx + 0.5 + (R._hash(cx * 17.0 + cd * 3.1, seed) - 0.5) * jitter) * cell
            sd = (cd + 0.5 + (R._hash(cx * 5.3 + cd * 29.0, seed + 7.0) - 0.5) * jitter) * cell
            dist = np.hypot(xw - sx, dw - sd)
            lid = (cx.astype(np.int64) + 5000) * 100003 + (cd.astype(np.int64) + 5000)
            closer = dist < d1
            d2 = np.where(closer, d1, np.minimum(d2, dist))
            lab = np.where(closer, lid, lab)
            d1 = np.where(closer, dist, d1)
    return lab, d2 - d1


def _pair_hash(a, b, seed):
    lo, hi = np.minimum(a, b), np.maximum(a, b)
    return R._hash((lo % 99991) * 0.37 + (hi % 99989) * 0.113, seed)


# ============================================================================ uw_ice_bg (640x400, opaque)
def make_bg():
    hz = HORIZON
    # above: the ice itself further overhead than the ceiling layer reaches (only when the camera looks up), then the
    # water just under the ice's far edge, the horizon haze, the dark below
    stops = [(0, ICE[2]), (hz - 118, ICE[2]), (hz - 76, WATER[0]), (hz - 50, WATER[0]), (hz - 34, WATER[1]),
             (hz - 16, WATER[2]), (hz, WATER[3]), (hz + 18, WATER[4]), (hz + 65, WATER[5])]
    idx, pal = E.quant_rows(stops, BG_H, BG_W)
    img = R.to_rgba(idx, pal)
    th = R.bayer(BG_H, BG_W)
    rows = np.arange(BG_H)[:, None] + 0.5
    x = np.arange(BG_W)[None, :] + 0.5
    xs = x[0]
    rough = (E.fbm(xs / 11.0, np.zeros_like(xs), 4.0) - 0.5) * 4.0
    hz = HORIZON
    # far bed (faint, higher up): a long low swell across the middle
    far_top = hz - 6 - 5 * np.exp(-((xs - 330) / 90.0) ** 2) - 3 * np.sin(xs / 37.0) ** 2 + rough * 0.6
    # near bed, left: the lake bed climbing to the shore where it meets the ice (boulder humps on it)
    left = hz - 31 * np.clip(1 - xs / 250.0, 0, 1) ** 1.45 - 4.5 * np.exp(-((xs - 70) / 13.0) ** 2) \
        - 3.0 * np.exp(-((xs - 158) / 9.0) ** 2) - 2.2 * np.exp(-((xs - 205) / 7.0) ** 2) + rough
    # near bed, right: a ridge with boulders, rising to the shore at the right edge
    right = hz - 12 * np.exp(-((xs - 548) / 64.0) ** 2) - 7 * np.exp(-((xs - 468) / 22.0) ** 2) \
        - 5 * np.exp(-((xs - 590) / 10.0) ** 2) - 26 * np.clip((xs - 596) / 44.0, 0, 1) ** 1.3 + rough
    right = np.where(xs > 405, right, hz + 20)
    near_top = np.minimum(left, right)
    far = (rows > far_top[None, :]) & (rows < hz + 8)
    near = (rows > near_top[None, :]) & (rows < hz + 8)
    # a dead snag standing on the right ridge (trunk leaning left, three bare branches)
    snag = np.zeros((BG_H, BG_W), bool)
    base_x, base_y = 520.0, float(right[520]) + 1
    for t in np.linspace(0, 1, 60):
        cx, cy = base_x - 5 * t, base_y - 24 * t
        wdt = 1.6 - 1.0 * t
        yi = int(cy)
        snag[yi, int(cx - wdt):int(cx + wdt) + 1] = True
    for (t0, ang, ln) in ((0.45, 35, 10), (0.62, -40, 8), (0.8, 25, 6)):
        sx0, sy0 = base_x - 5 * t0, base_y - 24 * t0
        for s in np.linspace(0, 1, 16):
            px = int(round(sx0 + math.sin(math.radians(ang)) * ln * s))
            py = int(round(sy0 - math.cos(math.radians(ang)) * ln * s))
            snag[py, px] = True
    # bases dissolve into the haze band (dithered), far bed fainter
    fade_far = np.clip((rows - (hz - 9)) / 12.0, 0, 1)
    fade_near = np.clip((rows - (hz - 6)) / 14.0, 0, 1)
    E.put(img, far & (th >= fade_far) & ~near, SLOPE[0])
    nm = (near | snag) & (th >= fade_near)
    E.put(img, nm, SLOPE[1])
    # 1 px lit top edge where the bed faces up to the ice light (only well above the haze)
    top_edge = nm & ~R.shift(nm, 1, 0, False) & (rows < hz - 8) & ~snag
    E.put(img, top_edge & (R.bayer(BG_H, BG_W) < 0.75), SLOPE[2])
    # the haze band along the horizon: one step lighter water, dash-dithered, over the silhouettes' feet
    band = np.exp(-((rows - hz) / 8.0) ** 2) * 0.6
    hzm = (band > R.dash_threshold(BG_H, BG_W, random.Random(4), 3, 10)) & (rows < hz + 9)
    E.put(img, hzm, WATER[2])
    # a few dim specks of silt hanging in the far water (never near the horizon: the code draws the moving flecks)
    rng = np.random.default_rng(21)
    specks = (rng.random((BG_H, BG_W)) < 0.0016) & (rows > hz - 70) & (rows < hz - 10) & ~nm
    E.put(img, specks, WATER[1])
    img[..., 3] = 1.0
    return img


# ============================================================================ uw_ice_ceiling (768x120) - the ice underside
CEIL_W, CEIL_H = 768, 120


def make_ceiling():
    h, w = CEIL_H, CEIL_W
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    rows = yy[:, :1]
    d = E.ceiling_rows(h, CEIL_D, F, R0_CEIL)             # m to the ice point of each row (top 4.8 m, bottom 30 m)
    dd = np.broadcast_to(d, (h, w))
    xw = (xx - w / 2) * dd / F                             # world x of each pixel
    th = R.bayer(h, w)
    thd = R.dash_threshold(h, w, random.Random(12), 2, 7)
    pal = R.Pal(ICE + CRACK + RIDGE + BUB + WATER)
    # base tone: snow-covered (dark) vs clear (light) ice in big drifts, frost texture in dashes
    tone = np.full((h, w), 2.25)
    drift = E.fbm(xw / 5.5, dd / 4.0, 3.0)
    tone += (drift - 0.5) * 3.0
    clear = E.fbm(xw / 3.0 + 7.0, dd / 2.4, 9.0)
    tone += np.clip((clear - 0.58) / 0.08, 0, 1) * 1.4          # patches of clear "black ice" let more light in
    frost = E.fbm(xw / 0.45, dd / 0.45, 5.0)
    tone += (frost - 0.5) * 1.1
    tone -= np.clip((dd - 12.0) / 14.0, 0, 1) * 0.8             # a little dimmer with distance
    k = np.clip(np.floor(tone + 0.5 + (thd - 0.5) * 0.9), 0, 5).astype(int)          # dash-dithered steps
    idx = np.array([pal.index(c) for c in ICE])[k]
    # crack network: a jittered Voronoi in warped world space (jagged plate edges), broken - only some borders crack;
    # 1 px lines, dim (light leaking through), a few brighter near ones; they thin out into the distance
    wx = xw + (E.fbm(xw / 1.4, dd / 1.4, 23.0) - 0.5) * 1.3
    wd = dd + (E.fbm(xw / 1.4 + 5.0, dd / 1.4, 29.0) - 0.5) * 1.3
    lab, _ = _voronoi(wx, wd, 3.4, 3.0)
    lab_r = R.shift(lab, 0, -1, -1)
    lab_d = R.shift(lab, -1, 0, -1)
    edge_r = (lab != lab_r) & (lab_r >= 0)
    edge_d = (lab != lab_d) & (lab_d >= 0)
    crack = (edge_r & (_pair_hash(lab, lab_r, 1.0) < 0.45)) | (edge_d & (_pair_hash(lab, lab_d, 1.0) < 0.45))
    lab2, _ = _voronoi(wx, wd, 1.1, 11.0)                        # finer hairline cracks, near only
    l2r = R.shift(lab2, 0, -1, -1)
    crack |= ((lab2 != l2r) | (lab2 != R.shift(lab2, -1, 0, -1))) & (_pair_hash(lab2, l2r, 2.0) < 0.2) & (dd < 8.0)
    crack &= (th < np.clip((20.0 - dd) / 10.0, 0, 1)) & (thd > 0.15)
    bright = crack & (_pair_hash(lab, lab_r, 5.0) < 0.25) & (dd < 9.0)
    idx[crack] = pal.index(CRACK[0])
    idx[bright] = pal.index(CRACK[1])
    # one pressure ridge: a sinuous band of jumbled, snow-free broken plates across the near-middle distance (the top
    # of the rest window) - lighter ice blocks with dark joints between them and a dark far edge (not a silhouette)
    ridge_pts = [(-60.0, 15.5), (-30.0, 12.0), (-14.0, 10.2), (-2.0, 9.4), (9.0, 9.9), (24.0, 11.6), (44.0, 14.5),
                 (80.0, 19.0)]
    px_r = np.array([p[0] for p in ridge_pts])
    pd_r = np.array([p[1] for p in ridge_pts])
    ridge_d = np.interp(xw, px_r, pd_r) + (E.fbm(xw / 2.0, np.zeros_like(xw), 13.0) - 0.5) * 0.9
    bid = np.floor(xw / 0.6 + 2.0 * E.fbm(xw / 0.9, dd / 0.9, 19.0))            # rubble blocks along the ridge
    half = 0.36 + 0.3 * R._hash(bid, 17.0)                                       # each block its own height
    band = np.abs(dd - ridge_d) < half
    blk = R._hash(bid, 23.0)                                                     # per-block brightness
    idx[band] = pal.index(ICE[4])
    idx[band & (blk > 0.55) & (thd > 0.35)] = pal.index(ICE[5])
    idx[band & (blk < 0.2)] = pal.index(ICE[3])
    near_edge = band & ~R.shift(band, 1, 0, False)                     # the edge towards the camera (upper rows)
    far_edge = band & ~R.shift(band, -1, 0, False)                     # the far edge (lower rows)
    joint = band & (bid != R.shift(bid, 0, -1, 0.0))
    idx[joint | (far_edge & (th < 0.85))] = pal.index(RIDGE[0])
    idx[near_edge & ~joint & (th < 0.6)] = pal.index(CRACK[1])
    # bubbles frozen into the ice: clusters of flat discs (perspective-squashed), glint towards the camera
    rng = random.Random(31)
    for (cx, cd, n, spread) in ((-3.2, 6.0, 30, 1.0), (2.8, 7.2, 22, 0.8), (-8.0, 8.6, 22, 1.2), (6.5, 9.0, 16, 1.0),
                                (12.5, 7.2, 14, 1.0), (-14.0, 6.8, 16, 1.1), (0.5, 5.0, 12, 0.6), (-20.0, 9.5, 12, 1.4)):
        for _ in range(n):
            bd = cd + rng.gauss(0, spread * 0.6)
            bx = cx + rng.gauss(0, spread)
            if bd < 4.6:
                continue
            rad = math.exp(rng.uniform(math.log(0.03), math.log(0.12)))
            rx = F * rad / bd
            sq = CEIL_D / math.hypot(CEIL_D, bd)
            ry = rx * sq
            cy = (h - 0.5 + R0_CEIL) - CEIL_D * F / bd                 # the row of distance bd
            cxp = w / 2 + F * bx / bd
            if rx < 1.3:                                             # too small to read as a disc: skip (no stars)
                continue
            ry = max(0.6, ry)
            y0, y1 = int(max(0, cy - ry - 2)), int(min(h, cy + ry + 2))
            x0, x1 = int(max(0, cxp - rx - 2)), int(min(w, cxp + rx + 2))
            if y1 <= y0 or x1 <= x0:
                continue
            sy = np.arange(y0, y1)[:, None] + 0.5
            sx = np.arange(x0, x1)[None, :] + 0.5
            e = ((sx - cxp) / rx) ** 2 + ((sy - cy) / ry) ** 2
            disc = e < 1.0
            if not disc.any():
                continue
            sub = idx[y0:y1, x0:x1]
            sub[disc] = pal.index(BUB[1])
            rim = disc & R.dilate(~disc)                             # a darker rim on the far side (a flat disc)
            sub[rim & (sy > cy - 0.3 * ry)] = pal.index(BUB[0])
            glint = disc & (sy < cy) & (sx < cxp) & (e < 0.55)
            if glint.any():
                gy, gx = np.argwhere(glint)[0]
                sub[gy, gx] = pal.index(BUB[2])
    # distance: step into the water colour (dash dithered), then the last rows dissolve (the bg shows through)
    haze = np.clip((dd - 13.0) / 15.0, 0, 1) ** 1.2
    step = haze * 1.6 > thd
    idx[step & (haze < 0.75)] = pal.index(WATER[0])
    idx[step & (haze >= 0.75)] = pal.index(WATER[1])
    img = R.to_rgba(idx, pal)
    gone = np.broadcast_to(np.clip((rows - (h - 9)) / 9.0, 0, 1) + np.clip((5.0 - rows) / 5.0, 0, 1), (h, w)) > th
    img[gone] = 0.0                                              # far edge into the water, top rows into the bg's ice
    return img


# ============================================================================ uw_ice_ray (64x256) - the hole + column
def make_ray():
    h, w = 256, 64
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    cx = w / 2
    t = np.clip((yy - 8) / (h - 8), 0, 1)
    img = E.blank(h, w)
    th = R.bayer(h, w)
    # the column: 48 px under the hole, widening to ~60 px, fading down; a brighter core; vertical light fingers
    half = 23.0 + 7.0 * t
    dx = np.abs(xx - cx) / half
    inten = 0.48 * (1.0 - 0.62 * t) + 0.06 * np.exp(-((yy - (h - 8)) / 6.0) ** 2)
    fingers = R.dash_threshold(w, h, random.Random(7), 10, 40).T                   # vertical runs
    col_noise = R._hash(np.floor(xx / 3.0) * 1.0, 3.0)
    inten = inten * (0.82 + 0.36 * (fingers > 0.55) * (col_noise > 0.35))
    inten = inten * np.clip((1.0 - dx) / 0.28, 0, 1) * (1.0 + 0.25 * (dx < 0.4))   # soft edge, brighter core
    levels = [0.0, 0.14, 0.22, 0.32, 0.44]
    q = np.zeros((h, w))
    for i in range(1, len(levels)):
        lo, hi = levels[i - 1], levels[i]
        span = (inten - lo) / (hi - lo)
        q = np.where((inten >= lo) & (inten < hi), np.where(span > th, hi, lo), q)
    q = np.where(inten >= levels[-1], levels[-1] + 0.04, q)
    q[yy < 8] = 0.0
    img[..., :3] = E.rgb(COLUMN)
    img[..., 3] = q
    # the pool of light where it lands (last rows): a flat ellipse, one level up
    pool = (((xx - cx) / 29.0) ** 2 + ((yy - (h - 4.5)) / 4.0) ** 2) < 1.0
    img[pool, 3] = np.maximum(img[pool, 3], np.where(th[pool] < 0.6, 0.32, 0.22))
    # motes glinting in the column
    rng = np.random.default_rng(5)
    motes = (dx < 0.85) & (rng.random((h, w)) < 0.006) & (yy > 14) & (yy < h - 10)
    img[motes, :3] = E.rgb(HOLE[2])
    img[motes, 3] = 0.85
    # the hole: a bright 48x10 ellipse (rim #bfe8ff, core #ffffff) with the lit ice wall just under its near half
    e = ((xx - cx) / 24.0) ** 2 + ((yy - 4.5) / 5.0) ** 2
    core = ((xx - cx) / 19.5) ** 2 + ((yy - 4.2) / 3.3) ** 2
    hole = e < 1.0
    wall = (((xx - cx) / 24.0) ** 2 + ((yy - 6.2) / 5.0) ** 2 < 1.0) & ~hole & (yy > 4.5)
    img[wall, :3] = E.rgb(HOLE[0])
    img[wall, 3] = 1.0
    img[hole, :3] = E.rgb(HOLE[1])
    img[hole, 3] = 1.0
    img[core < 1.0, :3] = E.rgb(HOLE[2])
    return img


# ============================================================================ uw_ice_mid (768x200) - 3D render
MID_W, MID_H, MID_PPU = 768, 200, 8.0     # 96 m x 25 m on the ortho camera


def boulder(name, x, y, r, h, seed, mat, kind="rock"):
    """A rounded, knobbly boulder half sunk into the bed (flat-shaded ellipsoid, noise-displaced)."""
    ob = R.ellipsoid(name, (x, y, h * 0.35), (r, r * 0.8, h), mat, 14, 8)
    for v in ob.data.vertices:
        p = v.co
        n = mnoise.noise(V((p.x * 1.7 + seed, p.y * 1.7, p.z * 1.7))) * 0.22
        p.x *= 1 + n
        p.y *= 1 + n
        p.z *= 1 + n * 0.8
        if p.z < -0.35:
            p.z = -0.35
    ob.data.update()
    C.set_smooth(ob, False)
    return R.tagk(ob, kind)


def branch(name, pts, r0, r1, mat, kind="wood", segs=6):
    n = len(pts)
    rads = [r0 + (r1 - r0) * i / max(1, n - 1) for i in range(n)]
    return R.tagk(R.loft(name, pts, rads, mat, segs=segs, smooth=False), kind)


def weed_tuft(name, x, y, n, hgt, seed, mat):
    rng = random.Random(seed)
    for i in range(n):
        bx = x + rng.uniform(-0.8, 0.8)
        hh = hgt * rng.uniform(0.55, 1.0)
        lean = rng.uniform(-0.5, 0.5)
        pts = [(bx + lean * hh * (k / 5.0) ** 1.4 + 0.12 * math.sin(k * 1.3 + seed), y, hh * k / 5.0) for k in range(6)]
        branch(f"{name}{i}", pts, 0.1, 0.06, mat, "weed", segs=4)


def make_mid():
    C.reset_scene()
    R.reset_materials()
    R.use_preset("ice")
    L = V((0.25, -0.3, 0.92)).normalized()          # from above (the ice), a little towards the camera
    rock = R.m_tone(ROCK, [0.52, 0.74, 0.92], soft=0.02, light=L, noise=0.04, nscale=0.6, ncoord="world", name="Rock")
    rock_far = R.m_tone(ROCK_FAR, [0.8], soft=0.03, light=L, name="RockFar")
    wood = R.m_tone(WOOD, [0.6, 0.86], soft=0.02, light=L, noise=0.03, nscale=1.4, ncoord="world", name="Wood")
    weed = R.m_tone(WEED, [0.55, 0.85], light=L, name="Weed")
    # far boulders (behind, close to the water colour)
    for i, (x, r, hh) in enumerate(((-44.0, 2.4, 1.2), (-26.0, 1.6, 0.8), (4.0, 2.0, 0.9), (21.0, 1.4, 0.7),
                                    (39.0, 2.6, 1.3))):
        boulder(f"BF{i}", x, 14.0, r, hh, 40 + i, rock_far, "far")
    # near boulders: big ones at the sides, small ones near the middle (the column and the worm stay clear)
    for i, (x, r, hh) in enumerate(((-41.0, 3.2, 1.7), (-36.5, 1.6, 0.9), (-21.5, 1.8, 1.0), (-12.0, 0.9, 0.5),
                                    (-3.5, 0.6, 0.3), (8.5, 0.8, 0.45), (15.0, 1.9, 1.2), (29.0, 2.6, 1.5),
                                    (33.5, 1.3, 0.8), (44.5, 2.2, 1.3))):
        boulder(f"B{i}", x, 2.0 + (i % 3) * 1.5, r, hh, 60 + i, rock)
    # a sunken dead tree lying on the bed (trunk, a root plate, bare branches reaching up)
    branch("Trunk", [(17.0, 3.0, 0.45), (22.0, 3.2, 0.55), (27.0, 3.4, 0.6), (31.5, 3.6, 0.55)], 0.55, 0.3, wood)
    for k in range(7):                                                # root plate at the thick end
        a = math.radians(-70 + k * 23)
        branch(f"Root{k}", [(16.8, 3.0, 0.5), (16.8 - 0.4 * abs(math.cos(a)), 3.0, 0.5 + 1.4 * math.sin(a) + 0.3),
                            (16.3 - 0.9 * abs(math.cos(a)), 3.0, 0.5 + 2.0 * math.sin(a) + 0.4)], 0.22, 0.08, wood)
    for k, (x0, ln, ang) in enumerate(((22.5, 2.1, 60), (25.0, 2.6, 105), (28.0, 1.8, 70), (30.0, 1.5, 120))):
        a = math.radians(ang)
        p0 = (x0, 3.3, 0.8)
        p1 = (x0 + ln * 0.5 * math.cos(a), 3.3, 0.8 + ln * 0.55 * math.sin(a))
        p2 = (x0 + ln * math.cos(a) + 0.3, 3.3, 0.8 + ln * math.sin(a))
        branch(f"Br{k}", [p0, p1, p2], 0.2, 0.07, wood)
        branch(f"Tw{k}", [p1, (p1[0] + 0.7 * math.cos(a - 0.9), 3.3, p1[2] + 0.8)], 0.08, 0.05, wood)
    # a standing snag on the left (a dead trunk sticking up out of the bed)
    branch("Snag", [(-30.0, 4.0, -0.2), (-30.3, 4.0, 1.4), (-30.9, 4.0, 2.6), (-31.2, 4.0, 3.1)], 0.3, 0.12, wood)
    branch("SnagB", [(-30.5, 4.0, 1.9), (-29.6, 4.0, 2.4), (-29.0, 4.0, 2.7)], 0.1, 0.06, wood)
    # tufts of dead winter weed
    weed_tuft("WA", -16.5, 1.0, 6, 1.3, 1, weed)
    weed_tuft("WB", 5.5, 1.5, 5, 1.0, 2, weed)
    weed_tuft("WC", 38.0, 1.0, 7, 1.5, 3, weed)
    weed_tuft("WD", -45.0, 1.2, 5, 1.1, 4, weed)
    C.ortho_camera(0.0, MID_H / MID_PPU / 2, MID_W, MID_H, MID_PPU)
    ps = R.render_passes("mid")
    kinds = R.kind_map(ps)
    pal = R.Pal(ROCK + ROCK_FAR + WOOD + WEED + [ROCK_HI] + WATER)
    idx, _ = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    rows = np.arange(MID_H)[:, None] + 0.5
    solid = idx >= 0
    far = (kinds == "far") & solid
    near = solid & ~far
    # 1 px rim on the near objects' top edges (the diffuse light from the ice), broken
    top = near & ~R.shift(near, 1, 0, False)
    idx[top & (R.bayer(MID_H, MID_W) < 0.7)] = pal.index(ROCK_HI)
    # haze: the far objects take one step towards the water; everything's feet dissolve into the haze band
    thd = R.dash_threshold(MID_H, MID_W, random.Random(2), 2, 7)
    idx[far & (thd < 0.55)] = pal.index(WATER[2])
    feet = np.broadcast_to(np.clip((rows - (MID_H - 7)) / 7.0, 0, 1), idx.shape)
    idx[solid & (feet * 1.4 > thd)] = pal.index(WATER[3])
    idx[solid & (feet > thd + 0.35)] = -1
    return R.to_rgba(idx, pal)


# ============================================================================ uw_ice_floor (768x120) - pseudo-perspective
FL_W, FL_H = 768, 120


def make_floor():
    h, w = FL_H, FL_W
    rows = np.arange(h)[:, None] + 0.5
    cols = np.arange(w)[None, :] + 0.5
    dist = CAM_H * F / (rows + R0_FLOOR)                              # metres to the floor point of each row
    xw = (cols - w / 2) * dist / F
    th = R.bayer(h, w)
    # base: dark silt, lighter patches mid-distance, darker near (outside any light) and far (haze)
    near_k = np.clip((1.6 - dist) / 1.0, 0, 1)
    far_k = np.clip((dist - 4.5) / 8.0, 0, 1)
    tone = np.full((h, w), 2.6)
    tone -= near_k * 1.6 + far_k * 1.1
    patch = E.fbm(xw / 1.2, dist / 1.2, 2.0)
    tone += (patch - 0.5) * 1.6
    grit = E.fbm(xw / 0.12, dist / 0.12, 4.0)                          # gravel grain (fades with distance)
    tone += (grit - 0.5) * 1.2 * np.clip((5.0 - dist) / 3.0, 0, 1)
    k = np.clip(np.floor(tone + th - 0.5), 1, 4).astype(int)
    pal = R.Pal(SILT + WATER)
    lut = np.array([pal.index(c) for c in SILT])
    # stones (gravel + a few bigger ones): world-placed, projected; lit top edge, contact shadow under
    rng = random.Random(9)
    stones = []
    for _ in range(260):
        d = math.exp(rng.uniform(math.log(0.7), math.log(10.0)))
        x = rng.uniform(-2.3, 2.3) * d
        s = rng.uniform(0.012, 0.04) * (2.8 if rng.random() < 0.07 else 1.0)
        stones.append((d, x, s, rng.uniform(0, 6.28)))
    for d, x, s, ph in sorted(stones, key=lambda q: -q[0]):
        rx = F * s / d
        if rx < 0.9:
            continue
        cy = CAM_H * F / d - R0_FLOOR
        cx = w / 2 + F * x / d
        ry = max(0.8, rx * 0.6)
        y0, y1 = int(max(0, cy - ry * 1.6 - 2)), int(min(h, cy + ry + 3))
        x0, x1 = int(max(0, cx - rx * 1.5 - 2)), int(min(w, cx + rx * 1.5 + 3))
        if y1 <= y0 or x1 <= x0:
            continue
        yy = np.arange(y0, y1)[:, None] + 0.5
        xx = np.arange(x0, x1)[None, :] + 0.5
        dy = (yy - cy) / np.where(yy > cy, ry * 0.55, ry)
        dx = (xx - cx) / rx
        ang = np.arctan2(dy, dx)
        wob = 1 + 0.2 * np.sin(ang * 3 + ph) + 0.1 * np.sin(ang * 5 + 2 * ph)
        body = dx ** 2 + dy ** 2 < wob ** 2
        if not body.any():
            continue
        sub = k[y0:y1, x0:x1]
        dark = d < 1.2 or d > 7.0
        top = body & ~R.shift(body, 1, 0, False)
        below = R.shift(body, 1, 0, False) & ~body
        sub[below] = 0
        sub[body] = 1 if dark else 2
        sub[top] = (3 if dark else 4) if rx > 1.8 else 3
        if rx > 3.0:
            top2 = body & R.shift(top, 1, 0, False) & (R.bayer(y1 - y0, x1 - x0, x0, y0) < 0.5)
            sub[top2] = 3
    # sunken twigs lying on the silt (thin dark lines in perspective)
    for _ in range(14):
        d = math.exp(rng.uniform(math.log(0.9), math.log(6.0)))
        x = rng.uniform(-2.0, 2.0) * d
        ln = rng.uniform(0.15, 0.5)
        a = rng.uniform(0, math.pi)
        n = 24
        for j in range(n):
            u = (j / (n - 1) - 0.5) * ln
            wx, wd = x + math.cos(a) * u, d + math.sin(a) * u * 0.6
            if wd <= 0.3:
                continue
            py = int(CAM_H * F / wd - R0_FLOOR)
            px = int(w / 2 + F * wx / wd)
            if 0 <= py < h and 0 <= px < w:
                k[py, px] = 0
    idx = lut[np.clip(k, 0, 5)]
    # a few pale glints (a shell, a quartz pebble) in the mid distance
    for _ in range(10):
        d = rng.uniform(1.4, 5.0)
        x = rng.uniform(-2.0, 2.0) * d
        cy, cx = int(CAM_H * F / d - R0_FLOOR), int(w / 2 + F * x / d)
        if 0 <= cy < h and 0 <= cx < w:
            idx[cy, cx] = pal.index(SILT[5])
    img = R.to_rgba(idx, pal)
    fade = np.clip((8.0 - rows) / 8.0, 0, 1) + np.clip((rows - (h - 18)) / 18.0, 0, 1)
    gone = np.broadcast_to(fade > th, (h, w))
    img[gone] = 0.0
    return img


# ============================================================================ uw_ice_fore (256x128)
def make_fore():
    h, w = 128, 256
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    x = xx / w
    crest = h - (104 * np.exp(-((x - 0.12) / 0.28) ** 2) + 46 * np.exp(-((x - 0.5) / 0.18) ** 2) * (x < 0.72)
                 + 16 * (1 - x)) * (1 - np.clip((x - 0.78) / 0.12, 0, 1))
    crest += (E.fbm(xx / 20.0, np.zeros_like(xx), 6.0) - 0.5) * 14
    body = yy > crest
    th = R.bayer(h, w)
    depth_in = yy - crest
    img = E.blank(h, w)
    E.put(img, body, FORE[1])
    E.put(img, body & (E.fbm(xx / 28.0, yy / 28.0, 3.0) > 0.57), FORE[0])
    E.put(img, body & (depth_in < 6) & (xx > 26), FORE[2])
    E.put(img, body & (depth_in < 2.5) & (xx > 64) & (xx < 205), FORE[3])
    edge = (yy > crest - 2) & ~body & (th < 0.5)
    E.put(img, edge, FORE[2])
    rim = body & (depth_in < 1.0) & (xx > 118) & (xx < 196)             # the column's cold light on the wet edge
    E.put(img, rim, FORE[4])
    for cx, cy, r in ((142, 102, 2.0), (182, 116, 1.5)):                # two soft ice-light specks
        m = ((xx - cx) ** 2 + (yy - cy) ** 2 < r * r) & body
        E.put(img, m, FORE[3])
    return img


# ============================================================================ the set
def layers():
    """name -> top-down RGBA: every uw_ice_* layer."""
    return {"uw_ice_bg": make_bg(), "uw_ice_ceiling": make_ceiling(), "uw_ice_ray": make_ray(),
            "uw_ice_floor": make_floor(), "uw_ice_fore": make_fore(), "uw_ice_mid": make_mid()}


def frame():
    """enc_frame_ice: ice blocks (lit #dff4ff top row, #6a90b8, #2a4060), #bfefff inner line, frost-crystal studs."""
    fr = FRAME["frost"]
    return E.make_frame(FRAME, (fr, fr, fr, fr), stud=FROST_STUD)
