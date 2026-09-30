"""
hybrid - the island world map (hub): fk_misc's island (terrain_h, anchors, markers, tilted ortho camera) repainted
with the hybrid kit - 16-bit overworld craft lit by the "sea" preset (bright late afternoon) with the key light
from the north-east (upper right, lifted): R.use_preset("sea", key_dir=(0.5, 0.4, 0.77)).

  * terrain: the fk_misc height field (imported read-only, same noise seed) as a fine mesh; every pixel is
    re-classified from its own world position (depth pass of the ortho camera -> terrain_h) and painted with
    curated, graded ramps (beach, grass + patches, forest floor, rock, snow, earth banks, swamp, ice) picked by
    the per-pixel Lambert of the key light, Bayer dither only at tone transitions; ridge contours on depth
    steps (hue-shifted darker far side) + a sun-side rim on the ridge tops;
  * ocean: the preset water_bands by DISTANCE FROM THE COAST (silver-cyan shallows -> deep blue), joined by
    horizontal dash dithering; a broken foam line on the shore, wet sand on the shadow side of the beach, lit
    sand on the sun side; sparse wave marks and a sparse far glitter patch in the north-east (sun side) ocean;
  * props (3D, palette-ramp materials, quantised): forests / pines, village, lake pier + rowboat, footbridge,
    harbour mole + lighthouse + wooden jetty + skiff, ice shanty + snowy pines, fishing boat offshore, swamp
    shrubs; rim on the OUTER sun-side silhouette, hue-shifted outer outline (never ink), 1 px ground shadow;
  * hand stamps (retro16 style): dead swamp trees, cave mouth with crystal glints, waterfall, ice hole, wake;
  * every marker spot (the 23x23 px pin footprint of MapScene + its bottom margin) is kept clear of props.

Outputs: _tmp/variants/hybrid/map_world.png (640x400 opaque, like Stages/map_world.png), map.json (same keys /
marker coordinates as Data/map.json), preview_map.png (game crop 480x270 at 2x, marker spots dotted).
Run: blender -b --python variants/hybrid/hyb_map.py
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
from mathutils.kdtree import KDTree  # noqa: E402
import fk_common as C  # noqa: E402
import fk_misc as M  # noqa: E402  (read only: terrain_h, anchors, MARKERS)

SID = "map"
# key from the north-east, lifted; rim on the sun side (right / top); glitter: a sparse patch in the NE ocean
PR = R.use_preset("sea", key_dir=(0.5, 0.4, 0.77), rim=dict(dirs=dict(r=1.0, tr=0.85, t=0.55)),
                  glitter=dict(dx=172, row0=72, row1=200, far=0.45, dens=0.2, width=(15.0, 30.0), maxlen=3))
R.WORK = os.path.join(R.OUT, "work", SID)        # scratch EXR passes of the map only (parallel runs never collide)
os.makedirs(R.WORK, exist_ok=True)
W, H, PPU = M.W, M.H, M.PPU                      # 640 x 400, 16 px per unit
G = R.grade_hex
LV = np.array(PR.key_dir, float) / np.linalg.norm(PR.key_dir)
CROP = (80, 65, 480, 270)                        # what PixelView shows (480x270 around the centre)

# ------------------------------------------------------------------ camera: identical to fk_misc.build_map
TILT = math.radians(38)
ORTHO = W / PPU * 1.4
CAM_LOC = (0.0, -40 * math.sin(TILT) - 0.8, 40 * math.cos(TILT))
PXU = W / ORTHO                                  # 11.43 px per world unit
ST, CT = math.sin(TILT), math.cos(TILT)

# ------------------------------------------------------------------ fk_misc terrain noise (same seed / phases)
_rnd = random.Random(7)
PH = [_rnd.uniform(0, 100) for _ in range(6)]


def noise(x, y):
    return (math.sin(x * 1.7 + PH[0]) * math.cos(y * 1.3 + PH[1]) + math.sin(x * 0.7 + y * 1.1 + PH[2]) * 0.6
            + math.sin(x * 2.9 - y * 2.3 + PH[3]) * 0.25) / 1.85


def vnoise(x, y):
    """numpy version of noise() (texture only)."""
    return (np.sin(x * 1.7 + PH[0]) * np.cos(y * 1.3 + PH[1]) + np.sin(x * 0.7 + y * 1.1 + PH[2]) * 0.6
            + np.sin(x * 2.9 - y * 2.3 + PH[3]) * 0.25) / 1.85


def TH(x, y):
    return M.terrain_h(float(x), float(y), noise)


def to_px(x, y, z):
    """world -> (col, row) top-down continuous pixel coords of the map camera."""
    v = (y + 0.8) * CT + z * ST
    return W / 2 + x * PXU, H / 2 - v * PXU


LAKE, ICE, SWAMP, HARBOR, CAVE, BOAT = M.LAKE, M.ICE, M.SWAMP, M.HARBOR, M.CAVE, M.BOAT
RIVER = M.RIVER


def river_dist(x, y):
    p = Vector((x, y))
    return min(M.seg_dist(p, RIVER[i], RIVER[i + 1]) for i in range(len(RIVER) - 1))


def marker_px():
    out = {}
    for sid, (x, y, z) in M.MARKERS.items():
        h, _ = TH(x, y)
        out[sid] = to_px(x, y, max(h, z))
    return out


PINS = marker_px()                                # marker pixel (col, row) per stage id


def in_pin(c, r, pad=2.0):
    """Inside a MapScene pin footprint (23x23 art px, bottom 4 px below the marker) + pad."""
    for mc, mr in PINS.values():
        if mc - 11.5 - pad <= c <= mc + 11.5 + pad and mr - 19.5 - pad <= r <= mr + 4.5 + pad:
            return True
    return False


def clear_of_pins(x, y, z0, z1, rad, pad=2.0):
    rp = rad * PXU
    for z in (z0, z1):
        c, r = to_px(x, y, z)
        for dc in (-rp, 0.0, rp):
            if in_pin(c + dc, r, pad):
                return False
    return True


# ------------------------------------------------------------------ curated palettes (graded with the preset)
WB = PR.water["bands"]                            # silver-cyan shallows -> deep blue
SEA = WB + [PR.water["dark"]]                     # coast -> open ocean (7 levels)
FOAM = "#e2eef0"
GLIT = PR.glitter["cols"]
SAND = G(["#b09468", "#dcc48e", "#f2e2b0"])       # wet / base / sunlit
GRASS = G(["#2c5a36", "#44783c", "#62a046", "#90c058"])    # dark / shade / base / lit
GRASS_P = G("#56903f")                             # meadow patches (between shade and base)
FORESTF = G(["#28503a", "#386a3c"])               # forest floor
ROCK = G(["#4c4a5c", "#6e6a72", "#948c86", "#bcb09c"])
SNOW = G(["#9aa6c8", "#ccd6ec", "#f2f4f8"])
EARTH = G(["#5a4438", "#7e6046"])                 # low banks (lake basin, river banks)
SWAMPG = G(["#384630", "#525e3a", "#6e764a"])     # swamp moss
SWAMPW = G(["#2c403c", "#46605a"])                # murky swamp pools (teal-olive, a notch below the moss)
SMIST = G("#a8aa8c")                              # the swamp stage's fog, as thin wisps
ICEC = G(["#98b2d4", "#c0d6ee"])                  # ice (+ SNOW[2] for drifts)
TREE = G(["#1c4234", "#2a5e3e", "#468240", "#74a64a"])
PINE = G(["#1a3a3a", "#265646", "#3a7450"])
WALL = G(["#a89480", "#e8dcc2"])
ROOF_R = G(["#943a34", "#cc5c48"])
ROOF_B = G(["#34508a", "#4c7cbc"])
ROOF_Y = G(["#a8763a", "#dca656"])
WOOD = G(["#4a3228", "#76543a", "#a47e50"])
DEAD = G(["#3a3034", "#948678"])
CAVEC = ["#161020", "#2c2238"]
CRYSTAL = R.PRESETS["cave"]["crystal"]            # the cave stage's crystal light colours
LAMP = "#fff0a4"

TERRAIN_HEX = (SEA + [FOAM] + SAND + GRASS + [GRASS_P] + FORESTF + ROCK + SNOW + EARTH + SWAMPG + SWAMPW + [SMIST]
               + ICEC + DEAD + CAVEC + CRYSTAL + [LAMP] + GLIT)
# ramps that can step one tone lighter (ridge-top rims, sheen) without adding colours
STEP_RAMPS = [SAND, GRASS, [FORESTF[0], FORESTF[1], GRASS[1], GRASS[2]], ROCK, SNOW, EARTH, SWAMPG, ICEC + [SNOW[2]],
              SEA[::-1]]
PROP_HEX = TREE + PINE + [EARTH[0]] + WALL + ROOF_R + ROOF_B + ROOF_Y + WOOD + ROCK + SNOW[1:] + SWAMPG + [LAMP]
# deepest hue-shifted tones for the dark side of the outlines (so Pal.darker never falls back to near-black ink)
DEEP = G(["#0f2824", "#2a1a1e"])                  # deep teal-green (foliage), deep plum-brown (wood, roofs)
ALL_HEX = TERRAIN_HEX + PROP_HEX + DEEP

# terrain classes
C_SEA, C_SAND, C_GRASS, C_FOREST, C_ROCK, C_SNOW, C_LAKE, C_RIVER, C_SWAMP, C_SWAMPW, C_ICE, C_BANK, C_SHORE = range(13)
KCODE = {"land": 0, "lake": 1, "lakeshore": 2, "river": 3, "swamp": 4, "ice": 5}
WATER_C = (C_SEA, C_LAKE, C_RIVER, C_SWAMPW)

# ------------------------------------------------------------------ terrain grid (fk_misc.terrain_h)
GS = 0.1
GX = np.round(np.arange(-21.0, 21.0 + 1e-6, GS), 4)
GY = np.round(np.arange(-19.0, 17.0 + 1e-6, GS), 4)


def terrain_grid():
    ny, nx = len(GY), len(GX)
    hr = np.zeros((ny, nx))
    kd = np.zeros((ny, nx), np.int8)
    for j, y in enumerate(GY):
        for i, x in enumerate(GX):
            h, k = TH(x, y)
            hr[j, i] = h
            kd[j, i] = KCODE[k]
    hs = np.where((kd == 0) & (hr < 0), 0.0, hr)       # visible surface: the sea is flat at 0 (as in fk_misc)
    return hr, hs, kd


def terrain_mesh(hs):
    ny, nx = hs.shape
    X, Y = np.meshgrid(GX, GY)
    verts = np.stack([X.ravel(), Y.ravel(), hs.ravel()], 1)
    j, i = np.mgrid[0:ny - 1, 0:nx - 1]
    a = (j * nx + i).ravel()
    faces = np.stack([a, a + 1, a + nx + 1, a + nx], 1)
    ob = R.mesh_from("Terrain", verts.tolist(), faces.tolist(), R.m_flat(GRASS[2]), smooth=False)
    return R.tagk(ob, "terrain")


# ================================================================== props (3D, kit materials)
PROP_KINDS = ["tree", "pine", "house", "lighthouse", "mole", "pier", "boat", "shanty", "bridge", "shrub"]


def cone(name, x, y, z, r, h, mat, verts=8, rot=0.0):
    ob = C.add_prim("cone", name, mat, vertices=verts, radius1=r, radius2=0.0, depth=h,
                    location=(x, y, z + h / 2), rotation=(0, 0, rot))
    return ob


def blob(name, c, r, mat):
    ob = C.add_prim("ico", name, mat, radius=1.0, location=(0, 0, 0), subdivisions=2)
    ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    C.set_smooth(ob)
    return ob


class Mats:
    pass


def make_mats():
    mt = R.m_tone
    m = Mats()
    m.crown = mt(TREE, [0.34, 0.58, 0.82], noise=0.08, nscale=4.0, ndetail=1.0, name="Crown")
    m.pine = mt(PINE, [0.42, 0.7], name="Pine")
    m.spine = mt(PINE[:2] + [SNOW[1]], [0.45, 0.8], name="SnowPine")
    m.cap = mt(SNOW[1:], [0.5], name="Cap")
    m.trunk = R.m_flat(EARTH[0])
    m.wall = mt(WALL, [0.28], name="Wall")         # front / gable pale, only the west faces in shade
    m.roofs = [mt(ROOF_R, [0.6], name="RoofR"), mt(ROOF_B, [0.6], name="RoofB"), mt(ROOF_Y, [0.6], name="RoofY")]
    m.wood = mt(WOOD, [0.42, 0.72], pats=((0, 0.2, 0.3, -0.25, "world"),), name="Planks")
    m.woodv = mt(WOOD, [0.42, 0.72], pats=((1, 0.2, 0.3, -0.25, "world"),), name="PlanksV")
    m.post = mt(WOOD[:2], [0.6], name="Post")
    m.stone = mt(ROCK, [0.3, 0.55, 0.8], noise=0.1, nscale=6.0, name="Stone")
    # lighthouse: red / white bands (stripes push the ramp into the red half)
    m.lh = mt([ROOF_R[0], ROOF_R[1], WALL[0], WALL[1]], [0.25, 0.5, 0.64], lam=0.5, bias=0.5,
              pats=((2, 0.44, 0.36, -0.5, "world", 0.1),), name="LH")
    m.lamp = R.m_flat(LAMP)
    m.hull = mt(WALL, [0.5], name="Hull")
    m.cabin = mt(ROOF_B, [0.55], name="Cabin")
    m.shanty = mt(ROOF_R, [0.55], name="Shanty")
    m.shrub = mt(SWAMPG, [0.4, 0.72], noise=0.1, nscale=5.0, name="Shrub")
    return m


def tree(x, y, rnd, m, grp, pine=False, snow=False):
    h, _ = TH(x, y)
    s = rnd.uniform(0.27, 0.37)
    if pine:
        mat = m.spine if snow else m.pine
        R.tagk(cone("Pine", x, y, h - 0.05, s * 1.0, s * 2.1, mat, 8, rnd.uniform(0, 1)), "pine", grp)
        R.tagk(cone("Pine", x, y, h + s * 1.05, s * 0.72, s * 1.7, mat, 8, rnd.uniform(0, 1)), "pine", grp)
        if snow:
            R.tagk(cone("Cap", x, y, h + s * 2.0, s * 0.36, s * 0.8, m.cap, 8), "pine", grp)
    else:
        R.tagk(R.loft("Trunk", [(x, y, h - 0.05), (x, y, h + s * 1.0)], s * 0.16, m.trunk, 6), "tree", grp)
        r = s * rnd.uniform(0.9, 1.05)
        R.tagk(blob("Crown", (x, y, h + s * 1.55), (r * 1.05, r, r * 0.92), m.crown), "tree", grp)
        if rnd.random() < 0.45:
            dx = rnd.choice((-1, 1)) * r * 0.6
            R.tagk(blob("Crown", (x + dx, y - 0.05, h + s * 1.25), (r * 0.7, r * 0.7, r * 0.62), m.crown), "tree", grp)
    return h


def house(x, y, rnd, m, roof, grp):
    """Cottage with the gable towards the viewer: lit east roof slope, shaded west slope, pale gable wall."""
    h, _ = TH(x, y)
    R.tagk(R.box("Walls", (x, y, h + 0.24), (0.6, 0.56, 0.5), m.wall), "house", grp)
    z0, top = h + 0.49, h + 0.98
    hw, hl = 0.4, 0.36
    v = [(x - hw, y - hl, z0), (x + hw, y - hl, z0), (x + hw, y + hl, z0), (x - hw, y + hl, z0),
         (x, y - hl, top), (x, y + hl, top)]
    f = [(0, 4, 5, 3), (1, 2, 5, 4), (3, 5, 2), (0, 3, 2, 1)]
    ob = R.mesh_from("Roof", v, f, roof, smooth=False)
    R._fix_normals(ob)
    R.tagk(ob, "house", grp)
    gv = [(x - hw + 0.06, y - hl + 0.02, z0), (x + hw - 0.06, y - hl + 0.02, z0), (x, y - hl + 0.02, top - 0.07)]
    ob = R.mesh_from("Gable", gv, [(0, 1, 2)], m.wall, smooth=False)
    R.tagk(ob, "house", grp)
    HOUSE_DOORS.append((x + 0.08, y - 0.29, h + 0.02))


HOUSE_DOORS = []


VILLAGE = [(-6.8, -3.4, 0), (-7.8, -2.4, 1), (-6.6, -2.3, 2), (-7.6, -4.2, 1), (-5.5, -4.15, 0)]
# lake pier on the WEST shore (towards the village), left of the lake pin: the south shore is hidden by the
# near rim of the lake basin in this 3/4 view
LAKE_PIER = (-6.0, -4.75, -1.05)                  # x (shore end), x (lake end), y
JETTY = [(-10.15, -7.45), (-11.5, -7.9)]          # beach -> sea
MOLE = [(-8.2, -8.85), (-8.7, -9.45), (-9.5, -9.85)]   # stone breakwater, lighthouse at the end
BRIDGE = (-0.7, 2.25)
SHANTY = (-6.75, 5.75)
OCEAN_BOAT = (16.9, -4.5)


def props(rnd, m):
    gid = [0]
    HOUSE_DOORS.clear()

    def g(p):
        gid[0] += 1
        return f"{p}{gid[0]}"

    # ---- village next to the lake (hue-shifted outlines added in post)
    for (x, y, k) in VILLAGE:
        house(x, y, rnd, m, m.roofs[k], g("house"))
    # ---- lake: plank pier from the west shore + a moored rowboat
    x0, x1, py = LAKE_PIER
    gp = g("pier")
    R.tagk(R.box("Pier", ((x0 + x1) / 2, py, 0.36), (x1 - x0, 0.28, 0.06), m.wood), "pier", gp)
    for xx in (x0 + 0.15, (x0 + x1) / 2, x1 - 0.05):
        for sy in (-0.13, 0.13):
            R.tagk(R.box("Post", (xx, py + sy, 0.3), (0.05, 0.05, 0.2), m.post), "pier", gp)
    R.tagk(R.ellipsoid("Row", (x1 - 0.2, py - 0.42, 0.3), (0.38, 0.16, 0.1), m.post), "boat", g("row"))
    # ---- stream: footbridge across the river
    bx, by = BRIDGE
    bh, _ = TH(bx, by)
    rot = Matrix.Rotation(math.atan2(RIVER[2].y - RIVER[1].y, RIVER[2].x - RIVER[1].x), 4, "Z")
    R.tagk(R.box("Bridge", (bx, by, bh + 0.14), (0.3, 1.25, 0.07), m.wood, rot=rot), "bridge", g("bridge"))
    # ---- sea (harbour): stone mole + lighthouse, wooden jetty, a skiff
    gm = g("mole")
    pts = [(x, y, 0.12) for x, y in MOLE]
    R.tagk(R.loft("Mole", pts, [(0.2, 0.14)] * len(pts), m.stone, 8), "mole", gm)
    lx, ly = MOLE[-1]
    gl = g("lh")
    R.tagk(R.loft("LH", [(lx, ly, 0.1), (lx, ly, 1.3)], [0.21, 0.15], m.lh, 12), "lighthouse", gl)
    R.tagk(R.box("Lamp", (lx, ly, 1.42), (0.24, 0.24, 0.24), m.lamp), "lighthouse", gl)
    R.tagk(cone("LHCap", lx, ly, 1.54, 0.22, 0.26, m.roofs[0], 10), "lighthouse", gl)
    (ax, ay), (bx2, by2) = JETTY
    gj = g("jetty")
    ang = math.atan2(by2 - ay, bx2 - ax)
    ln = math.hypot(bx2 - ax, by2 - ay)
    rj = Matrix.Rotation(ang, 4, "Z")
    R.tagk(R.box("Jetty", ((ax + bx2) / 2, (ay + by2) / 2, 0.3), (ln, 0.28, 0.06), m.wood, rot=rj), "pier", gj)
    for t in (0.25, 0.6, 0.95):
        for sy in (-0.13, 0.13):
            ox, oy = -math.sin(ang) * sy, math.cos(ang) * sy
            R.tagk(R.box("JPost", (ax + (bx2 - ax) * t + ox, ay + (by2 - ay) * t + oy, 0.24), (0.05, 0.05, 0.2),
                         m.post), "pier", gj)
    sk = (ax + (bx2 - ax) * 0.55 - 0.05, ay + (by2 - ay) * 0.55 - 0.4)
    R.tagk(R.ellipsoid("Skiff", (sk[0], sk[1], 0.08), (0.38, 0.15, 0.1), m.hull,
                       rot=Matrix.Rotation(ang, 4, "Z")), "boat", g("skiff"))
    # ---- ice: shanty on the frozen lake
    sx, sy = SHANTY
    gs_ = g("shanty")
    R.tagk(R.box("Shanty", (sx, sy, 2.4 + 0.22), (0.46, 0.4, 0.44), m.shanty), "shanty", gs_)
    v = [(sx - 0.29, sy - 0.26, 2.84), (sx + 0.29, sy - 0.26, 2.84), (sx + 0.29, sy + 0.26, 2.84),
         (sx - 0.29, sy + 0.26, 2.84), (sx - 0.29, sy, 3.08), (sx + 0.29, sy, 3.08)]
    f = [(0, 1, 5, 4), (3, 4, 5, 2), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]
    ob = R.mesh_from("SRoof", v, f, m.post, smooth=False)
    R._fix_normals(ob)
    R.tagk(ob, "shanty", gs_)
    # ---- ocean: fishing boat offshore east (right of the pin)
    ox, oy = OCEAN_BOAT
    gb = g("boat")
    R.tagk(R.box("Hull", (ox, oy, 0.1), (1.2, 0.42, 0.22), m.hull, bevel=0.08), "boat", gb)
    R.tagk(R.box("Cabin", (ox - 0.22, oy, 0.35), (0.4, 0.3, 0.28), m.cabin), "boat", gb)
    R.tagk(R.box("Mast", (ox + 0.18, oy, 0.62), (0.06, 0.06, 0.62), m.post), "boat", gb)
    # ---- swamp shrubs (low, olive) around the edge
    for k in range(9):
        a = rnd.uniform(0, 6.28)
        d = rnd.uniform(1.6, 3.0)
        x, y = SWAMP.x + math.cos(a) * d * 1.1, SWAMP.y + math.sin(a) * d
        h, kind = TH(x, y)
        if kind != "swamp" or not clear_of_pins(x, y, h, h + 0.5, 0.35, 4):
            continue
        r = rnd.uniform(0.22, 0.34)
        R.tagk(blob("Shrub", (x, y, h + r * 0.4), (r * 1.3, r, r * 0.75), m.shrub), "shrub", g("shrub"))
    # ---- forests: clustered broadleaf woods on the lowland, conifers higher up
    n = 0
    step = 0.44
    for gy in np.arange(-10.5, 9.8, step):
        for gx in np.arange(-14.5, 14.5, step):
            x = gx + rnd.uniform(-0.19, 0.19)
            y = gy + rnd.uniform(-0.19, 0.19)
            h, kind = TH(x, y)
            if kind != "land" or not 0.5 < h < 2.75:
                continue
            p = Vector((x, y))
            if ((p - HARBOR).length < 2.6 or (p - LAKE).length < 3.0 or river_dist(x, y) < 0.8
                    or (p - SWAMP).length < 3.5 or (p - ICE).length < 2.75):
                continue
            # nothing tall just south of the lake: tree tops would hide the near shore and the pier
            if y < LAKE.y and abs(x - LAKE.x) < 2.9 and y > LAKE.y - 4.4:
                continue
            if any(math.hypot(x - vx, y - vy) < 1.0 for vx, vy, _ in VILLAGE):
                continue
            if (p - CAVE).length < 1.6 or (x > 13.2 and y < -2.4):
                continue
            wood = vnoise(x * 0.45 + 11.0, y * 0.45 - 3.0)
            if h >= 1.4:
                dens = 0.85 if h < 2.3 else 0.35
            else:
                dens = 0.82 if wood > 0.12 else (0.25 if wood > -0.1 else 0.05)
            if rnd.random() > dens:
                continue
            if not clear_of_pins(x, y, h, h + 1.0, 0.4, 4):
                continue
            pine = h > 1.55 or rnd.random() < 0.25
            tree(x, y, rnd, m, g("t"), pine=pine)
            n += 1
    # ---- snowy pines around the frozen lake + a few on the lower mountain flanks
    k = 0
    while k < 70:
        k += 1
        a = rnd.uniform(0, 6.28)
        d = rnd.uniform(2.75, 3.6)
        x, y = ICE.x + math.cos(a) * d * 1.05, ICE.y + math.sin(a) * d * 0.85
        h, kind = TH(x, y)
        if kind != "land" or h > 3.4 or not clear_of_pins(x, y, h, h + 1.0, 0.4, 4):
            continue
        tree(x, y, rnd, m, g("sp"), pine=True, snow=True)
        n += 1
    print("HYB props trees", n)


# ================================================================== render + post
def world_from_depth(depth, a):
    cols = np.arange(W)[None, :] + 0.5
    rows = np.arange(H)[:, None] + 0.5
    U = np.broadcast_to((cols - W / 2) / PXU, (H, W))
    V = np.broadcast_to((H / 2 - rows) / PXU, (H, W))
    d = np.where(a, depth, 40.0).astype(np.float64)
    Y = V * CT + (d - 40.0) * ST - 0.8
    Z = V * ST - (d - 40.0) * CT
    Y = np.where(a, Y, V / CT - 0.8)                 # empty (beyond the mesh): the sea plane z = 0
    Z = np.where(a, Z, 0.0)
    return np.array(U), Y, Z


def sample_terrain(X, Y):
    xf, yf = X.ravel(), Y.ravel()
    h = np.empty(len(xf))
    k = np.empty(len(xf), np.int8)
    for i in range(len(xf)):
        hh, kk = M.terrain_h(float(xf[i]), float(yf[i]), noise)
        h[i] = hh
        k[i] = KCODE[kk]
    return h.reshape(X.shape), k.reshape(X.shape)


def face_normals(hs, X, Y):
    """Normal of the grid quad under each pixel (flat per quad: crisp facets, exact walls)."""
    ny, nx = hs.shape
    fx = np.clip((X - GX[0]) / GS, 0, nx - 1.001)
    fy = np.clip((Y - GY[0]) / GS, 0, ny - 1.001)
    i0 = fx.astype(int)
    j0 = fy.astype(int)
    dx = ((hs[j0, i0 + 1] - hs[j0, i0]) + (hs[j0 + 1, i0 + 1] - hs[j0 + 1, i0])) / (2 * GS)
    dy = ((hs[j0 + 1, i0] - hs[j0, i0]) + (hs[j0 + 1, i0 + 1] - hs[j0, i0 + 1])) / (2 * GS)
    n = np.stack([-dx, -dy, np.ones_like(dx)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n


BY = R.bayer(H, W)


def pick(lam, bounds, soft=0.05):
    """Tone index along a ramp from the Lambert value: flat bands, Bayer only across a narrow transition."""
    v = np.zeros_like(lam)
    for b in bounds:
        v += np.clip((lam - b) / soft + 0.5, 0, 1)
    k = np.floor(v).astype(int)
    fr = v - k
    dith = (fr > 0.02) & (fr < 0.98)
    k = k + ((fr > BY) & dith)
    return np.clip(k, 0, len(bounds)), dith


def coast_kdtree(hr, kd):
    sea = (kd == 0) & (hr < 0)
    land = ~sea
    edge = sea & (R.shift(land, 1, 0) | R.shift(land, -1, 0) | R.shift(land, 0, 1) | R.shift(land, 0, -1))
    js, is_ = np.nonzero(edge)
    tree_ = KDTree(len(js))
    for n_, (j, i) in enumerate(zip(js, is_)):
        tree_.insert((float(GX[i]), float(GY[j]), 0.0), n_)
    tree_.balance()
    return tree_


def band_by_dist(d, zones, rng, lmin=2, lmax=6):
    """Band index by distance: level i -> i+1 across zones[i] = (d0, d1), horizontal dash dithering."""
    thr = R.dash_threshold(H, W, rng, lmin, lmax)
    k = np.zeros(d.shape, int)
    for i, (a, b) in enumerate(zones):
        t = np.clip((d - a) / (b - a), 0, 1)
        k = np.where(d >= b, i + 1, np.where((d >= a) & (t > thr), i + 1, k))
    return k


def nb_any(m):
    return R.shift(m, 1, 0) | R.shift(m, -1, 0) | R.shift(m, 0, 1) | R.shift(m, 0, -1)


def stamp(idx, pal, pattern, cmap, c0, r0, only=None):
    for j, ln in enumerate(pattern):
        for i, ch in enumerate(ln):
            y, x = r0 + j, c0 + i
            if ch in cmap and 0 <= y < H and 0 <= x < W and (only is None or only[y, x]):
                idx[y, x] = pal.index(cmap[ch])


# hand stamps (retro16 style): light from the right -> D = shaded bark (left), L = lit bark (right)
DEAD_A = ["D......L",
          ".D....L.",
          "..D..L..",
          "...DL...",
          "D..DL...",
          ".D.DL.LL",
          "..DDLL..",
          "...DL...",
          "...DL...",
          "...DL...",
          "..DDLL.."]
DEAD_B = ["..D....",
          "...D..L",
          "D..DL.L",
          ".D.DLL.",
          "..DDL..",
          "...DL..",
          "...DL..",
          "...DL..",
          "..DDLL."]
CAVE_ST = ["...rooor...",
           ".rrkkkkkoo.",
           ".rkkkkkkkko",
           "rkkkkmkkkko",
           "rkkkkkkkkko",
           "rkckkkkvkko",
           "rkcckmkvvko",
           "rrmmmmmmmro"]
HOLE_ST = [".dd.",
           "dwwd",
           ".dd."]


def build_scene(rnd):
    C.reset_scene()
    R.reset_materials()
    sc = bpy.context.scene
    cd = bpy.data.cameras.new("MapCam")
    cam = bpy.data.objects.new("MapCam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    cd.type = "ORTHO"
    cd.ortho_scale = ORTHO
    cd.clip_end = 200
    cam.rotation_euler = (TILT, 0, 0)
    cam.location = CAM_LOC
    sc.render.resolution_x, sc.render.resolution_y = W, H
    hr, hs, kd = terrain_grid()
    terrain_mesh(hs)
    props(rnd, make_mats())
    bpy.context.view_layer.update()
    return hr, hs, kd


def markers_json():
    """Marker positions exactly like fk_misc.build_map (world -> pixel through the SAME camera)."""
    out = []
    for sid, (x, y, z) in M.MARKERS.items():
        h, _ = TH(x, y)
        px, py = C.world_to_pixel((x, y, max(h, z)))
        out.append({"id": sid, "x": round((px - W / 2) / PPU, 3), "y": round((py - H / 2) / PPU, 3)})
    return out


def relief_grid(hs, kd):
    """Shading-only detail height on the mountains: downhill gullies and spurs, so the south faces break into
    lit (east-facing) and shaded (west-facing) facets instead of one flat band. The geometry is unchanged."""
    Xg, Yg = np.meshgrid(GX, GY)
    fade = np.clip((hs - 1.9) / 0.9, 0, 1) * ~np.isin(kd, (1, 3, 5))
    det = 0.45 * (1 - np.abs(vnoise(Xg * 2.7 + 1.3, Yg * 0.7 - 2.0))) + 0.15 * vnoise(Xg * 5.3 + 4.0, Yg * 1.6)
    return hs + fade * det


def pin_mask_px(pad_x=14, pad_top=22, pad_bot=7):
    m = np.zeros((H, W), bool)
    for mc, mr in PINS.values():
        m[max(0, int(mr - pad_top)):int(mr + pad_bot), max(0, int(mc - pad_x)):int(mc + pad_x + 1)] = True
    return m


def render_map():
    rnd = random.Random(41)
    hr, hs, kd = build_scene(rnd)
    markers = markers_json()
    ps = R.render_passes("map_scene")
    kid = R.kind_map(ps)
    a = ps["a"]
    terr = (kid == "terrain") | ~a
    propm = a & ~terr
    X, Y, Z = world_from_depth(ps["depth"], a)
    Ht, Kt = sample_terrain(X, Y)
    nz = face_normals(hs, X, Y)[..., 2]                  # geometric steepness
    N = face_normals(relief_grid(hs, kd), X, Y)          # shading normal (+ mountain gullies)
    lam = (N * LV[None, None, :]).sum(-1)
    pal = R.Pal(ALL_HEX)
    P = lambda h: pal.index(h)                          # noqa: E731
    pinm = pin_mask_px()
    # one-step lighter / darker along the curated ramps (ridge rims, contours, shadows, sheen: no new colours)
    up_lut = np.arange(len(pal))
    dn_lut = np.arange(len(pal))
    for ramp in STEP_RAMPS:
        for i in range(len(ramp) - 1):
            up_lut[P(ramp[i])] = P(ramp[i + 1])
            dn_lut[P(ramp[i + 1])] = P(ramp[i])
    for i in range(len(pal)):
        if dn_lut[i] == i:
            dn_lut[i] = pal.darker(i, 1)

    # ---------------------------------------------------------- classes (per pixel, from its own world position)
    cls = np.full((H, W), C_GRASS, int)
    land = Kt == 0
    steep = nz < 0.52
    dice = np.hypot((X - ICE.x) / 4.3, (Y - ICE.y) / 3.3)     # snowy north: the frozen lake's surroundings
    cls[land & (Ht < 0)] = C_SEA
    cls[land & (Ht >= 0) & (Ht < 0.42)] = C_SAND
    cls[land & (Ht >= 0.42) & (Ht < 1.4)] = C_GRASS
    cls[land & (Ht >= 1.4) & (Ht < 2.3)] = C_FOREST
    cls[land & (Ht >= 2.3)] = C_ROCK
    cls[land & ((Z >= 3.4) | ((Ht >= 1.1) & (dice < 1.0)))] = C_SNOW
    cls[Kt == 1] = C_LAKE
    cls[Kt == 2] = C_SHORE
    cls[Kt == 3] = C_RIVER
    sw = Kt == 4
    cls[sw] = C_SWAMP
    cls[sw & (vnoise(X * 2.3, Y * 2.3) > -0.05)] = C_SWAMPW
    cls[Kt == 5] = C_ICE
    # steep faces: rock high up / around the ice plateau (snow stays on the less steep ones), grassy banks low down
    wall = steep & (cls != C_SEA) & (Z > 0.05)
    hi = (Z > 2.0) | (dice < 1.0)
    cls[wall & hi & ~((cls == C_SNOW) & (nz > 0.4))] = C_ROCK
    cls[wall & ~hi] = C_BANK
    cls[~terr] = -1
    waterc = np.isin(cls, WATER_C)

    # ---------------------------------------------------------- terrain tones (curated ramps x key Lambert)
    idx = np.full((H, W), -1, int)
    dithm = np.zeros((H, W), bool)

    def paint(mask, ramp, bounds, soft=0.025):
        nonlocal dithm
        k, d = pick(lam, bounds, soft)
        lut = np.array([P(c) for c in ramp])
        idx[mask] = lut[k[mask]]
        dithm |= mask & d

    paint(np.isin(cls, (C_SAND, C_SHORE)), SAND, [0.42, 0.9])
    grass = cls == C_GRASS
    paint(grass, GRASS, [0.36, 0.64, 0.875])
    idx[grass & (vnoise(X * 2.3, Y * 2.3) < -0.22) & (idx == P(GRASS[2]))] = P(GRASS_P)   # meadow patches
    paint(cls == C_FOREST, [FORESTF[0], FORESTF[1], GRASS[1], GRASS[2]], [0.4, 0.66, 0.9])
    paint(cls == C_ROCK, ROCK, [0.3, 0.56, 0.82], 0.04)
    paint(cls == C_SNOW, SNOW, [0.42, 0.76], 0.04)
    # banks (lake basin, river valley, swamp rim): grassy slope with a 2 px earth lip above the water / beach
    wet = waterc | np.isin(cls, (C_SHORE, C_SAND, C_SWAMP))
    lip = R.shift(wet, -1, 0) | R.shift(wet, -2, 0)
    bank = cls == C_BANK
    paint(bank & lip, EARTH, [0.5])
    paint(bank & ~lip, [FORESTF[0], GRASS[0], GRASS[1]], [0.3, 0.62])
    paint(cls == C_SWAMP, SWAMPG, [0.5, 0.86])
    idx[cls == C_SWAMPW] = P(SWAMPW[0])
    # ice: flat pale ice, a few hairline cracks, wind-blown snow drifts
    ice = cls == C_ICE
    idx[ice] = P(ICEC[1])
    idx[ice & (np.abs(vnoise(X * 3.1 + 2.0, Y * 3.4 - 1.0)) < 0.035)] = P(ICEC[0])
    idx[ice & (vnoise(X * 1.7 - 4.0, Y * 2.2 + 3.0) > 0.45)] = P(SNOW[2])
    idx = np.where(terr, idx, -1)
    idx = R.despeckle(idx, None, protect=dithm | ~terr, passes=1)     # class-boundary orphans only

    # ---------------------------------------------------------- water
    rng = random.Random(5)
    sea = terr & (cls == C_SEA)
    kdt = coast_kdtree(hr, kd)
    dist = np.zeros((H, W))
    for r, c in np.argwhere(sea):
        dist[r, c] = kdt.find((float(X[r, c]), float(Y[r, c]), 0.0))[2]
    dist += 0.22 * vnoise(X * 1.1 + 5.0, Y * 1.1)
    # water_bands by distance from the coast (world units): shallows -> deep, dash-dithered hand-overs
    zones = [(0.28, 0.6), (0.95, 1.45), (1.9, 2.55), (3.0, 3.8), (4.4, 5.4), (6.2, 7.6)]
    band = band_by_dist(dist, zones, rng, 2, 6)
    sea_lut = np.array([P(c) for c in SEA])
    idx[sea] = sea_lut[band[sea]]
    lake = cls == C_LAKE
    ld = 2.3 - np.hypot(X - LAKE.x, Y - LAKE.y)
    lb = band_by_dist(ld, [(0.12, 0.3), (0.55, 0.85), (1.3, 1.7)], rng, 2, 5)
    lut = np.array([P(c) for c in (WB[1], WB[2], WB[3], WB[4])])
    idx[lake] = lut[lb[lake]]
    riv = cls == C_RIVER
    rd = np.full((H, W), 9.0)
    for r, c in np.argwhere(riv):
        rd[r, c] = river_dist(float(X[r, c]), float(Y[r, c]))
    idx[riv] = P(WB[1])
    idx[riv & (rd < 0.2)] = P(WB[2])
    # ---- shore lines: broken foam on the sea side; lit sand on the sun side (N / E edges), wet sand elsewhere
    dry = terr & ~waterc
    foam = sea & nb_any(dry)
    fm = np.stack([R.dash_mask(W, 0.78, 3, 9, rng) for _ in range(H)])
    idx[foam & fm] = P(FOAM)
    shore = dry & nb_any(sea)
    sand_like = np.isin(cls, (C_SAND, C_SHORE))
    lit_side = R.shift(sea, 1, 0) | R.shift(sea, 0, -1)
    idx[shore & sand_like & lit_side] = P(SAND[2])
    idx[shore & sand_like & ~lit_side] = P(SAND[0])
    lrim = np.isin(cls, (C_LAKE, C_RIVER)) & R.shift(dry, -1, 0)          # the near (south) lake / river edge
    idx[lrim] = P(WB[0])
    # ---- swamp pools: dark murky water, a lighter near edge, sparse sky dashes
    pool = cls == C_SWAMPW
    idx[pool & R.shift(cls == C_SWAMP, -1, 0)] = P(SWAMPW[1])
    sky = pool & np.stack([R.dash_mask(W, 0.1, 2, 4, rng) for _ in range(H)]) & ~R.shift(~pool, 1, 0)
    idx[sky] = P(SWAMPW[1])

    # ---------------------------------------------------------- ridge contours + sun-side ridge-top rims
    depth = np.where(a, ps["depth"], 1e9)
    above = R.shift(depth, 1, 0, 1e9)
    ridge_far = terr & R.shift(terr, -1, 0) & (depth - R.shift(depth, -1, 0, 0.0) > 0.6) & ~waterc
    idx[ridge_far] = dn_lut[idx[ridge_far]]
    ridge_top = terr & (above - depth > 0.6) & ~waterc & (lam > 0.55)
    idx[ridge_top] = up_lut[idx[ridge_top]]

    # ---------------------------------------------------------- props: quantise, contours, shadow, rim, outline
    ppal = R.Pal(PROP_HEX + DEEP)
    pidx, solid = R.quantize(ps["rgb"], propm, ppal, dither=True)
    pidx = R.remove_specks(pidx, 3)
    pidx = R.despeckle(pidx, ps["id"], protect=~solid, passes=1)
    pidx = R.inner_lines(pidx, ppal, ps["id"], ps["depth"], thr=0.25, rel=0.0, steps=1)
    lutp = np.array([pal.index(h) for h in ppal.hex])
    gi = np.where(pidx >= 0, lutp[np.maximum(pidx, 0)], -1)
    pm = gi >= 0
    shadow = terr & ~pm & R.shift(pm, 1, -1) & (idx >= 0)                # 1 px ground shadow, down-left
    idx[shadow] = dn_lut[idx[shadow]]
    gi, pal = R.rim_light(gi, pal, PR, mask=pm, levels=1, snap=0.035)
    ol = R.outer_outline(gi, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    newline = (gi < 0) & (ol >= 0)
    idx = np.where(gi >= 0, gi, idx)
    idx[newline] = ol[newline]
    olab = pal.lab[np.unique(ol[newline])]
    ol_stats = (round(float(olab[:, 0].min()), 3), int(((olab[:, 0] < 0.2) & (np.hypot(olab[:, 1], olab[:, 2]) < 0.03)).sum()))
    # fill the few pixels of removed prop specks from their neighbours
    for _ in range(4):
        hole = idx < 0
        if not hole.any():
            break
        for dy, dx in ((-1, 0), (1, 0), (0, 1), (0, -1)):
            nbv = R.shift(idx, dy, dx, -1)
            idx = np.where((idx < 0) & (nbv >= 0), nbv, idx)

    # ---------------------------------------------------------- 16-bit touches on the ground
    trng = np.random.default_rng(3)
    base_g = np.isin(idx, (P(GRASS[2]), P(GRASS_P))) & terr
    ok = base_g & R.shift(base_g, 0, 1) & R.shift(base_g, 0, -1) & R.shift(base_g, 1, 0) & ~pinm
    for r, c in np.argwhere(ok & (trng.random((H, W)) < 0.011)):
        idx[r, c - 1] = idx[r, c + 1] = idx[r - 1, c] = P(GRASS[1])          # grass tuft "^"
    for (x, y, z) in HOUSE_DOORS:                                              # a door in every gable
        c, r = to_px(x, y, z)
        for dr in (1, 2):
            if pm[int(r) - dr, int(c)]:
                idx[int(r) - dr, int(c)] = P(WOOD[0])
    # swamp fog wisps (the swamp stage's mood): two thin dash bands, never on props or the pin
    swr_ = np.flatnonzero((Kt == 4).any(1))
    if len(swr_):
        r0, r1 = swr_.min(), swr_.max()
        mrng = random.Random(13)
        for rc_, amp in ((r0 + 0.3 * (r1 - r0), 0.42), (r0 + 0.72 * (r1 - r0), 0.34)):
            for r in range(int(rc_) - 3, int(rc_) + 4):
                dens = amp * math.exp(-((r - rc_) / 1.8) ** 2)
                wm = R.dash_mask(W, dens, 3, 10, mrng) & (Kt[r] == 4) & terr[r] & ~pinm[r]
                idx[r, wm] = P(SMIST)

    # ---------------------------------------------------------- hand stamps (landmarks)
    drng = random.Random(9)
    placed = 0
    for k in range(500):
        if placed >= 7:
            break
        a_ = drng.uniform(0, 6.28)
        d_ = drng.uniform(0.6, 2.9)
        x, y = SWAMP.x + math.cos(a_) * d_ * 1.1, SWAMP.y + math.sin(a_) * d_
        h, kind = TH(x, y)
        if kind != "swamp":
            continue
        c, r = to_px(x, y, h)
        if cls[int(r), int(c)] not in (C_SWAMP, C_SWAMPW):         # on the bog itself, not on the basin rim
            continue
        st = DEAD_A if drng.random() < 0.5 else DEAD_B
        c0, r0 = int(c) - 3, int(r) - len(st) + 1
        box = propm[r0 - 1:r0 + len(st) + 1, c0 - 1:c0 + len(st[0]) + 1]
        if box.any() or any(in_pin(cc, rr, 3) for cc in (c0, c0 + len(st[0])) for rr in (r0, r0 + len(st))):
            continue
        stamp(idx, pal, st, {"D": DEAD[0], "L": DEAD[1]}, c0, r0)
        propm[r0:r0 + len(st), c0:c0 + len(st[0])] = True              # keep the next tree apart
        placed += 1
    # cave mouth right of the cave pin, cut into the south face of the mountain (crystal glints inside)
    cx, cy = to_px(CAVE.x + 1.55, CAVE.y - 0.55, TH(CAVE.x + 1.55, CAVE.y - 0.55)[0])
    stamp(idx, pal, CAVE_ST, {"r": ROCK[1], "o": ROCK[3], "k": CAVEC[0], "m": CAVEC[1], "c": CRYSTAL[0],
                              "v": CRYSTAL[1]}, int(cx) - 5, int(cy) - 6)
    # ice fishing hole next to the shanty
    hx, hy = to_px(SHANTY[0] + 0.55, SHANTY[1] - 0.3, 2.4)
    stamp(idx, pal, HOLE_ST, {"d": ICEC[0], "w": WB[4]}, int(hx) - 1, int(hy) - 1)
    # waterfall where the river leaves the gorge wall, rapids on the upper river
    rs = np.argwhere(riv)
    if len(rs):
        top = rs[rs[:, 0].argmin()]
        wc = int(top[1])
        for r in range(int(top[0]) - 7, int(top[0]) + 1):
            for c in range(wc - 1, wc + 2):
                if 0 <= r < H and not riv[r, c] and terr[r, c]:
                    idx[r, c] = P(FOAM) if (c - wc + r) % 3 else P(WB[0])
    rr = riv & (Y > 3.4) & (np.stack([R.dash_mask(W, 0.18, 1, 3, rng) for _ in range(H)])) & ~pinm
    idx[rr] = P(FOAM)
    # wake behind the ocean boat
    bxp, byp = to_px(OCEAN_BOAT[0] - 0.75, OCEAN_BOAT[1], 0.0)
    for k in range(1, 7):
        for sgn in (-1, 1):
            r = int(round(byp + sgn * (k * 0.5 + 0.5)))
            c = int(round(bxp - k * 1.8))
            if 0 <= r < H and 0 <= c < W and sea[r, c] and gi[r, c] < 0 and k % 3 != 0:
                idx[r, c] = P(FOAM)
                if k < 4 and sea[r, c - 1]:
                    idx[r, c - 1] = P(WB[0])

    # ---------------------------------------------------------- wave marks, sheen, glitter (open sea only)
    open_sea = sea & (idx == sea_lut[np.clip(band, 0, len(SEA) - 1)]) & (dist > 1.2)
    g_am, g_br = R.glitter_masks(PR, open_sea, random.Random(17), avoid=pinm)
    g_am = (g_am | (R.shift(g_am, 0, 1) & open_sea)) & ~g_br           # top-down map: short 2 px dashes, no dots
    wrng = random.Random(11)
    nwm = 0
    for k in range(400):
        if nwm >= 46:
            break
        r, c = wrng.randrange(CROP[1], CROP[1] + CROP[3]), wrng.randrange(CROP[0], CROP[0] + CROP[2])
        n = wrng.randint(2, 4)
        if not open_sea[r:r + 2, c - 1:c + n + 2].all() or pinm[r, c] or dist[r, c] < 2.0:
            continue
        if (g_am | g_br)[r - 2:r + 3, c - 3:c + n + 3].any():
            continue
        idx[r, c:c + n] = up_lut[idx[r, c:c + n]]                       # light dash, dark dash below-right
        idx[r + 1, c + 1:c + n] = dn_lut[idx[r + 1, c + 1:c + n]]
        nwm += 1
    gcol = W / 2 + PR.glitter["dx"]
    rows = np.arange(H)[:, None]
    cols = np.arange(W)[None, :]
    g0 = PR.glitter["row0"]
    g1 = g0 + PR.glitter["far"] * (PR.glitter["row1"] - g0)
    t = np.clip((rows - g0) / (g1 - g0), 0, 1)
    halfw = PR.glitter["width"][0] + (PR.glitter["width"][1] - PR.glitter["width"][0]) * t
    sheen = 0.55 * (1 - t) ** 1.5 * np.exp(-((cols - gcol) / (1.4 * halfw)) ** 2) * (rows >= g0) * (rows < g1)
    sm = open_sea & (sheen > R.dash_threshold(H, W, random.Random(23), 2, 6)) & ~g_am & ~g_br
    idx[sm] = up_lut[idx[sm]]                                            # sheen = one band lighter
    idx[g_am] = P(GLIT[0])
    idx[g_br] = P(GLIT[1])

    img = R.to_rgba(idx, pal)
    img[..., 3] = 1.0
    R.save_png(img, os.path.join(R.OUT, "map_world.png"))
    np.save(os.path.join(R.WORK, "cls.npy"), cls)
    print("HYB map colours", R.count_colours(img), "palette", len(pal), "props px", int(propm.sum()),
          "wave marks", nwm, "glitter px", int(g_am.sum() + g_br.sum()), "dead trees", placed)
    print("CHECK prop outline darkest L", ol_stats[0], "near-black ink tones", ol_stats[1])
    return img, markers, dict(cls=cls, sea=sea, dist=dist, propm=a & ~terr, X=X, Y=Y, Z=Z, Kt=Kt, idx=idx, pal=pal)


# ================================================================== JSON, checks, preview
def write_json(markers):
    with open(os.path.join(C.DATA, "map.json"), encoding="utf-8") as f:
        old = json.load(f)
    new = {"widthPx": W, "heightPx": H, "ppu": PPU, "markers": markers}
    with open(os.path.join(R.OUT, "map.json"), "w", encoding="utf-8") as f:
        json.dump(new, f, ensure_ascii=False, indent=1)
    same_keys = list(old) == list(new) and [m["id"] for m in old["markers"]] == [m["id"] for m in new["markers"]]
    dmax = max(max(abs(o["x"] - n["x"]), abs(o["y"] - n["y"])) for o, n in zip(old["markers"], new["markers"]))
    print("CHECK map.json keys/order same", same_keys, "size same", (old["widthPx"], old["heightPx"], old["ppu"]) ==
          (W, H, PPU), "max marker delta (units)", round(dmax, 4))


# matching feature per marker + the window (half width / height px) it is looked for in around the spot
FEATURE = {"lake": ((C_LAKE,), 8, 6), "stream": ((C_RIVER,), 8, 6), "sea": ((C_SEA,), 22, 16),
           "swamp": ((C_SWAMP, C_SWAMPW), 8, 6), "ice": ((C_ICE,), 8, 6), "ocean": ((C_SEA,), 22, 16),
           "cave": ((C_ROCK, C_SNOW), 8, 6)}


def checks(img, info):
    cls, propm = info["cls"], info["propm"]
    old = R.load_png(os.path.join(C.SPRITES, "Stages", "map_world.png"))
    print("CHECK map_world size", img.shape[:2], "game", old.shape[:2], "same", img.shape[:2] == old.shape[:2],
          "colours", R.count_colours(img))
    for sid, (c, r) in PINS.items():
        ci, ri = int(c), int(r)
        feats, hw, hh = FEATURE[sid]
        win = cls[max(0, ri - hh):ri + hh + 1, max(0, ci - hw):ci + hw + 1]
        feat = np.isin(win, feats).mean()
        foot = (slice(max(0, int(r - 19.5)), int(r + 4.5)), slice(int(c - 11.5), int(c + 11.5)))
        q = np.round(img[foot][..., :3] * 255).astype(int)
        ncol = len(np.unique(q[..., 0] * 65536 + q[..., 1] * 256 + q[..., 2]))
        print("CHECK marker", sid, "px", round(c, 1), round(r, 1), "feature share near spot", round(float(feat), 2),
              "props in pin footprint", int(propm[foot].sum()), "colours in footprint", ncol)
    # floating props lighter than the local water
    for name, (x, y) in (("ocean boat", OCEAN_BOAT), ("skiff", (JETTY[0][0] + (JETTY[1][0] - JETTY[0][0]) * 0.55,
                                                               JETTY[0][1] + (JETTY[1][1] - JETTY[0][1]) * 0.55 - 0.4))):
        c, r = to_px(x, y, 0.2)
        win = img[int(r) - 2:int(r) + 3, int(c) - 4:int(c) + 5, :3].reshape(-1, 3)
        Ls = R.oklab(win)[:, 0]
        print("CHECK", name, "hull L max", round(float(Ls.max()), 3), "vs water L", round(R.lum(SEA[3]), 3))


def preview(img):
    x0, y0, w, h = CROP
    crop = img[y0:y0 + h, x0:x0 + w].copy()
    big = R.upscale(crop, 2)
    for sid, (c, r) in PINS.items():
        cx, cy = int(round((c - x0) * 2)), int(round((r - y0) * 2))
        # dotted pin footprint (what MapScene covers) + a marker dot
        fx0, fx1 = int((c - 11.5 - x0) * 2), int((c + 11.5 - x0) * 2)
        fy0, fy1 = int((r - 19.5 - y0) * 2), int((r + 4.5 - y0) * 2)
        for x in range(fx0, fx1 + 1, 6):
            for y in (fy0, fy1):
                big[max(0, y):y + 2, max(0, x):x + 2, :3] = (1.0, 0.9, 0.3)
        for y in range(fy0, fy1 + 1, 6):
            for x in (fx0, fx1):
                big[max(0, y):y + 2, max(0, x):x + 2, :3] = (1.0, 0.9, 0.3)
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                d = max(abs(dx), abs(dy))
                y, x = cy + dy, cx + dx
                if 0 <= y < big.shape[0] and 0 <= x < big.shape[1]:
                    if d == 3:
                        big[y, x, :3] = (0.1, 0.08, 0.12)
                    elif d <= 2:
                        big[y, x, :3] = (1.0, 0.25, 0.2) if d <= 1 else (1.0, 1.0, 1.0)
    big[..., 3] = 1.0
    R.save_png(big, os.path.join(R.OUT, "preview_map.png"))


def main():
    img, markers, info = render_map()
    write_json(markers)
    checks(img, info)
    preview(img)
    print("HYB MAP done")


if __name__ == "__main__":
    main()
