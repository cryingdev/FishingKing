"""
Legend module: 황금잉어 GOLDEN CARP (lake, backdrop "lake"), the bait-eater. Loaded by hyb_legend3d.py (model) and
hyb_legend_preview.py (review mock). Design row: Docs/legends_rollout.md 3.1 (model, palette, rig, choreography),
4.2 (the lake set), 7 (species bones).

A deep-bodied golden carp (depth ~0.30 L, humped just behind the head): big scales as V-arc edge decals in 5 staggered
rows, a gill-cover edge, one long dorsal (the raisable front lobe Dorsal1, modelled RAISED and folded -40 deg to rest,
plus a fixed low fringe on the back segments that follows the spine), a forked tail, pectoral / pelvic / anal fins, a
protrusible lip tube `Lips` (the upper lip; the lower lip `Jaw` and the `Mouth` empty ride it) and two barbel pairs on
the lips (1 = short, upper lip; 2 = long, mouth corners).
"""
import math
import os
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
import hyb_legend_kit as K

V = K.V

# ============================================================================ identity
ID = "golden_carp"
MODEL = "legend_golden_carp"
ARMATURE = "GoldenCarp"              # the FBX root node
PREFIX = "gcarp"                     # every material is gcarp_* (ActorArt caches materials globally by name)
CM = (50, 100)                       # in-game length range (GameDatabase), runtime scale = cm / 100
STAGE = "lake"                       # surface stage
BACKDROP = "lake"                    # EncounterDef.backdrop = encounter_sets/lake.py
PRESET = "lake"                      # hyb_core preset used while building / checking

# ============================================================================ palette (rollout 3.1)
OUTLINE = "#1c1208"                  # a warm dark-brown ink (reads better on gold than the coelacanth's navy)
RAMPS = {
    "gcarp_back": ["#8a5a12", "#b8801a", "#d8a42a"],
    "gcarp_side": ["#b8801a", "#e0aa2a", "#ffd24a"],
    "gcarp_belly": ["#c89a4a", "#ecc878", "#fff0b8"],
    "gcarp_scale": ["#6a4210", "#8a5a18", "#b07a24"],
    "gcarp_fin": ["#a8601a", "#d88a2a", "#f4b04a"],
    "gcarp_lips": ["#b86a3a", "#e0946a", "#f8c0a0"],
    "gcarp_barbel": ["#a86a3a", "#d0946a", "#f0c09a"],
    "gcarp_mouth": ["#4a1a1a", "#7a2e2e", "#a04a44"],
    "gcarp_eye_ring": ["#1a0e04", "#2a1a08", "#4a3010"],
    "gcarp_eye_glow": ["#a07a20", "#ffc830", "#fff6d0"],
}
# lure-light constants of the lake set (rollout 4.1: abyss = the water colour, fog outline) and the gold eyeshine
LURE_LIGHT = {"abyss": "#1a3024", "fogOutline": "#3a5a3a", "eyeCore": "#fff6d0", "eyeGlow": "#ffc830",
              "frameLine": "#a8e878"}
FACE_BONES = ["Head", "Lips", "Jaw"]
RIG = {"protrude": [{"bone": "Lips", "move": [0, -0.010, 0.030], "tilt": [12, 0, 0]}],
       "limits": [{"bone": "Jaw", "max": 30}, {"bone": "Dorsal1", "max": 40}]}
BODY_GROUP = {"Lips"}                # the lip tube reads as one skin with the head in the check renders

# ============================================================================ body
# profile (Unity model space, metres): z, top y, bottom y, half-width x, snout (z 0.478, the lips sit in front of it)
# -> caudal peduncle (z -0.345). Deepest at z ~0.09 (0.297 = 0.30 L), the nape rises into the hump behind the head.
GC_BODY = [
    (0.478, 0.006, -0.030, 0.016), (0.472, 0.018, -0.041, 0.024), (0.460, 0.029, -0.053, 0.032),
    (0.440, 0.039, -0.066, 0.040), (0.410, 0.049, -0.080, 0.047), (0.370, 0.060, -0.094, 0.053),
    (0.320, 0.076, -0.107, 0.058), (0.270, 0.103, -0.118, 0.062), (0.210, 0.134, -0.129, 0.065),
    (0.150, 0.151, -0.137, 0.066), (0.090, 0.157, -0.140, 0.066), (0.030, 0.153, -0.139, 0.064),
    (-0.030, 0.143, -0.133, 0.060), (-0.090, 0.129, -0.122, 0.054), (-0.150, 0.110, -0.106, 0.046),
    (-0.200, 0.090, -0.087, 0.037), (-0.245, 0.072, -0.069, 0.029), (-0.285, 0.058, -0.055, 0.022),
    (-0.315, 0.050, -0.047, 0.018), (-0.345, 0.046, -0.043, 0.015),
]
OVER = 0.015
INSET = 0.0015
BODY = K.Body(GC_BODY, ("gcarp_back", "gcarp_side", "gcarp_belly"), nseg=16, back_pinch=0.10, inset=INSET,
              over=OVER, mouth=None, zones=(0.85, 0.15))
