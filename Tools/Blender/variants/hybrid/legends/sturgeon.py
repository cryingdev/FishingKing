"""
Legend module: 철갑상어 STURGEON (ice, backdrop "ice"). Loaded by hyb_legend3d.py (model) and hyb_legend_preview.py
(review mock). Design row: Docs/legends_rollout.md 3.3 (trigger / moods / choreography / texts), 7 (species bones).

The armoured bottom-feeder: a long shark-like body with a PENTAGONAL section (roof-shaped back, flat belly) and five
rows of pale bony scutes on its corners (1 dorsal saw-tooth keel row, 2 lateral, 2 ventral), a long flat pointed
rostrum (z +0.38 .. +0.50), four barbels in a row across the underside of the snout (z ~ +0.42) in front of an
underslung PROTRUSIBLE mouth tube (`Lips`, z ~ +0.37), a heterocercal tail (the body runs up into the long upper
lobe), low wide planing pectorals, one dorsal (Dorsal2) and the anal far back, small pelvics.

Species rig (Unity localRotation Euler / localPosition on the identity rest, see legends/README.md):
  Lips        on Head   protrude p 0..1: localPosition += p (0, -0.030, 0.008), localRotation X += p 20 deg (RIG);
                        the tube is modelled RETRACTED (only a fleshy lip bump shows under the head) and long enough to
                        stay rooted in the head when it drops; its opening (the dark stur_mouth cap) faces ~20 deg
                        forward of straight down at rest and straight down when fully protruded.
  Jaw         on Lips   the rear (lower) lip; opens +X (swings back / down off the opening), clamp 25 (RIG).
  Mouth       on Lips   the lure point, just outside the opening (the worm is sucked up into the tube).
  Barbel.L1/R1 (inner), Barbel.L2/R2 (outer), on Head: they HANG from the snout raked ~18 deg FORWARD of vertical and
                        curve down, so the rollout's "feel" (X +25 deg * barbel) swings them to vertical / onto the worm
                        and the fake-out twitch (X +35 deg) sweeps them back across it; +X moves the tips back under the
                        snout towards the mouth, -X would rake them forward (not used).
"""
import math
import random
from mathutils.bvhtree import BVHTree
import hyb_legend_kit as K

V = K.V

# ============================================================================ identity
ID = "sturgeon"
MODEL = "legend_sturgeon"
ARMATURE = "Sturgeon"               # the FBX root node
PREFIX = "stur"
CM = (120, 280)                     # GameDatabase sturgeon 120..280 cm
STAGE = "ice"
BACKDROP = "ice"
PRESET = "ice"

# ============================================================================ palette (rollout 3.3)
OUTLINE = "#0b1322"
RAMPS = {
    "stur_back": ["#1a1e24", "#262c34", "#343c46"],
    "stur_side": ["#2e343a", "#444c54", "#5e6870"],
    "stur_belly": ["#8a8a7e", "#b4b2a4", "#dcd8c8"],
    "stur_scute": ["#6a6a60", "#a8a494", "#e0dccb"],
    "stur_fin": ["#1e2228", "#2e343c", "#424a54"],
    "stur_barbel": ["#8a7a78", "#b4a2a0", "#dccac6"],
    "stur_lips": ["#7a6a68", "#a8908c", "#d0b8b2"],
    "stur_mouth": ["#3a2424", "#5a3432", "#7a4a46"],
    "stur_eye_ring": ["#08090c", "#101218", "#1e222a"],
    "stur_eye_glow": ["#4a7a9a", "#9ad8ff", "#f0fbff"],
}
LURE_LIGHT = {"abyss": "#04080f", "fogOutline": "#2a4a6a", "eyeCore": "#f0fbff", "eyeGlow": "#9ad8ff",
              "frameLine": "#bfefff"}
FACE_BONES = ["Head", "Lips", "Jaw"]
RIG = {"protrude": [{"bone": "Lips", "move": [0, -0.030, 0.008], "tilt": [20, 0, 0]}],
       "limits": [{"bone": "Jaw", "max": 25}]}
BODY_GROUP = {"Lips"}               # check renders: no ink seam where the tube leaves the head

