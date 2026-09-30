"""
FishingKing - shared scene-building helpers for the Blender pipeline: cached toon/flat/gradient
materials, primitive helpers (tubes, rocks, trees, reeds, clouds...), front/back layer tagging
and the drifting cloud sprites. Used by fk_stages.py and fk_misc.py.
"""
import sys
import os
import math
import random
import contextlib
import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402

OUT = os.path.join(C.SPRITES, "Stages")
W, H, PPU = 640, 400, 16
X0, X1 = -20.5, 20.5
ZB = -13.0  # below the canvas
SURF = 1.0

_front = [False]
_mats = {}


# ----------------------------------------------------------------------------- material helpers
def mflat(hexc, emit=1.0):
    key = ("f", hexc, emit)
    if key not in _mats:
        _mats[key] = C.toon_material("F" + hexc, hexc, flat=True, emit=emit)
    return _mats[key]


def mtoon(hexc, shine=0.0):
    key = ("t", hexc, shine)
    if key not in _mats:
        _mats[key] = C.toon_material("T" + hexc, hexc, shine=shine)
    return _mats[key]


def mglow(hexc, s=1.3):
    key = ("g", hexc, s)
    if key not in _mats:
        _mats[key] = C.glow_material("G" + hexc, hexc, s)
    return _mats[key]


def lerp_col(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(4))


def mgrad(cols, z0, z1, steps=8, alpha=None):
    """Banded vertical gradient in world Z: cols[0] at z0 ... cols[-1] at z1."""
    key = ("gr", tuple(cols), z0, z1, steps, alpha)
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new("Grad")
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    s = nb.sep(geo.outputs["Position"])
    f = nb.math("DIVIDE", nb.math("SUBTRACT", s[2], z0), z1 - z0, clamp=True)
    lc = [C.lin(c) for c in cols]
    stops = []
    for i in range(steps):
        t = i / (steps - 1)
        seg = min(int(t * (len(lc) - 1)), len(lc) - 2)
        lt = t * (len(lc) - 1) - seg
        stops.append((i / steps, lerp_col(lc[seg], lc[seg + 1], lt)))
    col = nb.ramp(f, stops)
    if alpha is not None:
        m.surface_render_method = "BLENDED"
        nb.output_emission(col, 1.0, alpha=alpha)
    else:
        nb.output_emission(col, 1.0)
    _mats[key] = m
    return m


def mnoise(a, b, scale=3.0, thresh=0.5, flat=True):
    key = ("n", a, b, scale, thresh, flat)
    if key not in _mats:
        _mats[key] = C.pattern_material("N", a, b, kind="noise", scale=scale, thresh=thresh, flat=flat)
    return _mats[key]


def mwood(a="#8a5a30", b="#6a4220", flat=False):
    key = ("w", a, b, flat)
    if key not in _mats:
        _mats[key] = C.pattern_material("Wood", a, b, kind="stripes", scale=5.0, thresh=0.25, axis=2, flat=flat)
    return _mats[key]


# ----------------------------------------------------------------------------- object helpers
@contextlib.contextmanager
def front():
    _front[0] = True
    try:
        yield
    finally:
        _front[0] = False


def tag(ob):
    ob["layer"] = "front" if _front[0] else "back"
    return ob


def poly(pts, mat, y, th=0.05):
    return tag(C.poly_object("Poly", pts, mat, thickness=th, y=y))


def quad(x0, z0, x1, z1, mat, y):
    return poly([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], mat, y)


def tube(pts, r, mat, segs=8):
    return tag(C.tube_along("Tube", pts, r, mat, segs))


def sphere(x, y, z, r, mat, sx=1.0, sy=1.0, sz=1.0, seg=12, rings=8):
    ob = C.add_prim("sphere", "Sph", mat, radius=r, location=(x, y, z), segments=seg, ring_count=rings)
    ob.scale = (sx, sy, sz)
    C.set_smooth(ob)
    return tag(ob)


def rock(x, y, z, r, mat, rnd, sx=1.0, sz=0.7, amp=0.18):
    ob = C.add_prim("ico", "Rock", mat, radius=r, location=(x, y, z), subdivisions=2)
    for v in ob.data.vertices:
        v.co *= 1 + rnd.uniform(-amp, amp)
    ob.scale = (sx, 0.8, sz)
    return tag(ob)


