"""
hybrid - real-time 3D actors for Unity: a rigged 3D angler (rigid, bone-parented body parts) and one 3D reel per
fk_items.REELS entry, exported as FBX + palette JSON for a Unity toon shader.

The angler is the hyb_character model (same ~6-head proportions, palette PAL graded at 0.55, bucket hat, plaid
shirt, olive vest, wicker creel on a strap, trousers, boots) rebuilt in a neutral REST pose (standing, arms
hanging slightly away from the body, elbows nearly straight). The reels follow fk_items.build_world_reel.

Outputs (Tools/Blender/_tmp/actors3d/):
  angler.fbx  angler.blend (the build scene, for inspection)  angler_palette.json
  reel_<id>.fbx (one per REELS id)  reel_palette.json
  actors3d_data.json   rest-pose numbers in UNITY space (joints, bone lengths, grips, reel anchors)
Check: blender -b --python variants/hybrid/hyb_actors3d_check.py  (re-import + renders)

Frames
  C  hyb char frame (hyb_character): x forward, y = character's LEFT, z up, feet at 0
  W  Blender world (build + .blend): the character faces -Y, his left = +X, z up        W = C2W @ C
  F  FBX file / armature space:     faces +Z, left = +X, y up (right-handed)           F = W2F @ W
  U  Unity (importer negates x):    faces +Z, left = -X, y up (left-handed)            U = (-Fx, Fy, Fz)
The exporter (axis_forward '-Z', axis_up 'Y') pre-multiplies every ROOT object by Rx(-90). The armature object and
every reel's root empty carry F2W = Rx(+90), so the FBX root nodes are written with IDENTITY transforms (no -90
x rotation in Unity) and all bone / mesh data is already Y-up.

Run: blender -b --python variants/hybrid/hyb_actors3d.py
"""
import os
import sys
import math
import json
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hyb_core as R  # noqa: E402
import hyb_character as HC  # noqa: E402  (PAL, CL, brim, two_bone; its main() is not run)
import bpy  # noqa: E402
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_items as I  # noqa: E402

V = Vector
OUT = os.path.join(R.BL, "_tmp", "actors3d")
C2W = Matrix.Rotation(math.radians(-90), 4, "Z")
W2F = Matrix.Rotation(math.radians(-90), 4, "X")
F2W = W2F.inverted()

FBX_OPTS = dict(
    use_selection=False, object_types={"ARMATURE", "MESH", "EMPTY"},
    axis_forward="-Z", axis_up="Y", use_space_transform=True, bake_space_transform=False,
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    use_mesh_modifiers=True, mesh_smooth_type="FACE", use_tspace=False, use_triangles=False,
    use_custom_props=False, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    use_armature_deform_only=False, armature_nodetype="NULL", bake_anim=False,
    path_mode="AUTO", embed_textures=False, use_metadata=True,
)

# ---------------------------------------------------------------- rig dimensions (metres, from hyb_character)
L_UP, L_FORE, L_HAND, GRIP = 0.295, 0.265, 0.09, 0.045     # shoulder-elbow, elbow-wrist, wrist-knuckles, wrist-grip
L_THIGH, L_SHIN = 0.395, 0.375
BONES = [  # (name, parent) - parents before children
    ("Root", None), ("Hips", "Root"), ("Spine", "Hips"), ("Chest", "Spine"), ("Neck", "Chest"), ("Head", "Neck"),
    ("Shoulder.L", "Chest"), ("UpperArm.L", "Shoulder.L"), ("ForeArm.L", "UpperArm.L"), ("Hand.L", "ForeArm.L"),
    ("Shoulder.R", "Chest"), ("UpperArm.R", "Shoulder.R"), ("ForeArm.R", "UpperArm.R"), ("Hand.R", "ForeArm.R"),
    ("Thigh.L", "Hips"), ("Shin.L", "Thigh.L"), ("Foot.L", "Shin.L"),
    ("Thigh.R", "Hips"), ("Shin.R", "Thigh.R"), ("Foot.R", "Shin.R"),
]
SIDES = (("L", 1.0), ("R", -1.0))   # char frame: +y = the character's left


def u_of_c(c):
    """char frame point -> Unity space."""
    return V((-c[1], c[2], c[0]))


def u_of_f(f):
    return V((-f[0], f[1], f[2]))


def r3(v):
    return [round(float(x), 4) for x in v]


# ---------------------------------------------------------------- palette
def outline_of(ramp, deep, dl=-0.1, hue=None):
    """Hue-shifted darker tone of the darkest ramp colour. Where that would get darker than the sprite's darkest
    outline tone `deep` (hyb_character PAL['deep'], a violet navy), the outline IS `deep`: never black ink."""
    d = ramp[0]
    if R.lum(d) + dl <= R.lum(deep) + 0.01:
        return deep
    if hue is not None and warm(d):
        return shade_dir(d, dl, -hue, 1.05)
    return R.shade(d, dl, hue=hue)


