"""
hybrid - geometry KIT for the real-time 3D legend models (shared by every legends/<fish_id>.py module and by
hyb_legend3d.py). Stable API: the legend modules only ADD code in their own file; if a module needs a new helper it
keeps it local (this file is shared by parallel work - do not edit it for one species).

Everything here works in UNITY MODEL SPACE (metres, the 1.0 m model): x = the fish's right, y = up, z = towards the
nose (nose tip z +0.50, tail tip z -0.50). hyb_legend3d converts to Blender / FBX space when it exports.

  Part(name)                          one rigid part geo_<name>: a bmesh + its material list
  Part.face(verts, mat, smooth) / Part.closed(faces)
  loft(part, rings, fmat, smooth=True, cap0=None, cap1=None)   closed tube through rings (+ fan caps)
  plate(part, pts, normal, th, mat)   flat fin: a planar polygon extruded +-th/2
  ellipsoid(part, c, radii, axes, mat, segs=8, rings=5, smooth=True)
  frame_from(n, hint)                 orthonormal (a, b, n)
  decal(part, bvh, center, skin_n, radii, mat, lift, spin, flat)   flat spot conformed to the skin
  strip_decal(part, bvh, pts, skin_ns, width, mat, lift)           thin band along a polyline on the skin
  Body(table, ...)                    the lofted body from a profile table (z, top y, bottom y, half-width x):
      .prof / .sec / .pt / .normal / .mouth_y / .zone_mat / .stations / .body_rings / .full_ring / .full_mat /
      .clip_ring (skull / jaw shells split on the mouth line)
  u_of_w(w) / w_of_u(u)               Blender world <-> Unity model space;  r3(v) rounds for reports
"""
import math
import bmesh
from mathutils import Vector, Matrix

V = Vector
S = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))           # Unity <-> FBX file (the importer negates x)
F2W = Matrix.Rotation(math.radians(90), 4, "X")      # FBX / armature space (y up) -> Blender world (z up)
W2F = F2W.inverted()
U2W = F2W @ S                                        # Unity model space -> Blender world: (x, y, z) -> (-x, -z, y)
C_UW = U2W.to_3x3()


def r3(v):
    return [round(float(x), 4) for x in v]


def u_of_w(w):
    """Blender world point -> Unity model space."""
    return V((-w[0], w[2], -w[1]))


def w_of_u(u):
    return V((-u[0], -u[2], u[1]))


