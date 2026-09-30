"""
FishingKing - stage dioramas seen from behind the angler (perspective).

Each stage is a 3D scene in metres (X right, Y forward, Z up, water at Z = 0) rendered with the
shared camera from fk_persp.py into:
  Stages/<id>_back.png   sky, far scenery, ground and the water surface (fish shadows go on top)
  Stages/<id>_front.png  everything standing on/above the water near the play area + foreground
  Data/stage_<id>.json   gameplay layout (fishable zone, depth profile, colours, mode ...)

Run:  blender -b --python Tools/Blender/fk_stages.py [-- lake stream ...]
"""
import sys
import os
import math
import random
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402
import fk_scene as S  # noqa: E402  (material + primitive helpers)

OUT = os.path.join(C.SPRITES, "Stages")
mflat, mtoon, mglow, mgrad, mnoise, mwood = S.mflat, S.mtoon, S.mglow, S.mgrad, S.mnoise, S.mwood
front, tag, tube, sphere, rock, cone, boxo = S.front, S.tag, S.tube, S.sphere, S.rock, S.cone, S.boxo
STAND = [1.0]


# ----------------------------------------------------------------------------- helpers
def mgrad_y(cols, y0, y1, steps=8, alpha=None):
    """Banded gradient along world Y (distance)."""
    key = ("gy", tuple(cols), y0, y1, steps, alpha)
    if key in S._mats:
        return S._mats[key]
    m = bpy.data.materials.new("GradY")
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    s = nb.sep(geo.outputs["Position"])
    f = nb.math("DIVIDE", nb.math("SUBTRACT", s[1], y0), y1 - y0, clamp=True)
    lc = [C.lin(c) for c in cols]
    stops = []
    for i in range(steps):
        t = i / (steps - 1)
        seg = min(int(t * (len(lc) - 1)), len(lc) - 2)
        lt = t * (len(lc) - 1) - seg
        stops.append((i / steps, S.lerp_col(lc[seg], lc[seg + 1], lt)))
    col = nb.ramp(f, stops)
    if alpha is not None:
        m.surface_render_method = "BLENDED"
        nb.output_emission(col, 1.0, alpha=alpha)
    else:
        nb.output_emission(col, 1.0)
    S._mats[key] = m
    return m


def hpoly(pts, z, mat, th=0.02):
    """Horizontal polygon (points in XY) at height z."""
    import bmesh
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
    return tag(C.mesh_object("HPoly", bm, mat))


def hquad(x0, y0, x1, y1, z, mat):
    return hpoly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], z, mat)


def vpoly(pts, y, mat, th=0.05):
    return S.poly(pts, mat, y, th)


def ellipse_pts(cx, cy, rx, ry, n=16, rot=0.0):
    out = []
    for k in range(n):
        a = 2 * math.pi * k / n
        x, y = rx * math.cos(a), ry * math.sin(a)
        c, s = math.cos(rot), math.sin(rot)
        out.append((cx + x * c - y * s, cy + x * s + y * c))
    return out


def water_marks(rnd, n, y0, y1, xs, col, rmin=0.3, rmax=0.9, z=0.02):
    """Small flat foam / glint ellipses on the water (they shrink to 1px dashes in the distance)."""
    for _ in range(n):
        y = rnd.uniform(y0, y1)
        x = rnd.uniform(-xs, xs) * (0.25 + y / y1)
        r = rnd.uniform(rmin, rmax) * (1 + y * 0.015)
        hpoly(ellipse_pts(x, y, r, r * 0.3, 8), z, mflat(col))


def glitter(rnd, x_sun, n, y0, y1, col):
    """Sun path: dashes along the line towards the sun."""
    for _ in range(n):
        y = y0 + (y1 - y0) * rnd.random() ** 1.6
        x = x_sun * (y / 3500.0) + rnd.uniform(-1, 1) * (0.5 + y * 0.03)
        r = rnd.uniform(0.3, 0.8) * (1 + y * 0.02)
        hpoly(ellipse_pts(x, y, r, r * 0.22, 6), 0.03, mflat(col))


def reflection(pts_xz, y, color):
    """Fake mirror image of a vertical silhouette (points (x, z) at distance y) on the water."""
    mp = [P.reflect_point(x, y, z, STAND[0]) for x, z in pts_xz]
    return hpoly(mp, 0.03, mflat(color))


def ridge(rnd, x0, x1, step, lo, hi, jag=0.4, base=0.0):
    pts = []
    x = x0
    while x <= x1:
        pts.append((x, base + rnd.uniform(lo, hi)))
        x += step * rnd.uniform(1 - jag, 1 + jag)
    pts.append((x1, base + rnd.uniform(lo, hi)))
    return pts


def sky(top, horizon, y=4000.0):
    cz = STAND[0] + P.CAM_UP
    S.quad(-9000, -600, 9000, 4000, mgrad([horizon, top], cz, cz + y * 0.42, 12), y)


def sun(x, z, r, col, halo, y=3500.0):
    sphere(x, y, z, r * 1.7, mflat(halo))
    sphere(x, y - 50, z, r, mflat(col))


