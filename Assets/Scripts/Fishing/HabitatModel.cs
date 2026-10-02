using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishingKing
{
    /// <summary>Where in the water column a species swims: anywhere between its depths, or near the bottom.</summary>
    public enum Column { Mid, Bottom }

    /// <summary>
    /// A species' habitat on the generated bed (Docs/terrain_depth_spec.md 7.1): its preferred water depth [a, b], a weight
    /// per bed kind and material, how much it likes an edge, its column, how strongly the habitat steers it (beta), the
    /// period shifts of its depths (m, - shallower) and per-period kind weights, and whether a big one runs for the deep.
    /// Parsed from a "key:value,..." string (GameDatabase.Habitats).
    /// </summary>
    public sealed class HabitatDef
    {
        public float a = 0f, b = 99f;
        public float[] kindW = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        public float[] matW = { 1f, 1f, 1f, 1f };
        public float edge;
        public Column col;
        public float beta = 0.6f;
        public float[] shift = new float[4];
        public float[,] periodKind = new float[4, 9];
        public bool runDeep;

        public HabitatDef()
        {
            for (int p = 0; p < 4; p++)
            for (int k = 0; k < 9; k++) periodKind[p, k] = 1f;
        }

        static readonly string[] KindKeys = { "open", "shelf", "flat", "shoal", "dropoff", "hump", "hole", "channel", "basin" };
        static readonly string[] MatKeys = { "mud", "sand", "gravel", "weed" };
        static readonly string[] PeriodKeys = { "dawn", "day", "evening", "night" };

        /// <summary>
        /// "depth:a-b", a kind or material key with its weight, "edge:x", "col:mid|bottom", "beta:x", "@period:shift",
        /// "@period.kind:weight", "runDeep". An unknown key is warned about (<paramref name="warn"/>) and skipped.
        /// </summary>
        public static HabitatDef Parse(string id, string spec, Action<string> warn)
        {
            var h = new HabitatDef();
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in spec.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                int c = part.IndexOf(':');
                string key = c >= 0 ? part.Substring(0, c).Trim() : part;
                string val = c >= 0 ? part.Substring(c + 1).Trim() : "";
                bool F(out float v) => float.TryParse(val, NumberStyles.Float, ci, out v);
                if (key == "runDeep")
                {
                    h.runDeep = true;
                    continue;
                }
                if (key == "depth")
                {
                    var ab = val.Split('-');
                    if (ab.Length == 2 && float.TryParse(ab[0], NumberStyles.Float, ci, out float lo) && float.TryParse(ab[1], NumberStyles.Float, ci, out float hi))
                    {
                        h.a = lo;
                        h.b = hi;
                    }
                    else warn?.Invoke($"{id}: bad depth '{val}'");
                    continue;
                }
                if (key == "col")
                {
                    if (val == "mid") h.col = Column.Mid;
                    else if (val == "bottom") h.col = Column.Bottom;
                    else warn?.Invoke($"{id}: bad column '{val}'");
                    continue;
                }
                if (key == "edge" && F(out float e)) { h.edge = e; continue; }
                if (key == "beta" && F(out float be)) { h.beta = be; continue; }
                int ki = Array.IndexOf(KindKeys, key);
                if (ki >= 0 && F(out float kw)) { h.kindW[ki] = kw; continue; }
                int mi = Array.IndexOf(MatKeys, key);
                if (mi >= 0 && F(out float mw)) { h.matW[mi] = mw; continue; }
                if (key.StartsWith("@"))
                {
                    var pk = key.Substring(1).Split('.');
                    int pi = Array.IndexOf(PeriodKeys, pk[0]);
                    if (pi >= 0 && pk.Length == 1 && F(out float sh)) { h.shift[pi] = sh; continue; }
                    if (pi >= 0 && pk.Length == 2)
                    {
                        int kk = Array.IndexOf(KindKeys, pk[1]);
                        if (kk >= 0 && F(out float pw)) { h.periodKind[pi, kk] = pw; continue; }
                    }
                }
                warn?.Invoke($"{id}: unknown habitat key '{part}'");
            }
            return h;
        }
    }

    /// <summary>The rig classes the bite budget is kept per (Docs/terrain_depth_spec.md 8.1).</summary>
    public enum RigClass { F1, F2, F4, F6, Surface, Mid, Bottom }

    /// <summary>A species as the habitat model sees it.</summary>
    public struct HabSpecies
    {
        public string id;
        public float dMin, dMax, maxCm;
        public HabitatDef h;
    }

    /// <summary>
    /// The habitat model on a <see cref="Bathymetry"/> (Docs/terrain_depth_spec.md 7, 8), free of the running game so the
    /// tests can sweep it: the habitat factor of a node, the target / spawn densities (the old uniform one and the new one
    /// leaning towards the habitat by beta), the swim depths and the bite budget's estimator.
    /// </summary>
    public static class HabitatModel
    {
        /// <summary>The least water a fish of this size swims in (m): 0.3 .. 1.2 (a 90 cm carp 0.56).</summary>
        public static float MinWater(float cm) => Mathf.Clamp(0.2f + 0.4f * cm / 100f, 0.3f, 1.2f);

        /// <summary>The habitat factor h of water <paramref name="w"/> m deep over this bed in period <paramref name="p"/> (spec 7.2), 0 too shallow.</summary>
        public static float H(HabitatDef h, int p, float w, BedKind k, BedMat m, float edgeDist, float minWater)
        {
            if (w < minWater) return 0f;
            if (h == null) return 1f;
            float d = h.shift[p], a = h.a + d, b = h.b + d;
            float fit = w < a ? Mathf.Max(0.2f, 1f - 0.8f * (a - w)) : w > b ? Mathf.Max(0.2f, 1f - 0.4f * (w - b)) : 1f;
            return fit * h.kindW[(int)k] * h.periodKind[p, (int)k] * h.matW[(int)m] * (1f + h.edge * Mathf.Max(0f, 1f - edgeDist / 2.5f));
        }

        /// <summary>The swim depth's range over water <paramref name="water"/> m deep (spec 7.5): the column's, shifted by half the period's shift.</summary>
        public static void SwimRange(HabitatDef h, float dMin, float dMax, int p, float water, out float lo, out float hi)
        {
            float bot = water - 0.3f;
            if (h == null)
            {
                lo = Mathf.Min(dMin, bot);
                hi = Mathf.Min(dMax, bot);
            }
            else
            {
                float d = h.shift[p] * 0.5f;
                float l = Mathf.Max(0.3f, dMin + d), u = Mathf.Max(l, dMax + d);
                if (h.col == Column.Bottom)
                {
                    lo = Mathf.Max(Mathf.Min(l, bot), bot - 2.0f);
                    hi = bot;
                }
                else
                {
                    lo = Mathf.Min(l, bot);
                    hi = Mathf.Min(u, bot);
                }
            }
            lo = Mathf.Max(0.3f, lo);
            hi = Mathf.Max(lo, hi);
        }

        /// <summary>Today's range over the profile's water (what the old PickTarget drew from).</summary>
        public static void LegacyRange(float dMin, float dMax, float water, out float lo, out float hi)
        {
            float bot = water - 0.3f;
            lo = Mathf.Max(0.3f, Mathf.Min(dMin, bot));
            hi = Mathf.Max(lo, Mathf.Min(dMax, bot));
        }

        /// <summary>The share of [lo, hi] within 2.5 m of the hook's depth (the bite's depth gate); an indicator when lo = hi.</summary>
        public static float Pd(float lo, float hi, float hd)
        {
            float a = hd - 2.5f, b = hd + 2.5f;
            if (hi - lo < 1e-4f) return lo >= a && lo <= b ? 1f : 0f;
            float o = Mathf.Min(hi, b) - Mathf.Max(lo, a);
            return o <= 0f ? 0f : o / (hi - lo);
        }

        /// <summary>The hook's depth of a rig class over water <paramref name="water"/> m deep (spec 8.2).</summary>
        public static float HookDepth(RigClass r, float water) => r switch
        {
            RigClass.F1 => Mathf.Min(1f, water - 0.25f),
            RigClass.F2 => Mathf.Min(2f, water - 0.25f),
            RigClass.F4 => Mathf.Min(4f, water - 0.25f),
            RigClass.F6 => Mathf.Min(6f, water - 0.25f),
            RigClass.Surface => 0.05f,
            RigClass.Mid => Mathf.Min(1.5f, water - 0.25f),
            _ => water - 0.25f,
        };

        public static bool IsLure(RigClass r) => r >= RigClass.Surface;

        // ------------------------------------------------------------------ regions and densities
        /// <summary>The nodes of a region (z in [zMin, zMax], |x| within the half width at z) and today's density (uniform z, uniform x).</summary>
        public sealed class Region
        {
            public float zMin, zMax;
            public int[] nodes;
            public float[] u;
            public float uSum;
        }

        public static Region MakeRegion(Bathymetry b, float zMin, float zMax, Func<float, float> half)
        {
            var nodes = new List<int>();
            var u = new List<float>();
            float sum = 0f;
            int j0 = Mathf.Max(0, Mathf.CeilToInt((zMin - b.Z0) / Bathymetry.Cell)), j1 = Mathf.Min(b.Nz - 1, Mathf.FloorToInt((zMax - b.Z0) / Bathymetry.Cell));
            for (int j = j0; j <= j1; j++)
            {
                float z = b.Z0 + j * Bathymetry.Cell;
                float hw = half(z);
                if (hw <= 0f) continue;
                float uu = 1f / (2f * hw);
                for (int i = 0; i < b.Nx; i++)
                {
                    float x = b.X0 + i * Bathymetry.Cell;
                    if (Mathf.Abs(x) > hw) continue;
                    nodes.Add(j * b.Nx + i);
                    u.Add(uu);
                    sum += uu;
                }
            }
            return new Region { zMin = zMin, zMax = zMax, nodes = nodes.ToArray(), u = u.ToArray(), uSum = sum };
        }

        /// <summary>
        /// The new density over a region (spec 7.3): W = U x ((1 - beta) + beta x hn) where hn = h / mean_U(h) clamped to
        /// [0.25, 3]; 0 where the water is too shallow for the species' biggest. Returns the cumulative sums.
        /// </summary>
        public static float[] Weights(Bathymetry b, Region reg, HabSpecies s, int p, out float total)
        {
            int n = reg.nodes.Length;
            var h = new float[n];
            float mw = MinWater(s.maxCm);
            double hu = 0;
            for (int q = 0; q < n; q++)
            {
                int k = reg.nodes[q];
                h[q] = H(s.h, p, b.NodeDepth(k), b.NodeKind(k), b.NodeMat(k), b.EdgeDistNode(k), mw);
                hu += reg.u[q] * h[q];
            }
            float mean = reg.uSum > 0f ? (float)(hu / reg.uSum) : 1f;
            if (mean <= 1e-6f) mean = 1f;
            float beta = s.h != null ? s.h.beta : 0f;
            var cum = new float[n];
            double acc = 0;
            for (int q = 0; q < n; q++)
            {
                float w = 0f;
                if (h[q] > 0f || s.h == null)
                {
                    float hn = Mathf.Clamp(h[q] / mean, 0.25f, 3f);
                    w = reg.u[q] * ((1f - beta) + beta * hn);
                    if (b.NodeDepth(reg.nodes[q]) < mw) w = 0f;
                }
                acc += w;
                cum[q] = (float)acc;
            }
            total = (float)acc;
            return cum;
        }

        /// <summary>The weight of node q in a cumulative array.</summary>
        public static float WeightAt(float[] cum, int q) => q == 0 ? cum[0] : cum[q] - cum[q - 1];

        /// <summary>The index whose cumulative weight first reaches <paramref name="r"/> (binary search).</summary>
        public static int Pick(float[] cum, float r)
        {
            int lo = 0, hi = cum.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] < r) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        // ------------------------------------------------------------------ the bite budget's estimator (spec 8.2)
        /// <summary>
        /// The reach scale's limits. (Spec 8 had 0.82..1.22; the period shifts asked for, fish shallower at dawn and
        /// evening and the carp on the flats at night, bring a shallow rig on the longest rods up to ~2x today's encounters,
        /// past what 0.82 can take back: 0.70..1.30 keeps every rig and period in the 0.8..1.25 band.)
        /// </summary>
        public const float ScaleMin = 0.70f, ScaleMax = 1.30f;
        /// <summary>The raw ratios the scale can take back (1 / ScaleMax^2 .. 1 / ScaleMin^2).</summary>
        public const float RawMin = 1f / (ScaleMax * ScaleMax), RawMax = 1f / (ScaleMin * ScaleMin);

        public struct Budget
        {
            public float raw, scale, scaled, best10, worst10;
            /// <summary>The best 10 % of casts' ratio before the reach scale (what the bed alone does to the best spots).</summary>
            public float best10Raw;
            public int casts;
        }

        /// <summary>The casts and the stride nodes within reach of each (shared by every species, period and rig of a rod).</summary>
        public sealed class CastSet
        {
            public Vector2[] at;
            public float[] profileWater, gridWater;
            public float castDist, reach;
            public Region region;
            public int[][] near;       // per cast: indices into region.nodes (stride nodes within reach)
            public float[][] nearD;    // their distance
        }

        /// <summary>
        /// The reference player's casts (spec 8.2): yaw -38..38 in 4 degree steps, from zNear + 2.5 out to the rod's cast
        /// distance every metre, from (<paramref name="anchorX"/>, 0); and the region's 1 m stride nodes within
        /// <paramref name="reach"/> of each.
        /// </summary>
        public static CastSet Casts(Bathymetry b, StageLayout L, Region reg, float castDist, float anchorX, float reach)
        {
            var at = new List<Vector2>();
            for (int yi = 0; yi < 20; yi++)
            {
                float yaw = (-38f + 4f * yi) * Mathf.Deg2Rad;
                for (float rho = L.zNear + 2.5f; rho <= castDist + 1e-3f; rho += 1f)
                    at.Add(new Vector2(anchorX + rho * Mathf.Sin(yaw), rho * Mathf.Cos(yaw)));
            }
            var cs = new CastSet { at = at.ToArray(), castDist = castDist, reach = reach, region = reg };
            int nc = cs.at.Length;
            cs.profileWater = new float[nc];
            cs.gridWater = new float[nc];
            cs.near = new int[nc][];
            cs.nearD = new float[nc][];
            // the stride nodes of the region (even i and j), bucketed by row for the reach search
            var rows = new Dictionary<int, List<int>>();
            for (int q = 0; q < reg.nodes.Length; q++)
            {
                int k = reg.nodes[q];
                int i = k % b.Nx, j = k / b.Nx;
                if ((i & 1) != 0 || (j & 1) != 0) continue;
                if (!rows.TryGetValue(j, out var l)) rows[j] = l = new List<int>();
                l.Add(q);
            }
            var tmp = new List<int>();
            var tmpD = new List<float>();
            for (int c = 0; c < nc; c++)
            {
                var p = cs.at[c];
                cs.profileWater[c] = L.ProfileDepth(p.y);
                cs.gridWater[c] = b.Covers(p.x, p.y) ? b.Depth(p.x, p.y) + StageLayout.TideOffset : L.ProfileDepth(p.y);
                tmp.Clear();
                tmpD.Clear();
                int j0 = Mathf.Max(0, Mathf.FloorToInt((p.y - reach - b.Z0) / Bathymetry.Cell)), j1 = Mathf.Min(b.Nz - 1, Mathf.CeilToInt((p.y + reach - b.Z0) / Bathymetry.Cell));
                for (int j = j0; j <= j1; j++)
                {
                    if (!rows.TryGetValue(j, out var l)) continue;
                    foreach (int q in l)
                    {
                        var np = b.NodePos(reg.nodes[q]);
                        float d = (np - p).magnitude;
                        if (d > reach) continue;
                        tmp.Add(q);
                        tmpD.Add(d);
                    }
                }
                cs.near[c] = tmp.ToArray();
                cs.nearD[c] = tmpD.ToArray();
            }
            return cs;
        }

        /// <summary>
        /// The estimator (spec 8.2): over the casts, the encounter score of today's density on the profile and of the new
        /// density on the grid (sum of the density times the share of each node's swim depths within 2.5 m of the hook);
        /// raw = new / old, the reach scale = clamp(sqrt(1 / raw), 0.82, 1.22); with <paramref name="full"/> also the scaled
        /// estimate (the new density within reach x scale) and the per-cast ratios of the best and worst 10 % of casts.
        /// </summary>
        public static Budget Estimate(Bathymetry b, StageLayout L, CastSet cs, HabSpecies s, int p, RigClass rig, float[] cumNew, float totalNew, bool full)
        {
            var reg = cs.region;
            int nc = cs.at.Length;
            // per region node: the normalized densities and the swim ranges (old on the profile, new on the grid)
            var budget = new Budget { casts = nc };
            double eLeg = 0, eNew = 0;
            var perLeg = full ? new float[nc] : null;
            var rawRatios = full ? new List<float>() : null;
            float invU = reg.uSum > 0f ? 1f / reg.uSum : 0f, invW = totalNew > 0f ? 1f / totalNew : 0f;
            // (the swim ranges per region node, filled on first use: x/y today's on the profile, z/w the new on the grid)
            int nr = reg.nodes.Length;
            var rg = new float[nr * 4];
            var have = new bool[nr];
            (float x, float y, float z, float w) Ranges(int q)
            {
                int o = q * 4;
                if (!have[q])
                {
                    have[q] = true;
                    int k = reg.nodes[q];
                    var np = b.NodePos(k);
                    LegacyRange(s.dMin, s.dMax, L.ProfileDepth(np.y), out rg[o], out rg[o + 1]);
                    SwimRange(s.h, s.dMin, s.dMax, p, b.NodeDepth(k) + StageLayout.TideOffset, out rg[o + 2], out rg[o + 3]);
                }
                return (rg[o], rg[o + 1], rg[o + 2], rg[o + 3]);
            }
            for (int c = 0; c < nc; c++)
            {
                float hdL = HookDepth(rig, cs.profileWater[c]), hdN = HookDepth(rig, cs.gridWater[c]);
                double el = 0, en = 0;
                var near = cs.near[c];
                for (int t = 0; t < near.Length; t++)
                {
                    int q = near[t];
                    var r = Ranges(q);
                    el += reg.u[q] * invU * Pd(r.x, r.y, hdL);
                    en += WeightAt(cumNew, q) * invW * Pd(r.z, r.w, hdN);
                }
                eLeg += el;
                eNew += en;
                if (full)
                {
                    perLeg[c] = (float)el;
                    if (el > 1e-9) rawRatios.Add((float)(en / el));
                }
            }
            if (full)
            {
                rawRatios.Sort();
                int ten = Mathf.Max(1, rawRatios.Count / 10);
                float top = 0f;
                for (int t = 0; t < ten && t < rawRatios.Count; t++) top += rawRatios[rawRatios.Count - 1 - t];
                budget.best10Raw = rawRatios.Count > 0 ? top / ten : 0f;
            }
            float raw = eLeg > 0 ? (float)(eNew / eLeg) : 1f;
            budget.raw = raw;
            budget.scale = Mathf.Clamp(Mathf.Sqrt(1f / Mathf.Max(1e-4f, raw)), ScaleMin, ScaleMax);
            if (!full) return budget;
            // the scaled estimate: the new density within reach x scale (the nodes out to it, gathered again)
            float R2 = cs.reach * budget.scale;
            double eS = 0;
            var ratios = new List<float>();
            for (int c = 0; c < nc; c++)
            {
                float hdN = HookDepth(rig, cs.gridWater[c]);
                var pc = cs.at[c];
                double en = 0;
                int j0 = Mathf.Max(0, Mathf.FloorToInt((pc.y - R2 - b.Z0) / Bathymetry.Cell)), j1 = Mathf.Min(b.Nz - 1, Mathf.CeilToInt((pc.y + R2 - b.Z0) / Bathymetry.Cell));
                int i0 = Mathf.Max(0, Mathf.FloorToInt((pc.x - R2 - b.X0) / Bathymetry.Cell)), i1 = Mathf.Min(b.Nx - 1, Mathf.CeilToInt((pc.x + R2 - b.X0) / Bathymetry.Cell));
                for (int j = j0 + (j0 & 1); j <= j1; j += 2)
                for (int i = i0 + (i0 & 1); i <= i1; i += 2)
                {
                    int k = j * b.Nx + i;
                    int q = RegionIndex(reg, k);
                    if (q < 0) continue;
                    if ((b.NodePos(k) - pc).magnitude > R2) continue;
                    var r = Ranges(q);
                    en += WeightAt(cumNew, q) * invW * Pd(r.z, r.w, hdN);
                }
                eS += en;
                if (perLeg[c] > 1e-9f) ratios.Add((float)(en / perLeg[c]));
            }
            budget.scaled = eLeg > 0 ? (float)(eS / eLeg) : 1f;
            ratios.Sort();
            int tenth = Mathf.Max(1, ratios.Count / 10);
            float lo = 0f, hi = 0f;
            for (int t = 0; t < tenth && t < ratios.Count; t++)
            {
                lo += ratios[t];
                hi += ratios[ratios.Count - 1 - t];
            }
            budget.worst10 = ratios.Count > 0 ? lo / tenth : 0f;
            budget.best10 = ratios.Count > 0 ? hi / tenth : 0f;
            return budget;
        }

        static readonly Dictionary<Region, Dictionary<int, int>> regionIndex = new Dictionary<Region, Dictionary<int, int>>();

        /// <summary>The node's index in the region (-1: outside it).</summary>
        static int RegionIndex(Region reg, int k)
        {
            if (!regionIndex.TryGetValue(reg, out var map))
            {
                if (regionIndex.Count > 64) regionIndex.Clear();
                map = new Dictionary<int, int>(reg.nodes.Length);
                for (int q = 0; q < reg.nodes.Length; q++) map[reg.nodes[q]] = q;
                regionIndex[reg] = map;
            }
            return map.TryGetValue(k, out int v) ? v : -1;
        }
    }
}
