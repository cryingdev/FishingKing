"""
hybrid - KIT for the legend-encounter underwater sets (shared by hyb_encounter.py and every encounter_sets/<set>.py).
Stable API: set modules only ADD code in their own file (a helper one set needs stays in that set's module).

Image arrays are top-down RGBA float32 (h, w, 4), sRGB, alpha 0..1 (hyb_core conventions: R.save_png / R.over /
R.quantize / R.Pal / R.bayer / R.dash_threshold / R.shift / R.dilate ...).

  rgb(hex) / blank(h, w) / put(img, mask, hex, alpha)       basic pixel ops
  vnoise2 / fbm                                              smooth value noise (numpy)
  poly_mask(h, w, pts)                                       even-odd polygon fill
  quant_rows(stops, h, w)                                    banded vertical gradient quantised to its stop colours
  floor_rows(h, cam_h, fpx, r0) / ceiling_rows(...)          pseudo-perspective distance per row (floor / surface)
  make_frame(F, corners)                                     24x24 9-slice window frame (6 px border)
Shared sprites (one copy for every set, tinted at runtime): make_ray, make_halo, make_eyeshine, make_silt,
make_bubbles, make_waterline; lure_frames(keys, tweaks) renders lure_<key>_0/1 (16x8) from fk_items.build_bait.
"""
import os
import math
import random
import numpy as np
import bpy
from mathutils import Matrix
import hyb_core as R
import fk_common as C


# ============================================================================ basic pixel ops
def rgb(h):
    return R.hexrgb(h).astype(np.float32)


def blank(h, w):
    return np.zeros((h, w, 4), np.float32)


def put(img, mask, hexc, alpha=1.0):
    img[mask, :3] = rgb(hexc)
    img[mask, 3] = alpha


def vnoise2(x, y, seed=0.0):
    """Smooth 2-D value noise (numpy, 0..1)."""
    xi, yi = np.floor(x), np.floor(y)
    fx, fy = x - xi, y - yi
    ux, uy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)

    def h(a, b):
        return R._hash(a * 57.0 + b * 131.0, seed)
    a, b, c, d = h(xi, yi), h(xi + 1, yi), h(xi, yi + 1), h(xi + 1, yi + 1)
    return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy


def fbm(x, y, seed=0.0, oct=3):
    s, amp, tot = 0.0, 1.0, 0.0
    for o in range(oct):
        s = s + amp * vnoise2(x * 2 ** o, y * 2 ** o, seed + o * 7.1)
        tot += amp
        amp *= 0.5
    return s / tot


