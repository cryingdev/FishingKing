"""
periods/ice.py - the ICE lake's four looks (Docs/time_currents_spec.md 3.6). Owner: the ice agent.

Compass: the angler faces WEST (today's rose afterglow sits ahead-left, dx -140, after sunset). Dawn: the sun rises
BEHIND him and paints the snowy range pink (alpenglow) under a pastel sky, long shadows run away from him; day: bright
white snow, the sun high on the LEFT (above the crop, only its outer glow ring shows); night: deep navy, stars, the
aurora at full strength and the moon on the right (dx +150), moonlit blue snow, the shanty windows glowing warm with a
pool of lamplight on the snow. The "water" of the preset is the ICE surface (the play-area band = waterTint: fish
shadows are seen through it) - kept light enough at night (moonlit snow, L ~0.51).

hyb_ice.py constants a period changes (PER.const, native = today's values):
  WINDOW   [lower, upper] colours of the shanty windows (lit / dim / unlit)
  SNOW_CAP the brightest snow (ice ramp top, slush ring round the hole, bank snow): moonlit at night
  RSNOW    the far range's snow (rose afterglow today -> alpenglow / white / moonlit)
  LR, LD   the light on the far range / on the drifts (towards the light, Blender axes)
Night lights (back_post / front_post): lamplight pools on the snow in front of / beside the near shanty and a tiny one
at the far shanty, a warm glow on the boards round the windows. LOOK["night"]["lights"]: the near shanty's front window.
"""
NATIVE = "evening"          # hyb_ice.py today: blue hour after sunset with aurora

# the shanties (hyb_ice.py SHANTY / SHANTY2 / shanty()): windows and lamplight pools in world metres (Blender axes:
# x right, y forward, z up; the ice top is iceY = 0.2)
ICE_Y = 0.2
NEAR_WIN_F = (12.1, 25.77, ICE_Y + 1.38)      # front window of the near shanty (x = 12.6 - 0.5); its wall y = 25.8
NEAR_WIN_S = (11.568, 27.2, ICE_Y + 1.35)     # side window (the wall facing the centre, x = 11.59)
FAR_WIN_F = (-22.05, 53.89, ICE_Y + 1.242)
# lamplight pools on the ice plane: (centre x, y, radius along x, radius along y, strength, keep-side test)
POOLS = [   # centred on the lit wall's foot, half-ellipses spreading out of the window (a pixel-art exaggeration)
    (12.0, 25.8, 2.8, 5.0, 1.0, lambda X, Y: Y < 25.8),       # out of the front window / door, towards the camera
    (11.6, 27.2, 3.6, 2.2, 0.85, lambda X, Y: X < 11.6),      # out of the side window (the drift piled there)
    (-22.05, 53.92, 2.0, 3.4, 0.75, lambda X, Y: Y < 53.92),  # the far shanty
]
POOL_COL = "#ffcc84"
POOL_LV = ((0.62, 0.40), (0.34, 0.24), (0.10, 0.11))           # (falloff threshold, light level) inner -> outer
WIN_GLOW = "#ffc870"

