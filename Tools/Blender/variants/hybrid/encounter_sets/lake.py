"""
Encounter set LAKE (황금잉어 golden_carp; Docs/legends_rollout.md 4.2): murky green daylight. The lure / bait rests on
the mud of the weed edge (5-7 m deep), the surface far above is a bright band with lily-pad shadows, soft sunbeams
come from the top right and everything melts into the green at ~3.6 m. Built by hyb_encounter.py
(see encounter_sets/README.md).

  uw_lake_bg.png      640x400 opaque  water gradient #6a8a4a (bright upper water) -> #50703e -> #3a5a36 -> #243e2a ->
                                      #16281c, banded + Bayer; the top ~100 rows are the surface far above: lily-pad
                                      shadows #2a4a26 (round, notched, flattened by perspective, the lower ones
                                      dissolving) with stems hanging down, dash-dithered caustic streaks #86a656; far
                                      wavy weed-forest silhouettes #2a4a2c rising from the haze band at row HORIZON
  uw_lake_mid.png     768x200 alpha   weed beds rendered in 3D (Blender, palette-ramp materials, ortho 8 px/m): twisting
                                      eelgrass ribbons and pondweed (#2e5a2a .. #6a9a3a), a sunken mossy log, reed stems
                                      at both edges, low tufts along the floor line; the centre stays open water; the
                                      tops dissolve upwards into the murk (dash dither), 1 px sun flecks on the tips
  uw_lake_floor.png   768x120 alpha   mud and silt #3a3a24 .. #6a6a44 in pseudo-perspective (camera 0.45 m above the
                                      floor, f 177 px): soft patches, faint caustics near, pebbles, fallen leaves,
                                      1 px mussel-shell glints; fogs into the green by ~3.6 m, the top rows dissolve
                                      into the haze band, the nearest rows into the dark
  uw_lake_fore.png    256x128 alpha   out-of-focus weed fronds #10200e for the bottom-left (flat tones, dithered edge,
                                      faint top-right sun rim)
  enc_frame_lake.png   24x24          9-slice, 6 px border: mossy wood band, 1 px #a8e878 inner line at px 3, lily-pad
                                      corner studs (notch facing out)
  lures: golden, corn (new; frame-1 tweaks here), softworm (kit tweak, shared with the ice set)
"""
import math
import random
import bmesh
import bpy
import numpy as np
from mathutils import Vector, Matrix, noise as mnoise
import hyb_core as R
import fk_common as C
import hyb_encounter_kit as E

V = Vector
SET = "lake"
PRESET = "lake"
LURES = ["golden", "corn", "softworm"]


# ------------------------------------------------------------------ lure frame-1 tweaks (billboards 16x8)
def _keep_frame(objs, fn):
    """Run a frame-1 tweak without changing the billboard framing: kit.lure_frames fits the camera to the mesh bounds,
    so two loose (never rendered) vertices pin the frame-0 bounds (keep the tweak inside them)."""
    x0, x1, z0, z1 = C.world_bounds(objs)
    fn(objs)
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


def _tweak_golden(objs):
    """Frame 1: the glitter crumbs shift (two of the three sparks drift down; the framing sparks keep their x)."""
    def fn(objs):
        sparks = sorted([o for o in objs if o.name.startswith("Spark")],
                        key=lambda o: min((o.matrix_world @ v.co).z for v in o.data.vertices))
        for o, dz in zip(sparks[1:], (-0.32, -0.42)):       # the lowest spark stays (it sets the bottom bound)
            o.matrix_world = Matrix.Translation((0.0, 0.0, dz)) @ o.matrix_world
    _keep_frame(objs, fn)


def _tweak_corn(objs):
    """Frame 1: the kernels swing on the hook (about the hook eye, pulled back inside the frame-0 bounds)."""
    def fn(objs):
        piv = (0.0, 0.0, 0.95)
        for o in objs:
            if o.name.startswith("Kernel") or o.name.startswith("KTip"):
                o.matrix_world = Matrix.Translation((0.06, 0.0, 0.05)) @ _about(piv, -9) @ o.matrix_world
    _keep_frame(objs, fn)


LURE_TWEAKS = {"golden": _tweak_golden, "corn": _tweak_corn}

