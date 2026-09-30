"""
hybrid - the angler seen from behind (retro16 design: ~6 heads, bucket hat, brick flannel, olive vest,
slate trousers, creel; retro16 pose table), lit by the "lake" preset: key from the sun side (right,
lifted) so the back sits in the mid tone, cooler hue-shifted shadow side on the left, a SUBTLE warm rim
on the OUTER right/top silhouette only (fading down the body), and a hue-shifted selective outline
(never near-black ink). The palette is only mildly graded, so the one sprite set suits every preset.
Rendered through the shared stage camera (fk_persp.setup_camera(0.0)), cropped to 96x112 with the feet
10 px above the bottom (same crop rules as fk_character.py).

Outputs (Tools/Blender/_tmp/variants/hybrid/character/):
  angler_<pose>.png  for idle, aim, cast, reel, reel2, fight, cheer
  character.json     same fields as Data/character.json (ROD hand = LEFT hand: px, 3D; rod dir, crop)
  ../character_sheet.png (4x on mid-grey)
  ../character_grip.json (debug: where the right hand lands vs. the game's reel knob, stage px)

The rod is held in the LEFT hand and leans up-left on screen; the RIGHT hand cranks the reel (reel / reel2 /
fight grip the crank knob where the game draws it).

Run: blender -b --python variants/hybrid/hyb_character.py [-- pose ...] [-- --dry]  (--dry: IK report, no render)
"""
import os
import sys
import math
import json
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

OUT = os.path.join(R.OUT, "character")
CROP_W, CROP_H, FEET_PX = 96, 112, 10
TURN = 80.0

PR = R.use_preset("lake")
G = R.grade_hex
GRADE = 0.55          # mild: the one character set is shown on every stage / preset
RIM_SCALE = 0.62      # x preset rim strength: subtle enough for every preset

# ---------------------------------------------------------------- curated 17-colour palette (graded mildly)
PAL = dict(
    deep=G("#2a2638", amount=GRADE),       # darkest outline tone (hue-shifted violet-navy, not ink)
    skin=G(["#a95c4a", "#d08c68", "#f0bc94"], amount=GRADE),
    leather=G(["#3c2826", "#6e5a3c"], amount=GRADE),
    khaki=G(["#6e5a3c", "#a48c60", "#d4c08e"], amount=GRADE),
    olive=G(["#343e2c", "#56623c", "#808c56"], amount=GRADE),
    shirt=G(["#4a2432", "#7c3a3c", "#a95c4a"], amount=GRADE),
    trou=G(["#2a2c38", "#434858", "#606878"], amount=GRADE),
)
B3 = [0.45, 0.74]     # tone boundaries (half-lambert) for 3-tone ramps
B3V = [0.55, 0.8]     # vest: the back splits into a lit left half and a mid right half
B2 = [0.6]
# key light from the sun side (right), lifted and nearly level with the back: the back reads in the mid
# tone, the right flank / hat top are lit warm, the left side falls into the cool shadow tone
CL = Vector((0.72, -0.12, 0.62)).normalized()

# ---------------------------------------------------------------- rod in the LEFT hand, reel cranked by the RIGHT
# Rod direction per pose in Unity axes (x right, y up, z forward): fk_character.POSES with rx negated, so the rod
# held in the left hand leans up-LEFT on screen (up / forward components unchanged). Unity draws the rod from the
# left hand (character.json hx/hy/hz) along this direction.
ROD = {
    "idle": (-0.22, 0.72, 0.66),
    "aim": (-0.25, 0.72, -0.64),
    "cast": (-0.08, 0.3, 0.95),
    "reel": (-0.2, 0.62, 0.76),
    "reel2": (-0.2, 0.62, 0.76),
    "fight": (-0.12, 0.9, 0.42),
    "cheer": (-0.45, 0.75, 0.3),
}

# Where the game puts the reel (Assets/Scripts/Fishing/Angler.cs, mirrored here for the unloaded starting rod):
# the rod is a cubic curve from the hand (c1 at 0.5 L, c2 at 0.7 L, tip at L), the 16 px reel sprite hangs off
# its underside at s = 0.33 of the curve (~0.46 m along the straight rod), foot top 1.5 px under the rod, the
# sprite centre 5 px below that, tilted 35 % towards the rod's normal. The right hand is aimed at the crank knob
# of that sprite ON SCREEN (knob centres of reel_basic_w0 / _w1, px from the sprite centre, x right / y up, as
# printed by fk_items.py worldreels).
GAME_ROD_LEN = 3.0
GAME_REEL_S, GAME_REEL_FOOT_PX, GAME_REEL_TILT = 0.33, 5.0, 0.35
REEL_KNOB_PX = ((4.02, 3.58), (4.96, -2.64))   # w0: knob up, w1: knob down

