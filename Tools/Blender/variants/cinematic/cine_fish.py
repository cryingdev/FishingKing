"""
FishingKing - cinematic variant of the fish sprites.

Species table, body loft, fin outlines and patterns come from fk_fish.py (imported, not modified).
build_fish is COPIED here so materials can know the pixel scale and tails can change shape:

side view (<id>_0/_1):
  * 3 values only: back (warm key), flank (neutral), belly (cool shade), aligned with the colour bands so
    light never adds extra horizontal stripes; the cool water bounce only on the lowest 1-2 rows
  * glint: after the base multiply, a mix towards #fff2dc (0.55) in a 3-6 px window right behind the gill
  * tails: square tails get a concave trailing edge; every tail/fin has 1 px rays over darker membrane
  * OKLCh chroma clamped per sprite (0.11, 0.15 for showcase species), per-sprite palette, cool outline

top view (<id>_t0/_t1): drawn in numpy as a top-down spindle - widest 30-35% from the snout, narrow tail
  stalk, symmetric forked tail swinging per frame, two pectoral stubs, bills/barbels kept; 3 tones (dark
  spine, body, 1 px lighter edge) desaturated 70% towards #1c3438 so it reads as a shadow in the water.

Run:  blender -b --python Tools/Blender/variants/cinematic/cine_fish.py [-- id1 id2 ...]
Out:  _tmp/variants/cinematic/fish/<id>_0.png, <id>_1.png, <id>_t0.png, <id>_t1.png, fish_sheet.png
"""
import sys
import os
import math
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
SHOWCASE = {"golden_carp", "crystal_koi"}      # allowed a little more chroma (0.15)
CHROMA = 0.1

# body light per colour band (multipliers): belly in cool shade, flank neutral, back warm key
BACK_L, FLANK_L, BELLY_L = "#fff0dc", "#d6d2d0", "#a9b1c2"
COOL_BOUNCE = "#8aaabb"
GLINT = "#fff2dc"


def _lin(h):
    return K.lin4(h)


def body_material(p, ppu, name="BodyMat"):
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
    light = nb.ramp(v, [(0.0, _lin(BELLY_L)), (bl, _lin(FLANK_L)), (0.72, _lin(BACK_L))])
    geo = nb.node("ShaderNodeNewGeometry")
    n = geo.outputs["Normal"]
    nz = nb.sep(n)[2]
    low = nb.math("LESS_THAN", nz, -0.8)                       # the lowest 1-2 rows only
    light = nb.mix(nb.math("MULTIPLY", low, 0.55), light, _lin(COOL_BOUNCE))
    out = nb.mix(1.0, base, light, "MULTIPLY")
    # glint: small, after the multiply, just behind the gill cover
    body_px = ppu  # body length is 1 unit
    L = min(max(0.18 * body_px, 3.0), 6.0) / max(body_px, 1.0)
    t1 = 1 - (1 - p["peak"]) * 0.85
    t0 = t1 - L
    win = nb.math("MULTIPLY", nb.math("GREATER_THAN", t, t0), nb.math("LESS_THAN", t, t1))
    # threshold chosen so the glint band is ~1 px tall whatever the fish size
    hh_px = max(p["h"] * ppu, 2.0)
    dlt = min(0.55 / hh_px, 0.2)
    hv = nb.math("GREATER_THAN", nb.vmath("DOT_PRODUCT", n, tuple(HALF)), 0.9665 * math.cos(dlt))
    g = nb.math("MULTIPLY", win, hv)
    g = nb.math("MULTIPLY", g, nb.math("GREATER_THAN", v, 0.5))
    k = 0.55 * min(1.0, 0.55 + 0.5 * p.get("shine", 0.3))
    out = nb.mix(nb.math("MULTIPLY", g, k), out, _lin(GLINT))
    nb.output_emission(out, p.get("emit", 1.0))
    return m


