using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The encounter's HUD (Docs/lures_legend_spec.md 2.4), on the fishing HUD's canvas and placed from the window's crop
    /// rectangle every frame: the prompt plate inside its top edge (the mood's verb icon and what to do), the interest
    /// gauge inside its bottom edge (mood icon, a 180 x 7 px bar in three zones: 경계 / 호기심 / 흥분, flashing red on a
    /// penalty and gold at the tell), captions (the fishing HUD's flash messages come here while the encounter is on) and
    /// the name card sliding in from the top-left. From the lunge on the prompt and gauge go and the captions show large.
    /// <para>Nothing may cover the legend's face (its eyes, mouth and head: <see cref="EncounterView.FaceRect"/>, every
    /// frame). Captions sit one line (or a line and a tip) on a dark translucent plate in a band: the window's bottom, just
    /// above the gauge, or from the lunge on the screen's lower third. When the face would fall under the band they flip
    /// to the top band (under the prompt / the screen's upper band), else slide to a side; the prompt, the gauge and the
    /// name card step aside the same way. Every overlay also keeps clear of the ones placed before it, the lure and the
    /// reel. A spot is left at once when the face reaches it, but only taken back (the preferred one) when clear by an
    /// extra margin for a while, so nothing jumps back and forth.</para>
    /// </summary>
    [DefaultExecutionOrder(1100)] // after the view has placed the window
    public class EncounterHUD : MonoBehaviour
    {
        static readonly Color[] MoodCol = { new Color32(0x7a, 0x8a, 0xa8, 0xff), new Color32(0x6a, 0xff, 0xea, 0xff), new Color32(0xff, 0xc8, 0x30, 0xff) };
        static readonly string[] MoodIcon = { "mood_wary", "mood_curious", "mood_excited" };
        // by Verb: Wind, FlickPause, Hold, RunPause (a wind run, then a pause: the wind icon)
        static readonly string[] VerbIcon = { "verb_wind", "verb_flick", "verb_hold", "verb_wind" };
        const float Px = UIKit.Px;               // canvas units per pixel-view pixel
        const float BarW = 180f * Px, BarH = 7f * Px;
        // around the face this much stays clear (canvas units); a spot the overlay is not on must be clear by Hyst more
        const float FacePad = 10f, Hyst = 14f;
        static readonly string[] CaptionSpots = { "bottom", "top", "bottom-left", "bottom-right", "top-left", "top-right" };
        static readonly float[] CaptionPrefs = { 0f, 4f, 6f, 6f, 8f, 8f };

        public static EncounterHUD Current { get; private set; }

        LegendEncounter enc;
        EncounterView view;
        RectTransform root, prompt, gauge, gaugePlate, caption, card;
        Image promptPlate, verbIcon, moodIcon, barBg, fill, cardPlate, captionPlate;
        Text promptText, moodText, captionSmall, captionBig, cardName, cardSub;
        Text captionText => big ? captionBig : captionSmall;
        CanvasGroup promptGroup, gaugeGroup, captionGroup, cardGroup;
        float captionT, captionLen, redT = -1f, goldT = -1f, cardT = -1f, cardShow, cardWait;
        bool cardClear = true;
        bool big, named, revealed;

        /// <summary>One overlay's place among a few candidate spots, kept with hysteresis.</summary>
        class Spot
        {
            public readonly float hold;           // how long a spot is kept before a clearly better one is taken
            public int cur = -1;
            public float held;
            public readonly List<Rect> rects = new List<Rect>();
            public readonly List<float> prefs = new List<float>();
            public Spot(float hold) => this.hold = hold;

            public void Clear()
            {
                rects.Clear();
                prefs.Clear();
            }

            public void Add(Rect r, float pref)
            {
                rects.Add(r);
                prefs.Add(pref);
            }

            public Rect Chosen => rects[Mathf.Clamp(cur, 0, rects.Count - 1)];
        }

        readonly Spot promptSpot = new Spot(0.8f), gaugeSpot = new Spot(1.0f), cardSpot = new Spot(0.8f), captionSpot = new Spot(0.5f);
        readonly List<Rect> blocks = new List<Rect>();
        bool hasFace, hasFrog;
        Rect face;                                // the face this frame (root-local canvas units), no margin
        Rect frog;                                // the top view's lure: kept clear like the face

        public static EncounterHUD Create(RectTransform canvasRoot, LegendEncounter e, EncounterView v)
        {
            var go = new GameObject("EncounterHUD", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            var hud = go.AddComponent<EncounterHUD>();
            hud.root = canvasRoot;
            ((RectTransform)go.transform).Fill();
            hud.enc = e;
            hud.view = v;
            hud.Build();
            Current = hud;
            return hud;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        static Sprite Icon(string name) => Resources.Load<Sprite>("Sprites/UI/" + name);

        void Build()
        {
            var me = (RectTransform)transform;
            // prompt plate: [verb] prompt
            promptPlate = UIKit.Panel(me, "panel_dark", new Color(1, 1, 1, 0.8f), "Prompt");
            promptPlate.raycastTarget = false;
            prompt = promptPlate.rectTransform;
            prompt.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 40), new Vector2(0.5f, 1f));
            verbIcon = UIKit.Img(prompt, null, new Vector2(16, 16) * Px, "Verb");
            verbIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(16, 16) * Px, new Vector2(0, 0.5f));
            promptText = UIKit.Label(prompt, "", 18, UIKit.Cream, TextAnchor.MiddleLeft);
            promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
            promptText.rectTransform.Fill(48, 10, 2, 2);
            promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            promptGroup.blocksRaycasts = false;

            // gauge: [mood] label [=====|====|===]
            gauge = UIKit.Rect(me, "Gauge").At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BarW + 150, 30), new Vector2(0.5f, 0f));
            var gp = UIKit.Panel(gauge, "panel_dark", new Color(1, 1, 1, 0.72f), "Plate");
            gp.raycastTarget = false;
            gp.rectTransform.Fill(-6, -6, -4, -4);
            gaugePlate = gp.rectTransform;
            moodIcon = UIKit.Img(gauge, null, new Vector2(12, 12) * Px, "Mood");
            moodIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(12, 12) * Px, new Vector2(0, 0.5f));
            moodText = UIKit.Label(gauge, "", 16, MoodCol[0], TextAnchor.MiddleLeft);
            moodText.horizontalOverflow = HorizontalWrapMode.Overflow;
            moodText.rectTransform.At(new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(100, 30), new Vector2(0, 0.5f));
            barBg = UIKit.Solid(gauge, new Color(0.04f, 0.06f, 0.1f, 0.9f), "Bar");
            barBg.rectTransform.At(new Vector2(1, 0.5f), Vector2.zero, new Vector2(BarW + 4, BarH + 4), new Vector2(1, 0.5f));
            var def = enc.Def;
            // the three zones under the fill, dim, with 1 px ticks at the thresholds
            Zone(0, def.curiousAt, MoodCol[0]);
            Zone(def.curiousAt, def.excitedAt, MoodCol[1]);
            Zone(def.excitedAt, 100, MoodCol[2]);
            fill = UIKit.Solid(barBg.transform, MoodCol[0], "Fill");
            fill.rectTransform.anchorMin = new Vector2(0, 0);
            fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            fill.rectTransform.offsetMin = new Vector2(2, 2);
            fill.rectTransform.offsetMax = new Vector2(2, -2);
            foreach (int at in new[] { def.curiousAt, def.excitedAt })
            {
                var tick = UIKit.Solid(barBg.transform, new Color(1, 1, 1, 0.8f), "Tick");
                tick.rectTransform.At(new Vector2(0, 0.5f), new Vector2(2 + BarW * at / 100f, 0), new Vector2(Px, BarH + 4), new Vector2(0.5f, 0.5f));
            }
            gaugeGroup = gauge.gameObject.AddComponent<CanvasGroup>();
            gaugeGroup.blocksRaycasts = false;

            // caption: one line (or a line and a tip) on a dark translucent plate sized to it
            caption = UIKit.Rect(me, "Caption").At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 34), new Vector2(0.5f, 0.5f));
            captionPlate = UIKit.Panel(caption, "panel_dark", new Color(1, 1, 1, 0.6f), "Plate");
            captionPlate.raycastTarget = false;
            captionPlate.rectTransform.Fill();
            captionSmall = UIKit.Label(caption, "", 20, UIKit.Cream);
            captionSmall.horizontalOverflow = HorizontalWrapMode.Overflow;
            captionSmall.rectTransform.Fill();
            captionBig = UIKit.Label(caption, "", 28, UIKit.Cream);
            captionBig.horizontalOverflow = HorizontalWrapMode.Overflow;
            captionBig.rectTransform.Fill();
            captionBig.enabled = false;
            captionGroup = caption.gameObject.AddComponent<CanvasGroup>();
            captionGroup.alpha = 0f;
            captionGroup.blocksRaycasts = false;

            // name card
            cardPlate = UIKit.Panel(me, "panel_dark", new Color(1, 1, 1, 0.85f), "NameCard");
            cardPlate.raycastTarget = false;
            card = cardPlate.rectTransform;
            card.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 58), new Vector2(0, 1));
            cardName = UIKit.Label(card, "", 20, RarityInfo.Color(Rarity.Legendary), TextAnchor.UpperLeft);
            cardName.horizontalOverflow = HorizontalWrapMode.Overflow;
            cardName.rectTransform.Fill(12, 8, 6, 26);
            cardSub = UIKit.Label(card, "", 15, UIKit.Cream, TextAnchor.LowerLeft);
            cardSub.horizontalOverflow = HorizontalWrapMode.Overflow;
            cardSub.rectTransform.Fill(12, 8, 30, 6);
            cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            cardGroup.alpha = 0f;
            cardGroup.blocksRaycasts = false;

            enc.MoodChanged += m =>
            {
                RefreshMood();
                Tween.Punch(gauge, 0.12f);
            };
            enc.Penalty += (k, text) =>
            {
                redT = 0f;
                Caption(text, UIKit.Bad, 1.2f);
            };
            // (from above the 흥분 credit and the nose-in's line are the top view's own)
            enc.Credit += text => Caption(enc.Mood == 2 && view.TopFlow && enc.Def.top?.holdCredit != null ? enc.Def.top.holdCredit : text,
                MoodCol[Mathf.Min(enc.Mood, 2)], 1.2f);
            enc.Tell += () =>
            {
                goldT = 0f;
                Caption("온다…!", UIKit.Gold, 0.8f);
            };
            enc.PhaseChanged += OnPhase;
            // the fake-out: its own caption (cream, no gold), then the tell's first line
            enc.FakeOut += () => Caption(enc.Def.fakeOutText ?? "…", UIKit.Cream, 0.8f);
            enc.FakeOutEnd += () =>
            {
                if (enc.Ph == LegendEncounter.Phase.NoseIn) Caption(enc.Def.tellText, UIKit.Cream, 0.8f);
            };
            enc.HookSet += perfect =>
            {
                // the two in one line (never stacked over each other)
                if (perfect) Caption(enc.PerfectLure ? "완벽한 챔질!  완벽한 유혹!" : "완벽한 챔질!", UIKit.Gold, 1.4f);
                else Caption("걸었다!", UIKit.Gold, 1.2f);
            };
            promptGroup.alpha = gaugeGroup.alpha = 0f;
            RefreshMood();
        }

        void Zone(float from, float to, Color c)
        {
            var z = UIKit.Solid(barBg.transform, new Color(c.r, c.g, c.b, 0.22f), "Zone");
            z.rectTransform.anchorMin = z.rectTransform.anchorMax = new Vector2(0, 0.5f);
            z.rectTransform.pivot = new Vector2(0, 0.5f);
            z.rectTransform.anchoredPosition = new Vector2(2 + BarW * from / 100f, 0);
            z.rectTransform.sizeDelta = new Vector2(BarW * (to - from) / 100f, BarH);
        }

        void RefreshMood()
        {
            int m = Mathf.Clamp(enc.Mood, 0, 2);
            var md = enc.Def.moods[m];
            moodText.text = md.name;
            moodText.color = MoodCol[m];
            var mi = Icon(MoodIcon[m]);
            moodIcon.sprite = mi;
            moodIcon.enabled = mi != null;
            // turning away: the 경계 action wins it back, so say that legend's 경계 action (slow for most, fast for the marlin)
            var calm = enc.Def.moods[0];
            if (enc.Leaving) SetPrompt("돌아서려 해요! " + calm.prompt, VerbIcon[(int)calm.verb]);
            else SetPrompt(md.prompt, VerbIcon[(int)md.verb]);
        }

        string shownPrompt;

        void SetPrompt(string text, string icon)
        {
            if (text == shownPrompt) return;
            shownPrompt = text;
            promptText.text = text;
            var vi = Icon(icon);
            verbIcon.sprite = vi;
            verbIcon.enabled = vi != null;
            promptText.rectTransform.Fill(vi != null ? 48 : 14, 12, 2, 2);
            prompt.sizeDelta = new Vector2(promptText.preferredWidth + (vi != null ? 62 : 28), 40);
            Tween.Punch(prompt, 0.08f);
        }

        void OnPhase(LegendEncounter.Phase p)
        {
            switch (p)
            {
                case LegendEncounter.Phase.Omen:
                case LegendEncounter.Phase.Open:
                    break;
                case LegendEncounter.Phase.Eyes:
                    Caption(enc.Def.eyesText, MoodCol[1], 2.2f);
                    break;
                case LegendEncounter.Phase.Approach:
                    ShowCard();
                    break;
                case LegendEncounter.Phase.Tease:
                    RefreshMood();
                    break;
                case LegendEncounter.Phase.NoseIn:
                    // with a fake-out its caption comes first and the tell's after it
                    if (enc.Def.fakeOuts <= 0)
                        Caption(view.TopFlow && enc.Def.top?.noseInText != null ? enc.Def.top.noseInText : enc.Def.tellText, UIKit.Cream, 1.0f);
                    break;
                case LegendEncounter.Phase.Lunge:
                    // full screen from here: the caption showing (the tell's) carries on large, in the screen's band
                    string still = captionT > 0.05f ? captionSmall.text : null;
                    Color stillCol = captionSmall.color;
                    big = true;
                    if (still != null) Caption(still, stillCol, Mathf.Max(0.5f, captionT));
                    else
                    {
                        captionSmall.enabled = false;
                        FitCaption();
                    }
                    break;
                case LegendEncounter.Phase.HookWindow:
                    Caption(enc.Def.biteText + " 지금! 챔질!", UIKit.Gold, 0.9f);
                    break;
                case LegendEncounter.Phase.TurnAway:
                    big = false;
                    Caption(enc.FailTip != null ? enc.FailText + "\n<size=16>" + enc.FailTip + "</size>" : enc.FailText, UIKit.Bad, 2.4f);
                    break;
            }
        }

        void ShowCard()
        {
            named = enc.Repeat;
            cardName.text = named ? enc.Sp.name : "???";
            cardSub.text = named ? enc.Def.nameSub : "전설의 물고기";
            cardT = 0f;
            cardShow = 2.2f;
            cardWait = 0f;
        }

        /// <summary>A short message in the window's caption band (or, from the lunge on, large in the screen's).</summary>
        public void Caption(string text, Color c, float time = 1.2f)
        {
            captionSmall.enabled = !big;
            captionBig.enabled = big;
            captionText.text = text;
            captionText.color = c;
            captionT = time;
            captionLen = time;
            captionGroup.alpha = 1f;
            var o = captionText.GetComponent<PixelOutline>();
            if (o != null) o.enabled = true;
            FitCaption();
            captionSpot.cur = -1; // a new caption takes the best spot for it now
            CaptionSerial++;
            Tween.Punch(caption, big ? 0.18f : 0.1f);
        }

        /// <summary>The plate hugs the text: 14 units each side, 6 above and below (large: 20 and 8).</summary>
        void FitCaption()
        {
            var t = captionText;
            float padX = big ? 20f : 14f, padY = big ? 8f : 6f;
            caption.sizeDelta = new Vector2(Mathf.Ceil(t.preferredWidth) + 2f * padX, Mathf.Ceil(Mathf.Max(big ? 36f : 26f, t.preferredHeight)) + 2f * padY);
        }

        Vector2 Local(Vector2 rtPx)
        {
            var screen = view.RTToScreen(rtPx);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            return local;
        }

        Rect LocalRect(Rect rtPx)
        {
            var a = Local(rtPx.min);
            var b = Local(rtPx.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        readonly Vector3[] corners = new Vector3[4];

        /// <summary>A rect as drawn now (punch scale included), root-local.</summary>
        Rect Drawn(RectTransform rt)
        {
            rt.GetWorldCorners(corners);
            var a = root.InverseTransformPoint(corners[0]);
            var b = root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static Rect Grow(Rect r, float m) => new Rect(r.x - m, r.y - m, r.width + 2f * m, r.height + 2f * m);

        static Rect Box(float cx, float cy, Vector2 size) => new Rect(cx - size.x * 0.5f, cy - size.y * 0.5f, size.x, size.y);

        static float Area(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        /// <summary>What a spot costs: on the face (with its margin) 1000 and up, on another overlay / the lure / the reel 100.</summary>
        float Cost(Rect r, bool current)
        {
            float c = 0f;
            if (hasFace)
            {
                var f = Grow(face, FacePad + (current ? 0f : Hyst));
                float a = Area(r, f);
                if (a > 0f) c += 1000f + a / 100f;
            }
            if (hasFrog)
            {
                float a = Area(r, Grow(frog, current ? 2f : 4f));
                if (a > 0f) c += 1000f + a / 100f;
            }
            // (the lure hops: a spot not taken must be clear of it and the others by the extra margin too)
            foreach (var b in blocks)
                if (Area(r, current ? b : Grow(b, Hyst)) > 0f) c += 100f;
            return c;
        }

        /// <summary>
        /// Picks the spot: the cheapest (its preference plus <see cref="Cost"/>) at first; after that the current one is
        /// left at once when it is on the face or an overlay, otherwise only for a clearly better one after holding a while.
        /// </summary>
        void Choose(Spot s, float dt)
        {
            int n = s.rects.Count;
            s.held += dt;
            if (s.cur < 0 || s.cur >= n)
            {
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float c = s.prefs[i] + Cost(s.rects[i], true);
                    if (c < best)
                    {
                        best = c;
                        s.cur = i;
                    }
                }
                s.held = 0f;
                return;
            }
            float hit = Cost(s.rects[s.cur], true), cc = s.prefs[s.cur] + hit;
            int bi = s.cur;
            float bc = cc;
            for (int i = 0; i < n; i++)
            {
                if (i == s.cur) continue;
                float c = s.prefs[i] + Cost(s.rects[i], false);
                if (c < bc)
                {
                    bc = c;
                    bi = i;
                }
            }
            if (bi != s.cur && (hit >= 100f || (s.held >= s.hold && bc + 1f < cc)))
            {
                s.cur = bi;
                s.held = 0f;
            }
        }

        void LateUpdate()
        {
            if (enc == null || view == null) return;
            float dt = Time.deltaTime;
            var ph = enc.Ph;
            bool windowUp = view.Open && ph >= LegendEncounter.Phase.Eyes && ph <= LegendEncounter.Phase.NoseIn;
            bool tease = ph == LegendEncounter.Phase.Tease || ph == LegendEncounter.Phase.NoseIn;
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, windowUp && tease ? 1f : 0f, dt * 5f);
            gaugeGroup.alpha = Mathf.MoveTowards(gaugeGroup.alpha, windowUp && tease ? 1f : 0f, dt * 5f);
            // (from above, the lunge is a cut to the underwater view: they go with it)
            if (view.TopFlow && ph >= LegendEncounter.Phase.Lunge) promptGroup.alpha = gaugeGroup.alpha = 0f;
            if (tease) RefreshMood();
            // the gauge
            float g = Mathf.Clamp(enc.Gauge, 0f, 100f);
            fill.rectTransform.sizeDelta = new Vector2(BarW * g / 100f, 0f);
            var fc = MoodCol[Mathf.Clamp(enc.Mood, 0, 2)];
            if (redT >= 0f)
            {
                redT += dt;
                if (redT < 0.2f) fc = UIKit.Bad;
                else redT = -1f;
            }
            fill.color = fc;
            var bc = new Color(0.04f, 0.06f, 0.1f, 0.9f);
            if (goldT >= 0f)
            {
                goldT += dt;
                if (goldT < 0.35f) bc = Color.Lerp(UIKit.Gold, bc, goldT / 0.35f);
                else goldT = -1f;
            }
            barBg.color = bc;
            // the name card: slides in for 2.2 s; the first time the name comes up when the gauge first reaches 60
            if (!named && !revealed && ph == LegendEncounter.Phase.Tease && enc.Gauge >= 60f)
            {
                revealed = named = true;
                cardName.text = enc.Sp.name;
                cardSub.text = enc.Def.nameSub;
                cardT = 0f;
                cardShow = 2.2f;
                cardWait = 0f;
                Tween.Punch(card, 0.15f);
            }
            if (cardT >= 0f)
            {
                // (from above the legend circles the lure mid-window: the card waits, up to 3 s, for a spot clear of
                // its head and the lure)
                if (view.TopView && cardT <= 0f && !cardClear && cardWait < 3f) cardWait += dt;
                else cardT += dt;
                cardGroup.alpha = view.Open ? Mathf.Clamp01(Mathf.Min(cardT / 0.3f, (cardShow + 0.3f - cardT) / 0.3f)) : 0f;
                if (cardT > cardShow + 0.3f) cardT = -1f;
            }
            else cardGroup.alpha = 0f;
            Place(dt, ph, windowUp && tease);
            if (captionT > 0f)
            {
                captionT -= dt;
                if (captionT < 0.3f)
                {
                    var o = captionText.GetComponent<PixelOutline>();
                    if (o != null && o.enabled) o.enabled = false;
                    captionGroup.alpha = Mathf.Clamp01(captionT / 0.3f);
                }
            }
            else captionGroup.alpha = 0f;
        }

        /// <summary>
        /// Places the prompt, the gauge, the name card and the caption (in that order, each clear of the ones before it)
        /// off the legend's face.
        /// </summary>
        void Place(float dt, LegendEncounter.Phase ph, bool teaseUp)
        {
            hasFace = view.FaceRect(out var fr);
            if (hasFace) face = LocalRect(fr);
            // from above the lure is as untouchable as the face (the prompt and the gauge keep off it too)
            hasFrog = view.TopView && view.LureShown;
            if (hasFrog) frog = LocalRect(view.LureBox);
            // canvas units per render-target pixel, and the window (before it has opened and while it closes: the full
            // window, so a caption then holds still)
            float k = (Local(new Vector2(100f, 0f)).x - Local(Vector2.zero).x) / 100f;
            bool whole = ph <= LegendEncounter.Phase.Open || ph >= LegendEncounter.Phase.Close;
            var win = LocalRect(whole ? view.Window : view.Crop);
            var scr = root.rect;
            blocks.Clear();
            // (only what is showing, or coming up, is in the way: an overlay fading out is not)
            {
                // the prompt inside the top edge: centred, or slid to a side
                var ps = prompt.sizeDelta;
                float top = win.yMax - 4f * k;
                promptSpot.Clear();
                promptSpot.Add(new Rect(win.center.x - ps.x * 0.5f, top - ps.y, ps.x, ps.y), 0f);
                promptSpot.Add(new Rect(win.xMin + 6f * k, top - ps.y, ps.x, ps.y), 4f);
                promptSpot.Add(new Rect(win.xMax - 6f * k - ps.x, top - ps.y, ps.x, ps.y), 4f);
                // (from above the head can cross the whole top band as the legend turns: the prompt may drop to just
                // above the gauge's band)
                if (view.TopView)
                    promptSpot.Add(new Rect(win.center.x - ps.x * 0.5f, win.yMin + 5f * k - 4f + (gauge.sizeDelta.y + 8f) + 4f, ps.x, ps.y), 10f);
                Choose(promptSpot, dt);
                var pr = promptSpot.Chosen;
                prompt.anchoredPosition = new Vector2(Mathf.Round(pr.center.x), Mathf.Round(pr.yMax));
                if (teaseUp) blocks.Add(pr);

                // the gauge inside the bottom edge, or under the prompt (its plate reaches 6 / 4 units past it)
                var gs = gauge.sizeDelta + new Vector2(12f, 8f);
                gaugeSpot.Clear();
                gaugeSpot.Add(new Rect(win.center.x + 10f * k - gs.x * 0.5f, win.yMin + 5f * k - 4f, gs.x, gs.y), 0f);
                gaugeSpot.Add(new Rect(win.center.x + 10f * k - gs.x * 0.5f, pr.yMin - 4f - gs.y, gs.x, gs.y), 6f);
                Choose(gaugeSpot, dt);
                var gr = gaugeSpot.Chosen;
                gauge.anchoredPosition = new Vector2(Mathf.Round(gr.center.x), Mathf.Round(gr.yMin + 4f));
                bool gaugeLow = gaugeSpot.cur == 0;
                if (teaseUp) blocks.Add(gr);

                // the name card: top-left under the prompt, else top-right, else low (above the gauge)
                var cs = card.sizeDelta;
                float cardTop = win.yMax - 26f * k, cardLow = (teaseUp && gaugeLow ? gr.yMax : win.yMin + 5f * k) + 4f;
                cardSpot.Clear();
                cardSpot.Add(new Rect(win.xMin + 6f * k, cardTop - cs.y, cs.x, cs.y), 0f);
                cardSpot.Add(new Rect(win.xMax - 6f * k - cs.x, cardTop - cs.y, cs.x, cs.y), 2f);
                cardSpot.Add(new Rect(win.xMin + 6f * k, cardLow, cs.x, cs.y), 5f);
                cardSpot.Add(new Rect(win.xMax - 6f * k - cs.x, cardLow, cs.x, cs.y), 5f);
                var saved = blocks.Count;
                if (view.LureShown) blocks.Add(LureRect(k));
                Choose(cardSpot, dt);
                cardClear = Cost(cardSpot.Chosen, true) < 1000f;
                blocks.RemoveRange(saved, blocks.Count - saved);
                var cr = cardSpot.Chosen;
                float slide = cardT >= 0f ? Mathf.Clamp01(cardT / 0.3f) : 0f;
                bool fromRight = cardSpot.cur == 1 || cardSpot.cur == 3;
                card.anchoredPosition = new Vector2(Mathf.Round(cr.xMin), Mathf.Round(cr.yMax)) +
                                        new Vector2((fromRight ? 40f : -40f) * (1f - slide * (2f - slide)), 0f);
                if (cardT >= 0f && view.Open && !(view.TopView && cardT <= 0f)) blocks.Add(cr);
            }

            // the caption: the window's band just above the gauge (or its bottom edge) / under the prompt, or from the
            // lunge on the screen's lower third / upper band; clear of the lure and the reel too
            if (view.LureShown) blocks.Add(LureRect(k));
            var reel = ReelGrip.Zone;
            blocks.Add(new Rect(scr.xMax + reel.xMin, scr.yMin + reel.yMin, reel.width, reel.height));
            var size = caption.sizeDelta * Mathf.Max(1f, caption.localScale.x);
            float lowY, highY, left, right;
            if (big)
            {
                lowY = scr.yMin + 0.12f * scr.height;
                highY = scr.yMax - 0.10f * scr.height;
                left = scr.xMin + 24f;
                right = scr.xMax - 24f;
            }
            else
            {
                var gr = gaugeSpot.Chosen;
                lowY = (teaseUp && gaugeSpot.cur == 0 ? gr.yMax : win.yMin + 5f * k) + 4f;
                highY = (teaseUp ? gaugeSpot.cur == 1 ? gr.yMin : promptSpot.Chosen.yMin : win.yMax - 5f * k) - 4f;
                left = win.xMin + 6f * k;
                right = win.xMax - 6f * k;
            }
            float cx = big ? scr.center.x : win.center.x;
            float lx = left + size.x * 0.5f, rx = right - size.x * 0.5f;
            float by = lowY + size.y * 0.5f, ty = highY - size.y * 0.5f;
            captionSpot.Clear();
            captionSpot.Add(Box(cx, by, size), CaptionPrefs[0]);
            captionSpot.Add(Box(cx, ty, size), CaptionPrefs[1]);
            captionSpot.Add(Box(lx, by, size), CaptionPrefs[2]);
            captionSpot.Add(Box(rx, by, size), CaptionPrefs[3]);
            captionSpot.Add(Box(lx, ty, size), CaptionPrefs[4]);
            captionSpot.Add(Box(rx, ty, size), CaptionPrefs[5]);
            Choose(captionSpot, dt);
            caption.anchorMin = caption.anchorMax = new Vector2(0.5f, 0.5f);
            var at = captionSpot.Chosen.center;
            caption.anchoredPosition = new Vector2(Mathf.Round(at.x), Mathf.Round(at.y));
        }

        /// <summary>The lure and its halo (10 render-target pixels around it; the top view's frog and its spray), root-local.</summary>
        Rect LureRect(float k) => LocalRect(view.LureBox);

        // ------------------------------------------------------------------ for the autopilot's check
        /// <summary>The legend's face this frame (root-local canvas units, no margin), if it shows.</summary>
        public bool FaceNow(out Rect r)
        {
            r = face;
            return hasFace;
        }

        /// <summary>The top view's lure this frame (root-local canvas units), if it shows.</summary>
        public bool FrogNow(out Rect r)
        {
            r = frog;
            return hasFrog;
        }

        public bool CaptionShown => captionGroup.alpha > 0.01f && !string.IsNullOrEmpty(captionText.text);
        public string CaptionNow => captionText.text;
        /// <summary>Counts the captions shown (a new one, even with the same text).</summary>
        public int CaptionSerial { get; private set; }
        public string CaptionSpotName => captionSpot.cur >= 0 ? CaptionSpots[captionSpot.cur] : "-";
        /// <summary>The caption's plate as drawn now (root-local).</summary>
        public Rect CaptionRect => Drawn(caption);
        public bool PromptShown => promptGroup.alpha > 0.01f;
        public Rect PromptRect => Drawn(prompt);
        public bool GaugeShown => gaugeGroup.alpha > 0.01f;
        public Rect GaugeRect => Drawn(gaugePlate);
        public bool CardShown => cardGroup.alpha > 0.01f;
        public Rect CardRect => Drawn(card);
    }
}