def clouds(rnd, n, y=3000.0, zlo=250, zhi=600, xs=1400):
    m = C.toon_material("Cloud", "#ffffff", stops=[(0.0, C.lin("#c8d8ee")), (0.5, C.lin("#ffffff"))])
    for _ in range(n):
        x = rnd.uniform(-xs, xs)
        z = rnd.uniform(zlo, zhi)
        s = rnd.uniform(40, 90)
        for k in range(4):
            sphere(x + (k - 1.5) * s * 0.9, y + rnd.uniform(-20, 20), z + (s * 0.3 if k in (1, 2) else 0),
                   s * rnd.uniform(0.6, 0.9), m, 1.4, 0.5, 0.75)


def mountains(rnd, y, lo, hi, col, snow=None, step=160.0, xs=5000):
    pts = ridge(rnd, -xs, xs, step, lo, hi)
    vpoly(pts + [(xs, -400), (-xs, -400)], y, mflat(col))
    if snow:
        sl = lo + (hi - lo) * 0.6
        bot = [(x, min(z, sl + rnd.uniform(-20, 8))) for (x, z) in pts]
        vpoly(pts + list(reversed(bot)), y - 5, mflat(snow))
    return pts


def water(cols, y0=0.0, y1=120.0, steps=10, far=3000.0):
    hquad(-6000, -40, 6000, far, 0.0, mgrad_y(cols, y0, y1, steps))


def tree_line(rnd, y0, y1, x0, x1, z0, hmin, hmax, pine_col="#2f6a3e", round_col=("#3f7f3a", "#357032"),
              density=0.25, pine_ratio=0.5, snow=None, toon=True):
    n = int((x1 - x0) * density)
    for _ in range(n):
        x = rnd.uniform(x0, x1)
        y = rnd.uniform(y0, y1)
        h = rnd.uniform(hmin, hmax)
        if rnd.random() < pine_ratio:
            S.pine(x, y, z0, h, pine_col, snow=snow, toon=toon)
        else:
            S.round_tree(x, y, z0, h, round_col[0], round_col[1], toon=toon)


def reeds(rnd, cx, cy, n, spread=1.0, hmin=1.2, hmax=2.2, col="#5a9a3a", head="#6a4424"):
    for _ in range(n):
        x = cx + rnd.uniform(-spread, spread)
        y = cy + rnd.uniform(-spread, spread) * 0.8
        h = rnd.uniform(hmin, hmax)
        lean = rnd.uniform(-0.25, 0.25)
        tube([(x, y, -0.2), (x + lean, y, h)], 0.035, mtoon(col), 5)
        if rnd.random() < 0.6:
            tube([(x + lean * 0.7, y - 0.02, h * 0.66), (x + lean * 0.85, y - 0.02, h * 0.85)], 0.075, mtoon(head), 8)


def lily(x, y, r, flower=None):
    pts = ellipse_pts(x, y, r, r, 14)
    # notch
    pts[0] = (x + r * 0.2, y)
    hpoly(pts, 0.03, mtoon("#4a9a3a"))
    hpoly(ellipse_pts(x, y, r * 0.75, r * 0.75, 12), 0.035, mflat("#5aae44"))
    if flower:
        for k in range(5):
            a = 2 * math.pi * k / 5
            sphere(x + math.cos(a) * 0.12 * r, y + math.sin(a) * 0.12 * r, 0.12, 0.12 * r / 0.6, mtoon(flower), 1, 1, 0.7)
        sphere(x, y, 0.16, 0.07 * r / 0.6, mtoon("#ffe060"))


def plank_deck(x0, x1, y0, y1, z, col_a="#9a6a3a", col_b="#845a30", plank=0.28, gap=0.04, thick=0.18, rnd=None):
    n = int((x1 - x0) / plank)
    for i in range(n):
        xa = x0 + i * plank
        c = col_a if i % 2 == 0 else col_b
        dz = rnd.uniform(-0.02, 0.02) if rnd else 0
        boxo(xa + plank / 2, (y0 + y1) / 2, z - thick / 2 + dz, plank - gap, y1 - y0, thick, mwood(c, "#6a4424"))


def posts(xs, ys, top, r=0.14, col="#6a4424"):
    for x in xs:
        for y in ys:
            tube([(x, y, -1.5), (x, y, top)], r, mwood(col, "#5a3a1e"), 8)


