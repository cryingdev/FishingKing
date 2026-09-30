"""
hybrid - TIME-OF-DAY PERIODS of the stage renders (Docs/time_currents_spec.md, sections 2-3 and 12).

Every stage script hyb_<stage>.py renders ONE look per run. This helper decides which, and where it goes:

  no --period        LEGACY: the stage's look of today -> _tmp/variants/hybrid/<stage>_back.png, <stage>_front.png,
                     stage_<stage>.json, exactly as before (build_hybrid.ps1 -Install copies those).
  --period <p>       p = dawn | day | evening | night: the stage's own preset (its R.use_preset overrides) plus
                     periods/<stage>.py PERIODS[p] on top ->
                       _tmp/variants/hybrid/periods/<stage>_<p>_back.png, _front.png, <stage>_<p>.json (look)
                       and, unless --dry, Assets/Resources/Sprites/Stages/<stage>_<p>_back.png / _front.png and
                       Assets/Resources/Data/Periods/<stage>_<p>.json.
                     It NEVER writes a legacy name (<stage>_back.png / _front.png / stage_<stage>.json).
                     The stage's NATIVE period (the look it has today: NATIVE below) must reproduce today's images
                     byte for byte: save() checks it against Sprites/Stages/<stage>_<layer>.png and prints
                     "HYB PERIOD CHECK ... identical" (or how many pixels differ).

Run (from Tools/Blender):  blender -b --python variants/hybrid/hyb_<stage>.py -- [back|front|json] --period <p> [--dry]
                           (json = no render, only the look json; front before back: the back reads the front's
                           overlay from the same scratch dir)
Scratch: _tmp/variants/hybrid/work/periods/<stage>_<p>/ (parallel runs of different stages / periods never collide).

Stage scripts use, in this order:
  PR = PER.use_preset(SID, **the stage's own overrides)       # instead of R.use_preset(SID, ...)
  R.WORK = PER.work(SID, <its legacy scratch dir>)            # before anything reads R.WORK
  PER.const(name, default)                                    # stage constants a period may change (CONSTS)
  PER.hook("back_scene" | "front_scene", ...ctx)              # after building the geometry (period mode only)
  idx, pal = PER.hook("back_post" | "front_post", idx, pal, ...ctx)   # just before R.to_rgba
  PER.save(img, SID, "back" | "front")                        # instead of R.save_png(img, <legacy path>)
  d = PER.stage_json(SID, PR, clouds=..., birds=...)          # instead of R.stage_json
  which = PER.which()                                         # instead of parsing sys.argv
The period module API is in periods/README.md. Never edit this file or hyb_core.py from a stage task.
"""
import os
import sys
import json
import importlib.util

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402
import fk_common as C  # noqa: E402

PERIODS = ("dawn", "day", "evening", "night")
KO = {"dawn": "새벽", "day": "낮", "evening": "저녁", "night": "밤"}
# the look every stage has today = the period it already depicts (spec 2.3)
NATIVE = {"lake": "evening", "stream": "dawn", "sea": "day", "swamp": "evening", "ice": "evening", "ocean": "dawn",
          "cave": "day"}
PERIOD_DIR = os.path.join(HERE, "periods")
OUT = os.path.join(R.OUT, "periods")
INSTALL_SPR = os.path.join(C.SPRITES, "Stages")
INSTALL_DATA = os.path.join(C.DATA, "Periods")

# Unity look defaults per period (spec 5.1); periods/<stage>.py LOOK[p] overrides any of them
LOOK_DEFAULTS = {
    "dawn": dict(actorTint="#e8e2ee", glintCol="#ffe6e0", glintDensity=0.6, fxAlpha=0.9, birds=True, fireflies=False),
    "day": dict(actorTint="#ffffff", glintCol="#ffffff", glintDensity=1.0, fxAlpha=1.0, birds=True, fireflies=False),
    "evening": dict(actorTint="#ffffff", glintCol="#ffe0b0", glintDensity=0.8, fxAlpha=0.9, birds=True, fireflies=False),
    "night": dict(actorTint="#8e9ac0", glintCol="#c8d4f0", glintDensity=0.3, fxAlpha=0.7, birds=False, fireflies=False),
}
_DIRS = {"r": (1, 0), "tr": (1, 1), "t": (0, 1), "tl": (-1, 1), "l": (-1, 0), "b": (0, -1), "br": (1, -1), "bl": (-1, -1)}


