"""
obstacles/stream.py - the STREAM's obstacles (Docs/obstacles_spec.md 3.2). Owner: the stream agent.

Everything hyb_stream.py paints in the water is a granite boulder of its FRONT layer (the back layer's bank boulders,
shrubs and trees stand on the gravel beach and the slopes, outside the fishable channel |x| <= 7.5):
  Mid    x5  the boulders hugging the sides of the channel (x ~ +-6, z 13.5 .. 92): above-water colliders (3 tiers: they
             are domes), an underwater snag skirt (their bodies go on under the surface: the painted boulder is clipped
             at the waterline), a cover for rock-holding fish, and the slack pockets of CurrentField (tag "pocket")
  Bank   ~20 the waterline boulders along both banks (clusters of 1-3): colliders, a narrower skirt, a smaller cover
  Flank  x3  the stones flanking the angler's boulder, in the water at his feet: colliders only ("near": not a cover,
             casts never reach them; they only matter for a fish brought in close)
  AnglerRock  his boulder: not an obstacle
Tufts (grass on the bank tops) are not obstacles.
"""
STAGE = "stream"

# hyb_stream.main(): render_front(random.Random(31)), then render_back(random.Random(77)). Only the front holds
# anything in the water, so only the front is built.
LAYERS = [("front", "render_front", 31)]

RULES = [
    dict(name="AnglerRock", obst=None),
    dict(name="Mid", obst="solid", mat="rock", tiers=3, skirt=0.5, skirt_top=-0.35, cover=1.6, cover_for="rock",
         tags="rock,abrasive,midstream,pocket"),
    dict(name="Bank", obst="solid", mat="rock", tiers=3, skirt=0.35, skirt_top=-0.35, cover=1.1, cover_for="rock",
         tags="rock,abrasive,bank"),
    dict(name="Flank", obst="solid", mat="rock", tiers=4, skirt=0.25, skirt_top=-0.35, tags="rock,abrasive,near"),
]

# CurrentField.Rocks (Assets/Scripts/Fishing/CurrentField.cs): the pockets are hard-coded from hyb_stream.py's "Mid"
# boulders (x, z, r). The check below compares them with the export (the spec moves them to the JSON's "pocket" tag).
POCKET_ROCKS = [(-6.35, 13.5, 0.62), (6.3, 25.0, 0.78), (-6.1, 44.0, 1.0), (5.9, 66.0, 1.15), (-6.0, 92.0, 1.2)]


def extra(ctx):
    """No export-only helpers: every stream obstacle is a painted boulder (or its underwater body)."""
    return []


def check(ctx):
    mids = [e for e in ctx["obstacles"] if e["kind"] == "solid" and "pocket" in e["tags"]]
    for (x, z, r) in POCKET_ROCKS:
        best = min(mids, key=lambda e: (e["x"] - x) ** 2 + (e["z"] - z) ** 2)
        ctx["log"]("CHECK pocket rock (%.2f, %.2f, r %.2f) -> %s centre (%.2f, %.2f) off %.2f m, footprint r %.2f"
                   % (x, z, r, best["id"], best["x"], best["z"], ((best["x"] - x) ** 2 + (best["z"] - z) ** 2) ** 0.5,
                      best["r"]))