# ============================================================================ STAGES
def stage_lake(rnd):
    STAND[0] = 1.0
    sky("#5aa6e6", "#cdeaf7")
    sun(900, 700, 90, "#fff6c8", "#fff0b0")
    clouds(rnd, 7)
    mountains(rnd, 2600, 120, 330, "#9cbad2", step=220)
    mountains(rnd, 1800, 40, 150, "#86a8bc", step=150)
    # far shore bank + forest
    bank = [(-1500, 0)] + [(x, 3.0 + 1.0 * math.sin(x * 0.05) + rnd.uniform(-0.3, 0.3)) for x in range(-1500, 1501, 25)] + [(1500, 0)]
    vpoly(bank, 180, mflat("#4f8a3e"))
    tree_line(rnd, 182, 200, -460, 460, 2.5, 6, 10, density=0.5, toon=True)
    tree_line(rnd, 215, 290, -900, 900, 3.0, 10, 16, pine_col="#3a6e4a", round_col=("#4e8a52", "#437a48"), density=0.22, toon=False)
    boxo(60, 186, 5.0, 8, 6, 5.0, mtoon("#b86a3a"))
    cone(60, 186, 7.5, 6.2, 3.6, mtoon("#8a2e2a"), 4, rot=(0, 0, math.radians(45)))
    # soft reflection of the far forest
    sil = [(-220, 0.0)]
    x = -220.0
    while x < 220:
        sil.append((x, 5.0 + 3.0 * abs(math.sin(x * 0.11)) + rnd.uniform(0, 1.5)))
        x += 6.0
    sil.append((220, 0.0))
    reflection(sil, 180, "#357f86")
    water(["#2c7aa4", "#3b90b8", "#58a8c8", "#8ccbe0"], 0, 140, 10)
    with front():
        rnd2 = random.Random(3)
        plank_deck(-1.3, 1.3, -18, 1.1, STAND[0], rnd=rnd2)
        posts([-1.35, 1.35], [-12, -8, -4, 0.8], STAND[0] + 0.15)
        boxo(-1.35, 0.8, STAND[0] + 0.45, 0.18, 0.18, 0.9, mwood("#6a4424", "#5a3a1e"))
        boxo(1.35, 0.8, STAND[0] + 0.45, 0.18, 0.18, 0.9, mwood("#6a4424", "#5a3a1e"))
        tube([(-0.9, -1.6, STAND[0]), (-0.9, -1.6, STAND[0] + 0.5)], [0.25, 0.3], mtoon("#4a7ac0", 0.5), 12)  # bucket
        reeds(rnd, -5.8, 2.5, 12, 0.9, 1.0, 1.8)
        reeds(rnd, 6.8, 4.5, 10, 0.9, 1.0, 1.8)
        reeds(rnd, -13, 18, 12, 1.4, 1.6, 2.6)
        reeds(rnd, 15, 30, 10, 1.6, 1.8, 2.8)
        for (x, y, r, fl) in ((-3.2, 8, 0.55, "#f29ab8"), (4.2, 13, 0.6, None), (-7.5, 21, 0.7, "#ffffff"),
                              (9.0, 27, 0.7, "#f29ab8"), (1.5, 33, 0.8, None), (-13, 38, 0.9, None)):
            lily(x, y, r, fl)
        # moored rowboat
        hull = mtoon("#c85a3a")
        boxo(-9.5, 10, 0.25, 1.3, 3.4, 0.5, hull, 0.25)
        boxo(-9.5, 10, 0.45, 1.0, 3.0, 0.2, mwood("#9a6a3a", "#845a30"))
    return dict(mode="shore", zNear=1.2, zFar=176, xLim=90, depth=[(0, 1.2), (8, 3), (20, 6), (40, 8), (140, 6), (176, 1)],
                waterTint="#2c7aa4", waterDeep="#0e2f44", ambient="day", clouds=True, birds=True)


