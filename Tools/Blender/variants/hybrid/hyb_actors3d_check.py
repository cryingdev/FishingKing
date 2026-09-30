"""
hybrid - check of the 3D actors exported by hyb_actors3d.py: re-imports every FBX into a fresh scene, verifies the
Unity contract numerically (raw FBX node transforms as Unity reads them + the Blender re-import) and renders check
images from the stage camera direction (behind, above) with the palette-JSON toon ramps (3 bands, key light CL of
the sprite, 1 px outline in the JSON outline colour for the 1x game-scale renders).

Outputs (Tools/Blender/_tmp/actors3d/):
  check_angler.png  row 1: sprite angler_idle | 3D rest pose at game scale (1x, shown 4x) | 3D posed test (1x)
                    row 2: rest (stage direction, 4x res) | posed (stage direction) | rest side view | posed side view
  check_reels.png   row 1: every reel on a rod stub, stage camera direction (rod leaning up-left like the idle pose)
                    row 2: side view from the crank side (rod tip to the right), crank angle 0 (knob up)
                    row 3: same, Crank rotated +90 deg about its local X (knob must move towards the rod tip)
                    markers: magenta = reel origin (foot top), red = Crank origin (axis), yellow = Knob, cyan = Spool
  check_report.json numbers printed below
Run: blender -b --python variants/hybrid/hyb_actors3d_check.py
"""
import os
import re
import sys
import math
import json
import shutil
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hyb_core as R  # noqa: E402
import hyb_character as HC  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix, Euler  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402
import fk_items as I  # noqa: E402
from io_scene_fbx import parse_fbx  # noqa: E402

V = Vector
OUT = os.path.join(R.BL, "_tmp", "actors3d")
WORK = os.path.join(OUT, "_work")
BANDS = [0.45, 0.74]
CL = V(HC.CL).normalized()          # towards the key light (stage frame: x right, y forward, z up)
BG = (0.5, 0.5, 0.5)
CROP_W, CROP_H, FEET_PX = 96, 112, 10
K = 4                               # hi-res factor
REPORT = {}


def u_of_f(p, unit=1.0):
    return V((-p[0] * unit, p[1] * unit, p[2] * unit))


def r3(v):
    return [round(float(x), 4) for x in v]


# ---------------------------------------------------------------- raw FBX (what Unity reads)
def fbx_scene(path):
    root, _ = parse_fbx.parse(path)
    top = {e.id: e for e in root.elems}

    def kids(e, name):
        return [c for c in e.elems if c.id == name]

    def props70(e):
        out = {}
        for pp in kids(e, b"Properties70"):
            for p in kids(pp, b"P"):
                out[p.props[0].decode()] = p.props[4:]
        return out
    gs = props70(top[b"GlobalSettings"])
    models, geoms = {}, {}
    for e in top[b"Objects"].elems:
        if e.id == b"Model":
            pr = props70(e)
            models[e.props[0]] = dict(
                name=e.props[1].split(b"\x00\x01")[0].decode(), type=e.props[2].decode(),
                T=V(pr.get("Lcl Translation", (0, 0, 0))), R=V(pr.get("Lcl Rotation", (0, 0, 0))),
                S=V(pr.get("Lcl Scaling", (1, 1, 1))), pre=V(pr.get("PreRotation", (0, 0, 0))),
                post=V(pr.get("PostRotation", (0, 0, 0))), parent=None, geo=None)
        elif e.id == b"Geometry":
            vs = kids(e, b"Vertices")
            if vs:
                geoms[e.props[0]] = np.array(vs[0].props[0], np.float64).reshape(-1, 3)
    for c in top[b"Connections"].elems:
        if c.props[0] == b"OO":
            ch, par = c.props[1], c.props[2]
            if ch in models:
                models[ch]["parent"] = par if par in models else None
            if ch in geoms and par in models:
                models[par]["geo"] = ch

    def rot(e):
        return Euler([math.radians(a) for a in e], "XYZ").to_matrix().to_4x4()

    def local(m):
        return (Matrix.Translation(m["T"]) @ rot(m["pre"]) @ rot(m["R"]) @ rot(m["post"]).inverted()
                @ Matrix.Diagonal((m["S"][0], m["S"][1], m["S"][2], 1.0)))
    glob = {}

    def g(uid):
        if uid not in glob:
            m = models[uid]
            glob[uid] = (g(m["parent"]) if m["parent"] else Matrix()) @ local(m)
        return glob[uid]
    byname = {}
    for uid, m in models.items():
        m["G"] = g(uid)
        byname[m["name"]] = m
    unit = float(gs["UnitScaleFactor"][0]) / 100.0
    axes = {k: int(gs[k][0]) for k in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis",
                                       "CoordAxisSign")}
    return dict(gs=gs, models=models, geoms=geoms, byname=byname, unit=unit, axes=axes)