# the lip tube (Lips = upper half, Jaw = lower half, split on the horizontal mouth line y = MOUTH_Y); its rear runs
# 5 cm back inside the snout so the 3 cm protrusion never opens a gap
MOUTH_Y = -0.012
LIP_T = [
    (0.500, -0.003, -0.021, 0.013), (0.497, 0.004, -0.028, 0.018), (0.490, 0.008, -0.032, 0.021),
    (0.480, 0.009, -0.033, 0.022), (0.468, 0.007, -0.031, 0.020), (0.450, 0.003, -0.027, 0.017),
    (0.420, 0.000, -0.024, 0.014),
]
LIPB = K.Body(LIP_T, ("gcarp_lips", "gcarp_lips", "gcarp_lips"), nseg=12, back_pinch=0.0, inset=0.0, over=0.0,
              mouth=((0.42, MOUTH_Y), (0.50, MOUTH_Y)), n_arc=7, n_chord=1)


def _r4(v):
    return tuple(round(float(x), 4) for x in v)


def top_y(z):
    return BODY.prof(z)[0]


def bot_y(z):
    return BODY.prof(z)[1]


# attachment points (right side; the left side mirrors x)
PEC_BASE = _r4(BODY.pt(0.276, math.radians(-52)))
PEL_BASE = _r4(BODY.pt(0.060, math.radians(-70)))
BARB1_TH, BARB1_Z = math.radians(22), 0.488          # short pair: the upper lip, low on its side
BARB2_TH, BARB2_Z = math.radians(2), 0.484           # long pair: the mouth corners (on the exposed lip roll)
BARB1_BASE = _r4(LIPB.pt(BARB1_Z, BARB1_TH))
BARB2_BASE = _r4(LIPB.pt(BARB2_Z, BARB2_TH))
DORSAL1_HEAD = (0.0, round(top_y(0.128) - 0.005, 4), 0.128)
ANAL_HEAD = (0.0, round(bot_y(-0.140) + 0.005, 4), -0.140)
DORSAL_FOLD = 40.0                   # Dorsal1 modelled raised, folded -40 deg about X to rest (Unity +40 raises it)


def _mx(p, sg):
    return (sg * p[0], p[1], p[2])


# bones: (name, parent, head in Unity model space). Rest rotation = identity for every bone.
BONES = [
    ("Root", None, (0, 0, 0)),
    ("Spine.F", "Root", (0, 0, 0.04)),
    ("Head", "Spine.F", (0, 0, 0.27)),
    ("Lips", "Head", (0, MOUTH_Y, 0.455)),
    ("Jaw", "Lips", (0, MOUTH_Y - 0.002, 0.462)),
    ("Barbel.L1", "Lips", _mx(BARB1_BASE, -1)),
    ("Barbel.R1", "Lips", _mx(BARB1_BASE, 1)),
    ("Barbel.L2", "Lips", _mx(BARB2_BASE, -1)),
    ("Barbel.R2", "Lips", _mx(BARB2_BASE, 1)),
    ("Pec.L", "Head", _mx(PEC_BASE, -1)),
    ("Pec.R", "Head", _mx(PEC_BASE, 1)),
    ("Dorsal1", "Spine.F", DORSAL1_HEAD),
    ("Pel.L", "Spine.F", _mx(PEL_BASE, -1)),
    ("Pel.R", "Spine.F", _mx(PEL_BASE, 1)),
    ("Spine.B1", "Root", (0, 0, -0.03)),
    ("Spine.B2", "Spine.B1", (0, 0, -0.135)),
    ("Anal", "Spine.B2", ANAL_HEAD),
    ("Spine.B3", "Spine.B2", (0, 0, -0.235)),
    ("Tail", "Spine.B3", (0, 0, -0.30)),
    ("Tail.Upper", "Tail", (0, 0.02, -0.325)),
    ("Tail.Lower", "Tail", (0, -0.02, -0.325)),
]
EYE_Z, EYE_Y = 0.395, 0.022          # eye centre (on the head surface), Eye.L at -x
EYE_RING_R, EYE_GLOW_R = 0.020, 0.0155

# body segments: bone, nominal z range (rear, front), inset extension at the rear (the FRONT segment runs OVER the joint)
SEGS = [
    ("Tail", (-0.345, -0.30), False),
    ("Spine.B3", (-0.30, -0.235), True),
    ("Spine.B2", (-0.235, -0.135), True),
    ("Spine.B1", (-0.135, 0.0), True),
    ("Spine.F", (0.0, 0.27), True),
    ("Head", (0.27, 0.478), True),
]
# dorsal: (z, height above the back line); a negative height = inside the body (the embedded base)
DORSAL_LOBE = [(0.132, -0.007), (0.131, 0.030), (0.128, 0.070), (0.123, 0.105), (0.116, 0.126), (0.107, 0.132),
               (0.098, 0.124), (0.086, 0.100), (0.070, 0.078), (0.052, 0.063), (0.032, 0.053), (0.014, 0.047),
               (0.006, -0.007)]
