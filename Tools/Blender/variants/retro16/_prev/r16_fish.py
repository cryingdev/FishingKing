"""
retro16 - fish sprites. Geometry comes from fk_fish (read-only import); materials are replaced at
runtime (monkeypatch inside this process only) by palette-ramp versions:
  * body: per-region 3-tone hue-shifted ramps (back / side / belly) picked by banded lambert, blended
    along the belly->back axis in narrow zones that become ORDERED-DITHER transitions in post;
  * patterns (spots, bands, stripes, mottles) are shaded with their own 3-tone ramp;
  * scales: crisp staggered scale rows (darker tone of the local region, ~3 px cells);
  * fins: 3-tone ramp with dark fin rays every ~2.5 px.
Post: quantise (+Bayer 4x4) -> despeckle (not the dither) -> inner contour lines -> selective outline.

Outputs: _tmp/variants/retro16/fish/<id>_0.png, <id>_1.png, <id>_t0.png, <id>_t1.png,
         _tmp/variants/retro16/fish_sheet.png (+ fish_all.png for review)
Run: blender -b --python variants/retro16/r16_fish.py [-- id1 id2 ...]
"""
import os
import sys
import math
import shutil
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import r16_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
import fk_common as C  # noqa: E402
import fk_fish as FF  # noqa: E402

OUT = os.path.join(R.OUT, "fish")
FL = Vector((-0.35, -0.62, 0.7)).normalized()   # side view: light from the upper-left, towards the camera
B3 = [0.42, 0.87]
CUR = {"ppu": 16.0, "p": None}


def ramps(p):
    col = p["col"]
    return dict(back=R.ramp_from(col["back"], 1, 1, 0.1, 0.08), side=R.ramp_from(col["side"], 1, 1, 0.09, 0.07),
                belly=R.ramp_from(col["belly"], 1, 1, 0.09, 0.05), fin=R.ramp_from(col["fin"], 1, 1, 0.1, 0.07))


def _region_col(nb, f, rp, t, v, bl):
    cb = R.band_ramp(nb, f, rp["belly"], B3)
    cs = R.band_ramp(nb, f, rp["side"], B3)
    ck = R.band_ramp(nb, f, rp["back"], B3)
    k1 = nb.node("ShaderNodeMapRange")
    k1.clamp = True
    nb.link(v, k1.inputs[0])
    k1.inputs[1].default_value = bl - 0.1
    k1.inputs[2].default_value = bl + 0.1
    c = nb.mix(k1.outputs[0], cb, cs)
    k2 = nb.node("ShaderNodeMapRange")
    k2.clamp = True
    nb.link(v, k2.inputs[0])
    k2.inputs[1].default_value = 0.6
    k2.inputs[2].default_value = 0.8
    return nb.mix(k2.outputs[0], c, ck)


def body_material(p, name="BodyMat"):
    rp = ramps(p)
    for k in ("back", "side", "belly"):
        R._use(rp[k])
    m = bpy.data.materials.new(name)
    nb = R.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["UV"])
    t, v = s[0], s[1]
    f = R._factor_lambert(nb, FL, 0.5)
    bl = p.get("belly_line", 0.36)
    base = _region_col(nb, f, rp, t, v, bl)
    ppu = CUR["ppu"]
    dark = _region_col(nb, nb.math("SUBTRACT", f, 0.4), rp, t, v, bl)
    # gill cover edge: a 1-px arc behind the eye, bulging towards the tail at mid height
    L_px = max(8.0, p["px"] * 0.72)
    tg = 1 - (1 - p["peak"]) * 0.66
    vv = nb.math("MULTIPLY", nb.math("SUBTRACT", v, 0.5), 2.0)
    tl = nb.math("SUBTRACT", tg, nb.math("MULTIPLY", nb.math("SUBTRACT", 1.0, nb.math("MULTIPLY", vv, vv)), 0.03))
    gm = nb.math("LESS_THAN", nb.math("ABSOLUTE", nb.math("SUBTRACT", t, tl)), 0.62 / L_px)
    gm = nb.math("MULTIPLY", gm, nb.in_range(v, 0.2, 0.8))
    if p["px"] >= 20:
        base = nb.mix(gm, base, dark)
    for i, pat in enumerate(p.get("pat", [])):
        if pat["type"] == "scales":
            mask = scale_mask(nb, t, v, p, pat, ppu)
            mask = nb.math("MULTIPLY", mask, nb.math("LESS_THAN", t, tg - 0.05))
            base = nb.mix(mask, base, dark)
        else:
            pr = R.ramp_from(pat["color"], 1, 1, 0.09, 0.06)
            R._use(pr)
            pcol = R.band_ramp(nb, f, pr, B3)
            mask = pattern_mask(nb, t, v, pat, p, i)
            base = nb.mix(mask, base, pcol)
    nb.output_emission(base, 1.0)
    return m