# ============================================================================ body
# profile (Unity model space, metres): z, top y, bottom y, half-width x. Rostrum tip z +0.50; deepest / widest at
# z ~ +0.09; the axis rises into the upper tail lobe from z -0.40 (heterocercal).
TABLE = [
    (0.500, 0.004, -0.003, 0.003), (0.485, 0.008, -0.009, 0.010), (0.465, 0.013, -0.015, 0.018),
    (0.440, 0.019, -0.021, 0.026), (0.410, 0.026, -0.027, 0.033), (0.380, 0.034, -0.033, 0.040),
    (0.345, 0.044, -0.038, 0.047), (0.305, 0.054, -0.044, 0.053), (0.265, 0.063, -0.050, 0.058),
    (0.220, 0.071, -0.056, 0.063), (0.160, 0.077, -0.061, 0.066), (0.090, 0.080, -0.063, 0.067),
    (0.020, 0.079, -0.062, 0.065), (-0.050, 0.075, -0.058, 0.061), (-0.120, 0.067, -0.051, 0.054),
    (-0.190, 0.057, -0.042, 0.045), (-0.250, 0.047, -0.033, 0.036), (-0.300, 0.039, -0.025, 0.028),
    (-0.345, 0.032, -0.017, 0.019), (-0.385, 0.029, -0.010, 0.013), (-0.420, 0.030, -0.001, 0.009),
    (-0.450, 0.036, 0.012, 0.006), (-0.470, 0.043, 0.025, 0.0042), (-0.487, 0.052, 0.042, 0.0025),
]
OVER = 0.015
INSET = 0.0015
BODY = K.Body(TABLE, ("stur_back", "stur_side", "stur_belly"), nseg=15, inset=INSET, over=OVER)

# pentagonal section: corners (ring parameter, degrees; 0 = the right flank, counter-clockwise seen from the nose)
LAT_R, DORSAL, LAT_L, VEN_L, VEN_R = 12.0, 90.0, 168.0, 232.0, 308.0
CORNERS = [LAT_R, DORSAL, LAT_L, VEN_L, VEN_R]
RING_DEG = [12, 38, 64, 90, 116, 142, 168, 189, 210, 232, 257, 283, 308, 329, 350]
# how pentagonal the section is along the body (0 = the plain ellipse; the rostrum is a flat rounded blade)
PENTA = [(0.50, 0.10), (0.42, 0.28), (0.32, 0.45), (0.20, 0.62), (-0.28, 0.62), (-0.40, 0.40), (-0.49, 0.20)]


def _lerp_table(tab, z):
    if z >= tab[0][0]:
        return tab[0][1]
    for (z0, a), (z1, b) in zip(tab, tab[1:]):
        if z1 <= z <= z0:
            return a + (b - a) * (z - z0) / (z1 - z0)
    return tab[-1][1]


def _rpoly(th):
    """Radius of the inscribed pentagon (unit circle, corners CORNERS) along the ray at th (radians)."""
    d = math.degrees(th) % 360.0
    cs = CORNERS + [CORNERS[0] + 360.0]
    if d < cs[0]:
        d += 360.0
    for a, b in zip(cs, cs[1:]):
        if a <= d <= b:
            p0 = (math.cos(math.radians(a)), math.sin(math.radians(a)))
            p1 = (math.cos(math.radians(b)), math.sin(math.radians(b)))
            e = (p1[0] - p0[0], p1[1] - p0[1])
            r = (math.cos(math.radians(d)), math.sin(math.radians(d)))
            den = r[0] * e[1] - r[1] * e[0]
            return (p0[0] * e[1] - p0[1] * e[0]) / den if abs(den) > 1e-9 else 1.0
    return 1.0


def spt(z, th, inset=0.0):
    """Surface point of the pentagonal body at (z, ring angle th radians)."""
    top, bot, hw = BODY.prof(z)
    yc = (top + bot) / 2
    ry = max(0.0015, (top - bot) / 2 - inset)
    rx = max(0.0015, hw - inset)
    r = 1.0 + (_rpoly(th) - 1.0) * _lerp_table(PENTA, z)
    return V((rx * r * math.cos(th), yc + ry * r * math.sin(th), z))


def snormal(z, th):
    p = spt(z, th)
    tz = spt(z + 0.002, th) - spt(z - 0.002, th)
    tt = spt(z, th + 0.01) - spt(z, th - 0.01)
    n = tt.cross(tz).normalized()
    top, bot, _ = BODY.prof(z)
    if n.dot(p - V((0, (top + bot) / 2, z))) < 0:
        n = -n
    return n


def ring(z, inset):
    return [spt(z, math.radians(a), inset) for a in RING_DEG]


def zone(deg):
    d = deg % 360.0
    if LAT_R <= d < LAT_L:
        return "stur_back"
    if VEN_L <= d < VEN_R:
        return "stur_belly"
    return "stur_side"


def ring_mat(i, k):
    a, b = RING_DEG[k], RING_DEG[(k + 1) % len(RING_DEG)]
    if b < a:
        b += 360
    return zone((a + b) / 2)


def yc(z):
    top, bot, _ = BODY.prof(z)
    return round((top + bot) / 2, 4)


def under_y(z, x=0.0):
    """y of the belly skin at (x, z) (on the flat underside)."""
    lo, hi = (270.0, 359.0) if x >= 0 else (181.0, 270.0)
    for _ in range(40):
        mid = (lo + hi) / 2
        px = spt(z, math.radians(mid)).x
        if (px < x) == (x >= 0):
            lo, hi = (mid, hi) if x >= 0 else (lo, mid)
        else:
            lo, hi = (lo, mid) if x >= 0 else (mid, hi)
    return spt(z, math.radians((lo + hi) / 2)).y


def _r(v):
    return tuple(round(float(c), 4) for c in v)


