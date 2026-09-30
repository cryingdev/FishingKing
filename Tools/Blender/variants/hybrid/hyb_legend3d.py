"""
hybrid - real-time 3D LEGEND fish for the legend encounter (spec v1, section 3.3; per-legend rows in
Docs/legends_rollout.md): a rigid, bone-parented model exported like the angler (hyb_actors3d.py) - FBX + palette JSON
for the toon shader (Assets/Shaders/ActorToon.shader), loaded in Unity with ActorArt.Model("legend_<id>") +
ActorArt.Palette("legend_<id>_palette") and rendered by the encounter's fish camera (partial-3D pipeline: perspective
camera -> transparent RT -> quad, ActorRim rim).

GENERIC builder: every legend is ONE module legends/<fish_id>.py (see legends/README.md for the module API, bone
naming, palette slots and the triangle budget). This file only loads, validates, rigs, exports and checks; the
geometry helpers live in hyb_legend_kit.py. Adding a legend never edits this file.

Contract (identical to hyb_actors3d.py unless stated):
  * FBX_OPTS of hyb_actors3d: metres, +Y up, the fish faces +Z in Unity, its left is Unity -X; the FBX root node
    (the armature object, named after the species) has an IDENTITY transform (it carries Rx(+90) in Blender).
  * Every bone's rest rotation is IDENTITY (all bones point along armature +Y, roll 0): a bone's local axes are the
    model axes (x = the fish's right, y = up, z = towards the nose), so Unity poses are plain localRotation Eulers
    (protrusion bones also move by localPosition, see the module's RIG).
  * Rigid parts: one mesh geo_<Bone> per bone (Root has none), local T/R identity, vertices in the bone-head frame.
    geo_Eye.L / geo_Eye.R (the <prefix>_eye_glow lenses) ride the bone of their Eye.* empty.
  * Size: modelled 1.0 m long (the foremost tip - nose, bill or lip - at z +0.50, the tail tip at z -0.50); scale at
    runtime = cm / 100.
  * Materials are prefixed (<prefix>_*) because ActorArt caches toon materials globally by name; *_glow = unlit (the
    light tone), no outline. UV0 "rest" = rest-pose (z, y) in metres (side-view position).
  * Budget: <= 3500 triangles (validated).

Outputs (Tools/Blender/_tmp/actors3d/):
  legend_<id>.fbx  legend_<id>_palette.json  legend_<id>_report.json  legend_<id>_check.png  legend_<id>.blend
  --install also copies the FBX + palette JSON into Assets/Resources/Models/.
Run: blender -b --python variants/hybrid/hyb_legend3d.py [-- <fish_id> ... | --all] [--install]
     (no id = coelacanth; --all = every legends/*.py not starting with "_")
"""
import os
import sys
import math
import json
import re
import shutil
import importlib.util
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hyb_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix, Quaternion  # noqa: E402
import fk_common as C  # noqa: E402
import hyb_legend_kit as K  # noqa: E402
from hyb_legend_kit import (V, S, F2W, W2F, U2W, C_UW, r3, u_of_w, w_of_u, Part, loft, plate, ellipsoid,  # noqa: E402,F401
                            frame_from, decal, strip_decal, Body)

LEGEND_DIR = os.path.join(HERE, "legends")
OUT = os.path.join(R.BL, "_tmp", "actors3d")
WORK = os.path.join(OUT, "_work_legend")            # set per legend by set_work() (parallel builds never share it)
MODELS = os.path.join(C.ROOT, "Assets", "Resources", "Models")

# same as hyb_actors3d.FBX_OPTS (copied: importing hyb_actors3d pulls in fk_items / hyb_character)
FBX_OPTS = dict(
    use_selection=False, object_types={"ARMATURE", "MESH", "EMPTY"},
    axis_forward="-Z", axis_up="Y", use_space_transform=True, bake_space_transform=False,
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    use_mesh_modifiers=True, mesh_smooth_type="FACE", use_tspace=False, use_triangles=False,
    use_custom_props=False, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    use_armature_deform_only=False, armature_nodetype="NULL", bake_anim=False,
    path_mode="AUTO", embed_textures=False, use_metadata=True,
)
BONE_LEN = 0.03
TRI_BUDGET = 3500

# ============================================================================ the generic skeleton (Legend3D.cs)
# REQUIRED on every legend (Legend3D poses them; the encounter needs the eyes and the mouth)
REQUIRED_BONES = ["Root", "Spine.F", "Head", "Jaw", "Spine.B1", "Spine.B2", "Spine.B3", "Tail", "Tail.Upper",
                  "Tail.Lower", "Pec.L", "Pec.R"]
REQUIRED_EMPTIES = ["Eye.L", "Eye.R", "Mouth"]
# OPTIONAL generic bones (posed when present, skipped when absent)
OPTIONAL_BONES = ["Skull", "Pec.L.Fan", "Pec.R.Fan", "Pel.L", "Pel.R", "Pel.L.Fan", "Pel.R.Fan", "Dorsal1",
                  "Dorsal2", "Dorsal2.Fan", "Anal", "Anal.Fan", "Tail.Mid"]
