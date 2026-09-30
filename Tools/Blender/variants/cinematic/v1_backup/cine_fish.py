"""
FishingKing - cinematic variant of the fish sprites.

Same geometry and species table as fk_fish.py (imported, not modified): only the materials and the
pixel post-process change. Warm key light from the upper right (the fish faces right), cool bluish
shadow and bounce from below, a hard glossy highlight band along the back, softer, warm translucent
fins; per-sprite palette + cool selective outline instead of the heavy near-black line.

Run:  blender -b --python Tools/Blender/variants/cinematic/cine_fish.py [-- id1 id2 ...]
Out:  _tmp/variants/cinematic/fish/<id>_0.png, <id>_1.png, <id>_t0.png, <id>_t1.png, fish_sheet.png
"""
import sys
import os
import math
import shutil
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cine_common as K  # noqa: E402
import fk_fish as FF  # noqa: E402

C = K.C
OUTD = os.path.join(K.OUT, "fish")
KEY = np.array([0.45, -0.55, 0.72])
KEY = KEY / np.linalg.norm(KEY)
HALF = KEY + np.array([0.0, -1.0, 0.0])
HALF = HALF / np.linalg.norm(HALF)
KEY_TOP = np.array([0.35, -0.25, 0.9])  # top view: light mostly from above, a bit from the sun side
KEY_TOP = KEY_TOP / np.linalg.norm(KEY_TOP)
MODE = ["side"]

# light multipliers (sRGB hex -> linear) for the banded half-lambert
BODY_STOPS = [(0.0, "#3c4666"), (0.3, "#66728f"), (0.47, "#aeaab2"), (0.61, "#f2e4d0"), (0.8, "#fff4e4")]
FIN_STOPS = [(0.0, "#5e6680"), (0.45, "#b0a8a4"), (0.82, "#eedcc6")]


def _lin(h):
    return K.lin4(h)


def cine_shade(nb, stops, shine=0.0, rim_cool=0.0):
    key = KEY_TOP if MODE[0] == "top" else KEY
    geo = nb.node("ShaderNodeNewGeometry")
    n = geo.outputs["Normal"]
    d = nb.vmath("DOT_PRODUCT", n, tuple(key))
    s = nb.math("MULTIPLY_ADD", d, 0.5, 0.5)
    shade = nb.ramp(s, [(p, _lin(c)) for p, c in stops])
    if rim_cool > 0:
        # cool bounce from the water below on down-facing surfaces
        nz = nb.sep(n)[2]
        dn = nb.math("GREATER_THAN", nb.math("MULTIPLY", nz, -1.0), 0.72)
        shade = nb.mix(nb.math("MULTIPLY", dn, rim_cool), shade, _lin("#8ab0c0"), "MIX")
    if shine > 0:
        half = HALF if MODE[0] == "side" else (KEY_TOP + np.array([0, 0, 1.0])) / np.linalg.norm(KEY_TOP + np.array([0, 0, 1.0]))
        sp = nb.math("GREATER_THAN", nb.vmath("DOT_PRODUCT", n, tuple(half)), 0.9)
        shade = nb.mix(nb.math("MULTIPLY", sp, min(1.0, 0.45 + 0.35 * shine)), shade, (2.6, 2.45, 2.2, 1), "MIX")
    return shade


def body_material(p, name="BodyMat"):
    col = p["col"]
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["UV"])
    t, v = s[0], s[1]
    bl = p.get("belly_line", 0.36)
    base = nb.ramp(v, [(0.0, C.lin(col["belly"])), (bl, C.lin(col["side"])), (0.72, C.lin(col["back"]))])
    for i, pat in enumerate(p.get("pat", [])):
        base = FF.apply_pattern(nb, base, t, v, pat, p, i)
    shade = cine_shade(nb, BODY_STOPS, max(p.get("shine", 0.0), 0.25), rim_cool=0.35)
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(out, p.get("emit", 1.0))
    return m


def fin_material(p, name="FinMat"):
    col = p["col"]["fin"]
    spots = p.get("finspots")
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    base = nb.rgb(C.lin(col))
    if spots:
        tc = nb.node("ShaderNodeTexCoord")
        vo = nb.node("ShaderNodeTexVoronoi")
        nb.link(tc.outputs["Object"], vo.inputs["Vector"])
        vo.inputs["Scale"].default_value = 26.0
        mask = nb.math("LESS_THAN", vo.outputs["Distance"], 0.2)
        base = nb.mix(mask, base, C.lin(spots))
    if MODE[0] == "top":
        # seen from above the fins sit over dark water: take the back colour so the shadow reads as one shape
        base = nb.mix(0.55, base, C.lin(p["col"]["back"]))
        base = nb.mix(1.0, base, _lin("#b8b4b4"), "MULTIPLY")
    # translucent fins: backlit by the warm key -> lighter and warmer, low contrast
    base = nb.mix(0.08, base, _lin("#ffcc90"))
    shade = cine_shade(nb, FIN_STOPS)
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(out, p.get("emit", 1.0))
    return m


FF.body_material = body_material
FF.fin_material = fin_material


# ----------------------------------------------------------------------------- post-process
def post(arr, n_colors=18):
    a = K.harden(arr)
    a = K.grade(a, sat=0.92, split=0.6, contrast=0.05)
    m = a[..., 3] > 0.5
    if m.sum() > 8:
        pal = K.kmeans_palette(a, n_colors, mask=m, weight_pow=0.7)
        q = K.quantize(a, pal)
        q[..., 3] = a[..., 3]
        a = q
    a = K.outline(a, col=(0.05, 0.08, 0.11), mul=0.42)
    return a