def angler_ramps():
    P = HC.PAL
    le, kh, ol, tr = P["leather"], P["khaki"], P["olive"], P["trou"]
    return {
        "skin": list(P["skin"]),
        "hair": [le[0], le[0], le[1]],          # sprite: 2 tones, bound 0.7 (mostly dark)
        "hat": list(kh),
        "hatband": [le[0], le[1], le[1]],       # sprite: 2 tones, bound 0.6
        "shirt": list(P["shirt"]),
        "vest": list(ol),                       # sprite bounds 0.55 / 0.8
        "vest_dark": [ol[0]] * 3,               # yoke seam, hem (flat in the sprite)
        "vest_light": [ol[1], ol[2], ol[2]],    # pocket flap
        "trousers": list(tr),
        "trousers_dark": [tr[0]] * 3,           # seat seams, inseam (flat)
        "trousers_light": [tr[2]] * 3,          # fold behind the knee (flat)
        "boots": [le[0], le[1], le[1]],
        "boots_dark": [le[0]] * 3,              # heel, sole (flat)
        "strap": [le[0], le[0], le[1]],         # strap, belt, latch
        "bag": [kh[0], kh[1], kh[1]],           # wicker creel (sprite: khaki dark/mid, bound 0.3)
        "bag_lid": [kh[1], kh[1], kh[2]],
    }


def palette_json(ramps, **kw):
    deep = HC.PAL["deep"]
    return {k: {"ramp": [c.lower() for c in v], "outline": outline_of(v, deep, **kw).lower()} for k, v in ramps.items()}


def export_material(name, ramp):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    col = C.lin(ramp[1])
    nt = m.node_tree
    bsdf = None
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


# ---------------------------------------------------------------- geometry helpers
def bx(name, c, s, mat, bevel=0.0, segs=1, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= s[0]
        v.co.y *= s[1]
        v.co.z *= s[2]
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=segs, affect="EDGES")
    ob = C.mesh_object(name, bm, mat)
    M = Matrix.Translation(V(c))
    if rot is not None:
        M = M @ rot
    ob.matrix_world = M
    C.set_smooth(ob, segs > 1)
    return ob


def frame_rot(y_axis, x_hint):
    """4x4 rotation whose local +Y = y_axis and local +X ~ x_hint (right-handed)."""
    y = V(y_axis).normalized()
    x = V(x_hint)
    x = (x - y * x.dot(y)).normalized()
    z = x.cross(y)
    return Matrix((x, y, z)).transposed().to_4x4()


def bake(ob, M=None):
    """Apply (M @) the object's matrix to its mesh data and reset the object to identity."""
    mw = ob.matrix_world.copy()
    if M is not None:
        mw = M @ mw
    ob.data.transform(mw)
    ob.matrix_world = Matrix()
    return ob


