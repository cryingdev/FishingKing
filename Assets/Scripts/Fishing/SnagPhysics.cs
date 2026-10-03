using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// A face of a prop the caught rig rests against (Docs/obstacles_spec.md 6.10): the waterline face's outward normal in
    /// plan, the face's lean (how far the prop reaches further out over the rig per metre up, from its tiers: + leans over
    /// the rig, - leans away), its friction and the prop's top above the water.
    /// </summary>
    public struct PropFace
    {
        public Vector2 n;
        public float slope, mu, top;
        public string id;

        /// <summary>The outward normal in 3D (x, y up, z): tilted down under a face leaning over the rig, up on one leaning away.</summary>
        public Vector3 N3 => new Vector3(n.x, -slope, n.y).normalized;
        /// <summary>The face leans over the rig: its upper end leads into the prop's underside, its lower end is a lip the rig slips out under.</summary>
        public bool Over => slope > SnagPhysics.OverSlope;
    }

    /// <summary>One evaluation of the rig on its faces (<see cref="SnagPhysics.Slide"/>).</summary>
    public struct PropSlide
    {
        /// <summary>The slide in plan (m/s, along the waterline faces) and up / down the faces (m/s).</summary>
        public Vector2 plan;
        public float vy;
        /// <summary>The faces' normal force (tension units: 1 = the snag tension that breaks the line).</summary>
        public float normal;
        /// <summary>The pull along the faces and what holds it (friction x normal + the bitten-in point).</summary>
        public float drive, resist;
        public bool moving;
        /// <summary>The waterline faces close in on the pull in plan (a notch): only up / down the crease is left.</summary>
        public bool planLocked;
    }

    /// <summary>
    /// The physics of a rig caught on a prop (Docs/obstacles_spec.md 6.10): a point resting against the faces it touches,
    /// pulled by the line (a 3D vector: towards the rod along the line, its size the snag tension) and by its own weight and
    /// the water. Per face the pull splits into the part pressing the rig in (the normal force) and the part along the face;
    /// Coulomb friction: it slides when the part along the faces beats mu x the normal force plus what the hook point has
    /// bitten in (<see cref="PinK"/> x embed: a hard pull into rough faces drives the point in, slack lets it back out);
    /// it slides at a capped, water-damped speed. Two faces closing in on the pull (a notch) leave only the crease.
    /// Pure functions: no randomness, no Unity objects (the test harness compiles this file as is).
    /// </summary>
    public static class SnagPhysics
    {
        // ---- the rig and the water
        /// <summary>The rig's weight in the water (tension units): carried by the line while it holds <see cref="HoldT"/>, it pulls the rig down the faces when the line is slack.</summary>
        public const float Weight = 0.02f;
        /// <summary>A line at this tension or more carries the rig's weight (the rod's floor tension in still water is 0.05).</summary>
        public const float HoldT = 0.05f;
        /// <summary>The water's drag on the rig (tension units per m/s of current; the current pushes it along / into the faces).</summary>
        public const float CurrentDrag = 0.03f;
        /// <summary>The water damps the slide: speed = (drive - resist) / Damp (m/s per tension unit) ...</summary>
        public const float Damp = 0.02f;
        /// <summary>... never faster than this (m/s).</summary>
        public const float MaxSlide = 0.6f;
        /// <summary>A rig at rest must beat this many times the faces' friction to start sliding (static over sliding friction; kept under
        /// the full sweep's sideways share of the pull, 0.6 x sin 30° = 0.3, over concrete's 0.25, so the old sweep still slides it).</summary>
        public const float StaticK = 1.1f;

        // ---- the hook point biting in (the material term: replaces a tension-scaled friction)
        /// <summary>Normal force (tension units) past which the point bites in ...</summary>
        public const float BiteN = 0.3f;
        /// <summary>... at this rate (embed per s per tension unit past <see cref="BiteN"/>), up to 1 ...</summary>
        public const float BiteRate = 2.5f;
        /// <summary>... and holds this much (tension units) against the slide when fully in (x the material's grab).</summary>
        public const float PinK = 0.25f;
        /// <summary>Under this normal force (a slack line) the point backs out at <see cref="EaseRate"/> per s.</summary>
        public const float EaseN = 0.08f;
        public const float EaseRate = 0.6f;

        // ---- the line's angle
        /// <summary>
        /// The line's rise at the rig (dy per m in plan) with the rod lifted / lowered the full way: held at rest the last
        /// of the line lies flat on the water to the rig (rise 0); lifted, the rod takes it up off the water; lowered, the
        /// tip near the water, the line runs under the rig's level.
        /// </summary>
        public const float PitchRise = 0.5f;
        /// <summary>The rod tip's lift / drop at the full pitch (m): its take-up of line is the 3D chord's change.</summary>
        public const float TipLift = 0.8f;
        /// <summary>The rod tip's shift to the side at the full sweep (m).</summary>
        public const float TipSide = 1.2f;
        /// <summary>The rod tip's height over the water at rest above his feet (m, plus the stand height).</summary>
        public const float TipH = 1.6f;

        // ---- the faces up and down
        /// <summary>A face leaning more than this (m out per m up) over the rig overhangs it.</summary>
        public const float OverSlope = 0.05f;
        /// <summary>Slid this far down past the waterline under an overhang, the rig is out from under its lip: free.</summary>
        public const float UnderLip = 0.12f;
        /// <summary>Down a face that does not overhang (it carries on under the water, widening) the rig settles at most this deep (m).</summary>
        public const float SinkRoom = 0.4f;
        /// <summary>Slid this far up a face that does not overhang (m), the rig is lifted off the edge that held it: free.</summary>
        public const float UpClear = 0.15f;
        /// <summary>Slid this far along the faces in plan (m) from where it caught, the rig is out of the crevice that held it: free.</summary>
        public const float SlideOut = 0.15f;

        // ---- the 톡: an impulse along the line
        /// <summary>The 톡's pull (tension units at strength 1) ...</summary>
        public const float TokPull = 0.6f;
        /// <summary>... for this long (s) ...</summary>
        public const float TokTime = 0.12f;
        /// <summary>... with the rod snapped up (this much more rise) ...</summary>
        public const float TokRise = 0.6f;
        /// <summary>... taking up this much line (m): slack past it swallows the 톡.</summary>
        public const float TokTake = 0.5f;

        /// <summary>
        /// The face of a prop at the rig: <paramref name="n"/> the waterline face's normal; its lean from the tiers, each
        /// tier's reach along the normal on the line through the rig (where the line meets the tier at all), fitted against
        /// the tier's height (least squares; fewer than two tiers on the line: upright).
        /// </summary>
        public static PropFace Face(Vector2 xz, Vector2 n, Vector2[][] tiers, float[] y0, float[] y1, float top, float mu, string id = "")
        {
            float sy = 0f, se = 0f, syy = 0f, sye = 0f;
            int m = 0;
            if (tiers != null)
                for (int k = 0; k < tiers.Length; k++)
                {
                    if (tiers[k] == null || tiers[k].Length < 3) continue;
                    if (!Reach(tiers[k], xz, n, out float e)) continue;
                    float y = 0.5f * (y0[k] + y1[k]);
                    sy += y;
                    se += e;
                    syy += y * y;
                    sye += y * e;
                    m++;
                }
            float slope = 0f;
            float den = m * syy - sy * sy;
            if (m >= 2 && den > 1e-6f) slope = Mathf.Clamp((m * sye - sy * se) / den, -1.5f, 1.5f);
            return new PropFace { n = n, slope = slope, mu = mu, top = top, id = id };
        }

        /// <summary>How far out along <paramref name="n"/> the convex polygon reaches on the line through <paramref name="p"/> (false: the line misses it).</summary>
        static bool Reach(Vector2[] poly, Vector2 p, Vector2 n, out float e)
        {
            // clip the line p + t n against every edge's inner half-plane (counter-clockwise polygon)
            float lo = -1e6f, hi = 1e6f;
            int c = poly.Length;
            for (int i = 0, j = c - 1; i < c; j = i++)
            {
                var a = poly[j];
                var ab = poly[i] - a;
                // inside: cross(ab, x - a) >= 0
                float f0 = ab.x * (p.y - a.y) - ab.y * (p.x - a.x);
                float fd = ab.x * n.y - ab.y * n.x;
                if (Mathf.Abs(fd) < 1e-9f)
                {
                    if (f0 < 0f)
                    {
                        e = 0f;
                        return false;
                    }
                    continue;
                }
                float t = -f0 / fd;
                if (fd > 0f) lo = Mathf.Max(lo, t);
                else hi = Mathf.Min(hi, t);
            }
            e = hi;
            return lo <= hi;
        }

        /// <summary>
        /// The rig under the force <paramref name="F"/> (3D, tension units) against <paramref name="faces"/>, the hook
        /// point bitten in <paramref name="embed"/> (0..1) with the material's grab <paramref name="grab"/>, <paramref name="still"/> at rest
        /// (static friction: x <see cref="StaticK"/>): the pressing
        /// parts off (N = sum of max(0, -F.n)), the rest along the faces; held while |along| &lt;= mu N + pin, else sliding
        /// at min(<see cref="MaxSlide"/>, (|along| - mu N - pin) / <see cref="Damp"/>). In plan the slide keeps to the
        /// waterline faces (the plan pull with the parts into them off), up / down it follows the 3D slide's climb.
        /// </summary>
        public static PropSlide Slide(List<PropFace> faces, Vector3 F, float embed, float grab = 1f, bool still = false)
        {
            var r = new PropSlide();
            if (faces == null || faces.Count == 0)
            {
                r.moving = true;
                r.drive = F.magnitude;
                return r;
            }
            var v = F;
            for (int pass = 0; pass < 4; pass++)
                foreach (var f in faces)
                {
                    var nn = f.N3;
                    float dn = Vector3.Dot(v, nn);
                    if (dn < 0f) v -= dn * nn;
                }
            float N = 0f, mu = 0f;
            bool locked = false;
            foreach (var f in faces)
            {
                var nn = f.N3;
                float into = -Vector3.Dot(F, nn);
                if (into > 0f)
                {
                    N += into;
                    mu = Mathf.Max(mu, f.mu);
                }
                if (Vector3.Dot(v, nn) < -1e-4f) locked = true;
            }
            r.normal = N;
            r.drive = locked ? 0f : v.magnitude;
            r.resist = mu * (still ? StaticK : 1f) * N + PinK * grab * Mathf.Clamp01(embed);
            if (locked || r.drive <= r.resist + 1e-6f) return r;
            float speed = Mathf.Min(MaxSlide, (r.drive - r.resist) / Damp);
            var w = new Vector2(F.x, F.z);
            for (int pass = 0; pass < 4; pass++)
                foreach (var f in faces)
                {
                    float dn = Vector2.Dot(w, f.n);
                    if (dn < 0f) w -= dn * f.n;
                }
            bool planLocked = w.sqrMagnitude < 1e-10f;
            foreach (var f in faces) if (Vector2.Dot(w, f.n) < -1e-4f) planLocked = true;
            float planShare = new Vector2(v.x, v.z).magnitude / r.drive;
            r.plan = planLocked ? Vector2.zero : w.normalized * (speed * planShare);
            r.vy = speed * v.y / r.drive;
            r.planLocked = planLocked;
            r.moving = r.plan.sqrMagnitude > 1e-10f || Mathf.Abs(r.vy) > 1e-5f;
            return r;
        }

        /// <summary>The bitten-in point after <paramref name="dt"/> s under the normal force <paramref name="normal"/>: in past <see cref="BiteN"/>, out under <see cref="EaseN"/>.</summary>
        public static float Bite(float embed, float normal, float dt, float grab = 1f)
        {
            if (normal > BiteN) embed += BiteRate * grab * (normal - BiteN) * dt;
            else if (normal < EaseN) embed -= EaseRate * dt;
            return Mathf.Clamp01(embed);
        }

        /// <summary>
        /// The line's force on the rig: tension <paramref name="T"/> along the line towards the rod (in plan
        /// <paramref name="pull"/>, the swept rod's bent pull, Tackle.Pull; its rise <paramref name="rise"/> per m in plan),
        /// plus the rig's weight where the line does not carry it and the water's drag (<paramref name="water"/>, m/s).
        /// </summary>
        public static Vector3 Force(float T, Vector2 pull, float rise, Vector2 water)
        {
            var L = new Vector3(pull.x, rise * pull.magnitude, pull.y);
            var F = L.sqrMagnitude > 1e-8f ? L.normalized * T : Vector3.zero;
            F.y -= Weight * (1f - Mathf.Clamp01(T / HoldT));
            F += new Vector3(water.x, 0f, water.y) * CurrentDrag;
            return F;
        }

        /// <summary>Whether the faces let the rig out at the top (none leans over it) / under the lip (all of them lean over it).</summary>
        public static void Exits(List<PropFace> faces, out bool up, out bool down, out float top)
        {
            up = faces.Count > 0;
            down = faces.Count > 0;
            top = 99f;
            foreach (var f in faces)
            {
                if (f.Over) up = false;
                else down = false;
                top = Mathf.Min(top, f.top);
            }
        }
    }
}
