using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The top view (<see cref="TopViewDef"/>, <see cref="EncounterDef.teaseView"/> = Top): a topwater legend's tease
    /// watched from above the surface. The encounter opens underwater as usual; over the approach's last
    /// <see cref="TopViewDef.riseT"/> s the camera rises through the waterline (the window splits at a moving waterline:
    /// the underwater view slides down below it, the top view comes down above it), then a camera straight over the lure
    /// shows the tease and the nose-in; the lunge cuts to the full-screen underwater view (the strike from below).
    /// <para>The top compose is a second pixel-art scene (at <see cref="TopOrigin"/>, rendered by the underwater compose's
    /// orthographic camera into its own target and shown by a second crop quad): the murky water (bg, parallax 0.6), the
    /// legend's shadow (the fish camera looking straight down renders the 3D model into its own target, drawn through
    /// EncShadow.shader: darker and crisper near the surface, fainter and softer deep, along the body from the head to the
    /// tail), the surface bulge, the scum lines, the drifting floats, rings, bubbles, the wake, the line, the lure, the
    /// splash and the overhanging branch. The lure stays at <see cref="TopViewDef.lureAt"/> and the water slides past it
    /// as it swims (the camera follows the lure, so the legend's moves relative to it read as they are); it is driven by
    /// the player's input: a wind swims it forward kicking with a V-wake, a 톡 pops it ("퐁": a splash, a ring, a hop),
    /// a pause lets it float while the rings fade.</para>
    /// <para>The legend's moves for this camera: 경계 a slow far pass on a wide flat ellipse, deep and faint (the arapaima's
    /// breath roll gulps at the surface once), 호기심 closer and shallower, circling, bubbles rising over its head, the
    /// head tilting up after a pop; 흥분 hanging head-up right under the lure (the surface bulging and swirling over its
    /// head); the nose-in rises closer (the bulge grows), the tell streams bubbles.</para>
    /// </summary>
    public partial class EncounterView
    {
        /// <summary>The top compose, 1600 px above the underwater one (the orthographic camera sees one at a time).</summary>
        static readonly Vector3 TopOrigin = new Vector3(4000f, 4100f, 0f);
        // sort orders inside the top compose
        const int TBg = 0, TShadow = 1, TEyes = 2, TEyeCore = 3, TBulge = 4, TMid = 5, TFloat = 6, TRing = 7, TBubble = 8,
            TWake = 9, TLine = 10, TFrog = 11, TSplash = 12, TFore = 13;
        // the lure's top frames: the body centre (the pivot) is 5 px under the frame's centre, the line tie 7 px under
        // the pivot; the wake's pivot (the same point) 8 px under its centre
        const float FrogUp = 5f, FrogTie = 7f, WakeUp = 8f;
        // the frog's pace in the top view: full speed up to the calm crank, only 12% of any extra above it, all at 60%
        // of the lure's real travel (≈8 px/s at 0.8 rev/s, ≈12 px/s at a flat-out crank); a 톡 moves it 4 px over 0.25 s
        const float FrogCalmRev = 0.8f, FrogOverRev = 0.12f, FrogPace = 0.6f, FrogJerkPx = 4f, FrogJerkT = 0.25f;
        // one swim stroke (phase 0..1): legs drawn in until StrokeKick (slowing to StrokeRecover of the pace), then the
        // kick's surge StrokeGain × the pace decaying over StrokeGlide of the stroke (averages ≈ 1 × the pace);
        // 2.0 s per stroke at a calm crank, 1.6 s fast; a new wind starts just before a kick
        const float StrokeSlow = 2.0f, StrokeFast = 1.6f, StrokeKick = 0.15f, StrokeRecover = 0.15f, StrokeGain = 3.46f,
            StrokeGlide = 0.3f, StrokeResume = 0.1f;
        float strokePh = StrokeResume;
        static readonly int TintId = Shader.PropertyToID("_Tint"), HeadId = Shader.PropertyToID("_Head"),
            TailId = Shader.PropertyToID("_Tail"), DetailId = Shader.PropertyToID("_Detail");

        enum TopState { Off, Rise, On, Cut }

        TopViewDef top;
        bool topFlow;
        TopState topState;
        float riseK = -1f, uwShiftPx, topShiftPx, splitPx;
        bool riseSplashed;

        RenderTexture rtFishTop, rtTop;
        Transform topRoot;
        MeshRenderer cropTopQuad, shadowQuad;
        Mesh cropTopMesh, shadowMesh;
        Material cropTopMat, shadowMat;
        bool shadowShader;
        Color shadowTint;

        // the top camera (set frame): straight down over the lure; the screen's right / up on the water
        Vector3 tPos, tRight = Vector3.right, tUp = Vector3.forward;
        Quaternion tRot = Quaternion.identity;
        float tF = 140f;
        Vector2 tPP, anchor, frogPx;

        Sprite bgT, midT, foreT, bubbleTop;
        Sprite[] frogT, wakeT, ringT, splashT, bulgeT;
        readonly List<SpriteRenderer> bgTiles = new List<SpriteRenderer>(), midTiles = new List<SpriteRenderer>();
        SpriteRenderer foreSr, frogSr, wakeSr, bulgeSr, eyeTL, eyeTR, eyeTCoreL, eyeTCoreR;
        LineRenderer lineT;

        // the water slides up past the lure as it swims (px; + = up the screen)
        Vector2 waterOff;
        float waterV, jerkLeft, wakeA, frogAnimT, hopT = 9f, settleT = -1f, bubbleNext = 1f, splashAgo = 99f, bulgeA, gulpT = -1f;
        bool wasWinding;
        // the top beat's state: the path angle, the side the legend hangs out to
        int topMood = -1;
        float topTh, topSide = 1f;

        class TopFloat
        {
            public SpriteRenderer sr;
            public Vector2 at, vel;
        }

        readonly List<TopFloat> floats = new List<TopFloat>();

        /// <summary>A splash, ring or bubble in the top compose (px from the anchor; water-borne ones ride the water).</summary>
        class TopFx
        {
            public SpriteRenderer sr;
            public Vector2 q;
            public Sprite[] frames, then;
            public float t, fps, life;
            public bool water, bubble;
            public float wobble;
        }

        readonly List<TopFx> topFx = new List<TopFx>();
        readonly Stack<SpriteRenderer> topPool = new Stack<SpriteRenderer>();

        // ------------------------------------------------------------------ for the HUD and the autopilot
        /// <summary>This encounter shows its tease from above.</summary>
        public bool TopFlow => topFlow;
        /// <summary>The top view is what the window mainly shows now (from half-way through the rise until the cut).</summary>
        public bool TopView => topFlow && (topState == TopState.On || topState == TopState.Rise && riseK >= 0.5f);
        /// <summary>0..1 through the rise through the waterline (-1 = not rising).</summary>
        public float RiseK => riseK;
        /// <summary>Seconds since the lure's last pop in the top view (a "퐁").</summary>
        public float TopSplashAgo => splashAgo;
        bool ShowsUW => !topFlow || topState != TopState.On;
        bool ShowsTop => topFlow && (topState == TopState.Rise || topState == TopState.On);
        float SurfY => float.IsNaN(set.surfaceY) ? LureRest : set.surfaceY;

        // ------------------------------------------------------------------ setup
        /// <summary>The top view's art and objects, when the legend has one and the lure has top frames (else the side view).</summary>
        void InitTop()
        {
            top = def.top;
            if (def.teaseView != TeaseView.Top || top == null || !rollout) return;
            string lid = bait != null ? bait.id.Replace("bait_", "") : "";
            var f0 = TopS($"lure_{lid}_top_0");
            bgT = TopS($"uw_{top.set}_bg");
            if (f0 == null || bgT == null)
            {
                Debug.Log($"[ENC] top view: no {(f0 == null ? $"lure_{lid}_top_0" : $"uw_{top.set}_bg")}, the tease stays underwater");
                return;
            }
            topFlow = true;
            frogT = TopFrames($"lure_{lid}_top_", 4);
            wakeT = TopFrames("fx_top_wake_", 3);
            ringT = TopFrames("fx_top_ring_", 4);
            splashT = TopFrames("fx_top_splash_", 4);
            bulgeT = TopFrames("fx_top_bulge_", 4);
            bubbleTop = TopS("fx_top_bubble");
            midT = TopS($"uw_{top.set}_mid");
            foreT = TopS($"uw_{top.set}_fore");
            // (darkened with the water by the time of day, so it keeps its contrast at dawn, evening and night)
            shadowTint = Art.Hex(top.shadow) * new Color(pTint.r, pTint.g, pTint.b, 1f);
            // the screen's up is away from the angler (against the lure's travel), its right on the water
            var dd = new Vector3(DragDir.x, 0f, DragDir.z).normalized;
            tUp = -dd;
            tRight = new Vector3(tUp.z, 0f, -tUp.x);

            topRoot = new GameObject("EncTop").transform;
            topRoot.SetParent(transform, false);
            for (int i = 0; i < 4; i++) bgTiles.Add(PixelSprite("TopBg", bgT, TBg, topRoot, UwLayer));
            if (midT != null)
                for (int i = 0; i < 4; i++) midTiles.Add(PixelSprite("TopMid", midT, TMid, topRoot, UwLayer));
            if (foreT != null) foreSr = PixelSprite("TopFore", foreT, TFore, topRoot, UwLayer);
            // the floats: every prop of the set once (the small ones first, so they are the ones repeated), 11 in all,
            // scattered round the lure (none on it) and drifting on the current
            var props = new List<Sprite>();
            foreach (var kind in new[] { "leaf", "duck", "twig", "pad" })
                for (int n = 0; n < 6; n++)
                {
                    var s = TopS($"uw_{top.set}_float_{kind}_{n}");
                    if (s == null) break;
                    props.Add(s);
                }
            var rnd = new System.Random(23);
            float rw = WinW + 100f, rh = WinH + 90f;
            for (int i = 0; props.Count > 0 && i < Mathf.Max(11, props.Count); i++)
            {
                var sp = props[i % props.Count];
                Vector2 at = Vector2.zero;
                for (int tries = 0; tries < 40; tries++)
                {
                    at = new Vector2((float)(rnd.NextDouble() - 0.5) * rw, (float)(rnd.NextDouble() - 0.5) * rh);
                    bool ok = at.magnitude > 40f;
                    foreach (var o in floats) ok &= (o.at - at).magnitude > 34f;
                    if (ok) break;
                }
                floats.Add(new TopFloat
                {
                    sr = PixelSprite("TopFloat", sp, TFloat, topRoot, UwLayer), at = at,
                    vel = new Vector2(1.5f + (float)(rnd.NextDouble() - 0.5) * 1.2f, 0.45f + (float)(rnd.NextDouble() - 0.5) * 0.8f),
                });
            }
            // the time of day: the water and its floats tinted like the underwater backdrop (PeriodSetup)
            var tintTop = new Color(pTint.r, pTint.g, pTint.b, 1f);
            foreach (var t in bgTiles) t.color = tintTop;
            foreach (var t in midTiles) t.color = tintTop;
            if (foreSr != null) foreSr.color = tintTop;
            foreach (var o in floats) o.sr.color = tintTop;
            bulgeSr = PixelSprite("TopBulge", bulgeT[0], TBulge, topRoot, UwLayer);
            bulgeSr.enabled = false;
            wakeSr = PixelSprite("TopWake", wakeT[0], TWake, topRoot, UwLayer);
            wakeSr.enabled = false;
            frogSr = PixelSprite("TopLure", frogT[0], TFrog, topRoot, UwLayer);
            var glint = Enc("eyeshine", "eyeshine");
            eyeTL = PixelSprite("TopEyeL", glint, TEyes, topRoot, UwLayer);
            eyeTR = PixelSprite("TopEyeR", glint, TEyes, topRoot, UwLayer);
            eyeTCoreL = PixelSprite("TopEyeCoreL", EyeCoreSprite(), TEyeCore, topRoot, UwLayer);
            eyeTCoreR = PixelSprite("TopEyeCoreR", EyeCoreSprite(), TEyeCore, topRoot, UwLayer);
            eyeTL.enabled = eyeTR.enabled = eyeTCoreL.enabled = eyeTCoreR.enabled = false;

            var lgo = new GameObject("TopLine");
            lgo.transform.SetParent(topRoot, false);
            lgo.layer = UwLayer;
            lineT = lgo.AddComponent<LineRenderer>();
            lineT.sharedMaterial = Angler.LineMaterial;
            lineT.useWorldSpace = true;
            lineT.positionCount = 12;
            lineT.widthMultiplier = 1f / PPU;
            lineT.numCapVertices = 0;
            lineT.sortingOrder = TLine;
            lineT.startColor = lineT.endColor = Art.Hex(set.line, 0.85f);
            lineT.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineT.receiveShadows = false;

            // the legend's shadow: the fish target from above through EncShadow (a plain tinted copy without it)
            var baseMat = Resources.Load<Material>("Models/EncShadow");
            shadowShader = baseMat != null && baseMat.shader != null && baseMat.shader.isSupported;
            shadowMat = shadowShader ? new Material(baseMat) : new Material(Angler.LineMaterial);
            shadowMat.name = "EncShadowMat";
            var sq = new GameObject("TopShadowQuad");
            sq.transform.SetParent(topRoot, false);
            sq.layer = UwLayer;
            shadowQuad = sq.AddComponent<MeshRenderer>();
            shadowMesh = new Mesh { name = "EncShadowQuad" };
            sq.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
            SetupQuad(shadowQuad, shadowMat, TShadow);

            // the second window quad in the pixel view (the top compose)
            var cq = new GameObject("EncCropTop");
            cq.transform.SetParent(transform, false);
            cropTopQuad = cq.AddComponent<MeshRenderer>();
            cropTopMesh = new Mesh { name = "EncCropTop" };
            cq.AddComponent<MeshFilter>().sharedMesh = cropTopMesh;
            cropTopMat = new Material(Angler.LineMaterial) { name = "EncCropTopMat" };
            SetupQuad(cropTopQuad, cropTopMat, OrderCrop);
            Debug.Log($"[ENC] top view: set {top.set}, lure {lid} ({frogT.Length} frames), {floats.Count} floats, mid {(midT != null)} fore {(foreT != null)}, " +
                      $"shadow shader {shadowShader}, cam {top.camHeight:0.0} m {top.surfacePx:0} px/m, rise {top.riseT:0.00} s");
        }

        static Sprite TopS(string name) => Resources.Load<Sprite>("Sprites/Encounter/" + name);

        /// <summary>n frames prefix0..; a missing one repeats the one before it.</summary>
        static Sprite[] TopFrames(string prefix, int n)
        {
            var a = new Sprite[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = TopS(prefix + i);
                if (a[i] == null) a[i] = i > 0 ? a[i - 1] : Art.Pixel;
            }
            return a;
        }

        /// <summary>The top view's render targets and quad (from <see cref="Layout"/>).</summary>
        void LayoutTop()
        {
            if (!topFlow) return;
            rtFishTop = NewRT("EncFishTopRT", 24);
            rtTop = NewRT("EncTopRT", 0);
            shadowMat.mainTexture = rtFishTop;
            cropTopMat.mainTexture = rtTop;
            float hw = w * 0.5f / PPU, hh = h * 0.5f / PPU;
            shadowMesh.Clear();
            shadowMesh.vertices = new[] { new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(-hw, hh, 0), new Vector3(hw, hh, 0) };
            shadowMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            var c = shadowShader ? new Color32(255, 255, 255, 255) : (Color32)new Color(shadowTint.r * 4f, shadowTint.g * 4f, shadowTint.b * 4f, 0.6f);
            shadowMesh.colors32 = new[] { c, c, c, c };
            shadowMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            shadowMesh.bounds = new Bounds(Vector3.zero, new Vector3(hw * 2f, hh * 2f, 1f));
            shadowQuad.transform.position = TopOrigin;
            shadowQuad.enabled = fish != null;
        }

        void ReleaseTop()
        {
            foreach (var rt in new[] { rtFishTop, rtTop })
            {
                if (rt == null) continue;
                rt.Release();
                Destroy(rt);
            }
            rtFishTop = rtTop = null;
        }

        void DestroyTop()
        {
            if (shadowMat != null) Destroy(shadowMat);
            if (cropTopMat != null) Destroy(cropTopMat);
            if (shadowMesh != null) Destroy(shadowMesh);
            if (cropTopMesh != null) Destroy(cropTopMesh);
        }

        /// <summary>The top compose's world point for a render-target pixel.</summary>
        Vector3 UT(Vector2 px) => TopOrigin + new Vector3((px.x - w * 0.5f) / PPU, (px.y - h * 0.5f) / PPU, 0f);

        /// <summary>A set-frame point through the top camera: render-target pixels and the depth along its view.</summary>
        Vector3 ProjTop(Vector3 p)
        {
            var v = Quaternion.Inverse(tRot) * (p - tPos);
            float d = Mathf.Max(0.05f, v.z);
            return new Vector3(tPP.x + tF * v.x / d, tPP.y + tF * v.y / d, v.z);
        }

        Vector2 ProjTop2(Vector3 p)
        {
            var q = ProjTop(p);
            return new Vector2(q.x, q.y);
        }

        /// <summary>A point on a top patrol (semi-axes across / up the screen, depth) around the lure.</summary>
        Vector3 TopPathPoint(Vector3 path, float th)
        {
            var p = lure + tRight * (path.x * Mathf.Cos(th)) + tUp * (path.y * Mathf.Sin(th));
            p.y = SurfY - path.z;
            return p;
        }

        // ------------------------------------------------------------------ the state: underwater, rising, on top, cut
        void UpdateTopState(LegendEncounter.Phase ph)
        {
            riseK = -1f;
            uwShiftPx = topShiftPx = 0f;
            if (!topFlow) return;
            switch (ph)
            {
                case LegendEncounter.Phase.Approach:
                {
                    float from = enc.PhaseLen - top.riseT;
                    if (enc.PhaseT >= from)
                    {
                        topState = TopState.Rise;
                        riseK = Mathf.Clamp01((enc.PhaseT - from) / Mathf.Max(0.05f, top.riseT));
                    }
                    break;
                }
                case LegendEncounter.Phase.Tease:
                case LegendEncounter.Phase.NoseIn:
                    if (topState != TopState.Cut) topState = TopState.On;
                    break;
                case LegendEncounter.Phase.Lunge:
                case LegendEncounter.Phase.HookWindow:
                case LegendEncounter.Phase.Hooked:
                case LegendEncounter.Phase.Surface:
                    topState = TopState.Cut;
                    break;
                // (a turn-away from the tease stays on top; one after a missed hook set stays underwater)
            }
            if (topState != TopState.Rise) return;
            // the window's contents slide down together: the underwater view below the waterline, the top view above it
            float s = Mathf.SmoothStep(0f, 1f, riseK);
            uwShiftPx = Mathf.Round(s * window.height);
            topShiftPx = -(window.height - uwShiftPx);
            if (!riseSplashed && s >= 0.5f)
            {
                riseSplashed = true;
                Sfx.Play(Sfx.Splash, 0.3f, 1.4f);
            }
        }

        // ------------------------------------------------------------------ the legend's moves for the top camera
        /// <summary>
        /// The tease from above: 경계 a slow far pass on the wide flat ellipse (deep, faint; the arapaima's breath roll),
        /// 호기심 circling closer and shallower (the head tilting up after each pop), 흥분 hanging head-up right under the
        /// lure, the body out to the side it came from. The paths are flat so the head stays between the prompt and the
        /// gauge.
        /// </summary>
        void TopTeaseBeat(float dt, int mood, ref Vector3 target, ref float smooth, ref bool hover, ref float tFlare, ref float tPitch,
            ref float tHeadPitch, ref float wantHeadYaw, ref float faceHeading, ref float amp, ref float finHz)
        {
            var li = ctl.LureIn;
            if (mood != topMood)
            {
                // carry on round from where it is
                topMood = mood;
                var path = mood == 0 ? top.waryPath : top.curiousPath;
                var d = fPos - lure;
                float x = Vector3.Dot(d, tRight), y = Vector3.Dot(d, tUp);
                topTh = Mathf.Atan2(y / Mathf.Max(0.1f, path.y), x / Mathf.Max(0.1f, path.x));
                topSide = x >= 0f ? 1f : -1f;
            }
            if (mood <= 1)
            {
                var path = mood == 0 ? top.waryPath : top.curiousPath;
                topTh += (mood == 0 ? top.pathSpeed.x : top.pathSpeed.y) * dt;
                target = TopPathPoint(path, topTh);
                if (mood == 0 && cho.style == ChoreoStyle.Arapaima) target.y = BreathRoll(dt, target.y, ref tPitch);
                smooth = mood == 0 ? 0.9f : 0.6f;
                if (mood == 1)
                {
                    if (li.FlickNow) popLookT = 0f;
                    popLookT += dt;
                    if (popLookT < 1.0f)
                    {
                        tHeadPitch = -20f;
                        HeadTrack(ref wantHeadYaw, 35f);
                    }
                }
                topSide = Vector3.Dot(fPos - lure, tRight) >= 0f ? 1f : -1f;
            }
            else
            {
                hover = true;
                float hd = HeadingOf(-tRight * topSide);
                var nose = new Vector3(lure.x, SurfY - top.hover.x, lure.z) + tRight * (topSide * 0.06f);
                target = RootForNose(nose, hd, top.hover.y);
                faceHeading = hd;
                tPitch = top.hover.y;
                tFlare = 0.5f;
                amp = 0.6f;
                finHz = 0.9f;
                smooth = 0.6f;
            }
        }

        /// <summary>The arapaima's breath roll (once, in 경계 below gauge 40): up to the surface, a gulp, back down.</summary>
        float BreathRoll(float dt, float y, ref float tPitch)
        {
            if (!breathDone && breathT < 0f && patrolT > 2.5f && enc.Gauge < 40f) breathT = 0f;
            if (breathT < 0f) return y;
            breathT += dt;
            float up = breathT < 0.9f ? Mathf.SmoothStep(0f, 1f, breathT / 0.9f) : breathT < 1.3f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (breathT - 1.3f) / 1.1f);
            y = Mathf.Lerp(y, SurfY - 0.05f * fishScale, up);
            tPitch = breathT < 1.3f ? -25f : 10f;
            if (breathT >= 0.9f && breathT - dt < 0.9f)
            {
                var m = Mouth();
                Boil(new Vector3(m.x, SurfY, m.z));
                TopGulp(ProjTop2(new Vector3(m.x, SurfY, m.z)));
                Sfx.Play(Sfx.Bubble, 0.8f, 0.7f);
            }
            if (breathT > 2.4f)
            {
                breathT = -1f;
                breathDone = true;
            }
            return y;
        }

        /// <summary>The nose-in from above: the head rises from the hover to noseIn under the lure; the tell streams bubbles.</summary>
        void TopNoseInBeat(float dt, ref Vector3 target, ref float smooth, ref bool hover, ref float tJaw, ref float tFlare, ref float tDorsal,
            ref float tPitch, ref float faceHeading, ref float amp)
        {
            hover = true;
            smooth = 0.3f;
            amp = 0.5f;
            bool tell = enc.InTell;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(enc.PhaseT / Mathf.Max(0.1f, enc.PhaseLen - def.tell)));
            float depth = Mathf.Lerp(top.hover.x, top.noseIn.x, u), pitch = Mathf.Lerp(top.hover.y, top.noseIn.y, u);
            float hd = HeadingOf(-tRight * topSide);
            var nose = new Vector3(lure.x, SurfY - depth, lure.z) + tRight * (topSide * 0.06f);
            target = RootForNose(nose, hd, pitch);
            faceHeading = hd;
            tPitch = pitch;
            tFlare = tell ? 1f : 0.6f;
            tJaw = tell ? 10f : 2f;
            tDorsal = tell ? 30f : 15f;
            if (tell && (bubbleT -= dt) <= 0f)
            {
                bubbleT = 0.1f;
                var m = Mouth();
                TopBubbles(ProjTop2(new Vector3(m.x, SurfY, m.z)), 1, 4f, 0f);
            }
        }

        // ------------------------------------------------------------------ frame
        /// <summary>The top camera, the compose, the shadow and the render into the top target.</summary>
        void RenderTop(float dt, LegendEncounter.Phase ph)
        {
            if (!ShowsTop) return;
            anchor = new Vector2(Mathf.Round(window.x + top.lureAt.x * window.width), Mathf.Round(window.y + top.lureAt.y * window.height));
            tF = top.surfacePx * top.camHeight;
            tPos = new Vector3(lure.x, SurfY + top.camHeight, lure.z);
            tRot = Quaternion.LookRotation(Vector3.down, tUp);
            tPP = anchor;
            ComposeTop(dt, ph);
            if (fish != null && rtFishTop != null)
            {
                PlaceFishCamera(tPos, tRot, tF, tPP, rtFishTop);
                fishCam.Render();
            }
            uwCam.targetTexture = rtTop;
            uwCam.transform.position = TopOrigin + (Vector3)(uwShake / PPU) + new Vector3(0f, topShiftPx / PPU, -10f);
            uwCam.Render();
            uwCam.targetTexture = rtUW;
        }

        void ComposeTop(float dt, LegendEncounter.Phase ph)
        {
            var li = ctl.LureIn;
            bool live = ph >= LegendEncounter.Phase.Eyes && ph <= LegendEncounter.Phase.NoseIn || ph == LegendEncounter.Phase.TurnAway;
            bool winding = live && li.Winding;
            // the water slides up past the lure as it swims (a 톡 jerks it on 4 px): a frog paddles slowly, so above the
            // calm pace (the good 경계 band tops out at 0.8 rev/s) a faster crank only nudges it a little faster
            float rev = Mathf.Max(0f, li.Speed);
            float paced = Mathf.Min(rev, FrogCalmRev) + Mathf.Max(0f, rev - FrogCalmRev) * FrogOverRev;
            // it swims in strokes: legs drawn in (it slows), a kick, then a long glide with the legs trailing; about one
            // stroke every 2 s (1.6 s on a fast crank), the average pace unchanged
            if (winding) strokePh = (strokePh + dt / Mathf.Lerp(StrokeSlow, StrokeFast, Mathf.InverseLerp(0.3f, 1.2f, rev))) % 1f;
            else strokePh = StrokeResume;
            float surge = strokePh < StrokeKick ? StrokeRecover : StrokeGain * Mathf.Exp(-(strokePh - StrokeKick) / StrokeGlide);
            float want = winding ? paced * top.travel * top.surfacePx * FrogPace * surge : 0f;
            waterV = Mathf.Lerp(waterV, want, 1f - Mathf.Exp(-dt / 0.08f));
            waterOff.y += waterV * dt;
            if (jerkLeft > 0f)
            {
                float k = Mathf.Min(dt, jerkLeft);
                jerkLeft -= dt;
                waterOff.y += FrogJerkPx / FrogJerkT * k;
            }
            // the lure: kicking while it swims, a hop on a pop, floating still
            splashAgo += dt;
            if (live && li.FlickNow) TopPop();
            hopT += dt;
            frogAnimT += dt;
            int fr = 0;
            if (hopT < 0.2f) fr = 3;
            else if (winding) fr = strokePh < StrokeKick ? 1 : 2;   // legs drawn in, then kicked back and trailing in the glide
            float hop = hopT < 0.2f ? 2.5f * Mathf.Sin(hopT / 0.2f * Mathf.PI) : 0f;
            // a gentle weave while it glides (1 px either way, one sway per stroke)
            float weave = winding ? Mathf.Round(Mathf.Sin(strokePh * Mathf.PI * 2f) * 0.8f) : 0f;
            frogPx = anchor + new Vector2(weave, Mathf.Round(hop));
            // it settles: a ring when it stops, one more 1.4 s later if it still floats
            if (wasWinding && !winding)
            {
                settleT = 0f;
                TopRing(frogPx);
            }
            wasWinding = winding;
            if (settleT >= 0f)
            {
                settleT += dt;
                if (settleT >= 1.4f)
                {
                    settleT = -1f;
                    if (!winding && hopT > 0.5f) TopRing(frogPx);
                }
            }
            // the V-wake swells after each kick and fades through the glide
            wakeA = Mathf.MoveTowards(wakeA, winding ? Mathf.Clamp01(0.3f + 0.7f * surge / StrokeGain) : 0f, dt / (winding ? 0.12f : 0.3f));

            // the layers
            var cover = new Rect(window.x - 2f, window.y - 2f, window.width + 4f, window.height + 4f);
            Tile(bgTiles, bgT, waterOff * 0.6f + new Vector2(0.5f, 0.25f) * time, cover);
            if (midT != null) Tile(midTiles, midT, waterOff + new Vector2(1.2f, 0f) * time, cover);
            if (foreSr != null)
            {
                // the overhanging branch, out of focus: only its leafy tip, in the bottom-left corner
                float sway = Mathf.Round(Mathf.Sin(time * Mathf.PI * 2f * 0.2f));
                Place(foreSr, new Vector2(window.xMin + 18f + sway, window.yMin + 4f), true);
            }
            float rw = WinW + 100f, rh = WinH + 90f;
            foreach (var fl in floats)
            {
                var p = fl.at + waterOff + fl.vel * time;
                p.x = Mathf.Repeat(p.x + rw * 0.5f, rw) - rw * 0.5f;
                p.y = Mathf.Repeat(p.y + rh * 0.5f, rh) - rh * 0.5f;
                Place(fl.sr, anchor + p, true);
            }
            wakeSr.enabled = wakeA > 0.01f;
            if (wakeSr.enabled)
            {
                wakeSr.sprite = wakeT[(int)(time * 8f) % wakeT.Length];
                wakeSr.color = new Color(1f, 1f, 1f, wakeA);
                Place(wakeSr, frogPx + new Vector2(0f, WakeUp), true);
            }
            frogSr.sprite = frogT[fr];
            frogSr.enabled = true;
            Place(frogSr, frogPx + new Vector2(0f, FrogUp), true);
            TopLine(winding || hopT < 0.3f);
            TopLegend(dt, ph);
            UpdateTopFx(dt);
        }

        /// <summary>Covers <paramref name="cover"/> with the sprite tiled from <paramref name="off"/> (px).</summary>
        void Tile(List<SpriteRenderer> tiles, Sprite s, Vector2 off, Rect cover)
        {
            float tw = s.rect.width, th = s.rect.height;
            float x0 = Mathf.Round(cover.xMin + Mathf.Repeat(off.x, tw) - tw), y0 = Mathf.Round(cover.yMin + Mathf.Repeat(off.y, th) - th);
            int i = 0;
            for (float y = y0; y < cover.yMax && i < tiles.Count; y += th)
                for (float x = x0; x < cover.xMax && i < tiles.Count; x += tw)
                {
                    if (x + tw <= cover.xMin || y + th <= cover.yMin) continue;
                    var sr = tiles[i++];
                    sr.enabled = true;
                    Place(sr, new Vector2(x + tw * 0.5f, y + th * 0.5f), true);
                }
            for (; i < tiles.Count; i++) tiles[i].enabled = false;
        }

        /// <summary>The line from the lure's nose down out of the window, taut while it is pulled, sagging a little at rest.</summary>
        void TopLine(bool taut)
        {
            var tie = frogPx - new Vector2(0f, FrogTie);
            var dir = top.lineDir.normalized;
            var n = new Vector2(-dir.y, dir.x);
            float sag = taut ? 0f : 2f;
            int cnt = lineT.positionCount;
            for (int i = 0; i < cnt; i++)
            {
                float t = i / (cnt - 1f);
                var p = tie + dir * (170f * t) + n * (sag * 4f * t * (1f - t));
                lineT.SetPosition(i, UT(new Vector2(Mathf.Round(p.x) + 0.5f, Mathf.Round(p.y) + 0.5f)));
            }
        }

        /// <summary>
        /// The legend from above: the shadow's opacity and blur by depth at the head and the tail, the bulge over its head
        /// (흥분: swirling, the nose-in: growing), bubbles over its head (호기심), faint eye glints (brighter with the mood).
        /// </summary>
        void TopLegend(float dt, LegendEncounter.Phase ph)
        {
            if (fish == null) return;
            float sy = SurfY;
            float K(float y) => Mathf.Clamp01(Mathf.Max(0f, sy - y) / Mathf.Max(0.1f, top.shadowDeep));
            var head = fish.HeadPos - SetOrigin;
            var tail = fish.TailPos - SetOrigin;
            float kh = K(head.y), kt = K(tail.y);
            float fade = 1f;
            if (ph == LegendEncounter.Phase.TurnAway) fade = 1f - Mathf.Clamp01((enc.PhaseT - 0.3f) / 0.9f);
            else if (ph == LegendEncounter.Phase.Close || ph == LegendEncounter.Phase.Done) fade = 0f;
            bool shown = bodyShown && fade > 0.001f;
            shadowQuad.enabled = shown;
            if (shown)
            {
                var hp = ProjTop2(head);
                var tp = ProjTop2(tail);
                if (shadowShader)
                {
                    shadowMat.SetColor(TintId, shadowTint);
                    shadowMat.SetVector(HeadId, new Vector4(hp.x, hp.y, Mathf.Lerp(top.shadowAlpha.x, top.shadowAlpha.y, kh) * fade, Mathf.Lerp(top.shadowSoft.x, top.shadowSoft.y, kh)));
                    shadowMat.SetVector(TailId, new Vector4(tp.x, tp.y, Mathf.Lerp(top.shadowAlpha.x, top.shadowAlpha.y, kt) * fade, Mathf.Lerp(top.shadowSoft.x, top.shadowSoft.y, kt)));
                    shadowMat.SetFloat(DetailId, top.shadowDetail * (1f - kh));
                }
            }
            // the surface over its head
            var m = Mouth();
            var hs = ProjTop2(new Vector3(m.x, sy, m.z));
            float wantBulge = 0f;
            int bf = 0;
            bool leaving = enc.Leaving;
            if (ph == LegendEncounter.Phase.Tease && enc.Mood == 2 && !leaving)
            {
                wantBulge = 1f;
                bf = (int)(time * 4f) % 2;
            }
            else if (ph == LegendEncounter.Phase.NoseIn)
            {
                wantBulge = 1f;
                bf = 1 + Mathf.Min(2, (int)(3f * enc.PhaseT / Mathf.Max(0.1f, enc.PhaseLen - 0.05f)));
                bulgeA = 1f;
            }
            if (gulpT >= 0f)
            {
                // the breath roll's gulp: the dome rises and breaks
                gulpT += dt;
                wantBulge = 1f;
                bf = 1 + Mathf.Min(2, (int)(gulpT * 8f));
                if (gulpT > 0.6f) gulpT = -1f;
            }
            bulgeA = Mathf.MoveTowards(bulgeA, wantBulge, dt / (wantBulge > 0f ? 0.5f : 0.3f));
            bulgeSr.enabled = bulgeA > 0.01f && shown;
            if (bulgeSr.enabled)
            {
                bulgeSr.sprite = bulgeT[bf];
                bulgeSr.color = new Color(1f, 1f, 1f, bulgeA);
                Place(bulgeSr, hs, true);
            }
            // 호기심: a few bubbles rise over its head now and then
            if (ph == LegendEncounter.Phase.Tease && enc.Mood == 1 && !leaving)
            {
                bubbleNext -= dt;
                if (bubbleNext <= 0f)
                {
                    bubbleNext = Random.Range(1.6f, 2.4f);
                    TopBubbles(hs, Random.Range(2, 5), 5f, 0.35f);
                }
            }
            else bubbleNext = Mathf.Min(bubbleNext, 0.8f);
            // the eyes: faint glints in the murk, brighter with the mood and at the tell
            float ea = 0f;
            if (ph == LegendEncounter.Phase.Tease) ea = enc.Mood == 0 ? 0f : enc.Mood == 1 ? 0.3f : 0.5f;
            else if (ph == LegendEncounter.Phase.NoseIn) ea = enc.InTell ? 1f : 0.65f;
            else if (ph == LegendEncounter.Phase.TurnAway) ea = 0.3f * (1f - Mathf.Clamp01(enc.PhaseT / 0.8f));
            if (dimEyeT < 0.3f) ea *= 0.5f;
            ea *= (1f - 0.8f * kh) * (eyesShown ? 1f : 0f) * fade;
            bool eyesOn = ea > 0.02f && shown;
            eyeTL.enabled = eyeTR.enabled = eyeTCoreL.enabled = eyeTCoreR.enabled = eyesOn;
            if (eyesOn)
            {
                var a = ProjTop2(fish.EyeL - SetOrigin);
                var b = ProjTop2(fish.EyeR - SetOrigin);
                float d = (b - a).magnitude;
                if (d < 6f)
                {
                    var axis = d > 0.5f ? (b - a) / d : Vector2.right;
                    var c = (a + b) * 0.5f;
                    a = c - axis * 3f;
                    b = c + axis * 3f;
                }
                // (1:1 even at the tell: from above the bulge and the frame's gold carry it)
                eyeTL.color = eyeTR.color = new Color(eyeGlow.r, eyeGlow.g, eyeGlow.b, ea * 0.7f);
                eyeTCoreL.color = eyeTCoreR.color = new Color(eyeCore.r, eyeCore.g, eyeCore.b, ea);
                Place(eyeTL, a, true);
                Place(eyeTR, b, true);
                Place(eyeTCoreL, a, true);
                Place(eyeTCoreR, b, true);
            }
        }

        // ------------------------------------------------------------------ splashes, rings, bubbles
        /// <summary>The lure pops ("퐁"): the spray crown, then a ring, the hop, a jerk forward.</summary>
        void TopPop()
        {
            hopT = 0f;
            jerkLeft = FrogJerkT;
            splashAgo = 0f;
            settleT = -1f;
            var fx = TopSpawn(splashT, frogPx, 12f, TSplash, true);
            fx.then = ringT;
        }

        void TopRing(Vector2 px) => TopSpawn(ringT, px, 10f, TRing, true);

        /// <summary>n bubbles coming up round <paramref name="px"/> (scattered r px, staggered up to <paramref name="stagger"/> s).</summary>
        void TopBubbles(Vector2 px, int n, float r, float stagger)
        {
            if (bubbleTop == null) return;
            for (int i = 0; i < n; i++)
            {
                var fx = TopSpawn(new[] { bubbleTop }, px + new Vector2(Random.Range(-r, r), Random.Range(-r, r)), 0f, TBubble, true);
                fx.bubble = true;
                fx.life = Random.Range(0.4f, 0.8f);
                fx.t = -Random.Range(0f, stagger);
                fx.wobble = Random.Range(0f, 6f);
                fx.sr.enabled = fx.t >= 0f;
            }
        }

        /// <summary>The breath roll's gulp at the surface: the dome breaks, spray, rings, a burst of bubbles.</summary>
        void TopGulp(Vector2 px)
        {
            gulpT = 0f;
            var s = TopSpawn(splashT, px, 10f, TSplash, true);
            s.then = ringT;
            TopSpawn(ringT, px + new Vector2(0f, -3f), 8f, TRing, true);
            TopBubbles(px, 6, 8f, 0.4f);
        }

        TopFx TopSpawn(Sprite[] frames, Vector2 px, float fps, int order, bool water)
        {
            var sr = topPool.Count > 0 ? topPool.Pop() : PixelSprite("TopFx", frames[0], order, topRoot, UwLayer);
            sr.sprite = frames[0];
            sr.sortingOrder = order;
            sr.color = Color.white;
            sr.enabled = true;
            // (kept in water coordinates: it stays where it was made while the water slides past the lure)
            var fx = new TopFx { sr = sr, q = px - anchor - (water ? waterOff : Vector2.zero), frames = frames, fps = fps, water = water };
            topFx.Add(fx);
            return fx;
        }

        void UpdateTopFx(float dt)
        {
            for (int i = topFx.Count - 1; i >= 0; i--)
            {
                var fx = topFx[i];
                fx.t += dt;
                bool done;
                if (fx.t < 0f)
                {
                    fx.sr.enabled = false;
                    continue;
                }
                fx.sr.enabled = true;
                if (fx.bubble)
                {
                    // a bubble bobs on the surface, then pops (one frame of the smallest ring)
                    done = fx.t >= fx.life + 0.1f;
                    fx.sr.sprite = fx.t < fx.life ? bubbleTop : ringT[0];
                    fx.sr.sortingOrder = fx.t < fx.life ? TBubble : TRing;
                }
                else
                {
                    int f = (int)(fx.t * fx.fps);
                    done = f >= fx.frames.Length;
                    if (!done) fx.sr.sprite = fx.frames[f];
                    else if (fx.then != null)
                    {
                        // the splash leaves its ring where it was
                        fx.frames = fx.then;
                        fx.then = null;
                        fx.fps = 10f;
                        fx.t = 0f;
                        fx.sr.sortingOrder = TRing;
                        fx.sr.sprite = fx.frames[0];
                        done = false;
                    }
                }
                if (done)
                {
                    fx.sr.enabled = false;
                    topPool.Push(fx.sr);
                    topFx.RemoveAt(i);
                    continue;
                }
                var p = anchor + fx.q + (fx.water ? waterOff : Vector2.zero);
                if (fx.bubble) p.x += Mathf.Round(Mathf.Sin(time * 5f + fx.wobble));
                Place(fx.sr, p, true);
            }
        }

        // ------------------------------------------------------------------ the HUD's view of it
        /// <summary>The face in the top view (render-target pixels, the rise's shift applied), clipped to the top part.</summary>
        bool TopFace(out Rect r)
        {
            r = default;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in facePts)
            {
                var q = ProjTop(p - SetOrigin);
                x0 = Mathf.Min(x0, q.x);
                y0 = Mathf.Min(y0, q.y);
                x1 = Mathf.Max(x1, q.x);
                y1 = Mathf.Max(y1, q.y);
            }
            float pad = 3f + Mathf.Abs(uwShake.x) + Mathf.Abs(uwShake.y);
            r = Rect.MinMaxRect(x0 - pad, y0 - pad - topShiftPx, x1 + pad, y1 + pad - topShiftPx);
            var part = Rect.MinMaxRect(crop.xMin, splitPx, crop.xMax, crop.yMax);
            r = Rect.MinMaxRect(Mathf.Max(r.xMin, part.xMin), Mathf.Max(r.yMin, part.yMin), Mathf.Min(r.xMax, part.xMax), Mathf.Min(r.yMax, part.yMax));
            return r.width > 0f && r.height > 0f;
        }

        /// <summary>The lure in the top view (render-target pixels): the frog with its kicked legs and its hop.</summary>
        Rect TopLureBox => new Rect(anchor.x - 12f, anchor.y - 10f - topShiftPx, 24f, 31f);
    }
}
