"""
hybrid - OBSTACLE EXPORT (Docs/obstacles_spec.md 2 and 13).

Builds a stage's Blender scene exactly as hyb_<stage>.py does (its own builder functions, its own seeds) but WITHOUT
rendering: the builder is stopped at its first render pass. Then it stamps the obstacle tags of obstacles/<stage>.py
onto the objects, collects every object tagged `obst`, converts it into GAME space (x right, y up, water y = 0,
z forward from the angler's feet; Blender (X, Y, Z) -> game (X, Z, Y)) and writes obstacles_<stage>.json.

Check (default on, --no-check skips it): the export is projected back onto the painted layers with the stage camera
of Data/stage_<stage>.json (the maths of Persp.ToPixel, not Blender's camera) and compared with
Sprites/Stages/<stage>_front.png:
  HYB OBST CHECK camera   max |JSON camera - Blender camera| over every obstacle vertex (px)
  HYB OBST CHECK painted  the front layer rasterised through the JSON camera vs the PNG's opaque pixels
                          (the painted props = the raster + the 1 px outline the stage post adds)
  HYB OBST CHECK <id>     per solid: raster vs painted edge distance, collider (tiers) vs painted IoU / edge distance
and an overlay preview is written (the exported shapes on the painted stage, plus the pixel-agreement map).

Tagging convention (Blender custom properties, Docs/obstacles_spec.md 13.2). A stage script may stamp them itself;
obstacles/<stage>.py RULES stamp them after the build (the stage scripts stay untouched):
  obst             "solid" | "pad" | "snag" | "weed" | "cover" | "rim"   (required: marks the object as an obstacle)
  obst_id          obstacle id (default: the object's "grp", else its name without a .001 suffix); objects sharing an
                   id and a kind are ONE obstacle (a boulder and its moss cap)
  obst_mat         rock | concrete | wood | root | reed | leaf | hull | crystal | ice | pad | weed | gravel
  obst_tags        comma list (see the spec: abrasive, pocket, near, overhang, ...)
  obst_tiers       solids: stacked height bands of the collider (1..4, default 1)
  obst_shape       auto | poly | circle | capsule (default auto)
  obst_skirt       m: also emit an underwater snag ring "<id>.skirt" (the waterline footprint grown by this)
  obst_skirt_top   y of that ring's top (default -0.3)
  obst_cover       m: also emit a fish cover zone "<id>.cover" (the footprint grown by this) with a hold point
  obst_cover_for   comma list of cover types it counts as (default: the mat)
  obst_weed        m: also emit a weed bed "<id>.weed" (the footprint grown by this; pads, reeds)
  obst_weed_top    y of that bed's top (default -0.15)
  obst_top, obst_bot   override the y range (helpers: -99 = the bed)
  obst_grabK, obst_roughK   multipliers of the material's snag grab / abrasion (default 1)
Export-only helpers (sunken logs, weed beds, rock piles: never painted) are made by obstacles/<stage>.py extra(),
named "OBST_<kind>_<id>", hidden from render.

Run (Blender 5.2):
  blender -b --python Tools/Blender/variants/hybrid/hyb_obstacles.py -- stream             # export, install, check
  blender -b --python Tools/Blender/variants/hybrid/hyb_obstacles.py -- stream lake --dry  # _tmp only
  blender -b --python Tools/Blender/variants/hybrid/hyb_obstacles.py -- --all --no-check
  (or build_obstacles.ps1 -Stage stream [-Dry] [-NoCheck])
Outputs: Tools/Blender/_tmp/variants/hybrid/obstacles/obstacles_<stage>.json, obstacles_<stage>_overlay.png; unless
--dry also Assets/Resources/Data/obstacles_<stage>.json. Never renders, never writes a sprite, never edits a stage file.
"""
import os
import sys
import re
import json
import math
import random
import importlib
import importlib.util

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)


def _parse(argv):
    rest = argv[argv.index("--") + 1:] if "--" in argv else []
    stages, dry, check, every = [], False, True, False
    for a in rest:
        if a == "--dry":
            dry = True
        elif a == "--no-check":
            check = False
        elif a == "--all":
            every = True
        elif a.startswith("-"):
            raise SystemExit("HYB OBST ERROR unknown option %r (use <stage> ... | --all, --dry, --no-check)" % a)
        else:
            stages.append(a)
    return stages, dry, check, every


ARG_STAGES, DRY, CHECK, ALL = _parse(sys.argv)
# the stage scripts import hyb_period, which parses the words after "--" at import time: it must see none
if "--" in sys.argv:
    del sys.argv[sys.argv.index("--"):]

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
from bpy_extras.object_utils import world_to_camera_view  # noqa: E402
import hyb_core as R  # noqa: E402
import fk_common as C  # noqa: E402

VERSION = 1
MOD_DIR = os.path.join(HERE, "obstacles")
OUT = os.path.join(R.OUT, "obstacles")
INSTALL = C.DATA
KINDS = ("solid", "pad", "snag", "weed", "cover", "rim")
MATS = ("rock", "concrete", "wood", "root", "reed", "leaf", "hull", "crystal", "ice", "pad", "weed", "gravel")
BED = -99.0                     # "down to the bed" (the game reads the depth there: StageLayout.DepthAt)
PROPS = ("obst", "obst_id", "obst_mat", "obst_tags", "obst_tiers", "obst_shape", "obst_skirt", "obst_skirt_top",
         "obst_cover", "obst_cover_for", "obst_weed", "obst_weed_top", "obst_top", "obst_bot", "obst_grabK",
         "obst_roughK")


def log(*a):
    print("HYB OBST", *a)
    sys.stdout.flush()


# ============================================================================ plan geometry ((x, z) of game space)
def _cross(o, a, b):
    return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])