# ============================================================================ command line
def _parse(argv):
    rest = argv[argv.index("--") + 1:] if "--" in argv else []
    period, dry, which = None, False, []
    i = 0
    while i < len(rest):
        a = rest[i]
        if a == "--period" and i + 1 < len(rest):
            period = rest[i + 1].lower()
            i += 2
            continue
        if a.startswith("--period="):
            period = a.split("=", 1)[1].lower()
        elif a == "--dry":
            dry = True
        elif a in ("back", "front", "json"):
            which.append(a)
        else:
            # a stray word must never turn into "render nothing" (or an install that was meant to be dry)
            raise SystemExit("HYB PERIOD ERROR unknown argument %r after '--' (use back | front | json, --period <p>, "
                             "--dry)" % a)
        i += 1
    if period is not None and period not in PERIODS:
        raise SystemExit("HYB PERIOD ERROR unknown period %r (use %s)" % (period, " | ".join(PERIODS)))
    return period, dry, which


ACTIVE, DRY, _WHICH = _parse(sys.argv)
_STAGE = [None]
_MOD = {}


def period():
    """The period this run renders (None = legacy run)."""
    return ACTIVE


def which():
    """The layers to render: 'back' / 'front' given after '--' (both when none is given; 'json' = neither, only the
    look json of a period run)."""
    return _WHICH or ["back", "front"]


def is_native(stage):
    return ACTIVE is None or ACTIVE == NATIVE[stage]


def module(stage):
    """periods/<stage>.py (loaded once)."""
    if stage not in _MOD:
        path = os.path.join(PERIOD_DIR, stage + ".py")
        if not os.path.isfile(path):
            raise SystemExit("HYB PERIOD ERROR no period module %s" % path)
        spec = importlib.util.spec_from_file_location("period_" + stage, path)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        if getattr(mod, "NATIVE", None) != NATIVE[stage]:
            raise SystemExit("HYB PERIOD ERROR periods/%s.py NATIVE must be %r" % (stage, NATIVE[stage]))
        if mod.PERIODS.get(NATIVE[stage]):
            raise SystemExit("HYB PERIOD ERROR periods/%s.py: the native period %r must stay {} (today's look)"
                             % (stage, NATIVE[stage]))
        _MOD[stage] = mod
    return _MOD[stage]


# ============================================================================ preset / constants / scratch
def use_preset(stage, **stage_overrides):
    """R.use_preset(stage, **stage_overrides), plus periods/<stage>.py PERIODS[period] on top in period mode (sub-dicts
    merge; a dict with "__whole__": True replaces the sub-dict; None removes a feature)."""
    _STAGE[0] = stage
    base = R.PRESETS[stage].copy(**stage_overrides)
    _NATIVE_PR[0] = base
    if ACTIVE is None:
        return R.use_preset(stage, **stage_overrides)
    over = module(stage).PERIODS.get(ACTIVE)
    if over is None:
        raise SystemExit("HYB PERIOD ERROR periods/%s.py has no PERIODS[%r]" % (stage, ACTIVE))
    pr = R.use_preset(base, **over)
    print("HYB PERIOD", stage, ACTIVE, "(native)" if is_native(stage) else "", "overrides", sorted(over))
    return pr


_NATIVE_PR = [None]


def native_pr():
    """The stage's NATIVE preset (today's look, with the stage's own overrides), never activated: use it for
    GEOMETRY anchors (a valley under the sun, keep-out boxes), which must be the same in every period, since the four
    renders cross-fade into each other and Unity builds the water mask once."""
    if _NATIVE_PR[0] is None:
        raise SystemExit("HYB PERIOD ERROR native_pr() before use_preset()")
    return _NATIVE_PR[0]


def work(stage, legacy_dir):
    """The scratch dir for EXR passes / overlays: legacy_dir in a legacy run, else work/periods/<stage>_<period>."""
    d = legacy_dir if ACTIVE is None else os.path.join(R.OUT, "work", "periods", "%s_%s" % (stage, ACTIVE))
    os.makedirs(d, exist_ok=True)
    return d