DORSAL_FRINGE = {   # the low rear rays, fixed on the back segments (they bend with the spine)
    "Spine.B1": [(0.0, -0.007), (0.0, 0.046), (-0.03, 0.044), (-0.06, 0.041), (-0.09, 0.038), (-0.12, 0.035),
                 (-0.147, 0.032), (-0.147, -0.007)],
    "Spine.B2": [(-0.135, -0.007), (-0.135, 0.033), (-0.160, 0.030), (-0.185, 0.027), (-0.205, 0.023),
                 (-0.222, 0.017), (-0.227, 0.010), (-0.219, -0.005)],
}
# anal: (z, depth below the belly line); negative = inside
ANAL = [(-0.136, -0.008), (-0.140, 0.010), (-0.150, 0.034), (-0.163, 0.054), (-0.176, 0.066), (-0.187, 0.068),
        (-0.192, 0.058), (-0.196, 0.040), (-0.203, 0.018), (-0.213, 0.004), (-0.208, -0.006)]
# caudal: upper lobe (z, y), the lower lobe mirrors it (x 0.96); forked, the notch at z -0.432
TAIL_UP = [(-0.310, 0.028), (-0.325, 0.046), (-0.350, 0.070), (-0.385, 0.098), (-0.425, 0.122), (-0.462, 0.138),
           (-0.488, 0.146), (-0.500, 0.144), (-0.495, 0.128), (-0.477, 0.100), (-0.459, 0.066), (-0.443, 0.032),
           (-0.432, 0.006), (-0.380, 0.003), (-0.314, 0.005)]
# paired fins: blade outline (u along the fin, w across it; w > 0 = the leading / upper edge)
PEC_SHAPE = [(-0.004, -0.010), (0.025, -0.020), (0.060, -0.024), (0.088, -0.018), (0.100, -0.004), (0.096, 0.010),
             (0.075, 0.020), (0.040, 0.021), (0.010, 0.013), (-0.004, 0.006)]
PEL_SHAPE = [(-0.004, -0.008), (0.030, -0.016), (0.062, -0.017), (0.080, -0.006), (0.078, 0.008), (0.055, 0.016),
             (0.020, 0.013), (-0.004, 0.006)]
SCALE_ROWS = (-38.0, -13.0, 12.0, 37.0, 61.0)       # row centres (deg on the section; 0 = the flank's mid line)
SCALE_ROW_D = 25.0                                  # row height (deg); odd columns are shifted by half a row


def split_on_joints(pts, ns):
    """Cut a skin polyline (points ordered along it) where it crosses a spine joint -> [(segment bone, pts, ns)], so
    every piece lies on ONE rigid part (a piece on the wrong side of a joint would sink under the neighbour's skin)."""
    def seg_of(z):
        return next(b for b, (lo, hi), _ in SEGS if lo <= z < hi or (b == "Head" and z >= lo))
    pieces = []
    cur_b, cur_p, cur_n = seg_of(pts[0].z), [pts[0]], [ns[0]]
    for a, b, na, nb in zip(pts, pts[1:], ns, ns[1:]):
        sb = seg_of(b.z)
        if sb != cur_b:
            jz = next(lo for bb, (lo, hi), _ in SEGS if bb == (cur_b if b.z < a.z else sb))
            t = (jz - a.z) / (b.z - a.z)
            q, nq = a.lerp(b, t), na.lerp(nb, t).normalized()
            cur_p.append(q)
            cur_n.append(nq)
            pieces.append((cur_b, cur_p, cur_n))
            cur_b, cur_p, cur_n = sb, [q], [nq]
        cur_p.append(b)
        cur_n.append(nb)
    pieces.append((cur_b, cur_p, cur_n))
    return [p for p in pieces if len(p[1]) >= 2 and (p[1][0] - p[1][-1]).length > 0.003]


def fin_blade(part, base, d, nb, shape, k, mat, sg, th=0.0035):
    """Flat paddle fin: shape (u, w) laid out from base along d; blade normal nb. Mirrored exactly for the left fin."""
    base, d = V(base), V(d).normalized()
    nb = V(nb)
    nb = (nb - d * nb.dot(d)).normalized()
    e = nb.cross(d).normalized() * sg
    pts = [base + d * (u * k) + e * (w * k) for u, w in shape]
    K.plate(part, pts, nb, th, mat)


def barbel(part, base, d, length, r0, mat, droop=0.28, n=3):
    """A tapered 4-sided tube from base, trailing along d and drooping a little more with each segment."""
    pts = [V(base)]
    d = V(d).normalized()
    for i in range(n):
        di = (d + V((0, -droop * (i + 1) / n, 0))).normalized()
        pts.append(pts[-1] + di * (length / n))
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n)] - pts[max(i - 1, 0)]).normalized()
        a, b, _ = K.frame_from(t, (0, 1, 0))
        r = r0 * (1.0 - 0.72 * i / n)
        rings.append([p + (a * math.cos(q) + b * math.sin(q)) * r for q in (0.785, 2.356, 3.927, 5.498)])
    K.loft(part, rings, lambda i, k: mat, smooth=True, cap0=mat, cap1=mat)