# ---------------------------------------------------------------- mouth tube (Lips), lower lip (Jaw), barbels
LIP_AX = V((0.0, -1.0, 0.36)).normalized()               # tube axis at rest (down, ~20 deg forward)
LIP_Z = 0.368
RIM0 = V((0.0, under_y(LIP_Z) - 0.003, LIP_Z))          # rest centre of the lip bulge / opening
LIP_P = RIM0 - LIP_AX * 0.016                           # Lips pivot (inside the head)
LIP_BACK = V((0.0, 0.0, -1.0)) - LIP_AX * LIP_AX.dot(V((0.0, 0.0, -1.0)))
LIP_BACK.normalize()                                    # across the tube towards the tail
JAW_H = RIM0 + LIP_BACK * 0.010 - LIP_AX * 0.004        # hinge of the rear lip (above / behind it)
BARB_Z = 0.425
BARBELS = [("Barbel.L1", -0.0075, 0.034, -6.0), ("Barbel.R1", 0.0075, 0.034, 6.0),
           ("Barbel.L2", -0.0195, 0.037, -15.0), ("Barbel.R2", 0.0195, 0.037, 15.0)]   # bone, x, length, splay
BARB_ROOT = {b: V((x, under_y(BARB_Z, x) + 0.0015, BARB_Z)) for b, x, _, _ in BARBELS}
PEC_B = {sg: spt(0.215, math.radians(-44.0 if sg > 0 else 224.0)) for sg in (1, -1)}
PEL_B = {sg: spt(-0.170, math.radians(VEN_R if sg > 0 else VEN_L)) for sg in (1, -1)}
EYE_TH = 33.0
EYE_Z = 0.338

# bones: (name, parent, head in Unity model space); rest rotation = identity for every bone
BONES = [
    ("Root", None, (0, 0, 0)),
    ("Spine.F", "Root", (0, yc(0.02), 0.02)),
    ("Head", "Spine.F", (0, yc(0.24), 0.24)),
    ("Lips", "Head", _r(LIP_P)),
    ("Jaw", "Lips", _r(JAW_H)),
    ("Barbel.L1", "Head", _r(BARB_ROOT["Barbel.L1"])),
    ("Barbel.R1", "Head", _r(BARB_ROOT["Barbel.R1"])),
    ("Barbel.L2", "Head", _r(BARB_ROOT["Barbel.L2"])),
    ("Barbel.R2", "Head", _r(BARB_ROOT["Barbel.R2"])),
    ("Pec.L", "Spine.F", _r(PEC_B[-1])),
    ("Pec.R", "Spine.F", _r(PEC_B[1])),
    ("Spine.B1", "Root", (0, yc(-0.02), -0.02)),
    ("Spine.B2", "Spine.B1", (0, yc(-0.12), -0.12)),
    ("Pel.L", "Spine.B2", _r(PEL_B[-1])),
    ("Pel.R", "Spine.B2", _r(PEL_B[1])),
    ("Spine.B3", "Spine.B2", (0, yc(-0.23), -0.23)),
    ("Dorsal2", "Spine.B3", (0, round(BODY.prof(-0.235)[0] - 0.004, 4), -0.235)),
    ("Anal", "Spine.B3", (0, round(BODY.prof(-0.258)[1] + 0.004, 4), -0.258)),
    ("Tail", "Spine.B3", (0, yc(-0.33), -0.33)),
    ("Tail.Upper", "Tail", (0, 0.026, -0.38)),
    ("Tail.Lower", "Tail", (0, -0.006, -0.395)),
]
# body segments: bone, nominal z range, inset extension at the rear / front (the FRONT segment runs OVER back)
SEGS = [("Tail", (-0.487, -0.33), False, False), ("Spine.B3", (-0.33, -0.23), True, False),
        ("Spine.B2", (-0.23, -0.12), True, False), ("Spine.B1", (-0.12, 0.0), True, False),
        ("Spine.F", (0.0, 0.24), True, False), ("Head", (0.24, 0.50), True, False)]
JOINTS = [-0.33, -0.23, -0.12, 0.0, 0.24]


def seg_of(z):
    return next(b for b, (z0, z1), _, _ in SEGS if z0 <= z < z1)


# ============================================================================ local geometry helpers
def pyramid(part, base, apex, mat):
    """A low pyramid (a bony scute / keel) on the skin: base = 4 points in order, apex; faces outward, flat."""
    vs = [part.bm.verts.new(p) for p in base]
    av = part.bm.verts.new(apex)
    cen = (sum(base, V()) + V(apex)) / 5.0
    for k in range(4):
        tri = [vs[k], vs[(k + 1) % 4], av]
        n = (tri[1].co - tri[0].co).cross(tri[2].co - tri[0].co)
        fc = (tri[0].co + tri[1].co + tri[2].co) / 3.0
        if n.dot(fc - cen) < 0:
            tri = tri[::-1]
        f = part.face(tri, mat, False)
        for e in f.edges:
            e.smooth = False