def join(objs, name):
    """Join mesh objects (all baked to identity) into one multi-material mesh object."""
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    for uv in list(ob.data.uv_layers):
        ob.data.uv_layers.remove(uv)
    return ob


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def profile_at(prof, z):
    """Linear interpolation of a [(z, (ra, rb)), ...] profile."""
    if z <= prof[0][0]:
        return prof[0][1]
    for (z0, r0), (z1, r1) in zip(prof, prof[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / (z1 - z0)
            return (r0[0] + (r1[0] - r0[0]) * t, r0[1] + (r1[1] - r0[1]) * t)
    return prof[-1][1]


def profile_cut(prof, z0, z1, inset_from=None, inset=0.0):
    """Sub-profile z0..z1; above inset_from the radii shrink by `inset` (hidden inside the overlapping part)."""
    zs = [z0] + [z for z, _ in prof if z0 < z < z1] + [z1]
    if inset_from is not None:
        zs += [inset_from - 0.012, inset_from]
    zs = sorted(set(round(z, 4) for z in zs if z0 <= z <= z1))
    out = []
    for z in zs:
        ra, rb = profile_at(prof, z)
        if inset_from is not None and z >= inset_from:
            ra, rb = ra - inset, rb - inset
        out.append((z, (ra, rb)))
    return out


# ---------------------------------------------------------------- angler: joints (char frame, rest pose)
def joints():
    J = {}
    J["Root"] = (V((0, 0, 0)), V((0, 0, 0.25)))
    J["Hips"] = (V((0, 0, 0.88)), V((0, 0, 1.0)))
    J["Spine"] = (V((0, 0, 1.0)), V((0, 0, 1.2)))
    J["Chest"] = (V((0, 0, 1.2)), V((0.005, 0, 1.4)))
    J["Neck"] = (V((0.005, 0, 1.4)), V((0.012, 0, 1.52)))
    J["Head"] = (V((0.012, 0, 1.52)), V((0.012, 0, 1.72)))
    for s, sg in SIDES:
        sh = V((-0.005, sg * 0.19, 1.335))
        J["Shoulder." + s] = (V((0.0, sg * 0.05, 1.37)), sh)
        d1 = V((-0.04, sg * 0.16, -1.0)).normalized()        # upper arm: down, ~9 deg out, a little back
        el = sh + d1 * L_UP
        d2 = V((0.16, sg * 0.12, -1.0)).normalized()          # forearm: ~9 deg forward, ~7 deg out (elbow ~11 deg)
        wr = el + d2 * L_FORE
        J["UpperArm." + s] = (sh, el)
        J["ForeArm." + s] = (el, wr)
        J["Hand." + s] = (wr, wr + d2 * L_HAND)
        hip = V((0.0, sg * 0.094, 0.865))
        ank = V((0.03, sg * 0.1, 0.1))
        knee, _ = HC.two_bone(hip, ank, L_THIGH, L_SHIN, (1.0, 0.0, 0.25))
        J["Thigh." + s] = (hip, knee)
        J["Shin." + s] = (knee, ank)
        J["Foot." + s] = (ank, ank + V((0.17, 0.0, -0.07)))
    return J


def build_angler_parts(M, J):
    """-> {bone: [mesh objects in the char frame]}"""
    parts = {}

    def add(bone, ob):
        parts.setdefault(bone, []).append(ob)
        return ob

    # ------------------------------------------------ legs
    for s, sg in SIDES:
        hip, knee = J["Thigh." + s]
        ank = J["Shin." + s][1]
        add("Thigh." + s, R.ellipsoid("HipCap", hip, (0.08, 0.082, 0.08), M["trousers"], 12, 8))
        mid = (hip + knee) / 2 + V((0.012, 0, 0))
        add("Thigh." + s, R.loft("Thigh", [hip, mid, knee], [(0.084, 0.086), (0.078, 0.08), (0.062, 0.064)],
                                 M["trousers"], 12))
        kb = knee + V((-0.064, 0, 0.012))       # light crease behind the knee
        add("Thigh." + s, R.loft("KneeFold", [kb + V((0.01, -0.035, 0.008)), kb + V((0, 0, -0.004)),
                                              kb + V((0.01, 0.035, 0.008))], 0.0105, M["trousers_light"], 6))
        add("Shin." + s, R.ellipsoid("KneeCap", knee, (0.062, 0.064, 0.062), M["trousers"], 12, 8))
        shin_mid = (knee + ank) / 2 + V((-0.008, 0, 0))
        add("Shin." + s, R.loft("Shin", [knee, shin_mid, ank + V((0, 0, 0.05))],
                                [(0.062, 0.064), (0.056, 0.057), (0.058, 0.06)], M["trousers"], 12))
        add("Shin." + s, R.loft("Hem", [ank + V((0, 0, 0.1)), ank + V((0, 0, 0.055))],
                                [(0.064, 0.066), (0.066, 0.068)], M["trousers"], 12))
        add("Foot." + s, R.loft("Boot", [ank + V((-0.05, 0, 0.07)), ank + V((-0.02, 0, 0.0)),
                                         ank + V((0.08, 0, -0.045)), ank + V((0.16, 0, -0.05))],
                                [(0.056, 0.058), (0.06, 0.06), (0.05, 0.055), (0.03, 0.045)], M["boots"], 12))
        add("Foot." + s, bx("Heel", ank + V((-0.045, 0, -0.07)), (0.07, 0.1, 0.05), M["boots_dark"], bevel=0.006))
        add("Foot." + s, bx("Sole", ank + V((0.05, 0, -0.085)), (0.3, 0.1, 0.03), M["boots_dark"], bevel=0.008))
    # ------------------------------------------------ hips: seat, belt, seams
    add("Hips", R.loft("Pelvis", [(0, 0, 0.8), (0, 0, 0.9), (0, 0, 1.0)],
                       [(0.105, 0.155), (0.118, 0.172), (0.112, 0.16)], M["trousers"], 16))
    add("Hips", R.loft("Belt", [(0, 0, 0.975), (0, 0, 1.015)], [(0.118, 0.166), (0.118, 0.166)], M["strap"], 16))
    for s_ in (-1, 1):
        add("Hips", R.loft("SeatV", [(-0.112, s_ * 0.05, 0.925), (-0.117, s_ * 0.022, 0.885), (-0.116, 0.0, 0.855)],
                           0.011, M["trousers_dark"], 6))
    add("Hips", R.loft("Inseam", [(-0.105, 0.0, 0.86), (-0.08, 0.0, 0.8)], 0.012, M["trousers_dark"], 6))
    # ------------------------------------------------ torso: vest split at z 1.2 (Spine below, Chest above)
    torso = [(0.98, (0.11, 0.156)), (1.1, (0.118, 0.165)), (1.22, (0.126, 0.182)), (1.31, (0.118, 0.19)),
             (1.37, (0.092, 0.165)), (1.42, (0.06, 0.08))]
    vest = [(0.93, (0.126, 0.172)), (1.04, (0.13, 0.176)), (1.2, (0.138, 0.192)), (1.3, (0.13, 0.198)),
            (1.36, (0.1, 0.17)), (1.395, (0.07, 0.1))]
    # the shirt is only visible above the vest (shoulders / neckline): its body lives on the Chest
    sh_up = profile_cut(torso, 1.16, 1.42)
    add("Chest", R.loft("Shirt", [(0.0, 0, z) for z, _ in sh_up], [r for _, r in sh_up], M["shirt"], 16))
    v_lo = profile_cut(vest, 0.93, 1.26, inset_from=1.212, inset=0.004)
    v_up = profile_cut(vest, 1.2, 1.395)
    add("Spine", R.loft("VestLo", [(-0.004, 0, z) for z, _ in v_lo], [r for _, r in v_lo], M["vest"], 18))
    add("Chest", R.loft("VestUp", [(-0.004, 0, z) for z, _ in v_up], [r for _, r in v_up], M["vest"], 18))
    add("Chest", R.loft("Yoke", [(-0.004, 0, 1.232), (-0.004, 0, 1.256)], [(0.143, 0.198), (0.141, 0.198)],
                        M["vest_dark"], 18))
    add("Spine", R.loft("VHem", [(-0.004, 0, 0.925), (-0.004, 0, 0.972)], [(0.131, 0.177), (0.133, 0.179)],
                        M["vest_dark"], 18))
    add("Spine", bx("Pocket", (-0.134, 0.0, 1.06), (0.03, 0.24, 0.13), M["vest"], bevel=0.01, segs=2))
    add("Spine", bx("Flap", (-0.147, 0.0, 1.128), (0.02, 0.25, 0.034), M["vest_light"], bevel=0.006))
    add("Chest", R.loft("Collar", [(0.0, 0, 1.39), (0.0, 0, 1.44)], [(0.07, 0.078), (0.066, 0.072)], M["shirt"], 14))
    # ------------------------------------------------ creel on the strap (right shoulder -> behind the left hip)
    cre = V((-0.155, 0.13, 0.9))
    add("Spine", bx("Creel", cre, (0.09, 0.18, 0.135), M["bag"], bevel=0.012, segs=2))
    add("Spine", bx("Lid", cre + V((0, 0, 0.074)), (0.1, 0.19, 0.02), M["bag_lid"], bevel=0.006))
    add("Spine", bx("Latch", cre + V((-0.05, 0, 0.05)), (0.01, 0.025, 0.04), M["strap"]))
    sp = [V((-0.02, -0.17, 1.375)), V((-0.11, -0.12, 1.3)), V((-0.148, -0.03, 1.2)), V((-0.15, 0.06, 1.1)),
          V((-0.16, 0.12, 1.0))]
    up_pts = sp[:3] + [sp[2].lerp(sp[3], 0.15)]
    lo_pts = [sp[1].lerp(sp[2], 0.85)] + sp[2:]
    add("Chest", R.loft("StrapUp", up_pts, [(0.009, 0.024)] * len(up_pts), M["strap"], 8, up=(1, 0, 0)))
    add("Spine", R.loft("StrapLo", lo_pts, [(0.009, 0.024)] * len(lo_pts), M["strap"], 8, up=(1, 0, 0)))
    # ------------------------------------------------ neck, head, hat
    add("Neck", R.loft("Neck", [(0.01, 0, 1.4), (0.01, 0, 1.52)], 0.05, M["skin"], 10))
    add("Head", R.ellipsoid("Head", (0.012, 0, 1.585), (0.1, 0.086, 0.114), M["skin"], 14, 9))
    add("Head", R.ellipsoid("Hair", (-0.02, 0, 1.585), (0.098, 0.091, 0.118), M["hair"], 14, 9))
    for s_ in (-1, 1):
        add("Head", R.ellipsoid("Ear", (0.01, s_ * 0.088, 1.575), (0.022, 0.014, 0.032), M["skin"], 8, 6))
    add("Head", R.ellipsoid("Nose", (0.106, 0, 1.566), (0.02, 0.014, 0.022), M["skin"], 8, 6))
    hat_c = [(1.615, (0.104, 0.098)), (1.675, (0.099, 0.093)), (1.703, (0.09, 0.084)), (1.712, (0.06, 0.056)),
             (1.715, (0.005, 0.005))]
    add("Head", R.loft("Crown", [(0.0, 0, z) for z, _ in hat_c], [r for _, r in hat_c], M["hat"], 16))
    add("Head", R.loft("HatBand", [(0.0, 0, 1.63), (0.0, 0, 1.655)], [(0.108, 0.102), (0.106, 0.1)], M["hatband"], 16))
    add("Head", HC.brim("Brim", 1.628, (0.102, 0.096), 1.572, (0.188, 0.178), 0.014, M["hat"], segs=20))
    # ------------------------------------------------ shoulders + arms
    for s, sg in SIDES:
        s0, sh = J["Shoulder." + s]
        bd = sh - s0
        add("Shoulder." + s, R.ellipsoid("ShoulderPad", s0.lerp(sh, 0.6), (0.06, 0.075, 0.04), M["shirt"], 10, 6,
                                         rot=frame_rot(bd, (1, 0, 0))))
        sh, el = J["UpperArm." + s]
        wr = J["ForeArm." + s][1]
        fd = (wr - el).normalized()
        fore = (wr + fd * 0.042) - el                           # elbow -> fist centre (as hyb_character)
        add("UpperArm." + s, R.ellipsoid("ArmCap", sh, (0.057, 0.057, 0.057), M["shirt"], 12, 8))
        add("UpperArm." + s, R.loft("Upper", [sh, (sh + el) / 2, el], [0.056, 0.052, 0.047], M["shirt"], 12))
        cuff = el + (wr - el) * 0.4
        add("ForeArm." + s, R.ellipsoid("ElbowCap", el, (0.047, 0.047, 0.047), M["shirt"], 10, 6))
        add("ForeArm." + s, R.loft("Sleeve", [el, cuff], [0.047, 0.05], M["shirt"], 12))
        add("ForeArm." + s, R.loft("Cuff", [cuff - fore * 0.02, cuff + fore * 0.07], [0.052, 0.05], M["shirt"], 12))
        add("ForeArm." + s, R.loft("Fore", [cuff, wr], [0.038, 0.031], M["skin"], 10))
        rot = frame_rot(fd, (0, sg, 0))                         # hand frame: +Y along the hand, +X outwards
        add("Hand." + s, R.ellipsoid("WristCap", wr, (0.031, 0.031, 0.031), M["skin"], 8, 6))
        add("Hand." + s, R.ellipsoid("Fist", wr + fd * GRIP, (0.033, 0.047, 0.04), M["skin"], 12, 8, rot=rot))
        fwd = V((1, 0, 0))
        fwd = (fwd - fd * fwd.dot(fd)).normalized()
        add("Hand." + s, R.ellipsoid("Thumb", wr + fd * 0.03 + fwd * 0.033 - V((0, sg * 0.008, 0)),
                                     (0.013, 0.022, 0.013), M["skin"], 8, 6, rot=rot))
    return parts


def build_armature(J):
    ad = bpy.data.armatures.new("Angler")
    arm = bpy.data.objects.new("Angler", ad)
    C.link(arm)
    arm.matrix_world = F2W
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    to_f = W2F @ C2W
    back_f = (to_f.to_3x3() @ V((-1, 0, 0))).normalized()     # the character's back
    up_f = (to_f.to_3x3() @ V((0, 0, 1))).normalized()
    for name, par in BONES:
        h, t = J[name]
        eb = ad.edit_bones.new(name)
        eb.head = to_f @ h
        eb.tail = to_f @ t
        eb.align_roll(up_f if name.startswith("Foot") else back_f)
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
    """Rigid bone parent; `local` = transform relative to the bone HEAD frame (identity by default)."""
    b = arm.data.bones[name]
    ob.parent = arm
    ob.parent_type = "BONE"
    ob.parent_bone = name
    ob.matrix_parent_inverse = Matrix()
    # Blender parents to the bone TAIL: step back along the bone's +Y to its head
    ob.matrix_basis = Matrix.Translation((0, -b.length, 0)) @ (local or Matrix())


def build_angler():
    C.reset_scene()
    R.reset_materials()
    ramps = angler_ramps()
    M = {k: export_material(k, v) for k, v in ramps.items()}
    J = joints()
    arm = build_armature(J)
    parts = build_angler_parts(M, J)
    geo = {}
    tris = 0
    for name, _ in BONES:
        objs = parts.get(name)
        if not objs:
            continue
        for ob in objs:
            bake(ob, C2W)                           # char frame -> Blender world
        ob = join(objs, "geo_" + name)
        me = ob.data
        uv = me.uv_layers.new(name="rest")          # rest-pose position in Unity metres: (x, y)
        for lp in me.loops:
            co = me.vertices[lp.vertex_index].co
            uv.data[lp.index].uv = (-co.x, co.z)
        Mb = bone_world(arm, name)
        me.transform(Mb.inverted())                 # vertices in the bone-head frame
        parent_to_bone(ob, arm, name)
        geo[name] = tri_count(ob)
        tris += geo[name]
    grips = {}
    for s, _ in SIDES:
        e = bpy.data.objects.new("HandGrip." + s, None)
        e.empty_display_type = "SPHERE"
        e.empty_display_size = 0.02
        C.link(e)
        parent_to_bone(e, arm, "Hand." + s, Matrix.Translation((0, GRIP, 0)))
        grips[s] = e
    bpy.context.view_layer.update()
    # ------------------------------------------------ report numbers (Unity space)
    lowest = min((ob.matrix_world @ v.co).z for ob in bpy.data.objects if ob.type == "MESH" for v in ob.data.vertices)
    data = {"bones": {}, "lengths": {}, "grips": {}, "trisPerBone": geo, "tris": tris, "lowestZ": lowest}
    for name, par in BONES:
        b = arm.data.bones[name]
        data["bones"][name] = {"parent": par, "head": r3(u_of_f(b.head_local)), "tail": r3(u_of_f(b.tail_local)),
                               "length": round(b.length, 4)}
    for s, _ in SIDES:
        data["lengths"]["arm." + s] = {
            "shoulderToElbow": round(arm.data.bones["UpperArm." + s].length, 4),
            "elbowToWrist": round(arm.data.bones["ForeArm." + s].length, 4),
            "wristToGrip": GRIP, "wristToKnuckles": round(arm.data.bones["Hand." + s].length, 4)}
        data["lengths"]["leg." + s] = {
            "hipToKnee": round(arm.data.bones["Thigh." + s].length, 4),
            "kneeToAnkle": round(arm.data.bones["Shin." + s].length, 4),
            "ankleToToe": round(arm.data.bones["Foot." + s].length, 4)}
        gw = W2F @ grips[s].matrix_world.translation
        data["grips"]["HandGrip." + s] = r3(u_of_f(gw))
    print("ACT angler tris", tris, "per bone", geo, "lowest z", round(lowest, 4))
    os.makedirs(OUT, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0     # no angler.blend1 backups
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "angler.blend"))
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "angler.fbx"), **FBX_OPTS)
    pal = palette_json(ramps)
    with open(os.path.join(OUT, "angler_palette.json"), "w", encoding="utf-8") as f:
        json.dump(pal, f, indent=1)
    return data


