"""
Encounter set SWAMP_TOP - the arapaima's TEASE seen from ABOVE the surface (topwater frog / popper). The companion of
encounter_sets/swamp.py: the omen / eyes / approach and the lunge stay in the swamp UNDERWATER set; the camera rises
through the waterline into this straight-down view for the tease (경계 / 호기심 / 흥분) and the noseIn, then cuts back
under water for the strike. The arapaima is the 3D model rendered from above as a dark shadow in Unity (no fish art).

Built by hyb_encounter.py like any set (`hyb_encounter.py -- swamp_top [--install]`, also part of `--all`). It meets the
runner's module API (bg / mid / fore / frame) and, from layers(), also writes its top-view sprites into the same
encounter/ folder (copied into Assets/Resources/Sprites/Encounter by --install and by build_hybrid.ps1 -Install), a
review sheet and a mock frame (_tmp/legend_art/swamp_top/sprites.png, mock.png).

Straight-down view, 1:1 pixels (PixelView PPU), dusk light from the screen's upper left, the angler towards the BOTTOM of
the screen (the line leaves the frog's nose downwards):
  uw_swamp_top_bg.png        640x400 opaque, TILEABLE on both axes: murky brown-green water (stage play-area water
                             #34442c with a tannin-brown twin ramp), slow depth patches, dim drowned branches deep in
                             the murk, dappled canopy light with faint caustic nets, sparse pollen / duckweed specks
  uw_swamp_top_mid.png       768x200 (runner layer, tileable in x): surface scum band - meandering pollen / foam lines
                             with duckweed caught in them (an optional slow drift overlay)
  uw_swamp_top_fore.png      256x128 (runner layer): an out-of-focus overhanging cypress branch with moss, bottom-left
  enc_frame_swamp_top.png    24x24: identical copy of enc_frame_swamp (runner requirement; the window keeps the swamp one)
  uw_swamp_top_float_*.png   drifting props from above: pad_0 / pad_1 (torn, decaying) / pad_2 (with a white lily),
                             duck_0..2 (duckweed clusters), leaf_0 (brown) / leaf_1 (yellow-green), twig_0
  lure_frog_top_0..3.png     24x24, the frog lure from above (fk_items bait_frog materials, rendered top-down), nose
                             DOWN; pivot = body centre (12, 16) top-down: 0 idle float, 1 legs drawn in (knees out),
                             2 legs kicked straight back, 3 hop (lifted a little, its shadow on the water)
  lure_popper_top_0..3.png   24x28, the popper from above (fk_items bait_popper parts / materials, the frog's light,
                             pivot and line tie), nose DOWN, its frames meaning what the frog's do: 0 at rest
                             (tail-down, the cup's hollow tipped up, the feathers sunk in the murk), 1 the chug (the face
                             digs in, the feathers flare), 2 the glide (level, the feathers streaming back), 3 the pop
                             (the nose kicked up, the cup to the sky, its shadow on the water); the spray, wake and
                             rings are the shared fx_top_* (no splash in the frames)
  fx_top_ring_0..3.png       32x32 expanding ring (crest + trough), pivot centre
  fx_top_wake_0..2.png       40x48 V-wake + leg churn behind the swimming frog, pivot (20, 32) = the frog's pivot
  fx_top_splash_0..3.png     32x32 "퐁" splash: crown -> droplets -> landing rings, pivot centre (= the frog's pivot)
  fx_top_bulge_0..3.png      48x48 water swirling / bulging over the rising fish, pivot centre
  fx_top_bubble.png          5x5 surface bubble
"""
import os
import sys
import math
import random
import shutil
import importlib.util
import numpy as np
import bpy
from mathutils import Vector, Matrix
import hyb_core as R
import fk_common as C
import hyb_encounter_kit as E

SET = "swamp_top"
PRESET = "swamp"
LURES = []                  # no side-view billboards (frog / popper belong to swamp.py); their top frames are made here
HORIZON = 0                 # straight down: no horizon
REVIEW = dict(ray=("#c8b478", 0.0, 470, 0), mid=(-64, 100), fore=(0, 400 - 128))

# ------------------------------------------------------------------ palette (swamp stage water + underwater murk)
WATER = ["#1e2a20", "#26321f", "#2c3a26", "#34442c", "#3a4a30", "#48573e"]   # deep .. lit; #34442c = stage play water
TANNIN = ["#1c2416", "#252c1a", "#2e3620", "#3a4028", "#4a4a2a", "#5a5a32"]  # the brown twin (swamp MURK / ceiling)
CAUSTIC = "#6a6a3a"                                                          # rare caustic cores (swamp CEIL olive)
SHEEN = ["#525a46", "#6c6e56", "#8a8466"]                                    # sky sheen: ring / wake crests
SPRAY = ["#a8a070", "#c8c098", "#e8e0d6", "#fffeef"]                         # spray / droplets / bubbles
LILY = ["#425d3b", "#607f48", "#819e56", "#adc06e"]                          # hyb_swamp LILY (graded): pads, duckweed
FLOWER = ["#a29d99", "#e8e0d6", "#fffeef"]                                   # hyb_swamp white water lily
YELLOW = ["#987541", "#e8c040"]
LEAF_B = ["#463a31", "#6a5847", "#987541", "#a78450"]                        # brown fallen leaf: outline..lit
LEAF_G = ["#2b3b28", "#566440", "#7b8254", "#8d9553"]                        # yellow-green decaying leaf
WOOD = ["#282625", "#413c36", "#5e5549", "#7d715d"]                          # hyb_swamp WOOD (graded)
FORE = ["#060a05", "#0a0e08", "#121a0e", "#26301c"]                          # swamp.py FORE
FROG_PAL = ["#7cf35c", "#5ab442", "#51a63c", "#3a7a2a", "#387530", "#234e1e", "#1a3d1e", "#213827", "#182720",
            "#172622", "#c8d0da", "#8388a7", "#403f51", "#101010", "#0c0a19"]   # bait_frog / lure_frog_0/1 colours
LX, LY = -0.62, -0.78        # screen direction TOWARDS the light (x right, y down): the upper left

# ------------------------------------------------------------------ runtime hints (the code's top-view row, preview only)
PROFILE = dict(
    view="top",
    of="swamp",                              # the underwater set this view belongs to
    clear="#1e2a20", water="#34442c",        # fill behind the bg / the stage play-area water it continues
    frog_pivot=(12, 19), frog_nose=(12, 26),  # lure_frog_top_*: body centre / line tie (top-down px, 24x28)
    wake_pivot=(20, 32), bulge_pivot=(24, 24), ring_pivot=(16, 16), splash_pivot=(16, 16),
    shadow=("#10180e", 0.62),                # suggested tint / max alpha of the arapaima shadow (the 3D model from above)
    line="#c8c098",
    fps=dict(frog_kick=8, wake=8, ring=10, splash=12, bulge_loop=4),
)

# ------------------------------------------------------------------ layout
BG_W, BG_H = 640, 400
HERE = os.path.dirname(os.path.abspath(__file__))
REVIEW_DIR = os.path.join(C.TMP, "legend_art", "swamp_top")


# ============================================================================ helpers
def _pnoise(x, y, px, py, seed):
    """Periodic value noise (period px, py lattice cells) - tileable textures."""
    xi, yi = np.floor(x), np.floor(y)
    fx, fy = x - xi, y - yi
    ux, uy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)

    def h(a, b):
        return R._hash(np.mod(a, px) * 57.0 + np.mod(b, py) * 131.0, seed)
    a, b, c, d = h(xi, yi), h(xi + 1, yi), h(xi, yi + 1), h(xi + 1, yi + 1)
    return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy


def _pfbm(x, y, cx, cy, seed, oct=3, W=BG_W, H=BG_H):
    """Tileable fbm over a W x H image; the cell sizes cx / 2^o, cy / 2^o must divide W, H."""
    s, amp, tot = 0.0, 1.0, 0.0
    for o in range(oct):
        kx, ky = cx / 2 ** o, cy / 2 ** o
        s = s + amp * _pnoise(x / kx, y / ky, round(W / kx), round(H / ky), seed + o * 7.1)
        tot += amp
        amp *= 0.5
    return s / tot