def scute(part, z, deg, hl, hw, h, mat="stur_scute", back=0.28, lift=0.0008):
    """A diamond scute centred on the skin at (z, ring angle deg): half length hl (along z), half width hw (across),
    keel height h, the apex pulled `back` * hl towards the tail (a thorn)."""
    th = math.radians(deg)
    c = spt(z, th)
    n = snormal(z, th)
    dsd = (spt(z, th + 0.01) - spt(z, th - 0.01)).length / 0.02       # skin metres per radian across
    dth = hw / max(1e-4, dsd)
    pts = [(z + hl, th), (z, th + dth), (z - hl, th), (z, th - dth)]
    base = [spt(zz, tt) + snormal(zz, tt) * lift for zz, tt in pts]
    t = (spt(z + 0.002, th) - spt(z - 0.002, th)).normalized()
    pyramid(part, base, c + n * h - t * (back * hl), mat)


def tube(part, pts, radii, mat, sides=4, cap_mat=None, spin=0.25):
    """A tapered tube (barbel) through the centre points pts with radii; square-ish section, closed."""
    rings = []
    for i, p in enumerate(pts):
        d = (V(pts[min(i + 1, len(pts) - 1)]) - V(pts[max(i - 1, 0)])).normalized()
        a, b, _ = K.frame_from(d, (1, 0, 0))
        rings.append([V(p) + (a * math.cos(2 * math.pi * (k + spin) / sides) +
                              b * math.sin(2 * math.pi * (k + spin) / sides)) * radii[i] for k in range(sides)])
    K.loft(part, rings, lambda i, k: mat, smooth=True, cap0=mat, cap1=cap_mat or mat)


def fin_plate(part, base, U, W0, outline, th, mat):
    """Planar fin: outline (u along the span U, w along the chord ~W0) from the base point."""
    U = V(U).normalized()
    W = V(W0) - U * U.dot(V(W0))
    W.normalize()
    n = U.cross(W).normalized()
    K.plate(part, [V(base) + U * u + W * w for u, w in outline], n, th, mat)


