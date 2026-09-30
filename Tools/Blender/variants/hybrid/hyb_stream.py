"""
hybrid - the stream stage: the mountain-stream identity of fk_stages.stage_stream (a narrow cold stream
between gravel banks, conifers, boulders, a waterfall, the angler on a boulder at the bottom centre) built
with the hybrid craft + the "stream" preset mood (clear early morning, valley mist, key light from the LEFT).

Back layer (opaque):
  * banded pale-gold -> cream -> mint -> sky-blue sky, a pale pixel sun low in the notch of the distant
    ridge (left of centre), concentric dithered glow rings;
  * a V-valley of layers stepping back by distance, every ramp pushed towards the cream-green haze:
    distant ridge 2600 m (notch under the sun) -> forested far slopes 1100 m -> mid slopes 480 m (conifer
    teeth on the crests) -> near valley walls 210 / 260 m (individual conifers) with a small WATERFALL
    hugging the right wall -> the bank forests (conifers stepping back from 5 to 170 m);
  * gravel / moss banks (left bank in cool shade, right bank sunlit), thick valley mist lying on the far
    waterline and in the valley bottom (dash dithered, flat core, wisps);
  * water: dash-dithered depth bands (cream far -> sage -> calm green-teal play area = waterTint), mirrored
    sky in the far water, EXACT reflections of every far layer (mirror pass, water-tinted, break lines,
    dissolve), lighter shallow margins along both banks, sparse flow marks, short sparse glitter under the
    sun column (far third only), the reflections / ripples / foam of the front objects.
Front layer (transparent): the angler's flat-topped granite boulder + two flanking stones, waterline
boulders along both banks (some moss-capped), a few boulders hugging the sides of the stream, grass tufts;
rim light on the OUTER left / top silhouettes, hue-shifted outline (no ink).

Camera: fk_persp.setup_camera(standH = 1.4 from Data/stage_stream.json), 640x400, same gameplay layout
(xLim 7.5: the banks stay at |x| >= 8; the middle of the stream stays open and calm).
Outputs: _tmp/variants/hybrid/stream_back.png, stream_front.png, stage_stream.json
Scratch: _tmp/variants/hybrid/work/stream/ (EXR passes, front overlay)
Run: blender -b --python variants/hybrid/hyb_stream.py [-- back|front] [--period dawn|day|evening|night [--dry]]   (hyb_period.py)
"""
import os
import sys
import math
import json
import random
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import hyb_period as PER  # noqa: E402
import bpy  # noqa: E402
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

SID = "stream"
LAYOUT = json.load(open(os.path.join(C.DATA, f"stage_{SID}.json"), encoding="utf-8"))
STAND = float(LAYOUT["standH"])                  # 1.4
X_LIM = float(LAYOUT["xLim"])                    # 7.5: fishable half width
Z_FAR = float(LAYOUT["zFar"])                    # 118
R.WORK = PER.work(SID, R.WORK)                   # period runs: work/periods/stream_<p>/
WORKD = os.path.join(R.WORK, SID)
os.makedirs(WORKD, exist_ok=True)
W, H = P.W, P.H

# play-area band == waterTint (#3e7a78, what Unity uses for splashes / the underwater tint)
PR = PER.use_preset(SID, mist=dict(above=7.0), water=dict(bands=["#c6d4c6", "#9cbcb6", "#6e9c9a", "#4e8886", "#3e7a78", "#356e6e"],
                                  dark="#2b5e60", tint="#3e7a78"),
                  glitter=dict(far=0.16))
G = R.grade_hex


def tag(name):
    """render tag -> work/stream/stream_<name>_*.exr (parallel stage runs never collide)."""
    return f"{SID}/{SID}_{name}"


# ------------------------------------------------------------------ geometry anchors of the mood
SUN_C, SUN_R = R.sun_rc(PER.native_pr())       # (250, 73.5): pale sun low in the notch, left of centre
# (geometry anchor = the NATIVE sun, the same notch in every period; the sheen / glitter follow this period's light)
LIGHT_C = R.sun_rc(PR)[0]
D_RANGE, D_FAR, D_MID, D_WALLL, D_WALLR = 2600.0, 1100.0, 480.0, 262.0, 212.0
FALL_X, FALL_Y, FALL_H = 36.0, D_WALLR - 10.0, 14.0   # waterfall on the right valley wall (~col 410)
# screen boxes (col0, col1, row0, row1) kept free of near trees: sun + glow, the valley opening, the fall
KEEP = [(SUN_C - 42, SUN_C + 44, -999, 99), (SUN_C + 44, 386, -999, 106)]
NEAR_REFL_Y = 55.0                              # bank trees nearer than this do not reflect (calm play area)
NEAR_GRPS = set()

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)
MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)
WB = PR.water["bands"]                          # far (horizon) -> near (angler)
WSTEP = [PR.water["dark"]] + WB[::-1] + [PR.sky[0][1]]
REFL = PR.water["refl"]
RANGE = R.hazed(G(["#4c5a78", "#6a7890"]), D_RANGE, extra=-0.24)
FARL = R.hazed(G(["#274446", "#3e5e56"]), D_FAR, extra=-0.1)
MIDL = R.hazed(G(["#1a3438", "#28483f", "#44684a"]), D_MID, extra=-0.04)
WALLT = R.hazed(G(["#132a2a", "#1d3a34", "#2e5240", "#56784a"]), 235.0)
BTN = G(["#0e1c1c", "#15302a", "#214434", "#355e3c", "#6a8a4a"])      # bank conifers (near, dark, lit tips)
BTF = R.hazed(BTN[:4], 115.0)                                           # bank conifers 70-170 m
TRUNK = G(["#261e22", "#46382e"])
CLIFF = R.hazed(G(["#343c42", "#58625f", "#8a9286"]), 205.0)
FALL = PER.const("FALL", ["#cfe2da", "#f4f8ee"])     # waterfall body / foam (periods/stream.py CONSTS)
GRAVEL = G(["#434646", "#646660", "#8a8878"])
GRASS = G(["#1e3428", "#2c4830", "#44623a", "#6e8048"])
FLOOR = G(["#152120", "#1d2e28"])
BROCK = G(["#3a4146", "#5e6664", "#8a9086"])
FOAM = PER.const("FOAM", "#e6eee2")                   # foam at the stones (periods/stream.py CONSTS)
MIST = PR.mist["col"]
GLIT = PR.glitter["cols"]
BACK_PAL = (SKY_PAL + MSKY_PAL + WB + [PR.water["dark"]] + REFL + RANGE + FARL + MIDL + WALLT + BTN + BTF + TRUNK
            + CLIFF + FALL + GRAVEL + GRASS + FLOOR + BROCK + [FOAM, MIST] + GLIT)

ROCK = G(["#252a33", "#3a4247", "#58615d", "#7c857a", "#a3aa99"])      # granite: navy shade -> warm lit grey
MOSS = G(["#33472f", "#4f6a3a", "#788c4e"])
TUFT = G(["#23402c", "#40663a", "#76964a"])
DEEP = "#1c2230"                                                        # darkest (outline of the darkest tone)
FRONT_PAL = ROCK + MOSS + TUFT + [DEEP]

LB = Vector(PR.key_dir).normalized()          # one key light for the whole stage (from the left, lifted)

