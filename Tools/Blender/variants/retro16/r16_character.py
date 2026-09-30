"""
retro16 - the angler seen from behind (not chibi: ~6 heads), rendered through the shared stage
camera (fk_persp.setup_camera(0.0)), cropped to 96x112 with the feet 10 px above the bottom.

Outputs (Tools/Blender/_tmp/variants/retro16/character/):
  angler_<pose>.png  for idle, aim, cast, reel, reel2, fight, cheer
  angler.json        same schema as Data/character.json (hand px, hand 3D, rod dir)
  ../character_sheet.png

Run: blender -b --python variants/retro16/r16_character.py
"""
import os
import sys
import math
import json
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import r16_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402
import fk_character as FC  # noqa: E402  (POSES table + rod directions; read only)

OUT = os.path.join(R.OUT, "character")
CROP_W, CROP_H, FEET_PX = 96, 112, 10
TURN = 80.0

# ---------------------------------------------------------------- curated 16-colour palette
PAL = dict(
    ink="#1c1622",
    skin=["#a95c4a", "#d08c68", "#f0bc94"],
    leather=["#3c2826", "#6e5a3c"],
    khaki=["#6e5a3c", "#a48c60", "#d4c08e"],
    olive=["#343e2c", "#56623c", "#808c56"],
    shirt=["#4a2432", "#7c3a3c", "#a95c4a"],
    trou=["#2a2c38", "#434858", "#606878"],
)
B3 = [0.45, 0.74]     # tone boundaries (half-lambert) for 3-tone ramps
B3V = [0.55, 0.8]     # vest: the back splits into a lit left half and a mid right half
B2 = [0.6]
CL = Vector((-0.72, -0.4, 0.56)).normalized()   # key light: strongly from the left so the back gets form

# ---------------------------------------------------------------- poses for the ~6-head rig
# Hand targets are absolute points in the character frame (x forward, y left (+) / right (-), z up, feet at 0),
# chosen so both forearms clear the torso outline when seen from behind. back: lean back (deg, towards the
# camera); crouch: hip drop (m); feet: ((x, y) near, (x, y) far); tilt: pelvis tilt (deg, near hip up);
# farknee: extra far-knee bend (deg). The rod direction (Unity axes) is kept from fk_character.POSES.
POSES = {
    "idle": dict(near=(0.22, -0.30, 0.96), far=(0.05, 0.31, 0.82), back=0, feet=((0.1, -0.1), (-0.04, 0.1)),
                 tilt=3.0, farknee=8),
    "aim": dict(near=(-0.14, -0.27, 1.72), far=(-0.1, -0.16, 1.5), back=8, feet=((0.08, -0.11), (-0.1, 0.11))),
    "cast": dict(near=(0.56, -0.23, 1.33), far=(0.5, -0.1, 1.28), back=-10, feet=((0.16, -0.11), (-0.06, 0.1)),
                 farknee=10),
    "reel": dict(near=(0.23, -0.32, 1.05), far=(0.3, -0.25, 1.2), back=3, feet=((0.1, -0.11), (-0.03, 0.11))),
    "reel2": dict(near=(0.17, -0.32, 0.95), far=(0.3, -0.25, 1.2), back=3, feet=((0.1, -0.11), (-0.03, 0.11))),
    "fight": dict(near=(0.22, -0.34, 1.3), far=(0.12, -0.28, 1.02), back=18, crouch=0.07,
                  feet=((0.14, -0.16), (-0.1, 0.16))),
    "cheer": dict(near=(0.0, -0.30, 1.85), far=(0.0, 0.30, 1.85), back=0, feet=((0.06, -0.12), (0.0, 0.12)),
                  l1=0.27, l2=0.24, fist=0.034, shrug=0.03),
}


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
        lower.append(R.box("Sole" + side, ank2 + Vector((0.05, 0, -0.085)), (0.3, 0.1, 0.03), R.m_flat(PAL["ink"]), bevel=0.008))
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

    # ------------------------------------------------ arms: 3D IK, elbows flared ~0.12 m sideways
    hands = {}
    arm_objs = []
    l1 = pd.get("l1", 0.295)
    l2 = pd.get("l2", 0.265)
    fist = pd.get("fist", 0.042)
    shrug = pd.get("shrug", 0.0)
    for side, sy in (("far", 0.19), ("near", -0.19)):
        sgn = 1 if sy > 0 else -1
        sh = LM @ Vector((-0.005, sy, 1.335 + shrug))
        tgt = Vector(pd[side])
        pole = Vector((-0.35, sgn * 1.0, -0.45))
        elbow, hand = two_bone(sh, tgt, l1, l2 + fist, pole)
        fore = hand - elbow
        wrist = hand - fore.normalized() * fist
        cuff = elbow + (wrist - elbow) * 0.4
        g = "arm" + side
        arm_objs.append(R.loft("Upper" + side, [sh, (sh + elbow) / 2, elbow], [0.056, 0.052, 0.047], M["shirt"], 12))
        arm_objs.append(R.loft("Sleeve" + side, [elbow, cuff], [0.047, 0.05], M["shirt"], 12))
        arm_objs.append(R.loft("Cuff" + side, [cuff - fore * 0.02, cuff + fore * 0.07], [0.052, 0.05], M["shirt"], 12))
        arm_objs.append(grp(R.loft("Fore" + side, [cuff, wrist], [0.038, 0.031], M["skin"], 10), g))
        arm_objs.append(grp(R.ellipsoid("Hand" + side, hand, (fist, fist * 0.85, fist * 0.95), M["skin"], 10, 8), g))
        hands[side] = hand
    objs = lower + pelvis + upper + arm_objs
    turn = Matrix.Rotation(math.radians(TURN), 4, "Z")
    for ob in objs:
        ob.matrix_world = turn @ ob.matrix_world
    return objs, turn @ hands["near"]


