"""
obstacles/swamp.py - the SWAMP's obstacles (Docs/obstacles_spec.md 3.5). Owner: B1.

Everything hyb_swamp.py paints in the water is in its FRONT layer (the back layer's ghost rows stand 60-130 m out,
beyond zFar 58):
  cypress T0..T4   Trunk + Rib (kind trunk): the flared base with its buttress ribs up to 0.2 h = "T<k>" (solid wood,
                   to the bed, 4 tiers, the root skirt 0.8 m and a root cover 1.5 m), the shaft above it = "T<k>.trunk"
                   (4 tiers, up to its real top inside the crown: the spec's "top 3.8" left the painted trunk between
                   the pads without a collider). One prism stack over a trunk that flares from 2.6 r0 at the water to
                   1 r0 at 2 m was twice as wide as the painted shaft, so extra() cuts the painted meshes at 0.2 h
                   (nothing is added or removed: the pieces rasterise to the same pixels).
  crowns           Limb, Pad + Sprig (kind trunk): one leaf overhang per limb, "T<k>.crown<j>" (4 tiers), instead of one
                   hull over both sides of the tree (mostly air)
  moss             Drape, Moss (kind moss, grp T<k>m): the spec's own overhang "T<k>m.crown", one per clump of curtains
                   under a limb ("T<k>m.crown<j>[a-c]"; curtains hanging within MOSS_LINK of each other), 4 tiers
  knees            Knee (kind knee, 23): solid root, root + abrasive, 3 tiers (cones), skirt 0.3
  dead snag S0     Snag + SRib: "S0" base / "S0.trunk" shaft as the cypress; each Branch (+ its Twig) its own leaf
                   overhang "S0.b<j>", 2 tiers (bare branches: one hull over all of them would be air)
  Log              Log + LogMoss: solid wood, log + abrasive, capsule footprint, 2 tiers, skirt 0.4, log cover 1.2; the
                   broken branch Stub its own solid "log.stub" (with it the log's upper tier spanned its whole length);
                   the log FLOATS: its part under -0.08 m is cut off the obstacle (still painted) so bed = false
  Post             the two boardwalk-end posts in the water "postL" / "postR" (+-1.3, 0.8): solid wood, to the bed,
                   near; the six under the boardwalk (y < 0) are behind him. The lantern, its arm and brace: not
                   obstacles.
  pad (kind)       the 12 lily pads "pad<+x>_<z>": pad surfaces with a weed bed 0.8 (top -0.4); the middle pad of each
                   cluster carries the cluster's pad cover 1.2 (snakehead / arapaima)
  reeds            4 clumps: weed zones round the stems at the water (helpers OBST_weed_reed*, as the lake), top
                   -0.05, reed cover 1.0
  helpers          two sunken logs in the open channel (snag wood, top -1.5, log cover 1.0)
Every solid dipping under the surface gets its waterline section (lake.cut_waterline). The front layer paints the parts
under the water too (no occluder): check() adds an above-the-waterline collider check (lake.waterline_check).
"""
import math
import os
import importlib.util

STAGE = "swamp"

# hyb_swamp.main(): render_front(random.Random(31)), then render_back(random.Random(77)). Only the front holds anything
# in the fishable water (the back layer starts at 60 m, zFar is 58).
LAYERS = [("front", "render_front", 31)]

BASE_T = 0.2            # x h: where a trunk's flared base (and its buttress ribs) ends and its shaft begins
CROWN_TIERS, MOSS_TIERS, KNEE_TIERS = 4, 4, 3
MOSS_LINK = 0.9         # m: moss curtains of one limb hanging closer than this (plan) form one overhang
TRUNK = dict(mat="wood", tags="trunk,abrasive", skirt=0.8, skirt_top=-0.35, cover=1.5, cover_for="root")

_spec = importlib.util.spec_from_file_location("obst_lake_shared", os.path.join(os.path.dirname(__file__), "lake.py"))
LK = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(LK)                # the lake module's pad / reed / helper code (same hyb_* builders)


def _stage():
    import sys
    return sys.modules["hyb_swamp"]


def _middle_pad(ob):
    return LK._is_middle_pad(ob, _stage().PADS)


def _in_front(ob):
    return (ob.matrix_world @ ob.data.vertices[0].co).y > 0.0


