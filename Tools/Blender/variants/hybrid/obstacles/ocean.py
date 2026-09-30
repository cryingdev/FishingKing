"""
obstacles/ocean.py - the OCEAN's obstacles (Docs/obstacles_spec.md 3.7). Owner: B3 (was B2 in 13.6).

The angler stands on the bow of a boat far out at sea: nothing painted is in the water (the deck, bulwarks, pulpit,
cooler, life ring, coil and rod are the boat he stands on; the cast leaves the rod tip beyond the bow: spec 1.3).
Everything the ocean exports is an export-only helper (never painted: only ever seen as the aim outlines):
  hull   snag hull   the hull under the painted bow: plan = hyb_ocean.outline(inset=0) (the gunwale outline the stage
                     builds the bulwark / cap rail / deck from, Blender x, y -> game x, z), y -1.4 .. 0; abrasive.
                     "front" layer (the ocean's front layer bobs with the swell: spec 9.3).
         skirt       the ring round it (snag, top -0.35): 1.5 m, not the spec's 1.0 - see SKIRT below.
         cover       grown 1.2 m, cover type "hull" (방어 / 참다랑어 / 백상아리 dive under the bow): the hold point lies
                     in front of the stem, inside the skirt.
  The hull and its zones all lie under the painted bow (z <= 2.7): the aim outlines never show them. The open water
  gets its own structure, in view and within every rod's cast (REEF / KELP below):
  reef   snag rock   a submerged pinnacle (수중여) left of the bow, centre (-6.5, 16.0), ~2.2 m across its top, from the
                     bed up to 2.2 m under the surface; abrasive. Sinking lures (spoon, jig, egi) and float rigs set
                     deeper than 2.2 m snag on it; a crank (runs to 2.5 m) grazes its top (the 딱! deflection).
         cover       grown 1.5 m, cover type "rock" (방어 hold on the reef: the line rubs on the rock).
  kelp   weed weed   a drifting mat of sargassum weed (모자반 떼) right of the bow, centre (6.2, 11.5), ~5.2 x 2.8 m,
                     from 1.4 m under the surface up to the film (top -0.05: surface lures and the kona catch it too).
         cover       grown 1.0 m, cover type "weed" (만새기 / 참다랑어 hang under floating weed); not abrasive.
  Both keep off the straight-ahead lane (|x| < 3 m) and lie short of the legends' lurk band (z 20-45, 6 m deep), and
  their outlines (drawn at the zone's top, refracted) fall in open water, clear of the painted bow. (A structure on the
  bed would read wrongly: 16 m down, refraction draws its outline some 10 m nearer, on top of the kelp.)
SKIRT: the exporter puts a cover's hold point on the skirt ring 0.6 x skirt out from the footprint and only inside the
fight's limits with a margin (z >= zNear + 0.5 = 2.1). The stem is at z 1.25, so a 1.0 m skirt puts it at z 1.85 and
the exporter writes no cover at all; 1.5 m puts it at z 2.15 (inside the skirt, which then reaches z 2.75, so a fish
holding there rubs the line on the hull: spec 7.3 / 7.4 case 2).
"""
import math

STAGE = "ocean"

# hyb_ocean.main(): render_front(random.Random(31)), then render_back(random.Random(77)). The back layer is the open
# sea and the far silhouettes (islands at 1-4 km, a ship at 2 km): nothing within reach, so only the front is built.
LAYERS = [("front", "render_front", 31)]

RULES = [
    # the boat he stands on and everything on it: not obstacles (spec 1.3)
    dict(kind=["deck", "plank", "margin", "bulwark", "cap", "rail", "prop", "ring", "rod"], obst=None),
]

HULL_BOT = -1.4
SKIRT, SKIRT_TOP, COVER = 1.5, -0.35, 1.2

