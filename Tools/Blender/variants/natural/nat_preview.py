"""
FishingKing - "natural" variant: in-game style composite of the lake.

preview_lake.png (960x540) = central 480x270 of the 640x400 stage, 2x nearest:
  back layer -> 3 fish top-shadows (tinted like StageView.UnderwaterTint) -> front layer
  -> angler idle (feet on the projected feet point) -> rod line from the hand anchor.

Run: blender -b --python Tools/Blender/variants/natural/nat_preview.py
"""
import sys
import os
import math
import json

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nat_core as N  # noqa: E402
from nat_core import C, P, bpy, np  # noqa: E402

STAND = 1.0
W, H = P.W, P.H
CW, CH = 480, 270


def load_td(path):
    """Load PNG as top-down float array."""
    return C.load_pixels(path)[::-1].copy()


def paste_td(dst, src, x0, y0, tint=None, alpha=1.0):
    """Composite src (top-down) onto dst (top-down) with its top-left at (x0, y0)."""
    h, w = src.shape[:2]
    for yy in range(h):
        ty = y0 + yy
        if ty < 0 or ty >= dst.shape[0]:
            continue
        for xx in range(w):
            tx = x0 + xx
            if tx < 0 or tx >= dst.shape[1]:
                continue
            a = src[yy, xx, 3] * alpha
            if a <= 0:
                continue
            c = src[yy, xx, :3] * (tint if tint is not None else 1.0)
            dst[ty, tx, :3] = dst[ty, tx, :3] * (1 - a) + c * a


def transform_sprite(src, scale, squash, angle):
    """Nearest-neighbour sprite transform exactly like FishAgent: the sprite is rotated by the heading
    (child), then the parent scales it by (scale, scale * squash) in screen space. Point filtering."""
    h, w = src.shape[:2]
    ca, sa = math.cos(angle), math.sin(angle)
    corners = [(-w / 2, -h / 2), (w / 2, -h / 2), (-w / 2, h / 2), (w / 2, h / 2)]
    xs = [(x * ca - y * sa) * scale for x, y in corners]
    ys = [(x * sa + y * ca) * scale * squash for x, y in corners]
    ow, oh = int(math.ceil(max(xs) - min(xs))) + 2, int(math.ceil(max(ys) - min(ys))) + 2
    out = np.zeros((oh, ow, 4), np.float32)
    yy, xx = np.mgrid[0:oh, 0:ow].astype(np.float64)
    dx, dy = (xx + 0.5 - ow / 2) / scale, -(yy + 0.5 - oh / 2) / (scale * squash)      # y up, unscaled
    lx = dx * ca + dy * sa
    ly = -dx * sa + dy * ca
    sx = np.floor(lx + w / 2).astype(int)
    sy = np.floor(h / 2 - ly).astype(int)
    ok = (sx >= 0) & (sx < w) & (sy >= 0) & (sy < h)
    out[ok] = src[sy[ok], sx[ok]]
    return out


def underwater_tint(depth, tint_hex, deep_hex):
    k = min(max(depth / 7.0, 0.0), 1.0)
    white = np.ones(3)
    tint = N.hex2srgb(tint_hex)
    deep = N.hex2srgb(deep_hex)
    c = white + (tint - white) * 0.35
    c = c + (deep - c) * (k * 0.75)
    a = 0.95 + (0.6 - 0.95) * k
    return c, a


def foreshorten(p):
    a = np.array(P.project(p, STAND)[:2])
    bx = np.array(P.project((p[0] + 0.1, p[1], p[2]), STAND)[:2])
    bz = np.array(P.project((p[0], p[1] + 0.1, p[2]), STAND)[:2])
    lx, lz = np.linalg.norm(bx - a), np.linalg.norm(bz - a)
    return min(1.0, lz / lx) if lx > 1e-4 else 1.0


def put_fish(img, layout, fid, ux, uy, uz, cm, head, frame=0):
    """Unity point (x, -depth, z) -> Blender (x, z, -depth); scale/squash/tint exactly like FishAgent."""
    path = os.path.join(N.OUT, "fish", f"{fid}_t{frame}.png")
    if not os.path.exists(path):
        return
    spr = load_td(path)
    bp = (ux, uz, uy)
    px, py, depth = P.project(bp, STAND)
    vis = 0.85 + cm / 100 * 1.5
    sc = max(0.35, (P.F_PX / depth) * vis / max(8.0, spr.shape[1]))
    sq = min(max(foreshorten(bp) * 1.8 + 0.25, 0.4), 0.9)
    t = transform_sprite(spr, sc, sq, head)
    tint, a = underwater_tint(-uy, layout["waterTint"], layout["waterDeep"])
    cx, cy = W / 2 + px, H / 2 - py
    paste_td(img, t, int(round(cx - t.shape[1] / 2)), int(round(cy - t.shape[0] / 2)), tint, a)