def build():
    """-> (parts {bone: Part}, empties {name: (parent bone, position U, facing U or None)}, info)."""
    B = BODY
    parts = {}

    def P(name):
        if name not in parts:
            parts[name] = K.Part(name)
        return parts[name]

    # ------------------------------------------------ body segments (rear -> front); the head keeps its rounded snout
    bvh = {}
    for bone, (z0, z1), e0 in SEGS:
        p = P(bone)
        if bone == "Head":
            rings = [B.full_ring(z0 - OVER, INSET), B.full_ring(z0 - 0.001, INSET)]
            rings += [B.full_ring(z, 0.0) for z in B.stations(z0, z1, 0.05, 0.004)]
        else:
            rings = B.body_rings(z0, z1, e0, False, B.full_ring)
        K.loft(p, rings, B.full_mat, cap0="gcarp_side", cap1="gcarp_lips" if bone == "Head" else "gcarp_side")
        p.bm.normal_update()
        bvh[bone] = (BVHTree.FromBMesh(p.bm), (z0, z1))
    # ------------------------------------------------ gill-cover edge: a curved band, bulging back at mid height
    for sg in (1, -1):
        pts, ns = [], []
        for j in range(9):
            u = -1 + 2 * j / 8                                 # -1 = the throat .. +1 = the nape
            thd = math.radians(-62 + 120 * j / 8)
            th = thd if sg > 0 else math.pi - thd
            z = 0.289 + 0.015 * u * u + 0.008 * max(0.0, -u)
            pts.append(B.pt(z, th))
            ns.append(B.normal(z, th))
        K.strip_decal(P("Head"), bvh["Head"][0], pts, ns, 0.005, "gcarp_scale")
    # ------------------------------------------------ scales: the exposed rear edge of each big scale as a U-arc
    # bulging towards the tail, imbricate: columns along the body, 5 rows per column, odd columns shifted half a row;
    # an arc that crosses a spine joint is cut into pieces, each on its own segment
    n_arcs = 0
    hr = math.radians(SCALE_ROW_D / 2)
    col, z0 = 0, 0.252
    while z0 > -0.29:
        s = min(1.0, max(0.4, B.sec(z0)[1] / 0.148))
        step = 0.050 * (0.55 + 0.45 * s)
        dz = 0.80 * step
        width = 0.0045 * (0.6 + 0.4 * s)
        for thd in SCALE_ROWS:
            th0 = math.radians(thd) + (hr if col % 2 else 0.0)
            if th0 > math.radians(66):
                continue                                         # keep clear of the dorsal base
            for sg in (1, -1):
                pts, ns = [], []
                for phi in (0.0, 60.0, 120.0, 180.0):
                    ph = math.radians(phi)
                    z = z0 - dz * math.sin(ph)
                    t = th0 + 0.92 * hr * math.cos(ph)
                    t = t if sg > 0 else math.pi - t
                    pts.append(B.pt(z, t))
                    ns.append(B.normal(z, t))
                for seg, pp, nn in split_on_joints(pts, ns):
                    K.strip_decal(P(seg), bvh[seg][0], pp, nn, width, "gcarp_scale")
                n_arcs += 1
        col += 1
        z0 -= step
    # ------------------------------------------------ eyes: dark ring + pupil on the head, glow lens = geo_Eye.*
    empties = {}
    yc, ry, _ = B.sec(EYE_Z)
    th_e = math.asin(max(-0.95, min(0.95, (EYE_Y - yc) / ry)))
    for side, sg in (("L", -1), ("R", 1)):
        th = th_e if sg > 0 else math.pi - th_e
        e = B.pt(EYE_Z, th)
        n = (B.normal(EYE_Z, th) + V((0, 0.08, 0.22))).normalized()      # out, a little up and forward
        a, b, n = K.frame_from(n, (0, 1, 0))
        K.ellipsoid(P("Head"), e + n * 0.0008, (EYE_RING_R, EYE_RING_R, 0.0042), (a, b, n), "gcarp_eye_ring", 10, 4,
                    smooth=False)
        K.ellipsoid(P("Eye." + side), e + n * 0.0028, (EYE_GLOW_R, EYE_GLOW_R, 0.0058), (a, b, n), "gcarp_eye_glow",
                    10, 4, smooth=True)
        K.ellipsoid(P("Head"), e + n * (0.0028 + 0.0055), (0.0072, 0.0066, 0.0015), (a, b, n), "gcarp_eye_ring", 8, 3,
                    smooth=False)
        empties["Eye." + side] = ("Head", e, n)
    # ------------------------------------------------ lip tube: upper lip (Lips) / lower lip (Jaw), split on the mouth
    # line; the chord faces (palate / mouth floor) are the mouth colour, seen when the lower lip drops
    L = LIPB
    for bone, upper in (("Lips", True), ("Jaw", False)):
        zs = [0.42, 0.45, 0.468, 0.48, 0.49, 0.497, 0.50]
        rings, angs = [], []
        for z in zs:
            pts, ths = L.clip_ring(z, 0.0, upper)
            rings.append(pts)
            angs.append(ths)
        K.loft(P(bone), rings, lambda i, k: "gcarp_mouth" if k >= L.n_arc - 1 else "gcarp_lips",
               cap0="gcarp_lips", cap1="gcarp_lips")
    empties["Mouth"] = ("Lips", V((0, MOUTH_Y, 0.498)), None)
    # ------------------------------------------------ barbels (on Lips): trail back and droop at rest
    for sg, side in ((1, "R"), (-1, "L")):
        for idx, base, th, z, d, ln, r0 in (
                ("1", BARB1_BASE, BARB1_TH, BARB1_Z, (0.35, -0.55, -0.75), 0.036, 0.0036),
                ("2", BARB2_BASE, BARB2_TH, BARB2_Z, (0.28, -0.70, -0.66), 0.068, 0.0045)):
            t = th if sg > 0 else math.pi - th
            root = V(_mx(base, sg)) - L.normal(z, t) * 0.0015                  # embedded in the lip
            barbel(P("Barbel.%s%s" % (side, idx)), root, (sg * d[0], d[1], d[2]), ln, r0, "gcarp_barbel")
    # ------------------------------------------------ dorsal: raisable front lobe (modelled RAISED, folded -40 deg to
    # rest; Unity localRotation +40 about X raises it) + the fixed low fringe on Spine.B1 / Spine.B2
    d1 = V(DORSAL1_HEAD)
    fold = Matrix.Rotation(math.radians(-DORSAL_FOLD), 3, "X")               # standard matrix = Unity AngleAxis(-40, right)
    pts = [d1 + fold @ (V((0, top_y(z) + hh, z)) - d1) for z, hh in DORSAL_LOBE]
    K.plate(P("Dorsal1"), pts, (1, 0, 0), 0.004, "gcarp_fin")
    for bone, outline in DORSAL_FRINGE.items():
        K.plate(P(bone), [V((0, top_y(z) + hh, z)) for z, hh in outline], (1, 0, 0), 0.0035, "gcarp_fin")
    # ------------------------------------------------ anal fin (Spine.B2)
    K.plate(P("Anal"), [V((0, bot_y(z) - dd, z)) for z, dd in ANAL], (1, 0, 0), 0.0035, "gcarp_fin")
    # ------------------------------------------------ paired fins
    for side, sg in (("L", -1), ("R", 1)):
        fin_blade(P("Pec." + side), V(_mx(PEC_BASE, sg)) - V((sg * 0.004, 0, 0)), (sg * 0.40, -0.35, -0.85),
                  (sg * 0.85, -0.35, 0.2), PEC_SHAPE, 1.0, "gcarp_fin", sg)
        fin_blade(P("Pel." + side), V(_mx(PEL_BASE, sg)) - V((sg * 0.003, -0.002, 0)), (sg * 0.22, -0.50, -0.84),
                  (sg * 0.9, -0.3, 0.1), PEL_SHAPE, 1.0, "gcarp_fin", sg)
    # ------------------------------------------------ caudal fin: forked, upper + lower lobe plates (sagittal)
    K.plate(P("Tail.Upper"), [V((0, y, z)) for z, y in TAIL_UP], (1, 0, 0), 0.004, "gcarp_fin")
    K.plate(P("Tail.Lower"), [V((0, -y * 0.96, z)) for z, y in TAIL_UP][::-1], (1, 0, 0), 0.004, "gcarp_fin")
    info = {"scaleArcs": n_arcs, "jointsZ": {b: lo for b, (lo, hi), _ in SEGS},
            "lips": {"restFrontZ": 0.50, "mouthY": MOUTH_Y, "protrudeMove": RIG["protrude"][0]["move"],
                     "protrudeTiltX": RIG["protrude"][0]["tilt"][0]},
            "dorsal1": {"foldedAtRestDeg": -DORSAL_FOLD, "raiseMaxX": DORSAL_FOLD},
            "barbels": {"1": "short, upper lip, 36 mm", "2": "long, mouth corners, 68 mm",
                        "curlDownX": "negative (they trail back: -X swings the tip down / forward)"}}
    return parts, empties, info


