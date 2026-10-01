using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// Settings dialog: sound (the master mute, and a 음량 button opening the volume window), the reel-gesture ring (circle
    /// + direction arrows), which way a drawn circle winds in (clockwise by default, counter-clockwise for players who
    /// prefer it) and the fishing view's zoom after the cast (끔 / 1.25배 (default) / 1.5배 / 액티브, <see cref="ZoomMode"/>).
    /// The volume window (<see cref="OpenAudio"/>, Docs/music.md 4) stacks over it: 전체 볼륨 (with 소리 켜짐/꺼짐), 배경음악
    /// (with 배경음 켜짐/꺼짐), 효과음, 환경음 — each a 10-step bar with − / + (tap a cell, or drag along the bar).
    /// </summary>
    public static class SettingsUI
    {
        // (four rows 72 apart in a 444-high window: the window and its ribbon stay 26 px inside the smallest canvas, 540 high)
        const float Width = 700, Height = 444, RowStep = 72, FirstRowY = -96;
        /// <summary>The volume window's columns: label, −, the bar, +, the percent, the toggle (right edge at Width − 44).</summary>
        const float LabelX = 44, LabelW = 120, MinusX = 172, BtnW = 52, BarX = 230, BarW = 200, BarH = 32, PlusX = 436,
            PctX = 496, PctW = 56, ToggleW = 96;

        public static void Open()
        {
            var w = Dialog.Window("설정", new Vector2(Width, Height), out var close);
            Row(w, 0, "소리", () => Game.Data.soundOn, on => Game.I.SetSound(on), toggleRight: -44 - 120 - 12);
            var vol = UIKit.Button(w, "음량", "blue", OpenAudio, new Vector2(120, 52), 20, null, "Volume");
            vol.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-44, FirstRowY), new Vector2(120, 52), new Vector2(1, 0.5f));
            Row(w, 1, "릴 감기 원 · 방향 화살표", () => Game.Data.reelRing, on => Game.I.SetReelRing(on));
            Row(w, 2, "원을 그려 감는 방향", () => Game.Data.reelReverse, on => Game.I.SetReelReverse(on),
                "반시계방향", "시계방향", "blue", "blue");
            Choice(w, 3, "캐스팅 후 줌인", () => Game.Data.zoomMode, v => Game.I.SetZoomMode((ZoomMode)v),
                ("끔", (int)ZoomMode.Off), ("1.25배", (int)ZoomMode.X125), ("1.5배", (int)ZoomMode.X150), ("액티브", (int)ZoomMode.Active));
            var done = UIKit.Button(w, "닫기", "grey", close, new Vector2(160, 56));
            done.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(160, 56), new Vector2(0.5f, 0));
        }

        /// <summary>The volume window (over 설정; 닫기 goes back to it).</summary>
        public static void OpenAudio()
        {
            var w = Dialog.Window("음량", new Vector2(Width, Height), out var close);
            Level(w, 0, "전체 볼륨", AudioChannel.Master, ("소리", () => Game.Data.soundOn, on => Game.I.SetSound(on)));
            Level(w, 1, "배경음악", AudioChannel.Music, ("배경음", () => Game.Data.musicOn, on => Game.I.SetMusic(on)));
            Level(w, 2, "효과음", AudioChannel.Effects);
            Level(w, 3, "환경음", AudioChannel.Ambience);
            var done = UIKit.Button(w, "닫기", "grey", close, new Vector2(160, 56));
            done.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(160, 56), new Vector2(0.5f, 0));
        }

        /// <summary>A small "설정" button for a screen corner.</summary>
        public static Button CornerButton(Transform parent) => UIKit.Button(parent, "설정", "grey", Open, new Vector2(96, 56), 20);

        static void Row(RectTransform w, int i, string label, Func<bool> get, Action<bool> set,
            string onText = "켜짐", string offText = "꺼짐", string onStyle = "green", string offStyle = "grey", float toggleRight = -44)
        {
            float y = FirstRowY - i * RowStep;
            var t = UIKit.Label(w, label, 20, UIKit.Ink, TextAnchor.MiddleLeft, false);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(44, y), new Vector2(300, 52), new Vector2(0, 0.5f));
            var b = Toggle(w, get, set, new Vector2(150, 52), onText, offText, onStyle, offStyle, "Toggle_" + label);
            b.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(toggleRight, y), new Vector2(150, 52), new Vector2(1, 0.5f));
        }

        static Button Toggle(RectTransform w, Func<bool> get, Action<bool> set, Vector2 size,
            string onText = "켜짐", string offText = "꺼짐", string onStyle = "green", string offStyle = "grey", string name = "Toggle")
        {
            Button b = null;
            Action show = () =>
            {
                bool on = get();
                b.SetLabel(on ? onText : offText);
                b.SetStyle(on ? onStyle : offStyle);
            };
            b = UIKit.Button(w, onText, onStyle, () =>
            {
                set(!get());
                show();
            }, size, 20, null, name);
            show();
            return b;
        }

        /// <summary>
        /// A volume row: the label, −, a 10-cell bar (a tap or a drag sets the step under the pointer; left of the first
        /// cell's first quarter is 0), +, the percent, and an optional on/off toggle at the right.
        /// </summary>
        static void Level(RectTransform w, int i, string label, AudioChannel ch, (string name, Func<bool> get, Action<bool> set)? toggle = null)
        {
            float y = FirstRowY - i * RowStep;
            var t = UIKit.Label(w, label, 20, UIKit.Ink, TextAnchor.MiddleLeft, false);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(LabelX, y), new Vector2(LabelW, 52), new Vector2(0, 0.5f));

            var bg = UIKit.Panel(w, "bar_bg", null, "VolumeBar_" + ch);
            bg.raycastTarget = true;
            bg.rectTransform.At(new Vector2(0, 1), new Vector2(BarX, y), new Vector2(BarW, BarH), new Vector2(0, 0.5f));
            int n = AudioMix.Max / AudioMix.Step;
            var cells = new Image[n];
            float cw = (BarW - 8f) / n;
            for (int k = 0; k < n; k++)
            {
                var c = UIKit.Solid(bg.transform, Color.white, "Cell" + k);
                c.raycastTarget = false;
                c.rectTransform.At(new Vector2(0, 0.5f), new Vector2(4f + k * cw + 1f, 0f), new Vector2(cw - 2f, BarH - 10f), new Vector2(0, 0.5f));
                cells[k] = c;
            }
            var pct = UIKit.Label(w, "", 18, UIKit.Ink, TextAnchor.MiddleRight, false);
            pct.rectTransform.At(new Vector2(0, 1), new Vector2(PctX, y), new Vector2(PctW, 52), new Vector2(0, 0.5f));
            pct.name = "Percent_" + ch;

            var on = new Color32(0x6a, 0xe0, 0x6a, 0xff);
            var off = new Color(0f, 0f, 0f, 0.28f);
            Action show = () =>
            {
                int v = AudioMix.Get(Game.Data, ch);
                for (int k = 0; k < n; k++) cells[k].color = (k + 1) * AudioMix.Step <= v ? (Color)on : off;
                pct.text = v + "%";
            };
            Action<int> set = v =>
            {
                v = AudioMix.Snap(v);
                if (v == AudioMix.Get(Game.Data, ch)) return;
                Game.I.SetVolume(ch, v);
                show();
                // (a preview of the effects at their new level; the music and the ambience are heard changing live)
                if (ch == AudioChannel.Effects) Sfx.Play(Sfx.Click);
            };
            var drag = bg.gameObject.AddComponent<VolumeBar>();
            drag.Set = f => set(Mathf.Clamp(Mathf.FloorToInt(f * n + 0.75f), 0, n) * AudioMix.Step);

            var minus = UIKit.Button(w, "-", "grey", () => set(AudioMix.Get(Game.Data, ch) - AudioMix.Step), new Vector2(BtnW, 52), 24, null, "Minus_" + ch);
            minus.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(MinusX, y), new Vector2(BtnW, 52), new Vector2(0, 0.5f));
            var plus = UIKit.Button(w, "+", "grey", () => set(AudioMix.Get(Game.Data, ch) + AudioMix.Step), new Vector2(BtnW, 52), 24, null, "Plus_" + ch);
            plus.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(PlusX, y), new Vector2(BtnW, 52), new Vector2(0, 0.5f));
            if (toggle is { } tg)
            {
                var b = Toggle(w, tg.get, tg.set, new Vector2(ToggleW, 52), name: "Toggle_" + tg.name);
                b.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-44, y), new Vector2(ToggleW, 52), new Vector2(1, 0.5f));
            }
            show();
        }

        /// <summary>A volume bar taking a tap or a drag: <see cref="Set"/> gets the pointer's x as 0..1 of the bar's cells.</summary>
        class VolumeBar : MonoBehaviour, IPointerDownHandler, IDragHandler, IBeginDragHandler
        {
            public Action<float> Set;

            public void OnPointerDown(PointerEventData e) => At(e);
            public void OnBeginDrag(PointerEventData e) => At(e);
            public void OnDrag(PointerEventData e) => At(e);

            void At(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var p)) return;
                float x = (p.x - rt.rect.xMin - 4f) / Mathf.Max(1f, rt.rect.width - 8f);
                Set?.Invoke(Mathf.Clamp01(x));
            }
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