# the open-water structure: (centre (x, z), plan radii of an irregular outline (radius, angle deg), rotation)
REEF_C, REEF_TOP, REEF_COVER = (-6.5, 16.0), -2.2, 1.5
REEF_R = [1.25, 1.05, 1.2, 0.95, 1.1, 1.3, 1.0, 1.15, 1.2, 0.9, 1.05, 1.2]
KELP_C, KELP_AX, KELP_ROT, KELP_BOT, KELP_TOP, KELP_COVER = (6.2, 11.5), (2.6, 1.4), -18.0, -1.4, -0.05, 1.0
KELP_R = [1.0, 0.92, 1.05, 0.9, 0.97, 1.0, 0.88, 1.04, 0.95, 1.0, 0.9, 1.02, 0.96, 0.9, 1.0, 0.94]


def depth_at(L, z):
    """StageLayout.DepthAt (Assets/Scripts/Core/Art.cs) without the tide (the ocean has none)."""
    zs, vs = L["depthZ"], L["depthV"]
    if z <= zs[0]:
        return float(vs[0])
    for i in range(1, len(zs)):
        if z <= zs[i]:
            return float(vs[i - 1] + (vs[i] - vs[i - 1]) * (z - zs[i - 1]) / (zs[i] - zs[i - 1]))
    return float(vs[-1])