def fin_material(p, ppu, kind="fin", name="FinMat"):
    """Warm translucent fin, 1 px lighter rays over darker membrane. kind: 'tail' (rays radiate from the
    peduncle, object coords) or 'fin' (parallel, raked rays)."""
    col = p["col"]["fin"]
    spots = p.get("finspots")
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    base = nb.rgb(C.lin(col))
    tc = nb.node("ShaderNodeTexCoord")
    co = tc.outputs["Object"]
    sx, sy, sz = nb.sep(co)
    if spots:
        vo = nb.node("ShaderNodeTexVoronoi")
        nb.link(co, vo.inputs["Vector"])
        vo.inputs["Scale"].default_value = 26.0
        mask = nb.math("LESS_THAN", vo.outputs["Distance"], 0.2)
        base = nb.mix(mask, base, C.lin(spots))
    if kind == "tail":
        tl = p["tail"]["len"]
        ang = nb.math("ARCTAN2", sz, nb.math("SUBTRACT", 0.05, sx))
        step = 2.0 / max(tl * ppu, 2.0)             # radians between rays at the trailing edge (~2 px)
        fr = nb.math("FRACT", nb.math("MULTIPLY", ang, 1.0 / step))
    else:
        u = nb.math("MULTIPLY_ADD", nb.math("ABSOLUTE", sz), 0.35, sx)
        fr = nb.math("FRACT", nb.math("MULTIPLY", u, ppu / 2.0))
    ray = nb.math("LESS_THAN", fr, 0.5)
    base = nb.mix(1.0, base, _lin("#ffe6c8"), "MULTIPLY")   # backlit by the warm key: a touch warmer
    memb = nb.mix(1.0, base, (0.62, 0.62, 0.66, 1), "MULTIPLY")
    rayc = nb.mix(1.0, base, (1.3, 1.26, 1.18, 1), "MULTIPLY")
    base = nb.mix(ray, memb, rayc)
    geo = nb.node("ShaderNodeNewGeometry")
    d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(KEY))
    s = nb.math("MULTIPLY_ADD", nb.math("ABSOLUTE", d), 0.5, 0.5)
    shade = nb.ramp(s, [(0.0, _lin("#8e96ac")), (0.62, _lin("#ddd4ca"))])
    out = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(out, p.get("emit", 1.0))
    return m