# ------------------------------------------------------------------ palette (murky green daylight)
WATER = ["#6a8a4a", "#50703e", "#3a5a36", "#2e4c32", "#243e2a", "#16281c"]    # bright upper water -> deep
PAD = "#2a4a26"                                                                # lily-pad shadows, stems
CAUSTIC = "#86a656"                                                            # caustic streaks (bright band)
WEED_BG = "#2a4a2c"                                                            # far weed-forest silhouettes
WEED = ["#223f1f", "#2e5a2a", "#44762e", "#6a9a3a"]                            # near weeds dark -> sunlit
WEED_FAR = ["#2e4c32", "#35553a", "#46683a"]                                   # far weeds (near the water)
SUNFLECK = "#9ac452"                                                           # 1 px sun on weed tips
LOG = ["#1c2214", "#2e3220", "#46482a", "#5a7030"]                             # sunken log, mossy top
REED = ["#2a3a1c", "#3e5226", "#5a7032"]
MUD = ["#2c2c1c", "#3a3a24", "#4a4a2e", "#5a5a38", "#6a6a44", "#80805a"]         # shadow .. stone top
LEAF = ["#403a1e", "#56482a", "#6e5c32"]
SHELL = "#d0d8c0"
FOG = ["#2e4c32", "#3a5a36", "#46603a"]                                        # the floor melts into these
FORE = ["#0a160a", "#10200e", "#162a14", "#26441f"]
FRAME = dict(ink="#0e140a", dark="#2e2214", mid="#4a3620", lit="#5a7a2e", line="#a8e878",
             pad=["#2e5a24", "#5a9a3a", "#a8e878"])

# ------------------------------------------------------------------ layout
BG_W, BG_H = 640, 400
HORIZON = 150             # bg row on the projected floor horizon (the haze band); same camera as the cave
REVIEW = dict(ray=("#f0ffc0", 0.14 * 2, 470, 0), mid=(-64, HORIZON - 200), floor=(-64, HORIZON), fore=(0, 400 - 128))

# ------------------------------------------------------------------ runtime look (the preview mock uses these; the
# EncounterSetDef row in the code carries the same values - Docs/legends_rollout.md 4.1)
PROFILE = dict(
    lure_at="floor",                         # the bait rests on the mud (set y = 0 = the floor)
    lure_rest=(0.0, 0.04, 0.0),
    cam=(-1.2, 0.45, -2.7), frame_at=(0.33, 0.30),
    line_up=(-0.9, 2.2, -1.6),
    drag_dir=(-0.45, 0.0, -0.9),
    clear="#1a3024",
    rays=[("#f0ffc0", 0.14, 150, -20), ("#f0ffc0", 0.14, 232, -44)],     # 2 x shared uw_ray (tint, a, x from right, y)
    halo="#fff0a0", halo_alpha=0.5,          # golden bait 0.5, the other baits 0
    snow_far="#6a8a5a", snow_near="#c8e0a8",  # pollen
    line="#d8e8c8", silt="#a8966a",
    rim=("#e8f8c0", 0.35 * 0.62, [((0, 1), 1.0), ((-1, 1), 0.8), ((1, 1), 0.8)]),
    fore_x=-20,
    abyss="#1a3024", fog_outline="#3a5a3a", sun_mix=0.75, visibility=3.6,
    veil=("#0c1a10", 0.55), frame_line="#a8e878",
)


# ============================================================================ helpers
def _ramp_rgb(t, cols):
    """t (array 0..1) -> linear RGB along hex stops (evenly spaced)."""
    lc = np.array([R.s2l(R.hexrgb(c)) for c in cols])
    x = np.clip(t, 0, 1) * (len(cols) - 1)
    i = np.clip(np.floor(x).astype(int), 0, len(cols) - 2)
    f = (x - i)[..., None]
    return lc[i] * (1 - f) + lc[i + 1] * f


def _nearest(pal, lin_rgb):
    d = ((pal.lin - np.asarray(lin_rgb)[None, :]) ** 2).sum(1)
    return int(np.argmin(d))


