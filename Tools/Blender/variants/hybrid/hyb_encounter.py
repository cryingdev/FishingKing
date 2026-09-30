"""
hybrid - the LEGEND ENCOUNTER underwater sets (spec v1, section 3.2; per-set rows in Docs/legends_rollout.md): the
pixel-art layers of the back camera (layer 27, orthographic, 1:1 pixels), the lure billboards of the fish camera, the
front FX and the window frame.

GENERIC runner: every backdrop set is ONE module encounter_sets/<set>.py (the set id = EncounterDef.backdrop; see
encounter_sets/README.md for the module API, file names and sizes). The shared helpers and sprites live in
hyb_encounter_kit.py. Adding a set never edits this file.

Per set (from its module):
  uw_<set>_bg.png 640x400 opaque | uw_<set>_mid.png 768x200 | uw_<set>_floor.png 768x120 | uw_<set>_fore.png 256x128
  (+ optional uw_<set>_ceiling.png 768x120, uw_<set>_ray.png 64x256) | enc_frame_<set>.png 24x24 9-slice
  lure_<key>_0/1.png 16x8 for the lures the set lists (LURES)
Shared (every run, identical whichever set builds them; tinted in code):
  uw_ray 64x256 | lure_halo 24x24 | eyeshine 7x7 | silt_0..3 8x8 | bubble_s 3x3, bubble_m 5x5 | waterline 32x4
Review: encounter_sheet_<set>.png (the cave keeps its legacy encounter_sheet.png) - never installed.

Outputs: _tmp/variants/hybrid/encounter/*.png; build_hybrid.ps1 -Install copies the PNGs (not the review sheets) into
Assets/Resources/Sprites/Encounter/ (this script's --install does the same for what it built).
Run: blender -b --python variants/hybrid/hyb_encounter.py [-- <set> ... | --all] [--install]
     (no set = cave; --all = every encounter_sets/*.py not starting with "_")
"""
import os
import sys
import shutil
import importlib.util
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hyb_core as R  # noqa: E402
import numpy as np  # noqa: E402
import fk_common as C  # noqa: E402
import hyb_encounter_kit as E  # noqa: E402

SET_DIR = os.path.join(HERE, "encounter_sets")
OUT = os.path.join(R.OUT, "encounter")
WORK0 = R.WORK
INSTALL = os.path.join(C.SPRITES, "Encounter")
# layer sizes (w, h) and whether they must be opaque; "req" = every set must have it
LAYERS = {"bg": ((640, 400), True, True), "mid": ((768, 200), False, True), "floor": ((768, 120), False, False),
          "fore": ((256, 128), False, True), "ceiling": ((768, 120), False, False), "ray": ((64, 256), False, False)}


def set_ids():
    return sorted(f[:-3] for f in os.listdir(SET_DIR) if f.endswith(".py") and not f.startswith("_"))


