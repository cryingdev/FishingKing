"""
hybrid - the shared STYLE KIT of the "classic 16-bit + cinematic mood" art direction.

Base craft (from retro16): curated limited palettes, palette-ramp emission materials (hard bands, narrow
linear transitions that become ORDERED Bayer dither in post), despeckle, inner contour lines on depth
steps, selective hue-shifted outlines (never near-black ink), horizontal DASH dithering for water.
Mood layer (from cinematic): a TIME-OF-DAY PRESET drives everything - banded sky with a pixel sun and a
concentric dithered glow, key/shadow hues of every ramp, aerial-perspective haze per distance, a mist
band on the far waterline, a sparse far-third sun glitter, exact mirrored reflections, warm rim light on
the OUTER silhouette only, teal-orange grading of the curated palettes.

Arrays are TOP-DOWN (row 0 = top), sRGB 0..1 unless noted. Screen-space sky/sun/mist/glitter use the
fixed stage camera (fk_persp): horizon row HORIZON_ROW, the game shows CROP (x0, y0, w, h).
Nothing here writes outside Tools/Blender/_tmp/variants/hybrid. Stage scripts never edit this file:
they pick a preset with use_preset(name, **overrides) and call the helpers (see notes.md).
"""
import sys
import os
import math
import copy
import json
import random
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
BL = os.path.abspath(os.path.join(HERE, "..", ".."))
if BL not in sys.path:
    sys.path.insert(0, BL)
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import bpy  # noqa: E402
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

OUT = os.path.join(BL, "_tmp", "variants", "hybrid")
WORK = os.path.join(OUT, "work")
os.makedirs(WORK, exist_ok=True)
W, H = P.W, P.H
CROP = (80, 65, 480, 270)                       # x0, y0, w, h (top-down) of what the game shows (its home view)
OX = 0                                          # columns the canvas adds on each side of the 640 px layout
HORIZON_PY = P.F_PX * math.tan(math.radians(P.PITCH))
HORIZON_ROW = H / 2 - HORIZON_PY                # 89.5: the true horizon (independent of standH)


def set_canvas(width, height=400):
    """The stage's canvas (fk_persp.stage_canvas): 640 x 400, or wider with OVERSCAN (the game's camera pans over the
    extra columns beyond its home view; Assets/Scripts/Core/ViewZoom.cs). The camera keeps its focal length and centre,
    so the wider image is the same view with more on both sides. Every screen-space size stays in px; the few
    ABSOLUTE columns of the 640 px layout (preset sky streaks / aurora x0..x1, a stage's anchor columns) shift by OX,
    and CROP (the game's home view) stays centred. Call it before building / rendering a stage (a stage script does it
    at import; tools that load several stages in one process do it per stage)."""
    global W, H, CROP, OX, HORIZON_ROW
    P.set_canvas(width, height)
    W, H = P.W, P.H
    OX = (W - 640) // 2
    CROP = ((W - 480) // 2, (H - 270) // 2, 480, 270)
    HORIZON_ROW = H / 2 - HORIZON_PY

# current key light (towards the light) + hue targets of ramp shading; use_preset() sets them
LIGHT = Vector((-0.55, -0.5, 0.67)).normalized()
SHIFT = dict(dark=285.0, light=95.0, amt=18.0)


# ============================================================================ colour maths
def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float64) / 255.0


def tohex(rgb):
    r = np.clip(np.round(np.asarray(rgb, np.float64) * 255), 0, 255).astype(int)
    return "#%02x%02x%02x" % tuple(r)


def s2l(x):
    x = np.asarray(x, np.float64)
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def l2s(x):
    x = np.clip(np.asarray(x, np.float64), 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)


def oklab(srgb):
    c = s2l(srgb)
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    l_ = np.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
    m_ = np.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
    s_ = np.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)
    return np.stack([0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
                     1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
                     0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_], -1)


def oklab_inv(lab):
    L, a, b = lab[..., 0], lab[..., 1], lab[..., 2]
    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b
    l, m, s = l_ ** 3, m_ ** 3, s_ ** 3
    rgb = np.stack([4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s], -1)
    return l2s(rgb)


def lum(hexc):
    """OKLab lightness of a hex colour (0..1)."""
    return float(oklab(hexrgb(hexc))[0])


def hue_of(hexc):
    L, a, b = oklab(hexrgb(hexc))
    return math.degrees(math.atan2(b, a)) % 360


def _rot_hue(a, b, target_deg, amount_deg):
    h = math.degrees(math.atan2(b, a))
    d = (target_deg - h + 540) % 360 - 180
    step = max(-abs(amount_deg), min(abs(amount_deg), d))
    h2 = math.radians(h + step)
    ch = math.hypot(a, b)
    return ch * math.cos(h2), ch * math.sin(h2)


def shade(hexc, dl, hue=None, chroma=1.0):
    """Hue-shifted shade: dl < 0 darker (hue -> the preset's shadow hue), dl > 0 lighter (-> key hue)."""
    L, a, b = oklab(hexrgb(hexc))
    if hue is None:
        hue = SHIFT["amt"] * abs(dl) / 0.1
    tgt = SHIFT["dark"] if dl < 0 else SHIFT["light"]
    ch = math.hypot(a, b)
    if ch > 0.01:
        a, b = _rot_hue(a, b, tgt, hue)
    k = chroma if chroma != 1.0 else (1.08 if dl < 0 and L + dl > 0.35 else (0.85 if dl < 0 else 0.9))
    return tohex(oklab_inv(np.array([min(0.99, max(0.02, L + dl)), a * k, b * k])))


def ramp_from(hexc, dark=2, light=1, dd=0.095, dlgt=0.075):
    """dark->light list of hue-shifted shades around a base colour."""
    out = [shade(hexc, -dd * i) for i in range(dark, 0, -1)] + [hexc]
    out += [shade(hexc, dlgt * i) for i in range(1, light + 1)]
    return out


def mixhex(a, b, t):
    """Mix two hex colours in linear light (t = 0 -> a)."""
    return tohex(l2s(s2l(hexrgb(a)) * (1 - t) + s2l(hexrgb(b)) * t))


# ============================================================================ palette
class Pal:
    def __init__(self, hexes):
        seen = []
        for h in hexes:
            h = h.lower()
            if h not in seen:
                seen.append(h)
        self.hex = seen
        self.srgb = np.array([hexrgb(h) for h in seen])
        self.lin = s2l(self.srgb)
        self.lab = oklab(self.srgb)
        self._dark = {}

    def __len__(self):
        return len(self.hex)

    def index(self, h):
        return self.hex.index(h.lower())

    def darker(self, i, steps=1, dl=0.1):
        key = (i, steps)
        if key in self._dark:
            return self._dark[key]
        L, a, b = self.lab[i]
        tl = L - dl * steps
        a2, b2 = _rot_hue(a, b, SHIFT["dark"], 12.0 * steps) if math.hypot(a, b) > 0.01 else (a, b)
        best, bi = 1e9, i
        for j in range(len(self.hex)):
            Lj, aj, bj = self.lab[j]
            if Lj > L - 0.035 * steps:
                continue
            d = (Lj - tl) ** 2 * 1.0 + ((aj - a2) ** 2 + (bj - b2) ** 2) * 2.2
            if d < best:
                best, bi = d, j
        if bi == i:  # nothing darker: darkest colour overall
            bi = int(np.argmin(self.lab[:, 0]))
            if self.lab[bi, 0] >= L:
                bi = i
        self._dark[key] = bi
        return bi

    def extend(self, rgbs, snap=0.018):
        """-> (new Pal, [index per rgb]). Colours closer than `snap` (OKLab) to an existing entry reuse it."""
        hexes = list(self.hex)
        labs = [tuple(x) for x in self.lab]
        out = []
        for rgb in rgbs:
            h = tohex(rgb)
            if h in hexes:
                out.append(hexes.index(h))
                continue
            lab = oklab(hexrgb(h))
            d = [((lab[0] - q[0]) ** 2 + (lab[1] - q[1]) ** 2 + (lab[2] - q[2]) ** 2) ** 0.5 for q in labs]
            j = int(np.argmin(d))
            if d[j] < snap:
                out.append(j)
            else:
                hexes.append(h)
                labs.append(tuple(lab))
                out.append(len(hexes) - 1)
        return Pal(hexes), out


def recolour(idx, pal, mask, fn, snap=0.018):
    """Palette-space edit: every pixel in mask gets fn(srgb Nx3 of its palette colour) -> new colour,
    added to the palette when needed (one new entry per SOURCE colour, so palettes stay small)."""
    m = mask & (idx >= 0)
    if not m.any():
        return idx, pal
    src = np.unique(idx[m])
    new = fn(pal.srgb[src].copy())
    pal2, ni = pal.extend(list(new), snap)
    lut = np.arange(len(pal2))
    lut[src] = ni
    out = idx.copy()
    out[m] = lut[idx[m]]
    return out, pal2


