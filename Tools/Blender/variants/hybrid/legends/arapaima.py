"""
Legend module: 피라루쿠 ARAPAIMA (swamp, backdrop "swamp", surface lures). Loaded by hyb_legend3d.py (model) and
hyb_legend_preview.py (review mock). Design row: Docs/legends_rollout.md 3.2 (+ 4.3 for the set, 7 for the rig).

The read at any size: a very long, nearly cylindrical body (depth 0.14 L) with a flat, broad, bony head (darker
"helmet", wider than it is tall), an UPTURNED mouth (the gape rises steeply to the snout; the lower jaw forms the
front), small eyes high on the head, olive-grey scales on the front two thirds turning into red-edged scales on the
rear third, and the "paddle": the dorsal and anal fins set far back (z -0.21 .. -0.42) framing a small rounded tail,
all dark red-brown with a red margin.

No species bones (rollout 7): Head (with the snout / upper jaw shell), Jaw (the upturned lower jaw, opens +X up to
45 deg), the generic spine / tail chain, Pec / Pel pairs, Dorsal2 + Anal (the paddle fins, both on Spine.B3), and
Tail.Upper / Tail.Lower (the two halves of the rounded caudal fin). No Skull and no Dorsal1.
Spine joints sit ON the body splits (z 0.29 / 0.0 / -0.10 / -0.22 / -0.40): Spine.B3 is one rigid "paddle" segment
carrying both long fins, so the swim wave bends in front of and behind the paddle and never tears a fin base.
"""
import math
import random
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
import hyb_legend_kit as K

V = K.V

# ============================================================================ identity
ID = "arapaima"
MODEL = "legend_arapaima"
ARMATURE = "Arapaima"                # the FBX root node
PREFIX = "arap"                      # every material is arap_*
CM = (150, 300)                      # GameDatabase: F("arapaima", "피라루쿠", L, 150, 300, ...)
STAGE = "swamp"
BACKDROP = "swamp"                   # encounter_sets/swamp.py
PRESET = "swamp"

# ============================================================================ palette (rollout 3.2)
OUTLINE = "#0c100a"                  # dark olive ink (the swamp murk), not the cave's navy
RAMPS = {
    "arap_back": ["#1a2418", "#26341f", "#34462a"],
    "arap_side": ["#2e3c2a", "#44543a", "#5e6e4a"],
    "arap_belly": ["#4a5242", "#6a7258", "#8a9272"],
    "arap_red": ["#6a1e14", "#a0301e", "#d85a34"],
    "arap_scale": ["#1e2a1a", "#2a3822", "#3a4a2e"],
    "arap_head": ["#1a1e16", "#2a3024", "#3e4634"],
    "arap_fin": ["#3a1a14", "#6a2a1e", "#a04028"],
    "arap_mouth": ["#3a1410", "#6a2a20", "#8a4030"],
    "arap_eye_ring": ["#0a0806", "#18120c", "#2a2016"],
    "arap_eye_glow": ["#8a3a18", "#ff7a3a", "#ffe6c8"],
}
# swamp lure light (rollout 4.1 swamp column; eye colours 3.2)
LURE_LIGHT = {"abyss": "#10180e", "fogOutline": "#2e3a22", "eyeCore": "#ffe6c8", "eyeGlow": "#ff7a3a",
              "frameLine": "#d8e070"}
FACE_BONES = ["Head", "Jaw"]
RIG = {"limits": [{"bone": "Jaw", "max": 45}]}


# ============================================================================ body
def _ss(e0, e1, x):
    """smoothstep from e0 to e1 (e0 > e1 allowed: falls instead of rises)."""
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


class ArapBody(K.Body):
    """K.Body with the arapaima's flat, broad head: towards the head the upper half of the section becomes a
    superellipse (exponent n_top 2 -> 3.1: a flat top with rounded shoulders, the 'spoon' snout seen from above), the
    throat a little flatter (n_bot 2 -> 2.4), and the back pinch fades out on the head. clip_ring() solves the
    mouth-line angle on that section so the skull / jaw shells still split exactly on the mouth line."""

    def n_top(self, z):
        return 2.0 + 1.1 * _ss(0.10, 0.32, z)

    def n_bot(self, z):
        return 2.0 + 0.4 * _ss(0.15, 0.36, z)

    def pinch(self, z):
        return self.back_pinch * (1.0 - _ss(0.12, 0.30, z))

    def pt(self, z, th, inset=0.0):
        yc, ry, hw = self.sec(z, inset)
        s, c = math.sin(th), math.cos(th)
        e = 2.0 / (self.n_top(z) if s > 0 else self.n_bot(z))
        cx = math.copysign(abs(c) ** e, c)
        sy = math.copysign(abs(s) ** e, s)
        return V((hw * cx * (1 - self.pinch(z) * max(0.0, sy)), yc + ry * sy, z))

    def th_at(self, z, y, inset=0.0):
        yc, ry, hw = self.sec(z, inset)
        m = max(-0.95, min(0.95, (y - yc) / ry))
        n = self.n_top(z) if m > 0 else self.n_bot(z)
        return math.asin(math.copysign(abs(m) ** (n / 2.0), m))

    def clip_ring(self, z, inset, upper):
        t0 = self.th_at(z, self.mouth_y(z), inset)
        n_arc = self.n_arc
        if upper:
            ths = [t0 + (math.pi - 2 * t0) * j / (n_arc - 1) for j in range(n_arc)]
        else:
            ths = [math.pi - t0 + (math.pi + 2 * t0) * j / (n_arc - 1) for j in range(n_arc)]
        pts = [self.pt(z, th, inset) for th in ths]
        a, b = pts[-1], pts[0]
        # the chord is not flat: the mouth floor sinks into a trough between the jaw rims and the palate arches up,
        # so the open mouth reads as a cavity (not a flat red tongue); shut, the two shells still meet on the rims
        yc, ry, _ = self.sec(z, inset)
        my = self.mouth_y(z)
        dip = -0.5 * max(0.0, my - (yc - ry)) if not upper else 0.35 * max(0.0, (yc + ry) - my)
        for j in range(self.n_chord):
            q = a.lerp(b, (j + 1) / (self.n_chord + 1))
            pts.append(V((q.x, q.y + dip, q.z)))
        return pts, ths


