"""
retro16 - shared core for the "classic 16-bit" art variant.

Pipeline (per asset):
  1. build detailed geometry with *palette ramp* materials: every material emits exact palette colours
     (hard CONSTANT bands) except gradients, which interpolate linearly between two neighbouring
     palette colours inside narrow transition zones;
  2. render a colour pass (EXR, linear) and an ID/depth pass (every object gets pass_index; the ID
     material writes object index + camera depth);
  3. numpy post-process: quantise to the asset palette with ORDERED Bayer 4x4 dithering (only the
     in-between gradient pixels dither, exact palette pixels stay put), despeckle orphan pixels inside
     one object, inner contour lines on depth discontinuities (on the farther pixel), selective outer
     outline (dark hue-shifted on the shadow side, one step darker on the lit side).

Arrays in this module are TOP-DOWN (row 0 = top of the image), sRGB 0..1 unless noted.
Never writes outside Tools/Blender/_tmp/variants/retro16.
"""
import sys
import os
import math
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
BL = os.path.abspath(os.path.join(HERE, "..", ".."))
if BL not in sys.path:
    sys.path.insert(0, BL)
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import bpy  # noqa: E402
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

OUT = os.path.join(BL, "_tmp", "variants", "retro16")
WORK = os.path.join(OUT, "work")
os.makedirs(WORK, exist_ok=True)

# light: upper-left, from behind the viewer's left shoulder (towards the light)
LIGHT = Vector((-0.55, -0.5, 0.67)).normalized()


# ============================================================================ colour maths
def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float64) / 255.0


def tohex(rgb):
    r = np.clip(np.round(np.asarray(rgb, np.float64) * 255), 0, 255).astype(int)
    return "#%02x%02x%02x" % tuple(r)


def s2l(x):
    x = np.asarray(x, np.float64)
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def l2s(x):
    x = np.clip(np.asarray(x, np.float64), 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)


def oklab(srgb):
    c = s2l(srgb)
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    l_ = np.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
    m_ = np.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
    s_ = np.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)
    return np.stack([0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
                     1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
                     0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_], -1)


def oklab_inv(lab):
    L, a, b = lab[..., 0], lab[..., 1], lab[..., 2]
    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b
    l, m, s = l_ ** 3, m_ ** 3, s_ ** 3
    rgb = np.stack([4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s], -1)
    return l2s(rgb)


def _rot_hue(a, b, target_deg, amount_deg):
    h = math.degrees(math.atan2(b, a))
    d = (target_deg - h + 540) % 360 - 180
    step = max(-abs(amount_deg), min(abs(amount_deg), d))
    h2 = math.radians(h + step)
    ch = math.hypot(a, b)
    return ch * math.cos(h2), ch * math.sin(h2)


def shade(hexc, dl, hue=None, chroma=1.0):
    """Hue-shifted shade: dl < 0 darker (hue -> blue/violet), dl > 0 lighter (hue -> warm yellow)."""
    L, a, b = oklab(hexrgb(hexc))
    if hue is None:
        hue = 18.0 * abs(dl) / 0.1
    tgt = 285.0 if dl < 0 else 95.0
    ch = math.hypot(a, b)
    if ch > 0.01:
        a, b = _rot_hue(a, b, tgt, hue)
    k = chroma if chroma != 1.0 else (1.08 if dl < 0 and L + dl > 0.35 else (0.85 if dl < 0 else 0.9))
    return tohex(oklab_inv(np.array([min(0.99, max(0.02, L + dl)), a * k, b * k])))


def ramp_from(hexc, dark=2, light=1, dd=0.095, dlgt=0.075):
    """dark->light list of hue-shifted shades around a base colour."""
    out = [shade(hexc, -dd * i) for i in range(dark, 0, -1)] + [hexc]
    out += [shade(hexc, dlgt * i) for i in range(1, light + 1)]
    return out