def blend_idx(idx, pal, mask, target, k, lighter=False, snap=0.018, space="linear"):
    """Move masked pixels towards `target` (hex / rgb) by k (linear-light mix; space="srgb" mixes in display
    space - gentler on dark colours, use it for light shafts / glows over dark scenes). lighter=True also
    forces the result to be at least a little lighter than the source (for rims)."""
    t0 = hexrgb(target) if isinstance(target, str) else np.asarray(target)
    tl = s2l(t0)

    def fn(c):
        if space == "srgb":
            o = c * (1 - k) + t0[None, :] * k
        else:
            o = l2s(s2l(c) * (1 - k) + tl[None, :] * k)
        if lighter:
            L0 = oklab(c)[:, 0]
            lab = oklab(o)
            lab[:, 0] = np.maximum(lab[:, 0], L0 + 0.05)
            o = oklab_inv(lab)
        return o
    return recolour(idx, pal, mask, fn, snap)


# ============================================================================ time-of-day presets
class Preset(dict):
    """dict with attribute access. See PRESETS and notes.md for every field."""

    def __getattr__(self, k):
        try:
            return self[k]
        except KeyError:
            raise AttributeError(k)

    def copy(self, **kw):
        p = Preset(copy.deepcopy(dict(self)))
        for k, v in kw.items():
            if isinstance(v, dict) and v.get("__whole__"):
                p[k] = {kk: vv for kk, vv in v.items() if kk != "__whole__"}   # replaces the sub-dict entirely
            elif isinstance(v, dict) and isinstance(p.get(k), dict):
                p[k] = dict(p[k], **v)          # partial override of a sub-dict
            else:
                p[k] = v
        return p