def load_set(sid):
    path = os.path.join(SET_DIR, sid + ".py")
    if not os.path.isfile(path):
        raise SystemExit("ENC ERROR no set module %s (have: %s)" % (path, ", ".join(set_ids())))
    spec = importlib.util.spec_from_file_location("encset_" + sid, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def validate(sid, S, made):
    """Names and sizes of a set's own images (errors stop the run before anything is saved)."""
    err = []
    if getattr(S, "SET", None) != sid:
        err.append("SET must be %r" % sid)
    for kind, ((w, h), opaque, req) in LAYERS.items():
        name = "uw_%s_%s" % (sid, kind)
        if name not in made:
            if req:
                err.append("layer %s missing" % name)
            continue
        img = made[name]
        if img.shape[:2] != (h, w):
            err.append("%s is %dx%d, want %dx%d" % (name, img.shape[1], img.shape[0], w, h))
        if opaque and (img[..., 3] < 0.999).any():
            err.append("%s must be opaque" % name)
    for name in made:
        if not (name.startswith("uw_%s_" % sid) or name == "enc_frame_" + sid):
            err.append("image %s: a set only makes uw_%s_* and enc_frame_%s" % (name, sid, sid))
        elif name.startswith("uw_%s_" % sid) and name[len("uw_%s_" % sid):] not in LAYERS:
            err.append("unknown layer %s (known: %s)" % (name, ", ".join(LAYERS)))
    fr = made.get("enc_frame_" + sid)
    if fr is None:
        err.append("enc_frame_%s missing" % sid)
    elif fr.shape[:2] != (24, 24):
        err.append("enc_frame_%s must be 24x24" % sid)
    for e in err:
        print("ENC ERROR", sid, e)
    if err:
        raise SystemExit("ENC ERROR %s: %d problem(s) - nothing saved" % (sid, len(err)))


def save(img, name):
    R.save_png(img, os.path.join(OUT, name + ".png"))
    return name


def save_shared(img, name):
    """Shared sprites are identical from every set: write only when missing or different (parallel runs)."""
    path = os.path.join(OUT, name + ".png")
    if os.path.isfile(path):
        old = R.load_png(path)
        if old.shape == img.shape and np.abs(old - img).max() < 0.5 / 255:
            return name
    tmp = os.path.join(R.WORK, name + ".png")
    R.save_png(img, tmp)
    os.replace(tmp, path)
    return name


def review(S, made, sid):
    """Review sheet: the layers stacked like the back camera (REVIEW offsets of the set), then the sprites 4x."""
    rv = getattr(S, "REVIEW", {})
    comp = np.zeros((400, 640, 4), np.float32)
    comp[:] = made["uw_%s_bg" % sid]
    ray = (made.get("uw_%s_ray" % sid) if "uw_%s_ray" % sid in made else made["uw_ray"]).copy()
    tint, a, rx, ry = rv.get("ray", ("#ffffff", 0.24, 470, 0))
    ray[..., :3] *= E.rgb(tint)
    R.over(comp, ray, rx, ry, alpha=a)
    hz = getattr(S, "HORIZON", 150)
    for kind, default in (("ceiling", (-64, hz - 120)), ("mid", (-64, hz - 200)), ("floor", (-64, hz)),
                          ("fore", (0, 400 - 128))):
        name = "uw_%s_%s" % (sid, kind)
        if name in made:
            x, y = rv.get(kind, default)
            R.over(comp, made[name], x, y)
    small = [made[k] for k in made if not k.startswith("uw_")]
    row = R.flow_sheet([small], (0.1, 0.12, 0.16), scale=4, pad=8)
    sheet = np.zeros((comp.shape[0] + row.shape[0] + 8, max(comp.shape[1], row.shape[1]), 4), np.float32)
    sheet[..., :3] = 0.1
    sheet[..., 3] = 1
    R.over(sheet, comp, 0, 0)
    R.over(sheet, row, 0, comp.shape[0] + 8)
    R.save_png(sheet, os.path.join(OUT, getattr(S, "SHEET", "encounter_sheet_%s.png" % sid)))


def build_set(sid, install):
    S = load_set(sid)
    R.WORK = os.path.join(WORK0, "encounter", sid)          # scratch passes of this set only (parallel runs)
    os.makedirs(R.WORK, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    R.use_preset(getattr(S, "PRESET", sid))
    own = dict(S.layers())
    own["enc_frame_" + sid] = S.frame()
    validate(sid, S, own)
    frame = own.pop("enc_frame_" + sid)
    made = {}
    made.update(own)
    made.update(E.lure_frames(getattr(S, "LURES", []), getattr(S, "LURE_TWEAKS", {})))
    shared = E.shared_sprites()
    for k, v in made.items():
        save(v, k)
    save(frame, "enc_frame_" + sid)
    for k, v in shared.items():
        save_shared(v, k)
    # print / review order: the set's layers, the shared ray, the lures, the shared small sprites, the frame
    order = dict(own)
    order["uw_ray"] = shared["uw_ray"]
    order.update({k: v for k, v in made.items() if k.startswith("lure_")})
    order.update({k: v for k, v in shared.items() if k != "uw_ray"})
    order["enc_frame_" + sid] = frame
    for k, v in order.items():
        print("ENC", k, v.shape[1], "x", v.shape[0], "colours", R.count_colours(v))
    review(S, order, sid)
    if install:
        os.makedirs(INSTALL, exist_ok=True)
        for k in order:
            shutil.copy2(os.path.join(OUT, k + ".png"), os.path.join(INSTALL, k + ".png"))
        print("ENC installed", len(order), "->", INSTALL)
    shutil.rmtree(R.WORK, ignore_errors=True)
    print("ENC done", sid, len(order))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    sids = set_ids() if "--all" in argv else ([a for a in argv if not a.startswith("--")] or ["cave"])
    for sid in sids:
        build_set(sid, "--install" in argv)


if __name__ == "__main__":
    main()
