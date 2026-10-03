using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingKing
{
    /// <summary>
    /// Turns a circular mouse / finger motion anywhere on screen into reel revolutions.
    /// A circle is least-squares fitted (Kasa fit) to the last ~0.4 s of pointer samples; the change
    /// of angle around its centre is accumulated. Clockwise = winding in (positive), or the other way
    /// round with the reversed-direction setting.
    /// Keyboard fallback: hold Space / R to wind, B to give line.
    /// </summary>
    public class CircleGesture
    {
        struct Sample { public Vector2 p; public float t; }

        readonly List<Sample> samples = new List<Sample>();
        float lastAngle;
        bool hasAngle;

        public float FrameRevs { get; private set; }   // revolutions this frame (+ = wind in)
        public float Speed { get; private set; }       // smoothed revolutions per second
        public float TotalRevs { get; private set; }   // for the reel-handle visual
        public Vector2 Center { get; private set; }    // screen px
        public float Radius { get; private set; }      // screen px
        public bool Circling { get; private set; }

        const float Window = 0.4f;
        // slow circles (a lure's slow retrieve, down to ~0.2 rev/s) sweep too little of a turn in Window: when the short
        // window fails the fit falls back to the last SlowWindow, so fast circling behaves exactly as before
        const float SlowWindow = 1.5f, SlowCoverage = 0.3f;
        const float MaxRevsPerSec = 4.5f;
        // a circle resumed after a hold (the finger never lifted) is measured around the circle it was last moving on, at
        // once: after a hold the window is full of still samples and the fit would need ~0.3 of a turn again (a slow run of
        // a few tenths of a second, the encounter's "살짝 끌고… 멈춰요", would never count). Moving on that circle within
        // StickyT of the last motion, while still samples are in the window, uses it; everything else is as before.
        const float StickyT = 3f, StickyBand = 0.35f, StickyMinDeg = 0.5f, StillPx = 1f;
        Vector2 stickyC, lastPos;
        float stickyR, stickyT = -99f, stillT = -99f;
        bool angleSticky;

        /// <summary>Settings option: counter-clockwise winds in, clockwise gives line.</summary>
        public static bool Reversed => Game.Data != null && Game.Data.reelReverse;
        /// <summary>The screen direction that winds in / gives line, for help texts.</summary>
        public static string WindWay => Reversed ? "반시계방향" : "시계방향";
        public static string GiveWay => Reversed ? "시계방향" : "반시계방향";

        public void Reset()
        {
            samples.Clear();
            hasAngle = false;
            stickyT = stillT = -99f;
            Speed = 0;
            FrameRevs = 0;
            Circling = false;
        }

        public void Update(bool down, Vector2 pos, float dt)
        {
            FrameRevs = 0;
            float now = Time.unscaledTime;
            float key = 0;
            if (PointerInput.KeyHeld(Key.Space) || PointerInput.KeyHeld(Key.R)) key = 2.6f;
            if (PointerInput.KeyHeld(Key.B)) key = -1.5f;
            if (PointerInput.SimWind) key = 2.6f;
            if (PointerInput.SimGive) key = -1.5f;

            if (!down)
            {
                samples.Clear();
                hasAngle = false;
                Circling = false;
                stickyT = stillT = -99f;
            }
            else
            {
                if (samples.Count > 0 && (pos - lastPos).magnitude < StillPx) stillT = now;
                lastPos = pos;
                samples.Add(new Sample { p = pos, t = now });
                while (samples.Count > 4 && now - samples[0].t > SlowWindow) samples.RemoveAt(0);
                float minR = Screen.height * 0.025f, maxR = Screen.height * 0.55f;
                int recent = samples.Count;
                while (recent > 0 && now - samples[recent - 1].t <= Window) recent--;
                // the last Window first (as always); a slow circle over the whole SlowWindow
                Vector2 c = Vector2.zero;
                float r = 0f;
                bool ok = samples.Count - recent >= 6 && Fit(recent, out c, out r) && r > minR && r < maxR && Coverage(c, recent) > 0.35f;
                if (!ok && recent > 0)
                    ok = samples.Count >= 6 && Fit(0, out c, out r) && r > minR && r < maxR && Coverage(c, 0) > SlowCoverage;
                bool onSticky = now - stickyT < StickyT && stickyR > minR && Mathf.Abs((pos - stickyC).magnitude - stickyR) < stickyR * StickyBand;
                bool afterHold = now - stillT < SlowWindow;
                if (ok && !(afterHold && onSticky))
                {
                    Center = Vector2.Lerp(Center, c, Circling ? 0.35f : 1f);
                    Radius = r;
                    float ang = Mathf.Atan2(pos.y - Center.y, pos.x - Center.x) * Mathf.Rad2Deg;
                    if (hasAngle && !angleSticky)
                    {
                        float d = Mathf.DeltaAngle(lastAngle, ang);
                        FrameRevs = -d / 360f; // screen y is up: clockwise = negative angle change
                        if (Reversed) FrameRevs = -FrameRevs;
                        if (Mathf.Abs(d) > StickyMinDeg)
                        {
                            stickyC = Center;
                            stickyR = Radius;
                            stickyT = now;
                        }
                    }
                    lastAngle = ang;
                    angleSticky = false;
                    hasAngle = true;
                    Circling = true;
                }
                else if (onSticky)
                {
                    // held on (or back on) the circle it was moving on: moving along it winds at once; still is no circling
                    float ang = Mathf.Atan2(pos.y - stickyC.y, pos.x - stickyC.x) * Mathf.Rad2Deg;
                    float d = hasAngle && angleSticky ? Mathf.DeltaAngle(lastAngle, ang) : 0f;
                    Circling = Mathf.Abs(d) > StickyMinDeg;
                    if (Circling)
                    {
                        FrameRevs = -d / 360f;
                        if (Reversed) FrameRevs = -FrameRevs;
                        stickyT = now;
                    }
                    lastAngle = ang;
                    angleSticky = true;
                    hasAngle = true;
                }
                else
                {
                    hasAngle = false;
                    Circling = false;
                }
            }

            if (key != 0) FrameRevs = key * dt;
            float maxStep = MaxRevsPerSec * Mathf.Max(dt, 1f / 120f);
            FrameRevs = Mathf.Clamp(FrameRevs, -maxStep, maxStep);
            TotalRevs += FrameRevs;
            float inst = dt > 0 ? FrameRevs / dt : 0;
            Speed = Mathf.Lerp(Speed, inst, 1f - Mathf.Exp(-dt / 0.12f));
            if (!down && key == 0) Speed = Mathf.Lerp(Speed, 0, 1f - Mathf.Exp(-dt / 0.08f));
        }

        /// <summary>Kasa algebraic circle fit over the samples from index <paramref name="from"/> on.</summary>
        bool Fit(int from, out Vector2 center, out float radius)
        {
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;
            int n = samples.Count - from;
            // centre the data for numerical stability
            Vector2 m = Vector2.zero;
            for (int i = from; i < samples.Count; i++) m += samples[i].p;
            m /= Mathf.Max(1, n);
            for (int i = from; i < samples.Count; i++)
            {
                var s = samples[i];
                double x = s.p.x - m.x, y = s.p.y - m.y, z = x * x + y * y;
                sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y; sxz += x * z; syz += y * z; sz += z;
            }
            // Solve [sxx sxy sx; sxy syy sy; sx sy n] [a b c] = [-sxz -syz -sz]
            double[,] A = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
            double[] B = { -sxz, -syz, -sz };
            double det = Det(A);
            center = m;
            radius = 0;
            if (System.Math.Abs(det) < 1e-9) return false;
            double a = Det(Replace(A, B, 0)) / det, b = Det(Replace(A, B, 1)) / det, c = Det(Replace(A, B, 2)) / det;
            double cx = -a / 2, cy = -b / 2;
            double r2 = cx * cx + cy * cy - c;
            if (r2 <= 0) return false;
            center = new Vector2((float)cx + m.x, (float)cy + m.y);
            radius = (float)System.Math.Sqrt(r2);
            return true;
        }

        /// <summary>Fraction of a full turn swept by the samples from index <paramref name="from"/> on (rejects straight drags).</summary>
        float Coverage(Vector2 c, int from)
        {
            float swept = 0;
            for (int i = from + 1; i < samples.Count; i++)
            {
                float a0 = Mathf.Atan2(samples[i - 1].p.y - c.y, samples[i - 1].p.x - c.x) * Mathf.Rad2Deg;
                float a1 = Mathf.Atan2(samples[i].p.y - c.y, samples[i].p.x - c.x) * Mathf.Rad2Deg;
                swept += Mathf.Abs(Mathf.DeltaAngle(a0, a1));
            }
            return swept / 360f;
        }

        static double Det(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
            m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
            m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

        static double[,] Replace(double[,] a, double[] b, int col)
        {
            var r = (double[,])a.Clone();
            for (int i = 0; i < 3; i++) r[i, col] = b[i];
            return r;
        }
    }
}
