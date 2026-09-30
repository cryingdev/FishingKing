"""
FishingKing - aquarium CLEANING art: the tank gets dirty over REAL time (algae film on the front glass, floating
debris, dirty gravel, murky water) and the player cleans it BY HAND with a sponge (glass), a net (floating debris)
and a gravel siphon (bottom dirt). All made in Blender: the tools are toon-shaded meshes (the items' outline, the
aquarium's upper-left light), the algae / dirt fields are Blender procedural noise / Voronoi renders that are then
painted into the aquarium's retro-16 pixel style (quarter-step alpha, horizontal dash dither, curated ramps), the FX /
debris are flat pixel cells rendered through Blender (as the aquafeed FX).

Run:  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python Tools/Blender/fk_aquaclean.py [-- dry]
      [-- overlays tools fx mock]   (default: everything; dry = into _tmp/aquadecor/clean/world|items only)
Review: _tmp/aquadecor/clean_sheet.png (every sprite zoomed), _tmp/aquadecor/clean_mock.png (the game's 480x270 view
        at 2x: filthy / cleaning in progress / clean).

Conventions (as AQUARIUM FEED / LIVE FOOD in fk_items.py): CENTRE pivot (PixelArtImporter, 16 PPU), even sizes,
offsets in sprite px from the centre, +x right, +y UP. Room image = the 640x400 aquarium_back/front (centred at world
(0, 0)): world x = (col - 320) / 16, world y = (200 - row) / 16.

OVERLAYS (Sprites/World) - pixel-exact to the room image; the reveal mask is a per-pixel array over the overlay:
  GLASS rect = room cols 102..538, rows 110..274 (the glass opening; the front frame covers everything outside it)
    -> 436x164, centre world (0, +0.5). Rows 0..6 of the sprite = the lamp-lit air gap, row 7 = the meniscus row 117.
    clean_algae_1..4   algae film on the front glass: 1 = first specks + faint haze in the lower corners, 2 = patches +
                       a waterline ring, 3 = most of the glass filmed + hair algae from the waterline, 4 = filthy.
                       NESTED: every pixel of stage k is also in k+1 (only thicker), so one mask wipes every stage.
    clean_algae_grow   the level (0..1, grey = R) at which each pixel first appears (alpha 0 = never): per-pixel
                       continuous growth -> show pixel p when its local level a(p) >= grow(p), with the colour of
                       stage max(ceil(4 a(p)), first stage containing p).
    clean_murk_1..3    murky water: an olive haze in 3 dash-dithered depth bands (stronger near the bottom), water rows
                       only (alpha 0 above the meniscus); continuous alpha (a colour grade, not a film).
  DIRT rect = room cols 102..538, rows 246..274 (the gravel bed, its top line 251..263) -> 436x28, centre world
    (0, -3.75).
    clean_dirt_1..3    dirty gravel (darker patches only on gravel pixels - never on the plants / stones) + detritus
                       (mulm clumps, waste strands, leaf bits, mouldy food) lying on the gravel top line. NESTED.
    clean_dirt_grow    as clean_algae_grow.
  Brushes (white, alpha = how clean one stamp makes it): clean_sponge_brush 22x14 (the pressed pad's footprint, 0.5
    fringe -> faint streaks until wiped twice), clean_siphon_brush 12x28 (a full-height column of the DIRT rect: stamp
    it with its centre at (mouth x, DIRT centre) while the mouth is within ~6 px of the gravel top).
DEBRIS (World, 8x8, 2 tumble frames each, flipX free): clean_debris_<kind>_f0/f1, kind = leaf (green trimming),
  deadleaf, fibre (pale curl), hair (algae thread tangle), mulm (fluffy brown), speck (dark motes), food (dissolving
  pellet mush), scale (fish scale, f1 glints), scum (surface foam, keep on the meniscus: centre row = surface).
TOOLS (World). On the ledge (outline bottom = the sprite's bottom row, as the bags -> centre = ledge point + (0, h/2
  px), the ledge = world y -5.25 = the top edge of room row 284; shadows centred on the ledge line); suggested
  ledge slots (room col): sponge 452, siphon 486, net 522 (the feed containers use 160 / 196 / 232 / 272).
  clean_sponge_rest 24x16, clean_sponge_shadow 24x4; clean_sponge_held 26x22 (lifted, 3/4 view);
  clean_sponge_wipe_f0..f2 26x22 (pressed flat on the glass: the green scrub pad face-on in a yellow foam rim; f0 level,
  f1 / f2 rolled -10 / +10 deg and squashed = the scrub wobble; same centre as held).
  clean_net_rest 20x44 (head down on the ledge, handle up), clean_net_shadow 16x4.
  clean_net_held, clean_net_sweep_f0/f1, clean_net_full 32x32: the head (frame centre = sprite centre + (0, -2)); the
  handle leaves the sprite top at x 0 -> continue it with clean_net_handle (4x16 tile, SpriteRenderer drawMode Tiled,
  width 0.25, centre x = the head's x) up past the screen top. sweep = moving RIGHT (flipX for left): the opening faces
  the motion, the bag trails and flutters; full = debris in the sagging bag.
  clean_siphon_rest 28x36, clean_siphon_shadow 28x4; clean_siphon_held, clean_siphon_work_f0..f2 16x32: the clear gravel
  tube (see-through, alpha 0.5 body), mouth centre = sprite centre + (0, -15); the hose leaves the top at x 0 ->
  clean_siphon_hose (4x16 tile, clear) / clean_siphon_hose_flow_f0..f3 (dirty water rising, loop ~0.08 s a frame).
  work = gravel tumbling in the tube + dirt rising; clean_siphon_suck_f0..f2 16x12 = the swirl at the mouth (its top
  row centre = the mouth: centre = mouth + (0, -6)).
FX (World): clean_squeak_f0..f2 12x12 (the squeaky sparkle at the sponge's trailing edge), clean_streak_f0..f2 24x16 (a
  shine sliding over freshly wiped glass), clean_burst_f0..f4 32x32 (the "clean!" burst: flash, star ring, bubbles).
ITEMS (Sprites/Items, 32x32 shop icons): tool_sponge, tool_net, tool_siphon.
"""
import os
import sys
import math
import random
import bmesh
import bpy
import numpy as np

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402
import fk_items as FI  # noqa: E402  (helpers only - its main() is not run)

ACL_OUT = os.path.join(C.TMP, "aquadecor")
ACL_WORK = os.path.join(ACL_OUT, "clean_work")          # own scratch (other groups render elsewhere)
ROOM_W, ROOM_H, PPU = 640, 400, 16
GLASS = (102, 538, 110, 274)                            # room cols c0..c1, rows r0..r1 (the glass opening)
DIRT = (102, 538, 246, 274)
SURF_ROW = 117                                          # the meniscus row
LEDGE_ROW = 284                                         # the cabinet top (the bags' base row)
SLOTS = dict(sponge=452, siphon=486, net=522)           # suggested ledge columns

# ------------------------------------------------------------------ palettes (the aquarium's hybrid ramps)
ALGAE = dict(
    g=[("#90a862", 0.25), ("#768c44", 0.5), ("#5c7234", 0.75)],        # green film: haze, film, thick
    b=[("#a4925e", 0.25), ("#8a784a", 0.5), ("#6e5c38", 0.75)],        # brown diatoms (lower glass)
    spot="#4a7438", crust=("#a6ae82", "#7c8c58"), hair=("#4a7a32", "#6e9c46"), fuzz=("#5e7a34", "#86a052"))
ALG_COV = [0.12, 0.34, 0.6, 0.86]                     # share of the water glass filmed per stage
ALG_T = (0.08, 0.3, 0.2)                                # rank depth -> film (0.5), thick (0.75, only where bias > [2])
SPOT_COV = [0.12, 0.22, 0.3, 0.36]                     # green spot algae: share of Voronoi cells with a spot
SPOT_R = [0.7, 0.95, 1.2, 1.4]                         # spot radius (px) per stage
MURK = [("#6a6a44", (0.06, 0.09, 0.13)), ("#66643e", (0.12, 0.17, 0.23)), ("#625e38", (0.2, 0.27, 0.34))]
MURK_BANDS = (160, 215)                                 # band boundaries (room rows) - dash-dithered +-3 rows
DIRT_COV = [0.3, 0.6, 0.9]
DIRT_T = (0.25, 0.7)                                    # -> 0.5, 0.75 darkening
DIRT_DARK = "#3a3222"
DET = dict(lit="#7c6c4a", body="#5a4c34", base="#453b2a", sunk="#3a3222", fluff="#6e5e40", waste="#3c3824",
           leaf=("#9a7236", "#6e5226"), food=("#b07a44", "#8a5a2c"), mould="#e4e0cc")
SPONGE = dict(foam="#f6c64a", pad="#3a9a56")
SPONGE_S = 1.4                                          # the in-game sponge scale (pressed pad ~17x10 px)
NET = dict(wire="#e4eaee", mesh="#46a672", mesh_in="#2c7a55", handle="#2f6fc4", ferrule="#c9d1da", cap="#24549a")
SIPH = dict(tube="#cdeef0", glass="#d8f4f4", glint="#f4fdfd", dirty="#aab494", rim="#6fa8b4", cap="#5f93a4",
            hose="#b4dac6", bulb="#3a78b4")
FXC = dict(W="#ffffff", C="#c8f4ff", Y="#ffe68a", B="#9ee0f0")
GRAVEL_TONES = ("#dcc49a", "#ba9f79", "#957c64", "#9b9d9d", "#7c858f")


def hexv(h):
    return np.array(C.hex_rgb(h), np.float64)


