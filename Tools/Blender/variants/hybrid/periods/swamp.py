"""
periods/swamp.py - the SWAMP's four looks (Docs/time_currents_spec.md 3.5). Owner: the swamp agent.

Compass: the angler faces WEST (the dusk glow sits behind the fog ahead, dx -40). The swamp is always foggy; the
periods change what lights the fog: a cold silver dawn from behind him, a bright olive day (the fog thins: haze 260 m),
the amber dusk of today, and at night a moon glow behind the fog (right) with the boardwalk lantern as the warm
accent (brighter at night: hook / hyb_swamp.py lantern_light). hyb_swamp.py needs PR.glow in every period (glow_rims,
fog_glow): keep a glow dict. Geometry (the valley under the glow) stays on the NATIVE glow (GLOW_C).
"""
NATIVE = "evening"          # hyb_swamp.py today: foggy dusk

PERIODS = {
    # ---------------------------------------------------------------- 새벽: cold pale fog lit from behind, grey-green
    "dawn": dict(
        sky=[(0, "#b8bcb8"), (10, "#a8aeac"), (22, "#949c9c"), (38, "#7e8888"), (60, "#6a7676"), (100, "#586464")],
        glow=dict(col="#dce0e4", rings=[(30, 0.18), (60, 0.08)], soft=8.0, flat=0.35, dx=-40, up=6),
        key_dir=(-0.2, -0.6, 0.77), key_col="#e4e4dc", shadow_col="#384440",
        rim=dict(col="#e0e4dc", strength=0.12, dirs=dict(t=1.0)),
        haze=dict(col="#a4aaa4", near=20.0, dist=110.0, max=0.94),
        mist=dict(col="#c4c8c4", amount=1.0, above=10.0, below=7.0, wisp=0.5),
        water=dict(bands=["#9ca09a", "#80867e", "#686f66", "#566052", "#485444", "#404c3c"], dark="#384430",
                   refl=["#2a3226", "#343c2e"], tint="#485444", deep="#141c14"),
        grade=dict(shadow=(-0.01, 0.015, 0.01), high=(0.02, 0.02, 0.0), sat=0.7, contrast=0.03, amount=1.0,
                   gain=0.9, tint="#e8eef0"),
    ),
    # ---------------------------------------------------------------- 낮: bright diffuse olive day, thinner fog,
    # the sun a pale glow high on the left
    "day": dict(
        sky=[(0, "#c8c8a4"), (10, "#b8c0a0"), (22, "#a2b09a"), (38, "#8ca092"), (60, "#789088"), (100, "#66807c")],
        glow=dict(col="#f0e8c0", rings=[(26, 0.25), (54, 0.12)], soft=6.0, flat=0.5, dx=-120, up=24),
        key_dir=(-0.3, 0.3, 0.9), key_col="#f0e8c8", shadow_col="#34443a",
        rim=dict(col="#e8e0b8", strength=0.15, dirs=dict(t=1.0, tl=0.6)),
        haze=dict(col="#a8ac88", near=20.0, dist=260.0, max=0.9),
        mist=dict(col="#c0c4a4", amount=0.7, above=6.0, below=4.0, wisp=0.55),
        water=dict(bands=["#9c9a78", "#80826a", "#686e58", "#56604a", "#4a563e", "#424e38"], dark="#3a4632",
                   refl=["#2a3222", "#343e2a"], tint="#4a563e", deep="#141e12"),
        grade=dict(shadow=(-0.01, 0.02, 0.0), high=(0.03, 0.02, -0.02), sat=0.85, contrast=0.04, amount=1.0,
                   gain=1.02),
    ),
    "evening": {},
    # ---------------------------------------------------------------- 밤: moon glow behind the fog (right), near-black
    # green murk, the lantern and the fireflies are the lights
    "night": dict(
        sky=[(0, "#2a3430"), (10, "#242e2c"), (22, "#1e2828"), (38, "#182222"), (60, "#131c1c"), (100, "#0e1616")],
        # (round rings with small steps: the draft's wide flat outer ring drew a hard vertical edge
        # through the dark fog)
        glow=dict(col="#a8b4c8", rings=[(6, 0.26), (13, 0.15), (22, 0.07)], soft=12.0, flat=1.0, dx=60, up=16),
        key_dir=(0.25, 0.3, 0.92), key_col="#b4c0d0", shadow_col="#101818",
        rim=dict(col="#b8c4d4", strength=0.18, dirs=dict(t=1.0, tr=0.6)),
        haze=dict(col="#2a3430", near=20.0, dist=120.0, max=0.93),
        mist=dict(col="#3c4842", amount=0.9, above=8.0, below=6.0, wisp=0.55),
        water=dict(bands=["#3e4844", "#38423e", "#323c38", "#2e3834", "#2a3430", "#26302c"], dark="#222c28",
                   refl=["#161e1a", "#1c2420"], tint="#2a3430", deep="#060a08"),
        grade=dict(shadow=(-0.02, 0.0, 0.04), high=(0.0, 0.01, 0.02), sat=0.7, contrast=0.03, amount=1.0,
                   gain=0.42, tint="#c0d0e0"),
    ),
}

