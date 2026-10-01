"""
obstacles/sea.py - the SEA's obstacles (Docs/obstacles_spec.md 3.4). Owner: B2.

Everything hyb_sea.py paints in the water is in its FRONT layer (the back layer's mole, lighthouse and coast are
hundreds of metres out):
  tet (kind)   the 60 tetrapods of the two mounds (Leg x4 + Hub, grp tet<k>): the 640 layout's 34 (tet0..tet33) and the
               overscan's 26 (tet34..tet59, hyb_sea._TLX / _TRX: the ridges running on into the 800 px canvas's lower
               corners, beyond the game's home view). The 34 get above-water colliders, an underwater snag skirt (their
               legs go on under the surface) and a tetrapod cover each (the classic 우럭 / 감성돔 hole); the overscan's 26
               are solids only (no skirt, no cover: a cover-seeking fish is never drawn out of the view and the cast fan).
               ONE convex prism stack per unit held the water between its splayed legs (collider IoU 0.54-0.77 on a
               third of them), so every unit is 4 solids (split_tetrapods): "tet<k>" = the hub + leg 0 (upright: the
               leg pointing up) carrying the unit's skirt (0.8 + 0.8 s m) and cover (1.2 + 0.8 s m: the spec's 0.8 /
               1.2 round the whole unit, s = its scale), and "tet<k>.l1".."l3" = the other legs; 4 tiers each.
               tet0 / tet17 (against the breakwater at z 0.4-0.6) and the overscan's tet34 / tet47 (its nearest units,
               z 0.7-0.9, no cover zone to reach further) never touch the fishable water (zNear 3.6): the exporter drops
               them (56 exported).
  buoy (kind)  the red lateral buoy (17, 44): Float, Body, Band, Mark, Lamp = one solid (3 tiers), no cover
  helpers      the underwater tetrapod field along each mound's toe: one snag concrete band per side (the plan hull of
               the mound's underwater leg tips and hubs, grown 0.6 m, kept off the breakwater head |x| < 2.6), top -1.0,
               abrasive: tetfieldL / tetfieldR from the 34 (as before the overscan), tetfieldLX / tetfieldRX from the
               overscan's units
Not obstacles: Deck, Kerb, Bollard, Cooler, Bucket (his ground and his gear), the far mole / lighthouse (back layer).

The WATERLINE: hyb_sea.front_scene lays a holdout plane "Hold" at z = 0 (kind "hold") that hides every part of the
front props under the surface in the render. extra() reproduces it on the geometry: every visible mesh reaching under
z = 0 is cut at the waterline (bmesh bisect, the part below removed) and the Hold plane is hidden. So the export sees
exactly the painted (clipped) props: the tetrapod waterline footprints are the true cut sections, bot = 0, bed = false
(the buoy's float too), and the painted check rasterises what the render showed.
"""
import os
import importlib.util

STAGE = "sea"

_spec = importlib.util.spec_from_file_location("obst_lake_shared", os.path.join(os.path.dirname(__file__), "lake.py"))
LK = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(LK)                # shared helpers: _prism, _depth_at, bounds_check

# hyb_sea.main(): render_front(random.Random(31)), then render_back(random.Random(77)). Only the front holds anything
# in the water.
LAYERS = [("front", "render_front", 31)]

DECK_X = 2.6            # |x| of the breakwater head's sides (+ its kerb): the underwater field stays outside it
FIELD_GROW = 0.6        # m: the tetrapod field = the hull of the underwater legs / hubs of a mound grown by this
FIELD_TOP = -1.0
SKIRT, COVER = 0.8, 1.2  # m round a whole unit (spec 3.4)
LEG_REACH = 0.8         # x the unit's scale: how far the legs reach out beyond the hub + leg 0 footprint
HUB_TIERS, LEG_TIERS = 4, 4   # the hub + leg 0 collider; each other leg (they slope: more bands follow them better)

RULES = [
    dict(kind=["hold", "deck", "prop", "bucketw"], obst=None),
    dict(kind="tet", obst="solid", mat="concrete", tiers=3, skirt=0.8, skirt_top=-0.35, cover=1.2, cover_for="tet",
         tags="tet,abrasive"),
    dict(kind="buoy", obst="solid", mat="hull", tiers=3, tags="hull"),
]


