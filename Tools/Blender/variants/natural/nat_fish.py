"""
FishingKing - "natural" variant fish sprites.

Geometry, species params and swim frames come from fk_fish (imported, untouched); only the look changes
(monkeypatched inside this process).

SIDE views: wide countershading (back L -0.12, belly +0.08), real sun + sky light through Shader-to-RGB
with a cool-shadow / warm-light multiply ramp, wet glint, softened patterns (no scale noise on small
fish), fins in the flank colour at L -0.04 with darker 1px rays and a darker trailing tail edge. The
render is mode-filtered, snapped to <= 12 colours (20 for fish >= 60 px), then gets a selective outline
(back-colour ramp step 0 along the belly and tail, step 1 along the back, OKLab L >= 0.22) and a painted
eye (2x2 = dark pupil + 1 catchlight pixel for fish >= 28 px, 1 dark pixel below that).

TOP views (underwater shadows): their own silhouette look - 3 flat body tones (back colour mixed 50%
towards the deep-water colour #16262b, desaturated), fins exactly one tone lighter, no patterns (one
darker midline for trout and pike), a tapered 2-3 px snout, 2-3 px pectoral nubs behind the head and
no outline, so the in-game UnderwaterTint turns them into readable dark fish shapes.

Writes (only) into Tools/Blender/_tmp/variants/natural/:
  fish/<id>_0.png, <id>_1.png   side view facing +X (2 swim frames)
  fish/<id>_t0.png, <id>_t1.png top view (underwater shadow sprite)
  fish_sheet.png                crucian_carp, largemouth_bass, rainbow_trout, red_seabream, blue_marlin + bass top
  fish_all.png                  every rendered species (inspection)

Run: blender -b --python Tools/Blender/variants/natural/nat_fish.py [-- id1 id2 ...] [--sheet]
"""
import sys
import os
import math

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N  # noqa: E402
from nat_core import C, bpy, bmesh, np, Vector  # noqa: E402
import fk_fish as FF  # noqa: E402

OUTD = os.path.join(N.OUT, "fish")
SS = 4
SIDE_SUN = Vector((-0.35, -0.8, 0.5)).normalized()
TOP_LIGHT = Vector((-0.35, -0.3, 0.89)).normalized()
LIGHT_MUL = ["#878a9c", "#b6b8c4", "#e7e5e3", "#fffdf6"]
LIGHT_POS = [0.0, 0.32, 0.55, 0.82]
DEEP = "#16262b"
MIDLINE = {"rainbow_trout", "northern_pike"}
FANTASY = {"golden_carp", "crystal_koi", "cave_tetra"}
_cur = {}

# marlin: tall sail at the front tapering to a low ridge (h 1.6 -> ~0.3)
FF.FIN_SHAPES["marlin"] = [(0.0, 0.19), (0.4, 0.2), (0.6, 0.28), (0.74, 0.48), (0.84, 0.8), (0.9, 1.0),
                           (0.95, 0.94), (1.0, 0.0)]


def soften(h, dc=0.86, dl=0.0):
    lab = N.srgb2lab(N.hex2srgb(h))
    lab = np.array([lab[0] + dl, lab[1] * dc, lab[2] * dc])
    return N.srgb2hex(N.lab2srgb(lab))


def natural_params(fid, p):
    """Species params -> natural look (desaturated, wider countershading, softer patterns)."""
    q = dict(p)
    col = {k: soften(v) for k, v in p["col"].items()}
    col["back"] = N.shade_hex(col["back"], dl=-0.12)
    col["belly"] = N.shade_hex(col["belly"], dl=0.08)
    q["col"] = col
    pats = []
    for pat in p.get("pat", []):
        pt = dict(pat)
        pt["color"] = soften(pat["color"], 0.9)
        if pt["type"] == "scales":
            if p["px"] < 30:
                continue                     # 1px scale noise on small fish only reads as mud
            pt["alpha"] = pat.get("alpha", 1.0) * 0.45
        if pt["type"] == "spots":
            lp = N.hexL(pt["color"])
            ls = N.hexL(col["side"])
            if lp > ls:
                pt["color"] = N.mixhex(pt["color"], col["side"], 0.45)
        pats.append(pt)
    q["pat"] = pats
    q["_eye"] = p.get("eye", 0.11)
    q["eye"] = 0.0                                           # eyes are painted in post (finish_side)
    if fid == "blue_marlin":
        d = [dict(t0=0.3, t1=0.8, h=1.6, shape="marlin", rake=0.12)] + list(p["dorsal"][1:])
        q["dorsal"] = d
    return q