def const(name, default):
    """A stage-script constant a period may change (periods/<stage>.py CONSTS[period][name]); default otherwise."""
    st = _STAGE[0]
    if ACTIVE is None or st is None or is_native(st):
        return default
    return getattr(module(st), "CONSTS", {}).get(ACTIVE, {}).get(name, default)


def hook(name, *args, **ctx):
    """Calls periods/<stage>.py <name>(*args, ctx) in a non-native period run; returns its result, else the args
    unchanged (one arg -> that arg, several -> the tuple, none -> None)."""
    st = _STAGE[0]
    passthru = args[0] if len(args) == 1 else (args if args else None)
    if ACTIVE is None or st is None or is_native(st):
        return passthru
    fn = getattr(module(st), name, None)
    if fn is None:
        return passthru
    ctx.update(stage=st, period=ACTIVE, R=R, W=R.W, H=R.H)
    return fn(*args, ctx)


# ============================================================================ outputs
def _load(path):
    return np.round(R.load_png(path) * 255).astype(np.int32)


def save(img, stage, layer):
    """Legacy: R.OUT/<stage>_<layer>.png. Period: periods/<stage>_<p>_<layer>.png (+ Sprites/Stages unless --dry),
    then the checks: native period vs today's Sprites/Stages/<stage>_<layer>.png; a front layer's silhouette vs
    today's front (geometry must not change between periods; lights may add a few pixels)."""
    if ACTIVE is None:
        path = os.path.join(R.OUT, "%s_%s.png" % (stage, layer))
        R.save_png(img, path)
        return path
    name = "%s_%s_%s.png" % (stage, ACTIVE, layer)
    path = os.path.join(OUT, name)
    R.save_png(img, path)
    if not DRY:
        R.save_png(img, os.path.join(INSTALL_SPR, name))
    today = os.path.join(INSTALL_SPR, "%s_%s.png" % (stage, layer))
    if os.path.isfile(today):
        a, b = _load(path), _load(today)
        if a.shape != b.shape:
            print("HYB PERIOD CHECK", stage, ACTIVE, layer, "size", a.shape, "differs from", os.path.basename(today))
        elif is_native(stage):
            n = int((np.abs(a - b).max(-1) > 0).sum())
            print("HYB PERIOD CHECK", stage, ACTIVE, layer, "native:",
                  "identical to %s" % os.path.basename(today) if n == 0 else "%d px differ from %s" % (n, os.path.basename(today)))
        elif layer == "front":
            n = int(((a[..., 3] > 127) != (b[..., 3] > 127)).sum())
            print("HYB PERIOD CHECK", stage, ACTIVE, "front silhouette:", n, "px differ from", os.path.basename(today),
                  "(0 = same geometry; only added lights may differ)")
    print("HYB PERIOD saved", path, "" if DRY else "+ installed")
    return path


def _stage_dict(stage, pr, clouds, birds):
    """What R.stage_json writes (same keys, same order), without writing it."""
    with open(os.path.join(C.DATA, "stage_%s.json" % stage), encoding="utf-8") as f:
        d = json.load(f)
    d["waterTint"] = pr.water["tint"]
    d["waterDeep"] = pr.water["deep"]
    d["clouds"] = bool(clouds)
    if birds is not None:
        d["birds"] = bool(birds)
    return d


