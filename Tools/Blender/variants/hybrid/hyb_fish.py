"""
hybrid - fish sprites: retro16 craft with a gentle warm key / cool shadow. Every ramp is hue-shifted with
the "lake" preset's key hue (lighter tones lean warm amber) and shadow hue (darker tones lean cool
blue), the species colours get a light teal-orange grade (FISH_GRADE), NO gloss / specular stripes, one
limited palette per fish. Geometry comes from fk_fish (read-only import); materials are replaced at
runtime (monkeypatch inside this process only) by palette-ramp versions:
  * body: per-region 3-tone hue-shifted ramps (back / side / belly) picked by banded lambert, blended
    along the belly->back axis in narrow zones that become ORDERED-DITHER transitions in post;
  * patterns (spots, bands, stripes, mottles) are shaded with their own 3-tone ramp;
  * scales: crisp staggered scale rows (darker tone of the local region, ~3 px cells);
  * fins: 3-tone ramp with dark fin rays every ~2.5 px.
Post: quantise (+Bayer 4x4) -> despeckle (not the dither) -> inner contour lines -> selective outline.

Outputs: _tmp/variants/hybrid/fish/<id>_0.png, <id>_1.png, <id>_t0.png, <id>_t1.png (same pixel sizes as
         Assets/Resources/Sprites/Fish), _tmp/variants/hybrid/fish_sheet.png (+ work/fish_all.png)
Run: blender -b --python variants/hybrid/hyb_fish.py [-- id1 id2 ...]
"""
import os
import sys
import math
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
import fk_common as C  # noqa: E402
import fk_fish as FF  # noqa: E402

PR = R.use_preset("lake")
FISH_GRADE = 0.35     # gentle: species colours stay recognisable in the catch UI / aquarium
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
WATER_TINT, WATER_DEEP = PR.water["tint"], PR.water["deep"]   # stage_lake.json (sheet preview only)


def graded(p):
    """Species colours with the preset's light teal-orange grade (geometry keys untouched)."""
    G = R.grade_hex
    q = dict(p)
    q["col"] = {k: G(v, amount=FISH_GRADE) for k, v in p["col"].items()}
    q["pat"] = [dict(pt, color=G(pt["color"], amount=FISH_GRADE)) if "color" in pt else pt for pt in p.get("pat", [])]
    if p.get("finspots"):
        q["finspots"] = G(p["finspots"], amount=FISH_GRADE)
    return q
SMALL_PX = 36     # below this: no mottle / scale rows (they turn into 1 px noise) - 3 bands + one species mark
DITHER_PX = 40     # only fish of this length or more get ordered-dither blends
MARK_ORDER = ["stripe", "bands", "region", "blotch", "mottle", "spots"]


def simplify(p):
    """Small-sprite version of a species: drop scales, keep ONE species mark; a mottle becomes a ~2 px
    dark lateral band (e.g. the largemouth bass), stripes are at least ~1 px tall."""
    q = dict(p)
    hpx = max(4.0, p["px"] * p["h"] * 2.1)
    cand = [pt for pt in p.get("pat", []) if pt["type"] != "scales"]
    cand.sort(key=lambda pt: MARK_ORDER.index(pt["type"]))
    keep = []
    if cand:
        pt = dict(cand[0])
        if pt["type"] == "mottle":
            c = 0.5 * (pt.get("v0", 0.3) + min(1.0, pt.get("v1", 0.7)))
            pt = dict(type="stripe", color=pt["color"], center=min(0.62, c), width=1.0 / hpx,
                      t0=max(0.08, pt.get("t0", 0.08)), t1=min(0.86, pt.get("t1", 0.86)))
        if pt["type"] == "stripe":
            pt["width"] = max(pt["width"], 0.55 / hpx)
        keep = [pt]
    q["pat"] = keep
    return q


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
    big, mid = p["px"] >= 44, p["px"] >= 24
    rows = (y - 1, y, y + 1) if big else ((y, y + 1) if mid else (y,))
    right = x + 1 if (big or mid) else x
    # keep at least 1 px of head between the eye and the snout edge
    for rr in rows:
        if 0 <= rr < H and (idx[rr] >= 0).any():
            xmax = int(np.flatnonzero(idx[rr] >= 0).max())
            if right > xmax - 1:
                x -= right - (xmax - 1)
                right = xmax - 1

    def put(xx, yy, v):
        if 0 <= yy < H and 0 <= xx < W and idx[yy, xx] >= 0:
            idx[yy, xx] = v
    if big:                    # 3x3 eye: ring + 2x2 pupil with a catch-light
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                put(x + dx, y + dy, ring)
        put(x, y, pupil); put(x + 1, y, pupil); put(x, y + 1, pupil); put(x + 1, y + 1, pupil)
        put(x, y, hi)
    elif mid:                  # 2x2 pupil, catch-light top-left
        put(x, y, hi); put(x + 1, y, pupil); put(x, y + 1, pupil); put(x + 1, y + 1, pupil)
    else:
        put(x, y, pupil)
    return idx