# Screen units: every sky/sun/glow/streak/aurora/shaft size is in PIXELS of the 640x400 stage canvas;
# "up" = pixels above HORIZON_ROW, "dx" = pixels right of the image centre. Distances (haze) in metres.
PRESETS = {
    # ------------------------------------------------------------------ lake: golden hour
    "lake": Preset(
        name="lake", mood="golden hour: low sun ahead-right over an open bay, warm haze, backlit rims",
        sky=[(0, "#f7c67c"), (6, "#f0aa6a"), (12, "#de8e68"), (18, "#bb7a78"), (24, "#8e7390"),
             (32, "#646e9c"), (46, "#465e92"), (68, "#324c7e"), (100, "#243a6a")],
        sky_soft=0.4,
        sun=dict(dx=122, up=7.5, r=4.6, core="#fff6dc", limb="#ffd88e"),
        glow=dict(col="#ffc274", rings=[(6, 0.58), (11, 0.38), (17, 0.21), (26, 0.09)], soft=2.0, flat=0.36),
        streaks=[dict(up=17, x0=366, x1=500, th=3, col="#c9837a", lit="#ffc98c"),
                 dict(up=23, x0=176, x1=250, th=2, col="#a47a88", lit="#e8a282")],
        aurora=None, shaft=None,
        key_dir=(0.70, 0.10, 0.70), key_col="#ffd49a", shadow_col="#4a5c90",
        rim=dict(col="#ffcf8c", strength=0.55, dirs=dict(r=1.0, tr=0.85, t=0.55)),
        haze=dict(col="#dfa58e", near=150.0, dist=900.0, max=0.86),
        mist=dict(col="#ecc3a2", amount=0.8, above=2.6, below=2.2, wisp=0.4),
        glitter=dict(cols=["#ffd690", "#fff4d8"], far=0.3, dens=0.45, width=(4.0, 20.0), maxlen=4),
        water=dict(bands=["#d7a07e", "#a98a90", "#7a7f9c", "#577796", "#456a8a", "#395e7e"], dark="#2c5070",
                   refl=["#2a4250", "#3a5462"], tint="#456a8a", deep="#10283a"),
        grade=dict(shadow=(-0.03, 0.012, 0.035), high=(0.045, 0.012, -0.04), sat=0.92, contrast=0.05, amount=1.0),
    ),
    # ------------------------------------------------------------------ stream: clear early morning, valley mist
    "stream": Preset(
        name="stream", mood="clear early morning: pale sun low in the valley notch (left), cool blue-green shade, "
                            "thick valley mist, cream haze",
        sky=[(0, "#f2e4b8"), (8, "#e6e2c6"), (16, "#cadcd6"), (26, "#a8cadc"), (40, "#86b4d6"), (60, "#6a9ecc"),
             (100, "#5286c0")],
        sky_soft=0.45,
        sun=dict(dx=-70, up=16, r=3.6, core="#fffcee", limb="#fff0c0"),
        glow=dict(col="#fff2cc", rings=[(8, 0.55), (18, 0.32), (34, 0.14)], soft=2.5, flat=0.7),
        streaks=[], aurora=None, shaft=None,
        key_dir=(-0.62, 0.15, 0.77), key_col="#fff0c8", shadow_col="#3c5c6c",
        rim=dict(col="#fff2c8", strength=0.4, dirs=dict(l=1.0, tl=0.8, t=0.5)),
        haze=dict(col="#d6e2da", near=40.0, dist=500.0, max=0.8),
        mist=dict(col="#eef0e4", amount=0.95, above=6.0, below=4.0, wisp=0.5),
        glitter=dict(cols=["#f4f4dc", "#ffffff"], far=0.2, dens=0.3, width=(3.0, 12.0), maxlen=2),
        water=dict(bands=["#c6d4c6", "#9cbcb6", "#6e9c9a", "#4e8284", "#3c6e70", "#2e5c5e"], dark="#224c4e",
                   refl=["#1e3a36", "#2c4c46"], tint="#3e7a78", deep="#0c2a2c"),
        grade=dict(shadow=(-0.025, 0.02, 0.02), high=(0.03, 0.02, -0.02), sat=0.9, contrast=0.04, amount=1.0),
    ),
    # ------------------------------------------------------------------ sea: bright late afternoon
    "sea": Preset(
        name="sea", mood="bright late afternoon: sun high left (above the crop, glow only), crisp blue sky, "
                         "silver-cyan water, light from the left",
        sky=[(0, "#e6e6d6"), (8, "#cfe0e4"), (18, "#b0d4e6"), (32, "#8cc0e2"), (52, "#68a8da"), (80, "#4c90d0"),
             (110, "#3a7cc4")],
        sky_soft=0.5,
        sun=dict(dx=-230, up=72, r=5.0, core="#fffff4", limb="#fff6d8"),
        glow=dict(col="#fff8e4", rings=[(14, 0.45), (30, 0.22), (60, 0.1)], soft=3.0, flat=0.9),
        streaks=[dict(up=20, x0=260, x1=420, th=1, col="#e8eef0", lit="#ffffff")],
        aurora=None, shaft=None,
        key_dir=(-0.6, -0.12, 0.79), key_col="#fff2d6", shadow_col="#3a5a84",
        rim=dict(col="#fff4de", strength=0.3, dirs=dict(l=1.0, tl=0.8, t=0.5)),
        haze=dict(col="#d4e2e8", near=200.0, dist=2500.0, max=0.75),
        mist=dict(col="#e4ecee", amount=0.3, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#e4f2fa", "#ffffff"], far=0.45, dens=0.35, width=(10.0, 40.0), maxlen=3, dx=-150),
        water=dict(bands=["#9ec8d8", "#7cb4cc", "#5c9cbc", "#4284a8", "#2e7094", "#225e82"], dark="#1a4e70",
                   refl=["#1c3e50", "#2a5064"], tint="#2c7aa4", deep="#0a2a40"),
        grade=dict(shadow=(-0.02, 0.008, 0.025), high=(0.025, 0.012, -0.015), sat=1.0, contrast=0.05, amount=1.0),
    ),
    # ------------------------------------------------------------------ swamp: foggy dusk
    "swamp": Preset(
        name="swamp", mood="foggy dusk: no sun disc, a dull amber glow behind the fog, olive-grey haze that eats "
                           "everything past ~150 m, heavy mist, no glitter",
        sky=[(0, "#b4a070"), (10, "#9c9474"), (22, "#7c806a"), (38, "#5e6858"), (60, "#465248"), (100, "#343e38")],
        sky_soft=0.6,
        sun=None,
        glow=dict(col="#d0a864", rings=[(22, 0.3), (48, 0.15)], soft=6.0, flat=0.4, dx=-40, up=4),
        streaks=[], aurora=None, shaft=None,
        key_dir=(0.25, 0.45, 0.86), key_col="#c8b48a", shadow_col="#2c3a34",
        rim=dict(col="#d8c088", strength=0.2, dirs=dict(t=1.0, tl=0.6, tr=0.6)),
        haze=dict(col="#96946e", near=20.0, dist=140.0, max=0.92),
        mist=dict(col="#aeaa88", amount=0.95, above=8.0, below=6.0, wisp=0.55),
        glitter=None,
        water=dict(bands=["#8a8466", "#6c6e56", "#525a46", "#3e4a3a", "#303e30", "#263428"], dark="#1e2a20",
                   refl=["#1c2418", "#283222"], tint="#3a4a30", deep="#10180e"),
        grade=dict(shadow=(-0.01, 0.02, 0.0), high=(0.03, 0.015, -0.03), sat=0.8, contrast=0.03, amount=1.0),
    ),
    # ------------------------------------------------------------------ ice: blue hour with aurora
    "ice": Preset(
        name="ice", mood="blue hour after sunset: rose afterglow at the horizon, deep blue zenith, a green aurora "
                         "band, cool moonlit key, pink rims",
        sky=[(0, "#d6a8b2"), (7, "#b098b8"), (15, "#8a86b4"), (26, "#626ea6"), (42, "#44548e"), (66, "#2e3c74"),
             (100, "#1c2656")],
        sky_soft=0.45,
        sun=None,
        glow=dict(col="#f0b8b0", rings=[(16, 0.35), (36, 0.16)], soft=4.0, flat=0.35, dx=-140, up=-2),
        streaks=[],
        aurora=dict(cols=["#4cc896", "#9cf0c4"], up0=9, height=17, amp=3.0, amount=0.45, x0=60, x1=600),
        shaft=None,
        key_dir=(-0.3, 0.3, 0.9), key_col="#cad6f2", shadow_col="#2a3260",
        rim=dict(col="#ffc8d0", strength=0.3, dirs=dict(l=1.0, tl=0.8, t=0.4)),
        haze=dict(col="#a4a0c8", near=100.0, dist=1800.0, max=0.8),
        mist=dict(col="#c4c6e2", amount=0.4, above=3.0, below=2.0, wisp=0.5),
        glitter=None,
        water=dict(bands=["#c6c4e0", "#aab4d6", "#8ea2c8", "#7690bc", "#6480b0", "#5670a2"], dark="#485f90",
                   refl=["#2a3462", "#3a4676"], tint="#6a8ab8", deep="#142040"),
        grade=dict(shadow=(-0.01, 0.0, 0.04), high=(0.03, 0.0, 0.01), sat=0.9, contrast=0.05, amount=1.0),
    ),
    # ------------------------------------------------------------------ ocean: sunrise
    "ocean": Preset(
        name="ocean", mood="sunrise over open sea: sun just up left of centre, pink-gold horizon, azure zenith, "
                           "long glitter path, rims from the left",
        sky=[(0, "#ffca92"), (6, "#f6ae96"), (13, "#e09ca4"), (22, "#b69cbc"), (34, "#8a9ccc"), (52, "#6690cc"),
             (80, "#4a7cc0"), (110, "#3868b0")],
        sky_soft=0.45,
        sun=dict(dx=-90, up=6.0, r=5.0, core="#fff8e4", limb="#ffe0a4"),
        glow=dict(col="#ffd49c", rings=[(10, 0.58), (20, 0.36), (36, 0.18), (60, 0.08)], soft=2.5, flat=0.5),
        streaks=[dict(up=15, x0=120, x1=300, th=2, col="#d898a0", lit="#ffd2a0")],
        aurora=None, shaft=None,
        key_dir=(-0.62, 0.3, 0.72), key_col="#ffdcb0", shadow_col="#3a4c7a",
        rim=dict(col="#ffd8b0", strength=0.5, dirs=dict(l=1.0, tl=0.85, t=0.55)),
        haze=dict(col="#f0c8b2", near=300.0, dist=3000.0, max=0.8),
        mist=dict(col="#f4d4c0", amount=0.35, above=2.0, below=2.0, wisp=0.5),
        glitter=dict(cols=["#ffdca6", "#fff8e6"], far=0.45, dens=0.5, width=(5.0, 30.0), maxlen=4),
        water=dict(bands=["#dcb4a8", "#a8a0b4", "#7088aa", "#44709c", "#2c5c8a", "#1f4c78"], dark="#183e66",
                   refl=["#1a2c46", "#283e5a"], tint="#1e5a8a", deep="#081c34"),
        grade=dict(shadow=(-0.025, 0.01, 0.035), high=(0.04, 0.01, -0.03), sat=0.95, contrast=0.05, amount=1.0),
    ),
    # ------------------------------------------------------------------ cave: crystal glow + one light shaft
    "cave": Preset(
        name="cave", mood="underground lake: no sky (dark violet rock gradient), one daylight shaft from a ceiling "
                          "hole, cyan/violet crystal glow as local lights, dark fog",
        sky=[(0, "#1e1830"), (20, "#181428"), (50, "#120f1e"), (100, "#0c0a14")],
        sky_soft=0.6,
        sun=None,
        glow=None, streaks=[], aurora=None,
        shaft=dict(col="#e6f0d2", amount=0.16, top=(250, 0, 36), bottom=(292, 330, 70), fade=0.6),
        crystal=["#6ad8ff", "#c89cff"],
        key_dir=(0.18, 0.32, 0.93), key_col="#dcebe4", shadow_col="#1e1a34",
        rim=dict(col="#86dcff", strength=0.45, dirs=dict(t=1.0, tl=0.8, tr=0.8)),
        haze=dict(col="#141020", near=10.0, dist=60.0, max=0.9),
        mist=dict(col="#2a3048", amount=0.5, above=3.0, below=3.0, wisp=0.5),
        glitter=dict(cols=["#b8e8e8", "#f0ffff"], far=1.0, dens=0.35, width=(10.0, 26.0), maxlen=3, dx=-20,
                     row0=180, row1=330),
        water=dict(bands=["#1c2c3c", "#18283a", "#142234", "#101c2c", "#0c1824", "#0a141e"], dark="#08101a",
                   refl=["#0a0c16", "#141828"], tint="#1a4a5a", deep="#04101a"),
        grade=dict(shadow=(-0.01, 0.0, 0.03), high=(0.0, 0.02, 0.02), sat=0.95, contrast=0.04, amount=1.0),
    ),
}

PR = [None]


def use_preset(p, **overrides):
    """Select the active preset (name or Preset), optionally overriding fields (sub-dicts merge).
    Sets the default key light of m_tone materials and the hue targets of shade()/ramp_from()/darker()."""
    global LIGHT
    pr = (PRESETS[p] if isinstance(p, str) else p).copy(**overrides)
    PR[0] = pr
    LIGHT = Vector(pr.key_dir).normalized()
    SHIFT["dark"] = hue_of(pr.shadow_col)
    SHIFT["light"] = hue_of(pr.key_col)
    return pr


def preset():
    return PR[0]


def grade_hex(cols, pr=None, amount=None):
    """Teal-orange split toning + saturation + gentle S-curve of a list of hex colours (the mood grade is
    applied to the CURATED palettes, so materials still emit exact palette colours)."""
    pr = pr or PR[0]
    g = pr.grade
    k = g["amount"] if amount is None else amount
    single = isinstance(cols, str)
    out = []
    for h in ([cols] if single else cols):
        c = hexrgb(h)
        L = float(c @ np.array([0.2126, 0.7152, 0.0722]))
        c2 = c + (1 - L) ** 3 * np.array(g["shadow"]) + L ** 2 * np.array(g["high"])
        L2 = float(c2 @ np.array([0.2126, 0.7152, 0.0722]))
        c2 = L2 + (c2 - L2) * g["sat"]
        c2 = c2 + g["contrast"] * (c2 - 0.5) * (1 - np.abs(2 * c2 - 1))
        if g.get("gain") is not None or g.get("tint") is not None:
            # period looks (hyb_period / periods/<stage>.py): exposure gain and a light-colour multiply, linear light
            lin = s2l(np.clip(c2, 0, 1)) * (1.0 if g.get("gain") is None else g["gain"])
            if g.get("tint") is not None:
                lin = lin * s2l(hexrgb(g["tint"]))
            c2 = l2s(lin)
        out.append(tohex(np.clip(c * (1 - k) + np.clip(c2, 0, 1) * k, 0, 1)))
    return out[0] if single else out


def haze_f(dist, pr=None):
    """Aerial perspective amount (0..max) for something `dist` metres from the camera."""
    hz = (pr or PR[0]).haze
    return hz["max"] * (1 - math.exp(-max(0.0, dist - hz["near"]) / hz["dist"]))


def hazed(cols, dist, pr=None, extra=0.0):
    """Curated ramp pushed towards the preset haze colour by the aerial perspective at `dist` m (+extra)."""
    pr = pr or PR[0]
    f = min(0.97, haze_f(dist, pr) + extra)
    return [mixhex(c, pr.haze["col"], f) for c in cols]


def sun_rc(pr=None):
    """(col, row) of the painted sun centre (also valid when pr.sun is None: the glow centre)."""
    pr = pr or PR[0]
    s = pr.sun or pr.glow or dict(dx=0, up=0)
    return W / 2 + s.get("dx", 0), HORIZON_ROW - s.get("up", 0)


def sun_side(pr=None):
    """'right' or 'left': the side the key light comes from (for outlines / rims)."""
    return "right" if (pr or PR[0]).key_dir[0] >= 0 else "left"


# ============================================================================ sky / water colour (numpy, screen space)
def _bands(up, stops, soft):
    """Flat colour bands by 'px above horizon' with narrow linear transitions (-> Bayer in quantize)."""
    es = [e for e, _ in stops]
    cs = [s2l(hexrgb(c)) for _, c in stops]
    col = np.broadcast_to(cs[0], up.shape + (3,)).copy()
    band = np.zeros(up.shape, int)
    for i in range(1, len(es)):
        wd = max(1.0, soft * (es[i] - es[i - 1]))
        t = np.clip((up - (es[i] - wd / 2)) / wd, 0, 1)[..., None]
        col = col * (1 - t) + cs[i] * t
        band = np.where(up >= es[i], i, band)
    return col, band


def glow_level(pr, w=None, h=None, mirror=False):
    """Concentric banded glow level (0..~0.6) around the sun, flat rings with narrow transitions."""
    w, h = w or W, h or H
    g = pr.glow
    if not g:
        return np.zeros((h, w)), np.zeros((h, w), int)
    sc, sr = W / 2 + g.get("dx", (pr.sun or {}).get("dx", 0)), HORIZON_ROW - g.get("up", (pr.sun or {}).get("up", 0))
    rows = np.arange(h)[:, None] + 0.5
    if mirror:
        rows = 2 * HORIZON_ROW - rows
    cols = np.arange(w)[None, :] + 0.5
    d = np.sqrt(((cols - sc) * g.get("flat", 0.5)) ** 2 + (rows - sr) ** 2)
    lv = np.zeros((h, w))
    ring = np.zeros((h, w), int)
    rings = sorted(g["rings"], key=lambda r: -r[0])        # outer first
    for k, (rad, level) in enumerate(rings):
        t = np.clip((rad + g["soft"] / 2 - d) / g["soft"], 0, 1)
        lv = lv * (1 - t) + level * t
        ring = np.where(d < rad, k + 1, ring)
    return lv, ring


def sky_rgb(pr=None, w=None, h=None, mirror=False):
    """Continuous sRGB sky (banded gradient + glow rings + sun disc + stratus streaks + aurora) and the
    curated palette (list of hex) it quantises to. mirror=True returns the sky as mirrored in calm water
    (rows below the horizon see the sky at the same distance above it; the sun disc is left out - use
    glitter for it). Quantise with quantize(rgb, a, Pal(hexes), dither=True)."""
    pr = pr or PR[0]
    w, h = w or W, h or H
    rows = np.arange(h)[:, None] + 0.5
    up = (HORIZON_ROW - rows) if not mirror else (rows - HORIZON_ROW)
    up = np.broadcast_to(up, (h, w)).copy()
    col, band = _bands(up, pr.sky, pr.sky_soft)
    pal = [c for _, c in pr.sky]
    g = pr.glow
    if g:
        lv, ring = glow_level(pr, w, h, mirror)
        gl = s2l(hexrgb(g["col"]))
        col = col * (1 - lv[..., None]) + gl * lv[..., None]
        rings = sorted(g["rings"], key=lambda r: -r[0])
        for b in np.unique(band[ring > 0]):
            for k in np.unique(ring[(band == b) & (ring > 0)]):
                pal.append(mixhex(pr.sky[b][1], g["col"], rings[k - 1][1]))
    if pr.aurora:
        au = pr.aurora
        cols = np.arange(w)[None, :] + 0.5 - OX              # (the 640 px layout's columns: x0 / x1 are in them)
        lo = au["up0"] + au["amp"] * (np.sin(cols / 37.0) * 0.7 + np.sin(cols / 13.0 + 1.3) * 0.3)
        hgt = au["height"] * (0.7 + 0.3 * np.sin(cols / 23.0 + 0.4))
        u = (up - lo) / hgt
        rays = 0.6 + 0.4 * np.sin(cols * 0.9 + np.sin(cols * 0.21) * 3.0)
        inside = (u > 0) & (u < 1) & (cols > au["x0"]) & (cols < au["x1"])
        a = np.where(inside, np.clip(1 - u, 0, 1) ** 1.5 * rays * au["amount"], 0.0)
        bright = inside & (u < 0.18)
        c1, c2 = s2l(hexrgb(au["cols"][0])), s2l(hexrgb(au["cols"][1]))
        ac = np.where(bright[..., None], c2, c1)
        lvl = np.where(a > 0.22, 0.5, np.where(a > 0.08, 0.25, 0.0))
        lvl = np.where(bright & (a > 0.08), 0.6, lvl)
        col = col * (1 - lvl[..., None]) + ac * lvl[..., None]
        for b in np.unique(band[lvl > 0]):
            for lvv, cc in ((0.25, au["cols"][0]), (0.5, au["cols"][0]), (0.6, au["cols"][1])):
                pal.append(mixhex(pr.sky[b][1], cc, lvv))
    st = pr.get("stars")
    if st and not mirror:
        # night looks (periods/<stage>.py): single-pixel stars, a dim and a bright colour, never inside the moon's glow
        u = np.random.default_rng(st.get("seed", 11)).random((h, w))
        near = glow_level(pr, w, h)[0] if pr.glow else np.zeros((h, w))
        star = (up >= st.get("up0", 10)) & (near < st.get("glow_max", 0.12)) & (u < st["dens"])
        bright = star & (u < st["dens"] * st.get("bright", 0.25))
        col[star & ~bright] = s2l(hexrgb(st["cols"][0]))
        col[bright] = s2l(hexrgb(st["cols"][1]))
        pal += list(st["cols"])
    if not mirror:
        for st in pr.streaks or []:
            cols = np.arange(w) + 0.5 - OX                    # (the 640 px layout's columns: x0 / x1 are in them)
            u = np.clip((cols - st["x0"]) / (st["x1"] - st["x0"]), 0, 1)
            # tapered lens: 1 px tails, st["th"] px in the middle, the flat underside row lit gold near the sun
            env = np.where((cols > st["x0"]) & (cols < st["x1"]), np.clip(np.sin(np.pi * u), 0, 1), 0.0)
            wob = 1 + 0.22 * np.sin(cols * 0.19 + st["up"])
            th = np.round(st["th"] * env * wob + 0.4 * (env > 0.08))
            r0 = int(round(HORIZON_ROW - st["up"]))
            for x in range(w):
                t = int(th[x])
                for k in range(t):
                    r = r0 - k
                    if 0 <= r < h:
                        col[r, x] = s2l(hexrgb(st["col"]))
                if t >= 2 and 0 <= r0 < h and abs(x - sun_rc(pr)[0]) < 170:   # (x: canvas column, like sun_rc)
                    col[r0, x] = s2l(hexrgb(st["lit"]))
            pal += [st["col"], st["lit"]]
        if pr.sun:
            s = pr.sun
            sc, sr = sun_rc(pr)
            cols = np.arange(w)[None, :] + 0.5
            d = np.sqrt((cols - sc) ** 2 + (rows - sr) ** 2)
            core = d < s["r"] - 1.1
            disc = d < s["r"]
            col[disc] = s2l(hexrgb(s["limb"]))
            col[core] = s2l(hexrgb(s["core"]))
            pal += [s["core"], s["limb"]]
    return l2s(col), pal


def light_shaft(pr=None, w=None, h=None):
    """Amount map (0..1) of a screen-space light shaft (trapezoid, soft edges, fading downwards)."""
    pr = pr or PR[0]
    w, h = w or W, h or H
    s = pr.shaft
    if not s:
        return np.zeros((h, w))
    (xt, rt, wt), (xb, rb, wb) = s["top"], s["bottom"]
    rows = np.arange(h)[:, None] + 0.5
    cols = np.arange(w)[None, :] + 0.5 - OX                  # (the 640 px layout's columns: the shaft's x are in them)
    t = np.clip((rows - rt) / max(1.0, rb - rt), 0, 1)
    xc = xt + (xb - xt) * t
    hw = (wt + (wb - wt) * t) / 2
    edge = np.clip((hw - np.abs(cols - xc)) / 6.0, 0, 1)
    return edge * (1 - s["fade"] * t) * ((rows >= rt) & (rows <= rb))     # x pr.shaft["amount"] when blending


def apply_shaft(idx, pal, pr=None, seed=5):
    """Translucent light shaft in palette space: 2 flat levels (core / soft edge) + sparse dust motes."""
    pr = pr or PR[0]
    s = pr.shaft
    if not s:
        return idx, pal
    a = light_shaft(pr, idx.shape[1], idx.shape[0])
    core = a > 0.3 + 0.4 * bayer(*a.shape)                  # Bayer-dithered hand-over core -> soft level
    idx, pal = blend_idx(idx, pal, core, s["col"], s["amount"], space="srgb")
    idx, pal = blend_idx(idx, pal, (a > 0.1) & ~core, s["col"], s["amount"] * 0.5, space="srgb")
    dust = (a > 0.3) & (np.random.default_rng(seed).random(a.shape) < 0.012)
    return blend_idx(idx, pal, dust, s["col"], min(1.0, s["amount"] * 3.0), space="srgb")


# ============================================================================ water helpers (dash dithering)
def dash_mask(n, t, lmin, lmax, rng):
    """1-D mask of horizontal dashes (length lmin..lmax) covering ~t of the row, random offsets."""
    m = np.zeros(n, bool)
    if t <= 0.0:
        return m
    if t >= 1.0:
        m[:] = True
        return m
    c = -rng.randint(0, lmax * 3)
    while c < n:
        L = rng.randint(lmin, lmax)
        g = max(1, int(round(L * (1 - t) / t * rng.uniform(0.35, 1.65))))
        m[max(0, c):max(0, c + L)] = True
        c += L + g
    return m


def run_noise(rng, n, lmin, lmax):
    """Row of random values held constant over runs of lmin..lmax px (horizontal 'dash' noise)."""
    v = np.zeros(n)
    c = 0
    while c < n:
        L = rng.randint(lmin, lmax)
        v[c:c + L] = rng.random()
        c += L
    return v


def dash_threshold(h, w, rng, lmin=2, lmax=8):
    """HxW threshold map made of horizontal runs: `amount > thr` gives dash-dithered mist/water edges."""
    return np.stack([run_noise(rng, w, lmin, lmax) for _ in range(h)])


def min_runs(lab, minlen=3):
    """Per row: merge label runs shorter than minlen into the left (or right) neighbour run."""
    out = lab.copy()
    for r in range(out.shape[0]):
        row = out[r]
        if not row.any():
            continue
        for _ in range(3):
            edges = np.flatnonzero(np.diff(row)) + 1
            starts = np.concatenate([[0], edges])
            ends = np.concatenate([edges, [len(row)]])
            changed = False
            for s, e in zip(starts, ends):
                if e - s < minlen:
                    if s > 0:
                        row[s:e] = row[s - 1]
                    elif e < len(row):
                        row[s:e] = row[e]
                    changed = True
            if not changed:
                break
    return out


def dash_len(r, row_h, row_p):
    s = min(1.0, max(0.0, (r - row_h) / max(1, row_p - row_h)))
    return int(round(2 + 6 * s)), int(round(3 + 13 * s))


def water_bands(pal, bands, zones, rng, row_h=106, row_p=335):
    """Per-pixel water band palette index (HxW): bands[i] -> bands[i+1] across zones[i] = (row0, row1),
    joined by horizontal dash dithering (2-3 px dashes near the horizon growing to 8-16 px near row_p)."""
    wi = [pal.index(c) for c in bands]
    out = np.zeros((H, W), int)
    for r in range(H):
        k = 0
        t = 0.0
        for i, (a, b) in enumerate(zones):
            if r >= b:
                k = i + 1
            elif r >= a:
                k = i
                t = (r - a + 0.5) / (b - a)
                break
        out[r] = wi[min(k, len(wi) - 1)]
        if t > 0 and k + 1 < len(wi):
            lmin, lmax = dash_len(r, row_h, row_p)
            out[r, dash_mask(W, t, lmin, lmax, rng)] = wi[k + 1]
    return out


def ramp_step(pal, ramp, arr, d):
    """Move palette indices d steps along a hex ramp list (dark -> light); others stay put."""
    lut = np.arange(len(pal))
    for i, h in enumerate(pal.hex):
        if h in ramp:
            lut[i] = pal.index(ramp[min(len(ramp) - 1, max(0, ramp.index(h) + d))])
    return lut[arr]


def glitter_masks(pr, water, rng, avoid=None):
    """Sun glitter as sparse SHORT horizontal dashes in the far part of the water under the sun column,
    fading out towards the viewer. Returns (amber mask, bright mask). No rays, no pillar."""
    g = pr.glitter
    am = np.zeros((H, W), bool)
    br = np.zeros((H, W), bool)
    if not g:
        return am, br
    col_s = int(round(W / 2 + g.get("dx", (pr.sun or {}).get("dx", 0))))
    col_s = min(W - 1, max(0, col_s))
    wc = np.flatnonzero(water[:, col_s])
    if not len(wc):
        return am, br
    r0 = g.get("row0", int(wc.min()))
    r1 = g.get("row1", int(wc.max()))
    r_end = int(r0 + g["far"] * (r1 - r0))
    for r in range(r0, r_end):
        t = (r - r0) / max(1, r_end - r0)
        if t > 0.3 and r % 2:
            continue
        if t > 0.65 and r % 4:
            continue
        half = g["width"][0] + (g["width"][1] - g["width"][0]) * t
        dens = g["dens"] * (1 - t) ** 1.4
        maxl = max(1, int(round(1 + (g["maxlen"] - 1) * min(1.0, t * 1.6))))
        x = col_s - half * 1.6 - rng.randint(0, 3)
        while x < col_s + half * 1.6:
            L = rng.randint(1, maxl)
            q = dens * math.exp(-((x + L / 2 - col_s) / half) ** 2)
            if rng.random() < q:
                xi = int(x)
                sl = slice(max(0, xi), min(W, xi + L))
                ok = water[r, sl] & (True if avoid is None else ~avoid[r, sl])
                am[r, sl] |= ok
                if abs(x - col_s) < half * 0.45 and t < 0.6 and rng.random() < 0.45:
                    br[r, sl] |= ok
            x += L + rng.randint(1, 3) + int(4 * t)
    return am & ~br, br


def mist_amount(pr, line, land, water, cols_w=None):
    """Mist amount (0..1) around a per-column far waterline `line` (row array, len W): decays upwards over
    the land (pr.mist.above px) and downwards over the water (pr.mist.below px), with 1-D wisps."""
    m = pr.mist
    if not m:
        return np.zeros((H, W))
    rows = np.arange(H)[:, None] + 0.5
    L = np.asarray(line, float)[None, :]
    x = np.arange(W)
    wisp = 1 - m["wisp"] * (0.6 * _vnoise(x / 23.0, 3.0) + 0.4 * _vnoise(x / 7.0, 5.0))
    up = np.where(land & (rows < L), np.exp(-(L - rows) / m["above"]), 0.0)
    dn = np.where(water & (rows >= L), np.exp(-(rows - L) / m["below"]), 0.0)
    a = m["amount"] * (up + dn) * wisp[None, :]
    if cols_w is not None:
        a *= cols_w[None, :]
    return np.clip(a, 0, 1)


def _hash(a, b=0.0):
    v = np.sin(np.asarray(a, np.float64) * 12.9898 + b * 78.233) * 43758.5453
    return v - np.floor(v)


def _vnoise(x, seed=0.0):
    i = np.floor(x)
    f = x - i
    a, b = _hash(i, seed), _hash(i + 1, seed)
    u = f * f * (3 - 2 * f)
    return a + (b - a) * u


# ============================================================================ materials
USED = []          # hex colours used by the materials of the current asset (palette registry)
_mats = {}


def reset_materials():
    USED.clear()
    _mats.clear()


def _cached(key):
    m = _mats.get(key)
    if m is None:
        return None
    try:
        m.name
        return m
    except ReferenceError:
        del _mats[key]
        return None


def _use(cols):
    for c in cols:
        c = c.lower()
        if c not in USED:
            USED.append(c)


class NB(C.NB):
    pass


def _factor_lambert(nb, light=None, wrap=0.5):
    geo = nb.node("ShaderNodeNewGeometry")
    d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(light or LIGHT))
    return nb.math("MULTIPLY_ADD", d, wrap, 1 - wrap)