# ============================================================================ uw_lake_bg (640x400, opaque)
def make_bg():
    h, w = BG_H, BG_W
    stops = [(0, WATER[0]), (70, WATER[0]), (100, WATER[1]), (122, WATER[2]), (156, WATER[4]), (255, WATER[5])]
    idx, pal = E.quant_rows(stops, h, w)
    img = R.to_rgba(idx, pal)
    th = R.bayer(h, w)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    rows = yy[:, :1]
    rng = random.Random(11)
    # --- caustic streaks in the bright band: ridged noise stretched sideways, dash dithered, fading downwards
    n = E.fbm(xx / 30.0, yy / (5.0 + yy * 0.03), 3.0)
    ridge = 1.0 - np.abs(2.0 * n - 1.0)
    amt = np.clip((ridge - 0.84) / 0.16, 0, 1) * np.clip((86 - rows) / 60.0, 0, 1)
    caustic = amt > R.dash_threshold(h, w, random.Random(3), 2, 7)
    # --- lily-pad shadows: clusters of flattened, notched ellipses (nearer = higher, bigger, rounder)
    pads = np.zeros((h, w), bool)
    stems = np.zeros((h, w), bool)
    # (the rest window shows bg rows ~80..216: the lower clusters are the ones seen in its top rows)
    clusters = [(84, 14, 3), (236, 44, 4), (410, 24, 3), (560, 60, 4), (338, 84, 3), (30, 76, 3), (612, 10, 2),
                (170, 92, 4), (470, 96, 3), (292, 8, 2), (254, 101, 2), (402, 88, 2), (600, 94, 2), (110, 100, 2)]
    for cx0, cy0, k in clusters:
        for _ in range(k):
            cy = cy0 + rng.uniform(-6, 6)
            near = max(0.0, 1.0 - cy / 150.0)
            rx = 3.5 + 30.0 * near ** 1.5 * rng.uniform(0.7, 1.15)
            ry = rx * (0.2 + 0.3 * near)
            cx = cx0 + rng.uniform(-1.6, 1.6) * rx
            ang = np.arctan2((yy - cy) / ry, (xx - cx) / rx)
            wob = 1 + 0.05 * np.sin(ang * 5 + cx)
            e = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
            notch_a = rng.uniform(-math.pi, math.pi)
            dang = np.abs((ang - notch_a + math.pi) % (2 * math.pi) - math.pi)
            m = (e < wob ** 2) & ~((dang < 0.28) & (e > 0.04))
            keep = th < np.clip((124 - cy) / 30.0, 0, 1)                       # the far ones dissolve
            pads |= m & keep
            if cy < 96 and rng.random() < 0.55:                              # a stem hanging down, fading out
                L = rng.uniform(26, 70)
                ph = rng.uniform(0, 6.28)
                ys = np.arange(int(cy + ry * 0.2), int(min(h - 1, cy + L)))
                for y in ys:
                    t = (y - cy) / L
                    x = int(round(cx + rng.uniform(-0.3, 0.3) + 2.2 * math.sin(y / 9.0 + ph) * t))
                    if 0 <= x < w and th[y, x] > t * 1.05 and y < 128:
                        stems[y, x] = True
    E.put(img, caustic & ~pads & (rows < 92), CAUSTIC)
    # --- far weed forest: a low rolling bank on the horizon + wavy stalk clumps rising into the haze
    far = np.zeros((h, w), bool)
    bank_top = HORIZON - 4 - 11 * E.fbm(xx[:1] / 38.0, np.zeros((1, w)), 6.0) ** 1.5
    far |= (rows > bank_top) & (rows < HORIZON + 8)
    for x0, cnt, hmax in ((30, 7, 44), (128, 5, 30), (214, 8, 58), (318, 4, 26), (402, 6, 40), (500, 9, 62),
                          (606, 6, 36)):
        for _ in range(cnt):
            xs = x0 + rng.uniform(-18, 18)
            H = rng.uniform(0.45, 1.0) * hmax
            wid = rng.uniform(0.9, 2.1)
            ph, kk, amp = rng.uniform(0, 6.28), rng.uniform(1.0, 2.2), rng.uniform(2.0, 5.0)
            u = np.clip((HORIZON + 4 - rows[:, 0]) / H, 0, 1)
            xc = xs + amp * np.sin(u * kk * math.pi + ph) * u
            half = wid * (1.0 - 0.55 * u)
            ok = (rows[:, 0] <= HORIZON + 4) & (rows[:, 0] >= HORIZON + 4 - H)
            m = (np.abs(xx - xc[:, None]) < half[:, None]) & ok[:, None]
            m &= th > np.clip((u[:, None] - 0.55) / 0.45, 0, 1)             # the tips dissolve
            far |= m
    fade_base = np.clip((rows - (HORIZON - 8)) / 14.0, 0, 1)
    fm = far & (th >= fade_base)
    E.put(img, fm, WEED_BG)
    # a 1 px lighter top edge where the stalks catch the light from above (upper stalks only)
    top_edge = fm & ~R.shift(fm, 1, 0, False) & (rows < HORIZON - 14)
    E.put(img, top_edge, WATER[2])
    E.put(img, stems & ~fm, PAD)
    E.put(img, pads, PAD)
    # --- below the floor band (bg rows > HORIZON + 112, only seen full screen): the near mud right under the camera,
    #     big soft patches (it sits at parallax 0.1 under the floor layer's dissolving near rows)
    near_mud = np.broadcast_to(rows >= HORIZON + 100, (h, w))       # solid under the floor's dissolving near rows
    tone = E.fbm(xx / 44.0, yy / 16.0, 12.0)
    E.put(img, near_mud, MUD[2])
    E.put(img, near_mud & (tone > 0.58 + (th - 0.5) * 0.08), MUD[3])
    E.put(img, near_mud & (rows > HORIZON + 190) & (th < np.clip((rows - HORIZON - 190) / 50.0, 0, 1)), MUD[1])
    # --- haze band along the horizon (one step lighter water, dash dithered)
    band = np.exp(-((rows - HORIZON) / 9.0) ** 2) * 0.6
    hz = (band > R.dash_threshold(h, w, random.Random(4), 3, 9)) & ~fm
    E.put(img, hz & (rows > HORIZON - 12) & (rows < HORIZON + 12), WATER[3])
    img[..., 3] = 1.0
    return img