def stage_stream(rnd):
    STAND[0] = 1.4
    sky("#62b0ee", "#dff2ff")
    clouds(rnd, 5)
    mountains(rnd, 2600, 250, 520, "#7e9cbc", snow="#eef4fa", step=200)
    mountains(rnd, 1400, 80, 220, "#5e8a8a", step=120)
    tree_line(rnd, 400, 700, -900, 900, 30, 30, 50, pine_col="#2e5a48", density=0.08, pine_ratio=1.0, toon=False)
    # waterfall cliff at the end of the stream
    cliff = [(-200, 0), (-60, 20), (-30, 34), (-10, 30), (-4, 30), (4, 30), (12, 36), (40, 26), (200, 0)]
    vpoly(cliff, 125, mnoise("#6e7478", "#5e6468", 0.08, 0.5))
    S.quad(-4, 0, 4, 30.5, mgrad(["#bfe8f8", "#ffffff"], 0, 30, 5), 124)
    for k in range(10):
        sphere(rnd.uniform(-5, 5), 122, 0.5, rnd.uniform(1.2, 2.2), mtoon("#ffffff"), 1.2, 0.6, 0.7)
    tree_line(rnd, 126, 140, -120, 120, 25, 10, 16, pine_col="#2e6a4a", density=0.2, pine_ratio=1.0)
    # banks (ground) on both sides
    for sgn in (-1, 1):
        edge = [(sgn * (8 + 0.6 * math.sin(y * 0.3)), y) for y in range(-30, 131, 5)]
        far = [(sgn * 400, 131), (sgn * 400, -30)]
        pts = edge + far if sgn > 0 else edge + far
        hpoly(pts if sgn < 0 else list(reversed(pts)), 0.5, mnoise("#6a8a52", "#5e7c48", 0.15, 0.5))
        hpoly([(sgn * 8.3, y) for y in (-30, 131)] + [(sgn * 6.8, 131), (sgn * 6.8, -30)], 0.02, mflat("#72c8c0"))
    water(["#2e9aa0", "#3aaab0", "#62c2c2", "#9adada"], 0, 110, 9)
    water_marks(rnd, 70, 3, 115, 6, "#8ad8d4", 0.2, 0.5)
    with front():
        # the angler's boulder
        rock(0, -1.2, STAND[0] - 1.9, 2.6, mtoon("#8a8c86"), rnd, 1.1, 0.75, 0.12)
        rock(-3.2, -2.0, 0.0, 1.6, mtoon("#7a7c76"), rnd, 1.2, 0.7)
        rock(3.4, -1.0, -0.2, 1.4, mtoon("#80827c"), rnd, 1.2, 0.7)
        for (x, y, r) in ((-4, 14, 0.9), (4.5, 22, 1.2), (-2.5, 34, 1.0), (3, 48, 1.3), (-5, 60, 1.5), (1, 75, 1.2)):
            rock(x, y, -0.2, r, mtoon("#8a8e8a"), rnd, 1.3, 0.6)
            hpoly(ellipse_pts(x, y - r * 0.2, r * 1.5, r * 0.9, 12), 0.02, mflat("#bff0ea"))
        # bank props
        for sgn in (-1, 1):
            for k in range(26):
                y = rnd.uniform(-4, 110)
                x = sgn * rnd.uniform(8.8, 30)
                if rnd.random() < 0.55:
                    S.pine(x, y, 0.5, rnd.uniform(6, 12), "#2e6a4a")
                else:
                    rock(x, y, 0.6, rnd.uniform(0.6, 1.6), mtoon("#8a8c86"), rnd)
            for k in range(10):
                y = rnd.uniform(0, 100)
                rock(sgn * rnd.uniform(7.6, 8.6), y, 0.2, rnd.uniform(0.4, 0.9), mtoon("#7e8078"), rnd, 1.3, 0.7)
    return dict(mode="shore", zNear=1.8, zFar=118, xLim=7.5, depth=[(0, 1.0), (10, 2.5), (30, 4.2), (60, 4.8), (118, 3)],
                waterTint="#2e9aa0", waterDeep="#0f4450", ambient="day", clouds=True, birds=True)


def stage_sea(rnd):
    STAND[0] = 3.0
    sky("#58a0e0", "#d6eef9")
    sun(-600, 520, 80, "#fff8d8", "#fff2c0")
    clouds(rnd, 8)
    # islands on the horizon
    for (x0, x1, h) in ((200, 900, 90), (1100, 1500, 50), (-1500, -1000, 60)):
        pts = [(x0, 0)] + [(x0 + (x1 - x0) * k / 8, h * math.sin(math.pi * k / 8) * rnd.uniform(0.7, 1.0)) for k in range(1, 8)] + [(x1, 0)]
        vpoly(pts, 3000, mflat("#8eaac2"))
    # lighthouse jetty (left)
    S.quad(-200, 0, -45, 3, mflat("#c8c4bc"), 190)
    tube([(-50, 190, 3), (-50, 190, 22)], [2.4, 1.6], C.pattern_material("LH", "#f2f2ee", "#d83a3a", kind="stripes",
                                                                       scale=0.18, thresh=0.5, axis=2), 12)
    sphere(-50, 189, 23.5, 1.6, mglow("#fff2a0"))
    cone(-50, 190, 24.5, 2.2, 3.0, mtoon("#d83a3a"))
    water(["#1d74ac", "#2a88c0", "#4aa0d0", "#8ac4e6"], 0, 200, 10, far=3000)
    hquad(-6000, 2400, 6000, 3000, 0.05, mflat("#a8d8f0"))  # horizon glare
    water_marks(rnd, 260, 6, 700, 60, "#dff2ff", 0.25, 0.7)
    glitter(rnd, -600, 120, 30, 2500, "#fff8d8")
    boxo(-90, 420, 1.2, 9, 3, 2.4, mtoon("#f2f2ee"))
    boxo(-92, 420, 3.2, 4, 2.5, 2.0, mtoon("#3a6ab0"))
    tube([(-89, 420, 3.2), (-89, 420, 9)], 0.2, mtoon("#b8c0c8"), 6)
    with front():
        top = STAND[0]
        conc = mnoise("#b8b4ac", "#a6a29a", 0.4, 0.55)
        boxo(0, -9.0, top / 2 - 1, 5.0, 20.0, top + 2, conc)
        boxo(0, -9.0, top + 0.12, 5.2, 20.0, 0.25, mflat("#cac6be"))
        boxo(-1.6, 0.2, top + 0.35, 0.5, 0.5, 0.6, mtoon("#d8d8d0"))  # bollard
        tet = mtoon("#a8a49a")
        for (x, y, z, r, a) in ((-3.4, 2.2, 0.4, 1.2, 10), (3.6, 2.0, 0.3, 1.2, 40), (-1.2, 3.4, -0.2, 1.1, 70),
                                (1.6, 3.8, -0.3, 1.1, 20), (-5.2, 4.0, -0.4, 1.0, 45), (5.4, 4.4, -0.5, 1.0, 5),
                                (-4.2, 0.2, 1.4, 1.2, 30), (4.4, -0.2, 1.3, 1.2, 60)):
            dirs = [(0, 0, 1), (0.94, 0, -0.33), (-0.47, 0.82, -0.33), (-0.47, -0.82, -0.33)]
            ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
            for dx, dy, dz in dirs:
                rx, ry = dx * ca - dy * sa, dx * sa + dy * ca
                tag(C.tube_along("Tet", [(x, y, z), (x + rx * r, y + ry * r, z + dz * r)], [0.45, 0.28], tet, 8))
            sphere(x, y, z, 0.45, tet)
        # buoy
        tube([(7, 26, -0.3), (7, 26, 1.0)], [0.55, 0.3], mtoon("#e84a3a", 0.5), 12)
        sphere(7, 26, 1.1, 0.2, mglow("#fff2a0"))
        for k in range(6):
            sphere(rnd.uniform(-7, 7), rnd.uniform(1.5, 4), 0.02, rnd.uniform(0.3, 0.6), mflat("#f2faff"), 1.5, 0.8, 0.15)
    return dict(mode="shore", zNear=3.6, zFar=600, xLim=90, depth=[(0, 3), (10, 6), (30, 10), (60, 12)],
                waterTint="#1d74ac", waterDeep="#06223e", ambient="day", clouds=True, birds=True)


