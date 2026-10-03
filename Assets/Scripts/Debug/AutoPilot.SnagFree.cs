using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto snagfree (with -fkscene Fishing -fkstage sea; -fksfsecs &lt;s&gt; real-time limit (default 180),
    /// -fksfcatches &lt;n&gt; prop catches per way (default 40), -fksfseed &lt;n&gt; (default: the clock, logged)): the snag-release
    /// technique (Docs/obstacles_spec.md 6.10). Like the stall hunt's random tries, the float is laid on or by the sea's
    /// tetrapods (half round the props standing in the water, half along the tetrapod snag zones, a random tide) and wound
    /// in (회수); each rig caught on a prop is worked one of three ways in turn, and anything still caught is cut (끊기):
    /// <list type="bullet">
    /// <item>sweep: the old way, the rod held to the strip's free side (A / D) for up to 2.5 s;</item>
    /// <item>slack: line given (B) until there is 0.6 m of slack, the hook point left to back out, the slack wound back up
    /// (Space) until the line is taut, then the rod moved the way the snag arrow shows (W / S,
    /// A / D) for up to 4 s;</item>
    /// <item>tight: winding (Space) for 0.8 s, then the free-side sweep added while still winding, 2.5 s in all.</item>
    /// </list>
    /// Then three catches at the dead zone (-7.07, 10.70): left alone 3 s they must hold, then the slack way is tried for
    /// the record. Keys only (the simulated pointer is never pressed). The first slack catch is shot: caught with the guide, slack and
    /// the arrow, freed. [SNAGFREE] lines: per way catches / freed (by how) / cut / broke, a digest of the outcomes (the
    /// same seed gives the same digest: the release is deterministic); CHECKs: the sweep frees at least
    /// 60 %, slack frees at least 85 % and no less than the sweep, tight frees less than the sweep and breaks more, the dead
    /// zone holds when left alone.
    /// </summary>
    public partial class AutoPilot
    {
        class SfWay
        {
            public string name;
            public int catches, freed, cut, broke;
            public float freeT;
            public readonly Dictionary<string, int> how = new Dictionary<string, int>();
            public float Share(int n) => catches > 0 ? (float)n / catches : 0f;
        }

        static void SfKeys(bool l = false, bool r = false, bool up = false, bool down = false, bool wind = false, bool give = false)
        {
            PointerInput.SimLeft = l;
            PointerInput.SimRight = r;
            PointerInput.SimLift = up;
            PointerInput.SimDrop = down;
            PointerInput.SimWind = wind;
            PointerInput.SimGive = give;
        }

        IEnumerator SnagFreeTest()
        {
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            SfKeys();
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("[SNAGFREE] CHECK FAIL no FishingController (use -fkscene Fishing -fkstage sea)");
                Log("snagfree test done: 1 failed");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.4f);
            }
            var obs = ctl.Stage.Obstacles;
            var tets = obs == null ? new List<Obstacle>() : obs.Snags.Where(o => o.Has("tet")).ToList();
            var props = obs == null ? new List<Obstacle>() : obs.Solids.Where(o => o.Standing && !o.Near).ToList();
            if (props.Count == 0)
            {
                Log($"[SNAGFREE] CHECK FAIL {ctl.Stage.Def.id} has no props standing in the water");
                Log("snagfree test done: 1 failed");
                Application.Quit();
                yield break;
            }
            float limit = ArgF("-fksfsecs") ?? 180f;
            int target = Mathf.Max(3, Mathf.RoundToInt(ArgF("-fksfcatches") ?? 40f));
            int seed = Mathf.RoundToInt(ArgF("-fksfseed") ?? System.Environment.TickCount % 100000);
            var rnd = new System.Random(seed);
            Obstacles.Rnd = new System.Random(seed + 1);
            SteerGear("rod_carbon", "reel_highgear", "line_nylon4");
            EquipTest("bait_shrimp", ctl);
            // -fksfhook <id>: the rig's hook (a pack bought and put on; the weedless one's guard snags less and bites less)
            string hookArg = Arg("-fksfhook");
            var sfHook = hookArg != null ? GameDatabase.GetItem<HookDef>(hookArg) : null;
            if (sfHook != null)
            {
                if (!sfHook.infinite) Game.I.AddHook(sfHook.id, 999);
                Game.I.Equip(sfHook);
            }
            FishingController.NoBites = true;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            Log($"[SNAGFREE] {ctl.Stage.Def.id}: {props.Count} props, {tets.Count} tetrapod zones, seed {seed}, {target} catches a way, limit {limit:0} s, hook {Game.I.Hook.id}");
            var ways = new[] { new SfWay { name = "sweep" }, new SfWay { name = "slack" }, new SfWay { name = "tight" } };
            float[] phases = { 0.5f, 0.25f, 0.75f };
            float start = Time.realtimeSinceStartup;
            int tries = 0, caughtN = 0, other = 0;
            var digest = new System.Text.StringBuilder();
            bool shot = false;
            while (Time.realtimeSinceStartup - start < limit && ways.Any(w => w.catches < target))
            {
                tries++;
                Vector2 p;
                if (tets.Count == 0 || rnd.Next(2) == 0)
                {
                    var z = props[rnd.Next(props.Count)];
                    float ang = (float)(rnd.NextDouble() * Mathf.PI * 2f), r = z.R * 0.7f + 0.2f + (float)rnd.NextDouble() * 1.8f;
                    p = z.C + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                }
                else
                {
                    var z = tets[rnd.Next(tets.Count)];
                    var a = new Vector2(z.x0, z.z0);
                    var b = new Vector2(z.x1, z.z1);
                    var on = Vector2.Lerp(a, b, (float)rnd.NextDouble());
                    var dir = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector2.right;
                    p = on + new Vector2(-dir.y, dir.x) * (((float)rnd.NextDouble() * 2f - 1f) * (z.rad + 1.5f));
                }
                GameClock.TidePhase = phases[rnd.Next(phases.Length)];
                SfKeys();
                yield return ToReady(ctl);
                if (Game.I.Bait == null || Game.I.Bait.id != "bait_shrimp") EquipTest("bait_shrimp", ctl);
                var tk = ctl.Tackle;
                if (!ctl.DebugPlaceRig(new Vector3(p.x, 0f, p.y))) continue;
                yield return null;
                // wound in until it is caught on a prop or home (another snag: cut, not counted)
                SnagInfo sn = null;
                for (float g = 0f; g < 30f && ctl.State != FishingController.S.Ready; g += Time.deltaTime)
                {
                    if (ctl.State == FishingController.S.Snagged)
                    {
                        if (tk.Snag != null && tk.Snag.kind == "prop")
                        {
                            sn = tk.Snag;
                            break;
                        }
                        other++;
                        ctl.Retrieve();   // (끊기)
                    }
                    else if (ctl.State == FishingController.S.Waiting) ctl.Retrieve();
                    yield return null;
                }
                if (sn == null) continue;
                var w = ways[caughtN % ways.Length];
                caughtN++;
                if (w.catches >= target)
                {
                    // (this way has its count: the next one that still needs catches)
                    w = ways.First(x => x.catches < target);
                }
                w.catches++;
                int frees0 = ctl.SnagFrees, breaks0 = ctl.SnagBreaks;
                bool shoot = !shot && w.name == "slack";
                float t = 0f;
                if (shoot)
                {
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot("snag_guide");
                    Log($"[SNAGFREE] shot: caught on {sn.zone?.id}, strip '{PhaseText()}', arrow {ctl.SnagArrowDir}");
                }
                if (w.name == "sweep")
                {
                    SfKeys(l: sn.freeSide <= 0, r: sn.freeSide > 0);
                    while (t < 2.5f && ctl.State == FishingController.S.Snagged)
                    {
                        t += Time.deltaTime;
                        yield return null;
                    }
                }
                else if (w.name == "tight")
                {
                    // cranking first (the line comes tight on the snag), then the sweep added, still winding
                    SfKeys(wind: true);
                    while (t < 2.5f && ctl.State == FishingController.S.Snagged)
                    {
                        if (t >= 0.8f) SfKeys(l: sn.freeSide <= 0, r: sn.freeSide > 0, wind: true);
                        t += Time.deltaTime;
                        yield return null;
                    }
                }
                else
                {
                    // give line to 0.6 m of slack, wait for the hook point to back out, take the slack back up, then
                    // follow the arrow (the physics' guide)
                    SfKeys(give: true);
                    while (t < 1.5f && ctl.State == FishingController.S.Snagged && ctl.SnagSlack < 0.6f)
                    {
                        t += Time.deltaTime;
                        yield return null;
                    }
                    SfKeys();
                    while (t < 3f && ctl.State == FishingController.S.Snagged && !ctl.SnagLoose)
                    {
                        t += Time.deltaTime;
                        yield return null;
                    }
                    if (shoot && ctl.State == FishingController.S.Snagged)
                    {
                        yield return new WaitForSeconds(0.25f);
                        yield return Shot("snag_slack");
                        Log(string.Format(CIc, "[SNAGFREE] shot: slack {0:0.00} m, loose {1}, strip '{2}'", ctl.SnagSlack, ctl.SnagLoose, PhaseText()));
                    }
                    SfKeys(wind: true);
                    while (t < 5f && ctl.State == FishingController.S.Snagged && ctl.SnagSlack >= FishingController.SlackMin)
                    {
                        t += Time.deltaTime;
                        yield return null;
                    }
                    SfKeys();
                    float t0 = t;
                    bool shotMove = false;
                    while (t - t0 < 4f && ctl.State == FishingController.S.Snagged)
                    {
                        var g = ctl.SnagArrowDir;
                        SfKeys(l: g.x < 0, r: g.x > 0, up: g.y > 0, down: g.y < 0);
                        if (shoot && !shotMove && ctl.PushArrow.Visible && ctl.PushArrow.State == SideArrow.Mode.Right)
                        {
                            shotMove = true;
                            yield return Shot("snag_move");
                            Log($"[SNAGFREE] shot: arrow pitch {ctl.PushArrow.Pitch:+0;-0;0} side {ctl.PushArrow.Side:+0;-0;0}, rod pitch {ctl.PitchNow:0.00}, strip '{PhaseText()}'");
                        }
                        t += Time.deltaTime;
                        yield return null;
                    }
                }
                SfKeys();
                string outcome;
                if (ctl.SnagFrees > frees0)
                {
                    w.freed++;
                    w.freeT += t;
                    w.how.TryGetValue(ctl.LastFreeWay, out int n);
                    w.how[ctl.LastFreeWay] = n + 1;
                    outcome = $"freed ({ctl.LastFreeWay}) in {t:0.00} s";
                    if (shoot)
                    {
                        shot = true;
                        yield return Shot("snag_freed");
                    }
                }
                else if (ctl.SnagBreaks > breaks0)
                {
                    w.broke++;
                    outcome = $"broke in {t:0.00} s";
                }
                else
                {
                    w.cut++;
                    ctl.Retrieve();   // (끊기)
                    outcome = "cut";
                    if (shoot) shot = true;   // (the next slack catch is not shot again: one set)
                }
                Log(string.Format(CIc, "[SNAGFREE] try {0} catch {1} ({2}) on {3} at ({4:0.00}, {5:0.00}) free side {6:+0;-0;0} embed {7:0.00}: {8}",
                    tries, caughtN, w.name, sn.zone != null ? sn.zone.id : "?", sn.at.x, sn.at.z, sn.freeSide, sn.embed, outcome));
                digest.Append(w.name[1]).Append(outcome[0]);
            }
            // the dead zone (the user's catch-and-cut spot): wound in from there it is caught, and left alone it stays
            // caught; then the slack way is tried for the record (not a check)
            var dz = new Vector2(-7.07f, 10.70f);
            int dzCaught = 0, dzHeld = 0, dzWorked = 0;
            for (int i = 0; i < 3; i++)
            {
                SfKeys();
                yield return ToReady(ctl);
                if (!ctl.DebugPlaceRig(new Vector3(dz.x, 0f, dz.y))) continue;
                yield return null;
                SnagInfo sn = null;
                for (float g = 0f; g < 30f && ctl.State != FishingController.S.Ready; g += Time.deltaTime)
                {
                    if (ctl.State == FishingController.S.Snagged)
                    {
                        if (ctl.Tackle.Snag != null && ctl.Tackle.Snag.kind == "prop")
                        {
                            sn = ctl.Tackle.Snag;
                            break;
                        }
                        ctl.Retrieve();
                    }
                    else if (ctl.State == FishingController.S.Waiting) ctl.Retrieve();
                    yield return null;
                }
                if (sn == null)
                {
                    Log($"[SNAGFREE] dead zone try {i}: not caught on a prop");
                    continue;
                }
                dzCaught++;
                int frees0 = ctl.SnagFrees;
                for (float t = 0f; t < 3f && ctl.State == FishingController.S.Snagged; t += Time.deltaTime) yield return null;
                bool held = ctl.SnagFrees == frees0 && ctl.State == FishingController.S.Snagged;
                if (held) dzHeld++;
                string worked = "-";
                if (held)
                {
                    SfKeys(give: true);
                    for (float t = 0f; t < 1.5f && ctl.State == FishingController.S.Snagged && ctl.SnagSlack < 0.6f; t += Time.deltaTime) yield return null;
                    SfKeys();
                    for (float t = 0f; t < 3f && ctl.State == FishingController.S.Snagged && !ctl.SnagLoose; t += Time.deltaTime) yield return null;
                    SfKeys(wind: true);
                    for (float t = 0f; t < 2f && ctl.State == FishingController.S.Snagged && ctl.SnagSlack >= FishingController.SlackMin; t += Time.deltaTime) yield return null;
                    for (float t = 0f; t < 4f && ctl.State == FishingController.S.Snagged; t += Time.deltaTime)
                    {
                        var g = ctl.SnagArrowDir;
                        SfKeys(l: g.x < 0, r: g.x > 0, up: g.y > 0, down: g.y < 0);
                        yield return null;
                    }
                    SfKeys();
                    worked = ctl.SnagFrees > frees0 ? $"freed ({ctl.LastFreeWay})" : "still caught";
                    if (ctl.SnagFrees > frees0) dzWorked++;
                }
                if (ctl.State == FishingController.S.Snagged) ctl.Retrieve();   // (끊기)
                Log(string.Format(CIc, "[SNAGFREE] dead zone try {0}: caught on {1}, wedged {2}, left alone {3}, worked: {4}", i, sn.zone?.id, ctl.SnagWedged, held ? "held" : "came free", worked));
            }
            SfKeys();
            FishingController.NoBites = false;
            int failed = 0;
            void Check(bool ok, string what)
            {
                if (!ok) failed++;
                Log($"[SNAGFREE] CHECK {(ok ? "PASS" : "FAIL")} {what}");
            }
            foreach (var w in ways)
                Log(string.Format(CIc, "[SNAGFREE] {0}: {1} caught, {2} freed ({3:0}%{4}), {5} cut, {6} broke; freed in {7:0.00} s on average",
                    w.name, w.catches, w.freed, 100f * w.Share(w.freed), w.how.Count > 0 ? ": " + string.Join(", ", w.how.Select(kv => $"{kv.Key} {kv.Value}")) : "",
                    w.cut, w.broke, w.freed > 0 ? w.freeT / w.freed : 0f));
            Log($"[SNAGFREE] {tries} tries in {Time.realtimeSinceStartup - start:0} s: {caughtN} prop catches, {other} other snags cut");
            Log($"[SNAGFREE] digest {digest}");
            var sw = ways[0];
            var sl = ways[1];
            var ti = ways[2];
            Check(ways.All(x => x.catches >= 10), $"every way had at least 10 catches ({sw.catches} / {sl.catches} / {ti.catches})");
            Check(sw.Share(sw.freed) >= 0.6f, $"the sweep alone still frees at least 60 % ({100f * sw.Share(sw.freed):0}%)");
            Check(sl.Share(sl.freed) >= 0.85f && sl.Share(sl.freed) >= sw.Share(sw.freed),
                $"slack and the rod's moves free at least 85 % and no less than the sweep ({100f * sl.Share(sl.freed):0}% vs {100f * sw.Share(sw.freed):0}%)");
            Check(ti.Share(ti.freed) < sw.Share(sw.freed) && ti.Share(ti.broke) > sw.Share(sw.broke),
                $"pulling tight frees less and breaks more than the sweep (freed {100f * ti.Share(ti.freed):0}% vs {100f * sw.Share(sw.freed):0}%, broke {100f * ti.Share(ti.broke):0}% vs {100f * sw.Share(sw.broke):0}%)");
            Check(dzCaught > 0 && dzHeld == dzCaught, $"the dead zone catches and, left alone, holds ({dzHeld} of {dzCaught} held; the slack way then freed {dzWorked})");
            Log($"snagfree test done: {failed} failed");
            Application.Quit();
        }

        /// <summary>The fight strip's phase label now (the snag strip's guide).</summary>
        static string PhaseText()
        {
            var hud = FindAnyObjectByType<FishingHUD>();
            return hud != null ? hud.PhaseText : "";
        }
    }
}