def scale_mask(nb, t, v, p, pat, ppu):
    """Staggered rows of 1-px scale edges: cells ~3.2 px along the body, ~2.4 px high."""
    L_px = max(8.0, p["px"] * 0.72)
    H_px = max(4.0, p["px"] * p["h"] * 2.1)
    nu = max(3.0, L_px / 3.2)
    nv = max(2.0, H_px / 2.4)
    u = nb.math("MULTIPLY", t, nu)
    w = nb.math("MULTIPLY", v, nv)
    row = nb.math("FLOOR", w)
    shift = nb.math("MULTIPLY", nb.math("MODULO", row, 2.0), 0.5)
    fu = nb.math("FRACT", nb.math("ADD", u, shift))
    fw = nb.math("FRACT", w)
    # scale edge: the lower rim of each cell (a short dash), fading towards the head/tail
    m = nb.math("MULTIPLY", nb.math("LESS_THAN", fw, 0.36), nb.math("LESS_THAN", fu, 0.62))
    m = nb.math("MULTIPLY", m, nb.in_range(t, 0.14, 0.8))
    m = nb.math("MULTIPLY", m, nb.in_range(v, 0.3, 0.86))
    return m


def pattern_mask(nb, t, v, pat, p, idx):
    """Masks as in fk_fish.apply_pattern (copied, read-only source) but returning only the mask."""
    typ = pat["type"]
    H = p["h"]
    seed = pat.get("seed", 0.0) + idx * 1.7 + (sum(map(ord, p.get("_id", ""))) % 97) * 0.13
    if typ == "bands":
        tt = t
        if pat.get("wave"):
            wv = nb.math("SINE", nb.math("MULTIPLY", v, 14.0))
            tt = nb.math("MULTIPLY_ADD", wv, pat["wave"] / pat["count"], t)
        fr = nb.math("FRACT", nb.math("MULTIPLY_ADD", tt, pat["count"], 0.37))
        mask = nb.math("LESS_THAN", fr, max(pat["width"], 0.3))
    elif typ == "stripe":
        d = nb.math("ABSOLUTE", nb.math("SUBTRACT", v, pat["center"]))
        mask = nb.math("LESS_THAN", d, max(pat["width"], 0.06))
    elif typ == "spots":
        vec = nb.combine(t, nb.math("MULTIPLY", v, 2.2 * H), seed)
        vo = nb.node("ShaderNodeTexVoronoi")
        vo.voronoi_dimensions = "3D"
        nb.link(vec, vo.inputs["Vector"])
        vo.inputs["Scale"].default_value = min(pat["scale"], p["px"] / 4.0)
        if "Randomness" in vo.inputs:
            vo.inputs["Randomness"].default_value = 0.7
        mask = nb.math("LESS_THAN", vo.outputs["Distance"], max(pat["size"], 0.3))
    elif typ == "mottle":
        vec = nb.combine(t, nb.math("MULTIPLY", v, 2.2 * H), seed)
        no = nb.node("ShaderNodeTexNoise")
        no.noise_dimensions = "3D"
        nb.link(vec, no.inputs["Vector"])
        no.inputs["Scale"].default_value = min(pat["scale"], p["px"] / 3.2)
        no.inputs["Detail"].default_value = 1.0
        mask = nb.math("GREATER_THAN", no.outputs["Fac"], pat["thresh"])
    elif typ == "blotch":
        dx = nb.math("SUBTRACT", t, pat["t"])
        dz = nb.math("MULTIPLY", nb.math("SUBTRACT", v, pat["v"]), 2.2 * H)
        d = nb.vmath("LENGTH", nb.combine(dx, dz, 0.0))
        mask = nb.math("LESS_THAN", d, max(pat["r"], 1.3 / p["px"]))
    elif typ == "region":
        soft = pat.get("soft", 0.0)
        # soft edge -> a narrow ordered-dither zone: mix factor ramps linearly, post dithers it
        if soft > 0:
            k = nb.node("ShaderNodeMapRange")
            k.clamp = True
            nb.link(t, k.inputs[0])
            k.inputs[1].default_value = pat["t1"] + soft * 0.5
            k.inputs[2].default_value = pat["t1"] - soft * 0.5
            mask = k.outputs[0]
        else:
            mask = nb.in_range(t, pat["t0"], pat["t1"])
    else:
        raise ValueError(typ)
    if typ != "region" and ("t0" in pat or "t1" in pat):
        mask = nb.math("MULTIPLY", mask, nb.in_range(t, pat.get("t0", -1), pat.get("t1", 2)))
    if "v0" in pat or "v1" in pat:
        mask = nb.math("MULTIPLY", mask, nb.in_range(v, pat.get("v0", -1), pat.get("v1", 2)))
    return mask