def stage_swamp(rnd):
    STAND[0] = 0.9
    sky("#7a9686", "#d2dcbe")
    sun(300, 260, 70, "#f2e8c0", "#e2dcb0")
    for (y, col, hmin, hmax) in ((900, "#a2b6a0", 40, 80), (500, "#8aa48a", 25, 55), (220, "#6e8a6e", 15, 30)):
        for k in range(int(40 * (1 + (900 - y) / 900))):
            x = rnd.uniform(-y * 1.1, y * 1.1)
            h = rnd.uniform(hmin, hmax)
            tube([(x, y, 0), (x, y, h)], h * 0.03, mflat(col), 6)
            sphere(x, y - 1, h, h * rnd.uniform(0.25, 0.4), mflat(col), 1.4, 0.5, 0.6)
        S.quad(-y * 1.3, -5, y * 1.3, hmin * 0.35, mflat(col), y + 5)
        S.quad(-y * 1.3, 0, y * 1.3, hmax * 0.6, mgrad(["#d8e0c8", "#d8e0c8"], 0, 1, 2, alpha=0.35), y - 10)
    # near far shore
    vpoly([(-400, 0)] + [(x, 1.2 + 0.5 * math.sin(x * 0.2)) for x in range(-400, 401, 10)] + [(400, 0)], 62, mflat("#4e5e3a"))
    sil = []
    for k in range(18):
        x = rnd.uniform(-60, 60)
        h = rnd.uniform(9, 16)
        tube([(x, 63, 0), (x + 0.5, 63, h)], [0.9, 0.5], mtoon("#5a4a36"), 8)
        sphere(x + 0.5, 62.5, h + 1, rnd.uniform(3.5, 5.5), mtoon("#3e5a32"), 1.4, 0.7, 0.8)
        for m in range(4):
            tube([(x - 2 + m * 1.3, 61.5, h), (x - 2 + m * 1.3, 61.5, h - rnd.uniform(3, 6))], 0.18, mtoon("#8a9a6a"), 4)
        sil += [(x - 4, h * 0.7), (x, h + 4), (x + 4, h * 0.7)]

    S.quad(-300, 0, 300, 6, mgrad(["#c8d4b8", "#c8d4b8"], 0, 1, 2, alpha=0.4), 55)  # mist over the water
    water(["#34502a", "#3e5c30", "#56724a", "#7e9270"], 0, 60, 9)
    with front():
        rnd2 = random.Random(9)
        plank_deck(-1.2, 1.2, -18, 0.9, STAND[0], "#7a6446", "#6a5638", rnd=rnd2)
        posts([-1.25, 1.25], [-10, -6, -2, 0.7], STAND[0] + 0.3, col="#5a4630")
        tube([(1.1, 0.6, STAND[0]), (1.1, 0.6, STAND[0] + 1.3)], 0.05, mtoon("#2a2a2a"), 6)
        sphere(1.1, 0.5, STAND[0] + 1.45, 0.18, mglow("#ffd878", 1.4))
        for (x, y, s) in ((-8, 9, 1.0), (10, 14, 1.2), (-15, 26, 1.4), (14, 33, 1.3), (-4, 44, 1.2), (22, 20, 1.5)):
            h = 8 * s
            tube([(x, y, 1.5 * s), (x + 0.3, y, h)], [0.5 * s, 0.35 * s], mtoon("#5a4630"), 8)
            for k in range(6):
                a = 2 * math.pi * k / 6 + rnd.uniform(-0.3, 0.3)
                tube([(x, y, 1.8 * s), (x + math.cos(a) * 1.4 * s, y + math.sin(a) * 1.4 * s, 0.9 * s),
                      (x + math.cos(a) * 2.2 * s, y + math.sin(a) * 2.2 * s, -0.3)], [0.18 * s, 0.13 * s, 0.1 * s], mtoon("#5a4630"), 6)
            sphere(x + 0.3, y, h + 1.0, 3.0 * s, mtoon("#3e5a2e"), 1.3, 1.0, 0.7)
            for m in range(5):
                xx = x - 2 * s + m * s
                tube([(xx, y - 1, h), (xx, y - 1, h - rnd.uniform(2, 4) * s)], 0.1 * s, mtoon("#8a9a6a"), 4)
        for (x, y) in ((-2.8, 6), (3.5, 10), (-6, 17), (1.5, 24), (6, 30), (-9, 36)):
            lily(x, y, rnd.uniform(0.5, 0.8), rnd.choice([None, "#f2e8f0"]))
        reeds(rnd, -4.5, 2.0, 18, 1.2, col="#5a7a32", head="#5a3e22")
        reeds(rnd, 5.0, 3.5, 14, 1.2, col="#5a7a32", head="#5a3e22")
        tube([(4, 18, -0.4), (7, 20, 0.6)], 0.35, mtoon("#4a3a26"), 8)  # log
    return dict(mode="shore", zNear=1.0, zFar=58, xLim=40, depth=[(0, 1.0), (8, 2.5), (20, 4.5), (40, 5.5), (58, 2)],
                waterTint="#34502a", waterDeep="#101a08", ambient="mist", clouds=False, fireflies=True)