# ============================================================================ palette
class Pal:
    def __init__(self, hexes):
        seen = []
        for h in hexes:
            h = h.lower()
            if h not in seen:
                seen.append(h)
        self.hex = seen
        self.srgb = np.array([hexrgb(h) for h in seen])
        self.lin = s2l(self.srgb)
        self.lab = oklab(self.srgb)
        self._dark = {}

    def __len__(self):
        return len(self.hex)

    def index(self, h):
        return self.hex.index(h.lower())

    def darker(self, i, steps=1, dl=0.1):
        key = (i, steps)
        if key in self._dark:
            return self._dark[key]
        L, a, b = self.lab[i]
        tl = L - dl * steps
        a2, b2 = _rot_hue(a, b, 285.0, 12.0 * steps) if math.hypot(a, b) > 0.01 else (a, b)
        best, bi = 1e9, i
        for j in range(len(self.hex)):
            Lj, aj, bj = self.lab[j]
            if Lj > L - 0.035 * steps:
                continue
            d = (Lj - tl) ** 2 * 1.0 + ((aj - a2) ** 2 + (bj - b2) ** 2) * 2.2
            if d < best:
                best, bi = d, j
        if bi == i:  # nothing darker: darkest colour overall
            bi = int(np.argmin(self.lab[:, 0]))
            if self.lab[bi, 0] >= L:
                bi = i
        self._dark[key] = bi
        return bi


# ============================================================================ materials
USED = []          # hex colours used by the materials of the current asset (palette registry)
_mats = {}


def reset_materials():
    USED.clear()
    _mats.clear()


def _cached(key):
    m = _mats.get(key)
    if m is None:
        return None
    try:
        m.name
        return m
    except ReferenceError:
        del _mats[key]
        return None


def _use(cols):
    for c in cols:
        c = c.lower()
        if c not in USED:
            USED.append(c)


class NB(C.NB):
    pass


def _factor_lambert(nb, light=None, wrap=0.5):
    geo = nb.node("ShaderNodeNewGeometry")
    d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(light or LIGHT))
    return nb.math("MULTIPLY_ADD", d, wrap, 1 - wrap)


def _coord(nb, kind):
    if kind == "world":
        geo = nb.node("ShaderNodeNewGeometry")
        return geo.outputs["Position"]
    tc = nb.node("ShaderNodeTexCoord")
    return tc.outputs["Object" if kind == "object" else ("UV" if kind == "uv" else "Generated")]


def band_ramp(nb, fac, cols, bounds=None, soft=0.0):
    """Palette ramp: flat bands, optional narrow linear transitions (these dither in post)."""
    n = len(cols)
    lc = [C.lin(c) for c in cols]
    if bounds is None:
        bounds = [i / n for i in range(1, n)]
    hard = all(x <= 0 for x in soft) if isinstance(soft, (list, tuple)) else soft <= 0
    if hard or n == 1:
        stops = [(0.0, lc[0])] + [(b, lc[i + 1]) for i, b in enumerate(bounds)]
        return nb.ramp(fac, stops, "CONSTANT")
    stops = [(0.0, lc[0])]
    for i, b in enumerate(bounds):
        s = soft[i] if isinstance(soft, (list, tuple)) else soft
        stops.append((max(0.0, b - s / 2), lc[i]))
        stops.append((min(1.0, b + s / 2 + 1e-4), lc[i + 1]))
    return nb.ramp(fac, stops, "LINEAR")