def _prism(ctx, kind, oid, ring, z0, z1, **props):
    """A vertical prism (Blender axes) over the plan ring (x, y) between z0 and z1."""
    n = len(ring)
    v = [(x, y, z) for z in (z0, z1) for (x, y) in ring]
    f = [tuple(range(n))[::-1], tuple(range(n, 2 * n))] + [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    return ctx["helper"](kind, oid, v, f, **props)


def _blob(c, radii, ax=(1.0, 1.0), rot_deg=0.0):
    """An irregular closed outline round c: radius radii[k] x the ellipse axes, rotated."""
    n = len(radii)
    cs, sn = math.cos(math.radians(rot_deg)), math.sin(math.radians(rot_deg))
    out = []
    for k, r in enumerate(radii):
        a = 2 * math.pi * k / n
        x, y = ax[0] * r * math.cos(a), ax[1] * r * math.sin(a)
        out.append((c[0] + x * cs - y * sn, c[1] + x * sn + y * cs))
    return out


def extra(ctx):
    smod, L = ctx["smod"], ctx["L"]
    out = []
    # ---- the hull under the bow
    pts = smod.outline(inset=0.0)                       # the gunwale outline (Blender x, y), clockwise from aft-left
    n = len(pts)
    v = [(x, y, z) for z in (HULL_BOT, 0.0) for (x, y) in pts]
    f = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))] + [(k, k + 1, n + k + 1, n + k) for k in range(n - 1)] \
        + [(n - 1, 0, n, 2 * n - 1)]
    out.append(ctx["helper"]("snag", "hull", v, f, mat="hull", tags="hull,abrasive", bot=HULL_BOT, top=0.0,
                             skirt=SKIRT, skirt_top=SKIRT_TOP, cover=COVER, cover_for="hull"))
    # ---- the reef pinnacle: bed .. 2.2 m under the surface
    bed = -depth_at(L, REEF_C[1])
    out.append(_prism(ctx, "snag", "reef", _blob(REEF_C, REEF_R), bed - 0.3, REEF_TOP, mat="rock",
                      tags="rock,abrasive,reef", top=REEF_TOP, bot=-99.0, cover=REEF_COVER, cover_for="rock"))
    # ---- the drifting weed mat: 1.4 m deep .. the film
    out.append(_prism(ctx, "weed", "kelp", _blob(KELP_C, KELP_R, KELP_AX, KELP_ROT), KELP_BOT, KELP_TOP, mat="weed",
                      tags="weed,kelp", top=KELP_TOP, bot=KELP_BOT, cover=KELP_COVER, cover_for="weed"))
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
    """The hull footprint vs the painted bow: the outline (game x, z) lifted to the top of the cap rail and projected with
    the JSON camera, against (a) the rebuilt bulwark + cap rail + deck raster and (b) the painted front layer."""
    import os
    import math
    import importlib
    import numpy as np
    import bpy
    X = _X()
    R = X.R
    L, log = ctx["L"], ctx["log"]
    smod = importlib.import_module("hyb_ocean")
    obst = {e["id"]: e for e in ctx["obstacles"]}
    cam = X.Cam(L)
    W, H = cam.W, cam.H
    hull = obst["hull"]
    foot = np.array(hull["pts"]).reshape(-1, 2)
    ref = np.array(smod.outline(inset=0.0))
    d = [min(float(np.min(np.linalg.norm(ref - p, axis=1))), 9) for p in foot]
    log("CHECK ocean hull footprint vs hyb_ocean.outline(inset=0): %d corners, max off %.4f m; x %.2f..%.2f z %.2f..%.2f"
        % (len(foot), max(d), foot[:, 0].min(), foot[:, 0].max(), foot[:, 1].min(), foot[:, 1].max()))
    # (a) raster of the rebuilt hull parts (the current scene is the front layer) through the JSON camera
    tris, ids = [], []
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or ob.hide_render or ob.get("kind") not in ("deck", "plank", "margin", "bulwark", "cap"):
            continue
        g, tri = X.mesh_data(ob)
        if len(tri):
            tris.append(cam.px(g)[tri])
            ids.append(np.zeros(len(tri), np.int32))
    ib, _ = X.raster(np.concatenate(tris), np.concatenate(ids), W, H)
    body = ib >= 0
    gun_top = smod.GUN + 0.065                          # top of the navy cap rail
    prism = np.concatenate([np.stack([foot[:, 0], np.full(len(foot), y), foot[:, 1]], -1)
                            for y in (smod.STAND - 0.06, gun_top)])
    P = X.poly_mask(cam.px(prism)[:, :2], W, H)
    iou = float((P & body).sum()) / max(1, int((P | body).sum()))
    em, ex = X.edge_dist(P, body)
    log("CHECK ocean hull outline at the gunwale (y %.2f..%.2f) vs the rebuilt bulwark + cap + deck raster: IoU %.3f, "
        "edge %.2f / max %.2f px (the cap rail's outer edge is 0.04 m outside the gunwale line)" %
        (smod.STAND - 0.06, gun_top, iou, em, ex))
    # (b) vs the painted front layer: the outline's projected edge vs the nearest edge of the painted hull body (the
    # painted pixels on the rebuilt body + its 1 px outline ring; the pulpit rails, life ring and rod are not the hull)
    front = R.load_png(os.path.join(X.C.SPRITES, "Stages", "ocean_front.png"))
    A = front[..., 3] > 0.5
    ea = X.edge(A & X._nb4_or(body))
    samp = []
    for k in range(len(foot)):
        a, b = foot[k - 1], foot[k]
        for t in np.linspace(0, 1, 12, endpoint=False):
            samp.append(a + (b - a) * t)
    samp = np.array(samp)
    s3 = np.stack([samp[:, 0], np.full(len(samp), gun_top), samp[:, 1]], -1)
    sp = cam.px(s3)[:, :2]
    vis = (sp[:, 0] >= 0) & (sp[:, 0] < W) & (sp[:, 1] >= 0) & (sp[:, 1] < H)
    dd = np.sqrt(((sp[vis][:, None, [1, 0]] - (ea[None, :, :] + 0.5)) ** 2).sum(-1)).min(1)
    log("CHECK ocean hull outline at the cap-rail top vs the painted hull body (ocean_front.png): %d samples on "
        "screen, distance to its edge mean %.2f px, max %.2f px" % (int(vis.sum()), dd.mean(), dd.max()))
    # the zones
    for oid in ("hull.skirt", "hull.cover"):
        e = obst.get(oid)
        if e is None:
            log("CHECK ocean %s FAIL: missing" % oid)
            continue
        p = np.array(e["pts"]).reshape(-1, 2)
        log("CHECK ocean %-10s %-5s x %.2f..%.2f z %.2f..%.2f top %.2f" % (oid, e["kind"], p[:, 0].min(), p[:, 0].max(),
                                                                        p[:, 1].min(), p[:, 1].max(), e["top"]))
        if e["kind"] == "cover":
            sk = np.array(obst["hull.skirt"]["pts"]).reshape(-1, 2)
            log("CHECK ocean hull.cover hold point (%.2f, %.2f): inside the skirt %s, inside the cover %s, fight limits "
                "(z >= zNear + 0.2 = %.2f) %s" % (e["hx"], e["hz"], X.inside(sk, (e["hx"], e["hz"])),
                                                  X.inside(p, (e["hx"], e["hz"])), L["zNear"] + 0.2,
                                                  e["hz"] >= L["zNear"] + 0.2))
    # the open-water structure: where the aim outlines draw it (the zone at its top, refracted) vs the painted bow
    for oid in ("reef", "reef.cover", "kelp", "kelp.cover"):
        e = obst.get(oid)
        if e is None:
            log("CHECK ocean %s FAIL: missing" % oid)
            continue
        p = np.array(e["pts"]).reshape(-1, 2)
        samp = np.concatenate([p[k - 1] + (p[k] - p[k - 1]) * t[:, None]
                               for k in range(len(p)) for t in [np.linspace(0, 1, 16, endpoint=False)]])
        y = min(0.0, e["top"])
        q = cam.px(cam.apparent(np.stack([samp[:, 0], np.full(len(samp), y), samp[:, 1]], -1)))[:, :2]
        on = (q[:, 0] >= 0) & (q[:, 0] < W) & (q[:, 1] >= 0) & (q[:, 1] < H)
        qi = q[on].astype(int)
        deck = int(A[qi[:, 1], qi[:, 0]].sum()) if len(qi) else 0
        near = float(np.min(np.hypot(p[:, 0], p[:, 1])))
        extra_ = ""
        if e["kind"] == "cover":
            extra_ = " hold (%.2f, %.2f) cross %s for %s" % (e["hx"], e["hz"], e["holdCross"], ",".join(e["coverFor"]))
        ok = on.all() and (deck == 0 or e["kind"] == "cover")      # (covers are not outlined while aiming)
        log("CHECK ocean %-11s %s %-5s %-4s x %.2f..%.2f z %.2f..%.2f y %.2f..%.2f, %.1f m from him at the nearest; "
            "outline on screen %d/%d samples (px x %.0f..%.0f y %.0f..%.0f), on the painted bow %d%s"
            % (oid, "PASS" if ok else "FAIL", e["kind"], e["mat"], p[:, 0].min(), p[:, 0].max(), p[:, 1].min(),
               p[:, 1].max(), e["bot"], e["top"], near, int(on.sum()), len(q), q[:, 0].min(), q[:, 0].max(),
               q[:, 1].min(), q[:, 1].max(), deck, extra_))
    # close-up preview of the bow (4x): the hull at the gunwale (yellow, the painted bow edge it follows) and the
    # underwater zones at their apparent tops as the game would draw them
    back = R.load_png(os.path.join(X.C.SPRITES, "Stages", "ocean_back.png"))
    base = back.copy()
    R.over(base, front, 0, 0)
    c0, c1, r0, r1 = 160, 480, 200, 400
    k = 3
    img = R.upscale(base[r0:r1, c0:c1], k).copy()
    for e in sorted(ctx["obstacles"], key=lambda e: {"cover": 0, "snag": 1}.get(e["kind"], 2)):
        p = np.array(e["pts"]).reshape(-1, 2)
        y = e["top"]
        p3 = cam.apparent(np.stack([p[:, 0], np.full(len(p), y), p[:, 1]], -1)) if y < 0 else \
            np.stack([p[:, 0], np.full(len(p), y), p[:, 1]], -1)
        X._poly_line(img, [tuple(q) for q in (cam.px(p3)[:, :2] - [c0, r0]) * k], X.COL[e["kind"]], 0.95, dash=4)
        if e["kind"] == "cover":
            h = (cam.px(cam.apparent(np.array([e["hx"], -0.5, e["hz"]])))[:2] - [c0, r0]) * k
            for dd_ in range(-5, 6):
                X._put(img, h[0] + dd_, h[1], X.COL["cover"])
                X._put(img, h[0], h[1] + dd_, X.COL["cover"])
    g3 = np.stack([foot[:, 0], np.full(len(foot), gun_top), foot[:, 1]], -1)
    X._poly_line(img, [tuple(q) for q in (cam.px(g3)[:, :2] - [c0, r0]) * k], X.COL["solid"], 0.9)
    out = os.path.join(X.C.ROOT, "Tools", "Blender", "_tmp", "obstacles", "obstacles_ocean_closeup.png")
    R.save_png(img, out)
    log("ocean: close-up %s" % out)
