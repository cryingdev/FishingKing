"""
Legend module: 청새치 BLUE MARLIN (ocean, backdrop "ocean", shared lurk with the great white). Loaded by
hyb_legend3d.py (model) and hyb_legend_preview.py (review mock). Design row: Docs/legends_rollout.md 3.4 (model,
palette, choreography), 7 (species bones), 4.5 (the ocean set).

Read at small size by: the long dark spear (Bill), the tall cobalt sail (Dorsal1, folds into a low ridge), the tall
lunate tail, cobalt back over a bright blue band and silver-white lower flanks, and 15 vertical stripes on their own
slot `marl_stripes_glow` (unlit; code lerps its light tone ramp[1] dim -> ramp[2] lit with the `glow` channel).

Rig notes (Unity localRotation Euler on the identity rest; see also RIG / the report):
  * Dorsal1 = the sail: modelled RAISED, folded -75 deg about X at rest; +X 0..75 raises it (0 cruising, 35 curious,
    75 excited / tell). Folded, only its leading edge shows as a low ridge on the back; the long low rear part of the
    first dorsal is a FIXED ridge on Spine.F / Spine.B1 (marl_sail), so the raised sail runs into it.
  * Bill (on Head, head at the bill root z 0.37): yaw +-4 deg flex, +-6 deg in the slash. The Head joint carries a
    long tapered plug (32 mm) so the slash (Head yaw +-25 deg) opens no gap.
  * Jaw (gape corner under / behind the eye): opens +X, 0..30.
  * Pec.L / Pec.R: rigid falcate pectorals, folded flat on the flank at rest. They flare OUT with Pec.L +Y / Pec.R -Y
    (0..40) - the opposite sign of the coelacanth's lobe "flare" (Legend3D.Apply uses Pec.L -30*flare): a
    backward-pointing fin swung the other way goes into the body. No paired-fin trot for this fish (finAmp 0).
"""
import math
import os
import random
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
import hyb_legend_kit as K

V = K.V

# ============================================================================ identity
ID = "blue_marlin"
MODEL = "legend_blue_marlin"
ARMATURE = "BlueMarlin"              # the FBX root node
PREFIX = "marl"
CM = (200, 400)                      # GameDatabase blue_marlin 200..400 cm; runtime scale = cm / 100
STAGE = "ocean"
BACKDROP = "ocean"
PRESET = "ocean"

# ============================================================================ palette (rollout 3.4)
OUTLINE = "#0b1322"
RAMPS = {
    "marl_back": ["#0c1630", "#142448", "#1e3462"],
    "marl_side": ["#1e3a78", "#2c56a4", "#4a7ac8"],
    "marl_belly": ["#8e9cb4", "#c4ceda", "#eef2f8"],
    "marl_stripes_glow": ["#1a3a6a", "#2a5a9a", "#7ae0ff"],      # unlit: code shows lerp(ramp[1], ramp[2], glow)
    "marl_sail": ["#0e1a44", "#1a3070", "#2e4ea0"],
    "marl_fin": ["#0e1a3a", "#182c5a", "#26407e"],
    "marl_bill": ["#0a1224", "#16203a", "#26344e"],
    "marl_mouth": ["#2a1a2a", "#44283a", "#603a4e"],
    "marl_eye_ring": ["#04060a", "#0a0e16", "#161e2a"],
    "marl_eye_glow": ["#1a5a9a", "#4ab0ff", "#e8f8ff"],
}
LURE_LIGHT = {"abyss": "#081e44", "fogOutline": "#2a5a90", "eyeCore": "#e8f8ff", "eyeGlow": "#4ab0ff",
              "frameLine": "#7fd4ff"}
FACE_BONES = ["Head", "Bill", "Jaw"]
RIG = {
    "limits": [{"bone": "Jaw", "max": 30}, {"bone": "Dorsal1", "max": 75}],
    "glow": [{"material": "marl_stripes_glow"}],
}
BODY_GROUP = {"Bill"}                # the spear reads as one skin with the head in the check renders