def fin_colour(p):
    side = p["col"]["side"]
    c = N.mixhex(p["col"]["fin"], side, 0.5)
    lab = N.srgb2lab(N.hex2srgb(c))
    lab[0] = N.hexL(side) - 0.04
    return N.srgb2hex(N.lab2srgb(lab))


# ----------------------------------------------------------------------------- materials (monkeypatched)
def _light_mul(nb):
    L = N._light_value(nb, ao=0.35, ao_dist=0.05)
    return N.ramp_colours(nb, L, LIGHT_MUL, LIGHT_POS), L


def _gloss(nb, amount):
    g = nb.node("ShaderNodeBsdfGlossy")
    g.inputs["Roughness"].default_value = 0.25
    s2r = nb.node("ShaderNodeShaderToRGB")
    nb.link(g.outputs[0], s2r.inputs[0])
    bw = nb.node("ShaderNodeRGBToBW")
    nb.link(s2r.outputs[0], bw.inputs[0])
    hi = nb.math("GREATER_THAN", bw.outputs[0], 0.55)
    return nb.math("MULTIPLY", hi, amount)


def body_material(p, name="NatBody"):
    col = p["col"]
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["UV"])
    t, v = s[0], s[1]
    bl = p.get("belly_line", 0.36)
    belly, side, back = col["belly"], col["side"], col["back"]
    stops = [(0.0, N.lin4(belly)), (bl * 0.6, N.lin4(N.mixhex(belly, side, 0.45))), (bl, N.lin4(side)),
             ((bl + 0.72) / 2, N.lin4(N.mixhex(side, back, 0.5))), (0.74, N.lin4(back)),
             (0.9, N.lin4(N.shade_hex(back, dl=-0.03)))]
    base = nb.ramp(v, stops)
    for i, pat in enumerate(p.get("pat", [])):
        base = FF.apply_pattern(nb, base, t, v, pat, p, i)
    tg = 1.0 - (1.0 - p["peak"]) * 0.6
    vv = nb.math("MULTIPLY", nb.math("SUBTRACT", v, 0.5), 2.0)
    tl = nb.math("SUBTRACT", tg, nb.math("MULTIPLY", nb.math("MULTIPLY", vv, vv), 0.045))
    gill = nb.math("LESS_THAN", nb.math("ABSOLUTE", nb.math("SUBTRACT", t, tl)), 0.55 / p["px"])
    gill = nb.math("MULTIPLY", gill, nb.in_range(v, 0.2, 0.86))
    base = nb.mix(nb.math("MULTIPLY", gill, 0.45), base, N.lin4(N.shade_hex(col["back"], dl=-0.06)))
    shade, L = _light_mul(nb)
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    sh = p.get("shine", 0.0)
    if sh > 0.2:
        g = _gloss(nb, 0.1 + 0.12 * min(sh, 1.2))
        out = nb.mix(g, out, (1.0, 0.98, 0.93, 1.0))
    if p.get("emit", 1.0) > 1.05:
        out = nb.mix(0.15, out, (1, 1, 1, 1), "ADD")
    nb.output_emission(out, 1.0)
    return m


def fin_material(p, name="NatFin"):
    col = fin_colour(p)
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    base = nb.rgb(N.lin4(col))
    spots = p.get("finspots")
    tc = nb.node("ShaderNodeTexCoord")
    ob = nb.sep(tc.outputs["Object"])
    if spots:
        vo = nb.node("ShaderNodeTexVoronoi")
        nb.link(tc.outputs["Object"], vo.inputs["Vector"])
        vo.inputs["Scale"].default_value = 24.0
        mask = nb.math("LESS_THAN", vo.outputs["Distance"], 0.2)
        base = nb.mix(mask, base, N.lin4(soften(spots, 0.9)))
    # darker 1px rays radiating from the object origin (tail/pectoral base, body centre for the others)
    ang = nb.math("ARCTAN2", ob[2], nb.math("MULTIPLY", ob[0], -1.0))
    nray = max(10.0, 0.55 * p["px"])
    ray = nb.math("GREATER_THAN", nb.math("SINE", nb.math("MULTIPLY", ang, nray)), 0.45)
    base = nb.mix(nb.math("MULTIPLY", ray, 0.55), base, N.lin4(N.shade_hex(col, dl=-0.1)))
    shade = N.ramp_colours(nb, N._light_value(nb, ao=0.2, ao_dist=0.05),
                           ["#7b8398", "#b7bccb", "#f0efee", "#ffffff"], [0.0, 0.25, 0.5, 0.85])
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(out, 1.0)
    return m


