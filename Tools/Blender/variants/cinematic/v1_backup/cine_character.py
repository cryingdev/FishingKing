"""
FishingKing - cinematic variant: the angler seen from behind (realistic ~7.3 heads, muted outfit),
backlit by the low golden-hour sun ahead-right -> dark body, warm rim on the right/top edges.

Rendered through the shared stage camera (fk_persp.setup_camera(0.0)), 96x112 crop, feet 10 px above
the bottom, exactly like fk_character.py, and writes character.json in the same format.

Run:  blender -b --python Tools/Blender/variants/cinematic/cine_character.py
Out:  _tmp/variants/cinematic/character/angler_<pose>.png, character.json, character_sheet.png
"""
import sys
import os
import math
import json
import bmesh
import bpy
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cine_common as K  # noqa: E402

C, P = K.C, K.P
OUTD = os.path.join(K.OUT, "character")
CROP_W, CROP_H, FEET_PX = 96, 112, 10
TURN = 68.0   # local +X (forward) -> world ~+Y, turned a little right so the rod arm shows
HEIGHT = 1.76

# Hand targets in the local frame (forward, lateral(+left), up) in metres, torso lean (deg, + = forward)
# and the rod direction in Unity axes (x right, y up, z forward) - same rod vectors as fk_character.
POSES = {
    "idle": dict(near=(0.30, -0.10, 1.02), far=(0.40, 0.00, 1.12), lean=3, rod=(0.22, 0.72, 0.66)),
    "aim": dict(near=(-0.06, -0.16, 1.66), far=(0.06, -0.05, 1.56), lean=-6, rod=(0.25, 0.72, -0.64)),
    "cast": dict(near=(0.50, -0.08, 1.40), far=(0.46, 0.02, 1.48), lean=10, rod=(0.08, 0.3, 0.95)),
    "reel": dict(near=(0.30, -0.10, 1.00), far=(0.40, 0.02, 1.12), lean=2, rod=(0.2, 0.62, 0.76)),
    "reel2": dict(near=(0.28, -0.12, 1.12), far=(0.40, 0.02, 1.10), lean=2, rod=(0.2, 0.62, 0.76)),
    "fight": dict(near=(0.30, -0.08, 1.26), far=(0.40, 0.02, 1.42), lean=-12, rod=(0.12, 0.9, 0.42)),
    "cheer": dict(near=(0.14, -0.26, 1.93), far=(0.14, 0.26, 1.93), lean=-2, rod=(0.45, 0.75, 0.3)),
}

# muted outfit (backlit, so most of it is seen in shadow tones)
COL = dict(jacket="#56603f", vest="#8e7e5a", vest2="#807052", pants="#4a5256", boots="#3e2e22",
           skin="#c98e6a", hair="#2e231c", hat="#a08e68", band="#4a4234", strap="#4a3c30", bag="#5a5040",
           sole="#1e1a18")

RIG = K.Rig(key=(0.0, 0.0, 1.0), key_col="#ffd8a8", fill_col="#7c86a2", bounce_col="#2f6468",
            shadow_col="#58607c", rim_col="#ffe0aa", mid_col="#b4aaa8")


def set_rig():
    a, e = math.radians(55.0), math.radians(24.0)
    RIG.key = np.array((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)))
    K.RIG[0] = RIG


def M(key, **kw):
    return K.mcine(COL[key] if key in COL else key, name=key, **kw)


# ----------------------------------------------------------------------------- geometry helpers
def loft(name, rings, mat, seg=18, cap=True):
    """rings: (cx, cy, z, rx, ry) ellipses (rx forward, ry lateral), bottom -> top."""
    bm = bmesh.new()
    rv = []
    for (cx, cy, z, rx, ry) in rings:
        rv.append([bm.verts.new((cx + rx * math.cos(2 * math.pi * k / seg), cy + ry * math.sin(2 * math.pi * k / seg), z))
                   for k in range(seg)])
    for i in range(len(rv) - 1):
        for k in range(seg):
            k2 = (k + 1) % seg
            bm.faces.new((rv[i][k], rv[i][k2], rv[i + 1][k2], rv[i + 1][k]))
    if cap:
        bm.faces.new(rv[0][::-1])
        bm.faces.new(rv[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = C.mesh_object(name, bm, mat)
    C.set_smooth(ob)
    return ob


def ellipsoid(name, c, r, mat, seg=16, rings=10):
    ob = C.add_prim("sphere", name, mat, radius=1.0, location=c, segments=seg, ring_count=rings)
    ob.matrix_world = ob.matrix_world @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    C.set_smooth(ob)
    return ob


def tube(name, pts, radii, mat, segs=12):
    return C.tube_along(name, pts, radii, mat, segs)


def ik(sh, target, l1, l2, pole):
    S, T, pv = Vector(sh), Vector(target), Vector(pole)
    d = T - S
    dist = min(d.length, l1 + l2 - 1e-3)
    dn = d.normalized()
    a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
    h = math.sqrt(max(l1 * l1 - a * a, 0.0))
    pp = (pv - dn * pv.dot(dn))
    pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, -1))
    elbow = S + dn * a + pp * h
    hand = S + dn * dist
    return elbow, hand


