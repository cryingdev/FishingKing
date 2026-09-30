"""
Legend module: 실러캔스 COELACANTH (cave, backdrop "cave"). Loaded by hyb_legend3d.py (model) and
hyb_legend_preview.py (review mock). Spec: Docs/lures_legend_spec.md 3.3 (model), 2.5-2.7 (staging).

The first legend: its output (legend_coelacanth.fbx / _palette.json / _report.json) is the reference the builder
refactor was checked against - keep it byte-identical (palette / report) unless the spec changes. Its face outline
for the caption placement lives in Assets/Scripts/Fishing/Legend/Legend3D.cs (FaceOutline); later legends export
theirs through FACE_BONES (see legends/README.md).
"""
import math
import random
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
import hyb_legend_kit as K

V = K.V

# ============================================================================ identity
ID = "coelacanth"
MODEL = "legend_coelacanth"
ARMATURE = "Coelacanth"              # the FBX root node
PREFIX = "coel"                      # every material is coel_* (ActorArt caches materials globally by name)
CM = (120, 200)                      # in-game length range (GameDatabase), runtime scale = cm / 100
STAGE = "cave"                       # surface stage (preview backdrop)
BACKDROP = "cave"                    # EncounterDef.backdrop = encounter_sets/<BACKDROP>.py
PRESET = "cave"                      # hyb_core preset used while building / checking

# ============================================================================ palette (spec 3.3)
OUTLINE = "#0b1322"
RAMPS = {
    "coel_back": ["#1c2744", "#27375a", "#35466b"],
    "coel_side": ["#27375a", "#35466b", "#4d628a"],
    "coel_belly": ["#35466b", "#465a7c", "#5d7396"],
    "coel_fin": ["#18223c", "#27375a", "#3a4d74"],
    "coel_spots": ["#8e9ab4", "#c2cadb", "#e4eaf4"],
    "coel_mouth": ["#3a1a2a", "#6a2f44", "#8e4a5e"],
    "coel_teeth": ["#a8a090", "#d0c8b4", "#e8e0cc"],
    "coel_eye_ring": ["#05080c", "#0a0e14", "#1a2230"],
    "coel_eye_glow": ["#6ea060", "#b8ff8a", "#f0ffd8"],
}
# lure-light constants of the encounter (ActorToon _Abyss / _FogOutline, eyeshine): not materials (no "ramp" key,
# ActorArt.Palette skips the entry)
LURE_LIGHT = {"abyss": "#03080c", "fogOutline": "#1f5f70", "eyeCore": "#f0ffd8", "eyeGlow": "#b8ff8a",
              "frameLine": "#6affea"}

# ============================================================================ body
# body profile (Unity model space, metres): z, top y, bottom y, half-width x. Nose tip z +0.50; the body tapers into
# the middle lobe; the caudal lobes / middle lobe tip end at z -0.50. Deepest at z ~ +0.05 (under the first dorsal).
COEL_BODY = [
    (0.500, -0.004, -0.016, 0.007), (0.492, 0.014, -0.031, 0.016), (0.475, 0.030, -0.045, 0.024),
    (0.450, 0.044, -0.056, 0.030), (0.415, 0.056, -0.064, 0.035), (0.370, 0.067, -0.071, 0.040),
    (0.330, 0.075, -0.077, 0.043), (0.290, 0.082, -0.082, 0.046), (0.240, 0.089, -0.087, 0.049),
    (0.180, 0.096, -0.092, 0.052), (0.110, 0.101, -0.095, 0.054), (0.050, 0.103, -0.096, 0.054),
    (-0.010, 0.101, -0.094, 0.053), (-0.070, 0.096, -0.089, 0.050), (-0.130, 0.087, -0.082, 0.046),
    (-0.190, 0.075, -0.071, 0.040), (-0.250, 0.061, -0.058, 0.033), (-0.310, 0.047, -0.045, 0.025),
    (-0.360, 0.035, -0.034, 0.019), (-0.405, 0.024, -0.024, 0.014), (-0.445, 0.015, -0.015, 0.010),
]
OVER = 0.015            # each segment runs this far past its joint
INSET = 0.0015          # overlap zones sit this far inside the nominal surface (no z-fighting)
MOUTH_HINGE = (0.33, -0.025)    # (z, y) of the gape corner = the Jaw bone
MOUTH_FRONT = (0.49, -0.010)    # (z, y) of the mouth at the front = the Mouth empty
N_ARC = 9
BODY = K.Body(COEL_BODY, ("coel_back", "coel_side", "coel_belly"), nseg=16, back_pinch=0.12, inset=INSET, over=OVER,
              mouth=(MOUTH_HINGE, MOUTH_FRONT), n_arc=N_ARC, n_chord=2)