def _coord(nb, kind):
    if kind == "world":
        geo = nb.node("ShaderNodeNewGeometry")
        return geo.outputs["Position"]
    tc = nb.node("ShaderNodeTexCoord")
    return tc.outputs["Object" if kind == "object" else ("UV" if kind == "uv" else "Generated")]


def band_ramp(nb, fac, cols, bounds=None, soft=0.0):
    """Palette ramp: flat bands, optional narrow linear transitions (these dither in post)."""
    n = len(cols)
    lc = [C.lin(c) for c in cols]
    if bounds is None:
        bounds = [i / n for i in range(1, n)]
    hard = all(x <= 0 for x in soft) if isinstance(soft, (list, tuple)) else soft <= 0
    if hard or n == 1:
        stops = [(0.0, lc[0])] + [(b, lc[i + 1]) for i, b in enumerate(bounds)]
        return nb.ramp(fac, stops, "CONSTANT")
    stops = [(0.0, lc[0])]
    for i, b in enumerate(bounds):
        s = soft[i] if isinstance(soft, (list, tuple)) else soft
        stops.append((max(0.0, b - s / 2), lc[i]))
        stops.append((min(1.0, b + s / 2 + 1e-4), lc[i + 1]))
    return nb.ramp(fac, stops, "LINEAR")