# ============================================================================ build
def build():
    """-> (parts {bone: Part}, empties {name: (parent bone, position U, facing U or None)}, info)."""
    rng = random.Random(11)
    parts = {}

    def P(name):
        if name not in parts:
            parts[name] = K.Part(name)
        return parts[name]

    # ------------------------------------------------ body segments (pentagonal loft), rear -> front
    bvh = {}
    for bone, (z0, z1), e0, e1 in SEGS:
        p = P(bone)
        rings = BODY.body_rings(z0, z1, e0, e1, ring)
        K.loft(p, rings, ring_mat, cap0="stur_side", cap1="stur_side")
        p.bm.normal_update()
        bvh[bone] = BVHTree.FromBMesh(p.bm)
    # ------------------------------------------------ gill slit: a dark crescent on each side of the head
    for sg in (1, -1):
        pts, ns = [], []
        for j in range(8):
            u = j / 7.0
            deg = -46.0 + 104.0 * u                          # from the belly corner up over the flank
            deg = deg if sg > 0 else 180.0 - deg
            z = 0.262 - 0.014 * math.sin(math.pi * u)        # the operculum edge bulges back at mid height
            pts.append(spt(z, math.radians(deg)))
            ns.append(snormal(z, math.radians(deg)))
        K.strip_decal(P("Head"), bvh["Head"], pts, ns, 0.0045, "stur_fin")
    # ------------------------------------------------ scutes: 5 rows on the pentagon's corners
    def placed(z, hl):
        for j in JOINTS:
            if abs(z - j) < hl + 0.004:
                z = j + math.copysign(hl + 0.004, z - j)
        return z
    counts = {"dorsal": 0, "lateral": 0, "ventral": 0, "head": 0}
    for i in range(12):                                            # dorsal keels (saw-tooth ridge)
        u = i / 11.0
        hl = 0.0145 - 0.0035 * u
        z = placed(0.207 - 0.420 * u, hl)
        scute(P(seg_of(z)), z, DORSAL, hl, 0.0085 - 0.002 * u, 0.0115 - 0.003 * u, back=0.35)
        counts["dorsal"] += 1
    for sg, deg in ((1, LAT_R), (-1, LAT_L)):                      # lateral rows (to the tail)
        for i in range(17):
            u = i / 16.0
            hl = 0.0115 - 0.0045 * u
            z = placed(0.200 - 0.600 * u, hl)
            scute(P(seg_of(z)), z, deg, hl, 0.0075 - 0.0025 * u, 0.0048 - 0.0018 * u)
            counts["lateral"] += 1
    for sg, deg in ((1, VEN_R), (-1, VEN_L)):                      # ventral rows (pectorals to pelvics)
        for i in range(9):
            u = i / 8.0
            hl = 0.0100 - 0.0015 * u
            z = placed(0.172 - 0.300 * u, hl)
            scute(P(seg_of(z)), z, deg, hl, 0.0065, 0.0036)
            counts["ventral"] += 1
    # head shields: low bony plates on the head roof
    for z, deg, hl, hw in ((0.302, 90.0, 0.016, 0.0095), (0.344, 90.0, 0.012, 0.0075),
                           (0.284, 64.0, 0.011, 0.0065), (0.284, 116.0, 0.011, 0.0065)):
        scute(P("Head"), z, deg, hl, hw, 0.0022, back=0.0)
        counts["head"] += 1
    # ------------------------------------------------ eyes: dark ring + pupil on the head, glow lens = geo_Eye.*
    empties = {}
    for side, sg in (("L", -1), ("R", 1)):
        deg = EYE_TH if sg > 0 else 180.0 - EYE_TH
        e = spt(EYE_Z, math.radians(deg))
        n = (snormal(EYE_Z, math.radians(deg)) + V((0, 0.10, 0.28))).normalized()   # out, a little up and forward
        a, b, n = K.frame_from(n, (0, 1, 0))
        K.ellipsoid(P("Head"), e + n * 0.0004, (0.0125, 0.0115, 0.0032), (a, b, n), "stur_eye_ring", 10, 4,
                    smooth=False)
        K.ellipsoid(P("Eye." + side), e + n * 0.0020, (0.0085, 0.0080, 0.0045), (a, b, n), "stur_eye_glow", 10, 4)
        K.ellipsoid(P("Head"), e + n * (0.0020 + 0.0043), (0.0036, 0.0030, 0.0011), (a, b, n), "stur_eye_ring",
                    6, 3, smooth=False)
        empties["Eye." + side] = ("Head", e, n)
    # ------------------------------------------------ mouth tube (Lips, retracted) + rear lip (Jaw) + Mouth empty
    ex = V((1.0, 0.0, 0.0))
    lp = P("Lips")
    rings = []
    for t, rx, rz in ((-0.034, 0.0095, 0.0075), (-0.012, 0.0115, 0.0090), (0.004, 0.0125, 0.0100),
                      (0.0125, 0.0160, 0.0130), (0.0185, 0.0172, 0.0142), (0.0225, 0.0150, 0.0122),
                      (0.0205, 0.0095, 0.0072)):                  # tube -> fleshy lip roll -> the recessed opening
        c = LIP_P + LIP_AX * t
        rings.append([c + ex * (rx * math.cos(2 * math.pi * k / 10)) + LIP_BACK * (rz * math.sin(2 * math.pi * k / 10))
                      for k in range(10)])
    K.loft(lp, rings, lambda i, k: "stur_mouth" if i == len(rings) - 2 else "stur_lips", smooth=True,
           cap0="stur_lips", cap1="stur_mouth")                  # the inner face of the lip roll is the dark mouth
    jc = RIM0 + LIP_AX * 0.0062 + LIP_BACK * 0.0052
    K.ellipsoid(P("Jaw"), jc, (0.0128, 0.0058, 0.0032), (ex, LIP_BACK, LIP_AX), "stur_lips", 8, 4)
    empties["Mouth"] = ("Lips", RIM0 + LIP_AX * 0.010, None)
    # ------------------------------------------------ barbels: 4 in a row under the snout, hanging, raked forward
    for bone, x, ln, splay in BARBELS:
        root = BARB_ROOT[bone] + V((0, 0.003, 0))                    # start inside the snout
        pts, p = [root], V(BARB_ROOT[bone])
        pts.append(p.copy())
        n_seg = 4
        for j in range(n_seg):
            u = (j + 0.5) / n_seg
            rake = math.radians(18.0 * (1 - u) + 2.0 * u)            # forward rake straightening towards the tip
            sp = math.radians(splay * (0.4 + 0.6 * u))
            d = V((math.sin(sp) * math.cos(rake), -math.cos(rake), math.sin(rake))).normalized()
            p = p + d * (ln / n_seg)
            pts.append(p.copy())
        radii = [0.0027, 0.0026, 0.0023, 0.0019, 0.0015, 0.0010]
        tube(P(bone), pts, radii, "stur_barbel")
    # ------------------------------------------------ fins
    for sg in (1, -1):
        fin_plate(P("Pec.R" if sg > 0 else "Pec.L"), PEC_B[sg], (sg * 0.85, -0.50, -0.32), (0, 0, 1),
                  [(-0.005, 0.016), (0.029, 0.010), (0.055, -0.003), (0.075, -0.023), (0.081, -0.038),
                   (0.072, -0.047), (0.048, -0.041), (0.020, -0.032), (-0.005, -0.026)], 0.0045, "stur_fin")
        fin_plate(P("Pel.R" if sg > 0 else "Pel.L"), PEL_B[sg], (sg * 0.7, -0.62, -0.45), (0, 0, 1),
                  [(-0.003, 0.011), (0.018, 0.004), (0.032, -0.011), (0.030, -0.022), (0.014, -0.019),
                   (-0.003, -0.013)], 0.0035, "stur_fin")
    t0 = BODY.prof(-0.228)[0]
    t1 = BODY.prof(-0.318)[0]
    K.plate(P("Dorsal2"), [V((0, y, z)) for z, y in
                           ((-0.226, t0 - 0.006), (-0.236, t0 + 0.004), (-0.256, 0.068), (-0.275, 0.088),
                            (-0.286, 0.089), (-0.289, 0.078), (-0.296, 0.061), (-0.307, 0.048), (-0.320, t1 + 0.001),
                            (-0.320, t1 - 0.007))], (1, 0, 0), 0.0035, "stur_fin")
    b0 = BODY.prof(-0.252)[1]
    b1 = BODY.prof(-0.318)[1]
    K.plate(P("Anal"), [V((0, y, z)) for z, y in
                        ((-0.252, b0 + 0.006), (-0.262, b0 - 0.004), (-0.280, -0.050), (-0.294, -0.063),
                         (-0.302, -0.060), (-0.305, -0.046), (-0.312, -0.031), (-0.320, b1 - 0.001),
                         (-0.320, b1 + 0.007))], (1, 0, 0), 0.0035, "stur_fin")
    # caudal: the long upper lobe (the body axis runs up into it) and the short lower lobe
    K.plate(P("Tail.Upper"), [V((0, y, z)) for z, y in
                              ((-0.370, 0.021), (-0.382, 0.032), (-0.410, 0.037), (-0.440, 0.046), (-0.470, 0.061),
                               (-0.500, 0.081), (-0.494, 0.068), (-0.479, 0.050), (-0.464, 0.034), (-0.452, 0.019),
                               (-0.442, 0.009), (-0.420, 0.006))], (1, 0, 0), 0.0035, "stur_fin")
    K.plate(P("Tail.Lower"), [V((0, y, z)) for z, y in
                              ((-0.392, -0.002), (-0.398, -0.011), (-0.412, -0.022), (-0.428, -0.036),
                               (-0.442, -0.047), (-0.448, -0.044), (-0.448, -0.030), (-0.446, -0.015),
                               (-0.444, -0.003), (-0.430, 0.004))], (1, 0, 0), 0.0035, "stur_fin")
    info = {"scutes": counts, "section": "pentagon corners (deg) %s" % CORNERS,
            "mouth": {"rimRest": _r(RIM0), "axis": _r(LIP_AX), "lipsPivot": _r(LIP_P), "jawHinge": _r(JAW_H)},
            "barbels": {b: {"root": _r(BARB_ROOT[b]), "len": ln} for b, _, ln, _ in BARBELS},
            "jointsZ": JOINTS}
    return parts, empties, info


