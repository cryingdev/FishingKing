"""
FishingKing - time of day, tides and currents: the HUD icons and the moving-water FX sprites
(Docs/time_currents_spec.md sections 10 and 11). Separate from fk_items.py (it only borrows its icon helpers).

Run:  blender -b --python Tools/Blender/fk_tod.py [-- ui fx sheet] [dry]      (no group = all three)
      dry: write the sprites to _tmp/tod/ui and _tmp/tod/world instead of Assets (review without a Unity import)

UI  -> Assets/Resources/Sprites/UI (drawn at 2x in the HUD like the lure action chips; 1 px hue-shifted outline)
  tod_dawn / tod_day / tod_evening / tod_night     12 px   clock panel: pink half sun rising with rays on a pale
                                                           horizon / high sun with 8 rays / orange half sun with a
                                                           dusky cloud streak on a purple horizon / crescent + star
  tod_<period>_s                                    8 px   small (map species chip, toast)
  tide_flood / tide_ebb / tide_high / tide_low     10 px   들물 ▲ / 날물 ▼ / 만조 ━ / 간조 ━
  tide_bar_bg                                     28x8     level bar frame, the well is the inner 24x4 at (2, 2) px
  tide_bar_fill                                   24x4     level water: Image.Type.Filled, Horizontal, origin Left
  cur_arrow_0..7                                   9 px    current / wind arrow, k x 45 deg counter-clockwise from
                                                           screen right (0 right, 2 up = away, 4 left, 6 down = towards
                                                           the angler); neutral light grey: tint with Image.color
  cur_arrow_s_0..7                                 7 px    the same, small (sea row beside the tide bar)
  cur_pip_on / cur_pip_off                        3x3      current strength pips (no outline)
  cur_chevron                                     9x7      the fight strip's ">>" (7x5 glyph + outline); flipX = left
World -> Assets/Resources/Sprites/World (16 PPU, pivot = centre, even sizes so the pivot sits on a pixel corner)
  Tint convention: WHITE pixels take SpriteRenderer.color (WaterFx lit / lighter / foam of the current look), BLACK
  pixels with alpha 0.25 / 0.5 are the shade (they darken the water under them and keep its hue). Every alpha is a
  quarter step. Debris sprites carry their own colours (multiply by the look's actorTint at night).
  fx_flow_<len>_<s>        flow line with a head, flowing to screen RIGHT (flipX for left): sea / ocean / lake gusts
  fx_streak_<len>_<s>      symmetric crest dash sliding up / down the screen: the stream
                           len 4 / 7 / 10 / 14 px; s 1 weak, 2 moving, 3 strong / surge (crest + trough + spray)
  fx_foamline_<len>_f0/f1  broken foam line (8 / 14 / 20 px), 2 shimmer frames: seams, tetrapods, rocks
  fx_foampatch_<s|m|l>_f0/f1   foam clumps / patches, 2 frames (bubbles popping)
  fx_fleck_<1|2|3>_f0/f1   stream foam flecks (1 / 2 / 3-4 px)
  fx_slick_24 / _40        slack-water slick (3 glassy rows, alpha 0.25): 만조 / 간조
  fx_catspaw_0/1 (40x12), fx_catspaw_s_0/1 (24x8)   gust cat's paw: dark ripple field + glints
  fx_wake_<down|up|side>_f0..3        float wake + bow pillow, near (24x12); trails down (towards the camera) / up /
  fx_wake_<down|up|side>_s_f0..3      to the right (flipX = left); far (12x8). Anchor = the sprite centre = the
                                      float's waterline; the loop runs the dashes outward along the arms
  fx_leaf_<yellow|brown|green|dead>_f0..3   spinning leaf, near (6x4); fx_leaf_<col>_s_f0/f1 far (2x2: 2x1 leaf + shade)
  fx_twig_f0/f1, fx_weed_f0..2 (10x4), fx_weed_s_f0..2 (6x2), fx_tuft_0/1/2 (4x2: upright / top leaning / combed
  flat, towards screen right, root = column 1), fx_sargassum_<s|m|l>_f0/f1, fx_driftwood, fx_pollen_<s|m|l>,
  fx_duckweed_<s|m|l>
Preview -> Tools/Blender/_tmp/tod/fx_ui_sheet.png: icons 8x (+ HUD row mocks 4x), every FX sprite 6x on its stage's
water by day and (row below) at night with the Unity tint rules, then per stage the game view with hand-placed FX at
2x and a 4x zoom on them.
"""
import sys
import os
import math
import random
import bmesh
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402
import fk_items as I   # noqa: E402  (extruded: the icon shapes with a bevel, like the lure action chips)

UI = os.path.join(C.SPRITES, "UI")
WORLD = os.path.join(C.SPRITES, "World")
OUT = os.path.join(C.TMP, "tod")
WORK = os.path.join(OUT, "work")          # own scratch (other agents render in parallel: never _tmp/raw.png)
M = C.toon_material

MADE = []                                 # (group, name, w, h, meaning) for the manifest


# ============================================================================ scene helpers
_depth = [0]


def _front():
    """Paint order: every new object sits a little nearer the camera (the camera looks along +y)."""
    _depth[0] += 1
    return -0.002 * _depth[0]


def hexs(col):
    if isinstance(col, str):
        return col.lower()
    return "#%02x%02x%02x" % tuple(int(round(max(0.0, min(1.0, c)) * 255)) for c in col[:3])


def flat(col):
    """Flat emission material (sRGB hex or 0..1 tuple), cached by colour (re-made after clear_objects)."""
    name = "F" + hexs(col)
    m = bpy.data.materials.get(name)
    if m is None:
        m = C.glow_material(name, hexs(col), 1.0)
        m.name = name
    return m


def pixels(cells, col, a=1.0, name="px"):
    """One mesh of unit squares at canvas pixels (i, j) (j from the bottom): flat colour, alpha a."""
    cells = sorted(set(cells))
    if not cells:
        return None
    bm = bmesh.new()
    for i, j in cells:
        v = [bm.verts.new((i, 0, j)), bm.verts.new((i + 1, 0, j)), bm.verts.new((i + 1, 0, j + 1)), bm.verts.new((i, 0, j + 1))]
        bm.faces.new(v)
    ob = C.mesh_object(name, bm, flat(col))
    ob.location.y = _front()
    ob["a"] = float(a)
    return ob


def poly(pts, col, a=1.0, name="poly", mat=None):
    ob = C.poly_object(name, pts, mat or flat(col), thickness=0.01)
    ob.location.y = _front()
    ob["a"] = float(a)
    return ob


def ellipse(cx, cz, rx, rz, n=24, rot=0.0):
    c, s = math.cos(rot), math.sin(rot)
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        x, z = rx * math.cos(t), rz * math.sin(t)
        pts.append((cx + x * c - z * s, cz + x * s + z * c))
    return pts


def disc(cx, cz, r, n=24):
    return ellipse(cx, cz, r, r, n)


def q4(a):
    return np.round(np.clip(a, 0, 1) * 4) / 4


def render_raw_arr(tag):
    p = os.path.join(WORK, tag + ".png")
    C.render_raw(p)
    return C.load_pixels(p)


def render_canvas(w, h, cx=None, cz=None):
    """Colour pass + alpha pass (every object's material swapped for a flat grey = its "a"). Pixel units, canvas
    [0, w] x [0, h] unless a centre is given. Returns RGBA, rows bottom-up, alpha in quarter steps."""
    bpy.context.view_layer.update()
    cw, ch = max(w, 4), max(h, 4)                 # Blender renders at least 4x4: pad, then crop the bottom-left
    if cx is None:
        C.ortho_camera(cw / 2.0, ch / 2.0, cw, ch, 1.0)
    else:
        C.ortho_camera(cx + (cw - w) / 2.0, cz + (ch - h) / 2.0, cw, ch, 1.0)
    col = render_raw_arr("col")
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    keep = []
    for o in objs:
        keep.append([s.material for s in o.material_slots])
        g = float(o.get("a", 1.0))
        m = flat((g, g, g))
        for s in o.material_slots:
            s.material = m
    am = render_raw_arr("alpha")
    for o, mats in zip(objs, keep):
        for s, m in zip(o.material_slots, mats):
            s.material = m
    col, am = col[:h, :w], am[:h, :w]
    cov = col[..., 3] > 0.5
    out = np.zeros_like(col)
    out[..., :3] = col[..., :3]
    out[..., 3] = np.where(cov, q4(am[..., 0]), 0.0)
    out[out[..., 3] <= 0] = 0.0
    return out


def save(arr, folder, name, group, meaning):
    path = os.path.join(folder, name + ".png")
    C.save_pixels(arr, path)
    MADE.append((group, name, arr.shape[1], arr.shape[0], meaning))
    return path


def add_shade_below(arr, a=0.25, only_if=None):
    """Black shade (alpha a) on the empty pixel just below every coloured pixel (debris lifting off the water)."""
    h, w = arr.shape[:2]
    solid = (arr[..., 3] >= 0.5) & (arr[..., :3].max(axis=2) > 0.02)
    for j in range(1, h):
        for i in range(w):
            if solid[j, i] and arr[j - 1, i, 3] == 0 and (only_if is None or only_if(i, j)):
                arr[j - 1, i] = (0, 0, 0, a)
    return arr