def _pworley(x, y, cell, seed, W=BG_W, H=BG_H):
    """Tileable Worley F1 / F2 (one jittered point per cell)."""
    nx, ny = W // cell, H // cell
    pts = np.random.default_rng(seed).random((ny, nx, 2)) * 0.8 + 0.1
    ci, cj = np.floor(x / cell).astype(int), np.floor(y / cell).astype(int)
    f1 = np.full(x.shape, 1e9)
    f2 = np.full(x.shape, 1e9)
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            a, b = ci + di, cj + dj
            p = pts[np.mod(b, ny), np.mod(a, nx)]
            d = np.hypot(x - (a + p[..., 0]) * cell, y - (b + p[..., 1]) * cell)
            f2 = np.where(d < f1, f1, np.minimum(f2, d))
            f1 = np.minimum(f1, d)
    return f1, f2


def _disc(m, cx, cy, r, wrap=True):
    h, w = m.shape
    x0, x1 = int(math.floor(cx - r - 1)), int(math.ceil(cx + r + 1))
    y0, y1 = int(math.floor(cy - r - 1)), int(math.ceil(cy + r + 1))
    xs, ys = np.arange(x0, x1 + 1), np.arange(y0, y1 + 1)
    inside = (xs[None, :] + 0.5 - cx) ** 2 + (ys[:, None] + 0.5 - cy) ** 2 < r * r
    yi, xi = np.nonzero(inside)
    Y, X = ys[yi], xs[xi]
    if wrap:
        m[np.mod(Y, h), np.mod(X, w)] = True
    else:
        ok = (Y >= 0) & (Y < h) & (X >= 0) & (X < w)
        m[Y[ok], X[ok]] = True


def _px(img, x, y, hexc, a=1.0):
    x, y = int(math.floor(x)), int(math.floor(y))
    if 0 <= y < img.shape[0] and 0 <= x < img.shape[1]:
        img[y, x, :3] = E.rgb(hexc)
        img[y, x, 3] = a


def _circle(cx, cy, r):
    """Pixels of a 1 px circle (parametric, no doubles) -> list of (x, y, angle)."""
    out, seen = [], set()
    n = max(12, int(r * 8))
    for k in range(n):
        a = 2 * math.pi * k / n
        x, y = int(math.floor(cx + r * math.cos(a))), int(math.floor(cy + r * math.sin(a)))
        if (x, y) not in seen:
            seen.add((x, y))
            out.append((x, y, a))
    return out


def _lit(a):
    """-1..1: how much a direction (angle, y down) faces the light."""
    return math.cos(a) * LX + math.sin(a) * LY


def _grid(h, w):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    return yy, xx


def _erode(m):
    return m & R.shift(m, 1, 0) & R.shift(m, -1, 0) & R.shift(m, 0, 1) & R.shift(m, 0, -1)


# ============================================================================ uw_swamp_top_bg (640x400, opaque, tileable)
def _branch(m, x, y, ang, L, r0, r1, rng, depth=0):
    n = int(L)
    for i in range(n):
        t = i / max(1, n - 1)
        ang += rng.uniform(-0.06, 0.06)
        x += math.cos(ang)
        y += math.sin(ang)
        r = r0 + (r1 - r0) * t
        _disc(m, x, y, r)
        if depth == 0 and 0.2 < t < 0.8 and rng.random() < 0.03:
            _branch(m, x, y, ang + rng.choice((-1, 1)) * rng.uniform(0.5, 0.9), L * rng.uniform(0.2, 0.35),
                    r * 0.6, 0.6, rng, 1)


def make_bg():
    h, w = BG_H, BG_W
    yy, xx = _grid(h, w)
    th = R.bayer(h, w)
    thr = 0.5 + (th - 0.5) * 0.5                                     # narrow dithered band transitions
    wx = (_pfbm(xx, yy, 80, 80, 3.0, 2) - 0.5) * 48
    wy = (_pfbm(xx, yy, 80, 80, 9.0, 2) - 0.5) * 48
    X, Y = xx + wx, yy + wy
    # depth patches: deeper murk (darker) .. silt shallows (lighter)
    depth = _pfbm(X, Y, 160, 100, 1.0, 3)
    depth = (depth - depth.min()) / (depth.max() - depth.min())
    tone = 3.05 - (depth - 0.5) * 2.6
    # dappled canopy light: soft lit patches (one step lighter), caustic nets inside them
    sun = _pfbm(xx + wy * 0.7, yy + wx * 0.7, 160, 100, 5.0, 3)
    sun = np.clip((sun - 0.56) / 0.16, 0, 1)
    tone += sun * 0.75
    # drowned branches deep in the murk: one step darker, blurred by the dither
    rng = random.Random(31)
    sunk = np.zeros((h, w), bool)
    for _ in range(6):
        _branch(sunk, rng.uniform(0, w), rng.uniform(0, h), rng.uniform(0, 2 * math.pi), rng.uniform(70, 150),
                rng.uniform(2.2, 3.4), 1.0, rng)
    halo = R.dilate(R.dilate(sunk)) & ~sunk
    tone -= np.where(sunk & (th < 0.8), 1.0, 0.0) + np.where(halo & (th < 0.3), 1.0, 0.0)
    k = np.clip(np.floor(tone + thr - 0.5), 0, 5).astype(int)
    # caustics: a warped Worley net, only in the lit patches
    f1, f2 = _pworley(xx + wx * 0.35, yy + wy * 0.35, 20, 12)
    edge = (f2 - f1) < 1.25
    caus = edge & (th < sun * 0.9) & ~sunk
    k = np.where(caus, np.minimum(k + 1, 5), k)
    # hue: tannin-brown and greener water in big soft patches
    hue = _pfbm(X * 0.9 + 17, Y * 0.9 + 5, 160, 100, 7.0, 2)
    brown = hue + (th - 0.5) * 0.1 > 0.5
    pal = R.Pal(WATER + TANNIN + [CAUSTIC] + SHEEN[:1] + LILY[1:3])
    gl = np.array([pal.index(c) for c in WATER])
    bl = np.array([pal.index(c) for c in TANNIN])
    idx = np.where(brown, bl[k], gl[k])
    core = caus & ((f2 - f1) < 0.55) & (sun > 0.75) & (th < 0.35)
    idx[core] = pal.index(CAUSTIC)
    # sparse surface specks (pollen, stray duckweed fronds): they sell the surface plane when the layer pans
    rs = np.random.default_rng(5)
    for _ in range(620):
        x, y = int(rs.integers(0, w)), int(rs.integers(0, h))
        q = rs.random()
        idx[y, x] = pal.index(SHEEN[0] if q < 0.6 else (LILY[1] if q < 0.9 else LILY[2]))
        if q > 0.9 and rs.random() < 0.5:
            idx[y, (x + 1) % w] = pal.index(LILY[1])
    img = R.to_rgba(idx, pal)
    img[..., 3] = 1.0
    return img