def poly_mask(h, w, pts):
    """Even-odd fill of a polygon (pixel centres), pts in (x, y) pixels, y down."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    m = np.zeros((h, w), bool)
    n = len(pts)
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        if y0 == y1:
            continue
        cond = (yy >= min(y0, y1)) & (yy < max(y0, y1))
        xc = x0 + (yy - y0) * (x1 - x0) / (y1 - y0)
        m ^= cond & (xx < xc)
    return m


def quant_rows(stops, h, w, ox=0, oy=0):
    """Vertical gradient through (row, hex) stops in linear light, quantised to the stop colours (Bayer)."""
    rows = np.arange(h) + 0.5
    lin = np.zeros((h, 3))
    rs = [r for r, _ in stops]
    cs = [R.s2l(R.hexrgb(c)) for _, c in stops]
    for i, r in enumerate(rows):
        if r <= rs[0]:
            lin[i] = cs[0]
        elif r >= rs[-1]:
            lin[i] = cs[-1]
        else:
            k = max(j for j in range(len(rs)) if rs[j] <= r)
            t = (r - rs[k]) / (rs[k + 1] - rs[k])
            # flat bands with a narrow transition (the middle 40 % of each span): the hybrid "banded" sky
            t = min(1.0, max(0.0, (t - 0.3) / 0.4))
            lin[i] = cs[k] * (1 - t) + cs[k + 1] * t
    img = np.repeat(R.l2s(lin)[:, None, :], w, 1)
    pal = R.Pal([c for _, c in stops])
    idx, _ = R.quantize(img, np.ones((h, w), bool), pal, dither=True, ox=ox, oy=oy)
    return idx, pal


def floor_rows(h, cam_h, fpx=177.0, r0=1.6):
    """Distance (m) to the floor point seen by each row of a floor band whose top row is the horizon: a camera
    cam_h m above a flat floor, focal fpx px (the encounter's 177 px), rows offset r0 px below the horizon."""
    rows = np.arange(h)[:, None] + 0.5
    return cam_h * fpx / (rows + r0)


def ceiling_rows(h, cam_d, fpx=177.0, r0=1.6):
    """The same for the underside of the water surface seen from cam_d m below it: the band's BOTTOM row is the
    horizon (far), its top row the nearest surface (flip of floor_rows)."""
    return floor_rows(h, cam_d, fpx, r0)[::-1]


# ============================================================================ window frame (24x24 9-slice, 6 px border)
STUD = ["..s..", ".sHs.", "sHBBs", ".BBd.", "..d.."]


def make_frame(F, corners, stud=STUD):
    """F: ink (1 px outer), dark / mid / lit (the 2 px band: lit top row, mid top / left, dark bottom / right),
    line (the 1 px inner line at px 3, sits 3 px outside the crop). corners: 3-colour palettes [shadow, body,
    highlight] of the corner studs, in the order top-left, top-right, bottom-left, bottom-right."""
    n, b = 24, 6
    img = blank(n, n)
    for i in range(n):
        for j in range(n):
            d = min(i, j, n - 1 - i, n - 1 - j)             # distance from the outer edge
            if d == 0:
                c = F["ink"]
            elif d in (1, 2):
                top = i == d and j >= d and j <= n - 1 - d      # top edge row of this ring
                left = j == d and i >= d and i <= n - 1 - d
                bottom = i == n - 1 - d
                c = F["lit"] if (top and d == 1) else (F["mid"] if (top or left) else F["dark"])
                if bottom or (j == n - 1 - d):
                    c = F["dark"]
            elif d == 3:
                c = F["line"]
            else:
                continue
            img[i, j, :3] = rgb(c)
            img[i, j, 3] = 1.0
    for (ci, cj), pal in zip(((0, 0), (0, n - 5), (n - 5, 0), (n - 5, n - 5)), corners):
        for di, row in enumerate(stud):
            for dj, ch in enumerate(row):
                if ch == ".":
                    continue
                c = {"s": pal[0], "d": pal[0], "B": pal[1], "H": pal[2]}[ch]
                img[ci + di, cj + dj, :3] = rgb(c)
                img[ci + di, cj + dj, 3] = 1.0
    return img


# ============================================================================ shared sprites (tinted at runtime)
PUFF = ["#35606a", "#4f7e84", "#7aa8a4"]                                          # silt puffs (tinted per set)
BUB = ["#7ec8d4", "#e0ffff"]


def make_ray():
    """uw_ray 64x256: white god-ray slanting down-left (tinted + alpha in code), 2 alpha levels + motes."""
    h, w = 256, 64
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5
    t = yy / h
    xc = 45 - 24 * t                                 # slants down-left
    half = 6.5 + 15 * t
    d = np.abs(xx - xc) / half
    fade = np.clip(1.25 - 1.35 * t, 0, 1)            # fades out towards the bottom
    th = R.bayer(h, w)
    core = (d < 0.55) & (fade > th * 0.9)
    edge = (d < 1.0) & ~core & (fade * 0.8 > th)
    img = blank(h, w)
    put(img, edge, "#ffffff", 0.5)
    put(img, core, "#ffffff", 1.0)
    rng = np.random.default_rng(3)
    motes = (d < 0.9) & (rng.random((h, w)) < 0.01) & (t < 0.75)
    put(img, motes, "#ffffff", 1.0)
    return img


def make_halo():
    n = 24
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64) + 0.5
    r = np.hypot(xx - n / 2, yy - n / 2)
    a = np.clip(1.0 - r / 11.8, 0, 1) ** 1.6
    th = R.bayer(n, n)
    levels = [0.0, 0.12, 0.28, 0.5, 0.8]
    q = np.zeros((n, n))
    for i in range(1, len(levels)):
        lo, hi = levels[i - 1], levels[i]
        span = (a - lo) / (hi - lo)
        q = np.where((a >= lo) & (a < hi), np.where(span > th, hi, lo), q)
    q = np.where(a >= levels[-1], levels[-1] + 0.15, q)
    img = blank(n, n)
    img[..., :3] = 1.0
    img[..., 3] = np.clip(q, 0, 1)
    return img


