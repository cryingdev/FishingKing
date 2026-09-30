"""
hybrid - the sea stage (tetrapod breakwater): retro16 craft + the "sea" preset mood (bright late afternoon).

Back layer (opaque):
  * banded sky from the preset (hazy white horizon -> pale cyan -> blue), the sun high on the left ABOVE the
    game crop (only its outer glow ring reaches into the crop), one thin stratus streak;
  * layered far silhouettes with aerial perspective (every ramp pushed towards the pale blue-grey haze by
    its distance): low islands 3600 m (centre right), the far coast 2600 m (left, running out into the sea
    before the glitter column), a green headland with pale sea cliffs 1400 m (right, cliff end lit from the
    left), the harbour mole with rock armour and the red / white lighthouse 330 m (left), a tiny fishing
    boat on the horizon; soft rims on the top / left (sun-side) edges;
  * water: retro16 depth bands joined by horizontal dash dithering (silver-cyan horizon -> the play-area
    blue #2c7aa4 = waterTint -> deeper blue at the breakwater), the horizon strip mirrors the sky, EXACT
    mirrored reflections of every far layer (mirror pass, water-tinted, break lines, dissolve), sparse wave
    marks (thinned in the play area), a light mist on the far waterline, a faint silver sheen and sparse
    short glitter dashes in the far third only, under the glitter column (dx -150, open sea);
  * the reflections / waterline foam / ripples of the front objects (computed by the front pass).
Front layer (transparent, solid objects only): the concrete breakwater head the angler stands on (slab
joints, side kerbs, contact shadow), a cast-iron bollard, cooler box + bait bucket, two tetrapod mounds
hugging the lower-left / lower-right corners (wet algae line at the water), a red lateral buoy far right,
one perched gull. Key light from the left and high, pale warm rim on the OUTER left/top silhouette.

Same camera (fk_persp.setup_camera(standH = 3.0 from Data/stage_sea.json)), 640x400, same gameplay layout.
Outputs: _tmp/variants/hybrid/sea_back.png, sea_front.png, stage_sea.json
Scratch (EXR passes, overlay): _tmp/variants/hybrid/work/sea/
Run: blender -b --python variants/hybrid/hyb_sea.py [-- back|front] [--period dawn|day|evening|night [--dry]]   (hyb_period.py)
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
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

SID = "sea"
# preset overrides (stage-local, the kit is untouched):
#  * glitter far 0.3: the back water runs to row 400 at the glitter column, 0.45 would reach row ~229 (the middle
#    of the play area); 0.3 keeps it in the far third of the VISIBLE water (ends ~row 183, d ~33 m)
#  * water bands: band 4 (the play area, rows ~158-262) is exactly the preset waterTint #2c7aa4 that Unity uses
#    for the underwater tint; band 5 (at the breakwater) a notch deeper
PR = PER.use_preset("sea", glitter=dict(far=0.3),
                  water=dict(bands=["#9ec8d8", "#7cb4cc", "#5c9cbc", "#4284a8", "#2c7aa4", "#236894"]))
STAND = float(json.load(open(os.path.join(C.DATA, f"stage_{SID}.json"), encoding="utf-8"))["standH"])   # 3.0
W, H = P.W, P.H
G = R.grade_hex
SCR = PER.work(SID, os.path.join(R.OUT, "work", SID))   # period runs: work/periods/sea_<p>/
os.makedirs(SCR, exist_ok=True)
R.WORK = SCR            # EXR passes of this stage go to its own scratch dir (parallel Blender runs never collide)

# ------------------------------------------------------------------ geometry anchors of the mood
SUN_C, SUN_R = R.sun_rc(PR)                     # (90, 17.5): above the crop, glow only
GLIT_C = W / 2 + PR.glitter["dx"]               # 170: glitter column over OPEN sea
MOLE_TIP_C = 122.0                              # lighthouse column (left, clear of the glitter)
HEAD_C0 = 438.0                                 # right headland: cliff end column
COAST_C1 = 150.0                                # left far coast runs out into the sea here
BOAT_C = 236                                    # tiny fishing boat on the horizon
D_ISLE, D_COAST, D_HEAD, D_MOLE, D_BOAT = 3600.0, 2600.0, 1400.0, 330.0, 560.0

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)
MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)
WB = PR.water["bands"]                          # 6 bands, far (horizon) -> near (breakwater)
WSTEP = [PR.water["dark"]] + WB[::-1] + [PR.sky[0][1]]   # dark -> light ramp for +/- steps
REFL = PR.water["refl"]
FOAM = "#e2eef0"
ISLE = R.hazed(G(["#48647a", "#5e7c8c"]), D_ISLE, extra=-0.24)
COAST = R.hazed(G(["#34505e", "#46646a", "#62806e"]), D_COAST, extra=-0.1)
HEAD = R.hazed(G(["#203a46", "#2e4e4c", "#466a52", "#6c8a5e"]), D_HEAD, extra=-0.04)
CLIFF = R.hazed(G(["#5c5e62", "#8e8a80", "#bcb4a0"]), D_HEAD, extra=-0.04)
MOLE = R.hazed(G(["#48505a", "#7a7e7e", "#a6a69c", "#cecabc"]), D_MOLE)
LHW = R.hazed(G(["#8894a2", "#c4c8c6", "#f4f2e8"]), D_MOLE)
LHR = R.hazed(G(["#7a2a34", "#b83c3a", "#e0604a"]), D_MOLE)
BOATB = R.hazed(G(["#2e5a96"]), D_BOAT)
GLIT = PR.glitter["cols"]
BACK_PAL = (SKY_PAL + MSKY_PAL + WB + [PR.water["dark"]] + REFL + [FOAM] + ISLE + COAST + HEAD + CLIFF + MOLE
            + LHW + LHR + BOATB + GLIT)

CONC = G(["#4c525a", "#6a6e70", "#8c8c86", "#aeaa9c", "#d0cab6"])     # breakwater + tetrapod concrete
WET = G(["#28343a", "#3a4a4a", "#566660"])                            # wet line (dark concrete, a hint of algae)
IRON = G(["#2a323e", "#44505e", "#6a7882"])
COOLB = G(["#1e4474", "#2e62a0", "#4c88c4"])
COOLW = G(["#9eaab2", "#d4dcde", "#f4f6f0"])
RED = G(["#7c2830", "#b83c38", "#e25c46", "#f69c7c"])
LAMP = "#ffe890"
BEAK = "#e8b040"
FRONT_PAL = CONC + WET + IRON + COOLB + COOLW + RED + [LAMP, BEAK]

LB = Vector(PR.key_dir).normalized()          # one key light for the whole stage (sun high on the left)

# back-layer overlay codes produced by the front pass (applied relative to the local water band)
OV_DARK, OV_WM1, OV_RIPPLE, OV_FOAM, OV_FOAM2 = 1, 2, 3, 5, 6
_OV = {}


def rc(p):
    return R.rc(p, STAND)


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


def mpp_at(y):
    """metres per pixel at forward distance y (water level)."""
    return P.project((0.0, y, 0.0), STAND)[2] / P.F_PX


# ================================================================== local materials (kit has no 2-ramp helpers)
def _noise(nb, f, noise, nscale, ncoord="object", ndetail=1.0):
    no = nb.node("ShaderNodeTexNoise")
    nb.link(R._coord(nb, ncoord), no.inputs["Vector"])
    no.inputs["Scale"].default_value = nscale
    no.inputs["Detail"].default_value = ndetail
    return nb.math("MULTIPLY_ADD", nb.math("SUBTRACT", no.outputs["Fac"], 0.5), noise * 2, f)


def m_split(lo_cols, hi_cols, zline, b_lo, b_hi, bias=0.0, noise=0.0, nscale=2.0, ncoord="object", name="Split"):
    """Two palette ramps split at world height zline (below: lo_cols, e.g. the wet algae line / sea cliffs),
    both shaded by the stage key light."""
    key = ("split", tuple(lo_cols), tuple(hi_cols), zline, tuple(b_lo), tuple(b_hi), bias, noise, nscale, ncoord)
    m = R._cached(key)
    if m is not None:
        return m
    R._use(lo_cols + hi_cols)
    m = bpy.data.materials.new(name)
    nb = R.NB(m)
    f = nb.math("ADD", R._factor_lambert(nb, LB, 0.5), bias)
    if noise:
        f = _noise(nb, f, noise, nscale, ncoord)
    lo = R.band_ramp(nb, f, lo_cols, b_lo)
    hi = R.band_ramp(nb, f, hi_cols, b_hi)
    s = nb.sep(R._coord(nb, "world"))
    col = nb.mix(nb.math("LESS_THAN", s[2], zline), hi, lo)
    nb.output_emission(col, 1.0)
    R._mats[key] = m
    return m


def m_stripes(a_cols, b_cols, z0, period, b_a, b_b, name="Stripes"):
    """Horizontal paint bands (lighthouse): b_cols where fract((z - z0) / period) >= 0.5, key-lit."""
    key = ("stripes", tuple(a_cols), tuple(b_cols), z0, period, tuple(b_a), tuple(b_b))
    m = R._cached(key)
    if m is not None:
        return m
    R._use(a_cols + b_cols)
    m = bpy.data.materials.new(name)
    nb = R.NB(m)
    f = R._factor_lambert(nb, LB, 0.5)
    ca = R.band_ramp(nb, f, a_cols, b_a)
    cb = R.band_ramp(nb, f, b_cols, b_b)
    s = nb.sep(R._coord(nb, "world"))
    fr = nb.math("FRACT", nb.math("DIVIDE", nb.math("SUBTRACT", s[2], z0), period))
    col = nb.mix(nb.math("GREATER_THAN", fr, 0.5), ca, cb)
    nb.output_emission(col, 1.0)
    R._mats[key] = m
    return m


def m_holdout():
    """Water occluder for the FRONT pass: hides every part below z = 0 and renders transparent."""
    m = bpy.data.materials.new("Holdout")
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    nt.nodes.clear()
    ho = nt.nodes.new("ShaderNodeHoldout")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(ho.outputs[0], out.inputs["Surface"])
    return m


# ================================================================== BACK LAYER geometry
def land_mesh(name, rnd, y0, depth, c0, c1, prof, mat, kind, step_px=1.5, base=-1.5):
    """Ridge between screen columns c0..c1 at forward distance y0 (retro16 ridge grid: 7 rows in y, the
    crest at y0); prof(u) = crest height in SCREEN PIXELS for u = 0..1 along the ridge."""
    mpp = mpp_at(y0) / math.cos(math.radians(P.PITCH))
    n = max(4, int(abs(c1 - c0) / step_px))
    xs = [R.col_to_x(c0 + (c1 - c0) * i / n, y0, STAND) for i in range(n + 1)]
    hs = [max(0.0, prof(i / n)) * mpp for i in range(n + 1)]
    rows = [-1.0, -0.55, -0.2, 0.0, 0.3, 0.7, 1.0]
    verts, faces = [], []
    dx = (xs[1] - xs[0])
    for i, x in enumerate(xs):
        for j, v in enumerate(rows):
            fall = max(0.0, 1 - abs(v)) ** 1.35
            z = hs[i] * fall + (rnd.uniform(-0.05, 0.05) * hs[i] if 0 < abs(v) < 1 else 0.0)
            jx = rnd.uniform(-0.3, 0.3) * dx if 0 < abs(v) < 1 else 0.0
            verts.append((x + jx, y0 + v * depth * 0.5, max(base, z + base)))
    nr = len(rows)
    for i in range(len(xs) - 1):
        for j in range(nr - 1):
            a = i * nr + j
            b = (i + 1) * nr + j
            faces.append((a, b, b + 1, a + 1))
    ob = R.mesh_from(name, verts, faces, mat, smooth=False)
    R._fix_normals(ob)
    return tagk(ob, kind)


def back_scene(rnd):
    mt = R.m_tone
    # ---- low islands on the far horizon (centre right, very hazy)
    im = mt(ISLE, [0.62], light=LB, name="Isle")
    land_mesh("Isle", rnd, D_ISLE, 400, 284, 320, lambda u: 4.6 * math.sin(math.pi * u) ** 0.6
              + 0.8 * math.sin(u * 17.0), im, "isle", step_px=2.0)
    land_mesh("Isle", rnd, D_ISLE + 300, 300, 327, 345, lambda u: 2.6 * math.sin(math.pi * u) ** 0.7, im, "isle",
              step_px=2.0)
    # ---- far coast on the left (2600 m): low hills that run out into the sea before the glitter column
    cm = mt(COAST, [0.45, 0.74], light=LB, noise=0.1, nscale=0.02, ncoord="world", name="Coast")

    def coast(u):
        c = -30 + (COAST_C1 + 30) * u
        end = min(1.0, max(0.0, (COAST_C1 - c) / 40.0))
        return (6.8 + 2.2 * math.sin(c / 21.0) + 1.0 * math.sin(c / 7.3 + 2.0)) * end ** 0.8
    land_mesh("Coast", rnd, D_COAST, 700, -30, COAST_C1, coast, cm, "coast", step_px=1.5)
    # ---- the headland on the right (1400 m): green top, pale sea cliffs at the base, steep cliff end on
    #      the left (faces the key light)
    hm = m_split(CLIFF, HEAD, 8.0, [0.46, 0.72], [0.36, 0.56, 0.78], noise=0.12, nscale=0.035, ncoord="world",
                 name="Head")

    def head(u):
        c = HEAD_C0 + (700 - HEAD_C0) * u
        rise = min(1.0, max(0.0, (c - HEAD_C0) / 10.0))
        hgt = 15.5 + 2.6 * math.sin(c / 26.0 + 1.0) + 1.3 * math.sin(c / 8.5) + (c - HEAD_C0) * 0.018
        return hgt * rise ** 0.55
    land_mesh("Head", rnd, D_HEAD, 260, HEAD_C0 - 2, 700, head, hm, "head", step_px=1.2)
    # ---- harbour mole (330 m) with rock armour, the lighthouse on its tip
    molem = mt(MOLE, [0.42, 0.62, 0.82], light=LB, name="Mole")
    x0 = R.col_to_x(-60, D_MOLE, STAND)
    x1 = R.col_to_x(MOLE_TIP_C - 1, D_MOLE, STAND)
    tagk(R.box("Mole", ((x0 + x1) / 2, D_MOLE, 1.0), (x1 - x0, 8.0, 6.0), molem), "mole", "mole")
    tagk(R.box("Parapet", ((x0 + x1) / 2 - 3, D_MOLE + 3.2, 4.6), (x1 - x0 - 6, 1.6, 1.4), molem), "mole", "mole")
    armm = mt(MOLE[:3], [0.44, 0.7], light=LB, name="Armour")
    x = x0
    while x < x1 + 5.5:
        r = rnd.uniform(1.1, 1.7)
        yy = D_MOLE - 4.6 + rnd.uniform(-0.6, 0.6) if x < x1 - 1 else D_MOLE + rnd.uniform(-4.0, 3.5)
        ob = C.add_prim("ico", "Armour", armm, radius=1.0, location=(0, 0, 0), subdivisions=1)
        ob.matrix_world = Matrix.Translation((x, yy, rnd.uniform(-0.2, 0.9))) @ Matrix.Diagonal((r * 1.2, r, r * 0.9, 1))
        tagk(ob, "mole", "armour")
        x += rnd.uniform(1.3, 2.2)
    lx = x1 + 1.2
    tagk(R.loft("LHBase", [(lx, D_MOLE, -1.0), (lx, D_MOLE, 4.4)], 3.0, molem, 16), "lh", "lh")
    lhm = m_stripes(LHW, LHR, 4.4, 3.4, [0.4, 0.7], [0.45, 0.72], name="LHouse")
    tagk(R.loft("Tower", [(lx, D_MOLE, 4.4), (lx, D_MOLE, 16.9)], [2.05, 1.6], lhm, 16), "lh", "lh")
    redm = mt(LHR, [0.45, 0.72], light=LB, name="LHRed")
    tagk(R.loft("Gallery", [(lx, D_MOLE, 16.8), (lx, D_MOLE, 17.5)], 2.3, redm, 16), "lh", "lh")
    tagk(R.loft("Lantern", [(lx, D_MOLE, 17.5), (lx, D_MOLE, 19.3)], 1.25, R.m_flat(MOLE[0]), 12), "lh", "lh")
    tagk(R.loft("Cap", [(lx, D_MOLE, 19.2), (lx, D_MOLE, 20.3), (lx, D_MOLE, 21.3)], [1.65, 0.95, 0.12], redm, 12),
         "lh", "lh")
    tagk(R.hpoly("Water", [(-9000, -40), (9000, -40), (9000, 5200), (-9000, 5200)], 0.0, R.m_flat(WB[3]), 0.01), "water")


# ================================================================== FRONT LAYER geometry
# tetrapods (x, y, z of the hub, scale): two mounds hugging the lower corners, highest next to the breakwater,
# falling towards the open water; the central corridor in front of the angler stays free
# The breakwater armour runs out diagonally on both sides of the tip: its waterline goes from next to the
# pier (x ~ +-4.5, y ~ 5) to the crop edge (x ~ +-10, y ~ 13), so the mounds fill the lower-left / lower-right
# corners (screen cols ~80-200 / ~440-560, rows ~255-335) and the middle ~60 % of the water stays open.
_TL = [(-3.3, 0.6, 2.3, 0.9), (-3.7, 2.6, 1.9, 0.9),                                          # against the pier
       (-5.0, 1.6, 2.1, 0.9), (-5.3, 3.8, 1.6, 0.9), (-6.0, 6.0, 1.1, 0.88),                   # 2nd row
       (-6.9, 3.4, 1.9, 0.9), (-7.3, 6.1, 1.45, 0.88), (-7.8, 8.5, 0.95, 0.86), (-8.4, 10.8, 0.5, 0.85),
       (-9.1, 5.8, 1.9, 0.9), (-9.6, 8.6, 1.45, 0.88), (-10.3, 11.2, 1.0, 0.85), (-10.9, 13.6, 0.5, 0.82),
       (-4.5, 5.2, 0.5, 0.85), (-5.7, 7.8, 0.45, 0.85), (-6.9, 10.3, 0.35, 0.82), (-8.4, 12.9, 0.3, 0.8)]
_TR = [(3.4, 0.4, 2.3, 0.9), (3.8, 2.4, 1.85, 0.9),
       (5.1, 1.4, 2.1, 0.9), (5.4, 3.6, 1.55, 0.9), (6.2, 5.9, 1.05, 0.88),
       (7.0, 3.1, 1.95, 0.9), (7.5, 5.8, 1.4, 0.88), (8.0, 8.2, 0.9, 0.86), (8.7, 10.5, 0.45, 0.85),
       (9.3, 5.5, 1.85, 0.9), (9.8, 8.3, 1.4, 0.88), (10.5, 10.9, 0.95, 0.85), (11.2, 13.3, 0.45, 0.82),
       (4.7, 5.0, 0.5, 0.85), (5.9, 7.5, 0.4, 0.85), (7.1, 10.0, 0.35, 0.82), (8.8, 12.6, 0.3, 0.8)]
TETS = _TL + _TR
TET_DIRS = [(0.0, 0.0, 1.0), (0.943, 0.0, -0.333), (-0.471, 0.816, -0.333), (-0.471, -0.816, -0.333)]
BUOY = (17.0, 44.0)
BOLLARD = (-1.85, 0.5)


def tetrapod(k, c, s, rot, mat):
    """Tetrapod: hub + 4 truncated-cone legs to the tetrahedron directions (one leg up when upright)."""
    g = f"tet{k}"
    objs = []
    tips = []
    for d in TET_DIRS:
        dv = rot @ Vector(d)
        a = Vector(c)
        b = a + dv * (1.05 * s)
        objs.append(tagk(R.loft("Leg", [a, a + dv * 0.5 * s, b], [0.34 * s, 0.27 * s, 0.2 * s], mat, 12), "tet", g))
        tips.append(b)
    objs.append(tagk(R.ellipsoid("Hub", c, (0.38 * s, 0.38 * s, 0.38 * s), mat, 14, 8), "tet", g))
    return objs, tips


TET_TIPS = []


def front_scene(rnd):
    mt = R.m_tone
    TET_TIPS.clear()
    # ---- water occluder: nothing below z = 0 shows in the front layer
    tagk(R.hpoly("Hold", [(-60, -30), (60, -30), (60, 120), (-60, 120)], 0.0, m_holdout(), 0.004), "hold")
    # ---- breakwater head: concrete slabs (joints across / along), side kerbs; top = the angler's feet level
    deck = mt(CONC[1:], [0.3, 0.775, 0.97], light=LB, bias=-0.1, noise=0.22, nscale=0.45, ncoord="world", ndetail=1.0,
              pats=((1, 3.2, 0.013, -0.34, "world", 0.35), (0, 2.5, 0.017, -0.34, "world", 1.27)), name="Deck")
    tagk(R.box("Deck", (0, -9.5, (STAND - 0.6) / 2), (5.0, 21.0, STAND + 0.6), deck), "deck", "deck")
    kerb = mt(CONC[1:], [0.3, 0.55, 0.8], light=LB, name="Kerb")
    for sx in (-2.39, 2.39):
        tagk(R.box("Kerb", (sx, -9.6, STAND + 0.07), (0.22, 20.8, 0.14), kerb), "deck", f"kerb{sx}")
    # ---- cast-iron bollard (mushroom head)
    iron = mt(IRON, [0.4, 0.68], light=LB, name="Iron")
    bx, by = BOLLARD
    tagk(R.loft("Bollard", [(bx, by, STAND - 0.05), (bx, by, STAND + 0.12), (bx, by, STAND + 0.34),
                            (bx, by, STAND + 0.37)], [0.2, 0.15, 0.14, 0.22], iron, 16), "prop", "bollard")
    tagk(R.ellipsoid("BCap", (bx, by, STAND + 0.39), (0.22, 0.22, 0.07), iron, 16, 8), "prop", "bollard")
    # ---- cooler box (blue body, white lid) + bait bucket
    cb = mt(COOLB, [0.42, 0.72], light=LB, name="Cooler")
    cw = mt(COOLW, [0.4, 0.7], light=LB, name="Lid")
    cx, cy = -1.05, -0.3
    tagk(R.box("Cooler", (cx, cy, STAND + 0.18), (0.62, 0.4, 0.36), cb, bevel=0.03), "prop", "cooler")
    tagk(R.box("CLid", (cx, cy, STAND + 0.39), (0.64, 0.42, 0.07), cw, bevel=0.02), "prop", "cooler")
    tagk(R.loft("CHandle", [(cx - 0.18, cy, STAND + 0.42), (cx - 0.14, cy, STAND + 0.47), (cx + 0.14, cy, STAND + 0.47),
                            (cx + 0.18, cy, STAND + 0.42)], 0.018, R.m_flat(COOLW[0]), 6), "prop", "coolerh")
    red = mt(RED, [0.3, 0.55, 0.8], light=LB, name="Red")
    ux, uy = 1.25, -0.25
    tagk(R.loft("Bucket", [(ux, uy, STAND), (ux, uy, STAND + 0.34)], [0.15, 0.185], red, 16), "prop", "bucket")
    tagk(R.hpoly("BWater", R.ellipse_pts(ux, uy, 0.165, 0.165, 16), STAND + 0.33, R.m_flat(COOLB[1]), 0.005), "prop", "bucketw")
    tagk(R.loft("Bail", [(ux - 0.19, uy, STAND + 0.32), (ux - 0.11, uy - 0.04, STAND + 0.5), (ux, uy - 0.06, STAND + 0.55),
                         (ux + 0.11, uy - 0.04, STAND + 0.5), (ux + 0.19, uy, STAND + 0.32)], 0.011, R.m_flat(IRON[1]), 6),
         "prop", "bail")
    # ---- tetrapod mounds (wet algae line at the water, dry weathered concrete above)
    tm = m_split(WET, CONC, 0.2, [0.4, 0.66], [0.3, 0.46, 0.62, 0.8], noise=0.08, nscale=1.6, name="Tet")
    refl = []
    for k, (x, y, z, s) in enumerate(TETS):
        yaw = rnd.uniform(0, 2 * math.pi)
        tumble = rnd.random() < 0.2                  # most units sit upright (the readable tetrapod silhouette)
        tilt = rnd.uniform(-1.1, 1.1) if tumble else rnd.uniform(-0.3, 0.3)
        tilt2 = rnd.uniform(-0.3, 0.3)
        rot = Matrix.Rotation(yaw, 3, "Z") @ Matrix.Rotation(tilt, 3, "X") @ Matrix.Rotation(tilt2, 3, "Y")
        objs, tips = tetrapod(k, (x, y, z), s, rot, tm)
        refl += objs
        TET_TIPS.append(tips)
    # ---- red lateral buoy (far right): float, cage body, top mark, lamp. A floating thing: its own tones stay
    #      LIGHTER than the water (RED[2:]); only its 1 px outline uses the darker reds
    bx, by = BUOY
    bred = mt(RED[2:], [0.5], light=LB, name="BuoyRed")
    buoy = []
    buoy.append(tagk(R.loft("Float", [(bx, by, -0.3), (bx, by, 0.28), (bx, by, 0.36)], [0.78, 0.78, 0.55], bred, 16), "buoy", "buoy"))
    buoy.append(tagk(R.loft("Body", [(bx, by, 0.34), (bx, by, 1.5)], [0.46, 0.24], bred, 12), "buoy", "buoy"))
    buoy.append(tagk(R.loft("Band", [(bx, by, 0.85), (bx, by, 1.08)], [0.4, 0.36], mt(COOLW[1:], [0.5], light=LB, name="Band"), 12),
                     "buoy", "buoy"))
    buoy.append(tagk(R.loft("Mark", [(bx, by, 1.5), (bx, by, 1.95)], 0.25, bred, 12), "buoy", "buoy"))
    buoy.append(tagk(R.ellipsoid("Lamp", (bx, by, 2.08), (0.12, 0.12, 0.14), R.m_flat(LAMP), 10, 6), "buoy", "buoy"))
    return refl + buoy


# ================================================================== back composite
# water depth zones (start row, end row) for WB[i] -> WB[i+1]; the play area (rows ~158-262) is WB[4] = waterTint
WATER_Z = [(94, 99), (102, 110), (116, 128), (140, 158), (262, 292)]
ROW_H, ROW_P = 92, 335             # horizon-side / breakwater-side reference rows for the dash length


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
    """EXACT reflection of every far layer (mirror pass): the layers' own palette colours tinted towards the
    far water and darkened; runs >= 3 px; a lighter break line every third row; the sea is choppier than the
    lake, so the image dissolves over the bottom 65 % of its height."""
    mp = R.mirror_pass(refl_objs, tag="sea_farmirror")
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
        p = np.clip((frac[r] - 0.15) / 0.6, 0, 1) ** 0.8
        brk = R.run_noise(rng, W, 3, 8) < p
        keep = m & ~brk & water[r]
        out[r, keep] = lab[r, keep] - 1
        keepm[r] = keep
        if (r - int(np.median(top))) % 2 == 1:
            linem[r] = R.dash_mask(W, 0.5, 4, 14, rng) & keep
    body = keepm & ~linem
    out2, pal = R.recolour(out, pal, body, reflect_tint(WB[1], 0.52, 0.84), snap=0.035)
    out2, pal = R.recolour(out2, pal, linem, reflect_tint(WB[1], 0.74, 0.95), snap=0.035)
    return out2, pal, keepm


def horizon_sky(pal, idx, water, wband):
    """Open sea: the strip just below the horizon mirrors the sky exactly (hazy white bands), tinted towards the
    far water, handed over to the bands by dash dithering; retro16 light break lines every third row."""
    rng = random.Random(21)
    rows = np.arange(H)[:, None]
    top = R.HORIZON_ROW
    zone = water & (rows < top + 13)
    thr = R.dash_threshold(H, W, rng, 2, 7)
    m_idx, _ = R.quantize(MSKY_RGB, zone, pal, dither=True, thr=thr)
    fade = np.clip((rows - (top + 4)) / 8.0, 0, 1)
    use = zone & (R.dash_threshold(H, W, rng, 3, 9) >= fade)
    out = idx.copy()
    out[use] = m_idx[use]
    out, pal = R.recolour(out, pal, use, reflect_tint(WB[0], 0.3, 0.92), snap=0.02)
    for r in range(int(top), int(top) + 13):
        if r % 3 == 1:
            bl = R.dash_mask(W, 0.3, 5, 16, rng) & use[r]
            out[r, bl] = wband[r, bl]
    return out, pal, use


def wave_marks(pal, idx, water, avoid, wband):
    """Sparse wave marks: a light dash (water +1) with a darker dash (water -1) one row below, 1 px right.
    A little more chop than the lake at the sides / far away; cut hard in the central play rectangle."""
    rng = random.Random(11)
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    dn1 = R.ramp_step(pal, WSTEP, wband, -1)
    ok = water & ~avoid
    for k in range(560):
        y = 3.0 + 700 * rng.random() ** 2.0
        x = rng.uniform(-1, 1) * (y + 14) * 0.85
        c, r = rc((x, y, 0))
        if r <= R.HORIZON_ROW + 3 or r >= H - 1 or c < 0 or c >= W:
            continue
        ri, ci = int(r), int(c)
        if avoid[ri, ci]:
            continue
        central = 176 < c < 464 and 140 < r < 335
        if central and rng.random() < 0.82:
            continue
        if y > 110 and rng.random() < 0.55:          # far water: the band dashes already carry the texture
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = min(16, max(2, int(round(560 / depth * rng.uniform(0.25, 0.6)))))
        draw_dash(idx, r, c, n, int(up1[ri, ci]), ok)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), int(dn1[min(H - 1, ri + 1), ci]), ok)
    return idx


def far_rims(idx, pal, kid):
    """Pale warm rim on the top / left (sun-side) outer edges of the far silhouettes: the high afternoon sun
    lights the crest lines and the left-facing cliffs, fading with the haze."""
    rim = PR.rim
    out, p2 = idx, pal
    # period looks scale the three rim groups (headland, coast + islands, mole + lighthouse): at night the hazed far
    # layers are nearly the sky colour, a full-strength rim would leave only their outlines (periods/sea.py CONSTS)
    ks = PER.const("FAR_RIM_K", (1.0, 1.0, 1.0))
    for (kinds, k), kk in zip(((("head",), 0.4), (("coast", "isle"), 0.25), (("mole", "lh"), 0.35)), ks):
        k = k * kk
        if k <= 0:
            continue
        m = np.isin(kid, kinds)
        top = m & ~R.shift(m, 1, 0, False)
        left = m & ~R.shift(m, 0, 1, False)
        out, p2 = R.blend_idx(out, p2, top | left, rim["col"], k, lighter=True, snap=0.03)
    return out, p2


BOAT_STAMP = ["......M....",
              "......M....",
              "...CCCwM...",
              "...CCCww...",
              "HHHHHHHHHHh",
              ".hhhhhhhhh."]


def stamp_boat(idx, pal, water):
    """Tiny fishing boat on the horizon (hand-drawn: it is only 11 px long) + a broken 3-row reflection."""
    cmap = {"M": MOLE[0], "C": BOATB[0], "w": LHW[1], "H": LHW[2], "h": LHW[0]}
    wr = int(round(R.water_row(D_BOAT, STAND)))
    y0 = wr - len(BOAT_STAMP) + 1
    for j, ln in enumerate(BOAT_STAMP):
        for i, ch in enumerate(ln):
            if ch in cmap:
                idx[y0 + j, BOAT_C + i] = pal.index(cmap[ch])
    rm = np.zeros(idx.shape, bool)
    for j, (ln, keep) in enumerate(((BOAT_STAMP[5], "hhhhhhhhh"), (BOAT_STAMP[4], "HHHH.HHHH"), (BOAT_STAMP[3], "CCC.w"))):
        r = wr + 1 + j
        k = 0
        for i, ch in enumerate(ln):
            if ch in cmap and water[r, BOAT_C + i]:
                if k < len(keep) and keep[k] != ".":
                    idx[r, BOAT_C + i] = pal.index(cmap[ch])
                    rm[r, BOAT_C + i] = True
                k += 1
    idx, pal = R.recolour(idx, pal, rm, reflect_tint(WB[1], 0.35, 0.8), snap=0.035)
    return idx, pal, rm


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    PER.hook("back_scene", rnd=rnd, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("sea_back")
    kid = R.kind_map(ps)
    sky = ~ps["a"]
    water = kid == "water"
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = SKY_RGB[sky]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid | sky, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=3.0, rel=0.015, steps=1)
    idx = np.where(np.isin(kid, ["mole", "lh"]), lined, idx)
    idx, pal = far_rims(idx, pal, kid)
    # ---- water: depth bands with horizontal dash dithering
    wband = R.water_bands(pal, WB, WATER_Z, random.Random(5), ROW_H, ROW_P)
    idx[water] = wband[water]
    idx, pal, hsky = horizon_sky(pal, idx, water, wband)
    # ---- exact reflections of the far layers
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") in ("isle", "coast", "head", "mole", "lh")]
    idx, pal, farm = far_reflection(pal, idx, water, refl_objs)
    idx, pal, boatm = stamp_boat(idx, pal, water)
    # ---- front-object reflections / foam / ripples (computed by the front pass)
    ov = _OV.get("ov")
    if ov is None:
        p = os.path.join(SCR, "front_overlay.npy")
        ov = np.load(p) if os.path.exists(p) else np.zeros((H, W), np.int8)
    g_am, g_br = R.glitter_masks(PR, water, random.Random(17), avoid=(ov > 0) | farm)
    idx = wave_marks(pal, idx, water, farm | boatm | (ov > 0) | g_am | g_br, wband)
    idx[water & (ov == OV_DARK)] = pal.index(REFL[0])
    for code, d in ((OV_WM1, -1), (OV_RIPPLE, +1), (OV_FOAM2, +2)):
        m = water & (ov == code)
        idx[m] = R.ramp_step(pal, WSTEP, wband, d)[m]
    idx[water & (ov == OV_FOAM)] = pal.index(FOAM)
    # ---- sun path: a faint silver sheen + sparse short glitter dashes, far third only (glitter column)
    rows = np.arange(H)[:, None]
    gc = int(GLIT_C)
    wc = np.flatnonzero(water[:, gc])
    r0 = wc.min()
    r_end = r0 + PR.glitter["far"] * (wc.max() - r0)
    t = np.clip((rows - r0) / (r_end - r0), 0, 1)
    halfw = PR.glitter["width"][0] + (PR.glitter["width"][1] - PR.glitter["width"][0]) * t
    cols = np.arange(W)[None, :]
    sheen = 0.7 * (1 - t) ** 1.5 * np.exp(-((cols - GLIT_C) / (1.7 * halfw)) ** 2) * (rows < r_end)
    sm = water & (sheen > R.dash_threshold(H, W, random.Random(23), 2, 6)) & (ov == 0) & ~farm
    idx, pal = R.blend_idx(idx, pal, sm, GLIT[0], 0.3, lighter=True, snap=0.03)
    idx[g_am] = pal.index(GLIT[0])
    idx[g_br] = pal.index(GLIT[1])
    # ---- light mist band on the far waterline (dash dithered, wisps)
    land = ~sky & ~water
    line = np.array([np.flatnonzero(water[:, c]).min() if water[:, c].any() else H for c in range(W)], float)
    ma = R.mist_amount(PR, line, land, water)
    thr = R.dash_threshold(H, W, random.Random(29), 2, 9)
    idx, pal = R.blend_idx(idx, pal, (ma > thr) & (ma > 0.08), PR.mist["col"], 0.5, snap=0.03)
    idx, pal = PER.hook("back_post", idx, pal, kid=kid, water=water, sky=sky, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "back")
    # readability: the rendered play-area water vs the waterTint Unity uses for the underwater tint
    play = water & (rows > 160) & (rows < 330) & (cols > 176) & (cols < 464) & (ov == 0)
    vals, cnt = np.unique(idx[play], return_counts=True)
    main = pal.hex[int(vals[np.argmax(cnt)])]
    print("HYB back colours", R.count_colours(img), "palette", len(pal), "play-area water", main,
          "L", round(R.lum(main), 3), "share", round(cnt.max() / cnt.sum(), 2), "| waterTint", PR.water["tint"],
          "L", round(R.lum(PR.water["tint"]), 3), "| glitter rows", int(r0), int(r_end))
    return pal


# ---------------------------------------------------------------- front post helpers
GULL_STAMP = ["..WW......",
              ".WWWW.....",
              "YWWWW.....",
              "..WWWGGGG.",
              "..WWGGGGGD",
              "...WWWGGDD",
              "....L..L.."]


def stamp_gull(idx, pal, kid):
    """One herring gull perched on the highest up-pointing leg of the right mound that is visible in the game
    crop and stands against open water (hand-drawn, faces the light)."""
    best = None
    x0c, y0c, wc, hc = R.CROP
    for k, (x, y, z, s) in enumerate(TETS):
        if x < 0:
            continue
        top = max(TET_TIPS[k], key=lambda v: v.z)
        c, r = rc(tuple(top))
        if not (x0c + 20 < c < x0c + wc - 20 and r > y0c + 10) or top.z - z < 0.8 * s:
            continue
        ri, ci = int(round(r)), int(round(c))
        if kid[ri, ci] != "tet" or (idx[ri - 9:ri - 1, ci - 4:ci + 5] >= 0).sum() > 4:
            continue                                  # tip hidden, or no open space above it
        if best is None or r < best[1]:
            best = (c, r)
    if best is None:
        return None
    c, r = best
    cmap = {"W": COOLW[2], "G": COOLW[0], "D": IRON[1], "Y": BEAK, "L": BEAK}
    x0, y0 = int(round(c)) - 4, int(round(r)) - len(GULL_STAMP) + 1
    for j, ln in enumerate(GULL_STAMP):
        for i, ch in enumerate(ln):
            if ch in cmap and 0 <= y0 + j < H and 0 <= x0 + i < W:
                idx[y0 + j, x0 + i] = pal.index(cmap[ch])
    return (x0, y0)


def contact_shadow(idx, pal, kid):
    """Soft cast shadow of the angler on the slab: the key is high on the left, so it leans right."""
    fc, fr = rc((0, 0, STAND))
    cx, cy = int(round(fc + 3)), int(fr) + 1
    conc = [pal.index(c) for c in CONC[2:]]
    for dy, a, b in ((-1, -3, 9), (0, -6, 13), (1, -3, 9)):
        for dx in range(a, b):
            y, x = cy + dy, cx + dx
            if 0 <= y < H and 0 <= x < W and idx[y, x] in conc and kid[y, x] == "deck":
                idx[y, x] = pal.darker(int(idx[y, x]), 1)


def front_reflections(refl):
    """Reflections of every front object standing in water -> back-layer overlay codes.
    2 tones (dark wet base / water -1), per-row +-1 px shift only for runs >= 4 px, dash breaks every
    2nd/3rd row, fade over 50 % of the reflected height (choppy water)."""
    mp = R.mirror_pass(refl, ids=True, tag="sea_frontmirror")
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
    p = np.clip(frac / 0.5, 0, 1) ** 1.2
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


def waterline_foam(ov, ps, kid, a):
    """Breaking-wave foam where the tetrapods / buoy meet the water: a white core line right under the
    waterline pixels (below their 1 px outline), a lacy fringe (water +2) below it, and loose foam dashes on
    the water around the lowest units. Only where the final front layer `a` is transparent."""
    rng = random.Random(41)
    z = R.world_z(ps["depth"], STAND)
    below_open = ~R.shift(ps["a"], -1, 0, True)
    wl = np.isin(kid, ["tet", "buoy"]) & (z < 0.3) & below_open
    core = R.shift(wl, 2, 0, False)
    core = core | R.shift(core, 0, 1, False) | R.shift(core, 0, -1, False) | R.shift(core, 0, 2, False) | R.shift(core, 0, -2, False)
    core2 = R.shift(core, 1, 1, False) & np.stack([R.dash_mask(W, 0.5, 2, 6, rng) for _ in range(H)])
    fr1 = R.shift(wl, 3, 0, False) | R.shift(wl, 3, 2, False) | R.shift(wl, 3, -2, False)
    fr2 = R.shift(wl, 4, 1, False) | R.shift(wl, 5, -2, False)
    dm = np.stack([R.dash_mask(W, 0.55, 2, 6, rng) for _ in range(H)])
    ov[fr2 & dm & ~a & (ov == 0)] = OV_FOAM2
    ov[fr1 & ~a] = OV_FOAM2
    ov[core & ~a & (np.stack([R.dash_mask(W, 0.9, 3, 10, rng) for _ in range(H)]))] = OV_FOAM
    ov[core2 & ~a] = OV_FOAM
    # loose foam around the low units (on the water plane, in front of / beside them)
    for (x, y, zz, s) in TETS:
        if zz > 1.5:
            continue
        for k in range(14):
            th = rng.uniform(-0.25 * math.pi, 1.25 * math.pi)
            rr = rng.uniform(1.0, 2.1) * s
            c, r = rc((x + rr * math.cos(th), y + rr * math.sin(th) * 0.8, 0.0))
            ri, ci = int(r), int(c)
            if not (0 <= ri < H and 0 <= ci < W) or a[ri, ci]:
                continue
            n = rng.randint(2, 5)
            v = OV_FOAM if rng.random() < 0.3 else OV_FOAM2
            for cc in range(ci, min(W, ci + n)):
                if not a[ri, cc]:
                    ov[ri, cc] = v
    return ov


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    refl = front_scene(rnd)
    PER.hook("front_scene", rnd=rnd, refl=refl, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes("sea_front")
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    idx = R.despeckle(idx, ps["id"], protect=~solid, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    contact_shadow(idx, pal, kid)
    gpos = stamp_gull(idx, pal, kid)
    # pale warm rim on the OUTER sun-side (left / top) silhouette of the solid props, before the outline
    solid_k = np.isin(kid, ["tet", "deck", "prop", "buoy"])
    idx, pal = R.rim_light(idx, pal, PR, mask=solid_k)
    # selective outline (hue-shifted darker neighbour, never ink); light from the left
    ol = R.outer_outline(idx, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    idx = ol
    # ---- back-layer overlays: reflections, waterline foam, ripple dashes at the buoy
    ov = front_reflections(refl)
    a_now = idx >= 0
    ov = waterline_foam(ov, ps, kid, a_now)
    rng = random.Random(5)
    bx, by = BUOY
    c, r = rc((bx, by, 0))
    depth = P.project((bx, by, 0), STAND)[2]
    half = 0.8 * P.F_PX / depth
    for dr, sc in ((0, 1.0), (1, 0.7), (2, 1.3), (3, 1.0)):
        rr = int(r + dr)
        for side in (-1, 1):
            n = rng.randint(2, 4)
            c0 = c + side * (half * sc + rng.uniform(1, 3)) - (n if side < 0 else 0)
            for cc in range(int(c0), int(c0 + n)):
                if 0 <= cc < W and 0 <= rr < H and not a_now[rr, cc]:
                    ov[rr, cc] = OV_RIPPLE
    _OV["ov"] = ov
    np.save(os.path.join(SCR, "front_overlay.npy"), ov)
    idx, pal = PER.hook("front_post", idx, pal, kid=kid, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "front")
    # floating prop check: the buoy's own pixels (outline excluded) vs the local water band (WB[4] at its row)
    bm = (kid == "buoy") & (idx >= 0)
    fl = [R.lum(pal.hex[int(i)]) for i in np.unique(idx[bm])]
    print("HYB front colours", R.count_colours(img), "palette", len(pal), "buoy L min", round(min(fl), 3),
          "vs local water L", round(R.lum(WB[4]), 3), "gull at", gpos)
    x0c, y0c, wc, hc = R.CROP
    vis = sum(1 for (x, y, z, s) in TETS if x0c - 10 < rc((x, y, z))[0] < x0c + wc + 10 and rc((x, y, z + s))[1] < y0c + hc)
    print("HYB tetrapods", len(TETS), "visible in the crop", vis)


def main():
    C.reset_scene()
    which = PER.which()                      # back / front (+ --period <p> [--dry], see hyb_period)
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    PER.stage_json(SID, PR, clouds=False, birds=True)
    print("HYB SEA done")


if __name__ == "__main__":
    main()