def acl_smooth(a, b, x):
    t = np.clip((np.asarray(x, np.float64) - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def acl_rank(f):
    """Values -> their rank in 0..1 (uniform spread: coverage thresholds become exact shares)."""
    f = np.asarray(f, np.float64)
    o = np.argsort(f, axis=None, kind="stable")
    r = np.empty(f.size)
    r[o] = np.arange(f.size) / max(1, f.size - 1)
    return r.reshape(f.shape)


def acl_dash(h, w, seed, lo=2, hi=6):
    """Retro-16 horizontal dashes: a (h, w) field constant along random 2..6 px runs of every row (a threshold field
    that makes every tone edge a dash edge, as the aquarium's water bands)."""
    rng = random.Random(seed)
    out = np.zeros((h, w))
    for r in range(h):
        c = -rng.randint(0, hi - 1)
        while c < w:
            L = rng.randint(lo, hi)
            out[r, max(0, c):max(0, c + L)] = rng.random()
            c += L
    return out


def acl_hash(h, w, seed):
    rows = np.arange(h, dtype=np.int64)[:, None]
    cols = np.arange(w, dtype=np.int64)[None, :]
    v = (cols * 73856093) ^ (rows * 19349663) ^ (seed * 83492791)
    return (v % 1000) / 1000.0


def acl_srgb_inv(s):
    return np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)


# ------------------------------------------------------------------ Blender procedural fields
def acl_field(w, h, tag, build):
    """A scalar procedural field (Blender shader nodes on a w x h plane, 1 unit = 1 px, sampled at the pixel centres)
    -> (h, w) float array, top row first (the Standard view's sRGB curve undone)."""
    C.clear_objects()
    m = bpy.data.materials.new("Fld" + tag)
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    v = build(nb, tc.outputs["Object"])
    nb.output_emission(nb.combine(v, v, v), 1.0)
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in ((0, 0, 0), (w, 0, 0), (w, 0, h), (0, 0, h))]
    bm.faces.new(vs)
    C.mesh_object("Fld", bm, m)
    bpy.context.view_layer.update()
    C.ortho_camera(w / 2.0, h / 2.0, w, h, 1.0)
    p = os.path.join(ACL_WORK, "fld_" + tag + ".png")
    C.render_raw(p)
    a = C.load_pixels(p)[::-1, :, 0].astype(np.float64)
    return acl_srgb_inv(a)


def fn_noise(sx, sy, detail=2.0, rough=0.5, w=0.0):
    """Noise texture (4D, W decorrelates fields) stretched sx x sy px."""
    def build(nb, co):
        mp = nb.node("ShaderNodeMapping")
        nb.link(co, mp.inputs["Vector"])
        mp.inputs["Scale"].default_value = (1.0 / sx, 1.0, 1.0 / sy)
        n = nb.node("ShaderNodeTexNoise")
        n.noise_dimensions = "4D"
        nb.link(mp.outputs["Vector"], n.inputs["Vector"])
        n.inputs["W"].default_value = w
        n.inputs["Scale"].default_value = 1.0
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough
        return n.outputs["Fac"]
    return build


def fn_voro(s, ofs=(0.0, 0.0), out="dist"):
    """2D Voronoi F1 of s px cells (the plane's x-z turned into the texture's x-y): out = "dist" (distance / 2, cell
    units) or "col" (the cell's random value)."""
    def build(nb, co):
        mp = nb.node("ShaderNodeMapping")
        nb.link(co, mp.inputs["Vector"])
        mp.inputs["Scale"].default_value = (1.0 / s, 1.0, 1.0 / s)
        mp.inputs["Rotation"].default_value = (math.radians(90.0), 0.0, 0.0)
        mp.inputs["Location"].default_value = (ofs[0], ofs[1], 0.0)
        vo = nb.node("ShaderNodeTexVoronoi")
        vo.voronoi_dimensions = "2D"
        vo.feature = "F1"
        nb.link(mp.outputs["Vector"], vo.inputs["Vector"])
        vo.inputs["Scale"].default_value = 1.0
        vo.inputs["Randomness"].default_value = 1.0
        if out == "dist":
            return nb.math("MULTIPLY", vo.outputs["Distance"], 0.5)
        sep = nb.node("ShaderNodeSeparateColor")
        nb.link(vo.outputs["Color"], sep.inputs[0])
        return sep.outputs[0]
    return build


def acl_grow(stages, cont, seed):
    """Grow map: the level (0..1) at which each pixel first shows. Pixels that first show through the continuous film
    (cont = its level) keep it; the rest (spots, crust, strands, detritus) appear at a hashed moment inside the band of
    the first stage that contains them. -> RGBA top-down (grey = level, alpha 0 = never)."""
    n = len(stages)
    h, w = stages[0].shape[:2]
    first = np.full((h, w), 99)
    for k in range(n - 1, -1, -1):
        first[stages[k][..., 3] > 0] = k
    lo, hi = first / float(n), (first + 1) / float(n)
    u = acl_hash(h, w, seed)
    g = np.where((cont > lo) & (cont <= hi + 1e-9), cont, lo + (0.15 + 0.8 * u) / n)
    # 8-bit exact: the byte of a pixel first shown by stage k (1-based) lies in (round(255 (k-1) / n), round(255 k / n)]
    edges = np.round(255.0 * np.arange(n + 1) / n)
    f = np.clip(first, 0, n - 1)
    byte = np.clip(np.round(g * 255.0), edges[f] + 1, edges[f + 1])
    out = np.zeros((h, w, 4))
    have = first < 99
    out[have, 0] = out[have, 1] = out[have, 2] = byte[have] / 255.0
    out[have, 3] = 1.0
    return out


def acl_put(img, sel, col, a):
    img[sel, :3] = hexv(col)
    img[sel, 3] = a


# ------------------------------------------------------------------ ALGAE (front glass)
def acl_algae():
    c0, c1, r0, r1 = GLASS
    w, h = c1 - c0, r1 - r0
    rows = (np.arange(h) + r0)[:, None] * np.ones((1, w))
    cols = np.ones((h, 1)) * (np.arange(w) + c0)[None, :]
    n1 = acl_rank(acl_field(w, h, "alg_blot", fn_noise(40, 24, 3.0, 0.55, 0.37)))
    n2 = acl_rank(acl_field(w, h, "alg_grain", fn_noise(7, 5, 2.0, 0.6, 5.13)))
    n3 = acl_rank(acl_field(w, h, "alg_diatom", fn_noise(70, 26, 2.0, 0.5, 9.71)))
    n4 = acl_rank(acl_field(w, h, "alg_mottle", fn_noise(4, 3, 2.0, 0.6, 13.3)))
    vd = acl_field(w, h, "alg_vd", fn_voro(13.0, (3.3, 7.1), "dist")) * 2.0 * 13.0
    vc = acl_field(w, h, "alg_vc", fn_voro(13.0, (3.3, 7.1), "col"))
    water = rows >= SURF_ROW
    wr = np.clip((rows - SURF_ROW) / float(r1 - 1 - SURF_ROW), 0.0, 1.0)
    edge = np.minimum(cols - c0, c1 - 1 - cols)
    # algae likes the bottom, the side panes' corners and the band just under the waterline; the middle clears last
    bias = (0.3 * acl_smooth(0.3, 1.0, wr) + 0.24 * np.clip(1.0 - edge / 64.0, 0.0, 1.0) ** 2
            + 0.18 * np.exp(-((rows - SURF_ROW - 3) / 5.0) ** 2)
            - 0.1 * np.exp(-((cols - 320) / 110.0) ** 2 - ((rows - 175) / 45.0) ** 2))
    F = 0.6 * n1 + 0.2 * n2 + bias
    fr = np.full((h, w), -1.0)
    fr[water] = acl_rank(F[water])
    jit = (acl_dash(h, w, 71) - 0.5) * 0.04
    brown = (0.65 * n3 + 0.45 * acl_smooth(0.45, 1.0, wr)) > 0.6
    cd = acl_dash(h, w, 83, 2, 7)
    crust = {1: [(116, 0.35, 0, 0.5)],
             2: [(116, 0.12, 0, 0.75), (115, 0.6, 0, 0.5), (117, 0.7, 1, 0.5)],
             3: [(116, 0.0, 0, 0.75), (115, 0.3, 0, 0.5), (114, 0.7, 0, 0.25), (117, 0.35, 1, 0.75)]}
    rng = random.Random(97)
    strands = []
    for q in range(46):
        strands.append((rng.randint(3, w - 4), rng.randint(5, 15), 2 if rng.random() < 0.5 else 3,
                        rng.uniform(0, 6.28), rng.choice((0.0, 0.7, 1.2)), "hair", SURF_ROW + 1, 1))
    for q in range(70):
        strands.append((rng.randint(2, w - 3), rng.randint(2, 5), 2 if rng.random() < 0.4 else 3,
                        rng.uniform(0, 6.28), rng.choice((0.0, 0.6)), "fuzz", r1 - 1, -1))
    stages = []
    for k in range(4):
        img = np.zeros((h, w, 4))
        d = fr + jit - (1.0 - ALG_COV[k])
        vis = water & (d > 0)
        tone = np.where((d > ALG_T[1]) & (bias + 0.3 * (n2 - 0.5) > ALG_T[2]), 2, np.where(d > ALG_T[0], 1, 0))
        tone = np.where((n4 > 0.84) & (tone >= 1), tone - 1, tone)          # mottle: lighter flecks in the film
        for fam, m in (("g", ~brown), ("b", brown)):
            for t in range(3):
                col, a = ALGAE[fam][t]
                acl_put(img, vis & m & (tone == t), col, a)
        sp = water & (rows > SURF_ROW + 1) & (vc < SPOT_COV[k]) & (vd < SPOT_R[k])
        acl_put(img, sp, ALGAE["spot"], 0.75)
        for kk in range(1, k + 1):
            for (row, thr, ci, a) in crust.get(kk, []):
                r = row - r0
                sel = np.zeros((h, w), bool)
                sel[r] = cd[r] > thr
                sel &= img[..., 3] <= a
                acl_put(img, sel, ALGAE["crust"][ci], a)
        for (x, L, st, ph, amp, kind, rstart, step) in strands:
            if k < st:
                continue
            n = L if k == 3 else (L + 1) // 2
            dark, tip = ALGAE[kind]
            for i in range(n):
                rr = rstart - r0 + step * i
                cc = x + int(round(amp * math.sin(ph + i * 0.55)))
                if 0 <= rr < h and 0 <= cc < w:
                    last = i >= n - 2
                    a = (0.75 if last else 1.0) if k == 3 else (0.5 if last else 0.75)
                    img[rr, cc, :3] = hexv(tip if last else dark)
                    img[rr, cc, 3] = max(a, img[rr, cc, 3])
        stages.append(img)
    cov = [0.0] + ALG_COV
    cont = np.interp(1.0 - fr - jit, cov, [0.0, 0.25, 0.5, 0.75, 1.0])
    cont[~water] = 9.0
    return stages, acl_grow(stages, cont, 5)