def make_eyeshine():
    img = blank(7, 7)
    core = [(3, 3), (2, 3), (4, 3), (3, 2), (3, 4)]
    ring = [(2, 2), (4, 2), (2, 4), (4, 4), (1, 3), (5, 3), (3, 1), (3, 5)]
    tips = [(0, 3), (6, 3), (3, 0), (3, 6)]
    img[..., :3] = 1.0
    for x, y in tips:
        img[y, x, 3] = 0.2
    for x, y in ring:
        img[y, x, 3] = 0.4
    for x, y in core:
        img[y, x, 3] = 1.0
    return img


def make_silt():
    frames = []
    rng = np.random.default_rng(12)
    for f in range(4):
        n = 8
        yy, xx = np.mgrid[0:n, 0:n].astype(np.float64) + 0.5
        rx, ry = 1.6 + f * 0.8, 1.0 + f * 0.55
        cy = 5.6 - f * 0.55
        e = ((xx - 4) / rx) ** 2 + ((yy - cy) / ry) ** 2
        th = R.bayer(n, n, f, f)
        keep = 1.0 - f * 0.22
        m = (e < 1.0) & (th < keep) & (rng.random((n, n)) < 0.92 - f * 0.12)
        img = blank(n, n)
        put(img, m, PUFF[0])
        put(img, m & (e < 0.45) & (yy < cy), PUFF[1])
        put(img, m & (e < 0.2) & (f < 2), PUFF[2])
        frames.append(img)
    return frames


def make_bubbles():
    s = blank(3, 3)
    for x, y in ((1, 0), (0, 1), (2, 1), (1, 2)):
        put(s, (np.arange(3)[:, None] == y) & (np.arange(3)[None, :] == x), BUB[0])
    s[0, 1, :3] = rgb(BUB[1])
    m = blank(5, 5)
    ring = [(1, 0), (2, 0), (3, 0), (0, 1), (4, 1), (0, 2), (4, 2), (0, 3), (4, 3), (1, 4), (2, 4), (3, 4)]
    for x, y in ring:
        m[y, x, :3] = rgb(BUB[0])
        m[y, x, 3] = 1.0
    m[1, 1, :3] = rgb(BUB[1])
    m[1, 1, 3] = 1.0
    for y in range(1, 4):
        for x in range(1, 4):
            if m[y, x, 3] == 0:
                m[y, x, :3] = rgb(BUB[0])
                m[y, x, 3] = 0.15
    return s, m


def make_waterline():
    w, h = 32, 4
    img = blank(h, w)
    img[3, :, :3] = rgb("#bfffff")
    img[3, :, 3] = 1.0
    rng = random.Random(5)
    x = 0
    while x < w:                                           # foam dashes (rows 1-2), tileable at 32
        L = rng.randint(2, 6)
        gap = rng.randint(1, 3)
        for k in range(L):
            xx = (x + k) % w
            img[2, xx, :3] = rgb("#e8ffff")
            img[2, xx, 3] = 0.85
            if k % 2 == 0 and L > 3:
                img[1, xx, :3] = rgb("#9fe8ee")
                img[1, xx, 3] = 0.55
        x += L + gap
    for xx in (3, 11, 19, 26):                               # sparse flecks (row 0)
        img[0, xx, :3] = rgb("#e8ffff")
        img[0, xx, 3] = 0.5
    return img