# back-layer overlay codes produced by the front pass (applied relative to the local water band)
OV_DARK, OV_WM1, OV_RIPPLE, OV_FOAM = 1, 2, 3, 4
_OV = {}


def rc(p):
    return R.rc(p, STAND)


def col_of(x, y):
    return rc((x, y, 0.0))[0]


def zc_of(y):
    return P.project((0.0, y, 0.0), STAND)[2]


def row0(y):
    return rc((0.0, y, 0.0))[1]


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


def wob(c, seed):
    """Smooth 1-D wobble (-1..1) along screen columns."""
    return (0.55 * math.sin(c * 0.041 + seed) + 0.3 * math.sin(c * 0.113 + 2.1 * seed)
            + 0.15 * math.sin(c * 0.29 + 3.7 * seed))


# ------------------------------------------------------------------ banks (the gameplay channel: |x| >= 8)
def xw(y, side):
    """|x| of the waterline of a bank at forward distance y (>= 8.05 inside the fishing zone, narrowing upstream)."""
    ph = 0.0 if side > 0 else 2.1
    b = 8.75 + 0.45 * math.sin(0.23 * y + ph) + 0.25 * math.sin(0.071 * y + 1.3 * ph)
    return b * (1 - 0.42 * smooth((y - 124.0) / 150.0))


def gravel_w(y, side):
    """Width (m) of the gravel beach: irregular, 1.0 .. 2.6 m."""
    ph = 0.4 if side > 0 else 2.6
    t = 0.5 + 0.35 * math.sin(0.16 * y + ph) + 0.15 * math.sin(0.41 * y + 2 * ph)
    jag = 1 + 0.22 * math.sin(1.7 * y + ph) + 0.12 * math.sin(3.3 * y + 1.0)
    return 0.45 + 2.1 * smooth((t - 0.35) / 0.55) * jag


def bank_z(u, side, y):
    """Bank height at distance u (m) from the waterline: lip -> gravel beach -> moss -> gentle rise; the
    valley walls are separate layers, so the banks stay low (and sink to the valley floor upstream)."""
    g = gravel_w(y, side)
    if u < 0.35:
        z = 0.04 + 0.26 * u / 0.35
    elif u < g:
        z = 0.30 + 0.22 * (u - 0.35) / max(0.1, g - 0.35)
    else:
        bump = 0.3 * math.sin(0.37 * y + 0.9 * u + (0 if side > 0 else 1.7)) * min(1.0, (u - g) / 2.0)
        s = 0.2 if side > 0 else 0.13
        uu = u - g
        z = 0.52 + min(uu, 26.0) * s + max(0.0, uu - 26.0) * 0.04 + bump
    return z * (1 - 0.6 * smooth((y - 140.0) / 110.0))


def is_blocked(x, y, z0, h, r):
    """True if a tree's screen bbox overlaps a keep-free box (sun / valley opening / waterfall)."""
    c, rt = rc((x, y, z0 + h))
    _, rb = rc((x, y, z0))
    rpx = r * P.F_PX / max(1.0, P.project((x, y, z0), STAND)[2])
    for (c0, c1, r0_, r1_) in KEEP + [FALL_BOX[0]]:
        if c + rpx > c0 and c - rpx < c1 and rt < r1_ and rb > r0_:
            return True
    return False


FALL_BOX = [None]


# ================================================================== BACK LAYER geometry
def band_mesh(name, dist, depth, x0, x1, step, crest_fn, mat, rnd, kind):
    """Ridge band at forward distance `dist` whose crest stands at screen row crest_fn(col) (authored in
    screen space -> metres). 7 rows in depth like hyb_lake.ridge_mesh. Returns (ob, xs, crest z)."""
    xs = np.arange(x0, x1 + step, step)
    zc = zc_of(dist)
    r0 = row0(dist)
    ridge = []
    for x in xs:
        hpx = r0 - crest_fn(col_of(x, dist))
        ridge.append(max(0.0, hpx * zc / P.F_PX))
    rows = [-1.0, -0.55, -0.2, 0.0, 0.3, 0.7, 1.0]
    verts, faces = [], []
    for i, x in enumerate(xs):
        for j, v in enumerate(rows):
            fall = max(0.0, 1 - abs(v)) ** 1.35
            z = ridge[i] * fall + (rnd.uniform(-0.05, 0.05) * ridge[i] if 0 < abs(v) < 1 else 0)
            jx = rnd.uniform(-0.3, 0.3) * step if abs(v) < 1 else 0.0
            verts.append((x + jx, dist + v * depth * 0.5, max(-2.0, z - 1.0)))
    nr = len(rows)
    for i in range(len(xs) - 1):
        for j in range(nr - 1):
            a = i * nr + j
            b = (i + 1) * nr + j
            faces.append((a, b, b + 1, a + 1))
    ob = R.mesh_from(name, verts, faces, mat, smooth=False)
    return tagk(ob, kind), xs, np.array(ridge)


def teeth_mesh(name, dist, depth, xs, zr, mat, rnd, kind, hmin, hmax, spacing, rows=((0.0, 1.0), (-0.3, 0.6))):
    """Conifer 'teeth' (open cones, merged in ONE mesh) along the crest and on the upper face of a band:
    serrated forested silhouettes for the far layers without thousands of objects."""
    verts, faces = [], []
    for (v, keep) in rows:
        x = xs[0]
        while x < xs[-1]:
            h = rnd.uniform(hmin, hmax)
            if rnd.random() < keep:
                zc_ = float(np.interp(x, xs, zr))
                fall = max(0.0, 1 - abs(v)) ** 1.35
                zb = zc_ * fall - 1.0 - 0.25 * h
                if zc_ > 1.5:
                    y = dist + v * depth * 0.5 + rnd.uniform(-0.03, 0.0) * depth
                    r = h * rnd.uniform(0.24, 0.3)
                    b = len(verts)
                    verts.append((x + rnd.uniform(-0.1, 0.1) * r, y, zb + h))
                    n = 6
                    rot = rnd.uniform(0, 6.28)
                    for i in range(n):
                        a = rot + 2 * math.pi * i / n
                        verts.append((x + r * math.cos(a), y + r * math.sin(a), zb))
                    for i in range(n):
                        faces.append((b, b + 1 + i, b + 1 + (i + 1) % n))
            x += spacing * h * rnd.uniform(0.7, 1.3)
    ob = R.mesh_from(name, verts, faces, mat, smooth=True)
    return tagk(ob, kind)


def conifer(x, y, z0, h, rnd, mat, trunk, grp, tiers=None, slim=1.0, kind="tree", split=False):
    """Spruce: short trunk + 4-7 star-shaped tiers (hyb_lake.conifer, slimmer, drooping tips)."""
    tagk(R.loft("Trunk", [(x, y, z0 - 0.5), (x, y, z0 + h * 0.3)], h * 0.03, trunk, 6), kind, grp)
    tiers = tiers or rnd.randint(5, 7)
    for k in range(tiers):
        t0 = 0.08 + 0.8 * k / tiers
        zc = z0 + h * t0
        r = h * 0.22 * slim * (1 - 0.82 * k / tiers) * rnd.uniform(0.88, 1.1)
        th = h * 0.26
        n = 12
        verts = [(x, y, zc + th)]
        rot = rnd.uniform(0, 6.28)
        for i in range(n):
            a = rot + 2 * math.pi * i / n
            rr = r * (1.0 if i % 2 == 0 else 0.6) * rnd.uniform(0.85, 1.1)
            dz = -r * 0.32 if i % 2 == 0 else 0.0
            verts.append((x + rr * math.cos(a), y + rr * math.sin(a), zc + dz))
        verts.append((x, y, zc - r * 0.1))
        faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)] + [(n + 1, 1 + (i + 1) % n, 1 + i) for i in range(n)]
        ob = R.mesh_from("Tier", verts, faces, mat, smooth=True)
        R._fix_normals(ob)
        tagk(ob, kind, f"{grp}_{k}" if split else grp)