# ============================================================================ check renders (hyb_legend3d.check)
BITE = {"Lips": {"rot": (12, 0, 0), "move": (0, -0.010, 0.030)}, "Jaw": (30, 0, 0), "Dorsal1": (40, 0, 0),
        "Barbel.L1": (-25, 0, 0), "Barbel.R1": (-25, 0, 0), "Barbel.L2": (-25, 0, 0), "Barbel.R2": (-25, 0, 0)}
CHECK_OPEN = dict(BITE, **{"Pec.L": (0, -20, 0), "Pec.R": (0, 20, 0)})
CHECK_BEND = {"Spine.F": (0, -6, 0), "Head": (0, -12, 0), "Spine.B1": (0, 10, 0), "Spine.B2": (0, 15, 0),
              "Spine.B3": (0, 15, 0), "Tail": (0, 15, 0), "Tail.Upper": (0, 8, 0), "Tail.Lower": (0, 8, 0),
              "Barbel.L2": (0, -10, 0), "Barbel.R2": (0, -10, 0)}

# ============================================================================ review mock (hyb_legend_preview.py)
PREVIEW_LURE = "golden"              # the golden bait billboard (encounter_sets/lake.py); softworm if not built yet
PREVIEW_CM = 90                      # size shown in the mock
LURE_R = 3.6                         # lake visibility (rollout 3.1 light R)
TURNTABLE = dict(
    open=dict(BITE, **{"Pec.L": (0, -25, 0), "Pec.R": (0, 25, 0), "Pel.L": (0, 15, 0), "Pel.R": (0, -15, 0),
                       "Anal": (0, 0, 12)}),
    bend={"Spine.F": (0, -2, 0), "Head": (0, -8, 0), "Spine.B1": (0, 6, 0), "Spine.B2": (0, 10, 0),
          "Spine.B3": (0, 14, 0), "Tail": (0, 15, 0), "Tail.Upper": (0, 6, 0), "Tail.Lower": (0, 6, 0),
          "Pec.L": (0, -25, 0), "Pec.R": (0, 25, 0), "Barbel.L2": (0, -10, 0), "Barbel.R2": (0, -10, 0)},
    lit={"Dorsal1": (20, 0, 0), "Pec.L": (0, -15, 0), "Pec.R": (0, 15, 0)},
    game={"Dorsal1": (20, 0, 0)},
)
SUN_KEY = (0.30, 0.93, 0.20)         # lake: sunbeams from above, top-right (set frame)
VEIL = ("#0c1a10", 0.55)             # the Eyes-beat veil over the back layers (rollout 4.1)
CRUMB = "#ffd24a"                    # golden crumb puff