def post(ps, p, cam=None):
    ink = R.shade(p["col"]["back"], -0.28)       # hue-shifted dark of the back colour, never black
    R._use([ink, PUPIL])
    pal = R.Pal(R.USED)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=p["px"] >= DITHER_PX)
    idx = R.despeckle(idx, ps["id"], protect=~solid if p["px"] >= DITHER_PX else None, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.025, steps=1)
    idx = paint_eye(idx, pal, p, cam)
    idx = R.outer_outline(idx, pal, lit_steps=2, dark_col=pal.index(ink))
    return R.to_rgba(idx, pal), len(pal)


# ---- underwater shadow (top view): no outline, 2 tones, forked tail, 1-2 px pectoral stubs
def shadow_cols(p):
    return R.hexrgb(R.shade(p["col"]["back"], -0.25)), R.hexrgb(R.shade(p["col"]["back"], -0.35))


def _dilate(m, n):
    out = m.copy()
    for _ in range(n):
        o = out.copy()
        o[1:] |= out[:-1]
        o[:-1] |= out[1:]
        o[:, 1:] |= out[:, :-1]
        o[:, :-1] |= out[:, 1:]
        out = o
    return out


def shadow_img(a, body, fin, p, ridge=True):
    """2-tone shadow sprite from masks: body tone everywhere, darker back ridge along the spine
    (top view) or darker fins (side-view species)."""
    cb, cr = shadow_cols(p)
    H, W = a.shape
    out = np.zeros((H, W, 4), np.float32)
    out[a, :3] = cb
    out[a, 3] = 1.0
    if ridge:
        cols = np.flatnonzero(body.any(0))
        if len(cols):
            c0, c1 = cols.min(), cols.max()
            for c in range(c0 + 1, c1 - 1):
                rr = np.flatnonzero(body[:, c])
                if len(rr) < 3:
                    continue
                mid = 0.5 * (rr.min() + rr.max())
                if len(rr) >= 6:
                    sel = [int(math.floor(mid)), int(math.floor(mid)) + 1]
                else:
                    sel = [int(round(mid))]
                for r in sel:
                    out[r, c, :3] = cr
    else:
        out[fin & a, :3] = cr
    return out


def top_kinds(objs):
    for ob in objs:
        ob["kind"] = ob.name.split(".")[0].lower()
    return objs


def render_side(fid):
    p0 = graded(dict(FF.F[fid], _id=fid))
    p = simplify(p0) if p0["px"] < SMALL_PX else p0
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
        objs = top_kinds(FF.build_fish(fid, p, fr, ppu))
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
        if fid in FF.TOP_USES_SIDE:
            kinds = ps["kinds"]
            kid = np.vectorize(lambda i: kinds.get(int(i), ""))(ps["id"])
            body = (kid == "body") | (kid == "eye") | (kid == "pupil")
            sh = shadow_img(ps["a"], body, ps["a"] & ~body, p, ridge=False)
            R.save_png(sh, os.path.join(OUT, f"{fid}_t{fr}.png"))
        out.append(img)
    print("HYB fish", fid, "px", out[0].shape[1], "colours", R.count_colours(out[0]))
    return out


def _pip(X, Y, poly):
    """Even-odd point-in-polygon test for arrays of points."""
    inside = np.zeros(X.shape, bool)
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        cond = (y1 > Y) != (y2 > Y)
        xint = (x2 - x1) * (Y - y1) / ((y2 - y1) if abs(y2 - y1) > 1e-12 else 1e-12) + x1
        inside ^= cond & (X < xint)
    return inside


def _xform(pts, ang, ox, oy):
    c, s = math.cos(ang), math.sin(ang)
    return [(ox + x * c - y * s, oy + x * s + y * c) for x, y in pts]