def toon_material(name, color, shine=0.0, flat=False, emit=1.0, stops=None):
    p = _cur.get("p", {})
    if name == "Mouth":
        return N.flat(N.shade_hex(p.get("col", {}).get("side", "#555555"), dl=-0.3, dc=0.6))
    if _cur.get("top"):
        return N.flat(_cur["tones"][2])
    if isinstance(color, str):
        return N.lit(color, n=4, ao=0.2, ao_dist=0.05, haze=0.0, name=name)
    return N.flat("#808080")


FF.body_material = body_material
FF.fin_material = fin_material
C.toon_material = toon_material


# ----------------------------------------------------------------------------- render helpers
def ortho(cx, cy_or_z, w, h, ppu, top=False):
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new("FishCam", bpy.data.cameras.new("FishCam"))
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.type = "ORTHO"
    cam.data.clip_start = 0.1
    cam.data.clip_end = 200
    cam.data.ortho_scale = max(w, h) / ppu
    if top:
        cam.location = (cx, cy_or_z, 30)
        cam.rotation_euler = (0, 0, 0)
    else:
        cam.location = (cx, -100, cy_or_z)
        cam.rotation_euler = (math.radians(90), 0, 0)
    sc.render.resolution_x, sc.render.resolution_y = w, h


def clear_meshes():
    sc = bpy.context.scene
    for ob in list(sc.objects):
        if ob.type == "MESH":
            bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    for m in list(bpy.data.materials):
        if m.users == 0:
            bpy.data.materials.remove(m)
    N._mats.clear()


def set_sun(sun, d):
    sun.rotation_euler = d.to_track_quat("Z", "Y").to_euler()


def build_side(fid, p, frame, ppu=None):
    objs = FF.build_fish(fid, p, frame, ppu)
    keep = []
    for ob in objs:
        if ob.name.startswith(("Eye", "Pupil")):
            bpy.data.objects.remove(ob, do_unlink=True)
        else:
            keep.append(ob)
    return keep


def eye_points(p):
    b = FF.Body(p)
    et = p.get("eye_t", 1 - (1 - p["peak"]) * 0.28)
    ez = b.zc(et) + p.get("eye_v", 0.3) * b.ru(et)
    pts = [(b.x(et) + 0.01, ez)]
    if p.get("two_eyes"):
        pts.append((b.x(et) + 0.01, ez + p.get("_eye", 0.11) * b.H * 2.6))
    return pts


def lab_of(h):
    return N.srgb2lab(N.hex2srgb(h))