# species bones in use (Docs/legends_rollout.md 7): any other name is reported as "unknown bone" (a warning)
SPECIES_BONES = ["Lips", "Barbel.L1", "Barbel.R1", "Barbel.L2", "Barbel.R2", "Bill", "UpperJaw", "EyeRoll.L",
                 "EyeRoll.R"]
BONE_RE = re.compile(r"^[A-Z][A-Za-z0-9]*(\.[A-Z0-9][A-Za-z0-9]*)*$")
# the contour groups of the check renders: no ink line between these (one continuous skin)
BODY_GROUP = {"Head", "Skull", "Jaw", "Spine.F", "Spine.B1", "Spine.B2", "Spine.B3", "Tail", "Tail.Mid"}
LURE_LIGHT_KEYS = ["abyss", "fogOutline", "eyeCore", "eyeGlow", "frameLine"]
# check-render poses (Unity localRotation Euler degrees; a bone the model lacks is skipped)
CHECK_OPEN = {"Jaw": (40, 0, 0), "Skull": (-8, 0, 0), "Dorsal1": (60, 0, 0),
              "Pec.L": (0, -20, 0), "Pec.L.Fan": (0, -15, 0), "Pec.R": (0, 20, 0), "Pec.R.Fan": (0, 15, 0)}
CHECK_BEND = {"Spine.F": (0, -6, 0), "Head": (0, -12, 0), "Spine.B1": (0, 10, 0), "Spine.B2": (0, 15, 0),
              "Spine.B3": (0, 15, 0), "Tail": (0, 15, 0), "Tail.Upper": (0, 8, 0), "Tail.Lower": (0, 8, 0),
              "Tail.Mid": (0, 12, 0), "Pec.L.Fan": (0, -15, 0), "Pec.R.Fan": (0, 15, 0)}


# ============================================================================ legend modules
def legend_ids():
    """Every legend module in legends/ (files starting with "_" are templates / helpers)."""
    return sorted(f[:-3] for f in os.listdir(LEGEND_DIR) if f.endswith(".py") and not f.startswith("_"))