# ============================================================================ uw_swamp_top_mid (768x200, tileable in x)
def make_mid():
    """Surface scum band: meandering pollen / foam lines with duckweed caught in them (transparent elsewhere)."""
    h, w = 200, 768
    img = E.blank(h, w)
    th = R.bayer(h, w)
    rng = random.Random(9)
    rn = np.random.default_rng(9)
    for li, y0 in enumerate((34, 92, 150, 182)):
        k1, k2, k3 = rng.randint(1, 2), rng.randint(3, 5), rng.randint(7, 11)
        p1, p2, p3 = (rng.uniform(0, 6.28) for _ in range(3))
        amp = rng.uniform(8, 16)
        # dash pattern along the line (periodic in x)
        dash = _pnoise(np.arange(w) / 12.0, np.zeros(w) + li * 3.3, w // 12, 7, 20.0 + li) > 0.55
        for x in range(w):
            t = 2 * math.pi * x / w
            y = y0 + amp * math.sin(k1 * t + p1) + amp * 0.35 * math.sin(k2 * t + p2) + 2.0 * math.sin(k3 * t + p3)
            yi = int(math.floor(y))
            if not dash[x] or not (0 <= yi < h):
                continue
            c = SHEEN[1] if th[yi, x] < 0.3 else SHEEN[0]
            _px(img, x, yi, c)
            # duckweed / pollen collect along the scum line
            if rn.random() < 0.18:
                dy = int(rn.integers(-3, 4))
                if 0 <= yi + dy < h and img[yi + dy, x, 3] == 0:
                    q = rn.random()
                    _px(img, x, yi + dy, LILY[1] if q < 0.6 else (LILY[2] if q < 0.92 else LILY[3]))
    # loose pollen specks
    for _ in range(260):
        x, y = int(rn.integers(0, w)), int(rn.integers(0, h))
        if img[y, x, 3] == 0:
            _px(img, x, y, SHEEN[0])
    return img


# ============================================================================ uw_swamp_top_fore (256x128)
def make_fore():
    """An out-of-focus overhanging cypress branch with moss clumps, seen from above (bottom-left of the window)."""
    h, w = 128, 256
    yy, xx = _grid(h, w)
    th = R.bayer(h, w)
    body = np.zeros((h, w), bool)
    fol = np.zeros((h, w), bool)
    rng = random.Random(4)
    # the limb: from the left edge, arching up-right; two side twigs
    for (x0, y0, x1, y1, bend, r0, r1) in ((-12, 118, 200, 70, 26, 11, 3.5), (70, 96, 150, 132, -8, 4, 1.5),
                                          (120, 84, 170, 40, 6, 3.5, 1.2)):
        n = 90
        for i in range(n + 1):
            t = i / n
            cx = x0 + (x1 - x0) * t
            cy = y0 + (y1 - y0) * t - bend * math.sin(math.pi * t)
            r = (r0 + (r1 - r0) * t) * (1 + 0.12 * math.sin(t * 23 + x0))
            _disc(body, cx, cy, r, wrap=False)
            if t > 0.35 and rng.random() < 0.12:              # feathery cypress sprays along the outer part
                for _ in range(3):
                    _disc(fol, cx + rng.uniform(-14, 14), cy + rng.uniform(-12, 12), rng.uniform(3, 7), wrap=False)
    fol &= E.fbm(xx / 6.0, yy / 6.0, 3.0) > 0.42
    img = E.blank(h, w)
    allm = body | fol
    E.put(img, fol, FORE[1])
    E.put(img, fol & (E.fbm(xx / 3.0, yy / 3.0, 8.0) > 0.62), FORE[2])
    E.put(img, body, FORE[1])
    E.put(img, body & (E.fbm(xx / 14.0, yy / 14.0, 6.0) > 0.6), FORE[0])
    lit = body & ~R.shift(body, 2, 2, False)                      # the dim rim facing the glow (upper left)
    E.put(img, lit & (th < 0.6), FORE[3])
    # out of focus: a dithered halo
    E.put(img, R.dilate(allm) & ~allm & (th < 0.5), FORE[1])
    return img


# ============================================================================ floats (top view)
def make_pad(W, H, cx, cy, r, notch, seed, tear=None, decay=False, flower=None):
    yy, xx = _grid(H, W)
    th = R.bayer(H, W)
    dx, dy = xx - cx, yy - cy
    rad = np.hypot(dx, dy)
    ang = np.arctan2(dy, dx)
    wob = 1 + 0.03 * np.sin(ang * 5 + seed) + 0.02 * np.sin(ang * 9 + seed * 1.7)
    dang = np.abs((ang - notch + math.pi) % (2 * math.pi) - math.pi)
    slit = (dang < 0.04 + 0.15 * rad / r) & (rad > 0.7)
    pad = (rad < r * wob) & ~slit
    if tear is not None:
        ta, tr = tear
        pad &= np.hypot(xx - (cx + r * math.cos(ta)), yy - (cy + r * math.sin(ta))) > tr
    lit = (dx * LX + dy * LY) / r
    img = E.blank(H, W)
    rn = np.random.default_rng(int(seed * 100))
    tone = np.where(lit + (th - 0.5) * 0.35 > -0.1, 2, 1)
    # veins: radial, one step darker, broken
    vein = np.zeros((H, W), bool)
    nv = 11
    for kk in range(nv):
        a = notch + (kk + 0.5) * 2 * math.pi / nv
        for s in np.arange(0.18 * r, 0.86 * r, 0.5):
            x, y = int(math.floor(cx + s * math.cos(a))), int(math.floor(cy + s * math.sin(a)))
            if 0 <= y < H and 0 <= x < W:
                vein[y, x] = True
    vein &= pad & (th < 0.7)
    tone = np.where(vein, tone - 1, tone)
    rim = pad & ~_erode(pad)
    tone = np.where(rim & (lit > 0.15), 3, tone)
    tone = np.where(rim & (lit < -0.35), 0, tone)
    tone = np.where(pad & (lit > 0.3) & (rn.random((H, W)) < 0.03), 3, tone)
    lut = np.array([E.rgb(c) for c in LILY])
    img[pad, :3] = lut[np.clip(tone, 0, 3)][pad]
    img[pad, 3] = 1.0
    if decay:                                                          # brown decaying edge (torn / old pad)
        edge = pad & (rad > r * 0.72) & (E.fbm(xx / 5.0, yy / 5.0, seed) > 0.52)
        E.put(img, edge, LEAF_B[1])
        E.put(img, edge & (lit > 0.1) & (th < 0.5), LEAF_B[2])
    # wet dark edge on the water side
    out = R.dilate(pad) & ~pad
    E.put(img, out & (dx * LX + dy * LY < 0.25 * np.maximum(rad, 1)), WATER[0])
    E.put(img, out & ~(dx * LX + dy * LY < 0.25 * np.maximum(rad, 1)) & (th < 0.5), WATER[1])
    if flower is not None:
        _flower(img, pad, *flower)
    return img


def _flower(img, pad, fx, fy, rp, seed):
    H, W = img.shape[:2]
    yy, xx = _grid(H, W)
    dx, dy = xx - fx, yy - fy
    rad = np.hypot(dx, dy)
    ang = np.arctan2(dy, dx)
    n = 8

    def petals(Rp, off):
        p = np.mod((ang - off) * n / (2 * math.pi), 1.0)
        prof = 1 - np.abs(p - 0.5) * 2
        return rad < Rp * (0.3 + 0.7 * prof ** 0.8), prof
    outer, po = petals(rp, seed)
    inner, pi_ = petals(rp * 0.66, seed + math.pi / n)
    lit = (dx * LX + dy * LY) / np.maximum(rad, 0.5)
    allm = outer | inner
    # shadow of the flower on the pad / water (lower right)
    sh = R.shift(allm, 1, 1, False) & ~allm
    E.put(img, sh & pad, LILY[0])
    E.put(img, sh & ~pad, WATER[0])
    E.put(img, outer, FLOWER[1])
    E.put(img, outer & ((lit < -0.25) | (po < 0.22)), FLOWER[0])
    E.put(img, inner, FLOWER[2])
    E.put(img, inner & (lit < -0.3), FLOWER[1])
    E.put(img, rad < 1.7, YELLOW[1])
    E.put(img, (rad < 1.7) & (lit < -0.2), YELLOW[0])


def make_duck(W, H, seed, dens=1.0):
    yy, xx = _grid(H, W)
    cx, cy = W / 2, H / 2
    e = ((xx - cx) / (W * 0.44)) ** 2 + ((yy - cy) / (H * 0.44)) ** 2
    n = E.fbm(xx / 6.0, yy / 6.0, seed)
    f = (1 - e) * 0.9 + (n - 0.5) * 1.2
    rn = np.random.default_rng(int(seed * 10))
    r1, r2, r3 = rn.random((H, W)), rn.random((H, W)), rn.random((H, W))
    core = f > 0.62
    loose = (r1 < np.clip(f / 0.62, 0, 1) ** 1.6 * 0.5 * dens) & (f > 0.02)
    fr = (core | loose) & (r1 < 0.97)
    fr |= R.shift(loose & (r2 < 0.35), 0, 1, False) & (f > 0)          # 2 px fronds
    img = E.blank(H, W)
    sh = R.shift(fr, 1, 1, False) & ~fr & (r3 < 0.55)
    E.put(img, sh, WATER[1])
    E.put(img, fr, LILY[1])
    E.put(img, fr & (r2 < 0.34), LILY[2])
    E.put(img, fr & (r2 < 0.07), LILY[3])
    E.put(img, fr & core & (r2 > 0.9), LILY[0])
    return img


def make_leaf(W, H, L, Wd, ang, cols, seed):
    """cols: outline, shade, base, lit. A pointed leaf with a midrib and a stalk, the half facing the light lighter."""
    yy, xx = _grid(H, W)
    th = R.bayer(H, W)
    cx, cy = W / 2, H / 2
    ca, sa = math.cos(ang), math.sin(ang)
    du = (xx - cx) * ca + (yy - cy) * sa
    dv = -(xx - cx) * sa + (yy - cy) * ca
    u, v = du / (L / 2), dv / (Wd / 2)
    prof = (1 - np.abs(u) ** 1.7) * (1 + 0.18 * u) + 0.04 * np.sin(u * 9 + seed)
    body = (np.abs(v) < prof) & (np.abs(u) < 1)
    stalk = (np.abs(dv) < 0.55) & (u < -0.9) & (u > -1.28)
    img = E.blank(H, W)
    litside = (dv * (-sa * LX + ca * LY)) > 0                       # the half whose outward normal faces the light
    E.put(img, R.dilate(body) & ~body & ~stalk, cols[0])
    E.put(img, body, cols[2])
    E.put(img, body & litside, cols[3])
    E.put(img, body & ~litside & (np.abs(v) > 0.55) & (th < 0.6), cols[1])
    E.put(img, body & (np.abs(dv) < 0.55) & (np.abs(u) < 0.85), cols[1])       # midrib
    for s in (-0.45, -0.1, 0.25, 0.55):                                          # side veins
        for q in np.arange(0.6, 1.0, 0.5):
            for sg in (-1, 1):
                t = np.arange(0.0, 1.0, 0.25)
                for tt in t:
                    uu = s + tt * 0.28
                    vv = sg * tt * Wd * 0.32
                    x = cx + (uu * L / 2) * ca - vv * sa
                    y = cy + (uu * L / 2) * sa + vv * ca
                    xi, yi = int(math.floor(x)), int(math.floor(y))
                    if 0 <= yi < H and 0 <= xi < W and body[yi, xi] and tt > 0.2 and th[yi, xi] < 0.75:
                        img[yi, xi, :3] = E.rgb(cols[1])
    E.put(img, stalk, cols[1])
    return img


def make_twig(W, H, segs, seed):
    """segs: list of (polyline pts, r0, r1)."""
    m = np.zeros((H, W), bool)
    for pts, r0, r1 in segs:
        L = sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1))
        s = 0.0
        for i in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            n = int(math.hypot(x1 - x0, y1 - y0) * 2) + 1
            for k in range(n):
                t = k / n
                d = s + t * math.hypot(x1 - x0, y1 - y0)
                _disc(m, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r0 + (r1 - r0) * d / L, wrap=False)
            s += math.hypot(x1 - x0, y1 - y0)
    img = E.blank(H, W)
    rn = np.random.default_rng(seed)
    litedge = m & ~R.shift(m, 1, 1, False)
    darkedge = m & ~R.shift(m, -1, -1, False)
    E.put(img, R.shift(m, 1, 1, False) & ~m, WATER[0])
    E.put(img, m, WOOD[1])
    E.put(img, darkedge, WOOD[0])
    E.put(img, litedge, WOOD[2])
    E.put(img, litedge & (rn.random((H, W)) < 0.3), WOOD[3])
    E.put(img, m & (rn.random((H, W)) < 0.06), WOOD[0])
    return img