def finish_side(raw, p, fid, cam):
    """mode filter -> <= k colours -> trailing tail edge -> eye -> selective outline."""
    cxw, czw, w, h, ppu = cam
    arr, _ = N.mode_down(raw, SS, cover=0.45)
    k = 20 if p["px"] >= 60 else 12
    arr = N.quantize(arr, k - 3)
    op = arr[..., 3] > 0.5
    ramp = N.gen_ramp(p["col"]["back"], 5, minL=0.22)
    o0, o1 = N.hex2srgb(ramp[0]), N.hex2srgb(ramp[1])
    back_lab = lab_of(p["col"]["back"])
    pupil = N.lab2srgb(np.array([0.2, back_lab[1] * 0.4, back_lab[2] * 0.4]))
    ped_x = int(math.floor(w / 2 + (-0.5 - cxw) * ppu))
    cy = h / 2 + (0.0 - czw) * ppu
    # palette of this sprite (for 'one step darker' picks)
    keys = np.unique(N.ckeys(arr[..., :3])[op])
    pal = N.unkey(keys)
    pal_lab = N.arr_lab(pal)

    def darker(rgb, dl=0.1):
        lab = N.arr_lab(rgb)
        cand = pal_lab[:, 0] < lab[0] - 0.03
        if not cand.any():
            return np.asarray(rgb)
        tgt = lab - np.array([dl, 0, 0])
        d = ((pal_lab - tgt) ** 2 * np.array([1.0, 2.0, 2.0])).sum(-1)
        d[~cand] = 1e9
        return pal[int(d.argmin())]
    # darker trailing edge on the tail (tail pixels whose left neighbour is empty)
    out = arr.copy()
    for y in range(h):
        for x in range(1, min(ped_x, w)):
            if op[y, x] and not op[y, x - 1]:
                out[y, x, :3] = darker(arr[y, x, :3])
    arr = out
    # eye(s)
    big = p["px"] >= 28
    lightest = pal[int(pal_lab[:, 0].argmax())]
    for (ex, ez) in eye_points(p):
        px = w / 2 + (ex - cxw) * ppu
        py = h / 2 + (ez - czw) * ppu
        if big:
            x0, y0 = int(round(px - 1.0)), int(round(py - 1.0))
            for (dx, dy) in ((0, 0), (1, 0), (0, 1), (1, 1)):
                if 0 <= y0 + dy < h and 0 <= x0 + dx < w and op[y0 + dy, x0 + dx]:
                    arr[y0 + dy, x0 + dx, :3] = pupil
            if 0 <= y0 + 1 < h and 0 <= x0 < w and op[y0 + 1, x0]:
                arr[y0 + 1, x0, :3] = lightest          # catchlight: upper pixel on the sun side
        else:
            xi, yi = int(math.floor(px)), int(math.floor(py))
            if 0 <= yi < h and 0 <= xi < w and op[yi, xi]:
                arr[yi, xi, :3] = pupil
    # selective outline (bottom-up arrays): back half (above the body axis, not the tail) -> step 1
    out = arr.copy()
    for y in range(h):
        for x in range(w):
            if op[y, x]:
                continue
            nb = [(y, x - 1), (y, x + 1), (y - 1, x), (y + 1, x)]
            if not any(0 <= yy < h and 0 <= xx < w and op[yy, xx] for yy, xx in nb):
                continue
            back = y > cy and x >= ped_x
            out[y, x, :3] = o1 if back else o0
            out[y, x, 3] = 1.0
    return out


def render_side(fid, sun):
    p = natural_params(fid, FF.F[fid])
    _cur.clear()
    _cur["p"] = p
    set_sun(sun, SIDE_SUN)
    clear_meshes()
    objs = FF.build_fish(fid, p, 0)
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    box = None
    out = []
    for fr in (0, 1):
        clear_meshes()
        objs = build_side(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        if fr == 0:
            box = C.world_bounds(objs)
        fx0, fx1, fz0, fz1 = box
        pad = 2
        w = int(math.ceil((fx1 - fx0) * ppu)) + pad * 2
        h = int(math.ceil((fz1 - fz0) * ppu)) + pad * 2
        cam = ((fx0 + fx1) / 2, (fz0 + fz1) / 2, w, h, ppu)
        ortho(cam[0], cam[1], w, h, ppu)
        raw = N.render_ss("fish_ss.png", SS, w, h)
        arr = finish_side(raw, p, fid, cam)
        C.save_pixels(arr, os.path.join(OUTD, f"{fid}_{fr}.png"))
        out.append(arr)
    return out


# ----------------------------------------------------------------------------- top view (silhouette look)
def top_tones(fid, p):
    """(dark, mid, light) flat tones: back colour mixed towards the deep water colour, desaturated."""
    back = p["col"]["back"]
    t, dc = (0.25, 0.9) if fid in FANTASY else (0.5, 0.6)
    lab = N.srgb2lab(N.hex2srgb(N.mixhex(back, DEEP, t)))
    L = float(np.clip(lab[0], 0.33, 0.46)) if fid not in FANTASY else lab[0]
    mid = np.array([L, lab[1] * dc, lab[2] * dc])
    tones = []
    for dl, cool in ((-0.055, 0.3), (0.0, 0.0), (0.065, -0.2)):
        c = mid + np.array([dl, -0.006 * cool, -0.03 * cool])
        tones.append(N.srgb2hex(N.lab2srgb(c)))
    return tones


def top_body_mat(tones, midline):
    """3 flat tones from the (fixed) light direction: lit upper-left strip, body, shadow flank."""
    m = bpy.data.materials.new("TopBody")
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    nl = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(TOP_LIGHT))
    col = nb.ramp(nl, [(-1.0, N.lin4(tones[0])), (0.5, N.lin4(tones[1])), (0.975, N.lin4(tones[2]))])
    if midline:
        tc = nb.node("ShaderNodeTexCoord")
        v = nb.sep(tc.outputs["UV"])[1]
        t = nb.sep(tc.outputs["UV"])[0]
        ml = nb.math("MULTIPLY", nb.math("GREATER_THAN", v, 0.972), nb.in_range(t, 0.08, 0.86))
        col = nb.mix(ml, col, N.lin4(tones[0]))
    nb.output_emission(col, 1.0)
    return m


