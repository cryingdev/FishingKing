"""
FishingKing - cinematic variant: the angler seen from behind (realistic ~7.3 heads, muted outfit),
backlit by the low golden-hour sun ahead-right -> dark body, warm rim on the OUTER silhouette only.

Rendered through the shared stage camera (fk_persp.setup_camera(0.0)), 96x112 crop, feet 10 px above
the bottom, exactly like fk_character.py, and writes character.json in the same format.

Three Blender passes per pose (all cheap, emission only):
  shade : the cinematic banded shader lit by the painted sun (azimuth of SUN_PX, 3 deg up)
  cap   : the same with the "hot" band clamped to "lit" -> used for pixels > 1 px inside the silhouette
  id    : flat class colours (hair, ear, collar, ...) for region-aware pixel post (nape glow, collar line)

Run:  blender -b --python Tools/Blender/variants/cinematic/cine_character.py [-- pose ...]
Out:  _tmp/variants/cinematic/character/angler_<pose>.png, character.json, character_sheet.png,
      silhouettes.png (flat black check at 1x and 3x)
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

# Hand targets in the local frame (forward, lateral(+left), up) in metres, torso lean (deg, + = forward),
# the rod direction in Unity axes (x right, y up, z forward) - same rod vectors as fk_character - and
# the stance: feet = ((fx, fy) far/left foot, (fx, fy) near/right foot), pelvis = (forward, drop),
# heel = (far, near) heel raise (m), lift = whole body raise (m, on the toes).
POSES = {
    "idle": dict(near=(0.30, -0.12, 1.02), far=(0.40, 0.00, 1.12), lean=3, rod=(0.22, 0.72, 0.66),
                 feet=((0.03, 0.14), (-0.05, -0.15)), pelvis=(0.0, -0.01)),
    "aim": dict(near=(-0.08, -0.18, 1.68), far=(0.05, -0.06, 1.58), lean=-9, rod=(0.25, 0.72, -0.64),
                feet=((0.16, 0.14), (-0.16, -0.17)), pelvis=(-0.05, -0.02), heel=(0.05, 0.0)),
    "cast": dict(near=(0.64, -0.12, 1.36), far=(0.54, -0.03, 1.42), lean=15, rod=(0.08, 0.3, 0.95),
                 feet=((0.27, 0.13), (-0.20, -0.17)), pelvis=(0.10, -0.03), heel=(0.0, 0.07)),
    "reel": dict(near=(0.27, -0.18, 0.97), far=(0.40, 0.00, 1.12), lean=5, rod=(0.2, 0.62, 0.76),
                 feet=((0.04, 0.15), (-0.06, -0.15)), pelvis=(0.0, -0.015)),
    "reel2": dict(near=(0.31, -0.195, 1.025), far=(0.40, 0.00, 1.12), lean=5, rod=(0.2, 0.62, 0.76),
                  feet=((0.04, 0.15), (-0.06, -0.15)), pelvis=(0.0, -0.015)),
    "fight": dict(near=(0.12, -0.24, 1.34), far=(0.19, -0.13, 1.62), lean=-18, rod=(0.12, 0.9, 0.42),
                  feet=((0.10, 0.225), (-0.20, -0.225)), pelvis=(0.08, -0.08)),
    "cheer": dict(near=(0.10, -0.30, 1.96), far=(0.10, 0.30, 1.96), lean=-4, rod=(0.45, 0.75, 0.3),
                  feet=((0.02, 0.16), (-0.02, -0.16)), pelvis=(0.0, 0.0), heel=(0.06, 0.06), lift=0.055),
}

# muted outfit (backlit, so most of it is seen in shadow tones); vest one value darker than the jacket
COL = dict(jacket="#56603f", vest="#5e5a40", pants="#4d4f4c", boots="#3e2e22",
           skin="#c98e6a", hair="#2e231c", hat="#6a5c46", band="#3a342a", strap="#3e342a", bag="#4e4636",
           sole="#1e1a18")
# class ids for the id pass (region-aware post)
IDS = dict(jacket=1, vest=2, pants=3, boots=4, skin=5, hair=6, hat=7, band=8, strap=9, bag=10, sole=11,
           head=12, ear=13, collar=14, neck=15)

RIG = K.Rig(key=(0.0, 1.0, 0.0), key_col="#ffd8a8", fill_col="#7c86a2", bounce_col="#2f6468",
            shadow_col="#58607c", rim_col="#ffe0aa", mid_col="#b4aaa8")
MODE = ["shade"]


def set_rig():
    """The angler is lit by the same sun as the stage: the painted sun's azimuth, lifted to 3 deg."""
    RIG.key = K.sun_key()
    K.RIG[0] = RIG


