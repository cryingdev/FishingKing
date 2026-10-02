using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto tidebites (Docs/time_currents_spec.md 15, phase 4b): the tide on the bites per minute. On the stage it opens
    /// on (-fkstage; the stock as usual, or one species with -fkfish) with -fkbait (default 새우) on a float, one fixed
    /// rig (카본 루어 로드, 하이기어 릴, 나일론 4호), the clock frozen at 12:30 and the tide fixed at high slack (phase
    /// 0.5) and at the flood's peak (0.25): the stage is reloaded from the same seed, the float laid at (Angler.X, 14) and
    /// soaked for -fktidesecs game seconds (default 2400) on a fixed 1/60 s step, as fast as the machine draws. Every bite is
    /// counted and let go (the fish swims off as after a missed bite, the bait stays on, the rig stays out). Two ways to
    /// fish the peak: "recast", the float laid again when the water has carried it 8 m off, holds it against an edge (it
    /// moves less than half its free drift over a second) or has stopped it (under 0.1 m in a second: the slack against
    /// the tetrapods), as an active player would; "left alone", laid once and left wherever the water takes it (laid again
    /// only if it snags). Bites per minute of soak (the time the rig lies out); [TIDE] lines.
    /// On the sea the rules are measured three ways: before the reach (<see cref="FishingController.DebugOldTide"/>: slack,
    /// peak recast), before the tetrapods' slack (<see cref="CurrentField.DebugNoCushion"/>: peak recast and left alone) and
    /// now (slack, peak recast, peak left alone). CHECK now: peak recast >= 1.3 x slack, left alone >= 1.0 x slack and lying
    /// in the slack off the tetrapods in deep water, and a float laid far out held at the edge of the view in the running
    /// tide shows the pin hint (a float in the slack does not). Elsewhere (no tide): CHECK the tide's factors are 1 at both
    /// phases (the same code runs at both: their two rates are logged as the run-to-run spread, ~x0.7–1.4 for the ocean's
    /// mixed stock of 7).
    /// </summary>
    public partial class AutoPilot
    {
        int tideFails;

        void TCheck(string what, bool ok)
        {
            if (!ok) tideFails++;
            Log($"[TIDE] CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        struct TideRun
        {
            public int bites, approaches, rolls, recasts, held, stopped, relays;
            public float soak, inReach, free, real, slackT, depthSum, restT;
            public bool pin;
            public Vector3 end;
            public float PerMin => soak > 0f ? bites * 60f / soak : 0f;
            public float SlackShare => soak > 0f ? slackT / soak : 0f;
            public float MeanDepth => soak > 0f ? depthSum / soak : 0f;
        }

        /// <summary>One soak: its tide phase, its rules (before the reach / before the slack at the tetrapods / now) and how the peak is fished.</summary>
        struct TideSpec
        {
            public string key, label;
            public float phase;
            public bool old, noCushion, alone;

            public TideSpec(string key, string label, float phase, bool old, bool noCushion, bool alone)
            {
                this.key = key;
                this.label = label;
                this.phase = phase;
                this.old = old;
                this.noCushion = noCushion;
                this.alone = alone;
            }
        }

        IEnumerator TideBitesTest()
        {
            // the simulated pointer from the start, never pressed: a real mouse on a shared desktop must not touch the rig
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("no FishingController (use -fkscene Fishing -fkstage <id>)");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.4f);
            }
            string stage = ctl.Stage.Def.id;
            bool sea = stage == "sea";
            string baitId = Arg("-fkbait") ?? "bait_shrimp";
            float secs = Mathf.Max(60f, ArgF("-fktidesecs") ?? 2400f);
            SteerGear("rod_carbon", "reel_highgear", "line_nylon4");
            FishingController.NoBites = false;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            var tideWas = GameClock.TidePhase;
            int fpsWas = Application.targetFrameRate;
            float capWas = Time.captureDeltaTime;
            // a fixed step, as fast as it draws (the same game seconds whatever the machine)
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            bool hintWas = Game.Data.tideHint, hintSet = false;
            Log(string.Format(CIc, "[TIDE] {0}: bait {1}, stock {2}, {3:0} s of soak per run, rod {4} line {5}, clock {6}",
                stage, baitId, FishSpawner.OnlySpecies != null ? FishSpawner.OnlySpecies.id : "the stage's", secs, Game.I.Rod.id, Game.I.Line.id,
                GameClock.HHMM(GameClock.Min, false)));
            var specs = sea
                ? new[]
                {
                    new TideSpec("old slack", "before the reach, slack", 0.5f, true, true, false),
                    new TideSpec("old peak", "before the reach, peak recast", 0.25f, true, true, false),
                    new TideSpec("bare peak", "before the tetrapods' slack, peak recast", 0.25f, false, true, false),
                    new TideSpec("bare alone", "before the tetrapods' slack, peak left alone", 0.25f, false, true, true),
                    new TideSpec("now slack", "now, slack", 0.5f, false, false, false),
                    new TideSpec("now peak", "now, peak recast", 0.25f, false, false, false),
                    new TideSpec("now alone", "now, peak left alone", 0.25f, false, false, true),
                }
                : new[]
                {
                    new TideSpec("now slack", "slack", 0.5f, false, false, false),
                    new TideSpec("now peak", "peak recast", 0.25f, false, false, false),
                };
            var runs = new Dictionary<string, TideRun>();
            bool broken = false;
            foreach (var spec in specs)
            {
                FishingController.DebugOldTide = spec.old;
                CurrentField.DebugNoCushion = spec.noCushion;
                GameClock.TidePhase = spec.phase;
                Random.InitState(1515);
                var prev = ctl;
                yield return GoStage(stage, 3f);
                // the scene loads in real time while the fixed step runs ahead of it: wait for the new scene's controller
                float load0 = Time.realtimeSinceStartup;
                ctl = null;
                while (Time.realtimeSinceStartup - load0 < 60f)
                {
                    var c = FindAnyObjectByType<FishingController>();
                    if (c != null && c != prev && c.State == FishingController.S.Ready)
                    {
                        ctl = c;
                        break;
                    }
                    yield return null;
                }
                if (ctl == null)
                {
                    broken = true;
                    break;
                }
                yield return new WaitForSeconds(3f);
                if (Dialog.Open)
                {
                    Click("알겠어요");
                    yield return new WaitForSeconds(0.4f);
                }
                EquipTest(baitId, ctl);
                yield return null;
                var tk = ctl.Tackle;
                var cf = ctl.Stage.Current;
                var spot = new Vector3(ctl.Angler.X, 0f, 14f);
                // (does the water at the spot run? the slack at high water: no float ever stops "in the slack" there)
                bool running = cf != null && cf.Water(spot.x, spot.z).magnitude >= 0.1f;
                yield return ToReady(ctl);
                // (the pin hint, shown once ever: cleared for each run on this test save, so each run shows whether it came)
                if (sea) Game.Data.pinHint = false;
                ctl.DebugPlaceRig(spot);
                if (sea && !hintWas && !hintSet) hintSet = Game.Data.tideHint;
                var r = new TideRun();
                float real0 = Time.realtimeSinceStartup, game = 0f;
                int a0 = ctl.Approaches, r0 = ctl.ApproachRolls;
                // a 1 s window of the float's way against its free drift: held by an edge (less than half its drift) or
                // stopped (under 0.1 m while the water at the spot runs)
                var lastAt = tk.Surface;
                float winT = 0f, winMoved = 0f, winFree = 0f, still = 0f;
                while (r.soak < secs && game < secs * 3f)
                {
                    if (ctl == null)
                    {
                        broken = true;
                        break;
                    }
                    game += Time.deltaTime;
                    if (ctl.State == FishingController.S.Biting)
                    {
                        // counted and let go: the fish swims off, the rig stays out
                        r.bites++;
                        ctl.DebugLetBiteGo();
                    }
                    else if (ctl.State == FishingController.S.Waiting && tk.State == Tackle.Mode.Water)
                    {
                        winMoved += new Vector2(tk.Surface.x - lastAt.x, tk.Surface.z - lastAt.z).magnitude;
                        winFree += tk.FreeDrift.magnitude * Time.deltaTime;
                        winT += Time.deltaTime;
                        lastAt = tk.Surface;
                        bool held = false, stopped = false;
                        if (winT >= 1f)
                        {
                            held = winFree >= 0.1f && winMoved < 0.5f * winFree && !(cf != null && cf.Slack(tk.Surface.x, tk.Surface.z));
                            stopped = !held && running && winMoved < 0.1f * winT;
                            // (left alone: how long it has lain still)
                            still = winMoved < 0.1f * winT ? still + winT : 0f;
                            winT = winMoved = winFree = 0f;
                        }
                        bool off = new Vector2(tk.Surface.x - spot.x, tk.Surface.z - spot.z).magnitude >= 8f;
                        if (!spec.alone && (held || stopped || off))
                        {
                            // the water carried it off, pinned it or stopped it: wound in and laid at the spot again, as an
                            // active player would
                            r.recasts++;
                            if (held) r.held++;
                            if (stopped) r.stopped++;
                            yield return ToReady(ctl);
                            ctl.DebugPlaceRig(spot);
                            lastAt = tk.Surface;
                            winT = winMoved = winFree = still = 0f;
                            continue;
                        }
                        float dt = Time.deltaTime;
                        r.soak += dt;
                        // where it lies: in the slack against the tetrapods, the depth under it, when it came to rest
                        var s = tk.Surface;
                        if (cf != null && cf.Slack(s.x, s.z)) r.slackT += dt;
                        r.depthSum += ctl.Stage.L.DepthAt(s.x, s.z) * dt;
                        if (still >= 5f && r.restT <= 0f) r.restT = r.soak;
                        // the fish within the game's reach and depth tests while none is coming (the stock the bites come from)
                        if (!ctl.Spawner.Fish.Any(f => f.Engaged))
                        {
                            r.free += dt;
                            var hook = tk.HookPos;
                            float sense = ctl.SenseRange();
                            int n = 0;
                            foreach (var f in ctl.Spawner.Fish)
                                if (f.State == FishAgent.St.Wander && f.Sp.encounter == null && f.Sp.Appeal(tk.Bait) > 0f
                                    && new Vector2(hook.x - f.Pos.x, hook.z - f.Pos.z).magnitude <= sense && Mathf.Abs(f.Depth - tk.Depth) <= 2.5f) n++;
                            r.inReach += n * dt;
                        }
                    }
                    else if (ctl.State != FishingController.S.Waiting)
                    {
                        // (snagged, or anything else that took the rig out of the water: freed and laid again)
                        r.relays++;
                        yield return ToReady(ctl);
                        ctl.DebugPlaceRig(spot);
                        lastAt = tk.Surface;
                        winT = winMoved = winFree = still = 0f;
                        continue;
                    }
                    yield return null;
                }
                if (broken) break;
                r.approaches = ctl.Approaches - a0;
                r.rolls = ctl.ApproachRolls - r0;
                r.real = Time.realtimeSinceStartup - real0;
                r.end = tk.Surface;
                r.pin = sea && Game.Data.pinHint;
                runs[spec.key] = r;
                var tide = GameClock.Tide;
                var we = cf != null ? cf.Water(r.end.x, r.end.z) : Vector2.zero;
                Log(string.Format(CIc, "[TIDE] {0} {1}: tide s {2:0.00}, bite x{3:0.00}, reach x{4:0.00} ({5:0.00} m) at the end: {6} bites in {7:0} s of soak = {8:0.00}/min; approaches {9} of {10} rolls; fish in reach while none comes {11:0.00}; recasts {12} ({13} held at an edge, {14} stopped in slack water), laid again {15} (snags); the float at the end ({16:0.00}, {17:0.00}) water {18:0.00} m/s, in the tetrapods' slack {19:0}% of the soak, mean depth under it {20:0.0} m, at rest from {21:0} s; pin hint {22}; {23:0} game s in {24:0} real s",
                    stage, spec.label, tide.S, ctl.TideMult(), ctl.TideReach(), ctl.SenseRange(), r.bites, r.soak, r.PerMin, r.approaches, r.rolls,
                    r.free > 0f ? r.inReach / r.free : 0f, r.recasts, r.held, r.stopped, r.relays, r.end.x, r.end.z, we.magnitude, 100f * r.SlackShare,
                    r.MeanDepth, r.restT, r.pin ? "shown" : "no", game, r.real));
                yield return ToReady(ctl);
            }
            FishingController.DebugOldTide = false;
            CurrentField.DebugNoCushion = false;
            if (broken || ctl == null) TCheck($"{stage} scene for every run", false);
            else
            {
                float Ratio(TideRun p, TideRun s) => p.PerMin / Mathf.Max(1e-3f, s.PerMin);
                // the tide's factors at the two phases, read off the controller (no rig out: the open water's)
                GameClock.TidePhase = 0.5f;
                float mS = ctl.TideMult(), rS = ctl.TideReach();
                GameClock.TidePhase = 0.25f;
                float mP = ctl.TideMult(), rP = ctl.TideReach();
                var ns = runs["now slack"];
                var np = runs["now peak"];
                float rn = Ratio(np, ns);
                if (sea)
                {
                    var bs = runs["old slack"];
                    var bp = runs["old peak"];
                    var xp = runs["bare peak"];
                    var xa = runs["bare alone"];
                    var na = runs["now alone"];
                    float rb = Ratio(bp, bs), ra = Ratio(na, ns), rxa = Ratio(xa, ns), rxp = Ratio(xp, ns);
                    TCheck(string.Format(CIc, "sea factors: bite x{0:0.00} / x{1:0.00}, reach x{2:0.00} / x{3:0.00} at slack / peak (0.60 / 1.35, 0.85 / 1.30)", mS, mP, rS, rP),
                        Mathf.Abs(mS - 0.6f) < 0.01f && Mathf.Abs(mP - 1.35f) < 0.01f && Mathf.Abs(rS - 0.85f) < 0.01f && Mathf.Abs(rP - 1.3f) < 0.01f);
                    TCheck(string.Format(CIc, "sea bites/min, recast at the peak: {0:0.00} / slack {1:0.00} = x{2:0.00} (>= 1.30; {3} / {4} bites); before the tetrapods' slack x{5:0.00} ({6:0.00}); before the reach x{7:0.00} ({8:0.00} / {9:0.00})",
                        np.PerMin, ns.PerMin, rn, np.bites, ns.bites, rxp, xp.PerMin, rb, bp.PerMin, bs.PerMin),
                        rn >= 1.3f && ns.bites >= 20);
                    TCheck(string.Format(CIc, "sea bites/min, left alone at the peak: {0:0.00} / slack {1:0.00} = x{2:0.00} (>= 1.00; {3} bites); before the tetrapods' slack {4:0.00} = x{5:0.00}; recast x{6:0.00} of left alone",
                        na.PerMin, ns.PerMin, ra, na.bites, xa.PerMin, rxa, np.PerMin / Mathf.Max(1e-3f, na.PerMin)),
                        ra >= 1.0f && na.bites >= 20);
                    TCheck(string.Format(CIc, "left alone, the float lies in the slack off the tetrapods in deep water: in the slack {0:0}% of the soak (>= 75), mean depth under it {1:0.0} m (>= 5.5), at rest from {2:0} s at ({3:0.00}, {4:0.00}); before: in the slack {5:0}%, {6:0.0} m, at ({7:0.00}, {8:0.00})",
                        100f * na.SlackShare, na.MeanDepth, na.restT, na.end.x, na.end.z, 100f * xa.SlackShare, xa.MeanDepth, xa.end.x, xa.end.z),
                        na.SlackShare >= 0.75f && na.MeanDepth >= 5.5f);
                    if (!hintWas)
                        TCheck("the first sea cast shows the tide hint (물때: 들물·날물엔 입질이 활발하고, 만조·간조엔 뜸해요; tideHint saved)", hintSet);
                    else Log("[TIDE] the tide hint was shown before (not a fresh save): not checked");
                    // the pin hint: not for a float lying in the slack (the run left alone), but for one the running tide holds
                    // at the edge of the view: laid far out at the flood's peak, it reaches the left edge in ~30 s
                    yield return PinHintCheck(ctl, na.pin);
                }
                else
                {
                    // (the same code runs at both phases here: the two rates only show how far two runs of this stage's stock
                    // spread, not checked; the seed does not pin them, the scene loads in real time)
                    TCheck(string.Format(CIc, "{0} has no tide: factors 1 at both phases (bite x{1:0.00} / x{2:0.00}, reach x{3:0.00} / x{4:0.00}) and both runs bite ({5} / {6}); peak {7:0.00} / slack {8:0.00}/min = x{9:0.00} (the same rules: the run-to-run spread)",
                        stage, mS, mP, rS, rP, np.bites, ns.bites, np.PerMin, ns.PerMin, rn),
                        mS == 1f && mP == 1f && rS == 1f && rP == 1f && ns.bites >= 10 && np.bites >= 10);
                }
            }
            GameClock.TidePhase = tideWas;
            Application.targetFrameRate = fpsWas;
            Time.captureDeltaTime = capWas;
            Log($"[TIDE] tide bites test done: {tideFails} failed");
            Application.Quit();
        }

        /// <summary>
        /// The pin hint (spec 11.3) at the flood's peak: a float laid at (Angler.X, 24), beyond the tetrapods, is carried to
        /// the left edge of the view and held there in the running tide: the hint shows (pinHint saved) within 90 game s.
        /// <paramref name="aloneShown"/>: whether it came in the run left alone (lying in the slack: it must not).
        /// </summary>
        IEnumerator PinHintCheck(FishingController ctl, bool aloneShown)
        {
            GameClock.TidePhase = 0.25f;
            FishingController.NoBites = true;
            yield return ToReady(ctl);
            Game.Data.pinHint = false;
            var tk = ctl.Tackle;
            var cf = ctl.Stage.Current;
            ctl.DebugPlaceRig(new Vector3(ctl.Angler.X, 0f, 24f));
            float t = 0f;
            while (t < 90f && !Game.Data.pinHint && ctl.State == FishingController.S.Waiting)
            {
                t += Time.deltaTime;
                yield return null;
            }
            var s = tk.Surface;
            bool shown = Game.Data.pinHint;
            TCheck(string.Format(CIc, "pin hint (찌가 물살에 밀려 멈췄어요 — 감아서 물살 위쪽에 다시 던져요): a float laid at (Angler.X, 24) at the flood's peak held at ({0:0.00}, {1:0.00}) (water {2:0.00} m/s, in view {3}) shows it after {4:0} s ({5}); the float left alone in the slack: {6} (none)",
                s.x, s.z, cf != null ? cf.Water(s.x, s.z).magnitude : 0f, ctl.Stage.Water == null || ctl.Stage.Water.DriftOpen(s.x, s.z), t, shown ? "shown" : "not shown",
                aloneShown ? "shown" : "none"),
                shown && !aloneShown);
            FishingController.NoBites = false;
            yield return ToReady(ctl);
        }
    }
}