def shared_sprites():
    """Every set-independent sprite, name -> image (identical whichever set builds them)."""
    out = {"uw_ray": make_ray(), "lure_halo": make_halo(), "eyeshine": make_eyeshine()}
    for i, f in enumerate(make_silt()):
        out[f"silt_{i}"] = f
    out["bubble_s"], out["bubble_m"] = make_bubbles()
    out["waterline"] = make_waterline()
    return out


# ============================================================================ lure billboards (16x8, from fk_items)
def _tweak_egi(objs):
    """Frame 1: the cloth fins flutter, the pin crown lifts a little."""
    for o in objs:
        if o.name.startswith("Fin"):
            o.matrix_world = (Matrix.Translation((0.3, 0, -0.05)) @ Matrix.Rotation(math.radians(24), 4, "Y")
                              @ Matrix.Translation((-0.3, 0, 0.05)) @ o.matrix_world)
        if o.name.startswith("Pin"):
            o.matrix_world = (Matrix.Translation((-1.0, 0, 0.04)) @ Matrix.Rotation(math.radians(-10), 4, "Y")
                              @ Matrix.Translation((1.0, 0, -0.04)) @ o.matrix_world)


def _tweak_softworm(objs):
    """Frame 1: the curly tail swings up."""
    for o in objs:
        if o.name.startswith("Tail"):
            o.matrix_world = (Matrix.Translation((-0.62, 0, -0.02)) @ Matrix.Rotation(math.radians(-38), 4, "Y")
                              @ Matrix.Translation((0.62, 0, 0.02)) @ o.matrix_world)


def _tweak_jig(objs):
    """Frame 1: jig flash, the holographic side lights up."""
    flash = C.glow_material("JigFlash", "#f4fbff", 1.6)
    for o in objs:
        if o.name.startswith("Holo") or o.name.startswith("Jig"):
            o.data.materials.clear()
            o.data.materials.append(flash if o.name.startswith("Holo") else
                                    C.pattern_material("JigB", "#7ab0ff", "#ff8ad8", kind="stripes",
                                                       scale=0.5, thresh=0.5, axis=2, shine=1.3))


# frame-1 tweaks of the lures that exist; a set that adds a lure brings its tweak in its own LURE_TWEAKS
LURE_TWEAKS = {"egi": _tweak_egi, "softworm": _tweak_softworm, "jig": _tweak_jig}


def lure_frames(keys, tweaks=None):
    """lure_<key>_0/1 (16x8, side view, nose / line tie to the RIGHT) for fk_items bait ids bait_<key>; frame 1 runs
    tweaks[key](mesh objects) (no tweak = frame 1 equals frame 0)."""
    import fk_items as I
    tw = dict(LURE_TWEAKS, **(tweaks or {}))
    out = {}
    for key in keys:
        bid = "bait_" + key
        for fr in (0, 1):
            C.reset_scene()
            I.build_bait(bid)
            for ob in [o for o in bpy.context.scene.objects if o.name.startswith("Fx")]:
                bpy.data.objects.remove(ob, do_unlink=True)
            bpy.context.view_layer.update()
            objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
            if fr == 1 and key in tw:
                tw[key](objs)
            bpy.context.view_layer.update()
            x0, x1, z0, z1 = C.world_bounds(objs)
            ppu = min(14.0 / (x1 - x0), 6.0 / (z1 - z0))
            C.ortho_camera((x0 + x1) / 2, (z0 + z1) / 2, 16, 8, ppu)
            raw = os.path.join(R.WORK, f"lure_{key}_{fr}_raw.png")
            C.render_raw(raw)
            arr = C.pixelize(C.load_pixels(raw), outline=True)
            out[f"lure_{key}_{fr}"] = arr[::-1].copy()                  # top-down like the other arrays
    return out