def load_module(fid):
    path = os.path.join(LEGEND_DIR, fid + ".py")
    if not os.path.isfile(path):
        raise SystemExit("LEG ERROR no legend module %s (have: %s)" % (path, ", ".join(legend_ids())))
    spec = importlib.util.spec_from_file_location("legend_" + fid, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def definition(fid):
    """The legend's definition dict (module attributes + builder defaults)."""
    m = load_module(fid)
    missing = [k for k in ("ARMATURE", "PREFIX", "CM", "BONES", "RAMPS", "LURE_LIGHT", "build") if not hasattr(m, k)]
    if missing:
        raise SystemExit("LEG ERROR legends/%s.py lacks %s (see legends/README.md)" % (fid, ", ".join(missing)))
    g = lambda k, d=None: getattr(m, k, d)  # noqa: E731
    return dict(
        id=g("ID", fid), model=g("MODEL", "legend_" + fid), armature=g("ARMATURE"), prefix=g("PREFIX"),
        build=m.build, bones=m.BONES, ramps=m.RAMPS, outline=g("OUTLINE", "#0b1322"), lure_light=m.LURE_LIGHT,
        cm=tuple(g("CM")), preset=g("PRESET", "cave"), stage=g("STAGE"), backdrop=g("BACKDROP"),
        check_open=g("CHECK_OPEN", CHECK_OPEN), check_bend=g("CHECK_BEND", CHECK_BEND),
        body_group=BODY_GROUP | set(g("BODY_GROUP", ())), rig=g("RIG"), face_bones=g("FACE_BONES"),
        module=m,
    )


class _Legends(dict):
    """LEGENDS[fid] loads legends/<fid>.py on first use (hyb_legend_preview.py reads it)."""

    def __missing__(self, fid):
        self[fid] = definition(fid)
        return self[fid]


LEGENDS = _Legends()


def set_work(fid):
    global WORK
    WORK = os.path.join(OUT, "_work_legend_" + fid)


# ============================================================================ palette
def export_material(name, ramp):
    """Blender material named exactly like the palette key (diffuse = mid tone, the Unity importer's fallback)."""
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    col = C.lin(ramp[1])
    nt = m.node_tree
    if nt is not None:
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            nt.nodes.clear()
            bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
            out = nt.nodes.new("ShaderNodeOutputMaterial")
            nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
        bsdf.inputs["Base Color"].default_value = col
        bsdf.inputs["Roughness"].default_value = 1.0
        for nm in ("Specular IOR Level", "Specular"):
            if nm in bsdf.inputs:
                bsdf.inputs[nm].default_value = 0.0
    m.diffuse_color = col
    return m


# ============================================================================ validation (printed; errors stop the export)
HEX_RE = re.compile(r"^#[0-9a-fA-F]{6}$")


def validate_definition(L):
    err, warn = [], []
    pre = L["prefix"]
    if not pre or not re.match(r"^[a-z]+$", pre):
        err.append("PREFIX must be lower-case letters (got %r)" % pre)
    if not L["armature"]:
        err.append("ARMATURE (the FBX root node name) missing")
    if not L["model"].startswith("legend_"):
        err.append("MODEL must be legend_<fish_id> (got %s)" % L["model"])
    names = [b for b, _, _ in L["bones"]]
    if len(set(names)) != len(names):
        err.append("duplicate bone names")
    for b, par, _ in L["bones"]:
        if not BONE_RE.match(b):
            err.append("bone name %r breaks the naming rule (Name, Name.L, Name.L1, Name.Fan ...)" % b)
        if par is not None and par not in names:
            err.append("bone %s: parent %s missing" % (b, par))
        if b not in REQUIRED_BONES + OPTIONAL_BONES + SPECIES_BONES:
            warn.append("unknown bone %s (not in the generic / species lists: tell the code agent)" % b)
    for b in REQUIRED_BONES:
        if b not in names:
            err.append("required bone %s missing" % b)
    if names and names[0] != "Root":
        err.append("the first bone must be Root")
    for k, v in L["ramps"].items():
        if not k.startswith(pre + "_"):
            err.append("material %s lacks the prefix %s_" % (k, pre))
        if len(v) != 3 or not all(HEX_RE.match(c) for c in v):
            err.append("material %s: ramp must be 3 '#rrggbb' colours dark -> light" % k)
    for slot in ("back", "side", "belly", "fin", "eye_ring", "eye_glow"):
        if "%s_%s" % (pre, slot) not in L["ramps"]:
            err.append("required palette slot %s_%s missing" % (pre, slot))
    for k in LURE_LIGHT_KEYS:
        if not HEX_RE.match(str(L["lure_light"].get(k, ""))):
            err.append("LURE_LIGHT.%s must be '#rrggbb'" % k)
    return err, warn


def validate_build(L, parts, empties, tris, bbox):
    err, warn = [], []
    names = [b for b, _, _ in L["bones"]]
    for e in REQUIRED_EMPTIES:
        if e not in empties:
            err.append("required empty %s missing" % e)
    for e in ("Eye.L", "Eye.R"):
        if e in empties and empties[e][2] is None:
            err.append("%s needs a facing (the eye's +Z)" % e)
        if e not in parts:
            err.append("glow lens part %s (geo_%s) missing" % (e, e))
        elif set(parts[e].mats) != {L["prefix"] + "_eye_glow"}:
            err.append("geo_%s must use only %s_eye_glow" % (e, L["prefix"]))
    for name, part in parts.items():
        if name not in names and name not in empties:
            err.append("part %s is neither a bone nor an empty" % name)
        for m in part.mats:
            if m not in L["ramps"]:
                err.append("part %s uses %s, not in RAMPS" % (name, m))
    for b in names[1:]:
        if b not in parts:
            warn.append("bone %s has no geo_%s part" % (b, b))
    if tris > TRI_BUDGET:
        err.append("%d triangles > budget %d" % (tris, TRI_BUDGET))
    (x0, y0, z0), (x1, y1, z1) = bbox
    if abs(z1 - 0.5) > 0.01 or abs(z0 + 0.5) > 0.01:
        warn.append("length: z %.3f .. %.3f (want -0.50 .. +0.50)" % (z0, z1))
    return err, warn


def report_problems(fid, err, warn, stage):
    for w in warn:
        print("LEG WARN", fid, w)
    for e in err:
        print("LEG ERROR", fid, e)
    if err:
        raise SystemExit("LEG ERROR %s: %d problem(s) in %s - nothing exported" % (fid, len(err), stage))
    print("LEG validate", fid, stage, "ok", "(%d warnings)" % len(warn) if warn else "")


# ============================================================================ rig + export
def build_armature(name, bones):
    ad = bpy.data.armatures.new(name)
    arm = bpy.data.objects.new(name, ad)
    C.link(arm)
    arm.matrix_world = F2W
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, par, head in bones:
        hf = S.to_3x3() @ V(head)                  # Unity -> FBX / armature space
        eb = ad.edit_bones.new(bn)
        eb.head = hf
        eb.tail = hf + V((0, BONE_LEN, 0))         # every bone along armature +Y, roll 0 = identity rest rotation
        eb.roll = 0.0
        eb.use_connect = False
        eb.use_deform = True
        if par:
            eb.parent = ad.edit_bones[par]
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    return arm


def bone_world(arm, name):
    return arm.matrix_world @ arm.data.bones[name].matrix_local


def parent_to_bone(ob, arm, name, local=None):
    """Rigid bone parent; `local` = transform relative to the bone HEAD frame (Blender parents to the tail)."""
    b = arm.data.bones[name]
    ob.parent = arm
    ob.parent_type = "BONE"
    ob.parent_bone = name
    ob.matrix_parent_inverse = Matrix()
    ob.matrix_basis = Matrix.Translation((0, -b.length, 0)) @ (local or Matrix())


def part_object(part, arm, bone, mats):
    """Part (Unity space) -> mesh object geo_<part.name> in the frame of `bone`, parented to it."""
    bm = part.bm
    uv = bm.loops.layers.uv.new("rest")
    for f in bm.faces:
        for lp in f.loops:
            lp[uv].uv = (lp.vert.co.z, lp.vert.co.y)
    bm.transform(U2W)                                   # reflection: flip the winding back
    import bmesh
    bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    bm.transform(bone_world(arm, bone).inverted())
    bm.normal_update()
    me = bpy.data.meshes.new("geo_" + part.name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("geo_" + part.name, me)
    C.link(ob)
    for mn in part.mats:
        me.materials.append(mats[mn])
    parent_to_bone(ob, arm, bone)
    return ob


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# 14 directions (axes + corner diagonals) for the face outline's extreme points
FACE_DIRS = [V(d).normalized() for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))
             + tuple((sx, sy, sz) for sx in (1, -1) for sy in (1, -1) for sz in (1, -1))]