# ---------------------------------------------------------------- reels (fk_items.REELS / build_world_reel)
REEL_H = {"reel_basic": 0.11, "reel_light": 0.10, "reel_highgear": 0.12, "reel_baitcast": 0.10,
          "reel_electric": 0.12, "reel_poseidon": 0.13}   # overall height (m), foot top to the lowest point
WIRE = "#d0d4dc"
STAR = "#d8b040"
BAIT_LINE = "#e8f0a0"


def shade_dir(h, dl, deg, chroma):
    """OKLab lightness step with a SIGNED hue rotation (deg) and chroma factor."""
    L, a, b = R.oklab(R.hexrgb(h))
    ang = math.atan2(b, a) + math.radians(deg)
    ch = math.hypot(a, b) * chroma
    return R.tohex(R.oklab_inv(np.array([min(0.99, max(0.02, L + dl)), ch * math.cos(ang), ch * math.sin(ang)])))


def warm(h):
    """yellow / gold / olive hues (OKLab 40..140 deg): their shadows turn towards orange, never towards green."""
    L, a, b = R.oklab(R.hexrgb(h))
    return math.hypot(a, b) > 0.03 and 40.0 <= math.degrees(math.atan2(b, a)) % 360 <= 140.0


def reel_ramp(h):
    """[dark, mid, light] around the REELS colour with a gentle hue shift (metal / plastic colours keep their hue);
    near-black bases stay the darkest tone and get two lighter steps."""
    if R.lum(h) < 0.3:
        return [h, R.shade(h, 0.08, hue=5.0), R.shade(h, 0.16, hue=8.0)]
    dark = shade_dir(h, -0.12, -12.0, 1.05) if warm(h) else R.shade(h, -0.12, hue=9.0)
    return [dark, h, R.shade(h, 0.09, hue=6.0)]