# ============================================================================ uw_lake_mid (768x200) - 3D render
MID_W, MID_H, MID_PPU = 768, 200, 8.0     # 96 m x 25 m on the ortho camera, bottom row = the floor horizon


def ribbon(name, x, y, H, wid, seed, mat, kind, lean=0.0, sway=1.0, n=22):
    """Eelgrass blade: a flat strip up a wavy path, twisting (|cos| of the facing >= 0.35 so it never vanishes)."""
    rng = random.Random(seed)
    ph, k = rng.uniform(0, 6.28), rng.uniform(0.5, 1.2)
    a0, tw, tph = rng.uniform(-0.45, 0.45), rng.uniform(0.5, 0.75), rng.uniform(0, 6.28)
    bm = bmesh.new()
    prev = None
    for i in range(n + 1):
        u = i / n
        z = H * u - 0.4
        cx = x + lean * u * H + sway * math.sin(u * math.pi * 2 * k + ph) * u * (0.5 + 0.09 * H)
        ang = a0 + tw * math.sin(u * math.pi * 1.7 + tph)
        ww = wid * (1.0 - 0.8 * u ** 3) * (1.0 + 0.25 * math.exp(-u * 8))
        dx, dy = math.cos(ang) * ww / 2, math.sin(ang) * ww / 2
        v1 = bm.verts.new((cx - dx, y - dy, z))
        v2 = bm.verts.new((cx + dx, y + dy, z))
        if prev:
            bm.faces.new((prev[0], prev[1], v2, v1))
        prev = (v1, v2)
    ob = C.mesh_object(name, bm, mat)
    C.set_smooth(ob, False)
    return R.tagk(ob, kind)


def pondweed(name, x, y, H, seed, stem_mat, leaf_mat, kind, lean=0.0):
    """Pondweed: a wavy stem with alternating oval leaves angled up."""
    rng = random.Random(seed)
    ph = rng.uniform(0, 6.28)
    pts, n = [], 12
    for i in range(n + 1):
        u = i / n
        pts.append((x + lean * u * H + 0.6 * math.sin(u * 3.0 + ph) * u, y, H * u - 0.3))
    objs = [R.tagk(R.loft(name + "S", pts, 0.09, stem_mat, segs=6, smooth=True), kind)]
    m = int(H / 0.8)
    for j in range(1, m):
        u = j / m
        p = V(pts[min(n, int(round(u * n)))])
        side = 1 if j % 2 else -1
        L = rng.uniform(0.7, 1.05) * (1.0 - 0.35 * u)
        ang = math.radians(side * rng.uniform(35, 60))
        c = p + V((math.sin(ang) * L * 0.55, rng.uniform(-0.2, 0.2), math.cos(ang) * L * 0.55))
        rot = Matrix.Rotation(ang, 4, "Y") @ Matrix.Rotation(rng.uniform(-0.6, 0.6), 4, "Z")
        ob = R.ellipsoid(f"{name}L{j}", tuple(c), (0.16, 0.05, L * 0.5), leaf_mat, 8, 5, rot=rot)
        objs.append(R.tagk(ob, kind))
    return objs


