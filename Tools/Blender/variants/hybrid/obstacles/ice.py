"""
obstacles/ice.py - the ICE lake's obstacles (Docs/obstacles_spec.md 3.6). Owner: B3 (ice + cave).

Nothing painted on the ice is an obstacle: the drifts, the two shanties, the auger and the bucket stand ON the ice
(spec 1.3), and no cast flies here (the jig drops straight into the hole). What the ice exports:
  hole   rim    the hole's edge, from the BACK layer's slush funnel: hyb_ice.ice_sheet() builds the funnel from the ice
                top (iceY) down to the water plane, and its BOTTOM RING is the gameplay hole circle (holeX, holeZ, holeR)
                at y = 0. The exporter's rim fit takes every vertex within 3 cm of the lowest one, which would also take
                the funnel's next ring (1.9 cm up, 8 cm wider): so the rim is an export-only copy of the funnel's exact
                bottom ring (96 of its own vertices), not the whole funnel. bot -0.4 (the ice under the water line),
                top iceY. Fights: the line leans on its lower edge (spec 7.4 case 3).
  gravel snag   the gravel under the hole, where the jig / egi / soft worm touch the bottom: a circle r 1.5 m round the
                hole, from the bed up to bed + 0.3 (bed at the hole's centre). Not abrasive (a fish fought straight
                under the hole must not rub on it every fight); per-touch snag rolls only (spec 6.9).
  pile   snag   a rock pile on the bed at (5.0, 8.0), r 2.0, up to bed + 0.8: a cover for burbot (모캐, cover "rock");
                abrasive (a fish holding in it grinds the line on it).
Helpers are circles written as 16-gons of the circle's AREA (circumradius r / 0.98716), so the exporter's circle fit
(the area-equivalent radius of its 16-gon) gives back exactly r.
"""
import math

STAGE = "ice"

# hyb_ice.main(): render_front(random.Random(31)), then render_back(random.Random(77)). The front holds nothing in the
# water (it is built so the painted check proves the rebuild is the painted layer); the rim lives in the back layer.
LAYERS = [("front", "render_front", 31), ("back", "render_back", 77)]

RULES = [
    # everything on the ice: not obstacles (spec 1.3)
    dict(kind=["drift", "prop", "bail"], obst=None),
    # the funnel: the rim helper below copies its bottom ring (the whole funnel would bias the circle fit)
    dict(name="Funnel", obst=None),
]

GRAVEL_R, GRAVEL_UP = 1.5, 0.3
PILE_C, PILE_R, PILE_UP, PILE_COVER = (5.0, 8.0), 2.0, 0.8, 1.5
RIM_BOT = -0.4
AREA_K = math.sqrt(16 * math.sin(2 * math.pi / 16) / 2 / math.pi)    # 0.98716: area-equivalent radius of a 16-gon


def depth_at(L, z):
    """StageLayout.DepthAt (Assets/Scripts/Core/Art.cs) without the tide (the ice has none)."""
    zs, vs = L["depthZ"], L["depthV"]
    if z <= zs[0]:
        return float(vs[0])
    for i in range(1, len(zs)):
        if z <= zs[i]:
            return float(vs[i - 1] + (vs[i] - vs[i - 1]) * (z - zs[i - 1]) / (zs[i] - zs[i - 1]))
    return float(vs[-1])


