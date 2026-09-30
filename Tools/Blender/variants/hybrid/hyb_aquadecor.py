"""
hybrid - aquarium DECORATIONS for the slot-based decor mode of the AquariumScene tank (aquarium_back / _front.png).

Same kit + mood as hyb_aquarium.py (lake preset, lamp key from the upper left / front, curated ramps pushed towards the
tank water by an underwater aerial perspective, palette quantisation per part, depth contour lines, a warm lamp rim on
the upper outer silhouette, hue-shifted selective outline - never ink). Every floor item is lit for ONE slot depth:
  back  = farther: hazed 30 % towards the water + 10 % towards the deep tone, soft rim, 1-step outline, big items
  mid   = hazed 15 %, medium items
  front = nearest (against the glass): 4 % haze, crisp, small items
Floor sprites sit on the gravel: canvas bottom edge = the base line, the last 1..3 rows are a gravel berm painted in
the tank's own sand ramp (A.SAND) so any base blends with the gravel band. Lights are pendants that hang over the hood
(depth "air", no haze) + a tank-sized light overlay (flat wash + a dash-edged cone) per theme.

Outputs (+ "dry": into _tmp/aquadecor/world|items instead of Assets):
  Sprites/World/decor_*.png  (sprites, frames, overlays, bubbles, slot markers)
  Sprites/Items/decor_*.png  (32x32 shop icons)
  _tmp/aquadecor/art_sheet.png, mock.png (fully decorated, game crop x3), lights.png (5 themes), slotmap.png,
  decor.json (slot table, sprite sizes / frames / anchors, light tints)
Run: blender -b --python Tools/Blender/variants/hybrid/hyb_aquadecor.py [-- dry] [only=ship,wheel,...]
"""
import os
import sys
import math
import json
import random
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hyb_aquarium as A  # noqa: E402  (palettes, gravel line, tank layout; importing renders nothing)
import hyb_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402

OUT = os.path.join(C.TMP, "aquadecor")
R.WORK = os.path.join(OUT, "work")                # own scratch EXR passes (parallel renders never collide)
os.makedirs(R.WORK, exist_ok=True)
PPU = 16
PR = A.PR
LB = A.LB
G = R.grade_hex
PI = math.pi

# ------------------------------------------------------------------ depth kinds
DEPTH = dict(
    back=dict(haze=0.18, deep=0.06, rim=0.25, outline=(1, 1), lines=0.35, berm=(3, 2, 1)),
    mid=dict(haze=0.09, deep=0.02, rim=0.38, outline=(1, 2), lines=0.3, berm=(2, 1, 0)),
    front=dict(haze=0.03, deep=0.0, rim=0.5, outline=(1, 2), lines=0.3, berm=(2, 1, 0)),
    air=dict(haze=0.0, deep=0.0, rim=0.0, outline=(1, 2), lines=0.3, berm=None),
    icon=dict(haze=0.0, deep=0.0, rim=0.45, outline=(2, 3), lines=0.3, berm=(3, 2, 1)),
)

# ------------------------------------------------------------------ curated ramps (raw; graded + hazed per depth)
HULL = ["#2a1e24", "#43302e", "#5e443a", "#7c5c48", "#9a7858"]
MAST = ["#3e3028", "#62503e", "#8a7258"]
SAIL = ["#7e7664", "#aca286", "#d6ccac"]
BRASS = ["#4a3620", "#86662e", "#c49e4a", "#ecd07a"]
PORTC = ["#0e141e", "#20303c"]
HOLE = ["#0c1016"]
MOSS = ["#1d4636", "#2c6244", "#468048", "#76a452"]
PALEWOOD = ["#3a2e2e", "#5a4a44", "#7e6a5c", "#a48c76", "#c8b296"]
CORAL = ["#6a2838", "#a44454", "#d86a6e", "#f49a88", "#ffc8a8"]
ROCKC = ["#44465a", "#5e6274", "#7c7e8c", "#9c9a9c"]
SLATE = ["#34343e", "#4e505c", "#6e707c", "#9294a0", "#b8b8c2"]
CABOMBA = ["#1e4a30", "#2e6e3e", "#4c9448", "#7cbc5c", "#b0dc7a"]
CABSTEM = ["#1e4030", "#2c5c3a", "#467a44"]
WHEELW = ["#3a2820", "#5e4230", "#84603e", "#a8804e", "#c8a068"]
IRON = ["#23232c", "#3e3e4a", "#62626e"]
CHESTW = ["#3a1e1e", "#5e2e26", "#844430", "#a8603c", "#c8804c"]
GOLDC = ["#5a3e1a", "#9a7a2c", "#d4ae40", "#f8e070"]
COIN = ["#8a6420", "#d4a632", "#f6d860", "#fff4b0"]
INNER = ["#1a0e12", "#2e1a1a"]
GEM_R = ["#6a1020", "#c02838", "#ff7080"]
GEM_B = ["#10306a", "#2860c0", "#70b0ff"]
BRAIN = ["#3a4424", "#5a6a30", "#80903e", "#b0b452", "#e6de7a"]
COPPER = ["#4a2a1a", "#7a4a26", "#b0743a", "#d8a452", "#f4d488"]
HGLASS = ["#10202a", "#24485a", "#4a8aa0", "#a8e0e8"]
FERN = ["#16302a", "#224a34", "#34683e", "#4e8a48", "#78ae5c"]
FERNRH = ["#2a2220", "#4a3a2e", "#6a543e"]
ROT_G = ["#3a3424", "#5c5230", "#86783e", "#aa9a52"]
ROT_R = ["#5a2030", "#90344a", "#c85466", "#ec8a8e", "#ffc0b8"]
FAN = ["#2e1a44", "#4e2a6a", "#7a3e94", "#a45cb8", "#d090dc"]

# the lights: fixture ramps, glow, and the tank overlay (wash = flat colour over the whole tank interior, cone = the
# lamp's beam under the fixture, core = its brighter middle) + a multiply tint the game may put on the fish / decor
LIGHTS = {
    "day": dict(name="주광 조명", shade=["#6e767e", "#a4acb2", "#d2d8da", "#f4f6f2"], glow=["#fff2c8", "#fffdf0"],
                wash=("#eef8ff", 0.06), cone=("#ffffff", 0.10), core=("#ffffff", 0.16), tint="#ffffff"),
    "sunset": dict(name="노을 조명", shade=["#3e2016", "#6e3620", "#a45a32", "#d48a4e"], glow=["#ffb04a", "#ffe29a"],
                   wash=("#ff9448", 0.13), cone=("#ffb860", 0.12), core=("#ffd890", 0.18), tint="#ffe0c0"),
    "moon": dict(name="달빛 조명", shade=["#56628e", "#8494c4", "#b8c6ea", "#eef2ff"], glow=["#a8c4ff", "#e4eeff"],
                 wash=("#1e2c6a", 0.26), cone=("#8eaaf0", 0.07), core=("#b4caff", 0.1), tint="#b4c4ee"),
    "neon_pink": dict(name="네온 핑크", shade=["#8a1e6a", "#e03ca8", "#ff84d4", "#ffe4f6"], glow=["#ff84d4", "#ffe4f6"],
                      wash=("#c02890", 0.15), cone=("#ff70c8", 0.13), core=("#ffb0e4", 0.18), tint="#ffd0ee"),
    "neon_cyan": dict(name="네온 시안", shade=["#10707e", "#20c8d8", "#8af0fa", "#e4ffff"], glow=["#8af0fa", "#e4ffff"],
                      wash=("#10a0b8", 0.14), cone=("#60e8f4", 0.13), core=("#b0f8ff", 0.18), tint="#d0f6ff"),
}

# ------------------------------------------------------------------ slots (canvas px of aquarium_back.png, 640x400)
# (x, base) = the bottom-centre point of a sprite placed there (its bottom edge = the base row's top edge);
# world = ((x - 320) / 16, (200 - base) / 16). max = (w, h) px the slot kind accepts. lv = tank level that unlocks it.
SLOT_KINDS = dict(light=(48, 24), back=(128, 100), mid=(56, 52), front=(40, 40))
SLOTS = [
    dict(id="light", kind="light", x=320, base=86, lv=0),
    dict(id="back_c", kind="back", x=338, base=261, lv=0),
    dict(id="mid_l", kind="mid", x=314, base=266, lv=0),
    dict(id="mid_r", kind="mid", x=412, base=268, lv=0),
    dict(id="front_l", kind="front", x=124, base=274, lv=0),
    dict(id="front_c", kind="front", x=364, base=274, lv=0),
    dict(id="back_l", kind="back", x=196, base=260, lv=1),
    dict(id="back_r", kind="back", x=472, base=263, lv=2),
    dict(id="mid_rr", kind="mid", x=488, base=268, lv=3),
    dict(id="front_r", kind="front", x=516, base=274, lv=4),
]

KP = {}              # kind tag -> palette (hex list) of the current render
OBJS = []
CUR = dict(depth="mid")


# ================================================================== palette / object helpers
def hz(cols, depth=None):
    d = DEPTH[depth or CUR["depth"]]
    out = []
    for c in G(list(cols)):
        if d["haze"]:
            c = R.mixhex(c, A.WHZ, d["haze"])
        if d["deep"]:
            c = R.mixhex(c, A.WT[4], d["deep"])
        out.append(c.lower())
    return out


def mat(kind, cols, bounds=None, soft=0.0, raw=False, **kw):
    pal = [c.lower() for c in cols] if raw else hz(cols)
    if kind in KP and KP[kind] != pal:
        raise ValueError(f"kind {kind} re-used with another palette")
    if kw.get("spec"):
        kw["spec"] = kw["spec"].lower() if raw else hz([kw["spec"]])[0]
        pal = pal + [kw["spec"]]
    KP[kind] = pal
    if len(pal) == 1:
        return R.m_flat(pal[0])
    kw.setdefault("light", LB)
    return R.m_tone(pal, bounds, soft, name=kind, **kw)


def T(ob, kind, grp=None):
    R.tagk(ob, kind, grp)
    OBJS.append(ob)
    return ob


def tube(kind, pts, rads, m, segs=10, grp=None, caps=True):
    return T(R.loft("Tube", pts, rads, m, segs, caps=caps), kind, grp)


def ell(kind, c, r, m, rot=None, grp=None, segs=16, rings=10):
    return T(R.ellipsoid("Ell", c, r, m, segs, rings, rot), kind, grp)


def bx(kind, c, s, m, rot=None, bevel=0.0, grp=None):
    return T(R.box("Box", c, s, m, rot, bevel), kind, grp)