def reed(name, x, y, r, seed, mat, kind, lean=0.0, top=27.0, kink=None):
    rng = random.Random(seed)
    pts = []
    for i in range(10):
        u = i / 9
        z = -0.5 + (top + 0.5) * u
        xx = x + lean * z + rng.uniform(-0.04, 0.04)
        if kink is not None and z > kink:                     # a broken reed: the top bends over
            xx += (z - kink) * 0.9
            z = kink + (z - kink) * 0.45
        pts.append((xx, y, z))
    return R.tagk(R.loft(name, pts, r, mat, segs=6, smooth=True), kind)


def sunken_log(name, x0, x1, z, r, y, mat, seed):
    rng = random.Random(seed)
    pts, rads = [], []
    n = 14
    for i in range(n):
        u = i / (n - 1)
        pts.append((x0 + (x1 - x0) * u, y + rng.uniform(-0.1, 0.1), z + 0.25 * math.sin(u * 2.2) + rng.uniform(-0.05, 0.05)))
        rr = r * (0.8 + 0.25 * math.sin(u * 5 + 1) * 0.5 + (0.3 if u > 0.92 else 0.0))
        rads.append((rr, rr * 0.9))
    objs = [R.loft(name, pts, rads, mat, segs=10, smooth=False)]
    for v in objs[0].data.vertices:
        k = mnoise.noise(V((v.co.x * 0.8 + seed, v.co.y * 0.8, v.co.z * 0.8))) * 0.12 * r
        v.co.z += k
    objs[0].data.update()
    # a broken branch stub angled up, and a shorter snapped one
    bx = x0 + (x1 - x0) * 0.38
    objs.append(R.loft(name + "B1", [(bx, y - 0.2, z + r * 0.6), (bx + 0.9, y - 0.3, z + 2.2), (bx + 1.3, y - 0.3, z + 3.3)],
                       [0.34, 0.24, 0.16], mat, segs=7, smooth=False))
    bx2 = x0 + (x1 - x0) * 0.72
    objs.append(R.loft(name + "B2", [(bx2, y - 0.2, z + r * 0.5), (bx2 - 0.5, y - 0.3, z + 1.4)], [0.26, 0.2], mat,
                       segs=7, smooth=False))
    return [R.tagk(o, "log") for o in objs]


