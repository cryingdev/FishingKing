"""
FishingKing - item icons (rods, reels, lines, baits, tanks), in-world lure sprites, UI icons,
9-slice UI frames and the reel widget.

Run:  blender -b --python Tools/Blender/fk_items.py [-- group ...]   groups: rods reels lines baits tanks ui frames
      (also: lures = only the spec-v1 lures (spinner crank kona popper softworm egi: Items/<id>.png 32 px +
       World/<id>_w.png 11 px) and the lure action chips (UI/act_*.png 12 px), plus the review sheet
       _tmp/legend_art/lure_sheet.png - the existing bait icons are not re-rendered;
       world = float sprites; worldreels = the small reel under the angler's rod, 2 handle frames per reel;
       reelarrow = only the reel-gesture help arrows, which frames rebuilds too;
       castarrow = the flick-cast block arrow over the angler's head, frames cast_arrow_f0..f7 -> _tmp/castarrow4
       (+ preview); the game loads them from Sprites/UI, so copy cast_arrow_f*.png there);
       sidearrow = the fight's side-pressure arrow (sideways gold block arrow, pointing right), frames
       side_arrow_f0..f7 + side_arrow_on straight into Sprites/UI (+ zoom sheets in _tmp/sidearrow);
       obstacles [dry] = Docs/obstacles_spec.md's snag / rub icons, abrasion meter and cast-contact / rub / pad FX
       (UI/icon_snag*, icon_rub, abr_*; World/fx_hit*, fx_chip*, fx_rub*, fx_padland*, obst_dash) + the review sheet
       _tmp/obstacles/ui_sheet.png; dry = into _tmp/obstacles/ui|world only
       aquafeed [dry] = the aquarium feeding art (feed bags on the cabinet ledge: sealed / open 4..1 / folded / tilt /
       pour, torn strip + tear burst, pellets / flakes / splash / crumb / dissolve FX, scissors hint: World/feed_*;
       UI/icon_full, icon_hungry, icon_growth; Items/feed_basic, feed_premium) + _tmp/aquafeed/art_sheet.png and
       mock.png; dry = into _tmp/aquafeed/world|ui|items only
       aqualive [dry] = the live food for the big fish (shrimp tub / sardine cooler on the ledge: closed / ajar / open /
       few / empty, the held / falling / sinking / resting shrimp and sardine, bite bits, drop splashes, the big-fish
       gulp: World/feed_tub_*, feed_cooler_*, feed_shrimp_*, feed_sardine_*, feed_drop_*, feed_gulp_*; UI/icon_diet_*;
       Items/feed_shrimp, feed_sardine) + _tmp/aqualive/art_sheet.png and mock.png; dry = into _tmp/aqualive/*
"""
import sys
import os
import math
import random
import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fk_common as C  # noqa: E402

ITEMS = os.path.join(C.SPRITES, "Items")
WORLD = os.path.join(C.SPRITES, "World")
UI = os.path.join(C.SPRITES, "UI")

M = C.toon_material