def noise_field(w, h, scale, seed, wv=0.0, detail=1.0):
    """Blender noise texture sampled at the canvas pixel centres (rows bottom-up), 0..1."""
    C.clear_objects()
    m = bpy.data.materials.new("Noise")
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    mp = nb.node("ShaderNodeMapping")
    nb.link(tc.outputs["Object"], mp.inputs["Vector"])
    mp.inputs["Location"].default_value = (seed * 13.7, seed * 7.3, 0.0)
    no = nb.node("ShaderNodeTexNoise")
    no.noise_dimensions = "4D"
    nb.link(mp.outputs["Vector"], no.inputs["Vector"])
    no.inputs["Scale"].default_value = scale
    no.inputs["W"].default_value = seed * 0.37 + wv
    no.inputs["Detail"].default_value = detail
    nb.output_emission(no.outputs["Fac"], 1.0)
    bm = bmesh.new()
    v = [bm.verts.new((0, 0, 0)), bm.verts.new((w, 0, 0)), bm.verts.new((w, 0, h)), bm.verts.new((0, 0, h))]
    bm.faces.new(v)
    C.mesh_object("NoisePlane", bm, m)
    bpy.context.view_layer.update()
    C.ortho_camera(w / 2.0, h / 2.0, w, h, 1.0)
    arr = render_raw_arr("noise")
    C.clear_objects()
    return arr[..., 0].copy()


def line_cells(x0, y0, x1, y1):
    """Bresenham pixel line, inclusive."""
    cells = []
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx + dy
    x, y = x0, y0
    while True:
        cells.append((x, y))
        if x == x1 and y == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x += sx
        if e2 <= dx:
            err += dx
            y += sy
    return cells


def mask_cells(rows):
    """ASCII mask (rows top -> bottom) -> set of (i, j) with j from the bottom, and the symbol per cell."""
    H = len(rows)
    out = {}
    for r, row in enumerate(rows):
        for c, ch in enumerate(row):
            if ch != ".":
                out[(c, H - 1 - r)] = ch
    return out


def rot90(rows):
    """Rotate a square ASCII mask 90 deg counter-clockwise."""
    n = len(rows)
    return ["".join(rows[c][n - 1 - r] for c in range(n)) for r in range(n)]


def lit_cells(cells, light, base, shade):
    """Pixel lighting of a flat glyph: top edge light, bottom edge shade, else base."""
    groups = {light: [], base: [], shade: []}
    for (i, j) in cells:
        if (i, j + 1) not in cells:
            groups[light].append((i, j))
        elif (i, j - 1) not in cells:
            groups[shade].append((i, j))
        elif (i - 1, j) not in cells:
            groups[light].append((i, j))
        else:
            groups[base].append((i, j))
    return groups


def glyph(cells, light, base, shade, ox=0, oy=0):
    for col, cs in lit_cells(set(cells), light, base, shade).items():
        pixels([(i + ox, j + oy) for i, j in cs], col)


def icon_render(size_w, size_h, centred=True):
    """Opaque icon + the UI's 1 px hue-shifted outline (fk_common.pixelize), pixel units."""
    bpy.context.view_layer.update()
    if centred:
        C.ortho_camera(0.0, 0.0, size_w, size_h, 1.0)
    else:
        C.ortho_camera(size_w / 2.0, size_h / 2.0, size_w, size_h, 1.0)
    arr = render_raw_arr("icon")
    return C.pixelize(arr, outline=True)


# ============================================================================ UI: period icons
PERIOD_TEXT = {"dawn": "#f8c8d0", "day": "#fff4d0", "evening": "#ffc890", "night": "#b8c8ff"}


def rect(x0, z0, x1, z1):
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]


def half_disc(cx, cz, r, n=20):
    return [(cx + r * math.cos(math.pi * k / n), cz + r * math.sin(math.pi * k / n)) for k in range(n + 1)]


def stepped_half_sun(z0, half_widths):
    """Half sun on the pixel grid: rows from z0 upwards with the given half widths (px), as one polygon."""
    right = []
    for k, hw in enumerate(half_widths):
        right += [(hw, z0 + k), (hw, z0 + k + 1)]
    return right + [(-x, z) for x, z in reversed(right)]


def stepped_rows(rows):
    """Symmetric pixel shape from (z of the row's bottom, half width) rows, bottom to top, as one polygon."""
    right = []
    for z, hw in rows:
        right += [(hw, z), (hw, z + 1)]
    return right + [(-x, z) for x, z in reversed(right)]


def crescent(ca, ra, cb, rb, n=96):
    """Disc A minus disc B as one polygon (A's arc outside B, then B's arc inside A)."""
    def ring(c, r):
        return [(c[0] + r * math.cos(2 * math.pi * k / n), c[1] + r * math.sin(2 * math.pi * k / n)) for k in range(n)]

    def run(pts, keep):
        start = next(k for k in range(n) if keep[k] and not keep[k - 1])
        out, k = [], start
        while keep[k % n] and len(out) < n:
            out.append(pts[k % n])
            k += 1
        return out
    A, B = ring(ca, ra), ring(cb, rb)
    arc_a = run(A, [math.dist(p, cb) >= rb for p in A])
    arc_b = run(B, [math.dist(p, ca) < ra for p in B])
    if math.dist(arc_b[0], arc_a[-1]) > math.dist(arc_b[-1], arc_a[-1]):
        arc_b = arc_b[::-1]
    return arc_a + arc_b


def build_tod(period, small=False):
    """Authored in pixel units centred on the origin: content +-5 (12 px) or +-3 (8 px), outline outside."""
    ext = lambda name, pts, mat, d=2.0, b=0.35: I.extruded(name, pts, mat, d, b)  # noqa: E731
    if not small:
        if period == "day":
            ext("Sun", disc(0, 0, 3.0, 24), M("Sun", "#ffe070", shine=0.9))
            ray = M("Ray", "#ffc440", shine=0.4)
            for x0, z0, x1, z1 in ((-1, 4, 1, 5), (-1, -5, 1, -4), (4, -1, 5, 1), (-5, -1, -4, 1),
                                   (3, 3, 4, 4), (-4, 3, -3, 4), (3, -4, 4, -3), (-4, -4, -3, -3)):
                ext("Ray", rect(x0, z0, x1, z1), ray, 1.0, 0.0)
        elif period == "dawn":
            # rising: a full pink half sun on the pale blue horizon with five short rays
            ext("Horizon", rect(-5, -4, 5, -3), M("Hz", "#b8c8e8", shine=0.3), 1.0, 0.15)
            ext("Sun", stepped_half_sun(-3, (4, 4, 3, 2)), M("Sun", "#ffb8a0", shine=0.9)).location.y = -0.5
            ray = M("Ray", "#ffd2b4", shine=0.4)
            for x0, z0, x1, z1 in ((-1, 2, 1, 3), (3, 1, 4, 2), (-4, 1, -3, 2), (4, -1, 5, 0), (-5, -1, -4, 0)):
                ext("Ray", rect(x0, z0, x1, z1), ray, 1.0, 0.0)
        elif period == "evening":
            # setting: an orange half sun on the purple horizon, a dusky cloud streak drawn across it
            ext("Horizon", rect(-5, -3, 5, -2), M("Hz", "#8a6a9a", shine=0.3), 1.0, 0.15)
            ext("Sun", stepped_half_sun(-2, (4, 4, 3, 2)), M("Sun", "#ff9a50", shine=0.9)).location.y = -0.5
            ext("Cloud", rect(-5, 0, 2, 1), M("Cloud", "#c07898", shine=0.3), 1.0, 0.1).location.y = -1.6
        else:
            ext("Moon", crescent((-1.0, -1.0), 4.0, (1.2, 0.4), 3.4), M("Moon", "#e8ecff", shine=0.9))
            star = M("Star", "#b8c8ff", shine=0.6)
            ext("StarV", rect(3, 2, 4, 5), star, 1.0, 0.0)
            ext("StarH", rect(2, 3, 5, 4), star, 1.0, 0.0)
        return icon_render(12, 12)
    # 8 px: content +-3
    if period == "day":
        ext("Sun", disc(0, 0, 2.05, 16), M("Sun", "#ffe070", shine=0.9))
        ray = M("Ray", "#ffc440", shine=0.4)
        for x0, z0 in ((2, 2), (-3, 2), (2, -3), (-3, -3)):       # 4 diagonal rays (axis rays would sit on the outline)
            ext("Ray", rect(x0, z0, x0 + 1, z0 + 1), ray, 1.0, 0.0)
    elif period == "dawn":
        ext("Horizon", rect(-3, -2, 3, -1), M("Hz", "#b8c8e8", shine=0.3), 1.0, 0.15)
        ext("Sun", stepped_half_sun(-1, (3, 3, 2)), M("Sun", "#ffb8a0", shine=0.9)).location.y = -0.5
    elif period == "evening":
        ext("Horizon", rect(-3, -2, 3, -1), M("Hz", "#8a6a9a", shine=0.3), 1.0, 0.15)
        ext("Sun", stepped_half_sun(-1, (3, 3, 2)), M("Sun", "#ff9a50", shine=0.9)).location.y = -0.5
        ext("Cloud", rect(-3, 0, 1, 1), M("Cloud", "#c07898"), 1.0, 0.0).location.y = -1.6
    else:
        ext("Moon", crescent((-0.6, -0.6), 3.0, (0.9, 0.3), 2.5), M("Moon", "#e8ecff", shine=0.9))
        ext("Star", rect(2, 2, 3, 3), M("Star", "#b8c8ff"), 1.0, 0.0)
    return icon_render(8, 8)


