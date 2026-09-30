"""
FishingKing - "natural" variant angler: a ~1.76 m adult seen from behind (7.5 heads tall), muted
outdoor clothing (slate shirt, khaki fishing vest with back pocket + mesh yoke, olive work
trousers, brown leather boots, sand boonie hat), rendered through the shared stage camera.

Writes (only) into Tools/Blender/_tmp/variants/natural/:
  character/angler_<pose>.png   96x112, feet 10 px above the bottom (same crop as fk_character)
  character.json                same schema as Data/character.json
  character_sheet.png           poses side by side, 4x on mid-grey

Run: blender -b --python Tools/Blender/variants/natural/nat_character.py
"""
import sys
import os
import math
import json

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N  # noqa: E402
from nat_core import C, P, bpy, bmesh, np, Vector, Matrix  # noqa: E402

OUTD = os.path.join(N.OUT, "character")
CROP_W, CROP_H, FEET_PX = 96, 112, 10
SS = 4
TURN = 80.0          # facing +Y (away from the camera), turned slightly right so the rod arm shows

COL = dict(skin="#c39174", hair="#3b2d25", shirt="#5d6b7f", vest="#978b63", yoke="#716c52", pocket="#85794f",
           trousers="#4b4a3c", boots="#45331f", sole="#2f2620", hat="#ae9f78", band="#5b4a38", belt="#3c3028",
           net="#7b5d3d", mesh="#5c5a4c")

# Hand targets in the body frame: (forward, side[-=right/near], up), pole = elbow hint, lean deg (+ = forward),
# stance = (foot spread, near-foot forward, far-foot forward, knee bend), rod = Unity dir (x right, y up, z fwd)
POSES = {
    "idle": dict(near=(0.3, -0.25, 1.03), far=(0.42, -0.02, 1.14), lean=3, stance=(0.12, -0.03, 0.05, 0.0),
                 rod=(0.22, 0.72, 0.66)),
    "aim": dict(near=(-0.12, -0.2, 1.74), far=(0.0, -0.08, 1.66), lean=-5, stance=(0.14, -0.1, 0.12, 0.02),
                rod=(0.25, 0.72, -0.64), pole_up=True),
    "cast": dict(near=(0.52, -0.1, 1.46), far=(0.48, 0.0, 1.54), lean=10, stance=(0.14, 0.12, -0.06, 0.03),
                 rod=(0.08, 0.3, 0.95)),
    "reel": dict(near=(0.3, -0.24, 1.02), far=(0.44, 0.02, 1.12), lean=0, stance=(0.13, -0.03, 0.05, 0.01),
                 rod=(0.2, 0.62, 0.76)),
    "reel2": dict(near=(0.28, -0.24, 1.13), far=(0.44, 0.02, 1.12), lean=0, stance=(0.13, -0.03, 0.05, 0.01),
                  rod=(0.2, 0.62, 0.76)),
    "fight": dict(near=(0.3, -0.22, 1.28), far=(0.42, -0.02, 1.44), lean=-12, stance=(0.17, -0.12, 0.14, 0.07),
                  rod=(0.12, 0.9, 0.42)),
    "cheer": dict(near=(0.18, -0.26, 1.98), far=(0.16, 0.22, 1.96), lean=0, stance=(0.13, -0.02, 0.04, 0.0),
                  rod=(0.45, 0.75, 0.3), pole_up=True),
}

HIP_Z, KNEE_Z = 0.86, 0.48
SH_Z, SH_Y = 1.40, 0.2
UPPER, FORE = 0.29, 0.265
THIGH, SHIN = 0.40, 0.40


