"""
FishingKing - world map (tilted 3/4 island), aquarium room and title logo.

  Stages/map_world.png      640x400, Data/map.json with stage marker positions (Unity units)
  Stages/aquarium_back.png  640x400, Stages/aquarium_front.png, Data/aquarium.json (swim bounds)
  UI/logo.png               title logo (Galmuri11 Bold, pixel grid)

Run:  blender -b --python Tools/Blender/fk_misc.py [-- map aquarium logo]
"""
import sys
import os
import math
import random
import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402
import fk_scene as S  # noqa: E402

STAGES_DIR = os.path.join(C.SPRITES, "Stages")
UI = os.path.join(C.SPRITES, "UI")
W, H, PPU = 640, 400, 16


def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


# ============================================================================ MAP
LAKE = Vector((-3.5, -0.5))
ICE = Vector((-8.0, 6.3))
RIVER = [Vector((1.5, 6.0)), Vector((0.3, 3.5)), Vector((-1.8, 1.2))]
SWAMP = Vector((7.0, -4.2))
HARBOR = Vector((-9.5, -7.6))
CAVE = Vector((4.2, 4.7))
BOAT = Vector((14.3, -3.6))

MARKERS = {
    "lake": (LAKE.x, LAKE.y, 0.4), "stream": (0.6, 3.9, 1.3), "sea": (HARBOR.x + 0.5, HARBOR.y + 0.3, 0.5),
    "swamp": (SWAMP.x, SWAMP.y, 0.4), "ice": (ICE.x, ICE.y, 2.5), "ocean": (BOAT.x, BOAT.y, 0.4),
    "cave": (CAVE.x, CAVE.y - 0.2, 1.5),
}


def terrain_h(x, y, noise):
    p = Vector((x, y))
    n = noise(x * 0.35, y * 0.35) * 0.9 + noise(x * 0.9 + 7, y * 0.9) * 0.35
    r = math.sqrt((x / 13.5) ** 2 + (y / 10.0) ** 2) + n * 0.12
    h = (1 - r) * 2.2 + 0.2
    # northern mountain range
    mr = math.exp(-((y - 6.2) / 2.2) ** 2) * smooth(-14, -9, x) * (1 - smooth(6, 10, x))
    peaks = 0.6 + 0.4 * math.sin(x * 1.3) * math.cos(x * 0.7 + 1)
    h += mr * (3.0 + 1.6 * peaks + n)
    kind = "land"
    # ice lake plateau
    if (p - ICE).length < 2.0:
        h = max(h, 0.0) * 0 + 2.4 + 0.02 * n
        kind = "ice"
    elif (p - ICE).length < 2.6:
        h = max(h, 2.6)
    # lake
    dl = (p - LAKE).length
    if dl < 2.7:
        h = 0.25
        kind = "lake" if dl < 2.3 else "lakeshore"
    # river
    dr = min(seg_dist(p, RIVER[i], RIVER[i + 1]) for i in range(len(RIVER) - 1))
    if dr < 0.45 and kind == "land":
        h = min(h, 0.3 + max(0, p.y - 1) * 0.35)
        kind = "river"
    # swamp basin
    ds = (p - SWAMP).length
    if ds < 3.2 and kind == "land":
        h = 0.35 + n * 0.08
        kind = "swamp"
    return h, kind