def top_shadow(p, fr, cx, w, h, ppu):
    """Top-down underwater shadow drawn from the fk body profile (same length, width x1.45, same swim
    bend and tail swing as fk_fish.build_fish_top) but rasterised directly: a clear forked tail with
    lobes >= ~1.5 px, 1-2 px pectoral stubs, bill / barbels as 1 px lines, 2 tones, no outline."""
    q = dict(p)
    q["w"] = min(p.get("w", 0.55) * 1.45, 0.95)      # fk widens x1.45; capped so sharks / anglers stay fish-shaped
    b = FF.Body(q)
    sgn = 1 if fr == 0 else -1
    px1 = 1.0 / ppu

    def spine(t):
        return sgn * 0.075 * ((0.5 - t) / 0.5) ** 2 if t < 0.5 else 0.0

    ts = np.linspace(0.0, 1.0, 72)
    upper = [(b.x(t), spine(t) + max(b.half_w(t), 0.45 * px1 if t < 0.999 else 0.0)) for t in ts]
    lower = [(b.x(t), spine(t) - max(b.half_w(t), 0.45 * px1 if t < 0.999 else 0.0)) for t in ts[::-1]]
    body_poly = upper + lower
    tail = p["tail"]
    tl = min(max(tail["len"], 3.6 * px1), max(tail["len"], 3.6 * px1) if p["px"] >= 40 else 5.5 * px1)
    sp = max(tail.get("span", 1.0) * b.H * 0.9, 2.8 * px1)
    hb = b.half_w(0.0) * 0.9 + 0.4 * px1
    typ = tail["type"]
    if typ in ("shark", "coel", "clavus"):
        n = 0.62
    else:
        n = 0.48                                  # forked, notch at ~half the tail length
    fork = [(0.03, hb), (-tl * 0.55, sp * 0.72), (-tl, sp), (-tl * 0.72, sp * 0.36), (-tl * n, 0.0),
            (-tl * 0.72, -sp * 0.36), (-tl, -sp), (-tl * 0.55, -sp * 0.72), (0.03, -hb)]
    tail_poly = _xform(fork, math.radians(sgn * 24), b.x(0.0), spine(0.0))
    # pectoral stubs: sweep back and out ~1.5 px from the body edge
    tp = p.get("pect_t", min(0.78, p["peak"] + 0.12))
    xe = b.x(tp)
    stubs = []
    for side in (1, -1):
        ye = spine(tp) + side * b.half_w(tp)
        stubs.append([(xe + 0.6 * px1, ye - side * 0.6 * px1), (xe - 1.9 * px1, ye + side * 1.7 * px1),
                      (xe - 1.4 * px1, ye - side * 0.6 * px1)])
    cols = np.arange(w) + 0.5
    rows = np.arange(h) + 0.5
    X = cx + (cols[None, :] - w / 2) / ppu
    Y = (h / 2 - rows[:, None]) / ppu
    X = np.broadcast_to(X, (h, w))
    Y = np.broadcast_to(Y, (h, w))
    body = _pip(X, Y, body_poly)
    a = body | _pip(X, Y, tail_poly)
    for st in stubs:
        a |= _pip(X, Y, st)

    def to_px(x, y):
        return (x - cx) * ppu + w / 2, h / 2 - y * ppu

    def pline(x0, y0, x1, y1):
        c0, r0 = to_px(x0, y0)
        c1, r1 = to_px(x1, y1)
        nstep = int(max(abs(c1 - c0), abs(r1 - r0))) + 1
        for k in range(nstep + 1):
            tt = k / nstep
            c = int(math.floor(c0 + (c1 - c0) * tt))
            r = int(math.floor(r0 + (r1 - r0) * tt))
            if 0 <= r < h and 0 <= c < w:
                a[r, c] = True
    if p.get("bill"):
        pline(b.x(0.98), 0.0, b.x(1.0) + p["bill"], 0.0)
    for bar in p.get("barbels", []):
        at = bar.get("at", 0.985)
        L = min(bar["len"], 3.2 * px1)             # whisker stubs only: long barbels read as a bar
        for side in (1, -1):
            pline(b.x(at), side * b.half_w(at), b.x(at) - L * 0.4, side * (b.half_w(at) + L * 0.8))
    # 2 tones: body everywhere, darker back ridge along the spine (1 px, 2 px where the body is >= 6 px wide)
    cb, cr = shadow_cols(p)
    out = np.zeros((h, w, 4), np.float32)
    out[a, :3] = cb
    out[a, 3] = 1.0
    for c in range(w):
        xw = cx + (c + 0.5 - w / 2) / ppu
        t = xw + 0.5
        if not 0.1 <= t <= 0.9:
            continue
        yc = spine(t)
        width_px = 2 * b.half_w(t) * ppu
        rr = h / 2 - yc * ppu
        sel = [int(math.floor(rr - 0.5)), int(math.floor(rr - 0.5)) + 1] if width_px >= 6 else [int(math.floor(rr))]
        for r in sel:
            if 0 <= r < h and body[r, c]:
                out[r, c, :3] = cr
    return out


