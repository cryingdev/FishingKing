using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto lure (Docs/lures_legend_spec.md 4.5): the lure action test. With -fklure &lt;id&gt; it tests that lure, with
    /// -fklure all one lure of each action (spinner, minnow, popper, soft worm, metal jig) in turn. Per lure: cast, then
    /// 20 s of its ideal input (a steady lure's reel band midpoint; a cycle lure's flick count, gap and rest midpoints, or
    /// the frog's short wind runs), then 10 s of the opposite verb (flicks instead of winding, fast winding instead of
    /// flicks). Logs "[LURE] t q strike depth onBottom" every 0.5 s and each scored cycle of a 톡 lure (count, gaps, rest),
    /// then checks "[AUTO] CHECK q_good&gt;=0.7" and "q_bad&lt;=0.3". No fish engage while Q is measured (a bite stopped the
    /// scripted cycle and spoiled its score at random); with -fklurebites they do: bites are left to run out (no hook set),
    /// a fight that starts anyway is fought out. The 톡s are short DOWNWARD pulls (<see cref="LureFlick"/>); first <see cref="LureDirectionCheck"/>
    /// checks with the minnow that an upward swipe is no 톡 and a downward one is (and jerks the rod). With -fkshots it
    /// saves two shots of each lure moving in the water (and the rod at rest / at a jerk's peak); -fklureui adds the
    /// shop's lure tab and the bait picker.
    /// </summary>
    public partial class AutoPilot
    {
        static readonly string[] LureSet = { "bait_spinner", "bait_minnow", "bait_popper", "bait_softworm", "bait_jig" };

        int lureFails;

        IEnumerator LureTest()
        {
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("no FishingController (use -fkscene Fishing)");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            var argv = Environment.GetCommandLineArgs();
            bool shotsOn = Array.IndexOf(argv, "-fkshots") >= 0;
            string which = Arg("-fklure");
            var lures = which == "all"
                ? LureSet.Select(id => GameDatabase.GetItem<BaitDef>(id)).ToList()
                : new List<BaitDef> { Game.I.Bait };
            PointerInput.SimDpi = 0f;
            if (shotsOn && Array.IndexOf(argv, "-fklureui") >= 0) yield return LureUiShots(ctl);
            if (ctl.Stage.L.IsIce) yield return LureIceDepth(ctl, shotsOn);
            yield return LureDirectionCheck(ctl, shotsOn);
            Log($"lure test on {ctl.Stage.Def.id}: {string.Join(", ", lures.Where(b => b != null).Select(b => b.id))} (rod {Game.I.Rod.id}, reel {Game.I.Reel.id} {Game.I.Reel.retrieve:0.00} m/rev)");
            foreach (var b in lures)
            {
                if (b == null || !b.isLure)
                {
                    Log($"lure test: {(b == null ? "unknown id" : b.id + " is no lure")} (use -fklure <lure id> or all)");
                    continue;
                }
                yield return LureRun(ctl, b, shotsOn);
            }
            Log($"lure test done: {lureFails} failed");
            PointerInput.SimActive = false;
            Application.Quit();
        }

        IEnumerator LureRun(FishingController ctl, BaitDef b, bool shotsOn)
        {
            yield return BackToReady(ctl);
            if (ctl.Stage.L.IsIce && !LureInfo.IceOk(b))
            {
                Log($"lure {b.id}: {LureInfo.IceBanned} (skipped)");
                yield break;
            }
            if (!Game.I.Owns(b.id)) Game.Data.ownedItems.Add(b.id);
            ctl.EquipBait(b);
            yield return null;
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "lure {0} ({1}, work {2}, {3}): band {4:0.0}-{5:0.0} rev/s, flicks {6}-{7}, gap {8:0.00}-{9:0.00} s, rest {10:0.0}-{11:0.0} s",
                b.id, b.action, b.work, b.buoyancy, b.reelBand.x, b.reelBand.y, b.flicks.x, b.flicks.y, b.gap.x, b.gap.y, b.rest.x, b.rest.y));
            int f0 = ctl.LureFollows, r0 = ctl.LureStrikeRolls, b0 = ctl.LureBites, t0 = ctl.LureTurned;
            // Q is scored on the scripted input alone: a fish that bites (or is hooked) stops the script mid-cycle (hands off
            // through the bite, a recast after a fight resets Q), which spoiled the next cycle's score at random. No fish
            // engage while Q is measured, unless -fklurebites (the old behaviour: bites are left to run out).
            bool bitesOn = Array.IndexOf(Environment.GetCommandLineArgs(), "-fklurebites") >= 0;
            bool noBitesWas = FishingController.NoBites;
            FishingController.NoBites = !bitesOn;
            var good = new List<float>();
            yield return LurePlay(ctl, b, true, 20f, good, shotsOn);
            int f1 = ctl.LureFollows, r1 = ctl.LureStrikeRolls, b1 = ctl.LureBites, t1 = ctl.LureTurned;
            var bad = new List<float>();
            yield return LurePlay(ctl, b, false, 10f, bad, false);
            FishingController.NoBites = noBitesWas;
            PointerInput.SimDown = false;
            float qGood = good.Count > 0 ? good.Average() : -1f;
            float qBad = bad.Count > 0 ? bad[bad.Count - 1] : 1f;
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[LURE] {0} summary: good {1} samples q {2:0.00} (follows {3}, strike rolls {4}, bites {5}, turned away {6}) | bad q {7:0.00} (follows {8}, strike rolls {9}, bites {10}, turned away {11}), cycles scored {12}",
                b.id, good.Count, qGood, f1 - f0, r1 - r0, b1 - b0, t1 - t0, qBad, ctl.LureFollows - f1, ctl.LureStrikeRolls - r1, ctl.LureBites - b1, ctl.LureTurned - t1,
                ctl.Rhythm.Cycles));
            bool okG = qGood >= 0.7f, okB = qBad <= 0.3f;
            if (!okG) lureFails++;
            if (!okB) lureFails++;
            Log($"CHECK {(okG ? "PASS" : "FAIL")} {b.id} q_good>=0.7 ({qGood:0.00})");
            Log($"CHECK {(okB ? "PASS" : "FAIL")} {b.id} q_bad<=0.3 ({qBad:0.00})");
            yield return BackToReady(ctl);
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>The waiting-time clock of one play phase, kept by <see cref="LureLogger"/>.</summary>
        class LureClock
        {
            public float t, castT;
            public bool done;
        }

        /// <summary>
        /// Plays the lure's ideal (good) or opposite (bad) input for <paramref name="dur"/> seconds of waiting time (casts,
        /// bites and fights do not count), recasting when the rig comes home. Meanwhile <see cref="LureLogger"/> logs every
        /// 0.5 s and samples Q into <paramref name="qs"/>.
        /// </summary>
        IEnumerator LurePlay(FishingController ctl, BaitDef b, bool good, float dur, List<float> qs, bool shotsOn)
        {
            var clock = new LureClock();
            StartCoroutine(LureLogger(ctl, b, good, dur, qs, clock));
            float windSign = CircleGesture.Reversed ? -1f : 1f, waitBottom = 0f;
            var centre = Scr(0.72f, 0.4f);
            float radius = Screen.height * 0.12f;
            bool needBottom = b.needBottom || b.action == LureAction.Vertical;
            bool bottomed = !needBottom;
            int shotsLeft = shotsOn ? 2 : 0;
            float nextShot = 5f;
            string ShotName() => $"lure_{b.id.Replace("bait_", "")}_{ctl.Stage.Def.id}_{2 - shotsLeft}";
            while (clock.t < dur)
            {
                if (ctl.State == FishingController.S.Ready)
                {
                    PointerInput.SimDown = false;
                    if (Game.I.Bait != b)
                    {
                        // the line broke in a fight and took the lure: the test goes on with a new one
                        if (!Game.I.Owns(b.id)) Game.Data.ownedItems.Add(b.id);
                        ctl.EquipBait(b);
                        Log($"[LURE] {b.id} lost in a fight: re-equipped");
                    }
                    yield return null;
                    yield return CastLure(ctl);
                    bottomed = !needBottom;
                    waitBottom = 0f;
                    continue;
                }
                if (ctl.State == FishingController.S.Fighting || ctl.State == FishingController.S.Landing || ctl.State == FishingController.S.Result)
                {
                    PointerInput.SimDown = false;
                    yield return LureFightOut(ctl);
                    continue;
                }
                if (ctl.State != FishingController.S.Waiting)
                {
                    // casting or a bite running out: hands off (a press or a circle would set the hook)
                    PointerInput.SimDown = false;
                    yield return null;
                    continue;
                }
                var tk = ctl.Tackle;
                if (!bottomed)
                {
                    // bottom and vertical lures go down to the bottom first
                    PointerInput.SimDown = false;
                    waitBottom += Time.deltaTime;
                    if (tk.OnBottom || waitBottom > 10f) bottomed = true;
                    yield return null;
                    continue;
                }
                bool shotDue = shotsLeft > 0 && clock.t >= nextShot;
                bool windVerb = b.IsSteady ? good : b.work == Work.Wind ? good : !good;
                if (windVerb)
                {
                    if (b.IsSteady || !good)
                    {
                        // wind continuously: a steady lure at its band's midpoint; a flick lure (bad) fast
                        float rps = good ? (b.reelBand.x + b.reelBand.y) * 0.5f : 1.5f;
                        yield return CircleRun(rps, 0.25f, windSign, centre, radius);
                    }
                    else
                    {
                        // the frog: a short wind run in its band, then a rest
                        yield return CircleRun((b.reelBand.x + b.reelBand.y) * 0.5f, Mathf.Min(1f, b.runMax * 0.5f), windSign, centre, radius);
                        if (shotDue && ctl.State == FishingController.S.Waiting)
                        {
                            shotsLeft--;
                            nextShot = clock.t + 6f;
                            yield return LureShot(ctl, ShotName());
                            shotDue = false;
                        }
                        PointerInput.SimDown = false;
                        yield return WaitWaiting(ctl, (b.rest.x + b.rest.y) * 0.5f);
                    }
                }
                else if (good)
                {
                    // a cycle: the flick burst (count and gaps at their midpoints), then the rest
                    int n = Mathf.Max(1, Mathf.RoundToInt((b.flicks.x + b.flicks.y) * 0.5f));
                    float gap = b.gap.y > 0f ? (b.gap.x + b.gap.y) * 0.5f : 0.4f;
                    for (int i = 0; i < n && ctl.State == FishingController.S.Waiting; i++)
                    {
                        yield return LureFlick();
                        if (i == 0 && shotDue && ctl.State == FishingController.S.Waiting)
                        {
                            // the lure mid-hop / mid-dart
                            shotsLeft--;
                            nextShot = clock.t + 6f;
                            yield return LureShot(ctl, ShotName());
                            shotDue = false;
                        }
                        if (i < n - 1) yield return WaitWaiting(ctl, Mathf.Max(0f, gap - 0.14f));
                    }
                    yield return WaitWaiting(ctl, (b.rest.x + b.rest.y) * 0.5f);
                }
                else
                {
                    // the opposite verb for a winding lure: flicks, no winding
                    yield return LureFlick();
                    yield return WaitWaiting(ctl, 0.35f);
                }
                if (shotDue && ctl.State == FishingController.S.Waiting)
                {
                    shotsLeft--;
                    nextShot = clock.t + 6f;
                    yield return LureShot(ctl, ShotName());
                }
            }
            clock.done = true;
            PointerInput.SimDown = false;
            yield return null;
        }

        /// <summary>
        /// Keeps the phase's waiting-time clock and logs "[LURE] t q strike depth onBottom" every 0.5 s of it. Samples Q: in
        /// the good phase over its second half once the lure has settled after a cast (5 s for a steady lure, two scored
        /// cycles for the others); in the bad phase every 0.5 s.
        /// </summary>
        IEnumerator LureLogger(FishingController ctl, BaitDef b, bool good, float dur, List<float> qs, LureClock clock)
        {
            float logT = 0f;
            var prev = ctl.State;
            var rh = ctl.Rhythm;
            string tag = good ? "good" : "bad";
            // the 톡s as the rhythm saw them (waiting time), to log each scored cycle's flick count, gaps and rest
            var flicks = new List<float>();
            var strengths = new List<float>();
            float bottomT = 0f, bottomRest = 0f;
            int cyclesSeen = rh.Cycles;
            while (!clock.done)
            {
                float dt = Time.deltaTime;
                if (ctl.State == FishingController.S.Waiting)
                {
                    if (prev != FishingController.S.Waiting && prev != FishingController.S.Biting) clock.castT = 0f; // a new cast
                    clock.t += dt;
                    clock.castT += dt;
                    bool flickNow = ctl.LureIn.FlickNow;
                    if (flickNow)
                    {
                        flicks.Add(clock.t);
                        strengths.Add(ctl.LureIn.FlickStrength);
                        bottomRest = bottomT;
                        bottomT = 0f;
                    }
                    else if (ctl.Tackle.OnBottom) bottomT += dt;
                    if (rh.Cycles < cyclesSeen)
                    {
                        // (a recast reset the rhythm)
                        cyclesSeen = rh.Cycles;
                        flicks.Clear();
                        strengths.Clear();
                        if (flickNow)
                        {
                            flicks.Add(clock.t);
                            strengths.Add(ctl.LureIn.FlickStrength);
                        }
                    }
                    else if (rh.Cycles > cyclesSeen)
                    {
                        // (scored at the next work's first 톡, this frame's: that 톡 starts the next cycle)
                        var mine = flickNow && flicks.Count > 0 ? flicks.GetRange(0, flicks.Count - 1) : new List<float>(flicks);
                        var power = flickNow && strengths.Count > 0 ? strengths.GetRange(0, strengths.Count - 1) : new List<float>(strengths);
                        float onBottom = flickNow ? bottomRest : bottomT;
                        var gaps = new List<string>();
                        for (int i = 1; i < mine.Count; i++) gaps.Add((mine[i] - mine[i - 1]).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                        float rest = mine.Count > 0 ? clock.t - mine[mine.Count - 1] : -1f;
                        if (!b.IsSteady && b.work == Work.Flick)
                            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "[LURE] {0} {1} cycle {2} score {3:0.00}: {4} 톡 (want {5}-{6}), gaps [{7}] s (want {8:0.00}-{9:0.00}), rest {10:0.00} s (want {11:0.0}-{12:0.0}), on the bottom {13:0.00} s of it, strength max {14:0.00} (max {15:0.00}), q {16:0.00}",
                                b.id, tag, rh.Cycles, rh.LastScore, mine.Count, b.flicks.x, b.flicks.y, string.Join(" ", gaps), b.gap.x, b.gap.y, rest, b.rest.x, b.rest.y,
                                onBottom, power.Count > 0 ? power.Max() : 0f, b.maxStrength, rh.Q));
                        cyclesSeen = rh.Cycles;
                        flicks.Clear();
                        strengths.Clear();
                        if (flickNow)
                        {
                            flicks.Add(clock.t);
                            strengths.Add(ctl.LureIn.FlickStrength);
                        }
                    }
                    logT -= dt;
                    if (logT <= 0f)
                    {
                        logT = 0.5f;
                        var tk = ctl.Tackle;
                        Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "[LURE] {0} {1} t={2:0.0} q={3:0.00} strike={4} depth={5:0.00} onBottom={6} falling={7} speed={8:0.00} phase={9} cycles={10} last={11:0.00}",
                            b.id, tag, clock.t, rh.Q, rh.StrikeOpen ? 1 : 0, tk.Depth, tk.OnBottom ? 1 : 0, tk.Falling ? 1 : 0, ctl.LureIn.Speed, rh.Phase, rh.Cycles, rh.LastScore));
                        bool settled = b.IsSteady ? clock.castT > 5f : rh.Cycles >= 2;
                        if (!good || (clock.t > dur * 0.5f && settled)) qs.Add(rh.Q);
                    }
                }
                prev = ctl.State;
                yield return null;
            }
        }
        /// <summary>A shot of the lure in the water; logs where the lure is on screen (x, y from the top) for zoomed crops.</summary>
        IEnumerator LureShot(FishingController ctl, string name)
        {
            var tk = ctl.Tackle;
            var P = ctl.Stage.P;
            if (PixelView.Current != null)
            {
                var hp = tk.HookPos;
                var s = PixelView.Current.WorldToScreen(P.To2D(tk.Depth <= 0.12f ? new Vector3(hp.x, 0f, hp.z) : P.Apparent(hp)));
                Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "lureshot {0:00}_{1}.png {2:0} {3:0} depth {4:0.00} z {5:0.0}",
                    shotIndex, name, s.x, Screen.height - s.y, tk.Depth, hp.z));
            }
            yield return Shot(name);
        }

        float circleAng;
        /// <summary>Circles for <paramref name="time"/> seconds at <paramref name="rps"/> (winding in), pointer down.</summary>
        IEnumerator CircleRun(float rps, float time, float windSign, Vector2 centre, float radius)
        {
            PointerInput.SimActive = true;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                circleAng -= windSign * Time.deltaTime * rps * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * radius;
                yield return null;
            }
        }

        /// <summary>
        /// A 톡: a short DOWNWARD pull from a fresh press (0.10 H at 1.0 H/s: strength ~0.4), let go at its end.
        /// <paramref name="up"/>: the old upward swipe instead (the same stroke mirrored), which must not count.
        /// </summary>
        IEnumerator LureFlick(float speed = 1.0f, float travel = 0.10f, bool up = false)
        {
            if (PointerInput.SimDown)
            {
                PointerInput.SimDown = false;
                yield return null;
            }
            PointerInput.SimActive = true;
            PointerInput.SimPos = Scr(0.5f, up ? 0.42f : 0.58f);
            PointerInput.SimDown = true;
            yield return null;
            yield return Flick(speed, up ? 0f : 180f, travel);
        }

        /// <summary>
        /// The 톡's direction check (with the minnow, before the lure runs): in Waiting an upward swipe must not count
        /// (LureInput rejects it as "up"), a downward one must, and it must jerk the rod up. With -fkshots: the rod at rest
        /// and at the jerk's peak (lure_jerk_rest / lure_jerk_peak), logging the rod hand / tip on screen for crops.
        /// </summary>
        IEnumerator LureDirectionCheck(FishingController ctl, bool shotsOn)
        {
            var b = GameDatabase.GetItem<BaitDef>("bait_minnow");
            yield return BackToReady(ctl);
            if (b == null || (ctl.Stage.L.IsIce && !LureInfo.IceOk(b))) yield break;
            if (!Game.I.Owns(b.id)) Game.Data.ownedItems.Add(b.id);
            ctl.EquipBait(b);
            yield return null;
            for (int tries = 0; tries < 3 && ctl.State != FishingController.S.Waiting; tries++)
            {
                yield return CastLure(ctl);
                for (float w = 0f; w < 6f && ctl.State != FishingController.S.Waiting && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
                    yield return null;
            }
            if (ctl.State != FishingController.S.Waiting)
            {
                lureFails++;
                Log($"CHECK FAIL 톡 direction: no Waiting to test in ({ctl.State})");
                yield break;
            }
            yield return new WaitForSeconds(1.2f);
            var li = ctl.LureIn;
            // the old 톡: a quick upward swipe
            int n0 = li.Flicks, up0 = li.UpRejects;
            yield return LureFlick(1.0f, 0.10f, up: true);
            yield return null;
            bool upOk = li.Flicks == n0 && li.UpRejects == up0 + 1 && li.LastReject == "up";
            Log($"[LURE] upward swipe in {ctl.State}: flicks {n0} -> {li.Flicks}, reject '{li.LastReject}', up rejects {li.UpRejects}");
            if (!upOk) lureFails++;
            Log($"CHECK {(upOk ? "PASS" : "FAIL")} upward swipe is no 톡 (reject '{li.LastReject}')");
            yield return WaitWaiting(ctl, 0.3f);
            if (shotsOn && ctl.State == FishingController.S.Waiting) yield return Shot("lure_up_reminder");
            // (the reminder gives way to the lure's hint again)
            yield return WaitWaiting(ctl, 2.2f);
            if (shotsOn && ctl.State == FishingController.S.Waiting)
            {
                LogRod(ctl, "lure_jerk_rest");
                yield return Shot("lure_jerk_rest");
            }
            // the new 톡: a quick downward pull, and the rod jerks up
            int n1 = li.Flicks;
            float jerkMax = 0f;
            yield return LureFlick(1.0f, 0.10f);
            bool shot = false;
            for (float w = 0f; w < 0.5f && ctl.State == FishingController.S.Waiting; w += Time.deltaTime)
            {
                float j = ctl.RodJerkNow;
                jerkMax = Mathf.Max(jerkMax, j);
                if (shotsOn && !shot && j > 0f && j >= jerkMax - 1e-4f && w >= 0.06f)
                {
                    shot = true;
                    LogRod(ctl, "lure_jerk_peak");
                    yield return Shot("lure_jerk_peak");
                    continue;
                }
                yield return null;
            }
            bool downOk = li.Flicks == n1 + 1 && jerkMax >= 0.3f;
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[LURE] downward pull: flicks {0} -> {1}, strength {2:0.00}, rod jerk peak {3:0.00}",
                n1, li.Flicks, li.FlickStrength, jerkMax));
            if (!downOk) lureFails++;
            Log($"CHECK {(downOk ? "PASS" : "FAIL")} downward pull is a 톡 and jerks the rod (peak {jerkMax:0.00})");
            yield return BackToReady(ctl);
        }

        /// <summary>
        /// The ice: a lure stops at the depth the drag let it down to. The jig let down part way rests there (not on the
        /// bottom) and counts as down (OnBottom: its rhythm works there); a 톡 lifts it and it falls back to that depth; a full
        /// drag lays it on the bottom, inside the sturgeon's bottom band. With -fkshots: lure_ice_mid / lure_ice_bottom.
        /// </summary>
        IEnumerator LureIceDepth(FishingController ctl, bool shotsOn)
        {
            var b = GameDatabase.GetItem<BaitDef>("bait_jig");
            yield return BackToReady(ctl);
            if (b == null) yield break;
            if (!Game.I.Owns(b.id)) Game.Data.ownedItems.Add(b.id);
            ctl.EquipBait(b);
            yield return null;
            var tk = ctl.Tackle;
            // (let down, then still until the depth holds for 0.4 s)
            IEnumerator Drop(float pull)
            {
                yield return WindUp(pull);
                PointerInput.SimDown = false;
                for (float w = 0f; w < 6f && ctl.State != FishingController.S.Waiting; w += Time.deltaTime) yield return null;
                yield return Settle();
            }
            IEnumerator Settle()
            {
                float last = -1f, still = 0f;
                for (float w = 0f; w < 8f && ctl.State == FishingController.S.Waiting && still < 0.4f; w += Time.deltaTime)
                {
                    still = Mathf.Abs(tk.Depth - last) < 1e-4f && !tk.Falling && !tk.Hopping ? still + Time.deltaTime : 0f;
                    last = tk.Depth;
                    yield return null;
                }
            }
            yield return Drop(0.12f);
            float aim = ctl.AimDepth, rest = tk.Depth, bottom = tk.Bottom;
            bool onFloor = tk.OnBottom;
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[LURE] ice: let down to {0:0.00} m, rests at {1:0.00} m (bottom {2:0.00} m), on its floor {3}, {4}",
                aim, rest, bottom, onFloor, ctl.State));
            if (shotsOn) yield return LureShot(ctl, "lure_ice_mid");
            bool ok = ctl.State == FishingController.S.Waiting && Mathf.Abs(rest - aim) <= 0.05f && rest < bottom - 1f && onFloor;
            if (!ok) lureFails++;
            Log($"CHECK {(ok ? "PASS" : "FAIL")} ice: the jig rests at the drag's depth {aim:0.00} m ({rest:0.00}), not on the bottom ({bottom:0.00})");
            // a 톡 lifts it; it falls back to the drag's depth
            float top = rest;
            yield return LureFlick(1.0f, 0.10f);
            for (float w = 0f; w < 0.5f; w += Time.deltaTime)
            {
                top = Mathf.Min(top, tk.Depth);
                yield return null;
            }
            yield return Settle();
            ok = ctl.State == FishingController.S.Waiting && top < rest - 0.3f && Mathf.Abs(tk.Depth - rest) <= 0.05f;
            if (!ok) lureFails++;
            Log($"CHECK {(ok ? "PASS" : "FAIL")} ice: a 톡 lifts the jig to {top:0.00} m and it falls back to {tk.Depth:0.00} m (the drag's {rest:0.00})");
            // a full drag: on the bottom, in the sturgeon's band
            yield return BackToReady(ctl);
            yield return new WaitForSeconds(0.3f);
            yield return Drop(0.32f);
            float aim2 = ctl.AimDepth, d2 = tk.Depth, b2 = tk.Bottom;
            var enc = GameDatabase.GetFish("sturgeon")?.encounter;
            bool band = enc == null || (d2 >= enc.depthMin && d2 >= b2 - enc.bottomBand);
            if (shotsOn) yield return LureShot(ctl, "lure_ice_bottom");
            ok = ctl.State == FishingController.S.Waiting && d2 >= b2 - 0.02f && band;
            if (!ok) lureFails++;
            Log($"CHECK {(ok ? "PASS" : "FAIL")} ice: a full drag ({aim2:0.00} m) lays it on the bottom ({d2:0.00} of {b2:0.00} m), in the sturgeon's band {band}" +
                (enc != null ? $" (>= {enc.depthMin:0.0} m, within {enc.bottomBand:0.0} m of the bottom)" : ""));
            yield return BackToReady(ctl);
        }

        /// <summary>Logs where the rod hand and tip are on screen (x, y from the top) for a crop of the shot that follows.</summary>
        void LogRod(FishingController ctl, string name)
        {
            if (PixelView.Current == null) return;
            var P = ctl.Stage.P;
            var a = ctl.Angler;
            var h = PixelView.Current.WorldToScreen(P.To2D(a.Hand));
            var t = PixelView.Current.WorldToScreen(P.To2D(a.RodTip));
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "rodshot {0:00}_{1}.png hand {2:0} {3:0} tip {4:0} {5:0} jerk {6:0.00} 3d {7}",
                shotIndex, name, h.x, Screen.height - h.y, t.x, Screen.height - t.y, a.RodJerk01, a.Uses3D));
        }

        /// <summary>Waits this long, or until the controller leaves Waiting.</summary>
        IEnumerator WaitWaiting(FishingController ctl, float s)
        {
            for (float w = 0f; w < s && ctl.State == FishingController.S.Waiting; w += Time.deltaTime) yield return null;
        }

        IEnumerator CastLure(FishingController ctl)
        {
            var (speed, angle) = FlickArg();
            for (int tries = 0; tries < 4 && ctl.State == FishingController.S.Ready; tries++)
            {
                yield return Cast(ctl, speed, angle ?? 0f);
                for (float w = 0f; w < 6f && ctl.State != FishingController.S.Waiting && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
                    yield return null;
            }
            Log($"[LURE] cast -> {ctl.State} at z {ctl.Tackle.Surface.z:0.0} m, bottom {ctl.Tackle.Bottom:0.0} m");
        }

        /// <summary>A fight that started anyway (a circle as a bite came): reel it in, easing off at high tension; sell the catch.</summary>
        IEnumerator LureFightOut(FishingController ctl)
        {
            var c = Scr(0.72f, 0.4f);
            float r = Screen.height * 0.13f, ang = 0f, t = 0f;
            float windSign = CircleGesture.Reversed ? -1f : 1f;
            bool winding = true;
            string who = ctl.Hooked != null ? ctl.Hooked.Sp.id : "?";
            PointerInput.SimActive = true;
            while (ctl.State == FishingController.S.Fighting && t < 90f)
            {
                t += Time.deltaTime;
                var f = ctl.Fight;
                if (f != null)
                {
                    if (winding && (f.TensionRatio > 0.8f || f.Jumping)) winding = false;
                    else if (!winding && f.TensionRatio < 0.55f && !f.Jumping) winding = true;
                }
                if (winding) ang -= windSign * Time.deltaTime * 2.3f * Mathf.PI * 2f;
                else if (f != null && f.TensionRatio > 0.95f) ang += windSign * Time.deltaTime * 1.2f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                yield return null;
            }
            PointerInput.SimDown = false;
            while (ctl.State == FishingController.S.Landing) yield return null;
            if (ctl.State == FishingController.S.Result)
            {
                for (float w = 0; w < 3f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(0.3f);
                Click("판매");
                yield return new WaitForSeconds(0.6f);
            }
            Log($"[LURE] fight with {who} ended -> {ctl.State}");
        }

        /// <summary>The shop's lure tab (top and scrolled to the end) and the bait picker (natural baits, then the lures).</summary>
        IEnumerator LureUiShots(FishingController ctl)
        {
            foreach (var b in GameDatabase.Baits.Where(x => x.isLure))
                if (!Game.I.Owns(b.id)) Game.Data.ownedItems.Add(b.id);
            // leave a couple unowned so the shop shows prices too
            Game.Data.ownedItems.Remove("bait_kona");
            Game.Data.ownedItems.Remove("bait_crank");
            var hud = FindAnyObjectByType<FishingHUD>();
            ShopUI.BaitLures = true;
            ShopUI.Open(hud.Canvas.transform, ItemKind.Bait);
            yield return new WaitForSeconds(0.7f);
            yield return Shot("shop_lures");
            var shop = GameObject.Find("Shop");
            var scroll = shop != null ? shop.GetComponentInChildren<ScrollRect>() : null;
            if (scroll != null)
            {
                scroll.verticalNormalizedPosition = 0f;
                yield return new WaitForSeconds(0.3f);
                yield return Shot("shop_lures_end");
            }
            if (shop != null) Destroy(shop.transform.parent.gameObject);
            ShopUI.BaitLures = false;
            yield return new WaitForSeconds(0.3f);
            var bait = FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.transform.parent != null && x.transform.parent.name == "Tackle");
            if (bait != null)
            {
                bait.onClick.Invoke();
                yield return new WaitForSeconds(0.7f);
                yield return Shot("bait_picker");
                var d = GameObject.Find("[Dialogs]");
                var ps = d != null ? d.GetComponentInChildren<ScrollRect>() : null;
                if (ps != null)
                {
                    ps.verticalNormalizedPosition = 0f;
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot("bait_picker_lures");
                }
                CloseDialogs();
                yield return new WaitForSeconds(0.4f);
            }
        }
    }
}