def make_mid():
    C.reset_scene()
    R.reset_materials()
    R.use_preset("lake")
    L = V((0.45, -0.3, 0.84)).normalized()               # the sun: from the upper right, a little towards the camera
    weed = R.m_tone(WEED, [0.42, 0.64, 0.86], light=L, name="Weed")
    weed_far = R.m_tone(WEED_FAR, [0.55, 0.82], light=L, name="WeedFar")
    log = R.m_tone(LOG, [0.36, 0.58, 0.8], light=L, noise=0.07, nscale=0.9, ncoord="world", name="Log")
    reed_m = R.m_tone(REED, [0.5, 0.78], light=L, name="Reed")
    rng = random.Random(5)
    k = 0
    # far weeds (behind, close to the water colour): thin ribbons across the whole width, lower in the centre
    for x in np.linspace(-46, 46, 30):
        xx = float(x) + rng.uniform(-1.2, 1.2)
        H = rng.uniform(3.0, 9.0) * (0.55 if abs(xx) < 12 else 1.0)
        ribbon(f"F{k}", xx, 14.0, H, rng.uniform(0.35, 0.5), 100 + k, weed_far, "far", lean=rng.uniform(-0.05, 0.05))
        k += 1
    # near weed beds: tall eelgrass + pondweed clumps at both sides and the window edges; the centre stays open
    beds = [(-40.0, 9, 17.0), (-30.0, 7, 12.0), (-18.5, 7, 11.0), (16.0, 8, 13.0), (27.0, 6, 9.0), (39.0, 9, 18.0)]
    for bx, cnt, hmax in beds:
        for _ in range(cnt):
            xx = bx + rng.uniform(-2.8, 2.8)
            ribbon(f"R{k}", xx, rng.uniform(-1.0, 1.0), rng.uniform(0.45, 1.0) * hmax, rng.uniform(0.42, 0.62),
                   200 + k, weed, "weed", lean=rng.uniform(-0.06, 0.06), sway=rng.uniform(0.6, 1.2))
            k += 1
        for j in range(2):
            pondweed(f"P{k}", bx + rng.uniform(-2.5, 2.5), -1.4, rng.uniform(0.35, 0.7) * hmax, 300 + k, weed, weed,
                     "weed", lean=rng.uniform(-0.05, 0.05))
            k += 1
    # low tufts along the floor line (the weed edge of the drop-off), sparse in the centre
    for x in np.linspace(-44, 44, 40):
        xx = float(x) + rng.uniform(-1, 1)
        if abs(xx) < 10 and rng.random() < 0.55:
            continue
        ribbon(f"T{k}", xx, rng.uniform(-2.0, 2.0), rng.uniform(0.9, 2.6), rng.uniform(0.35, 0.5), 400 + k, weed,
               "weed", sway=0.5)
        k += 1
    # the sunken log (left of centre, its right end at the window's left third) and reeds at both edges
    sunken_log("Log", -24.0, -9.5, 0.35, 0.75, -2.5, log, 7)
    for i, (x, kink) in enumerate(((-28.5, None), (-27.4, 9.5), (-26.2, None), (-21.8, None), (-20.9, 13.0),
                                   (21.2, None), (22.4, 11.0), (26.0, None), (27.1, None), (28.3, 8.0))):
        reed(f"Reed{i}", x, -3.0, rng.uniform(0.13, 0.18), 500 + i, reed_m, "reed", lean=rng.uniform(-0.012, 0.012),
             kink=kink)
    C.ortho_camera(0.0, MID_H / MID_PPU / 2, MID_W, MID_H, MID_PPU)
    ps = R.render_passes("mid")
    kinds = R.kind_map(ps)
    pal = R.Pal(WEED + WEED_FAR + LOG + REED + WATER + [SUNFLECK])
    idx, _ = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    rows = np.arange(MID_H)[:, None] + 0.5
    th = R.bayer(MID_H, MID_W)
    solid = idx >= 0
    far = (kinds == "far") & solid
    near = solid & ~far
    # sun flecks: a few top pixels of sunlit near weeds
    rngn = np.random.default_rng(8)
    lit = near & (kinds == "weed") & (idx == pal.index(WEED[3]))
    tops = lit & ~R.shift(near, 1, 0, False)
    idx[tops & (rngn.random(idx.shape) < 0.35)] = pal.index(SUNFLECK)
    # haze: everything dissolves upwards into the murk (dash dither): first a step towards the water, then gone;
    # the far weeds are hazier; the reeds keep a little longer (they rise out of the water)
    thd = R.dash_threshold(MID_H, MID_W, random.Random(2), 2, 7)
    haze = np.clip((150 - rows) / 150.0, 0, 1) ** 1.15 + far * 0.3 - (kinds == "reed") * 0.12
    haze = np.broadcast_to(haze, idx.shape)
    step = (haze * 1.7 > thd) & solid
    idx[step & (rows < 80)] = pal.index(WATER[1])
    idx[step & (rows >= 80)] = pal.index(WATER[2])
    idx[(haze > thd) & solid] = -1
    # the bases sit in the haze band on the floor horizon: the last rows dissolve (the floor layer overlaps them)
    idx[(np.broadcast_to(np.clip((rows - (MID_H - 5)) / 5.0, 0, 1), idx.shape) > th) & (idx >= 0)] = -1
    return R.to_rgba(idx, pal)


# ============================================================================ uw_lake_floor (768x120) - pseudo-perspective
FL_W, FL_H = 768, 120
CAM_H, FPX, R0 = 0.45, 177.0, 1.6
VIS = 3.6                                          # m: the mud melts into the green water