# profile (Unity model space, metres): z, top y, bottom y, half-width x. The lower jaw's tip / snout at z +0.50 (the
# mouth line meets the front high up); the head is wider than tall (0.046 vs 0.041 at the gape corner); deepest
# 0.138 at z +0.06 .. -0.04; the peduncle stays deep (the paddle) and ends at z -0.445 under the rounded caudal fin.
ARAP_BODY = [
    (0.500, 0.023, 0.011, 0.013), (0.495, 0.027, 0.004, 0.023), (0.485, 0.031, -0.004, 0.030),
    (0.465, 0.035, -0.014, 0.036), (0.440, 0.039, -0.026, 0.041), (0.405, 0.044, -0.038, 0.046),
    (0.360, 0.050, -0.047, 0.051),
    (0.310, 0.055, -0.054, 0.054), (0.250, 0.060, -0.060, 0.056), (0.160, 0.065, -0.066, 0.057),
    (0.060, 0.068, -0.070, 0.056), (-0.040, 0.068, -0.070, 0.054), (-0.120, 0.066, -0.067, 0.050),
    (-0.200, 0.061, -0.062, 0.044), (-0.260, 0.056, -0.056, 0.037), (-0.320, 0.049, -0.049, 0.029),
    (-0.370, 0.042, -0.042, 0.022), (-0.410, 0.035, -0.035, 0.016), (-0.445, 0.030, -0.030, 0.012),
]
OVER = 0.015
INSET = 0.0015
NSEG = 16
N_ARC = 9
MOUTH_HINGE = (0.410, -0.021)        # (z, y) gape corner = the Jaw bone: low, just behind the eye
MOUTH_FRONT = (0.500, 0.018)         # (z, y) the mouth at the front: high (upturned), the Mouth empty
BODY = ArapBody(ARAP_BODY, ("arap_back", "arap_side", "arap_belly"), nseg=NSEG, back_pinch=0.06, inset=INSET,
                over=OVER, mouth=(MOUTH_HINGE, MOUTH_FRONT), n_arc=N_ARC, n_chord=2)

HEAD_Z = 0.29                        # head joint: the bony helmet in front of it
J = {"Tail": -0.40, "Spine.B3": -0.22, "Spine.B2": -0.10, "Spine.B1": 0.0, "Spine.F": 0.0, "Head": HEAD_Z}
EYE_Z, EYE_TH = 0.434, math.radians(24)      # small eyes high on the flat head, inside its side silhouette
EYE_RING, EYE_R, PUPIL = 0.0122, 0.0086, 0.0037
PEC_BASE_Z, PEC_TH = 0.283, math.radians(-50)
PEL_BASE_Z, PEL_TH = -0.035, math.radians(-74)


def _base(z, th, sg, sink=0.004):
    p = BODY.pt(z, th)
    n = BODY.normal(z, th)
    q = p - n * sink
    return (sg * abs(q.x), q.y, q.z)


PEC_L, PEC_R = _base(PEC_BASE_Z, PEC_TH, -1), _base(PEC_BASE_Z, PEC_TH, 1)
PEL_L, PEL_R = _base(PEL_BASE_Z, PEL_TH, -1), _base(PEL_BASE_Z, PEL_TH, 1)

# bones: (name, parent, head in Unity model space). Rest rotation = identity for every bone.
BONES = [
    ("Root", None, (0, 0, 0)),
    ("Spine.F", "Root", (0, 0, J["Spine.F"])),
    ("Head", "Spine.F", (0, 0, HEAD_Z)),
    ("Jaw", "Head", (0, MOUTH_HINGE[1], MOUTH_HINGE[0])),
    ("Pec.L", "Spine.F", tuple(round(v, 4) for v in PEC_L)),
    ("Pec.R", "Spine.F", tuple(round(v, 4) for v in PEC_R)),
    ("Spine.B1", "Root", (0, 0, J["Spine.B1"])),
    ("Pel.L", "Spine.B1", tuple(round(v, 4) for v in PEL_L)),
    ("Pel.R", "Spine.B1", tuple(round(v, 4) for v in PEL_R)),
    ("Spine.B2", "Spine.B1", (0, 0, J["Spine.B2"])),
    ("Spine.B3", "Spine.B2", (0, 0, J["Spine.B3"])),
    ("Dorsal2", "Spine.B3", (0, 0.047, -0.31)),       # mid-base of the long dorsal (it sculls about Z round here)
    ("Anal", "Spine.B3", (0, -0.047, -0.30)),
    ("Tail", "Spine.B3", (0, 0, J["Tail"])),
    ("Tail.Upper", "Tail", (0, 0.004, -0.42)),
    ("Tail.Lower", "Tail", (0, -0.004, -0.42)),
]


