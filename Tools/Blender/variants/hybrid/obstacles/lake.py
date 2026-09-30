"""
obstacles/lake.py - the LAKE's obstacles (Docs/obstacles_spec.md 3.3). Owner: B1.

Everything hyb_lake.py paints in the water is in its FRONT layer (the back layer is the far shore, 180 m out):
  Post (+ RopeWrap)  the two pier-end posts standing in the water at (+-1.38, 0.95), "postL" / "postR": colliders
                     ("near": at his feet), a snag skirt (no cover: a hooked fish never gets that close); the four under
                     the pier (y < 0) are behind him: not obstacles
  Stake              the mooring stake (-10.4, 13.0): collider (to the bed), skirt, a post cover
  boat (kind)        the moored rowboat: Hull, Gunwale, Thwart, Oar, Blade = one solid (3 tiers: with 2 the stern came
                     out 0.14 m too tall; hull top ~0.5 m), a keel skirt and a boat cover. It FLOATS: the hull under
                     -0.08 m is cut off the obstacle (still painted) so bed = false.
  pad (kind)         the 12 lily pads "pad<+x>_<z>" (4 clusters): pad surfaces, each with a weed bed round it (the
                     stems); the middle pad of every cluster carries the cluster's cover
  reeds              the 4 reed clumps: a weed zone each (helper OBST_weed_reed*: the plan hull of the clump's stems AT
                     THE WATER, not of the arching blades in the air) with a reed cover
  helpers            a sunken log on the bed in open water (snag wood, cover log) and a submerged weed bed (weed, top
                     -1.2: the crankbait dives into it; cover weed)
Not obstacles: the pier (DeckBase / Plank / Stringer: his ground), the hanging Rope, the MoorRope, bucket, tackle box.
Every solid dipping under the surface gets its waterline section (cut_waterline).

This module also holds the code the swamp shares (same lily / reed_clump builders): split_z, float_off_bed, rename,
cut_waterline, reed_clumps, waterline_check, shared_check.

Checks (check()): solids above the waterline (the front layer paints the parts under the water too), per pad the
exported footprint vs its painted pixels (IoU, edge distance), per reed zone the share of the clump's painted waterline
pixels inside the zone, the helpers' depths.
"""
import math

STAGE = "lake"

# hyb_lake.main(): render_front(random.Random(31)), then render_back(random.Random(77)). Only the front holds anything
# in the water.
LAYERS = [("front", "render_front", 31)]

PAD_CLUSTER = 3.2          # m: pads closer than this to each other form one cluster (one cover on its middle pad)
REED_BASE_Z = 0.25         # m: stem points below this height are "at the water" (the blades arch out above it)
REED_GROW = 0.2            # m: the weed zone = the stems' plan hull grown by this


def _stage():
    import sys
    return sys.modules["hyb_lake"]


def _middle_pads(PADS):
    """(x, y) of the pad nearest the centroid of each pad cluster of PADS (hyb_lake / hyb_swamp .PADS)."""
    pads = [(p[0], p[1], p[2]) for p in PADS]
    left = list(range(len(pads)))
    out = []
    while left:
        cl = [left.pop(0)]
        grew = True
        while grew:
            grew = False
            for j in list(left):
                if any(math.hypot(pads[j][0] - pads[i][0], pads[j][1] - pads[i][1]) < PAD_CLUSTER for i in cl):
                    cl.append(j)
                    left.remove(j)
                    grew = True
        cx = sum(pads[i][0] for i in cl) / len(cl)
        cy = sum(pads[i][1] for i in cl) / len(cl)
        best = min(cl, key=lambda i: math.hypot(pads[i][0] - cx, pads[i][1] - cy))
        out.append(pads[best][:2])
    return out


def _pad_xy(ob):
    """A lily pad mesh's centre vertex (hyb_lake.lily: vertex 0 is the centre)."""
    v = ob.matrix_world @ ob.data.vertices[0].co
    return v.x, v.y


def _is_middle_pad(ob, PADS=None):
    x, y = _pad_xy(ob)
    return any(math.hypot(x - mx, y - my) < 0.05 for (mx, my) in _middle_pads(PADS or _stage().PADS))