# ---- opt-in top view of an eel-like fish (fk_fish `top=dict(tail="taper", wave=N[, amp, stub])`, only
# freshwater_eel sets it): no fork polygon, the body tapers to a 1 px tip, the spine is an S-curve of `wave`
# half-waves along the whole length (frames 0/1 in opposite phase), pectoral stubs kept. Every other species
# goes through top_shadow and the rest of render_top unchanged (their sprites stay byte-identical).
def taper_opt(p):
    top = p.get("top")
    return top if top and top.get("tail") == "taper" else None


def _taper_geom(p, ppu):
    """-> (b, x_tip, x_nose, spine(x, sgn), hw(x)) in model units for the taper top view."""
    top = taper_opt(p)
    q = dict(p)
    q["w"] = min(p.get("w", 0.55) * 1.45, 0.95)
    b = FF.Body(q)
    x_tip = b.x(0.0) - p["tail"]["len"] * 0.6           # the round tail fin continues the taper
    x_nose = b.x(1.0)
    n = top.get("wave", 2)
    amp = top.get("amp", 1.5) / ppu                     # S-curve amplitude at the tail, px -> units

    def s_of(x):
        return (x - x_tip) / (x_nose - x_tip)           # 0 = tail tip, 1 = snout

    def spine(x, sgn):
        s = min(1.0, max(0.0, s_of(x)))
        env = 0.3 + 0.7 * (1.0 - s)                     # the head swings least, the tail most
        return sgn * amp * env * math.sin(math.pi * n * (1.0 - s))

    def hw(x):
        t = x + 0.5
        tip = 0.5 / ppu                                 # 1 px wide at the very tip
        if t <= 0.0:
            h0 = b.half_w(0.0) * 0.45
            k = (x - x_tip) / (b.x(0.0) - x_tip)
            return max(tip, tip + (h0 - tip) * k)
        return max(tip, b.half_w(t) * (0.45 + 0.55 * FF.smoothstep(0.0, 0.6, t)))
    return b, x_tip, x_nose, spine, hw


def _taper_shape(p, fr, ppu):
    """Polygons (body, stubs) in model units, frame fr."""
    top = taper_opt(p)
    b, x_tip, x_nose, spine, hw = _taper_geom(p, ppu)
    sgn = 1 if fr == 0 else -1
    xs = np.linspace(x_tip, x_nose, 96)
    upper = [(x, spine(x, sgn) + hw(x)) for x in xs]
    lower = [(x, spine(x, sgn) - hw(x)) for x in xs[::-1]]
    px1 = 1.0 / ppu
    st = top.get("stub", 1.0)                           # pectoral stub size (1 = the default stubs' 1.7 px)
    tp = p.get("pect_t", min(0.78, p["peak"] + 0.12))
    xe = b.x(tp)
    stubs = []
    for side in (1, -1):
        ye = spine(xe, sgn) + side * hw(xe)
        stubs.append([(xe + 0.6 * px1 * st, ye - side * 0.6 * px1), (xe - 1.9 * px1 * st, ye + side * 1.7 * px1 * st),
                      (xe - 1.4 * px1 * st, ye - side * 0.6 * px1)])
    return upper + lower, stubs


def taper_canvas(p):
    """Canvas of the taper top view: (cx, w, h, ppu); width px + 2 like every sprite, height even (spine centred)."""
    b, x_tip, x_nose, spine, hw = _taper_geom(p, 16.0)
    ppu = (p["px"] - 2) / (x_nose - x_tip)
    ymax = 0.0
    for fr in (0, 1):
        body, stubs = _taper_shape(p, fr, ppu)
        ymax = max([ymax] + [abs(y) for _, y in body] + [abs(y) for s in stubs for _, y in s])
    pad = 2
    w = int(math.ceil((x_nose - x_tip) * ppu)) + pad * 2
    h = int(math.ceil(2 * ymax * ppu)) + pad * 2
    h += h % 2
    return (x_tip + x_nose) / 2, w, h, ppu


