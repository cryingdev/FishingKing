"""
FishingKing - shared Blender helpers for pixel-art sprite rendering.

Pipeline: build low-poly geometry -> toon (normal-based, light-independent) emission
materials -> orthographic side camera -> aliased EEVEE render -> numpy post-process
(alpha cut + 1px selective outline) -> PNG into the Unity project.

World axes: Blender X = Unity X, Blender Z = Unity Y, camera looks along +Y.
"""
import bpy
import bmesh
import math
import os
import json
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SPRITES = os.path.join(ROOT, "Assets", "Resources", "Sprites")
DATA = os.path.join(ROOT, "Assets", "Resources", "Data")
TMP = os.path.join(ROOT, "Tools", "Blender", "_tmp")

# Light direction (towards the light) used by every toon material: upper-left, towards camera.
LIGHT_DIR = Vector((-0.45, -0.62, 0.64)).normalized()
HALF_DIR = (LIGHT_DIR + Vector((0, -1, 0))).normalized()


# ----------------------------------------------------------------------------- colours
def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(h, a=1.0):
    """hex sRGB -> linear RGBA tuple for shader sockets."""
    r, g, b = hex_rgb(h) if isinstance(h, str) else h
    return (srgb_to_lin(r), srgb_to_lin(g), srgb_to_lin(b), a)


# ----------------------------------------------------------------------------- scene
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.film_transparent = True
    sc.render.filter_size = 0.0
    try:
        sc.eevee.taa_render_samples = 1
    except Exception:
        pass
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.image_settings.color_depth = "8"
    sc.render.resolution_percentage = 100
    sc.render.dither_intensity = 0.0
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0
    sc.view_settings.gamma = 1
    world = bpy.data.worlds.new("World")
    sc.world = world
    try:
        world.use_nodes = True
    except Exception:
        pass
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0, 0, 0, 1)
        bg.inputs[1].default_value = 0.0
    os.makedirs(TMP, exist_ok=True)
    return sc


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def mesh_object(name, bm, mat=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    link(ob)
    if mat is not None:
        ob.data.materials.append(mat)
    return ob


def set_smooth(ob, smooth=True):
    for p in ob.data.polygons:
        p.use_smooth = smooth


# ----------------------------------------------------------------------------- node helpers
class NB:
    """Tiny node-graph builder for a material."""

    def __init__(self, mat):
        try:
            mat.use_nodes = True
        except Exception:
            pass
        self.nt = mat.node_tree
        self.nt.nodes.clear()
        self.x = 0

    def node(self, kind, **props):
        n = self.nt.nodes.new(kind)
        n.location = (self.x, 0)
        self.x += 180
        for k, v in props.items():
            setattr(n, k, v)
        return n

    def link(self, a, b):
        self.nt.links.new(a, b)

    def val(self, v):
        n = self.node("ShaderNodeValue")
        n.outputs[0].default_value = v
        return n.outputs[0]

    def rgb(self, col):
        n = self.node("ShaderNodeRGB")
        n.outputs[0].default_value = col if len(col) == 4 else (*col, 1)
        return n.outputs[0]

    def math(self, op, a, b=None, c=None, clamp=False):
        n = self.node("ShaderNodeMath", operation=op)
        n.use_clamp = clamp
        self._in(n.inputs[0], a)
        if b is not None:
            self._in(n.inputs[1], b)
        if c is not None:
            self._in(n.inputs[2], c)
        return n.outputs[0]

    def combine(self, x=0.0, y=0.0, z=0.0):
        n = self.node("ShaderNodeCombineXYZ")
        self._in(n.inputs[0], x)
        self._in(n.inputs[1], y)
        self._in(n.inputs[2], z)
        return n.outputs[0]

    def in_range(self, x, a, b):
        g = self.math("GREATER_THAN", x, a)
        l = self.math("LESS_THAN", x, b)
        return self.math("MULTIPLY", g, l)

    def vmath(self, op, a, b=None):
        n = self.node("ShaderNodeVectorMath", operation=op)
        self._in(n.inputs[0], a)
        if b is not None:
            self._in(n.inputs[1], b)
        out = n.outputs["Value"] if op in ("DOT_PRODUCT", "LENGTH", "DISTANCE") else n.outputs["Vector"]
        return out

    def _in(self, sock, v):
        if isinstance(v, bpy.types.NodeSocket):
            self.link(v, sock)
        else:
            sock.default_value = v

    def mix(self, fac, a, b, blend="MIX"):
        n = self.node("ShaderNodeMix", data_type="RGBA", blend_type=blend)
        fin = [s for s in n.inputs if s.name == "Factor" and s.type == "VALUE"][0]
        cols = [s for s in n.inputs if s.type == "RGBA"]
        self._in(fin, fac)
        self._in(cols[0], a)
        self._in(cols[1], b)
        return [s for s in n.outputs if s.type == "RGBA"][0]

    def ramp(self, fac, stops, interp="CONSTANT"):
        """stops: list of (pos, rgba)."""
        n = self.node("ShaderNodeValToRGB")
        cr = n.color_ramp
        cr.interpolation = interp
        # first two elements exist already
        while len(cr.elements) < len(stops):
            cr.elements.new(0.5)
        for el, (p, c) in zip(cr.elements, stops):
            el.position = p
            el.color = c if len(c) == 4 else (*c, 1)
        self._in(n.inputs[0], fac)
        return n.outputs[0]

    def sep(self, vec):
        n = self.node("ShaderNodeSeparateXYZ")
        self._in(n.inputs[0], vec)
        return n.outputs

    def output_emission(self, color, strength=1.0, alpha=None):
        em = self.node("ShaderNodeEmission")
        self._in(em.inputs["Color"], color)
        em.inputs["Strength"].default_value = strength
        out = self.node("ShaderNodeOutputMaterial")
        if alpha is None:
            self.link(em.outputs[0], out.inputs["Surface"])
        else:
            tr = self.node("ShaderNodeBsdfTransparent")
            mx = self.node("ShaderNodeMixShader")
            self._in(mx.inputs[0], alpha)
            self.link(tr.outputs[0], mx.inputs[1])
            self.link(em.outputs[0], mx.inputs[2])
            self.link(mx.outputs[0], out.inputs["Surface"])


SHADE_STOPS = [
    (0.0, lin("#5a5a86")),   # deep shadow (cool)
    (0.30, lin("#a8a8c4")),  # shadow
    (0.55, lin("#ffffff")),  # lit
]


def toon_shade(nb, shine=0.0, stops=None, flat=False):
    """Returns a colour socket that multiplies the base colour (banded lambert)."""
    if flat:
        return nb.rgb((1, 1, 1, 1))
    geo = nb.node("ShaderNodeNewGeometry")
    d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(LIGHT_DIR))
    half = nb.math("MULTIPLY_ADD", d, 0.5, 0.5)
    shade = nb.ramp(half, stops or SHADE_STOPS)
    if shine > 0:
        s = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(HALF_DIR))
        hi = nb.math("GREATER_THAN", s, 0.955)
        hi = nb.math("MULTIPLY", hi, shine)
        shade = nb.mix(hi, shade, (1.6, 1.6, 1.6, 1), "ADD")
    return shade