def zone(z, th):
    """Body material by position: the bony head (dark helmet, pale throat), then back / side / belly; the peduncle's
    flank goes red."""
    v = 0.5 + 0.5 * math.sin(th)
    if z > HEAD_Z:
        return "arap_belly" if v < 0.2 else "arap_head"
    if v >= 0.75:
        return "arap_back"
    if v < 0.2:
        return "arap_belly"
    return "arap_red" if z < -0.405 else "arap_side"


# ============================================================================ local helpers
def _crescent(part, bvh, c, n, r, thick, mat, span=84.0, lift=0.0012):
    """A scale's free edge: a pointed crescent convex towards the tail, centred on the scale (c) at the skin, conformed
    to the part's surface. 4 outer + 2 inner points, 4 flat triangles (the budget: ~180 scales)."""
    n = V(n).normalized()
    t = V((0, 0, -1)) - n * (-n.z)
    t.normalize()
    u = n.cross(t).normalized()
    outer = []
    for a in (-span, -span / 3, span / 3, span):
        ar = math.radians(a)
        outer.append(c + t * (r * math.cos(ar)) + u * (r * math.sin(ar)))
    ci = c - t * thick
    inner = []
    for a in (-span / 3, span / 3):
        ar = math.radians(a)
        inner.append(ci + t * (r * math.cos(ar)) + u * (r * math.sin(ar)))

    def snap(p):
        loc, nrm, _, _ = bvh.find_nearest(p)
        return part.bm.verts.new(loc + nrm * lift if loc is not None else p + n * lift)
    o = [snap(p) for p in outer]
    i = [snap(p) for p in inner]
    tris = [(o[0], o[1], i[0]), (o[1], i[1], i[0]), (o[1], o[2], i[1]), (o[2], o[3], i[1])]
    faces = []
    for vs in tris:
        e1, e2 = vs[1].co - vs[0].co, vs[2].co - vs[0].co
        if e1.cross(e2).dot(n) < 0:
            vs = (vs[0], vs[2], vs[1])
        faces.append(part.face(vs, mat, False))
    return faces


def _offset_in(F, k, w, cen):
    """Point k of the 2D polyline F [(z, y)] moved w towards the fin's inside (the side of cen)."""
    a = V(F[max(k - 1, 0)])
    b = V(F[min(k + 1, len(F) - 1)])
    t = (b - a).normalized()
    nn = V((-t.y, t.x))
    if nn.dot(V(cen) - V(F[k])) < 0:
        nn = -nn
    return V(F[k]) + nn * w


def _sagittal_fin(part, free, base, mat, edge_mat, width, t0, end_dir=None, th=0.004):
    """A fin in the x = 0 plane. free [(z, y)]: the free edge from the front base point to the rear end; base [(z, y)]:
    the buried base line from the rear end back towards the front (without the end points). A margin band of
    edge_mat runs along the free edge from fraction t0 (width ramps up over 0.2 of the edge). end_dir: the offset of the
    band's last point (None = the band closes on the rear end point, else it keeps its width along end_dir)."""
    m = len(free)
    cen = sum((V(p) for p in free + base), V((0, 0))) / (m + len(base))
    k0 = max(1, int(round(t0 * (m - 1))))
    inner = {}
    for k in range(k0, m - 1):
        w = width * min(1.0, (k - k0 + 1) / max(1.0, 0.2 * (m - 1)))
        inner[k] = _offset_in(free, k, w, cen)
    if end_dir is not None:
        inner[m - 1] = V(free[m - 1]) + V(end_dir).normalized() * width
    last = m - 1 if end_dir is not None else m - 2
    to3 = lambda p: V((0.0, p[1], p[0]))  # noqa: E731
    # inner fin: front part of the free edge, then the band's inner line, (the rear end), the base back to the front
    poly = [free[k] for k in range(0, k0)] + [inner[k] for k in range(k0, last + 1)]
    if end_dir is None:
        poly.append(free[m - 1])
    poly += list(base)
    K.plate(part, [to3(p) for p in poly], (1, 0, 0), th, mat)
    band = [free[k0 - 1]] + [free[k] for k in range(k0, m)]
    band += [inner[k] for k in range(last, k0 - 1, -1)]
    K.plate(part, [to3(p) for p in band], (1, 0, 0), th, edge_mat)


def _paddle(part, base, d, nb, length, width, shape, mat, th=0.0035):
    """A paired fin: a flat rounded paddle from `base` along d (blade normal nb)."""
    base = V(base)
    d = V(d).normalized()
    nb = V(nb)
    nb = (nb - d * nb.dot(d)).normalized()
    e = nb.cross(d).normalized()
    pts = [base + d * (u * length) + e * (w * width) for u, w in shape]
    K.plate(part, pts, nb, th, mat)


