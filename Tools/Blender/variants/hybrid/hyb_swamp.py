"""
hybrid - the swamp stage: the old fk_stages.stage_swamp() layout (boardwalk end with a lantern post, cypress
trees with hanging moss standing in the water on both sides, reeds, pads, a floating log, a close mossy far
shore at ~62 m behind which ghostly tree rows sink into fog) built in the hybrid craft with the "swamp"
preset mood (foggy dusk: no sun disc, a dull amber glow behind the fog left of centre, olive haze that eats
everything past ~150 m, heavy mist, no glitter).

Back layer (opaque):
  * banded sky + the flat dithered glow rings (preset); four silhouettes by distance, each ramp pushed
    towards the olive haze: fog forest 460 m (almost gone) -> ghost cypress row 175 m -> a sparse darker row
    at ~100 m + two dead snags standing in the open channel -> the far shore 62-72 m (bald cypress with
    fluted flared bases, flat foliage tiers, Spanish moss, knees, dead snags, a mud bank);
  * the far shore opens into a channel under the glow (GAP_C) and the far rows dip into a valley there, so
    the glow column sits over open water; the fog in front of the far rows catches the glow (fog_glow), the
    shore trees get a faint glow-side rim near it, their bases sink into ground fog;
  * water: murky olive depth bands joined by dash dithering (play area = waterTint #3a4a30), the channel
    water mirrors the sky + glow, EXACT (mirror pass) reflections of every far layer, murky: tinted strongly
    towards the water and dissolving early, sparse wave marks, a heavy dash-dithered mist band on the far
    waterline + low fog wisps over the far water; no glitter.
  * the reflections / ripples / pad lines of the front objects (computed by the front pass).
Front layer (transparent): the old boardwalk end (weathered planks, two broken ones, header beam, crooked
posts), the lantern on the right end post (warm glass, lights the post and the planks next to it), 5 bald
cypress + 1 dead snag in the water hugging the sides (knees, fluted bases, moss), reed clumps, hand-shaded
lily pads with white lilies, duckweed specks (all floating things lighter than the water), a mossy log.

Camera fk_persp.setup_camera(0.9) (standH of Data/stage_swamp.json), 640x400; the middle ~60 % of the water
stays open. Scratch files go to _tmp/variants/hybrid/work/swamp (parallel stage builds never collide).
Outputs: _tmp/variants/hybrid/swamp_back.png, swamp_front.png, stage_swamp.json
Run: blender -b --python variants/hybrid/hyb_swamp.py [-- back|front] [--period dawn|day|evening|night [--dry]]   (hyb_period.py)
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
import numpy as np  # noqa: E402
from mathutils import Vector, Matrix  # noqa: E402
import fk_common as C  # noqa: E402
import fk_persp as P  # noqa: E402

SID = "swamp"
# own scratch dir: render_exr() reads hyb_core.WORK at call time, so EXR passes of this process never share a
# file with the other stage builds running in parallel (tags are prefixed with the stage id as well)
R.WORK = PER.work(SID, os.path.join(R.OUT, "work", SID))   # period runs: work/periods/swamp_<p>/
os.makedirs(R.WORK, exist_ok=True)

# play-area band == waterTint: the preset's bands[4] (#303e30) was darker than its own tint (#3a4a30); the
# bands are lifted so the water the game tints / splashes with (#3a4a30) is the water rendered in the play area
PR = PER.use_preset(SID, water=dict(bands=["#8a8466", "#6e7058", "#58624a", "#48573e", "#3a4a30", "#34442c"],
                                  dark="#2c3a26", refl=["#26321f", "#2e3b27"]))
STAND = json.load(open(os.path.join(C.DATA, f"stage_{SID}.json"), encoding="utf-8"))["standH"]    # 0.9
W, H = P.W, P.H
G = R.grade_hex

# ------------------------------------------------------------------ geometry anchors of the mood
GLOW_C, GLOW_R = R.sun_rc(PER.native_pr())     # glow centre (280, 85.5): no disc, amber behind the fog
# (geometry anchor = the NATIVE glow, the same valley in every period; the rims follow this period's light)
LIGHT_C = R.sun_rc(PR)[0]
GAP_C = (240.0, 326.0)                          # open channel in the far shore under the glow (screen columns)
VAL_C = (244.0, 318.0)                          # valley of the far rows under the glow
D_A, D_B, D_C2, D_SHORE = 460.0, 175.0, 100.0, 62.0

# ------------------------------------------------------------------ curated palettes
SKY_RGB, SKY_PAL = R.sky_rgb(PR)
MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)
WB = PR.water["bands"]                          # 6 bands, far (horizon) -> near (boardwalk)
WSTEP = [PR.water["dark"]] + WB[::-1] + [PR.sky[0][1]]
REFL = PR.water["refl"]
GLOWC = PR.glow["col"]
FOGA = R.hazed(G(["#26322a", "#3a4636"]), D_A, extra=-0.1)
FOGB = R.hazed(G(["#1c2820", "#304030", "#56604a"]), D_B)
FOGC = R.hazed(G(["#18221a", "#28362a", "#4a5440"]), D_C2)
FOREST = R.hazed(G(["#16201a", "#223024", "#36462e", "#56603c"]), 68.0)
BARKF = R.hazed(G(["#201c1a", "#382f28", "#58503f"]), 66.0)
MOSSF = R.hazed(G(["#4c5844", "#788264"]), 66.0)
BACK_PAL = (SKY_PAL + MSKY_PAL + WB + [PR.water["dark"]] + REFL + FOGA + FOGB + FOGC + FOREST + BARKF + MOSSF)

WOOD = G(["#2a2426", "#443a36", "#605448", "#7e705a", "#a09070"])    # weathered grey-brown: boardwalk, snag, log
BARK = G(["#2a201e", "#4a3830", "#6e5644"])                         # cypress bark (red-brown)
CROWN = G(["#1c281e", "#2a3a26", "#42542e", "#687440"])
MOSS = G(["#4a5644", "#6e7c62", "#9aa284"])                          # Spanish moss grey-green
REED = [CROWN[1], CROWN[2]] + G(["#5e7a38", "#8a9448"])
LILY = G(["#3e5e36", "#5a8040", "#7a9e4a", "#a6c060"])               # pads / duckweed: lighter than the water
FLOWER = G(["#a09a9a", "#e4dcd8", "#fffaf2"])                        # white water lilies
YELLOW = "#e8c040"
METAL = G(["#262628", "#46484c", "#76787a"])
LAMP = PER.const("LAMP", ["#ffc466", "#fff0c4"])                     # lantern glass / flame (unGraded: a light)
# lantern light on the post / planks: (blend level, radius px); level 0 = unlit (periods/swamp.py CONSTS)
LANTERN_LIGHT = PER.const("LANTERN_LIGHT", (0.3, 30.0))
FOG_GLOW_K = PER.const("FOG_GLOW_K", 1.0)       # x the glow's pull on the fog rows (fog_glow)
GLOW_RIM_K = PER.const("GLOW_RIM_K", 1.0)       # x the glow-side rim of the far shore (glow_rims); 0 = none
FRONT_PAL = WOOD + BARK + CROWN + MOSS + REED + LILY + FLOWER + [YELLOW] + METAL + LAMP

LB = Vector(PR.key_dir).normalized()          # one key light for the whole stage (high, dull)

# back-layer overlay codes produced by the front pass (applied relative to the local water band)
OV_DARK, OV_WM1, OV_RIPPLE, OV_PADLINE = 1, 2, 3, 4
_OV = {}


def rc(p):
    return R.rc(p, STAND)


def col_of(x, y):
    return rc((x, y, 0.0))[0]


def gap_dist(col, gap=GAP_C):
    """Screen distance (px) from the open channel: < 0 inside it."""
    return max(gap[0] - col, col - gap[1])


def valley_k(col, val=VAL_C, soft=30.0, floor=0.0):
    d = max(val[0] - col, col - val[1])
    k = min(1.0, max(0.0, (d + soft) / soft))
    k = k * k * (3 - 2 * k)
    return floor + (1 - floor) * k


def tagk(ob, kind, grp=None):
    return R.tagk(ob, kind, grp)


def ico(name, c, s, mat, sub=2):
    ob = C.add_prim("ico", name, mat, radius=1.0, location=(0, 0, 0), subdivisions=sub)
    ob.matrix_world = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
    C.set_smooth(ob)
    return ob


def ribbon(name, pts, widths, mat, th=0.006):
    """Closed thin ribbon along pts whose broad faces always face the camera (width axis = t x Y)."""
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


# ================================================================== trees (shared by back and front)
def moss_drape(x, y, z, w, L, rnd, mat):
    """One clump of Spanish moss: a tapering, slightly swaying curtain (wide top, wispy end)."""
    sw = rnd.uniform(-0.2, 0.2) * L
    return R.loft("Drape", [(x, y, z + 0.05 * L), (x + sw * 0.3, y, z - 0.35 * L), (x + sw * 0.8, y, z - 0.75 * L),
                            (x + sw, y, z - L)], [(w, w * 0.5), (w * 0.85, w * 0.45), (w * 0.5, w * 0.3), (w * 0.12, w * 0.1)],
                  mat, 6)


def cypress(x, y, h, rnd, M, grp, kinds=("tree", "moss", "knee"), moss="drape", knees=True, tiers=None, out=None,
            moss_k=1.0):
    """Bald cypress: fluted flared base (buttress ribs), straight tapering trunk, 3 irregular tiers of
    foliage pads on limbs from half height up, Spanish moss curtains hanging from the pad undersides
    ("drape": curtains only - far; "beard": a curtain + a few fine strands - near), knees around the base.
    M = dict(trunk, crown, moss=[mats], knee)."""
    out = out if out is not None else []
    grp = grp or "far"
    depth = max(1.0, P.project((x, y, h * 0.5), STAND)[2])
    mpp = depth / P.F_PX
    r0 = h * rnd.uniform(0.045, 0.055)
    lx, ly = rnd.uniform(-0.05, 0.05) * h, rnd.uniform(-0.02, 0.02) * h
    kt, km, kk = kinds

    def tp(t):
        return (x + lx * t * t, y + ly * t * t, h * t)
    zs = [0.0, 0.04, 0.1, 0.2, 0.4, 0.62, 0.8, 0.92]
    rs = [2.6, 1.9, 1.35, 1.05, 0.9, 0.72, 0.52, 0.32]
    pts = [(x, y, -0.4)] + [tp(t) for t in zs]
    out.append(tagk(R.loft("Trunk", pts, [r0 * 2.8] + [r0 * s for s in rs], M["trunk"], 10), kt, grp))
    nrib = rnd.randint(5, 7)
    for k in range(nrib):
        a = 2 * math.pi * k / nrib + rnd.uniform(-0.3, 0.3)
        ca, sa = math.cos(a), math.sin(a)
        f = rnd.uniform(0.85, 1.2)
        out.append(tagk(R.loft("Rib", [(x + ca * r0 * 3.1 * f, y + sa * r0 * 3.1 * f, -0.3),
                                       (x + ca * r0 * 2.3 * f, y + sa * r0 * 2.3 * f, 0.035 * h),
                                       (x + ca * r0 * 1.0, y + sa * r0 * 1.0, 0.17 * h * f)],
                               [0.55 * r0, 0.38 * r0, 0.18 * r0], M["trunk"], 6), kt, grp))
    ntier = tiers or 3
    for k in range(ntier):
        tt = (0.5 + 0.42 * k / max(1, ntier - 1)) * rnd.uniform(0.96, 1.04)
        zt = h * tt
        sides = [-1, 1] if k < ntier - 1 else [0, rnd.choice([-1, 1])]
        for side in sides:
            spread = h * rnd.uniform(0.14, 0.3) * (1 - 0.3 * k / ntier) * (0.3 if side == 0 else 1.0)
            bx, by = tp(tt)[0], y
            ex = bx + (side if side else rnd.choice([-1, 1])) * spread
            ey = y + rnd.uniform(-0.08, 0.08) * h
            ez = zt + rnd.uniform(0.03, 0.08) * h
            out.append(tagk(R.loft("Limb", [tp(tt - 0.06), ((bx + ex) / 2, (by + ey) / 2, (zt + ez) / 2 + 0.02 * h),
                                            (ex, ey, ez)], [0.32 * r0, 0.2 * r0, 0.1 * r0], M["trunk"], 6), kt, grp))
            rx = h * rnd.uniform(0.1, 0.14) * (1 - 0.2 * k / ntier)
            npad = rnd.randint(3, 4)
            for q in range(npad):
                u = (q / (npad - 1) - 0.5) * 2 if npad > 1 else 0.0          # spread the pads along the limb
                px = ex + u * rx * 1.1 + rnd.uniform(-0.25, 0.25) * rx
                py = ey + rnd.uniform(-0.5, 0.5) * rx
                pz = ez + rnd.uniform(-0.3, 0.3) * rx - 0.2 * rx * abs(u)
                prx = rx * rnd.uniform(0.7, 1.05)
                out.append(tagk(ico("Pad", (px, py, pz), (prx * 1.3, prx * 1.0, prx * 0.55), M["crown"]), kt, grp))
                # feathery rim: small sprigs poking out of the pad silhouette (no smooth cushion outline)
                for s in range(rnd.randint(3, 5) if moss == "beard" else rnd.randint(1, 2)):
                    a = rnd.uniform(-0.25, 1.25) * math.pi
                    sr = prx * rnd.uniform(0.22, 0.34)
                    out.append(tagk(ico("Sprig", (px + math.cos(a) * prx * 1.22, py - prx * rnd.uniform(0.0, 0.5),
                                                  pz + math.sin(a) * prx * 0.48), (sr * 1.3, sr, sr * 0.8), M["crown"], sub=1),
                                    kt, grp))
                # Spanish moss under the pad: far = one tapering curtain; near = a stringy clump of 3-5 narrow
                # curtains of different lengths + a few fine strands
                for d in range(rnd.randint(1, 2) if rnd.random() < moss_k else 0):
                    L = h * rnd.uniform(0.08, 0.2)
                    dx = px + rnd.uniform(-0.9, 0.9) * prx
                    dy = py - prx * rnd.uniform(0.3, 0.7)
                    dz = pz - prx * 0.3
                    mat = rnd.choice(M["moss"])
                    if moss != "beard":
                        w = max(prx * rnd.uniform(0.18, 0.3), 1.0 * mpp)
                        out.append(tagk(moss_drape(dx, dy, dz, w, L, rnd, mat), km, f"{grp}m"))
                        continue
                    wb = max(prx * 0.07, 1.1 * mpp)
                    for s in range(rnd.randint(3, 5)):
                        out.append(tagk(moss_drape(dx + rnd.uniform(-1.6, 1.6) * wb, dy + rnd.uniform(-0.04, 0.04), dz,
                                                   wb * rnd.uniform(0.8, 1.4), L * rnd.uniform(0.45, 1.15), rnd,
                                                   rnd.choice(M["moss"])), km, f"{grp}m"))
                    for s in range(rnd.randint(2, 3)):
                        sx = dx + rnd.uniform(-2.2, 2.2) * wb
                        sL = L * rnd.uniform(0.7, 1.4)
                        sway = rnd.uniform(-0.15, 0.15) * sL
                        spts = [(sx + sway * (j / 4) ** 1.5, dy - 0.03, dz - sL * j / 4) for j in range(5)]
                        w0 = max(0.6 * mpp, 0.012)
                        out.append(tagk(ribbon("Moss", spts, [w0 * 1.3, w0 * 1.1, w0, w0 * 0.8, w0 * 0.5], mat),
                                        km, f"{grp}m"))
    if knees:
        for k in range(rnd.randint(2, 5)):
            a = rnd.uniform(0, 2 * math.pi)
            d = r0 * rnd.uniform(3.6, 7.5)
            kh = h * rnd.uniform(0.03, 0.08)
            kx, ky = x + math.cos(a) * d, y + math.sin(a) * d
            ob = R.loft("Knee", [(kx, ky, -0.2), (kx, ky, kh * 0.6), (kx + 0.02 * h * rnd.uniform(-1, 1), ky, kh)],
                        [r0 * 0.75, r0 * 0.45, r0 * 0.25], M["knee"], 8)
            out.append(tagk(ob, kk, f"{grp}k{k}"))
            KNEES.append((kx, ky, r0 * 0.6))
    return out


def snag(x, y, h, rnd, mat, kind, grp, out=None):
    """Dead tree: flared base, tapering trunk with a broken slanted top, a few bare branches (some broken)."""
    out = out if out is not None else []
    r0 = h * rnd.uniform(0.035, 0.045)
    lx = rnd.uniform(-0.06, 0.06) * h

    def tp(t):
        return (x + lx * t * t, y, h * t)
    zs = [0.0, 0.05, 0.2, 0.5, 0.8, 0.94]
    pts = [(x, y, -0.4)] + [tp(t) for t in zs]
    top = tp(1.0)
    pts.append((top[0] + rnd.choice([-1, 1]) * 0.05 * h, top[1], top[2]))
    out.append(tagk(R.loft("Snag", pts, [r0 * s for s in (2.2, 2.0, 1.4, 1.0, 0.8, 0.6, 0.45, 0.2)], mat, 8), kind, grp))
    for k in range(rnd.randint(3, 4)):
        a = 2 * math.pi * k / 4 + rnd.uniform(-0.4, 0.4)
        out.append(tagk(R.loft("SRib", [(x + math.cos(a) * r0 * 2.6, y + math.sin(a) * r0 * 2.6, -0.3),
                                        (x + math.cos(a) * r0 * 1.0, y + math.sin(a) * r0 * 1.0, 0.12 * h)],
                               [0.45 * r0, 0.2 * r0], mat, 6), kind, grp))
    nb = rnd.randint(3, 5)
    for k in range(nb):
        t = rnd.uniform(0.42, 0.88)
        side = -1 if k % 2 == 0 else 1
        L = h * rnd.uniform(0.12, 0.3) * (0.4 if rnd.random() < 0.3 else 1.0)       # some broken short
        up = rnd.uniform(0.5, 1.2)
        b0 = tp(t)
        b1 = (b0[0] + side * L * 0.55, b0[1] + rnd.uniform(-0.2, 0.2) * L, b0[2] + L * 0.35 * up)
        b2 = (b0[0] + side * L, b0[1], b0[2] + L * 0.8 * up)
        out.append(tagk(R.loft("Branch", [b0, b1, b2], [0.34 * r0, 0.2 * r0, 0.09 * r0], mat, 6), kind, grp))
        if L > 0.2 * h and rnd.random() < 0.6:
            c2 = (b1[0] + side * L * 0.25, b1[1], b1[2] + L * 0.5)
            out.append(tagk(R.loft("Twig", [b1, c2], [0.14 * r0, 0.06 * r0], mat, 5), kind, grp))
    return out


# ================================================================== BACK LAYER geometry
KNEES = []


def back_scene(rnd):
    mt = R.m_tone
    KNEES.clear()
    # ---- A: the far forest drowned in fog (460 m): a low canopy band, lower in the valley under the glow
    am = mt(FOGA, [0.6], light=LB, lam=0.5, name="FogA")
    tagk(R.box("FogABase", (0, D_A + 12, 0.5), (1800, 10, 7.0), am), "fogA")
    # the fog wall: nothing (no water either) is seen past ~1.5 km, only fog meeting the sky
    tagk(R.box("FogWall", (0, 1500.0, -2.0), (9000, 10, 16.0), R.m_flat(R.hazed([FOGA[0]], 1500.0)[0])), "fogA")
    x = -800.0
    while x < 800:
        k = valley_k(col_of(x, D_A), VAL_C, 34, floor=0.35)
        hgt = rnd.uniform(3.5, 7.5) * k
        if rnd.random() < 0.1:
            hgt *= 1.7                                   # an emergent cypress crown
        r = rnd.uniform(4.0, 7.0)
        tagk(ico("FA", (x, D_A + rnd.uniform(-10, 10), hgt - r * 0.2), (r * 1.5, r, r * 0.5), am, sub=1), "fogA")
        x += rnd.uniform(4.0, 8.0)
    # ---- B: ghost cypress row (175 m) on a low bank + understory, a valley under the glow
    bt = mt(FOGB[:2], [0.55], light=LB, name="FogBT")
    bc = mt(FOGB, [0.4, 0.75], light=LB, noise=0.1, nscale=1.0, ndetail=1.0, name="FogBC")
    bmoss = [mt(FOGB[1:], [0.6], light=LB, lam=0.4, name="FogBM")]
    MB = dict(trunk=bt, crown=bc, moss=bmoss, knee=bt)
    tagk(R.box("BankB", (0, D_B + 3, 0.2), (1400, 6, 0.8), bt), "fogB")
    x = -330.0
    while x < 330:
        c = col_of(x, D_B)
        k = valley_k(c, VAL_C, 28, floor=0.3)
        r = rnd.uniform(1.6, 3.2)
        tagk(ico("Under", (x, D_B + rnd.uniform(-1, 2), r * 0.35), (r * 1.6, r, r * 0.8 * (0.5 + 0.5 * k)), bc, sub=1), "fogB")
        if rnd.random() < 0.75:
            h = rnd.uniform(8.0, 14.0) * k
            if h > 3.0:
                if rnd.random() < 0.18:
                    snag(x, D_B + rnd.uniform(0, 4), h, rnd, bt, "fogB", None)
                else:
                    cypress(x, D_B + rnd.uniform(0, 4), h, rnd, MB, None, kinds=("fogB",) * 3, knees=False,
                            tiers=2)
        x += rnd.uniform(5.0, 10.0)
    # ---- C2: a sparse darker row at ~100 m on the sides (tops show above the far shore) + two dead snags
    #      standing in the open channel in front of the glow
    ct = mt(FOGC[:2], [0.55], light=LB, name="FogCT")
    cc = mt(FOGC, [0.4, 0.75], light=LB, noise=0.1, nscale=1.4, ndetail=1.0, name="FogCC")
    cmoss = [mt(FOGC[1:], [0.6], light=LB, lam=0.4, name="FogCM")]
    MC = dict(trunk=ct, crown=cc, moss=cmoss, knee=ct)
    x = -120.0
    while x < 120:
        yy = D_C2 + rnd.uniform(-6, 8)
        if gap_dist(col_of(x, yy)) > 10:
            h = rnd.uniform(10.0, 15.0)
            cypress(x, yy, h, rnd, MC, None, kinds=("fogC",) * 3, knees=False, tiers=rnd.randint(2, 3))
        x += rnd.uniform(9.0, 16.0)
    snag(-5.4, 112.0, 10.5, rnd, ct, "fogC", "snagC1")
    snag(-13.2, 126.0, 5.5, rnd, ct, "fogC", "snagC2")
    for (kx, ky, kh) in ((-4.0, 110.0, 0.9), (-7.2, 114.0, 0.6), (-12.0, 124.5, 0.7)):
        tagk(R.loft("KneeC", [(kx, ky, -0.2), (kx, ky, kh)], [0.3, 0.1], ct, 6), "fogC")
    # ---- the far shore (62-72 m): mud bank left + right of the channel, cypress + snags, knees, understory
    tr = mt(BARKF, [0.42, 0.78], light=LB, noise=0.12, nscale=3.0, nvec=(1.0, 1.0, 0.15), name="BarkF")
    cr = mt(FOREST, [0.32, 0.56, 0.82], light=LB, noise=0.14, nscale=1.6, ndetail=1.0, bias=-0.04, name="Crown")
    ms = [mt(MOSSF, [0.55], light=LB, lam=0.4, bias=b, name="MossF") for b in (-0.08, 0.06)]
    un = mt(FOREST[:3], [0.4, 0.75], light=LB, noise=0.16, nscale=2.5, ndetail=1.0, name="Under")
    mud = mt([BARKF[0], FOREST[1]], [0.6], light=LB, name="Mud")
    MS = dict(trunk=tr, crown=cr, moss=ms, knee=tr)
    xl = R.col_to_x(GAP_C[0], 64.0, STAND)
    xr = R.col_to_x(GAP_C[1], 64.0, STAND)
    for (a, b, side) in ((-70.0, xl, -1), (xr, 70.0, 1)):
        verts, faces = [], []
        xs = np.arange(a, b + 0.01, 1.0)
        for x in xs:
            edge = (b - x) if side < 0 else (x - a)
            taper = min(1.0, max(0.1, edge / 4.0))
            zt = (0.45 + 0.15 * math.sin(x * 0.7) + rnd.uniform(-0.05, 0.05)) * taper
            verts += [(x, 64.0, 0.05), (x, 65.2, zt), (x, 95.0, zt + 0.3)]
        for i in range(len(xs) - 1):
            p, q = i * 3, (i + 1) * 3
            faces += [(p, q, q + 1, p + 1), (p + 1, q + 1, q + 2, p + 2)]
        tagk(R.mesh_from("Bank", verts, faces, mud, smooth=False), "bank")
    gid = [0]
    x = -54.0
    while x < 54:
        yy = rnd.uniform(62.4, 70.0)
        c = col_of(x, yy)
        gd = gap_dist(c)
        if gd > 3:
            gid[0] += 1
            far = abs(c - GLOW_C)
            h = rnd.uniform(5.5, 8.5) * (1.0 + 0.45 * min(1.0, max(0.0, (far - 120) / 90)))
            h *= min(1.0, max(0.55, gd / 34.0))          # lower towards the channel
            if rnd.random() < 0.14:
                snag(x, yy, h * 1.1, rnd, tr, "snag", f"sn{gid[0]}")
            else:
                cypress(x, yy, h, rnd, MS, f"c{gid[0]}")
        x += rnd.uniform(3.2, 6.5)
    # understory along the bank: breaks the stick-on-water look, the mist veils it
    x = -60.0
    while x < 60:
        if gap_dist(col_of(x, 64.5)) > 1:
            r = rnd.uniform(0.5, 1.3)
            tagk(ico("Shrub", (x, rnd.uniform(64.6, 66.5), r * 0.35), (r * 1.5, r, r * 0.75), un), "tree", f"u{int(x * 10)}")
        x += rnd.uniform(0.9, 2.2)
    tagk(R.hpoly("Water", [(-9000, -40), (9000, -40), (9000, 3200), (-9000, 3200)], 0.0, R.m_flat(WB[3]), 0.01), "water")


# ================================================================== FRONT LAYER geometry
# (x, y, h): cypress in the water hugging the sides (the old stage's six trees, the central one moved left)
TREES = [(-8.6, 9.5, 8.6), (10.6, 14.0, 9.6), (-15.5, 26.0, 10.8), (14.5, 33.0, 10.4), (-19.0, 45.0, 10.0)]
SNAG = (17.5, 41.0, 11.0)
PADS = [(-6.4, 12.0, 0.6, True), (-7.5, 13.3, 0.5, False), (-6.0, 14.5, 0.42, False),
        (6.7, 8.4, 0.62, False), (7.9, 9.7, 0.7, True), (6.5, 10.9, 0.45, False),
        (-10.4, 21.4, 0.8, False), (-9.2, 23.2, 0.7, True), (10.9, 19.8, 0.8, False), (12.2, 21.8, 0.72, True),
        (-13.2, 32.0, 0.9, False), (12.2, 29.0, 0.85, False)]
# duckweed patches (x, y, radius m, density): along the sides, at posts / reeds / trunks / log - never mid-water
DUCK = [(3.5, 1.7, 0.9, 0.55), (-3.5, 1.5, 1.0, 0.55), (-4.8, 3.0, 1.6, 0.5), (5.6, 4.4, 1.5, 0.5),
        (-8.2, 10.4, 2.2, 0.42), (10.0, 14.8, 2.4, 0.4), (-7.2, 13.0, 2.2, 0.35), (-15.0, 27.0, 3.2, 0.35),
        (14.0, 33.8, 3.0, 0.35), (10.4, 18.2, 2.2, 0.4), (-11.4, 19.2, 2.6, 0.35), (12.8, 25.4, 2.6, 0.32)]
LANTERN = (1.68, 0.78, STAND + 1.02)
PAD_INFO = []
FLOWERS = []
CLUMP_BASES = []


def front_scene(rnd):
    mt = R.m_tone
    PAD_INFO.clear()
    FLOWERS.clear()
    CLUMP_BASES.clear()
    KNEES.clear()
    # ---- the old boardwalk end: weathered planks (two broken short), header beam, crooked posts
    tagk(R.box("DeckBase", (0, -8.6, STAND - 0.09), (2.42, 19.0, 0.12), R.m_flat(WOOD[0])), "pier", "pier0")
    nplank = 9
    pw = 2.4 / nplank
    broken = {2: 0.42, 6: 0.66}
    for i in range(nplank):
        xc = -1.2 + pw * (i + 0.5)
        bias = rnd.uniform(-0.08, 0.06)
        pm = mt(WOOD[1:], [0.5, 0.72, 0.9], light=LB, bias=bias, noise=0.18, nscale=1.3,
                ncoord="object", nvec=(9.0, 0.35, 1.0), ndetail=1.0, name="Plank")
        y1 = broken.get(i, 1.0 + rnd.uniform(-0.06, 0.05))
        tilt = Matrix.Rotation(rnd.uniform(-0.012, 0.012), 4, "Y")
        tagk(R.box("Plank", (xc, (y1 - 18.0) / 2, STAND - 0.025 + rnd.uniform(-0.01, 0.008)), (pw - 0.03, y1 + 18.0, 0.05), pm,
                   rot=tilt), "plank", f"pl{i}")
    beam = mt(WOOD[:4], [0.4, 0.6, 0.8], light=LB, noise=0.1, nscale=2, nvec=(1, 0.2, 4), name="Beam")
    for sx in (-1.24, 1.24):
        tagk(R.box("Stringer", (sx, -8.6, STAND - 0.12), (0.1, 19.0, 0.22), beam), "pier", f"st{sx}")
    tagk(R.box("Header", (0.0, 0.93, STAND - 0.16), (2.5, 0.12, 0.2), beam), "pier", "header")
    postm = mt(WOOD[:4], [0.34, 0.55, 0.8], light=LB, noise=0.08, nscale=3, nvec=(1, 1, 0.15), name="Post")
    posts = []
    for (px, py, top, lean) in ((-1.3, 0.8, STAND + 0.42, 0.07), (1.3, 0.8, STAND + 1.36, -0.015),
                                (-1.3, -2.2, STAND + 0.16, 0.0), (1.3, -2.2, STAND + 0.12, 0.03),
                                (-1.3, -6.0, STAND + 0.14, -0.02), (1.3, -6.0, STAND + 0.16, 0.0),
                                (-1.3, -10.0, STAND + 0.15, 0.0), (1.3, -10.0, STAND + 0.15, 0.0)):
        tx = px + lean * (top + 1.2)
        ob = R.loft("Post", [(px, py, -1.2), (tx, py, top - 0.04), (tx, py, top)], [0.12, 0.11, 0.085], postm, 12)
        posts.append(tagk(ob, "post", f"po{px}{py}"))
        CLUMP_BASES.append((px, py, 0.14))
    # ---- lantern hanging from an arm on the right end post (lights the post and the planks next to it)
    metal = mt(METAL, [0.45, 0.76], light=LB, name="Metal")
    ax, ay, az = 1.3 - 0.015 * 2.5, 0.8, STAND + 1.28
    lx, ly, lz = LANTERN
    posts.append(tagk(R.loft("Arm", [(ax, ay, az), (lx + 0.02, ay, az)], 0.028, postm, 6), "post", "arm"))
    posts.append(tagk(R.loft("Brace", [(ax, ay, az - 0.28), (ax + 0.24, ay, az - 0.01)], 0.02, postm, 6), "post", "arm"))
    lamp = []
    lamp.append(R.loft("Hook", [(lx, ly, az), (lx, ly, lz + 0.2)], 0.012, R.m_flat(METAL[0]), 6))
    lamp.append(R.box("Cap", (lx, ly, lz + 0.155), (0.24, 0.24, 0.05), metal))
    lamp.append(R.loft("CapTop", [(lx, ly, lz + 0.18), (lx, ly, lz + 0.22)], [0.07, 0.02], metal, 8))
    lamp.append(R.box("Base", (lx, ly, lz - 0.14), (0.22, 0.22, 0.045), metal))
    lamp.append(R.box("Glass", (lx, ly, lz), (0.17, 0.17, 0.24), R.m_flat(LAMP[0])))
    lamp.append(R.box("Flame", (lx, ly - 0.09, lz - 0.02), (0.06, 0.02, 0.1), R.m_flat(LAMP[1])))
    for (dx, dy) in ((-0.095, -0.095), (0.095, -0.095)):
        lamp.append(R.box("Frame", (lx + dx, ly + dy, lz), (0.022, 0.022, 0.26), R.m_flat(METAL[1])))
    for ob in lamp:
        tagk(ob, "lantern", "lantern")
    posts += lamp
    # ---- cypress trees in the water (sides) + a dead snag
    bark = mt(BARK, [0.4, 0.75], light=LB, noise=0.12, nscale=3.5, nvec=(1.0, 1.0, 0.12), name="Bark")
    crown = mt(CROWN, [0.34, 0.58, 0.84], light=LB, noise=0.22, nscale=4.5, ndetail=1.0, bias=-0.03, name="CrownN")
    knee = mt(BARK[1:], [0.55], light=LB, name="Knee")
    trees = []
    for k, (x, y, h) in enumerate(TREES):
        # stringy moss: darker vertical stripes ~2.6 px apart at this tree's distance
        per = 2.6 * P.project((x, y, h * 0.6), STAND)[2] / P.F_PX
        moss = [mt(MOSS, [0.45, 0.78], light=LB, lam=0.5, bias=b, pats=((0, per, 0.38, -0.22, "world", k * 0.37),),
                   name="Moss") for b in (-0.1, 0.0, 0.1)]
        MF = dict(trunk=bark, crown=crown, moss=moss, knee=knee)
        trees += cypress(x, y, h, rnd, MF, f"T{k}", kinds=("trunk", "moss", "knee"), moss="beard", tiers=3)
        CLUMP_BASES.append((x, y, h * 0.1))
    sm = mt(WOOD[:4], [0.35, 0.6, 0.84], light=LB, noise=0.1, nscale=3.0, nvec=(1.0, 1.0, 0.15), name="SnagW")
    trees += snag(SNAG[0], SNAG[1], SNAG[2], rnd, sm, "trunk", "S0")
    CLUMP_BASES.append((SNAG[0], SNAG[1], SNAG[2] * 0.08))
    for (kx, ky, kr) in KNEES:
        CLUMP_BASES.append((kx, ky, kr))
    # ---- reeds (clumps framing the sides, centre left open)
    catm = mt(BARK[1:], [0.6], light=LB, name="Cattail")
    refl = []
    for (cx, cy, n, spread, hmin, hmax) in ((-4.6, 2.3, 40, 0.8, 1.0, 1.9), (5.3, 3.7, 36, 0.85, 1.0, 1.8),
                                            (-11.8, 18.0, 30, 1.3, 1.4, 2.4), (12.6, 24.6, 28, 1.3, 1.5, 2.5)):
        refl += reed_clump(cx, cy, n, spread, hmin, hmax, rnd, catm)
    # ---- lily pads (flat mask geometry; shaded by hand in post)
    padm = R.m_flat(LILY[2])
    for (x, y, r, fl) in PADS:
        rot = -math.pi / 2 + rnd.uniform(-0.9, 0.9)
        lily(x, y, r, rot, padm, rnd)
        if fl:
            FLOWERS.append((x + r * 0.3 * math.cos(rot + 2.6), y + r * 0.3 * math.sin(rot + 2.6)))
    # ---- floating mossy log (weathered grey, lighter than the water) with a broken branch stub
    logm = mt(WOOD[2:], [0.35, 0.7], light=LB, noise=0.1, nscale=2.0, nvec=(0.3, 1.0, 1.0), name="Log")
    lm = mt(MOSS[1:], [0.5], light=LB, lam=0.5, name="LogMoss")
    a, b = Vector((8.7, 16.6, 0.06)), Vector((11.7, 18.9, 0.1))
    logs = [tagk(R.loft("Log", [a, (a + b) / 2 + Vector((0, 0, 0.04)), b], [0.3, 0.28, 0.24], logm, 12), "log", "log"),
            tagk(R.loft("LogMoss", [a + Vector((0.3, 0.2, 0.25)), (a + b) / 2 + Vector((0, 0, 0.3)), b - Vector((0.6, 0.45, -0.2))],
                        [(0.16, 0.05), (0.2, 0.06), (0.14, 0.05)], lm, 8), "logmoss", "log"),
            tagk(R.loft("Stub", [(a + b) / 2 + Vector((0.1, -0.2, 0.15)), (a + b) / 2 + Vector((-0.2, -0.5, 0.75))],
                        [0.07, 0.04], logm, 6), "log", "log")]
    CLUMP_BASES.append((10.2, 17.75, 0.25))
    return refl + posts + trees + logs


def reed_material(hz, bias):
    return R.m_tone(REED, [0.25, 0.5, 0.75], light=LB, lam=0.4, bias=bias,
                    grads=((2, hz * 0.96, hz * 1.04, 0.5, "world"),), name="Reed")


def blade(x, y, h, dirx, diry, bend, a0, a1, mat, droop=0.55, nseg=10):
    pts, ws = [], []
    for k in range(nseg):
        t = k / (nseg - 1)
        off = bend * h * 0.8 * t * t
        z = h * (t - bend * droop * t ** 3)
        pts.append((x + dirx * off, y + diry * off, z))
        ws.append(a0 * (1 - t) + a1 * t)
    return ribbon("Blade", pts, ws, mat)


def reed_clump(cx, cy, n, spread, hmin, hmax, rnd, catm):
    objs = []
    depth = P.project((cx, cy, 0), STAND)[2]
    mpp = depth / P.F_PX
    hz = 0.4 * (hmin + hmax) / 2
    mats = [reed_material(hz, b) for b in (-0.06, 0.0, 0.05)]
    irism = R.m_tone(LILY[:3], [0.45, 0.75], light=LB, name="Iris")
    nfan = max(4, n // 7)
    bases = []
    for f in range(nfan):
        side = -1 if rnd.random() < 0.5 else 1
        bases.append((cx + rnd.gauss(0, spread * 0.6), cy + rnd.gauss(0, spread * 0.3), side))
        CLUMP_BASES.append((bases[-1][0], bases[-1][1], spread * 0.3))
    ncat = max(1, int(round(n * rnd.uniform(0.1, 0.15))))
    a_tip = 0.52 * mpp
    a_base = max(0.016, 0.95 * mpp)
    for i in range(n):
        bx, by, side = bases[i % nfan]
        g = f"reed{cx}_{i % nfan}"
        x = bx + rnd.gauss(0, 0.05)
        y = by + rnd.gauss(0, 0.05)
        h = rnd.uniform(hmin, hmax)
        mat = mats[rnd.randrange(3)]
        if i < ncat:
            lean = rnd.uniform(-0.08, 0.08)
            top = (x + lean * h, y, h)
            objs.append(tagk(ribbon("Stem", [(x, y, -0.2), (x + lean * h * 0.5, y, h * 0.5), top], [a_tip] * 3, mat), "reed", g))
            hl = 5.0 * mpp
            t1 = 0.86
            t0 = t1 - hl / h
            a = (x + lean * h * t0, y, h * t0)
            b = (x + lean * h * t1, y, h * t1)
            objs.append(tagk(R.loft("Head", [a, b], [1.0 * mpp, 1.0 * mpp], catm, 8), "cattail", g))
            objs.append(tagk(ribbon("Spike", [b, top], [0.5 * mpp, 0.5 * mpp], mat), "reed", g))
        elif rnd.random() < 0.6:
            dx = side * rnd.uniform(0.25, 1.0) if rnd.random() < 0.8 else -side * rnd.uniform(0.2, 0.6)
            bend = rnd.uniform(0.35, 0.8)
            objs.append(tagk(blade(x, y, h * rnd.uniform(0.65, 1.0), dx, rnd.uniform(-0.3, 0.3), bend,
                                   a_base, a_tip, mat), "reed", g))
        else:
            dx = rnd.uniform(-1, 1)
            objs.append(tagk(blade(x, y, h, dx, rnd.uniform(-0.3, 0.3), rnd.uniform(0.04, 0.2),
                                   a_base, a_tip, mat, droop=0.3), "reed", g))
    for k in range(rnd.randint(3, 5)):
        bx, by, side = bases[k % nfan]
        hh = rnd.uniform(0.4, 0.62) * hmin
        objs.append(tagk(blade(bx + rnd.gauss(0, 0.12), by - 0.12, hh, rnd.uniform(-0.6, 0.6), 0.0, rnd.uniform(0.06, 0.16),
                               max(0.03, 1.5 * mpp), 0.52 * mpp, irism, droop=0.1, nseg=6), "reed", f"iris{cx}_{k}"))
    return objs


def lily(x, y, r, rot, padm, rnd):
    n = 22
    verts = [(x, y, 0.03)]
    for i in range(n):
        a = rot + 2 * math.pi * i / n
        rr = r * (0.08 if i == 0 else 1.0) * rnd.uniform(0.97, 1.03)
        verts.append((x + rr * math.cos(a), y + rr * math.sin(a), 0.012))
    faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)]
    ob = R.mesh_from("Pad", verts, faces, padm, smooth=True)
    R._fix_normals(ob)
    tagk(ob, "pad", f"pad{x}{y}")
    PAD_INFO.append(dict(ob=ob, x=x, y=y, r=r, rot=rot))


# ================================================================== back composite
# water depth zones (start row, end row) for WB[i] -> WB[i+1]: rows 107-131 are only seen in the channel; the
# play area (rows >= ~150) is WB[4] = waterTint, the last rows near the boardwalk WB[5]
WATER_Z = [(108, 113), (118, 125), (131, 140), (148, 166), (262, 286)]
ROW_H, ROW_P = 106, 335


def draw_dash(img_idx, r, c0, n, v, mask=None):
    r = int(r)
    if r < 0 or r >= img_idx.shape[0]:
        return
    for c in range(int(c0), int(c0) + max(1, int(n))):
        if 0 <= c < img_idx.shape[1] and (mask is None or mask[r, c]):
            img_idx[r, c] = v


def reflect_tint(ref_hex, k, dark):
    wl = R.s2l(R.hexrgb(ref_hex))

    def fn(c):
        return R.l2s((R.s2l(c) * (1 - k) + wl[None, :] * k) * dark)
    return fn


def main_water(water):
    """Per column: the contiguous water run that reaches the bottom row (the swamp itself, not the far slivers
    of water seen between the distant layers). -> (mask, smoothed top row per column)."""
    out = np.zeros_like(water)
    top = np.full(W, H, float)
    for c in range(W):
        col = water[:, c]
        if not col[-1]:
            continue
        nz = np.flatnonzero(~col)
        s = nz.max() + 1 if len(nz) else 0
        out[s:, c] = True
        top[c] = s
    # a knee / snag base standing in the channel must not notch the waterline: running median over 7 columns
    sm = np.array([np.median(top[max(0, c - 3):c + 4]) for c in range(W)])
    return out, sm


def far_reflection(pal, idx, water, refl_objs, mainw, line):
    """EXACT reflection of every far layer (mirror pass) on the main water. Murky swamp water: tinted further
    towards the water than the lake, light break lines every third row, dissolving over the lower ~55 % of its
    height (the close far shore would otherwise throw long columns deep into the play area)."""
    mp = R.mirror_pass(refl_objs, tag=f"{SID}_farmirror")
    rng = random.Random(7)
    ra = mp["a"] & mainw
    midx, _ = R.quantize(mp["rgb"], ra, pal, dither=False)
    lab = R.min_runs(np.where(ra, midx + 1, 0), 3)
    top = np.clip(line, 0, H - 1).astype(int)
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
        p = np.clip((frac[r] - 0.42) / 0.5, 0, 1) ** 0.8
        brk = R.run_noise(rng, W, 3, 8) < p
        keep = m & ~brk & mainw[r]
        out[r, keep] = lab[r, keep] - 1
        keepm[r] = keep
        if (r - int(np.median(top))) % 3 == 2:
            linem[r] = R.dash_mask(W, 0.4, 6, 20, rng) & keep
    body = keepm & ~linem
    out2, pal = R.recolour(out, pal, body, reflect_tint(WB[4], 0.3, 0.84), snap=0.03)
    out2, pal = R.recolour(out2, pal, linem, reflect_tint(WB[3], 0.6, 0.95), snap=0.03)
    return out2, pal, keepm


def bay_sky(pal, idx, water, wband, shore_row):
    """The channel water mirrors the sky + glow rings exactly (no disc), tinted towards the far water, joined
    to the bands by dash dithering; light break lines every third row."""
    rng = random.Random(21)
    cols = np.arange(W)[None, :]
    zone = water & (np.arange(H)[:, None] < shore_row + 5) & (cols > GAP_C[0] - 24) & (cols < GAP_C[1] + 24)
    thr = R.dash_threshold(H, W, rng, 2, 7)
    m_idx, _ = R.quantize(MSKY_RGB, zone, pal, dither=True, thr=thr)
    rows = np.arange(H)[:, None]
    fade = np.clip((rows - (shore_row - 1)) / 6.0, 0, 1)
    use = zone & (R.dash_threshold(H, W, rng, 3, 9) >= fade)
    out = idx.copy()
    out[use] = m_idx[use]
    out, pal = R.recolour(out, pal, use, reflect_tint(WB[0], 0.22, 0.92), snap=0.025)
    for r in range(int(R.HORIZON_ROW), int(shore_row) + 5):
        if r % 3 == 1:
            bl = R.dash_mask(W, 0.35, 5, 16, rng) & use[r]
            out[r, bl] = wband[r, bl]
    return out, pal, use


def wave_marks(pal, idx, water, avoid, wband, shore_row):
    """Very sparse wave marks (still swamp water): a light dash (water +1) over a darker dash (water -1)."""
    rng = random.Random(11)
    up1 = R.ramp_step(pal, WSTEP, wband, +1)
    dn1 = R.ramp_step(pal, WSTEP, wband, -1)
    for k in range(170):
        y = 1.0 + 60 * rng.random() ** 1.5
        x = rng.uniform(-1, 1) * (y + 12) * 0.75
        c, r = rc((x, y, 0))
        if r <= shore_row + 3 or r >= H - 1 or c < 0 or c >= W:
            continue
        ri, ci = int(r), int(c)
        if avoid[ri, ci]:
            continue
        central = 190 < c < 450 and 140 < r < 330
        if central and rng.random() < 0.85:
            continue
        depth = P.project((x, y, 0), STAND)[2]
        n = min(12, max(2, int(round(420 / depth * rng.uniform(0.25, 0.55)))))
        ok = water & ~avoid
        draw_dash(idx, r, c, n, int(up1[ri, ci]), ok)
        draw_dash(idx, r + 1, c + 1, max(1, n - 2), int(dn1[min(H - 1, ri + 1), ci]), ok)
    return idx


def fog_glow(idx, pal, kid):
    """The dull amber glow sits BEHIND the fog: the fog in front of the far rows catches it. Far layers inside
    the glow rings move towards the glow colour (2 flat levels, Bayer-dithered hand-over, farther = more)."""
    lv, _ = R.glow_level(PR)
    t = lv / max(r[1] for r in PR.glow["rings"])
    b = R.bayer(H, W)
    for kinds, k1, k2 in ((("fogA",), 0.42, 0.22), (("fogB",), 0.3, 0.15), (("fogC",), 0.2, 0.1)):
        m = np.isin(kid, kinds)
        hi = m & (t > 0.72 + 0.3 * (b - 0.5))
        lo = m & ~hi & (t > 0.3 + 0.3 * (b - 0.5))
        idx, pal = R.blend_idx(idx, pal, hi, GLOWC, k1 * FOG_GLOW_K, snap=0.03)
        idx, pal = R.blend_idx(idx, pal, lo, GLOWC, k2 * FOG_GLOW_K, snap=0.03)
    return idx, pal


def glow_rims(idx, pal, kid):
    """Weak glow-side rim on the far-shore silhouette (tops + the side facing the glow), only near the glow."""
    if GLOW_RIM_K <= 0:
        return idx, pal
    lv, _ = R.glow_level(PR)
    prox = np.clip(lv / 0.2, 0, 1)
    rim = PR.rim
    cols = np.arange(W)[None, :]
    m = np.isin(kid, ["tree", "snag", "moss", "knee"])
    up_out = m & ~R.shift(m, 1, 0, False)
    right_out = m & ~R.shift(m, 0, -1, False)
    left_out = m & ~R.shift(m, 0, 1, False)
    side_out = np.where(cols < LIGHT_C, right_out, left_out)
    k = (up_out * 1.0 + side_out * 0.7).clip(0, 1) * prox
    idx, pal = R.blend_idx(idx, pal, k > 0.5, rim["col"], 0.34 * GLOW_RIM_K, lighter=True, snap=0.03)
    idx, pal = R.blend_idx(idx, pal, (k > 0.2) & (k <= 0.5), rim["col"], 0.18 * GLOW_RIM_K, lighter=True, snap=0.03)
    return idx, pal


def ground_fog(idx, pal, kid, ps):
    """Ground fog: the lower metres of the far shore / middle row sink into the haze (one flat level with a
    dash-dithered upper edge)."""
    z = R.world_z(ps["depth"], STAND)
    thr = R.dash_threshold(H, W, random.Random(41), 2, 7)
    for kinds, zs, amt in ((("tree", "moss", "knee", "snag", "bank"), 1.5, 0.36), (("fogC",), 2.6, 0.3)):
        m = np.isin(kid, kinds)
        a = np.exp(-np.clip(z, 0, None) / zs)
        fm = m & (a > 0.4 + 0.45 * (thr - 0.5))
        idx, pal = R.blend_idx(idx, pal, fm, PR.haze["col"], amt, snap=0.035)
    return idx, pal


def water_fog(idx, pal, water, line):
    """Low fog wisps drifting over the far water just below the mist band (heavy mist, but the play area in
    the middle stays clear): horizontal dash runs, fading within ~26 rows of the far waterline."""
    rng = random.Random(53)
    rows = np.arange(H)[:, None] + 0.5
    x = np.arange(W)
    wisp = np.clip(0.5 + 0.9 * (R._vnoise(x / 31.0, 7.0) - 0.5) + 0.5 * (R._vnoise(x / 9.0, 11.0) - 0.5), 0, 1)
    d = rows - line[None, :]
    a = np.where(water & (d > 3), np.exp(-(d - 3) / 8.0), 0.0) * (0.3 + 0.7 * wisp[None, :])
    thr = R.dash_threshold(H, W, rng, 4, 14)
    m = water & (a > thr) & (a > 0.15)
    return R.blend_idx(idx, pal, m, PR.mist["col"], 0.2, snap=0.03)


def render_back(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    back_scene(rnd)
    PER.hook("back_scene", rnd=rnd, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_back")
    kid = R.kind_map(ps)
    sky = ~ps["a"]
    water = kid == "water"
    near = np.isin(kid, ["tree", "moss", "knee", "snag"])
    pal = R.Pal(BACK_PAL)
    rgb = ps["rgb"].copy()
    rgb[sky] = SKY_RGB[sky]
    idx, solid = R.quantize(rgb, np.ones((H, W), bool), pal, dither=True)
    idx = R.despeckle(idx, ps["id"], protect=~solid | sky, passes=1)
    lined = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=3.0, rel=0.015, steps=1)
    idx = np.where(near, lined, idx)
    idx, pal = fog_glow(idx, pal, kid)
    idx, pal = glow_rims(idx, pal, kid)
    idx, pal = ground_fog(idx, pal, kid, ps)
    # ---- water: depth bands with horizontal dash dithering
    wband = R.water_bands(pal, WB, WATER_Z, random.Random(5), ROW_H, ROW_P)
    idx[water] = wband[water]
    shore_row = rc((0, D_SHORE, 0))[1]
    mainw, line = main_water(water)
    idx, pal, baym = bay_sky(pal, idx, water, wband, shore_row)
    # ---- exact reflections of the far layers
    refl_objs = [ob for ob in R.mesh_objects() if ob.get("kind") not in ("water", None)]
    idx, pal, farm = far_reflection(pal, idx, water, refl_objs, mainw, line)
    # ---- front-object reflections / ripples / pad lines (computed by the front pass)
    ov = _OV.get("ov")
    if ov is None:
        p = os.path.join(R.WORK, "front_overlay.npy")
        ov = np.load(p) if os.path.exists(p) else np.zeros((H, W), np.int8)
    idx = wave_marks(pal, idx, water, farm | (ov > 0), wband, shore_row)
    idx[water & (ov == OV_DARK)] = pal.index(REFL[0])
    for code, d in ((OV_WM1, -1), (OV_RIPPLE, +1), (OV_PADLINE, -1)):
        m = water & (ov == code)
        idx[m] = R.ramp_step(pal, WSTEP, wband, d)[m]
    # ---- heavy mist on the far waterline + low fog wisps over the far water (no glitter in this preset)
    land = ~sky & ~water
    idx, pal = water_fog(idx, pal, mainw, line)
    ma = R.mist_amount(PR, line, land, water)
    thr = R.dash_threshold(H, W, random.Random(29), 2, 9)
    idx, pal = R.blend_idx(idx, pal, (ma > thr) & (ma > 0.08), PR.mist["col"], 0.5, snap=0.03)
    idx, pal = PER.hook("back_post", idx, pal, kid=kid, water=water, sky=sky, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "back")
    print("HYB back colours", R.count_colours(img), "palette", len(pal), "shore row", round(shore_row, 1),
          "play water", WB[4], "L", round(R.lum(WB[4]), 3), "near", WB[5], "L", round(R.lum(WB[5]), 3),
          "tint", PR.water["tint"], "L", round(R.lum(PR.water["tint"]), 3))
    return pal


# ---------------------------------------------------------------- front post helpers
FLOWER_STAMP = ["..L..",
                ".LML.",
                "LMYML",
                "DDDDD"]


def shade_pads(idx, pal, ps):
    """Hand-style pad shading: sun-side half LILY[2], other half LILY[1], 1 px lit rim LILY[3], vein LILY[0]."""
    allm = np.zeros(idx.shape, bool)
    right = R.sun_side(PR) == "right"
    for info in PAD_INFO:
        pid = info["ob"].pass_index
        m = ps["id"] == pid
        if not m.any():
            continue
        allm |= m
        cx, cy = rc((info["x"], info["y"], 0.02))
        cols = np.arange(W)[None, :]
        lit_half = (cols >= int(round(cx))) if right else (cols < int(round(cx)))
        idx[m] = pal.index(LILY[1])
        idx[m & lit_half] = pal.index(LILY[2])
        up = np.zeros_like(m)
        up[1:] = m[:-1]
        sd = np.zeros_like(m)
        if right:
            sd[:, :-1] = m[:, 1:]
        else:
            sd[:, 1:] = m[:, :-1]
        rim = m & ((~up) | (~sd)) & lit_half & ((cols > cx + 1) if right else (cols < cx - 1))
        idx[rim] = pal.index(LILY[3])
        rot = info["rot"]
        r = info["r"]
        for k in range(1, 6):
            t = 0.12 + 0.5 * k / 5
            vx, vy = rc((info["x"] - r * t * math.cos(rot), info["y"] - r * t * math.sin(rot), 0.02))
            vi, vj = int(vy), int(vx)
            if 0 <= vi < H and 0 <= vj < W and m[vi, vj]:
                idx[vi, vj] = pal.index(LILY[0])
    return allm


def stamp_flowers(idx, pal):
    cmap = {"L": FLOWER[2], "M": FLOWER[1], "D": FLOWER[0], "Y": YELLOW}
    for (fx, fy) in FLOWERS:
        c, r = rc((fx, fy, 0.05))
        x0, y0 = int(round(c)) - 2, int(round(r)) - 3
        for j, ln in enumerate(FLOWER_STAMP):
            for i, ch in enumerate(ln):
                if ch in cmap and 0 <= y0 + j < H and 0 <= x0 + i < W:
                    idx[y0 + j, x0 + i] = pal.index(cmap[ch])


def stamp_duckweed(idx, pal, rng):
    """Duckweed: specks and 2-3 px flecks (lighter than the water) scattered in soft-edged patches on empty
    front pixels only, denser in the patch centre. Returns the duckweed mask."""
    cols = [pal.index(LILY[1]), pal.index(LILY[2]), pal.index(LILY[3])]
    mask = np.zeros(idx.shape, bool)
    for (x, y, r, dens) in DUCK:
        c, rr = rc((x, y, 0.005))
        rx = abs(rc((x + r, y, 0.005))[0] - c)
        ry = abs(rc((x, y - r, 0.005))[1] - rc((x, y + r, 0.005))[1]) / 2
        for py in range(int(rr - ry) - 1, int(rr + ry) + 2):
            for px in range(int(c - rx) - 1, int(c + rx) + 2):
                if not (0 <= py < H and 0 <= px < W):
                    continue
                d = ((px - c) / max(rx, 1)) ** 2 + ((py - rr) / max(ry, 1)) ** 2
                if d > 1 or idx[py, px] >= 0:
                    continue
                if rng.random() < dens * (1 - d) ** 0.9 * 0.42:
                    n = 1 if rng.random() < 0.55 else (2 if rng.random() < 0.75 else 3)
                    q = rng.random()
                    cc = cols[0] if q < 0.5 else (cols[1] if q < 0.88 else cols[2])
                    for k in range(n):
                        if px + k < W and idx[py, px + k] < 0:
                            idx[py, px + k] = cc
                            mask[py, px + k] = True
    return mask


def contact_shadow(idx, pal, kid):
    fc, fr = rc((0, 0, STAND))
    cx, cy = int(round(fc - 0.5)), int(fr) + 1
    woods = [pal.index(c) for c in WOOD[2:]]
    for dy, hw in ((-1, 4), (0, 7), (1, 4)):
        for dx in range(-hw, hw):
            y, x = cy + dy, cx + dx
            if 0 <= y < H and 0 <= x < W and idx[y, x] in woods and kid[y, x] == "plank":
                idx[y, x] = pal.index(WOOD[1])


def lantern_light(idx, pal, kid):
    """The lantern lights the post, the arm and the right end of the planks around it (one warm level;
    LANTERN_LIGHT = (level, radius px): brighter and wider at night, none while the lantern is unlit)."""
    level, rad = LANTERN_LIGHT
    if level <= 0:
        return idx, pal
    lc, lr = rc(LANTERN)
    rows = np.arange(H)[:, None] + 0.5
    cols = np.arange(W)[None, :] + 0.5
    d = np.sqrt((cols - lc) ** 2 + ((rows - lr) * 0.8) ** 2)
    b = R.bayer(H, W)
    m = np.isin(kid, ["post", "plank", "pier"]) & (idx >= 0) & (d < rad + 10 * (b - 0.5))
    return R.blend_idx(idx, pal, m, LAMP[0], level, lighter=True, snap=0.05)


def front_reflections(refl):
    """Reflections of every front object standing in water -> back-layer overlay codes (as hyb_lake)."""
    mp = R.mirror_pass(refl, ids=True, tag=f"{SID}_frontmirror")
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
    code = np.where(a, np.where(lumv < 0.36, OV_DARK, OV_WM1), 0)
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


def render_front(rnd):
    C.clear_objects(keep_camera=True)
    R.reset_materials()
    refl = front_scene(rnd)
    PER.hook("front_scene", rnd=rnd, refl=refl, pr=PR, stand=STAND)
    P.setup_camera(STAND)
    bpy.context.view_layer.update()
    ps = R.render_passes(f"{SID}_front")
    pal = R.Pal(FRONT_PAL)
    kid = R.kind_map(ps)
    idx, solid = R.quantize(ps["rgb"], ps["a"], pal, dither=True)
    idx = R.remove_specks(idx, 3)
    thin = np.isin(kid, ["reed", "cattail", "moss"])
    idx = R.despeckle(idx, ps["id"], protect=~solid | thin, passes=1)
    idx = R.inner_lines(idx, pal, ps["id"], ps["depth"], thr=0.12, rel=0.02, steps=1)
    padm = shade_pads(idx, pal, ps)
    contact_shadow(idx, pal, kid)
    stamp_flowers(idx, pal)
    idx, pal = lantern_light(idx, pal, kid)
    # weak top rim on the OUTER silhouette of solid props (dull high key), before the outline
    solid_k = np.isin(kid, ["post", "plank", "pier", "trunk", "knee", "log", "lantern"])
    idx, pal = R.rim_light(idx, pal, PR, mask=solid_k, levels=1, snap=0.035)
    # selective outline for solid props only (hue-shifted darker neighbour, never ink); reeds, moss, pads,
    # duckweed and the lantern glass get none
    duck = stamp_duckweed(idx, pal, random.Random(19))
    noline = thin | padm | duck | (kid == "logmoss")
    base = np.where(noline, -1, idx)
    ol = R.outer_outline(base, pal, lit_steps=1, dark_steps=2, light=R.sun_side(PR))
    idx = np.where(noline & (idx >= 0), idx, ol)
    # ---- back-layer overlays: reflections, ripple dashes at stems / posts / trunks, dark line under pads
    ov = front_reflections(refl)
    rng = random.Random(5)
    for (x, y, rad) in CLUMP_BASES:
        c, r = rc((x, y, 0))
        depth = P.project((x, y, 0), STAND)[2]
        half = rad * P.F_PX / depth
        n = max(2, int(520 / depth * 0.22))
        for dr, sc in ((0, 1.0), (1, 0.6), (2, 1.25)):
            rr = int(r + dr)
            if not 0 <= rr < H or rng.random() < 0.3:
                continue
            for side in (-1, 1):
                c0 = c + side * (half * sc + rng.uniform(1, 3)) - (n if side < 0 else 0)
                for cc in range(int(c0), int(c0 + n)):
                    if 0 <= cc < W:
                        ov[rr, cc] = OV_RIPPLE
    below = np.zeros_like(padm)
    below[1:] = padm[:-1]
    ov[below & ~padm] = OV_PADLINE
    _OV["ov"] = ov
    np.save(os.path.join(R.WORK, "front_overlay.npy"), ov)
    idx, pal = PER.hook("front_post", idx, pal, kid=kid, ps=ps, pr=PR, stand=STAND, rc=rc)
    img = R.to_rgba(idx, pal)
    PER.save(img, SID, "front")
    pl = [R.lum(c) for c in LILY[1:]]
    print("HYB front colours", R.count_colours(img), "palette", len(pal), "pad/duckweed L min", round(min(pl), 3),
          "vs water L", round(R.lum(WB[3]), 3), round(R.lum(WB[4]), 3), round(R.lum(WB[5]), 3),
          "log L", round(R.lum(WOOD[3]), 3))


def main():
    C.reset_scene()
    which = PER.which()                      # back / front (+ --period <p> [--dry], see hyb_period)
    if "front" in which:
        render_front(random.Random(31))
    if "back" in which:
        render_back(random.Random(77))
    d = PER.stage_json(SID, PR, clouds=False)
    print("HYB stage json", {k: d[k] for k in ("standH", "waterTint", "waterDeep", "clouds", "fireflies")})
    print("HYB SWAMP done")


if __name__ == "__main__":
    main()