# ---------------------------------------------------------------- poses for the ~6-head rig
# lh = LEFT (rod) hand target, an absolute point in the character frame (x forward, y left (+) / right (-),
# z up, feet at 0). While fishing it sits low at the left hip, just outside the torso, with the elbow tucked
# back (lpole) so the upper arm hangs clear of the reel the game draws ~14 px higher up the rod; the fist peeks
# out under the elbow.
# rh = RIGHT hand: an absolute point, or a grip resolved against the game's rod / reel on screen:
#   dict(knob=k, fwd=x)   the fist centred on the crank knob (k 0 = up (w0), 1 = down (w1), in between = lerp),
#                         placed at char-frame depth x (it only moves along the camera ray, so it stays on the knob)
#   dict(sprite=(u, v), fwd=x)  any point of the reel sprite (px from its centre, before the game's tilt)
#   dict(s=s)             on the rod s metres from the left hand (s < 0: the rear grip behind it; exact 3D)
#   off=(dx, dy)          extra screen px shift of the fist.
# Reaching across the chest to the reel: rsh rolls the right shoulder forward / in (protraction) and rpole puts
# the elbow in front of the sternum, so the forearm shows crossing over the left shoulder.
# back: lean back (deg, towards the camera); crouch: hip drop (m); feet: ((x, y) right, (x, y) left);
# tilt: pelvis tilt (deg, right hip up); farknee: extra left-knee bend (deg); lpole / rpole: elbow pole vectors.
TUCK = (-0.6, 0.5, -1.0)          # left elbow back / down along the side
CROSS = (1.0, -0.4, -0.8)         # right elbow in front of the chest
POSES = {
    # ready: right hand resting on the reel's handle side, just under the knob
    "idle": dict(lh=(0.24, 0.33, 0.86), rh=dict(sprite=(3.0, 1.5), fwd=0.22), rsh=(0.09, 0.07, -0.01),
                 rpole=CROSS, lpole=TUCK, back=0, feet=((0.1, -0.1), (-0.04, 0.1)), tilt=3.0, farknee=8),
    # rod swung back over the left shoulder (drawn in front of the body): right hand below on the rear grip
    "aim": dict(lh=(-0.14, 0.27, 1.72), rh=dict(s=-0.13), back=8, feet=((0.08, -0.11), (-0.1, 0.11))),
    # rod thrown forward: both hands forward on the grip, the right one behind the left
    "cast": dict(lh=(0.5, 0.40, 1.33), rh=dict(s=-0.14), rsh=(0.07, 0.04, 0.0), rpole=CROSS,
                 back=-10, feet=((0.16, -0.11), (-0.06, 0.1)), farknee=10),
    # reeling: the rod hand stays put, the right hand follows the knob (w0 up / w1 down, alternated by the game)
    "reel": dict(lh=(0.24, 0.33, 0.88), rh=dict(knob=0, fwd=0.24), rsh=(0.09, 0.07, -0.01), rpole=CROSS,
                 lpole=TUCK, back=3, feet=((0.1, -0.11), (-0.03, 0.11))),
    "reel2": dict(lh=(0.24, 0.33, 0.88), rh=dict(knob=1, fwd=0.21), rsh=(0.09, 0.07, -0.01), rpole=CROSS,
                  lpole=TUCK, back=3, feet=((0.1, -0.11), (-0.03, 0.11))),
    # leaning back, rod high: the game shows the knob-up frame (w0) here, so the fist holds the handle a third
    # of the way down from it (mid height would leave a ~3 px gap to the drawn knob)
    "fight": dict(lh=(0.27, 0.39, 0.9), rh=dict(knob=0.3, fwd=0.24), rsh=(0.08, 0.06, -0.01), rpole=CROSS,
                  lpole=TUCK, back=18, crouch=0.07, feet=((0.14, -0.16), (-0.1, 0.16))),
    # fish landed: the left hand holds the rod up high (the game lifts it upright), right arm in a fist pump
    "cheer": dict(lh=(0.0, 0.30, 1.85), rh=(0.03, -0.34, 1.7), back=0, feet=((0.06, -0.12), (0.0, 0.12)),
                  l1=0.27, l2=0.24, fist=0.034, shrug=0.03),
}


