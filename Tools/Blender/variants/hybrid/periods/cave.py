"""
periods/cave.py - the CAVE's four looks (Docs/time_currents_spec.md 3.8). Owner: the cave agent.

No sky: the only daylight is the shaft from the ceiling hole (hyb_cave.py HOLE -> POOL, positions fixed by the stage
script; a period changes its colour / strength / fade only) and its lit pool + glitter on the far water. The rim
stays the cyan crystal light in every period; crystals and mushrooms are self-lit (hyb_cave.py grades them without
the period's gain / tint), so they keep today's colours while the rock follows the period:

  dawn     thin rose-pink shaft (weak low sun), rock a little darker and cooler (gain 0.9, lavender)
  day      today's white-green shaft (native, byte-identical)
  evening  golden-amber shaft, rock a little warmer (gain 0.94)
  night    a faint silver-blue moonbeam with sparkling dust, the rock darker (gain 0.68, blue); the crystals glow
           unchanged and the lantern carries the foreground: its pool on the ledge x1.3 (CONSTS LAMP_POOL) and its
           broken gold reflection streak on the water above the ledge edge (back_post). The play water keeps today's
           band (L 0.42).

The lantern (lit in every period) flickers in Unity: LOOK lights, canvas (250, 275) = the glass centre measured on
cave_front.png (hyb_cave.py: lantern at (-1.35, -0.95, standH + 1.26)).
"""
NATIVE = "day"              # hyb_cave.py today: crystal glow + a daylight shaft

PERIODS = {
    "dawn": dict(
        shaft=dict(col="#ffc2c0", amount=0.14, fade=0.42),
        glitter=dict(cols=["#f0bcc8", "#fff0f4"], dens=0.3),
        grade=dict(gain=0.9, tint="#eee6f8"),
    ),
    "day": {},
    "evening": dict(
        shaft=dict(col="#ffb870", amount=0.17, fade=0.36),
        glitter=dict(cols=["#f4be80", "#fff0d0"], dens=0.38),
        grade=dict(gain=0.94, tint="#fff0e2"),
    ),
    "night": dict(
        shaft=dict(col="#b0c4f0", amount=0.11, fade=0.45),
        glitter=dict(cols=["#90a8d0", "#d0e0f8"], dens=0.2),
        key_dir=(0.0, 0.3, 0.95), key_col="#a8e0f0",
        grade=dict(gain=0.68, tint="#d4deff"),
    ),
}

CONSTS = {
    "night": {"LAMP_POOL": (1.3, 1.15)},      # the lantern's pool on the ledge: x1.3 level, x1.15 radius
}

LANTERN = dict(x=250.0, y=275.0, col="#ffc860", r=4, blink="flicker")
LOOK = {
    "dawn": dict(birds=False, actorTint="#f4f0f4", glintCol="#f0e4ec", glintDensity=0.85, fxAlpha=0.95,
                 lights=[LANTERN]),
    "day": dict(birds=False, lights=[LANTERN]),
    "evening": dict(birds=False, actorTint="#fff4ec", glintCol="#f8ead4", glintDensity=0.85, fxAlpha=0.95,
                    lights=[LANTERN]),
    "night": dict(birds=False, actorTint="#dce4f0", glintCol="#c8e8f0", glintDensity=0.6, fxAlpha=0.85,
                  lights=[LANTERN]),
}

# the lantern's light on the water (night): its broken reflection streak on the water between the ledge edge and
# the far water straight below the lantern (the mirror image itself is hidden behind the ledge), narrow and sparse
# far away, wider and denser near the ledge; two levels (halo, core) of a lighter mix towards the flame colour
STREAK = dict(col=250.0, row_far=284, row_near=326, half=(1.0, 5.0), dens=(0.3, 0.9), col_hex="#ffc860",
              levels=(0.22, 0.45), seed=19)


def back_post(idx, pal, ctx):
    if ctx["period"] != "night":
        return idx, pal
    import random
    import numpy as np
    R, water = ctx["R"], ctx["water"]
    s = STREAK
    rng = random.Random(s["seed"])
    halo = np.zeros(idx.shape, bool)
    core = np.zeros(idx.shape, bool)
    r0, r1 = s["row_far"], s["row_near"]
    for r in range(r0, r1):
        t = (r - r0) / float(r1 - r0)              # 0 far -> 1 near the ledge
        if t < 0.5 and r % 2:
            continue                                # broken far part: every other row
        if rng.random() > s["dens"][0] + (s["dens"][1] - s["dens"][0]) * t:
            continue
        hw = s["half"][0] + (s["half"][1] - s["half"][0]) * t * rng.uniform(0.7, 1.15)
        c = s["col"] + rng.uniform(-1.0, 1.0) * (0.5 + 1.5 * t)
        a, b = int(round(c - hw)), int(round(c + hw)) + 1
        halo[r, max(0, a):max(0, b)] = True
        ch = max(0.5, hw * 0.45)
        core[r, max(0, int(round(c - ch))):max(0, int(round(c + ch)) + 1)] = True
    halo &= water & ~core
    core &= water
    idx, pal = R.blend_idx(idx, pal, halo, s["col_hex"], s["levels"][0], lighter=True, snap=0.03, space="srgb")
    idx, pal = R.blend_idx(idx, pal, core, s["col_hex"], s["levels"][1], lighter=True, snap=0.03, space="srgb")
    print("HYB CAVE night lantern streak: col", s["col"], "rows", r0, r1, "px halo", int(halo.sum()), "core",
          int(core.sum()))
    return idx, pal