# ============================================================================ UI: tide states
WAVE_TOP = (1, 2, 5, 6)          # 8-cell zig-zag wave: top row cells, bottom row the others
WAVE_BOT = (0, 3, 4, 7)


def wave_cells(x0, z_bot):
    """Pixel wave across 8 cells from x0 on the rows z_bot and z_bot + 1."""
    return [(x0 + c, z_bot + 1) for c in WAVE_TOP] + [(x0 + c, z_bot) for c in WAVE_BOT]


def build_tide(state):
    """10 px icon, content +-4 (pixel units centred on the origin: cells x -4..3, z -4..3)."""
    ext = lambda name, pts, mat, d=2.0, b=0.3: I.extruded(name, pts, mat, d, b)  # noqa: E731
    wave = M("Wave", "#2a64a8", shine=0.3)
    if state in ("flood", "ebb"):
        for (i, j) in wave_cells(-4, -4):
            ext("W", rect(i, j, i + 1, j + 1), wave, 1.0, 0.0)
        mat = M("Arrow", "#6ad8ff" if state == "flood" else "#7a9cff", shine=0.8)
        # a stepped triangle on the pixel grid: rows 8 / 6 / 4 / 2 px, pointing up (flood) or down (ebb)
        widths = (4, 3, 2, 1) if state == "flood" else (1, 2, 3, 4)
        right = []
        for k, hw in enumerate(widths):
            right += [(hw, -1 + k), (hw, k)]
        ext("Tri", right + [(-x, z) for x, z in reversed(right)], mat, 2.0, 0.3)
    else:
        water = M("Water", "#3a86d0", shine=0.4)
        crest = M("Crest", "#8ad0ff", shine=0.5)
        bar = M("Bar", "#e8eef6", shine=0.6)
        if state == "high":
            ext("Water", rect(-4, -4, 4, 1), water)
            for c in WAVE_TOP:
                ext("C", rect(-4 + c, 1, -3 + c, 2), crest, 1.0, 0.0)
            ext("Bar", rect(-4, 3, 4, 4), bar, 1.5, 0.2)
        else:
            ext("Water", rect(-4, -4, 4, -3), water, 1.5, 0.15)
            for c in WAVE_TOP:
                ext("C", rect(-4 + c, -3, -3 + c, -2), crest, 1.0, 0.0)
            ext("Bar", rect(-4, -1, 4, 0), bar, 1.5, 0.2)
            mark = M("Mark", "#6a7a94", shine=0.2)
            for x in (-4, -1, 2):
                ext("Mark", rect(x, 3, x + 2, 4), mark, 1.0, 0.0)
    return icon_render(10, 10)


def build_tide_bar():
    """Frame 28x8 (outline, inset bevel, well with 25 / 50 / 75 % ticks) and the 24x4 fill. Pixel units, bottom-left
    origin, drawn as pixel squares (no automatic outline: the frame carries its own)."""
    C.clear_objects()
    W, H = 28, 8
    outline, bev_dark, bev_light, well, tick = "#0a1018", "#0e1620", "#34485e", "#1c2a3a", "#2e4460"
    ring0 = [(i, j) for i in range(W) for j in range(H) if (i in (0, W - 1) or j in (0, H - 1))
             and not ((i in (0, W - 1)) and (j in (0, H - 1)))]
    pixels(ring0, outline)
    top_left = [(i, H - 2) for i in range(1, W - 1)] + [(1, j) for j in range(1, H - 2)]
    bot_right = [(i, 1) for i in range(2, W - 1)] + [(W - 2, j) for j in range(2, H - 2)]
    pixels(top_left, bev_dark)
    pixels(bot_right, bev_light)
    pixels([(i, j) for i in range(2, W - 2) for j in range(2, H - 2)], well)
    for frac, tall in ((0.25, 1), (0.5, 2), (0.75, 1)):
        x = 2 + int(24 * frac)
        pixels([(x, j) for j in range(2, 2 + tall)], tick)
    bg = render_canvas(W, H)
    C.clear_objects()
    W2, H2 = 24, 4
    pixels([(i, 0) for i in range(W2)], "#3a86d0")
    pixels([(i, 1) for i in range(W2)], "#5aa6f0")
    pixels([(i, 2) for i in range(W2)], "#6ab8ff")
    pixels([(i, 3) for i in range(W2) if i % 6 in (0, 1)], "#d4f0ff")
    pixels([(i, 3) for i in range(W2) if i % 6 not in (0, 1)], "#94d0ff")
    fill = render_canvas(W2, H2)
    return bg, fill


# ============================================================================ UI: current arrows, pips, chevron
ARROW_R = [".......", "....X..", ".....X.", "X.XXXXX", ".....X.", "....X..", "......."]
ARROW_UR = ["....XXX", ".....XX", "....X.X", "...X...", "..X....", ".......", "X......"]
ARROW_S_R = ["..X..", "...X.", "XXXXX", "...X.", "..X.."]
ARROW_S_UR = ["..XXX", "...XX", "..X.X", ".X...", "X...."]
ARROW_COLS = ("#ffffff", "#e4eaf2", "#a0acbc")        # neutral: the HUD tints it (#a8e0ff current, #d8e8f0 wind)