def floats():
    out = {}
    out["uw_swamp_top_float_pad_0"] = make_pad(42, 42, 21, 21, 18.5, math.radians(-30), 1.3)
    out["uw_swamp_top_float_pad_1"] = make_pad(32, 32, 16, 16, 13.5, math.radians(150), 2.1,
                                               tear=(math.radians(60), 4.5), decay=True)
    out["uw_swamp_top_float_pad_2"] = make_pad(40, 38, 18, 20, 15.5, math.radians(100), 3.7,
                                               flower=(26.5, 14.5, 6.2, 0.2))
    out["uw_swamp_top_float_duck_0"] = make_duck(36, 24, 1.0)
    out["uw_swamp_top_float_duck_1"] = make_duck(24, 16, 2.0)
    out["uw_swamp_top_float_duck_2"] = make_duck(56, 30, 3.0, 0.8)
    out["uw_swamp_top_float_leaf_0"] = make_leaf(20, 16, 17, 8, math.radians(-28), LEAF_B, 1.0)
    out["uw_swamp_top_float_leaf_1"] = make_leaf(16, 14, 13, 6.5, math.radians(40), LEAF_G, 2.0)
    out["uw_swamp_top_float_twig_0"] = make_twig(
        40, 20, [([(2, 15), (12, 12), (24, 9.5), (37, 5)], 1.1, 0.6), ([(18, 10.8), (24, 15), (28, 17.5)], 0.7, 0.5),
                 ([(29, 7.8), (33, 2.5)], 0.6, 0.5)], 7)
    return out


# ============================================================================ frog lure from above (Blender render)
FROG_W, FROG_H = 24, 28
FROG_PPU = 7.5
FROG_PIV = (12.0, 19.0)            # body centre, top-down px (x on the pixel boundary: the frog is mirror-symmetric)
FROG_NOSE = 0.92                   # model units in front of the body centre (-> the line tie, 7 px under the pivot)
FROG_LIGHT = Vector((-0.5, 0.45, 0.74)).normalized()     # towards the light: the screen's upper left, mostly overhead
# right legs (x right, y BACK = screen up, z up; nose at -y), mirrored for the left: hip, knee, ankle, foot
HIND = {0: [(0.42, 0.52, -0.10), (1.00, 0.50, -0.16), (0.74, 1.00, -0.20), (0.92, 1.42, -0.22)],     # relaxed Z
        1: [(0.42, 0.52, -0.10), (1.08, 0.30, -0.16), (0.76, 0.72, -0.20), (1.00, 0.98, -0.22)],     # drawn in
        2: [(0.40, 0.55, -0.10), (0.50, 1.12, -0.16), (0.34, 1.62, -0.20), (0.38, 2.02, -0.22)],     # kicked back
        3: [(0.40, 0.55, -0.10), (0.58, 1.08, -0.16), (0.50, 1.52, -0.20), (0.62, 1.90, -0.22)]}     # hop, spread
HIND_R = [0.17, 0.13, 0.11, 0.20]
FRONT = {0: [(0.38, -0.28, -0.10), (0.76, -0.36, -0.18), (0.70, -0.70, -0.22)],
         1: [(0.38, -0.28, -0.10), (0.78, -0.30, -0.18), (0.78, -0.62, -0.22)],
         2: [(0.38, -0.28, -0.10), (0.70, -0.10, -0.18), (0.80, 0.16, -0.22)],
         3: [(0.38, -0.28, -0.10), (0.62, -0.62, -0.18), (0.52, -0.96, -0.22)]}


def _frog_scene(pose):
    """fk_items bait_frog materials on a mirror-symmetric frog built for the top view: a pear-shaped body (wide rear,
    narrower head), eye bulbs standing out of the head outline, Z-folded hind legs with webbed feet, small arms."""
    import fk_items as I
    C.reset_scene()
    keep = (C.LIGHT_DIR, C.HALF_DIR)
    C.LIGHT_DIR = FROG_LIGHT
    C.HALF_DIR = (FROG_LIGHT + Vector((0, 0, 1))).normalized()
    try:
        g = C.pattern_material("Frog", "#5ab442", "#3a7a2a", kind="noise", scale=3, thresh=0.58, shine=0.6)
    finally:
        C.LIGHT_DIR, C.HALF_DIR = keep
    objs = [I.sphere("Body", g, 0.62, (0, 0.12, 0), scale=(1.0, 1.08, 0.62)),
            I.sphere("Head", g, 0.47, (0, -0.46, 0.03), scale=(1.0, 0.98, 0.6))]
    for sx in (-1, 1):
        objs.append(I.sphere("EyeB", g, 0.2, (0.37 * sx, -0.56, 0.28)))
        objs.append(C.tube_along("LegB", [(x * sx, y, z) for x, y, z in HIND[pose]], HIND_R, g, 8))
        foot = HIND[pose][-1]
        objs.append(I.sphere("Foot", g, 0.2, (foot[0] * sx, foot[1] + 0.06, foot[2]), scale=(1.0, 1.25, 0.35)))
        objs.append(C.tube_along("LegF", [(x * sx, y, z) for x, y, z in FRONT[pose]], [0.1, 0.08, 0.1], g, 8))
    bpy.context.view_layer.update()
    if pose == 3:                                                   # lifted: a little closer to the camera
        for o in objs:
            o.matrix_world = Matrix.Scale(1.1, 4) @ o.matrix_world
    return objs


def _render_top(tag, ppu=FROG_PPU, lure="frog"):
    """Straight down at the model (x right, y back = screen up), its origin on the sprite's pivot FROG_PIV."""
    sc = bpy.context.scene
    cd = bpy.data.cameras.new("TopCam")
    cam = bpy.data.objects.new("TopCam", cd)
    C.link(cam)
    sc.camera = cam
    cd.type = "ORTHO"
    cd.ortho_scale = max(FROG_W, FROG_H) / ppu
    cd.clip_start = 0.1
    cd.clip_end = 200
    cam.location = ((FROG_W / 2 - FROG_PIV[0]) / ppu, (FROG_PIV[1] - FROG_H / 2) / ppu, 50.0)
    cam.rotation_euler = (0, 0, 0)
    sc.render.resolution_x = FROG_W
    sc.render.resolution_y = FROG_H
    raw = os.path.join(R.WORK, "%s_top_%s_raw.png" % (lure, tag))
    C.render_raw(raw)
    arr = C.pixelize(C.load_pixels(raw), outline=True)
    return arr[::-1].copy()


