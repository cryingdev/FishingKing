"""
hybrid - REVIEW SHEET of a stage's four time-of-day looks (Docs/time_currents_spec.md 3.9 / 12): per period the game
crop (480x270 of the 640x400 stage) with the back layer -> 3 fish shadows drawn the Unity way with THAT period's
waterTint / waterDeep (StageView.UnderwaterTint + FishShadowTint, gamma-space blending) -> the front layer -> the angler
idle sprite multiplied by the period's actorTint (spec 5.2). Sheet: dawn | day over evening | night, 1x, 8 px gaps.
Prints per period and fish: the OKLab L of the water under the fish and of the shadow (the shadow must be >= 0.08
darker), plus the colour counts of the layers.

Sources per period p (first found): _tmp/variants/hybrid/periods/<stage>_<p>_back.png / _front.png / <stage>_<p>.json,
then Assets/Resources/Sprites/Stages/<stage>_<p>_*.png + Data/Periods/<stage>_<p>.json; a missing period is left grey.
Output: _tmp/variants/hybrid/periods/periods_<stage>.png
Run: blender -b --python variants/hybrid/hyb_period_preview.py -- <stage> [<stage> ...]
"""
import os
import sys
import json
import math
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

PERIODS = ("dawn", "day", "evening", "night")
W, H = P.W, P.H
CX0, CY0, CW, CH = R.CROP
SRC = [os.path.join(R.OUT, "periods")]
INST_SPR, INST_DATA = os.path.join(C.SPRITES, "Stages"), os.path.join(C.DATA, "Periods")
# (species, cm, (x, -depth, z forward), heading deg, frame): the stage previews' fish
FISH = {
    "lake": [("largemouth_bass", 46, (-3.2, -1.5, 12.0), 20, 0), ("carp", 62, (3.4, -1.0, 19.0), 150, 1),
             ("crucian_carp", 24, (4.2, -1.0, 11.0), 160, 0)],
    "stream": [("rainbow_trout", 48, (-2.6, -2.4, 12.0), 15, 0), ("cherry_salmon", 30, (2.9, -3.2, 21.0), 165, 1),
               ("lenok", 58, (0.8, -3.8, 33.0), 200, 0)],
    "sea": [("mackerel", 36, (-3.6, -1.5, 16.0), 20, 0), ("black_porgy", 38, (4.6, -2.0, 25.0), 150, 1),
            ("horse_mackerel", 24, (-0.8, -1.2, 36.0), 200, 0)],
    "swamp": [("snakehead", 60, (-3.0, -1.5, 12.5), 25, 0), ("catfish", 70, (3.4, -2.5, 20.0), 150, 1),
              ("piranha", 25, (3.6, -1.0, 10.0), 165, 0)],
    "ice": [("smelt", 13, (2.2, -0.8, 14.0), 30, 0), ("burbot", 58, (-5.0, -4.2, 18.0), 160, 1),
            ("northern_pike", 85, (5.4, -2.4, 15.5), 200, 0)],
    "ocean": [("yellowtail", 80, (-4.3, -1.5, 16.5), 25, 0), ("mahi_mahi", 110, (4.6, -1.2, 21.0), 160, 1),
              ("bluefin_tuna", 180, (-1.5, -2.5, 33.0), 190, 0)],
    "cave": [("crystal_koi", 60, (-4.0, -2.0, 16.0), 20, 0), ("anglerfish", 70, (3.6, -5.5, 19.0), 150, 1),
             ("cave_tetra", 10, (2.4, -1.5, 7.5), 160, 0)],
}


def find(name, inst):
    for d in SRC + [inst]:
        p = os.path.join(d, name)
        if os.path.isfile(p):
            return p
    return None


def lum709(c):
    return float(0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2])


def shadow_tint(depth, sprite_lum, tint, deep, ice):
    """StageView.FishShadowTint (baseAlpha 1): (rgb multiplier, alpha)."""
    k = min(1.0, max(0.0, depth / 7.0))
    c = (np.ones(3) * 0.65 + tint * 0.35) * (1 - k * 0.75) + deep * (k * 0.75)
    a = (0.95 + (0.6 - 0.95) * k) * (0.6 if ice else 1.0)
    water = lum709(tint)
    aa = max(0.05, a)
    m = 0.5 + (0.3 - 0.5) * min(1.0, max(0.0, (water - 0.25) / 0.2))
    seen = sprite_lum * lum709(c)
    dark, light = water * (1 - m / aa), water * (1 + m / aa)
    if dark < seen < light:
        c = c * (max(0.0, dark) / max(1e-4, seen))
    return c.astype(np.float32), a


