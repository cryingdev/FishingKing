"""
FishingKing - cinematic variant of the lake stage (golden hour).

Blender renders only geometry, one pass per depth layer (flat silhouettes far away, cinematic-shaded
props near the camera), each pass twice: through the stage camera and through the camera mirrored in
the water plane (-> exact reflections, flipped vertically). Sky, sun, haze, water (Fresnel, sun
glitter), aerial perspective, mist, rim light, vignette and the palette/dither are done in numpy.

Run:  blender -b --python Tools/Blender/variants/cinematic/cine_lake.py
Out:  _tmp/variants/cinematic/lake_back.png, lake_front.png, stage_lake.json (+ raw/ passes)
"""
import sys
import os
import math
import json
import random
import bmesh
import bpy
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cine_common as K  # noqa: E402

C, P = K.C, K.P
W, H = P.W, P.H
STAND = 1.0
CAM = P.cam_pos(STAND)
HORIZON_PY = P.F_PX * math.tan(math.radians(P.PITCH))
SUN_PX = (92.0, HORIZON_PY + 10.0)       # sun centre (px from image centre, y up)
SUN = K.ray_dir(*SUN_PX)
SUN_R = 0.64                             # deg (drawn bigger than life)
FOG_D = 1300.0                           # aerial perspective distance (m)

# ============================================================================ sky model (numpy)
SKY = [(-2.0, "#f7c68a"), (0.0, "#f4b57e"), (1.2, "#eba378"), (2.6, "#d69078"), (4.5, "#a87e86"),
       (7.5, "#737489"), (12.0, "#4a5e76"), (19.0, "#30475e"), (32.0, "#1f3044"), (90.0, "#141f30")]
SKY_E = np.array([e for e, _ in SKY], np.float32)
SKY_C = np.array([K.hexf(c) for _, c in SKY], np.float32)
GLOW = K.to_lin(K.hexf("#ffae58"))
HOT = K.hexf("#ffd08c")


def angles(d):
    el = np.degrees(np.arcsin(np.clip(d[..., 2], -1, 1)))
    ang = np.degrees(np.arccos(np.clip((d * SUN).sum(-1), -1, 1)))
    az = np.degrees(np.arctan2(d[..., 0], d[..., 1]))
    saz = math.degrees(math.atan2(SUN[0], SUN[1]))
    return el, ang, az - saz


def glow(ang, k=1.0):
    return k * (0.95 * np.exp(-ang / 1.3) + 0.42 * np.exp(-ang / 4.5) + 0.16 * np.exp(-ang / 15.0))


def sky_base(el, daz):
    e = np.clip(el, SKY_E[0], SKY_E[-1])
    col = np.stack([np.interp(e, SKY_E, SKY_C[:, k]) for k in range(3)], -1)
    w = np.exp(-np.abs(daz) / 30.0) * np.exp(-np.clip(el, 0, None) / 2.4)
    return K.mix(col, HOT, 0.5 * w)


def clouds(el, daz, ang):
    """Thin golden-hour stratus streaks: returns (alpha, colour)."""
    a = np.zeros_like(el)
    col = np.zeros(el.shape + (3,), np.float32)
    streaks = [  # (centre el, thickness, daz0, daz1, phase) - only low streaks: they are what the game shows
        (1.66, 0.26, -9, 13, 0.3), (2.12, 0.16, 6, 28, 1.7), (1.95, 0.2, -40, -17, 2.9)]
    for (ec, th, a0, a1, ph) in streaks:
        wob = 0.08 * np.sin(daz * 0.35 + ph)
        env = K.smoothstep(a0, a0 + 6, daz) * (1 - K.smoothstep(a1 - 6, a1, daz))
        thick = th * (0.6 + 0.4 * np.sin(daz * 0.23 + ph * 3)) * env
        m = np.abs(el - ec - wob) < thick * 0.5
        a = np.where(m, 1.0, a)
        # soft body just a shade darker/cooler than the sky behind it; the underside catches the sun
        low = (el - ec - wob) < -thick * 0.18
        g = np.clip(glow(ang, 1.0), 0, 1.4)
        sky = sky_base(el, daz)
        body = K.mix(sky * np.array([0.86, 0.8, 0.84], np.float32), K.hexf("#8a6a70"), 0.25 + 0.3 * np.clip(g, 0, 1))
        lit = K.mix(np.clip(sky * 1.08, 0, 1), K.hexf("#ffe2ae"), np.clip(g * 0.9, 0, 1))
        c = np.where(low[..., None], lit, body)
        col = np.where(m[..., None], c, col)
    return a, col


def sky_image(d, with_clouds=True):
    el, ang, daz = angles(d)
    base = sky_base(el, daz)
    if with_clouds:
        ca, cc = clouds(el, daz, ang)
        base = K.mix(base, cc, ca * (el > 0))
    lin = K.to_lin(base) + glow(ang)[..., None] * GLOW * 0.55
    disc = ang < SUN_R
    limb = np.clip(ang / SUN_R, 0, 1)
    sun = K.to_lin(K.mix(K.hexf("#fff7e4"), K.hexf("#ffd892"), limb ** 3))
    lin = np.where(disc[..., None], sun * 1.0, lin)
    return np.clip(K.to_srgb(lin), 0, 1)


def haze_image(d):
    """Colour distant things fade into: horizon sky at the same azimuth + sun glare."""
    el, ang, daz = angles(d)
    base = sky_base(np.full_like(el, 0.45), daz)
    lin = K.to_lin(base) + glow(ang, 0.8)[..., None] * GLOW * 0.5
    return np.clip(K.to_srgb(lin), 0, 1)