def _snap(img, hexes):
    pal = R.Pal(hexes)
    m = img[..., 3] > 0.5
    lab = R.oklab(img[m][:, :3].astype(np.float64))
    d = ((lab[:, None, :] - pal.lab[None, :, :]) ** 2).sum(-1)
    img[m, :3] = pal.srgb[d.argmin(1)].astype(np.float32)
    img[~m] = 0.0
    return img


def _to_px(x, y, scale=1.0):
    """Model point (x right, y back) -> top-down pixel of the frog sprite."""
    return FROG_PIV[0] + x * scale * FROG_PPU, FROG_PIV[1] - y * scale * FROG_PPU


def frog_frames():
    out = {}
    yy, xx = _grid(FROG_H, FROG_W)
    for pose in range(4):
        _frog_scene(pose)
        img = _snap(_render_top(pose), FROG_PAL)
        s = 1.1 if pose == 3 else 1.0
        # hand finish: the eyes - a black pupil on the outer half of each bulb, a 1 px glint on the lit (left) side
        for sx in (-1, 1):
            ex, ey = _to_px(0.37 * sx, -0.56, s)
            px = int(math.floor(ex + 0.4 * sx))                        # the bulb's outer column
            py = int(math.floor(ey))
            _px(img, px, py, "#101010")
            _px(img, px, py + 1, "#0c0a19")
            _px(img, px - sx, py, "#c8d0da")
        if pose == 3:                                                  # hop: its shadow on the water, lower right
            a = img[..., 3] > 0.5
            E.put(img, R.shift(a, 3, 2, False) & ~a, WATER[1])
        out["lure_frog_top_%d" % pose] = img
    return out


# ============================================================================ popper lure from above (Blender render)
# fk_items build_popper's parts, sizes and materials (pearl body with a blue back, red cupped face, metal tie, painted
# eyes, white / red feather tail), rebuilt mirror-symmetric for the top view like the frog: both eyes, the feathers
# fanned sideways, the trebles left out (they hang under the body, as the frog's back hooks are left out). Same canvas,
# light, outline and pivot as lure_frog_top_* (24x28, body pivot (12, 19), the line tie 7 px under it, nose DOWN), so
# EncounterView.Top draws it with the frog's constants, and its frames mean what the frog's mean there: 0 at rest,
# 1 / 2 the two beats of a swim stroke (1 the short one before the surge, 2 the glide), 3 the pop (0.2 s). No spray in
# the frames: the water FX are the game's fx_top_* for any lure (the V-wake, the "퐁" splash and its ring), as fk_items'
# Fx* splash drops are icon-only accents.
POP_PPU = 8.0                              # px per bait_popper unit: its 2.0-unit body is 16 px (the frog's is 13)
POP_TIE = Vector((1.03, 0.0, -0.02))       # the line tie on the cup face (build_popper frame: nose +x, z up)
POP_TIE_PX = 7.0                           # under the pivot, like the frog's nose (EncounterView.Top FrogTie)
POP_WL = 0.12                              # the waterline on the level popper: the blue back rides above it
POP_ROCK = Vector((0.15, 0.0, POP_WL))     # it rocks about this point on the waterline
POP_SINK, POP_SINK_D = 0.5, 0.5           # under water its colour goes up to POP_SINK towards the water by this depth
# per frame: pitch (deg, + = nose up), the feathers' half-spread at the tips / length / droop at the tips, scale
POP_POSE = {0: (18, 0.18, 0.62, -0.08, 1.0),   # at rest: tail-down, the cup's hollow tipped to the sky, feathers sunk
            1: (-5, 0.30, 0.56, 0.0, 1.0),     # the chug: the face digs in (nose down), the feathers flare out
            2: (4, 0.07, 0.72, 0.04, 1.0),     # the glide: planing level, the feathers streaming back together
            3: (40, 0.20, 0.60, -0.10, 1.1)}   # the pop: the nose kicked up out of the water (lifted: its shadow)
# bait_popper / lure_popper_0/1 colours (blue back, pearl, red face, cup, metal, eye, feathers, outlines) + the tones
# its blue / white / red take under the murky water
POP_PAL = ["#5dbaff", "#3a78d8", "#234da6", "#182751", "#111b43",
           "#f4f2ea", "#f4f0e8", "#a19fb4", "#56557a", "#4c4855", "#353247",
           "#e0343a", "#e03a3a", "#931f2a", "#4e0c1a", "#1d081c",
           "#5a1424", "#461425", "#380919", "#1a020d",
           "#c8d0da", "#8388a7", "#403f51", "#fff070", "#101010", "#0c0a19",
           "#3762a1", "#b6b5ad", "#a83f34", "#70362b", "#49211e"]


def _sink(mat):
    """Below the waterline (world z < POP_WL) the toon colour fades towards the murky water with the depth."""
    nt = mat.node_tree
    em = next(n for n in nt.nodes if n.type == "EMISSION")
    src = em.inputs["Color"].links[0].from_socket
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Position"], sep.inputs[0])
    d = nt.nodes.new("ShaderNodeMath")
    d.operation = "SUBTRACT"
    d.inputs[0].default_value = POP_WL
    nt.links.new(sep.outputs[2], d.inputs[1])
    k = nt.nodes.new("ShaderNodeMath")
    k.operation = "MULTIPLY"
    k.use_clamp = True
    k.inputs[1].default_value = 1.0 / POP_SINK_D
    nt.links.new(d.outputs[0], k.inputs[0])
    f = nt.nodes.new("ShaderNodeMath")
    f.operation = "MULTIPLY"
    f.inputs[1].default_value = POP_SINK
    nt.links.new(k.outputs[0], f.inputs[0])
    mx = nt.nodes.new("ShaderNodeMix")
    mx.data_type = "RGBA"
    fin = [s for s in mx.inputs if s.name == "Factor" and s.type == "VALUE"][0]
    cols = [s for s in mx.inputs if s.type == "RGBA"]
    nt.links.new(f.outputs[0], fin)
    nt.links.new(src, cols[0])
    cols[1].default_value = C.lin(WATER[3])
    nt.links.new([s for s in mx.outputs if s.type == "RGBA"][0], em.inputs["Color"])


