"""
periods/lake.py - the LAKE's four looks (Docs/time_currents_spec.md 3.2). Owner: the lake agent (only this file and,
if a hook is not enough, hyb_lake.py - whose NATIVE output must stay byte-identical: see periods/README.md).

Compass: the angler faces WEST (the golden-hour sun sets ahead-right, over the bay). Dawn sun rises BEHIND him
(behind-left), the midday sun is high on his LEFT (south), the moon is placed over the bay.

  dawn     sun behind-left (no disc): pink belt of Venus over the blue earth shadow, far shore front-lit, first-light
           pink on the distant range's crest, thick pale mist, pastel grey-blue water, the cabin window dim amber
  day      sun high on the left (above the crop, glow ring only), clear blue sky, blue-teal water, sparse white
           glitter on the far left water, the cabin window unlit (it shows the sky)
  evening  today's golden hour (native, untouched)
  night    full moon over the bay (dx +110, up 16) with a silver moon path, stars, navy water; the cabin window lit
           (glow rings + a broken streak on the water below it); a hurricane lantern on the front-left pier post
           (baked glow on the post, a soft warm pool on the planks; its reflection falls under the pier / below the
           game crop, so the water gets none); fireflies (Unity). LOOK lights: window "fixed", lantern "flicker".

Pure data at the top (Docs tooling reads it with plain Python); numpy is imported inside the hooks.
"""
NATIVE = "evening"          # hyb_lake.py today: golden hour