def build_map():
    C.clear_objects()
    rnd = random.Random(7)
    ph = [rnd.uniform(0, 100) for _ in range(6)]

    def noise(x, y):
        return (math.sin(x * 1.7 + ph[0]) * math.cos(y * 1.3 + ph[1]) + math.sin(x * 0.7 + y * 1.1 + ph[2]) * 0.6
                + math.sin(x * 2.9 - y * 2.3 + ph[3]) * 0.25) / 1.85

    nx, ny = 170, 130
    xs = [-21 + 42 * i / (nx - 1) for i in range(nx)]
    ys = [-19 + 36 * j / (ny - 1) for j in range(ny)]
    bm = bmesh.new()
    col_layer = bm.loops.layers.float_color.new("Col")
    vcol = {}
    verts = []
    pal = {
        "deep": "#1d5e9c", "mid": "#2f7cc0", "shallow": "#4aa2d8", "sand": "#ecd89c", "grass": "#6cb24c",
        "grass2": "#5aa044", "forest": "#3e8a3c", "rock": "#8e8a7c", "rock2": "#7a766a", "snow": "#f2f5fa",
        "lake": "#3f98d0", "lakeshore": "#d8cc90", "river": "#48a8dc", "swamp": "#5a7a3a", "swampw": "#4a6a44",
        "ice": "#cfe8f8",
    }
    lin = {k: C.lin(v) for k, v in pal.items()}
    for j, y in enumerate(ys):
        row = []
        for i, x in enumerate(xs):
            h, kind = terrain_h(x, y, noise)
            n = noise(x * 2.3, y * 2.3)
            if kind == "land":
                if h < 0:
                    c = "deep" if h < -0.9 else ("mid" if h < -0.35 else "shallow")
                    h = 0.0
                elif h < 0.42:
                    c = "sand"
                elif h < 1.4:
                    c = "grass" if n > -0.2 else "grass2"
                elif h < 2.3:
                    c = "forest" if n > -0.1 else "grass2"
                elif h < 3.6:
                    c = "rock" if n > 0 else "rock2"
                else:
                    c = "snow"
            elif kind == "swamp":
                c = "swampw" if n > 0.25 else "swamp"
            else:
                c = kind
            v = bm.verts.new((x, y, h))
            vcol[v] = lin[c]
            row.append(v)
        verts.append(row)
    for j in range(ny - 1):
        for i in range(nx - 1):
            f = bm.faces.new((verts[j][i], verts[j][i + 1], verts[j + 1][i + 1], verts[j + 1][i]))
            c = vcol[verts[j][i]]  # flat colour per face (no blurry interpolation)
            for lp in f.loops:
                lp[col_layer] = c
    m = bpy.data.materials.new("Terrain")
    nb = C.NB(m)
    attr = nb.node("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    shade = C.toon_shade(nb, 0.0)
    out = nb.mix(1.0, attr.outputs["Color"], shade, "MULTIPLY")
    nb.output_emission(out, 1.0)
    ob = C.mesh_object("Terrain", bm, m)
    C.set_smooth(ob, False)
    # decorations
    tree_m = [S.mtoon("#2e7a38"), S.mtoon("#3a8a3e"), S.mtoon("#2a6a44")]
    trunk = S.mtoon("#5a3a22")
    for _ in range(420):
        x, y = rnd.uniform(-13, 11), rnd.uniform(-9, 8)
        h, kind = terrain_h(x, y, noise)
        if kind != "land" or not 0.55 < h < 2.6:
            continue
        if (Vector((x, y)) - HARBOR).length < 2.5:
            continue
        s = rnd.uniform(0.28, 0.42)
        if h > 1.6 or rnd.random() < 0.4:
            S.cone(x, y, h, s, s * 3.0, rnd.choice(tree_m), 7)
        else:
            S.tube([(x, y, h), (x, y, h + s * 1.2)], s * 0.18, trunk, 5)
            S.sphere(x, y, h + s * 1.5, s * 0.9, rnd.choice(tree_m))
    # snowy pines around the ice lake
    for _ in range(26):
        a = rnd.uniform(0, 6.28)
        d = rnd.uniform(2.1, 3.0)
        x, y = ICE.x + math.cos(a) * d, ICE.y + math.sin(a) * d
        h, _ = terrain_h(x, y, noise)
        S.cone(x, y, h, 0.3, 0.9, S.mtoon("#3a5a6a"), 7)
        S.cone(x, y, h + 0.55, 0.16, 0.4, S.mflat("#f4f8ff"), 7)
    # swamp trees
    for _ in range(18):
        a = rnd.uniform(0, 6.28)
        d = rnd.uniform(0.5, 3.0)
        x, y = SWAMP.x + math.cos(a) * d, SWAMP.y + math.sin(a) * d
        S.tube([(x, y, 0.35), (x, y, 1.3)], 0.08, S.mtoon("#5a4630"), 5)
        S.sphere(x, y, 1.45, 0.5, S.mtoon("#4a6a34"), 1.2, 1.2, 0.6)
    # village near the lake
    for (x, y, c) in ((-6.8, -3.4, "#c85a3a"), (-7.8, -2.4, "#3a6ac8"), (-6.2, -1.9, "#c8a03a")):
        S.boxo(x, y, 0.75, 0.7, 0.6, 0.6, S.mtoon("#f0e6d0"))
        S.cone(x, y, 1.05, 0.55, 0.5, S.mtoon(c), 4, rot=(0, 0, math.radians(45)))
    # harbour breakwater + lighthouse
    S.tube([(HARBOR.x - 1.5, HARBOR.y + 1.0, 0.2), (HARBOR.x + 0.5, HARBOR.y - 0.6, 0.2), (HARBOR.x + 2.2, HARBOR.y - 0.8, 0.2)],
           0.25, S.mtoon("#c8c4bc"), 8)
    S.tube([(HARBOR.x + 2.2, HARBOR.y - 0.8, 0.2), (HARBOR.x + 2.2, HARBOR.y - 0.8, 1.6)], [0.22, 0.16],
           C.pattern_material("LH", "#f2f2ee", "#d83a3a", kind="stripes", scale=2.5, thresh=0.5, axis=2), 10)
    S.sphere(HARBOR.x + 2.2, HARBOR.y - 0.8, 1.75, 0.14, S.mglow("#fff2a0"))
    # boat on the open ocean
    S.boxo(BOAT.x, BOAT.y, 0.15, 1.8, 0.6, 0.35, S.mtoon("#f2f2ee"), 0.08)
    S.boxo(BOAT.x - 0.3, BOAT.y, 0.5, 0.6, 0.45, 0.4, S.mtoon("#2a5ab0"), 0.04)
    # cave mouth
    ch, _ = terrain_h(CAVE.x, CAVE.y, noise)
    S.sphere(CAVE.x, CAVE.y - 0.4, ch + 0.1, 0.8, S.mflat("#141020"), 1.2, 0.6, 1.1)
    for k in range(5):
        S.cone(CAVE.x - 0.8 + k * 0.4, CAVE.y - 0.9, ch - 0.3, 0.1, rnd.uniform(0.3, 0.6),
               S.mglow(rnd.choice(["#5ae8ff", "#b86aff"]), 1.3), 6)
    # wave marks
    for _ in range(60):
        x, y = rnd.uniform(-20, 20), rnd.uniform(-15, 15)
        h, kind = terrain_h(x, y, noise)
        if kind == "land" and h < -0.6:
            S.boxo(x, y, 0.02, 0.5, 0.06, 0.02, S.mflat("#a8d8f8"))
    # tilted camera
    sc = bpy.context.scene
    cd = bpy.data.cameras.new("MapCam")
    cam = bpy.data.objects.new("MapCam", cd)
    C.link(cam)
    old = sc.camera
    sc.camera = cam
    cd.type = "ORTHO"
    cd.ortho_scale = W / PPU * 1.4  # zoomed out so the island fits between the HUD bars
    cd.clip_end = 200
    tilt = math.radians(38)
    cam.rotation_euler = (tilt, 0, 0)
    cam.location = (0, -40 * math.sin(tilt) - 0.8, 40 * math.cos(tilt))
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.film_transparent = False
    raw = os.path.join(C.TMP, "map_raw.png")
    C.render_raw(raw)
    arr = C.load_pixels(raw)
    arr[..., 3] = 1
    C.save_pixels(arr, os.path.join(STAGES_DIR, "map_world.png"))
    markers = []
    for sid, (x, y, z) in MARKERS.items():
        h, _ = terrain_h(x, y, noise)
        px, py = C.world_to_pixel((x, y, max(h, z)))
        markers.append({"id": sid, "x": round((px - W / 2) / PPU, 3), "y": round((py - H / 2) / PPU, 3)})
    C.write_json("map.json", {"widthPx": W, "heightPx": H, "ppu": PPU, "markers": markers})
    sc.camera = old
    bpy.data.objects.remove(cam)
    print("MAP ok", markers)


# ============================================================================ AQUARIUM
def build_aquarium():
    C.clear_objects()
    S._mats.clear()
    rnd = random.Random(11)
    S._front[0] = False
    # room
    S.quad(-21, -13, 21, 13.5, C.pattern_material("Wall", "#e8d6b6", "#dcc8a4", kind="stripes", scale=0.5, thresh=0.5, axis=0, flat=True), 30)
    S.quad(-21, 9.8, 21, 10.4, S.mflat("#b89668"), 29)
    S.quad(-21, -13, 21, -8.6, C.pattern_material("Floor", "#a0704a", "#8a5e3c", kind="stripes", scale=0.8, thresh=0.5, axis=0, flat=True), 29)
    S.quad(-21, -8.7, 21, -8.3, S.mflat("#6a4a30"), 28.9)
    # window with a view
    S.quad(-19.5, 2.5, -15.5, 8.5, S.mflat("#6a4a30"), 28)
    S.quad(-19.1, 2.9, -15.9, 8.1, S.mgrad(["#bfe6f8", "#6ab4ec"], 3, 8, 5), 27.9)
    S.quad(-17.6, 2.9, -17.4, 8.1, S.mflat("#6a4a30"), 27.8)
    S.quad(-19.1, 5.4, -15.9, 5.6, S.mflat("#6a4a30"), 27.8)
    # picture frame + shelf with trophy
    S.quad(15.4, 4.0, 19.4, 7.6, S.mflat("#c8a040"), 28)
    S.quad(15.8, 4.4, 19.0, 7.2, S.mgrad(["#3a8ac0", "#a8dcf0"], 4.4, 7.2, 4), 27.9)
    fishy = C.load_pixels  # noqa: F841  (picture content is simple shapes)
    S.sphere(17.4, 27.8, 5.8, 0.55, S.mtoon("#f0a030"), 1.6, 0.3, 0.8)
    S.poly([(16.3, 5.8), (15.9, 6.2), (15.9, 5.4)], S.mflat("#f0a030"), 27.7)
    S.quad(15.0, 1.6, 20.0, 1.9, S.mflat("#8a5e3c"), 28)
    S.tube([(17.5, 27.5, 1.9), (17.5, 27.5, 2.2)], 0.35, S.mtoon("#d8b040", 1.0), 10)
    S.tube([(17.5, 27.5, 2.2), (17.5, 27.5, 2.9)], [0.12, 0.5], S.mtoon("#e8c040", 1.0), 10)
    # potted plant on the floor
    S.tube([(-17.5, 20, -8.6), (-17.5, 20, -6.8)], [0.9, 1.2], S.mtoon("#c86a3a"), 12)
    for k in range(7):
        a = math.radians(40 + 100 * k / 6)
        S.tube([(-17.5, 19.8, -6.9), (-17.5 + math.cos(a) * 1.6, 19.8, -6.9 + math.sin(a) * 2.6)], [0.25, 0.08], S.mtoon("#4a9a3a"), 6)
    # cabinet
    S.boxo(0, 10, -6.9, 30, 2, 3.6, S.mwood("#8a5a32", "#7a4e2a"), 0.1)
    for x in (-10, 0, 10):
        S.quad(x - 4.6, -8.2, x + 4.6, -5.5, S.mflat("#6e4526"), 8.9)
        S.sphere(x + 3.8, 8.8, -6.8, 0.2, S.mtoon("#d8b040", 1.0))
    # tank back + water
    bx0, bx1, bz0, bz1 = -14.0, 14.0, -5.1, 6.0
    S.quad(bx0, bz0, bx1, bz1, S.mflat("#1c2430"), 8)
    S.quad(bx0 + 0.4, bz0 + 0.3, bx1 - 0.4, 5.2, S.mgrad(["#1a6a8e", "#4ab8d8"], -4.8, 5.2, 9), 7.5)
    S.light_rays(rnd, [-9, -2, 5, 11], "#bff4ff", y=7.0, alpha=0.14, top=5.2, bottom=-3.4)
    # gravel + decor (behind fish)
    grav = [(bx0 + 0.4, -3.6)] + [(x, -3.6 + 0.35 * math.sin(x * 0.6) + 0.15 * math.sin(x * 1.7)) for x in
                                   [bx0 + 0.4 + k * 0.5 for k in range(56)]] + [(bx1 - 0.4, -3.6)]
    S.poly(grav + [(bx1 - 0.4, bz0 + 0.3), (bx0 + 0.4, bz0 + 0.3)], S.mnoise("#d8c090", "#b89a6a", 5, 0.5), 6)
    for k in range(40):
        x = rnd.uniform(bx0 + 1, bx1 - 1)
        S.sphere(x, 5.8, -3.8 + rnd.uniform(-0.3, 0.2), rnd.uniform(0.1, 0.22),
                 S.mtoon(rnd.choice(["#e8e0d0", "#a8a098", "#d8a878", "#8a8a8a"])), 1.3, 1, 0.8)
    # castle
    cm = S.mtoon("#b8a890")
    S.boxo(8.5, 5.4, -2.2, 2.6, 1.2, 2.6, cm)
    for dx in (-1.2, 1.2):
        S.tube([(8.5 + dx, 5.3, -3.4), (8.5 + dx, 5.3, 0.2)], 0.55, cm, 10)
        S.cone(8.5 + dx, 5.2, 0.2, 0.7, 1.1, S.mtoon("#c85a4a"), 10)
    S.quad(8.1, -3.5, 8.9, -2.1, S.mflat("#2a2030"), 4.7)
    # rocks, driftwood and plants
    for (x, r) in ((-10.5, 1.2), (-8.8, 0.8), (3.0, 0.9), (12.0, 0.7)):
        S.rock(x, 5.2, -3.3, r, S.mtoon("#7a7a82"), rnd, 1.2, 0.8)
    S.tube([(-5.5, 5.0, -3.2), (-3.0, 5.0, -2.6), (-1.0, 5.0, -1.4)], [0.35, 0.28, 0.15], S.mtoon("#6a4a30"), 8)
    for k in range(16):
        x = rnd.uniform(bx0 + 1, bx1 - 1)
        S.weed(rnd, x, 5.5, -3.6, rnd.uniform(2.0, 7.0), rnd.choice(["#3a9a4a", "#4ab04a", "#2e8a52", "#6ab83a"]), 0.12)
    # treasure chest + bubbler
    S.boxo(-1.5, 4.8, -3.2, 1.3, 0.8, 0.8, S.mwood("#8a5a30", "#6a4220"), 0.05)
    S.boxo(-1.5, 4.7, -2.75, 1.4, 0.85, 0.2, S.mtoon("#d8b040", 0.8))
    S.sphere(-1.5, 4.5, -2.6, 0.18, S.mglow("#fff0a0"))
    S.sphere(-6.5, 4.8, -3.5, 0.35, S.mtoon("#8a8a92"), 1.4, 1, 0.6)
    with S.front():
        fr = S.mtoon("#2a3240", 0.6)
        S.boxo(0, -1, 5.9, 28.4, 1.6, 0.5, fr, 0.05)       # top rim
        S.boxo(0, -1, -5.0, 28.4, 1.6, 0.5, fr, 0.05)      # bottom rim
        S.boxo(-14.0, -1, 0.45, 0.45, 1.6, 11.3, fr, 0.05)
        S.boxo(14.0, -1, 0.45, 0.45, 1.6, 11.3, fr, 0.05)
        S.boxo(0, -1.2, 6.6, 26.0, 1.4, 0.9, S.mtoon("#3a4250", 0.4), 0.1)  # lamp hood
        S.quad(-12.5, 6.1, 12.5, 6.2, S.mglow("#fff8e0", 1.2), -1.9)
        glass = S.mgrad(["#ffffff", "#ffffff"], 0, 1, 2, alpha=0.18)
        for (x, w) in ((-11.0, 0.8), (-9.6, 0.3), (6.5, 1.0), (8.1, 0.35)):
            S.poly([(x, -4.6), (x + w, -4.6), (x + w + 3.0, 5.3), (x + 3.0, 5.3)], glass, -1.8)
        for k in range(6):
            x = rnd.uniform(-13, 13)
            S.weed(rnd, x, -1.4, -4.8, rnd.uniform(1.0, 2.5), "#3a9a4a", 0.1)
    bpy.context.view_layer.update()
    C.ortho_camera(0, 0, W, H, PPU)
    sc = bpy.context.scene
    for ob in sc.objects:
        if ob.type == "MESH":
            if "layer" not in ob:
                ob["layer"] = "back"
            ob.hide_render = ob["layer"] == "front"
    sc.render.film_transparent = False
    raw = os.path.join(C.TMP, "aq_raw.png")
    C.render_raw(raw)
    arr = C.load_pixels(raw)
    arr[..., 3] = 1
    C.save_pixels(arr, os.path.join(STAGES_DIR, "aquarium_back.png"))
    for ob in sc.objects:
        if ob.type == "MESH":
            ob.hide_render = ob["layer"] != "front"
    sc.render.film_transparent = True
    C.render_sprite(os.path.join(STAGES_DIR, "aquarium_front.png"), outline=True, outline_mul=0.4)
    C.write_json("aquarium.json", {"swimMinX": -12.6, "swimMaxX": 12.6, "swimMinY": -2.6, "swimMaxY": 4.4,
                                   "surfaceY": 5.2, "waterTop": "#4ab8d8", "waterDeep": "#1a6a8e"})
    print("AQUARIUM ok")


# ============================================================================ LOGO
LOGO_FONT = os.path.join(C.ROOT, "Tools", "Fonts", "Galmuri11-Bold.ttf")  # SIL OFL 1.1 (see Tools/Fonts)


def pixel_text(fnt, body, px, mat, x, z, y=0.0):
    """Galmuri text as a flat mesh whose font pixels are exactly `px` image pixels (1 unit = 1 image px),
    with its left/bottom pixel edges on whole units."""
    cu = bpy.data.curves.new("T", "FONT")
    cu.body = body
    cu.font = fnt
    cu.size = 1.0
    cu.resolution_u = 1
    cu.extrude = 0.0
    ob = bpy.data.objects.new("Text", cu)
    C.link(ob)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob)
    # the outlines are axis-aligned pixel squares: the smallest gap between distinct x/y coords is one font pixel
    xs = sorted({round(v.co.x, 5) for v in me.vertices})
    ys = sorted({round(v.co.y, 5) for v in me.vertices})
    gaps = [b - a for s in (xs, ys) for a, b in zip(s, s[1:]) if b - a > 1e-4]
    unit = min(gaps)
    k = px / unit
    ox, oy = xs[0], ys[0]
    for v in me.vertices:
        v.co.x = round((v.co.x - ox) / unit) * px
        v.co.y = round((v.co.y - oy) / unit) * px
    mo = bpy.data.objects.new("TextMesh", me)
    C.link(mo)
    mo.data.materials.append(mat)
    mo.location = (x, y, z)
    mo.rotation_euler = (math.radians(90), 0, 0)  # XY glyph plane -> facing the camera
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds([mo])
    return mo, (x1 - x0, z1 - z0), k