PADDLE = [(-0.12, -0.30), (0.25, -0.48), (0.62, -0.46), (0.90, -0.28), (1.0, 0.0), (0.93, 0.26), (0.68, 0.42),
          (0.30, 0.44), (-0.12, 0.30)]


def build():
    """-> (parts {bone: Part}, empties {name: (parent bone, position U, facing U or None)}, info)."""
    B = BODY
    rng = random.Random(23)
    parts = {}

    def P(name):
        if name not in parts:
            parts[name] = K.Part(name)
        return parts[name]

    def seg_loft(p, rings, cap0, cap1):
        zs = [r[0].z for r in rings]
        K.loft(p, rings, lambda i, k: zone((zs[i] + zs[i + 1]) / 2, 2 * math.pi * (k + 0.5) / NSEG),
               cap0=cap0, cap1=cap1)

    # ------------------------------------------------ body segments (joint z, rear -> front); the FRONT segment carries
    # the inset extension back over each joint, the rear one ends in a cap on the joint
    segs = [  # bone, nominal z range, extension at the rear / front
        ("Tail", (-0.445, J["Tail"]), False, False),
        ("Spine.B3", (J["Tail"], J["Spine.B3"]), True, False),
        ("Spine.B2", (J["Spine.B3"], J["Spine.B2"]), True, False),
        ("Spine.B1", (J["Spine.B2"], J["Spine.B1"]), True, False),
        ("Spine.F", (J["Spine.B1"], HEAD_Z), True, False),
        ("Head", (HEAD_Z, MOUTH_HINGE[0]), True, True),   # the front extension = the throat plug under the jaw
    ]
    bvh = {}
    for bone, (z0, z1), e0, e1 in segs:
        p = P(bone)
        rings = B.body_rings(z0, z1, e0, e1, B.full_ring)
        cap1 = "arap_mouth" if bone == "Head" else "arap_side"
        seg_loft(p, rings, "arap_side", cap1)
        p.bm.normal_update()
        bvh[bone] = (BVHTree.FromBMesh(p.bm), (z0, z1))
    # ------------------------------------------------ snout (upper shell, on Head) / upturned lower jaw (on Jaw)
    zh = MOUTH_HINGE[0]
    for bone, upper in (("Head", True), ("Jaw", False)):
        p = P(bone)
        zs = [zh - OVER, zh - 0.001] + B.stations(zh, 0.50, 0.03, 0.008)
        rings, angs = [], []
        for i, z in enumerate(zs):
            pts, ths = B.clip_ring(z, INSET if i < 2 else 0.0, upper)
            rings.append(pts)
            angs.append(ths)

        def fm(i, k, angs=angs, zs=zs):
            if k >= N_ARC - 1:
                return "arap_mouth"                       # palate / mouth floor
            return zone((zs[i] + zs[i + 1]) / 2, (angs[i][k] + angs[i][k + 1]) / 2)
        K.loft(p, rings, fm, cap0="arap_mouth", cap1="arap_head")
        p.bm.normal_update()
        if bone == "Jaw":
            bvh["Jaw"] = (BVHTree.FromBMesh(p.bm), (zh, 0.50))
        else:
            bvh["Snout"] = (BVHTree.FromBMesh(p.bm), (zh, 0.50))
    # the bony tongue on the mouth floor (hidden while the mouth is shut)
    zt = 0.446
    sl = math.radians(23)                                  # along the mouth line (it rises ~23 deg)
    yc_t, ry_t, _ = B.sec(zt)
    y_floor = B.mouth_y(zt) - 0.5 * (B.mouth_y(zt) - (yc_t - ry_t))           # the trough of the mouth floor
    K.ellipsoid(P("Jaw"), V((0, y_floor + 0.0022, zt)), (0.0095, 0.0036, 0.024),
                ((1, 0, 0), (0, math.cos(sl), -math.sin(sl)), (0, math.sin(sl), math.cos(sl))),
                "arap_belly", 8, 3, smooth=True)
    # ------------------------------------------------ scales: pointed crescents (the free edge of each big scale),
    # olive on the front two thirds, red towards the tail (denser red low on the flank), none across a joint
    joints = [J["Tail"], J["Spine.B3"], J["Spine.B2"], J["Spine.B1"], HEAD_Z]
    rows = [-64, -42, -20, 2, 24, 46, 68]
    dz = 0.040
    n_red = n_olive = 0
    for sg in (1, -1):
        for j, thd in enumerate(rows):
            z = 0.268 - (j % 2) * dz / 2
            while z > -0.412:
                zc = z
                z -= dz
                if thd >= 68 and zc > -0.14:
                    continue                                        # the dark back: edges would not read, save tris
                ry = B.sec(zc)[1]
                f = min(1.0, (ry / 0.069) ** 0.85)
                r = 0.0205 * f
                apex = zc - r
                lo, hi = apex - 0.004, apex + 0.92 * r + 0.004
                if any(lo <= jz <= hi for jz in joints):
                    continue
                if abs(zc - PEC_BASE_Z) < 0.05 and -58 < thd < -30:
                    continue                                        # under the pectoral
                if abs(zc - PEL_BASE_Z) < 0.05 and thd < -60:
                    continue
                th = math.radians(thd) if sg > 0 else math.pi - math.radians(thd)
                c = B.pt(zc, th)
                n = B.normal(zc, th)
                pred = _ss(0.10, -0.20, zc) * (0.65 if thd >= 46 else 1.0) * (1.0 if thd <= 2 else 0.92)
                red = rng.random() < pred
                mat = "arap_red" if red else "arap_scale"
                thick = (0.0082 if red else 0.0062) * max(0.7, f)
                bone = next((b for b, (z0, z1), _, _ in segs if z0 <= apex and apex + 0.92 * r <= z1), None)
                if bone is None:
                    continue
                _crescent(P(bone), bvh[bone][0], c, n, r, thick, mat)
                if red:
                    n_red += 1
                else:
                    n_olive += 1
    info = {"scalesRed": n_red, "scalesOlive": n_olive}
    # ------------------------------------------------ the bony head: gill-cover edge (dark slit), preopercle groove
    hb = bvh["Head"][0]
    for sg in (1, -1):
        def TH(d):
            return math.radians(d) if sg > 0 else math.pi - math.radians(d)
        pts, ns = [], []
        for k in range(9):
            u = -1 + 2 * k / 8
            thd = -62 + 104 * (u + 1) / 2
            z = 0.305 - 0.009 * (1 - u * u)
            pts.append(B.pt(z, TH(thd)))
            ns.append(B.normal(z, TH(thd)))
        K.strip_decal(P("Head"), hb, pts, ns, 0.0055, "arap_eye_ring", lift=0.0012)     # the gill slit: dark
        pts, ns = [], []
        for z, thd in ((0.398, 22), (0.378, 6), (0.364, -14), (0.360, -34), (0.368, -52)):
            pts.append(B.pt(z, TH(thd)))
            ns.append(B.normal(z, TH(thd)))
        K.strip_decal(P("Head"), hb, pts, ns, 0.0040, "arap_back", lift=0.0012)
    # ------------------------------------------------ eyes: small, high on the flat head (ring + glow lens + pupil)
    empties = {}
    sb = bvh["Snout"][0]
    for side, sg in (("L", -1), ("R", 1)):
        th = EYE_TH if sg > 0 else math.pi - EYE_TH
        e0 = B.pt(EYE_Z, th)
        loc, _, _, _ = sb.find_nearest(e0)
        e = V(loc) if loc is not None else e0
        n = V((sg * 0.84, 0.30, 0.44)).normalized()          # out, a little up and forward
        a, b, n = K.frame_from(n, (0, 1, 0))
        K.ellipsoid(P("Head"), e + n * 0.0006, (EYE_RING, EYE_RING, 0.004), (a, b, n), "arap_eye_ring", 10, 4,
                    smooth=False)
        g = K.Part("Eye." + side)
        parts["Eye." + side] = g
        K.ellipsoid(g, e + n * 0.0022, (EYE_R, EYE_R, 0.0048), (a, b, n), "arap_eye_glow", 10, 4, smooth=True)
        K.ellipsoid(P("Head"), e + n * (0.0022 + 0.0046), (PUPIL, PUPIL * 0.85, 0.0014), (a, b, n),
                    "arap_eye_ring", 8, 3, smooth=False)
        empties["Eye." + side] = ("Head", e, n)
    empties["Mouth"] = ("Head", V((0, MOUTH_FRONT[1] - 0.002, MOUTH_FRONT[0] - 0.006)), None)
    # ------------------------------------------------ the paddle: long dorsal + anal far back (red margins)
    top = lambda z: B.prof(z)[0]  # noqa: E731
    bot = lambda z: B.prof(z)[1]  # noqa: E731
    d_free = [(-0.212, top(-0.212) - 0.005), (-0.224, top(-0.224) + 0.009), (-0.252, top(-0.252) + 0.017),
              (-0.290, top(-0.290) + 0.025), (-0.330, top(-0.330) + 0.032), (-0.362, top(-0.362) + 0.038),
              (-0.388, top(-0.388) + 0.042), (-0.407, top(-0.407) + 0.040), (-0.419, 0.066), (-0.423, 0.052),
              (-0.418, 0.040), (-0.404, top(-0.404) - 0.005)]
    d_base = [(z, top(z) - 0.005) for z in (-0.37, -0.33, -0.29, -0.25)]
    _sagittal_fin(P("Dorsal2"), d_free, d_base, "arap_fin", "arap_red", 0.0075, 0.45)
    a_free = [(-0.200, bot(-0.200) + 0.005), (-0.213, bot(-0.213) - 0.010), (-0.242, bot(-0.242) - 0.019),
              (-0.282, bot(-0.282) - 0.027), (-0.322, bot(-0.322) - 0.034), (-0.358, bot(-0.358) - 0.040),
              (-0.386, bot(-0.386) - 0.043), (-0.406, bot(-0.406) - 0.041), (-0.419, -0.067), (-0.423, -0.053),
              (-0.418, -0.040), (-0.404, bot(-0.404) + 0.005)]
    a_base = [(z, bot(z) + 0.005) for z in (-0.37, -0.33, -0.29, -0.25, -0.22)]
    _sagittal_fin(P("Anal"), a_free, a_base, "arap_fin", "arap_red", 0.0075, 0.45)
    # ------------------------------------------------ rounded caudal fin: two halves meeting on the midline
    c_free = [(-0.414, 0.027), (-0.430, 0.041), (-0.447, 0.051), (-0.464, 0.055), (-0.479, 0.052), (-0.491, 0.043),
              (-0.498, 0.029), (-0.5, 0.014), (-0.5, 0.0)]
    c_base = [(-0.414, 0.0)]
    _sagittal_fin(P("Tail.Upper"), c_free, c_base, "arap_fin", "arap_red", 0.0095, 0.3, end_dir=(1, 0))
    _sagittal_fin(P("Tail.Lower"), [(z, -y) for z, y in c_free], [(z, -y) for z, y in c_base], "arap_fin",
                  "arap_red", 0.0095, 0.3, end_dir=(1, 0))
    # ------------------------------------------------ small low pectorals, small abdominal pelvics
    for side, sg in (("L", -1), ("R", 1)):
        _paddle(P("Pec." + side), PEC_L if sg < 0 else PEC_R, (sg * 0.55, -0.30, -0.78), (sg * 0.30, 0.95, 0.0),
                0.060, 0.036, PADDLE, "arap_scale")
        _paddle(P("Pel." + side), PEL_L if sg < 0 else PEL_R, (sg * 0.22, -0.42, -0.88), (sg * 0.97, -0.15, 0.0),
                0.044, 0.026, PADDLE, "arap_scale")
    info["jointsZ"] = J
    info["mouthLine"] = {"hinge_zy": MOUTH_HINGE, "front_zy": MOUTH_FRONT}
    info["eye"] = {"z": EYE_Z, "ring": EYE_RING, "lens": EYE_R}
    return parts, empties, info


