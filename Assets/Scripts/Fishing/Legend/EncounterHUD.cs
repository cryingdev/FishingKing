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
    /// to the top band (under the prompt / the screen's upper band), else slide to a side, and only when every spot in the
    /// window is on the face or the frog (a narrow window: the frog across every band) go just outside its frame, below or
    /// above it; the prompt, the gauge and the name card step aside the same way. Every overlay also keeps clear of the
    /// ones placed before it, the lure and the reel. While a caption pops in, a spot is also judged by all it will reach
    /// (its punch's peak, growing from the band's edge) against the face and the frog themselves, so not one frame of the
    /// pop-in covers them. A spot is left at once when the face reaches it, but only taken back (the preferred one) when
    /// clear by an extra margin for a while, so nothing jumps back and forth.</para>
    /// <para>The gauge has eight spots (<see cref="GaugeSpots"/>): inside the window's bottom edge (centred or slid to
    /// a side), under the prompt (the same three), and as a last resort just outside the window's frame, below it or above
    /// it, so one is always clear of the face and of the top view's frog. Each is kept inside the screen's safe area. It
    /// takes the cheapest (the least overlap); a relocation fades it out where it was and in at the new spot, or, when
    /// something reaches where it is, moves it at once and fades it in there (it never slides across the frog).</para>
    /// </summary>
    [DefaultExecutionOrder(1100)] // after the view has placed the window
    public class EncounterHUD : MonoBehaviour
    {
        static readonly Color[] MoodCol = { new Color32(0x7a, 0x8a, 0xa8, 0xff), new Color32(0x6a, 0xff, 0xea, 0xff), new Color32(0xff, 0xc8, 0x30, 0xff) };
        static readonly string[] MoodIcon = { "mood_wary", "mood_curious", "mood_excited" };
        // by Verb: Wind, FlickPause, Hold, RunPause (a wind run, then a pause: the wind's ring round the hold's bars)
        static readonly string[] VerbIcon = { "verb_wind", "verb_flick", "verb_hold", "verb_runpause" };
        const float Px = UIKit.Px;               // canvas units per pixel-view pixel
        const float BarW = 180f * Px, BarH = 7f * Px;
        // around the face this much stays clear (canvas units); a spot the overlay is not on must be clear by Hyst more
        const float FacePad = 10f, Hyst = 14f;
        // the caption: its band in the window (0-5; from the lunge on the screen's), else just outside the window's frame
        // (6-7, the window's captions only)
        static readonly string[] CaptionSpots = { "bottom", "top", "bottom-left", "bottom-right", "top-left", "top-right", "below", "above" };
        // (outside: a last resort, only when every spot in the window is on the face or the frog, 1000 and up)
        static readonly float[] CaptionPrefs = { 0f, 4f, 6f, 6f, 8f, 8f, 900f, 901f };
        const float CaptionPop = 0.1f, CaptionPopBig = 0.18f;   // the caption's pop-in (Tween.Punch amount; large ones)
        // the gauge: the window's bottom band (0-2), the band under the prompt (3-5), just outside the window's frame (6-7)
        static readonly string[] GaugeSpots = { "bottom", "bottom-left", "bottom-right", "top", "top-left", "top-right", "below", "above" };
        static readonly float[] GaugePrefs = { 0f, 2f, 2f, 6f, 7f, 7f, 10f, 11f };
        const float SafePad = 4f;                       // canvas units kept inside the screen's safe area
        const float GaugeOut = 0.1f, GaugeIn = 0.16f;   // s: a relocation's fade out (where it was) and in (at the new spot)

        public static EncounterHUD Current { get; private set; }

        LegendEncounter enc;
        EncounterView view;
        RectTransform root, prompt, gauge, gaugePlate, caption, card;
        Image promptPlate, verbIcon, moodIcon, barBg, fill, cardPlate, captionPlate;
        Text promptText, moodText, captionSmall, captionBig, cardName, cardSub;
        Text captionText => big ? captionBig : captionSmall;
        CanvasGroup promptGroup, gaugeGroup, captionGroup, cardGroup;
        float captionT, captionLen, redT = -1f, goldT = -1f, cardT = -1f, cardShow, cardWait;
        float captionPopT = 99f;                   // since the caption's pop-in began (unscaled s, like the punch)
        bool cardClear = true;
        bool big, named, revealed;

        /// <summary>One overlay's place among a few candidate spots, kept with hysteresis.</summary>
        class Spot
        {
            public readonly float hold;           // how long a spot is kept before a clearly better one is taken
            // steady: a better spot is taken only once it has been better (by gain) for hold s on end, not merely clear
            // at the moment the hold runs out
            public readonly bool steady;
            public readonly float gain;
            public int cur = -1;
            public float held, better;
            public readonly List<Rect> rects = new List<Rect>();
            public readonly List<float> prefs = new List<float>();
            // (the caption's: all a spot takes while it pops in, kept off the face and the frog themselves)
            public readonly List<Rect> reach = new List<Rect>();

            public Spot(float hold, bool steady = false)
            {
                this.hold = hold;
                this.steady = steady;
                // (the gauge: 8, more than any two spots in the window differ by, so a clear spot is kept)
                gain = steady ? 8f : 1f;
            }

            public void Clear()
            {
                rects.Clear();
                prefs.Clear();
                reach.Clear();
            }

            public void Add(Rect r, float pref)
            {
                rects.Add(r);
                prefs.Add(pref);
            }

            public void Add(Rect r, float pref, Rect reaches)
            {
                Add(r, pref);
                reach.Add(reaches);
            }

            public Rect Chosen => rects[Mathf.Clamp(cur, 0, rects.Count - 1)];
        }

        readonly Spot promptSpot = new Spot(0.8f), gaugeSpot = new Spot(1.0f, true), cardSpot = new Spot(0.8f), captionSpot = new Spot(0.5f);
        readonly List<Rect> blocks = new List<Rect>();
        bool hasFace, hasFrog;
        Rect face;                                // the face this frame (root-local canvas units), no margin
        Rect frog;                                // the top view's lure: kept clear like the face
        // the gauge as drawn: its spot and rect (they lag the chosen spot through a relocation's fade-out), the tease's
        // alpha and the relocation's fade (the group's alpha is their product)
        int gaugeDrawn = -1;
        Rect gaugeAt;
        float gaugeShow, gaugeK = 1f;
        bool gaugeSeen;                           // the gauge has shown at its drawn spot (only then does leaving it count)
        Rect? faceClip;                           // while the gauge is placed: the window (the face's margin stops there)

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
            captionPopT = 0f;
            Tween.Punch(caption, big ? CaptionPopBig : CaptionPop);
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
                if (faceClip is Rect w) f = Rect.MinMaxRect(Mathf.Max(f.xMin, w.xMin), Mathf.Max(f.yMin, w.yMin), Mathf.Min(f.xMax, w.xMax), Mathf.Min(f.yMax, w.yMax));
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
        /// A spot's <see cref="Cost"/>, and for the caption also what it reaches while it pops in: 1000 and up on the face
        /// or the frog themselves (a unit round them, for the drawn position's rounding).
        /// </summary>
        float Cost(Spot s, int i, bool current)
        {
            float c = Cost(s.rects[i], current);
            if (i >= s.reach.Count) return c;
            var r = s.reach[i];
            if (hasFace)
            {
                float a = Area(r, Grow(face, 1f));
                if (a > 0f) c += 1000f + a / 100f;
            }
            if (hasFrog)
            {
                float a = Area(r, Grow(frog, 1f));
                if (a > 0f) c += 1000f + a / 100f;
            }
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
                    float c = s.prefs[i] + Cost(s, i, true);
                    if (c < best)
                    {
                        best = c;
                        s.cur = i;
                    }
                }
                s.held = s.better = 0f;
                return;
            }
            float hit = Cost(s, s.cur, true), cc = s.prefs[s.cur] + hit;
            int bi = s.cur;
            float bc = cc;
            for (int i = 0; i < n; i++)
            {
                if (i == s.cur) continue;
                float c = s.prefs[i] + Cost(s, i, false);
                if (c < bc)
                {
                    bc = c;
                    bi = i;
                }
            }
            bool gain = bi != s.cur && bc + s.gain < cc;
            s.better = gain ? s.better + dt : 0f;
            if (bi != s.cur && (hit >= 100f || (s.held >= s.hold && gain && (!s.steady || s.better >= s.hold))))
            {
                s.cur = bi;
                s.held = s.better = 0f;
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
            gaugeShow = Mathf.MoveTowards(gaugeShow, windowUp && tease ? 1f : 0f, dt * 5f);
            // (from above, the lunge is a cut to the underwater view: they go with it)
            if (view.TopFlow && ph >= LegendEncounter.Phase.Lunge) promptGroup.alpha = gaugeShow = 0f;
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
            captionPopT += Time.unscaledDeltaTime;
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
            var safe = SafeRect();
            float frame = 4f * k + 4f;   // the window's frame and its corner studs reach about this far out
            blocks.Clear();
            bool gaugeLow, gaugeHigh;
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

                // the gauge (its plate reaches 6 / 4 units past it): inside the bottom edge or under the prompt (its band:
                // the top edge when the prompt has dropped), centred or slid to a side; else just outside the window's
                // frame, below or above it. All kept inside the safe area.
                var gs = gauge.sizeDelta + new Vector2(12f, 8f);
                float gx = win.center.x + 10f * k - gs.x * 0.5f, gxl = win.xMin + 6f * k, gxr = win.xMax - 6f * k - gs.x;
                float gyLow = win.yMin + 5f * k - 4f, gyHigh = (promptSpot.cur <= 2 ? pr.yMin - 4f : win.yMax - 4f * k) - gs.y;
                gaugeSpot.Clear();
                for (int i = 0; i < 6; i++)
                    gaugeSpot.Add(Inside(new Rect(i % 3 == 0 ? gx : i % 3 == 1 ? gxl : gxr, i < 3 ? gyLow : gyHigh, gs.x, gs.y), safe), GaugePrefs[i]);
                // (outside the window: off the reel, and only where it fits; one the safe area pushes back over the window
                // is all but ruled out)
                var reelR = ReelRect(scr);
                for (int i = 6; i < 8; i++)
                {
                    var r0 = new Rect(win.center.x - gs.x * 0.5f, i == 6 ? win.yMin - frame - gs.y : win.yMax + frame, gs.x, gs.y);
                    var r = Inside(r0, safe);
                    gaugeSpot.Add(r, GaugePrefs[i] + (r.Overlaps(reelR) ? 100f : 0f) + ((r.position - r0.position).sqrMagnitude > 4f ? 500f : 0f));
                }
                // (in the side view the lure is kept clear too; from above it is the frog, kept clear like the face. The
                // face's margin stops at the window's frame: past it the gauge covers nothing of it)
                int before = blocks.Count;
                if (view.LureShown && !hasFrog) blocks.Add(LureRect(k));
                faceClip = win;
                // (hidden, it simply takes the best spot: it shows up there, no move)
                if (gaugeShow <= 0.01f) gaugeSpot.cur = -1;
                Choose(gaugeSpot, dt);
                var gr = PlaceGauge(dt, teaseUp);
                faceClip = null;
                blocks.RemoveRange(before, blocks.Count - before);
                gaugeLow = gaugeDrawn <= 2;
                gaugeHigh = gaugeDrawn >= 3 && gaugeDrawn <= 5;
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
            // lunge on the screen's lower third / upper band; clear of the lure and the reel too. Only when every spot in
            // the window is on the face or the frog (a narrow window: the frog across every band) it goes just outside
            // the window's frame, below it or above it (stacked past the gauge when that is there), like the gauge.
            if (view.LureShown) blocks.Add(LureRect(k));
            blocks.Add(ReelRect(scr));
            // (drawn at its size now, growing from its band's edge as it pops in, and kept off the overlays and the
            // face's margin as drawn; while it pops in a spot is also judged by all it will reach, its punch's peak, so
            // not one frame of the pop-in covers the face or the frog)
            var size = caption.sizeDelta * Mathf.Max(1f, caption.localScale.x);
            var peak = captionPopT < Tween.PunchTime
                ? caption.sizeDelta * Mathf.Max(Tween.PunchPeak(big ? CaptionPopBig : CaptionPop), caption.localScale.x)
                : size;
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
                var gr = gaugeAt;
                lowY = (teaseUp && gaugeLow ? gr.yMax : win.yMin + 5f * k) + 4f;
                highY = (teaseUp ? gaugeHigh ? gr.yMin : promptSpot.Chosen.yMin : win.yMax - 5f * k) - 4f;
                left = win.xMin + 6f * k;
                right = win.xMax - 6f * k;
            }
            float cx = big ? scr.center.x : win.center.x;
            // just outside the window's frame, or past the gauge when it is drawn there during the tease
            float belowTop = teaseUp && gaugeDrawn == 6 ? gaugeAt.yMin - 4f : win.yMin - frame;
            float aboveBottom = teaseUp && gaugeDrawn == 7 ? gaugeAt.yMax + 4f : win.yMax + frame;
            int spots = big ? 6 : CaptionSpots.Length;
            captionSpot.Clear();
            for (int i = 0; i < spots; i++)
            {
                Rect r = CaptionBox(i, size), reach = CaptionBox(i, peak);
                float pref = CaptionPrefs[i];
                // (outside the window: inside the safe area, and only where it fits; one the safe area pushes back
                // towards the window is all but ruled out)
                if (i >= 6)
                {
                    var r1 = Inside(r, safe);
                    if ((r1.position - r.position).sqrMagnitude > 4f) pref += 500f;
                    reach.position += r1.position - r.position;
                    r = r1;
                }
                captionSpot.Add(r, pref, reach);
            }
            Choose(captionSpot, dt);
            caption.anchorMin = caption.anchorMax = new Vector2(0.5f, 0.5f);
            var at = captionSpot.Chosen.center;
            caption.anchoredPosition = new Vector2(Mathf.Round(at.x), Mathf.Round(at.y));

            // a spot's rect for a caption of this size: anchored at its band's edge (the bottom band's bottom, the top
            // band's top, a side's side; below the window its top, above it its bottom), so it grows away from there
            Rect CaptionBox(int i, Vector2 sz)
            {
                float lx = left + sz.x * 0.5f, rx = right - sz.x * 0.5f;
                float by = lowY + sz.y * 0.5f, ty = highY - sz.y * 0.5f;
                switch (i)
                {
                    case 0: return Box(cx, by, sz);
                    case 1: return Box(cx, ty, sz);
                    case 2: return Box(lx, by, sz);
                    case 3: return Box(rx, by, sz);
                    case 4: return Box(lx, ty, sz);
                    case 5: return Box(rx, ty, sz);
                    case 6: return Box(cx, belowTop - sz.y * 0.5f, sz);
                    default: return Box(cx, aboveBottom + sz.y * 0.5f, sz);
                }
            }
        }

        /// <summary>The lure and its halo (10 render-target pixels around it; the top view's frog and its spray), root-local.</summary>
        Rect LureRect(float k) => LocalRect(view.LureBox);

        /// <summary>The reel and its label (root-local).</summary>
        static Rect ReelRect(Rect scr)
        {
            var reel = ReelGrip.Zone;
            return new Rect(scr.xMax + reel.xMin, scr.yMin + reel.yMin, reel.width, reel.height);
        }

        /// <summary>
        /// Draws the gauge at its spot and returns the rect it takes. It follows the spot; a relocation fades it out where
        /// it was (<see cref="GaugeOut"/>), then in at the new spot (<see cref="GaugeIn"/>), unless the face, the frog or
        /// an overlay has reached where it is: then it goes at once and fades in there. Hidden, it simply goes. Once the
        /// tease is over (<paramref name="up"/> false: the lunge growing the window, the turn-away) it fades out where it
        /// is, cut at once if the face or the lure reaches it: never relocated.
        /// </summary>
        Rect PlaceGauge(float dt, bool up)
        {
            int want = gaugeSpot.cur;
            var to = gaugeSpot.Chosen;
            if (gaugeDrawn < 0 || gaugeShow <= 0.01f)
            {
                gaugeDrawn = want;
                gaugeAt = to;
                gaugeK = 1f;
                gaugeSeen = false;
            }
            else if (!up)
            {
                if (Cost(gaugeAt, true) >= 100f) gaugeK = 0f;
            }
            else if (want != gaugeDrawn || (to.center - gaugeAt.center).sqrMagnitude > 64f)
            {
                bool reached = Cost(gaugeAt, true) >= 100f;
                if (gaugeK <= 0f || reached)
                {
                    GaugeMovedAtOnce = reached && gaugeK > 0f;
                    gaugeDrawn = want;
                    gaugeAt = to;
                    gaugeK = 0f;
                    // (only a move from where it has shown counts)
                    if (gaugeSeen) GaugeMoves++;
                    gaugeSeen = false;
                }
                else gaugeK = Mathf.MoveTowards(gaugeK, 0f, dt / GaugeOut);
            }
            else
            {
                gaugeAt = to;
                gaugeK = Mathf.MoveTowards(gaugeK, 1f, dt / GaugeIn);
                if (gaugeShow * gaugeK > 0.01f) gaugeSeen = true;
            }
            gauge.anchoredPosition = new Vector2(Mathf.Round(gaugeAt.center.x), Mathf.Round(gaugeAt.yMin + 4f));
            gaugeGroup.alpha = gaugeShow * gaugeK;
            return gaugeAt;
        }

        /// <summary>The screen's safe area (root-local), <see cref="SafePad"/> in.</summary>
        Rect SafeRect() => Grow(SafeArea, -SafePad);

        /// <summary>The screen's safe area (root-local).</summary>
        public Rect SafeArea
        {
            get
            {
                var sa = Screen.safeArea;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sa.min, null, out var a);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sa.max, null, out var b);
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
        }

        /// <summary>The rect moved (not resized) inside the area; centred on an axis it does not fit.</summary>
        static Rect Inside(Rect r, Rect area)
        {
            float x = r.width >= area.width ? area.center.x - r.width * 0.5f : Mathf.Clamp(r.x, area.xMin, area.xMax - r.width);
            float y = r.height >= area.height ? area.center.y - r.height * 0.5f : Mathf.Clamp(r.y, area.yMin, area.yMax - r.height);
            return new Rect(x, y, r.width, r.height);
        }

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
        /// <summary>The verb icon a mood's prompt shows (Resources/Sprites/UI).</summary>
        public static string VerbIconOf(Verb v) => VerbIcon[(int)v];
        /// <summary>The prompt's verb icon as shown now (its sprite's name; null = none).</summary>
        public string VerbIconShown => verbIcon.enabled && verbIcon.sprite != null ? verbIcon.sprite.name : null;
        /// <summary>The verb icon's screen rectangle (pixels, the overlay canvas: for a zoomed capture).</summary>
        public Rect VerbIconScreen
        {
            get
            {
                var c = new Vector3[4];
                verbIcon.rectTransform.GetWorldCorners(c);
                return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
            }
        }
        public bool GaugeShown => gaugeGroup.alpha > 0.01f;
        public Rect GaugeRect => Drawn(gaugePlate);
        /// <summary>Where the gauge is drawn (<see cref="GaugeSpots"/>).</summary>
        public string GaugeSpotName => gaugeDrawn >= 0 ? GaugeSpots[gaugeDrawn] : "-";
        /// <summary>How often the gauge has moved from a spot where it had shown, during the tease (a spot re-placed
        /// within counts too; the fade-out after the tease never moves it).</summary>
        public int GaugeMoves { get; private set; }
        /// <summary>The last move went at once (something reached the gauge), not after a fade-out where it was.</summary>
        public bool GaugeMovedAtOnce { get; private set; }
        /// <summary>The gauge's alpha now (the tease's fade times a relocation's).</summary>
        public float GaugeAlpha => gaugeGroup.alpha;

        /// <summary>For the log: each gauge spot this frame and what it would cover (the frog, the face, or nothing).</summary>
        public string GaugeSpotsNow()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < gaugeSpot.rects.Count; i++)
            {
                var r = gaugeSpot.rects[i];
                string on = hasFrog && Area(r, frog) > 0f ? "frog" : hasFace && Area(r, face) > 0f ? "face" : "clear";
                sb.Append(i > 0 ? " " : "").Append(GaugeSpots[i]).Append('=').Append(on);
            }
            return sb.ToString();
        }
        public bool CardShown => cardGroup.alpha > 0.01f;
        public Rect CardRect => Drawn(card);
    }
}