# ============================================================================ mesh building (Unity model space U)
class Part:
    """One rigid part (geo_<name>): a bmesh in Unity model space with a material list and the "rest" UV."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def face(self, vs, mat, smooth):
        f = self.bm.faces.new(vs)
        f.material_index = self.mi(mat)
        f.smooth = smooth
        return f

    def closed(self, faces):
        """Outward normals for a closed piece."""
        bmesh.ops.recalc_face_normals(self.bm, faces=list(faces))


def loft(part, rings, fmat, smooth=True, cap0=None, cap1=None):
    """Closed tube through rings (closed loops of the same length). fmat(i, k) -> material of the quad between ring
    i / i+1 and vertex k / k+1. Caps: a fan to the ring centroid (flat, sharing the ring vertices); cap0 / cap1 are
    their materials (None = the material of the first / last ring's quad 0)."""
    vs = [[part.bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    if cap0 is None:
        cap0 = fmat(0, 0)
    if cap1 is None:
        cap1 = fmat(len(rings) - 2, 0)
    faces = []
    zc = [sum(p[2] for p in r) / n for r in rings]
    for i in range(len(rings) - 1):
        step = abs(zc[i + 1] - zc[i]) < 0.002          # nominal <-> inset step ring: a flat annulus
        for k in range(n):
            k2 = (k + 1) % n
            faces.append(part.face((vs[i][k], vs[i][k2], vs[i + 1][k2], vs[i + 1][k]), fmat(i, k),
                                   smooth and not step))
    for ring, mat in ((vs[0], cap0), (vs[-1], cap1)):
        c = sum((v.co for v in ring), V()) / n
        cv = part.bm.verts.new(c)
        for k in range(n):
            faces.append(part.face((ring[k], ring[(k + 1) % n], cv), mat, False))
    # flat faces (caps, steps) must not bend the smooth side normals: their edges are sharp
    for f in faces:
        if not f.smooth:
            for e in f.edges:
                e.smooth = False
    part.closed(faces)
    return faces


def plate(part, pts, normal, th, mat):
    """Flat fin: the planar polygon pts (3D, in order) extruded +-th/2 along normal, caps ear-clipped, flat shaded."""
    n = V(normal).normalized()
    fr = [part.bm.verts.new(V(p) + n * (th / 2)) for p in pts]
    bk = [part.bm.verts.new(V(p) - n * (th / 2)) for p in pts]
    faces = [part.face(fr, mat, False), part.face(bk[::-1], mat, False)]
    m = len(pts)
    for i in range(m):
        j = (i + 1) % m
        faces.append(part.face((fr[i], fr[j], bk[j], bk[i]), mat, False))
    res = bmesh.ops.triangulate(part.bm, faces=faces[:2], quad_method="BEAUTY", ngon_method="EAR_CLIP")
    part.closed(res["faces"] + faces[2:])
    return res["faces"] + faces[2:]


def ellipsoid(part, c, radii, axes, mat, segs=8, rings=5, smooth=True):
    """Ellipsoid centred at c with semi-axes radii along the orthonormal axes (a, b, n)."""
    res = bmesh.ops.create_uvsphere(part.bm, u_segments=segs, v_segments=rings, radius=1.0)
    verts = res["verts"]
    a, b, nn = (V(x).normalized() for x in axes)
    M = Matrix((a * radii[0], b * radii[1], nn * radii[2])).transposed().to_4x4()
    M.translation = V(c)
    bmesh.ops.transform(part.bm, matrix=M, verts=verts)
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = part.mi(mat)
        f.smooth = smooth
    part.closed(faces)
    return faces


def frame_from(n, hint=(0, 1, 0)):
    """Orthonormal (a, b, n): n given, a ~ hint projected, b = n x a."""
    n = V(n).normalized()
    a = V(hint) - n * V(hint).dot(n)
    if a.length < 1e-6:
        a = V((1, 0, 0)) - n * n.x
    a.normalize()
    return a, n.cross(a).normalized(), n


def decal(part, bvh, center, skin_n, radii, mat, lift=0.001, spin=0.0, flat=True):
    """Flat irregular spot conformed to the part's surface (BVH), `lift` m off the skin, facing outward."""
    a, b, n = frame_from(skin_n, (0, 0, 1))
    pts = []
    for k, r in enumerate(radii):
        ang = spin + 2 * math.pi * k / len(radii)
        pts.append(V(center) + (a * math.cos(ang) + b * math.sin(ang)) * r)
    out = []
    for p in [V(center)] + pts:
        loc, nrm, _, _ = bvh.find_nearest(p)
        out.append(part.bm.verts.new(loc + nrm * lift if loc is not None else p + n * lift))
    c, ring = out[0], out[1:]
    faces = []
    for k in range(len(ring)):
        vs = (c, ring[k], ring[(k + 1) % len(ring)])
        e1, e2 = vs[1].co - vs[0].co, vs[2].co - vs[0].co
        if e1.cross(e2).dot(n) < 0:
            vs = (c, ring[(k + 1) % len(ring)], ring[k])
        faces.append(part.face(vs, mat, not flat))
    return faces


def strip_decal(part, bvh, pts, skin_ns, width, mat, lift=0.001):
    """A thin band along a polyline on the skin (e.g. the gill-cover edge)."""
    rows = []
    for i, (p, sn) in enumerate(zip(pts, skin_ns)):
        t = (V(pts[min(i + 1, len(pts) - 1)]) - V(pts[max(i - 1, 0)])).normalized()
        side = V(sn).cross(t).normalized()
        row = []
        for s_ in (-0.5, 0.5):
            q = V(p) + side * (width * s_)
            loc, nrm, _, _ = bvh.find_nearest(q)
            row.append(part.bm.verts.new(loc + nrm * lift))
        rows.append(row)
    faces = []
    for i in range(len(rows) - 1):
        vs = (rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0])
        e1, e2 = vs[1].co - vs[0].co, vs[3].co - vs[0].co
        if e1.cross(e2).dot(V(skin_ns[i])) < 0:
            vs = vs[::-1]
        faces.append(part.face(vs, mat, False))
    return faces


# ============================================================================ lofted body from a profile table
class Body:
    """The fish body as an elliptic loft through a profile table [(z, top y, bottom y, half-width x), ...] ordered
    from the nose (high z) to the tail (low z). A section at z is an ellipse centred between top and bottom, its
    upper half pinched by back_pinch (the back a little narrower than the belly).

    Segments for the rigid parts: body_rings(z_lo, z_hi, ext_lo, ext_hi, ring_fn) runs `over` m past a joint as an
    extension inset by `inset` (bends up to ~15 deg per joint never open a gap: give the FRONT segment the rear
    extension, end the rear segment in a cap on the joint). Faces take a zone material by height on the section:
    back (v >= zones[0]), belly (v < zones[1]), side otherwise, v = 0.5 + 0.5 sin(theta).
    Ring vertex 0 sits on the right flank at mid height, counter-clockwise seen from +z.

    mouth = ((z, y) of the gape corner / Jaw hinge, (z, y) at the front): the mouth line used by clip_ring() to split
    the head into an upper (skull) and a lower (jaw) shell and by mouth_y()."""

    def __init__(self, table, mats, nseg=16, back_pinch=0.12, inset=0.0015, over=0.015, mouth=None,
                 zones=(0.75, 0.2), n_arc=9, n_chord=2):
        self.table = table
        self.mat_back, self.mat_side, self.mat_belly = mats
        self.nseg, self.back_pinch, self.inset, self.over = nseg, back_pinch, inset, over
        self.mouth = mouth
        self.zones = zones
        self.n_arc, self.n_chord = n_arc, n_chord

    def prof(self, z):
        t = self.table
        if z >= t[0][0]:
            return t[0][1:]
        for a, b in zip(t, t[1:]):
            if b[0] <= z <= a[0]:
                u = (z - a[0]) / (b[0] - a[0])
                return tuple(a[i] + (b[i] - a[i]) * u for i in (1, 2, 3))
        return t[-1][1:]

    def sec(self, z, inset=0.0):
        """-> (centre y, half height, half width) of the section at z."""
        top, bot, hw = self.prof(z)
        return (top + bot) / 2, max(0.0015, (top - bot) / 2 - inset), max(0.0015, hw - inset)

    def pt(self, z, th, inset=0.0):
        yc, ry, hw = self.sec(z, inset)
        s, c = math.sin(th), math.cos(th)
        return V((hw * c * (1 - self.back_pinch * max(0.0, s)), yc + ry * s, z))

    def normal(self, z, th):
        """Approximate outward surface normal at (z, th) (finite differences)."""
        p = self.pt(z, th)
        tz = self.pt(z + 0.002, th) - self.pt(z - 0.002, th)
        tt = self.pt(z, th + 0.01) - self.pt(z, th - 0.01)
        n = tt.cross(tz).normalized()
        if n.dot(p - V((0, self.sec(z)[0], z))) < 0:
            n = -n
        return n

    def mouth_y(self, z):
        (z0, y0), (z1, y1) = self.mouth
        return y0 + (y1 - y0) * (z - z0) / (z1 - z0)

    def zone_mat(self, th):
        v = 0.5 + 0.5 * math.sin(th)
        return self.mat_back if v >= self.zones[0] else (self.mat_belly if v < self.zones[1] else self.mat_side)

    def stations(self, z0, z1, step=0.065, keep=0.018):
        """Profile stations between z0 < z1 (none closer than `keep` to the ends) plus even fill so no gap exceeds step."""
        zs = sorted({z0, z1} | {z for z, *_ in self.table if z0 + keep < z < z1 - keep})
        out = [zs[0]]
        for a, b in zip(zs, zs[1:]):
            k = max(1, int(math.ceil((b - a) / step - 1e-6)))
            out += [a + (b - a) * i / k for i in range(1, k + 1)]
        return out

    def body_rings(self, z_lo, z_hi, ext_lo, ext_hi, ring_fn):
        """Rings from z_lo to z_hi (nominal) with inset extensions of `over` beyond each end where ext_*.
        Returns rings ordered from the rear (low z) to the front."""
        rings = []
        if ext_lo:
            rings += [ring_fn(z_lo - self.over, self.inset), ring_fn(z_lo - 0.001, self.inset)]
        rings += [ring_fn(z, 0.0) for z in self.stations(z_lo, z_hi)]
        if ext_hi:
            rings += [ring_fn(z_hi + 0.001, self.inset), ring_fn(z_hi + self.over, self.inset)]
        return rings

    def full_ring(self, z, inset):
        return [self.pt(z, 2 * math.pi * k / self.nseg, inset) for k in range(self.nseg)]

    def full_mat(self, i, k):
        return self.zone_mat(2 * math.pi * (k + 0.5) / self.nseg)

    def clip_ring(self, z, inset, upper):
        """Skull (upper) / jaw (lower) section: the ellipse arc above / below the mouth line + the flat chord (palate /
        mouth floor). Counter-clockwise seen from +z like the full rings. -> (points, arc angles)."""
        yc, ry, hw = self.sec(z, inset)
        s = max(-0.95, min(0.95, (self.mouth_y(z) - yc) / ry))
        t0 = math.asin(s)
        n_arc = self.n_arc
        if upper:
            ths = [t0 + (math.pi - 2 * t0) * j / (n_arc - 1) for j in range(n_arc)]
        else:
            ths = [math.pi - t0 + (math.pi + 2 * t0) * j / (n_arc - 1) for j in range(n_arc)]
        pts = [self.pt(z, th, inset) for th in ths]
        a, b = pts[-1], pts[0]
        pts += [a.lerp(b, (j + 1) / (self.n_chord + 1)) for j in range(self.n_chord)]
        return pts, ths