def m_tone(cols, bounds=None, soft=0.0, light=None, wrap=0.5, lam=1.0, bias=0.0,
           grads=(), noise=0.0, nscale=5.0, ncoord="object", ndetail=2.0, spec=None, spec_th=0.955,
           pats=(), nvec=None, name="Tone"):
    """General palette-ramp material. f = lam*lambert + sum(grad terms) + noise + bias -> band ramp.
    grads: tuples (axis 0/1/2, a0, a1, weight, coord kind) -> weight * clamp((pos-a0)/(a1-a0))."""
    key = ("tone", tuple(cols), tuple(bounds or ()), soft if not isinstance(soft, list) else tuple(soft),
           tuple(light or LIGHT), wrap, lam, bias, tuple(grads), noise, nscale, ncoord, ndetail, spec, spec_th,
           tuple(pats), nvec)
    if _cached(key) is not None:
        return _mats[key]
    _use(cols)
    m = bpy.data.materials.new(name)
    nb = NB(m)
    f = nb.val(bias)
    if lam:
        f = nb.math("MULTIPLY_ADD", _factor_lambert(nb, light, wrap), lam, f)
    for g in grads:
        ax, a0, a1, w = g[:4]
        kind = g[4] if len(g) > 4 else "world"
        s = nb.sep(_coord(nb, kind))
        t = nb.math("DIVIDE", nb.math("SUBTRACT", s[ax], a0), a1 - a0, clamp=True)
        f = nb.math("MULTIPLY_ADD", t, w, f)
    if noise:
        no = nb.node("ShaderNodeTexNoise")
        co = _coord(nb, ncoord)
        if nvec:
            co = nb.vmath("MULTIPLY", co, tuple(nvec))
        nb.link(co, no.inputs["Vector"])
        no.inputs["Scale"].default_value = nscale
        no.inputs["Detail"].default_value = ndetail
        f = nb.math("MULTIPLY_ADD", nb.math("SUBTRACT", no.outputs["Fac"], 0.5), noise * 2, f)
    for pt in pats:
        # stripes: (axis, period, duty, weight, coord kind, offset)
        ax, per, duty, w = pt[:4]
        kind = pt[4] if len(pt) > 4 else "object"
        off = pt[5] if len(pt) > 5 else 0.0
        s = nb.sep(_coord(nb, kind))
        fr = nb.math("FRACT", nb.math("DIVIDE", nb.math("ADD", s[ax], off), per))
        f = nb.math("MULTIPLY_ADD", nb.math("LESS_THAN", fr, duty), w, f)
    col = band_ramp(nb, f, cols, bounds, soft)
    if spec:
        _use([spec])
        geo = nb.node("ShaderNodeNewGeometry")
        half = (Vector(light or LIGHT) + Vector((0, -1, 0))).normalized()
        s = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(half))
        hi = nb.math("GREATER_THAN", s, spec_th)
        col = nb.mix(hi, col, C.lin(spec))
    nb.output_emission(col, 1.0)
    _mats[key] = m
    return m


def m_flat(hexc):
    key = ("flat", hexc)
    if _cached(key) is None:
        _use([hexc])
        m = bpy.data.materials.new("Flat")
        nb = NB(m)
        nb.output_emission(nb.rgb(C.lin(hexc)), 1.0)
        _mats[key] = m
    return _mats[key]


def m_grad(cols, axis, a0, a1, bounds=None, soft=0.1, coord="world"):
    """Pure gradient along one axis (no lighting) with dithered transitions."""
    return m_tone(cols, bounds, soft, lam=0.0, grads=((axis, a0, a1, 1.0, coord),), name="Grad")


_idmat = [None]


def id_material():
    ok = False
    if _idmat[0] is not None:
        try:
            ok = _idmat[0].name in bpy.data.materials
        except ReferenceError:
            ok = False
    if not ok:
        m = bpy.data.materials.new("R16_ID")
        nb = NB(m)
        oi = nb.node("ShaderNodeObjectInfo")
        cd = nb.node("ShaderNodeCameraData")
        lo = nb.math("MODULO", oi.outputs["Object Index"], 1024.0)
        hi = nb.math("FLOOR", nb.math("DIVIDE", oi.outputs["Object Index"], 1024.0))
        nb.output_emission(nb.combine(lo, cd.outputs["View Z Depth"], hi), 1.0)
        _idmat[0] = m
    return _idmat[0]


