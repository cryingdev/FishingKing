using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The time of day and the moving water (Docs/time_currents_spec.md 15):
    /// <list type="bullet">
    /// <item>-fkauto periods (phase 2): every stage at the four period centres (the render target per_&lt;stage&gt;_&lt;period&gt;.png
    /// and the full screen _hud), one mid-dissolve frame per stage (per_&lt;stage&gt;_x.png, 08:00 = F 0.5), the map in the four
    /// periods (map_&lt;period&gt;.png) and a grid of all the render-target frames (per_grid.png: dawn | day | evening | night);</item>
    /// <item>-fkauto current (phase 4): the stream's drift and a mend, the sea's tide on bites (flood peak vs high slack), a
    /// spinner hanging in the ebb, a stream fight with downstream runs and side pressure against the with-current runs,
    /// logged as [CUR] lines with [CUR] CHECK results, and 4 shots.</item>
    /// </list>
    /// Use with -fkrich (every stage open) and -fkshots &lt;dir&gt;.
    /// </summary>
    public partial class AutoPilot
    {
        static readonly CultureInfo CIc = CultureInfo.InvariantCulture;
        int curFails;

        void CCheck(string what, bool ok)
        {
            if (!ok) curFails++;
            Log($"[CUR] CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        /// <summary>The pixel view's render target now (480x270, no UI), as a readable texture (null without one).</summary>
        static Texture2D GrabRT()
        {
            var rt = PixelView.Current != null ? PixelView.Current.Target : null;
            if (rt == null) return null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            t.Apply(false);
            RenderTexture.active = prev;
            return t;
        }

        IEnumerator NamedShot(string name)
        {
            yield return new WaitForEndOfFrame();
            string p = Path.Combine(shots, name + ".png");
            ScreenCapture.CaptureScreenshot(p);
            Log("shot " + p);
            yield return null;
        }

        IEnumerator GoStage(string id, float wait = 2.5f)
        {
            SceneFlow.PendingStage = id;
            SceneFlow.Go("Fishing");
            yield return new WaitForSeconds(wait);
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.4f);
            }
        }

        // ------------------------------------------------------------------ phase 2: the four looks of every stage
        IEnumerator PeriodsTest()
        {
            yield return new WaitForSeconds(1.5f);
            GameClock.Scale = 0f;
            PointerInput.SimActive = false;
            var grid = new Dictionary<(int, int), Texture2D>();
            int w = 0, h = 0;
            var stages = GameDatabase.Stages;
            for (int si = 0; si < stages.Count; si++)
            {
                var st = stages[si];
                GameClock.Min = GameClock.Centre(Period.Day);
                yield return GoStage(st.id);
                for (int pi = 0; pi < 4; pi++)
                {
                    var p = (Period)pi;
                    GameClock.Min = GameClock.Centre(p);
                    yield return new WaitForSeconds(3f);
                    yield return new WaitForEndOfFrame();
                    var t = GrabRT();
                    if (t != null)
                    {
                        w = t.width;
                        h = t.height;
                        File.WriteAllBytes(Path.Combine(shots, $"per_{st.id}_{GameClock.Id(p)}.png"), t.EncodeToPNG());
                        grid[(si, pi)] = t;
                    }
                    yield return NamedShot($"per_{st.id}_{GameClock.Id(p)}_hud");
                    var sv = FindAnyObjectByType<StageView>();
                    Log(string.Format(CIc, "[PER] {0} {1}: look {2} water #{3} tint #{4} glints {5:0.00} fx {6:0.00} rim #{7} {8:0.00}",
                        st.id, GameClock.Id(p), sv != null ? sv.Now.Blend.ToString() : "-", sv != null ? ColorUtility.ToHtmlStringRGB(sv.WaterTint) : "-",
                        sv != null ? ColorUtility.ToHtmlStringRGB(sv.ActorTint) : "-", sv != null ? sv.Now.GlintDensity : 0f, sv != null ? sv.Now.FxAlpha : 0f,
                        sv != null ? ColorUtility.ToHtmlStringRGB(sv.Now.RimCol) : "-", sv != null ? sv.Now.RimStrength : 0f));
                }
                // the middle of the dawn -> day dissolve (08:00: F = 0.5, half the pixels of each)
                GameClock.Min = 480f;
                yield return new WaitForSeconds(1.2f);
                yield return new WaitForEndOfFrame();
                var x = GrabRT();
                if (x != null)
                {
                    File.WriteAllBytes(Path.Combine(shots, $"per_{st.id}_x.png"), x.EncodeToPNG());
                    Destroy(x);
                }
            }
            // the map in the four periods
            for (int pi = 0; pi < 4; pi++)
            {
                GameClock.Min = GameClock.Centre((Period)pi);
                SceneFlow.Go("Map");
                yield return new WaitForSeconds(1.6f);
                yield return NamedShot($"map_{GameClock.Id((Period)pi)}");
            }
            // one grid of every render-target frame: a row per stage, dawn | day | evening | night
            if (w > 0)
            {
                const int gap = 2;
                var g = new Texture2D(4 * w + 3 * gap, stages.Count * h + (stages.Count - 1) * gap, TextureFormat.RGB24, false);
                var fill = new Color32[g.width * g.height];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 28, 255);
                g.SetPixels32(fill);
                foreach (var kv in grid)
                {
                    var (si, pi) = kv.Key;
                    if (kv.Value.width != w || kv.Value.height != h) continue;
                    g.SetPixels(pi * (w + gap), (stages.Count - 1 - si) * (h + gap), w, h, kv.Value.GetPixels());
                }
                g.Apply(false);
                File.WriteAllBytes(Path.Combine(shots, "per_grid.png"), g.EncodeToPNG());
                Log($"[PER] grid {g.width}x{g.height}: {grid.Count} frames");
            }
            foreach (var t in grid.Values) Destroy(t);
            Log("[PER] done");
            Application.Quit();
        }

        // ------------------------------------------------------------------ phase 4: the current in play
        IEnumerator CurrentTest()
        {
            yield return new WaitForSeconds(2f);
            PointerInput.SimDpi = 0f;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            SteerGear("rod_carbon", "reel_highgear", null);
            yield return CurDrift();
            yield return CurTide();
            yield return CurHang();
            yield return CurFight();
            FishingController.NoBites = false;
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Log($"[CUR] current test done: {curFails} failed");
            Application.Quit();
        }

        static void EquipTest(string id, FishingController ctl)
        {
            var b = GameDatabase.GetItem<BaitDef>(id);
            if (b == null) return;
            if (b.isLure) { if (!Game.I.Owns(b.id)) Game.Data.ownedItems.Add(b.id); }
            else Game.I.AddBait(b.id, 99);
            ctl.EquipBait(b);
        }

        IEnumerator ToReady(FishingController ctl)
        {
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            for (float w = 0f; w < 30f && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
            {
                if (ctl.State == FishingController.S.Waiting || ctl.State == FishingController.S.Snagged) ctl.Retrieve();   // (snagged: 끊기)
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>1. The stream: a float drifts down the lane; then a float off to the side bows its line and is mended.</summary>
        IEnumerator CurDrift()
        {
            yield return GoStage("stream");
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null) { CCheck("stream scene", false); yield break; }
            FishingController.NoBites = true;
            EquipTest("bait_worm", ctl);
            yield return null;
            var cf = ctl.Stage.Current;
            var tk = ctl.Tackle;
            CCheck("float placed at (0, 25) in the lane", ctl.DebugPlaceRig(new Vector3(0f, 0f, 25f)));
            yield return new WaitForSeconds(1f);
            var p0 = tk.Surface;
            float expSum = 0f;
            int n = 0;
            for (int s = 1; s <= 12; s++)
            {
                float t0 = Time.time;
                var a = tk.Surface;
                float exp = 0f;
                int k = 0;
                while (Time.time - t0 < 1f)
                {
                    exp += 0.85f * cf.Water(tk.Surface.x, tk.Surface.z).y;
                    k++;
                    yield return null;
                }
                exp /= Mathf.Max(1, k);
                expSum += exp;
                n++;
                Log(string.Format(CIc, "[CUR] drift t {0:00}s x {1:0.00} z {2:0.00} dz/dt {3:+0.00;-0.00} (0.85 x water {4:+0.00;-0.00}) bow {5:+0.00;-0.00} rel {6:0.00} lane {7:0.00} surge {8:0.00}",
                    s, tk.Surface.x, tk.Surface.z, tk.Surface.z - a.z, exp, tk.Bow, tk.RelSpeed, cf.Lane(tk.Surface.x, tk.Surface.z), cf.Surge(tk.Surface.z)));
            }
            yield return NamedShot("cur_stream_drift");
            float measured = (tk.Surface.z - p0.z) / 12f, expected = expSum / Mathf.Max(1, n);
            CCheck(string.Format(CIc, "drift: the float comes down the lane at {0:0.00} m/s, 0.85 x the lane's water {1:0.00} m/s (+-20 %)", -measured, -expected),
                expected < -0.05f && Mathf.Abs(measured - expected) <= 0.2f * Mathf.Abs(expected));
            // the mend: a float off to the side bows its line in the flow
            yield return ToReady(ctl);
            CCheck("float placed at (5, 12) across the flow", ctl.DebugPlaceRig(new Vector3(5f, 0f, 12f)));
            for (float w = 0f; w < 15f && Mathf.Abs(tk.Bow) < 0.8f; w += Time.deltaTime) yield return null;
            Log(string.Format(CIc, "[CUR] bow built up to {0:+0.00;-0.00} m at ({1:0.00}, {2:0.00})", tk.Bow, tk.Surface.x, tk.Surface.z));
            yield return NamedShot("cur_stream_bow");
            float bow0 = tk.Bow, drift0 = tk.SweepDrift;
            int mends0 = ctl.Mends;
            // sweep against the belly (A / D: the keys commit a full sweep at once)
            PointerInput.SimActive = true;
            PointerInput.SimLeft = bow0 > 0f;
            PointerInput.SimRight = bow0 < 0f;
            yield return new WaitForSeconds(0.5f);
            float bow1 = tk.Bow, dragged = Mathf.Abs(tk.SweepDrift - drift0);
            PointerInput.SimLeft = PointerInput.SimRight = false;
            yield return new WaitForSeconds(0.3f);
            yield return NamedShot("cur_stream_mended");
            Log(string.Format(CIc, "[CUR] mend: bow {0:+0.00;-0.00} -> {1:+0.00;-0.00} in 0.5 s, the sweep dragged it {2:0.00} m, mends {3}", bow0, bow1, dragged, ctl.Mends - mends0));
            CCheck(string.Format(CIc, "mend: the belly drops >= 70 % within 0.5 s ({0:+0.00;-0.00} -> {1:+0.00;-0.00}) and the sweep moves the float <= 0.15 m ({2:0.00})", bow0, bow1, dragged),
                ctl.Mends > mends0 && Mathf.Abs(bow0) >= 0.5f && Mathf.Abs(bow1) <= 0.3f * Mathf.Abs(bow0) && dragged <= 0.151f);
            yield return ToReady(ctl);
        }

        /// <summary>2. The sea: approaches to a shrimp float at the flood's peak vs at high slack (120 s each, every approach let go).</summary>
        IEnumerator CurTide()
        {
            FishSpawner.OnlySpecies = GameDatabase.GetFish("mackerel");
            FishingController.NoBites = false;
            yield return GoStage("sea", 3f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null) { CCheck("sea scene", false); yield break; }
            EquipTest("bait_shrimp", ctl);
            yield return null;
            var counts = new Dictionary<string, int>();
            var rolls = new Dictionary<string, int>();
            foreach (var (name, phase) in new[] { ("flood", 0.25f), ("slack", 0.5f) })
            {
                GameClock.TidePhase = phase;
                yield return ToReady(ctl);
                var at = new Vector3(ctl.Angler.X, 0f, 14f);
                ctl.DebugPlaceRig(at);
                int a0 = ctl.Approaches, r0 = ctl.ApproachRolls;
                float t = 0f, fixT = 0f;
                bool shot = false;
                while (t < 120f)
                {
                    t += Time.deltaTime;
                    fixT += Time.deltaTime;
                    if (!shot && t >= 3f)
                    {
                        shot = true;
                        yield return NamedShot("cur_sea_" + name);
                    }
                    // every approach is let go (no hook set): the rig stays in the water
                    foreach (var f in ctl.Spawner.Fish)
                        if (f.State == FishAgent.St.Approach || f.State == FishAgent.St.Nibble || f.State == FishAgent.St.Bite) f.LoseInterest();
                    if (ctl.State != FishingController.S.Waiting)
                    {
                        yield return ToReady(ctl);
                        ctl.DebugPlaceRig(at);
                    }
                    // (the tide carries the float off: back to the soak spot now and then, as a player would recast)
                    if (fixT >= 5f && ctl.Tackle.State == Tackle.Mode.Water && (ctl.Tackle.Surface - at).magnitude > 2f)
                    {
                        fixT = 0f;
                        ctl.Tackle.Surface = at;
                    }
                    yield return null;
                }
                counts[name] = ctl.Approaches - a0;
                rolls[name] = ctl.ApproachRolls - r0;
                var tide = GameClock.Tide;
                Log(string.Format(CIc, "[CUR] tide {0} (s {1:0.00}, bite x{2:0.00}): {3} approaches of {4} rolls in 120 s ({5:0.00} per roll)", name, tide.S, ctl.TideMult(),
                    counts[name], rolls[name], counts[name] / (float)Mathf.Max(1, rolls[name])));
            }
            float ratio = counts["flood"] / (float)Mathf.Max(1, counts["slack"]);
            float rateF = counts["flood"] / (float)Mathf.Max(1, rolls["flood"]), rateS = counts["slack"] / (float)Mathf.Max(1, rolls["slack"]);
            float rateRatio = rateF / Mathf.Max(1e-4f, rateS);
            // (every approach is let go here, so each fish waits 3-6 s before it can come again: the counts saturate, the
            // chance per roll shows the tide's factor itself)
            CCheck(string.Format(CIc, "tide: a fish's chance to come per roll at the flood's peak / at high slack = {0:0.00} / {1:0.00} = x{2:0.00} (>= 1.5); approaches {3} / {4} = x{5:0.00} (more at the flood)",
                rateF, rateS, rateRatio, counts["flood"], counts["slack"], ratio), rateRatio >= 1.5f && counts["flood"] > counts["slack"]);
            // the flood never carries the float out of view (the sea's xLim is 90 m): it stops at the edge / the tetrapods
            GameClock.TidePhase = 0.25f;
            FishingController.NoBites = true;
            yield return ToReady(ctl);
            ctl.DebugPlaceRig(new Vector3(ctl.Angler.X - 2f, 0f, 14f));
            for (float w = 0f; w < 25f && ctl.Tackle.State == Tackle.Mode.Water; w += Time.deltaTime) yield return null;
            var es = ctl.Tackle.Surface;
            yield return NamedShot("cur_sea_edge");
            CCheck(string.Format(CIc, "flood edge: after 25 s the float is still in view at ({0:0.00}, {1:0.00}), rel {2:0.00} m/s", es.x, es.z, ctl.Tackle.RelSpeed),
                ctl.Tackle.State == Tackle.Mode.Water && ctl.Stage.Water.DriftOpen(es.x, es.z));
            FishingController.NoBites = false;
            FishSpawner.OnlySpecies = null;
            yield return ToReady(ctl);
        }

        /// <summary>3. The sea at peak ebb: a spinner out on the ebb side, not wound, hangs in the current and works.</summary>
        IEnumerator CurHang()
        {
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Stage.Def.id != "sea") { CCheck("sea for the hanging lure", false); yield break; }
            FishingController.NoBites = true;
            GameClock.TidePhase = 0.75f;
            yield return ToReady(ctl);
            SteerGear(Game.I.Rod.id, GameDatabase.StarterReel, null);   // (the basic reel: 0.8 m per turn, as in the spec's example)
            EquipTest("bait_spinner", ctl);
            yield return null;
            var tk = ctl.Tackle;
            ctl.DebugPlaceRig(new Vector3(15f, 0f, 10f));
            // held 0.5 m deep until it hangs
            for (float w = 0f; w < 0.6f; w += Time.deltaTime)
            {
                tk.Depth = 0.5f;
                yield return null;
            }
            var p0 = tk.Surface;
            float d0 = tk.Depth;
            for (float w = 0f; w < 6f; w += Time.deltaTime) yield return null;
            float moved = new Vector2(tk.Surface.x - p0.x, tk.Surface.z - p0.z).magnitude;
            Log(string.Format(CIc, "[CUR] hang: c_along {0:+0.00;-0.00} m/s ({1:0.00} rev/s of the {2} reel), hanging {3}, in band {4}, Q {5:0.00}, moved {6:0.00} m, depth {7:0.00} -> {8:0.00}",
                ctl.Rhythm.CAlong, ctl.Rhythm.CAlong / Game.I.Reel.retrieve, Game.I.Reel.id, ctl.Rhythm.Hanging, ctl.Rhythm.InBand, ctl.Rhythm.Q, moved, d0, tk.Depth));
            yield return NamedShot("cur_sea_hang");
            CCheck(string.Format(CIc, "hang: Q {0:0.00} >= 0.5 and the rig moved {1:0.00} m < 0.3 (hanging {2})", ctl.Rhythm.Q, moved, ctl.Rhythm.Hanging),
                ctl.Rhythm.Q >= 0.5f && moved < 0.3f && ctl.Rhythm.Hanging);
            GameClock.TidePhase = null;
            yield return ToReady(ctl);
        }

        /// <summary>
        /// 4. The stream: a hooked rainbow trout's runs; side pressure against every with-current run. Checks a downstream
        /// run, a with-current run turned (pulled out of the flow: the load x 0.3 for 4 s) and that runs with the current
        /// load the line more than the others.
        /// </summary>
        IEnumerator CurFight()
        {
            yield return GoStage("stream");
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null) { CCheck("stream scene for the fight", false); yield break; }
            FishingController.NoBites = true;
            SteerGear("rod_glass", "reel_light", "line_nylon4");
            var sp = GameDatabase.GetFish("rainbow_trout");
            int runs = 0;
            bool shotRun = false, shotTurn = false;
            float withT = 0f, withTen = 0f, otherT = 0f, otherTen = 0f, withLine = 0f, otherLine = 0f;
            float loadBefore = -1f, loadAfterSum = 0f, loadAfterN = 0f, turnAt = -1f;
            Action<int, float, bool> onRun = (side, dur, turned) => runs++;
            ctl.RunEnded += onRun;
            for (int fish = 0; fish < 4 && runs < 6; fish++)
            {
                yield return ToReady(ctl);
                EquipTest("bait_minnow", ctl);
                yield return null;
                ctl.DebugPlaceRig(new Vector3(0f, 0f, 16f));
                yield return new WaitForSeconds(0.3f);
                if (!ctl.DebugHook(sp, 55f, 777 + fish, new Vector3(1f, -1.2f, 18f))) { CCheck("hook the trout", false); break; }
                var f = ctl.Fight;
                var c = Scr(0.72f, 0.42f);
                float r = Screen.height * 0.13f, ang = 0f, ws = CircleGesture.Reversed ? -1f : 1f, t = 0f, withRunT = 0f;
                PointerInput.SimActive = true;
                while (ctl.State == FishingController.S.Fighting && ctl.Fight == f && t < 40f && runs < 8)
                {
                    float dt = Time.deltaTime;
                    t += dt;
                    int run = ctl.FishRun;
                    bool running = (f.State == FightModel.Phase.Run || f.State == FightModel.Phase.Burst) && !f.Exhausted && !f.Jumping;
                    // the same reeling throughout: wind while it rests, stop while it runs, give line near the break
                    if (f.TensionRatio > 0.95f) ang += ws * dt * 1.0f * Mathf.PI * 2f;
                    else if (!running && !f.Jumping && f.TensionRatio < 0.8f) ang -= ws * dt * 1.4f * Mathf.PI * 2f;
                    PointerInput.SimDown = true;
                    PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    // side pressure against the runs that go with the current (the downstream ones included)
                    bool withCur = run != 0 && (ctl.RunAlign >= 0.3f || ctl.DownstreamRun);
                    int lean = withCur && withRunT >= 0.4f ? -run : 0;
                    PointerInput.SimLeft = lean < 0;
                    PointerInput.SimRight = lean > 0;
                    float line0 = f.Line;
                    yield return null;
                    if (ctl.Fight != f) break;
                    withRunT = withCur ? withRunT + dt : 0f;
                    if (running)
                    {
                        if (ctl.RunAlign >= 0.3f) { withT += dt; withTen += f.TensionRatio * dt; withLine += f.Line - line0; }
                        else { otherT += dt; otherTen += f.TensionRatio * dt; otherLine += f.Line - line0; }
                    }
                    if (!shotRun && (ctl.DownstreamRun || ctl.RunAlign >= 0.5f) && withRunT >= 0.3f && lean == 0)
                    {
                        shotRun = true;
                        Log(string.Format(CIc, "[CUR] shot: run {0} align {1:+0.00;-0.00} downstream {2} tension {3:0.00} load {4:0.00}", run, ctl.RunAlign, ctl.DownstreamRun, f.TensionRatio, f.CurrentLoad));
                        yield return NamedShot("cur_fight_run");
                    }
                    if (ctl.OutOfFlowT > 0f)
                    {
                        if (turnAt < 0f)
                        {
                            turnAt = t;
                            Log(string.Format(CIc, "[CUR] pulled out of the flow at {0:0.00}s: load {1:0.00} -> {2:0.00}", t, loadBefore, f.CurrentLoad));
                        }
                        if (ctl.OutOfFlowT > 0.2f && loadBefore > 0f)
                        {
                            loadAfterSum += f.CurrentLoad / loadBefore;
                            loadAfterN++;
                        }
                        if (!shotTurn && t - turnAt >= 0.5f)
                        {
                            shotTurn = true;
                            yield return NamedShot("cur_fight_turned");
                        }
                    }
                    else if (running && f.CurrentLoad > 0f) loadBefore = f.CurrentLoad / (ctl.DownstreamRun ? 1.5f : 1f);
                }
                PointerInput.SimLeft = PointerInput.SimRight = false;
                PointerInput.SimDown = false;
                Log(string.Format(CIc, "[CUR] fight {0}: {1:0.0}s, runs {2}, downstream {3}, pulled out {4}, stamina {5:0.00}", fish, t, runs, ctl.DownstreamRuns, ctl.FlowTurns, ctl.Fight != null ? ctl.Fight.Stamina : -1f));
                if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
                for (float w = 0f; w < 4f && ctl.State != FishingController.S.Ready && ctl.State != FishingController.S.Result; w += Time.deltaTime) yield return null;
                if (ctl.State == FishingController.S.Result || ctl.State == FishingController.S.Landing)
                {
                    for (float w = 0f; w < 4f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                    Click("판매");
                    yield return new WaitForSeconds(0.6f);
                }
            }
            ctl.RunEnded -= onRun;
            float wT = withT > 0f ? withTen / withT : 0f, oT = otherT > 0f ? otherTen / otherT : 0f;
            Log(string.Format(CIc, "[CUR] fight summary: with-current running {0:0.0}s (tension {1:0.000} of the line, line taken {2:+0.00;-0.00} m/s), other running {3:0.0}s (tension {4:0.000}, {5:+0.00;-0.00} m/s)",
                withT, wT, withT > 0f ? withLine / withT : 0f, otherT, oT, otherT > 0f ? otherLine / otherT : 0f));
            CCheck($"downstream: {ctl.DownstreamRuns} downstream run(s) in {runs} runs", ctl.DownstreamRuns >= 1);
            float after = loadAfterN > 0 ? loadAfterSum / loadAfterN : -1f;
            CCheck(string.Format(CIc, "turn: {0} with-current run(s) pulled out of the flow, the load after x{1:0.00} of before (want ~x0.3)", ctl.FlowTurns, after),
                ctl.FlowTurns >= 1 && after > 0.15f && after < 0.45f);
            CCheck(string.Format(CIc, "harder: running with the current loads the line more ({0:0.000} vs {1:0.000} of the line)", wT, oT), withT > 0.3f && otherT > 0.3f && wT > oT);
        }
    }
}