def build_logo():
    """Title logo drawn with Galmuri11 Bold on an exact pixel grid, stacked-shadow extrusion + banded gold."""
    C.clear_objects()
    fnt = bpy.data.fonts.load(LOGO_FONT)
    S._mats.clear()
    # measure first so both lines can be centred on whole pixels
    TP, SP = 6, 2  # image pixels per font pixel (title / subtitle)
    _, (tw, th), _ = pixel_text(fnt, "낚시왕", TP, S.mflat("#ffffff"), 0, 0)
    _, (sw, sh), _ = pixel_text(fnt, "FISHING KING", SP, S.mflat("#ffffff"), 0, 0)
    C.clear_objects()
    S._mats.clear()
    gap = 10
    sub_z = 0
    title_z = sh + gap
    tx = -round(tw / 2)
    sx = -round(sw / 2)
    gold = S.mgrad(["#e2761a", "#ffb22a", "#ffe04a", "#fff6b0"], title_z, title_z + th, 4)
    depth = ["#d0601a", "#c0521a", "#a8421a", "#90361a", "#782a18", "#5e2014"]
    pixel_text(fnt, "낚시왕", TP, gold, tx, title_z, 0.0)
    for d, col in enumerate(depth, 1):  # extrusion: copies stepped one pixel down-right, behind the face
        pixel_text(fnt, "낚시왕", TP, S.mflat(col), tx + d, title_z - d, 0.2 * d)
    pixel_text(fnt, "FISHING KING", SP, S.mflat("#f4f8ff"), sx, sub_z, 0.0)
    for d in (1, 2):
        pixel_text(fnt, "FISHING KING", SP, S.mflat("#3a6ab0" if d == 1 else "#243f78"), sx + d, sub_z - d, 0.2 * d)
    bpy.context.view_layer.update()
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    x0, x1, z0, z1 = C.world_bounds(objs)
    pad = 2
    x0, z0 = math.floor(x0) - pad, math.floor(z0) - pad
    x1, z1 = math.ceil(x1) + pad, math.ceil(z1) + pad
    w, h = int(x1 - x0), int(z1 - z0)
    C.ortho_camera((x0 + x1) / 2, (z0 + z1) / 2, w, h, 1.0)
    C.render_sprite(os.path.join(UI, "logo.png"), outline=True, outline_mul=0.25)
    print("LOGO ok", w, h)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    what = argv or ["map", "aquarium", "logo"]
    C.reset_scene()
    if "map" in what:
        build_map()
    if "aquarium" in what:
        build_aquarium()
    if "logo" in what:
        build_logo()


if __name__ == "__main__":
    main()