def _disc(ctx, kind, oid, c, r, z0, z1, **props):
    """A vertical 16-gon prism of the circle's area (Blender axes) between z0 and z1."""
    rr = r / AREA_K
    n = 16
    v = [(c[0] + rr * math.cos(2 * math.pi * k / n), c[1] + rr * math.sin(2 * math.pi * k / n), z)
         for z in (z0, z1) for k in range(n)]
    f = [tuple(range(n))[::-1], tuple(range(n, 2 * n))] + [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    return ctx["helper"](kind, oid, v, f, shape="circle", **props)


def extra(ctx):
    if ctx["layer"] != "back":
        return []
    import numpy as np
    L, smod = ctx["L"], ctx["smod"]
    out = []
    # ---- the rim: the funnel's exact bottom ring (y = 0 in game space)
    fun = [ob for ob in ctx["scene"].objects if ob.type == "MESH" and ob.name.split(".")[0] == "Funnel"]
    if len(fun) != 1:
        raise SystemExit("HYB OBST ERROR ice: expected one Funnel in the back layer, found %d" % len(fun))
    ob = fun[0]
    M = np.array(ob.matrix_world)
    co = np.array([tuple(v.co) for v in ob.data.vertices], np.float64) @ M[:3, :3].T + M[:3, 3]
    ring = co[co[:, 2] <= co[:, 2].min() + 1e-4]
    n = len(ring)
    ang = np.arctan2(ring[:, 1] - ring[:, 1].mean(), ring[:, 0] - ring[:, 0].mean())
    ring = ring[np.argsort(ang)]
    out.append(ctx["helper"]("rim", "hole", [tuple(p) for p in ring], [tuple(range(n))], mat="ice", bot=RIM_BOT,
                             top=float(smod.ICE_Y), shape="circle"))
    # ---- the gravel under the hole (bed .. bed + 0.3 at the hole's centre)
    hx, hz = float(L["holeX"]), float(L["holeZ"])
    bed = -depth_at(L, hz)
    out.append(_disc(ctx, "snag", "gravel", (hx, hz), GRAVEL_R, bed - 0.3, bed + GRAVEL_UP, mat="gravel",
                     tags="gravel", bot=-99.0, top=round(bed + GRAVEL_UP, 3)))
    # ---- the rock pile (cover for 모캐)
    bed = -depth_at(L, PILE_C[1])
    out.append(_disc(ctx, "snag", "pile", PILE_C, PILE_R, bed - 0.3, bed + PILE_UP, mat="rock", tags="rock,abrasive",
                     bot=-99.0, top=round(bed + PILE_UP, 3), cover=PILE_COVER, cover_for="rock"))
    return out


# ---------------------------------------------------------------------------------------------------- the checks
def _X():
    """The running exporter (hyb_obstacles.py is Blender's __main__; importing it again is harmless)."""
    import sys
    m = sys.modules.get("__main__")
    if m is not None and hasattr(m, "Cam") and hasattr(m, "edge_dist"):
        return m
    import importlib
    return importlib.import_module("hyb_obstacles")


PREVIEW_DIR = None      # set in check(): Tools/Blender/_tmp/obstacles


def check(ctx):
    import os
    import numpy as np
    import importlib
    X = _X()
    R = X.R
    L, log = ctx["L"], ctx["log"]
    obst = {e["id"]: e for e in ctx["obstacles"]}
    cam = X.Cam(L)
    W, H = cam.W, cam.H
    hx, hz, hr = float(L["holeX"]), float(L["holeZ"]), float(L["holeR"])
    rim = obst.get("hole")
    if rim is None:
        log("CHECK ice rim FAIL: no rim exported")
        return
    pts = np.array(rim["pts"]).reshape(-1, 2)
    circ = float(np.mean(np.linalg.norm(pts - [rim["x"], rim["z"]], axis=1)))
    log("CHECK ice rim: centre (%.3f, %.3f) vs hole (%.3f, %.3f) off %.4f m; fitted radius (its 16-gon's corners) %.4f "
        "vs holeR %.3f; JSON rad %.3f (the exporter's area-equivalent radius of the 16-gon); bot %.2f top %.2f"
        % (rim["x"], rim["z"], hx, hz, math.hypot(rim["x"] - hx, rim["z"] - hz), circ, hr, rim["rad"], rim["bot"],
           rim["top"]))
    # ---- the rim projected at the water plane vs the painted open water in the hole (ice_back.png)
    smod = importlib.import_module("hyb_ice")
    back = R.load_png(os.path.join(X.C.SPRITES, "Stages", "ice_back.png"))
    rgb = np.round(back[..., :3] * 255).astype(int)
    hole_cols = [tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) for c in smod.HOLE]
    painted = np.zeros((H, W), bool)
    for c in hole_cols:
        painted |= np.all(rgb == np.array(c)[None, None, :], -1)
    ang = np.linspace(0, 2 * math.pi, 256, endpoint=False)

    def circle_mask(cx, cz, r):
        p3 = np.stack([cx + r * np.cos(ang), np.zeros_like(ang), cz + r * np.sin(ang)], -1)
        return X.poly_mask(cam.px(p3)[:, :2], W, H)
    near = circle_mask(hx, hz, hr + 0.4)
    painted &= near                                         # the same colours elsewhere (far ice) do not count
    for name, r in (("fitted", circ), ("JSON rad", rim["rad"])):
        m = circle_mask(rim["x"], rim["z"], r)
        iou = float((m & painted).sum()) / max(1, int((m | painted).sum()))
        em, ex = X.edge_dist(m, painted)
        py, px_ = np.argwhere(painted).mean(0)
        qy, qx = np.argwhere(m).mean(0)
        log("CHECK ice rim %-8s r %.3f vs painted hole: %d px painted, %d px circle, IoU %.3f, edge %.2f / max %.2f px, "
            "centre offset %.2f px" % (name, r, int(painted.sum()), int(m.sum()), iou, em, ex, math.hypot(qx - px_, qy - py)))
    # ---- the helpers: in the water, above the bed, within reach
    for oid in ("gravel", "pile", "pile.cover"):
        e = obst.get(oid)
        if e is None:
            log("CHECK ice %s FAIL: missing" % oid)
            continue
        p = np.array(e["pts"]).reshape(-1, 2)
        beds = [-depth_at(L, z) for z in p[:, 1]]
        log("CHECK ice %-10s %-5s centre (%.2f, %.2f) rad %.3f; plan x %.2f..%.2f z %.2f..%.2f; band bed(%.2f..%.2f) .. "
            "top %.2f; in the water: %s" % (oid, e["kind"], e["x"], e["z"], e["rad"], p[:, 0].min(), p[:, 0].max(),
                                            p[:, 1].min(), p[:, 1].max(), min(beds), max(beds), e["top"],
                                            abs(p[:, 0]).max() <= L["xLim"] and p[:, 1].min() >= L["zNear"] - 1
                                            and e["top"] < 0))
        if e["kind"] == "cover":
            par = obst[e["parent"]]
            pp = np.array(par["pts"]).reshape(-1, 2)
            from_hole = X.seg_hits(pp, (hx, hz), (e["hx"], e["hz"]))
            log("CHECK ice %s hold point (%.2f, %.2f): %.2f m from the hole; the line from the HOLE (the ice fight's "
                "anchor) crosses the pile: %s; holdCross (from the feet) %s"
                % (oid, e["hx"], e["hz"], math.hypot(e["hx"] - hx, e["hz"] - hz), from_hole, e["holdCross"]))
    # ---- close-up preview: the hole (rim, gravel, pile + cover) on the painted stage, 4x
    front = R.load_png(os.path.join(X.C.SPRITES, "Stages", "ice_front.png"))
    base = back.copy()
    R.over(base, front, 0, 0)
    corners = []
    for e in ctx["obstacles"]:
        p = np.array(e["pts"]).reshape(-1, 2)
        y = e["top"] if e["kind"] in ("snag", "cover", "weed") else 0.0
        p3 = np.stack([p[:, 0], np.full(len(p), y), p[:, 1]], -1)
        if y < 0:
            p3 = cam.apparent(p3)
        corners.append(cam.px(p3)[:, :2])
    allp = np.concatenate(corners)
    c0, c1 = max(0, int(allp[:, 0].min()) - 10), min(W, int(allp[:, 0].max()) + 11)
    r0, r1 = max(0, int(allp[:, 1].min()) - 10), min(H, int(allp[:, 1].max()) + 11)
    k = 4
    img = R.upscale(base[r0:r1, c0:c1], k).copy()
    for e in sorted(ctx["obstacles"], key=lambda e: {"cover": 0, "snag": 1, "rim": 2}.get(e["kind"], 3)):
        p = np.array(e["pts"]).reshape(-1, 2)
        y = e["top"] if e["kind"] != "rim" else 0.0
        if e["kind"] == "rim":
            a = np.linspace(0, 2 * math.pi, 128, endpoint=False)
            p = np.stack([e["x"] + circ * np.cos(a), e["z"] + circ * np.sin(a)], -1)
        p3 = np.stack([p[:, 0], np.full(len(p), y), p[:, 1]], -1)
        if y < 0:
            p3 = cam.apparent(p3)
        sp = (cam.px(p3)[:, :2] - [c0, r0]) * k
        X._poly_line(img, [tuple(q) for q in sp], X.COL[e["kind"]], 0.95, dash=None if e["kind"] == "rim" else 4)
        if e["kind"] == "cover":
            h = (cam.px(cam.apparent(np.array([e["hx"], -0.5, e["hz"]])))[:2] - [c0, r0]) * k
            for d in range(-5, 6):
                X._put(img, h[0] + d, h[1], X.COL["cover"])
                X._put(img, h[0], h[1] + d, X.COL["cover"])
    out = os.path.join(X.C.ROOT, "Tools", "Blender", "_tmp", "obstacles", "obstacles_ice_closeup.png")
    R.save_png(img, out)
    log("ice: close-up %s" % out)