# ------------------------------------------------------------------ MURK (water haze)
def acl_murk():
    c0, c1, r0, r1 = GLASS
    w, h = c1 - c0, r1 - r0
    rows = (np.arange(h) + r0)[:, None] * np.ones((1, w))
    dsh = acl_dash(h, w, 131, 3, 9)
    band = np.zeros((h, w), int)
    for b in MURK_BANDS:
        t = np.clip((rows - (b - 3)) / 6.0, 0.0, 1.0)               # 0 above the dithered zone .. 1 below
        band += (dsh < t).astype(int)
    out = []
    for col, alphas in MURK:
        img = np.zeros((h, w, 4))
        img[..., :3] = hexv(col)
        img[..., 3] = np.choose(band, alphas)
        img[rows < SURF_ROW] = 0.0
        out.append(img)
    return out


# ------------------------------------------------------------------ DIRT (gravel bed)
def acl_gtop():
    """The gravel top line of hyb_aquarium.py (room row per column)."""
    def ztop(x):
        a = abs(x)
        s = min(1.0, max(0.0, (a - 8.0) / 5.6))
        return -3.72 + 0.16 * math.sin(x * 0.55 + 0.7) + 0.08 * math.sin(x * 1.7 + 2.0) + 0.38 * s * s * (3 - 2 * s)
    return np.array([int(math.ceil((ROOM_H / 2 - PPU * ztop((c + 0.5 - ROOM_W / 2) / PPU)) - 0.5)) for c in range(ROOM_W)])


def acl_room(name):
    return FI.afd_td(os.path.join(C.SPRITES, "Stages", name))