# ---------------------------------------------------------------- game rod / reel on screen (stage px, y up)
def unity_to_world(u):
    return Vector((u[0], u[2], u[1]))


def spx(p):
    """Blender world point (feet at the origin, stand 0) -> stage px offset from the image centre (x right, y up)."""
    x, y, _ = P.project(p, 0.0)
    return Vector((x, y))


def game_rod_at(hand_w, pose):
    """-> at(s): stage px of the point the game paints s metres along the rod (Angler.cs At(), unloaded)."""
    d = unity_to_world(ROD[pose]).normalized()
    L = GAME_ROD_LEN
    tip = hand_w + d * L
    c1 = hand_w + d * (L * 0.5)
    c2 = tip - d * (L * 0.3)

    def at(s):
        if s < 0:
            return spx(hand_w + d * s)
        t = s / L
        u = 1.0 - t
        return spx(hand_w * (u ** 3) + c1 * (3 * u * u * t) + c2 * (3 * u * t * t) + tip * (t ** 3))
    return at


def game_reel(hand_w, pose):
    """-> (seat px, reel sprite centre px, reel rotation deg ccw) exactly as Angler.cs places the reel sprite."""
    at = game_rod_at(hand_w, pose)
    seat = at(GAME_REEL_S)
    tan = at(GAME_REEL_S + 0.05) - at(GAME_REEL_S - 0.05)
    under = Vector((-tan.y, tan.x)).normalized()
    if under.y > 0 or (abs(under.y) < 0.2 and under.x < 0):
        under = -under
    up = Vector((0.0, 1.0)).lerp(-under, GAME_REEL_TILT).normalized()
    deg = math.degrees(math.atan2(-up.x, up.y))
    return seat, seat + under * 1.5 - up * GAME_REEL_FOOT_PX, deg


def reel_point_px(hand_w, pose, uv):
    """A point of the reel sprite (px from its centre, x right / y up) -> stage px, with the game's tilt."""
    _, c, deg = game_reel(hand_w, pose)
    a = math.radians(deg)
    return c + Vector((uv[0] * math.cos(a) - uv[1] * math.sin(a), uv[0] * math.sin(a) + uv[1] * math.cos(a)))


def knob_uv(k):
    (x0, y0), (x1, y1) = REEL_KNOB_PX
    return (x0 + (x1 - x0) * k, y0 + (y1 - y0) * k)


def unproject_char(target, fwd, turn):
    """Char-frame point (fwd, y, z) whose stage px (after `turn`) is `target` (Newton on y, z)."""
    y, z = 0.0, 1.2
    h = 1e-4
    for _ in range(30):
        p0 = spx(turn @ Vector((fwd, y, z)))
        e = target - p0
        if e.length < 1e-5:
            break
        jy = (spx(turn @ Vector((fwd, y + h, z))) - p0) / h
        jz = (spx(turn @ Vector((fwd, y, z + h))) - p0) / h
        det = jy.x * jz.y - jz.x * jy.y
        y += (e.x * jz.y - jz.x * e.y) / det
        z += (jy.x * e.y - e.x * jy.y) / det
    return Vector((fwd, y, z))


def right_target(pd, pose, lhand_c, turn):
    """RIGHT-hand IK target in the character frame (see POSES) + the stage px it should land on."""
    rh = pd["rh"]
    lw = turn @ lhand_c
    if not isinstance(rh, dict):
        t = Vector(rh)
    elif "s" in rh:
        d = unity_to_world(ROD[pose]).normalized()
        t = turn.inverted() @ (lw + d * rh["s"])
    else:
        uv = knob_uv(rh["knob"]) if "knob" in rh else rh["sprite"]
        goal = reel_point_px(lw, pose, uv) + Vector(rh.get("off", (0.0, 0.0)))
        t = unproject_char(goal, rh["fwd"], turn)
    return t, spx(turn @ t)