def fbx_report(path, bone_names=()):
    fs = fbx_scene(path)
    unit = fs["unit"]
    roots = [m for m in fs["models"].values() if m["parent"] is None]
    rep = {"axes": fs["axes"], "UnitScaleFactor": float(fs["gs"]["UnitScaleFactor"][0]),
           "roots": {m["name"]: {"T": r3(m["T"]), "R": r3(m["R"]), "S": r3(m["S"]), "type": m["type"]} for m in roots}}
    # every mesh / null node's local transform (mesh parts must be at identity relative to their bone)
    rep["meshLocalMax"] = {"T": 0.0, "R": 0.0}
    ymin, pts = 1e9, []
    for m in fs["models"].values():
        if m["type"] == "Mesh":
            rep["meshLocalMax"]["T"] = max(rep["meshLocalMax"]["T"], m["T"].length)
            rep["meshLocalMax"]["R"] = max(rep["meshLocalMax"]["R"], max(abs(a) for a in m["R"]))
            if m["geo"] is not None:
                for v in fs["geoms"][m["geo"]]:
                    w = m["G"] @ V(v)
                    pts.append(u_of_f(w, unit))
                    ymin = min(ymin, w.y * unit)
    rep["meshMinY_unity"] = round(ymin, 5)
    arr = np.array([tuple(p) for p in pts])
    rep["meshBBoxUnity"] = [r3(arr.min(0)), r3(arr.max(0))]
    rep["nodes"] = {}
    for nm in bone_names:
        m = fs["byname"].get(nm)
        if m:
            rep["nodes"][nm] = {"type": m["type"], "unityPos": r3(u_of_f(m["G"].translation, unit)),
                                "parent": fs["models"][m["parent"]]["name"] if m["parent"] else None}
    rep["_fs"] = fs
    return rep


# ---------------------------------------------------------------- scene helpers
def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False, ignore_leaf_bones=False,
                             automatic_bone_orientation=False)
    return [o for o in bpy.data.objects if o not in before]


def mount(objs, Xr=(1, 0, 0), Yr=(0, 0, 1), Zr=(0, 1, 0), pos=(0, 0, 0), name="Mount"):
    """Parent the imported roots to an empty so that the asset's UNITY axes (x right, y up, z forward / rod tip)
    land on Xr, Yr, Zr of the stage frame (x right, y forward, z up). A re-imported asset sits in Blender's native
    frame W with Unity u = (-Wx, Wz, -Wy), so W.x -> -Xr, W.y -> -Zr, W.z -> Yr."""
    Xr, Yr, Zr = V(Xr), V(Yr), V(Zr)
    M3 = Matrix((-Xr, -Zr, Yr)).transposed()
    e = bpy.data.objects.new(name, None)
    C.link(e)
    e.matrix_world = Matrix.Translation(V(pos)) @ M3.to_4x4()
    bpy.context.view_layer.update()
    for o in objs:
        if o.parent is None:
            mw = o.matrix_world.copy()
            o.parent = e
            o.matrix_parent_inverse = Matrix()
            o.matrix_basis = mw
    bpy.context.view_layer.update()
    return e