def _stage():
    import sys
    return sys.modules["hyb_sea"]


def clip_at_waterline(scene):
    """The holdout plane of hyb_sea on the geometry: cut every visible mesh that dips under z = 0, drop the part below
    and hide the Hold plane. -> number of meshes cut."""
    import bmesh
    from mathutils import Matrix
    n = 0
    for ob in list(scene.objects):
        if ob.type != "MESH" or ob.hide_render:
            continue
        if ob.get("kind") == "hold":
            ob.hide_render = True
            continue
        M = ob.matrix_world
        if min((M @ v.co).z for v in ob.data.vertices) >= 0.0:
            continue
        if ob.data.users > 1:
            ob.data = ob.data.copy()
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0.0, 0.0, 0.0),
                               plane_no=(0.0, 0.0, 1.0), clear_inner=True)
        bm.to_mesh(ob.data)
        bm.free()
        ob.matrix_world = Matrix.Identity(4)
        n += 1
    return n


def _clip_half(poly, sx, x0):
    """Convex polygon [(x, y)] clipped to the half plane sx * x >= x0 (Sutherland-Hodgman, one edge)."""
    out = []
    for i in range(len(poly)):
        a, b = poly[i - 1], poly[i]
        ia, ib = sx * a[0] >= x0, sx * b[0] >= x0
        if ia != ib:
            t = (x0 - sx * a[0]) / (sx * (b[0] - a[0]))
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
        if ib:
            out.append(b)
    return out


_prism = LK._prism
_depth_at = LK._depth_at


def split_tetrapods(scene, smod):
    """One convex collider per tetrapod is a triangle-prism with the water between the legs inside it. Split every
    unit into 4 solids: "tet<k>" = the hub + leg 0 (upright units: the leg pointing up), "tet<k>.l1..l3" = the other
    legs (HUB_TIERS / LEG_TIERS tiers). The unit's skirt and cover stay on "tet<k>", grown to reach round the legs
    (skirt SKIRT + LEG_REACH * s, cover COVER + LEG_REACH * s; s = the unit's scale). -> number of units."""
    import re
    units = {}
    for ob in scene.objects:
        if ob.type == "MESH" and ob.get("kind") == "tet" and ob.get("obst") == "solid":
            units.setdefault(str(ob["grp"]), []).append(ob)
    from mathutils import Vector
    n0 = n_layout(smod)
    for grp, obs in units.items():
        k = int(re.match(r"tet(\d+)$", grp).group(1))
        tips = smod.TET_TIPS[k]
        x, y, z, s = smod.TETS[k]
        c = Vector((x, y, z))
        dirs = [(t - c).normalized() for t in tips]
        for ob in obs:
            if len(ob.data.vertices) == 0:              # wholly under the surface: cut away, nothing painted
                del ob["obst"]
                continue
            if base_is(ob, "Hub"):
                leg = 0
            else:                                        # which leg: the direction of its farthest point from the hub
                M = ob.matrix_world
                far = max((M @ v.co for v in ob.data.vertices), key=lambda w: (w - c).length)
                leg = max(range(4), key=lambda i: dirs[i].dot((far - c).normalized()))
            if k >= n0:
                # an overscan unit (beyond the game's home view): a solid only, no skirt and no cover (a cover-seeking
                # fish must not be drawn out of the view and the cast fan to it)
                ob["obst_id"] = grp if leg == 0 else "%s.l%d" % (grp, leg)
                ob["obst_tiers"] = HUB_TIERS if leg == 0 else LEG_TIERS
                for key in ("obst_skirt", "obst_skirt_top", "obst_cover", "obst_cover_for"):
                    if key in ob:
                        del ob[key]
                continue
            if leg == 0:
                ob["obst_id"] = grp
                ob["obst_tiers"] = HUB_TIERS
                ob["obst_skirt"] = SKIRT + LEG_REACH * s
                ob["obst_cover"] = COVER + LEG_REACH * s
            else:
                ob["obst_id"] = "%s.l%d" % (grp, leg)
                ob["obst_tiers"] = LEG_TIERS
                for key in ("obst_skirt", "obst_skirt_top", "obst_cover", "obst_cover_for"):
                    if key in ob:
                        del ob[key]
    return len(units)