def look(stage, pr):
    """The Unity look of this period (Data/Periods/<stage>_<p>.json, spec 5): water, rim, key light from the preset,
    the rest from LOOK_DEFAULTS[p] and periods/<stage>.py LOOK[p]."""
    p = ACTIVE
    if is_native(stage):
        # the native look = the game as it is today (white actor, white glints, the stage json's birds / fireflies)
        with open(os.path.join(C.DATA, "stage_%s.json" % stage), encoding="utf-8") as f:
            base = json.load(f)
        lk = dict(actorTint="#ffffff", glintCol="#ffffff", glintDensity=1.0, fxAlpha=1.0,
                  birds=bool(base.get("birds", False)), fireflies=bool(base.get("fireflies", False)))
    else:
        lk = dict(LOOK_DEFAULTS[p])
    lk.update(getattr(module(stage), "LOOK", {}).get(p, {}))
    rim = pr.rim
    dirs = []
    for k, w in sorted(rim["dirs"].items(), key=lambda kv: -kv[1])[:3]:
        dirs += [float(_DIRS[k][0]), float(_DIRS[k][1]), float(w)]
    kd = [float(v) for v in pr.key_dir]
    # keyDir in UNITY axes (x right, y up, z forward from the angler), towards the light; the preset's is Blender's
    out = dict(stage=stage, period=p, native=is_native(stage), waterTint=pr.water["tint"], waterDeep=pr.water["deep"],
               rimCol=rim["col"], rimStrength=float(rim["strength"]), rimDirs=dirs,
               keyDir=[kd[0], kd[2], kd[1]], keyCol=pr.key_col, skyHorizon=pr.sky[0][1],
               skyZenith=pr.sky[-1][1])
    for k in ("actorTint", "glintCol", "glintDensity", "fxAlpha", "birds", "fireflies"):
        out[k] = lk[k]
    out["lights"] = [dict(x=float(q["x"]), y=float(q["y"]), col=q["col"], r=float(q.get("r", 3)),
                          blink=q.get("blink", "fixed")) for q in lk.get("lights", [])]
    return out


def stage_json(stage, pr, clouds=False, birds=None):
    """Legacy: R.stage_json. Period: returns the same dict (not written) and writes the look json instead."""
    if ACTIVE is None:
        return R.stage_json(stage, pr, clouds=clouds, birds=birds)
    d = _stage_dict(stage, pr, clouds, birds)
    lk = look(stage, pr)
    name = "%s_%s.json" % (stage, ACTIVE)
    os.makedirs(OUT, exist_ok=True)
    txt = json.dumps(lk, indent=1, ensure_ascii=False)
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        f.write(txt)
    if not DRY:
        os.makedirs(INSTALL_DATA, exist_ok=True)
        with open(os.path.join(INSTALL_DATA, name), "w", encoding="utf-8") as f:
            f.write(txt)
    print("HYB PERIOD look", name, {k: lk[k] for k in ("waterTint", "waterDeep", "rimCol", "rimStrength", "actorTint")})
    return d


# ============================================================================ helpers for night lights (hooks)
def point_glow(idx, pal, c, r, col, rings=((2.5, 0.7), (5.0, 0.4), (9.0, 0.18)), mask=None, flat=1.0, snap=0.03):
    """Flat concentric glow rings (level = sRGB mix towards col, always a little lighter) around the screen point
    (c, r) of the 640x400 canvas, in palette space: a lit window / lantern / lamp. mask limits it (e.g. rock, water)."""
    H_, W_ = idx.shape
    rows = np.arange(H_)[:, None] + 0.5
    cols = np.arange(W_)[None, :] + 0.5
    d = np.sqrt(((cols - c) * flat) ** 2 + (rows - r) ** 2)
    done = np.zeros((H_, W_), bool)
    for rad, level in sorted(rings, key=lambda q: q[0]):
        m = (d < rad) & ~done & (idx >= 0)
        if mask is not None:
            m &= mask
        idx, pal = R.blend_idx(idx, pal, m, col, level, lighter=True, snap=snap, space="srgb")
        done |= d < rad
    return idx, pal


def light_streak(idx, pal, water, c, r0, r1, col, half=(1.0, 3.0), dens=0.55, seed=5, snap=0.03):
    """A light's broken reflection on the water under it: short horizontal dashes from row r0 (the light's
    waterline) down to r1, half width growing from half[0] to half[1] px, thinning out with distance from r0."""
    rng = np.random.default_rng(seed)
    m = np.zeros(idx.shape, bool)
    for r in range(int(r0), int(min(r1, idx.shape[0]))):
        t = (r - r0) / max(1.0, r1 - r0)
        if rng.random() > dens * (1 - 0.7 * t):
            continue
        hw = half[0] + (half[1] - half[0]) * t
        x0 = int(round(c - hw + rng.uniform(-1, 1)))
        m[r, max(0, x0):max(0, int(round(x0 + 2 * hw)))] = True
    return R.blend_idx(idx, pal, m & water, col, 0.6, lighter=True, snap=snap, space="srgb")