def _in_water(ob):
    """Pier posts: only the ones in front of him (y > 0); the four under the pier are behind the angler."""
    return (ob.matrix_world @ ob.data.vertices[0].co).y > 0.0


RULES = [
    dict(name=["DeckBase", "Plank", "Stringer", "Bucket", "BWater", "Bail", "Tackle", "TLid", "THandle", "Latch",
               "MoorRope"], obst=None),
    dict(name="Post", where=lambda ob: not _in_water(ob), obst=None),
    dict(name=["Post", "RopeWrap"], obst="solid", mat="wood", tiers=1, skirt=0.3, skirt_top=-0.35, cover=0.8,
         cover_for="post", tags="post,abrasive,near"),
    dict(name="Rope", obst=None),                                   # the rope hanging from the right post
    dict(name="Stake", obst="solid", mat="wood", tiers=1, skirt=0.3, skirt_top=-0.35, cover=0.8, cover_for="post",
         tags="post,abrasive"),
    dict(kind="boat", obst="solid", mat="hull", tiers=3, skirt=0.4, skirt_top=-0.35, cover=1.2, cover_for="boat",
         tags="hull,abrasive"),
    dict(kind="pad", where=_is_middle_pad, obst="pad", mat="pad", weed=0.8, weed_top=-0.4, cover=1.2, cover_for="pad"),
    dict(kind="pad", obst="pad", mat="pad", weed=0.8, weed_top=-0.4),
    dict(kind=["reed", "cattail"], obst=None),                      # -> the stem-base helpers of extra()
]


def _depth_at(L, z):
    """StageLayout.DepthAt (no tide): the bed is at y = -depth."""
    zs, vs = L["depthZ"], L["depthV"]
    if z <= zs[0]:
        return vs[0]
    for i in range(1, len(zs)):
        if z <= zs[i]:
            return vs[i - 1] + (vs[i] - vs[i - 1]) * (z - zs[i - 1]) / (zs[i] - zs[i - 1])
    return vs[-1]


def _prism(ctx, kind, oid, ring, z0, z1, **props):
    """A vertical prism helper over a plan ring [(x, y), ...] (Blender axes) from z0 to z1."""
    n = len(ring)
    verts = [(x, y, z0) for (x, y) in ring] + [(x, y, z1) for (x, y) in ring]
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    return ctx["helper"](kind, oid, verts, faces, **props)


def reed_clumps(scene):
    """{clump x: [(x, y), ...]} the world points of every reed / cattail / iris object below REED_BASE_Z, per clump
    (grp "reed<cx>_<fan>" / "iris<cx>_<k>" of hyb_lake.reed_clump)."""
    import re
    out = {}
    for ob in scene.objects:
        if ob.type != "MESH" or ob.get("kind") not in ("reed", "cattail"):
            continue
        m = re.match(r"^(?:reed|iris)(-?[0-9.]+)_\d+$", str(ob.get("grp", "")))
        if not m:
            continue
        M = ob.matrix_world
        pts = out.setdefault(float(m.group(1)), [])
        for v in ob.data.vertices:
            w = M @ v.co
            if w.z < REED_BASE_Z:
                pts.append((w.x, w.y))
    return out


def cut_waterline(scene):
    """Adds the waterline section to every solid that dips under the surface (bmesh bisect at z = 0, NOTHING removed:
    the lake / swamp front layers paint the part under the water too). The exporter builds a solid's footprint from its
    vertices at y >= 0; without the cut a post / rib / hull crossing the surface between two rings has no vertex at
    the water and its footprint shrinks to the ring above. -> number of meshes cut."""
    import bmesh
    from mathutils import Matrix
    n = 0
    for ob in scene.objects:
        if ob.type != "MESH" or ob.get("obst") != "solid":
            continue
        M = ob.matrix_world.copy()
        zs = [(M @ v.co).z for v in ob.data.vertices]
        if not zs or min(zs) >= 0.0 or max(zs) <= 0.0:
            continue
        if ob.data.users > 1:
            ob.data = ob.data.copy()
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0.0, 0.0, 0.0),
                               plane_no=(0.0, 0.0, 1.0))
        bm.to_mesh(ob.data)
        bm.free()
        ob.matrix_world = Matrix.Identity(4)
        n += 1
    return n


