"""
hybrid - FRONT-LAYER DEPTH MAPS: per-pixel occlusion of the scene sprites the game draws above the front layer.

The game sorts the fighting / jumping fish (42), splashes (44), bait and the above-water line (45 / 46), the fight float
... ABOVE the painted front layer (StageView.OrderFront = 40), so a fish or a line that goes under the pier / rock / boat
the angler stands on is drawn on top of it. This script renders, for every stage, a depth map aligned pixel for pixel
with Sprites/Stages/<stage>_front.png (same camera, 640x400, same pixel grid): at every pixel where the front layer is
opaque it stores the CAMERA DEPTH of the painted surface, so the game can hide any fragment that lies farther away.

Builds the stage's front scene exactly as hyb_<stage>.py does (its own render_front(Random(31)), same seeds, like
hyb_obstacles.py) and stops it at its first render pass (the pass the painted front is made of), then renders:
  * hyb_core.render_passes (colour -> alpha, object ids) with its own tag into _tmp/frontdepth/work,
  * a precise depth pass: an emission material R = floor(View Z Depth), G = frac(View Z Depth), B = world height
    (EEVEE's render target is half float: splitting keeps ~0.3 mm instead of ~3 cm at 50 m).
Nothing of the stage is saved; no stage file, no shared script, no other output is touched.

QUANTITY (one per pixel): d = camera depth in metres = Persp.ToPixel(p, out depth) of the game point p on the painted
surface:     d = (p.z + camBack) * cos(pitch) - (p.y - standH - camUp) * sin(pitch)
(Blender's View Z Depth of the stage camera; identical maths.) Along one pixel's ray d grows monotonically with the
distance, so "farther than the front layer here" is simply  d(fragment) > d(front) (+ a small bias).
For an underwater point compare the point the game DRAWS: d of Persp.Apparent(p).

ENCODING  <stage>_front_depth.png, RGBA8, 640x400, row 0 = top (exactly like <stage>_front.png):
  A = 255 where the front layer is opaque in ALL periods (the occluder coverage), 0 = no occluder
  code = R * 256 + G (16 bit);  d = near + code * (far - near) / 65535      (near / far: frontdepth_<stage>.json)
  empty pixels: R = G = 255, B = 0, A = 0 (read A first); opaque codes are clamped to 0..65534
  B = 255 where the occluder is a flat thing floating ON the water (lily pads, duckweed specks: a prop lying within
  +-0.12 m of the water, or a painted speck on the water plane),
  0 for a solid prop (optional: lets the game keep the float / splashes on top of pads)
Texel of a game point p (Unity, v up, the sprite centred on the scene origin like the front layer):
  (x, y) = Persp.ToPixel(p);  u = 0.5 + x / 640,  v = 0.5 + y / 400      (CPU: ix = floor(320 + x), iy = floor(200 + y))
Inverse (pixel col, row top-down, depth d -> game point):
  X = (col + 0.5 - 320) * d / f,  Yc = (200 - (row + 0.5)) * d / f
  x = X,  y = standH + camUp + Yc * cos - d * sin,  z = -camBack + Yc * sin + d * cos
Unity import: point filter, no mipmaps, uncompressed, sRGB OFF, alphaIsTransparency OFF, npotScale None, clamp.

Checks (printed, in the JSON and on the overlay):
  periods   the four period fronts <stage>_<p>_front.png against <stage>_front.png: same size, alpha diff (lights added
            by a period's front_post are the only allowed difference; coverage = opaque in all of them)
  fill      painted pixels with a rendered depth / the 1 px outer outline the stage post adds (nearest neighbour depth)
            / details painted in post (on a prop: its depth; detached specks on the water: the water plane y = 0)
            / left without depth (dropped); rendered-but-unpainted pixels (specks the stage post removed)
  camera    Blender's camera vs the Persp maths of stage_<id>.json on every front vertex (px, and depth in m)
  grid      ray casts through the pixel centres of a grid of interior pixels: decoded PNG depth vs the Persp depth of
            the ray hit (mean / max error, and the same with the map shifted by one row as a control)
  probes    3 named probe points per stage (pier end, rock, deck edge ...): decoded depth, ray-cast depth, the game
            point rebuilt from the decoded depth, and the expected game z of that feature from the geometry
Overlay: _tmp/frontdepth/frontdepth_<stage>_check.png (depth tint + iso-depth lines over the front, probe markers,
a x8 zoom on the angler's platform, the fill-class map, the numbers).

Run (Blender 5.2, background):
  blender -b --python Tools/Blender/variants/hybrid/hyb_frontdepth.py -- --all            # build, install, check
  blender -b --python Tools/Blender/variants/hybrid/hyb_frontdepth.py -- lake sea --dry   # _tmp only
Outputs: Tools/Blender/_tmp/frontdepth/<stage>_front_depth.png, frontdepth_<stage>.json, frontdepth_<stage>_check.png;
unless --dry also Assets/Resources/Sprites/Stages/<stage>_front_depth.png and Assets/Resources/Data/frontdepth_<stage>.json.
"""
import os
import sys
import json
import math
import zlib
import struct
import random
import importlib

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

STAGES = ("lake", "stream", "sea", "swamp", "ice", "ocean", "cave")
PERIODS = ("dawn", "day", "evening", "night")


def _parse(argv):
    rest = argv[argv.index("--") + 1:] if "--" in argv else []
    st, dry = [], False
    for a in rest:
        if a == "--dry":
            dry = True
        elif a == "--all":
            st += [s for s in STAGES if s not in st]
        elif a.startswith("-"):
            raise SystemExit("HYB FDEPTH ERROR unknown option %r (use <stage> ... | --all, --dry)" % a)
        elif a in STAGES:
            st.append(a)
        else:
            raise SystemExit("HYB FDEPTH ERROR unknown stage %r (%s)" % (a, " ".join(STAGES)))
    return st, dry


ARG_STAGES, DRY = _parse(sys.argv)
# the stage scripts import hyb_period, which parses the words after "--" at import time: it must see none
if "--" in sys.argv:
    del sys.argv[sys.argv.index("--"):]

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
import hyb_core as R  # noqa: E402
import fk_common as C  # noqa: E402

VERSION = 1
OUT = os.path.join(C.TMP, "frontdepth")
WORKD = os.path.join(OUT, "work")
SPR = os.path.join(C.SPRITES, "Stages")
EMPTY = 65535
SEED = 31                                   # render_front(Random(31)): the seed every stage's main() uses