def base_name(n):
    return re.sub(r"\.\d{3}$", "", n)


def apply_toon(objs, pal):
    used = {}
    for o in objs:
        if o.type != "MESH":
            continue
        for s in o.material_slots:
            nm = base_name(s.material.name)
            e = pal[nm]
            s.material = R.m_tone(e["ramp"], BANDS, light=CL, name="T_" + nm)
            used[nm] = e
    return used


def flat(hexc):
    return R.m_flat(hexc)


def render_exr(tag):
    os.makedirs(WORK, exist_ok=True)
    sc = bpy.context.scene
    st = sc.render.image_settings
    st.file_format = "OPEN_EXR"
    st.color_mode = "RGBA"
    st.color_depth = "32"
    path = os.path.join(WORK, tag + ".exr")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return R._load_exr(path)


def aa(on):
    sc = bpy.context.scene
    sc.render.filter_size = 1.2 if on else 0.0
    try:
        sc.eevee.taa_render_samples = 16 if on else 1
    except Exception:
        pass


def render_rgba(tag, bg=BG):
    col = render_exr(tag)
    rgb = R.l2s(col[..., :3])
    a = col[..., 3:4]
    out = np.ones(col.shape, np.float32)
    out[..., :3] = rgb * a + np.array(bg) * (1 - a)
    return out


def render_ids(tag, meshes):
    """Per-material id pass (EXR float), id = 1 + index into the returned name list; 0 = empty."""
    names = sorted({base_name(s.material.name)[2:] for o in meshes for s in o.material_slots})
    saved = []
    idm = {}
    for i, nm in enumerate(names):
        m = bpy.data.materials.new("ID_" + nm)
        nb = R.NB(m)
        nb.output_emission(nb.rgb((float(i + 1), 0.0, 0.0, 1.0)), 1.0)
        idm[nm] = m
    for o in meshes:
        for s in o.material_slots:
            saved.append((s, s.material))
            s.material = idm[base_name(s.material.name)[2:]]
    ids = render_exr(tag + "_id")
    for s, m in saved:
        s.material = m
    idv = np.round(ids[..., 0]).astype(int)
    idv[ids[..., 3] < 0.5] = 0
    return idv, names


def outline_px(rgba, ids, names, pal):
    """1 px outline around the silhouette in the JSON outline colour of the neighbouring material (the darkest
    one where several touch), on transparent background."""
    H_, W_ = ids.shape
    a = ids > 0
    out = np.zeros((H_, W_, 4), np.float32)
    out[a, :3] = rgba[a, :3]
    out[a, 3] = 1
    ocol = np.zeros((len(names) + 1, 3))
    olum = np.full(len(names) + 1, 9.0)
    for i, nm in enumerate(names):
        h = pal[nm]["outline"]
        ocol[i + 1] = R.hexrgb(h)
        olum[i + 1] = R.lum(h)
    best = np.full((H_, W_), 9.0)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nb = np.zeros_like(ids)
        ys0, ys1 = max(0, dy), H_ + min(0, dy)
        xs0, xs1 = max(0, dx), W_ + min(0, dx)
        nb[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx] = ids[ys0:ys1, xs0:xs1]
        m = (~a) & (nb > 0) & (olum[nb] < best)
        out[m, :3] = ocol[nb[m]]
        out[m, 3] = 1
        best[m] = olum[nb[m]]
    return out


def on_bg(rgba, bg=BG):
    o = rgba.copy()
    o[..., :3] = rgba[..., :3] * rgba[..., 3:4] + np.array(bg) * (1 - rgba[..., 3:4])
    o[..., 3] = 1
    return o