# ============================================================================ body
# z, top y, bottom y, half-width x (nose -> tail). The head starts at the bill root (z 0.385, the upper-jaw tip; the
# Bill part runs on to z +0.50); steep forehead to the nape; deepest at z ~0.09 (depth 0.20); a depressed, keeled
# peduncle; the lunate tail's tips reach z -0.50.
MARL_BODY = [
    (0.385, 0.007, -0.018, 0.008), (0.365, 0.014, -0.029, 0.014), (0.340, 0.024, -0.040, 0.021),
    (0.310, 0.036, -0.050, 0.028), (0.280, 0.050, -0.062, 0.035), (0.250, 0.066, -0.074, 0.042),
    (0.220, 0.080, -0.084, 0.048), (0.180, 0.092, -0.092, 0.053), (0.130, 0.100, -0.097, 0.056),
    (0.090, 0.102, -0.098, 0.056), (0.040, 0.100, -0.094, 0.054), (-0.020, 0.093, -0.086, 0.050),
    (-0.080, 0.082, -0.074, 0.045), (-0.140, 0.068, -0.060, 0.039), (-0.200, 0.053, -0.046, 0.032),
    (-0.250, 0.040, -0.035, 0.026), (-0.300, 0.026, -0.023, 0.021), (-0.335, 0.015, -0.013, 0.020),
    (-0.365, 0.012, -0.010, 0.017), (-0.390, 0.010, -0.008, 0.012),
]
OVER = 0.015
INSET = 0.0015
MOUTH_HINGE = (0.285, -0.022)        # (z, y) of the gape corner = the Jaw bone
MOUTH_FRONT = (0.385, -0.009)        # (z, y) of the mouth at the front (the lower-jaw tip / bill root)
N_ARC = 9
# zones: cobalt back above ~22 deg on the section, one bright-blue band down to the mid line, silver-white below
BODY = K.Body(MARL_BODY, ("marl_back", "marl_side", "marl_belly"), nseg=16, back_pinch=0.12, inset=INSET, over=OVER,
              mouth=(MOUTH_HINGE, MOUTH_FRONT), zones=(0.70, 0.45), n_arc=N_ARC, n_chord=2)

SAIL_HINGE = (0.0, 0.086, 0.205)     # Dorsal1 head: the sail's front base, on the back line
# the raised sail (dz, dy from the hinge). Nothing lies in front of the hinge (it would rise out of the back when
# folded); folded -75, the leading edge lies ~5 mm over the back and everything behind it sinks into the body.
SAIL_RAISED = [(-0.004, -0.008), (0.003, 0.030), (0.000, 0.075), (-0.008, 0.115), (-0.017, 0.148), (-0.028, 0.170),
               (-0.040, 0.178), (-0.045, 0.160), (-0.048, 0.132), (-0.053, 0.102), (-0.061, 0.075), (-0.074, 0.054),
               (-0.094, 0.040), (-0.118, 0.033), (-0.140, 0.030), (-0.150, 0.028), (-0.155, -0.010), (-0.080, -0.018)]
SAIL_SPOTS = [(-0.020, 0.120, 0.006), (-0.031, 0.150, 0.005), (-0.026, 0.088, 0.0065), (-0.040, 0.115, 0.0055),
              (-0.050, 0.075, 0.005), (-0.078, 0.036, 0.004)]
SAIL_FOLD = -75.0                    # rest = folded (Unity localRotation +75 about X raises it)
HEAD_PLUG = [(0.001, 0.0015), (0.012, 0.0025), (0.022, 0.005), (0.032, 0.012)]   # (behind the joint, inset)

BONES = [
    ("Root", None, (0, 0, 0)),
    ("Spine.F", "Root", (0, 0, 0.02)),
    ("Head", "Spine.F", (0, 0, 0.22)),
    ("Jaw", "Head", (0, MOUTH_HINGE[1], MOUTH_HINGE[0])),
    ("Bill", "Head", (0, -0.0015, 0.37)),
    ("Dorsal1", "Spine.F", SAIL_HINGE),
    ("Pec.L", "Spine.F", (-0.047, -0.048, 0.198)),
    ("Pec.R", "Spine.F", (0.047, -0.048, 0.198)),
    ("Pel.L", "Spine.F", (-0.010, -0.090, 0.165)),
    ("Pel.R", "Spine.F", (0.010, -0.090, 0.165)),
    ("Spine.B1", "Root", (0, 0, -0.02)),
    ("Spine.B2", "Spine.B1", (0, 0, -0.14)),
    ("Spine.B3", "Spine.B2", (0, 0, -0.25)),
    ("Dorsal2", "Spine.B3", (0, 0.032, -0.265)),
    ("Anal", "Spine.B3", (0, -0.030, -0.265)),
    ("Tail", "Spine.B3", (0, 0, -0.335)),
    ("Tail.Upper", "Tail", (0, 0.008, -0.37)),
    ("Tail.Lower", "Tail", (0, -0.008, -0.37)),
]
EYE_C = (0.0244, 0.006, 0.315)       # |x|, y, z of the eye centres (Eye.L at -x)
EYE_RING, EYE_R, PUPIL = 0.016, 0.0128, 0.0058
MOUTH_EMPTY = (0.0, -0.012, 0.365)   # the lure is grabbed crosswise between the jaws, just behind the bill root
STRIPES_Z = [0.188, 0.158, 0.128, 0.098, 0.068, 0.038, 0.012, -0.018, -0.046, -0.074, -0.102, -0.126, -0.158,
             -0.186, -0.214]


