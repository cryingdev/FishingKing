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
TGT_SCALE = 0.92   # fk POSES hand targets are in fk model units (scaled by 0.92 in the game)

# ---------------------------------------------------------------- curated 17-colour palette
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
B2 = [0.6]
CL = Vector((-0.72, -0.4, 0.56)).normalized()   # key light: strongly from the left so the back gets form


def mats():
    def t(cols, b, **kw):
        return R.m_tone(cols, b, light=CL, **kw)
    plaid = ((2, 0.075, 0.28, -0.16, "object"), (1, 0.075, 0.28, -0.16, "object"))
    return dict(
        skin=t(PAL["skin"], B3, name="Skin"),
        hair=t(PAL["leather"], [0.7], name="Hair"),
        khaki=t(PAL["khaki"], B3, name="Khaki"),
        hatband=t(PAL["leather"], B2, name="Band"),
        vest=t(PAL["olive"], B3, noise=0.05, nscale=7, name="Vest"),
        vestdk=t(PAL["olive"][:2], [0.8], name="VestDk"),
        shirt=t(PAL["shirt"], B3, pats=plaid, name="Shirt"),
        trou=t(PAL["trou"], B3, noise=0.09, nscale=8, name="Trou"),
        boot=t(PAL["leather"], B2, name="Boot"),
        strap=t(PAL["leather"], [0.7], name="Strap"),
        wicker=t(PAL["khaki"], B3, pats=((2, 0.05, 0.45, -0.2, "world"),), name="Wicker"),
        cord=R.m_flat(PAL["leather"][0]),
    )


def two_bone(sh, target, l1, l2, bend_sign=-1):
    """Analytic 2-bone IK in the local XZ plane (like fk_character). Returns (elbow, hand)."""
    s = Vector((sh[0], sh[2]))
    t = Vector((target[0], target[1]))
    d = t - s
    dist = min(d.length, l1 + l2 - 1e-3)
    d.normalize()
    a = math.acos(max(-1, min(1, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist))))
    base = math.atan2(d.y, d.x)
    ang = base + bend_sign * a
    e = s + Vector((math.cos(ang), math.sin(ang))) * l1
    return Vector((e.x, sh[1], e.y)), Vector((s.x + d.x * dist, sh[1], s.y + d.y * dist))


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


