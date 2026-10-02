using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// A shallow area of the bed (Docs/terrain_depth_spec.md 4.2, step 1): a capsule a-b of radius r (x a random scale,
    /// its rim wobbled by noise) whose inside is set to a depth drawn from <see cref="depth"/>, feathered over a band drawn
    /// from <see cref="edge"/>; <see cref="gap"/> &gt; 0 keeps it that far from the lane. <see cref="flat"/> names the zone
    /// it belongs to (flatL, flatR, shoal).
    /// </summary>
    public struct Blob
    {
        public string id, flat;
        public Vector2 a, b;
        public float r;
        public Vector2 depth, edge;
        public float gap;
        public bool shoal;
    }

    /// <summary>What a pin holds (spec 5.1): a rect, an obstacle by id, weed zones by tag, every obstacle of a kind, the
    /// stems round the pads, the lane polygon, or any deep band zone not matched by another pin.</summary>
    public enum PinSel { Rect, Id, KindTag, Kind, StemOfPad, Lane, GenericBand }

    /// <summary>
    /// A constraint from the fixed art (spec 5.1): the nodes in the selected footprints grown by <see cref="grow"/> (the
    /// core) are clamped into [lo, hi] (a <see cref="profile"/> pin: the old profile's depth), the ring round them blended
    /// towards it over <see cref="blend"/> m. Higher <see cref="prio"/> wins where windows do not overlap.
    /// </summary>
    public struct Pin
    {
        public PinSel sel;
        public string id, kind, tag;
        public Rect rect;
        public float grow, lo, hi, blend;
        public bool profile;
        public int prio;
    }

    /// <summary>The old creek's numbers (step 2).</summary>
    public sealed class ChannelSpec
    {
        public Vector2 startX = new Vector2(-2.5f, 0.5f), startZ = new Vector2(17f, 20f), len = new Vector2(28f, 38f), amp = new Vector2(2.5f, 5f);
        public Vector2 halfWidth = new Vector2(1.5f, 2.5f), extra = new Vector2(0.6f, 1.0f);
        public float step = 8f;
    }

    /// <summary>Holes (step 3) or humps (step 4): how many, their size, shape and depth change.</summary>
    public sealed class FeatureSpec
    {
        public int countMin = 1, countMax = 2;
        public Vector2 radius, aspect, amount;
        /// <summary>The first one's centre region (z range, |x| bound; x 0 = from the visible half width) and the others'.</summary>
        public Vector2 z1, zN;
        public float x1, xNFrac;
        public int tries = 40;
    }

    /// <summary>A stage's terrain recipe (spec 3.3): every number of the generator. Bump <see cref="version"/> on any change.</summary>
    public sealed class TerrainRecipe
    {
        public string stage;
        public int version = 1;
        public float x0 = -48f, z0 = 0f;
        public int nx = 193, nz = 129;
        public float minDepth = 0.35f, maxDepth = 9.0f, maxSlope = 1.5f, feather = 6f;
        public int attempts = 8;
        public Blob[] blobs;
        public Vector2 shelfZ = new Vector2(5.5f, 8f);
        public float shelfWobble = 1.5f;
        public float noiseA = 0.30f, noiseScaleA = 14f, noiseB = 0.12f, noiseScaleB = 5f, noiseFlatDamp = 0.6f;
        public ChannelSpec channel;
        public FeatureSpec holes, humps;
        public Pin[] pins;
        /// <summary>The lane polygon (concave, counter-clockwise): deep open water in front of the pier.</summary>
        public Vector2[] lane;
    }

    /// <summary>The stages' terrain recipes: the lake only (phase 1); every other stage keeps its profile.</summary>
    public static class TerrainRecipes
    {
        public static TerrainRecipe For(string stageId) => stageId == "lake" ? Lake() : null;

        static Blob B(string id, string flat, float ax, float az, float bx, float bz, float r, float d0, float d1, float e0 = 2f, float e1 = 3.5f, float gap = 0f, bool shoal = false) =>
            new Blob { id = id, flat = flat, a = new Vector2(ax, az), b = new Vector2(bx, bz), r = r, depth = new Vector2(d0, d1), edge = new Vector2(e0, e1), gap = gap, shoal = shoal };

        static Pin P(PinSel sel, string key, float grow, float lo, float hi, float blend, int prio, string tag = null) =>
            new Pin { sel = sel, id = sel == PinSel.Id ? key : null, kind = sel == PinSel.Kind || sel == PinSel.KindTag ? key : null, tag = tag, grow = grow, lo = lo, hi = hi, blend = blend, prio = prio };

        /// <summary>
        /// The lake (spec 4.2, 5.1): shallow flats under the pads and reeds on both sides, a weed shoal under the submerged
        /// weed bed, a deep lane from the pier out past the sunken log (the legend's spot reachable with the bamboo rod),
        /// an old creek, 1-2 holes and 1-3 humps further out. The pins mirror the baked obstacle tops of
        /// Tools/Blender/variants/hybrid/obstacles/lake.py (sunklog bed 6.225, weedbed top -1.2): change both together.
        /// </summary>
        static TerrainRecipe Lake() => new TerrainRecipe
        {
            stage = "lake",
            version = 1,
            blobs = new[]
            {
                B("L1", "flatL", -40f, 3.0f, -7.5f, 3.2f, 3.2f, 0.6f, 1.0f),
                B("L2", "flatL", -7.5f, 5.0f, -9.8f, 13.5f, 3.2f, 1.3f, 1.8f),
                B("L3", "flatL", -6.6f, 14.0f, -7.4f, 18.0f, 2.4f, 1.0f, 1.6f, 1.4f, 2.0f, 1.2f),
                B("L4", "flatL", -8.4f, 18.5f, -11.8f, 23.6f, 3.0f, 1.0f, 1.7f),
                B("L5", "flatL", -13.2f, 17.1f, -13.2f, 17.1f, 3.0f, 0.6f, 1.1f),
                B("L6", "flatL", -40f, 12f, -15f, 21f, 8.0f, 1.2f, 2.0f),
                B("R1", "flatR", 7.5f, 3.2f, 40f, 3.0f, 3.2f, 0.6f, 1.0f),
                B("R2", "flatR", 6.8f, 4.5f, 7.2f, 14.2f, 3.0f, 0.9f, 1.5f),
                B("R3", "shoal", 5.4f, 13.0f, 2.8f, 14.4f, 2.2f, 2.5f, 2.9f, 1.2f, 1.2f, 1.3f, true),
                B("R4", "flatR", 10.0f, 22.0f, 14.6f, 29.0f, 3.4f, 1.0f, 1.7f),
                B("R5", "flatR", 15f, 10f, 40f, 19f, 8.0f, 1.2f, 2.0f),
            },
            channel = new ChannelSpec(),
            holes = new FeatureSpec
            {
                countMin = 1, countMax = 2, radius = new Vector2(2f, 3.5f), aspect = new Vector2(1f, 1.5f), amount = new Vector2(1.0f, 1.8f),
                z1 = new Vector2(18f, 32f), x1 = 6f, zN = new Vector2(30f, 50f), xNFrac = 0.8f,
            },
            humps = new FeatureSpec
            {
                countMin = 1, countMax = 3, radius = new Vector2(3f, 6f), aspect = new Vector2(1f, 1.8f), amount = new Vector2(2.4f, 4.0f),
                z1 = new Vector2(30f, 38f), zN = new Vector2(30f, 52f),
            },
            pins = new[]
            {
                new Pin { sel = PinSel.Rect, id = "pier", rect = Rect.MinMaxRect(-3.5f, 0f, 3.5f, 3.5f), profile = true, blend = 3.0f, prio = 10 },
                P(PinSel.Id, "sunklog", 0.3f, 6.125f, 6.325f, 2.0f, 9),
                P(PinSel.Id, "weedbed", 0.2f, 2.4f, 3.0f, 1.5f, 8),
                P(PinSel.KindTag, "weed", 0.3f, 0.35f, 1.2f, 2.0f, 7, "reed"),
                P(PinSel.Id, "boat", 0.4f, 1.2f, 2.5f, 2.0f, 6),
                P(PinSel.Id, "boat.skirt", 0.4f, 1.2f, 2.5f, 2.0f, 6),
                P(PinSel.Id, "stake", 0.3f, 0.8f, 2.5f, 1.5f, 6),
                P(PinSel.Id, "stake.skirt", 0.3f, 0.8f, 2.5f, 1.5f, 6),
                P(PinSel.Kind, "pad", 0.3f, 1.0f, 2.0f, 1.5f, 5),
                new Pin { sel = PinSel.StemOfPad, id = "stems", grow = 0f, lo = 0.8f, hi = 2.2f, blend = 1.5f, prio = 4 },
                new Pin { sel = PinSel.Lane, id = "lane", lo = 4.5f, hi = 9.0f, blend = 1.0f, prio = 3 },
                new Pin { sel = PinSel.GenericBand, id = "generic", grow = 0f, lo = 0.15f, hi = 9.0f, blend = 1.0f, prio = 2 },
            },
            lane = new[]
            {
                new Vector2(-3.4f, 12.5f), new Vector2(-0.9f, 12.5f), new Vector2(-0.9f, 16.9f), new Vector2(1.5f, 18.6f), new Vector2(4.6f, 21.0f),
                new Vector2(5.2f, 30.0f), new Vector2(-4.9f, 30.0f), new Vector2(-5.2f, 25.0f), new Vector2(-4.6f, 19.4f), new Vector2(-3.4f, 18.6f),
            },
        };
    }
}