def _popper_scene(pose):
    import fk_items as I
    C.reset_scene()
    pitch, spread, flen, droop, scale = POP_POSE[pose]
    keep = (C.LIGHT_DIR, C.HALF_DIR)
    C.LIGHT_DIR = FROG_LIGHT
    C.HALF_DIR = (FROG_LIGHT + Vector((0, 0, 1))).normalized()
    try:
        pearl = I.split_material("Pearl", "#3a78d8", "#f4f2ea", z=0.14, shine=1.0)
        face = I.M("Face", "#e0343a", shine=0.6)
        cup = I.M("CupIn", "#5a1424")
        metal = I.M("Metal", "#c8d0da", shine=1.0)
        iris = I.M("EyeI", "#fff070", flat=True)
        pupil = I.M("EyeP", "#101010", flat=True)
        featw = I.M("FeatW", "#f4f0e8")
        featr = I.M("FeatR", "#e03a3a")
    finally:
        C.LIGHT_DIR, C.HALF_DIR = keep
    for m in (pearl, face, cup, metal, iris, pupil, featw, featr):
        _sink(m)
    objs = [C.tube_along("Body", [(-1.02, 0, 0.02), (-0.7, 0, 0.02), (0.0, 0, 0), (0.6, 0, 0), (0.9, 0, 0)],
                         [0.1, 0.24, 0.4, 0.45, 0.45], pearl, 16),
            C.tube_along("Rim", [(0.86, 0, 0), (0.98, 0, 0)], 0.45, face, 16),
            C.tube_along("Cup", [(0.9, 0, 0), (0.995, 0, 0)], 0.3, cup, 16),
            I.torus("Tie", metal, 0.08, 0.035, tuple(POP_TIE), rot=(0, math.radians(90), 0))]
    # the painted eyes on the head's shoulders, facing out and up (flat discs like fk_items eye_dot)
    phi = math.radians(45)
    for sy in (-1, 1):
        n = Vector((0.0, sy * math.sin(phi), math.cos(phi)))
        rot = Matrix.Rotation(math.atan2(math.cos(phi), sy * math.sin(phi)), 4, "X") @ Matrix.Diagonal((1, 0.4, 1, 1))
        p = Vector((0.62, 0.0, 0.0)) + n * 0.43
        e = I.sphere("Eye", iris, 0.14, (0, 0, 0))
        e.matrix_world = Matrix.Translation(p) @ rot
        q = I.sphere("Pupil", pupil, 0.14 * 0.55, (0, 0, 0))
        q.matrix_world = Matrix.Translation(p + n * 0.05 + Vector((0.03, 0, 0))) @ rot
        objs += [e, q]
    # the feather tail fanned sideways: white outside, the red strand on top in the middle (side: fan position -1..1,
    # radius at the root / tip, length factor, raised by)
    for u, mat, r0, r1, ln, dz in ((-1.0, featw, 0.08, 0.03, 1.0, 0.0), (1.0, featw, 0.08, 0.03, 1.0, 0.0),
                                   (-0.45, featw, 0.075, 0.03, 0.95, 0.01), (0.45, featw, 0.075, 0.03, 0.95, 0.01),
                                   (0.0, featr, 0.1, 0.05, 0.85, 0.1)):
        pts, rr = [], []
        for i in range(7):
            t = i / 6
            pts.append((-1.0 - flen * ln * t, u * (0.04 + spread * t), 0.02 + dz + droop * t * t))
            rr.append(r0 + (r1 - r0) * t)
        objs.append(C.tube_along("Feather", pts, rr, mat, 6))
    bpy.context.view_layer.update()
    # the pose: rocked about the waterline point, (3) lifted closer about the tie; then nose DOWN on the screen with
    # the tie POP_TIE_PX under the pivot (the model origin = the sprite's pivot)
    M = Matrix.Translation(POP_ROCK) @ Matrix.Rotation(math.radians(-pitch), 4, "Y") @ Matrix.Translation(-POP_ROCK)
    tie = M @ POP_TIE
    if scale != 1.0:
        M = Matrix.Translation(tie) @ Matrix.Scale(scale, 4) @ Matrix.Translation(-tie) @ M
    Rz = Matrix.Rotation(math.radians(-90), 4, "Z")
    t2 = Rz @ tie
    M = Matrix.Translation(Vector((-t2.x, -POP_TIE_PX / POP_PPU - t2.y, 0.0))) @ Rz @ M
    for o in objs:
        o.matrix_world = M @ o.matrix_world
    bpy.context.view_layer.update()
    return objs


def popper_frames():
    out = {}
    for pose in range(4):
        _popper_scene(pose)
        img = _render_top(pose, POP_PPU, "popper")
        if POP_PAL is not None:
            img = _snap(img, POP_PAL)
        if pose == 3:                                                  # popped up: its shadow on the water, lower right
            a = img[..., 3] > 0.5
            E.put(img, R.shift(a, 3, 2, False) & ~a, WATER[1])
        out["lure_popper_top_%d" % pose] = img
    return out


# ============================================================================ FX (top view)
def make_ring(r, fade, inner=None):
    n = 32
    c = n / 2
    img = E.blank(n, n)
    th = R.bayer(n, n)

    def draw(rr, f, bright):
        for x, y, a in _circle(c, c, rr + 1.0):               # trough just outside the crest (lower right only)
            if _lit(a) < 0.1 and 0 <= x < n and 0 <= y < n and th[y, x] < 0.8 - f:
                _px(img, x, y, WATER[1])
        for x, y, a in _circle(c, c, rr):
            if not (0 <= x < n and 0 <= y < n) or th[y, x] >= 1.0 - f:
                continue
            L = _lit(a)
            col = SHEEN[2] if (bright and L > -0.2) else (SHEEN[1] if (L > -0.5 or bright) else SHEEN[0])
            if f > 0.45:
                col = SHEEN[1] if L > 0.2 else SHEEN[0]
            _px(img, x, y, col)
    draw(r, fade, fade < 0.2)
    if inner is not None:
        draw(inner[0], inner[1], False)
    return img


WAKE_W, WAKE_H, WAKE_PIV = 40, 48, (20.0, 32.0)                 # pivot = the frog's pivot (body centre)


def make_wake(phase):
    """V-wake of the frog swimming nose-down: the bow wave hugging the head, two diverging crest arms (a light crest
    with a dark trough inside it, brighter feather ticks travelling back along them with the phase), transverse
    crest arcs between the arms, and the kick churn behind the legs. phase 0..1 (frames 0, 1/3, 2/3) loops."""
    W, H = WAKE_W, WAKE_H
    img = E.blank(H, W)
    th = R.bayer(H, W)
    ax, ay = WAKE_PIV[0], WAKE_PIV[1] + FROG_NOSE * FROG_PPU      # apex = the nose
    theta = math.radians(20)
    L = 44.0

    def put_if_empty(x, y, c):
        xi, yi = int(math.floor(x)), int(math.floor(y))
        if 0 <= xi < W and 0 <= yi < H and img[yi, xi, 3] == 0:
            _px(img, x, y, c)
    # bow wave: two short bright arcs from just ahead of the nose, hugging the head sides into the arms
    for side in (-1, 1):
        for t in np.arange(0.0, 6.0, 0.5):
            _px(img, ax + side * (0.6 + 3.4 * math.sqrt(t / 6.0)), ay + 1.2 - t, SHEEN[2] if t < 3.0 else SHEEN[1])
    for side in (-1, 1):
        dxa, dya = side * math.sin(theta), -math.cos(theta)
        # the arm crest: a continuous line fading out, the trough 1 px inside it
        for t in np.arange(5.0, L, 0.5):
            s = t / L
            x, y = ax + side * 3.2 + t * dxa, ay - 1.0 + t * dya
            xi, yi = int(math.floor(x)), int(math.floor(y))
            if not (0 <= xi < W and 0 <= yi < H) or th[yi, xi] > 1.0 - max(0.0, s - 0.4) * 1.5:
                continue
            _px(img, x, y, SHEEN[2] if s < 0.25 else (SHEEN[1] if s < 0.6 else SHEEN[0]))
            put_if_empty(x - side, y, WATER[1])
        # feather ticks: brighter 2-3 px strokes turned outward, travelling back along the arm
        N = 6
        for kk in range(-1, N + 1):
            s = (kk + phase) / N
            if s < 0.12 or s > 0.92:
                continue
            t0 = s * L
            px0, py0 = ax + side * 3.2 + t0 * dxa, ay - 1.0 + t0 * dya
            fa = math.atan2(dya, dxa) + side * math.radians(55)
            fl = 3.2 * (1 - 0.5 * s)
            for u in np.arange(0.0, fl, 0.5):
                x, y = px0 + math.cos(fa) * u, py0 + math.sin(fa) * u
                xi, yi = int(math.floor(x)), int(math.floor(y))
                if 0 <= xi < W and 0 <= yi < H and th[yi, xi] < 1.2 - 0.8 * s:
                    _px(img, x, y, SPRAY[0] if s < 0.35 else (SHEEN[2] if s < 0.65 else SHEEN[1]))
                    put_if_empty(x, y + 1, WATER[1])
    # transverse crests: shallow arcs between the arms (concave towards the lure), travelling back with the phase
    for kk in range(3):
        s = (kk + phase) / 3.0
        yc = ay - 12 - s * 26
        half = (ay - yc) * math.tan(theta) + 1.5
        for x in np.arange(ax - half * 0.8, ax + half * 0.8, 0.5):
            u = (x - ax) / max(1.0, half)
            y = yc + 2.2 * u * u
            xi, yi = int(math.floor(x)), int(math.floor(y))
            if 0 <= xi < W and 0 <= yi < H and th[yi, xi] < 0.75 - 0.6 * s and img[yi, xi, 3] == 0:
                _px(img, x, y, SHEEN[0])
    # churn behind the kicking legs (from the feet up), widening and fading, drifting back with the phase
    rng = random.Random(3)
    for i in range(26):
        s = ((i + phase * 3.0) % 26) / 26.0
        y = WAKE_PIV[1] - 14.0 - s * 18.0
        spread = 1.0 + s * 4.0
        x = ax + rng.uniform(-spread, spread)
        q = rng.random()
        xi, yi = int(math.floor(x)), int(math.floor(y))
        if not (0 <= yi < H and 0 <= xi < W) or th[yi, xi] > 1.1 - s:
            continue
        if q < 0.3:
            _px(img, x, y, SHEEN[1])
        elif q < 0.5:
            _px(img, x, y, SPRAY[1])
        elif q < 0.8:
            _px(img, x, y, SHEEN[0])
            put_if_empty(x + 1, y, SHEEN[0])
        else:
            put_if_empty(x, y, WATER[1])
    return img