def face_outline(parts, bones, face_bones):
    """The face (what captions must never cover) as extreme points of the face bones' parts, in Unity model space
    at rest: [{"bone", "at" (bone head), "pts" (flat x, y, z ...)}] -> Legend3D.FacePoints (palette "_face")."""
    heads = {b: h for b, _, h in bones}
    out = []
    for b in face_bones:
        if b not in parts:
            continue
        co = [v.co.copy() for v in parts[b].bm.verts]
        pts = []
        for d in FACE_DIRS:
            p = max(co, key=lambda c: c.dot(d))
            q = r3(p)
            if q not in pts:
                pts.append(q)
        out.append({"bone": b, "at": r3(heads[b]), "pts": [x for p in pts for x in p]})
    return out


def build(fid):
    L = LEGENDS[fid]
    report_problems(fid, *validate_definition(L), "definition")
    C.reset_scene()
    R.reset_materials()
    mats = {k: export_material(k, v) for k, v in L["ramps"].items()}
    arm = build_armature(L["armature"], L["bones"])
    parts, empties, info = L["build"]()
    face = face_outline(parts, L["bones"], L["face_bones"]) if L["face_bones"] else None
    us0 = np.array([tuple(v.co) for p in parts.values() for v in p.bm.verts])      # Unity model space, at rest
    geo, tris = {}, 0
    bone_names = [b for b, _, _ in L["bones"]]
    part_names = dict(parts)
    for name, part in parts.items():
        bone = name if name in bone_names else empties[name][0]       # geo_Eye.* ride on their empty's bone
        ob = part_object(part, arm, bone, mats)
        geo[ob.name] = {"bone": bone, "tris": tri_count(ob), "materials": list(part.mats)}
        tris += geo[ob.name]["tris"]
    report_problems(fid, *validate_build(L, part_names, empties, tris, (us0.min(0), us0.max(0))), "model")
    emp = {}
    for name, (bone, pos, facing) in empties.items():
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = "ARROWS" if facing is not None else "SPHERE"
        e.empty_display_size = 0.02
        C.link(e)
        pf = S.to_3x3() @ V(pos)
        head_f = S.to_3x3() @ V(next(h for b, _, h in L["bones"] if b == bone))
        rot = Matrix()
        if facing is not None:
            df = (S.to_3x3() @ V(facing)).normalized()
            up = V((0, 1, 0))
            yv = (up - df * up.dot(df)).normalized()
            xv = yv.cross(df)
            rot = Matrix((xv, yv, df)).transposed().to_4x4()          # F-space: local +Z = facing
        parent_to_bone(e, arm, bone, Matrix.Translation(pf - head_f) @ rot)
        emp[name] = {"parent": bone, "pos": r3(pos), "facingZ": r3(V(facing).normalized()) if facing is not None else None}
    bpy.context.view_layer.update()
    os.makedirs(OUT, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, L["model"] + ".blend"))
    fbx = os.path.join(OUT, L["model"] + ".fbx")
    bpy.ops.export_scene.fbx(filepath=fbx, **FBX_OPTS)
    pal = {k: {"ramp": [c.lower() for c in v], "outline": L["outline"]} for k, v in L["ramps"].items()}
    pal["_lureLight"] = L["lure_light"]
    if L["rig"]:
        pal["_rig"] = L["rig"]
    if face:
        pal["_face"] = face
    with open(os.path.join(OUT, L["model"] + "_palette.json"), "w", encoding="utf-8") as f:
        json.dump(pal, f, indent=1)
    # rest numbers (Unity model space)
    allw = [ob.matrix_world @ v.co for ob in bpy.data.objects if ob.type == "MESH" for v in ob.data.vertices]
    us = np.array([tuple(u_of_w(w)) for w in allw])
    rep = {
        "model": L["model"], "fbxRoot": L["armature"], "frame": "Unity model space: x = the fish's right, y up, z = "
        "towards the nose; bones have identity rest rotation (local axes = these axes)",
        "lengthModelled_m": 1.0, "runtimeScale": "cm / 100 (in-game %d..%d cm)" % L["cm"],
        "bboxUnity": [r3(us.min(0)), r3(us.max(0))], "tris": tris, "parts": geo, "empties": emp,
        "bones": {b: {"parent": p, "head": r3(h)} for b, p, h in L["bones"]}, "materials": list(L["ramps"]),
        "info": info,
    }
    if L["rig"]:
        rep["rig"] = L["rig"]
    if face:
        rep["face"] = face
    print("LEG built", fid, "tris", tris, "bbox", rep["bboxUnity"])
    return rep