def toon_material(name, color, shine=0.0, flat=False, emit=1.0, stops=None):
    """Simple toon material with constant base colour (hex or linear tuple)."""
    m = bpy.data.materials.new(name)
    nb = NB(m)
    base = nb.rgb(lin(color) if isinstance(color, str) else color)
    shade = toon_shade(nb, shine, stops, flat)
    col = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(col, emit)
    return m


def pattern_material(name, color, alt, kind="wood", scale=8.0, thresh=0.5, shine=0.0, emit=1.0,
                     axis=0, flat=False, stops=None):
    """Toon material with a two-colour procedural pattern (wood grain, noise, stripes)."""
    m = bpy.data.materials.new(name)
    nb = NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    co = tc.outputs["Object"]
    if kind == "wood":
        s = nb.sep(co)
        no = nb.node("ShaderNodeTexNoise")
        nb.link(co, no.inputs["Vector"])
        no.inputs["Scale"].default_value = scale * 0.6
        warped = nb.math("MULTIPLY_ADD", no.outputs["Fac"], 0.8, s[2 if axis == 0 else 0])
        fr = nb.math("FRACT", nb.math("MULTIPLY", warped, scale))
        mask = nb.math("LESS_THAN", fr, thresh)
    elif kind == "stripes":
        s = nb.sep(co)
        fr = nb.math("FRACT", nb.math("MULTIPLY", s[axis], scale))
        mask = nb.math("LESS_THAN", fr, thresh)
    else:  # noise blotches
        no = nb.node("ShaderNodeTexNoise")
        nb.link(co, no.inputs["Vector"])
        no.inputs["Scale"].default_value = scale
        no.inputs["Detail"].default_value = 2.0
        mask = nb.math("GREATER_THAN", no.outputs["Fac"], thresh)
    base = nb.mix(mask, lin(color), lin(alt))
    shade = toon_shade(nb, shine, stops, flat)
    col = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(col, emit)
    return m


