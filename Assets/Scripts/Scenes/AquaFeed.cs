using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The aquarium's feed bags (Sprites/World/feed_*, Tools/Blender/fk_items.py aquafeed): one per feed type stands on
    /// the cabinet ledge in front of the tank. A new bag is sealed: swiping along its dotted perforation line (either
    /// way, over most of its width) tears the top off and opens it; a tap shows "점선을 따라 잘라요". An open bag is
    /// dragged up over the tank: above the water it tips into the pouring pose and sprinkles pellets from its mouth
    /// (faster the more it moves / shakes, a trickle when held still); letting go glides it back. Pellets splash on the
    /// surface, sink with a drift, the fish chase the nearest one and eat it (AquaCare.Eat), leftovers dissolve on the
    /// bottom. Pouring stops using feed once the food in the water covers everyone's hunger, and when all are full
    /// "배불러요!". An emptied bag folds away and the next spare appears sealed; with no bags at all a faint outline
    /// says "상점에서 사료를 사요" (tap: the shop's 사료 section). Only the fish whose diet has pellets go for them; the
    /// shrimp tub and the sardine cooler beside the bags are in AquaFeed.Live.cs.
    /// </summary>
    public partial class AquaFeed : MonoBehaviour
    {
        public static AquaFeed Current { get; private set; }

        // ---- anchors, sprite pixels from the centre, +y up (see the art notes in fk_items.py aquafeed)
        const float Ppu = 16f;
        // the ledge and the water of the tank in use (AquaLayout; the 중형 수조's values in the comments)
        static AquaLayout L => AquaLayout.Active;
        static float LedgeCentreY => L.bagCentreY;             // a standing 32x36 bag's centre (its base on the ledge, y -5.25)
        static float[] SlotX => L.bagX;                        // -10, -7.75
        const float PerfY = 11.5f, PerfHalf = 10f, PerfBand = 3.5f; // the perforation row, x -10..+10, +-3 px hit band
        const int PerfCells = 20, PerfNeed = 15;              // a stroke must cover 15 of its 20 one-pixel cells
        static readonly Vector2 GripPx = new Vector2(0f, -2f);           // the tilt / pour sprites' centre
        static readonly Vector2 PourMouthPx = new Vector2(13f, -7.5f);
        static readonly Vector2 PourDir = new Vector2(0.866f, -0.5f);
        static float SurfaceFx => L.surfaceFx;                 // splash centre (the pixel row of surfaceY 5.2: 5.1875)
        static float BottomY => L.BottomY;                     // where pellets come to rest on the gravel (-3.5)
        static float TankMinX => L.tankMinX;                   // pellets stay where the fish can reach (-12.9 .. 12.9)
        static float TankMaxX => L.tankMaxX;
        const int MaxPellets = 140;

        enum BagState { None, Sealed, Open, Folding }
        enum Pose { Carry, Tilt, Pour }

        class Slot
        {
            public FeedDef feed;
            public int index;
            public Vector2 home;
            public SpriteRenderer sr, shadow, scissors;
            public BagState state;
            public float anim;          // folding / pop-in time
            public bool popping;
            public float hintPhase;
            public bool hintOnce;       // after the first tear: one snip of the scissors hint is due
            public Image badge;
            public Text badgeText;
            public SpriteRenderer[] cut;
            public Sprite sealedS, tiltS, pourS;
            public Sprite[] openS, foldS, stripS, tearS;
        }

        /// <summary>A pellet (basic) or flake (premium) in the tank.</summary>
        public class Pellet
        {
            public SpriteRenderer sr, glint;
            public Vector2 pos, vel;
            public int state;           // 0 falling through the air, 1 sinking, 2 resting on the bottom
            public float t, phase, sink, rest, glintT;
            public FeedDef feed;
            public bool dead;
            public Vector2 Pos => pos;
        }

        PixelView pv;
        AquariumData box;
        List<TankFish> fish;
        RectTransform ui;
        Transform root;
        readonly List<Slot> slots = new List<Slot>();
        readonly List<Pellet> pellets = new List<Pellet>();
        SpriteRenderer outline;
        Text emptyLabel;
        Image emptyPlate;
        Text hint;
        Image hintPlate;
        float hintTime;
        Vector2 hintAt;

        // drag
        Slot drag;
        Vector2 grip, gripOffset, vel, lastPointer;
        Pose pose;
        float poseT, emitAcc;
        bool flip, wantPour;
        Slot returning;
        Vector2 returnFrom;
        float returnT;
        // swipe
        Slot swipe;
        readonly bool[] cover = new bool[20];
        Vector2 swipeLast, pressAt;
        bool swipeHasLast, moved;
        float swipeDir;
        // fullness: per food, everyone who eats it was full last frame
        readonly bool[] groupWasFull = new bool[3];
        float soundT, plinkT;
        Sprite[] pelletS, flakeS, glintS, splashS, crumbS, dissolveS;
        Sprite shadowS, scissorsA, scissorsB, fullIcon, hungryIcon;

        // ---- read by the test autopilot
        public int Emitted { get; private set; }
        public int Eaten { get; private set; }
        public float LastEatTime { get; private set; } = -99f;
        public Vector2 LastEatAt { get; private set; }
        public int Celebrations { get; private set; }
        public int Tears { get; private set; }
        /// <summary>The food eaten last (the 배불러요! of its eaters).</summary>
        public Diet LastEatKind { get; private set; } = Diet.Pellet;
        /// <summary>Fish that just got full ("배불러요!" over each).</summary>
        public int FullPops { get; private set; }
        public bool Dragging => drag != null;
        public bool Pouring => drag != null && pose == Pose.Pour;
        public int PelletsAlive => pellets.Count(p => !p.dead);
        /// <summary>A bag is being dragged or cut, or a shrimp / sardine held: fish taps wait.</summary>
        public bool Busy => drag != null || swipe != null || held != null;
        /// <summary>
        /// Food in the water, a bag being poured, a piece held, or something eaten a moment ago (a gulp's splash at the
        /// surface): the hungry fish are busy (no hungry bubbles), the top hint stays out of the way.
        /// </summary>
        public bool FeedingNow => drag != null || pellets.Count > 0 || held != null || pieces.Count > 0 || Time.time - LastEatTime < 1.2f;

        public bool ScissorsShown(string id) => slots.Any(s => s.feed.id == id && s.scissors.enabled);
        public string HintShown => hintTime > 0f && hint != null ? hint.text : "";
        public Vector2 BagCentre(string id) => slots.FirstOrDefault(s => s.feed.id == id)?.home ?? Vector2.zero;
        public string BagStateOf(string id) => slots.FirstOrDefault(s => s.feed.id == id)?.state.ToString() ?? "None";
        /// <summary>The perforation line's ends in world space (for the autopilot's swipe).</summary>
        public (Vector2 a, Vector2 b) Perforation(string id)
        {
            var c = BagCentre(id);
            return (c + new Vector2(-PerfHalf, PerfY) / Ppu, c + new Vector2(PerfHalf, PerfY) / Ppu);
        }

        public void Init(PixelView view, AquariumData data, List<TankFish> tankFish, RectTransform canvas)
        {
            Current = this;
            pv = view;
            box = data;
            fish = tankFish;
            ui = canvas;
            root = transform;
            pelletS = Frames("feed_pellet_", 3);
            flakeS = Frames("feed_flake_", 3);
            glintS = Frames("feed_flake_glint_f", 2);
            splashS = Frames("feed_splash_f", 4);
            crumbS = Frames("feed_crumb_f", 3);
            dissolveS = Frames("feed_dissolve_f", 4);
            shadowS = Art.Get("World/feed_shadow");
            scissorsA = Art.Get("World/feed_scissors_f0");
            scissorsB = Art.Get("World/feed_scissors_f1");
            fullIcon = Art.UI("icon_full");
            hungryIcon = Art.UI("icon_hungry");
            for (int i = 0; i < AquaCare.Feeds.Count && i < SlotX.Length; i++) slots.Add(MakeSlot(AquaCare.Feeds[i], i));

            outline = NewSprite("FeedOutline", MakeOutline(), 44);
            outline.transform.position = Snap(new Vector2(SlotX[0], LedgeCentreY));
            outline.color = new Color(1f, 0.96f, 0.86f, 0.55f);
            emptyLabel = UIKit.PlateLabel(ui, "상점에서 사료를 사요", 16, UIKit.Cream, out emptyPlate);
            emptyPlate.rectTransform.anchorMin = emptyPlate.rectTransform.anchorMax = Vector2.zero;
            emptyPlate.rectTransform.pivot = new Vector2(0f, 0.5f);
            hint = UIKit.PlateLabel(ui, "", 16, UIKit.Cream, out hintPlate);
            hintPlate.rectTransform.anchorMin = hintPlate.rectTransform.anchorMax = Vector2.zero;
            hintPlate.rectTransform.pivot = new Vector2(0.5f, 0f);
            UIKit.FitPlate(hint, hintPlate);
            foreach (var s in slots) SyncSlot(s, true);
            InitLive();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            Suspended = false;
        }

        static Sprite[] Frames(string prefix, int n)
        {
            var a = new Sprite[n];
            for (int i = 0; i < n; i++) a[i] = Art.Get("World/" + prefix + i);
            return a;
        }

        SpriteRenderer NewSprite(string name, Sprite s, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            sr.sprite = s;
            sr.sortingOrder = order;
            return sr;
        }

        Slot MakeSlot(FeedDef f, int i)
        {
            string k = "World/feed_" + f.key;
            var s = new Slot
            {
                feed = f, index = i, home = new Vector2(SlotX[i], LedgeCentreY), hintPhase = i * 1.6f,
                sealedS = Art.Get(k + "_sealed"), tiltS = Art.Get(k + "_tilt"), pourS = Art.Get(k + "_pour"),
                openS = new[] { Art.Get(k + "_open1"), Art.Get(k + "_open2"), Art.Get(k + "_open3"), Art.Get(k + "_open4") },
                foldS = new[] { Art.Get(k + "_fold_f0"), Art.Get(k + "_fold_f1") },
                stripS = new[] { Art.Get(k + "_strip_f0"), Art.Get(k + "_strip_f1"), Art.Get(k + "_strip_f2") },
                tearS = new[] { Art.Get(k + "_tear_f0"), Art.Get(k + "_tear_f1"), Art.Get(k + "_tear_f2"), Art.Get(k + "_tear_f3") },
            };
            s.shadow = NewSprite("FeedShadow_" + f.key, shadowS, 44);
            // the shadow's 4 px strip sits on the ledge under the bag's base (y -5.25)
            s.shadow.transform.position = Snap(new Vector2(s.home.x, L.ledgeY + 1f / Ppu));
            s.sr = NewSprite("FeedBag_" + f.key, s.sealedS, 45 + i);
            s.sr.transform.position = Snap(s.home);
            s.scissors = NewSprite("FeedScissors_" + f.key, scissorsA, 49);
            s.scissors.enabled = false;
            // spare bags: a small "+N" tag over the bag
            s.badge = UIKit.Panel(ui, "badge", null, "FeedBadge_" + f.key);
            s.badge.raycastTarget = false;
            s.badge.rectTransform.anchorMin = s.badge.rectTransform.anchorMax = Vector2.zero;
            s.badge.rectTransform.pivot = new Vector2(0.5f, 0f);
            s.badge.rectTransform.sizeDelta = new Vector2(38, 26);
            s.badgeText = UIKit.Label(s.badge.transform, "", 16, UIKit.Ink, TextAnchor.MiddleCenter, false);
            s.badgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            s.badgeText.rectTransform.Fill(0, 0, 2, 4);
            // the cut so far along the perforation (one dark pixel per cell the stroke has covered)
            s.cut = new SpriteRenderer[PerfCells];
            for (int c = 0; c < PerfCells; c++)
            {
                var px = NewSprite("Cut", Art.Pixel, 47);
                px.color = new Color32(0x2a, 0x1e, 0x14, 0xe0);
                px.transform.position = s.home + new Vector2(-PerfHalf + 0.5f + c, PerfY) / Ppu; // (pixel centres: half-pixel offsets)
                px.enabled = false;
                s.cut[c] = px;
            }
            return s;
        }

        /// <summary>A dashed 32x36 outline where a bag would stand (no feed at all).</summary>
        static Sprite MakeOutline()
        {
            const int w = 32, h = 36;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = (x == 4 || x == w - 5) && y >= 2 && y <= h - 4 || (y == 2 || y == h - 4) && x >= 4 && x <= w - 5;
                bool corner = (x == 4 || x == w - 5) && (y == 2 || y == h - 4);
                bool dash = ((x + y) / 2) % 2 == 0;
                t.SetPixel(x, y, edge && !corner && dash ? Color.white : clear);
            }
            // a small "+" in the middle
            for (int i = -3; i <= 3; i++)
            {
                t.SetPixel(w / 2 + i, h / 2, Color.white);
                t.SetPixel(w / 2, h / 2 + i, Color.white);
            }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16);
        }

        static Vector2 Snap(Vector2 v) => new Vector2(Mathf.Round(v.x * Ppu) / Ppu, Mathf.Round(v.y * Ppu) / Ppu);

        static void Place(Component c, Vector2 p) => c.transform.position = Snap(p);

        Vector2 ToCanvas(Vector2 world) => pv.WorldToScreen(world) * UIKit.CanvasPerScreenPx;

        // ------------------------------------------------------------------ state
        BagState Desired(Slot s)
        {
            var st = AquaCare.Stock(s.feed.id);
            return st.portions > 0.001f ? BagState.Open : st.bags > 0 ? BagState.Sealed : BagState.None;
        }

        Sprite OpenSprite(Slot s)
        {
            float frac = AquaCare.Stock(s.feed.id).portions / Mathf.Max(1f, s.feed.portions);
            int i = frac > 0.75f ? 3 : frac > 0.5f ? 2 : frac > 0.25f ? 1 : 0;
            return s.openS[i];
        }

        /// <summary>Brings a resting slot in line with the stock (bought bags pop in).</summary>
        void SyncSlot(Slot s, bool instant = false)
        {
            if (s == drag || s == returning || s.state == BagState.Folding) return;
            var want = Desired(s);
            if (want != s.state)
            {
                if (s.state == BagState.None && want != BagState.None && !instant)
                {
                    s.popping = true;
                    s.anim = 0f;
                }
                s.state = want;
            }
        }

        // ------------------------------------------------------------------ frame
        /// <summary>The feeding ignores the pointer (the decoration mode: AquaDecor).</summary>
        public static bool Suspended;

        void Update()
        {
            float dt = Time.deltaTime;
            bool blocked = Dialog.Open || UIKit.ModalCount > 0 || Suspended;
            Vector2 w = pv.ScreenToWorld(PointerInput.Position);
            if (blocked)
            {
                if (drag != null) Release();
                EndSwipe(false);
                if (held != null && !held.back) PutBack(false);
            }
            else
            {
                // ~8 s without any input: the sealed bags snip their scissors hint once
                if (PointerInput.IsDown || PointerInput.Pressed) { idleT = 0f; idleHinted = false; }
                else if ((idleT += dt) >= IdleHint && !idleHinted)
                {
                    idleHinted = true;
                    foreach (var s in slots) ReplayHint(s);
                }
                if (PointerInput.WorldPressed) Press(w);
                if (PointerInput.IsDown) Hold(w);
                else
                {
                    if (drag != null) Release();
                    if (swipe != null) EndSwipe(true);
                    if (held != null && !held.back) ReleaseHeld();
                }
            }
            foreach (var s in slots)
            {
                SyncSlot(s);
                DrawSlot(s, dt);
            }
            UpdateDrag(dt);
            UpdatePellets(dt);
            UpdateLive(dt);
            UpdateFullness(dt);
            UpdateUI(dt);
            UpdateLiveUI();
        }

        /// <summary>The press lands on a bag, a live food container (or an empty outline / slot): fish taps ignore it.</summary>
        public bool HitsBag(Vector2 w) => HitSlot(w) != null || (outline != null && outline.enabled && HitOutline(w)) || HitsBox(w);

        Slot HitSlot(Vector2 w)
        {
            Slot best = null;
            float bestD = float.MaxValue;
            foreach (var s in slots)
            {
                if (s.state != BagState.Sealed && s.state != BagState.Open) continue;
                if (s == returning) continue;
                var l = (w - s.home) * Ppu;
                // sealed: wide enough that a swipe may start a little off the bag's sides
                float hx = s.state == BagState.Sealed ? 26f : 18f;
                if (Mathf.Abs(l.x) > hx || l.y < -19f || l.y > 20f) continue;
                if (Mathf.Abs(l.x) < bestD)
                {
                    bestD = Mathf.Abs(l.x);
                    best = s;
                }
            }
            return best;
        }

        bool HitOutline(Vector2 w)
        {
            var l = (w - (Vector2)outline.transform.position) * Ppu;
            return Mathf.Abs(l.x) <= 18f && Mathf.Abs(l.y) <= 20f;
        }

        void Press(Vector2 w)
        {
            if (PressLive(w)) return; // (the tub / cooler first: they stand right beside the bags)
            var s = HitSlot(w);
            if (s == null)
            {
                if (outline.enabled && HitOutline(w))
                {
                    Sfx.Play(Sfx.Click);
                    ShopUI.TankFeed = true;
                    ShopUI.Open(ui, ItemKind.Tank);
                }
                return;
            }
            if (s.state == BagState.Open) StartDrag(s, w);
            else
            {
                swipe = s;
                for (int i = 0; i < cover.Length; i++) cover[i] = false;
                swipeHasLast = false;
                moved = false;
                pressAt = w;
                swipeDir = 0f;
                AddStroke(w);
            }
        }

        void Hold(Vector2 w)
        {
            if (drag != null) lastPointer = w;
            if (swipe != null) AddStroke(w);
            if (held != null && !held.back) held.at = w;
        }

        // ------------------------------------------------------------------ cutting the perforation
        void AddStroke(Vector2 w)
        {
            var s = swipe;
            var l = (w - s.home) * Ppu;
            if ((w - pressAt).magnitude > 0.2f) moved = true;
            bool inBand = Mathf.Abs(l.y - PerfY) <= PerfBand;
            if (swipeHasLast)
            {
                bool lastIn = Mathf.Abs(swipeLast.y - PerfY) <= PerfBand;
                if (inBand && lastIn)
                {
                    float a = Mathf.Min(swipeLast.x, l.x), b = Mathf.Max(swipeLast.x, l.x);
                    for (int i = 0; i < cover.Length; i++)
                    {
                        float c0 = -PerfHalf + i, c1 = c0 + 1f;
                        if (b >= c0 && a <= c1) cover[i] = true;
                    }
                }
                if (Mathf.Abs(l.x - swipeLast.x) > 0.5f) swipeDir = Mathf.Sign(l.x - swipeLast.x);
            }
            swipeLast = l;
            swipeHasLast = true;
            // the scissors ride the finger along the line while it is on it
            if (inBand && moved)
            {
                s.scissors.enabled = true;
                s.scissors.flipX = swipeDir < 0f;
                s.scissors.sprite = ((int)(Time.time * 12f)) % 2 == 0 ? scissorsA : scissorsB;
                float x = Mathf.Clamp(l.x, -PerfHalf - 2f, PerfHalf + 2f);
                Place(s.scissors, s.home + new Vector2(x + (swipeDir < 0f ? -8f : 8f), PerfY + 0.5f) / Ppu); // (ahead: the cut shows behind)
            }
            for (int i = 0; i < cover.Length; i++) s.cut[i].enabled = cover[i];
            int n = cover.Count(c => c);
            if (n >= PerfNeed) Tear(s);
        }

        void EndSwipe(bool released)
        {
            if (swipe == null) return;
            var s = swipe;
            swipe = null;
            s.scissors.enabled = false;
            foreach (var px in s.cut) px.enabled = false;
            if (!released || s.state != BagState.Sealed) return;
            if (!moved)
            {
                ShowHint(s.home + new Vector2(0f, 1.45f), "점선을 따라 잘라요");
                Sfx.Play(Sfx.Click, 0.6f);
            }
            ReplayHint(s); // replay the scissors hint (a swipe off the line does nothing else)
            AquaCare.Log($"swipe {s.feed.id}: {(moved ? "off the line" : "tap")}, cover {cover.Count(c => c)}/{cover.Length}");
        }

        void Tear(Slot s)
        {
            swipe = null;
            s.scissors.enabled = false;
            foreach (var px in s.cut) px.enabled = false;
            AquaCare.Open(s.feed);
            s.state = BagState.Open;
            Tears++;
            if (Game.Data != null && !Game.Data.feedTornOnce)
            {
                // learned: from now on the scissors hint only plays on a tap, a swipe off the line or a long idle
                Game.Data.feedTornOnce = true;
                Game.I.Save();
            }
            AquaCare.Log($"tear {s.feed.id}: cover {cover.Count(c => c)}/{cover.Length} dir {swipeDir}");
            Fx.Frames(s.tearS, Snap(s.home + new Vector2(0f, 11f) / Ppu), 0.06f, 48, Color.white);
            StartCoroutine(StripFall(s, swipeDir == 0f ? 1f : swipeDir));
            Sfx.Play(AquaSfx.Rip, 0.9f);
            Tween.Punch(s.sr.transform, 0.06f, 0.12f);
            StartCoroutine(HintLater(0.55f, s.home + new Vector2(0f, 1.45f), "수조 위로 끌어서 뿌려요")); // (after the strip has flown)
        }

        const float IdleHint = 8f;  // s without input before the sealed bags snip their scissors hint once
        float idleT;
        bool idleHinted;

        /// <summary>Plays the scissors hint of a sealed bag once more from its start (it loops anyway until a first tear).</summary>
        void ReplayHint(Slot s)
        {
            if (s.state != BagState.Sealed) return;
            s.hintPhase = 0f;
            s.hintOnce = true;
        }

        IEnumerator HintLater(float delay, Vector2 world, string text)
        {
            yield return new WaitForSeconds(delay);
            if (drag == null) ShowHint(world, text);
        }

        IEnumerator StripFall(Slot s, float dir)
        {
            var sr = NewSprite("FeedStrip", s.stripS[0], 47);
            Vector2 p = s.home + new Vector2(0f, 13f) / Ppu;
            Vector2 v = new Vector2(dir * 1.1f, 1.6f);
            float t = 0f;
            const float life = 0.45f;
            while (t < life)
            {
                t += Time.deltaTime;
                if (t > 0.05f)
                {
                    v.y -= 11f * Time.deltaTime;
                    p += v * Time.deltaTime;
                    sr.sprite = s.stripS[1 + ((int)((t - 0.05f) / 0.08f)) % 2];
                    sr.flipX = dir < 0f;
                }
                sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - (t - 0.15f) / (life - 0.15f)));
                Place(sr, p);
                yield return null;
            }
            Destroy(sr.gameObject);
        }

        // ------------------------------------------------------------------ carrying and pouring
        void StartDrag(Slot s, Vector2 w)
        {
            drag = s;
            returning = null;
            grip = s.home + GripPx / Ppu;
            gripOffset = grip - w;
            lastPointer = w;
            vel = Vector2.zero;
            pose = Pose.Carry;
            poseT = 0f;
            emitAcc = 0f;
            wantPour = false;
            s.sr.sortingOrder = 52;
            Sfx.Play(Sfx.Rustle, 0.7f);
            AquaCare.Log($"pick up {s.feed.id}: portions {AquaCare.Stock(s.feed.id).portions:0.0}");
        }

        void Release()
        {
            if (drag == null) return;
            var s = drag;
            drag = null;
            returning = s;
            returnFrom = grip;
            returnT = 0f;
            pose = Pose.Carry;
            Game.I.Save();
            AquaCare.Log($"release {s.feed.id}: portions {AquaCare.Stock(s.feed.id).portions:0.0}, emitted {Emitted}, eaten {Eaten}");
        }

        Vector2 MouthOf(Vector2 g, bool flipped) => g + new Vector2(flipped ? -PourMouthPx.x : PourMouthPx.x, PourMouthPx.y) / Ppu;

        void UpdateDrag(float dt)
        {
            if (drag != null)
            {
                var s = drag;
                var half = pv.ViewSize * 0.5f;
                Vector2 target = lastPointer + gripOffset;
                target.x = Mathf.Clamp(target.x, -half.x + 1.2f, half.x - 1.2f);
                target.y = Mathf.Clamp(target.y, -half.y + 1.2f, half.y - 1.2f);
                Vector2 step = target - grip;
                grip = target;
                vel = Vector2.Lerp(vel, step / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 14f));
                // pour towards the middle of the tank
                if (grip.x > 0.8f) flip = true;
                else if (grip.x < -0.8f) flip = false;
                var mouth = MouthOf(grip, flip);
                wantPour = mouth.y > box.surfaceY + 0.15f && mouth.x > TankMinX && mouth.x < TankMaxX;
                Pose next = pose;
                poseT += dt;
                if (wantPour && pose == Pose.Carry) { next = Pose.Tilt; }
                else if (wantPour && pose == Pose.Tilt && poseT > 0.07f) next = Pose.Pour;
                else if (!wantPour && pose == Pose.Pour) next = Pose.Tilt;
                else if (!wantPour && pose == Pose.Tilt && poseT > 0.07f) next = Pose.Carry;
                if (next != pose)
                {
                    pose = next;
                    poseT = 0f;
                }
                switch (pose)
                {
                    case Pose.Carry:
                        s.sr.sprite = OpenSprite(s);
                        s.sr.flipX = false;
                        Place(s.sr, grip - GripPx / Ppu);
                        break;
                    case Pose.Tilt:
                        s.sr.sprite = s.tiltS;
                        s.sr.flipX = flip;
                        Place(s.sr, grip);
                        break;
                    default:
                        s.sr.sprite = s.pourS;
                        s.sr.flipX = flip;
                        Place(s.sr, grip);
                        Emit(s, dt);
                        break;
                }
                s.shadow.enabled = false;
            }
            else if (returning != null)
            {
                var s = returning;
                returnT += dt / 0.28f;
                float k = 1f - (1f - Mathf.Clamp01(returnT)) * (1f - Mathf.Clamp01(returnT));
                var g = Vector2.Lerp(returnFrom, s.home + GripPx / Ppu, k);
                bool empty = AquaCare.Stock(s.feed.id).portions <= 0.001f;
                s.sr.sprite = returnT < 0.25f ? s.tiltS : empty ? s.openS[0] : OpenSprite(s);
                s.sr.flipX = returnT < 0.25f && flip;
                Place(s.sr, returnT < 0.25f ? g : g - GripPx / Ppu);
                if (returnT >= 1f)
                {
                    returning = null;
                    s.sr.sortingOrder = 45 + s.index;
                    s.sr.flipX = false;
                    Place(s.sr, s.home);
                    Sfx.Play(Sfx.Knock, 0.35f, 1.3f);
                    if (empty) StartFold(s);
                    else s.state = BagState.Open;
                }
            }
        }

        void Emit(Slot s, float dt)
        {
            var st = AquaCare.Stock(s.feed.id);
            var mouth = MouthOf(grip, flip);
            if (fish.Count == 0)
            {
                ShowHint(mouth + new Vector2(0f, 1.1f), "물고기가 없어요", 0.4f);
                return;
            }
            int eaters = 0, fullEaters = 0;
            foreach (var f in fish)
                if (f != null && AquaCare.Eats(f.Data, Diet.Pellet))
                {
                    eaters++;
                    if (AquaCare.Full(f.Data)) fullEaters++;
                }
            if (eaters > 0 && fullEaters == eaters)
            {
                // the pellet eaters are full: a hungry fish that wants something else says so, else 배불러요!
                var other = HungryNonEater(Diet.Pellet, mouth);
                if (other != null) FoodHint(other, 0.4f);
                else ShowHint(mouth + new Vector2(0f, 1.1f), "배불러요!", 0.4f);
                return;
            }
            if (eaters == 0)
            {
                // nobody here eats pellets (a tank of big fish): the few that fall in are ignored, and a fish says what it likes
                var other = HungryNonEater(Diet.Pellet, mouth) ?? NearestFish(mouth);
                if (other != null) FoodHint(other, 0.4f);
            }
            // food already in the water covers the pellet eaters' hunger: hold back (no feed used)
            float need = 0f;
            foreach (var f in fish) if (f != null && AquaCare.Eats(f.Data, Diet.Pellet) && !AquaCare.Full(f.Data)) need += 1f - f.Data.fullness;
            float inWater = pellets.Count(p => !p.dead) * AquaCare.PelletFill;
            if (inWater >= need + AquaCare.PelletFill || pellets.Count >= MaxPellets) return;
            float speed = Mathf.Min(vel.magnitude, 5f);
            float rate = 2.5f + 5f * speed; // pellets/s: a trickle held still, a shower shaken
            emitAcc += rate * dt;
            while (emitAcc >= 1f)
            {
                emitAcc -= 1f;
                SpawnPellet(s.feed, mouth);
                st.portions = Mathf.Max(0f, st.portions - 1f / AquaCare.PelletsPerPortion);
                if (soundT <= 0f)
                {
                    Sfx.Play(AquaSfx.Tick, 0.35f, Random.Range(0.9f, 1.2f));
                    soundT = 0.07f;
                }
                if (st.portions <= 0.001f)
                {
                    st.portions = 0f;
                    Toast.Show($"{s.feed.name} 봉투가 비었어요", UIKit.Cream);
                    Release();
                    return;
                }
            }
        }

        /// <summary>Test hook (-fkauto newspecies, -fkaqua species): <paramref name="n"/> pellets of a feed poured at <paramref name="at"/> (world), as a bag pours them (no pointer, no feed used).</summary>
        public void DebugPellets(string feedId, Vector2 at, int n)
        {
            var f = AquaCare.Feed(feedId);
            if (f == null || f.live) return;
            for (int i = 0; i < n; i++) SpawnPellet(f, at);
        }

        void SpawnPellet(FeedDef feed, Vector2 mouth)
        {
            var arr = feed.premium ? flakeS : pelletS;
            var p = new Pellet
            {
                feed = feed,
                pos = mouth + new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) / Ppu,
                phase = Random.Range(0f, 6.28f),
                sink = feed.premium ? Random.Range(0.35f, 0.45f) : Random.Range(0.5f, 0.65f),
            };
            var dir = new Vector2(flip ? -PourDir.x : PourDir.x, PourDir.y);
            p.vel = dir * Random.Range(0.7f, 1.5f) + vel * 0.3f + new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(-0.2f, 0.2f));
            p.sr = NewSprite("Pellet", arr[Random.Range(0, arr.Length)], 51);
            p.sr.flipX = Random.value < 0.5f;
            if (feed.premium)
            {
                p.glint = NewSprite("Glint", glintS[0], 52);
                p.glint.enabled = false;
                p.glintT = Random.Range(0.3f, 1.5f);
            }
            Place(p.sr, p.pos);
            pellets.Add(p);
            Emitted++;
        }

        // ------------------------------------------------------------------ pellets
        void UpdatePellets(float dt)
        {
            soundT -= dt;
            plinkT -= dt;
            for (int i = pellets.Count - 1; i >= 0; i--)
            {
                var p = pellets[i];
                if (p.dead)
                {
                    Kill(p);
                    pellets.RemoveAt(i);
                    continue;
                }
                p.t += dt;
                switch (p.state)
                {
                    case 0:
                        p.vel.y -= 16f * dt;
                        p.pos += p.vel * dt;
                        p.pos.x = Mathf.Clamp(p.pos.x, TankMinX, TankMaxX);
                        if (p.pos.y <= box.surfaceY)
                        {
                            p.pos.y = box.surfaceY - 0.02f;
                            p.state = 1;
                            p.sr.sortingOrder = 32;
                            p.vel = new Vector2(p.vel.x * 0.25f, -Mathf.Min(1.6f, Mathf.Abs(p.vel.y) * 0.4f + 0.6f));
                            Fx.Frames(splashS, new Vector2(Mathf.Round(p.pos.x * Ppu) / Ppu, SurfaceFx), 0.07f, 36, Color.white);
                            if (plinkT <= 0f)
                            {
                                Sfx.Play(AquaSfx.Plink, 0.3f, Random.Range(0.85f, 1.25f));
                                plinkT = 0.05f;
                            }
                        }
                        break;
                    case 1:
                        float drift = Mathf.Sin(p.t * 2.1f + p.phase) * (p.feed.premium ? 0.32f : 0.2f);
                        p.vel.y = Mathf.MoveTowards(p.vel.y, -p.sink, 2.5f * dt);
                        p.vel.x = Mathf.Lerp(p.vel.x, drift, 1f - Mathf.Exp(-dt * 2f));
                        p.pos += p.vel * dt;
                        p.pos.x = Mathf.Clamp(p.pos.x, TankMinX, TankMaxX);
                        if (p.pos.y <= BottomY + Mathf.Sin(p.phase * 3f) * 0.08f)
                        {
                            p.state = 2;
                            p.rest = Random.Range(4.5f, 6.5f);
                        }
                        break;
                    default:
                        p.rest -= dt;
                        if (p.rest <= 0f)
                        {
                            // left uneaten: it melts into a little cloud on the gravel (and dirties it: AquaTank)
                            Fx.Frames(dissolveS, Snap(p.pos + new Vector2(0f, 1f / Ppu)), 0.15f, 33, p.feed.food);
                            p.dead = true;
                            AquaTank.OnUneaten(p.pos, false);
                        }
                        break;
                }
                if (p.glint != null)
                {
                    p.glintT -= dt;
                    if (p.glintT <= 0f && !p.glint.enabled && p.state >= 1)
                    {
                        p.glint.enabled = true;
                        p.glint.sprite = glintS[Random.value < 0.4f ? 1 : 0];
                        p.glintT = 0.14f;
                    }
                    else if (p.glintT <= 0f && p.glint.enabled)
                    {
                        p.glint.enabled = false;
                        p.glintT = Random.Range(0.6f, 1.6f);
                    }
                    p.glint.sortingOrder = p.sr.sortingOrder + 1;
                    Place(p.glint, p.pos);
                }
                Place(p.sr, p.pos);
            }
        }

        void Kill(Pellet p)
        {
            if (p.sr != null) Destroy(p.sr.gameObject);
            if (p.glint != null) Destroy(p.glint.gameObject);
        }

        /// <summary>The pellet in the water nearest to <paramref name="from"/> (null = none).</summary>
        public Pellet NearestPellet(Vector2 from)
        {
            Pellet best = null;
            float bd = float.MaxValue;
            foreach (var p in pellets)
            {
                if (p.dead || p.state == 0) continue;
                float d = (p.pos - from).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>A fish's mouth reached a pellet.</summary>
        public void Eat(TankFish tf, Pellet p)
        {
            if (p == null || p.dead) return;
            p.dead = true;
            bool wasFull = AquaCare.Full(tf.Data);
            AquaCare.Eat(Game.Data, tf.Data, p.feed);
            Eaten++;
            LastEatTime = Time.time;
            LastEatAt = tf.Mouth;
            LastEatKind = Diet.Pellet;
            Fx.Frames(crumbS, Snap(tf.Mouth), 0.08f, 34, p.feed.food);
            Sfx.Play(AquaSfx.Nom, 0.45f, Random.Range(0.9f, 1.15f));
            if (!wasFull && AquaCare.Full(tf.Data)) GotFull(tf);
        }

        const float LiveCheerDelay = 0.5f; // s after a live piece is eaten before its 배불러요! (the gulp's splash: 0.35 s)

        /// <summary>
        /// A fish just got full: its mood icon and a "배불러요!" floating up over it (it ignores food now); after a gulp at
        /// the surface a moment later, once it has sunk back and the splash is over.
        /// </summary>
        void GotFull(TankFish tf, float delay = 0f)
        {
            FullPops++;
            AquaCare.Log($"full {tf.Data.speciesId}: fullness {tf.Data.fullness:0.00}");
            if (delay > 0f) StartCoroutine(FullPopLater(tf, delay));
            else FullPop(tf);
        }

        IEnumerator FullPopLater(TankFish tf, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (tf != null) FullPop(tf);
        }

        void FullPop(TankFish tf)
        {
            tf.ShowMood(fullIcon, 1.4f);
            UIKit.PlateLabel(ui, "배불러요!", 16, UIKit.Good, out var plate);
            plate.name = "FullPop";
            var rt = plate.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            var at = ToCanvas(tf.Pos + new Vector2(0f, tf.HalfHeight + 1.15f));
            at.y = Mathf.Min(at.y, ui.rect.height - 150f); // (under the title, the top bar and the hint)
            rt.anchoredPosition = at;
            Tween.FloatUp(rt, 22f, 1.2f);
        }

        // ------------------------------------------------------------------ fullness
        static int KindIndex(Diet k) => k == Diet.Shrimp ? 1 : k == Diet.Sardine ? 2 : 0;

        /// <summary>
        /// Everyone who eats the food just eaten is full: "배불러요!" (with a pellet-only tank that is the whole tank, as
        /// before; a tank of big fish celebrates its sardine eaters).
        /// </summary>
        void UpdateFullness(float dt)
        {
            bool done = false;
            foreach (var k in AquaCare.Kinds)
            {
                int n = 0, full = 0;
                foreach (var f in fish)
                    if (f != null && AquaCare.Eats(f.Data, k))
                    {
                        n++;
                        if (AquaCare.Full(f.Data)) full++;
                    }
                bool all = n > 0 && full == n;
                int i = KindIndex(k);
                // after a shrimp / sardine the bite or the gulp's splash plays out first (the toast would cover the surface)
                if (all && !groupWasFull[i] && k == LastEatKind && k != Diet.Pellet && Time.time - LastEatTime < LiveCheerDelay) continue;
                if (all && !groupWasFull[i] && !done && k == LastEatKind && Time.time - LastEatTime < 4f)
                {
                    done = true;
                    Celebrations++;
                    Toast.Show("배불러요!", UIKit.Good, 2.2f);
                    Sfx.Play(AquaSfx.Chime, 0.7f);
                    foreach (var f in fish) if (f != null && AquaCare.Eats(f.Data, k)) f.ShowMood(fullIcon, 2.2f);
                    Game.I.Save();
                    AquaCare.Log($"all {AquaCare.KindName(k)} eaters full ({n}): emitted {Emitted}, eaten {Eaten}, left in water {PelletsAlive}, live eaten {LiveEaten}");
                }
                groupWasFull[i] = all;
            }
        }

        /// <summary>The fish nearest to <paramref name="at"/> that is hungry and does not eat <paramref name="k"/> (null = none).</summary>
        TankFish HungryNonEater(Diet k, Vector2 at)
        {
            TankFish best = null;
            float bd = float.MaxValue;
            foreach (var f in fish)
            {
                if (f == null || AquaCare.Eats(f.Data, k) || !AquaCare.Hungry(f.Data)) continue;
                float d = (f.Pos - at).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = f;
                }
            }
            return best;
        }

        TankFish NearestFish(Vector2 at)
        {
            TankFish best = null;
            float bd = float.MaxValue;
            foreach (var f in fish)
            {
                if (f == null) continue;
                float d = (f.Pos - at).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = f;
                }
            }
            return best;
        }

        /// <summary>"이 물고기는 정어리를 좋아해요" over a fish.</summary>
        void FoodHint(TankFish tf, float time = 1.8f)
        {
            ShowHint(tf.Pos + new Vector2(0f, tf.HalfHeight + 0.9f), AquaCare.LikesText(tf.Data), time);
        }

        /// <summary>Everyone shows how they feel for a moment (the scene opening, a time jump).</summary>
        public void ShowMoods(float time = 2f)
        {
            foreach (var f in fish)
                if (f != null) f.ShowMood(AquaCare.Hungry(f.Data) ? hungryIcon : AquaCare.Full(f.Data) ? fullIcon : null, time);
        }

        public Sprite HungryIcon => hungryIcon;

        // ------------------------------------------------------------------ the bags at rest
        void DrawSlot(Slot s, float dt)
        {
            if (s == drag || s == returning)
            {
                s.scissors.enabled = s == swipe && s.scissors.enabled;
                s.shadow.enabled = false;
                return;
            }
            float alpha = 1f, scale = 1f;
            switch (s.state)
            {
                case BagState.None:
                    s.sr.enabled = false;
                    s.shadow.enabled = false;
                    s.scissors.enabled = false;
                    return;
                case BagState.Sealed:
                    s.sr.sprite = s.sealedS;
                    break;
                case BagState.Open:
                    s.sr.sprite = OpenSprite(s);
                    break;
                case BagState.Folding:
                    s.anim += dt;
                    s.sr.sprite = s.anim < 0.25f ? s.foldS[0] : s.foldS[1];
                    if (s.anim > 0.6f) alpha = Mathf.Clamp01(1f - (s.anim - 0.6f) / 0.3f);
                    if (s.anim >= 0.9f)
                    {
                        s.state = BagState.None;
                        s.anim = 0f;
                        SyncSlot(s);
                        AquaCare.Log($"folded {s.feed.id}: next {s.state}, sealed {AquaCare.Stock(s.feed.id).bags}");
                    }
                    break;
            }
            if (s.popping)
            {
                s.anim += dt;
                float k = Mathf.Clamp01(s.anim / 0.22f);
                scale = Mathf.LerpUnclamped(0.6f, 1f, Tween.BackOut(k));
                alpha = k;
                if (k >= 1f)
                {
                    s.popping = false;
                    s.anim = 0f;
                }
            }
            s.sr.enabled = true;
            s.sr.flipX = false;
            s.sr.color = new Color(1f, 1f, 1f, alpha);
            s.sr.transform.localScale = new Vector3(scale, scale, 1f);
            Place(s.sr, s.home + new Vector2(0f, (scale - 1f) * -18f / Ppu));
            s.shadow.enabled = true;
            s.shadow.color = new Color(1f, 1f, 1f, alpha);
            // the scissors hint snipping along the perforation of a sealed bag: it loops until the player has torn a bag
            // open once; after that only one snip after a tap, a swipe off the line or a long idle (ReplayHint)
            bool tutorial = Game.Data == null || !Game.Data.feedTornOnce;
            if (s.state == BagState.Sealed && swipe == null && drag == null && !s.popping && (tutorial || s.hintOnce))
            {
                s.hintPhase += dt;
                float cyc = s.hintPhase % 3.4f;
                bool on = cyc < 1.2f;
                if (!tutorial && s.hintPhase >= 1.2f) { s.hintOnce = false; on = false; }
                s.scissors.enabled = on;
                if (on)
                {
                    float x = Mathf.Lerp(-PerfHalf - 3f, PerfHalf - 2f, cyc / 1.2f);
                    s.scissors.flipX = false;
                    s.scissors.sprite = ((int)(cyc / 0.1f)) % 2 == 0 ? scissorsA : scissorsB;
                    Place(s.scissors, s.home + new Vector2(x - 4f, PerfY + 0.5f) / Ppu);
                }
            }
            else if (s != swipe) s.scissors.enabled = false;
        }

        void StartFold(Slot s)
        {
            s.state = BagState.Folding;
            s.anim = 0f;
            Sfx.Play(Sfx.Rustle, 0.6f, 0.8f);
            AquaCare.Log($"fold {s.feed.id}: sealed left {AquaCare.Stock(s.feed.id).bags}");
        }

        // ------------------------------------------------------------------ UI overlay
        void ShowHint(Vector2 world, string text, float time = 1.8f)
        {
            if (hint.text != text || hintTime <= 0.1f)
            {
                hint.text = text;
                UIKit.FitPlate(hint, hintPlate);
            }
            hintAt = new Vector2(world.x, Mathf.Min(world.y, box.surfaceY)); // (kept under the top bar)
            hintTime = Mathf.Max(hintTime, time);
        }

        void UpdateUI(float dt)
        {
            foreach (var s in slots)
            {
                int spare = AquaCare.Stock(s.feed.id).bags - (s.state == BagState.Sealed ? 1 : 0);
                bool show = spare > 0 && (s.state == BagState.Sealed || s.state == BagState.Open) && s != drag && s != returning;
                s.badge.enabled = show;
                s.badgeText.enabled = show;
                if (show)
                {
                    s.badgeText.text = "+" + spare;
                    s.badge.rectTransform.anchoredPosition = ToCanvas(s.home + new Vector2(0f, 19f) / Ppu);
                }
            }
            // no bags: the faint outline where one would stand; no food at all: with "상점에서 사료를 사요" beside it (the
            // tub / cooler slots then stay hidden under the label)
            bool none = slots.All(s => s.state == BagState.None) && drag == null && returning == null;
            bool noFood = none && !AnyBoxShown;
            outline.enabled = none;
            emptyPlate.enabled = noFood;
            emptyLabel.enabled = noFood;
            if (noFood) emptyPlate.rectTransform.anchoredPosition = ToCanvas(new Vector2(SlotX[0] + 1.3f, LedgeCentreY));
            hintTime -= dt;
            bool h = hintTime > 0f && !string.IsNullOrEmpty(hint.text);
            hintPlate.enabled = h;
            hint.enabled = h;
            if (h) hintPlate.rectTransform.anchoredPosition = ToCanvas(hintAt);
        }
    }

    /// <summary>The feeding's little sounds, synthesized like <see cref="Sfx"/>'s (no audio files).</summary>
    public static class AquaSfx
    {
        const int Rate = 22050;
        static AudioClip rip, plink, tick, nom, chime;

        public static AudioClip Rip => rip != null ? rip : rip = MakeRip();
        public static AudioClip Plink => plink != null ? plink : plink = MakePlink();
        public static AudioClip Tick => tick != null ? tick : tick = MakeTick();
        public static AudioClip Nom => nom != null ? nom : nom = MakeNom();
        public static AudioClip Chime => chime != null ? chime : chime = MakeChime();
        static AudioClip splash, gulp, lid;
        public static AudioClip Splash => splash != null ? splash : splash = MakeSplash();
        public static AudioClip Gulp => gulp != null ? gulp : gulp = MakeGulp();
        public static AudioClip Lid => lid != null ? lid : lid = MakeLid();

        /// <summary>A shrimp / sardine hitting the water: a noise burst over a falling "bloop".</summary>
        static AudioClip MakeSplash()
        {
            int n = (int)(0.22f * Rate);
            var d = new float[n];
            var r = new System.Random(79);
            double ph = 0;
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                y += 0.5f * ((float)r.NextDouble() * 2f - 1f - y);
                ph += Mathf.Lerp(700f, 220f, Mathf.Sqrt(t)) / Rate;
                float noise = y * Mathf.Pow(1f - t, 4f);
                float bloop = Mathf.Sin((float)ph * 2f * Mathf.PI) * Mathf.Pow(1f - t, 2f) * 0.6f;
                d[i] = (noise + bloop) * Mathf.Min(1f, i / (0.003f * Rate));
            }
            return Make("feed_splash", d);
        }

        /// <summary>A big fish gulping at the surface: a low rising "gloop", then the spray.</summary>
        static AudioClip MakeGulp()
        {
            int n = (int)(0.42f * Rate);
            var d = new float[n];
            var r = new System.Random(83);
            double ph = 0;
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                ph += Mathf.Lerp(90f, 260f, Mathf.Clamp01(t / 0.35f)) / Rate;
                float gloop = t < 0.4f ? Mathf.Sin((float)ph * 2f * Mathf.PI) * Mathf.Sin(t / 0.4f * Mathf.PI) : 0f;
                y += 0.6f * ((float)r.NextDouble() * 2f - 1f - y);
                float spray = t > 0.18f ? y * Mathf.Pow(1f - (t - 0.18f) / 0.82f, 3f) * 0.7f : 0f;
                d[i] = gloop + spray;
            }
            return Make("feed_gulp", d);
        }

        /// <summary>A plastic lid snapping open / shut: two quick clicks.</summary>
        static AudioClip MakeLid()
        {
            int n = (int)(0.09f * Rate);
            var d = new float[n];
            var r = new System.Random(89);
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float w = (float)r.NextDouble() * 2f - 1f;
                float a = t < 0.3f ? Mathf.Pow(1f - t / 0.3f, 3f) : t > 0.55f ? Mathf.Pow(1f - (t - 0.55f) / 0.45f, 3f) * 0.6f : 0f;
                d[i] = (w - prev) * a;
                prev = w;
            }
            return Make("feed_lid", d);
        }

        static AudioClip Make(string name, float[] d)
        {
            float peak = 0.0001f;
            foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            float g = Mathf.Min(1f, 0.9f / peak);
            for (int i = 0; i < d.Length; i++) d[i] *= g;
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>Paper-foil tearing: band noise chopped into grains (the fibres giving way one by one).</summary>
        static AudioClip MakeRip()
        {
            int n = (int)(0.3f * Rate);
            var d = new float[n];
            var r = new System.Random(71);
            float y = 0f, lo = 0f, grain = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                if (i % 45 == 0) grain = r.NextDouble() < 0.25 ? 0.15f : 0.5f + 0.5f * (float)r.NextDouble();
                y += 0.7f * ((float)r.NextDouble() * 2f - 1f - y);
                lo += 0.08f * (y - lo);
                float env = Mathf.Min(1f, i / (0.008f * Rate)) * (t < 0.7f ? 1f - 0.3f * t : (1f - t) / 0.3f * 0.79f);
                d[i] = (y - lo) * grain * env;
            }
            return Make("feed_rip", d);
        }

        static AudioClip MakePlink()
        {
            int n = (int)(0.07f * Rate);
            var d = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                ph += Mathf.Lerp(1500f, 650f, t) / Rate;
                d[i] = Mathf.Sin((float)ph * 2f * Mathf.PI) * Mathf.Pow(1f - t, 2.5f);
            }
            return Make("feed_plink", d);
        }

        static AudioClip MakeTick()
        {
            int n = (int)(0.018f * Rate);
            var d = new float[n];
            var r = new System.Random(73);
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float w = (float)r.NextDouble() * 2f - 1f;
                d[i] = (w - prev) * Mathf.Pow(1f - t, 3f);
                prev = w;
            }
            return Make("feed_tick", d);
        }

        static AudioClip MakeNom()
        {
            int n = (int)(0.08f * Rate);
            var d = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                ph += Mathf.Lerp(420f, 260f, t) / Rate;
                float tri = 4f * Mathf.Abs((float)(ph % 1.0) - 0.5f) - 1f;
                d[i] = tri * Mathf.Min(1f, i / (0.004f * Rate)) * Mathf.Pow(1f - t, 1.5f);
            }
            return Make("feed_nom", d);
        }

        static AudioClip MakeChime()
        {
            int n = (int)(0.5f * Rate);
            var d = new float[n];
            float[] f = { 988f, 1319f };
            for (int k = 0; k < 2; k++)
            {
                int s0 = (int)(k * 0.1f * Rate);
                double ph = 0;
                for (int i = s0; i < n; i++)
                {
                    float t = (float)(i - s0) / (n - s0);
                    ph += f[k] / Rate;
                    d[i] += Mathf.Sin((float)ph * 2f * Mathf.PI) * Mathf.Pow(1f - t, 2f) * 0.5f;
                }
            }
            return Make("feed_chime", d);
        }
    }
}
