"""
FishingKing - "natural" variant fish sprites.

Geometry, species params and swim frames come from fk_fish (imported, untouched); only the look changes
(monkeypatched inside this process): posterised countershading bands, real sun + sky light through
Shader-to-RGB with a cool-shadow / warm-light multiply ramp, wet specular glint, softer patterns,
subtle scales, translucent-looking fins with rays, small realistic eyes (muted iris), 1px darker
same-hue outline, 4x supersampled mode-filtered render.

Writes (only) into Tools/Blender/_tmp/variants/natural/:
  fish/<id>_0.png, <id>_1.png   side view facing +X (2 swim frames)
  fish/<id>_t0.png, <id>_t1.png top view (underwater shadow sprite)
  fish_sheet.png                crucian_carp, largemouth_bass, rainbow_trout, red_seabream, blue_marlin + bass top
  fish_all.png                  every rendered species (inspection)

Run: blender -b --python Tools/Blender/variants/natural/nat_fish.py [-- id1 id2 ...]
"""
import sys
import os
import math
import shutil

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N  # noqa: E402
from nat_core import C, bpy, np, Vector  # noqa: E402
import fk_fish as FF  # noqa: E402

OUTD = os.path.join(N.OUT, "fish")
SS = 4
SIDE_SUN = Vector((-0.35, -0.8, 0.5)).normalized()
TOP_SUN = Vector((-0.35, -0.3, 0.89)).normalized()
LIGHT_MUL = ["#878a9c", "#b6b8c4", "#e7e5e3", "#fffdf6"]
LIGHT_POS = [0.0, 0.32, 0.55, 0.82]
_cur = {}


def soften(h, dc=0.86, dl=0.0):
    lab = N.srgb2lab(N.hex2srgb(h))
    lab = np.array([lab[0] + dl, lab[1] * dc, lab[2] * dc])
    return N.srgb2hex(N.lab2srgb(lab))


def natural_params(p):
    """Species params -> natural look (desaturated colours, softer scales, small muted eyes)."""
    q = dict(p)
    q["col"] = {k: soften(v) for k, v in p["col"].items()}
    pats = []
    for pat in p.get("pat", []):
        pt = dict(pat)
        pt["color"] = soften(pat["color"], 0.9)
        if pt["type"] == "scales":
            pt["alpha"] = pat.get("alpha", 1.0) * 0.45
        if pt["type"] == "spots":
            lp = N.srgb2lab(N.hex2srgb(pt["color"]))[0]
            ls = N.srgb2lab(N.hex2srgb(q["col"]["side"]))[0]
            if lp > ls:      # light/iridescent spots: keep them subtle
                pt["color"] = N.mixhex(pt["color"], q["col"]["side"], 0.45)
        pats.append(pt)
    q["pat"] = pats
    q["eye"] = p.get("eye", 0.11) * 0.8
    shiny = p.get("shine", 0.0) >= 0.8
    q["eye_col"] = "#b9bcb4" if shiny else "#a8904e"
    if "eye_col" in p and p["eye_col"] not in ("#f2e6c0",):
        q["eye_col"] = soften(p["eye_col"], 0.8)
    return q


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
    # operculum (gill cover) arc: a 1px darker curve behind the head
    tg = 1.0 - (1.0 - p["peak"]) * 0.6
    vv = nb.math("MULTIPLY", nb.math("SUBTRACT", v, 0.5), 2.0)
    tl = nb.math("SUBTRACT", tg, nb.math("MULTIPLY", nb.math("MULTIPLY", vv, vv), 0.045))
    gill = nb.math("LESS_THAN", nb.math("ABSOLUTE", nb.math("SUBTRACT", t, tl)), 0.55 / p["px"])
    gill = nb.math("MULTIPLY", gill, nb.in_range(v, 0.2, 0.86))
    base = nb.mix(nb.math("MULTIPLY", gill, 0.45), base, N.lin4(N.shade_hex(col["back"], dl=-0.08)))
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
    col = N.mixhex(p["col"]["fin"], p["col"]["side"], 0.2)
    col = N.shade_hex(col, dl=0.05, dc=0.85)
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
    # fin rays radiating from the object origin (tail/pectoral base, body centre for the others)
    ang = nb.math("ARCTAN2", ob[2], nb.math("MULTIPLY", ob[0], -1.0))
    nray = max(10.0, 0.55 * p["px"])
    ray = nb.math("GREATER_THAN", nb.math("SINE", nb.math("MULTIPLY", ang, nray)), 0.35)
    base = nb.mix(nb.math("MULTIPLY", ray, 0.28), base, N.lin4(N.shade_hex(col, dl=-0.12)))
    shade = N.ramp_colours(nb, N._light_value(nb, ao=0.2, ao_dist=0.05),
                           ["#7b8398", "#b7bccb", "#f0efee", "#ffffff"], [0.0, 0.25, 0.5, 0.85])
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(out, 1.0)
    return m