# ----------------------------------------------------------------------------- the angler
def build(pose):
    p = POSES[pose]
    objs = []
    jacket = M("jacket")
    pants = M("pants")
    boots = M("boots", spec=0.6)
    skin = M("skin")
    hair = M("hair")
    hat = M("hat")
    band = M("band")
    vest = M("vest")
    strap = M("strap")
    bag = M("bag")
    sole = M("sole")

    # legs - stance a bit wider than the hips, right foot slightly back
    for side, fy, fx in ((1, 0.14, 0.03), (-1, -0.15, -0.04)):
        hip = (0.0, side * 0.09, 0.90)
        knee = (fx * 0.5 + 0.035, side * 0.12, 0.50)
        ankle = (fx, fy, 0.11)
        objs.append(tube("Thigh", [hip, ((hip[0] + knee[0]) / 2, (hip[1] + knee[1]) / 2, 0.70), knee],
                         [0.088, 0.078, 0.062], pants, 14))
        objs.append(tube("Shin", [knee, (knee[0] - 0.01, (knee[1] + ankle[1]) / 2, 0.32), ankle],
                         [0.062, 0.060, 0.050], pants, 14))
        # boot: shaft + foot
        objs.append(tube("BootShaft", [(fx, fy, 0.08), (fx, fy, 0.25)], [0.060, 0.058], boots, 14))
        objs.append(tube("BootFoot", [(fx - 0.07, fy, 0.055), (fx + 0.03, fy, 0.055), (fx + 0.16, fy, 0.045)],
                         [0.058, 0.056, 0.036], boots, 12))
        objs.append(tube("Sole", [(fx - 0.075, fy, 0.012), (fx + 0.17, fy, 0.012)], [0.05, 0.04], sole, 10))

    torso = []
    torso.append(loft("Pelvis", [(0, 0, 0.80, 0.12, 0.16), (0, 0, 0.90, 0.125, 0.165), (0, 0, 0.98, 0.12, 0.16)], pants))
    # jacket (hem flare -> waist -> chest -> shoulders -> collar)
    torso.append(loft("Jacket", [(0.0, 0, 0.78, 0.145, 0.195), (0.0, 0, 0.90, 0.135, 0.18), (0.0, 0, 1.04, 0.125, 0.172),
                                 (0.0, 0, 1.20, 0.135, 0.19), (0.0, 0, 1.33, 0.14, 0.2), (-0.005, 0, 1.42, 0.125, 0.205),
                                 (-0.01, 0, 1.47, 0.09, 0.15), (-0.01, 0, 1.50, 0.065, 0.075)], jacket, 20))
    # vest over the jacket (shorter, boxier)
    torso.append(loft("Vest", [(0.0, 0, 0.93, 0.15, 0.195), (0.0, 0, 1.04, 0.14, 0.186), (0.0, 0, 1.20, 0.148, 0.2),
                               (0.0, 0, 1.33, 0.152, 0.205), (-0.01, 0, 1.415, 0.13, 0.17), (-0.012, 0, 1.45, 0.1, 0.12)],
                      vest, 20))
    # back pocket panel on the vest + D-ring
    torso.append(K.C.poly_object("Pocket", [(-0.12, 0.99), (0.12, 0.99), (0.12, 1.2), (-0.12, 1.2)],
                                 K.mcine(COL["vest2"], name="pocket"), thickness=0.01))
    torso[-1].matrix_world = Matrix.Translation((-0.152, 0, 0)) @ Matrix.Rotation(math.radians(90), 4, "Z")
    # sling strap across the back (right shoulder -> left hip) + bag
    torso.append(tube("Strap", [(-0.06, -0.15, 1.46), (-0.155, -0.02, 1.25), (-0.16, 0.12, 1.02), (-0.08, 0.2, 0.88)],
                      0.022, strap, 8))
    torso.append(loft("Bag", [(-0.02, 0.215, 0.80, 0.09, 0.05), (-0.02, 0.22, 0.95, 0.1, 0.055)], bag, 12))
    # shoulders
    for side in (1, -1):
        torso.append(ellipsoid("Delt", (-0.01, side * 0.19, 1.405), (0.075, 0.07, 0.07), jacket))
    # neck, collar, head
    torso.append(tube("Neck", [(0.0, 0, 1.46), (0.01, 0, 1.56)], [0.055, 0.05], skin, 12))
    torso.append(loft("Collar", [(-0.005, 0, 1.475, 0.075, 0.085), (-0.01, 0, 1.53, 0.07, 0.078)], jacket, 16, cap=False))
    torso.append(ellipsoid("Head", (0.015, 0, 1.635), (0.1, 0.082, 0.112), skin))
    torso.append(ellipsoid("Hair", (-0.012, 0, 1.64), (0.092, 0.084, 0.1), hair))
    for side in (1, -1):
        torso.append(ellipsoid("Ear", (0.01, side * 0.083, 1.63), (0.022, 0.012, 0.03), skin, 8, 6))
    # hat: soft crown + drooping brim + band
    torso.append(loft("Crown", [(0.005, 0, 1.685, 0.108, 0.1), (0.005, 0, 1.73, 0.104, 0.096), (0.0, 0, 1.775, 0.092, 0.084),
                                (0.0, 0, 1.79, 0.07, 0.064)], hat, 18))
    torso.append(loft("Band", [(0.005, 0, 1.688, 0.111, 0.103), (0.005, 0, 1.712, 0.109, 0.101)], band, 18, cap=False))
    torso.append(loft("Brim", [(0.01, 0, 1.655, 0.2, 0.19), (0.008, 0, 1.672, 0.16, 0.15), (0.005, 0, 1.69, 0.11, 0.102)],
                      hat, 22, cap=False))

    lean = math.radians(p["lean"])
    rot = Matrix.Rotation(lean, 4, "Y")
    pivot = Vector((0, 0, 0.86))
    L = Matrix.Translation(pivot) @ rot @ Matrix.Translation(-pivot)
    for ob in torso:
        ob.matrix_world = L @ ob.matrix_world
    objs += torso

    hands = {}
    for side, key in ((1, "far"), (-1, "near")):
        sh = L @ Vector((-0.005, side * 0.195, 1.40))
        tgt = Vector(p[key])
        pole = Vector((-0.6, side * 0.8, -0.5))
        if tgt.z > 1.5:
            pole = Vector((0.2, side * 1.0, -0.2))
        elbow, hand = ik(sh, tgt, 0.30, 0.27, pole)
        objs.append(tube("Upper", [sh, (sh + elbow) / 2, elbow], [0.056, 0.052, 0.046], jacket, 12))
        objs.append(ellipsoid("Elbow", elbow, (0.047, 0.047, 0.047), jacket, 10, 8))
        fdir = (hand - elbow).normalized()
        wrist = hand - fdir * 0.035
        objs.append(tube("Fore", [elbow, (elbow + wrist) / 2, wrist], [0.046, 0.044, 0.038], jacket, 12))
        objs.append(ellipsoid("Hand", hand + fdir * 0.01, (0.042, 0.036, 0.04), skin, 10, 8))
        hands[key] = hand

    turn = Matrix.Rotation(math.radians(TURN), 4, "Z")
    for ob in objs:
        ob.matrix_world = turn @ ob.matrix_world
    return objs, turn @ hands["near"]