def build(pose):
    M = mats()
    pd = FC.POSES[pose]
    lower, upper = [], []
    # ------------------------------------------------ legs (stance: near/right foot a bit forward)
    for side, y, xf in (("near", -0.098, 0.1), ("far", 0.098, -0.05)):
        hip = Vector((0.0, y * 0.95, 0.86))
        knee = Vector((xf * 0.55 + 0.035, y * 1.1, 0.47))
        ank = Vector((xf, y * 1.15, 0.1))
        mid = (hip + knee) / 2 + Vector((0.012, 0, 0))
        lower.append(R.loft("Thigh" + side, [hip, mid, knee], [(0.084, 0.086), (0.078, 0.08), (0.062, 0.064)], M["trou"], 14))
        shin_mid = (knee + ank) / 2 + Vector((-0.008, 0, 0))
        lower.append(R.loft("Shin" + side, [knee, shin_mid, ank + Vector((0, 0, 0.05))],
                            [(0.062, 0.064), (0.056, 0.057), (0.058, 0.06)], M["trou"], 14))
        # bunched hem over the boot
        lower.append(R.loft("Hem" + side, [ank + Vector((0, 0, 0.1)), ank + Vector((0, 0, 0.055))],
                            [(0.064, 0.066), (0.066, 0.068)], M["trou"], 14))
        lower.append(R.loft("Boot" + side, [ank + Vector((-0.05, 0, 0.07)), ank + Vector((-0.02, 0, 0.0)),
                                             ank + Vector((0.08, 0, -0.045)), ank + Vector((0.16, 0, -0.05))],
                            [(0.056, 0.058), (0.06, 0.06), (0.05, 0.055), (0.03, 0.045)], M["boot"], 12))
        lower.append(R.box("Sole" + side, ank + Vector((0.05, 0, -0.085)), (0.3, 0.1, 0.03), R.m_flat(PAL["ink"]), bevel=0.008))
    # ------------------------------------------------ hips + belt
    upper.append(R.loft("Hips", [(0, 0, 0.8), (0, 0, 0.9), (0, 0, 1.0)],
                        [(0.105, 0.155), (0.118, 0.172), (0.112, 0.16)], M["trou"], 18))
    upper.append(R.loft("Belt", [(0, 0, 0.975), (0, 0, 1.015)], [(0.118, 0.166), (0.118, 0.166)], M["strap"], 18))
    # ------------------------------------------------ torso (shirt) + vest
    torso = [(0.98, (0.11, 0.156)), (1.1, (0.118, 0.165)), (1.22, (0.126, 0.182)), (1.31, (0.118, 0.19)),
             (1.37, (0.092, 0.165)), (1.42, (0.06, 0.08))]
    upper.append(R.loft("Shirt", [(0.0, 0, z) for z, _ in torso], [r for _, r in torso], M["shirt"], 20))
    vest = [(0.93, (0.126, 0.172)), (1.04, (0.13, 0.176)), (1.2, (0.138, 0.192)), (1.3, (0.13, 0.198)),
            (1.36, (0.1, 0.17)), (1.395, (0.07, 0.1))]
    upper.append(R.loft("Vest", [(-0.004, 0, z) for z, _ in vest], [r for _, r in vest], M["vest"], 20))
    # vest back: yoke seam + big cargo pocket with flap
    upper.append(R.loft("Yoke", [(-0.004, 0, 1.235), (-0.004, 0, 1.25)], [(0.141, 0.196), (0.14, 0.196)], M["vestdk"], 20))
    upper.append(R.box("Pocket", (-0.132, 0.0, 1.06), (0.03, 0.26, 0.14), M["vest"], bevel=0.01))
    upper.append(R.box("Flap", (-0.143, 0.0, 1.13), (0.02, 0.27, 0.035), M["vestdk"], bevel=0.006))
    # collar + neck + head
    upper.append(R.loft("Collar", [(0.0, 0, 1.39), (0.0, 0, 1.44)], [(0.07, 0.078), (0.066, 0.072)], M["shirt"], 16))
    upper.append(R.loft("Neck", [(0.01, 0, 1.4), (0.01, 0, 1.52)], 0.05, M["skin"], 12))
    upper.append(R.ellipsoid("Head", (0.012, 0, 1.585), (0.1, 0.086, 0.114), M["skin"]))
    upper.append(R.ellipsoid("Hair", (-0.02, 0, 1.585), (0.098, 0.091, 0.118), M["hair"]))
    for s in (-1, 1):
        upper.append(R.ellipsoid("Ear", (0.01, s * 0.088, 1.575), (0.022, 0.014, 0.032), M["skin"], 10, 6))
    # bucket hat
    hat_c = [(1.615, (0.104, 0.098)), (1.675, (0.099, 0.093)), (1.703, (0.09, 0.084)), (1.712, (0.06, 0.056)), (1.715, (0.005, 0.005))]
    upper.append(R.loft("Crown", [(0.0, 0, z) for z, _ in hat_c], [r for _, r in hat_c], M["khaki"], 20))
    upper.append(R.loft("HatBand", [(0.0, 0, 1.63), (0.0, 0, 1.655)], [(0.108, 0.102), (0.106, 0.1)], M["hatband"], 20))
    b = brim("Brim", 1.628, (0.102, 0.096), 1.572, (0.188, 0.178), 0.014, M["khaki"])
    upper.append(b)
    # ------------------------------------------------ creel on the far (left) hip + strap over the right shoulder
    cre = R.box("Creel", (-0.03, 0.235, 0.9), (0.24, 0.12, 0.18), M["wicker"], bevel=0.015)
    upper.append(cre)
    upper.append(R.box("Lid", (-0.03, 0.235, 0.998), (0.25, 0.13, 0.026), M["khaki"], bevel=0.008))
    upper.append(R.box("Latch", (-0.155, 0.235, 0.965), (0.012, 0.03, 0.05), M["strap"]))
    sp = [(-0.02, -0.17, 1.375), (-0.11, -0.12, 1.3), (-0.148, -0.02, 1.2), (-0.146, 0.08, 1.1), (-0.13, 0.17, 1.02),
          (-0.09, 0.22, 1.0)]
    upper.append(R.loft("Strap", sp, [(0.009, 0.024)] * len(sp), M["strap"], 8, up=(1, 0, 0)))
    # ------------------------------------------------ lean the upper body (like fk: pivot at the hips)
    lean = math.radians(pd["lean"])
    rot = Matrix.Rotation(-lean, 4, "Y")
    pivot = Vector((0, 0, 0.78))
    LM = Matrix.Translation(pivot) @ rot @ Matrix.Translation(-pivot)
    for ob in upper:
        ob.matrix_world = LM @ ob.matrix_world

    def lp(pt):
        return LM @ Vector(pt)

    # ------------------------------------------------ arms (IK to the fk pose targets)
    hands = {}
    arm_objs = []
    for side, sy, tgt in (("far", 0.19, pd["far"]), ("near", -0.19, pd["near"])):
        sh = lp((-0.005, sy, 1.335))
        tg = (tgt[0] * TGT_SCALE, tgt[1] * TGT_SCALE)
        l1, l2 = 0.295, 0.265
        elbow, wrist = two_bone(sh, tg, l1, l2, bend_sign=-1 if tg[0] > sh.x - 0.1 else 1)
        flare = 0.05 if side == "near" else -0.03
        elbow = elbow + Vector((0, -flare if side == "near" else 0.03, 0))
        wrist = wrist + Vector((0, 0.04 if side == "near" else -0.03, 0))
        fore = (wrist - elbow)
        cuff = elbow + fore * 0.3
        arm_objs.append(R.loft("Upper" + side, [sh, (sh + elbow) / 2, elbow], [0.056, 0.052, 0.047], M["shirt"], 12))
        arm_objs.append(R.loft("Sleeve" + side, [elbow, cuff], [0.047, 0.05], M["shirt"], 12))
        arm_objs.append(R.loft("Cuff" + side, [cuff - fore * 0.02, cuff + fore * 0.07], [0.052, 0.05], M["shirt"], 12))
        arm_objs.append(R.loft("Fore" + side, [cuff, wrist], [0.038, 0.031], M["skin"], 10))
        hand = wrist + fore.normalized() * 0.045
        arm_objs.append(R.ellipsoid("Hand" + side, hand, (0.045, 0.036, 0.042), M["skin"], 10, 8))
        hands[side] = hand
    objs = lower + upper + arm_objs
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
    for pose, pd in FC.POSES.items():
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
        rod = Vector(pd["rod"]).normalized()
        data["poses"].append({
            "name": pose,
            "handX": round(P.W / 2 + hx - x0, 2), "handY": round(P.H / 2 + hy - y0, 2),
            "hx": round(hand.x, 3), "hy": round(hand.z, 3), "hz": round(hand.y, 3),
            "rx": round(rod.x, 3), "ry": round(rod.y, 3), "rz": round(rod.z, 3),
        })
        top = np.argmax(img[..., 3].any(1))
        print("R16 pose", pose, "colours", R.count_colours(img), "height px", CROP_H - FEET_PX - top)
    with open(os.path.join(OUT, "angler.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    sh = R.sheet(imgs, (0.5, 0.5, 0.5), scale=4, pad=8)
    R.save_png(sh, os.path.join(R.OUT, "character_sheet.png"))
    print("R16 CHAR done")


if __name__ == "__main__":
    main()
