using System;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// Settings dialog: sound, the background music (<see cref="Music"/>), the reel-gesture ring (circle + direction
    /// arrows), which way a drawn circle winds in (clockwise by default, counter-clockwise for players who prefer it) and
    /// the fishing view's zoom after the cast (끔 / 1.25배 (default) / 1.5배 / 액티브, <see cref="ZoomMode"/>).
    /// </summary>
    public static class SettingsUI
    {
        // (five rows: 64 apart and 488 high keep the window and its ribbon inside the smallest canvas, 540 high)
        const float Width = 700, Height = 488, RowStep = 64, FirstRowY = -96;

        public static void Open()
        {
            var w = Dialog.Window("설정", new Vector2(Width, Height), out var close);
            Row(w, 0, "소리", () => Game.Data.soundOn, on => Game.I.SetSound(on));
            Row(w, 1, "배경음", () => Game.Data.musicOn, on => Game.I.SetMusic(on));
            Row(w, 2, "릴 감기 원 · 방향 화살표", () => Game.Data.reelRing, on => Game.I.SetReelRing(on));
            Row(w, 3, "원을 그려 감는 방향", () => Game.Data.reelReverse, on => Game.I.SetReelReverse(on),
                "반시계방향", "시계방향", "blue", "blue");
            Choice(w, 4, "캐스팅 후 줌인", () => Game.Data.zoomMode, v => Game.I.SetZoomMode((ZoomMode)v),
                ("끔", (int)ZoomMode.Off), ("1.25배", (int)ZoomMode.X125), ("1.5배", (int)ZoomMode.X150), ("액티브", (int)ZoomMode.Active));
            var done = UIKit.Button(w, "닫기", "grey", close, new Vector2(160, 56));
            done.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(160, 56), new Vector2(0.5f, 0));
        }

        /// <summary>A small "설정" button for a screen corner.</summary>
        public static Button CornerButton(Transform parent) => UIKit.Button(parent, "설정", "grey", Open, new Vector2(96, 56), 20);

        static void Row(RectTransform w, int i, string label, Func<bool> get, Action<bool> set,
            string onText = "켜짐", string offText = "꺼짐", string onStyle = "green", string offStyle = "grey")
        {
            float y = FirstRowY - i * RowStep;
            var t = UIKit.Label(w, label, 20, UIKit.Ink, TextAnchor.MiddleLeft, false);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(44, y), new Vector2(300, 52), new Vector2(0, 0.5f));
            Button b = null;
            Action show = () =>
            {
                bool on = get();
                b.GetComponentInChildren<Text>().text = on ? onText : offText;
                b.GetComponent<Image>().sprite = Art.UI("btn_" + (on ? onStyle : offStyle));
            };
            b = UIKit.Button(w, onText, onStyle, () =>
            {
                set(!get());
                show();
            }, new Vector2(150, 52), 20);
            b.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-44, y), new Vector2(150, 52), new Vector2(1, 0.5f));
            show();
        }

        /// <summary>A row choosing one of several values: a button per choice (the chosen one blue, the others grey), right-aligned like the toggles.</summary>
        static void Choice(RectTransform w, int i, string label, Func<int> get, Action<int> set, params (string text, int value)[] choices)
        {
            const float bw = 96, gap = 8, h = 52;
            float y = FirstRowY - i * RowStep;
            float span = choices.Length * bw + (choices.Length - 1) * gap;
            var t = UIKit.Label(w, label, 20, UIKit.Ink, TextAnchor.MiddleLeft, false);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(44, y), new Vector2(Width - 88 - span - 12, h), new Vector2(0, 0.5f));
            var buttons = new Button[choices.Length];
            Action show = () =>
            {
                int v = get();
                for (int k = 0; k < choices.Length; k++) buttons[k].SetStyle(choices[k].value == v ? "blue" : "grey");
            };
            for (int k = 0; k < choices.Length; k++)
            {
                int value = choices[k].value;
                var b = UIKit.Button(w, choices[k].text, "grey", () =>
                {
                    set(value);
                    show();
                }, new Vector2(bw, h), 20, null, "Choice_" + choices[k].text);
                float x = -44 - (choices.Length - 1 - k) * (bw + gap);
                b.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(x, y), new Vector2(bw, h), new Vector2(1, 0.5f));
                buttons[k] = b;
            }
            show();
        }
    }
}
