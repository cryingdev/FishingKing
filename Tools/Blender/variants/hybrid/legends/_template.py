"""
TEMPLATE legend module - copy to legends/<fish_id>.py and replace everything (see legends/README.md and
Docs/legends_rollout.md for your legend's row). Files starting with "_" are skipped by --all; this one still builds
(`hyb_legend3d.py -- _template`) as a smoke test of the generic path: a plain fusiform fish with the REQUIRED
generic bones, a face outline (FACE_BONES) and no species bones.
"""
import math
from mathutils.bvhtree import BVHTree  # noqa: F401  (decals: BVHTree.FromBMesh(part.bm))
import hyb_legend_kit as K

V = K.V

# ============================================================================ identity
ID = "_template"
MODEL = "legend_template"           # legend_<fish_id>
ARMATURE = "Template"               # FBX root node (the species, CamelCase)
PREFIX = "tmpl"                     # material prefix: lower-case, unique per legend (coel, gcarp, arap, stur, marl, gw)
CM = (100, 200)                     # in-game length range (GameDatabase)
STAGE = "cave"                      # surface stage id
BACKDROP = "cave"                   # encounter_sets/<BACKDROP>.py
PRESET = "cave"                     # hyb_core preset while building

# ============================================================================ palette
OUTLINE = "#0b1322"                 # one ink colour for every material
RAMPS = {                           # "<prefix>_<slot>": [dark, mid, light]; *_glow = unlit (shows the light tone)
    "tmpl_back": ["#1c2744", "#27375a", "#35466b"],
    "tmpl_side": ["#27375a", "#35466b", "#4d628a"],
    "tmpl_belly": ["#35466b", "#465a7c", "#5d7396"],
    "tmpl_fin": ["#18223c", "#27375a", "#3a4d74"],
    "tmpl_mouth": ["#3a1a2a", "#6a2f44", "#8e4a5e"],
    "tmpl_eye_ring": ["#05080c", "#0a0e14", "#1a2230"],
    "tmpl_eye_glow": ["#6ea060", "#b8ff8a", "#f0ffd8"],
}
LURE_LIGHT = {"abyss": "#03080c", "fogOutline": "#1f5f70", "eyeCore": "#f0ffd8", "eyeGlow": "#b8ff8a",
              "frameLine": "#6affea"}
FACE_BONES = ["Head", "Jaw"]        # parts whose extreme points make the face outline (palette "_face")
RIG = None                          # e.g. {"protrude": [{"bone": "Lips", "move": [0, -0.012, 0.03], "tilt": [15, 0, 0]}]}

# ============================================================================ body
TABLE = [  # z, top y, bottom y, half-width x (nose -> tail)
    (0.500, 0.000, -0.012, 0.006), (0.480, 0.025, -0.035, 0.020), (0.440, 0.048, -0.055, 0.032),
    (0.380, 0.066, -0.070, 0.041), (0.300, 0.080, -0.082, 0.047), (0.200, 0.090, -0.090, 0.050),
    (0.080, 0.094, -0.092, 0.050), (-0.040, 0.088, -0.086, 0.046), (-0.160, 0.072, -0.070, 0.037),
    (-0.260, 0.052, -0.050, 0.026), (-0.340, 0.034, -0.033, 0.017), (-0.420, 0.020, -0.020, 0.010),
    (-0.440, 0.018, -0.018, 0.009),
]
HINGE, FRONT = (0.32, -0.030), (0.49, -0.008)          # mouth line: (z, y) of the gape corner / the front
BODY = K.Body(TABLE, ("tmpl_back", "tmpl_side", "tmpl_belly"), mouth=(HINGE, FRONT))
BONES = [  # (name, parent, head in Unity model space); rest rotation is identity for all
    ("Root", None, (0, 0, 0)),
    ("Spine.F", "Root", (0, 0, 0.02)),
    ("Head", "Spine.F", (0, 0, 0.22)),
    ("Jaw", "Head", (0, HINGE[1], HINGE[0])),
    ("Pec.L", "Head", (-0.045, -0.045, 0.25)),
    ("Pec.R", "Head", (0.045, -0.045, 0.25)),
    ("Dorsal1", "Spine.F", (0, 0.092, 0.06)),
    ("Spine.B1", "Root", (0, 0, -0.02)),
    ("Pel.L", "Spine.B1", (-0.03, -0.08, -0.04)),
    ("Pel.R", "Spine.B1", (0.03, -0.08, -0.04)),
    ("Spine.B2", "Spine.B1", (0, 0, -0.14)),
    ("Anal", "Spine.B2", (0, -0.06, -0.20)),
    ("Spine.B3", "Spine.B2", (0, 0, -0.26)),
    ("Tail", "Spine.B3", (0, 0, -0.36)),
    ("Tail.Upper", "Tail", (0, 0.01, -0.42)),
    ("Tail.Lower", "Tail", (0, -0.01, -0.42)),
]
EYE = (0.034, 0.022, 0.40)