def mats():
    def t(cols, b, **kw):
        return R.m_tone(cols, b, light=CL, **kw)
    plaid = ((2, 0.075, 0.28, -0.16, "object"), (1, 0.075, 0.28, -0.16, "object"))
    return dict(
        skin=t(PAL["skin"], B3, name="Skin"),
        hair=t(PAL["leather"], [0.7], name="Hair"),
        khaki=t(PAL["khaki"], B3, name="Khaki"),
        hatband=t(PAL["leather"], B2, name="Band"),
        vest=t(PAL["olive"], B3V, name="Vest"),
        vestdk=R.m_flat(PAL["olive"][0]),
        vestlt=t(PAL["olive"][1:], [0.35], name="VestLt"),
        shirt=t(PAL["shirt"], B3, pats=plaid, name="Shirt"),
        trou=t(PAL["trou"], B3, noise=0.04, nscale=8, name="Trou"),
        troudk=R.m_flat(PAL["trou"][0]),
        troult=R.m_flat(PAL["trou"][2]),
        boot=t(PAL["leather"], B2, name="Boot"),
        heel=R.m_flat(PAL["leather"][0]),
        strap=t(PAL["leather"], [0.7], name="Strap"),
        wicker=t([PAL["khaki"][0], PAL["khaki"][1]], [0.3], pats=((2, 0.045, 0.34, -0.5, "world"),), name="Wicker"),
        cord=R.m_flat(PAL["leather"][0]),
    )


def two_bone(sh, target, l1, l2, pole):
    """Analytic 3D 2-bone IK: the elbow bends towards `pole`. Returns (elbow, end) as Vectors."""
    s = Vector(sh)
    t = Vector(target)
    d = t - s
    dist = min(d.length, l1 + l2 - 1e-3)
    u = d.normalized()
    v = Vector(pole) - u * Vector(pole).dot(u)
    v.normalize()
    ca = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)))
    a = math.acos(ca)
    elbow = s + (u * math.cos(a) + v * math.sin(a)) * l1
    return elbow, s + u * dist


def brim(name, z_in, r_in, z_out, r_out, th, mat, segs=28):
    """Sloped hat brim: annulus from (r_in at z_in) down to (r_out at z_out), thickness th."""
    verts, faces = [], []
    rings = [(r_in, z_in + th / 2), (r_out, z_out + th / 2), (r_out, z_out - th / 2), (r_in, z_in - th / 2)]
    for (rx_ry, z) in rings:
        rx, ry = rx_ry
        for k in range(segs):
            a = 2 * math.pi * k / segs
            verts.append((rx * math.cos(a), ry * math.sin(a), z))
    for r in range(4):
        r2 = (r + 1) % 4
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append((r * segs + k, r * segs + k2, r2 * segs + k2, r2 * segs + k))
    ob = R.mesh_from(name, verts, faces, mat)
    R._fix_normals(ob)
    return ob


def grp(ob, g):
    ob["grp"] = g
    return ob