PERIODS = {
    # ---------------------------------------------------------------- 새벽: sun behind him, pink belt of Venus ahead,
    # blue earth shadow on the horizon, the far shore front-lit (no rims), thick pale mist on the water
    "dawn": dict(
        sky=[(0, "#9ea4c0"), (4, "#c0a8bc"), (9, "#e0b4b8"), (15, "#d8b8c8"), (22, "#b8bcd8"), (32, "#9aaed6"),
             (46, "#80a0ce"), (68, "#688cc2"), (100, "#5076b2")],
        sun=None, glow=None, aurora=None,
        streaks=[dict(up=18, x0=370, x1=520, th=2, col="#c4a4bc", lit="#e8c0c4")],
        key_dir=(-0.3, -0.8, 0.52), key_col="#ffd8c8", shadow_col="#4c5c8e",
        rim=dict(col="#ffe4dc", strength=0.18, dirs=dict(t=1.0, tl=0.6)),
        haze=dict(col="#b8b8d4", near=150.0, dist=800.0, max=0.86),
        mist=dict(col="#dcd8e8", amount=0.95, above=4.0, below=3.0, wisp=0.45),
        # no sun ahead: no glitter, and the (sourceless) sheen column is cut to a few rows at the far waterline
        glitter=dict(cols=["#e8c4cc", "#f8e4e8"], far=0.05, dens=0.0, width=(4.0, 20.0), maxlen=3),
        # (the pads must stay lighter than bands[3] / [4]: pads L ~0.54 at gain 0.92 vs water 0.52 / 0.50)
        water=dict(bands=["#b8a6bc", "#9696b4", "#7888a8", "#546c8a", "#4e6684", "#465e7a"], dark="#3a5270",
                   refl=["#30405a", "#3e4e66"], tint="#4e6684", deep="#12243a"),
        grade=dict(shadow=(-0.03, 0.01, 0.04), high=(0.03, 0.0, 0.01), sat=0.85, contrast=0.04, amount=1.0,
                   gain=0.92, tint="#f0e6f4"),
    ),
    # ---------------------------------------------------------------- 낮: sun high on the left (above the crop, only
    # its outer glow ring shows), clear blue sky, blue-teal water, sparse white glitter
    "day": dict(
        sky=[(0, "#e4ecee"), (6, "#cfe2ec"), (14, "#b4d4ea"), (24, "#98c4e6"), (36, "#7eb2e0"), (56, "#66a0d8"),
             (84, "#5290d0"), (110, "#4482c8")],
        sun=dict(dx=-200, up=86, r=5.0, core="#fffff4", limb="#fff8e0"),
        glow=dict(col="#fffaf0", rings=[(16, 0.42), (36, 0.2), (70, 0.08)], soft=3.0, flat=0.9),
        streaks=[dict(up=20, x0=290, x1=450, th=2, col="#eef4f6", lit="#ffffff")], aurora=None,
        key_dir=(-0.45, 0.05, 0.89), key_col="#fff4e0", shadow_col="#46628a",
        rim=dict(col="#fff8ea", strength=0.22, dirs=dict(tl=1.0, t=0.8, l=0.5)),
        haze=dict(col="#cad8e4", near=150.0, dist=1400.0, max=0.8),
        mist=dict(col="#e6eef2", amount=0.25, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#f4f8fc", "#ffffff"], far=0.3, dens=0.2, width=(6.0, 26.0), maxlen=3),
        # (pads L ~0.61 at gain 1.08 vs water bands[3] / [4] 0.59 / 0.56)
        water=dict(bands=["#b8d0dc", "#98bcd0", "#7aa6c4", "#5684a6", "#4a7a9e", "#407092"], dark="#346488",
                   refl=["#2c4a4c", "#3a5a5a"], tint="#4a7a9e", deep="#10304a"),
        grade=dict(shadow=(-0.015, 0.01, 0.02), high=(0.025, 0.015, -0.01), sat=1.0, contrast=0.05, amount=1.0,
                   gain=1.08),
    ),
    "evening": {},
    # ---------------------------------------------------------------- 밤: full moon over the bay, stars, dark blue
    # water with a silver moon path, the cabin window lit brighter (CONSTS), cool moon rim from the right
    "night": dict(
        sky=[(0, "#34405e"), (5, "#2c3856"), (12, "#24304e"), (22, "#1e2946"), (34, "#19233e"), (52, "#141d36"),
             (80, "#10182e"), (110, "#0c1226")],
        sun=dict(dx=110, up=16, r=3.4, core="#f8f4e4", limb="#d6d4c6"),
        glow=dict(col="#8e9cc4", rings=[(7, 0.4), (16, 0.18), (30, 0.07)], soft=2.5, flat=0.8),
        streaks=[dict(up=19, x0=370, x1=520, th=2, col="#2e3a5a", lit="#6c7ca4")], aurora=None,
        stars=dict(cols=["#7c88ac", "#dce2f4"], dens=0.006, up0=9, seed=7, bright=0.25, glow_max=0.1),
        key_dir=(0.55, 0.2, 0.81), key_col="#c8d4f0", shadow_col="#161c38",
        rim=dict(col="#c4d4ff", strength=0.35, dirs=dict(r=1.0, tr=0.85, t=0.5)),
        haze=dict(col="#2a3452", near=150.0, dist=700.0, max=0.86),
        mist=dict(col="#46506e", amount=0.5, above=2.4, below=2.0, wisp=0.45),
        glitter=dict(cols=["#9aa8c8", "#e0e6f4"], far=0.3, dens=0.4, width=(3.0, 16.0), maxlen=3),
        # (pads L ~0.37 at gain 0.42 vs water bands[3] / [4] 0.36 / 0.34)
        water=dict(bands=["#445070", "#3c4868", "#344262", "#2e3c5c", "#2a3856", "#26324e"], dark="#222e4c",
                   refl=["#141a2a", "#1c2436"], tint="#2a3856", deep="#080e1c"),
        grade=dict(shadow=(-0.02, 0.0, 0.05), high=(0.0, 0.005, 0.03), sat=0.7, contrast=0.03, amount=1.0,
                   gain=0.42, tint="#bccaff"),
    ),
}

# hyb_lake.py constants a period changes (PER.const)
CONSTS = {
    "dawn": {"WINDOW": "#e8a060"},      # someone is up: a dim warm window
    "day": {"WINDOW": "#6a7c8c"},       # unlit: the glass shows the sky
    "night": {"WINDOW": "#ffd27a"},     # bright; its glow + water streak in back_post
}

# ------------------------------------------------------------------ night light sources (canvas px, y down)
WINDOW_PX = (208.0, 94.5)           # the cabin window (4 x 3 px: cols 206-209, rows 93-95 of every lake render)
WIN_GLOW = "#ffc870"
POST_TOP = (-1.38, 0.95, 1.62)      # hyb_lake.py: the front-left pier post (x, y, top) - the lantern stands on it
LANTERN = [                         # a hurricane lantern, 7 x 12 px (D iron, M lit metal, G glass, F bright, W flame)
    "...D...",
    "..D.D..",
    "..DDD..",
    ".DMMMD.",
    ".DGFGD.",
    "DGGFGGD",
    "DGFWFGD",
    "DGGFGGD",
    ".DGGGD.",
    ".DMMMD.",
    "DMMMMMD",
    ".DDDDD.",
]
LANTERN_COLS = {"D": "#262434", "M": "#6e5a48", "G": "#e8a24e", "F": "#ffd070", "W": "#fff4c8"}
LANTERN_LIGHT = "#ffc870"
LANTERN_FLAME = (260.5, 266.5)      # canvas px of the flame (= the stamp's W pixel centre; checked in front_post)

# Unity look extras (Data/Periods/lake_<p>.json); lights = animated overlays in canvas px (x, y top-down)
LOOK = {
    "dawn": dict(birds=True),
    "day": dict(birds=True),
    "evening": dict(birds=True),
    "night": dict(birds=False, fireflies=True, lights=[
        dict(x=WINDOW_PX[0], y=WINDOW_PX[1], col="#ffc870", r=4, blink="fixed"),
        dict(x=LANTERN_FLAME[0], y=LANTERN_FLAME[1], col="#ffc870", r=5, blink="flicker"),
    ]),
}


# ============================================================================ hooks (non-native periods only)
def _grid(H, W):
    import numpy as np
    return np.arange(H)[:, None] + 0.5, np.arange(W)[None, :] + 0.5


def _pool(idx, pal, R, cx, cy, rx, ry, col, k, mask, core=0.5, k_edge=None, snap=0.04):
    """Soft elliptical light pool (rx, ry px) around (cx, cy): a flat core (d < core) at k, then a Bayer-dithered
    falloff to the edge at k_edge (default k / 2) - two levels only, sRGB mix towards col, always a little lighter."""
    import numpy as np
    rows, cols = _grid(*idx.shape)
    d = np.sqrt(((cols - cx) / rx) ** 2 + ((rows - cy) / ry) ** 2)
    m0 = (d < core) & mask & (idx >= 0)
    edge = (d >= core) & (d < 1.0) & mask & (idx >= 0)
    edge &= R.bayer(*idx.shape) < (1.0 - d) / (1.0 - core)
    idx, pal = R.blend_idx(idx, pal, m0, col, k, lighter=True, snap=snap, space="srgb")
    return R.blend_idx(idx, pal, edge, col, k * 0.5 if k_edge is None else k_edge, lighter=True, snap=snap,
                       space="srgb")


def _stamp(idx, pal, R, art, cmap, col, row_bottom):
    """Hand-drawn sprite into the palette image, centred on column col, its last row on row_bottom. Returns
    (idx, pal, mask of the stamped pixels, (col, row) centre of its 'W' pixel)."""
    import numpy as np
    keys = list(cmap)
    pal, ids = pal.extend([R.hexrgb(cmap[k]) for k in keys], snap=0.0)
    lut = dict(zip(keys, ids))
    H, W = idx.shape
    x0 = int(col) - len(art[0]) // 2
    y0 = int(row_bottom) - len(art) + 1
    m = np.zeros(idx.shape, bool)
    flame = None
    for j, line in enumerate(art):
        for i, ch in enumerate(line):
            y, x = y0 + j, x0 + i
            if ch in lut and 0 <= y < H and 0 <= x < W:
                idx[y, x] = lut[ch]
                m[y, x] = True
                if ch == "W":
                    flame = (x + 0.5, y + 0.5)
    return idx, pal, m, flame


def front_post(idx, pal, ctx):
    """Night: the lantern on the front-left pier post + its light on the post, rope and the planks."""
    if ctx["period"] != "night":
        return idx, pal
    import numpy as np
    import hyb_period as PERH
    R, rc, kid = ctx["R"], ctx["rc"], ctx["kid"]
    c, r = rc(POST_TOP)
    solid = idx >= 0
    idx, pal, lm, flame = _stamp(idx, pal, R, LANTERN, LANTERN_COLS, c, int(r))
    print("HYB PERIOD lake night lantern flame at", flame, "(LOOK uses", LANTERN_FLAME, ")")
    fc, fr = flame
    # the lamp's own light on the post / rope wrap (flat rings, never on the lantern itself)
    idx, pal = PERH.point_glow(idx, pal, fc, fr, LANTERN_LIGHT, rings=((4.5, 0.42), (9.0, 0.2)), mask=solid & ~lm,
                               snap=0.045)
    # a soft warm pool on the deck below it (the planks at the front-left corner of the pier)
    pc, pr_ = rc((POST_TOP[0] + 0.5, POST_TOP[1] - 0.35, 1.0))
    deck = np.isin(kid, ["plank", "pier", "prop"]) & ~lm
    idx, pal = _pool(idx, pal, R, pc, pr_, 28.0, 10.0, LANTERN_LIGHT, 0.28, deck, core=0.45, k_edge=0.2)
    return idx, pal


def back_post(idx, pal, ctx):
    """Dawn: first light on the range. Night: the lit cabin window (glow rings + a broken streak on the water below)."""
    import numpy as np
    R, water, rc = ctx["R"], ctx["water"], ctx["rc"]
    if ctx["period"] == "dawn":
        return _first_light(idx, pal, ctx)
    if ctx["period"] != "night":
        return idx, pal
    import hyb_period as PERH
    wc, wr = WINDOW_PX
    # a small warm halo (the window is 4 x 3 px on a 20 px cabin: no bigger than the cabin)
    idx, pal = PERH.point_glow(idx, pal, wc, wr, WIN_GLOW, rings=((2.5, 0.5), (5.0, 0.24), (8.0, 0.1)), mask=~water,
                               snap=0.04)
    shore = int(np.flatnonzero(water[:, int(wc)]).min())
    idx, pal = PERH.light_streak(idx, pal, water, wc, shore, shore + 14, WIN_GLOW, half=(1.0, 2.5), dens=0.7,
                                 seed=5, snap=0.04)
    print("HYB PERIOD lake night window", WINDOW_PX, "streak rows", shore, shore + 14)
    return idx, pal


def _first_light(idx, pal, ctx):
    """Dawn: the sun (behind him, just up) catches only the crest of the distant range: a 1-2 px pink-gold top rim."""
    import numpy as np
    R, kid = ctx["R"], ctx["kid"]
    for kinds, lv in ((("range",), (0.42, 0.2)), (("hill",), (0.22, 0.0))):
        m = np.isin(kid, kinds)
        top1 = m & ~R.shift(m, 1, 0, False)
        top2 = m & ~top1 & ~R.shift(m & ~top1, 1, 0, False)
        idx, pal = R.blend_idx(idx, pal, top1, "#f8c4b4", lv[0], lighter=True, snap=0.03)
        if lv[1]:
            idx, pal = R.blend_idx(idx, pal, top2, "#f8c4b4", lv[1], lighter=True, snap=0.03)
    return idx, pal