# ----------------------------------------------------------------------------- post-process
RIM = K.hexf("#f4c890")


def post(arr):
    """arr: top-down RGBA crop straight from EEVEE."""
    a = K.harden(arr)
    a = K.grade(a, sat=0.88, split=0.8, contrast=0.06)
    a, rim = K.rim_light(a, RIM, strength=0.55, right=1.0, top=0.55, topright=0.8)
    a = K.outline(a, col=(0.045, 0.06, 0.075), mul=0.3, skip=None)
    return a


def rim_weight(a):
    """Rim light is strongest on the hat/shoulders/arms and fades down the legs (sun is low but the
    pier and the body itself shade the lower half)."""
    rows = np.where(a[..., 3].max(1) > 0.5)[0]
    top, bot = rows.min(), rows.max()
    t = (np.arange(a.shape[0], dtype=np.float32) - top) / max(bot - top, 1)
    w = np.clip(1.2 - t * 1.35, 0.12, 1.0)
    return np.repeat(w[:, None], a.shape[1], 1)


def main():
    C.reset_scene()
    set_rig()
    P.setup_camera(0.0)
    fx, fy, _ = P.project((0, 0, 0), 0.0)
    cx = int(round(P.W / 2 + fx))
    cy = int(round(P.H / 2 + fy))  # bottom-up
    x0, y0 = cx - CROP_W // 2, cy - FEET_PX  # bottom-up crop origin (same as fk_character)
    r0 = P.H - (y0 + CROP_H)  # top-down first row
    data = {"cropW": CROP_W, "cropH": CROP_H, "feetPx": FEET_PX, "style": "cinematic", "poses": []}
    crops = {}
    hands = {}
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    poses = [k for k in POSES if not only or k in only]
    for pose in poses:
        pd = POSES[pose]
        C.clear_objects()
        K._mats.clear()
        _, hand = build(pose)
        hands[pose] = hand
        bpy.context.view_layer.update()
        img = K.render("char_" + pose)
        crop = img[r0:r0 + CROP_H, x0:x0 + CROP_W]
        crops[pose] = crop
    # one shared palette for all poses so frames never flicker
    graded = {k: K.grade(K.harden(v), sat=0.88, split=0.8, contrast=0.06) for k, v in crops.items()}
    stack = np.concatenate([g for g in graded.values()], 0)
    pal = K.kmeans_palette(stack, 22, mask=stack[..., 3] > 0.5, weight_pow=0.7)
    paths = []
    for pose in poses:
        pd = POSES[pose]
        g = K.quantize(graded[pose], pal)
        g[..., 3] = graded[pose][..., 3]
        g, _ = K.rim_light(g, RIM, strength=0.6, right=1.0, top=0.8, topright=0.9, weight=rim_weight(g))
        g = K.outline(g, col=(0.045, 0.06, 0.075), mul=0.3)
        path = os.path.join(OUTD, f"angler_{pose}.png")
        K.save(g, path)
        paths.append(path)
    # rod anchors (rebuild each pose for the hand position; cheap)
    for pose in poses:
        pd = POSES[pose]
        hand = hands[pose]
        hx, hy, _ = P.project(hand, 0.0)
        rod = Vector(pd["rod"]).normalized()
        data["poses"].append({
            "name": pose,
            "handX": round(P.W / 2 + hx - x0, 2), "handY": round(P.H / 2 + hy - y0, 2),
            "hx": round(hand.x, 3), "hy": round(hand.z, 3), "hz": round(hand.y, 3),
            "rx": round(rod.x, 3), "ry": round(rod.y, 3), "rz": round(rod.z, 3),
        })
    with open(os.path.join(OUTD, "character.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    sheet(paths, os.path.join(K.OUT, "character_sheet.png"))
    print("CHAR done", [p["name"] for p in data["poses"]])


def sheet(paths, out, scale=4, bg=(0.42, 0.42, 0.44)):
    imgs = [K.load(p) for p in paths]
    cw = max(i.shape[1] for i in imgs) * scale + 16
    ch = max(i.shape[0] for i in imgs) * scale + 16
    s = np.zeros((ch, cw * len(imgs), 4), np.float32)
    s[..., :3] = bg
    s[..., 3] = 1
    for k, im in enumerate(imgs):
        big = K.upscale(im, scale)
        y0, x0 = 8, k * cw + 8
        reg = s[y0:y0 + big.shape[0], x0:x0 + big.shape[1]]
        a = big[..., 3:4]
        reg[..., :3] = reg[..., :3] * (1 - a) + big[..., :3] * a
    K.save(s, out)


if __name__ == "__main__":
    main()
