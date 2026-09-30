"""
periods/stream.py - the STREAM's four looks (Docs/time_currents_spec.md 3.3). Owner: the stream agent.

Compass: the angler faces EAST, up the valley (the early sun rises in the notch ahead-left). The midday sun is high on
his RIGHT (south), the evening sun sets BEHIND him (the valley walls and ridge glow warm, front-lit), the moon rises
in the same notch. Geometry anchors (the notch, the keep-out boxes) stay on the NATIVE sun (hyb_stream.py SUN_C);
the sheen / glitter follow this period's light (LIGHT_C).

  dawn     today's clear early morning (native, untouched)
  day      sun high on the right (above the crop, glow ring only), mist burnt off, clear green-teal water
  evening  sun low BEHIND-RIGHT (no disc): pink belt of Venus over the notch, the valley front-lit gold, alpenglow on
           the crests of the range / far / mid slopes, the waterfall warm, warm mist
  night    full moon in the notch (dx -70, up 18) with a short silver path, stars, moonlit valley mist, blue-teal
           water, a dimmed moonlit waterfall and foam; a hurricane lantern on the angler's boulder (left of his feet:
           a warm pool on the rock). LOOK light: lantern "flicker".
"""
NATIVE = "dawn"             # hyb_stream.py today: clear early morning, valley mist