def bank_meshes(rnd):
    """Both banks: gravel beach, moss meadow and the rising forest floor as three height-field strips each
    (y -16..300 m), wound so the normals face up (the lambert then lights the right bank, shades the left)."""
    mt = R.m_tone
    ys = np.concatenate([np.arange(-16.0, 30.0, 0.7), np.arange(30.0, 90.0, 1.5), np.arange(90.0, 180.0, 3.0),
                         np.arange(180.0, 301.0, 6.0)])
    for side in (-1, 1):
        sb = 0.0 if side > 0 else -0.07             # the left bank lies in the morning shade of the ridge
        mats = dict(gravel=mt(GRAVEL, [0.5, 0.82], light=LB, noise=0.42, nscale=6.5, ncoord="world", ndetail=1.0,
                              bias=sb, name="Gravel"),
                    grass=mt(GRASS, [0.3, 0.56, 0.9], light=LB, noise=0.2, nscale=2.2, ncoord="world", ndetail=2.0,
                             bias=sb, name="Grass"),
                    floor=mt(FLOOR + GRASS[:2], [0.3, 0.56, 0.86], light=LB, noise=0.14, nscale=0.45, ncoord="world",
                             ndetail=1.0, bias=sb - 0.06, name="Floor"))
        for zn in ("gravel", "grass", "floor"):
            verts, faces = [], []
            nu = 0
            for y in ys:
                w = xw(y, side)
                g = gravel_w(y, side)
                if zn == "gravel":
                    us = [0.0, 0.12, 0.35] + [0.35 + (g - 0.35) * t for t in (0.33, 0.66, 1.0)]
                elif zn == "grass":
                    us = [g + (9.0 - g) * t for t in (0.0, 0.15, 0.3, 0.45, 0.6, 0.8, 1.0)]
                else:
                    us = [9.0, 12.0, 16.0, 22.0, 30.0, 42.0, 60.0, 90.0, 130.0]
                nu = len(us)
                for u in us:
                    verts.append((side * (w + u), y, bank_z(u, side, y)))
            for i in range(len(ys) - 1):
                for j in range(nu - 1):
                    a = i * nu + j
                    b = a + nu
                    faces.append((a, a + 1, b + 1, b) if side > 0 else (a, b, b + 1, a + 1))
            tagk(R.mesh_from(f"Bank{zn}", verts, faces, mats[zn], smooth=False), zn)