def stage_ice(rnd):
    STAND[0] = 0.25
    sky("#28366e", "#f2b6a0")
    sun(-400, 60, 70, "#ffd8b0", "#f8c8a8")
    for k in range(70):
        sphere(rnd.uniform(-3000, 3000), 3900, rnd.uniform(500, 1600), rnd.uniform(4, 8), mglow("#ffffff", 1.0))
    for k in range(3):
        pts = [(-4000 + i * 400, 1100 + k * 120 + 120 * math.sin(i * 0.6 + k)) for i in range(21)]
        band = pts + [(x, z - 180) for (x, z) in reversed(pts)]
        vpoly(band, 3800 - k * 10, mgrad(["#5affc0", "#8a6aff"], 900, 1500, 4, alpha=0.28))
    mountains(rnd, 2600, 200, 460, "#8a94c0", snow="#f2eef6", step=190)
    mountains(rnd, 1500, 60, 180, "#a8b2d8", snow="#f4f2fa", step=140)
    vpoly([(-600, 0)] + [(x, 3 + 1.2 * math.sin(x * 0.05)) for x in range(-600, 601, 20)] + [(600, 0)], 90, mflat("#e2eaf6"))
    tree_line(rnd, 92, 120, -300, 300, 2.5, 10, 18, pine_col="#3a5a6a", density=0.45, pine_ratio=1.0, snow="#f4f8ff")
    # ice sheet (opaque, fish shadows show faintly through it in Unity)
    hquad(-6000, -40, 6000, 3000, 0.2, mgrad_y(["#9cc4e2", "#b8d8ee", "#d4e8f6", "#e6f0fa"], 0, 90, 8))
    for k in range(60):
        x, y = rnd.uniform(-40, 40), rnd.uniform(0, 85)
        hpoly(ellipse_pts(x, y, rnd.uniform(1.0, 3.0), rnd.uniform(0.3, 0.9), 10, rnd.uniform(-0.3, 0.3)), 0.21,
              mflat(rnd.choice(["#d8e8f6", "#cfe2f2", "#e2eef8"])))
    for k in range(25):  # cracks
        x, y = rnd.uniform(-30, 30), rnd.uniform(2, 70)
        a = rnd.uniform(0, math.pi)
        L = rnd.uniform(2, 6)
        hpoly([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L), (x + math.cos(a) * L + 0.08, y + math.sin(a) * L + 0.05)],
              0.215, mflat("#7aa4c8"))
    # the fishing hole
    HX, HY = 1.9, 3.0
    hpoly(ellipse_pts(HX, HY, 1.0, 0.85, 20), 0.22, mflat("#e8f2fc"))
    hpoly(ellipse_pts(HX, HY, 0.72, 0.6, 20), 0.225, mflat("#0c2a4a"))
    hpoly(ellipse_pts(HX, HY + 0.15, 0.55, 0.42, 16), 0.23, mflat("#16406a"))
    with front():
        for (x, y) in ((-14, 26), (18, 40), (-30, 55), (40, 70)):
            cone(x, y, 0.2, 1.8, 2.8, mtoon("#d86a4a"), 8)
            S.quad(x - 0.35, 0.2, x + 0.35, 1.2, mflat("#3a2a2a"), y - 1.9)
        tube([(3.6, 2.4, 0.2), (4.0, 2.4, 1.9)], 0.06, mtoon("#b8c0c8", 0.8), 6)
        tube([(3.8, 2.35, 1.0), (4.2, 2.35, 1.05)], 0.06, mtoon("#d83a3a"), 6)
        tube([(-1.4, 0.8, 0.2), (-1.4, 0.8, 0.62)], [0.25, 0.3], mtoon("#e8c030", 0.5), 12)
        for k in range(8):
            x = rnd.uniform(-12, 12)
            y = rnd.uniform(-3, 6)
            if abs(x) > 2.5 and abs(x - HX) > 2.0:
                sphere(x, y, 0.2, rnd.uniform(0.4, 0.8), mtoon("#f4f8ff"), 1.6, 1.2, 0.3)
    return dict(mode="ice", zNear=0.8, zFar=85, xLim=35, depth=[(0, 4), (10, 6), (30, 8), (85, 4)],
                waterTint="#1c4a78", waterDeep="#06122a", ambient="snow", clouds=False, snow=True,
                holeX=1.9, holeZ=3.0, holeR=0.62, iceY=0.2)


