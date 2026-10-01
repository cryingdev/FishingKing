"""
hybrid - OVERSCAN splice: a stage rendered wider than the 640 px layout (Data/stage_<id>.json widthPx > 640, e.g. the
sea's 800: the same camera and focal length, more of the same view on both sides, for the game's camera to pan over
when a fish runs past its 480 px home frame; Assets/Scripts/Core/ViewZoom.cs) keeps its 640 px layout pixel for pixel.

The stage script builds the 640 layout's geometry first and exactly as before (the same vertices, random sequences and
object ids) and only then the extensions (their own random), so the 3D render agrees wherever the two overlap; a FRONT
layer (geometry only) comes out identical in the overlap by itself. A BACK layer's 2D post (water dash dithering,
reflections' breaks, mist) draws its random runs across the whole canvas width, so a wider canvas re-rolls them all.
build_overscan.ps1 therefore renders the stage twice (FK_CANVAS_W=640: the layout as it was; then the wide canvas) and
this script puts the layout's back layer into the centre of the wide one: the game's home view (and every column the
640 art had) stays exactly today's, the overscan columns come from the wide render (their random runs meet the
centre's at the seam, outside the home view).

Run: blender -b --python hyb_overscan.py -- <wide_back.png> <layout640_back.png> <out.png> [<out2.png> ...]
Prints "HYB OVERSCAN" lines: sizes, the seam columns, how many centre pixels the splice replaced, and the front check
when a pair of fronts is given with --front <wide_front.png> <layout640_front.png> (identical centre expected).
"""
import os
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    front = None
    if "--front" in a:
        i = a.index("--front")
        front = a[i + 1:i + 3]
        a = a[:i] + a[i + 3:]
    if len(a) < 3:
        raise SystemExit("HYB OVERSCAN ERROR usage: -- <wide_back.png> <layout640_back.png> <out.png> [...] [--front <wide> <640>]")
    return a[0], a[1], a[2:], front


def splice(wide, lay):
    """The layout's columns into the centre of the wide image; -> (image, ox, centre px that differed)."""
    H, W = wide.shape[:2]
    h, w = lay.shape[:2]
    if h != H or w > W or (W - w) % 2:
        raise SystemExit("HYB OVERSCAN ERROR sizes %s / %s" % (wide.shape, lay.shape))
    ox = (W - w) // 2
    out = wide.copy()
    centre = out[:, ox:ox + w]
    diff = int((np.abs(np.round(centre * 255) - np.round(lay * 255)).max(-1) > 0).sum())
    out[:, ox:ox + w] = lay
    return out, ox, diff


def main():
    wide_p, lay_p, outs, front = _args()
    wide, lay = R.load_png(wide_p), R.load_png(lay_p)
    img, ox, diff = splice(wide, lay)
    for o in outs:
        R.save_png(img, o)
    print("HYB OVERSCAN back %s: %dx%d, the 640 layout at columns %d..%d (seams), %d of its px re-rolled in the wide "
          "render replaced -> %s" % (os.path.basename(wide_p), img.shape[1], img.shape[0], ox, ox + lay.shape[1] - 1, diff,
                                     ", ".join(outs)))
    if front:
        fw, fl = R.load_png(front[0]), R.load_png(front[1])
        fox = (fw.shape[1] - fl.shape[1]) // 2
        c = fw[:, fox:fox + fl.shape[1]]
        # (outside the home view the overscan's own props may reach into the 640 layout's columns: count the home view)
        x0, y0, cw, ch = (fl.shape[1] - 480) // 2, (fl.shape[0] - 270) // 2, 480, 270
        d_home = int((np.abs(np.round(c[y0:y0 + ch, x0:x0 + cw] * 255) - np.round(fl[y0:y0 + ch, x0:x0 + cw] * 255)).max(-1) > 0).sum())
        d_all = int((np.abs(np.round(c * 255) - np.round(fl * 255)).max(-1) > 0).sum())
        print("HYB OVERSCAN CHECK front %s: the home view %s (%d px differ), the 640 layout's columns: %d px differ (the "
              "overscan's props reaching in)" % (os.path.basename(front[0]), "identical" if d_home == 0 else "DIFFERS", d_home, d_all))


if __name__ == "__main__":
    main()
