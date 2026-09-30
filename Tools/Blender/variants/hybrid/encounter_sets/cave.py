"""
Encounter set CAVE (the coelacanth; spec 3.2): the pixel-art layers of the back camera (layer 27, orthographic, 1:1
pixels) and the window frame. Cave preset palette (crystals #6ad8ff / #c89cff, dark violet rock, deep water #04101a).
Built by hyb_encounter.py (see encounter_sets/README.md).

  uw_cave_bg.png      640x400 opaque  abyss gradient #0b2230 (top) -> #04101a (bottom), banded + Bayer, far rock arches
                                      #0e2a38 dissolving into a haze band around the floor horizon (canvas row ~150)
  uw_cave_mid.png     768x200 alpha   pillars (hourglass columns) + crystal clusters, rendered in 3D (Blender, palette
                                      ramp materials, ortho) -> quantised; bottom row = the floor horizon; 1 px #6affea
                                      twinkles on the crystals; the upper parts dissolve into the water
  uw_cave_floor.png   768x120 alpha   silt floor in pseudo-perspective (camera 0.45 m above the floor, f 177 px):
                                      top row = the horizon (dissolves into the haze), ripple dashes and stones that
                                      get smaller towards the top, the nearest rows dissolve into the dark
  uw_cave_fore.png    256x128 alpha   out-of-focus rock edge for the bottom-left (dark, dithered edge, faint cyan rim)
  enc_frame_cave.png   24x24          9-slice, 6 px border (3 px stone, 1 px #6affea inner line at px 3, crystal studs)
  lures: egi, softworm, jig (lure_<key>_0/1)
"""
import math
import random
import bmesh
import numpy as np
from mathutils import Vector, Matrix, noise as mnoise
import hyb_core as R
import fk_common as C
import hyb_encounter_kit as E

V = Vector
SET = "cave"
PRESET = "cave"
LURES = ["egi", "softworm", "jig"]           # lure billboards this set's legend needs (fk_items bait_<key>)
LURE_TWEAKS = {}                             # frame-1 tweaks of NEW lures (egi / softworm / jig are in the kit)
SHEET = "encounter_sheet.png"                # legacy review-sheet name (other sets: encounter_sheet_<set>.png)

# ------------------------------------------------------------------ palette (cave preset, underwater)
WATER = ["#0b2230", "#0a1e2c", "#091a28", "#081724", "#06131f", "#04101a"]     # top -> bottom
ARCH = ["#0c2634", "#0e2a38", "#12303f"]                                         # far, body, lit edge
ROCK = ["#061821", "#0a202a", "#0f2c37"]                                         # near pillars dark -> lit
ROCK_FAR = ["#081c26", "#0a212c"]                                                # far pillars (near the water)
ROCK_HI = "#1f5a66"                                                              # 1 px rim on the lit edges
ROCK_GLOW = "#15404c"                                                            # crystal light on the rock
CRY_C = ["#1f5a66", "#2f8a96", "#52c2cc", "#9ff4f0"]                             # cyan crystals
CRY_V = ["#2a2a58", "#443e86", "#6e5cb8", "#b494f0"]                             # violet crystals
TWINKLE = "#6affea"
TWINKLE_V = "#e4d4ff"
SILT = ["#04121a", "#061820", "#0a2029", "#0d2832", "#10303a", "#18444e"]         # shadow .. stone top
FORE = ["#03090e", "#050d14", "#081820", "#0e2630", "#1c4a55"]
FRAME = dict(ink="#0c0a16", dark="#231d38", mid="#2e2848", lit="#3a3458", line="#6affea",
             cy=["#2a7c9a", "#6ad8ff", "#c8fff6"], vi=["#6a4aa8", "#c89cff", "#f0e0ff"])

# ------------------------------------------------------------------ layout (canvas rows / review placement)
BG_W, BG_H = 640, 400
HORIZON = 150             # bg row where the far arches stand in the haze (~ the floor horizon of a 270 view)
HORIZON_BG = HORIZON
# review sheet (hyb_encounter.review): ray (tint, alpha x3 for the review, x, y), layer offsets
REVIEW = dict(ray=("#86dcff", 0.08 * 3, 470, 0), mid=(-64, HORIZON - 200), floor=(-64, HORIZON), fore=(0, 400 - 128))