# ============================================================================ check renders (hyb_legend3d.check)
CHECK_OPEN = {"Jaw": (45, 0, 0), "Head": (-6, 0, 0), "Pec.L": (0, 25, 0), "Pec.R": (0, -25, 0),
              "Pel.L": (0, 15, 0), "Pel.R": (0, -15, 0)}
CHECK_BEND = {"Spine.F": (0, -6, 0), "Head": (0, -12, 0), "Spine.B1": (0, 10, 0), "Spine.B2": (0, 15, 0),
              "Spine.B3": (0, 15, 0), "Tail": (0, 15, 0), "Tail.Upper": (0, 8, 0), "Tail.Lower": (0, 8, 0),
              "Pec.L": (0, 15, 0), "Pec.R": (0, -15, 0)}


# ============================================================================ review mock (hyb_legend_preview.py)
PREVIEW_PREFIX = "legend_arapaima"
PREVIEW_LURE = "frog"                # lure billboard key (Encounter/lure_frog_0/1, built by encounter_sets/swamp.py)
PREVIEW_CM = 220
LURE_R = 2.2                         # swamp visibility (rollout 3.2 light R)
TURNTABLE = dict(
    open={"Jaw": (45, 0, 0), "Head": (-6, 0, 0), "Pec.L": (0, 25, 0), "Pec.R": (0, -25, 0), "Pel.L": (0, 15, 0),
          "Pel.R": (0, -15, 0), "Dorsal2": (0, 0, 12), "Anal": (0, 0, -12), "Spine.B3": (0, 4, 0), "Tail": (0, 6, 0)},
    bend={"Spine.F": (0, -2, 0), "Head": (0, -7, 0), "Spine.B1": (0, 4, 0), "Spine.B2": (0, 8, 0),
          "Spine.B3": (0, 11, 0), "Tail": (0, 14, 0), "Tail.Upper": (0, 6, 0), "Tail.Lower": (0, 6, 0),
          "Pec.L": (0, 20, 0), "Pec.R": (0, -20, 0), "Dorsal2": (0, 0, -10), "Anal": (0, 0, 10)},
    lit={"Pec.L": (0, 15, 0), "Pec.R": (0, -15, 0)},
    game={},
)


