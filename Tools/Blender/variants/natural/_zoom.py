"""Debug helper: zoom a region of an image. args: src out x0 y0 w h scale (top-down coords)"""
import sys, os
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N
from nat_core import C, np
a = sys.argv[sys.argv.index("--") + 1:]
src, out = a[0], a[1]
x0, y0, w, h, s = map(int, a[2:7])
img = C.load_pixels(src)[::-1]
crop = img[y0:y0 + h, x0:x0 + w].copy()
bg = np.array([0.42, 0.42, 0.42])
al = crop[..., 3:4]
crop[..., :3] = crop[..., :3] * al + bg * (1 - al)
crop[..., 3] = 1
C.save_pixels(N.upscale(crop, s)[::-1].copy(), out)
