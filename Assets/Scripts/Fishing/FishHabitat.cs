using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace FishingKing
{
    /// <summary>
    /// The fish's ecology on the generated bed (Docs/terrain_depth_spec.md 7, 8), made by FishingController.Init when the
    /// stage has one (the lake): where a fish swims to and is stocked (a node drawn by its habitat's density instead of a
    /// uniform x and z), how deep it swims there (its column, shifted by the period), the least water it swims in, and the
    /// bite budget's reach scale (how far a fish senses the rig, so the bites per minute of a rig at a time of day stay
    /// today's). The math is <see cref="HabitatModel"/>; this holds the caches and draws with UnityEngine.Random.
    /// </summary>
    public sealed class FishHabitat
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public readonly Bathymetry B;
        readonly StageView stage;
        readonly FishingController ctl;
        readonly Dictionary<string, Sampler> samplers = new Dictionary<string, Sampler>();
        readonly Dictionary<string, HabitatModel.Region> regions = new Dictionary<string, HabitatModel.Region>();
        readonly Dictionary<string, HabitatModel.CastSet> castSets = new Dictionary<string, HabitatModel.CastSet>();
        readonly Dictionary<string, float> scales = new Dictionary<string, float>();
        // where the fish spend their time (spec 8.2): on this bed per species, period and fish region; today's lake per stage,
        // species and fish region (the same for every seed). Made on demand, or ahead on a worker (Prewarm).
        readonly Dictionary<string, Lazy<HabitatModel.Occupancy>> occBed = new Dictionary<string, Lazy<HabitatModel.Occupancy>>();
        static readonly Dictionary<string, Lazy<HabitatModel.Occupancy>> occToday = new Dictionary<string, Lazy<HabitatModel.Occupancy>>();
        // the half width by z, tabled for the simulation (it may run on a worker thread)
        readonly float[] halfTab;
        readonly float halfZ0;
        const float HalfStep = 0.25f;

        sealed class Sampler
        {
            public HabitatModel.Region reg;
            public float[] cum;
            public float total;
        }

        StageLayout L => stage.L;

        public FishHabitat(StageView stage, FishingController ctl, Bathymetry b)
        {
            this.stage = stage;
            this.ctl = ctl;
            B = b;
            halfZ0 = stage.L.zNear - 1f;
            int n = Mathf.Max(2, Mathf.CeilToInt((stage.L.zFar + 1f - halfZ0) / HalfStep) + 1);
            halfTab = new float[n];
            for (int i = 0; i < n; i++) halfTab[i] = Half(halfZ0 + i * HalfStep);
        }

        /// <summary>The least water a fish of this size swims in (m): clamp(0.2 + 0.4 cm / 100, 0.3, 1.2).</summary>
        public static float MinWater(float cm) => HabitatModel.MinWater(cm);

        /// <summary>The half width fish use at z (as the old uniform draw: the 600 px view, within the stage's x limit).</summary>
        public float Half(float z) => Mathf.Min(L.xLim - 0.5f, stage.P.VisibleHalfWidth(z, 600));

        /// <summary><see cref="Half"/> from the table (linear between 0.25 m steps): safe off the main thread.</summary>
        float HalfTabled(float z)
        {
            float t = (z - halfZ0) / HalfStep;
            if (t <= 0f) return halfTab[0];
            int i = (int)t;
            if (i >= halfTab.Length - 1) return halfTab[halfTab.Length - 1];
            return halfTab[i] + (halfTab[i + 1] - halfTab[i]) * (t - i);
        }

        static HabSpecies Hab(FishSpecies sp) => new HabSpecies
        {
            id = sp.id, dMin = sp.depthMin, dMax = sp.depthMax, minCm = sp.minCm, maxCm = sp.maxCm, speed = sp.speed,
            coverSeek = sp.coverSeek, h = sp.habitat,
        };

        HabitatModel.Region Region(float zMin, float zMax)
        {
            string key = zMin.ToString("0.00", CI) + "/" + zMax.ToString("0.00", CI);
            if (!regions.TryGetValue(key, out var r)) regions[key] = r = HabitatModel.MakeRegion(B, zMin, zMax, Half);
            return r;
        }

        Sampler Get(FishSpecies sp, int period, float zMin, float zMax)
        {
            string key = sp.id + "/" + period + "/" + zMin.ToString("0.00", CI) + "/" + zMax.ToString("0.00", CI);
            if (samplers.TryGetValue(key, out var s)) return s;
            var reg = Region(zMin, zMax);
            var cum = HabitatModel.Weights(B, reg, Hab(sp), period, out float total);
            s = new Sampler { reg = reg, cum = cum, total = total };
            samplers[key] = s;
            return s;
        }

        /// <summary>The period a draw uses: the clock's blend picks the outgoing or the incoming one.</summary>
        public static int DrawPeriod()
        {
            var look = GameClock.Look;
            return (int)(Random.value < look.F ? look.To : look.From);
        }

        /// <summary>A point drawn by the species' density over z in [zMin, zMax]: a node, jittered within its cell; y its swim depth there.</summary>
        bool Draw(FishSpecies sp, float zMin, float zMax, out Vector3 p)
        {
            int period = DrawPeriod();
            var s = Get(sp, period, zMin, zMax);
            if (s.total <= 0f || s.reg.nodes.Length == 0)
            {
                p = default;
                return false;
            }
            int q = HabitatModel.Pick(s.cum, Random.value * s.total);
            var np = B.NodePos(s.reg.nodes[q]);
            float x = np.x + Random.Range(-0.25f, 0.25f), z = np.y + Random.Range(-0.25f, 0.25f);
            z = Mathf.Clamp(z, zMin, zMax);
            float hw = Half(z);
            x = Mathf.Clamp(x, -hw, hw);
            // (jittered into shallower water than the node's: the node itself, which has water enough for its biggest)
            if (L.DepthAt(x, z) < HabitatModel.MinWater(sp.maxCm))
            {
                x = np.x;
                z = np.y;
            }
            p = new Vector3(x, -Mathf.Max(0.3f, SwimDepth(sp, period, L.DepthAt(x, z))), z);
            return true;
        }

        /// <summary>A wander target for this fish (spec 7.4): the target region z in [zNear + 1.5, zMax].</summary>
        public bool Target(FishAgent f, float zMax, out Vector3 target) => Draw(f.Sp, L.zNear + 1.5f, zMax, out target);

        /// <summary>Where a new fish is stocked: z in [zNear + 2, zMax], or the far band [zMax - 4, zMax] (one swimming in).</summary>
        public Vector3 SpawnPoint(FishSpecies sp, bool fromDistance, float zMax)
        {
            float zMin = fromDistance ? zMax - 4f : L.zNear + 2f;
            if (Draw(sp, zMin, zMax, out var p)) return p;
            // (no water deep enough for it there: the old uniform draw)
            float z = Random.Range(zMin, zMax);
            float hw = Half(z);
            float x = Random.Range(-hw, hw);
            return new Vector3(x, -Mathf.Max(0.3f, SwimDepth(sp, DrawPeriod(), L.DepthAt(x, z))), z);
        }

        /// <summary>
        /// How deep it swims over water <paramref name="water"/> m deep (spec 7.5): mid-water species between their depths,
        /// bottom species within 2 m of the bed, both shifted by half the period's shift; at least 0.3. A species without a
        /// habitat keeps today's rule.
        /// </summary>
        public float SwimDepth(FishSpecies sp, int period, float water)
        {
            if (sp.habitat == null)
            {
                float bottom = water - 0.3f;
                return Mathf.Max(0.3f, Random.Range(Mathf.Min(sp.depthMin, bottom), Mathf.Min(sp.depthMax, bottom)));
            }
            HabitatModel.SwimRange(sp.habitat, sp.depthMin, sp.depthMax, period, water, out float lo, out float hi);
            return Mathf.Max(0.3f, Random.Range(lo, hi));
        }

        // ------------------------------------------------------------------ the bite budget (spec 8)
        /// <summary>The rig's class: a float rig by its float depth, a lure by its buoyancy.</summary>
        public static RigClass RigOf(Tackle tk) => RigOf(tk.Bait, tk.FloatDepth);

        /// <summary>A bait's rig class (a float rig set at <paramref name="floatDepth"/>; a lure by its buoyancy).</summary>
        public static RigClass RigOf(BaitDef b, float floatDepth)
        {
            if (b == null || !b.isLure)
                return floatDepth <= 1.0f ? RigClass.F1 : floatDepth <= 2.5f ? RigClass.F2 : floatDepth <= 4.5f ? RigClass.F4 : RigClass.F6;
            return b.buoyancy == Buoyancy.Float ? RigClass.Surface : b.buoyancy == Buoyancy.Suspend ? RigClass.Mid : RigClass.Bottom;
        }

        /// <summary>
        /// The reach scale for this fish's species and the rig now: the outgoing and incoming periods' scales blended by the
        /// clock (each computed once per species, period, rig class and rod, spec 8.2).
        /// </summary>
        public float ReachScale(FishSpecies sp, PeriodBlend look, Tackle tk)
        {
            var rig = RigOf(tk);
            float cd = Game.I.Rod.castDist;
            float a = Scale(sp, (int)look.From, rig, cd);
            if (look.F <= 0f || look.To == look.From) return a;
            return Mathf.Lerp(a, Scale(sp, (int)look.To, rig, cd), look.F);
        }

        float Scale(FishSpecies sp, int period, RigClass rig, float castDist)
        {
            string key = sp.id + "/" + period + "/" + rig + "/" + castDist.ToString("0.0", CI);
            if (scales.TryGetValue(key, out float s)) return s;
            var bu = Budget(sp, period, rig, castDist, true);
            scales[key] = bu.scale;
            Debug.Log(string.Format(CI, "[HAB] budget {0} {1} {2} rod {3:0} raw {4:0.00} scale {5:0.00} scaled {6:0.00} best10 {7:0.00} worst10 {8:0.00}",
                sp.id, GameClock.Id((Period)period), rig, castDist, bu.raw, bu.scale, bu.scaled, bu.best10, bu.worst10));
            return bu.scale;
        }

        /// <summary>The fish region's far end for a rod (FishingController.FishZMax).</summary>
        float ZMax(float castDist) => Mathf.Min(L.zFar - 2f, castDist + 14f);

        /// <summary>Test hook (-fkauto depth): the estimator for this species, period, rig class and cast distance.</summary>
        public HabitatModel.Budget Budget(FishSpecies sp, int period, RigClass rig, float castDist, bool full)
        {
            float zMax = ZMax(castDist);
            float reach = HabitatModel.IsLure(rig) ? 7f : 5f;
            string ck = castDist.ToString("0.0", CI) + "/" + reach.ToString("0", CI);
            if (!castSets.TryGetValue(ck, out var cs))
                castSets[ck] = cs = HabitatModel.Casts(B, L, castDist, BathyGen.AnchorX(L.id), reach);
            return HabitatModel.Estimate(cs, Hab(sp), rig, OccBed(sp, period, zMax).Value, OccToday(sp, zMax).Value, full);
        }

        List<HabitatModel.CoverZone> Covers(FishSpecies sp, float zMax)
        {
            var list = new List<HabitatModel.CoverZone>();
            var obs = stage.Obstacles;
            if (obs == null || obs.Empty || sp.coverSeek <= 0f) return list;
            foreach (var c in obs.Covers)
            {
                if (!Obstacles.CoverMatch(c, sp) || c.hz > zMax || c.hz < L.zNear + 1f) continue;
                list.Add(new HabitatModel.CoverZone(c.Poly, c.C, c.hx, c.hz));
            }
            return list;
        }

        /// <summary>The species' time on this bed in a period (the simulation's world made here, on the main thread).</summary>
        Lazy<HabitatModel.Occupancy> OccBed(FishSpecies sp, int period, float zMax)
        {
            string key = sp.id + "/" + period + "/" + zMax.ToString("0.0", CI);
            if (occBed.TryGetValue(key, out var lz)) return lz;
            var s = Get(sp, period, L.zNear + 1.5f, zMax);
            var w = new HabitatModel.SimWorld
            {
                b = B, L = L, zMin = L.zNear + 1.5f, zMax = zMax, half = HalfTabled, reg = s.reg, cum = s.cum, total = s.total,
                covers = Covers(sp, zMax),
            };
            var hs = Hab(sp);
            uint seed = B.Hash ^ HabitatModel.Fnv(sp.id) ^ (uint)(period + 1) * 40503u ^ (uint)(zMax * 10f) * 2654435761u;
            lz = new Lazy<HabitatModel.Occupancy>(() => HabitatModel.Simulate(w, hs, period, seed), LazyThreadSafetyMode.ExecutionAndPublication);
            occBed[key] = lz;
            return lz;
        }

        /// <summary>The species' time on today's lake (no bed; the same for every seed, so shared by every FishHabitat).</summary>
        Lazy<HabitatModel.Occupancy> OccToday(FishSpecies sp, float zMax)
        {
            string key = L.id + "/" + sp.id + "/" + zMax.ToString("0.0", CI);
            lock (occToday)
            {
                if (occToday.TryGetValue(key, out var lz)) return lz;
                var w = new HabitatModel.SimWorld { L = L, zMin = L.zNear + 1.5f, zMax = zMax, half = HalfTabled, covers = Covers(sp, zMax) };
                var hs = Hab(sp);
                uint seed = HabitatModel.Fnv(sp.id) ^ (uint)(zMax * 10f) * 2654435761u ^ 0x51ED27u;
                lz = new Lazy<HabitatModel.Occupancy>(() => HabitatModel.Simulate(w, hs, 0, seed, HabitatModel.SimFishToday), LazyThreadSafetyMode.ExecutionAndPublication);
                occToday[key] = lz;
                return lz;
            }
        }

        /// <summary>
        /// Makes the estimator's simulations for the rod in hand ahead, on a worker thread (the stage's stock, today's lake
        /// and the bed's four periods, the period now first), so the first fish to sense the rig does not wait for them.
        /// </summary>
        public void Prewarm(float castDist)
        {
            float zMax = ZMax(castDist);
            var todo = new List<Lazy<HabitatModel.Occupancy>>();
            int now = (int)GameClock.Look.From;
            var species = new List<FishSpecies>();
            foreach (var kv in stage.Def.spawns)
            {
                var sp = GameDatabase.GetFish(kv.Key);
                if (sp != null && sp.encounter == null && kv.Value > 0f) species.Add(sp);
            }
            foreach (var sp in species) todo.Add(OccToday(sp, zMax));
            for (int k = 0; k < 4; k++)
                foreach (var sp in species)
                    todo.Add(OccBed(sp, (now + k) % 4, zMax));
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    foreach (var lz in todo) _ = lz.Value;
                    Debug.Log(string.Format(CI, "[HAB] prewarm: {0} simulations for rod {1:0} in {2:0} ms (worker)", todo.Count, castDist, sw.Elapsed.TotalMilliseconds));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HAB] prewarm failed (made on demand instead): " + e.Message);
                }
            });
        }

        /// <summary>Test hook: the sampler's region nodes and cumulative weights (D7's draws).</summary>
        internal (HabitatModel.Region reg, float[] cum, float total) DebugSampler(FishSpecies sp, int period, float zMax)
        {
            var s = Get(sp, period, L.zNear + 1.5f, zMax);
            return (s.reg, s.cum, s.total);
        }
    }
}