def back_scene(rnd):
    mt = R.m_tone
    # ---- distant ridge (2600 m): notch under the sun, two hazy tones (lit left faces lighter)
    def crest_range(c):
        notch = 0.4 + 0.6 * smooth((abs(c - SUN_C) - 8.0) / 48.0)
        return row0(D_RANGE) - (18.0 + 6.0 * wob(c, 1.0) + 3.0 * wob(c * 2.3, 4.0) + 0.04 * abs(c - 300)) * notch
    rmat = mt(RANGE, [0.6], light=LB, name="Range")
    band_mesh("Range", D_RANGE, 700, -3600, 3600, 24, crest_range, rmat, rnd, "range")
    # ---- far forested slopes (1100 m): shallow V around col 262, small conifer teeth
    def crest_far(c):
        return row0(D_FAR) - (4.0 + 0.1 * abs(c - 262) + 2.5 * wob(c, 2.0))
    fmat = mt(FARL, [0.56], light=LB, noise=0.12, nscale=0.05, ncoord="world", ndetail=1.0, name="Far")
    _, fx, fz = band_mesh("Far", D_FAR, 300, -1400, 1400, 12, crest_far, fmat, rnd, "far")
    teeth_mesh("FarT", D_FAR, 300, fx, fz, fmat, rnd, "far", 6.0, 10.0, 0.55)
    # ---- mid slopes (480 m): V around col 280, crest teeth + two rows of tips on the face
    def crest_mid(c):
        k = 0.2 if c < 280 else 0.22
        return row0(D_MID) - (2.0 + k * abs(c - 280) + 3.0 * wob(c, 3.0))
    mmat = mt(MIDL, [0.4, 0.72], light=LB, noise=0.12, nscale=0.06, ncoord="world", ndetail=1.0, name="Mid")
    _, mx, mz = band_mesh("Mid", D_MID, 160, -700, 700, 8, crest_mid, mmat, rnd, "mid")
    teeth_mesh("MidT", D_MID, 160, mx, mz, mmat, rnd, "mid", 5.0, 8.0, 0.5,
               rows=((0.0, 1.0), (-0.22, 0.7), (-0.45, 0.5)))
    # ---- near valley walls: left (262 m, shaded by the morning ridge) and right (212 m, sunlit, with the
    #      waterfall cliff); individual conifers on their faces, kept off the sun / valley opening / fall
    wmat = mt(WALLT[:3], [0.45, 0.8], light=LB, noise=0.1, nscale=0.3, ncoord="world", name="WallG")

    def crest_wl(c):
        return row0(D_WALLL) - max(0.0, 0.42 * (238 - c) + 4 * wob(c, 5.0))

    def crest_wr(c):
        return row0(D_WALLR) - max(0.0, 0.42 * (c - 372) + 4 * wob(c, 6.0))
    _, lx, lz = band_mesh("WallL", D_WALLL, 70, -700, 30, 5, crest_wl, wmat, rnd, "wall")
    _, rx_, rz_ = band_mesh("WallR", D_WALLR, 70, 0, 700, 5, crest_wr, wmat, rnd, "wall")
    tmat = mt(WALLT, [0.3, 0.55, 0.82], light=LB, noise=0.12, nscale=2.8, ndetail=1.0, bias=-0.04, name="WallTree")
    trunk = mt(TRUNK, [0.55], light=LB, name="Trunk")
    gid = [0]
    for (dist, xs_, zs_, x0, x1, n, hmin, hmax) in ((D_WALLL, lx, lz, -330, 10, 170, 9.0, 14.0),
                                                   (D_WALLR, rx_, rz_, 8, 330, 170, 9.0, 14.0)):
        for k in range(n):
            x = rnd.uniform(x0, x1)
            zr = float(np.interp(x, xs_, zs_))
            if zr < 1.0:
                continue
            v = -rnd.random() ** 1.3 * 0.75
            y = dist + v * 35.0
            z = zr * (1 - abs(v)) ** 1.35 - 1.2
            h = rnd.uniform(hmin, hmax)
            if is_blocked(x, y, z, h, h * 0.22):
                continue
            gid[0] += 1
            conifer(x, y, z, h, rnd, tmat, trunk, f"w{gid[0]}", tiers=rnd.randint(4, 6), kind="wtree")
    # ---- waterfall cliff on the right wall: a jagged rock face + a white ribbon falling into the mist
    cliff_m = mt(CLIFF, [0.4, 0.72], light=LB, noise=0.2, nscale=0.35, ncoord="world", ndetail=1.0, name="Cliff")
    zt = FALL_H
    pts = [(FALL_X - 8.0, -1.0), (FALL_X - 8.0, zt * 0.5), (FALL_X - 5.0, zt * 0.78), (FALL_X - 2.2, zt + 1.2),
           (FALL_X - 0.8, zt + 0.6), (FALL_X + 1.6, zt + 0.9), (FALL_X + 3.4, zt + 2.2), (FALL_X + 6.5, zt * 0.88),
           (FALL_X + 9.0, zt * 0.62), (FALL_X + 9.0, -1.0)]
    cl = R.vpoly("Cliff", pts, FALL_Y + 1.2, cliff_m, th=3.0)
    tagk(cl, "cliff", "cliff")
    for (dx, dz, r) in ((-5.5, zt * 0.4, 2.8), (5.0, zt * 0.45, 3.2), (-3.0, 0.8, 2.4), (3.6, 1.0, 2.6)):
        ob = C.add_prim("ico", "CliffRock", cliff_m, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((FALL_X + dx, FALL_Y - 0.5, dz)) @ Matrix.Diagonal((r * 1.2, r * 0.8, r, 1))
        tagk(ob, "cliff", "cliff")
    for (dx, h) in ((-6.5, 9.0), (-4.2, 7.5), (5.2, 8.5), (7.6, 10.0)):        # trees on the cliff lip
        gid[0] += 1
        conifer(FALL_X + dx, FALL_Y + 2.5, zt * (0.75 if abs(dx) > 6 else 0.95), h, rnd, tmat, trunk, f"c{gid[0]}",
                tiers=5, kind="wtree")
    fall_m = mt(FALL, [0.5], lam=0.0, pats=((0, 0.55, 0.45, 0.6, "world", 0.2),), noise=0.2, nscale=0.6,
                ncoord="world", ndetail=1.0, name="Fall")
    tagk(R.box("Fall", (FALL_X + 0.2, FALL_Y - 0.9, (zt + 0.6) / 2), (2.2, 0.3, zt + 0.6), fall_m), "fall", "fall")
    foam_m = R.m_flat(FALL[1])
    for k in range(6):
        ob = C.add_prim("ico", "Spray", foam_m, radius=1.0, location=(0, 0, 0), subdivisions=2)
        r = rnd.uniform(1.0, 1.9)
        ob.matrix_world = Matrix.Translation((FALL_X + rnd.uniform(-2.2, 2.2), FALL_Y - 2.0, 0.3)) @ \
            Matrix.Diagonal((r * 1.3, r, r * 0.7, 1))
        tagk(ob, "fall", "fall")
    # ---- banks + scattered bank boulders + bank forests stepping back: the left (sun) side keeps a
    #      framing spruce group near the viewer and only low clumps far back; the right side is denser
    bank_meshes(rnd)
    brock = mt(BROCK, [0.4, 0.72], light=LB, noise=0.1, nscale=2.0, ndetail=1.0, name="BRock")
    for k in range(40):
        side = -1 if k % 2 else 1
        y = 3.0 + 110.0 * rnd.random() ** 2.0
        u = rnd.uniform(1.4, 14.0)
        r = rnd.uniform(0.3, 0.8) * (1 + y / 70.0)
        x = side * (xw(y, side) + u)
        ob = C.add_prim("ico", "BRock", brock, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, y, bank_z(u, side, y) + r * 0.15)) @ \
            Matrix.Rotation(rnd.uniform(0, 6.28), 4, "Z") @ Matrix.Diagonal((r * 1.4, r, r * 0.75, 1))
        tagk(ob, "brock", f"br{k}")
    shrub_m = mt(BTN[1:], [0.32, 0.6, 0.86], light=LB, noise=0.16, nscale=3.5, ndetail=1.0, bias=-0.04, name="Shrub")
    for k in range(36):
        side = -1 if k % 2 else 1
        y = 6.0 + 120.0 * rnd.random() ** 1.3
        u = gravel_w(y, side) + 2.0 + 15.0 * rnd.random() ** 0.7
        x = side * (xw(y, side) + u)
        z0 = bank_z(u, side, y)
        hh = rnd.uniform(0.4, 0.9) * (1 + y / 90.0)
        if is_blocked(x, y, z0, hh * 2, hh):
            continue
        for j in range(rnd.randint(2, 4)):
            r = hh * rnd.uniform(0.45, 0.75)
            c = (x + rnd.uniform(-1.1, 1.1) * hh, y + rnd.uniform(-0.4, 0.4) * hh, z0 + r * 0.45 + rnd.uniform(0, 0.3) * hh)
            ob = C.add_prim("ico", "Shrub", shrub_m, radius=1.0, location=(0, 0, 0), subdivisions=2)
            ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((r * 1.5, r, r * 0.62, 1))
            C.set_smooth(ob)
            tagk(ob, "shrub", f"sh{k}")
    near = mt(BTN, [0.28, 0.5, 0.72, 0.9], light=LB, noise=0.18, nscale=5.0, ndetail=2.0, bias=-0.05, name="BankT")
    farm = mt(BTF, [0.34, 0.6, 0.86], light=LB, noise=0.12, nscale=2.8, ndetail=1.0, bias=-0.04, name="BankTF")

    def plant(side, y, u, h):
        x = side * (xw(y, side) + u)
        z0 = bank_z(u, side, y)
        if is_blocked(x, y, z0, h, h * 0.22):
            return
        gid[0] += 1
        g = f"b{gid[0]}"
        conifer(x, y, z0, h, rnd, near if y < 70 else farm, trunk, g, split=y < 45)
        if y < NEAR_REFL_Y:
            NEAR_GRPS.update([g] + [f"{g}_{k}" for k in range(8)])
    NEAR_GRPS.clear()
    # left: a framing group of tall spruces at the crop edge, then low clumps set far back
    for (y, u, h) in ((6.0, 11.5, 17.0), (10.5, 12.5, 15.0), (15.0, 10.5, 16.0), (19.0, 13.5, 14.0), (24.0, 11.0, 15.5),
                      (29.0, 14.0, 13.0)):
        plant(-1, y + rnd.uniform(-1, 1), u + rnd.uniform(-1, 1), h * rnd.uniform(0.92, 1.08))
    y = 34.0
    while y < 172.0:
        if rnd.random() < 0.7:
            for k in range(rnd.randint(2, 4)):
                plant(-1, y + rnd.uniform(-2.5, 2.5), 16.0 + 0.1 * y + rnd.random() * 22.0, rnd.uniform(6.5, 10.5))
        y += rnd.uniform(5.0, 9.0) * (1 + y / 90.0)
    # right: dense sunlit forest from the viewer back to the fall
    y = -4.0
    while y < 172.0:
        umin = 4.5 if y < 70 else 12.0 + 0.12 * (y - 70)
        for k in range(rnd.randint(2, 3)):
            plant(1, y + rnd.uniform(-1.8, 1.8), umin + rnd.random() ** 1.5 * 24.0, rnd.uniform(9.0, 15.0))
        y += rnd.uniform(2.4, 4.4) * (1 + max(0.0, y) / 70.0)
    tagk(R.hpoly("Water", [(-9000, -40), (9000, -40), (9000, 3200), (-9000, 3200)], 0.0, R.m_flat(WB[3]), 0.01), "water")