def submerge(a, desat=0.25, tint=0.12):
    """Top views are seen through the water at dusk: a little desaturated and pulled towards teal."""
    c = a[..., :3]
    L = K.luma(c)[..., None]
    c = c + (L - c) * desat
    c = c + (K.hexf("#1e4a50") - c) * tint
    out = a.copy()
    out[..., :3] = np.clip(c, 0, 1)
    return out


def render_png(name):
    img = K.render("fish_" + name)
    return img


def render_species(fid, frames=(0, 1)):
    MODE[0] = "side"
    p = FF.F[fid]
    C.clear_objects()
    objs = FF.build_fish(fid, p, 0)
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    frame0 = None
    for fr in frames:
        C.clear_objects()
        objs = FF.build_fish(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        if fr == 0:
            frame0 = C.world_bounds(objs)
        fx0, fx1, fz0, fz1 = frame0
        pad = 2
        w = int(math.ceil((fx1 - fx0) * ppu)) + pad * 2
        h = int(math.ceil((fz1 - fz0) * ppu)) + pad * 2
        C.ortho_camera((fx0 + fx1) / 2, (fz0 + fz1) / 2, w, h, ppu)
        K.save(post(render_png(f"{fid}_{fr}")), os.path.join(OUTD, f"{fid}_{fr}.png"))


def render_species_top(fid):
    if fid in FF.TOP_USES_SIDE:
        for fr in (0, 1):
            shutil.copyfile(os.path.join(OUTD, f"{fid}_{fr}.png"), os.path.join(OUTD, f"{fid}_t{fr}.png"))
        return
    MODE[0] = "top"
    p = FF.F[fid]
    C.clear_objects()
    objs = FF.build_fish_top(fid, p, 0, 16.0)
    bpy.context.view_layer.update()
    x0, x1, _, _ = FF.bounds_xy(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    boxes = []
    for fr in (0, 1):
        C.clear_objects()
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
        C.clear_objects()
        FF.build_fish_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        FF.top_camera((bx0 + bx1) / 2, 0.0, w, h, ppu)
        K.save(submerge(post(render_png(f"{fid}_t{fr}"), 14)), os.path.join(OUTD, f"{fid}_t{fr}.png"))
    C.ortho_camera(0, 0, 32, 32, 16)
    MODE[0] = "side"


SHEET_SIDE = ["crucian_carp", "largemouth_bass", "rainbow_trout", "red_seabream", "blue_marlin"]


def fish_sheet(out, scale=4, bg=(0.42, 0.42, 0.44)):
    paths = [os.path.join(OUTD, f"{i}_0.png") for i in SHEET_SIDE] + [os.path.join(OUTD, "largemouth_bass_t0.png")]
    imgs = [K.load(p) for p in paths]
    gap = 12
    rows = [imgs[:4], imgs[4:]]
    rw = [sum(i.shape[1] * scale + gap for i in r) + gap for r in rows]
    rh = [max(i.shape[0] for i in r) * scale + gap for r in rows]
    s = np.zeros((sum(rh) + gap, max(rw), 4), np.float32)
    s[..., :3] = bg
    s[..., 3] = 1
    y = gap
    for r, hh in zip(rows, rh):
        x = gap
        for im in r:
            big = K.upscale(im, scale)
            yy = y + (hh - gap - big.shape[0]) // 2
            reg = s[yy:yy + big.shape[0], x:x + big.shape[1]]
            a = big[..., 3:4]
            reg[..., :3] = reg[..., :3] * (1 - a) + big[..., :3] * a
            x += big.shape[1] + gap
        y += hh
    K.save(s, out)


def all_sheet(ids, out, scale=3, cols=6, bg=(0.42, 0.42, 0.44)):
    imgs = [K.load(os.path.join(OUTD, f"{i}_0.png")) for i in ids]
    cw = max(i.shape[1] for i in imgs) * scale + 8
    ch = max(i.shape[0] for i in imgs) * scale + 8
    rows = (len(imgs) + cols - 1) // cols
    s = np.zeros((rows * ch, cols * cw, 4), np.float32)
    s[..., :3] = bg
    s[..., 3] = 1
    for k, im in enumerate(imgs):
        big = K.upscale(im, scale)
        y0 = (k // cols) * ch + (ch - big.shape[0]) // 2
        x0 = (k % cols) * cw + (cw - big.shape[1]) // 2
        reg = s[y0:y0 + big.shape[0], x0:x0 + big.shape[1]]
        a = big[..., 3:4]
        reg[..., :3] = reg[..., :3] * (1 - a) + big[..., :3] * a
    K.save(s, out)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ids = argv or list(FF.F.keys())
    C.reset_scene()
    C.ortho_camera(0, 0, 32, 32, 16)
    for fid in ids:
        render_species(fid)
        render_species_top(fid)
        print("FISH", fid, "ok")
    if all(os.path.exists(os.path.join(OUTD, f"{i}_0.png")) for i in SHEET_SIDE):
        fish_sheet(os.path.join(K.OUT, "fish_sheet.png"))
    if len(ids) == len(FF.F):
        all_sheet(ids, os.path.join(K.OUT, "fish_all_side.png"))
    print("FISH done")


if __name__ == "__main__":
    main()
