"""Inspection helper: nearest-neighbour zoom of a region of one or more PNGs, side by side.
blender -b --python cine_zoom.py -- out.png scale x0 y0 x1 y1 in1.png [in2.png ...]   (top-down coords, x1/y1 = -1 for full)
"""
import sys
import os
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cine_common as K  # noqa: E402

a = sys.argv[sys.argv.index("--") + 1:]
out, s, x0, y0, x1, y1 = a[0], int(a[1]), int(a[2]), int(a[3]), int(a[4]), int(a[5])
tiles = []
for p in a[6:]:
    im = K.load(p)
    xe = im.shape[1] if x1 < 0 else x1
    ye = im.shape[0] if y1 < 0 else y1
    im = im[y0:ye, x0:xe]
    bg = np.zeros(im.shape[:2] + (4,), np.float32)
    bg[..., :3] = 0.42
    bg[..., 3] = 1
    t = K.over(bg, im)
    tiles.append(K.upscale(t, s))
    tiles.append(np.ones((tiles[-1].shape[0], 6, 4), np.float32))
tiles = tiles[:-1]
hm = max(t.shape[0] for t in tiles)
padded = []
for t in tiles:
    p = np.zeros((hm, t.shape[1], 4), np.float32)
    p[..., :3] = 0.42
    p[..., 3] = 1
    p[:t.shape[0]] = t
    padded.append(p)
K.save(np.concatenate(padded, 1), out)
print("ZOOM ok")