# bones: name -> (parent, head in Unity model space). Rest rotation = identity for every bone.
BONES = [
    ("Root", None, (0, 0, 0)),
    ("Spine.F", "Root", (0, 0, 0.04)),
    ("Head", "Spine.F", (0, 0, 0.24)),
    ("Skull", "Head", (0, 0.035, 0.34)),
    ("Jaw", "Head", (0, -0.025, 0.33)),
    ("Pec.L", "Head", (-0.045, -0.035, 0.26)),
    ("Pec.L.Fan", "Pec.L", (-0.075, -0.05, 0.20)),
    ("Pec.R", "Head", (0.045, -0.035, 0.26)),
    ("Pec.R.Fan", "Pec.R", (0.075, -0.05, 0.20)),
    ("Dorsal1", "Spine.F", (0, 0.085, 0.10)),
    ("Spine.B1", "Root", (0, 0, -0.04)),
    ("Pel.L", "Spine.B1", (-0.03, -0.075, -0.02)),
    ("Pel.L.Fan", "Pel.L", (-0.04, -0.105, -0.06)),
    ("Pel.R", "Spine.B1", (0.03, -0.075, -0.02)),
    ("Pel.R.Fan", "Pel.R", (0.04, -0.105, -0.06)),
    ("Spine.B2", "Spine.B1", (0, 0, -0.16)),
    ("Dorsal2", "Spine.B2", (0, 0.06, -0.18)),
    ("Dorsal2.Fan", "Dorsal2", (0, 0.104, -0.218)),
    ("Anal", "Spine.B2", (0, -0.06, -0.18)),
    ("Anal.Fan", "Anal", (0, -0.100, -0.218)),
    ("Spine.B3", "Spine.B2", (0, 0, -0.27)),
    ("Tail", "Spine.B3", (0, 0, -0.36)),
    ("Tail.Upper", "Tail", (0, 0.03, -0.37)),
    ("Tail.Mid", "Tail", (0, 0, -0.44)),
    ("Tail.Lower", "Tail", (0, -0.03, -0.37)),
]
EYE_C = (0.032, 0.02, 0.41)          # |x|, y, z of the eye centres (Eye.L at -x)
EYE_R = 0.0225                       # glow disc radius (0.045 m disc)


