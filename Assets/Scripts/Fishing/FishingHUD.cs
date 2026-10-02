using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishingKing
{
    public class FishingHUD : MonoBehaviour
    {
        public Canvas Canvas { get; private set; }

        /// <summary>-1..1: the walk buttons held right now (left / right).</summary>
        public float WalkHeld => (holdR != null && holdR.Held ? 1f : 0f) - (holdL != null && holdL.Held ? 1f : 0f);

        FishingController ctl;
        RectTransform root, fightPanel, tacklePanel, aimPanel, depthGroup, lureGroup;
        // the lure slot of the tackle panel: [action chip] ●●●●○ 수심 3.2m, and the feedback word above the panel
        Image lureChip;
        Text lureAction, lureDepth, lureWord;
        readonly Image[] pips = new Image[5];
        CanvasGroup lureWordGroup;
        float lureWordT;
        const float LureWordTime = 1.2f;
        static readonly Color PipOff = new Color(0.1f, 0.08f, 0.12f, 0.75f), PipOn = new Color32(0xfb, 0xf4, 0xe2, 0xff);
        Image tension, stamina, baitIcon, dragMark, dangerZone, hintPlate;
        ReelGrip reel;
        Button walkL, walkR;
        HoldButton holdL, holdR;
        const float TackleWide = 252, TackleNarrow = 92;
        // walk buttons, a fixed pair in the bottom-right corner (canvas units): size, gap between them, inset from the
        // right edge (as the bait button's from the left) and their bottom, centring them on the bait button's height.
        // The feet stay within ~240 units of the centre (1 m at the feet is ~92 units at every aspect) and the canvas is
        // at least 960 wide, so the pair (from 160 units in from the right edge) never covers him or the props at his feet
        const float WalkBtn = 64, WalkGap = 12, WalkInset = 20, WalkBottom = 26;
        Text hint, flash, tensionLabel, fishName, distance, phaseLabel, baitCount, depthText, aimText, depthLabel;
        /// <summary>At the reel's place while it is hidden (ready): the spool and the reel (no line: what to do).</summary>
        Text spotText;
        /// <summary>Over the float: the line out while it changes.</summary>
        Text floatText;
        CanvasGroup floatGroup;
        float floatLast = -1f, floatIdle;
        RectTransform stageName;
        TopBar topBar;
        CanvasGroup hintGroup;
        Button settingsBtn;
        Button retrieveBtn, backBtn;
        CanvasGroup flashGroup;
        PixelOutline flashOutline;
        float flashTime;
        float shownTension;
        // the aim meter: the ice's depth while dragging; elsewhere a wind-up cue, then briefly the thrown flick's power
        CanvasGroup aimGroup;
        GameObject aimBarBg;
        float castFbT;
        const float CastFbTime = 1.3f, CastFbFade = 0.3f;
        // the legend encounter's own HUD (prompt, gauge, captions), while one is on
        EncounterHUD enc;
        // obstacles (Docs/obstacles_spec.md 10.2): the fight strip in snag mode (the icon and 밑걸림 / 수초 / 갈대 / 연잎 in
        // the name slot, the snag tension on the tension bar), the abrasion meter (쓸림) under the distance
        Image snagIcon, abrBg, abrFill;
        Text snagName, staminaLabel, abrLabel;
        GameObject staminaBar;
        static readonly Color AbrLow = new Color32(0xe8, 0xd8, 0xa0, 0xff), AbrMid = new Color32(0xff, 0x8a, 0x3a, 0xff);

        public static FishingHUD Create(FishingController c)
        {
            var hud = new GameObject("FishingHUD").AddComponent<FishingHUD>();
            hud.ctl = c;
            hud.Build();
            return hud;
        }

        void Build()
        {
            Canvas = UIKit.CreateCanvas("HUD", 10);
            root = (RectTransform)Canvas.transform;
            var st = ctl.Stage.Def;

            // top-left: back + stage name
            backBtn = UIKit.IconButton(root, Art.UI("icon_back"), "grey", OnBack, 56);
            backBtn.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(12, -10), new Vector2(56, 56));
            var namePanel = UIKit.Panel(root, "panel_dark", null, "StageName");
            stageName = namePanel.rectTransform;
            namePanel.rectTransform.At(new Vector2(0, 1), new Vector2(76, -12), new Vector2(250, 52));
            var nm = UIKit.Label(namePanel.transform, st.name, 21, UIKit.Cream, TextAnchor.UpperLeft);
            nm.rectTransform.Fill(14, 8, 5, 22);
            var stars = UIKit.Rect(namePanel.transform, "Stars").At(new Vector2(0, 0), new Vector2(14, 6), new Vector2(120, 16), new Vector2(0, 0));
            for (int i = 0; i < 5; i++)
            {
                var s = UIKit.Img(stars, Art.UI(i < st.difficulty ? "star" : "star_empty"), new Vector2(16, 16));
                s.rectTransform.At(new Vector2(0, 0.5f), new Vector2(i * 18, 0), new Vector2(16, 16), new Vector2(0, 0.5f));
            }

            topBar = TopBar.Create(root);
            settingsBtn = SettingsUI.CornerButton(root);
            settingsBtn.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(334, -10), new Vector2(96, 56));
            BuildClock();

            // bottom-left: tackle (bait + float depth)
            tacklePanel = UIKit.Panel(root, "panel_dark", null, "Tackle").rectTransform;
            tacklePanel.At(new Vector2(0, 0), new Vector2(12, 12), new Vector2(TackleWide, 92), new Vector2(0, 0));
            var baitBtn = UIKit.Button(tacklePanel, null, "grey", OpenBaitPicker, new Vector2(76, 76));
            baitBtn.GetComponent<RectTransform>().At(new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(76, 76), new Vector2(0, 0.5f));
            baitIcon = UIKit.Img(baitBtn.transform, null, new Vector2(56, 56), "BaitIcon");
            baitIcon.rectTransform.anchoredPosition = new Vector2(0, 4);
            baitCount = UIKit.Label(baitBtn.transform, "", 16, UIKit.Cream, TextAnchor.LowerRight);
            baitCount.rectTransform.Fill(4, 6, 4, 8);
            depthGroup = UIKit.Rect(tacklePanel, "Depth").At(new Vector2(0, 0.5f), new Vector2(92, 0), new Vector2(152, 76), new Vector2(0, 0.5f));
            depthLabel = UIKit.Label(depthGroup, "찌 수심", 15, UIKit.Sky, TextAnchor.UpperCenter);
            depthLabel.rectTransform.Fill(0, 0, 3, 49);
            depthText = UIKit.Label(depthGroup, "2.0m", 22, UIKit.Cream);
            depthText.horizontalOverflow = HorizontalWrapMode.Overflow; // "18.0m" must never split into two lines
            depthText.rectTransform.Fill(34, 34, 36, 6);                // same band as the -/+ buttons
            var minus = UIKit.Button(depthGroup, "-", "blue", () => ChangeDepth(-0.5f), new Vector2(34, 34), 24);
            minus.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(0, 6), new Vector2(34, 34), new Vector2(0, 0));
            var plus = UIKit.Button(depthGroup, "+", "blue", () => ChangeDepth(0.5f), new Vector2(34, 34), 24);
            plus.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(0, 6), new Vector2(34, 34), new Vector2(1, 0));
            // lures: the action, how well it is worked (5 pips = Q, gold from 0.75, blinking while a fish may strike), the depth
            lureGroup = UIKit.Rect(tacklePanel, "Lure").At(new Vector2(0, 0.5f), new Vector2(92, 0), new Vector2(152, 76), new Vector2(0, 0.5f));
            lureChip = UIKit.Img(lureGroup, null, new Vector2(24, 24), "Chip");
            lureChip.rectTransform.At(new Vector2(0, 1), new Vector2(0, -4), new Vector2(24, 24), new Vector2(0, 1));
            lureAction = UIKit.Label(lureGroup, "", 16, UIKit.Cream, TextAnchor.MiddleLeft);
            lureAction.rectTransform.At(new Vector2(0, 1), new Vector2(30, -4), new Vector2(120, 24), new Vector2(0, 1));
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = UIKit.Img(lureGroup, Art.Circle, new Vector2(10, 10), "Pip" + i);
                pips[i].rectTransform.At(new Vector2(0, 0.5f), new Vector2(2 + i * 16, 0), new Vector2(10, 10), new Vector2(0, 0.5f));
                pips[i].color = PipOff;
            }
            lureDepth = UIKit.Label(lureGroup, "", 16, UIKit.Sky, TextAnchor.MiddleLeft);
            lureDepth.horizontalOverflow = HorizontalWrapMode.Overflow;
            lureDepth.rectTransform.At(new Vector2(0, 0), new Vector2(0, 4), new Vector2(150, 22), new Vector2(0, 0));
            var lw = UIKit.Rect(root, "LureWord").At(new Vector2(0, 0), new Vector2(12, 12 + 92 + 4), new Vector2(360, 26), new Vector2(0, 0));
            lureWord = UIKit.Label(lw, "", 16, UIKit.Gold, TextAnchor.MiddleLeft);
            lureWord.horizontalOverflow = HorizontalWrapMode.Overflow;
            lureWord.rectTransform.Fill();
            lureWordGroup = lw.gameObject.AddComponent<CanvasGroup>();
            lureWordGroup.alpha = 0f;
            lureWordGroup.blocksRaycasts = false;

            // bottom-right: retrieve + the reel, which moves under the finger while the player draws circles
            retrieveBtn = UIKit.Button(root, "회수", "blue", () => ctl.Retrieve(), new Vector2(96, 48), 20);
            retrieveBtn.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(-200, 16), new Vector2(96, 48), new Vector2(1, 0)); // clear of the reel's arrow orbit
            reel = ReelGrip.Create(root, ctl.Gesture);
            spotText = UIKit.Label(root, "", 16, UIKit.Cream);
            // (over the walk buttons, which take the reel's place while ready)
            spotText.rectTransform.At(new Vector2(1, 0), new Vector2(-110, WalkBottom + WalkBtn + 8f + 23f), new Vector2(200, 46), new Vector2(0.5f, 0.5f));
            floatText = UIKit.Label(root, "", 16, UIKit.Cream, TextAnchor.LowerCenter, true, "FloatLine");
            floatText.horizontalOverflow = HorizontalWrapMode.Overflow;
            floatText.rectTransform.anchorMin = floatText.rectTransform.anchorMax = root.pivot;
            floatText.rectTransform.pivot = new Vector2(0.5f, 0f);
            floatText.rectTransform.sizeDelta = new Vector2(120, 22);
            floatGroup = floatText.gameObject.AddComponent<CanvasGroup>();
            floatGroup.alpha = 0f;
            floatGroup.blocksRaycasts = false;

            // bottom-right: walk left / right (hold); only while ready, when the reel and retrieve button are hidden
            walkL = WalkButton("◀", "WalkLeft", -(WalkInset + WalkBtn + WalkGap), out holdL);
            walkR = WalkButton("▶", "WalkRight", -WalkInset, out holdR);

            // hint
            hint = UIKit.PlateLabel(root, "", 20, UIKit.Cream, out hintPlate);
            hintPlate.rectTransform.anchorMin = hintPlate.rectTransform.anchorMax = new Vector2(0.5f, 1);
            hintPlate.rectTransform.pivot = new Vector2(0.5f, 1);
            hintPlate.rectTransform.anchoredPosition = new Vector2(0, -78);
            hintGroup = hintPlate.gameObject.AddComponent<CanvasGroup>(); // gives way to a flash message in the same spot
            hintGroup.blocksRaycasts = false;

            // aim meter
            aimPanel = UIKit.Rect(root, "Aim").At(new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(300, 60), new Vector2(0.5f, 1));
            aimText = UIKit.Label(aimPanel, "", 20, UIKit.Gold);
            aimText.rectTransform.At(new Vector2(0.5f, 1), Vector2.zero, new Vector2(300, 26), new Vector2(0.5f, 1));
            var aimBar = UIKit.Bar(aimPanel, new Vector2(260, 20), UIKit.Gold, "Power");
            aimBar.transform.parent.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), Vector2.zero, new Vector2(260, 20), new Vector2(0.5f, 0));
            aimPanel.gameObject.AddComponent<AimBarRef>().fill = aimBar;
            aimBarBg = aimBar.transform.parent.gameObject;
            aimGroup = aimPanel.gameObject.AddComponent<CanvasGroup>(); // shown over the water after a throw: never in the way of a tap
            aimGroup.blocksRaycasts = false;
            aimGroup.interactable = false;

            // fight strip along the top edge: the stage name, top bar and back button step aside while fighting,
            // so the strip only covers the sky and the far water (beyond ~200 m) and every jump stays in view
            fightPanel = UIKit.Panel(root, "panel_dark", new Color(1, 1, 1, 0.92f), "Fight").rectTransform;
            fightPanel.At(new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(680, 70), new Vector2(0.5f, 1));
            fishName = UIKit.Label(fightPanel, "", 20, UIKit.Cream, TextAnchor.UpperLeft);
            fishName.rectTransform.Fill(14, 470, 6, 38);
            distance = UIKit.Label(fightPanel, "", 20, UIKit.Sky, TextAnchor.UpperRight);
            distance.rectTransform.Fill(540, 14, 6, 38);
            tensionLabel = UIKit.Label(fightPanel, "장력", 15, UIKit.Cream, TextAnchor.MiddleLeft);
            tensionLabel.rectTransform.At(new Vector2(0, 0), new Vector2(14, 8), new Vector2(40, 24), new Vector2(0, 0));
            var tbBg = UIKit.Panel(fightPanel, "bar_bg", null, "TensionBg");
            tbBg.rectTransform.At(new Vector2(0, 0), new Vector2(54, 8), new Vector2(292, 24), new Vector2(0, 0));
            dangerZone = UIKit.Solid(tbBg.transform, new Color(0.9f, 0.2f, 0.15f, 0.35f), "Danger");
            dangerZone.rectTransform.anchorMin = new Vector2(0.85f, 0);
            dangerZone.rectTransform.anchorMax = new Vector2(1, 1);
            dangerZone.rectTransform.offsetMin = new Vector2(0, 3);
            dangerZone.rectTransform.offsetMax = new Vector2(-3, -3);
            tension = UIKit.Panel(tbBg.transform, "bar_fill", UIKit.Good, "Fill");
            tension.rectTransform.anchorMin = Vector2.zero;
            tension.rectTransform.anchorMax = Vector2.one;
            tension.rectTransform.offsetMin = new Vector2(2, 2);
            tension.rectTransform.offsetMax = new Vector2(-2, -2);
            dragMark = UIKit.Solid(tbBg.transform, new Color(1f, 1f, 1f, 0.9f), "Drag");
            dragMark.rectTransform.sizeDelta = new Vector2(3, 24);
            BuildSideStrip();
            // (the stamina row sits 3 units lower than before the abrasion meter came in over it)
            var sl = UIKit.Label(fightPanel, "체력", 15, UIKit.Cream, TextAnchor.MiddleLeft);
            sl.rectTransform.At(new Vector2(0, 0), new Vector2(470, 5), new Vector2(40, 24), new Vector2(0, 0));
            staminaLabel = sl;
            stamina = UIKit.Bar(fightPanel, new Vector2(156, 14), new Color32(0xff, 0x8a, 0x4a, 0xff), "Stamina");
            stamina.transform.parent.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(510, 10), new Vector2(156, 14), new Vector2(0, 0));
            staminaBar = stamina.transform.parent.gameObject;
            // the abrasion meter: 쓸림 and its bar (fk_items.py obstacles: abr_bar_bg 78x5, abr_bar_fill 76x3, drawn 2x),
            // under the distance, over the stamina bar; hidden until the line first rubs
            abrLabel = UIKit.Label(fightPanel, "쓸림", 15, UIKit.Cream, TextAnchor.MiddleLeft);
            abrLabel.verticalOverflow = VerticalWrapMode.Overflow;
            abrLabel.rectTransform.At(new Vector2(0, 0), new Vector2(470, 27), new Vector2(40, 12), new Vector2(0, 0));
            abrBg = UIKit.Img(fightPanel, Art.UI("abr_bar_bg"), new Vector2(156, 10), "Abrasion");
            abrBg.rectTransform.At(new Vector2(0, 0), new Vector2(510, 28), new Vector2(156, 10), new Vector2(0, 0));
            abrFill = UIKit.Img(abrBg.transform, Art.UI("abr_bar_fill"), new Vector2(152, 6), "Fill");
            abrFill.rectTransform.At(new Vector2(0, 0), new Vector2(2, 2), new Vector2(152, 6), new Vector2(0, 0));
            abrFill.type = Image.Type.Filled;
            abrFill.fillMethod = Image.FillMethod.Horizontal;
            abrFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            ShowAbrasion(false);
            // snag mode: the icon and the word in the fish name's slot
            snagIcon = UIKit.Img(fightPanel, Art.UI("icon_snag"), new Vector2(32, 32), "SnagIcon");
            snagIcon.rectTransform.At(new Vector2(0, 1), new Vector2(12, -6), new Vector2(32, 32), new Vector2(0, 1));
            snagName = UIKit.Label(fightPanel, "밑걸림", 20, UIKit.Bad, TextAnchor.UpperLeft);
            snagName.rectTransform.Fill(50, 470, 6, 38);
            snagIcon.enabled = snagName.enabled = false;
            phaseLabel = UIKit.Label(fightPanel, "", 17, UIKit.Gold, TextAnchor.UpperCenter);
            phaseLabel.rectTransform.Fill(190, 190, 8, 38);
            phaseLabel.horizontalOverflow = HorizontalWrapMode.Overflow; // one line, centred between name and distance

            // flash message: top centre, under the fight strip or in place of the hint (which fades out meanwhile),
            // i.e. over the sky and the far water, never over the float or a fighting fish
            var fl = UIKit.Rect(root, "Flash").At(new Vector2(0.5f, 1), new Vector2(0, -82), new Vector2(760, 50), new Vector2(0.5f, 1));
            flash = UIKit.Label(fl, "", 28, UIKit.Gold);
            flash.rectTransform.Fill();
            flashGroup = fl.gameObject.AddComponent<CanvasGroup>();
            flashGroup.alpha = 0;
            flashGroup.blocksRaycasts = false;

            RefreshTackle();
        }

        class AimBarRef : MonoBehaviour { public Image fill; }

        // ------------------------------------------------------------------ the clock panel (Docs/time_currents_spec.md 11.1)
        // in the top row between the settings button and the top bar: the period icon and "새벽 05:40"; under it by stage
        // the current (arrow, 3 pips, 물살: stream / ocean), the tide (icon, word, level bar, arrow: sea) or a gust (arrow,
        // 바람: lake / swamp)
        RectTransform clockPanel;
        Image clockIcon, rowArrow, tideIcon, tideFill;
        Text clockLabel, rowWord;
        readonly Image[] curPips = new Image[3];
        GameObject tideBar;
        int clockShown = -1;
        // what the row shows (set again only when it changes: no sprite names built and no canvas rebuilt every frame)
        int pipsShown = -1, arrowShown = -1, tideShown = -1;
        static readonly string[] ArrowNames = Names("cur_arrow_"), ArrowNamesS = Names("cur_arrow_s_");
        static string[] Names(string prefix)
        {
            var n = new string[8];
            for (int i = 0; i < 8; i++) n[i] = prefix + i;
            return n;
        }
        static readonly Color CurCol = new Color32(0xa8, 0xe0, 0xff, 0xff), WindCol = new Color32(0xd8, 0xe8, 0xf0, 0xff);

        void BuildClock()
        {
            var p = UIKit.Panel(root, "panel_dark", null, "Clock");
            clockPanel = p.rectTransform;
            clockPanel.At(new Vector2(0, 1), new Vector2(438, -12), new Vector2(140, 52));
            clockIcon = UIKit.Img(clockPanel, Art.UI("tod_day"), new Vector2(24, 24), "PeriodIcon");
            clockIcon.rectTransform.At(new Vector2(0, 1), new Vector2(8, -4), new Vector2(24, 24), new Vector2(0, 1));
            clockLabel = UIKit.Label(clockPanel, "", 20, UIKit.Cream, TextAnchor.MiddleLeft);
            clockLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            clockLabel.rectTransform.At(new Vector2(0, 1), new Vector2(36, -4), new Vector2(100, 24), new Vector2(0, 1));
            var kind = ctl.Stage.Current.K;
            bool sea = kind == CurrentField.Kind.Sea;
            // row 2: 20 units at the bottom
            rowArrow = UIKit.Img(clockPanel, Art.UI("cur_arrow_0"), sea ? new Vector2(14, 14) : new Vector2(18, 18), "CurArrow");
            rowArrow.rectTransform.At(new Vector2(0, 0), sea ? new Vector2(120, 7) : new Vector2(10, 5), sea ? new Vector2(14, 14) : new Vector2(18, 18), new Vector2(0, 0));
            rowArrow.enabled = false;
            if (sea)
            {
                tideIcon = UIKit.Img(clockPanel, Art.UI("tide_flood"), new Vector2(20, 20), "TideIcon");
                tideIcon.rectTransform.At(new Vector2(0, 0), new Vector2(6, 4), new Vector2(20, 20), new Vector2(0, 0));
                tideBar = UIKit.Img(clockPanel, Art.UI("tide_bar_bg"), new Vector2(56, 16), "TideBar").gameObject;
                tideBar.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(60, 6), new Vector2(56, 16), new Vector2(0, 0));
                tideFill = UIKit.Img(tideBar.transform, Art.UI("tide_bar_fill"), new Vector2(48, 8), "Fill");
                tideFill.rectTransform.At(new Vector2(0, 0), new Vector2(4, 4), new Vector2(48, 8), new Vector2(0, 0));
                tideFill.type = Image.Type.Filled;
                tideFill.fillMethod = Image.FillMethod.Horizontal;
                tideFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
            else
                for (int i = 0; i < curPips.Length; i++)
                {
                    curPips[i] = UIKit.Img(clockPanel, Art.UI("cur_pip_off"), new Vector2(6, 6), "Pip" + i);
                    curPips[i].rectTransform.At(new Vector2(0, 0), new Vector2(32 + 8 * i, 11), new Vector2(6, 6), new Vector2(0, 0));
                    curPips[i].gameObject.SetActive(kind == CurrentField.Kind.Stream || kind == CurrentField.Kind.Ocean);
                }
            rowWord = UIKit.Label(clockPanel, "", 15, CurCol, TextAnchor.MiddleLeft);
            rowWord.horizontalOverflow = HorizontalWrapMode.Overflow;
            rowWord.rectTransform.At(new Vector2(0, 0), sea ? new Vector2(28, 4) : new Vector2(kind == CurrentField.Kind.Lake || kind == CurrentField.Kind.Swamp ? 32 : 60, 4),
                new Vector2(40, 20), new Vector2(0, 0));
            UpdateClock();
        }

        /// <summary>The cur_arrow sprite index (k x 45 degrees counter-clockwise from screen right) of a current at a water point.</summary>
        int ArrowIndex(Vector3 at, Vector2 dir)
        {
            var P = ctl.Stage.P;
            var d = P.To2D(at + new Vector3(dir.x, 0f, dir.y)) - P.To2D(at);
            float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            return ((Mathf.RoundToInt(deg / 45f) % 8) + 8) % 8;
        }

        void UpdateClock()
        {
            float m = GameClock.Min;
            int shown = Mathf.FloorToInt(m) / 10;
            if (shown != clockShown)
            {
                clockShown = shown;
                var per = GameClock.PeriodAt(m);
                clockLabel.text = GameClock.Label(m);
                clockLabel.color = GameClock.TextColor(per);
                clockIcon.sprite = Art.UI("tod_" + GameClock.Id(per));
            }
            var cf = ctl.Stage.Current;
            if (cf == null) return;
            var tk = ctl.Tackle;
            var at = tk != null && tk.State == Tackle.Mode.Water ? tk.Surface : new Vector3(ctl.Angler.X, 0f, 15f);
            switch (cf.K)
            {
                case CurrentField.Kind.Stream:
                case CurrentField.Kind.Ocean:
                {
                    var w = cf.Water(at.x, at.z);
                    float sp = w.magnitude;
                    int pips = sp < 0.05f ? 0 : sp < 0.25f ? 1 : sp < 0.6f ? 2 : 3;
                    if (pips != pipsShown)
                    {
                        pipsShown = pips;
                        for (int i = 0; i < curPips.Length; i++) curPips[i].sprite = Art.UI(i < pips ? "cur_pip_on" : "cur_pip_off");
                    }
                    rowArrow.enabled = pips > 0;
                    if (rowArrow.enabled)
                    {
                        int ai = ArrowIndex(at, w);
                        if (ai != arrowShown) rowArrow.sprite = Art.UI(ArrowNames[arrowShown = ai]);
                        rowArrow.color = CurCol;
                    }
                    rowWord.text = "물살";
                    rowWord.color = CurCol;
                    break;
                }
                case CurrentField.Kind.Sea:
                {
                    var t = GameClock.Tide;
                    if ((int)t.Kind != tideShown)
                    {
                        tideShown = (int)t.Kind;
                        tideIcon.sprite = Art.UI(t.Kind switch { TideKind.Flood => "tide_flood", TideKind.Ebb => "tide_ebb", TideKind.High => "tide_high", _ => "tide_low" });
                    }
                    rowWord.text = t.Word;
                    rowWord.color = CurCol;
                    // (in 48 steps, the bar's width: the level changes a little every frame)
                    tideFill.fillAmount = Mathf.Round(Mathf.Clamp01((t.H + 1f) * 0.5f) * 48f) / 48f;
                    var w = cf.Water(at.x, at.z);
                    rowArrow.enabled = w.magnitude >= 0.05f;
                    if (rowArrow.enabled)
                    {
                        int ai = ArrowIndex(at, w);
                        if (ai != arrowShown) rowArrow.sprite = Art.UI(ArrowNamesS[arrowShown = ai]);
                        rowArrow.color = CurCol;
                    }
                    break;
                }
                case CurrentField.Kind.Lake:
                case CurrentField.Kind.Swamp:
                {
                    bool gust = cf.Gust01 > 0.2f;
                    rowArrow.enabled = gust;
                    rowWord.text = gust ? "바람" : "";
                    rowWord.color = WindCol;
                    if (gust)
                    {
                        int ai = ArrowIndex(at, cf.Wind.sqrMagnitude > 1e-8f ? cf.Wind : cf.GustDir);
                        if (ai != arrowShown) rowArrow.sprite = Art.UI(ArrowNames[arrowShown = ai]);
                        rowArrow.color = WindCol;
                    }
                    break;
                }
                default:
                    rowArrow.enabled = false;
                    rowWord.text = "";
                    break;
            }
        }

        // ------------------------------------------------------------------ side pressure strip (in the fight strip)
        // between the tension and stamina bars: the fish's run (an arrow and a fish on the side it runs to) and the rod (a
        // stick leaning / sliding with the rod's lean), gold when the rod leans against the run, orange when with it
        const float SideW = 108, SideH = 24, SideRodSlide = 30f, SideRodTilt = 35f;
        static readonly Color SideBadColor = new Color32(0xff, 0x8a, 0x4a, 0xff), SideDim = new Color(0.98f, 0.96f, 0.89f, 0.3f);
        Image sideBg, sideRod, sideFish, sideChevron;
        Text sideL, sideR;

        void BuildSideStrip()
        {
            sideBg = UIKit.Panel(fightPanel, "bar_bg", null, "Side");
            sideBg.rectTransform.At(new Vector2(0, 0), new Vector2(352, 8), new Vector2(SideW, SideH), new Vector2(0, 0));
            var tick = UIKit.Solid(sideBg.transform, new Color(0.98f, 0.96f, 0.89f, 0.35f), "Centre");
            tick.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 14));
            sideL = UIKit.Label(sideBg.transform, "◀", 15, SideDim);
            sideL.rectTransform.At(new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(18, 22), new Vector2(0, 0.5f));
            sideR = UIKit.Label(sideBg.transform, "▶", 15, SideDim);
            sideR.rectTransform.At(new Vector2(1, 0.5f), new Vector2(-3, 0), new Vector2(18, 22), new Vector2(1, 0.5f));
            sideFish = UIKit.Img(sideBg.transform, Art.UI("icon_fish"), new Vector2(18, 18), "Fish");
            sideFish.enabled = false;
            // the rod: a thin stick standing on the strip's floor, pivoting at its butt
            sideRod = UIKit.Solid(sideBg.transform, UIKit.Cream, "Rod");
            sideRod.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0, 3), new Vector2(3, 18), new Vector2(0.5f, 0f));
            // the current: a ">>" under the fish while its run goes with (or against) the flow (Docs/time_currents_spec.md 11.2)
            sideChevron = UIKit.Img(sideBg.transform, Art.UI("cur_chevron"), new Vector2(18, 14), "Current");
            sideChevron.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0, -2), new Vector2(18, 14), new Vector2(0.5f, 1f));
            sideChevron.enabled = false;
            // through an ice hole there is no side pressure
            sideBg.gameObject.SetActive(!ctl.Stage.L.IsIce);
        }

        /// <summary>The lean the fight strip's rod showed last (-1..1; for the tests: the controller's Lean, what the drawn rod shows and side pressure counts).</summary>
        internal float StripLean { get; private set; }

        /// <summary>The side-pressure strip and its word: the fish's run, the rod's lean, right / wrong.</summary>
        void UpdateSide()
        {
            // (the run's side only while side pressure counts: the fish really sweeping that way, or a run for cover)
            int run = ctl.SideActive ? ctl.FishRun : 0;
            float lean = ctl.Lean, side = ctl.SideNow, dead = FishingController.SideDead;
            StripLean = lean;
            bool blink = Mathf.Repeat(Time.unscaledTime * 3f, 1f) < 0.6f;
            sideL.color = run < 0 ? (blink ? SideBadColor : Color.Lerp(SideBadColor, SideDim, 0.5f)) : SideDim;
            sideR.color = run > 0 ? (blink ? SideBadColor : Color.Lerp(SideBadColor, SideDim, 0.5f)) : SideDim;
            sideFish.enabled = run != 0;
            if (run != 0)
            {
                sideFish.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(run * (SideW * 0.5f - 30f), 0), new Vector2(18, 18));
                sideFish.rectTransform.localScale = new Vector3(run, 1f, 1f);   // (the icon faces right: facing its run)
            }
            float al = ctl.RunAlign;
            int curRun = ctl.FishRun;   // (the current's chevron follows the run itself, sweeping or not)
            sideChevron.enabled = curRun != 0 && Mathf.Abs(al) >= 0.3f;
            if (sideChevron.enabled)
            {
                sideChevron.rectTransform.anchoredPosition = new Vector2(curRun * (SideW * 0.5f - 30f), -2f);
                sideChevron.rectTransform.localScale = new Vector3(curRun, 1f, 1f);
                sideChevron.color = al > 0f ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            }
            var rt = sideRod.rectTransform;
            rt.anchoredPosition = new Vector2(Mathf.Round(lean * SideRodSlide), 3);
            rt.localRotation = Quaternion.Euler(0, 0, -lean * SideRodTilt);
            sideRod.color = side > dead ? UIKit.Gold : side < -dead ? SideBadColor : UIKit.Cream;
            sideBg.color = side > dead ? new Color(1f, 0.95f, 0.7f, 1f) : Color.white;
        }

        /// <summary>Held from the pointer going down on it until that pointer is released, wherever it goes meanwhile.</summary>
        class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public bool Held { get; private set; }
            int pointer;

            public void OnPointerDown(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left || Held) return;
                Held = true;
                pointer = e.pointerId;
                Sfx.Play(Sfx.Click, 0.6f);
            }

            public void OnPointerUp(PointerEventData e)
            {
                if (e.pointerId == pointer) Held = false;
            }

            void OnDisable() => Held = false;
        }

        /// <summary>A walk button at a fixed spot: its right edge <paramref name="right"/> units from the canvas' right edge.</summary>
        Button WalkButton(string arrow, string name, float right, out HoldButton hold)
        {
            var b = UIKit.Button(root, arrow, "blue", null, new Vector2(WalkBtn, WalkBtn), 24, name: name);
            b.onClick.RemoveAllListeners(); // held, not clicked: the click sounds on the press
            b.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(right, WalkBottom), new Vector2(WalkBtn, WalkBtn), new Vector2(1, 0));
            hold = b.gameObject.AddComponent<HoldButton>();
            return b;
        }

        /// <summary>Greys out the walk button pointing past the end of the angler's standing area.</summary>
        void RefreshWalk()
        {
            if (!walkL.gameObject.activeSelf) return;
            var a = ctl.Angler;
            walkL.interactable = a.X > a.Range.x + 0.01f;
            walkR.interactable = a.X < a.Range.y - 0.01f;
        }

        // ------------------------------------------------------------------ actions
        void OnBack()
        {
            if (ctl.State == FishingController.S.Snagged)
            {
                Toast.Show("밑걸림을 풀거나 끊어야 나갈 수 있어요!", UIKit.Bad);
                return;
            }
            if (!ctl.CanLeave)
            {
                Toast.Show("물고기와 싸우는 중에는 나갈 수 없어요!", UIKit.Bad);
                return;
            }
            SceneFlow.Go("Map");
        }

        void ChangeDepth(float d)
        {
            var L = ctl.Stage.L;
            // (on the generated bed: down to its deepest node, so every depth can be plumbed with a lying float)
            float top = L.Terrain ? Mathf.Max(8f, Mathf.Ceil(L.Bathy.DepthMax * 2f) / 2f)
                : Mathf.Max(Mathf.Max(0.5f, L.ProfileDepth(Mathf.Max(L.zNear + 2f, 20f)) - 0.3f), 8f);
            ctl.Tackle.FloatDepth = Mathf.Clamp(Mathf.Round((ctl.Tackle.FloatDepth + d) * 2f) / 2f, 0.5f, top);
            RefreshTackle();
        }

        void OpenBaitPicker()
        {
            if (ctl.State != FishingController.S.Ready && ctl.State != FishingController.S.Waiting)
            {
                Toast.Show("지금은 미끼를 바꿀 수 없어요", UIKit.Bad);
                return;
            }
            var w = Dialog.Window("미끼 선택", new Vector2(700, 460), out var close);
            UIKit.ScrollList(w, out var content, 6).GetComponent<RectTransform>().Fill(24, 24, 50, 24);
            bool ice = ctl.Stage.L.IsIce;
            // species already caught here that go for it (Appeal >= 0.6): the row gets a small fish mark
            var caughtHere = GameDatabase.FishOfStage(ctl.Stage.Def.id).Where(f => Game.I.Record(f.id) != null).ToList();
            // this stage's legends, once seen in their encounters (their key lures are marked)
            var seenLegends = GameDatabase.FishOfStage(ctl.Stage.Def.id).Where(f => f.encounter != null && (Game.I.FindLegend(f.id)?.seen ?? 0) > 0).ToList();
            foreach (bool lures in new[] { false, true })
            {
                var owned = GameDatabase.Baits.Where(b => b.isLure == lures && Game.I.HasBait(b.id)).ToList();
                if (owned.Count == 0) continue;
                var head = UIKit.Label(content, lures ? "루어" : "미끼", 20, UIKit.Cream, TextAnchor.MiddleLeft);
                head.Layout(30);
                foreach (var b in owned)
                {
                    var row = UIKit.Panel(content, "panel_paper", null, "Row");
                    row.Layout(lures ? 78 : 70);
                    var ic = UIKit.Img(row.transform, Art.Item(b.id), new Vector2(52, 52));
                    ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(52, 52), new Vector2(0, 0.5f));
                    bool banned = ice && !LureInfo.IceOk(b);
                    string cnt = b.infinite ? "무한" : b.isLure ? "" : $"{Game.I.BaitCount(b.id)}개";
                    var t = UIKit.Label(row.transform, $"{b.name}  <size=16><color=#6a5a40>{cnt}</color></size>", 20, UIKit.Ink,
                        lures ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, false);
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    float nameX = 72;
                    if (b.isLure)
                    {
                        // the action chip before the name
                        var chip = UIKit.Img(row.transform, Art.UI(LureInfo.Icon(b.action)), new Vector2(24, 24), "Chip");
                        chip.rectTransform.At(new Vector2(0, 1), new Vector2(72, -8), new Vector2(24, 24), new Vector2(0, 1));
                        var an = UIKit.Label(row.transform, LureInfo.Name(b.action), 16, LureInfo.Color(b.action), TextAnchor.MiddleLeft);
                        an.rectTransform.At(new Vector2(0, 1), new Vector2(100, -8), new Vector2(44, 24), new Vector2(0, 1));
                        nameX = 148;
                        var h = UIKit.Label(row.transform, banned ? LureInfo.IceBanned : b.hint, 15, banned ? new Color32(0xa8, 0x3a, 0x2a, 0xff) : new Color32(0x3a, 0x5a, 0x8a, 0xff),
                            TextAnchor.LowerLeft, false);
                        h.horizontalOverflow = HorizontalWrapMode.Overflow;
                        h.rectTransform.Fill(72, 150, 36, 8);
                    }
                    t.rectTransform.Fill(nameX, 150, lures ? 10 : 6, lures ? 38 : 6);
                    if (caughtHere.Any(f => f.Appeal(b) >= 0.6f))
                    {
                        var mark = UIKit.Img(row.transform, Art.UI("icon_fish"), new Vector2(24, 24), "Fans");
                        mark.rectTransform.At(new Vector2(1, 1), new Vector2(-136, -6), new Vector2(24, 24), new Vector2(1, 1));
                    }
                    // the stage legend was seen: its key lures get an eye
                    if (seenLegends.Any(l => l.encounter.KeyWeight(b.id) > 0f))
                    {
                        var eye = UIKit.Img(row.transform, Art.UI("icon_eye"), new Vector2(32, 32), "Legend");
                        eye.rectTransform.At(new Vector2(1, 1), new Vector2(-164, -2), new Vector2(32, 32), new Vector2(1, 1));
                    }
                    bool eq = Game.I.Bait.id == b.id;
                    var bb = UIKit.Button(row.transform, eq ? "사용 중" : banned ? "사용 불가" : "사용", eq || banned ? "grey" : "green", () =>
                    {
                        ctl.EquipBait(b);
                        close();
                        RefreshTackle();
                    }, new Vector2(120, 50), 19);
                    bb.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(120, 50), new Vector2(1, 0.5f));
                    bb.interactable = !eq && !banned;
                    if (banned) row.color = new Color(0.78f, 0.78f, 0.78f, 1f);
                }
            }
        }

        /// <summary>
        /// The line readouts: over the reel what more it can give (ReelGrip.SetSpool: the reel's capacity or the spool,
        /// whichever is less, less the line out); at the reel's place while it is hidden (ready) the spool and the reel, or,
        /// with no line (thrown away), where to get one; over the float the line out while it changes (fading after 2 s
        /// still), not in a bite (its "!" is there), a fight or a snag (their strip shows it).
        /// </summary>
        void UpdateLineText(float dt)
        {
            if (spotText == null) return;
            var s = ctl.State;
            var g = Game.I;
            bool ready = s == FishingController.S.Ready || s == FishingController.S.Aiming;
            bool lineOut = s == FishingController.S.Waiting || s == FishingController.S.Biting || s == FishingController.S.Fighting || s == FishingController.S.Snagged;
            float cap = ctl.Fight != null ? ctl.Fight.SpoolCap : Mathf.Min(g.Reel.lineCap, g.LineLeftNow);
            reel.SetSpool(lineOut ? ctl.LineOut : 0f, cap);
            string t = !ready ? "" : g.HasLine ? $"감긴 줄 {g.LineLeftNow:0}m\n릴 {g.Reel.lineCap:0}m" : "줄 없음\n지도 상점에서 장착";
            if (spotText.text != t) spotText.text = t;
            spotText.color = g.HasLine ? UIKit.Cream : UIKit.Bad;
            var tk = ctl.Tackle;
            bool show = (s == FishingController.S.Waiting || s == FishingController.S.Retrieving)
                        && (tk.State == Tackle.Mode.Water || tk.State == Tackle.Mode.Perched) && PixelView.Current != null;
            if (!show)
            {
                floatGroup.alpha = 0f;
                floatLast = -1f;
                return;
            }
            if (Mathf.Abs(ctl.LineOut - floatLast) >= 0.05f)
            {
                floatLast = ctl.LineOut;
                floatIdle = 0f;
            }
            else floatIdle += dt;
            floatGroup.alpha = floatIdle < 2f ? 1f : Mathf.Clamp01(1f - (floatIdle - 2f) / 0.5f);
            string ft = $"{ctl.LineOut:0.0}m";
            if (floatText.text != ft) floatText.text = ft;
            // just over the float's top (where a bite's "!" goes), kept on screen under the top bar
            var at = tk.State == Tackle.Mode.Perched ? tk.PerchAt : tk.Surface;
            var w = ctl.Stage.P.To2D(at) + new Vector2(0f, tk.UsesFloat ? tk.FloatScale * 0.95f + 0.15f : 0.25f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, PixelView.Current.WorldToScreen(w), null, out var local);
            var half = root.rect.size * 0.5f;
            local.x = Mathf.Clamp(local.x, -half.x + 60f, half.x - 60f);
            local.y = Mathf.Clamp(local.y, -half.y + 20f, half.y - 170f);
            floatText.rectTransform.anchoredPosition = local;
        }

        public void RefreshTackle()
        {
            var b = Game.I.Bait;
            if (baitIcon == null) return;
            baitIcon.sprite = Art.Item(b.id);
            baitCount.text = b.infinite ? "∞" : b.isLure ? "" : "x" + Game.I.BaitCount(b.id);
            depthText.text = ctl.Stage.L.IsIce ? "-" : $"{ctl.Tackle.FloatDepth:0.0}m";
            bool showDepth = !b.isLure && !ctl.Stage.L.IsIce;
            depthGroup.gameObject.SetActive(showDepth);
            lureGroup.gameObject.SetActive(b.isLure);
            if (b.isLure)
            {
                lureChip.sprite = Art.UI(LureInfo.Icon(b.action));
                lureAction.text = LureInfo.Name(b.action);
                lureAction.color = LureInfo.Color(b.action);
                UpdateLureSlot();
            }
            tacklePanel.sizeDelta = new Vector2(showDepth || b.isLure ? TackleWide : TackleNarrow, 92);
        }

        /// <summary>The lure slot: pips = Q (gold from 0.75, blinking while a following fish may strike), the depth.</summary>
        void UpdateLureSlot()
        {
            bool inWater = ctl.State == FishingController.S.Waiting && ctl.Tackle.State == Tackle.Mode.Water && ctl.Tackle.Bait.isLure;
            var rh = ctl.Rhythm;
            float q = inWater ? rh.Q : 0f;
            int lit = Mathf.Clamp(Mathf.RoundToInt(q * 5f), 0, 5);
            bool blinkOff = inWater && rh.StrikeOpen && Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.35f;
            var on = q >= 0.75f ? UIKit.Gold : PipOn;
            for (int i = 0; i < pips.Length; i++)
                pips[i].color = i < lit ? (blinkOff ? Color.Lerp(on, PipOff, 0.6f) : on) : PipOff;
            lureDepth.text = !inWater ? "수심 -" : ctl.Tackle.Depth <= 0.12f ? "수심 수면" : $"수심 {ctl.Tackle.Depth:0.0}m";
        }

        /// <summary>A lure feedback word (좋아요!, 너무 빨라요 ...) just above the tackle panel, fading after 1.2 s.</summary>
        public void LureFeedback(string word, bool good) =>
            LureFeedback(word, good ? UIKit.Gold : (Color)new Color32(0xff, 0xb0, 0x8a, 0xff), LureWordTime);

        /// <summary>A feedback word in the lure slot's spot in any colour, for <paramref name="time"/> s (the mend's "멘딩!").</summary>
        public void LureFeedback(string word, Color c, float time)
        {
            lureWord.text = word;
            lureWord.color = c;
            lureWordT = time;
            lureWordGroup.alpha = 1f;
            var o = lureWord.GetComponent<PixelOutline>();
            if (o != null) o.enabled = true;
            Tween.Punch(lureWord.transform, 0.1f);
        }

        // ------------------------------------------------------------------ legend encounter
        /// <summary>The encounter's HUD over the window (flash messages go to its captions until <see cref="EndEncounter"/>).</summary>
        public EncounterHUD BeginEncounter(LegendEncounter e, EncounterView v)
        {
            EndEncounter();
            flashTime = 0f;
            flashGroup.alpha = 0f;
            enc = EncounterHUD.Create(root, e, v);
            return enc;
        }

        public void EndEncounter()
        {
            if (enc != null) Destroy(enc.gameObject);
            enc = null;
        }

        // ------------------------------------------------------------------ state display
        public void OnState(FishingController.S s)
        {
            bool snag = s == FishingController.S.Snagged;
            bool fighting = s == FishingController.S.Fighting;
            bool strip = fighting || snag;   // (snagged: the fight strip in snag mode)
            bool encounter = s == FishingController.S.Encounter;
            fightPanel.gameObject.SetActive(strip);
            // the fight strip takes the whole top edge; the encounter's window sits under it, so the top HUD steps aside
            topBar.gameObject.SetActive(!strip && !encounter);
            stageName.gameObject.SetActive(!strip && !encounter);
            clockPanel.gameObject.SetActive(!strip && !encounter);
            backBtn.gameObject.SetActive(!strip && !encounter);
            settingsBtn.gameObject.SetActive(!strip && !encounter);
            // snag mode: the name slot reads 밑걸림 (with its icon), no stamina, no side strip, no abrasion meter
            fishName.enabled = !snag;
            snagIcon.enabled = snagName.enabled = snag;
            staminaLabel.gameObject.SetActive(!snag);
            staminaBar.SetActive(!snag);
            sideBg.gameObject.SetActive(!snag && !ctl.Stage.L.IsIce);
            if (strip) ShowAbrasion(false);
            // above the reel at rest; while fighting it carries the wind / give-line help (two lines); in an encounter the
            // two verbs that work the lure
            reel.SetLabel(strip ? $"{CircleGesture.WindWay} = 감기\n{CircleGesture.GiveWay} = 풀기" : encounter ? "원 = 감기\n↓톡 = 당기기" : "원을 그려 감기");
            reel.Show(s == FishingController.S.Waiting || s == FishingController.S.Biting || strip || encounter);
            // the 회수 button reads 끊기 while snagged (a tap cuts the line)
            retrieveBtn.gameObject.SetActive(s == FishingController.S.Waiting || snag);
            retrieveBtn.SetLabel(snag ? "끊기" : "회수");
            retrieveBtn.SetStyle(snag ? "red" : "blue");
            aimPanel.gameObject.SetActive(s == FishingController.S.Aiming || castFbT > 0f);
            tacklePanel.gameObject.SetActive(s == FishingController.S.Ready || s == FishingController.S.Waiting || s == FishingController.S.Aiming);
            bool walk = s == FishingController.S.Ready && ctl.Angler.Range.y - ctl.Angler.Range.x > 0.01f;
            walkL.gameObject.SetActive(walk);
            walkR.gameObject.SetActive(walk);
            RefreshWalk();
            backBtn.interactable = ctl.CanLeave;
            bool ice = ctl.Stage.L.IsIce;
            switch (s)
            {
                case FishingController.S.Ready:
                    hint.text = ice ? "화면을 아래로 끌어 수심을 정하고 놓으면 얼음 구멍에 채비를 내려요"
                                    : "아래로 당겼다가 던질 쪽으로 위로 튕기며 놓기! 세게 튕길수록 멀리";
                    break;
                case FishingController.S.Aiming: hint.text = ""; break;
                case FishingController.S.Casting: hint.text = ""; break;
                case FishingController.S.Waiting:
                    hint.text = ctl.Tackle.UsesFloat ? "찌를 지켜보세요... 원을 그리면 채비를 감아요"
                        : !string.IsNullOrEmpty(ctl.Tackle.Bait.hint) ? ctl.Tackle.Bait.hint : "원을 그려 루어를 감으며 움직여 유혹하세요";
                    break;
                case FishingController.S.Biting: hint.text = "지금이야! 화면을 탭!"; break;
                case FishingController.S.Fighting: hint.text = ""; break;
                default: hint.text = ""; break;
            }
            UIKit.FitPlate(hint, hintPlate);
        }

        internal string DepthLabelText => depthLabel != null ? depthLabel.text : null;

        public void Flash(string text, Color c, float time = 1.5f)
        {
            // during an encounter the top-centre flash would sit inside the window: its captions carry the message
            if (enc != null && ctl.State == FishingController.S.Encounter)
            {
                enc.Caption(text, c, time);
                return;
            }
            // a message in the same spot: the thrown flick's power readout gives way
            if (castFbT > 0f)
            {
                castFbT = 0f;
                if (ctl.State != FishingController.S.Aiming) aimPanel.gameObject.SetActive(false);
            }
            flash.text = text;
            flash.color = c;
            flashGroup.alpha = 1;
            if (flashOutline == null) flashOutline = flash.GetComponent<PixelOutline>();
            if (flashOutline != null) flashOutline.enabled = true;
            flashTime = time;
            Tween.Punch(flash.transform, 0.12f);
        }

        public void Tick(float dt)
        {
            UpdateLineText(dt);
            if (flashTime > 0)
            {
                flashTime -= dt;
                if (flashTime < 0.4f)
                {
                    // fade the letters alone: stacked translucent outline copies would read as a dark smudge
                    if (flashOutline != null && flashOutline.enabled) flashOutline.enabled = false;
                    flashGroup.alpha = Mathf.Clamp01(flashTime / 0.4f);
                }
            }
            else flashGroup.alpha = 0f;
            float fb = 0f;
            if (castFbT > 0f)
            {
                castFbT -= dt;
                fb = Mathf.Clamp01(castFbT / CastFbFade);
                aimGroup.alpha = fb;
                if (castFbT <= 0f && ctl.State != FishingController.S.Aiming) aimPanel.gameObject.SetActive(false);
            }
            hintGroup.alpha = 1f - Mathf.Max(flashGroup.alpha, fb);
            if (clockPanel.gameObject.activeSelf) UpdateClock();
            RefreshWalk();
            if (lureGroup.gameObject.activeInHierarchy) UpdateLureSlot();
            // a float rig waiting: "찌 누움" in gold while the float lies flat (set deeper than the water: plumbing)
            if (depthLabel != null)
            {
                bool lying = ctl.Tackle.FloatLying && ctl.State == FishingController.S.Waiting;
                string want = lying ? "찌 누움" : "찌 수심";
                if (depthLabel.text != want)
                {
                    depthLabel.text = want;
                    depthLabel.color = lying ? UIKit.Gold : UIKit.Sky;
                }
            }
            if (lureWordT > 0f)
            {
                lureWordT -= dt;
                if (lureWordT < 0.3f)
                {
                    var o = lureWord.GetComponent<PixelOutline>();
                    if (o != null && o.enabled) o.enabled = false;
                }
                lureWordGroup.alpha = Mathf.Clamp01(lureWordT / 0.3f);
            }
            else lureWordGroup.alpha = 0f;
            if (ctl.State == FishingController.S.Aiming)
            {
                aimGroup.alpha = 1f;
                if (ctl.Stage.L.IsIce)
                {
                    var bar = aimPanel.GetComponent<AimBarRef>().fill;
                    aimBarBg.SetActive(true);
                    UIKit.SetBar(bar, ctl.AimPower);
                    aimText.color = UIKit.Gold;
                    aimText.text = $"수심 {ctl.AimDepth:0.0}m";
                }
                else
                {
                    // winding up: distance and direction come from the flick, so no power bar yet, only the cue once armed;
                    // the cue takes the flash message's spot, so a message still showing (a missed flick, +coins) gives way
                    aimBarBg.SetActive(false);
                    aimText.color = UIKit.Cream;
                    aimText.text = ctl.WindUpArmed ? "위로 튕기며 놓으세요!" : "";
                    if (ctl.WindUpArmed && flashGroup.alpha > 0f)
                    {
                        flashTime = 0f;
                        flashGroup.alpha = 0f;
                    }
                }
            }
        }

        /// <summary>
        /// Right after a throw: how strong the flick was ("힘 72%", "최고!" at the top) with the bar, for a moment under the
        /// top edge; the hint gives way meanwhile and a flash message cuts it short.
        /// </summary>
        public void ShowCastPower(float power)
        {
            int pct = Mathf.RoundToInt(Mathf.Clamp01(power) * 100f);
            bool best = power >= 0.95f;
            aimText.text = best ? $"최고! 힘 {pct}%" : $"힘 {pct}%";
            aimText.color = best ? UIKit.Gold : UIKit.Cream;
            aimBarBg.SetActive(true);
            UIKit.SetBar(aimPanel.GetComponent<AimBarRef>().fill, power);
            flashTime = 0f;
            flashGroup.alpha = 0f;
            castFbT = CastFbTime;
            aimGroup.alpha = 1f;
            aimPanel.gameObject.SetActive(true);
            Tween.Punch(aimPanel, best ? 0.2f : 0.1f);
        }

        public void UpdateFight(FightModel f, FishAgent fish)
        {
            float r = Mathf.Clamp01(f.TensionRatio);
            shownTension = Mathf.Lerp(shownTension, r, 0.35f);
            UIKit.SetBar(tension, shownTension);
            tension.color = shownTension < 0.55f ? UIKit.Good : shownTension < 0.85f ? new Color32(0xff, 0xd2, 0x3a, 0xff) : UIKit.Bad;
            if (shownTension > 0.85f)
            {
                float blink = Mathf.PingPong(Time.time * 6f, 1f);
                tension.color = Color.Lerp(UIKit.Bad, Color.white, blink * 0.5f);
            }
            float dragPos = Mathf.Clamp01(Game.I.Reel.dragMax / f.LineLimit);
            dragMark.enabled = dragPos < 0.99f;
            dragMark.rectTransform.anchorMin = dragMark.rectTransform.anchorMax = new Vector2(dragPos, 0.5f);
            dragMark.rectTransform.anchoredPosition = Vector2.zero;
            UIKit.SetBar(stamina, f.Stamina);
            var sp = fish.Sp;
            fishName.text = $"<color=#{ColorUtility.ToHtmlStringRGB(RarityInfo.Color(sp.rarity))}>{sp.name}</color>";
            distance.text = $"{f.Line:0.0}m";
            string ph;
            if (f.Exhausted) ph = "지쳤다! 지금 힘껏 감아요!";
            else if (f.Jumping && f.Shaking) ph = "머리를 턴다! 릴 멈춰요!";
            else if (f.Jumping) ph = f.Jump switch
            {
                FightModel.JumpKind.Shake => "몸부림! 머리 털 때 멈춰요!",
                FightModel.JumpKind.TailWalk => "꼬리로 수면을 달린다!",
                _ => "점프! 장력을 낮춰요!",
            };
            else if (f.State == FightModel.Phase.Run || f.State == FightModel.Phase.Burst) ph = "도망친다! 장력 조심!";
            else ph = "쉬는 중 - 감아요!";
            // side pressure: the rod leant against the run / with it (the arrow: the way to lean instead)
            var phc = UIKit.Gold;
            // (no finger on the reel: the spool runs free)
            if (f.Free)
            {
                ph = "손을 떼서 줄이 풀려요 — 누르고 있으면 버텨요";
                phc = UIKit.Sky;
            }
            if (ctl.SideActive && ctl.SideNow > FishingController.SideDead) ph = "사이드 프레셔!";
            else if (ctl.SideActive && ctl.SideNow < -FishingController.SideDead)
            {
                ph = ctl.FishRun > 0 ? "◀ 반대쪽으로!" : "반대쪽으로! ▶";
                phc = SideBadColor;
            }
            // structure (Docs/obstacles_spec.md 7.7): break > abrasion > rubbing > cover run > side pressure > the rest
            bool blinkOn = Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.6f;
            if (f.CoverHold) { ph = "커버에 박혔다!"; phc = UIKit.Bad; }
            else if (f.CoverRun) { ph = "커버로 도망친다!"; phc = UIKit.Bad; }
            if (ctl.Rubbing)
            {
                ph = "줄이 쓸리는 중!";
                phc = blinkOn ? UIKit.Bad : Color.Lerp(UIKit.Bad, Color.white, 0.4f);
                if (f.Abrasion >= 0.8f) ph = "줄이 버티지 못해요! 빨리 빼내요!";
            }
            if (f.BreakRatio > 0.3f) { ph = "줄이 끊어지려 한다!!"; phc = UIKit.Gold; }
            else if (f.SlackRatio > 0.5f && !ctl.Rubbing) { ph = "줄이 느슨해! 감아요!"; phc = UIKit.Gold; }
            phaseLabel.text = ph;
            phaseLabel.color = phc;
            UpdateSide();
            // the abrasion meter, from the first rub on
            ShowAbrasion(ctl.RubbedThisFight);
            if (ctl.RubbedThisFight)
            {
                float a = Mathf.Clamp01(f.Abrasion);
                abrFill.fillAmount = Mathf.Round(a * 76f) / 76f;
                var c = a < 0.5f ? AbrLow : a < 0.8f ? AbrMid : UIKit.Bad;
                if (a >= 0.8f && !blinkOn) c = Color.Lerp(UIKit.Bad, Color.white, 0.45f);
                abrFill.color = c;
            }
        }

        void ShowAbrasion(bool on)
        {
            if (abrLabel == null) return;
            abrLabel.enabled = abrBg.enabled = abrFill.enabled = on;
        }

        /// <summary>
        /// The fight strip in snag mode (Docs/obstacles_spec.md 10.2): 밑걸림 / 수초 / 갈대 / 연잎 in the name slot, the rig's
        /// distance, the snag tension on the tension bar, and the phase label: the tension warning, the free side (once the
        /// arrow shows), else how to free it.
        /// </summary>
        public void UpdateSnag(SnagInfo sn)
        {
            if (sn == null) return;
            string name = sn.kind == "weed" ? "수초" : sn.kind == "reed" ? "갈대" : sn.kind == "pad" ? "연잎"
                : sn.kind == "prop" ? (!string.IsNullOrEmpty(sn.zone?.Name) ? sn.zone.Name : "걸림") : "밑걸림";
            if (snagName.text != name) snagName.text = name;
            snagName.color = UIKit.Bad;
            var icon = Art.UI(sn.kind == "pad" ? "icon_snag_pad" : sn.soft ? "icon_snag_weed" : "icon_snag");
            if (icon != null && snagIcon.sprite != icon) snagIcon.sprite = icon;
            distance.text = $"{ctl.LineOut:0.0}m";
            float r = Mathf.Clamp01(sn.r);
            shownTension = Mathf.Lerp(shownTension, r, 0.35f);
            UIKit.SetBar(tension, shownTension);
            tension.color = shownTension < 0.55f ? UIKit.Good : shownTension < 0.85f ? new Color32(0xff, 0xd2, 0x3a, 0xff) : UIKit.Bad;
            dragMark.enabled = false;
            bool blinkOn = Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.6f;
            if (sn.r >= 0.6f && sn.kind != "pad")
            {
                phaseLabel.text = "팽팽해요! 감지 마세요!";
                phaseLabel.color = blinkOn ? UIKit.Bad : Color.Lerp(UIKit.Bad, Color.white, 0.4f);
            }
            else if (ctl.SnagArrowOn)
            {
                phaseLabel.text = sn.freeSide < 0 ? "◀ 반대쪽으로 밀어 봐요" : "반대쪽으로 밀어 봐요 ▶";
                phaseLabel.color = UIKit.Gold;
            }
            else
            {
                // (no rod sweep through the ice hole: only the 톡 and a slack line free it there)
                // (wedged on a prop, no sweep slides it off: a 톡 or 끊기)
                phaseLabel.text = ctl.Stage.L.IsIce ? "감지 말고 톡!" : sn.kind == "prop" && sn.freeSide == 0 ? "끼었어요! 톡 하거나 끊어요"
                    : "감지 말고 톡! 또는 좌우로 밀어요";
                phaseLabel.color = UIKit.Sky;
            }
        }
    }
}