# ============================================================================ scene helpers (Blender)
LAYERS = {}   # name -> distance (m) used for fog


def tagl(ob, layer):
    ob["L"] = layer
    return ob


def screen_to_plane(px, above, Y):
    """Point on the vertical plane y=Y seen at pixel (px, horizon+above)."""
    d = K.ray_dir(px, HORIZON_PY + above)
    t = (Y - CAM[1]) / d[1]
    return CAM[0] + t * d[0], CAM[2] + t * d[2]


def vpoly(pts_xz, Y, mat, layer, th=0.05):
    ob = C.poly_object("V", pts_xz, mat, thickness=th, y=Y)
    return tagl(ob, layer)


def skyline(profile, Y, mat, layer, step=2.0, jag=0.0, rnd=None, base=0.0):
    """profile: [(px, height_px_above_horizon)] control points (screen space). Returns world polygon."""
    xs = np.arange(profile[0][0], profile[-1][0] + 0.01, step)
    hp = np.interp(xs, [p for p, _ in profile], [h for _, h in profile])
    if jag and rnd is not None:
        hp = hp + np.array([rnd.uniform(-jag, jag) for _ in xs])
    pts = [screen_to_plane(x, h, Y) for x, h in zip(xs, hp)]
    pts = [(x, max(z, base + 0.01)) for x, z in pts]
    poly = [(pts[0][0], base)] + pts + [(pts[-1][0], base)]
    return vpoly(poly, Y, mat, layer)


def conifer(rnd, x, Y, z0, h, mat, layer, wr=0.26):
    """Jagged spruce silhouette (tiers of drooping branches)."""
    tiers = rnd.randint(6, 9)
    top = z0 + h
    left, right = [], []
    for k in range(tiers + 1):
        t = k / tiers
        zt = top - h * (0.06 + 0.86 * t)
        w = h * wr * (0.12 + 0.88 * t ** 0.9) * rnd.uniform(0.82, 1.15)
        droop = h * 0.035
        right += [(x + w * 0.45, zt + droop * 1.4), (x + w, zt - droop * rnd.uniform(0.3, 1.0))]
        w2 = h * wr * (0.12 + 0.88 * t ** 0.9) * rnd.uniform(0.82, 1.15)
        left += [(x - w2 * 0.45, zt + droop * 1.4), (x - w2, zt - droop * rnd.uniform(0.3, 1.0))]
    trunk = h * 0.03
    pts = [(x, top)] + right + [(x + trunk, z0 + h * 0.06), (x + trunk, z0), (x - trunk, z0), (x - trunk, z0 + h * 0.06)] + left[::-1]
    return vpoly(pts, Y, mat, layer)


def broadleaf(rnd, x, Y, z0, h, mat, layer):
    """Irregular clumpy crown: several noisy lobes + trunk."""
    # broad, low, lumpy crown (no lollipops): a wide main mass + many sub-lobes around its upper rim
    R = h * rnd.uniform(0.3, 0.38)
    cz = z0 + h * 0.56
    wide = rnd.uniform(1.1, 1.45)
    lobes = [(0, 0, R, wide)]
    for _ in range(rnd.randint(5, 8)):
        a = rnd.uniform(-0.35, math.pi + 0.35)
        lobes.append((math.cos(a) * R * wide * 0.85, math.sin(a) * R * 0.62, R * rnd.uniform(0.3, 0.55), 1.0))
    for (dx, dz, r, sx) in lobes:
        n = 12
        ph = rnd.uniform(0, 6.28)
        pts = []
        for k in range(n):
            a = 2 * math.pi * k / n
            rr = r * (1 + 0.14 * math.sin(3 * a + ph) + 0.08 * math.sin(5 * a + ph * 2) + rnd.uniform(-0.07, 0.07))
            pts.append((x + dx + rr * math.cos(a) * sx, max(cz + dz + rr * math.sin(a) * 0.8, z0 + h * 0.18)))
        vpoly(pts, Y, mat, layer)
    tw = h * 0.035
    vpoly([(x - tw, z0), (x + tw, z0), (x + tw * 0.6, cz), (x - tw * 0.6, cz)], Y + 0.3, mat, layer)


