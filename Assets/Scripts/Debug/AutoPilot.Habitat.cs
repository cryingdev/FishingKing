using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto habitat (Docs/terrain_depth_spec.md 14.2): the lake's bites per minute with its generated bed against the
    /// same lake without it (Bathymetry.Off, the stage reloaded), on the tide-bites soak loop: the bamboo rod with the
    /// starter reel and line, the stage's stock, the clock frozen at the centre of each period, three float rigs (떡밥 2 m,
    /// 옥수수 4 m, 지렁이 1 m), each soaked -fkhabsecs game seconds (default 20) at 70 spots spread over the reference
    /// player's fan (the estimator's: yaw -36..36 every 8 degrees x from zNear + 2.5 every 2 m out to the cast, from the
    /// walk's middle) on a fixed 1/60 s step; every bite is counted and let go. A cell is a rig, a period and the bed on
    /// or off; -fkhabmode on|off, -fkhabperiods 0,2 and -fkhabrigs paste,worm run part of them (for parallel runs: the
    /// [HAB] cell lines are pooled by hand). With both modes of a cell: CHECK (hard) every rig and period, and every rig
    /// pooled over the periods, bites on / off in [0.8, 1.25] - the point estimate once both sides have 100 bites, else
    /// its 95 % interval must reach the band; the fish in reach (SenseFor, the depth gate) pooled per rig in [0.8, 1.25];
    /// with the bed on, no fish (wandering, coming, nibbling, biting, fleeing) in water shallower than it swims in. [HAB] lines.
    /// </summary>
    public partial class AutoPilot
    {
        int habFails;

        void HabCheck(string what, bool ok)
        {
            if (!ok) habFails++;
            Log($"[HAB] CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        struct HabCell
        {
            public int bites, approaches, rolls;
            public float soak, inReach, free;
            public float PerMin => soak > 0f ? bites * 60f / soak : 0f;
            public float Reach => free > 0f ? inReach / free : 0f;
        }

        /// <summary>
        /// The band rule on a ratio of two bite counts (spec 14.2): with 100 bites on each side the point estimate within
        /// [0.8, 1.25]; with fewer, its 95 % interval (log ratio +- 1.96 sqrt(1/a + 1/b), half a bite added to each) must
        /// reach the band.
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
        /// weed / snag zones (a float laid on a pad snags and is cut over and over): from the obstacles alone, so the bed on
        /// and off soak the same spots.
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

        IEnumerator HabitatSoakTest()
        {
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("[HAB] no FishingController (use -fkscene Fishing -fkstage lake)");
                Application.Quit();
                yield break;
            }
            float secs = Mathf.Max(5f, ArgF("-fkhabsecs") ?? 20f);
            string modeArg = Arg("-fkhabmode") ?? "both";
            var modes = modeArg == "on" ? new[] { true } : modeArg == "off" ? new[] { false } : new[] { true, false };
            var periods = (Arg("-fkhabperiods") ?? "0,1,2,3").Split(',').Select(t => int.Parse(t.Trim())).Where(p => p >= 0 && p < 4).ToArray();
            var rigFilter = Arg("-fkhabrigs");
            SteerGear("rod_bamboo", GameDatabase.StarterReel, GameDatabase.StarterLine);
            FishSpawner.OnlySpecies = null;
            FishingController.NoBites = false;
            GameClock.Scale = 0f;
            int fpsWas = Application.targetFrameRate;
            float capWas = Time.captureDeltaTime;
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            var rigs = new[] { ("bait_paste", 2f, "떡밥 2 m"), ("bait_corn", 4f, "옥수수 4 m"), ("bait_worm", 1f, "지렁이 1 m") }
                .Where(rg => rigFilter == null || rigFilter.Split(',').Any(f => rg.Item1 == "bait_" + f.Trim())).ToArray();
            // the reference player's fan (the estimator's casts, every other yaw and distance)
            var L0 = ctl.Stage.L;
            float anchorX = BathyGen.AnchorX(L0.id), cast = Game.I.Rod.castDist;
            var fan = new List<(float yaw, float dist)>();
            for (float yaw = -36f; yaw <= 36.01f; yaw += 8f)
                for (float rho = L0.zNear + 2.5f; rho <= cast + 1e-3f; rho += 2f)
                    fan.Add((yaw, rho));
            var cells = new Dictionary<string, HabCell>();
            bool broken = false;
            int shallowOn = 0;
            float real0 = Time.realtimeSinceStartup;
            Log(string.Format(CIc, "[HAB] soak: {0:0} game s per spot, {1} spots, rigs {2}, periods {3}, terrain {4}; rod {5}",
                secs, fan.Count, string.Join(" / ", rigs.Select(rg => rg.Item3)), string.Join(",", periods), modeArg, Game.I.Rod.id));
            foreach (bool on in modes)
            {
                Bathymetry.Off = !on;
                foreach (int pi in periods)
                {
                    if (broken) break;
                    var period = (Period)pi;
                    GameClock.Min = GameClock.Centre(period);
                    Random.InitState(1515);
                    var prev = ctl;
                    yield return GoStage("lake", 2f);
                    ctl = null;
                    for (float w = 0f; w < 60f && ctl == null; w += Time.unscaledDeltaTime)
                    {
                        var c = FindAnyObjectByType<FishingController>();
                        if (c != null && c != prev && c.State == FishingController.S.Ready) ctl = c;
                        yield return null;
                    }
                    if (ctl == null || ctl.Stage.L.Terrain != on)
                    {
                        broken = true;
                        break;
                    }
                    GameClock.Scale = 0f;
                    GameClock.Min = GameClock.Centre(period);
                    yield return new WaitForSeconds(2f);
                    var tk = ctl.Tackle;
                    FishAgent.ShallowFrames = FishAgent.UnderBedFrames = 0;
                    foreach (var (baitId, depth, label) in rigs)
                    {
                        EquipTest(baitId, ctl);
                        var cell = new HabCell();
                        int a0 = ctl.Approaches, r0 = ctl.ApproachRolls;
                        foreach (var (yaw, dist) in fan)
                        {
                            yield return ToReady(ctl);
                            tk.FloatDepth = depth;
                            var spot = SoakSpot(ctl, anchorX, yaw, dist);
                            ctl.DebugPlaceRig(spot);
                            float soak = 0f, start = Time.time;
                            int relays = 0;
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
                                    cell.bites++;
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
                                    if (!ctl.Spawner.Fish.Any(f => f.Engaged))
                                    {
                                        cell.free += dt;
                                        var hook = tk.HookPos;
                                        float hookWater = ctl.Stage.L.DepthAt(hook.x, hook.z);
                                        int n = 0;
                                        foreach (var f in ctl.Spawner.Fish)
                                        {
                                            if (f.State != FishAgent.St.Wander || f.Sp.encounter != null || f.Sp.Appeal(tk.Bait) <= 0f) continue;
                                            if (new Vector2(hook.x - f.Pos.x, hook.z - f.Pos.z).magnitude > ctl.SenseFor(f) || Mathf.Abs(f.Depth - tk.Depth) > 2.5f) continue;
                                            if (ctl.Habitat != null && hookWater < FishHabitat.MinWater(f.Cm)) continue;
                                            n++;
                                        }
                                        cell.inReach += n * dt;
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
                            cell.soak += soak;
                            if (broken) break;
                        }
                        cell.approaches = ctl.Approaches - a0;
                        cell.rolls = ctl.ApproachRolls - r0;
                        string key = baitId + "/" + pi + "/" + (on ? "on" : "off");
                        cells[key] = cell;
                        Log(string.Format(CIc, "[HAB] soak {0} {1} terrain {2}: {3} bites in {4:0} s = {5:0.00}/min; approaches {6} of {7} rolls; fish in reach {8:0.00} (while none comes, {9:0} s)",
                            label, GameClock.Id(period), on ? "on" : "off", cell.bites, cell.soak, cell.PerMin, cell.approaches, cell.rolls, cell.Reach, cell.free));
                        Log(string.Format(CIc, "[HAB] cell {0} {1} {2} bites {3} soak {4:0.0} inreach {5:0.000} free {6:0.0} approaches {7} rolls {8}",
                            baitId, pi, on ? "on" : "off", cell.bites, cell.soak, cell.inReach, cell.free, cell.approaches, cell.rolls));
                        if (broken) break;
                    }
                    if (on)
                    {
                        shallowOn += FishAgent.ShallowFrames;
                        Log($"[HAB] cell shallow {pi} frames {FishAgent.ShallowFrames}");
                    }
                }
            }
            Bathymetry.Off = false;
            if (broken) HabCheck("the lake reloaded for every run", false);
            else
            {
                if (modes.Contains(true))
                    HabCheck($"the bed on: {shallowOn} frames of a fish (wandering, coming, nibbling, biting, fleeing) in water shallower than it swims in", shallowOn == 0);
                var est = new List<(float est, float live)>();
                foreach (var (baitId, depth, label) in rigs)
                {
                    var both = periods.Where(pi => cells.ContainsKey(baitId + "/" + pi + "/on") && cells.ContainsKey(baitId + "/" + pi + "/off")).ToArray();
                    if (both.Length == 0) continue;
                    HabCell Pool(bool on)
                    {
                        var p = new HabCell();
                        foreach (int pi in both)
                        {
                            var c = cells[baitId + "/" + pi + "/" + (on ? "on" : "off")];
                            p.bites += c.bites;
                            p.soak += c.soak;
                            p.inReach += c.inReach;
                            p.free += c.free;
                            p.approaches += c.approaches;
                            p.rolls += c.rolls;
                        }
                        return p;
                    }
                    var a = Pool(true);
                    var o = Pool(false);
                    bool okB = BiteBand(a.bites, a.soak, o.bites, o.soak, out float bite, out string how);
                    float reach = a.Reach / Mathf.Max(1e-3f, o.Reach);
                    HabCheck(string.Format(CIc, "{0}, pooled over {1} period(s): bites {2:0.00} / {3:0.00} per min = x{4:0.00} ({5} / {6} bites; {7}; in [0.80, 1.25]), fish in reach {8:0.00} / {9:0.00} = x{10:0.00} (in [0.80, 1.25])",
                        label, both.Length, a.PerMin, o.PerMin, bite, a.bites, o.bites, how, a.Reach, o.Reach, reach), okB && reach >= 0.8f && reach <= 1.25f);
                    foreach (int pi in both)
                    {
                        var con = cells[baitId + "/" + pi + "/on"];
                        var cof = cells[baitId + "/" + pi + "/off"];
                        bool ok = BiteBand(con.bites, con.soak, cof.bites, cof.soak, out float r, out string h);
                        HabCheck(string.Format(CIc, "{0} {1}: bites {2} / {3} = x{4:0.00} ({5}; in [0.80, 1.25]); fish in reach x{6:0.00}",
                            label, GameClock.Id((Period)pi), con.bites, cof.bites, r, h, con.Reach / Mathf.Max(1e-3f, cof.Reach)), ok);
                        est.Add((con.Reach / Mathf.Max(1e-3f, cof.Reach), r));
                    }
                }
                // (the in-reach ratio as the estimate of the live bite ratio: how well they agree)
                if (est.Count > 1)
                {
                    float mx = est.Average(e => e.est), my = est.Average(e => e.live);
                    float sxy = est.Sum(e => (e.est - mx) * (e.live - my)), sxx = est.Sum(e => (e.est - mx) * (e.est - mx)), syy = est.Sum(e => (e.live - my) * (e.live - my));
                    float corr = sxx > 0f && syy > 0f ? sxy / Mathf.Sqrt(sxx * syy) : 0f;
                    Log(string.Format(CIc, "[HAB] in-reach ratio vs bite ratio over the {0} cells: correlation {1:0.00} (mean {2:0.00} / {3:0.00})", est.Count, corr, mx, my));
                }
            }
            Log(string.Format(CIc, "[HAB] {0:0} real s", Time.realtimeSinceStartup - real0));
            Application.targetFrameRate = fpsWas;
            Time.captureDeltaTime = capWas;
            Log($"[HAB] habitat test done: {habFails} failed");
            Application.Quit();
        }
    }
}