def make_floor():
    h, w = FL_H, FL_W
    rows = np.arange(h)[:, None] + 0.5
    cols = np.arange(w)[None, :] + 0.5
    dist = CAM_H * FPX / (rows + R0)                                  # metres to the floor point of each row
    xw = (cols - w / 2) * dist / FPX                                  # world x of each pixel
    th = R.bayer(h, w)
    rng = random.Random(9)
    pal = R.Pal(MUD + LEAF + [SHELL] + FOG)
    # base mud: soft lighter / darker patches, lighter near (sunlit), a faint caustic network in the first metres
    patch = E.fbm(xw / 1.3, dist / 1.3, 2.0)
    tone = 0.46 + (patch - 0.5) * 0.55 + np.clip((2.2 - dist) / 2.0, 0, 1) * 0.12
    cn = E.fbm(xw / 0.55, dist / 0.35, 7.0)
    ridge = 1.0 - np.abs(2 * cn - 1)
    caus = (ridge > 0.9) & (dist > 0.75) & (dist < 2.6)
    tone = tone + caus * 0.16
    lin = _ramp_rgb(np.broadcast_to(tone, (h, w)) * 0.8 + 0.1, MUD[1:5])
    # distance fog into the green water (the mud colour steps towards FOG and is gone by VIS)
    fog = np.clip((dist - 1.1) / (VIS - 1.1), 0, 1) ** 0.9
    fog_c = _ramp_rgb(np.broadcast_to(np.clip((dist - 1.1) / 6.0, 0, 1), (h, w)), FOG[::-1])
    lin = lin * (1 - fog[..., None]) + fog_c * fog[..., None]
    # flat tones with narrow dithered transitions (hybrid banding): Bayer squeezed into 0.3 .. 0.7
    thr = 0.5 + (th - 0.5) * 0.4
    idx, _ = R.quantize(R.l2s(lin).astype(np.float32), np.ones((h, w), bool), pal, dither=True, thr=thr)

    def fogged(hexc, d):
        f = min(1.0, max(0.0, (d - 1.1) / (VIS - 1.1))) ** 0.9
        fc = _ramp_rgb(np.array([min(1.0, max(0.0, (d - 1.1) / 6.0))]), FOG[::-1])[0]
        return _nearest(pal, R.s2l(R.hexrgb(hexc)) * (1 - f) + fc * f)

    # world-placed objects, projected (smaller towards the top): pebbles, fallen leaves, mussel shells
    items = []
    for _ in range(80):
        d = math.exp(rng.uniform(math.log(0.75), math.log(6.0)))
        items.append((d, rng.uniform(-2.2, 2.2) * d, "pebble", rng.uniform(0.02, 0.06) * (2.2 if rng.random() < 0.1 else 1.0),
                      rng.uniform(0, 6.28)))
    for _ in range(46):
        d = math.exp(rng.uniform(math.log(0.75), math.log(5.0)))
        items.append((d, rng.uniform(-2.2, 2.2) * d, "leaf", rng.uniform(0.05, 0.09), rng.uniform(0, 6.28)))
    for d, x, kind, s, ph in sorted(items, key=lambda q: -q[0]):
        rx = FPX * s / d
        if rx < 0.9:
            continue
        cy = CAM_H * FPX / d - R0
        cx = w / 2 + FPX * x / d
        ry = max(0.7, rx * CAM_H / d * (1.4 if kind == "pebble" else 1.0))
        y0, y1 = int(max(0, cy - ry * 2 - 2)), int(min(h, cy + ry + 3))
        x0, x1 = int(max(0, cx - rx * 1.6 - 2)), int(min(w, cx + rx * 1.6 + 3))
        if y1 <= y0 or x1 <= x0:
            continue
        yy = np.arange(y0, y1)[:, None] + 0.5
        xx = np.arange(x0, x1)[None, :] + 0.5
        sub = idx[y0:y1, x0:x1]
        if kind == "pebble":
            dy = (yy - cy) / np.where(yy > cy, ry * 0.6, ry)
            dx = (xx - cx) / rx
            ang = np.arctan2(dy, dx)
            body = dx ** 2 + dy ** 2 < (1 + 0.2 * np.sin(ang * 3 + ph)) ** 2
            if not body.any():
                continue
            top = body & ~R.shift(body, 1, 0, False)
            below = R.shift(body, 1, 0, False) & ~body
            sub[below] = fogged(MUD[0], d)
            sub[body] = fogged(MUD[2], d)
            sub[top] = fogged(MUD[5] if rx > 1.8 else MUD[4], d)
        else:                                                     # a fallen leaf: pointed ellipse, midrib
            ca, sa = math.cos(ph), math.sin(ph)
            u = ((xx - cx) * ca + (yy - cy) / (CAM_H / d * 1.3 + 0.25) * sa) / rx
            v = (-(xx - cx) * sa + (yy - cy) / (CAM_H / d * 1.3 + 0.25) * ca) / (rx * 0.42)
            body = (np.abs(v) < (1 - np.abs(u) ** 1.6)) & (np.abs(u) < 1)
            if not body.any():
                continue
            col = LEAF[rng.randrange(0, 2)]
            sub[body] = fogged(col, d)
            if rx > 2.5:
                sub[body & (np.abs(v) < 0.18)] = fogged(LEAF[2], d)
    # mussel-shell glints (1 px) in the lit near mud
    for _ in range(14):
        d = rng.uniform(0.8, 2.6)
        x = rng.uniform(-2.0, 2.0) * d
        cy, cx = int(CAM_H * FPX / d - R0), int(w / 2 + FPX * x / d)
        if 0 <= cy < h and 0 <= cx < w:
            idx[cy, cx] = pal.index(SHELL)
            if cy + 1 < h:
                idx[cy + 1, cx] = pal.index(MUD[0])
    img = R.to_rgba(idx, pal)
    # the far rows dissolve into the haze band (transparent), the nearest rows into the dark
    fade = np.clip((9.0 - rows) / 9.0, 0, 1) + np.clip((rows - (h - 16)) / 16.0, 0, 1)
    img[np.broadcast_to(fade > th, (h, w))] = 0.0
    return img


