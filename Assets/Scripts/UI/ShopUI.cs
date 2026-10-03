using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>Tackle shop: rods, reels, lines, hooks, baits and aquarium upgrades.</summary>
    public static class ShopUI
    {
        static readonly (ItemKind kind, string label)[] Tabs =
        {
            (ItemKind.Rod, "낚싯대"), (ItemKind.Reel, "릴"), (ItemKind.Line, "낚싯줄"), (ItemKind.Hook, "바늘"), (ItemKind.Bait, "미끼"), (ItemKind.Tank, "수조"),
        };

        public static void Open(Transform parent, ItemKind start = ItemKind.Rod)
        {
            RectTransform dim = UIKit.Modal(parent);
            var win = UIKit.Panel(dim, "panel_wood", null, "Shop").rectTransform;
            win.At(new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(860, 480));
            win.GetComponent<Image>().raycastTarget = true;
            Tween.Pop(win, 0.8f);
            UIKit.Ribbon(win, "낚시 용품점");
            var close = UIKit.IconButton(win, Art.UI("icon_back"), "red", () => Object.Destroy(dim.gameObject), 50);
            close.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-14, -14), new Vector2(50, 50));

            var listHolder = UIKit.Rect(win, "List").Fill(24, 24, 104, 24);
            var tabBtns = new List<Button>();
            System.Action<ItemKind> show = null;
            show = kind =>
            {
                for (int i = 0; i < Tabs.Length; i++) tabBtns[i].SetStyle(Tabs[i].kind == kind ? "green" : "grey");
                UIKit.Clear(listHolder);
                float top = 0f;
                if (kind == ItemKind.Bait)
                {
                    // the bait tab has two sub-tabs: natural baits | lures
                    top = 50f;
                    for (int i = 0; i < 2; i++)
                    {
                        bool lures = i == 1;
                        var sb = UIKit.Button(listHolder, lures ? "루어" : "미끼", BaitLures == lures ? "blue" : "grey", () =>
                        {
                            BaitLures = lures;
                            show(ItemKind.Bait);
                        }, new Vector2(120, 42), 20, name: lures ? "SubLures" : "SubBaits");
                        sb.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(4 + i * 128, 0), new Vector2(120, 42), new Vector2(0, 1));
                    }
                }
                else if (kind == ItemKind.Tank)
                {
                    // the tank tab has four sub-tabs: feed | decorations | cleaning tools | tank upgrades
                    top = 50f;
                    string[] labels = { "사료", "장식", "청소", "수조 확장" };
                    string[] names = { "SubFeed", "SubDecor", "SubClean", "SubTanks" };
                    for (int i = 0; i < labels.Length; i++)
                    {
                        int sub = i;
                        var sb = UIKit.Button(listHolder, labels[i], TankSub == sub ? "blue" : "grey", () =>
                        {
                            TankSub = sub;
                            show(ItemKind.Tank);
                        }, new Vector2(150, 42), 20, name: names[i]);
                        sb.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(4 + i * 158, 0), new Vector2(150, 42), new Vector2(0, 1));
                    }
                }
                var sr = UIKit.ScrollList(listHolder, out var content, 6);
                sr.GetComponent<RectTransform>().Fill(0, 0, top, 0);
                if (kind == ItemKind.Tank && TankSub != SubTanks)
                {
                    System.Action<string> again = id =>
                    {
                        Focus = id;
                        show(kind);
                    };
                    if (TankSub == SubFeed)
                    {
                        // the pellet bags, then the live food (생새우, 정어리); Focus scrolls to one (a tap on its empty slot)
                        foreach (var f in AquaCare.All) FeedRow(content, f, () => again(f.id));
                    }
                    else if (TankSub == SubDecor)
                    {
                        // the decorations by where they stand: lights, back, mid, front
                        DecorHeader(content);
                        foreach (var k in new[] { DecorKind.Light, DecorKind.Back, DecorKind.Mid, DecorKind.Front })
                        {
                            TextHeader(content, "Group_" + k, AquaTank.KindName(k) + (k == DecorKind.Light ? " (수조 위)" : " 자리"));
                            foreach (var def in AquaTank.Decors.Where(x => x.kind == k)) DecorRow(content, def, () => again(def.id));
                        }
                    }
                    else
                        foreach (var t in AquaTank.Tools) ToolRow(content, t, () => again(t.id));
                    if (!string.IsNullOrEmpty(Focus)) ScrollTo(sr, content, Focus);
                    Focus = null;
                }
                else if (kind == ItemKind.Bait && BaitLures)
                {
                    // lures grouped by action: 감기 / 저킹 / 수면 / 바닥 / 수직
                    foreach (var a in LureInfo.Order)
                    {
                        var group = GameDatabase.Baits.Where(b => b.isLure && b.action == a).ToList();
                        if (group.Count == 0) continue;
                        GroupHeader(content, a);
                        foreach (var b in group) Row(content, b, () => show(kind));
                    }
                }
                else
                {
                    if (kind == ItemKind.Tank) TankHeader(content);
                    if (kind == ItemKind.Hook) TextHeader(content, "HookNote", "미끼 채비에 쓰는 바늘 · 루어엔 안 써요 · 줄이 끊기면 1개 잃어요");
                    foreach (var item in Items(kind))
                        if (!(item is BaitDef bd && bd.isLure)) Row(content, item, () => show(kind));
                }
            };
            for (int i = 0; i < Tabs.Length; i++)
            {
                var k = Tabs[i].kind;
                var b = UIKit.Button(win, Tabs[i].label, "grey", () => show(k), new Vector2(128, 50), 20);
                b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(26 + i * 134, -48), new Vector2(128, 50));
                tabBtns.Add(b);
            }
            show(start);
        }

        /// <summary>The bait tab's sub-tab: lures (true) or natural baits (false); kept while the game runs.</summary>
        public static bool BaitLures;

        public const int SubFeed = 0, SubDecor = 1, SubClean = 2, SubTanks = 3;

        /// <summary>The tank tab's sub-tab: 사료 / 장식 / 청소 / 수조 확장 (Sub*); kept while the game runs.</summary>
        public static int TankSub = SubFeed;

        /// <summary>The tank tab's sub-tab is the feed (true) or the tank upgrades (false).</summary>
        public static bool TankFeed
        {
            get => TankSub == SubFeed;
            set => TankSub = value ? SubFeed : SubTanks;
        }

        /// <summary>The food row the feed sub-tab scrolls to when it opens next (then cleared).</summary>
        public static string Focus;

        /// <summary>Scrolls the list so the row named <paramref name="id"/> shows (at the top, or the list's end).</summary>
        static void ScrollTo(ScrollRect sr, RectTransform content, string id)
        {
            Tween.After(0.02f, () =>
            {
                if (sr == null || content == null) return;
                Canvas.ForceUpdateCanvases();
                var row = content.Find(id) as RectTransform;
                if (row == null) return;
                float range = content.rect.height - ((RectTransform)sr.viewport).rect.height;
                if (range <= 1f) return;
                var corners = new Vector3[4];
                row.GetWorldCorners(corners);
                float top = -content.InverseTransformPoint(corners[1]).y; // (below the content's top edge)
                sr.verticalNormalizedPosition = 1f - Mathf.Clamp01((top - 6f) / range);
            });
        }

        /// <summary>
        /// A food: what one purchase gives, how full it keeps a fish, how it is given, who eats it (and how many of the
        /// kept fish do), what is owned.
        /// </summary>
        static void FeedRow(RectTransform content, FeedDef f, System.Action refresh)
        {
            var ink2 = new Color32(0x3a, 0x5a, 0x8a, 0xff);
            var row = UIKit.Panel(content, "panel_paper", null, f.id);
            row.Layout(104);
            var slot = UIKit.Panel(row.transform, "slot", null, "Slot");
            slot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(80, 80), new Vector2(0, 0.5f));
            UIKit.Img(slot.transform, Art.Item(f.id), new Vector2(64, 64));
            string pack = f.live ? $"{f.piecesPerBuy}마리" : $"봉투 {f.bagsPerBuy}개";
            var name = UIKit.Label(row.transform, $"{f.name}  <size=16><color=#3a5a8a>{pack}</color></size>", 22, UIKit.Ink, TextAnchor.UpperLeft, false);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.rectTransform.Fill(102, 230, 8, 50);
            var icon = UIKit.Img(row.transform, Art.UI(AquaCare.KindIcon(f.kind)), new Vector2(24, 24), "Diet");
            icon.rectTransform.At(new Vector2(0, 1), new Vector2(104 + name.preferredWidth + 8, -9), new Vector2(24, 24), new Vector2(0, 1));
            string stat = f.live
                ? (f.fill >= 1f ? "한 마리 = 큰 물고기 한 끼 (배부름 100%)" : $"한 마리 = 배부름 {f.fill * 100f:0}%") + $" · {f.fullHours:0}시간 · 수입 +20%"
                : $"봉투당 {f.portions}회분 · 배부름 {f.fullHours:0}시간" + (f.growth > 1f ? $" · 성장 +{(f.growth - 1f) * 100f:0}%" : "") + " · 수입 +20%";
            var st = UIKit.Label(row.transform, stat, 15, ink2, TextAnchor.UpperLeft, false);
            st.horizontalOverflow = HorizontalWrapMode.Overflow;
            st.rectTransform.Fill(102, 200, 36, 30);
            var desc = UIKit.Label(row.transform, f.desc, 14, new Color32(0x6a, 0x5a, 0x40, 0xff), TextAnchor.UpperLeft, false);
            desc.horizontalOverflow = HorizontalWrapMode.Overflow;
            desc.rectTransform.Fill(102, 200, 58, 6);
            // who eats it, and how many of the kept fish do
            int mine = Game.Data.aquarium.Count(c => AquaCare.Eats(c, f.kind));
            string who = $"먹는 물고기: {f.who}" + (mine > 0 ? $"  <color=#2f8a2f>· 내 수조 {mine}마리</color>" : "  <color=#8a7a60>· 내 수조 0마리</color>");
            var wl = UIKit.Label(row.transform, who, 14, new Color32(0x6a, 0x5a, 0x40, 0xff), TextAnchor.UpperLeft, false, "Who");
            wl.horizontalOverflow = HorizontalWrapMode.Overflow;
            wl.rectTransform.Fill(102, 200, 78, 4);
            var btn = UIKit.Button(row.transform, UIKit.Num(f.price), "yellow", () =>
            {
                if (AquaCare.Buy(f))
                {
                    Sfx.Play(Sfx.Coin);
                    Toast.Show(f.live ? $"{f.name} {f.piecesPerBuy}마리 구매! (보유 {AquaCare.Pieces(f)}마리)"
                        : $"{f.name} 봉투 {f.bagsPerBuy}개 구매! (보유 {AquaCare.Bags(f)}봉투)", UIKit.Gold);
                }
                else
                {
                    Sfx.Play(Sfx.Error);
                    Toast.Show("코인이 부족해요", UIKit.Bad);
                }
                refresh();
            }, new Vector2(170, 60), 21, Art.UI("coin"), "Buy");
            btn.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-14, 8), new Vector2(170, 60), new Vector2(1, 0.5f));
            var s = AquaCare.Stock(f.id);
            string own = f.live
                ? (s.pieces > 0 ? $"보유 {s.pieces}마리" : "보유 없음")
                : AquaCare.Bags(f) > 0 ? $"보유 {AquaCare.Bags(f)}봉투" + (s.portions > 0.001f ? $" (뜯은 봉투 {Mathf.CeilToInt(s.portions)}회분 남음)" : "") : "보유 없음";
            var cnt = UIKit.Label(row.transform, own, 14, ink2, TextAnchor.LowerRight, false);
            cnt.horizontalOverflow = HorizontalWrapMode.Overflow;
            cnt.rectTransform.At(new Vector2(1, 0), new Vector2(-16, 6), new Vector2(200, 20), new Vector2(1, 0));
        }

        static readonly Color InkBlue = new Color32(0x3a, 0x5a, 0x8a, 0xff);
        static readonly Color InkBrown = new Color32(0x6a, 0x5a, 0x40, 0xff);
        static readonly Color InkGreen = new Color32(0x2f, 0x8a, 0x2f, 0xff);

        /// <summary>The tank now: its 칸 used, the size classes and the 칸 each takes.</summary>
        static void TankHeader(RectTransform content)
        {
            var d = Game.Data;
            var t = Game.I.Tank;
            int used = AquaTank.Used(d), over = AquaTank.Over(d).Count;
            var row = UIKit.Panel(content, "panel_paper", new Color(0.86f, 0.95f, 1f), "TankSummary");
            row.Layout(64);
            string state = over > 0 ? $" <color=#c0392b>(넘친 물고기 {over}마리 · 새로 넣을 수 없어요)</color>" : "";
            var a = UIKit.Label(row.transform, $"지금 수조: {t.name}  <color=#2f8a2f>{used}/{t.capacity}칸</color>{state}", 18, UIKit.Ink, TextAnchor.UpperLeft, false, "Summary");
            a.horizontalOverflow = HorizontalWrapMode.Overflow;
            a.rectTransform.Fill(14, 10, 6, 30);
            var b = UIKit.Label(row.transform, "크기별 칸: 소형 40cm 미만 1칸 · 중형 ~100cm 2칸 · 대형 ~200cm 4칸 · 초대형 8칸", 15, InkBlue, TextAnchor.UpperLeft, false, "Classes");
            b.horizontalOverflow = HorizontalWrapMode.Overflow;
            b.rectTransform.Fill(14, 10, 34, 4);
        }

        /// <summary>A plain text header in a list.</summary>
        static void TextHeader(RectTransform content, string name, string text)
        {
            var h = UIKit.Rect(content, name);
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
            var t = UIKit.Label(h, text, 20, UIKit.Cream, TextAnchor.MiddleLeft);
            t.rectTransform.Fill(10, 0, 0, 0);
        }

        /// <summary>The decorations' bonus now against its cap, and how to place them.</summary>
        static void DecorHeader(RectTransform content)
        {
            var d = Game.Data;
            AquaTank.Ensure(d);
            float bonus = AquaTank.DecorBonus(d);
            int slots = AquaTank.SlotCount(d.tankLevel);
            var row = UIKit.Panel(content, "panel_paper", new Color(0.86f, 0.95f, 1f), "DecorSummary");
            row.Layout(58);
            string cap = bonus > AquaTank.BonusCap + 1e-4f ? $" <color=#8a7a60>(최대 +{AquaTank.BonusCap * 100f:0}%)</color>" : $" <size=15><color=#8a7a60>/ 최대 +{AquaTank.BonusCap * 100f:0}%</color></size>";
            var t = UIKit.Label(row.transform, $"장식 보너스 <color=#2f8a2f>+{Mathf.Min(bonus, AquaTank.BonusCap) * 100f:0}%</color>{cap}   <size=15>· 자리 {slots}칸 (수조를 넓히면 늘어나요) · 꾸미기 버튼으로 놓아요</size>",
                18, UIKit.Ink, TextAnchor.MiddleLeft, false, "Summary");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.rectTransform.Fill(14, 10, 4, 6);
        }

        /// <summary>A decoration: where it stands, its effects, bought / placed.</summary>
        static void DecorRow(RectTransform content, DecorDef def, System.Action refresh)
        {
            var row = UIKit.Panel(content, "panel_paper", null, def.id);
            row.Layout(104);
            var slot = UIKit.Panel(row.transform, "slot", null, "Slot");
            slot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(80, 80), new Vector2(0, 0.5f));
            UIKit.Img(slot.transform, Art.Item(def.id), new Vector2(64, 64));
            var name = UIKit.Label(row.transform, $"{def.name}  <size=16><color=#3a5a8a>{AquaTank.KindName(def.kind)}{(def.kind == DecorKind.Light ? "" : " 자리")}</color></size>",
                22, UIKit.Ink, TextAnchor.UpperLeft, false);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.rectTransform.Fill(102, 230, 8, 50);
            var fx = UIKit.Label(row.transform, AquaTank.EffectText(def), 15, InkGreen, TextAnchor.UpperLeft, false, "Effect");
            fx.horizontalOverflow = HorizontalWrapMode.Overflow;
            fx.rectTransform.Fill(102, 200, 38, 30);
            var desc = UIKit.Label(row.transform, def.desc, 14, InkBrown, TextAnchor.UpperLeft, false);
            desc.horizontalOverflow = HorizontalWrapMode.Overflow;
            desc.rectTransform.Fill(102, 200, 62, 6);
            bool owned = AquaTank.Owns(def.id);
            string at = AquaTank.SlotOf(def.id);
            bool standing = at != null && AquaTank.Unlocked(AquaTank.Slot(at));
            string label = owned ? (standing ? "배치됨" : "보관 중") : UIKit.Num(def.price);
            var btn = UIKit.Button(row.transform, label, owned ? "grey" : "yellow", () =>
            {
                if (AquaTank.Buy(def.id, def.price))
                {
                    Sfx.Play(Sfx.Coin);
                    Toast.Show($"{def.name} 구매! 수조의 꾸미기에서 놓아요", UIKit.Gold);
                }
                else
                {
                    Sfx.Play(Sfx.Error);
                    Toast.Show("코인이 부족해요", UIKit.Bad);
                }
                refresh();
            }, new Vector2(170, 60), 21, owned ? null : Art.UI("coin"), "Buy");
            btn.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(170, 60), new Vector2(1, 0.5f));
            btn.interactable = !owned;
        }

        /// <summary>A cleaning tool (the sponge comes free).</summary>
        static void ToolRow(RectTransform content, ToolDef t, System.Action refresh)
        {
            var row = UIKit.Panel(content, "panel_paper", null, t.id);
            row.Layout(96);
            var slot = UIKit.Panel(row.transform, "slot", null, "Slot");
            slot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(80, 80), new Vector2(0, 0.5f));
            UIKit.Img(slot.transform, Art.Item(t.id), new Vector2(64, 64));
            var name = UIKit.Label(row.transform, t.name, 22, UIKit.Ink, TextAnchor.UpperLeft, false);
            name.rectTransform.Fill(102, 230, 10, 50);
            var use = UIKit.Label(row.transform, "사용법: " + t.use, 15, InkBlue, TextAnchor.UpperLeft, false, "Use");
            use.horizontalOverflow = HorizontalWrapMode.Overflow;
            use.rectTransform.Fill(102, 200, 38, 30);
            var desc = UIKit.Label(row.transform, t.desc, 14, InkBrown, TextAnchor.UpperLeft, false);
            desc.horizontalOverflow = HorizontalWrapMode.Overflow;
            desc.rectTransform.Fill(102, 200, 60, 6);
            bool owned = AquaTank.Owns(t.id);
            string label = owned ? (t.price == 0 ? "기본 제공" : "보유 중") : UIKit.Num(t.price);
            var btn = UIKit.Button(row.transform, label, owned ? "grey" : "yellow", () =>
            {
                if (AquaTank.Buy(t.id, t.price))
                {
                    Sfx.Play(Sfx.Coin);
                    Toast.Show($"{t.name} 구매! 수조 앞 선반에 있어요", UIKit.Gold);
                }
                else
                {
                    Sfx.Play(Sfx.Error);
                    Toast.Show("코인이 부족해요", UIKit.Bad);
                }
                refresh();
            }, new Vector2(170, 60), 21, owned ? null : Art.UI("coin"), "Buy");
            btn.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(170, 60), new Vector2(1, 0.5f));
            btn.interactable = !owned;
        }

        static void GroupHeader(RectTransform content, LureAction a)
        {
            var h = UIKit.Rect(content, "Group_" + a);
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var ic = UIKit.Img(h, Art.UI(LureInfo.Icon(a)), new Vector2(24, 24), "Chip");
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(24, 24), new Vector2(0, 0.5f));
            var t = UIKit.Label(h, LureInfo.Name(a), 20, LureInfo.Color(a), TextAnchor.MiddleLeft);
            t.rectTransform.Fill(40, 0, 0, 0);
        }

        /// <summary>A small coloured tag (depth zone, stage) laid out left to right from <paramref name="x"/>.</summary>
        static void Chip(Transform parent, string text, Color bg, Color fg, ref float x, float y)
        {
            var p = UIKit.Solid(parent, bg, "Tag");
            var t = UIKit.Label(p.transform, text, 16, fg, TextAnchor.MiddleCenter, false);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = t.preferredWidth + 12f;
            p.rectTransform.At(new Vector2(0, 1), new Vector2(x, y), new Vector2(w, 22), new Vector2(0, 1));
            t.rectTransform.Fill(0, 0, 0, 2);
            x += w + 6f;
        }

        static IEnumerable<ItemDef> Items(ItemKind k)
        {
            switch (k)
            {
                case ItemKind.Rod: return GameDatabase.Rods;
                case ItemKind.Reel: return GameDatabase.Reels;
                case ItemKind.Line: return GameDatabase.Lines;
                case ItemKind.Bait: return GameDatabase.Baits;
                case ItemKind.Hook: return GameDatabase.Hooks;
                default: return GameDatabase.Tanks;
            }
        }

        public static string Stats(ItemDef it)
        {
            switch (it)
            {
                case RodDef r:
                    return $"비거리 {r.castDist:0}m · 탄력 {r.flex * 100:0}% · 챔질 여유 +{r.hookBonus:0.0}초" + (r.luck > 1 ? $" · 행운 x{r.luck:0.0}" : "");
                case ReelDef r:
                    return $"감기 {r.retrieve:0.0}m/회전 · 드랙 {r.dragMax:0.#}kg · 줄 {r.lineCap:0}m" + (r.autoReel > 0 ? " · 자동감기" : "");
                case LineDef l:
                    return $"강도 {l.strength:0}kg · {Abrasion(l.tough)} · 줄 {l.spool:0}m" + (Game.I.Owns(l.id) && Game.I.LineLeft(l.id) < l.spool ? $" (남은 {Game.I.LineLeft(l.id):0}m)" : "")
                        + (l.stealth > 1 ? $" · 입질 +{(l.stealth - 1f) * 100f:0}%" : "");
                case BaitDef b:
                {
                    var fans = GameDatabase.Fish.Where(f => f.Appeal(b) >= 0.8f && Game.I.Record(f.id) != null).Select(f => f.name).Take(4).ToList();
                    string who = fans.Count > 0 ? "좋아함: " + string.Join(", ", fans) : "어떤 물고기가 좋아할까?";
                    string kind = b.infinite ? "무한" : b.isLure ? "루어(재사용, 줄 끊기면 분실)" : $"{b.packSize}개 묶음";
                    return $"{kind} · {who}";
                }
                case HookDef h:
                {
                    var parts = new List<string> { h.infinite ? "무한" : $"{h.packSize}개 묶음", "맞는 크기 " + HookFit(h) };
                    if (h.hold > 1f) parts.Add("<color=#2e7d3a>잘 안 털림</color>");
                    if (h.snagK < 1f) parts.Add("<color=#2e7d3a>밑걸림 적음</color>");
                    if (h.setStrength >= 0.8f) parts.Add("<color=#b0402e>세게 챔질</color>");
                    else if (h.setStrength <= 0.45f) parts.Add("가볍게 챔질");
                    return string.Join(" · ", parts);
                }
                case TankDef t:
                    return $"수조 {AquaTank.LimitText(t)}";
            }
            return "";
        }

        /// <summary>The line's abrasion resistance (LineDef.tough: wear rubbing on structure is divided by it) as a grade:
        /// weak in red, strong in green, so a strong but abrasion-weak PE line reads as such.</summary>
        static string Abrasion(float tough)
        {
            if (tough >= 3f) return "<color=#2e7d3a>쓸림 매우 강함</color>";
            if (tough >= 1.4f) return "<color=#2e7d3a>쓸림 강함</color>";
            if (tough >= 0.95f) return "쓸림 보통";
            return "<color=#b0402e>쓸림 약함</color>";
        }

        /// <summary>Sold in packs and used up (natural baits, hooks): bought again and again, counted, put on with 사용.</summary>
        static bool IsPack(ItemDef it) => it is HookDef || (it is BaitDef b && !b.isLure);
        /// <summary>The free pack item that never runs out (the paste, the small hook).</summary>
        static bool IsFree(ItemDef it) => it is HookDef h ? h.infinite : it is BaitDef b && b.infinite;
        static int PackCount(ItemDef it) => it is HookDef ? Game.I.HookCount(it.id) : Game.I.BaitCount(it.id);
        static int PackSize(ItemDef it) => it is HookDef h ? h.packSize : it is BaitDef b ? b.packSize : 1;

        /// <summary>A hook's fit as text: the fish lengths it suits (HookDef.fitCm).</summary>
        public static string HookFit(HookDef h) =>
            h.fitCm.x <= 0f ? $"{h.fitCm.y:0}cm 이하" : h.fitCm.y >= 999f ? $"{h.fitCm.x:0}cm 이상" : $"{h.fitCm.x:0}~{h.fitCm.y:0}cm";

        static void Row(RectTransform content, ItemDef it, System.Action refresh)
        {
            var row = UIKit.Panel(content, "panel_paper", null, it.id);
            row.Layout(96);
            var slot = UIKit.Panel(row.transform, "slot", null, "Slot");
            slot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(80, 80), new Vector2(0, 0.5f));
            UIKit.Img(slot.transform, Art.Item(it.id), new Vector2(64, 64));
            if (it is BaitDef lure && lure.isLure)
            {
                // [action chip] name / 사용법: hint / [depth] [stages where something goes for it]
                var chip = UIKit.Img(row.transform, Art.UI(LureInfo.Icon(lure.action)), new Vector2(24, 24), "Chip");
                chip.rectTransform.At(new Vector2(0, 1), new Vector2(102, -10), new Vector2(24, 24), new Vector2(0, 1));
                var ln = UIKit.Label(row.transform, $"{it.name}  <size=16><color=#{ColorUtility.ToHtmlStringRGB(LureInfo.Color(lure.action) * 0.7f)}>{LureInfo.Name(lure.action)}</color></size>",
                    22, UIKit.Ink, TextAnchor.UpperLeft, false);
                ln.horizontalOverflow = HorizontalWrapMode.Overflow;
                ln.rectTransform.Fill(132, 230, 10, 50);
                var how = UIKit.Label(row.transform, "사용법: " + lure.hint, 15, new Color32(0x3a, 0x5a, 0x8a, 0xff), TextAnchor.UpperLeft, false);
                how.horizontalOverflow = HorizontalWrapMode.Overflow;
                how.rectTransform.Fill(102, 200, 40, 30);
                float x = 102f;
                Chip(row.transform, LureInfo.Zone(lure), new Color32(0x3a, 0x5a, 0x8a, 0xff), UIKit.Cream, ref x, -66);
                foreach (var st in GameDatabase.Stages)
                    if (x < 500f && Game.I.IsUnlocked(st.id) && GameDatabase.FishOfStage(st.id).Any(f => f.Appeal(lure) >= 0.6f))
                        Chip(row.transform, st.name, new Color32(0x6a, 0x5a, 0x40, 0xff), UIKit.Cream, ref x, -66);
            }
            else
            {
                var name = UIKit.Label(row.transform, it.name, 22, UIKit.Ink, TextAnchor.UpperLeft, false);
                name.rectTransform.Fill(102, 230, 10, 50);
                var stat = UIKit.Label(row.transform, Stats(it), 15, new Color32(0x3a, 0x5a, 0x8a, 0xff), TextAnchor.UpperLeft, false);
                stat.rectTransform.Fill(102, 230, 38, 30);
                var desc = UIKit.Label(row.transform, it.desc, 14, new Color32(0x6a, 0x5a, 0x40, 0xff), TextAnchor.UpperLeft, false);
                desc.rectTransform.Fill(102, 230, 60, 6);
            }

            var g = Game.I;
            string label;
            string style;
            bool interact = true;
            Sprite icon = null;
            bool owned = it is TankDef tk ? tk.level <= Game.Data.tankLevel : IsPack(it) ? false : g.Owns(it.id);
            if (it is TankDef tank)
            {
                if (tank.level <= Game.Data.tankLevel) { label = "보유 중"; style = "grey"; interact = false; }
                else if (tank.level > Game.Data.tankLevel + 1) { label = "이전 단계 필요"; style = "grey"; interact = false; }
                else { label = UIKit.Num(it.price); style = "yellow"; icon = Art.UI("coin"); }
            }
            else if (IsPack(it))
            {
                if (IsFree(it)) { label = g.IsEquipped(it) ? "사용 중" : "사용"; style = g.IsEquipped(it) ? "grey" : "green"; interact = !g.IsEquipped(it); }
                else { label = UIKit.Num(it.price); style = "yellow"; icon = Art.UI("coin"); }
            }
            else if (owned)
            {
                bool eq = g.IsEquipped(it);
                label = eq ? "장착 중" : "장착";
                style = eq ? "grey" : "green";
                interact = !eq;
            }
            else
            {
                label = UIKit.Num(it.price);
                style = "yellow";
                icon = Art.UI("coin");
            }
            bool consumable = IsPack(it);
            var btn = UIKit.Button(row.transform, label, style, () =>
            {
                bool freeBait = IsFree(it);
                if (freeBait || (owned && !consumable && !(it is TankDef)))
                {
                    g.Equip(it);
                    refresh();
                    return;
                }
                var res = g.Buy(it);
                switch (res)
                {
                    case BuyResult.Ok:
                        Sfx.Play(Sfx.Coin);
                        Toast.Show(IsPack(it) ? $"{it.name} {PackSize(it)}개 구매! (보유 {PackCount(it)})" : $"{it.name} 구매!", UIKit.Gold);
                        // (the first pack bought goes on in place of the free one)
                        if (IsPack(it) && (it is HookDef ? g.Hook.infinite : g.Bait.infinite)) g.Equip(it);
                        break;
                    case BuyResult.NotEnoughCoins:
                        Sfx.Play(Sfx.Error);
                        Toast.Show("코인이 부족해요", UIKit.Bad);
                        break;
                }
                refresh();
            }, new Vector2(170, 60), 21, icon);
            btn.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-14, IsPack(it) && !IsFree(it) ? 8 : 0), new Vector2(170, 60), new Vector2(1, 0.5f));
            btn.interactable = interact;
            if (IsPack(it) && !IsFree(it))
            {
                var cnt = UIKit.Label(row.transform, $"보유 {PackCount(it)}개" + (g.IsEquipped(it) ? " · 사용 중" : ""), 14,
                    new Color32(0x3a, 0x5a, 0x8a, 0xff), TextAnchor.LowerRight, false);
                cnt.rectTransform.At(new Vector2(1, 0), new Vector2(-16, 6), new Vector2(200, 20), new Vector2(1, 0));
                if (PackCount(it) > 0 && !g.IsEquipped(it))
                {
                    var use = UIKit.Button(row.transform, "사용", "green", () => { g.Equip(it); refresh(); }, new Vector2(76, 44), 20);
                    use.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-194, 8), new Vector2(76, 44), new Vector2(1, 0.5f));
                }
            }
        }
    }
}