def flank_x(z, y, off=0.0):
    """x of the body surface (right flank) at (z, y), + off."""
    yc, ry, hw = BODY.sec(z)
    s = max(-0.999, min(0.999, (y - yc) / ry))
    return hw * math.sqrt(1 - s * s) * (1 - BODY.back_pinch * max(0.0, s)) + off


def build():
    B = BODY
    rng = random.Random(11)
    parts = {}

    def P(name):
        if name not in parts:
            parts[name] = K.Part(name)
        return parts[name]

    # ------------------------------------------------ body segments (rear -> front); the FRONT one carries the plug
    J = {"Tail": -0.335, "Spine.B3": -0.25, "Spine.B2": -0.14, "Spine.B1": 0.0, "Head": 0.22}
    segs = [("Tail", (-0.390, -0.335), False), ("Spine.B3", (-0.335, -0.25), True),
            ("Spine.B2", (-0.25, -0.14), True), ("Spine.B1", (-0.14, 0.0), True), ("Spine.F", (0.0, 0.22), True)]
    bvh = {}
    for bone, (z0, z1), e0 in segs:
        p = P(bone)
        K.loft(p, B.body_rings(z0, z1, e0, False, B.full_ring), B.full_mat, cap0="marl_side", cap1="marl_side")
        p.bm.normal_update()
        bvh[bone] = BVHTree.FromBMesh(p.bm)
    # Head: a long tapered plug back into Spine.F (the bill slash yaws the head +-25 deg), the throat plug forward
    hp = P("Head")
    rings = [B.full_ring(0.22 - e, ins) for e, ins in reversed(HEAD_PLUG)]
    rings += [B.full_ring(z, 0.0) for z in B.stations(0.22, MOUTH_HINGE[0])]
    rings += [B.full_ring(MOUTH_HINGE[0] + 0.001, INSET), B.full_ring(MOUTH_HINGE[0] + OVER, INSET)]
    K.loft(hp, rings, B.full_mat, cap0="marl_side", cap1="marl_mouth")
    hp.bm.normal_update()
    bvh["Head"] = BVHTree.FromBMesh(hp.bm)
    # gill-cover edge: a dark crescent on each side, bulging back at mid flank
    for sg in (1, -1):
        pts, ns = [], []
        for j in range(8):
            a = math.radians(-55 + 140 * j / 7)
            th = a if sg > 0 else math.pi - a
            z = 0.252 - 0.016 * math.cos(a * 1.1)
            pts.append(B.pt(z, th))
            ns.append(B.normal(z, th))
        K.strip_decal(hp, bvh["Head"], pts, ns, 0.0035, "marl_fin")
    # upper head shell (Head) and lower jaw (Jaw), split on the mouth line
    for bone, upper in (("Head", True), ("Jaw", False)):
        zs = [MOUTH_HINGE[0] - OVER, MOUTH_HINGE[0] - 0.001] + B.stations(MOUTH_HINGE[0], MOUTH_FRONT[0], 0.05, 0.012)
        rings, angs = [], []
        for i, z in enumerate(zs):
            pts, ths = B.clip_ring(z, INSET if i < 2 else 0.0, upper)
            rings.append(pts)
            angs.append(ths)

        def fm(i, k, angs=angs):
            if k >= N_ARC - 1:
                return "marl_mouth"
            return B.zone_mat((angs[i][k] + angs[i][k + 1]) / 2)
        K.loft(P(bone), rings, fm, cap0="marl_mouth", cap1="marl_side" if upper else "marl_belly")
    # ------------------------------------------------ the bill: a round dark spear from inside the snout to z +0.50
    bill = P("Bill")
    cy = -0.0015
    rings = []
    for z, rx, ry in ((0.350, 0.0100, 0.0110), (0.372, 0.0086, 0.0096), (0.392, 0.0072, 0.0080),
                      (0.420, 0.0056, 0.0062), (0.450, 0.0040, 0.0044), (0.476, 0.0026, 0.0028),
                      (0.494, 0.0012, 0.0013), (0.4995, 0.0003, 0.0003)):
        rings.append([V((rx * math.cos(2 * math.pi * k / 6), cy + 0.0015 * (z - 0.37) / 0.13 + ry * math.sin(2 * math.pi * k / 6), z))
                      for k in range(6)])
    K.loft(bill, rings, lambda i, k: "marl_bill", smooth=True, cap0="marl_bill", cap1="marl_bill")
    # ------------------------------------------------ vertical stripes (marl_stripes_glow), upper flank
    stripes = 0
    seg_of = lambda z: next(b for b, (a0, a1), _ in segs if a0 <= z < a1)  # noqa: E731
    for i, z in enumerate(STRIPES_Z):
        bone = seg_of(z)
        top = math.radians(74 - 10 * max(0.0, -z) / 0.22)          # shorter towards the tail
        bot = math.radians(-12 + 18 * max(0.0, -z) / 0.22)
        wid = 0.0075 - 0.0025 * max(0.0, -z) / 0.22
        for sg in (1, -1):
            pts, ns = [], []
            for j in range(6):
                a = top + (bot - top) * j / 5
                th = a if sg > 0 else math.pi - a
                zz = z + 0.007 * (1 - j / 5) - 0.002 * math.sin(math.pi * j / 5)    # tops lean a little forward
                pts.append(B.pt(zz, th))
                ns.append(B.normal(zz, th))
            K.strip_decal(P(bone), bvh[bone], pts, ns, wid, "marl_stripes_glow", lift=0.0012)
            stripes += 1
    # ------------------------------------------------ eyes: dark ring + pupil on Head, the glow lens = geo_Eye.*
    empties = {}
    for side, sg in (("L", -1), ("R", 1)):
        e = V((sg * EYE_C[0], EYE_C[1], EYE_C[2]))
        a, b, n = K.frame_from(V((sg * 0.92, 0.10, 0.38)), (0, 1, 0))
        K.ellipsoid(hp, e + n * 0.0005, (EYE_RING, EYE_RING, 0.004), (a, b, n), "marl_eye_ring", 10, 4, smooth=False)
        g = P("Eye." + side)
        K.ellipsoid(g, e + n * 0.0022, (EYE_R, EYE_R, 0.0055), (a, b, n), "marl_eye_glow", 10, 4, smooth=True)
        K.ellipsoid(hp, e + n * (0.0022 + 0.0053), (PUPIL, PUPIL * 0.85, 0.0014), (a, b, n), "marl_eye_ring", 8, 3,
                    smooth=False)
        empties["Eye." + side] = ("Head", e, n)
    empties["Mouth"] = ("Head", V(MOUTH_EMPTY), None)
    # ------------------------------------------------ the sail (Dorsal1): modelled RAISED, folded -75 deg about X
    H = V(SAIL_HINGE)
    fold = Matrix.Rotation(math.radians(SAIL_FOLD), 3, "X")      # = Unity AngleAxis(-75, right)
    sail = P("Dorsal1")
    K.plate(sail, [H + fold @ V((0, dy, dz)) for dz, dy in SAIL_RAISED], (1, 0, 0), 0.004, "marl_sail")
    # dark spots on both faces of the sail (mostly inside the body when folded)
    for dz, dy, r in SAIL_SPOTS:
        for sg in (1, -1):
            ring = [V((sg * 0.0028, dy + r * math.sin(2 * math.pi * k / 6 + 0.3) * 0.9, dz + r * math.cos(2 * math.pi * k / 6 + 0.3)))
                    for k in range(6)]
            vs = [sail.bm.verts.new(H + fold @ q) for q in (ring[::-1] if sg > 0 else ring)]     # outward = +-x
            sail.face(vs, "marl_bill", False)
    # the long low rear part of the first dorsal: fixed ridges on Spine.F and Spine.B1
    K.plate(P("Spine.F"), [V((0, y, z)) for z, y in ((0.078, 0.096), (0.070, 0.116), (0.030, 0.115), (-0.006, 0.110),
                                                        (-0.010, 0.092))], (1, 0, 0), 0.0035, "marl_sail")
    K.plate(P("Spine.B1"), [V((0, y, z)) for z, y in ((0.004, 0.090), (-0.004, 0.110), (-0.040, 0.105),
                                                         (-0.075, 0.096), (-0.100, 0.083), (-0.112, 0.070))],
            (1, 0, 0), 0.0035, "marl_sail")
    # ------------------------------------------------ pectorals: rigid falcate blades folded flat on the lower flank
    for side, sg in (("L", -1), ("R", 1)):
        top_e, bot_e = [], []
        for j in range(7):
            s = j / 6
            zc = 0.196 - 0.150 * s
            yc = -0.049 - 0.046 * s ** 1.35
            w = 0.027 * (1 - s) ** 0.75 + 0.0015
            bow = 0.006 * math.sin(math.pi * s)                          # falcate: convex upper edge, concave lower
            top_e.append((zc + 0.004 * (1 - s), yc + w * 0.45 + bow))
            bot_e.append((zc - 0.006 * (1 - s), yc - w * 0.55 + bow * 0.4))
        outline = top_e + bot_e[::-1][1:]
        pts = [V((sg * flank_x(z, y, 0.0055), y, z)) for z, y in outline]
        K.plate(P("Pec." + side), pts, (sg * 0.93, -0.37, 0.0), 0.003, "marl_fin")
        # thin pelvic spikes hanging under the pectorals
        K.plate(P("Pel." + side), [V((sg * x, y, z)) for x, y, z in ((0.010, -0.085, 0.174), (0.012, -0.115, 0.125),
                                                                         (0.014, -0.138, 0.084), (0.012, -0.106, 0.120),
                                                                         (0.010, -0.088, 0.152))],
                (1, 0, 0), 0.0025, "marl_fin")
    # ------------------------------------------------ first anal fin (fixed, Spine.B1) and the small second dorsal / anal
    K.plate(P("Spine.B1"), [V((0, y, z)) for z, y in ((-0.028, -0.080), (-0.052, -0.114), (-0.072, -0.136),
                                                        (-0.086, -0.139), (-0.083, -0.119), (-0.092, -0.092),
                                                        (-0.104, -0.066))], (1, 0, 0), 0.003, "marl_fin")
    finlet = ((-0.258, 0.031), (-0.268, 0.046), (-0.282, 0.058), (-0.288, 0.050), (-0.296, 0.032), (-0.302, 0.020))
    K.plate(P("Dorsal2"), [V((0, y, z)) for z, y in finlet], (1, 0, 0), 0.003, "marl_fin")
    K.plate(P("Anal"), [V((0, -y * 0.95, z)) for z, y in finlet][::-1], (1, 0, 0), 0.003, "marl_fin")
    # ------------------------------------------------ caudal keels (Tail) + the tall lunate tail
    for sg in (1, -1):
        K.plate(P("Tail"), [V((sg * x, 0.0, z)) for x, z in ((0.014, -0.326), (0.029, -0.348), (0.029, -0.368),
                                                               (0.012, -0.388))], (0, 1, 0), 0.003, "marl_fin")
    upper = [(-0.355, 0.004), (-0.368, 0.035), (-0.385, 0.070), (-0.408, 0.105), (-0.435, 0.140), (-0.465, 0.170),
             (-0.490, 0.188), (-0.500, 0.192), (-0.492, 0.176), (-0.472, 0.150), (-0.452, 0.118), (-0.433, 0.083),
             (-0.418, 0.048), (-0.408, 0.020), (-0.404, 0.0)]
    K.plate(P("Tail.Upper"), [V((0, y, z)) for z, y in upper], (1, 0, 0), 0.004, "marl_fin")
    K.plate(P("Tail.Lower"), [V((0, -y * 0.97, z)) for z, y in upper][::-1], (1, 0, 0), 0.004, "marl_fin")
    info = {"stripes": stripes, "jointsZ": J, "headPlug": HEAD_PLUG,
            "mouthLine": {"hinge_zy": MOUTH_HINGE, "front_zy": MOUTH_FRONT},
            "sail": {"hinge": list(SAIL_HINGE), "restFoldDeg": SAIL_FOLD, "raise": "+X 0..75"},
            "pecFlare": "Pec.L +Y / Pec.R -Y, 0..40 (out from the flank)"}
    return parts, empties, info