def m_tone(cols, bounds=None, soft=0.0, light=None, wrap=0.5, lam=1.0, bias=0.0,
           grads=(), noise=0.0, nscale=5.0, ncoord="object", ndetail=2.0, spec=None, spec_th=0.955,
           pats=(), nvec=None, name="Tone"):
    """General palette-ramp material. f = lam*lambert + sum(grad terms) + noise + bias -> band ramp.
    grads: tuples (axis 0/1/2, a0, a1, weight, coord kind) -> weight * clamp((pos-a0)/(a1-a0))."""
    key = ("tone", tuple(cols), tuple(bounds or ()), soft if not isinstance(soft, list) else tuple(soft),
           tuple(light or LIGHT), wrap, lam, bias, tuple(grads), noise, nscale, ncoord, ndetail, spec, spec_th,
           tuple(pats), nvec)
    if _cached(key) is not None:
        return _mats[key]
    _use(cols)
    m = bpy.data.materials.new(name)
    nb = NB(m)
    f = nb.val(bias)
    if lam:
        f = nb.math("MULTIPLY_ADD", _factor_lambert(nb, light, wrap), lam, f)
    for g in grads:
        ax, a0, a1, w = g[:4]
        kind = g[4] if len(g) > 4 else "world"
        s = nb.sep(_coord(nb, kind))
        t = nb.math("DIVIDE", nb.math("SUBTRACT", s[ax], a0), a1 - a0, clamp=True)
        f = nb.math("MULTIPLY_ADD", t, w, f)
    if noise:
        no = nb.node("ShaderNodeTexNoise")
        co = _coord(nb, ncoord)
        if nvec:
            co = nb.vmath("MULTIPLY", co, tuple(nvec))
        nb.link(co, no.inputs["Vector"])
        no.inputs["Scale"].default_value = nscale
        no.inputs["Detail"].default_value = ndetail
        f = nb.math("MULTIPLY_ADD", nb.math("SUBTRACT", no.outputs["Fac"], 0.5), noise * 2, f)
    for pt in pats:
        # stripes: (axis, period, duty, weight, coord kind, offset)
        ax, per, duty, w = pt[:4]
        kind = pt[4] if len(pt) > 4 else "object"
        off = pt[5] if len(pt) > 5 else 0.0
        s = nb.sep(_coord(nb, kind))
        fr = nb.math("FRACT", nb.math("DIVIDE", nb.math("ADD", s[ax], off), per))
        f = nb.math("MULTIPLY_ADD", nb.math("LESS_THAN", fr, duty), w, f)
    col = band_ramp(nb, f, cols, bounds, soft)
    if spec:
        _use([spec])
        geo = nb.node("ShaderNodeNewGeometry")
        half = (Vector(light or LIGHT) + Vector((0, -1, 0))).normalized()
        s = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(half))
        hi = nb.math("GREATER_THAN", s, spec_th)
        col = nb.mix(hi, col, C.lin(spec))
    nb.output_emission(col, 1.0)
    _mats[key] = m
    return m


