"""
FishingKing - "natural" art variant: shared core.

Technique summary (see notes.md):
  * one physical sun (late morning, behind-left of the camera) + hemispherical sky ambient,
    evaluated by EEVEE (Diffuse BSDF -> Shader to RGB) so cast shadows and AO are real;
  * the resulting light value drives a per-material CONSTANT colour ramp (5 stops) generated in
    OKLab with hue shift (cool/blue shadows, warm/yellow lights) -> every pixel is a ramp colour;
  * optional world-space noise added to the light value breaks band edges into organic clusters
    (foliage, bark, rock, cloth folds); ambient occlusion darkens contacts;
  * atmospheric perspective: quantised mix towards a haze colour by camera distance;
  * everything is rendered at SSx resolution with 1 sample and reduced with a MODE filter
    (majority colour per SSxSS block, coverage threshold for alpha) -> clean pixel clusters,
    no anti-aliasing blends, thin features kept or dropped deterministically;
  * outlines: none for scenery, 1px darker same-hue ("sel-out") outline for sprites.

Import only; never writes outside Tools/Blender/_tmp/variants/natural.
"""
import sys
import os
import math
import random

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
BL = os.path.abspath(os.path.join(HERE, "..", ".."))
for p_ in (BL, HERE):
    if p_ not in sys.path:
        sys.path.insert(0, p_)

import bpy  # noqa: E402
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix, Euler  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

OUT = os.path.join(BL, "_tmp", "variants", "natural")
WORK = os.path.join(OUT, "_work")
os.makedirs(WORK, exist_ok=True)

# ----------------------------------------------------------------------------- light rig
SUN_DIR = Vector((-0.80, -0.15, 0.58)).normalized()   # towards the sun: three-quarter side light from the left, ~35 deg high
SUN_ENERGY = math.pi                                   # N.L = 1 -> light value 1.0
AMB = dict(up=0.30, hor=0.20, down=0.07)               # hemispherical sky ambient


# ============================================================================ colour science
def hex2srgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float64)


def srgb2hex(c):
    c = np.clip(np.asarray(c), 0, 1)
    return "#%02x%02x%02x" % tuple(int(round(v * 255)) for v in c[:3])