# hyb_swamp.py constants a period changes (PER.const):
#   LAMP = [glass, flame] of the boardwalk lantern (unlit by day: dull glass, no flame)
#   LANTERN_LIGHT = (level, radius px) of its light on the post / planks (0 = unlit; night: brighter, area x2)
#   FOG_GLOW_K / GLOW_RIM_K scale the glow's pull on the fog rows and the glow-side rim of the far shore: at night
#   the pale moon glow on the near-black fog made ghost outlines of every tree inside it
UNLIT = {"LAMP": ["#8a7a58", "#766a4e"], "LANTERN_LIGHT": (0.0, 0.0)}
CONSTS = {
    "dawn": dict(UNLIT),
    "day": dict(UNLIT),
    "night": {"LAMP": ["#ffd070", "#fff4c8"], "LANTERN_LIGHT": (0.4, 46.0), "FOG_GLOW_K": 0.3, "GLOW_RIM_K": 0.0},
}

# the lantern glass centre (canvas px, x right, y down): LANTERN (1.68, 0.78, standH + 1.02) through the stage camera
LANTERN = (394.0, 257.4)

# Unity look extras (Data/Periods/swamp_<p>.json)
LOOK = {
    "dawn": dict(birds=False, fireflies=False),
    "day": dict(birds=False, fireflies=False),
    "evening": dict(birds=False, fireflies=True),
    "night": dict(birds=False, fireflies=True,
                  lights=[dict(x=LANTERN[0], y=LANTERN[1], col="#ffc870", r=5, blink="flicker")]),
}


def front_post(idx, pal, ctx):
    """Night: warm glow rings of the lantern on the post, the arm and the planks near it (the glass stays)."""
    if ctx["period"] != "night":
        return idx, pal
    import numpy as np
    import hyb_period as PER
    kid = ctx["kid"]
    wood = np.isin(kid, ["post", "plank", "pier"]) & (idx >= 0)
    return PER.point_glow(idx, pal, *LANTERN, "#ffc870", rings=((3.0, 0.6), (6.0, 0.35), (11.0, 0.15)), mask=wood)


def back_post(idx, pal, ctx):
    """Night: the lantern's halo in the damp air (on the water behind it; the front hides the lantern itself) and
    its broken light streak on the water below it."""
    if ctx["period"] != "night":
        return idx, pal
    import hyb_period as PER
    idx, pal = PER.point_glow(idx, pal, *LANTERN, "#ffc870", rings=((5.0, 0.3), (9.0, 0.15), (14.0, 0.06)))
    return PER.light_streak(idx, pal, ctx["water"], LANTERN[0] - 0.5, LANTERN[1] + 6, LANTERN[1] + 24, "#ffc870",
                            half=(1.0, 2.0), dens=0.7, seed=6)