def build(pose):
    M = mats()
    pd = POSES[pose]
    crouch = pd.get("crouch", 0.0)
    lower, upper, pelvis = [], [], []
    hip_z = 0.86 - crouch
    tilt_deg = pd.get("tilt", 0.0)
    tilt = math.radians(tilt_deg)
    # ------------------------------------------------ legs: 2-bone IK hip -> ankle, knees bend forward
    (nfx, nfy), (ffx, ffy) = pd["feet"]
    for side, hy, (fx, fy), hdz in (("near", -0.094, (nfx, nfy), 0.004 * tilt_deg),
                                    ("far", 0.094, (ffx, ffy), -0.004 * tilt_deg)):
        hip = Vector((0.0, hy, hip_z + hdz))
        ank = Vector((fx, fy, 0.1))
        l1, l2 = 0.395, 0.375
        extra = pd.get("farknee", 0) if side == "far" else 0
        if extra:
            # lower the far hip along the leg so the knee bends by ~extra degrees
            ang = math.radians(extra)
            reach = math.sqrt(l1 * l1 + l2 * l2 + 2 * l1 * l2 * math.cos(ang))
            dv = hip - ank
            if dv.length > reach:
                hip = ank + dv.normalized() * reach
        knee, ank2 = two_bone(hip, ank, l1, l2, (1.0, 0.0, 0.25))
        g = "leg" + side
        mid = (hip + knee) / 2 + Vector((0.012, 0, 0))
        lower.append(grp(R.loft("Thigh" + side, [hip, mid, knee], [(0.084, 0.086), (0.078, 0.08), (0.062, 0.064)], M["trou"], 14), g))
        shin_mid = (knee + ank2) / 2 + Vector((-0.008, 0, 0))
        lower.append(grp(R.loft("Shin" + side, [knee, shin_mid, ank2 + Vector((0, 0, 0.05))],
                                [(0.062, 0.064), (0.056, 0.057), (0.058, 0.06)], M["trou"], 14), g))
        lower.append(grp(R.loft("Hem" + side, [ank2 + Vector((0, 0, 0.1)), ank2 + Vector((0, 0, 0.055))],
                                [(0.064, 0.066), (0.066, 0.068)], M["trou"], 14), g))
        # fold highlight behind the knee (short light crease across the back of the leg)
        kb = knee + Vector((-0.064, 0, 0.012))
        lower.append(grp(R.loft("KneeFold" + side, [kb + Vector((0.01, -0.035, 0.008)), kb + Vector((0, 0, -0.004)),
                                                    kb + Vector((0.01, 0.035, 0.008))], 0.0105, M["troult"], 6), g))
        lower.append(R.loft("Boot" + side, [ank2 + Vector((-0.05, 0, 0.07)), ank2 + Vector((-0.02, 0, 0.0)),
                                             ank2 + Vector((0.08, 0, -0.045)), ank2 + Vector((0.16, 0, -0.05))],
                            [(0.056, 0.058), (0.06, 0.06), (0.05, 0.055), (0.03, 0.045)], M["boot"], 12))
        # heel block: visible from behind under the trouser hem
        lower.append(R.box("Heel" + side, ank2 + Vector((-0.045, 0, -0.07)), (0.07, 0.1, 0.05), M["heel"], bevel=0.006))
        lower.append(R.box("Sole" + side, ank2 + Vector((0.05, 0, -0.085)), (0.3, 0.1, 0.03), R.m_flat(PAL["leather"][0]), bevel=0.008))
    # ------------------------------------------------ hips + belt + seat 'V' (tilted with the pelvis)
    pelvis.append(R.loft("Hips", [(0, 0, 0.8), (0, 0, 0.9), (0, 0, 1.0)],
                         [(0.105, 0.155), (0.118, 0.172), (0.112, 0.16)], M["trou"], 18))
    pelvis.append(R.loft("Belt", [(0, 0, 0.975), (0, 0, 1.015)], [(0.118, 0.166), (0.118, 0.166)], M["strap"], 18))
    for s_ in (-1, 1):
        pelvis.append(grp(R.loft("SeatV", [(-0.112, s_ * 0.05, 0.925), (-0.117, s_ * 0.022, 0.885), (-0.116, 0.0, 0.855)],
                                 0.011, M["troudk"], 6), "seat"))
    pelvis.append(grp(R.loft("Inseam", [(-0.105, 0.0, 0.86), (-0.08, 0.0, 0.8)], 0.012, M["troudk"], 6), "seat"))
    # ------------------------------------------------ torso (shirt) + vest
    torso = [(0.98, (0.11, 0.156)), (1.1, (0.118, 0.165)), (1.22, (0.126, 0.182)), (1.31, (0.118, 0.19)),
             (1.37, (0.092, 0.165)), (1.42, (0.06, 0.08))]
    upper.append(R.loft("Shirt", [(0.0, 0, z) for z, _ in torso], [r for _, r in torso], M["shirt"], 20))
    vest = [(0.93, (0.126, 0.172)), (1.04, (0.13, 0.176)), (1.2, (0.138, 0.192)), (1.3, (0.13, 0.198)),
            (1.36, (0.1, 0.17)), (1.395, (0.07, 0.1))]
    upper.append(R.loft("Vest", [(-0.004, 0, z) for z, _ in vest], [r for _, r in vest], M["vest"], 20))
    # vest back: 1 px yoke seam, 2 px darker hem, cargo pocket with a light flap
    upper.append(R.loft("Yoke", [(-0.004, 0, 1.232), (-0.004, 0, 1.256)], [(0.143, 0.198), (0.141, 0.198)], M["vestdk"], 20))
    upper.append(R.loft("VHem", [(-0.004, 0, 0.925), (-0.004, 0, 0.972)], [(0.131, 0.177), (0.133, 0.179)], M["vestdk"], 20))
    upper.append(R.box("Pocket", (-0.134, 0.0, 1.06), (0.03, 0.24, 0.13), M["vest"], bevel=0.01))
    upper.append(R.box("Flap", (-0.147, 0.0, 1.128), (0.02, 0.25, 0.034), M["vestlt"], bevel=0.006))
    # collar + neck + head
    upper.append(R.loft("Collar", [(0.0, 0, 1.39), (0.0, 0, 1.44)], [(0.07, 0.078), (0.066, 0.072)], M["shirt"], 16))
    upper.append(R.loft("Neck", [(0.01, 0, 1.4), (0.01, 0, 1.52)], 0.05, M["skin"], 12))
    upper.append(R.ellipsoid("Head", (0.012, 0, 1.585), (0.1, 0.086, 0.114), M["skin"]))
    upper.append(R.ellipsoid("Hair", (-0.02, 0, 1.585), (0.098, 0.091, 0.118), M["hair"]))
    for s_ in (-1, 1):
        upper.append(R.ellipsoid("Ear", (0.01, s_ * 0.088, 1.575), (0.022, 0.014, 0.032), M["skin"], 10, 6))
    hat_c = [(1.615, (0.104, 0.098)), (1.675, (0.099, 0.093)), (1.703, (0.09, 0.084)), (1.712, (0.06, 0.056)), (1.715, (0.005, 0.005))]
    upper.append(R.loft("Crown", [(0.0, 0, z) for z, _ in hat_c], [r for _, r in hat_c], M["khaki"], 20))
    upper.append(R.loft("HatBand", [(0.0, 0, 1.63), (0.0, 0, 1.655)], [(0.108, 0.102), (0.106, 0.1)], M["hatband"], 20))
    upper.append(brim("Brim", 1.628, (0.102, 0.096), 1.572, (0.188, 0.178), 0.014, M["khaki"]))
    # ------------------------------------------------ creel: small, khaki-mid wicker tucked behind the far hip
    cre = (-0.155, 0.13, 0.9)
    upper.append(R.box("Creel", cre, (0.09, 0.18, 0.135), M["wicker"], bevel=0.012))
    upper.append(R.box("Lid", (cre[0], cre[1], cre[2] + 0.074), (0.1, 0.19, 0.02), R.m_flat(PAL["khaki"][1]), bevel=0.006))
    upper.append(R.box("Latch", (cre[0] - 0.05, cre[1], cre[2] + 0.05), (0.01, 0.025, 0.04), M["strap"]))
    sp = [(-0.02, -0.17, 1.375), (-0.11, -0.12, 1.3), (-0.148, -0.03, 1.2), (-0.15, 0.06, 1.1), (-0.16, 0.12, 1.0)]
    upper.append(R.loft("Strap", sp, [(0.009, 0.024)] * len(sp), M["strap"], 8, up=(1, 0, 0)))
    # ------------------------------------------------ pelvis tilt, crouch, lean back (pivot at the hips)
    TM = Matrix.Translation((0, 0, 0.9)) @ Matrix.Rotation(tilt, 4, "X") @ Matrix.Translation((0, 0, -0.9))
    DM = Matrix.Translation((0, 0, -crouch))
    for ob in pelvis:
        ob.matrix_world = DM @ TM @ ob.matrix_world
    back = math.radians(pd.get("back", 0.0))
    pivot = Vector((0, 0, 0.78 - crouch))
    LM = Matrix.Translation(pivot) @ Matrix.Rotation(-back, 4, "Y") @ Matrix.Translation(-pivot) @ DM
    for ob in upper:
        ob.matrix_world = LM @ ob.matrix_world

    # ------------------------------------------------ arms: 3D IK, elbows flared sideways. LEFT hand first (it holds
    # the rod); the RIGHT hand's target follows from where the game then draws the reel knob.
    turn = Matrix.Rotation(math.radians(TURN), 4, "Z")
    arms = solve_arms(pose, LM, turn)
    arm_objs = []
    fist = pd.get("fist", 0.042)
    for side in ("left", "right"):
        sh, elbow, hand = arms[side][:3]
        fore = hand - elbow
        wrist = hand - fore.normalized() * fist
        cuff = elbow + (wrist - elbow) * 0.4
        g = "arm" + side
        arm_objs.append(R.loft("Upper" + side, [sh, (sh + elbow) / 2, elbow], [0.056, 0.052, 0.047], M["shirt"], 12))
        arm_objs.append(R.loft("Sleeve" + side, [elbow, cuff], [0.047, 0.05], M["shirt"], 12))
        arm_objs.append(R.loft("Cuff" + side, [cuff - fore * 0.02, cuff + fore * 0.07], [0.052, 0.05], M["shirt"], 12))
        arm_objs.append(grp(R.loft("Fore" + side, [cuff, wrist], [0.038, 0.031], M["skin"], 10), g))
        arm_objs.append(grp(R.ellipsoid("Hand" + side, hand, (fist, fist * 0.85, fist * 0.95), M["skin"], 10, 8), g))
    objs = lower + pelvis + upper + arm_objs
    for ob in objs:
        ob.matrix_world = turn @ ob.matrix_world
    return objs, arms