# probe points: (label, selector, mode). selector: kinds / grps (prefix match) of the ID pass; mode "far" = the interior
# pixel of the selection with the largest game z (the pier end / deck edge: expected = the owner's max vertex z),
# "centre" = the interior pixel nearest the selection's pixel centroid (expected = the owner's z range)
PROBES = {
    "lake": [("pier end", dict(kind=("plank", "pier")), "far"),
             ("end post", dict(grp=("po1.380.95", "po-1.380.95")), "centre"),
             ("rowboat", dict(kind=("boat",)), "centre")],
    "stream": [("angler rock", dict(grp=("arock",)), "far"),
               ("flank rock", dict(grp=("fl2.0",)), "centre"),
               ("bank rock", dict(grp=("bk16.5",)), "centre")],
    "sea": [("quay edge", dict(kind=("deck",)), "far"),
            ("tetrapod", dict(kind=("tet",)), "centre"),
            ("buoy", dict(grp=("buoy",)), "centre")],
    "swamp": [("pier end", dict(kind=("plank", "pier")), "far"),
              ("lantern post", dict(grp=("po1.30.8",)), "centre"),
              ("log", dict(grp=("log",)), "centre")],
    "ice": [("snowbank", dict(grp=("dr0",)), "far"),
            ("auger", dict(grp=("auger",)), "centre"),
            ("shanty", dict(grp=("sh1",)), "centre")],
    "ocean": [("bow deck edge", dict(kind=("cap", "bulwark", "deck", "margin")), "far"),
              ("pulpit rail", dict(kind=("rail",)), "centre"),
              ("cooler", dict(grp=("cooler",)), "centre")],
    "cave": [("ledge edge", dict(kind=("ledge",)), "far"),
             ("boulder", dict(grp=("Chunk8",)), "centre"),
             ("stalagmite", dict(grp=("sgR",)), "centre")],
}


def log(*a):
    print("HYB FDEPTH", *a)
    sys.stdout.flush()


# ============================================================================ exact PNG I/O (no colour management)
def write_png_rgba8(path, arr):
    """arr: (H, W, 4) uint8, row 0 = top -> 8-bit RGBA PNG with exactly these bytes (no gAMA / sRGB chunk)."""
    arr = np.ascontiguousarray(arr, np.uint8)
    h, w, _ = arr.shape
    raw = b"".join(b"\x00" + arr[y].tobytes() for y in range(h))

    def chunk(t, data):
        return struct.pack(">I", len(data)) + t + data + struct.pack(">I", zlib.crc32(t + data) & 0xFFFFFFFF)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) +
                chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def read_png_rgba8(path):
    """8-bit RGBA PNG (any filter) -> (H, W, 4) uint8, row 0 = top. Raw bytes, no colour management."""
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    pos, idat, w = 8, [], None
    while pos < len(data):
        n = struct.unpack(">I", data[pos:pos + 4])[0]
        t = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + n]
        if t == b"IHDR":
            w, h, bd, ct = struct.unpack(">IIBB", body[:10])
            assert bd == 8 and ct == 6, "%s: not 8-bit RGBA" % path
        elif t == b"IDAT":
            idat.append(body)
        pos += 12 + n
    raw = np.frombuffer(zlib.decompress(b"".join(idat)), np.uint8).reshape(h, 1 + w * 4)
    out = np.zeros((h, w * 4), np.int32)
    prev = np.zeros(w * 4, np.int32)
    for y in range(h):
        ft, line = raw[y, 0], raw[y, 1:].astype(np.int32)
        if ft == 0:
            cur = line
        elif ft == 2:
            cur = (line + prev) & 255
        else:                                   # Sub / Average / Paeth: sequential per pixel
            cur = np.zeros_like(line)
            for x in range(w * 4):
                a = cur[x - 4] if x >= 4 else 0
                b = prev[x]
                c = prev[x - 4] if x >= 4 else 0
                if ft == 1:
                    p = a
                elif ft == 3:
                    p = (a + b) // 2
                else:
                    pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                    p = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                cur[x] = (line[x] + p) & 255
        out[y] = cur
        prev = cur
    return out.reshape(h, w, 4).astype(np.uint8)


# ============================================================================ the stage camera (Persp.cs maths)
class Cam:
    def __init__(self, L):
        self.L = L
        self.f = float(L["focalPx"])
        a = math.radians(float(L["pitch"]))
        self.s, self.c = math.sin(a), math.cos(a)
        self.stand = float(L["standH"])
        self.cam = np.array([0.0, self.stand + float(L["camUp"]), -float(L["camBack"])])   # game x, y, z
        self.W, self.H = int(L["widthPx"]), int(L["heightPx"])

    def depth(self, p):
        """Persp.ToPixel's depth of game point(s) p (..., 3)."""
        p = np.asarray(p, np.float64)
        return (p[..., 2] - self.cam[2]) * self.c - (p[..., 1] - self.cam[1]) * self.s

    def px(self, p):
        """game point(s) -> (col, row) top-down canvas coordinates (pixel centres at +0.5)."""
        p = np.asarray(p, np.float64)
        r = p - self.cam
        d = np.maximum(0.05, r[..., 2] * self.c - r[..., 1] * self.s)
        yc = r[..., 1] * self.c + r[..., 2] * self.s
        return np.stack([self.W / 2 + self.f * r[..., 0] / d, self.H / 2 - self.f * yc / d], -1)

    def unproject(self, col, row, d):
        """pixel centre (col, row top-down) + camera depth -> game point (x, y, z)."""
        col, row, d = np.asarray(col, np.float64), np.asarray(row, np.float64), np.asarray(d, np.float64)
        X = (col + 0.5 - self.W / 2) * d / self.f
        Yc = (self.H / 2 - (row + 0.5)) * d / self.f
        return np.stack([X, self.cam[1] + Yc * self.c - d * self.s, self.cam[2] + Yc * self.s + d * self.c], -1)

    def ray(self, col, row):
        """camera origin and direction (BLENDER axes: x right, y forward, z up) through the pixel centre."""
        px = (col + 0.5 - self.W / 2) / self.f
        py = (self.H / 2 - (row + 0.5)) / self.f
        g = (px, self.c * py - self.s, self.s * py + self.c)                 # game (x, y up, z forward)
        return Vector((self.cam[0], self.cam[2], self.cam[1])), Vector((g[0], g[2], g[1])).normalized()


def to_game(w):
    w = np.asarray(w, np.float64)
    return w[..., [0, 2, 1]]


# ============================================================================ building the stage's front scene
class _Stop(Exception):
    pass


_DMAT = [None]


