"""
hybrid - the cave stage: retro16 craft + the "cave" preset mood (underground lake, crystal glow, one
daylight shaft).

Back layer (opaque):
  * no sky: the cave shell fills the frame - a low vaulted ceiling fringed with stalactites (the top of the
    game crop), a stratified far wall at ~111 m, rock walls closing in along both sides, two hourglass
    columns and a few rock islets on the far waterline; every rock ramp is the same curated dark-violet ramp
    pushed into the near-black violet fog by its distance (4 distance bins, darker = farther);
  * glowing crystal clusters (cyan / violet preset colours) on the far wall and the side walls, each a local
    light: flat concentric dithered glow rings on the rock and water around it, plus its small exact
    reflection (shimmering, alternate rows);
  * water: dark blue-teal depth bands joined by horizontal dash dithering (black-violet far water under the
    wall reflections -> calm teal play area = waterTint), one band darker beyond |x| 11.5 m (light falloff
    towards the walls), EXACT mirrored reflections of the lowest 4.5 m of the walls / columns / islets /
    crystals (water-tinted, retro16 break lines, dissolving), drip rings in the far / side water, a thin
    cold mist on the far waterline;
  * ONE daylight shaft from a ceiling hole (3D: hole (-7, 64, 9.6) -> lit pool on the water at (-3, 47)):
    translucent 2-level shaft with dust (apply_shaft), a lit sheen pool + sparse glitter ONLY under it (far
    third of the water, no pillar over the play area);
  * the reflections / ripples / crystal glows of the front objects (computed by the front pass).
Front layer (transparent): the rock promontory the angler stands on (boulder chunks, flat top at standH),
side boulders stepping down into the water, two stalagmites framing the mid-distance sides, crystal clusters
(local lights: glow rings on the rock + halo / reflection on the water), glowing mushrooms, a crook post with
a hanging lantern (warm light pool on the ledge). Cyan top rim (crystal light) on the outer silhouettes,
hue-shifted outlines on the rock / lantern only (crystals and mushrooms glow, no outline).

Same camera (fk_persp.setup_camera(standH of Data/stage_cave.json)), 640x400, same gameplay layout.
Outputs: _tmp/variants/hybrid/cave_back.png, cave_front.png, stage_cave.json (scratch: work/cave/)
Run: blender -b --python variants/hybrid/hyb_cave.py [-- back|front] [--period dawn|day|evening|night [--dry]]   (hyb_period.py)
"""
import os
import sys
import json
import math
import random
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hyb_core as R  # noqa: E402
import hyb_period as PER  # noqa: E402
import bpy  # noqa: E402
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix, noise as mnoise  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

SID = "cave"
R.WORK = PER.work(SID, os.path.join(R.WORK, SID))   # scratch EXR passes of this stage only (parallel runs never collide)
os.makedirs(R.WORK, exist_ok=True)
with open(os.path.join(C.DATA, f"stage_{SID}.json"), encoding="utf-8") as _f:
    STAND = float(json.load(_f)["standH"])      # 1.2
W, H = P.W, P.H


def rc(p):
    return R.rc(p, STAND)


# ------------------------------------------------------------------ the light shaft, placed in 3D
# daylight falls through a ceiling hole and lands on the water in the far third, left of centre: the shaft
# and its glitter stay out of the play area (the preset's screen shaft reached row 330)
HOLE = (-7.0, 64.0, 9.6)
POOL = (-3.0, 47.0, 0.0)


def _span(p, wm):
    a = rc((p[0] - wm / 2, p[1], p[2]))
    b = rc((p[0] + wm / 2, p[1], p[2]))
    return (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, b[0] - a[0]


_HC, _HR, _HW = _span(HOLE, 3.0)
POOL_C, POOL_R, _PW = _span(POOL, 4.6)

# ------------------------------------------------------------------ water (preset override: the preset bands are
# far too dark for fish shadows; the play-area band IS waterTint, so Unity's tint and the render agree)
WB = ["#181b2d", "#162537", "#173142", "#1a404f", "#275463", "#244c5b"]    # far -> near (L .23 .26 .30 .35 .42 .40)
WDARK, WCAP = "#111524", "#366671"
PR = PER.use_preset(
    SID,
    water=dict(bands=WB, dark=WDARK, refl=["#10121e", "#1a2232"], tint=WB[4]),
    shaft=dict(top=(round(_HC, 1), round(_HR, 1), round(_HW, 1)), bottom=(round(POOL_C, 1), round(POOL_R, 1), round(_PW, 1)),
               amount=0.2, fade=0.35),
    glitter=dict(dx=round(POOL_C - W / 2, 1), row0=int(POOL_R) - 5, row1=int(POOL_R) + 20, far=1.0, dens=0.42,
                 width=(8.0, 18.0), maxlen=3))
G = R.grade_hex
# self-lit things (crystals, mushroom caps / stems) ignore a period's exposure gain / light tint (periods/cave.py):
# at night the rock darkens and the crystals keep glowing (native run: no gain / tint -> same colours as G)
_PR_SELF = PR.copy(grade=dict(PR.grade, gain=None, tint=None))


def GS(cols):
    return R.grade_hex(cols, _PR_SELF)


# the lantern's warm pool on the ledge: (x level, x radius), brighter / wider at night (periods/cave.py CONSTS)
LAMP_POOL = PER.const("LAMP_POOL", (1.0, 1.0))
LB = Vector(PR.key_dir).normalized()          # key: the shaft, high above

# water step maps (the cave bands get LIGHTER towards the viewer, so the lake's WSTEP list does not apply)
UP = {WB[0]: WB[1], WB[1]: WB[2], WB[2]: WB[3], WB[3]: WB[4], WB[4]: WCAP, WB[5]: WB[4], WDARK: WB[0], WCAP: WCAP}
DN = {WB[0]: WDARK, WB[1]: WB[0], WB[2]: WB[1], WB[3]: WB[2], WB[4]: WB[3], WB[5]: WB[3], WDARK: WDARK, WCAP: WB[4]}
REFL = PR.water["refl"]

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)               # only a fallback: the cave shell covers the frame
ROCK = G(["#241c32", "#342a46", "#4a3c5e", "#665678"])
BINS = [(-22.0, 32.0, 36.0), (32.0, 58.0, 60.0), (58.0, 84.0, 84.0), (84.0, 118.0, 118.0)]   # y0, y1, camera dist
RB = [R.hazed(ROCK, d, extra=-0.06 * k) for k, (_, _, d) in enumerate(BINS)]
CRYC = GS(["#2c70b8", "#6ad8ff", "#d0f6ff"])    # preset crystal cyan in the middle
CRYV = GS(["#6a44b8", "#c89cff", "#f0e2ff"])    # preset crystal violet in the middle
CRYC_B = R.hazed(CRYC, 70.0, extra=-0.5)
CRYV_B = R.hazed(CRYV, 70.0, extra=-0.5)
GLIT = PR.glitter["cols"]
BACK_PAL = SKY_PAL + WB + [WDARK, WCAP] + REFL + sum(RB, []) + CRYC_B + CRYV_B + GLIT

