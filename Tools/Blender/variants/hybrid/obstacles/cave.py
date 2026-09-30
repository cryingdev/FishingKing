"""
obstacles/cave.py - the CAVE lake's obstacles (Docs/obstacles_spec.md 3.8). Owner: B3 (ice + cave).

FRONT layer (hyb_cave.front_scene):
  sgL / sgR    the two stalagmite groups framing the mid-distance sides, (-7.6, 9.7) and (8.3, 12.7): the tall spire
               (Stalag grp sgL / sgR) + the three rounded Base boulders at its foot (all named "Base", grp "Base": split
               by side) = one solid rock obstacle each, 4 tiers (spire over a boulder mound: the lowest band holds the
               mound, the upper ones the spire), skirt 0.5, cover 1.4 "rock".
  sgL2 sgL3 sgR2 sgR3   the smaller spires beside them: solid rock, 3 tiers, not the spec's 1 (a tapering spire in 1
               tier is the prism of its foot: collider IoU 0.42-0.78 against the painted spire; 3 tiers: 0.82-0.86), no
               skirt / cover of their own (the group's skirt and cover take them in).
  fc2 fc3      the crystal clusters on the stalagmite bases (the front crystals beyond z 5): solid crystal, 2 tiers
               (they sit on the boulders: bot = their lowest point, not the water), cover 1.0 "crystal".
  Chunk5..10   the boulders stepping down into the water beside the ledge: solid rock, tag near (z < 2.5: casts never
               reach them), skirt 0.4.
  not obstacles: the ledge Chunk0..4 (his ground), the crook post + lantern, the mushrooms, the crystals on the near
               boulders (fc0 fc1 fc4).
BACK layer (hyb_cave.back_scene):
  Wall*        the side walls. Each is one open sheet per distance bin (54 m long): its convex hull would cut across
               the alcoves, so extra() slices every sheet into 2.7 m pieces (export-only copies of the painted wall's own
               vertices, thickened 3 m into the rock; the pieces behind zNear - 1 are skipped), solid rock, tags rock,
               wall, abrasive, 4 tiers (the wall leans 3 m into the lake towards the vault; 1 tier would put its
               waterline footprint up to 3 m out on the water). At the waterline the walls stand 15.1-21.6 m out for
               z >= 0.6 (the 9.3 / 10.7 m of the spec's table is the vault's lean behind the angler, z ~ -22): every
               piece lies beyond xLim + 0.3 = 14.3 and the exporter drops it (spec 1.3). Casts land within
               |x| <= 13.4 and fights stay within 13.7, so nothing can reach them.
  Column       the hourglass column at (15.5, 77) (the other, (-17, 92), is beyond xLim): solid rock, 4 tiers, skirt 0.6,
               cover 1.5 "rock". Kept by the spec's reach rule (zFar 100), though no rod casts past 36 m.
  Islet        the far waterline islets (only (-9, 101) reaches zFar 100): solid rock, 3 tiers, skirt 0.6, cover 1.5
               (its hold point would lie beyond zFar - 1: the exporter writes no cover).
  Cry          the back crystal clusters (far wall z ~110, side walls |x| >= 15): solid crystal, skirt 0.6, cover 1.5
               "crystal" - none is within reach (dropped).
  helper       ridge: a rock ridge on the bed across (-3, 30) - (4, 36), 1.6 m wide, up to bed + 1.0: snag rock,
               abrasive, cover 1.5 "rock" (실러캔스 / 초롱아귀).
  not obstacles: the vault (Ceil*), the stalactites (Stal), the far wall (z 109-113 > zFar), the water plane.
"""
import math

STAGE = "cave"

# hyb_cave.main(): render_front(random.Random(31)), then render_back(random.Random(77))
LAYERS = [("front", "render_front", 31), ("back", "render_back", 77)]


def _mean_x(ob):
    """rock_chunk() bakes the world position into the mesh (matrix_world = identity): the side from the vertices."""
    vs = ob.data.vertices
    return sum(v.co.x for v in vs) / max(1, len(vs))


GROUP = dict(obst="solid", mat="rock", tiers=4, skirt=0.5, skirt_top=-0.35, cover=1.4, cover_for="rock",
             tags="rock,abrasive")

