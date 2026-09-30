"""
TEMPLATE encounter set - copy to encounter_sets/<set>.py (the set id = EncounterDef.backdrop) and replace everything
(see encounter_sets/README.md and Docs/legends_rollout.md 4 for your set's look). Files starting with "_" are skipped
by --all; this one still builds (`hyb_encounter.py -- _template`) as a smoke test of the generic runner: flat
placeholder layers incl. the optional surface CEILING, a frame, and one new lure (popper) with a frame-1 tweak.
"""
import math
import numpy as np
from mathutils import Matrix
import hyb_core as R
import hyb_encounter_kit as E

SET = "_template"
PRESET = "lake"
LURES = ["popper"]                           # lure billboards this set needs (fk_items bait_<key>)


def _tweak_popper(objs):
    """Frame 1: the popper nods nose-up (the 'pop')."""
    for o in objs:
        o.matrix_world = Matrix.Rotation(math.radians(-12), 4, "Y") @ o.matrix_world


LURE_TWEAKS = {"popper": _tweak_popper}      # only lures the kit does not already know (egi / softworm / jig)
HORIZON = 150                                # bg row of the eye-level horizon (haze band)
REVIEW = dict(ray=("#e8ffc8", 0.3, 470, 0))  # review sheet: ray tint / alpha / x / y (+ optional layer offsets)
WATER = ["#3a5a3a", "#2e4c32", "#243e2a", "#1a3022", "#12241a"]
PROFILE = dict(lure_at="floor", cam=(-1.2, 0.45, -2.7), line_up=(-0.9, 2.2, -1.6), clear="#12241a",
               ray=("#e8ffc8", 0.10, 150, -20), halo=None, snow_far="#4a6a4a", snow_near="#a8c8a0",
               line="#b8c8b0", silt="#9a9070", rim=("#d8f0a0", 0.3, [((0, 1), 1.0), ((-1, 1), 0.8), ((1, 1), 0.8)]),
               fore_x=-20, abyss="#0e1c14", fog_outline="#3a5a40", sun_mix=0.7)


def layers():
    stops = [(0, WATER[0]), (80, WATER[1]), (160, WATER[2]), (240, WATER[3]), (320, WATER[4])]
    idx, pal = E.quant_rows(stops, 400, 640)
    bg = R.to_rgba(idx, pal)
    bg[..., 3] = 1.0
    mid = E.blank(200, 768)
    xx = np.arange(768)[None, :]
    yy = np.arange(200)[:, None]
    E.put(mid, (np.abs(((xx % 96) - 48)) < 3) & (yy > 200 - 60 - (xx % 37)), "#1e3a24")      # weed stalks
    floor = E.blank(120, 768)
    dist = E.floor_rows(120, 0.45)
    E.put(floor, np.broadcast_to((dist < 12.0), (120, 768)) & (R.bayer(120, 768) < 0.8), "#2a3a26")
    ceiling = E.blank(120, 768)
    near = E.ceiling_rows(120, 3.0)
    E.put(ceiling, np.broadcast_to(near < 10.0, (120, 768)) & (R.bayer(120, 768) < 0.5), "#6a8a5a", 0.6)
    fore = E.blank(128, 256)
    E.put(fore, ((xx[:, :256] / 256.0) ** 2 + ((128 - np.arange(128)[:, None]) / 128.0) ** 2) < 0.4, "#0e1c12")
    return {"uw__template_bg": bg, "uw__template_mid": mid, "uw__template_floor": floor,
            "uw__template_ceiling": ceiling, "uw__template_fore": fore}


def frame():
    F = dict(ink="#0e140c", dark="#26301e", mid="#34422a", lit="#445636", line="#a8e878")
    leaf = ["#2e5a24", "#5a9a3a", "#a8e878"]
    return E.make_frame(F, (leaf, leaf, leaf, leaf))
