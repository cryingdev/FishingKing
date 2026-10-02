using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

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
        }

        /// <summary>The least water a fish of this size swims in (m): clamp(0.2 + 0.4 cm / 100, 0.3, 1.2).</summary>
        public static float MinWater(float cm) => HabitatModel.MinWater(cm);

        /// <summary>The half width fish use at z (as the old uniform draw: the 600 px view, within the stage's x limit).</summary>
        public float Half(float z) => Mathf.Min(L.xLim - 0.5f, stage.P.VisibleHalfWidth(z, 600));

        static HabSpecies Hab(FishSpecies sp) => new HabSpecies { id = sp.id, dMin = sp.depthMin, dMax = sp.depthMax, maxCm = sp.maxCm, h = sp.habitat };

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
        public static RigClass RigOf(Tackle tk)
        {
            var b = tk.Bait;
            if (b == null || !b.isLure)
            {
                float d = tk.FloatDepth;
                return d <= 1.0f ? RigClass.F1 : d <= 2.5f ? RigClass.F2 : d <= 4.5f ? RigClass.F4 : RigClass.F6;
            }
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
            Debug.Log(string.Format(CI, "[HAB] budget {0} {1} {2} rod {3:0} raw {4:0.00} scale {5:0.00} best10 {6:0.00} worst10 {7:0.00}",
                sp.id, GameClock.Id((Period)period), rig, castDist, bu.raw, bu.scale, bu.best10, bu.worst10));
            return bu.scale;
        }

        /// <summary>Test hook (-fkauto depth): the estimator for this species, period, rig class and cast distance.</summary>
        public HabitatModel.Budget Budget(FishSpecies sp, int period, RigClass rig, float castDist, bool full)
        {
            float zMax = Mathf.Min(L.zFar - 2f, castDist + 14f);
            var s = Get(sp, period, L.zNear + 1.5f, zMax);
            float reach = HabitatModel.IsLure(rig) ? 7f : 5f;
            string ck = castDist.ToString("0.0", CI) + "/" + reach.ToString("0", CI);
            if (!castSets.TryGetValue(ck, out var cs))
                castSets[ck] = cs = HabitatModel.Casts(B, L, s.reg, castDist, BathyGen.AnchorX(L.id), reach);
            return HabitatModel.Estimate(B, L, cs, Hab(sp), period, rig, s.cum, s.total, full);
        }

        /// <summary>Test hook: the sampler's region nodes and cumulative weights (D7's draws).</summary>
        internal (HabitatModel.Region reg, float[] cum, float total) DebugSampler(FishSpecies sp, int period, float zMax)
        {
            var s = Get(sp, period, L.zNear + 1.5f, zMax);
            return (s.reg, s.cum, s.total);
        }
    }
}
