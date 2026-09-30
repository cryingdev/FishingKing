"""Debug: inspection sheet. args: out scale cols files..."""
import sys, os
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N
from nat_core import C
a = sys.argv[sys.argv.index("--") + 1:]
out, sc, cols = a[0], int(a[1]), int(a[2])
imgs = [C.load_pixels(os.path.join(N.OUT, f)) for f in a[3:]]
N.sheet(imgs, os.path.join(N.WORK, out), scale=sc, bg="#5f7078", pad=6, cols=cols)