def solid_material(hexc, name="Solid"):
    """Bills, barbels, lure stalks: 2 values (lit top / cool underside)."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    nz = nb.sep(geo.outputs["Normal"])[2]
    shade = nb.ramp(nb.math("MULTIPLY_ADD", nz, 0.5, 0.5), [(0.0, _lin("#a4acbc")), (0.55, _lin("#f0e4d4"))])
    nb.output_emission(nb.mix(1.0, nb.rgb(C.lin(hexc)), shade, "MULTIPLY"), 1.0)
    return m


def tail_outline(b, tail):
    """fk_fish tails, except square paddles get a concave (emarginate) trailing edge."""
    if tail["type"] != "square":
        return FF.tail_outline(b, tail)
    tl = tail["len"]
    ts = tail["span"] * b.H
    hb = b.p["ped"] * b.H * 0.95
    return [(0.03, hb), (-tl * 0.72, ts * 0.9), (-tl, ts), (-tl * 0.86, ts * 0.35), (-tl * 0.8, 0.0),
            (-tl * 0.86, -ts * 0.35), (-tl, -ts), (-tl * 0.72, -ts * 0.9), (0.03, -hb)]


# ----------------------------------------------------------------------------- side view (copied builder)
def build_fish(fid, p, frame, ppu=None):
    p = dict(p)
    p["_id"] = fid
    q = ppu or 16.0
    b = FF.Body(p)
    bmat = body_material(p, q)
    fmat = fin_material(p, q, "fin")
    tmat = fin_material(p, q, "tail")
    objs = [FF.build_body(b, bmat)]
    if frame == 1:
        me = objs[0].data
        for v in me.vertices:
            t = v.co.x + 0.5
            if t < 0.45:
                k = (0.45 - t) / 0.45
                v.co.y -= 0.06 * k * k
    tail = C.poly_object("Tail", tail_outline(b, p["tail"]), tmat, thickness=0.012)
    tail.location = (b.x(0.0), 0, b.zc(0.0))
    if frame == 1:
        tail.rotation_euler = (0, math.radians(-7), math.radians(58))
        tail.location.y -= 0.02
    objs.append(tail)
    for fin in p.get("dorsal", []):
        objs.append(C.poly_object("Dorsal", FF.fin_outline(b, fin, True), fmat))
    for fin in p.get("anal", []):
        objs.append(C.poly_object("Anal", FF.fin_outline(b, fin, False), fmat))
    pel = p.get("pelvic")
    if pel:
        t = pel["t"]
        fin = dict(t0=t - 0.07, t1=t + 0.02, h=pel["h"], shape=pel.get("shape", "tri"), rake=0.6)
        objs.append(C.poly_object("Pelvic", FF.fin_outline(b, fin, False), fmat, y=-0.004))
    pect = p.get("pect", 0.0)
    if pect > 0:
        t = p.get("pect_t", min(0.78, p["peak"] + 0.12))
        L = pect * b.H * 1.3
        zc = b.zc(t) - 0.25 * b.rl(t)
        pts = [(0.0, 0.03 * L), (-L * 0.35, 0.05 * L), (-L * 0.95, -L * 0.25), (-L, -L * 0.42), (-L * 0.4, -L * 0.3),
               (0.0, -0.08 * L)]
        y = -b.half_w(t) * 0.92 - 0.004
        ob = C.poly_object("Pect", pts, fmat, thickness=0.006, y=0)
        ob.location = (b.x(t), y, zc)
        objs.append(ob)
    eye = p.get("eye", 0.11)
    if eye > 0:
        et = p.get("eye_t", 1 - (1 - p["peak"]) * 0.28)
        ez_list = [b.zc(et) + p.get("eye_v", 0.3) * b.ru(et)]
        if p.get("two_eyes"):
            ez_list.append(ez_list[0] + eye * b.H * 2.6)
        r = max(eye * b.H, 0.012)
        px = 1.0 / ppu if ppu else 0.0
        big = ppu is None or r * ppu >= 1.7
        for ez in ez_list:
            ey = -b.half_w(et) * 0.95 - r * 0.2
            if big:
                white = C.add_prim("sphere", "Eye", C.toon_material("EyeW", p.get("eye_col", "#e0d6b4"), flat=True),
                                   radius=r, location=(b.x(et), ey, ez), segments=12, ring_count=8)
                white.scale = (1, 0.3, 1)
                objs.append(white)
                pr = max(r * 0.62, px * 0.8)
            else:
                pr = max(r * 0.8, px * 0.75)
            pupil = C.add_prim("sphere", "Pupil", C.toon_material("EyeP", "#0a0a12", flat=True),
                               radius=pr, location=(b.x(et) + r * 0.1, ey - r * 0.3, ez), segments=12, ring_count=8)
            pupil.scale = (1, 0.3, 1)
            objs.append(pupil)
    ml = p.get("mouth_line", 0.0)
    if ml > 0:
        tn = 0.995
        x1 = b.x(tn)
        zz = b.zc(tn) - 0.03 * b.H
        pts = [(x1 - ml * 0.7, zz - 0.01), (x1 + 0.005, zz + 0.02 * b.H), (x1 + 0.005, zz - 0.05 * b.H),
               (x1 - ml * 0.7, zz - 0.03)]
        mm = C.toon_material("Mouth", "#1a1014", flat=True)
        tmid = 1 - ml * 0.35
        objs.append(C.poly_object("Mouth", pts, mm, thickness=0.004, y=-b.half_w(tmid) * 0.9 - 0.006))
        if p.get("teeth"):
            tm = C.toon_material("Teeth", "#e8e2d0", flat=True)
            for k in range(4):
                tx = x1 - ml * 0.6 * (k + 0.5) / 4
                objs.append(C.poly_object("Tooth", [(tx - 0.015, zz), (tx + 0.015, zz), (tx, zz - 0.045)], tm,
                                          thickness=0.004, y=-b.half_w(tmid) * 0.9 - 0.01))
    for bar in p.get("barbels", []):
        at = bar.get("at", 0.985)
        x0, z0 = b.x(at), b.zc(at) + bar["z"] * b.H * b.f(at)
        ang = math.radians(bar["ang"])
        L = bar["len"]
        cur = bar.get("curve", 0.0)
        pts = []
        for k in range(6):
            s = k / 5
            a = ang + cur * s
            pts.append((x0 + math.cos(a) * L * s, -b.half_w(at) - 0.01, z0 + math.sin(a) * L * s))
        objs.append(C.tube_along("Barbel", pts, [0.009 * (1 - 0.5 * k / 5) for k in range(6)],
                                 solid_material(p["col"]["side"])))
    if p.get("bill"):
        L = p["bill"]
        tip = (b.x(1.0) + L, 0, b.zc(1.0))
        pts = [(b.x(0.96), 0, b.zc(0.96)), (b.x(1.0) + L * 0.5, 0, b.zc(1.0)), tip]
        objs.append(C.tube_along("Bill", pts, [b.H * 0.12, b.H * 0.06, 0.004], solid_material(p["col"]["back"])))
    if p.get("lure"):
        lu = p["lure"]
        t = 0.93
        base = (b.x(t), 0, b.ztop(t) - 0.01)
        L = lu["len"]
        pts = [base, (base[0] + L * 0.3, 0, base[2] + L * 0.45), (base[0] + L * 0.75, 0, base[2] + L * 0.5),
               (base[0] + L * 0.95, 0, base[2] + L * 0.3)]
        objs.append(C.tube_along("Stalk", pts, 0.012, solid_material(p["col"]["side"])))
        objs.append(C.add_prim("sphere", "Bulb", C.glow_material("Glow", lu["col"], 1.4), radius=0.05,
                               location=(pts[-1][0], -0.02, pts[-1][2] - 0.02), segments=10, ring_count=6))
    fl = p.get("finlets")
    if fl:
        fm = C.toon_material("Finlet", fl["col"], flat=True)
        for k in range(fl["n"]):
            t = fl["t0"] + (fl["t1"] - fl["t0"]) * (k + 0.5) / fl["n"]
            w = (fl["t1"] - fl["t0"]) / fl["n"] * 0.8
            for top in (True, False):
                fin = dict(t0=t - w / 2, t1=t + w / 2, h=0.35, shape="tri", rake=0.4)
                objs.append(C.poly_object("Finlet", FF.fin_outline(b, fin, top), fm, y=-0.003))
    return objs


# ----------------------------------------------------------------------------- post-process
def post(arr, fid, n_colors=18):
    a = K.harden(arr)
    a = K.grade(a, sat=0.92, split=0.6, contrast=0.05)
    a = K.clamp_chroma(a, 0.14 if fid in SHOWCASE else CHROMA)
    m = a[..., 3] > 0.5
    if m.sum() > 8:
        pal = K.kmeans_palette(a, n_colors, mask=m, weight_pow=0.7)
        q = K.quantize(a, pal)
        q[..., 3] = a[..., 3]
        a = q
    a = K.outline(a, col=(0.05, 0.08, 0.11), mul=0.42)
    return a


def render_species(fid, frames=(0, 1)):
    p = FF.F[fid]
    C.clear_objects()
    objs = build_fish(fid, p, 0)
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(objs)
    ppu = (p["px"] - 2) / (x1 - x0)
    frame0 = None
    for fr in frames:
        C.clear_objects()
        objs = build_fish(fid, p, fr, ppu)
        bpy.context.view_layer.update()
        if fr == 0:
            frame0 = C.world_bounds(objs)
        fx0, fx1, fz0, fz1 = frame0
        pad = 2
        w = int(math.ceil((fx1 - fx0) * ppu)) + pad * 2
        h = int(math.ceil((fz1 - fz0) * ppu)) + pad * 2
        C.ortho_camera((fx0 + fx1) / 2, (fz0 + fz1) / 2, w, h, ppu)
        K.save(post(K.render("fish_" + f"{fid}_{fr}"), fid), os.path.join(OUTD, f"{fid}_{fr}.png"))


# ----------------------------------------------------------------------------- top view (numpy spindle)
SHADOW_BASE = K.hexf("#1c3438")


SHADOW_LUMA = 0.19     # every species lands at the same darkness: ~20-25% under the water after the game tint


def shadow_tones(p):
    c = K.mix(K.mix(K.hexf(p["col"]["back"]), K.hexf(p["col"]["side"]), 0.3), SHADOW_BASE, 0.7)
    c = np.clip(c * SHADOW_LUMA / max(float(K.luma(c)), 1e-4), 0, 1)
    c = K.clamp_chroma(c[None, None, :], 0.045)[0, 0]      # only a hint of the species hue survives
    return dict(body=c, spine=c * 0.62, edge=np.clip(c * 1.28 + 0.02, 0, 1))


def top_geometry(p, frame, min_hw=0.0):
    """Polygons (body, tail, pectorals) + lines (bill, barbels) in body units: x tail end 0 .. snout 1,
    y lateral. Returns (polys, lines, spine, extents)."""
    Hh = p["h"]
    hw_max = max(min(max(p.get("w", 0.55) * 1.45 * Hh, 0.075), 0.13), min_hw)
    tp = min(max(0.64 + 0.25 * (p["peak"] - 0.5), 0.6, ), 0.74)
    pedw = min(max(p["ped"] * 0.55, 0.14), 0.4)
    ke = 1.2 + p["nose"] * 0.9
    sgn = 1.0 if frame == 0 else -1.0
    A = 0.05

    def hw(t):
        if t >= tp:
            u = (t - tp) / (1 - tp)
            return hw_max * max(1 - u ** ke, 0.0) ** 0.55
        u = (tp - t) / tp
        return hw_max * (pedw + (1 - pedw) * max(1 - u ** 1.8, 0.0))

    def yc(t):
        return sgn * A * max(0.0, (0.55 - t) / 0.55) ** 2

    ts = np.linspace(0, 1, 41)
    left = [(t, yc(t) + hw(t)) for t in ts]
    right = [(t, yc(t) - hw(t)) for t in ts[::-1]]
    polys = [left + right]
    # tail: symmetric, in the frame of the tail stalk, swung with the body
    tail = p["tail"]
    tl = tail["len"] * 0.95
    span = max(tail["span"] * Hh * 0.85, hw_max * 0.9)
    typ = tail["type"]
    pw = hw(0.0)
    if typ in ("fork", "deep", "lunate", "shark"):
        notch = {"fork": 0.5, "deep": 0.42, "lunate": 0.38, "shark": 0.45}[typ]
        half = [(0.0, pw), (-tl * 0.5, span * 0.62), (-tl, span), (-tl * 0.8, span * 0.62), (-tl * notch, 0.0)]
    elif typ == "square":
        half = [(0.0, pw), (-tl * 0.8, span * 0.9), (-tl, span), (-tl * 0.86, span * 0.3), (-tl * 0.8, 0.0)]
    elif typ == "coel":
        half = [(0.0, pw), (-tl * 0.5, span * 0.5), (-tl * 1.1, span * 0.15), (-tl * 1.2, 0.0)]
    else:  # round / clavus
        half = [(0.0, pw)] + [(-tl * (0.5 + 0.5 * math.cos(a)), span * math.sin(a))
                              for a in np.linspace(1.2, 0.0, 6)]
    full = half + [(x, -y) for x, y in half[::-1][1:]]
    dy = -2 * sgn * A / 0.55                    # slope of the centreline at the stalk
    ang = 0.6 * math.atan(dy) - sgn * math.radians(3)
    ca, sa = math.cos(ang), math.sin(ang)
    polys.append([(x * ca - y * sa, yc(0.0) + x * sa + y * ca) for x, y in full])
    # pectoral stubs
    tpc = min(0.82, tp + 0.08)
    Lp = min(max(p.get("pect", 0.3) * Hh * 1.1, 0.05), 0.11)
    for s in (1, -1):
        e = yc(tpc) + s * hw(tpc) * 0.9
        polys.append([(tpc + 0.03, e), (tpc - 0.06, e), (tpc - 0.12, e + s * Lp)])
    lines = []
    if p.get("bill"):
        lines.append([(0.99, 0.0), (1.0 + p["bill"] * 0.9, 0.0)])
    for bar in p.get("barbels", []):
        L = bar["len"]
        if L < 0.1:
            continue
        L = min(L, 0.2) * 0.6
        for s in (1, -1):
            lines.append([(0.98, s * hw(0.97) * 0.5), (1.0 + 0.3 * L, s * (hw(0.97) * 0.6 + 0.5 * L)),
                          (1.0 + 0.35 * L, s * (hw(0.97) * 0.6 + L))])
    spine = [(t, yc(t)) for t in np.linspace(0.2, 0.86, 12)]
    xs = [x for poly in polys for x, _ in poly] + [x for ln in lines for x, _ in ln]
    ys = [abs(y) for poly in polys for _, y in poly] + [abs(y) for ln in lines for _, y in ln]
    return polys, lines, spine, (min(xs), max(xs), max(ys)), hw_max


def top_sprite(fid, p, frame, dims=None):
    polys, lines, spine, (xmin, xmax, ymax), hw_max = top_geometry(p, frame)
    s = (p["px"] - 2) / (xmax - xmin)
    if hw_max * s < 1.6:                           # never thinner than ~3-4 px: widen, keep the length
        polys, lines, spine, (xmin, xmax, ymax), hw_max = top_geometry(p, frame, 1.6 / s)
        s = (p["px"] - 2) / (xmax - xmin)
    pad = 2
    if dims is None:
        w = int(math.ceil((xmax - xmin) * s)) + pad * 2
        h = int(math.ceil(2 * ymax * s)) + pad * 2
        h += 1 - h % 2                             # odd height: the spine sits on the middle row
    else:
        w, h = dims
    cy = h / 2.0

    def P2(x, y):
        return (pad + (x - xmin) * s, cy - y * s)

    mask = np.zeros((h, w), bool)
    for poly in polys:
        mask |= K.fill_poly(h, w, [P2(x, y) for x, y in poly])
    for ln in lines:
        for (x, y) in K.polyline_pixels([P2(*q) for q in ln]):
            if 0 <= y < h and 0 <= x < w:
                mask[y, x] = True
    tones = shadow_tones(p)
    img = np.zeros((h, w, 4), np.float32)
    img[mask, :3] = tones["body"]
    img[mask, 3] = 1
    edge = mask & ~K.erode(mask, 4)
    img[edge, :3] = tones["edge"]
    for (x, y) in K.polyline_pixels([P2(*q) for q in spine]):
        if 0 <= y < h and 0 <= x < w and mask[y, x] and not edge[y, x]:
            img[y, x, :3] = tones["spine"]
    return img, (w, h)


def top_from_side(fid, p, fr):
    """Flounder / sunfish: their broad side is what you see from above - same 3-tone shadow treatment."""
    side = K.load(os.path.join(OUTD, f"{fid}_{fr}.png"))
    mask = side[..., 3] > 0.5
    tones = shadow_tones(p)
    img = np.zeros(side.shape, np.float32)
    img[mask, :3] = tones["body"]
    img[mask, 3] = 1
    core = K.erode(K.erode(K.erode(mask, 4), 4), 4)
    img[core, :3] = K.mix(tones["body"], tones["spine"], 0.6)
    edge = mask & ~K.erode(mask, 4)
    img[edge, :3] = tones["edge"]
    return img


def render_species_top(fid):
    p = FF.F[fid]
    if fid in FF.TOP_USES_SIDE:
        for fr in (0, 1):
            K.save(top_from_side(fid, p, fr), os.path.join(OUTD, f"{fid}_t{fr}.png"))
        return
    # both frames share one canvas
    _, d0 = top_sprite(fid, p, 0)
    _, d1 = top_sprite(fid, p, 1)
    dims = (max(d0[0], d1[0]), max(d0[1], d1[1]))
    for fr in (0, 1):
        img, _ = top_sprite(fid, p, fr, dims)
        K.save(img, os.path.join(OUTD, f"{fid}_t{fr}.png"))


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


def all_sheet(ids, out, scale=3, cols=6, bg=(0.42, 0.42, 0.44), suffix="_0"):
    imgs = [K.load(os.path.join(OUTD, f"{i}{suffix}.png")) for i in ids]
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
        all_sheet(ids, os.path.join(K.OUT, "fish_all_top.png"), scale=4, bg=(0.16, 0.3, 0.32), suffix="_t0")
    print("FISH done")


if __name__ == "__main__":
    main()
