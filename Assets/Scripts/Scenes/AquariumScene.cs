using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The player's aquarium: kept fish swim here and earn viewing income over time. They are fed from the feed bags on
    /// the ledge in front of the tank (<see cref="AquaFeed"/>) and grow a little while fed (<see cref="AquaCare"/>). The
    /// tank gets dirty on real time and is cleaned by hand with the tools beside the feed (<see cref="AquaClean"/>), and
    /// decorated in its slots (<see cref="AquaDecor"/>: the 꾸미기 button); both change the income (<see cref="AquaTank"/>,
    /// a tap on the income panel shows the breakdown).
    /// </summary>
    public class AquariumScene : MonoBehaviour
    {
        PixelView pv;
        AquaLayout data;
        int builtLevel;
        AquaFeed feed;
        AquaClean clean;
        AquaDecor decor;
        RectTransform uiRoot;
        readonly List<TankFish> fish = new List<TankFish>();
        Text title, income, collectLabel, hintText;
        Image hintPlate, incomePanel;
        Button collectBtn, upBtn, decorBtn;
        float refreshT, bubbleT;
        int ticks;

        const string TapHint = "물고기를 탭하면 정보를 보고 판매할 수 있어요";

        void Start()
        {
            // this tank level's own art and layout (Resources/Data/aquarium_tank_<n>.json): a bigger tank, a wider view
            builtLevel = Game.Data.tankLevel;
            data = AquaLayout.Scene = AquaLayout.Of(builtLevel);
            pv = PixelView.Create(Color.black);
            pv.SetBaseHeight(data.viewH);
            AquaCare.Advance(Game.Data, SaveSystem.Now);
            var back = new GameObject("Back").AddComponent<SpriteRenderer>();
            back.sprite = Art.Stage(data.back);
            var front = new GameObject("Front").AddComponent<SpriteRenderer>();
            front.sprite = Art.Stage(data.front);
            AquaCare.Log($"aquarium scene: {data.name} (level {data.level}), view {data.viewH} px high, glass {data.glassW}x{data.glassH}, {AquaTank.Used(Game.Data)}/{Game.I.Capacity}칸");
            front.sortingOrder = 40;
            Fx.Ensure();
            Toast.Init();
            Sfx.Ambience("none");
            Sfx.Music(true);
            var root = uiRoot = BuildUI();
            Rebuild();
            feed = new GameObject("AquaFeed").AddComponent<AquaFeed>();
            feed.Init(pv, data, fish, root);
            clean = new GameObject("AquaClean").AddComponent<AquaClean>();
            clean.Init(pv, data, fish, root);
            decor = new GameObject("AquaDecor").AddComponent<AquaDecor>();
            decor.Init(pv, data, root);
            decor.ModeChanged += OnDecorMode;
            feed.ShowMoods(2.5f);
            RefreshUI();
        }

        /// <summary>The decoration mode hides the bottom panels (its tray takes their place).</summary>
        void OnDecorMode()
        {
            bool on = AquaDecor.ModeOn;
            incomePanel.gameObject.SetActive(!on);
            upBtn.gameObject.SetActive(!on);
            decorBtn.gameObject.SetActive(!on);
            refreshT = 0f;
            RefreshUI();
        }

        void Rebuild()
        {
            foreach (var f in fish) if (f != null) Destroy(f.gameObject);
            fish.Clear();
            int i = 0;
            foreach (var cf in Game.Data.aquarium)
            {
                var tf = new GameObject("Tank_" + cf.speciesId).AddComponent<TankFish>();
                tf.Init(cf, data, i++);
                AquaCare.Log($"tank fish {cf.speciesId} {AquaCare.GrownCm(cf):0.0}cm ({AquaTank.ClassNames[AquaTank.ClassOf(cf)]}): drawn {tf.DrawnPx:0} px long (sprite {tf.SpritePx} px x{tf.Scale:0.00})");
                fish.Add(tf);
            }
            RefreshUI();
        }

        RectTransform BuildUI()
        {
            var canvas = UIKit.CreateCanvas("AquariumUI", 10);
            var root = (RectTransform)canvas.transform;
            var back = UIKit.IconButton(root, Art.UI("icon_back"), "grey", () => SceneFlow.Go("Map"), 56);
            back.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(12, -10), new Vector2(56, 56));
            var tp = UIKit.Panel(root, "panel_dark", null, "Title");
            tp.rectTransform.At(new Vector2(0, 1), new Vector2(76, -10), new Vector2(340, 56));
            title = UIKit.Label(tp.transform, "", 22, UIKit.Cream);
            title.rectTransform.Fill(8, 8, 4, 8);
            TopBar.Create(root);

            // the income: this minute's rate and what makes it (a tap: the breakdown), the collect button
            var ip = incomePanel = UIKit.Panel(root, "panel_dark", null, "Income");
            ip.rectTransform.At(new Vector2(0, 0), new Vector2(12, 12), new Vector2(490, 76), new Vector2(0, 0));
            ip.raycastTarget = true;
            var ib = ip.gameObject.AddComponent<Button>();
            ib.transition = Selectable.Transition.None;
            ib.onClick.AddListener(() =>
            {
                Sfx.Play(Sfx.Click);
                ShowIncome();
            });
            income = UIKit.Label(ip.transform, "", 17, UIKit.Cream, TextAnchor.MiddleLeft);
            income.horizontalOverflow = HorizontalWrapMode.Overflow;
            income.rectTransform.Fill(14, 150, 6, 8);
            collectBtn = UIKit.Button(ip.transform, "받기", "yellow", Collect, new Vector2(130, 56), 18, Art.UI("coin"));
            collectBtn.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(130, 56), new Vector2(1, 0.5f));
            collectLabel = collectBtn.GetComponentInChildren<Text>();
            // big pending sums stay on one line: shrink the digits to fit instead of wrapping
            collectLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            collectLabel.resizeTextForBestFit = true;
            collectLabel.resizeTextMinSize = 11;
            collectLabel.resizeTextMaxSize = 18;

            var up = upBtn = UIKit.Button(root, "수조 확장", "blue", () =>
            {
                ShopUI.TankFeed = false;
                ShopUI.Open(root, ItemKind.Tank);
            }, new Vector2(170, 60), 20, Art.UI("icon_tank"));
            up.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(-14, 14), new Vector2(170, 60), new Vector2(1, 0));
            // the decoration mode (the treasure chest's icon at its own pixel size)
            decorBtn = UIKit.Button(root, "꾸미기", "green", () => decor.Enter(), new Vector2(150, 60), 20, null, "DecorButton");
            decorBtn.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(-192, 14), new Vector2(150, 60), new Vector2(1, 0));
            var dl = decorBtn.GetComponentInChildren<Text>();
            dl.rectTransform.Fill(46, 6, 4, 8);
            var di = UIKit.Img(decorBtn.transform, Art.Item("decor_chest"), new Vector2(32, 32), "Icon");
            di.rectTransform.At(new Vector2(0, 0.5f), new Vector2(12, 2), new Vector2(32, 32), new Vector2(0, 0.5f));
            hintText = UIKit.PlateLabel(root, TapHint, 17, UIKit.Cream, out hintPlate);
            hintPlate.rectTransform.anchorMin = hintPlate.rectTransform.anchorMax = new Vector2(0.5f, 1);
            hintPlate.rectTransform.pivot = new Vector2(0.5f, 1);
            hintPlate.rectTransform.anchoredPosition = new Vector2(0, -76);
            Game.I.Changed += RefreshUI;
            return root;
        }

        void OnDestroy()
        {
            if (Game.I != null) Game.I.Changed -= RefreshUI;
            if (AquaLayout.Scene == data) AquaLayout.Scene = null;
        }

        const string DecorHint = "장식을 끌어서 빛나는 자리에 놓아요 · 자리 밖으로 끌면 보관함으로";

        void RefreshUI()
        {
            if (title == null) return;
            var d = Game.Data;
            // the space in 칸 (a fish takes 1 / 2 / 4 / 8 by its size); over the limits (an older save): in red
            int used = AquaTank.Used(d), over = AquaTank.Over(d).Count;
            title.text = over > 0 || used > Game.I.Capacity
                ? $"{data.name}  <color=#ff9a8a>{used}/{Game.I.Capacity}칸 (넘침)</color>"
                : $"{data.name}  {used}/{Game.I.Capacity}칸";
            int pm = Game.I.IncomePerMinute;
            int pending = Game.I.PendingIncome;
            int hungry = d.aquarium.Count(AquaCare.Hungry);
            string state = hungry == 0 ? "<color=#8ae08a>배불러요 +20%</color>"
                : hungry == d.aquarium.Count ? "<color=#ff9a8a>배고파요 -30%</color>"
                : $"<color=#ff9a8a>배고픈 물고기 {hungry}마리</color>";
            // the tank's own share: the decorations' bonus, what the dirt costs
            float bonus = Mathf.Min(AquaTank.BonusCap, AquaTank.DecorBonus(d));
            float pen = AquaTank.Penalty(d);
            if (bonus > 0.004f) state += $" · <color=#8ae08a>장식 +{bonus * 100f:0}%</color>";
            if (pen >= 0.005f) state += $" · <color=#ffb87a>청소 -{pen * 100f:0}%</color>";
            income.text = d.aquarium.Count > 0 ? $"관람 수입 {UIKit.Num(pm)} 코인/분\n<size=15>{state}</size>" : "물고기를 넣으면\n관람 수입이 생겨요";
            collectLabel.text = pending > 0 ? UIKit.Num(pending) : "받기";
            collectBtn.interactable = pending > 0;
            // the top hint teaches the feeding while someone is hungry, then the cleaning; the decoration mode says how
            string h = TapHint;
            if (AquaDecor.ModeOn) h = DecorHint;
            else if (hungry > 0) h = FeedHint(d);
            else if (AquaClean.DirtHint() is var dh && dh != "") h = dh;
            if (hintText.text != h)
            {
                hintText.text = h;
                UIKit.FitPlate(hintText, hintPlate);
            }
            // out of the way while food is poured over the water's top or a tool works
            bool show = (feed == null || !feed.FeedingNow) && (clean == null || !clean.Busy);
            hintPlate.enabled = show && !string.IsNullOrEmpty(h);
            hintText.enabled = hintPlate.enabled;
        }

        /// <summary>
        /// The viewing income's breakdown: the fish's own income (fed / hungry), the decorations' bonus, the light's
        /// favoured fish, the dirt's cost, the total.
        /// </summary>
        void ShowIncome()
        {
            var d = Game.Data;
            AquaCare.Advance(d, SaveSystem.Now);
            var w = Dialog.Window("관람 수입 내역", new Vector2(900, 400), out var close);
            var box = UIKit.Panel(w, "panel_paper", null, "Breakdown");
            box.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(840, 16 + 7 * RowPitch - 4), new Vector2(0.5f, 1));
            int n = d.aquarium.Count, hungry = d.aquarium.Count(AquaCare.Hungry);
            float baseSum = d.aquarium.Sum(AquaCare.BaseIncome), fedSum = d.aquarium.Sum(AquaCare.IncomeF);
            IncomeRow(box.transform, 0, Art.UI("icon_fish"), "물고기", $"{n}마리 · 기본 {baseSum:0.0} 코인/분");
            IncomeRow(box.transform, 1, Art.UI(hungry > 0 ? "icon_hungry" : "icon_full"), "배부름",
                n == 0 ? "-" : hungry == 0 ? $"<color={Green}>모두 배불러요 +20%</color> <size=16>→ {fedSum:0.0}</size>"
                : $"<color={Red}>배고픈 {hungry}마리 -30%</color> <size=16>· 배부른 {n - hungry}마리 +20% → {fedSum:0.0}</size>");
            var placed = AquaTank.Placed(d);
            float raw = AquaTank.DecorBonus(d), bonus = Mathf.Min(AquaTank.BonusCap, raw);
            var floor = placed.Where(x => x.kind != DecorKind.Light).Select(x => x.name).ToList();
            string items = floor.Count == 0 ? "장식이 없어요 · 꾸미기에서 놓아요" : $"{floor.Count}개: " + string.Join(" ", floor.Take(4)) + (floor.Count > 4 ? " …" : "");
            IncomeRow(box.transform, 2, Art.UI("icon_eye"), "장식",
                $"<color={Green}>+{bonus * 100f:0}%</color><size=16>{(raw > AquaTank.BonusCap + 1e-4f ? $" (최대, 합 +{raw * 100f:0}%)" : $" / 최대 +{AquaTank.BonusCap * 100f:0}%")} · {items}</size>");
            var light = AquaTank.Light(d);
            int fav = light == null ? 0 : d.aquarium.Count(f => AquaTank.Favour(d, f) > 0f);
            IncomeRow(box.transform, 3, Art.UI("icon_xp"), "조명",
                light == null ? "조명 없음 <size=16>· 조명마다 좋아하는 물고기가 있어요</size>"
                : $"{light.name} +{light.bonus * 100f:0}% <size=16>· {light.favourText} {fav}마리 +{AquaTank.FavourBonus * 100f:0}%</size>");
            var t = d.tank;
            float pen = AquaTank.Penalty(d);
            IncomeRow(box.transform, 4, Art.UI(pen > 0.1f ? "icon_hungry" : "icon_check"), "수조 청결",
                (pen >= 0.005f ? $"<color={Red}>-{pen * 100f:0}%</color>" : $"<color={Green}>깨끗해요</color>")
                + $"<size=16> · 이끼 {t.algae * 100f:0}% · 찌꺼기 {t.debris * 100f:0}% · 바닥 {t.dirt * 100f:0}% (최대 -{AquaTank.MaxPenalty * 100f:0}%)</size>");
            AquaTank.Rates(d, out float ra, out _, out _);
            float hrs = ra > 0f ? (1f - t.algae) / ra / 3600f : 0f;
            IncomeRow(box.transform, 5, Art.UI("icon_tank"), "더러워짐",
                $"<size=16>물고기 {n}마리 x{AquaTank.FishFactor(n):0.00} · 수초 {AquaTank.Plants(d)}개{(AquaTank.Bubbler(d) ? " · 기포기" : "")} · 이끼 가득까지 {(t.algae >= 0.999f ? "지금" : Mathf.CeilToInt(hrs) + "시간")}</size>");
            IncomeRow(box.transform, 6, Art.UI("coin"), "합계", $"<color={Green}>{AquaTank.IncomePerMin(d):0.0} 코인/분</color> <size=16>· 받을 수입 {UIKit.Num(Game.I.PendingIncome)} 코인</size>");
            AquaCare.Log($"income breakdown: base {baseSum:0.00}, fed {fedSum:0.00}, decor +{bonus * 100f:0}% (raw {raw * 100f:0}%), light {light?.key ?? "none"} fav {fav}, penalty {pen * 100f:0.0}%, total {AquaTank.IncomePerMin(d):0.00}/min");
            var ok = UIKit.Button(w, "닫기", "blue", close, new Vector2(200, 60), 21, null, "IncomeClose");
            ok.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(200, 60), new Vector2(0.5f, 0));
        }

        static void IncomeRow(Transform box, int i, Sprite icon, string label, string value)
        {
            float y = -8 - i * RowPitch;
            if (icon != null)
            {
                var ic = UIKit.Img(box, icon, new Vector2(24, 24), "Icon");
                ic.rectTransform.At(new Vector2(0, 1), new Vector2(14, y - 2), new Vector2(24, 24), new Vector2(0, 1));
            }
            var l = UIKit.Label(box, label, 16, Muted, TextAnchor.MiddleLeft, false, "Key");
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            l.rectTransform.At(new Vector2(0, 1), new Vector2(46, y), new Vector2(110, 28), new Vector2(0, 1));
            var v = UIKit.Label(box, value, 20, UIKit.Ink, TextAnchor.MiddleLeft, false, "Value");
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            v.rectTransform.At(new Vector2(0, 1), new Vector2(150, y), new Vector2(676, 28), new Vector2(0, 1));
        }

        /// <summary>
        /// What to do for the hungry fish: a hungry fish that eats no pellets and whose food the player has none of comes
        /// first ("피라루쿠는 정어리를 좋아해요 · 상점에서 사요"), then a live food to give (open the lid / pick one up), then the
        /// pellet bags (as before).
        /// </summary>
        static string FeedHint(SaveData d)
        {
            var hungry = d.aquarium.Where(AquaCare.Hungry).ToList();
            var starving = hungry.FirstOrDefault(f => !AquaCare.Eats(f, Diet.Pellet) && !AquaCare.OwnsFoodFor(f));
            if (starving != null && starving.Species != null)
                return $"{starving.Species.name}{Josa(starving.Species.name, "은", "는")} {AquaCare.DietText(AquaCare.DietOf(starving))}를 좋아해요 · 상점에서 사요";
            bool pellets = hungry.Any(f => AquaCare.Eats(f, Diet.Pellet) && AquaCare.Owns(Diet.Pellet));
            if (!pellets)
            {
                foreach (var lf in AquaCare.Live)
                {
                    if (!hungry.Any(f => AquaCare.Eats(f, lf.kind)) || AquaCare.Pieces(lf) <= 0) continue;
                    bool shrimp = lf.kind == Diet.Shrimp;
                    return AquaCare.Stock(lf.id).open
                        ? (shrimp ? "새우를 집어서 수조 위에 놓아요" : "정어리를 집어서 수조 위에 놓아요")
                        : (shrimp ? "생새우 통 뚜껑을 탭해서 열어요" : "아이스박스 뚜껑을 탭해서 열어요");
                }
            }
            bool open = AquaCare.Feeds.Any(f => AquaCare.Stock(f.id).portions > 0.001f);
            bool sealedOnly = !open && AquaCare.AnyFeed;
            return open ? "사료 봉투를 수조 위로 끌어서 먹이를 줘요"
                : sealedOnly ? "새 사료 봉투는 점선을 따라 잘라서 열어요"
                : "상점에서 사료를 사서 먹이를 줘요";
        }

        /// <summary>은 / 는 (이 / 가 ...) after a Korean word: the first when its last syllable has a final consonant.</summary>
        static string Josa(string word, string withFinal, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0 ? withFinal : without;
        }

        void Collect()
        {
            int got = Game.I.CollectIncome();
            if (got <= 0) return;
            Sfx.Play(Sfx.Coin);
            Toast.Show($"관람 수입 +{UIKit.Num(got)} 코인", UIKit.Gold);
            Fx.Burst(Vector2.zero, UIKit.Gold, 18, 5f);
        }

        void Update()
        {
            refreshT -= Time.deltaTime;
            bool busy = (feed != null && feed.FeedingNow) || (clean != null && clean.Busy);
            if (busy == hintPlate.enabled) refreshT = 0f; // (the top hint makes way at once)
            if (refreshT <= 0)
            {
                refreshT = 1f;
                if (++ticks % 5 == 0) AquaCare.Advance(Game.Data, SaveSystem.Now);
                RefreshUI();
            }
            bubbleT -= Time.deltaTime;
            if (bubbleT <= 0)
            {
                bubbleT = 0.18f;
                var b0 = data.Bubble;
                StartCoroutine(Bubble(new Vector2(b0.x + Random.Range(-0.2f, 0.2f), b0.y + 0.07f)));
                if (Random.value < 0.3f) StartCoroutine(Bubble(new Vector2(Random.Range(data.glassL + 1.6f, data.glassR - 1.6f), data.gravelTopY + 0.2f)));
            }
            // a bigger tank bought here (the shop over the scene): once the shop is closed, the new tank is set up
            if (Game.Data.tankLevel > builtLevel && !Dialog.Open && UIKit.ModalCount == 0 && !AquaDecor.ModeOn && (feed == null || !feed.Busy) && (clean == null || !clean.Busy))
            {
                builtLevel = Game.Data.tankLevel;
                AquaTank.Commit();
                Game.I.Save();
                Toast.Show($"{Game.I.Tank.name}{(Game.I.Tank.name.EndsWith("항") || Game.I.Tank.name.EndsWith("관") || Game.I.Tank.name.EndsWith("움") ? "으로" : "로")} 옮겼어요!", UIKit.Gold);
                AquaCare.Log($"tank upgraded to {Game.I.Tank.name}: the scene is set up again");
                SceneFlow.Go("Aquarium");
                return;
            }
            if (PointerInput.WorldPressed && !Dialog.Open && UIKit.ModalCount == 0 && !AquaDecor.ModeOn)
            {
                var w = pv.ScreenToWorld(PointerInput.Position);
                // (a press on a feed bag or a cleaning tool, or while one is carried / cut / held, is theirs)
                if ((feed == null || (!feed.Busy && !feed.HitsBag(w))) && (clean == null || (!clean.Busy && !clean.Hits(w))))
                {
                    var hit = fish.Where(f => f != null).OrderBy(f => (f.Pos - w).sqrMagnitude).FirstOrDefault();
                    if (hit != null && (hit.Pos - w).magnitude < Mathf.Max(1.2f, hit.HalfWidth + 0.4f)) ShowFish(hit);
                    else if (decor != null) decor.PressNormal(w); // (held a moment: the decoration mode with it lifted)
                }
            }
        }

        /// <summary>The cleaning tools and the decorations (the test autopilot).</summary>
        public AquaClean Clean => clean;
        public AquaDecor Decor => decor;

        /// <summary>Opens the income breakdown (the test autopilot; a tap on the income panel).</summary>
        public void ShowIncomeBreakdown() => ShowIncome();

        /// <summary>Opens a kept fish's info window (the test autopilot).</summary>
        public void ShowInfo(CaughtFish cf)
        {
            Resync();
            var tf = fish.FirstOrDefault(f => f != null && f.Data == cf);
            if (tf != null) ShowFish(tf);
        }

        /// <summary>The hint at the top of the screen (the test autopilot).</summary>
        public string TopHint => hintText != null ? hintText.text : "";

        /// <summary>The swimmer of a kept fish (the test autopilot).</summary>
        public TankFish TankOf(CaughtFish cf)
        {
            Resync();
            return fish.FirstOrDefault(f => f != null && f.Data == cf);
        }

        /// <summary>After a test time jump: the fish show how they feel, the panel follows.</summary>
        public void AfterTimeJump()
        {
            Resync();
            if (feed != null) feed.ShowMoods(2.5f);
            RefreshUI();
        }

        /// <summary>Rebuilds the swimmers when the kept fish changed behind the scene's back (the test autopilot adds some).</summary>
        void Resync()
        {
            var list = Game.Data.aquarium;
            if (fish.Count != list.Count || fish.Where((f, i) => f == null || f.Data != list[i]).Any()) Rebuild();
        }

        System.Collections.IEnumerator Bubble(Vector2 p)
        {
            var sr = new GameObject("Bubble").AddComponent<SpriteRenderer>();
            sr.sprite = Art.Ring;
            sr.sortingOrder = 20;
            float s = Random.Range(0.04f, 0.08f);
            sr.transform.localScale = Vector3.one * s;
            sr.color = new Color(0.85f, 0.97f, 1f, 0.8f);
            float t = 0;
            while (p.y < data.surfaceY - 0.2f && sr != null)
            {
                t += Time.deltaTime;
                p += new Vector2(Mathf.Sin(t * 6f) * 0.01f, 2.2f * Time.deltaTime);
                sr.transform.position = new Vector3(Mathf.Round(p.x * 16) / 16, Mathf.Round(p.y * 16) / 16, 0);
                yield return null;
            }
            if (sr != null) Destroy(sr.gameObject);
        }

        static readonly Color Muted = new Color32(0x6a, 0x5a, 0x40, 0xff);
        const string Green = "#2f8a2f", Red = "#c0392b";

        static string Duration(long secs)
        {
            secs = System.Math.Max(0, secs);
            long d = secs / 86400, h = secs % 86400 / 3600, m = secs % 3600 / 60;
            if (d > 0) return $"{d}일 {h}시간";
            if (h > 0) return $"{h}시간 {m}분";
            return m > 0 ? $"{m}분" : "방금 들어왔어요";
        }

        const float RowPitch = 32f;

        static void CareRow(Transform box, int i, Sprite icon, string label, string value)
        {
            float y = -8 - i * RowPitch;
            if (icon != null)
            {
                var ic = UIKit.Img(box, icon, new Vector2(24, 24), "Icon");
                ic.rectTransform.At(new Vector2(0, 1), new Vector2(14, y - 2), new Vector2(24, 24), new Vector2(0, 1));
            }
            var l = UIKit.Label(box, label, 16, Muted, TextAnchor.MiddleLeft, false, "Key");
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            l.rectTransform.At(new Vector2(0, 1), new Vector2(46, y), new Vector2(110, 28), new Vector2(0, 1));
            var v = UIKit.Label(box, value, 20, UIKit.Ink, TextAnchor.MiddleLeft, false, "Value");
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            v.rectTransform.At(new Vector2(0, 1), new Vector2(150, y), new Vector2(396, 28), new Vector2(0, 1));
        }

        /// <summary>
        /// The favourite food row: its icon (two overlapped for an omnivore), the food names; hungry and none of it
        /// owned: "이 물고기는 정어리를 좋아해요" in red. None owned: a 상점 button (the shop's 사료 section, at that food).
        /// </summary>
        void DietRow(Transform box, int i, CaughtFish cf, bool hungry, System.Action close)
        {
            float y = -8 - i * RowPitch;
            var kinds = AquaCare.KindsOf(AquaCare.DietOf(cf)).ToList();
            if (kinds.Count == 1)
            {
                var ic = UIKit.Img(box, Art.UI(AquaCare.KindIcon(kinds[0])), new Vector2(24, 24), "DietIcon");
                ic.rectTransform.At(new Vector2(0, 1), new Vector2(14, y - 2), new Vector2(24, 24), new Vector2(0, 1));
            }
            else
            {
                // two foods: two smaller icons overlapped in the icon column
                var a = UIKit.Img(box, Art.UI(AquaCare.KindIcon(kinds[0])), new Vector2(18, 18), "DietIcon");
                a.rectTransform.At(new Vector2(0, 1), new Vector2(10, y), new Vector2(18, 18), new Vector2(0, 1));
                var b = UIKit.Img(box, Art.UI(AquaCare.KindIcon(kinds[1])), new Vector2(18, 18), "DietIcon2");
                b.rectTransform.At(new Vector2(0, 1), new Vector2(22, y - 10), new Vector2(18, 18), new Vector2(0, 1));
            }
            var l = UIKit.Label(box, "좋아하는 먹이", 16, Muted, TextAnchor.MiddleLeft, false, "Key");
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            l.rectTransform.At(new Vector2(0, 1), new Vector2(46, y), new Vector2(110, 28), new Vector2(0, 1));
            bool owned = AquaCare.OwnsFoodFor(cf);
            bool asks = hungry && !owned;
            string value = asks ? AquaCare.LikesText(cf)
                : string.Join(" · ", kinds.Select(k => AquaCare.KindName(k) + (AquaCare.Owns(k) ? "" : " <color=#8a7a60>(없음)</color>")));
            // (the key is a little longer than the others': the value starts a bit further right)
            var v = UIKit.Label(box, value, asks ? 16 : 20, asks ? (Color)new Color32(0xc0, 0x39, 0x2b, 0xff) : UIKit.Ink, TextAnchor.MiddleLeft, false, "DietValue");
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            v.rectTransform.At(new Vector2(0, 1), new Vector2(162, y), new Vector2(290, 28), new Vector2(0, 1));
            if (owned) return;
            var want = kinds.Select(k => k == Diet.Pellet ? AquaCare.Feeds[0] : AquaCare.LiveOf(k)).FirstOrDefault(f => f != null);
            var shop = UIKit.Button(box, "상점", "blue", () =>
            {
                close();
                ShopUI.TankFeed = true;
                ShopUI.Focus = want?.id;
                ShopUI.Open(uiRoot, ItemKind.Tank);
            }, new Vector2(96, 36), 20, null, "DietShop");
            shop.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-10, y + 4), new Vector2(96, 36), new Vector2(1, 1));
        }

        void ShowFish(TankFish tf)
        {
            AquaCare.Advance(Game.Data, SaveSystem.Now);
            var cf = tf.Data;
            var sp = cf.Species;
            Sfx.Play(Sfx.Bubble);
            var w = Dialog.Window(sp.name, new Vector2(640, 486), out var close);
            var plate = UIKit.Panel(w, "panel_paper", new Color(0.62f, 0.86f, 1f), "Plate");
            plate.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(560, 100), new Vector2(0.5f, 1));
            var spr = Art.Fish(sp.id, 0);
            float scale = Mathf.Max(1, Mathf.Floor(Mathf.Min(460f / spr.rect.width, 88f / spr.rect.height) / UIKit.Px));
            UIKit.PixelImg(plate.transform, spr, scale, "Fish").gameObject.AddComponent<FishWiggle>().Init(sp.id);
            var stage = GameDatabase.GetStage(cf.stageId);
            // its size class and the 칸 it takes (over the tank's limits: in red, why)
            int cls = AquaTank.ClassOf(cf);
            bool overTank = AquaTank.Over(Game.Data).Contains(cf);
            var tankDef = Game.I.Tank;
            string why = !overTank ? "" : cls > tankDef.maxClass ? " · 이 수조엔 너무 커요" : " · 자리 넘침";
            string sizeClass = $"<color={(overTank ? Red : "#1f6fc4")}>{AquaTank.ClassNames[cls]} {AquaTank.ClassSpace[cls]}칸{why}</color>";
            var t = UIKit.Label(w,
                $"<color=#{ColorUtility.ToHtmlStringRGB(RarityInfo.InkColor(sp.rarity))}>{RarityInfo.Name(sp.rarity)}</color>  ·  {cf.sizeCm:0.0}cm  ·  {CatchPopup.FormatKg(cf.weightKg)}  ·  {sizeClass}",
                20, UIKit.Ink, TextAnchor.MiddleCenter, false, "SizeClass");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -148), new Vector2(580, 32), new Vector2(0.5f, 1));

            // care: days kept, growth, fullness, price, income, favourite food
            var box = UIKit.Panel(w, "panel_paper", null, "Care");
            box.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -184), new Vector2(560, 16 + 6 * RowPitch - 4), new Vector2(0.5f, 1));
            long now = SaveSystem.Now;
            CareRow(box.transform, 0, Art.UI("icon_tank"), "사육 기간", Duration(now - cf.addedAt) + (stage != null ? $" <size=16>· {stage.name}에서 잡음</size>" : ""));
            float grown = AquaCare.GrownCm(cf);
            float pct = (AquaCare.SizeRatio(cf) - 1f) * 100f;
            bool atMax = sp != null && grown >= sp.maxCm - 0.01f && cf.fedDays > 0.01f;
            string growth = cf.fedDays < 0.005f
                ? $"+0.0cm (+0.0%) <size=16>· 먹이를 먹으면 자라요</size>"
                : $"<color={Green}>+{grown - cf.baseCm:0.0}cm (+{pct:0.0}%)</color> <size=16>· 처음 {cf.baseCm:0.0}cm{(atMax ? " · 최대 크기" : "")}</size>";
            CareRow(box.transform, 1, Art.UI("icon_growth"), "성장", growth);
            bool hungry = AquaCare.Hungry(cf);
            float hrs = AquaCare.HoursToHungry(cf);
            string left = hrs >= 1f ? $"{Mathf.FloorToInt(hrs)}시간" : $"{Mathf.Max(1, Mathf.FloorToInt(hrs * 60f))}분";
            string full = hungry ? $"<color={Red}>배고파요! 성장이 멈췄어요</color> <size=16>({cf.fullness * 100f:0}%)</size>"
                : AquaCare.Full(cf) ? $"배불러요! {cf.fullness * 100f:0}% <size=16>· {left} 뒤 배고파져요</size>"
                : $"{cf.fullness * 100f:0}% <size=16>· {left} 뒤 배고파져요</size>";
            CareRow(box.transform, 2, Art.UI(hungry ? "icon_hungry" : "icon_full"), "배부름", full);
            int diff = cf.value - cf.baseValue;
            string price = $"{UIKit.Num(cf.value)} 코인 <size=16>" + (diff != 0
                ? $"<color={Green}>(잡았을 때 {UIKit.Num(cf.baseValue)}, {(diff > 0 ? "+" : "")}{UIKit.Num(diff)})</color></size>"
                : "(잡았을 때 그대로)</size>");
            CareRow(box.transform, 3, Art.UI("coin"), "판매가", price);
            // (the tank's share too: the decorations' bonus and the light's favour, the dirt's cost)
            float tm = AquaTank.Mult(Game.Data, cf);
            string tank = Mathf.Abs(tm - 1f) >= 0.005f ? $" · <color={(tm >= 1f ? Green : Red)}>수조 {(tm >= 1f ? "+" : "-")}{Mathf.Abs(tm - 1f) * 100f:0}%</color>" : "";
            string inc = $"{AquaCare.IncomeF(cf) * tm:0.0} 코인/분 <size=16>" + (hungry ? $"<color={Red}>(배고픔 -30%{tank})</color></size>" : $"<color={Green}>(배부름 +20%</color>{tank}<color={Green}>)</color></size>");
            CareRow(box.transform, 4, Art.UI("icon_eye"), "관람 수입", inc);
            DietRow(box.transform, 5, cf, hungry, close);
            AquaCare.Log($"info {cf.speciesId}: {AquaTank.ClassNames[cls]} {AquaTank.ClassSpace[cls]}칸{(overTank ? " (over the tank)" : "")}, kept {Duration(now - cf.addedAt)}, likes {AquaCare.DietText(AquaCare.DietOf(cf))} (owned {AquaCare.OwnsFoodFor(cf)}) | {AquaCare.Describe(cf)}");

            var sell = UIKit.Button(w, $"판매 +{UIKit.Num(cf.value)}", "yellow", () =>
            {
                int v = Game.I.SellFromAquarium(cf.uid);
                Sfx.Play(Sfx.Coin);
                Toast.Show($"{sp.name} 판매 +{UIKit.Num(v)} 코인", UIKit.Gold);
                AquaCare.Log($"sold {cf.speciesId} for {v} (caught {cf.baseValue}), coins {Game.Data.coins}");
                close();
                Rebuild();
            }, new Vector2(220, 64), 21, Art.UI("coin"));
            sell.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-120, 22), new Vector2(220, 64), new Vector2(0.5f, 0));
            var keep = UIKit.Button(w, "계속 기르기", "blue", close, new Vector2(200, 64), 21);
            keep.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(120, 22), new Vector2(200, 64), new Vector2(0.5f, 0));
        }
    }

    /// <summary>
    /// Side-view fish swimming lazily inside the tank bounds. A little bigger as it grows; slower when hungry; while there
    /// is food of its diet in the water and it is not full it goes for it (faster when hungry): the nearest pellet, or a
    /// dropped shrimp / sardine by its <see cref="FeedStyle"/>: Grab dashes to it in mid-water, Bottom waits for it down
    /// on the gravel, Surge (the big ones) comes up under it and lunges through the surface to gulp it. Food it does not
    /// eat it ignores. A hungry fish now and then shows the hungry icon, then its favourite food.
    /// </summary>
    public class TankFish : MonoBehaviour
    {
        public CaughtFish Data { get; private set; }
        public Vector2 Pos => pos;
        public float HalfWidth => (sr.sprite != null ? sr.sprite.rect.width / 32f : 1f) * scale;
        public float HalfHeight => (sr.sprite != null ? sr.sprite.rect.height / 32f : 0.5f) * scale;
        /// <summary>Where it bites: the front of the head.</summary>
        public Vector2 Mouth => pos + new Vector2((sr.flipX ? -1f : 1f) * HalfWidth * 0.85f, 0f);
        public bool FacingLeft => sr.flipX;
        public FeedStyle Style => Data.Species != null ? Data.Species.feedStyle : FeedStyle.Grab;
        /// <summary>1 rising through the surface for a piece, 2 sinking back after the gulp, 0 not.</summary>
        public int Lunge => lunge;
        /// <summary>Swim speed / tail beat factor (AquaTank.Lively: the bubbler, the dirt).</summary>
        public float Lively { get; private set; } = 1f;
        public Color Tint => sr != null ? sr.color : Color.white;

        SpriteRenderer sr, mood;
        Sprite[] frames;
        AquaLayout box;
        Vector2 pos, target;
        float speed, t, pause, scale = 1f, moodT, nextMood;
        bool chasing;
        Sprite moodNext;
        float moodNextT;
        // the surge: the piece it is gulping, rising (1) / sinking back (2)
        AquaFeed.Piece prey;
        int lunge;
        float lungeT, lungeTop;
        Vector2 fallFrom;

        float ChaseMinY => box.ChaseMinY;   // (-3.35 in the 중형 수조)

        /// <summary>The sprite scale by its real length (AquaTank.FishScale), its drawn length, the sprite's own width.</summary>
        public float Scale => scale;
        public float DrawnPx => sr != null && sr.sprite != null ? sr.sprite.rect.width * scale : 0f;
        public int SpritePx => sr != null && sr.sprite != null ? (int)sr.sprite.rect.width : 0;
        const float SurgeZone = 2.4f;   // a big fish surges for a piece this close under the surface (deeper: it grabs it)

        public void Init(CaughtFish cf, AquaLayout data, int index)
        {
            Data = cf;
            box = data;
            frames = new[] { Art.Fish(cf.speciesId, 0), Art.Fish(cf.speciesId, 1) };
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            sr.sortingOrder = 10 + index % 20;
            mood = new GameObject("Mood_" + cf.speciesId).AddComponent<SpriteRenderer>();
            mood.sortingOrder = 41;
            mood.enabled = false;
            scale = AquaTank.FishScale(cf, frames[0], box);
            var sp = cf.Species;
            speed = Mathf.Clamp(sp.speed * 0.35f, 0.6f, 1.8f);
            pos = RandomPoint(sp);
            target = RandomPoint(sp);
            t = Random.value * 3f;
            nextMood = Random.Range(3f, 8f);
        }

        void OnDestroy()
        {
            if (mood != null) Destroy(mood.gameObject);
        }

        /// <summary>A mood bubble (배불러요 / 배고파요 icon) over it for a moment, then <paramref name="then"/> if given; null hides it.</summary>
        public void ShowMood(Sprite s, float time, Sprite then = null, float thenTime = 0f)
        {
            if (mood == null) return;
            moodNext = then;
            moodNextT = thenTime;
            if (s == null)
            {
                moodT = 0f;
                mood.enabled = false;
                return;
            }
            mood.sprite = s;
            moodT = time;
        }

        /// <summary>Puts it somewhere in the tank (the test autopilot).</summary>
        public void Place(Vector2 p, bool faceLeft)
        {
            pos = p;
            target = p;
            pause = 2f;
            sr.flipX = faceLeft;
        }

        /// <summary>The icon of a food it eats, one the player has if any (the hungry fish's thought bubble).</summary>
        Sprite FoodIcon()
        {
            var kinds = AquaCare.KindsOf(AquaCare.DietOf(Data)).ToList();
            var k = kinds.FirstOrDefault(AquaCare.Owns);
            return Art.UI(AquaCare.KindIcon(k != Diet.None ? k : kinds[0]));
        }

        void MoveTo(Vector2 goal, float sp, float dt)
        {
            var d = goal - pos;
            if (d.sqrMagnitude > 1e-6f) pos += d.normalized * Mathf.Min(sp * dt, d.magnitude);
        }

        /// <summary>A dropped shrimp / sardine of its diet: by its feed style.</summary>
        void ChasePiece(AquaFeed feed, AquaFeed.Piece p, bool hungry, float dt)
        {
            pause = 0f;
            chasing = true;
            if (Mathf.Abs(p.Pos.x - pos.x) > 0.12f) sr.flipX = p.Pos.x < pos.x;
            float dir = sr.flipX ? -1f : 1f, mo = HalfWidth * 0.85f;
            float fast = hungry ? 1.2f : 1f;
            var style = Style;
            if (style == FeedStyle.Surge && (p.state == 0 || p.Pos.y > box.surfaceY - SurgeZone))
            {
                // under it, then up through the surface
                var goal = new Vector2(Mathf.Clamp(p.Pos.x - dir * mo, box.swimMinX, box.swimMaxX),
                    Mathf.Clamp(Mathf.Min(p.Pos.y, box.surfaceY) - 1.5f, box.swimMinY, box.surfaceY - 1.2f));
                MoveTo(goal, Mathf.Max(4.5f, speed * 3.5f) * fast, dt);
                if (p.state >= 1 && Mathf.Abs(Mouth.x - p.Pos.x) < 0.7f && Mouth.y < p.Pos.y + 0.3f && Mathf.Abs(pos.y - goal.y) < 1f)
                {
                    prey = p;
                    feed.Claim(p, this);
                    lunge = 1;
                    lungeT = 0f;
                    lungeTop = box.surfaceY - HalfHeight * 0.8f; // (its back just breaks the surface)
                }
                return;
            }
            float minY = Mathf.Max(ChaseMinY, AquaFeed.Gravel + HalfHeight * 0.6f);
            if (style == FeedStyle.Bottom && p.state == 1 && p.Pos.y > minY + 0.8f)
            {
                // a bottom feeder waits for it down there
                MoveTo(new Vector2(Mathf.Clamp(p.Pos.x - dir * mo, box.swimMinX, box.swimMaxX), minY + 0.1f), speed * 1.6f * fast, dt);
                return;
            }
            // a dash to it, mouth first
            var g = p.Pos - new Vector2(dir * mo, 0f);
            g.x = Mathf.Clamp(g.x, box.swimMinX, box.swimMaxX);
            g.y = Mathf.Clamp(g.y, minY, box.surfaceY - 0.3f);
            MoveTo(g, Mathf.Max(3f, speed * 3f) * fast, dt);
            if ((p.Pos - Mouth).magnitude < Mathf.Max(0.36f, HalfHeight * 0.45f)) feed.Grab(this, p);
        }

        /// <summary>The surge: up through the surface with the piece, one gulp, then it sinks back.</summary>
        void UpdateLunge(AquaFeed feed, float dt)
        {
            chasing = true;
            lungeT += dt;
            if (lunge == 1)
            {
                bool gone = feed == null || prey == null || prey.dead || prey.state == 3 && prey.holder != this;
                if (gone || lungeT > 1.5f)
                {
                    if (feed != null) feed.Unclaim(prey, this);
                    prey = null;
                    lunge = 2;
                    lungeT = 0f;
                    fallFrom = pos;
                    return;
                }
                float dir = sr.flipX ? -1f : 1f;
                var top = new Vector2(prey.state == 3 ? pos.x : Mathf.Clamp(prey.Pos.x - dir * HalfWidth * 0.85f, box.swimMinX, box.swimMaxX), lungeTop);
                MoveTo(top, 9f, dt);
                if (prey.state != 3 && ((prey.Pos - Mouth).magnitude < Mathf.Max(0.7f, HalfHeight) || pos.y > prey.Pos.y)) feed.Take(this, prey);
                if (pos.y >= lungeTop - 0.02f)
                {
                    // at the top: it has it, or it is just over its mouth (a big head breaks the surface under it)
                    if (prey.state != 3 && Mathf.Abs(prey.Pos.x - Mouth.x) < 1f && prey.Pos.y > Mouth.y - 0.8f) feed.Take(this, prey);
                    if (prey.state == 3 && prey.holder == this) feed.Gulp(this, prey);
                    else feed.Unclaim(prey, this);
                    prey = null;
                    lunge = 2;
                    lungeT = 0f;
                    fallFrom = pos;
                }
            }
            else
            {
                float k = Mathf.Clamp01(lungeT / 0.55f);
                pos.y = fallFrom.y - 1.5f * (1f - (1f - k) * (1f - k));
                if (k >= 1f)
                {
                    lunge = 0;
                    target = RandomPoint(Data.Species);
                }
            }
        }

        Vector2 RandomPoint(FishSpecies sp)
        {
            float hw = HalfWidth;
            // bottom dwellers stay low, surface fish stay high
            float depthBias = Mathf.InverseLerp(0f, 8f, (sp.depthMin + sp.depthMax) * 0.5f);
            float y = Mathf.Lerp(box.swimMaxY, box.swimMinY, Mathf.Clamp01(depthBias + Random.Range(-0.35f, 0.35f)));
            // (a big fish keeps its whole body in the water, over the gravel)
            float lo = Mathf.Max(box.swimMinY, box.gravelTopY + HalfHeight + 0.1f), hi = Mathf.Min(box.swimMaxY, box.surfaceY - HalfHeight - 0.2f);
            y = lo <= hi ? Mathf.Clamp(y, lo, hi) : (lo + hi) * 0.5f;
            float x0 = box.swimMinX + hw, x1 = box.swimMaxX - hw;
            return new Vector2(x0 <= x1 ? Random.Range(x0, x1) : (box.swimMinX + box.swimMaxX) * 0.5f, y);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            scale = AquaTank.FishScale(Data, frames[0], box);
            transform.localScale = new Vector3(scale, scale, 1f);
            bool hungry = AquaCare.Hungry(Data);
            // the tank's mood: livelier with the bubbler, a little sluggish in dirty water; the light's colour, dulled by dirt
            Lively = AquaTank.Lively(Game.Data, Data, AquaTank.LookBubbler, AquaTank.LookFilth);
            sr.color = AquaTank.FishTint;
            var feed = AquaFeed.Current;
            bool full = AquaCare.Full(Data);
            var piece = lunge == 0 && feed != null && !full ? feed.PieceFor(this) : null;
            // pellets only for the fish that eat them (a full one ignores food)
            var food = lunge == 0 && piece == null && feed != null && !full && AquaCare.Eats(Data, Diet.Pellet) ? feed.NearestPellet(pos) : null;
            if (lunge > 0) UpdateLunge(feed, dt);
            else if (piece != null) ChasePiece(feed, piece, hungry, dt);
            else if (food != null)
            {
                // food in the water: to the nearest pellet, mouth first
                pause = 0f;
                chasing = true;
                if (Mathf.Abs(food.Pos.x - pos.x) > 0.12f) sr.flipX = food.Pos.x < pos.x;
                var goal = food.Pos - new Vector2((sr.flipX ? -1f : 1f) * HalfWidth * 0.85f, 0f);
                goal.x = Mathf.Clamp(goal.x, box.swimMinX, box.swimMaxX);
                goal.y = Mathf.Clamp(goal.y, ChaseMinY, box.surfaceY - 0.3f);
                var d = goal - pos;
                float sp = speed * (hungry ? 2.4f : 1.8f);
                if (d.sqrMagnitude > 1e-6f) pos += d.normalized * Mathf.Min(sp * dt, d.magnitude);
                if ((food.Pos - Mouth).magnitude < 0.34f) feed.Eat(this, food);
            }
            else
            {
                if (chasing)
                {
                    chasing = false;
                    target = RandomPoint(Data.Species);
                }
                if (pause > 0)
                {
                    pause -= dt;
                }
                else
                {
                    var d = target - pos;
                    if (d.magnitude < 0.2f)
                    {
                        target = RandomPoint(Data.Species);
                        if (Random.value < (hungry ? 0.5f : 0.3f)) pause = Random.Range(0.8f, 2.5f);
                    }
                    else
                    {
                        // hungry fish drift about slowly (livelier ones swim faster)
                        pos += d.normalized * speed * (hungry ? 0.55f : 1f) * Lively * dt * Mathf.Clamp01(d.magnitude);
                        if (Mathf.Abs(d.x) > 0.05f) sr.flipX = d.x < 0;
                    }
                }
            }
            float beat = (lunge == 1 ? 0.08f : pause > 0 ? 0.6f : chasing ? 0.18f : hungry ? 0.45f : 0.3f) / Mathf.Max(0.5f, Lively);
            sr.sprite = frames[((int)(t / beat)) % 2];
            var p = pos + new Vector2(0, chasing ? 0f : Mathf.Sin(t * 1.3f) * 0.08f);
            transform.position = new Vector3(Mathf.Round(p.x * 16) / 16, Mathf.Round(p.y * 16) / 16, 0);

            // now and then a hungry fish says so, then thinks of its food (not while food is coming)
            if (hungry && feed != null && !feed.FeedingNow)
            {
                nextMood -= dt;
                if (nextMood <= 0f)
                {
                    ShowMood(feed.HungryIcon, 1.6f, FoodIcon(), 1.3f);
                    nextMood = Random.Range(6f, 10f);
                }
            }
            moodT -= dt;
            if (moodT <= 0f && moodNext != null)
            {
                mood.sprite = moodNext;
                moodT = moodNextT;
                moodNext = null;
            }
            mood.enabled = moodT > 0f && mood.sprite != null;
            if (mood.enabled)
            {
                var m = pos + new Vector2(0f, HalfHeight + 0.6f + (((int)(t * 2f)) % 2) / 16f);
                mood.transform.position = new Vector3(Mathf.Round(m.x * 16) / 16, Mathf.Round(m.y * 16) / 16, 0);
            }
        }
    }
}