PERIODS = {
    # ---------------------------------------------------------------- 새벽: sun behind him, belt of Venus ahead over
    # the blue earth shadow, alpenglow on the range, cold lilac snow, long shadows away from him
    "dawn": dict(
        sky=[(0, "#a8acc8"), (4, "#c8b0c4"), (9, "#e4b8c0"), (16, "#d4bccc"), (26, "#b0bcd8"), (42, "#8ea8d4"),
             (66, "#7494c8"), (100, "#5a80bc")],
        sun=None, glow=None, aurora=None, streaks=[],
        key_dir=(-0.25, -0.8, 0.55), key_col="#ffd8d8", shadow_col="#4a5a90",
        rim=dict(col="#ffe0e4", strength=0.15, dirs=dict(t=1.0, tl=0.6)),
        haze=dict(col="#b8b8d8", near=100.0, dist=1800.0, max=0.8),
        mist=dict(col="#dcd4e4", amount=0.5, above=3.0, below=2.0, wisp=0.5),
        water=dict(bands=["#d8d0e4", "#c0c4e0", "#a8b4d8", "#90a4cc", "#7c94c0", "#6e86b4"], dark="#5a70a0",
                   refl=["#3a4470", "#4a5680"], tint="#7c94c0", deep="#18284a"),
        grade=dict(shadow=(-0.01, 0.0, 0.04), high=(0.03, 0.0, 0.01), sat=0.85, contrast=0.05, amount=1.0,
                   gain=0.88, tint="#f4e8f4"),
    ),
    # ---------------------------------------------------------------- 낮: bright snow, clear blue sky, sun high left
    # (glow centred on the sun itself: the native glow's dx / up would otherwise stay at the afterglow)
    "day": dict(
        sky=[(0, "#e8eef4"), (6, "#d4e4f0"), (14, "#bcd6ee"), (24, "#9ec4ea"), (38, "#80b0e2"), (58, "#649cd8"),
             (86, "#4c88cc"), (110, "#3c78c0")],
        sun=dict(dx=-200, up=86, r=5.0, core="#fffff4", limb="#fff8e0"),
        glow=dict(col="#fffaf0", rings=[(16, 0.42), (36, 0.2), (70, 0.08)], soft=3.0, flat=0.9, dx=-200, up=86),
        aurora=None, streaks=[dict(up=21, x0=330, x1=470, th=1, col="#eef2f8", lit="#ffffff")],
        key_dir=(-0.45, 0.1, 0.89), key_col="#fffaf0", shadow_col="#5070a8",
        rim=dict(col="#ffffff", strength=0.2, dirs=dict(tl=1.0, t=0.8, l=0.5)),
        haze=dict(col="#d8e2ee", near=100.0, dist=2500.0, max=0.75),
        mist=dict(col="#eef2f6", amount=0.3, above=2.0, below=2.0, wisp=0.5),
        water=dict(bands=["#eef2f8", "#dce6f2", "#c8d8ec", "#b4cae4", "#a0bcdc", "#90aed2"], dark="#7e9cc4",
                   refl=["#5a6e90", "#6a80a0"], tint="#a0bcdc", deep="#1c3458"),
        grade=dict(shadow=(-0.015, 0.01, 0.02), high=(0.025, 0.015, -0.01), sat=0.95, contrast=0.05, amount=1.0,
                   gain=1.05),
    ),
    "evening": {},
    # ---------------------------------------------------------------- 밤: navy sky, stars, full aurora, moon right,
    # moonlit blue snow, the shanty windows glowing (lamplight pools: back_post / front_post)
    "night": dict(
        sky=[(0, "#28345a"), (6, "#223052"), (14, "#1c284a"), (26, "#172242"), (42, "#121c3a"), (66, "#0e1630"),
             (100, "#0a1026")],
        sun=dict(dx=150, up=17, r=3.4, core="#f8f4e4", limb="#d6d4c6"),
        glow=dict(col="#8e9cc4", rings=[(7, 0.4), (16, 0.18), (30, 0.07)], soft=2.5, flat=0.8, dx=150, up=17),
        # kept inside the game crop (its top is 24.5 px above the horizon), like today's band
        aurora=dict(cols=["#3cc08c", "#9cf0c4"], up0=7, height=16, amp=2.5, amount=0.7, x0=40, x1=620),
        stars=dict(cols=["#7c88ac", "#dce2f4"], dens=0.006, up0=9, seed=7, bright=0.25, glow_max=0.1),
        streaks=[],
        key_dir=(0.5, 0.25, 0.83), key_col="#c4d0f0", shadow_col="#141c40",
        rim=dict(col="#c8d8ff", strength=0.3, dirs=dict(r=1.0, tr=0.8, t=0.4)),
        haze=dict(col="#2a3456", near=100.0, dist=1500.0, max=0.82),
        mist=dict(col="#3e4a70", amount=0.4, above=3.0, below=2.0, wisp=0.5),
        water=dict(bands=["#7480a8", "#6c7aa2", "#64729c", "#5c6c96", "#566690", "#50608a"], dark="#44547e",
                   refl=["#1e2648", "#283258"], tint="#566690", deep="#0a1226"),
        grade=dict(shadow=(-0.02, 0.0, 0.05), high=(0.0, 0.005, 0.03), sat=0.75, contrast=0.03, amount=1.0,
                   gain=0.5, tint="#bccaff"),
    ),
}

CONSTS = {
    "dawn": dict(
        WINDOW=["#b8744a", "#e8a060"],            # a dim lamp still on inside, outshone by the dawn
        SNOW_CAP="#eee2ec",                       # pink-white snow
        RSNOW=["#f0bccc", "#ffe6ea"],             # alpenglow: the range faces the rising sun behind him
        LR=(-0.25, -0.85, 0.46),                  # the range lit from behind the angler, low
        LD=(-0.3, -0.7, 0.65),                    # drifts front-lit from behind him
    ),
    "day": dict(
        WINDOW=["#4c5a72", "#6a7c8c"],            # unlit: the glass shows the sky
        SNOW_CAP="#f4f6fc",
        RSNOW=["#d8e0f0", "#f6f8fc"],             # white snow on the range
        LR=(-0.45, 0.1, 0.89),                    # the midday sun, high left
        LD=(-0.5, 0.15, 0.85),
    ),
    "night": dict(
        WINDOW=["#ffc870", "#fff0c0"],            # lamps lit inside
        SNOW_CAP="#a4aed4",                       # moonlit snow: lighter than the ice, never white
        RSNOW=["#8a96c4", "#b0bce0"],             # moonlit range
        LR=(0.5, 0.25, 0.83),                     # the moon, right
        LD=(0.5, 0.2, 0.84),                      # drifts lit from the moon side
    ),
}

