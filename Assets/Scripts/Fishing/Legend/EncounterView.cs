using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The legend encounter on screen (Docs/lures_legend_spec.md 2.4-2.7, Docs/legends_rollout.md 1.4, 1.5, 3, 4): an
    /// underwater window over the dimmed surface scene that opens out of the lure, grows to the whole view at the bite and
    /// is wiped away bottom-up (the waterline) back to the surface fight, or shrinks back into the lure when the legend
    /// turns away.
    /// <para>The underwater scene is composed off screen. A perspective camera (<see cref="FishLayer"/>, off-centre
    /// projection: focal <c>f</c> px, principal point at the window centre) renders the 3D legend (<see cref="Legend3D"/>,
    /// (a topwater legend's tease is watched from above instead: EncounterView.Top.cs)
    /// lit by the lure: ActorToon's lure light, plus the sun in a daylight set) into a transparent render target. An
    /// orthographic camera (<see cref="UwLayer"/>) then renders the pixel-art set into a second target of the pixel view's
    /// size: the backdrop layers of the legend's <see cref="EncounterSetDef"/> (bg / ceiling / god-rays / mid / floor /
    /// the ice's light column / fore, placed on the horizon the 3D camera sees and shifted with its yaw for parallax), the
    /// Eyes beat's veil, the lure's halo, the fish layer (the fish target through the set's rim material), the lure and
    /// the line, bubbles, silt, marine snow and the eyeshine. That target is shown in the pixel view by one quad whose
    /// vertices are the crop rectangle and whose UVs are the crop over the target size, so every pixel stays 1:1 however
    /// the crop grows.</para>
    /// <para>The set frame: the lure's rest point is the origin (x, z) and y follows the set (the floor at y = 0 in the
    /// cave, lake and ice; the surface film at the lure in the swamp; open water in the ocean); world =
    /// <see cref="SetOrigin"/> + set. The choreography (eyes in the dark, the approach, the moods' patrols, the hover,
    /// the fake-out, the lunge and bite, the turn away) moves the fish's root towards per-phase targets by the legend's
    /// <see cref="Choreo"/> style; the camera follows the lure. The coelacanth keeps its original code path.</para>
    /// </summary>
    [DefaultExecutionOrder(1000)] // after PixelView (camera position) and the controller (this frame's input)
    public partial class EncounterView : MonoBehaviour
    {
        public const int UwLayer = 27, FishLayer = 28;
        static readonly Vector3 SetOrigin = new Vector3(0f, -200f, 0f);
        static readonly Vector3 UwOrigin = new Vector3(4000f, 4000f, 0f);
        const float PPU = PixelView.PPU;
        // pixel view sort orders (the dim goes under the line, rod and angler; the window over everything)
        const int OrderDim = 45, OrderCrop = 91, OrderFrame = 94, OrderTimer = 95, OrderTop = 96;
        // sort orders inside the underwater compose (only their order matters)
        const int UBg = 0, UCeil = 1, URay = 2, UMid = 3, UFloor = 4, URayCol = 5, USnowFar = 6, UFore = 7, UVeil = 8, UHalo = 9,
            ULureBack = 10, UFish = 13, ULine = 14, ULureFront = 15, UFx = 17, UEyes = 19, UEyeCore = 20, USnowNear = 21;

        // window (spec 2.4)
        const int WinW = 288, WinH = 136, WinTop = 10;
        // excited / nose-in: the fish hovers on this side of the lure (right of it, a little towards the camera), side-on
        static readonly Vector3 SideDir = new Vector3(0.883f, 0f, -0.469f);
        const float GrowT = 0.3f;                           // the window grows to full screen over this long at the lunge

        public static EncounterView Current { get; private set; }

        FishingController ctl;
        LegendEncounter enc;
        EncounterDef def;
        EncounterSetDef set;
        Choreo cho;
        StageView stage;
        PixelView pv;
        BaitDef bait;
        Legend3D fish;
        float fishScale = 1.6f;
        bool rollout;                         // a rollout legend (not the coelacanth's original path)

        Camera fishCam, uwCam;
        RenderTexture rtFish, rtUW;
        int w, h;
        Transform setRoot, uwRoot;
        MeshRenderer cropQuad, fishQuad;
        Mesh cropMesh, fishMesh;
        Material cropMat, fishMat;
        float rimPreset;
        static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");

        SpriteRenderer dim, frame, timer, flashSr, waterline;
        SpriteRenderer bg, ceilSr, ceilFillSr, mid, floorSr, fore, halo, lureSr, eyeSrL, eyeSrR, eyeCoreL, eyeCoreR, veil;
        readonly List<SpriteRenderer> rays = new List<SpriteRenderer>();
        // the ice's light column: a mesh (a sprite can't shear) over uw_<id>_ray, its art's uv rect and the sheared ends
        MeshRenderer rayCol;
        Mesh rayColMesh;
        Material rayColMat;
        Rect rayColUv;
        int rayColW;
        Vector2 rayColTop, rayColBot;
        LineRenderer line;
        Sprite[] lureFrames, silt;
        Sprite bubbleS, bubbleM;
        Color eyeCore, eyeGlow, abyss, fogOutline, abyssNear, fogNear, lineCol;

        // geometry (render-target pixels, origin bottom-left)
        Rect window, crop;
        Vector2 ppWin;
        float f0;
        Vector2 lureSurf;                    // the lure's apparent surface point on the render target (this frame)
        Vector2 lureWorld;                   // ... in the pixel-view world, when it began
        Rect closeFrom;

        // camera (set frame)
        Vector3 camPos, track;
        Quaternion camRot = Quaternion.identity;
        Vector2 pp;
        float f, followYaw, lungeK, restYaw, yawPx, horizon;
        Vector3 lungeCam, lungeAim;
        bool lungeInit;
        float push, pushVel;                  // the nose-in's push-in (def.noseFill): 0 = the rest framing, 1 = in
        Vector3 pushOff;                      // its framed point (from the lure) and camera distance
        float pushDist;
        bool pushHas;

        // the lure (set frame)
        Vector3 lure;
        float hopLeft, hopV, dragAcc, lureAnimT, lureTilt;
        bool lureInMouth, falling;
        float popT = 9f, dartLeft, trailAcc, knockT = 9f, spinT = 9f;
        Vector3 knockV, dartV;
        bool lureHidden;

        // the fish (set frame)
        Vector3 fPos, fVel;
        float fHead, fBank, fPitch, yawRate, swimPh, finPh, headYaw, jaw, flare, dorsal, noseDist;
        float headPitch, protrude, barbel, eyeRoll, glow;
        float flinchT = 9f, dimEyeT = 9f, dorsalUpT = 9f, closeT = -1f, shakeT = -1f;
        Vector3 flinchDir;
        float orbitTh, patrolT;
        int orbitMood = -1;
        Vector3 moodFrom;
        bool approachStarted;
        float heartT, time;
        float flashT = -1f, frameGoldT = -1f, uwShakeT, uwShakeAmp;
        Vector2 uwShake;
        float lightR;
        // the rollout choreography's state
        float breathT = -1f, digT = 3f, digOn = -1f, popLookT = 9f, creditFlashT = 9f, twitchT = 9f, bubbleT;
        bool breathDone, fakeKnocked, lungeFromSet;
        Vector3 lungeFrom;
        float lungeHead, lungePitch;
        int lastCredits;

        // particles in the underwater compose (render-target pixels)
        class Part
        {
            public SpriteRenderer sr;
            public Vector2 px, vel;
            public float life, max, wobble;
            public Sprite[] frames;
            public Color col;
        }

        readonly List<Part> parts = new List<Part>();
        readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();

        /// <summary>A ring on the surface seen from below (the popper's "퐁", a boil's edge): a growing 1 px ellipse.</summary>
        class Ring
        {
            public SpriteRenderer sr;
            public Vector3 at;       // set frame
            public float t, life, maxR;
            public Color col;
        }

        readonly List<Ring> rings = new List<Ring>();

        class Fleck
        {
            public SpriteRenderer sr;
            public Vector2 px;
            public float speed, phase, alpha;
        }

        readonly List<Fleck> snowFar = new List<Fleck>(), snowNear = new List<Fleck>();

        Vector3 CamRest => set.camRest;
        float LureRest => set.lureRest;
        Vector3 LineUp => set.lineUp;
        Vector3 DragDir => set.dragDir.normalized;
        Vector3 Eyes0 => cho.eyes0;
        Vector3 Eyes1 => cho.eyes1;
        Vector3 DeepDir => cho.deepDir.normalized;

        // ================================================================== setup
        /// <summary>Test hook (-fkenccm, EncProbe): the legend shown at this length whatever it rolled (0 = off).</summary>
        public static float DebugCm;
        /// <summary>
        /// Test hook (-fkencwinh &lt;px&gt;): the window this many render-target px tall (64..136, its top kept), so the
        /// frog and the face crowd the HUD's bands and the gauge has to move; 0 = off (the window as laid out).
        /// </summary>
        public static float DebugWinH;

        public static EncounterView Create(FishingController ctl, LegendEncounter enc, BaitDef bait, float cm)
        {
            var v = new GameObject("EncounterView").AddComponent<EncounterView>();
            v.Init(ctl, enc, bait, DebugCm > 0f ? DebugCm : cm);
            Current = v;
            return v;
        }

        void Init(FishingController c, LegendEncounter e, BaitDef b, float cm)
        {
            ctl = c;
            enc = e;
            def = e.Def;
            set = GameDatabase.GetSet(def.backdrop);
            cho = def.choreo ?? new Choreo();
            rollout = cho.style != ChoreoStyle.Coelacanth;
            stage = c.Stage;
            pv = PixelView.Current;
            bait = b;
            fishScale = cm / 100f;
            lure = new Vector3(0f, LureRest, 0f);
            eyeCore = Art.Hex(def.eyeCore);
            eyeGlow = Art.Hex(def.eyeGlow ?? def.eyeCore);
            abyss = Art.Hex(set.abyss);
            fogOutline = Art.Hex(set.fogOutline);
            abyssNear = set.abyssNear != null ? Art.Hex(set.abyssNear) : abyss;
            fogNear = set.fogNear != null ? Art.Hex(set.fogNear) : fogOutline;
            lineCol = stage.UnderwaterLine(Game.I.Line.color);
            lineCol = Color.Lerp(lineCol, Art.Hex(set.line), 0.5f);
            lineCol.a = 0.75f;

            setRoot = new GameObject("EncSet").transform;
            setRoot.SetParent(transform, false);
            setRoot.position = SetOrigin;
            uwRoot = new GameObject("EncCompose").transform;
            uwRoot.SetParent(transform, false);

            fishCam = MakeCamera("EncFishCam", FishLayer, new Color(0, 0, 0, 0), false);
            uwCam = MakeCamera("EncUwCam", UwLayer, Art.Hex(set.clear), true);

            fish = Legend3D.TryCreate(def, FishLayer, setRoot);
            if (fish != null) fish.SetVisible(false);

            // pixel view overlays
            dim = PixelSprite("EncDim", Art.Pixel, OrderDim, null);
            dim.color = new Color(0, 0, 0, 0);
            var fq = new GameObject("EncCrop");
            fq.transform.SetParent(transform, false);
            cropQuad = fq.AddComponent<MeshRenderer>();
            cropMesh = new Mesh { name = "EncCrop" };
            fq.AddComponent<MeshFilter>().sharedMesh = cropMesh;
            cropMat = new Material(Angler.LineMaterial) { name = "EncCropMat" };
            SetupQuad(cropQuad, cropMat, OrderCrop);
            frame = PixelSprite("EncFrame", Enc("enc_frame_" + def.backdrop, "enc_frame_cave"), OrderFrame, null);
            frame.drawMode = SpriteDrawMode.Sliced;
            frame.enabled = false;
            timer = PixelSprite("EncTimer", Art.Pixel, OrderTimer, null);
            timer.color = Art.Hex("#e8fffb");
            timer.enabled = false;
            flashSr = PixelSprite("EncFlash", Art.Pixel, OrderTop, null);
            flashSr.enabled = false;
            waterline = PixelSprite("EncWaterline", Enc("waterline", "waterline"), OrderTop, null);
            waterline.drawMode = SpriteDrawMode.Tiled;
            waterline.enabled = false;

            // the underwater compose: backdrop, halo, fish layer, lure, line, eyeshine (the cave's art only for the cave:
            // an optional layer another set lacks is left out)
            string bd = "uw_" + def.backdrop;
            bool cave = def.backdrop == "cave";
            bg = PixelSprite("Bg", Enc(bd + "_bg", "uw_cave_bg"), UBg, uwRoot, UwLayer);
            if (set.ceiling) ceilSr = Optional("Ceiling", bd + "_ceiling", UCeil);
            if (ceilSr != null && !string.IsNullOrEmpty(set.ceilFill))
            {
                // (a close camera looking up at the strike sees past the layer's top edge: the underside goes on)
                ceilFillSr = PixelSprite("CeilingFill", Art.Pixel, UCeil, uwRoot, UwLayer);
                ceilFillSr.color = Art.Hex(set.ceilFill);
                ceilFillSr.enabled = false;
            }
            foreach (var r in set.rays)
            {
                var sr = PixelSprite("Ray", Enc("uw_ray", "uw_ray"), URay, uwRoot, UwLayer);
                sr.color = Art.Hex(r.tint, r.alpha);
                rays.Add(sr);
            }
            if (set.rayAnchored) MakeColumn(bd + "_ray");
            mid = PixelSprite("Mid", Enc(bd + "_mid", "uw_cave_mid"), UMid, uwRoot, UwLayer);
            floorSr = set.hasFloor ? (cave ? PixelSprite("Floor", Enc(bd + "_floor", "uw_cave_floor"), UFloor, uwRoot, UwLayer) : Optional("Floor", bd + "_floor", UFloor)) : null;
            fore = PixelSprite("Fore", Enc(bd + "_fore", "uw_cave_fore"), UFore, uwRoot, UwLayer);
            if (!string.IsNullOrEmpty(set.veil))
            {
                veil = PixelSprite("Veil", Art.Pixel, UVeil, uwRoot, UwLayer);
                veil.enabled = false;
            }
            halo = PixelSprite("Halo", Enc("lure_halo", "lure_halo"), UHalo, uwRoot, UwLayer);
            halo.color = Art.Hex(set.halo ?? "#9affea");
            string lid = bait != null ? bait.id.Replace("bait_", "") : "egi";
            lureFrames = new[] { Enc($"lure_{lid}_0", "lure_egi_0"), Enc($"lure_{lid}_1", "lure_egi_1") };
            lureSr = PixelSprite("Lure", lureFrames[0], ULureFront, uwRoot, UwLayer);
            eyeSrL = PixelSprite("EyeL", Enc("eyeshine", "eyeshine"), UEyes, uwRoot, UwLayer);
            eyeSrR = PixelSprite("EyeR", eyeSrL.sprite, UEyes, uwRoot, UwLayer);
            eyeSrL.enabled = eyeSrR.enabled = false;
            if (rollout)
            {
                // the rollout legends' eyes: the halo in the glow colour, the core in the core colour (spec 2.5.7)
                var core = EyeCoreSprite();
                eyeCoreL = PixelSprite("EyeCoreL", core, UEyeCore, uwRoot, UwLayer);
                eyeCoreR = PixelSprite("EyeCoreR", core, UEyeCore, uwRoot, UwLayer);
                eyeCoreL.enabled = eyeCoreR.enabled = false;
            }
            silt = new[] { Enc("silt_0", "silt_0"), Enc("silt_1", "silt_1"), Enc("silt_2", "silt_2"), Enc("silt_3", "silt_3") };
            bubbleS = Enc("bubble_s", "bubble_s");
            bubbleM = Enc("bubble_m", "bubble_m");

            var lgo = new GameObject("Line");
            lgo.transform.SetParent(uwRoot, false);
            lgo.layer = UwLayer;
            line = lgo.AddComponent<LineRenderer>();
            line.sharedMaterial = Angler.LineMaterial;
            line.useWorldSpace = true;
            line.positionCount = 12;
            line.widthMultiplier = 1f / PPU;
            line.numCapVertices = 0;
            line.sortingOrder = ULine;
            line.startColor = line.endColor = lineCol;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var fqo = new GameObject("FishLayerQuad");
            fqo.transform.SetParent(uwRoot, false);
            fqo.layer = UwLayer;
            fishQuad = fqo.AddComponent<MeshRenderer>();
            fishMesh = new Mesh { name = "EncFishQuad" };
            fqo.AddComponent<MeshFilter>().sharedMesh = fishMesh;
            fishMat = (set.rimCol != null ? ActorArt.RimMaterial(set.rimCol, set.rimStrength, set.rimDirs) : ActorArt.RimMaterial(stage.Def.id))
                      ?? new Material(Angler.LineMaterial);
            fishMat.name = "EncFishMat";
            rimPreset = fishMat.HasProperty(RimStrengthId) ? fishMat.GetFloat(RimStrengthId) : 0f;
            SetupQuad(fishQuad, fishMat, UFish);

            var rnd = new System.Random(7);
            for (int i = 0; i < 30; i++)
                snowFar.Add(new Fleck { sr = PixelSprite("SnowFar", Art.Pixel, USnowFar, uwRoot, UwLayer), px = new Vector2(rnd.Next(0, 640), rnd.Next(0, 400)),
                    speed = 1f + (float)rnd.NextDouble(), phase = (float)rnd.NextDouble() * 6f, alpha = 0.7f });
            for (int i = 0; i < 12; i++)
                snowNear.Add(new Fleck { sr = PixelSprite("SnowNear", Art.Pixel, USnowNear, uwRoot, UwLayer), px = new Vector2(rnd.Next(0, 640), rnd.Next(0, 400)),
                    speed = 3f + 2f * (float)rnd.NextDouble(), phase = (float)rnd.NextDouble() * 6f, alpha = 0.3f + 0.2f * (float)rnd.NextDouble() });
            // the time of day under the water (read once, kept for the encounter): the backdrop's tint and light
            PeriodSetup();
            foreach (var s in snowFar) s.sr.color = Art.Hex(set.snowFar, s.alpha * pSnow);

            // the legend flinches at every penalty, flares at the tell, bites at the close
            enc.Penalty += (k, t) => Flinch();
            enc.Early += Flinch;
            enc.Tell += OnTell;
            enc.Closed += OnClose;
            enc.HookSet += OnHookSet;
            enc.FakeOut += OnFakeOut;

            InitTop();
            Layout();
            var hp = ctl.Tackle.HookPos;
            lureWorld = stage.P.To2D(stage.P.Apparent(hp));
            lureSurf = LureRT();
            fHead = Mathf.Atan2(-Eyes0.x, -Eyes0.z) * Mathf.Rad2Deg;
            fPos = Eyes0;
            track = new Vector3(0f, LureRest, 0f);
            UpdateCamera(0f);
            restYaw = camRot.eulerAngles.y;
            Debug.Log($"[ENC] view: set {set.id} ({set.lureAt}) rt {w}x{h} window {window} f {f0:0.0} lure surface px {lureSurf} " +
                      $"fish {(fish != null ? "3D" : "missing")} {fishScale:0.00} m, style {cho.style}, cam x{def.camScale:0.00}, " +
                      $"layers ceiling {(ceilSr != null)} floor {(floorSr != null)} rays {rays.Count}{(rayCol != null ? "+column" : "")} veil {(veil != null)}, " +
                      $"tease {(topFlow ? "from above" : "underwater")}");
        }

        // ------------------------------------------------------------------ the time of day (Docs/time_currents_spec.md 5.5)
        // the underwater sets were drawn as daylight: 낮 is the identity; the period tints the layers and dims the rays
        Color pTint = Color.white;
        float pRays = 1f, pSun = 1f, pLight = 1f, pHalo = 1f, pSnow = 1f, pColumn = 1f;

        struct PeriodFx
        {
            public Color tint, rayTo;
            public float rays, rayMix, sun, light, halo, snow, column;
        }

        PeriodFx FxOf(Period p)
        {
            var id = new PeriodFx { tint = Color.white, rayTo = Color.white, rays = 1f, sun = 1f, light = 1f, halo = 1f, snow = 1f, column = 1f };
            string bd = def.backdrop ?? "";
            if (bd == "cave")
            {
                // the shaft is far, the crystals light it: only the night changes it
                if (p != Period.Night) return id;
                id.tint = Art.Hex("#c8d0e8");
                id.rays = 0.3f;
                id.column = 0.3f;
                return id;
            }
            switch (p)
            {
                case Period.Dawn:
                    return new PeriodFx { tint = Art.Hex("#d8d0e4"), rays = 0.6f, rayTo = Art.Hex("#ffd8e0"), rayMix = 0.4f, sun = 0.8f, light = 1f, halo = 1f, snow = 0.8f, column = 0.6f };
                case Period.Evening:
                    return new PeriodFx { tint = Art.Hex("#f0d4b8"), rays = 0.7f, rayTo = Art.Hex("#ffc890"), rayMix = 0.4f, sun = 0.85f, light = 1.1f, halo = 1.1f, snow = 0.8f, column = 0.7f };
                case Period.Night:
                {
                    bool ice = bd == "ice";
                    return new PeriodFx
                    {
                        tint = Art.Hex(ice ? "#6a78a8" : "#5a6a98"), rays = 0.15f, rayTo = Art.Hex("#a8c0ff"), rayMix = 0.6f, sun = 0.35f,
                        light = 1.25f, halo = 1.3f, snow = 0.6f, column = ice ? 0.3f : 0.15f,
                    };
                }
                default: return id;
            }
        }

        /// <summary>The blend of the clock when the encounter opens: tints the backdrop layers and the rays, scales the light.</summary>
        void PeriodSetup()
        {
            var b = GameClock.Look;
            PeriodFx a = FxOf(b.From), c = FxOf(b.To);
            float f = b.F;
            pTint = Color.Lerp(a.tint, c.tint, f);
            pRays = Mathf.Lerp(a.rays, c.rays, f);
            pSun = Mathf.Lerp(a.sun, c.sun, f);
            pLight = Mathf.Lerp(a.light, c.light, f);
            pHalo = Mathf.Lerp(a.halo, c.halo, f);
            pSnow = Mathf.Lerp(a.snow, c.snow, f);
            pColumn = Mathf.Lerp(a.column, c.column, f);
            var solid = new Color(pTint.r, pTint.g, pTint.b, 1f);
            foreach (var sr in new[] { bg, ceilSr, ceilFillSr, floorSr, fore })
                if (sr != null) sr.color = sr.color * solid;
            for (int i = 0; i < rays.Count && i < set.rays.Length; i++)
            {
                var r = set.rays[i];
                var col = Color.Lerp(Art.Hex(r.tint), Color.Lerp(a.rayTo, c.rayTo, f), Mathf.Lerp(a.rayMix, c.rayMix, f)) * solid;
                col.a = r.alpha * pRays;
                rays[i].color = col;
            }
            if (rayColMat != null) rayColMat.color = new Color(pTint.r, pTint.g, pTint.b, pColumn);
            if (b.From != Period.Day || b.To != Period.Day)
                Debug.Log($"[ENC] period {b}: tint #{ColorUtility.ToHtmlStringRGB(pTint)} rays x{pRays:0.00} sun x{pSun:0.00} light x{pLight:0.00}");
        }

        Sprite Enc(string name, string fallback)
        {
            var s = Resources.Load<Sprite>("Sprites/Encounter/" + name);
            return s != null ? s : Art.Get("Encounter/" + fallback);
        }

        /// <summary>An optional backdrop layer: hidden when its sprite is missing (no fallback to the cave's art).</summary>
        SpriteRenderer Optional(string name, string sprite, int order)
        {
            var s = Resources.Load<Sprite>("Sprites/Encounter/" + sprite);
            if (s == null)
            {
                Debug.Log($"[ENC] view: no {sprite}, layer left out");
                return null;
            }
            return PixelSprite(name, s, order, uwRoot, UwLayer);
        }

        /// <summary>The hole's light column (left out when its art is missing): a mesh over the sprite's texture, placed by <see cref="PlaceColumn"/>.</summary>
        void MakeColumn(string sprite)
        {
            var s = Resources.Load<Sprite>("Sprites/Encounter/" + sprite);
            if (s == null)
            {
                Debug.Log($"[ENC] view: no {sprite}, layer left out");
                return;
            }
            var go = new GameObject("RayColumn");
            go.transform.SetParent(uwRoot, false);
            go.layer = UwLayer;
            rayCol = go.AddComponent<MeshRenderer>();
            rayColMesh = new Mesh { name = "EncRayColumn" };
            rayColMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = rayColMesh;
            rayColMat = new Material(Angler.LineMaterial) { name = "EncRayColumnMat", mainTexture = s.texture };
            SetupQuad(rayCol, rayColMat, URayCol);
            // the sprite's rect in its texture (point-filtered: one texel per render-target pixel across)
            Vector2 lo = Vector2.one, hi = Vector2.zero;
            foreach (var uv in s.uv)
            {
                lo = Vector2.Min(lo, uv);
                hi = Vector2.Max(hi, uv);
            }
            rayColUv = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
            rayColW = Mathf.RoundToInt(s.rect.width);
        }

        static Sprite eyeCoreSprite;

        /// <summary>The eyeshine's opaque core (a 5 px plus in its 7 x 7 frame), for the two-tone eyes.</summary>
        static Sprite EyeCoreSprite()
        {
            if (eyeCoreSprite != null) return eyeCoreSprite;
            var tex = new Texture2D(7, 7, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "EyeCore" };
            var px = new Color32[49];
            foreach (var (x, y) in new[] { (3, 2), (2, 3), (3, 3), (4, 3), (3, 4) }) px[y * 7 + x] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            eyeCoreSprite = Sprite.Create(tex, new Rect(0, 0, 7, 7), new Vector2(0.5f, 0.5f), PPU);
            return eyeCoreSprite;
        }

        static readonly Dictionary<int, Sprite> ellipses = new Dictionary<int, Sprite>();

        /// <summary>A 1 px ellipse outline (or filled) with these radii (px), made once and kept.</summary>
        static Sprite Ellipse(int rx, int ry, bool fill)
        {
            rx = Mathf.Clamp(rx, 1, 60);
            ry = Mathf.Clamp(ry, 1, 30);
            int key = (rx * 64 + ry) * 2 + (fill ? 1 : 0);
            if (ellipses.TryGetValue(key, out var s) && s != null) return s;
            int tw = 2 * rx + 1, th = 2 * ry + 1;
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "EncEllipse" };
            var px = new Color32[tw * th];
            bool In(int x, int y)
            {
                if (x < 0 || y < 0 || x >= tw || y >= th) return false;
                float dx = (x - rx) / (rx + 0.5f), dy = (y - ry) / (ry + 0.5f);
                return dx * dx + dy * dy <= 1f;
            }
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    if (!In(x, y)) continue;
                    bool edge = !In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1);
                    if (fill || edge) px[y * tw + x] = new Color32(255, 255, 255, 255);
                }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), PPU);
            ellipses[key] = s;
            return s;
        }

        Camera MakeCamera(string name, int layer, Color clear, bool ortho)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; // rendered by hand in LateUpdate
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.cullingMask = 1 << layer;
            cam.orthographic = ortho;
            cam.nearClipPlane = ortho ? 0.1f : 0.05f;
            cam.farClipPlane = ortho ? 100f : 120f;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.allowDynamicResolution = false;
            cam.useOcclusionCulling = false;
            cam.renderingPath = RenderingPath.Forward;
            cam.depthTextureMode = DepthTextureMode.None;
            return cam;
        }

        static void SetupQuad(MeshRenderer r, Material m, int order)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.sortingOrder = order;
            r.enabled = false;
        }

        SpriteRenderer PixelSprite(string name, Sprite s, int order, Transform parent, int layer = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.layer = layer;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>(Re)builds the render targets and the window for the pixel view's current size.</summary>
        void Layout()
        {
            w = pv.Target.width;
            h = pv.Target.height;
            Release();
            rtFish = NewRT("EncFishRT", 24);
            rtUW = NewRT("EncUwRT", 0);
            fishCam.targetTexture = rtFish;
            uwCam.targetTexture = rtUW;
            uwCam.orthographicSize = h * 0.5f / PPU;
            cropMat.mainTexture = rtUW;
            fishMat.mainTexture = rtFish;
            // the window: 288 x 136, centred, 10 px under the top; on taller views centred between the hat and the top
            float top = h - WinTop, bottom = top - WinH;
            if (h > PixelView.BaseHeight)
            {
                float hat = WorldToRT(ctl.Angler.HatPos2D - stage.DeckBob).y + 10f + 16f;
                float span = top - hat;
                float wh = Mathf.Clamp(span, 96f, WinH);
                bottom = Mathf.Round(hat + (span - wh) * 0.5f);
                top = bottom + wh;
            }
            // (test hook: a narrower window, its top kept, as tight as the tall views' clamp or tighter)
            if (DebugWinH > 0f) bottom = top - Mathf.Round(Mathf.Clamp(DebugWinH, 64f, WinH));
            window = new Rect(Mathf.Round((w - WinW) * 0.5f), bottom, WinW, top - bottom);
            ppWin = window.center;
            f0 = (window.height * 0.5f) / Mathf.Tan(21f * Mathf.Deg2Rad);
            // the fish layer: a quad the size of the target, 1:1 in the compose
            float hw = w * 0.5f / PPU, hh = h * 0.5f / PPU;
            fishMesh.Clear();
            fishMesh.vertices = new[] { new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(-hw, hh, 0), new Vector3(hw, hh, 0) };
            fishMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            fishMesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            fishMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            fishMesh.bounds = new Bounds(Vector3.zero, new Vector3(hw * 2f, hh * 2f, 1f));
            fishQuad.transform.position = UwOrigin;
            // without the 3D model the fish target is never rendered (its contents undefined): leave the layer out
            fishQuad.enabled = fish != null;
            LayoutTop();
        }

        RenderTexture NewRT(string name, int depth)
        {
            var rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1, useMipMap = false,
                autoGenerateMips = false, name = name,
            };
            rt.Create();
            return rt;
        }

        void Release()
        {
            foreach (var rt in new[] { rtFish, rtUW })
            {
                if (rt == null) continue;
                rt.Release();
                Destroy(rt);
            }
            rtFish = rtUW = null;
            if (fishCam != null) fishCam.targetTexture = null;
            if (uwCam != null) uwCam.targetTexture = null;
            ReleaseTop();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            Release();
            DestroyTop();
            fish?.Destroy();
            if (cropMat != null) Destroy(cropMat);
            if (fishMat != null) Destroy(fishMat);
            if (cropMesh != null) Destroy(cropMesh);
            if (fishMesh != null) Destroy(fishMesh);
            if (rayColMat != null) Destroy(rayColMat);
            if (rayColMesh != null) Destroy(rayColMesh);
        }

        // ================================================================== coordinates
        Vector2 CamPos2D
        {
            get
            {
                var p = pv.WorldCamera.transform.position;
                return new Vector2(p.x, p.y);
            }
        }

        /// <summary>A pixel-view world point -> render-target pixels (bottom-left origin).</summary>
        public Vector2 WorldToRT(Vector2 world) => (world - CamPos2D) * PPU + new Vector2(w * 0.5f, h * 0.5f);

        /// <summary>Render-target pixels -> pixel-view world.</summary>
        public Vector2 RTToWorld(Vector2 px) => CamPos2D + (px - new Vector2(w * 0.5f, h * 0.5f)) / PPU;

        /// <summary>Render-target pixels -> screen pixels (for the HUD).</summary>
        public Vector2 RTToScreen(Vector2 px) => pv.WorldToScreen(RTToWorld(px));

        /// <summary>The underwater compose's world point for a render-target pixel.</summary>
        Vector3 U(Vector2 px) => UwOrigin + new Vector3((px.x - w * 0.5f) / PPU, (px.y - h * 0.5f) / PPU, 0f);

        static float SnapPx(float v) => Mathf.Round(v);

        /// <summary>The window as it is now (render-target pixels) and its full size.</summary>
        public Rect Crop => crop;
        public Rect Window => window;
        public bool Open => cropQuad.enabled || cropTopQuad != null && cropTopQuad.enabled;
        public int Width => w;
        public int Height => h;

        /// <summary>A set-frame point through the fish camera: render-target pixels and the depth along its view.</summary>
        Vector3 Proj(Vector3 p)
        {
            var v = Quaternion.Inverse(camRot) * (p - camPos);
            float d = Mathf.Max(0.05f, v.z);
            return new Vector3(pp.x + f * v.x / d, pp.y + f * v.y / d, v.z);
        }

        /// <summary>For the autopilot: the fish's distance from the camera (m), or -1.</summary>
        public float FishCamDist => fish != null ? Vector3.Distance(fPos, camPos) : -1f;

        /// <summary>For the tests: the fish's distance from the lure (m), or -1.</summary>
        public float FishLureDist => fish != null ? Vector3.Distance(fPos, lure) : -1f;

        /// <summary>For the tests: the fish from the snout to the tail bone across the window (share of its width), or -1.</summary>
        public float FishSpanK
        {
            get
            {
                if (fish == null || window.width <= 0f) return -1f;
                var a = Proj(fish.Mouth - SetOrigin);
                var b = Proj(fish.TailPos - SetOrigin);
                return Mathf.Abs(a.x - b.x) / window.width;
            }
        }

        readonly List<Vector3> facePts = new List<Vector3>(), framePts = new List<Vector3>();

        /// <summary>
        /// The legend's face this frame in render-target pixels, for the HUD to keep its text off: the eyes, the mouth and
        /// the head from the gill cover to the snout with the jaw as posed (only the eyes while the body is still dark),
        /// clipped to the window. False while none of it shows.
        /// </summary>
        public bool FaceRect(out Rect r)
        {
            r = default;
            if (fish == null || !Open) return false;
            var ph = enc.Ph;
            bool eyesOnly = ph == LegendEncounter.Phase.Eyes || !bodyShown;
            if (ph <= LegendEncounter.Phase.Open || ph >= LegendEncounter.Phase.Done || (eyesOnly && ph != LegendEncounter.Phase.Eyes && !eyesShown)) return false;
            facePts.Clear();
            fish.FacePoints(facePts, eyesOnly);
            // the top view (and, while rising through the waterline, both parts of the window)
            bool any = false;
            if (ShowsTop && cropTopQuad.enabled && TopFace(out var tr))
            {
                r = tr;
                any = true;
            }
            if (!ShowsUW || !cropQuad.enabled) return any;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            bool behind = false;
            foreach (var p in facePts)
            {
                var q = Proj(p - SetOrigin);
                if (q.z < 0.1f) behind = true;
                x0 = Mathf.Min(x0, q.x);
                y0 = Mathf.Min(y0, q.y);
                x1 = Mathf.Max(x1, q.x);
                y1 = Mathf.Max(y1, q.y);
            }
            // the eyeshine sprites (x2 at the tell) and the compose's shake reach a few pixels past the points
            float pad = 3f + Mathf.Abs(uwShake.x) + Mathf.Abs(uwShake.y);
            var uw = behind ? crop : Rect.MinMaxRect(x0 - pad, y0 - pad - uwShiftPx, x1 + pad, y1 + pad - uwShiftPx);
            float uwTop = ShowsTop ? splitPx : crop.yMax;
            uw = Rect.MinMaxRect(Mathf.Max(uw.xMin, crop.xMin), Mathf.Max(uw.yMin, crop.yMin), Mathf.Min(uw.xMax, crop.xMax), Mathf.Min(uw.yMax, uwTop));
            if (uw.width <= 0f || uw.height <= 0f) return any;
            r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, uw.xMin), Mathf.Min(r.yMin, uw.yMin), Mathf.Max(r.xMax, uw.xMax), Mathf.Max(r.yMax, uw.yMax)) : uw;
            return true;
        }

        /// <summary>The lure in the window (render-target pixels) and whether it shows (not while in the legend's mouth).</summary>
        public Vector2 LurePx => TopView ? frogPx - new Vector2(0f, topShiftPx) : uwLurePx - new Vector2(0f, uwShiftPx);
        Vector2 uwLurePx;
        public bool LureShown => TopView ? frogSr.enabled && cropTopQuad.enabled : lureSr != null && lureSr.enabled && cropQuad.enabled;
        /// <summary>What of the lure must stay clear (render-target pixels): 10 px round it, or the top view's frog and its spray.</summary>
        public Rect LureBox => TopView ? TopLureBox : new Rect(LurePx.x - 10f, LurePx.y - 10f, 20f, 20f);

        // ================================================================== events
        void Flinch()
        {
            flinchT = 0f;
            dimEyeT = 0f;
            dorsalUpT = 0f;
            flinchDir = -Fwd(fHead);
            Emit(bubbleS, Proj(fPos + Fwd(fHead) * 0.45f * fishScale), 3, 14f);
            if (TopView)
            {
                var m = fPos + Fwd(fHead) * 0.45f * fishScale;
                TopBubbles(ProjTop2(new Vector3(m.x, SurfY, m.z)), 2, 4f, 0.1f);
            }
        }

        void OnTell()
        {
            frameGoldT = 0f;
            Sfx.Play(Sfx.Glint, 0.6f);
        }

        void OnClose()
        {
            closeT = 0f;
            lureInMouth = true;
            flashT = 0f;
            uwShakeT = 0.15f;
            uwShakeAmp = 2.5f;
            pv.Shake(def.shake.x, def.shake.y);
            var m = Proj(Mouth());
            if (cho.style == ChoreoStyle.Arapaima)
            {
                // the strike from below: the surface boils
                Sfx.Play(Sfx.Splash, 1f);
                Sfx.Play(Sfx.Chomp, 1f, 0.8f);
                Boil(new Vector3(lure.x, set.surfaceY, lure.z));
            }
            else Sfx.Play(Sfx.Chomp, 1f);
            Emit(bubbleM, m, 4, 20f);
            EmitSilt(m + new Vector3(0f, -4f, 0f), cho.style == ChoreoStyle.Sturgeon ? 4 : 2);
        }

        void OnHookSet(bool perfect)
        {
            shakeT = 0f;
            var m = Proj(Mouth());
            Emit(bubbleS, m, 8, 26f);
            Emit(bubbleM, m, 3, 20f);
            EmitSilt(m + new Vector3(0f, -6f, 0f), 3);
            uwShakeT = 0.25f;
            uwShakeAmp = 1.5f;
        }

        /// <summary>The fake-out (no gold flash, no "ting"): what it does to the lure; the fish's part is in its choreography.</summary>
        void OnFakeOut()
        {
            fakeKnocked = false;
            twitchT = 0f;
            switch (cho.style)
            {
                case ChoreoStyle.Marlin:
                {
                    // the bill's slash: the lure tumbles 0.25 m sideways, spinning
                    var side = Vector3.Cross(Vector3.up, DragDir).normalized;
                    knockV = side * (0.25f / 0.25f);
                    knockT = 0f;
                    spinT = 0f;
                    Emit(bubbleS, Proj(lure), 4, 16f);
                    Sfx.Play(Sfx.Whoosh, 0.35f, 1.3f);
                    break;
                }
                case ChoreoStyle.Sturgeon:
                    EmitSilt(Proj(new Vector3(lure.x, set.floorY, lure.z)), 2);
                    break;
            }
        }

        // ================================================================== frame
        void LateUpdate()
        {
            if (pv == null || pv.Target == null || enc == null) return;
            if (pv.Target.width != w || pv.Target.height != h) Layout();
            pv.WorldCamera.cullingMask &= ~((1 << UwLayer) | (1 << FishLayer));
            float dt = Time.deltaTime;
            time += dt;
            var ph = enc.Ph;
            UpdateTopState(ph);
            UpdateLure(dt, ph);
            if (rollout) UpdateRollout(dt, ph);
            else UpdateFish(dt, ph);
            UpdateCamera(dt);
            UpdateLight(ph);
            // the underwater view (the top view alone skips its renders; its compose still runs, so nothing freezes)
            bool uw = ShowsUW;
            if (uw && fish != null && rtFish != null)
            {
                PlaceFishCamera(camPos, camRot, f, pp, rtFish);
                fishCam.Render();
            }
            ComposeUnderwater(dt, ph);
            if (uw)
            {
                uwCam.targetTexture = rtUW;
                uwCam.transform.position = UwOrigin + (Vector3)(uwShake / PPU) + new Vector3(0f, uwShiftPx / PPU, -10f);
                uwCam.Render();
            }
            RenderTop(dt, ph);
            UpdateCrop(dt, ph);
            Heartbeat(dt, ph);
            if (DebugLog && fish != null && (debugT -= dt) <= 0f)
            {
                debugT = ph == LegendEncounter.Phase.Tease ? 0.1f : 0.5f;
                var pr = Proj(fPos);
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[ENC] dbg {0} fPos {1} head {2:0} pitch {3:0} cam {4} proj ({5:0},{6:0},{7:0.00}) lure {8} in wind {10} {11:0.00} rev/s pause {12:0.00} circ {13} / {14} / {9}",
                    ph, fPos.ToString("F2"), fHead, fPitch, camPos.ToString("F2"), pr.x, pr.y, pr.z, lure.ToString("F2"), fish.DebugText(),
                    ctl.LureIn.Winding, ctl.LureIn.Speed, ctl.LureIn.PauseT, ctl.Gesture.Circling, enc.DebugRun));
            }
        }

        static readonly bool DebugLog = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fkencdebug") >= 0;
        float debugT;

        // ------------------------------------------------------------------ the lure (local motion only: the surface rig is held)
        float HopFor(float s)
        {
            if (set.lureHop > 0f) return set.lureHop;
            switch (bait != null ? bait.id : "")
            {
                case "bait_softworm": return 0.2f + 0.2f * s;
                case "bait_jig": return 0.3f + 0.5f * s;
                default: return 0.25f + 0.35f * s;
            }
        }

        float FallSpeed => set.lureFall > 0f ? set.lureFall
            : bait != null && bait.id == "bait_softworm" ? 0.6f : bait != null && bait.id == "bait_jig" ? 0.8f : 0.35f;

        bool GoldenBait => bait != null && bait.id == "bait_golden";

        void UpdateLure(float dt, LegendEncounter.Phase ph)
        {
            lureAnimT += dt;
            if (lureInMouth)
            {
                if (fish != null) lure = Mouth();
                return;
            }
            // a fake-out's knock (the marlin's slash, the great white's bump): pushed over 0.25 s
            if (knockT < 0.25f)
            {
                float k = Mathf.Min(dt, 0.25f - knockT);
                knockT += dt;
                var next = lure + knockV * k;
                if (set.lureAt == LureAt.Mid || new Vector2(next.x, next.z).magnitude <= set.lureBound + 0.5f) lure = next;
            }
            if (spinT < 0.45f) spinT += dt;
            bool live = ph != LegendEncounter.Phase.Omen && ph != LegendEncounter.Phase.Done;
            var li = ctl.LureIn;
            switch (set.lureAt)
            {
                case LureAt.Surface: SurfaceLure(dt, live, li); break;
                case LureAt.Mid: MidLure(dt, live, li); break;
                default: FloorLure(dt, live, li); break;
            }
        }

        void FloorLure(float dt, bool live, LureInput li)
        {
            if (live && li.FlickNow)
            {
                float hop = HopFor(li.FlickStrength);
                hopLeft = 0.14f;
                hopV = hop / 0.14f;
                // bubbles off the lure, behind the fish when the lure is
                int bo = lureSr.sortingOrder + 1;
                Emit(bubbleS, Proj(lure), 3 + Random.Range(0, 2), 16f, bo);
                Emit(bubbleM, Proj(lure), 1, 12f, bo);
            }
            if (live && li.Winding && hopLeft <= 0f)
            {
                // a circle drags the lure along the bottom towards the angler
                float m = Mathf.Max(0f, li.Speed) * set.lureDrag * dt;
                var next = lure + DragDir * m;
                if (new Vector2(next.x, next.z).magnitude <= set.lureBound)
                {
                    lure = next;
                    dragAcc += m;
                    // the golden bait sheds crumbs every 0.1 m, the rest a silt puff every 0.3 m
                    if (GoldenBait && dragAcc >= 0.1f)
                    {
                        dragAcc = 0f;
                        CrumbPuff(Proj(lure), 3);
                    }
                    else if (dragAcc >= 0.3f)
                    {
                        dragAcc = 0f;
                        EmitSilt(Proj(new Vector3(lure.x, set.floorY, lure.z)), 1);
                    }
                }
            }
            if (hopLeft > 0f)
            {
                float k = Mathf.Min(dt, hopLeft);
                hopLeft -= dt;
                lure.y += hopV * k;
                falling = true;
                lureTilt = Mathf.MoveTowards(lureTilt, 25f, dt * 300f);
            }
            else if (lure.y > LureRest + 0.001f)
            {
                lure.y = Mathf.Max(LureRest, lure.y - FallSpeed * dt);
                falling = true;
                float flutter = bait != null && bait.id == "bait_jig" ? 15f * Mathf.Sin(lureAnimT * Mathf.PI * 2f * 4f) : 4f * Mathf.Sin(lureAnimT * Mathf.PI * 2f * 2f);
                lureTilt = Mathf.MoveTowards(lureTilt, -20f + flutter, dt * 200f);
                if (lure.y <= LureRest + 0.001f)
                {
                    EmitSilt(Proj(new Vector3(lure.x, set.floorY, lure.z)), 2);
                    Sfx.Play(Sfx.Nibble, 0.15f, 1.4f);
                }
            }
            else
            {
                falling = false;
                lureTilt = Mathf.MoveTowards(lureTilt, 0f, dt * 90f);
            }
        }

        /// <summary>The swamp: the frog / popper on the surface film, seen from below; a flick is a "pop".</summary>
        void SurfaceLure(float dt, bool live, LureInput li)
        {
            if (live && li.FlickNow)
            {
                popT = 0f;
                var at = Proj(lure);
                Emit(bubbleS, at, 4, 14f, lureSr.sortingOrder + 1);
                Emit(bubbleM, at, 2, 10f, lureSr.sortingOrder + 1);
                PopRing(new Vector3(lure.x, set.surfaceY, lure.z), 12f);
                Sfx.PlayVar(Sfx.Plop, 0.35f, 0.12f);
            }
            falling = false;
            if (live && li.Winding)
            {
                // a circle skims it along the surface, the frog kicking
                float m = Mathf.Max(0f, li.Speed) * 0.12f * dt;
                var next = lure + DragDir * m;
                if (new Vector2(next.x, next.z).magnitude <= set.lureBound)
                {
                    lure.x = next.x;
                    lure.z = next.z;
                    dragAcc += m;
                    if (dragAcc >= 0.25f)
                    {
                        dragAcc = 0f;
                        PopRing(new Vector3(lure.x, set.surfaceY, lure.z), 6f);
                    }
                }
            }
            popT += dt;
            float dip = popT < 0.2f ? -0.06f * Mathf.Sin(popT / 0.2f * Mathf.PI) : 0f;
            lure.y = LureRest + dip;
            lureTilt = Mathf.MoveTowards(lureTilt, popT < 0.2f ? 8f : 0f, dt * 120f);
        }

        /// <summary>
        /// The ocean: winding runs the kona forward (0.15 m a turn, a crawl limps 0.08 m a turn yawing +-15 degrees) with a
        /// bubble trail; paused it sinks 0.1 m/s (0.6 m at most); a flick darts it 0.2 m.
        /// </summary>
        void MidLure(float dt, bool live, LureInput li)
        {
            if (live && li.FlickNow)
            {
                dartLeft = 0.12f;
                dartV = DragDir * (0.2f / 0.12f);
                Emit(bubbleS, Proj(lure), 3, 16f, lureSr.sortingOrder + 1);
            }
            bool winding = live && li.Winding;
            bool limp = winding && li.Speed <= 1.2f;
            if (winding)
            {
                float m = Mathf.Max(0f, li.Speed) * (limp ? 0.08f : 0.15f) * dt;
                lure += DragDir * m;
                lure.y = Mathf.MoveTowards(lure.y, LureRest, 0.3f * dt);
                trailAcc += m;
                if (trailAcc >= 0.08f)
                {
                    trailAcc = 0f;
                    Emit(bubbleS, Proj(lure - DragDir * 0.08f), 1, 8f, lureSr.sortingOrder - 1);
                }
            }
            else if (dartLeft <= 0f) lure.y = Mathf.Max(LureRest - 0.6f, lure.y - 0.1f * dt);
            if (dartLeft > 0f)
            {
                float k = Mathf.Min(dt, dartLeft);
                dartLeft -= dt;
                lure += dartV * k;
            }
            falling = !winding && lure.y > LureRest - 0.6f + 0.001f;
            float want = limp ? 15f * Mathf.Sin(lureAnimT * Mathf.PI * 2f * 1.6f) : dartLeft > 0f ? 12f : falling ? -10f : 0f;
            if (spinT < 0.45f) want = spinT * 1440f;
            lureTilt = spinT < 0.45f ? want : Mathf.MoveTowards(lureTilt, want, dt * 160f);
        }

        Vector3 Mouth() => fish != null ? fish.Mouth - SetOrigin : fPos + Fwd(fHead) * 0.49f * fishScale;

        // ------------------------------------------------------------------ the legend
        static Vector3 Fwd(float headingDeg)
        {
            float r = headingDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>Forward with a pitch (+ = nose down).</summary>
        static Vector3 Fwd3(float headingDeg, float pitchDeg)
        {
            float r = headingDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r) * Mathf.Cos(p), -Mathf.Sin(p), Mathf.Cos(r) * Mathf.Cos(p));
        }

        static float HeadingOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        /// <summary>The pitch (+ = nose down) that points along <paramref name="v"/>.</summary>
        static float PitchOf(Vector3 v) => -Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;

        /// <summary>Where the root must be for the nose tip to be at <paramref name="nose"/> facing <paramref name="heading"/>.</summary>
        Vector3 RootForNose(Vector3 nose, float heading) => nose - Fwd(heading) * 0.5f * fishScale;

        Vector3 RootForNose(Vector3 nose, float heading, float pitch) => nose - Fwd3(heading, pitch) * 0.5f * fishScale;

        Vector3 CamDirFlat
        {
            get
            {
                var d = camPos - lure;
                d.y = 0f;
                return d.sqrMagnitude > 1e-4f ? d.normalized : new Vector3(-0.41f, 0f, -0.91f);
            }
        }

        /// <summary>The coelacanth (Docs/lures_legend_spec.md 2.7), unchanged by the rollout.</summary>
        void UpdateFish(float dt, LegendEncounter.Phase ph)
        {
            if (fish == null) return;
            // the bite: the jaws snap shut (0.08 s), then the fish alone freezes for a moment (the hit-stop, 0.12 s)
            if (closeT >= 0f)
            {
                closeT += dt;
                if (closeT >= 0.08f && closeT < 0.08f + def.hitStop) return;
            }
            float swimHz = 0.8f, finHz = 0.6f, amp = 1f, targetJaw = 0f, targetFlare = 0f, targetDorsal = 0f, skull = 0f, shake = 0f;
            float wantHeadYaw = 0f;
            bool hover = false;
            Vector3 target = fPos;
            float smooth = 0.6f, maxSpeed = 2.2f;
            var lureFlat = new Vector3(lure.x, 0f, lure.z);
            bool bodyOn = true, eyesOn = true;
            switch (ph)
            {
                case LegendEncounter.Phase.Omen:
                case LegendEncounter.Phase.Open:
                    bodyOn = eyesOn = false;
                    fPos = RootForNose(Eyes0, HeadingOf(lureFlat - Eyes0));
                    fHead = HeadingOf(lureFlat - Eyes0);
                    fVel = Vector3.zero;
                    break;
                case LegendEncounter.Phase.Eyes:
                {
                    // eyes in the dark: open at 0.2 s, one blink, drifting closer
                    bodyOn = false;
                    float t = enc.PhaseT, len = enc.PhaseLen;
                    float blinkAt = enc.Repeat ? 0.6f : 1.6f;
                    eyesOn = t >= 0.2f && !(t >= blinkAt && t < blinkAt + 0.1f);
                    var nose = Vector3.Lerp(Eyes0, Eyes1, Mathf.SmoothStep(0f, 1f, t / len));
                    fHead = HeadingOf(lureFlat - nose);
                    var root = RootForNose(nose, fHead);
                    fVel = (root - fPos) / Mathf.Max(dt, 1e-4f);
                    fPos = root;
                    swimHz = 0.6f;
                    amp = 0.6f;
                    targetDorsal = 30f;
                    break;
                }
                case LegendEncounter.Phase.Approach:
                {
                    // head-on along an S-curve to the orbit entry, outside the light: a black silhouette with a cyan rim
                    if (!approachStarted)
                    {
                        approachStarted = true;
                        moodFrom = fPos;
                    }
                    float u = Mathf.Clamp01(enc.PhaseT / enc.PhaseLen);
                    var entry = lureFlat + DeepDir * def.orbitFar + new Vector3(0f, 0.45f, 0f);
                    var dir = entry - moodFrom;
                    var side = Vector3.Cross(Vector3.up, dir.normalized);
                    float e = Mathf.SmoothStep(0f, 1f, u);
                    var p = Vector3.Lerp(moodFrom, entry, e) + side * (0.7f * Mathf.Sin(e * Mathf.PI * 2f));
                    var v = (p - fPos) / Mathf.Max(dt, 1e-4f);
                    fPos = p;
                    fVel = Vector3.Lerp(fVel, v, 0.5f);
                    swimHz = 0.9f;
                    targetDorsal = 60f;
                    wantHeadYaw = 0f;
                    break;
                }
                case LegendEncounter.Phase.Tease:
                {
                    int mood = enc.Mood;
                    if (mood != orbitMood)
                    {
                        orbitMood = mood;
                        patrolT = 0f;
                        // the first curious pass crosses between the camera and the lure ~4 s into the mood
                        orbitTh = -2.0f;
                    }
                    patrolT += dt;
                    if (enc.Leaving)
                    {
                        // about to turn away: yawed off, drifting out to 3 m
                        var away = fPos - lureFlat;
                        away.y = 0f;
                        away = away.sqrMagnitude > 1e-3f ? away.normalized : DeepDir;
                        target = lureFlat + away * 3f + new Vector3(0f, 0.45f, 0f) + Vector3.Cross(Vector3.up, away) * 0.8f;
                        swimHz = 0.6f;
                        targetDorsal = 60f;
                        smooth = 1.2f;
                    }
                    else if (mood == 0)
                    {
                        // wary: patrols an arc on the far side, mostly outside the light
                        float th = Mathf.Atan2(DeepDir.z, DeepDir.x) + 1.1f * Mathf.Sin(0.32f * patrolT);
                        target = lureFlat + new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th)) * def.orbitFar + new Vector3(0f, 0.45f, 0f);
                        swimHz = 0.6f;
                        finHz = 0.5f;
                        targetDorsal = 60f;
                        smooth = 0.9f;
                    }
                    else if (mood == 1)
                    {
                        // curious: circles the lure (counter-clockwise from above) on an ellipse stretched towards the camera,
                        // fully lit, its head tracking every hop and fall
                        orbitTh += 0.5f * dt;
                        var uDir = CamDirFlat;
                        var vDir = new Vector3(-uDir.z, 0f, uDir.x);
                        float camD = new Vector2(camPos.x - lure.x, camPos.z - lure.z).magnitude;
                        float a = Mathf.Min(1.4f * def.orbitNear, Mathf.Max(def.orbitNear, camD - 1.2f));
                        target = lureFlat + uDir * (a * Mathf.Cos(orbitTh)) + vDir * (def.orbitNear * Mathf.Sin(orbitTh)) + new Vector3(0f, 0.32f, 0f);
                        swimHz = 0.8f;
                        finHz = 0.6f;
                        smooth = 0.5f;
                        var toLure = Quaternion.Inverse(Quaternion.Euler(0f, fHead, 0f)) * (lure - (fPos + Fwd(fHead) * 0.3f * fishScale));
                        wantHeadYaw = Mathf.Clamp(Mathf.Atan2(toLure.x, toLure.z) * Mathf.Rad2Deg, -30f, 30f);
                    }
                    else
                    {
                        // excited: stops noseDist from the lure facing it, nose-nudging, the jaw mouthing, the lobes flared
                        hover = true;
                        float nudge = 0.05f * (1f - Mathf.Cos(patrolT * Mathf.PI * 2f * 1.2f));
                        var nose = lure + SideDir * (def.noseDist - nudge) + new Vector3(0f, 0.02f, 0f);
                        target = RootForNose(nose, HeadingOf(-SideDir));
                        swimHz = 1.0f;
                        finHz = 0.7f;
                        amp = 0.6f;
                        smooth = 0.45f;
                        targetJaw = 4f + 4f * Mathf.Sin(patrolT * Mathf.PI * 2f * 1.2f);
                        targetFlare = 0.8f;
                        targetDorsal = 30f;
                    }
                    break;
                }
                case LegendEncounter.Phase.NoseIn:
                {
                    // lines up head-on at noseDist; the tell: eyes and fins flare
                    hover = true;
                    target = RootForNose(lure + SideDir * def.noseDist + new Vector3(0f, 0.02f, 0f), HeadingOf(-SideDir));
                    smooth = 0.3f;
                    swimHz = 1.0f;
                    amp = 0.5f;
                    targetFlare = enc.InTell ? 1f : 0.7f;
                    targetDorsal = enc.InTell ? 45f : 30f;
                    targetJaw = enc.InTell ? 6f : 2f;
                    break;
                }
                case LegendEncounter.Phase.Lunge:
                {
                    // the head drives onto the lure, jaws open, the skull tilts up
                    hover = true;
                    float k = Mathf.Clamp01(enc.PhaseT / enc.PhaseLen);
                    var nose = lure + SideDir * (def.noseDist * (1f - k * k)) + new Vector3(0f, 0.02f, 0f);
                    fHead = Mathf.MoveTowardsAngle(fHead, HeadingOf(-SideDir), 400f * dt);
                    fPos = RootForNose(nose, fHead) + Fwd(fHead) * 0.05f * fishScale;
                    fVel = Vector3.zero;
                    target = fPos;
                    swimHz = 2.5f;
                    targetJaw = 40f * Mathf.Clamp01(enc.PhaseT / 0.12f);
                    jaw = targetJaw;
                    skull = 8f;
                    targetFlare = 1f;
                    targetDorsal = 40f;
                    break;
                }
                case LegendEncounter.Phase.HookWindow:
                case LegendEncounter.Phase.Hooked:
                case LegendEncounter.Phase.Surface:
                {
                    hover = true;
                    target = fPos;
                    fVel = Vector3.zero;
                    float c = closeT >= 0f ? closeT : 0f;
                    jaw = targetJaw = 40f * (1f - Mathf.Clamp01(c / 0.08f));
                    swimHz = ph == LegendEncounter.Phase.HookWindow ? 1.4f : 3f;
                    amp = ph == LegendEncounter.Phase.HookWindow ? 0.7f : 1.2f;
                    targetFlare = 0.6f;
                    targetDorsal = 40f;
                    skull = Mathf.Lerp(8f, 0f, Mathf.Clamp01(c / 0.2f));
                    if (shakeT >= 0f) shake = 25f * Mathf.Sin(shakeT * Mathf.PI * 2f * 5f);
                    break;
                }
                case LegendEncounter.Phase.TurnAway:
                case LegendEncounter.Phase.Close:
                {
                    // turns away into the deep: the light shrinks first, the eyes fade last
                    if (lureInMouth)
                    {
                        // spat out: the lure drops back
                        lureInMouth = false;
                        lure = Mouth();
                    }
                    target = lureFlat + DeepDir * 9f + new Vector3(0f, 0.6f, 0f);
                    smooth = 1.0f;
                    maxSpeed = 2.6f;
                    swimHz = 1.4f;
                    targetDorsal = 60f;
                    eyesOn = enc.Ph == LegendEncounter.Phase.TurnAway && enc.PhaseT < 1.0f;
                    break;
                }
            }
            if (shakeT >= 0f) shakeT += dt;
            bool driven = ph == LegendEncounter.Phase.Tease || ph == LegendEncounter.Phase.NoseIn || ph == LegendEncounter.Phase.TurnAway
                          || ph == LegendEncounter.Phase.Close;
            if (driven)
            {
                fPos = Vector3.SmoothDamp(fPos, target, ref fVel, smooth, maxSpeed, dt);
                fPos.y = Mathf.Max(0.2f * fishScale * 0.5f, fPos.y);
            }
            // heading: along the swim, or at the lure while hovering
            float want = fHead;
            var flat = new Vector3(fVel.x, 0f, fVel.z);
            if (hover) want = HeadingOf(new Vector3(lure.x - fPos.x, 0f, lure.z - fPos.z));
            else if (flat.magnitude > 0.12f) want = HeadingOf(flat);
            if (enc.Leaving) want += 60f;
            float before = fHead;
            fHead = Mathf.MoveTowardsAngle(fHead, want, (hover ? 220f : 140f) * dt);
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(before, fHead) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt / 0.15f));
            fBank = Mathf.Lerp(fBank, Mathf.Clamp(-yawRate * 0.12f, -10f, 10f), 1f - Mathf.Exp(-dt / 0.3f));
            // a flinch: jerks back 0.3 m in 0.15 s with a tail snap
            flinchT += dt;
            dimEyeT += dt;
            dorsalUpT += dt;
            float fl = flinchT < 0.15f ? flinchT / 0.15f : Mathf.Clamp01(1f - (flinchT - 0.15f) / 0.5f);
            var flinchOff = flinchDir * (0.3f * fl * (flinchT < 0.65f ? 1f : 0f));
            float tailSnap = flinchT < 0.3f ? 25f * Mathf.Sin(flinchT / 0.3f * Mathf.PI) : 0f;
            if (dorsalUpT < 0.8f) targetDorsal = 60f;
            swimPh += dt * swimHz * Mathf.PI * 2f;
            finPh += dt * finHz * Mathf.PI * 2f;
            headYaw = Mathf.Lerp(headYaw, wantHeadYaw, 1f - Mathf.Exp(-dt / 0.25f));
            if (ph != LegendEncounter.Phase.Lunge && ph != LegendEncounter.Phase.HookWindow) jaw = Mathf.Lerp(jaw, targetJaw, 1f - Mathf.Exp(-dt / 0.1f));
            flare = Mathf.Lerp(flare, targetFlare, 1f - Mathf.Exp(-dt / 0.2f));
            dorsal = Mathf.Lerp(dorsal, targetDorsal, 1f - Mathf.Exp(-dt / 0.15f));
            fish.Place(SetOrigin + fPos + flinchOff, fHead, fBank, fishScale);
            fish.Apply(new Legend3D.Pose
            {
                swim = swimPh, swimAmp = amp, fin = finPh, finAmp = 1f, headYaw = headYaw,
                bend = Mathf.Clamp(yawRate * 0.15f, -12f, 12f), jaw = jaw, skull = skull, dorsal1 = dorsal, flare = flare,
                tailSnap = tailSnap, shake = shake,
            });
            fish.SetBodyVisible(bodyOn);
            fish.SetEyesVisible(eyesOn);
            eyesShown = eyesOn;
            bodyShown = bodyOn;
        }

        bool eyesShown, bodyShown;

        // ------------------------------------------------------------------ the rollout legends' choreography
        /// <summary>A point on a circle of radius <paramref name="r"/> around the lure (x = cos, z = sin: CCW from above).</summary>
        Vector3 Around(float r, float th, float y) => new Vector3(lure.x + Mathf.Cos(th) * r, y, lure.z + Mathf.Sin(th) * r);

        /// <summary>The height the patrols are measured from: the floor in a floor set, the lure's rest in the others.</summary>
        float BaseY => set.lureAt == LureAt.Floor ? set.floorY : LureRest;

        /// <summary>
        /// The five rollout legends (Docs/legends_rollout.md 3.x): eyes in the dark, the approach, the moods' patrols,
        /// the hover, the fake-out, the tell and the style's bite; the same smoothing, heading, flinch and pose as the
        /// coelacanth, plus pitch and the species channels.
        /// </summary>
        void UpdateRollout(float dt, LegendEncounter.Phase ph)
        {
            if (fish == null) return;
            if (ph != LegendEncounter.Phase.NoseIn) lureHidden = false;   // (an early tap mid-taste gives the bait back)
            if (closeT >= 0f)
            {
                closeT += dt;
                if (closeT >= 0.08f && closeT < 0.08f + def.hitStop) return;
            }
            var st = cho.style;
            float swimHz = cho.tailHz.x, finHz = 0.6f, amp = 1f, tJaw = cho.cruiseJaw, tFlare = 0f, tDorsal = 0f, skull = 0f, shake = 0f;
            float wantHeadYaw = 0f, tPitch = 0f, tHeadPitch = 0f, tProtrude = 0f, tBarbel = 0.1f, tEyeRoll = 0f, bill = 0f;
            bool hover = false, bodyOn = true, eyesOn = true, driven = false, velPitch = true, jawDirect = false;
            float faceHeading = float.NaN;
            Vector3 target = fPos;
            float smooth = 0.6f, maxSpeed = cho.cruise * 1.6f + 0.6f;
            var lureFlat = new Vector3(lure.x, BaseY, lure.z);
            var uDir = CamDirFlat;
            var vDir = new Vector3(-uDir.z, 0f, uDir.x);
            var D = new Vector3(DragDir.x, 0f, DragDir.z).normalized;          // the lure's travel
            var V = Vector3.Cross(Vector3.up, D).normalized;
            // the side the fish noses in from: the coelacanth's (right of the lure, towards the camera) for most
            var side = SideDir;
            if (st == ChoreoStyle.GreatWhite) side = Quaternion.Euler(0f, 25f, 0f) * -uDir;   // head-on, from the far side
            float hoverPitch = cho.hover.y;
            glowTarget = 0f;
            switch (ph)
            {
                case LegendEncounter.Phase.Omen:
                case LegendEncounter.Phase.Open:
                {
                    bodyOn = eyesOn = false;
                    var to = lure - Eyes0;
                    fHead = HeadingOf(to);
                    fPitch = Mathf.Clamp(PitchOf(to), -45f, 45f);
                    fPos = RootForNose(Eyes0, fHead, fPitch);
                    fVel = Vector3.zero;
                    break;
                }
                case LegendEncounter.Phase.Eyes:
                {
                    // eyes in the dark: open at 0.2 s, one blink, drifting closer
                    bodyOn = false;
                    float t = enc.PhaseT, len = enc.PhaseLen;
                    float blinkAt = enc.Repeat ? 0.6f : 1.6f;
                    eyesOn = t >= 0.2f && !(t >= blinkAt && t < blinkAt + 0.1f);
                    float e = Mathf.SmoothStep(0f, 1f, t / len);
                    if (st == ChoreoStyle.Marlin) e = Mathf.Pow(Mathf.Clamp01(t / len), 1.8f);   // rising, then a streak
                    var nose = Vector3.Lerp(Eyes0, Eyes1, e);
                    var to = lure - nose;
                    fHead = HeadingOf(to);
                    fPitch = Mathf.Clamp(PitchOf(to), -45f, 45f);
                    var root = RootForNose(nose, fHead, fPitch);
                    fVel = (root - fPos) / Mathf.Max(dt, 1e-4f);
                    fPos = root;
                    swimHz = cho.tailHz.x;
                    amp = 0.6f;
                    velPitch = false;
                    tPitch = fPitch;
                    break;
                }
                case LegendEncounter.Phase.Approach:
                    Approach(dt, lureFlat, D, ref swimHz, ref tDorsal, ref tPitch);
                    break;
                case LegendEncounter.Phase.Tease:
                {
                    int mood = enc.Mood;
                    if (mood != orbitMood)
                    {
                        orbitMood = mood;
                        patrolT = 0f;
                        // carry on round the lure from where it is
                        orbitTh = Mathf.Atan2(fPos.z - lure.z, fPos.x - lure.x);
                    }
                    patrolT += dt;
                    driven = true;
                    swimHz = mood == 0 ? cho.tailHz.x : mood == 1 ? cho.tailHz.y : cho.tailHz.z;
                    if (enc.Leaving)
                    {
                        // about to turn away: yawed off, drifting out to 3 m
                        var away = fPos - lureFlat;
                        away.y = 0f;
                        away = away.sqrMagnitude > 1e-3f ? away.normalized : new Vector3(DeepDir.x, 0f, DeepDir.z).normalized;
                        float y = set.lureAt == LureAt.Floor ? BaseY + 0.4f : LureRest + cho.orbitDepth;
                        if (topFlow) y = SurfY - top.waryPath.z - 0.3f;   // (from above: sinking out of sight too)
                        target = new Vector3(lure.x, y, lure.z) + away * 3f + Vector3.Cross(Vector3.up, away) * 0.8f;
                        swimHz = cho.tailHz.x;
                        smooth = 1.2f;
                        topMood = -1;
                    }
                    else if (topFlow) TopTeaseBeat(dt, mood, ref target, ref smooth, ref hover, ref tFlare, ref tPitch, ref tHeadPitch,
                        ref wantHeadYaw, ref faceHeading, ref amp, ref finHz);
                    else TeaseBeat(dt, mood, uDir, vDir, D, V, side, hoverPitch, ref target, ref smooth, ref maxSpeed, ref hover, ref tJaw,
                        ref tFlare, ref tDorsal, ref tPitch, ref tHeadPitch, ref tProtrude, ref tBarbel, ref wantHeadYaw, ref faceHeading, ref amp, ref finHz);
                    break;
                }
                case LegendEncounter.Phase.NoseIn:
                    driven = true;
                    if (topFlow) TopNoseInBeat(dt, ref target, ref smooth, ref hover, ref tJaw, ref tFlare, ref tDorsal, ref tPitch, ref faceHeading, ref amp);
                    else NoseInBeat(dt, D, V, side, hoverPitch, ref target, ref smooth, ref hover, ref tJaw, ref tFlare, ref tDorsal, ref tPitch,
                        ref tHeadPitch, ref tProtrude, ref tBarbel, ref tEyeRoll, ref wantHeadYaw, ref bill, ref faceHeading, ref amp);
                    swimHz = cho.tailHz.z;
                    break;
                case LegendEncounter.Phase.Lunge:
                    LungeBeat(dt, D, V, side, ref tJaw, ref tFlare, ref tDorsal, ref tHeadPitch, ref tProtrude, ref tBarbel, ref tEyeRoll, ref skull);
                    hover = true;
                    velPitch = false;
                    jawDirect = true;
                    swimHz = cho.tailHz.w;
                    tPitch = fPitch;
                    break;
                case LegendEncounter.Phase.HookWindow:
                case LegendEncounter.Phase.Hooked:
                case LegendEncounter.Phase.Surface:
                {
                    hover = true;
                    velPitch = false;
                    target = fPos;
                    fVel = Vector3.zero;
                    float c = closeT >= 0f ? closeT : 0f;
                    float open = BiteJaw();
                    jaw = tJaw = open * (1f - Mathf.Clamp01(c / 0.08f));
                    jawDirect = true;
                    swimHz = ph == LegendEncounter.Phase.HookWindow ? cho.tailHz.z * 1.4f : cho.tailHz.w;
                    amp = ph == LegendEncounter.Phase.HookWindow ? 0.7f : 1.2f;
                    tFlare = 0.6f;
                    tDorsal = st == ChoreoStyle.Marlin ? 75f : 30f;
                    tPitch = fPitch;
                    tHeadPitch = Mathf.Lerp(headPitch, 0f, Mathf.Clamp01(c / 0.4f));
                    tProtrude = st == ChoreoStyle.Carp || st == ChoreoStyle.Sturgeon ? 0.4f : st == ChoreoStyle.GreatWhite ? 0.5f : 0f;
                    tBarbel = 0.5f;
                    tEyeRoll = st == ChoreoStyle.GreatWhite && c < 0.6f ? 1f : 0f;
                    glowTarget = st == ChoreoStyle.Marlin ? 1f : 0f;
                    if (shakeT >= 0f) shake = 25f * Mathf.Sin(shakeT * Mathf.PI * 2f * 5f);
                    break;
                }
                case LegendEncounter.Phase.TurnAway:
                case LegendEncounter.Phase.Close:
                {
                    // turns away into the deep: the light shrinks first, the eyes fade last
                    if (lureInMouth)
                    {
                        // spat out: the lure drops back
                        lureInMouth = false;
                        lure = Mouth();
                        if (set.lureAt == LureAt.Surface) lure.y = LureRest;
                    }
                    driven = true;
                    target = lure + DeepDir * 9f + (set.lureAt == LureAt.Floor ? new Vector3(0f, 0.6f, 0f) : Vector3.zero);
                    smooth = 1.0f;
                    maxSpeed = Mathf.Max(2.6f, cho.cruise * 1.5f);
                    swimHz = cho.tailHz.y * 1.6f;
                    eyesOn = enc.Ph == LegendEncounter.Phase.TurnAway && enc.PhaseT < 1.0f;
                    break;
                }
            }
            if (shakeT >= 0f) shakeT += dt;
            if (driven)
            {
                fPos = Vector3.SmoothDamp(fPos, target, ref fVel, smooth, maxSpeed, dt);
                ClampFish(ph);
            }
            // heading: along the swim, or at the lure while hovering (or as the beat says)
            float want = fHead;
            var flat = new Vector3(fVel.x, 0f, fVel.z);
            if (!float.IsNaN(faceHeading)) want = faceHeading;
            else if (hover) want = HeadingOf(new Vector3(lure.x - fPos.x, 0f, lure.z - fPos.z));
            else if (flat.magnitude > 0.12f) want = HeadingOf(flat);
            if (enc.Leaving) want += 60f;
            float before = fHead;
            if (ph != LegendEncounter.Phase.Lunge || st != ChoreoStyle.Marlin)
                fHead = Mathf.MoveTowardsAngle(fHead, want, (hover ? 220f : 140f) * dt);
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(before, fHead) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt / 0.15f));
            fBank = Mathf.Lerp(fBank, Mathf.Clamp(-yawRate * 0.12f, -10f, 10f), 1f - Mathf.Exp(-dt / 0.3f));
            // pitch: the beat's, plus climbing / diving along the swim
            if (velPitch && !hover && fVel.magnitude > 0.15f) tPitch += Mathf.Clamp(PitchOf(fVel) * 0.7f, -35f, 35f);
            if (ph != LegendEncounter.Phase.Omen && ph != LegendEncounter.Phase.Open && ph != LegendEncounter.Phase.Eyes)
                fPitch = Mathf.MoveTowardsAngle(fPitch, Mathf.Clamp(tPitch, -70f, 70f), (hover ? 90f : 60f) * dt);
            // a flinch: jerks back 0.3 m in 0.15 s with a tail snap
            flinchT += dt;
            dimEyeT += dt;
            dorsalUpT += dt;
            float fl = flinchT < 0.15f ? flinchT / 0.15f : Mathf.Clamp01(1f - (flinchT - 0.15f) / 0.5f);
            var flinchOff = flinchDir * (0.3f * fl * (flinchT < 0.65f ? 1f : 0f));
            float tailSnap = flinchT < 0.3f ? 25f * Mathf.Sin(flinchT / 0.3f * Mathf.PI) : 0f;
            if (dorsalUpT < 0.8f) tDorsal = Mathf.Max(tDorsal, 40f);
            swimPh += dt * swimHz * Mathf.PI * 2f;
            finPh += dt * finHz * Mathf.PI * 2f;
            headYaw = Mathf.Lerp(headYaw, wantHeadYaw, 1f - Mathf.Exp(-dt / (st == ChoreoStyle.Marlin && enc.InFakeOut ? 0.03f : 0.25f)));
            if (!jawDirect) jaw = Mathf.Lerp(jaw, tJaw, 1f - Mathf.Exp(-dt / 0.1f));
            flare = Mathf.Lerp(flare, tFlare, 1f - Mathf.Exp(-dt / 0.2f));
            dorsal = Mathf.Lerp(dorsal, tDorsal, 1f - Mathf.Exp(-dt / 0.15f));
            headPitch = Mathf.Lerp(headPitch, tHeadPitch, 1f - Mathf.Exp(-dt / 0.08f));
            protrude = Mathf.Lerp(protrude, tProtrude, 1f - Mathf.Exp(-dt / 0.06f));
            barbel = Mathf.Lerp(barbel, tBarbel, 1f - Mathf.Exp(-dt / 0.1f));
            eyeRoll = Mathf.MoveTowards(eyeRoll, tEyeRoll, dt / 0.2f);
            // the marlin's stripes: by the gauge, flashing on each credit, lit from the tell on
            creditFlashT += dt;
            if (enc.Credits != lastCredits)
            {
                lastCredits = enc.Credits;
                creditFlashT = 0f;
            }
            if (st == ChoreoStyle.Marlin)
            {
                float g = ph == LegendEncounter.Phase.Tease ? Mathf.Clamp01((enc.Gauge - 40f) / 35f) : ph >= LegendEncounter.Phase.NoseIn && ph <= LegendEncounter.Phase.Surface ? 0.8f : 0f;
                if (enc.InTell || ph >= LegendEncounter.Phase.Lunge && ph <= LegendEncounter.Phase.Surface) g = 1f;
                if (creditFlashT < 0.3f) g = 1f;
                glowTarget = Mathf.Max(glowTarget, g);
            }
            glow = Mathf.MoveTowards(glow, glowTarget, dt * 4f);
            fish.Place(SetOrigin + fPos + flinchOff, fHead, fBank, fishScale, fPitch);
            fish.Apply(new Legend3D.Pose
            {
                swim = swimPh, swimAmp = amp, fin = finPh, finAmp = 1f, headYaw = headYaw,
                bend = Mathf.Clamp(yawRate * 0.15f, -12f, 12f), jaw = jaw, skull = skull, dorsal1 = dorsal, flare = flare,
                tailSnap = tailSnap, shake = shake, headPitch = headPitch, protrude = protrude, barbel = barbel, eyeRoll = eyeRoll,
                bill = bill, glow = glow,
            });
            fish.SetBodyVisible(bodyOn);
            fish.SetEyesVisible(eyesOn);
            eyesShown = eyesOn;
            bodyShown = bodyOn;
        }

        float glowTarget;

        /// <summary>Keeps the root in the water: over the floor, under the surface (the swamp's gulp and strike reach it).</summary>
        void ClampFish(LegendEncounter.Phase ph)
        {
            if (set.hasFloor) fPos.y = Mathf.Max(set.floorY + 0.1f * fishScale, fPos.y);
            if (!float.IsNaN(set.surfaceY))
            {
                float gap = set.lureAt == LureAt.Surface ? (breathT >= 0f ? 0.05f : 0.15f) * fishScale : 0.3f;
                fPos.y = Mathf.Min(set.surfaceY - gap, fPos.y);
            }
        }

        /// <summary>The approach (3 s): the body comes on as a silhouette with the set's rim, and swims to its patrol.</summary>
        void Approach(float dt, Vector3 lureFlat, Vector3 D, ref float swimHz, ref float tDorsal, ref float tPitch)
        {
            if (!approachStarted)
            {
                approachStarted = true;
                moodFrom = fPos;
            }
            var st = cho.style;
            float u = Mathf.Clamp01(enc.PhaseT / enc.PhaseLen);
            var deepFlat = new Vector3(DeepDir.x, 0f, DeepDir.z).normalized;
            Vector3 entry, p;
            float e;
            if (topFlow)
            {
                // watched from above next: a slow rising curve out of the murk that ends on the 경계 pass, arriving along
                // it (swimming across the top camera's view, not at it)
                entry = TopPathPoint(top.waryPath, top.entryAt);
                e = Mathf.SmoothStep(0f, 1f, u);
                var tan = tRight * (-top.waryPath.x * Mathf.Sin(top.entryAt)) + tUp * (top.waryPath.y * Mathf.Cos(top.entryAt));
                var ctrl = entry - tan.normalized * 1.8f + new Vector3(0f, -0.3f, 0f);
                float a = 1f - e;
                p = a * a * moodFrom + 2f * a * e * ctrl + e * e * entry;
            }
            else switch (st)
            {
                case ChoreoStyle.Arapaima:
                {
                    // a long slow rising spiral out of the murk below
                    entry = new Vector3(lure.x, LureRest + cho.orbitDepth, lure.z) + deepFlat * def.orbitFar;
                    e = Mathf.SmoothStep(0f, 1f, u);
                    var dir = (entry - moodFrom).normalized;
                    var sd = Vector3.Cross(Vector3.up, new Vector3(dir.x, 0f, dir.z).normalized);
                    float ph = e * Mathf.PI * 1.5f;
                    p = Vector3.Lerp(moodFrom, entry, e) + (sd * Mathf.Sin(ph) + new Vector3(dir.x, 0f, dir.z) * (Mathf.Cos(ph) - 1f) * 0.5f) * 2f * Mathf.Sin(e * Mathf.PI) * 0.6f;
                    break;
                }
                case ChoreoStyle.Sturgeon:
                    // a straight, slow line along the floor
                    entry = lureFlat + deepFlat * def.orbitFar + new Vector3(0f, cho.orbitDepth, 0f);
                    e = Mathf.SmoothStep(0f, 1f, u);
                    p = Vector3.Lerp(moodFrom, entry, e);
                    p.y = Mathf.Lerp(moodFrom.y, entry.y, e);
                    break;
                case ChoreoStyle.Marlin:
                    // up fast and straight, then flattening out behind the lure
                    entry = new Vector3(lure.x, LureRest + cho.orbitDepth, lure.z) - D * def.orbitFar;
                    e = 1f - Mathf.Pow(1f - u, 3f);
                    p = Vector3.Lerp(moodFrom, entry, e);
                    break;
                case ChoreoStyle.GreatWhite:
                {
                    // the huge silhouette rises slowly onto its circle
                    entry = Around(def.orbitFar, Mathf.Atan2(deepFlat.z, deepFlat.x), LureRest + cho.orbitDepth);
                    e = Mathf.SmoothStep(0f, 1f, u);
                    p = Vector3.Lerp(moodFrom, entry, e);
                    p.y = Mathf.Lerp(moodFrom.y, entry.y, Mathf.Sqrt(e));
                    break;
                }
                default:
                {
                    // the carp: a slow S-curve along the bottom, head slightly down; wide enough (1.1 m) that it swims
                    // across the view mid-way (4-5 m out) and its flank's silhouette shows, not only its head-on face
                    entry = lureFlat + deepFlat * def.orbitFar + new Vector3(0f, cho.orbitDepth, 0f);
                    var dir = entry - moodFrom;
                    var sd = Vector3.Cross(Vector3.up, dir.normalized);
                    e = Mathf.SmoothStep(0f, 1f, u);
                    p = Vector3.Lerp(moodFrom, entry, e) + sd * (1.1f * Mathf.Sin(e * Mathf.PI * 2f));
                    tPitch = 8f;
                    break;
                }
            }
            var v = (p - fPos) / Mathf.Max(dt, 1e-4f);
            fPos = p;
            ClampFish(LegendEncounter.Phase.Approach);
            fVel = Vector3.Lerp(fVel, v, 0.5f);
            swimHz = Mathf.Lerp(cho.tailHz.x, cho.tailHz.y, 0.5f) * 1.2f;
            tDorsal = st == ChoreoStyle.Carp ? 40f : 0f;
        }

        void TeaseBeat(float dt, int mood, Vector3 uDir, Vector3 vDir, Vector3 D, Vector3 V, Vector3 side, float hoverPitch,
            ref Vector3 target, ref float smooth, ref float maxSpeed, ref bool hover, ref float tJaw, ref float tFlare, ref float tDorsal,
            ref float tPitch, ref float tHeadPitch, ref float tProtrude, ref float tBarbel, ref float wantHeadYaw, ref float faceHeading,
            ref float amp, ref float finHz)
        {
            var li = ctl.LureIn;
            switch (cho.style)
            {
                case ChoreoStyle.Carp:
                    if (mood == 0)
                    {
                        // a slow CCW ellipse at orbitFar, half in the murk, the dorsal up
                        orbitTh += cho.orbitSpeed.x * dt;
                        target = Around(def.orbitFar, orbitTh, BaseY + cho.orbitDepth);
                        tDorsal = 40f;
                        smooth = 0.9f;
                    }
                    else if (mood == 1)
                    {
                        // grubbing: nose-down 20 degrees, the nose 0.2 m over the mud, following every drag; digs now and then
                        orbitTh += cho.orbitSpeed.y * dt;
                        float pitch = 20f;
                        digT -= dt;
                        if (digT <= 0f)
                        {
                            digT = Random.Range(3f, 5f);
                            digOn = 0f;
                        }
                        if (digOn >= 0f)
                        {
                            digOn += dt;
                            pitch = 32f;
                            tBarbel = 0.8f;
                            if (digOn >= 0.35f && digOn - dt < 0.35f) EmitSilt(Proj(new Vector3(Mouth().x, set.floorY, Mouth().z)), 2);
                            if (digOn > 0.7f) digOn = -1f;
                        }
                        float rootY = BaseY + 0.2f + Mathf.Sin(pitch * Mathf.Deg2Rad) * 0.5f * fishScale;
                        target = Around(def.orbitNear, orbitTh, rootY);
                        tPitch = pitch;
                        tBarbel = Mathf.Max(tBarbel, 0.25f);
                        smooth = 0.5f;
                        HeadTrack(ref wantHeadYaw, 30f);
                    }
                    else
                    {
                        // hovers nose-down, the lips 0.4 m from the bait, the barbels on the floor, the lips pulsing
                        hover = true;
                        var nose = lure + side * def.noseDist + new Vector3(0f, cho.hover.x, 0f);
                        float hd = HeadingOf(-side);
                        target = RootForNose(nose, hd, hoverPitch);
                        faceHeading = hd;
                        tPitch = hoverPitch;
                        tBarbel = 1f;
                        tProtrude = 0.15f + 0.15f * Mathf.Sin(patrolT * Mathf.PI * 2f * 1.5f);
                        tFlare = 0.6f;
                        tDorsal = 20f;
                        amp = 0.5f;
                        smooth = 0.45f;
                    }
                    break;
                case ChoreoStyle.Arapaima:
                    if (mood == 0)
                    {
                        // a huge dark shape passing under the lure; once, if the gauge is low, it rises to gulp air
                        orbitTh += cho.orbitSpeed.x * dt;
                        float y = LureRest + cho.orbitDepth;
                        if (!breathDone && breathT < 0f && patrolT > 2.5f && enc.Gauge < 40f) breathT = 0f;
                        if (breathT >= 0f)
                        {
                            breathT += dt;
                            float up = breathT < 0.9f ? Mathf.SmoothStep(0f, 1f, breathT / 0.9f) : breathT < 1.3f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (breathT - 1.3f) / 1.1f);
                            y = Mathf.Lerp(y, set.surfaceY - 0.05f * fishScale, up);
                            tPitch = breathT < 1.3f ? -25f : 10f;
                            if (breathT >= 0.9f && breathT - dt < 0.9f)
                            {
                                Boil(new Vector3(Mouth().x, set.surfaceY, Mouth().z));
                                Sfx.Play(Sfx.Bubble, 0.8f, 0.7f);
                            }
                            if (breathT > 2.4f)
                            {
                                breathT = -1f;
                                breathDone = true;
                            }
                        }
                        target = Around(def.orbitFar, orbitTh, y);
                        smooth = 0.9f;
                    }
                    else if (mood == 1)
                    {
                        // 1.2 m round, 0.5 m under the surface; the head tilts up at the lure after every pop
                        orbitTh += cho.orbitSpeed.y * dt;
                        target = Around(def.orbitNear, orbitTh, LureRest - 0.5f);
                        if (li.FlickNow) popLookT = 0f;
                        popLookT += dt;
                        if (popLookT < 1.0f)
                        {
                            tHeadPitch = -20f;
                            HeadTrack(ref wantHeadYaw, 35f);
                        }
                        smooth = 0.6f;
                    }
                    else
                    {
                        // hangs 1.0 m directly under the lure, head up, the pectorals sculling, the tail sweeping slowly
                        hover = true;
                        var nose = lure + new Vector3(0f, cho.hover.x, 0f) + side * 0.15f;
                        float hd = HeadingOf(-side);
                        target = RootForNose(nose, hd, hoverPitch);
                        faceHeading = hd;
                        tPitch = hoverPitch;
                        tFlare = 0.5f;
                        amp = 0.6f;
                        finHz = 0.9f;
                        smooth = 0.6f;
                    }
                    break;
                case ChoreoStyle.Sturgeon:
                    if (mood == 0)
                    {
                        // mowing back and forth past the worm on a long oval behind it, outside the light column
                        orbitTh += cho.orbitSpeed.x * dt;
                        var c = new Vector3(lure.x, BaseY + cho.orbitDepth, lure.z) - uDir * 1.8f;
                        target = c + vDir * (def.orbitFar * Mathf.Cos(orbitTh)) + uDir * (0.7f * Mathf.Sin(orbitTh));
                        smooth = 0.9f;
                    }
                    else if (mood == 1)
                    {
                        // passes through the light column on every lap, the snout sweeping the bottom +-12 degrees
                        orbitTh += cho.orbitSpeed.y * dt;
                        var c = new Vector3(lure.x, BaseY + cho.orbitDepth - 0.03f, lure.z);
                        target = c + vDir * (def.orbitNear * Mathf.Cos(orbitTh)) + uDir * (0.45f * Mathf.Sin(orbitTh));
                        wantHeadYaw = 12f * Mathf.Sin(patrolT * Mathf.PI * 2f / 3f);
                        tBarbel = 0.4f;
                        smooth = 0.6f;
                    }
                    else
                    {
                        // glides in and stops, the snout 0.35 m over the worm, the pectorals planing, the barbels on it
                        hover = true;
                        var nose = lure + side * def.noseDist + new Vector3(0f, cho.hover.x, 0f);
                        float hd = HeadingOf(-side);
                        target = RootForNose(nose, hd, hoverPitch);
                        faceHeading = hd;
                        tPitch = hoverPitch;
                        tBarbel = 1f;
                        tFlare = 0.5f;
                        amp = 0.5f;
                        smooth = 0.7f;
                    }
                    break;
                case ChoreoStyle.Marlin:
                    if (mood == 0)
                    {
                        // tails the lure 3 m behind it, weaving +-1 m at 0.5 Hz, the sail folded
                        float wv = Mathf.Sin(patrolT * Mathf.PI * 2f * 0.5f);
                        target = new Vector3(lure.x, LureRest + cho.orbitDepth, lure.z) - D * def.orbitFar + V * wv;
                        faceHeading = HeadingOf(D) + 25f * Mathf.Cos(patrolT * Mathf.PI * 2f * 0.5f);
                        smooth = 0.35f;
                        maxSpeed = 6f;
                    }
                    else if (mood == 1)
                    {
                        // fast figure-eight passes (2.5 m/s) crossing between the camera and the lure, the sail half up
                        orbitTh += 1.75f * dt;
                        var c = new Vector3(lure.x, LureRest + cho.orbitDepth, lure.z) + uDir * 0.6f;
                        target = c + vDir * (def.orbitNear * Mathf.Sin(orbitTh)) + uDir * (0.5f * def.orbitNear * Mathf.Sin(orbitTh) * Mathf.Cos(orbitTh));
                        tDorsal = 35f;
                        smooth = 0.25f;
                        maxSpeed = 6f;
                    }
                    else
                    {
                        // lit up, the sail raised, the pectorals flared: cruising right behind the lure, matching it
                        hover = true;
                        var nose = new Vector3(lure.x, lure.y - 0.05f, lure.z) - D * 0.5f;
                        float hd = HeadingOf(D);
                        target = RootForNose(nose, hd, 0f);
                        faceHeading = hd;
                        tDorsal = 75f;
                        tFlare = 1f;
                        smooth = 0.35f;
                        maxSpeed = 6f;
                        amp = 0.7f;
                    }
                    break;
                case ChoreoStyle.GreatWhite:
                    if (mood == 0)
                    {
                        // a huge slow circle under the lure: its near side sweeps across the camera like a grey wall
                        orbitTh += cho.orbitSpeed.x * dt;
                        target = Around(def.orbitFar, orbitTh, LureRest + cho.orbitDepth);
                        smooth = 1.0f;
                    }
                    else if (mood == 1)
                    {
                        // closer and lower
                        orbitTh += cho.orbitSpeed.y * dt;
                        target = Around(def.orbitNear, orbitTh, LureRest + cho.orbitDepth - 0.3f);
                        smooth = 0.9f;
                    }
                    else
                    {
                        // a spiral tightening to 1.6 m, then head-on 1.5 m out, closing slowly
                        if (patrolT < 2.5f)
                        {
                            orbitTh += 0.35f * dt;
                            target = Around(Mathf.Lerp(def.orbitNear, 1.6f, patrolT / 2.5f), orbitTh, LureRest + cho.orbitDepth + 0.1f);
                            smooth = 0.8f;
                        }
                        else
                        {
                            hover = true;
                            float close = Mathf.Clamp01((patrolT - 2.5f) / 5f);
                            var nose = lure + side * (def.noseDist - 0.3f * close) + new Vector3(0f, -0.1f, 0f);
                            float hd = HeadingOf(-side);
                            target = RootForNose(nose, hd, 0f);
                            faceHeading = hd;
                            smooth = 0.9f;
                            tJaw = 8f;
                        }
                    }
                    break;
            }
        }

        /// <summary>The head turns towards the lure, up to <paramref name="max"/> degrees.</summary>
        void HeadTrack(ref float wantHeadYaw, float max)
        {
            var toLure = Quaternion.Inverse(Quaternion.Euler(0f, fHead, 0f)) * (lure - (fPos + Fwd(fHead) * 0.3f * fishScale));
            wantHeadYaw = Mathf.Clamp(Mathf.Atan2(toLure.x, toLure.z) * Mathf.Rad2Deg, -max, max);
        }

        /// <summary>The nose-in: lined up at noseDist, the fake-out mid-way (the carp tastes, the sturgeon feels, the marlin
        /// slashes, the great white bumps), then the tell.</summary>
        void NoseInBeat(float dt, Vector3 D, Vector3 V, Vector3 side, float hoverPitch, ref Vector3 target, ref float smooth,
            ref bool hover, ref float tJaw, ref float tFlare, ref float tDorsal, ref float tPitch, ref float tHeadPitch, ref float tProtrude,
            ref float tBarbel, ref float tEyeRoll, ref float wantHeadYaw, ref float bill, ref float faceHeading, ref float amp)
        {
            hover = true;
            smooth = 0.3f;
            amp = 0.5f;
            bool tell = enc.InTell, fake = enc.InFakeOut;
            float fk = enc.FakeOutK;
            // in and back out over the fake-out (a touch at its middle)
            float touch = fake ? Mathf.Sin(fk * Mathf.PI) : 0f;
            if (fake) smooth = 0.07f;   // (the touch is quick: the nose must reach the lure within the 0.45 s)
            tFlare = tell ? 1f : 0.6f;
            twitchT += dt;
            switch (cho.style)
            {
                case ChoreoStyle.Carp:
                {
                    float dist = Mathf.Lerp(def.noseDist, 0.06f, touch);
                    var nose = lure + side * dist + new Vector3(0f, cho.hover.x * (1f - touch), 0f);
                    float hd = HeadingOf(-side);
                    target = RootForNose(nose, hd, hoverPitch);
                    faceHeading = hd;
                    tPitch = hoverPitch;
                    tBarbel = 1f;
                    tProtrude = fake ? 0.6f * touch : tell ? 0.25f : 0.1f;
                    // the bait vanishes into the lips for a moment and pops back out with a golden puff
                    bool gone = fake && fk > 0.25f && fk < 0.8f;
                    if (lureHidden && !gone) CrumbPuff(Proj(lure), 6);
                    lureHidden = gone;
                    tJaw = tell ? 12f : 4f;
                    break;
                }
                case ChoreoStyle.Arapaima:
                {
                    var nose = lure + new Vector3(0f, -def.noseDist, 0f) + side * 0.15f;
                    float hd = HeadingOf(-side);
                    target = RootForNose(nose, hd, hoverPitch);
                    faceHeading = hd;
                    tPitch = hoverPitch;
                    tJaw = tell ? 10f : 2f;
                    if (tell && (bubbleT -= dt) <= 0f)
                    {
                        bubbleT = 0.07f;
                        Emit(bubbleS, Proj(Mouth()), 1, 22f);
                    }
                    break;
                }
                case ChoreoStyle.Sturgeon:
                {
                    var nose = lure + side * def.noseDist + new Vector3(0f, cho.hover.x, 0f);
                    float hd = HeadingOf(-side);
                    target = RootForNose(nose, hd, hoverPitch);
                    faceHeading = hd;
                    tPitch = hoverPitch;
                    // the barbels sweep across the worm: two twitches
                    tBarbel = fake && (fk < 0.33f || (fk > 0.5f && fk < 0.83f)) ? 1.4f : 1f;
                    if (fake) wantHeadYaw = 8f * Mathf.Sin(fk * Mathf.PI * 2f);
                    tProtrude = tell ? 0.3f : 0.05f;
                    break;
                }
                case ChoreoStyle.Marlin:
                {
                    var nose = new Vector3(lure.x, lure.y - 0.05f, lure.z) - D * 0.5f;
                    float hd = HeadingOf(D);
                    target = RootForNose(nose, hd, 0f);
                    faceHeading = hd;
                    // the bill's slash: the head whips +-25 degrees in 0.12 s
                    if (fake && enc.PhaseT - enc.FakeAt < 0.24f)
                    {
                        float s = Mathf.Sin((enc.PhaseT - enc.FakeAt) / 0.12f * Mathf.PI);
                        wantHeadYaw = 25f * s;
                        bill = 6f * s;
                    }
                    tDorsal = tell ? 75f : 50f;
                    tFlare = tell ? 1f : 0.8f;
                    glowTarget = tell ? 1f : 0.7f;
                    break;
                }
                case ChoreoStyle.GreatWhite:
                {
                    float hd = HeadingOf(-side);
                    // the bump: in onto the lure and back; the lure is knocked 0.3 m on at the touch
                    float dist = Mathf.Lerp(def.noseDist - 0.3f, 0.05f, touch);
                    var nose = lure + side * dist + new Vector3(0f, -0.05f, 0f);
                    target = RootForNose(nose, hd, 0f);
                    faceHeading = hd;
                    if (fake && fk >= 0.5f && !fakeKnocked)
                    {
                        fakeKnocked = true;
                        knockV = -side * (0.3f / 0.25f);
                        knockT = 0f;
                        Emit(bubbleS, Proj(lure), 5, 18f);
                        Sfx.Play(Sfx.Nibble, 0.8f, 0.6f);
                    }
                    // the tell: the eyes roll back, the mouth starts to open
                    tEyeRoll = tell ? 1f : 0f;
                    tJaw = tell ? 18f : 8f;
                    break;
                }
            }
            tDorsal = cho.style == ChoreoStyle.Marlin ? tDorsal : tell ? 30f : 15f;
        }

        /// <summary>The largest jaw opening of this legend's bite (degrees).</summary>
        float BiteJaw()
        {
            switch (cho.style)
            {
                case ChoreoStyle.Carp: return 25f;
                case ChoreoStyle.Arapaima: return 45f;
                case ChoreoStyle.Sturgeon: return 20f;
                case ChoreoStyle.Marlin: return 30f;
                case ChoreoStyle.GreatWhite: return 50f;
                default: return 40f;
            }
        }

        /// <summary>
        /// The lunge (lungeT): the carp sucks the bait in with its lips out, the arapaima rockets up from below, the
        /// sturgeon's tube drops and vacuums the worm up in a silt cloud, the marlin swings in from the side and grabs the
        /// kona crosswise, the great white lifts its snout and throws its upper jaw out.
        /// </summary>
        void LungeBeat(float dt, Vector3 D, Vector3 V, Vector3 side, ref float tJaw, ref float tFlare, ref float tDorsal,
            ref float tHeadPitch, ref float tProtrude, ref float tBarbel, ref float tEyeRoll, ref float skull)
        {
            float t = enc.PhaseT, k = Mathf.Clamp01(t / enc.PhaseLen);
            if (!lungeFromSet)
            {
                lungeFromSet = true;
                lungeFrom = fPos + Fwd3(fHead, fPitch) * 0.5f * fishScale;
                lungeHead = fHead;
                lungePitch = fPitch;
                if (cho.style == ChoreoStyle.Sturgeon)
                    for (int i = 0; i < 3; i++) EmitSilt(Proj(new Vector3(lure.x, set.floorY, lure.z)) + new Vector3(Random.Range(-6f, 6f), 0f, 0f), 2);
                if (cho.style == ChoreoStyle.Carp) CrumbPuff(Proj(lure), 8);
            }
            Vector3 nose;
            float head = lungeHead, pitch = lungePitch;
            // (the mouth, not the snout tip, ends on the lure: the marlin's bill and the shark's snout reach past it)
            float mb = fish.MouthBack * fishScale;
            switch (cho.style)
            {
                case ChoreoStyle.Carp:
                {
                    nose = Vector3.Lerp(lungeFrom, lure + side * 0.05f, k);
                    tHeadPitch = 25f * Mathf.Clamp01(t / 0.2f);
                    tProtrude = Mathf.Clamp01(t / 0.35f);
                    tBarbel = 1f;
                    jaw = BiteJaw() * Mathf.Clamp01(t / 0.2f);
                    // the bait streams into the tube
                    if (k > 0.35f) lure = Vector3.Lerp(lure, Mouth(), 1f - Mathf.Exp(-dt / 0.06f));
                    if ((bubbleT -= dt) <= 0f)
                    {
                        bubbleT = 0.06f;
                        CrumbPuff(Proj(lure), 2);
                    }
                    break;
                }
                case ChoreoStyle.Arapaima:
                {
                    // rockets up from below: the upturned jaw open, the gill covers flared
                    float e = k * k;
                    nose = Vector3.Lerp(lungeFrom, lure + new Vector3(0f, 0.02f, 0f), e);
                    pitch = Mathf.Lerp(lungePitch, -60f, k);
                    jaw = BiteJaw() * Mathf.Clamp01(t / 0.08f);
                    tFlare = 1f;
                    break;
                }
                case ChoreoStyle.Sturgeon:
                {
                    nose = Vector3.Lerp(lungeFrom, lure + side * 0.1f + new Vector3(0f, 0.12f, 0f), k);
                    tProtrude = Mathf.Clamp01(t / 0.18f);
                    tHeadPitch = 10f * k;
                    tBarbel = 1f;
                    jaw = BiteJaw() * Mathf.Clamp01(t / 0.18f);
                    // the worm is sucked up into the tube
                    if (k > 0.3f) lure = Vector3.Lerp(lure, Mouth(), 1f - Mathf.Exp(-dt / 0.05f));
                    break;
                }
                case ChoreoStyle.Marlin:
                {
                    // in from the side at 3 m/s: out to the side first, then across onto the kona
                    var ctrl = lure + V * 0.9f - D * 0.2f;
                    var end = lure + (lure - ctrl).normalized * mb;
                    float a = 1f - k;
                    nose = a * a * lungeFrom + 2f * a * k * ctrl + k * k * end;
                    var tan = 2f * a * (ctrl - lungeFrom) + 2f * k * (end - ctrl);
                    if (tan.sqrMagnitude > 1e-4f) head = HeadingOf(tan);
                    fHead = Mathf.MoveTowardsAngle(fHead, head, 720f * dt);
                    head = fHead;
                    pitch = 0f;
                    jaw = BiteJaw() * Mathf.Clamp01(t / 0.12f);
                    tDorsal = 75f;
                    tFlare = 1f;
                    glowTarget = 1f;
                    break;
                }
                case ChoreoStyle.GreatWhite:
                {
                    nose = Vector3.Lerp(lungeFrom, lure + Fwd(lungeHead) * mb, k * k);
                    tHeadPitch = -12f * Mathf.Clamp01(t / 0.15f);
                    tProtrude = Mathf.Clamp01(t / 0.2f);
                    tEyeRoll = 1f;
                    jaw = BiteJaw() * Mathf.Clamp01(t / 0.15f);
                    tFlare = 0.5f;
                    break;
                }
                default:
                    nose = lure;
                    break;
            }
            fPitch = pitch;
            if (cho.style != ChoreoStyle.Marlin) fHead = Mathf.MoveTowardsAngle(fHead, head, 400f * dt);
            fPos = RootForNose(nose, fHead, fPitch);
            ClampFish(LegendEncounter.Phase.Lunge);
            fVel = Vector3.zero;
            tJaw = jaw;
        }

        // ------------------------------------------------------------------ camera
        void UpdateCamera(float dt)
        {
            var ph = enc.Ph;
            // follows the lure (smoothed), dollies in from the rest distance to 87 % of it over the approach
            var want = new Vector3(lure.x, LureRest + (lure.y - LureRest) * 0.3f, lure.z);
            if (lureInMouth) want = track;
            track = dt <= 0f ? want : Vector3.Lerp(track, want, 1f - Mathf.Exp(-dt / 0.4f));
            float rest = set.camDist * def.camScale, dolly = rest * (2.6f / 3.0f);
            float dist = rest;
            if (ph == LegendEncounter.Phase.Approach) dist = Mathf.Lerp(rest, dolly, Mathf.SmoothStep(0f, 1f, enc.PhaseT / enc.PhaseLen));
            else if (ph > LegendEncounter.Phase.Approach) dist = dolly;
            var restPos = track + CamRest.normalized * dist;
            // the nose-in's push-in (the carp): in on the hovering fish's head and the bait, part of the way in 흥분
            float pushWant = 0f;
            bool nosing = ph == LegendEncounter.Phase.NoseIn || ph == LegendEncounter.Phase.Lunge;
            if (def.noseFill.x > 0f && def.noseFill.y > 0f && fish != null && !topFlow)
            {
                if (nosing) pushWant = 1f;
                else if (ph == LegendEncounter.Phase.Tease && enc.Mood == 2 && !enc.Leaving) pushWant = def.noseCamTease;
            }
            if (dt <= 0f) push = pushWant;
            else push = Mathf.SmoothDamp(push, pushWant, ref pushVel, 0.18f, Mathf.Infinity, dt);
            if (pushWant <= 0f && push < 0.001f) pushHas = false;
            if (pushWant > 0f && (!pushHas || !nosing))
            {
                // the framing: the fish's outline (snout, tail tip, back and belly, the tail's lobes) filling noseFill of
                // the window, it and the lure centred on noseFrame and kept inside; followed while the fish hovers, held
                // from the nose-in on (the fake-out's dart in and back shows as the fish's own move)
                var fwd = -CamRest.normalized;
                var right = Vector3.Cross(Vector3.up, fwd).normalized;
                var up = Vector3.Cross(fwd, right);
                var F = fish.Forward;
                var U = Vector3.Cross(F, Vector3.Cross(Vector3.up, F).normalized);
                var root = SetOrigin + fPos;
                var tip = root - F * (0.5f * fishScale);
                framePts.Clear();
                framePts.Add(fish.Mouth);
                framePts.Add(fish.Mouth - U * (0.06f * fishScale));
                framePts.Add(root + U * (0.17f * fishScale));
                framePts.Add(root - U * (0.15f * fishScale));
                framePts.Add(tip + U * (0.15f * fishScale));
                framePts.Add(tip - U * (0.15f * fishScale));
                float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
                foreach (var p in framePts)
                {
                    float px = Vector3.Dot(p - root, right), py = Vector3.Dot(p - root, up);
                    x0 = Mathf.Min(x0, px); x1 = Mathf.Max(x1, px); y0 = Mathf.Min(y0, py); y1 = Mathf.Max(y1, py);
                }
                float d = f0 * Mathf.Max((x1 - x0) / (def.noseFill.x * window.width), (y1 - y0) / (def.noseFill.y * window.height));
                // the lure too, and the whole of it inside 94 % of the window
                var lw = SetOrigin + lure;
                float lx = Vector3.Dot(lw - root, right), ly = Vector3.Dot(lw - root, up);
                x0 = Mathf.Min(x0, lx); x1 = Mathf.Max(x1, lx); y0 = Mathf.Min(y0, ly); y1 = Mathf.Max(y1, ly);
                d = Mathf.Max(0.3f, d, f0 * Mathf.Max((x1 - x0) / (0.94f * window.width), (y1 - y0) / (0.94f * window.height)));
                var off = fPos + right * ((x0 + x1) * 0.5f) + up * ((y0 + y1) * 0.5f) - lure;
                float k = !pushHas || dt <= 0f ? 1f : 1f - Mathf.Exp(-dt / 0.4f);
                pushOff = Vector3.Lerp(pushOff, off, k);
                pushDist = Mathf.Lerp(pushDist, d, k);
                pushHas = true;
            }
            var basePos = restPos;
            var aim = track;
            var frameAt = set.frameAt;
            if (push > 0.001f && pushHas)
            {
                var focus = track + pushOff;
                basePos = Vector3.Lerp(restPos, focus + CamRest.normalized * pushDist, push);
                aim = Vector3.Lerp(track, focus, push);
                frameAt = Vector2.Lerp(set.frameAt, def.noseFrame, push);
            }
            // yaw towards the fish, up to 6 degrees
            float follow = 0f;
            if (fish != null && (ph == LegendEncounter.Phase.Approach || ph == LegendEncounter.Phase.Tease || ph == LegendEncounter.Phase.NoseIn))
            {
                var toFish = fPos - basePos;
                var toLure = track - basePos;
                follow = Mathf.Clamp(Mathf.DeltaAngle(HeadingOf(toLure), HeadingOf(toFish)) * 0.25f, -6f, 6f) * (1f - push);
            }
            followYaw = dt <= 0f ? follow : Mathf.Lerp(followYaw, follow, 1f - Mathf.Exp(-dt / 0.5f));
            // the lunge: dollies onto the head, the focal length up 1.5x, the view centred on the bite
            bool lunge = ph >= LegendEncounter.Phase.Lunge && ph <= LegendEncounter.Phase.Surface;
            if (lunge)
            {
                if (!lungeInit)
                {
                    lungeInit = true;
                    lungeCam = lure;
                    // the coelacanth's bite is framed a little towards its side; the others towards their head
                    lungeAim = rollout && fish != null ? (fish.HeadPos - SetOrigin - lure) * 0.35f : SideDir * 0.25f;
                }
                float k = ph == LegendEncounter.Phase.Lunge ? Mathf.Clamp01(enc.PhaseT / enc.PhaseLen) : 1f;
                lungeK = 1f - (1f - k) * (1f - k);
            }
            else lungeK = Mathf.MoveTowards(lungeK, 0f, dt * 3f);
            // (the bite's framing from the rest position, as without a push-in)
            var near = lungeCam + (restPos - lungeCam).normalized * (1.3f * def.camScale) + new Vector3(0f, 0.04f, 0f);
            camPos = Vector3.Lerp(basePos, near, lungeK);
            aim = Vector3.Lerp(aim, lungeCam + lungeAim, lungeK);
            f = f0 * Mathf.Lerp(1f, 1.5f, lungeK);
            pp = Vector2.Lerp(ppWin, new Vector2(w * 0.5f, h * 0.5f), lungeK);
            // the lure sits at the set's frame point in the window (the cave: 0.33 W, 0.30 H): the view turns right / up of it
            var to = aim - camPos;
            float yaw = HeadingOf(to), pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            float offX = (0.5f - frameAt.x) * window.width * (1f - lungeK), offY = (0.5f - frameAt.y) * window.height * (1f - lungeK);
            yaw += Mathf.Atan2(offX, f) * Mathf.Rad2Deg + followYaw;
            pitch += Mathf.Atan2(offY, f) * Mathf.Rad2Deg;
            camRot = Quaternion.Euler(-pitch, yaw, 0f);
            yawPx = Mathf.DeltaAngle(restYaw, yaw) * Mathf.Deg2Rad * f;
        }

        /// <summary>The fish camera at a set-frame pose with focal <paramref name="fl"/> px and principal point <paramref name="ppx"/>, into <paramref name="target"/>.</summary>
        void PlaceFishCamera(Vector3 pos, Quaternion rot, float fl, Vector2 ppx, RenderTexture target)
        {
            fishCam.targetTexture = target;
            fishCam.transform.SetPositionAndRotation(SetOrigin + pos, rot);
            float n = fishCam.nearClipPlane, fa = fishCam.farClipPlane;
            var m = Matrix4x4.zero;
            m[0, 0] = 2f * fl / w;
            m[0, 2] = 1f - 2f * ppx.x / w;
            m[1, 1] = 2f * fl / h;
            m[1, 2] = 1f - 2f * ppx.y / h;
            m[2, 2] = -(fa + n) / (fa - n);
            m[2, 3] = -2f * fa * n / (fa - n);
            m[3, 2] = -1f;
            fishCam.projectionMatrix = m;
        }

        // ------------------------------------------------------------------ the lure light (the darkness)
        void UpdateLight(LegendEncounter.Phase ph)
        {
            if (fish == null) return;
            float R = (bait != null && bait.glow ? def.lightGlow : def.lightPlain) * pLight;
            R *= 1f + 0.06f * Mathf.Sin(time * Mathf.PI * 2f * 1.5f);
            if (ph == LegendEncounter.Phase.TurnAway) R *= 1f - Mathf.Clamp01(enc.PhaseT / 0.7f);
            else if (ph == LegendEncounter.Phase.Close) R = 0f;
            lightR = R;
            // (w must stay > 0: the darkness stays on even when the light has shrunk to nothing); the ice's light is the
            // hole's column, centred over the worm's rest point
            var lp = SetOrigin + (lureInMouth ? Mouth() : lure);
            if (set.rayAnchored) lp = SetOrigin + new Vector3(0f, LureRest + set.lightUp, 0f);
            else lp.y += set.lightUp;
            // a daylight murk's silhouette: a step darker than the water near the light, melting into it further out (and
            // as the light goes out when it turns away)
            float sil = set.silFade.y > set.silFade.x
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(set.silFade.x, set.silFade.y, Vector3.Distance(SetOrigin + fPos, lp))) : 1f;
            if (ph == LegendEncounter.Phase.TurnAway) sil = Mathf.Max(sil, Mathf.Clamp01(enc.PhaseT / 0.7f));
            else if (ph == LegendEncounter.Phase.Close) sil = 1f;
            fish.SetLight(new Vector4(lp.x, lp.y, lp.z, Mathf.Max(0.001f, R)), Color.Lerp(abyssNear, abyss, sil),
                Color.Lerp(fogNear, fogOutline, sil), set.sunMix * pSun, set.keyDir);
            // the silhouette's top rim comes in over the first second of the approach
            if (fishMat.HasProperty(RimStrengthId))
            {
                float rim = ph <= LegendEncounter.Phase.Eyes ? 0f : ph == LegendEncounter.Phase.Approach ? rimPreset * Mathf.Clamp01(enc.PhaseT) : rimPreset;
                fishMat.SetFloat(RimStrengthId, rim);
            }
        }

        // ------------------------------------------------------------------ the underwater compose
        void ComposeUnderwater(float dt, LegendEncounter.Phase ph)
        {
            // the horizon the 3D camera sees (the floor's, 30 m out; the ocean's at the lure's depth); the backdrop layers
            // sit on it, shifted with its yaw
            horizon = SnapPx(Proj(new Vector3(camPos.x, set.horizonY, camPos.z + 30f)).y);
            float yp = yawPx;
            Place(bg, new Vector2(w * 0.5f - 0.1f * yp, horizon + (set.bgHorizon - 200f)));
            if (ceilSr != null)
            {
                // the surface / ice seen from below: its bottom row on the far edge of the surface
                float ceil = SnapPx(Proj(new Vector3(camPos.x, set.surfaceY, camPos.z + 30f)).y);
                Place(ceilSr, new Vector2(w * 0.5f - 0.6f * yp, ceil + 60f));
                if (ceilFillSr != null)
                {
                    // from the layer's top edge (1 px under it, no seam) to past the view's top (the shake's margin)
                    float top = Mathf.Round(ceil + 60f) + ceilSr.sprite.rect.height * 0.5f - 1f, span = h + 8f - top;
                    ceilFillSr.enabled = span > 0f;
                    if (span > 0f)
                    {
                        ceilFillSr.transform.localScale = new Vector3(w + 2f, span, 1f);
                        ceilFillSr.transform.position = U(new Vector2(w * 0.5f, top + span * 0.5f));
                    }
                }
            }
            for (int i = 0; i < rays.Count; i++)
            {
                var r = set.rays[i];
                float sway = 3f * Mathf.Sin(time * Mathf.PI * 2f * 0.1f + i * 1.7f);
                Place(rays[i], new Vector2(w - r.xFromRight + 32f - 0.2f * yp + sway, h - r.y - 128f));
            }
            Place(mid, new Vector2(w * 0.5f - 0.35f * yp, horizon + 100f));
            float pulse = 1f + set.midPulse * Mathf.Sin(time * Mathf.PI * 2f * 0.3f);
            mid.color = new Color(pulse * pTint.r, pulse * pTint.g, pulse * pTint.b, 1f);
            if (floorSr != null) Place(floorSr, new Vector2(w * 0.5f - 0.8f * yp, horizon - 60f));
            if (rayCol != null) PlaceColumn();
            Place(fore, new Vector2(set.foreX + 128f - 1.3f * yp, 64f));
            // the Eyes beat's veil over the back layers, gone over the first 1.5 s of the approach
            if (veil != null)
            {
                float va = ph <= LegendEncounter.Phase.Eyes ? 1f : ph == LegendEncounter.Phase.Approach ? 1f - Mathf.Clamp01(enc.PhaseT / 1.5f) : 0f;
                veil.enabled = va > 0.001f;
                if (veil.enabled)
                {
                    veil.color = Art.Hex(set.veil, set.veilAlpha * va);
                    veil.transform.localScale = new Vector3(w + 2, h + 2, 1f);
                    veil.transform.position = U(new Vector2(w * 0.5f, h * 0.5f));
                }
            }
            // the lure, its halo and the line down to it
            var lp = Proj(lureInMouth ? Mouth() : lure);
            var lpx = new Vector2(SnapPx(lp.x), SnapPx(lp.y));
            uwLurePx = lpx;
            bool glowLure = bait == null || bait.glow;
            float haloA = glowLure ? set.haloGlow : set.haloPlain;
            if (set.haloBait != null && bait != null && bait.id == set.haloBait) haloA = set.haloBaitAlpha;
            halo.enabled = !lureInMouth && !lureHidden && set.halo != null && haloA > 0f;
            Place(halo, lpx);
            float hp = Mathf.Repeat(time * 1.5f, 1f) < 0.5f ? 1f : 0.82f;
            halo.color = Art.Hex(set.halo ?? "#9affea", Mathf.Clamp01(haloA * hp * pHalo));
            lureSr.enabled = !lureInMouth && !lureHidden;
            bool moving = falling || ctl.LureIn.Winding;
            int fr = (int)(lureAnimT * (moving ? 5f : 1.5f)) % 2;
            if (set.lureAt == LureAt.Surface) fr = popT < 0.2f ? 1 : ctl.LureIn.Winding ? fr : 0;
            lureSr.sprite = lureFrames[fr];
            int scale = lungeK > 0.5f || push > 0.5f ? 2 : 1;
            lureSr.transform.localScale = new Vector3(scale, scale, 1f);
            lureSr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Round(lureTilt / 5f) * 5f);
            Place(lureSr, lpx);
            // the lure goes behind the fish when the fish is between it and the camera
            float fishDepth = Proj(fPos).z;
            lureSr.sortingOrder = fish != null && fishDepth < lp.z - 0.2f ? ULureBack : ULureFront;
            UpdateLine(lpx);
            UpdateEyeshine(ph);
            UpdateSnow(dt, lpx);
            UpdateParts(dt);
            UpdateRings(dt);
            if (uwShakeT > 0f)
            {
                uwShakeT -= dt;
                float a = uwShakeAmp * Mathf.Clamp01(uwShakeT * 8f);
                uwShake = new Vector2(Mathf.Round(Random.Range(-a, a)), Mathf.Round(Random.Range(-a, a)));
            }
            else uwShake = shakeT >= 0f && shakeT < 0.8f ? new Vector2(Mathf.Round(Mathf.Sin(shakeT * 60f)), 0f) : Vector2.zero;
        }

        const int ColumnStrips = 16;
        readonly Vector3[] colVerts = new Vector3[(ColumnStrips + 1) * 2];
        readonly Vector2[] colUvs = new Vector2[(ColumnStrips + 1) * 2];
        bool colBuilt;

        /// <summary>
        /// The hole's light column, through the fish camera: from the hole (set (0, surfaceY, 0)) along the light
        /// (-keyDir; straight down under the ice) to the floor, clipped at the camera's near plane. A vertical column
        /// only looks vertical while the camera looks level (the rest view); pitched (the lunge's close-up looking down
        /// at the bite) it leans towards the vertical vanishing point, so the art is sheared along the projected line:
        /// every row keeps its texels 1:1 across, shifted by whole pixels (the ends are whole pixels), and the rows are
        /// spread along it by the 3D height (perspective-correct in 16 strips: the far end packs tighter).
        /// </summary>
        void PlaceColumn()
        {
            var hole = new Vector3(0f, set.surfaceY, 0f);
            var down = -set.keyDir.normalized;
            if (down.y > -0.2f) down = Vector3.down;
            var span = down * ((set.surfaceY - set.floorY) / -down.y);
            const float near = 0.1f;
            float dA = Proj(hole).z, dB = Proj(hole + span).z;
            if (dA < near && dB < near)
            {
                rayCol.enabled = false;
                return;
            }
            float tA = 0f, tB = 1f;
            if (dA < near) tA = (near - dA) / (dB - dA);
            else if (dB < near) tB = (near - dA) / (dB - dA);
            Vector3 pA = Proj(hole + span * tA), pB = Proj(hole + span * tB);
            // the foot's end on a whole pixel (as before: where it shows), the hole's end a whole number of pixels off it
            float xB = Mathf.Round(pB.x), yB = Mathf.Round(pB.y);
            float xA = xB + Mathf.Round(pA.x - pB.x), yA = Mathf.Round(pA.y);
            if (Mathf.Abs(yA - yB) < 2f || Mathf.Abs(pA.y - pB.y) < 0.01f)
            {
                rayCol.enabled = false;
                return;
            }
            rayColTop = new Vector2(xA, yA);
            rayColBot = new Vector2(xB, yB);
            float half = rayColW * 0.5f, odd = rayColW % 2 == 1 ? 0.5f : 0f;
            for (int i = 0; i <= ColumnStrips; i++)
            {
                float t = Mathf.Lerp(tA, tB, i / (float)ColumnStrips);
                // this strip edge's projected height, mapped onto the snapped ends; x on the line between them
                float k = i == 0 ? 0f : i == ColumnStrips ? 1f : (Proj(hole + span * t).y - pA.y) / (pB.y - pA.y);
                float y = Mathf.Lerp(yA, yB, k), x = Mathf.Lerp(xA, xB, k) + odd;
                float v = rayColUv.yMax - rayColUv.height * t;
                colVerts[i * 2] = new Vector3((x - half - w * 0.5f) / PPU, (y - h * 0.5f) / PPU, 0f);
                colVerts[i * 2 + 1] = new Vector3((x + half - w * 0.5f) / PPU, (y - h * 0.5f) / PPU, 0f);
                colUvs[i * 2] = new Vector2(rayColUv.xMin, v);
                colUvs[i * 2 + 1] = new Vector2(rayColUv.xMax, v);
            }
            rayColMesh.vertices = colVerts;
            rayColMesh.uv = colUvs;
            if (!colBuilt)
            {
                colBuilt = true;
                var cols = new Color32[colVerts.Length];
                for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
                rayColMesh.colors32 = cols;
                var tris = new int[ColumnStrips * 6];
                for (int i = 0; i < ColumnStrips; i++)
                {
                    // (the fish quad's winding: bottom-left, top-left, bottom-right; top-left, top-right, bottom-right)
                    int a = i * 2 + 2, b = i * 2;   // a: the lower edge (towards the floor), b: the upper
                    tris[i * 6] = a;
                    tris[i * 6 + 1] = b;
                    tris[i * 6 + 2] = a + 1;
                    tris[i * 6 + 3] = b;
                    tris[i * 6 + 4] = b + 1;
                    tris[i * 6 + 5] = a + 1;
                }
                rayColMesh.triangles = tris;
            }
            rayColMesh.RecalculateBounds();
            rayCol.transform.SetPositionAndRotation(UwOrigin, Quaternion.identity);
            rayCol.enabled = true;
        }

        /// <summary>For the autopilot: the light column's ends (render-target pixels; the hole's end first), false without one.</summary>
        public bool ColumnEnds(out Vector2 top, out Vector2 bottom)
        {
            top = rayColTop;
            bottom = rayColBot;
            return rayCol != null && rayCol.enabled;
        }

        /// <summary>Puts a sprite's centre at a render-target pixel (odd-sized sprites on pixel centres).</summary>
        void Place(SpriteRenderer sr, Vector2 px, bool topScene = false)
        {
            var s = sr.sprite;
            if (s == null) return;
            float sx = s.rect.width * Mathf.Abs(sr.transform.localScale.x), sy = s.rect.height * Mathf.Abs(sr.transform.localScale.y);
            float x = Mathf.Round(px.x) + ((int)sx % 2 == 1 ? 0.5f : 0f), y = Mathf.Round(px.y) + ((int)sy % 2 == 1 ? 0.5f : 0f);
            sr.transform.position = topScene ? UT(new Vector2(x, y)) : U(new Vector2(x, y));
        }

        void UpdateLine(Vector2 lpx)
        {
            // (the ice: straight up to the hole over the worm's rest point)
            var top = set.rayAnchored ? new Vector3(0f, set.surfaceY, 0f) : (lureInMouth ? Mouth() : lure) + LineUp;
            var up = Proj(top);
            var a = new Vector2(up.x, up.y);
            // taut while winding, rising or on a fish; a little sag (3 px) when the lure rests
            bool taut = ctl.LureIn.Winding || hopLeft > 0f || lureInMouth;
            float sag = taut ? 0f : 3f;
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                var p = Vector2.Lerp(a, lpx, t) - new Vector2(0f, sag * 4f * t * (1f - t));
                line.SetPosition(i, U(new Vector2(Mathf.Round(p.x) + 0.5f, Mathf.Round(p.y) + 0.5f)));
            }
            line.enabled = enc.Ph != LegendEncounter.Phase.TurnAway || enc.PhaseT < 0.9f;
        }

        void UpdateEyeshine(LegendEncounter.Phase ph)
        {
            bool on = fish != null && eyesShown && ph != LegendEncounter.Phase.Omen && ph != LegendEncounter.Phase.Open;
            eyeSrL.enabled = eyeSrR.enabled = on;
            if (eyeCoreL != null) eyeCoreL.enabled = eyeCoreR.enabled = on;
            if (!on) return;
            var el = Proj(fish.EyeL - SetOrigin);
            var er = Proj(fish.EyeR - SetOrigin);
            if (el.z <= 0.1f || er.z <= 0.1f)
            {
                eyeSrL.enabled = eyeSrR.enabled = false;
                if (eyeCoreL != null) eyeCoreL.enabled = eyeCoreR.enabled = false;
                return;
            }
            var a = new Vector2(el.x, el.y);
            var b = new Vector2(er.x, er.y);
            // two eyes must read as two: at least 6 px apart along the eyes' axis on screen
            float d = (b - a).magnitude;
            if (d < 6f)
            {
                var axis = d > 0.5f ? (b - a) / d : Vector2.right;
                var c = (a + b) * 0.5f;
                a = c - axis * 3f;
                b = c + axis * 3f;
            }
            var cam = SetOrigin + camPos;
            float Face(Vector3 eye, Vector3 fwd)
            {
                float k = Mathf.InverseLerp(-0.3f, 0.3f, Vector3.Dot(fwd, (cam - eye).normalized));
                return k * k * (3f - 2f * k);
            }
            float mult = 1f;
            if (ph == LegendEncounter.Phase.Tease && enc.Mood == 0 || ph == LegendEncounter.Phase.Approach) mult = 0.6f;
            if (dimEyeT < 0.3f) mult *= 0.5f;
            if (ph == LegendEncounter.Phase.TurnAway) mult *= 1f - Mathf.Clamp01((enc.PhaseT - 0.7f) / 0.5f);
            if (ph == LegendEncounter.Phase.Close) mult = 0f;
            // in the dark (the eyes beat) they face us: full; later by where each eye looks (the great white's roll back
            // turns them away)
            float fl = ph == LegendEncounter.Phase.Eyes ? 1f : Face(fish.EyeL, fish.EyeFwdL);
            float fr = ph == LegendEncounter.Phase.Eyes ? 1f : Face(fish.EyeR, fish.EyeFwdR);
            int s = enc.InTell ? 2 : 1;
            eyeSrL.transform.localScale = eyeSrR.transform.localScale = new Vector3(s, s, 1f);
            if (eyeCoreL != null)
            {
                // two-tone: the halo in the glow colour, the core in the core colour
                eyeSrL.color = new Color(eyeGlow.r, eyeGlow.g, eyeGlow.b, fl * mult);
                eyeSrR.color = new Color(eyeGlow.r, eyeGlow.g, eyeGlow.b, fr * mult);
                eyeCoreL.transform.localScale = eyeCoreR.transform.localScale = new Vector3(s, s, 1f);
                eyeCoreL.color = new Color(eyeCore.r, eyeCore.g, eyeCore.b, fl * mult);
                eyeCoreR.color = new Color(eyeCore.r, eyeCore.g, eyeCore.b, fr * mult);
                Place(eyeCoreL, a);
                Place(eyeCoreR, b);
            }
            else
            {
                eyeSrL.color = new Color(eyeCore.r, eyeCore.g, eyeCore.b, fl * mult);
                eyeSrR.color = new Color(eyeCore.r, eyeCore.g, eyeCore.b, fr * mult);
            }
            Place(eyeSrL, a);
            Place(eyeSrR, b);
        }

        void UpdateSnow(float dt, Vector2 lurePx)
        {
            bool suck = enc.Ph == LegendEncounter.Phase.Lunge;
            var mouth = new Vector2(Proj(Mouth()).x, Proj(Mouth()).y);
            foreach (var s in snowFar)
            {
                s.px.y -= s.speed * 0.6f * dt;
                if (s.px.y < -4f) s.px = new Vector2(Random.Range(0f, 640f), h + 4f);
                Place(s.sr, new Vector2(Mathf.Repeat(s.px.x - 0.5f * yawPx, 640f) - (640f - w) * 0.5f, s.px.y));
            }
            foreach (var s in snowNear)
            {
                if (suck)
                {
                    var screen = new Vector2(Mathf.Repeat(s.px.x, 640f) - (640f - w) * 0.5f, s.px.y);
                    var toM = mouth - screen;
                    if (toM.magnitude < 60f) s.px += toM.normalized * 90f * dt;
                }
                else
                {
                    s.px.y -= s.speed * dt;
                    s.px.x += Mathf.Sin(time * 0.8f + s.phase) * 2f * dt;
                }
                if (s.px.y < -4f) s.px = new Vector2(Random.Range(0f, 640f), h + 4f);
                s.sr.color = Art.Hex(set.snowNear, s.alpha * pSnow);
                Place(s.sr, new Vector2(Mathf.Repeat(s.px.x, 640f) - (640f - w) * 0.5f, s.px.y));
            }
        }

        // ------------------------------------------------------------------ bubbles, silt, crumbs, rings
        void Emit(Sprite s, Vector3 at, int n, float rise, int order = UFx)
        {
            if (s == null) return;
            for (int i = 0; i < n; i++)
            {
                var p = Spawn(new[] { s }, new Vector2(at.x + Random.Range(-3f, 3f), at.y + Random.Range(-1f, 3f)), Color.white, Random.Range(0.8f, 1.5f));
                p.sr.sortingOrder = order;
                p.vel = new Vector2(Random.Range(-2f, 2f), rise * Random.Range(0.7f, 1.2f));
                p.wobble = Random.Range(0f, 6f);
            }
        }

        void EmitSilt(Vector3 at, int n)
        {
            if (set.silt == null)
            {
                // open water: no bottom to stir, bubbles instead
                Emit(bubbleS, at, n + 1, 12f);
                return;
            }
            for (int i = 0; i < n; i++)
            {
                var p = Spawn(silt, new Vector2(at.x + Random.Range(-4f, 4f), at.y + Random.Range(-1f, 2f)), Art.Hex(set.silt, 0.8f * pSnow), 0.4f + Random.Range(0f, 0.2f));
                p.vel = new Vector2(Random.Range(-6f, 6f), Random.Range(1f, 4f));
            }
        }

        /// <summary>A puff of golden crumbs off the golden bait (1 px #ffd24a particles).</summary>
        void CrumbPuff(Vector3 at, int n)
        {
            if (!GoldenBait) return;
            for (int i = 0; i < n; i++)
            {
                var p = Spawn(new[] { Art.Pixel }, new Vector2(at.x + Random.Range(-2f, 2f), at.y + Random.Range(-1f, 2f)), Art.Hex("#ffd24a", 0.95f), Random.Range(0.5f, 0.9f));
                p.vel = new Vector2(Random.Range(-7f, 7f), Random.Range(2f, 8f));
                p.wobble = Random.Range(0f, 6f);
            }
        }

        /// <summary>A pop ring at the surface line over a set point (a growing 1 px ellipse, flattened like the surface seen from below).</summary>
        void PopRing(Vector3 at, float maxR)
        {
            SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : PixelSprite("Ring", Ellipse(2, 1, false), UFx, uwRoot, UwLayer);
            sr.enabled = true;
            sr.sortingOrder = UFx;
            rings.Add(new Ring { sr = sr, at = at, t = 0f, life = 0.6f, maxR = maxR, col = Art.Hex(set.snowNear, 0.8f) });
        }

        /// <summary>A boil at the surface line: 20 bubbles, a ring and two frames of a white ellipse flash.</summary>
        void Boil(Vector3 at)
        {
            var px = Proj(at);
            for (int i = 0; i < 20; i++)
            {
                var p = Spawn(new[] { i % 3 == 0 ? bubbleM : bubbleS }, new Vector2(px.x + Random.Range(-14f, 14f), px.y - Random.Range(0f, 14f)), Color.white, Random.Range(0.5f, 1.1f));
                p.vel = new Vector2(Random.Range(-6f, 6f), Random.Range(10f, 30f));
                p.wobble = Random.Range(0f, 6f);
            }
            PopRing(at, 26f);
            SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : PixelSprite("Flash", Ellipse(14, 3, true), UFx, uwRoot, UwLayer);
            sr.enabled = true;
            sr.sortingOrder = UFx;
            rings.Add(new Ring { sr = sr, at = at, t = 0f, life = 0.034f, maxR = -14f, col = new Color(1f, 1f, 1f, 0.85f) });
        }

        void UpdateRings(float dt)
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.t += dt;
                if (r.t >= r.life)
                {
                    r.sr.enabled = false;
                    pool.Push(r.sr);
                    rings.RemoveAt(i);
                    continue;
                }
                float k = r.t / r.life;
                if (r.maxR < 0f)
                    r.sr.sprite = Ellipse(Mathf.RoundToInt(-r.maxR), Mathf.Max(1, Mathf.RoundToInt(-r.maxR / 4f)), true);  // the flash
                else
                {
                    int rx = Mathf.Max(2, Mathf.RoundToInt(2f + (r.maxR - 2f) * (1f - (1f - k) * (1f - k))));
                    r.sr.sprite = Ellipse(rx, Mathf.Max(1, Mathf.RoundToInt(rx / 4f)), false);
                }
                var c = r.col;
                c.a *= r.maxR < 0f ? 1f : 1f - k;
                r.sr.color = c;
                r.sr.transform.localScale = Vector3.one;
                r.sr.transform.rotation = Quaternion.identity;
                var p = Proj(r.at);
                Place(r.sr, new Vector2(p.x, p.y));
            }
        }

        Part Spawn(Sprite[] frames, Vector2 px, Color c, float life)
        {
            SpriteRenderer sr;
            if (pool.Count > 0)
            {
                sr = pool.Pop();
                sr.enabled = true;
            }
            else sr = PixelSprite("Fx", frames[0], UFx, uwRoot, UwLayer);
            sr.sprite = frames[0];
            sr.color = c;
            sr.sortingOrder = UFx;
            var p = new Part { sr = sr, px = px, life = life, max = life, frames = frames, col = c };
            parts.Add(p);
            return p;
        }

        void UpdateParts(float dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                p.life -= dt;
                if (p.life <= 0f)
                {
                    p.sr.enabled = false;
                    pool.Push(p.sr);
                    parts.RemoveAt(i);
                    continue;
                }
                float k = 1f - p.life / p.max;
                p.px += (p.vel + new Vector2(Mathf.Sin(time * 5f + p.wobble) * 3f, 0f)) * dt;
                p.sr.sprite = p.frames[Mathf.Min(p.frames.Length - 1, (int)(k * p.frames.Length))];
                var c = p.col;
                c.a *= k > 0.7f ? 1f - (k - 0.7f) / 0.3f : 1f;
                p.sr.color = c;
                Place(p.sr, p.px);
            }
        }

        // ------------------------------------------------------------------ the window in the pixel view
        /// <summary>
        /// The lure's point on the render target now: the camera's pan over a stage's overscan moves it (an encounter that
        /// began with the view panned out to the rig sees the view ease home under it), the shake does not.
        /// </summary>
        Vector2 LureRT() => (lureWorld - pv.BaseCenter) * PPU + new Vector2(w * 0.5f, h * 0.5f) - (Vector2)pv.Pan;

        void UpdateCrop(float dt, LegendEncounter.Phase ph)
        {
            float t = enc.PhaseT;
            float dimA = 0.55f;
            bool show = true, frameOn = true;
            float frameA = 1f;
            lureSurf = LureRT();
            Rect lureRect = new Rect(lureSurf.x - 4f, lureSurf.y - 4f, 8f, 8f);
            // the grow to full screen takes GrowT from the lunge's start, whatever the lunge's length (from the top view: a cut)
            float grow = ph == LegendEncounter.Phase.Lunge ? t : ph == LegendEncounter.Phase.HookWindow ? def.lungeT + t : 99f;
            if (topFlow && topState == TopState.Cut) grow = Mathf.Max(grow, GrowT);
            switch (ph)
            {
                case LegendEncounter.Phase.Omen:
                    show = false;
                    dimA = 0.55f * Mathf.Clamp01(t / 0.6f);
                    break;
                case LegendEncounter.Phase.Open:
                {
                    float k = Mathf.Clamp01(t / enc.PhaseLen);
                    crop = Lerp(lureRect, window, 1f - (1f - k) * (1f - k) * (1f - k));
                    break;
                }
                case LegendEncounter.Phase.Lunge:
                case LegendEncounter.Phase.HookWindow when grow < GrowT:
                {
                    float k = Mathf.Clamp01(grow / GrowT);
                    k = 1f - (1f - k) * (1f - k);
                    crop = Lerp(window, new Rect(0, 0, w, h), k);
                    frameA = 1f - k;
                    break;
                }
                case LegendEncounter.Phase.HookWindow:
                case LegendEncounter.Phase.Hooked:
                    crop = new Rect(0, 0, w, h);
                    frameOn = false;
                    break;
                case LegendEncounter.Phase.Surface:
                {
                    // the waterline wipes up from the bottom and the surface fight is revealed under it (undimmed)
                    float k = Mathf.Clamp01(t / enc.PhaseLen);
                    float yb = Mathf.Round(h * Mathf.SmoothStep(0f, 1f, k));
                    crop = new Rect(0, yb, w, h - yb);
                    frameOn = false;
                    dimA = 0f;
                    break;
                }
                case LegendEncounter.Phase.TurnAway:
                    crop = window;
                    closeFrom = window;
                    break;
                case LegendEncounter.Phase.Close:
                {
                    float k = Mathf.Clamp01(t / enc.PhaseLen);
                    crop = Lerp(closeFrom.width > 0 ? closeFrom : window, lureRect, k * k);
                    dimA = 0.55f * (1f - k);
                    break;
                }
                case LegendEncounter.Phase.Done:
                    show = false;
                    dimA = 0f;
                    break;
                default:
                    crop = window;
                    break;
            }
            crop = new Rect(Mathf.Round(crop.x), Mathf.Round(crop.y), Mathf.Max(1f, Mathf.Round(crop.width)), Mathf.Max(0f, Mathf.Round(crop.height)));
            if (crop.height < 1f) show = false;
            var cam = CamPos2D;
            // the dim over the surface scene (under the line, the rod and the angler)
            dim.transform.position = new Vector3(cam.x, cam.y, 0.5f);
            dim.transform.localScale = new Vector3(w + 2, h + 2, 1f);
            dim.color = new Color(0f, 0f, 0f, dimA);
            dim.enabled = dimA > 0.001f;
            // the window shows the underwater view below the split and the top view above it (rising through the
            // waterline: the split slides down the window; on top: the whole window)
            splitPx = crop.yMax;
            if (ShowsTop) splitPx = topState == TopState.Rise ? Mathf.Clamp(window.yMax - uwShiftPx, crop.yMin, crop.yMax) : crop.yMin;
            cropQuad.enabled = show && splitPx > crop.yMin;
            if (cropQuad.enabled) CropMesh(cropQuad, cropMesh, Rect.MinMaxRect(crop.xMin, crop.yMin, crop.xMax, splitPx), cam);
            if (cropTopQuad != null)
            {
                cropTopQuad.enabled = show && ShowsTop && splitPx < crop.yMax;
                if (cropTopQuad.enabled) CropMesh(cropTopQuad, cropTopMesh, Rect.MinMaxRect(crop.xMin, splitPx, crop.xMax, crop.yMax), cam);
            }
            // the frame 3 px outside the crop (its inner line lands on the crop's edge)
            frame.enabled = show && frameOn && frameA > 0.02f;
            if (frame.enabled)
            {
                frame.size = new Vector2((crop.width + 6f) / PPU, (crop.height + 6f) / PPU);
                frame.transform.position = RTToWorld(crop.center);
                var fc = Color.white;
                if (frameGoldT >= 0f && frameGoldT < 0.35f) fc = Color.Lerp(UIKit.Gold, Color.white, frameGoldT / 0.35f);
                fc.a = frameA;
                frame.color = fc;
            }
            if (frameGoldT >= 0f) frameGoldT += dt;
            // the time limit: the top line shrinks from both ends through the tease
            timer.enabled = show && ph == LegendEncounter.Phase.Tease;
            if (timer.enabled)
            {
                float left = Mathf.Clamp01(1f - enc.TeaseT / def.teaseLimit);
                float tw = Mathf.Round(crop.width * left);
                if ((int)tw % 2 != (int)crop.width % 2) tw = Mathf.Max(0f, tw - 1f);
                timer.transform.localScale = new Vector3(Mathf.Max(0f, tw), 1f, 1f);
                timer.transform.position = RTToWorld(new Vector2(crop.center.x, crop.yMax - 0.5f));
                timer.enabled = tw > 0f;
            }
            // the bite's flash: two frames of white
            flashSr.enabled = flashT >= 0f && flashT < 0.04f;
            if (flashT >= 0f) flashT += dt;
            if (flashSr.enabled)
            {
                flashSr.transform.position = new Vector3(cam.x, cam.y, 0f);
                flashSr.transform.localScale = new Vector3(w + 2, h + 2, 1f);
                flashSr.color = new Color(1f, 1f, 1f, 0.6f);
            }
            // the waterline riding the wipe (and the split while rising through it)
            waterline.enabled = show && ph == LegendEncounter.Phase.Surface && crop.yMin > 0f;
            if (waterline.enabled)
            {
                waterline.size = new Vector2((w + 32f) / PPU, 4f / PPU);
                waterline.transform.position = RTToWorld(new Vector2(w * 0.5f, crop.yMin + 2f));
            }
            else if (show && topFlow && topState == TopState.Rise && splitPx > crop.yMin && splitPx < crop.yMax)
            {
                waterline.enabled = true;
                waterline.size = new Vector2(crop.width / PPU, 4f / PPU);
                waterline.transform.position = RTToWorld(new Vector2(crop.center.x, splitPx));
            }
        }

        /// <summary>A window quad in the pixel view: <paramref name="r"/> (render-target pixels) of its target, 1:1.</summary>
        void CropMesh(MeshRenderer quad, Mesh mesh, Rect r, Vector2 cam)
        {
            quad.transform.position = new Vector3(cam.x, cam.y, 0f);
            float x0 = (r.xMin - w * 0.5f) / PPU, x1 = (r.xMax - w * 0.5f) / PPU, y0 = (r.yMin - h * 0.5f) / PPU, y1 = (r.yMax - h * 0.5f) / PPU;
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(x0, y0, 0), new Vector3(x1, y0, 0), new Vector3(x0, y1, 0), new Vector3(x1, y1, 0) };
            mesh.uv = new[]
            {
                new Vector2(r.xMin / w, r.yMin / h), new Vector2(r.xMax / w, r.yMin / h),
                new Vector2(r.xMin / w, r.yMax / h), new Vector2(r.xMax / w, r.yMax / h),
            };
            mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f), new Vector3(x1 - x0, y1 - y0, 1f));
        }

        static Rect Lerp(Rect a, Rect b, float k) =>
            Rect.MinMaxRect(Mathf.Lerp(a.xMin, b.xMin, k), Mathf.Lerp(a.yMin, b.yMin, k), Mathf.Lerp(a.xMax, b.xMax, k), Mathf.Lerp(a.yMax, b.yMax, k));

        void Heartbeat(float dt, LegendEncounter.Phase ph)
        {
            float hz = ph == LegendEncounter.Phase.Eyes ? 0.5f : ph == LegendEncounter.Phase.Approach ? 0.8f : 0f;
            if (hz <= 0f)
            {
                heartT = 0f;
                return;
            }
            heartT -= dt;
            if (heartT <= 0f)
            {
                heartT = 1f / hz;
                Sfx.Play(Sfx.Heartbeat, 0.7f);
            }
        }
    }
}