def hpoly(pts, z, mat, layer, th=0.01):
    bm = bmesh.new()
    top = [bm.verts.new((x, y, z + th / 2)) for x, y in pts]
    bot = [bm.verts.new((x, y, z - th / 2)) for x, y in pts]
    bm.faces.new(top)
    bm.faces.new(bot[::-1])
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((top[i], bot[i], bot[j], top[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return tagl(C.mesh_object("H", bm, mat), layer)


def box(c, s, mat, layer, rot=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2]))
    ob = C.mesh_object("Box", bm, mat)
    ob.matrix_world = Matrix.Translation(c) @ Matrix.Rotation(rot, 4, "Z")
    return tagl(ob, layer)


def tube(pts, r, mat, layer, segs=6):
    ob = C.tube_along("T", pts, r, mat, segs)
    return tagl(ob, layer)


# ============================================================================ the lake
def build_far(rnd):
    # --- layer 1: distant range (3200 m): lowest right at the sun so the disc sinks into it
    LAYERS["mtn"] = 3200.0
    skyline([(-340, 12), (-250, 20), (-170, 15), (-90, 24), (-20, 17), (40, 10), (80, 6.5), (100, 5.2), (125, 6.5),
             (170, 12), (230, 21), (290, 16), (340, 23)], 3200, K.mflat("#6a5e6e"), "mtn", step=3, jag=0.8, rnd=rnd)
    # --- layer 2: rolling hills (1400 m), open valley towards the sun
    LAYERS["hills"] = 1400.0
    skyline([(-340, 10), (-280, 16), (-210, 11), (-150, 19), (-90, 13), (-40, 9), (10, 5), (45, 2.5), (80, 1.2),
             (140, 1.6), (190, 4), (240, 12), (300, 17), (340, 14)], 1400, K.mflat("#4a4650"), "hills", step=2, jag=0.5, rnd=rnd)
    # --- layer 3: forested ridge (520 m) left half + headland right
    LAYERS["ridge"] = 520.0
    m = K.mflat("#2f3438")
    skyline([(-340, 14), (-270, 19), (-200, 15), (-140, 22), (-80, 17), (-30, 12), (10, 6), (30, 2), (50, 0.2)],
            520, m, "ridge", step=1.5, jag=0.9, rnd=rnd)
    for _ in range(90):
        px = rnd.uniform(-335, 20)
        hp = float(np.interp(px, [-340, -270, -200, -140, -80, -30, 10, 30, 50], [14, 19, 15, 22, 17, 12, 6, 2, 0.2]))
        X, Z = screen_to_plane(px, hp - 0.8, 520)
        conifer(rnd, X, 519, max(Z - 1, 0), rnd.uniform(9, 16), m, "ridge", 0.22)
    skyline([(205, 0.2), (225, 5), (260, 11), (300, 15), (345, 18)], 520, m, "ridge", step=1.5, jag=0.9, rnd=rnd)
    for _ in range(30):
        px = rnd.uniform(215, 340)
        hp = float(np.interp(px, [205, 225, 260, 300, 345], [0.2, 5, 11, 15, 18]))
        X, Z = screen_to_plane(px, hp - 0.8, 520)
        conifer(rnd, X, 519, max(Z - 1, 0), rnd.uniform(9, 15), m, "ridge", 0.22)
    # --- layer 4: far end of the bay (900 m): thin wooded shoreline across the gap
    LAYERS["bay"] = 900.0
    mb = K.mflat("#262c2e")
    skyline([(0, 1.0), (60, 1.6), (120, 1.3), (180, 2.0), (230, 1.4)], 900, mb, "bay", step=1, jag=0.35, rnd=rnd)
    # clumps of low trees (soft bumps, not fence posts)
    px = 8.0
    while px < 222:
        n = rnd.randint(2, 6)
        for _ in range(n):
            X, Z = screen_to_plane(px + rnd.uniform(-3, 3), 0.8, 900)
            if rnd.random() < 0.5:
                conifer(rnd, X, 899, 0.0, rnd.uniform(6, 10), mb, "bay", 0.3)
            else:
                broadleaf(rnd, X, 899, 0.0, rnd.uniform(7, 11), mb, "bay")
        px += rnd.uniform(12, 30)
    # --- layer 5: the far shore (180-200 m, left + centre) and the right headland (250 m)
    LAYERS["shore"] = 190.0
    tree_a, tree_b, tree_c = K.mflat("#1a2426"), K.mflat("#1f2a2a"), K.mflat("#243030")
    bank = K.mflat("#22262a")
    # shoreline x-extent at y=180: from the left edge to a reedy point at ~ +11 m (px ~ +30)
    X_END = 11.0
    vpoly([(-140, 0), (-140, 0.8), (X_END - 4, 0.6), (X_END + 2.5, 0.12), (X_END + 3, 0)], 180, bank, "shore")
    # understory band (bushes) so trunks never show sky between them
    pts = [(-140, 0)]
    x = -140.0
    while x < X_END:
        taper = min(1.0, (X_END - x) / 12.0)
        pts.append((x, (2.8 + 1.2 * math.sin(x * 0.4) + rnd.uniform(0, 1.4)) * taper + 0.3))
        x += rnd.uniform(0.8, 1.6)
    pts += [(X_END + 1, 0.2), (X_END + 1, 0)]
    vpoly(pts, 181, tree_b, "shore")
    x = -140.0
    while x < X_END:
        taper = min(1.0, (X_END - x) / 16.0)
        h = rnd.uniform(5.0, 9.5) * (0.35 + 0.65 * taper)
        y = rnd.uniform(182, 196)
        mat = tree_a if y < 187 else (tree_b if y < 192 else tree_c)
        if rnd.random() < 0.62:
            conifer(rnd, x, y, 0.3, h * 1.1, mat, "shore")
        else:
            broadleaf(rnd, x, y, 0.3, h * 0.9, mat, "shore")
        x += rnd.uniform(1.4, 3.6)
    # reeds on the point
    for _ in range(26):
        rx = rnd.uniform(X_END - 6, X_END + 3)
        hh = rnd.uniform(0.8, 2.0)
        vpoly([(rx - 0.08, 0), (rx + 0.08, 0), (rx + rnd.uniform(-0.3, 0.3), hh)], 179, tree_a, "shore")
    # cabin with a warm window + little jetty
    cx = -52.0
    wall, roof = K.mflat("#2a2626"), K.mflat("#1e1a1c")
    vpoly([(cx - 3.2, 0.8), (cx + 3.2, 0.8), (cx + 3.2, 3.8), (cx - 3.2, 3.8)], 179.5, wall, "shore")
    vpoly([(cx - 4.0, 3.6), (cx + 4.0, 3.6), (cx + 0.2, 6.6), (cx - 0.2, 6.6)], 179.4, roof, "shore")
    vpoly([(cx + 1.6, 6.0), (cx + 2.2, 6.0), (cx + 2.2, 7.0), (cx + 1.6, 7.0)], 179.6, roof, "shore")
    vpoly([(cx - 1.9, 1.9), (cx - 0.7, 1.9), (cx - 0.7, 2.9), (cx - 1.9, 2.9)], 179.0, K.mflat("#ffb45a", 1.0), "shore")
    vpoly([(cx - 3.0, 0.0), (cx + 3.0, 0.0), (cx + 3.0, 0.8), (cx - 3.0, 0.8)], 179.3, bank, "shore")
    for k in range(4):
        vpoly([(cx + 3.6 + k * 1.2, 0), (cx + 3.75 + k * 1.2, 0), (cx + 3.75 + k * 1.2, 0.9), (cx + 3.6 + k * 1.2, 0.9)], 170 - k * 2, wall, "shore")
    vpoly([(cx + 3.5, 0.8), (cx + 8.2, 0.8), (cx + 8.2, 1.0), (cx + 3.5, 1.0)], 169, wall, "shore")
    # right headland (y=250): from px ~ +200 to the right edge
    LAYERS["head"] = 250.0
    Xh0 = screen_to_plane(196, 0, 250)[0]
    ha, hb = K.mflat("#1e282a"), K.mflat("#243030")
    vpoly([(Xh0 - 2, 0), (Xh0, 0.5), (Xh0 + 300, 0.9), (Xh0 + 300, 0)], 249, bank, "head")
    x = Xh0 + 1.0
    pts = [(Xh0 + 0.5, 0)]
    while x < Xh0 + 300:
        taper = min(1.0, (x - Xh0) / 14.0)
        pts.append((x, (2.0 + rnd.uniform(0, 1.5)) * taper + 0.3))
        x += rnd.uniform(1.0, 2.0)
    pts += [(Xh0 + 300, 0)]
    vpoly(pts, 250, hb, "head")
    x = Xh0 + 1.5
    while x < Xh0 + 300:
        taper = min(1.0, (x - Xh0) / 20.0)
        h = rnd.uniform(6.5, 11) * (0.35 + 0.65 * taper)
        y = rnd.uniform(251, 262)
        if rnd.random() < 0.7:
            conifer(rnd, x, y, 0.3, h * 1.1, ha if y < 256 else hb, "head")
        else:
            broadleaf(rnd, x, y, 0.3, h * 0.9, ha if y < 256 else hb, "head")
        x += rnd.uniform(1.6, 3.8)


# ----------------------------------------------------------------------------- near props (front layer)
def wood_mat(a, b):
    return K.mcine(a, name="wood", hexalt=b, pattern=K.stripe_pattern(7.0, 0.3, axis=0, warp=0.8))


def reed_clump(rnd, cx, cy, n, spread, hmin, hmax, heads=0.5, layer="front"):
    d = math.hypot(cx, cy - CAM[1])
    r = max(0.014, 0.78 * d / P.F_PX)  # >= ~1.5 px wide so blades never break into dots
    stem = K.mcine("#48522f", name="reed", rim=0.4)
    leaf = K.mcine("#525a33", name="leaf", rim=0.4)
    dry = K.mcine("#6a5c3c", name="dry", rim=0.4)
    head = K.mcine("#4a3222", name="cattail", spec=0.4)
    for _ in range(n):
        x = cx + rnd.gauss(0, spread * 0.45)
        y = cy + rnd.gauss(0, spread * 0.3)
        h = rnd.uniform(hmin, hmax)
        out = (x - cx) / max(spread, 1e-3)  # blades lean away from the clump centre
        if rnd.random() < heads * 0.6:
            lean = out * 0.15 + rnd.uniform(-0.08, 0.12)
            pts = [(x, y, 0.0), (x + lean * 0.3, y, h * 0.5), (x + lean, y, h)]
            tube(pts, [r * 1.1, r, r * 0.8], stem, layer, 5)
            t0, t1 = 0.7, 0.88
            tube([(x + lean * t0, y - 0.01, h * t0), (x + lean * t1, y - 0.01, h * t1)], r * 2.4, head, layer, 8)
        else:
            # blade: rises, then arches over (sword-like), thick at the base
            hb = h * rnd.uniform(0.55, 1.0)
            side = 1 if out + rnd.uniform(-0.6, 0.6) > 0 else -1
            bend = rnd.uniform(0.1, 0.7) * side
            pts = []
            for k in range(7):
                t = k / 6
                pts.append((x + bend * t ** 2 * hb * 0.55, y + rnd.uniform(-0.02, 0.02), hb * (t - 0.3 * abs(bend) * t ** 3)))
            tube(pts, [r * 1.35 * (1 - 0.5 * k / 6) for k in range(7)], rnd.choice((leaf, leaf, leaf, dry)), layer, 4)


def lily_group(rnd, cx, cy, n, spread, flowers=1, layer="front"):
    pad = K.mcine("#304a38", name="pad", rim=0.3, spec=0.6)
    pad2 = K.mcine("#2a4034", name="pad2", rim=0.3, spec=0.6)
    fl = K.mflat("#e8c4b0")
    fc = K.mflat("#ffd070")
    placed = []
    for i in range(n):
        for _ in range(20):
            x = cx + rnd.uniform(-spread, spread)
            y = cy + rnd.uniform(-spread, spread) * 0.8
            rr = rnd.uniform(0.28, 0.55)
            if all(math.hypot(x - a, y - b) > rr + c for a, b, c in placed):
                break
        placed.append((x, y, rr))
        rot = rnd.uniform(0, 6.28)
        notch = rnd.uniform(0.3, 0.5)
        pts = [(x, y)]
        for k in range(15):
            a = rot + notch / 2 + (2 * math.pi - notch) * k / 14
            q = rr * (1 + rnd.uniform(-0.05, 0.05))
            pts.append((x + q * math.cos(a), y + q * math.sin(a)))
        hpoly(pts, 0.02, rnd.choice((pad, pad2)), layer, th=0.02)
    for (x, y, rr) in placed[:flowers]:
        for k in range(7):
            a = 2 * math.pi * k / 7
            ob = C.add_prim("cone", "Petal", fl, vertices=6, radius1=0.06, radius2=0.0, depth=0.22,
                            location=(x + math.cos(a) * 0.07, y + math.sin(a) * 0.07, 0.12))
            ob.matrix_world = ob.matrix_world @ Matrix.Rotation(math.radians(35), 4, Vector((-math.sin(a), math.cos(a), 0)))
            tagl(ob, layer)
        tagl(C.add_prim("sphere", "Core", fc, radius=0.05, location=(x, y, 0.13), segments=8, ring_count=6), layer)


def rowboat(cx, cy, rot, layer="front"):
    outer = K.mcine("#4c4640", name="hull", rim=1.0)
    stripe = K.mcine("#3c5654", name="hullstripe", rim=1.0)
    inner = K.mcine("#5e5446", name="hullin", rim=0.4)
    seat = K.mcine("#6a5c4a", name="seat")
    L, Wd, Hh = 3.6, 1.25, 0.42
    n = 11

    def sec(t, shrink=0.0):
        # half-width and height profile along the boat (t=-1 stern .. +1 bow)
        w = Wd / 2 * (1 - abs(t) ** 2.4 * (0.95 if t > 0 else 0.55)) - shrink
        z = Hh + 0.08 * t * t
        return max(w, 0.02), z

    bm = bmesh.new()
    rings_o, rings_i = [], []
    for k in range(n):
        t = -1 + 2 * k / (n - 1)
        w, z = sec(t)
        wi, _ = sec(t, 0.05)
        yy = t * L / 2
        ro, ri = [], []
        for m_ in range(7):  # U profile from left gunwale down to the waterline and up
            a = math.pi * m_ / 6
            ro.append(bm.verts.new((-w * math.cos(a), yy, max(0.0, z * (1 - math.sin(a))))))
            ri.append(bm.verts.new((-wi * math.cos(a), yy, max(0.06, (z - 0.03) * (1 - math.sin(a) * 0.9)))))
        rings_o.append(ro)
        rings_i.append(ri)
    for k in range(n - 1):
        for m_ in range(6):
            bm.faces.new((rings_o[k][m_], rings_o[k][m_ + 1], rings_o[k + 1][m_ + 1], rings_o[k + 1][m_]))
            bm.faces.new((rings_i[k][m_ + 1], rings_i[k][m_], rings_i[k + 1][m_], rings_i[k + 1][m_ + 1]))
        for m_ in (0, 6):
            bm.faces.new((rings_o[k][m_], rings_o[k + 1][m_], rings_i[k + 1][m_], rings_i[k][m_]))
    for ring_o, ring_i in ((rings_o[0], rings_i[0]), (rings_o[-1], rings_i[-1])):
        for m_ in range(6):
            bm.faces.new((ring_o[m_], ring_o[m_ + 1], ring_i[m_ + 1], ring_i[m_]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    hull = C.mesh_object("Hull", bm, outer)
    hull.data.materials.append(inner)
    # interior faces use material 1 (normals pointing up/in)
    for f in hull.data.polygons:
        if f.normal.z > 0.35 and abs(f.center.x) < Wd / 2 - 0.04:
            f.material_index = 1
    C.set_smooth(hull, False)
    Mw = Matrix.Translation((cx, cy, 0)) @ Matrix.Rotation(rot, 4, "Z")
    hull.matrix_world = Mw
    tagl(hull, layer)
    for (yy, w) in ((-0.6, 0.95), (0.55, 0.9)):
        b = box(Vector((0, yy, Hh - 0.1)), (w, 0.2, 0.05), seat, layer)
        b.matrix_world = Mw @ b.matrix_world
    b = box(Vector((0, -L / 2 + 0.1, Hh - 0.02)), (0.7, 0.08, 0.1), stripe, layer)
    b.matrix_world = Mw @ b.matrix_world
    # oar resting across
    o = tube([(-0.9, 0.1, Hh + 0.02), (0.2, -0.3, Hh + 0.04), (1.1, -0.65, 0.1)], 0.025, seat, layer)
    o.matrix_world = Mw @ o.matrix_world
    return Mw


def build_front(rnd):
    LAYERS["front"] = 15.0
    L = "front"
    # --- pier (cross planks on stringers, weathered grey-brown)
    top = STAND
    pl_a = wood_mat("#62584c", "#50473c")
    pl_b = wood_mat("#564e44", "#463f37")
    post = K.mcine("#3e342c", name="post", rim=1.0)
    y = 1.1
    k = 0
    while y > -19:
        wdt = rnd.uniform(0.2, 0.26)
        x0, x1 = -1.25 + rnd.uniform(-0.04, 0.03), 1.25 + rnd.uniform(-0.03, 0.05)
        box(Vector(((x0 + x1) / 2, y - wdt / 2, top - 0.03 + rnd.uniform(-0.012, 0.012))), (x1 - x0, wdt - 0.03, 0.06),
            pl_a if k % 3 else pl_b, L, rot=math.radians(rnd.uniform(-0.8, 0.8)))
        y -= wdt
        k += 1
    for sx in (-1.0, 1.0):
        box(Vector((sx, -9.0, top - 0.14)), (0.14, 20.2, 0.16), post, L)
    box(Vector((0, 1.08, top - 0.14)), (2.6, 0.12, 0.18), post, L)
    for yy in (-12.0, -8.0, -4.0):
        for sx in (-1.3, 1.3):
            tube([(sx, yy, 0.0), (sx, yy, top + 0.05)], 0.12, post, L, 8)
    for sx in (-1.34, 1.34):
        tube([(sx, 0.95, 0.0), (sx, 0.95, top + 0.95)], 0.13, post, L, 8)
        tube([(sx, 0.95, top + 0.95), (sx, 0.95, top + 0.99)], 0.1, post, L, 8)
    # rope coil + cleat on the right, bucket + tackle box on the left
    rope = K.mcine("#8a7a5c", name="rope", rim=0.8)
    for kk in range(3):
        ob = C.add_prim("torus", "Coil", rope, major_radius=0.2 - kk * 0.03, minor_radius=0.025, major_segments=16,
                        minor_segments=6, location=(0.85, -0.55, top + 0.03 + kk * 0.035))
        tagl(bpy.context.active_object, L)
    tube([(1.34, 0.95, top + 0.6), (1.2, 0.5, top + 0.1), (0.95, -0.2, top + 0.05)], 0.02, rope, L, 5)
    metal = K.mcine("#7c8286", name="bucket", spec=1.0, rim=1.0)
    tube([(-0.85, -1.2, top), (-0.85, -1.2, top + 0.34)], [0.17, 0.21], metal, L, 12)
    tube([(-0.85, -1.2, top + 0.335), (-0.85, -1.2, top + 0.345)], [0.2, 0.2], K.mcine("#4c5256", name="bucketin"), L, 12)
    box(Vector((-0.95, -0.35, top + 0.13)), (0.42, 0.26, 0.26), K.mcine("#3e4a3c", name="tackle", rim=1.0), L, rot=math.radians(8))
    box(Vector((-0.95, -0.35, top + 0.275)), (0.12, 0.05, 0.04), K.mcine("#8a8a80", name="handle"), L, rot=math.radians(8))
    # --- reeds: side clumps + a foreground frame bottom-left; the middle stays open
    reed_clump(rnd, -4.6, 0.6, 40, 1.1, 1.2, 2.2, 0.45)
    reed_clump(rnd, -6.4, 3.4, 30, 1.0, 1.0, 1.9)
    reed_clump(rnd, 6.9, 4.8, 30, 1.0, 1.0, 1.8)
    reed_clump(rnd, 4.6, 1.4, 16, 0.6, 0.7, 1.4, 0.3)
    reed_clump(rnd, -13.5, 19, 26, 1.6, 1.4, 2.6)
    reed_clump(rnd, 16.0, 30, 22, 1.8, 1.6, 2.8)
    # --- lily pads hug the reed beds
    lily_group(rnd, -3.9, 5.6, 5, 0.9, 1)
    lily_group(rnd, 4.4, 8.5, 4, 0.8, 1)
    lily_group(rnd, -8.8, 17.5, 5, 1.3, 0)
    lily_group(rnd, 11.5, 24.0, 4, 1.4, 1)
    # --- moored rowboat + stake
    rowboat(-8.4, 12.5, math.radians(-28))
    tube([(-6.6, 15.2, 0.0), (-6.62, 15.2, 1.05)], 0.07, post, L, 6)
    tube([(-6.62, 15.2, 0.95), (-7.3, 14.3, 0.55), (-7.6, 13.9, 0.45)], 0.018, rope, L, 4)


# ============================================================================ render passes
def set_cam(mirror):
    cam = P.setup_camera(STAND)
    if mirror:
        cam.location = (CAM[0], CAM[1], -CAM[2])
        cam.rotation_euler = (math.radians(90 + P.PITCH), 0, 0)
    return cam


def render_layer(name, mirror):
    sc = bpy.context.scene
    for ob in sc.objects:
        if ob.type == "MESH":
            ob.hide_render = ob.get("L") != name
    set_cam(mirror)
    img = K.render(f"lake_{name}{'_m' if mirror else ''}")
    return img[::-1].copy() if mirror else img


def hash2(a, b):
    v = np.sin(a * 12.9898 + b * 78.233) * 43758.5453
    return v - np.floor(v)


def vnoise1(x, seed=0.0):
    i = np.floor(x)
    f = x - i
    a, b = hash2(i, seed), hash2(i + 1, seed)
    u = f * f * (3 - 2 * f)
    return a + (b - a) * u


def compose_back(far, far_m, near_m):
    d, pxo, pyo = K.pixel_rays()
    el, ang, daz = angles(d)
    jj, ii = np.mgrid[0:H, 0:W].astype(np.float32)
    # ---------------------------------------------------------------- sky + layers (above the water)
    img = sky_image(d)
    haze = haze_image(d)
    dm = d.copy()
    dm[..., 2] *= -1
    img_m = sky_image(dm)
    haze_m = haze_image(dm)
    occ = np.zeros((H, W), bool)
    for name in ["mtn", "hills", "ridge", "bay", "head", "shore"]:
        dist = LAYERS[name]
        for mirror, lay, base, hz in ((False, far[name], img, haze), (True, far_m[name], img_m, haze_m)):
            a = lay[..., 3] > 0.5
            f = 1 - math.exp(-dist / FOG_D)
            col = K.mix(lay[..., :3], hz, f)
            # ground mist hugging the waterline of this layer
            wl = K.water_y_row(dist, STAND)
            hpx = (wl - jj) if not mirror else (jj - wl)
            mist_amt = {"shore": 0.62, "head": 0.55, "bay": 0.5, "ridge": 0.35}.get(name, 0.0)
            if mist_amt:
                wisp = 0.65 + 0.35 * vnoise1(ii / 23.0, 3.0) + 0.2 * vnoise1(ii / 7.0, 5.0)
                mist = mist_amt * np.exp(-np.clip(hpx, 0, None) / 3.2) * np.clip(wisp, 0, 1.1)
                col = K.mix(col, np.clip(hz * 1.06 + 0.02, 0, 1), np.clip(mist, 0, 0.9))
            if not mirror:
                # warm rim on the top/right edges facing the sun, fading with distance from the sun
                up_empty = ~K.shift(a, 1, 0, False)
                right_empty = ~K.shift(a, 0, -1, False)
                g = np.clip(glow(ang, 1.0) * 1.1, 0, 1)
                k = (up_empty * 1.0 + right_empty * 0.6).clip(0, 1) * a * g * (1 - f * 0.8)
                col = K.mix(col, np.clip(hz * 1.25 + 0.08, 0, 1), np.clip(k, 0, 0.85))
                occ |= a
            base[a] = col[a]
    # veiling glare over everything near the sun
    glare = (0.55 * np.exp(-ang / 1.6) + 0.18 * np.exp(-ang / 6.0))[..., None] * GLOW
    img = np.clip(K.to_srgb(K.to_lin(img) + glare * 0.8), 0, 1)
    img_m = np.clip(K.to_srgb(K.to_lin(img_m) + glare * 0.4), 0, 1)
    # near objects (props) reflected
    a_n = near_m[..., 3] > 0.5
    img_m[a_n] = K.mix(near_m[..., :3], img_m, 0.1)[a_n]

    # ---------------------------------------------------------------- water
    water = (el < 0) & ~occ
    dep = np.clip(-el, 0.02, 90)
    t = CAM[2] / np.clip(-d[..., 2], 1e-4, None)
    X = CAM[0] + t * d[..., 0]
    Y = CAM[1] + t * d[..., 1]
    near = 1 - K.smoothstep(1.5, 9.0, dep)  # 1 far .. 0 near  (in terms of depression)
    fres = 0.02 + 0.98 * (1 - np.sin(np.radians(dep))) ** 5
    fres = np.clip(fres * 1.05 + 0.03, 0, 1)
    # ripples: world-space wave field, sampled so that far rows do not alias into noise
    wv = (np.sin(Y * 1.9 + X * 0.35 + 1.3) + 0.6 * np.sin(Y * 3.3 - X * 0.6 + 0.4) + 0.35 * np.sin(Y * 5.1 + X * 1.4 + 2.2))
    wv = wv / 1.95
    far_blend = K.smoothstep(40.0, 110.0, Y)
    rowrand = hash2(np.floor(jj), 11.0) * 2 - 1
    ripple = wv * (1 - far_blend) + rowrand * far_blend
    ax = np.round(ripple * (0.6 + 2.4 * K.smoothstep(4.0, 20.0, dep))).astype(np.int32)
    ay = np.round(ripple * (0.4 + 1.6 * K.smoothstep(4.0, 20.0, dep))).astype(np.int32)
    si = np.clip(ii.astype(np.int32) + ax, 0, W - 1)
    sj = np.clip(jj.astype(np.int32) + ay, 0, H - 1)
    refl = img_m[sj, si]
    body = K.mix(K.hexf("#153a3e"), K.hexf("#1e4648"), K.smoothstep(4.0, 60.0, Y))
    wcol = K.mix(body, refl, fres)
    # sun path: slope-distribution sheen + dash glitter
    S = np.array(SUN, np.float32)
    nrm = S[None, None, :] - d
    nrm = nrm / np.linalg.norm(nrm, axis=-1, keepdims=True)
    tx = nrm[..., 0] / np.clip(nrm[..., 2], 1e-3, None)
    ty = nrm[..., 1] / np.clip(nrm[..., 2], 1e-3, None)
    p = np.exp(-(tx ** 2 / (2 * 0.05 ** 2) + ty ** 2 / (2 * 0.095 ** 2)))
    pb = np.exp(-(tx ** 2 / (2 * 0.12 ** 2) + ty ** 2 / (2 * 0.2 ** 2)))
    lin = K.to_lin(wcol) + (p * 0.30 + pb * 0.06)[..., None] * GLOW
    dash = np.clip(1 + 5 * K.smoothstep(3.0, 18.0, dep), 1, 6)
    off = hash2(np.floor(jj), 3.0) * 40
    seg = np.floor((ii + off) / dash)
    r = hash2(seg, np.floor(jj) + 0.5)
    rows_ok = (np.floor(jj) % 2 == 0) | (dep > 5)
    spark = (r < np.clip(p ** 1.4 * (0.95 - 0.7 * K.smoothstep(3, 22, dep)), 0, 1)) & rows_ok & (p > 0.14)
    spark2 = spark & (r < p ** 2.2 * 0.5)
    lin = np.where(spark[..., None], lin + K.to_lin(K.hexf("#ffcf86")) * 0.9, lin)
    lin = np.where(spark2[..., None], K.to_lin(K.hexf("#fff3d6")) * 1.0, lin)
    wcol = np.clip(K.to_srgb(lin), 0, 1)
    # mist over the far water
    mist = 0.42 * K.smoothstep(70.0, 190.0, Y) * (0.8 + 0.2 * vnoise1(ii / 31.0, 9.0))
    wcol = K.mix(wcol, np.clip(haze * 1.04, 0, 1), mist)
    img[water] = wcol[water]
    # ---------------------------------------------------------------- long light: soft rays fanning from the sun
    sx, sy = W / 2 + SUN_PX[0], H / 2 - SUN_PX[1]
    th = np.arctan2(jj - sy, ii - sx)
    rr = np.hypot(jj - sy, ii - sx)
    rays = np.clip(np.sin(th * 23.0 + 0.7) * 0.5 + np.sin(th * 37.0 + 2.1) * 0.35 + np.sin(th * 11.0) * 0.4, 0, None)
    rays = rays * np.exp(-rr / 230.0) * K.smoothstep(10.0, 60.0, rr) * (jj > sy)
    img = np.clip(K.to_srgb(K.to_lin(img) + (rays * 0.035)[..., None] * GLOW), 0, 1)
    # ---------------------------------------------------------------- vignette (on the visible crop)
    cx0, cy0, cx1, cy1 = K.CROP
    vx = (ii - (cx0 + cx1) / 2) / ((cx1 - cx0) / 2)
    vy = (jj - (cy0 + cy1) / 2) / ((cy1 - cy0) / 2)
    v = K.smoothstep(0.55, 1.45, np.sqrt(vx * vx * 0.85 + vy * vy)) * 0.42
    img = img * (1 - v[..., None] * (1 - np.array([0.52, 0.6, 0.72], np.float32)))
    return img, water


def main():
    C.reset_scene()
    K.clear_mats()
    rnd = random.Random(20260928)
    build_far(rnd)
    build_front(random.Random(7))
    sc = bpy.context.scene
    sc.render.film_transparent = True
    bpy.context.view_layer.update()
    far, far_m = {}, {}
    for name in ["mtn", "hills", "ridge", "bay", "head", "shore"]:
        far[name] = render_layer(name, False)
        far_m[name] = render_layer(name, True)
    front = render_layer("front", False)
    front_m = render_layer("front", True)
    back, water = compose_back(far, far_m, front_m)
    np.save(os.path.join(K.RAW, "lake_back_float.npy"), back)
    np.save(os.path.join(K.RAW, "lake_front_float.npy"), front)
    finish(back, front)


def finish(back, front):
    # ---- back: grade -> palette (from the visible crop) -> ordered dither on gradients
    b = K.grade(back[..., :3], sat=0.92, split=1.0, contrast=0.06)
    x0, y0, x1, y1 = K.CROP
    crop_mask = np.zeros(b.shape[:2], bool)
    crop_mask[y0:y1, x0:x1] = True
    pal = K.kmeans_palette(b, 56, mask=crop_mask, keep=("#fff3d6", "#ffcf86"), weight_pow=0.5)
    bq = K.quantize(b, pal, dither=0.55)
    K.save(bq, os.path.join(K.OUT, "lake_back.png"))
    # ---- front: hard alpha -> grade -> palette -> rim on sun-facing edges (>=2px thick parts)
    f = K.harden(front)
    f = K.grade(f, sat=0.9, split=1.0, contrast=0.06)
    a = f[..., 3] > 0.5
    fpal = K.kmeans_palette(f, 32, mask=a, weight_pow=0.6)
    fq = K.quantize(f, fpal)
    fq[..., 3] = f[..., 3]
    thick = a & K.shift(a, 0, 1, False)  # has an opaque pixel on its left
    fq, _ = K.rim_light(fq, K.hexf("#f2c28a"), strength=0.42, right=1.0, top=0.6, topright=0.7, mask=thick)
    K.save(fq, os.path.join(K.OUT, "lake_front.png"))
    layout = dict(id="lake", style="cinematic", standH=STAND, widthPx=W, heightPx=H, ppu=P.PPU,
                  camBack=P.CAM_BACK, camUp=P.CAM_UP, pitch=P.PITCH, focalPx=P.F_PX,
                  depthZ=[0, 8, 20, 40, 140, 176], depthV=[1.2, 3, 6, 8, 6, 1],
                  mode="shore", zNear=1.2, zFar=176, xLim=90, waterTint="#2a6468", waterDeep="#0a1e24",
                  ambient="dusk", clouds=False, birds=True,
                  sunPx=[round(W / 2 + SUN_PX[0], 1), round(H / 2 - SUN_PX[1], 1)])
    with open(os.path.join(K.OUT, "stage_lake.json"), "w", encoding="utf-8") as fo:
        json.dump(layout, fo, indent=1)
    print("LAKE done")


if __name__ == "__main__":
    if "--finish" in sys.argv:
        C.reset_scene()
        finish(np.load(os.path.join(K.RAW, "lake_back_float.npy")), np.load(os.path.join(K.RAW, "lake_front_float.npy")))
    else:
        main()