# ------------------------------------------------------------------ runtime look (the preview mock uses these; the
# EncounterSetDef row in the code carries the same values - Docs/legends_rollout.md 4)
PROFILE = dict(
    lure_at="floor",                         # the lure rests on the floor (set y = 0) / "surface" / "mid"
    cam=(-1.2, 0.45, -2.7),                  # set camera rest (lure = origin)
    line_up=(-0.9, 2.2, -1.6),               # where the line comes down from
    clear="#04101a",                         # back camera clear colour
    ray=("#86dcff", 0.08, 150, -20),         # god-ray tint, alpha, x from the right edge, y (px)
    halo="#9affea",                          # lure halo tint
    snow_far="#3a6a7a", snow_near="#9fd8e0",
    line="#8fb8c0",                          # the line in the window (mixed 50 % with the line colour)
    silt="#8fb8b8",                          # silt-puff tint
    rim=("#86dcff", 0.45 * 0.62, [((0, 1), 1.0), ((-1, 1), 0.8), ((1, 1), 0.8)]),   # fish-quad rim (col, s, dirs)
    fore_x=-20,                              # fore layer x (px, before parallax)
    abyss="#03080c", fog_outline="#1f5f70", sun_mix=0.0,
)


# ============================================================================ uw_cave_bg (640x400, opaque)
def arch_shape(h, w, cx, base, top, span, leg, thick, seed):
    """A natural rock arch: two tapering legs + a thick curved span, rough edges."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    rough = (E.fbm(xx / 9.0, yy / 9.0, seed) - 0.5) * 7.0
    m = np.zeros((h, w), bool)
    for sgn in (-1, 1):
        lx = cx + sgn * span / 2
        t = np.clip((yy - top) / max(1, base - top), 0, 1)
        half = leg * (0.55 + 0.6 * t ** 1.6)                   # legs widen to the base
        m |= (np.abs(xx - lx - sgn * 6 * (1 - t)) < half + rough) & (yy >= top + thick * 0.3) & (yy <= base)
    # span: an elliptic ring segment over the opening
    ex, ey = span / 2 + leg * 0.5, (base - top) * 0.62
    d = np.sqrt(((xx - cx) / ex) ** 2 + ((yy - (top + ey)) / ey) ** 2)
    m |= (d > 1.0 - thick / ey + rough / ey * 0.6) & (d < 1.0 + rough / ey * 0.4) & (yy < top + ey)
    return m


def column_shape(h, w, cx, base, top, wid, seed):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    t = np.clip((yy - top) / max(1, base - top), 0, 1)
    half = wid * (0.5 + 0.35 * np.abs(t - 0.45) * 2 + 0.3 * t ** 3)
    rough = (E.fbm(xx / 7.0, yy / 11.0, seed) - 0.5) * 6.0
    return (np.abs(xx - cx) < half + rough) & (yy >= top) & (yy <= base)


def make_bg():
    stops = [(0, WATER[0]), (60, WATER[1]), (118, WATER[2]), (178, WATER[3]), (240, WATER[4]), (300, WATER[5])]
    idx, pal = E.quant_rows(stops, BG_H, BG_W)
    img = R.to_rgba(idx, pal)
    th = R.bayer(BG_H, BG_W)
    rows = np.arange(BG_H)[:, None] + 0.5
    # far layer (fainter, #0c2634): two pale columns and a low arch far right
    far = column_shape(BG_H, BG_W, 60, HORIZON_BG + 6, 20, 26, 3.0)
    far |= column_shape(BG_H, BG_W, 598, HORIZON_BG + 6, 10, 30, 5.0)
    far |= arch_shape(BG_H, BG_W, 455, HORIZON_BG + 4, 70, 120, 20, 22, 9.0)
    # near-far layer (#0e2a38): the big arch left of centre and a broken arch on the right
    near = arch_shape(BG_H, BG_W, 228, HORIZON_BG + 10, 30, 190, 30, 34, 1.0)
    near |= column_shape(BG_H, BG_W, 520, HORIZON_BG + 10, 64, 34, 7.0) & (rows > 90)
    near |= arch_shape(BG_H, BG_W, 560, HORIZON_BG + 12, 58, 80, 26, 24, 11.0) & ((np.arange(BG_W)[None, :] > 540)
                                                                                  | (rows > 110))
    # the bases dissolve into the haze band (dithered), the tops fade into the murk
    fade_far = np.clip((rows - (HORIZON_BG - 26)) / 30.0, 0, 1) + np.clip((40 - rows) / 40.0, 0, 1)
    fade_near = np.clip((rows - (HORIZON_BG - 14)) / 24.0, 0, 1) + np.clip((24 - rows) / 30.0, 0, 1) * 0.6
    E.put(img, far & (th >= fade_far), ARCH[0])
    nm = near & (th >= fade_near)
    E.put(img, nm, ARCH[1])
    # 1 px lit edge on the arch tops (light from above, the surface far up)
    top_edge = nm & ~R.shift(nm, 1, 0, False)
    E.put(img, top_edge & (rows < HORIZON_BG - 30), ARCH[2])
    # a haze band along the horizon: one step lighter water, dash-dithered
    band = np.exp(-((rows - HORIZON_BG) / 10.0) ** 2) * 0.55
    hz = (band > R.dash_threshold(BG_H, BG_W, random.Random(4), 3, 9)) & ~nm
    E.put(img, hz & (rows < HORIZON_BG + 12), WATER[1])
    img[..., 3] = 1.0
    return img


# ============================================================================ uw_cave_mid (768x200) - 3D render
MID_W, MID_H, MID_PPU = 768, 200, 8.0     # 96 m x 25 m on the ortho camera


def rock_pillar(name, x, width, seed, mat, top=27.0, y=0.0, lean=0.0, waist=0.45, kind="rock"):
    """Hourglass column (a stalagmite grown into its stalactite): flared base, waist, widening upwards; bumpy."""
    rng = random.Random(seed)
    pts, rads = [], []
    n = 16
    for i in range(n):
        z = top * i / (n - 1)
        u = z / top
        r = width / 2 * (1.0 + 0.5 * math.exp(-z / 1.2) - waist * math.exp(-((u - 0.45) / 0.22) ** 2) + 0.35 * u ** 2)
        r *= 1.0 + rng.uniform(-0.08, 0.08)
        pts.append((x + lean * u * width + rng.uniform(-0.15, 0.15), y, z))
        rads.append((r, r * 0.8))
    ob = R.loft(name, pts, rads, mat, segs=12, smooth=False)
    for v in ob.data.vertices:
        p = v.co
        k = mnoise.noise(V((p.x * 0.35 + seed, p.y * 0.35, p.z * 0.22))) * 0.18 * width
        p.x += k * (1 if p.x > x else -1)
    ob.data.update()
    return R.tagk(ob, kind)


def crystal(name, base, height, radius, tilt, yaw, mat, kind):
    """Hexagonal prism with a pointed tip, flat shaded."""
    bm = bmesh.new()
    ring0, ring1 = [], []
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.3
        ring0.append(bm.verts.new((radius * math.cos(a), radius * math.sin(a), 0.0)))
        ring1.append(bm.verts.new((radius * 0.92 * math.cos(a), radius * 0.92 * math.sin(a), height * 0.72)))
    tip = bm.verts.new((0, 0, height))
    for k in range(6):
        k2 = (k + 1) % 6
        bm.faces.new((ring0[k], ring0[k2], ring1[k2], ring1[k]))
        bm.faces.new((ring1[k], ring1[k2], tip))
    bm.faces.new(ring0[::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = C.mesh_object(name, bm, mat)
    ob.matrix_world = Matrix.Translation(V(base)) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(tilt, 4, "Y")
    C.set_smooth(ob, False)
    return R.tagk(ob, kind)


def cluster(name, x, z, n, scale, mat, kind, seed, y=-1.5, spread=1.0, lean=0.0):
    rng = random.Random(seed)
    objs = []
    for i in range(n):
        h = scale * rng.uniform(0.45, 0.9) * (1.3 if i == 0 else 1.0)
        tilt = math.radians(rng.uniform(-40, 40) * (0.4 if i == 0 else 1.0) + lean)
        objs.append(crystal(f"{name}{i}", (x + rng.uniform(-1, 1) * spread * scale * 0.45, y - rng.uniform(0, 0.5),
                                           z + rng.uniform(-0.1, 0.1)), h, h * rng.uniform(0.17, 0.24), tilt,
                            rng.uniform(0, 6.28), mat, kind))
    return objs


def make_mid():
    C.reset_scene()
    R.reset_materials()
    R.use_preset("cave")
    L = V((0.6, -0.4, 0.7)).normalized()            # from the upper right (the god-ray side), towards the camera
    rock = R.m_tone(ROCK, [0.56, 0.8], soft=0.02, light=L, noise=0.035, nscale=0.5, ncoord="world", name="Rock")
    rock_far = R.m_tone(ROCK_FAR, [0.72], soft=0.04, light=L, name="RockFar")
    cc = R.m_tone(CRY_C, [0.36, 0.58, 0.84], light=L, name="CryC")
    cv = R.m_tone(CRY_V, [0.36, 0.58, 0.84], light=L, name="CryV")
    # far pillars (behind, close to the water colour) and near pillars; the centre stays open water
    rock_pillar("PF1", -27.0, 4.0, 11, rock_far, y=12.0, lean=0.1, kind="far")
    rock_pillar("PF2", 11.0, 3.2, 12, rock_far, y=12.0, lean=-0.15, waist=0.3, kind="far")
    rock_pillar("PF3", 35.0, 4.6, 13, rock_far, y=12.0, kind="far")
    rock_pillar("P1", -39.0, 8.0, 1, rock, lean=0.12)
    rock_pillar("P2", -18.5, 4.5, 2, rock, lean=-0.1, waist=0.55)
    rock_pillar("P3", 24.0, 6.5, 3, rock, lean=-0.08)
    rock_pillar("P4", 45.0, 5.0, 4, rock, lean=0.05, waist=0.35)
    # low boulders on the floor line
    for i, (x, r) in enumerate(((-30.0, 1.3), (-7.0, 0.9), (9.0, 1.1), (35.5, 1.5))):
        ob = R.ellipsoid(f"B{i}", (x, -1.0, 0.1), (r * 1.5, r, r * 0.7), rock, 10, 6)
        R.tagk(ob, "rock")
    # crystal clusters in front of the pillar bases (base radius = 0.75 width), on ledges, alone on the floor
    cluster("CA", -36.5, 0.2, 6, 4.4, cc, "cry_c", 21, y=-5.4, lean=-6)
    cluster("CB", -17.0, 0.2, 4, 3.0, cv, "cry_v", 22, y=-3.3)
    cluster("CC", 22.0, 0.2, 5, 3.8, cc, "cry_c", 23, y=-4.6, lean=6)
    cluster("CD", 3.5, 0.0, 3, 2.0, cc, "cry_c", 24, y=-2.0)
    cluster("CE", 27.5, 9.0, 3, 2.2, cv, "cry_v", 25, y=-4.2)       # on the right pillar's waist
    cluster("CF", -35.0, 13.0, 3, 2.0, cc, "cry_c", 26, y=-4.2)
    cluster("CG", 43.0, 0.1, 4, 3.0, cv, "cry_v", 27, y=-4.0)
    C.ortho_camera(0.0, MID_H / MID_PPU / 2, MID_W, MID_H, MID_PPU)
    ps = R.render_passes("mid")
    kinds = R.kind_map(ps)
    pal = R.Pal(ROCK + ROCK_FAR + [ROCK_HI, ROCK_GLOW] + CRY_C + CRY_V + WATER + [TWINKLE, TWINKLE_V])
    idx, _ = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    rows = np.arange(MID_H)[:, None] + 0.5
    cry = np.isin(kinds, ["cry_c", "cry_v"]) & (idx >= 0)
    rockm = np.isin(kinds, ["rock", "far"]) & (idx >= 0)
    far = (kinds == "far") & (idx >= 0)
    # crystal light on the rock around each cluster (dithered falloff over ~7 px)
    near_c = cry.copy()
    glow = np.zeros(idx.shape)
    for k in range(7):
        near_c = R.dilate(near_c)
        glow += near_c
    glow /= 7.0
    th = R.bayer(MID_H, MID_W)
    lit = rockm & ~far & (glow * 0.9 > th)
    idx[lit] = R.ramp_step(pal, ROCK, idx, 1)[lit]
    idx[rockm & ~far & (glow > 0.8)] = pal.index(ROCK_GLOW)
    # 1 px rim on the near pillars' lit (right) edges, below the haze
    near_rock = rockm & ~far
    rim = near_rock & ~R.shift(near_rock, 0, -1, False) & (idx == pal.index(ROCK[2])) & (rows > 70)
    idx[rim] = pal.index(ROCK_HI)
    # haze: everything but the crystals dissolves upwards into the water (dash dithered, the hybrid water style):
    # first a step towards the water colour, then transparent (the bg shows through)
    thd = R.dash_threshold(MID_H, MID_W, random.Random(2), 2, 7)
    haze = np.clip((125 - rows) / 125.0, 0, 1) ** 1.1 + far * 0.3
    haze = np.broadcast_to(haze, idx.shape)
    step = (haze * 1.7 > thd) & rockm
    idx[step] = pal.index(WATER[2])
    gone = (haze > thd) & rockm
    idx[gone] = -1
    # crystals: dark outline in their own hue, twinkle pixels on the tips and a few lit facets
    out_ring = R.dilate(cry) & ~cry
    idx[out_ring & R.dilate((kinds == "cry_c") & cry)] = pal.index(CRY_C[0])
    idx[out_ring & R.dilate((kinds == "cry_v") & cry) & ~R.dilate((kinds == "cry_c") & cry)] = pal.index(CRY_V[0])
    rng = np.random.default_rng(8)
    for kind, tw, top_col in (("cry_c", TWINKLE, CRY_C[3]), ("cry_v", TWINKLE_V, CRY_V[3])):
        m = (kinds == kind) & cry
        tops = m & ~R.shift(m, 1, 0, False)
        pick = tops & (rng.random(m.shape) < 0.4)
        pick |= m & (idx == pal.index(top_col)) & (rng.random(m.shape) < 0.1)
        idx[pick] = pal.index(tw)
    return R.to_rgba(idx, pal)


# ============================================================================ uw_cave_floor (768x120) - pseudo-perspective
FL_W, FL_H = 768, 120
CAM_H, FPX, R0 = 0.45, 177.0, 1.6


def make_floor():
    h, w = FL_H, FL_W
    rows = np.arange(h)[:, None] + 0.5
    cols = np.arange(w)[None, :] + 0.5
    dist = CAM_H * FPX / (rows + R0)                                  # metres to the floor point of each row
    xw = (cols - w / 2) * dist / FPX                                  # world x of each pixel
    th = R.bayer(h, w)
    # base silt tone by distance: dark near (outside any light), #10303a patches mid, darker haze far
    near_k = np.clip((1.7 - dist) / 1.0, 0, 1)
    far_k = np.clip((dist - 5.0) / 9.0, 0, 1)
    tone = np.full((h, w), 3.0)                                       # SILT[3] = #0d2832
    tone -= near_k * 1.7 + far_k * 1.0
    patch = E.fbm(xw / 1.4, dist / 1.4, 2.0)                          # soft lighter / darker patches
    tone += (patch - 0.5) * 1.5
    base = np.clip(np.floor(tone + th - 0.5), 1, 4).astype(int)
    pal = R.Pal(SILT + WATER)
    lut = np.array([pal.index(c) for c in SILT])
    k = base.copy()
    # ripple marks: ridges every 0.22 m of distance, wavy in x; crest one step lighter, trough one darker (dashes)
    phase = dist / 0.22 + (E.fbm(xw / 0.9, dist / 2.0, 5.0) - 0.5) * 1.6
    px_per_ridge = FPX * CAM_H * 0.22 / dist ** 2
    crest = (np.abs(np.mod(phase, 1.0) - 0.5) < 0.12) & (px_per_ridge > 2.2)
    trough = (np.abs(np.mod(phase + 0.28, 1.0) - 0.5) < 0.10) & (px_per_ridge > 3.0)
    dash = R.dash_threshold(h, w, random.Random(6), 3, 12) > 0.4
    k = np.where(crest & dash, np.minimum(k + 1, 4), k)
    k = np.where(trough & dash & ~crest, np.maximum(k - 1, 1), k)
    # stones: world-placed, projected (smaller towards the top), irregular, flat-bottomed; lit top edge, contact shadow
    rng = random.Random(9)
    stones = []
    for _ in range(95):
        d = math.exp(rng.uniform(math.log(0.8), math.log(12.0)))
        x = rng.uniform(-2.2, 2.2) * d
        s = rng.uniform(0.015, 0.055) * (2.4 if rng.random() < 0.1 else 1.0)
        stones.append((d, x, s, rng.uniform(0, 6.28)))
    for d, x, s, ph in sorted(stones, key=lambda q: -q[0]):
        rx = FPX * s / d
        if rx < 1.0:
            continue
        cy = CAM_H * FPX / d - R0
        cx = w / 2 + FPX * x / d
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
        wob = 1 + 0.22 * np.sin(ang * 3 + ph) + 0.12 * np.sin(ang * 5 + 2 * ph)
        body = dx ** 2 + dy ** 2 < wob ** 2
        if not body.any():
            continue
        sub = k[y0:y1, x0:x1]
        near = d < 1.3
        top = body & ~R.shift(body, 1, 0, False)
        below = R.shift(body, 1, 0, False) & ~body
        sub[below] = 0
        sub[body] = 1 if near else 2
        sub[top] = (3 if near else 4) if rx > 2.0 else 3
        if rx > 3.0:                                   # second lit row on the bigger stones, broken
            top2 = body & R.shift(top, 1, 0, False) & (R.bayer(y1 - y0, x1 - x0, x0, y0) < 0.5)
            sub[top2] = 3
    idx = lut[np.clip(k, 0, 5)]
    # a few crystal shards glinting in the silt (1 px) in the mid distance
    for _ in range(9):
        d = rng.uniform(1.5, 6.0)
        x = rng.uniform(-2.0, 2.0) * d
        cy, cx = int(CAM_H * FPX / d - R0), int(w / 2 + FPX * x / d)
        if 0 <= cy < h and 0 <= cx < w:
            idx[cy, cx] = pal.index(SILT[5])
    img = R.to_rgba(idx, pal)
    # far rows dissolve into the haze (transparent), nearest rows into the dark (transparent over the bg)
    fade = np.clip((8.0 - rows) / 8.0, 0, 1) + np.clip((rows - (h - 18)) / 18.0, 0, 1)
    gone = np.broadcast_to(fade > th, (h, w))
    img[gone] = 0.0
    return img


# ============================================================================ uw_cave_fore (256x128)
def make_fore():
    h, w = 128, 256
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    # lumpy mound from the left edge, highest near x ~ 50, reaching the bottom edge at x ~ 235
    x = xx / w
    crest = h - (112 * np.exp(-((x - 0.16) / 0.3) ** 2) + 58 * np.exp(-((x - 0.55) / 0.2) ** 2) * (x < 0.75)
                 + 20 * (1 - x)) * (1 - np.clip((x - 0.8) / 0.12, 0, 1))
    crest += (E.fbm(xx / 18.0, np.zeros_like(xx), 3.0) - 0.5) * 16
    body = yy > crest
    th = R.bayer(h, w)
    depth_in = yy - crest
    img = E.blank(h, w)
    # out of focus: two big flat tones, a soft dithered upper edge, a faint cyan rim where the lure light reaches
    E.put(img, body, FORE[1])
    E.put(img, body & (E.fbm(xx / 30.0, yy / 30.0, 8.0) > 0.58), FORE[0])
    E.put(img, body & (depth_in < 7) & (xx > 30), FORE[2])
    E.put(img, body & (depth_in < 2.5) & (xx > 70) & (xx < 215), FORE[3])
    edge = (yy > crest - 2) & ~body & (th < 0.5)
    E.put(img, edge, FORE[2])
    rim = body & (depth_in < 1.0) & (xx > 110) & (xx < 190)
    E.put(img, rim, FORE[4])
    # two soft bokeh specks (crystal light on wet rock)
    for cx, cy, r in ((150, 104, 2.2), (196, 118, 1.6)):
        m = ((xx - cx) ** 2 + (yy - cy) ** 2 < r * r) & body
        E.put(img, m, FORE[3])
    return img


# ============================================================================ the set
def layers():
    """name -> top-down RGBA: every uw_cave_* layer (in build order)."""
    return {"uw_cave_bg": make_bg(), "uw_cave_floor": make_floor(), "uw_cave_fore": make_fore(),
            "uw_cave_mid": make_mid()}


def frame():
    """enc_frame_cave: dark stone, #6affea inner line, crystal studs (cyan TL / BR, violet TR / BL)."""
    return E.make_frame(FRAME, (FRAME["cy"], FRAME["vi"], FRAME["vi"], FRAME["cy"]))
