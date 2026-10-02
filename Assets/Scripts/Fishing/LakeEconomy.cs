using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The lake's economy estimator (Docs/lake_phase2_spec.md A7), pure (no Unity state, no randomness; safe on a worker
    /// thread): what a reference player catches and earns per unit of time on a lake, from where the fish spend their time
    /// (<see cref="HabitatModel.Simulate"/>'s occupancy). The reference player casts evenly over the fan
    /// (<see cref="HabitatModel.Casts"/>), half the time with float rigs and half with lures (<see cref="Reference"/>), over
    /// the periods by their length (3 / 9 / 3 / 9 h). Per species s, period p, rig and cast c the expected takes are
    /// <c>lambda = N pi_s(p) F occ T</c>: N the stage's population, pi_s(p) the species' share of the stock's spawn weights,
    /// F the per-encounter feeding chance, occ the share of the species' fish-time within the rig's reach of the cast (5 m a
    /// float, 7 m a lure) whose depth is within 2.5 m of the hook (x the share of its sizes that swim in the water there),
    /// T = 1 - (1 - p)^8 the chance a fish in reach takes (it rolls the game's approach chance p about once a second while
    /// it is in reach). Income weights each take by the species' expected price for the sizes that swim there. "Today" is
    /// the lake before the bed: the authored profile, uniform targets, 8 fish of the legacy stock, F = 1. The economy rule:
    /// the lake's catches and income per minute within 0.8..1.25 of today's; the feeding chance F = clamp(1 / sqrt(C1 I1),
    /// 0.25, 1) from the ratios at F = 1 puts catches at 1 / sqrt(pi) and income at sqrt(pi), pi the mean price ratio.
    /// </summary>
    public static class LakeEconomy
    {
        /// <summary>Today's lake: its population, its stock (crucian 40, bluegill 35, carp 16, bass 14) and F = 1.</summary>
        public const int PopToday = 8;
        public static readonly string[] LegacyIds = { "crucian_carp", "bluegill", "carp", "largemouth_bass" };
        public static readonly float[] LegacyW = { 40f, 35f, 16f, 14f };

        /// <summary>The periods' lengths (h): dawn 3, day 9, evening 3, night 9.</summary>
        public static readonly float[] PeriodW = { 3f, 9f, 3f, 9f };

        /// <summary>The feeding chance's clamp, the approach rolls a fish in reach makes, the reaches, a worked lure's activity.</summary>
        public const float FMin = 0.25f, FMax = 1f;
        public const int TakeRolls = 8;
        public const float FloatReach = 5f, LureReach = 7f, LureAct = 0.15f + 1.25f * 0.6f;

        /// <summary>The economy rule's band.</summary>
        public const float BandLo = 0.8f, BandHi = 1.25f;

        /// <summary>A rig of the estimate: a bait at a rig class (a float's depth class, a lure's buoyancy) and its share of the time.</summary>
        public sealed class Rig
        {
            public string id, bait;
            public RigClass cls;
            public bool lure;
            public float weight;
        }

        /// <summary>
        /// The reference player's rigs: floats (half the time, equal) paste F2, paste on the bottom (F6), worm F1, worm on the
        /// bottom, corn on the bottom, shrimp F2; lures (half the time, equal) spinner, spoon, crank, minnow, soft worm,
        /// popper, frog, each by its buoyancy (<paramref name="lureClass"/>: FishHabitat.RigOf). The golden paste is left out.
        /// </summary>
        public static Rig[] Reference(Func<string, RigClass> lureClass)
        {
            var floats = new[] { ("paste2", "bait_paste", RigClass.F2), ("pasteB", "bait_paste", RigClass.F6), ("worm1", "bait_worm", RigClass.F1),
                ("wormB", "bait_worm", RigClass.F6), ("cornB", "bait_corn", RigClass.F6), ("shrimp2", "bait_shrimp", RigClass.F2) };
            var lures = new[] { "bait_spinner", "bait_spoon", "bait_crank", "bait_minnow", "bait_softworm", "bait_popper", "bait_frog" };
            var list = new List<Rig>();
            foreach (var (id, bait, cls) in floats) list.Add(new Rig { id = id, bait = bait, cls = cls, weight = 0.5f / floats.Length });
            foreach (var bait in lures) list.Add(new Rig { id = bait.Substring(5), bait = bait, cls = lureClass(bait), lure = true, weight = 0.5f / lures.Length });
            return list.ToArray();
        }

        /// <summary>
        /// The live soak's rigs by name (Docs/lake_phase2_spec.md A8): paste2 (paste, float at 2 m), pasteB (paste on the
        /// bottom: the float at 6 m), worm1, wormB, corn2, cornB, shrimp2, spinner (a lure by its buoyancy); equal weights.
        /// </summary>
        public static Rig SoakRig(string name, Func<string, RigClass> lureClass)
        {
            switch (name)
            {
                case "paste2": return new Rig { id = name, bait = "bait_paste", cls = RigClass.F2, weight = 1f };
                case "pasteB": return new Rig { id = name, bait = "bait_paste", cls = RigClass.F6, weight = 1f };
                case "worm1": return new Rig { id = name, bait = "bait_worm", cls = RigClass.F1, weight = 1f };
                case "wormB": return new Rig { id = name, bait = "bait_worm", cls = RigClass.F6, weight = 1f };
                case "corn2": return new Rig { id = name, bait = "bait_corn", cls = RigClass.F2, weight = 1f };
                case "cornB": return new Rig { id = name, bait = "bait_corn", cls = RigClass.F6, weight = 1f };
                case "shrimp2": return new Rig { id = name, bait = "bait_shrimp", cls = RigClass.F2, weight = 1f };
                default:
                    string bait = "bait_" + name;
                    return new Rig { id = name, bait = bait, cls = lureClass(bait), lure = true, weight = 1f };
            }
        }

        /// <summary>The float depth (m) a soak rig is set at (the bottom rigs at 6 m: the bait lies on the bed in shallower water).</summary>
        public static float SoakFloatDepth(RigClass cls) => cls == RigClass.F1 ? 1f : cls == RigClass.F2 ? 2f : cls == RigClass.F4 ? 4f : 6f;

        /// <summary>A species of a side: its habitat, its activity, its appeal per bait id, its price, its stock weight per period.</summary>
        public sealed class Species
        {
            public HabSpecies h;
            public Rarity rarity;
            public float[] act;
            /// <summary>Appeal by bait id (made on the main thread from FishSpecies.Appeal).</summary>
            public Dictionary<string, float> appeal = new Dictionary<string, float>();
            /// <summary>FishSpecies.Price (pure).</summary>
            public Func<float, int> price;
            /// <summary>The spawn weight per period (the derived W, or the legacy stock x a).</summary>
            public float[] w;
            public int xp;

            public float Appeal(string bait) => bait != null && appeal.TryGetValue(bait, out float v) ? v : 0f;
        }

        /// <summary>One lake as the estimate sees it: the population, the feeding chance, the species and where they spend their time.</summary>
        public sealed class Side
        {
            public int population;
            public float feed = 1f;
            /// <summary>The rod's luck (>= Rare) and the bait's rare boost (>= Epic) on the stock (information: dragon luck, golden paste).</summary>
            public float luck = 1f, rareBoost = 1f;
            public List<Species> species = new List<Species>();
            /// <summary>Per species and period (today: the same one in every period).</summary>
            public HabitatModel.Occupancy[,] occ;
            /// <summary>The water at the casts: today's authored profile (true) or the bed's.</summary>
            public bool today;
        }

        /// <summary>A side's estimate: the expected takes (C), income (I) and XP per unit time, and their breakdowns.</summary>
        public sealed class Eval
        {
            public double C, I, Xp;
            /// <summary>Per rig (periods by length, casts averaged; not weighted by the rig's share), per period (rigs by share), per species.</summary>
            public double[] rigC, rigI, perC, perI, spC, spI;
            /// <summary>Per species, rig and cast: the takes over the periods by length (the positive gate).</summary>
            public double[,,] spRigCast;
        }

        /// <summary>
        /// The estimate of one side over the casts, the rigs (by their weights) and the periods (by <paramref name="periodW"/>):
        /// takes, income and XP per unit time (the same units on both sides: only ratios are read). Pure; the per-cast window
        /// counts are made once per species, period and rig class.
        /// </summary>
        public static Eval Evaluate(HabitatModel.CastSet cs, Side side, Rig[] rigs, float[] periodW, float stealth, float biteMult, bool perCast = false)
        {
            int nc = cs.at.Length, nr = rigs.Length, ns = side.species.Count;
            var e = new Eval { rigC = new double[nr], rigI = new double[nr], perC = new double[4], perI = new double[4], spC = new double[ns], spI = new double[ns] };
            if (perCast) e.spRigCast = new double[ns, nr, nc];
            var water = side.today ? cs.profileWater : cs.gridWater;
            double pwSum = 0, rwSum = 0;
            for (int p = 0; p < 4; p++) pwSum += periodW[p];
            foreach (var r in rigs) rwSum += r.weight;
            if (pwSum <= 0 || rwSum <= 0 || nc == 0) return e;
            // the expected price of the sizes that swim in each cast's water, per species
            var ep = new float[ns, nc];
            for (int s = 0; s < ns; s++)
                for (int c = 0; c < nc; c++) ep[s, c] = ExpectedPrice(side.species[s], water[c]);
            var windows = new Dictionary<int, float[]>();
            float[] Window(int s, int p, RigClass cls)
            {
                int key = (s * 4 + p) * 8 + (int)cls;
                if (windows.TryGetValue(key, out var win)) return win;
                win = WindowShares(cs, water, side.species[s].h, side.occ[s, p], cls);
                windows[key] = win;
                return win;
            }
            for (int p = 0; p < 4; p++)
            {
                if (periodW[p] <= 0f) continue;
                double pw = periodW[p] / pwSum;
                double wsum = 0;
                var wEff = new double[ns];
                for (int s = 0; s < ns; s++)
                {
                    var sp = side.species[s];
                    double w = sp.w != null ? sp.w[p] : 0;
                    if (sp.rarity >= Rarity.Rare) w *= side.luck;
                    if (sp.rarity >= Rarity.Epic) w *= side.rareBoost;
                    wEff[s] = w;
                    wsum += w;
                }
                if (wsum <= 0) continue;
                for (int s = 0; s < ns; s++)
                {
                    var sp = side.species[s];
                    float a = sp.act != null ? sp.act[p] : 1f;
                    if (a <= 0f || wEff[s] <= 0) continue;
                    double pi = wEff[s] / wsum;
                    float m = Mathf.Clamp(Mathf.Sqrt(a), 0.25f, 2f);
                    for (int r = 0; r < nr; r++)
                    {
                        var rig = rigs[r];
                        float app = sp.Appeal(rig.bait);
                        if (app <= 0f) continue;
                        float pr = Mathf.Min(0.95f, app * stealth * biteMult * (rig.lure ? LureAct : 1f) * 0.45f * m);
                        double take = 1.0 - Math.Pow(1.0 - pr, TakeRolls);
                        double k = side.population * pi * side.feed * take;
                        var win = Window(s, p, rig.cls);
                        double sc = 0, si = 0;
                        for (int c = 0; c < nc; c++)
                        {
                            double lam = k * win[c];
                            sc += lam;
                            si += lam * ep[s, c];
                            if (perCast) e.spRigCast[s, r, c] += pw * lam;
                        }
                        sc /= nc;
                        si /= nc;
                        double rw = rig.weight / rwSum;
                        e.C += pw * rw * sc;
                        e.I += pw * rw * si;
                        e.Xp += pw * rw * sc * sp.xp;
                        e.rigC[r] += pw * sc;
                        e.rigI[r] += pw * si;
                        e.perC[p] += rw * sc;
                        e.perI[p] += rw * si;
                        e.spC[s] += pw * rw * sc;
                        e.spI[s] += pw * rw * si;
                    }
                }
            }
            return e;
        }

        /// <summary>
        /// Per cast: the share of the species' fish-time within the rig's reach (5 m a float, 7 m a lure, over the 1 m cells)
        /// whose depth is within 2.5 m of the hook's depth there, x the share of its sizes that swim in that water.
        /// </summary>
        public static float[] WindowShares(HabitatModel.CastSet cs, float[] water, HabSpecies s, HabitatModel.Occupancy occ, RigClass cls)
        {
            int nc = cs.at.Length;
            var o = new float[nc];
            if (occ == null || occ.samples <= 0) return o;
            float R = HabitatModel.IsLure(cls) ? LureReach : FloatReach;
            float inv = 1f / occ.samples;
            for (int c = 0; c < nc; c++)
            {
                var pc = cs.at[c];
                float gate = HabitatModel.SizeShare(s, water[c]);
                if (gate <= 0f) continue;
                float hd = HabitatModel.HookDepth(cls, water[c]);
                double n = 0;
                int i0 = Mathf.Max(0, Mathf.FloorToInt(pc.x - R - occ.x0)), i1 = Mathf.Min(occ.nx - 1, Mathf.FloorToInt(pc.x + R - occ.x0));
                int j0 = Mathf.Max(0, Mathf.FloorToInt(pc.y - R - occ.z0)), j1 = Mathf.Min(occ.nz - 1, Mathf.FloorToInt(pc.y + R - occ.z0));
                for (int j = j0; j <= j1; j++)
                {
                    float cz = occ.z0 + j + 0.5f - pc.y;
                    for (int i = i0; i <= i1; i++)
                    {
                        float cx = occ.x0 + i + 0.5f - pc.x;
                        if (cx * cx + cz * cz > R * R) continue;
                        n += occ.Count(i, j, hd - 2.5f, hd + 2.5f);
                    }
                }
                o[c] = (float)(n * inv) * gate;
            }
            return o;
        }

        /// <summary>The expected price of the species' sizes (32 quantiles of t = U^1.7) that swim in water this deep (MinWater).</summary>
        public static float ExpectedPrice(Species sp, float water)
        {
            if (sp.price == null) return 0f;
            double sum = 0;
            int n = 0;
            for (int i = 0; i < 32; i++)
            {
                double t = Math.Pow((i + 0.5) / 32.0, 1.7);
                float cm = sp.h.minCm + (sp.h.maxCm - sp.h.minCm) * (float)t;
                if (HabitatModel.MinWater(cm) > water) continue;
                sum += sp.price(cm);
                n++;
            }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        /// <summary>F = clamp(1 / sqrt(C1 I1), 0.25, 1) from the new lake's catches and income at F = 1 over today's.</summary>
        public static float Feed(double c1, double i1, out bool clamped)
        {
            double raw = c1 > 0 && i1 > 0 ? 1.0 / Math.Sqrt(c1 * i1) : 1.0;
            clamped = raw < FMin || raw > FMax;
            return (float)Math.Max(FMin, Math.Min(FMax, raw));
        }
    }
}