RULES = [
    dict(name=["DeckBase", "Plank", "Stringer", "Header", "Arm", "Brace", "Hook", "Cap", "CapTop", "Base", "Glass",
               "Flame", "Frame"], obst=None),
    dict(name="Post", where=lambda ob: not _in_front(ob), obst=None),
    dict(name="Post", obst="solid", mat="wood", tiers=1, skirt=0.3, skirt_top=-0.35, cover=0.8, cover_for="post",
         tags="post,abrasive,near"),
    dict(name=["Trunk", "Rib", "Snag", "SRib"], kind="trunk", obst="solid", tiers=4, **TRUNK),
    dict(name=["Limb", "Pad", "Sprig"], kind="trunk", obst="solid", mat="leaf", tiers=CROWN_TIERS, tags="overhang",
         id="{grp}.crown"),
    dict(kind="moss", obst="solid", mat="leaf", tiers=MOSS_TIERS, tags="overhang", id="{grp}.crown"),
    dict(name=["Branch", "Twig"], kind="trunk", obst="solid", mat="leaf", tiers=2, tags="overhang", id="{grp}.b"),
    dict(kind="knee", obst="solid", mat="root", tiers=KNEE_TIERS, skirt=0.3, skirt_top=-0.35, tags="root,abrasive"),
    dict(name="Stub", grp="^log$", obst="solid", mat="wood", tiers=2, tags="log,abrasive", id="log.stub"),
    dict(name="Log", grp="^log$", obst="solid", mat="wood", tiers=2, shape="capsule", skirt=0.4, skirt_top=-0.35,
         cover=1.2, cover_for="log", tags="log,abrasive"),                          # (Log also takes LogMoss)
    dict(kind="pad", where=_middle_pad, obst="pad", mat="pad", weed=0.8, weed_top=-0.4, cover=1.2, cover_for="pad"),
    dict(kind="pad", obst="pad", mat="pad", weed=0.8, weed_top=-0.4),
    dict(kind=["reed", "cattail"], obst=None),                      # -> the stem-base helpers of extra()
]


def _base(ob):
    import re
    return re.sub(r"\.\d{3}$", "", ob.name)


split_z = LK.split_z


def _set_shaft(ob, oid):
    ob["obst_id"] = oid
    ob["obst_tiers"] = 4
    for key in ("obst_skirt", "obst_skirt_top", "obst_cover", "obst_cover_for"):
        if key in ob:
            del ob[key]


def split_trunks(scene, smod):
    """Trunk / Rib of T0..T4 and Snag / SRib of S0: the base (below BASE_T h) keeps the id, skirt and cover; the shaft
    above becomes "<grp>.trunk"."""
    heights = {"T%d" % k: h for k, (x, y, h) in enumerate(smod.TREES)}
    heights["S0"] = smod.SNAG[2]
    n = 0
    for ob in list(scene.objects):
        if ob.type != "MESH" or ob.get("obst") != "solid" or _base(ob) not in ("Trunk", "Rib", "Snag", "SRib"):
            continue
        grp = str(ob.get("grp", ""))
        if grp not in heights:
            continue
        lo, hi = split_z(ob, BASE_T * heights[grp])
        if lo is not None:
            lo["obst_id"] = grp
        if hi is not None:
            _set_shaft(hi, grp + ".trunk")
        n += 1
    return n


def _pts(ob):
    M = ob.matrix_world
    return [M @ v.co for v in ob.data.vertices]


def split_crowns(scene, smod):
    """One overhang per limb: every Pad / Sprig / moss curtain of a cypress goes to the limb whose end is nearest (moss:
    from the point it hangs from); every Twig of the dead snag to its branch."""
    from mathutils import Vector
    trees = {"T%d" % k: (x, y) for k, (x, y, h) in enumerate(smod.TREES)}
    obs = [ob for ob in scene.objects if ob.type == "MESH" and ob.get("obst") == "solid"]
    n = 0
    for grp, (x, y) in trees.items():
        axis = Vector((x, y, 0.0))
        limbs = [ob for ob in obs if ob.get("grp") == grp and _base(ob) == "Limb"]
        ends = []
        for ob in limbs:
            p = max(_pts(ob), key=lambda w: (w.x - axis.x) ** 2 + (w.y - axis.y) ** 2)
            ends.append((p, ob))
        ends.sort(key=lambda e: (round(e[0].z, 1), e[0].x))
        for j, (p, ob) in enumerate(ends):
            ob["obst_id"] = "%s.crown%d" % (grp, j)
        moss = {}
        for ob in obs:
            if ob.get("grp") == grp and _base(ob) in ("Pad", "Sprig"):
                pts = _pts(ob)
                c = sum(pts, Vector()) / len(pts)
            elif ob.get("grp") == grp + "m":
                c = max(_pts(ob), key=lambda w: w.z)               # where the curtain hangs from
            else:
                continue
            j = min(range(len(ends)), key=lambda i: (ends[i][0] - c).length)
            if ob.get("grp") == grp:
                ob["obst_id"] = "%s.crown%d" % (grp, j)
            else:
                moss.setdefault(j, []).append((c, ob))
            n += 1
        # the moss of a limb: its own overhang (spec 3.5: "T<k>m.crown"), one per clump of curtains (curtains hanging
        # within MOSS_LINK of each other); a limb's clumps under different pads would otherwise span the air between
        for j, items in moss.items():
            groups = []
            for c, ob in sorted(items, key=lambda it: (it[0].x, it[0].y)):
                hit = [g for g in groups if any(math.hypot(c.x - d.x, c.y - d.y) < MOSS_LINK for d, _ in g)]
                merged = [(c, ob)]
                for g in hit:
                    merged += g
                    groups.remove(g)
                groups.append(merged)
            groups.sort(key=lambda g: min(d.x for d, _ in g))
            for q, g in enumerate(groups):
                for _, ob in g:
                    ob["obst_id"] = "%sm.crown%d%s" % (grp, j, "abcdefgh"[q] if len(groups) > 1 else "")
    # the dead snag: each branch its own overhang, twigs with their branch
    br = [ob for ob in obs if ob.get("grp") == "S0" and _base(ob) == "Branch"]
    br.sort(key=lambda ob: min(w.z for w in _pts(ob)))
    for j, ob in enumerate(br):
        ob["obst_id"] = "S0.b%d" % j
    for ob in obs:
        if ob.get("grp") == "S0" and _base(ob) == "Twig":
            p0 = min(_pts(ob), key=lambda w: w.z)
            j = min(range(len(br)), key=lambda i: min((w - p0).length for w in _pts(br[i])))
            ob["obst_id"] = "S0.b%d" % j
    return n


