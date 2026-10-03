using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>The bed's material under a grid node (Docs/terrain_depth_spec.md 4.2, step 11).</summary>
    public enum BedMat : byte { Mud, Sand, Gravel, Weed }

    /// <summary>The bed's form under a grid node; its index is the column of the habitat tables (FishHabitat).</summary>
    public enum BedKind : byte { Open, Shelf, Flat, Shoal, Dropoff, Hump, Hole, Channel, Basin }

    /// <summary>Per-node flags: on a slope (Edge), on a drop-off beside weed (WeedEdge), in a pin's core, in the lane.</summary>
    [Flags] public enum BedFlag : byte { None = 0, Edge = 1, WeedEdge = 2, Pinned = 4, Lane = 8 }

    /// <summary>A named part of the bed (얕은 턱, 수중 둔덕 1, ...): its nodes and their stats.</summary>
    public sealed class BedZone
    {
        public string id, name;
        public BedKind kind;
        public Vector2 c;
        public float area, minD, maxD;
        public Rect box;
        public int[] nodes;
    }

    /// <summary>
    /// A lake's character as drawn (Docs/lake_phase2_spec.md A1): its four u (depth, weed, rock, side), the numbers derived
    /// from them and a label ("shallow weedy", "deep rocky", ...).
    /// </summary>
    public struct BedCharacter
    {
        public float uDepth, uWeed, uRock, uSide;
        /// <summary>The base's main depth, shelf end, drop width, side shoaling and its axis, far rise; the noise's scale; the lane's carve target.</summary>
        public float mainDepth, shelfEnd, dropWidth, side, sideX, farRise, noise, laneT;
        public int holes, humps, weedFlats;
        public string label;

        /// <summary>"shallow" / "mid" / "deep" by u_depth (thirds), then "weedy" / "rocky" / "mixed" by the larger of u_weed and u_rock (over 0.6).</summary>
        public static string Label(float ud, float uw, float ur)
        {
            string depth = ud < 1f / 3f ? "shallow" : ud > 2f / 3f ? "deep" : "mid";
            string bed = uw >= 0.6f && uw >= ur ? "weedy" : ur >= 0.6f ? "rocky" : "mixed";
            return depth + " " + bed;
        }
    }

    /// <summary>
    /// A stage's bed as data (Docs/terrain_depth_spec.md 3.2, Docs/lake_phase2_spec.md A1): a 0.5 m grid of depths (whole
    /// cm), kinds, materials, zones and flags over the fishable water and the art's overscan, generated at runtime from the
    /// save's world seed (<see cref="BathyGen"/>, the stage's <see cref="TerrainRecipe"/>). Nothing of it is drawn: the
    /// painted water stays as rendered. The bed is generated freely from the seed's character; its distance profile
    /// (<see cref="Profile"/>, the rows' medians) is derived from it and is the water off the grid. Only the lake has a
    /// recipe; every other stage keeps <see cref="StageLayout"/>'s authored profile exactly. Callers read depths through
    /// <see cref="StageLayout.DepthAt(float, float)"/>, which adds the tide; <see cref="Depth"/> is the mean water. The
    /// lake's statistics (<see cref="Quantile"/>, <see cref="NodeRankPct"/>, the shares) are over the reference region
    /// R_ref: z in [zNear + 1.5, 50], |x| within the 600 px view, every row weighing the same.
    /// </summary>
    public sealed class Bathymetry
    {
        // ------------------------------------------------------------------ switches (spec 13)
        /// <summary>-fkbathy off | log | show | dump (comma-separated; parsed in Game.DebugBoot).</summary>
        public static bool Off, Show, Dump, Log;

        static bool seedRead;
        static int? seedOverride;

        /// <summary>
        /// The world seed the terrain uses instead of the save's: -fkbathyseed &lt;n&gt;; else 1 whenever -fkauto is given
        /// (every test run sees the same lake); else null. Read from the command line itself (no boot-order race with the
        /// title scene); never written to the save.
        /// </summary>
        public static int? SeedOverride
        {
            get
            {
                if (!seedRead)
                {
                    seedRead = true;
                    var args = Environment.GetCommandLineArgs();
                    int i = Array.IndexOf(args, "-fkbathyseed");
                    if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int s)) seedOverride = s;
                    else if (Array.IndexOf(args, "-fkauto") >= 0) seedOverride = 1;
                }
                return seedOverride;
            }
        }

        static Bathymetry cached;
        static string cacheKey;

        /// <summary>
        /// The stage's grid for this world seed: null with -fkbathy off or for a stage without a recipe (every stage but the
        /// lake). Built once and cached (the last one only: key stage, world seed, recipe version).
        /// </summary>
        public static Bathymetry For(StageLayout L, int worldSeed)
        {
            if (Off || L == null) return null;
            var r = TerrainRecipes.For(L.id);
            if (r == null) return null;
            string key = L.id + "/" + worldSeed + "/" + r.version;
            if (cached != null && cacheKey == key)
            {
                cached.L = L;
                return cached;
            }
            var b = BathyGen.Build(L, r, worldSeed, Obstacles.ReadSet(L.id));
            cached = b;
            cacheKey = key;
            Debug.Log(b.Summary());
            Debug.Log(b.CharacterLine());
            return b;
        }

        /// <summary>Forgets the cached grid (the determinism test builds the same seed twice).</summary>
        public static void ClearCache()
        {
            cached = null;
            cacheKey = null;
        }

        /// <summary>The kind's name in the texts.</summary>
        public static string Name(BedKind k) => k switch
        {
            BedKind.Shelf => "얕은 턱",
            BedKind.Flat => "수초 평지",
            BedKind.Shoal => "수초 둔덕",
            BedKind.Dropoff => "브레이크라인",
            BedKind.Hump => "수중 둔덕",
            BedKind.Hole => "깊은 웅덩이",
            BedKind.Channel => "물골",
            BedKind.Basin => "깊은 바닥",
            _ => "열린 바닥",
        };

        // ------------------------------------------------------------------ the grid
        public string StageId { get; internal set; }
        public int WorldSeed { get; internal set; }
        public int Attempt { get; internal set; }
        public uint StageSeed { get; internal set; }
        public uint Hash { get; internal set; }
        public bool Fallback { get; internal set; }
        public float BuildMs { get; internal set; }
        public const float Cell = 0.5f;
        public float X0 { get; internal set; }
        public float Z0 { get; internal set; }
        public float X1 { get; internal set; }
        public float Z1 { get; internal set; }
        public int Nx { get; internal set; }
        public int Nz { get; internal set; }
        /// <summary>Over all nodes (m).</summary>
        public float DepthMin { get; internal set; }
        public float DepthMax { get; internal set; }
        public float DepthMean { get; internal set; }
        /// <summary>The validation's failures per attempt (for the log and the test).</summary>
        public string Checks { get; internal set; } = "";

        internal ushort[] cm, edgeCm, weedEdgeCm;
        internal byte[] kind, mat, zone, flags;
        internal List<BedZone> zones = new List<BedZone>();
        /// <summary>The layout it was built for (the off-grid profile's far end and the tide in the along / disc queries).</summary>
        internal StageLayout L;
        /// <summary>The derived profile P* per row (m, whole cm), the rows' median windows [rowI0, rowI1] (node columns).</summary>
        internal float[] pStar;
        internal int[] rowI0, rowI1;
        /// <summary>The lake's statistics over R_ref: the depth (cm) at each whole percentile 0..100, every node's mid-rank (per mille), the shares.</summary>
        internal ushort[] quantCm, rankPm;
        internal float[] matShare = new float[4], kindShare = new float[9];
        internal int[] refNodes;
        internal float[] refU;
        /// <summary>The mid-rank (0..1) of every whole cm 0..rankCmMax (for <see cref="RankPct"/>).</summary>
        internal float[] rankOfCm;

        /// <summary>The seed's character (Docs/lake_phase2_spec.md A1).</summary>
        public BedCharacter Character { get; internal set; }

        /// <summary>The derived profile's rows (m, z = Z0 + j x Cell).</summary>
        public float[] ProfileRows => pStar;

        /// <summary>R_ref's nodes and their weights (1 / (2 hw) a node: every row weighs the same), for pure callers.</summary>
        public int[] RefNodes => refNodes;
        public float[] RefU => refU;

        /// <summary>
        /// The derived distance profile (m, no tide): P* lerped between rows; before the grid its first row; beyond it the
        /// last row scaled by the authored profile's shape (<see cref="StageLayout.AuthoredMeanDepth"/>).
        /// </summary>
        public float Profile(float z)
        {
            if (pStar == null) return L != null ? L.AuthoredMeanDepth(z) : 0f;
            if (z <= Z0) return pStar[0];
            if (z > Z1)
            {
                float a1 = L != null ? L.AuthoredMeanDepth(Z1) : 0f;
                return a1 > 0f ? pStar[Nz - 1] * L.AuthoredMeanDepth(z) / a1 : pStar[Nz - 1];
            }
            float fz = (z - Z0) / Cell;
            int j = Mathf.Min((int)fz, Nz - 2);
            float t = Mathf.Min(1f, fz - j);
            return pStar[j] + (pStar[j + 1] - pStar[j]) * t;
        }

        /// <summary>The depth (m) at a percentile of R_ref's depths (0 = the shallowest, 100 = the deepest; lerped between whole percentiles).</summary>
        public float Quantile(float pct)
        {
            float p = Mathf.Clamp(pct, 0f, 100f);
            int i = Mathf.Min(99, (int)p);
            float t = p - i;
            return (quantCm[i] + (quantCm[i + 1] - (float)quantCm[i]) * t) * 0.01f;
        }

        /// <summary>A node's depth as a percentile of R_ref's (its mid-rank, 0..100).</summary>
        public float NodeRankPct(int k) => rankPm[k] * 0.1f;

        /// <summary>A depth (m) as a percentile of R_ref's (the mid-rank of its whole cm; 0 shallower than all, 100 deeper).</summary>
        public float RankPct(float depthM)
        {
            int c = Mathf.RoundToInt(depthM * 100f);
            if (c < 0) return 0f;
            if (c >= rankOfCm.Length) return 100f;
            return rankOfCm[c] * 100f;
        }

        /// <summary>R_ref's share (U-weighted) of a material / a kind.</summary>
        public float MatShare(BedMat m) => matShare[(int)m];
        public float KindShare(BedKind k) => kindShare[(int)k];

        /// <summary>The median of row j's final depths over its window (the profile's check: D3b).</summary>
        public float FinalRowMedian(int j)
        {
            int n = rowI1[j] - rowI0[j] + 1;
            if (n <= 0) return pStar[j];
            var v = new float[n];
            for (int i = 0; i < n; i++) v[i] = cm[j * Nx + rowI0[j] + i] * 0.01f;
            Array.Sort(v);
            return (n & 1) == 1 ? v[n / 2] : 0.5f * (v[n / 2 - 1] + v[n / 2]);
        }

        public IReadOnlyList<BedZone> Zones => zones;

        public BedZone Zone(string id)
        {
            foreach (var z in zones) if (z.id == id) return z;
            return null;
        }

        public bool Covers(float x, float z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;

        /// <summary>The mean water's depth (m): bilinear over the nodes' cm; no tide (callers use StageLayout.DepthAt).</summary>
        public float Depth(float x, float z)
        {
            float fx = (x - X0) / Cell, fz = (z - Z0) / Cell;
            if (fx < 0f) fx = 0f;
            if (fz < 0f) fz = 0f;
            int i = Mathf.Min((int)fx, Nx - 2), j = Mathf.Min((int)fz, Nz - 2);
            float tx = Mathf.Min(1f, fx - i), tz = Mathf.Min(1f, fz - j);
            int k = j * Nx + i;
            float a = cm[k] + (cm[k + 1] - (float)cm[k]) * tx;
            float b = cm[k + Nx] + (cm[k + Nx + 1] - (float)cm[k + Nx]) * tx;
            return (a + (b - a) * tz) * 0.01f;
        }

        public int NodeCount => Nx * Nz;

        /// <summary>The nearest node (-1 off the grid).</summary>
        public int NodeAt(float x, float z)
        {
            if (!Covers(x, z)) return -1;
            int i = Mathf.Clamp(Mathf.RoundToInt((x - X0) / Cell), 0, Nx - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt((z - Z0) / Cell), 0, Nz - 1);
            return j * Nx + i;
        }

        public Vector2 NodePos(int k) => new Vector2(X0 + (k % Nx) * Cell, Z0 + (k / Nx) * Cell);
        public float NodeDepth(int k) => cm[k] * 0.01f;
        public BedMat NodeMat(int k) => (BedMat)mat[k];
        public BedKind NodeKind(int k) => (BedKind)kind[k];
        public BedFlag NodeFlags(int k) => (BedFlag)flags[k];
        public int NodeZone(int k) => zone[k];

        public BedMat MatAt(float x, float z)
        {
            int k = NodeAt(x, z);
            return k < 0 ? BedMat.Mud : (BedMat)mat[k];
        }

        public BedKind KindAt(float x, float z)
        {
            int k = NodeAt(x, z);
            return k < 0 ? BedKind.Open : (BedKind)kind[k];
        }

        public BedZone ZoneAt(float x, float z)
        {
            int k = NodeAt(x, z);
            return k < 0 || zone[k] >= zones.Count ? null : zones[zone[k]];
        }

        public BedFlag FlagsAt(float x, float z)
        {
            int k = NodeAt(x, z);
            return k < 0 ? BedFlag.None : (BedFlag)flags[k];
        }

        /// <summary>(dD/dx, dD/dz): central differences at +-<see cref="Cell"/>; points deeper.</summary>
        public Vector2 Gradient(float x, float z)
        {
            float x0 = Mathf.Max(X0, x - Cell), x1 = Mathf.Min(X1, x + Cell);
            float z0 = Mathf.Max(Z0, z - Cell), z1 = Mathf.Min(Z1, z + Cell);
            float gx = x1 > x0 ? (Depth(x1, z) - Depth(x0, z)) / (x1 - x0) : 0f;
            float gz = z1 > z0 ? (Depth(x, z1) - Depth(x, z0)) / (z1 - z0) : 0f;
            return new Vector2(gx, gz);
        }

        public float Slope(float x, float z) => Gradient(x, z).magnitude;

        /// <summary>The way down the slope (unit); zero where it is flatter than 0.02.</summary>
        public Vector2 DeeperDir(float x, float z)
        {
            var g = Gradient(x, z);
            float m = g.magnitude;
            return m < 0.02f ? Vector2.zero : g / m;
        }

        /// <summary>Metres to the nearest Edge node (99 off the grid).</summary>
        public float EdgeDist(float x, float z)
        {
            int k = NodeAt(x, z);
            return k < 0 ? 99f : Mathf.Min(99f, edgeCm[k] * 0.01f);
        }

        /// <summary>Metres to the nearest WeedEdge node (99 off the grid).</summary>
        public float WeedEdgeDist(float x, float z)
        {
            int k = NodeAt(x, z);
            return k < 0 ? 99f : Mathf.Min(99f, weedEdgeCm[k] * 0.01f);
        }

        internal float EdgeDistNode(int k) => Mathf.Min(99f, edgeCm[k] * 0.01f);

        /// <summary>
        /// The water's depth anywhere over this grid (off it its own derived profile; the tide added). Read from the grid
        /// itself, never through L.Bathy (which is assigned only after a build, and in the sweeps is another grid).
        /// </summary>
        float Water(float x, float z) => (Covers(x, z) ? Depth(x, z) : Profile(z)) + StageLayout.TideOffset;

        /// <summary>The least water along a-b, sampled every 0.25 m (ends included).</summary>
        public float MinDepthAlong(Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.25f));
            float m = float.MaxValue;
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                m = Mathf.Min(m, Water(p.x, p.y));
            }
            return m;
        }

        /// <summary>The first point along a-b (every 0.25 m) with less than <paramref name="d"/> of water.</summary>
        public bool FirstShallower(Vector2 a, Vector2 b, float d, out Vector2 at)
        {
            float len = (b - a).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.25f));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                if (Water(p.x, p.y) < d)
                {
                    at = p;
                    return true;
                }
            }
            at = b;
            return false;
        }

        static readonly Vector2[] Ring8 =
        {
            new Vector2(1f, 0f), new Vector2(0.70710678f, 0.70710678f), new Vector2(0f, 1f), new Vector2(-0.70710678f, 0.70710678f),
            new Vector2(-1f, 0f), new Vector2(-0.70710678f, -0.70710678f), new Vector2(0f, -1f), new Vector2(0.70710678f, -0.70710678f),
        };

        /// <summary>The least water of the centre and 8 points round it at <paramref name="r"/> (0, 45, ... 315 degrees).</summary>
        public float MinDepthDisc(float x, float z, float r)
        {
            float m = Water(x, z);
            foreach (var u in Ring8) m = Mathf.Min(m, Water(x + u.x * r, z + u.y * r));
            return m;
        }

        /// <summary>The [BATHY] line of a build.</summary>
        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var z in zones) sb.Append(' ').Append(z.id).Append(':').Append(z.nodes.Length);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[BATHY] {0}: world seed {1} stage seed {2} attempt {3}{4} hash 0x{5:x8} built {6:0.0} ms depth min/mean/max {7:0.00}/{8:0.00}/{9:0.00} zones{10}",
                StageId, WorldSeed, StageSeed, Attempt, Fallback ? " fallback" : "", Hash, BuildMs, DepthMin, DepthMean, DepthMax, sb);
        }

        /// <summary>The [BATHY] character line of a build: its u, the derived numbers, R_ref's quartiles and shares.</summary>
        public string CharacterLine()
        {
            var c = Character;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[BATHY] character depth {0:0.00} weed {1:0.00} rock {2:0.00} side {3:0.00} \"{4}\": Dm {5:0.00} shelf {6:0.0} drop {7:0.0} lane {8:0.00} holes {9} humps {10} weed flats {11}; Q10/50/90 {12:0.00}/{13:0.00}/{14:0.00} m; weed {15:0.0}% gravel {16:0.0}% sand {17:0.0}% mud {18:0.0}%",
                c.uDepth, c.uWeed, c.uRock, c.uSide, c.label, c.mainDepth, c.shelfEnd, c.dropWidth, c.laneT, c.holes, c.humps, c.weedFlats,
                Quantile(10f), Quantile(50f), Quantile(90f), 100f * MatShare(BedMat.Weed), 100f * MatShare(BedMat.Gravel), 100f * MatShare(BedMat.Sand), 100f * MatShare(BedMat.Mud));
        }
    }
}
