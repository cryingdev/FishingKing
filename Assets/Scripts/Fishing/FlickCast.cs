using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Flick casting (every stage but the ice): the player pulls down (the wind-up), then flicks UP and lets go. Read from
    /// the pointer's samples (<see cref="PointerInput.Samples"/>) at the moment of release:
    /// <list type="bullet">
    /// <item>power 0..1 from the upward flick's speed: the peak, over the current upward stroke (since its lowest point), of
    /// the displacement over a short window (<see cref="SpeedWindow"/>), so a flick that slows just before the finger lifts
    /// still counts; strokes shorter than <see cref="StrokeFull"/> are scaled down;</item>
    /// <item>direction from the way the finger moved in the last <see cref="DirWindow"/> before it lifted (a least-squares
    /// fit over those samples, so one jittery last frame barely matters), NOT from where the stroke started: the flick's
    /// direction at the moment of release is the throw's direction (on screen: the controller turns it into the world
    /// yaw through the camera, so the rig lands on the line the finger flicked along).</item>
    /// </list>
    /// Lengths are in screen heights (H) and seconds. Speeds are in a unit that means the same hand motion on any screen
    /// (<see cref="Units"/>): cm/s on the glass for touch (a landscape phone's H is only ~7 cm, a tablet's ~15 cm), display
    /// heights per second for a mouse.
    /// </summary>
    public static class FlickCast
    {
        // ------------------------------------------------------------------ tuning
        /// <summary>The wind-up: pull at least this far (H) below the press point before a flick counts.</summary>
        public const float WindUpMin = 0.07f;
        /// <summary>Touch: upward flicks slower than this (cm/s on the screen) are no flick: letting go does not cast.</summary>
        public const float TouchSpeedMin = 5f;
        /// <summary>Touch: this fast (cm/s) or faster is full power (the rod's whole cast distance). An ordinary thumb flick
        /// is ~25-60 cm/s; retune both from the [Flick] lines in Player.log.</summary>
        public const float TouchSpeedFull = 60f;
        /// <summary>Touch, when the screen reports no dpi: its height is taken to be this many cm (a phone held sideways).</summary>
        public const float TouchFallbackHeightCm = 7f;
        /// <summary>Mouse: the slowest flick, in display heights per second (the cursor crosses the display's pixels, not the
        /// window's, so a small window does not make the same hand motion stronger).</summary>
        public const float MouseSpeedMin = 0.35f;
        /// <summary>Mouse: full power (display heights per second; ~4300 px/s on a 1080p display).</summary>
        public const float MouseSpeedFull = 4f;
        /// <summary>power = x^PowerCurve, x = where the speed lies from min to full: 1 = linear (typical flicks land in
        /// the middle of the range), &gt; 1 = ease-in (more room near the top).</summary>
        public const float PowerCurve = 1f;
        /// <summary>Speeds are measured over at least this span (s), which smooths out single-frame jitter.</summary>
        public const float SpeedWindow = 0.05f;
        /// <summary>Only the motion this close (s) to the release counts: flick, hold still, then let go = no flick.</summary>
        public const float PeakMaxAge = 0.3f;
        /// <summary>Upward strokes shorter than this (H) are scaled down: power x travel / StrokeFull.</summary>
        public const float StrokeFull = 0.10f;
        /// <summary>Shorter upward strokes (H) are a twitch, no flick.</summary>
        public const float StrokeMin = 0.015f;
        /// <summary>Coming back down this far (H) from the stroke's top starts a new stroke from there.</summary>
        public const float StrokeReset = 0.03f;
        /// <summary>Motion flatter than this (degrees from straight up) is not an upward flick.</summary>
        public const float MaxUpAngle = 75f;
        /// <summary>Direction: the finger's motion over the last this-many seconds before it lifted...</summary>
        public const float DirWindow = 0.07f;
        /// <summary>...over at least this many samples...</summary>
        public const int DirMinSamples = 3;
        /// <summary>...and this far (H); with less, the direction is taken from the stroke's lowest point instead.</summary>
        public const float DirMinTravel = 0.015f;
        /// <summary>Flicks within this many degrees of straight up (on screen) go straight up the screen.</summary>
        public const float DirDeadzone = 3f;
        /// <summary>Degrees on screen of the throw's line per degree of flick (screen right = +x).</summary>
        public const float DirGain = 1f;
        /// <summary>The widest throw either side (degrees of world yaw; on screen it looks wider, ~57 degrees).</summary>
        public const float YawMax = 38f;
        /// <summary>A step shorter than this (H) is the finger resting: a still tail before the lift is skipped.</summary>
        const float StillStep = 0.002f;

        /// <summary>How the speeds of a press are measured: the unit, screen pixels per unit length, the min / full speeds.</summary>
        public struct Units
        {
            public string name;
            public float pxPerUnit, min, full;
        }

        /// <summary>
        /// The units for the current press: touch in cm/s (from the screen's dpi; the simulated pointer's
        /// <see cref="PointerInput.SimDpi"/>), a mouse in display heights per second, the autopilot's mouse-like pointer in
        /// window heights (so its tests do not depend on the monitor).
        /// </summary>
        public static Units UnitsNow(float screenH)
        {
            float H = Mathf.Max(1f, screenH);
            bool sim = PointerInput.SimActive;
            if (PointerInput.PressKind == PointerInput.Kind.Touch)
            {
                float dpi = sim ? PointerInput.SimDpi : Screen.dpi;
                return new Units
                {
                    name = "cm/s",
                    pxPerUnit = dpi > 1f ? dpi / 2.54f : H / TouchFallbackHeightCm,
                    min = TouchSpeedMin,
                    full = TouchSpeedFull,
                };
            }
            float display = Screen.currentResolution.height;
            return new Units
            {
                name = sim ? "H/s" : "DH/s",
                pxPerUnit = sim || display < 1f ? H : display,
                min = MouseSpeedMin,
                full = MouseSpeedFull,
            };
        }

        public struct Result
        {
            public bool armed, flick;
            public bool quiet;          // no flick, and the finger came back up to the press point: called off, no scolding
            public float windUp;        // deepest pull below the press point (H)
            public float releaseDrop;   // how far below the press point it was let go (H, set by the controller)
            public string unit;         // the unit of the speeds
            public float speed;         // peak upward flick speed (unit) within PeakMaxAge of the release
            public float speedAny;      // the same over the whole stroke, however long before the release
            public float speedH;        // speed in screen heights per second (for the log)
            public float peakAge;       // how long before the release the peak was (s)
            public float travel;        // the upward stroke's length (H)
            public float angle;         // the flick's direction at release, degrees from straight up on screen (right +)
            public float strokeAngle;   // the whole stroke's direction (lowest point -> end), for comparison
            public int dirSamples;      // samples in the direction fit (0 = fell back to the stroke)
            public bool dirFallback;
            public float aim;           // the throw's line on screen: the angle after the dead zone / gain (degrees)
            public float power, yaw;    // 0..1, world degrees (right +, set by the controller from aim through the camera)
            public float dist;          // the cast distance it made (m, set by the controller)
            public Vector3 target;
            public int samples;
            public string why;          // why it is no flick
        }

        /// <summary>
        /// Power / direction of the flick that ends with the samples (the pointer was just let go). <paramref name="down"/>:
        /// measure a DOWNWARD stroke instead (the lure's 톡, <see cref="LureInput"/>): the same analysis on the screen's y
        /// mirrored, so "up" / "upward" in the fields and constants means down the screen and angles are from straight down.
        /// </summary>
        public static Result Analyze(IReadOnlyList<PointerInput.Sample> s, float screenH, Units u, bool down = false)
        {
            var r = new Result { samples = s.Count, why = "", unit = u.name };
            float H = Mathf.Max(1f, screenH);
            float toUnit = H / Mathf.Max(1e-3f, u.pxPerUnit); // H/s -> unit/s
            float sy = down ? -1f : 1f;
            Vector2 Pos(int i) => new Vector2(s[i].pos.x, s[i].pos.y * sy);
            int n = s.Count;
            if (n < 2)
            {
                r.why = "tap";
                return r;
            }
            // the current upward stroke starts at its lowest point (a new one if the finger comes back down)
            int low = 0;
            float top = Pos(0).y;
            for (int i = 1; i < n; i++)
            {
                float y = Pos(i).y;
                if (y <= Pos(low).y) { low = i; top = y; }
                else if (y > top) top = y;
                else if (y < top - StrokeReset * H) { low = i; top = y; }
            }
            // where the motion ended: a resting tail (the finger stopped before it lifted) is skipped
            int end = n - 1;
            while (end > low && (Pos(end) - Pos(end - 1)).magnitude < StillStep * H) end--;
            float tRelease = s[n - 1].time;
            var stroke = (Pos(end) - Pos(low)) / H;
            r.travel = stroke.y > 0f ? stroke.magnitude : 0f;
            r.strokeAngle = stroke.y > 0f ? Mathf.Atan2(stroke.x, stroke.y) * Mathf.Rad2Deg : 0f;

            // speed: the peak over the stroke's recent part, each measured over at least SpeedWindow (and over the whole
            // stroke, to tell a flick held too long from a slow return)
            for (int i = low + 1; i <= end; i++)
            {
                int j = i - 1;
                while (j > low && s[i].time - s[j].time < SpeedWindow) j--;
                float dt = s[i].time - s[j].time;
                if (dt < 1e-3f) continue;
                var v = (Pos(i) - Pos(j)) / (H * dt);
                if (v.y <= 0f || Mathf.Abs(Mathf.Atan2(v.x, v.y)) * Mathf.Rad2Deg > MaxUpAngle) continue;
                float sp = v.magnitude;
                r.speedAny = Mathf.Max(r.speedAny, sp * toUnit);
                if (tRelease - s[i].time > PeakMaxAge) continue;
                if (sp > r.speedH)
                {
                    r.speedH = sp;
                    r.peakAge = tRelease - s[i].time;
                }
            }
            r.speed = r.speedH * toUnit;

            // direction at the release: least-squares velocity over the last DirWindow (>= DirMinSamples) of the motion
            int first = end;
            while (first - 1 >= low && (end - first + 1 < DirMinSamples || s[end].time - s[first - 1].time <= DirWindow)) first--;
            int m = end - first + 1;
            var dir = Vector2.zero;
            if (m >= DirMinSamples && (Pos(end) - Pos(first)).magnitude >= DirMinTravel * H)
            {
                float mt = 0f;
                var mp = Vector2.zero;
                for (int k = first; k <= end; k++)
                {
                    mt += s[k].time;
                    mp += Pos(k);
                }
                mt /= m;
                mp /= m;
                float stt = 0f;
                var stp = Vector2.zero;
                for (int k = first; k <= end; k++)
                {
                    float dt = s[k].time - mt;
                    stt += dt * dt;
                    stp += dt * (Pos(k) - mp);
                }
                if (stt > 1e-9f) dir = stp / stt;
                r.dirSamples = m;
            }
            if (dir.y <= 0f)
            {
                dir = stroke;
                r.dirFallback = true;
                r.dirSamples = 0;
            }
            r.angle = dir.y > 0f ? Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg : 0f;

            if (r.speed < u.min) r.why = r.speed > 0f ? "slow" : r.travel >= StrokeMin ? "stopped" : "no-upstroke";
            else if (r.travel < StrokeMin) r.why = "twitch";
            else
            {
                r.flick = true;
                float x = Mathf.InverseLerp(u.min, u.full, r.speed);
                r.power = Mathf.Pow(x, PowerCurve) * Mathf.Clamp01(r.travel / StrokeFull);
                r.aim = AimAngle(r.angle);
            }
            return r;
        }

        /// <summary>The throw's line on screen (degrees from straight up, right +) for a flick this many degrees from straight
        /// up: within the dead zone straight up the screen, otherwise along the flick (times the gain).</summary>
        public static float AimAngle(float angle) => Mathf.Abs(angle) <= DirDeadzone ? 0f : angle * DirGain;

        public static string Describe(Result r) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "wind={0:0.000}H release={1:+0.000;-0.000;0.000}H armed={2} samples={3} travel={4:0.000}H speed={5:0.0}{6} ({7:0.00}H/s, peak {8:0.000}s before release; whole stroke {9:0.0}) angle={10:+0.0;-0.0;0.0} (stroke {11:+0.0;-0.0;0.0}, {12}) aim={13:+0.0;-0.0;0.0} power={14:0.00} yaw={15:+0.0;-0.0;0.0} dist={16:0.0}m{17}",
            r.windUp, r.releaseDrop, r.armed ? 1 : 0, r.samples, r.travel, r.speed, r.unit, r.speedH, r.peakAge, r.speedAny, r.angle, r.strokeAngle,
            r.dirFallback ? "from stroke" : $"fit n={r.dirSamples}", r.aim, r.power, r.yaw, r.dist, r.quiet ? " quiet" : "");
    }
}