RULES = [
    # ---------------------------------------------------------------- front
    dict(layer="front", kind="ledge", obst=None),                                  # Chunk0..4: his ground
    dict(layer="front", kind=["post", "lantern", "flame", "mushroom"], obst=None),
    dict(layer="front", grp=r"^Chunk([5-9]|10)$", obst="solid", mat="rock", tiers=4, skirt=0.4, skirt_top=-0.35,
         tags="rock,abrasive,near"),
    dict(layer="front", name="Stalag", grp=r"^sg[LR]$", id="{grp}", **GROUP),
    dict(layer="front", name="Base", where=lambda ob: _mean_x(ob) < 0, id="sgL", **GROUP),
    dict(layer="front", name="Base", where=lambda ob: _mean_x(ob) >= 0, id="sgR", **GROUP),
    dict(layer="front", name="Stalag", grp=r"^sg[LR][23]$", obst="solid", mat="rock", tiers=3, tags="rock,abrasive"),
    dict(layer="front", kind="crystal", grp=r"^fc[23]$", obst="solid", mat="crystal", tiers=2, cover=1.0,
         cover_for="crystal", tags="crystal,abrasive"),
    dict(layer="front", kind="crystal", obst=None),                               # fc0 fc1 fc4: on the near boulders
    # ---------------------------------------------------------------- back
    dict(layer="back", name="Wall", obst=None),                                   # sliced into pieces in extra()
    dict(layer="back", name="Column", obst="solid", mat="rock", tiers=4, skirt=0.6, skirt_top=-0.35, cover=1.5,
         cover_for="rock", tags="rock,abrasive"),
    dict(layer="back", name="Islet", obst="solid", mat="rock", tiers=3, skirt=0.6, skirt_top=-0.35, cover=1.5,
         cover_for="rock", tags="rock,abrasive"),
    dict(layer="back", kind="crystal", obst="solid", mat="crystal", tiers=2, skirt=0.6, skirt_top=-0.35, cover=1.5,
         cover_for="crystal", tags="crystal,abrasive"),
    dict(layer="back", kind=["ceiling", "stalactite", "farwall", "water"], obst=None),
]

WALL_PIECE = 2          # rows of the wall sheet per piece (bin 0: 1.35 m apart -> 2.7 m pieces)
WALL_THICK = 3.0        # m the piece is thickened into the rock (a rig can never pass behind the painted face)
RIDGE = ((-3.0, 30.0), (4.0, 36.0), 1.6, 1.0, 1.5)       # end, end, width, height above the bed, cover


def depth_at(L, z):
    """StageLayout.DepthAt (Assets/Scripts/Core/Art.cs); the cave has no tide."""
    zs, vs = L["depthZ"], L["depthV"]
    if z <= zs[0]:
        return float(vs[0])
    for i in range(1, len(zs)):
        if z <= zs[i]:
            return float(vs[i - 1] + (vs[i] - vs[i - 1]) * (z - zs[i - 1]) / (zs[i] - zs[i - 1]))
    return float(vs[-1])


def extra(ctx):
    if ctx["layer"] != "back":
        return []
    import numpy as np
    L = ctx["L"]
    out = []
    # ---- the side walls, sliced along the lake into convex pieces (the painted sheet's own vertices)
    for ob in list(ctx["scene"].objects):
        nm = ob.name.split(".")[0]
        if ob.type != "MESH" or not nm.startswith("Wall"):
            continue
        side = -1.0 if nm.startswith("Wall-1") else 1.0
        M = np.array(ob.matrix_world)
        co = np.array([tuple(v.co) for v in ob.data.vertices], np.float64) @ M[:3, :3].T + M[:3, 3]
        ys = np.unique(np.round(co[:, 1], 4))
        for i0 in range(0, len(ys) - 1, WALL_PIECE):
            y0, y1 = ys[i0], ys[min(len(ys) - 1, i0 + WALL_PIECE)]
            if y1 < float(L["zNear"]) - 1.0:
                continue            # behind the angler (and partly behind the camera): never fishable water
            seg = co[(co[:, 1] >= y0 - 1e-3) & (co[:, 1] <= y1 + 1e-3)]
            back = seg.copy()
            back[:, 0] += side * WALL_THICK
            v = np.concatenate([seg, back])
            oid = "wall%s%.1f" % ("L" if side < 0 else "R", (y0 + y1) / 2)
            out.append(ctx["helper"]("solid", oid, [tuple(p) for p in v], [], mat="rock", tags="rock,wall,abrasive",
                                     tiers=4))
    # ---- the rock ridge on the bed (cover for the deep cave fish)
    (ax, az), (bx, bz), wd, up, cov = RIDGE
    cz = (az + bz) / 2
    bed = -depth_at(L, cz)
    ln = math.hypot(bx - ax, bz - az)
    out.append(ctx["helper_box"]("snag", "ridge", ((ax + bx) / 2, cz, bed + (up - 0.4) / 2), (ln, wd, up + 0.4),
                                 rot_z=math.atan2(bz - az, bx - ax), mat="rock", tags="rock,abrasive", bot=-99.0,
                                 top=round(bed + up, 3), cover=cov, cover_for="rock"))
    return out