# ============================================================================ check: raw FBX + re-import + renders
def fbx_scene(path):
    """Model nodes of an FBX as Unity reads them (from hyb_actors3d_check.fbx_scene)."""
    from io_scene_fbx import parse_fbx
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
                S=V(pr.get("Lcl Scaling", (1, 1, 1))), pre=V(pr.get("PreRotation", (0, 0, 0))), parent=None, geo=None)
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
    unit = float(gs["UnitScaleFactor"][0]) / 100.0
    axes = {k: int(gs[k][0]) for k in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign")}
    return dict(models=models, geoms=geoms, unit=unit, axes=axes, usf=float(gs["UnitScaleFactor"][0]))


def fbx_check(path, bones, rotated=("Eye.L", "Eye.R")):
    """`rotated`: the empties that carry a facing rotation (left out of the bone-rotation check)."""
    fs = fbx_scene(path)
    ms = fs["models"]
    byname = {m["name"]: m for m in ms.values()}
    roots = [m for m in ms.values() if m["parent"] is None]
    rot_max = max(max(abs(a) for a in m["R"]) + max(abs(a) for a in m["pre"]) for m in ms.values() if m["type"] != "Mesh"
                  and m["name"] not in rotated)
    mesh_T = max(m["T"].length for m in ms.values() if m["type"] == "Mesh")
    mesh_R = max(max(abs(a) for a in m["R"]) for m in ms.values() if m["type"] == "Mesh")

    def glob(m):
        T = V((0, 0, 0))
        while m is not None:
            T = T + m["T"]      # identity rotations on the bone chain: world = sum of translations
            m = ms.get(m["parent"]) if m["parent"] else None
        return T
    head_err = 0.0
    for b, _, h in bones:
        g = glob(byname[b]) * fs["unit"]
        head_err = max(head_err, (V((-g.x, g.y, g.z)) - V(h)).length)
    return {"axes": fs["axes"], "UnitScaleFactor": fs["usf"],
            "roots": {m["name"]: {"T": r3(m["T"]), "R": r3(m["R"]), "S": r3(m["S"])} for m in roots},
            "boneNodesMaxRotationDeg": round(rot_max, 6), "meshNodesMaxLocalT": round(mesh_T, 7),
            "meshNodesMaxLocalR": round(mesh_R, 6), "boneHeadsMaxErr_m": round(head_err, 7),
            "meshNodes": sum(1 for m in ms.values() if m["type"] == "Mesh"),
            "parentOf": {m["name"]: ms[m["parent"]]["name"] for m in ms.values() if m["parent"]}}


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False, ignore_leaf_bones=False, automatic_bone_orientation=False)
    return [o for o in bpy.data.objects if o not in before]


def base_name(n):
    return re.sub(r"\.\d{3}$", "", n)


def toon_mat(key, ramp, light=None, lure=None, abyss=None, unlit=False):
    """Emission material that emulates ActorToon: 3 hard bands of f = 0.5 + 0.5 dot(N, key) (world key light), or the
    encounter's lure light (lure = (world pos, radius)): lit = 1 - smoothstep(0.35 r, r, d),
    f = (0.5 + 0.5 dot(N, towards the lure)) * lit, f < 0.12 -> abyss."""
    m = bpy.data.materials.new("T_" + key)
    nb = R.NB(m)
    if unlit:
        nb.output_emission(nb.rgb(C.lin(ramp[2])), 1.0)
        return m
    geo = nb.node("ShaderNodeNewGeometry")
    if lure is None:
        d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(V(light).normalized()))
        f = nb.math("MULTIPLY_ADD", d, 0.5, 0.5)
        col = nb.ramp(f, [(0.0, C.lin(ramp[0])), (0.45, C.lin(ramp[1])), (0.74, C.lin(ramp[2]))], "CONSTANT")
    else:
        lp, rad = lure
        to = nb.vmath("SUBTRACT", tuple(lp), geo.outputs["Position"])
        dist = nb.vmath("LENGTH", to)
        d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], nb.vmath("NORMALIZE", to))
        half = nb.math("MULTIPLY_ADD", d, 0.5, 0.5)
        mr = nb.node("ShaderNodeMapRange")
        mr.interpolation_type = "SMOOTHSTEP"
        mr.clamp = True
        nb.link(dist, mr.inputs["Value"])
        mr.inputs["From Min"].default_value = 0.35 * rad
        mr.inputs["From Max"].default_value = rad
        mr.inputs["To Min"].default_value = 1.0
        mr.inputs["To Max"].default_value = 0.0
        f = nb.math("MULTIPLY", half, mr.outputs["Result"])
        col = nb.ramp(f, [(0.0, C.lin(abyss)), (0.12, C.lin(ramp[0])), (0.45, C.lin(ramp[1])), (0.74, C.lin(ramp[2]))],
                      "CONSTANT")
    nb.output_emission(col, 1.0)
    return m


