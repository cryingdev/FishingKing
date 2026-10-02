using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The reel in the bottom-right corner: its handle turns with the reel as the player draws circles anywhere on
    /// screen. A blinking ring on the handle's path and one short curved arrow orbiting slowly just outside the rim
    /// show which way to turn (green: wind in, orange: give line) at their own pace, not the reeling speed. The ring
    /// and arrow can be switched off in the settings.
    /// </summary>
    public class ReelGrip : MonoBehaviour
    {
        // art pixels of the sprites: reel_face / reel_handle are 64 px with the knob 24 px from the centre;
        // reel_arrow_* is an 88 px canvas centred on the reel with its short arc just outside the rim
        const float FaceArt = 64f, KnobArt = 24f, ArrowArt = 88f;
        const int Units = 2;                                     // canvas units per art pixel (128 unit reel)
        const float OrbitRevsPerSec = 0.3f, BlinkHz = 1.5f;
        // reel centre, from the bottom-right corner: far enough in for the arrow's orbit to stay on screen
        static readonly Vector2 ReelPos = new Vector2(-100, 104);
        static readonly Color WindColor = new Color32(0x6a, 0xe0, 0x6a, 0xff), GiveColor = new Color32(0xff, 0x8a, 0x4a, 0xff);

        /// <summary>
        /// What the reel covers (the arrow's orbit and the two-line label over it), in canvas units from the canvas's
        /// bottom-right corner (x negative), for overlays to keep clear of.
        /// </summary>
        public static Rect Zone => Rect.MinMaxRect(ReelPos.x - ArrowArt * Units * 0.5f - 12f, 0f, 0f, ReelPos.y + SpoolTextY + 11f + 6f);
        // the spool readout over the help label: what more line the reel can give, and a bar of it
        const float SpoolBarY = 140f, SpoolTextY = 156f;

        RectTransform root;
        Image face, handle, ring, arrow;
        Text label, spoolText;
        Image spoolFill;
        CircleGesture g;
        Sprite windArrow, giveArrow;
        bool tintArrow;
        float orbit;

        public static ReelGrip Create(RectTransform canvasRoot, CircleGesture gesture)
        {
            var root = UIKit.Rect(canvasRoot, "ReelGrip").At(new Vector2(1, 0), Vector2.zero, Vector2.zero, new Vector2(1, 0));
            var rg = root.gameObject.AddComponent<ReelGrip>();
            rg.root = root;
            rg.g = gesture;
            rg.Build();
            return rg;
        }

        void Build()
        {
            var reel = UIKit.Rect(root, "Reel").At(new Vector2(0.5f, 0.5f), ReelPos, Vector2.zero, new Vector2(0.5f, 0.5f));
            face = UIKit.Img(reel, Art.UI("reel_face"), Vector2.one * FaceArt * Units, "Face");
            // Art.Ring draws its ring ~1.5 px inside a 64 px square: sized so the ring lies on the knob's path
            ring = UIKit.Img(reel, Art.Ring, Vector2.one * KnobArt * Units * 2f * 64f / 61f, "Ring");
            handle = UIKit.Img(reel, Art.UI("reel_handle"), Vector2.one * FaceArt * Units, "Handle");
            windArrow = Art.UI("reel_arrow_wind");
            giveArrow = Art.UI("reel_arrow_give");
            tintArrow = windArrow == null || giveArrow == null; // fallback: the small code-drawn arrow, tinted
            arrow = UIKit.Img(reel, tintArrow ? Art.Arrow : windArrow,
                tintArrow ? new Vector2(9, 7) * Units * 1.5f : Vector2.one * ArrowArt * Units, "Arrow");
            label = UIKit.Label(root, "원을 그려 감기", 15, UIKit.Cream);
            label.rectTransform.At(new Vector2(0.5f, 0.5f), ReelPos + new Vector2(0, 100), new Vector2(170, 22));
            spoolFill = UIKit.Bar(root, new Vector2(124, 12), WindColor, "Spool");
            ((RectTransform)spoolFill.transform.parent).At(new Vector2(0.5f, 0.5f), ReelPos + new Vector2(0, SpoolBarY), new Vector2(124, 12));
            spoolText = UIKit.Label(root, "", 15, UIKit.Cream);
            spoolText.horizontalOverflow = HorizontalWrapMode.Overflow;
            spoolText.rectTransform.At(new Vector2(0.5f, 0.5f), ReelPos + new Vector2(0, SpoolTextY), new Vector2(190, 22));
        }

        /// <summary>
        /// The spool over the reel: how much more line it can give (<paramref name="cap"/>: the reel's capacity or what is
        /// left on the spool, whichever is less; <paramref name="lineOut"/> out now) as text and a bar of what is left,
        /// green, gold from 70 % out, red from 90 %.
        /// </summary>
        public void SetSpool(float lineOut, float cap)
        {
            float used = cap > 0.01f ? Mathf.Clamp01(lineOut / cap) : 1f;
            string t = $"남은 줄 {Mathf.Max(0f, cap - lineOut):0}m";
            if (spoolText.text != t) spoolText.text = t;
            var c = used >= 0.9f ? UIKit.Bad : used >= 0.7f ? UIKit.Gold : WindColor;
            spoolFill.color = c;
            spoolText.color = used >= 0.7f ? c : UIKit.Cream;
            UIKit.SetBar(spoolFill, 1f - used);
        }

        public void Show(bool on) => root.gameObject.SetActive(on);

        /// <summary>The help text above the reel (one or two lines).</summary>
        public void SetLabel(string text)
        {
            label.text = text;
            bool two = text.Contains("\n");
            label.rectTransform.sizeDelta = new Vector2(two ? 190 : 170, two ? 42 : 22);
            label.rectTransform.anchoredPosition = ReelPos + new Vector2(0, two ? 110 : 100);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            handle.rectTransform.localRotation = Quaternion.Euler(0, 0, -g.TotalRevs * 360f);

            // help: blinking ring + one short arrow orbiting slowly the way to turn
            bool help = Game.Data.reelRing;
            ring.enabled = arrow.enabled = help;
            if (!help) return;
            bool on = Mathf.Repeat(Time.unscaledTime * BlinkHz, 1f) < 0.62f;
            bool turning = g.Circling && Mathf.Abs(g.Speed) > 0.15f;
            bool wind = !turning || g.Speed > 0f;              // idle, it shows the way to wind in
            bool clockwise = wind != CircleGesture.Reversed;
            orbit += (clockwise ? -1f : 1f) * OrbitRevsPerSec * 360f * dt;
            var col = wind ? WindColor : GiveColor;
            var rc = col;
            rc.a = on ? 0.9f : 0.3f; // the handle's path, in the direction colour so it stands out on the pale face
            ring.color = rc;
            if (tintArrow)
            {
                col.a = on ? 1f : 0.4f;
                arrow.color = col;
                // small fallback arrow: ride the orbit just outside the face, pointing along it
                float a = orbit * Mathf.Deg2Rad, r = (FaceArt * 0.5f + 6f) * Units;
                arrow.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, orbit + (clockwise ? -90f : 90f));
            }
            else
            {
                arrow.sprite = wind ? windArrow : giveArrow;
                arrow.color = new Color(1, 1, 1, on ? 1f : 0.4f);
                // the sprite's arc runs counter-clockwise round the reel centre; mirrored it runs clockwise
                arrow.rectTransform.localScale = new Vector3(clockwise ? -1f : 1f, 1f, 1f);
                arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, orbit);
            }
        }
    }
}