def _sun_toon(orig, key_w, mix):
    """ActorToon with _SunMix (rollout 1.5) for the mock: L = normalize(lerp(to-lure, key, mix)); the lure light is a
    visibility sphere (lit = 1 - smoothstep(0.35 R, R, d)); f = (0.5 + 0.5 N.L) * lit, f < 0.12 -> abyss (the water)."""
    import hyb_core as R
    import fk_common as C
    import bpy

    def toon_mat(key, ramp, light=None, lure=None, abyss=None, unlit=False):
        if lure is None or unlit or mix <= 0:
            return orig(key, ramp, light, lure, abyss, unlit)
        m = bpy.data.materials.new("T_" + key)
        nb = R.NB(m)
        geo = nb.node("ShaderNodeNewGeometry")
        lp, rad = lure
        to = nb.vmath("SUBTRACT", tuple(lp), geo.outputs["Position"])
        dist = nb.vmath("LENGTH", to)
        ldir = nb.vmath("NORMALIZE", nb.vmath("ADD", nb.vmath("MULTIPLY", nb.vmath("NORMALIZE", to),
                                                                  (1 - mix, 1 - mix, 1 - mix)),
                                              tuple(V(key_w).normalized() * mix)))
        d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], ldir)
        half = nb.math("MULTIPLY_ADD", d, 0.5, 0.5)
        mr = nb.node("ShaderNodeMapRange")
        mr.interpolation_type = "SMOOTHSTEP"
        mr.clamp = True
        nb.link(dist, mr.inputs["Value"])
        mr.inputs["From Min"].default_value = 0.35 * rad
        mr.inputs["From Max"].default_value = rad
        mr.inputs["To Min"].default_value = 1.0
        mr.inputs["To Max"].default_value = 0.0
        f = nb.math("MULTIPLY", half, mr.outputs["Result"])
        col = nb.ramp(f, [(0.0, C.lin(abyss)), (0.12, C.lin(ramp[0])), (0.45, C.lin(ramp[1])), (0.74, C.lin(ramp[2]))],
                      "CONSTANT")
        nb.output_emission(col, 1.0)
        return m
    return toon_mat