def fin_material(p, name="FinMat"):
    rp = ramps(p)
    R._use(rp["fin"])
    m = bpy.data.materials.new(name)
    nb = R.NB(m)
    f = R._factor_lambert(nb, FL, 0.5)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["Object"])
    per = 2.6 / CUR["ppu"]
    rays = nb.math("LESS_THAN", nb.math("FRACT", nb.math("DIVIDE", s[0], per)), 0.38)
    f = nb.math("MULTIPLY_ADD", rays, -0.28, f)
    col = R.band_ramp(nb, f, rp["fin"], [0.45, 0.76])
    spots = p.get("finspots")
    if spots:
        R._use([spots])
        vo = nb.node("ShaderNodeTexVoronoi")
        nb.link(tc.outputs["Object"], vo.inputs["Vector"])
        vo.inputs["Scale"].default_value = min(26.0, CUR["ppu"] / 3.0)
        mask = nb.math("LESS_THAN", vo.outputs["Distance"], 0.24)
        col = nb.mix(mask, col, C.lin(spots))
    nb.output_emission(col, 1.0)
    return m


def toon_material(name, color, shine=0.0, flat=False, emit=1.0, stops=None):
    if flat:
        return R.m_flat(color)
    return R.m_tone(R.ramp_from(color, 1, 1, 0.1, 0.07), B3, light=FL, name=name)


def glow_material(name, color, strength=1.0):
    return R.m_flat(color)


# runtime-only patches of the read-only modules (this process only)
FF.body_material = body_material
FF.fin_material = fin_material
C.toon_material = toon_material
C.glow_material = glow_material


# ----------------------------------------------------------------------------- render
PUPIL = "#0a0a12"


def eye_px(p, cam):
    """Pixel (col, row top-down) of the eye centre for the side view."""
    b = FF.Body(dict(p))
    et = p.get("eye_t", 1 - (1 - p["peak"]) * 0.28)
    ez = b.zc(et) + p.get("eye_v", 0.3) * b.ru(et)
    cx, cz, w, h, ppu = cam
    return (b.x(et) - cx) * ppu + w / 2, h / 2 - (ez - cz) * ppu


def paint_eye(idx, pal, p, cam):
    if p.get("eye", 0.11) <= 0 or cam is None:
        return idx
    ex, ey = eye_px(p, cam)
    pupil = pal.index(PUPIL) if PUPIL in pal.hex else int(np.argmin(pal.lab[:, 0]))
    hi = int(np.argmax(pal.lab[:, 0]))
    ring = pal.index(p.get("eye_col", "#f2e6c0")) if p.get("eye_col", "#f2e6c0") in pal.hex else hi
    x, y = int(math.floor(ex)), int(math.floor(ey))
    H, W = idx.shape
    def put(xx, yy, v):
        if 0 <= yy < H and 0 <= xx < W and idx[yy, xx] >= 0:
            idx[yy, xx] = v
    if p["px"] >= 44:          # 3x3 eye: ring + 2x2 pupil with a catch-light
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                put(x + dx, y + dy, ring)
        put(x, y, pupil); put(x + 1, y, pupil); put(x, y + 1, pupil); put(x + 1, y + 1, pupil)
        put(x, y, hi)
    elif p["px"] >= 24:        # 2x2 pupil, catch-light top-left
        put(x, y, hi); put(x + 1, y, pupil); put(x, y + 1, pupil); put(x + 1, y + 1, pupil)
    else:
        put(x, y, pupil)
    return idx


def post(ps, p, cam=None):
    ink = R.shade(p["col"]["back"], -0.3)
    R._use([ink, PUPIL])
    pal = R.Pal(R.USED)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.025, steps=1)
    idx = paint_eye(idx, pal, p, cam)
    idx = R.outer_outline(idx, pal, lit_steps=2, dark_col=pal.index(ink))
    return R.to_rgba(idx, pal), len(pal)


