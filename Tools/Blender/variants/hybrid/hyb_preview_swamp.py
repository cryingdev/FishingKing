"""
hybrid - preview composite of the swamp as the game shows it (central 480x270 of the 640x400 stage,
scaled 2x -> 960x540): back layer -> 3 fish top-shadows (lerp 70 % towards waterDeep, 0.8 alpha, like
StageView's underwater tint) -> front layer -> angler idle (feet on the projected feet point) -> rod line.
Copy of hyb_preview.py with the swamp's stage id, standH (0.9) and swamp fish / points in open water.
Also prints the luminance of shadow vs. water under each fish (they must be clearly darker), plus the shadow
the game itself draws (StageView.UnderwaterTint: lerp(lerp(white, waterTint, .35), waterDeep, k * .75) x the
sprite, alpha lerp(.95, .6, k), k = depth / 7) at a shallow and a deep fish depth.
Output: _tmp/variants/hybrid/preview_swamp.png
Run: blender -b --python variants/hybrid/hyb_preview_swamp.py
"""
import os
import sys
import json
import math
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402
import fk_persp as P  # noqa: E402

SID = "swamp"
LAYOUT = json.load(open(os.path.join(R.OUT, f"stage_{SID}.json"), encoding="utf-8"))
STAND = LAYOUT["standH"]                 # 0.9
W, H = P.W, P.H
CX0, CY0, CW, CH = R.CROP
SHADOW_MIX, SHADOW_ALPHA = 0.7, 0.8     # shadow colour = lerp(sprite, waterDeep, 0.7), drawn at 0.8 alpha


def to_px(p):
    x, y, d = P.project(p, STAND)
    return W / 2 + x, H / 2 - y, d


def tint_sprite(spr):
    wd = R.hexrgb(LAYOUT["waterDeep"]).astype(np.float32)
    out = spr.copy()
    out[..., :3] = spr[..., :3] * (1 - SHADOW_MIX) + wd * SHADOW_MIX
    return out


def unity_tint(depth):
    """StageView.UnderwaterTint (colour multiplied onto the sprite, alpha) at a fish depth in metres."""
    k = min(1.0, max(0.0, depth / 7.0))
    wt = R.hexrgb(LAYOUT["waterTint"])
    wd = R.hexrgb(LAYOUT["waterDeep"])
    c = np.ones(3) * 0.65 + wt * 0.35
    c = c * (1 - k * 0.75) + wd * k * 0.75
    return c.astype(np.float32), 0.95 + (0.6 - 0.95) * k


def foreshorten(p):
    a = to_px(p)
    bx = to_px((p[0] + 0.1, p[1], p[2]))
    bz = to_px((p[0], p[1] + 0.1, p[2]))
    lx = math.hypot(bx[0] - a[0], bx[1] - a[1])
    lz = math.hypot(bz[0] - a[0], bz[1] - a[1])
    return min(1.0, lz / lx) if lx > 1e-4 else 1.0


def luma(rgb):
    return float(R.oklab(np.clip(rgb, 0, 1))[..., 0].mean())


def main():
    back = R.load_png(os.path.join(R.OUT, f"{SID}_back.png"))
    front = R.load_png(os.path.join(R.OUT, f"{SID}_front.png"))
    img = back.copy()
    # ---- fish shadows (swamp species): (species, cm, unity point (x, -depth, z fwd), heading deg, frame);
    #      all in the open middle water, clear of the boardwalk, the angler sprite, pads, duckweed and trunks
    fishes = [("snakehead", 60, (-3.0, -1.5, 12.5), 25, 0), ("catfish", 70, (3.4, -2.5, 20.0), 150, 1),
              ("piranha", 25, (3.6, -1.0, 10.0), 165, 0)]
    for fid, cm, (ux, uy, uz), head, fr in fishes:
        spr = R.load_png(os.path.join(R.OUT, "fish", f"{fid}_t{fr}.png"))
        p3 = (ux, uz, uy)                       # Blender axes: x right, y forward, z up
        c, r, d = to_px(p3)
        vis = min(6.5, max(0.9, 0.85 + cm / 100 * 1.5))
        s = max(0.35, (P.F_PX / d) * vis / max(8.0, spr.shape[1]))
        sq = min(0.9, max(0.4, foreshorten(p3) * 1.8 + 0.25))
        t = R.affine_nn(tint_sprite(spr), s, s * sq, head)
        x0, y0 = int(round(c - t.shape[1] / 2)), int(round(r - t.shape[0] / 2))
        m = t[..., 3] > 0.5
        under = back[y0:y0 + t.shape[0], x0:x0 + t.shape[1], :3][m]
        R.over(img, t, x0, y0, alpha=SHADOW_ALPHA)
        shaded = img[y0:y0 + t.shape[0], x0:x0 + t.shape[1], :3][m]
        # the game's own tint at this fish's depth (shallow) and at 5 m (deep)
        raw = R.affine_nn(spr, s, s * sq, head)[..., :3][m]
        ul = []
        for dep in (-uy, 5.0):
            tc, ta = unity_tint(dep)
            ul.append(round(luma(under * (1 - ta) + raw * tc * ta), 3))
        print("HYB preview fish", fid, "at px", int(c), int(r), "water L", round(luma(under), 3),
              "shadow L", round(luma(shaded), 3), "unity-tint shadow L (%.1f m / 5 m)" % -uy, ul[0], ul[1])
    R.over(img, front, 0, 0)
    # ---- angler (idle) with the feet pixel on the projected feet point
    data = json.load(open(os.path.join(R.OUT, "character", "character.json"), encoding="utf-8"))
    pose = [q for q in data["poses"] if q["name"] == "idle"][0]
    spr = R.load_png(os.path.join(R.OUT, "character", "angler_idle.png"))
    fc, fr_, _ = to_px((0, 0, STAND))
    ox = int(round(fc)) - data["cropW"] // 2
    oy = int(round(fr_)) - (data["cropH"] - data["feetPx"])
    # ---- rod: from the hand along the pose rod direction
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
    rod_hi = R.hexrgb("#a0845e")            # warm lantern-side highlight (the lantern hangs on the right)
    R.over(img, spr, ox, oy)
    R.line(img, hx, hy, tc, tr, rod_dark, 1)
    R.line(img, hx + 1, hy, tc + 1, tr, rod_hi, 1)
    R.line(img, hx, hy + 1, hx + (tc - hx) * 0.3, hy + 1 + (tr - hy) * 0.3, rod_dark, 1)
    crop = img[CY0:CY0 + CH, CX0:CX0 + CW]
    crop[..., 3] = 1.0
    R.save_png(R.upscale(crop, 2), os.path.join(R.OUT, f"preview_{SID}.png"))
    print("HYB preview rod px", round(math.hypot(tc - hx, tr - hy), 1), "feet", round(fc, 1), round(fr_, 1),
          "waterTint", LAYOUT["waterTint"], "L", round(R.lum(LAYOUT["waterTint"]), 3),
          "waterDeep", LAYOUT["waterDeep"], "L", round(R.lum(LAYOUT["waterDeep"]), 3))


if __name__ == "__main__":
    main()