def hull2(p):
    """Convex hull, counter-clockwise in (x, z) (x right, z forward = up in a plan view)."""
    p = np.unique(np.round(np.asarray(p, np.float64).reshape(-1, 2), 5), axis=0)
    if len(p) < 3:
        return p
    pts = [tuple(q) for q in p[np.lexsort((p[:, 1], p[:, 0]))]]
    lower, upper = [], []
    for q in pts:
        while len(lower) >= 2 and _cross(lower[-2], lower[-1], q) <= 0:
            lower.pop()
        lower.append(q)
    for q in reversed(pts):
        while len(upper) >= 2 and _cross(upper[-2], upper[-1], q) <= 0:
            upper.pop()
        upper.append(q)
    return np.array(lower[:-1] + upper[:-1])


def simplify(poly, n):
    """Drops the vertex that cuts the least area until at most n are left (a convex polygon stays convex)."""
    poly = [tuple(q) for q in poly]
    while len(poly) > n:
        best, bi = None, 0
        for i in range(len(poly)):
            a, b, c = poly[i - 1], poly[i], poly[(i + 1) % len(poly)]
            ar = abs(_cross(a, b, c))
            if best is None or ar < best:
                best, bi = ar, i
        poly.pop(bi)
    return np.array(poly)


def grow(poly, d, n=16):
    """The convex polygon grown outwards by d metres (rounded corners, <= n vertices)."""
    ang = np.linspace(0.0, 2 * math.pi, 16, endpoint=False)
    ring = np.asarray(poly)[:, None, :] + d * np.stack([np.cos(ang), np.sin(ang)], -1)[None]
    return simplify(hull2(ring.reshape(-1, 2)), n)


def area(poly):
    x, z = poly[:, 0], poly[:, 1]
    return 0.5 * float(np.sum(x * np.roll(z, -1) - np.roll(x, -1) * z))


def centroid(poly):
    x, z = poly[:, 0], poly[:, 1]
    cr = x * np.roll(z, -1) - np.roll(x, -1) * z
    a = cr.sum() / 2.0
    if abs(a) < 1e-9:
        return poly.mean(0)
    return np.array([((x + np.roll(x, -1)) * cr).sum(), ((z + np.roll(z, -1)) * cr).sum()]) / (6.0 * a)


def inside(poly, p):
    for i in range(len(poly)):
        if _cross(poly[i - 1], poly[i], p) < -1e-9:
            return False
    return True


def seg_hits(poly, a, b):
    """Does the plan segment a-b touch the convex CCW polygon? (Cyrus-Beck clip)"""
    a, b = np.asarray(a, float), np.asarray(b, float)
    d = b - a
    t0, t1 = 0.0, 1.0
    for i in range(len(poly)):
        p0, p1 = poly[i - 1], poly[i]
        e = p1 - p0
        n = np.array([e[1], -e[0]])            # outward normal of a CCW edge
        num = float(np.dot(n, a - p0))
        den = float(np.dot(n, d))
        if abs(den) < 1e-12:
            if num > 0:
                return False
            continue
        t = -num / den
        if den < 0:
            t0 = max(t0, t)
        else:
            t1 = min(t1, t)
        if t0 > t1:
            return False
    return True


def fit_shape(poly, want="auto"):
    """-> ("circle" | "capsule" | "poly", params). A circle for round footprints (posts, pads), a capsule for long ones
    (logs, planks), a convex polygon otherwise."""
    c = centroid(poly)
    q = poly - c
    rb = float(np.max(np.linalg.norm(q, axis=1)))
    a = abs(area(poly))
    w, v = np.linalg.eigh(q.T @ q / len(q))
    major, minor = v[:, 1], v[:, 0]
    pa, pb = q @ major, q @ minor
    ln, wd = pa.max() - pa.min(), max(1e-3, pb.max() - pb.min())
    if want == "circle" or (want == "auto" and a / (math.pi * rb * rb) >= 0.8 and ln / wd < 1.2):
        return "circle", dict(x=c[0], z=c[1], rad=math.sqrt(a / math.pi))
    if want == "capsule" or (want == "auto" and ln / wd >= 2.5):
        rad = wd / 2.0
        mid = c + major * (pa.max() + pa.min()) / 2.0 + minor * (pb.max() + pb.min()) / 2.0
        half = max(0.0, ln / 2.0 - rad)
        p0, p1 = mid - major * half, mid + major * half
        return "capsule", dict(x0=p0[0], z0=p0[1], x1=p1[0], z1=p1[1], rad=rad)
    return "poly", {}


# ============================================================================ the stage camera (Persp.cs maths)
class Cam:
    """Game point -> canvas pixel (x right, y DOWN from the top of the widthPx x heightPx stage canvas) exactly as
    Persp.ToPixel + the sprite centred on the scene origin; from the stage JSON only."""

    def __init__(self, L):
        self.L = L
        self.f = float(L["focalPx"])
        a = math.radians(float(L["pitch"]))
        self.s, self.c = math.sin(a), math.cos(a)
        self.cam = np.array([0.0, float(L["standH"]) + float(L["camUp"]), -float(L["camBack"])])
        self.W, self.H = int(L["widthPx"]), int(L["heightPx"])

    def px(self, p):
        """p: (..., 3) game points -> (..., 3) = col, row, depth."""
        p = np.asarray(p, np.float64)
        r = p - self.cam
        depth = np.maximum(0.05, r[..., 2] * self.c - r[..., 1] * self.s)
        yc = r[..., 1] * self.c + r[..., 2] * self.s
        return np.stack([self.W / 2 + self.f * r[..., 0] / depth, self.H / 2 - self.f * yc / depth, depth], -1)

    def apparent(self, p):
        """Persp.Apparent: an underwater point lifted by refraction (where the game draws it)."""
        p = np.array(p, np.float64)
        under = p[..., 1] < 0
        ti = np.hypot(p[..., 0] - self.cam[0], p[..., 2] - self.cam[2]) / self.cam[1]
        st = ti / np.sqrt(1 + ti * ti) / 1.333
        tt = st / np.sqrt(1 - st * st)
        p[..., 1] = np.where(under, p[..., 1] * tt / np.maximum(1e-4, ti), p[..., 1])
        return p