def rib(kind, pts, ws, m, grp=None, th=0.006):
    return T(A.ribbon("Rib", pts, ws, m, th), kind, grp)


def disc(kind, c, r, depth, m, grp=None, verts=24, normal=None):
    """Cylinder whose axis runs along Y (a disc facing the camera), or along `normal`."""
    ob = C.add_prim("cyl", "Disc", m, radius=r, depth=depth, vertices=verts, location=(0, 0, 0))
    n = Vector(normal or (0, -1, 0)).normalized()
    rot = Vector((0, 0, 1)).rotation_difference(n).to_matrix().to_4x4()
    ob.matrix_world = Matrix.Translation(Vector(c)) @ rot
    return T(ob, kind, grp)


def stone(kind, c, s, m, rnd, rot_y=0.0, grp=None, flat=0.55):
    ob = C.add_prim("ico", "Stone", m, radius=1.0, location=(0, 0, 0), subdivisions=2)
    for v in ob.data.vertices:
        co = v.co * (1.0 + rnd.uniform(-0.09, 0.09))
        if co.z > flat:
            co.z = flat + (co.z - flat) * 0.45
        if co.z < -flat:
            co.z = -flat + (co.z + flat) * 0.45
        v.co = co
    ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Rotation(math.radians(rot_y), 4, "Y") @ \
        Matrix.Diagonal((s[0], s[1], s[2], 1.0))
    C.set_smooth(ob, False)
    return T(ob, kind, grp)


def xf(obs, M):
    for ob in obs:
        ob.matrix_world = M @ ob.matrix_world


def lerp_pts(pts, t):
    n = len(pts) - 1
    f = min(n - 1e-6, max(0.0, t * n))
    i = int(f)
    u = f - i
    a, b = Vector(pts[i]), Vector(pts[i + 1])
    return a + (b - a) * u


# ================================================================== render + post
def paint_berm(idx, pal, tones, hmax=3, ext=2, seed=3):
    """Gravel lip in front of the base: the tank's sand ramp (A.SAND, + a few grey stones), 1..hmax rows high over the
    footprint (the columns the item touches in its bottom 2 rows), 1 row on the ext px beyond it; 2..3 px pebbles lit
    on their left end, dark on their right end (paint_gravel style)."""
    H_, W_ = idx.shape
    lit, base, dark = (pal.index(A.SAND[t]) for t in tones)
    rk = [pal.index(c) for c in A.ROCK]
    rng = random.Random(seed)
    foot = (idx[H_ - 2:] >= 0).any(0)
    if not foot.any():
        return idx
    f2 = foot.copy()
    for s in range(1, ext + 1):
        f2[s:] |= foot[:-s]
        f2[:-s] |= foot[s:]
    out = idx.copy()
    bm = np.zeros_like(foot, shape=(H_, W_))
    cols = np.nonzero(f2)[0]
    for c in cols:
        inner = foot[c] and foot[max(0, c - 2)] and foot[min(W_ - 1, c + 2)]
        hgt = 1 if not foot[c] else (2 if not inner else (hmax if rng.random() < 0.5 else max(2, hmax - 1)))
        for k in range(hgt):
            out[H_ - 1 - k, c] = base
            bm[H_ - 1 - k, c] = True
    for r in range(H_ - hmax, H_):
        c = int(cols.min()) + rng.randint(0, 1)
        while c <= cols.max():
            wdt = rng.choice((1, 2, 2, 3))
            if rng.random() < 0.62:
                fam = (lit, base, dark) if rng.random() < 0.86 else (rk[3], rk[2], rk[1])
                for i in range(wdt):
                    cc = c + i
                    if cc < W_ and bm[r, cc]:
                        out[r, cc] = fam[0] if i == 0 else (fam[2] if i == wdt - 1 else fam[1])
            c += wdt + rng.randint(0, 2)
    # the berm's top row: lit crumbs only (it reads as a heap, not a slab)
    top = bm & ~R.shift(bm, 1, 0, False)
    crumbs = top & (np.random.RandomState(seed).rand(H_, W_) < 0.3)
    out[crumbs] = lit
    return out


def finish(ps, depth, post=None, late=None, berm=True, outline=True, rim=True, lines=True, hmax=3):
    d = DEPTH[depth]
    kid = R.kind_map(ps)
    hexes = []
    for cols in KP.values():
        hexes += cols
    hexes += A.SAND + A.ROCK
    pal = R.Pal(hexes)
    H_, W_ = kid.shape
    idx = np.full((H_, W_), -1, np.int64)
    solid = np.zeros((H_, W_), bool)
    for k, cols in KP.items():
        m = (kid == k) & ps["a"]
        if not m.any():
            continue
        sp = R.Pal(cols)
        i, s = R.quantize(ps["rgb"], m, sp, dither=True)
        lut = np.array([pal.index(hx) for hx in sp.hex])
        idx[m] = lut[i[m]]
        solid[m] = s[m]
    idx = R.remove_specks(idx, 2)
    idx = R.despeckle(idx, ps["id"], protect=~solid, passes=1)
    if lines:
        idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=d["lines"], steps=1)
    if post:
        idx, pal = post(idx, pal, ps, kid)
    if rim and d["rim"]:
        idx, pal = R.rim_light(idx, pal, PR, strength=d["rim"], dirs=dict(t=1.0, tl=0.8, l=0.4))
    if outline:
        lo, do = d["outline"]
        idx = R.outer_outline(idx, pal, lit_steps=lo, dark_steps=do, light="left")
    if late:
        idx, pal = late(idx, pal, ps, kid)
    if berm and d["berm"]:
        idx = paint_berm(idx, pal, d["berm"], hmax=hmax)
    return idx, pal, kid


def begin(depth):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    KP.clear()
    OBJS.clear()
    CUR["depth"] = depth


def render(tag, w, h, depth, build, *args, cz=None, scale=1.0, icon=None, **fin):
    """Canvas w x h px, centred on x = 0, bottom edge = world z 0 (the base line) unless cz is given."""
    begin(depth)
    extra = build(*args) or {}
    if scale != 1.0:
        xf(OBJS[:], Matrix.Scale(scale, 4))
        extra = {k: tuple(v * scale for v in val) for k, val in extra.items()}
    C.ortho_camera(0.0, (h / 2) / PPU if cz is None else cz, w, h, PPU)
    bpy.context.view_layer.update()
    ps = R.render_passes(tag)
    idx, pal, kid = finish(ps, depth, **fin)
    return R.to_rgba(idx, pal), extra


def render_icon(tag, build, *args, fill=28, zmin=0.0, fit=None, cx=None, top=False, **fin):
    """32x32 shop icon: the same model, unhazed, fitted into fill px, base line 1 px above the bottom edge."""
    begin("icon")
    extra = build(*args) or {}
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(OBJS)
    z0 = max(z0, zmin)
    ppu = min(fill / (x1 - x0), fill / (z1 - z0)) if fit is None else fill / (z1 - z0)
    ppu = min(ppu, 40.0)
    hh = 31
    cz = (z1 + 2.0 / ppu - hh / 2 / ppu) if top else (z0 + hh / 2 / ppu)
    C.ortho_camera((x0 + x1) / 2 if cx is None else cx, cz, 32, hh, ppu)
    bpy.context.view_layer.update()
    ps = R.render_passes(tag)
    idx, pal, kid = finish(ps, "icon", **fin)
    img = R.to_rgba(idx, pal)
    out = np.zeros((32, 32, 4), np.float32)
    out[:31] = img
    return out, extra


# ================================================================== 2D pixel helpers
def hexa(h, a=1.0):
    r, g, b = R.hexrgb(h)
    return np.array([r, g, b, a], np.float32)


def put(img, r, c, col):
    if 0 <= r < img.shape[0] and 0 <= c < img.shape[1]:
        img[r, c] = col


def stamp(img, rows, r0, c0, cmap):
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            if ch in cmap and cmap[ch] is not None:
                put(img, r0 + j, c0 + i, cmap[ch])


def vline(img, c, r0, r1, col):
    for r in range(r0, r1):
        put(img, r, c, col)