def depth_material():
    """Emission R = floor(View Z Depth), G = frac(View Z Depth), B = world height + 64 (Blender Z = game y)."""
    m = _DMAT[0]
    try:
        if m is not None and m.name in bpy.data.materials:
            return m
    except ReferenceError:
        pass
    m = bpy.data.materials.new("FD_DEPTH")
    nb = R.NB(m)
    cd = nb.node("ShaderNodeCameraData")
    z = cd.outputs["View Z Depth"]
    fl = nb.math("FLOOR", z)
    fr = nb.math("SUBTRACT", z, fl)
    geo = nb.node("ShaderNodeNewGeometry")
    sep = nb.node("ShaderNodeSeparateXYZ")
    nb.link(geo.outputs["Position"], sep.inputs[0])
    nb.output_emission(nb.combine(fl, fr, nb.math("ADD", sep.outputs["Z"], 64.0)), 1.0)     # (emission clamps < 0)
    _DMAT[0] = m
    return m


def depth_pass(tag):
    """-> (depth HxW float64, world height HxW) of every render-visible mesh, top-down."""
    dm = depth_material()
    saved = []
    for ob in R.mesh_objects():
        mats = [s.material for s in ob.material_slots]
        saved.append((ob, mats))
        if not ob.material_slots:
            ob.data.materials.append(dm)
        for s in ob.material_slots:
            s.material = dm
    try:
        ex = R.render_exr(tag)
    finally:
        for ob, mats in saved:
            if not mats:
                ob.data.materials.clear()
            for s, mm in zip(ob.material_slots, mats):
                s.material = mm
    return ex[..., 0].astype(np.float64) + ex[..., 1].astype(np.float64), ex[..., 2].astype(np.float64) - 64.0


def build_and_render(stage, smod):
    """Runs hyb_<stage>.render_front(Random(31)) up to its first render pass, renders our passes there, stops."""
    real = R.render_passes
    box = {}

    def grab(tag, want_ids=True):
        old = R.WORK
        R.WORK = WORKD
        try:
            box["tag"] = tag
            box["ps"] = real("fd_%s_front" % stage, want_ids=True)
            box["depth"], box["height"] = depth_pass("fd_%s_front_depth" % stage)
        finally:
            R.WORK = old
        raise _Stop(tag)
    R.render_passes = grab
    try:
        smod.render_front(random.Random(SEED))
    except _Stop:
        pass
    finally:
        R.render_passes = real
    if "ps" not in box:
        raise SystemExit("HYB FDEPTH ERROR %s.render_front() returned without reaching its render pass" % smod.__name__)
    bpy.context.view_layer.update()
    return box


def object_table():
    """pass_index (as written by render_passes) -> dict(kind, grp, names, obs); objects hidden from render excluded."""
    tab = {}
    for ob in R.mesh_objects():
        t = tab.setdefault(int(ob.pass_index), dict(kind=str(ob.get("kind", "")), grp=str(ob.get("grp", "")),
                                                     names=[], obs=[]))
        t["names"].append(ob.name)
        t["obs"].append(ob)
    for t in tab.values():                       # flat things floating on the water (the whole id within +-FLOAT_Y)
        zs = np.concatenate([world_verts(ob)[:, 2] for ob in t["obs"]] + [np.zeros(0)])
        t["flat"] = bool(len(zs) and zs.min() >= -FLOAT_Y and zs.max() <= FLOAT_Y)
    return tab


