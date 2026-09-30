"""
hybrid - preset swatch sheet: one synthetic "mood plate" per time-of-day preset, built ONLY from the kit
API (no Blender geometry), so every preset is exercised and the parallel stage agents can see the target
mood before modelling anything. Each plate is the game crop (480x270) of a 640x400 canvas:
banded sky + sun / glow / streaks / aurora -> three hazed silhouette layers (2400 m, 700 m, 200 m, valley
under the sun) -> water bands (dash dithered) -> mirrored sky in the far water -> glitter -> mist ->
(cave) light shaft; a strip of the preset's key / shadow / rim / haze / mist / water colours underneath.
Plate order (left -> right, top -> bottom): lake, stream, sea, swamp, ice, ocean, cave.

Output: _tmp/variants/hybrid/presets.png
Run: blender -b --python variants/hybrid/hyb_presets.py
"""
import os
import sys
import math
import random
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402

W, H = R.W, R.H
STAND = 1.0
LAYERS = [(2400.0, "#4c4866", 12.0, 0.021), (700.0, "#34505a", 9.0, 0.043), (200.0, "#1b3331", 13.0, 0.09)]
ZONES = [(107, 111), (113, 119), (129, 138), (165, 178), (221, 240)]


def plate(name):
    pr = R.use_preset(name)
    rng = random.Random(3)
    sky, spal = R.sky_rgb(pr)
    msky, mpal = R.sky_rgb(pr, mirror=True)
    sc, _ = R.sun_rc(pr)
    cols = np.arange(W)
    rows = np.arange(H)[:, None]
    lay_cols = []
    land = np.zeros((H, W), bool)
    lab = np.zeros((H, W), int)                          # 1..3 layer id
    for k, (dist, base, amp, fr) in enumerate(LAYERS):
        wr = R.water_row(dist, STAND)
        valley = np.clip((np.abs(cols - sc) - 40) / 50.0, 0, 1) if k < 2 else np.ones(W)
        hgt = amp * (0.55 + 0.3 * np.sin(cols * fr + k) + 0.15 * np.sin(cols * fr * 3.1 + 2 * k)) * valley
        if k == 2:                                          # near shore: open bay under the sun
            hgt = np.where(np.abs(cols - sc) < 60, 0.0, hgt + 3 * (np.sin(cols * 0.7) > 0.3))
        m = (rows >= wr - hgt[None, :]) & (rows < wr) & (hgt[None, :] > 0.5)
        c = R.hazed(R.grade_hex([base]), dist)[0]
        lay_cols.append(c)
        lab[m] = k + 1
        land |= m
    shore = R.water_row(LAYERS[2][0], STAND)
    water = (rows >= R.HORIZON_ROW) & ~land
    wb = pr.water["bands"]
    glit = pr.glitter["cols"] if pr.glitter else []
    pal = R.Pal(spal + mpal + lay_cols + wb + [pr.water["dark"]] + glit)
    idx, _ = R.quantize(sky, np.ones((H, W), bool), pal, dither=True)
    for k, c in enumerate(lay_cols):
        idx[lab == k + 1] = pal.index(c)
    wband = R.water_bands(pal, wb, ZONES, random.Random(5))
    idx[water] = wband[water]
    # far water mirrors the sky exactly (dash dithered), handing over to the bands below the near shore
    zone = water & (rows < shore + 5)
    thr = R.dash_threshold(H, W, rng, 2, 7)
    m_idx, _ = R.quantize(msky, zone, pal, dither=True, thr=thr)
    fade = np.clip((rows - (shore - 1)) / 6.0, 0, 1)
    use = zone & (R.dash_threshold(H, W, rng, 3, 9) >= fade)
    idx[use] = m_idx[use]
    idx, pal = R.recolour(idx, pal, use, lambda c: R.l2s(R.s2l(c) * 0.85), snap=0.02)
    if pr.glitter:
        g_am, g_br = R.glitter_masks(pr, water, random.Random(17))
        idx[g_am] = pal.index(glit[0])
        idx[g_br] = pal.index(glit[1])
    line = np.array([np.flatnonzero(water[:, c]).min() if water[:, c].any() else H for c in range(W)], float)
    ma = R.mist_amount(pr, line, land, water)
    idx, pal = R.blend_idx(idx, pal, (ma > R.dash_threshold(H, W, rng, 2, 9)) & (ma > 0.08), pr.mist["col"], 0.5)
    idx, pal = R.apply_shaft(idx, pal, pr)
    x0, y0, cw, ch = R.CROP
    img = R.to_rgba(idx, pal)[y0:y0 + ch, x0:x0 + cw]
    # swatch strip: key, shadow, rim, haze, mist, water bands, tint, deep
    sw = [pr.key_col, pr.shadow_col, pr.rim["col"], pr.haze["col"], pr.mist["col"]] + wb + [pr.water["tint"],
                                                                                           pr.water["deep"]]
    strip = np.zeros((14, cw, 4), np.float32)
    strip[..., 3] = 1
    wsw = cw // len(sw)
    for i, c in enumerate(sw):
        strip[:, i * wsw:(i + 1) * wsw, :3] = R.hexrgb(c)
    print("HYB preset", name, "palette", len(pal))
    return np.concatenate([img, strip], 0)


def main():
    plates = [plate(n) for n in R.PRESETS]
    ph, pw = plates[0].shape[:2]
    pad = 8
    cols = 2
    rows = (len(plates) + cols - 1) // cols
    out = np.zeros((rows * (ph + pad) + pad, cols * (pw + pad) + pad, 4), np.float32)
    out[..., :3] = 0.16
    out[..., 3] = 1
    for i, p in enumerate(plates):
        r, c = divmod(i, cols)
        R.over(out, p, pad + c * (pw + pad), pad + r * (ph + pad))
    R.save_png(out, os.path.join(R.OUT, "presets.png"))
    print("HYB PRESETS done")


if __name__ == "__main__":
    main()