def pose_frame(pd):
    """Upper-body matrix (crouch + lean back about the hips), as applied in build()."""
    crouch = pd.get("crouch", 0.0)
    back = math.radians(pd.get("back", 0.0))
    pivot = Vector((0, 0, 0.78 - crouch))
    return (Matrix.Translation(pivot) @ Matrix.Rotation(-back, 4, "Y") @ Matrix.Translation(-pivot)
            @ Matrix.Translation((0, 0, -crouch)))


def solve_arms(pose, LM, turn):
    """-> {side: (shoulder, elbow, hand, target, reach ratio)} in the character frame, plus 'goal' (stage px the
    right hand should land on) and 'rpx' (where it lands)."""
    pd = POSES[pose]
    l1 = pd.get("l1", 0.295)
    l2 = pd.get("l2", 0.265)
    fist = pd.get("fist", 0.042)
    shrug = pd.get("shrug", 0.0)
    out = {}
    for side, sy in (("left", 0.19), ("right", -0.19)):
        sgn = 1 if sy > 0 else -1
        # rsh: the right shoulder rolls forward / in when that arm reaches across the chest to the reel
        dsh = Vector(pd.get("rsh", (0.0, 0.0, 0.0))) if side == "right" else Vector((0.0, 0.0, 0.0))
        sh = LM @ (Vector((-0.005, sy, 1.335 + shrug)) + dsh)
        if side == "left":
            tgt = Vector(pd["lh"])
        else:
            tgt, out["goal"] = right_target(pd, pose, out["left"][2], turn)
        pole = Vector(pd.get("lpole" if side == "left" else "rpole", (-0.35, sgn * 1.0, -0.45)))
        elbow, hand = two_bone(sh, tgt, l1, l2 + fist, pole)
        out[side] = (sh, elbow, hand, tgt, (tgt - sh).length / (l1 + l2 + fist))
    out["rpx"] = spx(turn @ out["right"][2])
    return out