ROCKF = G(["#1c1626", "#2c2238", "#40344e", "#584a68", "#76688a"])
WOOD = G(["#2a1e24", "#46322e", "#6a4a38"])
METAL = G(["#34323e", "#6a6878"])
FLAME = ["#e8842e", "#ffc860", "#fff2c4"]
STEM = GS(["#6e6488", "#b0a6cc"])
FRONT_PAL = ROCKF + CRYC + CRYV + WOOD + METAL + FLAME + STEM

# back-layer overlay codes produced by the front pass (applied relative to the local water band)
OV_DARK, OV_WM1, OV_RIPPLE, OV_CRYC, OV_CRYV = 1, 2, 3, 5, 6
_OV = {}
FRONT_GLOWS = []          # crystal / lantern lights of the front layer (screen ellipses), shared with the back pass
BACK_GLOWS = []


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


def fbm(p, octaves=3):
    v, a, f = 0.0, 1.0, 1.0
    for o in range(octaves):
        v += a * mnoise.noise(Vector((p[0] * f + 11.3 * o, p[1] * f + 7.7 * o, p[2] * f + 3.1 * o)))
        a *= 0.5
        f *= 2.03
    return v


def orient(ob):
    """Flip an open sheet so its faces look at the camera (Lambert lights the visible side)."""
    cam = Vector(P.cam_pos(STAND))
    s = sum(p.normal.dot(cam - p.center) for p in ob.data.polygons)
    if s < 0:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(ob.data)
        bm.free()
    return ob


def sheet(name, us, vs, fn, mat, kind, grp=None):
    verts = [fn(u, v) for u in us for v in vs]
    nv = len(vs)
    faces = []
    for i in range(len(us) - 1):
        for j in range(nv - 1):
            a, b = i * nv + j, (i + 1) * nv + j
            faces.append((a, b, b + 1, a + 1))
    ob = R.mesh_from(name, verts, faces, mat, smooth=False)
    return tagk(orient(ob), kind, grp)


def bin_of(y):
    for k, (y0, y1, _) in enumerate(BINS):
        if y < y1:
            return k
    return len(BINS) - 1


# ================================================================== BACK LAYER geometry
def ceil_z(x, y):
    return 9.9 - 0.011 * (y - 30.0) + 0.4 * fbm((x * 0.07, y * 0.07, 3.3), 2)


def wall_x(side, y, z):
    """Surface x of the side wall (side -1 left, +1 right): alcoves along the lake, curving into the vault,
    layered strata (each stratum leans back = a lit ledge top, the step below it = a dark overhang line)."""
    zc = ceil_z(side * 20.0, y)
    v = min(1.0, max(0.0, (z + 1.5) / (zc + 2.5)))
    base = 15.5 + 0.13 * y + 2.2 * fbm((y * 0.045, side * 3.1, 0.5), 2)
    lean = 3.0 * v ** 2
    strata = 0.75 * ((z * 0.52 + 0.45 * fbm((y * 0.05, side * 2.0, 6.0), 1)) % 1.0)
    crack = 0.4 * fbm((y * 0.42, z * 0.12, side * 9.0), 2)
    return side * (base - lean + strata + crack)


def farwall_y(x, z):
    strata = 0.6 * ((z * 0.62 + 0.35 * fbm((x * 0.05, 0.0, 4.4), 1)) % 1.0)     # sawtooth ledges (lit tops)
    return 111.0 + 2.0 * fbm((x * 0.09, z * 0.14, 2.0), 3) + strata


def rock_mat(ramp, name, noise=0.12, bias=0.0):
    return R.m_tone(ramp, [0.36, 0.58, 0.8], light=LB, noise=noise, nscale=0.35, ncoord="world", ndetail=1.0,
                    bias=bias, name=name)


SHARP = [(-0.3, 1.05), (0.0, 1.0), (0.22, 0.74), (0.45, 0.5), (0.7, 0.27), (0.88, 0.12)]
BLUNT = [(-0.3, 1.1), (0.0, 1.0), (0.14, 0.9), (0.3, 0.8), (0.46, 0.74), (0.62, 0.64), (0.76, 0.52), (0.87, 0.38),
         (0.95, 0.2)]     # drippy rounded stalagmite (bulging rings, rounded crown)


def spike(name, x, y, z0, length, r0, mat, rnd, down=True, sides=7, rings=SHARP):
    """Stalactite (down) / stalagmite: a lumpy faceted cone with a slight bend, closed mesh."""
    sgn = -1.0 if down else 1.0
    bx, by = rnd.uniform(-0.1, 0.1) * length, rnd.uniform(-0.1, 0.1) * length
    rot = rnd.uniform(0, 6.28)
    verts, faces = [], []
    for (t, rr) in rings:
        cx, cy = x + bx * t * t, y + by * t * t
        zz = z0 + sgn * t * length
        if not down:
            zz = max(zz, -0.05)                 # nothing below the waterline in the front layer
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            w = r0 * rr * rnd.uniform(0.84, 1.12)
            verts.append((cx + w * math.cos(a), cy + w * math.sin(a), zz))
    tip = len(verts)
    verts.append((x + bx, y + by, z0 + sgn * length))
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            faces.append((i * sides + k, i * sides + k2, (i + 1) * sides + k2, (i + 1) * sides + k))
    last = (len(rings) - 1) * sides
    for k in range(sides):
        faces.append((last + k, last + (k + 1) % sides, tip))
    faces.append(tuple(range(sides))[::-1])
    ob = R.mesh_from(name, verts, faces, mat, smooth=False)
    R._fix_normals(ob)
    return ob


def prism(name, base, axis, length, r, mat, sides=6):
    """One crystal: hexagonal prism with a pointed tip along axis."""
    ax = Vector(axis).normalized()
    ref = Vector((1, 0, 0)) if abs(ax.x) < 0.9 else Vector((0, 1, 0))
    a = ax.cross(ref).normalized()
    b = ax.cross(a).normalized()
    base = Vector(base)
    verts, faces = [], []
    for t in (0.0, 0.68):
        c = base + ax * (length * t)
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            verts.append(c + a * (math.cos(ang) * r) + b * (math.sin(ang) * r))
    verts.append(base + ax * length)
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
        faces.append((sides + k, sides + k2, 2 * sides))
    faces.append(tuple(range(sides))[::-1])
    ob = R.mesh_from(name, [tuple(v) for v in verts], faces, mat, smooth=False)
    R._fix_normals(ob)
    return ob


