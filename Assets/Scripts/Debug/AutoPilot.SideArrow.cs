using System.Collections;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto steer -fksteer arrow: the fight's side-pressure arrow (<see cref="SideArrow"/>) with a float rig and a lure
    /// (the same fish, spot and seed as the side-pressure fights): not pushed (the prompt), pushed the right way (bright),
    /// then on a later run pushed the wrong way (shaking), a shot of each with the arrow's and its anchor's screen
    /// positions logged ("arrowshot" lines, y down) for the review crops; every frame it checks the arrow shows only while
    /// side pressure counts (not in a rest or a jump), points against the run, shows the state the side pressure is in,
    /// and floats a few pixels over its anchor (the float riding the line, else the line's entry). With -fkfloatshots the
    /// float rig's float moments are shot too (<see cref="FloatMoment"/>), and where it lies after the fish is let go.
    /// </summary>
    public partial class AutoPilot
    {
        /// <summary>
        /// Every fight frame: side pressure (the arrow, the strip, the model's side multipliers) only while the fish really
        /// sweeps sideways (<see cref="FishingController.Sweeping"/>; a cover run excepted). Counts the run frames, those with
        /// side pressure on / the arrow up, the frames the fish has sat at its run's target bearing for <see cref="Grace"/> s
        /// (the sweep's smoothing, the off hold and the arrow's fade: the most it may take to notice) and the arrow up in
        /// them, the arrow pointing the wrong way, flickers (off under 0.25 s mid-run, then on again), and the frames where the model's side pressure and the HUD / arrow's word
        /// disagree about the lean's dead zone.
        /// </summary>
        class SideWatch
        {
            public const float Grace = 0.7f;
            public int runFrames, activeFrames, arrowFrames, atTarget, settled, settledArrow, settledActive, wrongWay, deadMiss, flicker;
            float settledT, activeT, offT = -1f, lastYaw = float.NaN;
            bool wasActive;
            FishingHUD hud;

            public void Frame(FishingController ctl, float dt)
            {
                var f = ctl.Fight;
                var ar = ctl.PushArrow;
                if (f == null) return;
                bool running = ctl.FishRun != 0 && (f.State == FightModel.Phase.Run || f.State == FightModel.Phase.Burst) && !f.Exhausted && !f.Jumping && !ctl.Stage.L.IsIce;
                bool cover = f.CoverRun || f.CoverHold;
                // at the target bearing and not moving (not a target pushed on outwards with the fish tracking it)
                float yaw = ctl.FightYawNow, yawRate = float.IsNaN(lastYaw) || dt <= 0f ? 0f : Mathf.Abs(yaw - lastYaw) / dt;
                lastYaw = yaw;
                bool at = running && !cover && Mathf.Abs(yaw - ctl.FightYawGoal) < 0.005f && yawRate < 0.02f;
                settledT = at ? settledT + dt : 0f;
                activeT = ctl.SideActive ? activeT + dt : 0f;
                bool vis = ar != null && ar.Visible;
                // flicker: side pressure off for under 0.25 s in the middle of one run, then on again
                if (!running) offT = -1f;
                else if (ctl.SideActive)
                {
                    if (!wasActive && offT >= 0f && offT < 0.25f) flicker++;
                    offT = -1f;
                }
                else if (wasActive) offT = 0f;
                else if (offT >= 0f) offT += dt;
                wasActive = running && ctl.SideActive;
                if (running)
                {
                    runFrames++;
                    if (ctl.SideActive) activeFrames++;
                    if (vis) arrowFrames++;
                    if (at) atTarget++;
                }
                if (settledT >= Grace)
                {
                    settled++;
                    if (vis) settledArrow++;
                    if (ctl.SideActive) settledActive++;
                }
                if (vis && ctl.SideActive && activeT > 0.3f && ar.Side != -ctl.FishRun) wrongWay++;
                if (ctl.SideActive)
                {
                    // (and the strip's rod as drawn in the controller's Update: past the dead zone exactly when the word /
                    // model are; not ctl.Lean, read after the Angler's LateUpdate has moved the rod on)
                    if (hud == null) hud = UnityEngine.Object.FindAnyObjectByType<FishingHUD>();
                    bool model = f.SideGood > 0f || f.SideBad > 0f, word = Mathf.Abs(ctl.SideNow) > SideArrow.Deadband;
                    if (model != word || (hud != null && (Mathf.Abs(hud.StripLean) > SideArrow.Deadband) != word)) deadMiss++;
                }
            }

            public void Add(SideWatch o)
            {
                runFrames += o.runFrames; activeFrames += o.activeFrames; arrowFrames += o.arrowFrames; atTarget += o.atTarget;
                settled += o.settled; settledArrow += o.settledArrow; settledActive += o.settledActive; wrongWay += o.wrongWay; deadMiss += o.deadMiss; flicker += o.flicker;
            }

            static float Pct(int a, int b) => b > 0 ? 100f * a / b : 0f;

            public string Report() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "run frames {0}: at the target bearing {1} ({2:0}%); side pressure on {3} ({4:0}%, 100% before sweeping gated it), arrow up {5} ({6:0}%); sat at the target >= {7:0.0}s {8} frames: arrow up {9}, side on {10}; arrow the wrong way {11}; dead-zone mismatches {12}; flickers {13}",
                runFrames, atTarget, Pct(atTarget, runFrames), activeFrames, Pct(activeFrames, runFrames), arrowFrames, Pct(arrowFrames, runFrames),
                Grace, settled, settledArrow, settledActive, wrongWay, deadMiss, flicker);

            public bool Ok => runFrames > 0 && activeFrames > 0 && settledArrow == 0 && settledActive == 0 && wrongWay == 0 && deadMiss == 0 && flicker == 0;
        }

        IEnumerator SteerArrow(FishingController ctl)
        {
            bool ocean = ctl.Stage.Def.id == "ocean";
            string fishId = ocean ? "yellowtail" : "carp";
            float cm = ocean ? 90f : 70f;
            if (ocean) SteerGear("rod_biggame", "reel_baitcast", "line_pe3");
            else SteerGear("rod_glass", "reel_light", "line_nylon4");
            var sp = GameDatabase.GetFish(fishId);
            float homeX = Mathf.Clamp(0f, ctl.Angler.Range.x, ctl.Angler.Range.y);
            Log($"[ARROW] {ctl.Stage.Def.id}: {fishId} {cm:0}cm, rod {Game.I.Rod.id}, angler x {N(homeX)}");
            arrowLetGos = 0;
            foreach (var (rig, bait) in new[] { ("float", "bait_paste"), ("lure", "bait_minnow") })
                yield return ArrowFight(ctl, sp, cm, homeX, rig, bait);
            SCheck($"arrow {ctl.Stage.Def.id}: the let-go after a fish shakes off was checked ({arrowLetGos} of 2 rigs released before landing)", arrowLetGos > 0);
        }

        IEnumerator ArrowShot(FishingController ctl, string name)
        {
            yield return new WaitForEndOfFrame();
            var ar = ctl.PushArrow;
            var pv = PixelView.Current;
            if (pv != null)
            {
                var s = pv.WorldToScreen(ar.Pos);
                var a = pv.WorldToScreen(ar.AnchorPos2D);
                Log(string.Format(CI, "arrowshot {0:00}_{1}.png arrow {2:0} {3:0} anchor {4:0} {5:0} state {6} side {7:+0;-0} frame {8} alpha {9:0.00} scale {10:0.00} gap {11:0.0} kind {12} run {13:+0;-0;0} sideNow {14:+0.00;-0.00;0.00}",
                    shotIndex, name, s.x, Screen.height - s.y, a.x, Screen.height - a.y, ar.State, ar.Side, ar.Frame, ar.Alpha, ar.Scale, ar.GapNow, ar.AnchorKind, ctl.FishRun, ctl.SideNow));
            }
            string p = System.IO.Path.Combine(shots, $"{shotIndex++:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(p);
            Log("shot " + p);
            yield return null;
        }

        int arrowLetGos;

        IEnumerator ArrowFight(FishingController ctl, FishSpecies sp, float cm, float homeX, string rig, string bait)
        {
            string st = ctl.Stage.Def.id;
            yield return SteerCast(ctl, bait, homeX);
            if (ctl.State != FishingController.S.Waiting)
            {
                SCheck($"arrow {rig}: cast ({ctl.State})", false);
                yield break;
            }
            SCheck($"arrow {rig}: the rig is {(ctl.Tackle.UsesFloat ? "a float rig" : "a lure")} ({ctl.Tackle.Bait.id})", ctl.Tackle.UsesFloat == (rig == "float"));
            if (!ctl.DebugHook(sp, cm, 4242, new Vector3(homeX + 1f, -1.2f, 16f)))
            {
                SCheck($"arrow {rig}: hook", false);
                yield break;
            }
            var ar = ctl.PushArrow;
            var f = ctl.Fight;
            int runsEnded = 0;
            System.Action<int, float, bool> onRun = (s, d, tr) => runsEnded++;
            ctl.RunEnded += onRun;
            Caption($"화살표 · {(rig == "float" ? "찌" : "루어")}");
            var c = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.13f, ang = 0f, ws = CircleGesture.Reversed ? -1f : 1f;
            // 0 prompt shot, 1 push the right way (shot), 2 wait for a new run, 3 push the wrong way (shot), 4 done
            int phase = 0, mark = 0;
            float t = 0f, activeT = 0f, idleT = 0f, holdT = 0f;
            int frames = 0, visFrames = 0, showMiss = 0, hideMiss = 0, sideMiss = 0, stateMiss = 0, jumpFrames = 0, jumpShown = 0;
            int sidesSeen = 0, entryFrames = 0, mouthFrames = 0, floatFrames = 0;
            floatDone = 0;
            float gapMin = 999f, gapMax = -999f, scaleMin = 9f, scaleMax = 0f;
            bool shotPrompt = false, shotRight = false, shotWrong = false;
            var watch = new SideWatch();
            PointerInput.SimActive = true;
            // (after the shots it fights on a while, not pushing, to see a jump through: up to 28 s in all)
            while (ctl.State == FishingController.S.Fighting && t < 45f && (phase < 4 || (jumpFrames == 0 || f.Jumping) && t < 28f))
            {
                float dt = Time.deltaTime;
                t += dt;
                int run = ctl.FishRun;
                bool running = run != 0 || f.State == FightModel.Phase.Burst;
                if (f.TensionRatio > 0.95f) ang += ws * dt * 1.0f * Mathf.PI * 2f;
                else if (!running && !f.Jumping && f.TensionRatio < 0.8f) ang -= ws * dt * 1.6f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                int lean = phase == 1 ? -run : phase == 3 ? run : 0;
                PointerInput.SimLeft = lean < 0;
                PointerInput.SimRight = lean > 0;
                // (a coroutine resumes after the Updates and before the LateUpdates: at the end of the frame the arrow
                // shows this frame's side pressure)
                yield return new WaitForEndOfFrame();
                if (ctl.Fight == null) break;
                // (let the fish go before it can be landed: the shake-off check after the loop needs the fight still on)
                if (f.Line <= f.LandDist + 4f) break;
                // ---- the rules, every frame (after the fade in / out has had its time)
                bool active = ctl.State == FishingController.S.Fighting && ctl.SideActive;
                activeT = active ? activeT + Time.deltaTime : 0f;
                idleT = active ? 0f : idleT + Time.deltaTime;
                frames++;
                watch.Frame(ctl, Time.deltaTime);
                if (ar.Visible) visFrames++;
                if (activeT > 0.3f && !ar.Visible) showMiss++;
                if (idleT > SideArrow.FadeOut + 0.05f && ar.Visible) hideMiss++;
                if (f.Jumping)
                {
                    jumpFrames++;
                    if (ar.Visible && idleT > SideArrow.FadeOut + 0.05f) jumpShown++;
                }
                if (ar.Visible && activeT > 0.3f)
                {
                    if (ar.Side != -ctl.FishRun) sideMiss++;
                    var want = ctl.SideNow > SideArrow.Deadband ? SideArrow.Mode.Right : ctl.SideNow < -SideArrow.Deadband ? SideArrow.Mode.Wrong : SideArrow.Mode.Prompt;
                    if (ar.State != want) stateMiss++;
                    sidesSeen |= ar.Side > 0 ? 2 : 1;
                    if (ar.State != SideArrow.Mode.Right)   // (the pop squeezes the gap for a moment)
                    {
                        gapMin = Mathf.Min(gapMin, ar.GapNow);
                        gapMax = Mathf.Max(gapMax, ar.GapNow);
                    }
                    scaleMin = Mathf.Min(scaleMin, ar.Scale);
                    scaleMax = Mathf.Max(scaleMax, ar.Scale);
                    if (ar.AnchorKind == "entry") entryFrames++;
                    else if (ar.AnchorKind == "float") floatFrames++;
                    else mouthFrames++;
                }
                // -fkfloatshots: the float's moments (float rig)
                var fm = rig == "float" ? FloatMoment(ctl, t) : null;
                if (fm != null) yield return FloatShot(ctl, fm);
                // ---- the shots
                switch (phase)
                {
                    case 0:
                        if (active && ar.Visible && ar.State == SideArrow.Mode.Prompt && activeT > 0.6f && ar.Frame >= 3 && ar.Frame <= 5)
                        {
                            yield return ArrowShot(ctl, $"arrow_prompt_{rig}_{st}");
                            shotPrompt = true;
                            phase = 1;
                            holdT = 0f;
                        }
                        break;
                    case 1:
                        holdT = active && ar.State == SideArrow.Mode.Right && ctl.SideNow > 0.6f ? holdT + Time.deltaTime : 0f;
                        if (holdT >= 0.3f)
                        {
                            yield return ArrowShot(ctl, $"arrow_right_{rig}_{st}");
                            shotRight = true;
                            phase = 2;
                            mark = runsEnded;
                        }
                        break;
                    case 2:
                        if (runsEnded > mark && active && activeT > 0.3f)
                        {
                            phase = 3;
                            holdT = 0f;
                        }
                        break;
                    case 3:
                        holdT = active && ar.State == SideArrow.Mode.Wrong && ctl.SideNow < -0.6f ? holdT + Time.deltaTime : 0f;
                        // (a frame of the shake off centre)
                        if (holdT >= 0.3f && Mathf.Abs(ar.Pos.x - ar.AnchorPos2D.x) * PixelView.PPU >= 1f)
                        {
                            yield return ArrowShot(ctl, $"arrow_wrong_{rig}_{st}");
                            shotWrong = true;
                            phase = 4;
                        }
                        break;
                }
            }
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            ctl.RunEnded -= onRun;
            Caption(null);
            string sides = sidesSeen == 3 ? "both ways" : sidesSeen == 2 ? "right only" : sidesSeen == 1 ? "left only" : "none";
            Log(string.Format(CI, "[ARROW] {0} {1}: {2:0.0}s, {3} frames, arrow shown {4}; missing while active {5}, shown while idle {6}, wrong side {7}, wrong state {8}; jumps {9} frames (shown {10}); gap {11:0.0}..{12:0.0} px, scale {13:0.00}..{14:0.00}; anchor float {19} / entry {15} / mouth {16} frames; pointed {17}; runs ended {18}",
                st, rig, t, frames, visFrames, showMiss, hideMiss, sideMiss, stateMiss, jumpFrames, jumpShown, gapMin, gapMax, scaleMin, scaleMax, entryFrames, mouthFrames, sides, runsEnded, floatFrames));
            Log($"[ARROW] {st} {rig} sweeping: " + watch.Report());
            SCheck($"arrow {rig} {st}: only while the fish really sweeps sideways (a run straight out: none) - " + watch.Report(), watch.Ok);
            SCheck($"arrow {rig} {st}: shots prompt {shotPrompt} / right {shotRight} / wrong {shotWrong}", shotPrompt && shotRight && shotWrong);
            SCheck($"arrow {rig} {st}: shown whenever side pressure counts (missed {showMiss} of {frames} frames) and hidden otherwise, rests and jumps included (shown {hideMiss}; in jumps {jumpShown} of {jumpFrames})",
                showMiss == 0 && hideMiss == 0 && jumpShown == 0);
            SCheck($"arrow {rig} {st}: points against the run (wrong way {sideMiss} frames; pointed {sides}) and shows the side pressure's state (off {stateMiss} frames)", sideMiss == 0 && stateMiss == 0);
            SCheck($"arrow {rig} {st}: floats {N(gapMin, "0.0")}..{N(gapMax, "0.0")} px over the line's entry, scale {N(scaleMin)}..{N(scaleMax)} (min {N(SideArrow.MinScale)})",
                visFrames > 0 && gapMin >= 1.5f && gapMax <= 8f && scaleMin >= SideArrow.MinScale - 0.05f && scaleMax <= 1.3f);
            bool released = ctl.State == FishingController.S.Fighting;
            ctl.DebugRelease();
            yield return new WaitForEndOfFrame();
            SCheck($"arrow {rig} {st}: gone at once when the fight is over ({ctl.State}, visible {ar.Visible})", ctl.State == FishingController.S.Fighting || !ar.Visible);
            // the fish shook the hook (no break): the rig stays on the line where it was and is wound in, a float rig's float
            // and a lure alike (the lure is no longer gone at once: it comes home from the fish's mouth)
            bool floatRig = rig == "float";
            var rigAt = ctl.Tackle.Surface;
            if (released) arrowLetGos++;
            if (released) SCheck($"arrow {rig} {st}: after the fish is off the {(floatRig ? "float" : "lure")} stays in the water to be wound in ({ctl.State}, tackle {ctl.Tackle.State}, {N(rigAt.z, "0.0")} m out)",
                ctl.State == FishingController.S.Retrieving && ctl.Tackle.State == Tackle.Mode.Water && rigAt.z > ctl.Stage.L.zNear + 1f);
            else Log($"[ARROW] {st} {rig}: the fight ended by itself ({ctl.State}): no let-go to check (-fkauto breaks shake_off covers a fish shaking the lure off)");
            if (released && floatRig && FloatShotsOn)
            {
                yield return new WaitForSeconds(0.3f);
                yield return FloatShot(ctl, "float_letgo_" + st);
            }
            for (float w = 0f; w < 20f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
            if (released) SCheck($"arrow {rig} {st}: the {(floatRig ? "float" : "lure")} came home ({ctl.State})", ctl.State == FishingController.S.Ready);
            yield return new WaitForSeconds(0.4f);
        }
    }
}