def top_shadow_taper(p, fr, cx, w, h, ppu):
    body_poly, stubs = _taper_shape(p, fr, ppu)
    b, x_tip, x_nose, spine, hw = _taper_geom(p, ppu)
    sgn = 1 if fr == 0 else -1
    cols = np.arange(w) + 0.5
    rows = np.arange(h) + 0.5
    X = np.broadcast_to(cx + (cols[None, :] - w / 2) / ppu, (h, w))
    Y = np.broadcast_to((h / 2 - rows[:, None]) / ppu, (h, w))
    body = _pip(X, Y, body_poly)
    # every column from the tip to the snout keeps at least 1 px on the spine, so the thin taper never breaks up
    for c in range(w):
        xw = cx + (c + 0.5 - w / 2) / ppu
        if x_tip <= xw <= x_nose and not body[:, c].any():
            r = int(math.floor(h / 2 - spine(xw, sgn) * ppu))
            if 0 <= r < h:
                body[r, c] = True
    a = body.copy()
    for st in stubs:
        a |= _pip(X, Y, st)
    cb, cr = shadow_cols(p)
    out = np.zeros((h, w, 4), np.float32)
    out[a, :3] = cb
    out[a, 3] = 1.0
    # darker back ridge along the S spine (1 px; 2 px where the body is >= 6 px wide), t 0.1..0.9 as in top_shadow
    for c in range(w):
        xw = cx + (c + 0.5 - w / 2) / ppu
        t = xw + 0.5
        if not 0.1 <= t <= 0.9:
            continue
        rr = h / 2 - spine(xw, sgn) * ppu
        sel = [int(math.floor(rr - 0.5)), int(math.floor(rr - 0.5)) + 1] if 2 * hw(xw) * ppu >= 6 else [int(math.floor(rr))]
        for r in sel:
            if 0 <= r < h and body[r, c]:
                out[r, c, :3] = cr
    return out


def render_top(fid):
    if fid in FF.TOP_USES_SIDE:
        return                              # written by render_side (2-tone side silhouette)
    p = graded(FF.F[fid])
    if taper_opt(p):
        cx, w, h, ppu = taper_canvas(p)
        for fr in (0, 1):
            R.save_png(top_shadow_taper(p, fr, cx, w, h, ppu), os.path.join(OUT, f"{fid}_t{fr}.png"))
        return
    C.clear_objects()
    R.reset_materials()
    CUR["ppu"] = 16.0
    objs = FF.build_fish_top(fid, p, 0, 16.0)
    bpy.context.view_layer.update()
    x0, x1, _, _ = FF.bounds_xy(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
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
    for fr in (0, 1):                        # same canvas as fk: file sizes and centring unchanged
        img = top_shadow(p, fr, (bx0 + bx1) / 2, w, h, ppu)
        R.save_png(img, os.path.join(OUT, f"{fid}_t{fr}.png"))
    C.clear_objects()
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
    sel = ["crucian_carp", "largemouth_bass", "rainbow_trout", "red_seabream", "blue_marlin", "great_white"]
    if all(os.path.exists(os.path.join(OUT, f"{i}_0.png")) for i in sel):
        imgs = [R.load_png(os.path.join(OUT, f"{i}_0.png")) for i in sel]
        # two top views (the underwater shadow sprites) on a tile of the lake's play-area water, drawn the
        # game's way: colour lerp 70 % towards waterDeep, 0.8 alpha
        tiles = []
        for tid in ("largemouth_bass_t0", "red_seabream_t1"):
            top = R.load_png(os.path.join(OUT, f"{tid}.png"))
            tile = np.zeros((top.shape[0] + 8, top.shape[1] + 8, 4), np.float32)
            tile[..., :3] = R.hexrgb(WATER_TINT)
            tile[..., 3] = 1.0
            spr = top.copy()
            spr[..., :3] = spr[..., :3] * 0.3 + R.hexrgb(WATER_DEEP) * 0.7
            R.over(tile, spr, 4, 4, alpha=0.8)
            tiles.append(tile)
        sh = R.flow_sheet([imgs[:4], imgs[4:6], tiles], (0.23, 0.31, 0.38), scale=4, pad=16)
        R.save_png(sh, os.path.join(R.OUT, "fish_sheet.png"))
    # review sheet with everything rendered so far
    allids = [i for i in FF.F.keys() if os.path.exists(os.path.join(OUT, f"{i}_0.png"))]
    imgs = []
    for i in allids:
        imgs += [R.load_png(os.path.join(OUT, f"{i}_{k}.png")) for k in ("0", "1", "t0")]
    if imgs:
        R.save_png(R.sheet(imgs, (0.23, 0.31, 0.38), scale=3, pad=4, cols=9), os.path.join(R.OUT, "work", "fish_all.png"))
    print("HYB FISH done")


if __name__ == "__main__":
    main()