def cluster(x, y, z, size, mat, rnd, grp, up=(0, 0, 1), spread=0.5, n=None, kind="crystal"):
    upv = Vector(up).normalized()
    objs = []
    n = n or rnd.randint(4, 7)
    for k in range(n):
        tilt = Vector((rnd.gauss(0, spread), rnd.gauss(0, spread), rnd.gauss(0, spread)))
        ax = (upv + tilt).normalized()
        if ax.dot(upv) < 0.35:
            ax = (upv + tilt * 0.3).normalized()
        L = size * (1.0 if k == 0 else rnd.uniform(0.35, 0.8))
        r = L * rnd.uniform(0.15, 0.22)
        off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.3, 0.3))) * size * 0.2
        base = Vector((x, y, z)) + off - ax * (L * 0.1)
        objs.append(tagk(prism("Cry", base, ax, L, r, mat), kind, grp))
    return objs


def glow_entry(col, p, rad_m, levels=(0.09, 0.2), radii=(1.0, 0.5), water="mirror", rock=True, flat=1.0,
               wlevel=0.12):
    """Screen ellipses of a local light (crystal cluster / lantern) at world point p, radius rad_m metres:
    flat rings on the rock around it, and on the water either its mirror image (a vertical glow streak under
    the reflection, water="mirror") or the light spilled on the surface around its base (water="spill")."""
    c, r = rc(p)
    d = P.project(p, STAND)[2]
    rp = max(3.0, rad_m * P.F_PX / d)
    e = dict(col=col, c=c, r=r, rx=rp, ry=rp * flat, levels=list(levels), radii=list(radii), rock=rock,
             water=water is not None, wmode=water)
    if water == "mirror":
        mc, mr = rc((p[0], p[1], -p[2]))
        e.update(wc=mc, wr=mr + rp * 0.2, wrx=rp * 0.45, wry=rp * 0.9, wlevels=[wlevel], wradii=[1.0])
    elif water == "spill":
        bc, br = rc((p[0], p[1], 0.0))
        e.update(wc=bc, wr=br, wrx=rp * 1.1, wry=rp * 0.4, wlevels=[wlevel * 0.7], wradii=[1.0])
    return e


