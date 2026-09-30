"""
hybrid - REVIEW renders of a legend model and a MOCK of the legend encounter window (not game assets). Needs the
outputs of hyb_legend3d.py (legend_<id>.fbx + palette) and hyb_encounter.py (the uw_<set>_* layers and sprites).

GENERIC: the legend's module (legends/<fish_id>.py) supplies the beats (preview_beats), the turntable poses
(TURNTABLE), the lure (PREVIEW_LURE), the size (PREVIEW_CM) and the lure-light radius (LURE_R); its backdrop set
(encounter_sets/<BACKDROP>.py) supplies the look (PROFILE: camera, line, tints, rim, clear colour) and the layers.
Adding a legend never edits this file.

  _tmp/legend_art/legend_<id>_turntable.png
      row 1  key-lit palette check at the close-pass scale (toon ramps, 1 px outline), 3x:
             side (right flank) | 3/4 front | mouth open (TURNTABLE["open"]) | top, swim bend (TURNTABLE["bend"])
      row 2  the encounter lighting (ActorToon lure light, R = LURE_R, abyss / fog outline of the palette's
             _lureLight, the set's rim) on the set's clear colour: lure at the head | lure at mid-body |
             silhouette (lure 2.6 m away) | eyes only
      row 3  game scale at the typical 3 m (1 px = 1 px, shown 3x): side | 3/4 front | tail-on | lit
  _tmp/legend_art/<prefix>_encounter_mock.png   the beats at the pixel-view size 480x270, shown 2x
  _tmp/legend_art/<prefix>_mock_<n>_<beat>.png  the same frames at 1x
      (<prefix> = the module's PREVIEW_PREFIX, default legend_<id>; the coelacanth keeps the legacy "legend")

Mock staging follows spec 2.4 - 2.7 (window 288x136, 10 px below the top, centred; dim 0.55; frame 3 px outside
the crop; principal point at the window centre, f = (136/2)/tan 21 deg = 177 px; back layers 1:1 (bg fixed, mid on
the floor horizon, floor under it, fore bottom-left, ray top-right, halo on the lure); fish quad with the set's rim;
eyeshine held >= 6 px apart). The set camera is placed so the lure lands at PROFILE["frame_at"] of the window
(default (0.33 W, 0.30 H)).
Run: blender -b --python variants/hybrid/hyb_legend_preview.py [-- <fish_id>]   (default coelacanth)
"""
import os
import sys
import json
import math
import types
sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hyb_core as R  # noqa: E402
import hyb_legend3d as LG  # noqa: E402
import hyb_encounter as ENC_RUN  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
from bpy_extras.object_utils import world_to_camera_view  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

V = Vector
ART = os.path.join(R.BL, "_tmp", "legend_art")
ENC = os.path.join(R.OUT, "encounter")
VW, VH = 480, 270                          # pixel view
WIN = (96, 10, 288, 136)                   # window crop (x, y top-down, w, h)
PP = (WIN[0] + WIN[2] / 2, WIN[1] + WIN[3] / 2)       # principal point (top-down px) = window centre
FPX = (WIN[3] / 2) / math.tan(math.radians(21))       # 177.1
ME = sys.modules[__name__]

# ---- configured per legend by configure(fid) (the legend module + its backdrop set's PROFILE)
LEG = None                                 # the legend module
SETM = None                                # the encounter set module
SET = "cave"
STAGE = "cave"                             # the surface stage (sprites Stages/<stage>_back / _front)
PROF = {}
SETP = None                                # PROFILE as attributes (SETP.line_up ...)
RIM = None                                 # (colour, strength, dirs) of the fish quad
LURE_R = 3.0                               # lure-light radius
CAM = V((-1.2, 0.45, -2.7))                # set frame: lure rest point = origin, floor y = 0
EYE_CORE, EYE_GLOW = "#f0ffd8", "#b8ff8a"
LGBG_HORIZON = 150                         # the set's bg haze-band row (encounter_sets/<set>.HORIZON)


