using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>Coins + level/xp display that follows Game.Changed.</summary>
    public class TopBar : MonoBehaviour
    {
        Text coins, level;
        Image xp;
        int shownCoins = -1;
        float coinAnim;
        int targetCoins;

        public static TopBar Create(Transform canvas)
        {
            var root = UIKit.Rect(canvas, "TopBar").At(new Vector2(1, 1), new Vector2(-12, -10), new Vector2(360, 56));
            var tb = root.gameObject.AddComponent<TopBar>();
            tb.Build(root);
            return tb;
        }

        void Build(RectTransform root)
        {
            var lvPanel = UIKit.Panel(root, "panel_dark", null, "Level");
            lvPanel.rectTransform.At(new Vector2(0, 0.5f), Vector2.zero, new Vector2(150, 52), new Vector2(0, 0.5f));
            var star = UIKit.Img(lvPanel.transform, Art.UI("icon_xp"), new Vector2(28, 28), "Star");
            star.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 6), new Vector2(28, 28), new Vector2(0, 0.5f));
            level = UIKit.Label(lvPanel.transform, "Lv.1", 20, UIKit.Cream, TextAnchor.MiddleLeft);
            level.rectTransform.Fill(44, 6, 2, 18);
            xp = UIKit.Bar(lvPanel.transform, new Vector2(120, 12), UIKit.Sky);
            xp.transform.parent.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(4, 8), new Vector2(124, 14), new Vector2(0.5f, 0));

            var coinPanel = UIKit.Panel(root, "panel_dark", null, "Coins");
            coinPanel.rectTransform.At(new Vector2(1, 0.5f), Vector2.zero, new Vector2(198, 52), new Vector2(1, 0.5f));
            var ci = UIKit.Img(coinPanel.transform, Art.UI("coin"), new Vector2(32, 32), "Coin");
            ci.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 2), new Vector2(32, 32), new Vector2(0, 0.5f));
            coins = UIKit.Label(coinPanel.transform, "0", 24, UIKit.Gold, TextAnchor.MiddleRight);
            coins.rectTransform.Fill(46, 14, 2, 6);
            Game.I.Changed += Refresh;
            Refresh();
            shownCoins = targetCoins;
            coins.text = UIKit.Num(shownCoins);
        }

        void OnDestroy()
        {
            if (Game.I != null) Game.I.Changed -= Refresh;
        }

        void Refresh()
        {
            var d = Game.Data;
            level.text = "Lv." + d.level;
            UIKit.SetBar(xp, (float)d.xp / Game.XpToNext(d.level));
            if (d.coins != targetCoins && shownCoins >= 0) Tween.Punch(coins.transform.parent, 0.08f);
            targetCoins = d.coins;
        }

        void Update()
        {
            if (shownCoins == targetCoins) return;
            coinAnim += Time.unscaledDeltaTime;
            int diff = targetCoins - shownCoins;
            int step = Mathf.Max(1, Mathf.Abs(diff) / 8);
            shownCoins += Math.Sign(diff) * Mathf.Min(step, Mathf.Abs(diff));
            coins.text = UIKit.Num(shownCoins);
        }
    }

    /// <summary>
    /// Short messages, persistent across scenes. They stack under the top bar, or along the bottom edge while a
    /// modal is open so they never cover a window's title or tabs.
    /// </summary>
    public static class Toast
    {
        static Canvas canvas;
        static readonly List<RectTransform> top = new List<RectTransform>();
        static readonly List<RectTransform> bottom = new List<RectTransform>();

        static Canvas C
        {
            get
            {
                if (canvas != null) return canvas;
                canvas = UIKit.CreateCanvas("[Toasts]", 30000);
                UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
                canvas.GetComponent<GraphicRaycaster>().enabled = false;
                Game.I.LeveledUp += lv =>
                {
                    Sfx.Play(Sfx.LevelUp, 0.8f);
                    Show($"레벨 업!  Lv.{lv}   보상 +{UIKit.Num(lv * 100)} 코인", UIKit.Gold, 2.6f);
                };
                return canvas;
            }
        }

        public static void Init() { _ = C; }

        /// <summary>Removes every toast on screen (called when a modal opens).</summary>
        public static void Clear()
        {
            foreach (var r in top) if (r != null) UnityEngine.Object.Destroy(r.gameObject);
            foreach (var r in bottom) if (r != null) UnityEngine.Object.Destroy(r.gameObject);
            top.Clear();
            bottom.Clear();
        }

        public static void Show(string msg, Color? color = null, float time = 1.8f)
        {
            top.RemoveAll(r => r == null);
            bottom.RemoveAll(r => r == null);
            bool low = UIKit.ModalCount > 0;
            var list = low ? bottom : top;
            var p = UIKit.Panel(C.transform, "panel_dark", null, "Toast");
            var rt = p.rectTransform;
            var t = UIKit.Label(rt, msg, 20, color ?? UIKit.Cream);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.rectTransform.Fill(14, 14, 4, 6);
            float w = Mathf.Clamp(t.preferredWidth + 48, 220, 860);
            if (low) rt.At(new Vector2(0.5f, 0), new Vector2(0, 8 + list.Count * 56), new Vector2(w, 50), new Vector2(0.5f, 0));
            else rt.At(new Vector2(0.5f, 1), new Vector2(0, -84 - list.Count * 56), new Vector2(w, 50), new Vector2(0.5f, 1));
            list.Add(rt);
            Tween.Pop(rt, 0.7f);
            var cg = p.gameObject.AddComponent<CanvasGroup>();
            Tween.After(time, () =>
            {
                if (cg == null) return;
                // fade the text alone: 8 stacked outline copies would turn into a dark smudge while translucent
                var o = t != null ? t.GetComponent<PixelOutline>() : null;
                if (o != null) o.enabled = false;
                Tween.Fade(cg, 0, 0.3f, () =>
                {
                    list.Remove(rt);
                    if (rt != null) UnityEngine.Object.Destroy(rt.gameObject);
                });
            });
        }
    }
    /// <summary>Modal dialogs.</summary>
    public static class Dialog
    {
        static Canvas canvas;

        static Canvas C
        {
            get
            {
                if (canvas != null) return canvas;
                canvas = UIKit.CreateCanvas("[Dialogs]", 20000);
                UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
                return canvas;
            }
        }

        public static bool Open => canvas != null && canvas.transform.childCount > 0;

        public static RectTransform Window(string title, Vector2 size, out Action close, bool closeOnBackground = true)
        {
            RectTransform dim = null;
            Action doClose = () => { if (dim != null) UnityEngine.Object.Destroy(dim.gameObject); };
            dim = UIKit.Modal(C.transform, closeOnBackground ? doClose : null);
            var p = UIKit.Panel(dim, "panel_wood", null, "Window");
            p.raycastTarget = true;
            p.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, size);
            if (!string.IsNullOrEmpty(title)) UIKit.Ribbon(p.transform, title, "yellow", 240, size.x - 40, 22);
            Tween.Pop(p.transform, 0.75f);
            close = doClose;
            return p.rectTransform;
        }

        public static void Confirm(string title, string message, string ok, Action onOk, string cancel = "취소", string okStyle = "green")
        {
            var w = Window(title, new Vector2(600, 300), out var close);
            var t = UIKit.Label(w, message, 20, UIKit.Ink, TextAnchor.MiddleCenter, false);
            t.rectTransform.Fill(44, 44, 56, 100);
            // grow the window to fit the message (pixel glyphs are wider than a proportional font)
            float textH = t.preferredHeight;
            float h = Mathf.Clamp(textH + 172, 300, 500);
            w.sizeDelta = new Vector2(w.sizeDelta.x, h);
            var b1 = UIKit.Button(w, ok, okStyle, () => { close(); onOk?.Invoke(); }, new Vector2(190, 60));
            b1.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(cancel != null ? 105 : 0, 32), new Vector2(190, 60), new Vector2(0.5f, 0));
            if (cancel != null)
            {
                var b2 = UIKit.Button(w, cancel, "grey", close, new Vector2(190, 60));
                b2.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-105, 32), new Vector2(190, 60), new Vector2(0.5f, 0));
            }
        }

        public static void Info(string title, string message, string ok = "확인", Action onOk = null) =>
            Confirm(title, message, ok, onOk, null);
    }
}