def split_z(ob, zc):
    """Cuts a painted mesh at the height zc into two objects (the geometry is only divided: the pieces rasterise to the
    same pixels). -> (below or None, above or None); the piece above is a copy carrying the same custom properties."""
    import bmesh
    from mathutils import Matrix
    M = ob.matrix_world.copy()
    zs = [(M @ v.co).z for v in ob.data.vertices]
    if min(zs) >= zc:
        return None, ob
    if max(zs) <= zc:
        return ob, None
    up = ob.copy()
    up.data = ob.data.copy()
    for col in ob.users_collection:
        col.objects.link(up)
    if ob.data.users > 1:
        ob.data = ob.data.copy()
    for o, below in ((ob, True), (up, False)):
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0.0, 0.0, zc),
                               plane_no=(0.0, 0.0, 1.0), clear_outer=below, clear_inner=not below)
        bm.to_mesh(o.data)
        bm.free()
        o.matrix_world = Matrix.Identity(4)
    return ob, up


def untag(ob):
    for key in list(ob.keys()):
        if key.startswith("obst"):
            del ob[key]
    ob["_obst_skip"] = True


def float_off_bed(scene, pick, zc=-0.08):
    """A FLOATING prop (the rowboat's keel dips 0.24 m, the swamp's log 0.24 m): the exporter marks a solid reaching
    under -0.1 m as standing on the bed (bed = true, like a post). The part deeper than zc (-0.08: a cut AT -0.1 lands a
    hair under it) is cut off the obstacle (it stays painted, it is just no longer tagged; the skirt covers the water
    under the prop), so bed = false.
    -> number of meshes cut."""
    n = 0
    for ob in list(scene.objects):
        if ob.type != "MESH" or ob.get("obst") != "solid" or not pick(ob):
            continue
        lo, hi = split_z(ob, zc)
        if lo is not None:
            untag(lo)
            n += 1
    return n


def rename(scene):
    """Readable ids: the lily pads "pad<+x>_<z>" (the default was the grp "pad<x><y>": "pad-6.415.2"), the posts in
    the water "postL" / "postR"."""
    import re
    for ob in scene.objects:
        if ob.type != "MESH" or "obst" not in ob:
            continue
        if ob.get("kind") == "pad" and ob["obst"] == "pad":
            x, y = _pad_xy(ob)
            ob["obst_id"] = "pad%+.1f_%.1f" % (x, y)
        elif re.sub(r"\.\d{3}$", "", ob.name) in ("Post", "RopeWrap") and ob["obst"] == "solid":
            ob["obst_id"] = "postL" if (ob.matrix_world @ ob.data.vertices[0].co).x < 0 else "postR"


def extra(ctx):
    G = ctx["helper"].__globals__                     # hyb_obstacles' plan geometry (hull2, grow)
    L = ctx["L"]
    rename(ctx["scene"])
    nf = float_off_bed(ctx["scene"], lambda ob: ob.get("kind") == "boat")
    print("HYB OBST %s: %d boat meshes cut off below -0.08 m (floating), %d solids given their waterline section"
          % (ctx["stage"], nf, cut_waterline(ctx["scene"])))
    hs = []
    # ---- reed clumps: the weed zone round the stems at the water
    for k, (cx, pts) in enumerate(sorted(reed_clumps(ctx["scene"]).items(), key=lambda kv: kv[0])):
        ring = G["grow"](G["hull2"](pts), REED_GROW, 16)
        cy = sum(p[1] for p in pts) / len(pts)
        oid = "reed%+.0f_%.0f" % (cx, cy)
        hs.append(_prism(ctx, "weed", oid, [tuple(p) for p in ring], -1.0, -0.05, mat="reed", tags="reed",
                         top=-0.05, bot=-99.0, cover=1.0, cover_for="reed"))
    # ---- a sunken log on the bed in open water, left of the middle (-4.5, 21) - (-1.8, 23.5)
    a, b = (-4.5, 21.0), (-1.8, 23.5)
    c = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
    ln = math.hypot(b[0] - a[0], b[1] - a[1])
    bed = -_depth_at(L, c[1])
    hs.append(ctx["helper_box"]("snag", "sunklog", (c[0], c[1], bed + 0.2), (ln, 0.45, 0.4),
                                rot_z=math.atan2(b[1] - a[1], b[0] - a[0]), mat="wood", tags="log,abrasive",
                                shape="capsule", top=round(bed + 0.4, 3), bot=-99.0, cover=1.0, cover_for="log"))
    # ---- a submerged weed bed centred (3.0, 14.0), r 2.2, reaching up to 1.2 m under the surface
    ring = [(3.0 + 2.2 * math.cos(2 * math.pi * i / 16), 14.0 + 2.2 * math.sin(2 * math.pi * i / 16)) for i in range(16)]
    hs.append(_prism(ctx, "weed", "weedbed", ring, -_depth_at(L, 14.0), -1.2, mat="weed", tags="weed", top=-1.2,
                     bot=-99.0, cover=0.5, cover_for="weed"))
    return hs