# ============================================================================ geometry helpers
def mesh_from(name, verts, faces, mat, smooth=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if mat is not None:
        ob.data.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = smooth
    return ob


def loft(name, pts, rads, mat, segs=12, up=(0, 0, 1), caps=True, smooth=True, twist=0.0):
    """Tube along a polyline with elliptical sections. rads: r or (ra, rb) per point.
    'a' axis is perpendicular to the tangent in the plane containing `up` (for vertical tubes a = X)."""
    n = len(pts)
    P3 = [Vector(p) for p in pts]
    verts, faces = [], []
    upv = Vector(up)
    for i, p in enumerate(P3):
        t = (P3[min(i + 1, n - 1)] - P3[max(i - 1, 0)]).normalized()
        ref = Vector((1, 0, 0)) if abs(t.dot(Vector((1, 0, 0)))) < 0.9 else Vector((0, 1, 0))
        if abs(t.dot(upv)) < 0.9:
            ref = upv
            a = ref - t * ref.dot(t)
            a.normalize()
        else:
            a = ref - t * ref.dot(t)
            a.normalize()
        b = t.cross(a).normalized()
        r = rads[i] if isinstance(rads, (list, tuple)) else rads
        ra, rb = (r, r) if not isinstance(r, (list, tuple)) else r
        for k in range(segs):
            ang = 2 * math.pi * k / segs + twist
            verts.append(p + a * (math.cos(ang) * ra) + b * (math.sin(ang) * rb))
    for i in range(n - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append((i * segs + k, i * segs + k2, (i + 1) * segs + k2, (i + 1) * segs + k))
    if caps:
        c0 = len(verts)
        verts.append(P3[0])
        c1 = len(verts)
        verts.append(P3[-1])
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append((k2, k, c0))
            faces.append(((n - 1) * segs + k, (n - 1) * segs + k2, c1))
    ob = mesh_from(name, verts, faces, mat, smooth)
    _fix_normals(ob)
    return ob


def _fix_normals(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()


def ellipsoid(name, c, r, mat, segs=16, rings=10, rot=None):
    ob = C.add_prim("sphere", name, mat, radius=1.0, location=(0, 0, 0), segments=segs, ring_count=rings)
    M = Matrix.Translation(Vector(c))
    if rot is not None:
        M = M @ rot
    M = M @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    ob.matrix_world = M
    C.set_smooth(ob)
    return ob


def box(name, c, s, mat, rot=None, bevel=0.0, smooth=False):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= s[0]
        v.co.y *= s[1]
        v.co.z *= s[2]
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES")
    ob = C.mesh_object(name, bm, mat)
    M = Matrix.Translation(Vector(c))
    if rot is not None:
        M = M @ rot
    ob.matrix_world = M
    if smooth:
        C.set_smooth(ob)
    return ob


def hpoly(name, pts, z, mat, th=0.02):
    """Horizontal polygon (points in XY) at height z."""
    bm = bmesh.new()
    top = [bm.verts.new((x, y, z + th / 2)) for x, y in pts]
    bot = [bm.verts.new((x, y, z - th / 2)) for x, y in pts]
    bm.faces.new(top)
    bm.faces.new(bot[::-1])
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((top[i], bot[i], bot[j], top[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return C.mesh_object(name, bm, mat)


def vpoly(name, pts, y, mat, th=0.05):
    return C.poly_object(name, pts, mat, thickness=th, y=y)


def ellipse_pts(cx, cy, rx, ry, n=16, rot=0.0):
    out = []
    c, s = math.cos(rot), math.sin(rot)
    for k in range(n):
        a = 2 * math.pi * k / n
        x, y = rx * math.cos(a), ry * math.sin(a)
        out.append((cx + x * c - y * s, cy + x * s + y * c))
    return out


# ============================================================================ render
def _load_exr(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    arr = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(arr)
    bpy.data.images.remove(img)
    return arr.reshape(h, w, 4)[::-1].copy()   # top-down


def render_exr(tag):
    sc = bpy.context.scene
    st = sc.render.image_settings
    st.file_format = "OPEN_EXR"
    st.color_mode = "RGBA"
    st.color_depth = "32"
    path = os.path.join(WORK, f"{tag}.exr")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return _load_exr(path)


def mesh_objects(visible_only=True):
    out = []
    for ob in bpy.context.scene.objects:
        if ob.type == "MESH" and (not visible_only or not ob.hide_render):
            out.append(ob)
    return out


def render_passes(tag, want_ids=True):
    """-> dict(rgb=sRGB HxWx3, a=alpha HxW bool, id=int HxW (0 = empty), depth=HxW)."""
    sc = bpy.context.scene
    sc.render.film_transparent = True
    col = render_exr(tag + "_col")
    out = dict(rgb=l2s(col[..., :3]).astype(np.float32), a=col[..., 3] > 0.5)
    if not want_ids:
        return out
    objs = mesh_objects()
    grp = {}
    saved = []
    idm = id_material()
    for k, ob in enumerate(objs):
        g = ob.get("grp")
        if g is not None:
            if g not in grp:
                grp[g] = len(grp) + 1 + 8000
            ob.pass_index = grp[g]
        else:
            ob.pass_index = k + 1
        mats = [s.material for s in ob.material_slots]
        saved.append((ob, mats))
        if not ob.material_slots:
            ob.data.materials.append(idm)
        for s in ob.material_slots:
            s.material = idm
    ids = render_exr(tag + "_id")
    for ob, mats in saved:
        if not mats:
            ob.data.materials.clear()
        for s, m in zip(ob.material_slots, mats):
            s.material = m
    out["kinds"] = {ob.pass_index: ob.get("kind", "") for ob, _ in saved}
    idv = np.round(ids[..., 0]).astype(np.int64) + 1024 * np.round(ids[..., 2]).astype(np.int64)
    idv[~out["a"]] = 0
    out["id"] = idv
    out["depth"] = np.where(out["a"], ids[..., 1], 1e9).astype(np.float32)
    return out


# ============================================================================ post-process
BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float64) + 0.5) / 16.0


def bayer(h, w, ox=0, oy=0):
    yy = (np.arange(h)[:, None] + oy) % 4
    xx = (np.arange(w)[None, :] + ox) % 4
    return BAYER4[yy, xx]


def quantize(rgb, a, pal, dither=True, dmask=None, ox=0, oy=0, bias=0.0):
    """Palette quantisation with ordered 2-colour dithering along palette segments.
    Returns idx (HxW int, -1 = transparent) and the 'solid' mask (exact palette pixels)."""
    H, W = a.shape
    Cl = s2l(rgb.reshape(-1, 3))
    Pl = pal.lin
    d = (Cl ** 2).sum(1)[:, None] - 2.0 * (Cl @ Pl.T) + (Pl ** 2).sum(1)[None, :]
    i1 = d.argmin(1)
    dmin = d[np.arange(len(i1)), i1]
    solid = dmin < 2e-6
    idx = i1.copy()
    if dither:
        P1 = Pl[i1]
        best = np.full(len(i1), np.inf)
        tb = np.zeros(len(i1))
        i2 = i1.copy()
        D = Cl - P1
        for j in range(len(Pl)):
            v = Pl[j][None, :] - P1
            vv = (v * v).sum(1)
            ok = vv > 1e-10
            t = np.where(ok, (D * v).sum(1) / np.maximum(vv, 1e-10), 0.0).clip(0, 1)
            dist = ((D - t[:, None] * v) ** 2).sum(1) + 2e-3 * vv
            better = ok & (dist < best)
            best = np.where(better, dist, best)
            tb = np.where(better, t, tb)
            i2 = np.where(better, j, i2)
        th = bayer(H, W, ox, oy).reshape(-1)
        flip = (tb + bias > th) & (~solid)
        if dmask is not None:
            flip &= dmask.reshape(-1)
        idx = np.where(flip, i2, i1)
    idx = idx.reshape(H, W)
    idx[~a] = -1
    return idx, solid.reshape(H, W)


def _nb4(arr, fill):
    """Neighbour stack (up, down, left, right) of a 2D array -> HxWx4."""
    H, W = arr.shape
    out = np.full((H, W, 4), fill, dtype=arr.dtype)
    out[1:, :, 0] = arr[:-1, :]    # value of the pixel above
    out[:-1, :, 1] = arr[1:, :]    # below
    out[:, 1:, 2] = arr[:, :-1]    # left
    out[:, :-1, 3] = arr[:, 1:]    # right
    return out


def despeckle(idx, ids=None, protect=None, passes=1, min_same=0):
    """Replace orphan pixels (no 4-neighbour of the same colour) inside one object by the
    majority neighbour colour. protect: bool mask of pixels to keep."""
    for _ in range(passes):
        n = _nb4(idx, -1)
        same = (n == idx[..., None]).sum(-1)
        cand = (idx >= 0) & (same <= min_same) & (n >= 0).all(-1)
        if ids is not None:
            ni = _nb4(ids, -1)
            cand &= (ni == ids[..., None]).all(-1)
        if protect is not None:
            cand &= ~protect
        # majority of the 4 neighbours
        cnt = np.stack([(n == n[..., k:k + 1]).sum(-1) for k in range(4)], -1)
        pick = np.take_along_axis(n, cnt.argmax(-1)[..., None], -1)[..., 0]
        idx = np.where(cand, pick, idx)
    return idx


def inner_lines(idx, pal, ids, depth, thr=0.2, rel=0.0, steps=1, dirs=(0, 1, 2, 3), skip=None):
    """Contour lines on depth discontinuities: the farther pixel of a pair from different objects
    gets `steps` darker. skip: set of palette indices never darkened."""
    nd = _nb4(depth, 1e9)
    ni = _nb4(ids, 0)
    t = np.maximum(thr, rel * depth)
    mark = np.zeros(idx.shape, bool)
    for k in dirs:
        mark |= (ni[..., k] > 0) & (ni[..., k] != ids) & (depth - nd[..., k] > t)
    mark &= idx >= 0
    out = idx.copy()
    for i in np.unique(idx[mark]):
        if skip and i in skip:
            continue
        out[mark & (idx == i)] = pal.darker(int(i), steps)
    return out


def outer_outline(idx, pal, lit_steps=1, dark_steps=2, dark_col=None, lit_col=None, top=True, sides=True,
                  bottom=True, corners=False):
    """Selective 1px outline outside the silhouette. The shadow side (right / below the object)
    gets dark_steps darker (or dark_col), the lit side (left / above) lit_steps darker (or lit_col)."""
    a = idx >= 0
    n = _nb4(idx, -1)
    out = idx.copy()
    H, W = idx.shape
    # neighbour order: 0 above, 1 below, 2 left, 3 right (value of that neighbour)
    # pixel with object BELOW it is on the object's top side (lit); object ABOVE -> bottom (shadow)
    # object to the RIGHT -> we are on its left side (lit); object LEFT -> right side (shadow)
    cand = [(0, "dark", bottom), (2, "dark", sides), (1, "lit", top), (3, "lit", sides)]
    done = np.zeros_like(a)
    for k, kind, on in cand:
        if not on:
            continue
        m = (~a) & (~done) & (n[..., k] >= 0)
        if not m.any():
            continue
        src = n[..., k]
        for i in np.unique(src[m]):
            sel = m & (src == i)
            if kind == "dark":
                out[sel] = dark_col if dark_col is not None else pal.darker(int(i), dark_steps)
            else:
                out[sel] = lit_col if lit_col is not None else pal.darker(int(i), lit_steps)
        done |= m
    if corners:
        diag = np.zeros_like(a)
        diag[1:, 1:] |= a[:-1, :-1]
        diag[:-1, :-1] |= a[1:, 1:]
        diag[1:, :-1] |= a[:-1, 1:]
        diag[:-1, 1:] |= a[1:, :-1]
        m = (~a) & (~done) & diag
        out[m] = dark_col if dark_col is not None else int(np.argmin(pal.lab[:, 0]))
    return out


def remove_specks(idx, min_px=3, keep=None):
    """Drop 8-connected opaque islands smaller than min_px (broken bits of sub-pixel geometry)."""
    H, W = idx.shape
    a = idx >= 0
    seen = np.zeros_like(a)
    out = idx.copy()
    ys, xs = np.nonzero(a)
    for y0, x0 in zip(ys, xs):
        if seen[y0, x0]:
            continue
        comp = [(y0, x0)]
        seen[y0, x0] = True
        k = 0
        while k < len(comp) and len(comp) <= min_px:
            y, x = comp[k]
            k += 1
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < H and 0 <= xx < W and a[yy, xx] and not seen[yy, xx]:
                        seen[yy, xx] = True
                        comp.append((yy, xx))
        if len(comp) < min_px:
            if keep is not None and any(keep[p] for p in comp):
                continue
            for p in comp:
                out[p] = -1
        else:
            # finish marking the component so its pixels are not revisited
            while k < len(comp):
                y, x = comp[k]
                k += 1
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        yy, xx = y + dy, x + dx
                        if 0 <= yy < H and 0 <= xx < W and a[yy, xx] and not seen[yy, xx]:
                            seen[yy, xx] = True
                            comp.append((yy, xx))
    return out


def to_rgba(idx, pal):
    H, W = idx.shape
    out = np.zeros((H, W, 4), np.float32)
    m = idx >= 0
    out[m, :3] = pal.srgb[idx[m]]
    out[m, 3] = 1.0
    return out


def save_png(arr_td, path):
    """arr top-down RGBA float -> PNG (exact 8-bit values)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    C.save_pixels(np.ascontiguousarray(arr_td[::-1]), path)


def load_png(path):
    return C.load_pixels(path)[::-1].copy()


def count_colours(rgba):
    m = rgba[..., 3] > 0.5
    q = np.round(rgba[m][:, :3] * 255).astype(np.int32)
    return len(np.unique(q[:, 0] * 65536 + q[:, 1] * 256 + q[:, 2]))


# ============================================================================ image helpers (preview)
def upscale(arr, k):
    return np.repeat(np.repeat(arr, k, 0), k, 1)


def over(dst, src, x, y, alpha=1.0, tint=None):
    """Alpha-over src (top-down RGBA) onto dst at integer (x, y) top-left."""
    H, W = dst.shape[:2]
    h, w = src.shape[:2]
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return dst
    s = src[y0 - y:y1 - y, x0 - x:x1 - x]
    a = s[..., 3:4] * alpha
    c = s[..., :3]
    if tint is not None:
        c = c * np.array(tint[:3], np.float32)
    d = dst[y0:y1, x0:x1]
    d[..., :3] = d[..., :3] * (1 - a) + c * a
    d[..., 3:4] = np.maximum(d[..., 3:4], a)
    return dst


def affine_nn(src, sx, sy, rot_deg):
    """Nearest-neighbour scale (sx, sy) then rotate (deg, CCW on screen) -> new RGBA, centre-preserving."""
    h, w = src.shape[:2]
    th = math.radians(rot_deg)
    c, s = math.cos(th), math.sin(th)
    corners = []
    for px, py in ((-w / 2, -h / 2), (w / 2, -h / 2), (-w / 2, h / 2), (w / 2, h / 2)):
        X, Y = px * sx, py * sy
        corners.append((X * c + Y * s, -X * s + Y * c))
    W2 = int(math.ceil(max(abs(p[0]) for p in corners) * 2)) + 2
    H2 = int(math.ceil(max(abs(p[1]) for p in corners) * 2)) + 2
    yy, xx = np.mgrid[0:H2, 0:W2].astype(np.float64)
    X = xx + 0.5 - W2 / 2
    Y = yy + 0.5 - H2 / 2
    # inverse rotate (screen y down: CCW on screen)
    Xr = X * c - Y * s
    Yr = X * s + Y * c
    u = Xr / sx + w / 2
    v = Yr / sy + h / 2
    ui = np.floor(u).astype(int)
    vi = np.floor(v).astype(int)
    ok = (ui >= 0) & (ui < w) & (vi >= 0) & (vi < h)
    out = np.zeros((H2, W2, 4), np.float32)
    out[ok] = src[vi[ok], ui[ok]]
    return out


def line(dst, x0, y0, x1, y1, col, width=1):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for k in range(n + 1):
        t = k / max(n, 1)
        x = int(round(x0 + (x1 - x0) * t))
        y = int(round(y0 + (y1 - y0) * t))
        for dx in range(width):
            if 0 <= y < dst.shape[0] and 0 <= x + dx < dst.shape[1]:
                dst[y, x + dx, :3] = col
                dst[y, x + dx, 3] = 1.0


def sheet(images, bg, scale=4, pad=6, cols=None):
    """images: list of top-down RGBA arrays -> one sheet (top-down), nearest upscale, bottoms aligned."""
    cols = cols or len(images)
    rows = (len(images) + cols - 1) // cols
    cw = max(i.shape[1] for i in images) * scale + pad * 2
    ch = max(i.shape[0] for i in images) * scale + pad * 2
    out = np.zeros((rows * ch, cols * cw, 4), np.float32)
    out[..., :3] = bg
    out[..., 3] = 1
    for k, im in enumerate(images):
        big = upscale(im, scale)
        r, c = divmod(k, cols)
        x = c * cw + (cw - big.shape[1]) // 2
        y = r * ch + ch - pad - big.shape[0]
        over(out, big, x, y)
    return out


def flow_sheet(rows, bg, scale=4, pad=12):
    """rows: list of lists of top-down RGBA images; each row packed left to right, bottoms aligned."""
    rw = [sum(im.shape[1] * scale + pad for im in r) + pad for r in rows]
    rh = [max(im.shape[0] for im in r) * scale + pad * 2 for r in rows]
    out = np.zeros((sum(rh), max(rw), 4), np.float32)
    out[..., :3] = bg
    out[..., 3] = 1
    y = 0
    for r, h in zip(rows, rh):
        x = pad + (max(rw) - sum(im.shape[1] * scale + pad for im in r) - pad) // 2
        for im in r:
            big = upscale(im, scale)
            over(out, big, x, y + (h - pad - big.shape[0]))
            x += big.shape[1] + pad
        y += h
    return out