def glow_material(name, color, strength=1.0):
    m = bpy.data.materials.new(name)
    nb = NB(m)
    nb.output_emission(nb.rgb(lin(color)), strength)
    return m


# ----------------------------------------------------------------------------- geometry helpers
def bm_polygon_xz(points, thickness=0.01, y=0.0):
    """Flat polygon in the XZ plane, extruded along Y (faces the camera)."""
    bm = bmesh.new()
    front = [bm.verts.new((x, y - thickness * 0.5, z)) for x, z in points]
    back = [bm.verts.new((x, y + thickness * 0.5, z)) for x, z in points]
    bm.faces.new(front[::-1])
    bm.faces.new(back)
    n = len(points)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def poly_object(name, points, mat, thickness=0.01, y=0.0):
    return mesh_object(name, bm_polygon_xz(points, thickness, y), mat)


def add_prim(kind, name, mat=None, **kw):
    """Primitive via bmesh (fast, no operator/scene scans); torus falls back to the operator."""
    if kind in ("sphere", "ico", "cone", "cyl", "cube"):
        bm = bmesh.new()
        if kind == "sphere":
            bmesh.ops.create_uvsphere(bm, u_segments=kw.get("segments", 32), v_segments=kw.get("ring_count", 16),
                                      radius=kw.get("radius", 1.0))
        elif kind == "ico":
            bmesh.ops.create_icosphere(bm, subdivisions=kw.get("subdivisions", 2), radius=kw.get("radius", 1.0))
        elif kind == "cone":
            bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=kw.get("vertices", 32),
                                  radius1=kw.get("radius1", 1.0), radius2=kw.get("radius2", 0.0), depth=kw.get("depth", 2.0))
        elif kind == "cyl":
            r = kw.get("radius", 1.0)
            bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=kw.get("vertices", 32),
                                  radius1=r, radius2=r, depth=kw.get("depth", 2.0))
        else:
            bmesh.ops.create_cube(bm, size=kw.get("size", 2.0))
        ob = mesh_object(name, bm, mat)
        from mathutils import Euler, Matrix
        # set matrix_world directly: it is not refreshed from location until the depsgraph updates,
        # and callers compose transforms on top of it
        ob.matrix_world = Matrix.Translation(Vector(kw.get("location", (0, 0, 0)))) @ \
            Euler(kw.get("rotation", (0, 0, 0))).to_matrix().to_4x4()
        return ob
    ops = {
        "cube": bpy.ops.mesh.primitive_cube_add,
        "sphere": bpy.ops.mesh.primitive_uv_sphere_add,
        "ico": bpy.ops.mesh.primitive_ico_sphere_add,
        "cyl": bpy.ops.mesh.primitive_cylinder_add,
        "cone": bpy.ops.mesh.primitive_cone_add,
        "torus": bpy.ops.mesh.primitive_torus_add,
        "plane": bpy.ops.mesh.primitive_plane_add,
    }
    ops[kind](**kw)
    ob = bpy.context.active_object
    ob.name = name
    if mat is not None:
        ob.data.materials.append(mat)
    return ob


