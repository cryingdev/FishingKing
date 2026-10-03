using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The fight's side-pressure arrow ("push the rod this way"): the cast arrow's gold block arrow turned on its side
    /// (Tools/Blender/fk_items.py sidearrow: Sprites/UI/side_arrow_f0..f7 + side_arrow_on, 26 x 20 px canvas, the arrow
    /// 24 x 17 px, pointing right; flipped for a push to the left), floating a few pixels over the spot the player watches:
    /// over a float rig's float while it rides the surface on the line (<see cref="Tackle.FloatRiding"/>: over its top),
    /// else where the line cuts the surface over the hooked fish (<see cref="Angler.WaterEntry"/>: a lure, or the float
    /// pulled under), or over the fish's mouth when it is up at the surface and the line runs straight to it. It points the way to lean the rod: against the fish's run
    /// (-<see cref="FishingController.FishRun"/>), and flips when a new run heads the other way (a quick fade out, then
    /// in, pointing the new way).
    /// <para>Shown only while side pressure counts (<see cref="FishingController.SideActive"/>: a run to one side while
    /// the fish really sweeps sideways that way, or a run for cover; not resting, not jumping, not worn out, not on the
    /// ice; so never while the fish runs straight out or comes in straight, or outside the fight), fading in over <see cref="FadeIn"/> s and out over <see cref="FadeOut"/> s (a rest or a jump
    /// starting). Its three states follow the
    /// fight strip's word (<see cref="FishingController.SideNow"/> past +-<see cref="Deadband"/>):</para>
    /// <list type="bullet">
    /// <item>not pushed: a prompt loop (<see cref="Loop"/> s): the glint runs from the tail to the tip (f1..f7) while
    /// the arrow brightens and nudges up to <see cref="NudgePx"/> px the way to push, then it rests dimmer on f0;</item>
    /// <item>pushed the right way ("사이드 프레셔!"): the bright frame (side_arrow_on: lighter gold and a light halo), at
    /// full strength, with a small scale pop (<see cref="PopScale"/> over <see cref="PopTime"/> s) as it lights up;</item>
    /// <item>pushed the wrong way ("◀/▶ 반대쪽으로!"): the plain frame shaking sideways +-<see cref="ShakePx"/> px.</item>
    /// </list>
    /// <para>Perspective-scaled a little with the anchor's distance (<see cref="Persp.ScaleAt(Vector3)"/> to the power
    /// <see cref="ScalePow"/>, never under <see cref="MinScale"/>: the arrow stays readable far out; its width snapped
    /// to whole pixels), always whole-pixel positioned, sorted over the water effects, the fish, the line and the angler
    /// (<see cref="Order"/>) but under the HUD. The fight over (any other state): gone at once.</para>
    /// <para>Snagged it points a free way (<see cref="FishingController.SnagArrowDir"/>): sideways the side to sweep the rod;
    /// for a rig caught on a prop and settled on slack also the rod's pitch, turned a quarter round to point the way the
    /// FINGER slides (down to lift the rod, up to lower it; Docs/obstacles_spec.md 6.10).</para>
    /// </summary>
    [DefaultExecutionOrder(100)] // after the Angler's LateUpdate (the line's water entry) and the Tackle's (90: the float) of this frame
    public class SideArrow : MonoBehaviour
    {
        /// <summary>
        /// Over the water effects (splashes 44), the fish (42), the stage's front layer (40), the line (46), and the angler
        /// and his rod (50 / 53: a fish fought in close puts the line's entry right by him, and the arrow must still read);
        /// under the sparkles (60) and the HUD.
        /// </summary>
        public const int Order = Angler.OrderRodFront + 4;
        const float BottomPx = 9f;      // the arrow's lowest opaque row below the sprite's centre (fk_items.py build_side_arrow)
        const float GapPx = 4f;         // clear air between the anchor (the line's entry ring) and the arrow's bottom
        const float WidthPx = 26f;      // the sprite's width (for whole-pixel scaling)
        public const float MinScale = 0.7f, ScalePow = 0.35f;
        public const float Deadband = FishingController.SideDead;   // |SideNow| over this = pushed (the model, the HUD's word and strip use the same)
        const int FrameCount = 8;       // f0 plain + f1..f7 glint
        /// <summary>Prompt: seconds per glint frame (tail to tip), then the rest on the plain frame.</summary>
        public const float GlintStep = 0.07f, Hold = 0.34f;
        public const float Loop = GlintStep * (FrameCount - 1) + Hold;
        const float PulseLo = 0.6f;     // the prompt's alpha at rest (1 while the glint runs)
        public const float NudgePx = 2f;
        public const float FadeIn = 0.15f, FadeOut = 0.12f;
        public const float PopTime = 0.2f, PopScale = 0.22f;
        public const float ShakePx = 2f, ShakeHz = 11f;

        public enum Mode { Prompt, Right, Wrong }

        FishingController ctl;
        SpriteRenderer sr;
        Sprite[] frames;
        Sprite onFrame;
        float fade, t, popT = 99f;
        Vector2Int shownDir;
        int shownSide => shownDir.x;
        Mode lastMode;
        bool hidden = true;

        /// <summary>Drawn this frame (for the test autopilot).</summary>
        public bool Visible => sr != null && sr.enabled;
        /// <summary>The state shown (meaningful while visible).</summary>
        public Mode State { get; private set; }
        /// <summary>The way it points: +1 right, -1 left (0 before the first run, or pointing up / down).</summary>
        public int Side => shownSide;
        /// <summary>Snagged: the rod's pitch it shows, +1 lift (the arrow points down: slide down), -1 lower (points up); 0 sideways.</summary>
        public int Pitch => shownDir.y;
        /// <summary>The frame shown: 0 plain, 1..7 the glint, 8 the bright "on" frame.</summary>
        public int Frame { get; private set; }
        public float Alpha => sr != null ? sr.color.a : 0f;
        /// <summary>The sprite's scale this frame (perspective and pop).</summary>
        public float Scale { get; private set; } = 1f;
        /// <summary>The sprite's centre in the pixel scene (whole pixels).</summary>
        public Vector2 Pos => transform.position;
        /// <summary>The point it floats over (the line's water entry / the fish's mouth at the surface), in the pixel scene.</summary>
        public Vector2 AnchorPos2D { get; private set; }
        /// <summary>What it floats over this frame: "float" (a float riding the line), "entry" (the line's water entry) or "mouth" (the fish up at the surface).</summary>
        public string AnchorKind { get; private set; } = "";
        /// <summary>Pixels (native) from the anchor up to the arrow's lowest row this frame.</summary>
        public float GapNow { get; private set; }

        public static SideArrow Create(FishingController c)
        {
            var go = new GameObject("SideArrow");
            var a = go.AddComponent<SideArrow>();
            a.ctl = c;
            a.sr = go.AddComponent<SpriteRenderer>();
            a.sr.sortingOrder = Order;
            a.sr.enabled = false;
            a.frames = new Sprite[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                var s = Art.UI("side_arrow_f" + i);
                a.frames[i] = s != null ? s : (i > 0 ? a.frames[0] : null);
            }
            var on = Art.UI("side_arrow_on");
            a.onFrame = on != null ? on : a.frames[0];
            return a;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool fighting = ctl != null && ctl.State == FishingController.S.Fighting && ctl.Hooked != null && frames[0] != null;
            // snag mode (Docs/obstacles_spec.md 6.5): after a wrong sweep or a failed 톡 it points the way that frees the rig
            bool snag = ctl != null && ctl.State == FishingController.S.Snagged && frames[0] != null;
            // the fight over (landed, broken off, got away): gone at once (the line no longer runs to the fish)
            if (!fighting && !snag) fade = 0f;
            var snagDir = snag ? ctl.SnagArrowDir : Vector2Int.zero;
            bool want = fighting ? ctl.SideActive : snag && snagDir != Vector2Int.zero;
            var dir = want ? (fighting ? new Vector2Int(-ctl.FishRun, 0) : snagDir) : shownDir;
            // a new run the other way (a new free way): out first, then back in pointing the new way
            if (want && shownDir != Vector2Int.zero && dir != shownDir && fade > 0f) want = false;
            if (fade <= 0f && dir != Vector2Int.zero && dir != shownDir) shownDir = dir;
            fade = Mathf.MoveTowards(fade, want ? 1f : 0f, dt / (want ? FadeIn : FadeOut));
            if (fade <= 0f || shownDir == Vector2Int.zero || (!fighting && !snag))
            {
                sr.enabled = false;
                hidden = true;
                return;
            }

            // the state: from the side pressure while it counts, frozen as it fades out
            if (want)
            {
                // (snagged: bright while the rod is held swept / pitched the free way, else the prompt)
                float s = fighting ? ctl.SideNow
                    : shownDir.y != 0 ? (Mathf.Abs(ctl.PitchNow) >= 0.5f && Mathf.Sign(ctl.PitchNow) == shownDir.y ? 1f : 0f)
                    : (Mathf.Abs(ctl.LeanReq) >= 0.5f && Mathf.Sign(ctl.LeanReq) == shownDir.x ? 1f : 0f);
                State = s > Deadband ? Mode.Right : s < -Deadband ? Mode.Wrong : Mode.Prompt;
            }
            if (hidden || State != lastMode)
            {
                // coming up (or changing state): the prompt loop starts over with the glint at the tail, the bright frame pops
                t = 0f;
                popT = State == Mode.Right ? 0f : 99f;
                lastMode = State;
                hidden = false;
            }
            t += dt;
            popT += dt;

            // what it floats over (the water: no deck bob): the float riding the line (float rigs; its top at rest), else
            // the line's entry (a lure, or the float pulled under), else the fish's mouth up at the surface
            var a = ctl.Angler;
            var tk = ctl.Tackle;
            var P = ctl.Stage.P;
            Vector3 at;
            Vector2 p0;
            if (snag && !a.LineUnderwater)
            {
                // a snagged float rig: over the float lying pulled down
                at = tk.Surface;
                p0 = P.To2D(at) + new Vector2(0f, tk.FloatScale * 0.95f);
                AnchorKind = "float";
            }
            else if (tk != null && tk.FloatRiding)
            {
                at = tk.FloatAt;
                p0 = tk.FloatTop2D;
                AnchorKind = "float";
            }
            else if (a.LineUnderwater)
            {
                at = a.WaterEntry;
                p0 = P.To2D(at);
                AnchorKind = "entry";
            }
            else
            {
                var m = ctl.Hooked.MouthPos;
                at = new Vector3(m.x, 0f, m.z);
                p0 = P.To2D(at);
                AnchorKind = "mouth";
            }
            AnchorPos2D = p0;

            float scale = Mathf.Clamp(Mathf.Pow(Mathf.Max(0.01f, P.ScaleAt(at)), ScalePow), MinScale, 1f);
            scale = Mathf.Max(2f, Mathf.Round(WidthPx * scale / 2f) * 2f) / WidthPx;   // an even whole number of pixels wide
            float alpha = fade, dx = 0f, pop = 1f;
            switch (State)
            {
                case Mode.Right:
                    Frame = FrameCount;
                    sr.sprite = onFrame;
                    if (popT < PopTime) pop = 1f + PopScale * Mathf.Sin(popT / PopTime * Mathf.PI);
                    break;
                case Mode.Wrong:
                    Frame = 0;
                    sr.sprite = frames[0];
                    dx = Mathf.Round(ShakePx * Mathf.Sin(t * ShakeHz * 2f * Mathf.PI));
                    break;
                default:
                {
                    float u = Mathf.Repeat(t, Loop);
                    int k = Mathf.FloorToInt(u / GlintStep);
                    Frame = k < FrameCount - 1 ? k + 1 : 0;   // f1..f7, then f0 for the rest
                    sr.sprite = frames[Frame];
                    float run = GlintStep * (FrameCount - 1);
                    // bright while the glint runs, easing down to PulseLo over the rest and back up just before the next
                    float w = u < run ? 1f : 0.5f + 0.5f * Mathf.Cos((u - run) / Hold * 2f * Mathf.PI);
                    alpha *= Mathf.Lerp(PulseLo, 1f, w);
                    // a nudge the way to push while the glint runs
                    dx = u < run ? Mathf.Round(NudgePx * Mathf.Sin(u / run * Mathf.PI)) : 0f;
                    break;
                }
            }
            Scale = scale * pop;
            // (the pitch: turned a quarter round to point the finger's way, down to lift the rod and up to lower it;
            // the nudge and the shake go along it)
            bool vert = shownDir.y != 0;
            sr.flipX = !vert && shownSide < 0;
            transform.rotation = vert ? Quaternion.Euler(0f, 0f, shownDir.y > 0 ? -90f : 90f) : Quaternion.identity;
            transform.localScale = new Vector3(Scale, Scale, 1f);
            float bottom = vert ? WidthPx * 0.5f : BottomPx;   // (turned, its lowest row is half its width below the centre)
            float up = GapPx + bottom * scale;   // (the pop grows round the centre: the gap shrinks a pixel or so meanwhile)
            var nudge = State == Mode.Wrong ? new Vector2(dx, 0f)
                : vert ? new Vector2(0f, -dx * shownDir.y) : new Vector2(dx * shownSide, 0f);
            var p = p0 + (nudge + new Vector2(0f, up)) / PixelView.PPU;
            var snapped = new Vector3(Mathf.Round(p.x * PixelView.PPU) / PixelView.PPU, Mathf.Round(p.y * PixelView.PPU) / PixelView.PPU, 0f);
            transform.position = snapped;
            GapNow = (snapped.y - p0.y) * PixelView.PPU - bottom * Scale;
            sr.color = new Color(1f, 1f, 1f, alpha);
            sr.enabled = true;
        }
    }
}
