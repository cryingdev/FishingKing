"""
periods/ocean.py - the OCEAN (boat)'s four looks (Docs/time_currents_spec.md 3.7). Owner: the ocean agent.

Compass: the angler faces EAST from the bow (the sun rises ahead-left, dx -90, over open sea). The midday sun is high
on his RIGHT (south: above the crop, only its outer glow ring shows, white glitter under it far right), the evening
sun sets BEHIND the boat (the islands, freighter and lighthouse lit gold from the front, a pink belt of Venus ahead,
no sun path on the water), the moon rises in the sunrise gap (dx -90) with a silver path.

hyb_ocean.py constants a period changes (PER.const, native = today's values):
  LAMP   the lighthouse lantern (on at dawn / evening / night, off by day: the glass shows the sky)
  SHEEN  the warm sheen under the sun column (0 in the evening: the sun is behind the boat)
Lights (back_post): evening - a small glow round the lit lamp + a warm gold cast on the far silhouettes (front light
of the low sun behind the boat); night - the lamp's glow rings + its broken streak on the water, the freighter's
bridge / deck lights, masthead light and (we see its starboard side: bow to the right) the green sidelight, with a
faint streak under them. LOOK["night"]["lights"]: lighthouse "flash2", freighter "fixed" (canvas px, measured).
"""
NATIVE = "dawn"             # hyb_ocean.py today: sunrise

# the freighter (hyb_ocean.py ship(xcol(392, D_SHIP), D_SHIP)): L 112 m, beam 16 m, bow to the right, bridge aft
SHIP_COL, SHIP_Y, SHIP_L = 392.0, 2000.0, 112.0

