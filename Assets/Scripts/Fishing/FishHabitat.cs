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
    /// The fish's ecology on the generated bed (Docs/terrain_depth_spec.md 7, Docs/lake_phase2_spec.md A3-A7), made by
    /// FishingController.Init when the stage has one (the lake): where a fish swims to and is stocked (a node drawn by its
    /// habitat's density, its depth band relative to this lake), how deep it swims there (its column, shifted by the
    /// period), the least water it swims in, the stage's spawn weights derived from the bed (each species' base by rarity x
    /// how much of its habitat this lake has x its activity), and the feeding chance F a fish in reach of the rig rolls once
    /// per encounter, set so the lake's catches and income per minute stay today's (the economy estimate, run on worker
    /// threads at the stage's start). The math is <see cref="HabitatModel"/> and <see cref="LakeEconomy"/>; this holds the
    /// caches and draws with UnityEngine.Random.
    /// </summary>
    public sealed class FishHabitat
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public readonly Bathymetry B;
        readonly StageView stage;
        readonly FishingController ctl;
        readonly bool quiet;
        readonly Dictionary<string, Sampler> samplers = new Dictionary<string, Sampler>();
        readonly Dictionary<string, HabitatModel.Region> regions = new Dictionary<string, HabitatModel.Region>();
        readonly Dictionary<string, HabSpecies> habs = new Dictionary<string, HabSpecies>();
        readonly Dictionary<string, float[]> avail = new Dictionary<string, float[]>();
        // where the fish spend their time (the economy's estimate): on this bed per species, period and fish region; today's
        // lake per stage, species and fish region (the same for every seed). Made on worker threads (Prewarm).
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

        /// <summary>The stage's spawn weights on this bed (Docs/lake_phase2_spec.md A4): the ordinary species' availability A and W per period.</summary>
        public HabitatModel.Derived Derived { get; }

        public FishHabitat(StageView stage, FishingController ctl, Bathymetry b, bool quiet = false)
        {
            this.stage = stage;
            this.ctl = ctl;
            this.quiet = quiet;
            B = b;
            halfZ0 = stage.L.zNear - 1f;
            int n = Mathf.Max(2, Mathf.CeilToInt((stage.L.zFar + 1f - halfZ0) / HalfStep) + 1);
            halfTab = new float[n];
            for (int i = 0; i < n; i++) halfTab[i] = Half(halfZ0 + i * HalfStep);
            // the derived spawn weights over the stage's ordinary species (its roster's weight is the base: the rarity's, or an override)
            var list = new List<HabitatModel.WeightIn>();
            foreach (var kv in stage.Def.spawns)
            {
                var sp = GameDatabase.GetFish(kv.Key);
                if (sp == null || sp.encounter != null || !(kv.Value > 0f)) continue;
                list.Add(new HabitatModel.WeightIn { s = Hab(sp), baseW = kv.Value, act = Act(sp) });
            }
            Derived = HabitatModel.DerivedWeights(b, list);
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < Derived.ids.Length; s++)
            {
                avail[Derived.ids[s]] = new[] { Derived.A[s, 0], Derived.A[s, 1], Derived.A[s, 2], Derived.A[s, 3] };
                sb.Append(string.Format(CI, " {0} A={1:0.00}/{2:0.00}/{3:0.00}/{4:0.00} W={5:0.0}/{6:0.0}/{7:0.0}/{8:0.0}", Derived.ids[s],
                    Derived.A[s, 0], Derived.A[s, 1], Derived.A[s, 2], Derived.A[s, 3], Derived.W[s, 0], Derived.W[s, 1], Derived.W[s, 2], Derived.W[s, 3]));
            }
            if (!quiet) Debug.Log(string.Format(CI, "[HAB] weights seed {0}:{1}", b.WorldSeed, sb));
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

        static float[] Act(FishSpecies sp) => new[] { TimeActivity.A(sp.id, Period.Dawn), TimeActivity.A(sp.id, Period.Day), TimeActivity.A(sp.id, Period.Evening), TimeActivity.A(sp.id, Period.Night) };

        /// <summary>A species as the habitat model sees it on a bed (its relative band resolved there).</summary>
        public static HabSpecies HabOf(FishSpecies sp, Bathymetry b) => new HabSpecies
        {
            id = sp.id, dMin = sp.depthMin, dMax = sp.depthMax, minCm = sp.minCm, maxCm = sp.maxCm, speed = sp.speed,
            coverSeek = sp.coverSeek, h = sp.habitat, res = HabitatModel.Resolve(sp.habitat, b),
        };

        /// <summary>The species on this bed (cached; its band logged once: [HAB] band).</summary>
        HabSpecies Hab(FishSpecies sp)
        {
            if (habs.TryGetValue(sp.id, out var h)) return h;
            h = HabOf(sp, B);
            habs[sp.id] = h;
            if (h.res != null && !quiet)
                for (int p = 0; p < 4; p++)
                    Debug.Log(string.Format(CI, "[HAB] band {0} {1} p{2:0}-p{3:0} = {4:0.00}..{5:0.00} m (swim shift {6:+0.00;-0.00} m)",
                        sp.id, GameClock.Id((Period)p), h.res.lo[p], h.res.hi[p], h.res.loM[p], h.res.hiM[p], h.res.swimShiftM[p]));
            return h;
        }

        /// <summary>Test hook: the species on this bed.</summary>
        internal HabSpecies DebugHab(FishSpecies sp) => Hab(sp);

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
        /// bottom species within 2 m of the bed, both shifted by half the period's shift (a relative band's in metres on this
        /// lake); at least 0.3. A species without a habitat keeps today's rule.
        /// </summary>
        public float SwimDepth(FishSpecies sp, int period, float water)
        {
            if (sp.habitat == null)
            {
                float bottom = water - 0.3f;
                return Mathf.Max(0.3f, Random.Range(Mathf.Min(sp.depthMin, bottom), Mathf.Min(sp.depthMax, bottom)));
            }
            HabitatModel.SwimRange(sp.habitat, Hab(sp).res, sp.depthMin, sp.depthMax, period, water, out float lo, out float hi);
            return Mathf.Max(0.3f, Random.Range(lo, hi));
        }

        // ------------------------------------------------------------------ the derived spawn weights (Docs/lake_phase2_spec.md A4)
        /// <summary>
        /// FishSpawner.Pick's weight for a species on this bed: its roster base x its availability here (the clock's two
        /// periods blended) x its activity now. A species the stage does not list ordinarily (a test's -fkfish) keeps A = 1.
        /// </summary>
        public float SpawnWeight(FishSpecies sp, float baseW, PeriodBlend look)
        {
            float a = TimeActivity.A(sp.id, look);
            if (!avail.TryGetValue(sp.id, out var A)) return baseW * a;
            return baseW * Mathf.Lerp(A[(int)look.From], A[(int)look.To], look.F) * a;
        }

        // ------------------------------------------------------------------ the feeding chance (Docs/lake_phase2_spec.md A5, A7)
        /// <summary>
        /// The feeding chance before this bed's estimate is ready: the 50-seed medians of -fkauto depth D11' (bamboo 0.506,
        /// carbon 0.451, dragon 0.405), the rods it does not measure lerped by their cast distance.
        /// </summary>
        public static float FDefault(string rodId) => rodId switch
        {
            "rod_bamboo" => 0.506f,
            "rod_glass" => 0.478f,
            "rod_carbon" => 0.451f,
            "rod_biggame" => 0.440f,
            "rod_surf" => 0.413f,
            "rod_dragon" => 0.405f,
            _ => 0.45f,
        };

        static readonly Dictionary<string, float> feedCache = new Dictionary<string, float>();
        readonly object feedLock = new object();
        float feed = -1f, feedCd = -1f, wantCd = -1f;

        static string FeedKey(uint hash, float castDist) => hash.ToString("x8", CI) + "/" + castDist.ToString("0.0", CI);

        /// <summary>A bed's F for a rod from an estimate already made (any FishHabitat of it), or false.</summary>
        public static bool CachedFeed(uint hash, float castDist, out float f)
        {
            lock (feedCache) return feedCache.TryGetValue(FeedKey(hash, castDist), out f);
        }

        /// <summary>
        /// The chance a fish in reach of the rig is feeding (rolled once per encounter, FishingController.WantsToApproach):
        /// this bed's F for the rod in hand once its estimate is ready (a new rod starts one), else <see cref="FDefault"/>.
        /// </summary>
        public float FeedP
        {
            get
            {
                float cd = Game.I.Rod.castDist;
                lock (feedLock)
                {
                    if (feed >= 0f && Mathf.Approximately(feedCd, cd)) return feed;
                }
                if (CachedFeed(B.Hash, cd, out float f))
                {
                    lock (feedLock)
                    {
                        feed = f;
                        feedCd = cd;
                    }
                    return f;
                }
                if (!Mathf.Approximately(wantCd, cd)) Prewarm(cd);
                return FDefault(Game.I.Rod.id);
            }
        }

        /// <summary>The fish region's far end for a rod (FishingController.FishZMax).</summary>
        public float ZMax(float castDist) => Mathf.Min(L.zFar - 2f, castDist + 14f);

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

        /// <summary>The species' time on today's lake (no bed, the authored profile; the same for every seed, so shared by every FishHabitat).</summary>
        Lazy<HabitatModel.Occupancy> OccToday(FishSpecies sp, float zMax)
        {
            string key = L.id + "/" + sp.id + "/" + zMax.ToString("0.0", CI);
            lock (occToday)
            {
                if (occToday.TryGetValue(key, out var lz)) return lz;
                var w = new HabitatModel.SimWorld { L = L, zMin = L.zNear + 1.5f, zMax = zMax, half = HalfTabled, covers = Covers(sp, zMax) };
                var hs = new HabSpecies { id = sp.id, dMin = sp.depthMin, dMax = sp.depthMax, minCm = sp.minCm, maxCm = sp.maxCm, speed = sp.speed, coverSeek = sp.coverSeek, h = sp.habitat };
                uint seed = HabitatModel.Fnv(sp.id) ^ (uint)(zMax * 10f) * 2654435761u ^ 0x51ED27u;
                lz = new Lazy<HabitatModel.Occupancy>(() => HabitatModel.Simulate(w, hs, 0, seed, HabitatModel.SimFishToday), LazyThreadSafetyMode.ExecutionAndPublication);
                occToday[key] = lz;
                return lz;
            }
        }

        /// <summary>An economy species: its habitat, rarity, activity, appeal for every bait, price and stock weight per period.</summary>
        static LakeEconomy.Species EcoSpecies(FishSpecies sp, HabSpecies h, float[] w)
        {
            var e = new LakeEconomy.Species { h = h, rarity = sp.rarity, act = Act(sp), price = sp.Price, w = w, xp = RarityInfo.BaseXp(sp.rarity) };
            foreach (var bt in GameDatabase.Baits)
            {
                float a = sp.Appeal(bt);
                if (a > 0f) e.appeal[bt.id] = a;
            }
            return e;
        }

        /// <summary>
        /// One economy estimate for a rod (Docs/lake_phase2_spec.md A7): both sides (this bed with its stock, population and
        /// derived weights; today's lake with the legacy stock) over the reference player's fan and rigs. Made on the main
        /// thread (the databases, the obstacles, the samplers); <see cref="Run"/> anywhere.
        /// </summary>
        public sealed class EcoJob
        {
            public uint hash;
            public int seed;
            public float castDist, zMax, stealth, biteMult;
            public StageLayout L;
            public Bathymetry b;
            public LakeEconomy.Side side, today;
            public HabitatModel.CastSet cs;
            public LakeEconomy.Rig[] rigs;
            internal Lazy<HabitatModel.Occupancy>[,] newOcc, todayOcc;
            /// <summary>After <see cref="Run"/>: both sides at F = 1, the ratios, F (and whether it hit its clamp), the time taken.</summary>
            public LakeEconomy.Eval Today, New;
            public double C1, I1;
            public float F;
            public bool clamped;
            public double ms;

            /// <summary>The simulations (at most <paramref name="threads"/> at a time), then the two sides and F.</summary>
            public void Run(int threads)
            {
                var sw = Stopwatch.StartNew();
                var all = new List<Lazy<HabitatModel.Occupancy>>();
                foreach (var lz in todayOcc) if (lz != null) all.Add(lz);
                foreach (var lz in newOcc) if (lz != null) all.Add(lz);
                if (threads <= 1) foreach (var lz in all) _ = lz.Value;
                else Parallel.ForEach(all, new ParallelOptions { MaxDegreeOfParallelism = threads }, lz => _ = lz.Value);
                Fill();
                Today = LakeEconomy.Evaluate(cs, today, rigs, LakeEconomy.PeriodW, stealth, biteMult);
                New = LakeEconomy.Evaluate(cs, side, rigs, LakeEconomy.PeriodW, stealth, biteMult, true);
                C1 = Today.C > 0 ? New.C / Today.C : 1;
                I1 = Today.I > 0 ? New.I / Today.I : 1;
                F = LakeEconomy.Feed(C1, I1, out clamped);
                ms = sw.Elapsed.TotalMilliseconds;
            }

            void Fill()
            {
                side.occ = new HabitatModel.Occupancy[side.species.Count, 4];
                today.occ = new HabitatModel.Occupancy[today.species.Count, 4];
                for (int s = 0; s < side.species.Count; s++)
                    for (int p = 0; p < 4; p++) side.occ[s, p] = newOcc[s, p].Value;
                for (int s = 0; s < today.species.Count; s++)
                    for (int p = 0; p < 4; p++) today.occ[s, p] = todayOcc[s, 0].Value;
            }

            /// <summary>
            /// The estimate for other casts and rigs (the live soak's spots), after <see cref="Run"/>: both sides over
            /// <paramref name="at"/>, the new one at feeding chance <paramref name="f"/>.
            /// </summary>
            public (LakeEconomy.Eval today, LakeEconomy.Eval bed) Predict(Vector2[] at, LakeEconomy.Rig[] soakRigs, float[] periodW, float f)
            {
                var c = HabitatModel.CastsAt(b, L, at, castDist);
                var t = LakeEconomy.Evaluate(c, today, soakRigs, periodW, stealth, biteMult);
                float was = side.feed;
                side.feed = f;
                var n = LakeEconomy.Evaluate(c, side, soakRigs, periodW, stealth, biteMult);
                side.feed = was;
                return (t, n);
            }
        }

        /// <summary>
        /// The economy job for a rod (main thread): the stage's ordinary species with this bed's derived weights and
        /// occupancies (its population, F = 1), today's legacy stock (8 fish) on the authored lake, the reference fan and rigs.
        /// </summary>
        public EcoJob Economy(float castDist)
        {
            float zMax = ZMax(castDist);
            var job = new EcoJob
            {
                hash = B.Hash, seed = B.WorldSeed, castDist = castDist, zMax = zMax, L = L, b = B,
                stealth = GameDatabase.GetItem<LineDef>(GameDatabase.StarterLine)?.stealth ?? 1f, biteMult = stage.Def.biteMult,
                cs = HabitatModel.Casts(B, L, castDist, BathyGen.AnchorX(L.id)),
                rigs = LakeEconomy.Reference(id => RigOf(GameDatabase.GetItem<BaitDef>(id), 0f)),
                side = new LakeEconomy.Side { population = stage.Def.population },
                today = new LakeEconomy.Side { population = LakeEconomy.PopToday, today = true },
            };
            var ids = Derived.ids;
            job.newOcc = new Lazy<HabitatModel.Occupancy>[ids.Length, 4];
            for (int s = 0; s < ids.Length; s++)
            {
                var sp = GameDatabase.GetFish(ids[s]);
                job.side.species.Add(EcoSpecies(sp, Hab(sp), new[] { Derived.W[s, 0], Derived.W[s, 1], Derived.W[s, 2], Derived.W[s, 3] }));
                for (int p = 0; p < 4; p++) job.newOcc[s, p] = OccBed(sp, p, zMax);
            }
            job.todayOcc = new Lazy<HabitatModel.Occupancy>[LakeEconomy.LegacyIds.Length, 1];
            for (int s = 0; s < LakeEconomy.LegacyIds.Length; s++)
            {
                var sp = GameDatabase.GetFish(LakeEconomy.LegacyIds[s]);
                var act = Act(sp);
                var w = new float[4];
                for (int p = 0; p < 4; p++) w[p] = LakeEconomy.LegacyW[s] * act[p];
                var hs = new HabSpecies { id = sp.id, dMin = sp.depthMin, dMax = sp.depthMax, minCm = sp.minCm, maxCm = sp.maxCm, speed = sp.speed, coverSeek = sp.coverSeek, h = sp.habitat };
                job.today.species.Add(EcoSpecies(sp, hs, w));
                job.todayOcc[s, 0] = OccToday(sp, zMax);
            }
            return job;
        }

        /// <summary>The last economy job this FishHabitat ran (the live soak's prediction).</summary>
        public EcoJob LastJob { get; private set; }

        /// <summary>
        /// Makes this bed's economy estimate for the rod on worker threads (at most half the cores) and sets F
        /// ([ECO] seed .. rod .. C1 .. I1 .. -> F ..); the main thread never waits (F_default meanwhile).
        /// </summary>
        public void Prewarm(float castDist)
        {
            wantCd = castDist;
            if (CachedFeed(B.Hash, castDist, out _)) return;
            var job = Economy(castDist);
            int threads = Mathf.Max(1, Environment.ProcessorCount / 2);
            Task.Run(() =>
            {
                try
                {
                    job.Run(threads);
                    lock (feedCache) feedCache[FeedKey(job.hash, castDist)] = job.F;
                    LastJob = job;
                    Debug.Log(string.Format(CI, "[ECO] seed {0} rod {1:0} C1 {2:0.000} I1 {3:0.000} -> F {4:0.000}{5} (today C {6:0.0000} I {7:0.000}; {8} + {9} simulations, {10:0} ms on {11} threads)",
                        job.seed, castDist, job.C1, job.I1, job.F, job.clamped ? " CLAMPED" : "", job.Today.C, job.Today.I, job.newOcc.Length, job.todayOcc.Length, job.ms, threads));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[ECO] estimate failed (F stays the default): " + e);
                }
            });
        }

        // ------------------------------------------------------------------ rig classes
        /// <summary>The rig's class: a float rig by its float depth, a lure by its buoyancy.</summary>
        public static RigClass RigOf(Tackle tk) => RigOf(tk.Bait, tk.FloatDepth);

        /// <summary>A bait's rig class (a float rig set at <paramref name="floatDepth"/>; a lure by its buoyancy).</summary>
        public static RigClass RigOf(BaitDef b, float floatDepth)
        {
            if (b == null || !b.isLure)
                return floatDepth <= 1.0f ? RigClass.F1 : floatDepth <= 2.5f ? RigClass.F2 : floatDepth <= 4.5f ? RigClass.F4 : RigClass.F6;
            return b.buoyancy == Buoyancy.Float ? RigClass.Surface : b.buoyancy == Buoyancy.Suspend ? RigClass.Mid : RigClass.Bottom;
        }

        /// <summary>Test hook: the sampler's region nodes and cumulative weights (D7's draws).</summary>
        internal (HabitatModel.Region reg, float[] cum, float total) DebugSampler(FishSpecies sp, int period, float zMax)
        {
            var s = Get(sp, period, L.zNear + 1.5f, zMax);
            return (s.reg, s.cum, s.total);
        }
    }
}