def M(key, cls=None, **kw):
    cls = cls or key
    if MODE[0] == "id":
        return K.mflat(K.id_hex(IDS[cls]), name="id_" + cls)
    return K.mcine(COL[key] if key in COL else key, name=cls, **kw)


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
THIGH, SHIN = 0.40, 0.385


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
    lift = p.get("lift", 0.0)
    pdx, pdz = p.get("pelvis", (0.0, 0.0))
    heels = p.get("heel", (0.0, 0.0))

    # legs: two-bone IK from the hip to the ankle, knee pushed forward (and a little outward)
    for side, (fx, fy), hr in ((1, p["feet"][0], heels[0]), (-1, p["feet"][1], heels[1])):
        hip = Vector((pdx, side * 0.085, 0.90 + pdz + lift))
        ankle = Vector((fx, fy, 0.11 + hr))
        knee, ankle2 = ik(hip, ankle, THIGH, SHIN, Vector((1.0, side * 0.25, 0.0)))
        objs.append(tube("Thigh", [hip, (hip + knee) / 2, knee], [0.084, 0.074, 0.06], pants, 14))
        objs.append(tube("Shin", [knee, (knee + ankle2) / 2, ankle2], [0.06, 0.057, 0.048], pants, 14))
        objs.append(ellipsoid("Knee", knee, (0.062, 0.062, 0.066), pants, 12, 8))
        # boot: shaft + foot (toe stays down when the heel is raised)
        objs.append(tube("BootShaft", [(fx, fy, 0.08 + hr), (fx - 0.005, fy, 0.25 + hr * 0.8)], [0.058, 0.056],
                         boots, 14))
        objs.append(tube("BootFoot", [(fx - 0.07, fy, 0.055 + hr), (fx + 0.03, fy, 0.055 + hr * 0.45),
                                      (fx + 0.16, fy, 0.045)], [0.056, 0.054, 0.034], boots, 12))
        objs.append(tube("Sole", [(fx - 0.075, fy, 0.012 + hr), (fx + 0.17, fy, 0.012)], [0.048, 0.038], sole, 10))

    torso = []
    torso.append(loft("Pelvis", [(0, 0, 0.80, 0.115, 0.15), (0, 0, 0.90, 0.12, 0.155), (0, 0, 0.98, 0.115, 0.15)], pants))
    # jacket: hem (0.86) -> waist (tapered) -> chest -> shoulders (narrower) -> collar
    torso.append(loft("Jacket", [(0.0, 0, 0.86, 0.14, 0.168), (0.0, 0, 0.95, 0.13, 0.156), (0.0, 0, 1.04, 0.125, 0.15),
                                 (0.0, 0, 1.20, 0.135, 0.16), (0.0, 0, 1.33, 0.138, 0.165), (-0.005, 0, 1.42, 0.123, 0.165),
                                 (-0.01, 0, 1.47, 0.09, 0.125), (-0.01, 0, 1.50, 0.065, 0.072)], jacket, 20))
    # vest over the jacket (shorter), one value darker, no back pocket slab
    torso.append(loft("Vest", [(0.0, 0, 0.95, 0.14, 0.162), (0.0, 0, 1.04, 0.134, 0.156), (0.0, 0, 1.20, 0.143, 0.166),
                               (0.0, 0, 1.33, 0.146, 0.17), (-0.01, 0, 1.415, 0.128, 0.152), (-0.012, 0, 1.45, 0.1, 0.11)],
                      vest, 20))
    # sling strap across the back (right shoulder -> left hip) + bag
    torso.append(tube("Strap", [(-0.06, -0.13, 1.46), (-0.152, -0.02, 1.25), (-0.152, 0.1, 1.03), (-0.08, 0.17, 0.9)],
                      0.02, strap, 8))
    torso.append(loft("Bag", [(-0.02, 0.19, 0.80, 0.085, 0.048), (-0.02, 0.195, 0.94, 0.095, 0.052)], bag, 12))
    # shoulders
    for side in (1, -1):
        torso.append(ellipsoid("Delt", (-0.01, side * 0.16, 1.405), (0.072, 0.068, 0.068), jacket))
    # neck, collar, head
    torso.append(tube("Neck", [(0.0, 0, 1.46), (0.01, 0, 1.56)], [0.052, 0.048], M("skin", "neck"), 12))
    torso.append(loft("Collar", [(-0.005, 0, 1.475, 0.075, 0.082), (-0.01, 0, 1.53, 0.07, 0.075)], M("jacket", "collar"),
                      16, cap=False))
    torso.append(ellipsoid("Head", (0.015, 0, 1.635), (0.1, 0.082, 0.112), M("skin", "head")))
    torso.append(ellipsoid("Hair", (-0.012, 0, 1.64), (0.092, 0.084, 0.1), hair))
    for side in (1, -1):
        torso.append(ellipsoid("Ear", (0.01, side * 0.083, 1.63), (0.022, 0.014, 0.032), M("skin", "ear"), 8, 6))
    # hat: soft crown + small drooping brim + band
    torso.append(loft("Crown", [(0.005, 0, 1.685, 0.106, 0.098), (0.005, 0, 1.73, 0.102, 0.094), (0.0, 0, 1.772, 0.09, 0.082),
                                (0.0, 0, 1.787, 0.068, 0.062)], hat, 18))
    torso.append(loft("Band", [(0.005, 0, 1.688, 0.109, 0.101), (0.005, 0, 1.71, 0.107, 0.099)], band, 18, cap=False))
    torso.append(loft("Brim", [(0.01, 0, 1.658, 0.16, 0.152), (0.008, 0, 1.674, 0.135, 0.127), (0.005, 0, 1.69, 0.108, 0.1)],
                      hat, 22, cap=False))

    lean = math.radians(p["lean"])
    rot = Matrix.Rotation(lean, 4, "Y")
    pivot = Vector((0, 0, 0.86))
    L = Matrix.Translation((pdx, 0, pdz + lift)) @ Matrix.Translation(pivot) @ rot @ Matrix.Translation(-pivot)
    for ob in torso:
        ob.matrix_world = L @ ob.matrix_world
    objs += torso

    hands = {}
    for side, key in ((1, "far"), (-1, "near")):
        sh = L @ Vector((-0.005, side * 0.168, 1.40))
        tgt = Vector(p[key]) + Vector((0, 0, lift))
        pole = Vector((-0.6, side * 0.8, -0.5))
        if tgt.z > 1.5 + lift:
            pole = Vector((0.2, side * 1.0, -0.2))
        elbow, hand = ik(sh, tgt, 0.30, 0.27, pole)
        objs.append(tube("Upper", [sh, (sh + elbow) / 2, elbow], [0.054, 0.05, 0.045], jacket, 12))
        objs.append(ellipsoid("Elbow", elbow, (0.046, 0.046, 0.046), jacket, 10, 8))
        fdir = (hand - elbow).normalized()
        wrist = hand - fdir * 0.035
        objs.append(tube("Fore", [elbow, (elbow + wrist) / 2, wrist], [0.045, 0.043, 0.037], jacket, 12))
        objs.append(ellipsoid("Hand", hand + fdir * 0.01, (0.041, 0.035, 0.039), skin, 10, 8))
        hands[key] = hand

    turn = Matrix.Rotation(math.radians(TURN), 4, "Z")
    for ob in objs:
        ob.matrix_world = turn @ ob.matrix_world
    return objs, turn @ hands["near"]