def arrow_masks(right, up_right):
    out = []
    for k in range(8):
        m = right if k % 2 == 0 else up_right
        for _ in range(k // 2):
            m = rot90(m)
        out.append(m)
    return out


def build_mask_icon(rows, cols, size):
    C.clear_objects()
    cells = mask_cells(rows)
    n = len(rows)
    off = (size - n) // 2
    glyph(cells.keys(), *cols, ox=off, oy=off)
    return icon_render(size, size, centred=False)


def build_pips():
    out = {}
    for name, (l, b, s) in (("cur_pip_on", ("#e0f6ff", "#a8e0ff", "#6aa8d0")), ("cur_pip_off", ("#2c3c50", "#1c2a3a", "#141e2a"))):
        C.clear_objects()
        pixels([(0, 2), (1, 2), (0, 1)], l)
        pixels([(2, 2), (1, 1), (0, 0)], b)
        pixels([(2, 1), (1, 0), (2, 0)], s)
        out[name] = render_canvas(3, 3)
    return out


CHEVRON = ["XX.XX..", ".XX.XX.", "..XX.XX", ".XX.XX.", "XX.XX.."]


def build_chevron():
    C.clear_objects()
    cells = mask_cells(CHEVRON)
    glyph(cells.keys(), "#b8e0ff", "#6ab8ff", "#3a78c8", ox=1, oy=1)
    return icon_render(9, 7, centred=False)


# ============================================================================ WORLD: flow streaks
FLOW_LENS = (4, 7, 10, 14)


def even(n):
    return n + (n % 2)


def build_flow(L, s, symmetric):
    """Crest on the row just above the centre (row 2 of 4), shade on the row below it, spray / chop on row 3."""
    C.clear_objects()
    W, H = even(L + (2 if symmetric else 4)), 4
    x0 = (W - L - (0 if symmetric else 2)) // 2 if symmetric else 1
    crest, shade, spray = {}, {}, {}
    for k in range(L):
        x = x0 + k
        u = k / max(1, L - 1)
        t = 1.0 - abs(2 * u - 1) if symmetric else u
        if s == 1:
            a = 0.25 + 0.25 * t
        elif s == 2:
            a = 0.25 + 0.5 * t
        else:
            a = 0.5 + 0.5 * t
        crest[x] = float(q4(a))
    if not symmetric:
        xh = x0 + L - 1
        if s >= 2:
            crest[xh] = 1.0
        if s == 3:
            if L >= 7:
                crest[x0 + round(0.25 * (L - 1))] = 0.0        # a break in the tail
            spray[(xh + 1, 2)] = 0.5
            spray[(xh - 1, 3)] = 0.5
        for x, a in crest.items():
            u = (x - x0) / max(1, L - 1)
            if s == 2 and u >= 0.5 and a > 0:
                shade[x] = 0.25
            if s == 3 and u >= 0.25 and a > 0:
                shade[x] = 0.5
    else:
        for x, a in crest.items():
            u = (x - x0) / max(1, L - 1)
            t = 1.0 - abs(2 * u - 1)
            if s == 2 and t >= 0.5:
                shade[x] = 0.25
            if s == 3 and t >= 0.3:
                shade[x] = 0.5
        if s == 3:
            n = max(1, L // 3)
            xs = x0 + L // 2
            for k in range(n):
                spray[(xs + k, 3)] = 0.5 if k < n - 1 or n == 1 else 0.25
    for a in (0.25, 0.5, 0.75, 1.0):
        pixels([(x, 2) for x, v in crest.items() if v == a], "#ffffff", a)
        pixels([(x, 1) for x, v in shade.items() if v == a], "#000000", a)
        pixels([c for c, v in spray.items() if v == a], "#ffffff", a)
    return render_canvas(W, H)


# ============================================================================ WORLD: foam
def build_foamline(L, frame, seed):
    W, H = even(L + 2), 4
    nz = noise_field(W, H, 0.9, seed, wv=0.23 * frame, detail=2.0)
    C.clear_objects()
    x0 = 1
    main, lace, shade = {}, {}, {}
    for k in range(L):
        x = x0 + k
        u = (k + 0.5) / L
        env = min(1.0, 3 * u, 3 * (1 - u))
        v = nz[2, x]
        a = 1.0 if v > 0.56 else 0.75 if v > 0.47 else 0.5 if v > 0.4 else 0.25
        a = float(q4(a * (0.5 + 0.5 * env)))
        if a >= 0.25:
            main[x] = a
        if nz[3, x] > 0.58 and 0.2 < u < 0.85:
            lace[x] = 0.5
        if a >= 0.75:
            shade[x] = 0.5
        elif a >= 0.5:
            shade[x] = 0.25
    for a in (0.25, 0.5, 0.75, 1.0):
        pixels([(x, 1) for x, v in shade.items() if v == a], "#000000", a)
        pixels([(x, 2) for x, v in main.items() if v == a], "#ffffff", a)
        pixels([(x, 3) for x, v in lace.items() if v == a], "#ffffff", a)
    return render_canvas(W, H)


FOAMPATCH = {  # canvas, main ellipse (rx, rz), satellites
    "s": ((6, 4), (2.1, 0.95), 1),
    "m": ((10, 6), (3.5, 1.35), 3),
    "l": ((14, 8), (5.2, 2.1), 4),
}


def build_foampatch(size, frame, seed):
    (W, H), (rx, rz), nsat = FOAMPATCH[size]
    nz = noise_field(W, H, 1.1, seed, wv=0.3 * frame, detail=2.0)
    C.clear_objects()
    rng = random.Random(seed * 31 + 7)
    cx, cz = W / 2.0, H / 2.0 + 0.25
    poly(ellipse(cx, cz, rx, rz, 20, rng.uniform(-0.12, 0.12)), "#ffffff")
    for k in range(nsat):
        a = rng.uniform(0, 2 * math.pi)
        loose = rng.random() < 0.5                       # half the satellites are loose clumps beside the patch
        sx = cx + math.cos(a) * rx * (rng.uniform(1.05, 1.3) if loose else rng.uniform(0.5, 0.85))
        sz = cz + math.sin(a) * rz * (rng.uniform(0.9, 1.2) if loose else rng.uniform(0.3, 0.7))
        if frame == 1 and k == nsat - 1:
            continue                                     # a clump popped
        r = rng.uniform(0.12, 0.2) if loose else rng.uniform(0.28, 0.42)
        poly(ellipse(sx, sz, max(0.6, rx * r), max(0.55, rz * r * 1.4), 12), "#ffffff")
    blob = render_canvas(W, H)
    m = blob[..., 3] > 0
    out = np.zeros_like(blob)
    for j in range(H):
        for i in range(W):
            if not m[j, i]:
                continue
            nb = sum(1 for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1))
                     if 0 <= i + di < W and 0 <= j + dj < H and m[j + dj, i + di])
            v = nz[j, i]
            if nb == 4:                                  # inside: dense foam with lace holes
                a = 1.0 if v > 0.56 else 0.75 if v > 0.47 else 0.5
            else:                                        # rim: ragged, thinner
                a = 0.75 if v > 0.52 else 0.5 if v > 0.42 else 0.0
                if nb <= 1:
                    a = min(a, 0.5)
            if a > 0:
                out[j, i] = (1, 1, 1, a)
    solid = out[..., 3] >= 0.75
    for j in range(1, H):
        for i in range(W):
            if solid[j, i] and out[j - 1, i, 3] == 0:
                out[j - 1, i] = (0, 0, 0, 0.25)
    return out


def build_fleck(n, frame):
    C.clear_objects()
    if n == 1:
        pixels([(0, 1)], "#ffffff", 1.0 if frame == 0 else 0.5)
        return render_canvas(2, 2)
    if n == 2:
        pixels([(1, 1)], "#ffffff", 1.0 if frame == 0 else 0.75)
        pixels([(2, 1)], "#ffffff", 0.75 if frame == 0 else 0.5)
        return render_canvas(4, 2)
    if frame == 0:
        pixels([(1, 2), (2, 2)], "#ffffff", 1.0)
        pixels([(2, 3), (0, 2)], "#ffffff", 0.5)
        pixels([(1, 1), (2, 1)], "#000000", 0.25)
    else:
        pixels([(2, 2)], "#ffffff", 1.0)
        pixels([(1, 2)], "#ffffff", 0.75)
        pixels([(3, 2)], "#ffffff", 0.5)
        pixels([(2, 1)], "#000000", 0.25)
    return render_canvas(4, 4)