def make_splash(f):
    n = 32
    c = n / 2
    img = E.blank(n, n)
    th = R.bayer(n, n)
    rng = random.Random(77)
    drops = [(2 * math.pi * k / 13 + rng.uniform(-0.2, 0.2), rng.uniform(0.8, 1.15), rng.random()) for k in range(13)]
    if f == 0:
        yy, xx = _grid(n, n)
        rad = np.hypot(xx - c, yy - c)
        ang = np.arctan2(yy - c, xx - c)
        lit = np.cos(ang) * LX + np.sin(ang) * LY
        crown = (np.abs(rad - 5.4) < 0.9 + 0.7 * np.sin(ang * 7 + 1.0)) & (rad > 4.2)
        hole = np.abs(rad - 3.9) < 0.6
        E.put(img, hole & (lit < 0.2), WATER[0])
        E.put(img, crown, SPRAY[1])
        E.put(img, crown & (lit > -0.1), SPRAY[2])
        E.put(img, crown & (lit > 0.5) & (th < 0.5), SPRAY[3])
        E.put(img, crown & (lit < -0.55), SPRAY[0])
        for a, sp, q in drops:                                        # spray jets shooting out of the crown
            for r in np.arange(6.5, 6.5 + 3.5 * sp, 0.5):
                _px(img, c + r * math.cos(a), c + r * math.sin(a), SPRAY[2] if r < 8 else SPRAY[1])
            if q < 0.5:
                _px(img, c + (7.5 + 3.5 * sp) * math.cos(a), c + (7.5 + 3.5 * sp) * math.sin(a), SPRAY[3])
    elif f == 1:
        for x, y, a in _circle(c, c, 5.0):
            _px(img, x, y, SHEEN[2] if _lit(a) > -0.2 else SHEEN[1])
        for a, sp, q in drops:
            r = 9.0 * sp
            x, y = c + r * math.cos(a), c + r * math.sin(a)
            _px(img, x, y, SPRAY[2])
            _px(img, x + 1, y, SPRAY[1])
            _px(img, x, y + 1, SPRAY[1])
            _px(img, x + 1, y + 1, SPRAY[0])
            if q < 0.5:
                _px(img, x, y, SPRAY[3])
            _px(img, c + (r + 3.2) * math.cos(a + 0.12), c + (r + 3.2) * math.sin(a + 0.12), SPRAY[1])
    elif f == 2:
        for x, y, a in _circle(c, c, 7.5):
            if th[y % n, x % n] < 0.9:
                _px(img, x, y, SHEEN[1] if _lit(a) > -0.3 else SHEEN[0])
        for x, y, a in _circle(c, c, 8.5):
            if _lit(a) < 0 and th[y % n, x % n] < 0.6:
                _px(img, x, y, WATER[1])
        for a, sp, q in drops:
            r = 12.0 * sp
            x, y = c + r * math.cos(a), c + r * math.sin(a)
            if q < 0.45:                                              # landed: a tiny plus
                for ddx, ddy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
                    _px(img, x + ddx, y + ddy, SHEEN[1] if (ddx, ddy) != (0, 0) else SPRAY[0])
            else:                                                     # still falling: a drop with a short trail
                _px(img, x, y, SPRAY[2])
                _px(img, x - math.cos(a) * 1.2, y - math.sin(a) * 1.2, SPRAY[0])
    else:
        for x, y, a in _circle(c, c, 10.0):
            if th[y % n, x % n] < 0.55:
                _px(img, x, y, SHEEN[0] if _lit(a) < 0.3 else SHEEN[1])
        for a, sp, q in drops:
            if q < 0.6:
                r = 13.0 * min(sp, 1.1)
                x, y = c + r * math.cos(a), c + r * math.sin(a)
                for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    _px(img, x + ddx, y + ddy, SHEEN[0])
    return img


def make_bulge(f):
    n = 48
    c = n / 2
    img = E.blank(n, n)
    th = R.bayer(n, n)
    yy, xx = _grid(n, n)
    rad = np.hypot(xx - c, yy - c)
    ang = np.arctan2(yy - c, xx - c)
    lit = np.cos(ang) * LX + np.sin(ang) * LY
    dome = [0.0, 7.0, 12.0, 17.0][f]
    # the swirl: two spiral arcs outside the dome, turning with the frames (loop 0 <-> 1 for the 흥분 hold)
    rot = [0.0, 0.55, 1.1, 1.6][f]
    r_in = max(8.0, dome + 2.0)
    r_out = min(22.0, r_in + 11.0)
    for arm in (0, 1):
        a0 = rot + arm * math.pi
        for r in np.arange(r_in, r_out, 0.35):
            a = a0 + (r - r_in) * 0.16
            x, y = c + r * math.cos(a), c + r * math.sin(a)
            xi, yi = int(math.floor(x)), int(math.floor(y))
            fade = (r - r_in) / (r_out - r_in)
            if not (0 <= xi < n and 0 <= yi < n) or th[yi, xi] > 1.0 - 0.8 * fade:
                continue
            _px(img, x, y, SHEEN[0] if (f >= 2 or _lit(a) > 0.2) else WATER[5])
            tx, ty = c + (r - 1.0) * math.cos(a), c + (r - 1.0) * math.sin(a)
            if img[int(math.floor(ty)) % n, int(math.floor(tx)) % n, 3] == 0:
                _px(img, tx, ty, WATER[1])
    if f == 0:
        return img
    # the dome stays see-through (the fish's shadow shows under it): only its sheen - a highlight crescent on the lit
    # shoulder (dash-dithered at its ends) and one inner contour ripple
    inside = rad < dome
    band = inside & (rad > dome * 0.45) & (rad < dome * 0.82)
    sheen = band & (lit > 0.35 - 0.1 * f)
    E.put(img, sheen & (th < 0.35 + 0.3 * lit), WATER[5] if f == 1 else SHEEN[0])
    if f >= 2:
        E.put(img, sheen & (lit > 0.8) & (rad < dome * 0.7), SHEEN[1] if f == 2 else SHEEN[2])
        for x, y, a in _circle(c, c, dome * 0.62):
            if _lit(a) < -0.15 and th[y % n, x % n] < 0.6:
                _px(img, x, y, WATER[5] if f == 2 else SHEEN[0])
    for x, y, a in _circle(c, c, dome):                            # crest ring
        L = _lit(a)
        if f == 1 and L < 0:
            continue
        _px(img, x, y, SHEEN[2] if (f == 3 and L > 0.1) else (SHEEN[1] if L > -0.3 else SHEEN[0]))
    for x, y, a in _circle(c, c, dome + 1.2):                      # trough ring outside it
        L = _lit(a)
        if f == 1 and L > -0.3:
            continue
        if 0 <= x < n and 0 <= y < n and img[y, x, 3] == 0 or L < -0.2:
            _px(img, x, y, WATER[0] if L < -0.2 else WATER[1])
    if f == 3:
        for x, y, a in _circle(c, c, dome + 2.3):                  # the heavy side of the trough
            if _lit(a) < -0.4:
                _px(img, x, y, WATER[0])
        for x, y, a in _circle(c, c, 21.5):                        # push ring
            if th[y % n, x % n] < 0.35:
                _px(img, x, y, SHEEN[0])
        for (bx, by) in ((-5, -3), (3, 4), (6, -6), (-2, 7)):     # bubbles surfacing on the dome
            _px(img, c + bx, c + by, SPRAY[1])
            _px(img, c + bx - 1, c + by - 1, SPRAY[3])
        rng = random.Random(12)
        for _ in range(9):                                         # duckweed pushed outward
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(dome + 3.5, dome + 6.5)
            _px(img, c + r * math.cos(a), c + r * math.sin(a), LILY[1] if rng.random() < 0.7 else LILY[2])
    return img


def make_bubble():
    img = E.blank(5, 5)
    rows = [".LLL.", "LW..D", "L...D", "L...D", ".DDD."]
    cmap = {"L": SHEEN[2], "W": SPRAY[3], "D": WATER[1]}
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch in cmap:
                _px(img, x, y, cmap[ch])
    img[2, 2, :3] = E.rgb(SHEEN[0])
    img[2, 2, 3] = 1.0
    img[2, 3, :3] = E.rgb(SHEEN[0])
    img[2, 3, 3] = 1.0
    return img


