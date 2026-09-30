"""
FishingKing - angler seen from behind, rendered through the shared stage camera (fk_persp.py),
one sprite per pose, plus Data/character.json with the rod hand (sprite pixels + 3D position
relative to the feet in Unity axes) and the rod direction per pose. The rod itself is drawn in
Unity so it can bend under tension.

Run:  blender -b --python Tools/Blender/fk_character.py
"""
import sys
import os
import math
import bpy
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

OUT = os.path.join(C.SPRITES, "Character")
CROP_W, CROP_H, FEET_PX = 96, 112, 10

M = C.toon_material

# Hand targets (forward, up) in the character's local frame, body lean (deg) and the rod direction
# in Unity axes (x right, y up, z forward) used by the game to draw the rod from the near hand.
POSES = {
    "idle": dict(near=(0.42, 0.98), far=(0.5, 1.08), lean=0, rod=(0.22, 0.72, 0.66)),
    "aim": dict(near=(-0.22, 1.72), far=(-0.08, 1.66), lean=-8, rod=(0.25, 0.72, -0.64)),
    "cast": dict(near=(0.56, 1.45), far=(0.5, 1.55), lean=12, rod=(0.08, 0.3, 0.95)),
    "reel": dict(near=(0.36, 0.95), far=(0.5, 1.1), lean=-3, rod=(0.2, 0.62, 0.76)),
    "reel2": dict(near=(0.3, 1.12), far=(0.5, 1.1), lean=-3, rod=(0.2, 0.62, 0.76)),
    "fight": dict(near=(0.36, 1.22), far=(0.46, 1.38), lean=-14, rod=(0.12, 0.9, 0.42)),
    "cheer": dict(near=(0.3, 2.02), far=(0.3, 2.0), lean=0, rod=(0.45, 0.75, 0.3)),
}
SCALE = 0.92
TURN = 80.0  # degrees: facing +Y (away from the camera), turned a little right so the rod arm shows

COL = dict(skin="#f2c49a", shirt="#d23c34", vest="#5c6c34", jeans="#34548e", boots="#5a3a1c", hat="#c9b078",
           band="#6a4a2a", hair="#4a3020")


def limb(name, a, b, r0, r1, mat):
    return C.tube_along(name, [a, (Vector(a) + Vector(b)) / 2, b], [r0, (r0 + r1) / 2, r1], mat, 10)


def two_bone(sh, target, l1, l2, bend_sign=-1):
    """Analytic 2-bone IK in the local XZ plane. Returns (elbow, hand)."""
    s = Vector((sh[0], sh[2]))
    t = Vector((target[0], target[1]))
    d = t - s
    dist = min(d.length, l1 + l2 - 1e-3)
    d.normalize()
    a = math.acos(max(-1, min(1, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist))))
    base = math.atan2(d.y, d.x)
    ang = base + bend_sign * a
    e = s + Vector((math.cos(ang), math.sin(ang))) * l1
    return (e.x, sh[1], e.y), (s.x + d.x * dist, sh[1], s.y + d.y * dist)