# ============================================================================ geometry helpers
def loft(name, sections, mat, segs=16):
    """sections: list of (x, z, rx, ry) ellipses stacked along z (rx = forward depth, ry = width)."""
    bm = bmesh.new()
    rings = []
    for (x, z, rx, ry) in sections:
        rings.append([bm.verts.new((x + rx * math.cos(2 * math.pi * k / segs), ry * math.sin(2 * math.pi * k / segs), z))
                      for k in range(segs)])
    for i in range(len(rings) - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = C.mesh_object(name, bm, mat)
    C.set_smooth(ob)
    return ob


def tube(name, pts, radii, mat, segs=10):
    ob = C.tube_along(name, pts, radii, mat, segs)
    return ob


def ellip(name, c, r, mat, scale=(1, 1, 1), seg=16, rings=10):
    ob = C.add_prim("sphere", name, mat, radius=r, location=c, segments=seg, ring_count=rings)
    ob.matrix_world = ob.matrix_world @ Matrix.Diagonal((*scale, 1))
    C.set_smooth(ob)
    return ob


def ik(root, target, l1, l2, pole):
    S, T, pv = Vector(root), Vector(target), Vector(pole)
    d = T - S
    dist = min(d.length, l1 + l2 - 1e-3)
    dirv = d.normalized()
    a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
    h = math.sqrt(max(l1 * l1 - a * a, 0.0))
    pp = (pv - dirv * pv.dot(dirv))
    pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, -1))
    E = S + dirv * a + pp * h
    return E, S + dirv * dist


# ============================================================================ the model
def mats():
    def L(key, **kw):
        kw.setdefault("ao", 0.5)
        kw.setdefault("ao_dist", 0.12)
        kw.setdefault("haze", 0.0)
        return N.lit(COL[key], name=key, **kw)
    return dict(skin=L("skin", spread=0.8, warm=1.3), hair=L("hair"), shirt=L("shirt", tex=0.08, tex_scale=14.0),
                vest=L("vest", tex=0.06, tex_scale=18.0), yoke=L("yoke"), pocket=L("pocket"),
                trousers=L("trousers", tex=0.08, tex_scale=12.0), boots=L("boots", spread=1.1), sole=L("sole"),
                hat=L("hat", tex=0.05, tex_scale=20.0), band=L("band"), belt=L("belt"), net=L("net"),
                mesh=L("mesh"))