def acl_dirt():
    c0, c1, r0, r1 = DIRT
    w, h = c1 - c0, r1 - r0
    back = acl_room("aquarium_back.png")[r0:r1, c0:c1]
    rgb = np.round(back[..., :3] * 255).astype(int)
    rr, gg, bb = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    plant = (gg > rr) & (gg >= bb)
    gt = acl_gtop()[c0:c1]
    rows = (np.arange(h) + r0)[:, None] * np.ones((1, w))
    cols = np.ones((h, 1)) * (np.arange(w) + c0)[None, :]
    below = rows >= gt[None, :]
    gravel = below & ~plant
    n1 = acl_rank(acl_field(w, h, "dirt_blot", fn_noise(26, 4, 3.0, 0.55, 2.2)))
    n2 = acl_rank(acl_field(w, h, "dirt_grain", fn_noise(4, 2, 2.0, 0.6, 7.7)))
    edge = np.minimum(cols - c0, c1 - 1 - cols)
    bases = (133, 165, 180, 216, 358, 390, 438, 501, 528)           # rocks, driftwood foot, air stone, plants
    bump = sum(np.exp(-((cols - b) / 12.0) ** 2) for b in bases)
    top = np.clip(1.0 - (rows - gt[None, :]) / 5.0, 0.0, 1.0)      # mulm settles on the surface first
    bias = 0.22 * np.clip(1.0 - edge / 50.0, 0.0, 1.0) ** 2 + 0.14 * bump + 0.1 * (gt[None, :] - 251) / 12.0 + 0.12 * top
    F = 0.6 * n1 + 0.25 * n2 + bias
    fr = np.full((h, w), -1.0)
    fr[gravel] = acl_rank(F[gravel])
    jit = (acl_dash(h, w, 41, 2, 5) - 0.5) * 0.05
    # detritus items on the gravel top line (appear stage, pixels relative to (col, surface row))
    rng = random.Random(59)
    items, used = [], np.zeros(w, bool)
    plan = [(0, "mulm", 9), (1, "mulm", 12), (1, "waste", 6), (1, "leaf", 4), (2, "mulm", 16), (2, "waste", 8),
            (2, "leaf", 6), (2, "food", 5)]
    for st, kind, n in plan:
        tries = 0
        while n > 0 and tries < 4000:
            tries += 1
            x = rng.randint(3, w - 9)
            wd = rng.randint(3, 6) if kind == "mulm" else 5 if kind == "waste" else 3 if kind == "leaf" else 2
            if used[max(0, x - 2):x + wd + 2].any():
                continue
            s = gt[x] - r0
            if not (gravel[s, x] and gravel[s, min(w - 1, x + wd - 1)]):
                continue
            if plant[max(0, s - 3):s, x:x + wd].any():
                continue
            used[x:x + wd] = True
            n -= 1
            px = []
            if kind == "mulm":
                for i in range(wd):
                    px.append((s + 1 - (1 if i in (0, wd - 1) else 0), x + i, DET["base"], 1.0))
                    if 0 < i < wd - 1:
                        px.append((s, x + i, DET["body"], 1.0))
                        px.append((s + 1, x + i, DET["sunk"], 0.5))
                    if 0 < i < wd - 1 and wd >= 4:
                        px.append((s - 1, x + i, DET["lit"] if i == 1 else DET["body"], 1.0 if i < wd - 2 else 0.75))
                if wd >= 5:
                    px.append((s - 2, x + wd // 2, DET["fluff"], 0.75))
            elif kind == "waste":
                dy = rng.choice((0, 1))
                for i in range(wd):
                    px.append((s + dy + (1 if i >= 3 else 0), x + i, DET["waste"], 1.0 if i < wd - 1 else 0.5))
            elif kind == "leaf":
                px += [(s, x, DET["leaf"][0], 1.0), (s, x + 1, DET["leaf"][0], 1.0), (s, x + 2, DET["leaf"][1], 1.0),
                       (s - 1, x + 1, DET["leaf"][0], 0.75)]
            else:
                px += [(s - 1, x, DET["food"][0], 1.0), (s - 1, x + 1, DET["food"][1], 1.0), (s, x, DET["food"][1], 1.0),
                       (s, x + 1, DET["food"][1], 1.0), (s - 2, x, DET["mould"], 0.5), (s - 2, x + 1, DET["mould"], 0.5),
                       (s - 1, x - 1, DET["mould"], 0.5), (s - 1, x + 2, DET["mould"], 0.5), (s - 3, x + 1, DET["mould"], 0.25)]
            items.append((st, [(r, c, col, a) for r, c, col, a in px if 0 <= r < h and 0 <= c < w and not plant[r, c]]))
    stages = []
    for k in range(3):
        img = np.zeros((h, w, 4))
        d = fr + jit - (1.0 - DIRT_COV[k])
        vis = gravel & (d > 0)
        tone = np.where(d > DIRT_T[1], 0.75, np.where(d > DIRT_T[0], 0.5, 0.25))
        img[vis, :3] = hexv(DIRT_DARK)
        img[vis, 3] = tone[vis]
        for st, px in items:
            if st <= k:
                for r, c, col, a in px:
                    img[r, c, :3] = hexv(col)
                    img[r, c, 3] = a
        stages.append(img)
    cont = np.interp(1.0 - fr - jit, [0.0] + DIRT_COV, [0.0, 1 / 3.0, 2 / 3.0, 1.0])
    cont[~gravel] = 9.0
    return stages, acl_grow(stages, cont, 9)


# ------------------------------------------------------------------ pixel cells (FX, debris, brushes, shadows)
_ACL_Z = [0]


def acl_flat(col):
    name = "ACL" + FI.afd_hex(col)
    m = bpy.data.materials.get(name)
    if m is None:
        m = C.glow_material(name, FI.afd_hex(col), 1.0)
        m.name = name
    return m


def acl_px(w, h, parts, tag):
    """parts = [(cells [(i, j) from the bottom-left], colour, alpha)] -> RGBA bottom-up (colour pass + alpha pass,
    alpha in quarter steps); later parts paint over earlier ones (as fk_items afd_px, own scratch folder)."""
    C.clear_objects()
    for cells, col, a in parts:
        cells = sorted(set((int(round(i)), int(round(j))) for i, j in cells if 0 <= i < w and 0 <= j < h))
        if not cells:
            continue
        bm = bmesh.new()
        for i, j in cells:
            v = [bm.verts.new((i, 0, j)), bm.verts.new((i + 1, 0, j)), bm.verts.new((i + 1, 0, j + 1)),
                 bm.verts.new((i, 0, j + 1))]
            bm.faces.new(v)
        ob = C.mesh_object("px", bm, acl_flat(col))
        _ACL_Z[0] += 1
        ob.location.y = -0.002 * _ACL_Z[0]
        ob["a"] = float(a)
    bpy.context.view_layer.update()
    cw, chh = max(w, 4), max(h, 4)
    C.ortho_camera(cw / 2.0, chh / 2.0, cw, chh, 1.0)
    p = os.path.join(ACL_WORK, tag + "_c.png")
    C.render_raw(p)
    col = C.load_pixels(p)
    for o in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        g = float(o.get("a", 1.0))
        for s in o.material_slots:
            s.material = acl_flat((g, g, g))
    p = os.path.join(ACL_WORK, tag + "_a.png")
    C.render_raw(p)
    am = C.load_pixels(p)
    col, am = col[:h, :w], am[:h, :w]
    out = np.zeros_like(col)
    cov = col[..., 3] > 0.5
    out[..., :3] = col[..., :3]
    out[..., 3] = np.where(cov, np.round(np.clip(am[..., 0], 0, 1) * 4) / 4, 0.0)
    out[out[..., 3] <= 0] = 0.0
    return out


def acl_grid(rows, pal, dx=0, dy=0):
    """ASCII pixel art (top row first, '.' = none) -> parts; pal: char -> (colour, alpha)."""
    h = len(rows)
    groups = {}
    for r, row in enumerate(rows):
        for c, ch in enumerate(row):
            if ch in pal:
                groups.setdefault(ch, []).append((c + dx, h - 1 - r + dy))
    return [(cells, pal[ch][0], pal[ch][1]) for ch, cells in groups.items()]


def acl_shadow(w):
    S = "#1a0e14"
    parts = [([(i, 3) for i in range(2, w - 2)], S, 0.25), ([(i, 2) for i in range(1, w - 1)], S, 0.5),
             ([(i, 1) for i in range(3, w - 3)], S, 0.5), ([(i, 0) for i in range(6, w - 6)], S, 0.25)]
    return acl_px(w, 4, parts, f"shadow{w}")


DEBRIS_PAL = {"g": ("#7cb45a", 1.0), "G": ("#4e8e40", 1.0), "d": ("#336a30", 1.0),
              "b": ("#b88a48", 1.0), "B": ("#8e6832", 1.0), "e": ("#634822", 1.0),
              "f": ("#e2dcc6", 0.75), "F": ("#b4ae96", 0.5),
              "h": ("#4a7a32", 1.0), "H": ("#6e9c46", 0.75),
              "m": ("#8a7a56", 0.5), "M": ("#5e4e36", 1.0), "n": ("#43382a", 1.0),
              "s": ("#3a3226", 1.0), "t": ("#5a5040", 0.75), "u": ("#5a5040", 0.5),
              "o": ("#d8b888", 0.5), "O": ("#a06a3a", 1.0), "P": ("#7a4a24", 1.0),
              "c": ("#a8bcc6", 1.0), "C": ("#dce8ee", 1.0), "x": ("#ffffff", 1.0), "y": ("#dce8ee", 0.5),
              "w": ("#c8e0d8", 0.5), "W": ("#f2f8f4", 0.75)}
DEBRIS = {
    "leaf": (["........", "........", "...gG...", "..gGGG..", ".gGGGd..", "..Gdd...", "........", "........"],
             ["........", "........", "........", "..gGGGd.", "...ddd..", "........", "........", "........"],
             "a green plant trimming (flat / turned edge-on)"),
    "deadleaf": (["........", "........", "..bB....", "..BBBb..", "...BBBe.", "....ee..", "........", "........"],
                 ["........", "........", "....b...", "...bBe..", "..BBBe..", "..Be....", "........", "........"],
                 "a curled dead leaf bit"),
    "fibre": (["........", "......f.", ".....f..", "..fff...", ".f......", "..F.....", "........", "........"],
              ["........", "........", ".f....F.", "..f..f..", "...ff...", "........", "........", "........"],
              "a pale curly fibre"),
    "hair": (["........", "..h.....", "...h.H..", "...hh...", "..h.hh..", ".H....H.", "........", "........"],
             ["........", "....H...", ".h..h...", "..hhh...", "..h..h..", ".H...H..", "........", "........"],
             "a tangle of hair algae thread"),
    "mulm": (["........", "........", "...m....", "..mMm...", "..MMnm..", "...nm...", "........", "........"],
             ["........", "........", "....m...", "..mMM...", "..MnMm..", "...mn...", "........", "........"],
             "a fluffy brown mulm clump"),
    "speck": (["........", "........", ".....u..", "..t.....", "..s.....", "......s.", "....u...", "........"],
              ["........", "......u.", "........", "...s....", "...t..s.", "........", "..u.....", "........"],
              "dark motes (use also as suspended particles when murky)"),
    "food": (["........", "........", "...o....", "..oOo...", "..OPo...", "...o....", "........", "........"],
             ["........", "........", "....o...", "..oOo...", "...PPo..", "....o...", "........", "........"],
             "dissolving pellet mush (drop from uneaten food)"),
    "scale": (["........", "........", "...c....", "..cCc...", "...cc...", "........", "........", "........"],
              ["........", "........", "...x....", "..xCc...", "...cy...", "....y...", "........", "........"],
              "a shed fish scale (f1 glints)"),
    "scum": (["........", "........", "........", ".w.ww...", "wWwWWw..", ".wwww.w.", "........", "........"],
             ["........", "........", "........", "..ww.w..", ".wWWwWw.", "w.wwww..", "........", "........"],
             "surface foam scum (ride the meniscus: sprite row 4 = the surface row)"),
}


def fx_star(cx, cy, arm, a=1.0, cols=("W", "C"), diag=0, dcol="C"):
    parts = [([(cx, cy)], FXC[cols[0]], a)]
    for d in range(1, arm + 1):
        col = FXC[cols[0]] if d <= (arm + 1) // 2 else FXC[cols[1]]
        aa = a if d < arm or arm == 1 else max(0.25, a - 0.25)
        parts.append(([(cx + d, cy), (cx - d, cy), (cx, cy + d), (cx, cy - d)], col, aa))
    for d in range(1, diag + 1):
        parts.append(([(cx + d, cy + d), (cx - d, cy + d), (cx + d, cy - d), (cx - d, cy - d)], FXC[dcol],
                      max(0.25, a - 0.25 * d)))
    return parts


def fx_bubble(cx, cy, a=0.75):
    return [([(cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)], FXC["B"], a), ([(cx - 1, cy + 1)], FXC["W"], a)]


def acl_fx():
    out = {}
    sq = [fx_star(6, 6, 1) + [([(9, 9)], FXC["C"], 0.5), ([(2, 4), (3, 4)], FXC["W"], 0.5)],
          fx_star(6, 6, 2, diag=1) + [([(10, 10)], FXC["W"], 0.75), ([(2, 9)], FXC["C"], 0.5),
                                      ([(1, 3), (2, 3)], FXC["W"], 0.5)],
          fx_star(6, 7, 2, a=0.5, cols=("C", "C")) + [([(10, 11)], FXC["C"], 0.5)]]
    for f, p in enumerate(sq):
        out[f"clean_squeak_f{f}"] = (acl_px(12, 12, p, f"squeak{f}"),
                                     ["squeak: first glint", "squeak: star + flecks", "squeak: fading"][f])

    def slash(x0, y0, n, col, a):
        return ([(x0 + i, y0 + i) for i in range(n)], col, a)
    st = [[slash(3, 2, 10, FXC["W"], 0.75), slash(8, 2, 5, FXC["W"], 0.5), slash(4, 2, 9, FXC["C"], 0.25)],
          [slash(6, 2, 10, FXC["W"], 0.75), slash(11, 2, 5, FXC["W"], 0.5), slash(7, 2, 9, FXC["C"], 0.25)]
          + fx_star(16, 12, 1, a=0.75),
          [slash(9, 2, 10, FXC["C"], 0.5), slash(14, 2, 5, FXC["C"], 0.25)]]
    for f, p in enumerate(st):
        out[f"clean_streak_f{f}"] = (acl_px(24, 16, p, f"streak{f}"),
                                     ["glass shine appears", "shine slides + glint", "shine fades"][f])
    cx = cy = 16
    ring = lambda r, n, rot: [(cx + r * math.cos(math.radians(rot + 360.0 * q / n)),
                               cy + r * math.sin(math.radians(rot + 360.0 * q / n))) for q in range(n)]
    bu = [[], fx_bubble(9, 21, 0.5) + fx_bubble(23, 19, 0.5), fx_bubble(8, 24) + fx_bubble(24, 23) + fx_bubble(13, 26, 0.5),
          fx_bubble(8, 27, 0.5) + fx_bubble(24, 27, 0.5) + fx_bubble(13, 29, 0.5), fx_bubble(23, 30, 0.25)]
    fr = [fx_star(cx, cy, 4, diag=2),
          fx_star(cx, cy, 6, diag=3, cols=("W", "Y")) + sum((fx_star(int(round(x)), int(round(y)), 1, 0.75, ("Y", "W"))
                                                             for x, y in ring(9, 4, 45)), []),
          fx_star(cx, cy, 3, a=0.75, cols=("W", "Y"), diag=1)
          + sum((fx_star(int(round(x)), int(round(y)), 1, 1.0, ("W" if q % 2 else "Y", "C"))
                 for q, (x, y) in enumerate(ring(12, 6, 15))), []),
          fx_star(cx, cy, 1, a=0.5, cols=("Y", "Y"))
          + sum((fx_star(int(round(x)), int(round(y)), 1, 0.5, ("W", "C")) for x, y in ring(14, 6, 35)), []),
          [([(int(round(x)), int(round(y)))], FXC["W"], 0.5) for x, y in ring(15, 5, 60)]]
    names = ["flash", "big star + gold ring", "star ring spreads + bubbles", "sparkles fade out", "last twinkles"]
    for f in range(5):
        out[f"clean_burst_f{f}"] = (acl_px(32, 32, fr[f] + bu[f], f"burst{f}"), '"clean!" burst: ' + names[f])
    return out


def acl_brushes():
    sp = [([(i, j) for i in range(2, 20) for j in range(1, 13)], "#ffffff", 1.0),
          ([(1, j) for j in range(2, 12)] + [(20, j) for j in range(2, 12)] + [(i, 0) for i in range(3, 19)]
           + [(i, 13) for i in range(3, 19)], "#ffffff", 0.5)]
    si = [([(i, j) for i in range(2, 10) for j in range(28)], "#ffffff", 1.0),
          ([(1, j) for j in range(28)] + [(10, j) for j in range(28)], "#ffffff", 0.5),
          ([(0, j) for j in range(28)] + [(11, j) for j in range(28)], "#ffffff", 0.25)]
    return {"clean_sponge_brush": (acl_px(22, 14, sp, "brush_sponge"), "sponge mask stamp (1 core, 0.5 fringe)"),
            "clean_siphon_brush": (acl_px(12, 28, si, "brush_siphon"), "siphon mask stamp (full DIRT height)")}


# ------------------------------------------------------------------ toon tools (Blender meshes)
def acl_render(cw, ch, tag, outline=True, ss=4):
    bpy.context.view_layer.update()
    C.ortho_camera(cw / 2.0, ch / 2.0, cw * ss, ch * ss, float(ss))
    p = os.path.join(ACL_WORK, tag + ".png")
    C.render_raw(p)
    raw = C.load_pixels(p)
    if ss > 1:
        raw = FI.afd_majority(raw, ss)
    return C.pixelize(raw, outline=outline)


def acl_ids(cw, ch, tag, groups, ss=4):
    """{gid: [objects]} -> (ch, cw) int map bottom-up: the front-most group per pixel (0 = other, -1 = none)."""
    saved = []
    for ob in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        saved.append((ob, [s.material for s in ob.material_slots]))
        gid = next((g for g, objs in groups.items() if ob in objs), 0)
        m = acl_flat("#%02x0000" % (gid * 16))
        for s in ob.material_slots:
            s.material = m
    raw = acl_render(cw, ch, tag + "_ids", outline=False, ss=ss)
    for ob, mats in saved:
        for s, m in zip(ob.material_slots, mats):
            s.material = m
    ids = np.round(raw[..., 0] * 255 / 16.0).astype(int)
    ids[raw[..., 3] < 0.5] = -1
    return ids


def acl_meshes():
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def acl_fit(root, cw, ch, base=None):
    """Move the root so the meshes are centred in the canvas (base: their bottom at that canvas row instead)."""
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(acl_meshes())
    root.location.x += cw / 2.0 - (x0 + x1) / 2.0
    root.location.z += (ch / 2.0 - (z0 + z1) / 2.0) if base is None else (base - z0)
    bpy.context.view_layer.update()


def acl_bag_mat(name, out_col, in_col):
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    base = nb.mix(geo.outputs["Backfacing"], C.lin(out_col), C.lin(in_col))
    shade = C.toon_shade(nb, 0.0)
    nb.output_emission(nb.mix(1.0, base, shade, "MULTIPLY"), 1.0)
    return m


def acl_dot(arr, x, z, col, a=1.0):
    if 0 <= z < arr.shape[0] and 0 <= x < arr.shape[1]:
        arr[z, x, :3] = C.hex_rgb(col) if isinstance(col, str) else col
        arr[z, x, 3] = a


# ---- sponge
def acl_sponge(root, press=False, squash=1.0):
    fm = FI.M("SpFoam", SPONGE["foam"], shine=0.3)
    pm = FI.M("SpPad", SPONGE["pad"], shine=0.15)
    if press:           # the pad face-on (pressed on the glass), the foam bulging round it behind
        foam = FI.box("Foam", fm, 14.2 * squash, 3.6, 9.2 / squash, loc=(0, 2.4, 0), bevel=1.5, seg=3)
        pad = FI.box("Pad", pm, 12.2 * squash, 1.4, 7.2 / squash, loc=(0, 0.2, 0), bevel=0.35, seg=2)
    else:
        foam = FI.box("Foam", fm, 13.0, 6.0, 5.6, loc=(0, 0, 2.3 + 2.8), bevel=1.3, seg=3)
        pad = FI.box("Pad", pm, 13.0, 6.0, 2.3, loc=(0, 0, 1.15), bevel=0.5, seg=2)
    FI.alf_adopt([foam, pad], root)
    return foam, pad


def acl_sponge_texture(arr, ids, pressed):
    h, w = ids.shape
    for z in range(1, h - 1):
        for x in range(1, w - 1):
            inner = all(ids[z + dz, x + dx] >= 1 for dz, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if ids[z, x] == 1 and inner and (x * 3 + z * 5) % 7 == 0:
                arr[z, x, :3] *= 0.8                                   # foam pores
            if ids[z, x] == 2 and inner and pressed:
                if (x * 5 + z * 3) % 5 == 0:
                    arr[z, x, :3] = np.minimum(1.0, arr[z, x, :3] * 1.22)   # scrub fibres
                elif (x + 2 * z) % 7 == 3:
                    arr[z, x, :3] *= 0.84
    return arr


def acl_sponge_sprite(pose, cw, ch, tag, scale=1.0, sparkle=False):
    C.clear_objects()
    press = pose.startswith("wipe")
    rot = dict(rest=(24, 0, -8), held=(30, 8, -24), icon=(30, 10, -22), wipe0=(0, 0, 0), wipe1=(0, -10, 0),
               wipe2=(0, 10, 0))[pose]
    root = FI.alf_empty("Root", loc=(cw / 2.0, 0.0, ch / 2.0), rot=tuple(math.radians(a) for a in rot), scale=scale)
    squash = 1.0 if pose in ("wipe0",) else 1.06
    foam, pad = acl_sponge(root, press, squash if press else 1.0)
    acl_fit(root, cw, ch, base=1.0 if pose == "rest" else None)
    ids = acl_ids(cw, ch, tag, {1: [foam], 2: [pad]})
    arr = acl_render(cw, ch, tag)
    arr = acl_sponge_texture(arr, ids, press)
    if sparkle:
        for x, z, col in ((26, 26, "#ffffff"), (27, 26, "#fff4b0"), (25, 26, "#fff4b0"), (26, 27, "#fff4b0"),
                          (26, 25, "#fff4b0"), (26, 28, "#ffe68a"), (26, 24, "#ffe68a"), (28, 26, "#ffe68a"),
                          (24, 26, "#ffe68a"), (22, 29, "#ffffff"), (29, 21, "#fff4b0")):
            acl_dot(arr, x, z, col)
    return arr


# ---- net
def acl_rrect(w, h, r, n=6):
    pts = []
    for cx, cz, a0 in ((w / 2 - r, -h / 2 + r, -90), (w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90),
                       (-w / 2 + r, -h / 2 + r, 180)):
        for k in range(n + 1):
            a = math.radians(a0 + 90.0 * k / n)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


NET_FW, NET_FH = 15.0, 11.0                           # the frame (px)


def acl_net(root, by, bz, flutter=0.0, bulge=0.0, handle=20.0, cap=False):
    """Net head in local px: the frame centred on the origin facing -y (the camera), the bag behind (+y) sagging by
    bz (down), the handle up from the frame top (neck, ferrule, handle to local z = 9.5 + handle)."""
    wire = FI.M("NetWire", NET["wire"], shine=0.8)
    pts = acl_rrect(NET_FW, NET_FH, 2.6)
    frame = C.tube_along("Frame", [(x, 0.0, z) for x, z in pts + [pts[0]]], 0.55, wire, 6)
    neck = C.tube_along("Neck", [(0, 0, NET_FH / 2), (0, 0, NET_FH / 2 + 2.2)], 0.5, wire, 6)
    fer = C.tube_along("Ferrule", [(0, 0, NET_FH / 2 + 2.0), (0, 0, NET_FH / 2 + 4.0)], 1.3,
                       FI.M("NetFer", NET["ferrule"], shine=0.9), 12)
    hmat = FI.M("NetHandle", NET["handle"], shine=0.5)
    hz = NET_FH / 2 + 4.0
    hnd = C.tube_along("Handle", [(0, 0, hz), (0, 0, hz + handle)], 1.0, hmat, 12)
    parts = [frame, neck, fer, hnd]
    if cap:
        parts.append(C.tube_along("Cap", [(0, 0, hz + handle - 0.2), (0, 0, hz + handle + 1.6)], [1.25, 0.9],
                                  FI.M("NetCap", NET["cap"], shine=0.4), 12))
    bm = bmesh.new()
    rings = []
    n = 10
    for i in range(n + 1):
        t = i / n
        s = 1.0 - 0.8 * t ** 0.85 + bulge * math.sin(math.pi * t) * 0.35
        cx, cy = 0.0, by * t
        cz = -bz * t ** 1.4 + flutter * math.sin(math.pi * t * 1.2)
        rings.append([bm.verts.new((cx + s * x, cy, cz + s * z)) for x, z in pts])
    m = len(pts)
    for i in range(n):
        for k in range(m):
            k2 = (k + 1) % m
            bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
    tip = bm.verts.new((0.0, by + 0.6, -bz + flutter * math.sin(math.pi * 1.2)))
    for k in range(m):
        bm.faces.new((rings[n][k], rings[n][(k + 1) % m], tip))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bag = C.mesh_object("Bag", bm, acl_bag_mat("NetBag", NET["mesh"], NET["mesh_in"]))
    C.set_smooth(bag)
    FI.alf_adopt(parts + [bag], root)
    return bag, parts


def acl_netting(arr, ids, gid=1):
    """Knotted mesh: a diagonal lattice (pitch 3) of threads, the holes see-through (alpha 0.25)."""
    zz, xx = np.nonzero(ids == gid)
    for z, x in zip(zz, xx):
        if not ((x + z) % 3 == 0 or (x - z) % 3 == 0):
            arr[z, x, 3] = 0.25
    return arr


NET_CW, NET_CH, NET_FRAME = 32, 32, (16.0, 14.0)      # head canvas, the frame centre in it


def acl_net_sprite(pose, tag):
    C.clear_objects()
    if pose == "rest":
        cw, ch = 20, 44
        root = FI.alf_empty("Root", loc=(10.0, 0.0, 8.0), rot=(0.0, 0.0, math.radians(22)))
        bag, parts = acl_net(root, 5.5, 1.2, handle=22.0, cap=True)
        acl_fit(root, cw, ch, base=1.0)
    elif pose == "icon":
        cw, ch = 32, 32
        root = FI.alf_empty("Root", loc=(12.0, 0.0, 12.0), rot=(0.0, math.radians(38), math.radians(25)), scale=0.95)
        bag, parts = acl_net(root, 5.0, 7.0, handle=12.0, cap=True)
        acl_fit(root, cw, ch)
    else:
        cw, ch = NET_CW, NET_CH
        yaw, by, bz, fl, bu = dict(held=(25, 5.0, 8.0, 0.0, 0.0), sweep0=(72, 11.0, 2.5, 1.2, 0.0),
                                   sweep1=(72, 10.0, 3.5, -0.9, 0.0), full=(25, 5.0, 10.0, 0.0, 0.5))[pose]
        root = FI.alf_empty("Root", loc=(NET_FRAME[0], 0.0, NET_FRAME[1]), rot=(0.0, 0.0, math.radians(yaw)))
        bag, parts = acl_net(root, by, bz, fl, bu, handle=30.0)
    ids = acl_ids(cw, ch, tag, {1: [bag]})
    arr = acl_render(cw, ch, tag)
    arr = acl_netting(arr, ids)
    if pose == "full":                                   # debris caught in the sagging bag
        zz, xx = np.nonzero(ids == 1)
        zmin = zz.min()
        low = [(z, x) for z, x in zip(zz, xx) if z <= zmin + 4]
        rng = random.Random(3)
        rng.shuffle(low)
        cols = ["#5e4e36", "#8e6832", "#4a7a32", "#e2dcc6", "#43382a", "#7cb45a", "#5e4e36", "#b88a48"]
        for (z, x), col in zip(low[:8], cols):
            acl_dot(arr, x, z, col)
    return arr


# ---- siphon
SIPH_CW, SIPH_CH, SIPH_ROOT = 16, 32, (8.0, 2.0)      # held canvas, the tube's bottom centre (mouth) in it
SIPH_TUBE = (3.5, 19.0)                                # radius, length


def acl_siphon(root, hose_pts=None, bulb=None):
    r, L = SIPH_TUBE
    tube_m = FI.M("SiTube", SIPH["tube"], shine=0.9)
    rim_m = FI.M("SiRim", SIPH["rim"], shine=0.6)
    tube = FI.alf_can("Tube", r, 0.0, L)
    FI.alf_set_mats(tube, [tube_m, tube_m])
    rims = [FI.torus("RimB", rim_m, r, 0.6, (0, 0, 0.55), segs=24), FI.torus("RimT", rim_m, r, 0.5, (0, 0, L - 0.4), segs=24)]
    cap = C.tube_along("Cap", [(0, 0, L - 0.2), (0, 0, L + 1.4), (0, 0, L + 3.0)], [r + 0.2, 2.4, 1.3],
                       FI.M("SiCap", SIPH["cap"], shine=0.6), 16)
    hose_m = FI.M("SiHose", SIPH["hose"], shine=0.7)
    pts = hose_pts or [(0, 0, L + 2.6), (0, 0, L + 30.0)]
    hose = C.tube_along("Hose", pts, 1.0, hose_m, 10)
    extra = []
    if bulb:
        extra.append(FI.sphere("Bulb", FI.M("SiBulb", SIPH["bulb"], shine=0.6), 1.0, bulb, scale=(1.9, 1.9, 2.8)))
    FI.alf_adopt([tube, cap, hose] + rims + extra, root)
    return tube, rims, cap, hose, extra


def acl_glassify(arr, ids, murky=0.0):
    """The tube body see-through: per row the tube's inner pixels at alpha 0.5 (pale; murky = the upper share of the
    tube tinted with the dirty water), the lit left edge + a glint column opaque, the dark right edge opaque."""
    h, w = ids.shape
    rows = [z for z in range(h) if (ids[z] == 1).sum() >= 3]
    if not rows:
        return arr
    z0, z1 = min(rows), max(rows)
    for z in rows:
        xs = np.nonzero(ids[z] == 1)[0]
        dirty = murky > 0 and z >= z0 + (1.0 - murky) * (z1 - z0)
        for x in xs[1:-1]:
            arr[z, x, :3] = C.hex_rgb(SIPH["dirty"] if dirty else SIPH["glass"])
            arr[z, x, 3] = 0.5
        if len(xs) >= 5:
            arr[z, xs[1], :3] = C.hex_rgb(SIPH["glint"])
            arr[z, xs[1], 3] = 1.0
    return arr


def acl_siphon_contents(arr, ids, frame):
    """Gravel tumbling in the tube (2x2 pebbles, lit top-left) + dirt specks rising into the cap / hose."""
    inside = ids == 1
    peb = {0: [(-2, 3, 0), (0, 7, 1), (-1, 11, 0), (1, 4, 2)], 1: [(0, 4, 0), (-2, 8, 2), (1, 12, 1), (-1, 2, 1)],
           2: [(-1, 5, 1), (1, 9, 0), (-2, 13, 0), (0, 2, 2)]}[frame]
    tones = [("#dcc49a", "#ba9f79", "#957c64"), ("#ba9f79", "#957c64", "#6c5f57"), ("#9b9d9d", "#7c858f", "#5f6e7d")]
    cx, cz = int(SIPH_ROOT[0]), int(SIPH_ROOT[1])
    for dx, dz, t in peb:
        lt, bs, dk = tones[t]
        for (i, j, col) in ((0, 1, lt), (1, 1, bs), (0, 0, bs), (1, 0, dk)):
            x, z = cx + dx + i - 1, cz + dz + j
            if 0 <= z < inside.shape[0] and 0 <= x < inside.shape[1] and inside[z, x]:
                acl_dot(arr, x, z, col)
    for q in range(7):
        z = cz + 9 + ((q * 5 + frame * 2) % 18)
        x = cx - 1 + ((q * 3 + frame) % 3) - (1 if q % 3 == 0 else 0)
        if 0 <= z < arr.shape[0] and arr[z, x, 3] > 0:
            acl_dot(arr, x, z, "#4a3e2c" if q % 2 else "#5c4e34")
    return arr


def acl_siphon_sprite(pose, tag, tile=None):
    C.clear_objects()
    L = SIPH_TUBE[1]
    if pose == "rest" or pose == "icon":
        cw, ch = (28, 36) if pose == "rest" else (32, 32)
        root = FI.alf_empty("Root", loc=(8.0, 0.0, 2.0), rot=(math.radians(18.0), 0.0, 0.0),
                            scale=1.0 if pose == "rest" else 0.84)
        up = [(0, 0, L + 2.6), (0, 0, L + 6.0), (0.8, 0, L + 8.8), (3.0, 0, L + 10.4), (5.8, 0, L + 10.2),
              (7.8, 0, L + 8.4), (8.6, 0, L + 5.0), (8.8, 0, L + 1.0), (8.9, 0, 12.0), (8.8, 0, 6.0), (8.4, 0, 2.6)]
        coil = []
        turns = 1.12
        for q in range(1, 41):
            a = math.pi + 2 * math.pi * turns * q / 40.0
            R = 6.0 - 1.6 * q / 40.0
            y = R * math.sin(a)                                    # one loop lying flat, seen more from above
            coil.append((14.8 + R * math.cos(a), y, 2.2 + 1.2 * q / 40.0 - 0.8 * y))
        tube, rims, cap, hose, extra = acl_siphon(root, up + coil, bulb=(8.9, 0.0, 15.5))
        acl_fit(root, cw, ch, base=1.0 if pose == "rest" else None)
    else:
        cw, ch = SIPH_CW, SIPH_CH
        root = FI.alf_empty("Root", loc=(SIPH_ROOT[0], 0.0, SIPH_ROOT[1]))
        tube, rims, cap, hose, extra = acl_siphon(root)
    ids = acl_ids(cw, ch, tag, {1: [tube], 2: rims + [cap], 3: [hose] + extra})
    arr = acl_render(cw, ch, tag)
    frame = int(pose[-1]) if pose.startswith("work") else -1
    arr = acl_glassify(arr, ids, murky=0.55 if frame >= 0 else 0.0)
    if tile is not None:
        arr = acl_match_tile(arr, tile, 6)
    if frame >= 0:
        arr = acl_siphon_contents(arr, ids, frame)
    return arr


def acl_tile(arr, x0, rows=8, n=16):
    """A 4 px wide vertical tile: the most common 4 px row among the sprite's top rows (bottom-up arrays), repeated."""
    h = arr.shape[0]
    cands = [arr[r, x0:x0 + 4] for r in range(h - rows, h)]
    keys = [c.round(4).tobytes() for c in cands]
    best = cands[max(range(len(keys)), key=keys.count)]
    return np.repeat(best[None], n, axis=0).copy()


def acl_match_tile(arr, tile, x0, rows=8):
    """Make the sprite's pure handle / hose rows at the top (same coverage as the tile) exactly the tile's row, so
    the tiled continuation joins without a seam."""
    h = arr.shape[0]
    for r in range(h - rows, h):
        if ((arr[r, x0:x0 + 4, 3] > 0) == (tile[0, :, 3] > 0)).all() and (arr[r, :x0, 3] == 0).all()                 and (arr[r, x0 + 4:, 3] == 0).all():
            arr[r, x0:x0 + 4] = tile[0]
    return arr


def acl_hose_flow(tile):
    out = []
    for f in range(4):
        t = tile.copy()
        for base in (0, 8):
            z = (base + 2 * f) % 16
            acl_dot(t, 1, z, "#4a3e2c")
            acl_dot(t, 2, (z + 4) % 16, "#5c4e34")
            acl_dot(t, 2, (z + 1) % 16, "#7a7458")
        out.append(t)
    return out


def acl_suck():
    """Swirl at the siphon mouth (16x12; the mouth = the top row's centre): detritus + sand grains spiralling in."""
    out = {}
    specs = [(3.4, 5.2, 0.0, "#5c4e34"), (5.6, 3.6, 1.3, "#ba9f79"), (6.8, 6.0, 2.6, "#43382a"),
             (4.6, 8.0, 3.7, "#dcc49a"), (7.4, 2.4, 4.8, "#5e4e36"), (2.4, 9.0, 5.6, "#8a7a56")]
    for f in range(3):
        parts = []
        for r, hgt, ph, col in specs:
            rr = r - 1.1 * f
            a = ph + f * 1.1
            x = 8 + rr * math.cos(a) * 1.25
            z = 11 - hgt * (1.0 - 0.28 * f) + rr * 0.35 * math.sin(a)
            if rr > 0.4:
                parts.append(([(math.floor(x), math.floor(z))], col, 1.0 if f < 2 else 0.75))
        parts.append(([(7, 11), (8, 11)], "#5c4e34", 0.5))
        parts.append(([(7 - (2 - f), 10 - f % 2), (8 + (2 - f), 10 - (f + 1) % 2)], "#8a7a56", 0.5))
        out[f"clean_siphon_suck_f{f}"] = (acl_px(16, 12, parts, f"suck{f}"), ["swirl: bits lifting", "swirl: spiralling in",
                                                                               "swirl: sucked up"][f])
    return out


# ------------------------------------------------------------------ build
def acl_debris():
    out = {}
    for kind, (f0, f1, meaning) in DEBRIS.items():
        for f, rows in enumerate((f0, f1)):
            out[f"clean_debris_{kind}_f{f}"] = (acl_px(8, 8, acl_grid(rows, DEBRIS_PAL), f"deb_{kind}{f}"), meaning)
    return out


def aquaclean_art(groups, dry=False):
    world = os.path.join(ACL_OUT, "clean", "world") if dry else FI.WORLD
    items = os.path.join(ACL_OUT, "clean", "items") if dry else FI.ITEMS
    os.makedirs(ACL_WORK, exist_ok=True)
    made = {}

    def put(arr, folder, name, meaning, topdown=False):
        p = os.path.join(folder, name + ".png")
        if topdown:
            FI.afd_save(arr.astype(np.float32), p)
        else:
            C.save_pixels(arr, p)
        made[name] = (p, meaning, arr.shape[1], arr.shape[0])

    if "overlays" in groups:
        stages, grow = acl_algae()
        for k, img in enumerate(stages):
            cover = (img[..., 3] > 0).mean()
            put(img, world, f"clean_algae_{k + 1}", f"algae film stage {k + 1} (cover {cover:.0%})", True)
        put(grow, world, "clean_algae_grow", "algae appear level (grey)", True)
        for k, img in enumerate(acl_murk()):
            put(img, world, f"clean_murk_{k + 1}", f"murky water level {k + 1}", True)
        stages, grow = acl_dirt()
        for k, img in enumerate(stages):
            put(img, world, f"clean_dirt_{k + 1}", f"dirty gravel stage {k + 1}", True)
        put(grow, world, "clean_dirt_grow", "dirt appear level (grey)", True)
    if "fx" in groups:
        for grp in (acl_debris(), acl_fx(), acl_brushes(), acl_suck()):
            for name, (a, meaning) in grp.items():
                put(a, world, name, meaning)
        for name, w in (("clean_sponge_shadow", 24), ("clean_net_shadow", 16), ("clean_siphon_shadow", 28)):
            put(acl_shadow(w), world, name, "contact shadow on the ledge (hide while carried)")
    if "tools" in groups:
        put(acl_sponge_sprite("rest", 24, 16, "sp_rest", SPONGE_S), world, "clean_sponge_rest", "sponge on the ledge")
        put(acl_sponge_sprite("held", 26, 22, "sp_held", SPONGE_S), world, "clean_sponge_held", "sponge lifted (3/4)")
        for f in range(3):
            put(acl_sponge_sprite(f"wipe{f}", 26, 22, f"sp_wipe{f}", SPONGE_S), world, f"clean_sponge_wipe_f{f}",
                ["pressed flat", "pressed, rolled -10 (scrub)", "pressed, rolled +10 (scrub)"][f])
        held = acl_net_sprite("held", "net_held")
        ntile = acl_tile(held, 14)
        put(acl_match_tile(held, ntile, 14), world, "clean_net_held", "net head: still, bag hanging")
        put(ntile, world, "clean_net_handle", "handle tile (tile upwards)")
        for f in range(2):
            put(acl_match_tile(acl_net_sprite(f"sweep{f}", f"net_sweep{f}"), ntile, 14), world, f"clean_net_sweep_f{f}",
                "net sweeping right, bag trailing / fluttering")
        put(acl_match_tile(acl_net_sprite("full", "net_full"), ntile, 14), world, "clean_net_full",
            "net with debris in the bag")
        put(acl_net_sprite("rest", "net_rest"), world, "clean_net_rest", "net on the ledge (head down, handle up)")
        sh = acl_siphon_sprite("held", "si_held")
        tile = acl_tile(sh, 6)
        put(acl_match_tile(sh, tile, 6), world, "clean_siphon_held", "siphon: clear tube + hose")
        put(tile, world, "clean_siphon_hose", "hose tile (clear)")
        for f, t in enumerate(acl_hose_flow(tile)):
            put(t, world, f"clean_siphon_hose_flow_f{f}", "hose tile: dirty water rising")
        for f in range(3):
            put(acl_siphon_sprite(f"work{f}", f"si_work{f}", tile), world, f"clean_siphon_work_f{f}",
                "siphon working: gravel tumbling, dirt rising")
        put(acl_siphon_sprite("rest", "si_rest"), world, "clean_siphon_rest", "siphon on the ledge (hose coiled)")
        put(acl_sponge_sprite("icon", 32, 32, "icon_sponge", scale=1.55, sparkle=True), items, "tool_sponge",
            "스펀지 shop icon")
        put(acl_net_sprite("icon", "icon_net"), items, "tool_net", "뜰채 shop icon")
        put(acl_siphon_sprite("icon", "icon_siphon"), items, "tool_siphon", "사이펀 shop icon")
    for name, (p, meaning, w, h) in made.items():
        print(f"AQUACLEAN {name:28s} {w:3d}x{h:<3d} {meaning}")
    return made


# ------------------------------------------------------------------ anchors, review sheet, in-scene mock
def acl_anchors():
    """Offsets (sprite px from the centre, +y up) the code needs, from the layout constants."""
    rim = 0.05                                                      # the bottom rim's lowest point above the root
    return dict(
        glass_rect_room=GLASS, glass_centre_world=((GLASS[0] + GLASS[1]) / 2.0 / PPU - 20.0, 12.5 - (GLASS[2] + GLASS[3]) / 2.0 / PPU),
        dirt_rect_room=DIRT, dirt_centre_world=((DIRT[0] + DIRT[1]) / 2.0 / PPU - 20.0, 12.5 - (DIRT[2] + DIRT[3]) / 2.0 / PPU),
        meniscus_row_in_glass=SURF_ROW - GLASS[2],
        net_frame_centre=(NET_FRAME[0] - NET_CW / 2.0, NET_FRAME[1] - NET_CH / 2.0),
        net_opening_held=(NET_FRAME[0] - NET_CW / 2.0 - 6.5, NET_FRAME[0] - NET_CW / 2.0 + 6.5,
                          NET_FRAME[1] - NET_CH / 2.0 - 5.0, NET_FRAME[1] - NET_CH / 2.0 + 5.0),
        net_catch_sweep=(-3.0, 3.0, NET_FRAME[1] - NET_CH / 2.0 - 5.5, NET_FRAME[1] - NET_CH / 2.0 + 5.5),
        siphon_mouth=(0.0, SIPH_ROOT[1] + rim - SIPH_CH / 2.0),
        siphon_suck_centre_from_mouth=(0.0, -6.0),
        ledge_slots_room_col=SLOTS, ledge_row=LEDGE_ROW)


def acl_load(folder, name):
    return FI.afd_td(os.path.join(folder, name + ".png"))


def acl_paste_at(dst, a, cx, cy, flip=False):
    """Centre pivot at image px (col, row) - as the game places a sprite (centre on a pixel corner)."""
    FI.afd_paste(dst, a, int(round(cx - a.shape[1] / 2.0)), int(round(cy - a.shape[0] / 2.0)), 1, None, flip)


def acl_overlay(dst, a, rect, mask=None):
    img = a.copy()
    if mask is not None:
        img[..., 3] *= 1.0 - mask
    FI.afd_paste(dst, img, rect[0], rect[2])


def acl_stamp(mask, brush, cx, cy):
    """max-stamp a brush (top-down alpha) into a mask with its centre at mask px (cx, cy)."""
    bh, bw = brush.shape[:2]
    x0, y0 = int(round(cx - bw / 2.0)), int(round(cy - bh / 2.0))
    H, W = mask.shape
    xa, ya, xb, yb = max(0, x0), max(0, y0), min(W, x0 + bw), min(H, y0 + bh)
    if xb > xa and yb > ya:
        mask[ya:yb, xa:xb] = np.maximum(mask[ya:yb, xa:xb], brush[ya - y0:yb - y0, xa - x0:xb - x0, 3])


def acl_mock(world, out):
    """The game view (the 480x270 crop of the 640x400 room) at 2x, three frames stacked: A) filthy (algae 4, murk 3,
    dirt 3, floating debris, the tools resting on the ledge beside the feed containers); B) cleaning: the sponge wiped
    a patch of glass in overlapping passes (pixel-exact reveal; the fringe leaves faint streaks) and scrubs on with the
    squeak + streak glints, the net sweeps the debris near the surface (handle tiled up behind the hood), the siphon has
    cleaned the gravel from the left end and works on (hose tiled up, dirty water rising, the swirl at its mouth), the
    water clearer (murk 2); C) clean: the "clean!" burst, a glint on the glass, the tools back on the ledge."""
    back, front = acl_room("aquarium_back.png"), acl_room("aquarium_front.png")

    def L(n):
        return acl_load(world, n)

    def G(folder, n):
        return acl_load(os.path.join(C.SPRITES, folder), n)

    gt = acl_gtop()
    fishes = [("golden_carp", 250, 188, False), ("bluegill", 404, 158, True), ("crucian_carp", 330, 226, False)]
    debris = [("scum_f0", 188, SURF_ROW), ("scum_f1", 392, SURF_ROW), ("leaf_f0", 236, 138), ("deadleaf_f0", 300, 172),
              ("fibre_f0", 362, 146), ("hair_f1", 470, 196), ("mulm_f0", 214, 212), ("food_f0", 330, 127),
              ("speck_f0", 280, 160), ("speck_f1", 420, 216), ("scale_f1", 474, 140), ("leaf_f1", 150, 232)]
    brush_s, brush_d = L("clean_sponge_brush"), L("clean_siphon_brush")

    def ledge(v, tools=True):
        for k, x in (("basic", 160), ("premium", 196)):
            acl_paste_at(v, G("World", "feed_shadow"), x, LEDGE_ROW)
        acl_paste_at(v, G("World", "feed_basic_open3"), 160, LEDGE_ROW - 18)
        acl_paste_at(v, G("World", "feed_premium_sealed"), 196, LEDGE_ROW - 18)
        for k, x, w in (("tub", 232, 24), ("cooler", 272, 36)):
            acl_paste_at(v, G("World", f"feed_{k}_shadow"), x, LEDGE_ROW)
            acl_paste_at(v, G("World", f"feed_{k}_closed"), x, LEDGE_ROW - 20)
        if tools:
            for k in ("sponge", "siphon", "net"):
                r = L(f"clean_{k}_rest")
                acl_paste_at(v, L(f"clean_{k}_shadow"), SLOTS[k], LEDGE_ROW)
                acl_paste_at(v, r, SLOTS[k], LEDGE_ROW - r.shape[0] / 2.0)

    def tiles(v, tile, cx, top_row):
        y = top_row
        while y > 60:
            FI.afd_paste(v, tile, int(round(cx - 2)), y - tile.shape[0])
            y -= tile.shape[0]

    frames = []
    # A) filthy
    v = back.copy()
    acl_overlay(v, L("clean_dirt_3"), DIRT)
    for n, x, y, fl in fishes:
        acl_paste_at(v, G("Fish", n + "_0"), x, y, fl)
    for n, x, y in debris:
        acl_paste_at(v, L("clean_debris_" + n), x, y)
    acl_overlay(v, L("clean_murk_3"), GLASS)
    acl_overlay(v, L("clean_algae_4"), GLASS)
    FI.afd_paste(v, front, 0, 0)
    ledge(v)
    frames.append(v)
    # B) cleaning in progress
    v = back.copy()
    gw, gh = GLASS[1] - GLASS[0], GLASS[3] - GLASS[2]
    dw, dh = DIRT[1] - DIRT[0], DIRT[3] - DIRT[2]
    dmask = np.zeros((dh, dw))
    for x in range(0, 124, 2):                                      # the siphon came along the bottom from the left
        acl_stamp(dmask, brush_d, x, dh / 2.0)
    acl_overlay(v, L("clean_dirt_3"), DIRT, dmask)
    fishes_b = [("golden_carp", 262, 214, False), ("bluegill", 356, 176, True), ("crucian_carp", 300, 238, True)]
    for n, x, y, fl in fishes_b:
        acl_paste_at(v, G("Fish", n + "_0"), x, y, fl)
    for n, x, y in [d for d in debris if d[1] < 420 and d[0] not in ("scum_f1", "fibre_f0")]:
        acl_paste_at(v, L("clean_debris_" + n), x, y)
    acl_overlay(v, L("clean_murk_2"), GLASS)
    gmask = np.zeros((gh, gw))
    path = []
    for p, yy in enumerate(range(40, 112, 11)):                     # overlapping back-and-forth passes
        xs = range(56, 214, 2) if p % 2 == 0 else range(212, 56, -2)
        for x in xs:
            path.append((x + 3 * math.sin(yy * 0.3), yy + 1.5 * math.sin(x * 0.07)))
    for x, y in path[:-40]:
        acl_stamp(gmask, brush_s, x, y)
    acl_overlay(v, L("clean_algae_4"), GLASS, gmask)
    net = L("clean_net_sweep_f0")                                   # sweeping right through the debris near the top
    ncx, ncy = 432, 150
    tiles(v, L("clean_net_handle"), ncx, int(round(ncy - net.shape[0] / 2.0)))
    acl_paste_at(v, net, ncx, ncy)
    mouth_x = DIRT[0] + 126
    mouth_row = gt[mouth_x] - 1
    si = L("clean_siphon_work_f1")
    scy = mouth_row - (si.shape[0] / 2.0 - SIPH_ROOT[1])
    tiles(v, L("clean_siphon_hose_flow_f1"), mouth_x, int(round(scy - si.shape[0] / 2.0)))
    acl_paste_at(v, si, mouth_x, scy)
    acl_paste_at(v, L("clean_siphon_suck_f1"), mouth_x, mouth_row + 6)
    FI.afd_paste(v, front, 0, 0)
    sx, sy = path[-41]
    acl_paste_at(v, L("clean_sponge_wipe_f1"), GLASS[0] + sx, GLASS[2] + sy)
    acl_paste_at(v, L("clean_squeak_f1"), GLASS[0] + sx + 15, GLASS[2] + sy - 5)
    acl_paste_at(v, L("clean_streak_f1"), GLASS[0] + 120, GLASS[2] + 66)
    acl_paste_at(v, L("clean_squeak_f2"), GLASS[0] + 84, GLASS[2] + 50)
    ledge(v, tools=False)
    frames.append(v)
    # C) clean
    v = back.copy()
    for n, x, y, fl in fishes:
        acl_paste_at(v, G("Fish", n + "_0"), x, y, fl)
    FI.afd_paste(v, front, 0, 0)
    acl_paste_at(v, L("clean_burst_f2"), 318, 176)
    acl_paste_at(v, L("clean_streak_f0"), 178, 150)
    acl_paste_at(v, L("clean_streak_f1"), 470, 214)
    acl_paste_at(v, L("clean_squeak_f1"), 402, 136)
    ledge(v)
    frames.append(v)
    crops = [f[65:65 + 270, 80:80 + 480] for f in frames]
    img = np.zeros((270 * 2 * len(crops) + 16 * (len(crops) - 1), 960, 4), np.float32)
    img[..., 3] = 1.0
    img[..., :3] = FI.afd_rgb("#1e2836")
    for q, f in enumerate(crops):
        FI.afd_paste(img, f, 0, q * (540 + 16), 2)
    FI.afd_save(img, out)
    print("AQUACLEAN mock ->", out)


def acl_sheet(world, items, out):
    """Review sheet: the algae stages over the tank glass (1x), murk + the algae grow map, the dirt stages (2x), the debris
    (8x), the sponge / net / siphon sprites (6x / 5x) with their tiles, the FX (6x) and the shop icons (6x)."""
    back, front = acl_room("aquarium_back.png"), acl_room("aquarium_front.png")
    room = back.copy()
    FI.afd_paste(room, front, 0, 0)
    WATER, CAB, PANEL = "#5692a4", "#855a44", "#3a4a5e"

    def L(n, folder=None):
        return acl_load(folder or world, n)

    def over_room(name, rect, s):
        c0, c1, r0, r1 = rect
        v = back.copy()
        acl_overlay(v, L(name), rect)
        FI.afd_paste(v, front, 0, 0)
        crop = v[r0 - 4:r1 + 4, c0 - 4:c1 + 4]
        w, h = crop.shape[1] * s, crop.shape[0] * s

        def draw(sh, x, y):
            FI.afd_paste(sh, crop, x, y, s)
        return (w, h, draw)

    def grey(name, s):
        a = L(name)
        w, h = a.shape[1] * s, a.shape[0] * s

        def draw(sh, x, y):
            FI.afd_fill(sh, x, y, w, h, "#000000")
            FI.afd_paste(sh, a, x, y, s)
        return (w, h, draw)

    def cell(name, s, bg, folder=None, pad=0, ledge=False):
        a = L(name, folder)
        w, h = a.shape[1] * s + 2 * pad, a.shape[0] * s + 2 * pad

        def draw(sh, x, y):
            FI.afd_fill(sh, x, y, w, h, bg)
            if ledge:
                FI.afd_fill(sh, x, y + h - pad - s, w, pad + s, CAB)
            FI.afd_paste(sh, a, x + pad, y + pad, s)
        return (w, h, draw)

    rows = [[over_room(f"clean_algae_{k}", GLASS, 1) for k in range(1, 5)],
            [over_room(f"clean_murk_{k}", GLASS, 1) for k in range(1, 4)] + [grey("clean_algae_grow", 1)],
            [over_room("clean_dirt_1", DIRT, 2), over_room("clean_dirt_2", DIRT, 2)],
            [over_room("clean_dirt_3", DIRT, 2), grey("clean_dirt_grow", 2)],
            [cell(f"clean_debris_{k}_f{f}", 8, WATER) for k in DEBRIS for f in (0, 1)],
            [cell("clean_sponge_rest", 6, "#ba9c76", pad=6, ledge=True), cell("clean_sponge_shadow", 6, CAB, pad=6)]
            + [cell(n, 6, WATER, pad=6) for n in ["clean_sponge_held", "clean_sponge_wipe_f0", "clean_sponge_wipe_f1",
                                                  "clean_sponge_wipe_f2"]]
            + [cell("clean_sponge_brush", 6, "#000000", pad=6), cell("clean_siphon_brush", 4, "#000000", pad=6)],
            [cell(n, 5, WATER, pad=5) for n in ["clean_net_held", "clean_net_sweep_f0", "clean_net_sweep_f1",
                                                "clean_net_full"]]
            + [cell("clean_net_handle", 5, WATER, pad=5), cell("clean_net_rest", 5, "#ba9c76", pad=5, ledge=True),
               cell("clean_net_shadow", 5, CAB, pad=5)],
            [cell(n, 5, WATER, pad=5) for n in ["clean_siphon_held", "clean_siphon_work_f0", "clean_siphon_work_f1",
                                                "clean_siphon_work_f2"]]
            + [cell(n, 5, WATER, pad=5) for n in ["clean_siphon_hose"] + [f"clean_siphon_hose_flow_f{f}" for f in range(4)]]
            + [cell(f"clean_siphon_suck_f{f}", 5, "#ba9c76", pad=5) for f in range(3)]
            + [cell("clean_siphon_rest", 5, "#ba9c76", pad=5, ledge=True)],
            [cell(f"clean_squeak_f{f}", 6, WATER, pad=6) for f in range(3)]
            + [cell(f"clean_streak_f{f}", 6, WATER, pad=6) for f in range(3)]
            + [cell(f"clean_burst_f{f}", 5, WATER) for f in range(5)],
            [cell(n, 6, PANEL, folder=items, pad=12) for n in ("tool_sponge", "tool_net", "tool_siphon")]
            + [cell(n, 2, "#ba9c76", pad=4, ledge=True) for n in ("clean_sponge_rest", "clean_siphon_rest", "clean_net_rest")]
            + [cell(n, 2, WATER, pad=4) for n in ("clean_sponge_held", "clean_sponge_wipe_f0", "clean_net_held",
                                                  "clean_net_sweep_f0", "clean_net_full", "clean_siphon_work_f0")]]
    GAP = 10
    SW = max(sum(c[0] for c in r) + GAP * (len(r) + 1) for r in rows)
    SH = sum(max(c[1] for c in r) for r in rows) + GAP * (len(rows) + 1)
    sheet = np.zeros((SH, SW, 4), np.float32)
    sheet[..., 3] = 1.0
    sheet[..., :3] = FI.afd_rgb("#1e2836")
    y = GAP
    for r in rows:
        x = GAP
        for w, h, draw in r:
            draw(sheet, x, y)
            x += w + GAP
        y += max(c[1] for c in r) + GAP
    FI.afd_save(sheet, out)
    print("AQUACLEAN sheet ->", out, SW, "x", SH)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    dry = "dry" in argv
    groups = [g for g in argv if g != "dry"] or ["overlays", "fx", "tools", "mock"]
    C.reset_scene()
    aquaclean_art(groups, dry)
    for key, val in acl_anchors().items():
        print(f"AQUACLEAN anchor {key:30s} {val}")
    if "mock" in groups:
        world = os.path.join(ACL_OUT, "clean", "world") if dry else FI.WORLD
        items = os.path.join(ACL_OUT, "clean", "items") if dry else FI.ITEMS
        acl_sheet(world, items, os.path.join(ACL_OUT, "clean_sheet.png"))
        acl_mock(world, os.path.join(ACL_OUT, "clean_mock.png"))
    print("AQUACLEAN done")


if __name__ == "__main__":
    main()
