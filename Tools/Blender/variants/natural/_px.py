import sys, os
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N
from nat_core import C, np
a = sys.argv[sys.argv.index("--") + 1:]
img = C.load_pixels(a[0])[::-1]
for xy in a[1:]:
    x, y = map(int, xy.split(","))
    print("PX", x, y, N.srgb2hex(img[y, x, :3]), round(float(img[y, x, 3]), 2))