# ============================================================================ check renders (hyb_legend3d.check)
# bite pose: jaw 30, sail up 75, pectorals flared 35, bill flexed 6; bend: the bill slash (head 25 + bill 6) + tail 18
CHECK_OPEN = {"Jaw": (30, 0, 0), "Dorsal1": (75, 0, 0), "Pec.L": (0, 35, 0), "Pec.R": (0, -35, 0), "Bill": (0, 6, 0),
              "Pel.L": (0, 10, 0), "Pel.R": (0, -10, 0)}
CHECK_BEND = {"Head": (0, -25, 0), "Bill": (0, -6, 0), "Spine.F": (0, 1, 0), "Spine.B1": (0, 3, 0),
              "Spine.B2": (0, 6, 0), "Spine.B3": (0, 10, 0), "Tail": (0, 18, 0), "Tail.Upper": (0, 8, 0),
              "Tail.Lower": (0, 8, 0), "Dorsal1": (35, 0, 0)}

# ============================================================================ review mock (hyb_legend_preview.py)
PREVIEW_LURE = "kona"                # lure billboard (the ocean set builds lure_kona_0/1; jig until then)
PREVIEW_CM = 300                     # size shown in the mock (mid of 200..400 cm; camScale 1.2 keeps it in the window)
LURE_R = 6.0                         # ocean visibility (EncounterDef light R)
CAM_SCALE = 1.2                      # EncounterDef.camScale: camera rest distance x
SURFACE_ABOVE = 1.2                  # the surface 1.2 m above the lure (ocean set, lure "Mid")
SUN_KEY = (0.15, 1.0, -0.25)         # _KeyDir for the mock's _SunMix emulation (from above, a little camera-side)
TURNTABLE = dict(
    open=CHECK_OPEN,
    bend={"Head": (0, -8, 0), "Bill": (0, -3, 0), "Spine.B1": (0, 2, 0), "Spine.B2": (0, 4, 0), "Spine.B3": (0, 9, 0),
          "Tail": (0, 18, 0), "Tail.Upper": (0, 6, 0), "Tail.Lower": (0, 6, 0), "Pec.L": (0, 12, 0),
          "Pec.R": (0, -12, 0)},
    lit={"Dorsal1": (35, 0, 0), "Pec.L": (0, 12, 0), "Pec.R": (0, -12, 0)},
    game={},
)