def compose(stage, period, layout, stand):
    bp = find("%s_%s_back.png" % (stage, period), INST_SPR)
    fp = find("%s_%s_front.png" % (stage, period), INST_SPR)
    jp = find("%s_%s.json" % (stage, period), INST_DATA)
    if not (bp and fp and jp):
        print("HYB PREVIEW", stage, period, "missing", [n for n, p in (("back", bp), ("front", fp), ("json", jp)) if not p])
        g = np.zeros((CH, CW, 4), np.float32)
        g[..., :3] = 0.3
        g[..., 3] = 1
        return g
    look = json.load(open(jp, encoding="utf-8"))
    back, front = R.load_png(bp), R.load_png(fp)
    img = back.copy()
    tint, deep = R.hexrgb(look["waterTint"]), R.hexrgb(look["waterDeep"])
    ice = layout.get("mode") == "ice"
    for fid, cm, (ux, uy, uz), head, fr in FISH[stage]:
        spr = R.load_png(os.path.join(R.OUT, "fish", "%s_t%d.png" % (fid, fr)))
        op = spr[..., 3] > 0.5
        slum = float((spr[op][:, :3] @ np.array([0.2126, 0.7152, 0.0722])).mean()) if op.any() else 0.5
        p3 = (ux, uz, uy)
        x, y, d = P.project(p3, stand)
        c, r = W / 2 + x, H / 2 - y
        real = cm / 100.0
        vis = (real + min(6.5, max(0.9, 0.85 + real * 1.5))) * 0.5
        s = max(8.0, (P.F_PX / d) * vis) / max(8.0, spr.shape[1])
        a0 = P.project(p3, stand)
        bx = P.project((ux + 0.1, uz, uy), stand)
        bz = P.project((ux, uz + 0.1, uy), stand)
        lx, lz = math.hypot(bx[0] - a0[0], bx[1] - a0[1]), math.hypot(bz[0] - a0[0], bz[1] - a0[1])
        sq = min(0.8, max(0.3, (min(1.0, lz / lx) if lx > 1e-4 else 1.0) * 1.3 + 0.1))
        mul, al = shadow_tint(-uy, slum, tint, deep, ice)
        t = spr.copy()
        t[..., :3] *= mul[None, None, :]
        t = R.affine_nn(t, s, s * sq, head)
        x0, y0 = int(round(c - t.shape[1] / 2)), int(round(r - t.shape[0] / 2))
        m = t[..., 3] > 0.5
        under = back[y0:y0 + t.shape[0], x0:x0 + t.shape[1], :3][m]
        R.over(img, t, x0, y0, alpha=al)
        shaded = img[y0:y0 + t.shape[0], x0:x0 + t.shape[1], :3][m]
        wl, sl = float(R.oklab(np.clip(under, 0, 1))[..., 0].mean()), float(R.oklab(np.clip(shaded, 0, 1))[..., 0].mean())
        print("HYB PREVIEW", stage, period, "fish", fid, "water L", round(wl, 3), "shadow L", round(sl, 3),
              "OK" if wl - sl >= 0.08 else "TOO FAINT")
    R.over(img, front, 0, 0)
    # the angler idle sprite, multiplied by the period's actorTint (gamma space, like a SpriteRenderer colour)
    cj = os.path.join(R.OUT, "character", "character.json")
    cs = os.path.join(R.OUT, "character", "angler_idle.png")
    if os.path.isfile(cj) and os.path.isfile(cs):
        data = json.load(open(cj, encoding="utf-8"))
        spr = R.load_png(cs)
        spr[..., :3] *= R.hexrgb(look["actorTint"]).astype(np.float32)[None, None, :]
        fx, fy, _ = P.project((0, 0, stand), stand)
        fc, fr_ = W / 2 + fx, H / 2 - fy
        R.over(img, spr, int(round(fc)) - data["cropW"] // 2, int(round(fr_)) - (data["cropH"] - data["feetPx"]))
    print("HYB PREVIEW", stage, period, "colours back", R.count_colours(back), "front", R.count_colours(front),
          "waterTint", look["waterTint"], "L", round(R.lum(look["waterTint"]), 3), "actorTint", look["actorTint"])
    crop = img[CY0:CY0 + CH, CX0:CX0 + CW].copy()
    crop[..., 3] = 1.0
    return crop


def sheet(stage):
    global W, H, CX0, CY0, CW, CH
    layout = json.load(open(os.path.join(C.DATA, "stage_%s.json" % stage), encoding="utf-8"))
    # (the stage's canvas: 640 x 400, or wider with overscan; the sheet shows the game's home view, centred)
    R.set_canvas(int(layout.get("widthPx") or 640), int(layout.get("heightPx") or 400))
    W, H = P.W, P.H
    CX0, CY0, CW, CH = R.CROP
    stand = float(layout["standH"])
    pad = 8
    out = np.zeros((2 * CH + 3 * pad, 2 * CW + 3 * pad, 4), np.float32)
    out[..., :3] = 0.12
    out[..., 3] = 1
    for i, p in enumerate(PERIODS):
        r, c = divmod(i, 2)
        R.over(out, compose(stage, p, layout, stand), pad + c * (CW + pad), pad + r * (CH + pad))
    path = os.path.join(R.OUT, "periods", "periods_%s.png" % stage)
    R.save_png(out, path)
    print("HYB PREVIEW sheet", path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for st in [a for a in argv if not a.startswith("--")] or list(FISH):
        sheet(st)


if __name__ == "__main__":
    main()