def build(pose):
    p = POSES[pose]
    objs = []
    skin = M("Skin", COL["skin"])
    shirt = M("Shirt", COL["shirt"])
    vest = C.pattern_material("Vest", COL["vest"], "#4a5828", kind="stripes", scale=6, thresh=0.12, axis=2)
    jeans = M("Jeans", COL["jeans"])
    boots = M("Boots", COL["boots"], shine=0.3)
    hat = M("Hat", COL["hat"])
    band = M("Band", COL["band"])
    # legs (stance) - chunky chibi proportions, model faces +X locally
    for sx, y in ((-0.13, 0.13), (0.15, -0.13)):
        objs.append(limb("Leg", (sx * 0.6, y, 0.78), (sx, y, 0.2), 0.15, 0.14, jeans))
        objs.append(C.tube_along("Boot", [(sx - 0.05, y, 0.2), (sx + 0.02, y, 0.08), (sx + 0.19, y, 0.06)],
                                 [0.15, 0.14, 0.1], boots, 10))
    torso = []
    torso.append(C.tube_along("Hips", [(0, 0, 0.7), (0, 0, 0.86)], [0.22, 0.23], jeans, 14))
    torso.append(C.tube_along("Torso", [(0, 0, 0.8), (0.02, 0, 1.1), (0.0, 0, 1.38)], [0.25, 0.27, 0.22], shirt, 14))
    torso.append(C.tube_along("Vest", [(0.0, 0, 0.86), (0.02, 0, 1.1), (0.0, 0, 1.33)], [0.275, 0.29, 0.235], vest, 14))
    torso.append(C.tube_along("Neck", [(0.02, 0, 1.33), (0.03, 0, 1.43)], 0.09, skin, 8))
    # creel strap across the back
    torso.append(C.tube_along("Strap", [(-0.27, 0.2, 1.3), (-0.31, 0.0, 1.05), (-0.24, -0.22, 0.82)], 0.045, band, 6))
    head = [
        C.add_prim("sphere", "Head", skin, radius=0.27, location=(0.05, 0, 1.62), segments=16, ring_count=10),
        C.add_prim("sphere", "Ear", skin, radius=0.07, location=(0.02, -0.26, 1.6), segments=8, ring_count=6),
        C.add_prim("sphere", "Ear2", skin, radius=0.07, location=(0.02, 0.26, 1.6), segments=8, ring_count=6),
        C.add_prim("sphere", "Hair", M("Hair", COL["hair"]), radius=0.275, location=(-0.06, 0.0, 1.6), segments=12, ring_count=8),
        C.tube_along("Crown", [(0.03, 0, 1.74), (0.03, 0, 1.98)], [0.27, 0.2], hat, 14),
        C.tube_along("Band", [(0.03, 0, 1.76), (0.03, 0, 1.82)], 0.275, band, 14),
        C.tube_along("Brim", [(0.03, 0, 1.7), (0.03, 0, 1.75)], [0.48, 0.4], hat, 18),
    ]
    for ob in head:
        C.set_smooth(ob)
    torso += head
    sh_far = (0.02, 0.22, 1.28)
    sh_near = (0.02, -0.24, 1.28)
    lean = math.radians(p["lean"])
    rot = Matrix.Rotation(-lean, 4, "Y")
    pivot = Vector((0, 0, 0.75))

    def lean_pt(pt):
        v = Vector(pt) - pivot
        return tuple(rot @ v + pivot)

    for ob in torso:
        ob.matrix_world = Matrix.Translation(pivot) @ rot @ Matrix.Translation(-pivot) @ ob.matrix_world
    objs += torso
    objs.append(C.tube_along("Creel", [(-0.22, -0.3, 0.7), (-0.22, -0.3, 0.96)], [0.16, 0.19],
                             C.pattern_material("Creel", "#c8a060", "#a07840", kind="stripes", scale=40, thresh=0.5, axis=2), 10))
    hands = {}
    for side, sh, tgt in (("far", sh_far, p["far"]), ("near", sh_near, p["near"])):
        shw = lean_pt(sh)
        elbow, hand = two_bone(shw, tgt, 0.31, 0.29, bend_sign=-1 if tgt[0] > shw[0] - 0.1 else 1)
        objs.append(limb("Upper" + side, shw, elbow, 0.1, 0.09, shirt))
        objs.append(limb("Fore" + side, elbow, hand, 0.09, 0.08, shirt))
        objs.append(C.add_prim("sphere", "Hand" + side, skin, radius=0.095, location=hand, segments=10, ring_count=6))
        hands[side] = hand
    turn = Matrix.Scale(SCALE, 4) @ Matrix.Rotation(math.radians(TURN), 4, "Z")
    for ob in objs:
        ob.matrix_world = turn @ ob.matrix_world
    return objs, turn @ Vector(hands["near"])


def main():
    C.reset_scene()
    P.setup_camera(0.0)
    fx, fy, _ = P.project((0, 0, 0), 0.0)
    cx = int(round(P.W / 2 + fx))
    cy = int(round(P.H / 2 + fy))
    x0, y0 = cx - CROP_W // 2, cy - FEET_PX
    data = {"cropW": CROP_W, "cropH": CROP_H, "feetPx": FEET_PX, "poses": []}
    paths = []
    raw = os.path.join(C.TMP, "char_raw.png")
    for pose, pd in POSES.items():
        C.clear_objects()
        _, hand = build(pose)
        bpy.context.view_layer.update()
        C.render_raw(raw)
        arr = C.load_pixels(raw)[y0:y0 + CROP_H, x0:x0 + CROP_W]
        path = os.path.join(OUT, f"angler_{pose}.png")
        C.save_pixels(C.pixelize(arr), path)
        hx, hy, _ = P.project(hand, 0.0)
        rod = Vector(pd["rod"]).normalized()
        data["poses"].append({
            "name": pose,
            "handX": round(P.W / 2 + hx - x0, 2), "handY": round(P.H / 2 + hy - y0, 2),
            "hx": round(hand.x, 3), "hy": round(hand.z, 3), "hz": round(hand.y, 3),
            "rx": round(rod.x, 3), "ry": round(rod.y, 3), "rz": round(rod.z, 3),
        })
        paths.append(path)
    C.write_json("character.json", data)
    C.contact_sheet(paths, os.path.join(C.TMP, "char_sheet.png"), scale=4, cols=7)
    print("CHAR done", data)


if __name__ == "__main__":
    main()