def ortho_cam(loc, rot, scale, w, h, fit="VERTICAL"):
    """Orthographic camera; `scale` = metres across the `fit` dimension."""
    sc = bpy.context.scene
    cam = sc.camera
    cd = cam.data
    cd.type = "ORTHO"
    cd.sensor_fit = fit
    cd.ortho_scale = scale
    cd.shift_x = cd.shift_y = 0.0
    cam.location = loc
    cam.rotation_euler = rot
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.use_border = False


def stage_cam(k=1, crop=None):
    sc = bpy.context.scene
    cam = P.setup_camera(0.0, P.W * k, P.H * k)
    cam.data.type = "PERSP"
    if crop:
        x0, y0, w, h = crop            # bottom-up stage px
        sc.render.use_border = True
        sc.render.use_crop_to_border = True
        sc.render.border_min_x, sc.render.border_max_x = x0 / P.W, (x0 + w) / P.W
        sc.render.border_min_y, sc.render.border_max_y = y0 / P.H, (y0 + h) / P.H
    else:
        sc.render.use_border = False
    return cam


# ---------------------------------------------------------------- posing (armature space, axis-agnostic)
def pbh(arm, n):
    return arm.pose.bones[n].head.copy()


def rotate_about(arm, name, q, pivot=None):
    pb = arm.pose.bones[name]
    h = pb.head.copy() if pivot is None else pivot
    pb.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()


def aim(arm, name, cur, target_dir):
    rotate_about(arm, name, V(cur).rotation_difference(V(target_dir)))


def grip_arm(arm, grip):
    return arm.matrix_world.inverted() @ grip.matrix_world.translation


def ik_arm(arm, grips, s, target, pole):
    """Analytic two-bone IK on UpperArm/ForeArm so that HandGrip.<s> reaches `target` (armature space, i.e. the
    FBX frame F). The hand stays straight: elbow -> grip is one rigid segment (0.265 + 0.045 m)."""
    S = pbh(arm, "UpperArm." + s)
    E0 = pbh(arm, "ForeArm." + s)
    g = grips[s]
    l1 = (E0 - S).length
    l2 = (grip_arm(arm, g) - E0).length
    elbow, end = HC.two_bone(S, target, l1, l2, pole)
    aim(arm, "UpperArm." + s, pbh(arm, "ForeArm." + s) - S, elbow - S)
    e = pbh(arm, "ForeArm." + s)
    aim(arm, "ForeArm." + s, grip_arm(arm, g) - e, end - e)
    return (grip_arm(arm, g) - V(target)).length, l1, l2


def f_of_u(u):
    return V((-u[0], u[1], u[2]))