# ----------------------------------------------------------------------------- post-process
RIM = K.hexf("#f4c890")
GLOW = K.hexf("#c0704a")       # backlit ear / nape (light through skin)
OUTLINE = K.hexf("#0e2226")


def rim_weight(a):
    """Rim strongest on the hat/shoulders/arms, fading down the legs (the low sun grazes the whole figure,
    but a full-height gold stroke reads as a sticker)."""
    rows = np.where(a[..., 3].max(1) > 0.5)[0]
    top, bot = rows.min(), rows.max()
    t = (np.arange(a.shape[0], dtype=np.float32) - top) / max(bot - top, 1)
    w = np.clip(1.15 - t * 1.2, 0.3, 1.0)
    return np.repeat(w[:, None], a.shape[1], 1)


def head_details(g, ids):
    """Ear / nape glow on the sun side of the head + a 1 px lighter collar line under the head."""
    head = np.isin(ids, [IDS["hair"], IDS["head"], IDS["ear"], IDS["neck"]])
    if not head.any():
        return g
    out = g.copy()
    rows = np.where(head.any(1))[0]
    r0, r1 = rows.min(), rows.max()
    # right-edge head pixels in the lower 60% of the head (nape + ear), max 3
    right_free = ~K.shift(head, 0, -1, False)
    cand = head & right_free
    cand[:r0 + int((r1 - r0) * 0.4) + 1] = False
    ys, xs = np.where(cand)
    order = np.argsort(-ys)[:3]
    for y, x in zip(ys[order], xs[order]):
        out[y, x, :3] = K.mix(out[y, x, :3], GLOW, 0.8)
    ear = ids == IDS["ear"]
    out[ear, :3] = K.mix(out[ear, :3], GLOW, 0.7)
    # collar line: jacket/collar pixels right below the head/neck
    body = np.isin(ids, [IDS["collar"], IDS["jacket"], IDS["vest"]])
    below_head = K.shift(head, 1, 0, False)
    line = body & below_head
    out[line, :3] = np.clip(out[line, :3] * 1.45 + K.hexf("#3a2a10") * 0.35, 0, 1)
    return out


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
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    poses = [k for k in POSES if not only or k in only]
    crops, hands = {}, {}
    for pose in poses:
        passes = {}
        for mode in ("shade", "cap", "id"):
            MODE[0] = mode
            K.CAP_HOT[0] = mode == "cap"
            C.clear_objects()
            K._mats.clear()
            _, hand = build(pose)
            hands[pose] = hand
            bpy.context.view_layer.update()
            img = K.render(f"char_{pose}_{mode}")
            passes[mode] = K.harden(img[r0:r0 + CROP_H, x0:x0 + CROP_W])
        K.CAP_HOT[0] = False
        MODE[0] = "shade"
        crops[pose] = passes

    # composite: silhouette edge from the full shader, interior (> 1 px from the outer silhouette) capped
    comp, idmaps = {}, {}
    for pose, ps in crops.items():
        a = ps["shade"][..., 3] > 0.5
        oe = K.outer_empty(a, 3)
        edge = a & K.dilate(oe, 4)
        img = np.where(edge[..., None], ps["shade"], ps["cap"])
        comp[pose] = K.grade(img, sat=0.88, split=0.8, contrast=0.06)
        idmaps[pose] = K.decode_ids(ps["id"])
    # one shared palette for all poses so frames never flicker
    stack = np.concatenate(list(comp.values()), 0)
    pal = K.kmeans_palette(stack, 22, mask=stack[..., 3] > 0.5, weight_pow=0.7)
    paths, sil = [], []
    for pose in poses:
        g = K.quantize(comp[pose], pal)
        g[..., 3] = comp[pose][..., 3]
        g = head_details(g, idmaps[pose])
        g, _ = K.rim_outer(g, RIM, strength=0.62, weight=rim_weight(g))
        g = K.outline(g, col=OUTLINE, mul=0.5, sides=("l", "b"))
        path = os.path.join(OUTD, f"angler_{pose}.png")
        K.save(g, path)
        paths.append(path)
        sil.append(g[..., 3] > 0.5)
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
    if len(poses) == len(POSES):
        sheet(paths, os.path.join(K.OUT, "character_sheet.png"))
        silhouettes(sil, os.path.join(K.OUT, "silhouettes.png"))
    for pose, s in zip(poses, sil):
        rows = np.where(s.any(1))[0]
        print("CHAR", pose, "height_px", rows.max() - rows.min() + 1)
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


def silhouettes(masks, out):
    """Flat black silhouettes: a 1x strip (true game size) above a 3x strip."""
    h, w = masks[0].shape
    one = np.ones((h, w * len(masks), 4), np.float32)
    one[..., :3] = 0.62
    for k, m in enumerate(masks):
        one[:, k * w:(k + 1) * w][m, :3] = 0.05
    big = K.upscale(one, 3)
    pad = np.ones((8, big.shape[1], 4), np.float32)
    pad[..., :3] = 0.62
    top = np.ones((h, big.shape[1], 4), np.float32)
    top[..., :3] = 0.62
    top[:, :one.shape[1]] = one
    K.save(np.concatenate([top, pad, big], 0), out)


if __name__ == "__main__":
    main()