# ================================================================== BACK items
def b_ship():
    """Sunken galleon: a lofted planked hull (bow up 7 deg, buried at the stern), a sterncastle with two windows,
    brass-ringed portholes, a jagged hole, a broken main mast + fallen yard with a tattered sail, a foremast stump,
    a bowsprit, algae on the gunwale."""
    rnd = random.Random(5)
    hull = mat("hull", HULL, [0.3, 0.47, 0.63, 0.8], noise=0.06, nscale=3.0, ncoord="object", bias=-0.12,
               grads=((2, 0.2, 1.9, 0.34, "object"),),
               pats=((2, 0.32, 0.2, -0.2, "object"), (2, 0.64, 0.5, 0.08, "object", 0.1)))
    trim = mat("trim", MAST, [0.45, 0.7])
    mastm = mat("mast", MAST, [0.4, 0.66], noise=0.06, nscale=6.0)
    sail = mat("sail", SAIL, [0.42, 0.66], lam=0.8)
    brass = mat("brass", BRASS, [0.35, 0.55, 0.75])
    port = mat("port", PORTC, [0.55], lam=0.5)
    hole = mat("hole", HOLE)
    moss = mat("moss", MOSS[1:], [0.45, 0.72], noise=0.15, nscale=6.0)
    xs = np.linspace(-3.05, 2.95, 13)
    t = (xs + 3.05) / 6.0
    zd = 1.35 + 0.62 * np.clip((t - 0.55) / 0.45, 0, 1) ** 2 + 0.2 * (1 - t) ** 3
    ra = 1.2 * (1 - 0.72 * np.clip((t - 0.62) / 0.38, 0, 1) ** 2) * (1 - 0.18 * np.clip((0.12 - t) / 0.12, 0, 1))
    rb = 0.9 * (1 - 0.94 * np.clip((t - 0.68) / 0.32, 0, 1) ** 1.4)
    pts = [(float(x), 0.0, float(z)) for x, z in zip(xs, zd)]
    ob = tube("hull", pts, [(float(a), float(b)) for a, b in zip(ra, rb)], hull, segs=18, grp="hull")
    for v in ob.data.vertices:
        zl = float(np.interp(v.co.x, xs, zd))
        if v.co.z > zl:
            v.co.z = zl
    ob.data.update()
    obs = [ob]
    # gunwale (sheer line strip on the camera side) + posts of the rail
    gw = [(float(x), -float(b) * 0.97, float(z) + 0.03) for x, z, b in zip(xs[1:11], zd[1:11], rb[1:11])]
    obs.append(tube("trim", gw, 0.07, trim, segs=6, grp="trim"))
    for x in np.linspace(-1.4, 2.3, 7):
        z = float(np.interp(x, xs, zd))
        b = float(np.interp(x, xs, rb))
        obs.append(tube("trim", [(x, -b * 0.95, z), (x, -b * 0.95, z + 0.28)], 0.045, trim, segs=5, grp="trim"))
    rail = [(float(x), -float(np.interp(x, xs, rb)) * 0.95, float(np.interp(x, xs, zd)) + 0.28)
            for x in np.linspace(-1.4, 2.3, 8)]
    obs.append(tube("trim", rail, 0.04, trim, segs=5, grp="trim"))
    # sterncastle
    zs = float(np.interp(-2.35, xs, zd))
    obs.append(bx("hull", (-2.45, 0.0, zs + 0.38), (1.25, 1.45, 0.8), hull, bevel=0.05, grp="castle"))
    obs.append(bx("trim", (-2.45, -0.02, zs + 0.8), (1.35, 1.5, 0.1), trim, bevel=0.02, grp="ctrim"))
    for x in (-2.75, -2.2):
        obs.append(bx("port", (x, -0.74, zs + 0.42), (0.24, 0.06, 0.3), port, grp=f"win{x}"))
    # portholes (brass ring + dark glass) along the hull side
    for x in (-1.2, -0.25, 0.7, 1.6):
        z = float(np.interp(x, xs, zd)) - 0.48
        a_ = float(np.interp(x, xs, ra))
        b = float(np.interp(x, xs, rb))
        y = -b * math.sqrt(max(0.0, 1 - (0.48 / a_) ** 2))
        obs.append(disc("brass", (x, y - 0.02, z), 0.17, 0.08, brass, grp=f"pr{x}"))
        obs.append(disc("port", (x, y - 0.06, z), 0.11, 0.06, port, grp=f"pg{x}"))
    # the hole: a jagged dark gash low on the side + a sprung plank
    hp = [(-0.2, 0.62), (0.15, 0.75), (0.32, 0.55), (0.55, 0.7), (0.62, 0.42), (0.35, 0.3), (0.05, 0.36), (-0.12, 0.25)]
    obs.append(T(C.poly_object("Hole", [(x + 0.3, z + 0.42) for x, z in hp], hole, thickness=0.02, y=-0.9), "hole", "hole"))
    obs.append(bx("hull", (0.72, -0.93, 1.0), (0.7, 0.06, 0.1), hull, rot=Matrix.Rotation(math.radians(-24), 4, "Y"),
                  grp="plank"))
    # masts, fallen yard, tattered sail, bowsprit
    zm = float(np.interp(0.25, xs, zd))
    obs.append(tube("mast", [(0.25, 0.1, zm - 0.2), (0.12, 0.1, zm + 1.3), (0.02, 0.1, zm + 2.05), (0.1, 0.1, zm + 2.18)],
                    [0.11, 0.1, 0.085, 0.05], mastm, segs=8, grp="mast"))
    obs.append(tube("mast", [(-1.25, 0.25, zm + 1.55), (0.0, 0.25, zm + 1.9), (1.35, 0.25, zm + 1.2)],
                    [0.05, 0.06, 0.045], mastm, segs=6, grp="yard"))
    sp = [(-1.05, zm + 1.52), (-0.3, zm + 1.74), (0.5, zm + 1.62), (1.1, zm + 1.3), (0.95, zm + 0.95), (0.78, zm + 1.05),
          (0.6, zm + 0.7), (0.35, zm + 0.88), (0.1, zm + 0.52), (-0.18, zm + 0.8), (-0.45, zm + 0.6), (-0.7, zm + 0.98),
          (-0.95, zm + 1.1)]
    obs.append(T(C.poly_object("Sail", sp, sail, thickness=0.03, y=0.34), "sail", "sail"))
    zf = float(np.interp(2.0, xs, zd))
    obs.append(tube("mast", [(2.0, 0.05, zf - 0.2), (2.0, 0.05, zf + 0.62), (2.08, 0.05, zf + 0.72)],
                    [0.08, 0.07, 0.04], mastm, segs=8, grp="fore"))
    zb = float(zd[-1])
    obs.append(tube("mast", [(2.85, 0.0, zb - 0.05), (3.3, 0.0, zb + 0.3)], [0.07, 0.04], mastm, segs=6, grp="bows"))
    # algae tufts on the gunwale / castle
    for k in range(9):
        x = rnd.uniform(-2.9, 2.2)
        z = (float(np.interp(x, xs, zd)) if x > -1.8 else zs + 0.82) + 0.04
        b = float(np.interp(x, xs, rb))
        r_ = rnd.uniform(0.08, 0.15)
        obs.append(ell("moss", (x, -b * 0.9 - 0.08, z), (r_ * 1.6, r_, r_ * 0.8), moss, grp="moss"))
    xf(OBJS[:], Matrix.Translation((0.25, 0.0, -0.42)) @ Matrix.Rotation(math.radians(-7.0), 4, "Y"))
    return {}


def b_driftwood():
    """Pale spider-wood root: a lying base log with arching, forking limbs and a few java-moss cushions."""
    rnd = random.Random(8)
    wood = mat("dwood", PALEWOOD, [0.3, 0.48, 0.66, 0.84], noise=0.12, nscale=3.5, ndetail=1.0,
               grads=((2, 0.0, 3.5, 0.12, "world"),))
    moss = mat("dmoss", MOSS[1:], [0.45, 0.72], noise=0.15, nscale=6.0)
    limbs = [([(-2.75, 0.18, 0.2), (-1.7, 0.1, 0.3), (-0.5, 0.0, 0.42), (0.7, 0.05, 0.38), (1.9, 0.12, 0.26),
               (2.7, 0.2, 0.16)], [0.16, 0.24, 0.28, 0.26, 0.2, 0.12]),
             ([(-0.45, 0.0, 0.5), (-0.85, -0.05, 1.45), (-1.55, -0.1, 2.3), (-2.2, -0.05, 2.9), (-2.55, 0.0, 3.3)],
              [0.2, 0.15, 0.11, 0.075, 0.045]),
             ([(0.35, 0.1, 0.45), (0.62, 0.15, 1.5), (1.15, 0.2, 2.45), (1.55, 0.2, 3.3), (1.62, 0.2, 3.62)],
              [0.18, 0.13, 0.09, 0.055, 0.035]),
             ([(-0.95, -0.12, 1.62), (-0.35, -0.2, 2.28), (0.12, -0.2, 3.0), (0.2, -0.2, 3.3)],
              [0.1, 0.075, 0.05, 0.03]),
             ([(-1.8, 0.1, 0.34), (-2.35, 0.05, 0.85), (-2.8, 0.05, 1.05)], [0.1, 0.065, 0.035]),
             ([(0.95, 0.15, 2.05), (1.75, 0.1, 2.25), (2.45, 0.05, 2.78), (2.62, 0.05, 2.95)],
              [0.085, 0.06, 0.04, 0.025]),
             ([(1.6, 0.1, 0.3), (2.2, 0.0, 0.75), (2.55, 0.0, 1.35)], [0.09, 0.06, 0.035])]
    for k, (pts, rads) in enumerate(limbs):
        tube("dwood", pts, [r * 1.25 for r in rads], wood, segs=10, grp=f"limb{k}")
    for k in range(11):
        pts, rads = limbs[k % 4]
        i = rnd.randrange(len(pts) - 1)
        u = rnd.random()
        p = lerp_pts(pts, (i + u) / (len(pts) - 1))
        r = rads[i] * (1 - u) + rads[i + 1] * u
        rr = rnd.uniform(0.1, 0.17)
        ell("dmoss", (p.x + rnd.uniform(-0.08, 0.08), p.y - 0.25, p.z + r * 0.9), (rr * 1.5, rr, rr * 0.75), moss,
            grp="moss")
    return {}


def b_staghorn():
    """Branching staghorn coral (coral pink, lighter tips) on a small rock."""
    rnd = random.Random(14)
    cor = mat("coral", CORAL, [0.3, 0.5, 0.68, 0.84], grads=((2, 0.2, 3.0, 0.25, "world"),))
    tip = mat("ctip", CORAL[2:], [0.45, 0.72])
    rk = mat("crock", ROCKC, [0.34, 0.54, 0.76], noise=0.1, nscale=2.5)
    stone("crock", (0.05, 0.25, 0.12), (0.95, 0.6, 0.42), rk, rnd, grp="rock")

    def branch(p, ang, L, r, depth, g):
        a = math.radians(ang)
        n = 5
        pts, rads = [], []
        for i in range(n):
            t = i / (n - 1)
            aa = a * (1 - 0.35 * t)
            pts.append((p[0] + math.sin(aa) * L * t + 0.06 * math.sin(t * 5 + ang), p[1], p[2] + math.cos(aa) * L * t))
            rads.append(r * (1 - 0.35 * t))
        tube("coral", pts, rads, cor, segs=8, grp=g)
        end = pts[-1]
        if depth == 0:
            ell("ctip", end, (r * 0.75, r * 0.75, r * 0.9), tip, grp=g)
            return
        for s, da in ((-1, rnd.uniform(24, 38)), (1, rnd.uniform(20, 34))):
            branch(end, ang + s * da, L * rnd.uniform(0.6, 0.75), r * 0.78, depth - 1, g)
        if rnd.random() < 0.4:
            mid = pts[2]
            branch(mid, ang + rnd.choice((-1, 1)) * 55, L * 0.45, r * 0.7, 0, g)

    for k, (x, ang, L) in enumerate(((-0.55, -32, 1.05), (-0.15, -8, 1.3), (0.25, 14, 1.2), (0.6, 38, 0.95))):
        branch((x, -0.05 + 0.08 * k, 0.35), ang, L, 0.13, 2, f"br{k}")
    return {}