# ---------------------------------------------------------------------------------------------------- the checks
def _X():
    import sys
    m = sys.modules.get("__main__")
    if m is not None and hasattr(m, "Cam") and hasattr(m, "edge_dist"):
        return m
    import importlib
    return importlib.import_module("hyb_obstacles")


def check(ctx):
    """The back-layer solids (the scene is still the back layer): each exported collider vs the rebuilt back layer's
    raster of its own painted object through the JSON camera (the back PNG is opaque, so the painted check of the
    exporter covers the front layer only). Plus the helpers: in the water, within reach."""
    import numpy as np
    import bpy
    X = _X()
    L, log = ctx["L"], ctx["log"]
    cam = X.Cam(L)
    W, H = cam.W, cam.H
    obst = ctx["obstacles"]
    by_id = {e["id"]: e for e in obst}
    # the rebuilt back layer through the JSON camera, one id per object (triangles reaching behind the camera - the
    # vault and walls behind the angler - are left out: they never cover the far water)
    tris, ids, grp, mind = [], [], [], []
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or ob.hide_render:
            continue
        g, tri = X.mesh_data(ob)
        if len(tri) == 0:
            continue
        p = cam.px(g)[tri]
        p = p[(p[..., 2] > 0.5).all(1)]
        if not len(p):
            continue
        k = len(grp)
        grp.append(str(ob.get("obst_id") or ob.get("grp") or X.base_name(ob)) if "obst" in ob else None)
        mind.append(float(p[..., 2].min()))
        tris.append(p)
        ids.append(np.full(len(p), k, np.int32))
    ib, zb = X.raster(np.concatenate(tris), np.concatenate(ids), W, H)
    grp_px = np.array([g if g is not None else "" for g in grp] + [""], object)[ib]      # (-1 -> "")
    mind_px = np.array(mind + [np.inf])[ib]
    rows = []
    for e in obst:
        if e["kind"] != "solid" or e["layer"] != "back":
            continue
        mesh = grp_px == e["id"]
        if mesh.sum() < 4:
            log("CHECK back %-10s hidden: skipped" % e["id"])
            continue
        coll = X.tier_silhouette(e, cam, W, H)
        dc = float(cam.px(np.array([e["x"], 0.5 * (e["bot"] + e["top"]), e["z"]]))[2])
        coll &= ~((grp_px != e["id"]) & (mind_px < dc - e["r"] - 0.5))      # hidden behind nearer things
        iou = float((coll & mesh).sum()) / max(1, int((coll | mesh).sum()))
        em, ex = X.edge_dist(mesh, coll)
        rows.append(iou)
        log("CHECK back %-10s %4d px (rebuilt raster)  collider IoU %.2f, edge %.2f / max %.2f px  (x %.1f..%.1f, z %.1f)"
            % (e["id"], int(mesh.sum()), iou, em, ex, np.array(e["pts"])[0::2].min(), np.array(e["pts"])[0::2].max(),
               e["z"]))
    walls = [e for e in obst if "wall" in e["tags"]]
    log("CHECK walls: %d wall pieces kept (the rest lie beyond xLim + 0.3 = %.1f)" % (len(walls), L["xLim"] + 0.3))
    for e in obst:
        if e["kind"] not in ("snag", "cover") or e["parent"]:
            continue
        p = np.array(e["pts"]).reshape(-1, 2)
        beds = [-depth_at(L, z) for z in p[:, 1]]
        log("CHECK helper %-6s %-5s x %.2f..%.2f z %.2f..%.2f bed %.2f..%.2f top %.2f; in the water %s"
            % (e["id"], e["kind"], p[:, 0].min(), p[:, 0].max(), p[:, 1].min(), p[:, 1].max(), min(beds), max(beds),
               e["top"], abs(p[:, 0]).max() <= L["xLim"] and p[:, 1].min() >= L["zNear"] and e["top"] > max(beds)))
    for e in obst:
        if e["kind"] == "cover":
            log("CHECK cover %-12s for %-8s hold (%.2f, %.2f) holdCross %s" % (e["id"], ",".join(e["coverFor"]), e["hx"],
                                                                            e["hz"], e["holdCross"]))
