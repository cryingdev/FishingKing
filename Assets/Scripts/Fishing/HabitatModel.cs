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
    /// Parsed from a "key:value,..." string (the species file's "habitat").
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
        public float dMin, dMax, minCm, maxCm;
        /// <summary>Its swim speed (FishSpecies.speed: a wanderer swims at 0.18..0.3 of it) and how much it seeks cover.</summary>
        public float speed, coverSeek;
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
        /// How much of today's share each row of the region (a distance from the shore) keeps: all of it. The habitat moves a
        /// species' fish along a row, to the flats, drop-offs, humps and channels at that distance, not from out of a rod's
        /// reach into it, so the bites per minute of the reference player's fan stay today's (spec 8.3).
        /// </summary>
        public const float RowKeep = 1f;

        /// <summary>
        /// The new density over a region (spec 7.3): W = U x ((1 - beta) + beta x hn) where hn = h / mean_U(h) clamped to
        /// [0.25, 3]; 0 where the water is too shallow for the species' biggest; then every row (one z) scaled back to today's
        /// share of the region (<see cref="RowKeep"/>). Returns the cumulative sums.
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
            var wq = new float[n];
            for (int q = 0; q < n; q++)
            {
                float w = 0f;
                if (h[q] > 0f || s.h == null)
                {
                    float hn = Mathf.Clamp(h[q] / mean, 0.25f, 3f);
                    w = reg.u[q] * ((1f - beta) + beta * hn);
                    if (b.NodeDepth(reg.nodes[q]) < mw) w = 0f;
                }
                wq[q] = w;
            }
            // (the region's nodes run row by row: each row back to today's share)
            for (int q0 = 0; q0 < n;)
            {
                int row = reg.nodes[q0] / b.Nx, q1 = q0;
                double su = 0, sw = 0;
                while (q1 < n && reg.nodes[q1] / b.Nx == row)
                {
                    su += reg.u[q1];
                    sw += wq[q1];
                    q1++;
                }
                if (sw > 0)
                {
                    float f = Mathf.Lerp(1f, (float)(su / sw), RowKeep);
                    for (int q = q0; q < q1; q++) wq[q] *= f;
                }
                q0 = q1;
            }
            var cum = new float[n];
            double acc = 0;
            for (int q = 0; q < n; q++)
            {
                acc += wq[q];
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
        /// <summary>The reach scale's limits (spec 8).</summary>
        public const float ScaleMin = 0.82f, ScaleMax = 1.22f;
        /// <summary>D11's gate on the raw ratio (spec 8): fixed, not derived from the clamp.</summary>
        public const float RawMin = 0.67f, RawMax = 1.49f;

        public struct Budget
        {
            public float raw, scale, scaled, best10, worst10;
            /// <summary>The best 10 % of casts' ratio before the reach scale (what the bed alone does to the best spots).</summary>
            public float best10Raw;
            /// <summary>The encounter score today (over all the casts, within R) and on the bed within R x scale (the same units).</summary>
            public float today, bed;
            public int casts;
        }

        /// <summary>The reference player's casts (shared by every species, period and rig of a rod and reach).</summary>
        public sealed class CastSet
        {
            public Vector2[] at;
            public float[] profileWater, gridWater;
            public float castDist, reach;
        }

        /// <summary>
        /// The reference player's casts (spec 8.2): yaw -38..38 in 4 degree steps, from zNear + 2.5 out to the rod's cast
        /// distance every metre, from (<paramref name="anchorX"/>, 0); the water there today (the profile) and on the bed.
        /// </summary>
        public static CastSet Casts(Bathymetry b, StageLayout L, float castDist, float anchorX, float reach)
        {
            var at = new List<Vector2>();
            for (int yi = 0; yi < 20; yi++)
            {
                float yaw = (-38f + 4f * yi) * Mathf.Deg2Rad;
                for (float rho = L.zNear + 2.5f; rho <= castDist + 1e-3f; rho += 1f)
                    at.Add(new Vector2(anchorX + rho * Mathf.Sin(yaw), rho * Mathf.Cos(yaw)));
            }
            var cs = new CastSet { at = at.ToArray(), castDist = castDist, reach = reach };
            int nc = cs.at.Length;
            cs.profileWater = new float[nc];
            cs.gridWater = new float[nc];
            for (int c = 0; c < nc; c++)
            {
                var p = cs.at[c];
                cs.profileWater[c] = L.ProfileDepth(p.y);
                cs.gridWater[c] = b.Covers(p.x, p.y) ? b.Depth(p.x, p.y) + StageLayout.TideOffset : L.ProfileDepth(p.y);
            }
            return cs;
        }

        /// <summary>The share of a species' fish (FishSpawner.RollSize: t = U^1.7) that swim in water this deep (MinWater).</summary>
        public static float SizeShare(HabSpecies s, float water)
        {
            if (water >= MinWater(s.maxCm)) return 1f;
            if (water < 0.3f) return 0f;
            float cm = (water - 0.2f) / 0.004f;
            float t = s.maxCm > s.minCm ? Mathf.Clamp01((cm - s.minCm) / (s.maxCm - s.minCm)) : 1f;
            return (float)Math.Pow(t, 1.0 / 1.7);
        }

        // ------------------------------------------------------------------ where the fish spend their time (spec 8.2)
        /// <summary>A cover zone a cover seeker swims to (FishAgent.PickTarget's cover branch): its outline and hold point.</summary>
        public sealed class CoverZone
        {
            public Vector2[] poly;
            public Vector2 c;
            public float hx, hz, x0, x1, z0, z1;

            public CoverZone(Vector2[] poly, Vector2 centre, float hx, float hz)
            {
                this.poly = poly;
                c = centre;
                this.hx = hx;
                this.hz = hz;
                x0 = z0 = float.MaxValue;
                x1 = z1 = float.MinValue;
                foreach (var p in poly)
                {
                    x0 = Mathf.Min(x0, p.x);
                    x1 = Mathf.Max(x1, p.x);
                    z0 = Mathf.Min(z0, p.y);
                    z1 = Mathf.Max(z1, p.y);
                }
            }

            public bool Inside(float x, float z)
            {
                bool inside = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[i];
                    var b = poly[j];
                    if ((a.y > z) != (b.y > z) && x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x) inside = !inside;
                }
                return inside;
            }
        }

        /// <summary>
        /// The water a simulated fish swims in: the bed (<see cref="b"/>, the target density <see cref="cum"/> over
        /// <see cref="reg"/>) or, without one, today's lake (the profile, uniform targets); the target region
        /// [<see cref="zMin"/>, <see cref="zMax"/>] within <see cref="half"/>(z), the species' covers (hold in [zNear + 1, zMax]).
        /// </summary>
        public sealed class SimWorld
        {
            public Bathymetry b;
            public StageLayout L;
            public float zMin, zMax;
            public Func<float, float> half;
            public Region reg;
            public float[] cum;
            public float total;
            public List<CoverZone> covers;

            /// <summary>StageLayout.DepthAt over this bed (or the profile without one).</summary>
            public float Water(float x, float z) => b != null && b.Covers(x, z) ? b.Depth(x, z) + StageLayout.TideOffset : L.ProfileDepth(z);
        }

        /// <summary>
        /// Where a species' fish spend their time (the samples of simulated wanderers), as depth histograms over 1 m cells:
        /// x in [-xLim, xLim], z in [zNear, zMax + 1]. Both the bed's and today's are made over the same cells.
        /// </summary>
        public sealed class Occupancy
        {
            public const int NB = 48;
            public const float Bin = 0.25f;
            public readonly float x0, z0;
            public readonly int nx, nz;
            /// <summary>Per cell NB + 1 cumulative sample counts by depth (0, 0.25, .. 12 m).</summary>
            public readonly float[] cum;
            public int samples;
            float[] raw;

            public Occupancy(float xLim, float zNear, float zMax)
            {
                x0 = -Mathf.Ceil(xLim);
                z0 = Mathf.Floor(zNear);
                nx = Mathf.Max(1, Mathf.CeilToInt(xLim) * 2);
                nz = Mathf.Max(1, Mathf.CeilToInt(zMax + 1f - z0));
                raw = new float[nx * nz * NB];
                cum = new float[nx * nz * (NB + 1)];
            }

            public void Add(float x, float z, float depth)
            {
                samples++;
                int i = Mathf.FloorToInt(x - x0), j = Mathf.FloorToInt(z - z0);
                if (i < 0 || j < 0 || i >= nx || j >= nz) return;
                int d = Mathf.Clamp((int)(depth / Bin), 0, NB - 1);
                raw[(j * nx + i) * NB + d] += 1f;
            }

            public void Close()
            {
                for (int c = 0; c < nx * nz; c++)
                {
                    float acc = 0f;
                    cum[c * (NB + 1)] = 0f;
                    for (int d = 0; d < NB; d++)
                    {
                        acc += raw[c * NB + d];
                        cum[c * (NB + 1) + d + 1] = acc;
                    }
                }
                raw = null;
            }

            float F(int o, float depth)
            {
                float t = depth / Bin;
                if (t <= 0f) return 0f;
                if (t >= NB) return cum[o + NB];
                int i = (int)t;
                return cum[o + i] + (cum[o + i + 1] - cum[o + i]) * (t - i);
            }

            /// <summary>The samples of cell (i, j) with a depth in [lo, hi] (linear within a bin).</summary>
            public float Count(int i, int j, float lo, float hi)
            {
                int o = (j * nx + i) * (NB + 1);
                return F(o, hi) - F(o, lo);
            }
        }

        static float DeltaAngle(float cur, float target)
        {
            float t = target - cur;
            float n = t - (float)Math.Floor(t / 360f) * 360f;
            if (n > 180f) n -= 360f;
            return n;
        }

        static float MoveTowards(float cur, float target, float max) => Math.Abs(target - cur) <= max ? target : cur + Math.Sign(target - cur) * max;

        static float MoveTowardsAngle(float cur, float target, float max)
        {
            float d = DeltaAngle(cur, target);
            if (-max < d && d < max) return target;
            return MoveTowards(cur, cur + d, max);
        }

        /// <summary>A stable hash of a string (the simulation's seeds).</summary>
        public static uint Fnv(string s)
        {
            uint h = 2166136261u;
            foreach (char ch in s)
            {
                h ^= ch;
                h *= 16777619u;
            }
            return h;
        }

        /// <summary>
        /// The simulation's fish and time: 160 fish on the bed (today's lake, the same for every seed and so made once, 640),
        /// 20 s settling, then 240 s sampled every 0.5 s, a 0.2 s step.
        /// </summary>
        public const int SimFish = 160, SimFishToday = 640;
        public const float SimDt = 0.2f, SimWarm = 20f, SimRun = 240f, SimEvery = 0.5f;

        /// <summary>
        /// Where the species' fish spend their time (spec 8.2): <see cref="SimFish"/> fish wander as FishAgent's wander does
        /// (a target drawn by PickTarget's rules, or a cover point for a cover seeker; steered at 2.5 rad/s, its depth eased at
        /// half its speed; a new target on arrival or after 12 s; on the bed never into water shallower than its size swims in
        /// and never under the bed), sampled every <see cref="SimEvery"/> s. Deterministic for a seed (System.Random; the game's
        /// UnityEngine.Random is never touched).
        /// </summary>
        public static Occupancy Simulate(SimWorld w, HabSpecies s, int p, uint seed, int fish = SimFish)
        {
            var rng = new System.Random((int)(seed & 0x7fffffff));
            float U() => (float)rng.NextDouble();
            float Range(float a, float b) => a + (b - a) * U();
            var L = w.L;
            bool bed = w.b != null;
            var occ = new Occupancy(L.xLim, L.zNear, w.zMax);
            float mwMax = MinWater(s.maxCm);
            var ok = new List<CoverZone>();

            Vector3 Target(float cm)
            {
                Vector3 t = default;
                bool drawn = false;
                if (bed && w.total > 0f && w.reg.nodes.Length > 0)
                {
                    // FishHabitat.Draw
                    int q = Pick(w.cum, U() * w.total);
                    var np = w.b.NodePos(w.reg.nodes[q]);
                    float x = np.x + Range(-0.25f, 0.25f), z = np.y + Range(-0.25f, 0.25f);
                    z = Mathf.Clamp(z, w.zMin, w.zMax);
                    float hw = w.half(z);
                    x = Mathf.Clamp(x, -hw, hw);
                    if (w.Water(x, z) < mwMax)
                    {
                        x = np.x;
                        z = np.y;
                    }
                    SwimRange(s.h, s.dMin, s.dMax, p, w.Water(x, z), out float lo, out float hi);
                    t = new Vector3(x, -Mathf.Max(0.3f, Range(lo, hi)), z);
                    drawn = true;
                }
                if (!drawn)
                {
                    float z = Range(w.zMin, w.zMax);
                    float half = Mathf.Min(L.xLim - 0.5f, w.half(z));
                    float bottom = L.ProfileDepth(z) - 0.3f;
                    float d = Range(Mathf.Min(s.dMin, bottom), Mathf.Min(s.dMax, bottom));
                    t = new Vector3(Range(-half, half), -Mathf.Max(0.3f, d), z);
                }
                // the cover branch: 0.6 x seek of the targets in one of its covers (on the bed, one with water enough)
                if (w.covers != null && w.covers.Count > 0 && s.coverSeek > 0f && U() < 0.6f * s.coverSeek)
                {
                    ok.Clear();
                    float need = MinWater(cm) + 0.2f;
                    foreach (var c in w.covers)
                        if (!bed || w.Water(c.hx, c.hz) >= need) ok.Add(c);
                    if (ok.Count > 0)
                    {
                        var c = ok[Math.Min(ok.Count - 1, (int)(U() * ok.Count))];
                        float qx = c.c.x, qz = c.c.y;
                        for (int i = 0; i < 24; i++)
                        {
                            float rx = Range(c.x0, c.x1), rz = Range(c.z0, c.z1);
                            if (!c.Inside(rx, rz)) continue;
                            qx = rx;
                            qz = rz;
                            break;
                        }
                        float cz = Mathf.Clamp(qz, L.zNear + 1.5f, w.zMax);
                        float cx = Mathf.Clamp(qx, -L.xLim + 0.5f, L.xLim - 0.5f);
                        float water = w.Water(cx, cz);
                        float cd;
                        if (bed)
                        {
                            SwimRange(s.h, s.dMin, s.dMax, p, water, out float lo, out float hi);
                            cd = Range(lo, hi);
                        }
                        else
                        {
                            float cb = water - 0.3f;
                            cd = Range(Mathf.Min(s.dMin, cb), Mathf.Min(s.dMax, cb));
                        }
                        t = new Vector3(cx, -Mathf.Max(0.3f, cd), cz);
                    }
                }
                return t;
            }

            int steps = Mathf.RoundToInt((SimWarm + SimRun) / SimDt), warm = Mathf.RoundToInt(SimWarm / SimDt), every = Mathf.Max(1, Mathf.RoundToInt(SimEvery / SimDt));
            for (int k = 0; k < fish; k++)
            {
                float cm = Mathf.Lerp(s.minCm, s.maxCm, (float)Math.Pow(U(), 1.7));
                float mw = MinWater(cm);
                float speed = Mathf.Max(0.05f, s.speed) * Range(0.18f, 0.3f);
                var pos = Target(cm);
                float heading = Range(0f, Mathf.PI * 2f);
                var target = Target(cm);
                float stateT = 0f;
                for (int n = 0; n < steps; n++)
                {
                    stateT += SimDt;
                    float bx = pos.x, bz = pos.z;
                    // FishAgent.Steer
                    float dx = target.x - pos.x, dz = target.z - pos.z;
                    float dd = dx * dx + dz * dz;
                    if (dd > 0.0004f)
                    {
                        float want = (float)Math.Atan2(dz, dx) * Mathf.Rad2Deg;
                        heading = MoveTowardsAngle(heading * Mathf.Rad2Deg, want, 2.5f * Mathf.Rad2Deg * SimDt) * Mathf.Deg2Rad;
                    }
                    float fx = (float)Math.Cos(heading), fz = (float)Math.Sin(heading);
                    float len = (float)Math.Sqrt(dd);
                    float dot = len > 1e-5f ? (fx * dx + fz * dz) / len : 0f;
                    float align = Mathf.Clamp01(dot * 0.8f + 0.2f);
                    pos.x += fx * speed * align * SimDt;
                    pos.z += fz * speed * align * SimDt;
                    pos.y = MoveTowards(pos.y, target.y, speed * 0.5f * SimDt);
                    bool refused = false;
                    if (bed)
                    {
                        // FishAgent.KeepInWater / KeepOffBed
                        float now = w.Water(pos.x, pos.z);
                        if (now < mw && now < w.Water(bx, bz))
                        {
                            refused = true;
                            pos.x = bx;
                            pos.z = bz;
                            target = Target(cm);
                            stateT = 0f;
                            now = w.Water(pos.x, pos.z);
                        }
                        pos.y = Mathf.Max(pos.y, -Mathf.Max(0.3f, now - 0.3f));
                    }
                    if (!refused && ((target.x - pos.x) * (target.x - pos.x) + (target.z - pos.z) * (target.z - pos.z) < 0.36f || stateT > 12f))
                    {
                        target = Target(cm);
                        stateT = 0f;
                    }
                    if (n >= warm && (n - warm) % every == 0) occ.Add(pos.x, pos.z, -pos.y);
                }
            }
            occ.Close();
            return occ;
        }

        /// <summary>
        /// The estimator (spec 8.2): over the casts, the fish today (<paramref name="today"/>, within the reach R) and on the
        /// bed (<paramref name="bed"/>, x the share of its sizes that come into the water at the hook) whose depth is within
        /// 2.5 m of the hook's; raw = bed / today at R; the reach scale is the one that brings the bed's count to today's
        /// (solved over the cumulative count by distance), clamped to [ScaleMin, ScaleMax]; scaled = bed at R x scale / today.
        /// With <paramref name="full"/> also the per-cast ratios of the best and worst 10 % of casts (after the scale) and the
        /// best 10 % before it.
        /// </summary>
        public static Budget Estimate(CastSet cs, HabSpecies s, RigClass rig, Occupancy bed, Occupancy today, bool full)
        {
            int nc = cs.at.Length;
            float R = cs.reach, Rmax = R * ScaleMax;
            const int NH = 120;
            float hb = Rmax / NH;
            var hist = new double[NH + 1];
            double eLeg = 0;
            var perLeg = full ? new float[nc] : null;
            var perNewR = full ? new float[nc] : null;
            // (the two may be made with different numbers of fish: today's counts in the bed's samples)
            float norm = today.samples > 0 ? (float)bed.samples / today.samples : 1f;
            for (int c = 0; c < nc; c++)
            {
                var pc = cs.at[c];
                float hdL = HookDepth(rig, cs.profileWater[c]), hdN = HookDepth(rig, cs.gridWater[c]);
                float gate = SizeShare(s, cs.gridWater[c]);
                double el = 0, enR = 0;
                int i0 = Mathf.Max(0, Mathf.FloorToInt(pc.x - Rmax - bed.x0)), i1 = Mathf.Min(bed.nx - 1, Mathf.FloorToInt(pc.x + Rmax - bed.x0));
                int j0 = Mathf.Max(0, Mathf.FloorToInt(pc.y - Rmax - bed.z0)), j1 = Mathf.Min(bed.nz - 1, Mathf.FloorToInt(pc.y + Rmax - bed.z0));
                for (int j = j0; j <= j1; j++)
                {
                    float cz = bed.z0 + j + 0.5f - pc.y;
                    for (int i = i0; i <= i1; i++)
                    {
                        float cx = bed.x0 + i + 0.5f - pc.x;
                        float d = (float)Math.Sqrt(cx * cx + cz * cz);
                        if (d > Rmax) continue;
                        float en = gate > 0f ? bed.Count(i, j, hdN - 2.5f, hdN + 2.5f) * gate : 0f;
                        hist[Mathf.Min(NH, (int)(d / hb))] += en;
                        if (d <= R)
                        {
                            el += today.Count(i, j, hdL - 2.5f, hdL + 2.5f) * norm;
                            enR += en;
                        }
                    }
                }
                eLeg += el;
                if (full)
                {
                    perLeg[c] = (float)el;
                    perNewR[c] = (float)enR;
                }
            }
            // the bed's count within r, linear within a bin
            var H = new double[NH + 2];
            for (int k = 0; k <= NH; k++) H[k + 1] = H[k] + hist[k];
            double E(float r)
            {
                float t = r / hb;
                int k = Mathf.Clamp((int)t, 0, NH);
                return H[k] + hist[k] * Mathf.Clamp01(t - k);
            }
            var budget = new Budget { casts = nc };
            budget.raw = eLeg > 0 ? (float)(E(R) / eLeg) : 1f;
            float scale;
            if (eLeg <= 0) scale = 1f;
            else if (E(R * ScaleMax) < eLeg) scale = ScaleMax;
            else if (E(R * ScaleMin) > eLeg) scale = ScaleMin;
            else
            {
                float lo = R * ScaleMin, hi = R * ScaleMax;
                for (int it = 0; it < 30; it++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (E(mid) < eLeg) lo = mid;
                    else hi = mid;
                }
                scale = 0.5f * (lo + hi) / R;
            }
            budget.scale = Mathf.Clamp(scale, ScaleMin, ScaleMax);
            budget.scaled = eLeg > 0 ? (float)(E(R * budget.scale) / eLeg) : 1f;
            budget.today = (float)eLeg;
            budget.bed = (float)E(R * budget.scale);
            if (!full) return budget;
            // per cast: the bed within R x scale over today within R
            float R2 = R * budget.scale;
            var ratios = new List<float>();
            var rawRatios = new List<float>();
            for (int c = 0; c < nc; c++)
            {
                if (perLeg[c] <= 1e-6f) continue;
                var pc = cs.at[c];
                float hdN = HookDepth(rig, cs.gridWater[c]);
                float gate = SizeShare(s, cs.gridWater[c]);
                double en = 0;
                int i0 = Mathf.Max(0, Mathf.FloorToInt(pc.x - R2 - bed.x0)), i1 = Mathf.Min(bed.nx - 1, Mathf.FloorToInt(pc.x + R2 - bed.x0));
                int j0 = Mathf.Max(0, Mathf.FloorToInt(pc.y - R2 - bed.z0)), j1 = Mathf.Min(bed.nz - 1, Mathf.FloorToInt(pc.y + R2 - bed.z0));
                if (gate > 0f)
                    for (int j = j0; j <= j1; j++)
                    {
                        float cz = bed.z0 + j + 0.5f - pc.y;
                        for (int i = i0; i <= i1; i++)
                        {
                            float cx = bed.x0 + i + 0.5f - pc.x;
                            if (cx * cx + cz * cz > R2 * R2) continue;
                            en += bed.Count(i, j, hdN - 2.5f, hdN + 2.5f) * gate;
                        }
                    }
                ratios.Add((float)(en / perLeg[c]));
                rawRatios.Add(perNewR[c] / perLeg[c]);
            }
            ratios.Sort();
            rawRatios.Sort();
            int tenth = Mathf.Max(1, ratios.Count / 10);
            float lo10 = 0f, hi10 = 0f, hiRaw = 0f;
            for (int t = 0; t < tenth && t < ratios.Count; t++)
            {
                lo10 += ratios[t];
                hi10 += ratios[ratios.Count - 1 - t];
                hiRaw += rawRatios[rawRatios.Count - 1 - t];
            }
            budget.worst10 = ratios.Count > 0 ? lo10 / tenth : 0f;
            budget.best10 = ratios.Count > 0 ? hi10 / tenth : 0f;
            budget.best10Raw = rawRatios.Count > 0 ? hiRaw / tenth : 0f;
            return budget;
        }
    }
}
