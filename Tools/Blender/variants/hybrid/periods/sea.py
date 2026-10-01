"""
periods/sea.py - the SEA (breakwater)'s four looks (Docs/time_currents_spec.md 3.4). Owner: the sea agent.

Compass: the angler faces WEST, out to the open sea (the late-afternoon sun stands high ahead-left). Dawn light comes
from BEHIND him, the evening sun sets over the open sea in the glitter column (dx -150, clear of the islands and the
far coast), the moon hangs over the same column at night. Night lights to add (spec 3.4): the mole lighthouse lamp
(left, col ~122), the red lateral buoy's lamp (right) - baked glow here, blink in Unity via LOOK lights.
"""
NATIVE = "day"              # hyb_sea.py today: bright late afternoon

PERIODS = {
    # ---------------------------------------------------------------- 새벽: sun behind him, pastel belt, blue-grey sea
    "dawn": dict(
        sky=[(0, "#a4aac4"), (4, "#c4acc0"), (9, "#e2b8bc"), (15, "#dcbccc"), (24, "#bcc2dc"), (36, "#9cb4dc"),
             (52, "#80a4d4"), (80, "#6890c8"), (110, "#5480bc")],
        sun=None, glow=None, aurora=None,
        # (the streak is a lit pink cloud, lighter than every sky band: a colour near a band-transition midpoint
        # made the quantizer dither that row into the lighthouse red)
        streaks=[dict(up=20, x0=260, x1=420, th=1, col="#ecc8cc", lit="#f8e0e0")],
        key_dir=(-0.25, -0.85, 0.46), key_col="#ffdcc8", shadow_col="#4a5c8c",
        rim=dict(col="#ffe4dc", strength=0.18, dirs=dict(t=1.0, tl=0.6)),
        haze=dict(col="#c0c0d8", near=200.0, dist=2000.0, max=0.78),
        mist=dict(col="#e4d8e0", amount=0.45, above=2.5, below=2.0, wisp=0.5),
        glitter=dict(cols=["#ecd4dc", "#f8ecf0"], far=0.3, dens=0.05, width=(10.0, 40.0), maxlen=3, dx=-150),
        water=dict(bands=["#c4b8cc", "#a4acc8", "#8298bc", "#6886ac", "#56769e", "#4a6a94"], dark="#3c5c86",
                   refl=["#2c3a52", "#3a4a62"], tint="#56769e", deep="#0e2440"),
        grade=dict(shadow=(-0.03, 0.01, 0.04), high=(0.03, 0.0, 0.01), sat=0.85, contrast=0.04, amount=1.0,
                   gain=0.82, tint="#f0e6f4"),
    ),
    "day": {},
    # ---------------------------------------------------------------- 저녁: the sun sets over the open sea (glitter
    # column), gold -> coral -> rose -> navy sky, long gold glitter, warm rims from the left
    "evening": dict(
        sky=[(0, "#f8c478"), (5, "#f2a868"), (11, "#e08c6c"), (18, "#c07a7c"), (26, "#94749a"), (36, "#6a70a4"),
             (52, "#4c6098"), (76, "#364e86"), (110, "#263c70")],
        sun=dict(dx=-150, up=9, r=5.0, core="#fff4d8", limb="#ffc878"),
        glow=dict(col="#ffb870", rings=[(7, 0.6), (13, 0.4), (20, 0.22), (30, 0.1)], soft=2.0, flat=0.4),
        streaks=[dict(up=16, x0=100, x1=300, th=2, col="#c47c7c", lit="#ffc88c")], aurora=None,
        key_dir=(-0.55, 0.3, 0.78), key_col="#ffcc90", shadow_col="#4a4a86",
        rim=dict(col="#ffc888", strength=0.5, dirs=dict(l=1.0, tl=0.85, t=0.55)),
        haze=dict(col="#e0a890", near=200.0, dist=2200.0, max=0.8),
        mist=dict(col="#ecc0a4", amount=0.4, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#ffd08c", "#fff4d8"], far=0.3, dens=0.5, width=(5.0, 28.0), maxlen=4, dx=-150),
        water=dict(bands=["#e0a888", "#b08c98", "#7c80a4", "#5a7aa6", "#3e6c9a", "#326090"], dark="#285482",
                   refl=["#20304a", "#2e405a"], tint="#3e6c9a", deep="#0c2440"),
        grade=dict(shadow=(-0.03, 0.012, 0.035), high=(0.045, 0.012, -0.04), sat=0.92, contrast=0.05, amount=1.0,
                   gain=0.95, tint="#fff0e0"),
    ),
    # ---------------------------------------------------------------- 밤: moon over the open sea, stars, dark sea
    # with a silver path, the lighthouse and the buoy lit
    "night": dict(
        sky=[(0, "#34405e"), (5, "#2c3856"), (12, "#24304e"), (22, "#1e2946"), (34, "#19233e"), (52, "#141d36"),
             (80, "#10182e"), (110, "#0c1226")],
        sun=dict(dx=-150, up=15, r=3.4, core="#f8f4e4", limb="#d6d4c6"),
        glow=dict(col="#8e9cc4", rings=[(7, 0.4), (16, 0.18), (30, 0.07)], soft=2.5, flat=0.8),
        streaks=[dict(up=21, x0=260, x1=420, th=1, col="#2a3656", lit="#62729c")], aurora=None,
        stars=dict(cols=["#7c88ac", "#dce2f4"], dens=0.006, up0=9, seed=7, bright=0.25, glow_max=0.1),
        key_dir=(-0.55, 0.2, 0.81), key_col="#c8d4f0", shadow_col="#161c38",
        rim=dict(col="#c4d4ff", strength=0.3, dirs=dict(l=1.0, tl=0.8, t=0.5)),
        haze=dict(col="#283250", near=200.0, dist=1800.0, max=0.85),
        mist=dict(col="#3e4a6a", amount=0.3, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#9aa8c8", "#e0e6f4"], far=0.3, dens=0.4, width=(5.0, 24.0), maxlen=3, dx=-150),
        # (the buoy's own tones stay lighter: L ~0.37 at gain 0.42 vs the play band 0.34)
        water=dict(bands=["#46506e", "#3e4a68", "#364462", "#303e5c", "#2a3856", "#263250"], dark="#202c4a",
                   refl=["#121828", "#1a2234"], tint="#2a3856", deep="#060c1c"),
        grade=dict(shadow=(-0.02, 0.0, 0.05), high=(0.0, 0.005, 0.03), sat=0.7, contrast=0.03, amount=1.0,
                   gain=0.42, tint="#bccaff"),
    ),
}

# hyb_sea.py constants a period changes (PER.const): FAR_RIM_K scales the far-silhouette rims (headland, coast +
# islands, mole + lighthouse). At night the hazed far layers are close to the sky colour: the islands / coast get no
# rim (it would leave only their outline), the headland and the mole a faint moon-side one.
CONSTS = {
    "night": {"FAR_RIM_K": (0.3, 0.0, 0.5)},
}

# ---------------------------------------------------------------- light positions (canvas px, x right, y down), from
# hyb_sea.py's geometry through the stage camera (standH 3.0) and checked on the renders; columns in the 640 layout
# + OX (the sea's canvas is 800 wide: 80 px of overscan each side, hyb_core.set_canvas)
import hyb_core as _R      # noqa: E402
OX = _R.OX
LH = (120.6 + OX, 72.4)     # lighthouse lantern glass (tower at x1 + 1.2 m, x1 = col 121 at D_MOLE 330 m, z 17.5-19.3)
LH_WATER = 101.8            # the mole's waterline row under the lighthouse
BUOY = (482.2 + OX, 144.8)  # the red buoy's lamp (17, 44, 2.08); its waterline row 164.5
HARBOUR = tuple(c + OX for c in (88, 104, 113, 131, 142))   # columns of the harbour lights along the far coast's foot

# Unity look extras (Data/Periods/sea_<p>.json); lights = animated overlays (spec 5.6) over the baked lamps
LOOK = {
    "dawn": dict(birds=True),
    "day": dict(birds=True),
    "evening": dict(birds=True),
    "night": dict(birds=False, lights=[dict(x=LH[0], y=LH[1], col="#ffe8a0", r=6, blink="flash2"),
                                       dict(x=BUOY[0], y=BUOY[1], col="#ff5a4a", r=3, blink="flash1")]),
}


def _core(idx, pal, R, mask, col):
    """Paint the masked pixels exactly `col` (a lamp: may be darker than its unlit glass, e.g. the red buoy lamp)."""
    return R.recolour(idx, pal, mask, lambda c: c * 0 + R.hexrgb(col)[None, :], snap=0.0)


def _disc(shape, c, r, rad):
    import numpy as np
    rows = np.arange(shape[0])[:, None] + 0.5
    cols = np.arange(shape[1])[None, :] + 0.5
    return np.hypot(cols - c, rows - r) < rad


def back_post(idx, pal, ctx):
    """Evening: the lighthouse lamp is lit (1 px glow). Night: the lighthouse lamp with its glow rings and broken
    streak on the water, the buoy lamp's red halo + reflection, harbour lights along the foot of the far coast."""
    import numpy as np
    import hyb_period as PER
    R, kid, water, p = ctx["R"], ctx["kid"], ctx["water"], ctx["period"]
    lh = kid == "lh"
    if p == "evening":
        idx, pal = _core(idx, pal, R, lh & _disc(idx.shape, *LH, 1.6), "#ffe890")
        idx, pal = PER.point_glow(idx, pal, *LH, "#ffe890", rings=((2.8, 0.35),), mask=~_disc(idx.shape, *LH, 1.6))
        return idx, pal
    if p != "night":
        return idx, pal
    core = lh & _disc(idx.shape, *LH, 1.6)
    idx, pal = PER.point_glow(idx, pal, *LH, "#ffe8a0", rings=((3.0, 0.55), (7.0, 0.24), (12.0, 0.09)), mask=~core)
    idx, pal = _core(idx, pal, R, core, "#fff0b0")
    idx, pal = PER.light_streak(idx, pal, water, LH[0] + 0.3, LH_WATER + 1, LH_WATER + 32, "#ffe8a0", half=(0.5, 1.6),
                                dens=0.62, seed=9)
    # the buoy lamp: a small red halo (the front buoy hides its middle) and a short broken reflection
    idx, pal = PER.point_glow(idx, pal, *BUOY, "#ff5a4a", rings=((2.0, 0.5), (5.0, 0.2)))
    idx, pal = PER.light_streak(idx, pal, water, BUOY[0] - 1.5, 169, 182, "#ff6a58", half=(0.5, 1.1), dens=0.75,
                                seed=4)
    # harbour lights: 1 px on the lowest pixel of the far coast in a few columns (above the mole, or at the coast's
    # end over the water, where a 1 px reflection dash sits under it)
    lights = np.zeros(idx.shape, bool)
    refl = np.zeros(idx.shape, bool)
    for c in HARBOUR:
        rows = np.flatnonzero(kid[:, c] == "coast")
        if not len(rows):
            continue
        r = int(rows.max())
        lights[r, c] = True
        below = np.flatnonzero(water[r + 1:r + 8, c])
        if len(below):
            rr = r + 1 + int(below[0]) + 1
            refl[rr, c - 1:c + 1] = water[rr, c - 1:c + 1]
    idx, pal = _core(idx, pal, R, lights, "#ffd890")
    idx, pal = R.blend_idx(idx, pal, refl, "#ffd890", 0.45, lighter=True, snap=0.03, space="srgb")
    return idx, pal


def front_post(idx, pal, ctx):
    """Night: the buoy's lamp glows red. Its pixels are the light ones around the lamp centre (the yellow glass,
    its rim-lit variant and the lit outline above it, all lighter than the dark night buoy): the lightest become a
    hot core, the rest the red lamp, the darkest (the glass outline) a deep red; then a warm-red ring on the buoy top."""
    if ctx["period"] != "night":
        return idx, pal
    import numpy as np
    import hyb_period as PER
    R, kid = ctx["R"], ctx["kid"]
    L = np.append(pal.lab[:, 0], 0.0)[idx]                 # idx -1 -> L 0
    lamp = (idx >= 0) & _disc(idx.shape, *BUOY, 2.4) & (L >= 0.7)
    for lo, hi, col in ((0.95, 9.0, "#ffa08c"), (0.85, 0.95, "#ff5a4a"), (0.0, 0.85, "#b83a34")):
        idx, pal = _core(idx, pal, R, lamp & (L >= lo) & (L < hi), col)
    buoy = (kid == "buoy") & (idx >= 0) & ~lamp
    return PER.point_glow(idx, pal, *BUOY, "#ff7a5a", rings=((3.4, 0.35),), mask=buoy)