def configure(fid):
    global LEG, SETM, SET, PROF, SETP, RIM, LURE_R, CAM, EYE_CORE, EYE_GLOW, LGBG_HORIZON, STAGE
    L = LG.LEGENDS[fid]
    LEG = L["module"]
    STAGE = L["stage"] or "cave"
    SET = L["backdrop"] or "cave"
    SETM = ENC_RUN.load_set(SET)
    PROF = dict(getattr(SETM, "PROFILE", {}))
    SETP = types.SimpleNamespace(**PROF)
    RIM = PROF.get("rim")
    LURE_R = getattr(LEG, "LURE_R", 3.0)
    CAM = V(PROF.get("cam", (-1.2, 0.45, -2.7)))
    EYE_CORE, EYE_GLOW = L["lure_light"]["eyeCore"], L["lure_light"]["eyeGlow"]
    LGBG_HORIZON = getattr(SETM, "HORIZON", 150)
    LG.WORK = os.path.join(R.BL, "_tmp", "actors3d", "_work_preview_" + fid)


def load(name):
    return R.load_png(os.path.join(ENC, name + ".png"))


def load_opt(name):
    path = os.path.join(ENC, name + ".png")
    return R.load_png(path) if os.path.isfile(path) else None


def tint(img, hexc, a=1.0):
    o = img.copy()
    o[..., :3] *= R.hexrgb(hexc).astype(np.float32)
    o[..., 3] *= a
    return o


def cam_target():
    """Look-at so the lure (origin) projects to PROFILE["frame_at"] (default (0.33 W, 0.30 H)) of the window."""
    fx, fy = PROF.get("frame_at", (0.33, 0.30))
    dx, dy = fx * WIN[2] - WIN[2] / 2, fy * WIN[3] - WIN[3] / 2          # px from the window centre (y up)
    to = -CAM
    yaw_l = math.atan2(to.x, to.z)
    pitch_l = math.atan2(to.y, math.hypot(to.x, to.z))
    yaw = yaw_l - math.atan2(dx, FPX)
    pitch = pitch_l - math.atan2(dy, FPX)
    d = V((math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch)))
    return CAM + d * 4.0


def setcam(pos=None, target=None, f=FPX, pp=PP):
    """The set camera (Unity-like set frame) with the principal point at the window centre."""
    pos = CAM if pos is None else pos
    target = cam_target() if target is None else target
    return LG.cam_look(pos, target, f, VW, VH, shift_px=(pp[0] - VW / 2, -(pp[1] - VH / 2)))


def project(cam, p_u, w=VW, h=VH):
    """Set-frame (Unity-like) point -> top-down pixel (x, y) of the current render, and view depth."""
    co = world_to_camera_view(bpy.context.scene, cam, LG.C_UW @ V(p_u))
    return co.x * w, (1 - co.y) * h, co.z


def eye_facing(ob):
    return (ob.matrix_world.to_3x3() @ V((0, 0, 1))).normalized()


# ---------------------------------------------------------------- scene
class Scene:
    def __init__(self, fid):
        C.reset_scene()
        R.reset_materials()
        self.L = LG.LEGENDS[fid]
        self.objs = LG.import_fbx(os.path.join(LG.OUT, self.L["model"] + ".fbx"))
        self.arm = next(o for o in self.objs if o.type == "ARMATURE")
        self.mount = LG.mount_unity(self.objs, Matrix(), "FishMount")
        self.T = LG.Toon(fid, self.objs)
        self.eyes = {s: next(o for o in self.objs if LG.base_name(o.name) == "Eye." + s) for s in ("L", "R")}
        self.cm = getattr(LEG, "PREVIEW_CM", 160)

    def place(self, pos_u, heading_deg, bank=0.0):
        s = self.cm / 100.0
        M = (Matrix.Translation(V(pos_u)) @ Matrix.Rotation(math.radians(heading_deg), 4, "Y")
             @ Matrix.Rotation(math.radians(bank), 4, "Z") @ Matrix.Scale(s, 4))
        Cm = LG.C_UW.to_4x4()
        self.mount.matrix_world = Cm @ M @ Cm.inverted()
        bpy.context.view_layer.update()

    def pose(self, **bones):
        """Bone name with "_" for "." (Pec_L_Fan = Pec.L.Fan) -> (ax, ay, az) Unity Euler degrees, or
        {"rot": (...), "move": (dx, dy, dz)} (a protrusion, model metres). Bones the model lacks are skipped."""
        LG.reset_pose(self.arm)
        LG.apply_poses(self.arm, {b.replace("_", "."): v for b, v in bones.items()})
        bpy.context.view_layer.update()

    def show(self, eyes_only=False):
        for o in self.T.meshes:
            o.hide_render = eyes_only and not LG.base_name(o.name).startswith("geo_Eye")

    def eyes_px(self, cam, w=VW, h=VH):
        """Projected eye centres + eyeshine alpha (smoothstep(-0.3, 0.3, facing . to-camera))."""
        out = []
        cw = cam.matrix_world.translation
        for s in ("L", "R"):
            e = self.eyes[s]
            p = e.matrix_world.translation
            x, y, z = world_to_camera_view(bpy.context.scene, cam, p)
            f = eye_facing(e)
            d = f.dot((cw - p).normalized())
            t = min(1.0, max(0.0, (d + 0.3) / 0.6))
            out.append((x * w, (1 - y) * h, z, t * t * (3 - 2 * t)))
        return out