def post(ps, pal):
    ink = pal.index(PAL["ink"])
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=False)
    idx = R.despeckle(idx, ps["id"], passes=2)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.07, steps=1)
    idx = R.outer_outline(idx, pal, lit_steps=2, dark_col=ink)
    return idx


def main():
    C.reset_scene()
    R.reset_materials()
    mats()
    pal = R.Pal([PAL["ink"]] + sum([PAL[k] for k in ("skin", "leather", "khaki", "olive", "shirt", "trou")], []))
    print("R16 char palette", len(pal))
    P.setup_camera(0.0)
    fx, fy, _ = P.project((0, 0, 0), 0.0)
    cx = int(round(P.W / 2 + fx))
    cy = int(round(P.H / 2 + fy))
    x0, y0 = cx - CROP_W // 2, cy - FEET_PX           # bottom-up crop origin, like fk_character
    data = {"cropW": CROP_W, "cropH": CROP_H, "feetPx": FEET_PX, "poses": []}
    imgs = []
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    masks = {}
    for pose, pd in POSES.items():
        if only and pose not in only:
            continue
        C.clear_objects()
        _, hand = build(pose)
        bpy.context.view_layer.update()
        ps = R.render_passes("char_" + pose)
        H = ps["a"].shape[0]
        # crop (top-down): bottom-up rows y0..y0+CROP_H  ->  top-down rows H-(y0+CROP_H) .. H-y0
        r0 = H - (y0 + CROP_H)
        crop = {k: v[r0:r0 + CROP_H, x0:x0 + CROP_W] for k, v in ps.items() if isinstance(v, np.ndarray)}
        idx = post(crop, pal)
        img = R.to_rgba(idx, pal)
        R.save_png(img, os.path.join(OUT, f"angler_{pose}.png"))
        imgs.append(img)
        hx, hy, _ = P.project(hand, 0.0)
        rod = Vector(FC.POSES[pose]["rod"]).normalized()
        data["poses"].append({
            "name": pose,
            "handX": round(P.W / 2 + hx - x0, 2), "handY": round(P.H / 2 + hy - y0, 2),
            "hx": round(hand.x, 3), "hy": round(hand.z, 3), "hz": round(hand.y, 3),
            "rx": round(rod.x, 3), "ry": round(rod.y, 3), "rz": round(rod.z, 3),
        })
        top = np.argmax(img[..., 3].any(1))
        masks[pose] = img[..., 3] > 0.5
        dif = int((masks[pose] ^ masks["idle"]).sum()) if "idle" in masks else 0
        print("R16 pose", pose, "colours", R.count_colours(img), "height px", CROP_H - FEET_PX - top,
              "hand", data["poses"][-1]["handX"], data["poses"][-1]["handY"], "mask diff vs idle", dif)
    with open(os.path.join(OUT, "angler.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    sh = R.sheet(imgs, (0.5, 0.5, 0.5), scale=4, pad=8)
    R.save_png(sh, os.path.join(R.OUT, "character_sheet.png"))
    print("R16 CHAR done")


if __name__ == "__main__":
    main()