# ============================================================================ check renders (hyb_legend3d.check)
BITE = {"Lips": {"rot": (20, 0, 0), "move": (0, -0.030, 0.008)}, "Jaw": (25, 0, 0),
        "Barbel.L1": (25, 0, 0), "Barbel.R1": (25, 0, 0), "Barbel.L2": (25, 0, 0), "Barbel.R2": (25, 0, 0)}
CHECK_OPEN = dict(BITE, **{"Pec.L": (0, -20, 0), "Pec.R": (0, 20, 0)})
CHECK_BEND = {"Spine.F": (0, -6, 0), "Head": (0, -12, 0), "Spine.B1": (0, 10, 0), "Spine.B2": (0, 15, 0),
              "Spine.B3": (0, 15, 0), "Tail": (0, 15, 0), "Tail.Upper": (0, 8, 0), "Tail.Lower": (0, 8, 0)}

# ============================================================================ review mock (hyb_legend_preview.py)
PREVIEW_LURE = "softworm"
PREVIEW_CM = 200
LURE_R = 3.0
TURNTABLE = dict(
    open=dict(BITE, **{"Head": (10, 0, 0), "Pec.L": (0, -20, 0), "Pec.R": (0, 20, 0), "Dorsal2": (0, 0, 10),
                       "Anal": (0, 0, -10)}),
    bend={"Spine.F": (0, -2, 0), "Head": (0, -8, 0), "Spine.B1": (0, 6, 0), "Spine.B2": (0, 10, 0),
          "Spine.B3": (0, 13, 0), "Tail": (0, 14, 0), "Tail.Upper": (0, 6, 0), "Tail.Lower": (0, 6, 0),
          "Pec.L": (0, -15, 0), "Pec.R": (0, 15, 0)},
    lit={"Pec.L": (0, -10, 0), "Pec.R": (0, 10, 0)},
    game={},
)


LIGHT_UP = 1.2          # ice set: the light is centred 1.2 m above the lure, in the hole's light column (rollout 4.1)
HOLE_Y = 4.3            # the ice ceiling / the hole above the lure (set frame)
VEIL = ("#02040a", 0.5)  # ice set: the back layers are veiled during Eyes