def build():
    """-> (parts {bone: Part}, empties {name: (parent bone, position U, facing U or None)}, info)."""
    B = BODY
    rng = random.Random(7)
    parts = {}

    def P(name):
        if name not in parts:
            parts[name] = K.Part(name)
        return parts[name]

    # ------------------------------------------------ body segments (joint z, rear -> front)
    J = {"Tail": -0.36, "Spine.B3": -0.27, "Spine.B2": -0.16, "Spine.B1": 0.0, "Spine.F": 0.24}
    # one inset extension per joint is enough: the FRONT segment runs OVER back into the rear one (a bend moves that
    # extension with the front part and it keeps bridging the convex side); the rear segment ends in a cap on the joint
    segs = [  # bone, nominal z range, extension at the rear / front
        ("Tail", (-0.445, -0.36), False, False),
        ("Spine.B3", (-0.36, -0.27), True, False),
        ("Spine.B2", (-0.27, -0.16), True, False),
        ("Spine.B1", (-0.16, 0.0), True, False),
        ("Spine.F", (0.0, 0.24), True, False),
        ("Head", (0.24, 0.33), True, True),       # the front extension (throat plug) hides under the skull / jaw
    ]
    bvh = {}
    for bone, (z0, z1), e0, e1 in segs:
        p = P(bone)
        rings = B.body_rings(z0, z1, e0, e1, B.full_ring)
        cap1 = "coel_mouth" if bone == "Head" else "coel_side"      # the throat, seen when the mouth opens
        if bone == "Head":
            rings[-1] = B.full_ring(0.36, INSET)                     # run the throat plug to z 0.36
        K.loft(p, rings, B.full_mat, cap0="coel_side", cap1=cap1)
        p.bm.normal_update()
        bvh[bone] = (BVHTree.FromBMesh(p.bm), (z0, z1))
    # ------------------------------------------------ skull / jaw shells (z 0.315 .. 0.50), split on the mouth line
    for bone, upper in (("Skull", True), ("Jaw", False)):
        p = P(bone)
        zs = [0.33 - OVER, 0.33 - 0.001] + B.stations(0.33, 0.50, 0.05, 0.012)
        rings, angs = [], []
        for i, z in enumerate(zs):
            pts, ths = B.clip_ring(z, INSET if i < 2 else 0.0, upper)
            rings.append(pts)
            angs.append(ths)

        def fm(i, k, angs=angs):
            if k >= N_ARC - 1:
                return "coel_mouth"
            return B.zone_mat((angs[i][k] + angs[i][k + 1]) / 2)
        K.loft(p, rings, fm, cap0="coel_mouth", cap1="coel_side")
        p.bm.normal_update()
        bvh[bone] = (BVHTree.FromBMesh(p.bm), (0.33, 0.50))
    # ------------------------------------------------ spots: ~30 flat decals per side (coel_spots)
    spots = []
    for sg in (1, -1):
        placed = []
        tries = 0
        while len(placed) < 26 and tries < 4000:
            tries += 1
            z = rng.uniform(-0.40, 0.47)
            # fewer spots where the body is small; none on the thin tail end
            if rng.random() > (B.sec(z)[1] / 0.0915) ** 0.6:
                continue
            v = rng.uniform(0.14, 0.9)
            th = math.asin(2 * v - 1)
            th = th if sg > 0 else math.pi - th
            c = B.pt(z, th)
            r = rng.uniform(0.007, 0.013) * (1.35 if rng.random() < 0.18 else 1.0)
            r *= min(1.0, B.sec(z)[1] / 0.06) ** 0.5
            if any((c - q).length < max(0.034, 1.6 * (r + rq)) for q, rq in placed):
                continue
            if any(abs(z - jz) < r + 0.012 for jz in list(J.values()) + [0.33]):
                continue
            ex = V((sg * EYE_C[0], EYE_C[1], EYE_C[2]))
            if (c - ex).length < EYE_R + r + 0.012:
                continue
            if z > 0.31 and abs(c.y - B.mouth_y(z)) < r + 0.006:
                continue
            if z > 0.265 and z < 0.285:
                continue
            placed.append((c, r))
            spots.append((z, th, c, r))
    info = {"spots": len(spots)}
    for z, th, c, r in spots:
        if z > 0.33:
            bone = "Skull" if c.y > B.mouth_y(z) else "Jaw"
        else:
            bone = next(b for b, (zz0, zz1), _, _ in segs if zz0 <= z < zz1)
        tree = bvh[bone][0]
        k = 5
        radii = [r * rng.uniform(0.72, 1.15) for _ in range(k)]
        K.decal(P(bone), tree, c, B.normal(z, th), radii, "coel_spots", spin=rng.uniform(0, 6.28))
    # ------------------------------------------------ gill-cover edge: a thin dark crescent on each side of the head
    for sg in (1, -1):
        pts, ns = [], []
        for j in range(8):
            th = math.radians(-58 + 150 * j / 7)            # from low on the cheek up over the flank
            th = th if sg > 0 else math.pi - th
            z = 0.272 - 0.012 * math.cos(math.radians(-58 + 150 * j / 7) * 1.1)
            pts.append(B.pt(z, th))
            ns.append(B.normal(z, th))
        K.strip_decal(P("Head"), bvh["Head"][0], pts, ns, 0.004, "coel_fin")
    # ------------------------------------------------ eyes: dark ring + pupil on the skull, glow lens = geo_Eye.*
    empties = {}
    for side, sg in (("L", -1), ("R", 1)):
        e = V((sg * EYE_C[0], EYE_C[1], EYE_C[2]))
        n = V((sg * 0.92, 0.18, 0.34)).normalized()           # out, a little up and forward
        a, b, n = K.frame_from(n, (0, 1, 0))
        K.ellipsoid(P("Skull"), e + n * 0.0005, (0.026, 0.026, 0.0045), (a, b, n), "coel_eye_ring", 10, 4, smooth=False)
        g = K.Part("Eye." + side)
        parts["Eye." + side] = g
        K.ellipsoid(g, e + n * 0.0025, (EYE_R, EYE_R, 0.0065), (a, b, n), "coel_eye_glow", 10, 4, smooth=True)
        K.ellipsoid(P("Skull"), e + n * (0.0025 + 0.0062), (0.0095, 0.0075, 0.0016), (a, b, n),
                    "coel_eye_ring", 8, 3, smooth=False)
        empties["Eye." + side] = ("Skull", e, n)
    empties["Mouth"] = ("Head", V((0, MOUTH_FRONT[1], MOUTH_FRONT[0])), None)
    # ------------------------------------------------ teeth: small cones on both lips (hidden when the mouth is shut)
    for bone, up in (("Skull", True), ("Jaw", False)):
        p = P(bone)
        for sg in (1, -1):
            for z, h in ((0.372, 0.006), (0.40, 0.007), (0.425, 0.008), (0.448, 0.008), (0.468, 0.009)):
                yc, ry, hw = B.sec(z)
                ym = B.mouth_y(z)
                x = hw * math.sqrt(max(0.0, 1 - ((ym - yc) / ry) ** 2)) - 0.0035
                base = V((sg * x, ym, z))
                tip = base + V((sg * -0.001, -h if up else h, 0.0015))
                rr = 0.0022
                vs = [p.bm.verts.new(base + V((rr * math.cos(t), 0, rr * math.sin(t)))) for t in (0.0, 2.1, 4.2)]
                tv = p.bm.verts.new(tip)
                fs = [p.face((vs[0], vs[1], vs[2]), "coel_teeth", False)]
                for i in range(3):
                    fs.append(p.face((vs[i], vs[(i + 1) % 3], tv), "coel_teeth", False))
                p.closed(fs)
    # ------------------------------------------------ first dorsal: a spiny fan, modelled RAISED then folded -60 deg
    # (rest = folded; Unity localRotation +60 deg about X raises it)
    d1 = V((0, 0.085, 0.10))
    raised = [(0.012, -0.012), (0.006, 0.02), (-0.002, 0.05), (-0.012, 0.078), (-0.025, 0.098), (-0.04, 0.101),
              (-0.055, 0.089), (-0.064, 0.066), (-0.066, 0.04), (-0.062, 0.015), (-0.055, -0.012)]
    fold = Matrix.Rotation(math.radians(-60), 3, "X")         # standard matrix = Unity's AngleAxis(-60, right)
    pts = [d1 + fold @ V((0, dy, dz)) for dz, dy in raised]
    K.plate(P("Dorsal1"), pts, (1, 0, 0), 0.004, "coel_fin")
    # ------------------------------------------------ lobed fins: fleshy scaled stalk + ray fan (paddle)
    fan_shape = [(-0.005, -0.013), (0.015, -0.024), (0.045, -0.028), (0.07, -0.02), (0.082, -0.004), (0.078, 0.012),
                 (0.058, 0.024), (0.03, 0.026), (0.008, 0.018), (-0.005, 0.01)]

    def lobed(stalk_bone, fan_bone, base, joint, blade_n, stalk_r, fan_len, fan_w):
        base, joint = V(base), V(joint)
        d = (joint - base).normalized()
        nb = V(blade_n).normalized()
        nb = (nb - d * nb.dot(d)).normalized()
        e = nb.cross(d).normalized()                          # across the blade
        p0 = base - d * 0.012
        p1 = joint + d * 0.006
        ra0, rb0 = stalk_r
        rings = []
        for u, sc in ((0.0, 1.0), (0.35, 1.02), (0.75, 0.86), (1.0, 0.72)):
            c = p0.lerp(p1, u)
            rings.append([c + e * (ra0 * sc * math.cos(2 * math.pi * k / 6)) + nb * (rb0 * sc * math.sin(2 * math.pi * k / 6))
                          for k in range(6)])
        K.loft(P(stalk_bone), rings, lambda i, k: "coel_side", smooth=True, cap0="coel_side", cap1="coel_side")
        k_l = fan_len / 0.082
        k_w = fan_w / 0.026
        pts = [joint + d * (u * k_l) + e * (w * k_w) for u, w in fan_shape]
        K.plate(P(fan_bone), pts, nb, 0.0035, "coel_fin")
    for side, sg in (("L", -1), ("R", 1)):
        lobed("Pec." + side, "Pec.%s.Fan" % side, (sg * 0.045, -0.035, 0.26), (sg * 0.075, -0.05, 0.20),
              (sg * 0.8, -0.55, 0.2), (0.013, 0.009), 0.085, 0.027)
        lobed("Pel." + side, "Pel.%s.Fan" % side, (sg * 0.03, -0.075, -0.02), (sg * 0.04, -0.105, -0.06),
              (sg * 0.95, -0.1, 0.2), (0.011, 0.008), 0.066, 0.022)
    lobed("Dorsal2", "Dorsal2.Fan", (0, 0.06, -0.18), (0, 0.104, -0.218), (1, 0, 0), (0.017, 0.009), 0.064, 0.023)
    lobed("Anal", "Anal.Fan", (0, -0.06, -0.18), (0, -0.100, -0.218), (1, 0, 0), (0.017, 0.009), 0.06, 0.022)
    # ------------------------------------------------ caudal fin: upper + lower lobes (sagittal plates) + middle lobe
    upper = [(-0.315, 0.043), (-0.335, 0.064), (-0.36, 0.093), (-0.385, 0.122), (-0.405, 0.140), (-0.425, 0.141),
             (-0.441, 0.126), (-0.451, 0.099), (-0.456, 0.068), (-0.459, 0.036), (-0.45, 0.011), (-0.40, 0.014),
             (-0.35, 0.025)]
    K.plate(P("Tail.Upper"), [V((0, y, z)) for z, y in upper], (1, 0, 0), 0.004, "coel_fin")
    K.plate(P("Tail.Lower"), [V((0, -y * 0.94, z)) for z, y in upper][::-1], (1, 0, 0), 0.004, "coel_fin")
    mid = P("Tail.Mid")
    rings = []
    for z, ry, rx in ((-0.428, 0.017, 0.0115), (-0.445, 0.0145, 0.0095), (-0.468, 0.0095, 0.0065),
                      (-0.487, 0.0045, 0.0035)):
        rings.append([V((rx * math.cos(2 * math.pi * k / 8), ry * math.sin(2 * math.pi * k / 8), z)) for k in range(8)])
    K.loft(mid, rings, lambda i, k: B.zone_mat(2 * math.pi * (k + 0.5) / 8), cap0="coel_side", cap1="coel_side")
    tuft = [(-0.455, 0.004), (-0.468, 0.017), (-0.484, 0.022), (-0.496, 0.013), (-0.5, 0.0), (-0.496, -0.013),
            (-0.484, -0.022), (-0.468, -0.017), (-0.455, -0.004)]
    K.plate(mid, [V((0, y, z)) for z, y in tuft], (1, 0, 0), 0.003, "coel_fin")
    info["jointsZ"] = J
    info["mouthLine"] = {"hinge_zy": MOUTH_HINGE, "front_zy": MOUTH_FRONT}
    return parts, empties, info