def toon_material(name, color, shine=0.0, flat=False, emit=1.0, stops=None):
    p = _cur.get("p", {})
    if name == "EyeP":
        return N.flat("#15130f")
    if name == "EyeW":
        return N.lit(color if isinstance(color, str) else "#a8904e", n=3, ao=0.0, haze=0.0, name="Iris")
    if name == "Mouth":
        return N.flat(N.shade_hex(p.get("col", {}).get("side", "#555555"), dl=-0.3, dc=0.6))
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


def finish(raw, cover=0.45):
    arr, _ = N.mode_down(raw, SS, cover=cover)
    return N.outline_sel(arr, mulL=0.5, cool=0.7)


def set_sun(sun, d):
    sun.rotation_euler = d.to_track_quat("Z", "Y").to_euler()


def render_side(fid, sun):
    p = natural_params(FF.F[fid])
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
        objs = FF.build_fish(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        if fr == 0:
            box = C.world_bounds(objs)
        fx0, fx1, fz0, fz1 = box
        pad = 2
        w = int(math.ceil((fx1 - fx0) * ppu)) + pad * 2
        h = int(math.ceil((fz1 - fz0) * ppu)) + pad * 2
        ortho((fx0 + fx1) / 2, (fz0 + fz1) / 2, w, h, ppu)
        raw = N.render_ss("fish_ss.png", SS, w, h)
        arr = finish(raw)
        C.save_pixels(arr, os.path.join(OUTD, f"{fid}_{fr}.png"))
        out.append(arr)
    return out


def render_top(fid, sun):
    if fid in FF.TOP_USES_SIDE:
        for fr in (0, 1):
            shutil.copyfile(os.path.join(OUTD, f"{fid}_{fr}.png"), os.path.join(OUTD, f"{fid}_t{fr}.png"))
        return
    p = natural_params(FF.F[fid])
    _cur["p"] = p
    set_sun(sun, TOP_SUN)
    clear_meshes()
    objs = FF.build_fish_top(fid, p, 0, 16.0)
    bpy.context.view_layer.update()
    x0, x1, _, _ = FF.bounds_xy(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    boxes = []
    for fr in (0, 1):
        clear_meshes()
        objs = FF.build_fish_top(fid, p, fr, ppu)
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
        FF.build_fish_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        ortho((bx0 + bx1) / 2, 0.0, w, h, ppu, top=True)
        raw = N.render_ss("fish_top_ss.png", SS, w, h)
        C.save_pixels(finish(raw), os.path.join(OUTD, f"{fid}_t{fr}.png"))


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
        print("NAT fish", fid)
    L = lambda n: C.load_pixels(os.path.join(OUTD, n))  # noqa: E731
    want = ["crucian_carp", "largemouth_bass", "rainbow_trout", "red_seabream", "blue_marlin"]
    if all(os.path.exists(os.path.join(OUTD, f"{i}_0.png")) for i in want):
        rows = [[L(f"{i}_0.png") for i in want[:4]], [L("blue_marlin_0.png"), L("largemouth_bass_t0.png")]]
        N.sheet_rows(rows, os.path.join(N.OUT, "fish_sheet.png"), scale=4, bg="#6f7a80", pad=12, gap=40)
    allimgs = []
    for i in ids:
        allimgs += [L(f"{i}_0.png"), L(f"{i}_1.png"), L(f"{i}_t0.png")]
    N.sheet(allimgs, os.path.join(N.OUT, "fish_all.png"), scale=3, bg="#6f7a80", pad=6, cols=9)
    print("NAT fish done")


if __name__ == "__main__":
    main()