def lit_mat(key, pid, lure=None):
    """ID pass: R = part id, G = camera depth, B = lure-light 'lit' (1 without a lure)."""
    m = bpy.data.materials.new("I_" + key)
    nb = R.NB(m)
    cd = nb.node("ShaderNodeCameraData")
    if lure is None:
        lit = 1.0
    else:
        lp, rad = lure
        geo = nb.node("ShaderNodeNewGeometry")
        dist = nb.vmath("LENGTH", nb.vmath("SUBTRACT", tuple(lp), geo.outputs["Position"]))
        mr = nb.node("ShaderNodeMapRange")
        mr.interpolation_type = "SMOOTHSTEP"
        mr.clamp = True
        nb.link(dist, mr.inputs["Value"])
        mr.inputs["From Min"].default_value = 0.35 * rad
        mr.inputs["From Max"].default_value = rad
        mr.inputs["To Min"].default_value = 1.0
        mr.inputs["To Max"].default_value = 0.0
        lit = mr.outputs["Result"]
    nb.output_emission(nb.combine(float(pid), cd.outputs["View Z Depth"], lit), 1.0)
    return m


class Toon:
    """Re-imported legend + toon materials; renders pixel-exact frames with the ActorToon outline hull emulated
    (1 px outside the silhouette + inner contour where a nearer part overlaps a farther one)."""

    def __init__(self, fid, objs):
        self.L = LEGENDS[fid]
        with open(os.path.join(OUT, self.L["model"] + "_palette.json"), encoding="utf-8") as f:
            self.pal = json.load(f)
        self.objs = objs
        self.meshes = [o for o in objs if o.type == "MESH"]
        self.arm = next(o for o in objs if o.type == "ARMATURE")
        self.unlit_ids = set()
        # contour groups: the real hull draws no line on the seams between body segments (continuous surface) or
        # between a lobe's stalk and its fan; only where a separate fin / lobe overlaps the body
        body = self.L["body_group"]
        self.group = np.zeros(len(self.meshes) + 1, int)
        gnames = {}
        for i, o in enumerate(self.meshes):
            o["pid"] = i + 1
            if all(base_name(s.material.name).endswith("_glow") for s in o.material_slots):
                self.unlit_ids.add(i + 1)
            bn = base_name(o.name)[4:]
            g = "body" if bn in body else bn.replace(".Fan", "")
            self.group[i + 1] = gnames.setdefault(g, len(gnames) + 1)
        self.orig = {o.name: [base_name(s.material.name) for s in o.material_slots] for o in self.meshes}

    def set_materials(self, light=None, lure=None):
        cache = {}
        ab = self.L["lure_light"]["abyss"]
        for o in self.meshes:
            for s, nm in zip(o.material_slots, self.orig[o.name]):
                if nm not in cache:
                    cache[nm] = toon_mat(nm, self.pal[nm]["ramp"], light, lure, ab, unlit=nm.endswith("_glow"))
                s.material = cache[nm]

    def render(self, tag, lure=None, light=(0.3, -0.5, 0.8), outline=True, rim=None):
        """-> top-down RGBA float (sRGB) of the current camera / resolution."""
        self.set_materials(light=light, lure=lure)
        sc = bpy.context.scene
        sc.render.filter_size = 0.0
        try:
            sc.eevee.taa_render_samples = 1
        except Exception:
            pass
        col = _render_exr(tag + "_col")
        rgb = R.l2s(col[..., :3])
        a = col[..., 3] > 0.5
        saved = []
        for o in self.meshes:
            m = lit_mat(o.name, o["pid"], lure)
            for s in o.material_slots:
                saved.append((s, s.material))
                s.material = m
        ids = _render_exr(tag + "_id")
        for s, m in saved:
            s.material = m
        pid = np.round(ids[..., 0]).astype(int)
        pid[~a] = 0
        depth = np.where(a, ids[..., 1], 1e9)
        lit = ids[..., 2]
        out = np.zeros(rgb.shape[:2] + (4,), np.float32)
        out[a, :3] = rgb[a]
        out[a, 3] = 1
        if outline:
            ll = self.L["lure_light"]
            ink = R.hexrgb(self.L["outline"])
            fog = R.hexrgb(ll["fogOutline"])
            H_, W_ = pid.shape
            lined = np.zeros((H_, W_), bool)
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nb_id = R.shift(pid, dy, dx, 0)
                nb_d = R.shift(depth, dy, dx, 1e9)
                nb_l = R.shift(lit, dy, dx, 0.0)
                src_ok = (nb_id > 0) & ~np.isin(nb_id, list(self.unlit_ids))
                outer = (pid == 0) & src_ok
                inner = ((pid > 0) & src_ok & (self.group[pid] != self.group[nb_id]) & (depth - nb_d > 0.025)
                         & ~np.isin(pid, list(self.unlit_ids)))
                m = (outer | inner) & ~lined
                c = np.where((nb_l > 0.3)[..., None], ink, fog) if lure is not None else np.broadcast_to(ink, (H_, W_, 3))
                out[m, :3] = c[m]
                out[m, 3] = 1
                lined |= m
            self.last_outline = lined
        self.last_pid = pid
        self.last_lit = lit
        if rim is not None:
            out = rim_light(out, pid, rim)
        return out