def s2l(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(np.asarray(c, np.float64), 0, None)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


_M1 = np.array([[0.4122214708, 0.5363325363, 0.0514459929],
                [0.2119034982, 0.6806995451, 0.1073969566],
                [0.0883024619, 0.2817188376, 0.6299787005]])
_M2 = np.array([[0.2104542553, 0.7936177850, -0.0040720468],
                [1.9779984951, -2.4285922050, 0.4505937099],
                [0.0259040371, 0.7827717662, -0.8086757660]])
_M1i = np.linalg.inv(_M1)
_M2i = np.linalg.inv(_M2)


def lin2lab(rgb):
    lms = np.asarray(rgb, np.float64) @ _M1.T
    return np.cbrt(lms) @ _M2.T


def lab2lin(lab):
    lms = np.asarray(lab, np.float64) @ _M2i.T
    return (lms ** 3) @ _M1i.T


def srgb2lab(c):
    return lin2lab(s2l(c))


def lab2srgb(lab):
    return np.clip(l2s(np.clip(lab2lin(lab), 0, 1)), 0, 1)


def mixhex(a, b, t):
    return srgb2hex(hex2srgb(a) * (1 - t) + hex2srgb(b) * t)


def shade_hex(h, dl=0.0, dc=1.0, cool=0.0):
    """Lightness offset (OKLab L), chroma scale and cool(+)/warm(-) tint on a hex colour."""
    lab = srgb2lab(hex2srgb(h))
    lab = np.array([lab[0] + dl, lab[1] * dc - 0.006 * cool, lab[2] * dc - 0.03 * cool])
    return srgb2hex(lab2srgb(lab))


# Lightness multipliers / tint strengths for n-stop ramps (index 0 = deepest shadow).
RAMP_L = {3: [0.72, 0.9, 1.06], 4: [0.64, 0.8, 0.94, 1.07], 5: [0.6, 0.75, 0.88, 1.0, 1.1],
          6: [0.55, 0.68, 0.8, 0.9, 1.0, 1.1]}
RAMP_COOL = {3: [0.8, 0.25, 0.0], 4: [1.0, 0.55, 0.15, 0.0], 5: [1.0, 0.7, 0.35, 0.05, 0.0],
             6: [1.0, 0.8, 0.5, 0.25, 0.05, 0.0]}
RAMP_WARM = {3: [0, 0, 0.8], 4: [0, 0, 0.2, 0.9], 5: [0, 0, 0, 0.3, 1.0], 6: [0, 0, 0, 0.1, 0.4, 1.0]}
RAMP_POS = {3: [0.0, 0.3, 0.75], 4: [0.0, 0.26, 0.5, 0.9], 5: [0.0, 0.17, 0.36, 0.62, 0.93],
            6: [0.0, 0.12, 0.25, 0.42, 0.64, 0.95]}


def gen_ramp(base, n=5, spread=1.0, cool=1.0, warm=1.4, sat=1.0, minL=None):
    """Hue-shifted pixel-art ramp (list of sRGB hex, dark -> light). base = the 'lit' colour.
    warm 1.4 (default) gives the top step a gentle sunlit warm accent; minL clamps the darkest steps
    (OKLab L) so outlines built from step 0 never go near-black."""
    lab = srgb2lab(hex2srgb(base))
    out = []
    for i in range(n):
        m = 1 + (RAMP_L[n][i] - 1) * spread
        c = RAMP_COOL[n][i] * cool
        w = RAMP_WARM[n][i] * warm
        cm = sat * (0.92 + 0.08 * m if i == n - 1 else 1.0)
        L = lab[0] * m
        if minL is not None:
            L = max(L, minL + 0.035 * i)
        a = lab[1] * cm - 0.004 * c + 0.004 * w
        b = lab[2] * cm - 0.02 * c + 0.022 * w
        out.append(srgb2hex(lab2srgb(np.array([L, a, b]))))
    return out


def hexL(h):
    return float(srgb2lab(hex2srgb(h))[0])


def lin4(h, a=1.0):
    c = s2l(hex2srgb(h))
    return (float(c[0]), float(c[1]), float(c[2]), a)


# ============================================================================ scene / rig
def new_scene():
    sc = C.reset_scene()
    sc.eevee.taa_render_samples = 1
    try:
        sc.eevee.use_shadows = True
        sc.eevee.shadow_resolution_scale = 1.0
    except Exception:
        pass
    _mats.clear()
    return sc


def add_sun(direction=None, energy=SUN_ENERGY, name="NatSun"):
    d = Vector(direction or SUN_DIR).normalized()
    ld = bpy.data.lights.new(name, "SUN")
    ld.energy = energy
    ld.angle = math.radians(0.3)
    try:
        ld.use_shadow = True
    except Exception:
        pass
    ob = bpy.data.objects.new(name, ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return ob


def set_world(up=None, hor=None, down=None, flip=False):
    up = AMB["up"] if up is None else up
    hor = AMB["hor"] if hor is None else hor
    down = AMB["down"] if down is None else down
    w = bpy.context.scene.world
    nt = w.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sp = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sp.inputs[0])
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = 1.0 if flip else -1.0
    mr.inputs["From Max"].default_value = -1.0 if flip else 1.0
    nt.links.new(sp.outputs[2], mr.inputs["Value"])
    rp = nt.nodes.new("ShaderNodeValToRGB")
    cr = rp.color_ramp
    cr.interpolation = "LINEAR"
    cr.elements[0].position = 0.0
    cr.elements[0].color = (down, down, down, 1)
    cr.elements[1].position = 1.0
    cr.elements[1].color = (up, up, up, 1)
    e = cr.elements.new(0.5)
    e.color = (hor, hor, hor, 1)
    nt.links.new(mr.outputs[0], rp.inputs[0])
    bg = nt.nodes.new("ShaderNodeBackground")
    nt.links.new(rp.outputs[0], bg.inputs[0])
    bg.inputs[1].default_value = 1.0
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(bg.outputs[0], out.inputs[0])


# ============================================================================ materials
_mats = {}
HAZE = dict(col="#b7c5cb", dist=800.0, max=0.8, steps=12)


def _light_value(nb, ao=0.6, ao_dist=0.5):
    d = nb.node("ShaderNodeBsdfDiffuse")
    d.inputs["Color"].default_value = (1, 1, 1, 1)
    s2r = nb.node("ShaderNodeShaderToRGB")
    nb.link(d.outputs[0], s2r.inputs[0])
    bw = nb.node("ShaderNodeRGBToBW")
    nb.link(s2r.outputs[0], bw.inputs[0])
    L = bw.outputs[0]
    if ao > 0:
        a = nb.node("ShaderNodeAmbientOcclusion")
        a.inputs["Distance"].default_value = ao_dist
        try:
            a.samples = 16
        except Exception:
            pass
        f = nb.math("MULTIPLY_ADD", a.outputs["AO"], ao, 1.0 - ao)
        L = nb.math("MULTIPLY", L, f)
    return L


def _noise(nb, scale, coords="world", detail=2.0, stretch=(1, 1, 1), rough=0.55):
    if coords == "world":
        geo = nb.node("ShaderNodeNewGeometry")
        v = geo.outputs["Position"]
    else:
        tc = nb.node("ShaderNodeTexCoord")
        v = tc.outputs["Object" if coords == "object" else "UV"]
    if stretch != (1, 1, 1):
        v = nb.vmath("MULTIPLY", v, stretch)
    no = nb.node("ShaderNodeTexNoise")
    try:
        no.noise_dimensions = "3D"
    except Exception:
        pass
    nb.link(v, no.inputs["Vector"])
    no.inputs["Scale"].default_value = scale
    no.inputs["Detail"].default_value = detail
    if "Roughness" in no.inputs:
        no.inputs["Roughness"].default_value = rough
    return no.outputs["Fac"]


def bayer2(nb, w=P.W, h=P.H):
    """2x2 ordered-dither threshold in 0..1 at OUTPUT pixel resolution (window coords)."""
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["Window"])
    bx = nb.math("MODULO", nb.math("FLOOR", nb.math("MULTIPLY", s[0], float(w))), 2.0)
    by = nb.math("MODULO", nb.math("FLOOR", nb.math("MULTIPLY", s[1], float(h))), 2.0)
    b = nb.math("MODULO", nb.math("ADD", nb.math("MULTIPLY", bx, 2.0), nb.math("MULTIPLY", by, 3.0)), 4.0)
    return nb.math("MULTIPLY_ADD", b, 0.25, 0.125)


