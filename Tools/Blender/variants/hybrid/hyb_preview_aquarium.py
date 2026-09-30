"""
hybrid - preview composite of the aquarium as the game draws it: aquarium_back -> a handful of hybrid fish
sprites (side view frames _0/_1, some flipped like TankFish.flipX) at world points inside the swim bounds of
aquarium.json (col = 320 + 16 x, row = 200 - 16 y, sprite pivot = centre) -> aquarium_front.
Full 640x400 canvas at 2x nearest -> _tmp/variants/hybrid/preview_aquarium.png (phones in landscape show
~600 px of the width, 16:9 shows the central 480x270: that crop is also written to work/aquarium/).
Prints per fish: sprite L, water L behind it, silhouette-edge L (the fish must read against the water).
Run: blender -b --python variants/hybrid/hyb_preview_aquarium.py
"""
import os
import sys
import json
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402

SID = "aquarium"
W, H, PPU = 640, 400, 16
CX0, CY0, CW, CH = R.CROP
WORKD = os.path.join(R.WORK, SID)
os.makedirs(WORKD, exist_ok=True)
LAYOUT = json.load(open(os.path.join(R.OUT, f"{SID}.json"), encoding="utf-8"))

# (species, world x, world y, flipX, frame): spread over the swim box, a bottom dweller low, a surface fish high
FISHES = [("golden_carp", -7.2, 3.3, False, 0), ("crystal_koi", 5.2, 3.7, True, 1), ("bluegill", -1.6, 1.2, False, 1),
          ("rainbow_trout", 8.4, 0.2, True, 0), ("piranha", -9.4, -0.9, False, 0), ("catfish", 1.8, -2.3, True, 0),
          ("mandarin_fish", 10.2, 2.4, False, 1)]


def luma(rgb):
    return float(R.oklab(np.clip(rgb, 0, 1))[..., 0].mean())


def main():
    back = R.load_png(os.path.join(R.OUT, f"{SID}_back.png"))
    front = R.load_png(os.path.join(R.OUT, f"{SID}_front.png"))
    img = back.copy()
    for fid, x, y, flip, fr in FISHES:
        spr = R.load_png(os.path.join(R.OUT, "fish", f"{fid}_{fr}.png"))
        if flip:
            spr = spr[:, ::-1].copy()
        h, w = spr.shape[:2]
        hw = w / 32.0
        inside = LAYOUT["swimMinX"] + hw <= x <= LAYOUT["swimMaxX"] - hw and LAYOUT["swimMinY"] <= y <= LAYOUT["swimMaxY"]
        c, r = W / 2 + x * PPU, H / 2 - y * PPU
        x0, y0 = int(round(c - w / 2)), int(round(r - h / 2))
        m = spr[..., 3] > 0.5
        edge = m & ~(np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1))
        under = back[y0:y0 + h, x0:x0 + w, :3][m]
        R.over(img, spr, x0, y0)
        print("HYB preview fish", fid, "at", x, y, "inside bounds", inside, "fish L", round(luma(spr[m][:, :3]), 3),
              "edge L", round(luma(spr[edge][:, :3]), 3), "water L", round(luma(under), 3))
    R.over(img, front, 0, 0)
    img[..., 3] = 1.0
    R.save_png(R.upscale(img, 2), os.path.join(R.OUT, f"preview_{SID}.png"))
    crop = img[CY0:CY0 + CH, CX0:CX0 + CW].copy()
    R.save_png(R.upscale(crop, 2), os.path.join(WORKD, f"preview_{SID}_169.png"))
    print("HYB preview aquarium colours", R.count_colours(img))


if __name__ == "__main__":
    main()