def post(ps, pal):
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=False)
    idx = R.despeckle(idx, ps["id"], passes=2)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.07, steps=1)
    # subtle warm rim: OUTER silhouette on the sun side only, strong on hat / shoulders, faint on the boots
    a = idx >= 0
    rows = np.flatnonzero(a.any(1))
    y0, y1 = (rows.min(), rows.max()) if len(rows) else (0, 1)
    t = (np.arange(idx.shape[0])[:, None] - y0) / max(1, y1 - y0)
    weight = np.clip(1.2 - 0.95 * t, 0.3, 1.0) * np.ones_like(idx, float)
    idx, pal = R.rim_light(idx, pal, PR, strength=PR.rim["strength"] * RIM_SCALE, weight=weight, snap=0.035,
                           levels=1)
    # selective outline from the neighbour's own hue-shifted darker tones (no ink): 2 steps on the shadow
    # side / below, 1 step on the lit side
    idx = R.outer_outline(idx, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    return idx, pal


def main():
    C.reset_scene()
    R.reset_materials()
    mats()
    pal = R.Pal([PAL["deep"]] + sum([PAL[k] for k in ("skin", "leather", "khaki", "olive", "shirt", "trou")], []))
    print("HYB char palette", len(pal))
    P.setup_camera(0.0)
    fx, fy, _ = P.project((0, 0, 0), 0.0)
    cx = int(round(P.W / 2 + fx))
    cy = int(round(P.H / 2 + fy))
    x0, y0 = cx - CROP_W // 2, cy - FEET_PX           # bottom-up crop origin, like fk_character
    data = {"cropW": CROP_W, "cropH": CROP_H, "feetPx": FEET_PX, "poses": []}
    imgs = []
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    dry = "--dry" in args
    only = [a for a in args if not a.startswith("--")]
    masks = {}
    grip = {}
    turn = Matrix.Rotation(math.radians(TURN), 4, "Z")
    for pose, pd in POSES.items():
        if only and pose not in only:
            continue
        arms = solve_arms(pose, pose_frame(pd), turn)
        lw = turn @ arms["left"][2]
        seat, rc, rdeg = game_reel(lw, pose)
        lpx = spx(lw)
        goal = arms.get("goal", arms["rpx"])
        err = (arms["rpx"] - goal).length
        k0 = reel_point_px(lw, pose, REEL_KNOB_PX[0])
        k1 = reel_point_px(lw, pose, REEL_KNOB_PX[1])
        print(f"HYB grip {pose:6s} L px ({lpx.x:6.1f},{lpx.y:6.1f}) reach L {arms['left'][4]:.2f} R {arms['right'][4]:.2f}"
              f" | R px ({arms['rpx'].x:6.1f},{arms['rpx'].y:6.1f}) goal ({goal.x:6.1f},{goal.y:6.1f}) err {err:.2f}"
              f" | knob0 ({k0.x:6.1f},{k0.y:6.1f}) knob1 ({k1.x:6.1f},{k1.y:6.1f}) reel deg {rdeg:.1f}"
              f" | L elbow char {tuple(round(v, 3) for v in arms['left'][1])} R hand char {tuple(round(v, 3) for v in arms['right'][2])} elbow {tuple(round(v, 3) for v in arms['right'][1])}")
        grip[pose] = dict(left=[round(lpx.x, 2), round(lpx.y, 2)], right=[round(arms["rpx"].x, 2), round(arms["rpx"].y, 2)],
                          goal=[round(goal.x, 2), round(goal.y, 2)], seat=[round(seat.x, 2), round(seat.y, 2)],
                          reel=[round(rc.x, 2), round(rc.y, 2)], reelDeg=round(rdeg, 2),
                          knob0=[round(k0.x, 2), round(k0.y, 2)], knob1=[round(k1.x, 2), round(k1.y, 2)])
        if dry:
            continue
        C.clear_objects()
        _, arms = build(pose)
        hand = turn @ arms["left"][2]
        bpy.context.view_layer.update()
        ps = R.render_passes("char_" + pose)
        H = ps["a"].shape[0]
        # crop (top-down): bottom-up rows y0..y0+CROP_H  ->  top-down rows H-(y0+CROP_H) .. H-y0
        r0 = H - (y0 + CROP_H)
        crop = {k: v[r0:r0 + CROP_H, x0:x0 + CROP_W] for k, v in ps.items() if isinstance(v, np.ndarray)}
        idx, ppal = post(crop, pal)
        img = R.to_rgba(idx, ppal)
        R.save_png(img, os.path.join(OUT, f"angler_{pose}.png"))
        imgs.append(img)
        hx, hy, _ = P.project(hand, 0.0)
        rod = Vector(ROD[pose]).normalized()
        data["poses"].append({
            "name": pose,
            "handX": round(P.W / 2 + hx - x0, 2), "handY": round(P.H / 2 + hy - y0, 2),
            "hx": round(hand.x, 3), "hy": round(hand.z, 3), "hz": round(hand.y, 3),
            "rx": round(rod.x, 3), "ry": round(rod.y, 3), "rz": round(rod.z, 3),
        })
        top = np.argmax(img[..., 3].any(1))
        masks[pose] = img[..., 3] > 0.5
        dif = int((masks[pose] ^ masks["idle"]).sum()) if "idle" in masks else 0
        print("HYB pose", pose, "colours", R.count_colours(img), "height px", CROP_H - FEET_PX - top,
              "hand", data["poses"][-1]["handX"], data["poses"][-1]["handY"], "mask diff vs idle", dif)
    if dry:
        return
    with open(os.path.join(R.OUT, "character_grip.json"), "w", encoding="utf-8") as f:
        json.dump(grip, f, indent=1)
    with open(os.path.join(OUT, "character.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    sh = R.sheet(imgs, (0.5, 0.5, 0.5), scale=4, pad=8)
    R.save_png(sh, os.path.join(R.OUT, "character_sheet.png"))
    print("HYB CHAR done")


if __name__ == "__main__":
    main()