def stage_ocean(rnd):
    STAND[0] = 1.7
    sky("#3a86de", "#c3e4ff")
    sun(700, 900, 80, "#fffae0", "#fff4c8")
    clouds(rnd, 10, zlo=200, zhi=700)
    boxo(260, 2200, 6, 40, 10, 12, mflat("#7a8aa0"))
    boxo(250, 2200, 18, 16, 8, 12, mflat("#8a9ab0"))
    water(["#12508e", "#1a64a8", "#3a86c4", "#80bce4"], 0, 260, 11, far=3000)
    hquad(-6000, 2400, 6000, 3000, 0.05, mflat("#a8d4f4"))
    water_marks(rnd, 320, 4, 900, 80, "#d8ecff", 0.3, 0.8)
    glitter(rnd, 700, 160, 20, 2600, "#fffae0")
    vpoly([(-1400, 0), (-1250, 40), (-1150, 70), (-1000, 55), (-900, 0)], 2900, mflat("#7e9cc0"))
    with front():
        rnd2 = random.Random(5)
        plank_deck(-2.4, 2.4, -20, 0.2, STAND[0], "#b88a5a", "#a87a4a", rnd=rnd2)
        hullm = mflat("#f2f2ee")
        for sgn in (-1, 1):
            boxo(sgn * 2.55, -9.5, STAND[0] - 0.6, 0.35, 21, 2.4, hullm)
            boxo(sgn * 2.55, -9.5, STAND[0] + 0.66, 0.42, 21, 0.14, mflat("#2a5ab0"))
        boxo(0, 0.45, STAND[0] - 0.6, 5.4, 0.45, 2.4, hullm)
        boxo(0, 0.45, STAND[0] + 0.66, 5.5, 0.52, 0.14, mflat("#2a5ab0"))
        boxo(0, 0.5, -0.4, 5.3, 0.3, 0.6, mflat("#b83a2a"))
        for x in (-2.1, -0.7, 0.7, 2.1):
            tube([(x, 0.45, STAND[0] + 0.7), (x, 0.45, STAND[0] + 1.5)], 0.05, mtoon("#b8c0c8", 0.8), 6)
        tube([(-2.2, 0.45, STAND[0] + 1.5), (2.2, 0.45, STAND[0] + 1.5)], 0.06, mtoon("#b8c0c8", 0.8), 6)
        boxo(-1.5, -2.2, STAND[0] + 0.35, 1.1, 0.7, 0.7, mtoon("#e8e8f0"), 0.06)  # cooler
        boxo(-1.5, -2.2, STAND[0] + 0.72, 1.15, 0.75, 0.1, mtoon("#3a8ad8"), 0.03)
        C.add_prim("torus", "Ring", mtoon("#f25a3a"), major_radius=0.45, minor_radius=0.13, major_segments=20,
                   minor_segments=8, location=(2.35, -4.0, STAND[0] + 0.3), rotation=(0, math.radians(90), 0))
        tag(bpy.context.active_object)
        for k in range(8):
            sphere(rnd.uniform(-4, 4), rnd.uniform(1.0, 3.0), 0.02, rnd.uniform(0.3, 0.7), mflat("#e8f4ff"), 1.6, 0.8, 0.15)
    return dict(mode="boat", zNear=1.6, zFar=900, xLim=120, depth=[(0, 18), (100, 20)],
                waterTint="#12508e", waterDeep="#020c26", ambient="day", clouds=True, birds=True)


