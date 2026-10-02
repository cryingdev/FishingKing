using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto pan: the view follows a fish beyond the home 1x frame over the stage art's overscan (<see cref="ViewZoom"/>).
    /// <code>
    /// -fkfresh -fkrich -fkgear -fksave pan -fkscene Fishing -fkstage sea -fkauto pan -fkshots &lt;dir&gt; -screen-width 1920 -screen-height 1080 [-fkzoommode 125|off|150|active]
    /// </code>
    /// On the sea (any stage given with -fkstage works; the sea is the default), per mode (1.25배 and 끔, or the one
    /// -fkzoommode names): a fish hooked out on the right is held on a scripted run (the fight model stands still) out past
    /// the home frame's right edge (90 px past it: the shots), on to near the art's edge, a fast dash past it, back in, then
    /// the same out past the left edge,
    /// back in and landed (끔: let go). Every frame: the fish and the rod tip in the view (the fish wherever the bounds can
    /// show it; frames with it beyond the art itself are counted apart; zoomed, a run too far from the rod tip for one
    /// zoomed frame eases out to 1x: counted too), the crop in the bounds and its UV on the target, pixel exact at rest
    /// when zoomed, the camera on whole px, and the view's movement per frame (game px: no more than 6 px, or than the
    /// frame's own time allows at 400 px/s) and per 0.25 s;
    /// then the view home (the camera and the 1x pan back to zero, at 1x) within <see cref="PanHomeMax"/> s of the fight's
    /// end. At the far point the same moment is shot with the overscan off (the old clamp at the home frame's edge: the
    /// fish out of view) and on (followed): pan_&lt;mode&gt;_before / _after. Then a natural fight (the fight model running,
    /// wound in like the fish scenario) hooked far out on the right, the same per-frame checks; and the alignment: with
    /// the camera panned and time frozen, the render target must equal the home one shifted by the pan (stage layers,
    /// actor layers, the front layer's occlusion, the obstacle outlines, the water effects held as drawn: everything
    /// moves together). Last, on the lake (-fkpanenc off skips it): the legend encounter begun with the view panned out to
    /// the rig: the window opens from (and, played wrong so it turns away, closes onto) the lure's point as the camera
    /// has it now (<see cref="PanEncounter"/>; shot pan_enc_open). In 끔 a fish let go out there leaves its float, which
    /// is followed while it is wound in (no frame with it out of view); home is timed from when it is in.
    /// [PAN] CHECK lines; ends with "pan test done: N failed".
    /// </summary>
    public partial class AutoPilot
    {
        int panFails;
        /// <summary>Seconds the view may take to be home again after a fight ends far out (the zoom-out, the 1x pan back).</summary>
        const float PanHomeMax = 2.0f;
        /// <summary>Game px the view may move in one frame (more only in a long frame, at up to <see cref="PanFrameSpeed"/>), and px/s over 0.25 s, while following.</summary>
        const float PanStepMax = 6f, PanSpeedMax = 320f;
        /// <summary>Game px/s: a frame's step over its own time (a frame hitch moves the view farther in that frame, not a jump).</summary>
        const float PanFrameSpeed = 400f;

        /// <summary>A step of the view this frame bigger than a few px and than the frame's time allows.</summary>
        static bool PanJump(float step, float dt) => step > Mathf.Max(PanStepMax, PanFrameSpeed * dt);

        void PCheck(string name, bool ok, string numbers)
        {
            if (!ok) panFails++;
            Debug.Log($"[PAN] CHECK {name} {(ok ? "PASS" : "FAIL")} {numbers}");
        }

        class PanStats
        {
            public int frames, beyondHome, beyondArt, outFish, outTip, border, inexact, offWhole, maxSide, jumps, zoomedOut;
            public float maxStep, maxSpeed, maxCam, maxOver;
            public Vector2 lastCentre;
            public bool has;
            public readonly System.Collections.Generic.Queue<Vector3> trail = new System.Collections.Generic.Queue<Vector3>();

            public override string ToString() => string.Format(CIp,
                "{0} frames: the fish beyond the home frame {1} (by up to {2:0} px, the camera panned up to {3:0} px), beyond the art {4}; the fish out of view {5}, the rod tip out {6}; crop off the bounds / the target {7}, not pixel exact at rest {8}, camera off whole px {9}; the view's steps up to {10:0.0} px a frame ({12} jumps), {11:0} px/s; zoomed out (the fish too far from the rod tip for a zoomed frame) {13} frames",
                frames, beyondHome, maxOver, maxCam, beyondArt, outFish, outTip, border, inexact, offWhole, maxStep, maxSpeed, jumps, zoomedOut);
        }

        /// <summary>One frame's sample (read at its end): the fish, the rod tip, the crop, the camera.</summary>
        void PanSample(FishingController ctl, PanStats st)
        {
            var pv = PixelView.Current;
            var z = pv.Zoom;
            var b = z.BoundsPx;
            float W = pv.Target.width;
            st.frames++;
            var fish = z.WorldToPx(ctl.Fish2D(ctl.Hooked));
            var tip = z.WorldToPx(ctl.RodTip2D);
            float over = Mathf.Max(-fish.x, fish.x - W);
            if (over > 0f)
            {
                st.beyondHome++;
                st.maxOver = Mathf.Max(st.maxOver, over);
            }
            st.maxCam = Mathf.Max(st.maxCam, Mathf.Abs(z.CamPan.x));
            if (fish.x < b.xMin + 1f || fish.x > b.xMax - 1f) st.beyondArt++;
            else if (!InCropPx(z, new Vector2(fish.x, Mathf.Clamp(fish.y, z.CropPx.yMin, z.CropPx.yMax)), 0f)) st.outFish++;
            if (!InCropPx(z, tip, 0f)) st.outTip++;
            if (!CropInside(z)) st.border++;
            if (z.Level >= 1f && !z.StepEasing && !z.PixelExact) st.inexact++;
            // the camera on whole px: its place is home + the pan (+ a shake), snapped
            Vector2 cam = pv.WorldCamera.transform.position;
            float cx = cam.x * PixelView.PPU, cy = cam.y * PixelView.PPU;
            if (Mathf.Abs(cx - Mathf.Round(cx)) > 1e-3f || Mathf.Abs(cy - Mathf.Round(cy)) > 1e-3f) st.offWhole++;
            var c = z.CropPx.center;
            if (st.has)
            {
                float step = (c - st.lastCentre).magnitude;
                st.maxStep = Mathf.Max(st.maxStep, step);
                if (PanJump(step, Time.deltaTime)) st.jumps++;
            }
            if (FishingController.ZoomSetting != ZoomMode.Off && !z.ZoomedIn) st.zoomedOut++;
            st.lastCentre = c;
            st.has = true;
            st.maxSpeed = Mathf.Max(st.maxSpeed, TrailSpeed(st.trail, c));
        }

        /// <summary>The world x (metres) at depth y, distance z whose fish shows at home-view px x <paramref name="px"/>.</summary>
        static float XForPx(FishingController ctl, float px, float y, float z)
        {
            var P = ctl.Stage.P;
            var zm = ZoomNow;
            float lo = -ctl.Stage.L.xLim, hi = ctl.Stage.L.xLim;
            for (int i = 0; i < 40; i++)
            {
                float m = (lo + hi) * 0.5f;
                if (zm.WorldToPx(P.To2D(P.Apparent(new Vector3(m, y, z)))).x < px) lo = m;
                else hi = m;
            }
            return (lo + hi) * 0.5f;
        }

        IEnumerator PanTest()
        {
            PointerInput.SimActive = true;   // (the real pointer is ignored from the start: other windows may share the desktop)
            yield return new WaitForSeconds(2f);
            UnityEngine.Random.InitState(1616);
            PointerInput.SimDpi = 0f;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            var sd = Game.Data;
            sd.sweepHint = sd.tideHint = sd.driftHint = sd.mendHint = sd.sideHint = sd.timeHint = true;
            LegendWatch.DebugMode = null;
            string stage = Arg("-fkstage") ?? "sea";
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Stage.Def.id != stage)
            {
                yield return GoStage(stage, 3f);
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
                PCheck("scene", false, "no fishing scene");
                Application.Quit();
                yield break;
            }
            ctl.Watch?.DebugLurk(null);
            FishingController.NoBites = true;
            // (no snags: a float wound in after a fish lets go far out would catch in the tetrapod field out there, and
            // the view would rightly go out to it instead of home; snags are -fkauto obstacles' business)
            float snagWas = Obstacles.SnagMult;
            Obstacles.SnagMult = 0f;
            var L = ctl.Stage.L;
            var rt = PixelView.Current.Target;
            Log(string.Format(CIp, "[PAN] {0}: screen {1}x{2}, target {3}x{4}, art {5}, overscan {6} px each side, bounds {7}",
                stage, Screen.width, Screen.height, rt.width, rt.height, z.ArtPx, z.OverscanPx, z.BoundsPx));
            PCheck("overscan", z.OverscanPx > 0 && z.OverscanPx == Mathf.Max(0, ((L.widthPx > 0 ? L.widthPx : 640) - rt.width) / 2),
                $"{z.OverscanPx} px each side from the {L.widthPx}x{L.heightPx} art and the {rt.width}x{rt.height} target");
            SteerGear("rod_surf", "reel_highgear", "line_pe3");
            var arg = Arg("-fkzoommode");
            var modes = arg != null ? new[] { ZoomModeArg() } : new[] { ZoomMode.X125, ZoomMode.Off };
            foreach (var m in modes) yield return PanRun(ctl, m);
            sd.zoomMode = (int)(arg != null ? ZoomModeArg() : ZoomMode.X125);
            yield return PanNatural(ctl);
            yield return PanAlign(ctl);
            if (Arg("-fkpanenc") != "off") yield return PanEncounter();

            FishingController.NoBites = false;
            Obstacles.Show = false;
            Obstacles.SnagMult = snagWas;
            Time.timeScale = 1f;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Log($"[PAN] pan test done: {panFails} failed");
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        /// <summary>Hooks a fish on the right at the ready-made rig and returns once the zoom (if any) has settled.</summary>
        IEnumerator PanHook(FishingController ctl, string tag, Vector3 at, string fish, float cm, int seed)
        {
            yield return ToReady(ctl);
            EquipTest("bait_worm", ctl);
            yield return null;
            var z = ZoomNow;
            PCheck(tag + "_ready_home", z.AtHome && z.Level == 0f && z.UV == new Rect(0f, 0f, 1f, 1f), ZDesc(z));
            if (!ctl.DebugPlaceRig(at))
            {
                PCheck(tag + "_rig", false, $"state {ctl.State}");
                yield break;
            }
            for (float w = 0f; w < 3f && ctl.State != FishingController.S.Waiting; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(FishingController.ZoomSetting == ZoomMode.Off ? 0.6f : 1.2f);
            var land = ctl.Tackle.Surface;
            if (!ctl.DebugHook(GameDatabase.GetFish(fish), cm, seed, new Vector3(land.x, -1.2f, land.z)))
                PCheck(tag + "_hook", false, $"state {ctl.State} tackle {ctl.Tackle.State}");
        }

        IEnumerator PanRun(FishingController ctl, ZoomMode mode)
        {
            var sd = Game.Data;
            sd.zoomMode = (int)mode;
            string tag = mode == ZoomMode.Off ? "off" : mode == ZoomMode.X150 ? "150" : mode == ZoomMode.Active ? "active" : "125";
            var z = ZoomNow;
            var L = ctl.Stage.L;
            float W = PixelView.Current.Target.width;
            const float Z = 28f, Y = -1.0f;
            float x0 = XForPx(ctl, W * 0.62f, Y, Z);
            yield return PanHook(ctl, tag, new Vector3(x0, 0f, Z), "mackerel", 36f, 1616);
            if (ctl.State != FishingController.S.Fighting) yield break;
            ctl.Fight.Hold(9999f);
            var st = new PanStats();
            var b = z.BoundsPx;
            // the far points: 24 px inside the art's edges (beyond the home frame by the overscan less that), and a dash
            // out to 10 px past the art's edge (the bounds stop the view there: counted apart)
            float xR = XForPx(ctl, b.xMax - 24f, Y, Z), xL = XForPx(ctl, b.xMin + 24f, Y, Z), xEdge = XForPx(ctl, b.xMax + 10f, Y, Z);
            Log(string.Format(CIp, "[PAN] {0}: fish run x {1:0.0} -> {2:0.0} (px {3:0}) / dash {4:0.0} -> {5:0.0} (px {6:0}) at z {7} m; {8}",
                tag, x0, xR, b.xMax - 24f, xL, xEdge, b.xMax + 10f, Z, ZDesc(z)));

            IEnumerator Move(float from, float to, float speed)
            {
                float t = 0f, dur = Mathf.Abs(to - from) / speed;
                while (t < dur && ctl.State == FishingController.S.Fighting && ctl.Hooked != null)
                {
                    t += Time.deltaTime;
                    float u = Mathf.Clamp01(t / dur);
                    u = u * u * (3f - 2f * u);
                    ctl.DebugFishHold = new Vector3(Mathf.Lerp(from, to, u), Y, Z);
                    yield return new WaitForEndOfFrame();
                    if (ctl.State != FishingController.S.Fighting || ctl.Hooked == null) yield break;
                    PanSample(ctl, st);
                }
            }
            IEnumerator Stay(float x, float dur)
            {
                ctl.DebugFishHold = new Vector3(x, Y, Z);
                for (float t = 0f; t < dur && ctl.State == FishingController.S.Fighting; t += Time.deltaTime)
                {
                    yield return new WaitForEndOfFrame();
                    if (ctl.State != FishingController.S.Fighting || ctl.Hooked == null) yield break;
                    PanSample(ctl, st);
                }
            }

            // the shots: 90 px past the home frame's right edge (the farthest the measured sea runs went), where a 1.25x
            // frame still holds the fish beside the rod tip
            float xShot = XForPx(ctl, W + 90f, Y, Z);
            yield return Stay(x0, 0.5f);
            yield return Move(x0, xShot, 5f);        // a run out past the right edge (5 m/s)
            yield return Stay(xShot, 1.2f);
            if (ctl.State == FishingController.S.Fighting)
            {
                // the same moment without the overscan (the old clamp at the home frame's edge) and with it
                var art = z.ArtPx;
                z.ArtPx = Vector2Int.zero;
                yield return new WaitForSeconds(0.9f);
                yield return PanShot(ctl, $"pan_{tag}_before");
                z.ArtPx = art;
                yield return new WaitForSeconds(1.2f);
                st.has = false;
                st.trail.Clear();
                yield return PanShot(ctl, $"pan_{tag}_after");
                st.has = false;
                st.trail.Clear();
            }
            yield return Move(xShot, xR, 5f);        // on out to near the art's edge (zoomed: too far from the tip, out to 1x)
            yield return Stay(xR, 1.2f);
            yield return Move(xR, xEdge, 12f);       // a dash on past the art's edge (12 m/s)
            yield return Stay(xEdge, 0.6f);
            yield return Move(xEdge, x0, 6f);        // back in
            yield return Stay(x0, 0.8f);
            yield return Move(x0, xL, 6f);           // out past the left edge
            yield return Stay(xL, 1.0f);
            yield return Move(xL, x0, 6f);
            yield return Stay(x0, 0.8f);
            PCheck(tag + "_run", st.frames > 200 && st.beyondHome > 60 && st.maxCam >= z.OverscanPx * 0.6f && st.beyondArt > 0,
                $"the run reached past the home frame and the art: {st}");
            PCheck(tag + "_in_view", st.frames > 200 && st.outFish == 0 && st.outTip == 0, st.ToString());
            PCheck(tag + "_whole_px", st.border == 0 && st.inexact == 0 && st.offWhole == 0, st.ToString());
            PCheck(tag + "_smooth", st.jumps == 0 && st.maxSpeed <= PanSpeedMax,
                string.Format(CIp, "steps up to {0:0.0} px a frame, {1} of them more than {2} px and more than the frame's time allows at {3} px/s; {4:0} px/s over 0.25 s (<= {5})",
                    st.maxStep, st.jumps, PanStepMax, PanFrameSpeed, st.maxSpeed, PanSpeedMax));

            // the end of the fight from out on the right: landed (끔: let go); the view home after it
            yield return Move(x0, xR, 6f);
            yield return Stay(xR, 1.2f);
            float camOut = Mathf.Abs(z.CamPan.x) + Mathf.Abs(z.PanOne.x);
            if (mode == ZoomMode.Off) ctl.DebugRelease();
            else ctl.DebugLand();
            ctl.DebugFishHold = null;
            if (mode == ZoomMode.Off)
            {
                // let go: the float stays out there and is wound in, the view following it back over the overscan
                int rFrames = 0, rigOut = 0;
                var rb = z.BoundsPx;
                for (float tr = 0f; tr < 25f && ctl.State == FishingController.S.Retrieving; tr += Time.deltaTime)
                {
                    yield return new WaitForEndOfFrame();
                    if (ctl.State != FishingController.S.Retrieving || ctl.Tackle.State != Tackle.Mode.Water) continue;
                    rFrames++;
                    var rp = z.WorldToPx(ctl.RigShown2D);
                    if (rp.x > rb.xMin + 1f && rp.x < rb.xMax - 1f && !InCropPx(z, new Vector2(rp.x, Mathf.Clamp(rp.y, z.CropPx.yMin, z.CropPx.yMax)), 0f)) rigOut++;
                }
                PCheck(tag + "_retrieve_follow", rFrames > 30 && rigOut == 0,
                    $"the float wound in from out there: {rFrames} frames, out of view {rigOut}; now {ctl.State}, {ZDesc(z)}");
            }
            // (home from the end of the fight: the landing; let go, from the float back in)
            float t0 = Time.time, homeAt = -1f;
            int maxStepBack = 0, jumpsBack = 0;
            var lastCam = z.CamPan;
            var stepsBack = new System.Text.StringBuilder();
            while (Time.time - t0 < 6f)
            {
                yield return new WaitForEndOfFrame();
                int sb = Mathf.Abs(z.CamPan.x - lastCam.x);
                maxStepBack = Mathf.Max(maxStepBack, sb);
                if (sb > 0 || stepsBack.Length > 0) stepsBack.Append(sb).Append(z.Level > 0f ? "z " : " ");
                if (PanJump(sb, Time.deltaTime))
                {
                    jumpsBack++;
                    // (what moved the camera that frame: the zoom level, the 1x pan, the rod tip and the fish on the home view)
                    var hk = ctl.Hooked;
                    Debug.Log(string.Format(CIp, "[PAN] home jump {0} px at {1:0.000} s (dt {2:0.0000}): level {3:0.000}, camera {4}, 1x pan {5}, state {6}, rod lift {7:0.00}, tip px {8}, fish px {9}",
                        sb, Time.time - t0, Time.deltaTime, z.Level, z.CamPan, z.PanOne, ctl.State, ctl.Angler.RodLift01,
                        z.WorldToPx(ctl.RodTip2D), hk != null ? z.WorldToPx(ctl.Stage.P.To2D(hk.Pos)).ToString() : "-"));
                }
                lastCam = z.CamPan;
                if (homeAt < 0f && z.AtHome && z.Level <= 0f) homeAt = Time.time - t0;
                if (HasButton("판매") || (mode == ZoomMode.Off && ctl.State == FishingController.S.Ready && homeAt >= 0f)) break;
            }
            Debug.Log("[PAN] home steps (px a frame, z = zoomed): " + stepsBack);
            PCheck(tag + "_home", camOut > 0f && homeAt >= 0f && homeAt <= PanHomeMax && jumpsBack == 0,
                string.Format(CIp, "panned {0:0} px when the fight ended ({1}), home (1x, camera and 1x pan at 0) {2:0.00} s after {6}, the camera's steps back up to {3} px a frame ({4} jumps); now {5}",
                    camOut, mode == ZoomMode.Off ? "let go" : "landed", homeAt, maxStepBack, jumpsBack, ZDesc(z),
                    mode == ZoomMode.Off ? "the float came in" : "the landing began"));
            if (HasButton("판매"))
            {
                PCheck(tag + "_card_home", z.AtHome && z.Level == 0f && z.UV == new Rect(0f, 0f, 1f, 1f), ZDesc(z));
                yield return PanShot(ctl, $"pan_{tag}_card");
            }
            yield return ToReady(ctl);
            PCheck(tag + "_ready_home_after", ctl.State == FishingController.S.Ready && z.AtHome && z.Level == 0f, $"state {ctl.State}; {ZDesc(z)}");
        }

        /// <summary>A natural fight hooked far out on the right (the fight model running, wound in like the fish scenario), 40 s at most.</summary>
        IEnumerator PanNatural(FishingController ctl)
        {
            var z = ZoomNow;
            float W = PixelView.Current.Target.width;
            const float Z = 30f;
            float x0 = XForPx(ctl, W + 20f, -1.2f, Z);
            yield return PanHook(ctl, "natural", new Vector3(x0, 0f, Z), "mackerel", 38f, 2002);
            if (ctl.State != FishingController.S.Fighting) yield break;
            var st = new PanStats();
            var c = Scr(0.72f, 0.4f);
            float r = Screen.height * 0.13f, ang = 0f, t = 0f, ws = CircleGesture.Reversed ? -1f : 1f;
            bool winding = false, shot = false;
            while (ctl.State == FishingController.S.Fighting && ctl.Hooked != null && t < 40f)
            {
                float dt = Time.deltaTime;
                t += dt;
                var f = ctl.Fight;
                if (winding && (f.TensionRatio > 0.8f || f.Jumping)) winding = false;
                else if (!winding && f.TensionRatio < 0.55f && !f.Jumping && t > 4f) winding = true;   // (let it run first)
                if (winding) ang -= ws * dt * 1.6f * Mathf.PI * 2f;
                else if (f.TensionRatio > 0.95f) ang += ws * dt * 1.2f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                yield return new WaitForEndOfFrame();
                if (ctl.State != FishingController.S.Fighting || ctl.Hooked == null) break;
                PanSample(ctl, st);
                if (!shot && st.maxOver > 30f && z.WorldToPx(ctl.Fish2D(ctl.Hooked)).x > W + 30f)
                {
                    shot = true;
                    st.has = false;
                    st.trail.Clear();
                    yield return PanShot(ctl, "pan_natural");
                }
            }
            PointerInput.SimDown = false;
            PCheck("natural_in_view", st.frames > 120 && st.outFish == 0 && st.outTip == 0 && st.border == 0 && st.inexact == 0 && st.offWhole == 0,
                $"(the fight ended {ctl.State} after {Z2(t)} s) {st}");
            PCheck("natural_smooth", st.jumps == 0 && st.maxSpeed <= PanSpeedMax, st.ToString());
            Log($"[PAN] natural: the fish went past the home frame in {st.beyondHome} of {st.frames} frames (shot {shot})");
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            // (a float let go out there is followed while it is wound in: home from when it is in)
            for (float tr = 0f; tr < 25f && ctl.State == FishingController.S.Retrieving; tr += Time.deltaTime) yield return null;
            float t0 = Time.time, homeAt = -1f;
            while (Time.time - t0 < 6f && homeAt < 0f)
            {
                yield return new WaitForEndOfFrame();
                if (z.AtHome && z.Level <= 0f) homeAt = Time.time - t0;
                if (HasButton("판매")) break;
            }
            if (HasButton("판매"))
            {
                yield return new WaitForSeconds(0.3f);
                homeAt = z.AtHome && z.Level <= 0f ? Time.time - t0 : -1f;
            }
            PCheck("natural_home", homeAt >= 0f && homeAt <= PanHomeMax + 1f, $"home {Z2(homeAt)} s after the fight; {ZDesc(z)}");
            yield return ToReady(ctl);
        }

        /// <summary>
        /// The camera panned (a fish held far out on the right, the obstacle outlines shown), time frozen: the render target
        /// with the camera at its pan must be the home one shifted by the pan, pixel for pixel, where they overlap.
        /// </summary>
        IEnumerator PanAlign(FishingController ctl)
        {
            var z = ZoomNow;
            var pv = PixelView.Current;
            float W = pv.Target.width;
            const float Z = 26f, Y = -0.8f;
            float x0 = XForPx(ctl, W * 0.6f, Y, Z);
            Game.Data.zoomMode = (int)ZoomMode.Off;
            yield return PanHook(ctl, "align", new Vector3(x0, 0f, Z), "mackerel", 36f, 77);
            if (ctl.State != FishingController.S.Fighting) yield break;
            ctl.Fight.Hold(9999f);
            float xR = XForPx(ctl, z.BoundsPx.xMax - 30f, Y, Z);
            ctl.DebugFishHold = new Vector3(xR, Y, Z);
            Obstacles.Show = true;
            for (float w = 0f; w < 4f && z.CamPan.x < z.OverscanPx * 0.5f; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.8f);
            var pan = z.CamPan;
            float ts = Time.timeScale;
            Time.timeScale = 0f;
            CurrentField.DebugFreeze = true;
            // (the water's effects held as drawn: they recycle what leaves the view even with time frozen, so the camera
            // jumping home would respawn the strip it leaves into the overlap; their sprites stay put in the world)
            var wfx = ctl.Stage.Water;
            bool wfxOn = wfx != null && wfx.enabled;
            if (wfx != null) wfx.enabled = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var a = GrabRT();
            z.Hold(0f, Vector2Int.zero);
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var home = GrabRT();
            z.Hold(0f, pan);
            yield return null;
            yield return new WaitForEndOfFrame();
            if ((Time.frameCount & 1) != 0) yield return new WaitForEndOfFrame();
            var a2 = GrabRT();
            z.Release();
            if (wfx != null) wfx.enabled = wfxOn;
            Time.timeScale = ts;
            CurrentField.DebugFreeze = false;
            if (a == null || home == null || a2 == null || pan.x == 0)
            {
                PCheck("align", false, $"no capture or no pan ({pan})");
            }
            else
            {
                int w = a.width, h = a.height, diff = 0, noise = 0, overlap = 0;
                var pa = a.GetPixels32();
                var ph = home.GetPixels32();
                var p2 = a2.GetPixels32();
                string first = "";
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int hx = x + pan.x, hy = y + pan.y;
                    if (hx < 0 || hx >= w || hy < 0 || hy >= h) continue;
                    var p = pa[y * w + x];
                    var q = p2[y * w + x];
                    if (p.r != q.r || p.g != q.g || p.b != q.b)
                    {
                        noise++;
                        continue;
                    }
                    overlap++;
                    var s = ph[hy * w + hx];
                    if (p.r != s.r || p.g != s.g || p.b != s.b)
                        if (diff++ == 0) first = $" first at ({x},{y}) panned #{p.r:x2}{p.g:x2}{p.b:x2} vs home ({hx},{hy}) #{s.r:x2}{s.g:x2}{s.b:x2}";
                }
                // (a thin diagonal drawn at fractional places, the fishing line, may round one staircase pixel the other way
                // when the camera sits elsewhere: up to 1 in 10000 isolated ties; a layer out of place moves whole edges)
                PCheck("align", overlap > w * h / 2 && diff <= overlap / 10000 && noise < w * h / 50,
                    $"camera panned {pan.x},{pan.y} px: {diff} of {overlap} overlapping px differ from the home target shifted by the pan (<= {overlap / 10000}: rounding ties on the line; frame-to-frame noise {noise} left out){first}");
                File.WriteAllBytes(Path.Combine(shots, "pan_align_panned_rt.png"), a.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(shots, "pan_align_home_rt.png"), home.EncodeToPNG());
            }
            if (a != null) Destroy(a);
            if (home != null) Destroy(home);
            if (a2 != null) Destroy(a2);
            Obstacles.Show = false;
            ctl.DebugFishHold = null;
            ctl.DebugRelease();
            yield return ToReady(ctl);
        }

        /// <summary>
        /// The legend encounter begun with the view panned (the lake, 끔: the rig put out past the home frame's right edge,
        /// the view following it; the legend comes 1 s into the soak, -fkencounter now's way): the camera eases home under
        /// the encounter, and the window must open from where the lure is on the render target now (its crop the
        /// Open phase's ease from the lure's 8 px box to the window, the box at the lure's point less the camera's pan
        /// now), not from where it was when the encounter began; the close (if it turns away) onto the same.
        /// </summary>
        IEnumerator PanEncounter()
        {
            yield return GoStage("lake", 3f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Watch == null || ctl.Watch.Legends.Count == 0)
            {
                PCheck("enc_scene", false, "no lake / no legend");
                yield break;
            }
            var z = ZoomNow;
            var pv = PixelView.Current;
            Game.Data.zoomMode = (int)ZoomMode.Off;
            var sp = ctl.Watch.Legends[0];
            var ed = sp.encounter;
            var key = GameDatabase.GetItem<BaitDef>(ed.keyLures.OrderByDescending(kv => kv.Value).First().Key);
            if (Game.I.Line.strength < ed.minLine)
            {
                var line = GameDatabase.Lines.Where(l => l.strength >= ed.minLine).OrderBy(l => l.strength).FirstOrDefault() ?? GameDatabase.Lines[^1];
                if (!Game.I.Owns(line.id)) Game.Data.ownedItems.Add(line.id);
                Game.I.Equip(line);
            }
            yield return ToReady(ctl);
            if (key.isLure) { if (!Game.I.Owns(key.id)) Game.Data.ownedItems.Add(key.id); }
            else Game.I.AddBait(key.id, 20);
            ctl.EquipBait(key);
            yield return null;
            LegendWatch.ClearCooldowns();
            LegendWatch.DebugMode = "now";
            LegendWatch.DebugLegend = sp.id;
            FishingController.NoBites = false;   // (the watch only soaks with bites on)
            float W = pv.Target.width;
            const float Z = 16f;
            var at = new Vector3(XForPx(ctl, W + 40f, 0f, Z), 0f, Z);
            if (!ctl.DebugPlaceRig(at))
            {
                PCheck("enc_rig", false, $"state {ctl.State}");
                yield break;
            }
            Log(string.Format(CIp, "[PAN] encounter: {0} with {1}, the rig at ({2:0.0}, {3:0.0}) m = home px {4}", sp.id, key.id, at.x, at.z, ZV(z.WorldToPx(ctl.RigShown2D))));
            for (float w = 0f; w < 8f && ctl.State != FishingController.S.Encounter; w += Time.deltaTime) yield return null;
            if (ctl.State != FishingController.S.Encounter)
            {
                PCheck("enc_start", false, $"no encounter (state {ctl.State})");
                LegendWatch.DebugMode = null;
                FishingController.NoBites = true;
                yield break;
            }
            var panStart = z.CamPan;
            var lureWorld = ctl.Stage.P.To2D(ctl.Stage.P.Apparent(ctl.Tackle.HookPos));
            int RW = pv.Target.width, RH = pv.Target.height;
            Vector2 LureNow() => (lureWorld - pv.BaseCenter) * PixelView.PPU + new Vector2(RW * 0.5f, RH * 0.5f) - (Vector2)pv.Pan;
            Vector2 lureStart = LureNow();
            Rect Expect(Rect from, Rect to, float k) => new Rect(Mathf.Round(Mathf.Lerp(from.x, to.x, k)), Mathf.Round(Mathf.Lerp(from.y, to.y, k)),
                Mathf.Max(1f, Mathf.Round(Mathf.Lerp(from.width, to.width, k))), Mathf.Max(0f, Mathf.Round(Mathf.Lerp(from.height, to.height, k))));
            Rect Box(Vector2 p) => new Rect(p.x - 4f, p.y - 4f, 8f, 8f);
            // the crop the view draws (rounded like UpdateCrop) against the one eased from / onto the lure's point now, and
            // the one the lure's point as the encounter began would give
            float RectErr(Rect a, Rect b) => Mathf.Max(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)), Mathf.Max(Mathf.Abs(a.width - b.width), Mathf.Abs(a.height - b.height)));
            int openFrames = 0, closeFrames = 0;
            float openErr = 0f, openOld = 0f, closeErr = 0f, closeOld = 0f, ang = 0f, te = 0f;
            bool shot = false;
            var panAtOpen = Vector2Int.zero;
            var centre = Scr(0.72f, 0.4f);
            float radius = Screen.height * 0.12f, windSign = CircleGesture.Reversed ? -1f : 1f;
            PointerInput.SimActive = true;
            while (ctl.State == FishingController.S.Encounter && te < 60f)
            {
                var e = ctl.Encounter;
                // (the wrong play, so it turns away and closes onto the lure: too fast in the tease, -fkencplay bad's way)
                if (e != null && e.Ph == LegendEncounter.Phase.Tease)
                {
                    var md = e.MoodDef;
                    float fast = md.tooFast < 90f ? md.tooFast + 0.8f : 0f;
                    if (fast > 0f) Circle(ref ang, fast, windSign, centre, radius, Time.deltaTime);
                    else PointerInput.SimDown = false;
                }
                else PointerInput.SimDown = false;
                yield return new WaitForEndOfFrame();
                te += Time.deltaTime;
                var v = ctl.EncounterView;
                if (e == null || v == null || ctl.State != FishingController.S.Encounter) break;
                float k = Mathf.Clamp01(e.PhaseT / Mathf.Max(1e-3f, e.PhaseLen));
                if (e.Ph == LegendEncounter.Phase.Open)
                {
                    if (openFrames++ == 0) panAtOpen = z.CamPan;
                    float ke = 1f - (1f - k) * (1f - k) * (1f - k);
                    var want = Expect(Box(LureNow()), v.Window, ke);
                    openErr = Mathf.Max(openErr, RectErr(v.Crop, want));
                    openOld = Mathf.Max(openOld, RectErr(Expect(Box(lureStart), v.Window, ke), want));
                    if (!shot && k >= 0.25f)
                    {
                        shot = true;
                        yield return PanShot(ctl, "pan_enc_open");
                    }
                }
                else if (e.Ph == LegendEncounter.Phase.Close)
                {
                    closeFrames++;
                    var want = Expect(v.Window, Box(LureNow()), k * k);
                    closeErr = Mathf.Max(closeErr, RectErr(v.Crop, want));
                    closeOld = Mathf.Max(closeOld, RectErr(Expect(v.Window, Box(lureStart), k * k), want));
                }
            }
            PointerInput.SimDown = false;
            PCheck("enc_open_on_lure", panStart.x != 0 && openFrames > 3 && openErr <= 1f,
                string.Format(CIp, "the camera panned {0} px as it began, {1} px as the window opened ({2} Open frames): the crop off the ease from the lure's point now by up to {3:0.0} px (from the lure's point as it began it would be off by {4:0.0} px)",
                    panStart.x, panAtOpen.x, openFrames, openErr, openOld));
            if (closeFrames > 0)
                PCheck("enc_close_on_lure", closeErr <= 1f,
                    string.Format(CIp, "{0} Close frames: the crop off the ease onto the lure's point now by up to {1:0.0} px (onto the point as it began: {2:0.0} px)",
                        closeFrames, closeErr, closeOld));
            else Log("[PAN] encounter: no close (it did not turn away)");
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
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return ToReady(ctl);
        }

        IEnumerator PanShot(FishingController ctl, string name)
        {
            yield return AutoShot.Frame();
            var z = ZoomNow;
            string fish = ctl.Hooked != null ? ZV(z.WorldToPx(ctl.Fish2D(ctl.Hooked))) : "-";
            bool fishIn = ctl.Hooked != null && InCrop(z, ctl.Fish2D(ctl.Hooked), 0f);
            Log(string.Format(CIp, "[PAN] SHOT {0} state {1} fish {2} in view {3} tip {4}; {5}", name, ctl.State, fish, fishIn, ZV(z.WorldToPx(ctl.RodTip2D)), ZDesc(z)));
            string p = Path.Combine(shots, name + ".png");
            AutoShot.Save(p);
            Log("shot " + p);
            yield return null;
        }
    }
}