# ============================================================================ check
def check(ctx):
    """Solids above the waterline (waterline_check), pads: exported footprint (at the pad's top) vs the pad's painted
    pixels. Reeds: the clump's painted waterline pixels inside the zone's apparent outline. Helpers: depth."""
    waterline_check(ctx, "lake")
    shared_check(ctx, _stage(), "lake")
    bounds_check(ctx, "lake")


def bounds_check(ctx, stage):
    """A solid's x, z, r are its FOOTPRINT's (the lowest tier: hyb_obstacles.entry). Where an upper tier reaches out
    beyond it (an overhang's pads above its moss ends, a sloping tetrapod leg, a branch, a flared post top) the broad
    phase "bounding circle r + 0.05 holds q.xz" (spec 4.3) would skip a real contact. Logs how many solids that is and
    by how much: the game must take the broad-phase radius over ALL tier points (Obstacles.Load)."""
    import numpy as np
    out = []
    for e in ctx["obstacles"]:
        if e["kind"] != "solid":
            continue
        c = np.array([e["x"], e["z"]])
        rt = max(float(np.max(np.linalg.norm(np.array(t["pts"]).reshape(-1, 2) - c, axis=1))) for t in e["tiers"])
        if rt > e["r"] + 0.05:
            out.append((rt - e["r"], e["id"]))
    out.sort(reverse=True)
    ctx["log"]("CHECK bounds %s: %d / %d solids have tiers beyond r + 0.05 (up to %.2f m: %s) -> the loader must use "
               "the radius over all tier points" % (stage, len(out), sum(1 for e in ctx["obstacles"] if e["kind"] == "solid"),
                                                    out[0][0] if out else 0.0, ", ".join(i for _, i in out[:4]) or "-"))