def build(pose):
    p = POSES[pose]
    M = mats()
    parts = []          # (object, part id)

    def add(ob, pid):
        parts.append((ob, pid))
        return ob
    spread, nf, ff, bend = p["stance"]
    hip_drop = bend * 0.9
    feet = []
    # ---------------------------------------------------------------- legs + boots (local: +X forward, +Y left)
    for side, fwd in ((-1, nf), (1, ff)):
        hip = Vector((0.0, side * 0.095, HIP_Z - hip_drop))
        ankle = Vector((fwd, side * spread, 0.1))
        knee, ank = ik(hip, ankle, THIGH, SHIN, (1.0, side * 0.15, 0.0))
        add(tube("Thigh", [hip + Vector((0, 0, 0.06)), hip, (hip + knee) / 2, knee],
                 [0.085, 0.082, 0.07, 0.058], M["trousers"], 12), 2)
        add(tube("Shin", [knee, (knee + ank) / 2, ank + Vector((0, 0, 0.03))], [0.058, 0.056, 0.052],
                 M["trousers"], 12), 2)
        bx = ank.x
        feet.append(Vector((bx + 0.05, ank.y, 0.0)))
        add(tube("Boot", [(bx - 0.07, ank.y, 0.2), (bx - 0.05, ank.y, 0.07), (bx + 0.06, ank.y, 0.055),
                          (bx + 0.17, ank.y, 0.045)], [0.055, 0.058, 0.052, 0.035], M["boots"], 12), 1)
        add(C.tube_along("Sole", [(bx - 0.09, ank.y, 0.012), (bx + 0.18, ank.y, 0.012)], 0.045, M["sole"], 8), 1)
    # ---------------------------------------------------------------- torso (lean around the hips)
    up = []
    up.append(add(loft("Pelvis", [(0.0, 0.78, 0.105, 0.155), (0.0, 0.88, 0.115, 0.17), (0.0, 0.97, 0.105, 0.158)],
                       M["trousers"]), 2))
    up.append(add(loft("Belt", [(0.0, 0.925, 0.112, 0.163), (0.0, 0.965, 0.11, 0.16)], M["belt"]), 3))
    up.append(add(loft("Shirt", [(0.0, 0.92, 0.1, 0.152), (0.0, 1.02, 0.098, 0.148), (0.005, 1.18, 0.108, 0.172),
                                 (0.0, 1.32, 0.104, 0.196), (0.0, 1.4, 0.092, 0.2), (0.0, 1.45, 0.07, 0.13),
                                 (0.01, 1.49, 0.058, 0.07)], M["shirt"]), 3))
    # fishing vest: shorter and narrower than the shirt at the shoulders (sleeves + shoulders show)
    up.append(add(loft("Vest", [(0.0, 0.99, 0.108, 0.158), (0.0, 1.06, 0.106, 0.157), (0.005, 1.18, 0.114, 0.177),
                                (0.0, 1.3, 0.11, 0.188), (0.0, 1.4, 0.1, 0.172), (0.0, 1.455, 0.078, 0.12)],
                      M["vest"]), 3))
    # mesh yoke on the upper back + big back pocket (the back faces -X)
    up.append(add(loft("Yoke", [(-0.003, 1.29, 0.114, 0.19), (-0.003, 1.4, 0.104, 0.175), (0.0, 1.458, 0.081, 0.122)],
                       M["yoke"]), 3))
    pk = add(C.poly_object("Pocket", [(-0.11, 0.0), (0.11, 0.0), (0.105, 0.165), (-0.105, 0.165)], M["pocket"], 0.02), 3)
    pk.matrix_world = Matrix.Translation((-0.118, 0.0, 1.035)) @ Matrix.Rotation(math.radians(90), 4, "Z")
    up.append(pk)
    # collar + neck + head
    up.append(add(tube("Collar", [(0.0, 0, 1.44), (0.005, 0, 1.49)], [0.075, 0.068], M["shirt"], 12), 3))
    up.append(add(tube("Neck", [(0.01, 0, 1.45), (0.015, 0, 1.56)], [0.052, 0.05], M["skin"], 10), 6))
    hc = Vector((0.02, 0.0, 1.615))
    up.append(add(ellip("Head", hc, 1.0, M["skin"], (0.1, 0.078, 0.112)), 6))
    up.append(add(ellip("Hair", hc + Vector((-0.012, 0, -0.004)), 1.0, M["hair"], (0.098, 0.082, 0.105)), 6))
    for s in (-1, 1):
        up.append(add(ellip("Ear", hc + Vector((0.005, s * 0.078, -0.01)), 1.0, M["skin"], (0.02, 0.012, 0.03), 8, 6), 6))
    # flat-topped bucket hat with a softly drooping brim
    up.append(add(loft("Crown", [(0.02, 1.665, 0.103, 0.091), (0.02, 1.72, 0.097, 0.085), (0.02, 1.757, 0.091, 0.079),
                                 (0.02, 1.764, 0.07, 0.06), (0.02, 1.766, 0.02, 0.02)], M["hat"]), 7))
    up.append(add(loft("Band", [(0.02, 1.668, 0.105, 0.093), (0.02, 1.688, 0.103, 0.091)], M["band"]), 7))
    up.append(add(loft("Brim", [(0.02, 1.628, 0.145, 0.134), (0.02, 1.656, 0.116, 0.104), (0.02, 1.668, 0.103, 0.091)],
                       M["hat"]), 7))

    lean = math.radians(p["lean"])
    pivot = Vector((0, 0, HIP_Z - hip_drop))
    R = Matrix.Translation(pivot) @ Matrix.Rotation(lean, 4, "Y") @ Matrix.Translation(-pivot)
    for ob in up:
        o = ob[0] if isinstance(ob, tuple) else ob
        o.matrix_world = R @ Matrix.Translation((0, 0, -hip_drop)) @ o.matrix_world
    # ---------------------------------------------------------------- arms (shoulders follow the lean)
    hands = {}
    for side, key in ((1, "far"), (-1, "near")):
        sh = R @ Vector((0.0, side * SH_Y, SH_Z - hip_drop))
        tgt = Vector(p[key])
        if p.get("pole_up"):
            pole = (0.2, side * 1.0, 0.35)
        else:
            pole = (-0.55, side * 0.75, -0.45)
        elbow, hand = ik(sh, tgt, UPPER, FORE, pole)
        pid = 4 if side > 0 else 5
        add(tube("Deltoid", [sh + (sh - elbow).normalized() * 0.02, sh, sh.lerp(elbow, 0.5)], [0.05, 0.066, 0.056],
                 M["shirt"], 12), pid)
        add(tube("Upper", [sh, sh.lerp(elbow, 0.5), elbow], [0.062, 0.055, 0.048], M["shirt"], 12), pid)
        wrist = elbow.lerp(hand, 0.93)
        add(tube("Fore", [elbow, elbow.lerp(hand, 0.5), wrist], [0.048, 0.044, 0.04], M["shirt"], 12), pid)
        fdir = (hand - elbow).normalized()
        hc2 = hand + fdir * 0.03
        add(ellip("Hand", hc2, 1.0, M["skin"], (0.05, 0.034, 0.042), 10, 8), pid)
        hands[key] = hc2
    turn = Matrix.Rotation(math.radians(TURN), 4, "Z")
    for ob, _ in parts:
        ob.matrix_world = turn @ ob.matrix_world
    for i, (ob, pid) in enumerate(parts):
        ob.pass_index = pid
    return parts, turn @ hands["near"], [turn @ f for f in feet]