def reel_roles(p):
    roles = {"body": p["body"], "knob": p["knob"]}
    if p["kind"] == "spin":
        roles.update(spool=p["spool"], line=p["line"], wire=WIRE)
        if p.get("gem"):
            roles["gem_glow"] = p["gem"]
    elif p["kind"] == "bait":
        roles.update(plate=p["plate"], line=BAIT_LINE, star=STAR)
    else:
        roles.update(plate=p["plate"], lcd_glow=p["lcd"])
    return roles


def tube(name, pts, r, mat, segs=12):
    return C.tube_along(name, [tuple(q) for q in pts], r, mat, segs)


def reel_model(p, M):
    """Model frame (fk_items): x = towards the rod tip, y = the reel's LEFT, z up; the crank is on -y (the
    angler's right) with the knob straight UP (crank angle 0). -> (body objs, crank objs, axis, knob, spool, foot)."""
    body, crank = [], []
    hs = -1.0
    if p["kind"] == "spin":
        b = p.get("big", 1.0)
        ft = 1.3 * b
        body.append(bx("Foot", (0.1 * b, 0, ft - 0.07 * b), (0.9 * b, 0.34 * b, 0.14 * b), M["body"], 0.03 * b, 2))
        body.append(bx("Stem", (-0.08 * b, 0, 0.75 * b), (0.24 * b, 0.22 * b, 1.0 * b), M["body"], 0.05 * b, 2,
                       rot=Matrix.Rotation(math.radians(-12), 4, "Y")))
        body.append(R.ellipsoid("Gear", (-0.25 * b, 0, 0.1 * b), (0.52 * b, 0.44 * b, 0.52 * b), M["body"], 14, 9))
        body.append(tube("Rotor", [(0.1 * b, 0, 0.1 * b), (0.5 * b, 0, 0.1 * b)], [0.4 * b, 0.45 * b], M["body"], 16))
        body.append(tube("Spool", [(0.5 * b, 0, 0.1 * b), (1.0 * b, 0, 0.1 * b)], 0.33 * b, M["spool"], 16))
        body.append(tube("LineW", [(0.56 * b, 0, 0.1 * b), (0.88 * b, 0, 0.1 * b)], 0.37 * b, M["line"], 16))
        body.append(tube("Lip", [(0.88 * b, 0, 0.1 * b), (1.02 * b, 0, 0.1 * b)], 0.42 * b, M["spool"], 16))
        if p.get("gem"):
            body.append(R.ellipsoid("Gem", (-0.25 * b, 0.43 * b, 0.1 * b), (0.15 * b, 0.07 * b, 0.15 * b),
                                    M["gem_glow"], 10, 6))
        axis = V((-0.25 * b, hs * 0.42 * b, 0.1 * b))
        L, ky = 0.72 * b, 1.0 * b
        end = V((axis.x, hs * (ky - 0.1 * b), axis.z + L))
        crank.append(tube("Hub", [(axis.x, hs * 0.38 * b, axis.z), (axis.x, hs * 0.52 * b, axis.z)], 0.12 * b,
                          M["wire"], 12))
        crank.append(tube("Arm", [axis, V((axis.x, axis.y + hs * 0.1 * b, axis.z + L * 0.5)), end], 0.08 * b,
                          M["wire"], 8))
        knob = V((axis.x, hs * ky, axis.z + L))
        crank.append(R.ellipsoid("KnobM", knob, (0.2 * b, 0.22 * b, 0.2 * b), M["knob"], 12, 8))
        return body, crank, axis, knob, V((1.02 * b, 0, 0.52 * b)), V((0.1 * b, 0, ft))
    if p["kind"] == "bait":
        ft = 0.62
        body.append(R.ellipsoid("BodyM", (0, 0, 0), (0.96, 0.576, 0.576), M["body"], 16, 10))
        for sy in (-1, 1):
            body.append(tube("Side", [(0, sy * 0.44, 0), (0, sy * 0.58, 0)], 0.52, M["plate"], 20))
        body.append(tube("Level", [(0.7, -0.3, 0.3), (0.7, 0.3, 0.3)], 0.12, M["line"], 10))
        body.append(bx("Foot", (0, 0, ft - 0.07), (0.9, 0.34, 0.14), M["body"], 0.03, 2))
        body.append(tube("Star", [(0, hs * 0.58, 0), (0, hs * 0.72, 0)], 0.19, M["star"], 8))
        axis = V((0, hs * 0.8, 0))
        crank.append(tube("Hub", [(0, hs * 0.7, 0), (0, hs * 0.84, 0)], 0.1, M["plate"], 10))
        e1, e2 = V((0, hs * 0.82, 0.42)), V((0, hs * 0.82, -0.42))
        crank.append(tube("Arm", [e2, e1], 0.08, M["plate"], 8))
        for e in (e1, e2):
            crank.append(R.ellipsoid("KnobM", (0, hs * 0.9, e.z), (0.19, 0.21, 0.19), M["knob"], 12, 8))
        return body, crank, axis, V((0, hs * 0.9, 0.42)), V((0.75, 0, 0.42)), V((0, 0, ft))
    # electric
    ft = 0.62
    body.append(bx("BodyM", (0, 0, 0), (1.5, 0.95, 1.0), M["body"], 0.2, 3))
    body.append(bx("Face", (0.1, hs * 0.48, 0.08), (1.0, 0.1, 0.55), M["plate"], 0.05, 2))
    body.append(bx("LCD", (0.1, hs * 0.53, 0.1), (0.7, 0.1, 0.3), M["lcd_glow"]))
    body.append(bx("Foot", (0, 0, ft - 0.07), (0.9, 0.34, 0.14), M["body"], 0.03, 2))
    axis = V((-0.45, hs * 0.48, -0.05))
    L, ky = 0.44, 0.72                  # shorter than the sprite's 0.58: the knob stays below the foot top
    crank.append(tube("Hub", [(axis.x, hs * 0.44, axis.z), (axis.x, hs * 0.58, axis.z)], 0.11, M["plate"], 10))
    crank.append(tube("Arm", [axis, V((axis.x, axis.y + hs * 0.1, axis.z + L * 0.5)),
                              V((axis.x, hs * (ky - 0.1), axis.z + L))], 0.09, M["plate"], 8))
    knob = V((axis.x, hs * ky, axis.z + L))
    crank.append(R.ellipsoid("KnobM", knob, (0.21, 0.23, 0.21), M["knob"], 12, 8))
    return body, crank, axis, knob, V((0.75, 0, 0.35)), V((0, 0, ft))


