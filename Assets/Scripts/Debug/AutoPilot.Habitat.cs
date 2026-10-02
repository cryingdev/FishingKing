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
    /// 옥수수 4 m, 지렁이 1 m) each soaked -fkhabsecs game seconds (default 75) at 12 spots (yaw -30 / -10 / 10 / 30 x 8 /
    /// 12 / 15 m) on a fixed 1/60 s step; every bite is counted and let go. Logged per rig, period and mode: bites per
    /// minute, approaches, rolls and the fish within reach (SenseFor, the depth gate) while none comes. CHECK pooled over
    /// the periods per rig: bites on / off in [0.8, 1.25] and fish in reach on / off in [0.85, 1.18]; every cell in [0.6,
    /// 1.6]. [HAB] lines.
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
        /// A soak spot at (yaw, distance) from him, moved to the nearest open water clear of pads, standing props and weed /
        /// snag zones (a float laid on a pad snags and is cut over and over): from the obstacles alone, so the bed on and
        /// off soak the same spots.
        /// </summary>
        static Vector3 SoakSpot(FishingController ctl, float yaw, float dist)
        {
            var obs = ctl.Stage.Obstacles;
            var water = ctl.Stage.Water;
            float yr = yaw * Mathf.Deg2Rad;
            var b = new Vector2(ctl.Angler.X + Mathf.Sin(yr) * dist, Mathf.Cos(yr) * dist);
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
            float secs = Mathf.Max(10f, ArgF("-fkhabsecs") ?? 75f);
            SteerGear("rod_bamboo", GameDatabase.StarterReel, GameDatabase.StarterLine);
            FishSpawner.OnlySpecies = null;
            FishingController.NoBites = false;
            GameClock.Scale = 0f;
            int fpsWas = Application.targetFrameRate;
            float capWas = Time.captureDeltaTime;
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            var rigs = new[] { ("bait_paste", 2f, "떡밥 2 m"), ("bait_corn", 4f, "옥수수 4 m"), ("bait_worm", 1f, "지렁이 1 m") };
            var cells = new Dictionary<string, HabCell>();
            bool broken = false;
            float real0 = Time.realtimeSinceStartup;
            Log(string.Format(CIc, "[HAB] soak: {0:0} game s per spot, 12 spots, 3 rigs, 4 periods, terrain on / off; rod {1}", secs, Game.I.Rod.id));
            foreach (bool on in new[] { true, false })
            {
                Bathymetry.Off = !on;
                for (int pi = 0; pi < 4 && !broken; pi++)
                {
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
                    foreach (var (baitId, depth, label) in rigs)
                    {
                        EquipTest(baitId, ctl);
                        var cell = new HabCell();
                        int a0 = ctl.Approaches, r0 = ctl.ApproachRolls;
                        foreach (float yaw in new[] { -30f, -10f, 10f, 30f })
                        foreach (float dist in new[] { 8f, 12f, 15f })
                        {
                            yield return ToReady(ctl);
                            tk.FloatDepth = depth;
                            var spot = SoakSpot(ctl, yaw, dist);
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
                        if (broken) break;
                    }
                }
            }
            Bathymetry.Off = false;
            if (broken) HabCheck("the lake reloaded for every run", false);
            else
            {
                var est = new List<(float est, float live)>();
                foreach (var (baitId, depth, label) in rigs)
                {
                    HabCell Pool(bool on)
                    {
                        var p = new HabCell();
                        for (int pi = 0; pi < 4; pi++)
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
                    float bite = a.PerMin / Mathf.Max(1e-3f, o.PerMin), reach = a.Reach / Mathf.Max(1e-3f, o.Reach);
                    HabCheck(string.Format(CIc, "{0}, pooled over the 4 periods: bites {1:0.00} / {2:0.00} per min = x{3:0.00} (in [0.80, 1.25]; {4} / {5} bites), fish in reach {6:0.00} / {7:0.00} = x{8:0.00} (in [0.85, 1.18])",
                        label, a.PerMin, o.PerMin, bite, a.bites, o.bites, a.Reach, o.Reach, reach), bite >= 0.8f && bite <= 1.25f && reach >= 0.85f && reach <= 1.18f);
                    var cellRatios = new List<string>();
                    bool sane = true;
                    for (int pi = 0; pi < 4; pi++)
                    {
                        var con = cells[baitId + "/" + pi + "/on"];
                        var cof = cells[baitId + "/" + pi + "/off"];
                        float r = con.PerMin / Mathf.Max(1e-3f, cof.PerMin);
                        cellRatios.Add($"{GameClock.Id((Period)pi)} x{F2(r)}");
                        if (cof.bites >= 10 && (r < 0.6f || r > 1.6f)) sane = false;
                        est.Add((con.Reach / Mathf.Max(1e-3f, cof.Reach), r));
                    }
                    HabCheck($"{label}: every period's ratio in the sanity band [0.6, 1.6] ({string.Join(", ", cellRatios)})", sane);
                }
                // (the in-reach ratio as the estimate of the live bite ratio: how well they agree)
                float mx = est.Average(e => e.est), my = est.Average(e => e.live);
                float sxy = est.Sum(e => (e.est - mx) * (e.live - my)), sxx = est.Sum(e => (e.est - mx) * (e.est - mx)), syy = est.Sum(e => (e.live - my) * (e.live - my));
                float corr = sxx > 0f && syy > 0f ? sxy / Mathf.Sqrt(sxx * syy) : 0f;
                Log(string.Format(CIc, "[HAB] in-reach ratio vs bite ratio over the 12 cells: correlation {0:0.00} (mean {1:0.00} / {2:0.00}); {3:0} real s", corr, mx, my, Time.realtimeSinceStartup - real0));
            }
            Application.targetFrameRate = fpsWas;
            Time.captureDeltaTime = capWas;
            Log($"[HAB] habitat test done: {habFails} failed");
            Application.Quit();
        }
    }
}
