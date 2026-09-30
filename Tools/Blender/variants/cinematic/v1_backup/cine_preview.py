"""
FishingKing - cinematic variant: in-game style preview of the lake.
Central 480x270 crop of the stage, back layer -> 3 fish top-shadows (tinted like StageView.UnderwaterTint)
-> front layer -> angler idle (feet on the projected feet point) + rod line, scaled 2x -> preview_lake.png

Run:  blender -b --python Tools/Blender/variants/cinematic/cine_preview.py
"""
import sys
import os
import math
import json
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cine_common as K  # noqa: E402

P = K.P
W, H = P.W, P.H
STAND = 1.0
FISH = os.path.join(K.OUT, "fish")
# Unity game position (x right, y = -depth, z forward), species, size in cm, heading (deg, 0 = +x)
SHADOWS = [((-3.0, -1.5, 12.0), "largemouth_bass", 38, 200.0, 0),
           ((4.0, -1.0, 18.0), "crucian_carp", 26, 20.0, 1),
           ((1.5, -2.2, 7.0), "rainbow_trout", 34, 150.0, 0)]


def rotate_nearest(img, deg, sx, sy):
    """Scale (sx, sy) then rotate by deg (counter-clockwise on screen), nearest neighbour."""
    h, w = img.shape[:2]
    a = math.radians(deg)
    ca, sa = math.cos(a), math.sin(a)
    corners = np.array([[x, y] for x in (-w / 2, w / 2) for y in (-h / 2, h / 2)], np.float32)
    corners[:, 0] *= sx
    corners[:, 1] *= sy
    rx = corners[:, 0] * ca - corners[:, 1] * sa
    ry = corners[:, 0] * sa + corners[:, 1] * ca
    ow, oh = int(math.ceil(rx.max() - rx.min())) + 2, int(math.ceil(ry.max() - ry.min())) + 2
    yy, xx = np.mgrid[0:oh, 0:ow].astype(np.float32)
    X = xx + 0.5 - ow / 2
    Y = -(yy + 0.5 - oh / 2)  # y up
    ux = (X * ca + Y * sa) / sx
    uy = (-X * sa + Y * ca) / sy
    ix = np.floor(ux + w / 2).astype(np.int32)
    iy = np.floor(h / 2 - uy).astype(np.int32)
    ok = (ix >= 0) & (ix < w) & (iy >= 0) & (iy < h)
    out = np.zeros((oh, ow, 4), np.float32)
    out[ok] = img[iy[ok], ix[ok]]
    return out


def paste(dst, src, cx, cy):
    """Alpha-paste src centred at (cx, cy) (top-down pixel coords)."""
    h, w = src.shape[:2]
    x0 = int(round(cx - w / 2))
    y0 = int(round(cy - h / 2))
    paste_at(dst, src, x0, y0)


def paste_at(dst, src, x0, y0):
    h, w = src.shape[:2]
    sx0, sy0 = max(0, -x0), max(0, -y0)
    dx0, dy0 = max(0, x0), max(0, y0)
    dx1, dy1 = min(dst.shape[1], x0 + w), min(dst.shape[0], y0 + h)
    if dx1 <= dx0 or dy1 <= dy0:
        return
    s = src[sy0:sy0 + dy1 - dy0, sx0:sx0 + dx1 - dx0]
    a = s[..., 3:4]
    dst[dy0:dy1, dx0:dx1, :3] = dst[dy0:dy1, dx0:dx1, :3] * (1 - a) + s[..., :3] * a


def line(dst, x0, y0, x1, y1, col, width=1):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for k in range(n + 1):
        t = k / n
        x = int(round(x0 + (x1 - x0) * t))
        y = int(round(y0 + (y1 - y0) * t))
        for dx in range(width):
            if 0 <= y < dst.shape[0] and 0 <= x + dx < dst.shape[1]:
                dst[y, x + dx, :3] = col if callable(col) is False else col(t)


