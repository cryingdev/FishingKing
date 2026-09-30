"""
hybrid - preview composite of the ICE stage as the game shows it (central 480x270 of the 640x400 stage,
scaled 2x -> 960x540): back layer -> 3 fish top-shadows under the ice -> front layer -> angler idle (feet on
the projected feet point) -> rod -> line + float at the gameplay hole.
Fish shadows follow StageView.UnderwaterTint exactly for mode "ice": sprite colour x
lerp(lerp(white, waterTint, 0.35), waterDeep, k * 0.75), alpha x lerp(0.95, 0.6, k) x 0.6 (seen through the
ice), k = depth / 7 (Unity project colour space: gamma -> sRGB blending, as R.over).
Prints the OKLab L of the ice vs. the shadow under each fish (they must be clearly darker) and where the
float lands relative to the painted hole.
Output: _tmp/variants/hybrid/preview_ice.png
Run: blender -b --python variants/hybrid/hyb_preview_ice.py
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

SID = "ice"
LAYOUT = json.load(open(os.path.join(R.OUT, f"stage_{SID}.json"), encoding="utf-8"))
STAND = float(LAYOUT["standH"])
ICE = LAYOUT.get("mode") == "ice"
W, H = P.W, P.H
CX0, CY0, CW, CH = R.CROP


def to_px(p):
    x, y, d = P.project(p, STAND)
    return W / 2 + x, H / 2 - y, d


def underwater_tint(depth):
    """StageView.UnderwaterTint(depth): (rgb, alpha)."""
    k = min(1.0, max(0.0, depth / 7.0))
    tint = R.hexrgb(LAYOUT["waterTint"])
    deep = R.hexrgb(LAYOUT["waterDeep"])
    c = np.ones(3) * (1 - 0.35) + tint * 0.35
    c = c * (1 - k * 0.75) + deep * (k * 0.75)
    a = (0.95 + (0.6 - 0.95) * k) * (0.6 if ICE else 1.0)
    return c.astype(np.float32), a


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
    # ---- fish shadows under the ice around the hole: (species, cm, unity point (x, -depth, z fwd), heading, frame);
    #      clear of the hole, the angler sprite and the props
    fishes = [("smelt", 13, (2.2, -0.8, 14.0), 30, 0), ("burbot", 58, (-5.0, -4.2, 18.0), 160, 1),
              ("northern_pike", 85, (5.4, -2.4, 15.5), 200, 0)]
    for fid, cm, (ux, uy, uz), head, fr in fishes:
        spr = R.load_png(os.path.join(R.OUT, "fish", f"{fid}_t{fr}.png"))
        p3 = (ux, uz, uy)                       # Blender axes: x right, y forward, z up
        c, r, d = to_px(p3)
        vis = min(6.5, max(0.9, 0.85 + cm / 100 * 1.5))
        s = max(0.35, (P.F_PX / d) * vis / max(8.0, spr.shape[1]))
        sq = min(0.9, max(0.4, foreshorten(p3) * 1.8 + 0.25))
        tc_, ta = underwater_tint(-uy)
        t = R.affine_nn(spr, s, s * sq, head)
        t[..., :3] *= tc_
        x0, y0 = int(round(c - t.shape[1] / 2)), int(round(r - t.shape[0] / 2))
        m = t[..., 3] > 0.5
        under = back[y0:y0 + t.shape[0], x0:x0 + t.shape[1], :3][m]
        R.over(img, t, x0, y0, alpha=ta)
        shaded = img[y0:y0 + t.shape[0], x0:x0 + t.shape[1], :3][m]
        print("HYB preview fish", fid, "at px", int(c), int(r), "depth", -uy, "alpha", round(ta, 2), "ice L",
              round(luma(under), 3), "shadow L", round(luma(shaded), 3))
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
    rod_hi = R.hexrgb("#9c8aa8")            # cool sky-side highlight (blue hour, light from the left)
    # ---- line from the rod tip into the hole + the float where Unity draws it (Surface + 4 px up)
    gx, gy, _ = to_px((LAYOUT["holeX"], LAYOUT["holeZ"], 0.0))
    fx, fy = int(math.floor(gx)), int(math.floor(gy)) - 4
    R.line(img, tc, tr, fx, fy - 2, R.hexrgb("#d8d4ec"), 1)
    for (dx, dy, col) in ((0, -2, "#e04a3a"), (-1, -1, "#e04a3a"), (0, -1, "#ff7a5a"), (1, -1, "#e04a3a"),
                          (-1, 0, "#f4f0f8"), (0, 0, "#ffffff"), (1, 0, "#f4f0f8"), (0, 1, "#c8c4dc")):
        img[fy + dy, fx + dx, :3] = R.hexrgb(col)
    R.over(img, spr, ox, oy)
    R.line(img, hx, hy, tc, tr, rod_dark, 1)
    R.line(img, hx - 1, hy, tc - 1, tr, rod_hi, 1)
    R.line(img, hx, hy + 1, hx + (tc - hx) * 0.3, hy + 1 + (tr - hy) * 0.3, rod_dark, 1)
    crop = img[CY0:CY0 + CH, CX0:CX0 + CW]
    crop[..., 3] = 1.0
    R.save_png(R.upscale(crop, 2), os.path.join(R.OUT, f"preview_{SID}.png"))
    hole_px = back[int(gy), int(gx), :3]
    print("HYB preview rod px", round(math.hypot(tc - hx, tr - hy), 1), "feet", round(fc, 1), round(fr_, 1),
          "hole point", round(gx, 2), round(gy, 2), "float px", fx, fy, "back colour there", R.tohex(hole_px),
          "L", round(luma(hole_px), 3), "waterTint", LAYOUT["waterTint"], "waterDeep", LAYOUT["waterDeep"])


if __name__ == "__main__":
    main()