# ================================================================== FRONT LAYER geometry
def boulder(name, c, r, mat, rnd, p=2.6, top=None, amp=0.09, subdiv=2, rotz=0.0, kind="rock", grp=None,
            moss=None, moss_nz=0.62):
    """Faceted granite boulder: icosphere pushed to a superellipsoid (p > 2 = blockier), jittered, top
    optionally clamped flat (the angler's standing plateau), nothing below the water (z >= 0).
    moss: material of a moss cap (the up-facing faces lifted 1.5 cm)."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    cs, sn = math.cos(rotz), math.sin(rotz)
    for v in bm.verts:
        x, y, z = v.co
        n = (abs(x) ** p + abs(y) ** p + abs(z) ** p) ** (1.0 / p)
        x, y, z = x / n, y / n, z / n
        k = 1 + rnd.uniform(-amp, amp)
        X = c[0] + (x * cs - y * sn) * r[0] * k
        Y = c[1] + (x * sn + y * cs) * r[1] * k
        Z = c[2] + z * r[2] * k
        if top is not None and Z > top:
            Z = top + rnd.uniform(-0.012, 0.0)
        v.co = (X, Y, max(0.0, Z))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    cap = None
    if moss is not None:
        cv, cf = [], []
        vid = {}
        for f in bm.faces:
            f.normal_update()
            if f.normal.z > moss_nz and f.calc_center_median().z > c[2] + r[2] * 0.2:
                ids = []
                for v in f.verts:
                    if v.index not in vid:
                        vid[v.index] = len(cv)
                        cv.append(tuple(v.co + Vector((0, 0, 0.015))))
                    ids.append(vid[v.index])
                cf.append(tuple(ids))
        if cf:
            cap = R.mesh_from(name + "Moss", cv, cf, moss, smooth=False)
            tagk(cap, kind, grp)
    ob = C.mesh_object(name, bm, mat)
    return tagk(ob, kind, grp), cap


def ribbon(name, pts, widths, mat, th=0.006):
    """Thin camera-facing ribbon (hyb_lake.ribbon)."""
    P3 = [Vector(p) for p in pts]
    n = len(P3)
    verts, faces = [], []
    segs = 6
    for i, p in enumerate(P3):
        t = (P3[min(i + 1, n - 1)] - P3[max(i - 1, 0)]).normalized()
        a = t.cross(Vector((0, 1, 0)))
        if a.length < 1e-4:
            a = Vector((1, 0, 0))
        a.normalize()
        b = t.cross(a).normalized()
        for k in range(segs):
            ang = 2 * math.pi * k / segs
            verts.append(p + a * (math.cos(ang) * widths[i]) + b * (math.sin(ang) * th))
    for i in range(n - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append((i * segs + k, i * segs + k2, (i + 1) * segs + k2, (i + 1) * segs + k))
    ob = R.mesh_from(name, verts, faces, mat, smooth=False)
    R._fix_normals(ob)
    return ob


def tuft(x, y, z, h, rnd, mats, grp, n=9):
    """Grass tuft: arching tapered blades (camera-facing ribbons), two value zones like the lake reeds."""
    depth = P.project((x, y, z), STAND)[2]
    mpp = depth / P.F_PX
    objs = []
    for i in range(n):
        hh = h * rnd.uniform(0.55, 1.0)
        dx = rnd.uniform(-1, 1)
        bend = rnd.uniform(0.2, 0.7)
        pts, ws = [], []
        bx, by = x + rnd.gauss(0, 0.08), y + rnd.gauss(0, 0.05)
        for k in range(7):
            t = k / 6
            off = bend * hh * 0.8 * t * t
            pts.append((bx + dx * off, by, z + hh * (t - bend * 0.4 * t ** 3)))
            ws.append(max(0.012, 1.1 * mpp) * (1 - t) + 0.5 * mpp * t)
        objs.append(tagk(ribbon("Blade", pts, ws, mats[rnd.randrange(len(mats))]), "tuft", grp))
    return objs


STONES = []            # (x, y, radius) of every front rock touching the water -> ripples / foam in the back


def front_scene(rnd):
    mt = R.m_tone
    STONES.clear()
    rock_m = mt(ROCK, [0.3, 0.5, 0.7, 0.91], light=LB, noise=0.07, nscale=2.5, ncoord="object", ndetail=1.0,
                name="Rock")
    moss_m = mt(MOSS, [0.45, 0.75], light=LB, noise=0.18, nscale=4.0, ncoord="world", ndetail=1.0, name="Moss")
    refl = []
    # ---- the angler's boulder (flat plateau at standH under the feet) + two flanking stones
    ob, _ = boulder("AnglerRock", (0.05, -0.8, 0.15), (1.8, 2.4, 1.5), rock_m, rnd, p=2.7, top=STAND, amp=0.06,
                    subdiv=3, rotz=0.12, grp="arock")
    refl.append(ob)
    for (x, y, zc, r, rz, mossy) in ((-2.05, 0.9, 0.05, (0.95, 0.85, 0.95), 0.4, True),
                                     (2.0, 1.5, -0.1, (0.8, 0.7, 0.8), -0.3, False),
                                     (-3.1, 2.3, -0.15, (0.5, 0.45, 0.5), 0.9, False)):
        ob, cap = boulder("Flank", (x, y, zc), r, rock_m, rnd, p=2.3, rotz=rz, grp=f"fl{x}", moss=moss_m if mossy else None)
        refl += [ob] + ([cap] if cap else [])
        STONES.append((x, y, r[0]))
    # ---- waterline boulders along both banks: clusters of 1-3 with gaps (half in the water, some mossy)
    for side in (-1, 1):
        y = 3.5 if side < 0 else 6.5
        while y < 95.0:
            n = 1 if rnd.random() < 0.45 else rnd.randint(2, 3)
            for k in range(n):
                yy = y + k * rnd.uniform(0.9, 1.7) * (1 + y / 40.0)
                r = rnd.uniform(0.45, 1.05) * (1 + yy / 90.0) * (1.0 if k == 0 else rnd.uniform(0.45, 0.7))
                x = side * (xw(yy, side) + rnd.uniform(-0.3, 0.9) * r)
                if abs(x) - r < 6.9:                    # never into the fishing channel (xLim 7.5)
                    x = side * (6.9 + r)
                ob, cap = boulder("Bank", (x, yy, rnd.uniform(-0.3, 0.0) * r),
                                  (r * rnd.uniform(1.1, 1.5), r, r * rnd.uniform(0.65, 0.85)), rock_m, rnd,
                                  p=rnd.uniform(2.1, 2.6), rotz=rnd.uniform(-0.6, 0.6), grp=f"bk{side}{yy:.1f}",
                                  moss=moss_m if rnd.random() < 0.35 else None, moss_nz=0.72)
                refl += [ob] + ([cap] if cap else [])
                if abs(x) - r * 1.3 < xw(yy, side):
                    STONES.append((x, yy, r * 1.2))
            y += rnd.uniform(9.0, 16.0) * (1 + y / 45.0)
    # ---- a few boulders hugging the sides of the stream (the middle ~60 % stays open for casting / shadows)
    for (x, y, r) in ((-6.35, 13.5, 0.62), (6.3, 25.0, 0.78), (-6.1, 44.0, 1.0), (5.9, 66.0, 1.15), (-6.0, 92.0, 1.2)):
        ob, cap = boulder("Mid", (x, y, -0.2 * r), (r * 1.25, r, r * 0.85), rock_m, rnd, p=2.4, rotz=rnd.uniform(-0.5, 0.5),
                          grp=f"md{y}", moss=moss_m if y > 30 else None)
        refl += [ob] + ([cap] if cap else [])
        STONES.append((x, y, r * 1.25))
    # ---- grass tufts on the bank tops (behind the waterline stones) and in the angler rock's crevice
    tm = [R.m_tone(TUFT, [0.35, 0.7], light=LB, lam=0.5, bias=b, grads=((2, 0.2, 0.9, 0.35, "object"),), name="Tuft")
          for b in (-0.05, 0.05)]
    for side in (-1, 1):
        for k in range(7):
            y = 4.0 + k * rnd.uniform(4.5, 7.0) * (1 + k * 0.25)
            u = rnd.uniform(1.4, 3.2)
            tuft(side * (xw(y, side) + u), y, bank_z(u, side, y) - 0.05, rnd.uniform(0.45, 0.8) * (1 + y / 60), rnd, tm,
                 f"tf{side}{k}")
    tuft(-1.55, 0.35, 0.95, 0.42, rnd, tm, "tfa", n=7)
    return refl


# ================================================================== back composite
# water depth zones (start row, end row) for WB[i] -> WB[i+1]; the play area (rows >= ~150) = waterTint band
WATER_Z = [(103, 111), (113, 119), (122, 131), (136, 150), (262, 292)]
ROW_H, ROW_P = 104, 335
SKY_END = 130                          # the far water mirrors the sky above this row


def water_xy():
    """World (x, y) of every pixel on the water plane (analytic, stage camera)."""
    ys = np.linspace(-9.0, 4000.0, 20000)
    rows = np.array([row0(y) for y in ys[::50]])
    ysub = ys[::50]
    rr = np.arange(H) + 0.5
    ok = rr > R.HORIZON_ROW + 0.3
    yrow = np.full(H, 1e5)
    yrow[ok] = np.interp(-rr[ok], -rows, ysub)       # rows decrease with y
    zc = np.array([zc_of(y) if y < 1e5 else 1e9 for y in yrow])
    xs = (np.arange(W)[None, :] + 0.5 - W / 2) * zc[:, None] / P.F_PX
    return xs, np.broadcast_to(yrow[:, None], (H, W))


def draw_dash(img_idx, r, c0, n, v, mask=None):
    r = int(r)
    if r < 0 or r >= img_idx.shape[0]:
        return
    for c in range(int(c0), int(c0) + max(1, int(n))):
        if 0 <= c < img_idx.shape[1] and (mask is None or mask[r, c]):
            img_idx[r, c] = v


def reflect_tint(ref_hex, k, dark):
    """Colour transform for reflections: towards the water colour by k, then darker (linear x dark)."""
    wl = R.s2l(R.hexrgb(ref_hex))

    def fn(c):
        return R.l2s((R.s2l(c) * (1 - k) + wl[None, :] * k) * dark)
    return fn


def far_reflection(pal, idx, water, refl_objs):
    """EXACT reflection of the far layers / banks / trees (mirror pass), water-tinted and darkened, runs >= 3 px,
    a lighter break line every third row, dissolving over the bottom 25 % (hyb_lake.far_reflection)."""
    mp = R.mirror_pass(refl_objs, tag=tag("farmirror"))
    rng = random.Random(7)
    ra = mp["a"] & water
    midx, _ = R.quantize(mp["rgb"], ra, pal, dither=False)
    lab = R.min_runs(np.where(ra, midx + 1, 0), 3)
    top = np.argmax(water, 0)
    has = lab.any(0)
    bot = np.where(has, H - 1 - np.argmax(lab[::-1] > 0, 0), top)
    kern = np.ones(9) / 9
    botf = np.convolve(np.pad(bot.astype(float), 4, mode="edge"), kern, "valid")
    hgt = np.maximum(1.0, botf - top + 1)
    rows = np.arange(H)[:, None]
    frac = (rows - top[None, :]) / hgt[None, :]
    out = idx.copy()
    keepm = np.zeros((H, W), bool)
    linem = np.zeros((H, W), bool)
    for r in range(H):
        m = lab[r] > 0
        if not m.any():
            continue
        p = np.clip((frac[r] - 0.75) / 0.25, 0, 1)
        brk = R.run_noise(rng, W, 3, 8) < p
        keep = m & ~brk & water[r]
        out[r, keep] = lab[r, keep] - 1
        keepm[r] = keep
        if r % 3 == 2:
            linem[r] = R.dash_mask(W, 0.4, 6, 20, rng) & keep
    body = keepm & ~linem
    out2, pal = R.recolour(out, pal, body, reflect_tint(WB[2], 0.28, 0.8), snap=0.035)
    out2, pal = R.recolour(out2, pal, linem, reflect_tint(WB[2], 0.58, 0.93), snap=0.035)
    return out2, pal, keepm


def far_sky(pal, idx, water, wband):
    """The far water mirrors the sky exactly (bands + glow, no disc), tinted towards the far water, handed
    over to the depth bands by dash dithering; retro16 light break lines every third row (hyb_lake.bay_sky)."""
    rng = random.Random(21)
    rows = np.arange(H)[:, None]
    zone = water & (rows < SKY_END + 4)
    thr = R.dash_threshold(H, W, rng, 2, 7)
    m_idx, _ = R.quantize(MSKY_RGB, zone, pal, dither=False)
    fade = np.clip((rows - (SKY_END - 10)) / 12.0, 0, 1)
    use = zone & (R.dash_threshold(H, W, rng, 3, 9) >= fade)
    out = idx.copy()
    out[use] = m_idx[use]
    out, pal = R.recolour(out, pal, use, reflect_tint(WB[1], 0.45, 0.92), snap=0.03)
    for r in range(int(R.HORIZON_ROW), SKY_END + 4):
        if r % 4 == 1:
            bl = R.dash_mask(W, 0.25, 4, 12, rng) & use[r]
            out[r, bl] = wband[r, bl]
    return out, pal, use


def shallows(pal, idx, water, wband, wx, wy):
    """Lighter shallow margins along both banks (the gravel bed shows through): the band one step lighter
    over the last ~0.9 m before the waterline, dash-dithered edge, only in the nearer two thirds."""
    rng = random.Random(41)
    edge = np.where(wx < 0, np.vectorize(lambda y: xw(y, -1))(wy), np.vectorize(lambda y: xw(y, 1))(wy))
    d = edge - np.abs(wx)                                  # metres from the waterline (inside the stream)
    t = np.clip((1.05 - d) / 0.6, 0, 1) * (wy < 80) * (wy > -2)
    m = water & (t > R.dash_threshold(H, W, rng, 2, 9))
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    idx[m] = up1[m]
    return idx, m


def flow_marks(pal, idx, water, avoid, wband):
    """Sparse flow marks: a light dash (band +1) with a darker dash (band -1) one row below, 1 px right.
    Cut hard in the central play rectangle, a little denser along the banks (hyb_lake.wave_marks)."""
    rng = random.Random(11)
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    dn1 = R.ramp_step(pal, WSTEP, wband, -1)
    for k in range(420):
        y = 2.0 + 115 * rng.random() ** 1.7
        lim = xw(y, 1) - 0.4
        x = rng.uniform(-1, 1) * lim
        c, r = rc((x, y, 0))
        if r >= H - 1 or c < 0 or c >= W:
            continue
        ri, ci = int(r), int(c)
        if avoid[ri, ci] or not water[ri, ci]:
            continue
        central = abs(x) < 4.6 and 150 < r < 330
        if central and rng.random() < 0.85:
            continue
        if y > 55 and rng.random() < 0.6:
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = min(14, max(2, int(round(520 / depth * rng.uniform(0.25, 0.55)))))
        ok = water & ~avoid
        draw_dash(idx, r, c, n, int(up1[ri, ci]), ok)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), int(dn1[min(H - 1, ri + 1), ci]), ok)
    return idx


def far_rims(idx, pal, kid):
    """Cream rim on the silhouette tops / sun-side (LEFT) edges of the far layers, only near the sun (glow)."""
    lv, _ = R.glow_level(PR)
    prox = np.clip(lv / 0.3, 0, 1)
    rim = PR.rim
    out, p2 = idx, pal
    for kinds in (("range",), ("far",), ("mid",), ("wall", "wtree", "tree")):
        m = np.isin(kid, kinds)
        up_out = m & ~R.shift(m, 1, 0, False)
        side_out = m & ~R.shift(m, 0, -1, False) if R.sun_side(PR) == "right" else m & ~R.shift(m, 0, 1, False)
        k = (up_out * 1.0 + side_out * 0.6).clip(0, 1) * prox
        out, p2 = R.blend_idx(out, p2, k > 0.55, rim["col"], 0.55, lighter=True, snap=0.03)
        out, p2 = R.blend_idx(out, p2, (k > 0.22) & (k <= 0.55), rim["col"], 0.3, lighter=True, snap=0.03)
    return out, p2


def valley_mist(ps, kid, sky, water):
    """THICK valley mist (0..1): the kit's band on the far waterline (decays 6 px up over the land, 4 px down
    over the water, wisps) + a thinner mist lying at the FOOT of every far layer (where it meets the nearer
    layer / the valley floor: decays upwards, per-column foot row smoothed) - the layers step back through
    the mist. Only beyond the near banks (depth > ~95 m), never on the sky."""
    depth = ps["depth"]
    line = np.full(W, row0(128.0))
    far = depth > 95.0
    land = ~sky & ~water
    a = R.mist_amount(PR, line, land & far, water)
    rows = np.arange(H)[:, None]
    x = np.arange(W)
    for k, (kinds, amt, dec) in enumerate(((("wall", "wtree", "cliff", "fall"), 0.75, 5.0), (("mid",), 0.7, 4.0),
                                           (("far",), 0.62, 3.0), (("range",), 0.5, 3.0))):
        m = np.isin(kid, kinds) & far
        if not m.any():
            continue
        has = m.any(0)
        foot = np.where(has, H - 1 - np.argmax(m[::-1], 0), -1).astype(float)
        # smooth the foot line (serrated nearer tree tops) with a running max then a box blur
        fp = np.pad(foot, 5, mode="edge")
        foot = np.array([fp[i:i + 11].max() for i in range(W)])
        foot = np.convolve(np.pad(foot, 4, mode="edge"), np.ones(9) / 9, "valid")
        wisp = 1 - 0.5 * (0.6 * R._vnoise(x / 27.0, 3.0 + k) + 0.4 * R._vnoise(x / 8.0, 5.0 + k))
        af = amt * np.exp(-np.maximum(0.0, foot[None, :] - rows) / dec) * wisp[None, :]
        a = np.maximum(a, np.where(m & (rows <= foot[None, :] + 0.5) & has[None, :], af, 0.0))
    return a


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    FALL_BOX[0] = (int(col_of(FALL_X, FALL_Y)) - 12, int(col_of(FALL_X, FALL_Y)) + 12, 40, 112)
    back_scene(rnd)
    PER.hook("back_scene", rnd=rnd, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(tag("back"))
    kid = R.kind_map(ps)
    sky = ~ps["a"]
    water = kid == "water"
    tree = np.isin(kid, ["tree", "wtree", "brock", "shrub"])
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = SKY_RGB[sky]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid | sky, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=2.0, rel=0.015, steps=1)
    idx = np.where(tree | (kid == "cliff"), lined, idx)
    idx, pal = far_rims(idx, pal, kid)
    # ---- water: depth bands with horizontal dash dithering, mirrored sky in the far water, shallow margins
    wband = R.water_bands(pal, WB, WATER_Z, random.Random(5), ROW_H, ROW_P)
    idx[water] = wband[water]
    idx, pal, skym = far_sky(pal, idx, water, wband)
    wx, wy = water_xy()
    idx, shal = shallows(pal, idx, water, wband, wx, wy)
    # ---- exact reflections of the far layers, banks and bank forests
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("range", "far", "mid", "wall", "wtree", "tree",
                                                                      "cliff", "fall", "gravel", "grass", "floor",
                                                                      "brock", "shrub") and ob.get("grp") not in NEAR_GRPS]
    idx, pal, farm = far_reflection(pal, idx, water, refl_objs)
    # ---- front-object reflections / ripples / foam (computed by the front pass)
    ov = _OV.get("ov")
    if ov is None:
        p = os.path.join(WORKD, f"{SID}_front_overlay.npy")
        ov = np.load(p) if os.path.exists(p) else np.zeros((H, W), np.int8)
    g_am, g_br = R.glitter_masks(PR, water, random.Random(17), avoid=(ov > 0))
    idx = flow_marks(pal, idx, water, farm | (ov > 0) | g_am | g_br | shal, wband)
    idx[water & (ov == OV_DARK)] = pal.index(REFL[0])
    for code, d in ((OV_WM1, -1), (OV_RIPPLE, +1)):
        m = water & (ov == code)
        idx[m] = R.ramp_step(pal, WSTEP, wband, d)[m]
    idx[water & (ov == OV_FOAM)] = pal.index(FOAM)
    # ---- sun column: a faint cream sheen + sparse short glitter dashes, far third only
    rows = np.arange(H)[:, None]
    wc = np.flatnonzero(water[:, int(LIGHT_C)])
    r0 = wc.min()
    r_end = r0 + PR.glitter["far"] * (wc.max() - r0)
    t = np.clip((rows - r0) / max(1.0, r_end - r0), 0, 1)
    halfw = PR.glitter["width"][0] + (PR.glitter["width"][1] - PR.glitter["width"][0]) * t
    cols = np.arange(W)[None, :]
    sheen = 0.7 * (1 - t) ** 1.5 * np.exp(-((cols - LIGHT_C) / (1.7 * halfw)) ** 2) * (rows < r_end)
    sm = water & (sheen > R.dash_threshold(H, W, random.Random(23), 2, 6)) & (ov == 0) & ~farm
    idx, pal = R.blend_idx(idx, pal, sm, GLIT[0], 0.25, lighter=True, snap=0.03)
    idx[g_am] = pal.index(GLIT[0])
    idx[g_br] = pal.index(GLIT[1])
    # ---- THICK valley mist: dash-dithered soft level + flat dense core, wisps
    ma = valley_mist(ps, kid, sky, water)
    thr = R.dash_threshold(H, W, random.Random(29), 3, 12)
    thr2 = R.dash_threshold(H, W, random.Random(31), 3, 11)
    bankk = np.isin(kid, ["gravel", "grass", "floor", "brock", "shrub"])     # no streaky fringe on the meadows
    soft = (ma > thr) & (ma > 0.08) & ~bankk
    mid = ma > 0.38 + 0.3 * thr2
    core = ma > 0.66 + 0.25 * thr2
    idx, pal = R.blend_idx(idx, pal, soft & ~mid, MIST, 0.35, snap=0.035)
    idx, pal = R.blend_idx(idx, pal, mid & ~core, MIST, 0.6, snap=0.04)
    idx, pal = R.blend_idx(idx, pal, core, MIST, 0.85, snap=0.045)
    idx, pal = PER.hook("back_post", idx, pal, kid=kid, water=water, sky=sky, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "back")
    wl = R.lum(WB[4])
    print("HYB back colours", R.count_colours(img), "palette", len(pal), "play water", WB[4], "L", round(wl, 3),
          "near", WB[5], "L", round(R.lum(WB[5]), 3), "tint", PR.water["tint"], "deep", PR.water["deep"])
    np.save(os.path.join(WORKD, f"{SID}_mist.npy"), ma.astype(np.float32))
    return pal


# ---------------------------------------------------------------- front post helpers
def contact_shadow(idx, pal, kid):
    """14x3 px ellipse one rock tone darker on the plateau at the projected feet point."""
    fc, fr = rc((0, 0, STAND))
    cx, cy = int(round(fc - 0.5)), int(fr) + 1
    for dy, hw in ((-1, 4), (0, 7), (1, 4)):
        for dx in range(-hw, hw):
            y, x = cy + dy, cx + dx
            if 0 <= y < H and 0 <= x < W and kid[y, x] == "rock" and idx[y, x] >= 0:
                h = pal.hex[idx[y, x]]
                if h in ROCK and ROCK.index(h) > 0:
                    idx[y, x] = pal.index(ROCK[ROCK.index(h) - 1])


def front_reflections(refl):
    """Reflections of every front object standing in water -> back-layer overlay codes (hyb_lake)."""
    mp = R.mirror_pass(refl, ids=True, tag=tag("frontmirror"))
    rng = random.Random(3)
    a = mp["a"]
    ids = mp["id"]
    z = R.world_z(mp["depth"], STAND)
    zmin = {}
    for i in np.unique(ids[a]):
        zmin[int(i)] = float(np.min(z[a & (ids == i)]))
    zm = np.vectorize(lambda i: zmin.get(int(i), -1.0))(ids)
    frac = np.clip(z / np.minimum(zm, -1e-3), 0, 1)
    frac[~a] = 0
    lumv = (mp["rgb"] * np.array([0.3, 0.55, 0.15])).sum(-1)
    code = np.where(a, np.where(lumv < 0.3, OV_DARK, OV_WM1), 0)
    code[a & (z > -0.02)] = 0
    p = np.clip(frac / 0.6, 0, 1) ** 1.3
    shift = [(0, 1, 1, 0, -1, -1)[r % 6] for r in range(H)]
    out = np.zeros((H, W), np.int8)
    for r in range(H):
        row = code[r]
        if not row.any():
            continue
        brk = R.run_noise(rng, W, 2, 6) < p[r]
        row = np.where(brk, 0, row)
        if r % 5 in (0, 2):
            row = np.where(R.dash_mask(W, 0.45, 2, 6, rng), 0, row)
        m = row > 0
        edges = np.flatnonzero(np.diff(np.concatenate([[0], m.astype(int), [0]])))
        for s, e in zip(edges[::2], edges[1::2]):
            d = shift[r] if e - s >= 4 else 0
            s2, e2 = max(0, s + d), min(W, e + d)
            out[r, s2:e2] = row[s2 - d:e2 - d]
    return out


def stone_foam(ov):
    """Current around the stones: foam dashes at the waterline on both sides, a lighter V-wake running
    downstream (towards the viewer) and a ripple arc on the upstream face."""
    rng = random.Random(5)
    for (x, y, rad) in STONES:
        c, r = rc((x, y, 0))
        depth = P.project((x, y, 0), STAND)[2]
        half = rad * P.F_PX / depth
        n = max(2, int(520 / depth * 0.3))
        for side in (-1, 1):
            c0 = c + side * (half * 0.9) - (n if side < 0 else 0)
            for i, cc in enumerate(range(int(c0), int(c0 + n))):
                near_stone = (i >= n - 2) if side < 0 else (i < 2)
                if 0 <= cc < W and 0 <= int(r) < H:
                    ov[int(r), cc] = OV_FOAM if near_stone else OV_RIPPLE
        # V-wake: two diverging dashes per step, 3-4 steps downstream
        for k in range(1, 5):
            yy = y - rad * (0.6 + 0.9 * k)
            if yy < 0.6:
                break
            spread = rad * (0.9 + 0.45 * k)
            for side in (-1, 1):
                if rng.random() < 0.25:
                    continue
                cw, rw = rc((x + side * spread, yy, 0))
                dpt = P.project((x + side * spread, yy, 0), STAND)[2]
                nn = max(2, int(520 / dpt * rng.uniform(0.25, 0.5)))
                code = OV_RIPPLE
                for cc in range(int(cw - nn / 2), int(cw + nn / 2)):
                    if 0 <= cc < W and 0 <= int(rw) < H:
                        ov[int(rw), cc] = code
    return ov


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    refl = front_scene(rnd)
    PER.hook("front_scene", rnd=rnd, refl=refl, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(tag("front"))
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    thin = kid == "tuft"
    idx = R.despeckle(idx, ps["id"], protect=~solid | thin, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    contact_shadow(idx, pal, kid)
    # cream rim on the OUTER sun-side (left / top) silhouette of the rocks, before the outline
    solid_k = kid == "rock"
    idx, pal = R.rim_light(idx, pal, PR, mask=solid_k)
    # selective outline for the rocks only (hue-shifted darker neighbour, never ink); tufts get none
    base = np.where(thin, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    idx = np.where(thin & (idx >= 0), idx, ol)
    # ---- back-layer overlays: reflections + foam / wakes around every stone in the water
    ov = front_reflections(refl)
    ov = stone_foam(ov)
    _OV["ov"] = ov
    np.save(os.path.join(WORKD, f"{SID}_front_overlay.npy"), ov)
    idx, pal = PER.hook("front_post", idx, pal, kid=kid, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "front")
    rl = [R.lum(c) for c in ROCK]
    print("HYB front colours", R.count_colours(img), "palette", len(pal), "rock L", [round(v, 2) for v in rl])


def main():
    C.reset_scene()
    which = PER.which()                      # back / front (+ --period <p> [--dry], see hyb_period)
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    d = PER.stage_json(SID, PR, clouds=False, birds=True)
    print("HYB stage json", {k: d[k] for k in ("waterTint", "waterDeep", "clouds", "birds")})
    print("HYB STREAM done")


if __name__ == "__main__":
    main()