def stage_cave(rnd):
    STAND[0] = 1.2
    S.quad(-9000, -600, 9000, 4000, mgrad(["#100c1a", "#1e1830", "#150f22"], -20, 120, 8), 400)
    # far wall with crystals
    wall = ridge(rnd, -300, 300, 12, 20, 60)
    vpoly(wall + [(300, -20), (-300, -20)], 110, mnoise("#2a2238", "#231c30", 0.06, 0.5))
    for k in range(40):
        x, z = rnd.uniform(-120, 120), rnd.uniform(0, 30)
        col = rnd.choice(["#5ae8ff", "#b86aff", "#6affc0", "#ff8ad8"])
        cone(x, 108, z, rnd.uniform(0.6, 1.6), rnd.uniform(2, 6), mglow(col, 1.2), 6, rot=(0, math.radians(rnd.uniform(-30, 30)), 0))
    refl = []
    for k in range(24):
        x = rnd.uniform(-60, 60)
        col = rnd.choice(["#2a6a8a", "#4a3a7a", "#2a7a6a"])
        hpoly([(x - 0.4, 108), (x + 0.4, 108), (x + 0.3, 60), (x - 0.3, 60)], 0.03, mflat(col))
    water(["#0a2a40", "#0e3a52", "#1a4e66", "#24586e"], 0, 100, 8)
    with front():
        ledge = mnoise("#3a3248", "#2e283c", 0.5, 0.5)
        rock(0, -2.0, STAND[0] - 1.8, 3.2, mtoon("#3e3450"), rnd, 1.1, 0.6, 0.1)
        rock(-3.5, -1.0, 0.0, 1.8, mtoon("#342c46"), rnd, 1.2, 0.7)
        rock(3.8, -0.5, 0.0, 1.7, mtoon("#3a3250"), rnd, 1.2, 0.7)
        for sgn in (-1, 1):
            for k in range(14):
                y = k * 7 + rnd.uniform(-2, 2)
                x = sgn * (16 + y * 0.12 + rnd.uniform(0, 3))
                rock(x, y, rnd.uniform(-1, 4), rnd.uniform(3, 6), mtoon(rnd.choice(["#2e2640", "#342a48", "#282036"])), rnd, 1.0, 1.6)
            for k in range(6):
                y = rnd.uniform(5, 80)
                x = sgn * (14 + y * 0.12)
                for m in range(3):
                    cone(x + rnd.uniform(-1, 1), y, rnd.uniform(0, 3), rnd.uniform(0.3, 0.7), rnd.uniform(1.5, 3.5),
                         mglow(rnd.choice(["#5ae8ff", "#b86aff"]), 1.25), 6, rot=(0, math.radians(rnd.uniform(-25, 25)), 0))
        for k in range(26):
            y = rnd.uniform(12, 70)
            x = rnd.uniform(-30, 30)
            h = rnd.uniform(4, 12)
            cone(x, y, 26 - h, rnd.uniform(0.8, 1.8), h, mtoon("#2e2640"), 6, rot=(math.radians(180), 0, 0))
        S.quad(-400, 24, 400, 200, mflat("#1a1426"), 12)  # ceiling band
        for k in range(8):
            x = rnd.uniform(-3, 3)
            y = rnd.uniform(-2.5, 0.5)
            if abs(x) > 0.8:
                sphere(x, y, STAND[0] + 0.15, 0.2, mglow(rnd.choice(["#5ae8ff", "#6affc0"]), 1.3), 1.4, 1, 0.6)
                tube([(x, y, STAND[0] - 0.1), (x, y, STAND[0] + 0.1)], 0.06, mtoon("#d8d0e8"), 5)
        tube([(-1.6, -1.0, STAND[0]), (-1.6, -1.0, STAND[0] + 1.4)], 0.05, mtoon("#5a4a3a"), 6)
        sphere(-1.6, -1.1, STAND[0] + 1.5, 0.22, mglow("#ffc860", 1.4))
    return dict(mode="shore", zNear=1.6, zFar=100, xLim=14, depth=[(0, 2), (10, 5), (30, 8), (100, 9)],
                waterTint="#0e3a52", waterDeep="#02060e", ambient="cave", clouds=False, sparkles=True)


STAGES = {"lake": stage_lake, "stream": stage_stream, "sea": stage_sea, "swamp": stage_swamp,
          "ice": stage_ice, "ocean": stage_ocean, "cave": stage_cave}


def render_stage(sid):
    C.clear_objects(keep_camera=False)
    S._mats.clear()
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
    rnd = random.Random(sum(ord(ch) * (i + 1) for i, ch in enumerate(sid)))
    info = STAGES[sid](rnd)
    sc = bpy.context.scene
    P.setup_camera(STAND[0])
    for ob in sc.objects:
        if ob.type == "MESH" and "layer" not in ob:
            ob["layer"] = "back"
    bpy.context.view_layer.update()
    for ob in sc.objects:
        if ob.type == "MESH":
            ob.hide_render = ob["layer"] == "front"
    sc.render.film_transparent = False
    raw = os.path.join(C.TMP, "stage_raw.png")
    C.render_raw(raw)
    arr = C.load_pixels(raw)
    arr[..., 3] = 1.0
    C.save_pixels(arr, os.path.join(OUT, f"{sid}_back.png"))
    for ob in sc.objects:
        if ob.type == "MESH":
            ob.hide_render = ob["layer"] != "front"
    sc.render.film_transparent = True
    C.render_sprite(os.path.join(OUT, f"{sid}_front.png"), outline=True, outline_mul=0.4)
    depth = info.pop("depth")
    layout = dict(id=sid, standH=STAND[0], widthPx=P.W, heightPx=P.H, ppu=P.PPU,
                  camBack=P.CAM_BACK, camUp=P.CAM_UP, pitch=P.PITCH, focalPx=P.F_PX,
                  depthZ=[d[0] for d in depth], depthV=[d[1] for d in depth])
    layout.update(info)
    C.write_json(f"stage_{sid}.json", layout)
    print("STAGE", sid, "ok")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ids = argv or list(STAGES.keys())
    C.reset_scene()
    for sid in ids:
        if sid == "clouds":
            S.render_clouds()
            continue
        render_stage(sid)
    print("STAGES done")


if __name__ == "__main__":
    main()
