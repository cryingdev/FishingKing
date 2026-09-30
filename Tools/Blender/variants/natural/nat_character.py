"""
FishingKing - "natural" variant angler: a ~1.76 m adult seen from behind (7.5 heads tall), muted
outdoor clothing (slate shirt, khaki fishing vest with darker side panels, a lighter mesh yoke, back
pocket with flap and a collar D-ring, olive work trousers, leather boots, sand bucket hat), rendered
through the shared stage camera.

Colour discipline: 6 ramp families (skin 4, shirt 5, vest 5, trousers 5, leather 4, hat 4 = 27 colours)
+ 1 shadow colour. Every pixel is snapped to its own family's ramp; outlines and inner lines are ramp
steps of the neighbouring family (never black), so a sprite uses <= 28 colours.

Writes (only) into Tools/Blender/_tmp/variants/natural/:
  character/angler_<pose>.png   96x112, feet 10 px above the bottom (same crop as fk_character)
  character.json                same schema as Data/character.json
  character_sheet.png           poses side by side, 4x on mid-grey
  _work/char_silhouettes.png    solid-black 1x/3x silhouettes (readability check)

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

# ramp families (base = lit colour); step 0 is clamped to OKLab L >= 0.25 so outlines never go black
FAM = ["skin", "shirt", "vest", "trousers", "leather", "hat"]
RAMPS = dict(
    skin=N.gen_ramp("#c39174", 4, spread=0.8, warm=1.5, minL=0.25),
    shirt=N.gen_ramp("#636b73", 5, cool=0.4, minL=0.25),
    vest=N.gen_ramp("#978b63", 5, minL=0.25),
    trousers=N.gen_ramp("#55533f", 5, spread=0.9, minL=0.25),
    leather=N.gen_ramp("#5e4632", 4, spread=0.85, minL=0.25),
    hat=N.gen_ramp("#b0a07a", 4, spread=0.85),
)
GID = {f: i for i, f in enumerate(FAM)}
SHADOW_COL = "#141b26"

# Hand targets in the body frame: (forward, side[-=right/near], up), lean deg (+ = forward),
# stance = (foot spread, near-foot forward, far-foot forward, knee bend), rod = Unity dir (x right, y up, z fwd)
POSES = {
    "idle": dict(near=(0.3, -0.25, 1.03), far=(0.42, -0.02, 1.14), lean=3, stance=(0.16, -0.04, 0.07, 0.01),
                 rod=(0.22, 0.72, 0.66)),
    "aim": dict(near=(0.0, -0.3, 1.62), far=(0.35, -0.05, 1.25), lean=-5, stance=(0.17, -0.12, 0.13, 0.03),
                rod=(0.25, 0.72, -0.64), pole_up=True),
    "cast": dict(near=(0.55, -0.28, 1.5), far=(0.5, -0.18, 1.42), lean=12, stance=(0.17, 0.14, -0.08, 0.04),
                 rod=(0.08, 0.3, 0.95)),
    "reel": dict(near=(0.3, -0.24, 1.02), far=(0.44, 0.02, 1.12), lean=0, stance=(0.16, -0.04, 0.07, 0.02),
                 rod=(0.2, 0.62, 0.76)),
    "reel2": dict(near=(0.28, -0.24, 1.13), far=(0.44, 0.02, 1.12), lean=0, stance=(0.16, -0.04, 0.07, 0.02),
                  rod=(0.2, 0.62, 0.76)),
    "fight": dict(near=(0.3, -0.3, 1.15), far=(0.5, -0.3, 1.5), lean=-15, stance=(0.22, -0.14, 0.16, 0.1),
                  rod=(0.12, 0.9, 0.42)),
    "cheer": dict(near=(0.16, -0.44, 1.8), far=(0.14, 0.4, 1.78), lean=0, stance=(0.17, -0.03, 0.05, 0.0),
                  rod=(0.45, 0.75, 0.3), pole_up=True),
}

HIP_Z, KNEE_Z = 0.86, 0.48
SH_Z, SH_Y = 1.40, 0.2
UPPER, FORE = 0.29, 0.265
THIGH, SHIN = 0.40, 0.40


# ============================================================================ geometry helpers
def loft(name, sections, mat, segs=16, hem=0.0):
    """sections: list of (x, z, rx, ry) ellipses stacked along z (rx = forward depth, ry = width).
    hem > 0 drops the FIRST ring at the sides by hem * sin^2 (curved garment hem)."""
    bm = bmesh.new()
    rings = []
    for i, (x, z, rx, ry) in enumerate(sections):
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            dz = -hem * math.sin(a) ** 2 if i == 0 else 0.0
            ring.append(bm.verts.new((x + rx * math.cos(a), ry * math.sin(a), z + dz)))
        rings.append(ring)
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
    return C.tube_along(name, pts, radii, mat, segs)


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


# ============================================================================ materials
def fam_mat(fam, name, bias=0.0, tex=0.0, tex_scale=14.0, ao=0.5, ao_dist=0.12, panel=None):
    """Light value (sun + sky + AO) -> the family's constant ramp, shifted by `bias` (+ = lighter).
    panel = dict(y=..., z0=..., z1=..., side=-0.28, yoke=+0.24): object-space extra bias for the vest
    (darker side panels |y| > y, lighter yoke band z0 < z < z1 in the middle)."""
    cols = RAMPS[fam]
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    L = N._light_value(nb, ao, ao_dist)
    if tex:
        nz = N._noise(nb, tex_scale, "object")
        L = nb.math("MULTIPLY_ADD", nz, tex * 2.0, nb.math("SUBTRACT", L, tex))
    if bias:
        L = nb.math("ADD", L, bias)
    if panel:
        tc = nb.node("ShaderNodeTexCoord")
        o = nb.sep(tc.outputs["Object"])
        side = nb.math("GREATER_THAN", nb.math("ABSOLUTE", o[1]), panel["y"])
        L = nb.math("MULTIPLY_ADD", side, panel["side"], L)
        yk = nb.math("MULTIPLY", nb.in_range(o[2], panel["z0"], panel["z1"]), nb.math("SUBTRACT", 1.0, side))
        L = nb.math("MULTIPLY_ADD", yk, panel["yoke"], L)
    col = N.ramp_colours(nb, L, cols)
    nb.output_emission(col, 1.0)
    return m


def mats():
    return dict(
        skin=fam_mat("skin", "Skin"),
        hair=fam_mat("leather", "Hair", bias=-0.25),
        shirt=fam_mat("shirt", "Shirt", tex=0.04),
        vest=fam_mat("vest", "Vest", tex=0.03, tex_scale=18.0,
                     panel=dict(y=0.118, side=-0.3, z0=1.3, z1=1.375, yoke=0.26)),
        pocket=fam_mat("vest", "Pocket", bias=-0.12),
        flap=fam_mat("vest", "Flap", bias=-1.0, ao=0.0),
        dring=fam_mat("hat", "DRing", bias=0.6, ao=0.0),
        trousers=fam_mat("trousers", "Trousers", tex=0.04, tex_scale=12.0),
        boots=fam_mat("leather", "Boots"),
        sole=fam_mat("leather", "Sole", bias=-0.6, ao=0.0),
        belt=fam_mat("leather", "Belt", bias=-0.15),
        hat=fam_mat("hat", "Hat", tex=0.03, tex_scale=20.0),
        band=fam_mat("hat", "Band", bias=-0.38),
    )


# part id (inner lines) and ramp family per material key
FAMILY = dict(skin="skin", hair="leather", shirt="shirt", vest="vest", pocket="vest", flap="vest", dring="hat",
              trousers="trousers", boots="leather", sole="leather", belt="leather", hat="hat", band="hat")


# ============================================================================ the model
def build(pose):
    p = POSES[pose]
    M = mats()
    parts = []          # (object, part id, family)

    def add(ob, pid, key):
        parts.append((ob, pid, FAMILY[key]))
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
                 [0.085, 0.082, 0.07, 0.058], M["trousers"], 12), 2, "trousers")
        add(tube("Shin", [knee, (knee + ank) / 2, ank + Vector((0, 0, 0.03))], [0.058, 0.056, 0.054],
                 M["trousers"], 12), 2, "trousers")
        bx = ank.x
        feet.append(Vector((bx + 0.05, ank.y, 0.0)))
        add(tube("Boot", [(bx - 0.07, ank.y, 0.2), (bx - 0.05, ank.y, 0.07), (bx + 0.06, ank.y, 0.055),
                          (bx + 0.17, ank.y, 0.045)], [0.056, 0.059, 0.053, 0.036], M["boots"], 12), 1, "boots")
        add(C.tube_along("Sole", [(bx - 0.09, ank.y, 0.014), (bx + 0.18, ank.y, 0.014)], 0.046, M["sole"], 8), 1,
            "sole")
    # ---------------------------------------------------------------- torso (lean around the hips)
    up = []
    up.append(add(loft("Pelvis", [(0.0, 0.78, 0.105, 0.155), (0.0, 0.88, 0.115, 0.17), (0.0, 0.97, 0.105, 0.158)],
                       M["trousers"]), 2, "trousers"))
    up.append(add(loft("Belt", [(0.0, 0.925, 0.112, 0.163), (0.0, 0.965, 0.11, 0.16)], M["belt"]), 3, "belt"))
    up.append(add(loft("Shirt", [(0.0, 0.92, 0.1, 0.152), (0.0, 1.02, 0.098, 0.148), (0.005, 1.18, 0.108, 0.172),
                                 (0.0, 1.32, 0.104, 0.196), (0.0, 1.4, 0.092, 0.2), (0.0, 1.45, 0.07, 0.13),
                                 (0.01, 1.49, 0.058, 0.07)], M["shirt"]), 3, "shirt"))
    # fishing vest: curved hem (2px lower at the sides), darker side panels + lighter yoke via the material
    # (the vest covers the shoulder blades up to the shoulder points, so it reads as a garment, not a pack)
    up.append(add(loft("Vest", [(0.0, 1.0, 0.109, 0.159), (0.0, 1.06, 0.107, 0.158), (0.005, 1.18, 0.115, 0.179),
                                (0.0, 1.31, 0.112, 0.2), (0.0, 1.395, 0.101, 0.204), (0.0, 1.455, 0.079, 0.134)],
                      M["vest"], hem=0.045), 3, "vest"))
    # back pocket with a 1px flap line (the back faces -X)
    pk = add(C.poly_object("Pocket", [(-0.1, 0.0), (0.1, 0.0), (0.097, 0.15), (-0.097, 0.15)], M["pocket"], 0.02),
             3, "pocket")
    pk.matrix_world = Matrix.Translation((-0.119, 0.0, 1.05)) @ Matrix.Rotation(math.radians(90), 4, "Z")
    up.append(pk)
    fl = add(C.poly_object("Flap", [(-0.102, 0.0), (0.102, 0.0), (0.102, 0.022), (-0.102, 0.022)], M["flap"], 0.02),
             3, "flap")
    fl.matrix_world = Matrix.Translation((-0.124, 0.0, 1.165)) @ Matrix.Rotation(math.radians(90), 4, "Z")
    up.append(fl)
    # collar + D-ring + neck + head
    up.append(add(tube("Collar", [(0.0, 0, 1.44), (0.005, 0, 1.49)], [0.075, 0.068], M["shirt"], 12), 3, "shirt"))
    up.append(add(ellip("DRing", (-0.083, 0.0, 1.445), 1.0, M["dring"], (0.012, 0.02, 0.02), 8, 6), 3, "dring"))
    up.append(add(tube("Neck", [(0.01, 0, 1.45), (0.015, 0, 1.56)], [0.052, 0.05], M["skin"], 10), 6, "skin"))
    hc = Vector((0.02, 0.0, 1.615))
    up.append(add(ellip("Head", hc, 1.0, M["skin"], (0.1, 0.078, 0.112)), 6, "skin"))
    up.append(add(ellip("Hair", hc + Vector((-0.012, 0, -0.004)), 1.0, M["hair"], (0.098, 0.082, 0.105)), 6, "hair"))
    for s in (-1, 1):
        up.append(add(ellip("Ear", hc + Vector((0.005, s * 0.078, -0.01)), 1.0, M["skin"], (0.02, 0.012, 0.03), 8, 6),
                      6, "skin"))
    # bucket hat: lower crown, wider brim with the outer ring dropped 3 cm (no pith-helmet read)
    up.append(add(loft("Crown", [(0.02, 1.665, 0.103, 0.091), (0.02, 1.712, 0.097, 0.085), (0.02, 1.738, 0.09, 0.078),
                                 (0.02, 1.744, 0.068, 0.058), (0.02, 1.745, 0.02, 0.02)], M["hat"]), 7, "hat"))
    up.append(add(loft("Band", [(0.02, 1.668, 0.105, 0.093), (0.02, 1.686, 0.103, 0.091)], M["band"]), 7, "band"))
    up.append(add(loft("Brim", [(0.02, 1.598, 0.175, 0.165), (0.02, 1.645, 0.125, 0.113), (0.02, 1.668, 0.103, 0.091)],
                       M["hat"]), 7, "hat"))

    lean = math.radians(p["lean"])
    pivot = Vector((0, 0, HIP_Z - hip_drop))
    R = Matrix.Translation(pivot) @ Matrix.Rotation(lean, 4, "Y") @ Matrix.Translation(-pivot)
    for o in up:
        o.matrix_world = R @ Matrix.Translation((0, 0, -hip_drop)) @ o.matrix_world
    # ---------------------------------------------------------------- arms (shoulders follow the lean)
    hands = {}
    for side, key in ((1, "far"), (-1, "near")):
        sh = R @ Vector((0.0, side * SH_Y, SH_Z - hip_drop))
        tgt = Vector(p[key])
        if p.get("pole_up") and tgt.z > 1.45:
            pole = (0.2, side * 1.0, 0.35)
        else:
            pole = (-0.55, side * 0.75, -0.45)
        elbow, hand = ik(sh, tgt, UPPER, FORE, pole)
        pid = 4 if side > 0 else 5
        add(tube("Deltoid", [sh + (sh - elbow).normalized() * 0.02, sh, sh.lerp(elbow, 0.5)], [0.05, 0.066, 0.056],
                 M["shirt"], 12), pid, "shirt")
        add(tube("Upper", [sh, sh.lerp(elbow, 0.5), elbow], [0.062, 0.055, 0.049], M["shirt"], 12), pid, "shirt")
        wrist = elbow.lerp(hand, 0.93)
        add(tube("Fore", [elbow, elbow.lerp(hand, 0.5), wrist], [0.049, 0.045, 0.041], M["shirt"], 12), pid, "shirt")
        fdir = (hand - elbow).normalized()
        hc2 = hand + fdir * 0.03
        add(ellip("Hand", hc2, 1.0, M["skin"], (0.05, 0.034, 0.042), 10, 8), pid, "skin")
        hands[key] = hc2
    turn = Matrix.Rotation(math.radians(TURN), 4, "Z")
    for ob, _, _ in parts:
        ob.matrix_world = turn @ ob.matrix_world
    for ob, pid, fam in parts:
        ob.pass_index = pid * 8 + GID[fam]
    return parts, turn @ hands["near"], [turn @ f for f in feet]


# ============================================================================ passes
def id_material():
    m = bpy.data.materials.new("IDPass")
    nb = C.NB(m)
    oi = nb.node("ShaderNodeObjectInfo")
    cam = nb.node("ShaderNodeCameraData")
    r = nb.math("DIVIDE", oi.outputs["Object Index"], 64.0)
    g = nb.math("DIVIDE", nb.math("SUBTRACT", cam.outputs["View Distance"], 6.0), 12.0)
    nb.output_emission(nb.combine(r, g, 0.0), 1.0)
    return m


def ramp_lab():
    return {f: np.array([N.srgb2lab(N.hex2srgb(c)) for c in RAMPS[f]]) for f in FAM}


def snap_to_family(arr, fam):
    """Every opaque pixel -> nearest colour of its own family's ramp. Returns (arr, step index)."""
    out = arr.copy()
    idx = np.zeros(arr.shape[:2], int)
    op = arr[..., 3] > 0.5
    lab = N.arr_lab(arr[..., :3])
    RL = ramp_lab()
    for g, f in enumerate(FAM):
        m = op & (fam == g)
        if not m.any():
            continue
        d = ((lab[m][:, None, :] - RL[f][None]) ** 2).sum(-1)
        k = d.argmin(1)
        idx[m] = k
        out[m, :3] = np.array([N.hex2srgb(c) for c in RAMPS[f]])[k]
    return out, idx