def preview_beats(M, sc, cam, lure, surface):
    """The mock beats (rollout 3.3 choreography, 6 capture moments) -> [(name, 480x270 frame)]:
    eyes (two small pale eyes at floor level in the dark under the ice) -> approach (a straight slow line along the
    floor, a silhouette with the set's rim) -> pass (the curious lap through the light column, the scutes gleaming)
    -> excited (hovering over the worm, barbels curled onto it) -> bite_full (full screen: the mouth tube dropped onto
    the worm, head pitched down, silt cloud)."""
    rng = random.Random(5)
    frames = []
    s = sc.cm / 100.0
    LG = M.LG
    light_up = M.PROF.get("light_up", LIGHT_UP)
    hole_y = M.PROF.get("ceiling_y", HOLE_Y)
    veil_c, veil_a = M.PROF.get("veil", VEIL)
    mouth_ob = next(o for o in sc.objs if LG.base_name(o.name) == "Mouth")
    silt = M.PROF.get("silt", "#8aa0b8")

    def fwd_of(h):
        return V((math.sin(math.radians(h)), 0.0, math.cos(math.radians(h))))

    def lit(p):
        return V(p) + V((0.0, light_up, 0.0))

    def backdrop(c, cpos, lure_px, veil=False):
        hy = M.project(c, V((cpos.x, 0.0, cpos.z + 30.0)))[1]                  # the floor horizon
        cy = M.project(c, V((cpos.x, hole_y, cpos.z + 30.0)))[1]               # the ice ceiling's far edge
        rays = M.PROF.get("rays")
        if rays and M.PROF.get("ray_anchor") is not None:                     # the hole's column, anchored per camera
            hx, hy2, _ = M.project(c, V(M.PROF["ray_anchor"]))
            M.PROF["rays"] = [(t, a, int(round(M.VW - (hx - 32))), int(round(hy2))) for t, a, _, _ in rays]
        try:
            under = M.back_layers(lure_px, hy, ceil_row=cy)
        finally:
            M.PROF["rays"] = rays
        if veil:
            under[..., :3] = under[..., :3] * (1 - veil_a) + M.R.hexrgb(veil_c) * veil_a
        return under

    def puffs(img, at, n, k=1, spread=(10, 5)):
        for i in range(n):
            sp = M.tint(M.load("silt_%d" % (i % 4)), silt, 0.9)
            if k > 1:
                sp = M.R.upscale(sp, k)
            x = at[0] + rng.randint(-spread[0], spread[0]) - sp.shape[1] // 2
            y = at[1] + rng.randint(-spread[1], spread[1] // 2) - sp.shape[0] // 2
            M.R.over(img, sp, int(x), int(y))

    swim = dict(Spine_F=(0, -1, 0), Head=(0, -3, 0), Spine_B1=(0, 3, 0), Spine_B2=(0, 6, 0), Spine_B3=(0, 9, 0),
                Tail=(0, 13, 0), Tail_Upper=(0, 6, 0), Tail_Lower=(0, 6, 0), Pec_L=(0, -8, 0), Pec_R=(0, 8, 0),
                Dorsal2=(0, 0, 12), Anal=(0, 0, -12))
    lx, ly, _ = M.project(cam, lure)
    up = M.project(cam, lure + V(M.SETP.line_up))[:2]
    cpos = V(M.CAM)
    print("MOCK stur lure px", round(lx, 1), round(ly, 1))

    # 1) eyes: two small pale eyes at floor level in the far dark, drifting in towards the worm (deepDir (0.7, 0, 0.7))
    eyes_at = V((4.4, 0.2, 6.0))
    hdg = math.degrees(math.atan2(-eyes_at.x, -eyes_at.z)) + 6
    root = eyes_at - fwd_of(hdg) * (EYE_Z * s) - V((0, 0.024 * s, 0))
    sc.place(root, hdg)
    sc.pose(**swim)
    fish = M.render_fish(sc, cam, lit(lure), "s1", rim=False, eyes_only=True)
    under = backdrop(cam, cpos, (lx, ly), veil=True)
    M.R.over(under, fish, 0, 0)
    under = M.front_fx(under, (lx, ly), up, rng, snow=10)
    M.put_lure(under, (lx, ly), PREVIEW_LURE, 0)
    M.put_eyeshine(under, sc.eyes_px(cam))
    frames.append(("eyes", M.compose_window(*surface, under, M.WIN)))

    def windowed(tag, lure_u, lure_frame=0, eyes_mult=1.0, fish_over_lure=True, bubbles=0, silt_n=0):
        lpx = M.project(cam, lure_u)[:2]
        fish = M.render_fish(sc, cam, lit(lure_u), tag)
        under = backdrop(cam, cpos, lpx)
        if fish_over_lure:
            M.put_lure(under, lpx, PREVIEW_LURE, lure_frame)
            puffs(under, (int(lpx[0]), int(lpx[1]) + 2), silt_n)
        under = M.front_fx(under, lpx, up, rng, snow=12, bubbles=bubbles)
        M.R.over(under, fish, 0, 0)
        if not fish_over_lure:
            M.put_lure(under, lpx, PREVIEW_LURE, lure_frame)
        M.put_eyeshine(under, sc.eyes_px(cam), eyes_mult)
        return M.compose_window(*surface, under, M.WIN)

    # 2) approach: a straight, slow line along the floor out of the dark - a silhouette with the ice rim, barbels
    #    trailing, outside the light column (the lure light is centred 1.2 m above the worm)
    pos = V((1.75, 0.15, 2.15))
    sc.place(pos, math.degrees(math.atan2(-pos.x, -pos.z)) + 14)
    sc.pose(**swim)
    frames.append(("approach", windowed("s2", lure, lure_frame=1, eyes_mult=0.7)))
    # 3) pass (curious, 1.4 m): the lap through the light column behind the worm, side-on - the scute rows gleam in
    #    the column, the snout sweeping the bottom
    pos = V((0.35, 0.15, 1.25))
    sc.place(pos, -72.0)
    sc.pose(**dict(swim, Head=(4, 9, 0), Spine_F=(0, 2, 0), Barbel_L1=(10, 0, 0), Barbel_R1=(10, 0, 0),
                   Barbel_L2=(10, 0, 0), Barbel_R2=(10, 0, 0)))
    frames.append(("pass", windowed("s3", lure, lure_frame=0, silt_n=2)))
    # 4) excited: glides in and stops over the worm, pectorals planing, barbels curled onto it (barbel 1 = X +25)
    hdg = -86.0                                                     # from the deep side, side-on to the camera
    f = fwd_of(hdg)
    tip = lure + V((0.0, 0.17, 0.0)) - f * 0.22                     # the snout tip ~0.28 m above / behind the worm
    sc.place(tip - f * (0.5 * s), hdg, bank=0)
    sc.pose(Head=(6, 0, 0), Spine_B1=(0, 2, 0), Spine_B2=(0, 3, 0), Spine_B3=(0, 4, 0), Tail=(0, -5, 0),
            Pec_L=(0, -28, 0), Pec_R=(0, 28, 0), Pel_L=(0, 15, 0), Pel_R=(0, -15, 0),
            Barbel_L1=(25, 0, 0), Barbel_R1=(25, 0, 0), Barbel_L2=(25, 0, 0), Barbel_R2=(25, 0, 0),
            Lips={"rot": (4, 0, 0), "move": (0, -0.006, 0.0016)})
    frames.append(("excited", windowed("s4", lure, lure_frame=1)))
    # 5) full screen at the bite: vacuum from below - the tube drops onto the worm (Lips protrude 1), head pitched
    #    down 10, jaw open, a big silt cloud; camera dollied low onto the head, f x1.5
    cp = V((-0.62, 0.12, -1.02))
    c5 = M.setcam(pos=cp, target=V((0.30, 0.10, 0.08)), f=M.FPX * 1.5)
    hdg = -98.0
    worm = V((0.0, 0.03, 0.0))
    sc.place(V((0.0, 0.2, 0.0)), hdg, bank=-3)
    sc.pose(Head=(10, 2, 0), Spine_F=(2, 0, 0), Spine_B1=(0, 4, 0), Spine_B2=(0, 7, 0), Spine_B3=(0, 9, 0),
            Tail=(0, 12, 0), Tail_Upper=(0, 6, 0), Tail_Lower=(0, 6, 0), Pec_L=(0, -30, 0), Pec_R=(0, 30, 0),
            Pel_L=(0, 20, 0), Pel_R=(0, -20, 0), Jaw=(25, 0, 0),
            Lips={"rot": (20, 0, 0), "move": (0, -0.030, 0.008)},
            Barbel_L1=(25, 0, 0), Barbel_R1=(25, 0, 0), Barbel_L2=(25, 0, 0), Barbel_R2=(25, 0, 0))
    m_u = LG.u_of_w(mouth_ob.matrix_world.translation)
    sc.place(V((0.0, 0.2, 0.0)) + (worm - m_u), hdg, bank=-3)            # the mouth opening onto the worm
    wx, wy, _ = M.project(c5, worm)
    fish = M.render_fish(sc, c5, lit(worm), "s5")
    under = backdrop(c5, cp, (wx, wy))
    puffs(under, (int(wx), int(wy) + 4), 6, k=2, spread=(26, 8))
    M.put_lure(under, (wx, wy + 1), PREVIEW_LURE, 1, scale=2)
    M.R.over(under, fish, 0, 0)
    under = M.front_fx(under, None, None, rng, snow=16)
    for i in range(8):                                                   # silt specks sucked up into the tube
        a = i / 8 * 6.28
        x, y = int(wx + math.cos(a) * (12 + i * 2)), int(wy + 3 + math.sin(a) * (5 + i))
        if 0 <= x < M.VW and 0 <= y < M.VH:
            under[y, x, :3] = M.R.hexrgb(M.PROF.get("snow_near", "#cfe8ff"))
    M.put_eyeshine(under, sc.eyes_px(c5))
    frames.append(("bite_full", M.compose_window(*surface, under, (0, 0, M.VW, M.VH), frame=False)))
    return frames