P_MF = Matrix(((0, 1, 0), (0, 0, 1), (1, 0, 0)))   # model (x tip, y left, z up) -> F (x left, y up, z tip)


def build_reel(rid, p):
    C.reset_scene()
    R.reset_materials()
    roles = reel_roles(p)
    ramps = {f"{rid}_{k}": reel_ramp(v) for k, v in roles.items()}
    M = {k: export_material(f"{rid}_{k}", ramps[f"{rid}_{k}"]) for k in roles}
    body, crank, axis, knob, spool, foot = reel_model(p, M)
    bpy.context.view_layer.update()
    for ob in body + crank:
        bake(ob)
    zs = [v.co.z for ob in body + crank for v in ob.data.vertices]
    S = REEL_H[rid] / (foot.z - min(zs))
    MF = (Matrix.Scale(S, 4) @ P_MF.to_4x4() @ Matrix.Translation(-foot))

    def f(pt):
        return MF @ V(pt)
    root = bpy.data.objects.new("Reel", None)
    root.empty_display_type = "ARROWS"
    root.empty_display_size = 0.03
    C.link(root)
    root.matrix_world = F2W
    bo = join(body, "Body")
    bo.data.transform(MF)
    bo.parent = root
    bo.matrix_parent_inverse = Matrix()
    bo.matrix_basis = Matrix()
    ax = f(axis)
    co = join(crank, "Crank")
    co.data.transform(Matrix.Translation(-ax) @ MF)
    co.parent = root
    co.matrix_parent_inverse = Matrix()
    co.matrix_basis = Matrix.Translation(ax)
    ke = bpy.data.objects.new("Knob", None)
    ke.empty_display_type = "SPHERE"
    ke.empty_display_size = 0.006
    C.link(ke)
    ke.parent = co
    ke.matrix_parent_inverse = Matrix()
    ke.matrix_basis = Matrix.Translation(f(knob) - ax)
    se = bpy.data.objects.new("Spool", None)
    se.empty_display_type = "SPHERE"
    se.empty_display_size = 0.006
    C.link(se)
    se.parent = root
    se.matrix_parent_inverse = Matrix()
    se.matrix_basis = Matrix.Translation(f(spool))
    bpy.context.view_layer.update()
    tris = tri_count(bo) + tri_count(co)
    ys = [(W2F @ (o.matrix_world @ v.co)).y for o in (bo, co) for v in o.data.vertices]
    xs = [(W2F @ (o.matrix_world @ v.co)).x for o in (bo, co) for v in o.data.vertices]
    zs = [(W2F @ (o.matrix_world @ v.co)).z for o in (bo, co) for v in o.data.vertices]
    kd = f(knob) - ax
    info = {"crank": r3(u_of_f(ax)), "knob": r3(u_of_f(f(knob))), "spool": r3(u_of_f(f(spool))),
            "crankRadius": round(math.hypot(kd.y, kd.z), 4),
            "bboxMin": r3(u_of_f(V((max(xs), min(ys), min(zs))))), "bboxMax": r3(u_of_f(V((min(xs), max(ys), max(zs))))),
            "height": round(max(ys) - min(ys), 4), "tris": tris, "scale": round(S, 5)}
    print("ACT reel", rid, info)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, f"{rid}.fbx"), **FBX_OPTS)
    return info, palette_json(ramps, dl=-0.12, hue=10.0)


def main():
    os.makedirs(OUT, exist_ok=True)
    R.use_preset("lake")
    data = {"frame": "Unity space: x right (character's right), y up, z forward (the character faces +z)",
            "angler": build_angler(), "reels": {}}
    rpal = {}
    for rid, p in I.REELS.items():
        info, pal = build_reel(rid, p)
        data["reels"][rid] = info
        rpal.update(pal)
    with open(os.path.join(OUT, "reel_palette.json"), "w", encoding="utf-8") as f:
        json.dump(rpal, f, indent=1)
    kl = V(HC.CL)       # key light of the sprite (Blender stage frame x right, y forward, z up) -> Unity
    data["spriteKeyLight"] = {"towardsLightUnity": r3(V((kl.x, kl.z, kl.y)).normalized()),
                              "bands": [0.45, 0.74], "wrap": "f = 0.5 + 0.5 * dot(N, L)"}
    with open(os.path.join(OUT, "actors3d_data.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    print("ACT done ->", OUT)


if __name__ == "__main__":
    main()