def preview_beats(M, sc, cam, lure, surface):
    """Six mock beats of the golden-carp encounter (rollout 3.1) -> [(name, 480x270 frame)]."""
    import random
    import bpy
    import numpy as np
    rng = random.Random(11)
    frames = []
    LGm = M.LG
    key = PREVIEW_LURE if os.path.isfile(os.path.join(M.ENC, "lure_%s_0.png" % PREVIEW_LURE)) else "softworm"
    mix = float(M.PROF.get("sun_mix", 0.75))
    orig = LGm.toon_mat
    LGm.toon_mat = _sun_toon(orig, LGm.C_UW @ V(SUN_KEY), mix)
    Cm = LGm.C_UW.to_4x4()
    Ci = LGm.C_UW.inverted()
    mouth = next(o for o in sc.objs if LGm.base_name(o.name) == "Mouth")
    s = sc.cm / 100.0

    def place(pos, hdg, pitch=0.0, bank=0.0):
        Mx = (Matrix.Translation(V(pos)) @ Matrix.Rotation(math.radians(hdg), 4, "Y")
              @ Matrix.Rotation(math.radians(pitch), 4, "X") @ Matrix.Rotation(math.radians(bank), 4, "Z")
              @ Matrix.Scale(s, 4))
        sc.mount.matrix_world = Cm @ Mx @ Cm.inverted()
        bpy.context.view_layer.update()

    def mouth_at(target, hdg, pitch=0.0, bank=0.0):
        place((0, 0, 0), hdg, pitch, bank)
        m = Ci @ mouth.matrix_world.translation
        place(V(target) - m, hdg, pitch, bank)

    def fwd(hdg, pitch):
        h, p = math.radians(hdg), math.radians(pitch)
        return V((math.sin(h) * math.cos(p), -math.sin(p), math.cos(h) * math.cos(p)))

    def to_lure(pos):
        return math.degrees(math.atan2(-pos[0], -pos[2]))

    def crumbs(img, px, n=6, spread=6, toward=None):
        col = M.R.hexrgb(CRUMB)
        for i in range(n):
            if toward is None:
                x, y = px[0] + rng.randint(-spread, spread), px[1] - rng.randint(0, spread + 2)
            else:
                t = (i + 1) / (n + 1)
                x = px[0] + (toward[0] - px[0]) * t + rng.randint(-2, 2)
                y = px[1] + (toward[1] - px[1]) * t + rng.randint(-2, 2)
            xi, yi = int(round(x)), int(round(y))
            if 0 <= xi < M.VW and 0 <= yi < M.VH:
                img[yi, xi, :3] = col

    def silt(img, px, n=2, scale=1):
        tint = M.PROF.get("silt", "#a8966a")
        for i in range(n):
            sp = M.tint(M.load("silt_%d" % rng.randint(0, 3)), tint, 0.7)
            if scale > 1:
                sp = M.R.upscale(sp, scale)
            M.R.over(img, sp, int(px[0] - sp.shape[1] / 2 + rng.randint(-6, 6)),
                     int(px[1] - sp.shape[0] / 2 - rng.randint(0, 4)))

    try:
        lx, ly, _ = M.project(cam, lure)
        hx, hy, _ = M.project(cam, V((0, 0, 30.0)) + V((M.CAM.x, 0, M.CAM.z)))           # the floor horizon
        up = M.project(cam, lure + V(M.SETP.line_up))[:2]
        print("MOCK lure px", round(lx, 1), round(ly, 1), "horizon row", round(hy, 1), "lure", key)
        # 1) eyes: two gold glints low in the green murk beyond the weeds, drifting in towards the bait (veiled set)
        pos = V((4.0, 0.28, 6.6))
        place(pos, to_lure(pos) - 8, pitch=4)
        sc.pose(Spine_B1=(0, 4, 0), Spine_B2=(0, 6, 0), Tail=(0, 8, 0))
        fish = M.render_fish(sc, cam, lure, "g1", rim=False, eyes_only=True)
        under = M.back_layers((lx, ly), hy)
        vc, va = M.PROF.get("veil", VEIL)
        under[..., :3] = under[..., :3] * (1 - va) + M.R.hexrgb(vc) * va
        M.R.over(under, fish, 0, 0)
        under = M.front_fx(under, (lx, ly), up, rng, snow=10)
        M.put_lure(under, (lx, ly), key, 0)
        M.put_eyeshine(under, sc.eyes_px(cam))
        frames.append(("eyes", M.compose_window(*surface, under, M.WIN)))

        def windowed(tag, lure_u, lure_frame=0, eyes_mult=1.0, line=True, fx=None):
            lpx = M.project(cam, lure_u)[:2]
            upx = M.project(cam, lure_u + V(M.SETP.line_up))[:2]
            fish = M.render_fish(sc, cam, lure_u, tag)
            under = M.back_layers(lpx, hy)
            M.put_lure(under, lpx, key, lure_frame)
            if fx:
                fx(under, lpx)
            under = M.front_fx(under, lpx, upx if line else None, rng, snow=12)
            M.R.over(under, fish, 0, 0)
            M.put_eyeshine(under, sc.eyes_px(cam), eyes_mult)
            return M.compose_window(*surface, under, M.WIN)

        # 2) approach: a slow S-curve along the bottom, head slightly down, dorsal up (wary); the gold body comes out
        #    of the murk at the edge of the lake visibility (3.6 m)
        pos = V((1.75, 0.30, 1.85))
        place(pos, to_lure(pos) + 22, pitch=8, bank=-5)
        sc.pose(Spine_F=(0, -3, 0), Head=(0, -7, 0), Spine_B1=(0, 5, 0), Spine_B2=(0, 8, 0), Spine_B3=(0, 10, 0),
                Tail=(0, 12, 0), Tail_Upper=(0, 6, 0), Tail_Lower=(0, 6, 0), Dorsal1=(40, 0, 0),
                Pec_L=(0, -20, 0), Pec_R=(0, 25, 0), Pel_L=(0, 12, 0), Pel_R=(0, -12, 0),
                Barbel_L2=(0, 8, 0), Barbel_R2=(0, 8, 0))
        frames.append(("approach", windowed("g2", lure, lure_frame=1, eyes_mult=0.6)))
        # 3) tease (curious): grubbing on the 1.1 m orbit, nose-down 20 deg, nose ~0.2 m over the mud, barbels
        #    trailing; the bait is dragged (golden crumb puff) and the carp digs (silt puff at its lips)
        pos = V((0.62, 0.36, 0.95))
        place(pos, to_lure(pos) + 62, pitch=20, bank=4)
        sc.pose(Spine_F=(0, -4, 0), Head=(4, -10, 0), Spine_B1=(0, 4, 0), Spine_B2=(0, 6, 0), Spine_B3=(0, 7, 0),
                Tail=(0, 9, 0), Tail_Upper=(0, 5, 0), Tail_Lower=(0, 5, 0), Dorsal1=(12, 0, 0),
                Pec_L=(0, 22, 0), Pec_R=(0, -18, 0), Pel_L=(0, -12, 0), Pel_R=(0, 12, 0), Anal=(0, 0, -10),
                Lips={"rot": (2, 0, 0), "move": (0, -0.002, 0.006)},
                Barbel_L1=(-8, 6, 0), Barbel_R1=(-8, 6, 0), Barbel_L2=(-10, 8, 0), Barbel_R2=(-10, 8, 0))
        mpx = M.project(cam, Ci @ mouth.matrix_world.translation)[:2]
        lure3 = V((0.03, 0.04, -0.02))

        def fx3(img, lpx):
            crumbs(img, lpx, n=7, spread=5)
            silt(img, (mpx[0], mpx[1] + 4), n=2)
        frames.append(("tease", windowed("g3", lure3, lure_frame=1, fx=fx3)))
        # 4) the pass: the 2.2 m wary ellipse crosses between the camera and the bait - the broad golden flank close
        #    to the camera, lit from above (sun mix)
        pos = V((-0.40, 0.42, -1.15))
        place(pos, to_lure(pos) - 78, pitch=3, bank=-5)
        sc.pose(Spine_F=(0, -5, 0), Head=(0, -12, 0), Spine_B1=(0, 3, 0), Spine_B2=(0, 5, 0), Spine_B3=(0, 6, 0),
                Tail=(0, 8, 0), Tail_Upper=(0, 5, 0), Tail_Lower=(0, 5, 0), Dorsal1=(40, 0, 0),
                Pec_L=(0, 18, 0), Pec_R=(0, -22, 0), Pel_L=(0, -12, 0), Pel_R=(0, 12, 0), Anal=(0, 0, 12),
                Barbel_L2=(0, -8, 0), Barbel_R2=(0, -8, 0))
        frames.append(("pass", windowed("g4", lure, lure_frame=0, line=False)))
        # 5) excited: hovers nose-down 30 deg, lips 0.4 m from the bait, barbels curled down (barbel 1), lips pulsing
        #    (protrude 0.3), pectorals sculling
        hdg, pitch = -58.0, 30.0
        mouth_at(lure - fwd(hdg, pitch) * 0.40, hdg, pitch)
        sc.pose(Lips={"rot": (3.6, 0, 0), "move": (0, -0.003, 0.009)}, Jaw=(6, 0, 0), Dorsal1=(20, 0, 0),
                Head=(3, 0, 0), Spine_B1=(0, 2, 0), Spine_B2=(0, 3, 0), Tail=(0, -4, 0),
                Pec_L=(0, -30, 0), Pec_R=(0, 30, 0), Pel_L=(0, 20, 0), Pel_R=(0, -20, 0), Anal=(0, 0, 8),
                Barbel_L1=(-25, 0, 0), Barbel_R1=(-25, 0, 0), Barbel_L2=(-25, 0, 0), Barbel_R2=(-25, 0, 0))
        mouth_at(lure - fwd(hdg, pitch) * 0.40, hdg, pitch)
        frames.append(("excited", windowed("g5", lure, lure_frame=0, fx=lambda img, lpx: crumbs(img, lpx, n=4))))
        # 6) full screen at the bite: head tipped down 25 deg, the lip tube fully out (protrude 1), the lower lip
        #    dropped, the bait + crumbs + silt streaming into the mouth; camera dollied in, f x1.5
        bait = V((0.0, 0.035, 0.0))
        tgt = bait + V((0.17, 0.10, -0.02))                    # between the lips and the eye: the frame centre
        cpos = tgt + V((-0.34, 0.10, -0.80))
        cam6 = M.setcam(pos=cpos, target=tgt, f=M.FPX * 1.5, pp=(M.VW / 2, M.VH / 2 + 8))
        hdg, pitch = -80.0, 25.0
        sc.pose(**{k.replace(".", "_"): v for k, v in BITE.items()},
                Head=(4, 3, 0), Spine_B1=(0, 5, 0), Spine_B2=(0, 8, 0), Spine_B3=(0, 9, 0), Tail=(0, 12, 0),
                Pec_L=(0, -30, 0), Pec_R=(0, 30, 0), Pel_L=(0, 20, 0), Pel_R=(0, -20, 0))
        mouth_at(bait + fwd(hdg, pitch) * -0.01, hdg, pitch, bank=-3)
        mpx = M.project(cam6, Ci @ mouth.matrix_world.translation)[:2]
        bpx = M.project(cam6, bait + V((0.02, -0.005, 0.03)))[:2]
        hx6, hy6, _ = M.project(cam6, V((0, 0, 30.0)) + V((cpos.x, 0, cpos.z)))
        fish = M.render_fish(sc, cam6, bait, "g6")
        under = M.back_layers(bpx, hy6)
        silt(under, (bpx[0] + 6, bpx[1] + 8), n=4, scale=2)
        M.put_lure(under, bpx, key, 1, scale=2)
        crumbs(under, (bpx[0] + 30, bpx[1] + 14), n=8, toward=mpx)
        crumbs(under, (bpx[0] - 22, bpx[1] + 10), n=6, toward=mpx)
        M.R.over(under, fish, 0, 0)
        under = M.front_fx(under, None, None, rng, snow=16)
        M.put_eyeshine(under, sc.eyes_px(cam6))
        frames.append(("bite_full", M.compose_window(*surface, under, (0, 0, M.VW, M.VH), frame=False)))
    finally:
        LGm.toon_mat = orig
    return frames