def b_cabomba(f=0):
    """Cabomba: five stems of feathery whorls (flattened crossed fans), smaller and lighter towards the tips; the sway
    is a travelling wave (tips lag), 4 frames per loop."""
    ph = f * PI / 2
    rnd = random.Random(11)
    stem_m = mat("cstem", CABSTEM, [0.45, 0.72])
    leaf_m = mat("cleaf", CABOMBA, [0.3, 0.48, 0.66, 0.84], lam=0.85, grads=((2, 0.0, 5.2, 0.3, "world"),),
                 bias=-0.04)
    stems = [(-0.74, 3.8, 0.3), (-0.26, 4.95, 1.4), (0.2, 4.5, 2.3), (0.68, 3.5, 3.1)]
    for si, (x0, hgt, po) in enumerate(stems):
        n = 18
        pts = []
        for i in range(n):
            t = i / (n - 1)
            dx = x0 * 0.06 * hgt * t + 0.22 * t ** 1.6 * math.sin(ph + po - t * 1.6)
            pts.append((x0 + dx, 0.14 * si - 0.2, -0.1 + hgt * t))
        rib("cstem", pts, [0.045] * n, stem_m, grp=f"cs{si}")
        z = 0.34
        k = 0
        while z < hgt - 0.12:
            t = z / hgt
            p = lerp_pts(pts, (z + 0.1) / hgt)
            size = 0.38 * (1 - 0.3 * t ** 1.5)
            tilt = 9 * math.sin(ph + po - t * 1.6) * t          # the fans lean with the sway
            for a in (-1, 1):
                ang = a * (7 + 3 * (k % 2)) + tilt + rnd.uniform(-3, 3)
                rot = Matrix.Rotation(math.radians(ang), 4, "Y")
                ell("cleaf", (p.x, p.y - 0.03 * a, p.z), (size, 0.03, 0.034), leaf_m, rot=rot, grp=f"cl{si}",
                    segs=12, rings=6)
            z += 0.36 - 0.12 * t
            k += 1
        p = Vector(pts[-1])
        ell("cleaf", (p.x, p.y - 0.05, p.z + 0.02), (0.09, 0.05, 0.12), leaf_m, grp=f"cl{si}")
    return {}


# ================================================================== MID items
WHEEL_HUB = 1.5
WHEEL_X = -0.34
WHEEL_STEP = 10.0             # deg per frame: 6 frames = 60 deg = 2 paddle pitches (30) = 1 spoke pitch (60): seamless loop
PLASTER = ["#6a5a4e", "#9a8670", "#c4b08e", "#e4d4ae"]
ROOF = ["#3a1c1a", "#5e2c24", "#86402e", "#a85a3a"]


def b_wheel(f=0):
    """Water mill: a paddle wheel (two rims, 12 paddles between them, 6 spokes, iron hub) turning clockwise in front
    of a little mill house (plaster walls, tiled gable, a window and a door) on a stone slab."""
    rnd = random.Random(4)
    ww = mat("wwood", WHEELW, [0.3, 0.48, 0.66, 0.84], noise=0.05, nscale=5.0)
    pad = mat("wpad", WHEELW[1:], [0.35, 0.58, 0.8])
    iron = mat("iron", IRON, [0.45, 0.72])
    st = mat("wstone", ROCKC, [0.34, 0.54, 0.76], noise=0.1, nscale=2.5)
    wall = mat("plaster", PLASTER, [0.4, 0.6, 0.8], noise=0.05, nscale=4.0)
    roof = mat("roof", ROOF, [0.4, 0.6, 0.8], pats=((2, 0.16, 0.3, -0.25, "world"),))
    dark = mat("wdark", ["#161218", "#2a2226"], [0.5], lam=0.4)
    bx("wstone", (0.12, 0.2, 0.13), (2.9, 1.1, 0.34), st, bevel=0.07, grp="slab")
    stone("wstone", (-1.35, -0.25, 0.1), (0.3, 0.28, 0.18), st, rnd, grp="pebble")
    # the mill house (behind, right)
    hx, hy = 0.78, 0.55
    bx("plaster", (hx, hy, 0.3 + 0.5), (1.25, 0.9, 1.0), wall, bevel=0.03, grp="house")
    T(C.poly_object("Gable", [(hx - 0.78, 1.28), (hx + 0.78, 1.28), (hx, 1.86)], roof, thickness=1.0, y=hy),
      "roof", "roof")
    bx("roof", (hx, hy - 0.52, 1.27), (1.66, 0.12, 0.1), roof, grp="eave")
    bx("wdark", (hx + 0.28, hy - 0.46, 0.62), (0.3, 0.02, 0.56), dark, grp="door")
    bx("wdark", (hx - 0.05, hy - 0.46, 1.02), (0.24, 0.02, 0.2), dark, grp="win")
    bx("wwood", (hx - 0.05, hy - 0.475, 1.02), (0.3, 0.02, 0.04), ww, grp="winbar")
    # the axle into the house wall + the wheel (in front, left)
    tube("iron", [(WHEEL_X, -0.1, WHEEL_HUB), (hx - 0.3, hy - 0.4, WHEEL_HUB - 0.05)], 0.06, iron, segs=6, grp="axle")
    wheel = []
    for r_, y_, rad, g in ((1.08, -0.22, 0.07, "rimo"), (0.74, -0.22, 0.055, "rimi")):
        ring = [(WHEEL_X + r_ * math.cos(2 * PI * k / 48), y_, WHEEL_HUB + r_ * math.sin(2 * PI * k / 48))
                for k in range(49)]
        wheel.append(tube("wwood", ring, rad, ww, segs=8, grp=g, caps=False))
    for k in range(12):
        a = 2 * PI * k / 12
        c, s = math.cos(a), math.sin(a)
        rot = Matrix.Rotation(-a, 4, "Y")
        wheel.append(bx("wpad", (WHEEL_X + 0.97 * c, -0.12, WHEEL_HUB + 0.97 * s), (0.56, 0.4, 0.1), pad, rot=rot,
                        bevel=0.015, grp=f"pad{k}"))
    for k in range(6):
        a = 2 * PI * k / 6 + PI / 12
        c, s = math.cos(a), math.sin(a)
        rot = Matrix.Rotation(-a, 4, "Y")
        wheel.append(bx("wwood", (WHEEL_X + 0.4 * c, -0.2, WHEEL_HUB + 0.4 * s), (0.74, 0.07, 0.07), ww, rot=rot,
                        grp=f"spoke{k}"))
    wheel.append(disc("iron", (WHEEL_X, -0.3, WHEEL_HUB), 0.15, 0.16, iron, grp="hub"))
    ang = math.radians(f * WHEEL_STEP)
    M = Matrix.Translation((WHEEL_X, 0, WHEEL_HUB)) @ Matrix.Rotation(ang, 4, "Y") @ \
        Matrix.Translation((-WHEEL_X, 0, -WHEEL_HUB))
    xf(wheel, M)
    return {}


CHEST_LID = {"closed": 0.0, "opening": 38.0, "open": 108.0}
CHEST_TILT = 18.0


def chest_point(p):
    """Chest-local point -> world (after the forward tilt and the sink)."""
    M = Matrix.Translation((0, 0, -0.06)) @ Matrix.Rotation(math.radians(CHEST_TILT), 4, "X")
    return M @ Vector(p)


def b_chest(state="closed"):
    """Pirate chest (reddish planks, gold straps, lock plate), tilted 18 deg to show its top; the lid opens about its
    back hinge (closed / opening 38 deg / open 108 deg) over a heap of gold coins with two gems."""
    wd = mat("chest", CHESTW, [0.3, 0.48, 0.66, 0.84], pats=((0, 0.34, 0.14, -0.22, "world"),))
    gold = mat("band", GOLDC, [0.35, 0.58, 0.8])
    inner = mat("inner", INNER, [0.5])
    hole = mat("key", HOLE)
    obs = []
    W2, D2, Hb = 0.95, 0.55, 0.8
    th = 0.1
    obs.append(bx("chest", (0, 0, th / 2), (2 * W2, 2 * D2, th), wd, grp="body"))
    obs.append(bx("chest", (0, -D2 + th / 2, Hb / 2), (2 * W2, th, Hb), wd, bevel=0.02, grp="body"))
    obs.append(bx("chest", (0, D2 - th / 2, Hb / 2), (2 * W2, th, Hb), wd, grp="body"))
    for s in (-1, 1):
        obs.append(bx("chest", (s * (W2 - th / 2), 0, Hb / 2), (th, 2 * D2, Hb), wd, grp="body"))
    obs.append(bx("inner", (0, 0.0, Hb - 0.2), (2 * W2 - 2 * th, 2 * D2 - 2 * th, 0.02), inner, grp="inner"))
    for s in (-1, 1):
        obs.append(bx("band", (s * 0.58, -D2 - 0.012, Hb / 2), (0.13, 0.03, Hb + 0.02), gold, grp=f"sb{s}"))
    obs.append(bx("band", (0, -D2 - 0.02, Hb - 0.12), (0.3, 0.04, 0.26), gold, bevel=0.02, grp="lock"))
    obs.append(bx("key", (0, -D2 - 0.045, Hb - 0.15), (0.06, 0.02, 0.1), hole, grp="keyhole"))
    ang = CHEST_LID[state]
    if ang > 0:
        coins = mat("coin", COIN, [0.35, 0.58, 0.82], spec="#fffbe0")
        gr = mat("gemr", GEM_R, [0.4, 0.7], spec="#ffe0e8")
        gb = mat("gemb", GEM_B, [0.4, 0.7], spec="#e0f0ff")
        obs.append(ell("coin", (0, 0, Hb - 0.12), (W2 - 0.12, D2 - 0.12, 0.3), coins, grp="heap"))
        for k, (x, z) in enumerate(((-0.55, 0.1), (-0.2, 0.2), (0.3, 0.16), (0.62, 0.06), (0.05, 0.24))):
            obs.append(disc("coin", (x, -0.15 + 0.05 * k, Hb + z), 0.12, 0.04, coins, grp=f"c{k}",
                            normal=(0.3 * (k - 2), -0.6, 1.0)))
        obs.append(ell("gemr", (-0.32, -0.2, Hb + 0.2), (0.12, 0.1, 0.1), gr, grp="gr"))
        obs.append(ell("gemb", (0.42, -0.1, Hb + 0.18), (0.1, 0.09, 0.09), gb, grp="gb"))
    # lid: a half cylinder along x, straps over it; hinge at the back top edge
    lid = []
    lp = [(-W2 - 0.02, 0.0, 0.0), (W2 + 0.02, 0.0, 0.0)]
    ob = tube("chest", lp, (D2 + 0.02, D2 + 0.02), wd, segs=20, grp="lid")
    for v in ob.data.vertices:
        v.co.z = max(0.0, v.co.z) * 0.7
    ob.data.update()
    lid.append(ob)
    for s in (-1, 1):
        ob = tube("band", [(s * 0.58 - 0.065, 0, 0), (s * 0.58 + 0.065, 0, 0)], (D2 + 0.045, D2 + 0.045), gold,
                  segs=20, grp=f"lb{s}")
        for v in ob.data.vertices:
            v.co.z = max(0.0, v.co.z) * 0.7 + (0.015 if v.co.z > 0 else 0.0)
        ob.data.update()
        lid.append(ob)
    M = Matrix.Translation((0, D2, Hb)) @ Matrix.Rotation(math.radians(-ang), 4, "X") @ Matrix.Translation((0, -D2, 0))
    xf(lid, M)
    obs += lid
    xf(obs, Matrix.Translation((0, 0, -0.06)) @ Matrix.Rotation(math.radians(CHEST_TILT), 4, "X"))
    mouth = chest_point((0, 0, Hb + 0.2))
    return dict(mouth=(mouth.x, mouth.z))