def m_flat(hexc):
    key = ("flat", hexc)
    if _cached(key) is None:
        _use([hexc])
        m = bpy.data.materials.new("Flat")
        nb = NB(m)
        nb.output_emission(nb.rgb(C.lin(hexc)), 1.0)
        _mats[key] = m
    return _mats[key]


def m_grad(cols, axis, a0, a1, bounds=None, soft=0.1, coord="world"):
    """Pure gradient along one axis (no lighting) with dithered transitions."""
    return m_tone(cols, bounds, soft, lam=0.0, grads=((axis, a0, a1, 1.0, coord),), name="Grad")


_idmat = [None]


def id_material():
    ok = False
    if _idmat[0] is not None:
        try:
            ok = _idmat[0].name in bpy.data.materials
        except ReferenceError:
            ok = False
    if not ok:
        m = bpy.data.materials.new("HYB_ID")
        nb = NB(m)
        oi = nb.node("ShaderNodeObjectInfo")
        cd = nb.node("ShaderNodeCameraData")
        lo = nb.math("MODULO", oi.outputs["Object Index"], 1024.0)
        hi = nb.math("FLOOR", nb.math("DIVIDE", oi.outputs["Object Index"], 1024.0))
        nb.output_emission(nb.combine(lo, cd.outputs["View Z Depth"], hi), 1.0)
        _idmat[0] = m
    return _idmat[0]