def waterline_check(ctx, stage):
    """The exporter's collider check compares the tier prisms (which start at the water, y = 0) with ALL of a prop's
    painted pixels. The lake and swamp front layers have no water occluder: a post's, knee's, trunk's or log's part
    under the surface is painted too (the water is drawn behind the front layer), so that check counts the painted
    underwater part against the collider. This one compares the collider with the painted pixels ABOVE the waterline:
    the front layer is rasterised through the JSON camera with depth, every pixel of a solid gets the world height of
    its nearest surface, pixels (and the 1 px outline next to them) of its part under y = 0 are left out.
    Logs CHECK <id> above water: collider IoU, edge mean / max (px) and a summary."""
    import os
    import numpy as np
    import bpy
    G = ctx["log"].__globals__
    Cam, mesh_data, raster, edge_dist, tier_silhouette, nb4, base_name, R, C = (
        G["Cam"], G["mesh_data"], G["raster"], G["edge_dist"], G["tier_silhouette"], G["_nb4_or"], G["base_name"],
        G["R"], G["C"])
    log, L, obst = ctx["log"], ctx["L"], ctx["obstacles"]
    cam = Cam(L)
    W, H = cam.W, cam.H
    A = R.load_png(os.path.join(C.SPRITES, "Stages", "%s_front.png" % stage))[..., 3] > 0.5
    by_id = {e["id"]: e for e in obst if e["kind"] == "solid"}
    tri_all, id_all, names, gidx = [], [], [], {}
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or ob.hide_render:
            continue
        g, tri = mesh_data(ob)
        if len(tri) == 0:
            continue
        gi = -2
        if ob.get("obst") == "solid":
            oid = str(ob.get("obst_id") or ob.get("grp") or base_name(ob))
            if oid not in gidx:
                gidx[oid] = len(names)
                names.append(oid)
            gi = gidx[oid]
        tri_all.append(cam.px(g)[tri])
        id_all.append(np.full(len(tri), gi, np.int32))
    ib, zb = raster(np.concatenate(tri_all), np.concatenate(id_all), W, H)
    rows = (np.arange(H)[:, None] + 0.5) * np.ones((1, W))
    d = np.where(np.isfinite(zb), zb, 1.0)
    yc = (H / 2.0 - rows) * d / cam.f
    yw = cam.cam[1] + yc * cam.c - d * cam.s                 # world height of the nearest surface
    S = ib != -1
    ring = A & ~S & nb4(S)
    own = ib.copy()
    for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):         # the exporter's outline ownership
        src = np.full((H, W), -1, np.int32)
        src[max(0, -dy):H - max(0, dy), max(0, -dx):W - max(0, dx)] = ib[max(0, dy):H - max(0, -dy), max(0, dx):W - max(0, -dx)]
        take = ring & (own == -1) & (src != -1)
        own[take] = src[take]
    res = []
    for gi, oid in enumerate(names):
        e = by_id.get(oid)
        if e is None:
            continue
        mesh = ib == gi
        under = mesh & (yw < -0.005)
        above = mesh & ~under
        painted = A & (own == gi)
        rim = painted & ~mesh
        rim_under = rim & nb4(under) & ~nb4(above)
        pa = (painted & above) | (rim & ~rim_under)
        if pa.sum() < 4:
            continue
        others = A & (own != gi) & (own != -1)
        coll = tier_silhouette(e, cam, W, H) & ~others
        iou = float((coll & pa).sum()) / max(1, int((coll | pa).sum()))
        em, ex = edge_dist(coll, pa)
        res.append((oid, int(pa.sum()), int((painted & ~pa).sum()), iou, em, ex))
    for oid, n, nu, iou, em, ex in res:
        log("CHECK %-12s above water %4d px painted (%3d px under the surface left out)  collider IoU %.2f, edge %.2f / "
            "max %.2f px" % (oid, n, nu, iou, em, ex))
    if res:
        big = [r for r in res if r[1] >= 30]
        log("CHECK waterline summary %s: %d solids; above-water collider IoU mean %.2f, %d / %d >= 0.7; edge mean %.2f px "
            "(max %.2f); solids with >= 30 px: IoU mean %.2f, %d / %d >= 0.7, edge mean %.2f px"
            % (stage, len(res), float(np.mean([r[3] for r in res])), sum(1 for r in res if r[3] >= 0.7), len(res),
               float(np.nanmean([r[4] for r in res])), float(np.nanmax([r[5] for r in res])),
               float(np.mean([r[3] for r in big])) if big else 0, sum(1 for r in big if r[3] >= 0.7), len(big),
               float(np.nanmean([r[4] for r in big])) if big else 0))
    return res