def fam_rgb(g, k):
    f = FAM[g]
    k = int(np.clip(k, 0, len(RAMPS[f]) - 1))
    return N.hex2srgb(RAMPS[f][k])


def inner_lines(arr, idx, fam, ids, depth, thr=0.035):
    """At every part boundary, the pixel BEHIND drops one step of its own ramp (depth-aware inner line)."""
    out = arr.copy()
    op = arr[..., 3] > 0.5
    mask = np.zeros(op.shape, bool)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        oid = C.shift(ids, dy, dx)
        od = C.shift(depth, dy, dx)
        oop = C.shift(op, dy, dx)
        mask |= op & oop & (oid != ids) & (depth > od + thr)
    for y, x in zip(*np.where(mask)):
        out[y, x, :3] = fam_rgb(fam[y, x], idx[y, x] - 1)
    return out


def outline_selective(arr, fam):
    """1px outside outline = ramp step 0 of the neighbouring family on the shadow sides (right/bottom),
    step 1 on the lit sides (left/top: the sun comes from the left)."""
    out = arr.copy()
    op = arr[..., 3] > 0.5
    done = op.copy()
    # arrays are bottom-up: shift(a, dy, dx)[y, x] = a[y - dy, x - dx]
    # order: shadow-side neighbours first (sprite pixel to the left, sprite pixel above), then lit sides
    for (dy, dx), step in (((0, 1), 0), ((-1, 0), 0), ((0, -1), 1), ((1, 0), 1)):
        nop = C.shift(op, dy, dx)
        nf = C.shift(fam, dy, dx)
        e = nop & ~done
        for y, x in zip(*np.where(e)):
            out[y, x, :3] = fam_rgb(nf[y, x], step)
            out[y, x, 3] = 1.0
        done |= e
    return out