def extra(ctx):
    G = ctx["helper"].__globals__
    smod = _stage()
    scene = ctx["scene"]
    LK.rename(scene)                                        # pads "pad<+x>_<z>", the posts "postL" / "postR"
    nt = split_trunks(scene, smod)
    nc = split_crowns(scene, smod)
    nf = LK.float_off_bed(scene, lambda ob: ob.get("grp") == "log")      # the floating log is not on the bed
    nw = LK.cut_waterline(scene)
    hs = []
    # ---- reed clumps: the weed zone round the stems at the water (the lake's code: same reed_clump builder)
    for cx, pts in sorted(LK.reed_clumps(scene).items(), key=lambda kv: kv[0]):
        ring = G["grow"](G["hull2"](pts), LK.REED_GROW, 16)
        cy = sum(p[1] for p in pts) / len(pts)
        hs.append(LK._prism(ctx, "weed", "reed%+.0f_%.0f" % (cx, cy), [tuple(p) for p in ring], -1.0, -0.05, mat="reed",
                            tags="reed", top=-0.05, bot=-99.0, cover=1.0, cover_for="reed"))
    # ---- two sunken logs in the open channel (clear of the pads, reeds, trees and the floating log), 1.5 m under
    for oid, a, b in (("sunklog1", (-3.2, 18.4), (0.2, 20.2)), ("sunklog2", (1.6, 30.2), (5.2, 31.6))):
        c = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        bed = -LK._depth_at(ctx["L"], c[1])
        hs.append(ctx["helper_box"]("snag", oid, (c[0], c[1], (bed - 1.5) / 2), (math.hypot(b[0] - a[0], b[1] - a[1]),
                                                                                   0.5, -1.5 - bed),
                                    rot_z=math.atan2(b[1] - a[1], b[0] - a[0]), mat="wood", tags="log,abrasive",
                                    shape="capsule", top=-1.5, bot=-99.0, cover=1.0, cover_for="log"))
    print("HYB OBST swamp: %d trunk meshes cut at their flare, %d crown / moss pieces given to their limb, %d log meshes "
          "cut off below -0.08 m (floating), %d solids given their waterline section" % (nt, nc, nf, nw))
    return hs


def check(ctx):
    LK.waterline_check(ctx, "swamp")
    LK.shared_check(ctx, _stage(), "swamp")
    LK.bounds_check(ctx, "swamp")
    obst, log = ctx["obstacles"], ctx["log"]
    for k in range(len(_stage().TREES)):
        g = "T%d" % k
        crowns = [e for e in obst if e["id"].startswith(g + ".crown")]
        base = [e for e in obst if e["id"] == g]
        shaft = [e for e in obst if e["id"] == g + ".trunk"]
        if base:
            log("CHECK cypress %s: base r %.2f m (top %.2f), shaft %s, %d limb overhangs (bot %.2f .. %.2f m), knees %d"
                % (g, base[0]["r"], base[0]["top"], "%.2f .. %.2f m" % (shaft[0]["bot"], shaft[0]["top"]) if shaft else "-",
                   len(crowns), min([e["bot"] for e in crowns] or [0]), max([e["bot"] for e in crowns] or [0]),
                   sum(1 for e in obst if e["kind"] == "solid" and e["id"].startswith(g + "k"))))
