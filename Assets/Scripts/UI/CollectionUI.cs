using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>Fish encyclopedia (도감): every species by stage, silhouettes until caught.</summary>
    public static class CollectionUI
    {
        const int Cols = 6;
        static readonly Vector2 Cell = new Vector2(122, 104);

        public static void Open(Transform parent)
        {
            RectTransform dim = UIKit.Modal(parent);
            var win = UIKit.Panel(dim, "panel_wood", null, "Collection").rectTransform;
            win.At(new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(860, 480));
            win.GetComponent<Image>().raycastTarget = true;
            Tween.Pop(win, 0.8f);
            int caught = Game.Data.records.Count, total = GameDatabase.Fish.Count;
            UIKit.Ribbon(win, $"물고기 도감  {caught}/{total}");
            var close = UIKit.IconButton(win, Art.UI("icon_back"), "red", () => Object.Destroy(dim.gameObject), 50);
            close.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-14, -14), new Vector2(50, 50));

            var sr = UIKit.ScrollList(win, out var content, 10);
            sr.GetComponent<RectTransform>().Fill(24, 24, 60, 24);
            foreach (var st in GameDatabase.Stages)
            {
                var species = GameDatabase.FishOfStage(st.id).ToList();
                int got = species.Count(f => Game.I.Record(f.id) != null);
                var header = UIKit.Label(content, $"{st.name}  <size=16><color=#6a5a40>{got}/{species.Count}</color></size>", 22, UIKit.Ink, TextAnchor.MiddleLeft, false);
                header.Layout(32);
                int rows = (species.Count + Cols - 1) / Cols;
                var grid = UIKit.Rect(content, "Grid_" + st.id);
                grid.gameObject.AddComponent<LayoutElement>().preferredHeight = rows * (Cell.y + 6);
                var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
                gl.cellSize = Cell;
                gl.spacing = new Vector2(6, 6);
                gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gl.constraintCount = Cols;
                foreach (var f in species) Tile(grid, f, parent);
            }
        }

        static void Tile(RectTransform grid, FishSpecies f, Transform parent)
        {
            var rec = Game.I.Record(f.id);
            var tile = UIKit.Panel(grid, rec != null ? "panel_paper" : "slot", null, f.id);
            tile.raycastTarget = true;
            var img = UIKit.Img(tile.transform, Art.Fish(f.id, 0), new Vector2(100, 52));
            img.rectTransform.anchoredPosition = new Vector2(0, 12);
            if (rec == null) img.color = new Color(0, 0, 0, 0.7f);
            // a legend seen in its encounter but not caught yet: how often it was seen
            var seen = rec == null && f.encounter != null ? Game.I.FindLegend(f.id) : null;
            bool spotted = seen != null && seen.seen > 0;
            if (spotted) img.color = new Color(0.1f, 0.22f, 0.3f, 0.85f);
            var n = UIKit.Label(tile.transform, rec != null ? f.name : spotted ? $"목격 {seen.seen}회" : "???", 16,
                rec != null ? UIKit.Ink : spotted ? new Color32(0x6a, 0xd8, 0xe0, 0xff) : new Color(0.75f, 0.72f, 0.65f),
                TextAnchor.MiddleCenter, false);
            n.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(116, 22), new Vector2(0.5f, 0));
            if (spotted)
            {
                var nc = UIKit.Label(tile.transform, "아직 낚지 못함", 15, new Color(0.75f, 0.72f, 0.65f), TextAnchor.MiddleCenter, false);
                nc.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(116, 20), new Vector2(0.5f, 1));
            }
            var dot = UIKit.Img(tile.transform, Art.UI("star"), new Vector2(14, 14));
            dot.rectTransform.At(new Vector2(0, 1), new Vector2(6, -6), new Vector2(14, 14), new Vector2(0, 1));
            dot.color = rec != null ? RarityInfo.Color(f.rarity) : new Color(0.4f, 0.4f, 0.4f);
            if (rec != null)
            {
                var b = tile.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => Detail(f, rec));
            }
        }

        static void Detail(FishSpecies f, SpeciesRecord rec)
        {
            Sfx.Play(Sfx.Click);
            var w = Dialog.Window(f.name, new Vector2(620, 440), out _);
            var plate = UIKit.Panel(w, "panel_paper", new Color(0.62f, 0.86f, 1f), "Plate");
            plate.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(540, 150), new Vector2(0.5f, 1));
            var spr = Art.Fish(f.id, 0);
            float scale = Mathf.Max(1, Mathf.Floor(Mathf.Min(480f / spr.rect.width, 130f / spr.rect.height) / UIKit.Px));
            UIKit.PixelImg(plate.transform, spr, scale, "Fish").gameObject.AddComponent<FishWiggle>().Init(f.id);
            var st = GameDatabase.GetStage(GameDatabase.StageOfFish(f.id));
            var baits = f.baitPrefs.OrderByDescending(kv => kv.Value).Take(3)
                .Select(kv => GameDatabase.GetItem(kv.Key)?.name).Where(s => s != null);
            string text =
                $"<color=#{ColorUtility.ToHtmlStringRGB(RarityInfo.InkColor(f.rarity))}>{RarityInfo.Name(f.rarity)}</color>   서식지: {st?.name}\n" +
                $"{f.desc}\n" +
                $"<size=17>잡은 수 {rec.caught}마리 · 최대 {rec.bestCm:0.0}cm · 크기 {f.minCm:0}~{f.maxCm:0}cm\n" +
                $"수심 {f.depthMin:0.#}~{f.depthMax:0.#}m · 좋아하는 미끼: {string.Join(", ", baits)}\n" +
                $"{TimeActivity.Describe(f.id)}</size>";
            var t = UIKit.Label(w, text, 19, UIKit.Ink, TextAnchor.UpperLeft, false);
            t.rectTransform.Fill(40, 40, 214, 24);
        }
    }
}
