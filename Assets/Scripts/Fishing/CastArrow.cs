using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The flick wind-up's "throw it out there" arrow (Tools/Blender/fk_items.py castarrow: Sprites/UI/cast_arrow_f0..f7,
    /// 26 x 38 px, rendered through the game camera): a straight gold block arrow (a slab with real thickness: the right
    /// wall and the near butt show as darker faces) lying forward just above the angler's hat and tilted up, so it points
    /// straight up the screen, away over the far water, its near butt wider than its far end. It sits centred over the hat
    /// with a few pixels' gap above the crown (clear of the rod and his raised arms). It floats in the pixel
    /// scene over the angler and his rod (under the HUD), hanging off his hat (<see cref="Angler.HatPos2D"/>: the 3D
    /// figure's head bone, or the pose sprite's hat), so it moves with him, the boat's bob included, and shifts a few pixels
    /// with the rod's lean so it feels attached (no rotation: the pixel art stays crisp). The controller shows it while
    /// winding up (not on the ice): the plain frame (f0), dim while the finger is still pulling down, brightening as it nears
    /// <see cref="FlickCast.WindUpMin"/>; once armed, full and looping a light glint from the butt up to the tip
    /// (f1..f7, <see cref="GlintStep"/> each), then a short rest on the plain frame
    /// (<see cref="Hold"/>): <see cref="Loop"/> s per loop. Hidden the moment the wind-up ends (release, throw, cancel).
    /// </summary>
    [DefaultExecutionOrder(100)] // after the Angler's LateUpdate has posed him: the arrow hangs off this frame's head
    public class CastArrow : MonoBehaviour
    {
        /// <summary>Over the angler (50), the rod in front of him (53) and its dangling bait (55); under the sparkles (60).</summary>
        public const int Order = Fx.OrderSparkle - 2;
        // the sprite's centre from the hat's middle (px, x right, y up), printed by fk_items.py build_cast_arrow: the
        // shaft's axis right over the hat's middle (the centre sits 1.1 px right of it for the right wall), the sprite's
        // bottom edge 7.9 px above the hat's middle = ~3 px clear of the crown (~4.75 px above it); the head ~30-45 px up
        static readonly Vector2 OffPx = new Vector2(1.1f, 26.9f);
        const float LeanPx = 0.1f;          // px sideways per degree the rod leans from its wind-up rest (-Angler.WindLeanOut): +-3 px
        const float DimFrom = 0.3f, DimTo = 0.6f; // alpha while pulling down, from the first pull to just short of armed
        /// <summary>Armed: seconds per glint frame (f1..f7, butt to tip).</summary>
        public const float GlintStep = 0.09f;
        /// <summary>Armed: seconds on the plain frame after the glint reaches the head.</summary>
        public const float Hold = 0.27f;
        const int FrameCount = 8;           // f0 plain + f1..f7 glint
        /// <summary>One armed loop (s): the glint's run plus the rest.</summary>
        public const float Loop = GlintStep * (FrameCount - 1) + Hold;

        Angler angler;
        SpriteRenderer sr;
        Sprite[] frames;
        bool show, armed;
        float pull01, armedT;

        /// <summary>True while it is drawn (for the test autopilot).</summary>
        public bool Visible => sr != null && sr.enabled;
        /// <summary>The frame shown (0 = plain, 1..7 = the glint from the butt to the tip).</summary>
        public int Frame { get; private set; }
        /// <summary>A glint frame is up (for the test autopilot).</summary>
        public bool Bright => Visible && Frame > 0;
        public float Alpha => sr != null ? sr.color.a : 0f;
        /// <summary>The sprite's centre in the pixel scene (whole pixels).</summary>
        public Vector2 Pos => transform.position;

        public static CastArrow Create(Angler a)
        {
            var go = new GameObject("CastArrow");
            var c = go.AddComponent<CastArrow>();
            c.angler = a;
            c.sr = go.AddComponent<SpriteRenderer>();
            c.sr.sortingOrder = Order;
            c.sr.enabled = false;
            c.frames = new Sprite[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                var s = Art.UI("cast_arrow_f" + i);
                c.frames[i] = s != null ? s : (i > 0 ? c.frames[0] : null);
            }
            return c;
        }

        /// <summary>
        /// Winding up this frame: <paramref name="pulled"/> = the finger has started pulling down (or the wind-up is armed),
        /// <paramref name="isArmed"/> = pulled far enough to throw, <paramref name="pull"/> = 0..1 of the way to armed.
        /// </summary>
        public void Set(bool pulled, bool isArmed, float pull)
        {
            show = pulled || isArmed;
            if (isArmed && !armed) armedT = 0f; // the loop starts with the glint at the butt
            armed = isArmed;
            pull01 = Mathf.Clamp01(pull);
        }

        /// <summary>Gone at once (the wind-up ended: thrown, called off, or any other state).</summary>
        public void Hide()
        {
            show = armed = false;
            if (sr != null) sr.enabled = false;
        }

        void LateUpdate()
        {
            if (!show || angler == null || frames == null || frames[0] == null)
            {
                sr.enabled = false;
                return;
            }
            float alpha;
            if (armed)
            {
                armedT += Time.deltaTime;
                float t = Mathf.Repeat(armedT, Loop);
                int k = Mathf.FloorToInt(t / GlintStep);
                Frame = k < FrameCount - 1 ? k + 1 : 0; // f1..f7, then f0 for the rest
                alpha = 1f;
            }
            else
            {
                Frame = 0;
                alpha = Mathf.Lerp(DimFrom, DimTo, pull01);
            }
            sr.sprite = frames[Frame];
            float lean = Mathf.Round((angler.RodAngles.y + angler.HandSign * Angler.WindLeanOut) * LeanPx);   // (from the rest lean: out to the rod hand's side)
            var p = angler.HatPos2D + (OffPx + new Vector2(lean, 0f)) / PixelView.PPU;
            transform.position = new Vector3(Mathf.Round(p.x * PixelView.PPU) / PixelView.PPU, Mathf.Round(p.y * PixelView.PPU) / PixelView.PPU, 0f);
            sr.color = new Color(1f, 1f, 1f, alpha);
            sr.enabled = true;
        }
    }
}
