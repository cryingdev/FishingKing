"""Sanity check: compare retro16 outputs with the current game assets (sizes, hand anchors, colours)."""
import os
import sys
import json
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import r16_core as R  # noqa: E402
import fk_common as C  # noqa: E402
import fk_fish as FF  # noqa: E402

bad = 0
for fid in FF.F:
    for fr in ("0", "1", "t0", "t1"):
        a = R.load_png(os.path.join(C.SPRITES, "Fish", f"{fid}_{fr}.png"))
        b = R.load_png(os.path.join(R.OUT, "fish", f"{fid}_{fr}.png"))
        if a.shape != b.shape:
            bad += 1
            print("CHECK size", fid, fr, a.shape, b.shape)
print("CHECK fish size mismatches", bad)
old = json.load(open(os.path.join(C.DATA, "character.json")))
new = json.load(open(os.path.join(R.OUT, "character", "angler.json")))
for o, n in zip(old["poses"], new["poses"]):
    print("CHECK pose", o["name"], "dHand px", round(n["handX"] - o["handX"], 1), round(n["handY"] - o["handY"], 1))
for f in ("lake_back.png", "lake_front.png"):
    im = R.load_png(os.path.join(R.OUT, f))
    print("CHECK", f, im.shape, "colours", R.count_colours(im))
