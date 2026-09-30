"""
TEMPLATE obstacle module - copy to obstacles/<stage>.py (Docs/obstacles_spec.md 3 = your stage's list, 13 = the API).
Files starting with "_" are skipped by --all. Owner: the stage's agent; nobody else edits hyb_obstacles.py.

The exporter (hyb_obstacles.py) imports hyb_<stage>.py, runs the render functions named in LAYERS with their seeds
(so the scene is the painted one, object for object) and stops each at its first render pass. Then:
  1. RULES stamp the obst_* custom properties on the matching objects (first matching rule wins; obst=None = never an
     obstacle). Match keys: name (base-name prefix or list of prefixes; "Mid" also takes "MidMoss"), kind (the
     object's "kind" tag), grp (regex on its "grp" tag), where (callable(ob) -> bool), layer ("front" / "back";
     default: every layer). Every other key k becomes the property obst_<k> (see hyb_obstacles.py's docstring):
       obst (solid | pad | snag | weed | cover | rim), id, mat, tags, tiers, shape, skirt, skirt_top, cover, cover_for,
       weed, weed_top, top, bot, grabK, roughK
     id may use {grp} / {name} / {kind} (e.g. id="{grp}.crown" splits a cypress's crown from its trunk).
  2. extra(ctx) may add EXPORT-ONLY helper objects (underwater things that are not painted: sunken logs, weed beds,
     rock piles) with ctx["helper"](kind, id, verts, faces, **props) or ctx["helper_box"](kind, id, centre, size,
     rot_z=0, **props) - Blender axes (x right, y forward, z up) like every stage script; place them relative to
     the painted props (read ctx["smod"], the stage module, for its constants) and keep them in the water.
  3. Every object with an "obst" property becomes (part of) an obstacle; objects sharing obst_id (default: their "grp"
     tag) and kind are one obstacle. Derived zones: obst_skirt -> "<id>.skirt" (snag), obst_cover -> "<id>.cover"
     (fish cover with a hold point), obst_weed -> "<id>.weed" (weed bed).
  4. Obstacles that never touch the fishable water (|x| <= xLim + 0.3, zNear - 1 .. zFar) are dropped.
  5. check(ctx) (optional) prints stage-specific checks after the export (ctx: stage, obstacles, L, log).

Run: blender -b --python Tools/Blender/variants/hybrid/hyb_obstacles.py -- <stage> [--dry] [--no-check]
"""
STAGE = "_template"

# (layer, render function of hyb_<stage>.py, the seed its main() passes) - copy them from the stage's main()
LAYERS = [("front", "render_front", 0)]

RULES = [
    dict(name="AnglerRock", obst=None),                                  # the ground under his feet
    dict(name="Boulder", obst="solid", mat="rock", tiers=3, skirt=0.5, cover=1.5, tags="rock,abrasive"),
    dict(kind="post", obst="solid", mat="wood", tags="post,abrasive", cover=0.8, cover_for="post"),
    dict(kind="pad", obst="pad", mat="pad", weed=0.8, tags="pad"),
    dict(kind="reed", obst="weed", mat="reed", top=-0.05, bot=-99.0, tags="reed"),
]


def extra(ctx):
    """Export-only helpers. Example: a sunken log 1.8 m under the surface in front of a painted stump."""
    h = ctx["helper_box"]
    return [h("snag", "sunklog1", (3.0, 22.0, -1.8), (4.0, 0.35, 0.35), rot_z=0.4, mat="wood", tags="log,abrasive",
              top=-1.2, bot=-99.0)]


def check(ctx):
    pass