# ============================================================================ passes
def id_material():
    m = bpy.data.materials.new("IDPass")
    nb = C.NB(m)
    oi = nb.node("ShaderNodeObjectInfo")
    cam = nb.node("ShaderNodeCameraData")
    r = nb.math("DIVIDE", oi.outputs["Object Index"], 16.0)
    g = nb.math("DIVIDE", nb.math("SUBTRACT", cam.outputs["View Distance"], 6.0), 12.0)
    nb.output_emission(nb.combine(r, g, 0.0), 1.0)
    return m


def inner_lines(arr, ids, depth, thr=0.035, dl=0.1):
    """Darken the pixel BEHIND at every part boundary (depth-aware inner outline)."""
    out = arr.copy()
    op = arr[..., 3] > 0.5
    mask = np.zeros(op.shape, bool)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        oid = C.shift(ids, dy, dx)
        od = C.shift(depth, dy, dx)
        oop = C.shift(op, dy, dx)
        m = op & oop & (oid != ids) & (depth > od + thr)
        mask |= m
    dk = N.darker(arr[..., :3], dl=dl, cool=0.6)
    out[mask, :3] = dk[mask]
    return out


SHADOW_COL, SHADOW_A = "#141b26", 0.42


def shadow_catcher(feet):
    """Ground disc that outputs the sun light value, darkened in a contact zone around each boot."""
    m = bpy.data.materials.new("ShadowCatch")
    nb = C.NB(m)
    L = N._light_value(nb, ao=0.0)
    geo = nb.node("ShaderNodeNewGeometry")
    d = None
    for f in feet:
        dd = nb.vmath("DISTANCE", geo.outputs["Position"], tuple(f))
        d = dd if d is None else nb.math("MINIMUM", d, dd)
    contact = nb.math("LESS_THAN", d, 0.14)
    L = nb.math("MULTIPLY", L, nb.math("SUBTRACT", 1.0, contact))
    nb.output_emission(nb.combine(L, L, L), 1.0)
    ob = C.add_prim("cyl", "Catcher", m, radius=0.6, depth=0.004, location=(0.12, 0.2, -0.003), vertices=32)
    return ob


