"""Inspection helper: blender -b --python hyb_zoom.py -- in.png out.png x0 y0 w h scale [bg r g b]
(x0, y0 top-left in pixels, top-down). Composites on a background colour and nearest-upscales."""
import os
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402

a = sys.argv[sys.argv.index("--") + 1:]
src, dst = a[0], a[1]
x0, y0, w, h, k = map(int, a[2:7])
bg = tuple(map(float, a[7:10])) if len(a) >= 10 else (0.5, 0.5, 0.5)
img = R.load_png(src)
if w <= 0:
    w = img.shape[1] - x0
if h <= 0:
    h = img.shape[0] - y0
crop = img[y0:y0 + h, x0:x0 + w]
out = np.zeros((h, w, 4), np.float32)
out[..., :3] = bg
out[..., 3] = 1
R.over(out, crop, 0, 0)
R.save_png(R.upscale(out, k), dst)
print("ZOOM ok", img.shape)
