using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto zoom: the fishing view's zoom (<see cref="ViewZoom"/>, FishingController.Zoom.cs).
    /// <code>
    /// -fkfresh -fkrich -fksave zoom -fkscene Fishing -fkstage lake -fkauto zoom -fkshots &lt;dir&gt; -screen-width 1920 -screen-height 1080
    /// </code>
    /// The lake: at the ready (1x, the display is the whole target), a real flick cast (1x through the wind-up and the
    /// throw), the landing (eases in to the whole-pixel step in ~0.6 s, holds it pixel exact with the rod tip and the float
    /// in frame; the screen is compared pixel for pixel with the render target upscaled through the crop, a magenta marker
    /// texel must come out n x n screen px where WorldToScreen puts it, taps at known world points map back), a natural
    /// bite (still in), the hook set, the fish escaping (out), a retrieve (out), a quick recast after a spoon's fish is off
    /// (the wind-up hurries the zoom-out: 1x within 0.2 s of the press), the legend's cue off the zoomed frame (the view
    /// turns to it; across the whole view from the rig: out to 1x while it plays), a running carp fought for 14 s (the view
    /// follows the fish: [ZOOM] PAN lines, the fish and the rod tip in frame every frame, pixel exact, no empty borders; the
    /// side arrow's screen offset from its anchor is its world offset x the zoom, and the render target is the same zoomed
    /// and at 1x), its landing (out before the catch card), a line break (out), a fish held under the pier with the
    /// obstacle outlines shown (occlusion and outlines drawn the same zoomed and at 1x) and the legend encounter (out at its
    /// start, 1x throughout, its window placed on the whole view; back in for the fight it hooks). Then the stream (a snag,
    /// cut: out), the sea and the ocean: a rig placed and a fish fought, a zoomed frame each. [ZOOM] CHECK lines, [ZOOM]
    /// SHOT lines and shots zoom_ready, zoom_landed, zoom_bite, zoom_fight_1 / _2, zoom_landing, zoom_stream / _sea / _ocean
    /// (plus zoom_cue_pan, zoom_cue_out, zoom_enc_window, zoom_enc_fight); ends with "zoom test done: N failed".
    /// <para>-fkzoommode off|125|150|active (default 125): the setting (설정 → 캐스팅 후 줌인) under test, picked first
    /// through the settings row (its four choices, the chosen one highlighted, the panel inside the screen: shot
    /// zoom_settings; -fkzoomsettingsonly stops there, for other screen shapes). 125: the run above plus the setting changed
    /// mid-wait (1.5배: the step eases up; 끔: out; 액티브: stays out; 1.25배: back in). 150: the run above at the wide
    /// step (1080p: exactly 6 px per game px at rest; a fight's pan "gentle" as no faster than the fish it keeps in) plus a fish held and moved towards the angler until the wide frame
    /// no longer holds it with the rod tip (steps down to the largest step that does, both in frame every frame) and back
    /// (the wide step again). active: 1x through a real cast and the wait (nibbles too), in within 0.35-0.4 s of a
    /// natural bite on the float with the rod tip in frame, in through the fight it hooks, out on the landing; a missed
    /// float bite (out as it is wound in) and a missed lure bite (out as the wait resumes, 1x after); a snag at 1x; the
    /// legend encounter at 1x, in for its fight. off: a cast, the wait, a bite, a fight, the landing, a fish fought and
    /// the line broken, a snag: not one zoomed frame. Shots zoom_active_wait, zoom_active_bite, zoom_off_fight,
    /// zoom_wide_fallback ...</para>
    /// </summary>
    public partial class AutoPilot
    {
        int zFails;
        bool encMon;
        ZoomMode zMode;
        static readonly CultureInfo CIz = CultureInfo.InvariantCulture;

        static ZoomMode ZoomModeArg()
        {
            switch ((Arg("-fkzoommode") ?? "125").Trim().ToLowerInvariant())
            {
                case "off": return ZoomMode.Off;
                case "150": return ZoomMode.X150;
                case "active": return ZoomMode.Active;
                default: return ZoomMode.X125;
            }
        }

        void ZCheck(string name, bool ok, string numbers)
        {
            if (!ok) zFails++;
            Debug.Log($"[ZOOM] CHECK {name} {(ok ? "PASS" : "FAIL")} {numbers}");
        }

        static string Z2(float v) => v.ToString("0.00", CIz);
        static string ZV(Vector2 v, string f = "0.0") => "(" + v.x.ToString(f, CIz) + ", " + v.y.ToString(f, CIz) + ")";
        static ViewZoom ZoomNow => PixelView.Current != null ? PixelView.Current.Zoom : null;

        static string ZDesc(ViewZoom z) => z == null ? "-" : string.Format(CIz,
            "level {0:0.000} zoom {1:0.0000}x{2:0.0000} scale {3:0.000}x{4:0.000} step {5}x{6} crop ({7:0.00},{8:0.00} {9:0.00}x{10:0.00}) pan ({11:0.0}, {12:0.0}) exact {13} camera {14},{15} 1x {16} overscan {17}",
            z.Level, z.Zoom.x, z.Zoom.y, z.ScaleNow.x, z.ScaleNow.y, z.StepPx.x, z.StepPx.y, z.CropPx.x, z.CropPx.y, z.CropPx.width, z.CropPx.height,
            z.PanPx.x, z.PanPx.y, z.PixelExact, z.CamPan.x, z.CamPan.y, z.PanOne.x, z.OverscanPx);

        static bool InCropPx(ViewZoom z, Vector2 px, float margin)
        {
            var c = z.CropPx;
            return px.x >= c.xMin + margin && px.x <= c.xMax - margin && px.y >= c.yMin + margin && px.y <= c.yMax - margin;
        }

        static bool InCrop(ViewZoom z, Vector2 world, float margin) => InCropPx(z, z.WorldToPx(world), margin);

        /// <summary>
        /// No empty border: the crop lies inside the view's bounds (the render target at home, or the stage art with its
        /// overscan) and its UV inside the render target as the camera has it (the camera's pan holds it).
        /// </summary>
        static bool CropInside(ViewZoom z)
        {
            var b = z.BoundsPx;
            var c = z.CropPx;
            var uv = z.UV;
            return c.xMin >= b.xMin - 1e-3f && c.yMin >= b.yMin - 1e-3f && c.xMax <= b.xMax + 1e-3f && c.yMax <= b.yMax + 1e-3f
                   && uv.xMin >= -1e-4f && uv.yMin >= -1e-4f && uv.xMax <= 1f + 1e-4f && uv.yMax <= 1f + 1e-4f;
        }

        IEnumerator ZShot(FishingController ctl, string name)
        {
            yield return new WaitForEndOfFrame();
            var z = ZoomNow;
            string fish = ctl.Hooked != null ? ZV(z.WorldToPx(ctl.Fish2D(ctl.Hooked))) : "-";
            Log(string.Format(CIz, "[ZOOM] SHOT {0} state {1} {2} tip {3} rig {4} fish {5}", name, ctl.State, ZDesc(z),
                ZV(z.WorldToPx(ctl.RodTip2D)), ctl.Tackle.State != Tackle.Mode.Hidden ? ZV(z.WorldToPx(ctl.RigShown2D)) : "-", fish));
            string p = Path.Combine(shots, $"zoom_{name}.png");
            ScreenCapture.CaptureScreenshot(p);
            Log("shot " + p);
            yield return null;
        }

        /// <summary>
        /// Waits until the zoom is at the step (or <paramref name="timeout"/>); the seconds the ease took on its own clock
        /// (every frame's delta from the first frame it eased in, so a hitch just before or while it eases does not skew
        /// it) and on the wall clock (from now), -1 if not.
        /// </summary>
        IEnumerator WaitStep(float timeout, Action<float, float> took)
        {
            var z = ZoomNow;
            float t0 = Time.time, eased = 0f, last = z.Level;
            bool started = false;
            while (Time.time - t0 < timeout)
            {
                yield return new WaitForEndOfFrame();
                if (!started && z.Level > last) started = true;
                if (started) eased += Time.deltaTime;
                last = z.Level;
                if (z.Level >= 1f) break;
            }
            bool ok = z.Level >= 1f;
            took(ok ? eased : -1f, ok ? Time.time - t0 : -1f);
        }

        /// <summary>After an event that should zoom out: the target is out at once and 1x comes within <paramref name="max"/> s.</summary>
        IEnumerator ZoomOutCheck(FishingController ctl, string tag, float max = 0.75f)
        {
            var z = ZoomNow;
            var st = ctl.State;
            float lvl0 = z.Level, t0 = Time.time;
            yield return new WaitForEndOfFrame();
            bool targetOut = !z.ZoomedIn;
            float reach = -1f;
            for (float w = 0f; w < 2f; w += Time.deltaTime)
            {
                if (z.Level <= 0f)
                {
                    reach = Time.time - t0;
                    break;
                }
                yield return null;
            }
            ZCheck(tag + "_zoom_out", targetOut && reach >= 0f && reach <= max,
                $"from level {Z2(lvl0)} in {st}: target out {targetOut}, 1x after {Z2(reach)} s (now {ctl.State}); uv full {z.UV == new Rect(0f, 0f, 1f, 1f)}");
        }

        // ================================================================== the run
        IEnumerator ZoomTest()
        {
            yield return new WaitForSeconds(2f);
            UnityEngine.Random.InitState(4242);
            PointerInput.SimDpi = 0f;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            var sd = Game.Data;
            sd.sweepHint = sd.tideHint = sd.driftHint = sd.mendHint = sd.sideHint = sd.timeHint = true;
            LegendWatch.DebugMode = null;
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Stage.Def.id != "lake")
            {
                yield return GoStage("lake", 3f);
                ctl = FindAnyObjectByType<FishingController>();
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            var z = ZoomNow;
            if (ctl == null || z == null)
            {
                ZCheck("scene", false, "no lake / no zoom");
                Application.Quit();
                yield break;
            }
            // 설정 → 캐스팅 후 줌인: the mode under test, picked through the settings row (-fkzoommode off|125|150|active)
            zMode = ZoomModeArg();
            Log($"[ZOOM] mode {zMode} (-fkzoommode {Arg("-fkzoommode") ?? "-"})");
            yield return ZoomSettings(ctl, zMode);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fkzoomsettingsonly") >= 0)
            {
                Log($"[ZOOM] zoom test done: {zFails} failed");
                yield return new WaitForSeconds(0.3f);
                Application.Quit();
                yield break;
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            var b = z.BaseScale;
            float aim = zMode == ZoomMode.X150 ? ViewZoom.AimWide : ViewZoom.Aim;
            int want = Mathf.Max(Mathf.RoundToInt(b.y * aim), Mathf.FloorToInt(b.y + 1e-3f) + 1);
            if (zMode == ZoomMode.X150) want = Mathf.Max(want, Mathf.Max(Mathf.RoundToInt(b.y * ViewZoom.Aim), Mathf.FloorToInt(b.y + 1e-3f) + 1) + 1);
            Log(string.Format(CIz, "[ZOOM] screen {0}x{1} target {2}x{3}: base {4} screen px per game px, step {5}x{6} = {7}x (asked {8}x)",
                Screen.width, Screen.height, PixelView.Current.Target.width, PixelView.Current.Target.height, ZV(b, "0.000"), z.StepPx.x, z.StepPx.y,
                ZV(z.StepZoom, "0.0000"), aim));
            ZCheck("step", z.StepPx.y == want && z.StepPx.y > b.y && Mathf.Abs(z.StepZoom.y * b.y - z.StepPx.y) < 1e-3f
                           && (zMode != ZoomMode.X150 || Screen.height != 1080 || Screen.width != 1920 || z.StepPx.y == 6),
                $"base {ZV(b, "0.000")}: step {z.StepPx.x}x{z.StepPx.y} whole px (nearest to {aim}x: {want}) = {ZV(z.StepZoom, "0.0000")}x");
            SteerGear("rod_glass", "reel_light", "line_nylon4");
            // (no legend lurk point of its own: its cues would turn the view at random; they are checked in ZoomCue)
            ctl.Watch?.DebugLurk(null);

            if (zMode == ZoomMode.Off) yield return ZoomOffRun(ctl);
            else if (zMode == ZoomMode.Active) yield return ZoomActiveRun(ctl);
            else
            {
                yield return ZoomCastLand(ctl);
                yield return ZoomBite(ctl);
                yield return ZoomRetrieve(ctl);
                yield return ZoomRecast(ctl);
                yield return ZoomCue(ctl);
                yield return ZoomFightLand(ctl);
                yield return ZoomBreak(ctl);
                if (zMode == ZoomMode.X150) yield return ZoomWide(ctl);
                else yield return ZoomLiveSwitch(ctl);
                yield return ZoomPier(ctl);
                yield return ZoomEncounter(ctl);
                foreach (var id in new[] { "stream", "sea", "ocean" }) yield return ZoomStage(id);
            }

            FishingController.NoBites = false;
            Obstacles.Show = false;
            LegendWatch.DebugMode = null;
            Time.timeScale = 1f;
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Log($"[ZOOM] zoom test done: {zFails} failed");
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        // ------------------------------------------------------------------ 1. ready, a real cast, the landing
        IEnumerator ZoomCastLand(FishingController ctl)
        {
            var z = ZoomNow;
            FishingController.NoBites = true;
            yield return ToReady(ctl);
            ctl.Angler.DebugPlace(0f);
            EquipTest("bait_worm", ctl);
            yield return new WaitForSeconds(0.8f);
            ZCheck("ready_1x", z.Level == 0f && !z.ZoomedIn && z.UV == new Rect(0f, 0f, 1f, 1f), ZDesc(z));
            yield return ZShot(ctl, "ready");
            yield return ScreenMatch(ctl, "ready", false);
            // a real flick cast: 1x through the wind-up and the throw
            int castFrames = 0, castZoomed = 0;
            bool watching = true;
            IEnumerator WatchCast()
            {
                while (watching)
                {
                    if (ctl.State == FishingController.S.Aiming || ctl.State == FishingController.S.Casting)
                    {
                        castFrames++;
                        if (z.Level > 0f || z.ZoomedIn) castZoomed++;
                    }
                    yield return null;
                }
            }
            StartCoroutine(WatchCast());
            yield return Cast(ctl, 3.4f, 0f);
            for (float w = 0f; w < 6f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
            watching = false;
            float landT = Time.time;
            ZCheck("cast_1x", castFrames > 10 && castZoomed == 0, $"{castFrames} wind-up / throw frames, {castZoomed} zoomed; now {ctl.State}");
            if (ctl.State != FishingController.S.Waiting)
            {
                ZCheck("cast_landed", false, $"state {ctl.State}");
                yield break;
            }
            // eases in to the step
            float reach = -1f, last = 0f;
            bool monotonic = true;
            for (float w = 0f; w < 2f; w += Time.deltaTime)
            {
                if (z.Level < last - 1e-4f) monotonic = false;
                last = z.Level;
                if (z.Level >= 1f)
                {
                    reach = Time.time - landT;
                    break;
                }
                yield return null;
            }
            ZCheck("land_zoom_in", reach >= 0.5f && reach <= 0.75f && monotonic, $"the step {Z2(reach)} s after the landing (ease {ViewZoom.EaseTime} s), rising all the way {monotonic}");
            yield return new WaitForEndOfFrame();
            ZCheck("land_step_exact", z.PixelExact && Mathf.Abs(z.ScaleNow.x - z.StepPx.x) < 1e-3f && Mathf.Abs(z.ScaleNow.y - z.StepPx.y) < 1e-3f, ZDesc(z));
            if (zMode == ZoomMode.X150)
            {
                // 1.5배 at rest: the wide step itself (the rod tip and the float fit), 6 screen px per game px at 1080p
                bool hd = Screen.width == 1920 && Screen.height == 1080;
                ZCheck("wide_step_rest", z.StepPx.y == z.StepPxAsked && z.FitStep >= z.StepPxAsked && z.PixelExact && !z.StepEasing
                                         && (!hd || (z.StepPx.x == 6 && z.StepPx.y == 6 && Mathf.Abs(z.ScaleNow.x - 6f) < 1e-3f && Mathf.Abs(z.ScaleNow.y - 6f) < 1e-3f)),
                    $"step {z.StepPx.x}x{z.StepPx.y} (asked {z.StepPxAsked}, fits {z.FitStep}), scale {ZV(z.ScaleNow, "0.000")}; {ZDesc(z)}");
            }
            // holds: pixel exact, the rod tip and the float in frame, no empty border
            int frames = 0, off = 0, inexact = 0, outTip = 0, outRig = 0, border = 0;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                yield return new WaitForEndOfFrame();
                frames++;
                if (z.Level < 1f) off++;
                else if (!z.PixelExact) inexact++;
                if (!InCrop(z, ctl.RodTip2D, 2f)) outTip++;
                if (!InCrop(z, ctl.RigShown2D, 2f)) outRig++;
                if (!CropInside(z)) border++;
            }
            ZCheck("land_holds", frames > 30 && off == 0 && inexact == 0 && border == 0, $"{frames} frames: off the step {off}, not exact {inexact}, crop outside the target {border}");
            ZCheck("land_frames_rig", outTip == 0 && outRig == 0,
                $"rod tip out {outTip}, float out {outRig} of {frames} frames; tip {ZV(z.WorldToPx(ctl.RodTip2D))} float {ZV(z.WorldToPx(ctl.RigShown2D))} crop {z.CropPx}");
            yield return ZShot(ctl, "landed");
            yield return ScreenMatch(ctl, "landed", true);
            yield return TapMap(ctl, "landed");
        }

        // ------------------------------------------------------------------ 2. a natural bite, the hook set, the fish off
        IEnumerator ZoomBite(FishingController ctl)
        {
            var z = ZoomNow;
            if (ctl.State != FishingController.S.Waiting)
            {
                ZCheck("bite", false, $"not waiting ({ctl.State})");
                yield break;
            }
            FishingController.NoBites = false;
            int frames = 0, off = 0;
            float t = 0f;
            while (ctl.State == FishingController.S.Waiting && t < 45f)
            {
                t += Time.deltaTime;
                frames++;
                if (z.Level < 1f) off++;
                yield return null;
            }
            ZCheck("wait_zoomed", off == 0, $"{frames} frames waiting ({Z2(t)} s): {off} off the step");
            if (ctl.State != FishingController.S.Biting)
            {
                FishingController.NoBites = true;
                ZCheck("bite", false, $"no bite in {Z2(t)} s (state {ctl.State})");
                yield return ToReady(ctl);
                yield break;
            }
            FishingController.NoBites = true;
            yield return ZShot(ctl, "bite");
            ZCheck("bite_zoomed", z.Level >= 1f && z.PixelExact && InCrop(z, ctl.RigShown2D, 2f) && InCrop(z, ctl.RodTip2D, 2f), ZDesc(z));
            yield return new WaitForSeconds(0.05f);
            yield return Tap(Scr(0.5f, 0.6f));
            yield return null;
            ZCheck("hooked_zoomed", ctl.State == FishingController.S.Fighting && z.Level >= 1f, $"state {ctl.State}; {ZDesc(z)}");
            if (ctl.State != FishingController.S.Fighting)
            {
                yield return ToReady(ctl);
                yield break;
            }
            yield return new WaitForSeconds(1f);
            ctl.DebugRelease();   // the fish throws the hook: a float rig lies there and is wound in
            yield return ZoomOutCheck(ctl, "escape");
            yield return ToReady(ctl);
            ZCheck("escape_ready_1x", ctl.State == FishingController.S.Ready && z.Level == 0f, $"state {ctl.State}; {ZDesc(z)}");
        }

        /// <summary>A rig put on the water in front of him (as a cast lands) and the zoom settled in on it.</summary>
        IEnumerator ZoomPlace(FishingController ctl, Vector3 at, string tag)
        {
            yield return ToReady(ctl);
            // (out at 1x first: a wind-in cut short, as a snag cut free, leaves the view still easing out, and the
            // zoom-in after the rig comes down would only ease the rest of the way)
            for (float w = 0f; w < 1f && ZoomNow.Level > 0f; w += Time.deltaTime) yield return null;
            if (!ctl.DebugPlaceRig(at))
            {
                ZCheck(tag + "_place", false, $"state {ctl.State}");
                yield break;
            }
            float took = -1f, wall = -1f;
            yield return WaitStep(2f, (s, w) => { took = s; wall = w; });
            ZCheck(tag + "_zoom_in", took >= 0.5f && took <= 0.75f, $"the step {Z2(took)} s after the rig came down (its frames' deltas; wall clock {Z2(wall)} s)");
            yield return new WaitForSeconds(0.4f);
        }

        // ------------------------------------------------------------------ 3. the retrieve
        IEnumerator ZoomRetrieve(FishingController ctl)
        {
            var z = ZoomNow;
            yield return ZoomPlace(ctl, new Vector3(ctl.Angler.X + 1f, 0f, 14f), "retrieve");
            if (ctl.State != FishingController.S.Waiting) yield break;
            ctl.Retrieve();
            yield return ZoomOutCheck(ctl, "retrieve");
            int frames = 0, zoomed = 0;
            for (float w = 0f; w < 30f && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
            {
                frames++;
                if (z.Level > 0f) zoomed++;
                yield return null;
            }
            yield return new WaitForSeconds(0.4f);
            ZCheck("retrieve_1x", ctl.State == FishingController.S.Ready && zoomed == 0 && z.Level == 0f, $"{frames} frames winding in, {zoomed} zoomed; state {ctl.State}");
        }

        // ------------------------------------------------------------------ 3b. a quick recast: the wind-up hurries the zoom-out
        /// <summary>
        /// A spoon's fish throws the hook (a lure rig is simply gone: straight back to the ready, the zoom-out just begun)
        /// and he presses to wind up again 0.1 s later, as a player recasting at once does: the zoom-out is hurried, 1x
        /// within 0.2 s of the press and at the throw.
        /// </summary>
        IEnumerator ZoomRecast(FishingController ctl)
        {
            var z = ZoomNow;
            EquipTest("bait_spoon", ctl);
            var at = new Vector3(ctl.Angler.X + 1f, 0f, 14f);
            yield return ZoomPlace(ctl, at, "recast");
            if (ctl.State != FishingController.S.Waiting) yield break;
            if (!ctl.DebugHook(GameDatabase.GetFish("carp"), 45f, 99, new Vector3(at.x, -1.2f, at.z)))
            {
                ZCheck("recast_hook", false, $"state {ctl.State}");
                yield break;
            }
            yield return new WaitForSeconds(1f);
            ctl.DebugRelease();
            yield return new WaitForEndOfFrame();
            var stOff = ctl.State;
            float lvlOff = z.Level;
            yield return new WaitForSeconds(0.1f);
            float pressT = -1f, pressLvl = -1f, outAfter = -1f, throwLvl = -1f, throwAfter = -1f;
            int frames = 0, zoomedLate = 0;
            bool watching = true;
            IEnumerator WatchRecast()
            {
                while (watching)
                {
                    yield return new WaitForEndOfFrame();
                    var s = ctl.State;
                    if (s != FishingController.S.Aiming && s != FishingController.S.Casting) continue;
                    if (pressT < 0f)
                    {
                        pressT = Time.time;
                        pressLvl = z.Level;
                    }
                    frames++;
                    float since = Time.time - pressT;
                    if (outAfter < 0f && z.Level <= 0f) outAfter = since;
                    if (since > ZoomAimOutMax && z.Level > 0f) zoomedLate++;
                    if (s == FishingController.S.Casting && throwLvl < 0f)
                    {
                        throwLvl = z.Level;
                        throwAfter = since;
                    }
                }
            }
            StartCoroutine(WatchRecast());
            yield return Cast(ctl, 3.4f, 0f);
            for (float w = 0f; w < 6f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
            watching = false;
            yield return null;
            ZCheck("recast_hurried", stOff == FishingController.S.Ready && pressLvl > 0f && outAfter >= 0f && outAfter <= ZoomAimOutMax && zoomedLate == 0 && throwLvl == 0f,
                $"the fish off: {stOff} at level {Z2(lvlOff)}; pressed 0.1 s later at level {Z2(pressLvl)}: 1x {Z2(outAfter)} s after the press " +
                $"(<= {ZoomAimOutMax}), zoomed after that {zoomedLate} of {frames} wind-up / throw frames, the throw {Z2(throwAfter)} s after the press at level {Z2(throwLvl)}; now {ctl.State}");
            yield return ToReady(ctl);
            EquipTest("bait_worm", ctl);
        }

        const float ZoomAimOutMax = 0.2f;   // s: a wind-up has the view at 1x this soon (FishingController.ZoomAimOut 0.15 s for a whole zoom-out)

        // ------------------------------------------------------------------ 3c. the legend's cue while he waits, zoomed
        /// <summary>
        /// The legend's lurk point off the zoomed frame while he waits: its cue (the rings and the eye glint) must show every
        /// frame it plays. A little off the frame's side (the rig in front): the view turns to it, still at the step, pixel
        /// exact, the rod tip and the float in frame. Across the whole view from the rig (the rig far right, the lurk far
        /// left: no frame at the step holds both): out to 1x before it plays, back in after. Shots zoom_cue_pan, zoom_cue_out.
        /// </summary>
        IEnumerator ZoomCue(FishingController ctl)
        {
            var w = ctl.Watch;
            if (w == null)
            {
                ZCheck("cue", false, "no legend on this stage");
                yield break;
            }
            FishingController.NoBites = true;
            EquipTest("bait_worm", ctl);
            // (no snags: the cue rigs sit in the lake's weed bed and are wound out through it)
            float snagWas = Obstacles.SnagMult;
            Obstacles.SnagMult = 0f;
            yield return ZoomCueCase(ctl, "cue_pan", false);
            yield return ZoomCueCase(ctl, "cue_out", true);
            w.DebugLurk(null);
            Obstacles.SnagMult = snagWas;
            yield return ToReady(ctl);
        }

        IEnumerator ZoomCueCase(FishingController ctl, string tag, bool far)
        {
            var z = ZoomNow;
            var w = ctl.Watch;
            var P = ctl.Stage.P;
            var L = ctl.Stage.L;
            float RW = PixelView.Current.Target.width;
            float X = ctl.Angler.X;
            const float rz = 14f, lz = 18f;
            w.DebugLurk(null);
            // the rig: in front, or as far right as the water goes up to 420 px
            float rx = X + 1f;
            if (far)
                for (rx = X; rx < L.xLim - 0.7f; rx += 0.1f)
                    if (z.WorldToPx(P.To2D(new Vector3(rx, 0f, rz))).x >= 420f) break;
            yield return ZoomPlace(ctl, new Vector3(rx, 0f, rz), tag);
            if (ctl.State != FishingController.S.Waiting) yield break;
            var crop0 = z.CropPx;
            // the lurk point: beyond the frame's roomier side, halfway to the view's edge; far: at the view's left edge
            bool left = far || crop0.xMin > RW - crop0.xMax;
            float want = far ? 14f : left ? crop0.xMin * 0.5f : crop0.xMax + (RW - crop0.xMax) * 0.5f;
            float lx = X;
            for (int i = 0; i < 800; i++)
            {
                float px = z.WorldToPx(P.To2D(new Vector3(lx, 0f, lz))).x;
                if (left ? px <= want : px >= want) break;
                lx += left ? -0.05f : 0.05f;
            }
            w.DebugLurk(new Vector3(lx, 0f, lz), 1.2f);
            yield return new WaitForEndOfFrame();
            var ring0 = z.WorldToPx(w.CueRings2D);
            bool wasOut = !InCropPx(z, ring0, 0f);
            Log(string.Format(CIz, "[ZOOM] {0}: rig {1} tip {2}, the lurk at ({3:0.00}, {4:0.0}) m: rings {5} eyes {6}, outside the crop {7} {8}; cue in 1.2 s",
                tag, ZV(z.WorldToPx(ctl.RigShown2D)), ZV(z.WorldToPx(ctl.RodTip2D)), lx, lz, ZV(ring0), ZV(z.WorldToPx(w.CueEyes2D)), z.CropPx, wasOut));
            int frames = 0, ringOut = 0, eyesOut = 0, tipOut = 0, rigOut = 0, notStep = 0, inexact = 0, border = 0;
            float t0 = Time.time, cueStart = -1f, cueEnd = -1f, startLvl = -1f, backIn = -1f, minLvl = 1f;
            Vector2 panCue = default;
            bool shot = false;
            while (Time.time - t0 < 6f && ctl.State == FishingController.S.Waiting)
            {
                yield return new WaitForEndOfFrame();
                minLvl = Mathf.Min(minLvl, z.Level);
                if (!CropInside(z)) border++;
                if (w.CuePlaying)
                {
                    if (cueStart < 0f)
                    {
                        cueStart = Time.time;
                        startLvl = z.Level;
                    }
                    frames++;
                    if (!InCrop(z, w.CueRings2D, 0f)) ringOut++;
                    if (!InCrop(z, w.CueEyes2D, 0f)) eyesOut++;
                    if (!far)
                    {
                        if (z.Level < 1f) notStep++;
                        else if (!z.PixelExact) inexact++;
                        if (!InCrop(z, ctl.RodTip2D, 0f)) tipOut++;
                        if (!InCrop(z, ctl.RigShown2D, 0f)) rigOut++;
                    }
                    if (!shot && Time.time - cueStart >= 0.3f)
                    {
                        shot = true;
                        panCue = z.PanPx;
                        yield return ZShot(ctl, tag);
                    }
                }
                else if (cueStart >= 0f)
                {
                    if (cueEnd < 0f) cueEnd = Time.time;
                    if (z.Level >= 1f)
                    {
                        backIn = Time.time - cueEnd;
                        break;
                    }
                }
            }
            string nums = string.Format(CIz, "{0} cue frames: rings out {1}, eyes out {2}; level {3:0.00} as it began (lowest {4:0.00}), the step again {5:0.00} s after it; pan {6} -> {7}",
                frames, ringOut, eyesOut, startLvl, minLvl, backIn, ZV(crop0.center), ZV(panCue));
            if (!far)
                ZCheck(tag, wasOut && frames > 30 && ringOut == 0 && eyesOut == 0 && notStep == 0 && inexact == 0 && tipOut == 0 && rigOut == 0 && border == 0 && minLvl >= 1f,
                    nums + $"; zoomed through: off the step {notStep}, not exact {inexact}, rod tip out {tipOut}, float out {rigOut}, crop outside the target {border}");
            else
                ZCheck(tag, wasOut && frames > 30 && ringOut == 0 && eyesOut == 0 && startLvl == 0f && backIn >= 0f && backIn <= 0.8f && border == 0,
                    nums + $"; crop outside the target {border}");
            w.DebugLurk(null);
        }

        // ------------------------------------------------------------------ 4. a running fish: the view follows; the landing
        sealed class ZFightStats
        {
            public int frames, outFish, outTip, notStep, inexact, border, beyond, stepEase, stepChanges, lastStep = -1, minStep = 99, maxStep;
            public float maxSpeed, path, fishTop, tipTop;   // (fishTop / tipTop: the fish's / the rod tip's own top speed on the target, over 0.25 s)
            public readonly System.Collections.Generic.Queue<Vector3> fishTrail = new System.Collections.Generic.Queue<Vector3>();
            public readonly System.Collections.Generic.Queue<Vector3> tipTrail = new System.Collections.Generic.Queue<Vector3>();
            public readonly System.Collections.Generic.Queue<Vector3> panTrail = new System.Collections.Generic.Queue<Vector3>();
            public Vector2 min = new Vector2(1e9f, 1e9f), max = new Vector2(-1e9f, -1e9f);
            public bool arrowDone;
        }

        IEnumerator ZFight(FishingController ctl, string tag, float dur, ZFightStats st, float shot1 = -1f, float shot2 = -1f, bool arrow = false)
        {
            var z = ZoomNow;
            var f = ctl.Fight;
            var ar = ctl.PushArrow;
            var c = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.13f, ang = 0f, ws = CircleGesture.Reversed ? -1f : 1f, t = 0f, logT = 0f;
            Vector2 lastPan = z.PanPx, pan1 = lastPan;
            bool s1 = shot1 < 0f, s2 = shot2 < 0f, skipSpeed = false;
            st.arrowDone = !arrow;
            // (the whole view the crop may go over: the target at home, or the stage art with its overscan)
            var whole = z.BoundsPx;
            PointerInput.SimActive = true;
            while (ctl.State == FishingController.S.Fighting && t < dur)
            {
                float dt = Time.deltaTime;
                t += dt;
                bool running = ctl.FishRun != 0 || f.State == FightModel.Phase.Burst;
                if (f.TensionRatio > 0.95f) ang += ws * dt * 1.0f * Mathf.PI * 2f;
                else if (!running && !f.Jumping && f.TensionRatio < 0.8f) ang -= ws * dt * 1.6f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                yield return new WaitForEndOfFrame();
                if (ctl.State != FishingController.S.Fighting || ctl.Hooked == null) break;
                st.frames++;
                var pan = z.PanPx;
                var fishPx = z.WorldToPx(ctl.Fish2D(ctl.Hooked));
                var tipPx = z.WorldToPx(ctl.RodTip2D);
                // (a fish beyond the whole view, e.g. under the sea's breakwater, is out at 1x too: the crop stays in the
                // bounds; nor can a crop of this size in the bounds hold it with the rod tip when they lie farther apart than it)
                if (!whole.Contains(fishPx) || !OneCropHolds(z, whole, fishPx, tipPx, 1f)) st.beyond++;
                else if (!InCropPx(z, fishPx, 0f)) st.outFish++;
                if (!InCropPx(z, tipPx, 0f)) st.outTip++;
                if (z.Level < 1f) st.notStep++;
                else if (z.StepEasing) st.stepEase++;   // (1.5배: a step down to fit, or back up: not at rest)
                else if (!z.PixelExact) st.inexact++;
                if (!CropInside(z)) st.border++;
                if (st.lastStep >= 0 && z.StepPx.y != st.lastStep) st.stepChanges++;
                st.lastStep = z.StepPx.y;
                st.minStep = Mathf.Min(st.minStep, z.StepPx.y);
                st.maxStep = Mathf.Max(st.maxStep, z.StepPx.y);
                float step = (pan - lastPan).magnitude;
                lastPan = pan;
                st.path += step;
                // (the frame after a shot or a frozen compare spans several frames' pan: no speed sample; the step is this
                // frame's, so this frame's delta, read now at its end)
                float fdt = Time.deltaTime;
                // (a step easing moves the crop's centre by itself where the crop stops at the target's edge: not a pan)
                // measured over 0.25 s like the fish's and the rod tip's (a single frame's sample is mostly the crop's
                // whole-pixel snapping and the frame time's jitter); a skipped frame starts the window again
                if (fdt <= 0f || skipSpeed || z.StepEasing) st.panTrail.Clear();
                else st.maxSpeed = Mathf.Max(st.maxSpeed, TrailSpeed(st.panTrail, pan));
                skipSpeed = false;
                // the fish's and the rod tip's own speeds on the target, over the last 0.25 s (both are kept in frame)
                st.fishTop = Mathf.Max(st.fishTop, TrailSpeed(st.fishTrail, fishPx));
                st.tipTop = Mathf.Max(st.tipTop, TrailSpeed(st.tipTrail, tipPx));
                st.min = Vector2.Min(st.min, pan);
                st.max = Vector2.Max(st.max, pan);
                if ((logT -= dt) <= 0f)
                {
                    logT = 0.5f;
                    Log(string.Format(CIz, "[ZOOM] PAN {0} t {1:0.00} pan {2} fish {3} tip {4} run {5:+0;-0;0} phase {6} level {7:0.00} in fish {8} tip {9}",
                        tag, t, ZV(pan), ZV(fishPx), ZV(tipPx), ctl.FishRun, f.State, z.Level, InCropPx(z, fishPx, 0f), InCropPx(z, tipPx, 0f)));
                }
                if (!s1 && t >= shot1)
                {
                    s1 = true;
                    pan1 = pan;
                    skipSpeed = true;
                    yield return ZShot(ctl, tag + "_1");
                    continue;
                }
                if (!s2 && s1 && t >= shot2 && ((pan - pan1).magnitude >= 10f || t >= dur - 0.6f))
                {
                    s2 = true;
                    skipSpeed = true;
                    Log(string.Format(CIz, "[ZOOM] {0}: the pan moved {1:0.0} px between the two fight shots", tag, (pan - pan1).magnitude));
                    yield return ZShot(ctl, tag + "_2");
                    continue;
                }
                if (!st.arrowDone && ar != null && ar.Visible && ar.Alpha >= 0.95f && ctl.SideActive && t > 1f)
                {
                    st.arrowDone = true;
                    skipSpeed = true;
                    yield return FreezeCompare(ctl, tag + "_arrow", true);
                }
            }
            PointerInput.SimDown = false;
        }

        /// <summary>A point's speed on the target over the last 0.25 s (0 until 0.2 s of trail).</summary>
        static float TrailSpeed(System.Collections.Generic.Queue<Vector3> trail, Vector2 px)
        {
            trail.Enqueue(new Vector3(px.x, px.y, Time.time));
            while (trail.Count > 1 && Time.time - trail.Peek().z > 0.25f) trail.Dequeue();
            var old = trail.Peek();
            return Time.time - old.z >= 0.2f ? (px - (Vector2)old).magnitude / (Time.time - old.z) : 0f;
        }

        /// <summary>A crop the size of the one shown, on the target, holds both points at least <paramref name="m"/> px inside.</summary>
        static bool OneCropHolds(ViewZoom z, Rect whole, Vector2 a, Vector2 b, float m)
        {
            var size = z.CropPx.size;
            for (int ax = 0; ax < 2; ax++)
            {
                float lo = Mathf.Max(Mathf.Max(a[ax], b[ax]) + m - size[ax], whole.min[ax]);
                float hi = Mathf.Min(Mathf.Min(a[ax], b[ax]) - m, whole.max[ax] - size[ax]);
                if (lo > hi + 1e-3f) return false;
            }
            return true;
        }

        void ZFightChecks(string tag, ZFightStats st, bool mustMove)
        {
            var range = st.max - st.min;
            ZCheck(tag + "_in_frame", st.frames > 30 && st.outFish == 0 && st.outTip == 0,
                $"{st.frames} frames: the fish out {st.outFish}, the rod tip out {st.outTip} (the fish beyond the whole 1x view or no crop on it holding the fish with the rod tip {st.beyond})");
            ZCheck(tag + "_exact", st.notStep == 0 && st.inexact == 0 && st.border == 0,
                $"off the step {st.notStep}, not pixel exact {st.inexact}, crop outside the target {st.border} frames; steps {st.minStep}..{st.maxStep} px ({st.stepChanges} changes, {st.stepEase} frames easing between them)");
            // (a run faster than the gentle pace reaches the frame's edge and the keep-in-frame pull must keep up with it to
            // keep the fish in, at any step, as must the rod tip snapping up as a run ends: gentle then means never faster than
            // the fastest point it keeps in frame)
            float limit = Mathf.Max(90f, Mathf.Max(st.fishTop, st.tipTop));
            ZCheck(tag + "_follows", (!mustMove || Mathf.Max(range.x, range.y) >= 6f) && st.maxSpeed <= limit,
                string.Format(CIz, "pan range {0:0.0} x {1:0.0} px, path {2:0.0} px, top speed {3:0.0} px/s (gentle: <= 90, or no faster than the fish / the rod tip: <= {4:0}); their own top speeds {5:0.0} / {6:0.0} px/s",
                    range.x, range.y, st.path, st.maxSpeed, limit, st.fishTop, st.tipTop));
        }

        IEnumerator ZoomFightLand(FishingController ctl)
        {
            FishingController.NoBites = true;
            EquipTest("bait_paste", ctl);
            float x = ctl.Angler.X;
            var at = new Vector3(x + 1f, 0f, 16f);
            yield return ZoomPlace(ctl, at, "fight");
            if (!ctl.DebugHook(GameDatabase.GetFish("carp"), 70f, 4242, new Vector3(at.x, -1.2f, at.z)))
            {
                ZCheck("fight_hook", false, $"state {ctl.State}");
                yield break;
            }
            var st = new ZFightStats();
            yield return ZFight(ctl, "fight", 14f, st, 4f, 8f, true);
            ZFightChecks("fight", st, true);
            if (!st.arrowDone) ZCheck("fight_arrow", false, "the side arrow never showed");
            if (ctl.State != FishingController.S.Fighting)
            {
                ZCheck("fight_land", false, $"the fight ended early ({ctl.State})");
                yield return ToReady(ctl);
                yield break;
            }
            yield return ZoomLanding(ctl, "");
        }

        /// <summary>The landing (the fight won now): out as it starts, the catch card only after, 1x at the card and after it (checks and shot named <paramref name="pre"/>landing...).</summary>
        IEnumerator ZoomLanding(FishingController ctl, string pre)
        {
            var z = ZoomNow;
            ctl.DebugLand();
            float t0 = Time.time, outAt = -1f, cardAt = -1f;
            bool shot = false;
            while (Time.time - t0 < 6f)
            {
                float t = Time.time - t0;
                if (outAt < 0f && z.Level <= 0f) outAt = t;
                if (!shot && ctl.State == FishingController.S.Result && t >= 1.1f)
                {
                    shot = true;
                    yield return ZShot(ctl, pre + "landing");
                    continue;
                }
                if (HasButton("판매"))
                {
                    cardAt = t;
                    break;
                }
                yield return null;
            }
            ZCheck(pre + "landing_zoom_out", outAt >= 0f && outAt <= 0.75f && cardAt > outAt,
                $"1x {Z2(outAt)} s after the landing started, the catch card at {Z2(cardAt)} s; now {ZDesc(z)}");
            yield return new WaitForSeconds(0.3f);
            ZCheck(pre + "card_1x", z.Level == 0f && z.UV == new Rect(0f, 0f, 1f, 1f), ZDesc(z));
            Click("판매");
            yield return new WaitForSeconds(0.7f);
            ZCheck(pre + "after_card_1x", ctl.State == FishingController.S.Ready && z.Level == 0f && !z.ZoomedIn, $"state {ctl.State}; {ZDesc(z)}");
        }

        // ------------------------------------------------------------------ 5. a line break
        IEnumerator ZoomBreak(FishingController ctl)
        {
            var z = ZoomNow;
            var at = new Vector3(ctl.Angler.X - 1f, 0f, 13f);
            yield return ZoomPlace(ctl, at, "break");
            if (!ctl.DebugHook(GameDatabase.GetFish("carp"), 50f, 777, new Vector3(at.x, -1.2f, at.z)))
            {
                ZCheck("break_hook", false, $"state {ctl.State}");
                yield break;
            }
            yield return new WaitForSeconds(1.2f);
            ZCheck("break_before", ctl.State == FishingController.S.Fighting && z.Level >= 1f, $"state {ctl.State}; {ZDesc(z)}");
            ctl.DebugBreak();
            yield return ZoomOutCheck(ctl, "break");
            yield return ToReady(ctl);
            EquipTest("bait_paste", ctl);
        }

        // ------------------------------------------------------------------ 6. under the pier, the outlines shown
        IEnumerator ZoomPier(FishingController ctl)
        {
            var L = ctl.Stage.L;
            float x = ctl.Angler.X;
            var rig = new Vector3(x + 0.5f, 0f, L.zNear + 4f);
            yield return ZoomPlace(ctl, rig, "pier");
            if (!ctl.DebugHook(GameDatabase.GetFish("largemouth_bass"), 45f, 4242, new Vector3(rig.x, -1f, rig.z)))
            {
                ZCheck("pier_hook", false, $"state {ctl.State}");
                yield break;
            }
            ctl.Fight.Hold(9999f);
            ctl.DebugFishHold = new Vector3(x + 0.35f, -0.7f, L.zNear + 0.2f);
            Obstacles.Show = true;
            yield return new WaitForSeconds(1.6f);
            var ov = ctl.Overlay;
            ZCheck("pier_outlines", ov != null && ov.ShowDrawn && (Vector2)ov.transform.localPosition == ctl.Stage.DeckBob,
                $"outlines drawn {(ov != null && ov.ShowDrawn)} at {(ov != null ? ZV(ov.transform.localPosition, "0.000") : "-")} (the stage layers' place + bob {ZV(ctl.Stage.DeckBob, "0.000")})");
            yield return FreezeCompare(ctl, "pier", false);
            yield return ScreenMatch(ctl, "pier", false);
            Obstacles.Show = false;
            ctl.DebugFishHold = null;
            ctl.DebugRelease();
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ 7. the legend encounter
        IEnumerator ZoomEncounter(FishingController ctl)
        {
            if (ctl.Watch == null)
            {
                ZCheck("enc", false, "no legend on this stage");
                yield break;
            }
            var sp = ctl.Watch.Legends[0];
            var ed = sp.encounter;
            var key = GameDatabase.GetItem<BaitDef>(ed.keyLures.OrderByDescending(kv => kv.Value).First().Key);
            if (Game.I.Line.strength < ed.minLine)
            {
                var line = GameDatabase.Lines.Where(l => l.strength >= ed.minLine).OrderBy(l => l.strength).FirstOrDefault() ?? GameDatabase.Lines[^1];
                if (!Game.I.Owns(line.id)) Game.Data.ownedItems.Add(line.id);
                Game.I.Equip(line);
            }
            LegendWatch.DebugMode = "now";
            LegendWatch.DebugLegend = sp.id;
            LegendWatch.ClearCooldowns();
            // (the lurk point comes at once; its next cue put off past this run, which checks the encounter's own zoom)
            yield return null;
            if (ctl.Watch.HasLurk) ctl.Watch.DebugLurk(ctl.Watch.Lurk, 60f);
            encKey = key;
            encId = sp.id;
            FishingController.NoBites = false;   // (the watch only soaks with bites on)
            Log($"[ZOOM] encounter: {sp.id} with {key.id}");
            encMon = true;
            StartCoroutine(ZoomEncMonitor(ctl));
            yield return EncounterRun(ctl, "perfect");
            for (float w = 0f; w < 12f && encMon; w += Time.deltaTime) yield return null;
            encMon = false;
            LegendWatch.DebugMode = null;
            LegendWatch.DebugLegend = null;
            FishingController.NoBites = true;
            while (ctl.State == FishingController.S.Landing) yield return null;
            if (ctl.State == FishingController.S.Result)
            {
                for (float w = 0f; w < 4f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                Click("판매");
                yield return new WaitForSeconds(0.6f);
            }
            yield return ToReady(ctl);
        }

        IEnumerator ZoomEncMonitor(FishingController ctl)
        {
            var z = ZoomNow;
            float t = 0f;
            while (ctl.State != FishingController.S.Encounter && t < 40f && encMon)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (ctl.State != FishingController.S.Encounter)
            {
                ZCheck("enc_start", false, $"no encounter in {Z2(t)} s (state {ctl.State})");
                encMon = false;
                yield break;
            }
            float lvl0 = z.Level, t0 = Time.time, outAt = -1f;
            yield return new WaitForEndOfFrame();
            bool targetOut = !z.ZoomedIn;
            string phOut = "-";
            while (ctl.State == FishingController.S.Encounter && Time.time - t0 < 3f)
            {
                if (z.Level <= 0f)
                {
                    outAt = Time.time - t0;
                    phOut = ctl.Encounter != null ? ctl.Encounter.Ph.ToString() : "-";
                    break;
                }
                yield return null;
            }
            if (zMode == ZoomMode.Active)   // (액티브: he was waiting at 1x, and it stays there)
                ZCheck("enc_start_1x", lvl0 == 0f && targetOut && outAt == 0f, $"level {Z2(lvl0)} as it began, target out {targetOut}");
            else
                ZCheck("enc_zoom_out", lvl0 >= 0.99f && targetOut && outAt >= 0f && outAt <= 0.75f,
                    $"level {Z2(lvl0)} as it began, target out {targetOut}, 1x after {Z2(outAt)} s in phase {phOut} (the window opens after the {LegendEncounter.OmenT} s omen)");
            int frames = 0, zoomed = 0;
            bool win = false;
            while (ctl.State == FishingController.S.Encounter)
            {
                yield return new WaitForEndOfFrame();
                if (ctl.State != FishingController.S.Encounter) break;
                frames++;
                if (z.Level > 0f) zoomed++;
                var v = ctl.EncounterView;
                var e = ctl.Encounter;
                if (!win && v != null && v.Open && e != null && e.Ph >= LegendEncounter.Phase.Eyes)
                {
                    win = true;
                    var wr = v.Window;
                    Vector2 a = v.RTToScreen(wr.min), bb = v.RTToScreen(wr.max);
                    var ea = new Vector2(wr.xMin / v.Width * Screen.width, wr.yMin / v.Height * Screen.height);
                    var eb = new Vector2(wr.xMax / v.Width * Screen.width, wr.yMax / v.Height * Screen.height);
                    float err = Mathf.Max((a - ea).magnitude, (bb - eb).magnitude);
                    ZCheck("enc_window", err < 0.5f && z.Level == 0f,
                        $"window {wr} of {v.Width}x{v.Height} on screen {ZV(a)}..{ZV(bb)} (the whole view: {ZV(ea)}..{ZV(eb)}, off {Z2(err)} px) in {e.Ph}");
                    yield return ZShot(ctl, "enc_window");
                }
            }
            ZCheck("enc_1x_throughout", frames > 0 && zoomed == 0 && win, $"{frames} encounter frames, {zoomed} zoomed, window seen {win}");
            var after = ctl.State;
            float t1 = Time.time, inAt = -1f;
            while (Time.time - t1 < 3f)
            {
                if (z.Level >= 1f)
                {
                    inAt = Time.time - t1;
                    break;
                }
                yield return null;
            }
            if (zMode == ZoomMode.Active && after == FishingController.S.Waiting)   // (turned away: 액티브 waits at 1x)
                ZCheck("enc_back_waiting_1x", inAt < 0f, $"after the encounter: {after}, zoomed after {Z2(inAt)} s");
            else
                ZCheck("enc_back_in", (after == FishingController.S.Fighting || after == FishingController.S.Waiting) && inAt >= 0f && inAt <= 0.8f,
                    $"after the encounter: {after}, at the step {Z2(inAt)} s later");
            if (ctl.State == FishingController.S.Fighting && ctl.Hooked != null)
            {
                yield return new WaitForSeconds(1.2f);
                if (ctl.State == FishingController.S.Fighting && ctl.Hooked != null)
                {
                    yield return new WaitForEndOfFrame();
                    ZCheck("enc_fight_frame", z.PixelExact && InCrop(z, ctl.Fish2D(ctl.Hooked), 0f) && InCrop(z, ctl.RodTip2D, 0f),
                        $"{ctl.Hooked.Sp.id} {ZV(z.WorldToPx(ctl.Fish2D(ctl.Hooked)))} tip {ZV(z.WorldToPx(ctl.RodTip2D))}; {ZDesc(z)}");
                    yield return ZShot(ctl, "enc_fight");
                    ctl.DebugRelease();
                    yield return ZoomOutCheck(ctl, "enc_escape");
                }
            }
            encMon = false;
        }

        // ------------------------------------------------------------------ 8. the stream (+ a snag), the sea, the ocean
        IEnumerator ZoomStage(string id)
        {
            yield return GoStage(id, 3f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Stage.Def.id != id)
            {
                ZCheck(id + "_scene", false, "no scene");
                yield break;
            }
            ctl.Watch?.DebugLurk(null);
            var z = ZoomNow;
            var L = ctl.Stage.L;
            bool ocean = id == "ocean";
            SteerGear(ocean ? "rod_biggame" : "rod_carbon", ocean ? "reel_baitcast" : "reel_highgear", ocean ? "line_pe3" : "line_nylon4");
            FishingController.NoBites = true;
            yield return ToReady(ctl);
            ZCheck(id + "_ready_1x", z.Level == 0f && !z.ZoomedIn, ZDesc(z));
            if (id == "stream") yield return ZoomSnag(ctl);
            var (fishId, cm, bait) = OccFish(id);
            yield return ToReady(ctl);
            EquipTest(bait, ctl);
            yield return null;
            var rig = new Vector3(ctl.Angler.X + 1.2f, 0f, L.zNear + (ocean ? 14f : 10f));
            yield return ZoomPlace(ctl, rig, id);
            if (ctl.State != FishingController.S.Waiting) yield break;
            yield return new WaitForEndOfFrame();
            ZCheck(id + "_rig_frame", z.PixelExact && CropInside(z) && InCrop(z, ctl.RigShown2D, 2f) && InCrop(z, ctl.RodTip2D, 2f),
                $"rig {ZV(z.WorldToPx(ctl.RigShown2D))} tip {ZV(z.WorldToPx(ctl.RodTip2D))}; {ZDesc(z)}");
            if (!ctl.DebugHook(GameDatabase.GetFish(fishId), cm, 4242, new Vector3(rig.x, -1.2f, rig.z)))
            {
                ZCheck(id + "_hook", false, $"state {ctl.State}");
                yield break;
            }
            var st = new ZFightStats();
            yield return ZFight(ctl, id, 4.5f, st, 3.2f);
            ZFightChecks(id, st, false);
            if (ctl.State == FishingController.S.Fighting) yield return ScreenMatch(ctl, id, true);
            if (ctl.State == FishingController.S.Fighting)
            {
                ctl.DebugRelease();
                yield return ZoomOutCheck(ctl, id + "_off");
            }
            while (ctl.State == FishingController.S.Landing) yield return null;
            if (ctl.State == FishingController.S.Result)
            {
                for (float w = 0f; w < 4f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                Click("판매");
                yield return new WaitForSeconds(0.6f);
            }
            yield return ToReady(ctl);
        }

        /// <summary>The stream: a spoon snagged in md13.5's skirt (still in), the line cut (끊기: back to the ready, out).</summary>
        IEnumerator ZoomSnag(FishingController ctl)
        {
            var z = ZoomNow;
            var obs = ctl.Stage.Obstacles;
            if (obs == null || obs.Get("md13.5") == null || obs.Get("md13.5.skirt") == null)
            {
                ZCheck("snag", false, "no md13.5 on the stream");
                yield break;
            }
            var at = SkirtPoint(ctl, "md13.5");
            // (the stream's drift runs on the real clock: the spoon's path through the skirt varies, so a few tries, seeded)
            for (int tries = 0; tries < 3 && ctl.State != FishingController.S.Snagged; tries++)
            {
                Obstacles.Rnd = new System.Random(20260930 + tries);
                yield return SnagHere(ctl, "bait_spoon", at, 0f, 1f);
            }
            if (ctl.State != FishingController.S.Snagged)
            {
                ZCheck("snag", false, $"not snagged ({ctl.State})");
                yield return ToReady(ctl);
                yield break;
            }
            if (zMode == ZoomMode.Active || zMode == ZoomMode.Off)
            {
                // (액티브: the snag stays at the zoom it found, 1x from the wait; 끔: never)
                int frames = 0, zoomed = 0;
                for (float w = 0f; w < 0.8f && ctl.State == FishingController.S.Snagged; w += Time.deltaTime)
                {
                    yield return new WaitForEndOfFrame();
                    frames++;
                    if (z.Level > 0f || z.ZoomedIn) zoomed++;
                }
                ZCheck("snag_1x", frames > 10 && zoomed == 0, $"{frames} snagged frames, {zoomed} zoomed; {ZDesc(z)}");
                ctl.Retrieve();   // (끊기)
                yield return new WaitForSeconds(0.3f);
                ZCheck("snag_cut_ready", ctl.State == FishingController.S.Ready && z.Level == 0f, $"state {ctl.State}; {ZDesc(z)}");
                yield break;
            }
            yield return new WaitForSeconds(0.8f);
            yield return new WaitForEndOfFrame();
            ZCheck("snag_zoomed", z.Level >= 1f && z.PixelExact && InCrop(z, ctl.RigShown2D, 2f) && InCrop(z, ctl.RodTip2D, 2f),
                $"snagged: rig {ZV(z.WorldToPx(ctl.RigShown2D))} tip {ZV(z.WorldToPx(ctl.RodTip2D))}; {ZDesc(z)}");
            ctl.Retrieve();   // (끊기: the line is cut)
            yield return ZoomOutCheck(ctl, "snag_cut");
            ZCheck("snag_cut_ready", ctl.State == FishingController.S.Ready && z.Level == 0f, $"state {ctl.State}; {ZDesc(z)}");
        }

        // ================================================================== the pixel checks
        /// <summary>
        /// Freezes the moment and compares the screen (HUD hidden) pixel for pixel with the render target upscaled through
        /// the crop; with <paramref name="marker"/> a magenta texel is put on the water beside the rig first: it must come
        /// out as one n x n block where WorldToScreen puts it, and its centre must map back into it.
        /// </summary>
        IEnumerator ScreenMatch(FishingController ctl, string tag, bool marker)
        {
            var pv = PixelView.Current;
            var z = pv.Zoom;
            float ts = Time.timeScale;
            Time.timeScale = 0f;
            yield return null;
            SpriteRenderer mk = null;
            Vector2 mkWorld = default;
            int W = pv.Target.width, H = pv.Target.height;
            if (marker)
            {
                var c = z.CropPx;
                var tp = z.WorldToPx(ctl.RigShown2D) + new Vector2(17f, 7f);
                tp.x = Mathf.Floor(Mathf.Clamp(tp.x, c.xMin + 6f, c.xMax - 6f)) + 0.5f;
                tp.y = Mathf.Floor(Mathf.Clamp(tp.y, c.yMin + 6f, c.yMax - 40f)) + 0.5f;
                mkWorld = pv.BaseCenter + (tp - new Vector2(W * 0.5f, H * 0.5f)) / PixelView.PPU;
                mk = new GameObject("ZoomMarker").AddComponent<SpriteRenderer>();
                mk.sprite = Art.Pixel;
                mk.color = new Color32(255, 0, 255, 255);
                mk.sortingOrder = 32000;
                mk.transform.position = new Vector3(mkWorld.x, mkWorld.y, 0f);
            }
            var hud = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(cv => cv.enabled && cv.isRootCanvas && cv.name != "PixelCanvas").ToList();
            foreach (var cv in hud) cv.enabled = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            var rt = GrabRT();
            var scr = ScreenCapture.CaptureScreenshotAsTexture();
            var uv = z.UV;
            string desc = ZDesc(z);
            bool exact = z.PixelExact || z.Level <= 0f;
            Vector2 mkScreen = marker ? pv.WorldToScreen(mkWorld) : default;
            Vector2 cam = pv.WorldCamera.transform.position;
            foreach (var cv in hud) if (cv != null) cv.enabled = true;
            if (mk != null) Destroy(mk.gameObject);
            Time.timeScale = ts;
            if (rt == null || scr == null)
            {
                ZCheck(tag + "_screen", false, "no capture");
                yield break;
            }
            int SW = scr.width, SH = scr.height;
            var a = rt.GetPixels32();
            var q = scr.GetPixels32();
            double ox = uv.x * W, oy = uv.y * H, sx = uv.width * W / SW, sy = uv.height * H / SH;
            int bad = 0, mc = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            string first = "";
            for (int y = 0; y < SH; y++)
            {
                int ty = Mathf.Clamp((int)Math.Floor(oy + (y + 0.5) * sy), 0, H - 1);
                int row = ty * W, srow = y * SW;
                for (int x = 0; x < SW; x++)
                {
                    int tx = Mathf.Clamp((int)Math.Floor(ox + (x + 0.5) * sx), 0, W - 1);
                    var p = a[row + tx];
                    var s = q[srow + x];
                    if (Math.Abs(p.r - s.r) > 3 || Math.Abs(p.g - s.g) > 3 || Math.Abs(p.b - s.b) > 3)
                    {
                        if (bad++ == 0) first = $" first at screen ({x},{y}) texel ({tx},{ty}): target #{p.r:x2}{p.g:x2}{p.b:x2} screen #{s.r:x2}{s.g:x2}{s.b:x2}";
                    }
                    if (marker && s.r > 235 && s.g < 25 && s.b > 235)
                    {
                        mc++;
                        x0 = Math.Min(x0, x);
                        y0 = Math.Min(y0, y);
                        x1 = Math.Max(x1, x);
                        y1 = Math.Max(y1, y);
                    }
                }
            }
            ZCheck(tag + "_screen", bad == 0 && exact && SW == Screen.width && SH == Screen.height,
                $"{SW}x{SH} screen px against the {W}x{H} target through uv ({uv.x.ToString("0.0000", CIz)}, {uv.y.ToString("0.0000", CIz)} {uv.width.ToString("0.0000", CIz)}x{uv.height.ToString("0.0000", CIz)}): {bad} differ{first}; {desc}");
            if (marker)
            {
                var n = z.StepPx;
                int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
                var ctr = new Vector2((x0 + x1 + 1) * 0.5f, (y0 + y1 + 1) * 0.5f);
                float err = (ctr - mkScreen).magnitude;
                var back = (Vector2)pv.WorldCamera.ViewportToWorldPoint(new Vector3(uv.x + ctr.x / SW * uv.width, uv.y + ctr.y / SH * uv.height, 10f));
                float backErr = (back - mkWorld).magnitude * PixelView.PPU;
                bool nOk = z.Level >= 1f ? bw == n.x && bh == n.y && mc == n.x * n.y : mc > 0;
                ZCheck(tag + "_marker", nOk && err <= 0.05f && backErr <= 0.02f,
                    string.Format(CIz, "marker texel at world {0}: {1} magenta px in a {2}x{3} block (step {4}x{5}) centred {6}, WorldToScreen {7} (off {8:0.000} px); the block's centre maps back {9:0.000} game px from it (camera {10})",
                        ZV(mkWorld, "0.0000"), mc, bw, bh, n.x, n.y, ZV(ctr, "0.00"), ZV(mkScreen, "0.00"), err, backErr, ZV(cam, "0.000")));
            }
            Destroy(rt);
            Destroy(scr);
        }

        /// <summary>Taps (press and release, time frozen) at the screen points WorldToScreen gives for the float, the rod tip and the hat: ScreenToWorld of the press must land on the point, on the texel it lies in.</summary>
        IEnumerator TapMap(FishingController ctl, string tag)
        {
            var pv = PixelView.Current;
            var z = pv.Zoom;
            float ts = Time.timeScale;
            Time.timeScale = 0f;
            yield return null;
            float worst = 0f;
            int texMiss = 0, pressed = 0;
            string log = "";
            var pts = new (string, Vector2)[] { ("float", ctl.RigShown2D), ("tip", ctl.RodTip2D), ("hat", ctl.Angler.HatPos2D) };
            int W = pv.Target.width, H = pv.Target.height;
            foreach (var (name, w) in pts)
            {
                var s = pv.WorldToScreen(w);
                PointerInput.SimActive = true;
                PointerInput.SimDown = false;
                PointerInput.SimPos = s;
                yield return null;
                PointerInput.SimDown = true;
                yield return null;
                if (PointerInput.Pressed) pressed++;
                var at = PointerInput.Position;
                var back = pv.ScreenToWorld(at);
                float err = (back - w).magnitude * PixelView.PPU;
                worst = Mathf.Max(worst, err);
                // the screen pixel under the tap shows the texel the point lies in (unless it lies on a texel edge)
                Vector2 cam = pv.WorldCamera.transform.position;
                var tpx = (w - cam) * PixelView.PPU + new Vector2(W * 0.5f, H * 0.5f);
                var uv = z.UV;
                int sxp = Mathf.FloorToInt(at.x), syp = Mathf.FloorToInt(at.y);
                int txs = Mathf.FloorToInt((float)(uv.x * W + (sxp + 0.5) * uv.width * W / Screen.width));
                int tys = Mathf.FloorToInt((float)(uv.y * H + (syp + 0.5) * uv.height * H / Screen.height));
                bool edge = Mathf.Abs(tpx.x - Mathf.Round(tpx.x)) < 0.02f || Mathf.Abs(tpx.y - Mathf.Round(tpx.y)) < 0.02f;
                if (!edge && (txs != Mathf.FloorToInt(tpx.x) || tys != Mathf.FloorToInt(tpx.y))) texMiss++;
                log += string.Format(CIz, " {0} world {1} screen {2} back {3} ({4:0.0000} game px) texel ({5},{6}) under ({7},{8}){9};",
                    name, ZV(w, "0.000"), ZV(at, "0.0"), ZV(back, "0.000"), err, Mathf.FloorToInt(tpx.x), Mathf.FloorToInt(tpx.y), txs, tys, edge ? " edge" : "");
                PointerInput.SimDown = false;
                yield return null;
            }
            Time.timeScale = ts;
            ZCheck(tag + "_tap", worst < 0.01f && texMiss == 0 && pressed == pts.Length, $"{pressed} presses, worst {worst.ToString("0.0000", CIz)} game px, texel misses {texMiss}:{log} {ZDesc(z)}");
        }

        /// <summary>
        /// Freezes the moment and draws it zoomed, at 1x (the display only), zoomed again and at 1x again: the render target
        /// must be the same (the zoom never moves what is drawn: the side arrow over its anchor, the occlusion behind the
        /// pier, the outlines); frame-to-frame changes (zoomed vs zoomed, 1x vs 1x) are left out. With <paramref name="arrow"/> the side arrow's
        /// screen offset from its anchor must be its world offset x the zoom, both frames.
        /// </summary>
        IEnumerator FreezeCompare(FishingController ctl, string tag, bool arrow)
        {
            var pv = PixelView.Current;
            var z = pv.Zoom;
            var ar = ctl.PushArrow;
            float ts = Time.timeScale;
            Time.timeScale = 0f;
            // (the current's clock runs on real time: a lake gust's cat's paw would move between the grabs)
            CurrentField.DebugFreeze = true;
            var cur = ctl.Stage.Current;
            string gust = cur != null && cur.GustT >= 0f ? $", a gust {Z2(cur.GustT)} s in (held)" : "";
            int occA = -1, occB = -1;
            float errA = 0f, errB = 0f;
            string arA = "", arB = "";
            // (every grab on an even frame: a straining angler's rod shivers a pixel every other frame, Angler.Strain01)
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var a = GrabRT();
            if (arrow) arA = ArrowOnScreen(pv, ar, out errA);
            else yield return OccCount(n => occA = n);
            z.Hold(0f);
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var b = GrabRT();
            if (arrow) arB = ArrowOnScreen(pv, ar, out errB);
            else yield return OccCount(n => occB = n);
            z.Hold(1f);
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var c = GrabRT();
            // (and 1x once more: a frozen scene's few animated pixels, e.g. a held fish's shiver, may repeat every few
            // frames and come back the same in both zoomed grabs; what changes between either pair is left out)
            z.Hold(0f);
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var d = GrabRT();
            z.Hold(1f);
            yield return null;
            z.Release();
            Time.timeScale = ts;
            CurrentField.DebugFreeze = false;
            if (a == null || b == null || c == null || d == null)
            {
                ZCheck(tag + "_rt_same", false, "no render target");
                yield break;
            }
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            var pc = c.GetPixels32();
            var pd = d.GetPixels32();
            int diff = 0, noise = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                bool same = pa[i].r == pc[i].r && pa[i].g == pc[i].g && pa[i].b == pc[i].b
                            && pb[i].r == pd[i].r && pb[i].g == pd[i].g && pb[i].b == pd[i].b;
                if (!same)
                {
                    noise++;
                    continue;
                }
                if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b) diff++;
            }
            ZCheck(tag + "_rt_same", diff == 0 && noise < pa.Length / 50, $"{diff} px of the {a.width}x{a.height} target differ zoomed vs 1x (frame-to-frame noise {noise} px left out{gust})");
            if (arrow)
                ZCheck(tag + "_anchor", errA < 0.05f && errB < 0.05f && ar.GapNow >= 1.5f && ar.GapNow <= 8f,
                    $"zoomed:{arA} | 1x:{arB} | gap {Z2(ar.GapNow)} px over the {ar.AnchorKind}");
            else
                ZCheck(tag + "_occlusion", occA > 0 && occA == occB, $"hidden behind the pier: {occA} px zoomed, {occB} px at 1x");
            Destroy(a);
            Destroy(b);
            Destroy(c);
            Destroy(d);
        }

        /// <summary>The side arrow's offset from its anchor: world (game px) vs screen (px) / the scale now; the error in screen px.</summary>
        static string ArrowOnScreen(PixelView pv, SideArrow ar, out float err)
        {
            var z = pv.Zoom;
            var dW = (ar.Pos - ar.AnchorPos2D) * PixelView.PPU;
            var dS = pv.WorldToScreen(ar.Pos) - pv.WorldToScreen(ar.AnchorPos2D);
            var want = Vector2.Scale(dW, z.ScaleNow);
            err = (dS - want).magnitude;
            bool shown = InCrop(z, ar.Pos, 0f) && InCrop(z, ar.AnchorPos2D, 0f);
            return string.Format(CIz, " offset {0} game px, on screen {1} px (x {2:0.000} = {3}, off {4:0.000}), in frame {5}",
                ZV(dW, "0.00"), ZV(dS, "0.00"), z.ScaleNow.y, ZV(want, "0.00"), err, shown);
        }

        /// <summary>What the front layer hides now: the target drawn with the hiding off and on (time frozen), pixels that differ.</summary>
        IEnumerator OccCount(Action<int> result)
        {
            FrontOcclusion.Enabled = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var off = GrabRT();
            FrontOcclusion.Enabled = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var on = GrabRT();
            // (off again: the frame-to-frame noise of the frozen scene, which differs between grabs, is left out)
            FrontOcclusion.Enabled = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var off2 = GrabRT();
            FrontOcclusion.Enabled = true;
            int n = 0;
            if (off != null && on != null && off2 != null)
            {
                var a = off.GetPixels32();
                var b = on.GetPixels32();
                var a2 = off2.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                    if (a[i].r == a2[i].r && a[i].g == a2[i].g && a[i].b == a2[i].b && (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b)) n++;
            }
            if (off != null) Destroy(off);
            if (on != null) Destroy(on);
            if (off2 != null) Destroy(off2);
            result(n);
        }
    }
}