def render_side(fid):
    p = FF.F[fid]
    C.clear_objects()
    R.reset_materials()
    CUR["ppu"] = 16.0
    objs = FF.build_fish(fid, p, 0)
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    CUR["ppu"] = ppu
    frame0 = None
    out = []
    for fr in (0, 1):
        C.clear_objects()
        R.reset_materials()
        objs = FF.build_fish(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        if fr == 0:
            frame0 = C.world_bounds(objs)
        fx0, fx1, fz0, fz1 = frame0
        pad = 2
        w = int(math.ceil((fx1 - fx0) * ppu)) + pad * 2
        h = int(math.ceil((fz1 - fz0) * ppu)) + pad * 2
        C.ortho_camera((fx0 + fx1) / 2, (fz0 + fz1) / 2, w, h, ppu)
        ps = R.render_passes(f"fish_{fid}_{fr}")
        img, n = post(ps, p, ((fx0 + fx1) / 2, (fz0 + fz1) / 2, w, h, ppu))
        R.save_png(img, os.path.join(OUT, f"{fid}_{fr}.png"))
        out.append(img)
    print("R16 fish", fid, "px", out[0].shape[1], "colours", R.count_colours(out[0]))
    return out


def render_top(fid):
    if fid in FF.TOP_USES_SIDE:
        for fr in (0, 1):
            shutil.copyfile(os.path.join(OUT, f"{fid}_{fr}.png"), os.path.join(OUT, f"{fid}_t{fr}.png"))
        return
    p = FF.F[fid]
    C.clear_objects()
    R.reset_materials()
    CUR["ppu"] = 16.0
    objs = FF.build_fish_top(fid, p, 0, 16.0)
    bpy.context.view_layer.update()
    x0, x1, _, _ = FF.bounds_xy(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    CUR["ppu"] = ppu
    boxes = []
    for fr in (0, 1):
        C.clear_objects()
        R.reset_materials()
        objs = FF.build_fish_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        boxes.append(FF.bounds_xy(objs))
    bx0 = min(b[0] for b in boxes)
    bx1 = max(b[1] for b in boxes)
    by = max(max(abs(b[2]), abs(b[3])) for b in boxes)
    pad = 2
    w = int(math.ceil((bx1 - bx0) * ppu)) + pad * 2
    h = int(math.ceil(2 * by * ppu)) + pad * 2
    h += h % 2
    for fr in (0, 1):
        C.clear_objects()
        R.reset_materials()
        FF.build_fish_top(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        FF.top_camera((bx0 + bx1) / 2, 0.0, w, h, ppu)
        ps = R.render_passes(f"fisht_{fid}_{fr}")
        img, _ = post(ps, p)
        R.save_png(img, os.path.join(OUT, f"{fid}_t{fr}.png"))
    C.ortho_camera(0, 0, 32, 32, 16)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ids = [a for a in argv if not a.startswith("--")] or list(FF.F.keys())
    C.reset_scene()
    C.ortho_camera(0, 0, 32, 32, 16)
    for fid in ids:
        render_side(fid)
        render_top(fid)
    # deliverable sheet
    sel = ["crucian_carp", "largemouth_bass", "rainbow_trout", "red_seabream", "blue_marlin"]
    if all(os.path.exists(os.path.join(OUT, f"{i}_0.png")) for i in sel):
        imgs = [R.load_png(os.path.join(OUT, f"{i}_0.png")) for i in sel]
        top = R.load_png(os.path.join(OUT, "largemouth_bass_t0.png"))
        sh = R.flow_sheet([imgs[:4], [imgs[4], top]], (0.23, 0.31, 0.38), scale=4, pad=16)
        R.save_png(sh, os.path.join(R.OUT, "fish_sheet.png"))
    # review sheet with everything rendered so far
    allids = [i for i in FF.F.keys() if os.path.exists(os.path.join(OUT, f"{i}_0.png"))]
    imgs = []
    for i in allids:
        imgs += [R.load_png(os.path.join(OUT, f"{i}_{k}.png")) for k in ("0", "1", "t0")]
    if imgs:
        R.save_png(R.sheet(imgs, (0.23, 0.31, 0.38), scale=3, pad=4, cols=9), os.path.join(R.OUT, "work", "fish_all.png"))
    print("R16 FISH done")


if __name__ == "__main__":
    main()