def back_scene(rnd):
    BACK_GLOWS.clear()
    mats = [rock_mat(RB[k], f"Rock{k}", noise=0.07) for k in range(len(BINS))]
    ceil_mats = [rock_mat(RB[k], f"Ceil{k}", noise=0.08, bias=-0.06) for k in range(len(BINS))]
    # stalactites / columns: lit from the front-left (the shaft's pool and the crystals), so they read as
    # lighter faceted spikes against the darker wall instead of contour drawings
    LS = Vector((-0.55, -0.5, 0.67)).normalized()
    stal_mats = [R.m_tone(RB[k], [0.36, 0.58, 0.8], light=LS, noise=0.05, nscale=0.5, ncoord="world", ndetail=1.0,
                          bias=0.04, name=f"Stal{k}") for k in range(len(BINS))]
    # ---- side walls (alcoves / promontories along the lake, curving into the vault), one piece per distance bin
    for side in (-1, 1):
        for k, (y0, y1, _) in enumerate(BINS):
            ys = np.linspace(y0, min(y1, 114.0), max(3, int((min(y1, 114.0) - y0) / 1.3)))
            vs = np.linspace(0.0, 1.0, 44)

            def fn(y, v, side=side):
                zc = ceil_z(side * 20.0, y)
                z = -1.5 + v * (zc + 2.5)
                return (wall_x(side, y, z), y, z)
            sheet(f"Wall{side}_{k}", ys, vs, fn, mats[k], "wall", f"wall{side}_{k}")
    # ---- vaulted ceiling (seen at a grazing angle: the top rows of the crop)
    for k, (y0, y1, _) in enumerate(BINS):
        xs = np.linspace(-44, 44, 60)
        ys = np.linspace(y0, min(y1, 114.0), max(3, int((min(y1, 114.0) - y0) / 2.0)))
        sheet(f"Ceil{k}", xs, ys, lambda x, y: (x, y, ceil_z(x, y) + 0.55 * fbm((x * 0.22, y * 0.22, 7.7), 2)),
              ceil_mats[k], "ceiling", f"ceil{k}")
    # ---- stratified far wall
    xs = np.linspace(-47, 47, 95)
    vs = np.linspace(0.0, 1.0, 30)

    def fw(x, v):
        z = -1.5 + v * (ceil_z(x, 112.0) + 2.5)
        return (x, farwall_y(x, z), z)
    sheet("FarWall", xs, vs, fw, mats[3], "farwall", "farwall")
    # ---- stalactites: fringe of the ceiling, denser and longer towards the far wall
    for k in range(90):
        y = 36.0 + 76.0 * rnd.random() ** 0.7
        xl = abs(wall_x(-1, y, 7.0)) - 1.5
        xr = abs(wall_x(1, y, 7.0)) - 1.5
        x = rnd.uniform(-xl, xr)
        if abs(x - HOLE[0]) < 2.2 and abs(y - HOLE[1]) < 5:
            continue                                   # keep the hole open
        ln = rnd.uniform(0.8, 2.6) * (1.9 if rnd.random() < 0.18 else 1.0) * (0.8 + 0.4 * (y - 36) / 76)
        zt = ceil_z(x, y) + 0.4
        ob = spike("Stal", x, y, zt, ln + 0.4, rnd.uniform(0.22, 0.34) * ln + 0.18, stal_mats[bin_of(y)], rnd)
        tagk(ob, "stalactite", f"stal{k}")
    # ---- two hourglass columns (stalactite met stalagmite) and rock islets on the far waterline
    for (cx, cy, rw) in ((-17.0, 92.0, 2.0), (15.5, 77.0, 1.8)):
        zc = ceil_z(cx, cy) + 0.4
        n = 12
        pts, rads = [], []
        for i in range(n + 1):
            t = i / n
            z = -0.4 + t * (zc + 0.4)
            waist = 0.3 + 0.7 * min(1.0, abs(t - 0.58) / 0.42) ** 1.6
            rr = rw * waist * (1 + 0.12 * fbm((cx, z * 0.8, 1.0), 1))
            pts.append((cx + 0.25 * fbm((z * 0.3, cx, 2.0), 1), cy, z))
            rads.append((rr, rr * 0.9))
        tagk(R.loft("Column", pts, rads, stal_mats[bin_of(cy)], segs=9, smooth=False), "column", f"col{cx}")
    for (ix, iy, ir, ih) in ((-9.0, 101.0, 1.6, 0.9), (6.5, 104.0, 2.2, 1.3), (-20.0, 94.0, 1.4, 0.8),
                             (19.0, 80.0, 1.2, 0.7), (24.0, 99.0, 1.8, 1.0)):
        ob = C.add_prim("ico", "Islet", mats[bin_of(iy)], radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((ix, iy, ih * 0.3)) @ Matrix.Diagonal((ir * 1.4, ir, ih, 1))
        tagk(ob, "islet", f"isl{ix}")
    # ---- crystal clusters = local lights
    cm = R.m_tone(CRYC_B, [0.42, 0.78], light=LB, wrap=0.3, name="CryC")
    vm = R.m_tone(CRYV_B, [0.42, 0.78], light=LB, wrap=0.3, name="CryV")
    far = [(-23.0, 0.6, "c", 1.5), (-8.5, 3.4, "v", 1.1), (5.0, 1.0, "c", 1.7), (17.0, 4.3, "v", 1.2),
           (27.0, 0.9, "c", 1.3)]
    for (x, z, cc, s) in far:
        y = farwall_y(x, z) - 0.5
        cluster(x, y, z, s, cm if cc == "c" else vm, rnd, f"cf{x}", up=(0, -0.45, 1))
        BACK_GLOWS.append(glow_entry(PR.crystal[0] if cc == "c" else PR.crystal[1], (x, y, z + s * 0.4), s * 1.25))
    side = [(-1, 41.0, 0.3, "c", 1.7), (-1, 73.0, 2.9, "v", 1.4), (1, 51.0, 0.5, "v", 1.6), (1, 88.0, 1.6, "c", 1.3)]
    for (sd, y, z, cc, s) in side:
        x = wall_x(sd, y, z) - sd * 0.5
        cluster(x, y, z, s, cm if cc == "c" else vm, rnd, f"cs{y}", up=(-sd * 0.55, -0.25, 1))
        BACK_GLOWS.append(glow_entry(PR.crystal[0] if cc == "c" else PR.crystal[1], (x, y, z + s * 0.4), s * 1.25))
    cluster(-15.6, 91.0, 0.2, 1.0, vm, rnd, "ccol", up=(0.5, -0.4, 1))
    BACK_GLOWS.append(glow_entry(PR.crystal[1], (-15.6, 91.0, 0.6), 1.2))
    tagk(R.hpoly("Water", [(-9000, -40), (9000, -40), (9000, 3200), (-9000, 3200)], 0.0, R.m_flat(WB[3]), 0.01), "water")


# ================================================================== FRONT LAYER geometry
# rock chunks (x, y, sx, sy, sz, top, kind): the promontory under the angler (flat top at standH around the feet)
# and boulders stepping down into the water at the sides; the centre above the promontory stays open water
CHUNKS = [(0.0, -1.75, 2.2, 2.45, 1.3, STAND, "ledge", 3),
          (-2.2, -1.5, 1.45, 1.7, 1.15, STAND - 0.1, "ledge", 3),
          (2.3, -1.25, 1.5, 1.75, 1.1, STAND - 0.18, "ledge", 3),
          (0.95, 0.3, 0.75, 0.62, 0.55, STAND - 0.32, "ledge", 2),
          (-0.9, 0.18, 0.66, 0.55, 0.5, STAND - 0.42, "ledge", 2),
          (-3.7, -0.35, 1.25, 1.1, 0.95, 0.78, "boulder", 3),
          (3.9, -0.05, 1.2, 1.2, 0.9, 0.62, "boulder", 3),
          (-5.15, 0.95, 0.95, 0.85, 0.8, 0.98, "boulder", 2),
          (5.45, 1.35, 1.0, 0.95, 0.8, 0.86, "boulder", 2),
          (-2.6, 0.45, 0.45, 0.38, 0.3, 0.3, "boulder", 2),
          (2.9, 0.9, 0.42, 0.35, 0.28, 0.24, "boulder", 2)]


def rock_chunk(name, x, y, sx, sy, sz, top, mat, rnd, kind, sub=3, jag=0.1, flat=True):
    """Faceted boulder; flat=True cuts it at `top` (a walkable slab, gently undulating), else `top` is the
    crown of a rounded rock."""
    ob = C.add_prim("ico", name, mat, radius=1.0, location=(0, 0, 0), subdivisions=sub)
    cz = top - sz * (0.55 if flat else 1.0)
    ph = rnd.uniform(0, 100)
    for v in ob.data.vertices:
        n = v.co.normalized()
        d = 1.0 + jag * fbm((n.x * 1.6 + ph, n.y * 1.6, n.z * 1.6), 2)
        wx, wy, wz = x + n.x * sx * d, y + n.y * sy * d, cz + n.z * sz * d
        if flat:
            feet = min(1.0, math.hypot(wx, wy) / 1.3)          # level under the angler's feet
            wz = min(wz, top + 0.045 * feet * fbm((wx * 1.3, wy * 1.3, ph), 2))
        if wz < -0.05:
            # below the waterline: pull the vertex onto the waterline ring (no flat skirt / brim around the rock)
            u = (-0.05 - cz) / (sz * d)
            k = math.sqrt(max(0.0, 1 - u * u)) if u > -1 else 1.0
            k = min(k, math.hypot(n.x, n.y))
            hn = math.hypot(n.x, n.y) or 1.0
            wx, wy, wz = x + n.x / hn * sx * d * k, y + n.y / hn * sy * d * k, -0.05
        v.co = (wx, wy, wz)
    ob.data.update()
    ob.matrix_world = Matrix.Identity(4)
    C.set_smooth(ob, False)
    return tagk(ob, kind, name)


def front_scene(rnd):
    mt = R.m_tone
    FRONT_GLOWS.clear()
    # the foreground sits in the dark: the key is the far shaft, the lantern / crystals light it locally
    rockm = mt(ROCKF, [0.3, 0.5, 0.66, 0.82], light=LB, noise=0.12, nscale=1.2, ncoord="world", ndetail=1.0,
               bias=-0.24, grads=((1, -0.6, -3.2, -0.14, "world"),), name="RockF")
    solids = []
    for i, (x, y, sx, sy, sz, top, kind, sub) in enumerate(CHUNKS):
        solids.append(rock_chunk(f"Chunk{i}", x, y, sx, sy, sz, top, rockm, rnd, kind, sub))
    # ---- two stalagmite groups framing the mid-distance sides: a rounded base mound + blunt spires
    stm = mt(ROCKF, [0.3, 0.5, 0.68, 0.86], light=LB, noise=0.1, nscale=1.0, ncoord="world", ndetail=1.0,
             bias=-0.08, name="Stalag")
    for (x, y, h, r, g) in ((-7.6, 9.7, 2.4, 0.5, "sgL"), (-6.8, 10.2, 1.3, 0.38, "sgL2"), (-8.3, 10.4, 0.85, 0.32, "sgL3"),
                            (8.3, 12.7, 3.0, 0.56, "sgR"), (7.5, 13.2, 1.45, 0.4, "sgR2"), (9.1, 13.5, 0.95, 0.34, "sgR3")):
        solids.append(tagk(spike("Stalag", x, y, -0.3, h + 0.3, r, stm, rnd, down=False, sides=9, rings=BLUNT),
                           "stalagmite", g))
    for (x, y, sx, sy, sz, top) in ((-7.2, 9.15, 0.62, 0.5, 0.6, 0.52), (-8.05, 9.8, 0.5, 0.45, 0.5, 0.38),
                                    (-6.55, 9.95, 0.42, 0.38, 0.42, 0.3), (7.85, 12.15, 0.68, 0.55, 0.62, 0.55),
                                    (8.85, 12.8, 0.52, 0.48, 0.5, 0.4), (7.2, 12.9, 0.45, 0.4, 0.45, 0.32)):
        solids.append(rock_chunk("Base", x, y, sx, sy, sz, top, rockm, rnd, "boulder", 2, flat=False))
    # ---- crystal clusters: corner boulders, stalagmite bases, a small one on the right step
    cm = mt(CRYC, [0.4, 0.76], light=LB, wrap=0.3, name="CryCF")
    vm = mt(CRYV, [0.4, 0.76], light=LB, wrap=0.3, name="CryVF")
    crys = [(-5.05, 0.95, 0.96, "c", 0.62, (0.3, -0.35, 1)), (5.35, 1.3, 0.84, "v", 0.55, (-0.3, -0.35, 1)),
            (-7.1, 9.05, 0.44, "c", 0.8, (0.35, -0.4, 1)), (7.8, 12.05, 0.46, "v", 0.9, (-0.35, -0.4, 1)),
            (3.6, 0.15, 0.6, "c", 0.34, (0.2, -0.3, 1))]
    refl = []
    for i, (x, y, z, cc, s, up) in enumerate(crys):
        objs = cluster(x, y, z, s, cm if cc == "c" else vm, rnd, f"fc{i}", up=up, spread=0.42)
        if y > 5:
            refl += objs
        FRONT_GLOWS.append(glow_entry(PR.crystal[0] if cc == "c" else PR.crystal[1], (x, y, z + s * 0.45),
                                      s * 1.7, levels=(0.1, 0.22), water="mirror" if y > 5 else "spill"))
    # ---- glowing mushrooms (cyan caps) on the ledge sides
    stemm = mt(STEM, [0.55], light=LB, name="Stem")
    capm = mt(CRYC[:2], [0.72], light=LB, wrap=0.5, bias=-0.05, name="Cap")
    for (mx, my, mz, k) in ((-2.6, -0.45, STAND - 0.06, 3), (2.4, -0.35, STAND - 0.14, 2)):
        for j in range(k):
            x = mx + (j - (k - 1) / 2) * 0.17 + rnd.uniform(-0.03, 0.03)
            y = my + rnd.uniform(-0.08, 0.08)
            h = rnd.uniform(0.2, 0.32) * (1.0 if j == k // 2 else 0.72)
            cr = rnd.uniform(0.075, 0.1) * (1.0 if j == k // 2 else 0.8)
            tagk(R.loft("MStem", [(x, y, mz - 0.1), (x + 0.02, y, mz + h)], [0.024, 0.02], stemm, 6), "mushroom",
                 f"m{mx}{j}")
            ob = R.ellipsoid("MCap", (x + 0.02, y, mz + h), (cr, cr, cr * 0.7), capm, segs=10, rings=6)
            tagk(ob, "mushroom", f"m{mx}{j}")
        FRONT_GLOWS.append(glow_entry(PR.crystal[0], (mx, my, mz + 0.15), 0.5, levels=(0.1,), radii=(1.0,),
                                      water=None))
    # ---- crook post with a hanging lantern (the warm accent of the cave, left of the angler)
    wood = mt(WOOD, [0.4, 0.72], light=LB, noise=0.1, nscale=4.0, nvec=(1, 1, 0.15), name="Wood")
    px, py = -1.75, -0.95
    top = STAND + 1.62
    solids.append(tagk(R.loft("Post", [(px, py, STAND - 0.3), (px + 0.02, py, top - 0.1), (px + 0.06, py, top)],
                              [0.055, 0.05, 0.045], wood, 8, smooth=False), "post", "post"))
    solids.append(tagk(R.loft("Arm", [(px + 0.03, py, top - 0.04), (px + 0.2, py, top + 0.02), (px + 0.4, py, top - 0.02)],
                              0.03, wood, 6, smooth=False), "post", "post"))
    lx, lz = px + 0.4, top - 0.36
    metal = mt(METAL, [0.55], light=LB, name="Metal")
    tagk(R.loft("Hook", [(lx, py, top - 0.03), (lx, py, lz + 0.17)], 0.008, metal, 5), "lantern", "lamp")
    tagk(R.box("LCap", (lx, py, lz + 0.14), (0.2, 0.2, 0.05), metal, bevel=0.01), "lantern", "lamp")
    tagk(R.box("LBase", (lx, py, lz - 0.14), (0.2, 0.2, 0.04), metal), "lantern", "lamp")
    tagk(R.box("Glass", (lx, py, lz), (0.15, 0.15, 0.24), R.m_flat(FLAME[1])), "flame", "lamp")
    tagk(R.box("Flame", (lx, py - 0.08, lz - 0.02), (0.05, 0.01, 0.1), R.m_flat(FLAME[2])), "flame", "lamp")
    for (dx, dy) in ((-0.08, -0.08), (0.08, -0.08)):
        tagk(R.box("Bar", (lx + dx, py + dy, lz), (0.022, 0.022, 0.26), metal), "lantern", "lamp")
    kl, kr = LAMP_POOL
    FRONT_GLOWS.append(glow_entry(FLAME[1], (lx, py, STAND - 0.05), 1.3 * kr, levels=(0.08 * kl, 0.17 * kl),
                                  radii=(1.0, 0.5), water=None, flat=0.55))
    FRONT_GLOWS.append(glow_entry(FLAME[1], (lx, py, lz), 0.32 * kr, levels=(0.12 * kl, 0.26 * kl), radii=(1.0, 0.55),
                                  water=None))
    refl += [s for s in solids if s.get("kind") in ("stalagmite", "boulder")]
    return refl


# ================================================================== post helpers (shared)
def ring_levels(cx, cy, rx, ry, radii, levels, h, w, soft=4.0, thr=None):
    """Level map (0 / ring levels) of a glow ellipse: flat rings, dithered hand-over (~soft px; Bayer on rock,
    horizontal dashes when thr = dash_threshold is given, for water)."""
    rows = np.arange(h)[:, None] + 0.5
    cols = np.arange(w)[None, :] + 0.5
    d = np.sqrt(((cols - cx) / rx) ** 2 + ((rows - cy) / ry) ** 2)
    jit = ((R.bayer(h, w) if thr is None else thr) - 0.5) * soft / max(3.0, min(rx, ry))
    lv = np.zeros((h, w))
    for rad, level in sorted(zip(radii, levels), key=lambda t: -t[0]):
        lv = np.where(d + jit < rad, level, lv)
    return lv


def streak_levels(g, h, w, rng):
    """Retro reflection of a light on calm water: stacked horizontal dashes under its mirror image, every
    other row, shrinking and fading downwards."""
    lv = np.zeros((h, w))
    n = int(max(3, g["wry"] * 2.0))
    top = int(round(g["wr"] - g["wry"]))
    for k in range(0, n, 2):
        t = k / n
        r = top + k
        if not 0 <= r < h:
            continue
        hw = g["wrx"] * (1 - t) ** 0.7 * rng.uniform(0.6, 1.1)
        c0, c1 = int(round(g["wc"] - hw)), int(round(g["wc"] + hw)) + 1
        lv[r, max(0, c0):max(0, min(w, c1))] = g["wlevels"][0] * (1.0 if t < 0.45 else 0.6)
    return lv


def apply_glows(idx, pal, glows, rock_mask, water_mask=None, snap=0.03):
    """Local lights in palette space: per glow colour one level map (max over the lights), each ring level
    mixes the pixels towards the glow colour in display space (gentle on the dark cave colours)."""
    h, w = idx.shape
    rng = random.Random(41)
    wthr = R.dash_threshold(h, w, rng, 2, 8)
    for col in sorted(set(g["col"] for g in glows)):
        lv = np.zeros((h, w))
        for g in glows:
            if g["col"] != col:
                continue
            if g["rock"]:
                lv = np.maximum(lv, ring_levels(g["c"], g["r"], g["rx"], g["ry"], g["radii"], g["levels"], h, w)
                                * rock_mask)
            if water_mask is not None and g["water"]:
                if g.get("wmode") == "mirror":
                    lw = streak_levels(g, h, w, rng)
                else:
                    lw = ring_levels(g["wc"], g["wr"], g["wrx"], g["wry"], g["wradii"], g["wlevels"], h, w, soft=5.0,
                                     thr=wthr)
                lv = np.maximum(lv, lw * water_mask)
        for level in sorted(np.unique(lv[lv > 0])):
            m = (lv == level) & (idx >= 0)
            idx, pal = R.blend_idx(idx, pal, m, col, float(level), space="srgb", snap=snap)
    return idx, pal


def step_lut(pal, table):
    lut = np.arange(len(pal))
    for i, hx in enumerate(pal.hex):
        if hx in table:
            lut[i] = pal.index(table[hx])
    return lut


# ================================================================== back composite
# water depth zones (start row, end row) for WB[i] -> WB[i+1]: black-violet far water -> teal play area
WATER_Z = [(118, 124), (128, 138), (146, 162), (176, 196), (294, 314)]
ROW_H, ROW_P = 116, 335


def reflect_tint(ref_hex, k, dark):
    """Colour transform for reflections: towards the water colour by k, then darker (linear x dark)."""
    wl = R.s2l(R.hexrgb(ref_hex))

    def fn(c):
        return R.l2s((R.s2l(c) * (1 - k) + wl[None, :] * k) * dark)
    return fn


REFL_Z = 4.5          # reflections show the lowest 4.5 m of the shell only, dissolving over their last 45 %


def far_reflection(pal, idx, water, refl_objs):
    """EXACT reflection of the cave shell in the water (mirror pass): the layers' own colours tinted towards
    the water and darkened, runs >= 3 px, a lighter break line every third row; only the lowest REFL_Z metres
    of the mirrored shell, dissolving towards that height (the full 10 m walls would blacken the play area).
    Crystal reflections stay bright and shimmer (alternate rows)."""
    mp = R.mirror_pass(refl_objs, ids=True, tag="cave_farmirror")
    mk = R.kind_map(mp)
    rng = random.Random(7)
    zw = R.world_z(mp["depth"], STAND)
    ra = mp["a"] & water & (zw > -REFL_Z)
    midx, _ = R.quantize(mp["rgb"], ra, pal, dither=False)
    crym = ra & (mk == "crystal")
    lab = R.min_runs(np.where(ra & ~crym, midx + 1, 0), 3)
    top = np.argmax(water, 0)
    rows = np.arange(H)[:, None]
    frac = np.clip(-zw / REFL_Z, 0, 1)
    out = idx.copy()
    keepm = np.zeros((H, W), bool)
    linem = np.zeros((H, W), bool)
    for r in range(H):
        m = lab[r] > 0
        if not m.any():
            continue
        p = np.clip((frac[r] - 0.55) / 0.45, 0, 1)
        brk = R.run_noise(rng, W, 3, 8) < p
        keep = m & ~brk & water[r]
        out[r, keep] = lab[r, keep] - 1
        keepm[r] = keep
        if (r - int(np.median(top))) % 3 == 2:
            linem[r] = R.dash_mask(W, 0.4, 6, 20, rng) & keep
    body = keepm & ~linem
    out, pal = R.recolour(out, pal, body, reflect_tint(WB[1], 0.35, 0.8), snap=0.03)
    out, pal = R.recolour(out, pal, linem, reflect_tint(WB[2], 0.6, 0.95), snap=0.03)
    # crystal reflections: small, bright, shimmering (first 2 rows solid, then every other row)
    ctop = np.where(crym.any(0), np.argmax(crym, 0), H)
    shim = crym & (((rows - ctop[None, :]) < 2) | ((rows - ctop[None, :]) % 2 == 0))
    out[shim] = midx[shim]
    out, pal = R.recolour(out, pal, shim, reflect_tint(WB[2], 0.18, 0.92), snap=0.03)
    return out, pal, keepm | shim


EDGE_X = 11.5         # metres: beyond this the water falls off one band darker towards the walls


def edge_falloff(pal, idx, water):
    """Cinematic light falloff: the lit centre of the lake (the play area) stays on its band, the water towards
    the side walls is one band darker, joined by horizontal dash dithering."""
    ys = np.linspace(0.5, 400.0, 4000)
    rows_y = np.array([rc((0.0, y, 0.0))[1] for y in ys])
    ppm = np.array([P.F_PX / P.project((0.0, y, 0.0), STAND)[2] for y in ys])
    thr = R.dash_threshold(H, W, random.Random(13), 3, 10)
    dn = step_lut(pal, DN)
    cols = np.arange(W) + 0.5
    out = idx.copy()
    for r in range(H):
        m = water[r]
        if not m.any() or r < rows_y.min() or r > rows_y.max():
            continue
        hw = EDGE_X * float(np.interp(r + 0.5, rows_y[::-1], ppm[::-1]))
        e = np.abs(cols - W / 2) / hw + (thr[r] - 0.5) * 0.14
        sel = m & (e > 1.0)
        out[r, sel] = dn[idx[r, sel]]
    return out


def draw_dash(img_idx, r, c0, n, lut_arr, mask):
    r = int(r)
    if r < 0 or r >= img_idx.shape[0]:
        return
    for c in range(int(c0), int(c0) + max(1, int(n))):
        if 0 <= c < img_idx.shape[1] and mask[r, c]:
            img_idx[r, c] = lut_arr[img_idx[r, c]]


def calm_marks(pal, idx, water, avoid):
    """Very sparse wave marks (a cave lake is calm) + drip rings under the far stalactites; nothing in the
    central play rectangle."""
    rng = random.Random(11)
    up, dn = step_lut(pal, UP), step_lut(pal, DN)
    ok = water & ~avoid
    for k in range(160):
        y = 4.0 + 100 * rng.random() ** 1.5
        x = rng.uniform(-1, 1) * (y * 0.2 + 14)
        c, r = rc((x, y, 0))
        if not (0 <= c < W and 0 <= r < H - 1) or not ok[int(r), int(c)]:
            continue
        if 176 < c < 464 and r > 170:
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = min(12, max(2, int(round(420 / depth * rng.uniform(0.25, 0.6)))))
        draw_dash(idx, r, c, n, up, ok)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), dn, ok)
    # drip rings: 2 concentric flat ellipses (lighter upper arc, darker lower arc)
    for (x, y) in ((-12.5, 44.0), (11.0, 58.0), (-8.0, 84.0), (13.5, 36.0), (2.0, 96.0)):
        c0, r0 = rc((x, y, 0))
        d = P.project((x, y, 0), STAND)[2]
        for rad in (0.55, 1.15):
            rx = rad * P.F_PX / d
            ry = max(0.8, rx * (P.CAM_UP + STAND) / d)
            for a in np.linspace(0, 2 * math.pi, int(8 + rx * 3), endpoint=False):
                cc, rr = int(round(c0 + rx * math.cos(a))), int(round(r0 + ry * math.sin(a)))
                if 0 <= cc < W and 0 <= rr < H and ok[rr, cc]:
                    idx[rr, cc] = (dn if math.sin(a) > 0.3 else up)[idx[rr, cc]]
    return idx


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    PER.hook("back_scene", rnd=rnd, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("cave_back")
    kid = R.kind_map(ps)
    sky = ~ps["a"]
    water = kid == "water"
    cry = kid == "crystal"
    rock = np.isin(kid, ["wall", "ceiling", "farwall", "stalactite", "column", "islet"])
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = SKY_RGB[sky]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid | sky | cry, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=2.0, rel=0.015, steps=1,
                          skip=set(pal.index(c) for c in CRYC_B + CRYV_B))
    idx = np.where(rock | cry, lined, idx)
    # ---- water: depth bands with horizontal dash dithering
    wband = R.water_bands(pal, WB, WATER_Z, random.Random(5), ROW_H, ROW_P)
    idx[water] = wband[water]
    idx = edge_falloff(pal, idx, water)
    # ---- exact reflections of the cave shell (no ceiling: it would blacken the whole play area)
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("wall", "farwall", "column", "islet", "crystal")]
    idx, pal, farm = far_reflection(pal, idx, water, refl_objs)
    # ---- front-object reflections / ripples (computed by the front pass)
    ov = _OV.get("ov")
    if ov is None:
        p = os.path.join(R.WORK, "front_overlay.npy")
        ov = np.load(p) if os.path.exists(p) else np.zeros((H, W), np.int8)
    fg = FRONT_GLOWS
    if not fg:
        p = os.path.join(R.WORK, "front_glows.json")
        fg = json.load(open(p)) if os.path.exists(p) else []
    idx = calm_marks(pal, idx, water, farm | (ov > 0))
    idx[water & (ov == OV_DARK)] = pal.index(REFL[0])
    for code, table in ((OV_WM1, DN), (OV_RIPPLE, UP)):
        m = water & (ov == code)
        idx[m] = step_lut(pal, table)[idx[m]]
    idx[water & (ov == OV_CRYC)] = pal.index(CRYC_B[1])
    idx[water & (ov == OV_CRYV)] = pal.index(CRYV_B[1])
    # ---- crystal local lights: glow rings on the rock + halos (and their mirror images) on the water
    idx, pal = apply_glows(idx, pal, BACK_GLOWS, rock, water)
    idx, pal = apply_glows(idx, pal, [g for g in fg if g["water"]], np.zeros((H, W), bool), water)
    # ---- the shaft's pool: a faint lit sheen + sparse short glitter, ONLY under the shaft (far third)
    rows = np.arange(H)[:, None]
    cols = np.arange(W)[None, :]
    e = ((cols - POOL_C) / (_PW * 0.75)) ** 2 + ((rows - POOL_R) / 4.5) ** 2
    sheen = 0.85 * np.exp(-e)
    sm = water & (sheen > R.dash_threshold(H, W, random.Random(23), 2, 6)) & (ov == 0)
    idx, pal = R.blend_idx(idx, pal, sm, GLIT[0], 0.24, lighter=True, snap=0.03)
    g_am, g_br = R.glitter_masks(PR, water, random.Random(17), avoid=(ov > 0))
    idx[g_am] = pal.index(GLIT[0])
    idx[g_br] = pal.index(GLIT[1])
    # ---- thin cold mist on the far waterline (dash dithered, wisps)
    land = ~sky & ~water
    line = np.array([np.flatnonzero(water[:, c]).min() if water[:, c].any() else H for c in range(W)], float)
    ma = R.mist_amount(PR, line, land, water)
    thr = R.dash_threshold(H, W, random.Random(29), 2, 9)
    idx, pal = R.blend_idx(idx, pal, (ma > thr) & (ma > 0.08), PR.mist["col"], 0.5, snap=0.03)
    # ---- the daylight shaft (2 levels + dust)
    idx, pal = R.apply_shaft(idx, pal, PR)
    idx, pal = PER.hook("back_post", idx, pal, kid=kid, water=water, sky=sky, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "back")
    wl = R.lum(WB[4])
    print("HYB back colours", R.count_colours(img), "palette", len(pal), "play water L", round(wl, 3),
          "far water L", round(R.lum(WB[0]), 3), "tint", PR.water["tint"], "deep", PR.water["deep"])
    print("CHECK shaft top", PR.shaft["top"], "bottom", PR.shaft["bottom"], "glitter rows", PR.glitter["row0"],
          PR.glitter["row1"], "glitter px", int(g_am.sum() + g_br.sum()), "rows",
          int(np.flatnonzero((g_am | g_br).any(1)).min()) if (g_am | g_br).any() else -1,
          int(np.flatnonzero((g_am | g_br).any(1)).max()) if (g_am | g_br).any() else -1)
    crop = idx[R.CROP[1]:R.CROP[1] + R.CROP[3], R.CROP[0]:R.CROP[0] + R.CROP[2]]
    print("CHECK crop top row kinds", sorted(set(kid[R.CROP[1], R.CROP[0]:R.CROP[0] + R.CROP[2]].tolist())),
          "sky px in crop", int(sky[R.CROP[1]:R.CROP[1] + R.CROP[3], R.CROP[0]:R.CROP[0] + R.CROP[2]].sum()),
          "crop colours", len(np.unique(crop)))
    return pal