def shared_check(ctx, smod, stage):
    import os
    import numpy as np
    import bpy
    G = ctx["log"].__globals__
    Cam, mesh_data, raster, poly_mask, edge_dist, R, C = (G["Cam"], G["mesh_data"], G["raster"], G["poly_mask"],
                                                         G["edge_dist"], G["R"], G["C"])
    log, L, obst = ctx["log"], ctx["L"], ctx["obstacles"]
    cam = Cam(L)
    W, H = cam.W, cam.H
    front = R.load_png(os.path.join(C.SPRITES, "Stages", "%s_front.png" % stage))
    A = front[..., 3] > 0.5
    # the front layer's meshes through the JSON camera, one id per pad / reed clump (everything else -2)
    tri_all, id_all, names = [], [], []
    idx = {}
    import re
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or ob.hide_render:
            continue
        g, tri = mesh_data(ob)
        if len(tri) == 0:
            continue
        key = None
        if ob.get("kind") == "pad":
            x, y = _pad_xy(ob)
            key = "pad:%.2f,%.2f" % (x, y)
        elif ob.get("kind") in ("reed", "cattail"):
            m = re.match(r"^(?:reed|iris)(-?[0-9.]+)_\d+$", str(ob.get("grp", "")))
            key = "reed:%s" % m.group(1) if m else None
        if key is not None and key not in idx:
            idx[key] = len(names)
            names.append(key)
        tri_all.append(cam.px(g)[tri])
        id_all.append(np.full(len(tri), idx[key] if key is not None else -2, np.int32))
    ib, _ = raster(np.concatenate(tri_all), np.concatenate(id_all), W, H)
    pads = [e for e in obst if e["kind"] == "pad"]
    ious, edges = [], []
    for e in pads:
        key = min((k for k in names if k.startswith("pad:")),
                  key=lambda k: math.hypot(float(k[4:].split(",")[0]) - e["x"], float(k[4:].split(",")[1]) - e["z"]))
        painted = A & (ib == idx[key])
        p = np.array(e["pts"]).reshape(-1, 2)
        coll = poly_mask(cam.px(np.stack([p[:, 0], np.full(len(p), e["top"]), p[:, 1]], -1))[:, :2], W, H)
        coll &= ~(A & (ib != idx[key]) & (ib != -1))           # hidden behind nearer props
        if painted.sum() < 4:
            log("CHECK %-14s pad hidden (%d px)" % (e["id"], int(painted.sum())))
            continue
        iou = float((coll & painted).sum()) / max(1, int((coll | painted).sum()))
        em, ex = edge_dist(coll, painted)
        ious.append(iou)
        edges.append((em, ex))
        log("CHECK %-14s pad %4d px painted  footprint IoU %.2f, edge %.2f / max %.2f px"
            % (e["id"], int(painted.sum()), iou, em, ex))
    if ious:
        log("CHECK pads %s: %d pads, IoU mean %.2f (min %.2f), edge mean %.2f px (max %.2f)"
            % (stage, len(ious), float(np.mean(ious)), float(np.min(ious)), float(np.mean([a for a, _ in edges])),
               float(np.max([b for _, b in edges]))))
    # reeds: the lowest painted pixel of every column of a clump = where its stems meet the water
    for e in obst:
        if e["kind"] != "weed" or "reed" not in e["tags"]:
            continue
        key = min((k for k in names if k.startswith("reed:")), key=lambda k: abs(float(k[5:]) - e["x"]))
        m = A & (ib == idx[key])
        cols = np.flatnonzero(m.any(0))
        base = [(int(m.shape[0] - 1 - np.argmax(m[::-1, c])), int(c)) for c in cols]
        p = np.array(e["pts"]).reshape(-1, 2)
        p3 = cam.apparent(np.stack([p[:, 0], np.full(len(p), e["top"]), p[:, 1]], -1))
        zone = poly_mask(cam.px(p3)[:, :2], W, H)
        near = np.zeros_like(zone)                              # the zone grown by 1 px (its outline)
        near[1:] |= zone[:-1]
        near[:-1] |= zone[1:]
        near[:, 1:] |= zone[:, :-1]
        near[:, :-1] |= zone[:, 1:]
        near |= zone
        # the stems' own bottom pixels (below the arching blades): columns whose lowest paint lies in the lower
        # quarter of the clump's painted height
        rows = np.array([r for r, _ in base])
        lo = rows.max() - 0.25 * (rows.max() - np.argwhere(m)[:, 0].min())
        stem = [(r, c) for (r, c) in base if r >= lo]
        inside = sum(1 for (r, c) in stem if near[r, c])
        log("CHECK %-14s reed zone: %d / %d stem-base px of clump x %s inside (%.0f %%); zone %.1f x %.1f m"
            % (e["id"], inside, len(stem), key[5:], 100.0 * inside / max(1, len(stem)),
               p[:, 0].max() - p[:, 0].min(), p[:, 1].max() - p[:, 1].min()))
    for e in obst:
        if e["src"] and all(s.startswith("OBST_") for s in e["src"]) and "reed" not in e["tags"] and not e["parent"]:
            bed = -_depth_at(L, e["z"])
            log("CHECK helper %-12s %s at (%.2f, %.2f) r %.2f: top %.2f, bed %.2f (%.2f m over the bed, %.2f m under "
                "the surface)" % (e["id"], e["kind"], e["x"], e["z"], e["r"], e["top"], bed, e["top"] - bed, -e["top"]))
