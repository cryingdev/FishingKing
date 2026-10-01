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
    /// counted and let go (the fish swims off as after a missed bite, the bait stays on, the rig stays out), and the float
    /// is cast again when the water has carried it 8 m off or holds it against an edge (the flood takes it to the left edge
    /// / the tetrapods in seconds: it moves less than half its free drift over a second), as a player would. Bites per
    /// minute of soak (the time the rig lies out); [TIDE] lines.
    /// On the sea it is measured twice, with the rule before the reach (<see cref="FishingController.DebugOldTide"/>) and
    /// with the rule now: CHECK peak >= 1.3 x slack now. Elsewhere (no tide): CHECK the tide's factors are 1 at both phases
    /// (the same code runs at both: their two rates are logged as the run-to-run spread, ~x0.7–1.4 for the ocean's mixed
    /// stock of 7).
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
            public int bites, approaches, rolls, recasts, held;
            public float soak, inReach, free, real;
            public float PerMin => soak > 0f ? bites * 60f / soak : 0f;
        }

        IEnumerator TideBitesTest()
        {
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
            PointerInput.SimActive = false;
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
            var runs = new Dictionary<string, TideRun>();
            bool broken = false;
            foreach (bool old in sea ? new[] { true, false } : new[] { false })
            {
                foreach (var (name, phase) in new[] { ("slack", 0.5f), ("peak", 0.25f) })
                {
                    FishingController.DebugOldTide = old;
                    GameClock.TidePhase = phase;
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
                    var spot = new Vector3(ctl.Angler.X, 0f, 14f);
                    yield return ToReady(ctl);
                    ctl.DebugPlaceRig(spot);
                    if (sea && !hintWas && !hintSet) hintSet = Game.Data.tideHint;
                    var r = new TideRun();
                    float real0 = Time.realtimeSinceStartup, game = 0f;
                    int a0 = ctl.Approaches, r0 = ctl.ApproachRolls;
                    // the float held by the edge of the view / the tetrapods (the flood carries it there in seconds): it moves
                    // less than half its free drift over a second
                    var lastAt = tk.Surface;
                    float winT = 0f, winMoved = 0f, winFree = 0f;
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
                            bool held = false;
                            if (winT >= 1f)
                            {
                                held = winFree >= 0.1f && winMoved < 0.5f * winFree;
                                winT = winMoved = winFree = 0f;
                            }
                            if (held || new Vector2(tk.Surface.x - spot.x, tk.Surface.z - spot.z).magnitude >= 8f)
                            {
                                // the water carried it off or pinned it: wound in and laid at the spot again, as a player would
                                r.recasts++;
                                if (held) r.held++;
                                yield return ToReady(ctl);
                                ctl.DebugPlaceRig(spot);
                                lastAt = tk.Surface;
                                winT = winMoved = winFree = 0f;
                                continue;
                            }
                            float dt = Time.deltaTime;
                            r.soak += dt;
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
                            yield return ToReady(ctl);
                            ctl.DebugPlaceRig(spot);
                            lastAt = tk.Surface;
                            winT = winMoved = winFree = 0f;
                            continue;
                        }
                        yield return null;
                    }
                    if (broken) break;
                    r.approaches = ctl.Approaches - a0;
                    r.rolls = ctl.ApproachRolls - r0;
                    r.real = Time.realtimeSinceStartup - real0;
                    string key = (old ? "before " : "now ") + name;
                    runs[key] = r;
                    var tide = GameClock.Tide;
                    Log(string.Format(CIc, "[TIDE] {0} {1}: tide s {2:0.00}, bite x{3:0.00}, reach x{4:0.00} ({5:0.00} m): {6} bites in {7:0} s of soak = {8:0.00}/min; approaches {9} of {10} rolls; fish in reach while none comes {11:0.00}; recasts {12} ({13} held at an edge); {14:0} game s in {15:0} real s",
                        stage, key, tide.S, ctl.TideMult(), ctl.TideReach(), ctl.SenseRange(), r.bites, r.soak, r.PerMin, r.approaches, r.rolls,
                        r.free > 0f ? r.inReach / r.free : 0f, r.recasts, r.held, game, r.real));
                    yield return ToReady(ctl);
                }
                if (broken) break;
            }
            FishingController.DebugOldTide = false;
            if (broken || ctl == null) TCheck($"{stage} scene for every run", false);
            else
            {
                float Ratio(TideRun p, TideRun s) => p.PerMin / Mathf.Max(1e-3f, s.PerMin);
                // the tide's factors at the two phases, read off the controller
                GameClock.TidePhase = 0.5f;
                float mS = ctl.TideMult(), rS = ctl.TideReach();
                GameClock.TidePhase = 0.25f;
                float mP = ctl.TideMult(), rP = ctl.TideReach();
                var ns = runs["now slack"];
                var np = runs["now peak"];
                float rn = Ratio(np, ns);
                if (sea)
                {
                    var bs = runs["before slack"];
                    var bp = runs["before peak"];
                    float rb = Ratio(bp, bs);
                    TCheck(string.Format(CIc, "sea factors: bite x{0:0.00} / x{1:0.00}, reach x{2:0.00} / x{3:0.00} at slack / peak (0.60 / 1.35, 0.85 / 1.30)", mS, mP, rS, rP),
                        Mathf.Abs(mS - 0.6f) < 0.01f && Mathf.Abs(mP - 1.35f) < 0.01f && Mathf.Abs(rS - 0.85f) < 0.01f && Mathf.Abs(rP - 1.3f) < 0.01f);
                    TCheck(string.Format(CIc, "sea bites/min: peak {0:0.00} / slack {1:0.00} = x{2:0.00} (>= 1.30; {3} / {4} bites); before the reach x{5:0.00} ({6:0.00} / {7:0.00}); slack now x{8:0.00} of before, peak x{9:0.00}",
                        np.PerMin, ns.PerMin, rn, np.bites, ns.bites, rb, bp.PerMin, bs.PerMin, ns.PerMin / Mathf.Max(1e-3f, bs.PerMin), np.PerMin / Mathf.Max(1e-3f, bp.PerMin)),
                        rn >= 1.3f && ns.bites >= 20);
                    if (!hintWas)
                        TCheck("the first sea cast shows the tide hint (물때: 들물·날물엔 입질이 활발하고, 만조·간조엔 뜸해요; tideHint saved)", hintSet);
                    else Log("[TIDE] the tide hint was shown before (not a fresh save): not checked");
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
    }
}