def box(name, mat, sx, sy, sz, loc=(0, 0, 0), bevel=0.0, seg=2, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= sx
        v.co.y *= sy
        v.co.z *= sz
    ob = C.mesh_object(name, bm, mat)
    ob.location = loc
    if rot:
        ob.rotation_euler = rot
    if bevel > 0:
        md = ob.modifiers.new("bev", "BEVEL")
        md.width = bevel
        md.segments = seg
        md.limit_method = "NONE"
    return ob


def sphere(name, mat, r, loc, scale=(1, 1, 1), seg=16, rings=10):
    ob = C.add_prim("sphere", name, mat, radius=r, location=loc, segments=seg, ring_count=rings)
    ob.scale = scale
    C.set_smooth(ob)
    return ob


def cyl(name, mat, p0, p1, r, segs=12):
    return C.tube_along(name, [p0, p1], r, mat, segs)


def torus(name, mat, R, r, loc, rot=(0, 0, 0), segs=24):
    ob = C.add_prim("torus", name, mat, major_radius=R, minor_radius=r, major_segments=segs,
                    minor_segments=8, location=loc, rotation=rot)
    C.set_smooth(ob)
    return ob


def render_icon(path, size=32, margin=1, outline=True, ppu=None, objs=None):
    bpy.context.view_layer.update()
    objs = objs or [o for o in bpy.context.scene.objects if o.type == "MESH"]
    x0, x1, z0, z1 = C.world_bounds(objs)
    if ppu is None:
        ppu = (size - 2 * margin - 2) / max(x1 - x0, z1 - z0)
    C.ortho_camera((x0 + x1) / 2, (z0 + z1) / 2, size, size, ppu)
    C.render_sprite(path, outline)
    return ppu


def hook(mat, base, scale=1.0, flip=False, r=0.03):
    """J-hook hanging below `base`. `r` = wire radius per unit of scale (the newer lures use a thicker wire so
    the hook stays a solid 1 px line in the 32 px icon instead of breaking into dots)."""
    s = scale
    bx, by, bz = base
    sg = -1 if flip else 1
    pts = [(bx, by, bz), (bx, by, bz - 0.5 * s)]
    for k in range(1, 8):
        a = math.pi * k / 7
        pts.append((bx + sg * (0.18 * s - 0.18 * s * math.cos(a)), by, bz - 0.5 * s - 0.2 * s * math.sin(a)))
    pts.append((bx + sg * 0.36 * s, by, bz - 0.45 * s))
    ob = C.tube_along("Hook", pts, r * s, mat, 6)
    eye = torus("HookEye", mat, 0.06 * s, 0.02 * s * (r / 0.03), (bx, by, bz + 0.05 * s), rot=(math.radians(90), 0, 0))
    return [ob, eye]


def treble(mat, base, s=1.0, r=0.03):
    objs = []
    for sg in (-1, 1):
        objs += hook(mat, base, s * 0.7, flip=(sg < 0), r=r)
    return objs


# ============================================================================ RODS
RODS = {
    "rod_bamboo": dict(blank="#c9a95e", blank2="#8e6a32", grip="#8a5a30", seat="#6a4424", wrap="#6a4424", r=0.065, nodes=True),
    "rod_glass": dict(blank="#ecc93e", blank2="#b8942a", grip="#c9a070", seat="#2a2a2a", wrap="#2a2a2a", r=0.06),
    "rod_carbon": dict(blank="#2c2c34", blank2="#1a1a20", grip="#1c1c1c", seat="#b8c0c8", wrap="#d83434", r=0.055, shine=0.8),
    "rod_surf": dict(blank="#eceef2", blank2="#b8bcc4", grip="#5a5e66", seat="#b8c0c8", wrap="#2f6fd4", r=0.055, long=1.25),
    "rod_biggame": dict(blank="#1c2c5c", blank2="#101a3a", grip="#3c2c1c", seat="#d8b040", wrap="#d8b040", r=0.085, butt=True),
    "rod_dragon": dict(blank="#b82424", blank2="#701010", grip="#e2b432", seat="#ffe070", wrap="#ffd040", r=0.07, glow="#ffb040", shine=1.0),
}


def build_rod(p):
    d = Vector((1, 0, 1)).normalized()
    n = Vector((1, 0, -1)).normalized()
    L = 4.0 * p.get("long", 1.0)
    objs = []
    grip = M("Grip", p["grip"])
    seat = M("Seat", p["seat"], shine=0.8)
    wrap = M("Wrap", p["wrap"], shine=0.5)
    blank = C.pattern_material("Blank", p["blank"], p["blank2"], kind="stripes", scale=2.2, thresh=0.12,
                               shine=p.get("shine", 0.4), axis=0) if p.get("nodes") else M("Blank", p["blank"], shine=p.get("shine", 0.4))
    r = p["r"]
    P = lambda s: d * s  # noqa: E731
    objs.append(C.tube_along("Butt", [P(0), P(0.45)], r * 2.0, grip, 10))
    objs.append(C.tube_along("Seat", [P(0.45), P(0.75)], r * 1.6, seat, 10))
    objs.append(C.tube_along("Fore", [P(0.75), P(1.25)], r * 1.8, grip, 10))
    objs.append(C.tube_along("Blank", [P(1.25), P(L * 0.6), P(L)], [r, r * 0.8, r * 0.5], blank, 8))
    if p.get("butt"):
        objs.append(C.tube_along("Gimbal", [P(-0.12), P(0.02)], r * 2.4, seat, 10))
    for k in range(5):
        s = 1.5 + (L - 1.6) * k / 4
        base = P(s)
        objs.append(C.tube_along("Guide", [base, base + n * (0.16 - 0.015 * k)], 0.028, seat, 6))
        objs.append(C.tube_along("Wrap", [P(s - 0.06), P(s + 0.06)], r * 1.25, wrap, 8))
    if p.get("glow"):
        g = C.glow_material("Glow", p["glow"], 1.3)
        objs.append(sphere("Tip", g, 0.09, tuple(P(L + 0.02)), seg=10, rings=6))
        objs.append(sphere("Pommel", g, 0.14, tuple(P(-0.05)), seg=10, rings=6))
    return objs


# ============================================================================ REELS
REELS = {
    "reel_basic": dict(kind="spin", body="#3a3a40", spool="#8a8e96", line="#e8f0f0", knob="#2a2a2a"),
    "reel_light": dict(kind="spin", body="#e8eaee", spool="#3a7ad0", line="#d8ecf8", knob="#3a7ad0"),
    "reel_highgear": dict(kind="spin", body="#b8c0c8", spool="#d23a3a", line="#f0e8a0", knob="#1c1c1c", big=1.1),
    "reel_baitcast": dict(kind="bait", body="#2a4a3a", plate="#c8ccd0", knob="#d8b040"),
    "reel_electric": dict(kind="elec", body="#e0e2e6", plate="#2a2a30", knob="#d23a3a", lcd="#6aff8a"),
    "reel_poseidon": dict(kind="spin", body="#e2b432", spool="#2ab8b0", line="#bff8f0", knob="#fff0a0", big=1.2, gem="#5affff"),
}


def build_reel(p):
    objs = []
    body = M("Body", p["body"], shine=0.9)
    knob = M("Knob", p["knob"], shine=0.6)
    b = p.get("big", 1.0)
    if p["kind"] == "spin":
        spool = M("Spool", p["spool"], shine=0.9)
        line = M("Line", p["line"])
        # foot + stem
        objs.append(box("Foot", body, 1.1, 0.25, 0.12, loc=(0.1, 0, 1.35 * b), bevel=0.04))
        objs.append(box("Stem", body, 0.22, 0.2, 1.0 * b, loc=(-0.1, 0, 0.85 * b), bevel=0.06, rot=(0, math.radians(-12), 0)))
        objs.append(sphere("Gear", body, 0.55 * b, (-0.25, 0, 0.15), scale=(1.0, 0.8, 1.0)))
        objs.append(C.tube_along("Rotor", [(0.15, 0, 0.15), (0.55 * b, 0, 0.15)], [0.42 * b, 0.46 * b], body, 16))
        objs.append(C.tube_along("Spool", [(0.55 * b, 0, 0.15), (1.1 * b, 0, 0.15)], 0.34 * b, spool, 16))
        objs.append(C.tube_along("LineW", [(0.62 * b, 0, 0.15), (1.02 * b, 0, 0.15)], 0.37 * b, line, 16))
        objs.append(C.tube_along("Bail", [(0.5 * b, -0.3, 0.6 * b), (1.05 * b, -0.35, 0.58 * b), (1.2 * b, -0.3, 0.2)], 0.035, M("Wire", "#e8e8f0", shine=1), 6))
        objs.append(C.tube_along("Arm", [(-0.25, -0.5, 0.15), (-0.75 * b, -0.55, -0.45 * b)], 0.07, body, 8))
        objs.append(sphere("Knob", knob, 0.17 * b, (-0.8 * b, -0.62, -0.5 * b), scale=(1, 0.8, 1.2)))
        if p.get("gem"):
            objs.append(sphere("Gem", C.glow_material("Gem", p["gem"], 1.3), 0.16, (-0.25, -0.46, 0.15), scale=(1, 0.5, 1)))
            tri = M("Tri", "#fff0a0", shine=1)
            for dx in (-0.12, 0, 0.12):
                objs.append(C.tube_along("Prong", [(-0.25 + dx, -0.5, 0.3), (-0.25 + dx * 1.3, -0.5, 0.62)], 0.03, tri, 6))
    elif p["kind"] == "bait":
        plate = M("Plate", p["plate"], shine=0.9)
        objs.append(sphere("Body", body, 0.8, (0, 0, 0), scale=(1.25, 0.7, 0.75)))
        objs.append(C.tube_along("Side", [(0, -0.5, 0), (0, -0.58, 0)], 0.55, plate, 20))
        objs.append(C.tube_along("Spool", [(-0.6, -0.1, 0.1), (0.6, -0.1, 0.1)], 0.35, M("Line", "#e8f0a0"), 12))
        objs.append(box("Foot", body, 1.0, 0.2, 0.12, loc=(0, 0, 0.62), bevel=0.04))
        objs.append(C.tube_along("Arm", [(-0.35, -0.65, 0.05), (0.35, -0.65, -0.05)], 0.07, plate, 8))
        for sx in (-1, 1):
            objs.append(sphere("Knob", knob, 0.16, (sx * 0.45, -0.72, sx * -0.06), scale=(1.2, 0.8, 1)))
        objs.append(C.tube_along("Star", [(0, -0.6, 0), (0, -0.78, 0)], 0.16, M("StarM", "#d8b040", shine=1), 6))
    elif p["kind"] == "elec":
        plate = M("Plate", p["plate"], shine=0.6)
        objs.append(box("Body", body, 1.9, 1.0, 1.05, loc=(0, 0, 0), bevel=0.2, seg=3))
        objs.append(box("Face", plate, 1.3, 0.1, 0.55, loc=(0.1, -0.52, 0.1), bevel=0.06))
        objs.append(box("LCD", C.glow_material("LCD", p["lcd"], 1.2), 0.9, 0.1, 0.3, loc=(0.1, -0.58, 0.12)))
        objs.append(box("Foot", body, 1.3, 0.2, 0.12, loc=(0, 0, 0.64), bevel=0.04))
        objs.append(C.tube_along("Arm", [(-0.9, -0.55, 0), (-1.25, -0.6, -0.55)], 0.08, plate, 8))
        objs.append(sphere("Knob", knob, 0.2, (-1.3, -0.68, -0.62), scale=(1, 0.8, 1.2)))
        objs.append(C.tube_along("Cable", [(0.95, 0, -0.2), (1.25, 0, -0.5), (1.1, 0, -0.9), (1.4, 0, -1.1)], 0.05, M("Cable", "#1a1a1a"), 6))
    return objs


# ============================================================================ WORLD REELS
# Tiny reel hanging under the angler's rod, seen from behind/above roughly along the rod (from the butt end).
# Two frames per reel (<id>_w0 / _w1): the handle turned by half a revolution (a quarter for the symmetric
# double handle of the baitcaster), alternated by the game while reeling. Every sprite has the same canvas
# and the top of the foot (where it clamps to the rod) at the same pixel, horizontally centred.
REEL_W_SIZE = 16            # canvas (square, even so the centre pivot sits on a pixel corner)
REEL_W_PPU = 4.4            # px per model unit (the basic spinning reel is ~10 px tall)
REEL_W_FOOT = (8.0, 3.0)    # canvas px (x from left, y from top) of the centre of the foot's top face
REEL_W_TILT = 20.0          # deg: the camera is above the rod, looking along it towards the tip
REEL_W_YAW = 35.0           # deg: slight 3/4 turn, rod tip to the LEFT (the rod is held in the left hand and leans
                            # up-left on screen); the handle stays on the screen right, where the right hand cranks it


def build_world_reel(p, frame):
    """Simplified reel in the icon frame (x = towards the rod tip, z up, foot on top), handle on -y as in the
    icon (the reel's right side, where the angler's reeling arm is). Returns (objs, foot_top, line_exit)."""
    objs = []
    body = M("Body", p["body"], shine=0.9)
    knob = M("Knob", p["knob"], shine=0.6)
    b = 1.0 + (p.get("big", 1.0) - 1.0) * 0.5  # big reels only a little bigger (they must fit the shared canvas)
    hs = -1.0  # handle side (y sign)
    turn = math.pi if frame else 0.0

    def crank(c, L, a0, r_arm, r_knob, mat_arm, knob_y):
        """Single handle turning about the y axis through c; the knob sits out at |y| = knob_y."""
        a = a0 + turn
        ex, ez = c[0] + L * math.cos(a), c[2] + L * math.sin(a)
        mid = (c[0] + L * 0.5 * math.cos(a), c[1] + hs * 0.1, c[2] + L * 0.5 * math.sin(a))
        objs.append(C.tube_along("Arm", [c, mid, (ex, hs * (knob_y - 0.1), ez)], r_arm, mat_arm, 8))
        objs.append(sphere("Knob", knob, r_knob, (ex, hs * knob_y, ez), scale=(1, 1.1, 1), seg=12, rings=8))

    if p["kind"] == "spin":
        spool = M("Spool", p["spool"], shine=0.9)
        line = M("Line", p["line"])
        ft = 1.3 * b
        objs.append(box("Foot", body, 0.9 * b, 0.34 * b, 0.14 * b, loc=(0.1 * b, 0, ft - 0.07 * b), bevel=0.03))
        objs.append(box("Stem", body, 0.24 * b, 0.22 * b, 1.0 * b, loc=(-0.08 * b, 0, 0.75 * b), bevel=0.05,
                        rot=(0, math.radians(-12), 0)))
        objs.append(sphere("Gear", body, 0.52 * b, (-0.25 * b, 0, 0.1 * b), scale=(1.0, 0.85, 1.0)))
        objs.append(C.tube_along("Rotor", [(0.1 * b, 0, 0.1 * b), (0.5 * b, 0, 0.1 * b)], [0.4 * b, 0.45 * b], body, 16))
        objs.append(C.tube_along("Spool", [(0.5 * b, 0, 0.1 * b), (1.0 * b, 0, 0.1 * b)], 0.33 * b, spool, 16))
        objs.append(C.tube_along("LineW", [(0.56 * b, 0, 0.1 * b), (0.88 * b, 0, 0.1 * b)], 0.37 * b, line, 16))
        objs.append(C.tube_along("Lip", [(0.88 * b, 0, 0.1 * b), (1.02 * b, 0, 0.1 * b)], 0.42 * b, spool, 16))
        crank((-0.25 * b, hs * 0.42 * b, 0.1 * b), 0.72 * b, math.radians(75), 0.09 * b, 0.2 * b, M("Wire", "#d0d4dc", shine=1), 1.0 * b)
        if p.get("gem"):
            objs.append(sphere("Gem", C.glow_material("Gem", p["gem"], 1.3), 0.15 * b, (-0.3 * b, hs * 0.4 * b, 0.25 * b),
                               scale=(1, 0.5, 1), seg=10, rings=6))
        return objs, (0.1 * b, 0, ft), (1.02 * b, 0, 0.1 * b + 0.42 * b)
    if p["kind"] == "bait":
        plate = M("Plate", p["plate"], shine=0.9)
        ft = 0.62
        objs.append(sphere("Body", body, 0.8, (0, 0, 0), scale=(1.2, 0.72, 0.72)))
        objs.append(C.tube_along("Side", [(0, hs * 0.46, 0), (0, hs * 0.58, 0)], 0.52, plate, 20))
        objs.append(C.tube_along("Level", [(0.7, -0.3, 0.3), (0.7, 0.3, 0.3)], 0.12, M("Line", "#e8f0a0"), 8))
        objs.append(box("Foot", body, 0.9, 0.34, 0.14, loc=(0, 0, ft - 0.07), bevel=0.03))
        objs.append(C.tube_along("Star", [(0, hs * 0.55, 0), (0, hs * 0.8, 0)], 0.17, M("StarM", "#d8b040", shine=1), 6))
        # double handle: a half turn would look identical, so the second frame is a quarter turn
        a = math.radians(80) + (math.pi / 2 if frame else 0.0)
        e1 = (0.42 * math.cos(a), hs * 0.78, 0.42 * math.sin(a))
        e2 = (-e1[0], hs * 0.78, -e1[2])
        objs.append(C.tube_along("Arm", [e2, e1], 0.08, plate, 8))
        for e in (e1, e2):
            objs.append(sphere("Knob", knob, 0.19, (e[0], hs * 0.86, e[2]), scale=(1, 1.1, 1), seg=12, rings=8))
        return objs, (0, 0, ft), (0.75, 0, 0.42)
    # electric
    plate = M("Plate", p["plate"], shine=0.6)
    ft = 0.62
    objs.append(box("Body", body, 1.5, 0.95, 1.0, loc=(0, 0, 0), bevel=0.2, seg=3))
    objs.append(box("Face", plate, 1.0, 0.1, 0.55, loc=(0.1, hs * 0.48, 0.08), bevel=0.05))
    objs.append(box("LCD", C.glow_material("LCD", p["lcd"], 1.2), 0.7, 0.1, 0.3, loc=(0.1, hs * 0.53, 0.1)))
    objs.append(box("Foot", body, 0.9, 0.34, 0.14, loc=(0, 0, ft - 0.07), bevel=0.03))
    crank((-0.45, hs * 0.48, -0.05), 0.58, math.radians(100), 0.09, 0.21, plate, 0.72)
    return objs, (0, 0, ft), (0.75, 0, 0.35)


def world_reel_matrix():
    """Model (x = rod tip, z up) -> render frame (camera looks along +y): the rod points away from the camera and
    a little to the LEFT (left-handed rod leaning up-left), the handle side (-y, the angler's right) turns to the
    screen right (a little away from the camera, as seen from behind the angler), and the camera looks down along
    the rod by REEL_W_TILT."""
    from mathutils import Matrix
    return Matrix.Rotation(math.radians(REEL_W_TILT), 4, "X") @ Matrix.Rotation(math.radians(90 + REEL_W_YAW), 4, "Z")


def build_world_reels():
    out = []
    info = {}
    R = world_reel_matrix()
    S = REEL_W_SIZE
    for rid, p in REELS.items():
        for frame in (0, 1):
            C.clear_objects()
            objs, foot, spool = build_world_reel(p, frame)
            bpy.context.view_layer.update()
            for ob in objs:
                ob.matrix_world = R @ ob.matrix_world
            bpy.context.view_layer.update()
            f = R @ Vector(foot)
            # put the foot-top centre at REEL_W_FOOT (px from the top-left corner)
            cx = f.x - (REEL_W_FOOT[0] - S / 2) / REEL_W_PPU
            cz = f.z - ((S - REEL_W_FOOT[1]) - S / 2) / REEL_W_PPU
            C.ortho_camera(cx, cz, S, S, REEL_W_PPU)
            x0, x1, z0, z1 = C.world_bounds(objs)
            edge = min((x0 - cx) * REEL_W_PPU + S / 2, S / 2 - (x1 - cx) * REEL_W_PPU,
                       (z0 - cz) * REEL_W_PPU + S / 2, S / 2 - (z1 - cz) * REEL_W_PPU)
            if edge < 1.0:  # the 1 px outline needs room inside the canvas
                print(f"WARNING world reel {rid}_w{frame}: only {edge:.2f} px from the canvas edge")
            path = os.path.join(WORLD, f"{rid}_w{frame}.png")
            C.render_sprite(path, outline=True)
            out.append(path)
            fx, fy = C.world_to_pixel(R @ Vector(foot))
            sx, sy = C.world_to_pixel(R @ Vector(spool))
            # crank knob centre(s), as px from the sprite centre (x right, y up) - where the right hand grips it
            knobs = []
            for ob in objs:
                if ob.name.startswith("Knob"):
                    kc = sum((ob.matrix_world @ Vector(b) for b in ob.bound_box), Vector()) / 8.0
                    kx, ky = C.world_to_pixel(kc)
                    knobs.append((round(kx - S / 2, 2), round(ky - S / 2, 2)))
            info[f"{rid}_w{frame}"] = ((round(fx, 2), round(S - fy, 2)), (round(sx, 2), round(S - sy, 2)), knobs)
    for k, (ft, sp, kn) in info.items():
        print(f"world reel {k}: foot top {ft}  line exit {sp}  (px from top-left)  knob {kn} (px from centre, y up)")
    C.contact_sheet(out, os.path.join(C.TMP, "world_reels_zoom.png"), scale=8, cols=4)
    return out


# ============================================================================ LINES
LINES = {
    "line_nylon2": dict(line="#e6f2f2", spool="#3a8ad0"),
    "line_nylon4": dict(line="#b8e6a0", spool="#e8e8e8"),
    "line_fluoro6": dict(line="#f2c8dc", spool="#6a3a8a"),
    "line_pe3": dict(line="#3ab04a", line2="#e8e04a", spool="#1c1c1c"),
    "line_pe8": dict(line="#f07a2a", line2="#3a8ae0", spool="#2a2a3a"),
    "line_titan": dict(line="#c8ccd4", line2="#8a8e96", spool="#1c2a4a", shine=1.2),
}


def build_line(p):
    objs = []
    spool = M("Spool", p["spool"], shine=0.6)
    if "line2" in p:
        line = C.pattern_material("Line", p["line"], p["line2"], kind="stripes", scale=3.5, thresh=0.5, axis=2,
                                  shine=p.get("shine", 0.3))
    else:
        line = M("Line", p["line"], shine=0.3)
    ax = Vector((1.0, -0.5, 0.3)).normalized()  # mostly side-on so the wound line reads first
    objs.append(C.tube_along("FlangeA", [ax * -0.75, ax * -0.62], 1.0, spool, 28))
    objs.append(C.tube_along("FlangeB", [ax * 0.62, ax * 0.75], 1.0, spool, 28))
    objs.append(C.tube_along("Wound", [ax * -0.64, ax * 0.64], 0.84, line, 28))
    objs.append(C.tube_along("Hub", [ax * 0.74, ax * 0.8], 0.34, M("HubM", "#d8dce4", shine=0.8), 16))
    objs.append(C.tube_along("Hole", [ax * 0.79, ax * 0.83], 0.16, M("Hole", "#101014"), 12))
    return objs


# ============================================================================ BAITS
def bumpy_ball(name, mat, r, loc, amp=0.08, seed=1):
    ob = C.add_prim("ico", name, mat, radius=r, location=loc, subdivisions=3)
    rnd = random.Random(seed)
    for v in ob.data.vertices:
        v.co *= 1 + rnd.uniform(-amp, amp)
    C.set_smooth(ob)
    return ob


def wiggle(n, L, amp, waves, z0=0.0, x0=0.0):
    return [(x0 + L * k / (n - 1), 0, z0 + amp * math.sin(waves * math.pi * 2 * k / (n - 1))) for k in range(n)]


# ---------------------------------------------------------------------------- lures (spec v1: 10 lures, 5 actions)
# Same frame as the other baits: side view, x = the lure's nose (line tie) to the RIGHT, z up, the camera looks
# along +y. Objects named "Fx..." (glints, splash drops, bubbles) are icon-only accents: they are removed before the
# 11 px in-water sprite is rendered, because the game draws its own glint / splash / bubble effects on the lure.
def split_material(name, top, bottom, z=0.0, shine=0.0, emit=1.0):
    """Toon material: `top` colour above object-space height z, `bottom` below (lure back / belly)."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["Object"])
    mask = nb.math("GREATER_THAN", s[2], z)
    base = nb.mix(mask, C.lin(bottom), C.lin(top))
    shade = C.toon_shade(nb, shine)
    nb.output_emission(nb.mix(1.0, base, shade, "MULTIPLY"), emit)
    return m


def fx_sparkle(mat, x, z, s, y=-0.9):
    """4-point glint star (icon accent, like bait_golden's sparkles)."""
    return C.poly_object("FxGlint", [(x, z + s * 2), (x + s * 0.5, z + s * 0.5), (x + s * 2, z), (x + s * 0.5, z - s * 0.5),
                                     (x, z - s * 2), (x - s * 0.5, z - s * 0.5), (x - s * 2, z), (x - s * 0.5, z + s * 0.5)],
                         mat, y=y)


def leaf(p0, p1, half_w, n=12):
    """Pointed-ellipse outline (x, z) from p0 to p1 (willow blade, fins)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    nrm = Vector((-d.y, d.x)).normalized()
    up, lo = [], []
    for i in range(n + 1):
        t = i / n
        w = half_w * math.sin(math.pi * t) ** 0.8
        c = p0 + d * t
        up.append(tuple(c + nrm * w))
        lo.append(tuple(c - nrm * w))
    return up + lo[-2:0:-1]


def eye_dot(x, y, z, r, iris="#fff070", pupil="#101010"):
    """Painted lure eye (flat disc + pupil) on the camera side."""
    return [sphere("Eye", M("EyeI", iris, flat=True), r, (x, y, z), scale=(1, 0.4, 1)),
            sphere("Pupil", M("EyeP", pupil, flat=True), r * 0.55, (x + r * 0.2, y - r * 0.45, z), scale=(1, 0.4, 1))]


def strands(name, mat, x0, x1, fan, y=0.0, r=(0.05, 0.018), waves=1.0, amp=0.05, n=8):
    """Skirt / hackle strands streaming back from x0 to x1. fan = [(z at x0, z at x1), ...]."""
    objs = []
    for k, (za, zb) in enumerate(fan):
        pts, rr = [], []
        for i in range(n):
            u = i / (n - 1)
            x = x0 + (x1 - x0) * u
            z = za + (zb - za) * u * u + amp * u * math.sin(waves * math.pi * 2 * u + k * 1.7)
            pts.append((x, y + 0.02 * (k % 3 - 1), z))
            rr.append(r[0] + (r[1] - r[0]) * u)
        objs.append(C.tube_along(name, pts, rr, mat, 6))
    return objs


def build_spinner(metal):
    """스피너: willow blade on a clevis, black body with a red bead, red/white dressed treble."""
    objs = []
    wire = M("Wire", "#d8dce4", shine=1.0)
    objs.append(C.tube_along("Shaft", [(-0.7, 0, 0), (1.3, 0, 0)], 0.045, wire, 6))
    objs.append(torus("Tie", wire, 0.12, 0.045, (1.42, 0, 0), rot=(math.radians(90), 0, 0)))
    objs.append(C.tube_along("Clevis", [(1.16, 0, 0), (1.12, 0, 0.18), (1.02, 0, 0.24)], 0.045, wire, 6))
    # willow blade, swung out above the shaft (it spins round it), its face to the camera: a pointed leaf with
    # a darker cupped lower half
    objs.append(extruded("Blade", leaf((1.04, 0.24), (-0.5, 0.86), 0.34), M("Blade", "#f6cc48", shine=1.4), 0.08, 0.03))
    objs[-1].location = (0, -0.12, 0)
    objs.append(extruded("Cup", leaf((0.8, 0.28), (-0.26, 0.66), 0.14), M("BladeCup", "#c88a1c", shine=0.4), 0.02, 0))
    objs[-1].location = (0, -0.2, 0)
    objs.append(sphere("Bead", M("Bead", "#e03a3a", shine=0.9), 0.14, (0.84, 0, 0)))
    objs.append(C.tube_along("Body", [(0.72, 0, 0), (0.5, 0, 0), (0.05, 0, 0), (-0.42, 0, 0)], [0.1, 0.22, 0.25, 0.1],
                             M("Body", "#2a2c38", shine=1.0), 12))
    for x in (0.42, 0.02):
        objs.append(sphere("Dot", M("Dot", "#ffd23a", flat=True), 0.075, (x, -0.24, 0.03), scale=(1, 0.4, 1)))
    # dressed treble: a red / white hackle tuft over the hooks
    objs += treble(metal, (-0.62, 0, -0.02), 0.95, r=0.05)
    objs += strands("Hackle", M("HackR", "#e03a3a"), -0.5, -1.25, [(0.05, 0.12), (-0.05, -0.2)], y=-0.08, r=(0.07, 0.03))
    objs += strands("Hackle", M("HackW", "#f4f0e8"), -0.5, -1.2, [(0.0, -0.02)], y=-0.12, r=(0.07, 0.03))
    objs.append(fx_sparkle(C.glow_material("FxGlintM", "#fff8c8", 1.4), -0.15, 0.95, 0.1))
    return objs


def build_crank(metal):
    """크랭크베이트: fat fire-tiger body with a big diving lip, two trebles."""
    objs = []
    tiger = C.pattern_material("Tiger", "#c6e23a", "#2e5a26", kind="stripes", scale=2.6, thresh=0.24, axis=0, shine=1.0)
    objs.append(sphere("Body", tiger, 0.6, (0, 0, 0.02), scale=(1.4, 0.74, 1.02)))
    objs.append(sphere("Belly", M("Belly", "#f2822a", shine=0.8), 0.5, (0.04, -0.1, -0.26), scale=(1.42, 0.66, 0.62)))
    objs.append(C.poly_object("Lip", [(0.7, -0.1), (1.5, -0.52), (1.38, -0.84), (0.58, -0.4)],
                              M("LipM", "#cfe6f2", shine=1.2), thickness=0.06))
    objs.append(torus("Tie", metal, 0.09, 0.035, (1.12, 0, -0.44), rot=(math.radians(90), 0, 0)))
    objs += eye_dot(0.5, -0.38, 0.2, 0.17)
    objs += treble(metal, (0.08, 0, -0.56), 0.95, r=0.05)
    objs += treble(metal, (-0.86, 0, -0.04), 0.95, r=0.05)
    return objs


def build_kona(metal):
    """트롤링 루어: slant-faced blue resin head, orange / yellow skirt streaming back (the bubble trail is a game Fx)."""
    objs = []
    resin = M("Resin", "#2f8ad8", shine=1.3)
    head = C.tube_along("Head", [(0.2, 0, 0), (0.45, 0, 0), (0.85, 0, 0), (1.12, 0, 0)], [0.24, 0.32, 0.34, 0.3], resin, 16)
    for v in head.data.vertices:      # slant the face: the top of the face leans forward
        if v.co.x > 1.0:
            v.co.x += 0.3 * v.co.z
    head.data.update()
    objs.append(head)
    objs.append(C.poly_object("Insert", [(0.35, 0.02), (1.0, 0.1), (1.0, -0.04), (0.35, -0.12)],
                              M("Holo", "#e8f6ff", shine=1.2), y=-0.33))
    objs += eye_dot(0.88, -0.36, 0.06, 0.13, iris="#f4f4f4")
    # skirt: gathered collar, then strands flaring out towards the tips, orange and yellow alternating
    orange = M("SkirtO", "#ff6a2a", shine=0.5)
    yellow = M("SkirtY", "#ffd24a", shine=0.5)
    objs.append(C.tube_along("Collar", [(0.28, 0, 0), (0.05, 0, 0), (-0.25, 0, 0)], [0.26, 0.3, 0.31], orange, 12))
    fan = [(0.24, 0.62), (0.12, 0.3), (0.0, 0.0), (-0.12, -0.3), (-0.24, -0.62)]
    for k, f in enumerate(fan):
        objs += strands("Skirt", yellow if k % 2 else orange, -0.2, -1.62 + 0.08 * abs(k - 2), [f],
                        y=-0.1 + 0.05 * (k % 2), r=(0.1, 0.04), amp=0.07, waves=1.1 + 0.2 * k)
    objs += hook(metal, (-0.95, 0.0, -0.3), 1.05, r=0.05)
    return objs


def build_popper(metal):
    """포퍼: pearl body with a blue back, red cupped face turned to the camera, feather tail, splash drops."""
    from mathutils import Matrix
    objs = []
    pearl = split_material("Pearl", "#3a78d8", "#f4f2ea", z=0.14, shine=1.0)
    objs.append(C.tube_along("Body", [(-1.02, 0, 0.02), (-0.7, 0, 0.02), (0.0, 0, 0), (0.6, 0, 0), (0.9, 0, 0)],
                             [0.1, 0.24, 0.4, 0.45, 0.45], pearl, 16))
    objs.append(C.tube_along("Rim", [(0.86, 0, 0), (0.98, 0, 0)], 0.45, M("Face", "#e0343a", shine=0.6), 16))
    objs.append(C.tube_along("Cup", [(0.9, 0, 0), (0.995, 0, 0)], 0.3, M("CupIn", "#5a1424"), 16))
    objs.append(torus("Tie", metal, 0.08, 0.035, (1.03, 0, -0.02), rot=(0, math.radians(90), 0)))
    objs += eye_dot(0.62, -0.4, 0.14, 0.14)
    objs += treble(metal, (0.12, 0, -0.44), 0.95, r=0.05)
    objs += treble(metal, (-0.96, 0, -0.06), 0.9, r=0.05)
    objs += strands("Feather", M("FeatW", "#f4f0e8"), -1.0, -1.7, [(0.08, 0.2), (0.0, -0.08)], y=0.05, r=(0.08, 0.03))
    objs += strands("Feather", M("FeatR", "#e03a3a"), -1.0, -1.62, [(0.04, 0.05)], y=-0.08, r=(0.07, 0.03))
    # nose up (it sits tail-down in the water) and the cupped face turned towards the camera
    bpy.context.view_layer.update()
    R = Matrix.Rotation(math.radians(-30), 4, "Z") @ Matrix.Rotation(math.radians(-10), 4, "Y")
    for ob in objs:
        ob.matrix_world = R @ ob.matrix_world
    drop = C.glow_material("FxDropM", "#c8f0ff", 1.15)
    for (x, z, rr) in ((1.3, 0.62, 0.12), (1.55, 0.3, 0.1), (1.2, 0.95, 0.08)):
        objs.append(sphere("FxDrop", drop, rr, (x, -1.0, z), seg=10, rings=6))
    return objs


def build_softworm(metal):
    """소프트 웜: purple ribbed grub with a curly tail on a red round jig head."""
    objs = []
    body = C.pattern_material("Grub", "#9a5ae0", "#6c32b4", kind="stripes", scale=7, thresh=0.3, axis=0, shine=0.6)
    pts = [(0.74, 0, 0.0), (0.4, 0, 0.0), (0.0, 0, -0.02), (-0.4, 0, -0.03), (-0.62, 0, -0.02)]
    objs.append(C.tube_along("Grub", pts, [0.2, 0.21, 0.19, 0.15, 0.11], body, 12))
    # curly tail: leaves the body going left, curls down and forward underneath (radius shrinking, paddle at the end)
    cx, cz = -0.62, -0.38
    tp, tr = [], []
    n = 18
    for i in range(n):
        u = i / (n - 1)
        a = math.radians(90 + 250 * u)
        rad = 0.36 - 0.14 * u
        tp.append((cx + rad * math.cos(a), 0, cz + rad * math.sin(a)))
        tr.append(0.1 - 0.03 * math.sin(math.pi * min(1, u / 0.5)) + 0.07 * max(0.0, (u - 0.55) / 0.45) * (1 if u < 0.95 else 0.4))
    objs.append(C.tube_along("Tail", tp, tr, M("TailM", "#a868ea", shine=0.6), 10))
    objs.append(sphere("Head", M("JigHead", "#e04a2a", shine=0.9), 0.3, (0.94, 0, 0.02), scale=(1.12, 0.9, 1.0)))
    objs += eye_dot(1.06, -0.26, 0.1, 0.11)
    objs.append(torus("Tie", metal, 0.08, 0.035, (0.98, 0, 0.4), rot=(math.radians(90), 0, 0)))
    # the hook point comes out of the grub's back
    objs.append(C.tube_along("Point", [(0.3, 0, 0.1), (0.2, 0, 0.36), (0.3, 0, 0.47)], 0.045, metal, 6))
    return objs


def build_egi(metal):
    """야광 에기: shrimp-shaped cloth body, cyan glowing belly and fins, lead under the chin, pin crown at the tail."""
    objs = []
    cloth = C.pattern_material("Cloth", "#f2904a", "#cc5a36", kind="stripes", scale=4.2, thresh=0.3, axis=0, shine=0.6)
    xs = [1.02, 0.85, 0.55, 0.1, -0.4, -0.8, -1.0]
    rs = [0.09, 0.25, 0.31, 0.28, 0.21, 0.13, 0.08]
    objs.append(C.tube_along("Body", [(x, 0, 0.04 + 0.03 * math.sin(x)) for x in xs], rs, cloth, 14))
    glow = C.glow_material("EgiGlow", "#5ae8dc", 1.2)
    objs.append(C.tube_along("Belly", [(x, -0.05, -0.1) for x in xs[1:-1]], [r * 0.82 for r in rs[1:-1]], glow, 12))
    # feathered side fins (glowing, paler than the belly) sweeping back and down
    fin = C.glow_material("EgiFin", "#dcfff6", 1.1)
    objs.append(extruded("Fin", leaf((0.42, -0.08), (-0.28, -0.62), 0.13), fin, 0.04, 0))
    objs[-1].location = (0, -0.36, 0)
    objs.append(extruded("Fin2", leaf((0.08, -0.06), (-0.62, -0.5), 0.11), C.glow_material("EgiFin2", "#9af4ea", 1.1), 0.04, 0))
    objs[-1].location = (0, -0.32, 0)
    # lead sinker plate under the chin
    objs.append(box("Lead", M("Lead", "#50546a", shine=0.9), 0.62, 0.22, 0.13, loc=(0.74, 0, -0.27), bevel=0.05,
                    rot=(0, math.radians(16), 0)))
    objs += eye_dot(0.76, -0.27, 0.1, 0.13, iris="#ffd23a")
    objs.append(torus("Tie", metal, 0.08, 0.035, (1.14, 0, 0.06), rot=(math.radians(90), 0, 0)))
    # pin crown (kasa): two rows of wire spikes fanning back from the tail, tips curled forward
    for row, (x0, L) in enumerate(((-0.96, 0.36), (-1.04, 0.26))):
        for k in range(5):
            a = math.radians(-56 + 28 * k + 14 * row)
            tip = (x0 - L * math.cos(a), -0.02 * row, 0.04 + L * math.sin(a))
            curl = (tip[0] + 0.1, tip[1], tip[2] + 0.06 * math.copysign(1, a) if abs(a) > 0.1 else tip[2] + 0.08)
            objs.append(C.tube_along("Pin", [(x0 + 0.05, 0, 0.04), tip, curl], 0.045, metal, 6))
    g = C.glow_material("FxGlowM", "#b8fff4", 1.4)
    objs.append(fx_sparkle(g, -0.3, 0.72, 0.1))
    objs.append(fx_sparkle(g, 0.25, -0.72, 0.07))
    return objs


def build_bait(bid):
    metal = M("Metal", "#c8d0da", shine=1.0)
    objs = []
    if bid in ("bait_paste", "bait_golden"):
        col = "#d9c28e" if bid == "bait_paste" else "#ffcf30"
        mat = C.pattern_material("Dough", col, "#b8a070" if bid == "bait_paste" else "#ffe890", kind="noise",
                                 scale=6, thresh=0.55, shine=0.2 if bid == "bait_paste" else 1.2,
                                 emit=1.0 if bid == "bait_paste" else 1.15)
        objs += hook(metal, (0.25, 0.3, 0.9), 1.3)
        objs.append(bumpy_ball("Dough", mat, 0.55, (0.2, 0, 0.1)))
        if bid == "bait_golden":
            g = C.glow_material("Spark", "#fff8c0", 1.5)
            for (x, z, s) in ((-0.55, 0.65, 0.12), (0.85, 0.55, 0.09), (0.7, -0.45, 0.1)):
                objs.append(C.poly_object("Spark", [(x, z + s * 2), (x + s * 0.5, z + s * 0.5), (x + s * 2, z),
                                                     (x + s * 0.5, z - s * 0.5), (x, z - s * 2), (x - s * 0.5, z - s * 0.5),
                                                     (x - s * 2, z), (x - s * 0.5, z + s * 0.5)], g, y=-0.8))
    elif bid in ("bait_worm", "bait_glow", "bait_sandworm"):
        if bid == "bait_worm":
            mat = C.pattern_material("Worm", "#c9706a", "#a8504c", kind="stripes", scale=7, thresh=0.3, axis=0, shine=0.4)
        elif bid == "bait_glow":
            mat = C.pattern_material("Glow", "#6affc8", "#2ae08a", kind="stripes", scale=6, thresh=0.3, axis=0, shine=0.6, emit=1.35)
        else:
            mat = C.pattern_material("Sand", "#b83c3c", "#7a2424", kind="stripes", scale=9, thresh=0.35, axis=0, shine=0.5)
        pts = wiggle(14, 2.2, 0.28, 1.2, 0, -1.1)
        rr = [0.13 + 0.03 * math.sin(math.pi * k / 13) for k in range(14)]
        rr[0] *= 0.6
        rr[-1] *= 0.6
        objs.append(C.tube_along("Worm", pts, rr, mat, 10))
        if bid == "bait_sandworm":
            leg = M("Leg", "#e07a5a")
            for k in range(1, 13):
                x, _, z = pts[k]
                for sz in (-1, 1):
                    objs.append(sphere("Leg", leg, 0.05, (x, -0.05, z + sz * 0.16), seg=6, rings=4))
        objs += hook(metal, (0.1, 0.25, 0.75), 1.2)
    elif bid == "bait_corn":
        mat = M("Corn", "#f2cc32", shine=0.8)
        tip = M("Tip", "#f8e8a0")
        objs += hook(metal, (0.0, 0.3, 0.9), 1.4)
        for (x, z) in ((-0.45, 0.0), (0.2, -0.1), (-0.1, 0.5)):
            objs.append(sphere("Kernel", mat, 0.36, (x, 0, z), scale=(1, 0.8, 0.85)))
            objs.append(sphere("KTip", tip, 0.12, (x - 0.1, -0.28, z - 0.2), scale=(1, 0.6, 1)))
    elif bid == "bait_shrimp":
        mat = C.pattern_material("Shrimp", "#f2906a", "#d86a4a", kind="stripes", scale=8, thresh=0.25, axis=0, shine=0.6)
        pts = []
        for k in range(12):
            a = math.radians(200 - 190 * k / 11)
            pts.append((0.9 * math.cos(a), 0, 0.7 * math.sin(a) * 0.8))
        rr = [0.12 + 0.2 * math.sin(math.pi * min(1, (k + 2) / 12)) for k in range(12)]
        rr[-1] = 0.05
        objs.append(C.tube_along("Body", pts, rr, mat, 10))
        objs.append(C.poly_object("Fan", [(0.85, -0.05), (1.35, 0.25), (1.3, -0.15), (1.15, -0.4)], mat, thickness=0.05))
        ant = M("Ant", "#e07a5a")
        objs.append(C.tube_along("Ant", [(-0.85, 0, -0.1), (-1.3, 0, 0.3), (-1.5, 0, 0.8)], 0.025, ant, 5))
        objs.append(C.tube_along("Ant2", [(-0.85, 0, -0.1), (-1.4, 0, 0.1), (-1.7, 0, 0.3)], 0.025, ant, 5))
        objs.append(sphere("Eye", M("Eye", "#101010", flat=True), 0.08, (-0.78, -0.18, 0.0)))
        objs += hook(metal, (0.0, 0.3, 0.8), 1.1)
    elif bid == "bait_spoon":
        mat = M("Spoon", "#dfe4ea", shine=1.4)
        objs.append(sphere("Spoon", mat, 0.6, (0, 0, 0), scale=(1.6, 0.25, 0.85)))
        objs.append(C.poly_object("Stripe", [(-0.5, 0.05), (0.4, 0.3), (0.5, 0.1), (-0.4, -0.15)], M("Red", "#d83a3a"), y=-0.17))
        objs.append(torus("Ring", mat, 0.12, 0.03, (-1.05, 0, 0.0), rot=(math.radians(90), 0, 0)))
        objs += treble(metal, (1.05, 0, 0.0), 1.0)
    elif bid == "bait_minnow":
        back = C.pattern_material("Min", "#3a9a4a", "#e8eef2", kind="stripes", scale=1.0, thresh=0.5, axis=2, shine=1.0)
        body = sphere("Body", back, 0.5, (0, 0, 0), scale=(2.2, 0.6, 0.75))
        objs.append(body)
        objs.append(C.poly_object("Lip", [(1.0, -0.05), (1.55, -0.45), (1.35, -0.55), (0.9, -0.25)], M("LipM", "#c8e0f0", shine=1), thickness=0.05))
        objs.append(sphere("Eye", M("EyeW", "#f8e040", flat=True), 0.13, (0.75, -0.35, 0.12), scale=(1, 0.4, 1)))
        objs.append(sphere("Pupil", M("EyeP", "#101010", flat=True), 0.07, (0.78, -0.42, 0.12), scale=(1, 0.4, 1)))
        objs += treble(metal, (-0.2, 0, -0.4), 0.8)
        objs += treble(metal, (-1.05, 0, -0.1), 0.8)
    elif bid == "bait_frog":
        g = C.pattern_material("Frog", "#5ab442", "#3a7a2a", kind="noise", scale=3, thresh=0.58, shine=0.6)
        objs.append(sphere("Body", g, 0.7, (0, 0, 0), scale=(1.2, 0.8, 0.7)))
        objs.append(sphere("Belly", M("Belly", "#f0e070"), 0.55, (0.1, -0.1, -0.2), scale=(1.1, 0.7, 0.5)))
        for sx in (-1, 1):
            objs.append(sphere("EyeB", g, 0.22, (0.55, -0.1 * sx, 0.45)))
        objs.append(sphere("Pupil", M("P", "#101010", flat=True), 0.1, (0.62, -0.3, 0.5), scale=(1, 0.5, 1)))
        objs.append(C.tube_along("LegB", [(-0.6, -0.2, -0.2), (-1.1, -0.3, -0.5), (-1.5, -0.3, -0.3)], 0.12, g, 8))
        objs.append(C.tube_along("LegF", [(0.5, -0.3, -0.3), (0.8, -0.35, -0.6)], 0.08, g, 8))
        objs += treble(metal, (-0.2, 0.2, 0.8), 0.9)
    elif bid == "bait_squid":
        mat = C.pattern_material("Squid", "#f4dcd2", "#d88a8a", kind="noise", scale=5, thresh=0.6, shine=0.6)
        objs.append(C.tube_along("Mantle", [(1.2, 0, 0.1), (0.6, 0, 0.05), (-0.2, 0, 0)], [0.02, 0.4, 0.42], mat, 14))
        objs.append(C.poly_object("Fin", [(1.25, 0.1), (0.7, 0.55), (0.7, -0.35)], mat, thickness=0.05))
        tm = M("Tent", "#e8b8b0")
        for k in range(5):
            dz = (k - 2) * 0.1
            objs.append(C.tube_along("Tent", [(-0.2, 0, dz), (-0.8, 0, dz * 1.5 + 0.05 * (k % 2)), (-1.3, 0, dz * 2.2)],
                                     [0.07, 0.05, 0.02], tm, 6))
        objs.append(sphere("Eye", M("E", "#101010", flat=True), 0.1, (-0.1, -0.35, 0.12), scale=(1, 0.5, 1)))
        objs += hook(metal, (0.3, 0.3, 0.6), 1.2)
    elif bid == "bait_jig":
        mat = C.pattern_material("Jig", "#3a7ae0", "#e25ab8", kind="stripes", scale=0.5, thresh=0.5, axis=2, shine=1.3)
        ob = C.tube_along("Jig", [(-1.2, 0, 0), (-0.8, 0, 0), (0.8, 0, 0), (1.2, 0, 0)], [0.05, 0.28, 0.28, 0.05], mat, 6)
        ob.rotation_euler = (math.radians(20), math.radians(-15), 0)
        objs.append(ob)
        objs.append(C.poly_object("Holo", [(-0.8, 0.02), (0.8, 0.02), (0.8, 0.1), (-0.8, 0.1)], M("Holo", "#f4f8ff", shine=1), y=-0.3))
        objs.append(torus("Ring", metal, 0.12, 0.03, (1.35, 0, 0.0), rot=(math.radians(90), 0, 0)))
        objs += hook(metal, (1.45, 0.1, -0.1), 0.8)
    elif bid == "bait_spinner":
        objs += build_spinner(metal)
    elif bid == "bait_crank":
        objs += build_crank(metal)
    elif bid == "bait_kona":
        objs += build_kona(metal)
    elif bid == "bait_popper":
        objs += build_popper(metal)
    elif bid == "bait_softworm":
        objs += build_softworm(metal)
    elif bid == "bait_egi":
        objs += build_egi(metal)
    else:
        raise ValueError(bid)
    return objs


LURES_NEW = ["bait_spinner", "bait_crank", "bait_kona", "bait_popper", "bait_softworm", "bait_egi"]
BAITS = ["bait_paste", "bait_worm", "bait_corn", "bait_shrimp", "bait_sandworm", "bait_spoon", "bait_minnow",
         "bait_frog", "bait_squid", "bait_jig", "bait_glow", "bait_golden"] + LURES_NEW
# the 10 lures by action (spec order 감기 / 저킹 / 수면 / 바닥 / 수직) with the action chip icon (UI_ICONS)
LURE_ACTIONS = [
    ("act_steady", ["bait_spinner", "bait_spoon", "bait_crank", "bait_kona"]),
    ("act_twitch", ["bait_minnow"]),
    ("act_top", ["bait_popper", "bait_frog"]),
    ("act_bottom", ["bait_softworm"]),
    ("act_vertical", ["bait_jig", "bait_egi"]),
]
LURE_PREVIEW = os.path.join(C.TMP, "legend_art")


def render_bait(bid):
    """Item icon (32 px) + in-water sprite (11 px, hook + bait, no Fx accents)."""
    C.clear_objects()
    build_bait(bid)
    render_icon(os.path.join(ITEMS, bid + ".png"))
    for ob in [o for o in bpy.context.scene.objects if o.name.startswith("Fx")]:
        bpy.data.objects.remove(ob, do_unlink=True)
    render_icon(os.path.join(WORLD, bid + "_w.png"), size=11, margin=0)
    return os.path.join(ITEMS, bid + ".png")


# ============================================================================ TANKS
def build_tank(level):
    w = [1.6, 2.1, 2.6, 3.1][level]
    h = [1.1, 1.3, 1.45, 1.6][level]
    objs = []
    frame = M("Frame", ["#6a4a2a", "#3a3a44", "#1c2a4a", "#d8b040"][level], shine=0.6)
    water = M("Water", "#5ab8e0", shine=0.4)
    objs.append(box("Water", water, w, 0.6, h * 0.85, loc=(0, 0, -0.05)))
    objs.append(box("Top", frame, w + 0.2, 0.7, 0.12, loc=(0, 0, h * 0.45), bevel=0.03))
    objs.append(box("Bot", frame, w + 0.2, 0.7, 0.18, loc=(0, 0, -h * 0.52), bevel=0.03))
    for sx in (-1, 1):
        objs.append(box("Side", frame, 0.1, 0.7, h, loc=(sx * (w / 2 + 0.05), 0, 0)))
    objs.append(box("Gravel", C.pattern_material("Grav", "#c8a878", "#8a6a4a", kind="noise", scale=18, thresh=0.5), w, 0.5, 0.18, loc=(0, -0.1, -h * 0.38)))
    pl = M("Plant", "#3ab04a")
    for k in range(level + 1):
        x = -w / 2 + 0.3 + k * 0.55
        objs.append(C.tube_along("Plant", [(x, -0.2, -h * 0.35), (x + 0.1, -0.2, 0), (x - 0.05, -0.2, h * 0.25)],
                                 [0.07, 0.05, 0.02], pl, 6))
    fish = M("Fish", ["#f08a2a", "#f0c030", "#e04040", "#b86aff"][level], shine=0.6)
    for k in range(level + 1):
        x = -w / 2 + 0.6 + k * 0.6
        z = 0.1 + 0.18 * ((k * 7) % 3 - 1)
        objs.append(sphere("F", fish, 0.18, (x, -0.35, z), scale=(1.4, 0.4, 0.9)))
        objs.append(C.poly_object("FT", [(x - 0.22, z), (x - 0.42, z + 0.15), (x - 0.42, z - 0.15)], fish, y=-0.35))
    return objs


# ============================================================================ UI ICONS
def star_pts(r1, r2, n=5, rot=90):
    pts = []
    for k in range(n * 2):
        a = math.radians(rot + 180 * k / n)
        r = r1 if k % 2 == 0 else r2
        pts.append((r * math.cos(a), r * math.sin(a)))
    return pts


def extruded(name, pts, mat, depth=0.3, bevel=0.06):
    ob = C.poly_object(name, pts, mat, thickness=depth)
    if bevel:
        md = ob.modifiers.new("bev", "BEVEL")
        md.width = bevel
        md.segments = 2
        md.limit_method = "NONE"
    return ob


def build_ui_icon(name):
    objs = []
    if name == "coin":
        gold = M("Gold", "#ffcc33", shine=1.2)
        dark = M("Gold2", "#d89a1a", shine=0.6)
        ax = Vector((0.25, -1, 0.05)).normalized()
        objs.append(C.tube_along("Coin", [ax * -0.15, ax * 0.15], 1.0, dark, 24))
        objs.append(C.tube_along("Face", [ax * 0.1, ax * 0.18], 0.78, gold, 24))
        objs.append(extruded("Fish", [(-0.35, 0.0), (-0.05, 0.22), (0.25, 0.1), (0.4, 0.28), (0.4, -0.28), (0.25, -0.1), (-0.05, -0.22)],
                             dark, depth=0.1, bevel=0))
        objs[-1].location = (0.02, -0.25, 0)
    elif name == "star":
        objs.append(extruded("Star", star_pts(1.0, 0.45), M("Star", "#ffd23a", shine=1.2), 0.35, 0.12))
    elif name == "star_empty":
        objs.append(extruded("Star", star_pts(1.0, 0.45), M("StarE", "#5a5a6a", shine=0.3), 0.35, 0.12))
    elif name == "lock":
        body = M("Lock", "#d8a83a", shine=1.0)
        objs.append(box("Body", body, 1.2, 0.5, 0.95, loc=(0, 0, -0.35), bevel=0.12))
        sh = []
        for k in range(13):
            a = math.pi * k / 12
            sh.append((0.38 * math.cos(a), 0, 0.15 + 0.5 * math.sin(a)))
        sh = [(0.38, 0, 0.0)] + sh + [(-0.38, 0, 0.0)]
        objs.append(C.tube_along("Shackle", sh, 0.1, M("Steel", "#b8c0c8", shine=1), 8))
        objs.append(box("Key", M("KeyH", "#3a2a10", flat=True), 0.16, 0.1, 0.35, loc=(0, -0.28, -0.4)))
    elif name == "icon_shop":
        red = M("Box", "#c83a3a", shine=0.7)
        objs.append(box("Box", red, 2.0, 1.0, 1.1, loc=(0, 0, -0.3), bevel=0.1))
        objs.append(box("Lid", M("Lid", "#e05a4a", shine=0.7), 2.1, 1.05, 0.35, loc=(0, 0, 0.35), bevel=0.1))
        objs.append(box("Latch", M("Latch", "#e8e8f0", shine=1), 0.3, 0.2, 0.3, loc=(0, -0.55, 0.15), bevel=0.04))
        pts = [(-0.55, 0, 0.5)] + [(0.55 * math.cos(math.pi * (1 - k / 8)), 0, 0.5 + 0.45 * math.sin(math.pi * k / 8)) for k in range(9)] + [(0.55, 0, 0.5)]
        objs.append(C.tube_along("Handle", pts, 0.08, M("Hd", "#2a2a2a"), 8))
    elif name == "icon_tank":
        objs = build_tank(1)
    elif name == "icon_book":
        cover = M("Cover", "#3a6ad0", shine=0.6)
        objs.append(box("Cover", cover, 1.6, 0.35, 2.0, loc=(0, 0, 0), bevel=0.08))
        objs.append(box("Pages", M("Pages", "#f4ecd8"), 1.45, 0.3, 1.85, loc=(0.1, 0.05, 0), bevel=0.02))
        objs.append(box("Spine", M("Spine", "#2a4aa0"), 0.25, 0.4, 2.0, loc=(-0.72, 0, 0), bevel=0.05))
        f = M("Emb", "#ffd23a", shine=1)
        objs.append(extruded("Emb", [(-0.45, 0.0), (0.0, 0.3), (0.35, 0.1), (0.55, 0.3), (0.55, -0.3), (0.35, -0.1), (0.0, -0.3)], f, 0.05, 0))
        objs[-1].location = (0.05, -0.2, 0.1)
    elif name == "icon_map":
        paper = C.pattern_material("Map", "#e8d8a8", "#d8c088", kind="noise", scale=4, thresh=0.6)
        for k, (x, rot) in enumerate(((-0.75, 8), (0, -8), (0.75, 8))):
            ob = box("Panel", paper, 0.75, 0.1, 1.7, loc=(x, 0, 0), bevel=0.02)
            ob.rotation_euler = (0, 0, math.radians(rot))
            objs.append(ob)
        objs.append(C.tube_along("Path", [(-0.9, -0.3, -0.5), (-0.3, -0.3, 0.1), (0.2, -0.3, -0.2), (0.7, -0.3, 0.4)], 0.05, M("Path", "#c83a3a"), 6))
        objs.append(extruded("X", [(0.55, 0.45), (0.65, 0.55), (0.75, 0.45), (0.85, 0.55), (0.95, 0.45), (0.85, 0.35), (0.95, 0.25),
                                   (0.85, 0.15), (0.75, 0.25), (0.65, 0.15), (0.55, 0.25), (0.65, 0.35)], M("Xm", "#c83a3a"), 0.05, 0))
        objs[-1].location = (-0.05, -0.35, 0)
    elif name == "icon_back":
        objs.append(extruded("Arrow", [(-1.0, 0.0), (-0.1, 0.8), (-0.1, 0.35), (0.9, 0.35), (0.9, -0.35), (-0.1, -0.35), (-0.1, -0.8)],
                             M("Arr", "#f4f0e0", shine=0.8), 0.3, 0.1))
    elif name == "icon_bite":
        circle = [(math.cos(math.radians(90 + 18 * k)), math.sin(math.radians(90 + 18 * k)) * 0.9 + 0.2) for k in range(20)]
        objs.append(extruded("Bubble", circle, M("Bub", "#fbfbf6", shine=0.3), 0.2, 0.05))
        objs.append(extruded("Tail", [(-0.3, -0.5), (-0.6, -1.05), (0.1, -0.55)], M("Bub2", "#fbfbf6"), 0.2, 0))
        red = M("Ex", "#e03a2a", shine=0.6)
        objs.append(extruded("Ex1", [(-0.14, 0.95), (0.14, 0.95), (0.09, 0.0), (-0.09, 0.0)], red, 0.1, 0))
        objs[-1].location = (0, -0.2, 0)
        objs.append(extruded("Ex2", [(-0.12, -0.35), (0.12, -0.35), (0.12, -0.12), (-0.12, -0.12)], red, 0.1, 0))
        objs[-1].location = (0, -0.2, 0)
    elif name == "icon_sound":
        wm = M("Spk", "#f4f0e0", shine=0.6)
        objs.append(extruded("Spk", [(-0.9, 0.3), (-0.4, 0.3), (0.2, 0.8), (0.2, -0.8), (-0.4, -0.3), (-0.9, -0.3)], wm, 0.3, 0.06))
        for r in (0.5, 0.85):
            pts = [(0.3 + r * math.cos(a), 0, r * math.sin(a)) for a in [math.radians(-50 + 100 * k / 8) for k in range(9)]]
            objs.append(C.tube_along("Wave", pts, 0.07, wm, 6))
    elif name == "icon_mute":
        wm = M("Spk", "#8a8a96", shine=0.6)
        objs.append(extruded("Spk", [(-0.9, 0.3), (-0.4, 0.3), (0.2, 0.8), (0.2, -0.8), (-0.4, -0.3), (-0.9, -0.3)], wm, 0.3, 0.06))
        red = M("X", "#e03a2a")
        objs.append(C.tube_along("X1", [(0.4, -0.3, 0.35), (0.95, -0.3, -0.35)], 0.08, red, 6))
        objs.append(C.tube_along("X2", [(0.4, -0.3, -0.35), (0.95, -0.3, 0.35)], 0.08, red, 6))
    elif name == "icon_fish":
        fm = M("Fi", "#f4f0e0", shine=0.6)
        objs.append(sphere("Body", fm, 0.6, (0, 0, 0), scale=(1.5, 0.4, 0.9)))
        objs.append(extruded("Tail", [(-0.8, 0.0), (-1.4, 0.55), (-1.25, 0.0), (-1.4, -0.55)], fm, 0.1, 0))
        objs.append(sphere("Eye", M("E", "#1a1a2a", flat=True), 0.1, (0.55, -0.3, 0.12), scale=(1, 0.4, 1)))
    elif name == "icon_xp":
        objs.append(extruded("Star", star_pts(1.0, 0.55, 4, 90), M("XP", "#6ae0ff", shine=1.2), 0.3, 0.1))
    elif name == "icon_check":
        objs.append(extruded("Chk", [(-0.9, 0.1), (-0.55, 0.45), (-0.25, 0.1), (0.6, 0.95), (0.95, 0.6), (-0.25, -0.6)],
                             M("Ck", "#5ad05a", shine=0.8), 0.3, 0.08))
    elif name == "icon_bag":
        bm = M("Bag", "#b87a3a", shine=0.5)
        objs.append(sphere("Sack", bm, 0.9, (0, 0, -0.2), scale=(1.0, 0.7, 0.9)))
        objs.append(C.tube_along("Neck", [(0, 0, 0.5), (0, 0, 0.8)], [0.35, 0.45], bm, 12))
        objs.append(torus("Tie", M("Tie", "#e8c860"), 0.3, 0.07, (0, 0, 0.55)))
        objs.append(extruded("Sign", [(-0.2, 0.3), (0.2, 0.3), (0.2, -0.5), (-0.2, -0.5)], M("S", "#ffd23a"), 0.05, 0))
        objs[-1].location = (0, -0.65, -0.1)
    elif name in ACT_CHIPS:
        objs = build_act_chip(name)
    return objs


# lure action chips (12 px pictograms in the action colour; spec 1.8): name -> (colour, darker accent)
ACT_CHIPS = {
    "act_steady": ("#6ab8ff", "#1f3f73"),     # 감기: a reel with its crank (winding)
    "act_twitch": ("#6ad06a", "#35883a"),     # 저킹: a zig-zag dart arrow
    "act_top": ("#ffd24a", "#c89020"),        # 수면: surface wave + splash drops
    "act_bottom": ("#b8865a", "#6e4a2c"),     # 바닥: arrow down onto the bottom
    "act_vertical": ("#b58aff", "#6e4ac8"),   # 수직: up / down arrow (lift, then fall)
}


def arrow_head(name, mat, tip, direction, length, half_w, depth=2.0, bevel=0.5):
    """Flat extruded triangle (x, z) pointing along `direction`."""
    d = Vector((direction[0], direction[1])).normalized()
    n = Vector((-d.y, d.x))
    t = Vector(tip)
    b = t - d * length
    return extruded(name, [tuple(t), tuple(b + n * half_w), tuple(b - n * half_w)], mat, depth, bevel)


def teardrop(cx, cz, r, tip_len, ang=90.0, n=14):
    """Drop outline (x, z): round end of radius r at (cx, cz), point `tip_len` from the centre towards `ang` deg."""
    a0 = math.radians(ang)
    side = math.asin(min(0.99, r / tip_len))          # tangent points of the tip lines: a0 +- (90 deg - side)
    pts = [(cx + tip_len * math.cos(a0), cz + tip_len * math.sin(a0))]
    for k in range(n + 1):
        a = a0 + math.pi / 2 - side + (math.pi + 2 * side) * k / n
        pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def build_act_chip(name):
    """Authored in PIXEL units (1 unit = 1 px at the 12 px size: every chip's larger extent is 10 units, leaving
    1 px each side for the outline). Flat extruded shapes with a bevel so the toon light gives a lit top-left rim."""
    col, dark = ACT_CHIPS[name]
    mat = M("Chip", col, shine=0.8)
    acc = M("ChipDark", dark, shine=0.3)
    objs = []
    if name == "act_steady":            # a reel with its crank (a circle arrow does not survive 12 px)
        def disc(cx, cz, r, n=20):
            return [(cx + r * math.cos(2 * math.pi * k / n), cz + r * math.sin(2 * math.pi * k / n)) for k in range(n)]
        objs.append(extruded("Reel", disc(-1.4, -1.4, 3.4), mat, 2.0, 0.4))
        objs.append(extruded("Hub", disc(-1.4, -1.4, 1.2, 12), acc, 1.0, 0.2))
        objs[-1].location = (0, -1.2, 0)
        d = Vector((1, 1)).normalized()
        n = Vector((-d.y, d.x)) * 0.75
        a0, a1 = Vector((-1.4, -1.4)), Vector((3.2, 3.2))
        objs.append(extruded("Arm", [tuple(a0 + n), tuple(a1 + n), tuple(a1 - n), tuple(a0 - n)], acc, 1.0, 0.2))
        objs[-1].location = (0, -1.4, 0)
        objs.append(extruded("Knob", disc(3.4, 3.4, 1.5, 12), mat, 2.0, 0.4))
        objs[-1].location = (0, -1.8, 0)
    elif name == "act_twitch":          # zig-zag dart
        pts = [(-4.0, 0, -4.0), (-1.6, 0, 0.6), (0.4, 0, -1.8), (2.0, 0, 1.4)]
        objs.append(C.tube_along("Zig", pts, 1.0, mat, 10))
        d = Vector((1.6, 3.2)).normalized()
        objs.append(arrow_head("Head", mat, tuple(Vector((2.0, 1.4)) + d * 3.4), tuple(d), 3.6, 2.4))
    elif name == "act_top":             # one drop over the surface wave (three splash drops read as a crown at 12 px)
        objs.append(C.tube_along("Wave", [(-4.1 + 8.2 * k / 20, 0, -3.9 + 0.7 * math.sin(math.pi * 3 * k / 20))
                                          for k in range(21)], 0.95, acc, 8))
        objs.append(extruded("Drop", teardrop(0.0, 0.5, 1.8, 4.5, 90, 18), mat, 2.0, 0.35))
    elif name == "act_bottom":          # arrow down onto the bottom
        objs.append(extruded("Shaft", [(-1.0, 5.0), (1.0, 5.0), (1.0, 0.6), (-1.0, 0.6)], mat, 2.0, 0.3))
        objs.append(arrow_head("Head", mat, (0.0, -2.4), (0, -1), 3.4, 3.8, bevel=0.3))
        objs.append(extruded("Floor", [(-5.0, -3.6), (5.0, -3.6), (5.0, -5.0), (-5.0, -5.0)], acc, 2.0, 0.3))
    elif name == "act_vertical":        # lift up, fall down
        objs.append(extruded("Shaft", [(-1.0, 2.6), (1.0, 2.6), (1.0, -2.6), (-1.0, -2.6)], mat, 2.0, 0.3))
        objs.append(arrow_head("Up", mat, (0.0, 5.0), (0, 1), 2.6, 3.4, bevel=0.3))
        objs.append(arrow_head("Down", acc, (0.0, -5.0), (0, -1), 2.6, 3.4, bevel=0.3))
    return objs


# the legend encounter's HUD icons (spec 3.1): mood 12 px, verb 16 px, the seen-legend eye 16 px; name -> (colour, accent, size)
ENC_ICONS = {
    "mood_wary": ("#7a8aa8", "#34405c", 12),      # 경계: a narrowed eye glancing aside under a low brow
    "mood_curious": ("#6affea", "#1f8a80", 12),   # 호기심: "?"
    "mood_excited": ("#ffc830", "#b8700c", 12),   # 흥분: "!" with a spark
    "verb_wind": ("#6ab8ff", "#1f3f73", 16),      # 감기: a clockwise circle arrow (winding in)
    "verb_flick": ("#6ad06a", "#35883a", 16),     # 톡: a short downward pull (down arrow) that flicks the rod tip up
    "verb_hold": ("#ffd24a", "#c89020", 16),      # 멈춤: two bars (pause)
    "verb_runpause": ("#6ab8ff", "#1f3f73", 16),  # 감다 멈춤: a short wind (verb_wind's arrow) then verb_hold's bars
    "icon_eye": ("#6ad8e0", "#1a2230", 16),       # 목격: an eye (legend seen / its key lures)
}


def lens(a, b, n=16, cx=0.0, cz=0.0):
    """Almond (x, z): tips at +-a, half height b."""
    pts = []
    for k in range(n + 1):
        t = math.pi * k / n
        pts.append((cx - a * math.cos(t), cz + b * math.sin(t) ** 0.85))
    for k in range(1, n):
        t = math.pi * k / n
        pts.append((cx + a * math.cos(t), cz - b * math.sin(t) ** 0.85))
    return pts


def build_enc_icon(name):
    """Authored in PIXEL units like the action chips (larger extent: size - 2 px for the outline)."""
    col, dark, _ = ENC_ICONS[name]
    mat = M("Enc", col, shine=0.8)
    acc = M("EncDark", dark, shine=0.3)
    objs = []

    def disc(cx, cz, r, n=16):
        return [(cx + r * math.cos(2 * math.pi * k / n), cz + r * math.sin(2 * math.pi * k / n)) for k in range(n)]

    if name == "mood_wary":
        # a heavy lid over the top half, the pupil in the corner: a sidelong, narrowed look
        objs.append(extruded("Eye", lens(5.0, 3.0), M("White", "#e4ebf2", shine=0.4), 2.0, 0.3))
        objs.append(extruded("Pupil", disc(2.4, -0.9, 1.4, 12), acc, 1.0, 0.2))
        objs[-1].location = (0, -1.2, 0)
        lid = [(-5.4, 0.2), (5.4, -0.4)] + [(5.4 - 10.8 * k / 8, 0.2 + 3.4 * math.sin(math.pi * k / 8) ** 0.8) for k in range(1, 8)]
        objs.append(extruded("Lid", lid, mat, 2.0, 0.3))
        objs[-1].location = (0, -1.6, 0)
    elif name == "mood_curious":
        arc = [(2.6 * math.cos(math.radians(a)), 0, 1.9 + 2.6 * math.sin(math.radians(a))) for a in range(160, -71, -23)]
        arc += [(0.2, 0, -1.4)]
        objs.append(C.tube_along("Q", arc, 1.05, mat, 10))
        objs.append(extruded("Dot", disc(0.2, -3.9, 1.2, 12), mat, 2.0, 0.3))
    elif name == "mood_excited":
        objs.append(extruded("Bar", [(-2.4, 5.0), (0.2, 5.0), (-0.4, -1.4), (-1.8, -1.4)], mat, 2.0, 0.3))
        objs.append(extruded("Dot", disc(-1.1, -3.8, 1.2, 12), mat, 2.0, 0.3))
        objs.append(extruded("Spark", star_pts(2.0, 0.7, 4, 90), acc, 1.2, 0.2))
        objs[-1].location = (3.0, -0.6, 2.2)
    elif name == "verb_wind":
        ring = [(5.0 * math.cos(math.radians(a)), 0, 5.0 * math.sin(math.radians(a))) for a in range(120, -181, -20)]
        objs.append(C.tube_along("Ring", ring, 1.1, mat, 10))
        end = Vector((ring[-1][0], ring[-1][2]))
        tangent = Vector((0.0, 1.0))          # clockwise at 180 deg: heading up
        objs.append(arrow_head("Head", mat, tuple(end + tangent * 2.8), tuple(tangent), 4.2, 3.2, bevel=0.3))
        objs.append(extruded("Hub", disc(0, 0, 1.4, 12), acc, 1.0, 0.2))
    elif name == "verb_flick":
        # 톡 = a short quick pull DOWN (the finger: the green down arrow) that jerks the rod tip UP (the dark rod beside
        # it, bending up to a small arrowhead)
        objs.append(C.tube_along("Pull", [(-2.2, 0, 5.8), (-2.2, 0, -0.8)], 1.1, mat, 10))
        objs.append(arrow_head("Head", mat, (-2.2, -6.4), (0, -1), 4.4, 3.4, bevel=0.3))
        rod = [(1.6, 0, -5.0), (2.7, 0, -1.8), (3.6, 0, 1.2), (4.1, 0, 3.4)]
        objs.append(C.tube_along("Rod", rod, 0.65, acc, 8))
        d = Vector((0.2, 1.0)).normalized()
        objs.append(arrow_head("Tip", acc, tuple(Vector((4.1, 3.4)) + d * 3.0), tuple(d), 2.8, 2.2, depth=1.6, bevel=0.2))
    elif name == "verb_hold":
        objs.append(extruded("BarL", [(-4.6, -6.0), (-1.4, -6.0), (-1.4, 6.0), (-4.6, 6.0)], mat, 2.0, 0.35))
        objs.append(extruded("BarR", [(1.4, -6.0), (4.6, -6.0), (4.6, 6.0), (1.4, 6.0)], mat, 2.0, 0.35))
    elif name == "verb_runpause":
        # a short wind run, then a pause (RunPause): verb_wind's clockwise circle arrow with verb_hold's yellow pause
        # bars in place of its hub (side by side, the two do not survive 16 px)
        ring = [(5.0 * math.cos(math.radians(a)), 0, 5.0 * math.sin(math.radians(a))) for a in range(120, -181, -20)]
        objs.append(C.tube_along("Ring", ring, 1.1, mat, 10))
        end = Vector((ring[-1][0], ring[-1][2]))
        tangent = Vector((0.0, 1.0))
        objs.append(arrow_head("Head", mat, tuple(end + tangent * 2.8), tuple(tangent), 4.2, 3.2, bevel=0.3))
        bars = M("EncHold", ENC_ICONS["verb_hold"][0], shine=0.8)
        # (the ring's centre lands on a pixel corner at 16 px: 2 px bars either side of a 2 px gap, a thin bevel so the
        # shaded right side does not eat half of each)
        objs.append(extruded("BarL", [(-2.95, -2.75), (-1.05, -2.75), (-1.05, 2.75), (-2.95, 2.75)], bars, 2.0, 0.1))
        objs.append(extruded("BarR", [(1.05, -2.75), (2.95, -2.75), (2.95, 2.75), (1.05, 2.75)], bars, 2.0, 0.1))
    elif name == "icon_eye":
        objs.append(extruded("Eye", lens(7.0, 3.6), M("White", "#eef6f2", shine=0.4), 2.0, 0.3))
        objs.append(extruded("Iris", disc(0, 0, 2.9, 16), mat, 1.0, 0.2))
        objs[-1].location = (0, -1.2, 0)
        objs.append(extruded("Pupil", disc(0, 0, 1.3, 12), acc, 1.0, 0.1))
        objs[-1].location = (0, -1.6, 0)
        objs.append(extruded("Shine", disc(-1.0, 1.0, 0.6, 8), M("Shine", "#ffffff"), 1.0, 0.0))
        objs[-1].location = (0, -2.0, 0)
    return objs


UI_ICONS = {  # name: size
    "coin": 16, "star": 16, "star_empty": 16, "lock": 16, "icon_shop": 32, "icon_tank": 32, "icon_book": 32,
    "icon_map": 32, "icon_back": 16, "icon_bite": 16, "icon_sound": 16, "icon_mute": 16, "icon_fish": 16,
    "icon_xp": 16, "icon_check": 16, "icon_bag": 32,
    "act_steady": 12, "act_twitch": 12, "act_top": 12, "act_bottom": 12, "act_vertical": 12,
}


# ============================================================================ 9-SLICE FRAMES
def frame_render(path, objs, w, h):
    """ppu = 1 px per unit; objects are authored in pixel units centred on the origin."""
    bpy.context.view_layer.update()
    C.ortho_camera(0, 0, w, h, 1.0)
    C.render_sprite(path, outline=True, outline_mul=0.35)


def build_frames():
    out = {}
    # wooden panel with paper inset (48x48, border 14)
    C.clear_objects()
    wood = C.pattern_material("Wood", "#9a6232", "#80502a", kind="stripes", scale=0.35, thresh=0.5, axis=2, shine=0.5)
    paper = M("Paper", "#f2e6c8", shine=0.0)
    box("Frame", wood, 46, 6, 46, bevel=4, seg=3)
    box("Inner", M("Lip", "#5a3418"), 30, 6, 30, loc=(0, -2, 0), bevel=1, seg=1)
    box("Paper", paper, 28, 6, 28, loc=(0, -3.5, 0), bevel=1.5, seg=2)
    for sx in (-1, 1):
        for sz in (-1, 1):
            sphere("Nail", M("Nail", "#d8c8a0", shine=1), 1.5, (sx * 18.5, -3.5, sz * 18.5), seg=8, rings=5)
    frame_render(os.path.join(UI, "panel_wood.png"), None, 48, 48)
    out["panel_wood"] = 14
    # dark translucent-ish HUD panel (32x32, border 9)
    C.clear_objects()
    box("Dark", M("Dark", "#243448", shine=0.3), 30, 6, 30, bevel=3, seg=3)
    box("DarkIn", M("DarkIn", "#1a2636"), 24, 6, 24, loc=(0, -1.5, 0), bevel=1, seg=1)
    frame_render(os.path.join(UI, "panel_dark.png"), None, 32, 32)
    out["panel_dark"] = 9
    # paper card (32x32, border 8)
    C.clear_objects()
    box("Card", M("Card", "#f6ecd2"), 30, 6, 30, bevel=3, seg=3)
    box("CardIn", M("CardIn", "#e8d8b0"), 24, 6, 24, loc=(0, -1.0, 0), bevel=0.5, seg=1)
    frame_render(os.path.join(UI, "panel_paper.png"), None, 32, 32)
    out["panel_paper"] = 8
    # buttons (32x24, border 9)
    for name, col, dark in (("green", "#5ac25a", "#2e7a34"), ("blue", "#4a90e2", "#2a58a0"), ("red", "#e2504a", "#9a2a2a"),
                            ("yellow", "#f2c232", "#b0801a"), ("grey", "#9aa0aa", "#5a606a")):
        C.clear_objects()
        box("Base", M("Base", dark), 30, 6, 20, loc=(0, 0, -1), bevel=4, seg=3)
        box("Top", M("Top", col, shine=0.4), 28, 6, 17, loc=(0, -2, 1), bevel=3.5, seg=3)
        box("Hi", M("Hi", "#ffffff", flat=True, emit=0.9), 18, 1, 1.4, loc=(-2, -5.5, 7.3))
        frame_render(os.path.join(UI, f"btn_{name}.png"), None, 32, 24)
        out[f"btn_{name}"] = 9
    # item slot (24x24, border 7)
    C.clear_objects()
    box("Slot", M("Slot", "#7a5a3a"), 22, 6, 22, bevel=2.5, seg=2)
    box("SlotIn", M("SlotIn", "#4a3422"), 17, 6, 17, loc=(0, -1.0, 0), bevel=0.5, seg=1)
    frame_render(os.path.join(UI, "slot.png"), None, 24, 24)
    out["slot"] = 7
    # bars (16x10, border 4) - white fill is tinted in Unity
    C.clear_objects()
    box("BarBg", M("BarBg", "#1a1a24"), 14, 6, 8, bevel=2.5, seg=2)
    frame_render(os.path.join(UI, "bar_bg.png"), None, 16, 10)
    out["bar_bg"] = 4
    C.clear_objects()
    box("BarFill", M("BarFill", "#ffffff", stops=[(0.0, C.lin("#b8b8c8")), (0.45, C.lin("#ffffff"))], shine=0.3), 14, 6, 8, bevel=2.5, seg=2)
    frame_render(os.path.join(UI, "bar_fill.png"), None, 16, 10)
    out["bar_fill"] = 4
    # badge pill (white, tinted) 16x12 border 5
    C.clear_objects()
    box("Badge", M("Badge", "#ffffff", stops=[(0.0, C.lin("#c0c0cc")), (0.45, C.lin("#ffffff"))]), 14, 6, 10, bevel=4, seg=3)
    frame_render(os.path.join(UI, "badge.png"), None, 16, 12)
    out["badge"] = 5
    return out


def build_reel_widget():
    # reel face (64x64) and handle (64x64, pivot = centre)
    C.clear_objects()
    rim = M("Rim", "#c8ccd4", shine=1.0)
    ax = Vector((0, -1, 0))
    C.tube_along("Rim", [ax * -1.5, ax * 1.5], 29, rim, 48)
    C.tube_along("Face", [ax * 1.4, ax * 2.2], 24, M("FaceM", "#3a4a5a", shine=0.4), 48)
    C.tube_along("Line", [ax * 2.1, ax * 2.6], 17, M("LineM", "#e8f0f4", shine=0.3), 40)
    C.tube_along("Hub", [ax * 2.5, ax * 4.0], 7, rim, 24)
    for k in range(6):
        a = math.radians(60 * k)
        cx, cz = math.cos(a) * 12, math.sin(a) * 12
        C.tube_along("Hole", [(cx, -2.3, cz), (cx, -2.8, cz)], 2.2, M("HoleM", "#1a2230"), 12)
    bpy.context.view_layer.update()
    C.ortho_camera(0, 0, 64, 64, 1.0)
    C.render_sprite(os.path.join(UI, "reel_face.png"), outline=True)
    C.clear_objects()
    arm = M("Arm", "#d8dce4", shine=1.0)
    C.tube_along("Arm", [(0, -5, 0), (24, -5, 0)], 2.6, arm, 12)
    sphere("Pivot", arm, 4.5, (0, -6, 0), scale=(1, 0.6, 1))
    C.tube_along("Knob", [(24, -6, 0), (24, -14, 0)], 4.2, M("KnobM", "#e2b432", shine=0.8), 16)
    sphere("KnobCap", M("KnobM2", "#f2d25a", shine=1.0), 4.2, (24, -14, 0), scale=(1, 0.4, 1))
    bpy.context.view_layer.update()
    C.ortho_camera(0, 0, 64, 64, 1.0)
    C.render_sprite(os.path.join(UI, "reel_handle.png"), outline=True)


# reel-gesture help arrow: (dark, base, highlight) ramps, hue-shifted shade / warm highlight
REEL_ARROWS = {
    "reel_arrow_wind": ("#23845e", "#6ae06a", "#d2ff9e"),   # green: winding in
    "reel_arrow_give": ("#c23c3a", "#ff8a4a", "#ffe08e"),   # orange: giving line
}
REEL_ARROW_R = 34.0        # arc radius (px) around the image centre = reel centre (reel_face rim is at 29)
REEL_ARROW_SPAN = 55.0     # degrees, counter-clockwise from 0 (3 o'clock); the head sits at the 55 deg end
REEL_ARROW_TUBE = 3.0      # body radius (px), 2.4 at the tail
REEL_ARROW_HEAD = (10.0, 5.2)  # head length along the arc and half-width at its base (px)


def arrow_material(name, dark, base, hi, t_base=0.6, t_hi=0.92, towards=(0.0, -1.0, 0.0)):
    """Toon ramp lit straight from the camera, so the highlight runs along the tube's centre line and the
    shading stays correct however the game rotates the sprite (and when it is mirrored).
    `towards` = unit vector towards the 'light' (default: the ortho sprite camera)."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    geo = nb.node("ShaderNodeNewGeometry")
    d = nb.vmath("DOT_PRODUCT", geo.outputs["Normal"], tuple(towards))
    col = nb.ramp(d, [(0.0, C.lin(dark)), (t_base, C.lin(base)), (t_hi, C.lin(hi))])
    nb.output_emission(col, 1.0)
    return m


def build_reel_arrow():
    """Curved 3D arrow for the reel-gesture help (88x88, image centre = reel centre, 1 unit = 1 px).
    A short arc hugging the reel rim (REEL_ARROW_R / _SPAN); the head points counter-clockwise.
    Mirroring the sprite horizontally gives the clockwise version (lighting is symmetric)."""
    R = REEL_ARROW_R
    a0, a1 = 0.0, math.radians(REEL_ARROW_SPAN)

    def on_arc(a, r=R):
        return (math.cos(a) * r, 0.0, math.sin(a) * r)

    out = []
    for name, (dark, base, hi) in REEL_ARROWS.items():
        C.clear_objects()
        mat = arrow_material(name, dark, base, hi)
        # body: slightly thinner at the tail, full radius towards the head
        n = 32
        angs = [a0 + (a1 - a0) * i / (n - 1) for i in range(n)]
        radii = []
        for i in range(n):
            u = min(1.0, i / (n - 1) / 0.45)
            radii.append(REEL_ARROW_TUBE * 0.8 + REEL_ARROW_TUBE * 0.2 * (u * u * (3 - 2 * u)))
        C.tube_along("Body", [on_arc(a) for a in angs], radii, mat, 16)
        sphere("Tail", mat, radii[0], on_arc(a0), seg=16, rings=10)
        # head: a cone bent along the same arc, tip pointing counter-clockwise.
        # Its surface faces the camera less than the tube does, so its ramp is shifted to keep a highlight ridge.
        hmat = arrow_material(name + "_head", dark, base, hi, t_base=0.5, t_hi=0.8)
        head_len, head_w = REEL_ARROW_HEAD
        hl = head_len / R
        m = 10
        hangs = [a1 + hl * i / (m - 1) for i in range(m)]
        hr = [head_w * (1 - i / (m - 1)) + 0.15 for i in range(m)]
        C.tube_along("Head", [on_arc(a) for a in hangs], hr, hmat, 16)
        bpy.context.view_layer.update()
        C.ortho_camera(0, 0, 88, 88, 1.0)
        path = os.path.join(UI, name + ".png")
        C.render_sprite(path, outline=True)
        out.append(path)
    return out


# ============================================================================ CAST ARROW
# "Swing the rod up and throw it out there" help arrow for the flick cast's wind-up, over the angler's head: a straight
# gold block arrow (a flat slab in the shape of an arrow with real thickness: shaft + broad head with flared barbs and
# a rounded tip) lying along his forward direction a little above his hat and tilted up CAST_ARROW_POSE[2] deg, so
# from the game camera it points straight up the screen, away over the far water, the near butt wider than the far
# end (perspective plus a slight real taper). Its back face sits CAST_ARROW_SLAB[1] to his right of the front one (a
# sheared prism), so the camera sees the right wall and the near walls (the butt, the backs of the barbs) as the
# arrow's thickness, down and to the right of the front face. Rendered through the game's own camera (fk_persp /
# Persp.cs: 10.5 m behind and 4.75 m above the feet, pitched 12 deg down, f = 520 px) at 1:1, so its perspective and
# its size match the angler exactly. Model units = metres, the middle of his hat at the origin (Angler.HatPos2D:
# the head bone + 0.12 m, ~1.62 m above the feet), x = his right, y = away from the camera, z up.
# Frames (same silhouette): f0 = plain (the dim fade-in before armed, and the loop's rest), f1..fN = a light glint
# band running from the butt up to the tip - the game loops f1..fN, f0 (CastArrow.cs).
CAST_ARROWS = {  # (bottom, side, front, highlight): warm gold like the aim ring (#ffe64d), hue-shifted orange walls;
    "base": ("#c8641e", "#e8962c", "#ffe64d", "#fffbd8"),   # and the lighter palette of the glint band
    "glint": ("#f5b340", "#ffd35a", "#fffbe6", "#ffffff"),
}
CAST_ARROW_OUT = os.path.join(C.TMP, "castarrow4")  # review copies; the game wants them in Sprites/UI (Art.UI(name))
# The outline in the slab's own plane: x = his right, v = along the arrow from the butt (m).
CAST_ARROW_SHAFT = (0.125, 0.09, 1.00)       # half-width at the butt, half-width where the head starts, head start v
# barb half-width, how far the barb tips sweep back from the head start, tip v (before rounding), tip rounding
# radius, and the v squash the rounding is done in (the camera foreshortens v about this much: round on screen)
CAST_ARROW_HEAD = (0.245, 0.21, 1.86, 0.15, 0.55)
CAST_ARROW_SLAB = (0.07, 0.05)               # thickness, back face offset to his right
CAST_ARROW_POSE = (0.0, 0.25, 18.0)          # the butt's front edge: y (forward of the hat), z (above it); tilt up (deg)
CAST_ARROW_HILITE = 0.045                    # the front face's light strip inside its left edges (m)
CAST_ARROW_CAM = dict(back=10.5, up=3.13, pitch=12.0, f=520.0)   # the lake camera from the hat: behind, above, down
CAST_ARROW_GLINT = (7, 0.11)           # glint frames (butt to tip), band half-length (fraction of the arrow)
CAST_ARROW_PREVIEW_HAT = (240, 177)    # preview only: native px (from the top-left of the 480x270 view) of the hat


def cast_arrow_outline():
    """The front face, counter-clockwise from the butt's right corner: [(x, v)], plus the left slope's straight part
    ((x, v) at the barb tip, (x, v) where the rounding starts) for the highlight."""
    w0, w1, vh = CAST_ARROW_SHAFT
    W, back, La, r, k = CAST_ARROW_HEAD
    # round the tip in a space squashed along v by k (circular there = round on screen)
    A = Vector((0.0, La * k))
    P = Vector((W, (vh - back) * k))
    e = (P - A).normalized()
    beta = math.acos(max(-1.0, min(1.0, -e.y)))              # half the tip's angle
    cen = A + Vector((0.0, -r / math.sin(beta)))
    T = A + e * (r / math.tan(beta))
    a0 = math.atan2(T.y - cen.y, T.x - cen.x)
    arc = []
    n = 9
    for i in range(n):
        a = a0 + (math.pi - 2 * a0) * i / (n - 1)
        arc.append((cen.x + r * math.cos(a), (cen.y + r * math.sin(a)) / k))
    pts = [(w0, 0.0), (w1, vh), (W, vh - back)] + arc + [(-W, vh - back), (-w1, vh), (-w0, 0.0)]
    return pts, ((-W, vh - back), (-T.x, T.y / k))


def cast_arrow_length():
    return max(v for _, v in cast_arrow_outline()[0])


def cast_arrow_highlight(nb, x, v):
    """Shader mask: 1 on the front face within CAST_ARROW_HILITE inside the shaft's left edge and the head's left slope."""
    w0, w1, vh = CAST_ARROW_SHAFT
    back = CAST_ARROW_HEAD[1]
    sw = CAST_ARROW_HILITE
    edge = nb.math("MULTIPLY_ADD", v, -(w1 - w0) / vh, -w0)          # the shaft's left edge x at v
    shaft = nb.math("MULTIPLY", nb.math("LESS_THAN", nb.math("SUBTRACT", x, edge), sw), nb.math("LESS_THAN", v, vh))
    p0, p1 = (Vector(p) for p in cast_arrow_outline()[1])
    e = (p1 - p0).normalized()
    ni = Vector((e.y, -e.x))                                          # into the head
    d = nb.math("MULTIPLY_ADD", x, ni.x, nb.math("MULTIPLY_ADD", v, ni.y, -ni.dot(p0)))
    head = nb.math("MULTIPLY", nb.math("LESS_THAN", d, sw), nb.math("GREATER_THAN", v, vh - back))
    return nb.math("MAXIMUM", shaft, head)


def cast_arrow_material(name, cls, glint=None):
    """Flat colour by face class ("bottom" | "side" | "front"): CAST_ARROWS["base"], or its "glint" colours where the
    band passes (glint = (centre, half-length) as fractions of the length from the butt); the front face gets its
    light strip along the left edges."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    co = nb.sep(nb.node("ShaderNodeTexCoord").outputs["Object"])     # the slab's own x, v
    x, v = co[0], co[1]
    k = ("bottom", "side", "front").index(cls)
    base, hi = CAST_ARROWS["base"], CAST_ARROWS["glint"]
    col, gcol = C.lin(base[k]), C.lin(hi[k])
    if cls == "front":
        h = cast_arrow_highlight(nb, x, v)
        col = nb.mix(h, col, C.lin(base[3]))
        gcol = nb.mix(h, gcol, C.lin(hi[3]))
    if glint is not None:
        c, half = glint
        d = nb.math("ABSOLUTE", nb.math("MULTIPLY_ADD", v, 1.0 / cast_arrow_length(), -c))
        col = nb.mix(nb.math("LESS_THAN", d, half), col, gcol)
    nb.output_emission(col, 1.0)
    return m


def cast_arrow_cam_pos():
    p = CAST_ARROW_CAM
    return Vector((0.0, -p["back"], p["up"]))


def build_cast_arrow_mesh(glint=None):
    """The block arrow (one object, materials bottom / side / front), placed and tilted by CAST_ARROW_POSE.
    `glint` = (centre, half-length) of the light band (see cast_arrow_material). Returns (objs, the butt's
    front-edge middle)."""
    from mathutils import Matrix
    pts = cast_arrow_outline()[0]
    t, sx = CAST_ARROW_SLAB
    bm = bmesh.new()
    front = [bm.verts.new((x, v, 0.0)) for x, v in pts]
    back = [bm.verts.new((x + sx, v, -t)) for x, v in pts]
    bm.faces.new(front)
    bm.faces.new(back[::-1])
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[j], front[i], back[i], back[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:   # the front; the walls facing the butt end (towards the camera): "bottom"; the rest: "side"
        f.material_index = 2 if f.normal.z > 0.9 else (0 if f.normal.y < -0.5 else 1)
    ob = C.mesh_object("CastArrow", bm)
    for cls in ("bottom", "side", "front"):
        ob.data.materials.append(cast_arrow_material("cast_" + cls, cls, glint))
    y, z, tilt = CAST_ARROW_POSE
    ob.matrix_world = Matrix.Translation((0.0, y, z)) @ Matrix.Rotation(math.radians(tilt), 4, "X")
    return [ob], Vector((0.0, y, z))


def persp_camera(res, shift=(0.0, 0.0)):
    """The game camera (CAST_ARROW_CAM, from the hat) on a res x res canvas at 1 px = 1 game px, shifted by
    `shift` canvas widths."""
    p = CAST_ARROW_CAM
    cam = C.ortho_camera(0, 0, res, res, 1.0)
    cd = cam.data
    cd.type = "PERSP"
    cd.sensor_fit = "VERTICAL"
    cd.sensor_height = 24.0
    cd.lens = p["f"] * 24.0 / res
    cd.clip_start = 0.05
    cd.clip_end = 100.0
    cd.shift_x, cd.shift_y = shift
    cam.location = cast_arrow_cam_pos()
    cam.rotation_euler = (math.radians(90.0 - p["pitch"]), 0.0, 0.0)
    bpy.context.view_layer.update()
    return cam


def projected_bounds(objs):
    """(u0, u1, v0, v1) of the objects in normalised camera-view coordinates."""
    from bpy_extras.object_utils import world_to_camera_view
    sc = bpy.context.scene
    dg = bpy.context.evaluated_depsgraph_get()
    us, vs = [], []
    for ob in objs:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        for vt in me.vertices:
            co = world_to_camera_view(sc, sc.camera, ev.matrix_world @ vt.co)
            us.append(co.x)
            vs.append(co.y)
        ev.to_mesh_clear()
    return min(us), max(us), min(vs), max(vs)


def build_cast_arrow():
    """Renders cast_arrow_f0 (plain) and cast_arrow_f1..fN (the glint from the butt up to the tip) into
    CAST_ARROW_OUT: one even-sized crop for all (same silhouette), 1 px outline. Returns (paths, info); info gives
    the size and where the hat's middle (Angler.HatPos2D) is from the canvas centre, for CastArrow.cs."""
    nglint, half = CAST_ARROW_GLINT
    frames = [None] + [(k / (nglint - 1), half) for k in range(nglint)]
    res, shift = 128, None
    raws, hat_px = [], None
    raw = os.path.join(C.TMP, "raw_castarrow.png")   # its own scratch file (other groups render to raw.png)
    for g in frames:
        C.clear_objects()
        objs, _ = build_cast_arrow_mesh(g)
        bpy.context.view_layer.update()
        if shift is None:
            persp_camera(res)
            u0, u1, v0, v1 = projected_bounds(objs)
            shift = ((u0 + u1) / 2 - 0.5, (v0 + v1) / 2 - 0.5)
            if max(u1 - u0, v1 - v0) * res > res - 8:
                print("WARNING cast arrow canvas too small")
        persp_camera(res, shift)
        C.render_raw(raw)
        raws.append(C.load_pixels(raw))
        hat_px = C.world_to_pixel((0.0, 0.0, 0.0))
    # one crop for all frames: shape bbox + 1 px for the outline, even size
    mask = np.zeros(raws[0].shape[:2], bool)
    for a in raws:
        mask |= a[..., 3] > 0.5
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = ys.min() - 1, ys.max() + 2, xs.min() - 1, xs.max() + 2
    if (y1 - y0) % 2:
        y1 += 1
    if (x1 - x0) % 2:
        x1 += 1
    paths = []
    # the plain frame's 1 px dark outline on every frame: pixelize darkens each edge pixel's neighbour colour, so the
    # glint band would lighten the outline where it passes; the silhouette is the same, so the ring is too
    plain = C.pixelize(raws[0][y0:y1, x0:x1])
    ring = (plain[..., 3] > 0.5) & ~(raws[0][y0:y1, x0:x1, 3] > 0.5)
    for i, a in enumerate(raws):
        path = os.path.join(CAST_ARROW_OUT, f"cast_arrow_f{i}.png")
        px = C.pixelize(a[y0:y1, x0:x1])
        if not np.array_equal(px[..., 3] > 0.5, plain[..., 3] > 0.5):
            print(f"WARNING cast arrow frame {i}: silhouette differs from f0")
        px[ring] = plain[ring]
        C.save_pixels(px, path)
        paths.append(path)
    w, h = int(x1 - x0), int(y1 - y0)
    # the hat's middle relative to the canvas centre (x right, y up), px
    hx, hy = float(hat_px[0] - x0 - w / 2), float(hat_px[1] - y0 - h / 2)
    info = dict(size=(w, h), hat_from_centre=(round(hx, 1), round(hy, 1)))
    print(f"cast arrow: {len(paths)} frames {w}x{h} px, hat middle {info['hat_from_centre']} px from the canvas "
          f"centre (x right, y up) -> CastArrow.OffPx = ({-hx:.1f}, {-hy:.1f}); the sprite's bottom edge "
          f"{-hy - h / 2:.1f} px above the hat's middle")
    return paths, info


def cast_arrow_preview(paths, info):
    """Review composite: every frame at 4x over the wind-up QA capture (1920x1080 = 4x the 480x270 view), the hat's
    middle on CAST_ARROW_PREVIEW_HAT, plus an 8x sheet of the frames."""
    C.contact_sheet(paths, os.path.join(CAST_ARROW_OUT, "castarrow_zoom.png"), scale=8, cols=len(paths))
    shot = os.path.join(C.TMP, "qa_flick", "lake", "00_windup_guide.png")
    if not os.path.exists(shot):
        print("cast arrow preview skipped: no", shot)
        return
    bg = C.load_pixels(shot)                      # rows from the bottom
    H = bg.shape[0] // 4                          # 270
    w, h = info["size"]
    hx, hy = info["hat_from_centre"]
    # native top-left of the sprite so the hat's middle lands on CAST_ARROW_PREVIEW_HAT (centre snapped like the game)
    cx, cy = round(CAST_ARROW_PREVIEW_HAT[0] - hx), round(CAST_ARROW_PREVIEW_HAT[1] + hy)
    left, top = int(cx - w // 2), int(cy - h // 2)
    box = (200, 113, 280, 205)                    # native crop (x0, y0 from the top, x1, y1)
    panels = []
    gap = None
    for path in paths:
        img = bg.copy()
        spr = np.repeat(np.repeat(C.load_pixels(path), 4, 0), 4, 1)
        r0 = (H - top - h) * 4                    # bottom row of the sprite, counted from the bottom
        reg = img[r0:r0 + h * 4, left * 4:(left + w) * 4]
        al = spr[..., 3:4]
        reg[..., :3] = reg[..., :3] * (1 - al) + spr[..., :3] * al
        crop = img[(H - box[3]) * 4:(H - box[1]) * 4, box[0] * 4:box[2] * 4]
        if gap is None:
            gap = np.zeros((crop.shape[0], 8, 4), np.float32)
            gap[..., 3] = 1
        panels += [crop, gap]
    out = np.concatenate(panels[:-1], axis=1)
    out[..., 3] = 1
    C.save_pixels(out, os.path.join(CAST_ARROW_OUT, "castarrow_preview.png"))


# ============================================================================ SIDE ARROW
# The fight's side-pressure arrow (SideArrow.cs: floating over where the line enters the water, "push the rod this
# way"): the cast arrow's gold block arrow (a shaft and a broad head with flared barbs and a rounded tip, a slab with
# real thickness) turned on its side and seen face-on through an ortho camera at 1 unit = 1 px, pointing RIGHT (the
# game flips it for a push to the left). Its back face sits SIDE_ARROW_SLAB[1] below the front one (a sheared prism),
# so only the walls facing down show, under the front face: the shaft's underside ("bottom", the darkest) and the
# head's lower slope, the rounded tip and the lower barb's back edge ("side"); straight down, so it reads the same way
# flipped. The light strip runs inside the top edges (the shaft's top, the head's upper slope). One canvas for every
# frame (even size; a spare 1 px round the outline for the "on" frame's halo):
#   side_arrow_f0      plain (the prompt's rest, and the "wrong way" shake)
#   side_arrow_f1..fN  a light glint band running from the tail to the tip (the prompt loops f1..fN, f0)
#   side_arrow_on      pushed the right way: the lighter palette all over and a soft light halo outside the outline
SIDE_ARROWS = {  # (bottom, side, front, highlight): the cast arrow's gold and its glint; "on" = the bright frame
    "base": CAST_ARROWS["base"],
    "glint": CAST_ARROWS["glint"],
    "on": ("#f5a032", "#ffcf52", "#fff6a0", "#ffffff"),
}
SIDE_ARROW_SHAFT = (2.5, 13.0)             # shaft half-height, head start x (px from the tail); the axis is z = 0
SIDE_ARROW_HEAD = (6.5, 3.0, 24.0, 1.6)    # barb half-height, barb tips swept back, tip x (before rounding), tip radius
SIDE_ARROW_SLAB = (2.0, 2.0)               # thickness away from the camera, back face drop (px)
SIDE_ARROW_HILITE = 1.1                    # the front face's light strip inside its top edges (px)
SIDE_ARROW_GLINT = (7, 0.12)               # glint frames (tail to tip), band half-length (fraction of the length)
SIDE_ARROW_HALO = ("#ffec70", 0.85)        # the "on" frame's ring outside the outline: colour, alpha
SIDE_ARROW_OUT = os.path.join(C.TMP, "sidearrow")   # the review sheet; the frames go straight to Sprites/UI


def side_arrow_outline():
    """The front face in (x, z), counter-clockwise from the tail's bottom corner, the tip rounded."""
    h, xh = SIDE_ARROW_SHAFT
    W, back, La, r = SIDE_ARROW_HEAD
    A = Vector((La, 0.0))
    P = Vector((xh - back, W))
    e = (P - A).normalized()                                  # from the tip up and back along the upper slope
    beta = math.acos(max(-1.0, min(1.0, -e.x)))              # half the tip's angle
    cen = A + Vector((-r / math.sin(beta), 0.0))
    a0 = math.pi / 2 - beta                                   # the tangent points, round the rounding's centre
    n = 9
    arc = [(cen.x + r * math.cos(a), cen.y + r * math.sin(a)) for a in (-a0 + 2 * a0 * i / (n - 1) for i in range(n))]
    return [(0.0, -h), (xh, -h), (xh - back, -W)] + arc + [(xh - back, W), (xh, h), (0.0, h)]


def side_arrow_length():
    return max(x for x, _ in side_arrow_outline())


def side_arrow_highlight(nb, x, z):
    """Shader mask: 1 on the front face within SIDE_ARROW_HILITE inside the shaft's top edge and the head's upper slope."""
    h, xh = SIDE_ARROW_SHAFT
    W, back, La, _ = SIDE_ARROW_HEAD
    sw = SIDE_ARROW_HILITE
    shaft = nb.math("MULTIPLY", nb.math("GREATER_THAN", z, h - sw), nb.math("LESS_THAN", x, xh))
    P = Vector((xh - back, W))
    e = (Vector((La, 0.0)) - P).normalized()
    ni = Vector((e.y, -e.x))                                  # into the head (down and back)
    d = nb.math("MULTIPLY_ADD", x, ni.x, nb.math("MULTIPLY_ADD", z, ni.y, -ni.dot(P)))
    upper = nb.math("MULTIPLY", nb.math("GREATER_THAN", x, xh - back), nb.math("GREATER_THAN", z, 0.0))
    head = nb.math("MULTIPLY", nb.math("LESS_THAN", d, sw), upper)
    return nb.math("MAXIMUM", shaft, head)


def side_arrow_material(name, cls, pal, glint=None):
    """Flat colour by face class ("bottom" | "side" | "front") from SIDE_ARROWS[pal], or the glint colours where the
    band passes (glint = (centre, half-length) as fractions of the length from the tail); the front face gets its
    light strip along the top edges."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    co = nb.sep(nb.node("ShaderNodeTexCoord").outputs["Object"])     # the object sits at the origin: world x, z
    x, z = co[0], co[2]
    k = ("bottom", "side", "front").index(cls)
    base, hi = SIDE_ARROWS[pal], SIDE_ARROWS["glint"]
    col, gcol = C.lin(base[k]), C.lin(hi[k])
    if cls == "front":
        h = side_arrow_highlight(nb, x, z)
        col = nb.mix(h, col, C.lin(base[3]))
        gcol = nb.mix(h, gcol, C.lin(hi[3]))
    if glint is not None:
        c, half = glint
        d = nb.math("ABSOLUTE", nb.math("MULTIPLY_ADD", x, 1.0 / side_arrow_length(), -c))
        col = nb.mix(nb.math("LESS_THAN", d, half), col, gcol)
    nb.output_emission(col, 1.0)
    return m


def build_side_arrow_mesh(pal, glint=None):
    """The sideways block arrow (one object, materials bottom / side / front): the front face at y = 0 facing the
    camera, the back face SIDE_ARROW_SLAB behind it and dropped."""
    pts = side_arrow_outline()
    t, drop = SIDE_ARROW_SLAB
    bm = bmesh.new()
    front = [bm.verts.new((x, 0.0, z)) for x, z in pts]
    back = [bm.verts.new((x, t, z - drop)) for x, z in pts]
    bm.faces.new(front)
    bm.faces.new(back[::-1])
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[j], front[i], back[i], back[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:   # the front; walls facing (within 20 deg) straight down: "bottom"; the rest: "side"
        nx, ny, nz = f.normal
        l = math.hypot(nx, nz)
        f.material_index = 2 if ny < -0.9 else (0 if l > 1e-6 and nz < -0.94 * l else 1)
    ob = C.mesh_object("SideArrow", bm)
    for cls in ("bottom", "side", "front"):
        ob.data.materials.append(side_arrow_material("sidearr_" + cls, cls, pal, glint))
    return [ob]


def build_side_arrow():
    """Renders side_arrow_f0 (plain), side_arrow_f1..fN (the glint from the tail to the tip) and side_arrow_on (bright,
    with a halo) straight into Sprites/UI: one even-sized crop for all, 1 px dark outline (the plain frame's on every
    plain / glint frame) and 1 px spare round it. Returns (paths, info): the size, and the lowest opaque row's distance
    below the canvas centre (px) for SideArrow.cs."""
    nglint, half = SIDE_ARROW_GLINT
    specs = ([("side_arrow_f0", "base", None)]
             + [(f"side_arrow_f{k + 1}", "base", (k / (nglint - 1), half)) for k in range(nglint)]
             + [("side_arrow_on", "on", None)])
    L = side_arrow_length()
    W = SIDE_ARROW_HEAD[0]
    drop = SIDE_ARROW_SLAB[1]
    res_x, res_y = 2 * (int(L) // 2 + 8), 2 * (int(W + drop) + 6)
    # pixel columns on whole x, rows on half z: the tail, the head start and the barbs on pixel edges, the axis mid-row
    cx, cz = float(round(L / 2)), -0.5 - round(drop / 2)
    raw = os.path.join(C.TMP, "raw_sidearrow.png")      # its own scratch file (other groups render to raw.png)
    raws = []
    for name, pal, g in specs:
        C.clear_objects()
        build_side_arrow_mesh(pal, g)
        bpy.context.view_layer.update()
        C.ortho_camera(cx, cz, res_x, res_y, 1.0)
        C.render_raw(raw)
        raws.append(C.load_pixels(raw))
    mask = np.zeros(raws[0].shape[:2], bool)
    for a in raws:
        mask |= a[..., 3] > 0.5
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = ys.min() - 2, ys.max() + 3, xs.min() - 2, xs.max() + 3    # + outline + halo
    if (y1 - y0) % 2:
        y1 += 1
    if (x1 - x0) % 2:
        x1 += 1
    plain = C.pixelize(raws[0][y0:y1, x0:x1])
    ring = (plain[..., 3] > 0.5) & ~(raws[0][y0:y1, x0:x1, 3] > 0.5)
    halo_col = np.array(C.hex_rgb(SIDE_ARROW_HALO[0]), np.float32)
    paths = []
    for (name, pal, g), a in zip(specs, raws):
        px = C.pixelize(a[y0:y1, x0:x1])
        if pal == "on":
            solid = px[..., 3] > 0.5
            grow = np.zeros_like(solid)
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                grow |= C.shift(solid, dy, dx)
            halo = grow & ~solid
            px[halo, :3] = halo_col
            px[halo, 3] = SIDE_ARROW_HALO[1]
        else:
            if not np.array_equal(px[..., 3] > 0.5, plain[..., 3] > 0.5):
                print(f"WARNING side arrow {name}: silhouette differs from f0")
            px[ring] = plain[ring]
        path = os.path.join(UI, name + ".png")
        C.save_pixels(px, path)
        paths.append(path)
    w, h = int(x1 - x0), int(y1 - y0)
    rows = np.nonzero((plain[..., 3] > 0.5).any(axis=1))[0]
    cols = np.nonzero((plain[..., 3] > 0.5).any(axis=0))[0]
    info = dict(size=(w, h), bottom_below_centre=h / 2 - rows.min(), arrow_px=(int(cols.max() - cols.min() + 1), int(rows.max() - rows.min() + 1)))
    print(f"side arrow: {len(paths)} frames {w}x{h} px (the arrow {info['arrow_px'][0]}x{info['arrow_px'][1]} px with its "
          f"outline), its lowest row {info['bottom_below_centre']:.1f} px below the canvas centre")
    return paths, info


def side_arrow_preview(paths):
    """Review sheets: every frame at 8x (and flipped), over lake-ish water."""
    C.contact_sheet(paths, os.path.join(SIDE_ARROW_OUT, "sidearrow_zoom.png"), scale=8, cols=len(paths))
    flipped = []
    for p in paths:
        a = C.load_pixels(p)[:, ::-1]
        q = os.path.join(SIDE_ARROW_OUT, "flip_" + os.path.basename(p))
        C.save_pixels(a, q)
        flipped.append(q)
    C.contact_sheet(flipped, os.path.join(SIDE_ARROW_OUT, "sidearrow_zoom_flip.png"), scale=8, cols=len(paths))


# ============================================================================ LURE REVIEW SHEET
def lure_sheet(out):
    """Review sheet: (1) the 12 existing bait icons (style reference, 4x); (2) the 10 lures by action: chip (8x),
    then per lure its icon (4x) over its in-water sprite (8x), new lures marked with a gold bar; (3) the in-water
    sprites at 4x on lake water and on dark cave water."""
    def ld(p):
        return C.load_pixels(p)[::-1]        # rows top-down
    items, rects = [], []                    # (img, x, y, scale) / (x, y, w, h, rgb), top-down px
    pad = 16
    x, y = pad, pad
    for bid in BAITS[:12]:
        items.append((ld(os.path.join(ITEMS, bid + ".png")), x, y, 4))
        x += 136
    y = pad + 128 + 36
    x = pad
    for chip, ids in LURE_ACTIONS:
        items.append((ld(os.path.join(UI, chip + ".png")), x, y + 60, 8))
        x += 96 + 12
        for bid in ids:
            if bid in LURES_NEW:
                rects.append((x, y - 12, 128, 6, C.hex_rgb("#ffd24a")))
            items.append((ld(os.path.join(ITEMS, bid + ".png")), x, y, 4))
            items.append((ld(os.path.join(WORLD, bid + "_w.png")), x + 20, y + 136, 8))
            x += 136
        x += 28
    width = x + pad
    y += 136 + 88 + 36
    order = [b for _, ids in LURE_ACTIONS for b in ids]
    for water in ("#456a8a", "#04101a"):
        x = pad
        for bid in order:
            rects.append((x, y, 60, 60, C.hex_rgb(water)))
            items.append((ld(os.path.join(WORLD, bid + "_w.png")), x + 8, y + 8, 4))
            x += 64
        y += 64
    height = y + pad
    sheet = np.zeros((height, width, 4), np.float32)
    sheet[..., :3] = (0.25, 0.35, 0.45)
    sheet[..., 3] = 1
    for (rx, ry, rw, rh, rgb) in rects:
        sheet[ry:ry + rh, rx:rx + rw, :3] = rgb
    for img, ix, iy, s in items:
        big = np.repeat(np.repeat(img, s, 0), s, 1)
        reg = sheet[iy:iy + big.shape[0], ix:ix + big.shape[1]]
        a = big[..., 3:4]
        reg[..., :3] = reg[..., :3] * (1 - a) + big[..., :3] * a
    C.save_pixels(sheet[::-1], out)
    print("lure sheet ->", out)


# ============================================================================ OBSTACLES: UI + FX
# The small art of Docs/obstacles_spec.md (4.6 contacts, 5.1 pads, 6.4 snags, 7.5 / 10.2 the abrasion meter,
# 9.2 aim outlines, 10.4 rub sparks). Group "obstacles" (+ "dry": into _tmp/obstacles/ui|world, not Assets):
#   blender -b --python Tools/Blender/fk_items.py -- obstacles [dry]      review sheet: _tmp/obstacles/ui_sheet.png
# UI -> Sprites/UI (the HUD draws UI art at 2x, UIKit.Px; icons carry the 1 px hue-shifted outline)
#   icon_snag        16 px  밑걸림: a steel J hook in front of a grey rock, its point gone into the rock, the taut line
#                           running up to the right (the snag-mode name slot, or a mark over the line's entry)
#   icon_snag_weed   16 px  수초 / 갈대: the same hook tangled in weed blades
#   icon_snag_pad    16 px  연잎: the same hook, its point in a lily pad
#   icon_rub         16 px  줄 쓸림: the line pulled over a rock edge, frayed, sparks at the edge
#   abr_label        7x5    the abrasion meter's glyph (a line fraying at a spark), no outline (the dark fight panel)
#   abr_bar_bg       78x5   the meter frame, Image.Type.Simple at 156x10 canvas: outline, a 76x3 well that turns a dim
#                           red from 80 % (like the tension bar's danger zone)
#   abr_bar_fill     76x3   the meter fill: a grey twisted line; tint #e8d8a0 / #ff8a3a / UIKit.Bad; Image.Type.Filled
#                           (Horizontal, origin Left) inset 1 px (2 canvas) in the frame
# World -> Sprites/World (16 PPU, pivot = centre, even sizes, no outline, alpha in quarter steps; 1 frame ~0.05 s)
#   fx_hit_f0..f3      12x12  a cast striking a solid: a white-hot four-point flash that grows, breaks up and dies;
#                             untinted (crystal: tint #aef0ff); the pivot = the contact point (the core pixel sits
#                             just up-right of it: even size)
#   fx_hit_s_f0..f2     6x6   the same, far (Persp.ScaleAt < 0.35, like the fan dots)
#   fx_chip_f0..f3     12x12  chips knocked off: grey specks flung up and out, then falling; tint with the material's
#                             colour (rock #a0a098, concrete #c8c8c0, wood / root #8a6a48, hull #e8e8e8); flipX free
#   fx_chip_s_f0..f2    8x8   the same, far
#   fx_rub_f0..f3       8x8   line-rub sparks (#ffd080), a loop: one frame per 0.12 s spark tick at the contact point
#   fx_padland_f0..f3  16x10  the frog landing on a lily pad: a squashed ring of droplets and pad-green flecks
#   obst_dash           4x2   the aim outlines' dash key (9.2): top row snag #d8f0ff, bottom row weed #b8e8a0, 2 on /
#                             2 off (a colour / pattern reference: the overlay writes the same into its Texture2D)
OBX_OUT = os.path.join(C.TMP, "obstacles")
OBX_WORK = os.path.join(OBX_OUT, "work")        # own scratch (other groups render to _tmp/raw.png)
_OBX_Z = [0]
OBX_LINE = "#f4f0e0"                            # the fishing line in the icons (UIKit.Cream-ish)
OBX_STEEL = "#dfe6ee"
OBX_SPARK = ("#ffffff", "#fff4c4", "#ffd080", "#ffb050", "#ff8a3a")   # white, hot, gold (spec), amber, ember


def obx_hex(col):
    if isinstance(col, str):
        return col.lower()
    return "#%02x%02x%02x" % tuple(int(round(max(0.0, min(1.0, c)) * 255)) for c in col[:3])


def obx_flat(col):
    """Flat emission material, cached by colour (re-made after clear_objects)."""
    name = "OBX" + obx_hex(col)
    m = bpy.data.materials.get(name)
    if m is None:
        m = C.glow_material(name, obx_hex(col), 1.0)
        m.name = name
    return m


def obx_cells(cells, col, a=1.0):
    """Unit squares at canvas pixels (i, j) (j from the bottom): flat colour, alpha a (quarter steps); every call
    paints over the earlier ones."""
    cells = sorted(set((int(round(i)), int(round(j))) for i, j in cells))
    if not cells:
        return None
    bm = bmesh.new()
    for i, j in cells:
        v = [bm.verts.new((i, 0, j)), bm.verts.new((i + 1, 0, j)), bm.verts.new((i + 1, 0, j + 1)), bm.verts.new((i, 0, j + 1))]
        bm.faces.new(v)
    ob = C.mesh_object("px", bm, obx_flat(col))
    _OBX_Z[0] += 1
    ob.location.y = -0.002 * _OBX_Z[0]
    ob["a"] = float(a)
    return ob


def obx_raw(tag):
    p = os.path.join(OBX_WORK, tag + ".png")
    C.render_raw(p)
    return C.load_pixels(p)


def obx_render_cells(w, h):
    """Colour pass + alpha pass (every material swapped for a flat grey = its object's "a"): RGBA rows bottom-up,
    canvas [0, w] x [0, h] in pixel units, alpha in quarter steps."""
    bpy.context.view_layer.update()
    cw, ch = max(w, 4), max(h, 4)                 # Blender renders at least 4x4: pad, then crop the bottom-left
    C.ortho_camera(cw / 2.0, ch / 2.0, cw, ch, 1.0)
    col = obx_raw("col")
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    keep = []
    for o in objs:
        keep.append([s.material for s in o.material_slots])
        g = float(o.get("a", 1.0))
        for s in o.material_slots:
            s.material = obx_flat((g, g, g))
    am = obx_raw("alpha")
    for o, mats in zip(objs, keep):
        for s, m in zip(o.material_slots, mats):
            s.material = m
    col, am = col[:h, :w], am[:h, :w]
    out = np.zeros_like(col)
    cov = col[..., 3] > 0.5
    out[..., :3] = col[..., :3]
    out[..., 3] = np.where(cov, np.round(np.clip(am[..., 0], 0, 1) * 4) / 4, 0.0)
    out[out[..., 3] <= 0] = 0.0
    return out


# ---------------------------------------------------------------------------- UI icons (16 px, pixel units)
def obx_icon_render(size):
    """A toon icon authored in pixel units round the origin. Objects carry a layer ("lay", back to front): each layer
    is rendered alone with the UI's 1 px outline (fk_common.pixelize) and laid over the ones behind it, so the hook
    keeps a dark line where it crosses the rock / weed / pad."""
    bpy.context.view_layer.update()
    C.ortho_camera(0.0, 0.0, size, size, 1.0)
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    out = None
    for lay in sorted({int(o.get("lay", 0)) for o in objs}):
        for o in objs:
            o.hide_render = int(o.get("lay", 0)) != lay
        px = C.pixelize(obx_raw("icon"), outline=True)
        if out is None:
            out = px
        else:
            a = px[..., 3:4]
            out[..., :3] = out[..., :3] * (1 - a) + px[..., :3] * a
            out[..., 3:4] = np.maximum(out[..., 3:4], a)
    for o in objs:
        o.hide_render = False
    return out


def obx_lay(objs, lay):
    for o in objs:
        o["lay"] = lay
    return objs


# the shared snag hook (pixel units, 16 px icon): the eye, the shank down, the bend round a centre, the point up
OBX_HOOK = dict(eye=(2.8, 4.4), shank=-0.8, bend=(0.3, 2.5), tip=0.9, line_to=(5.4, 7.7))


def obx_hook(tip=None):
    """-> (the hook's shank + bend + the taut line to the upper right, the point: its last straight piece), steel
    tubes 1.3 px thick; the point is its own object so a layer can hide it (stuck in the rock / pad)."""
    h = OBX_HOOK
    steel = M("ObxSteel", OBX_STEEL, shine=1.0)
    ex, ez = h["eye"]
    bx, r = h["bend"]
    bz = h["shank"]
    pts = [(ex, 0, ez), (ex, 0, bz)]
    for k in range(1, 13):
        a = -math.pi * k / 12
        pts.append((bx + r * math.cos(a), 0, bz + r * math.sin(a)))
    body = C.tube_along("Hook", pts, 0.65, steel, 8)
    line = C.tube_along("Line", [(ex, 0, ez + 0.3), (h["line_to"][0], 0, h["line_to"][1])], 0.5,
                        M("ObxLine", OBX_LINE, shine=0.4), 6)
    px_ = bx - r
    point = C.tube_along("Point", [(px_, 0, bz - 0.2), (px_, 0, bz + 0.8), (px_ + 0.15, 0, tip if tip is not None else h["tip"])],
                         [0.65, 0.6, 0.35], steel, 8)
    return [body, line], [point]


def obx_rock(pts, facet, lit="#8e8a82", dark="#5e5a56"):
    """A faceted boulder like the stages' (the lit body + a darker facet on the right)."""
    objs = [extruded("Rock", pts, M("ObxRock", lit, shine=0.2), 3.0, 0.8)]
    objs.append(extruded("Facet", facet, M("ObxRockD", dark, shine=0.1), 1.0, 0.3))
    objs[-1].location = (0, -1.8, 0)
    return objs


def build_obx_icon(name):
    objs = []
    if name == "icon_snag":
        # the whole J in front of the rock, its point gone into the rock's hump: stuck; the line taut to the upper right
        body, point = obx_hook()
        obx_lay(point, 0)
        rock = obx_rock([(-7.3, -7.3), (6.8, -7.3), (7.2, -5.4), (6.0, -4.0), (3.6, -3.0), (1.6, -1.8), (-1.0, 0.3),
                         (-3.0, 1.7), (-5.2, 0.9), (-6.9, -1.8), (-7.4, -4.8)],
                        [(1.8, -7.3), (6.8, -7.3), (7.2, -5.4), (6.0, -4.0), (3.6, -3.0), (2.4, -4.8)])
        obx_lay(rock, 1)
        obx_lay(body, 2)
        objs = point + rock + body
    elif name == "icon_snag_weed":
        # the hook tangled in weed: blades behind it, one blade wound across its point
        body, point = obx_hook()
        back, front = M("ObxWeedD", "#3e7a36", shine=0.2), M("ObxWeed", "#6ab04a", shine=0.5)
        rad = [0.9, 0.85, 0.6, 0.25]
        blades = [
            ([(-6.2, 0, -7.6), (-6.6, 0, -4.4), (-5.0, 0, -1.8), (-6.0, 0, 1.2)], back, 0),
            ([(5.6, 0, -7.6), (6.5, 0, -4.8), (5.2, 0, -2.6), (6.4, 0, -0.4)], back, 0),
            ([(1.4, 0, -7.6), (2.2, 0, -5.6), (0.6, 0, -4.8), (1.6, 0, -3.8)], back, 0),
            ([(-3.4, 0, -7.6), (-2.2, 0, -4.8), (-1.6, 0, -2.4), (-3.0, 0, -0.2), (-2.0, 0, 1.6)], front, 2),
        ]
        for k, (pts, mat, lay) in enumerate(blades):
            objs += obx_lay([C.tube_along(f"Blade{k}", pts, rad if len(pts) == 4 else [0.9, 0.85, 0.75, 0.55, 0.25], mat, 8)], lay)
        objs += obx_lay(body + point, 1)
    elif name == "icon_snag_pad":
        # the J in front of a lily pad (seen low: a flat ellipse with its slit and a dark rim), the point in the pad
        body, point = obx_hook(tip=0.0)
        obx_lay(point, 0)
        pad = [(0.0 + 7.3 * math.cos(t), -3.4 + 3.4 * math.sin(t)) for t in (math.radians(a) for a in range(-60, 271, 15))]
        pad = [(1.0, -3.2)] + pad
        rim = extruded("PadRim", [(x, z - 0.9) for x, z in pad], M("ObxPadD", "#3a6634", shine=0.1), 1.0, 0.2)
        rim.location = (0, 1.0, 0)
        top = extruded("Pad", pad, M("ObxPad", "#7cb456", shine=0.5), 1.0, 0.5)
        objs = point + obx_lay([rim, top], 1) + obx_lay(body, 2)
    elif name == "icon_rub":
        # the line comes down from the upper left and is pulled over the rock's sharp edge (the fish is behind it):
        # frayed whiskers at the edge, a spark
        rock = obx_rock([(-3.4, -7.3), (7.3, -7.3), (7.3, -2.4), (4.6, 0.0), (1.6, 0.5), (-0.8, -2.8)],
                        [(2.6, -7.3), (7.3, -7.3), (7.3, -2.4), (4.6, 0.0), (3.4, -3.2)])
        objs += obx_lay(rock, 0)
        line = [C.tube_along("Line", [(-7.0, 0, 6.8), (1.3, 0, 1.2)], 0.5, M("ObxLine", OBX_LINE, shine=0.4), 6)]
        # pixel cells (icon pixel (i, j) = the square [i, i + 1] x [j, j + 1]): two frayed strands off the line, the
        # spark (white core, gold arms, amber tips) on the edge, a stray ember
        line.append(obx_cells([(-1, 3), (-1, 4), (-2, 5)], "#ffc890"))
        line.append(obx_cells([(-1, 1)], "#e0a878"))
        objs += obx_lay(line, 1)
        sp = [obx_cells([(1, 0)], OBX_SPARK[0]), obx_cells([(0, 0), (2, 0), (1, 1), (1, -1)], "#ffe27a"),
              obx_cells([(-1, 0), (3, 0), (1, 2), (1, -2)], OBX_SPARK[3]), obx_cells([(4, 3)], OBX_SPARK[2]),
              obx_cells([(5, 4)], OBX_SPARK[4])]
        objs += obx_lay(sp, 2)
    return objs


OBX_ICONS = {  # name: (size, meaning)
    "icon_snag": (16, "밑걸림: a J hook, its point in a rock, the taut line (snag-mode name slot / a mark over the entry)"),
    "icon_snag_weed": (16, "수초 / 갈대 snag: the hook tangled in weed blades"),
    "icon_snag_pad": (16, "연잎 snag: the hook, its point in a lily pad"),
    "icon_rub": (16, "줄 쓸림 warning: the line over a rock edge, frayed, sparks (A >= 0.6 / over the rub point)"),
}


# ---------------------------------------------------------------------------- the abrasion meter (pixel art)
def build_obx_meter():
    """abr_bar_bg 78x5, abr_bar_fill 76x3, abr_label 7x5 (pixel squares, bottom-left origin, no automatic outline)."""
    C.clear_objects()
    W, H = 78, 5
    ring = [(i, j) for i in range(W) for j in range(H) if (i in (0, W - 1) or j in (0, H - 1))
            and not (i in (0, W - 1) and j in (0, H - 1))]
    obx_cells(ring, "#0a1018")
    red0 = 1 + int(round(0.8 * (W - 2)))           # the well from 80 % of the fill's length
    obx_cells([(i, 3) for i in range(1, red0)], "#0e1620")
    obx_cells([(i, j) for i in range(1, red0) for j in (1, 2)], "#1c2a3a")
    obx_cells([(i, 3) for i in range(red0, W - 1)], "#2a1018")
    obx_cells([(i, j) for i in range(red0, W - 1) for j in (1, 2)], "#40202a")
    bg = obx_render_cells(W, H)
    # the fill: a twisted line in greys ("/" strands every 4 px), the HUD tints it
    C.clear_objects()
    W2 = W - 2
    rows = {2: ("#ffffff", "#c4c4c4", 2), 1: ("#e2e2e2", "#a2a2a2", 1), 0: ("#aaaaaa", "#747474", 0)}
    for j, (base, dark, ph) in rows.items():
        obx_cells([(i, j) for i in range(W2) if i % 4 != ph], base)
        obx_cells([(i, j) for i in range(W2) if i % 4 == ph], dark)
    fill = obx_render_cells(W2, 3)
    # the label glyph: the line (cream, a shade row under it) frays into two strands at a spark
    C.clear_objects()
    obx_cells([(i, 2) for i in range(0, 4)], OBX_LINE)
    obx_cells([(i, 1) for i in range(0, 3)], "#9c9484", 0.75)
    obx_cells([(4, 3), (4, 1)], "#e8c890")
    obx_cells([(5, 4), (5, 0)], "#c8a070", 0.75)
    obx_cells([(4, 2)], OBX_SPARK[1])
    obx_cells([(5, 2)], OBX_SPARK[2])
    obx_cells([(6, 3), (6, 1)], OBX_SPARK[4], 0.75)
    label = obx_render_cells(7, 5)
    return bg, fill, label


# ---------------------------------------------------------------------------- world FX (pixel art)
def obx_star(c, arms, diag=(), jitter=()):
    """Cells of a four-point star round the centre pixel c: arms = [(d, colour, a)], diag = [(d, colour, a)];
    jitter = extra [((dx, dy), colour, a)]."""
    out = []
    for d, col, a in arms:
        if d == 0:
            out.append(([c], col, a))
        else:
            out.append(([(c[0] + d, c[1]), (c[0] - d, c[1]), (c[0], c[1] + d), (c[0], c[1] - d)], col, a))
    for d, col, a in diag:
        out.append(([(c[0] + d, c[1] + d), (c[0] - d, c[1] + d), (c[0] + d, c[1] - d), (c[0] - d, c[1] - d)], col, a))
    for (dx, dy), col, a in jitter:
        out.append(([(c[0] + dx, c[1] + dy)], col, a))
    return out


def obx_draw(parts):
    for cells, col, a in parts:
        obx_cells(cells, col, a)


def build_obx_hit(small=False):
    """The contact flash: frames of a four-point star (white core, hot arms, gold / amber tips), then embers."""
    W, Hh, K = OBX_SPARK[0], OBX_SPARK[1], OBX_SPARK
    if small:
        c, size = (3, 3), 6
        frames = [
            obx_star(c, [(0, W, 1), (1, Hh, 1)]),
            obx_star(c, [(0, W, 1), (1, W, 1), (2, K[2], 1)], [(1, K[2], 0.5)]),
            obx_star(c, [(0, K[2], 0.5), (2, K[3], 0.75)], [(1, K[4], 0.5)]),
        ]
    else:
        c, size = (6, 6), 12
        frames = [
            obx_star(c, [(0, W, 1), (1, Hh, 1), (2, K[2], 1)], [(1, K[2], 0.5)]),
            obx_star(c, [(0, W, 1), (1, W, 1), (2, Hh, 1), (3, Hh, 1), (4, K[2], 1), (5, K[3], 0.75)],
                     [(1, Hh, 1), (2, K[2], 0.75)]),
            obx_star(c, [(0, K[2], 0.5), (3, K[2], 0.75), (4, K[2], 1), (5, K[3], 1)], [(3, K[3], 0.75)],
                     [((-2, 4), K[3], 0.5), ((4, -2), K[3], 0.5)]),
            obx_star(c, [(5, K[4], 0.5)], [(4, K[4], 0.5)], [((-6, 1), K[4], 0.5), ((2, 5), K[4], 0.5)]),
        ]
    out = []
    for parts in frames:
        C.clear_objects()
        obx_draw(parts)
        out.append(obx_render_cells(size, size))
    return out


# chips: (vx, vz, cells relative to the chip's position with grey tone index 0 lit / 1 mid / 2 shade)
OBX_CHIP_GREYS = ("#ffffff", "#bcbcbc", "#7c7c7c")
OBX_CHIPS = [
    (-1.5, 2.1, [((0, 0), 0), ((1, 0), 2)]),
    (-0.6, 2.6, [((0, 0), 0)]),
    (0.9, 2.8, [((0, 0), 0), ((1, 0), 1), ((0, -1), 1), ((1, -1), 2)]),
    (1.8, 1.7, [((0, 0), 1)]),
    (-2.3, 1.1, [((0, 0), 2)]),
]


def build_obx_chip(small=False):
    """Chips knocked off the prop: ballistic specks from the contact (the centre), frames at t = 0.6 .. 3.6 ticks;
    the two-pixel chips keep their shape for three frames, then one pixel; greys for the material tint."""
    c, size, nf, s = ((3, 3), 8, 3, 0.62) if small else ((5, 5), 12, 4, 1.0)
    g = 0.5 * s
    out = []
    for f in range(nf):
        t = 0.6 + f
        C.clear_objects()
        for n, (vx, vz, cells) in enumerate(OBX_CHIPS):
            if small and n == 4:
                continue
            x = c[0] + 0.5 + vx * s * t
            z = c[1] + 0.5 + vz * s * t - g * t * t
            if not (0 <= x < size and 0 <= z < size):
                continue
            use = cells if (f < 3 and not small) else cells[:1]
            a = 1.0 if f < nf - 1 else 0.75
            for (dx, dz), tone in use:
                obx_cells([(math.floor(x) + dx, math.floor(z) + dz)], OBX_CHIP_GREYS[tone], a)
        if f == 0:
            obx_cells([(c[0], c[1])], OBX_CHIP_GREYS[1], 0.75)       # the dust at the contact
        out.append(obx_render_cells(size, size))
    return out


def build_obx_rub():
    """Line-rub sparks, a 4-frame flicker round the contact pixel (4, 4)."""
    W, Hh, G, A, E = OBX_SPARK
    frames = [
        [([(4, 4)], W, 1), ([(5, 5)], Hh, 1), ([(6, 6)], G, 0.75), ([(3, 3)], E, 0.5)],
        [([(4, 4)], Hh, 1), ([(3, 5)], Hh, 1), ([(2, 6)], G, 1), ([(1, 6)], A, 0.5), ([(6, 4)], G, 0.75)],
        [([(4, 4)], W, 1), ([(5, 4)], Hh, 1), ([(6, 5)], G, 1), ([(7, 5)], A, 0.75), ([(3, 5)], G, 0.5)],
        [([(4, 4)], G, 0.75), ([(5, 6)], A, 0.75), ([(2, 5)], A, 0.5), ([(6, 3)], E, 0.5), ([(4, 7)], E, 0.5)],
    ]
    out = []
    for parts in frames:
        C.clear_objects()
        obx_draw(parts)
        out.append(obx_render_cells(8, 8))
    return out


def obx_ascii(rows, pal):
    """Paint an ASCII pixel map (rows top -> bottom) with pal = {char: (colour, alpha)}; "." = empty."""
    H = len(rows)
    for ch, (col, a) in pal.items():
        cells = [(c, H - 1 - r) for r, row in enumerate(rows) for c, x in enumerate(row) if x == ch]
        obx_cells(cells, col, a)


# the frog dropping onto a lily pad, 16x10 (the pivot = the landing point on the pad, between columns 7 / 8; the frog
# is drawn over the middle): a crown of drops thrown up and out, then falling, and a squashed ring spreading over the
# pad with a shade under its front lip. The maps are the LEFT halves (columns 0-7, rows top -> bottom), mirrored;
# flecks (pad-green bits) are added per frame as (row, column, char). W drop, w drop 75 %, p pale drop, q pale 50 %,
# L / D light / dark pad fleck (l: light 50 %), s shade (black 25 %)
OBX_PADLAND = [
    (["........", "........", "......W.", ".....W..", "........", "....w...", "...pW...", "....pWWW", ".....sss",
      "........"], [(4, 9, "L")]),
    (["....W...", "......W.", "..W.....", "....w...", "........", "..pW....", ".pW.....", "..pWW.WW", "...ss...",
      "........"], [(2, 10, "L"), (1, 5, "D")]),
    (["........", "..w.....", "....W...", "........", "...W....", ".p......", "p.......", ".pW.....", "..pW.W..",
      ".....s.."], [(3, 1, "D"), (4, 15, "L")]),
    (["........", "........", "........", "........", ".q......", "..w.....", "q.......", "........", "..q.q..q",
      "........"], [(5, 13, "l")]),
]
OBX_PADLAND_PAL = {"W": ("#f2faf6", 1.0), "w": ("#f2faf6", 0.75), "p": ("#c4e2d8", 1.0), "q": ("#c4e2d8", 0.5),
                   "L": ("#a8d064", 1.0), "l": ("#a8d064", 0.5), "D": ("#5c8c44", 1.0), "s": ("#000000", 0.25)}


def build_obx_padland():
    out = []
    for half, flecks in OBX_PADLAND:
        rows = [list(h + h[::-1]) for h in half]
        for r, c, ch in flecks:
            rows[r][c] = ch
        C.clear_objects()
        obx_ascii(["".join(r) for r in rows], OBX_PADLAND_PAL)
        out.append(obx_render_cells(16, 10))
    return out


def build_obx_dash():
    C.clear_objects()
    obx_cells([(0, 1), (1, 1)], "#d8f0ff")
    obx_cells([(0, 0), (1, 0)], "#b8e8a0")
    return obx_render_cells(4, 2)


# ---------------------------------------------------------------------------- build + review sheet
def obstacles_art(dry=False):
    """Every sprite of the group -> Sprites/UI + Sprites/World (dry: _tmp/obstacles/ui|world); returns
    {name: (path, meaning, w, h)} in sheet order and writes the review sheet."""
    ui = os.path.join(OBX_OUT, "ui") if dry else UI
    world = os.path.join(OBX_OUT, "world") if dry else WORLD
    os.makedirs(OBX_WORK, exist_ok=True)
    made = {}

    def put(arr, folder, name, meaning):
        p = os.path.join(folder, name + ".png")
        C.save_pixels(arr, p)
        made[name] = (p, meaning, arr.shape[1], arr.shape[0])

    for name, (size, meaning) in OBX_ICONS.items():
        C.clear_objects()
        build_obx_icon(name)
        put(obx_icon_render(size), ui, name, meaning)
    bg, fill, label = build_obx_meter()
    put(label, ui, "abr_label", "쓸림 meter glyph: a line fraying at a spark (no outline, for the dark fight panel)")
    put(bg, ui, "abr_bar_bg", "abrasion meter frame (Simple, 156x10 canvas): outline + 76x3 well, dim red from 80 %")
    put(fill, ui, "abr_bar_fill", "abrasion meter fill: grey twisted line, tinted; Filled Horizontal Left, inset 1 px")
    for k, a in enumerate(build_obx_hit()):
        put(a, world, f"fx_hit_f{k}", ["impact: small hot star", "the flash at its largest", "breaking up into sparks",
                                        "last embers"][k])
    for k, a in enumerate(build_obx_hit(small=True)):
        put(a, world, f"fx_hit_s_f{k}", ["impact", "flash", "embers"][k] + " (far)")
    for k, a in enumerate(build_obx_chip()):
        put(a, world, f"fx_chip_f{k}", ["chips breaking off + dust", "flung up and out", "top of the arc",
                                         "falling (fading)"][k])
    for k, a in enumerate(build_obx_chip(small=True)):
        put(a, world, f"fx_chip_s_f{k}", ["breaking off", "flung out", "falling"][k] + " (far)")
    for k, a in enumerate(build_obx_rub()):
        put(a, world, f"fx_rub_f{k}", "rub spark flicker %d / 4 (loop)" % (k + 1))
    for k, a in enumerate(build_obx_padland()):
        put(a, world, f"fx_padland_f{k}", ["impact: tight ring + drops up", "ring spreads, drops at the top",
                                            "wide sparse ring, drops falling", "remnants on the pad"][k])
    put(build_obx_dash(), world, "obst_dash", "aim outline dash key: top snag #d8f0ff, bottom weed #b8e8a0, 2 on / 2 off")
    for name, (p, meaning, w, h) in made.items():
        print(f"OBST ART {name:16s} {w:3d}x{h:<3d} {meaning}")
    obx_sheet(made, os.path.join(OBX_OUT, "ui_sheet.png"))
    return made


class ObxCam:
    """Game point -> stage canvas pixel (x right, y down): Persp.ToPixel maths from the stage JSON (as
    hyb_obstacles.Cam), and Persp.Apparent."""

    def __init__(self, L):
        self.f = float(L["focalPx"])
        a = math.radians(float(L["pitch"]))
        self.s, self.c = math.sin(a), math.cos(a)
        self.cam = np.array([0.0, float(L["standH"]) + float(L["camUp"]), -float(L["camBack"])])
        self.W, self.H = int(L["widthPx"]), int(L["heightPx"])

    def px(self, p):
        r = np.asarray(p, np.float64) - self.cam
        depth = max(0.05, r[2] * self.c - r[1] * self.s)
        yc = r[1] * self.c + r[2] * self.s
        return self.W / 2 + self.f * r[0] / depth, self.H / 2 - self.f * yc / depth

    def apparent(self, p):
        x, y, z = p
        if y >= 0:
            return p
        ti = math.hypot(x - self.cam[0], z - self.cam[2]) / self.cam[1]
        st = ti / math.sqrt(1 + ti * ti) / 1.333
        tt = st / math.sqrt(1 - st * st)
        return (x, y * tt / max(1e-4, ti), z)


def obx_td(path):
    return C.load_pixels(path)[::-1].copy()


def obx_rgb(h):
    return np.array(C.hex_rgb(h), np.float32)


def obx_paste(dst, img, x, y, scale=1, tint=None, alpha=1.0):
    """Alpha-blend img (top-down) into dst at (x, y) with nearest upscaling (and a multiply tint); clipped."""
    img = img.copy()
    if tint is not None:
        img[..., :3] *= obx_rgb(tint)
    img[..., 3] *= alpha
    big = np.repeat(np.repeat(img, scale, 0), scale, 1) if scale > 1 else img
    h, w = big.shape[:2]
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    s = big[y0 - y:y1 - y, x0 - x:x1 - x]
    a = s[..., 3:4]
    d = dst[y0:y1, x0:x1]
    d[..., :3] = d[..., :3] * (1 - a) + s[..., :3] * a


def obx_fill(dst, x, y, w, h, col):
    dst[max(0, y):max(0, y + h), max(0, x):max(0, x + w), :3] = obx_rgb(col) if isinstance(col, str) else col


def obx_stage(stage):
    """(the 480x270 view top-down, the front layer's opacity in it, camera, obstacles, the view's canvas offset)."""
    import json
    with open(os.path.join(C.DATA, f"stage_{stage}.json"), encoding="utf-8") as f:
        L = json.load(f)
    with open(os.path.join(C.DATA, f"obstacles_{stage}.json"), encoding="utf-8") as f:
        obst = json.load(f)["obstacles"]
    back = obx_td(os.path.join(C.SPRITES, "Stages", f"{stage}_back.png"))
    front = obx_td(os.path.join(C.SPRITES, "Stages", f"{stage}_front.png"))
    cam = ObxCam(L)
    ox, oy = (cam.W - 480) // 2, (cam.H - 270) // 2
    view = back.copy()
    obx_paste(view, front, 0, 0)
    return view[oy:oy + 270, ox:ox + 480].copy(), front[oy:oy + 270, ox:ox + 480, 3] > 0.5, cam, obst, (ox, oy)


def obx_view_px(cam, off, p):
    x, y = cam.px(cam.apparent(p))
    return x - off[0], y - off[1]


def obx_line(a, b):
    """Bresenham, inclusive."""
    (x0, y0), (x1, y1) = a, b
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err, out = dx + dy, []
    while True:
        out.append((x0, y0))
        if x0 == x1 and y0 == y1:
            return out
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def obx_aim_outlines(view, front, cam, off, obst, cast=24.0, alpha=0.32):
    """Spec 9.1-9.2 on a 480x270 view: every snag / weed zone within cast + 2 m of the angler, its polygon at y = top
    lifted by Persp.Apparent, 1 px dashed 2 on / 2 off, not over the front layer, zones < 4 px across skipped."""
    cols = {"snag": obx_rgb("#d8f0ff"), "weed": obx_rgb("#b8e8a0")}
    n = 0
    for o in obst:
        if o["kind"] not in cols:
            continue
        pts = list(zip(o["pts"][0::2], o["pts"][1::2]))
        if min(math.hypot(x, z) for x, z in pts) > cast + 2.0:
            continue
        y = o["top"] if o["top"] > -50 else -2.0
        pp = [obx_view_px(cam, off, (x, y, z)) for x, z in pts]
        xs, ys = [p[0] for p in pp], [p[1] for p in pp]
        if max(max(xs) - min(xs), max(ys) - min(ys)) < 4:
            continue
        n += 1
        ip = [(int(math.floor(x)), int(math.floor(y))) for x, y in pp]
        k, last = 0, None
        for a, b in zip(ip, ip[1:] + ip[:1]):
            for (x, y) in obx_line(a, b):
                if (x, y) == last:
                    continue
                last = (x, y)
                on = (k % 4) < 2
                k += 1
                if on and 0 <= x < 480 and 0 <= y < 270 and not front[y, x]:
                    view[y, x, :3] = view[y, x, :3] * (1 - alpha) + cols[o["kind"]] * alpha
    return n


def obx_sheet(made, out):
    """Review sheet: the UI icons (8x, on the fight panel and on water), the meter (4x: empty / 30 % cream / 60 %
    orange / 90 % red, + a 2x mock of the fight strip's corner), every FX frame (8x) on its ground, the chips in the
    material tints, then the game view (2x) with 4x zooms: the aim outlines on the stream and on the lake, the contact
    and rub FX on the stream, the frog's pad landing on the lake."""
    img = {k: obx_td(v[0]) for k, v in made.items()}
    blocks = []                                   # (height, draw(sheet, y))
    SW = 1984
    PANEL, PANEL_IN, WATER = "#243448", "#1a2636", "#3e7a78"

    def icons(sheet, y):
        x = 16
        for name in OBX_ICONS:
            for bg in (PANEL_IN, "#5a9ab0", WATER):
                obx_fill(sheet, x, y, 144, 144, bg)
                obx_paste(sheet, img[name], x + 8, y + 8, 8)
                x += 152
            x += 16
        obx_fill(sheet, x, y, 72, 56, PANEL_IN)
        obx_paste(sheet, img["abr_label"], x + 8, y + 8, 8)
    blocks.append((144, icons))

    def meter(sheet, y):
        x = 24
        for frac, tint in ((0.0, None), (0.3, "#e8d8a0"), (0.6, "#ff8a3a"), (0.9, "#ff6a5a")):
            obx_fill(sheet, x - 8, y, 78 * 4 + 16, 5 * 4 + 16, PANEL_IN)
            obx_paste(sheet, img["abr_bar_bg"], x, y + 8, 4)
            if frac > 0:
                f = img["abr_bar_fill"].copy()
                f[:, int(round(frac * f.shape[1])):, 3] = 0
                obx_paste(sheet, f, x + 4, y + 12, 4, tint)
            x += 78 * 4 + 32
        # the fight strip's lower right corner at 2x canvas: the stamina bar (156x14 at (510, 13)) and above it the
        # abrasion row: the glyph at (496, 28), the bar at (510, 28) 156x10 (canvas y up from the panel's bottom)
        s, pw, ph = 2, 220, 44
        x0, y0 = SW - 16 - pw * s, y
        obx_fill(sheet, x0, y0, pw * s, ph * s, PANEL)
        obx_fill(sheet, x0 + 4, y0 + 4, pw * s - 8, ph * s - 8, PANEL_IN)

        def at(cx, cy, h):
            return x0 + (cx - 456) * s, y0 + (ph - cy - h) * s
        bx, by = at(510, 13, 14)
        obx_fill(sheet, bx, by, 156 * s, 14 * s, "#0a1018")
        obx_fill(sheet, bx + 4, by + 4, int(156 * s * 0.55) - 8, 14 * s - 8, "#ff8a4a")
        gx, gy = at(496, 28, 10)
        obx_paste(sheet, img["abr_label"], gx, gy, 2 * s)
        bx, by = at(510, 28, 10)
        obx_paste(sheet, img["abr_bar_bg"], bx, by, 2 * s)
        f = img["abr_bar_fill"].copy()
        f[:, int(round(0.62 * f.shape[1])):, 3] = 0
        obx_paste(sheet, f, bx + 2 * s, by + 2 * s, 2 * s, "#ff8a3a")
    blocks.append((88, meter))

    def fx(sheet, y):
        def cell(x, y, name, scale, ground, tint=None):
            a = img[name]
            w, h = a.shape[1] * scale + 16, a.shape[0] * scale + 16
            obx_fill(sheet, x, y, w, h, {"deep": "#1c4a4c", "night": "#101a2a"}.get(ground, WATER))
            if ground == "rock":                  # the flash sits on the prop's edge: rock above, water below
                obx_fill(sheet, x, y, w, 8 + a.shape[0] * scale // 2, "#8e8e88")
            if ground == "pad":                   # a lily pad (lake colours) under the splash
                for j in range(a.shape[0]):
                    for i in range(a.shape[1]):
                        if ((i + 0.5 - 8) / 7.6) ** 2 + ((j + 0.5 - 5.5) / 3.8) ** 2 < 1:
                            obx_fill(sheet, x + 8 + i * scale, y + 8 + j * scale, scale, scale, "#76ac52" if j < 5 else "#568e4c")
            obx_paste(sheet, a, x + 8, y + 8, scale, tint)
            return x + w + 8
        # row 1: the flash (near, far), the chips in the rock tint (near, far), the rub sparks
        x = 16
        for n in [f"fx_hit_f{k}" for k in range(4)] + [f"fx_hit_s_f{k}" for k in range(3)]:
            x = cell(x, y, n, 8, "rock")
        x += 16
        for n in [f"fx_chip_f{k}" for k in range(4)] + [f"fx_chip_s_f{k}" for k in range(3)]:
            x = cell(x, y, n, 8, "rock", "#a0a098")
        x += 16
        for k in range(4):
            x = cell(x, y, f"fx_rub_f{k}", 8, "deep")
        # row 2: the pad landing, the crystal-tinted flash on dark water, the dash key
        y += 112 + 8
        x = 16
        for k in range(4):
            x = cell(x, y, f"fx_padland_f{k}", 8, "pad")
        x += 16
        for k in range(4):
            x = cell(x, y, f"fx_hit_f{k}", 8, "night", "#aef0ff")
        x += 16
        cell(x, y, "obst_dash", 8, "water")
        # row 3: the chips in each material tint (6x): rock, concrete, wood / root, hull
        y += 112 + 8
        x = 16
        for tint in ("#a0a098", "#c8c8c0", "#8a6a48", "#e8e8e8"):
            for k in range(4):
                x = cell(x, y, f"fx_chip_f{k}", 6, "water", tint)
            x += 16
    blocks.append((112 + 8 + 112 + 8 + 88, fx))

    # the game views: stages whose obstacle data exists (the lake's comes from its own Blender module)
    views, zooms = [], []
    have = [s for s in ("stream", "lake") if os.path.exists(os.path.join(C.DATA, f"obstacles_{s}.json"))]
    for stage in have:
        view, front, cam, obst, off = obx_stage(stage)
        n = obx_aim_outlines(view, front, cam, off, obst)
        print(f"OBST ART sheet: {stage} aim outlines, {n} zones in range")
        views.append(view)
        zones = [o for o in obst if o["kind"] in ("snag", "weed") and 8 <= o["z"] <= 26]
        pick = [o for o in zones if o["id"] in ("md13.5.skirt", "weedbed")] or sorted(zones, key=lambda o: -o["r"])[:1]
        zx, zy = obx_view_px(cam, off, (pick[0]["x"], 0.0, pick[0]["z"])) if pick else (240, 135)
        zooms.append((int(zx) - 90, int(zy) - 50))
    if "stream" in have:                        # a contact on md25.0 (flash + rock chips), a rub at md13.5, a far hit
        view, front, cam, obst, off = obx_stage("stream")
        by = {o["id"]: o for o in obst}

        def spr(name, p, tint=None):
            x, y = obx_view_px(cam, off, p)
            a = img[name]
            obx_paste(view, a, int(round(x - a.shape[1] / 2)), int(round(y - a.shape[0] / 2)), 1, tint)
            return x, y
        md = by["md25.0"]
        hx, hy = spr("fx_chip_f1", (md["x"] - 0.95, 0.3, md["z"] - 0.3), "#a0a098")
        spr("fx_hit_f1", (md["x"] - 0.95, 0.3, md["z"] - 0.3))
        md = by["md13.5"]
        spr("fx_rub_f0", (md["x"] + 1.0, -0.5, md["z"] - 0.5))
        spr("icon_rub", (md["x"] + 1.0, 1.4, md["z"] - 0.5))
        md = by["md44.0"]
        spr("fx_hit_s_f1", (md["x"] + 1.0, 0.3, md["z"] - 0.3))
        views.append(view)
        zooms.append((int(hx) - 110, int(hy) - 40))
    if "lake" in have:                          # the frog on the nearest pad (f1), the impact frame on the next one
        view, front, cam, obst, off = obx_stage("lake")
        pads = sorted((o for o in obst if o["kind"] == "pad"), key=lambda o: o["z"])[:2]
        frog = obx_td(os.path.join(WORLD, "bait_frog_w.png"))
        for k, pad in enumerate(pads):
            x, y = obx_view_px(cam, off, (pad["x"], 0.03, pad["z"]))
            obx_paste(view, img[f"fx_padland_f{1 - k}"], int(round(x - 8)), int(round(y - 5)))
            if k == 0:
                obx_paste(view, frog, int(round(x - frog.shape[1] / 2)), int(round(y - frog.shape[0] + 3)))
                zooms.append((int(x) - 110, int(y) - 60))
        if pads:
            views.append(view)

    def game(sheet, y):
        for k, v in enumerate(views):
            cx = 16 + (k % 2) * (960 + 32)
            cy = y + (k // 2) * (540 + 16 + 400 + 24)
            obx_paste(sheet, v, cx, cy, 2)
            zx, zy = zooms[k]
            zx, zy = max(0, min(480 - 180, zx)), max(0, min(270 - 100, zy))
            obx_paste(sheet, v[zy:zy + 100, zx:zx + 180].copy(), cx + 120, cy + 540 + 16, 4)
    blocks.append((((len(views) + 1) // 2) * (540 + 16 + 400 + 24), game))

    H = sum(h + 32 for h, _ in blocks) + 16
    sheet = np.zeros((H, SW, 4), np.float32)
    sheet[..., :3] = obx_rgb("#202c3a")
    sheet[..., 3] = 1
    y = 16
    for h, draw in blocks:
        draw(sheet, y)
        y += h + 32
    C.save_pixels(sheet[::-1].copy(), out)
    print("OBST ART sheet ->", out)


# ============================================================================ AQUARIUM FEED: bags, food, FX, icons
# The aquarium feeding art (no feed button: the owned feed bags stand on the cabinet ledge under the tank; a new bag is
# sealed, the player swipes along its dotted perforation to tear the top off, then drags the open bag over the tank,
# where it tilts and sprinkles food from its mouth). Group "aquafeed" (+ "dry": into _tmp/aquafeed/items|ui|world):
#   blender -b --python Tools/Blender/fk_items.py -- aquafeed [dry]
#   review sheet _tmp/aquafeed/art_sheet.png, in-scene mock _tmp/aquafeed/mock.png (idle on the ledge / pouring)
# Every sprite is imported with the CENTRE pivot (PixelArtImporter, 16 PPU) and has an even size, so the centre sits on
# a pixel corner. Offsets below: sprite px from the centre, +x right, +y UP (1 px = 1/16 unit). <k> = basic | premium.
# The bags are a toon-shaded Blender pouch mesh (smooth bulge, the aquarium's upper-left light, shade on the right) whose
# print is a pixel texture (UV = the rest pixel, nearest texel, 4x4 samples per pixel -> majority colour, so the bent /
# tilted prints stay clean); the 1 px outline is the items' hue-shifted darker neighbour tone.
# Anchors (printed by the build as "AQUAFEED anchor ..."):
#   standing 32x36: the base (outline bottom) is 18 px below the centre -> centre = ledge point + (0, 18 px); the body
#     spans x -10..+10. Ledge in the aquarium: the cabinet top, image row 284 of the 640x400 room = world y -5.25.
#   perforation (sealed): the 1 px dotted row centred at y = +11.5, from x = -10 to +10 (notched at both ends). Swipe
#     hit test: a band of about +-3 px round y = +11.5; the stroke must run along it over most of -10..+10.
#   open mouth: the dark mouth row at (0, +10.5); the torn top reaches +13.
#   tilt / pour 40x36: the sprite centre = the standing sprite's (0, -2) (the grip: swap sprites without a jump);
#     pour mouth = (+13, -7.5) (spawn the food a pixel or two out along (0.866, -0.5)); flipX -> (-13, -7.5);
#     tilt mouth = (+13, +7.5).
#   strip_f0 centre = standing centre + (0, +13); tear burst centre = standing centre + (0, +11) (its row 8 = the line).
# World (Sprites/World)
#   feed_<k>_sealed        32x36  new bag: the dotted perforation across its top (+ side notches, a printed scissors at
#                                 its left end), the crimped seal band above it
#   feed_<k>_open4..open1  32x36  torn open: 4/4 (food showing in the mouth) .. 1/4; the clear window shows the food
#                                 level and the empty top slumps to the right as it empties
#   feed_<k>_fold_f0, f1   32x36  empty: f0 collapsed / crumpled, f1 folded in half (then remove it; the next spare
#                                 bag appears sealed)
#   feed_<k>_tilt          40x36  lifted over the tank, tilted 60 deg clockwise (transition frame, no food yet)
#   feed_<k>_pour          40x36  pouring: tilted 120 deg clockwise, the mouth down-right (flipX to pour to the left)
#   feed_<k>_strip_f0..f2  24x12  the torn-off top strip: f0 = in place at the moment of the tear, f1 / f2 tumbling
#                                 (drop it with gravity and fade over ~0.4 s)
#   feed_<k>_tear_f0..f3   24x16  the tear burst (paper fibres + print confetti up, then falling), ~0.06 s a frame
#   feed_pellet_0..2        4x4   basic pellets (round, cylinder, dark)            } falling / sinking food particles
#   feed_flake_0..2         4x4   premium golden flakes (the lit edge glints)      } (flipX free)
#   feed_flake_glint_f0,f1  6x6   a flake flashing (small / big sparkle): swap in now and then while it sinks
#   feed_splash_f0..f3     16x12  a pellet hitting the surface: dent + crown drops, rebound, ripple foam spreading; the
#                                 row just BELOW the centre lies on the meniscus row: centre = (x, 5.1875) (the top edge
#                                 of image row 117 = surfaceY 5.2 on the pixel grid)
#   feed_crumb_f0..f2       8x8   a fish eats a pellet: specks scatter from the mouth (greys: tint with the food colour)
#   feed_dissolve_f0..f3   10x6   an uneaten pellet on the bottom softens into a fading cloud (greys: tint)
#   feed_shadow            28x4   contact shadow under a standing bag (centre on the ledge line; hide while dragged)
#   feed_scissors_f0, f1   16x12  the swipe hint: open / closed scissors (blades right) to slide along the perforation
# UI (Sprites/UI): icon_full 16 (배불러요: a plump happy fish + heart), icon_hungry 16 (배고파요: a thin fish, open mouth,
#   sweat drop), icon_growth 16 (성장: a green up arrow over a ruler)
# Items (Sprites/Items): feed_basic, feed_premium 32x32 shop icons (the sealed bag + a little spilled food)
AFD_OUT = os.path.join(C.TMP, "aquafeed")
AFD_WORK = os.path.join(AFD_OUT, "work")          # own scratch (other groups render to _tmp/raw.png)
AFD_CW, AFD_CH = 32, 36                           # standing canvas
AFD_BODY = (6, 1, 20, 34)                         # body: first col, first row (bottom-up), width, sealed height
AFD_PCW, AFD_PCH = 40, 36                         # tilt / pour canvas
AFD_PIVOT = (16, 16)                              # standing-canvas point at the tilt / pour sprite's centre
AFD_TILT, AFD_POUR = 60.0, 120.0                  # deg clockwise
AFD_SCW, AFD_SCH, AFD_SPIV = 24, 12, (16, 31)     # strip canvas; standing-canvas point at its centre
AFD_FOIL = ("#c8d0da", "#8e98a6")                 # the bag's inner lining (lit, shade)
AFD_TORN = "#f4f0e4"                              # torn film edge / paper fibres
AFD_MOUTH = "#1e1622"
AFD_KINDS = {
    "basic": dict(
        body=["#1c4a8c", "#2c6cbc", "#4290dc", "#76b6f0", "#c4e2fa"],
        seal=("#2c6cbc", "#76b6f0", "#aad4f6", "#8cc4f2"), perf="#f6f0dc", mark="#f6f0dc",
        field="#f6efd8", ring="#dccca4", fish=dict(T="#c85a1c", O="#f58a2c", L="#ffc27a", D="#d0661e", E="#1a1a2a"),
        stripe="#76b6f0", wframe="#d8eef8", wempty=("#aab6c4", "#8a98aa"), glint="#f4fbff",
        food=["#4e2c12", "#7a4a24", "#9e6634", "#c89058"], conf=["#76b6f0", "#4290dc", "#f58a2c"],
        box=("#f6efd8", "#9aa6b8"), code="#1a1a2a"),
    "premium": dict(
        body=["#3c0c1c", "#62162c", "#8c2640", "#b84456", "#e08888"],
        seal=("#a8741c", "#f0c040", "#fff0a0", "#f8d868"), perf="#ffe89a", mark="#ffe89a",
        field="#2a0a14", ring="#d8a030", fish=dict(T="#b07a1c", O="#f0c040", L="#fff0a0", D="#c89028", E="#2a0a14"),
        stripe="#e0b040", wframe="#d8a030", wempty=("#b4aab0", "#8e8490"), glint="#fffbe8",
        food=["#a86a14", "#d89a28", "#f4c848", "#fff2b0"], conf=["#f0c040", "#b84456", "#fff0a0"],
        box=("#2a0a14", "#d8a030"), code="#f0c040"),
}
AFD_FISH = ["......LLL...",                         # the printed fish logo (12x6, top row first), facing right
            "T....LLLLL..",
            ".T.OOOOOOEO.",
            "..TOOOOOOOOO",
            ".T.OOOOOOOO.",
            "T....DDDD..."]
AFD_SCISSORS = ["o..bb",                            # the printed scissors (5x3, top row first), blades to the right
                ".xx..",
                "o..bb"]
# fill levels of the open states: food fraction, slump (hinge row above the body bottom, deg, pinch, squash), crease
AFD_LEVELS = {4: (1.0, None, 0.0), 3: (0.75, (22, 7.0, 0.06, 0.05), 0.35),
              2: (0.5, (16, 22.0, 0.14, 0.18), 0.55), 1: (0.25, (11, 36.0, 0.22, 0.32), 0.7)}


def afd_hex(col):
    return col.lower() if isinstance(col, str) else "#%02x%02x%02x" % tuple(
        int(round(max(0.0, min(1.0, c)) * 255)) for c in col[:3])


def afd_sm(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------------------- the bag's print (pixel texture)
def afd_design(k, cw, ch, ox, oz, w, h, state, fill, back=False):
    """The print of bag kind k as a canvas-sized RGBA array (rows bottom-up) + its silhouette mask; the body is w x h px
    with its bottom-left pixel at canvas (ox, oz); h = the SEALED height. state: sealed | open | pour; fill 0..1 = the
    food level in the window (pour: the food has slid towards the mouth, the top of the window)."""
    P = AFD_KINDS[k]
    img = np.zeros((ch, cw, 4), np.float32)
    mask = np.zeros((ch, cw), bool)

    def put(i, j, col, m=True):
        x, z = ox + i, oz + j
        if 0 <= x < cw and 0 <= z < ch:
            img[z, x, :3] = C.hex_rgb(col)
            img[z, x, 3] = 1.0
            if m:
                mask[z, x] = True

    body = P["body"]
    for j in range(h):
        for i in range(w):
            if (j == 0 and (i < 2 or i > w - 3)) or (j == 1 and (i < 1 or i > w - 2)):
                continue                                           # the rounded gusset corners
            if j == h - 1 and (i < 1 or i > w - 2):
                continue                                           # the seal band's corners
            put(i, j, body[2])
    for i in range(w):                                             # gusset fold line
        put(i, 2, body[1])
    for j in range(3, h - 7):                                      # the film's gloss: a light streak down the left
        put(1, j, body[3])
        if j % 3 == 0 and j > 4:
            put(2, j, body[3])
    # the label: a rounded field (ring on the edge for premium, a shade edge for basic) with the fish logo
    l0, l1 = h - 17, h - 9
    for j in range(l0, l1 + 1):
        for i in range(2, w - 2):
            if (j in (l0, l1)) and (i in (2, w - 3)):
                continue
            edge = j in (l0, l1) or i in (2, w - 3)
            if k == "premium":
                put(i, j, P["ring"] if edge else P["field"])
            else:
                put(i, j, P["ring"] if (j == l0 or (edge and i == w - 3)) else P["field"])
    fx0 = (w - 12) // 2
    for r, row in enumerate(AFD_FISH):
        for c, ch_ in enumerate(row):
            if ch_ != ".":
                put(fx0 + c, l1 - 2 - r, P["fish"][ch_])
    big = h >= 32
    if big:
        for i in range(1, w - 1):
            put(i, h - 19, P["stripe"])
    # the clear window: frame ring (rounded), food up to the level, the bag's lining above it, a glass glint
    w0, w1 = 4, (h - 21 if big else h - 19)
    c0, c1 = (w - 12) // 2, (w - 12) // 2 + 11
    n = w1 - w0 - 1
    nf = int(round(fill * n))
    rng = random.Random(7 if k == "basic" else 11)
    food = P["food"]
    for j in range(w0, w1 + 1):
        for i in range(c0, c1 + 1):
            if j in (w0, w1) and i in (c0, c1):
                continue
            if j in (w0, w1) or i in (c0, c1):
                put(i, j, P["wframe"])
                continue
            r = j - w0 - 1                                         # 0 = the window's bottom content row
            has = (r >= n - nf) if state == "pour" else (r < nf)
            top = (r == nf - 1) if state != "pour" else (r == n - nf)
            if has and top and state != "pour" and nf < n and (i * 7 + 3) % 5 == 0:
                has = False                                        # a bumpy heap surface
            if has:
                v = rng.random()
                if k == "basic":
                    col = food[3] if v < 0.18 else food[0] if v < 0.36 else food[1] if v < 0.62 else food[2]
                else:
                    col = food[3] if v < 0.22 else food[0] if v < 0.34 else food[2] if v < 0.7 else food[1]
                put(i, j, col)
            else:
                put(i, j, P["wempty"][0] if r >= n // 2 else P["wempty"][1])
    for g in range(2):                                             # glass glint: two short diagonal strokes
        for d in range(2 - g):
            i, j = c0 + 2 + d + 2 * g, w1 - 2 - d - g
            if w0 < j < w1:
                put(i, j, P["glint"])
    if back:
        # the back print (seen on the flap of the folded empty bag): an info box with text lines + a barcode
        bx0, bx1 = 4, w - 5
        box, txt = P["box"]
        for j in range(9, h - 13):
            for i in range(bx0, bx1 + 1):
                put(i, j, box if (j in (9, h - 14) or i in (bx0, bx1) or (j % 2 == 1 or i % 5 == 0)) else txt)
        for i in range(w // 2 - 4, w // 2 + 4):
            for j in (4, 5, 6):
                put(i, j, P["code"] if i % 2 == 0 or i % 3 == 0 else P["field"] if k == "basic" else body[2])
    if state == "sealed":
        edge, ca, cb, top = P["seal"]
        for i in range(w):
            if 0 < i < w - 1:
                put(i, h - 1, top)
            put(i, h - 2, ca if i % 2 else cb)
            put(i, h - 3, cb if i % 2 else ca)
            put(i, h - 4, edge)
        pr = h - 6                                                 # the perforation row
        for i in range(7, w - 1):
            if i % 2 == 1:
                put(i, pr, P["perf"])
        for r, row in enumerate(AFD_SCISSORS):
            for c, ch_ in enumerate(row):
                if ch_ != ".":
                    put(1 + c, pr + 1 - r, P["mark"])
        for i in (0, w - 1):                                       # the tear notches at both ends of the line
            mask[oz + pr, ox + i] = False
            img[oz + pr, ox + i] = 0.0
    else:
        # torn open: the front film's jagged torn edge (white), the open mouth (dark, food showing when full) and the
        # back film's inner lining (foil) above it
        ft, bt = afd_tear_line(w, h, state)
        mouth = range(w // 2 - 5, w // 2 + 5)
        for i in range(w):
            for j in range(ft[i], h):
                x, z = ox + i, oz + j
                mask[z, x] = False
                img[z, x] = 0.0
            put(i, ft[i], AFD_TORN)
            for j in range(ft[i] + 1, bt[i] + 1):
                if i in mouth and (j == ft[i] + 1 or (state == "pour" and j < bt[i])):   # pouring: a wider mouth
                    if state == "pour":
                        showf = i % 3 != 1 if j == ft[i] + 1 else (i + 2 * j) % 5 == 0
                    else:
                        showf = fill >= 0.99 and i % 2 == 0
                    put(i, j, (food[2] if i % 4 == 0 else food[3]) if showf else AFD_MOUTH)
                elif j == ft[i] + 1:
                    put(i, j, AFD_FOIL[1])
                else:
                    put(i, j, AFD_FOIL[0])
    return img, mask


def afd_tear_line(w, h, state):
    """-> (front film top row, back film top row) per body column after the tear (body rows)."""
    fj = [1, 0, 1, 1, 0, 1, 0, 1, 1, 0]
    bj = [1, 0, 1, 1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 1, 1]
    dip = h - 9 if state == "pour" else h - 8
    ft, bt = [], []
    for i in range(w):
        f = h - 7 + fj[i % len(fj)]
        if w // 2 - 4 <= i < w // 2 + 4:
            f = dip
        elif i in (w // 2 - 5, w // 2 + 4):
            f = dip + 1
        ft.append(f)
        bt.append(max(f, h - 6 + bj[i % len(bj)]))
    return ft, bt


# ---------------------------------------------------------------------------- the pouch (Blender mesh + toon)
def afd_image(name, arr):
    im = bpy.data.images.new(name, arr.shape[1], arr.shape[0], alpha=True)
    im.pixels.foreach_set(np.ascontiguousarray(arr, np.float32).ravel())
    try:
        im.pack()
    except Exception:
        pass
    return im


def afd_bag_material(name, front, back, shine=0.0):
    """The print (nearest texel) x the items' toon shade (no glossy spot by default: the gloss streak is printed);
    back faces show the back print."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    uv = nb.node("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    texs = []
    for im in (front, back):
        t = nb.node("ShaderNodeTexImage")
        t.image = im
        t.interpolation = "Closest"
        t.extension = "EXTEND"
        nb.link(uv.outputs["UV"], t.inputs["Vector"])
        texs.append(t)
    geo = nb.node("ShaderNodeNewGeometry")
    base = nb.mix(geo.outputs["Backfacing"], texs[0].outputs["Color"], texs[1].outputs["Color"])
    shade = C.toon_shade(nb, shine)
    col = nb.mix(1.0, base, shade, "MULTIPLY")
    nb.output_emission(col, 1.0)
    return m


def afd_point(x, z, g):
    """Rest canvas point (x, z) -> the deformed pouch surface point (x, y, z); y < 0 = towards the camera."""
    bx0, bz0, bw = g["body"]
    xc, hw = bx0 + bw / 2.0, bw / 2.0
    u = max(-1.0, min(1.0, (x - xc) / hw))
    a = math.sqrt(max(0.0, 1.0 - u * u))
    zb = z - bz0
    zf = g["zf"]
    r = 0.55 + 0.45 * afd_sm(0.0, 6.0, zb)
    top = 1.0 - afd_sm(zf - 5.0, zf + 1.0, zb) * (1.0 - g["resid"])
    y = -g["depth"] * a * r * top
    if g.get("crease"):
        y += g["crease"] * afd_sm(zf - 1.0, zf + 3.0, zb) * math.sin(x * 1.7 + z * 0.9) * a
    if g.get("slump"):
        # the empty top collapses (squash + pinch) and flops over to the right round the hinge line's centre
        zh, deg, pinch, squash = g["slump"]
        if zb > zh:
            t = afd_sm(zh, zh + 5.0, zb)
            dz = (zb - zh) * (1.0 - squash * t)
            dx = (x - xc) * (1.0 - pinch * t)
            th = math.radians(deg) * t
            x = xc + dx * math.cos(th) + dz * math.sin(th)
            z = bz0 + zh - dx * math.sin(th) + dz * math.cos(th)
            y *= 1.0 - 0.5 * t
    if g.get("fold"):
        # folded in half: the top half turned down in front of the bottom half (its back print faces the camera)
        zh, deg = g["fold"]
        if zb > zh:
            dz = zb - zh
            th = math.radians(deg) * afd_sm(zh, zh + 1.5, zb)
            z = bz0 + zh + dz * math.cos(th)
            y = y - dz * math.sin(th) - 0.3
    if g.get("rot"):
        cx, cz, deg, tx, tz = g["rot"]
        th = math.radians(deg)
        dx, dz = x - cx, z - cz
        x = tx + dx * math.cos(th) + dz * math.sin(th)
        z = tz - dx * math.sin(th) + dz * math.cos(th)
    return (x, y, z)


def afd_bag_object(name, front, back, mask, g, sub=2):
    """The pouch as a grid mesh over the rest silhouette (sub x sub quads per pixel, shared verts: smooth normals),
    UVs = the rest canvas position, so the print lands 1:1 on the pixels in the standing pose."""
    ch, cw = mask.shape
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    verts = {}

    def vert(gi, gj):
        v = verts.get((gi, gj))
        if v is None:
            v = verts[(gi, gj)] = bm.verts.new(afd_point(gi / sub, gj / sub, g))
        return v
    for z in range(ch):
        for x in range(cw):
            if not mask[z, x]:
                continue
            for a in range(sub):
                for b in range(sub):
                    gi, gj = x * sub + a, z * sub + b
                    corners = [(gi, gj), (gi + 1, gj), (gi + 1, gj + 1), (gi, gj + 1)]
                    f = bm.faces.new([vert(*c) for c in corners])
                    for lp, (ci, cj) in zip(f.loops, corners):
                        lp[uvl].uv = (ci / sub / cw, cj / sub / ch)
    ob = C.mesh_object(name, bm, afd_bag_material(name + "M", afd_image(name + "F", front), afd_image(name + "B", back)))
    C.set_smooth(ob)
    return ob


def afd_render(cw, ch, tag, outline=True, cx=None, cz=None, ss=1):
    """Render the scene over the canvas [0, cw] x [0, ch] (pixel units, or centred on (cx, cz)) -> RGBA bottom-up.
    ss > 1: render ss x ss samples per pixel and keep each pixel's majority colour (covered by at least half of its
    samples), so a rotated / bent print keeps its lines instead of the breakup of one-sample-per-pixel sampling."""
    bpy.context.view_layer.update()
    C.ortho_camera(cw / 2.0 if cx is None else cx, ch / 2.0 if cz is None else cz, cw * ss, ch * ss, float(ss))
    p = os.path.join(AFD_WORK, tag + ".png")
    C.render_raw(p)
    raw = C.load_pixels(p)
    if ss > 1:
        raw = afd_majority(raw, ss)
    return C.pixelize(raw, outline=outline)


def afd_majority(arr, s):
    h, w = arr.shape[0] // s, arr.shape[1] // s
    q = np.round(np.clip(arr[..., :3], 0, 1) * 255).astype(np.int64)
    key = (q[..., 0] << 16) | (q[..., 1] << 8) | q[..., 2]
    op = arr[..., 3] > 0.5
    out = np.zeros((h, w, 4), np.float32)
    for j in range(h):
        for i in range(w):
            m = op[j * s:(j + 1) * s, i * s:(i + 1) * s]
            if m.sum() * 2 < s * s:
                continue
            vals, cnt = np.unique(key[j * s:(j + 1) * s, i * s:(i + 1) * s][m], return_counts=True)
            kk = int(vals[np.argmax(cnt)])
            out[j, i, :3] = ((kk >> 16) & 255, (kk >> 8) & 255, kk & 255)
            out[j, i, :3] /= 255.0
            out[j, i, 3] = 1.0
    return out


def afd_bag_geo(state, level=4):
    bx0, bz0, bw, bh = AFD_BODY
    g = dict(body=(bx0, bz0, bw), depth=4.2, resid=0.0, zf=bh - 5)
    if state in ("open", "pour", "tilt"):
        fill, slump, crease = AFD_LEVELS[level]
        g.update(zf=bh - 7 if level == 4 else 4 + fill * (bh - 12), resid=0.35 if level == 4 else 0.2, crease=crease)
        if slump:
            g["slump"] = slump
    if state == "fold0":
        g.update(depth=2.0, zf=3, resid=0.25, crease=0.9, slump=(8, 58.0, 0.3, 0.45))
    if state == "fold1":
        g.update(depth=0.9, zf=2, resid=0.4, crease=0.5, fold=(15, 162.0))
    if state in ("pour", "tilt"):
        pv = AFD_PIVOT
        g["rot"] = (pv[0], pv[1], AFD_POUR if state == "pour" else AFD_TILT, AFD_PCW / 2.0, AFD_PCH / 2.0)
    return g


def afd_bag_sprite(k, state, level=4):
    """One bag sprite -> RGBA bottom-up."""
    bx0, bz0, bw, bh = AFD_BODY
    C.clear_objects()
    dstate = {"sealed": "sealed", "pour": "pour"}.get(state, "open")
    fill = 0.0 if state.startswith("fold") else 0.5 if state == "pour" else AFD_LEVELS[level][0]
    front, mask = afd_design(k, AFD_CW, AFD_CH, bx0, bz0, bw, bh, dstate, fill)
    back, _ = afd_design(k, AFD_CW, AFD_CH, bx0, bz0, bw, bh, dstate, fill, back=True)
    afd_bag_object("Bag", front, back, mask, afd_bag_geo(state, level))
    if state in ("pour", "tilt"):
        return afd_render(AFD_PCW, AFD_PCH, f"{k}_{state}", ss=4)
    return afd_render(AFD_CW, AFD_CH, f"{k}_{state}{level}", ss=4)


def afd_strip_sprites(k):
    """The torn-off strip (the sealed print above the tear line) in place, then tumbling away (3 frames)."""
    bx0, bz0, bw, bh = AFD_BODY
    front, mask = afd_design(k, AFD_CW, AFD_CH, bx0, bz0, bw, bh, "sealed", 1.0)
    ft, _ = afd_tear_line(bw, bh, "open")
    smask = np.zeros_like(mask)
    for i in range(bw):
        x = bx0 + i
        for j in range(ft[i], bh):
            z = bz0 + j
            smask[z, x] = mask[z, x] or (j == ft[i] and 0 < i < bw - 1)
        front[bz0 + ft[i], x, :3] = C.hex_rgb(AFD_TORN)            # its torn lower edge
        front[bz0 + ft[i], x, 3] = 1.0
    out = []
    sx, sz = AFD_SPIV
    for f, (deg, curl, dx, dz) in enumerate(((0.0, 0.0, 0, 0), (-28.0, 1.2, 1, 0), (-72.0, 2.0, 2, -1))):
        C.clear_objects()
        g = dict(body=(bx0, bz0, bw), depth=0.8, resid=0.0, crease=0.0, zf=bh + 10,
                 rot=(sx, sz, deg, AFD_SCW / 2.0 + dx, AFD_SCH / 2.0 + dz))
        ob = afd_bag_object("Strip", front, front, smask, g)
        if curl:
            for v in ob.data.vertices:                             # the strip curls as it falls
                v.co.y -= curl * ((v.co.x - AFD_SCW / 2.0) / 10.0) ** 2
        out.append(afd_render(AFD_SCW, AFD_SCH, f"{k}_strip{f}", ss=4))
    return out


# ---------------------------------------------------------------------------- pixel sprites (flat cells)
_AFD_Z = [0]


def afd_flat(col):
    name = "AFD" + afd_hex(col)
    m = bpy.data.materials.get(name)
    if m is None:
        m = C.glow_material(name, afd_hex(col), 1.0)
        m.name = name
    return m


def afd_cells(cells, col, a=1.0):
    """Unit squares at canvas pixels (i, j) (j from the bottom), flat colour, alpha a (quarter steps); later calls paint
    over earlier ones."""
    cells = sorted(set((int(round(i)), int(round(j))) for i, j in cells))
    if not cells:
        return None
    bm = bmesh.new()
    for i, j in cells:
        v = [bm.verts.new((i, 0, j)), bm.verts.new((i + 1, 0, j)), bm.verts.new((i + 1, 0, j + 1)), bm.verts.new((i, 0, j + 1))]
        bm.faces.new(v)
    ob = C.mesh_object("px", bm, afd_flat(col))
    _AFD_Z[0] += 1
    ob.location.y = -0.002 * _AFD_Z[0]
    ob["a"] = float(a)
    return ob


def afd_px(w, h, parts, tag):
    """parts = [(cells, colour, alpha)] -> RGBA bottom-up (colour pass + alpha pass, alpha in quarter steps)."""
    C.clear_objects()
    for cells, col, a in parts:
        afd_cells(cells, col, a)
    bpy.context.view_layer.update()
    cw, chh = max(w, 4), max(h, 4)
    C.ortho_camera(cw / 2.0, chh / 2.0, cw, chh, 1.0)
    p = os.path.join(AFD_WORK, tag + "_c.png")
    C.render_raw(p)
    col = C.load_pixels(p)
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in objs:
        g = float(o.get("a", 1.0))
        for s in o.material_slots:
            s.material = afd_flat((g, g, g))
    p = os.path.join(AFD_WORK, tag + "_a.png")
    C.render_raw(p)
    am = C.load_pixels(p)
    col, am = col[:h, :w], am[:h, :w]
    out = np.zeros_like(col)
    cov = col[..., 3] > 0.5
    out[..., :3] = col[..., :3]
    out[..., 3] = np.where(cov, np.round(np.clip(am[..., 0], 0, 1) * 4) / 4, 0.0)
    out[out[..., 3] <= 0] = 0.0
    return out


def afd_food_sprites():
    """Pellets (basic) and flakes (premium), 4x4, + the flake glint frames 6x6."""
    L, M_, D, K = "#c89058", "#9a6232", "#7a4a24", "#5a3416"
    pel = [[([(1, 2)], L, 1), ([(2, 2), (1, 1)], M_, 1), ([(2, 1)], K, 1)],
           [([(0, 2), (1, 2)], L, 1), ([(2, 2), (0, 1), (1, 1)], M_, 1), ([(2, 1)], K, 1)],
           [([(1, 2)], "#a86e3a", 1), ([(2, 2), (1, 1)], D, 1), ([(2, 1)], "#48280e", 1)]]
    G3, G2, G1, G0 = "#fff2b0", "#ffd24a", "#e8a830", "#b87818"
    fl = [[([(0, 2)], G3, 1), ([(1, 2)], G2, 1), ([(2, 2), (1, 1)], G1, 1), ([(2, 1)], G0, 1)],
          [([(1, 2)], G3, 1), ([(2, 2), (0, 1)], G2, 1), ([(1, 1)], G0, 1)],
          [([(1, 3)], G2, 1), ([(2, 2)], G3, 1), ([(1, 2)], G1, 1), ([(2, 1)], G0, 1), ([(1, 1)], G1, 1)]]
    gl = [[([(2, 2), (3, 2)], G1, 1), ([(2, 3)], G2, 1), ([(3, 3)], "#ffffff", 1),
           ([(4, 3), (3, 4), (2, 3)], G3, 0.75)],
          [([(2, 2), (3, 2)], G1, 1), ([(2, 3)], G2, 1), ([(3, 3)], "#ffffff", 1),
           ([(4, 3), (3, 4), (3, 1)], G3, 1), ([(5, 3), (3, 5), (1, 3), (3, 0)], G3, 0.5)]]
    out = {}
    for k_, parts in enumerate(pel):
        out[f"feed_pellet_{k_}"] = (afd_px(4, 4, parts, f"pel{k_}"), ["round pellet", "cylinder pellet", "dark pellet"][k_])
    for k_, parts in enumerate(fl):
        out[f"feed_flake_{k_}"] = (afd_px(4, 4, parts, f"fl{k_}"), ["golden flake (wide)", "golden flake (slanted)",
                                                                     "golden flake (curled)"][k_])
    for k_, parts in enumerate(gl):
        out[f"feed_flake_glint_f{k_}"] = (afd_px(6, 6, parts, f"gl{k_}"), ["flake glint: small sparkle",
                                                                           "flake glint: big sparkle"][k_])
    return out


def afd_splash_sprites():
    """16x12 over the tank's surface: row j = 5 (just below the centre) lies ON the meniscus row, j >= 6 is the lamp-lit
    air gap (light: the drops are water-teal there), j <= 4 the water (the ripple is white foam just under the meniscus).
    Place the centre at (x, 5.1875) = the top edge of the meniscus row (surfaceY 5.2 on the pixel grid)."""
    W_, F_, T_, TL, B_ = "#ffffff", "#e2f4ea", "#5692a4", "#78b4b6", "#cfeede"
    frames = [
        [([(7, 5), (8, 5)], T_, 1), ([(6, 6), (9, 6)], W_, 1), ([(5, 7), (10, 7)], TL, 1),
         ([(6, 4), (7, 4), (8, 4), (9, 4)], W_, 1), ([(8, 3)], B_, 0.75)],
        [([(7, 5), (8, 5)], T_, 0.5), ([(8, 6)], W_, 1), ([(8, 7)], TL, 1), ([(8, 8)], T_, 1), ([(4, 8), (11, 8)], TL, 1),
         ([(4, 4), (5, 4), (10, 4), (11, 4)], W_, 1), ([(7, 4), (8, 4)], F_, 0.5), ([(8, 2)], B_, 0.75)],
        [([(3, 7), (12, 7)], TL, 0.75), ([(8, 6)], T_, 0.75), ([(2, 4), (3, 4), (4, 4), (11, 4), (12, 4), (13, 4)], F_, 0.75),
         ([(7, 1)], B_, 0.5)],
        [([(0, 4), (1, 4), (2, 4), (13, 4), (14, 4), (15, 4)], F_, 0.5), ([(3, 5), (12, 5)], W_, 0.5)],
    ]
    names = ["impact: meniscus dented, crown drops, foam under the surface", "rebound jet + drops up, ripple ring",
             "drops falling, ripple spreads", "faint wide ripple, drops landing"]
    return {f"feed_splash_f{k_}": (afd_px(16, 12, p, f"spl{k_}"), names[k_]) for k_, p in enumerate(frames)}


def afd_crumb_sprites():
    W_, L, G = "#ffffff", "#d8d8d8", "#a8a8a8"
    crumb = [
        [([(4, 4)], W_, 1), ([(3, 4), (4, 5)], L, 1), ([(5, 3)], G, 1), ([(3, 3)], G, 0.75)],
        [([(2, 5)], W_, 1), ([(5, 5), (6, 3)], L, 1), ([(3, 2)], G, 1), ([(4, 6), (1, 3)], G, 0.75)],
        [([(1, 6), (2, 1)], L, 0.5), ([(6, 6), (7, 2)], G, 0.5), ([(4, 7), (0, 3)], G, 0.25)],
    ]
    diss = [
        [([(4, 3)], W_, 1), ([(5, 3), (4, 2)], L, 1), ([(5, 2)], G, 1), ([(3, 2), (6, 3)], G, 0.5)],
        [([(4, 2), (5, 2)], L, 0.75), ([(3, 3), (6, 3), (2, 2), (7, 2)], L, 0.5), ([(5, 4)], G, 0.25)],
        [([(1, 2), (8, 2), (4, 2), (6, 3)], L, 0.5), ([(3, 3), (2, 1), (7, 1), (5, 4)], G, 0.25)],
        [([(0, 2), (9, 2), (3, 1), (6, 1), (5, 3)], L, 0.25)],
    ]
    out = {}
    for k_, p in enumerate(crumb):
        out[f"feed_crumb_f{k_}"] = (afd_px(8, 8, p, f"cr{k_}"), ["bite: specks at the mouth", "specks scatter",
                                                                 "fading specks"][k_] + " (tint)")
    for k_, p in enumerate(diss):
        out[f"feed_dissolve_f{k_}"] = (afd_px(10, 6, p, f"ds{k_}"), ["pellet on the bottom softens",
                                                                     "breaks into a cloud", "cloud spreads low",
                                                                     "last faint specks"][k_] + " (tint)")
    return out


def afd_tear_sprites(k):
    """24x16, centre = the middle of the perforation line (row 8 = the line): fibres (white strands) + print confetti
    burst up and out along the line, then fall (a small deterministic particle toss, t in frames)."""
    P = AFD_KINDS[k]
    rng = random.Random(21 if k == "basic" else 22)
    g = 0.55
    parts = []
    for n_ in range(18):
        x0 = -9.5 + 19.0 * (n_ + rng.random() * 0.8) / 18.0
        vx = x0 * 0.08 + rng.uniform(-0.9, 0.9)
        vz = rng.uniform(1.6, 3.3)
        fib = n_ % 3 == 0
        col = AFD_TORN if fib or n_ % 5 == 1 else P["conf"][n_ % len(P["conf"])]
        parts.append((x0, vx, vz, fib, col))
    out = {}
    for f, (t, a) in enumerate(((0.6, 1.0), (1.6, 1.0), (2.9, 0.75), (4.3, 0.5))):
        cells = []
        if f == 0:                                                 # the rip itself: a white flash along the line
            cells.append(([(12 + d, 8) for d in range(-10, 10, 2)], AFD_TORN, 1.0))
        for q, (x0, vx, vz, fib, col) in enumerate(parts):
            x = 12 + x0 + vx * t
            z = 8.5 + vz * t - g * t * t
            pts = [(x, z)]
            if fib:                                                # a curly fibre: a 2 px diagonal that tumbles
                pts.append((x + (1 if (q + f) % 2 else -1), z + 1))
            pts = [(math.floor(px), math.floor(pz)) for px, pz in pts if 0 <= px < 24 and 0 <= pz < 16]
            if pts:
                cells.append((pts, col, a))
        out[f"feed_{k}_tear_f{f}"] = (afd_px(24, 16, cells, f"{k}_tear{f}"),
                                      ["the rip: white flash + first bits", "fibres + confetti flung up",
                                       "top of the toss, spreading", "falling, fading"][f])
    return out


def afd_shadow():
    S = "#1a0e14"
    parts = [([(i, 3) for i in range(2, 26)], S, 0.25), ([(i, 2) for i in range(1, 27)], S, 0.5),
             ([(i, 1) for i in range(3, 25)], S, 0.5), ([(i, 0) for i in range(6, 22)], S, 0.25)]
    return afd_px(28, 4, parts, "shadow")


# ---------------------------------------------------------------------------- toon icons (pixel units, centred)
def afd_disc(cx, cz, rx, rz=None, n=20):
    rz = rx if rz is None else rz
    return [(cx + rx * math.cos(2 * math.pi * q / n), cz + rz * math.sin(2 * math.pi * q / n)) for q in range(n)]


def afd_heart(cx, cz, s, n=24):
    """The classic parametric heart, ~2 s wide, centred on (cx, cz)."""
    pts = []
    for q in range(n):
        t = 2 * math.pi * q / n
        x = 16 * math.sin(t) ** 3
        z = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((cx + s * x / 16.0, cz + s * (z + 2.5) / 16.0))
    return pts[::-1]


def afd_icon(name):
    """UI icons 16 px + the scissors hint 16x12: flat extruded toon shapes in pixel units round the origin."""
    C.clear_objects()
    if name == "icon_full":
        body = M("AfdFull", "#ffa24a", shine=0.8)
        extruded("Tail", [(-4.2, -0.8), (-7.2, 2.6), (-6.4, -0.8), (-7.2, -4.2)], M("AfdFullT", "#e8782a", shine=0.3), 1.6, 0.3)
        extruded("Body", afd_disc(-0.2, -0.8, 5.4, 4.8), body, 2.0, 0.6)
        C.tube_along("Eye", [(0.9, -1.6, -0.2), (1.8, -1.6, 0.8), (2.7, -1.6, -0.2)], 0.5, M("AfdEye", "#3a1e1e", flat=True), 6)
        ob = extruded("Cheek", afd_disc(3.1, -2.2, 0.9, 0.7, 10), M("AfdCheek", "#ff8a7a", flat=True), 0.4, 0)
        ob.location.y = -1.4
        ob = extruded("Heart", afd_heart(4.6, 4.2, 3.0), M("AfdHeart", "#ff5a7a", shine=0.9), 1.2, 0.3)
        ob.location.y = -2.4
    elif name == "icon_hungry":
        body = M("AfdHungry", "#9ab8d0", shine=0.6)
        extruded("Tail", [(-4.0, -1.2), (-7.2, 1.4), (-6.4, -1.2), (-7.2, -3.8)], M("AfdHungryT", "#6e8cae", shine=0.3), 1.6, 0.3)
        extruded("Body", afd_disc(-0.6, -1.2, 5.2, 2.8), body, 2.0, 0.5)
        ob = extruded("Mouth", afd_disc(4.2, -1.3, 1.1, 1.0, 10), M("AfdMouth", "#3a2a3e", flat=True), 0.4, 0)
        ob.location.y = -1.4
        ob = extruded("Eye", afd_disc(1.6, -0.3, 0.75, 0.75, 8), M("AfdEyeH", "#1e2230", flat=True), 0.4, 0)
        ob.location.y = -1.4
        ob = extruded("Drop", teardrop(4.4, 3.4, 1.5, 3.4, 90, 14), M("AfdDrop", "#8ae0ff", shine=0.9), 1.2, 0.3)
        ob.location.y = -2.0
    elif name == "icon_growth":
        extruded("Ruler", [(-7.0, -7.0), (7.0, -7.0), (7.0, -3.0), (-7.0, -3.0)], M("AfdRuler", "#f0c850", shine=0.6), 1.6, 0.3)
        tick = M("AfdTick", "#7a5a20", flat=True)
        for q, x in enumerate((-5.5, -3.5, -1.5, 0.5, 2.5, 4.5)):   # on pixel centres (a 1 px wide tick)
            zb = -5.4 if q % 2 == 0 else -4.4
            ob = extruded(f"T{q}", [(x - 0.5, -3.1), (x + 0.5, -3.1), (x + 0.5, zb), (x - 0.5, zb)], tick, 0.4, 0)
            ob.location.y = -1.2
        green = M("AfdUp", "#6ad06a", shine=0.8)
        ob = extruded("Shaft", [(-1.3, -1.8), (1.3, -1.8), (1.3, 2.6), (-1.3, 2.6)], green, 1.6, 0.3)
        ob.location.y = -1.6
        ob = arrow_head("Head", green, (0.0, 7.0), (0, 1), 4.8, 3.9, depth=1.6, bevel=0.3)
        ob.location.y = -1.6
    elif name in ("feed_scissors_f0", "feed_scissors_f1"):
        closed = name.endswith("f1")
        steel = M("AfdSteel", "#dfe6ee", shine=1.0)
        red = M("AfdHandle", "#e04a3a", shine=0.5, stops=[(0.0, C.lin("#b8a0c0")), (0.35, C.lin("#ffffff"))])
        tip = 0.6 if closed else 2.7
        for s in (1, -1):                                          # blades to the right, finger rings to the left
            C.tube_along(f"Blade{s}", [(-0.4, -0.6 * s, -0.3 * s), (3.4, -0.6 * s, s * tip * 0.5), (7.2, -0.6 * s, s * tip)],
                         [0.95, 0.7, 0.15], steel, 8)
            hz = s * (1.8 if closed else 2.4)
            C.tube_along(f"Arm{s}", [(0.2, 0.2, 0.0), (-2.6, 0.2, hz * 0.75)], 0.6, red, 6)
            torus(f"Ring{s}", red, 1.75, 0.6, (-4.3, 0.2, hz), rot=(math.radians(90), 0, 0), segs=16)
        C.add_prim("sphere", "Pin", M("AfdPin", "#6a7484", shine=0.6), radius=0.7, location=(0.2, -1.6, 0.0),
                   segments=8, ring_count=5)
    size = (16, 12) if name.startswith("feed_scissors") else (16, 16)
    return afd_render(size[0], size[1], name, cx=0.0, cz=0.0)


def afd_shop_icon(k):
    """Items/feed_<k>.png 32x32: the sealed bag (a 18x29 body, the same print) + a little spilled food at its foot."""
    C.clear_objects()
    ox, oz, w, h = 4, 1, 18, 29
    front, mask = afd_design(k, 32, 32, ox, oz, w, h, "sealed", 1.0)
    g = dict(body=(ox, oz, w), depth=3.8, resid=0.0, zf=h - 5)
    afd_bag_object("Bag", front, front, mask, g)
    if k == "basic":
        mat = M("AfdPellet", "#9a6232", shine=0.7)
        for q, (x, z) in enumerate(((23.6, 1.9), (26.4, 1.7), (25.0, 3.9), (28.6, 2.0))):
            sphere(f"P{q}", mat, 1.25, (x, -6.0 - q * 0.3, z), seg=10, rings=6)
    else:
        mat = M("AfdFlake", "#f0c040", shine=1.0)
        for q, (x, z, r) in enumerate(((23.8, 2.0, 15), (27.0, 1.8, -20), (25.4, 4.1, 40), (29.0, 2.6, -5))):
            ob = extruded(f"F{q}", [(-1.5, -0.7), (1.3, -0.9), (1.6, 0.6), (-0.4, 1.0), (-1.4, 0.4)], mat, 0.5, 0.15)
            ob.rotation_euler = (math.radians(-25), math.radians(r), 0)
            ob.location = (x, -6.0 - q * 0.3, z)
    return afd_render(32, 32, f"shop_{k}")


# ---------------------------------------------------------------------------- build + review sheet + mock
def afd_anchor_notes():
    """The spawn / hit-test anchors in sprite px from the centre (+y up), computed from the layout constants."""
    bx0, bz0, bw, bh = AFD_BODY
    cx, cz = AFD_CW / 2.0, AFD_CH / 2.0
    perf = bz0 + bh - 6
    ft, bt = afd_tear_line(bw, bh, "open")
    mouth_top = bz0 + max(bt) + 1                                  # above the back film
    mouth_c = bz0 + ft[bw // 2] + 1.5                              # the dark mouth row
    lip = bz0 + bh - 4.0                                           # a pixel out of the mouth (above the back film)
    px, _, pz = afd_point(bx0 + bw / 2.0, lip, afd_bag_geo("pour"))
    tx, _, tz = afd_point(bx0 + bw / 2.0, lip, afd_bag_geo("tilt"))
    return dict(
        base=(0.0, -cz),
        perf_line=((bx0 - cx, perf + 0.5 - cz), (bx0 + bw - cx, perf + 0.5 - cz)),
        mouth_open=(bx0 + bw / 2.0 - cx, mouth_c - cz), mouth_top=(0.0, mouth_top - cz),
        pour_mouth=(round(px - AFD_PCW / 2.0, 2), round(pz - AFD_PCH / 2.0, 2)),
        tilt_mouth=(round(tx - AFD_PCW / 2.0, 2), round(tz - AFD_PCH / 2.0, 2)),
        pour_grip=(AFD_PIVOT[0] - cx, AFD_PIVOT[1] - cz),
        strip_centre=(AFD_SPIV[0] - cx, AFD_SPIV[1] - cz), tear_centre=(0.0, perf - cz),
        pour_dir=(round(math.sin(math.radians(AFD_POUR)), 3), round(math.cos(math.radians(AFD_POUR)), 3)))


def aquafeed_art(dry=False):
    """Every sprite of the group -> Sprites/World|UI|Items (dry: _tmp/aquafeed/world|ui|items), the review sheet and
    the in-scene mock; returns {name: (path, meaning, w, h)}."""
    world = os.path.join(AFD_OUT, "world") if dry else WORLD
    ui = os.path.join(AFD_OUT, "ui") if dry else UI
    items = os.path.join(AFD_OUT, "items") if dry else ITEMS
    os.makedirs(AFD_WORK, exist_ok=True)
    made = {}

    def put(arr, folder, name, meaning):
        p = os.path.join(folder, name + ".png")
        C.save_pixels(arr, p)
        made[name] = (p, meaning, arr.shape[1], arr.shape[0])

    for k in AFD_KINDS:
        put(afd_bag_sprite(k, "sealed"), world, f"feed_{k}_sealed", "sealed: perforation + notches + printed scissors")
        for lv in (4, 3, 2, 1):
            put(afd_bag_sprite(k, "open", lv), world, f"feed_{k}_open{lv}", f"open, {lv}/4 left")
        put(afd_bag_sprite(k, "fold0"), world, f"feed_{k}_fold_f0", "empty: collapsed")
        put(afd_bag_sprite(k, "fold1"), world, f"feed_{k}_fold_f1", "empty: folded in half")
        put(afd_bag_sprite(k, "tilt"), world, f"feed_{k}_tilt", "lifted, tilted 60 deg")
        put(afd_bag_sprite(k, "pour"), world, f"feed_{k}_pour", "pouring, tilted 120 deg, mouth down-right")
        for f, a in enumerate(afd_strip_sprites(k)):
            put(a, world, f"feed_{k}_strip_f{f}", ["torn strip in place", "strip tumbling", "strip tumbling (edge on)"][f])
        for name, (a, meaning) in afd_tear_sprites(k).items():
            put(a, world, name, meaning)
    for group in (afd_food_sprites(), afd_splash_sprites(), afd_crumb_sprites()):
        for name, (a, meaning) in group.items():
            put(a, world, name, meaning)
    put(afd_shadow(), world, "feed_shadow", "contact shadow under a standing bag")
    for name in ("feed_scissors_f0", "feed_scissors_f1"):
        put(afd_icon(name), world, name, "swipe hint scissors " + ("open" if name.endswith("0") else "closed"))
    for name, meaning in (("icon_full", "배불러요: plump happy fish + heart"), ("icon_hungry", "배고파요: thin fish, open mouth, sweat"),
                          ("icon_growth", "성장: up arrow over a ruler")):
        put(afd_icon(name), ui, name, meaning)
    for k in AFD_KINDS:
        put(afd_shop_icon(k), items, f"feed_{k}", ("기본 사료" if k == "basic" else "고급 사료") + " shop icon")
    for name, (p, meaning, w, h) in made.items():
        print(f"AQUAFEED {name:24s} {w:3d}x{h:<3d} {meaning}")
    for key, v in afd_anchor_notes().items():
        print(f"AQUAFEED anchor {key:13s} {v}")
    afd_sheet(made, os.path.join(AFD_OUT, "art_sheet.png"))
    afd_mock(made, os.path.join(AFD_OUT, "mock.png"))
    return made


def afd_td(path):
    return C.load_pixels(path)[::-1].copy()


def afd_rgb(h):
    return np.array(C.hex_rgb(h), np.float32)


def afd_paste(dst, img, x, y, scale=1, tint=None, flip=False):
    """Alpha-blend img (top-down) into dst at (x, y) (top-left) with nearest upscaling and a multiply tint; clipped."""
    img = img[:, ::-1].copy() if flip else img.copy()
    if tint is not None:
        img[..., :3] *= afd_rgb(tint)
    big = np.repeat(np.repeat(img, scale, 0), scale, 1) if scale > 1 else img
    h, w = big.shape[:2]
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    s = big[y0 - y:y1 - y, x0 - x:x1 - x]
    a = s[..., 3:4]
    d = dst[y0:y1, x0:x1]
    d[..., :3] = d[..., :3] * (1 - a) + s[..., :3] * a


def afd_fill(dst, x, y, w, h, col):
    dst[max(0, y):max(0, y + h), max(0, x):max(0, x + w), :3] = afd_rgb(col)


def afd_save(arr_td, path):
    C.save_pixels(np.ascontiguousarray(arr_td[::-1]), path)


def afd_sheet(made, out):
    """Review sheet (flow layout, rows top-down): per kind the standing states (6x) on the ledge colours + tilt / pour
    (6x) over the water; per kind the torn strip and the tear burst (6x); pellets / flakes / glints (12x) and the
    surface splash (8x); crumb + dissolve in the basic and premium tints (8x) + the shadow; the UI icons (8x) on the info
    window's paper and on the dark panel + the scissors hint (8x); the shop icons (6x) and every bag at the game's 2x."""
    img = {k: afd_td(v[0]) for k, v in made.items()}
    WALL, WATER, GRAVEL, CAB, CABD, AIR = "#946e5c", "#5692a4", "#ba9c76", "#855a44", "#65423a", "#d6d2ae"

    def cell(name, s, bg, extra=None, tint=None, pad=0):
        a = img[name]
        w, h = a.shape[1] * s + 2 * pad, a.shape[0] * s + 2 * pad

        def draw(sh, x, y):
            afd_fill(sh, x, y, w, h, bg)
            if extra:
                extra(sh, x, y, w, h, s)
            afd_paste(sh, a, x + pad, y + pad, s, tint)
        return (w, h, draw)

    def ledge(sh, x, y, w, h, s):
        afd_fill(sh, x, y + h - 2 * s, w, 2 * s, CAB)

    def hood(sh, x, y, w, h, s):
        afd_fill(sh, x, y, w, 8 * s, CABD)

    def surface(sh, x, y, w, h, s):
        afd_fill(sh, x, y, w, 6 * s, AIR)
        afd_fill(sh, x, y + 6 * s, w, s, "#cfeede")

    def bottom(sh, x, y, w, h, s):
        afd_fill(sh, x, y + h - 3 * s, w, 3 * s, GRAVEL)

    rows = []
    for k in AFD_KINDS:
        rows.append([cell(f"feed_{k}_{n}", 6, GRAVEL, ledge) for n in
                     ["sealed", "open4", "open3", "open2", "open1", "fold_f0", "fold_f1"]]
                    + [cell(f"feed_{k}_{n}", 6, WATER, hood) for n in ["tilt", "pour"]])
    for k in AFD_KINDS:
        rows.append([cell(f"feed_{k}_strip_f{f}", 6, WALL) for f in range(3)]
                    + [cell(f"feed_{k}_tear_f{f}", 6, WALL) for f in range(4)])
    rows.append([cell(n, 12, WATER, pad=12) for n in [f"feed_pellet_{q}" for q in range(3)]
                 + [f"feed_flake_{q}" for q in range(3)] + ["feed_flake_glint_f0", "feed_flake_glint_f1"]]
                + [cell(f"feed_splash_f{f}", 8, WATER, surface) for f in range(4)])
    rows.append([cell(n, 8, WATER, bottom if "dissolve" in n else None, tint, pad=8) for tint in ("#a06a3a", "#ffcc40")
                 for n in [f"feed_crumb_f{f}" for f in range(3)] + [f"feed_dissolve_f{f}" for f in range(4)]]
                + [cell("feed_shadow", 6, CAB, lambda sh, x, y, w, h, s: afd_fill(sh, x, y, w, h // 2, GRAVEL), pad=12)])
    rows.append([cell(n, 8, bg, pad=16) for n in ["icon_full", "icon_hungry", "icon_growth"] for bg in ("#e8d8b0", "#243448")]
                + [cell(n, 8, "#4290dc", pad=8) for n in ["feed_scissors_f0", "feed_scissors_f1"]])
    rows.append([cell(f"feed_{k}", 6, "#3a4a5e", pad=12) for k in AFD_KINDS]
                + [cell(f"feed_{k}_{n}", 2, GRAVEL, ledge) for k in AFD_KINDS for n in
                   ["sealed", "open4", "open3", "open2", "open1", "fold_f0", "fold_f1", "tilt", "pour"]])
    GAP = 10
    SW = max(sum(c[0] for c in r) + GAP * (len(r) + 1) for r in rows)
    SH = sum(max(c[1] for c in r) for r in rows) + GAP * (len(rows) + 1)
    sheet = np.zeros((SH, SW, 4), np.float32)
    sheet[..., 3] = 1.0
    sheet[..., :3] = afd_rgb("#1e2836")
    y = GAP
    for r in rows:
        x = GAP
        for w, h, draw in r:
            draw(sheet, x, y)
            x += w + GAP
        y += max(c[1] for c in r) + GAP
    afd_save(sheet, out)
    print("AQUAFEED sheet ->", out, SW, "x", SH)


def afd_mock(made, out):
    """The game view (the 480x270 crop of the 640x400 aquarium) at 2x, two frames stacked: idle (both bags on the
    cabinet ledge, the premium one sealed with the swipe hint on its perforation) and pouring (the basic bag dragged
    over the tank from left to right: food falling from its mouth, splashes along its path, a trail of sinking pellets,
    a fish eating one, a pellet dissolving on the bottom; the premium bag still on the ledge)."""
    img = {k: afd_td(v[0]) for k, v in made.items()}
    back = afd_td(os.path.join(C.SPRITES, "Stages", "aquarium_back.png"))
    front = afd_td(os.path.join(C.SPRITES, "Stages", "aquarium_front.png"))

    def fish(name):
        return afd_td(os.path.join(C.SPRITES, "Fish", name + "_0.png"))

    def at(dst, a, cx, cy, tint=None, flip=False):              # centre pivot at image px (col, row)
        afd_paste(dst, a, int(round(cx - a.shape[1] / 2.0)), int(round(cy - a.shape[0] / 2.0)), 1, tint, flip)

    LEDGE = 284                                                    # the bags' base row (cabinet top, 640x400 image)
    notes = afd_anchor_notes()
    frames = []
    for mode in ("idle", "pour"):
        v = back.copy()
        fishes = [("golden_carp", 250, 190, False), ("bluegill", 420, 160, True), ("crucian_carp", 330, 225, False)]
        if mode == "pour":
            fishes = [("golden_carp", 333, 186, True), ("bluegill", 392, 150, True), ("crucian_carp", 250, 228, False)]
            for (px_, py_, q) in ((322, 168, 1), (330, 151, 2), (337, 137, 0), (343, 126, 1), (296, 214, 0)):
                at(v, img[f"feed_pellet_{q}"], px_, py_)           # the trail: older = further left and deeper
            at(v, img["feed_dissolve_f1"], 404, 262, "#a06a3a")
            at(v, img["feed_splash_f3"], 322, 117)
            at(v, img["feed_splash_f2"], 333, 117)
            at(v, img["feed_splash_f0"], 343, 117)
        for name, cx, cy, flip in fishes:
            at(v, fish(name), cx, cy, flip=flip)
        if mode == "pour":
            at(v, img["feed_crumb_f1"], 314, 186, "#a06a3a")
        afd_paste(v, front, 0, 0)
        cy = LEDGE - AFD_CH / 2.0
        if mode == "idle":
            at(v, img["feed_shadow"], 160, LEDGE)
            at(v, img["feed_basic_open3"], 160, cy)
        at(v, img["feed_shadow"], 196, LEDGE)
        at(v, img["feed_premium_sealed"], 196, cy)
        if mode == "idle":
            (lx0, ly), _ = notes["perf_line"]
            at(v, img["feed_scissors_f0"], 196 + lx0 + 4, cy - ly)
        else:
            pcx, pcy = 331, 96                                       # the dragged bag (its grip = the sprite centre)
            at(v, img["feed_basic_pour"], pcx, pcy)
            mx, my = notes["pour_mouth"]
            for q, d in enumerate((1.5, 6.5, 11.5)):                 # falling from the mouth to the surface
                at(v, img[f"feed_pellet_{q}"], pcx + mx + q, pcy - my + d)
        frames.append(v[65:65 + 270, 80:80 + 480])
    out_img = np.zeros((270 * 2 * 2 + 16, 480 * 2, 4), np.float32)
    out_img[..., 3] = 1.0
    out_img[..., :3] = afd_rgb("#1e2836")
    for q, f in enumerate(frames):
        afd_paste(out_img, f, 0, q * (540 + 16), 2)
    afd_save(out_img, out)
    print("AQUAFEED mock ->", out)


# ============================================================================ AQUARIUM LIVE FOOD: shrimp tub, sardine cooler
# Large fish don't eat pellets: 생새우 (live shrimp, a round tub) and 정어리 (sardines, a cooler box) stand on the
# cabinet ledge beside the feed bags and are fed ONE AT A TIME: tap the lid to open the container, press inside it to
# pick up one shrimp / sardine, drag it over the tank and let go: it falls, splashes, sinks; an eater dashes to it (a big
# fish surges to the surface and gulps it), the others ignore it and it dissolves on the bottom (feed_dissolve, tinted).
# Group "aqualive" (+ "dry": into _tmp/aqualive/items|ui|world):
#   blender -b --python Tools/Blender/fk_items.py -- aqualive [dry]
#   review sheet _tmp/aqualive/art_sheet.png, in-scene mock _tmp/aqualive/mock.png (idle ledge / a sardine held over the
#   tank / the gulp)
# Same conventions as AQUARIUM FEED: CENTRE pivot (PixelArtImporter, 16 PPU), even sizes, offsets in sprite px from the
# centre, +x right, +y UP. The containers are toon-shaded Blender meshes seen from ALF_TILT deg above (their open tops
# show; the bags are seen straight on), their prints are pixel textures projected 1:1 from the camera; the shrimp and
# sardines are small toon meshes (the shrimp's antennae / legs are 1 px strokes drawn after the outline); the contents
# of an open container get their own outlined pass so they read on the water / ice.
# Anchors (printed by the build as "AQUALIVE anchor ..."; rects = (x0, x1, y0, y1)):
#   both containers: 40 px tall, base (outline bottom) 20 px below the centre -> centre = ledge point + (0, 20 px) (the
#     bags use + 18); suggested ledge slots x = -5.5 (tub), -3.0 (cooler) beside AquaFeed.SlotX -10 / -7.75.
#   tub 24x40: lid (closed) (-9.2, 9.2, -11.0, -1.1), its front tab (0, -10.2); open lid standing behind
#     (-9.2, 9.2, -1.8, 18.0); the opening (water) (-7.7, 7.7, -11.2, -4.5), pick-up point (0, -7.4).
#   cooler 36x40: lid (closed) (-14.4, 14.4, -8.1, 1.2), its latch (0, -6.9); open lid (-14.4, 14.4, -1.2, 14.6); the
#     opening (ice) (-12.9, 12.9, -7.2, -2.0), pick-up point (0, -4.2).
#   held items: the finger's grip point = feed_shrimp_held_f* (0, +4) (behind the head), feed_sardine_held_f* (0, +8)
#     (the tail's wrist); their body centre (1.7, 1.4) / (2.1, 1.7) and (0, 1.8) / (-0.8, 1.8) = where to put the
#     fall / sink sprite's centre on release (no jump).
#   splashes (drop_s 16x14, drop_l 24x18, gulp 40x24): the row just below the centre lies on the meniscus row, as
#     feed_splash: centre = (x, 5.1875).
# World (Sprites/World)
#   feed_tub_closed|ajar|open|few|empty       24x40  shut (tap: lid / tab) | lid popping (transition) | open, 2 shrimp
#                                                    (2+ left) | the last shrimp (1 left) | only water (0 left)
#   feed_cooler_closed|ajar|open|few|empty    36x40  the same for the cooler: 3 sardines stuck in the ice tails-up (2+
#                                                    left) | the last one (1 left) | only ice
#   feed_tub_shadow 24x4, feed_cooler_shadow 36x4    contact shadows (centre on the ledge line; hide while carried)
#   feed_shrimp_held_f0, f1   16x24  held behind the head, tail hanging / tail flicked in (swap ~0.25 s: the dangle)
#   feed_shrimp_fall_f0, f1   16x16  released, falling: curled / tumbling (alternate)
#   feed_shrimp_sink_f0, f1   18x14  sinking head a little down, the legs and swimmerets flicker (alternate ~0.15 s)
#   feed_shrimp_rest          18x10  lying on the bottom (then fade out + feed_dissolve tinted #f7a07c)
#   feed_shrimp_bite_f0..f2   10x10  a fish bites it: coral shell bits scatter from the mouth
#   feed_sardine_held_f0, f1  12x28  held by the tail, head down / swung + curved (the dangle)
#   feed_sardine_fall_f0      20x24  falling nose-first;  feed_sardine_fall_f1 24x18 levelling out
#   feed_sardine_sink_f0, f1  26x14  sinking head a little down / body flexed; feed_sardine_glint 26x14 = sink_f0 with a
#                                    silver sparkle on the flank (swap in now and then)
#   feed_sardine_rest         26x12  lying on the bottom (fade + feed_dissolve tinted #c3d0da)
#   feed_drop_s_f0..f3        16x14  a shrimp hits the water: dent + crown, jet, drops fall, ripple
#   feed_drop_l_f0..f4        24x18  a sardine hits the water: bigger dent + crown, jet + drops, ring, ripple
#   feed_gulp_f0..f4          40x24  a big fish surges up and gulps at the surface: bulge, burst (white crown, sheets,
#                                    spray, foam cloud), spray peak, fall-back + bubbles, settling (~0.07 s a frame;
#                                    centre on the mouth, draw over the front frame so the spray tops the rim)
# UI (Sprites/UI): icon_diet_pellet, icon_diet_shrimp, icon_diet_sardine 16x16 (the info window's favourite food)
# Items (Sprites/Items): feed_shrimp, feed_sardine 32x32 shop icons (the shut container + a shrimp / sardine)
from mathutils import Matrix  # noqa: E402

ALF_OUT = os.path.join(C.TMP, "aqualive")
ALF_TILT = 26.0                                   # deg: the containers' tops lean towards the camera
ALF_TUB_CW, ALF_TUB_CH = 24, 40                   # tub canvas (every state)
ALF_BOX_CW, ALF_BOX_CH = 36, 40                   # cooler canvas (every state)
ALF_BASE = 1.0                                    # canvas row of the front-bottom edge (row 0 = its outline, as bags)
ALF_SHRIMP = dict(body="#f7a07c", seg="#e8825e", fan="#ee7048", eye="#1a1020", ant="#c4553a", leg="#e07a5c",
                  beak="#e88a68")
ALF_SARDINE = dict(back="#2c5f86", flank="#c3d0da", belly="#f1f5f8", spot="#1c3246", fin="#557a96", eye="#e8eef2",
                   pupil="#10141c")
ALF_TUB = dict(body="#e2eaee", inner="#aebfc6", band="#ee7650", label="#fff2dc", logo="#ee7650", lid="#2fb294",
               lid_in="#259a80", plug="#7ad6c0", pad="#48c4a6", hinge="#1f7e68", hole="#16614e", water="#5a9aae",
               ripple="#8ccad2", bubble="#e8f8f8")
ALF_BOX = dict(body="#eef1f3", inner="#a9bcc8", trim="#e2563a", lid="#e2563a", lid_in="#d4dde4", handle="#f4f6f8",
               latch="#8e98a4", logo="#2f78c4", logo_eye="#eef1f3", ice="#d6f0fa", hinge="#8e98a4")
ALF_FX = dict(W="#ffffff", F="#e2f4ea", T="#5692a4", L="#78b4b6", B="#cfeede")
ALF_TUB_LOGO = ["c..ccc..",                        # the coral shrimp printed on the tub's cream label (top row first)
                ".ccccccc",
                "..c.c.cc",
                "......c."]
ALF_BOX_LOGO = ["..bbb....",                       # the blue fish printed on the cooler's front (top row first)
                ".bbbbbb.b",
                "bwbbbbbbb",
                ".bbbbbb.b",
                "...bb...."]
ALF_LIDS = {"closed": 0.0, "ajar": 34.0, "open": 104.0, "few": 104.0, "empty": 104.0}   # lid angle (deg, back hinge)


# ---------------------------------------------------------------------------- scene helpers
def alf_empty(name, parent=None, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), scale=1.0, mode="XYZ"):
    ob = bpy.data.objects.new(name, None)
    C.link(ob)
    ob.parent = parent
    ob.rotation_mode = mode
    ob.location = loc
    ob.rotation_euler = rot
    ob.scale = (scale, scale, scale)
    return ob


def alf_adopt(objs, parent, pivot=None):
    """Parent objects built in the parent's local frame (pivot: the parent empty's rest location in that frame)."""
    inv = Matrix.Translation(Vector(pivot)).inverted() if pivot else Matrix.Identity(4)
    for ob in objs:
        ob.parent = parent
        ob.matrix_parent_inverse = inv
    return objs


def alf_world(ob, p):
    """Canvas point (x, z) of the local point p of ob (the camera looks along +y)."""
    w = ob.matrix_world @ Vector(p)
    return (w.x, w.z)


def alf_mat(name, base, bands=(), img=None, shine=0.0, flat=False):
    """Toon material: base colour, horizontal bands [(z0, z1, colour)] in object space, and an optional print (the
    canvas-projected UV map "Scr": a pixel image laid 1:1 over the canvas, see alf_screen_uv) over it."""
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    col = C.lin(base)
    if bands:
        tc = nb.node("ShaderNodeTexCoord")
        z = nb.sep(tc.outputs["Object"])[2]
        for z0, z1, c in bands:
            col = nb.mix(nb.in_range(z, z0, z1), col, C.lin(c))
    if img is not None:
        uv = nb.node("ShaderNodeUVMap")
        uv.uv_map = "Scr"
        t = nb.node("ShaderNodeTexImage")
        t.image = img
        t.interpolation = "Closest"
        t.extension = "CLIP"
        nb.link(uv.outputs["UV"], t.inputs["Vector"])
        col = nb.mix(t.outputs["Alpha"], col, t.outputs["Color"])
    shade = C.toon_shade(nb, shine, flat=flat)
    nb.output_emission(nb.mix(1.0, col, shade, "MULTIPLY"), 1.0)
    return m


def alf_screen_uv(ob, cw, ch):
    """UV map "Scr" = the canvas position of every corner (so a print image lands 1:1 on the rendered pixels)."""
    bpy.context.view_layer.update()
    me = ob.data
    uvl = me.uv_layers.get("Scr") or me.uv_layers.new(name="Scr")
    mw = ob.matrix_world
    for lp in me.loops:
        w = mw @ me.vertices[lp.vertex_index].co
        uvl.data[lp.index].uv = (w.x / cw, w.z / ch)


def alf_print(cw, ch):
    return np.zeros((ch, cw, 4), np.float32)


def alf_stamp(img, rows, cx, cz, pal):
    """Pattern rows (top row first, '.' = none) centred on canvas point (cx, cz) into a print (rows bottom-up)."""
    h, w = len(rows), len(rows[0])
    x0, z0 = int(round(cx - w / 2.0)), int(round(cz - h / 2.0))
    for r, row in enumerate(rows):
        for c, ch_ in enumerate(row):
            if ch_ in pal:
                x, z = x0 + c, z0 + h - 1 - r
                if 0 <= x < img.shape[1] and 0 <= z < img.shape[0]:
                    img[z, x, :3] = C.hex_rgb(pal[ch_])
                    img[z, x, 3] = 1.0


def alf_dot(img, x, z, col, a=1.0):
    i, j = int(math.floor(x)), int(math.floor(z))
    if 0 <= i < img.shape[1] and 0 <= j < img.shape[0]:
        img[j, i, :3] = C.hex_rgb(col)
        img[j, i, 3] = a


def alf_stroke(img, pts, col, a=1.0):
    """1 px line through canvas points [(x, z)]: Bresenham between the pixels holding the points (connected, clean
    diagonals)."""
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        i0, j0, i1, j1 = int(math.floor(x0)), int(math.floor(z0)), int(math.floor(x1)), int(math.floor(z1))
        di, dj = abs(i1 - i0), -abs(j1 - j0)
        si, sj = (1 if i1 > i0 else -1), (1 if j1 > j0 else -1)
        err = di + dj
        while True:
            alf_dot(img, i0 + 0.5, j0 + 0.5, col, a)
            if i0 == i1 and j0 == j1:
                break
            e2 = 2 * err
            if e2 >= dj:
                err += dj
                i0 += si
            if e2 <= di:
                err += di
                j0 += sj


def alf_render(cw, ch, tag, strokes=(), outline=True):
    """The scene over the canvas [0, cw] x [0, ch] at 4x4 samples per pixel (majority colour), outlined, then the 1 px
    strokes [(pts, colour)] on top -> RGBA bottom-up."""
    arr = afd_render(cw, ch, "alf_" + tag, outline=outline, ss=4)
    for pts, col in strokes:
        alf_stroke(arr, pts, col)
    return arr


def alf_cup(name, r0, r1, h, wall=0.7, floor=1.0, segs=48):
    """Open round tub: outer cone r0 (bottom) .. r1 (top), height h, its rim, inner wall and floor.
    Material slots: 0 outside + rim, 1 inside."""
    bm = bmesh.new()

    def ring(r, z):
        return [bm.verts.new((r * math.sin(2 * math.pi * k / segs), -r * math.cos(2 * math.pi * k / segs), z))
                for k in range(segs)]
    ob_, ot, it, ib = ring(r0, 0.0), ring(r1, h), ring(r1 - wall, h), ring(r0 - wall + (r1 - r0) * floor / h, floor)
    for k in range(segs):
        k2 = (k + 1) % segs
        bm.faces.new((ob_[k], ob_[k2], ot[k2], ot[k])).material_index = 0
        bm.faces.new((ot[k], ot[k2], it[k2], it[k])).material_index = 0
        bm.faces.new((it[k], it[k2], ib[k2], ib[k])).material_index = 1
    bm.faces.new(ob_[::-1]).material_index = 0
    bm.faces.new(ib).material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = len(f.verts) == 4
    return C.mesh_object(name, bm)


def alf_disc(name, r, z, segs=40, y=0.0):
    bm = bmesh.new()
    vs = [bm.verts.new((r * math.sin(2 * math.pi * k / segs), y - r * math.cos(2 * math.pi * k / segs), z))
          for k in range(segs)]
    bm.faces.new(vs)
    bm.normal_update()
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    return C.mesh_object(name, bm)


def alf_can(name, r, z0, z1, segs=40, loc=(0.0, 0.0)):
    """Closed cylinder (z0..z1). Material slots: 0 top + side, 1 underside."""
    bm = bmesh.new()
    lo = [bm.verts.new((loc[0] + r * math.sin(2 * math.pi * k / segs), loc[1] - r * math.cos(2 * math.pi * k / segs),
                        z0)) for k in range(segs)]
    hi = [bm.verts.new((v.co.x, v.co.y, z1)) for v in lo]
    for k in range(segs):
        k2 = (k + 1) % segs
        bm.faces.new((lo[k], lo[k2], hi[k2], hi[k]))
    bm.faces.new(hi)
    bm.faces.new(lo[::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    for f in bm.faces:
        f.smooth = len(f.verts) == 4
        f.material_index = 1 if f.normal.z < -0.5 else 0
    return C.mesh_object(name, bm)


def alf_rbox(name, x0, x1, y0, y1, z0, z1, r=1.5, segs=3, cavity=None):
    """Box with rounded plan corners; cavity = (wall, depth) digs an open top. Material slots: 0 outside, 1 underside,
    2 the cavity."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = (x0 if v.co.x < 0 else x1, y0 if v.co.y < 0 else y1, z0 if v.co.z < 0 else z1)
    vert = [e for e in bm.edges if abs(e.verts[0].co.x - e.verts[1].co.x) < 1e-6
            and abs(e.verts[0].co.y - e.verts[1].co.y) < 1e-6]
    bmesh.ops.bevel(bm, geom=vert, offset=r, offset_type="OFFSET", segments=segs, profile=0.5, affect="EDGES")
    if cavity:
        wall, depth = cavity
        top = max(bm.faces, key=lambda f: f.calc_center_median().z)
        bmesh.ops.inset_individual(bm, faces=[top], thickness=wall, depth=0.0)
        ext = bmesh.ops.extrude_face_region(bm, geom=[top])
        bmesh.ops.translate(bm, vec=(0.0, 0.0, -depth),
                            verts=[g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)])
        bmesh.ops.delete(bm, geom=[top], context="FACES_ONLY")    # the extrusion keeps the original cap at the rim
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    for f in bm.faces:
        c = f.calc_center_median()
        inner = cavity and c.z < z1 - 1e-3 and x0 + cavity[0] - 1e-3 < c.x < x1 - cavity[0] + 1e-3 \
            and y0 + cavity[0] - 1e-3 < c.y < y1 - cavity[0] + 1e-3
        f.material_index = 2 if inner else 1 if f.normal.z < -0.7 else 0
        f.smooth = False
    return C.mesh_object(name, bm)


def alf_items(subs):
    """The meshes of each content item (shrimp / sardine sub-root), back to front."""
    return [[o for o in s.children_recursive if o.type == "MESH"] for s in sorted(subs, key=lambda s: -s.location.y)]


def alf_set_mats(ob, mats):
    """Fill the material slots in order (Mesh.materials.clear() would reset the faces' material indices)."""
    for i, m in enumerate(mats):
        if i < len(ob.data.materials):
            ob.data.materials[i] = m
        else:
            ob.data.materials.append(m)


# ---------------------------------------------------------------------------- the shrimp and the sardine (toon meshes)
def alf_shrimp(parent, curl=0.0, legs=0, fan=0.0, name="Sh"):
    """Live shrimp in local px (head at +x, back up, ~13 px from the tail fan to the head), built under parent.
    curl 0..1 tucks the abdomen under; legs 0 / 1 = the two leg-flicker poses; fan = the tail fan's extra spread.
    Returns the 1 px strokes [(canvas pts, colour)] (antennae, beak, legs) - call after the parent is posed."""
    P = ALF_SHRIMP
    body_m = alf_mat(name + "B", P["body"], shine=0.55)
    seg_m = alf_mat(name + "S", P["seg"], shine=0.55)
    fan_m = alf_mat(name + "F", P["fan"], shine=0.4)
    eye_m = alf_mat(name + "E", P["eye"], flat=True)
    pts, rr, dirs = [], [], []
    x, z, phi = 5.0, 0.0, math.radians(166.0)
    radii = [0.75, 1.15, 1.28, 1.26, 1.2, 1.12, 1.02, 0.92, 0.8, 0.68, 0.58, 0.5]
    for k, r in enumerate(radii):
        pts.append((x, 0.0, z))
        rr.append(r)
        d = (math.cos(phi), math.sin(phi))
        dirs.append(d)
        x, z = x + d[0] * 1.15, z + d[1] * 1.15
        if k >= 2:
            phi += math.radians(10.0 + 24.0 * curl)
    body = C.tube_along(name + "Body", pts, rr, body_m, 10)
    body.data.materials.append(seg_m)
    for poly in body.data.polygons:                                         # the tail's segments: every other ring darker
        i = poly.index // 10
        if 4 <= i < len(pts) - 1 and i % 2 == 1:
            poly.material_index = 1
    for v in body.data.vertices:
        v.co.y *= 0.72
    objs = [body]
    # the tail fan: a flat spread fan off the last segment
    px_, _, pz_ = pts[-1]
    dx, dz = dirs[-1]
    nx, nz = -dz, dx
    s = 1.0 + fan
    fan_pts = [(px_ + 0.45 * nx, pz_ + 0.45 * nz), (px_ + 1.9 * dx + 1.5 * s * nx, pz_ + 1.9 * dz + 1.5 * s * nz),
               (px_ + 2.5 * dx + 0.5 * s * nx, pz_ + 2.5 * dz + 0.5 * s * nz),
               (px_ + 2.5 * dx - 0.5 * s * nx, pz_ + 2.5 * dz - 0.5 * s * nz),
               (px_ + 1.9 * dx - 1.5 * s * nx, pz_ + 1.9 * dz - 1.5 * s * nz), (px_ - 0.45 * nx, pz_ - 0.45 * nz)]
    objs.append(C.poly_object(name + "Fan", fan_pts, fan_m, thickness=0.5))
    for sy in (-1, 1):
        objs.append(sphere(name + "Eye", eye_m, 0.6, (4.2, sy * 0.85, 0.65), seg=10, rings=6))
    alf_adopt(objs, parent)
    bpy.context.view_layer.update()

    def cv(pl):
        return [alf_world(parent, (a, 0.0, b)) for a, b in pl]
    f = 1 if legs else -1
    strokes = [(cv([(5.3, 0.9), (6.8, 2.1), (8.2, 2.5)]), P["ant"]),
               (cv([(5.4, 0.2), (7.0, 0.2), (8.2, -0.4)]), P["ant"]),
               (cv([(5.4, 0.8), (6.5, 1.3)]), P["beak"])]
    for q, lx in enumerate((4.0, 3.0, 2.0)):                                # walking legs under the head
        strokes.append((cv([(lx, -0.9), (lx + 0.6 * f + 0.2 * (q - 1), -2.9)]), P["leg"]))
    for k in (5, 7, 9):                                                     # swimmerets under the tail, flickering
        (ax, _, az), r, (dx, dz) = pts[k], rr[k], dirs[k]
        ux, uz = -dz, dx                                                    # the underside direction
        bx, bz = ax + ux * (r + 0.3), az + uz * (r + 0.3)
        strokes.append((cv([(bx, bz), (bx + ux * 1.4 + dx * 0.7 * f, bz + uz * 1.4 + dz * 0.7 * f)]), P["leg"]))
    return strokes


def alf_sardine_material(name, bend=0.0):
    """Blue-green back / silver flank / white belly by the height above the (bent) spine, a row of dark spots."""
    P = ALF_SARDINE
    m = bpy.data.materials.new(name)
    nb = C.NB(m)
    tc = nb.node("ShaderNodeTexCoord")
    s = nb.sep(tc.outputs["Object"])
    x = s[0]
    q = nb.math("MULTIPLY", nb.math("SUBTRACT", x, 1.6), 1.0 / 8.0)
    zc = nb.math("SUBTRACT", s[2], nb.math("MULTIPLY", nb.math("MULTIPLY", q, q), bend))
    col = nb.mix(nb.math("LESS_THAN", zc, -0.95), C.lin(P["flank"]), C.lin(P["belly"]))
    col = nb.mix(nb.math("GREATER_THAN", zc, 0.6), col, C.lin(P["back"]))
    band = nb.math("LESS_THAN", nb.math("ABSOLUTE", nb.math("SUBTRACT", zc, 0.2)), 0.45)
    dots = nb.math("LESS_THAN", nb.math("FRACT", nb.math("MULTIPLY", nb.math("ADD", x, 0.3), 1.0 / 2.6)), 0.46)
    spot = nb.math("MULTIPLY", nb.math("MULTIPLY", band, dots), nb.in_range(x, -2.6, 5.4))
    col = nb.mix(spot, col, C.lin(P["spot"]))
    shade = C.toon_shade(nb, 1.0)
    nb.output_emission(nb.mix(1.0, col, shade, "MULTIPLY"), 1.0)
    return m


def alf_sardine(parent, bend=0.0, name="Sd"):
    """Sardine in local px (head at +x, back up; snout x = +9.6, tail fork tips x = -10), built under parent. bend > 0
    arches the body (the dangle's swing). Returns [] (no strokes)."""
    P = ALF_SARDINE
    prof = [(9.6, 0.35), (8.8, 1.1), (7.4, 1.75), (5.5, 2.15), (3.0, 2.3), (0.5, 2.15), (-2.0, 1.75), (-4.0, 1.25),
            (-5.6, 0.85), (-6.5, 0.72)]

    def zoff(x):
        return bend * ((x - 1.6) / 8.0) ** 2
    pts = [(x, 0.0, zoff(x)) for x, _ in prof]
    body = C.tube_along(name + "Body", pts, [r for _, r in prof], alf_sardine_material(name + "M", bend), 12)
    for v in body.data.vertices:
        v.co.y *= 0.6
    tz = zoff(-6.2)
    fin_m = alf_mat(name + "Fin", P["fin"], shine=0.7)
    tail = C.poly_object(name + "Tail", [(-5.8, tz + 0.7), (-8.6, tz + 2.9), (-10.1, tz + 3.3), (-8.5, tz),
                                        (-10.1, tz - 3.3), (-8.6, tz - 2.9), (-5.8, tz - 0.7)], fin_m, thickness=0.5)
    dz = zoff(1.0)
    dors = C.poly_object(name + "Dors", [(-0.6, dz + 1.9), (2.6, dz + 2.0), (0.4, dz + 3.2)], fin_m, thickness=0.4)
    objs = [body, tail, dors]
    eye_m = alf_mat(name + "Eye", P["eye"], shine=0.8)
    pup_m = alf_mat(name + "Pup", P["pupil"], flat=True)
    ez = zoff(7.0) + 0.35
    for sy in (-1, 1):
        objs.append(sphere(name + "Eye", eye_m, 0.95, (7.0, sy * 0.95, ez), scale=(1, 0.5, 1), seg=12, rings=6))
        objs.append(sphere(name + "Pup", pup_m, 0.55, (7.15, sy * 1.35, ez), scale=(1, 0.4, 1), seg=10, rings=5))
    alf_adopt(objs, parent)
    return []


# ---------------------------------------------------------------------------- the tub and the cooler
ALF_TUB_DIM = dict(r0=7.6, r1=8.6, h=10.4, lid_r=9.2, hinge=(0.0, 9.3, 11.0), water=8.7)
ALF_BOX_DIM = dict(w=14.0, d=7.0, h=12.6, lid=2.8, wall=1.1, depth=3.8)


def alf_container_root(kind, cw):
    """The container's root: base centre on the canvas, the top tilted ALF_TILT deg towards the camera."""
    t = math.radians(ALF_TILT)
    front = ALF_TUB_DIM["r0"] if kind == "tub" else ALF_BOX_DIM["d"]
    return alf_empty("Root", loc=(cw / 2.0, 0.0, ALF_BASE + front * math.sin(t)), rot=(t, 0.0, 0.0))


def alf_tub(state, cw=ALF_TUB_CW, ch=ALF_TUB_CH, root=None):
    """The live-shrimp tub (state: closed | ajar | open | few | empty) posed on the canvas; returns (root, info)."""
    P, D = ALF_TUB, ALF_TUB_DIM
    root = root or alf_container_root("tub", cw)
    r0, r1, h = D["r0"], D["r1"], D["h"]
    body = alf_cup("TubBody", r0, r1, h, wall=0.7)
    lip = torus("TubLip", None, r1 - 0.05, 0.5, (0.0, 0.0, h), segs=40)
    hx, hy, hz = D["hinge"]
    hinge_pin = cyl("TubHinge", alf_mat("TubHingeM", P["hinge"], shine=0.4), (-2.4, hy, hz), (2.4, hy, hz), 0.7)
    parts = [body, lip, hinge_pin]
    water = None
    if state in ("open", "few", "empty", "ajar"):
        water = alf_disc("TubWater", r0 + (r1 - r0) * D["water"] / h - 0.7, D["water"])
        parts.append(water)
    alf_adopt(parts, root)
    hinge = alf_empty("TubLidHinge", root, loc=(hx, hy, hz), rot=(-math.radians(ALF_LIDS[state]), 0.0, 0.0))
    lid = alf_can("TubLid", D["lid_r"], h - 0.2, h + 1.3)
    plug = alf_can("TubPlug", r1 - 1.0, h - 1.1, h - 0.2)                   # the snap ring under the lid (seen open)
    pad = alf_can("TubPad", 6.2, h + 1.3, h + 1.9)
    tab = sphere("TubTab", None, 1.0, (0.0, -D["lid_r"] - 0.7, h + 0.55), scale=(1.8, 1.0, 0.75), seg=12, rings=6)
    alf_adopt([lid, plug, pad, tab], hinge, pivot=(hx, hy, hz))
    shrimp = []
    if state in ("open", "few"):
        spots = [(-2.6, 2.8, 10.0, False), (2.8, -1.0, -8.0, True)]
        for q, (sx, sy, ang, flip) in enumerate(spots if state == "open" else [(0.4, 0.8, 6.0, False)]):
            sub = alf_empty(f"Shr{q}", root, loc=(sx, sy, D["water"] + 1.1), scale=0.82, mode="YZX",
                            rot=(-math.radians(50.0), math.radians(ang), math.pi if flip else 0.0))
            shrimp.append(sub)
    bpy.context.view_layer.update()
    strokes = []
    for q, sub in enumerate(shrimp):
        strokes += alf_shrimp(sub, curl=0.25 + 0.2 * q, legs=q % 2, name=f"Sh{q}")
    strokes = [(pts, col) for pts, col in strokes if col != ALF_SHRIMP["leg"]]   # legs are under water
    # prints: the band's shrimp logo (closed / ajar: the lid's air holes too), the water's ripple glints + bubbles
    band_img = alf_print(cw, ch)
    lx, lz = alf_world(root, (0.0, -(r0 + (r1 - r0) * 5.3 / h), 5.3))
    alf_stamp(band_img, ALF_TUB_LOGO, lx, lz, {"c": P["logo"]})
    alf_screen_uv(body, cw, ch)
    alf_set_mats(body, [alf_mat("TubOut", P["body"], bands=[(1.9, 8.7, P["band"]), (3.0, 7.6, P["label"])],
                                img=afd_image("TubBandP", band_img), shine=0.6), alf_mat("TubIn", P["inner"])])
    alf_set_mats(plug, [alf_mat("TubPlugM", P["lid"], shine=0.4), alf_mat("TubPlugIn", P["plug"], shine=0.4)])
    lip.data.materials.append(alf_mat("TubLipM", P["body"], shine=0.6))
    lid_img = alf_print(cw, ch)
    if state == "closed":
        for (ax, ay) in ((-2.6, 0.0), (2.6, 0.0), (0.0, 2.4), (0.0, -2.4)):
            px_, pz_ = alf_world(pad, (ax, ay, h + 1.9))
            alf_dot(lid_img, px_, pz_, P["hole"])
    alf_screen_uv(pad, cw, ch)
    alf_set_mats(lid, [alf_mat("TubLidM", P["lid"], shine=0.5), alf_mat("TubLidIn", P["lid_in"])])
    alf_set_mats(pad, [alf_mat("TubPadM", P["pad"], img=afd_image("TubPadP", lid_img), shine=0.5),
                       alf_mat("TubPadIn", P["lid_in"])])
    tab.data.materials.append(alf_mat("TubTabM", P["lid"], shine=0.5))
    if water is not None:
        w_img = alf_print(cw, ch)
        for q, (ax, ay, col) in enumerate(((-4.6, -2.0, P["ripple"]), (-3.8, -2.0, P["ripple"]),
                                           (4.2, -3.2, P["ripple"]), (5.0, 1.0, P["bubble"]),
                                           (-5.4, 3.0, P["bubble"]), (1.8, 3.6, P["bubble"]))):
            if state == "empty" or col == P["ripple"] or q == 3:
                px_, pz_ = alf_world(water, (ax, ay, D["water"]))
                alf_dot(w_img, px_, pz_, col)
        alf_screen_uv(water, cw, ch)
        water.data.materials.append(alf_mat("TubWaterM", P["water"], img=afd_image("TubWaterP", w_img), flat=True))
    info = dict(lid=[lid, plug, pad, tab], opening=water, strokes=strokes, root=root, tab=tab,
                contents=alf_items(shrimp), see_through=[water])
    return root, info


def alf_cooler(state, cw=ALF_BOX_CW, ch=ALF_BOX_CH, root=None):
    """The sardine cooler box (state: closed | ajar | open | few | empty) posed on the canvas; returns (root, info)."""
    P, D = ALF_BOX, ALF_BOX_DIM
    root = root or alf_container_root("box", cw)
    w, d, h, lh = D["w"], D["d"], D["h"], D["lid"]
    body = alf_rbox("BoxBody", -w, w, -d, d, 0.0, h, r=1.8, cavity=(D["wall"], D["depth"]))
    hinge_m = alf_mat("BoxHingeM", P["hinge"], shine=0.6)
    pins = [cyl(f"BoxPin{s}", hinge_m, (s * 8.0 - 1.6, d + 0.5, h + 0.3), (s * 8.0 + 1.6, d + 0.5, h + 0.3), 0.6)
            for s in (-1, 1)]
    parts = [body] + pins
    ice = []
    if state != "closed":
        rng = random.Random(5)
        ice_m = alf_mat("BoxIceM", P["ice"], shine=0.9)
        x_in, y_in, floor = w - D["wall"] - 1.0, d - D["wall"] - 1.0, h - D["depth"]
        for q in range(120):
            x, y = rng.uniform(-x_in, x_in), rng.uniform(-y_in, y_in)
            top = h - 1.1 + 1.0 * (y + y_in) / (2 * y_in)
            z = rng.uniform(floor + 0.8, top)
            s = rng.uniform(1.5, 2.3)
            ice.append(box("Ice", ice_m, s, s, s, loc=(x, y, z),
                           rot=(rng.uniform(0, 3.14), rng.uniform(0, 3.14), rng.uniform(0, 3.14))))
        parts += ice
    alf_adopt(parts, root)
    hy, hz = d + 0.4, h + 0.3
    hinge = alf_empty("BoxLidHinge", root, loc=(0.0, hy, hz), rot=(-math.radians(ALF_LIDS[state] * 0.97), 0.0, 0.0))
    lid = alf_rbox("BoxLid", -w - 0.4, w + 0.4, -d - 0.4, d + 0.4, h, h + lh, r=2.1)
    handle_m = alf_mat("BoxHandleM", P["handle"], shine=0.7)
    posts = [box(f"BoxPost{s}", handle_m, 1.6, 1.6, 2.0, loc=(s * 8.6, 0.0, h + lh + 0.9)) for s in (-1, 1)]
    bar = cyl("BoxBar", handle_m, (-8.9, 0.0, h + lh + 1.9), (8.9, 0.0, h + lh + 1.9), 0.8)
    latch = box("BoxLatch", alf_mat("BoxLatchM", P["latch"], shine=0.8), 4.2, 1.2, 1.9, loc=(0.0, -d - 0.8, h + 1.2))
    alf_adopt([lid, bar, latch] + posts, hinge, pivot=(0.0, hy, hz))
    fish = []
    if state in ("open", "few"):
        # stuck head-first in the ice, tails up (fanned): the forked tails read at this size
        spots = [(-6.2, 0.6, 16.6, 60.0), (0.6, 2.2, 17.6, 93.0), (6.6, 0.2, 16.4, 122.0)]
        use = spots if state == "open" else [(0.4, 0.8, 16.8, 84.0)]
        for q, (sx, sy, sz, ang) in enumerate(use):
            sub = alf_empty(f"Sar{q}", root, loc=(sx, sy, sz), rot=(-math.radians(28.0), math.radians(ang), 0.0),
                            scale=0.75, mode="YZX")
            alf_sardine(sub, name=f"Sd{q}")
            fish.append(sub)
    bpy.context.view_layer.update()
    logo = alf_print(cw, ch)
    lx, lz = alf_world(root, (0.0, -d, 6.6))
    alf_stamp(logo, ALF_BOX_LOGO, lx, lz, {"b": P["logo"], "w": P["logo_eye"]})
    alf_screen_uv(body, cw, ch)
    alf_set_mats(body, [alf_mat("BoxOut", P["body"], bands=[(-1.0, 2.0, P["trim"])], img=afd_image("BoxLogoP", logo),
                                shine=0.6), alf_mat("BoxUnder", P["trim"]), alf_mat("BoxCav", P["inner"])])
    alf_set_mats(lid, [alf_mat("BoxLidM", P["lid"], shine=0.6), alf_mat("BoxLidIn", P["lid_in"])])
    info = dict(lid=[lid, bar, latch] + posts, opening=body, root=root, latch=latch, ice=ice, strokes=[],
                contents=alf_items(fish), see_through=[])
    return root, info


# ---------------------------------------------------------------------------- sprites
def alf_bounds(objs):
    """Canvas bbox (x0, x1, z0, z1) of the objects' evaluated vertices."""
    bpy.context.view_layer.update()
    x0, x1, z0, z1 = C.world_bounds(objs)
    return (x0, x1, z0, z1)


def alf_container_sprite(kind, state):
    C.clear_objects()
    if kind == "tub":
        root, info = alf_tub(state)
        cw, ch = ALF_TUB_CW, ALF_TUB_CH
    else:
        root, info = alf_cooler(state)
        cw, ch = ALF_BOX_CW, ALF_BOX_CH
    cont = [o for item in info["contents"] for o in item]
    for o in cont:
        o.hide_render = True
    arr = alf_render(cw, ch, f"{kind}_{state}")
    # every shrimp / sardine gets its own pass + outline over the container, back to front (they rest on the water /
    # ice, and silver-on-ice or coral-on-coral needs the outline to read)
    for q, item in enumerate(info["contents"]):
        for o in bpy.context.scene.objects:                        # the rest still hides what is behind it (holdout)
            o.hide_render = o in info["see_through"]              # (the shrimp float on the water)
            o.is_holdout = o.type == "MESH" and o not in item
        top = alf_render(cw, ch, f"{kind}_{state}_c{q}")
        m = top[..., 3] > 0
        arr[m] = top[m]
    for o in bpy.context.scene.objects:
        o.is_holdout = False
    for pts, col in info["strokes"]:
        alf_stroke(arr, pts, col)
    return arr, info


def alf_container_anchors(kind):
    """Hit areas / pick-up points in sprite px from the centre (+y up), from the posed geometry."""
    cw, ch = (ALF_TUB_CW, ALF_TUB_CH) if kind == "tub" else (ALF_BOX_CW, ALF_BOX_CH)
    out = {}

    def rel(b):
        x0, x1, z0, z1 = b
        return (round(x0 - cw / 2.0, 1), round(x1 - cw / 2.0, 1), round(z0 - ch / 2.0, 1), round(z1 - ch / 2.0, 1))
    for state in ("closed", "open"):
        C.clear_objects()
        root, info = alf_tub(state) if kind == "tub" else alf_cooler(state)
        out[f"lid_rect_{state}"] = rel(alf_bounds(info["lid"]))
        if state == "closed":
            tab = info["tab"] if kind == "tub" else info["latch"]
            b = alf_bounds([tab])
            out["lid_tab"] = (round((b[0] + b[1]) / 2 - cw / 2.0, 1), round((b[2] + b[3]) / 2 - ch / 2.0, 1))
        else:
            if kind == "tub":
                D = ALF_TUB_DIM
                r = D["r0"] + (D["r1"] - D["r0"]) * D["water"] / D["h"] - 0.7
                pts = [alf_world(root, (r * math.sin(a), -r * math.cos(a), D["water"])) for a in
                       np.linspace(0, 2 * math.pi, 33)]
                c = alf_world(root, (0.0, 0.0, D["water"] + 0.5))
            else:
                D = ALF_BOX_DIM
                xi, yi = D["w"] - D["wall"], D["d"] - D["wall"]
                pts = [alf_world(root, (sx * xi, sy * yi, D["h"])) for sx in (-1, 1) for sy in (-1, 1)]
                c = alf_world(root, (0.0, 1.0, D["h"]))
            xs, zs = [p[0] for p in pts], [p[1] for p in pts]
            out["opening_rect"] = rel((min(xs), max(xs), min(zs), max(zs)))
            out["pick_point"] = (round(c[0] - cw / 2.0, 1), round(c[1] - ch / 2.0, 1))
    C.clear_objects()
    out["base"] = (0.0, -ch / 2.0)
    return out


ALF_SHRIMP_POSES = {
    # name: (canvas, body centre on the canvas, in-plane deg (+ = head down / clockwise), curl, legs, fan, meaning)
    "feed_shrimp_held_f0": ((16, 24), None, -86.0, 0.05, 0, 0.0, "held (pinched behind the head), tail hanging"),
    "feed_shrimp_held_f1": ((16, 24), None, -96.0, 0.45, 1, 0.3, "held: tail flicked in (dangle swing)"),
    "feed_shrimp_fall_f0": ((16, 16), (8.0, 8.0), 35.0, 0.42, 0, 0.3, "falling: curled, head down"),
    "feed_shrimp_fall_f1": ((16, 16), (8.0, 8.0), 70.0, 0.25, 1, 0.0, "falling: tumbling, uncurling"),
    "feed_shrimp_sink_f0": ((18, 14), (9.0, 7.0), 12.0, 0.15, 0, 0.0, "sinking: legs back"),
    "feed_shrimp_sink_f1": ((18, 14), (9.0, 7.0), 14.0, 0.20, 1, 0.25, "sinking: legs forward, tail fan flares"),
    "feed_shrimp_rest": ((18, 10), (9.0, 4.8), 6.0, 0.20, 0, 0.0, "lying on the bottom"),
}
ALF_SHRIMP_GRIP = (2.0, 1.8)                      # shrimp-local point the finger pinches (on the back, behind the head)
ALF_SARDINE_POSES = {
    "feed_sardine_held_f0": ((12, 28), None, 90.0, 0.0, "held by the tail, hanging head down"),
    "feed_sardine_held_f1": ((12, 28), None, 97.0, 0.9, "held by the tail: swung, body curved (dangle)"),
    "feed_sardine_fall_f0": ((20, 24), (10.0, 12.0), 55.0, 0.0, "falling nose-first"),
    "feed_sardine_fall_f1": ((24, 18), (12.0, 9.0), 32.0, -0.6, "falling, levelling out"),
    "feed_sardine_sink_f0": ((26, 14), (13.0, 7.0), 10.0, 0.0, "sinking, head a little down"),
    "feed_sardine_sink_f1": ((26, 14), (13.0, 7.0), 7.0, 0.6, "sinking, body flexed (wobble)"),
    "feed_sardine_rest": ((26, 12), (13.0, 5.4), 4.0, 0.3, "lying on the bottom"),
}
ALF_SARDINE_GRIP = (-6.2, 0.0)                    # sardine-local point held by the finger (the tail's wrist)


def alf_rot_y(deg, p):
    """In-plane rotation of local (x, z) by deg clockwise on screen (Blender +Y rotation)."""
    t = math.radians(deg)
    x, z = p
    return (x * math.cos(t) + z * math.sin(t), -x * math.sin(t) + z * math.cos(t))


def alf_creature_sprite(kind, name):
    """One shrimp / sardine sprite -> (RGBA bottom-up, meaning, None or (grip, body centre) for the held frames)."""
    C.clear_objects()
    if kind == "shrimp":
        (cw, ch), centre, deg, curl, legs, fan, meaning = ALF_SHRIMP_POSES[name]
        scale, grip_l, mid = 0.9, ALF_SHRIMP_GRIP, (-0.8, -0.3)
    else:
        (cw, ch), centre, deg, bend, meaning = ALF_SARDINE_POSES[name]
        scale, grip_l, mid = 1.0, ALF_SARDINE_GRIP, (0.0, 0.0)
    grip = None
    if centre is None:                            # held: the grip point on a pixel corner near the top centre
        gx, gz = alf_rot_y(deg, (grip_l[0] * scale, grip_l[1] * scale))
        grip = (cw / 2.0, ch - (8.0 if kind == "shrimp" else 6.0))
        loc = (grip[0] - gx, 0.0, grip[1] - gz)
    else:
        mx, mz = alf_rot_y(deg, (mid[0] * scale, mid[1] * scale))
        loc = (centre[0] - mx, 0.0, centre[1] - mz)
    sub = alf_empty("Pose", loc=loc, rot=(0.0, math.radians(deg), 0.0), scale=scale)
    bpy.context.view_layer.update()
    if kind == "shrimp":
        strokes = alf_shrimp(sub, curl=curl, legs=legs, fan=fan)
    else:
        strokes = alf_sardine(sub, bend=bend)
    arr = alf_render(cw, ch, name, strokes)
    g = None
    if grip is not None:                          # (grip, body centre) in sprite px from the centre
        mx, mz = alf_rot_y(deg, (mid[0] * scale, mid[1] * scale))
        g = ((grip[0] - cw / 2.0, grip[1] - ch / 2.0),
             (round(loc[0] + mx - cw / 2.0, 1), round(loc[2] + mz - ch / 2.0, 1)))
    return arr, meaning, g


def alf_glint(arr, cx, cz, big=True):
    """A white 4-point sparkle stamped onto a sprite (rows bottom-up)."""
    W, G = "#ffffff", "#e8f6ff"
    alf_dot(arr, cx, cz, W)
    for d in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        alf_dot(arr, cx + d[0], cz + d[1], W if big else G)
        if big:
            alf_dot(arr, cx + 2 * d[0], cz + 2 * d[1], G, 0.75)


def alf_bite_sprites():
    """feed_shrimp_bite_f0..f2 10x10: a fish bites the shrimp - coral shell bits + a leg fleck scatter from the mouth."""
    S, D_, W = ALF_SHRIMP["body"], ALF_SHRIMP["fan"], "#fff2e8"
    frames = [
        [([(4, 5), (5, 5)], S, 1), ([(5, 6), (4, 4)], D_, 1), ([(6, 5)], W, 1), ([(3, 6)], S, 0.75)],
        [([(2, 6), (7, 6)], S, 1), ([(6, 3), (3, 3)], D_, 1), ([(5, 7)], W, 1), ([(8, 4), (1, 4)], S, 0.75),
         ([(4, 5)], W, 0.5)],
        [([(1, 7), (8, 7)], S, 0.5), ([(7, 2), (2, 2)], D_, 0.5), ([(5, 8), (0, 4), (9, 5)], W, 0.25)],
    ]
    names = ["bite: bits at the mouth", "shell bits scatter", "bits fade"]
    return {f"feed_shrimp_bite_f{k}": (afd_px(10, 10, p, f"alf_bite{k}"), names[k]) for k, p in enumerate(frames)}


# ---------------------------------------------------------------------------- splashes (flat pixel cells)
def alf_splash(w, h, m, frames_spec, tag):
    """Splash frames on a w x h canvas whose row m (bottom-up, just below the centre) lies ON the meniscus row. Each
    frame spec: dict(dent=half width, crown=(half width, height), jet=height, drops=[(x, z, colour key, a)],
    foam=[(x0, x1, row offset below m, key, a)], bub=[(x, z, a)], ring=[(half, key, a)])."""
    X = ALF_FX
    cx = w / 2.0
    out = []
    for f, sp in enumerate(frames_spec):
        parts = []
        if sp.get("dent"):
            dw = sp["dent"]
            parts.append(([(int(cx - dw + i), m) for i in range(2 * dw)], X["T"], 1))
        if sp.get("crown"):
            cwd, chh = sp["crown"]
            for k in range(chh):
                for s in (-1, 1):
                    x = cx - 0.5 + s * (cwd + k * 0.7 + 0.5)
                    parts.append(([(int(math.floor(x)), m + 1 + k)], X["W"] if k == 0 else X["L"], 1))
        if sp.get("jet"):
            jh = sp["jet"]
            parts.append(([(int(cx) - 1, m + 1 + k) for k in range(jh)] + [(int(cx), m + 1 + k) for k in range(jh - 1)],
                          X["L"], 1))
            parts.append(([(int(cx) - 1, m + jh), (int(cx), m + jh - 1)], X["W"], 1))
            parts.append(([(int(cx) - 1, m + jh + 2)], X["T"], 1))
        for x0, x1, below, key, a in sp.get("foam", []):
            parts.append(([(x, m - below) for x in range(int(x0), int(x1) + 1)], X[key], a))
        for half, key, a in sp.get("ring", []):
            parts.append(([(int(cx - half), m), (int(cx + half - 1), m), (int(cx - half - 1), m), (int(cx + half), m)],
                          X[key], a))
        for x, z, key, a in sp.get("drops", []):
            parts.append(([(int(x), int(z))], X[key], a))
        for x, z, a in sp.get("bub", []):
            parts.append(([(int(x), int(z))], X["B"], a))
        out.append(afd_px(w, h, parts, f"alf_{tag}{f}"))
    return out


def alf_drops(n, seed, x_spread, vz, t, g, cx, m, w, h, key="L"):
    """Deterministic drop toss from the impact point: [(x, z, key, a)] at time t (drops under the surface dropped)."""
    rng = random.Random(seed)
    res = []
    for q in range(n):
        vx = rng.uniform(-x_spread, x_spread)
        vz_ = rng.uniform(vz * 0.55, vz)
        x = cx + vx * t
        z = m + 1 + vz_ * t - g * t * t
        if z >= m + 1 and 0 <= x < w and z < h:
            res.append((x, z, "W" if q % 4 == 0 else key, 1.0))
    return res


def alf_splash_sets():
    """Shrimp (small) and sardine (large) drop splashes + the big fish's gulp."""
    out = {}
    # small: 16x14, meniscus row 6
    w, h, m = 16, 14, 6
    small = [dict(dent=2, crown=(2, 2), foam=[(5, 10, 1, "W", 1)], bub=[(8, 3, 0.75)]),
             dict(jet=4, crown=(3, 2), drops=alf_drops(6, 3, 2.2, 3.4, 1.0, 0.5, 8, m, w, h),
                  foam=[(3, 5, 1, "W", 1), (10, 12, 1, "W", 1), (7, 8, 2, "F", 0.5)],
                  bub=[(7, 2, 0.75), (9, 3, 0.75)]),
             dict(drops=alf_drops(6, 3, 2.2, 3.4, 2.0, 0.5, 8, m, w, h) + [(7, m + 2, "L", 1), (8, m + 1, "T", 0.75)],
                  foam=[(1, 3, 1, "F", 0.75), (12, 14, 1, "F", 0.75)], ring=[(5, "W", 0.5)], bub=[(8, 2, 0.5)]),
             dict(foam=[(0, 2, 1, "F", 0.5), (13, 15, 1, "F", 0.5)],
                  drops=[(3, m + 1, "W", 0.5), (12, m + 1, "W", 0.5)])]
    names = ["impact: dent + crown, foam under", "rebound jet, drops up", "drops fall, ripple spreads", "fading ripple"]
    for k, a in enumerate(alf_splash(w, h, m, small, "drop_s")):
        out[f"feed_drop_s_f{k}"] = (a, "shrimp splash: " + names[k])
    # large: 24x18, meniscus row 8
    w, h, m = 24, 18, 8
    large = [dict(dent=3, crown=(3, 3), foam=[(7, 16, 1, "W", 1), (9, 14, 2, "F", 0.75)],
                  bub=[(11, 4, 0.75), (13, 5, 0.75)]),
             dict(jet=6, crown=(4, 3), drops=alf_drops(10, 7, 3.0, 5.0, 1.0, 0.6, 12, m, w, h),
                  foam=[(4, 8, 1, "W", 1), (15, 19, 1, "W", 1), (9, 14, 2, "F", 0.75)],
                  bub=[(10, 3, 0.75), (13, 4, 0.75), (12, 2, 0.5)]),
             dict(jet=3, drops=alf_drops(10, 7, 3.0, 5.0, 2.0, 0.6, 12, m, w, h),
                  foam=[(2, 5, 1, "W", 0.75), (18, 21, 1, "W", 0.75), (10, 13, 1, "F", 0.75)], ring=[(8, "W", 0.5)],
                  bub=[(11, 3, 0.5), (13, 2, 0.5)]),
             dict(drops=alf_drops(10, 7, 3.0, 5.0, 3.1, 0.6, 12, m, w, h),
                  foam=[(0, 3, 1, "F", 0.75), (20, 23, 1, "F", 0.75)], ring=[(10, "W", 0.5)], bub=[(12, 3, 0.25)]),
             dict(foam=[(0, 2, 1, "F", 0.5), (21, 23, 1, "F", 0.5)],
                  drops=[(5, m + 1, "W", 0.5), (18, m + 1, "W", 0.5)])]
    names = ["impact: wide dent + crown, foam", "jet + drops flung up", "jet collapses, drops at the top, ring",
             "drops fall, ripple spreads", "fading ripple"]
    for k, a in enumerate(alf_splash(w, h, m, large, "drop_l")):
        out[f"feed_drop_l_f{k}"] = (a, "sardine splash: " + names[k])
    for k, a in enumerate(alf_gulp()):
        out[f"feed_gulp_f{k}"] = a
    return out


def alf_gulp():
    """feed_gulp_f0..f4 40x24, meniscus row 11: a big fish surging to the surface and gulping (the fish itself is the
    game's sprite, its mouth at the centre): the bulge, the burst (a white crown + flaring sheets + spray, a foam cloud
    under the surface), the spray's peak over a foam patch, the fall-back with bubbles, the settling foam. The spray
    rises above the tank's 6 px air gap: draw it over the front frame (it splashes up over the rim)."""
    X = ALF_FX
    w, h, m = 40, 24, 11
    cx = 20
    frames, names = [], []

    def row(x0, x1, z, key, a=1.0, step=1):
        return ([(x, z) for x in range(int(x0), int(x1) + 1, step)], X[key], a)

    def drops(t, n=18, seed=11):
        d = alf_drops(n, seed, 8.0, 12.0, t, 0.95, cx, m, w, h)
        return [([(int(x), int(z)) for x, z, k, a in d if k == "L"], X["L"], 1),
                ([(int(x), int(z)) for x, z, k, a in d if k == "W"], X["W"], 1)]

    def bubbles(pts, a):
        return ([(x, z) for x, z in pts], X["B"], a)
    # f0: the surface bulges up over the rising head (a low dome, white rim), foam under it
    dome, rim = [], []
    for x in range(12, 28):
        u = (x + 0.5 - cx) / 8.0
        top = m + int(round(3.0 * max(0.0, 1 - u * u)))
        dome += [(x, z) for z in range(m, top)]
        rim.append((x, top))
    frames.append([(dome, X["L"], 1), (rim, X["W"], 1), row(9, 30, m - 1, "F", 0.75), row(13, 26, m - 2, "W", 0.5, 2),
                   ([(9, m), (30, m), (8, m + 1), (31, m + 1)], X["W"], 1), bubbles([(15, 6), (24, 5)], 0.5)])
    names.append("surge: the surface bulges over the rising head")
    # f1: the burst - a white crown round the mouth, sheets flaring out, spray; a foam cloud under the surface
    crown = [(x, m) for x in range(cx - 7, cx + 7)] + [(x, m + 1) for x in range(cx - 6, cx + 6)] \
        + [(x, m + 2) for x in range(cx - 5, cx + 5) if x % 3 != 0] \
        + [(cx - 3, m + 3), (cx + 2, m + 3), (cx - 1, m + 4)]
    sheet_w, sheet_l = [], []
    for k in range(8):
        for s in (-1, 1):
            x = cx - 0.5 + s * (6.5 + k * 0.85)
            sheet_w.append((int(math.floor(x)), m + 2 + k))
            if k < 5:
                sheet_l.append((int(math.floor(x - s)), m + 2 + k))
    frames.append([(sheet_l, X["L"], 1), (sheet_w, X["W"], 1), (crown, X["W"], 1)] + drops(0.55, 14, 9)
                  + [row(cx - 13, cx + 12, m - 1, "W"), row(cx - 11, cx + 10, m - 2, "F"),
                     row(cx - 9, cx + 8, m - 3, "F", 0.75, 2),
                     bubbles([(12, 6), (27, 5), (17, 4), (23, 7), (15, 2), (25, 3), (20, 5)], 0.75)])
    names.append("gulp: the burst - white crown, flaring sheets, spray; a foam cloud under the surface")
    # f2: the spray at its peak; the crown collapses into a foam patch; the cloud spreads, bubbles swirl
    frames.append(drops(1.25) + [([(cx - 12, m + 8), (cx + 11, m + 9), (cx - 13, m + 6), (cx + 12, m + 7)],
                                  X["W"], 1),
                                 row(cx - 10, cx + 9, m, "W"), row(cx - 7, cx + 6, m + 1, "F", 0.75, 2),
                                 row(cx - 16, cx + 15, m - 1, "F"), row(cx - 12, cx + 11, m - 2, "W", 0.75, 2),
                                 row(cx - 8, cx + 7, m - 3, "F", 0.5, 3),
                                 bubbles([(13, 7), (26, 6), (17, 3), (22, 8), (20, 2), (11, 9), (29, 4)], 0.75)])
    names.append("spray at its peak, foam patch on the surface, bubbles")
    # f3: drops fall back, the foam thins, ripple rings run out, bubbles rise
    frames.append(drops(2.1) + [row(cx - 8, cx + 7, m, "W", 0.75, 2), row(cx - 10, cx + 9, m - 1, "F", 0.5),
                                ([(2, m), (3, m), (36, m), (37, m)], X["W"], 0.75), row(0, 5, m - 1, "F", 0.5),
                                row(34, 39, m - 1, "F", 0.5),
                                bubbles([(14, 8), (25, 7), (18, 6), (21, 9), (16, 4)], 0.5)])
    names.append("drops fall back, foam thins, ripples run out, bubbles rise")
    # f4: settling foam specks + faint far ripples
    frames.append([row(cx - 6, cx + 5, m, "F", 0.5, 3), ([(0, m), (1, m), (38, m), (39, m)], X["W"], 0.5),
                   row(cx - 8, cx + 7, m - 1, "F", 0.25, 2), bubbles([(16, 9), (23, 10)], 0.25)])
    names.append("settling: foam specks, faint ripples")
    return [(afd_px(w, h, p, f"alf_gulp{k}"), "big-fish gulp: " + names[k]) for k, p in enumerate(frames)]


def alf_shadow(w):
    S = "#1a0e14"
    parts = [([(i, 3) for i in range(2, w - 2)], S, 0.25), ([(i, 2) for i in range(1, w - 1)], S, 0.5),
             ([(i, 1) for i in range(3, w - 3)], S, 0.5), ([(i, 0) for i in range(6, w - 6)], S, 0.25)]
    return afd_px(w, 4, parts, f"alf_shadow{w}")


# ---------------------------------------------------------------------------- icons
def alf_diet_icon(name):
    """UI diet icons 16x16 (정보창의 좋아하는 먹이): pellets / a shrimp / a sardine."""
    C.clear_objects()
    strokes = []
    if name == "icon_diet_pellet":
        mat = M("AlfPel", "#9a6232", shine=0.8)
        dark = M("AlfPelD", "#7a4a24", shine=0.6)
        for q, (x, z, r, mm) in enumerate(((4.4, 4.4, 2.6, mat), (11.6, 4.0, 2.4, dark), (8.2, 10.9, 2.7, mat))):
            sphere(f"P{q}", mm, r, (x, -2.0 - q * 0.3, z), scale=(1.0, 1.0, 0.88), seg=14, rings=8)
    elif name == "icon_diet_shrimp":
        sub = alf_empty("Pose", loc=(7.0, 0.0, 7.4), rot=(0.0, math.radians(-4.0), 0.0), scale=0.9)
        bpy.context.view_layer.update()
        strokes = [(p[:3], c) for p, c in alf_shrimp(sub, curl=0.5, legs=0, fan=0.2) if c != ALF_SHRIMP["leg"]]
    else:
        sub = alf_empty("Pose", loc=(8.2, 0.0, 7.8), rot=(0.0, math.radians(-32.0), 0.0), scale=0.66)
        bpy.context.view_layer.update()
        alf_sardine(sub)
    return alf_render(16, 16, name, strokes)


def alf_shop_icon(kind):
    """Items/feed_shrimp.png, feed_sardine.png 32x32: the closed container + one shrimp / sardine at its foot."""
    C.clear_objects()
    t = math.radians(ALF_TILT)
    strokes = []
    if kind == "shrimp":
        root = alf_empty("Root", loc=(12.0, 0.0, 1.0 + ALF_TUB_DIM["r0"] * math.sin(t) * 1.15), rot=(t, 0.0, 0.0),
                         scale=1.15)
        alf_tub("closed", 32, 32, root=root)
        sub = alf_empty("Pose", loc=(21.6, -14.0, 5.2), rot=(0.0, math.radians(-14.0), 0.0), scale=0.85)
        bpy.context.view_layer.update()
        strokes = [(p[:3], c) for p, c in alf_shrimp(sub, curl=0.4, legs=0, fan=0.2)]
    else:
        root = alf_empty("Root", loc=(15.0, 0.0, 8.0 + ALF_BOX_DIM["d"] * math.sin(t) * 0.9), rot=(t, 0.0, 0.0),
                         scale=0.9)
        alf_cooler("closed", 32, 32, root=root)
        sub = alf_empty("Pose", loc=(17.0, -14.0, 5.0), rot=(0.0, math.radians(-16.0), 0.0), scale=0.76)
        bpy.context.view_layer.update()
        alf_sardine(sub)
    return alf_render(32, 32, f"shop_{kind}", strokes)


# ---------------------------------------------------------------------------- build + review sheet + mock
def aqualive_art(dry=False):
    """Every sprite of the group -> Sprites/World|UI|Items (dry: _tmp/aqualive/world|ui|items), the review sheet and
    the in-scene mock; returns {name: (path, meaning, w, h)}."""
    world = os.path.join(ALF_OUT, "world") if dry else WORLD
    ui = os.path.join(ALF_OUT, "ui") if dry else UI
    items = os.path.join(ALF_OUT, "items") if dry else ITEMS
    os.makedirs(AFD_WORK, exist_ok=True)
    made, grips = {}, {}

    def put(arr, folder, name, meaning):
        p = os.path.join(folder, name + ".png")
        C.save_pixels(arr, p)
        made[name] = (p, meaning, arr.shape[1], arr.shape[0])

    tub_names = {"closed": "lid shut (tap the lid / its front tab)", "ajar": "lid popping open (transition)",
                 "open": "open: shrimp in the water (2+ left)", "few": "open: the last shrimp (1 left)",
                 "empty": "open: only water (none left)"}
    box_names = {"closed": "lid shut, latch at the front (tap the lid)", "ajar": "lid lifting (transition)",
                 "open": "open: sardines on ice (2+ left)", "few": "open: the last sardine on the ice (1 left)",
                 "empty": "open: only ice (none left)"}
    for state, meaning in tub_names.items():
        put(alf_container_sprite("tub", state)[0], world, f"feed_tub_{state}", meaning)
    for state, meaning in box_names.items():
        put(alf_container_sprite("box", state)[0], world, f"feed_cooler_{state}", meaning)
    put(alf_shadow(24), world, "feed_tub_shadow", "contact shadow under the tub (centre on the ledge line)")
    put(alf_shadow(36), world, "feed_cooler_shadow", "contact shadow under the cooler (centre on the ledge line)")
    for name in ALF_SHRIMP_POSES:
        arr, meaning, g = alf_creature_sprite("shrimp", name)
        put(arr, world, name, meaning)
        if g:
            grips[name] = g
    for name, (a, meaning) in alf_bite_sprites().items():
        put(a, world, name, meaning)
    for name in ALF_SARDINE_POSES:
        arr, meaning, g = alf_creature_sprite("sardine", name)
        put(arr, world, name, meaning)
        if g:
            grips[name] = g
    arr, _, _ = alf_creature_sprite("sardine", "feed_sardine_sink_f0")
    alf_glint(arr, 16, 7)
    put(arr, world, "feed_sardine_glint", "sinking + a silver glint on the flank (swap in now and then)")
    for name, (a, meaning) in alf_splash_sets().items():
        put(a, world, name, meaning)
    for name, meaning in (("icon_diet_pellet", "먹이: 사료 (pellets)"), ("icon_diet_shrimp", "먹이: 생새우"),
                          ("icon_diet_sardine", "먹이: 정어리")):
        put(alf_diet_icon(name), ui, name, meaning)
    put(alf_shop_icon("shrimp"), items, "feed_shrimp", "생새우 shop icon (the tub + a shrimp)")
    put(alf_shop_icon("sardine"), items, "feed_sardine", "정어리 shop icon (the cooler + a sardine)")
    for name, (p, meaning, w, h) in made.items():
        print(f"AQUALIVE {name:24s} {w:3d}x{h:<3d} {meaning}")
    for kind in ("tub", "box"):
        for key, v in alf_container_anchors(kind).items():
            print(f"AQUALIVE anchor {kind} {key:16s} {v}")
    for name, g in grips.items():
        print(f"AQUALIVE anchor {name:22s} grip {g[0]}  body centre {g[1]}")
    alf_sheet(made, os.path.join(ALF_OUT, "art_sheet.png"))
    alf_mock(made, os.path.join(ALF_OUT, "mock.png"))
    return made


def alf_sheet(made, out):
    """Review sheet (rows top-down): the tub and cooler states (6x) on the ledge colours; the shrimp (10x) and sardine
    (8x) sprites over the water, the bites; the splashes (8x / 6x) and the gulp (5x) over the surface; the diet icons
    (8x) on paper / dark, the shop icons (6x) and everything on the ledge at the game's 2x."""
    img = {k: afd_td(v[0]) for k, v in made.items()}
    WATER, GRAVEL, CAB, AIR = "#5692a4", "#ba9c76", "#855a44", "#d6d2ae"

    def cell(name, s, bg, extra=None, pad=0, flip=False):
        a = img[name]
        w, h = a.shape[1] * s + 2 * pad, a.shape[0] * s + 2 * pad

        def draw(sh, x, y):
            afd_fill(sh, x, y, w, h, bg)
            if extra:
                extra(sh, x, y, w, h, s, pad)
            afd_paste(sh, a, x + pad, y + pad, s, None, flip)
        return (w, h, draw)

    def ledge(sh, x, y, w, h, s, pad):
        afd_fill(sh, x, y + h - pad - 1 * s, w, 1 * s + pad, CAB)

    def surface(m_below_centre):
        def f(sh, x, y, w, h, s, pad):
            # rows above the meniscus = the lamp-lit air, the meniscus row = the light band, below = water
            hh = (h - 2 * pad) // s
            mrow_top = pad + (hh - m_below_centre - 1) * s          # image y of the meniscus row
            afd_fill(sh, x, y, w, mrow_top, AIR)
            afd_fill(sh, x, y + mrow_top, w, s, "#dbf1d9")
        return f

    def bottom(sh, x, y, w, h, s, pad):
        afd_fill(sh, x, y + h - pad, w, pad, GRAVEL)

    STATES = ["closed", "ajar", "open", "few", "empty"]
    rows = []
    rows.append([cell(f"feed_tub_{n}", 6, GRAVEL, ledge, pad=6) for n in STATES])
    rows.append([cell(f"feed_cooler_{n}", 6, GRAVEL, ledge, pad=6) for n in STATES])
    rows.append([cell(f"feed_shrimp_{n}", 10, WATER, pad=10) for n in ["held_f0", "held_f1", "fall_f0", "fall_f1",
                                                                          "sink_f0", "sink_f1"]]
                + [cell("feed_shrimp_rest", 10, WATER, bottom, pad=10)]
                + [cell(f"feed_shrimp_bite_f{k}", 10, WATER, pad=10) for k in range(3)])
    rows.append([cell(f"feed_sardine_{n}", 8, WATER, pad=8) for n in ["held_f0", "held_f1", "fall_f0", "fall_f1",
                                                                         "sink_f0", "sink_f1", "glint"]]
                + [cell("feed_sardine_rest", 8, WATER, bottom, pad=8)])
    rows.append([cell(f"feed_drop_s_f{k}", 8, WATER, surface(6)) for k in range(4)]
                + [cell(f"feed_drop_l_f{k}", 6, WATER, surface(8)) for k in range(5)])
    rows.append([cell(f"feed_gulp_f{k}", 5, WATER, surface(11)) for k in range(5)])
    rows.append([cell(n, 8, bg, pad=16) for n in ["icon_diet_pellet", "icon_diet_shrimp", "icon_diet_sardine"]
                 for bg in ("#e8d8b0", "#243448")]
                + [cell(f"feed_{k}", 6, "#3a4a5e", pad=12) for k in ("shrimp", "sardine")])
    rows.append([cell(n, 2, GRAVEL, ledge, pad=4) for n in
                 [f"feed_tub_{s}" for s in STATES] + [f"feed_cooler_{s}" for s in STATES]]
                + [cell(n, 2, WATER, pad=4) for n in ["feed_shrimp_held_f0", "feed_shrimp_sink_f0",
                                                      "feed_sardine_held_f0", "feed_sardine_sink_f0",
                                                      "feed_sardine_glint"]]
                + [cell(n, 6, CAB, pad=6) for n in ["feed_tub_shadow", "feed_cooler_shadow"]])
    GAP = 10
    SW = max(sum(c[0] for c in r) + GAP * (len(r) + 1) for r in rows)
    SH = sum(max(c[1] for c in r) for r in rows) + GAP * (len(rows) + 1)
    sheet = np.zeros((SH, SW, 4), np.float32)
    sheet[..., 3] = 1.0
    sheet[..., :3] = afd_rgb("#1e2836")
    y = GAP
    for r in rows:
        x = GAP
        for w, h, draw in r:
            draw(sheet, x, y)
            x += w + GAP
        y += max(c[1] for c in r) + GAP
    afd_save(sheet, out)
    print("AQUALIVE sheet ->", out, SW, "x", SH)


def alf_mock(made, out):
    """The game view (the 480x270 crop of the 640x400 aquarium) at 2x, frames stacked: A) idle - the two feed bags, the
    tub and the cooler shut on the ledge; B) a sardine held over the tank (the cooler open with one left, the tub
    open), the arapaima below it, a shrimp sinking with a bass coming for it; C) the sardine gulped: the arapaima at the
    surface in the gulp burst (drawn over the front frame), the drop's ripple fading beside it, the bass biting the
    shrimp. Ledge slots (image columns): bags 160 / 196 (as AquaFeed.SlotX), tub 232, cooler 272."""
    img = {k: afd_td(v[0]) for k, v in made.items()}
    back = afd_td(os.path.join(C.SPRITES, "Stages", "aquarium_back.png"))
    front = afd_td(os.path.join(C.SPRITES, "Stages", "aquarium_front.png"))

    def spr(folder, name):
        p = os.path.join(C.SPRITES, folder, name + ".png")
        return afd_td(p)

    def at(dst, a, cx, cy, flip=False, tint=None):
        afd_paste(dst, a, int(round(cx - a.shape[1] / 2.0)), int(round(cy - a.shape[0] / 2.0)), 1, tint, flip)

    LEDGE, SURF = 284, 117                                         # the bags' base row; the meniscus row
    SLOTS = dict(basic=160, premium=196, tub=232, cooler=272)
    frames = []
    for mode in ("idle", "held", "gulp"):
        v = back.copy()
        fishes = {"idle": [("arapaima", 300, 205, False), ("largemouth_bass", 430, 160, True),
                           ("golden_carp", 200, 175, False)],
                  "held": [("arapaima", 372, 168, False), ("largemouth_bass", 238, 196, False),
                           ("golden_carp", 450, 222, True)],
                  "gulp": [("arapaima", 364, 127, False), ("largemouth_bass", 262, 204, False),
                           ("golden_carp", 452, 226, True)]}[mode]
        for name, cx, cy, flip in fishes:
            at(v, spr("Fish", name + "_0"), cx, cy, flip)
        if mode == "held":
            at(v, img["feed_shrimp_sink_f0"], 268, 196)
            at(v, img["feed_drop_s_f3"], 270, SURF)
        if mode == "gulp":
            at(v, img["feed_shrimp_bite_f1"], 282, 202)
            at(v, img["feed_drop_l_f4"], 424, SURF)
        afd_paste(v, front, 0, 0)
        if mode == "gulp":                                           # over the front frame: the spray tops the rim
            at(v, img["feed_gulp_f1"], 397, SURF)                   # splash centre = the top edge of the meniscus row
        cy = LEDGE - 18
        for k in ("basic", "premium"):
            at(v, spr("World", "feed_shadow"), SLOTS[k], LEDGE)
        at(v, spr("World", "feed_basic_open3"), SLOTS["basic"], cy)
        at(v, spr("World", "feed_premium_sealed"), SLOTS["premium"], cy)
        at(v, img["feed_tub_shadow"], SLOTS["tub"], LEDGE)
        at(v, img["feed_cooler_shadow"], SLOTS["cooler"], LEDGE)
        tub = "closed" if mode == "idle" else "open"
        box = "closed" if mode == "idle" else "few"
        at(v, img[f"feed_tub_{tub}"], SLOTS["tub"], LEDGE - ALF_TUB_CH / 2.0)
        at(v, img[f"feed_cooler_{box}"], SLOTS["cooler"], LEDGE - ALF_BOX_CH / 2.0)
        if mode == "held":
            at(v, img["feed_sardine_held_f1"], 392, 100)
        frames.append(v[65:65 + 270, 80:80 + 480])
    out_img = np.zeros((270 * 2 * len(frames) + 16 * (len(frames) - 1), 480 * 2, 4), np.float32)
    out_img[..., 3] = 1.0
    out_img[..., :3] = afd_rgb("#1e2836")
    for q, f in enumerate(frames):
        afd_paste(out_img, f, 0, q * (540 + 16), 2)
    afd_save(out_img, out)
    print("AQUALIVE mock ->", out)


# ============================================================================ main
def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    groups = argv or ["rods", "reels", "lines", "baits", "tanks", "ui", "frames"]
    C.reset_scene()
    made = []
    if "rods" in groups:
        for rid, p in RODS.items():
            C.clear_objects()
            build_rod(p)
            render_icon(os.path.join(ITEMS, rid + ".png"))
            made.append(os.path.join(ITEMS, rid + ".png"))
    if "reels" in groups:
        for rid, p in REELS.items():
            C.clear_objects()
            build_reel(p)
            render_icon(os.path.join(ITEMS, rid + ".png"))
            made.append(os.path.join(ITEMS, rid + ".png"))
    if "lines" in groups:
        for lid, p in LINES.items():
            C.clear_objects()
            build_line(p)
            render_icon(os.path.join(ITEMS, lid + ".png"))
            made.append(os.path.join(ITEMS, lid + ".png"))
    if "baits" in groups:
        for bid in BAITS:
            made.append(render_bait(bid))   # icon + small in-world version (hook + bait) ~10px
    if "lures" in groups:
        # only the spec-v1 lures and their action chips (existing bait icons are left untouched) + review sheet
        for bid in LURES_NEW:
            render_bait(bid)
        for name in ACT_CHIPS:
            C.clear_objects()
            build_ui_icon(name)
            render_icon(os.path.join(UI, name + ".png"), size=UI_ICONS[name], margin=0)
        lure_sheet(os.path.join(LURE_PREVIEW, "lure_sheet.png"))
    if "enc" in groups:
        # the legend encounter's HUD icons only (mood / verb / eye) + their zoom sheet
        enc_made = []
        for name, (_, _, size) in ENC_ICONS.items():
            C.clear_objects()
            build_enc_icon(name)
            render_icon(os.path.join(UI, name + ".png"), size=size, margin=0)
            enc_made.append(os.path.join(UI, name + ".png"))
        C.contact_sheet(enc_made, os.path.join(C.TMP, "enc_icons_zoom.png"), scale=8, cols=len(enc_made))
    if "tanks" in groups:
        for lv in range(4):
            C.clear_objects()
            build_tank(lv)
            render_icon(os.path.join(ITEMS, f"tank_{lv + 1}.png"))
            made.append(os.path.join(ITEMS, f"tank_{lv + 1}.png"))
    if "ui" in groups:
        for name, size in UI_ICONS.items():
            C.clear_objects()
            build_ui_icon(name)
            render_icon(os.path.join(UI, name + ".png"), size=size, margin=0 if size <= 16 else 1)
            made.append(os.path.join(UI, name + ".png"))
        C.clear_objects()
        build_ui_icon("coin")
        render_icon(os.path.join(UI, "coin_big.png"), size=32)
    if "world" in groups:
        # floats (찌) seen from behind: a slim freshwater stick float and a round sea bobber
        C.clear_objects()
        C.tube_along("Body", [(0, 0, -0.9), (0, 0, -0.2), (0, 0, 0.5), (0, 0, 1.1)], [0.02, 0.16, 0.13, 0.03],
                     C.pattern_material("Float", "#f4f0e8", "#e8402a", kind="stripes", scale=1.0, thresh=0.5, axis=2, shine=0.8), 10)
        C.tube_along("Top", [(0, 0, 1.1), (0, 0, 1.7)], 0.03, M("Tip", "#ff6a2a"), 6)
        sphere("Tip", C.glow_material("TipG", "#ffd24a", 1.2), 0.06, (0, 0, 1.72), seg=8, rings=5)
        C.ortho_camera(0, 0.4, 7, 22, 8.0)
        C.render_sprite(os.path.join(WORLD, "float_stick.png"))
        C.clear_objects()
        sphere("Ball", C.pattern_material("Ball", "#f4f4f0", "#e03a2a", kind="stripes", scale=1.0, thresh=0.5, axis=2, shine=1.0),
               0.5, (0, 0, 0))
        C.tube_along("Top", [(0, 0, 0.45), (0, 0, 0.9)], 0.05, M("Tip", "#2a2a2a"), 6)
        C.ortho_camera(0, 0.2, 12, 16, 11.0)
        C.render_sprite(os.path.join(WORLD, "float_ball.png"))
        made += [os.path.join(WORLD, "float_stick.png"), os.path.join(WORLD, "float_ball.png")]
    if "worldreels" in groups:
        # the reel under the angler's rod, two handle frames per reel (own zoom sheet, not in items_sheet)
        for p in build_world_reels():
            print("world reel ->", p)
    if "frames" in groups:
        borders = build_frames()
        build_reel_widget()
        C.write_json("ui_borders.json", borders)
        for n in borders:
            made.append(os.path.join(UI, n + ".png"))
    if "reelarrow" in groups or "frames" in groups:
        for p in build_reel_arrow():
            print("reel arrow ->", p)
    if "castarrow" in groups:
        paths, info = build_cast_arrow()
        for p in paths:
            print("cast arrow ->", p)
        cast_arrow_preview(paths, info)
    if "sidearrow" in groups:
        paths, info = build_side_arrow()
        for p in paths:
            print("side arrow ->", p)
        side_arrow_preview(paths)
    if "obstacles" in groups:
        # the obstacles spec's UI / FX art (own section above), "dry" = into _tmp/obstacles instead of Assets
        obstacles_art(dry="dry" in groups)
    if "aquafeed" in groups:
        # the aquarium feeding art (own section above), "dry" = into _tmp/aquafeed instead of Assets
        aquafeed_art(dry="dry" in groups)
    if "aqualive" in groups:
        # the live food for the big fish (own section above), "dry" = into _tmp/aqualive instead of Assets
        aqualive_art(dry="dry" in groups)
    if made:
        C.contact_sheet(made, os.path.join(C.TMP, "items_sheet.png"), scale=4, cols=8)
    print("ITEMS done", len(made))


if __name__ == "__main__":
    main()