def _haze(nb, col, amount=1.0):
    cam = nb.node("ShaderNodeCameraData")
    d = cam.outputs["View Distance"]
    e = nb.math("EXPONENT", nb.math("DIVIDE", d, -HAZE["dist"]))
    f = nb.math("MULTIPLY", nb.math("SUBTRACT", 1.0, e), HAZE["max"] * amount)
    f = nb.math("DIVIDE", nb.math("FLOOR", nb.math("MULTIPLY", f, HAZE["steps"])), HAZE["steps"])
    return nb.mix(f, col, lin4(HAZE["col"]))


def ramp_colours(nb, fac, cols, pos=None):
    pos = pos or RAMP_POS[len(cols)]
    return nb.ramp(fac, [(p, lin4(c)) for p, c in zip(pos, cols)])


def lit(base, n=5, tex=0.0, tex_scale=2.0, tex_coords="world", tex_stretch=(1, 1, 1), ao=0.55, ao_dist=0.6,
        haze=1.0, bias=0.0, ramp=None, pos=None, spread=1.0, cool=1.0, warm=1.0, sat=1.0, name=None,
        shadow_only=False, alpha=None):
    """Main 'natural' material: sun+sky light value -> hue-shifted constant ramp (+noise, AO, haze)."""
    key = ("lit", base, n, tex, tex_scale, tex_coords, tex_stretch, ao, ao_dist, haze, bias,
           tuple(ramp) if ramp else None, tuple(pos) if pos else None, spread, cool, warm, sat)
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new(name or ("Lit" + base))
    nb = C.NB(m)
    L = _light_value(nb, ao, ao_dist)
    if tex:
        nz = _noise(nb, tex_scale, tex_coords, stretch=tex_stretch)
        L = nb.math("MULTIPLY_ADD", nz, tex * 2.0, nb.math("SUBTRACT", L, tex))
    if bias:
        L = nb.math("ADD", L, bias)
    cols = ramp or gen_ramp(base, n, spread, cool, warm, sat)
    col = ramp_colours(nb, L, cols, pos)
    if haze:
        col = _haze(nb, col, haze)
    nb.output_emission(col, 1.0, alpha=alpha)
    if alpha is not None:
        m.surface_render_method = "BLENDED"
    m["ramp"] = list(cols)
    _mats[key] = m
    return m