PERIODS = {
    "dawn": {},
    # ---------------------------------------------------------------- 낮: sun high on the right, mist burnt off,
    # clear green-teal water
    "day": dict(
        sky=[(0, "#e8eee6"), (8, "#d8e8e4"), (16, "#bcdcea"), (26, "#9ccae6"), (40, "#7eb6e0"), (60, "#66a2d8"),
             (100, "#4c8ccc")],
        sun=dict(dx=150, up=86, r=5.0, core="#fffff4", limb="#fff8e0"),
        glow=dict(col="#fffaf0", rings=[(16, 0.42), (36, 0.2), (70, 0.08)], soft=3.0, flat=0.9),
        streaks=[], aurora=None,
        key_dir=(0.45, 0.1, 0.89), key_col="#fff4e0", shadow_col="#44628a",
        rim=dict(col="#fff6e0", strength=0.22, dirs=dict(tr=1.0, t=0.8, r=0.5)),
        haze=dict(col="#cfe0dc", near=40.0, dist=900.0, max=0.75),
        mist=dict(col="#eef2ea", amount=0.35, above=3.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#f4f8f4", "#ffffff"], far=0.16, dens=0.2, width=(3.0, 12.0), maxlen=2),
        water=dict(bands=["#c8dcd4", "#a0c8bc", "#76aca4", "#58968e", "#468680", "#3c7a76"], dark="#306c6a",
                   refl=["#1c3a30", "#2a4a3e"], tint="#468680", deep="#0e2e30"),
        grade=dict(shadow=(-0.015, 0.01, 0.02), high=(0.025, 0.015, -0.01), sat=1.0, contrast=0.05, amount=1.0,
                   gain=1.04),
    ),
    # ---------------------------------------------------------------- 저녁: sun low behind-right, warm front light on
    # the valley (more side light than straight from behind, so the boulders keep their form), pink belt over the
    # ridge, alpenglow on the crests (back_post), warm-tinted mist
    "evening": dict(
        sky=[(0, "#c4a8b8"), (5, "#e8b4a8"), (12, "#f0bc9c"), (20, "#dcb4ac"), (30, "#b4a8c0"), (44, "#8a9cc8"),
             (66, "#6a88c0"), (100, "#4c70b0")],
        sun=None, glow=None, streaks=[], aurora=None,
        key_dir=(0.62, -0.52, 0.58), key_col="#ffc488", shadow_col="#4a4a7c",
        rim=dict(col="#ffd0a0", strength=0.22, dirs=dict(t=1.0, tr=0.6)),
        haze=dict(col="#dcc0b8", near=40.0, dist=600.0, max=0.8),
        mist=dict(col="#f0d4c0", amount=0.6, above=5.0, below=3.0, wisp=0.5),
        glitter=dict(cols=["#f0d4c4", "#fff0e4"], far=0.16, dens=0.05, width=(3.0, 12.0), maxlen=2),
        water=dict(bands=["#d4b8b8", "#b0a8b8", "#8a9cac", "#6a8c96", "#567e84", "#4a7276"], dark="#406668",
                   refl=["#2a3032", "#3a3e40"], tint="#567e84", deep="#10282c"),
        grade=dict(shadow=(-0.03, 0.012, 0.035), high=(0.06, 0.02, -0.05), sat=0.9, contrast=0.05, amount=1.0,
                   gain=0.92, tint="#ffd8b4"),
    ),
    # ---------------------------------------------------------------- 밤: moon in the notch, stars, moonlit valley
    # mist, teal-blue water
    "night": dict(
        sky=[(0, "#34405e"), (5, "#2c3856"), (12, "#24304e"), (22, "#1e2946"), (34, "#19233e"), (52, "#141d36"),
             (80, "#10182e"), (110, "#0c1226")],
        sun=dict(dx=-70, up=18, r=3.4, core="#f8f4e4", limb="#d6d4c6"),
        glow=dict(col="#8e9cc4", rings=[(7, 0.4), (16, 0.18), (30, 0.07)], soft=2.5, flat=0.8),
        streaks=[], aurora=None,
        stars=dict(cols=["#7c88ac", "#dce2f4"], dens=0.006, up0=9, seed=7, bright=0.25, glow_max=0.1),
        key_dir=(-0.6, 0.2, 0.78), key_col="#c8d4f0", shadow_col="#161c38",
        rim=dict(col="#c0d0f8", strength=0.3, dirs=dict(l=1.0, tl=0.8, t=0.5)),
        haze=dict(col="#26324a", near=40.0, dist=600.0, max=0.85),
        mist=dict(col="#56647c", amount=0.85, above=6.0, below=4.0, wisp=0.5),
        glitter=dict(cols=["#9aacc4", "#dce6f0"], far=0.16, dens=0.35, width=(3.0, 12.0), maxlen=2),
        water=dict(bands=["#4a5a70", "#42546a", "#3b4e64", "#35485e", "#304458", "#2a3e52"], dark="#24384c",
                   refl=["#121c24", "#1a262e"], tint="#304458", deep="#081418"),
        grade=dict(shadow=(-0.02, 0.0, 0.05), high=(0.0, 0.005, 0.03), sat=0.7, contrast=0.03, amount=1.0,
                   gain=0.42, tint="#b8c8f8"),
    ),
}

# hyb_stream.py constants a period changes (PER.const): the waterfall (body, foam) and the foam at the stones are
# not graded, so they follow the light here
CONSTS = {
    "evening": {"FALL": ["#e2d2c8", "#fff0e2"], "FOAM": "#f2e6dc"},
    "night": {"FALL": ["#8a9ab2", "#c8d4e8"], "FOAM": "#aebcd0"},
}

# ------------------------------------------------------------------ night light source (canvas px, y down)
LANTERN_AT = (-0.85, 0.3)           # on the angler's boulder plateau, left of his feet (world x, y; z = standH)
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
LANTERN_FLAME = (281.5, 303.5)      # canvas px of the flame (checked in front_post)

LOOK = {
    "dawn": dict(birds=True),
    "day": dict(birds=True),
    "evening": dict(birds=True),
    "night": dict(birds=False, lights=[dict(x=LANTERN_FLAME[0], y=LANTERN_FLAME[1], col="#ffc870", r=5,
                                            blink="flicker")]),
}


# ============================================================================ hooks (non-native periods only)
def _pool(idx, pal, R, cx, cy, rx, ry, col, levels, mask, snap=0.035):
    """Flat elliptical light pool (rx, ry px) around (cx, cy): levels [(radius fraction, k)] inner first."""
    import numpy as np
    rows = np.arange(idx.shape[0])[:, None] + 0.5
    cols = np.arange(idx.shape[1])[None, :] + 0.5
    d = np.sqrt(((cols - cx) / rx) ** 2 + ((rows - cy) / ry) ** 2)
    done = np.zeros(idx.shape, bool)
    for fr, k in sorted(levels):
        m = (d < fr) & ~done & mask & (idx >= 0)
        idx, pal = R.blend_idx(idx, pal, m, col, k, lighter=True, snap=snap, space="srgb")
        done |= d < fr
    return idx, pal


def _stamp(idx, pal, R, art, cmap, col, row_bottom):
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
    """Night: the lantern on the boulder plateau + its light on the rock (flat rings + a warm pool on the top)."""
    if ctx["period"] != "night":
        return idx, pal
    import numpy as np
    import hyb_period as PERH
    R, rc, kid, stand = ctx["R"], ctx["rc"], ctx["kid"], ctx["stand"]
    c, r = rc((LANTERN_AT[0], LANTERN_AT[1], stand))
    on_rock = kid[int(r), int(c)] == "rock"
    solid = idx >= 0
    idx, pal, lm, flame = _stamp(idx, pal, R, LANTERN, LANTERN_COLS, c, int(r))
    print("HYB PERIOD stream night lantern base", (round(c, 1), round(r, 1)), "on rock", on_rock, "flame at", flame,
          "(LOOK uses", LANTERN_FLAME, ")")
    fc, fr = flame
    rockm = np.isin(kid, ["rock", "moss", "tuft"]) & ~lm
    idx, pal = PERH.point_glow(idx, pal, fc, fr, LANTERN_LIGHT, rings=((5.0, 0.45), (10.0, 0.26), (16.0, 0.12)),
                               mask=solid & ~lm, snap=0.04)
    idx, pal = _pool(idx, pal, R, c + 1.0, r + 1.5, 32.0, 11.0, LANTERN_LIGHT, [(0.5, 0.28), (1.0, 0.12)], rockm,
                     snap=0.04)
    return idx, pal


def back_post(idx, pal, ctx):
    """Evening: alpenglow - the low sun behind him lights the crests of the range and the far / mid slopes."""
    if ctx["period"] != "evening":
        return idx, pal
    import numpy as np
    R, kid = ctx["R"], ctx["kid"]
    for kinds, lv in ((("range",), (0.42, 0.22)), (("far",), (0.36, 0.16)), (("mid",), (0.26, 0.0)),
                      (("wall", "wtree", "cliff"), (0.2, 0.0))):
        m = np.isin(kid, kinds)
        top1 = m & ~R.shift(m, 1, 0, False)
        top2 = m & ~top1 & R.shift(top1, 1, 0, False)
        idx, pal = R.blend_idx(idx, pal, top1, "#ffc8a0", lv[0], lighter=True, snap=0.03)
        if lv[1]:
            idx, pal = R.blend_idx(idx, pal, top2, "#ffc8a0", lv[1], lighter=True, snap=0.03)
    return idx, pal