def n_layout(smod):
    """The 640 layout's tetrapods (tet0 .. tet33); the ones after them (hyb_sea._TLX / _TRX) are the overscan's."""
    return len(smod._TL) + len(smod._TR)


def base_is(ob, name):
    import re
    return re.sub(r"\.\d{3}$", "", ob.name) == name


def extra(ctx):
    G = ctx["helper"].__globals__                     # hyb_obstacles' plan geometry (hull2, grow)
    cut = clip_at_waterline(ctx["scene"])
    smod = _stage()
    nu = split_tetrapods(ctx["scene"], smod)
    n0 = n_layout(smod)
    hs = []
    # one field per mound from the 640 layout's units (as before the overscan), and one per side from the overscan's
    for sx, name, ks in ((-1, "L", range(n0)), (1, "R", range(n0)),
                         (-1, "LX", range(n0, len(smod.TETS))), (1, "RX", range(n0, len(smod.TETS)))):
        pts = []
        for k in ks:
            (x, y, z, s), tips = smod.TETS[k], smod.TET_TIPS[k]
            if sx * x <= 0:
                continue
            pts.append((x, y))                                        # the hub (its body reaches the toe)
            pts += [(t.x, t.y) for t in tips if t.z < 0.0]            # the legs under the surface
        if len(pts) < 3:
            continue
        ring = [tuple(p) for p in G["grow"](G["hull2"](pts), FIELD_GROW, 16)]
        ring = _clip_half(ring, sx, DECK_X)
        hs.append(_prism(ctx, "snag", "tetfield" + name, ring, -3.0, FIELD_TOP, mat="concrete", tags="tet,abrasive",
                         top=FIELD_TOP, bot=-99.0))
    print("HYB OBST sea: %d meshes cut at the waterline (the Hold plane's job), Hold hidden; %d tetrapods split into "
          "hub + legs" % (cut, nu))
    return hs


def check(ctx):
    import numpy as np
    log, obst, L = ctx["log"], ctx["obstacles"], ctx["L"]
    tets = [e for e in obst if e["kind"] == "solid" and "tet" in e["tags"] and "." not in e["id"]]
    legs = [e for e in obst if e["kind"] == "solid" and "tet" in e["tags"] and "." in e["id"]]
    covers = [e for e in obst if e["kind"] == "cover" and e["parent"].startswith("tet")]
    standing = [e for e in tets + legs if e["bot"] <= 0.05]
    log("CHECK sea tetrapods: %d units of hyb_sea.TETS %d exported (hub + leg 0) + %d legs; %d skirts, %d covers "
        "(%d holdCross); %d solids stand in the water (bot 0), the rest rest on the mound (bot %.2f .. %.2f m); tops "
        "%.2f .. %.2f m"
        % (len(tets), len(_stage().TETS), len(legs),
           sum(1 for e in obst if e["kind"] == "snag" and e["parent"].startswith("tet")), len(covers),
           sum(1 for e in covers if e["holdCross"]), len(standing),
           min([e["bot"] for e in tets + legs if e["bot"] > 0.05] or [0]),
           max([e["bot"] for e in tets + legs] or [0]), min(e["top"] for e in tets), max(e["top"] for e in tets)))
    have = {e["id"] for e in tets}
    log("CHECK sea tetrapods not exported (never touch the fishable water): %s"
        % (" ".join("tet%d" % k for k in range(len(_stage().TETS)) if "tet%d" % k not in have) or "none"))
    for e in obst:
        if e["id"].startswith("tetfield"):
            p = np.array(e["pts"]).reshape(-1, 2)
            log("CHECK helper %-10s snag x %.2f .. %.2f, z %.2f .. %.2f, top %.2f (bed %.2f .. %.2f)"
                % (e["id"], p[:, 0].min(), p[:, 0].max(), p[:, 1].min(), p[:, 1].max(), e["top"],
                   -_depth_at(L, p[:, 1].min()), -_depth_at(L, p[:, 1].max())))
    LK.bounds_check(ctx, "sea")
