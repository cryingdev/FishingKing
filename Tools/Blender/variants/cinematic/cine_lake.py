"""
FishingKing - cinematic variant of the lake stage (golden hour).

Blender renders only geometry, one pass per depth layer (flat silhouettes far away, cinematic-shaded
props near the camera), each pass twice: through the stage camera and through the camera mirrored in
the water plane (-> exact reflections, flipped vertically). The front layer gets a third, flat "class id"
pass so the pixel post can treat planks, pads, the boat and posts differently. Sky, sun, haze, water
(Fresnel, sun glitter), aerial perspective, mist, rim light, vignette and the palette/dither are numpy.
Reeds are drawn directly as pixel-perfect 1-2 px strokes (projected 3D curves) - thin Blender tubes
alias into broken scribbles at this size.

Run:  blender -b --python Tools/Blender/variants/cinematic/cine_lake.py [-- --finish]
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
HORIZON_PY = K.HORIZON_PY
SUN_PX = K.SUN_PX                        # sun centre (px from image centre, y up) - shared with the angler rig
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


# (centre elevation deg, thickness deg (~9 px per deg), daz from, daz to, phase)
CLOUDS = [(1.95, 0.5, 10.5, 17.0, 0.3), (2.22, 0.46, -39.0, -32.0, 1.7), (1.62, 0.34, 20.0, 24.0, 2.6)]


def clouds(el, daz, ang):
    """Two or three tapered golden-hour stratus lenses, 2-4 px thick, flat lit underside."""
    a = np.zeros_like(el)
    col = np.zeros(el.shape + (3,), np.float32)
    g = np.clip(glow(ang, 1.0), 0, 1.4)
    sky = sky_base(el, daz)
    for (ec, th, a0, a1, ph) in CLOUDS:
        u = np.clip((daz - a0) / (a1 - a0), 0, 1)
        env = np.maximum(np.sin(np.pi * u), 0.0) ** 0.55
        top = ec + th * 0.5 * env * (1.0 + 0.22 * np.sin(daz * 0.8 + ph) + 0.12 * np.sin(daz * 2.3 + ph * 2))
        bot = ec - th * 0.5 * env * 0.75
        m = (el < top) & (el > bot) & (env > 0.18)
        a = np.where(m, 1.0, a)
        under = m & (el < bot + 0.115)
        body = K.mix(sky * np.array([0.8, 0.74, 0.8], np.float32), K.hexf("#7a5c68"), 0.35 + 0.3 * np.clip(g, 0, 1))
        lit = K.mix(np.clip(sky * 1.08, 0, 1), K.hexf("#ffe2ae"), np.clip(0.35 + g * 0.8, 0, 1))
        c = np.where(under[..., None], lit, body)
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


def canopy_mass(rnd, x, Y, z0, w, h, mat, layer):
    """Far deciduous canopy: a low lumpy dome with no trunk (a lollipop at 1 m per pixel reads as a toy)."""
    n = 16
    ph = rnd.uniform(0, 6.28)
    pts = [(x - w / 2, z0)]
    for k in range(n + 1):
        u = k / n
        zz = h * (math.sin(math.pi * u) ** 0.55) * (1 + 0.16 * math.sin(u * 11 + ph) + rnd.uniform(-0.07, 0.07))
        pts.append((x - w / 2 + w * u, z0 + max(zz, 0.0)))
    pts.append((x + w / 2, z0))
    vpoly(pts, Y, mat, layer)


def crown_mass(rnd, x, Y, z0, w, h, mat, layer):
    """Deciduous group at 190-260 m: 4-7 overlapping lumpy ellipses along x -> one irregular dome, no trunks."""
    n = rnd.randint(4, 7)
    for i in range(n):
        u = (i + 0.5) / n
        cx = x - w / 2 + w * u + rnd.uniform(-0.08, 0.08) * w
        rr = w / n * rnd.uniform(0.9, 1.4)
        hh = h * (0.55 + 0.45 * math.sin(math.pi * u)) * rnd.uniform(0.8, 1.1)
        ph = rnd.uniform(0, 6.28)
        pts = []
        for k in range(14):
            a = math.pi * k / 13
            q = 1 + 0.15 * math.sin(4 * a + ph) + rnd.uniform(-0.06, 0.06)
            pts.append((cx + math.cos(a) * rr * q, z0 + max(hh * 0.25 + math.sin(a) * hh * 0.75 * q, 0.0)))
        pts = [(cx + rr, z0)] + pts + [(cx - rr, z0)]
        vpoly(pts, Y + rnd.uniform(-0.3, 0.3), mat, layer)


def snag(rnd, x, Y, z0, h, mat, layer, px_m):
    """Dead tree: a bare 1 px trunk taller than the canopy with 2-3 short stubs."""
    tw = max(px_m * 0.55, h * 0.02)
    vpoly([(x - tw, z0), (x + tw, z0), (x + tw * 0.5, z0 + h), (x - tw * 0.5, z0 + h)], Y - 0.5, mat, layer)
    for k in range(rnd.randint(2, 3)):
        zz = z0 + h * rnd.uniform(0.6, 0.88)
        s = rnd.choice((-1, 1))
        L = max(h * rnd.uniform(0.04, 0.07), px_m * 1.2)
        vpoly([(x, zz), (x + s * L, zz + L * 1.1), (x + s * L, zz + L * 1.1 + px_m * 0.9), (x, zz + px_m * 1.1)],
              Y - 0.5, mat, layer)


def forest_clumps(rnd, px0, px1, prof, Y, mat, layer, base_h, spacing_px=(2.2, 3.8), gap_px=(10, 25),
                  broad=0.25, snags=2):
    """Trees in clumps of 3-8 (taller in the middle), 10-25 px gaps with only low canopy, heights x0.6-1.6
    that follow the hill profile (crests carry taller stands), ~25% broadleaf masses, a few tall snags."""
    xs_p = [p for p, _ in prof]
    hs_p = [h for _, h in prof]
    hmin, hmax = min(hs_p), max(hs_p)
    px_m = (Y - CAM[1]) / P.F_PX       # metres per pixel at this distance
    x = px0 + rnd.uniform(0, 6)
    clump_x = []
    while x < px1:
        n = rnd.randint(3, 8)
        cs = rnd.uniform(0.8, 1.25)
        xs = []
        for i in range(n):
            if x > px1:
                break
            hp = float(np.interp(x, xs_p, hs_p))
            crest = (hp - hmin) / max(hmax - hmin, 1e-3)
            mid = 1 - abs((i - (n - 1) / 2) / max((n - 1) / 2, 1))
            f = cs * (0.62 + 0.55 * mid) * (0.85 + 0.3 * crest) * rnd.uniform(0.85, 1.12)
            f = min(max(f, 0.6), 1.6)
            X, Z = screen_to_plane(x, hp - 0.8, Y)
            if rnd.random() < broad:
                canopy_mass(rnd, X, Y - 1, max(Z - px_m, 0), base_h * f * rnd.uniform(0.9, 1.3), base_h * f * 0.55,
                            mat, layer)
            else:
                conifer(rnd, X, Y - 1, max(Z - px_m, 0), base_h * f, mat, layer, 0.3)
            xs.append(x)
            x += rnd.uniform(*spacing_px)
        clump_x.append(xs)
        # gap: only a low rounded canopy (never bald, never a comb)
        g = rnd.uniform(*gap_px)
        gx = x + rnd.uniform(1, 3)
        while gx < min(x + g - 2, px1):
            hp = float(np.interp(gx, xs_p, hs_p))
            X, Z = screen_to_plane(gx, hp - 0.8, Y)
            canopy_mass(rnd, X, Y - 0.5, max(Z - px_m, 0), px_m * rnd.uniform(6, 11), px_m * rnd.uniform(2.0, 4.0),
                        mat, layer)
            gx += rnd.uniform(4, 7)
        x += g
    # tall snags poke out of a few clumps
    cands = [c for c in clump_x if len(c) >= 3]
    for c in rnd.sample(cands, min(snags, len(cands))):
        sx = c[len(c) // 2] + rnd.uniform(-1.5, 1.5)
        hp = float(np.interp(sx, xs_p, hs_p))
        X, Z = screen_to_plane(sx, hp - 0.8, Y)
        snag(rnd, X, Y - 2, max(Z - px_m, 0), base_h * rnd.uniform(1.45, 1.65), mat, layer, px_m)


def shore_trees(rnd, x0, x1, taper_fn, ys, mats, layer, hrange, broad=0.38):
    """Near shore (190-260 m): clumps of 3-7 crowns 1.0-1.8 m apart, 3-8 m gaps showing the understory,
    heights x0.6-1.5 per clump, merged broadleaf masses, the odd tall spruce."""
    x = x0 + rnd.uniform(0, 2)
    while x < x1:
        n = rnd.randint(3, 7)
        cs = rnd.uniform(0.6, 1.5)
        for i in range(n):
            if x > x1:
                break
            tp = taper_fn(x)
            mid = 1 - abs((i - (n - 1) / 2) / max((n - 1) / 2, 1))
            h = rnd.uniform(*hrange) * cs * (0.7 + 0.45 * mid) * (0.35 + 0.65 * tp)
            y = rnd.uniform(*ys)
            mat = mats[min(int((y - ys[0]) / (ys[1] - ys[0]) * len(mats)), len(mats) - 1)]
            if rnd.random() < broad:
                crown_mass(rnd, x, y, 0.8, h * rnd.uniform(0.9, 1.4), h * 0.8, mat, layer)
            else:
                conifer(rnd, x, y, 0.3, h * 1.1 * (1.25 if rnd.random() < 0.12 else 1.0), mat, layer)
            x += rnd.uniform(1.0, 1.8)
        x += rnd.uniform(3.0, 8.0)


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
RIDGE = [(-340, 14), (-270, 19), (-200, 15), (-140, 22), (-80, 17), (-30, 12), (10, 6), (30, 2), (50, 0.2)]
HEADL = [(205, 0.2), (225, 5), (260, 11), (300, 15), (345, 18)]


def build_far(rnd):
    # --- layer 1: distant range (3200 m): lowest right at the sun so the disc sinks into it
    LAYERS["mtn"] = 3200.0
    skyline([(-340, 12), (-250, 20), (-170, 15), (-90, 24), (-20, 17), (40, 10), (80, 6.5), (100, 5.2), (125, 6.5),
             (170, 12), (230, 21), (290, 16), (340, 23)], 3200, K.mflat("#6a5e6e"), "mtn", step=3, jag=0.8, rnd=rnd)
    # --- layer 2: rolling hills (1400 m), open valley towards the sun
    LAYERS["hills"] = 1400.0
    skyline([(-340, 10), (-280, 16), (-210, 11), (-150, 19), (-90, 13), (-40, 9), (10, 5), (45, 2.5), (80, 1.2),
             (140, 1.6), (190, 4), (240, 12), (300, 17), (340, 14)], 1400, K.mflat("#4a4650"), "hills", step=2, jag=0.5, rnd=rnd)
    # --- layer 3: forested ridge (520 m) left half + headland right: clumped stands, gaps, snags
    LAYERS["ridge"] = 520.0
    m = K.mflat("#2f3438")
    skyline(RIDGE, 520, m, "ridge", step=1.5, jag=0.9, rnd=rnd)
    forest_clumps(rnd, -338, 22, RIDGE, 520, m, "ridge", 8.5, snags=3)
    skyline(HEADL, 520, m, "ridge", step=1.5, jag=0.9, rnd=rnd)
    forest_clumps(rnd, 214, 342, HEADL, 520, m, "ridge", 8.5, gap_px=(8, 18), snags=1)
    # --- layer 4: far end of the bay (900 m): thin wooded shoreline across the gap
    LAYERS["bay"] = 900.0
    mb = K.mflat("#262c2e")
    skyline([(0, 1.0), (60, 1.6), (120, 1.3), (180, 2.0), (230, 1.4)], 900, mb, "bay", step=1, jag=0.35, rnd=rnd)
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
    X_END = 11.0
    vpoly([(-140, 0), (-140, 0.8), (X_END - 4, 0.6), (X_END + 2.5, 0.12), (X_END + 3, 0)], 180, bank, "shore")
    # understory band (bushes) so trunks never show sky between them; gently rolling, not a comb
    pts = [(-140, 0)]
    x = -140.0
    while x < X_END:
        taper = min(1.0, (X_END - x) / 12.0)
        pts.append((x, (2.6 + 1.0 * math.sin(x * 0.21) + 0.6 * math.sin(x * 0.57 + 1.0) + rnd.uniform(0, 0.5)) * taper + 0.3))
        x += rnd.uniform(1.2, 2.0)
    pts += [(X_END + 1, 0.2), (X_END + 1, 0)]
    vpoly(pts, 181, tree_b, "shore")
    shore_trees(rnd, -140, X_END, lambda xx: min(1.0, (X_END - xx) / 16.0), (182, 196), [tree_a, tree_b, tree_c],
                "shore", (5.0, 9.0))
    # reeds on the point
    for _ in range(18):
        rx = rnd.uniform(X_END - 6, X_END + 3)
        hh = rnd.uniform(0.8, 2.0)
        vpoly([(rx - 0.1, 0), (rx + 0.1, 0), (rx + rnd.uniform(-0.3, 0.3), hh)], 179, tree_a, "shore")
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
        pts.append((x, (1.9 + 0.8 * math.sin(x * 0.25) + rnd.uniform(0, 0.5)) * taper + 0.3))
        x += rnd.uniform(1.2, 2.0)
    pts += [(Xh0 + 300, 0)]
    vpoly(pts, 250, hb, "head")
    shore_trees(rnd, Xh0 + 1.5, Xh0 + 300, lambda xx: min(1.0, (xx - Xh0) / 20.0), (251, 262), [ha, hb], "head",
                (6.0, 10.0), broad=0.32)


# ----------------------------------------------------------------------------- near props (front layer)
DECK = []          # (y_far, y_near, z_top) per plank, for the pixel pass (gaps + grazing highlights)
GUNWALES = []      # 3D polylines of the rowboat's gunwale tops
REED_CLUMPS = [    # cx, cy, blades, spread, hmin, hmax, cattail heads
    (-4.6, 0.6, 18, 1.1, 1.2, 2.2, 6),
    (-6.4, 3.4, 16, 1.0, 1.0, 1.9, 5),
    (6.9, 4.8, 16, 1.0, 1.0, 1.8, 5),
    (4.6, 1.4, 12, 0.6, 0.7, 1.4, 4),
    (-13.5, 19.0, 14, 1.6, 1.4, 2.6, 5),
    (16.0, 30.0, 12, 1.8, 1.6, 2.8, 4),
]
# class ids of front-layer materials (id pass)
MAT_ID = dict(deckA=1, deckB=2, deckC=3, padA=4, padB=5, hull=6, hullin=7, thwart=8, endpost=9, post=10,
              petal=11, core=12, rope=13, bucket=14, tackle=15, oar=16, handle=17)


def lily_group(rnd, cx, cy, n, spread, flowers=1, layer="front"):
    """Flat pads (their colour is computed per pixel from the local water in compose_front)."""
    pad = K.mflat("#56663f", name="padA")
    pad2 = K.mflat("#4e5e3a", name="padB")
    fl = K.mflat("#e8c4b0", name="petal")
    fc = K.mflat("#ffd070", name="core")
    placed = []
    for i in range(n):
        for _ in range(20):
            x = cx + rnd.uniform(-spread, spread)
            y = cy + rnd.uniform(-spread, spread) * 0.8
            rr = rnd.uniform(0.3, 0.55)
            if all(math.hypot(x - a, y - b) > rr + c for a, b, c in placed):
                break
        placed.append((x, y, rr))
        # notch opens sideways or towards the camera, never away (it would vanish in the foreshortening)
        rot = math.radians(rnd.choice((rnd.uniform(-170, -120), rnd.uniform(-60, -10), rnd.uniform(160, 200))))
        notch = rnd.uniform(0.55, 0.75)
        pts = [(x, y)]
        for k in range(15):
            a = rot + notch / 2 + (2 * math.pi - notch) * k / 14
            q = rr * (1 + rnd.uniform(-0.04, 0.04))
            pts.append((x + q * math.cos(a), y + q * math.sin(a)))
        ob = hpoly(pts, 0.02, pad if i % 2 == 0 else pad2, layer, th=0.02)
        ob["nomirror"] = True
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
    stripe = K.mcine("#3c5654", name="hull", rim=1.0)
    inner = K.mcine("#96856a", name="hullin", rim=0.2)          # sky-lit inside: the lighter read of a boat
    thwart = K.mcine("#a08a68", name="thwart", rim=0.4)
    oar = K.mcine("#6a5c4a", name="oar")
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
    for f in hull.data.polygons:
        if f.normal.z > 0.35 and abs(f.center.x) < Wd / 2 - 0.04:
            f.material_index = 1
    C.set_smooth(hull, False)
    Mw = Matrix.Translation((cx, cy, 0)) @ Matrix.Rotation(rot, 4, "Z")
    hull.matrix_world = Mw
    tagl(hull, layer)
    # two thwarts spanning the inside + a stern seat
    for (yy, w) in ((-0.62, 1.08), (0.52, 1.06)):
        b = box(Vector((0, yy, Hh - 0.07)), (w, 0.22, 0.06), thwart, layer)
        b.matrix_world = Mw @ b.matrix_world
    b = box(Vector((0, -L / 2 + 0.28, Hh - 0.08)), (0.8, 0.3, 0.06), thwart, layer)
    b.matrix_world = Mw @ b.matrix_world
    b = box(Vector((0, -L / 2 + 0.06, Hh - 0.02)), (0.66, 0.06, 0.1), stripe, layer)
    b.matrix_world = Mw @ b.matrix_world
    o = tube([(-0.5, -0.2, Hh + 0.03), (0.2, -0.45, Hh + 0.05), (1.1, -0.75, 0.1)], 0.025, oar, layer)
    o.matrix_world = Mw @ o.matrix_world
    # gunwale top edges (pixel-drawn rim in compose_front)
    for s in (-1, 1):
        line = []
        for k in range(21):
            t = -1 + 2 * k / 20
            w, z = sec(t)
            line.append(tuple(Mw @ Vector((s * (w - 0.015), t * L / 2, z + 0.01))))
        GUNWALES.append(line)
    return Mw


def build_front(rnd):
    LAYERS["front"] = 15.0
    L = "front"
    DECK.clear()
    GUNWALES.clear()
    # --- pier: cross planks on stringers, weathered grey-brown, no texture seams (gaps/highlights are pixel rows)
    top = STAND
    decks = [K.mcine("#62584c", name="deckA", rim=0.3), K.mcine("#574f45", name="deckB", rim=0.3),
             K.mcine("#5e5448", name="deckC", rim=0.3)]
    post = K.mcine("#3e342c", name="post", rim=1.0)
    y = 1.1
    k = 0
    while y > -19:
        wdt = rnd.uniform(0.2, 0.26)
        x0, x1 = -1.25 + rnd.uniform(-0.04, 0.03), 1.25 + rnd.uniform(-0.03, 0.05)
        jz = rnd.uniform(-0.01, 0.01)
        box(Vector(((x0 + x1) / 2, y - wdt / 2, top - 0.03 + jz)), (x1 - x0, wdt - 0.03, 0.06),
            decks[(k * 7 + rnd.randint(0, 1)) % 3], L, rot=math.radians(rnd.uniform(-0.5, 0.5)))
        DECK.append((y - 0.015, y - wdt + 0.015, top + jz))
        y -= wdt
        k += 1
    for sx in (-1.0, 1.0):
        box(Vector((sx, -9.0, top - 0.14)), (0.14, 20.2, 0.16), post, L)
    box(Vector((0, 1.08, top - 0.14)), (2.6, 0.12, 0.18), post, L)
    for yy in (-12.0, -8.0, -4.0):
        for sx in (-1.3, 1.3):
            tube([(sx, yy, 0.0), (sx, yy, top + 0.05)], 0.12, post, L, 8)
    # end posts: low (+0.25 m), slim, warm dark wood with a rim - they frame, not compete
    endp = K.mcine("#4a3e32", name="endpost", rim=1.0)
    for sx in (-1.32, 1.32):
        tube([(sx, 0.95, 0.0), (sx, 0.95, top + 0.25)], 0.10, endp, L, 10)
        tube([(sx, 0.95, top + 0.25), (sx, 0.95, top + 0.27)], 0.08, endp, L, 10)
    # rope coil; its tail runs over the right edge down to a side post under the deck
    rope = K.mcine("#8a7a5c", name="rope", rim=0.8)
    for kk in range(3):
        C.add_prim("torus", "Coil", rope, major_radius=0.2 - kk * 0.03, minor_radius=0.025, major_segments=16,
                   minor_segments=6, location=(0.85, -0.55, top + 0.03 + kk * 0.035))
        tagl(bpy.context.active_object, L)
    tube([(0.98, -0.42, top + 0.04), (1.2, -0.28, top + 0.035), (1.31, -0.2, top - 0.02), (1.34, -0.15, top - 0.4)],
         0.02, rope, L, 5)
    metal = K.mcine("#7c8286", name="bucket", spec=1.0, rim=1.0)
    tube([(-0.85, -1.2, top), (-0.85, -1.2, top + 0.34)], [0.17, 0.21], metal, L, 12)
    tube([(-0.85, -1.2, top + 0.335), (-0.85, -1.2, top + 0.345)], [0.2, 0.2], K.mcine("#4c5256", name="bucket"), L, 12)
    box(Vector((-0.95, -0.35, top + 0.13)), (0.42, 0.26, 0.26), K.mcine("#3e4a3c", name="tackle", rim=1.0), L, rot=math.radians(8))
    box(Vector((-0.95, -0.35, top + 0.275)), (0.12, 0.05, 0.04), K.mcine("#8a8a80", name="handle"), L, rot=math.radians(8))
    # --- lily pads hug the reed beds near the pier (no far "dash" strings)
    lily_group(rnd, -3.9, 5.6, 5, 0.9, 1)
    lily_group(rnd, 4.4, 8.5, 4, 0.8, 1)
    # --- moored rowboat + stake
    rowboat(-8.4, 12.5, math.radians(-28))
    tube([(-6.6, 15.2, 0.0), (-6.62, 15.2, 1.05)], 0.07, post, L, 6)
    tube([(-6.62, 15.2, 0.95), (-7.3, 14.3, 0.55), (-7.6, 13.9, 0.45)], 0.018, rope, L, 4)


# ============================================================================ reeds (pixel strokes)
BLADE_TONES = [K.hexf(h) for h in ("#2f3a25", "#36402a", "#3c442b")]
DRY_TONE = K.hexf("#4c4630")
BLADE_RIM = K.hexf("#b58a55")
STEM_TONE = K.hexf("#353c27")
HEAD_TONE = K.hexf("#261a14")
HEAD_RIM = K.hexf("#b87a48")
HEAD_TOP = K.hexf("#dca462")
REED_BASE = K.hexf("#1a2a24")
RIPPLE = K.hexf("#7d9a94")


def reed_strokes(rnd):
    """Clumps -> list of strokes (sorted far to near). A stroke: kind, 3D points, tone, (head size)."""
    clumps = []
    for (cx, cy, nb, spread, hmin, hmax, nh) in REED_CLUMPS:
        d = math.hypot(cx - CAM[0], cy - CAM[1])
        s = []
        s.append(dict(kind="base", c=(cx, cy), r=(spread * 0.85, spread * 0.42), d=d))
        tries = 0
        blades = 0
        while blades < nb and tries < nb * 4:
            tries += 1
            x = cx + rnd.gauss(0, spread * 0.36)
            y = cy + rnd.gauss(0, spread * 0.2)
            hb = rnd.uniform(hmin * 0.6, hmax * 0.95)
            out = (x - cx) / max(spread, 1e-3)
            bend = max(-0.35, min(0.35, out * 0.3 + rnd.uniform(-0.18, 0.18)))
            pts = []
            for k in range(9):
                t = k / 8
                pts.append((x + bend * t * t * hb * 0.55, y, hb * (t - 0.3 * abs(bend) * t ** 3)))
            b0 = K.screen(pts[0], STAND)
            b1 = K.screen(pts[-1], STAND)
            if b1[1] > b0[1] - 2:       # tip must end clearly above its base on screen
                continue
            tone = DRY_TONE if rnd.random() < 0.2 else rnd.choice(BLADE_TONES)
            s.append(dict(kind="blade", pts=pts, tone=tone, bend=bend, d=d))
            blades += 1
        sizes = (["L", "L", "M", "M", "S", "S", "M"])[:nh]
        rnd.shuffle(sizes)
        for sz in sizes:
            x = cx + rnd.gauss(0, spread * 0.26)
            y = cy + rnd.gauss(0, spread * 0.16)
            h = rnd.uniform(hmax * 0.85, hmax * 1.05) * {"L": 1.0, "M": 0.92, "S": 0.82}[sz]
            lean = rnd.uniform(-0.05, 0.1)
            pts = [(x + lean * (k / 6) ** 1.5, y, h * k / 6) for k in range(7)]
            s.append(dict(kind="stem", pts=pts, size=sz, tone=STEM_TONE, d=d))
        clumps.append((d, s))
    clumps.sort(key=lambda c: -c[0])
    return clumps


def _put(img, x, y, col, a=1.0):
    if 0 <= y < img.shape[0] and 0 <= x < img.shape[1]:
        img[y, x, :3] = col
        img[y, x, 3] = a


def draw_reeds(img, clumps, mirror=False, dim=1.0):
    """Rasterise the reed strokes into an RGBA image. mirror: draw the water reflection (z -> -z)."""
    zs = -1.0 if mirror else 1.0
    for d, strokes in clumps:
        near = d < 20
        for st in strokes:
            if st["kind"] == "base":
                if mirror:
                    continue
                cx, cy = st["c"]
                rx, ry = st["r"]
                ring = []
                for k in range(24):
                    a = 2 * math.pi * k / 24
                    wob = 1 + 0.12 * math.sin(3 * a + cx)
                    ring.append(K.screen((cx + rx * wob * math.cos(a), cy + ry * wob * math.sin(a), 0.03), STAND))
                m = K.fill_poly(H, W, ring)
                img[m, :3] = REED_BASE
                img[m, 3] = 1
                # 1 px pale ripple where the stems meet the water: under the mass, broken into dashes
                cols = np.where(m.any(0))[0]
                for x in cols[1:-1]:
                    ys = np.where(m[:, x])[0]
                    y = ys.max() + 1
                    if y < H and img[y, x, 3] < 0.5 and K.hash01(x * 0.37 + cx) > 0.3:
                        _put(img, x, y, RIPPLE)
                continue
            pts3 = [(p[0], p[1], p[2] * zs) for p in st["pts"]]
            sp = [K.screen(p, STAND) for p in pts3]
            path = K.polyline_pixels(sp)
            n = len(path)
            if st["kind"] == "blade":
                thick_until = int(n * 0.5) if near else int(n * 0.3)
                for i, (x, y) in enumerate(path):
                    c = st["tone"] * dim
                    _put(img, x, y, c)
                    if i < thick_until and not mirror:
                        # 2 px base: the right pixel faces the sun -> warm rim (only above the waterline mass)
                        rim = 0.5 if i > n * 0.12 else 0.2
                        _put(img, x + 1, y, K.mix(c, BLADE_RIM, rim))
            else:  # stem + cattail head (the stem ends in the head + a short spike)
                sz = st["size"]
                hpx = {"L": 7, "M": 5, "S": 4}[sz] if near else {"L": 4, "M": 3, "S": 2}[sz]
                wpx = 2 if (sz != "S" and (near or sz == "L")) else 1
                hi = max(1, int(n * 0.86)) - 1          # path runs base -> tip; the head's lower end
                hx, hy = path[hi]
                for (x, y) in path[:hi + 1]:
                    _put(img, x, y, st["tone"] * dim)
                if mirror:
                    # reflected head: a dark blob below the reflected stem, no light
                    for yy in range(hpx):
                        for xx in range(wpx):
                            _put(img, hx + xx, hy + yy, HEAD_TONE * dim)
                    continue
                y_top = hy - hpx + 1
                for yy in range(y_top, hy + 1):
                    for xx in range(wpx):
                        c = HEAD_TONE
                        if xx == wpx - 1 and (wpx == 2 or near):
                            c = K.mix(HEAD_TONE, HEAD_RIM, 0.45)     # sun side
                        if yy == y_top:
                            c = K.mix(c, HEAD_TOP, 0.6)
                        _put(img, hx + xx, yy, c)
                # thin spike above the head
                for yy in range(1, (3 if sz != "S" else 2) if near else 2):
                    _put(img, hx, y_top - yy, st["tone"])
    return img


# ============================================================================ render passes
def set_cam(mirror):
    cam = P.setup_camera(STAND)
    if mirror:
        cam.location = (CAM[0], CAM[1], -CAM[2])
        cam.rotation_euler = (math.radians(90 + P.PITCH), 0, 0)
    return cam


def render_layer(name, mirror, suffix=""):
    sc = bpy.context.scene
    for ob in sc.objects:
        if ob.type == "MESH":
            ob.hide_render = ob.get("L") != name or (mirror and ob.get("nomirror", False))
    set_cam(mirror)
    img = K.render(f"lake_{name}{'_m' if mirror else ''}{suffix}")
    return img[::-1].copy() if mirror else img


def render_ids(name):
    """Flat class-id pass of one layer: every material slot swapped for its id colour, then restored."""
    sc = bpy.context.scene
    saved = []
    for ob in sc.objects:
        if ob.type != "MESH" or ob.get("L") != name:
            continue
        mats = [s.material for s in ob.material_slots]
        saved.append((ob, mats))
        for i, s in enumerate(ob.material_slots):
            base = s.material.name.split(".")[0] if s.material else ""
            k = MAT_ID.get(base, 0)
            ob.data.materials[i] = K.mflat(K.id_hex(k)) if k else K.mflat("#000000")
    img = render_layer(name, False, "_id")
    for ob, mats in saved:
        for i, m in enumerate(mats):
            ob.data.materials[i] = m
    return K.decode_ids(img)


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
            wl = K.water_y_row(dist, STAND)
            hpx = (wl - jj) if not mirror else (jj - wl)
            mist_amt = {"shore": 0.62, "head": 0.55, "bay": 0.5, "ridge": 0.35}.get(name, 0.0)
            if mist_amt:
                wisp = 0.65 + 0.35 * vnoise1(ii / 23.0, 3.0) + 0.2 * vnoise1(ii / 7.0, 5.0)
                mist = mist_amt * np.exp(-np.clip(hpx, 0, None) / 3.2) * np.clip(wisp, 0, 1.1)
                col = K.mix(col, np.clip(hz * 1.06 + 0.02, 0, 1), np.clip(mist, 0, 0.9))
            if not mirror:
                up_empty = ~K.shift(a, 1, 0, False)
                right_empty = ~K.shift(a, 0, -1, False)
                g = np.clip(glow(ang, 1.0) * 1.1, 0, 1)
                k = (up_empty * 1.0 + right_empty * 0.6).clip(0, 1) * a * g * (1 - f * 0.8)
                col = K.mix(col, np.clip(hz * 1.25 + 0.08, 0, 1), np.clip(k, 0, 0.85))
                occ |= a
            base[a] = col[a]
    glare = (0.55 * np.exp(-ang / 1.6) + 0.18 * np.exp(-ang / 6.0))[..., None] * GLOW
    img = np.clip(K.to_srgb(K.to_lin(img) + glare * 0.8), 0, 1)
    img_m = np.clip(K.to_srgb(K.to_lin(img_m) + glare * 0.4), 0, 1)
    sky_refl = img_m.copy()                 # clean mirror image (pads take their sky tint from it)
    a_n = near_m[..., 3] > 0.5
    img_m[a_n] = K.mix(near_m[..., :3], img_m, 0.1)[a_n]

    # ---------------------------------------------------------------- water
    water = (el < 0) & ~occ
    dep = np.clip(-el, 0.02, 90)
    t = CAM[2] / np.clip(-d[..., 2], 1e-4, None)
    X = CAM[0] + t * d[..., 0]
    Y = CAM[1] + t * d[..., 1]
    fres = 0.02 + 0.98 * (1 - np.sin(np.radians(dep))) ** 5
    fres = np.clip(fres * 1.05 + 0.03, 0, 1)
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
    # sun path: calm water under a ~1 deg sun only glitters in the far third. Slope-distribution sheen that
    # widens with depression, faded out by 4-9 deg; below that the water stays calm dark teal.
    S = np.array(SUN, np.float32)
    nrm = S[None, None, :] - d
    nrm = nrm / np.linalg.norm(nrm, axis=-1, keepdims=True)
    tx = nrm[..., 0] / np.clip(nrm[..., 2], 1e-3, None)
    ty = nrm[..., 1] / np.clip(nrm[..., 2], 1e-3, None)
    fade = 1 - K.smoothstep(4.0, 9.0, dep)
    sx = 0.05 + 0.02 * dep
    p = np.exp(-(tx ** 2 / (2 * sx ** 2) + ty ** 2 / (2 * 0.095 ** 2)))
    pb = np.exp(-(tx ** 2 / (2 * (2.2 * sx) ** 2) + ty ** 2 / (2 * 0.2 ** 2)))
    lin = K.to_lin(wcol) + ((p * 0.30 + pb * 0.06) * fade)[..., None] * GLOW
    # faint warm tint on the right of the calm near water (the sun is ahead-right)
    lin = lin + (0.010 * K.smoothstep(0.45, 1.0, ii / W) * (1 - fade) * water)[..., None] * GLOW
    # glitter: sparse horizontal 2-6 px dashes, thinning out towards the viewer
    dash = np.round(2 + 4 * K.smoothstep(0.6, 5.0, dep))
    off = hash2(np.floor(jj), 3.0) * 40
    seg = np.floor((ii + off) / dash)
    r = hash2(seg, np.floor(jj) + 0.5)
    row_keep = np.where(dep < 1.6, True, np.where(dep < 3.2, np.floor(jj) % 2 == 0, np.floor(jj) % 3 == 0))
    dens = np.clip(p ** 1.3 * (0.85 - 0.55 * K.smoothstep(1.0, 6.0, dep)), 0, 1) * fade
    spark = (r < dens) & row_keep & (p > 0.12)
    spark2 = spark & (r < dens * 0.35) & (dep < 3.0)
    lin = np.where(spark[..., None], lin + K.to_lin(K.hexf("#ffcf86")) * 0.9 * fade[..., None], lin)
    lin = np.where(spark2[..., None], K.to_lin(K.hexf("#fff3d6")) * 1.0, lin)
    wcol = np.clip(K.to_srgb(lin), 0, 1)
    mist = 0.42 * K.smoothstep(70.0, 190.0, Y) * (0.8 + 0.2 * vnoise1(ii / 31.0, 9.0))
    wcol = K.mix(wcol, np.clip(haze * 1.04, 0, 1), mist)
    img[water] = wcol[water]
    # ---------------------------------------------------------------- vignette (on the visible crop)
    cx0, cy0, cx1, cy1 = K.CROP
    vx = (ii - (cx0 + cx1) / 2) / ((cx1 - cx0) / 2)
    vy = (jj - (cy0 + cy1) / 2) / ((cy1 - cy0) / 2)
    v = K.smoothstep(0.55, 1.45, np.sqrt(vx * vx * 0.85 + vy * vy)) * 0.42
    img = img * (1 - v[..., None] * (1 - np.array([0.52, 0.6, 0.72], np.float32)))
    sky = (el >= 0) & ~occ
    return img, water, sky, sky_refl


PAD_RIM = K.hexf("#c89a62")
PLANK_HI = K.hexf("#c89a62")
PLANK_GAP = K.hexf("#120e0c")
GUNWALE = K.hexf("#c29566")


def compose_front(front, ids, back, sky_refl):
    """Pixel pass on the front layer: pads (lighter than the local water), plank gaps + grazing highlights,
    rowboat gunwale rims."""
    f = K.harden(front)
    a = f[..., 3] > 0.5
    # ---- lily pads: flat, 25% sky reflection, >= 8% lighter than the water under them, 2 tones + warm far rim
    pad = np.isin(ids, [MAT_ID["padA"], MAT_ID["padB"]]) & a
    if pad.any():
        base = np.where((ids == MAT_ID["padA"])[..., None], K.hexf("#56663f"), K.hexf("#4e5e3a"))
        col = K.mix(base, sky_refl, 0.25)
        near_edge = pad & ~K.shift(pad, -1, 0, False)          # lower neighbour is not pad -> shaded lip
        far_edge = pad & ~K.shift(pad, 1, 0, False)            # upper neighbour is not pad -> sun-grazed rim
        dark = col * 0.84
        wl = K.luma(back[..., :3])
        lo = 1.10 * wl / np.maximum(K.luma(dark), 1e-4)         # the darker tone must still clear the water
        hi = 1.38 * wl / np.maximum(K.luma(col), 1e-4)          # ...but the pad must not glow
        sc = np.clip(np.minimum(hi, 1.0), lo, None)[..., None]
        col = np.clip(col * sc, 0, 1)
        dark = np.clip(dark * sc, 0, 1)
        out = np.where(near_edge[..., None], dark, col)
        # warm rim only on the middle of the far edge (not a full outline)
        fe = far_edge & K.shift(far_edge, 0, 1, False) & K.shift(far_edge, 0, -1, False)
        out = np.where(fe[..., None], K.mix(col, PAD_RIM, 0.4), out)
        f[pad, :3] = out[pad]
    # ---- planks: 1 px dark gap between boards + 1 px warm grazing highlight on each board's far (sun) edge
    deck = np.isin(ids, [MAT_ID["deckA"], MAT_ID["deckB"], MAT_ID["deckC"]]) & a
    for i, (yf, yn, z) in enumerate(DECK):
        _, rf = K.screen((0.0, yf, z), STAND)
        _, rn = K.screen((0.0, yn, z), STAND)
        if rf >= H or rn < 0:
            continue
        rh = int(math.floor(rf + 0.5))          # first row of this board
        rg = rh - 1                               # the gap row above it
        if 0 <= rg < H and i > 0:
            m = deck[rg]
            f[rg, m, :3] = K.mix(f[rg, m, :3], PLANK_GAP, 0.72)
        if 0 <= rh < H and rn - rf >= 2.0:
            m = deck[rh]
            xs = np.where(m)[0]
            if len(xs):
                # weathered edge: highlight strength varies along the board, a few breaks
                n = K.hash01(xs * 0.173 + i * 3.1)
                k = np.where(n < 0.12, 0.0, 0.38 + 0.25 * K.hash01(np.floor(xs / 5.0) + i * 7.7))
                f[rh, xs, :3] = K.mix(f[rh, xs, :3], PLANK_HI, k)
    # ---- rowboat: warm 1 px rim along both gunwales (far side first)
    lines = sorted(GUNWALES, key=lambda l: -sum(p[1] for p in l))
    for li, line in enumerate(lines):
        sp = [K.screen(p, STAND) for p in line]
        for (x, y) in K.polyline_pixels(sp):
            if 0 <= y < H and 0 <= x < W:
                base = f[y, x, :3] if f[y, x, 3] > 0.5 else K.hexf("#4c4640")
                f[y, x, :3] = K.mix(base, GUNWALE, 0.75 if li == 0 else 0.55)
                f[y, x, 3] = 1.0
    rim_ok = np.isin(ids, [MAT_ID[k] for k in ("hull", "endpost", "post", "rope", "bucket", "tackle", "oar", "handle")])
    return f, rim_ok


def main():
    C.reset_scene()
    K.clear_mats()
    K.RIG[0] = K.sun_rig()
    rnd = random.Random(20260928)
    build_far(rnd)
    build_front(random.Random(7))
    reeds = reed_strokes(random.Random(11))
    sc = bpy.context.scene
    sc.render.film_transparent = True
    bpy.context.view_layer.update()
    far, far_m = {}, {}
    for name in ["mtn", "hills", "ridge", "bay", "head", "shore"]:
        far[name] = render_layer(name, False)
        far_m[name] = render_layer(name, True)
    front = render_layer("front", False)
    front_m = render_layer("front", True)
    ids = render_ids("front")
    front_m = draw_reeds(front_m, reeds, mirror=True, dim=0.8)
    back, water, sky, sky_refl = compose_back(far, far_m, front_m)
    front2, rim_ok = compose_front(front, ids, back, sky_refl)
    reeds_img = draw_reeds(np.zeros((H, W, 4), np.float32), reeds)
    np.savez_compressed(os.path.join(K.RAW, "lake_state.npz"), back=back, front=front2, rim_ok=rim_ok,
                        reeds=reeds_img, water=water, sky=sky)
    finish(back, front2, rim_ok, reeds_img, water, sky)


def finish(back, front, rim_ok, reeds_img, water, sky):
    # ---- back: grade -> palette (from the visible crop) -> Bayer dither only in the sky glow,
    #      horizontal 1 px lines at band edges everywhere else (water, mist)
    b = K.grade(back[..., :3], sat=0.92, split=1.0, contrast=0.06)
    x0, y0, x1, y1 = K.CROP
    crop_mask = np.zeros(b.shape[:2], bool)
    crop_mask[y0:y1, x0:x1] = True
    pal = K.kmeans_palette(b, 56, mask=crop_mask, keep=("#fff3d6", "#ffcf86"), weight_pow=0.5)
    bayer = np.tile(K.BAYER4, (H // 4 + 1, W // 4 + 1))[:H, :W]
    thr = np.where(sky, bayer, K.row_threshold(H, W))
    bq = K.quantize(b, pal, dither=0.5, thr=thr)
    K.save(bq, os.path.join(K.OUT, "lake_back.png"))
    # ---- front: hard alpha -> grade -> palette -> outer rim on posts / boat / props -> pixel reeds on top
    f = K.harden(front)
    f = K.grade(f, sat=0.9, split=1.0, contrast=0.06)
    a = f[..., 3] > 0.5
    fpal = K.kmeans_palette(f, 32, mask=a, weight_pow=0.6)
    fq = K.quantize(f, fpal)
    fq[..., 3] = f[..., 3]
    thick = a & K.shift(a, 0, 1, False)
    fq, _ = K.rim_outer(fq, K.hexf("#f2c28a"), strength=0.45, mask=rim_ok & thick)
    fq = K.over(fq, reeds_img)
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
        s = np.load(os.path.join(K.RAW, "lake_state.npz"))
        finish(s["back"], s["front"], s["rim_ok"], s["reeds"], s["water"], s["sky"])
    else:
        main()