def flat(hexc, haze=0.0, name=None):
    key = ("flat", hexc, haze)
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new(name or ("Flat" + hexc))
    nb = C.NB(m)
    col = nb.rgb(lin4(hexc))
    if haze:
        col = _haze(nb, col, haze)
    nb.output_emission(col, 1.0)
    _mats[key] = m
    return m


# ============================================================================ geometry
class Acc:
    """Accumulates many primitives into one bmesh (one object per material = fast scenes)."""

    def __init__(self):
        self.bm = bmesh.new()

    def ico(self, c, r, sub=1, scale=(1, 1, 1), jit=0.0, rnd=None, rot=0.0):
        ret = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1.0)
        cz, sz = math.cos(rot), math.sin(rot)
        for v in ret["verts"]:
            o = v.co.copy()
            k = 1.0 + (rnd.uniform(-jit, jit) if (jit and rnd) else 0.0)
            x, y, z = o.x * r * scale[0] * k, o.y * r * scale[1] * k, o.z * r * scale[2] * k
            v.co = Vector((c[0] + x * cz - y * sz, c[1] + x * sz + y * cz, c[2] + z))
        return ret["verts"]

    def uvsphere(self, c, r, scale=(1, 1, 1), seg=10, rings=6):
        ret = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=rings, radius=1.0)
        for v in ret["verts"]:
            v.co = Vector((c[0] + v.co.x * r * scale[0], c[1] + v.co.y * r * scale[1], c[2] + v.co.z * r * scale[2]))
        return ret["verts"]

    def cone(self, base, r, h, segs=8, jag=0.0, droop=0.0, rnd=None, cap=True):
        """Cone with optional jagged/drooping rim (conifer tier)."""
        bm = self.bm
        apex = bm.verts.new((base[0], base[1], base[2] + h))
        rim = []
        for k in range(segs):
            a = 2 * math.pi * k / segs + (rnd.uniform(-0.15, 0.15) if rnd else 0)
            rr = r * (1.0 + (rnd.uniform(-jag, jag * 0.5) if rnd else 0) - (jag * 0.6 if k % 2 else 0))
            dz = -(droop * h * (rnd.uniform(0.3, 1.0) if rnd else 1.0)) if k % 2 == 0 else 0.0
            rim.append(bm.verts.new((base[0] + math.cos(a) * rr, base[1] + math.sin(a) * rr, base[2] + dz)))
        for k in range(segs):
            bm.faces.new((rim[k], rim[(k + 1) % segs], apex))
        if cap:
            bm.faces.new(rim[::-1])

    def tube(self, pts, radii, segs=6, sx=1.0):
        bm = self.bm
        n = len(pts)
        rings = []
        for i, p in enumerate(pts):
            p = Vector(p)
            t = (Vector(pts[min(i + 1, n - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
            up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            a = t.cross(up).normalized()
            b = t.cross(a).normalized()
            r = radii[i] if isinstance(radii, (list, tuple)) else radii
            rings.append([bm.verts.new(p + (a * math.cos(2 * math.pi * k / segs) * sx +
                                            b * math.sin(2 * math.pi * k / segs)) * r) for k in range(segs)])
        for i in range(n - 1):
            for k in range(segs):
                k2 = (k + 1) % segs
                bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
        bm.faces.new(rings[0][::-1])
        bm.faces.new(rings[-1])

    def ribbon(self, pts, widths, side=None, twist=0.0):
        """Flat tapered strip through pts (grass/reed blade). side: width direction."""
        bm = self.bm
        n = len(pts)
        L, R = [], []
        for i, p in enumerate(pts):
            p = Vector(p)
            t = (Vector(pts[min(i + 1, n - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
            s = Vector(side) if side is not None else t.cross(Vector((0, 0, 1)))
            if s.length < 1e-4:
                s = Vector((1, 0, 0))
            s = (s - t * s.dot(t)).normalized()
            if twist:
                s = Matrix.Rotation(twist * i / max(n - 1, 1), 3, t) @ s
            w = widths[i] if isinstance(widths, (list, tuple)) else widths
            L.append(bm.verts.new(p - s * w * 0.5))
            R.append(bm.verts.new(p + s * w * 0.5))
        for i in range(n - 1):
            if (L[i + 1].co - R[i + 1].co).length < 1e-6:
                bm.faces.new((L[i], R[i], R[i + 1]))
            else:
                bm.faces.new((L[i], R[i], R[i + 1], L[i + 1]))

    def box(self, c, size, rot=(0, 0, 0)):
        ret = bmesh.ops.create_cube(self.bm, size=1.0)
        mt = Matrix.Translation(Vector(c)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
        for v in ret["verts"]:
            v.co = mt @ v.co
        return ret["verts"]

    def quad_strip_grid(self, xs, ys, zf):
        """Heightfield z = zf(x, y) on a grid."""
        bm = self.bm
        vs = [[bm.verts.new((x, y, zf(x, y))) for x in xs] for y in ys]
        for j in range(len(ys) - 1):
            for i in range(len(xs) - 1):
                bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))

    def poly_h(self, pts, z):
        vs = [self.bm.verts.new((x, y, z)) for x, y in pts]
        self.bm.faces.new(vs)

    def build(self, name, mat, smooth=True, layer="back", shadow=True):
        if len(self.bm.verts) == 0:
            self.bm.free()
            return None
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        ob = C.mesh_object(name, self.bm, mat)
        if smooth:
            C.set_smooth(ob)
        ob["layer"] = layer
        try:
            ob.visible_shadow = shadow
        except Exception:
            pass
        return ob


class Bank:
    """Material-keyed accumulators: bank[mat].ico(...), then bank.build(layer)."""

    def __init__(self):
        self.d = {}

    def __getitem__(self, mat):
        if mat.name not in self.d:
            self.d[mat.name] = (mat, Acc())
        return self.d[mat.name][1]

    def build(self, layer="back", smooth=True, shadow=True, prefix="Obj"):
        obs = []
        for name, (mat, acc) in self.d.items():
            ob = acc.build(prefix + name, mat, smooth, layer, shadow)
            if ob:
                obs.append(ob)
        self.d = {}
        return obs


# ============================================================================ value noise (numpy)
def vnoise2(x, y, seed=0):
    """Smooth value noise for numpy arrays (period-free hash)."""
    xi, yi = np.floor(x), np.floor(y)
    xf, yf = x - xi, y - yi

    def h(i, j):
        v = np.sin(i * 127.1 + j * 311.7 + seed * 74.7) * 43758.5453
        return v - np.floor(v)
    u = xf * xf * (3 - 2 * xf)
    v = yf * yf * (3 - 2 * yf)
    a, b, c, d = h(xi, yi), h(xi + 1, yi), h(xi, yi + 1), h(xi + 1, yi + 1)
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v


def fbm2(x, y, oct=3, seed=0):
    s, amp, tot = 0.0, 1.0, 0.0
    for o in range(oct):
        s = s + vnoise2(x * 2 ** o, y * 2 ** o, seed + o * 13) * amp
        tot += amp
        amp *= 0.5
    return s / tot


# ============================================================================ render + post
def render_to(path):
    sc = bpy.context.scene
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return C.load_pixels(path)


def render_ss(name, ss, w, h):
    """Render the current scene at (w*ss, h*ss); camera must already use sensor_fit so FOV is fixed."""
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = w * ss, h * ss
    arr = render_to(os.path.join(WORK, name))
    sc.render.resolution_x, sc.render.resolution_y = w, h
    return arr


def mode_down(arr, s, cover=0.45, extra=None):
    """Majority-colour downsample of an (H*s, W*s, 4) render. Returns (out, pick) where pick is the
    chosen sample index per output pixel (reuse it to downsample aligned passes with `take`)."""
    Hs, Ws = arr.shape[:2]
    H, W = Hs // s, Ws // s
    a = arr[:H * s, :W * s]
    q = np.round(a[..., :3] * 255).astype(np.int64)
    key = (q[..., 0] << 16) | (q[..., 1] << 8) | q[..., 2]
    op = a[..., 3] > 0.5
    key = np.where(op, key, -1)
    blk = key.reshape(H, s, W, s).transpose(0, 2, 1, 3).reshape(H, W, s * s)
    valid = blk >= 0
    eq = (blk[..., :, None] == blk[..., None, :]) & valid[..., None, :]
    cnt = eq.sum(-1).astype(np.float32)
    # prefer samples near the block centre on ties
    cy = cx = (s - 1) / 2
    idx = np.arange(s * s)
    dist = ((idx // s - cy) ** 2 + (idx % s - cx) ** 2)
    cnt = cnt - dist[None, None, :] * 0.01
    cnt = np.where(valid, cnt, -1e9)
    pick = cnt.argmax(-1)
    ncov = valid.sum(-1) / float(s * s)
    out = take(a, pick, s)
    alpha = (ncov >= cover).astype(np.float32)
    # a block may be 'covered' but its best sample transparent only when valid is all False -> alpha 0
    alpha = np.where(valid.any(-1), alpha, 0.0)
    out[..., 3] = alpha
    out[alpha == 0] = 0.0
    return out, pick


def take(a, pick, s):
    Hs, Ws = a.shape[:2]
    H, W = Hs // s, Ws // s
    blk = a[:H * s, :W * s].reshape(H, s, W, s, -1).transpose(0, 2, 1, 3, 4).reshape(H, W, s * s, -1)
    return np.take_along_axis(blk, pick[..., None, None], axis=2)[:, :, 0, :].copy()


def ckeys(rgb):
    """24-bit integer key per pixel (exact colour identity)."""
    q = np.round(np.clip(rgb, 0, 1) * 255).astype(np.int64)
    return (q[..., 0] << 16) | (q[..., 1] << 8) | q[..., 2]


def unkey(k):
    k = np.asarray(k, np.int64)
    return np.stack([(k >> 16) & 255, (k >> 8) & 255, k & 255], -1).astype(np.float64) / 255.0


def hexkey(h):
    return int(ckeys(hex2srgb(h)))


def count_colours(arr, mask=None):
    m = (arr[..., 3] > 0.5) if mask is None else mask
    return int(len(np.unique(ckeys(arr[..., :3])[m])))


def quantize(arr, k, mask=None, keep=(), chroma_w=1.7, iters=16, pick="freq"):
    """Snap the (masked / opaque) pixels to <= k colours. Weighted k-means in OKLab (chroma weighted up
    so greens and browns do not merge) over the unique colours; every cluster is then represented by
    an EXISTING member colour (its most frequent one), so ramp colours stay ramp colours. `keep` = hex
    colours that are always kept verbatim (they count towards k)."""
    out = arr.copy()
    m = (arr[..., 3] > 0.5) if mask is None else mask
    keys = ckeys(arr[..., :3])
    uk, inv, cnt = np.unique(keys[m], return_inverse=True, return_counts=True)
    if len(uk) <= k:
        return out
    lab = arr_lab(unkey(uk))
    lab[:, 1:] *= chroma_w
    w = cnt.astype(np.float64)
    fixed = [int(np.where(uk == hexkey(h))[0][0]) for h in keep if hexkey(h) in set(uk.tolist())]
    cent = list(fixed) or [int(np.argmax(w))]
    d = np.min(((lab[:, None, :] - lab[cent][None]) ** 2).sum(-1), 1)
    while len(cent) < k:
        j = int(np.argmax(d * w ** 0.4))
        cent.append(j)
        d = np.minimum(d, ((lab - lab[j]) ** 2).sum(-1))
    Cc = lab[cent].copy()
    nf = len(fixed)
    for _ in range(iters):
        a = ((lab[:, None, :] - Cc[None]) ** 2).sum(-1).argmin(1)
        for c in range(nf, k):
            s = a == c
            if s.any():
                Cc[c] = (lab[s] * w[s, None]).sum(0) / w[s].sum()
    a = ((lab[:, None, :] - Cc[None]) ** 2).sum(-1).argmin(1)
    rep = np.array(uk)
    for c in range(k):
        s = np.where(a == c)[0]
        if not len(s):
            continue
        if c < nf:
            rep[s] = uk[cent[c]]
        elif pick == "freq":
            rep[s] = uk[s[np.argmax(w[s])]]
        else:
            rep[s] = uk[s[np.argmin(((lab[s] - Cc[c]) ** 2).sum(-1))]]
    rgb = out[..., :3]
    rgb[m] = unkey(rep[inv]).astype(rgb.dtype)
    out[..., :3] = rgb
    return out


def despeckle(arr, mask=None):
    """Any pixel whose 4 neighbours all share one other colour becomes that colour (removes 1px noise)."""
    out = arr.copy()
    keys = ckeys(arr[..., :3])
    op = arr[..., 3] > 0.5
    nb = [C.shift(keys, dy, dx) for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1))]
    nop = [C.shift(op, dy, dx) for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1))]
    same = (nb[0] == nb[1]) & (nb[1] == nb[2]) & (nb[2] == nb[3]) & (nb[0] != keys) & op
    for o in nop:
        same &= o
    if mask is not None:
        same &= mask
    same[0, :] = same[-1, :] = False
    same[:, 0] = same[:, -1] = False
    out[same, :3] = unkey(nb[0][same])
    return out


def nearest_in(rgb, cols):
    """Index of the nearest colour (OKLab) in the hex list `cols` for every pixel of rgb (...,3)."""
    lab = arr_lab(rgb)
    pl = np.array([srgb2lab(hex2srgb(c)) for c in cols])
    d = ((lab[..., None, :] - pl) ** 2).sum(-1)
    return d.argmin(-1)


def arr_lab(rgb):
    return lin2lab(s2l(rgb))


def lab_arr(lab):
    return lab2srgb(lab)


def darker(rgb, dl=0.12, mulL=None, dc=1.05, cool=0.6):
    """Darker same-hue colour(s) for outlines (rgb array (...,3) in sRGB)."""
    lab = arr_lab(rgb)
    if mulL is not None:
        lab[..., 0] *= mulL
    else:
        lab[..., 0] -= dl
    lab[..., 1] = lab[..., 1] * dc - 0.006 * cool
    lab[..., 2] = lab[..., 2] * dc - 0.03 * cool
    return lab_arr(lab)


def outline_sel(arr, mulL=0.55, cool=0.8, diag=False):
    """1px outside outline coloured as a darker same-hue version of the adjacent sprite colour."""
    a = arr[..., 3] > 0.5
    out = arr.copy()
    acc = np.zeros(arr.shape[:2] + (3,), np.float64)
    cnt = np.zeros(arr.shape[:2], np.float64)
    offs = [(1, 0), (-1, 0), (0, 1), (0, -1)]
    if diag:
        offs += [(1, 1), (1, -1), (-1, 1), (-1, -1)]
    for dy, dx in offs:
        m = C.shift(a, dy, dx)
        c = C.shift(arr[..., :3], dy, dx)
        acc += c * m[..., None]
        cnt += m
    edge = (cnt > 0) & (~a)
    col = acc / np.maximum(cnt, 1)[..., None]
    dk = darker(col, mulL=mulL, cool=cool)
    out[edge, :3] = dk[edge]
    out[edge, 3] = 1.0
    return out


def upscale(arr, k):
    return np.repeat(np.repeat(arr, k, 0), k, 1)


def paste(dst, src, x0, y0, alpha_mul=1.0):
    """Alpha-composite src onto dst at (x0, y0) with y0 = row from the BOTTOM (arrays are bottom-up)."""
    h, w = src.shape[:2]
    H, W = dst.shape[:2]
    xs0, ys0 = max(0, -x0), max(0, -y0)
    xd0, yd0 = max(0, x0), max(0, y0)
    xs1 = min(w, W - x0)
    ys1 = min(h, H - y0)
    if xs1 <= xs0 or ys1 <= ys0:
        return
    s = src[ys0:ys1, xs0:xs1]
    d = dst[yd0:yd0 + (ys1 - ys0), xd0:xd0 + (xs1 - xs0)]
    al = s[..., 3:4] * alpha_mul
    d[..., :3] = d[..., :3] * (1 - al) + s[..., :3] * al
    d[..., 3:4] = np.maximum(d[..., 3:4], al)


def sheet(images, out, scale=4, bg="#6e6e6e", pad=6, cols=None, labels=None):
    """Nearest upscale + tile images (bottom-aligned) on a flat background."""
    cols = cols or len(images)
    rows = (len(images) + cols - 1) // cols
    cw = max(i.shape[1] for i in images) * scale + pad * 2
    ch = max(i.shape[0] for i in images) * scale + pad * 2
    S = np.zeros((rows * ch, cols * cw, 4), np.float32)
    S[..., :3] = hex2srgb(bg)
    S[..., 3] = 1
    for k, im in enumerate(images):
        big = upscale(im, scale)
        r = rows - 1 - k // cols
        c = k % cols
        x0 = c * cw + (cw - big.shape[1]) // 2
        y0 = r * ch + pad
        paste(S, big, x0, y0)
    C.save_pixels(S, out)
    return S


def sheet_rows(rows, out, scale=4, bg="#6e6e6e", pad=8, gap=10):
    """Packed contact sheet: rows = list of lists of images; every image keeps its own width."""
    rw = [sum(i.shape[1] * scale for i in r) + gap * (len(r) - 1) + pad * 2 for r in rows]
    rh = [max(i.shape[0] for i in r) * scale + pad * 2 for r in rows]
    S = np.zeros((sum(rh), max(rw), 4), np.float32)
    S[..., :3] = hex2srgb(bg)
    S[..., 3] = 1
    y = sum(rh)
    for r, h in zip(rows, rh):
        y -= h
        x = pad
        for im in r:
            big = upscale(im, scale)
            paste(S, big, x, y + pad + (h - pad * 2 - big.shape[0]) // 2)
            x += big.shape[1] + gap
    C.save_pixels(S, out)
    return S