PERIODS = {
    "dawn": {},
    # ---------------------------------------------------------------- 낮: sun high right, deep clean blue, white glints
    "day": dict(
        sky=[(0, "#e0eaee"), (8, "#c4dcea"), (18, "#a2c8e6"), (32, "#7eb2e0"), (52, "#5c9ad8"), (80, "#4484cc"),
             (110, "#3470c0")],
        sun=dict(dx=160, up=86, r=5.0, core="#fffff4", limb="#fff8e0"),
        glow=dict(col="#fffaf0", rings=[(16, 0.42), (36, 0.2), (70, 0.08)], soft=3.0, flat=0.9),
        streaks=[dict(up=18, x0=300, x1=470, th=1, col="#e8eef2", lit="#ffffff")], aurora=None,
        key_dir=(0.45, 0.1, 0.89), key_col="#fff6e4", shadow_col="#3a5a8a",
        rim=dict(col="#fff8ea", strength=0.22, dirs=dict(tr=1.0, t=0.8, r=0.5)),
        haze=dict(col="#cadae6", near=300.0, dist=4000.0, max=0.75),
        mist=dict(col="#e4ecf0", amount=0.25, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#eef6fc", "#ffffff"], far=0.45, dens=0.3, width=(8.0, 34.0), maxlen=3),
        water=dict(bands=["#a8c8dc", "#84b0cc", "#5e94bc", "#3e7aa8", "#2c6698", "#22588a"], dark="#1c4c7c",
                   refl=["#1a3a56", "#284a66"], tint="#2c6698", deep="#082040"),
        grade=dict(shadow=(-0.015, 0.01, 0.02), high=(0.025, 0.015, -0.01), sat=1.0, contrast=0.05, amount=1.0,
                   gain=1.04),
    ),
    # ---------------------------------------------------------------- 저녁: sun behind the boat, gold front light on
    # the islands / ship, pink belt ahead, lilac-blue sea, no sun path (glitter off, sheen off: CONSTS)
    "evening": dict(
        sky=[(0, "#c4a8b8"), (5, "#e8b4a8"), (12, "#f0bc9c"), (20, "#dcb4ac"), (30, "#b4a8c0"), (44, "#8a9cc8"),
             (66, "#6a88c0"), (100, "#4c70b0")],
        sun=None, glow=None, aurora=None,
        streaks=[dict(up=16, x0=120, x1=300, th=2, col="#d8a8a8", lit="#f4c8b0")],
        key_dir=(0.1, -0.85, 0.52), key_col="#ffc890", shadow_col="#484a80",
        rim=dict(col="#ffd0a0", strength=0.22, dirs=dict(t=1.0, tr=0.5, tl=0.5)),
        haze=dict(col="#e4c0b8", near=300.0, dist=3000.0, max=0.8),
        mist=dict(col="#f0d4c8", amount=0.35, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#f0d0c4", "#fff0e8"], far=0.45, dens=0.0, width=(5.0, 30.0), maxlen=3),
        water=dict(bands=["#d8b4ac", "#b0a4b4", "#8894b0", "#647ea4", "#4a6c96", "#3e5e88"], dark="#34547c",
                   refl=["#243048", "#324058"], tint="#4a6c96", deep="#0c2240"),
        grade=dict(shadow=(-0.03, 0.012, 0.035), high=(0.045, 0.012, -0.04), sat=0.92, contrast=0.05, amount=1.0,
                   gain=0.9, tint="#ffe8d4"),
    ),
    # ---------------------------------------------------------------- 밤: moon in the sunrise gap, long silver path,
    # stars, navy sea, the lighthouse lamp and the freighter lit (back_post)
    "night": dict(
        sky=[(0, "#34405e"), (5, "#2c3856"), (12, "#24304e"), (22, "#1e2946"), (34, "#19233e"), (52, "#141d36"),
             (80, "#10182e"), (110, "#0c1226")],
        sun=dict(dx=-90, up=15, r=3.4, core="#f8f4e4", limb="#d6d4c6"),
        glow=dict(col="#8e9cc4", rings=[(7, 0.4), (16, 0.18), (30, 0.07)], soft=2.5, flat=0.8),
        streaks=[dict(up=21, x0=110, x1=290, th=2, col="#26304e", lit="#56668e")], aurora=None,
        stars=dict(cols=["#7c88ac", "#dce2f4"], dens=0.006, up0=9, seed=7, bright=0.25, glow_max=0.1),
        key_dir=(-0.55, 0.25, 0.8), key_col="#c8d4f0", shadow_col="#161c38",
        rim=dict(col="#c4d4ff", strength=0.32, dirs=dict(l=1.0, tl=0.85, t=0.55)),
        haze=dict(col="#283250", near=300.0, dist=2500.0, max=0.85),
        mist=dict(col="#3e4a6a", amount=0.3, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#9aa8c8", "#e0e6f4"], far=0.45, dens=0.45, width=(4.0, 26.0), maxlen=4),
        water=dict(bands=["#445070", "#3c4868", "#344262", "#2e3c5c", "#283656", "#22304e"], dark="#1c2a48",
                   refl=["#101626", "#182032"], tint="#283656", deep="#040a18"),
        grade=dict(shadow=(-0.02, 0.0, 0.05), high=(0.0, 0.005, 0.03), sat=0.7, contrast=0.03, amount=1.0,
                   gain=0.42, tint="#bccaff"),
    ),
}

CONSTS = {
    "day": dict(LAMP="#d8d0c0"),                 # off: the lantern glass shows the sky
    "evening": dict(LAMP="#ffd890", SHEEN=0.0),  # on at dusk; no sun sheen ahead (the sun is behind the boat)
    "night": dict(LAMP="#fff4c0"),               # full brightness
}

LOOK = {
    "dawn": dict(birds=True),
    "day": dict(birds=True),
    # the sun is behind: the water's glints are few and pink (the sky ahead), not gold
    "evening": dict(birds=True, glintCol="#f4d4cc", glintDensity=0.5),
    # canvas px (y down), measured on the night render: the lamp centre / the freighter's bridge lights
    "night": dict(birds=False, lights=[dict(x=514.0, y=76.5, col="#fff0b0", r=6, blink="flash2"),
                                       dict(x=381.0, y=85.5, col="#ffe0a0", r=2, blink="fixed")]),
}


# ============================================================================ lights (hooks)
def _lamp_px(idx, pal, ctx):
    """Canvas (col, row) centre of the lit lantern (the LAMP colour on the tower) and its mask."""
    import numpy as np
    lamp_hex = CONSTS[ctx["period"]]["LAMP"].lower()
    m = np.zeros(idx.shape, bool)
    if lamp_hex in pal.hex:
        m = (idx == pal.index(lamp_hex)) & (ctx["kid"] == "tower")
    if not m.any():
        return 513.5, 76.5, m
    rr, cc = np.nonzero(m)
    return float(cc.mean()) + 0.5, float(rr.mean()) + 0.5, m


def _put(idx, pal, pts, hexc, R):
    """1 px lights: set the canvas pixels under the points to hexc (added to the palette)."""
    pal2, ni = pal.extend([R.hexrgb(hexc)], snap=0.0)
    for c, r in pts:
        ci, ri = int(c), int(r)
        if 0 <= ri < idx.shape[0] and 0 <= ci < idx.shape[1]:
            idx[ri, ci] = ni[0]
    return idx, pal2


def back_post(idx, pal, ctx):
    import numpy as np
    import hyb_period as PER
    R, kid, water, rc = ctx["R"], ctx["kid"], ctx["water"], ctx["rc"]
    p = ctx["period"]
    if p == "day":
        return idx, pal
    lc, lr, lamp = _lamp_px(idx, pal, ctx)
    print("HYB OCEAN lamp px", round(lc, 2), round(lr, 2), "n", int(lamp.sum()))
    if p == "evening":
        # the low sun behind the boat lights the far silhouettes from the front: a warm gold cast, stronger on
        # their lit (lighter) tones - the white tower turns gold
        far = np.isin(kid, ["far", "island", "scrub", "rock", "ship", "tower"]) & (idx >= 0) & ~lamp
        gold = R.hexrgb("#ffc488")

        def fn(c):
            L = R.oklab(np.clip(c, 0, 1))[:, 0]
            k = (0.08 + 0.26 * np.clip((L - 0.3) / 0.45, 0, 1))[:, None]
            return c * (1 - k) + gold[None, :] * k
        idx, pal = R.recolour(idx, pal, far, fn, 0.02)
        idx, pal = PER.point_glow(idx, pal, lc, lr, "#ffe0a0", rings=((2.0, 0.4),), mask=~lamp)
        return idx, pal
    # ---- night: the lighthouse lamp (glow rings over the sky / island, broken streak on the water below)
    idx, pal = PER.point_glow(idx, pal, lc, lr, "#ffe8a0", rings=((3.0, 0.55), (7.0, 0.28), (12.0, 0.11)),
                              mask=~lamp & ~water, flat=0.9)
    col_w = int(lc)
    wr = np.flatnonzero(water[:, col_w])
    if len(wr):
        r0 = int(wr.min()) + 1
        idx, pal = PER.light_streak(idx, pal, water, lc, r0, r0 + 16, "#ffe8a0", half=(0.5, 1.6), dens=0.6, seed=9)
    # ---- the freighter: bridge windows + deck lights (warm), masthead (white), starboard sidelight (green)
    st = ctx["stand"]
    xs = R.col_to_x(SHIP_COL, SHIP_Y, st)
    xb = xs - SHIP_L / 2 + 11                    # bridge block centre x (hyb_ocean.ship)
    yn = SHIP_Y - 8.2                            # the near (starboard) side of the hull / bridge
    warm = [rc((xb - 3.5, yn + 1.2, 19.5)), rc((xb + 2.5, yn + 1.2, 19.5))]
    deck = [rc((xs + dx, yn, 12.0)) for dx in (-24.0, -6.0, 12.0, 29.0)]
    idx, pal = _put(idx, pal, warm + deck, "#ffe0a0", R)
    idx, pal = _put(idx, pal, [rc((xb + 1.0, yn + 2.0, 28.5))], "#f4f6ff", R)
    idx, pal = _put(idx, pal, [rc((xb + 8.0, yn, 20.5))], "#7cf0a8", R)
    wr = np.flatnonzero(water[:, int(SHIP_COL)])
    if len(wr):
        r0 = int(wr.min()) + 1
        idx, pal = PER.light_streak(idx, pal, water, rc((xs - 6.0, yn, 0.0))[0], r0, r0 + 6, "#ffe0a0",
                                    half=(1.5, 3.0), dens=0.45, seed=13)
    print("HYB OCEAN ship lights", [(round(c, 1), round(r, 1)) for c, r in warm + deck])
    return idx, pal
