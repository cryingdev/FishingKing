using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// Code-built UGUI helpers. Layouts are authored for a canvas of at least 960x540 units; every Blender
    /// UI sprite is drawn at 2 canvas units per art pixel and the canvas is scaled by a whole number of
    /// screen pixels per art pixel, so sprites and the Galmuri pixel text stay crisp.
    /// </summary>
    public static class UIKit
    {
        public const float Px = 2f;

        public static readonly Color Ink = new Color32(0x2a, 0x1e, 0x14, 0xff);
        public static readonly Color Cream = new Color32(0xfb, 0xf4, 0xe2, 0xff);
        public static readonly Color Gold = new Color32(0xff, 0xd2, 0x3a, 0xff);
        public static readonly Color Good = new Color32(0x6a, 0xe0, 0x6a, 0xff);
        public static readonly Color Bad = new Color32(0xff, 0x6a, 0x5a, 0xff);
        public static readonly Color Sky = new Color32(0x8a, 0xd0, 0xff, 0xff);
        public static readonly Color Dim = new Color(0, 0, 0, 0.55f);

        /// <summary>Whole screen pixels per art pixel for the UI (1 art px = <see cref="Px"/> canvas units).</summary>
        /// <remarks>Bounded so the canvas is always at least 960x540 units (the layouts need that much room).</remarks>
        public static int ArtScale => Mathf.Max(1, Mathf.Min(Screen.height / 270, Screen.width / 480));

        /// <summary>Canvas scale factor: integer art-pixel scaling keeps sprites and pixel text crisp.</summary>
        public static float ScaleFactor => ArtScale / Px;

        /// <summary>Canvas units per screen pixel (for placing UI at screen positions).</summary>
        public static float CanvasPerScreenPx => 1f / ScaleFactor;

        // ------------------------------------------------------------------ canvas
        public static Canvas CreateCanvas(string name, int order)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            c.pixelPerfect = true;
            var cs = go.AddComponent<CanvasScaler>();
            cs.referencePixelsPerUnit = PixelView.PPU;
            go.AddComponent<PixelCanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            return c;
        }
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (es != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ------------------------------------------------------------------ layout
        public static RectTransform Rect(Transform parent, string name = "Rect")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchor a rect at a normalized point of its parent.</summary>
        public static RectTransform At(this RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static T Layout<T>(this T c, float preferredHeight = -1, float preferredWidth = -1, float flexW = -1) where T : Component
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
            if (preferredWidth >= 0) le.preferredWidth = preferredWidth;
            if (flexW >= 0) le.flexibleWidth = flexW;
            return c;
        }

        // ------------------------------------------------------------------ graphics
        public static Image Panel(Transform parent, string sprite = "panel_wood", Color? tint = null, string name = "Panel")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Art.UI(sprite);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f / Px;
            img.color = tint ?? Color.white;
            return img;
        }

        public static Image Img(Transform parent, Sprite s, Vector2 size, string name = "Image", bool raycast = false)
        {
            var rt = Rect(parent, name);
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.preserveAspect = true;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Image sized at an integer multiple of the sprite's pixel size.</summary>
        public static Image PixelImg(Transform parent, Sprite s, float scale, string name = "Image")
        {
            var size = s != null ? new Vector2(s.rect.width, s.rect.height) * Px * scale : Vector2.one * 32;
            return Img(parent, s, size, name);
        }

        public static Image Solid(Transform parent, Color c, string name = "Solid")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = 22, Color? color = null,
            TextAnchor align = TextAnchor.MiddleCenter, bool outline = true, string name = "Label")
        {
            var rt = Rect(parent, name);
            var pick = PixelFonts.For(size);
            var t = rt.gameObject.AddComponent<PixelText>();
            t.Setup(pick.gridPx);
            t.font = pick.font;
            t.fontSize = pick.size;
            t.text = text;
            t.color = color ?? Cream;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.lineSpacing = pick.gridPx > 0 ? 1f : 1.05f; // Galmuri line height is a whole number of pixels
            if (outline) rt.gameObject.AddComponent<PixelOutline>().shadowOnly = pick.gridPx == 8;
            return t;
        }

        /// <summary>Title ribbon (yellow button plate) sized to its text, with the text below the highlight stripe.</summary>
        public static Image Ribbon(Transform parent, string text, string style = "yellow", float minWidth = 240, float maxWidth = 640,
            float y = 20)
        {
            var ribbon = Panel(parent, "btn_" + style, null, "Ribbon");
            var t = Label(ribbon.transform, text, 24);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = Mathf.Clamp(t.preferredWidth + 64, minWidth, maxWidth);
            ribbon.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(w, 56), new Vector2(0.5f, 1));
            t.rectTransform.Fill(8, 8, 10, 8);
            return ribbon;
        }

        /// <summary>
        /// Text on a translucent dark plate sized to the text, for hints floating over busy art. The plate never
        /// blocks input (raycastTarget off) so world gestures still start underneath it.
        /// </summary>
        public static Text PlateLabel(Transform parent, string text, int size, Color color, out Image plate)
        {
            plate = Panel(parent, "panel_dark", new Color(1, 1, 1, 0.72f), "Plate");
            plate.raycastTarget = false;
            var t = Label(plate.transform, text, size, color);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.rectTransform.Fill();
            FitPlate(t, plate);
            return t;
        }

        public static void FitPlate(Text t, Image plate)
        {
            plate.enabled = !string.IsNullOrEmpty(t.text);
            t.enabled = plate.enabled;
            plate.rectTransform.sizeDelta = new Vector2(t.preferredWidth + 28, Mathf.Max(34, t.preferredHeight + 12));
        }

        public static Button Button(Transform parent, string label, string style, Action onClick, Vector2 size,
            int fontSize = 22, Sprite icon = null, string name = "Button")
        {
            var img = Panel(parent, "btn_" + style, null, name);
            img.rectTransform.sizeDelta = size;
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
            colors.fadeDuration = 0.05f;
            b.colors = colors;
            b.onClick.AddListener(() =>
            {
                Sfx.Play(Sfx.Click);
                onClick?.Invoke();
            });
            float textLeft = 8;
            if (icon != null)
            {
                float isz = Mathf.Min(size.y - 12, 36);
                var ic = Img(img.transform, icon, new Vector2(isz, isz), "Icon");
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(10, 2), new Vector2(isz, isz), new Vector2(0, 0.5f));
                textLeft = isz + 12;
            }
            if (!string.IsNullOrEmpty(label))
            {
                var t = Label(img.transform, label, fontSize);
                t.rectTransform.Fill(icon != null ? textLeft : 6, 6, 4, 8);
            }
            return b;
        }

        public static Button IconButton(Transform parent, Sprite icon, string style, Action onClick, float size = 56, float iconScale = 0.62f)
        {
            var b = Button(parent, null, style, onClick, new Vector2(size, size));
            var ic = Img(b.transform, icon, Vector2.one * size * iconScale, "Icon");
            ic.rectTransform.anchoredPosition = new Vector2(0, 2);
            return b;
        }

        public static void SetLabel(this Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static void SetStyle(this Button b, string style)
        {
            var img = b.GetComponent<Image>();
            if (img != null) img.sprite = Art.UI("btn_" + style);
        }

        /// <summary>Horizontal bar; returns the fill image (use SetBar).</summary>
        public static Image Bar(Transform parent, Vector2 size, Color fill, string name = "Bar")
        {
            var bg = Panel(parent, "bar_bg", null, name);
            bg.rectTransform.sizeDelta = size;
            var f = Panel(bg.transform, "bar_fill", fill, "Fill");
            f.rectTransform.anchorMin = Vector2.zero;
            f.rectTransform.anchorMax = new Vector2(1, 1);
            f.rectTransform.offsetMin = new Vector2(2, 2);
            f.rectTransform.offsetMax = new Vector2(-2, -2);
            return f;
        }

        public static void SetBar(Image fill, float t)
        {
            t = Mathf.Clamp01(t);
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(t, 1);
            fill.enabled = t > 0.01f;
        }

        public static ScrollRect ScrollList(Transform parent, out RectTransform content, float spacing = 8, RectOffset padding = null)
        {
            var root = Rect(parent, "Scroll");
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var sr = root.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 40;
            sr.decelerationRate = 0.1f;
            var vp = Rect(root, "Viewport").Fill();
            vp.gameObject.AddComponent<RectMask2D>();
            content = Rect(vp, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = spacing;
            vl.padding = padding ?? new RectOffset(6, 6, 6, 6);
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp;
            sr.content = content;
            return sr;
        }

        public static ScrollRect ScrollGrid(Transform parent, out RectTransform content, Vector2 cell, Vector2 spacing)
        {
            var sr = ScrollList(parent, out content);
            UnityEngine.Object.DestroyImmediate(content.GetComponent<VerticalLayoutGroup>());
            var g = content.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = spacing;
            g.padding = new RectOffset(8, 8, 8, 8);
            g.childAlignment = TextAnchor.UpperCenter;
            return sr;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Num(int v) => v.ToString("N0");

        /// <summary>Number of open modals (dialogs, shop, encyclopedia, catch card).</summary>
        public static int ModalCount { get; internal set; }

        class ModalMarker : MonoBehaviour
        {
            void OnDestroy() => ModalCount = Mathf.Max(0, ModalCount - 1);
        }

        /// <summary>Full-screen dimmer that eats clicks; returns the dimmer rect.</summary>
        public static RectTransform Modal(Transform canvas, Action onBackground = null, float alpha = 0.6f)
        {
            Toast.Clear(); // a toast shown before the modal would sit on top of its header
            ModalCount++;
            var dim = Solid(canvas, new Color(0, 0, 0, alpha), "Modal");
            dim.raycastTarget = true;
            dim.rectTransform.Fill();
            dim.gameObject.AddComponent<ModalMarker>();
            if (onBackground != null)
            {
                var b = dim.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => onBackground());
            }
            return dim.rectTransform;
        }
    }

    /// <summary>Coroutine-based micro tweens for UI juice.</summary>
    public class Tween : MonoBehaviour
    {
        static Tween runner;

        static Tween R
        {
            get
            {
                if (runner == null)
                {
                    var go = new GameObject("[Tween]");
                    DontDestroyOnLoad(go);
                    runner = go.AddComponent<Tween>();
                }
                return runner;
            }
        }

        public static Coroutine Run(IEnumerator e) => R.StartCoroutine(e);

        public static void Pop(Transform t, float from = 0.6f, float time = 0.22f)
        {
            if (t != null) Run(PopCo(t, from, time));
        }

        static IEnumerator PopCo(Transform t, float from, float time)
        {
            float e = 0;
            while (e < time && t != null)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / time);
                float s = Mathf.LerpUnclamped(from, 1f, BackOut(k));
                t.localScale = new Vector3(s, s, 1);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        public static void Punch(Transform t, float amount = 0.15f, float time = 0.18f)
        {
            if (t != null) Run(PunchCo(t, amount, time));
        }

        static IEnumerator PunchCo(Transform t, float amount, float time)
        {
            float e = 0;
            while (e < time && t != null)
            {
                e += Time.unscaledDeltaTime;
                float k = e / time;
                float s = 1 + amount * Mathf.Sin(k * Mathf.PI) * (1 - k * 0.3f);
                t.localScale = new Vector3(s, s, 1);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        public static void Fade(CanvasGroup g, float to, float time, Action done = null)
        {
            if (g != null) Run(FadeCo(g, to, time, done));
        }

        static IEnumerator FadeCo(CanvasGroup g, float to, float time, Action done)
        {
            float from = g.alpha, e = 0;
            while (e < time && g != null)
            {
                e += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, e / time);
                yield return null;
            }
            if (g != null) g.alpha = to;
            done?.Invoke();
        }

        public static void FloatUp(RectTransform rt, float dist, float time, bool destroy = true)
        {
            if (rt != null) Run(FloatCo(rt, dist, time, destroy));
        }

        static IEnumerator FloatCo(RectTransform rt, float dist, float time, bool destroy)
        {
            var start = rt.anchoredPosition;
            var g = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
            float e = 0;
            while (e < time && rt != null)
            {
                e += Time.unscaledDeltaTime;
                float k = e / time;
                rt.anchoredPosition = start + new Vector2(0, dist * (1 - (1 - k) * (1 - k)));
                g.alpha = k < 0.7f ? 1 : 1 - (k - 0.7f) / 0.3f;
                yield return null;
            }
            if (rt != null && destroy) Destroy(rt.gameObject);
        }

        public static IEnumerator Delay(float s, Action a)
        {
            yield return new WaitForSecondsRealtime(s);
            a?.Invoke();
        }

        public static void After(float s, Action a) => Run(Delay(s, a));

        public static float BackOut(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1;
            return 1 + c3 * Mathf.Pow(k - 1, 3) + c1 * Mathf.Pow(k - 1, 2);
        }
    }
}