def _clamp01(x):
    return max(0.0, min(1.0, x))


def _mix_hex(a, b, t):
    ca = [int(a[i:i + 2], 16) for i in (1, 3, 5)]
    cb = [int(b[i:i + 2], 16) for i in (1, 3, 5)]
    return "#%02x%02x%02x" % tuple(int(round(x + (y - x) * t)) for x, y in zip(ca, cb))


def install_preview_hooks(M, sc):
    """Mock-only emulation of two code-side channels (idempotent, local to this module):
    * glow: the stripes' light tone = lerp(ramp[1], ramp[2], glow); glow = sc.stripe_glow if set, else from the sail
      (clamp01((Dorsal1 - 35) / 40): folded / half up = dim, raised = lit), so the generic turntable shows both.
    * _SunMix (rollout 1.5): L = normalize(lerp(to-lure, key, sunMix)) with the lure light as a visibility sphere
      (the set PROFILE's sun_mix, 0.8 for the ocean). Key-lit renders (no lure) stay the builder's own material."""
    import bpy
    T = sc.T
    if getattr(T, "_marl_hooks", False):
        return
    T._marl_hooks = True
    base = list(T.pal["marl_stripes_glow"]["ramp"])
    orig_set = T.set_materials

    def set_materials(light=None, lure=None):
        g = getattr(sc, "stripe_glow", None)
        if g is None:
            pb = sc.arm.pose.bones.get("Dorsal1")
            d1 = math.degrees(pb.rotation_quaternion.to_euler().x) if pb else 0.0
            g = _clamp01((d1 - 35.0) / 40.0)
        T.pal["marl_stripes_glow"]["ramp"] = base[:2] + [_mix_hex(base[1], base[2], g)]
        orig_set(light=light, lure=lure)
    T.set_materials = set_materials
    s_mix = float(M.PROF.get("sun_mix", 0.8))
    orig_toon = M.LG.toon_mat
    key_w = tuple((M.LG.C_UW @ V(SUN_KEY)).normalized())

    def toon_mat(key, ramp, light=None, lure=None, abyss=None, unlit=False):
        if unlit or lure is None or s_mix <= 0:
            return orig_toon(key, ramp, light, lure, abyss, unlit)
        m = bpy.data.materials.new("T_" + key)
        nb = M.R.NB(m)
        C = M.C
        lp, rad = lure
        geo = nb.node("ShaderNodeNewGeometry")
        to = nb.vmath("SUBTRACT", tuple(lp), geo.outputs["Position"])
        dist = nb.vmath("LENGTH", to)
        dl = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], nb.vmath("NORMALIZE", to))
        dk = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], key_w)
        d = nb.math("ADD", nb.math("MULTIPLY", dl, 1.0 - s_mix), nb.math("MULTIPLY", dk, s_mix))
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
    M.LG.toon_mat = toon_mat


