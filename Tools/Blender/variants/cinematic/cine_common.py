"""
FishingKing - "cinematic" art-style variant: shared helpers.

Style: golden-hour, light-driven. Low sun ahead-right of the angler -> everything near the camera is
backlit (dark body, warm rim), the far scenery dissolves into warm haze (aerial perspective), the water
goes from dark teal (near, little Fresnel) to a bright amber sheen (far, strong Fresnel) with a glitter
path under the sun. Colour: teal-orange split toning, restrained saturation, per-image k-means palette
with 2-colour ordered dithering on gradients.

Nothing in here writes into Assets/ - every output goes to Tools/Blender/_tmp/variants/cinematic.
"""
import sys
import os
import math
import json
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
BL = os.path.abspath(os.path.join(HERE, "..", ".."))  # Tools/Blender
if BL not in sys.path:
    sys.path.insert(0, BL)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

OUT = os.path.join(BL, "_tmp", "variants", "cinematic")
RAW = os.path.join(OUT, "raw")
os.makedirs(RAW, exist_ok=True)

W, H = P.W, P.H
CROP = (80, 65, 560, 335)  # x0, y0 (top-down), x1, y1 : what the game shows


# ============================================================================ colour helpers
def hexf(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


def to_lin(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(np.asarray(c, np.float32), 0, None)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def lin4(h, a=1.0):
    """hex -> linear RGBA tuple for shader sockets."""
    r, g, b = to_lin(hexf(h))
    return (float(r), float(g), float(b), a)


def luma(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def oklab(rgb):
    l = to_lin(np.clip(rgb, 0, 1))
    m1 = np.array([[0.4122214708, 0.5363325363, 0.0514459929],
                   [0.2119034982, 0.6806995451, 0.1073969566],
                   [0.0883024619, 0.2817188376, 0.6299787005]], np.float32)
    m2 = np.array([[0.2104542553, 0.7936177850, -0.0040720468],
                   [1.9779984951, -2.4285922050, 0.4505937099],
                   [0.0259040371, 0.7827717662, -0.8086757660]], np.float32)
    lms = np.cbrt(l @ m1.T)
    return lms @ m2.T


def oklab_to_rgb(lab):
    L, a, b = lab[..., 0], lab[..., 1], lab[..., 2]
    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b
    l, m, s = l_ ** 3, m_ ** 3, s_ ** 3
    r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
    g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
    bb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    return np.clip(to_srgb(np.stack([r, g, bb], -1)), 0, 1).astype(np.float32)


def clamp_chroma(rgb, cmax):
    """Limit OKLCh chroma (keeps lightness and hue): tames toy-saturated colours."""
    lab = oklab(np.clip(rgb[..., :3], 0, 1))
    c = np.sqrt(lab[..., 1] ** 2 + lab[..., 2] ** 2)
    # soft knee (smooth min of c and cmax): keeps the chroma ORDER inside the sprite instead of flattening
    cs = c / (1 + (c / cmax) ** 4) ** 0.25
    f = cs / np.maximum(c, 1e-6)
    lab[..., 1] *= f
    lab[..., 2] *= f
    out = rgb.copy()
    out[..., :3] = oklab_to_rgb(lab)
    return out


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    t = np.asarray(t, np.float32)
    if t.ndim >= 1 and t.shape[-1] != 3:
        t = t[..., None]
    return a + (b - a) * t


# ============================================================================ image io (top-down arrays)
def load(path):
    return np.ascontiguousarray(np.flipud(C.load_pixels(path)))


def save(arr, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    a = arr
    if a.shape[-1] == 3:
        a = np.concatenate([a, np.ones(a.shape[:2] + (1,), np.float32)], -1)
    C.save_pixels(np.ascontiguousarray(np.flipud(np.clip(a, 0, 1))), path)


def render(name):
    """Render the current scene into RAW/<name>.png and return it top-down."""
    p = os.path.join(RAW, name + ".png")
    C.render_raw(p)
    return load(p)


def upscale(a, s):
    return np.repeat(np.repeat(a, s, 0), s, 1)


def over(dst, src):
    """alpha-composite RGBA src over RGB(A) dst (straight alpha)."""
    a = src[..., 3:4]
    out = dst.copy()
    out[..., :3] = dst[..., :3] * (1 - a) + src[..., :3] * a
    if dst.shape[-1] == 4:
        out[..., 3:4] = dst[..., 3:4] + a * (1 - dst[..., 3:4])
    return out


def shift(a, dy, dx, fill=0):
    """out[y, x] = a[y - dy, x - dx]  (content moves by +dy, +dx)."""
    out = np.full_like(a, fill)
    h, w = a.shape[:2]
    ys, yd = slice(max(dy, 0), h + min(dy, 0)), slice(max(-dy, 0), h + min(-dy, 0))
    xs, xd = slice(max(dx, 0), w + min(dx, 0)), slice(max(-dx, 0), w + min(-dx, 0))
    out[ys, xs] = a[yd, xd]
    return out


# ============================================================================ grading / palette
SHADOW_TINT = np.array([-0.030, 0.012, 0.032], np.float32)   # shadows -> teal
HIGHLIGHT_TINT = np.array([0.040, 0.008, -0.045], np.float32)  # highlights -> amber


def grade(rgb, sat=0.9, split=1.0, contrast=0.08):
    """Teal-orange split toning + restrained saturation + gentle S-curve (display space)."""
    c = np.clip(rgb[..., :3], 0, 1)
    L = luma(c)[..., None]
    c = c + split * ((1 - L) ** 3 * SHADOW_TINT + L ** 2 * HIGHLIGHT_TINT)
    L2 = luma(c)[..., None]
    c = L2 + (c - L2) * sat
    c = c + contrast * (c - 0.5) * (1 - np.abs(2 * c - 1))  # S-curve that keeps the ends
    out = rgb.copy()
    out[..., :3] = np.clip(c, 0, 1)
    return out


def kmeans_palette(rgb, n, mask=None, keep=(), iters=12, seed=1, weight_pow=0.55):
    """Palette of n colours (sRGB) from an image: weighted k-means in OKLab on unique colours.
    weight = count**weight_pow so small but important colours (sun, rims, eyes) survive."""
    px = rgb[..., :3].reshape(-1, 3)
    if mask is not None:
        px = px[mask.reshape(-1)]
    q = np.round(px * 63).astype(np.int32)
    key = q[:, 0] * 4096 + q[:, 1] * 64 + q[:, 2]
    uk, inv, cnt = np.unique(key, return_inverse=True, return_counts=True)
    sums = np.zeros((len(uk), 3), np.float64)
    np.add.at(sums, inv, px)
    uc = (sums / cnt[:, None]).astype(np.float32)
    w = cnt.astype(np.float32) ** weight_pow
    lab = oklab(uc)
    k = min(n - len(keep), len(uc))
    rng = np.random.default_rng(seed)
    # k-means++ init
    cent = [lab[np.argmax(w)]]
    d2 = np.sum((lab - cent[0]) ** 2, 1)
    for _ in range(1, k):
        p = d2 * w
        p = p / p.sum() if p.sum() > 0 else None
        i = rng.choice(len(lab), p=p)
        cent.append(lab[i])
        d2 = np.minimum(d2, np.sum((lab - lab[i]) ** 2, 1))
    cent = np.array(cent, np.float32)
    for _ in range(iters):
        d = ((lab[:, None, :] - cent[None, :, :]) ** 2).sum(-1)
        a = np.argmin(d, 1)
        for j in range(len(cent)):
            m = a == j
            if m.any():
                cent[j] = (lab[m] * w[m, None]).sum(0) / w[m].sum()
    # centroids back to rgb: weighted mean of member colours in rgb (stays in gamut)
    d = ((lab[:, None, :] - cent[None, :, :]) ** 2).sum(-1)
    a = np.argmin(d, 1)
    pal = []
    for j in range(len(cent)):
        m = a == j
        if m.any():
            pal.append((uc[m] * w[m, None]).sum(0) / w[m].sum())
    for kc in keep:
        pal.append(hexf(kc) if isinstance(kc, str) else np.asarray(kc, np.float32))
    return np.array(pal, np.float32)


BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) + 0.5) / 16


ROWLINES4 = (np.array([0, 2, 1, 3], np.float32) + 0.5) / 4


def row_threshold(h, w):
    """Threshold map that is constant along each row: dithering with it gives horizontal 1 px lines
    (the classic pixel-art water band transition) instead of a Bayer checker."""
    return np.repeat(np.tile(ROWLINES4, h // 4 + 1)[:h, None], w, 1)


def quantize(rgb, pal, dither=0.0, mask=None, thr=None):
    """Map to palette. dither>0: 2-colour ordered dither between the two nearest palette colours
    (dither = width of the dithered zone around the midpoint, 0..1). thr: optional (h, w) threshold map
    (default 4x4 Bayer)."""
    img = rgb[..., :3]
    h, w = img.shape[:2]
    lab = oklab(img).reshape(-1, 3)
    pl = oklab(pal)
    d = ((lab[:, None, :] - pl[None, :, :]) ** 2).sum(-1)
    order = np.argsort(d, 1)[:, :2]
    i1, i2 = order[:, 0], order[:, 1]
    out_idx = i1.copy()
    if dither > 0:
        a, b = pl[i1], pl[i2]
        ab = b - a
        t = ((lab - a) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-9)
        t = np.clip(t, 0, 1)
        # only dither between colours that are close (gradient neighbours), not across hard edges
        close = np.sqrt((ab * ab).sum(1)) < 0.09
        tt = np.clip((t - 0.5) / max(dither, 1e-3) + 0.5, 0, 1)
        if thr is None:
            thr = np.tile(BAYER4, (h // 4 + 1, w // 4 + 1))[:h, :w]
        thr = np.asarray(thr, np.float32).reshape(-1)
        pick2 = (tt > thr) & close
        if mask is not None:
            pick2 &= mask.reshape(-1)
        out_idx = np.where(pick2, i2, i1)
    out = rgb.copy()
    out[..., :3] = pal[out_idx].reshape(h, w, 3)
    return out


# ============================================================================ sprite post-process
def alpha_mask(arr, cut=0.5):
    return arr[..., 3] > cut


def rim_light(arr, rim_col, strength=0.65, right=1.0, top=0.6, topright=0.8, mask=None, weight=None,
              left=0.0, topleft=0.0):
    """Pixel-space rim: opaque pixels whose neighbour towards the sun (right / up; left for stages lit from
    the left) is empty get pushed towards a warm light colour. weight: optional per-pixel strength map.
    Returns (arr, rimmask)."""
    a = alpha_mask(arr)
    m = a if mask is None else mask
    # neighbour to the right empty: shift the alpha left by one
    nr = ~shift(a, 0, -1, False)
    nt = ~shift(a, 1, 0, False)
    ntr = ~shift(a, 1, -1, False)
    nl = ~shift(a, 0, 1, False)
    ntl = ~shift(a, 1, 1, False)
    k = np.maximum.reduce([nr * right, nt * top, ntr * topright, nl * left, ntl * topleft]).astype(np.float32) * m
    if weight is not None:
        k = k * weight
    out = arr.copy()
    rc = np.asarray(rim_col, np.float32)
    lit = np.clip(out[..., :3] * 1.35 + rc * 0.35, 0, 1)
    out[..., :3] = mix(out[..., :3], mix(lit, rc, 0.45), k * strength)
    return out, k > 0


def dilate(m, conn=8):
    out = m.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if (dy or dx) and (conn == 8 or dy == 0 or dx == 0):
                out |= shift(m, dy, dx, False)
    return out


def erode(m, conn=8):
    """Erosion; pixels beyond the image border count as 'set' (so a mask touching the border keeps it)."""
    inv = ~m
    out = inv.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if (dy or dx) and (conn == 8 or dy == 0 or dx == 0):
                out |= shift(inv, dy, dx, False)
    return ~out


def flood(seed, allowed, conn=4):
    reg = seed & allowed
    while True:
        nxt = dilate(reg, conn) & allowed
        if nxt.sum() == reg.sum():
            return nxt
        reg = nxt


def outer_empty(opaque, min_gap=3):
    """Empty pixels connected to the image border through gaps at least min_gap px wide.
    Enclosed holes (between an arm and the torso) and slits narrower than min_gap (between the legs)
    are NOT outer, so a rim or outline never lights up inside the figure."""
    empty = ~opaque
    e = empty
    r = (min_gap - 1) // 2
    for _ in range(r):
        e = ~dilate(~e)        # erode the empty area (outside the image counts as empty)
    for _ in range(r):
        e = dilate(e) & empty
    seed = np.zeros_like(e)
    seed[0, :] = seed[-1, :] = True
    seed[:, 0] = seed[:, -1] = True
    reg = flood(seed & e, e)
    return reg


RIM_W = dict(r=1.0, tr=0.9, t=0.7, tl=0.5, l=0.3)
_DIRS = {"r": (0, 1), "tr": (-1, 1), "t": (-1, 0), "tl": (-1, -1), "l": (0, -1), "b": (1, 0),
         "br": (1, 1), "bl": (1, -1)}


def rim_outer(arr, rim_col, strength=0.6, weights=None, weight=None, mask=None, min_gap=3, outer=None):
    """Rim light on the OUTER silhouette only: an opaque pixel is lit when its neighbour in direction d
    (towards the sun) is outer empty space. weights: per direction (r, tr, t, tl, l).
    Returns (arr, k) with k the per-pixel rim strength (0..1)."""
    a = alpha_mask(arr)
    oe = outer_empty(a, min_gap) if outer is None else outer
    w = RIM_W if weights is None else weights
    k = np.zeros(a.shape, np.float32)
    for key, wt in w.items():
        dy, dx = _DIRS[key]
        nb = shift(oe, -dy, -dx, True)   # nb[y, x] = oe[y + dy, x + dx]; beyond the border = outer
        k = np.maximum(k, nb.astype(np.float32) * wt)
    k *= a
    if mask is not None:
        k *= mask
    if weight is not None:
        k = k * weight
    out = arr.copy()
    rc = np.asarray(rim_col, np.float32)
    lit = np.clip(out[..., :3] * 1.35 + rc * 0.35, 0, 1)
    out[..., :3] = mix(out[..., :3], mix(lit, rc, 0.45), k * strength)
    return out, k


def outline(arr, col=(0.05, 0.08, 0.10), mul=0.35, sides=("l", "r", "t", "b"), alpha=1.0, skip=None):
    """1px outline outside the sprite: darkened neighbour colour pulled towards a cool dark."""
    a = alpha_mask(arr)
    acc = np.zeros(arr.shape[:2] + (3,), np.float32)
    cnt = np.zeros(arr.shape[:2], np.float32)
    # side "l" = outline pixel on the left of the sprite (its right neighbour is opaque): move the
    # opaque mask by (dy, dx) and keep the empty pixels it lands on
    for s in sides:
        dy, dx = {"l": (0, -1), "r": (0, 1), "t": (-1, 0), "b": (1, 0)}[s]
        m = shift(a, dy, dx, False)
        if skip is not None:
            m &= ~shift(skip, dy, dx, False)
        c = shift(arr[..., :3], dy, dx, 0)
        acc += c * m[..., None]
        cnt += m
    edge = (cnt > 0) & (~a)
    colr = acc / np.maximum(cnt, 1)[..., None] * mul + np.asarray(col, np.float32) * (1 - mul)
    out = arr.copy()
    out[edge, :3] = colr[edge]
    out[edge, 3] = alpha
    return out


def harden(arr, cut=0.5):
    out = arr.copy()
    a = arr[..., 3] > cut
    out[..., 3] = a.astype(np.float32)
    out[~a, :3] = 0
    return out


# ============================================================================ camera rays (numpy)
def pixel_rays(w=W, h=H):
    """World-space unit view rays for every pixel (top-down rows) + centre offsets px, py (y up)."""
    i = np.arange(w, dtype=np.float32) + 0.5 - w / 2
    j = h / 2 - (np.arange(h, dtype=np.float32) + 0.5)
    px, py = np.meshgrid(i, j)
    s, c = math.sin(math.radians(P.PITCH)), math.cos(math.radians(P.PITCH))
    dx = px
    dy = P.F_PX * c + py * s
    dz = -P.F_PX * s + py * c
    n = np.sqrt(dx * dx + dy * dy + dz * dz)
    return np.stack([dx / n, dy / n, dz / n], -1), px, py


def ray_dir(px, py):
    s, c = math.sin(math.radians(P.PITCH)), math.cos(math.radians(P.PITCH))
    v = np.array([px, P.F_PX * c + py * s, -P.F_PX * s + py * c], np.float64)
    return v / np.linalg.norm(v)


def water_y_row(dist, stand):
    """Image row (top-down, float) of the waterline at forward distance dist."""
    _, py, _ = P.project((0.0, dist, 0.0), stand)
    return H / 2 - py


def screen(pt, stand):
    """World point -> (col, row) float image coords (top-down)."""
    px, py, _ = P.project(pt, stand)
    return W / 2 + px, H / 2 - py


# ============================================================================ the one sun (shared by every pass)
HORIZON_PY = P.F_PX * math.tan(math.radians(P.PITCH))
SUN_PX = (92.0, HORIZON_PY + 10.0)       # painted sun centre (px from the image centre, y up)
SUN_EL_LIGHT = 3.0                       # the light rigs use the painted sun's azimuth, lifted to 3 deg


def sun_az_el():
    d = ray_dir(*SUN_PX)
    return math.degrees(math.atan2(d[0], d[1])), SUN_EL_LIGHT


def sun_key():
    az, el = sun_az_el()
    a, e = math.radians(az), math.radians(el)
    return np.array((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)))


# ============================================================================ id passes
def id_hex(k):
    """Flat colour that encodes object-class id k (1..27) exactly through an 8-bit Standard render."""
    return "#%02x%02x%02x" % (10 + 9 * k, 128, 64)


def decode_ids(img):
    """(h, w) int array: class id per pixel, 0 where empty / untagged."""
    a = img[..., 3] > 0.5
    r = np.round(img[..., 0] * 255)
    g = np.round(img[..., 1] * 255)
    k = np.round((r - 10) / 9).astype(np.int32)
    ok = a & (np.abs(g - 128) <= 2) & (k >= 1)
    return np.where(ok, k, 0)


# ============================================================================ pixel drawing
def hash01(x):
    v = np.sin(np.asarray(x, np.float64) * 12.9898 + 4.1414) * 43758.5453
    return v - np.floor(v)


def line_pixels(p0, p1):
    """Integer pixels of a 1 px line (DDA along the major axis)."""
    x0, y0 = p0
    x1, y1 = p1
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    out = []
    for k in range(n + 1):
        t = k / n
        out.append((int(math.floor(x0 + (x1 - x0) * t + 0.5)), int(math.floor(y0 + (y1 - y0) * t + 0.5))))
    return out


def polyline_pixels(pts):
    """Pixel-perfect 1 px path through float screen points: dedupe and drop L-corner doubles."""
    path = []
    for i in range(len(pts) - 1):
        for q in line_pixels(pts[i], pts[i + 1]):
            if not path or path[-1] != q:
                path.append(q)
    if len(path) < 3:
        return path
    clean = [path[0]]
    i = 1
    while i < len(path) - 1:
        a, b, c = clean[-1], path[i], path[i + 1]
        if (a[0] == b[0] or a[1] == b[1]) and (b[0] == c[0] or b[1] == c[1]) and a[0] != c[0] and a[1] != c[1]:
            i += 1       # b is an L corner between a and c: skip it
            continue
        clean.append(b)
        i += 1
    clean.append(path[-1])
    return clean


def fill_poly(h, w, pts, ss=4):
    """Boolean mask of a polygon (float pixel coords, top-down), supersampled coverage >= 0.5."""
    pts = np.asarray(pts, np.float64)
    x0 = max(int(math.floor(pts[:, 0].min())) - 1, 0)
    x1 = min(int(math.ceil(pts[:, 0].max())) + 1, w)
    y0 = max(int(math.floor(pts[:, 1].min())) - 1, 0)
    y1 = min(int(math.ceil(pts[:, 1].max())) + 1, h)
    m = np.zeros((h, w), bool)
    if x1 <= x0 or y1 <= y0:
        return m
    ys = (np.arange((y1 - y0) * ss) + 0.5) / ss + y0
    xs = (np.arange((x1 - x0) * ss) + 0.5) / ss + x0
    X, Y = np.meshgrid(xs, ys)
    inside = np.zeros(X.shape, bool)
    n = len(pts)
    for i in range(n):
        xa, ya = pts[i]
        xb, yb = pts[(i + 1) % n]
        cond = (ya > Y) != (yb > Y)
        xint = xa + (Y - ya) * (xb - xa) / np.where(yb - ya == 0, 1e-12, yb - ya)
        inside ^= cond & (X < xint)
    cov = inside.reshape(y1 - y0, ss, x1 - x0, ss).mean((1, 3))
    m[y0:y1, x0:x1] = cov >= 0.5
    return m


# ============================================================================ Blender materials
_mats = {}


def _nb(name):
    m = bpy.data.materials.new(name)
    return m, C.NB(m)


def mflat(hexc, strength=1.0, name=None):
    key = ("flat", hexc, strength, name)
    if key not in _mats:
        m, nb = _nb(name or ("Flat" + hexc))
        nb.output_emission(nb.rgb(lin4(hexc)), strength)
        _mats[key] = m
    return _mats[key]


class Rig:
    """Light rig for the cinematic shader (world-space directions TOWARDS the light)."""

    def __init__(self, key, key_col, fill_col, bounce_col, shadow_col, rim_col, view=(0, 1, 0), mid_col="#a09aa2"):
        self.key = np.array(key, np.float64) / np.linalg.norm(key)
        self.fill_dir = (0.0, -0.5, 0.85)
        self.fill_amt = 0.16
        self.key_col = key_col
        self.mid_col = mid_col
        self.fill_col = fill_col
        self.bounce_col = bounce_col
        self.shadow_col = shadow_col
        self.rim_col = rim_col
        self.view = np.array(view, np.float64) / np.linalg.norm(view)


def sun_rig(az=None, el=None):
    """Stage props: the painted sun's azimuth at ~3 deg: backlit, grazing light on horizontal surfaces,
    rims on the sun-facing (right/top) silhouette edges, dim cool sky fill."""
    saz, sel = sun_az_el()
    az = saz if az is None else az
    el = sel if el is None else el
    a, e = math.radians(az), math.radians(el)
    r = Rig(key=(math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)),
            key_col="#ffc890", fill_col="#5a6886", bounce_col="#2f6468", shadow_col="#3e4862",
            rim_col="#ffdca8", mid_col="#8e8a96")
    r.fill_amt = 0.08
    r.bands = [(0.0, "sh2"), (0.2, "sh"), (0.37, "fill"), (0.5, "mid"), (0.6, "lit"), (0.78, "hot")]
    return r


RIG = [None]
CAP_HOT = [False]   # True: the "hot" band renders as "lit" (used for the interior pass of sprites)


def mcine(hexc, spec=0.0, rim=1.0, wrap=0.0, bands=None, emit=1.0, name="Cine", rig=None, hexalt=None,
          pattern=None, flat_top=None):
    """Cinematic banded shader (emission): base * light_ramp(s) + warm_add(s).
    s = key lambert (wrapped) + sky fill (normal z) + fresnel rim towards the key side.
    pattern: optional callable(nb) -> factor socket that mixes hexc -> hexalt."""
    if RIG[0] is None:
        RIG[0] = sun_rig()
    rig = rig or RIG[0]
    key = ("cine", hexc, spec, rim, wrap, emit, id(rig), hexalt, bool(pattern), str(bands), CAP_HOT[0], name)
    if key in _mats and pattern is None:
        return _mats[key]
    m, nb = _nb(name)
    geo = nb.node("ShaderNodeNewGeometry")
    n = geo.outputs["Normal"]
    kd = nb.vmath("DOT_PRODUCT", n, tuple(rig.key))
    kd = nb.math("DIVIDE", nb.math("ADD", kd, wrap), 1 + wrap)
    kd = nb.math("MAXIMUM", kd, 0.0)
    nz = nb.sep(n)[2]
    s = nb.math("MULTIPLY_ADD", kd, 0.62, nb.math("MULTIPLY_ADD", nz, 0.08, 0.24))
    # soft cool sky fill from above/behind the camera: gives the shadow side some volume
    fd = np.array(rig.fill_dir, np.float64)
    fd = fd / np.linalg.norm(fd)
    fl = nb.math("MAXIMUM", nb.vmath("DOT_PRODUCT", n, tuple(fd)), 0.0)
    s = nb.math("MULTIPLY_ADD", fl, rig.fill_amt, s)
    # fresnel-ish rim on the key side: (1 - |n.I|)^2 * max(n.key_screen, 0)
    if rim > 0:
        ndv = nb.math("ABSOLUTE", nb.vmath("DOT_PRODUCT", n, geo.outputs["Incoming"]))
        fr = nb.math("POWER", nb.math("SUBTRACT", 1.0, ndv), 2.0)
        ks = rig.key.copy()
        ks[1] = 0.0
        ks = ks / max(np.linalg.norm(ks), 1e-6)
        side = nb.math("MAXIMUM", nb.vmath("DOT_PRODUCT", n, tuple(ks)), 0.0)
        s = nb.math("MULTIPLY_ADD", nb.math("MULTIPLY", fr, side), 0.55 * rim, s)
    if spec > 0:
        hv = rig.key + np.array([0, -1.0, 0.25])
        hv = hv / np.linalg.norm(hv)
        sp = nb.math("GREATER_THAN", nb.vmath("DOT_PRODUCT", n, tuple(hv)), 0.93)
        s = nb.math("MULTIPLY_ADD", sp, 0.35 * spec, s)
    b = bands or getattr(rig, "bands", None) or \
        [(0.0, "sh2"), (0.18, "sh"), (0.34, "fill"), (0.45, "mid"), (0.56, "lit"), (0.75, "hot")]
    sh = to_lin(hexf(rig.shadow_col))
    fill = to_lin(hexf(rig.fill_col))
    mid = to_lin(hexf(rig.mid_col))
    kc = to_lin(hexf(rig.key_col))
    rc = to_lin(hexf(rig.rim_col))
    mults = {
        "sh2": tuple(sh) + (1,),
        "sh": tuple(fill) + (1,),
        "fill": tuple(fill * 1.3 + 0.015) + (1,),
        "mid": tuple(mid) + (1,),
        "lit": tuple(kc * 1.1) + (1,),
        "hot": tuple(kc * 1.35 + rc * 0.2) + (1,),
    }
    adds = {"sh2": (0, 0, 0, 1), "sh": (0, 0, 0, 1), "fill": (0, 0, 0, 1), "mid": (0, 0, 0, 1),
            "lit": tuple(rc * 0.02) + (1,), "hot": tuple(rc * 0.12) + (1,)}
    if CAP_HOT[0]:
        mults["hot"] = mults["lit"]
        adds["hot"] = adds["lit"]
    ramp_m = nb.ramp(s, [(p, mults[k]) for p, k in b])
    ramp_a = nb.ramp(s, [(p, adds[k]) for p, k in b])
    if pattern is not None and hexalt:
        f = pattern(nb)
        base = nb.mix(f, lin4(hexc), lin4(hexalt))
    else:
        base = nb.rgb(lin4(hexc))
    col = nb.mix(1.0, base, ramp_m, "MULTIPLY")
    col = nb.mix(1.0, col, ramp_a, "ADD")
    nb.output_emission(col, emit)
    if pattern is None:
        _mats[key] = m
    return m


def noise_pattern(scale=4.0, thresh=0.5, coord="Object", detail=2.0, stretch=(1, 1, 1)):
    def f(nb):
        tc = nb.node("ShaderNodeTexCoord")
        v = tc.outputs[coord]
        if stretch != (1, 1, 1):
            v = nb.vmath("MULTIPLY", v, stretch)
        no = nb.node("ShaderNodeTexNoise")
        nb.link(v, no.inputs["Vector"])
        no.inputs["Scale"].default_value = scale
        no.inputs["Detail"].default_value = detail
        return nb.math("GREATER_THAN", no.outputs["Fac"], thresh)
    return f


def stripe_pattern(scale=5.0, thresh=0.25, axis=0, warp=0.6, coord="Object"):
    def f(nb):
        tc = nb.node("ShaderNodeTexCoord")
        co = tc.outputs[coord]
        s = nb.sep(co)
        no = nb.node("ShaderNodeTexNoise")
        nb.link(co, no.inputs["Vector"])
        no.inputs["Scale"].default_value = scale * 0.5
        wv = nb.math("MULTIPLY_ADD", no.outputs["Fac"], warp / scale, s[axis])
        fr = nb.math("FRACT", nb.math("MULTIPLY", wv, scale))
        return nb.math("LESS_THAN", fr, thresh)
    return f


def clear_mats():
    _mats.clear()
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