def b_brain():
    """Brain coral: a big and a small dome (meander grooves are painted in post)."""
    m = mat("brain", BRAIN, [0.3, 0.52, 0.74, 0.9], bias=-0.06)
    ell("brain", (-0.28, 0.1, 0.22), (0.98, 0.8, 0.72), m, grp="b1", segs=24, rings=14)
    ell("brain", (0.78, -0.1, 0.1), (0.5, 0.45, 0.42), m, grp="b2", segs=18, rings=10)
    return {}


def brain_post(idx, pal, ps, kid):
    """Meandering ridges: per dome, wavy rings around its base centre (contour lines of the dome, wiggled along the
    angle) - 1 px ridges two ramp steps up on the olive dome, the pixel under each ridge one step down."""
    ramp = KP["brain"]
    up = R.ramp_step(pal, ramp, R.ramp_step(pal, ramp, idx, +1), +1)
    dn = R.ramp_step(pal, ramp, idx, -1)
    out = idx.copy()
    r = np.arange(idx.shape[0])[:, None].astype(float) + 0.5
    c = np.arange(idx.shape[1])[None, :].astype(float) + 0.5
    for i in np.unique(ps["id"][kid == "brain"]):
        m = (kid == "brain") & (ps["id"] == i)
        rr, cc = np.nonzero(m)
        cx, cy = (cc.min() + cc.max() + 1) / 2, rr.max() + 1.0
        rx, ry = (cc.max() + 1 - cc.min()) / 2, rr.max() + 1.0 - rr.min()
        d = np.sqrt(((c - cx) / rx) ** 2 + ((r - cy) / ry) ** 2)
        th = np.arctan2(cy - r, (c - cx) * ry / rx)
        w = 0.11 * np.sin(th * 9 + 5 * d) + 0.05 * np.sin(th * 17 - 3 * d)
        n = ry / 4.0
        f = ((d + w) * n) % 1.0
        g = m & (f < 0.27) & (d < 0.95)
        shade = m & ~g & R.shift(g, 1, 0, False)        # the pixel under a ridge pixel
        out[g] = up[g]
        out[shade] = dn[shade]
    return out, pal


def b_rocks():
    """A stone cairn: three stacked flat slate stones + a top pebble, one stone beside it, a moss cushion."""
    rnd = random.Random(23)
    st = mat("slate", SLATE, [0.3, 0.48, 0.66, 0.84], noise=0.08, nscale=2.5, ndetail=1.0)
    moss = mat("rmoss", MOSS[1:], [0.45, 0.72], noise=0.15, nscale=6.0)
    stone("slate", (-0.25, 0.1, 0.24), (1.05, 0.7, 0.38), st, rnd, rot_y=3, grp="s1")
    stone("slate", (-0.12, 0.05, 0.72), (0.78, 0.58, 0.27), st, rnd, rot_y=-7, grp="s2")
    stone("slate", (-0.2, 0.0, 1.1), (0.52, 0.44, 0.2), st, rnd, rot_y=9, grp="s3")
    stone("slate", (-0.14, -0.02, 1.36), (0.27, 0.24, 0.14), st, rnd, rot_y=-4, grp="s4")
    stone("slate", (1.02, -0.15, 0.2), (0.48, 0.42, 0.3), st, rnd, rot_y=-12, grp="s5")
    for k, (x, z, r_) in enumerate(((-0.95, 0.42, 0.14), (-0.75, 0.5, 0.11), (0.78, 0.44, 0.12))):
        ell("rmoss", (x, -0.45, z), (r_ * 1.5, r_, r_ * 0.8), moss, grp="moss")
    return {}


HELMET_VALVE = (0.06, 1.74)          # helmet-local top of the valve (x, z) before the -6 deg lean


def b_helmet():
    """The air bubbler: a copper diver's helmet on its breastplate (front port with a grille, side port, bolts, a top
    valve the bubble stream leaves from)."""
    cu = mat("copper", COPPER, [0.26, 0.44, 0.64, 0.84], spec="#fff0c0")
    rim = mat("hrim", BRASS, [0.35, 0.55, 0.75])
    gl = mat("hglass", HGLASS, [0.3, 0.55, 0.8], lam=0.7)
    tube("copper", [(0, 0.05, -0.1), (0, 0.05, 0.12), (0, 0.05, 0.3), (0, 0.05, 0.42)],
         [(0.95, 0.62), (0.92, 0.6), (0.72, 0.5), (0.5, 0.42)], cu, segs=24, grp="plate")
    for k in range(5):
        a = PI * (0.18 + 0.64 * k / 4)
        ell("hrim", (-0.8 * math.cos(a), -0.42 * math.sin(a) - 0.12, 0.2), (0.06, 0.05, 0.06), rim, grp=f"bolt{k}")
    ell("copper", (0, 0.05, 1.02), (0.64, 0.6, 0.64), cu, grp="dome", segs=24, rings=14)
    disc("hrim", (0, -0.5, 1.0), 0.36, 0.12, rim, grp="prim")
    disc("hglass", (0, -0.57, 1.0), 0.27, 0.06, gl, grp="pglass")
    for x in (-0.12, 0.0, 0.12):
        tube("hrim", [(x, -0.62, 0.76), (x, -0.62, 1.24)], 0.028, rim, segs=5, grp="grille")
    disc("hrim", (0.52, -0.3, 1.08), 0.15, 0.08, rim, grp="srim", normal=(1.0, -0.8, 0.0))
    disc("hglass", (0.56, -0.34, 1.08), 0.1, 0.04, gl, grp="sglass", normal=(1.0, -0.8, 0.0))
    disc("hrim", (0.06, 0.05, 1.66), 0.08, 0.14, rim, grp="valve", normal=(0, 0, 1))
    M = Matrix.Translation((0, 0, -0.04)) @ Matrix.Rotation(math.radians(-6), 4, "Y")
    xf(OBJS[:], M)
    e = M @ Vector((HELMET_VALVE[0], 0.05, HELMET_VALVE[1]))
    return dict(emit=(e.x, e.z))


# ================================================================== FRONT items
def b_fern(f=0):
    """Java fern tied to a small stone: 7 lanceolate leaves arching out from a rhizome; travelling-wave sway."""
    ph = f * PI / 2
    rnd = random.Random(21)
    leaf = mat("fern", FERN, [0.3, 0.48, 0.66, 0.84], lam=0.9, grads=((2, 0.0, 2.0, 0.22, "world"),))
    rh = mat("fernrh", FERNRH, [0.45, 0.72])
    st = mat("fstone", ROCKC, [0.34, 0.54, 0.76], noise=0.1, nscale=2.5)
    stone("fstone", (0.05, 0.3, 0.12), (0.62, 0.4, 0.3), st, rnd, grp="stone")
    tube("fernrh", [(-0.55, -0.05, 0.28), (0.0, -0.1, 0.4), (0.55, -0.05, 0.3)], [0.07, 0.08, 0.06], rh, segs=6,
         grp="rh")
    leaves = [(-58, 0.95), (-38, 1.4), (-18, 1.75), (0, 1.9), (18, 1.7), (38, 1.35), (56, 0.95)]
    for k, (ang, L) in enumerate(leaves):
        p = Vector((-0.36 + 0.72 * k / 6, -0.15 - 0.03 * (k % 3), 0.36))
        pts, ws = [], []
        n = 12
        for i in range(n):
            t = i / (n - 1)
            pts.append(tuple(p))
            ws.append(0.03 + 0.15 * math.sin(PI * min(1.0, t * 1.05)) ** 0.7 * (1 - 0.25 * t))
            sway = 0.2 * t ** 1.4 * math.sin(ph + k * 0.8 - t * 1.3)
            a = math.radians(ang) * (0.55 + 0.75 * t) + sway
            p = p + Vector((math.sin(a), 0, math.cos(a))) * (L / (n - 1))
        rib("fern", pts, ws, leaf, grp=f"leaf{k}")
    return {}


def b_rotala(f=0):
    """Rotala: 5 stems of paired round leaves, bronze-green low, pink-red towards the tips; sway 4 frames."""
    ph = f * PI / 2
    lo = mat("rotlo", ROT_G, [0.4, 0.62, 0.82])
    hi = mat("rothi", ROT_R, [0.3, 0.48, 0.66, 0.84], grads=((2, 1.0, 2.4, 0.2, "world"),))
    stems = [(-0.58, 1.6, 0.3), (-0.2, 2.2, 1.1), (0.2, 2.0, 1.9), (0.56, 1.5, 2.6)]
    for si, (x0, hgt, po) in enumerate(stems):
        n = 14
        pts = []
        for i in range(n):
            t = i / (n - 1)
            dx = x0 * 0.18 * t + 0.14 * t ** 1.6 * math.sin(ph + po - t * 1.4)
            pts.append((x0 + dx, 0.06 * si - 0.12, -0.05 + hgt * t))
        rib("rotlo", pts, [0.035] * n, lo, grp=f"rs{si}")
        z = 0.2
        k = 0
        while z < hgt - 0.12:
            t = z / hgt
            p = lerp_pts(pts, (z + 0.05) / hgt)
            m, kind = (hi, "rothi") if t > 0.42 else (lo, "rotlo")
            sz = 0.17 * (1 - 0.35 * t)
            for s in (-1, 1):
                rot = Matrix.Rotation(math.radians(-s * (38 + 18 * t)), 4, "Y")
                ell(kind, (p.x + s * 0.12, p.y - 0.04, p.z + 0.04), (sz, 0.03, sz * 0.3), m, rot=rot, grp=f"rl{si}",
                    segs=10, rings=6)
            z += 0.21
            k += 1
        p = Vector(pts[-1])
        ell("rothi", (p.x, p.y - 0.05, p.z), (0.09, 0.05, 0.12), hi, grp=f"rl{si}")
    return {}


FAN_WEB = []