def world_verts(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    oe = ob.evaluated_get(dg)
    me = oe.to_mesh()
    n = len(me.vertices)
    co = np.empty(n * 3, np.float64)
    me.vertices.foreach_get("co", co)
    M = np.array(ob.matrix_world)
    w = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    oe.to_mesh_clear()
    return w


# ============================================================================ maps
def shifted(a, dy, dx, fill):
    """out[y, x] = a[y + dy, x + dx] (fill outside)."""
    H, W = a.shape
    out = np.full_like(a, fill)
    ys0, ys1 = max(0, -dy), H - max(0, dy)
    xs0, xs1 = max(0, -dx), W - max(0, dx)
    out[ys0:ys1, xs0:xs1] = a[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx]
    return out


def water_plane_depth(cam):
    """Camera depth where each pixel-centre ray meets the water plane y = 0 (inf at / above the horizon)."""
    py = (cam.H / 2 - (np.arange(cam.H) + 0.5)) / cam.f
    den = cam.s - cam.c * py
    t = np.where(den > 1e-6, cam.cam[1] / np.maximum(den, 1e-6), np.inf)
    return np.repeat(t[:, None], cam.W, 1)


def fill_depth(depth_raw, height_raw, flat_raw, raw_ok, cover, wpd):
    """Depth, surface height and the "flat floating owner" flag on every covered pixel -> (D, Hg, Fl, cls, left).
      1 rendered: the render's own surface;
      2 outline ring: 1 step from a rendered pixel, the nearest (min depth) 4-neighbour (the stage post's 1 px outer
        outline belongs to the prop it surrounds);
      3 / 5 painted details beyond that (flowers on pads, a gull on a tetrapod, duckweed specks on the water): the min
        of the depth propagated from painted neighbours (3) and the water plane under the pixel (5), so detached specks
        on the water get the water surface, details stamped on a prop keep the prop;
      4 covered but no depth (above the horizon and not connected to a rendered prop): dropped from the coverage."""
    nbs = ((-1, 0), (1, 0), (0, -1), (0, 1))
    D = np.where(raw_ok & cover, depth_raw, np.inf)
    Hg = np.where(raw_ok & cover, height_raw, np.nan)
    Fl = np.where(raw_ok & cover, flat_raw, False)
    cls = np.zeros(D.shape, np.uint8)
    cls[raw_ok & cover] = 1

    def step_once(need):
        sd = np.stack([shifted(D, dy, dx, np.inf) for dy, dx in nbs])
        sh = np.stack([shifted(Hg, dy, dx, np.nan) for dy, dx in nbs])
        sf = np.stack([shifted(Fl, dy, dx, False) for dy, dx in nbs])
        k = np.argmin(sd, 0)
        nd = np.take_along_axis(sd, k[None], 0)[0]
        nh = np.take_along_axis(sh, k[None], 0)[0]
        nf = np.take_along_axis(sf, k[None], 0)[0]
        take = need & np.isfinite(nd)
        D[take] = nd[take]
        Hg[take] = nh[take]
        Fl[take] = nf[take]
        return take
    need = cover & ~np.isfinite(D)
    take = step_once(need)                               # the outline ring
    cls[take] = 2
    need &= ~take
    rest = need.copy()
    for _ in range(64):                                  # painted details: propagate through painted pixels
        if not need.any():
            break
        take = step_once(need)
        if not take.any():
            break
        need &= ~take
    use_w = rest & (wpd < D) & np.isfinite(wpd)
    D[use_w] = wpd[use_w]
    Hg[use_w] = 0.0
    Fl[use_w] = True
    cls[rest & np.isfinite(D)] = 3
    cls[use_w] = 5
    left = cover & ~np.isfinite(D)
    cls[left] = 4
    return D, Hg, Fl, cls, left


FLOAT_Y = 0.12          # an object whose whole height lies within +-this of the water: flat, floating (B = 255)


def encode(D, cover, near, far, floating):
    code = np.clip(np.round((D - near) / (far - near) * 65535.0), 0, 65534).astype(np.int64)
    img = np.zeros(D.shape + (4,), np.uint8)
    img[..., 0] = 255
    img[..., 1] = 255
    img[cover, 0] = (code[cover] >> 8).astype(np.uint8)
    img[cover, 1] = (code[cover] & 255).astype(np.uint8)
    img[cover & floating, 2] = 255
    img[cover, 3] = 255
    return img


def decode(img, near, far):
    code = img[..., 0].astype(np.int64) * 256 + img[..., 1].astype(np.int64)
    d = near + code * (far - near) / 65535.0
    return np.where(img[..., 3] >= 128, d, np.nan)


# ============================================================================ checks
def check_camera(cam, tab):
    """Blender camera (world_to_camera_view / view Z) vs the Persp maths of the stage JSON on every front vertex."""
    from bpy_extras.object_utils import world_to_camera_view
    sc = bpy.context.scene
    bc = sc.camera
    Minv = np.array(bc.matrix_world.inverted())
    worst_px, worst_d, n = 0.0, 0.0, 0
    for t in tab.values():
        for ob in t["obs"]:
            w = world_verts(ob)
            if len(w) == 0:
                continue
            g = to_game(w)
            pj = cam.px(g)
            dj = cam.depth(g)
            vz = -(w @ Minv[:3, :3].T + Minv[:3, 3])[:, 2]
            ok = dj > 0.5
            if ok.any():
                worst_d = max(worst_d, float(np.abs(vz - dj)[ok].max()))
            for k in range(0, len(w), max(1, len(w) // 60)):
                if not ok[k]:
                    continue
                v = world_to_camera_view(sc, bc, Vector(w[k]))
                worst_px = max(worst_px, abs(v.x * cam.W - pj[k, 0]), abs((1.0 - v.y) * cam.H - pj[k, 1]))
                n += 1
    return worst_px, worst_d, n


def raycaster(tab, stage):
    """-> cast(col, row) = (hit game point, pass_index) or (None, None). Only render-visible, non-holdout meshes."""
    sc = bpy.context.scene
    keep = set()
    for t in tab.values():
        if t["kind"] == "hold":                   # the sea's holdout water plane: not a surface
            continue
        for ob in t["obs"]:
            keep.add(ob.name)
    for ob in sc.objects:
        if ob.type == "MESH":
            ob.hide_viewport = ob.name not in keep
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()

    def cast(cam, col, row):
        o, d = cam.ray(col, row)
        hit, loc, nrm, idx, ob, mat = sc.ray_cast(dg, o, d, distance=5000.0)
        if not hit:
            return None, None
        orig = ob.original if hasattr(ob, "original") else ob
        return to_game(np.array(loc[:])), int(orig.pass_index)

    def visible(cam, w):
        """Is the Blender world point w (on a surface) seen by the stage camera (nothing in front of it)?"""
        o = Vector((cam.cam[0], cam.cam[2], cam.cam[1]))
        v = Vector(tuple(w)) - o
        dist = v.length
        hit, loc, nrm, idx, ob, mat = sc.ray_cast(dg, o, v.normalized(), distance=max(0.0, dist - 0.03))
        return not hit
    cast.visible = visible
    return cast


def interior(ids, ok):
    """Pixels whose 3x3 neighbourhood has the same id and is all ok."""
    m = ok.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dy or dx:
                m &= shifted(ids, dy, dx, -1) == ids
                m &= shifted(ok, dy, dx, False)
    return m


def grid_check(cam, cast, dec, ids, inner, stepx=4, stepy=3):
    ys, xs = np.nonzero(inner)
    sel = (ys % stepy == 0) & (xs % stepx == 0)
    ys, xs = ys[sel], xs[sel]
    err, err_sh, match, n_miss = [], [], 0, 0
    for y, x in zip(ys, xs):
        p, pi = cast(cam, x, y)
        if p is None:
            n_miss += 1
            continue
        dr = float(cam.depth(p))
        err.append(abs(dec[y, x] - dr))
        if y + 1 < dec.shape[0] and np.isfinite(dec[y + 1, x]):
            err_sh.append(abs(dec[y + 1, x] - dr))
        match += int(pi == ids[y, x])
    err = np.array(err)
    err_sh = np.array(err_sh)
    return dict(n=int(len(err)), miss=n_miss, mean=float(err.mean()) if len(err) else float("nan"),
                p99=float(np.percentile(err, 99)) if len(err) else float("nan"),
                max=float(err.max()) if len(err) else float("nan"),
                shift_mean=float(err_sh.mean()) if len(err_sh) else float("nan"),
                match=float(match) / max(1, len(err)))


def _sel_mask(tab, ids, sel):
    pis = []
    for pi, t in tab.items():
        if "kind" in sel and t["kind"] in sel["kind"]:
            pis.append(pi)
        elif "grp" in sel and any(t["grp"] == g or t["grp"].startswith(g) for g in sel["grp"]):
            pis.append(pi)
    return np.isin(ids, pis), pis


def probes(stage, cam, cast, tab, ids, inner, dec, depth_raw):
    out = []
    for k, (label, sel, mode) in enumerate(PROBES.get(stage, [])):
        m, pis = _sel_mask(tab, ids, sel)
        cand = m & inner
        if not cand.any():
            cand = m & np.isfinite(dec)
        if not cand.any():
            log("CHECK probe %s %r: no visible pixel of %s" % (stage, label, sel))
            out.append(dict(n=k + 1, label=label, ok=False, note="not visible"))
            continue
        ys, xs = np.nonzero(cand)
        if mode == "far":
            g = cam.unproject(xs, ys, depth_raw[ys, xs])
            i = int(np.argmax(g[:, 2]))
        else:
            cy, cx = ys.mean(), xs.mean()
            i = int(np.argmin((ys - cy) ** 2 + (xs - cx) ** 2))
        y, x = int(ys[i]), int(xs[i])
        owner = tab[int(ids[y, x])]
        w = np.concatenate([world_verts(ob) for ob in owner["obs"]])
        gz = to_game(w)[:, 2]
        zlo, zhi = float(gz.min()), float(gz.max())
        d_png = float(dec[y, x])
        p_png = cam.unproject(x, y, d_png)
        hit, hpi = cast(cam, x, y)
        d_ray = float(cam.depth(hit)) if hit is not None else float("nan")
        # footprint of one pixel row on that surface (for the "far edge" tolerance)
        up = dec[y - 1, x] if y > 0 else np.nan
        foot = float(abs(cam.unproject(x, y - 1, up)[2] - p_png[2])) if np.isfinite(up) else float("nan")
        if mode == "far":
            # the far edge the camera can SEE: the largest z of the owner's vertices with a free line of sight
            # (a rounded rock's far side slopes away behind its own top; a flat deck's far corners are visible)
            sub = w[::max(1, len(w) // 4000)]
            vis = np.array([cast.visible(cam, v) for v in sub])
            zvis = float(to_game(sub[vis])[:, 2].max()) if vis.any() else zhi
            f_ = foot if np.isfinite(foot) else 0.1
            exp = dict(zEdge=zvis, zGeomMax=zhi)
            ok_z = p_png[2] <= zvis + max(0.02, f_) and zvis - p_png[2] <= max(0.05, 3.0 * f_)
            zhi = zvis
        else:
            exp = dict(zMin=zlo, zMax=zhi)
            ok_z = zlo - 0.02 <= p_png[2] <= zhi + 0.02
        ok = bool(ok_z and abs(d_png - d_ray) < 0.01 and hpi == int(ids[y, x]))
        r = dict(n=k + 1, label=label, mode=mode, col=x, row=y, obj=owner["names"][0], kind=owner["kind"],
                 grp=owner["grp"], dPng=d_png, dRay=d_ray, dErr=abs(d_png - d_ray),
                 x=float(p_png[0]), y=float(p_png[1]), z=float(p_png[2]),
                 hit=[float(v) for v in hit] if hit is not None else None, sameObj=bool(hpi == int(ids[y, x])),
                 rowFootZ=foot, ok=ok, **exp)
        out.append(r)
        if mode == "far":
            e = "expected visible far edge z %.3f (geometry max %.3f; 1 px row = %.3f m): z %.3f is %.3f m short" % (
                zhi, exp["zGeomMax"], foot, p_png[2], zhi - p_png[2])
        else:
            e = "expected z in %.3f..%.3f: z %.3f" % (zlo, zhi, p_png[2])
        log("CHECK probe %s %d %-13s px(%d,%d) %-12s decoded d %.4f  ray d %.4f  |err| %.4f m  point (%.3f, %.3f, %.3f)  "
            "%s  %s" % (stage, k + 1, label, x, y, owner["names"][0], d_png, d_ray, abs(d_png - d_ray),
                        p_png[0], p_png[1], p_png[2], e, "OK" if ok else "FAIL"))
    return out


# ============================================================================ overlay (numbers drawn with a 3x5 font)
_F = {
    "0": "111101101101111", "1": "010110010010111", "2": "111001111100111", "3": "111001011001111",
    "4": "101101111001001", "5": "111100111001111", "6": "111100111101111", "7": "111001010010010",
    "8": "111101111101111", "9": "111101111001111", ".": "000000000000010", "-": "000000111000000",
    "+": "000010111010000", "=": "000111000111000", ":": "000010000010000", "/": "001001010100100",
    "(": "010100100100010", ")": "010001001001010", " ": "000000000000000", ",": "000000000010100",
    "%": "101001010100101", "|": "010010010010010", "<": "001010100010001", ">": "100010001010100",
    "_": "000000000000111", "#": "101111101111101", "'": "010010000000000", "*": "000101010101000",
    "A": "010101111101101", "B": "110101110101110", "C": "011100100100011", "D": "110101101101110",
    "E": "111100110100111", "F": "111100110100100", "G": "011100101101011", "H": "101101111101101",
    "I": "111010010010111", "J": "001001001101010", "K": "101101110101101", "L": "100100100100111",
    "M": "101111111101101", "N": "110101101101101", "O": "010101101101010", "P": "110101110100100",
    "Q": "010101101110011", "R": "110101110101101", "S": "011100010001110", "T": "111010010010010",
    "U": "101101101101111", "V": "101101101101010", "W": "101101111111101", "X": "101101010101101",
    "Y": "101101010010010", "Z": "111001010100111",
}


def text(img, x, y, s, col, k=2):
    for ch in str(s).upper():
        g = _F.get(ch, _F["*"])
        for i in range(15):
            if g[i] == "1":
                r, c = i // 3, i % 3
                img[y + r * k:y + (r + 1) * k, x + c * k:x + (c + 1) * k, :3] = col
                img[y + r * k:y + (r + 1) * k, x + c * k:x + (c + 1) * k, 3] = 1.0
        x += 4 * k
    return x


RAMP = np.array([(1.0, 0.93, 0.35), (1.0, 0.55, 0.20), (0.88, 0.25, 0.52), (0.50, 0.25, 0.78), (0.16, 0.45, 0.92),
                 (0.25, 0.85, 0.85)], np.float32)


def ramp(t):
    t = np.clip(t, 0.0, 1.0) * (len(RAMP) - 1)
    i = np.minimum(np.floor(t).astype(np.int64), len(RAMP) - 2)
    f = (t - i)[..., None]
    return RAMP[i] * (1 - f) + RAMP[i + 1] * f


def tnorm(d, near, far):
    return np.log(np.maximum(d, near) / near) / math.log(far / near)


LEVELS = np.array(list(range(5, 20)) + [22.5] + list(range(25, 60, 5)) + list(range(60, 400, 20)), np.float64)


def iso_lines(D, cover):
    b = np.searchsorted(LEVELS, np.where(cover, D, 0.0))
    e = np.zeros(D.shape, bool)
    for dy, dx in ((0, 1), (1, 0)):
        nb = shifted(b, dy, dx, -1)
        nc = shifted(cover, dy, dx, False)
        e |= cover & nc & (nb != b)
    return e


def overlay(stage, cam, L, front, back, D, cover, cls, near, far, prb, info, path):
    W, H = cam.W, cam.H
    k = 2
    base = back.copy()
    base[..., :3] = base[..., :3] * 0.35 + 0.08
    base[..., 3] = 1.0
    fr = front.copy()
    tint = ramp(tnorm(np.where(cover, D, near), near, far))
    fr[cover, :3] = fr[cover, :3] * 0.4 + tint[cover] * 0.6
    R.over(base, fr, 0, 0)
    iso = iso_lines(D, cover)
    base[iso, :3] = base[iso, :3] * 0.45 + np.array((0.85, 1.0, 1.0), np.float32) * 0.55
    top = R.upscale(base, k).copy()
    x0, y0, cw, ch = R.CROP                                   # the game's home view (centred, with overscan too)
    for i in range(x0 * k, (x0 + cw) * k):
        if (i // 6) % 2 == 0:
            top[y0 * k, i, :3] = 1.0
            top[(y0 + ch) * k - 1, i, :3] = 1.0
    for j in range(y0 * k, (y0 + ch) * k):
        if (j // 6) % 2 == 0:
            top[j, x0 * k, :3] = 1.0
            top[j, (x0 + cw) * k - 1, :3] = 1.0

    def marker(img, cx, cy, s, n, rad=6):
        for d in range(-rad, rad + 1):
            for (xx, yy) in ((cx + d, cy), (cx, cy + d)):
                if 0 <= yy < img.shape[0] and 0 <= xx < img.shape[1] and abs(d) > 1:
                    img[yy, xx, :3] = (0.0, 0.0, 0.0) if abs(d) > rad - 2 else (1.0, 1.0, 1.0)
        tx, ty = cx + rad + 2, cy - rad - 8
        if 0 <= ty < img.shape[0] - 10 and 0 <= tx < img.shape[1] - 12:
            img[ty - 1:ty + 11, tx - 1:tx + 9, :3] = 0.0
            text(img, tx, ty, str(n), (1.0, 1.0, 1.0), 2)
    for p in prb:
        if "col" in p:
            marker(top, int((p["col"] + 0.5) * k), int((p["row"] + 0.5) * k), k, p["n"])
    # legend: colour bar with ticks
    leg = np.zeros((44, W * k, 4), np.float32)
    leg[..., 3] = 1.0
    bx0, bx1 = 24, W * k - 24
    tt = np.linspace(0, 1, bx1 - bx0)
    leg[8:22, bx0:bx1, :3] = ramp(tt)[None]
    for v in LEVELS:
        if v < near or v > far or not (v in (5, 10, 12, 15, 20, 25, 30, 40, 50, 60, 80, 100, 140, 200, 300)):
            continue
        xx = bx0 + int(tnorm(np.array(v), near, far) * (bx1 - bx0 - 1))
        leg[22:27, xx, :3] = 1.0
        text(leg, xx - 4, 29, "%g" % v, (0.9, 0.9, 0.9), 2)
    text(leg, bx0, 0, "CAMERA DEPTH (M), LOG SCALE %g..%g; PALE LINES = ISO-DEPTH (1 M < 20 M, 5 M < 60 M, 20 M BEYOND);"
         " DASHED = GAME VIEW" % (near, far), (0.85, 0.85, 0.85), 1)
    # zoom x8 on the angler's platform around probe 1
    p1 = next((p for p in prb if "col" in p), None)
    kz, zw, zh = 8, W * k // 8, 100
    zc, zr = (int(p1["col"]), int(p1["row"])) if p1 else (W // 2, 300)
    c0 = int(np.clip(zc - zw // 2, 0, W - zw))
    r0 = int(np.clip(zr - zh // 2, 0, H - zh))
    zoom = R.upscale(base[r0:r0 + zh, c0:c0 + zw], kz).copy()
    for p in prb:
        if "col" in p and c0 <= p["col"] < c0 + zw and r0 <= p["row"] < r0 + zh:
            marker(zoom, int((p["col"] - c0 + 0.5) * kz), int((p["row"] - r0 + 0.5) * kz), kz, p["n"], rad=12)
    zl = np.zeros((14, W * k, 4), np.float32)
    zl[..., 3] = 1.0
    text(zl, 24, 2, "X8 ZOOM: COLS %d..%d, ROWS %d..%d (ROW 0 = TOP OF THE 640X400 CANVAS)" % (c0, c0 + zw - 1, r0, r0 + zh - 1),
         (0.85, 0.85, 0.85), 1)
    # fill classes: rendered / outline ring / filled further / no depth / rendered-but-unpainted
    fc = np.zeros((H, W, 4), np.float32)
    lum = (front[..., :3] * np.array([0.3, 0.55, 0.15])).sum(-1)
    fc[..., :3] = (0.12 * lum)[..., None]
    fc[..., 3] = 1.0
    fc[cls == 1, :3] = (0.33, 0.62, 0.40)
    fc[cls == 2, :3] = (0.90, 0.82, 0.30)
    fc[cls == 3, :3] = (1.0, 0.45, 0.1)
    fc[cls == 4, :3] = (1.0, 0.15, 0.15)
    fc[cls == 5, :3] = (0.35, 0.85, 1.0)
    fc[info["unpaintedMask"], :3] = (0.25, 0.4, 1.0)
    fc[info["periodOnlyMask"], :3] = (0.9, 0.3, 0.95)
    fcl = np.zeros((14, W * k, 4), np.float32)
    fcl[..., 3] = 1.0
    xx = text(fcl, 24, 2, "FILL: ", (0.85, 0.85, 0.85), 1)
    for lab, col in (("RENDERED DEPTH ", (0.33, 0.62, 0.40)), ("OUTLINE RING (NEIGHBOUR MIN) ", (0.90, 0.82, 0.30)),
                     ("DETAIL ON A PROP ", (1.0, 0.45, 0.1)), ("ON THE WATER PLANE ", (0.35, 0.85, 1.0)),
                     ("NO DEPTH ", (1.0, 0.15, 0.15)),
                     ("RENDERED, NOT PAINTED (NO OCCLUDER) ", (0.25, 0.4, 1.0)),
                     ("OPAQUE IN SOME PERIODS ONLY (NO OCCLUDER)", (0.9, 0.3, 0.95))):
        xx = text(fcl, xx, 2, lab, col, 1)
    fcu = R.upscale(fc, k)
    # the numbers
    lines = ["%s  NEAR %.3f FAR %.3f  STEP %.2f MM  COVER %d PX  DEPTH %.3f..%.3f M  GAME Z %.2f..%.2f M" % (
        stage, near, far, (far - near) / 65535 * 1000, info["cover"], info["dMin"], info["dMax"], info["zMin"], info["zMax"])]
    g = info["grid"]
    lines.append("GRID: %d RAYS  |DECODED - RAYCAST| MEAN %.4f P99 %.4f MAX %.4f M  SAME OBJECT %.1f%%  (MAP SHIFTED 1 ROW: MEAN %.4f M)" % (
        g["n"], g["mean"], g["p99"], g["max"], 100 * g["match"], g["shift_mean"]))
    lines.append("CAMERA: BLENDER VS PERSP %.4f PX, %.5f M   PERIODS ALPHA DIFF VS FRONT: %s" % (
        info["camPx"], info["camD"], "  ".join("%s %d" % (p, info["periods"][p]) for p in PERIODS)))
    lines.append("FILL: RENDERED %d  RING %d  DETAIL ON PROP %d  WATER PLANE %d  NONE %d  RENDERED-NOT-PAINTED %d  FLOATING (B=255) %d" % (
        info["fill"]["rendered"], info["fill"]["ring"], info["fill"]["further"], info["fill"]["waterPlane"], info["fill"]["none"],
        info["unpainted"], info["floating"]))
    for p in prb:
        if "col" not in p:
            lines.append("%d %s: NOT VISIBLE" % (p["n"], p["label"]))
            continue
        if p["mode"] == "far":
            e = "EXPECTED EDGE Z %.3f (%.3f SHORT, 1 ROW = %.3f)" % (p["zEdge"], p["zEdge"] - p["z"], p["rowFootZ"])
        else:
            e = "EXPECTED Z %.2f..%.2f" % (p["zMin"], p["zMax"])
        lines.append("%d %s PX(%d,%d) %s: DECODED D %.4f RAY %.4f ERR %.4f  GAME (%.2f, %.2f, %.3f)  %s  %s" % (
            p["n"], p["label"], p["col"], p["row"], p["grp"] or p["obj"], p["dPng"], p["dRay"], p["dErr"], p["x"], p["y"],
            p["z"], e, "OK" if p["ok"] else "FAIL"))
    tx = np.zeros((len(lines) * 14 + 8, W * k, 4), np.float32)
    tx[..., 3] = 1.0
    for i, s in enumerate(lines):
        text(tx, 12, 4 + i * 14, s, (1.0, 1.0, 1.0) if i else (1.0, 0.9, 0.5), 2 if len(s) * 8 < W * k - 24 else 1)
    sep = np.zeros((6, W * k, 4), np.float32)
    sep[..., 3] = 1.0
    img = np.concatenate([top, leg, tx, sep, zl, zoom[:, :W * k], sep, fcl, fcu], 0)
    R.save_png(img, path)
    return path


# ============================================================================ JSON
def write_json(path, doc):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(doc, indent=1, ensure_ascii=False) + "\n")


def _r(v, n=4):
    if isinstance(v, float):
        return round(v, n) if math.isfinite(v) else None
    if isinstance(v, list):
        return [_r(x, n) for x in v]
    if isinstance(v, dict):
        return {a: _r(b, n) for a, b in v.items()}
    return v


# ============================================================================ main
def run(stage):
    L = json.load(open(os.path.join(C.DATA, "stage_%s.json" % stage), encoding="utf-8"))
    R.set_canvas(*R.P.stage_canvas(stage))      # (640 x 400, or wider with overscan; several stages in one run)
    cam = Cam(L)
    W, H = cam.W, cam.H
    C.reset_scene()
    smod = importlib.import_module("hyb_" + stage)
    R.use_preset(smod.PR)
    box = build_and_render(stage, smod)
    ps = box["ps"]
    tab = object_table()
    log("%s: built render_front(Random(%d)) up to its pass %r: %d render-visible meshes, %d ids"
        % (stage, SEED, box["tag"], sum(len(t["obs"]) for t in tab.values()), len(tab)))
    # ---- the painted layers
    front = R.load_png(os.path.join(SPR, "%s_front.png" % stage))
    back = R.load_png(os.path.join(SPR, "%s_back.png" % stage))
    assert front.shape[:2] == (H, W), (stage, front.shape)
    A = front[..., 3] > 0.5
    cover = A.copy()
    pdiff, pmods = {}, {}
    for p in PERIODS:
        fp = os.path.join(SPR, "%s_%s_front.png" % (stage, p))
        if not os.path.isfile(fp):
            pdiff[p] = -1
            continue
        pf = R.load_png(fp)
        if pf.shape[:2] != (H, W):
            raise SystemExit("HYB FDEPTH ERROR %s: size %s != %s" % (fp, pf.shape[:2], (H, W)))
        pa = pf[..., 3] > 0.5
        pdiff[p] = int((pa != A).sum())
        pmods[p] = dict(added=int((pa & ~A).sum()), removed=int((A & ~pa).sum()))
        cover &= pa
    period_only = np.zeros_like(A)
    for p in PERIODS:
        fp = os.path.join(SPR, "%s_%s_front.png" % (stage, p))
        if os.path.isfile(fp):
            period_only |= (R.load_png(fp)[..., 3] > 0.5) & ~A
    hooks = []
    try:
        import hyb_period as PERH
        pm = PERH.module(stage)
        hooks = [h for h in ("front_scene", "back_scene") if getattr(pm, h, None) is not None]
    except Exception as ex:                          # (only informative)
        hooks = ["?%s" % ex]
    log("CHECK periods %s: alpha px differing from %s_front.png: %s; front_scene hook (geometry change): %s"
        % (stage, stage, ", ".join("%s %d (+%d/-%d)" % (p, pdiff[p], pmods.get(p, {}).get("added", 0),
                                                         pmods.get(p, {}).get("removed", 0)) for p in PERIODS),
           "front_scene" in hooks and "YES" or "none"))
    # ---- depth
    raw_ok = ps["a"] & (box["depth"] > 0.05) & (box["depth"] < 1e6)
    dz = np.abs(box["depth"] - ps["depth"])[raw_ok]
    log("%s: precise depth pass vs hyb_core id-pass depth (half float): mean |diff| %.4f, max %.4f m"
        % (stage, float(dz.mean()), float(dz.max())))
    ids0 = np.where(raw_ok, ps["id"], -1)
    flat_ids = [pi for pi, t in tab.items() if t["flat"]]
    D, Hg, Fl, cls, left = fill_depth(box["depth"], box["height"], np.isin(ids0, flat_ids), raw_ok, cover,
                                      water_plane_depth(cam))
    if left.any():
        cover = cover & ~left
    unpainted = raw_ok & ~A
    floating = cover & Fl
    fill = dict(rendered=int((cls == 1).sum()), ring=int((cls == 2).sum()), further=int((cls == 3).sum()),
                waterPlane=int((cls == 5).sum()), none=int((cls == 4).sum()))
    under = int((cover & (Hg < -0.02)).sum())
    log("CHECK fill %s: painted %d px, covered %d; rendered depth %d, outline ring %d, painted details on a prop %d, "
        "on the water plane %d, no depth %d (dropped); rendered but not painted %d (no occluder); floating (B=255) %d px, "
        "below the water line %d px"
        % (stage, int(A.sum()), int(cover.sum()), fill["rendered"], fill["ring"], fill["further"], fill["waterPlane"],
           fill["none"], int(unpainted.sum()), int(floating.sum()), under))
    dv = D[cover]
    near = math.floor(float(dv.min())) - 1.0
    far = math.ceil(float(dv.max())) + 1.0
    img = encode(D, cover, near, far, floating)
    os.makedirs(OUT, exist_ok=True)
    png_tmp = os.path.join(OUT, "%s_front_depth.png" % stage)
    write_png_rgba8(png_tmp, img)
    back_img = read_png_rgba8(png_tmp)
    assert np.array_equal(back_img, img), "PNG round trip"
    dec = decode(back_img, near, far)
    qerr = float(np.nanmax(np.abs(dec - np.where(cover, D, np.nan))))
    gz = cam.unproject(*np.nonzero(cover)[::-1], D[cover])
    info = dict(cover=int(cover.sum()), dMin=float(dv.min()), dMax=float(dv.max()), zMin=float(gz[:, 2].min()),
                zMax=float(gz[:, 2].max()), yMin=float(gz[:, 1].min()), yMax=float(gz[:, 1].max()),
                fill=fill, unpainted=int(unpainted.sum()), unpaintedMask=unpainted, periodOnlyMask=period_only,
                periods=pdiff, floating=int(floating.sum()), floatMask=floating, under=under)
    log("%s: near %.3f far %.3f (step %.3f mm), max quantisation error %.5f m; depth %.3f..%.3f, game z %.2f..%.2f"
        % (stage, near, far, (far - near) / 65535 * 1000, qerr, info["dMin"], info["dMax"], info["zMin"], info["zMax"]))
    # ---- camera, grid and probes
    cpx, cd, nv = check_camera(cam, tab)
    info["camPx"], info["camD"] = cpx, cd
    log("CHECK camera %s: max |Blender camera - Persp(stage JSON)| %.4f px (%d vertices), view depth %.6f m"
        % (stage, cpx, nv, cd))
    cast = raycaster(tab, stage)
    ids = np.where(raw_ok, ps["id"], -1)
    inner = interior(ids, raw_ok & cover & (cls == 1))
    g = grid_check(cam, cast, dec, ids, inner)
    info["grid"] = g
    log("CHECK grid %s: %d rays through pixel centres, |decoded - raycast depth| mean %.5f p99 %.5f max %.5f m, same "
        "object %.1f%%, %d misses; control (map shifted by 1 row): mean %.4f m"
        % (stage, g["n"], g["mean"], g["p99"], g["max"], 100 * g["match"], g["miss"], g["shift_mean"]))
    prb = probes(stage, cam, cast, tab, ids, inner, dec, box["depth"])
    # ---- outputs
    doc = {
        "version": VERSION, "stage": stage, "generator": "Tools/Blender/variants/hybrid/hyb_frontdepth.py",
        "texture": "Sprites/Stages/%s_front_depth" % stage, "alignedWith": "Sprites/Stages/%s_front" % stage,
        "widthPx": W, "heightPx": H,
        "quantity": "camDepth",
        "near": near, "far": far, "empty": EMPTY,
        "decode": "if A < 128: no occluder. code = R*256 + G; depth = near + code * (far - near) / 65535 (metres). B = 255: the occluder is a flat thing floating ON the water (lily pads, duckweed specks: a prop lying within +-0.12 m of the water, or a painted speck on the water plane), 0: a solid prop (optional use: e.g. never let floating pixels hide the float or a splash).",
        "depthFormula": "depth = (p.z + camBack)*cos(pitch) - (p.y - standH - camUp)*sin(pitch) = Persp.ToPixel(p, out depth); "
                        "underwater points: use Persp.Apparent(p) (what the game draws)",
        "occludedWhen": "depth(fragment) > depth(front texel) + bias (bias ~0.05..0.2 m: things ON the water or a deck "
                        "surface sit at the same depth as the surface under them)",
        "texel": "(x, y) = Persp.ToPixel(p); u = 0.5 + x / widthPx; v = 0.5 + y / heightPx (v up, like the front sprite "
                 "centred on the scene origin; follow the front layer's offset, e.g. the ocean boat bob)",
        "inverse": "X=(col+0.5-W/2)*d/f; Yc=(H/2-(row+0.5))*d/f; x=X; y=standH+camUp+Yc*cos-d*sin; z=-camBack+Yc*sin+d*cos "
                   "(col, row top-down)",
        "import": "point filter, no mipmaps, uncompressed, sRGB off, alphaIsTransparency off, npotScale None, clamp",
        "standH": L["standH"], "camBack": L["camBack"], "camUp": L["camUp"], "pitch": L["pitch"], "focalPx": L["focalPx"],
        "coverPx": info["cover"], "depthMin": info["dMin"], "depthMax": info["dMax"],
        "zMin": info["zMin"], "zMax": info["zMax"], "yMin": info["yMin"], "yMax": info["yMax"],
        "floatingPx": info["floating"], "belowWaterPx": info["under"],
        "periodsAlphaDiff": pdiff, "fill": fill, "renderedNotPainted": info["unpainted"],
        "check": dict(cameraPx=cpx, cameraDepth=cd, gridRays=g["n"], gridMeanErr=g["mean"], gridMaxErr=g["max"],
                      gridSameObject=g["match"], gridShiftedRowMeanErr=g["shift_mean"], quantMaxErr=qerr),
        "probes": [{k: v for k, v in p.items() if k not in ("hit",)} for p in prb],
    }
    doc = _r(doc)
    js_tmp = os.path.join(OUT, "frontdepth_%s.json" % stage)
    write_json(js_tmp, doc)
    ov = overlay(stage, cam, L, front, back, D, cover, cls, near, far, prb, info,
                 os.path.join(OUT, "frontdepth_%s_check.png" % stage))
    log("%s: %s, %s, overlay %s" % (stage, png_tmp, js_tmp, ov))
    if not DRY:
        dst = os.path.join(SPR, "%s_front_depth.png" % stage)
        write_png_rgba8(dst, img)
        write_json(os.path.join(C.DATA, "frontdepth_%s.json" % stage), doc)
        log("%s: installed %s and Data/frontdepth_%s.json" % (stage, dst, stage))
    return doc


def main():
    if not ARG_STAGES:
        raise SystemExit("HYB FDEPTH ERROR name a stage (or --all): blender -b --python hyb_frontdepth.py -- lake")
    os.makedirs(WORKD, exist_ok=True)
    res = {}
    for s in ARG_STAGES:
        res[s] = run(s)
    for s, d in res.items():
        c = d["check"]
        log("SUMMARY %-6s near %7.3f far %7.3f  cover %6d px  grid err mean %.5f max %.5f m  probes %s" % (
            s, d["near"], d["far"], d["coverPx"], c["gridMeanErr"], c["gridMaxErr"],
            " ".join("OK" if p.get("ok") else "FAIL" for p in d["probes"])))
    log("done")


if __name__ == "__main__":
    main()