def rim_light(img, pid, rim):
    """ActorRim emulation: body pixels whose neighbour towards the light (dirs) is outline / empty and then empty get
    lighter towards the rim colour (hyb_core.rim_fn), the outline pixel in front of them becomes the lit body tone."""
    col, strength, dirs = rim
    body = pid > 0
    opaque = img[..., 3] > 0.5
    fn = R.rim_fn(col, strength)
    H_, W_ = body.shape
    k = np.zeros((H_, W_))
    for (dx, dy), w in dirs:
        # pixel step towards the light (image rows grow downwards: up = -1)
        n1 = R.shift(~opaque, dy, -dx, True) | R.shift(~body & opaque, dy, -dx, False)
        n2 = R.shift(~opaque, 2 * dy, -2 * dx, True)
        behind = R.shift(body, -dy, dx, False)
        hit = body & behind & ((R.shift(~opaque, dy, -dx, True)) | (n1 & n2))
        k = np.maximum(k, hit * w)
    m = k >= 0.45
    out = img.copy()
    if m.any():
        out[m, :3] = fn(img[m, :3].astype(np.float64))
    return out


def _render_exr(tag):
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


def mount_unity(objs, M_U, name="Mount"):
    """Parent the imported roots to an empty so that the asset's Unity-space pose is M_U (4x4 in a Unity-like set
    frame: x right, y up, z forward). Blender world W <-> Unity: W = (-x, -z, y)."""
    Cm = C_UW.to_4x4()
    e = bpy.data.objects.new(name, None)
    C.link(e)
    e.matrix_world = Cm @ M_U @ Cm.inverted()
    bpy.context.view_layer.update()
    for o in objs:
        if o.parent is None:
            mw = o.matrix_world.copy()
            o.parent = e
            o.matrix_parent_inverse = Matrix()
            o.matrix_basis = mw
    bpy.context.view_layer.update()
    return e


def pose(arm, name, ax=0.0, ay=0.0, az=0.0):
    """Unity localRotation Euler (degrees; Unity order Z, X, Y) on an identity-rest bone -> Blender pose. In the FBX /
    armature frame x is negated: X rotations keep their sign, Y and Z rotations flip."""
    pb = arm.pose.bones[name]
    pb.rotation_mode = "QUATERNION"
    q = (Matrix.Rotation(math.radians(-ay), 3, "Y") @ Matrix.Rotation(math.radians(ax), 3, "X")
         @ Matrix.Rotation(math.radians(-az), 3, "Z")).to_quaternion()
    pb.rotation_quaternion = q


def pose_move(arm, name, dx=0.0, dy=0.0, dz=0.0):
    """Unity localPosition offset (model metres; a protrusion bone) -> Blender pose location (x negated)."""
    arm.pose.bones[name].location = V((-dx, dy, dz))


def apply_poses(arm, poses):
    """{bone: (ax, ay, az) | {"rot": (ax, ay, az), "move": (dx, dy, dz)}}; bones the model lacks are skipped."""
    for b, v in poses.items():
        if b not in arm.pose.bones:
            continue
        if isinstance(v, dict):
            if "rot" in v:
                pose(arm, b, *v["rot"])
            if "move" in v:
                pose_move(arm, b, *v["move"])
        else:
            pose(arm, b, *v)


def reset_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = V((0, 0, 0))
    bpy.context.view_layer.update()


def cam_look(pos_u, target_u, fpx, w, h, shift_px=(0.0, 0.0), roll=0.0, up=(0.0, 1.0, 0.0)):
    """Perspective camera from Unity-space position / target with a focal length in pixels; shift_px moves the
    principal point (pixels, x right, y up) from the image centre."""
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cd = bpy.data.cameras.new("LegCam")
        cam = bpy.data.objects.new("LegCam", cd)
        C.link(cam)
        sc.camera = cam
    cd = cam.data
    cd.type = "PERSP"
    cd.sensor_fit = "HORIZONTAL"
    cd.sensor_width = 36.0
    cd.lens = fpx * 36.0 / w
    cd.clip_start = 0.05
    cd.clip_end = 200.0
    big = max(w, h)
    cd.shift_x = -shift_px[0] / big          # Blender: +shift moves the frame, i.e. the principal point the other way
    cd.shift_y = -shift_px[1] / big
    f = (V(target_u) - V(pos_u)).normalized()
    r = V(up).cross(f).normalized()          # Unity numerics: right = up x forward, up = forward x right
    u = f.cross(r).normalized()
    if roll:
        q = Matrix.Rotation(math.radians(roll), 3, f)
        r, u = q @ r, q @ u
    M = Matrix((C_UW @ r, C_UW @ u, -(C_UW @ f))).transposed().to_4x4()
    M.translation = C_UW @ V(pos_u)
    cam.matrix_world = M
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = True
    sc.render.use_border = False
    return cam


