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
            foreach (var (rig, bait) in new[] { ("float", "bait_paste"), ("lure", "bait_minnow") })
                yield return ArrowFight(ctl, sp, cm, homeX, rig, bait);
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
                // ---- the rules, every frame (after the fade in / out has had its time)
                bool active = ctl.State == FishingController.S.Fighting && ctl.SideActive;
                activeT = active ? activeT + Time.deltaTime : 0f;
                idleT = active ? 0f : idleT + Time.deltaTime;
                frames++;
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
            // a float rig: the float stays on the water where it was and is wound in; a lure is gone at once (ready)
            bool floatRig = rig == "float";
            if (released) SCheck($"arrow {rig} {st}: after the fish is off {(floatRig ? "the float stays in the water to be wound in" : "the lure is gone")} ({ctl.State}, tackle {ctl.Tackle.State})",
                floatRig ? ctl.State == FishingController.S.Retrieving && ctl.Tackle.State == Tackle.Mode.Water : ctl.State == FishingController.S.Ready);
            if (released && floatRig && FloatShotsOn)
            {
                yield return new WaitForSeconds(0.3f);
                yield return FloatShot(ctl, "float_letgo_" + st);
            }
            for (float w = 0f; w < 3f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.4f);
        }
    }
}