def _fwd(hdg, pitch):
    h, p = math.radians(hdg), math.radians(pitch)
    return V((math.sin(h) * math.cos(p), math.sin(p), math.cos(h) * math.cos(p)))


def _rot(hdg, pitch, bank=0.0):
    """Set-frame rotation of a fish placed with heading hdg, bank and Root pitch (+ = nose up)."""
    return (Matrix.Rotation(math.radians(hdg), 3, "Y") @ Matrix.Rotation(math.radians(bank), 3, "Z")
            @ Matrix.Rotation(math.radians(-pitch), 3, "X"))


def _ring(img, cx, cy, rx, ry, col, a=1.0):
    """A 1 px ellipse (the pop ring / boil edge)."""
    import numpy as np
    H, W = img.shape[:2]
    n = int(2 * math.pi * max(rx, ry)) + 8
    seen = set()
    for k in range(n):
        t = 2 * math.pi * k / n
        x, y = int(round(cx + rx * math.cos(t))), int(round(cy + ry * math.sin(t)))
        if (x, y) in seen or not (0 <= x < W and 0 <= y < H):
            continue
        seen.add((x, y))
        img[y, x, :3] = img[y, x, :3] * (1 - a) + np.asarray(col, np.float32) * a


def preview_beats(M, sc, cam, lure, surface):
    """Six mock beats for the swamp (rollout 3.2): eyes rising out of the brown murk -> the silhouette's rising spiral
    -> curious under the popping frog -> the pass under the lure -> hanging nose-up under the lure -> the strike from
    below at full screen. The lure floats on the surface line (0.05 above the lure point)."""
    rng = random.Random(5)
    frames = []
    s = sc.cm / 100.0
    surf_y = M.PROF.get("surface_y", lure.y + 0.05)          # encounter_sets/swamp.PROFILE (set frame, lure = 0)
    floor_y = M.PROF.get("floor_y", -3.0)
    boil = M.R.hexrgb(M.PROF.get("snow_near", "#a8a070"))
    white = M.R.hexrgb("#e8e8d0")

    def rows(cam_, cpos):
        hy = M.project(cam_, V((cpos.x, floor_y, cpos.z + 30.0)))[1]
        cy = M.project(cam_, V((cpos.x, surf_y, cpos.z + 30.0)))[1]
        return hy, cy
    hy, cy = rows(cam, M.CAM)
    print("MOCK arapaima horizon row", round(hy, 1), "ceiling row", round(cy, 1),
          "lure px", [round(v, 1) for v in M.project(cam, lure)[:2]])

    def place_head(head_u, hdg, pitch, bank=0.0, head_model=(0, 0.02, 0.43)):
        """Place so the model point head_model (default: between the eyes) lands on head_u."""
        R3 = _rot(hdg, pitch, bank)
        root = V(head_u) - (R3 @ V(head_model)) * s
        sc.place(root, hdg, bank)
        return root

    def pose(pitch, **kw):
        kw["Root"] = (-pitch, 0, 0)
        sc.pose(**kw)

    def windowed(tag, lure_u, lure_frame=0, eyes_mult=1.0, bubbles=0, line=True, pop=False, fish_front=True):
        lpx = M.project(cam, lure_u)[:2]
        upx = M.project(cam, lure_u + V(M.SETP.line_up))[:2]
        fish = M.render_fish(sc, cam, lure_u, tag)
        under = M.back_layers(lpx, hy, ceil_row=cy)
        if fish_front:
            M.put_lure(under, lpx, PREVIEW_LURE, lure_frame)
        under = M.front_fx(under, lpx, upx if line else None, rng, snow=12, bubbles=bubbles)
        if pop:
            _ring(under, lpx[0], lpx[1] - 2, 7, 2, white, 0.7)
            _ring(under, lpx[0], lpx[1] - 2, 11, 3, white, 0.35)
        M.R.over(under, fish, 0, 0)
        if not fish_front:
            M.put_lure(under, lpx, PREVIEW_LURE, lure_frame)
        M.put_eyeshine(under, sc.eyes_px(cam), eyes_mult)
        return M.compose_window(*surface, under, M.WIN)

    swim = dict(Spine_F=(0, -2, 0), Spine_B1=(0, 3, 0), Spine_B2=(0, 6, 0), Spine_B3=(0, 9, 0), Tail=(0, 12, 0),
                Tail_Upper=(0, 6, 0), Tail_Lower=(0, 6, 0))
    # 1) eyes: two red-amber glints low and ahead in the brown murk, rising towards the lure (eyes0 -> eyes1 midway)
    head = V((2.1, -2.1, 4.6))
    hdg = math.degrees(math.atan2(-head.x, -head.z))
    pitch = math.degrees(math.atan2(-head.y, math.hypot(head.x, head.z)))
    place_head(head, hdg, pitch)
    pose(pitch, **swim)
    lx, ly, _ = M.project(cam, lure)
    up = M.project(cam, lure + V(M.SETP.line_up))[:2]
    fish = M.render_fish(sc, cam, lure, "a1", rim=False, eyes_only=True)
    under = M.back_layers((lx, ly), hy, ceil_row=cy)
    M.R.over(under, fish, 0, 0)
    under = M.front_fx(under, (lx, ly), up, rng, snow=10)
    M.put_lure(under, (lx, ly), PREVIEW_LURE, 0)
    M.put_eyeshine(under, sc.eyes_px(cam))
    frames.append(("eyes", M.compose_window(*surface, under, M.WIN)))
    # 2) approach: the long slow rising spiral (r ~2 m) - a huge dark shape, only the head reaching into the light
    head = V((0.95, -1.05, 1.15))
    place_head(head, -100.0, 20.0, bank=-8)
    pose(20.0, Spine_F=(0, -2, 0), Head=(-4, -6, 0), Spine_B1=(0, 4, 0), Spine_B2=(0, 7, 0), Spine_B3=(0, 10, 0),
         Tail=(0, 13, 0), Tail_Upper=(0, 6, 0), Tail_Lower=(0, 6, 0), Pec_L=(0, 10, 0), Pec_R=(0, -10, 0))
    frames.append(("approach", windowed("a2", lure, lure_frame=1, eyes_mult=0.7)))
    # 3) tease, curious: on the 1.2 m orbit 0.5 m under the surface, flank to the camera, the head tilted up at the
    #    frog after a pop (rings + bubbles at the surface line)
    head = V((0.70, -0.42, 0.60))
    place_head(head, -96.0, 12.0, bank=4)
    pose(12.0, Head=(-10, -12, 0), Spine_F=(0, -4, 0), Spine_B1=(0, 3, 0), Spine_B2=(0, 5, 0), Spine_B3=(0, 7, 0),
         Tail=(0, 10, 0), Tail_Upper=(0, 5, 0), Tail_Lower=(0, 5, 0), Pec_L=(0, 22, 0), Pec_R=(0, -22, 0),
         Pel_L=(0, 10, 0), Pel_R=(0, -10, 0), Dorsal2=(0, 0, -8), Anal=(0, 0, 8))
    frames.append(("tease", windowed("a3", lure + V((0, -0.03, 0)), lure_frame=1, bubbles=5, pop=True)))
    # 4) the pass (wary): 0.8 m under the lure, crossing between the camera and the lure - backlit, the red paddle
    #    catching the light on the turn
    head = V((-1.05, -0.70, -0.95))
    place_head(head, -62.0, 4.0, bank=-8)
    pose(4.0, Head=(0, -8, 0), Spine_F=(0, -4, 0), Spine_B1=(0, 4, 0), Spine_B2=(0, 8, 0), Spine_B3=(0, 12, 0),
         Tail=(0, 16, 0), Tail_Upper=(0, 8, 0), Tail_Lower=(0, 8, 0), Pec_L=(0, 12, 0), Pec_R=(0, -12, 0),
         Dorsal2=(0, 0, 14), Anal=(0, 0, -14))
    frames.append(("pass", windowed("a4", lure, lure_frame=0, line=False)))
    # 5) excited: hangs 1.0 m directly under the lure, head up 50, pectorals sculling, jaw just parting
    mouth = V((0.10, -0.92, 0.32))
    R3 = _rot(-80.0, 50.0)
    root = mouth - (R3 @ V((0, MOUTH_FRONT[1], MOUTH_FRONT[0]))) * s
    sc.place(root, -80.0, 0.0)
    pose(50.0, Jaw=(6, 0, 0), Head=(-4, 0, 0), Spine_F=(0, -1, 0), Spine_B1=(0, 2, 0), Spine_B2=(0, 4, 0),
         Spine_B3=(0, 6, 0), Tail=(0, -8, 0), Tail_Upper=(0, -4, 0), Tail_Lower=(0, -4, 0), Pec_L=(0, 30, 0),
         Pec_R=(0, -30, 0), Pel_L=(0, 18, 0), Pel_R=(0, -18, 0), Dorsal2=(0, 0, 8), Anal=(0, 0, -8))
    frames.append(("excited", windowed("a5", lure, lure_frame=0)))
    # 6) full screen at the bite: the explosive strike from below - rocketing up, the upturned jaw open 45, the boil
    #    breaking at the surface line around the frog
    cpos = V((-0.50, -0.12, -0.98))
    cam6 = M.setcam(pos=cpos, target=V((0.10, -0.20, 0.22)), f=M.FPX * 1.5)
    hdg6, pitch6 = -95.0, 48.0
    mouth = V((0.0, -0.02, 0.0))
    R3 = _rot(hdg6, pitch6, -6.0)
    root = mouth - (R3 @ V((0, MOUTH_FRONT[1], MOUTH_FRONT[0]))) * s
    sc.place(root, hdg6, -6.0)
    pose(pitch6, Jaw=(45, 0, 0), Head=(-6, 4, 0), Spine_F=(0, 3, 0), Spine_B1=(0, 5, 0), Spine_B2=(0, 8, 0),
         Spine_B3=(0, 11, 0), Tail=(0, 15, 0), Tail_Upper=(0, 8, 0), Tail_Lower=(0, 8, 0), Pec_L=(0, 32, 0),
         Pec_R=(0, -32, 0), Pel_L=(0, 20, 0), Pel_R=(0, -20, 0), Dorsal2=(0, 0, -12), Anal=(0, 0, 12))
    lx6, ly6, _ = M.project(cam6, lure)
    hy6, cy6 = rows(cam6, cpos)
    fish = M.render_fish(sc, cam6, lure + V((0, -0.05, 0)), "a6")
    under = M.back_layers((lx6, ly6), hy6, ceil_row=cy6)
    # the boil at the surface line: a white flash ring, bubbles bursting out of the gape
    _ring(under, lx6, ly6 - 4, 26, 5, white, 0.8)
    _ring(under, lx6, ly6 - 4, 38, 7, white, 0.45)
    M.put_lure(under, (lx6, ly6), PREVIEW_LURE, 1, scale=2)
    M.R.over(under, fish, 0, 0)
    under = M.front_fx(under, None, None, rng, snow=16)
    bs, bm = M.load("bubble_s"), M.load("bubble_m")
    for i in range(22):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(10, 46)
        x = int(lx6 + math.cos(a) * rr * 1.3)
        y = int(ly6 - 6 + math.sin(a) * rr * 0.55 - rng.uniform(0, 10))
        M.R.over(under, bm if i % 3 == 0 else bs, x, y)
    for i in range(10):                                                     # debris kicked up (tinted snow)
        x, y = int(lx6 + rng.uniform(-60, 60)), int(ly6 + rng.uniform(-8, 30))
        if 0 <= x < M.VW and 0 <= y < M.VH:
            under[y, x, :3] = under[y, x, :3] * 0.4 + boil * 0.6
    M.put_eyeshine(under, sc.eyes_px(cam6))
    frames.append(("bite_full", M.compose_window(*surface, under, (0, 0, M.VW, M.VH), frame=False)))
    return frames