def ortho_look(pos_u, target_u, scale, w, h):
    cam = cam_look(pos_u, target_u, 100.0, w, h)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    return cam


def check(fid, rep):
    """Re-import the FBX, verify, render legend_<id>_check.png: rest (side) | jaw open | maximum bend (top)."""
    L = LEGENDS[fid]
    fbx = os.path.join(OUT, L["model"] + ".fbx")
    rotated = tuple(n for n, e in rep["empties"].items() if e["facingZ"] is not None)
    rep["fbx"] = fbx_check(fbx, L["bones"], rotated)
    C.reset_scene()
    R.reset_materials()
    objs = import_fbx(fbx)
    arm = next(o for o in objs if o.type == "ARMATURE")
    meshes = [o for o in objs if o.type == "MESH"]
    rep["reimport"] = {"bones": len(arm.data.bones), "meshes": len(meshes),
                       "tris": sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons),
                       "materials": sorted({base_name(s.material.name) for o in meshes for s in o.material_slots}),
                       "boneRestMaxOffAxisDeg": 0.0}
    dev = 0.0
    for b in arm.data.bones:
        m3 = b.matrix_local.to_3x3()
        dev = max(dev, math.degrees(m3.to_quaternion().angle))
    rep["reimport"]["boneRestMaxOffAxisDeg"] = round(dev, 5)
    mount_unity(objs, Matrix())
    T = Toon(fid, objs)
    shots = []
    key = C_UW @ V((0.45, 0.7, -0.35)).normalized()       # from above, the camera side and a little behind
    # 1) rest, side view of the right flank (nose to the right), game close-up scale
    reset_pose(arm)
    cam_look((1.6, 0.05, 0.0), (0, 0.0, 0.0), 300.0, 256, 96)
    shots.append(("rest (side)", T.render("rest", light=key)))
    # 2) mouth open (CHECK_OPEN: jaw 40, skull up 8, dorsal1 raised 60, pectorals flared ...); 3/4 front view
    apply_poses(arm, L["check_open"])
    bpy.context.view_layer.update()
    cam_look((0.95, 0.25, 1.05), (0, 0.0, 0.08), 300.0, 256, 128)
    shots.append(("jaw open", T.render("jaw", light=key)))
    # 3) maximum bend (CHECK_BEND: 15 deg per spine joint, C bend + tail lobes), top view
    reset_pose(arm)
    apply_poses(arm, L["check_bend"])
    bpy.context.view_layer.update()
    cam_look((0.0, 1.5, 0.0), (0.0, 0.0, 0.0), 300.0, 256, 128, up=(-1.0, 0.0, 0.0))
    shots.append(("max bend (top)", T.render("bend", light=key)))
    reset_pose(arm)
    # sheet: each shot 3x on a mid grey, plus a hi-res (4x) side view
    bg = (0.30, 0.33, 0.38)
    imgs = [R.upscale(_on(s[1], bg), 3) for s in shots]
    sheet = R.flow_sheet([[imgs[0]], [imgs[1], imgs[2]]], (0.2, 0.22, 0.26), scale=1, pad=10)
    R.save_png(sheet, os.path.join(OUT, L["model"] + "_check.png"))
    shutil.rmtree(WORK, ignore_errors=True)
    return rep


def _on(rgba, bg):
    o = rgba.copy()
    o[..., :3] = rgba[..., :3] * rgba[..., 3:4] + np.array(bg) * (1 - rgba[..., 3:4])
    o[..., 3] = 1
    return o


def install(fid):
    L = LEGENDS[fid]
    os.makedirs(MODELS, exist_ok=True)
    for ext in (".fbx", "_palette.json"):
        shutil.copy2(os.path.join(OUT, L["model"] + ext), os.path.join(MODELS, L["model"] + ext))
    print("LEG installed", L["model"], "->", MODELS)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ids = legend_ids() if "--all" in argv else ([a for a in argv if not a.startswith("--")] or ["coelacanth"])
    for fid in ids:
        L = LEGENDS[fid]
        R.use_preset(L["preset"])
        set_work(fid)
        rep = build(fid)
        rep = check(fid, rep)
        with open(os.path.join(OUT, L["model"] + "_report.json"), "w", encoding="utf-8") as f:
            json.dump(rep, f, indent=1, default=str)
        print("LEG check", json.dumps({k: rep[k] for k in ("fbx", "reimport")}, default=str))
        if "--install" in argv:
            install(fid)
    print("LEG done")


if __name__ == "__main__":
    main()