# ============================================================================ check renders (hyb_legend3d.check)
# Unity localRotation Euler degrees per bone (bones the model lacks are skipped). These are the builder defaults.
CHECK_OPEN = {"Jaw": (40, 0, 0), "Skull": (-8, 0, 0), "Dorsal1": (60, 0, 0),
              "Pec.L": (0, -20, 0), "Pec.L.Fan": (0, -15, 0), "Pec.R": (0, 20, 0), "Pec.R.Fan": (0, 15, 0)}
CHECK_BEND = {"Spine.F": (0, -6, 0), "Head": (0, -12, 0), "Spine.B1": (0, 10, 0), "Spine.B2": (0, 15, 0),
              "Spine.B3": (0, 15, 0), "Tail": (0, 15, 0), "Tail.Upper": (0, 8, 0), "Tail.Lower": (0, 8, 0),
              "Tail.Mid": (0, 12, 0), "Pec.L.Fan": (0, -15, 0), "Pec.R.Fan": (0, 15, 0)}


# ============================================================================ review mock (hyb_legend_preview.py)
PREVIEW_PREFIX = "legend"            # legacy file names: legend_encounter_mock.png, legend_mock_<n>_<beat>.png
PREVIEW_LURE = "egi"                 # lure billboard key (Encounter/lure_<key>_0/1)
PREVIEW_CM = 160                     # size shown in the mock
LURE_R = 3.0                         # lure-light radius (EncounterDef.lightGlow)
# turntable poses (row 1: open / swim bend, row 2 lit by the lure, row 3 game scale)
TURNTABLE = dict(
    open={"Jaw": (40, 0, 0), "Skull": (-8, 0, 0), "Dorsal1": (60, 0, 0), "Pec.L": (0, -20, 0), "Pec.R": (0, 20, 0),
          "Pec.L.Fan": (0, -15, 0), "Pec.R.Fan": (0, 15, 0), "Pel.L": (0, 15, 0), "Pel.R": (0, -15, 0),
          "Dorsal2": (0, 0, 20), "Anal": (0, 0, -20)},
    bend={"Spine.F": (0, -2, 0), "Head": (0, -8, 0), "Spine.B1": (0, 6, 0), "Spine.B2": (0, 10, 0),
          "Spine.B3": (0, 14, 0), "Tail": (0, 15, 0), "Tail.Mid": (0, 10, 0), "Pec.L": (0, -25, 0),
          "Pec.R": (0, 25, 0), "Pel.L": (0, 25, 0), "Pel.R": (0, -25, 0)},
    lit={"Dorsal1": (30, 0, 0), "Pec.L": (0, -15, 0), "Pec.R": (0, 15, 0)},
    game={"Dorsal1": (30, 0, 0)},
)