def main():
    stage = json.load(open(os.path.join(K.OUT, "stage_lake.json"), encoding="utf-8"))
    tint = K.hexf(stage["waterTint"])
    deep = K.hexf(stage["waterDeep"])
    img = K.load(os.path.join(K.OUT, "lake_back.png"))[..., :3].copy()
    for (gx, gy, gz), fid, cm, heading, frame in SHADOWS:
        path = os.path.join(FISH, f"{fid}_t{frame}.png")
        if not os.path.exists(path):
            continue
        spr = K.load(path)
        px, py, depth = P.project((gx, gz, gy), STAND)  # Blender axes: x, forward, up
        vis = 0.85 + cm / 100 * 1.5
        scale = (P.F_PX / depth) * vis / spr.shape[1]
        # foreshortening of a flat thing on the water (same rule as FishAgent)
        _, pz1, _ = P.project((gx, gz + 0.1, gy), STAND)
        px1, _, _ = P.project((gx + 0.1, gz, gy), STAND)
        fs = min(1.0, abs(pz1 - py) / max(abs(px1 - px), 1e-4))
        squash = min(0.9, max(0.4, fs * 1.8 + 0.25))
        spr_t = rotate_nearest(spr, heading, scale, scale * squash)
        k = min(1.0, -gy / 7.0)
        c = K.mix(K.mix(np.ones(3, np.float32), tint, 0.35), deep, k * 0.75)
        spr_t[..., :3] *= c
        spr_t[..., 3] *= 0.8 * (1 - 0.35 * k)
        paste(img, spr_t, W / 2 + px, H / 2 - py)
    front = K.load(os.path.join(K.OUT, "lake_front.png"))
    paste_at(img, front, 0, 0)
    # angler: sprite feet pixel (48, 10 px above the bottom) on the projected feet point
    ch = json.load(open(os.path.join(K.OUT, "character", "character.json"), encoding="utf-8"))
    ang = K.load(os.path.join(K.OUT, "character", "angler_idle.png"))
    fx, fy, _ = P.project((0, 0, STAND), STAND)
    feet_x, feet_y = W / 2 + fx, H / 2 - fy
    x0 = int(round(feet_x - ch["cropW"] // 2))
    y0 = int(round(feet_y - (ch["cropH"] - ch["feetPx"])))
    paste_at(img, ang, x0, y0)
    idle = [p for p in ch["poses"] if p["name"] == "idle"][0]
    hx, hy = x0 + idle["handX"], y0 + ch["cropH"] - idle["handY"]
    # rod: project hand + direction in 3D so it leans correctly, ~50 px long
    hand = np.array([idle["hx"], idle["hz"], idle["hy"] + STAND])
    rod = np.array([idle["rx"], idle["rz"], idle["ry"]])
    tip = hand + rod * 1.7
    tx, ty, _ = P.project(tuple(tip), STAND)
    bx, by, _ = P.project(tuple(hand), STAND)
    ex, ey = hx + (tx - bx), hy - (ty - by)
    L = math.hypot(ex - hx, ey - hy)
    ex, ey = hx + (ex - hx) * 50 / L, hy + (ey - hy) * 50 / L
    line(img, hx, hy, ex, ey, K.hexf("#1a1412"), 1)
    # the low sun catches a few pixels of the blank near the tip
    line(img, hx + (ex - hx) * 0.55, hy + (ey - hy) * 0.55, hx + (ex - hx) * 0.8, hy + (ey - hy) * 0.8,
         K.hexf("#9a6a48"), 1)
    line(img, hx - 1, hy + 1, hx + (ex - hx) * 0.12, hy + (ey - hy) * 0.12, K.hexf("#2a2220"), 2)  # grip
    x0c, y0c, x1c, y1c = K.CROP
    crop = img[y0c:y1c, x0c:x1c]
    K.save(K.upscale(crop, 2), os.path.join(K.OUT, "preview_lake.png"))
    print("PREVIEW done")


if __name__ == "__main__":
    main()