# ---------------------------------------------------------------- angler
def check_angler():
    C.reset_scene()
    R.reset_materials()
    path = os.path.join(OUT, "angler.fbx")
    bones = [b for b, _ in __import__("hyb_actors3d").BONES]
    rep = fbx_report(path, bones + ["HandGrip.L", "HandGrip.R", "Angler"])
    fs = rep.pop("_fs")
    # hierarchy as Unity sees it: mesh parts / grips under their bone
    par = {}
    for m in fs["models"].values():
        if m["type"] in ("Mesh", "Null") and m["parent"]:
            par[m["name"]] = fs["models"][m["parent"]]["name"]
    rep["parentOf"] = par
    rep["meshNodes"] = sum(1 for m in fs["models"].values() if m["type"] == "Mesh")
    # rest-pose bone axes as Unity will see them (importer: R -> S R S, S = diag(-1, 1, 1)):
    # local +Y / +Z world directions = S (G e_y) / S (G e_z)
    ax = {}
    for nm in bones:
        G = fs["byname"][nm]["G"].to_3x3().normalized()
        y, z = G.col[1], G.col[2]
        ax[nm] = {"Y": r3(V((-y[0], y[1], y[2]))), "Z": r3(V((-z[0], z[1], z[2])))}
    rep["boneAxesUnity"] = ax
    objs = import_fbx(path)
    arm = next(o for o in objs if o.type == "ARMATURE")
    meshes = [o for o in objs if o.type == "MESH"]
    grips = {s: next(o for o in objs if base_name(o.name) == "HandGrip." + s) for s in ("L", "R")}
    rep["reimport"] = {
        "armature": arm.name, "armatureMatrix": [r3(row) for row in arm.matrix_world],
        "bones": [b.name for b in arm.data.bones],
        "meshParents": {o.name: (o.parent_type, o.parent_bone) for o in meshes},
        "grips": {s: (g.parent_type, g.parent_bone) for s, g in grips.items()},
        "tris": sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons),
        "materials": sorted({base_name(s.material.name) for o in meshes for s in o.material_slots}),
    }
    with open(os.path.join(OUT, "angler_palette.json"), encoding="utf-8") as f:
        pal = json.load(f)
    apply_toon(meshes, pal)
    mount(objs, name="AnglerMount")
    bpy.context.view_layer.update()
    # rigid binding check: every part keeps its rest offset to its bone
    rest_rel = {o.name: (arm.matrix_world @ arm.pose.bones[o.parent_bone].matrix).inverted() @ o.matrix_world
                for o in meshes}
    # ------------------------------------------------ renders: rest
    fx, fy, _ = P.project((0, 0, 0), 0.0)
    cx, cy = int(round(P.W / 2 + fx)), int(round(P.H / 2 + fy))
    crop = (cx - CROP_W // 2, cy - FEET_PX, CROP_W, CROP_H)
    shots = {}

    def shoot(tag):
        aa(False)
        stage_cam(1, crop)
        col = render_exr(tag + "_px")
        rgba = np.zeros(col.shape, np.float32)
        rgba[..., :3] = R.l2s(col[..., :3])
        rgba[..., 3] = col[..., 3]
        ids, names = render_ids(tag + "_px", meshes)
        px = outline_px(rgba, ids, names, pal)
        aa(True)
        stage_cam(K, crop)
        hi = render_rgba(tag + "_hi")
        ortho_cam((4.0, 0.0, 0.95), (math.radians(90), 0, math.radians(90)), 2.2, CROP_W * K, CROP_H * K)
        side = render_rgba(tag + "_side")
        shots[tag] = dict(px=px, hi=hi, side=side)
        top = int(np.argmax((px[..., 3] > 0.5).any(1)))
        return CROP_H - FEET_PX - top
    rep["heightPx_rest"] = shoot("rest")
    # ------------------------------------------------ posed test (left arm raised forward, right arm bent, knee)
    aim(arm, "Thigh.L", pbh(arm, "Shin.L") - pbh(arm, "Thigh.L"), f_of_u((0.0, -1.0, 0.45)))
    aim(arm, "Shin.L", pbh(arm, "Foot.L") - pbh(arm, "Shin.L"), f_of_u((0.0, -1.0, -0.2)))
    rotate_about(arm, "Spine", Matrix.Rotation(math.radians(-8), 3, "X").to_quaternion())
    rotate_about(arm, "Head", Matrix.Rotation(math.radians(25), 3, "Y").to_quaternion())
    shL = u_of_f(pbh(arm, "UpperArm.L"))
    tL = f_of_u(shL + V((-0.03, 0.1, 0.55)))
    errL, l1, l2 = ik_arm(arm, grips, "L", tL, f_of_u((-0.5, -1.0, 0.0)))
    tR = f_of_u((0.03, 1.02, 0.3))
    errR, _, _ = ik_arm(arm, grips, "R", tR, f_of_u((1.0, -0.6, -0.3)))
    rel_err = 0.0
    for o in meshes:
        rel = (arm.matrix_world @ arm.pose.bones[o.parent_bone].matrix).inverted() @ o.matrix_world
        rel_err = max(rel_err, max(abs(a - b) for ra, rb in zip(rel, rest_rel[o.name]) for a, b in zip(ra, rb)))
    rep["posed"] = {"gripErrL_m": round(errL, 6), "gripErrR_m": round(errR, 6), "armL_l1_l2": [round(l1, 4), round(l2, 4)],
                    "rigidPartsMaxDeviation": round(rel_err, 8),
                    "gripL_unity": r3(u_of_f(grip_arm(arm, grips["L"]))), "targetL_unity": r3(u_of_f(tL))}
    rep["heightPx_posed"] = shoot("posed")
    # ------------------------------------------------ sheet
    sprite = R.load_png(os.path.join(R.OUT, "character", "angler_idle.png"))
    row1 = [R.upscale(on_bg(sprite), 4), R.upscale(on_bg(shots["rest"]["px"]), 4), R.upscale(on_bg(shots["posed"]["px"]), 4)]
    row2 = [shots["rest"]["hi"], shots["posed"]["hi"], shots["rest"]["side"], shots["posed"]["side"]]
    sheet = R.flow_sheet([row1, row2], (0.32, 0.34, 0.38), scale=1, pad=12)
    R.save_png(sheet, os.path.join(OUT, "check_angler.png"))
    for tag in ("rest", "posed"):
        R.save_png(shots[tag]["px"], os.path.join(WORK, f"angler_{tag}_1x.png"))
    top = int(np.argmax(sprite[..., 3].any(1)))
    rep["heightPx_sprite_idle"] = CROP_H - FEET_PX - top
    return rep


# ---------------------------------------------------------------- reels
def marker(parent, col, r=0.0042):
    ob = R.ellipsoid("Mk", (0, 0, 0), (r, r, r), flat(col), 8, 6)
    ob.data.transform(ob.matrix_world)          # the radius lives in the object matrix: bake it
    ob.parent = parent
    ob.matrix_parent_inverse = Matrix()
    ob.matrix_basis = Matrix()
    return ob


def reel_instance(rid, pal, pos, Xr, Yr, Zr, crank_deg=0.0):
    objs = import_fbx(os.path.join(OUT, f"{rid}.fbx"))
    by = {base_name(o.name): o for o in objs}
    root = next(o for o in objs if o.parent is None)
    apply_toon(objs, pal)
    rod = C.tube_along("Rod", [(0, 0.0072, -0.08), (0, 0.0072, 0.15)], 0.0062, flat("#7a5a3a"), 10)
    rod.parent = root
    rod.matrix_parent_inverse = Matrix()
    rod.matrix_basis = Matrix()
    for nm, col in (("Reel", "#ff40ff"), ("Crank", "#ff3030"), ("Knob", "#ffe040"), ("Spool", "#30e0ff")):
        marker(by[nm] if nm != "Reel" else root, col)
    mount([root], Xr, Yr, Zr, pos, name="Mount_" + rid)
    by["Crank"].rotation_mode = "XYZ"
    k0 = by["Knob"].matrix_world.translation.copy()
    by["Crank"].rotation_euler.x += math.radians(crank_deg)
    bpy.context.view_layer.update()
    return by, root, k0


def check_reels():
    C.reset_scene()
    R.reset_materials()
    P.setup_camera(0.0)
    with open(os.path.join(OUT, "reel_palette.json"), encoding="utf-8") as f:
        pal = json.load(f)
    rids = list(I.REELS)
    rep = {}
    for rid in rids:
        r = fbx_report(os.path.join(OUT, f"{rid}.fbx"), ["Reel", "Body", "Crank", "Knob", "Spool"])
        fs = r.pop("_fs")
        cr = fs["byname"]["Crank"]
        r["crankLocal"] = {"T_unity": r3(u_of_f(cr["T"], fs["unit"])), "R": r3(cr["R"])}
        r["parentOf"] = {m["name"]: fs["models"][m["parent"]]["name"] for m in fs["models"].values() if m["parent"]}
        rep[rid] = r
    # ------------------------------------------------ row A: stage camera direction, rod like the idle pose
    rod_u = V((-0.22, 0.72, 0.66)).normalized()
    Zr = V((rod_u.x, rod_u.z, rod_u.y))
    Yr = (V((0, 0, 1)) - Zr * Zr.z).normalized()
    Xr = Zr.cross(Yr)
    gap = 0.25
    xs = [(i - (len(rids) - 1) / 2) * gap for i in range(len(rids))]
    for rid, x in zip(rids, xs):
        reel_instance(rid, pal, (x, 0, 0), Xr, Yr, Zr)
    aa(True)
    W_, H_ = 1500, 320
    pitch = math.radians(90 - P.PITCH)
    fwd = V((0, math.sin(pitch), -math.cos(pitch)))
    ortho_cam(V((0, 0, 0.0)) - fwd * 4.0, (pitch, 0, 0), gap * len(rids), W_, H_, "HORIZONTAL")
    rowA = render_rgba("reels_A")
    # ------------------------------------------------ rows B / C: side view from the crank side, crank 0 / +90
    rows = []
    for deg in (0.0, 90.0):
        C.clear_objects()
        R.reset_materials()
        for rid, x in zip(rids, xs):
            by, root, k0 = reel_instance(rid, pal, (0, x * 1.0, 0), (1, 0, 0), (0, 0, 1), (0, 1, 0), crank_deg=deg)
            if deg:
                # Unity-space motion of the knob for +deg about the Crank's local X
                k1 = by["Knob"].matrix_world.translation
                c = by["Crank"].matrix_world.translation
                rep[rid]["crank+90"] = {"knobMove_unity": r3(V((k1.x - k0.x, k1.z - k0.z, k1.y - k0.y))),
                                        "radiusBefore": round((k0 - c).length, 5), "radiusAfter": round((k1 - c).length, 5),
                                        "axisOffsetX": round(k1.x - k0.x, 6)}
        ortho_cam((3.0, 0.0, -0.03), (math.radians(90), 0, math.radians(90)), gap * len(rids), W_, H_, "HORIZONTAL")
        rows.append(render_rgba(f"reels_side_{int(deg)}"))
    sheet = np.concatenate([rowA, rows[0], rows[1]], 0)
    R.save_png(sheet, os.path.join(OUT, "check_reels.png"))
    return rep


def main():
    R.use_preset("lake")
    os.makedirs(WORK, exist_ok=True)
    REPORT["angler"] = check_angler()
    REPORT["reels"] = check_reels()
    with open(os.path.join(OUT, "check_report.json"), "w", encoding="utf-8") as f:
        json.dump(REPORT, f, indent=1, default=str)
    a = REPORT["angler"]
    print("CHK axes", a["axes"], "unit", a["UnitScaleFactor"], "roots", a["roots"])
    print("CHK mesh local max", a["meshLocalMax"], "minY", a["meshMinY_unity"], "bbox", a["meshBBoxUnity"])
    print("CHK reimport", {k: v for k, v in a["reimport"].items() if k not in ("meshParents",)})
    print("CHK parents", a["parentOf"])
    for nm, v in a["boneAxesUnity"].items():
        print("CHK axes", nm, v)
    print("CHK posed", a["posed"], "height px rest/posed/sprite", a["heightPx_rest"], a["heightPx_posed"],
          a["heightPx_sprite_idle"])
    for rid, r in REPORT["reels"].items():
        print("CHK reel", rid, r["roots"], "minY", r["meshMinY_unity"], "bbox", r["meshBBoxUnity"], "nodes", r["nodes"],
              "crank", r["crankLocal"], r.get("crank+90"))
    shutil.rmtree(WORK, ignore_errors=True)
    print("CHK done")


if __name__ == "__main__":
    main()
