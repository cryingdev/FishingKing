"""
retro16 - preview composite of the lake as the game shows it (central 480x270 of the 640x400 stage,
scaled 2x): back layer -> 3 fish top-shadows (lerp 70 % towards waterDeep, 0.8 alpha) ->
front layer -> angler idle (feet on the projected feet point) -> 2 px rod line from the hand.
Output: _tmp/variants/retro16/preview_lake.png
Run: blender -b --python variants/retro16/r16_preview.py
"""
import os
import sys
import json
import math
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import r16_core as R  # noqa: E402
import numpy as np  # noqa: E402
import fk_persp as P  # noqa: E402

STAND = 1.0
W, H = P.W, P.H
CX0, CY0, CW, CH = 80, 65, 480, 270
LAYOUT = json.load(open(os.path.join(R.OUT, "lake.json"), encoding="utf-8"))


def to_px(p):
    x, y, d = P.project(p, STAND)
    return W / 2 + x, H / 2 - y, d


SHADOW_MIX, SHADOW_ALPHA = 0.7, 0.8     # shadow colour = lerp(sprite, waterDeep, 0.7), drawn at 0.8 alpha


def tint_sprite(spr):
    wd = R.hexrgb(LAYOUT["waterDeep"]).astype(np.float32)
    out = spr.copy()
    out[..., :3] = spr[..., :3] * (1 - SHADOW_MIX) + wd * SHADOW_MIX
    return out


def foreshorten(p):
    a = to_px(p)
    bx = to_px((p[0] + 0.1, p[1], p[2]))
    bz = to_px((p[0], p[1] + 0.1, p[2]))
    lx = math.hypot(bx[0] - a[0], bx[1] - a[1])
    lz = math.hypot(bz[0] - a[0], bz[1] - a[1])
    return min(1.0, lz / lx) if lx > 1e-4 else 1.0


def main():
    back = R.load_png(os.path.join(R.OUT, "lake_back.png"))
    front = R.load_png(os.path.join(R.OUT, "lake_front.png"))
    img = back.copy()
    # ---- fish shadows: (species, cm, unity point (x, -depth, z fwd), heading deg, frame)
    fishes = [("largemouth_bass", 46, (-3.0, -1.5, 12.0), 20, 0), ("carp", 62, (4.0, -1.0, 18.0), 150, 1),
              ("crucian_carp", 24, (3.2, -1.0, 12.5), 160, 0)]
    for fid, cm, (ux, uy, uz), head, fr in fishes:
        spr = R.load_png(os.path.join(R.OUT, "fish", f"{fid}_t{fr}.png"))
        p3 = (ux, uz, uy)                       # Blender axes: x right, y forward, z up
        c, r, d = to_px(p3)
        vis = min(6.5, max(0.9, 0.85 + cm / 100 * 1.5))
        s = max(0.35, (P.F_PX / d) * vis / max(8.0, spr.shape[1]))
        sq = min(0.9, max(0.4, foreshorten(p3) * 1.8 + 0.25))
        t = R.affine_nn(tint_sprite(spr), s, s * sq, head)
        R.over(img, t, int(round(c - t.shape[1] / 2)), int(round(r - t.shape[0] / 2)), alpha=SHADOW_ALPHA)
    R.over(img, front, 0, 0)
    # ---- angler (idle) with the feet pixel on the projected feet point
    data = json.load(open(os.path.join(R.OUT, "character", "angler.json"), encoding="utf-8"))
    pose = [q for q in data["poses"] if q["name"] == "idle"][0]
    spr = R.load_png(os.path.join(R.OUT, "character", "angler_idle.png"))
    fc, fr_, _ = to_px((0, 0, STAND))
    ox = int(round(fc)) - data["cropW"] // 2
    oy = int(round(fr_)) - (data["cropH"] - data["feetPx"])
    # ---- rod: from the hand along the pose rod direction (drawn under the near hand)
    hx = ox + pose["handX"]
    hy = oy + (data["cropH"] - pose["handY"])
    h3 = (pose["hx"], pose["hz"], pose["hy"] + STAND)
    rd = (pose["rx"], pose["rz"], pose["ry"])
    tip3 = tuple(h3[i] + rd[i] * 1.35 for i in range(3))
    tc, tr, _ = to_px(tip3)
    hc, hr, _ = to_px(h3)
    tc += hx - hc
    tr += hy - hr
    rod_dark = R.hexrgb("#2a1c1c")
    rod_hi = R.hexrgb("#8a6a4a")
    R.over(img, spr, ox, oy)
    R.line(img, hx, hy, tc, tr, rod_dark, 1)
    R.line(img, hx + 1, hy, tc + 1, tr, rod_hi, 1)
    R.line(img, hx, hy + 1, hx + (tc - hx) * 0.3, hy + 1 + (tr - hy) * 0.3, rod_dark, 1)
    crop = img[CY0:CY0 + CH, CX0:CX0 + CW]
    crop[..., 3] = 1.0
    R.save_png(R.upscale(crop, 2), os.path.join(R.OUT, "preview_lake.png"))
    print("R16 preview rod px", round(math.hypot(tc - hx, tr - hy), 1), "feet", fc, fr_)


if __name__ == "__main__":
    main()