def b_seafan():
    """Sea fan: a flat purple lattice (cut in post) on forking branches from a small holdfast."""
    web = mat("fanweb", FAN[1:4], [0.45, 0.72], lam=0.6)
    br = mat("fanbr", FAN, [0.3, 0.48, 0.66, 0.84])
    cx, cz = 0.0, 1.2
    pts = []
    for k in range(25):
        a = PI * (-0.08 + 1.16 * k / 24)
        pts.append((cx + 1.02 * math.cos(a), cz + 0.92 * math.sin(a)))
    pts += [(-0.55, 0.55), (-0.15, 0.22), (0.15, 0.22), (0.55, 0.55)][::-1]
    pts = pts[::-1]
    T(C.poly_object("Web", pts, web, thickness=0.02, y=0.12), "fanweb", "web")

    def branch(p, ang, L, r, depth, g):
        a = math.radians(ang)
        q = (p[0] + math.sin(a) * L, p[1], p[2] + math.cos(a) * L)
        tube("fanbr", [p, ((p[0] + q[0]) / 2 + 0.03 * math.sin(ang), p[1], (p[2] + q[2]) / 2), q], [r, r * 0.85, r * 0.7],
             br, segs=6, grp=g)
        if depth > 0:
            for s in (-1, 1):
                branch(q, ang + s * 17, L * 0.62, r * 0.72, depth - 1, g)

    for k, ang in enumerate((-58, -30, -4, 24, 52)):
        L = 0.72 if abs(ang) > 40 else 0.8
        branch((0.0, 0.0, 0.18), ang, L, 0.07, 2, f"fb{k}")
    ell("fanbr", (0, 0.0, 0.1), (0.2, 0.16, 0.14), br, grp="hold")
    return {}


def fan_late(idx, pal, ps, kid):
    """Cut the web into a diamond lattice (period 3) after the outline (holes stay holes)."""
    r = np.arange(idx.shape[0])[:, None]
    c = np.arange(idx.shape[1])[None, :]
    keep = ((r + c) % 3 == 0) | ((r - c) % 3 == 0)
    out = idx.copy()
    out[(kid == "fanweb") & ~keep] = -1
    return out, pal


# ================================================================== LIGHTS (pendants over the hood)
def b_light(theme):
    L = LIGHTS[theme]
    sh = mat("shade", L["shade"], [0.3, 0.55, 0.8], raw=True)
    gl = mat("glow", L["glow"], [0.5], raw=True, lam=0.6, bias=0.3)
    capm = mat("cap", IRON, [0.45, 0.72])
    if theme == "day":
        prof = [(0.74, 0.14), (0.66, 0.36), (0.5, 0.56), (0.3, 0.7), (0.12, 0.76)]
        tube("shade", [(0, 0, z) for _, z in prof], [(r, r * 0.7) for r, _ in prof], sh, segs=24, grp="shade")
        tube("shade", [(0, 0, 0.12), (0, 0, 0.16)], (0.8, 0.56), sh, segs=24, grp="lip")
        ell("glow", (0, -0.05, 0.14), (0.3, 0.2, 0.12), gl, grp="bulb")
        disc("cap", (0, 0, 0.82), 0.1, 0.12, capm, grp="cap", normal=(0, 0, 1))
        top = 0.88
    elif theme == "sunset":
        prof = [(0.7, 0.14), (0.52, 0.26), (0.34, 0.46), (0.26, 0.66), (0.18, 0.82), (0.1, 0.9)]
        tube("shade", [(0, 0, z) for _, z in prof], [(r, r * 0.7) for r, _ in prof], sh, segs=24, grp="shade")
        tube("shade", [(0, 0, 0.1), (0, 0, 0.15)], (0.74, 0.52), sh, segs=24, grp="lip")
        ell("glow", (0, -0.05, 0.13), (0.3, 0.2, 0.12), gl, grp="bulb")
        disc("cap", (0, 0, 0.96), 0.08, 0.14, capm, grp="cap", normal=(0, 0, 1))
        top = 1.02
    elif theme == "moon":
        m = mat("moon", L["shade"], [0.3, 0.55, 0.8], raw=True, lam=1.0, bias=0.08)
        ell("moon", (0, 0, 0.62), (0.52, 0.52, 0.52), m, grp="moon", segs=24, rings=14)
        disc("cap", (0, 0, 1.16), 0.1, 0.1, capm, grp="cap", normal=(0, 0, 1))
        top = 1.2
    else:
        # neon sign: a fish (pink) / a starfish (cyan) bent from a glowing tube, hung from two cords
        neon = mat("neon", L["shade"], [0.28, 0.55, 0.82], raw=True, lam=0.7, bias=0.12)
        if theme == "neon_pink":
            path = [(-0.95, 0.52), (-0.6, 0.78), (-0.1, 0.92), (0.38, 0.84), (0.72, 0.62), (0.9, 0.5), (0.72, 0.38),
                    (0.38, 0.18), (-0.1, 0.1), (-0.6, 0.24), (-0.95, 0.52), (-1.3, 0.82), (-1.3, 0.22), (-0.95, 0.52)]
            tube("neon", [(x, 0, z) for x, z in path], 0.07, neon, segs=8, grp="tube")
            ell("neon", (0.48, -0.05, 0.6), (0.07, 0.05, 0.07), neon, grp="eye")
            top = 1.0
        else:
            pts = []
            for k in range(11):
                a = PI / 2 + 2 * PI * k / 10
                r = 0.66 if k % 2 == 0 else 0.3
                pts.append((r * math.cos(a), 0, 0.58 + r * math.sin(a)))
            tube("neon", pts, 0.07, neon, segs=8, grp="tube")
            top = 1.3
    return dict(top=top)


