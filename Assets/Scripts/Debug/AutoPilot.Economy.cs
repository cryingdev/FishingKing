using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto economy (Docs/lake_phase2_spec.md A8): the lake's live bites and income per minute, the new lake (its bed,
    /// 15 fish, derived weights, the feeding roll) or today's (-fkecomode legacy: no bed, 8 fish of the legacy stock), on
    /// the soak loop: the rod (-fkecorod, default the bamboo) with the starter reel and line, the clock frozen at the centre
    /// of each period (-fkecoperiods 0,1,2,3), the rigs (-fkecorigs paste2,pasteB,worm1,cornB; spinner too), each soaked
    /// -fkecosecs game seconds (default 30) at every spot of the reference fan (yaw -36..36 every 8 degrees x from zNear +
    /// 2.5 every 2 m out to the cast, from the walk's middle, moved off pads and weed / snag zones) on a fixed 1/60 s step;
    /// Random.InitState(1515) as each stage opens; a fresh stock every -fkecorestock spots (default 10) and at every rig (the
    /// stage reopened, Random.InitState(1515 + 1000 n)). Every bite is counted (species, cm, price) and let go. [ECO] spot / cell
    /// lines (pooled by hand across the processes: one per mode and period); with the bed also the estimator's prediction
    /// for the same spots and rigs ([ECO] predict, LakeEconomy via FishHabitat's job) and the frames a fish stood in water
    /// shallower than it swims in. Quits itself.
    /// </summary>
    public partial class AutoPilot
    {
        int ecoFails;

        void EcoCheck(string what, bool ok)
        {
            if (!ok) ecoFails++;
            Log($"[ECO] CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        /// <summary>
        /// The band rule on a ratio of two bite counts: with 100 bites on each side the point estimate within [0.8, 1.25];
        /// with fewer, its 95 % interval (log ratio +- 1.96 sqrt(1/a + 1/b), half a bite added to each) must reach the band.
        /// </summary>
        public static bool BiteBand(int on, float onSecs, int off, float offSecs, out float ratio, out string how)
        {
            float a = on + 0.5f, b = off + 0.5f;
            ratio = onSecs > 0f && offSecs > 0f ? (a / onSecs) / (b / offSecs) : 1f;
            if (on >= 100 && off >= 100)
            {
                how = "point";
                return ratio >= 0.8f && ratio <= 1.25f;
            }
            float se = Mathf.Sqrt(1f / a + 1f / b);
            float lo = ratio * Mathf.Exp(-1.96f * se), hi = ratio * Mathf.Exp(1.96f * se);
            how = string.Format(CIc, "95% {0:0.00}..{1:0.00}", lo, hi);
            return hi >= 0.8f && lo <= 1.25f;
        }

        /// <summary>
        /// A soak spot at (yaw, distance) from (anchorX, 0), moved to the nearest open water clear of pads, standing props and
        /// weed / snag zones (a float laid on a pad snags and is cut over and over): from the obstacles alone, so both modes
        /// soak the same spots.
        /// </summary>
        static Vector3 SoakSpot(FishingController ctl, float anchorX, float yaw, float dist)
        {
            var obs = ctl.Stage.Obstacles;
            var water = ctl.Stage.Water;
            float yr = yaw * Mathf.Deg2Rad;
            var b = new Vector2(anchorX + Mathf.Sin(yr) * dist, Mathf.Cos(yr) * dist);
            bool Clear(Vector2 p)
            {
                if (water != null && !water.OpenWater(p.x, p.y)) return false;
                if (obs == null || obs.Empty) return true;
                if (obs.BlockedAtSurface(p, 1f) || obs.PadAt(p) != null) return false;
                foreach (var o in obs.All)
                    if ((o.kind == "pad" || o.kind == "weed" || o.kind == "snag") && obs.Inside(o, p, 0.8f)) return false;
                return true;
            }
            if (Clear(b)) return new Vector3(b.x, 0f, b.y);
            for (float r = 0.5f; r <= 5f; r += 0.5f)
                for (int a = 0; a < 12; a++)
                {
                    float ar = a * 30f * Mathf.Deg2Rad;
                    var p = b + new Vector2(Mathf.Cos(ar), Mathf.Sin(ar)) * r;
                    if (Clear(p)) return new Vector3(p.x, 0f, p.y);
                }
            return new Vector3(b.x, 0f, b.y);
        }

        FishingController ecoCtl;

        /// <summary>The lake (re)opened at the period's centre with the random state <paramref name="seed"/> (a fresh stock): <see cref="ecoCtl"/>, null if it failed.</summary>
        IEnumerator EcoOpen(Period period, int seed, string mode)
        {
            GameClock.Min = GameClock.Centre(period);
            Random.InitState(seed);
            var prev = FindAnyObjectByType<FishingController>();
            yield return GoStage("lake", 2f);
            ecoCtl = null;
            for (float w = 0f; w < 60f && ecoCtl == null; w += Time.unscaledDeltaTime)
            {
                var c = FindAnyObjectByType<FishingController>();
                if (c != null && c != prev && c.State == FishingController.S.Ready) ecoCtl = c;
                yield return null;
            }
            if (ecoCtl != null && ecoCtl.Stage.L.Terrain != (mode == "new")) ecoCtl = null;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(period);
            if (ecoCtl != null) yield return new WaitForSeconds(2f);
        }

        IEnumerator EconomySoakTest()
        {
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("[ECO] no FishingController (use -fkscene Fishing -fkstage lake)");
                Application.Quit();
                yield break;
            }
            float secs = Mathf.Max(5f, ArgF("-fkecosecs") ?? 30f);
            string mode = Arg("-fkecomode") == "legacy" ? "legacy" : "new";
            var periods = (Arg("-fkecoperiods") ?? "0,1,2,3").Split(',').Select(t => int.Parse(t.Trim())).Where(p => p >= 0 && p < 4).ToArray();
            var rigNames = (Arg("-fkecorigs") ?? "paste2,pasteB,worm1,cornB").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
            string rodArg = Arg("-fkecorod") ?? "rod_bamboo";
            string rodId = rodArg.StartsWith("rod_") ? rodArg : "rod_" + rodArg;
            if (GameDatabase.GetItem<RodDef>(rodId) == null) rodId = "rod_bamboo";
            if (!Game.I.Owns(rodId)) Game.Data.ownedItems.Add(rodId);
            SteerGear(rodId, GameDatabase.StarterReel, GameDatabase.StarterLine);
            var rigs = rigNames.Select(n => LakeEconomy.SoakRig(n, id => FishHabitat.RigOf(GameDatabase.GetItem<BaitDef>(id), 0f))).ToArray();
            FishSpawner.OnlySpecies = null;
            FishingController.NoBites = false;
            GameClock.Scale = 0f;
            int fpsWas = Application.targetFrameRate;
            float capWas = Time.captureDeltaTime;
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            var L0 = ctl.Stage.L;
            float anchorX = BathyGen.AnchorX(L0.id), cast = Game.I.Rod.castDist;
            var fan = new List<(float yaw, float dist)>();
            for (float yaw = -36f; yaw <= 36.01f; yaw += 8f)
                for (float rho = L0.zNear + 2.5f; rho <= cast + 1e-3f; rho += 2f)
                    fan.Add((yaw, rho));
            bool broken = false;
            int shallow = 0;
            float real0 = Time.realtimeSinceStartup;
            Log(string.Format(CIc, "[ECO] soak: mode {0}, {1:0} game s per spot, {2} spots, rigs {3}, periods {4}; rod {5} (cast {6:0}), save {7}",
                mode, secs, fan.Count, string.Join(",", rigNames), string.Join(",", periods), Game.I.Rod.id, cast, Arg("-fksave")));
            // (the stock is drawn afresh every -fkecorestock spots (default 10) and at every rig: a bite's fish is let go and
            // swims off, its place taken by a new draw, and a species that never takes the bait (a bass on paste) is never
            // taken out, so over a long soak it fills the stock (the 8-fish lake's paste on the bottom died that way); the
            // estimator models the stock as drawn, so the soak keeps it close to that)
            int restock = Mathf.Max(1, Mathf.RoundToInt(ArgF("-fkecorestock") ?? 10f));
            int opens = 0;
            foreach (int pi in periods)
            {
                if (broken) break;
                var period = (Period)pi;
                yield return EcoOpen(period, 1515, mode);
                ctl = ecoCtl;
                if (ctl == null)
                {
                    broken = true;
                    break;
                }
                // (the new lake: its economy estimate on the workers first, so the feeding chance is this bed's from the start and
                // the prediction has its job; a bed whose F came from the cache gets its job made here)
                FishHabitat.EcoJob job = null;
                if (ctl.Habitat != null)
                {
                    float cd = Game.I.Rod.castDist;
                    for (float w = 0f; w < 240f && ctl.Habitat.LastJob == null && !FishHabitat.CachedFeed(ctl.Habitat.B.Hash, cd, out _); w += Time.unscaledDeltaTime) yield return null;
                    job = ctl.Habitat.LastJob;
                    if (job == null)
                    {
                        job = ctl.Habitat.Economy(cd);
                        var jj = job;
                        var task = System.Threading.Tasks.Task.Run(() => jj.Run(Mathf.Max(1, SystemInfo.processorCount / 2)));
                        while (!task.IsCompleted) yield return null;
                    }
                }
                FishAgent.ShallowFrames = FishAgent.UnderBedFrames = 0;
                int perShallow = 0;
                Log(string.Format(CIc, "[ECO] stage {0} period {1}: {2} fish, population {3}, feeding chance {4}; a fresh stock every {5} spots", mode, GameClock.Id(period), ctl.Spawner.Fish.Count,
                    ctl.Stage.Def.population, ctl.Habitat != null ? ctl.Habitat.FeedP.ToString("0.000", CIc) + (job != null ? string.Format(CIc, " (the estimate's {0:0.000})", job.F) : " (default)") : "1 (no bed)", restock));
                var spots = new List<Vector3>();
                foreach (var (yaw, dist) in fan) spots.Add(SoakSpot(ctl, anchorX, yaw, dist));
                for (int ri = 0; ri < rigs.Length; ri++)
                {
                    var rig = rigs[ri];
                    int cellBites = 0;
                    float cellSoak = 0f;
                    long cellIncome = 0;
                    var bySpecies = new Dictionary<string, int>();
                    // (the diagnostics, summed over the stocks: the approach rolls, approaches and feeding decisions; while no fish
                    // is engaged, the fish that like the bait within the sense range, then also at the depth, then also with water
                    // enough at the hook, time-averaged; the time the rig moves through the water at 0.4 and 1.3 m/s or more)
                    int rolls = 0, appr = 0, fRolls = 0, fYes = 0;
                    int rolls0 = 0, appr0 = 0, fr0 = 0, fy0 = 0;
                    float free = 0f, inPlan = 0f, inDepth = 0f, inAll = 0f, slow = 0f, fast = 0f, engaged = 0f;
                    for (int si = 0; si < spots.Count; si++)
                    {
                        if (si % restock == 0)
                        {
                            if (ri > 0 || si > 0)
                            {
                                rolls += ctl.ApproachRolls - rolls0;
                                appr += ctl.Approaches - appr0;
                                fRolls += ctl.FeedRolls - fr0;
                                fYes += ctl.FeedYes - fy0;
                                perShallow += FishAgent.ShallowFrames;
                                FishAgent.ShallowFrames = 0;
                                yield return EcoOpen(period, 1515 + 1000 * ++opens, mode);
                                ctl = ecoCtl;
                                if (ctl == null)
                                {
                                    broken = true;
                                    break;
                                }
                            }
                            EquipTest(rig.bait, ctl);
                            rolls0 = ctl.ApproachRolls;
                            appr0 = ctl.Approaches;
                            fr0 = ctl.FeedRolls;
                            fy0 = ctl.FeedYes;
                        }
                        var tk = ctl.Tackle;
                        var spot = spots[si];
                        yield return ToReady(ctl);
                        tk.FloatDepth = LakeEconomy.SoakFloatDepth(rig.cls);
                        ctl.DebugPlaceRig(spot);
                        float soak = 0f, start = Time.time;
                        int relays = 0, bites = 0;
                        long income = 0;
                        // (game time from the spot's start, the frames spent winding in and laying it again included)
                        while (soak < secs && Time.time - start < secs * 3f && relays <= 8)
                        {
                            if (ctl == null)
                            {
                                broken = true;
                                break;
                            }
                            float dt = Time.deltaTime;
                            if (ctl.State == FishingController.S.Biting)
                            {
                                var f = ctl.DebugBiter;
                                if (f != null)
                                {
                                    bites++;
                                    income += f.Sp.Price(f.Cm);
                                    bySpecies[f.Sp.id] = (bySpecies.TryGetValue(f.Sp.id, out int n) ? n : 0) + 1;
                                }
                                ctl.DebugLetBiteGo();
                            }
                            else if (ctl.State == FishingController.S.Waiting && tk.State == Tackle.Mode.Water)
                            {
                                if (new Vector2(tk.Surface.x - spot.x, tk.Surface.z - spot.z).magnitude > 3f)
                                {
                                    yield return ToReady(ctl);
                                    ctl.DebugPlaceRig(spot);
                                    continue;
                                }
                                soak += dt;
                                if (tk.RelSpeed >= 0.4f) slow += dt;
                                if (tk.RelSpeed >= 1.3f) fast += dt;
                                if (ctl.Spawner.Fish.Any(f => f.Engaged)) engaged += dt;
                                else
                                {
                                    free += dt;
                                    var hook = tk.HookPos;
                                    float sense = ctl.SenseRange(), hookWater = ctl.Stage.L.DepthAt(hook.x, hook.z);
                                    foreach (var f in ctl.Spawner.Fish)
                                    {
                                        if (f.State != FishAgent.St.Wander || f.Sp.encounter != null || f.Sp.Appeal(tk.Bait) <= 0f) continue;
                                        if (new Vector2(hook.x - f.Pos.x, hook.z - f.Pos.z).magnitude > sense) continue;
                                        inPlan += dt;
                                        if (Mathf.Abs(f.Depth - tk.Depth) > 2.5f) continue;
                                        inDepth += dt;
                                        if (ctl.Habitat != null && hookWater < FishHabitat.MinWater(f.Cm)) continue;
                                        inAll += dt;
                                    }
                                }
                            }
                            else if (ctl.State != FishingController.S.Waiting)
                            {
                                relays++;
                                yield return ToReady(ctl);
                                ctl.DebugPlaceRig(spot);
                                continue;
                            }
                            yield return null;
                        }
                        cellBites += bites;
                        cellSoak += soak;
                        cellIncome += income;
                        Log(string.Format(CIc, "[ECO] spot {0} {1} {2} {3} {4:0.00} {5:0.00} bites {6} soak {7:0.0} income {8}", mode, pi, rig.id, si, spot.x, spot.z, bites, soak, income));
                        if (broken) break;
                    }
                    if (broken) break;
                    rolls += ctl.ApproachRolls - rolls0;
                    appr += ctl.Approaches - appr0;
                    fRolls += ctl.FeedRolls - fr0;
                    fYes += ctl.FeedYes - fy0;
                    Log(string.Format(CIc, "[ECO] cell {0} {1} {2} bites {3} soak {4:0.0} income {5} perMin {6:0.000} incomePerMin {7:0.0} species {8}", mode, pi, rig.id, cellBites, cellSoak, cellIncome,
                        cellSoak > 0f ? cellBites * 60f / cellSoak : 0f, cellSoak > 0f ? cellIncome * 60f / cellSoak : 0f, string.Join(",", bySpecies.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + kv.Value))));
                    Log(string.Format(CIc, "[ECO] diag {0} {1} {2} rolls {3} approaches {4} feedRolls {5} feeding {6} free {7:0.0} inPlan {8:0.000} inDepth {9:0.000} inAll {10:0.000} engaged {11:0.0} moving {12:0.0} fast {13:0.0}",
                        mode, pi, rig.id, rolls, appr, fRolls, fYes, free,
                        free > 0f ? inPlan / free : 0f, free > 0f ? inDepth / free : 0f, free > 0f ? inAll / free : 0f, engaged, slow, fast));
                }
                if (ctl != null && ctl.Habitat != null)
                {
                    perShallow += FishAgent.ShallowFrames;
                    shallow += perShallow;
                    Log($"[ECO] cell shallow {pi} frames {perShallow}");
                    // the estimator's prediction for these spots and rigs in this period (both lakes; the bed at its feeding chance)
                    if (job != null)
                    {
                        var pw = new float[4];
                        pw[pi] = 1f;
                        var at = spots.Select(s => new Vector2(s.x, s.z)).ToArray();
                        var (today, bed) = job.Predict(at, rigs, pw, job.F);
                        for (int r = 0; r < rigs.Length; r++)
                            Log(string.Format(CIc, "[ECO] predict {0} {1} today {2:0.000000} {3:0.0000} bed {4:0.000000} {5:0.0000} F {6:0.000} window {7:0.0000} {8:0.0000}",
                                pi, rigs[r].id, today.rigC[r], today.rigI[r], bed.rigC[r], bed.rigI[r], job.F, today.rigWin[r], bed.rigWin[r]));
                    }
                    else Log("[ECO] predict: no estimate (the workers did not finish)");
                }
            }
            if (broken) EcoCheck("the lake reloaded for every period", false);
            else if (mode == "new") EcoCheck($"the bed: {shallow} frames of a fish (wandering, coming, nibbling, biting, fleeing) in water shallower than it swims in", shallow == 0);
            Log(string.Format(CIc, "[ECO] {0:0} real s", Time.realtimeSinceStartup - real0));
            Application.targetFrameRate = fpsWas;
            Time.captureDeltaTime = capWas;
            Log($"[ECO] economy test done: {ecoFails} failed");
            Application.Quit();
        }
    }
}
