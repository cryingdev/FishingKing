using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// 설정 → 캐스팅 후 줌인 (<see cref="SaveData.zoomMode"/>; 0 is the default, so a save from before the setting reads
    /// 1.25배): <see cref="Off"/> never zooms; <see cref="X125"/> zooms in once the cast lands (the whole-pixel step nearest
    /// 1.25x); <see cref="X150"/> the same with the step nearest 1.5x (falling back to the largest step that keeps the rod tip
    /// and the rig / fish in frame); <see cref="Active"/> stays at 1x while he waits and zooms in quickly (1.25x) as a bite
    /// starts, through the fight it hooks.
    /// </summary>
    public enum ZoomMode { X125 = 0, Off = 1, X150 = 2, Active = 3 }

    /// <summary>
    /// The pixel view's zoom and pan (one per <see cref="PixelView"/>). The world is always rendered whole into the low-res
    /// target; the zoom only changes which part of it the full-screen display shows (the display's UV crop), so everything
    /// drawn in the pixel view (stage layers, actor layers, water, arrows, outlines, the encounter window) zooms together and
    /// the HUD canvases never do. Every screen / world mapping goes through <see cref="UV"/>
    /// (<see cref="PixelView.ScreenToWorld"/>, <see cref="PixelView.WorldToScreen"/>).
    /// <para>Pixel exact at rest: the step is the whole number of screen pixels per game pixel nearest to the zoom asked for
    /// (<see cref="StepAim"/>: <see cref="Aim"/> 1.25x by default, 1080p: 4 -> 5; 1440p: 5.33 -> 7; <see cref="AimWide"/>
    /// 1.5x, 1080p: 4 -> 6) times the base scale, per axis, and the crop's origin is snapped to whole screen pixels, so every
    /// game pixel is exactly n x n screen pixels. Only while easing (<see cref="EaseTime"/>, smoothstep) does it pass
    /// through fractional scales. Asked for more than <see cref="Aim"/>, the step is the largest one (down to
    /// <see cref="Aim"/>'s) whose frame holds every must-see point: it steps down in <see cref="StepDownTime"/> when they
    /// outgrow it and back up once the larger step has held them (with room to spare) for <see cref="StepUpHold"/>.</para>
    /// <para>The pan: <see cref="Director"/> says each frame whether to zoom (<see cref="Want"/>), where to look
    /// (<see cref="Focus"/>, followed with its own smoothing) and which points must stay in frame (<see cref="Keep"/>). The
    /// pan is held at the full step, clamped so the crop never leaves the view's bounds (<see cref="BoundsPx"/>: the render
    /// target, or with the stage art's overscan the art itself: no empty borders); the zoom pivots on it, so every
    /// in-between crop contains the final one.</para>
    /// <para>Overscan (<see cref="ArtPx"/>): the stage art is rendered wider than the render target (every stage's
    /// 640x400 layers against the 480x270 target at 16:9: 80 px each side; the sea's 800 px wide set: 160), so the view
    /// may pan sideways beyond the home 1x frame, over the art the target does not show. All coordinates here stay in
    /// home-view px (the render target with the camera at home, <see cref="WorldToPx"/>); the crop may lie anywhere in
    /// the bounds, and each frame the world camera moves by the whole game px (<see cref="PixelView.Pan"/>, nearest home)
    /// that brings the crop onto the target: the stage layers, the actor layers, the depth maps, the water and the
    /// effects all move with it, and the display shows the crop through it. At 1x the view itself pans (the director's
    /// must-see points beyond the home frame: <see cref="PanOne"/>, an acceleration-limited follow in whole game px that
    /// eases back home once nothing asks for it); zoomed, the crop's own pan goes into the overscan the same way. Without
    /// overscan (the aquarium, or art no wider than the target) the bounds are the target and nothing moves: as before.</para>
    /// </summary>
    [DefaultExecutionOrder(950)] // after the actors' LateUpdates (rod tip, fish, float), before the encounter HUD (1100)
    public class ViewZoom : MonoBehaviour
    {
        /// <summary>The zoom asked for by default, 1.25배 (the nearest whole-pixel step is used).</summary>
        public const float Aim = 1.25f;
        /// <summary>The wide zoom, 1.5배 (the nearest whole-pixel step, or a smaller one down to <see cref="Aim"/>'s that holds the must-see points).</summary>
        public const float AimWide = 1.5f;
        /// <summary>Seconds for a full zoom in or out.</summary>
        public const float EaseTime = 0.6f;
        /// <summary>Seconds: how fast a must-see point outside its margin pulls the pan along.</summary>
        public const float KeepTau = 0.08f;
        /// <summary>Seconds for a step down (the must-see points outgrew the frame at the larger step).</summary>
        public const float StepDownTime = 0.35f;
        /// <summary>Seconds the larger step must hold every must-see point (with room to spare) before it is taken again.</summary>
        public const float StepUpHold = 1f;
        const float FitSlack = 3f, FitSlackUp = 12f;   // game px of room a step must leave to be kept / to be taken again
        const float HoldMargin = 1f;   // game px: a must-see point is held at least this far inside the crop whenever one crop holds them all
        /// <summary>Game px: at 1x the view starts to pan this much before a must-see point reaches its margin (the follow accelerates from rest).</summary>
        public const float OneLead = 12f;
        /// <summary>Seconds: how the 1x pan settles on where it is asked to be.</summary>
        public const float OneTau = 0.25f;
        /// <summary>Game px / s: the 1x pan's top speed (3 px a frame at 60 fps) and its acceleration.</summary>
        public const float OneMaxSpeed = 180f, OneAccel = 700f;
        /// <summary>Game px / s: the 1x pan's top speed and acceleration back home once nothing is kept in frame (the fight is over: home before the catch card).</summary>
        public const float OneReturnSpeed = 320f, OneReturnAccel = 1400f;
        /// <summary>Game px / s: the 1x pan's top speed back home while <see cref="HurryHome"/> (a wind-up: casting is from the home view).</summary>
        public const float OneHurrySpeed = 600f;

        /// <summary>Called every frame before the zoom moves (the fishing controller): calls <see cref="Want"/>, <see cref="Focus"/>, <see cref="Keep"/>.</summary>
        public System.Action<ViewZoom> Director;

        /// <summary>Screen pixels along the top covered by the HUD (the fight strip): must-see points are kept below it.</summary>
        public float TopInsetPx;

        /// <summary>
        /// The stage art's size in game px, centred on the scene origin like the render target at home (zero: none, the
        /// view never leaves the target). The view may pan sideways over the part of it the target does not show
        /// (<see cref="OverscanPx"/>); vertically it stays on the target (a fish below the home frame is under the stand,
        /// hidden by the front layer: Docs/testing.md -fkauto panmeasure).
        /// </summary>
        public Vector2Int ArtPx;

        /// <summary>Set by the director for this frame: the 1x pan goes home at <see cref="OneHurrySpeed"/> (a wind-up).</summary>
        public bool HurryHome;

        /// <summary>The zoom asked for (set by the director every frame; <see cref="Aim"/> unless it says otherwise).</summary>
        public float StepAim = Aim;

        PixelView pv;
        float level, from, to, easeT, easeLen = EaseTime, easeFull = EaseTime;
        Vector2 panF;                 // the crop's centre at the full step (render-target px)
        bool jumpPan, hasFocus, held;
        Vector2 focusPx;
        float focusTau = 0.5f;
        readonly List<Vector3> keeps = new List<Vector3>();   // render-target px + margin
        readonly List<Vector3> softKeeps = new List<Vector3>();   // ... only steering where the view settles
        Rect uv = new Rect(0f, 0f, 1f, 1f), crop;
        Vector2 zoomNow = Vector2.one, stepZoom = Vector2.one, baseScale = Vector2.one, scaleNow = Vector2.one;
        Vector2Int stepPx = Vector2Int.one;
        Vector2 stepNow = Vector2.one, stepFrom = Vector2.one;   // the full zoom shown (easing from one step to another)
        float stepT, stepLen = StepDownTime, upT;
        bool stepEasing;
        int lastTop;
        Vector2 panOne, panOneV;      // the 1x view's offset from home (home-view px) and its speed
        Vector2Int heldCam;           // the camera's pan while a test holds the level

        internal void Init(PixelView v)
        {
            pv = v;
            crop = v.Target != null ? new Rect(0f, 0f, v.Target.width, v.Target.height) : new Rect(0f, 0f, PixelView.BaseWidth, PixelView.BaseHeight);
        }

        // ------------------------------------------------------------------ state
        /// <summary>0 = 1x .. 1 = the step (eased).</summary>
        public float Level => level;
        /// <summary>The zoom asked for is in (it may still be easing).</summary>
        public bool ZoomedIn => to >= 1f;
        public bool Easing => level != to;
        /// <summary>Zoom factor now per axis (1 = the whole target).</summary>
        public Vector2 Zoom => zoomNow;
        /// <summary>The zoom factor of the step per axis (1.25 at 1080p; 1.5 asked wide).</summary>
        public Vector2 StepZoom => stepZoom;
        /// <summary>Whole screen pixels per game pixel at the step.</summary>
        public Vector2Int StepPx => stepPx;
        /// <summary>Easing from one step to another (a step down to fit, back up, or the setting changed).</summary>
        public bool StepEasing => stepEasing;
        /// <summary>The whole screen pixels per game pixel (vertical) the zoom asked for comes to (before any step down to fit).</summary>
        public int StepPxAsked => TopStep;
        /// <summary>The largest step (vertical whole px) whose frame held last frame's must-see points, a little room around each (the step asked for when there were none; one less than the smallest allowed when not even that one did).</summary>
        public int FitStep { get; private set; }
        /// <summary>Screen pixels per game pixel at 1x.</summary>
        public Vector2 BaseScale => baseScale;
        /// <summary>Screen pixels per game pixel now.</summary>
        public Vector2 ScaleNow => scaleNow;
        /// <summary>The display's crop of the render target (UV, 0..1), as drawn this frame (the target as the camera has it now, pan included).</summary>
        public Rect UV => uv;
        /// <summary>The crop in home-view pixels (the render target with the camera at home, bottom-left origin; with overscan it may lie beyond it).</summary>
        public Rect CropPx => crop;
        /// <summary>The crop's centre (home-view px) and where it settles at the full step.</summary>
        public Vector2 PanPx => crop.center;
        public Vector2 PanTargetPx => panF;

        /// <summary>Game px the view may pan beyond the home view to either side (half the art's width beyond the target's; 0 without overscan).</summary>
        public int OverscanPx
        {
            get
            {
                if (pv == null || pv.Target == null || ArtPx.x <= 0) return 0;
                return Mathf.Max(0, (ArtPx.x - pv.Target.width) / 2);
            }
        }

        /// <summary>Where the view may go, in home-view px: the render target at home, widened by <see cref="OverscanPx"/> each side.</summary>
        public Rect BoundsPx
        {
            get
            {
                float W = pv != null && pv.Target != null ? pv.Target.width : PixelView.BaseWidth;
                float H = pv != null && pv.Target != null ? pv.Target.height : PixelView.BaseHeight;
                int m = OverscanPx;
                return new Rect(-m, 0f, W + 2f * m, H);
            }
        }

        /// <summary>The 1x view's offset from home (home-view px, whole px as shown).</summary>
        public Vector2Int PanOne => new Vector2Int(Mathf.RoundToInt(panOne.x), Mathf.RoundToInt(panOne.y));

        /// <summary>The world camera's offset from home this frame (whole game px).</summary>
        public Vector2Int CamPan => pv != null ? pv.Pan : Vector2Int.zero;

        /// <summary>The view is at home: 1x or zoomed, the camera has not moved and the 1x view is not panned.</summary>
        public bool AtHome => CamPan == Vector2Int.zero && PanOne == Vector2Int.zero;

        /// <summary>At the step, whole screen pixels per game pixel and the crop's origin on whole screen pixels.</summary>
        public bool PixelExact
        {
            get
            {
                if (level < 1f || stepEasing) return false;
                float sx = scaleNow.x, sy = scaleNow.y;
                return Mathf.Abs(sx - Mathf.Round(sx)) < 1e-3f && Mathf.Abs(sy - Mathf.Round(sy)) < 1e-3f
                       && Mathf.Abs(crop.x * sx - Mathf.Round(crop.x * sx)) < 1e-2f && Mathf.Abs(crop.y * sy - Mathf.Round(crop.y * sy)) < 1e-2f;
            }
        }

        /// <summary>A pixel-view world point -> home-view pixels (the render target with the unshaken camera at home, not panned).</summary>
        public Vector2 WorldToPx(Vector2 world)
        {
            var rt = pv != null ? pv.Target : null;
            float w = rt != null ? rt.width : PixelView.BaseWidth, h = rt != null ? rt.height : PixelView.BaseHeight;
            var c = pv != null ? pv.BaseCenter : Vector2.zero;
            return (world - c) * PixelView.PPU + new Vector2(w * 0.5f, h * 0.5f);
        }

        // ------------------------------------------------------------------ the director's calls (every frame)
        /// <summary>
        /// Zoom in to the step (true) or out to 1x, easing over <paramref name="time"/> s for the whole way (a part of the
        /// way: that part of it). Asked again for the same way with a shorter time while it eases, the rest of the ease
        /// plays that much faster (winding up to cast hurries a zoom-out along); a longer time never slows it.
        /// </summary>
        public void Want(bool zoomIn, float time = EaseTime)
        {
            float t = zoomIn ? 1f : 0f;
            time = Mathf.Max(0.01f, time);
            if (t == to)
            {
                if (level != to && time < easeFull - 1e-4f)
                {
                    // (the same curve, sped up from here: no jump, no restart)
                    float k = time / easeFull;
                    easeT *= k;
                    easeLen *= k;
                    easeFull = time;
                }
                return;
            }
            if (zoomIn && level <= 0f) jumpPan = true;   // from 1x: straight towards this frame's focus
            from = level;
            to = t;
            easeT = 0f;
            easeFull = time;
            easeLen = Mathf.Max(0.05f, time * Mathf.Abs(to - from));
        }

        /// <summary>Where to look (pixel-view world), followed with this smoothing time (s).</summary>
        public void Focus(Vector2 world, float tau)
        {
            hasFocus = true;
            focusPx = WorldToPx(world);
            focusTau = Mathf.Max(1e-3f, tau);
        }

        /// <summary>
        /// A point that must stay in frame this frame, at least <paramref name="marginPx"/> game pixels inside it (or up to
        /// the bounds' edge when it lies nearer that). <paramref name="soft"/>: it only steers where the view settles
        /// (followed at the focus's pace, and only when it fits with the others), never pulls it along fast. Zoomed out the
        /// same points steer the 1x view's pan over the overscan (<see cref="PanOne"/>); none: it goes home.
        /// </summary>
        public void Keep(Vector2 world, float marginPx, bool soft = false)
        {
            var p = WorldToPx(world);
            (soft ? softKeeps : keeps).Add(new Vector3(p.x, p.y, Mathf.Max(0f, marginPx)));
        }

        /// <summary>
        /// Every must-see point asked for so far this frame (the soft ones too) fits in one frame at the step (asked for
        /// more than <see cref="Aim"/>: at the smallest step it may step down to).
        /// </summary>
        public bool KeepsFit
        {
            get
            {
                if (pv == null || pv.Target == null) return true;
                return FitsAt(FloorStep, 0f);
            }
        }

        /// <summary>
        /// The must-see points asked for so far this frame fit side by side (across, with <paramref name="slack"/> game px
        /// more room each) in one frame at the smallest step it may take. Across only: a fish below the home frame is under
        /// the stand (hidden by the front layer), but one run out over the overscan can lie farther from the rod tip than
        /// a zoomed frame is wide (the director then eases out to 1x, whose view pans to hold them both).
        /// </summary>
        public bool KeepsFitAcross(float slack = 0f)
        {
            if (pv == null || pv.Target == null) return true;
            int ny = FloorStep;
            var n = StepAt(ny);
            var half = new Vector2(pv.Target.width * baseScale.x / n.x, pv.Target.height * baseScale.y / n.y) * 0.5f;
            Range(half, TopInsetPx / Mathf.Max(1, ny), false, true, out var lo, out var hi, slack);
            return lo.x <= hi.x + 1e-3f;
        }

        // ------------------------------------------------------------------ test hooks
        /// <summary>Test: show this level now and ignore the director until <see cref="Release"/> (the camera stays where it is: at 1x the view is the target as the camera has it).</summary>
        internal void Hold(float lvl)
        {
            if (!held) heldCam = CamPan;
            held = true;
            level = from = to = Mathf.Clamp01(lvl);
            stepNow = stepZoom;
            stepEasing = false;
            Apply();
        }

        /// <summary>Test (-fkauto pan): as <see cref="Hold(float)"/> with the camera put at this pan (the crop on its target where it can be).</summary>
        internal void Hold(float lvl, Vector2Int cam)
        {
            held = true;
            heldCam = cam;
            Hold(lvl);
        }

        internal void Release() => held = false;

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (pv == null || pv.Target == null) return;
            Bases();
            if (Director != null && Director.Target is Object o && o == null)
            {
                Director = null;
                ArtPx = Vector2Int.zero;
            }
            HurryHome = false;
            if (!held && Director != null) Director(this);
            float dt = Time.deltaTime;
            if (!held) ChooseStep(dt);
            if (!held && level != to)
            {
                easeT += dt;
                float f = Mathf.Clamp01(easeT / easeLen);
                level = f >= 1f ? to : Mathf.Lerp(from, to, f * f * (3f - 2f * f));
            }
            if (!held && stepEasing)
            {
                stepT += dt;
                float f = Mathf.Clamp01(stepT / stepLen);
                stepEasing = f < 1f;
                stepNow = stepEasing ? Vector2.Lerp(stepFrom, stepZoom, f * f * (3f - 2f * f)) : stepZoom;
            }
            // the pan, at the full step
            float W = pv.Target.width, H = pv.Target.height;
            var half = HalfAtStep;
            float topIn = TopInAtStep;
            if (hasFocus)
            {
                var tgt = Constrain(Clamp(focusPx, half), half, topIn, true);
                panF = jumpPan ? tgt : Vector2.Lerp(panF, tgt, 1f - Mathf.Exp(-dt / focusTau));
                jumpPan = false;
            }
            if (keeps.Count > 0)
            {
                panF = Vector2.Lerp(panF, Constrain(panF, half, topIn, false), 1f - Mathf.Exp(-dt / KeepTau));
                panF = HoldIn(panF, half);
            }
            panF = Clamp(panF, half);
            if (!held) FollowOne(dt, W);
            hasFocus = false;
            keeps.Clear();
            softKeeps.Clear();
            Apply();
        }

        /// <summary>
        /// The 1x view's pan (horizontal, over the overscan only): to the offset nearest home that keeps every must-see
        /// point its margin (+ <see cref="OneLead"/>) inside the home-sized view, soft ones too when they fit; home when
        /// there are none. Followed at <see cref="OneTau"/>, no faster than <see cref="OneMaxSpeed"/>, accelerating and
        /// braking at <see cref="OneAccel"/> (home with nothing kept: <see cref="OneReturnSpeed"/>; a wind-up:
        /// <see cref="OneHurrySpeed"/>); a hard point that would still leave the view pulls it along at once, by up to
        /// <see cref="OneMaxSpeed"/> more (so the view never moves faster than twice that, and a point that turns up
        /// outside, a rig landed or snagged out there, is glided to).
        /// </summary>
        void FollowOne(float dt, float W)
        {
            float m = OverscanPx;
            float tgt = 0f;
            if (m > 0f && keeps.Count + softKeeps.Count > 0)
            {
                OneRange(keeps, W, OneLead, out float lo, out float hi);
                if (softKeeps.Count > 0)
                {
                    OneRange(softKeeps, W, OneLead, out float slo, out float shi);
                    float a = Mathf.Max(lo, slo), b = Mathf.Min(hi, shi);
                    if (a <= b && a <= m && b >= -m)
                    {
                        lo = a;
                        hi = b;
                    }
                }
                tgt = lo <= hi ? Mathf.Clamp(0f, lo, hi) : (lo + hi) * 0.5f;
                tgt = Mathf.Clamp(tgt, -m, m);
            }
            bool going = keeps.Count + softKeeps.Count == 0;   // (nothing kept: back home, briskly)
            float vmax = HurryHome ? OneHurrySpeed : going ? OneReturnSpeed : OneMaxSpeed;
            float acc = HurryHome ? OneAccel * 3f : going ? OneReturnAccel : OneAccel;
            float err = tgt - panOne.x;
            float want = Mathf.Clamp(err / OneTau, -vmax, vmax);
            float brake = Mathf.Sqrt(2f * acc * Mathf.Abs(err));
            want = Mathf.Clamp(want, -brake, brake);
            panOneV.x = Mathf.MoveTowards(panOneV.x, want, acc * dt);
            panOne.x += panOneV.x * dt;
            if (Mathf.Abs(tgt - panOne.x) < 0.5f && Mathf.Abs(panOneV.x) < 10f)
            {
                panOne.x = tgt;
                panOneV.x = 0f;
            }
            if (m > 0f && keeps.Count > 0 && dt > 0f)
            {
                // (a hard point never leaves the view: the follow lagging behind a fast run is pulled along, by up to the
                // follow's own top speed more a frame; a point already well outside, a rig landed or snagged out there, is
                // glided to, not snapped)
                OneRange(keeps, W, HoldMargin, out float lo, out float hi);
                if (lo <= hi)
                {
                    float pull = Mathf.Clamp(Mathf.Clamp(panOne.x, lo, hi) - panOne.x, -OneMaxSpeed * dt, OneMaxSpeed * dt);
                    // (its speed is then the view's real speed this frame: the follow carries on from it)
                    if (pull != 0f) panOneV.x = Mathf.Clamp(panOneV.x + pull / dt, -2f * OneMaxSpeed, 2f * OneMaxSpeed);
                    panOne.x += pull;
                }
            }
            panOne.x = Mathf.Clamp(panOne.x, -m, m);
            panOne.y = 0f;
        }

        /// <summary>The 1x view's offsets (home-view px) that keep these points their margin + <paramref name="lead"/> inside it.</summary>
        static void OneRange(List<Vector3> ks, float W, float lead, out float lo, out float hi)
        {
            lo = float.MinValue;
            hi = float.MaxValue;
            foreach (var k in ks)
            {
                float mg = Mathf.Min(k.z + lead, W * 0.4f);
                if (lead <= HoldMargin) mg = Mathf.Min(lead, k.z);
                lo = Mathf.Max(lo, k.x + mg - W);
                hi = Mathf.Min(hi, k.x - mg);
            }
        }

        /// <summary>Half the crop at the full step shown (render-target px; the step while none is easing).</summary>
        Vector2 HalfAtStep => new Vector2(pv.Target.width / stepNow.x, pv.Target.height / stepNow.y) * 0.5f;

        /// <summary>The HUD's top inset in render-target px at the full step shown.</summary>
        float TopInAtStep => TopInsetPx / Mathf.Max(1f, stepNow.y * baseScale.y);

        /// <summary>
        /// The base scale for this screen and target, and the step's whole pixels per axis (the vertical one chosen by
        /// <see cref="ChooseStep"/>; the horizontal one nearest to the same zoom).
        /// </summary>
        void Bases()
        {
            float W = pv.Target.width, H = pv.Target.height;
            baseScale = new Vector2(Mathf.Max(1, Screen.width) / W, Mathf.Max(1, Screen.height) / H);
            if (stepPx == Vector2Int.one) SetStep(TopStep, 0f);
            else
            {
                // (the screen may have changed: the same vertical step, its zoom and the horizontal one again)
                stepPx = StepAt(stepPx.y);
                stepZoom = new Vector2(stepPx.x / baseScale.x, stepPx.y / baseScale.y);
                if (!stepEasing) stepNow = stepZoom;
            }
        }

        Vector2Int StepAt(int ny) => new Vector2Int(StepOf(baseScale.x, ny / baseScale.y), ny);

        /// <summary>Goes to the step with <paramref name="ny"/> whole screen px per game px, easing over <paramref name="time"/> s (0: at once).</summary>
        void SetStep(int ny, float time)
        {
            var n = StepAt(ny);
            if (n == stepPx && (time > 0f || !stepEasing)) return;
            stepPx = n;
            stepZoom = new Vector2(n.x / baseScale.x, n.y / baseScale.y);
            if (time <= 0f || level <= 0f)
            {
                stepNow = stepZoom;
                stepEasing = false;
                return;
            }
            stepFrom = stepNow;
            stepT = 0f;
            stepLen = time;
            stepEasing = true;
        }

        /// <summary>
        /// The step for this frame: the one asked for (<see cref="StepAim"/>); asked for more than <see cref="Aim"/>, the
        /// largest step down to <see cref="Aim"/>'s whose frame holds this frame's must-see points (with a little room). At
        /// 1x it is taken at once (the zoom-in goes straight for it); zoomed, a step down eases in
        /// <see cref="StepDownTime"/>, a step back up waits until the larger one has held them with room to spare for
        /// <see cref="StepUpHold"/> (a changed setting: at once) and eases in <see cref="EaseTime"/>; zooming out it stays.
        /// </summary>
        void ChooseStep(float dt)
        {
            int top = TopStep, floor = FloorStep;
            bool asked = top != lastTop;
            lastTop = top;
            int cur = stepPx.y, n = top, fit = top;
            bool atOne = level <= 0f;
            if (keeps.Count + softKeeps.Count > 0)
            {
                while (fit > floor && !FitsAt(fit, FitSlack)) fit--;
                n = Mathf.Min(fit, cur);
                // (a larger step than the one shown only with room to spare)
                for (int k = top; k > cur; k--)
                    if (k <= fit && FitsAt(k, FitSlackUp))
                    {
                        n = k;
                        break;
                    }
                if (atOne) n = fit;
            }
            FitStep = fit == floor && keeps.Count + softKeeps.Count > 0 && !FitsAt(floor, FitSlack) ? floor - 1 : fit;
            if (atOne)
            {
                upT = 0f;
                SetStep(n, 0f);
                return;
            }
            if (to <= 0f)
            {
                upT = 0f;
                return;
            }
            if (n < cur)
            {
                upT = 0f;
                SetStep(n, asked ? EaseTime : StepDownTime);
            }
            else if (n > cur)
            {
                upT += dt;
                if (asked || upT >= StepUpHold)
                {
                    upT = 0f;
                    SetStep(n, EaseTime);
                }
            }
            else upT = 0f;
        }

        /// <summary>This frame's must-see points (the soft ones too) fit in one frame at the step of <paramref name="ny"/> whole px, each with <paramref name="slack"/> game px more room.</summary>
        bool FitsAt(int ny, float slack)
        {
            var n = StepAt(ny);
            var half = new Vector2(pv.Target.width * baseScale.x / n.x, pv.Target.height * baseScale.y / n.y) * 0.5f;
            return Range(half, TopInsetPx / Mathf.Max(1, ny), true, true, out _, out _, slack);
        }

        /// <summary>The step asked for (vertical whole px): nearest to <see cref="StepAim"/>; more than <see cref="Aim"/> asked, at least one more than Aim's (4:3 1080p: 3 x 1.5 = 4.5 -> 5, not 4).</summary>
        int TopStep
        {
            get
            {
                int t = StepOf(baseScale.y, StepAim);
                return StepAim > Aim + 1e-3f ? Mathf.Max(t, StepOf(baseScale.y, Aim) + 1) : t;
            }
        }

        /// <summary>The smallest step it may step down to (Aim's, or the one asked for when that is less).</summary>
        int FloorStep => Mathf.Min(TopStep, StepOf(baseScale.y, Aim));

        /// <summary>The whole number of screen pixels per game pixel nearest to <paramref name="s"/> x <paramref name="z"/> (always more than 1x).</summary>
        static int StepOf(float s, float z) => Mathf.Max(Mathf.RoundToInt(s * z), Mathf.FloorToInt(s + 1e-3f) + 1);

        /// <summary>The pan (a crop's centre, <paramref name="half"/> its half size) kept so the crop stays in the bounds.</summary>
        Vector2 Clamp(Vector2 c, Vector2 half)
        {
            var b = BoundsPx;
            return new Vector2(Mathf.Clamp(c.x, b.xMin + half.x, Mathf.Max(b.xMin + half.x, b.xMax - half.x)),
                Mathf.Clamp(c.y, b.yMin + half.y, Mathf.Max(b.yMin + half.y, b.yMax - half.y)));
        }

        /// <summary>
        /// The pan nearest <paramref name="c"/> that has every must-see point inside its margins (the top also below the
        /// HUD); <paramref name="withSoft"/>: the soft ones too, when they fit with the others.
        /// </summary>
        Vector2 Constrain(Vector2 c, Vector2 half, float topIn, bool withSoft)
        {
            // (the soft ones only when a frame on the target holds them with the others)
            withSoft &= softKeeps.Count > 0 && Range(half, topIn, true, true, out _, out _);
            if (keeps.Count == 0 && !withSoft) return c;
            // (the full margins even at the target's edge: the pan goes for the edge and the clamp stops it there exactly)
            Range(half, topIn, withSoft, false, out var lo, out var hi);
            c.x = lo.x <= hi.x ? Mathf.Clamp(c.x, lo.x, hi.x) : (lo.x + hi.x) * 0.5f;
            c.y = lo.y <= hi.y ? Mathf.Clamp(c.y, lo.y, hi.y) : (lo.y + hi.y) * 0.5f;
            return c;
        }

        /// <summary>
        /// When the margins cannot all be kept (the rod tip and a fish far apart: the pull settles between them, short of
        /// both), the must-see points themselves still stay in the crop, <see cref="HoldMargin"/> inside, per axis wherever
        /// one crop holds them (the HUD strip may then cover the rod tip): at once, so a point never slips out at the edge.
        /// </summary>
        Vector2 HoldIn(Vector2 c, Vector2 half)
        {
            var lo = new Vector2(float.MinValue, float.MinValue);
            var hi = new Vector2(float.MaxValue, float.MaxValue);
            foreach (var k in keeps)
            {
                float m = Mathf.Min(HoldMargin, k.z);
                lo = Vector2.Max(lo, new Vector2(k.x + m - half.x, k.y + m - half.y));
                hi = Vector2.Min(hi, new Vector2(k.x - m + half.x, k.y - m + half.y));
            }
            if (lo.x <= hi.x) c.x = Mathf.Clamp(c.x, lo.x, hi.x);
            if (lo.y <= hi.y) c.y = Mathf.Clamp(c.y, lo.y, hi.y);
            return c;
        }

        /// <summary>
        /// The pans (the crop's centre at the step, per axis lo..hi) that keep the must-see points inside their margins (the
        /// top also below the HUD); false when there is none. <paramref name="onTarget"/>: only pans keeping the crop in the
        /// bounds (the target, or the art with overscan), and no margin asked past their edge (where the crop stops): whether
        /// a frame can hold them. <paramref name="slack"/>: that much more room around each point (whether a step holds them
        /// with room to spare).
        /// </summary>
        bool Range(Vector2 half, float topIn, bool withSoft, bool onTarget, out Vector2 lo, out Vector2 hi, float slack = 0f)
        {
            var b = BoundsPx;
            lo = onTarget ? b.min + half : new Vector2(float.MinValue, float.MinValue);
            hi = onTarget ? Vector2.Max(b.min + half, b.max - half) : new Vector2(float.MaxValue, float.MaxValue);
            topIn = Mathf.Min(topIn, half.y * 0.5f);
            Narrow(keeps, half, topIn, onTarget, b, slack, ref lo, ref hi);
            if (withSoft) Narrow(softKeeps, half, topIn, onTarget, b, slack, ref lo, ref hi);
            return lo.x <= hi.x + 1e-3f && lo.y <= hi.y + 1e-3f;
        }

        static void Narrow(List<Vector3> ks, Vector2 half, float topIn, bool onTarget, Rect b, float slack, ref Vector2 lo, ref Vector2 hi)
        {
            foreach (var k in ks)
            {
                float mx = Mathf.Min(k.z, half.x * 0.8f) + slack, my = Mathf.Min(k.z, half.y * 0.4f) + slack;
                float left = mx, right = mx, below = my, above = my + topIn;
                if (onTarget)
                {
                    left = Mathf.Clamp(left, 0f, k.x - b.xMin);
                    right = Mathf.Clamp(right, 0f, b.xMax - k.x);
                    below = Mathf.Clamp(below, 0f, k.y - b.yMin);
                    above = Mathf.Clamp(above, 0f, b.yMax - k.y);
                }
                lo.x = Mathf.Max(lo.x, k.x + right - half.x);
                hi.x = Mathf.Min(hi.x, k.x - left + half.x);
                lo.y = Mathf.Max(lo.y, k.y + above - half.y);
                hi.y = Mathf.Min(hi.y, k.y - below + half.y);
            }
        }

        /// <summary>The crop for this level and pan, onto the display.</summary>
        void Apply()
        {
            if (pv == null || pv.Target == null) return;
            if (stepPx == Vector2Int.one) Bases();
            float W = pv.Target.width, H = pv.Target.height;
            float zx = 1f + level * (stepNow.x - 1f), zy = 1f + level * (stepNow.y - 1f);
            zoomNow = new Vector2(zx, zy);
            var size = new Vector2(W / zx, H / zy);
            var b = BoundsPx;
            // the 1x view's centre: home, panned over the overscan (whole px: at 1x the camera itself is the view); a test's
            // hold keeps the camera where it was
            var one = held ? heldCam : PanOne;
            var C = new Vector2(W, H) * 0.5f + new Vector2(one.x, one.y);
            // the zoom pivots on the pan: offset from the centre grows as (1 - 1/z), reaching the pan at the full step
            float kx = stepNow.x > 1f ? (1f - 1f / zx) / (1f - 1f / stepNow.x) : 0f;
            float ky = stepNow.y > 1f ? (1f - 1f / zy) / (1f - 1f / stepNow.y) : 0f;
            var c = C + new Vector2((panF.x - C.x) * kx, (panF.y - C.y) * ky);
            var o = new Vector2(Mathf.Clamp(c.x - size.x * 0.5f, b.xMin, b.xMax - size.x), Mathf.Clamp(c.y - size.y * 0.5f, b.yMin, b.yMax - size.y));
            if (level >= 1f && !stepEasing)
            {
                // at rest: the crop's origin on whole screen pixels (every game pixel n x n)
                int sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
                int bx0 = Mathf.RoundToInt(b.xMin * stepPx.x), by0 = Mathf.RoundToInt(b.yMin * stepPx.y);
                int ox = Mathf.Clamp(Mathf.RoundToInt(o.x * stepPx.x), bx0, Mathf.Max(bx0, Mathf.RoundToInt(b.xMax * stepPx.x) - sw));
                int oy = Mathf.Clamp(Mathf.RoundToInt(o.y * stepPx.y), by0, Mathf.Max(by0, Mathf.RoundToInt(b.yMax * stepPx.y) - sh));
                o = new Vector2(ox / (float)stepPx.x, oy / (float)stepPx.y);
                size = new Vector2(sw / (float)stepPx.x, sh / (float)stepPx.y);
            }
            crop = new Rect(o, size);
            // the camera: the whole game px nearest home whose target holds the crop (moving the camera by whole px and
            // the crop on the target back by as much shows the same screen: only the art beyond the home view comes in)
            var cam = held ? heldCam : new Vector2Int(CamAxis(o.x, size.x, W, b.xMin, b.xMax), CamAxis(o.y, size.y, H, b.yMin, b.yMax));
            pv.SetPan(cam);
            var ot = new Vector2(Mathf.Clamp(o.x - cam.x, 0f, Mathf.Max(0f, W - size.x)), Mathf.Clamp(o.y - cam.y, 0f, Mathf.Max(0f, H - size.y)));
            var r = new Rect(ot.x / W, ot.y / H, size.x / W, size.y / H);
            if (level <= 0f) r = new Rect(0f, 0f, 1f, 1f);
            uv = r;
            scaleNow = new Vector2(Mathf.Max(1, Screen.width) / size.x, Mathf.Max(1, Screen.height) / size.y);
            pv.SetDisplayUV(uv);
        }

        /// <summary>
        /// The camera's whole-px offset on one axis whose target ([cam, cam + <paramref name="full"/>]) holds the crop
        /// [<paramref name="o"/>, o + <paramref name="s"/>] and stays in the bounds: the one nearest home (0); when none
        /// holds it exactly (easing, the crop within a pixel of the target's size), the nearest.
        /// </summary>
        static int CamAxis(float o, float s, float full, float bMin, float bMax)
        {
            int lo = Mathf.CeilToInt(o + s - full - 1e-3f), hi = Mathf.FloorToInt(o + 1e-3f);
            int bl = Mathf.CeilToInt(bMin - 1e-3f), bh = Mathf.Max(bl, Mathf.FloorToInt(bMax - full + 1e-3f));
            lo = Mathf.Max(lo, bl);
            hi = Mathf.Min(hi, bh);
            if (lo > hi) return Mathf.Clamp(Mathf.RoundToInt(o + (s - full) * 0.5f), bl, bh);
            return Mathf.Clamp(0, lo, hi);
        }
    }
}