# ============================================================================ geometry helpers
def mesh_from(name, verts, faces, mat, smooth=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if mat is not None:
        ob.data.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = smooth
    return ob


def loft(name, pts, rads, mat, segs=12, up=(0, 0, 1), caps=True, smooth=True, twist=0.0):
    """Tube along a polyline with elliptical sections. rads: r or (ra, rb) per point.
    'a' axis is perpendicular to the tangent in the plane containing `up` (for vertical tubes a = X)."""
    n = len(pts)
    P3 = [Vector(p) for p in pts]
    verts, faces = [], []
    upv = Vector(up)
    for i, p in enumerate(P3):
        t = (P3[min(i + 1, n - 1)] - P3[max(i - 1, 0)]).normalized()
        ref = Vector((1, 0, 0)) if abs(t.dot(Vector((1, 0, 0)))) < 0.9 else Vector((0, 1, 0))
        if abs(t.dot(upv)) < 0.9:
            ref = upv
        a = ref - t * ref.dot(t)
        a.normalize()
        b = t.cross(a).normalized()
        r = rads[i] if isinstance(rads, (list, tuple)) else rads
        ra, rb = (r, r) if not isinstance(r, (list, tuple)) else r
        for k in range(segs):
            ang = 2 * math.pi * k / segs + twist
            verts.append(p + a * (math.cos(ang) * ra) + b * (math.sin(ang) * rb))
    for i in range(n - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append((i * segs + k, i * segs + k2, (i + 1) * segs + k2, (i + 1) * segs + k))
    if caps:
        c0 = len(verts)
        verts.append(P3[0])
        c1 = len(verts)
        verts.append(P3[-1])
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append((k2, k, c0))
            faces.append(((n - 1) * segs + k, (n - 1) * segs + k2, c1))
    ob = mesh_from(name, verts, faces, mat, smooth)
    _fix_normals(ob)
    return ob


def _fix_normals(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()


def ellipsoid(name, c, r, mat, segs=16, rings=10, rot=None):
    ob = C.add_prim("sphere", name, mat, radius=1.0, location=(0, 0, 0), segments=segs, ring_count=rings)
    M = Matrix.Translation(Vector(c))
    if rot is not None:
        M = M @ rot
    M = M @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    ob.matrix_world = M
    C.set_smooth(ob)
    return ob


def box(name, c, s, mat, rot=None, bevel=0.0, smooth=False):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= s[0]
        v.co.y *= s[1]
        v.co.z *= s[2]
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES")
    ob = C.mesh_object(name, bm, mat)
    M = Matrix.Translation(Vector(c))
    if rot is not None:
        M = M @ rot
    ob.matrix_world = M
    if smooth:
        C.set_smooth(ob)
    return ob


def hpoly(name, pts, z, mat, th=0.02):
    """Horizontal polygon (points in XY) at height z."""
    bm = bmesh.new()
    top = [bm.verts.new((x, y, z + th / 2)) for x, y in pts]
    bot = [bm.verts.new((x, y, z - th / 2)) for x, y in pts]
    bm.faces.new(top)
    bm.faces.new(bot[::-1])
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((top[i], bot[i], bot[j], top[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return C.mesh_object(name, bm, mat)


def vpoly(name, pts, y, mat, th=0.05):
    return C.poly_object(name, pts, mat, thickness=th, y=y)


def ellipse_pts(cx, cy, rx, ry, n=16, rot=0.0):
    out = []
    c, s = math.cos(rot), math.sin(rot)
    for k in range(n):
        a = 2 * math.pi * k / n
        x, y = rx * math.cos(a), ry * math.sin(a)
        out.append((cx + x * c - y * s, cy + x * s + y * c))
    return out


def tagk(ob, kind, grp=None):
    ob["kind"] = kind
    if grp is not None:
        ob["grp"] = grp
    return ob


# ============================================================================ stage camera helpers
def rc(p, stand):
    """World point -> (col, row) top-down pixel coords on the 640x400 stage canvas."""
    x, y, _ = P.project(p, stand)
    return W / 2 + x, H / 2 - y


def col_to_x(col, y, stand):
    """World x on the line at forward distance y that projects to screen column col (at z = 0)."""
    _, _, zc = P.project((0.0, y, 0.0), stand)
    return (col - W / 2) * zc / P.F_PX


def water_row(dist, stand):
    """Screen row of the waterline at forward distance dist."""
    return rc((0.0, dist, 0.0), stand)[1]


def z_for_row(row, y, stand, x=0.0):
    lo, hi = -2000.0, 8000.0
    for _ in range(60):
        m = (lo + hi) / 2
        if rc((x, y, m), stand)[1] > row:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


def world_z(depth, stand):
    """Per-pixel world Z from the camera depth pass (stage camera)."""
    s, c = math.sin(math.radians(P.PITCH)), math.cos(math.radians(P.PITCH))
    yv = (H / 2 - (np.arange(H) + 0.5))[:, None]
    zc = np.where(depth < 1e8, depth, 0.0)
    yc = yv * zc / P.F_PX
    rz = -zc * s + yc * c
    return P.CAM_UP + stand + rz


# ============================================================================ render
def _load_exr(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    arr = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(arr)
    bpy.data.images.remove(img)
    return arr.reshape(h, w, 4)[::-1].copy()   # top-down


def render_exr(tag):
    sc = bpy.context.scene
    st = sc.render.image_settings
    st.file_format = "OPEN_EXR"
    st.color_mode = "RGBA"
    st.color_depth = "32"
    path = os.path.join(WORK, f"{tag}.exr")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return _load_exr(path)


def mesh_objects(visible_only=True):
    out = []
    for ob in bpy.context.scene.objects:
        if ob.type == "MESH" and (not visible_only or not ob.hide_render):
            out.append(ob)
    return out


def render_passes(tag, want_ids=True):
    """-> dict(rgb=sRGB HxWx3, a=alpha HxW bool, id=int HxW (0 = empty), depth=HxW, kinds={id: kind})."""
    sc = bpy.context.scene
    sc.render.film_transparent = True
    col = render_exr(tag + "_col")
    out = dict(rgb=l2s(col[..., :3]).astype(np.float32), a=col[..., 3] > 0.5)
    if not want_ids:
        return out
    objs = mesh_objects()
    grp = {}
    saved = []
    idm = id_material()
    for k, ob in enumerate(objs):
        g = ob.get("grp")
        if g is not None:
            if g not in grp:
                grp[g] = len(grp) + 1 + 8000
            ob.pass_index = grp[g]
        else:
            ob.pass_index = k + 1
        mats = [s.material for s in ob.material_slots]
        saved.append((ob, mats))
        if not ob.material_slots:
            ob.data.materials.append(idm)
        for s in ob.material_slots:
            s.material = idm
    ids = render_exr(tag + "_id")
    for ob, mats in saved:
        if not mats:
            ob.data.materials.clear()
        for s, m in zip(ob.material_slots, mats):
            s.material = m
    out["kinds"] = {ob.pass_index: ob.get("kind", "") for ob, _ in saved}
    idv = np.round(ids[..., 0]).astype(np.int64) + 1024 * np.round(ids[..., 2]).astype(np.int64)
    idv[~out["a"]] = 0
    out["id"] = idv
    out["depth"] = np.where(out["a"], ids[..., 1], 1e9).astype(np.float32)
    return out


def kind_map(ps):
    """HxW array of the 'kind' tag of every pixel ('' = empty / untagged)."""
    kinds = ps["kinds"]
    return np.vectorize(lambda i: kinds.get(int(i), ""))(ps["id"])


def mirror_pass(objs, ids=False, tag="mirror"):
    """EXACT planar reflection: render mirrored copies (z -> -z) of objs only, through the same camera."""
    sc = bpy.context.scene
    hidden = []
    for ob in mesh_objects(False):
        if not ob.hide_render:
            hidden.append(ob)
            ob.hide_render = True
    Mz = Matrix.Diagonal((1.0, 1.0, -1.0, 1.0))
    copies = []
    for ob in objs:
        c = ob.copy()
        sc.collection.objects.link(c)
        c.matrix_world = Mz @ ob.matrix_world
        c.hide_render = False
        copies.append(c)
    bpy.context.view_layer.update()
    ps = render_passes(tag, want_ids=ids)
    for c in copies:
        bpy.data.objects.remove(c, do_unlink=True)
    for ob in hidden:
        ob.hide_render = False
    return ps


# ============================================================================ post-process
BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float64) + 0.5) / 16.0


def bayer(h, w, ox=0, oy=0):
    yy = (np.arange(h)[:, None] + oy) % 4
    xx = (np.arange(w)[None, :] + ox) % 4
    return BAYER4[yy, xx]


def quantize(rgb, a, pal, dither=True, dmask=None, ox=0, oy=0, bias=0.0, thr=None):
    """Palette quantisation with ordered 2-colour dithering along palette segments.
    thr: optional HxW threshold map (default Bayer 4x4; dash_threshold() for horizontal runs).
    Returns idx (HxW int, -1 = transparent) and the 'solid' mask (exact palette pixels)."""
    H_, W_ = a.shape
    Cl = s2l(rgb.reshape(-1, 3))
    Pl = pal.lin
    d = (Cl ** 2).sum(1)[:, None] - 2.0 * (Cl @ Pl.T) + (Pl ** 2).sum(1)[None, :]
    i1 = d.argmin(1)
    dmin = d[np.arange(len(i1)), i1]
    solid = dmin < 2e-6
    idx = i1.copy()
    if dither:
        P1 = Pl[i1]
        best = np.full(len(i1), np.inf)
        tb = np.zeros(len(i1))
        i2 = i1.copy()
        D = Cl - P1
        for j in range(len(Pl)):
            v = Pl[j][None, :] - P1
            vv = (v * v).sum(1)
            ok = vv > 1e-10
            t = np.where(ok, (D * v).sum(1) / np.maximum(vv, 1e-10), 0.0).clip(0, 1)
            dist = ((D - t[:, None] * v) ** 2).sum(1) + 2e-3 * vv
            better = ok & (dist < best)
            best = np.where(better, dist, best)
            tb = np.where(better, t, tb)
            i2 = np.where(better, j, i2)
        th = (bayer(H_, W_, ox, oy) if thr is None else np.asarray(thr)).reshape(-1)
        flip = (tb + bias > th) & (~solid)
        if dmask is not None:
            flip &= dmask.reshape(-1)
        idx = np.where(flip, i2, i1)
    idx = idx.reshape(H_, W_)
    idx[~a] = -1
    return idx, solid.reshape(H_, W_)


def _nb4(arr, fill):
    """Neighbour stack (up, down, left, right) of a 2D array -> HxWx4."""
    H_, W_ = arr.shape
    out = np.full((H_, W_, 4), fill, dtype=arr.dtype)
    out[1:, :, 0] = arr[:-1, :]    # value of the pixel above
    out[:-1, :, 1] = arr[1:, :]    # below
    out[:, 1:, 2] = arr[:, :-1]    # left
    out[:, :-1, 3] = arr[:, 1:]    # right
    return out


def shift(a, dy, dx, fill=False):
    """out[y, x] = a[y - dy, x - dx]  (content moves by +dy, +dx)."""
    out = np.full_like(a, fill)
    h, w = a.shape[:2]
    ys, yd = slice(max(dy, 0), h + min(dy, 0)), slice(max(-dy, 0), h + min(-dy, 0))
    xs, xd = slice(max(dx, 0), w + min(dx, 0)), slice(max(-dx, 0), w + min(-dx, 0))
    out[ys, xs] = a[yd, xd]
    return out


def dilate(m, conn=8):
    out = m.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if (dy or dx) and (conn == 8 or dy == 0 or dx == 0):
                out |= shift(m, dy, dx, False)
    return out


def outer_empty(opaque, min_gap=3):
    """Empty pixels connected to the image border through gaps >= min_gap px wide. Holes (arm / torso)
    and slits narrower than min_gap (between the legs) are NOT outer: rims never light inner gaps."""
    empty = ~opaque
    e = empty
    r = (min_gap - 1) // 2
    for _ in range(r):
        e = ~dilate(~e)
    for _ in range(r):
        e = dilate(e) & empty
    seed = np.zeros_like(e)
    seed[0, :] = seed[-1, :] = True
    seed[:, 0] = seed[:, -1] = True
    reg = seed & e
    while True:
        nxt = dilate(reg, 4) & e
        if nxt.sum() == reg.sum():
            return nxt
        reg = nxt


_DIRS = {"r": (0, 1), "tr": (-1, 1), "t": (-1, 0), "tl": (-1, -1), "l": (0, -1), "b": (1, 0),
         "br": (1, 1), "bl": (1, -1)}


def rim_amount(idx, dirs, min_gap=3, min_thick=2, outer=None):
    """Per-pixel rim weight (0..1): opaque pixels whose neighbour towards the light (dirs: {'r': w, ...})
    is OUTER empty space. min_thick=2 skips parts only 1 px thick along that direction."""
    a = idx >= 0
    oe = outer_empty(a, min_gap) if outer is None else outer
    k = np.zeros(a.shape)
    for key, wt in dirs.items():
        dy, dx = _DIRS[key]
        nb = shift(oe, -dy, -dx, True)            # nb[y, x] = oe[y + dy, x + dx]
        if min_thick >= 2:
            nb &= shift(a, dy, dx, False)         # the pixel behind (away from the light) is opaque too
        k = np.maximum(k, nb * wt)
    return k * a


def rim_fn(rim_hex, s):
    """Colour transform of a rim pixel: lightness moves towards the rim colour's lightness (at least +0.05),
    hue/chroma only half as far - a lit blue cloth stays a lighter, warmer BLUE instead of turning grey-tan."""
    lr = oklab(hexrgb(rim_hex))

    def fn(c):
        lab = oklab(c)
        out = lab.copy()
        out[:, 0] = np.maximum(lab[:, 0] + (lr[0] - lab[:, 0]) * s * 0.75, lab[:, 0] + 0.05)
        out[:, 1:] = lab[:, 1:] + (lr[None, 1:] - lab[:, 1:]) * s * 0.5
        return oklab_inv(out)
    return fn


def rim_light(idx, pal, pr=None, strength=None, dirs=None, mask=None, weight=None, min_gap=3, min_thick=2,
              snap=0.018, levels=2):
    """Warm rim on the OUTER silhouette, sun-facing sides only (palette space): rim pixels get lighter and
    warmer (rim_fn) by strength (levels=2: a half-strength level where the direction weight is lower).
    Returns (idx, pal)."""
    pr = pr or PR[0]
    rm = pr.rim
    s = rm["strength"] if strength is None else strength
    k = rim_amount(idx, dirs or rm["dirs"], min_gap, min_thick)
    if mask is not None:
        k = k * mask
    if weight is not None:
        k = k * weight
    if levels >= 2:
        full = k >= 0.7
        half = (k >= 0.3) & ~full
        idx, pal = recolour(idx, pal, half, rim_fn(rm["col"], s * 0.55), snap)
    else:
        full = k >= 0.45
    idx, pal = recolour(idx, pal, full, rim_fn(rm["col"], s), snap)
    return idx, pal


def despeckle(idx, ids=None, protect=None, passes=1, min_same=0):
    """Replace orphan pixels (no 4-neighbour of the same colour) inside one object by the
    majority neighbour colour. protect: bool mask of pixels to keep."""
    for _ in range(passes):
        n = _nb4(idx, -1)
        same = (n == idx[..., None]).sum(-1)
        cand = (idx >= 0) & (same <= min_same) & (n >= 0).all(-1)
        if ids is not None:
            ni = _nb4(ids, -1)
            cand &= (ni == ids[..., None]).all(-1)
        if protect is not None:
            cand &= ~protect
        cnt = np.stack([(n == n[..., k:k + 1]).sum(-1) for k in range(4)], -1)
        pick = np.take_along_axis(n, cnt.argmax(-1)[..., None], -1)[..., 0]
        idx = np.where(cand, pick, idx)
    return idx


def inner_lines(idx, pal, ids, depth, thr=0.2, rel=0.0, steps=1, dirs=(0, 1, 2, 3), skip=None):
    """Contour lines on depth discontinuities: the farther pixel of a pair from different objects
    gets `steps` darker. skip: set of palette indices never darkened."""
    nd = _nb4(depth, 1e9)
    ni = _nb4(ids, 0)
    t = np.maximum(thr, rel * depth)
    mark = np.zeros(idx.shape, bool)
    for k in dirs:
        mark |= (ni[..., k] > 0) & (ni[..., k] != ids) & (depth - nd[..., k] > t)
    mark &= idx >= 0
    out = idx.copy()
    for i in np.unique(idx[mark]):
        if skip and i in skip:
            continue
        out[mark & (idx == i)] = pal.darker(int(i), steps)
    return out


def outer_outline(idx, pal, lit_steps=1, dark_steps=2, dark_col=None, lit_col=None, top=True, sides=True,
                  bottom=True, corners=False, light="left"):
    """Selective 1px outline outside the silhouette, hue-shifted darker tones of the neighbour (no ink).
    The shadow side (away from `light`, and below) gets dark_steps darker, the lit side lit_steps darker."""
    a = idx >= 0
    n = _nb4(idx, -1)
    out = idx.copy()
    # neighbour order: 0 above, 1 below, 2 left, 3 right (value of that neighbour)
    if light == "left":
        cand = [(0, "dark", bottom), (2, "dark", sides), (1, "lit", top), (3, "lit", sides)]
    else:
        cand = [(0, "dark", bottom), (3, "dark", sides), (1, "lit", top), (2, "lit", sides)]
    done = np.zeros_like(a)
    for k, kind, on in cand:
        if not on:
            continue
        m = (~a) & (~done) & (n[..., k] >= 0)
        if not m.any():
            continue
        src = n[..., k]
        for i in np.unique(src[m]):
            sel = m & (src == i)
            if kind == "dark":
                out[sel] = dark_col if dark_col is not None else pal.darker(int(i), dark_steps)
            else:
                out[sel] = lit_col if lit_col is not None else pal.darker(int(i), lit_steps)
        done |= m
    if corners:
        diag = np.zeros_like(a)
        diag[1:, 1:] |= a[:-1, :-1]
        diag[:-1, :-1] |= a[1:, 1:]
        diag[1:, :-1] |= a[:-1, 1:]
        diag[:-1, 1:] |= a[1:, :-1]
        m = (~a) & (~done) & diag
        out[m] = dark_col if dark_col is not None else int(np.argmin(pal.lab[:, 0]))
    return out


def remove_specks(idx, min_px=3, keep=None):
    """Drop 8-connected opaque islands smaller than min_px (broken bits of sub-pixel geometry)."""
    H_, W_ = idx.shape
    a = idx >= 0
    seen = np.zeros_like(a)
    out = idx.copy()
    ys, xs = np.nonzero(a)
    for y0, x0 in zip(ys, xs):
        if seen[y0, x0]:
            continue
        comp = [(y0, x0)]
        seen[y0, x0] = True
        k = 0
        while k < len(comp) and len(comp) <= min_px:
            y, x = comp[k]
            k += 1
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < H_ and 0 <= xx < W_ and a[yy, xx] and not seen[yy, xx]:
                        seen[yy, xx] = True
                        comp.append((yy, xx))
        if len(comp) < min_px:
            if keep is not None and any(keep[p] for p in comp):
                continue
            for p in comp:
                out[p] = -1
        else:
            while k < len(comp):
                y, x = comp[k]
                k += 1
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        yy, xx = y + dy, x + dx
                        if 0 <= yy < H_ and 0 <= xx < W_ and a[yy, xx] and not seen[yy, xx]:
                            seen[yy, xx] = True
                            comp.append((yy, xx))
    return out


def to_rgba(idx, pal):
    H_, W_ = idx.shape
    out = np.zeros((H_, W_, 4), np.float32)
    m = idx >= 0
    out[m, :3] = pal.srgb[idx[m]]
    out[m, 3] = 1.0
    return out


def save_png(arr_td, path):
    """arr top-down RGBA float -> PNG (exact 8-bit values)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    C.save_pixels(np.ascontiguousarray(arr_td[::-1]), path)


def load_png(path):
    return C.load_pixels(path)[::-1].copy()


def count_colours(rgba):
    m = rgba[..., 3] > 0.5
    q = np.round(rgba[m][:, :3] * 255).astype(np.int32)
    return len(np.unique(q[:, 0] * 65536 + q[:, 1] * 256 + q[:, 2]))


# ============================================================================ stage JSON
def stage_json(stage_id, pr=None, clouds=False, birds=None, out_dir=None):
    """Write stage_<id>.json: the game's Data/stage_<id>.json (read only) with ONLY waterTint / waterDeep
    (from the preset) and clouds / birds changed. Same keys, same order, same gameplay values."""
    pr = pr or PR[0]
    src = os.path.join(C.DATA, f"stage_{stage_id}.json")
    with open(src, encoding="utf-8") as f:
        d = json.load(f)
    d["waterTint"] = pr.water["tint"]
    d["waterDeep"] = pr.water["deep"]
    d["clouds"] = bool(clouds)
    if birds is not None:
        d["birds"] = bool(birds)
    path = os.path.join(out_dir or OUT, f"stage_{stage_id}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1)
    return d


# ============================================================================ image helpers (preview / sheets)
def upscale(arr, k):
    return np.repeat(np.repeat(arr, k, 0), k, 1)


def over(dst, src, x, y, alpha=1.0, tint=None):
    """Alpha-over src (top-down RGBA) onto dst at integer (x, y) top-left."""
    H_, W_ = dst.shape[:2]
    h, w = src.shape[:2]
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W_, x + w), min(H_, y + h)
    if x1 <= x0 or y1 <= y0:
        return dst
    s = src[y0 - y:y1 - y, x0 - x:x1 - x]
    a = s[..., 3:4] * alpha
    c = s[..., :3]
    if tint is not None:
        c = c * np.array(tint[:3], np.float32)
    d = dst[y0:y1, x0:x1]
    d[..., :3] = d[..., :3] * (1 - a) + c * a
    d[..., 3:4] = np.maximum(d[..., 3:4], a)
    return dst


def affine_nn(src, sx, sy, rot_deg):
    """Nearest-neighbour scale (sx, sy) then rotate (deg, CCW on screen) -> new RGBA, centre-preserving."""
    h, w = src.shape[:2]
    th = math.radians(rot_deg)
    c, s = math.cos(th), math.sin(th)
    corners = []
    for px, py in ((-w / 2, -h / 2), (w / 2, -h / 2), (-w / 2, h / 2), (w / 2, h / 2)):
        X, Y = px * sx, py * sy
        corners.append((X * c + Y * s, -X * s + Y * c))
    W2 = int(math.ceil(max(abs(p[0]) for p in corners) * 2)) + 2
    H2 = int(math.ceil(max(abs(p[1]) for p in corners) * 2)) + 2
    yy, xx = np.mgrid[0:H2, 0:W2].astype(np.float64)
    X = xx + 0.5 - W2 / 2
    Y = yy + 0.5 - H2 / 2
    Xr = X * c - Y * s
    Yr = X * s + Y * c
    u = Xr / sx + w / 2
    v = Yr / sy + h / 2
    ui = np.floor(u).astype(int)
    vi = np.floor(v).astype(int)
    ok = (ui >= 0) & (ui < w) & (vi >= 0) & (vi < h)
    out = np.zeros((H2, W2, 4), np.float32)
    out[ok] = src[vi[ok], ui[ok]]
    return out


def line(dst, x0, y0, x1, y1, col, width=1):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for k in range(n + 1):
        t = k / max(n, 1)
        x = int(round(x0 + (x1 - x0) * t))
        y = int(round(y0 + (y1 - y0) * t))
        for dx in range(width):
            if 0 <= y < dst.shape[0] and 0 <= x + dx < dst.shape[1]:
                dst[y, x + dx, :3] = col
                dst[y, x + dx, 3] = 1.0


def sheet(images, bg, scale=4, pad=6, cols=None):
    """images: list of top-down RGBA arrays -> one sheet (top-down), nearest upscale, bottoms aligned."""
    cols = cols or len(images)
    rows = (len(images) + cols - 1) // cols
    cw = max(i.shape[1] for i in images) * scale + pad * 2
    ch = max(i.shape[0] for i in images) * scale + pad * 2
    out = np.zeros((rows * ch, cols * cw, 4), np.float32)
    out[..., :3] = bg
    out[..., 3] = 1
    for k, im in enumerate(images):
        big = upscale(im, scale)
        r, c = divmod(k, cols)
        x = c * cw + (cw - big.shape[1]) // 2
        y = r * ch + ch - pad - big.shape[0]
        over(out, big, x, y)
    return out


def flow_sheet(rows, bg, scale=4, pad=12):
    """rows: list of lists of top-down RGBA images; each row packed left to right, bottoms aligned."""
    rw = [sum(im.shape[1] * scale + pad for im in r) + pad for r in rows]
    rh = [max(im.shape[0] for im in r) * scale + pad * 2 for r in rows]
    out = np.zeros((sum(rh), max(rw), 4), np.float32)
    out[..., :3] = bg
    out[..., 3] = 1
    y = 0
    for r, h in zip(rows, rh):
        x = pad + (max(rw) - sum(im.shape[1] * scale + pad for im in r) - pad) // 2
        for im in r:
            big = upscale(im, scale)
            over(out, big, x, y + (h - pad - big.shape[0]))
            x += big.shape[1] + pad
        y += h
    return out
