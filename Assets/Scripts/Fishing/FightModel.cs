using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Pure tug-of-war simulation between a hooked fish and the player's tackle.
    /// Line length, tension (kgf) and fish stamina evolve from the reel input (handle revolutions
    /// per second; negative = giving line). The fish alternates runs, rests, bursts and jumps.
    /// </summary>
    public class FightModel
    {
        public enum Phase { Run, Burst, Rest, Jump }
        public enum Outcome { None, Landed, Snapped, Escaped, Spooled }

        /// <summary>
        /// Hop: a short leap, the hook can be thrown any time the line is tight. Shake: hangs in the air whipping
        /// its head 2-4 times, TailWalk: skips across the surface shaking every ~0.45 s; for both the hook can
        /// only be thrown on a shake, so easing off at those moments saves the fish.
        /// </summary>
        public enum JumpKind { Hop, Shake, TailWalk }

        public float Line;          // metres of line out
        public float Tension;       // kgf
        public float Stamina = 1f;  // 0..1
        public Phase State = Phase.Run;
        public Outcome Result = Outcome.None;
        public float Power { get; }           // fish pulling force (kgf) at full stamina
        /// <summary>
        /// The line's breaking load now: its strength, weakened by the wear of rubbing on structure (x (1 - 0.45 A),
        /// Docs/obstacles_spec.md 7.5). The break test, the HUD's red zone and the drag mark follow it.
        /// </summary>
        public float LineLimit => line.strength * (1f - 0.45f * Mathf.Clamp01(Abrasion));
        public float TensionRatio => Tension / LineLimit;
        public bool Exhausted => Stamina <= 0.001f;
        public bool Jumping => State == Phase.Jump;
        public bool Outclassed => Power > line.strength * 1.25f && Power > reel.dragMax * 1.25f;
        public float PhaseTime => phaseTime;
        public float SlackRatio => slack / SlackLimit;
        public float BreakRatio => breakTimer / BreakGrace;
        public event System.Action<Phase> PhaseChanged;

        // ---- structure (Docs/obstacles_spec.md 7): the run for cover, the hold there, the line's wear
        public enum Cause { None, Tension, Abrasion }
        /// <summary>Why the line snapped (Snapped): the tension, or the wear of rubbing on structure.</summary>
        public Cause SnapCause { get; private set; }
        /// <summary>0..1: the line's wear from rubbing on structure in this fight (never decreasing); it breaks at 1.</summary>
        public float Abrasion { get; private set; }
        /// <summary>Set by the controller while the line rubs this step: a break now is the rub's (the scuffed line).</summary>
        public bool Rubbing;
        /// <summary>The run heads for a cover: pulls x1.2, takes line x1.15, its clock does not run out before it arrives (at most 4 s).</summary>
        public bool CoverRun { get; private set; }
        /// <summary>Dug in at the cover: pulls at 0.6, takes no line, tires at half the rate, for up to 5 s x dig.</summary>
        public bool CoverHold { get; private set; }
        public float CoverHoldLeft => coverHoldLeft;
        /// <summary>The hold ran out (the fish runs out with a normal run).</summary>
        public event System.Action HoldEnded;
        /// <summary>Giving line this step (the reel turned backwards).</summary>
        public bool Giving { get; private set; }
        float coverRunT, coverHoldLeft;

        public void BeginCoverRun()
        {
            CoverRun = true;
            CoverHold = false;
            coverRunT = 0f;
        }

        public void BeginHold(float dig)
        {
            CoverRun = false;
            CoverHold = true;
            coverHoldLeft = 5f * Mathf.Max(0.2f, dig);
        }

        /// <summary>Pulled out of the cover (or the run ended): no cover run, no hold.</summary>
        public void EndCover() => CoverRun = CoverHold = false;

        /// <summary>The line wears by <paramref name="dA"/>; worn through (A = 1) it snaps at once.</summary>
        public void Abrade(float dA)
        {
            if (Result != Outcome.None || dA <= 0f) return;
            Abrasion = Mathf.Min(1f, Abrasion + dA);
            if (Abrasion >= 1f)
            {
                Result = Outcome.Snapped;
                SnapCause = Cause.Abrasion;
            }
        }

        /// <summary>Test switch (-fkjump): the fish jumps this way after every rest, near the surface or not.</summary>
        public static JumpKind? ForceJump;

        // ---- side pressure (the controller leans the rod against / with the fish's run: see FishingController.SidePressure)
        /// <summary>0..1 this step: the rod leant against the fish's run (good: it tires faster and is turned) / with it (bad).</summary>
        public float SideGood, SideBad;
        /// <summary>Stamina drain x this at full side pressure against the run, x <see cref="SideDrainBad"/> with it.</summary>
        public const float SideDrainGood = 1.4f, SideDrainBad = 0.8f;
        /// <summary>Either way the rod held to the side puts this much more (fraction) on the line.</summary>
        public const float SideTension = 0.1f;
        /// <summary>Leant with the run, the run lasts longer: its clock runs this much (fraction) slower at full lean.</summary>
        public const float SideRunLonger = 0.35f;
        /// <summary>The stamina drain multiplier from side pressure this step (1 = none).</summary>
        public float SideDrainMult => 1f + (SideDrainGood - 1f) * Mathf.Clamp01(SideGood) - (1f - SideDrainBad) * Mathf.Clamp01(SideBad);
        /// <summary>The line's load multiplier from side pressure this step (1 = none).</summary>
        public float SideTensionMult => 1f + SideTension * Mathf.Clamp01(SideGood + SideBad);
        /// <summary>Runs turned by side pressure so far.</summary>
        public int Turns { get; private set; }

        /// <summary>Side pressure that turns a run cuts what is left of it to this fraction (it runs on, its head come round).</summary>
        public const float TurnCut = 0.5f;

        /// <summary>
        /// Side pressure turned the running fish's head (the controller calls it once the fish's sideways run has been bent
        /// back towards the rod's side): it runs on, but the rest of the run is cut to <see cref="TurnCut"/>.
        /// </summary>
        public void Turn()
        {
            if (Result != Outcome.None || (State != Phase.Run && State != Phase.Burst)) return;
            phaseTime *= TurnCut;
            Turns++;
        }

        // ---- the current (the controller sets these every step: Docs/time_currents_spec.md 9.7)
        /// <summary>-1..1: how much the fish's run goes with the current (+) or against it (-).</summary>
        public float CurrentAlign;
        /// <summary>kgf on the line from the fish holding in the flow, and from the current across the line.</summary>
        public float CurrentLoad, LineDrag;
        /// <summary>The current's speed at the fish (m/s): a downstream run comes nearer at a quarter of it.</summary>
        public float CurrentSpeed;
        /// <summary>This run rides the stream down past his side (it takes no line, it comes nearer; the load is x 1.5).</summary>
        public bool Downstream;

        /// <summary>A burst turned by side pressure runs on into a run (<see cref="NextPhase"/>): that run is cut short as well.</summary>
        public void CutRun()
        {
            if (Result == Outcome.None && State == Phase.Run) phaseTime *= TurnCut;
        }

        // ---- the current (or last) jump
        public JumpKind Jump { get; private set; }
        public float JumpDuration { get; private set; } = 1f;
        /// <summary>Jump progress 0..1 at the centre of each head shake.</summary>
        public float[] ShakeAt { get; private set; } = new float[0];
        /// <summary>Jump progress 0..1, or -1 when not jumping.</summary>
        public float JumpT => State == Phase.Jump ? Mathf.Clamp01(1f - phaseTime / JumpDuration) : -1f;
        /// <summary>True for a moment around each head shake (the moments the hook can be thrown).</summary>
        public bool Shaking
        {
            get
            {
                float t = JumpT;
                if (t < 0) return false;
                foreach (float s in ShakeAt) if (Mathf.Abs(t - s) * JumpDuration < 0.07f) return true;
                return false;
            }
        }
        int nextShake;

        readonly FishSpecies sp;
        readonly RodDef rod;
        readonly ReelDef reel;
        readonly LineDef line;
        readonly float sizeT, landDist;
        float phaseTime, slack, breakTimer, elapsed;
        // the tension without side pressure's extra load (smoothed as Tension): the stamina drain reads it, so the side
        // multipliers alone (SideDrainMult) set how much faster / slower the fish tires with the rod leant
        float plainTension;
        readonly System.Random rnd;

        float SlackLimit => 2.4f + rod.hookBonus * 2f;
        float BreakGrace => 0.3f + rod.flex * 0.6f;

        public FightModel(FishSpecies sp, float sizeCm, RodDef rod, ReelDef reel, LineDef line, float stagePower,
            float startLine, float landDist, int seed)
        {
            this.sp = sp;
            this.rod = rod;
            this.reel = reel;
            this.line = line;
            this.landDist = landDist;
            sizeT = sp.SizeT(sizeCm);
            Power = sp.power * (0.55f + 0.45f * sizeT) * stagePower;
            Line = startLine;
            rnd = new System.Random(seed);
            phaseTime = 1.0f;
            Tension = plainTension = Power * 0.3f;
        }

        float holdT;

        /// <summary>
        /// A grace period (a legend hooked out of its encounter): for <paramref name="seconds"/> the fight stands still, the
        /// tension at 0 and the line fixed, so the player can get a finger ready to circle.
        /// </summary>
        public void Hold(float seconds)
        {
            holdT = Mathf.Max(holdT, seconds);
            Tension = plainTension = 0f;
        }

        public bool Holding => holdT > 0f;

        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        void NextPhase(bool nearSurface)
        {
            var prev = State;
            if (Exhausted)
            {
                State = Phase.Rest;
                phaseTime = 1f;
            }
            else if (prev == Phase.Rest && ForceJump.HasValue)
            {
                State = Phase.Jump;
                StartJump();
                phaseTime = JumpDuration;
            }
            else if (prev == Phase.Rest)
            {
                if (rnd.NextDouble() < 0.55 + 0.4 * sp.aggression)
                {
                    if (nearSurface && rnd.NextDouble() < sp.jump * 0.6)
                    {
                        State = Phase.Jump;
                        StartJump();
                        phaseTime = JumpDuration;
                    }
                    else if (rnd.NextDouble() < sp.aggression * 0.35)
                    {
                        State = Phase.Burst;
                        phaseTime = 0.5f;
                    }
                    else
                    {
                        State = Phase.Run;
                        phaseTime = R(1.0f, 2.3f) * (0.8f + sp.aggression * 0.6f);
                    }
                }
                else
                {
                    State = Phase.Rest;
                    phaseTime = R(0.6f, 1.2f);
                }
            }
            else if (prev == Phase.Burst)
            {
                State = Phase.Run;
                phaseTime = R(0.8f, 1.8f);
            }
            else
            {
                State = Phase.Rest;
                phaseTime = R(0.9f, 2.2f) * (1.4f - sp.aggression * 0.6f);
            }
            if (State != prev) PhaseChanged?.Invoke(State);
        }

        void StartJump()
        {
            double r = rnd.NextDouble();
            Jump = ForceJump ?? sp.jumpStyle switch
            {
                JumpStyle.Shaker => r < 0.7 ? JumpKind.Shake : JumpKind.Hop,
                JumpStyle.TailWalker => r < 0.6 ? JumpKind.TailWalk : r < 0.85 ? JumpKind.Shake : JumpKind.Hop,
                _ => JumpKind.Hop,
            };
            int shakes;
            switch (Jump)
            {
                case JumpKind.Shake:
                    // rise 0.3 s, one shake per 0.24 s, fall 0.3 s
                    shakes = 2 + rnd.Next(3);
                    JumpDuration = 0.6f + 0.24f * shakes;
                    break;
                case JumpKind.TailWalk:
                    JumpDuration = R(1.8f, 2.6f);
                    shakes = Mathf.Max(2, Mathf.FloorToInt(JumpDuration * 0.76f / 0.45f));
                    break;
                default:
                    JumpDuration = R(0.7f, 0.9f);
                    shakes = 0;
                    break;
            }
            // shakes spread evenly over the part of the jump spent in the air (between rise and fall)
            float a = Jump == JumpKind.TailWalk ? 0.12f : 0.3f / JumpDuration, b = 1f - a;
            ShakeAt = new float[shakes];
            for (int i = 0; i < shakes; i++) ShakeAt[i] = a + (b - a) * (i + 0.5f) / shakes;
            nextShake = 0;
        }

        /// <param name="revs">reel handle revolutions per second (negative = back-reeling)</param>
        /// <param name="nearSurface">fish is shallow enough to jump</param>
        public void Step(float dt, float revs, bool nearSurface)
        {
            if (Result != Outcome.None) return;
            if (holdT > 0f)
            {
                holdT -= dt;
                Tension = plainTension = 0f;
                return;
            }
            elapsed += dt;
            Giving = revs < 0f;
            bool runNow = (State == Phase.Run || State == Phase.Burst) && !Exhausted;
            phaseTime -= runNow ? dt * (1f - SideRunLonger * Mathf.Clamp01(SideBad)) : dt;
            // the cover: the run does not end before it arrives (at most 4 s), the hold not before it runs out
            if (Exhausted || !runNow) CoverRun = CoverHold = false;
            if (CoverRun)
            {
                coverRunT += dt;
                if (coverRunT >= 4f) CoverRun = false;
                else phaseTime = Mathf.Max(phaseTime, 0.05f);
            }
            if (CoverHold)
            {
                coverHoldLeft -= dt;
                if (coverHoldLeft > 0f) phaseTime = Mathf.Max(phaseTime, 0.05f);
                else
                {
                    // it runs out of the cover with a normal run
                    CoverHold = false;
                    State = Phase.Run;
                    phaseTime = R(1.0f, 2.3f) * (0.8f + sp.aggression * 0.6f);
                    HoldEnded?.Invoke();
                    PhaseChanged?.Invoke(State);
                }
            }
            if (phaseTime <= 0) NextPhase(nearSurface);

            float stam = 0.35f + 0.65f * Stamina;
            float force, swim;
            switch (State)
            {
                case Phase.Run: force = 1f; swim = 1f; break;
                case Phase.Burst: force = 1.4f; swim = 1.5f; break;
                // each head shake jerks the line
                case Phase.Jump: force = Shaking ? 0.95f : 0.55f; swim = Jump == JumpKind.TailWalk ? 0.7f : 0.4f; break;
                default: force = 0.22f; swim = -0.12f; break;
            }
            if (Exhausted) { force = 0.12f; swim = -0.25f; }
            // dug in at its cover: it holds (0.6) and takes no line
            bool holding = CoverHold && !Exhausted;
            if (holding) { force = 0.6f; swim = 0f; }
            float F = Power * force * stam * Mathf.Clamp01(0.35f + elapsed / 0.8f); // the fish needs a moment to realise it is hooked
            float u = sp.speed * swim * (0.4f + 0.6f * Stamina);   // how fast the fish wants to take line
            float v = revs * reel.retrieve;                        // how fast the player winds
            if (revs >= 0) v += reel.autoReel;

            float targetT, dL;
            bool running = (State == Phase.Run || State == Phase.Burst) && !Exhausted;
            // the current: a run with it pulls harder and takes line faster (up to x1.5 / x1.6), against it less
            // (x0.7 / x0.6); a downstream run comes nearer, taking no line
            float al = Mathf.Clamp(CurrentAlign, -1f, 1f);
            if (running)
            {
                F *= 1f + 0.5f * Mathf.Max(al, 0f) + 0.3f * Mathf.Min(al, 0f);
                u = Downstream ? -0.25f * CurrentSpeed : u * (1f + 0.6f * Mathf.Max(al, 0f) + 0.4f * Mathf.Min(al, 0f));
                // racing for its cover: harder, faster
                if (CoverRun)
                {
                    F *= 1.2f;
                    u *= 1.15f;
                }
            }
            if (v >= 0)
            {
                // spool locked: the fish pulls against the rod; winding adds strain
                float strain = Mathf.Clamp(v / (reel.retrieve * 2f), 0f, 1.6f) * Power * 0.5f;
                targetT = F + strain;
                float weak = Mathf.Clamp01(1f - Power / line.strength);
                float eff = running ? 0.3f + 0.5f * weak : 1f;
                dL = Mathf.Max(0, u) * 0.18f - v * eff + (u < 0 ? u * 0.3f : 0f);
            }
            else
            {
                // giving line: tension collapses, the fish swims off freely
                targetT = F * 0.15f;
                dL = -v + Mathf.Max(0, u) * 0.5f;
            }
            // the rod held to the side (side pressure, either way) loads the line a little more
            float plainT = targetT;
            // the fish holding in the flow and the current across the line load it (half of it while giving line); the
            // current's own load is not the fish's work (plainT: the stamina drain)
            float flowLoad = Mathf.Max(0f, CurrentLoad) + Mathf.Max(0f, LineDrag);
            targetT += v >= 0 ? flowLoad : flowLoad * 0.5f;
            targetT *= SideTensionMult;

            // drag slips once the tension exceeds its setting
            if (targetT > reel.dragMax)
            {
                float excess = targetT - reel.dragMax;
                dL += excess / Mathf.Max(0.5f, Power) * sp.speed * 0.6f;
                targetT = reel.dragMax + excess * (1f - reel.dragSmooth);
            }
            if (plainT > reel.dragMax) plainT = reel.dragMax + (plainT - reel.dragMax) * (1f - reel.dragSmooth);

            float resp = Mathf.Lerp(10f, 4f, rod.flex); // flexible rods smooth out spikes
            float follow = 1f - Mathf.Exp(-resp * dt);
            Tension = Mathf.Lerp(Tension, targetT, follow);
            plainTension = Mathf.Lerp(plainTension, plainT, follow);
            Line = Mathf.Max(0f, Line + dL * dt);

            // (the work the fish does against the tackle, without the side load: see plainTension)
            float work = plainTension / Mathf.Max(0.1f, Power);
            // (with the current it tires slower, against it faster)
            Stamina -= dt * (0.25f + 0.75f * Mathf.Clamp(work, 0f, 1.5f)) / sp.stamina * SideDrainMult * (1f - 0.25f * al) * (holding ? 0.5f : 1f);
            if (State == Phase.Rest && Tension < Power * 0.3f) Stamina += dt * 0.02f;
            Stamina = Mathf.Clamp01(Stamina);

            if (Tension > LineLimit)
            {
                breakTimer += dt;
                if (breakTimer > BreakGrace)
                {
                    Result = Outcome.Snapped;
                    // a scuffed line that gives where it rubs broke on the structure
                    SnapCause = Rubbing && Abrasion > 0.02f ? Cause.Abrasion : Cause.Tension;
                }
            }
            else breakTimer = Mathf.Max(0, breakTimer - dt * 2f);

            if (Line > reel.lineCap) Result = Outcome.Spooled;

            if (Tension < Power * 0.05f && !Exhausted) slack += dt;
            else slack = Mathf.Max(0, slack - dt);
            if (slack > SlackLimit) Result = Outcome.Escaped;

            // a jumping fish shakes its head: too much tension throws the hook
            if (State == Phase.Jump)
            {
                if (Jump == JumpKind.Hop)
                {
                    if (Tension > LineLimit * 0.7f && rnd.NextDouble() < dt * 1.2f) Result = Outcome.Escaped;
                }
                else if (nextShake < ShakeAt.Length && JumpT >= ShakeAt[nextShake])
                {
                    // one roll per shake, on its peak
                    nextShake++;
                    float p = Jump == JumpKind.Shake ? 0.45f : 0.3f;
                    if (Tension > LineLimit * 0.6f && rnd.NextDouble() < p) Result = Outcome.Escaped;
                }
            }

            if (Line <= landDist && Result == Outcome.None) Result = Outcome.Landed;
        }
    }
}