def add_ground_shadow(sc, parts, feet, arr, x0, y0):
    """Semi-transparent cast + contact shadow on the ground under the angler (only where the sprite is empty)."""
    cat = shadow_catcher(feet)
    for ob, _ in parts:
        ob.visible_camera = False
    bpy.context.view_layer.update()
    raw = N.render_ss("char_sh_ss.png", SS, P.W, P.H)
    raw = raw[y0 * SS:(y0 + CROP_H) * SS, x0 * SS:(x0 + CROP_W) * SS]
    sh, _ = N.mode_down(raw, SS, cover=0.5)
    for ob, _ in parts:
        ob.visible_camera = True
    bpy.data.objects.remove(cat, do_unlink=True)
    m = (sh[..., 3] > 0.5) & (sh[..., 0] < 0.8) & (arr[..., 3] < 0.5)
    out = arr.copy()
    out[m, :3] = N.hex2srgb(SHADOW_COL)
    out[m, 3] = SHADOW_A
    return out


def main():
    sc = N.new_scene()
    N.add_sun()
    N.set_world()
    P.setup_camera(0.0)
    fx, fy, _ = P.project((0, 0, 0), 0.0)
    cx = int(round(P.W / 2 + fx))
    cy = int(round(P.H / 2 + fy))
    x0, y0 = cx - CROP_W // 2, cy - FEET_PX          # bottom-up
    data = {"cropW": CROP_W, "cropH": CROP_H, "feetPx": FEET_PX, "style": "natural", "poses": []}
    os.makedirs(OUTD, exist_ok=True)
    idm = id_material()
    sprites = []
    for pose, pd in POSES.items():
        for ob in list(sc.objects):
            if ob.type == "MESH":
                bpy.data.objects.remove(ob, do_unlink=True)
        for me in list(bpy.data.meshes):
            if me.users == 0:
                bpy.data.meshes.remove(me)
        parts, hand, feet = build(pose)
        bpy.context.view_layer.update()
        sc.render.film_transparent = True
        raw = N.render_ss("char_ss.png", SS, P.W, P.H)
        raw = raw[y0 * SS:(y0 + CROP_H) * SS, x0 * SS:(x0 + CROP_W) * SS]
        arr, pick = N.mode_down(raw, SS, cover=0.45)
        # id/depth pass (raw view transform, 16 bit)
        vl = bpy.context.view_layer
        vl.material_override = idm
        sc.view_settings.view_transform = "Raw"
        sc.render.image_settings.color_depth = "16"
        idr = N.render_ss("char_id_ss.png", SS, P.W, P.H)
        vl.material_override = None
        sc.view_settings.view_transform = "Standard"
        sc.render.image_settings.color_depth = "8"
        idr = idr[y0 * SS:(y0 + CROP_H) * SS, x0 * SS:(x0 + CROP_W) * SS]
        idd = N.take(idr, pick, SS)
        ids = np.round(idd[..., 0] * 16).astype(int)
        depth = idd[..., 1] * 12.0 + 6.0
        arr = inner_lines(arr, ids, depth)
        arr = N.outline_sel(arr, mulL=0.58, cool=0.9)
        arr = add_ground_shadow(sc, parts, feet, arr, x0, y0)
        path = os.path.join(OUTD, f"angler_{pose}.png")
        C.save_pixels(arr, path)
        sprites.append(arr)
        hx, hy, _ = P.project(hand, 0.0)
        rod = Vector(pd["rod"]).normalized()
        data["poses"].append({
            "name": pose,
            "handX": round(P.W / 2 + hx - x0, 2), "handY": round(P.H / 2 + hy - y0, 2),
            "hx": round(hand.x, 3), "hy": round(hand.z, 3), "hz": round(hand.y, 3),
            "rx": round(rod.x, 3), "ry": round(rod.y, 3), "rz": round(rod.z, 3),
        })
        op = arr[..., 3] > 0.5
        rows = np.where(op.any(1))[0]
        print("NAT pose", pose, "height px", rows.max() - FEET_PX + 1 if len(rows) else 0)
    with open(os.path.join(N.OUT, "character.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    N.sheet(sprites, os.path.join(N.OUT, "character_sheet.png"), scale=4, bg="#7a7a7a", pad=8)
    print("NAT character done")


if __name__ == "__main__":
    main()