def shadow_test(layout):
    """Readability check: top views at 0/35/90 deg, near (7 m) and far (40 m), shallow and deep."""
    img = load_td(os.path.join(N.OUT, "lake_back.png"))
    specs = [("largemouth_bass", 38), ("rainbow_trout", 34), ("crucian_carp", 24)]
    for j, (fid, cm) in enumerate(specs):
        for i, ang in enumerate((0, 35, 90)):
            put_fish(img, layout, fid, -9.0 + i * 3.2 + j * 9.6 - 4.5, -1.0, 7.0, cm, math.radians(ang))
            put_fish(img, layout, fid, -14.0 + i * 4.0 + j * 13.0 - 6.0, -2.5, 16.0, cm, math.radians(ang))
            put_fish(img, layout, fid, -30.0 + i * 8.0 + j * 26.0 - 12.0, -1.5, 40.0, cm, math.radians(ang))
    x0, y0 = (W - CW) // 2, (H - CH) // 2
    crop = img[y0:y0 + CH, x0:x0 + CW]
    big = N.upscale(crop, 2)
    big[..., 3] = 1.0
    C.save_pixels(big[::-1].copy(), os.path.join(N.WORK, "shadow_test.png"))


def main():
    out = N.OUT
    layout = json.load(open(os.path.join(out, "lake.json"), encoding="utf-8"))
    if "--test" in sys.argv:
        shadow_test(layout)
    img = load_td(os.path.join(out, "lake_back.png"))
    # --- fish shadows
    fishes = [("largemouth_bass", (-3.0, -1.5, 12.0), 38, math.radians(20)),
              ("crucian_carp", (4.0, -1.0, 18.0), 24, math.radians(160)),
              ("rainbow_trout", (4.0, -2.2, 8.0), 34, math.radians(-35))]
    for fid, (ux, uy, uz), cm, head in fishes:
        put_fish(img, layout, fid, ux, uy, uz, cm, head)
    front = load_td(os.path.join(out, "lake_front.png"))
    paste_td(img, front, 0, 0)
    # --- angler idle with the feet on the projected feet point
    cj = os.path.join(out, "character.json")
    ap = os.path.join(out, "character", "angler_idle.png")
    if os.path.exists(cj) and os.path.exists(ap):
        ch = json.load(open(cj, encoding="utf-8"))
        spr = load_td(ap)
        fx, fy, _ = P.project((0, 0, STAND), STAND)
        feet_x, feet_y = W / 2 + fx, H / 2 - fy          # top-down
        x0 = int(round(feet_x)) - ch["cropW"] // 2
        y0 = int(round(feet_y)) - (ch["cropH"] - ch["feetPx"])
        paste_td(img, spr, x0, y0)
        pose = [p for p in ch["poses"] if p["name"] == "idle"][0]
        hx = x0 + pose["handX"]
        hy = y0 + (ch["cropH"] - pose["handY"])
        hand = (pose["hx"], pose["hz"], STAND + pose["hy"])
        d = (pose["rx"], pose["rz"], pose["ry"])
        a2 = P.project(hand, STAND)
        b2 = P.project((hand[0] + d[0] * 0.3, hand[1] + d[1] * 0.3, hand[2] + d[2] * 0.3), STAND)
        vx, vy = b2[0] - a2[0], -(b2[1] - a2[1])
        ln = math.hypot(vx, vy)
        vx, vy = vx / ln, vy / ln
        rod_len = 54
        for k in range(rod_len):
            x = hx + vx * k
            y = hy + vy * k
            c = N.hex2srgb("#2c2621") if k > 6 else N.hex2srgb("#4a3a2c")
            xi, yi = int(round(x)), int(round(y))
            if 0 <= xi < W and 0 <= yi < H:
                img[yi, xi, :3] = c
                if k < 20:          # thicker butt section
                    img[yi, min(xi + 1, W - 1), :3] = c
    x0, y0 = (W - CW) // 2, (H - CH) // 2
    crop = img[y0:y0 + CH, x0:x0 + CW]
    big = N.upscale(crop, 2)
    big[..., 3] = 1.0
    C.save_pixels(big[::-1].copy(), os.path.join(out, "preview_lake.png"))
    print("NAT preview done")


if __name__ == "__main__":
    main()