def ground_ray(px, py):
    """Pixel offset from the image centre (x right, y up) -> world point on z = 0 (camera for standH 0)."""
    s, c = math.sin(math.radians(P.PITCH)), math.cos(math.radians(P.PITCH))
    cam = np.array(P.cam_pos(0.0))
    right, upv, fwd = np.array([1.0, 0, 0]), np.array([0, s, c]), np.array([0, c, -s])
    d = right[None, None] * (px / P.F_PX)[..., None] + upv[None, None] * (py / P.F_PX)[..., None] + fwd[None, None]
    t = -cam[2] / np.minimum(d[..., 2], -1e-6)
    return cam[None, None] + d * t[..., None]


def shadow_catcher():
    m = bpy.data.materials.new("ShadowCatch")
    nb = C.NB(m)
    L = N._light_value(nb, ao=0.0)
    nb.output_emission(nb.combine(L, L, L), 1.0)
    ob = C.add_prim("cyl", "Catcher", m, radius=2.0, depth=0.004, location=(0.0, 0.2, -0.003), vertices=48)
    ob.visible_shadow = False
    return ob


def add_ground_shadow(sc, parts, feet, arr, x0, y0):
    """Sun-cast shadow on the ground (catcher r = 2 m), 2 alpha tones fading with the distance to the
    nearest boot (0.45 core / 0.26 up to 0.85 m, nothing beyond) + a contact shadow around the boots."""
    cat = shadow_catcher()
    for ob, _, _ in parts:
        ob.visible_camera = False
    bpy.context.view_layer.update()
    raw = N.render_ss("char_sh_ss.png", SS, P.W, P.H)
    raw = raw[y0 * SS:(y0 + CROP_H) * SS, x0 * SS:(x0 + CROP_W) * SS]
    sh, _ = N.mode_down(raw, SS, cover=0.5)
    for ob, _, _ in parts:
        ob.visible_camera = True
    bpy.data.objects.remove(cat, do_unlink=True)
    yy, xx = np.mgrid[0:CROP_H, 0:CROP_W].astype(np.float64)
    px = x0 + xx + 0.5 - P.W / 2
    py = y0 + yy + 0.5 - P.H / 2
    g = ground_ray(px, py)
    d = np.min([np.hypot(g[..., 0] - f.x, g[..., 1] - f.y) for f in feet], axis=0)
    shade = (sh[..., 3] > 0.5) & (sh[..., 0] < 0.78)
    contact = d < 0.13
    empty = arr[..., 3] < 0.5
    out = arr.copy()
    core = empty & ((shade & (d < 0.42)) | contact)
    rim = empty & shade & (d >= 0.42) & (d < 0.85)
    out[core | rim, :3] = N.hex2srgb(SHADOW_COL)
    out[core, 3] = 0.45
    out[rim, 3] = 0.26
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
    sprites, sils = [], []
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
        idr = N.render_data_ss("char_id_ss.png", SS, P.W, P.H)
        vl.material_override = None
        sc.view_settings.view_transform = "Standard"
        sc.render.image_settings.color_depth = "8"
        idr = idr[y0 * SS:(y0 + CROP_H) * SS, x0 * SS:(x0 + CROP_W) * SS]
        idd = N.take(idr, pick, SS)
        code = np.round(idd[..., 0] * 64).astype(int)
        ids, fam = code // 8, np.clip(code % 8, 0, len(FAM) - 1)
        depth = idd[..., 1] * 12.0 + 6.0
        arr, idx = snap_to_family(arr, fam)
        arr = N.despeckle(arr)
        arr, idx = snap_to_family(arr, fam)
        arr = inner_lines(arr, idx, fam, ids, depth)
        arr = outline_selective(arr, fam)
        sil = np.zeros_like(arr)
        sil[..., 3] = arr[..., 3]
        sils.append(sil)
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
        op = arr[..., 3] > 0.99
        rows = np.where(op.any(1))[0]
        print("NAT pose", pose, "height px", rows.max() - FEET_PX + 1 if len(rows) else 0,
              "colours", N.count_colours(arr, arr[..., 3] > 0.01))
    with open(os.path.join(N.OUT, "character.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    N.sheet(sprites, os.path.join(N.OUT, "character_sheet.png"), scale=4, bg="#7a7a7a", pad=8)
    # silhouette check: 1x row + 3x row on light grey
    N.sheet(sils, os.path.join(N.WORK, "char_silhouettes.png"), scale=3, bg="#c8c8c8", pad=4)
    allc = np.concatenate([s.reshape(-1, 4) for s in sprites])
    print("NAT character colours (all poses)", N.count_colours(allc[None], allc[None][..., 3] > 0.01))
    print("NAT character done")


if __name__ == "__main__":
    main()
