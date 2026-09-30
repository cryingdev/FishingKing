using System;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>"You caught it!" card: fish, rarity, size, weight, value and sell / keep / release.</summary>
    public static class CatchPopup
    {
        public enum Choice { Sell, Keep, Release }

        public static void Show(Canvas canvas, CaughtFish cf, CatchReport rep, Action<Choice> done)
        {
            var sp = cf.Species;
            RectTransform dim = UIKit.Modal(canvas.transform, null, 0.55f);
            Action<Choice> finish = c =>
            {
                if (dim != null) UnityEngine.Object.Destroy(dim.gameObject);
                done?.Invoke(c);
            };
            var win = UIKit.Panel(dim, "panel_wood", null, "CatchCard").rectTransform;
            win.At(new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(620, 470));
            Tween.Pop(win, 0.6f, 0.3f);

            string title = rep.newSpecies ? "새로운 물고기 발견!" : rep.newRecord ? "최고 기록 경신!" : "낚았다!";
            UIKit.Ribbon(win, title, rep.newSpecies ? "yellow" : "green", 300, 560, 22);

            // fish picture on a water-coloured plate
            var plate = UIKit.Panel(win, "panel_paper", new Color(0.62f, 0.86f, 1f), "Plate");
            plate.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(540, 168), new Vector2(0.5f, 1));
            var glow = UIKit.Img(plate.transform, Art.SoftCircle, new Vector2(420, 150), "Glow");
            var rc = RarityInfo.Color(sp.rarity);
            glow.color = new Color(rc.r, rc.g, rc.b, sp.rarity == Rarity.Common ? 0.35f : 0.8f);
            var spr = Art.Fish(sp.id, 0);
            float scale = spr != null ? Mathf.Floor(Mathf.Min(480f / spr.rect.width, 140f / spr.rect.height) / UIKit.Px) : 1;
            scale = Mathf.Max(1, scale);
            var pic = UIKit.PixelImg(plate.transform, spr, scale, "Fish");
            pic.gameObject.AddComponent<FishWiggle>().Init(sp.id);

            // info
            var badge = UIKit.Panel(win, "badge", RarityInfo.Color(sp.rarity), "Rarity");
            badge.rectTransform.At(new Vector2(0.5f, 1), new Vector2(-150, -224), new Vector2(92, 30), new Vector2(0.5f, 1));
            UIKit.Label(badge.transform, RarityInfo.Name(sp.rarity), 17, UIKit.Ink, TextAnchor.MiddleCenter, false).rectTransform.Fill(2, 2, 2, 4);
            var name = UIKit.Label(win, sp.name, 32, UIKit.Ink, TextAnchor.MiddleCenter, false);
            name.rectTransform.At(new Vector2(0.5f, 1), new Vector2(20, -222), new Vector2(300, 40), new Vector2(0.5f, 1));
            var stars = UIKit.Rect(win, "Stars").At(new Vector2(0.5f, 1), new Vector2(0, -264), new Vector2(150, 22), new Vector2(0.5f, 1));
            int n = RarityInfo.Stars(sp.rarity);
            for (int i = 0; i < 5; i++)
            {
                var s = UIKit.Img(stars, Art.UI(i < n ? "star" : "star_empty"), new Vector2(22, 22));
                s.rectTransform.At(new Vector2(0, 0.5f), new Vector2(i * 26 + 10, 0), new Vector2(22, 22), new Vector2(0, 0.5f));
            }
            string rec = rep.newRecord ? "  <color=#d83a3a>NEW!</color>" : "";
            // a legend's own line under the stats (Docs/legends_rollout.md 1.1 catchLine)
            string line = sp.encounter != null && !string.IsNullOrEmpty(sp.encounter.catchLine)
                ? $"\n<size=16><color=#8a5a1a>{sp.encounter.catchLine}</color></size>" : "";
            var stats = UIKit.Label(win, $"크기 <color=#1f6fc4>{cf.sizeCm:0.0}cm</color>{rec}     무게 <color=#1f6fc4>{FormatKg(cf.weightKg)}</color>\n경험치 +{rep.xpGained}{line}",
                20, UIKit.Ink, TextAnchor.MiddleCenter, false);
            stats.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -292), new Vector2(560, line.Length > 0 ? 80 : 56), new Vector2(0.5f, 1));

            // choices
            var sell = UIKit.Button(win, $"판매  +{UIKit.Num(cf.value)}", "yellow", () => finish(Choice.Sell), new Vector2(200, 64), 20, Art.UI("coin"));
            sell.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-184, 26), new Vector2(200, 64), new Vector2(0.5f, 0));
            // the tank takes it by its size class and the 칸 left (AquaTank.CanAdd); if not, the button says so and a tap
            // shakes it with why ("이 수조엔 너무 커요 · 대형 수조가 필요해요")
            string refuse = Game.I.KeepRefusal(cf);
            int cls = AquaTank.ClassOf(cf);
            Text why = null;
            Button keep = null;
            keep = UIKit.Button(win, refuse == "" ? $"보관 +{AquaTank.ClassSpace[cls]}칸" : refuse.StartsWith("이 수조엔") ? "너무 커요" : "자리 부족", refuse == "" ? "blue" : "grey",
                () =>
                {
                    if (Game.I.KeepRefusal(cf) == "") { finish(Choice.Keep); return; }
                    Sfx.Play(Sfx.Error);
                    Tween.Punch(keep.transform, 0.08f, 0.2f);
                    if (why != null) Tween.Pop(why.transform, 0.8f, 0.2f);
                }, new Vector2(200, 64), 20, Art.UI("icon_tank"), "KeepButton");
            keep.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(24, 26), new Vector2(200, 64), new Vector2(0.5f, 0));
            // the tank's space over the buttons: "수조 7/12칸 · 중형 2칸", or why it does not go in (red)
            var t = Game.I.Tank;
            why = UIKit.Label(win, refuse == "" ? $"{t.name} {Game.I.UsedSpace}/{t.capacity}칸 · 이 물고기 {AquaTank.ClassNames[cls]} {AquaTank.ClassSpace[cls]}칸"
                    : $"<color=#c0392b>{refuse}</color>", 16, UIKit.Ink, TextAnchor.MiddleCenter, false, "KeepInfo");
            why.horizontalOverflow = HorizontalWrapMode.Overflow;
            why.rectTransform.At(new Vector2(0.5f, 0), new Vector2(24, 94), new Vector2(360, 22), new Vector2(0.5f, 0));
            var rel = UIKit.Button(win, "놓아주기", "grey", () => finish(Choice.Release), new Vector2(150, 64), 19);
            rel.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(207, 26), new Vector2(150, 64), new Vector2(0.5f, 0));
        }

        public static string FormatKg(float kg) => kg >= 1f ? $"{kg:0.0}kg" : $"{kg * 1000f:0}g";
    }

    /// <summary>Swaps the two swim frames of a fish image (catch card, aquarium info, encyclopedia).</summary>
    public class FishWiggle : MonoBehaviour
    {
        Sprite a, b;
        Image img;
        float t;

        public void Init(string id)
        {
            img = GetComponent<Image>();
            a = Art.Fish(id, 0);
            b = Art.Fish(id, 1);
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (img != null) img.sprite = ((int)(t / 0.35f)) % 2 == 0 ? a : b;
        }
    }
}