def render_fish(sc, cam, lure_u, tag, rim=True, eyes_only=False, lure_on=True):
    sc.show(eyes_only)
    lw = LG.C_UW @ V(lure_u)
    img = sc.T.render(tag, lure=(tuple(lw), LURE_R) if lure_on else None, light=(0.3, -0.5, 0.8),
                      rim=RIM if rim else None)
    sc.show(False)
    return img


# ---------------------------------------------------------------- 2D layers
def stage_stand(stage):
    try:
        with open(os.path.join(C.ROOT, "Assets", "Resources", "Data", "stage_%s.json" % stage), encoding="utf-8") as f:
            return float(json.load(f)["standH"])
    except Exception:
        return 1.2


def surface_scene():
    """The legend's stage at the pixel-view size with the angler (reel pose), as the dimmed backdrop of the window.
    -> (img, angler, angler_xy, rod tip, line entry)."""
    stage = STAGE
    back = R.load_png(os.path.join(C.SPRITES, "Stages", stage + "_back.png"))
    front = R.load_png(os.path.join(C.SPRITES, "Stages", stage + "_front.png"))
    x0, y0 = R.CROP[0], R.CROP[1]
    img = back[y0:y0 + VH, x0:x0 + VW].copy()
    R.over(img, front[y0:y0 + VH, x0:x0 + VW], 0, 0)
    ang = R.load_png(os.path.join(C.SPRITES, "Character", "angler_reel.png"))
    stand = stage_stand(stage)
    fx, fy, _ = P.project((0.0, 0.0, stand), stand)
    feet = (P.W / 2 + fx - x0, P.H / 2 - fy - y0)
    ax, ay = int(round(feet[0] - ang.shape[1] / 2)), int(round(feet[1] - (ang.shape[0] - 10)))
    ex, ey, _ = P.project(tuple(PROF.get("entry", (0.0, 16.0, 0.0))), stand)          # where the line enters
    entry = (P.W / 2 + ex - x0, P.H / 2 - ey - y0)
    a = ang[..., 3] > 0.5
    ys, xs = np.nonzero(a[:, ang.shape[1] // 2:])
    k = np.argmin(ys)
    tip = (ax + ang.shape[1] // 2 + xs[k], ay + ys[k])
    return img, ang, (ax, ay), tip, entry


def back_layers(lure_px, horizon_row, yaw_px=0.0, ceil_row=None):
    """The back camera's RT at the view size: bg fixed 1:1, ray, (ceiling), mid on the floor horizon, floor, halo,
    fore. ceil_row: the projected far edge of the surface (surface sets; None = no ceiling layer)."""
    img = np.zeros((VH, VW, 4), np.float32)
    img[..., :3] = R.hexrgb(PROF.get("clear", "#04101a"))
    img[..., 3] = 1
    bg = load("uw_%s_bg" % SET)
    R.over(img, bg, -80 - int(0.1 * yaw_px), -int(round(LGBG_HORIZON - horizon_row)))
    ray_src = load_opt("uw_%s_ray" % SET)
    ray_src = ray_src if ray_src is not None else load("uw_ray")
    for rt, ra, rx, ry in PROF.get("rays", [PROF.get("ray", ("#86dcff", 0.08, 150, -20))]):
        if ra > 0:                                  # (tint, alpha, x from the right edge, y) per ray
            R.over(img, tint(ray_src, rt, ra), VW - rx - int(0.2 * yaw_px), ry)
    ceil = load_opt("uw_%s_ceiling" % SET) if ceil_row is not None else None
    if ceil is not None:
        R.over(img, ceil, int(VW / 2 - ceil.shape[1] / 2 - 0.6 * yaw_px), int(ceil_row - ceil.shape[0]))
    mid = load("uw_%s_mid" % SET)
    R.over(img, mid, int(VW / 2 - mid.shape[1] / 2 - 0.35 * yaw_px), int(horizon_row - mid.shape[0]))
    fl = load_opt("uw_%s_floor" % SET)
    if fl is not None:
        R.over(img, fl, int(VW / 2 - fl.shape[1] / 2 - 0.8 * yaw_px), int(horizon_row))
    if lure_px is not None and PROF.get("halo"):
        halo = tint(load("lure_halo"), PROF["halo"], PROF.get("halo_alpha", 1.0))
        R.over(img, halo, int(round(lure_px[0] - 12)), int(round(lure_px[1] - 12)))
    fore = load("uw_%s_fore" % SET)
    R.over(img, fore, int(-1.3 * yaw_px) + PROF.get("fore_x", -20), VH - fore.shape[0])
    return img


def front_fx(img, lure_px, lure_up_px, rng, snow=12, bubbles=0):
    """Near marine snow, the line to the lure, bubbles."""
    for _ in range(snow):
        x, y = rng.randint(0, VW - 1), rng.randint(0, VH - 1)
        a = rng.uniform(0.3, 0.5)
        img[y, x, :3] = img[y, x, :3] * (1 - a) + R.hexrgb(PROF.get("snow_near", "#9fd8e0")) * a
    if lure_up_px is not None and lure_px is not None:
        (x0, y0), (x1, y1) = lure_up_px, lure_px
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        col = R.hexrgb(PROF.get("line", "#8fb8c0"))
        for i in range(n + 1):
            t = i / max(1, n)
            x = x0 + (x1 - x0) * t
            y = y0 + (y1 - y0) * t + 3 * 4 * t * (1 - t)        # 3 px quadratic sag
            xi, yi = int(round(x)), int(round(y))
            if 0 <= xi < VW and 0 <= yi < VH:
                img[yi, xi, :3] = img[yi, xi, :3] * 0.4 + col * 0.6
    if bubbles and lure_px is not None:
        bs, bm = load("bubble_s"), load("bubble_m")
        for i in range(bubbles):
            b = bm if i % 3 == 0 else bs
            R.over(img, b, int(lure_px[0] + rng.randint(-5, 6)), int(lure_px[1] - 6 - i * 5 - rng.randint(0, 3)))
    return img


def put_lure(img, lure_px, key="egi", frame=0, scale=1):
    s = load(f"lure_{key}_{frame}")
    if scale > 1:
        s = R.upscale(s, scale)
    R.over(img, s, int(round(lure_px[0] - s.shape[1] / 2)), int(round(lure_px[1] - s.shape[0] / 2)))


def put_eyeshine(img, eyes, mult=1.0, min_gap=6):
    es = load("eyeshine")
    pts = [(x, y, a) for x, y, z, a in eyes if z > 0]
    if len(pts) == 2:
        (x0, y0, a0), (x1, y1, a1) = pts
        d = math.hypot(x1 - x0, y1 - y0)
        if d < min_gap:                               # hold them >= min_gap apart along the projected eye axis
            ux, uy = ((x1 - x0) / d, (y1 - y0) / d) if d > 1e-3 else (1.0, 0.0)
            cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
            pts = [(cx - ux * min_gap / 2, cy - uy * min_gap / 2, a0), (cx + ux * min_gap / 2, cy + uy * min_gap / 2, a1)]
    for x, y, a in pts:
        if a * mult <= 0.02:
            continue
        halo = es.copy()
        core = halo[..., 3] >= 0.99
        halo[..., :3] = np.where(core[..., None], R.hexrgb(EYE_CORE), R.hexrgb(EYE_GLOW))
        R.over(img, halo, int(round(x - 3)), int(round(y - 3)), alpha=a * mult)


def nine_slice(w, h, spr, b=6):
    out = np.zeros((h, w, 4), np.float32)
    n = spr.shape[0]
    for (sy0, sy1, dy0, dy1) in ((0, b, 0, b), (b, n - b, b, h - b), (n - b, n, h - b, h)):
        for (sx0, sx1, dx0, dx1) in ((0, b, 0, b), (b, n - b, b, w - b), (n - b, n, w - b, w)):
            src = spr[sy0:sy1, sx0:sx1]
            ys = (np.arange(dy1 - dy0) * (sy1 - sy0) // max(1, dy1 - dy0)) + 0
            xs = (np.arange(dx1 - dx0) * (sx1 - sx0) // max(1, dx1 - dx0)) + 0
            out[dy0:dy1, dx0:dx1] = src[ys][:, xs]
    return out


def compose_window(surface, ang, ang_xy, tip, entry, under, crop, frame=True, dim=0.55):
    img = surface.copy()
    # the line from the rod tip to the water, then the dim (order 45) under the angler / rod / line
    img[..., :3] *= (1 - dim)
    R.line(img, tip[0], tip[1], entry[0], entry[1], R.hexrgb("#c8d8dc") * 0.8)
    R.over(img, ang, ang_xy[0], ang_xy[1])
    x, y, w, h = crop
    img[y:y + h, x:x + w] = under[y:y + h, x:x + w]
    if frame:
        fr = nine_slice(w + 6, h + 6, load("enc_frame_" + SET))
        R.over(img, fr, x - 3, y - 3)
    return img


# ---------------------------------------------------------------- mock
def mock(fid):
    sc = Scene(fid)
    surface = surface_scene()
    cam = setcam()
    lure = V(PROF.get("lure_rest", (0, 0.02, 0)))
    frames = LEG.preview_beats(ME, sc, cam, lure, surface)
    prefix = getattr(LEG, "PREVIEW_PREFIX", LG.LEGENDS[fid]["model"])
    os.makedirs(ART, exist_ok=True)
    for i, (nm, im) in enumerate(frames):
        R.save_png(im, os.path.join(ART, f"{prefix}_mock_{i + 1}_{nm}.png"))
    big = [R.upscale(im, 2) for _, im in frames]
    pad, cols = 12, 3
    rows = (len(big) + cols - 1) // cols
    sheet = np.zeros((rows * big[0].shape[0] + (rows + 1) * pad, cols * big[0].shape[1] + (cols + 1) * pad, 4), np.float32)
    sheet[..., :3] = 0.12
    sheet[..., 3] = 1
    for i, b in enumerate(big):
        r, c = divmod(i, cols)
        R.over(sheet, b, pad + c * (b.shape[1] + pad), pad + r * (b.shape[0] + pad))
    R.save_png(sheet, os.path.join(ART, f"{prefix}_encounter_mock.png"))
    return sc


# ---------------------------------------------------------------- turntable / contact sheet
def turntable(fid, sc=None):
    sc = sc or Scene(fid)
    tp = getattr(LEG, "TURNTABLE", {})
    sc.cm = 100
    sc.place(V((0, 0, 0)), 0.0)
    key = LG.C_UW @ V((0.45, 0.7, -0.35)).normalized()
    bg_k, bg_d = (0.30, 0.33, 0.38), R.hexrgb(PROF.get("clear", "#04101a"))
    row1, row2, row3 = [], [], []

    def shot(pos, tgt, f, w, h, up=(0, 1, 0), lure=None, rim=None, eyes_only=False, bg=bg_k, tag="t"):
        cam = LG.cam_look(pos, tgt, f, w, h, up=up)
        sc.show(eyes_only)
        img = sc.T.render(tag, lure=(tuple(LG.C_UW @ V(lure)), LURE_R) if lure is not None else None, light=key,
                          rim=rim)
        sc.show(False)
        if eyes_only:
            ev = sc.eyes_px(cam, w, h)
            tmp = np.zeros((h, w, 4), np.float32)
            R.over(tmp, img, 0, 0)
            put_eyeshine_local(tmp, ev)
            img = tmp
        return LG._on(img, bg)

    def posed(name):
        sc.pose(**{b.replace(".", "_"): v for b, v in tp.get(name, {}).items()})

    # row 1: key-lit palette check (close-pass scale: 1 m model at 1.3 m, f 300)
    sc.pose()
    row1.append(shot((1.6, 0.05, 0.0), (0, 0, 0), 300, 240, 110))
    row1.append(shot((0.95, 0.3, 1.05), (0, 0, 0.05), 300, 240, 130))
    posed("open")
    row1.append(shot((1.0, -0.1, 0.95), (0, 0, 0.08), 300, 240, 130))
    posed("bend")
    row1.append(shot((0, 1.6, 0), (0, 0, 0), 300, 240, 130, up=(-1, 0, 0)))
    # row 2: the lure light in the dark
    posed("lit")
    row2.append(shot((0.95, 0.3, 1.05), (0, 0, 0.05), 300, 240, 130, lure=(0.1, 0.0, 0.62), rim=RIM, bg=bg_d))
    row2.append(shot((1.6, 0.05, 0.0), (0, 0, 0), 300, 240, 110, lure=(0.35, -0.05, 0.0), rim=RIM, bg=bg_d))
    row2.append(shot((1.6, 0.05, 0.0), (0, 0, 0), 300, 240, 110, lure=(1.2, 0.0, 2.3), rim=RIM, bg=bg_d))
    row2.append(shot((0.95, 0.3, 1.05), (0, 0, 0.05), 300, 240, 130, lure=(1.2, 0.0, 2.3), eyes_only=True, bg=bg_d))
    # row 3: game scale at 3 m (a PREVIEW_CM fish), 1:1 pixels
    sc.cm = getattr(LEG, "PREVIEW_CM", 160)
    sc.place(V((0, 0, 0)), 0.0)
    posed("game")
    row3.append(shot((3.0, 0.2, 0.0), (0, 0, 0), FPX, 120, 60))
    row3.append(shot((1.9, 0.5, 2.3), (0, 0, 0.1), FPX, 120, 60))
    row3.append(shot((0.6, 0.35, -2.9), (0, 0, -0.2), FPX, 120, 60))
    row3.append(shot((1.9, 0.5, 2.3), (0, 0, 0.1), FPX, 120, 60, lure=(0.2, 0.0, 1.0), rim=RIM, bg=bg_d))
    sheet = R.flow_sheet([[R.upscale(i, 3) for i in row1], [R.upscale(i, 3) for i in row2],
                          [R.upscale(i, 3) for i in row3]], (0.16, 0.17, 0.2), scale=1, pad=10)
    R.save_png(sheet, os.path.join(ART, f"{LG.LEGENDS[fid]['model']}_turntable.png"))


def put_eyeshine_local(img, eyes):
    es = load("eyeshine")
    for x, y, z, a in eyes:
        if z <= 0 or a <= 0.02:
            continue
        halo = es.copy()
        core = halo[..., 3] >= 0.99
        halo[..., :3] = np.where(core[..., None], R.hexrgb(EYE_CORE), R.hexrgb(EYE_GLOW))
        R.over(img, halo, int(round(x - 3)), int(round(y - 3)), alpha=a)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    fid = next((a for a in argv if not a.startswith("--")), "coelacanth")
    configure(fid)
    R.use_preset(LG.LEGENDS[fid]["preset"])
    os.makedirs(ART, exist_ok=True)
    sc = mock(fid)
    turntable(fid, sc)
    import shutil
    shutil.rmtree(LG.WORK, ignore_errors=True)
    print("PREVIEW done ->", ART)


if __name__ == "__main__":
    main()
