using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The rod's sideways sweep (while a rig is in the water: the rod sweep, which bends a wound-in rig's path; in a fight:
    /// side pressure), read from a straight sideways slide on the water. It is STICKY: after the finger lifts the rod stays
    /// swept (so the same finger can then draw reel circles) until it is slid back the other way; a slide moves the sweep
    /// by its sideways travel (<see cref="FullTravel"/> W = the whole way from the centre to one side), a sweep left
    /// within <see cref="Snap"/> of the centre snaps to it. A / D (and the arrow keys) hold the full sweep while held.
    /// <para>Told apart from the other strokes the way <see cref="LureInput"/> tells a 톡 from a tap: net sideways travel
    /// at least <see cref="MinTravel"/> W, the stroke within <see cref="MaxAngle"/> of horizontal (both its chord and its
    /// latest direction), straight (no sample further than <see cref="Bow"/> chord lengths off the chord), never circling.
    /// A reel circle is a closed loop (the chord turns away and the gesture sees the circle); a 톡 is a pull DOWN (steeper
    /// than <see cref="MaxAngle"/>); a tap barely moves. While the stroke is still a straight slide the rod leans live
    /// (from <see cref="LiveTravel"/> W); a stroke that turns into a circle takes its live lean back. Letting go commits it,
    /// and so does holding still for <see cref="HoldCommit"/> s (slide, stop, then circle without lifting): still = within
    /// <see cref="StillMm"/> mm (a resting fingertip creeps and rolls by more than a pixel or two on a sharp screen).</para>
    /// <para>The keys only sweep once they have been let go since the sweep came on (A / D still held from walking at the
    /// ready sweep nothing); a bite holds the sweep (<see cref="Freeze"/>).</para>
    /// </summary>
    public class SideSlide
    {
        public const float MinTravel = 0.06f;    // W, net sideways, to commit
        public const float LiveTravel = 0.035f;  // W, the rod starts leaning live
        public const float MaxAngle = 30f;       // degrees from horizontal
        public const float LateAngle = 40f;      // the stroke's latest direction (over LateWindow) may be this steep
        public const float LateWindow = 0.05f;   // s
        public const float Bow = 0.12f;          // chord lengths
        public const float FullTravel = 0.2f;    // W of slide from the centre to the full sweep
        public const float Snap = 0.2f;          // |value| under this after a slide = back to the centre
        public const float HoldCommit = 0.2f;    // s still (within StillMm) while live: commit and start a new stroke
        public const float StillMm = 1.2f;       // mm a finger held still may still drift (no dpi known: 0.006 W)
        static float StillPx => Mathf.Max(3f, Screen.dpi > 0f ? Screen.dpi * StillMm / 25.4f : 0.006f * Screen.width);

        /// <summary>The sticky sweep: -1 (full left) .. 1 (full right).</summary>
        public float Value { get; private set; }
        /// <summary>What the rod shows: a live stroke's lean, the keys' full sweep while held, else <see cref="Value"/>.</summary>
        public float Shown { get; private set; }
        /// <summary>A straight slide is under way and the rod leans with it.</summary>
        public bool Live { get; private set; }
        /// <summary>Committed slides / live leans taken back (the stroke became a circle or bent away) so far.</summary>
        public int Slides { get; private set; }
        public int Cancels { get; private set; }
        /// <summary>True on the frame a slide is committed.</summary>
        public bool SlideNow { get; private set; }
        /// <summary>Why the last stroke let go was no slide (for the log / tests): "", "tap", "short", "angle", "bent", "circled".</summary>
        public string LastReject { get; private set; } = "";
        /// <summary>The keys (A / D, arrows) hold a sweep this frame.</summary>
        public bool KeyHeld { get; private set; }

        bool pressing, circled, wasLive;
        bool keyLatch;           // the keys were held when the sweep came on: they sweep nothing until let go once
        float fromT;             // the stroke's first sample time (the press, or the last hold commit)
        float startValue, stillT, lastT, liveDx;
        Vector2 lastPos;

        /// <summary>Back to the centre (a new cast, a fight starting, the rig home).</summary>
        public void Reset()
        {
            Value = Shown = 0f;
            Live = wasLive = pressing = circled = KeyHeld = false;
        }

        /// <summary>Call once a frame after <see cref="CircleGesture.Update"/>; <paramref name="on"/> false ignores input (the sweep is kept).</summary>
        public void Update(CircleGesture g, bool on)
        {
            SlideNow = false;
            if (!on)
            {
                pressing = Live = wasLive = KeyHeld = false;
                keyLatch = true;
                Shown = Value;
                return;
            }
            if (PointerInput.Pressed)
            {
                // (a press on the UI, the 회수 button say, ends any stroke still open and starts none)
                pressing = !PointerInput.StartedOverUI;
                circled = false;
                fromT = -1f;
                startValue = Value;
                stillT = 0f;
                lastPos = PointerInput.Position;
                lastT = Time.unscaledTime;
            }
            float live = Value;
            if (pressing && !PointerInput.IsDown && !PointerInput.Released)
            {
                // let go while this was not looking (a dialog over the game): the stroke is dropped, not guessed at
                pressing = Live = false;
            }
            if (pressing)
            {
                if (g.Circling) circled = true;
                var s = PointerInput.Samples;
                int from = 0;
                while (from < s.Count - 1 && s[from].time < fromT) from++;   // (the samples list drops its oldest on long presses)
                var cls = Classify(s, from);
                Live = !circled && cls.straight && Mathf.Abs(cls.dx) >= LiveTravel && Mathf.Abs(cls.lateDeg) <= LateAngle;
                if (Live)
                {
                    live = Clamp(startValue + cls.dx / FullTravel);
                    liveDx = cls.dx;
                }
                if (PointerInput.IsDown)
                {
                    // held still in the middle of a live slide: commit it here and start a new stroke (slide, stop, circle)
                    var pos = PointerInput.Position;
                    float now = Time.unscaledTime;
                    if ((pos - lastPos).magnitude > StillPx) { stillT = 0f; lastPos = pos; }
                    else stillT += now - lastT;
                    lastT = now;
                    if (Live && stillT >= HoldCommit && Mathf.Abs(cls.dx) >= MinTravel)
                    {
                        Commit(cls.dx);
                        fromT = s.Count > 0 ? s[s.Count - 1].time : now;
                        startValue = Value;
                        stillT = 0f;
                        Live = false;
                        live = Value;
                    }
                }
                if (PointerInput.Released)
                {
                    pressing = false;
                    Live = false;
                    if (circled) LastReject = "circled";
                    else if (cls.far < LureInput.TapMaxTravel * Screen.height / Mathf.Max(1f, Screen.width)) LastReject = "tap";
                    else if (Mathf.Abs(cls.dx) < MinTravel) LastReject = cls.deg > MaxAngle ? "angle" : "short";
                    else if (cls.deg > MaxAngle) LastReject = "angle";
                    else if (!cls.straight) LastReject = "bent";
                    else Commit(cls.dx);
                    live = Value;
                }
            }
            else Live = false;
            // a live lean that ended without being committed was taken back (the stroke became a circle / bent away)
            if (wasLive && !Live && !SlideNow) Cancels++;
            wasLive = Live;
            float k = PointerInput.WalkKeys;
            if (keyLatch)
            {
                if (k == 0f) keyLatch = false;
                else k = 0f;
            }
            KeyHeld = k != 0f;
            Shown = KeyHeld ? Mathf.Clamp(k, -1f, 1f) : live;
        }

        /// <summary>
        /// A bite (until the hook set or the miss): input is held off and the rod stays as it is. A stroke under way ends
        /// here, committed if it is already a slide (the rod keeps its lean), else dropped; a finger lifted meanwhile is
        /// never seen, so no stale stroke is left to come back to.
        /// </summary>
        public void Freeze()
        {
            SlideNow = false;
            if (pressing)
            {
                pressing = false;
                if (Live && Mathf.Abs(liveDx) >= MinTravel) Commit(liveDx);
                Live = false;
                if (!KeyHeld) Shown = Value;
            }
            wasLive = false;
        }

        void Commit(float dx)
        {
            float v = Clamp(startValue + dx / FullTravel);
            if (Mathf.Abs(v) < Snap) v = 0f;
            Value = v;
            Slides++;
            SlideNow = true;
            LastReject = "";
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[Sweep] slide {0:+0.000;-0.000}W -> sweep {1:+0.00;-0.00;0.00}", dx, v));
        }

        static float Clamp(float v) => Mathf.Clamp(v, -1f, 1f);

        struct Stroke
        {
            public float dx, deg, lateDeg, far;   // net sideways (W), chord / latest direction from horizontal (deg), farthest from the start (W)
            public bool straight;
        }

        /// <summary>The stroke from sample <paramref name="i0"/> to the last: net sideways travel, angles, straightness.</summary>
        static Stroke Classify(System.Collections.Generic.IReadOnlyList<PointerInput.Sample> s, int i0)
        {
            var r = new Stroke { deg = 90f, lateDeg = 90f };
            int n = s.Count;
            if (n - i0 < 2) return r;
            float W = Mathf.Max(1f, Screen.width);
            var p0 = s[i0].pos;
            var d = s[n - 1].pos - p0;
            float chord = d.magnitude;
            for (int i = i0 + 1; i < n; i++) r.far = Mathf.Max(r.far, (s[i].pos - p0).magnitude / W);
            r.dx = d.x / W;
            if (chord < 1f) return r;
            r.deg = Mathf.Atan2(Mathf.Abs(d.y), Mathf.Abs(d.x)) * Mathf.Rad2Deg;
            float bow = 0f;
            for (int i = i0 + 1; i < n - 1; i++)
            {
                var q = s[i].pos - p0;
                bow = Mathf.Max(bow, Mathf.Abs(d.x * q.y - d.y * q.x) / chord);
                // coming back past the start: a loop, not a slide
                // (more than a quarter chord behind the start along the slide: Dot(q, d) is that distance x the chord)
                if (Vector2.Dot(q, d) < -0.25f * chord * chord) bow = float.MaxValue;
            }
            // the latest direction: over the last LateWindow (at least two samples back), the same way as the chord
            int j = n - 2;
            while (j > i0 && s[n - 1].time - s[j].time < LateWindow) j--;
            var late = s[n - 1].pos - s[j].pos;
            r.lateDeg = late.sqrMagnitude < 4f ? 0f
                : Vector2.Dot(late, d) < 0f ? 180f : Mathf.Atan2(Mathf.Abs(late.y), Mathf.Abs(late.x)) * Mathf.Rad2Deg;
            r.straight = bow <= Bow * chord && r.deg <= MaxAngle;
            return r;
        }
    }
}