def build():
    """-> (parts {bone or empty name: K.Part}, empties {name: (parent bone, position, facing or None)}, info)."""
    B = BODY
    parts = {}

    def P(name):
        return parts.setdefault(name, K.Part(name))

    # body segments, rear -> front: the FRONT segment carries the inset extension back over each joint
    for bone, (z0, z1), e0 in (("Tail", (-0.44, -0.36), False), ("Spine.B3", (-0.36, -0.26), True),
                               ("Spine.B2", (-0.26, -0.14), True), ("Spine.B1", (-0.14, 0.0), True),
                               ("Spine.F", (0.0, 0.22), True), ("Head", (0.22, HINGE[0]), True)):
        rings = B.body_rings(z0, z1, e0, bone == "Head", B.full_ring)
        K.loft(P(bone), rings, B.full_mat, cap0="tmpl_side", cap1="tmpl_mouth" if bone == "Head" else "tmpl_side")
    # upper head shell (on Head) and lower jaw shell (on Jaw), split on the mouth line
    for bone, upper in (("Head", True), ("Jaw", False)):
        zs = [HINGE[0] - B.over, HINGE[0] - 0.001] + B.stations(HINGE[0], 0.50, 0.05, 0.012)
        rings, angs = [], []
        for i, z in enumerate(zs):
            pts, ths = B.clip_ring(z, B.inset if i < 2 else 0.0, upper)
            rings.append(pts)
            angs.append(ths)
        K.loft(P(bone), rings, lambda i, k, a=angs: "tmpl_mouth" if k >= B.n_arc - 1 else B.zone_mat((a[i][k] + a[i][k + 1]) / 2),
               cap0="tmpl_mouth", cap1="tmpl_side")
    # eyes: a dark ring on the head, the glow lens = geo_Eye.* (its own part, rides the Eye's bone)
    empties = {}
    for side, sg in (("L", -1), ("R", 1)):
        e = V((sg * EYE[0], EYE[1], EYE[2]))
        a, b, n = K.frame_from(V((sg * 0.92, 0.15, 0.35)), (0, 1, 0))
        K.ellipsoid(P("Head"), e + n * 0.0005, (0.02, 0.02, 0.004), (a, b, n), "tmpl_eye_ring", 10, 4, smooth=False)
        K.ellipsoid(P("Eye." + side), e + n * 0.0025, (0.016, 0.016, 0.005), (a, b, n), "tmpl_eye_glow", 10, 4)
        empties["Eye." + side] = ("Head", e, n)
    empties["Mouth"] = ("Head", V((0, FRONT[1], FRONT[0])), None)
    # fins: flat plates (modelled at rest; a raisable fin is modelled RAISED and folded, see coelacanth Dorsal1)
    for side, sg in (("L", -1), ("R", 1)):
        K.plate(P("Pec." + side), [V((sg * (0.045 + 0.05 * u), -0.045 - 0.02 * u, 0.25 - 0.07 * u + 0.02 * w))
                                   for u, w in ((0, 0), (1, -0.6), (1, 0.6), (0, 1))], (sg * 0.3, 1, 0), 0.003, "tmpl_fin")
        K.plate(P("Pel." + side), [V((sg * 0.03, -0.08 - 0.03 * u, -0.04 - 0.05 * u + 0.02 * w))
                                   for u, w in ((0, 0), (1, -0.5), (1, 0.5), (0, 1))], (1, 0, 0), 0.003, "tmpl_fin")
    K.plate(P("Dorsal1"), [V((0, y, z)) for z, y in ((0.10, 0.088), (0.05, 0.15), (-0.02, 0.13), (-0.06, 0.085))],
            (1, 0, 0), 0.003, "tmpl_fin")
    K.plate(P("Anal"), [V((0, y, z)) for z, y in ((-0.17, -0.07), (-0.21, -0.11), (-0.26, -0.09), (-0.27, -0.05))],
            (1, 0, 0), 0.003, "tmpl_fin")
    K.plate(P("Tail.Upper"), [V((0, y, z)) for z, y in ((-0.40, 0.015), (-0.47, 0.09), (-0.50, 0.10), (-0.46, 0.0))],
            (1, 0, 0), 0.003, "tmpl_fin")
    K.plate(P("Tail.Lower"), [V((0, y, z)) for z, y in ((-0.46, 0.0), (-0.50, -0.10), (-0.47, -0.09), (-0.40, -0.015))],
            (1, 0, 0), 0.003, "tmpl_fin")
    return parts, empties, {"note": "template"}


# ============================================================================ check renders / review mock (optional)
CHECK_OPEN = {"Jaw": (35, 0, 0), "Dorsal1": (0, 0, 0), "Pec.L": (0, -20, 0), "Pec.R": (0, 20, 0)}
PREVIEW_LURE = "egi"
PREVIEW_CM = 150
LURE_R = 3.0
TURNTABLE = dict(open={"Jaw": (35, 0, 0), "Pec.L": (0, -20, 0), "Pec.R": (0, 20, 0)},
                 bend={"Spine.F": (0, -2, 0), "Head": (0, -8, 0), "Spine.B1": (0, 6, 0), "Spine.B2": (0, 10, 0),
                       "Spine.B3": (0, 14, 0), "Tail": (0, 15, 0)},
                 lit={}, game={})


def preview_beats(M, sc, cam, lure, surface):
    """Minimal beats: eyes in the dark, then the fish side-on near the lure (write your own; see coelacanth.py)."""
    import random
    rng = random.Random(4)
    lx, ly, _ = M.project(cam, lure)
    hy = M.project(cam, V((0, 0, 30.0)) + V((M.CAM.x, 0, M.CAM.z)))[1]
    frames = []
    for tag, eyes_only, pos, hdg in (("eyes", True, (5.0, 0.4, 5.2), -136.0), ("near", False, (0.9, 0.3, 0.9), -60.0)):
        sc.place(pos, hdg)
        sc.pose(Spine_B2=(0, 6, 0), Tail=(0, 8, 0))
        fish = M.render_fish(sc, cam, lure, "t_" + tag, rim=not eyes_only, eyes_only=eyes_only)
        under = M.back_layers((lx, ly), hy)
        M.put_lure(under, (lx, ly), PREVIEW_LURE, 0)
        M.R.over(under, fish, 0, 0)
        under = M.front_fx(under, (lx, ly), None, rng)
        M.put_eyeshine(under, sc.eyes_px(cam))
        frames.append((tag, M.compose_window(*surface, under, M.WIN)))
    return frames