def tri_xy(name, pts, z, mat):
    bm = bmesh.new()
    vs = [bm.verts.new((x, y, z)) for x, y in pts]
    bm.faces.new(vs)
    return C.mesh_object(name, bm, mat)


def build_top(fid, p, frame, ppu):
    """Top view built for a silhouette read: slimmer than fk_fish's (w x1.3 instead of x1.45), tapered snout, pectoral nubs."""
    p = dict(p)
    p["_id"] = fid
    p["w"] = p.get("w", 0.55) * 1.3
    b = FF.Body(p)
    tones = _cur["tones"]
    bmat = top_body_mat(tones, fid in MIDLINE)
    fmat = N.flat(tones[2], name="TopFin")
    body = FF.build_body(b, bmat)
    objs = [body]
    sgn = 1 if frame == 0 else -1
    for v in body.data.vertices:
        t = v.co.x + 0.5
        if t > 0.7:
            v.co.y *= 1.0 - 0.42 * FF.smoothstep(0.7, 1.0, t)
        if t < 0.5:
            k = (0.5 - t) / 0.5
            v.co.y += sgn * 0.075 * k * k
    tail = C.poly_object("Tail", FF.tail_outline(b, p["tail"]), fmat, thickness=0.012)
    tail.location = (b.x(0.0), sgn * 0.075, b.zc(0.0))
    tail.rotation_euler = (math.radians(90), 0, math.radians(sgn * 24))
    objs.append(tail)
    # pectoral nubs: 2-3 px out, just behind the head
    tp = min(0.8, p["peak"] + 0.2)
    hw = b.half_w(tp) * (1.0 - 0.42 * FF.smoothstep(0.7, 1.0, tp))
    out_ = max(2.4 / ppu, hw * 0.5)
    ln = max(3.0 / ppu, 0.06)
    x = b.x(tp)
    zt = b.zc(tp) + b.ru(tp) * 0.2
    for side in (1, -1):
        pts = [(x + 0.2 / ppu, side * hw * 0.75), (x - ln, side * (hw + out_)), (x - ln * 0.55, side * hw * 0.75)]
        objs.append(tri_xy("Pect", pts, zt, fmat))
    for bar in p.get("barbels", []):
        at = bar.get("at", 0.985)
        x0, z0 = b.x(at), b.zc(at)
        L = bar["len"]
        for side in (1, -1):
            pts3 = []
            for k in range(6):
                s = k / 5
                a = math.radians(35 + 25 * s)
                pts3.append((x0 - math.cos(a) * L * s * 0.4 + L * 0.2 * s,
                             side * (b.half_w(at) * 0.6 + math.sin(a) * L * s), z0))
            objs.append(C.tube_along("Barbel", pts3, [max(0.012, 0.6 / ppu) * (1 - 0.4 * k / 5) for k in range(6)],
                                     fmat))
    if p.get("bill"):
        Lb = p["bill"]
        pts3 = [(b.x(0.96), 0, b.zc(0.96)), (b.x(1.0) + Lb * 0.5, 0, b.zc(1.0)), (b.x(1.0) + Lb, 0, b.zc(1.0))]
        objs.append(C.tube_along("Bill", pts3, [b.H * 0.1, max(b.H * 0.05, 0.6 / ppu), 0.006], bmat))
    if p.get("lure"):
        lu = p["lure"]
        base = (b.x(0.93), 0, b.ztop(0.93))
        Ll = lu["len"]
        pts3 = [base, (base[0] + Ll * 0.4, 0, base[2] + 0.1), (base[0] + Ll * 0.9, 0, base[2])]
        objs.append(C.tube_along("Stalk", pts3, 0.014, fmat))
        objs.append(C.add_prim("sphere", "Bulb", fmat, radius=0.06, location=pts3[-1], segments=10, ring_count=6))
    return objs