def _place(M, sc, pos, heading, pitch=0.0, bank=0.0):
    """Scene.place + pitch (+ = nose up)."""
    import bpy
    s = sc.cm / 100.0
    Mx = (Matrix.Translation(V(pos)) @ Matrix.Rotation(math.radians(heading), 4, "Y")
          @ Matrix.Rotation(math.radians(-pitch), 4, "X") @ Matrix.Rotation(math.radians(bank), 4, "Z")
          @ Matrix.Scale(s, 4))
    Cm = M.LG.C_UW.to_4x4()
    sc.mount.matrix_world = Cm @ Mx @ Cm.inverted()
    bpy.context.view_layer.update()


def _hdg(d):
    return math.degrees(math.atan2(d[0], d[2]))


def preview_beats(M, sc, cam, lure, surface):
    """Six mock beats (rollout 3.4): eyes deep below -> the rising silhouette -> the curious pass across the camera
    (sail half up) -> excited, lit up, tailing the kona -> the bill-slash fake-out -> full screen: the side grab."""
    install_preview_hooks(M, sc)
    rng = random.Random(9)
    frames = []
    key = PREVIEW_LURE if os.path.isfile(os.path.join(M.ENC, "lure_%s_0.png" % PREVIEW_LURE)) else "jig"
    cam0 = M.CAM
    M.CAM = V(cam0) * CAM_SCALE                              # camScale 1.2: the rest camera further back
    cam = M.setcam()
    cpos = V(M.CAM)
    M.CAM = cam0
    lure = V(lure)
    lpx = M.project(cam, lure)[:2]
    hy = M.project(cam, V((cpos.x, lure.y, cpos.z + 30.0)))[1]           # eye-level horizon (open water: no floor)
    cy = M.project(cam, V((cpos.x, lure.y + SURFACE_ABOVE, cpos.z + 30.0)))[1]    # the surface's far edge
    drag = V(M.PROF.get("drag_dir", (-0.6, 0, -0.8))).normalized()
    print("MOCK marlin lure px", [round(v, 1) for v in lpx], "horizon", round(hy, 1), "ceiling", round(cy, 1), "lure", key)

    def windowed(tag, lure_u, lure_frame=0, eyes_mult=1.0, line=True, bubbles=0, trail=False, fish_behind=False,
                 eyes_only=False, veil=0.0):
        lp = M.project(cam, lure_u)[:2]
        up = M.project(cam, lure_u + V(M.SETP.line_up))[:2] if line else None
        fish = M.render_fish(sc, cam, lure_u, tag, rim=not eyes_only, eyes_only=eyes_only)
        under = M.back_layers(lp, hy, ceil_row=cy)
        if veil > 0:
            under[..., :3] = under[..., :3] * (1 - veil) + M.R.hexrgb(M.PROF.get("veil", "#020a1a")) * veil
        if fish_behind:
            M.R.over(under, fish, 0, 0)
        M.put_lure(under, lp, key, lure_frame)
        if trail:                                              # the kona's bubble trail, behind it
            for i in range(5):
                bx = int(lp[0] - drag.x * 6 * (i + 1) + rng.randint(-1, 1) + 4 * (i + 1))
                by = int(lp[1] - 1 - i + rng.randint(-1, 1))
                M.R.over(under, M.load("bubble_s"), bx, by)
        under = M.front_fx(under, lp, up, rng, snow=14, bubbles=bubbles)
        if not fish_behind:
            M.R.over(under, fish, 0, 0)
        M.put_eyeshine(under, sc.eyes_px(cam), eyes_mult)
        return M.compose_window(*surface, under, M.WIN)

    rest = dict(Spine_B1=(0, 1, 0), Spine_B2=(0, 2, 0), Spine_B3=(0, 5, 0), Tail=(0, 12, 0), Tail_Upper=(0, 4, 0),
                Tail_Lower=(0, 4, 0))
    # 1) eyes: blue-white glints deep below and far, rising towards the lure (body off). The ocean camera shows the
    #    lure 62 % up the window: its lower edge is ~14 deg below eye level (rollout eyes0 (2.5, -4.5, 9) starts just
    #    under it, eyes1 is inside), so the mock uses a point on the way
    pos = V((2.3, -2.6, 8.5))
    to = lure - pos
    _place(M, sc, pos, _hdg(to), pitch=math.degrees(math.atan2(to.y, math.hypot(to.x, to.z))))
    sc.pose(**rest)
    sc.stripe_glow = 0.0
    frames.append(("eyes", windowed("b1", lure, eyes_only=True, veil=0.45)))
    # 2) approach: rising out of the blue ~5 m off, outside the visibility sphere - a silhouette in the water colour
    #    with the fog outline and the dim stripes, sail folded
    pos = V((2.9, -1.3, 5.8))
    _place(M, sc, pos, -112.0, pitch=16, bank=-6)
    sc.pose(Head=(0, -2, 0), Spine_B1=(0, 2, 0), Spine_B2=(0, 4, 0), Spine_B3=(0, 9, 0), Tail=(0, 16, 0),
            Tail_Upper=(0, 5, 0), Tail_Lower=(0, 5, 0))
    frames.append(("approach", windowed("b2", lure, lure_frame=1, eyes_mult=0.7, trail=True)))
    # 3) curious: a fast figure-eight pass crossing between the camera and the lure, sail half up, stripes flicker
    pos = V((0.15, -0.45, -1.35))
    _place(M, sc, pos, -104.0, pitch=4, bank=-12)
    sc.pose(Head=(0, 6, 0), Bill=(0, 2, 0), Spine_B1=(0, -2, 0), Spine_B2=(0, -4, 0), Spine_B3=(0, -8, 0),
            Tail=(0, -16, 0), Tail_Upper=(0, -5, 0), Tail_Lower=(0, -5, 0), Dorsal1=(35, 0, 0), Pec_L=(0, 12, 0),
            Pec_R=(0, -12, 0))
    sc.stripe_glow = 0.6
    frames.append(("pass", windowed("b3", lure + V((0.1, 0.05, 0)), lure_frame=0, line=False, trail=True)))
    # 4) excited: lit up, sail 75, pectorals flared, cruising 1 m behind the kona (bill tip ~0.5 m from it)
    s = sc.cm / 100.0
    fwd = V((-0.93, 0.0, -0.37)).normalized()                  # mostly side-on: the sail and stripes read
    root = lure - fwd * (0.5 + 0.5 * s) + V((0.05, -0.16, 0.0))
    _place(M, sc, root, _hdg(fwd), pitch=3, bank=4)
    sc.pose(Dorsal1=(75, 0, 0), Pec_L=(0, 38, 0), Pec_R=(0, -38, 0), Pel_L=(0, 10, 0), Pel_R=(0, -10, 0),
            Head=(0, -2, 0), Spine_B2=(0, 2, 0), Spine_B3=(0, 5, 0), Tail=(0, 10, 0), Jaw=(4, 0, 0))
    sc.stripe_glow = 1.0
    frames.append(("excited", windowed("b4", lure, lure_frame=0, trail=True, fish_behind=True)))
    # 5) the fake-out: a bill slash - the head whips 25 deg (bill 6), the kona tumbles sideways
    root = lure - fwd * (0.25 + 0.5 * s) + V((0.10, -0.10, 0.0))
    _place(M, sc, root, _hdg(fwd) + 6, pitch=2, bank=-6)
    sc.pose(Head=(0, 25, 0), Bill=(0, 6, 0), Dorsal1=(60, 0, 0), Pec_L=(0, 30, 0), Pec_R=(0, -30, 0),
            Spine_F=(0, -3, 0), Spine_B1=(0, -5, 0), Spine_B2=(0, -7, 0), Spine_B3=(0, -10, 0), Tail=(0, -18, 0))
    sc.stripe_glow = 0.8
    lure5 = lure + V((0.22, 0.04, 0.05))
    frames.append(("slash", windowed("b5", lure5, lure_frame=1, bubbles=3)))
    # 6) full screen at the bite: comes in from the side and grabs the kona crosswise; jaw 30, glow 1, sail up
    cpos6 = cpos * 0.48 + V((0.25, 0.25, 0.0))
    cam = M.setcam(pos=cpos6, target=V((0.5, -0.02, 0.15)), f=M.FPX * 1.15, pp=(M.VW / 2, M.VH / 2))
    hdg = -78.0                                                  # from screen right, side-on to the camera
    fw = V((math.sin(math.radians(hdg)), 0, math.cos(math.radians(hdg))))
    mouth = lure + V((0.02, 0.02, 0.0))
    mz = MOUTH_EMPTY[2]
    root = mouth - fw * (mz * s) + V((0, 0.012 * s, 0))
    _place(M, sc, root, hdg, pitch=-3, bank=-5)
    sc.pose(Jaw=(30, 0, 0), Dorsal1=(75, 0, 0), Head=(0, 4, 0), Bill=(0, 3, 0), Pec_L=(0, 32, 0), Pec_R=(0, -32, 0),
            Spine_B1=(0, 2, 0), Spine_B2=(0, 5, 0), Spine_B3=(0, 9, 0), Tail=(0, 16, 0), Tail_Upper=(0, 6, 0),
            Tail_Lower=(0, 6, 0))
    sc.stripe_glow = 1.0
    lx6, ly6, _ = M.project(cam, mouth)
    hy6 = M.project(cam, V((cpos6.x, lure.y, cpos6.z + 30.0)))[1]
    cy6 = M.project(cam, V((cpos6.x, lure.y + SURFACE_ABOVE, cpos6.z + 30.0)))[1]
    fish = M.render_fish(sc, cam, mouth, "b6")
    under = M.back_layers((lx6, ly6), hy6, ceil_row=cy6)
    M.put_lure(under, (lx6, ly6), key, 1, scale=2)
    M.R.over(under, fish, 0, 0)
    under = M.front_fx(under, None, None, rng, snow=18)
    bs = M.load("bubble_m")
    for i in range(7):                                                  # the grab churns the water
        M.R.over(under, bs, int(lx6 + rng.randint(-24, 24)), int(ly6 - 8 - i * 7 + rng.randint(-3, 3)))
    M.put_eyeshine(under, sc.eyes_px(cam))
    frames.append(("bite_full", M.compose_window(*surface, under, (0, 0, M.VW, M.VH), frame=False)))
    sc.stripe_glow = None                                               # the turntable follows the sail again
    return frames