def cone(x, y, z, r, h, mat, verts=8, rot=None):
    ob = C.add_prim("cone", "Cone", mat, vertices=verts, radius1=r, radius2=0.0, depth=h,
                    location=(x, y, z + h / 2))
    if rot:
        ob.rotation_euler = rot
    return tag(ob)


def boxo(x, y, z, sx, sy, sz, mat, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= sx
        v.co.y *= sy
        v.co.z *= sz
    ob = C.mesh_object("Box", bm, mat)
    ob.location = (x, y, z)
    if bevel:
        md = ob.modifiers.new("b", "BEVEL")
        md.width = bevel
        md.segments = 2
        md.limit_method = "NONE"
    return tag(ob)


def ridge_pts(rnd, x0, x1, step, base, lo, hi, jag=0.35, smooth=False):
    pts = []
    x = x0
    ph = [rnd.uniform(0, 6.28) for _ in range(3)]
    while x <= x1 + step:
        if smooth:
            z = base + (hi - lo) * (0.5 + 0.25 * math.sin(x * 0.23 + ph[0]) + 0.25 * math.sin(x * 0.51 + ph[1])) + lo
        else:
            z = rnd.uniform(lo, hi) + base
        pts.append((x, z))
        x += step * rnd.uniform(1 - jag, 1 + jag)
    return pts


def fill_below(pts, zb=ZB):
    return pts + [(pts[-1][0], zb), (pts[0][0], zb)]


def mountains(rnd, y, base, lo, hi, col, snow=None, step=2.6, snowline=None):
    pts = ridge_pts(rnd, X0 - 1, X1 + 1, step, base, lo, hi, 0.4)
    poly(fill_below(pts), mflat(col), y)
    if snow:
        sl = snowline if snowline is not None else base + lo + (hi - lo) * 0.62
        bot = [(x, min(z, sl + rnd.uniform(-0.3, 0.1))) for (x, z) in pts]
        poly(pts + list(reversed(bot)), mflat(snow), y - 0.2)
    return pts


def hills(rnd, y, base, amp, col, freq=0.25):
    pts = []
    ph = rnd.uniform(0, 6.28)
    ph2 = rnd.uniform(0, 6.28)
    x = X0 - 1
    while x <= X1 + 1:
        z = base + amp * (0.55 + 0.3 * math.sin(x * freq + ph) + 0.15 * math.sin(x * freq * 2.7 + ph2))
        pts.append((x, z))
        x += 0.5
    poly(fill_below(pts), mflat(col), y)
    return pts


def pine(x, y, z, h, col, trunk="#4a3020", snow=None, toon=True):
    m = mtoon(col) if toon else mflat(col)
    tube([(x, y, z), (x, y, z + h * 0.3)], h * 0.05, mtoon(trunk) if toon else mflat(trunk), 6)
    for k in range(3):
        zz = z + h * (0.18 + 0.24 * k)
        r = h * (0.3 - 0.07 * k)
        cone(x, y, zz, r, h * 0.42, m, verts=8)
        if snow:
            cone(x, y - 0.05, zz + h * 0.25, r * 0.45, h * 0.18, mflat(snow), verts=8)


def round_tree(x, y, z, h, col, col2=None, trunk="#5a3a22", toon=True):
    tube([(x, y, z), (x, y, z + h * 0.5)], h * 0.07, mtoon(trunk) if toon else mflat(trunk), 6)
    m = mtoon(col) if toon else mflat(col)
    m2 = mtoon(col2 or col) if toon else mflat(col2 or col)
    sphere(x, y, z + h * 0.62, h * 0.34, m)
    sphere(x - h * 0.22, y + 0.1, z + h * 0.5, h * 0.26, m2)
    sphere(x + h * 0.22, y + 0.1, z + h * 0.5, h * 0.26, m2)
    sphere(x + h * 0.05, y - 0.05, z + h * 0.82, h * 0.22, m)


def cloud(x, y, z, s, col="#ffffff", shade="#c8d8ee"):
    m = C.toon_material("Cloud", col, stops=[(0.0, C.lin(shade)), (0.5, C.lin("#ffffff"))])
    sphere(x, y, z, 0.9 * s, m, 1.3, 0.6, 0.8)
    sphere(x - 1.1 * s, y + 0.1, z - 0.25 * s, 0.65 * s, m, 1.3, 0.6, 0.8)
    sphere(x + 1.2 * s, y + 0.1, z - 0.2 * s, 0.7 * s, m, 1.3, 0.6, 0.8)
    sphere(x + 0.4 * s, y - 0.1, z + 0.35 * s, 0.6 * s, m, 1.2, 0.6, 0.8)


def weed(rnd, x, y, z0, h, col, r=0.07):
    pts = []
    ph = rnd.uniform(0, 6.28)
    for k in range(7):
        t = k / 6
        pts.append((x + 0.25 * math.sin(t * 5 + ph) * t, y, z0 + h * t))
    tube(pts, [r * (1 - 0.7 * k / 6) for k in range(7)], mtoon(col), 6)


def reed(x, y, z0, h, col="#5a9a3a", head="#6a4424"):
    tube([(x, y, z0), (x + 0.05, y, z0 + h)], 0.045, mtoon(col), 5)
    tube([(x + 0.04, y - 0.02, z0 + h * 0.62), (x + 0.05, y - 0.02, z0 + h * 0.85)], 0.09, mtoon(head), 8)


def grass_tufts(rnd, x0, x1, y, zf, col, n=20, h=0.35):
    m = mtoon(col)
    for _ in range(n):
        x = rnd.uniform(x0, x1)
        z = zf(x)
        for k in range(3):
            a = math.radians(rnd.uniform(-25, 25))
            tube([(x, y, z - 0.05), (x + math.sin(a) * h, y, z + math.cos(a) * h * rnd.uniform(0.6, 1.2))], 0.035, m, 4)


def profile_fn(pts):
    def f(x):
        if x <= pts[0][0]:
            return pts[0][1]
        for (xa, za), (xb, zb) in zip(pts, pts[1:]):
            if xa <= x <= xb:
                return za + (zb - za) * (x - xa) / (xb - xa)
        return pts[-1][1]
    return f


def bottom_terrain(rnd, prof, y, col_a, col_b, edge, scale=2.0):
    pts = [(x, z) for x, z in prof]
    poly(fill_below(pts), mnoise(col_a, col_b, scale, 0.52), y)
    f = profile_fn(prof)
    edge_pts = [(x, y - 0.2, f(x) - 0.05) for x in [X0 + k * 0.5 for k in range(int((X1 - X0) / 0.5) + 1)]]
    tube(edge_pts, 0.1, mflat(edge), 6)
    return f


def water_backdrop(top, deep, y=15.0, zbottom=ZB, steps=10):
    quad(X0 - 1, zbottom, X1 + 1, SURF, mgrad([deep, top], -9.0, SURF, steps), y)


def sky(top, horizon, zlow=SURF, y=60.0, steps=10):
    quad(X0 - 1, zlow - 3, X1 + 1, 13.5, mgrad([horizon, top], zlow, 12.0, steps), y)


def sun(x, z, r, col, halo):
    sphere(x, 55, z, r * 1.6, mflat(halo))
    sphere(x, 54, z, r, mflat(col))


def light_rays(rnd, xs, col, y=13.0, alpha=0.18, top=SURF, bottom=-8.5):
    m = mgrad([col, col], bottom, top, 2, alpha=alpha)
    k = (top - bottom) / 9.5
    for x in xs:
        w = rnd.uniform(0.6, 1.3)
        poly([(x, top), (x + w, top), (x + w - 3.0 * k, bottom), (x - 2.2 * k, bottom)], m, y)


def sample_profile(f):
    xs = [X0 + 0.5 * k for k in range(int((X1 - X0) / 0.5) + 1)]
    return [round(x, 2) for x in xs], [round(f(x), 3) for x in xs]



# ============================================================================ cloud sprites
def render_clouds():
    for k, (s, n) in enumerate(((1.0, 4), (1.4, 5), (0.8, 3))):
        C.clear_objects()
        rnd = random.Random(k + 5)
        m = C.toon_material("Cloud", "#ffffff", stops=[(0.0, C.lin("#c8d8ee")), (0.5, C.lin("#ffffff"))])
        for i in range(n):
            sphere(i * 0.9 * s + rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), rnd.uniform(0, 0.45) * s * (1 if 0 < i < n - 1 else 0.3),
                   rnd.uniform(0.6, 0.9) * s, m, 1.3, 0.6, 0.8)
        bpy.context.view_layer.update()
        objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        C.fit_camera(objs, PPU, pad_px=2)
        C.render_sprite(os.path.join(OUT, f"cloud_{k + 1}.png"), outline=False)