def light_late(theme):
    """Cords (1 px, dark iron) from the fixture's top to the top edge; neon signs get a soft 1 px halo."""
    def fn(idx, pal, ps, kid):
        H_, W_ = idx.shape
        pal2, ids = pal.extend([R.hexrgb(IRON[0]), R.hexrgb(IRON[1])])
        cord = ids[1]
        out = idx.copy()
        a = out >= 0
        cols = [W_ // 2 - 12, W_ // 2 + 11] if theme == "neon_pink" else [W_ // 2 - 1]
        for c in cols:
            rows = np.nonzero(a[:, c])[0]
            end = int(rows.min()) if len(rows) else H_ - 1
            for r in range(0, end):
                out[r, c] = cord
        return out, pal2
    return fn


def neon_halo(img, theme):
    """Soft glow ring around a neon tube (alpha 0.45 / 0.2, the tube's middle tone)."""
    col = R.hexrgb(LIGHTS[theme]["shade"][1])
    a = img[..., 3] > 0.5
    d1 = R.dilate(a) & ~a
    d2 = R.dilate(d1 | a) & ~a & ~d1
    out = img.copy()
    for m, al in ((d1, 0.45), (d2, 0.18)):
        m = m & (out[..., 3] == 0)
        out[m, :3] = col
        out[m, 3] = al
    return out


def light_overlay(theme):
    """The tank interior (cols 102..538, rows 110..274 -> 436x164) in one flat colour per level: wash everywhere,
    cone under the lamp (x 290..350 at the rim -> 214..426 at the gravel), core in its middle third; the cone's edges
    hand over in 2..6 px horizontal dashes (the lamp-shaft style of aquarium_back)."""
    L = LIGHTS[theme]
    W_, H_ = A.IN_C1 - A.IN_C0, A.IN_R1 - A.IN_R0
    out = np.zeros((H_, W_, 4), np.float32)
    out[...] = hexa(L["wash"][0], L["wash"][1])
    rng = random.Random(17)
    thr = R.dash_threshold(H_, W_, rng, 2, 6)
    cx = 320 - A.IN_C0
    for r in range(H_):
        t = r / (H_ - 1)
        half = 30 + 76 * t ** 0.9
        hc = half * 0.42
        c = np.arange(W_)
        dx = np.abs(c + 0.5 - cx)
        fade = 1.0 - 0.35 * t
        e = np.clip((half - dx) / 6.0, 0, 1)
        m = (e * fade > thr[r]) & (dx < half + 6)
        ec = np.clip((hc - dx) / 5.0, 0, 1)
        mc = (ec * fade > thr[r]) & (dx < hc + 5)
        out[r, m] = hexa(L["cone"][0], L["wash"][1] + L["cone"][1])
        out[r, mc] = hexa(L["core"][0], L["wash"][1] + L["core"][1])
    # the air gap above the meniscus stays lit a little more (the lamp's own hot spot at the rim)
    sr = A.SURF_ROW - A.IN_R0
    out[:sr, :, 3] = np.maximum(out[:sr, :, 3] - 0.02, 0.0)
    return out


# ================================================================== FX (flat pixel cells)
FX = dict(W="#ffffff", F="#e2f4ea", B="#cfeede", L="#a6d8d2", T="#78b4b6")


def bubble_sprites():
    """decor_bubble_0..3 (2, 3, 4, 6 px): rim + lit top-left, the inside see-through (alpha 0.25)."""
    W, F, B, L = (hexa(FX[k]) for k in "WFBL")
    ins = hexa(FX["B"], 0.25)
    out = []
    shapes = [["WF", "FB"],
              [".F.", "WiF", ".F."],
              [".FF.", "FWiB", "FiiB", ".BB."],
              ["..FF..", ".F..B.", "FW...B", "F....B", ".B..B.", "..BB.."]]
    for s in shapes:
        img = np.zeros((len(s), len(s[0]), 4), np.float32)
        stamp(img, s, 0, 0, dict(W=W, F=F, B=B, i=ins, **{".": None}))
        if len(s) == 6:
            for r in range(1, 5):
                for c in range(1, 5):
                    if img[r, c, 3] == 0:
                        img[r, c] = ins
        out.append(img)
    pops = []
    for rows in (["F.W.F", ".....", "B...B"], ["W...W", ".....", "....."]):
        img = np.zeros((3, 5, 4), np.float32)
        stamp(img, rows, 0, 0, dict(W=W, F=F, B=B))
        pops.append(img)
    return out, pops


def chest_burst():
    """4 frames (32x48, bottom-centre = the chest mouth): a gulp of bubbles leaves the chest, rises and spreads."""
    bubs, _ = bubble_sprites()
    rng = random.Random(9)
    seeds = [(rng.uniform(-5, 5), rng.choice((0, 1, 1, 2, 2, 3)), rng.uniform(0.7, 1.3), rng.uniform(0, 6))
             for _ in range(11)]
    frames = []
    for f in range(4):
        img = np.zeros((48, 32, 4), np.float32)
        t = (f + 1) / 4
        for k, (x0, sz, sp, ph) in enumerate(seeds):
            if f == 3 and k % 2 == 0:
                continue
            y = 3 + (40 * t ** 0.85) * sp * (0.4 + 0.6 * (k / len(seeds)))
            x = x0 * (0.6 + 1.6 * t) + 1.5 * math.sin(ph + y * 0.3)
            b = bubs[min(3, sz + (1 if f >= 2 and sz < 2 else 0))]
            h, w = b.shape[:2]
            r0 = int(round(48 - y - h))
            c0 = int(round(16 + x - w / 2))
            if r0 < 0:
                continue
            R.over(img, b, c0, r0)
        if f == 0:
            # the puff at the mouth: a white foam arc
            stamp(img, ["..FWWF..", ".F....F.", "B......B"], 44, 12, dict(W=hexa(FX["W"]), F=hexa(FX["F"]),
                                                                          B=hexa(FX["B"], 0.7)))
        frames.append(img)
    return frames


def slot_markers():
    """decor_slot_<kind>: a dashed floor ellipse (width = the slot kind's max width, 9 px high) / a dashed ring for the
    light slot, in cream (the game tints it: idle, valid target, occupied)."""
    out = {}
    col = hexa("#fff4dc")
    sh = hexa("#3a2a26", 0.55)
    for kind, (w, h) in SLOT_KINDS.items():
        if kind == "light":
            W_, H_ = 28, 28
            img = np.zeros((H_, W_, 4), np.float32)
            cx, cy, rx, ry = 13.5, 13.5, 12.0, 12.0
        else:
            W_, H_ = w, 12
            img = np.zeros((H_, W_, 4), np.float32)
            cx, cy, rx, ry = W_ / 2 - 0.5, 5.0, W_ / 2 - 1.5, 4.0
        n = int(2 * PI * max(rx, ry) * 1.6)
        pts = set()
        for k in range(n):
            a = 2 * PI * k / n
            pts.add((int(round(cy + ry * math.sin(a))), int(round(cx + rx * math.cos(a)))))
        pts = sorted(pts, key=lambda p: math.atan2(p[0] - cy, (p[1] - cx) * (ry / rx)))
        for i, (r, c) in enumerate(pts):
            if (i // 3) % 2 == 0:
                put(img, r + 1, c, sh)
        for i, (r, c) in enumerate(pts):
            if (i // 3) % 2 == 0:
                put(img, r, c, col)
        out[kind] = img
    return out


# ================================================================== the build
FLOOR = [
    # id, name, kind, build, args, canvas (w, h), frames, extra kw
    ("ship", "침몰선", "back", b_ship, (), (128, 72), None, dict(hmax=4, scale=1.125, icon=dict(fit="h", fill=27, cx=0.55))),
    ("driftwood", "유목", "back", b_driftwood, (), (108, 72), None, dict(scale=1.125)),
    ("coral_branch", "가지 산호", "back", b_staghorn, (), (82, 68), None, dict(scale=1.125)),
    ("cabomba", "카봄바", "back", b_cabomba, (), (54, 100), 4, dict(scale=1.125)),
    ("wheel", "물레방아", "mid", b_wheel, (), (56, 52), 6, {}),
    ("chest", "보물상자", "mid", b_chest, (), (40, 44), ("closed", "opening", "open"), {}),
    ("coral_brain", "뇌 산호", "mid", b_brain, (), (44, 28), None, dict(post=brain_post)),
    ("rocks", "돌탑", "mid", b_rocks, (), (52, 36), None, {}),
    ("bubbler", "기포기", "mid", b_helmet, (), (36, 32), None, {}),
    ("fern", "자바 펀", "front", b_fern, (), (40, 36), 4, {}),
    ("rotala", "로탈라", "front", b_rotala, (), (40, 40), 4, {}),
    ("coral_fan", "부채 산호", "front", b_seafan, (), (36, 40), None, dict(late=fan_late)),
]


def target_dirs(dry):
    if dry:
        return os.path.join(OUT, "world"), os.path.join(OUT, "items")
    return os.path.join(C.SPRITES, "World"), os.path.join(C.SPRITES, "Items")


def save(img, d, name, made):
    R.save_png(img, os.path.join(d, name + ".png"))
    made[name] = img


def px_of(world_x, world_z, w, h):
    """World point (item frame: x centred, z from the base line) -> (dx, dy) px from the sprite's bottom-centre
    (x right, y up) and from its centre (the importer's pivot)."""
    dx, dy = world_x * PPU, world_z * PPU
    return (round(dx, 1), round(dy, 1)), (round(dx, 1), round(dy - h / 2, 1))


def build_all(dry=False, only=None):
    wdir, idir = target_dirs(dry)
    made, icons, info = {}, {}, {}
    for (iid, name, kind, fn, args, (w, h), frames, kw) in FLOOR:
        if only and iid not in only:
            continue
        mw, mh = SLOT_KINDS[kind]
        assert w <= mw and h <= mh, (iid, w, h, kind)
        ent = dict(name=name, kind=kind, size=[w, h], pivot="centre (importer); base line = bottom edge",
                   centre_above_base_px=h / 2, sprites=[])
        if frames is None:
            img, ex = render(f"d_{iid}", w, h, kind, fn, *args, **kw)
            save(img, wdir, f"decor_{iid}", made)
            ent["sprites"].append(f"decor_{iid}")
        elif isinstance(frames, int):
            for f in range(frames):
                img, ex = render(f"d_{iid}{f}", w, h, kind, fn, f, **kw)
                save(img, wdir, f"decor_{iid}_f{f}", made)
                ent["sprites"].append(f"decor_{iid}_f{f}")
            ent["frames"] = frames
        else:
            for st in frames:
                img, ex = render(f"d_{iid}_{st}", w, h, kind, fn, st, **kw)
                save(img, wdir, f"decor_{iid}_{st}", made)
                ent["sprites"].append(f"decor_{iid}_{st}")
                if "mouth" in ex:
                    ent.setdefault("mouth_px_from_base", {})[st] = px_of(*ex["mouth"], w, h)[0]
        if frames is None and "emit" in ex:
            ent["emit_px_from_base"] = px_of(*ex["emit"], w, h)[0]
        # icon
        a0 = (frames if not isinstance(frames, (int, type(None))) else None)
        iargs = (a0[-1],) if a0 else ((0,) if isinstance(frames, int) else args)
        ikw = {k: v for k, v in kw.items() if k in ("post", "late")}
        ikw.update(kw.get("icon") or {})
        ic, _ = render_icon(f"i_{iid}", fn, *iargs, **ikw)
        save(ic, idir, f"decor_{iid}", icons)
        ent["icon"] = f"Items/decor_{iid}"
        info[iid] = ent
    # anchors
    if "wheel" in info:
        info["wheel"]["hub_px_from_base"] = [WHEEL_X * PPU, WHEEL_HUB * PPU]
        info["wheel"]["fps"] = 8
    if "cabomba" in info:
        info["cabomba"]["fps"] = 3
    for k in ("fern", "rotala"):
        if k in info:
            info[k]["fps"] = 3
    # lights
    for theme, L in LIGHTS.items():
        if only and "light" not in only and theme not in only:
            continue
        img, ex = render(f"d_light_{theme}", 48, 24, "air", b_light, theme, late=light_late(theme), berm=False,
                         rim=False, outline=not theme.startswith("neon"))
        if theme.startswith("neon"):
            img = neon_halo(img, theme)
        save(img, wdir, f"decor_light_{theme}", made)
        ov = light_overlay(theme)
        save(ov, wdir, f"decor_light_{theme}_glow", made)
        ic, _ = render_icon(f"i_light_{theme}", b_light, theme, fill=19, top=True, outline=not theme.startswith("neon"),
                            berm=False, rim=False)
        if theme.startswith("neon"):
            ic = neon_halo(ic, theme)
        ic = icon_beam(ic, theme)
        save(ic, idir, f"decor_light_{theme}", icons)
        info[f"light_{theme}"] = dict(
            name=L["name"], kind="light", size=[48, 24], sprites=[f"decor_light_{theme}"],
            overlay=dict(sprite=f"decor_light_{theme}_glow", size=[A.IN_C1 - A.IN_C0, A.IN_R1 - A.IN_R0],
                         rect_canvas=[A.IN_C0, A.IN_R0, A.IN_C1, A.IN_R1],
                         centre_world=[(A.IN_C0 + A.IN_C1) / 2 / PPU - 20, 12.5 - (A.IN_R0 + A.IN_R1) / 2 / PPU]),
            wash=[L["wash"][0], L["wash"][1]], cone=[L["cone"][0], round(L["wash"][1] + L["cone"][1], 3)],
            core=[L["core"][0], round(L["wash"][1] + L["core"][1], 3)], tint=L["tint"], icon=f"Items/decor_light_{theme}")
    # fx, markers
    if not only or "fx" in only:
        bubs, pops = bubble_sprites()
        for k, b in enumerate(bubs):
            save(b, wdir, f"decor_bubble_{k}", made)
        for k, p in enumerate(pops):
            save(p, wdir, f"decor_bubble_pop_f{k}", made)
        for k, fr in enumerate(chest_burst()):
            save(fr, wdir, f"decor_chest_burst_f{k}", made)
        for kind, img in slot_markers().items():
            save(img, wdir, f"decor_slot_{kind}", made)
    return made, icons, info


def icon_beam(ic, theme):
    """Light icons: a short beam under the lamp (the cone colour, alpha 0.55 / 0.3 in two dash-edged bands)."""
    L = LIGHTS[theme]
    a = ic[..., 3] > 0.5
    rows = np.nonzero(a.any(1))[0]
    r0 = int(rows.max()) + 1
    out = ic.copy()
    col = R.hexrgb(L["glow"][0])
    for r in range(r0, 32):
        t = (r - r0) / max(1, 31 - r0)
        half = 4 + 9 * t
        for c in range(32):
            dx = abs(c + 0.5 - 16)
            if dx < half and out[r, c, 3] == 0:
                al = 0.42 if dx < half * 0.5 else 0.22
                al *= (1 - 0.75 * t)
                if (r + c) % 2 == 0 or dx < half * 0.5:
                    out[r, c, :3] = col
                    out[r, c, 3] = al
    return out


# ================================================================== previews
BG = np.array(R.hexrgb("#1e2432"), np.float32)


def art_sheet(made, icons):
    water = np.array(R.hexrgb(A.WT[2]), np.float32)

    def onbg(img, bgc):
        o = np.zeros_like(img)
        o[..., :3] = bgc
        o[..., 3] = 1
        return R.over(o, img, 0, 0)

    rows = []
    groups = [["ship", "driftwood", "coral_branch", "cabomba_f0", "cabomba_f1", "cabomba_f2", "cabomba_f3"],
              ["wheel_f0", "wheel_f1", "wheel_f2", "wheel_f3", "wheel_f4", "wheel_f5", "chest_closed", "chest_opening",
               "chest_open", "coral_brain", "rocks", "bubbler"],
              ["fern_f0", "fern_f1", "fern_f2", "fern_f3", "rotala_f0", "rotala_f1", "rotala_f2", "rotala_f3",
               "coral_fan", "chest_burst_f0", "chest_burst_f1", "chest_burst_f2", "chest_burst_f3"],
              ["light_day", "light_sunset", "light_moon", "light_neon_pink", "light_neon_cyan", "bubble_0", "bubble_1",
               "bubble_2", "bubble_3", "bubble_pop_f0", "bubble_pop_f1"]]
    for g in groups:
        row = [onbg(made["decor_" + n], water if not n.startswith("light") else np.array(R.hexrgb("#6a5048")))
               for n in g if "decor_" + n in made]
        if row:
            rows.append(row)
    sl = [made[k] for k in sorted(made) if k.startswith("decor_slot_")]
    if sl:
        rows.append([onbg(i, water) for i in sl])
    ic = [icons[k] for k in sorted(icons)]
    if ic:
        rows.append([onbg(i, np.array(R.hexrgb("#e8d8b0"), np.float32)) for i in ic])
        rows.append([onbg(i, np.array(R.hexrgb("#2a3448"), np.float32)) for i in ic])
    sheet = R.flow_sheet(rows, BG, scale=3, pad=10)
    R.save_png(sheet, os.path.join(OUT, "art_sheet.png"))


def load(path):
    return R.load_png(path) if os.path.exists(path) else None


def base_comp():
    back = R.load_png(os.path.join(C.SPRITES, "Stages", "aquarium_back.png")).copy()
    return back


def place(dst, img, x, base):
    h, w = img.shape[:2]
    R.over(dst, img, int(round(x - w / 2)), int(base - h))


LEDGE = [("feed_basic_sealed", 160, 266), ("feed_premium_sealed", 196, 266), ("feed_tub_closed", 232, 264),
         ("feed_cooler_closed", 272, 264)]
# the cleaning tools (fk_aquaclean.py suggestion: bottom on the ledge row 284 at these columns), shown when present
TOOLS = [("clean_sponge_rest", 452), ("clean_siphon_rest", 486), ("clean_net_rest", 522)]


def compose(made, assign, theme, fish=True, ledge=True):
    comp = base_comp()
    front = R.load_png(os.path.join(C.SPRITES, "Stages", "aquarium_front.png"))
    sl = {s["id"]: s for s in SLOTS}
    order = sorted(assign.items(), key=lambda kv: dict(back=0, mid=1, front=3)[sl[kv[0]]["kind"]])
    for sid, spr in order:
        if sl[sid]["kind"] == "front":
            continue
        place(comp, made[spr], sl[sid]["x"], sl[sid]["base"])
    if fish:
        fd = os.path.join(C.SPRITES, "Fish")
        for fid, x, y, flip in (("golden_carp", 214, 166, False), ("bluegill", 400, 150, True),
                                ("rainbow_trout", 300, 206, False), ("crucian_carp", 470, 214, True),
                                ("sweetfish", 150, 132, False)):
            im = load(os.path.join(fd, f"{fid}_0.png"))
            if im is None:
                continue
            if flip:
                im = im[:, ::-1].copy()
            R.over(comp, im, x - im.shape[1] // 2, y - im.shape[0] // 2)
    for sid, spr in order:
        if sl[sid]["kind"] == "front":
            place(comp, made[spr], sl[sid]["x"], sl[sid]["base"])
    if theme:
        R.over(comp, made[f"decor_light_{theme}_glow"], A.IN_C0, A.IN_R0)
    R.over(comp, front, 0, 0)
    if theme:
        place(comp, made[f"decor_light_{theme}"], 320, 86)
    if ledge:
        for n, x, y in LEDGE:
            im = load(os.path.join(C.SPRITES, "World", n + ".png"))
            if im is not None:
                R.over(comp, im, x - im.shape[1] // 2, y - im.shape[0] // 2)
        for n, x in TOOLS:
            im = load(os.path.join(C.SPRITES, "World", n + ".png"))
            if im is not None:
                R.over(comp, im, x - im.shape[1] // 2, 284 - im.shape[0])
    return comp


DIG = {"0": ["111", "101", "101", "101", "111"], "1": ["010", "110", "010", "010", "111"],
       "2": ["111", "001", "111", "100", "111"], "3": ["111", "001", "111", "001", "111"],
       "4": ["101", "101", "111", "001", "001"], "5": ["111", "100", "111", "001", "111"],
       "6": ["111", "100", "111", "101", "111"], "7": ["111", "001", "010", "010", "010"],
       "8": ["111", "101", "111", "101", "111"], "9": ["111", "101", "111", "001", "111"]}


def text(img, s, r, c, col, k=1):
    for ch in s:
        g = DIG[ch]
        for j, row in enumerate(g):
            for i, v in enumerate(row):
                if v == "1":
                    img[r + j * k:r + (j + 1) * k, c + i * k:c + (i + 1) * k] = col
        c += 4 * k


def rect(img, c0, r0, c1, r1, col, dash=False):
    for c in range(c0, c1):
        for r in (r0, r1 - 1):
            if not dash or (c // 6) % 2 == 0:
                put(img, r, c, col)
    for r in range(r0, r1):
        for c in (c0, c1 - 1):
            if not dash or (r // 6) % 2 == 0:
                put(img, r, c, col)


def crop(img, k=3):
    x, y, w, h = R.CROP
    return R.upscale(img[y:y + h, x:x + w], k)


FULL = {"back_l": "decor_driftwood", "back_c": "decor_ship", "back_r": "decor_coral_branch",
        "mid_l": "decor_chest_open", "mid_rr": "decor_bubbler", "mid_r": "decor_wheel_f0",
        "front_l": "decor_fern_f0", "front_c": "decor_coral_fan", "front_r": "decor_rotala_f0"}


def previews(made, icons):
    art_sheet(made, icons)
    if not all(v in made for v in FULL.values()):
        return
    comp = compose(made, FULL, "day")
    R.save_png(crop(comp, 3), os.path.join(OUT, "mock.png"))
    # a second arrangement (the other items) under the sunset light
    alt = {"back_l": "decor_cabomba_f1", "back_c": "decor_driftwood", "back_r": "decor_ship",
           "mid_l": "decor_rocks", "mid_rr": "decor_coral_brain", "mid_r": "decor_chest_closed",
           "front_l": "decor_rotala_f2", "front_c": "decor_fern_f1", "front_r": "decor_coral_fan"}
    comp2 = compose(made, alt, "sunset")
    R.save_png(crop(comp2, 3), os.path.join(OUT, "mock_alt.png"))
    # the five lights (tank region, x2)
    tiles = []
    for th in LIGHTS:
        c = compose(made, FULL, th, ledge=False)
        tiles.append(R.upscale(c[60:290, 84:556], 2))
    sh = np.concatenate(tiles, 0)
    R.save_png(sh, os.path.join(OUT, "lights.png"))
    # slot map: the undecorated tank + each slot's max box above its base point, number = table order
    sm = compose(made, {}, None, fish=False, ledge=True)
    k = 3
    big = R.upscale(sm, k)
    cols = dict(light=hexa("#ffe060"), back=hexa("#6ab0ff"), mid=hexa("#7cf08a"), front=hexa("#ff9a5a"))
    for i, s in enumerate(SLOTS):
        w, h = SLOT_KINDS[s["kind"]]
        c0, r0 = s["x"] - w // 2, s["base"] - h
        col = cols[s["kind"]]
        box = big[r0 * k:s["base"] * k, c0 * k:(c0 + w) * k]
        box[..., :3] = box[..., :3] * 0.88 + col[:3] * 0.12
        for o in (0, 1):
            rect(big, c0 * k + o, r0 * k + o, (c0 + w) * k - o, s["base"] * k - o, col, dash=s["lv"] > 0)
        for d in range(-6, 7):
            for o in (0, 1):
                put(big, s["base"] * k + o, s["x"] * k + d, col)
                put(big, s["base"] * k + d, s["x"] * k + o, col)
        lab = str(i + 1)
        pl = big[r0 * k + 3:r0 * k + 19, c0 * k + 3:c0 * k + 7 + 8 * len(lab)]
        pl[..., :3] = 0.08
        text(big, lab, r0 * k + 6, c0 * k + 6, col, 2)
        pl = big[r0 * k + 3:r0 * k + 19, (c0 + w) * k - 15:(c0 + w) * k - 3]
        pl[..., :3] = 0.95
        text(big, str(s["lv"]), r0 * k + 6, (c0 + w) * k - 12, hexa("#202020"), 2)
    x, y, w, h = R.CROP
    R.save_png(big[y * k:(y + h) * k, x * k:(x + w) * k], os.path.join(OUT, "slotmap.png"))


def write_json(info):
    slots = []
    for s in SLOTS:
        w, h = SLOT_KINDS[s["kind"]]
        slots.append(dict(id=s["id"], kind=s["kind"], unlockLevel=s["lv"], base_px=[s["x"], s["base"]],
                          base_world=[round((s["x"] - 320) / PPU, 4), round((200 - s["base"]) / PPU, 4)],
                          max_px=[w, h]))
    d = dict(ppu=PPU, canvas="aquarium_back.png 640x400, world = ((col-320)/16, (200-row)/16)", slots=slots,
             items=info)
    with open(os.path.join(OUT, "decor.json"), "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=1)


def main():
    C.reset_scene()
    print("DECOR gravel top rows", {s["id"]: int(A.GTOP[s["x"]]) for s in SLOTS})
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    dry = "dry" in argv
    only = None
    for a in argv:
        if a.startswith("only="):
            only = set(a[5:].split(","))
    made, icons, info = build_all(dry, only)
    # previews need everything: fill in from the output folder when a partial run was asked
    wdir, idir = target_dirs(dry)
    for fn in os.listdir(wdir):
        if fn.startswith("decor_") and fn.endswith(".png") and fn[:-4] not in made:
            made[fn[:-4]] = R.load_png(os.path.join(wdir, fn))
    for fn in os.listdir(idir):
        if fn.startswith("decor_") and fn.endswith(".png") and fn[:-4] not in icons:
            icons[fn[:-4]] = R.load_png(os.path.join(idir, fn))
    previews(made, icons)
    if not only:
        write_json(info)
    for k in sorted(made):
        im = made[k]
        print("DECOR", k, im.shape[1], "x", im.shape[0], "colours", R.count_colours(im))
    print("DECOR icons", len(icons), "world", len(made), "dry", dry)
    print("DECOR done")


if __name__ == "__main__":
    main()
