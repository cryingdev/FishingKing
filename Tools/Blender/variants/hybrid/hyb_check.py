"""Sanity check of the hybrid deliverables against the current game data (read only):
fish sprite sizes (every species x 4 files; a species with no installed sprite yet is reported as new),
character.json fields + hand offsets, stage_lake.json keys / gameplay values (only waterTint / waterDeep /
clouds / birds may differ), layer sizes and colour counts.
Run: blender -b --python variants/hybrid/hyb_check.py"""
import os
import sys
import json
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import fk_common as C  # noqa: E402
import fk_fish as FF  # noqa: E402

bad = 0
n = 0
n_new = 0
for fid in FF.F:
    for fr in ("0", "1", "t0", "t1"):
        q = os.path.join(C.SPRITES, "Fish", f"{fid}_{fr}.png")
        p = os.path.join(R.OUT, "fish", f"{fid}_{fr}.png")
        if not os.path.exists(p):
            bad += 1
            print("CHECK missing", p)
            continue
        if not os.path.exists(q):            # a species modelled but not installed yet: nothing to compare with
            n_new += 1
            print("CHECK new", fid, fr)
            continue
        a = R.load_png(q)
        b = R.load_png(p)
        n += 1
        if a.shape != b.shape:
            bad += 1
            print("CHECK size", fid, fr, a.shape[:2], b.shape[:2])
print("CHECK fish files", n, "new", n_new, "size mismatches", bad)
old = json.load(open(os.path.join(C.DATA, "character.json")))
new = json.load(open(os.path.join(R.OUT, "character", "character.json")))
print("CHECK character keys same", sorted(old) == sorted(new), [k for k in old if k != "poses" and old[k] != new[k]])
for o, q in zip(old["poses"], new["poses"]):
    same = sorted(o) == sorted(q)
    print("CHECK pose", o["name"], q["name"], "fields ok" if same else "FIELDS DIFFER",
          "dHand px", round(q["handX"] - o["handX"], 1), round(q["handY"] - o["handY"], 1))
    im = R.load_png(os.path.join(R.OUT, "character", f"angler_{q['name']}.png"))
    if im.shape[:2] != (new["cropH"], new["cropW"]):
        print("CHECK pose size", q["name"], im.shape)
so = json.load(open(os.path.join(C.DATA, "stage_lake.json")))
sn = json.load(open(os.path.join(R.OUT, "stage_lake.json")))
print("CHECK stage keys/order same", list(so) == list(sn),
      "changed", {k: (so[k], sn[k]) for k in so if so[k] != sn.get(k)})
for f in ("lake_back.png", "lake_front.png", "preview_lake.png"):
    im = R.load_png(os.path.join(R.OUT, f))
    print("CHECK", f, im.shape[:2], "colours", R.count_colours(im))