# ============================================================================ uw_lake_fore (256x128)
def make_fore():
    h, w = 128, 256
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    img = E.blank(h, w)
    th = R.bayer(h, w)
    body = np.zeros((h, w), bool)
    rim = np.zeros((h, w), bool)
    inner = np.zeros((h, w), bool)
    # broad out-of-focus blades from the bottom-left, bending right (x0 at the bottom, height, lean, width)
    for x0, H, bend, wid in ((10, 124, 70, 26), (-18, 96, 30, 30), (58, 70, 90, 20), (120, 40, 60, 14), (-30, 128, 8, 22)):
        u = np.clip((h - yy) / H, 0, 1.2)
        xc = x0 + bend * u ** 2
        half = wid * (1 - u ** 1.8) * (1 + 0.1 * np.sin(u * 7 + x0))
        m = (np.abs(xx - xc) < half) & (u <= 1.0)
        body |= m
        # sun from the top right: the right edge of each blade gets a faint rim
        rim |= m & (xx - xc > half - 2.2) & (u > 0.15)
        inner |= m & (np.abs(xx - xc) < half * 0.45) & (u < 0.8)
    E.put(img, body, FORE[1])
    E.put(img, inner & (E.fbm(xx / 24.0, yy / 24.0, 8.0) > 0.5), FORE[0])
    E.put(img, body & ~inner & (E.fbm(xx / 20.0, yy / 20.0, 4.0) > 0.6), FORE[2])
    E.put(img, rim & body, FORE[3])
    # soft (out-of-focus) edge: a dithered half ring outside the blades
    edge = R.dilate(body) & ~body & (th < 0.5)
    E.put(img, edge, FORE[1])
    return img


# ============================================================================ the set
def layers():
    return {"uw_lake_bg": make_bg(), "uw_lake_floor": make_floor(), "uw_lake_fore": make_fore(),
            "uw_lake_mid": make_mid()}


LILY = [".sss.", "sHB.s", "sBB..", "sBBBs", ".sss."]          # a lily pad, the notch facing right (out)


def frame():
    """enc_frame_lake: mossy wood, #a8e878 inner line, lily-pad corner studs (the notch faces outwards)."""
    img = E.make_frame(FRAME, [FRAME["pad"]] * 4, stud=LILY)
    n = 24
    pal = FRAME["pad"]
    for ci, cj, flip in ((0, 0, True), (0, n - 5, False), (n - 5, 0, True), (n - 5, n - 5, False)):
        img[ci:ci + 5, cj:cj + 5] = 0
        # restore the frame band under the stud cell, then draw the (mirrored) pad
        base = E.make_frame(FRAME, [FRAME["pad"]] * 4, stud=["....."] * 5)
        img[ci:ci + 5, cj:cj + 5] = base[ci:ci + 5, cj:cj + 5]
        for di, row in enumerate(LILY):
            for dj, ch in enumerate(row[::-1] if flip else row):
                if ch == ".":
                    continue
                c = {"s": pal[0], "B": pal[1], "H": pal[2]}[ch]
                img[ci + di, cj + dj, :3] = E.rgb(c)
                img[ci + di, cj + dj, 3] = 1.0
    return img