def to_game(w):
    """Blender world (X right, Y forward, Z up) -> game (x right, y up, z forward)."""
    return np.asarray(w)[..., [0, 2, 1]]


# ============================================================================ building a stage without rendering
class _Stop(Exception):
    pass


def load_module(stage):
    path = os.path.join(MOD_DIR, stage + ".py")
    if not os.path.isfile(path):
        raise SystemExit("HYB OBST ERROR no obstacle module %s" % path)
    spec = importlib.util.spec_from_file_location("obst_" + stage, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def build_layer(smod, fn, seed):
    """Runs hyb_<stage>.<fn>(random.Random(seed)) (the stage's own render function: same geometry, same RNG order as
    its main()) and stops it at its first render pass, so the scene is built and the stage camera set, nothing rendered."""
    real = R.render_passes
    hit = []

    def stop(tag, want_ids=True):
        hit.append(tag)
        raise _Stop(tag)
    R.render_passes = stop
    try:
        getattr(smod, fn)(random.Random(seed))
    except _Stop:
        pass
    finally:
        R.render_passes = real
    if not hit:
        raise SystemExit("HYB OBST ERROR %s.%s() returned without reaching its render pass" % (smod.__name__, fn))
    bpy.context.view_layer.update()
    return hit[0]


def base_name(ob):
    return re.sub(r"\.\d{3}$", "", ob.name)


def _rule_hits(rule, ob):
    nm = base_name(ob)
    if "name" in rule:
        names = rule["name"] if isinstance(rule["name"], (list, tuple)) else [rule["name"]]
        if not any(nm.startswith(n) for n in names):
            return False
    if "kind" in rule:
        kinds = rule["kind"] if isinstance(rule["kind"], (list, tuple)) else [rule["kind"]]
        if ob.get("kind") not in kinds:
            return False
    if "grp" in rule and not re.search(rule["grp"], str(ob.get("grp", ""))):
        return False
    if "where" in rule and not rule["where"](ob):
        return False
    return True


def stamp_rules(rules, layer):
    """RULES of obstacles/<stage>.py -> the obst_* custom properties (first matching rule wins; obst=None = skip)."""
    n = 0
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or "obst" in ob or ob.get("_obst_skip"):
            continue
        for rule in rules:
            if rule.get("layer", layer) != layer or not _rule_hits(rule, ob):
                continue
            if rule.get("obst") is None:
                ob["_obst_skip"] = True
                break
            ob["obst"] = rule["obst"]
            for k, v in rule.items():
                if k in ("name", "kind", "grp", "where", "layer", "obst"):
                    continue
                key = "obst_" + k
                if key not in PROPS:
                    raise SystemExit("HYB OBST ERROR unknown rule key %r" % k)
                if k == "id":           # "{grp}.crown": one painted group split into several obstacles
                    v = str(v).format(grp=ob.get("grp", ""), name=base_name(ob), kind=ob.get("kind", ""))
                ob[key] = ",".join(v) if isinstance(v, (list, tuple)) else v
            n += 1
            break
    return n


def helper(kind, oid, verts, faces, **props):
    """An export-only obstacle object (never painted): obstacles/<stage>.py extra() makes them with this.
    verts / faces in BLENDER axes (x right, y forward, z up), like every stage script."""
    me = bpy.data.meshes.new("OBST_%s_%s" % (kind, oid))
    me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    ob = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.hide_render = True
    ob["obst"] = kind
    ob["obst_id"] = oid
    for k, v in props.items():
        ob["obst_" + k] = ",".join(v) if isinstance(v, (list, tuple)) else v
    return ob


def helper_box(kind, oid, c, size, rot_z=0.0, **props):
    """A box helper (Blender axes): c = centre, size = full extents, rotated rot_z radians about the vertical."""
    cs, sn = math.cos(rot_z), math.sin(rot_z)
    v = []
    for dz in (-0.5, 0.5):
        for (dx, dy) in ((-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5)):
            x, y = dx * size[0], dy * size[1]
            v.append((c[0] + x * cs - y * sn, c[1] + x * sn + y * cs, c[2] + dz * size[2]))
    f = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return helper(kind, oid, v, f, **props)


def mesh_data(ob):
    """World-space vertices (game axes) and triangles of an object (evaluated: modifiers applied)."""
    dg = bpy.context.evaluated_depsgraph_get()
    oe = ob.evaluated_get(dg)
    me = oe.to_mesh()
    n = len(me.vertices)
    co = np.empty(n * 3, np.float64)
    me.vertices.foreach_get("co", co)
    me.calc_loop_triangles()
    tri = np.empty(len(me.loop_triangles) * 3, np.int32)
    me.loop_triangles.foreach_get("vertices", tri)
    M = np.array(ob.matrix_world)
    w = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    oe.to_mesh_clear()
    return to_game(w), tri.reshape(-1, 3)


# ============================================================================ objects -> obstacles
def _f(ob, key, default):
    v = ob.get(key)
    return default if v is None else v


def _tags(s):
    return [t.strip() for t in str(s or "").split(",") if t.strip()]


def limits_of(L):
    return float(L["xLim"]), float(L["zNear"]), float(L["zFar"])


def hold_point(poly, L, skirt, anchor=(0.0, 0.0)):
    """Where a fish holds in a cover. Best: just behind the structure as seen from the angler, inside the fishable water,
    so the line to it crosses the footprint (holdCross). A structure at the edge of the fishable water (the bank
    boulders) has no such point: then the fish tucks in against its channel side, inside its skirt (it grinds the line
    on the rock's base there). -> ((x, z), crosses?), or None when a hooked fish can never get there (the fight keeps
    it within |x| <= xLim - 0.3, zNear + 0.2 .. zFar - 1)."""
    xl, zn, zf = limits_of(L)
    c = centroid(poly)
    u0 = c - np.asarray(anchor)
    u0 = u0 / max(1e-6, np.linalg.norm(u0))

    def ok(h):
        return abs(h[0]) <= xl - 0.3 and zn + 0.5 <= h[1] <= zf - 1.0

    for dang in (0, 15, -15, 30, -30, 45, -45, 60, -60, 75, -75, 90, -90):
        a = math.radians(dang)
        u = np.array([u0[0] * math.cos(a) - u0[1] * math.sin(a), u0[0] * math.sin(a) + u0[1] * math.cos(a)])
        h = c + u * (float(np.max((poly - c) @ u)) + 0.35)
        if ok(h) and seg_hits(poly, anchor, h):
            return h, True
    ring = grow(poly, max(0.1, 0.6 * skirt) if skirt > 0 else 0.2, 32)
    cand = [q for q in ring if ok(q)]
    if not cand:
        return None
    best = min(cand, key=lambda q: abs(q[0]))               # the channel side (nearest the middle of the water)
    return np.asarray(best), bool(seg_hits(poly, anchor, best))


def entry(oid, kind, mat, poly, bot, top, tags, layer, src, shape="auto", parent="", tiers=None, bed=False,
          hold=None, cover_for=None, grabK=1.0, roughK=1.0):
    poly = np.asarray(poly)
    c = centroid(poly)
    sh, sp = fit_shape(poly, shape)
    e = dict(id=oid, kind=kind, mat=mat, shape=sh, x=c[0], z=c[1],
             r=float(np.max(np.linalg.norm(poly - c, axis=1))),
             rad=sp.get("rad", 0.0), x0=sp.get("x0", 0.0), z0=sp.get("z0", 0.0), x1=sp.get("x1", 0.0),
             z1=sp.get("z1", 0.0), pts=poly.reshape(-1).tolist(), bot=bot, top=top, bed=bool(bed),
             tiers=tiers or [], grabK=grabK, roughK=roughK, tags=tags, coverFor=cover_for or [],
             hx=0.0, hz=0.0, holdCross=False, layer=layer, parent=parent, src=src)
    if hold is not None:
        e["hx"], e["hz"] = float(hold[0][0]), float(hold[0][1])
        e["holdCross"] = bool(hold[1])
    return e


def collect(layer, L, keep_raw):
    """Every object tagged obst in the current scene -> obstacle entries (+ derived skirts / covers / weed beds)."""
    groups = {}
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or "obst" not in ob:
            continue
        kind = str(ob["obst"])
        if kind not in KINDS:
            raise SystemExit("HYB OBST ERROR %s: obst=%r (use %s)" % (ob.name, kind, " | ".join(KINDS)))
        oid = str(ob.get("obst_id") or ob.get("grp") or base_name(ob))
        groups.setdefault((kind, oid), []).append(ob)
    out, raw, no_cover = [], {}, []
    for (kind, oid), obs in sorted(groups.items(), key=lambda kv: kv[0][1]):
        ob0 = obs[0]
        mat = str(_f(ob0, "obst_mat", "rock"))
        if mat not in MATS:
            raise SystemExit("HYB OBST ERROR %s: obst_mat=%r (use %s)" % (oid, mat, " | ".join(MATS)))
        tags = _tags(ob0.get("obst_tags"))
        shape = str(_f(ob0, "obst_shape", "auto"))
        g_all, tris = [], []
        base = 0
        for ob in obs:
            g, t = mesh_data(ob)
            g_all.append(g)
            tris.append(t + base)
            base += len(g)
        g = np.concatenate(g_all)
        y = g[:, 1]
        src = sorted({base_name(o) for o in obs})
        grabK, roughK = float(_f(ob0, "obst_grabK", 1.0)), float(_f(ob0, "obst_roughK", 1.0))
        if kind == "solid":
            ymin, top = float(y.min()), float(y.max())
            bot = ymin if ymin > 0.05 else 0.0            # an overhang floats above the water; a rock stands in it
            bed = ymin < -0.1                             # a post / stem goes on down to the bed
            if ob0.get("obst_bot") is not None:
                bot = float(ob0["obst_bot"])
            if ob0.get("obst_top") is not None:
                top = float(ob0["obst_top"])
            above = g[y >= bot - 1e-4]
            n = max(1, min(4, int(_f(ob0, "obst_tiers", 1))))
            h = max(1e-3, top - bot)
            tiers = []
            for k in range(n):
                lo, hi = bot + h * k / n, bot + h * (k + 1) / n
                ya = above[:, 1]
                sel = above[(ya >= lo - 0.02) & (ya <= hi + 0.02)]
                if len(sel) < 3:
                    sel = above[ya >= lo - 0.02]
                pl = simplify(hull2(sel[:, [0, 2]]), 12)
                if len(pl) < 3:
                    continue
                tiers.append(dict(y0=lo, y1=hi, pts=pl.reshape(-1).tolist()))
            foot = np.array(tiers[0]["pts"]).reshape(-1, 2)
            e = entry(oid, kind, mat, foot, bot, top, tags, layer, src, shape, tiers=tiers, bed=bed, grabK=grabK,
                      roughK=roughK)
        elif kind == "rim":
            low = g[y <= y.min() + 0.03]
            c = low[:, [0, 2]].mean(0)
            rad = float(np.mean(np.linalg.norm(low[:, [0, 2]] - c, axis=1)))
            ang = np.linspace(0, 2 * math.pi, 16, endpoint=False)
            foot = c + rad * np.stack([np.cos(ang), np.sin(ang)], -1)
            e = entry(oid, kind, mat, foot, float(_f(ob0, "obst_bot", y.min())), float(_f(ob0, "obst_top", y.max())),
                      tags, layer, src, "circle", grabK=grabK, roughK=roughK)
        else:
            foot = simplify(hull2(g[:, [0, 2]]), 16)
            bot = float(_f(ob0, "obst_bot", y.min()))
            top = float(_f(ob0, "obst_top", y.max()))
            e = entry(oid, kind, mat, foot, bot, top, tags, layer, src, shape, grabK=grabK, roughK=roughK)
        out.append(e)
        # ---- derived zones (all in the water under / around the painted prop)
        skirt = float(_f(ob0, "obst_skirt", 0.0))
        if skirt > 0:
            out.append(entry(oid + ".skirt", "snag", mat, grow(foot, skirt), BED, float(_f(ob0, "obst_skirt_top", -0.3)),
                             [t for t in tags if t in ("rock", "abrasive")] or [mat], layer, src, "poly", parent=oid,
                             grabK=grabK, roughK=roughK))
        cover = float(_f(ob0, "obst_cover", 0.0))
        hold = hold_point(foot, L, skirt) if cover > 0 else None
        if cover > 0 and hold is None:
            no_cover.append(oid)
        elif cover > 0:
            zone = grow(foot, cover)
            out.append(entry(oid + ".cover", "cover", mat, zone, BED, -0.2, [], layer, src, "poly", parent=oid,
                             hold=hold, cover_for=_tags(ob0.get("obst_cover_for")) or [mat]))
        weed = float(_f(ob0, "obst_weed", 0.0))
        if weed > 0:
            out.append(entry(oid + ".weed", "weed", "weed", grow(foot, weed), BED, float(_f(ob0, "obst_weed_top", -0.15)),
                             ["weed"], layer, src, "poly", parent=oid))
        if keep_raw:
            raw[oid if kind == "solid" else kind + ":" + oid] = dict(g=g, tri=np.concatenate(tris), kind=kind, entry=e)
    if no_cover:
        log("%s: no cover zone for %d obstacles (a hooked fish can never reach a hold point): %s"
            % (layer, len(no_cover), " ".join(no_cover)))
    return out, raw


def reach_filter(obst, L):
    """Drops obstacles that never touch the fishable water (|x| <= xLim + 0.3, zNear - 1 <= z <= zFar), with their
    derived zones."""
    xl, zn, zf = limits_of(L)
    keep_ids = set()
    for e in obst:
        if e["parent"]:
            continue
        pts = np.array(e["pts"]).reshape(-1, 2)
        grown = pts
        for d in obst:
            if d["parent"] == e["id"]:
                grown = np.concatenate([grown, np.array(d["pts"]).reshape(-1, 2)])
        if np.min(np.abs(grown[:, 0])) <= xl + 0.3 and grown[:, 1].max() >= zn - 1.0 and grown[:, 1].min() <= zf:
            keep_ids.add(e["id"])
    out = [e for e in obst if (e["parent"] or e["id"]) in keep_ids]
    return out, len([e for e in obst if not e["parent"]]) - len(keep_ids)


# ============================================================================ JSON
def _round(v):
    if isinstance(v, float):
        return round(v, 3)
    if isinstance(v, list):
        return [_round(x) for x in v]
    if isinstance(v, dict):
        return {k: _round(x) for k, x in v.items()}
    return v


def write_json(path, doc):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    head = {k: v for k, v in doc.items() if k != "obstacles"}
    lines = ["{"]
    for k, v in head.items():
        lines.append(" %s: %s," % (json.dumps(k), json.dumps(_round(v), ensure_ascii=False)))
    lines.append(' "obstacles": [')
    obs = doc["obstacles"]
    for i, e in enumerate(obs):
        lines.append("  " + json.dumps(_round(e), ensure_ascii=False, separators=(",", ":")) + ("," if i < len(obs) - 1 else ""))
    lines.append(" ]")
    lines.append("}")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


# ============================================================================ check: raster through the JSON camera
def raster(tri_px, ids, W, H):
    """Z-buffered pixel-centre raster of screen triangles. tri_px: (T, 3, 3) col, row, depth. -> (id buffer, depth)."""
    zb = np.full((H, W), np.inf)
    ib = np.full((H, W), -1, np.int32)
    for t in range(len(tri_px)):
        a, b, c = tri_px[t]
        x0 = max(0, int(math.floor(min(a[0], b[0], c[0]) - 0.5)))
        x1 = min(W, int(math.ceil(max(a[0], b[0], c[0]) + 0.5)))
        y0 = max(0, int(math.floor(min(a[1], b[1], c[1]) - 0.5)))
        y1 = min(H, int(math.ceil(max(a[1], b[1], c[1]) + 0.5)))
        if x1 <= x0 or y1 <= y0:
            continue
        ar = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        if abs(ar) < 1e-9:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        w0 = ((b[0] - X) * (c[1] - Y) - (b[1] - Y) * (c[0] - X)) / ar
        w1 = ((c[0] - X) * (a[1] - Y) - (c[1] - Y) * (a[0] - X)) / ar
        w2 = 1.0 - w0 - w1
        m = (w0 >= -1e-9) & (w1 >= -1e-9) & (w2 >= -1e-9)
        if not m.any():
            continue
        d = w0 * a[2] + w1 * b[2] + w2 * c[2]
        sub_z = zb[y0:y1, x0:x1]
        sub_i = ib[y0:y1, x0:x1]
        upd = m & (d < sub_z)
        sub_z[upd] = d[upd]
        sub_i[upd] = ids[t]
    return ib, zb


def poly_mask(poly2d, W, H):
    """Pixel-centre mask of a convex screen polygon (any winding)."""
    p = np.asarray(poly2d, np.float64)
    h = hull2(p)
    m = np.zeros((H, W), bool)
    if len(h) < 3:
        return m
    x0, x1 = max(0, int(math.floor(h[:, 0].min()))), min(W, int(math.ceil(h[:, 0].max())) + 1)
    y0, y1 = max(0, int(math.floor(h[:, 1].min()))), min(H, int(math.ceil(h[:, 1].max())) + 1)
    if x1 <= x0 or y1 <= y0:
        return m
    X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
    ok = np.ones(X.shape, bool)
    for i in range(len(h)):
        a, b = h[i - 1], h[i]
        ok &= (b[0] - a[0]) * (Y - a[1]) - (b[1] - a[1]) * (X - a[0]) >= -1e-9
    m[y0:y1, x0:x1] = ok
    return m


def _nb4_or(m):
    d = m.copy()
    d[1:] |= m[:-1]
    d[:-1] |= m[1:]
    d[:, 1:] |= m[:, :-1]
    d[:, :-1] |= m[:, 1:]
    return d


def edge(m):
    e = m & ~(np.pad(m, 1)[2:, 1:-1] & np.pad(m, 1)[:-2, 1:-1] & np.pad(m, 1)[1:-1, 2:] & np.pad(m, 1)[1:-1, :-2])
    return np.argwhere(e).astype(np.float64)


def edge_dist(ma, mb):
    """Symmetric edge distance between two masks (px): (mean, max); (nan, nan) if one is empty."""
    ea, eb = edge(ma), edge(mb)
    if len(ea) == 0 or len(eb) == 0:
        return float("nan"), float("nan")

    def one(p, q):
        out = np.empty(len(p))
        for i in range(0, len(p), 2048):
            d = np.sqrt(((p[i:i + 2048, None, :] - q[None, :, :]) ** 2).sum(-1))
            out[i:i + 2048] = d.min(1)
        return out
    d = np.concatenate([one(ea, eb), one(eb, ea)])
    return float(d.mean()), float(d.max())


def tier_silhouette(e, cam, W, H):
    """The screen silhouette of a solid's collider: the union of its tiers (each a vertical prism)."""
    m = np.zeros((H, W), bool)
    for t in e["tiers"]:
        pts = np.array(t["pts"]).reshape(-1, 2)
        corners = np.concatenate([np.stack([pts[:, 0], np.full(len(pts), t["y0"]), pts[:, 1]], -1),
                                  np.stack([pts[:, 0], np.full(len(pts), t["y1"]), pts[:, 1]], -1)])
        m |= poly_mask(cam.px(corners)[:, :2], W, H)
    return m


def check_camera(raw, cam):
    """JSON camera (Persp maths from stage_<id>.json) vs Blender's stage camera on every obstacle vertex."""
    sc = bpy.context.scene
    bc = sc.camera
    W, H = sc.render.resolution_x, sc.render.resolution_y
    worst, n = 0.0, 0
    for r in raw.values():
        g = r["g"]
        pj = cam.px(g)
        for k in range(0, len(g), max(1, len(g) // 400)):
            v = world_to_camera_view(sc, bc, Vector(g[k][[0, 2, 1]]))      # (game -> Blender axes)
            col, row = v.x * W, (1.0 - v.y) * H
            worst = max(worst, abs(col - pj[k, 0]), abs(row - pj[k, 1]))
            n += 1
    return worst, n


def layer_raster(cam, W, H):
    """Every visible mesh of the built layer through the JSON camera: -> id buffer (obstacle group index or -2 for
    non-obstacles), and the list of group ids."""
    tri_all, id_all, names = [], [], []
    gidx = {}
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or ob.hide_render:
            continue
        g, tri = mesh_data(ob)
        if len(tri) == 0:
            continue
        if "obst" in ob and str(ob["obst"]) == "solid":
            oid = str(ob.get("obst_id") or ob.get("grp") or base_name(ob))
            if oid not in gidx:
                gidx[oid] = len(names)
                names.append(oid)
            gi = gidx[oid]
        else:
            gi = -2
        p = cam.px(g)
        tri_all.append(p[tri])
        id_all.append(np.full(len(tri), gi, np.int32))
    ib, _ = raster(np.concatenate(tri_all), np.concatenate(id_all), W, H)
    return ib, names


def painted_check(stage, cam, ib, names, obst, front_png):
    """Front PNG opaque pixels vs the JSON-camera raster of the rebuilt front layer; per solid obstacle the collider
    silhouette vs its painted pixels."""
    W, H = cam.W, cam.H
    A = front_png[..., 3] > 0.5
    S = ib != -1
    ring = A & ~S & _nb4_or(S)
    stray = A & ~_nb4_or(S)
    unpainted = S & ~A
    log("CHECK painted %s: %d opaque px, raster %d px; outline ring %d px; unexplained paint %d px; raster not painted "
        "%d px" % (stage, int(A.sum()), int(S.sum()), int(ring.sum()), int(stray.sum()), int(unpainted.sum())))
    # the outline ring belongs to the neighbouring object (the stage post's outer_outline order: above, below, left, right)
    own = ib.copy()
    for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        src = np.full((H, W), -1, np.int32)            # src[y, x] = ib[y + dy, x + dx]
        src[max(0, -dy):H - max(0, dy), max(0, -dx):W - max(0, dx)] = ib[max(0, dy):H - max(0, -dy), max(0, dx):W - max(0, -dx)]
        take = ring & (own == -1) & (src != -1)
        own[take] = src[take]
    by_id = {e["id"]: e for e in obst}
    rows = []
    for gi, oid in enumerate(names):
        e = by_id.get(oid)
        if e is None:
            continue
        mesh = ib == gi
        painted = A & (own == gi)
        if painted.sum() < 4:
            rows.append((oid, int(painted.sum()), None))
            continue
        others = A & (own != gi) & (own != -1)
        coll = tier_silhouette(e, cam, W, H) & ~others        # the parts hidden behind nearer props do not count
        md = edge_dist(mesh, painted)
        cd = edge_dist(coll, painted)
        iou = float((coll & painted).sum()) / max(1, int((coll | painted).sum()))
        cy, cx = np.argwhere(painted).mean(0)
        qy, qx = np.argwhere(coll).mean(0) if coll.any() else (cy, cx)
        rows.append((oid, int(painted.sum()), dict(mesh_mean=md[0], mesh_max=md[1], coll_mean=cd[0], coll_max=cd[1],
                                                   iou=iou, dc=math.hypot(qx - cx, qy - cy))))
    good = [r[2] for r in rows if r[2]]
    for oid, npx, m in rows:
        if m is None:
            log("CHECK %-12s %4d px painted (hidden: skipped)" % (oid, npx))
        else:
            log("CHECK %-12s %4d px  painted-vs-export edge %.2f / max %.2f px   collider IoU %.2f, edge %.2f / max %.2f px,"
                " centre offset %.2f px" % (oid, npx, m["mesh_mean"], m["mesh_max"], m["iou"], m["coll_mean"], m["coll_max"],
                                            m["dc"]))
    summary = {}
    if good:
        summary = dict(n=len(good), mesh_mean=float(np.mean([m["mesh_mean"] for m in good])),
                       mesh_max=float(np.max([m["mesh_max"] for m in good])),
                       coll_mean=float(np.mean([m["coll_mean"] for m in good])),
                       coll_max=float(np.max([m["coll_max"] for m in good])),
                       iou=float(np.mean([m["iou"] for m in good])), dc=float(np.mean([m["dc"] for m in good])))
        log("CHECK summary %s: %d solids; painted-vs-export edge mean %.2f px (max %.2f); collider edge mean %.2f px (max "
            "%.2f), IoU %.2f, centre offset %.2f px" % (stage, summary["n"], summary["mesh_mean"], summary["mesh_max"],
                                                       summary["coll_mean"], summary["coll_max"], summary["iou"], summary["dc"]))
    return dict(A=A, S=S, ring=ring, stray=stray, unpainted=unpainted, summary=summary)


# ============================================================================ overlay preview
COL = dict(solid=(1.0, 0.88, 0.25), snag=(0.38, 0.88, 1.0), weed=(0.66, 0.94, 0.44), pad=(0.44, 0.94, 0.63),
           cover=(1.0, 0.44, 0.85), rim=(1.0, 1.0, 1.0), crop=(1.0, 1.0, 1.0))


def _put(img, x, y, col, a=1.0):
    x, y = int(round(x)), int(round(y))
    if 0 <= y < img.shape[0] and 0 <= x < img.shape[1]:
        img[y, x, :3] = img[y, x, :3] * (1 - a) + np.array(col) * a
        img[y, x, 3] = 1.0


def _poly_line(img, pts, col, a=1.0, dash=None, closed=True):
    pts = list(pts) + ([pts[0]] if closed else [])
    k = 0
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(n):
            t = i / n
            if dash is None or (k // dash) % 2 == 0:
                _put(img, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, col, a)
            k += 1


def _mask_edge_draw(img, m, k, col, a=1.0, fill=0.0):
    big = R.upscale(m[..., None].astype(np.float32), k)[..., 0] > 0.5
    if fill > 0:
        img[big, :3] = img[big, :3] * (1 - fill) + np.array(col) * fill
    e = big & ~(np.pad(big, 1)[2:, 1:-1] & np.pad(big, 1)[:-2, 1:-1] & np.pad(big, 1)[1:-1, 2:] & np.pad(big, 1)[1:-1, :-2])
    img[e, :3] = img[e, :3] * (1 - a) + np.array(col) * a


def overlay(stage, cam, obst, back_png, front_png, chk, path, k=2):
    W, H = cam.W, cam.H
    base = back_png.copy()
    R.over(base, front_png, 0, 0)
    top = R.upscale(base, k).copy()
    # the export on the painted stage
    for e in sorted(obst, key=lambda e: {"cover": 0, "weed": 1, "snag": 2, "pad": 3, "rim": 4, "solid": 5}[e["kind"]]):
        pts = np.array(e["pts"]).reshape(-1, 2)
        col = COL[e["kind"]]
        if e["kind"] == "solid":
            _mask_edge_draw(top, tier_silhouette(e, cam, W, H), k, col, 1.0, fill=0.22)
            continue
        y = e["top"] if e["kind"] != "rim" else 0.0
        p3 = np.stack([pts[:, 0], np.full(len(pts), y), pts[:, 1]], -1)
        if y < 0:
            p3 = cam.apparent(p3)
        sp = cam.px(p3)[:, :2] * k
        _poly_line(top, [tuple(q) for q in sp], col, 0.95, dash=None if e["kind"] in ("pad", "rim") else 3)
        if e["kind"] == "cover":
            h = cam.px(cam.apparent(np.array([e["hx"], -0.5, e["hz"]])))[:2] * k
            for d in range(-3, 4):
                _put(top, h[0] + d, h[1], col)
                _put(top, h[0], h[1] + d, col)
    x0, y0, cw, ch = R.CROP
    _poly_line(top, [(x0 * k, y0 * k), ((x0 + cw) * k - 1, y0 * k), ((x0 + cw) * k - 1, (y0 + ch) * k - 1),
                     (x0 * k, (y0 + ch) * k - 1)], COL["crop"], 0.5, dash=4)
    # the pixel agreement of the front layer (raster through the JSON camera vs the PNG)
    bot = np.zeros((H, W, 4), np.float32)
    lum = (base[..., :3] * np.array([0.3, 0.55, 0.15])).sum(-1)
    bot[..., :3] = (0.18 * lum)[..., None]
    bot[..., 3] = 1.0
    if chk:
        agree = chk["A"] & chk["S"]
        bot[agree, :3] = (0.33, 0.62, 0.40)
        bot[chk["ring"], :3] = (0.86, 0.80, 0.32)
        bot[chk["stray"], :3] = (1.0, 0.2, 0.2)
        bot[chk["unpainted"], :3] = (0.25, 0.4, 1.0)
    bot = R.upscale(bot, k)
    sep = np.zeros((6, W * k, 4), np.float32)
    sep[..., 3] = 1.0
    panels = [top, sep, bot]
    # a close-up of the far play area (the solids beyond 8 m), drawn the same way at a larger scale
    far = [e for e in obst if e["kind"] == "solid" and e["z"] > 8.0]
    if far:
        pts = np.concatenate([cam.px(np.stack([np.array(t["pts"]).reshape(-1, 2)[:, 0],
                                               np.full(len(t["pts"]) // 2, t["y1"]),
                                               np.array(t["pts"]).reshape(-1, 2)[:, 1]], -1))[:, :2]
                              for e in far for t in e["tiers"]])
        c0, c1 = max(0, int(pts[:, 0].min()) - 8), min(W, int(pts[:, 0].max()) + 9)
        r0, r1 = max(0, int(pts[:, 1].min()) - 8), min(H, int(pts[:, 1].max()) + 12)
        kz = max(k, (W * k) // max(1, c1 - c0))
        zoom = R.upscale(base[r0:r1, c0:c1], kz).copy()
        for e in far:
            m = tier_silhouette(e, cam, W, H)[r0:r1, c0:c1]
            _mask_edge_draw(zoom, m, kz, COL["solid"], 1.0, fill=0.18)
        for e in obst:
            if e["kind"] in ("snag", "cover") and e["z"] > 8.0:
                p = np.array(e["pts"]).reshape(-1, 2)
                p3 = cam.apparent(np.stack([p[:, 0], np.full(len(p), e["top"]), p[:, 1]], -1))
                sp = (cam.px(p3)[:, :2] - np.array([c0, r0])) * kz
                _poly_line(zoom, [tuple(q) for q in sp], COL[e["kind"]], 0.95, dash=4)
        pad = np.zeros((zoom.shape[0], W * k, 4), np.float32)
        pad[..., 3] = 1.0
        pad[:, :min(W * k, zoom.shape[1])] = zoom[:, :W * k]
        panels += [sep, pad]
    img = np.concatenate(panels, 0)
    R.save_png(img, path)
    return path


# ============================================================================ main
def run(stage):
    mod = load_module(stage)
    smod = importlib.import_module("hyb_" + stage)
    R.use_preset(smod.PR)                     # (colours only; the geometry anchors live in the stage module)
    L = json.load(open(os.path.join(C.DATA, "stage_%s.json" % stage), encoding="utf-8"))
    cam = Cam(L)
    W, H = cam.W, cam.H
    obst, chk_data = [], None
    for (layer, fn, seed) in mod.LAYERS:
        C.reset_scene()
        tag = build_layer(smod, fn, seed)
        n = stamp_rules(getattr(mod, "RULES", []), layer)
        extra = getattr(mod, "extra", None)
        nh = 0
        if extra is not None:
            ctx = dict(stage=stage, layer=layer, L=L, smod=smod, helper=helper, helper_box=helper_box, R=R,
                       scene=bpy.context.scene)
            nh = len(extra(ctx) or [])
        bpy.context.view_layer.update()
        found, raw = collect(layer, L, keep_raw=True)
        log("%s %s: built %s() up to its pass %r, %d objects stamped by rules, %d helpers, %d entries"
            % (stage, layer, fn, tag, n, nh, len(found)))
        obst += found
        if CHECK:
            worst, nv = check_camera(raw, cam)
            log("CHECK camera %s %s: max |JSON camera - Blender camera| = %.4f px over %d vertices" % (stage, layer, worst, nv))
            if layer == "front":
                ib, names = layer_raster(cam, W, H)
                chk_data = (ib, names)
    obst, dropped = reach_filter(obst, L)
    if dropped:
        log("%s: %d obstacles never touch the fishable water (dropped)" % (stage, dropped))
    order = {"solid": 0, "rim": 1, "pad": 2, "snag": 3, "weed": 4, "cover": 5}
    obst.sort(key=lambda e: (order[e["kind"]], e["z"], e["x"]))
    keys = ("standH", "camBack", "camUp", "pitch", "focalPx", "widthPx", "heightPx", "xLim", "zNear", "zFar")
    doc = dict(version=VERSION, stage=stage, generator="hyb_obstacles.py",
               source="hyb_%s.py %s" % (stage, ", ".join("%s(Random(%d))" % (fn, s) for (_, fn, s) in mod.LAYERS)),
               camera={k: L[k] for k in keys}, obstacles=obst)
    tmp = os.path.join(OUT, "obstacles_%s.json" % stage)
    write_json(tmp, doc)
    counts = {}
    for e in obst:
        counts[e["kind"]] = counts.get(e["kind"], 0) + 1
    log("%s: %s -> %s" % (stage, ", ".join("%d %s" % (v, k) for k, v in sorted(counts.items())), tmp))
    if not DRY:
        dst = os.path.join(INSTALL, "obstacles_%s.json" % stage)
        write_json(dst, doc)
        log("%s: installed %s" % (stage, dst))
    if CHECK:
        spr = os.path.join(C.SPRITES, "Stages")
        back = R.load_png(os.path.join(spr, "%s_back.png" % stage))
        front = R.load_png(os.path.join(spr, "%s_front.png" % stage))
        chk = None
        if chk_data is not None:
            ib, names = chk_data
            chk = painted_check(stage, cam, ib, names, obst, front)
        hook = getattr(mod, "check", None)
        if hook is not None:
            hook(dict(stage=stage, obstacles=obst, L=L, log=log))
        p = overlay(stage, cam, obst, back, front, chk, os.path.join(OUT, "obstacles_%s_overlay.png" % stage))
        log("%s: overlay %s" % (stage, p))
    return doc


def main():
    stages = list(ARG_STAGES)
    if ALL:
        stages = sorted(f[:-3] for f in os.listdir(MOD_DIR) if f.endswith(".py") and not f.startswith("_"))
    if not stages:
        raise SystemExit("HYB OBST ERROR name a stage (or --all): blender -b --python hyb_obstacles.py -- stream")
    for s in stages:
        run(s)
    log("done")


if __name__ == "__main__":
    main()