def build_slick(L):
    C.clear_objects()
    W, H = L, 6
    for row, (f0, f1) in ((4, (0.2, 0.92)), (2, (0.0, 0.76)), (0, (0.3, 1.0))):
        a0, a1 = int(round(f0 * (W - 1))), int(round(f1 * (W - 1)))
        cells = []
        for x in range(a0, a1 + 1):
            end = min(x - a0, a1 - x)
            if end < 3 and (x % 2) == (row // 2) % 2:
                continue                              # dithered ends
            cells.append((x, row))
        pixels(cells, "#ffffff", 0.25)
    return render_canvas(W, H)


def build_catspaw(W, H, n, seed):
    C.clear_objects()
    rng = random.Random(seed)
    cx, cz, rx, rz = W / 2.0, H / 2.0, W / 2.0 - 1, H / 2.0 - 1
    taken = set()
    dark = {0.25: [], 0.5: []}
    glint = {0.25: [], 0.5: []}
    tries = 0
    while n > 0 and tries < 400:
        tries += 1
        L = rng.randint(2, 4) if W > 30 else rng.randint(1, 3)
        x = rng.uniform(cx - rx, cx + rx - L)
        z = rng.uniform(cz - rz, cz + rz)
        d = ((x + L / 2 - cx) / rx) ** 2 + ((z - cz) / rz) ** 2
        if d > 1.0:
            continue
        i0, j = int(x), int(z)
        cells = [(i0 + k, j) for k in range(L)]
        ring = {(a + da, b + db) for a, b in cells for da in (-1, 0, 1) for db in (-1, 0, 1)}
        if ring & taken or j + 1 >= H or j < 1:
            continue
        taken |= set(cells) | {(c[0], c[1] + 1) for c in cells}
        a = 0.5 if d < 0.4 else 0.25
        dark[a] += cells
        if rng.random() < 0.4:
            glint[0.5 if d < 0.4 else 0.25].append((i0, j + 1))
        n -= 1
    for a, cs in dark.items():
        pixels(cs, "#000000", a)
    for a, cs in glint.items():
        pixels(cs, "#ffffff", a)
    return render_canvas(W, H)


# ============================================================================ WORLD: float wake + bow pillow
WAKE = {
    # size: canvas, waterline row, stem columns, arms {dir: [(root, end, far_side)]}, pillow {dir: [(cell, a)]}
    "n": ((24, 12), 5, (11, 12), {
        "down": [((10, 5), (2, 2), False), ((13, 5), (21, 2), False)],
        "up": [((10, 5), (3, 8), True), ((13, 5), (20, 8), True)],
        "side": [((13, 6), (22, 8), True), ((13, 5), (22, 2), False)],
    }, {
        "down": [((9, 5), 0.75), ((14, 5), 0.75)],
        "up": [((10, 4), 1.0), ((11, 4), 1.0), ((12, 4), 1.0), ((13, 4), 1.0), ((9, 5), 0.75), ((14, 5), 0.75)],
        "side": [((9, 5), 1.0), ((10, 5), 1.0), ((10, 6), 0.75), ((9, 4), 0.5), ((10, 4), 0.75)],
    }),
    "s": ((12, 8), 3, (5, 6), {
        "down": [((4, 3), (1, 1), False), ((7, 3), (10, 1), False)],
        "up": [((4, 3), (1, 5), True), ((7, 3), (10, 5), True)],
        "side": [((7, 4), (11, 5), True), ((7, 3), (11, 1), False)],
    }, {
        "down": [((3, 3), 0.5), ((8, 3), 0.5)],
        "up": [((5, 2), 1.0), ((6, 2), 1.0)],
        "side": [((4, 3), 1.0), ((4, 4), 0.5)],
    }),
}
WAKE_DASH = (1.0, 1.0, 0.5, 0.0)          # travelling dash pattern along an arm (period 4 = the 4 frames)


def build_wake(size, direction, frame):
    C.clear_objects()
    (W, H), wl, stem, arms, pillows = WAKE[size]
    cells = {}
    shade = {}

    def put(c, a):
        if a > cells.get(c, 0.0):
            cells[c] = a
    for root, end, far in arms[direction]:
        line = line_cells(root[0], root[1], end[0], end[1])
        n = len(line)
        for i, c in enumerate(line):
            fall = 1.0 - 0.7 * i / max(1, n - 1)
            pat = WAKE_DASH[(i - frame) % 4] if i >= 2 else 1.0
            a = float(q4(pat * fall * (0.75 if far else 1.0)))
            if a > 0:
                put(c, a)
                if size == "n" and not far and a >= 0.75:
                    shade[(c[0], c[1] - 1)] = 0.25
    for c, a in pillows[direction]:
        put(c, float(q4(a * (1.0 if frame % 2 == 0 else 0.75))))
    for c in list(shade):
        if c in cells or c[1] < 0:
            del shade[c]
    for a in (0.25, 0.5, 0.75, 1.0):
        pixels([c for c, v in shade.items() if v == a], "#000000", a)
        pixels([c for c, v in cells.items() if v == a], "#ffffff", a)
    return render_canvas(W, H)


# ============================================================================ WORLD: debris
LEAF = {  # light half, dark half (spec 10.1: the second pixel one step darker)
    "yellow": ("#c8a040", "#9a7430"),
    "brown": ("#8a6a2c", "#664a22"),
    "green": ("#6a8a3a", "#4a6a30"),
    "dead": ("#5e4a2e", "#3e3020"),
}


def leaf_halves(length=4.6, width=2.3, n=8):
    """Pointed leaf along +x from -L/2 to +L/2: upper half and lower half polygons (midrib on z = 0)."""
    up, lo = [], []
    for k in range(n + 1):
        t = k / n
        x = -length / 2 + length * t
        w = width / 2 * math.sin(math.pi * t) ** 0.8
        up.append((x, w))
        lo.append((x, -w))
    return up, lo


def build_leaf(col, frame):
    C.clear_objects()
    W, H = 6, 4
    light, dark = LEAF[col]
    ang = math.radians(20 + 45 * frame)
    squash = 0.55
    up, lo = leaf_halves()

    def tf(p):
        x, z = p
        c, s = math.cos(ang), math.sin(ang)
        return (W / 2 + x * c - z * s, H / 2 + 0.3 + (x * s + z * c) * squash)
    poly([tf(p) for p in lo], dark)
    poly([tf(p) for p in up], light)
    stem = [(-2.3, -0.2), (-3.0, -0.2), (-3.0, 0.2), (-2.3, 0.2)]
    poly([tf(p) for p in stem], dark)
    arr = render_canvas(W, H)
    return add_shade_below(arr, 0.25)


def build_leaf_small(col, frame):
    C.clear_objects()
    light, dark = LEAF[col]
    a, b = (light, dark) if frame == 0 else (dark, light)
    pixels([(0, 1)], a)
    pixels([(1, 1)], b)
    pixels([(0, 0), (1, 0)], "#000000", 0.25)
    return render_canvas(2, 2)


def build_twig(frame):
    C.clear_objects()
    bark, lit, knot = "#6a4a2c", "#8a6a44", "#4a321e"
    if frame == 0:
        body = [(x, 2) for x in range(1, 7)]
        stub = [(5, 3)]
    else:
        body = line_cells(1, 1, 6, 3)
        stub = [(3, 3)]
    pixels([c for k, c in enumerate(body) if k % 3 != 1], bark)
    pixels([c for k, c in enumerate(body) if k % 3 == 1], lit)
    pixels([body[0]], knot)
    pixels(stub, bark)
    arr = render_canvas(8, 4)
    return add_shade_below(arr, 0.25)


WEED = ("#2e4a26", "#3c5a2c", "#5a7a3a", "#7a9a4a")   # dark, body, top, highlight


def build_weed(frame, small=False):
    C.clear_objects()
    dark, body, top, hi = WEED
    if small:
        W, H = 6, 2
        ph = frame * 2 * math.pi / 3
        cells = [(x, 1 if math.sin(ph + x * 1.3) > -0.2 else 0) for x in range(1, 5)]
        pixels(cells[1:3], body)
        pixels([cells[0], cells[3]], dark)
        arr = render_canvas(W, H)
        return arr
    W, H = 10, 4
    ph = frame * 2 * math.pi / 3
    cells = []
    for x in range(1, 9):
        z = 2 + int(round(0.8 * math.sin(2 * math.pi * x / 6 + ph)))
        cells.append((x, min(3, max(1, z))))
    ends = [cells[0], cells[-1]]
    rising = [c for k, c in enumerate(cells[1:-1], 1) if cells[k - 1][1] < c[1] or cells[k + 1][1] < c[1]]
    pixels(ends, dark)
    pixels([c for c in cells[1:-1] if c not in rising], body)
    pixels(rising, top)
    blade = [(c[0], c[1] - 1) for c in cells[3:5] if c[1] - 1 >= 1]
    pixels(blade, body)
    if frame == 1:
        pixels([cells[4]], hi)
    arr = render_canvas(W, H)
    return add_shade_below(arr, 0.25)


def build_tuft(state):
    C.clear_objects()
    base, top = "#3c5a2c", "#5a7a3a"
    if state == 0:
        pixels([(1, 0)], base)
        pixels([(1, 1)], top)
    elif state == 1:
        pixels([(1, 0)], base)
        pixels([(2, 1)], top)
    else:
        pixels([(2, 0)], base)
        pixels([(3, 0)], top)
    return render_canvas(4, 2)


SARG = {"s": ((6, 4), 1.9, 1), "m": ((8, 4), 2.9, 2), "l": ((12, 6), 4.3, 3)}   # canvas, half length, lumps
SARG_COLS = ("#7a6428", "#a88a3a", "#c8a848", "#e0c060")                     # dark, body, lit, bladder


def build_sargassum(size, frame, seed=5):
    (W, H), spread, n = SARG[size]
    rng = random.Random(seed + {"s": 1, "m": 2, "l": 3}[size])
    C.clear_objects()
    cx, cz = W / 2.0, H / 2.0 + 0.2
    rz = 1.05 if size != "l" else 1.5
    nz = noise_field(W, H, 0.8, seed * 3 + {"s": 1, "m": 2, "l": 3}[size], wv=0.0, detail=2.0)
    poly(ellipse(cx, cz, spread, rz, 20, rng.uniform(-0.1, 0.1)), "#ffffff")
    for k in range(n):                           # a few lumps sticking out of the mat
        a = rng.uniform(0, 2 * math.pi)
        poly(disc(cx + math.cos(a) * spread * 0.8, cz + math.sin(a) * rz * 0.8, rng.uniform(0.6, 0.85), 10), "#ffffff")
    m = render_canvas(W, H)[..., 3] > 0
    core = m.copy()
    for j in range(H):                           # the rim is ragged: keep a rim pixel only where the noise is high
        for i in range(W):
            if m[j, i]:
                nb = sum(1 for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1))
                         if 0 <= i + di < W and 0 <= j + dj < H and m[j + dj, i + di])
                if nb < 4 and nz[j, i] < 0.47:
                    core[j, i] = False
    m = core
    dark, body, lit, blad = SARG_COLS
    C.clear_objects()
    groups = {dark: [], body: [], lit: [], blad: []}
    for j in range(H):
        for i in range(W):
            if not m[j, i]:
                continue
            upE = j + 1 >= H or not m[j + 1, i]
            leftE = i == 0 or not m[j, i - 1]
            downE = j == 0 or not m[j - 1, i]
            rightE = i + 1 >= W or not m[j, i + 1]
            if upE and not downE:
                groups[lit].append((i, j))
            elif upE or leftE:
                groups[lit if (i + j + frame) % 2 == 0 else body].append((i, j))
            elif downE or rightE:
                groups[dark].append((i, j))
            else:
                groups[body].append((i, j))
    inner = groups[body] + groups[lit]
    rng2 = random.Random(seed * 7 + frame)
    for c in rng2.sample(inner, min(len(inner), max(1, len(inner) // 5))):
        for g in groups.values():
            if c in g:
                g.remove(c)
        groups[blad].append(c)
    for col, cs in groups.items():
        pixels(cs, col)
    arr = render_canvas(W, H)
    return add_shade_below(arr, 0.25)


def build_driftwood():
    C.clear_objects()
    body, lit, end, knot = "#8a6a4a", "#b89a70", "#5a4430", "#6a5038"
    pixels([(x, 2) for x in range(1, 8)], lit)
    pixels([(x, 1) for x in range(1, 8) if x != 4], body)
    pixels([(4, 1)], knot)
    pixels([(8, 1), (8, 2)], end)
    pixels([(0, 1)], "#e8f0f4", 0.75)
    pixels([(9, 1)], "#e8f0f4", 0.5)
    arr = render_canvas(10, 4)
    return add_shade_below(arr, 0.25, only_if=lambda i, j: 1 <= i <= 8)


POLLEN = {"s": ((6, 4), 4), "m": ((10, 4), 7), "l": ((16, 6), 12)}


def build_pollen(size, seed=11):
    (W, H), n = POLLEN[size]
    rng = random.Random(seed + W)
    C.clear_objects()
    taken = set()
    pts = {("#e8dc8c", 0.75): [], ("#c8b860", 0.5): []}
    tries = 0
    while n > 0 and tries < 500:
        tries += 1
        i, j = rng.randrange(W), rng.randrange(H)
        d = ((i + 0.5 - W / 2) / (W / 2)) ** 2 + ((j + 0.5 - H / 2) / (H / 2)) ** 2
        if d > 1.0 or any((i + a, j + b) in taken for a in (-1, 0, 1) for b in (-1, 0, 1)):
            continue
        taken.add((i, j))
        pts[("#e8dc8c", 0.75) if d < 0.45 or rng.random() < 0.3 else ("#c8b860", 0.5)].append((i, j))
        n -= 1
    for (col, a), cs in pts.items():
        pixels(cs, col, a)
    return render_canvas(W, H)


DUCK = {"s": ((4, 2), [(1, 1)]), "m": ((6, 4), [(1, 2), (3, 2), (2, 1)]),
        "l": ((10, 4), [(1, 2), (3, 1), (4, 2), (6, 2), (7, 1), (5, 3)])}


def build_duckweed(size):
    (W, H), fronds = DUCK[size]
    C.clear_objects()
    lit, body, dark = "#a8c058", "#6a8a3a", "#4a6a30"
    L, B = [], []
    for (i, j) in fronds:
        L.append((i, j))
        if i + 1 < W and (i + 1, j) not in fronds:
            B.append((i + 1, j))
    pixels(B, body)
    pixels(L, lit)
    if size == "l":
        pixels([(8, 2)], dark)
    arr = render_canvas(W, H)
    return add_shade_below(arr, 0.25)


# ============================================================================ build groups
def build_ui():
    for p in ("dawn", "day", "evening", "night"):
        C.clear_objects()
        save(build_tod(p), UI, f"tod_{p}", "ui", f"clock icon {p} (12 px, text colour {PERIOD_TEXT[p]})")
        C.clear_objects()
        save(build_tod(p, small=True), UI, f"tod_{p}_s", "ui", f"clock icon {p}, small (8 px)")
    meanings = {"flood": "들물 rising (▲ over the wave)", "ebb": "날물 falling (▼ over the wave)",
                "high": "만조 high slack (water up to the bar)", "low": "간조 low slack (bar just over a sliver of water)"}
    for st in ("flood", "ebb", "high", "low"):
        C.clear_objects()
        save(build_tide(st), UI, f"tide_{st}", "ui", meanings[st])
    bg, fill = build_tide_bar()
    save(bg, UI, "tide_bar_bg", "ui", "tide level bar frame; the well is the inner 24x4 at (2, 2), ticks 25/50/75 %")
    save(fill, UI, "tide_bar_fill", "ui", "tide level water: Image Filled / Horizontal / Left, fillAmount = (h + 1) / 2")
    for k, m in enumerate(arrow_masks(ARROW_R, ARROW_UR)):
        save(build_mask_icon(m, ARROW_COLS, 9), UI, f"cur_arrow_{k}", "ui",
             f"current / wind arrow {45 * k} deg ccw from screen right (tint in the HUD)")
    for k, m in enumerate(arrow_masks(ARROW_S_R, ARROW_S_UR)):
        save(build_mask_icon(m, ARROW_COLS, 7), UI, f"cur_arrow_s_{k}", "ui", f"small current arrow {45 * k} deg")
    for name, arr in build_pips().items():
        save(arr, UI, name, "ui", "current strength pip " + ("lit #a8e0ff" if name.endswith("on") else "off #1c2a3a"))
    save(build_chevron(), UI, "cur_chevron", "ui", "fight strip '>>' (7x5 glyph + outline), flipX for a run to the left")


def build_fx():
    for L in FLOW_LENS:
        for s in (1, 2, 3):
            save(build_flow(L, s, False), WORLD, f"fx_flow_{L}_{s}", "flow", f"flow line {L} px strength {s}, head to the right")
            save(build_flow(L, s, True), WORLD, f"fx_streak_{L}_{s}", "flow", f"symmetric stream streak {L} px strength {s}")
    for i, L in enumerate((8, 14, 20)):
        for f in (0, 1):
            save(build_foamline(L, f, 3 + i), WORLD, f"fx_foamline_{L}_f{f}", "foam", f"foam line {L} px frame {f}")
    for i, sz in enumerate(("s", "m", "l")):
        for f in (0, 1):
            save(build_foampatch(sz, f, 17 + i), WORLD, f"fx_foampatch_{sz}_f{f}", "foam", f"foam patch {sz} frame {f}")
    for n in (1, 2, 3):
        for f in (0, 1):
            save(build_fleck(n, f), WORLD, f"fx_fleck_{n}_f{f}", "foam", f"foam fleck {n} frame {f}")
    for L in (24, 40):
        save(build_slick(L), WORLD, f"fx_slick_{L}", "foam", f"slack-water slick {L} px (alpha 0.25 rows)")
    for k in (0, 1):
        save(build_catspaw(40, 12, 16, 41 + k), WORLD, f"fx_catspaw_{k}", "gust", "gust cat's paw 40x12")
        save(build_catspaw(24, 8, 9, 51 + k), WORLD, f"fx_catspaw_s_{k}", "gust", "gust cat's paw small 24x8")
    for sz in ("n", "s"):
        for d in ("down", "up", "side"):
            for f in range(4):
                nm = f"fx_wake_{d}_f{f}" if sz == "n" else f"fx_wake_{d}_s_f{f}"
                save(build_wake(sz, d, f), WORLD, nm, "wake", f"float wake trailing {d} ({'near' if sz == 'n' else 'far'}) frame {f}")
    for col in LEAF:
        for f in range(4):
            save(build_leaf(col, f), WORLD, f"fx_leaf_{col}_f{f}", "debris", f"{col} leaf spinning, frame {f} (near)")
        for f in (0, 1):
            save(build_leaf_small(col, f), WORLD, f"fx_leaf_{col}_s_f{f}", "debris", f"{col} leaf 2x1 (far), frame {f}")
    for f in (0, 1):
        save(build_twig(f), WORLD, f"fx_twig_f{f}", "debris", f"twig, pose {f}")
    for f in range(3):
        save(build_weed(f), WORLD, f"fx_weed_f{f}", "debris", f"floating weed strand, undulation frame {f}")
        save(build_weed(f, small=True), WORLD, f"fx_weed_s_f{f}", "debris", f"small weed strand, frame {f}")
    for st in range(3):
        save(build_tuft(st), WORLD, f"fx_tuft_{st}", "debris",
             ("weed tuft upright (slack)", "weed tuft, top leaning with the flow (s >= 0.3)", "weed tuft combed flat (s >= 0.7)")[st])
    for sz in ("s", "m", "l"):
        for f in (0, 1):
            save(build_sargassum(sz, f), WORLD, f"fx_sargassum_{sz}_f{f}", "debris", f"sargassum clump {sz}, bob frame {f}")
    save(build_driftwood(), WORLD, "fx_driftwood", "debris", "driftwood with foam at its ends")
    for sz in ("s", "m", "l"):
        save(build_pollen(sz), WORLD, f"fx_pollen_{sz}", "debris", f"pollen film {sz}")
        save(build_duckweed(sz), WORLD, f"fx_duckweed_{sz}", "debris", f"duckweed cluster {sz}")


# ============================================================================ preview sheet
def load_td(path):
    """RGBA rows top-down."""
    return C.load_pixels(path)[::-1].copy()


def hexf(h):
    return np.array(C.hex_rgb(h), np.float32)


def lerp(a, b, t):
    return a + (b - a) * t


def water_cols(tint, night=0.0):
    t = hexf(tint)
    w = np.ones(3, np.float32)
    lit = lerp(t, w, 0.38)
    foam = lerp(w, t, 0.12)
    lighter = lerp(t, w, 0.58)
    ncol = hexf("#c8d4f0")
    foam = lerp(foam, ncol, 0.3 * night)
    return dict(lit=lit, foam=foam, lighter=lighter)


def tinted(img, tint, alpha_mul=1.0, mul=None):
    out = img.copy()
    if tint is not None:
        out[..., :3] = out[..., :3] * tint
    if mul is not None:
        out[..., :3] = out[..., :3] * mul
    out[..., 3] = q4(out[..., 3] * alpha_mul)
    return out


def paste(dst, img, x, y, scale=1):
    """Alpha-blend img (top-down) into dst (top-down) at (x, y) with nearest upscaling; clipped."""
    big = np.repeat(np.repeat(img, scale, 0), scale, 1) if scale > 1 else img
    h, w = big.shape[:2]
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    s = big[y0 - y:y1 - y, x0 - x:x1 - x]
    a = s[..., 3:4]
    d = dst[y0:y1, x0:x1]
    d[..., :3] = d[..., :3] * (1 - a) + s[..., :3] * a


def fill(dst, x, y, w, h, col):
    dst[max(0, y):y + h, max(0, x):x + w, :3] = col


class Packer:
    def __init__(self, width, pad=10):
        self.W, self.pad = width, pad
        self.x, self.y, self.row_h = pad, pad, 0
        self.items = []

    def add(self, w, h, draw):
        if self.x + w + self.pad > self.W:
            self.newline()
        self.items.append((self.x, self.y, draw))
        self.x += w + self.pad
        self.row_h = max(self.row_h, h)

    def newline(self, extra=0):
        if self.row_h or extra:
            self.y += self.row_h + self.pad + extra
        self.x, self.row_h = self.pad, 0

    def height(self):
        return self.y + self.row_h + self.pad


# stage water for the swatches: (tint, night tint of the same stage from the drafts)
SW = {
    "stream": ("#3e7a78", "#304458"), "sea": ("#2c7aa4", "#2a3856"), "ocean": ("#2c5c8a", "#283656"),
    "lake": ("#456a8a", "#2a3856"), "swamp": ("#3a4a30", "#2a3430"),
}
NIGHT_ACTOR = hexf("#8e9ac0")


def fx_cell(img, stage, role, scale, night):
    """One swatch cell: the sprite on the stage's water, tinted the Unity way."""
    tint = SW[stage][1 if night else 0]
    wc = water_cols(tint, 1.0 if night else 0.0)
    pad = 2 * scale
    h, w = img.shape[0] * scale + 2 * pad, img.shape[1] * scale + 2 * pad
    cell = np.zeros((h, w, 4), np.float32)
    cell[..., :3] = hexf(tint)
    cell[..., 3] = 1
    if role == "nat":
        spr = tinted(img, None, 1.0, NIGHT_ACTOR if night else None)
    else:
        spr = tinted(img, wc[role], 0.7 if night else 1.0)
    paste(cell, spr, pad, pad, scale)
    return cell


def fx_group_rows():
    """(sprite names, stage, role) per swatch group, in sheet order."""
    g = []
    g.append(([f"fx_flow_{L}_{s}" for s in (1, 2, 3) for L in FLOW_LENS], "sea", "lit"))
    g.append(([f"fx_streak_{L}_{s}" for s in (1, 2, 3) for L in FLOW_LENS], "stream", "lit"))
    g.append(([f"fx_foamline_{L}_f{f}" for L in (8, 14, 20) for f in (0, 1)] + [f"fx_foampatch_{s}_f{f}" for s in "sml" for f in (0, 1)]
              + [f"fx_fleck_{n}_f{f}" for n in (1, 2, 3) for f in (0, 1)], "stream", "foam"))
    g.append(([f"fx_slick_{L}" for L in (24, 40)], "sea", "lighter"))
    g.append(([f"fx_catspaw_{k}" for k in (0, 1)] + [f"fx_catspaw_s_{k}" for k in (0, 1)], "lake", "lit"))
    g.append(([f"fx_wake_{d}_f{f}" for d in ("down", "up", "side") for f in range(4)], "stream", "lit"))
    g.append(([f"fx_wake_{d}_s_f{f}" for d in ("down", "up", "side") for f in range(4)], "stream", "lit"))
    g.append(([f"fx_leaf_{c}_f{f}" for c in ("yellow", "brown", "green") for f in range(4)]
              + [f"fx_leaf_{c}_s_f{f}" for c in ("yellow", "brown", "green") for f in (0, 1)] + ["fx_twig_f0", "fx_twig_f1"], "stream", "nat"))
    g.append(([f"fx_weed_f{f}" for f in range(3)] + [f"fx_weed_s_f{f}" for f in range(3)] + [f"fx_tuft_{s}" for s in range(3)], "sea", "nat"))
    g.append(([f"fx_sargassum_{s}_f{f}" for s in "sml" for f in (0, 1)] + ["fx_driftwood"], "ocean", "nat"))
    g.append(([f"fx_leaf_yellow_f{f}" for f in range(4)] + [f"fx_leaf_green_f{f}" for f in range(4)]
              + [f"fx_pollen_{s}" for s in "sml"], "lake", "nat"))
    g.append(([f"fx_duckweed_{s}" for s in "sml"] + [f"fx_leaf_dead_f{f}" for f in range(4)]
              + [f"fx_leaf_dead_s_f{f}" for f in (0, 1)], "swamp", "nat"))
    return g


def stage_view(stage, period=None):
    """The 480x270 game view (top-down RGBA): the native preview (2x) halved, or a draft period's layers."""
    if period is None:
        p = os.path.join(C.TMP, "variants", "hybrid", f"preview_{stage}.png")
        img = load_td(p)
        return img[::2, ::2].copy()
    base = os.path.join(C.TMP, "variants", "hybrid", "periods")
    back = load_td(os.path.join(base, f"{stage}_{period}_back.png"))
    front = load_td(os.path.join(base, f"{stage}_{period}_front.png"))
    out = back.copy()
    paste(out, front, 0, 0)
    return out[65:335, 80:560].copy()


def in_context():
    """Hand-placed FX on the stage views (1x), for scale and readability; returns [(label, image 480x270)]."""
    def spr(name):
        return load_td(os.path.join(WORLD, name + ".png"))

    def put(view, name, x, y, tint=None, alpha=1.0, mul=None, flip=False):
        img = spr(name)
        if flip:
            img = img[:, ::-1].copy()
        img = tinted(img, tint, alpha, mul)
        h, w = img.shape[:2]
        paste(view, img, int(x - w // 2), int(y - h // 2))
    scenes = []
    # stream, native dawn: a surge band of strong streaks, the lane, leaves, flecks, a float with its wake
    v = stage_view("stream")
    wc = water_cols("#3e7a78")
    for (x, y, n) in ((250, 120, "fx_streak_7_2"), (275, 132, "fx_streak_10_2"), (230, 140, "fx_streak_7_1"),
                      (300, 150, "fx_streak_10_3"), (262, 150, "fx_streak_14_3"), (215, 158, "fx_streak_10_3"),
                      (330, 170, "fx_streak_14_2"), (190, 182, "fx_streak_14_1"), (280, 186, "fx_streak_7_2"),
                      (240, 104, "fx_streak_4_2"), (288, 96, "fx_streak_4_1")):
        put(v, n, x, y, wc["lit"])
    put(v, "fx_foamline_14_f0", 330, 116, wc["foam"])
    put(v, "fx_foampatch_m_f0", 108, 157, wc["foam"])
    for (x, y, n) in ((246, 130, "fx_fleck_3_f0"), (302, 176, "fx_fleck_2_f0"), (222, 170, "fx_fleck_1_f0")):
        put(v, n, x, y, wc["foam"])
    put(v, "fx_leaf_yellow_f1", 268, 165)
    put(v, "fx_leaf_green_f2", 205, 196)
    put(v, "fx_leaf_brown_s_f0", 262, 108)
    put(v, "fx_twig_f1", 318, 190)
    put(v, "fx_wake_down_f1", 292, 138, wc["lit"])
    fp = os.path.join(C.SPRITES, "World", "float_stick.png")
    fl = load_td(fp) if os.path.exists(fp) else None
    if fl is not None:
        paste(v, fl, 292 - fl.shape[1] // 2, 138 - fl.shape[0] + 3)
    scenes.append(("stream dawn (native): surge streaks, leaves, flecks, float wake", v, (270, 150)))
    # sea, native day, flood running left: flow lines, a slick far out, tufts leaning left, weed, foam at the tetrapods
    v = stage_view("sea")
    wc = water_cols("#2c7aa4")
    for (x, y, n) in ((220, 110, "fx_flow_10_2"), (300, 124, "fx_flow_14_3"), (170, 140, "fx_flow_14_2"),
                      (340, 150, "fx_flow_10_1"), (250, 162, "fx_flow_14_3"), (150, 176, "fx_flow_7_2"),
                      (330, 188, "fx_flow_10_2"), (260, 88, "fx_flow_7_1"), (190, 80, "fx_flow_4_1")):
        put(v, n, x, y, wc["lit"], flip=True)
    put(v, "fx_slick_40", 320, 70, wc["lighter"])
    for (x, y) in ((98, 194), (94, 214), (379, 176), (385, 199)):
        put(v, "fx_tuft_1", x, y, flip=True)
    put(v, "fx_foampatch_l_f0", 104, 204, wc["foam"])
    put(v, "fx_foamline_20_f1", 385, 195, wc["foam"])
    put(v, "fx_weed_f1", 210, 196)
    put(v, "fx_wake_side_f2", 402, 101, wc["lit"], flip=True)
    scenes.append(("sea day (native), 들물 to the left: flow lines, slick, tufts, weed, buoy wake", v, (200, 180)))
    # ocean, native dawn: sargassum and driftwood drifting right / away, flow lines
    v = stage_view("ocean")
    wc = water_cols("#2c5c8a")
    for (x, y, n) in ((170, 120, "fx_flow_10_1"), (300, 140, "fx_flow_14_1"), (120, 160, "fx_flow_7_2"), (360, 110, "fx_flow_7_1")):
        put(v, n, x, y, wc["lit"])
    put(v, "fx_sargassum_l_f0", 150, 150)
    put(v, "fx_sargassum_m_f1", 330, 128)
    put(v, "fx_sargassum_s_f0", 260, 100)
    put(v, "fx_driftwood", 380, 165)
    put(v, "fx_foampatch_s_f1", 220, 132, wc["foam"])
    scenes.append(("ocean dawn (native): sargassum, driftwood, foam, drift lines", v, (250, 140)))
    # lake, native evening: a gust cat's paw, leaves and pollen
    v = stage_view("lake")
    wc = water_cols("#456a8a")
    put(v, "fx_catspaw_0", 300, 110, wc["lit"])
    put(v, "fx_catspaw_s_1", 160, 150, wc["lit"])
    put(v, "fx_leaf_yellow_f0", 200, 170)
    put(v, "fx_leaf_green_f3", 330, 150)
    put(v, "fx_pollen_l", 250, 130)
    put(v, "fx_pollen_m", 120, 190)
    for (x, y, n) in ((240, 90, "fx_flow_7_1"), (360, 130, "fx_flow_4_1")):
        put(v, n, x, y, wc["lit"])
    scenes.append(("lake evening (native): gust cat's paws, leaves, pollen", v, (240, 140)))
    # swamp, native evening: duckweed and a dead leaf drifting with the wind, a small cat's paw
    v = stage_view("swamp")
    wc = water_cols("#3a4a30")
    put(v, "fx_duckweed_l", 230, 140)
    put(v, "fx_duckweed_m", 300, 160)
    put(v, "fx_duckweed_s", 190, 170)
    put(v, "fx_leaf_dead_f1", 260, 175)
    put(v, "fx_catspaw_s_0", 240, 110, wc["lit"])
    scenes.append(("swamp evening (native): duckweed, dead leaf, small cat's paw", v, (250, 150)))
    # sea night (draft look): the same FX with the night rules (fxAlpha 0.7, foam towards #c8d4f0, debris x actorTint)
    try:
        v = stage_view("sea", "night")
        wc = water_cols("#2a3856", 1.0)
        for (x, y, n) in ((220, 110, "fx_flow_10_2"), (300, 124, "fx_flow_14_3"), (170, 140, "fx_flow_14_2"),
                          (250, 162, "fx_flow_14_3"), (330, 188, "fx_flow_10_2")):
            put(v, n, x, y, wc["lit"], 0.7)
        for (x, y) in ((98, 194), (94, 214), (379, 176), (385, 199)):
            put(v, "fx_tuft_2", x, y, mul=NIGHT_ACTOR)
        put(v, "fx_foampatch_l_f1", 104, 204, wc["foam"], 0.7)
        put(v, "fx_weed_f2", 210, 196, mul=NIGHT_ACTOR)
        scenes.append(("sea night (draft look), 날물 peak, combed tufts: fxAlpha 0.7, foam -> #c8d4f0, debris x #8e9ac0", v, (200, 180)))
    except Exception as e:  # the draft may be re-rendering right now
        print("TOD sheet: no sea night draft:", e)
    return scenes


def build_sheet(out_path):
    W = 1976
    bg = np.array(C.hex_rgb("#20262e"), np.float32)
    panel = hexf("#1a2230")
    blocks = []                                   # (height, draw(sheet, y0))
    # --- UI icons (8x) on the HUD panel colour
    pk = Packer(W, 16)
    ui_rows = [
        [f"tod_{p}" for p in ("dawn", "day", "evening", "night")] + [f"tod_{p}_s" for p in ("dawn", "day", "evening", "night")],
        [f"tide_{s}" for s in ("flood", "ebb", "high", "low")] + ["tide_bar_bg", "tide_bar_fill"],
        [f"cur_arrow_{k}" for k in range(8)] + ["cur_pip_on", "cur_pip_off", "cur_chevron"],
        [f"cur_arrow_s_{k}" for k in range(8)],
    ]
    for row in ui_rows:
        for name in row:
            img = load_td(os.path.join(UI, name + ".png"))
            if name.startswith("cur_arrow"):
                img = tinted(img, hexf("#a8e0ff"))
            h, w = img.shape[0] * 8 + 16, img.shape[1] * 8 + 16

            def draw(sheet, x, y, img=img, w=w, h=h):
                fill(sheet, x, y, w, h, panel)
                paste(sheet, img, x + 8, y + 8, 8)
            pk.add(w, h, draw)
        pk.newline()
    # HUD row mock at 4x (2 canvas units per sprite px): the clock panel's second row on the sea and on the stream
    mock_w, mock_h = 70, 26
    for variant in ("sea", "stream", "wind"):
        def draw(sheet, x, y, variant=variant):
            fill(sheet, x, y, mock_w * 4, mock_h * 4, hexf("#243448"))
            fill(sheet, x + 8, y + 8, mock_w * 4 - 16, mock_h * 4 - 16, panel)
            paste(sheet, load_td(os.path.join(UI, "tod_dawn.png")), x + 4 * 4, y + 2 * 4 + 4, 4)
            if variant == "sea":
                paste(sheet, load_td(os.path.join(UI, "tide_flood.png")), x + 4 * 4, y + 15 * 4, 4)
                bgb = load_td(os.path.join(UI, "tide_bar_bg.png"))
                fb = load_td(os.path.join(UI, "tide_bar_fill.png"))
                fb[:, int(24 * 0.62):, 3] = 0
                paste(sheet, bgb, x + 33 * 4, y + 16 * 4, 4)
                paste(sheet, fb, x + 35 * 4, y + 18 * 4, 4)
                paste(sheet, tinted(load_td(os.path.join(UI, "cur_arrow_s_4.png")), hexf("#a8e0ff")), x + 62 * 4, y + 16 * 4, 4)
            elif variant == "stream":
                paste(sheet, tinted(load_td(os.path.join(UI, "cur_arrow_6.png")), hexf("#a8e0ff")), x + 4 * 4, y + 15 * 4, 4)
                for k, on in enumerate((1, 1, 0)):
                    paste(sheet, load_td(os.path.join(UI, "cur_pip_on.png" if on else "cur_pip_off.png")), x + (16 + 4 * k) * 4, y + 18 * 4, 4)
            else:
                paste(sheet, tinted(load_td(os.path.join(UI, "cur_arrow_0.png")), hexf("#d8e8f0")), x + 4 * 4, y + 15 * 4, 4)
        pk.add(mock_w * 4, mock_h * 4, draw)
    pk.newline()
    blocks.append((pk.height(), pk.items))
    # --- world FX at 6x: day water, then the same at night
    pk = Packer(W, 10)
    for names, stage, role in fx_group_rows():
        for night in (False, True):
            for name in names:
                img = load_td(os.path.join(WORLD, name + ".png"))
                cell = fx_cell(img, stage, role, 6, night)

                def draw(sheet, x, y, cell=cell):
                    paste(sheet, cell, x, y, 1)
                pk.add(cell.shape[1], cell.shape[0], draw)
            pk.newline()
        pk.newline(8)
    blocks.append((pk.height(), pk.items))
    # --- in context, 2x
    pk = Packer(W, 16)
    for label, view, (zx, zy) in in_context():
        def draw(sheet, x, y, view=view):
            paste(sheet, view, x, y, 2)
        pk.add(960, 540, draw)
        z0x, z0y = max(0, min(480 - 240, zx - 120)), max(0, min(270 - 135, zy - 67))
        zoom = view[z0y:z0y + 135, z0x:z0x + 240].copy()

        def draw_zoom(sheet, x, y, zoom=zoom):
            paste(sheet, zoom, x, y, 4)
        pk.add(960, 540, draw_zoom)
        pk.newline()
        print("TOD sheet context:", label)
    blocks.append((pk.height(), pk.items))
    H = sum(h for h, _ in blocks) + 8 * len(blocks)
    sheet = np.zeros((H, W, 4), np.float32)
    sheet[..., :3] = bg
    sheet[..., 3] = 1
    y0 = 0
    for h, items in blocks:
        for x, y, draw in items:
            draw(sheet, x, y + y0)
        y0 += h + 8
        fill(sheet, 0, y0 - 5, W, 2, hexf("#3a4658"))
    C.save_pixels(sheet[::-1].copy(), out_path)
    print("TOD sheet ->", out_path, W, H)


# ============================================================================ main
def main():
    global UI, WORLD
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "dry" in argv:
        argv.remove("dry")
        UI, WORLD = os.path.join(OUT, "ui"), os.path.join(OUT, "world")
    groups = argv or ["ui", "fx", "sheet"]
    os.makedirs(WORK, exist_ok=True)
    C.reset_scene()
    if "ui" in groups:
        build_ui()
    if "fx" in groups:
        build_fx()
    for g, name, w, h, meaning in MADE:
        print(f"TOD made {g:6s} {name:24s} {w:3d}x{h:<3d} {meaning}")
    if "sheet" in groups:
        build_sheet(os.path.join(OUT, "fx_ui_sheet.png"))
    print("TOD done", len(MADE))


if __name__ == "__main__":
    main()