def fx_frames():
    out = {}
    for i, (r, fade, inner) in enumerate(((3.5, 0.0, None), (6.5, 0.1, None), (10.0, 0.35, (5.0, 0.5)),
                                          (13.5, 0.6, (8.5, 0.7)))):
        out["fx_top_ring_%d" % i] = make_ring(r, fade, inner)
    for i in range(3):
        out["fx_top_wake_%d" % i] = make_wake(i / 3.0)
    for i in range(4):
        out["fx_top_splash_%d" % i] = make_splash(i)
    for i in range(4):
        out["fx_top_bulge_%d" % i] = make_bulge(i)
    out["fx_top_bubble"] = make_bubble()
    return out


# ============================================================================ review: sheet + mock frame
def _shadow_mock(h, w, spine, width):
    """Placeholder arapaima shadow (top view) along a spine (list of (x, y), snout first)."""
    m_core = np.zeros((h, w), bool)
    m_soft = np.zeros((h, w), bool)
    fins = np.zeros((h, w), bool)
    n = len(spine)
    for i in range(n):
        t = i / (n - 1)
        if t < 0.05:
            hw = width * (0.55 + 0.45 * math.sqrt(t / 0.05))
        elif t < 0.25:
            hw = width
        elif t < 0.72:
            hw = width * (1.0 - 0.3 * (t - 0.25) / 0.47)
        elif t < 0.9:
            hw = width * (0.7 - 0.25 * (t - 0.72) / 0.18)
        else:
            hw = width * (0.45 + 0.3 * math.sin((t - 0.9) / 0.1 * math.pi * 0.8))
        x, y = spine[i]
        _disc(m_core, x, y, max(0.8, hw - 1.5), wrap=False)
        _disc(m_soft, x, y, hw + 1.0, wrap=False)
        if 0.18 < t < 0.24 or 0.74 < t < 0.88:                       # pectorals / the paddle's fin edges
            i2 = min(n - 1, i + 1)
            dx, dy = spine[i2][0] - x, spine[i2][1] - y
            L = math.hypot(dx, dy) or 1
            nx, ny = -dy / L, dx / L
            ext = 0.55 * width if t < 0.3 else 0.35 * width
            for sg in (-1, 1):
                _disc(fins, x + nx * sg * (hw + ext * 0.5), y + ny * sg * (hw + ext * 0.5), ext * 0.5, wrap=False)
    return m_core, m_soft & ~m_core, fins & ~m_core


def _nine_slice(fr, W, H):
    def mp(i, N):
        return i if i < 6 else (23 - (N - 1 - i) if i >= N - 6 else 6 + (i - 6) % 12)
    ys = np.array([mp(i, H) for i in range(H)])
    xs = np.array([mp(i, W) for i in range(W)])
    return fr[ys[:, None], xs[None, :]].copy()


def make_mock(bg, mid, sp, frame_img):
    Wn, Hn = 288, 136
    ox, oy = 176, 132
    win = np.roll(np.roll(bg, -oy, 0), -ox, 1)[:Hn, :Wn].copy()
    th = R.bayer(Hn, Wn)
    # the arapaima shadow (under the surface): its head turned up at the frog from below-left, the long body curving
    # away out of the window (호기심 circling); the head stays clear of the frog so both read
    fx, fy = 150, 58
    spine = []
    for k in range(160):
        t = k / 159
        spine.append((fx - 26 - 210 * t, fy + 30 + 34 * t - 44 * t * (1 - t)))
    core, soft, fins = _shadow_mock(Hn, Wn, spine, 12.5)
    E.put(win, soft & (th < 0.5), "#1c2416")
    E.put(win, fins & (th < 0.5), "#1c2416")
    E.put(win, core, "#141c10")
    E.put(win, core & (th < 0.25), "#1c2416")
    ftail = fins & (np.arange(Wn)[None, :] < spine[80][0]) & (th < 0.25)
    E.put(win, ftail, "#3a2418")                                     # the red paddle edge, faint
    R.over(win, mid, -ox % 768 - 768, -30)
    R.over(win, mid, -ox % 768, -30)
    # floats
    for name, x, y in (("uw_swamp_top_float_pad_0", 14, 70), ("uw_swamp_top_float_pad_2", 222, 8),
                       ("uw_swamp_top_float_pad_1", 244, 88), ("uw_swamp_top_float_duck_2", 60, 14),
                       ("uw_swamp_top_float_duck_0", 196, 104), ("uw_swamp_top_float_duck_1", 108, 110),
                       ("uw_swamp_top_float_leaf_0", 100, 20), ("uw_swamp_top_float_leaf_1", 190, 58),
                       ("uw_swamp_top_float_twig_0", 30, 112)):
        R.over(win, sp[name], x, y)
    # bubbles over the fish's head, an old ring fading behind the frog
    hx, hy = spine[0]
    for (bx, by) in ((-4, -3), (3, 2), (-1, 5)):
        R.over(win, sp["fx_top_bubble"], int(hx + bx) - 2, int(hy + by) - 2)
    R.over(win, sp["fx_top_ring_3"], fx - 16, fy - 16 - 14)
    # the line from the nose down-left to the angler, then the wake and the frog (kick frame)
    R.line(win, fx, fy + 7, fx - 44, Hn, E.rgb(PROFILE["line"]))
    R.over(win, sp["fx_top_wake_1"], fx - int(WAKE_PIV[0]), fy - int(WAKE_PIV[1]))
    R.over(win, sp["lure_frog_top_2"], fx - int(FROG_PIV[0]), fy - int(FROG_PIV[1]))
    canvas = np.zeros((Hn + 6, Wn + 6, 4), np.float32)
    canvas[..., 3] = 1.0
    R.over(canvas, win, 3, 3)
    R.over(canvas, _nine_slice(frame_img, Wn + 6, Hn + 6), 0, 0)
    return R.upscale(canvas, 4)


def _swamp_frame():
    spec = importlib.util.spec_from_file_location("encset_swamp_for_top", os.path.join(HERE, "swamp.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.frame()


def sprites():
    out = {}
    out.update(floats())
    out.update(frog_frames())
    out.update(popper_frames())
    out.update(fx_frames())
    return out


def _emit(bg, mid):
    """Write the top-view sprites next to the runner's layers (+ install), the review sheet and the mock."""
    sp = sprites()
    out_dir = os.path.join(R.OUT, "encounter")
    inst = os.path.join(C.SPRITES, "Encounter")
    for k, v in sp.items():
        R.save_png(v, os.path.join(out_dir, k + ".png"))
        print("ENC", k, v.shape[1], "x", v.shape[0], "colours", R.count_colours(v))
    if "--install" in sys.argv:
        os.makedirs(inst, exist_ok=True)
        for k in sp:
            shutil.copy2(os.path.join(out_dir, k + ".png"), os.path.join(inst, k + ".png"))
        print("ENC installed", len(sp), "top-view sprites ->", inst)
    rows = [[sp[k] for k in sp if k.startswith("uw_swamp_top_float")],
            [sp[k] for k in sp if k.startswith("lure_frog_top") or k.startswith("lure_popper_top")] + [sp["fx_top_bubble"]],
            [sp[k] for k in sp if k.startswith("fx_top_ring") or k.startswith("fx_top_splash")],
            [sp[k] for k in sp if k.startswith("fx_top_wake") or k.startswith("fx_top_bulge")]]
    sheet = R.flow_sheet(rows, tuple(E.rgb(WATER[3])), scale=4, pad=12)
    os.makedirs(REVIEW_DIR, exist_ok=True)
    R.save_png(sheet, os.path.join(REVIEW_DIR, "sprites.png"))
    R.save_png(make_mock(bg, mid, sp, _swamp_frame()), os.path.join(REVIEW_DIR, "mock.png"))
    print("ENC review ->", REVIEW_DIR)


# ============================================================================ the set (runner API)
def layers():
    bg = make_bg()
    mid = make_mid()
    fore = make_fore()
    _emit(bg, mid)
    return {"uw_swamp_top_bg": bg, "uw_swamp_top_mid": mid, "uw_swamp_top_fore": fore}


def frame():
    """enc_frame_swamp_top: the swamp frame unchanged (the window keeps enc_frame_swamp at runtime)."""
    return _swamp_frame()