# ---------------------------------------------------------------- front post helpers
def front_reflections(refl):
    """Reflections of every front object standing in water -> back-layer overlay codes. Rock: 2 tones (dark /
    water -1), per-row +-1 px shift for runs >= 4 px, dash breaks, fade over 60 % of the reflected height.
    Crystals: their own bright code, shimmering rows."""
    mp = R.mirror_pass(refl, ids=True, tag="cave_frontmirror")
    rng = random.Random(3)
    a = mp["a"]
    ids = mp["id"]
    mk = R.kind_map(mp)
    z = R.world_z(mp["depth"], STAND)
    zmin = {}
    for i in np.unique(ids[a]):
        zmin[int(i)] = float(np.min(z[a & (ids == i)]))
    zm = np.vectorize(lambda i: zmin.get(int(i), -1.0))(ids)
    frac = np.clip(z / np.minimum(zm, -1e-3), 0, 1)
    frac[~a] = 0
    lumv = (mp["rgb"] * np.array([0.3, 0.55, 0.15])).sum(-1)
    code = np.where(a, np.where(lumv < 0.3, OV_DARK, OV_WM1), 0)
    code[a & (mk == "crystal")] = OV_CRYC
    vio = a & (mk == "crystal") & (mp["rgb"][..., 0] > mp["rgb"][..., 1])
    code[vio] = OV_CRYV
    code[a & (z > -0.02)] = 0
    p = np.clip(frac / 0.6, 0, 1) ** 1.3
    shift = [(0, 1, 1, 0, -1, -1)[r % 6] for r in range(H)]
    out = np.zeros((H, W), np.int8)
    for r in range(H):
        row = code[r]
        if not row.any():
            continue
        brk = (R.run_noise(rng, W, 2, 6) < p[r]) & (row < OV_CRYC)
        row = np.where(brk, 0, row)
        if r % 5 in (0, 2):
            row = np.where(R.dash_mask(W, 0.45, 2, 6, rng) & (row < OV_CRYC), 0, row)
        if r % 2:
            row = np.where(row >= OV_CRYC, 0, row)
        m = row > 0
        edges = np.flatnonzero(np.diff(np.concatenate([[0], m.astype(int), [0]])))
        for s, e in zip(edges[::2], edges[1::2]):
            d = shift[r] if e - s >= 4 else 0
            s2, e2 = max(0, s + d), min(W, e + d)
            out[r, s2:e2] = row[s2 - d:e2 - d]
    return out


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    refl = front_scene(rnd)
    PER.hook("front_scene", rnd=rnd, refl=refl, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("cave_front")
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    glowk = np.isin(kid, ["crystal", "mushroom", "flame"])
    idx = R.despeckle(idx, ps["id"], protect=~solid | glowk, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1,
                        skip=set(pal.index(c) for c in CRYC + CRYV + FLAME + STEM))
    # local lights on the rock: crystal glow rings, the lantern's warm pool on the ledge
    rockm = np.isin(kid, ["ledge", "boulder", "stalagmite"])
    idx, pal = apply_glows(idx, pal, FRONT_GLOWS, rockm | (kid == "post"), snap=0.035)
    # cyan (crystal light) rim on the OUTER top silhouette of solid props, before the outline
    solid_k = np.isin(kid, ["ledge", "boulder", "stalagmite", "post", "lantern"])
    idx, pal = R.rim_light(idx, pal, PR, mask=solid_k)
    # selective outline for solid props only (hue-shifted darker neighbour, never ink); glowing things get none
    noline = glowk
    base = np.where(noline, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    idx = np.where(noline & (idx >= 0), idx, ol)
    # ---- back-layer overlays: reflections + ripple dashes at the stalagmite bases
    ov = front_reflections(refl)
    rng = random.Random(5)
    for (x, y, rad) in ((-7.6, 9.6, 0.7), (-6.7, 10.4, 0.45), (8.3, 12.6, 0.8), (7.4, 13.3, 0.5)):
        c, r = rc((x, y, 0))
        depth = P.project((x, y, 0), STAND)[2]
        half = rad * P.F_PX / depth
        n = max(2, int(520 / depth * 0.22))
        for dr, sc in ((0, 1.0), (1, 0.6), (2, 1.25)):
            rr = int(r + dr)
            if not 0 <= rr < H or rng.random() < 0.25:
                continue
            for side in (-1, 1):
                c0 = c + side * (half * sc + rng.uniform(1, 3)) - (n if side < 0 else 0)
                for cc in range(int(c0), int(c0 + n)):
                    if 0 <= cc < W and ov[rr, cc] == 0:
                        ov[rr, cc] = OV_RIPPLE
    _OV["ov"] = ov
    np.save(os.path.join(R.WORK, "front_overlay.npy"), ov)
    with open(os.path.join(R.WORK, "front_glows.json"), "w") as f:
        json.dump(FRONT_GLOWS, f)
    idx, pal = PER.hook("front_post", idx, pal, kid=kid, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "front")
    cl = [R.lum(c) for c in CRYC[1:] + CRYV[1:]]
    print("HYB front colours", R.count_colours(img), "palette", len(pal), "crystal L min", round(min(cl), 3),
          "vs play water L", round(R.lum(WB[4]), 3))


def main():
    C.reset_scene()
    which = PER.which()                      # back / front (+ --period <p> [--dry], see hyb_period)
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    d = PER.stage_json(SID, PR, clouds=False, birds=None)
    src = json.load(open(os.path.join(C.DATA, f"stage_{SID}.json"), encoding="utf-8"))
    diff = [k for k in src if k not in ("waterTint", "waterDeep", "clouds") and src[k] != d.get(k)]
    print("CHECK json keys same", list(src.keys()) == list(d.keys()), "gameplay diffs", diff,
          "waterTint", d["waterTint"], "waterDeep", d["waterDeep"], "clouds", d["clouds"])
    print("HYB CAVE done")


if __name__ == "__main__":
    main()
