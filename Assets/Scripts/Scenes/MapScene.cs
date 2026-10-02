using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>Hub: island map with the 7 fishing spots, plus shop / aquarium / encyclopedia.</summary>
    public class MapScene : MonoBehaviour
    {
        PixelView pv;
        Canvas canvas;
        readonly List<(RectTransform rt, Vector2 world, RectTransform label)> markers = new List<(RectTransform, Vector2, RectTransform)>();
        readonly List<SpriteRenderer> waves = new List<SpriteRenderer>();

        void Start()
        {
            pv = PixelView.Create(new Color32(0x1d, 0x5e, 0x9c, 0xff));
            var map = new GameObject("Map").AddComponent<SpriteRenderer>();
            map.sprite = Art.Stage("map_world");
            for (int i = 0; i < 30; i++) waves.Add(NewWave());
            // the time of day over the island (the clock stands still here): a flat tint over the map and its waves
            periodTint = new GameObject("PeriodTint").AddComponent<SpriteRenderer>();
            periodTint.sprite = Art.Pixel;
            periodTint.sortingOrder = 2;
            periodTint.transform.localScale = new Vector3(PixelView.PPU * 48f, PixelView.PPU * 30f, 1f);
            var tb = GameClock.Look;
            Color TintOf(Period p) => p switch
            {
                Period.Dawn => Art.Hex("#c8a8d0", 0.16f),
                Period.Evening => Art.Hex("#ff9a50", 0.12f),
                Period.Night => Art.Hex("#1a2448", 0.42f),
                _ => Art.Hex("#ffffff", 0f),
            };
            Color ta = TintOf(tb.From), tc = TintOf(tb.To);
            // (from / to a clear day: keep the other's hue, fade its alpha)
            if (ta.a <= 0f) ta = new Color(tc.r, tc.g, tc.b, 0f);
            if (tc.a <= 0f) tc = new Color(ta.r, ta.g, ta.b, 0f);
            periodTint.color = Color.Lerp(ta, tc, tb.F);
            periodTint.enabled = periodTint.color.a > 0.005f;
            Toast.Init();
            Sfx.Ambience("day");
            Music.Play("map");

            canvas = UIKit.CreateCanvas("MapUI", 10);
            var root = (RectTransform)canvas.transform;
            TopBar.Create(root);
            var title = UIKit.Panel(root, "panel_dark", null, "Title");
            title.rectTransform.At(new Vector2(0, 1), new Vector2(12, -10), new Vector2(176, 56));
            UIKit.Label(title.transform, "낚시왕의 섬", 24, UIKit.Gold).rectTransform.Fill(8, 8, 4, 8);
            SettingsUI.CornerButton(root).GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(196, -10), new Vector2(96, 56));
            BuildClock(root);

            foreach (var m in Art.Map.markers) BuildMarker(root, m);

            var bar = UIKit.Rect(root, "BottomBar").At(new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(560, 84), new Vector2(0.5f, 0));
            MenuButton(bar, 0, "상점", "icon_shop", () => ShopUI.Open(canvas.transform));
            MenuButton(bar, 1, "수조", "icon_tank", () => SceneFlow.Go("Aquarium"));
            MenuButton(bar, 2, "도감", "icon_book", () => CollectionUI.Open(canvas.transform));
            MenuButton(bar, 3, "타이틀", "icon_back", () => SceneFlow.Go("Title"));

            int pending = Game.I.PendingIncome;
            if (pending > 0) Toast.Show($"수조 관람 수입 {UIKit.Num(pending)} 코인이 쌓였어요!", UIKit.Gold, 2.5f);
        }

        SpriteRenderer periodTint;

        /// <summary>The clock (stopped here: it only runs on a fishing stage), Docs/time_currents_spec.md 11.4.</summary>
        void BuildClock(RectTransform root)
        {
            var p = UIKit.Panel(root, "panel_dark", null, "Clock").rectTransform;
            p.At(new Vector2(0, 1), new Vector2(300, -10), new Vector2(172, 56));
            var per = GameClock.Now;
            var ic = UIKit.Img(p, Art.UI("tod_" + GameClock.Id(per)), new Vector2(24, 24), "PeriodIcon");
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(8, -5), new Vector2(24, 24), new Vector2(0, 1));
            var t = UIKit.Label(p, GameClock.Label(GameClock.Min), 20, GameClock.TextColor(per), TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.rectTransform.At(new Vector2(0, 1), new Vector2(36, -5), new Vector2(108, 24), new Vector2(0, 1));
            var s = UIKit.Label(p, "낚시터에서만 흘러요", 15, new Color32(0xa8, 0xa8, 0xb0, 0xff), TextAnchor.MiddleLeft);
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
            s.rectTransform.At(new Vector2(0, 0), new Vector2(10, 4), new Vector2(156, 20), new Vector2(0, 0));
        }

        void MenuButton(RectTransform bar, int i, string label, string icon, System.Action a)
        {
            var b = UIKit.Button(bar, label, i == 3 ? "grey" : "blue", a, new Vector2(132, 72), 20, Art.UI(icon));
            b.GetComponent<RectTransform>().At(new Vector2(0, 0.5f), new Vector2(i * 142, 0), new Vector2(132, 72), new Vector2(0, 0.5f));
        }

        void BuildMarker(RectTransform root, MapMarker m)
        {
            var st = GameDatabase.GetStage(m.id);
            if (st == null) return;
            bool open = Game.I.IsUnlocked(st.id);
            var rt = UIKit.Rect(root, "Marker_" + st.id);
            rt.sizeDelta = new Vector2(150, 70);
            var pin = UIKit.Button(rt, null, open ? "yellow" : "grey", () => ShowStage(st), new Vector2(46, 46));
            pin.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(46, 46), new Vector2(0.5f, 0));
            var ic = UIKit.Img(pin.transform, Art.UI(open ? "icon_fish" : "lock"), new Vector2(30, 30));
            ic.rectTransform.anchoredPosition = new Vector2(0, 3);
            var label = UIKit.Panel(rt, "panel_dark", new Color(1, 1, 1, 0.9f), "Label");
            label.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(Mathf.Max(96, st.name.Length * 20 + 28), 30), new Vector2(0.5f, 0));
            UIKit.Label(label.transform, st.name, 17, open ? UIKit.Cream : new Color(0.75f, 0.75f, 0.8f)).rectTransform.Fill(4, 4, 2, 4);
            if (Game.Data.lastStage == st.id) pin.gameObject.AddComponent<Pulse>();
            markers.Add((rt, new Vector2(m.x, m.y), label.rectTransform));
        }

        void LateUpdate()
        {
            float scale = UIKit.CanvasPerScreenPx;
            float hudTop = Screen.height * scale * 0.5f - 72f; // below the title / top bar band
            foreach (var (rt, w, label) in markers)
            {
                var s = pv.WorldToScreen(w);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                var pos = new Vector2(s.x - Screen.width * 0.5f, s.y - Screen.height * 0.5f) * scale + new Vector2(0, -8);
                rt.anchoredPosition = new Vector2(Mathf.Round(pos.x / UIKit.Px) * UIKit.Px, Mathf.Round(pos.y / UIKit.Px) * UIKit.Px);
                // a label that would slide under the HUD moves beside its pin, on the side away from the centre
                bool flip = rt.anchoredPosition.y + 48 + 30 > hudTop;
                float side = rt.anchoredPosition.x >= 0 ? 1f : -1f;
                label.anchoredPosition = flip ? new Vector2(side * (label.sizeDelta.x * 0.5f + 28f), 8f) : new Vector2(0, 48);
            }
            foreach (var w in waves)
            {
                var c = w.color;
                c.a -= Time.deltaTime * 0.35f;
                if (c.a <= 0)
                {
                    RespawnWave(w);
                    continue;
                }
                w.color = c;
            }
        }

        SpriteRenderer NewWave()
        {
            var sr = new GameObject("Wave").AddComponent<SpriteRenderer>();
            sr.sprite = Art.Pixel;
            sr.sortingOrder = 1;
            RespawnWave(sr);
            sr.color = new Color(1, 1, 1, Random.value * 0.6f);
            return sr;
        }

        static void RespawnWave(SpriteRenderer sr)
        {
            // keep the glints on open water near the edges of the island
            Vector2 p;
            do p = new Vector2(Random.Range(-20f, 20f), Random.Range(-12.5f, 12.5f));
            while ((p.x / 15.5f) * (p.x / 15.5f) + (p.y / 9.5f) * (p.y / 9.5f) < 1f);
            sr.transform.position = new Vector3(Mathf.Round(p.x * 16) / 16, Mathf.Round(p.y * 16) / 16, 0);
            sr.transform.localScale = new Vector3(Random.Range(2, 5), 1, 1);
            sr.color = new Color(0.8f, 0.92f, 1f, 0.7f);
        }

        // ------------------------------------------------------------------ stage info
        /// <summary>The period's colour, darkened to read on the cream paper.</summary>
        static Color PeriodInk(Period p) => Color.Lerp(GameClock.TextColor(p), new Color32(0x2a, 0x1e, 0x14, 0xff), 0.55f);

        /// <summary>
        /// A caught species' activity now (spec 11.4): a green ▲ when it is at its best (a >= 1.3), a grey ▼ when it is
        /// slow (a <= 0.6), the night icon over a faded fish when it only comes at night.
        /// </summary>
        static void ActivityChip(Transform cell, Image fishImg, FishSpecies f)
        {
            float a = TimeActivity.Now(f.id);
            if (a > 0f && a < 1.3f && a > 0.6f) return;
            var rt = UIKit.Rect(cell, "Activity").At(new Vector2(1, 1), new Vector2(-3, -3), new Vector2(16, 16), new Vector2(1, 1));
            if (a <= 0f)
            {
                var ic = UIKit.Img(rt, Art.UI("tod_night_s"), new Vector2(16, 16), "Night");
                ic.rectTransform.Fill();
                var c = fishImg.color;
                fishImg.color = new Color(c.r, c.g, c.b, 0.35f);
                return;
            }
            bool up = a >= 1.3f;
            var t = UIKit.Label(rt, up ? "▲" : "▼", 15, up ? new Color32(0x6a, 0xd0, 0x6a, 0xff) : new Color32(0x9a, 0x9a, 0xa4, 0xff), TextAnchor.MiddleCenter, false);
            t.rectTransform.Fill();
        }

        void ShowStage(StageDef st)
        {
            var w = Dialog.Window(st.name, new Vector2(640, 470), out var close);
            var sub = UIKit.Label(w, st.subtitle, 19, UIKit.Ink, TextAnchor.MiddleCenter, false);
            sub.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(560, 28), new Vector2(0.5f, 1));
            // the time of day there now (+ the sea's tide): the clock is stopped on the map
            var per = GameClock.Now;
            string when = $"지금 {GameClock.Label(GameClock.Min)}" + (st.id == "sea" ? $" · {GameClock.Tide.Word}" : "");
            var nowT = UIKit.Label(w, $"<color=#{ColorUtility.ToHtmlStringRGB(PeriodInk(per))}>{when}</color>", 17, UIKit.Ink, TextAnchor.MiddleCenter, false);
            nowT.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -72), new Vector2(560, 22), new Vector2(0.5f, 1));
            var stars = UIKit.Rect(w, "Stars").At(new Vector2(0.5f, 1), new Vector2(0, -98), new Vector2(170, 24), new Vector2(0.5f, 1));
            UIKit.Label(stars, "난이도", 16, UIKit.Ink, TextAnchor.MiddleRight, false).rectTransform.At(new Vector2(0, 0.5f), new Vector2(-6, 0), new Vector2(70, 24), new Vector2(1, 0.5f));
            for (int i = 0; i < 5; i++)
            {
                var s = UIKit.Img(stars, Art.UI(i < st.difficulty ? "star" : "star_empty"), new Vector2(24, 24));
                s.rectTransform.At(new Vector2(0, 0.5f), new Vector2(i * 28, 0), new Vector2(24, 24), new Vector2(0, 0.5f));
            }
            // species of this spot: 5 x 2 cells of 104 x 88; a stage with more than 10 (the lake's 11) gets 6 x 2 of 86 x 88
            // (12 + 6 x 86 + 5 x 6 + 12 = 570 of the panel's 580; 7 + 88 + 6 + 88 + 7 = 196 of its 202)
            var grid = UIKit.Panel(w, "panel_paper", null, "Species");
            grid.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -128), new Vector2(580, 202), new Vector2(0.5f, 1));
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            var stageFish = GameDatabase.FishOfStage(st.id).ToList();
            bool narrow = stageFish.Count > 10;
            gl.cellSize = narrow ? new Vector2(86, 88) : new Vector2(104, 88);
            gl.spacing = new Vector2(6, 6);
            gl.padding = new RectOffset(12, 12, 7, 7);
            gl.childAlignment = TextAnchor.MiddleCenter;
            foreach (var f in stageFish)
            {
                var rec = Game.I.Record(f.id);
                var cell = UIKit.Panel(grid.transform, "slot", null, f.id);
                var spr = Art.Fish(f.id, 0);
                var img = UIKit.Img(cell.transform, spr, narrow ? new Vector2(76, 38) : new Vector2(88, 44));
                img.rectTransform.anchoredPosition = new Vector2(0, 10);
                if (rec == null) img.color = new Color(0, 0, 0, 0.75f);
                var t = UIKit.Label(cell.transform, rec != null ? f.name : "???", narrow ? 14 : 15,
                    rec != null ? RarityInfo.Color(f.rarity) : new Color(0.7f, 0.7f, 0.7f));
                t.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(narrow ? 84 : 100, 22), new Vector2(0.5f, 0));
                if (rec != null) ActivityChip(cell.transform, img, f);
            }

            bool open = Game.I.IsUnlocked(st.id);
            string status;
            if (open) status = "";
            else if (Game.Data.level < st.reqLevel) status = $"<color=#b03a2a>레벨 {st.reqLevel} 이상 필요</color> (현재 Lv.{Game.Data.level})";
            else status = $"해금 비용: {UIKit.Num(st.unlockCost)} 코인";
            var stt = UIKit.Label(w, status, 19, UIKit.Ink, TextAnchor.MiddleCenter, false);
            stt.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(560, 30), new Vector2(0.5f, 0));

            Button go;
            if (open)
            {
                go = UIKit.Button(w, "낚시하러 가기!", "green", () => { close(); SceneFlow.Fishing(st.id); }, new Vector2(260, 68), 24);
            }
            else if (Game.Data.level < st.reqLevel)
            {
                go = UIKit.Button(w, "잠겨 있음", "grey", null, new Vector2(260, 68), 24, Art.UI("lock"));
                go.interactable = false;
            }
            else
            {
                go = UIKit.Button(w, $"해금하기", "yellow", () =>
                {
                    if (Game.I.Unlock(st))
                    {
                        Sfx.Play(Sfx.Unlock);
                        Toast.Show($"{st.name} 해금!", UIKit.Gold);
                        close();
                        SceneFlow.Go("Map");
                    }
                    else
                    {
                        Sfx.Play(Sfx.Error);
                        Toast.Show("코인이 부족해요", UIKit.Bad);
                    }
                }, new Vector2(260, 68), 24, Art.UI("coin"));
            }
            go.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(260, 68), new Vector2(0.5f, 0));
        }
    }
}