def preview_beats(M, sc, cam, lure, surface):
    """The six mock beats (spec 4.5 capture moments) -> [(name, 480x270 frame)]. M = the hyb_legend_preview module
    (its helpers), sc = its Scene (the imported model), cam = the rest camera, lure = the lure rest point (set frame),
    surface = (img, angler, angler_xy, rod tip, line entry) of the dimmed surface."""
    rng = random.Random(4)
    frames = []
    tgt = M.cam_target()
    lx, ly, _ = M.project(cam, lure)
    hx, hy, _ = M.project(cam, V((0, 0, 30.0)) + V((M.CAM.x, 0, M.CAM.z)))           # the floor horizon
    up = M.project(cam, lure + V(M.SETP.line_up))[:2]
    print("MOCK lure px", round(lx, 1), round(ly, 1), "window", M.WIN, "horizon row", round(hy, 1))
    # 1) eyes in the dark: body renderers off, eyes ~9 m out in the deep water (centre right), facing the lure
    sc.place((5.0, 0.4, 5.2), math.degrees(math.atan2(-5.0, -5.2)))
    sc.pose(Spine_B1=(0, 4, 0), Spine_B2=(0, 6, 0), Tail=(0, 8, 0))
    fish = M.render_fish(sc, cam, lure, "m1", rim=False, eyes_only=True)
    under = M.back_layers((lx, ly), hy)
    M.R.over(under, fish, 0, 0)
    under = M.front_fx(under, (lx, ly), up, rng, snow=10)
    M.put_lure(under, (lx, ly), PREVIEW_LURE, 0)
    M.put_eyeshine(under, sc.eyes_px(cam))
    frames.append(("eyes", M.compose_window(*surface, under, M.WIN)))

    def windowed(tag, lure_u, lure_frame=0, eyes_mult=1.0, fish_over_lure=True, bubbles=0, line=True):
        lpx = M.project(cam, lure_u)[:2]
        upx = M.project(cam, lure_u + V(M.SETP.line_up))[:2]
        fish = M.render_fish(sc, cam, lure_u, tag)
        under = M.back_layers(lpx, hy)
        if fish_over_lure:
            M.put_lure(under, lpx, PREVIEW_LURE, lure_frame)
        under = M.front_fx(under, lpx, upx if line else None, rng, snow=12, bubbles=bubbles)
        M.R.over(under, fish, 0, 0)
        if not fish_over_lure:
            M.put_lure(under, lpx, PREVIEW_LURE, lure_frame)
        M.put_eyeshine(under, sc.eyes_px(cam), eyes_mult)
        return M.compose_window(*surface, under, M.WIN)

    def orbit_heading(pos, ccw=True):
        rad = V((pos.x, 0, pos.z)).normalized()
        return math.degrees(math.atan2(-rad.z, rad.x) if ccw else math.atan2(rad.z, -rad.x))

    # 2) approach (wary): S-curve in from the deep water towards the orbit entry, 2.4 m from the lure: outside the
    #    light, a black silhouette (abyss body, fog outline #1f5f70, cyan top rim); only the spots facing the lure glint
    pos = V((1.9, 0.45, 1.5))
    sc.place(pos, math.degrees(math.atan2(-pos.x, -pos.z)) + 25, bank=-8)
    sc.pose(Spine_F=(0, -3, 0), Head=(0, -8, 0), Spine_B1=(0, 5, 0), Spine_B2=(0, 9, 0), Spine_B3=(0, 11, 0),
            Tail=(0, 12, 0), Tail_Upper=(0, 6, 0), Tail_Lower=(0, 6, 0), Dorsal1=(60, 0, 0),
            Pec_L=(0, -20, 0), Pec_R=(0, 25, 0), Pel_L=(0, 15, 0), Pel_R=(0, -15, 0))
    frames.append(("approach", windowed("m2", lure, lure_frame=1, eyes_mult=0.6)))
    # 3) tease, curious: on the 1.3 m orbit on the far side of the lure, flank to the lure (lit), head tracking it
    pos = V((0.75, 0.32, 0.95))
    sc.place(pos, orbit_heading(pos) - 6, bank=6)
    sc.pose(Spine_F=(0, -6, 0), Head=(0, -18, 0), Spine_B1=(0, 4, 0), Spine_B2=(0, 6, 0), Spine_B3=(0, 7, 0),
            Tail=(0, 9, 0), Tail_Upper=(0, 5, 0), Tail_Lower=(0, 5, 0), Dorsal1=(0, 0, 0),
            Pec_L=(0, 22, 0), Pec_R=(0, -18, 0), Pec_L_Fan=(0, 12, 0), Pec_R_Fan=(0, -12, 0),
            Pel_L=(0, -15, 0), Pel_R=(0, 15, 0), Dorsal2=(0, 0, -18), Anal=(0, 0, 18))
    lure3 = V((0, 0.28, 0))                                                    # hopped up on a flick, falling
    frames.append(("tease", windowed("m3", lure3, lure_frame=1, bubbles=4)))
    # 4) the pass: the orbit crosses between the camera and the lure ~1.4 m from the camera (the 3D moment) -
    #    backlit by the lure behind it, the silhouette blocks the halo for a moment
    pos = V((-0.55, 0.32, -1.25))
    sc.place(pos, orbit_heading(pos) + 8, bank=-6)
    sc.pose(Spine_F=(0, -6, 0), Head=(0, -16, 0), Spine_B1=(0, 3, 0), Spine_B2=(0, 5, 0), Spine_B3=(0, 6, 0),
            Tail=(0, 8, 0), Tail_Upper=(0, 5, 0), Tail_Lower=(0, 5, 0), Dorsal1=(15, 0, 0),
            Pec_L=(0, 18, 0), Pec_R=(0, -22, 0), Pec_L_Fan=(0, 12, 0), Pec_R_Fan=(0, -12, 0),
            Pel_L=(0, -15, 0), Pel_R=(0, 15, 0), Dorsal2=(0, 0, 18), Anal=(0, 0, -18), Jaw=(4, 0, 0))
    frames.append(("pass", windowed("m4", V((0, 0.3, 0)), lure_frame=0, line=False)))
    # 5) excited: hovers 0.6 m from the lure facing it, fins flared, dorsal half up, jaw mouthing, eyes full
    s = sc.cm / 100.0
    hdg = -62.0                                            # right of the lure facing it, side-on / slight 3/4
    fwd = V((math.sin(math.radians(hdg)), 0, math.cos(math.radians(hdg))))
    lure5 = V((0, 0.06, 0))
    root = lure5 - fwd * (0.6 + 0.49 * s) + V((0, 0.01 * s + 0.04, 0))
    sc.place(root, hdg, bank=0)
    sc.pose(Jaw=(8, 0, 0), Dorsal1=(30, 0, 0), Head=(4, 0, 0), Spine_B1=(0, 2, 0), Spine_B2=(0, 3, 0), Tail=(0, -4, 0),
            Pec_L=(0, -32, 0), Pec_R=(0, 32, 0), Pec_L_Fan=(0, -15, 0), Pec_R_Fan=(0, 15, 0),
            Pel_L=(0, 25, 0), Pel_R=(0, -25, 0), Dorsal2=(0, 0, 10), Anal=(0, 0, -10))
    frames.append(("excited", windowed("m5", lure5, lure_frame=0)))
    # 6) full screen at the bite: lunge onto the lure, jaw 40, skull up 8, f x1.5, camera dollied towards the head
    cpos = V((-0.46, 0.34, -1.12))
    cam = M.setcam(pos=cpos, target=V((0.32, 0.1, 0.02)), f=M.FPX * 1.5)
    hdg = -84.0                                            # from the deep water (screen right), 3/4 to the camera
    fwd = V((math.sin(math.radians(hdg)), 0, math.cos(math.radians(hdg))))
    mouth = V((0, 0.1, 0))
    root = mouth - fwd * (0.49 * s) - V((0, (-0.01) * s, 0)) - fwd * 0.03
    sc.place(root, hdg, bank=-4)
    sc.pose(Jaw=(40, 0, 0), Skull=(-8, 0, 0), Dorsal1=(30, 0, 0), Head=(-4, 3, 0), Spine_B1=(0, 5, 0),
            Spine_B2=(0, 8, 0), Spine_B3=(0, 9, 0), Tail=(0, 12, 0), Pec_L=(0, -30, 0), Pec_R=(0, 30, 0),
            Pec_L_Fan=(0, -15, 0), Pec_R_Fan=(0, 15, 0), Pel_L=(0, 20, 0), Pel_R=(0, -20, 0))
    lx4, ly4, _ = M.project(cam, mouth + V((0, -0.02, 0)) + fwd * 0.02)
    hx4, hy4, _ = M.project(cam, V((0, 0, 30.0)) + V((cpos.x, 0, cpos.z)))
    fish = M.render_fish(sc, cam, mouth, "m6")
    under = M.back_layers((lx4, ly4), hy4)
    M.put_lure(under, (lx4, ly4), PREVIEW_LURE, 0, scale=2)
    M.R.over(under, fish, 0, 0)
    under = M.front_fx(under, None, None, rng, snow=16)
    for i in range(6):                                                          # marine snow sucked in
        a = i / 6 * 6.28
        x, y = int(lx4 + math.cos(a) * (14 + i * 3)), int(ly4 + math.sin(a) * (10 + i * 2))
        if 0 <= x < M.VW and 0 <= y < M.VH:
            under[y, x, :3] = M.R.hexrgb("#bfe8ee")
    M.put_eyeshine(under, sc.eyes_px(cam))
    frames.append(("bite_full", M.compose_window(*surface, under, (0, 0, M.VW, M.VH), frame=False)))
    return frames
