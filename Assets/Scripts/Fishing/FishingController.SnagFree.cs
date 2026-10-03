using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Working a rig caught on a prop free (Docs/obstacles_spec.md 6.10, Docs/controls.md), by the physics of
    /// <see cref="SnagPhysics"/>: every frame the line pulls the rig (towards the rod, swept left / right and pitched up /
    /// down, as hard as the snag tension, plus its weight on a slack line and the water's drag) against the faces it
    /// touches; it slides along them where that pull beats their friction and the bitten-in hook point, and comes free
    /// once it has slid off the edge that held it (along the faces, up a face that does not overhang, or down out under
    /// one that does). Giving line takes the tension off so the point backs out; pulling tight drives it in. The 톡 is a
    /// short pull along the line. No scores, timers or dice: the same moves give the same result.
    /// </summary>
    public partial class FishingController
    {
        // ---- the slack (metres of line given past the chord to the rig)
        /// <summary>Slack (m) the HUD reads as a slack line (팽팽 / 느슨).</summary>
        public const float SlackMin = 0.05f;
        /// <summary>The HUD's slack bar is full at this (m).</summary>
        public const float SlackMax = 2f;
        /// <summary>Most slack (m) the reel gives a caught rig (the line lies on the water past it).</summary>
        public const float SlackCap = 3f;
        /// <summary>Moving water takes up the slack at this rate (m/s): the current pulls the line taut again.</summary>
        const float SlackDrain = 0.15f;
        /// <summary>A slack line sags on screen from this (Angler.Slack01) ...</summary>
        const float SlackShow0 = 0.5f;
        /// <summary>... plus this per metre of slack.</summary>
        const float SlackShow = 0.5f;
        /// <summary>The hook point is out of the face (the HUD: the rod's moves can work it) under this embed.</summary>
        public const float EmbedLoose = 0.05f;

        // ---- the rod's pitch
        /// <summary>The drawn rod follows the pitch with this time constant (s).</summary>
        const float PitchTau = 0.12f;
        /// <summary>A full lift raises the drawn rod this share of the way to upright (Angler.RodLift01).</summary>
        const float PitchLift = 0.35f;

        /// <summary>From this snag tension the HUD warns that the pull is driving the point in.</summary>
        public const float TightR = 0.6f;

        /// <summary>The rod's pitch while a rig is caught on a prop: a slow slide down (lift) / up (lower) the screen, W / S.</summary>
        public SideSlide Pitch { get; } = new SideSlide(true);
        float pitchS, tokT, tokS;
        Vector2Int propGuide;
        bool propWedged;
        readonly List<PropFace> propFaces = new List<PropFace>();

        /// <summary>Slack in the line to the caught rig (m).</summary>
        public float SnagSlack { get; private set; }
        /// <summary>The caught rig's hook point is out of the face: the rod's moves can work it.</summary>
        public bool SnagLoose => Tackle.Snag != null && Tackle.Snag.embed < EmbedLoose;
        /// <summary>No rod move slides the caught rig, even with the point out (a notch): a 톡 or 끊기.</summary>
        public bool SnagWedged => propWedged;
        /// <summary>The drawn rod's pitch, -1 (lowered) .. 1 (lifted).</summary>
        public float PitchNow => pitchS;

        /// <summary>
        /// The free way the snag arrow points (rod terms): x the sweep's side (+ right), y the pitch (+1 lift, -1 lower);
        /// zero: none. A prop catch: the rod move whose pull would slide it towards a way out (<see cref="PropGuideNow"/>),
        /// shown while the line is taut on it; the other snags: the free side after a wrong sweep or a failed 톡.
        /// </summary>
        public Vector2Int SnagArrowDir
        {
            get
            {
                var sn = Tackle.Snag;
                if (State != S.Snagged || sn == null || L.IsIce) return Vector2Int.zero;
                if (sn.kind == "prop") return SnagSlack < SlackMin ? propGuide : Vector2Int.zero;
                return snagArrow && !sn.soft && sn.freeSide != 0 ? new Vector2Int(sn.freeSide, 0) : Vector2Int.zero;
            }
        }

        /// <summary>A new catch: the rig at rest where it caught, no slack, the rod's pitch back to the middle.</summary>
        void ResetPropWork()
        {
            SnagSlack = 0f;
            tokT = 0f;
            propGuide = Vector2Int.zero;
            propWedged = false;
            Pitch.Reset();
        }

        /// <summary>The rod's pitch back to the middle (the catch over, any way).</summary>
        void ClearRodPitch()
        {
            pitchS = 0f;
            Pitch.Reset();
            Angler.RodLift01 = 0f;
            Angler.RodDip01 = 0f;
            SnagSlack = 0f;
            tokT = 0f;
        }

        /// <summary>
        /// The line to a rig caught on a prop: winding takes it in (tension once the slack is gone), giving pays it out past
        /// the chord as slack (up to <see cref="SlackCap"/>), left alone the tension eases to the floor and the slack stays
        /// (moving water takes it up at <see cref="SlackDrain"/>).
        /// </summary>
        void PropLine(float dt, bool winding, bool giving, float revs, float floor)
        {
            float k = StretchPerR, chord = LineChord, rest = chord - floor * k;
            if (winding) LineOut -= revs * Game.I.Reel.retrieve * dt;
            else if (giving) LineOut = Mathf.Min(chord + SlackCap, LineOut + Mathf.Max(-revs * Game.I.Reel.retrieve, 1.5f * k) * dt);
            else if (LineOut < rest) LineOut = Mathf.Min(rest, LineOut + 1.2f * k * dt);
            else if (Stage.Current != null && Stage.Current.Moving && !L.IsIce) LineOut = Mathf.Max(rest, LineOut - SlackDrain * dt);
        }

        /// <summary>A 톡 on a rig caught on a prop: a pull of <see cref="SnagPhysics.TokPull"/> x strength along the snapped-up line for <see cref="SnagPhysics.TokTime"/> s (slack past <see cref="SnagPhysics.TokTake"/> swallows it).</summary>
        void PropTok(float strength)
        {
            if (SnagSlack > SnagPhysics.TokTake)
            {
                Obstacles.Say(string.Format(CIo, "prop tok swallowed by {0:0.00} m of slack", SnagSlack));
                return;
            }
            tokT = SnagPhysics.TokTime;
            tokS = Mathf.Clamp01(strength);
            Obstacles.Say(string.Format(CIo, "prop tok s {0:0.00}", tokS));
        }

        /// <summary>The line's force on the caught rig for the rod at <paramref name="sweepSin"/> / <paramref name="pitch"/> under tension <paramref name="T"/>.</summary>
        Vector3 PropForce(float T, float sweepSin, float pitch, bool tok)
        {
            var tk = Tackle;
            var pull = tk.PullDir(ShorePoint, sweepSin);
            float rise = SnagPhysics.PitchRise * pitch + (tok ? SnagPhysics.TokRise : 0f);
            var water = Vector2.zero;
            var cf = Stage.Current;
            if (cf != null && cf.Moving && !L.IsIce) water = cf.Water(tk.Surface.x, tk.Surface.z) * CurrentField.Kd(tk.Depth);
            return SnagPhysics.Force(T, pull, rise, water);
        }

        /// <summary>
        /// One step of a prop catch's physics (after the line this frame): the line's force on the rig against its faces,
        /// the hook point biting in or backing out, the slide along / up / down the faces, and free once it has slid off
        /// the edge that held it. True when it came free this frame.
        /// </summary>
        bool PropStep(float dt, SnagInfo sn)
        {
            var tk = Tackle;
            float slack = Mathf.Max(0f, LineOut - LineChord);
            SnagSlack = slack;
            if (slack > 0f)
            {
                Angler.Tension01 = 0f;
                Angler.Strain01 = 0f;
                Angler.Slack01 = Mathf.Clamp01(SlackShow0 + SlackShow * slack);
            }
            bool tok = tokT > 0f;
            tk.PropFaces(propFaces);
            if (propFaces.Count == 0)
            {
                FreeSnag(PropFreeWay(tok));
                return true;
            }
            float T = sn.r + (tok ? SnagPhysics.TokPull * tokS : 0f);
            tokT -= dt;
            float grab = PropGrabNow();
            var F = PropForce(T, SweepSin, pitchS, tok);
            var s = SnagPhysics.Slide(propFaces, F, sn.embed, grab, !sn.moving);
            sn.embed = SnagPhysics.Bite(sn.embed, s.normal, dt, grab);
            sn.moving = s.moving;
            SnagPhysics.Exits(propFaces, out bool up, out bool down, out _);
            if (s.moving)
            {
                var d = s.plan * dt;
                tk.SlideOnProps(d);
                sn.slid += d.magnitude;
                sn.dy += s.vy * dt;
                if (!up) sn.dy = Mathf.Min(sn.dy, 0f);
                if (!down) sn.dy = Mathf.Max(sn.dy, -SnagPhysics.SinkRoom);
                if (Time.time - propSayT >= 0.25f)
                {
                    propSayT = Time.time;
                    Obstacles.Say(string.Format(CIo, "prop slide plan {0:0.000} m dy {1:+0.000;-0.000} (F {2:0.00} N {3:0.00} drive {4:0.00} resist {5:0.00} embed {6:0.00})",
                        sn.slid, sn.dy, F.magnitude, s.normal, s.drive, s.resist, sn.embed));
                }
                bool outPlan = sn.slid >= SnagPhysics.SlideOut, outUp = up && sn.dy >= SnagPhysics.UpClear, outDown = down && sn.dy <= -SnagPhysics.UnderLip;
                if (outPlan || outUp || outDown)
                {
                    Obstacles.Say(string.Format(CIo, "prop off the {0}: plan {1:0.00} m, dy {2:+0.00;-0.00}", outPlan ? "faces" : outUp ? "top edge" : "lip", sn.slid, sn.dy));
                    FreeSnag(PropFreeWay(tok));
                    return true;
                }
            }
            PropGuideNow(sn, grab, up, down);
            return false;
        }

        /// <summary>The hook point's grab on the faces touched: the props' (Tackle.PropGrab) x the hook's (a natural bait: HookDef.grabK).</summary>
        float PropGrabNow() => Tackle.PropGrab() * (RigHook != null ? RigHook.grabK : 1f);

        /// <summary>How it came free (the logs and the test): the 톡, the rod pitched, swept, a slack line, or the pull as it was.</summary>
        string PropFreeWay(bool tok) =>
            tok ? "tok"
            : SnagSlack >= SlackMin ? "slack"
            : Mathf.Abs(pitchS) >= 0.5f ? (pitchS > 0f ? "lift" : "lower")
            : Mathf.Abs(LeanReq) >= 0.5f ? "sweep" : "slide";

        /// <summary>
        /// The rod move the arrow shows (<see cref="SnagArrowDir"/>): of the four (swept left / right, pitched up / down, at
        /// the full way), the one whose pull under the tension now (at least the floor) would slide the rig the most past
        /// what holds it, towards a way out (along the faces in plan; up where no face overhangs; down where they all do).
        /// None: the point is in (give line) or no move would slide it (<see cref="SnagWedged"/> once the point is out).
        /// </summary>
        void PropGuideNow(SnagInfo sn, float grab, bool up, bool down)
        {
            float T = Mathf.Max(sn.r, 0.12f), sweep = Mathf.Sin(Angler.SweepMax * Mathf.Deg2Rad), best = 0f, bestSide = 0f;
            int side = 0;
            var pick = Vector2Int.zero;
            for (int i = 0; i < 4; i++)
            {
                var m = i == 0 ? new Vector2Int(1, 0) : i == 1 ? new Vector2Int(-1, 0) : i == 2 ? new Vector2Int(0, 1) : new Vector2Int(0, -1);
                var F = PropForce(T, sweep * m.x, m.y, false);
                var s = SnagPhysics.Slide(propFaces, F, sn.embed, grab, true);
                if (!s.moving) continue;
                bool outward = s.plan.sqrMagnitude > 1e-8f || (up && s.vy > 0f) || (down && s.vy < 0f);
                float margin = s.drive - s.resist;
                if (outward && m.x != 0 && margin > bestSide)
                {
                    bestSide = margin;
                    side = m.x;
                }
                if (outward && margin > best)
                {
                    best = margin;
                    pick = m;
                }
            }
            propGuide = pick;
            propWedged = pick == Vector2Int.zero && sn.embed < EmbedLoose;
            // (the sweep's free side, the strip and the old arrow: the side move that would slide it, if any)
            sn.freeSide = side;
        }

        /// <summary>The drawn rod follows the pitch (lifted towards upright, lowered towards the water).</summary>
        void RodPitch(float dt)
        {
            pitchS = Mathf.Lerp(pitchS, Pitch.Shown, 1f - Mathf.Exp(-dt / PitchTau));
            if (Mathf.Abs(pitchS) < 0.01f && Pitch.Shown == 0f) pitchS = 0f;
            Angler.RodLift01 = Mathf.Max(0f, pitchS) * PitchLift;
            Angler.RodDip01 = Mathf.Max(0f, -pitchS);
        }
    }
}
