"""Review helper: blender -b --python r16_review.py -- out.png scale cols file1.png file2.png ..."""
import os
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import r16_core as R  # noqa: E402

a = sys.argv[sys.argv.index("--") + 1:]
out, k, cols = a[0], int(a[1]), int(a[2])
imgs = [R.load_png(p) for p in a[3:]]
R.save_png(R.sheet(imgs, (0.23, 0.31, 0.38), scale=k, pad=4, cols=cols), out)
print("REVIEW ok")