def tube_along(name, pts, radii, mat, segs=8):
    """Tube following a polyline of 3D points with per-point radius."""
    bm = bmesh.new()
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        p = Vector(p)
        if i == 0:
            t = Vector(pts[1]) - p
        elif i == n - 1:
            t = p - Vector(pts[i - 1])
        else:
            t = Vector(pts[i + 1]) - Vector(pts[i - 1])
        t.normalize()
        up = Vector((0, 1, 0)) if abs(t.y) < 0.9 else Vector((1, 0, 0))
        a = t.cross(up).normalized()
        b = t.cross(a).normalized()
        r = radii[i] if isinstance(radii, (list, tuple)) else radii
        ring = []
        for k in range(segs):
            ang = 2 * math.pi * k / segs
            ring.append(bm.verts.new(p + (a * math.cos(ang) + b * math.sin(ang)) * r))
        rings.append(ring)
    for i in range(n - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = mesh_object(name, bm, mat)
    set_smooth(ob)
    return ob


def world_bounds(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    xs, ys, zs = [], [], []
    for ob in objs:
        if ob.type != "MESH":
            continue
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        for v in me.vertices:
            w = mw @ v.co
            xs.append(w.x)
            ys.append(w.y)
            zs.append(w.z)
        ev.to_mesh_clear()
    return min(xs), max(xs), min(zs), max(zs)


# ----------------------------------------------------------------------------- camera / render
def ortho_camera(cx, cz, res_x, res_y, ppu):
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cd = bpy.data.cameras.new("Cam")
        cam = bpy.data.objects.new("Cam", cd)
        link(cam)
        sc.camera = cam
    cd = cam.data
    cd.type = "ORTHO"
    cd.clip_start = 0.1
    cd.clip_end = 400
    cd.ortho_scale = max(res_x, res_y) / ppu
    cam.location = (cx, -100, cz)
    cam.rotation_euler = (math.radians(90), 0, 0)
    sc.render.resolution_x = int(res_x)
    sc.render.resolution_y = int(res_y)
    return cam


def fit_camera(objs, ppu, pad_px=2, min_w=0, min_h=0, snap_even=False):
    x0, x1, z0, z1 = world_bounds(objs)
    w = max(min_w, int(math.ceil((x1 - x0) * ppu)) + pad_px * 2)
    h = max(min_h, int(math.ceil((z1 - z0) * ppu)) + pad_px * 2)
    if snap_even:
        w += w % 2
        h += h % 2
    ortho_camera((x0 + x1) / 2, (z0 + z1) / 2, w, h, ppu)
    return w, h


def world_to_pixel(pt):
    """World point -> (px, py) pixel coordinates (origin bottom-left) in the current render."""
    from bpy_extras.object_utils import world_to_camera_view
    sc = bpy.context.scene
    co = world_to_camera_view(sc, sc.camera, Vector(pt))
    return co.x * sc.render.resolution_x, co.y * sc.render.resolution_y


def render_raw(path):
    sc = bpy.context.scene
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def load_pixels(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    arr = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(arr)
    bpy.data.images.remove(img)
    return arr.reshape(h, w, 4)


def save_pixels(arr, path):
    h, w, _ = arr.shape
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img = bpy.data.images.new("out", w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def shift(a, dy, dx):
    out = np.zeros_like(a)
    h, w = a.shape[:2]
    ys = slice(max(dy, 0), h + min(dy, 0))
    yd = slice(max(-dy, 0), h + min(-dy, 0))
    xs = slice(max(dx, 0), w + min(dx, 0))
    xd = slice(max(-dx, 0), w + min(-dx, 0))
    out[ys, xs] = a[yd, xd]
    return out


def pixelize(arr, outline=True, outline_mul=0.28, outline_tint=(0.03, 0.02, 0.08),
             alpha_cut=0.5, inner_line=False, keep_alpha=False):
    """Hard alpha + selective 1px outline (darkened neighbour colour)."""
    a = arr[..., 3] > alpha_cut
    out = arr.copy()
    if not keep_alpha:
        out[..., 3] = np.where(a, 1.0, 0.0)
    out[~a] = 0.0
    if outline:
        acc = np.zeros(arr.shape[:2] + (3,), np.float32)
        cnt = np.zeros(arr.shape[:2], np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            m = shift(a, dy, dx)
            c = shift(out[..., :3], dy, dx)
            acc += c * m[..., None]
            cnt += m
        edge = (cnt > 0) & (~a)
        col = acc / np.maximum(cnt, 1)[..., None] * outline_mul + np.array(outline_tint, np.float32)
        out[edge, :3] = col[edge]
        out[edge, 3] = 1.0
    return out


def render_sprite(path, outline=True, **kw):
    raw = os.path.join(TMP, "raw.png")
    render_raw(raw)
    arr = load_pixels(raw)
    arr = pixelize(arr, outline=outline, **kw)
    save_pixels(arr, path)
    return arr


def clear_objects(keep_camera=True):
    for ob in list(bpy.context.scene.objects):
        if keep_camera and ob.type == "CAMERA":
            continue
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.curves):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


def contact_sheet(paths, out, scale=4, cols=6, bg=(0.25, 0.35, 0.45)):
    """Preview helper: nearest-neighbour upscale and tile sprites onto a flat background."""
    imgs = [load_pixels(p) for p in paths]
    cw = max(i.shape[1] for i in imgs) * scale + 8
    ch = max(i.shape[0] for i in imgs) * scale + 8
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.zeros((rows * ch, cols * cw, 4), np.float32)
    sheet[..., :3] = bg
    sheet[..., 3] = 1
    for k, im in enumerate(imgs):
        big = np.repeat(np.repeat(im, scale, 0), scale, 1)
        r = rows - 1 - k // cols
        c = k % cols
        y0 = r * ch + 4
        x0 = c * cw + 4
        a = big[..., 3:4]
        reg = sheet[y0:y0 + big.shape[0], x0:x0 + big.shape[1]]
        reg[..., :3] = reg[..., :3] * (1 - a) + big[..., :3] * a
    save_pixels(sheet, out)


def write_json(name, data):
    os.makedirs(DATA, exist_ok=True)
    with open(os.path.join(DATA, name), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