LOOK = {
    "dawn": dict(birds=False),
    "day": dict(birds=False),
    "evening": dict(birds=False),
    # canvas px (y down), measured on the night render: the near shanty's front window, the far shanty's window
    "night": dict(birds=False, lights=[dict(x=494.0, y=140.0, col="#ffc870", r=4, blink="fixed"),
                                       dict(x=140.0, y=119.5, col="#ffc870", r=2, blink="fixed")]),
}


# ============================================================================ night lights (hooks)
def _ground(ctx, z0):
    """Per-pixel world (X, Y) where each pixel's view ray meets the plane z = z0 (NaN above the horizon)."""
    import math
    import numpy as np
    import fk_persp as P
    H, W = ctx["H"], ctx["W"]
    cx, cy, cz = P.cam_pos(ctx["stand"])
    s, c = math.sin(math.radians(P.PITCH)), math.cos(math.radians(P.PITCH))
    xc = np.broadcast_to(((np.arange(W) + 0.5 - W / 2) / P.F_PX)[None, :], (H, W))
    yc = np.broadcast_to(((H / 2 - (np.arange(H) + 0.5)) / P.F_PX)[:, None], (H, W))
    dz = yc * c - s
    with np.errstate(divide="ignore", invalid="ignore"):
        t = (z0 - cz) / dz
    ok = (dz < -1e-9) & (t > 0)
    return np.where(ok, cx + t * xc, np.nan), np.where(ok, cy + t * (yc * s + c), np.nan)


def _pool_levels(ctx, z0):
    """Light level per pixel of the lamplight pools (0 or one of POOL_LV), a smooth falloff in world space on the
    plane z0, quantised to 3 flat levels with ordered-dither hand-overs (hybrid glow rings)."""
    import numpy as np
    R = ctx["R"]
    X, Y = _ground(ctx, z0)
    Xs, Ys = np.nan_to_num(X, nan=1e4), np.nan_to_num(Y, nan=1e4)
    a = np.zeros(Xs.shape)
    for (px, py, rx, ry, k, side) in POOLS:
        d = np.sqrt(((Xs - px) / rx) ** 2 + ((Ys - py) / ry) ** 2)
        f = k * np.clip(1 - d, 0, 1) ** 1.3 * side(Xs, Ys)
        a = np.maximum(a, f)
    a = a + (R.bayer(*a.shape) - 0.5) * 0.1
    lv = np.zeros(a.shape)
    for thr, level in sorted(POOL_LV):
        lv = np.where(a > thr, level, lv)
    return lv


def _light(idx, pal, ctx, mask, lv, snap):
    """Warm lamplight ADDED in linear light (lamplit snow turns peach near the lamp, lilac further out)."""
    import numpy as np
    R = ctx["R"]
    add = R.s2l(R.hexrgb(POOL_COL))
    for level in sorted(set(lv[mask & (lv > 0)].tolist())):
        def fn(c, level=level):
            return R.l2s(np.clip(R.s2l(c) + add[None, :] * level, 0, 1))
        idx, pal = R.recolour(idx, pal, mask & (lv == level), fn, snap)
    return idx, pal


def back_post(idx, pal, ctx):
    if ctx["period"] != "night":
        return idx, pal
    # lamplight on the ice round the shanties (the front layer hides what is under the shanties themselves)
    lv = _pool_levels(ctx, ICE_Y)
    return _light(idx, pal, ctx, ctx["water"], lv, 0.03)


def front_post(idx, pal, ctx):
    if ctx["period"] != "night":
        return idx, pal
    import numpy as np
    import hyb_period as PER
    kid = ctx["kid"]
    # the pools on the drifts piled against / in front of the near shanty (their snow ~0.25 m above the ice)
    lv = _pool_levels(ctx, ICE_Y + 0.25)
    lv = np.where(lv > 0.2, lv, 0.0)                     # two levels only on the front layer (colour budget)
    idx, pal = _light(idx, pal, ctx, (kid == "drift") & (idx >= 0), lv, 0.045)
    # a warm glow on the boards round the windows (never on the window glass itself)
    win = np.zeros(idx.shape, bool)
    for h in CONSTS["night"]["WINDOW"]:
        if h.lower() in pal.hex:
            win |= idx == pal.index(h)
    boards = (kid == "prop") & (idx >= 0) & ~win
    for p, rad in ((NEAR_WIN_F, 3.0), (NEAR_WIN_S, 2.6), (FAR_WIN_F, 1.6)):
        c, r = ctx["rc"](p)
        idx, pal = PER.point_glow(idx, pal, c, r, WIN_GLOW, rings=((rad, 0.3),), mask=boards, snap=0.045)
    return idx, pal