def silhouette_from_side(arr, tones):
    """Top view for flat/tall fish (TOP_USES_SIDE): the side sprite re-toned into the 3 silhouette tones."""
    out = arr.copy()
    op = arr[..., 3] > 0.5
    L = N.arr_lab(arr[..., :3])[..., 0]
    lo, hi = np.percentile(L[op], [30, 80])
    t = np.where(L < lo, 0, np.where(L < hi, 1, 2))
    cols = np.array([N.hex2srgb(c) for c in tones])
    out[op, :3] = cols[t[op]]
    return out


def render_top(fid, sun):
    p = natural_params(fid, FF.F[fid])
    _cur.clear()
    _cur["p"] = p
    _cur["top"] = True
    _cur["tones"] = top_tones(fid, p)
    if fid in FF.TOP_USES_SIDE:
        for fr in (0, 1):
            arr = C.load_pixels(os.path.join(OUTD, f"{fid}_{fr}.png"))
            C.save_pixels(silhouette_from_side(arr, _cur["tones"]), os.path.join(OUTD, f"{fid}_t{fr}.png"))
        return
    clear_meshes()
    objs = build_top(fid, p, 0, 16.0)
    bpy.context.view_layer.update()
    x0, x1, _, _ = FF.bounds_xy(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    boxes = []
    for fr in (0, 1):
        clear_meshes()
        objs = build_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        boxes.append(FF.bounds_xy(objs))
    bx0 = min(b[0] for b in boxes)
    bx1 = max(b[1] for b in boxes)
    by0 = min(b[2] for b in boxes)
    by1 = max(b[3] for b in boxes)
    pad = 2
    w = int(math.ceil((bx1 - bx0) * ppu)) + pad * 2
    h = int(math.ceil(2 * max(abs(by0), abs(by1)) * ppu)) + pad * 2
    h += h % 2
    for fr in (0, 1):
        clear_meshes()
        build_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        ortho((bx0 + bx1) / 2, 0.0, w, h, ppu, top=True)
        raw = N.render_ss("fish_top_ss.png", SS, w, h)
        arr, _ = N.mode_down(raw, SS, cover=0.45)
        arr = N.quantize(arr, 3, keep=_cur["tones"])
        C.save_pixels(arr, os.path.join(OUTD, f"{fid}_t{fr}.png"))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ids = [a for a in argv if not a.startswith("--")] or list(FF.F.keys())
    sc = N.new_scene()
    sc.render.film_transparent = True
    sun = N.add_sun(SIDE_SUN)
    N.set_world(up=0.32, hor=0.24, down=0.12)
    os.makedirs(OUTD, exist_ok=True)
    for fid in ([] if "--sheet" in sys.argv else ids):
        render_side(fid, sun)
        render_top(fid, sun)
        a = C.load_pixels(os.path.join(OUTD, f"{fid}_0.png"))
        t = C.load_pixels(os.path.join(OUTD, f"{fid}_t0.png"))
        print("NAT fish", fid, "side", a.shape[1], "x", a.shape[0], "colours", N.count_colours(a),
              "top colours", N.count_colours(t))
    L = lambda n: C.load_pixels(os.path.join(OUTD, n))  # noqa: E731
    want = ["crucian_carp", "largemouth_bass", "rainbow_trout", "red_seabream", "blue_marlin"]
    if all(os.path.exists(os.path.join(OUTD, f"{i}_0.png")) for i in want):
        rows = [[L(f"{i}_0.png") for i in want[:4]], [L("blue_marlin_0.png"), L("largemouth_bass_t0.png")]]
        N.sheet_rows(rows, os.path.join(N.OUT, "fish_sheet.png"), scale=4, bg="#6f7a80", pad=12, gap=40)
    allimgs = []
    for i in (list(FF.F.keys()) if "--sheet" in sys.argv else ids):
        if os.path.exists(os.path.join(OUTD, f"{i}_0.png")):
            allimgs += [L(f"{i}_0.png"), L(f"{i}_1.png"), L(f"{i}_t0.png")]
    if allimgs:
        N.sheet(allimgs, os.path.join(N.OUT, "fish_all.png"), scale=3, bg="#6f7a80", pad=6, cols=9)
    print("NAT fish done")


if __name__ == "__main__":
    main()
