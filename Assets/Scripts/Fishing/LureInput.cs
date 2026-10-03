using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingKing
{
    /// <summary>
    /// The three verbs lures (and the legend encounter) are played with, read from the inputs the game already has
    /// (Docs/lures_legend_spec.md 1.1):
    /// <list type="bullet">
    /// <item>감기 (wind): reel circles anywhere, <see cref="CircleGesture.Speed"/> &gt;= <see cref="WindMin"/> rev/s (the
    /// keyboard's Space / R wind at 2.6 rev/s without circling, and count too);</item>
    /// <item>톡 (flick): a short quick DOWNWARD swipe (pulling the finger down jerks the rod up, like lifting the rod tip),
    /// classified when a world press is let go: at most <see cref="FlickMaxTime"/> long, at least <see cref="FlickMinTravel"/>
    /// H down, within <see cref="FlickMaxAngle"/> of straight down, never circling, its peak downward speed
    /// (<see cref="FlickCast"/>'s window, measured downward) at least the flick minimum. Strength 0..1 maps that speed from
    /// the minimum to half the full-power speed (touch: 5..30 cm/s). An upward swipe is no 톡 (reject "up": that is the
    /// cast's gesture). Keyboard: T = 0.45, Shift+T = 0.9;</item>
    /// <item>멈춤 (pause): neither; <see cref="PauseT"/> is the time since the last flick or wind (a finger resting still on
    /// the screen is a pause too).</item>
    /// </list>
    /// A tap (a press of at most 0.35 s that barely moves) does nothing to a lure; it sets the hook in a bite as before.
    /// </summary>
    public class LureInput
    {
        public const float WindMin = 0.1f;          // rev/s
        public const float FlickMaxTime = 0.35f;    // s
        public const float FlickMinTravel = 0.05f;  // H, net DOWNWARDS
        public const float FlickMaxAngle = 60f;     // degrees from straight down
        public const float TapMaxTravel = 0.02f;    // H
        public const float KeyWeak = 0.45f, KeyStrong = 0.9f;

        /// <summary>A flick (strength 0..1) this frame.</summary>
        public event Action<float> Flicked;
        public event Action Tapped;

        public bool FlickNow { get; private set; }
        public bool TapNow { get; private set; }
        /// <summary>Strength (0..1) of the last flick.</summary>
        public float FlickStrength { get; private set; }
        /// <summary>Winding in this frame (<see cref="CircleGesture.Speed"/> &gt;= <see cref="WindMin"/>).</summary>
        public bool Winding { get; private set; }
        /// <summary>Winding speed, rev/s (+ = in).</summary>
        public float Speed { get; private set; }
        /// <summary>Seconds since the last flick or wind.</summary>
        public float PauseT { get; private set; }
        /// <summary>Length of the current run of winding (0 while not winding).</summary>
        public float WindRunT { get; private set; }
        public float SinceFlick { get; private set; } = 99f;
        public int Flicks { get; private set; }
        /// <summary>Why the last world release was not a flick (for the log): "", "tap", "long", "up" (a quick upward swipe:
        /// the 톡 is pulled DOWN), "short", "angle", "circled", "slow".</summary>
        public string LastReject { get; private set; } = "";
        /// <summary>Upward swipes turned away so far (the old 톡 direction; for the HUD's reminder and the tests).</summary>
        public int UpRejects { get; private set; }
        /// <summary>True on the frame an upward swipe was turned away.</summary>
        public bool UpRejectNow { get; private set; }

        bool pressing, circled;

        public void Reset()
        {
            FlickNow = TapNow = Winding = UpRejectNow = false;
            PauseT = WindRunT = 0f;
            SinceFlick = 99f;
            pressing = circled = false;
        }

        /// <summary>Call once a frame right after <see cref="CircleGesture.Update"/>; <paramref name="on"/> false ignores input.</summary>
        public void Update(CircleGesture g, float dt, bool on)
        {
            FlickNow = TapNow = UpRejectNow = false;
            Speed = on ? g.Speed : 0f;
            Winding = on && g.Speed >= WindMin;
            if (!on)
            {
                pressing = false;
            }
            else
            {
                // only a press that started in the world (not on a button) while lure input is on can be a flick / tap
                if (PointerInput.Pressed && !PointerInput.StartedOverUI)
                {
                    pressing = true;
                    circled = false;
                }
                if (pressing && PointerInput.IsDown && g.Circling) circled = true;
                if (pressing && PointerInput.Released)
                {
                    pressing = false;
                    Classify();
                }
                if (PointerInput.KeyPressed(Key.T))
                {
                    bool shift = PointerInput.KeyHeld(Key.LeftShift) || PointerInput.KeyHeld(Key.RightShift);
                    Fire(shift ? KeyStrong : KeyWeak);
                }
            }
            SinceFlick += dt;
            if (Winding)
            {
                WindRunT += dt;
                PauseT = 0f;
            }
            else
            {
                WindRunT = 0f;
                PauseT += dt;
            }
            if (FlickNow) PauseT = 0f;
        }

        void Classify()
        {
            var s = PointerInput.Samples;
            int n = s.Count;
            if (n == 0) return;
            float H = Mathf.Max(1f, Screen.height);
            float dur = s[n - 1].time - s[0].time;
            var net = (s[n - 1].pos - s[0].pos) / H;
            float far = 0f;
            for (int i = 1; i < n; i++) far = Mathf.Max(far, (s[i].pos - s[0].pos).magnitude / H);
            if (dur <= FlickMaxTime && far < TapMaxTravel)
            {
                LastReject = "tap";
                TapNow = true;
                Tapped?.Invoke();
                return;
            }
            if (circled) { LastReject = "circled"; return; }
            if (dur > FlickMaxTime) { LastReject = "long"; return; }
            // the 톡 is pulled DOWN the screen (screen y grows upwards): a quick upward swipe is turned away
            float drop = -net.y;
            if (drop < FlickMinTravel)
            {
                if (net.y >= FlickMinTravel && Mathf.Abs(Mathf.Atan2(net.x, net.y)) * Mathf.Rad2Deg <= FlickMaxAngle)
                {
                    LastReject = "up";
                    UpRejects++;
                    UpRejectNow = true;
                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[Lure] no flick (up): an upward swipe {0:0.000}H in {1:0.00}s is no 톡 (pull DOWN)", net.y, dur));
                }
                else LastReject = "short";
                return;
            }
            if (Mathf.Abs(Mathf.Atan2(net.x, drop)) * Mathf.Rad2Deg > FlickMaxAngle) { LastReject = "angle"; return; }
            var units = FlickCast.UnitsNow(H);
            var r = FlickCast.Analyze(s, H, units, down: true);
            if (r.speed < units.min) { LastReject = "slow"; return; }
            LastReject = "";
            float strength = Mathf.InverseLerp(units.min, 0.5f * units.full, r.speed);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Lure] 톡 down {0:0.000}H in {1:0.00}s speed {2:0.00}{3} strength {4:0.00}", drop, dur, r.speed, r.unit, strength));
            Fire(strength);
        }

        /// <summary>
        /// The press just let go was a quick swipe UP the screen (the 챔질 in a bite: FishingController.Strike): at most
        /// <see cref="FlickMaxTime"/>, at least <see cref="FlickMinTravel"/> up, within <see cref="FlickMaxAngle"/> of
        /// straight up and fast enough; its <paramref name="strength"/> 0..1 on the 톡's scale (the same speeds, mirrored).
        /// </summary>
        public static bool UpSwipe(out float strength)
        {
            strength = 0f;
            var s = PointerInput.Samples;
            int n = s.Count;
            if (n < 2) return false;
            float H = Mathf.Max(1f, Screen.height);
            float dur = s[n - 1].time - s[0].time;
            var net = (s[n - 1].pos - s[0].pos) / H;
            if (dur > FlickMaxTime || net.y < FlickMinTravel) return false;
            if (Mathf.Abs(Mathf.Atan2(net.x, net.y)) * Mathf.Rad2Deg > FlickMaxAngle) return false;
            var units = FlickCast.UnitsNow(H);
            var r = FlickCast.Analyze(s, H, units);
            if (r.speed < units.min) return false;
            strength = Mathf.Clamp01(Mathf.InverseLerp(units.min, 0.5f * units.full, r.speed));
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Lure] swipe up {0:0.000}H in {1:0.00}s speed {2:0.00}{3} strength {4:0.00}", net.y, dur, r.speed, r.unit, strength));
            return true;
        }

        void Fire(float strength)
        {
            FlickNow = true;
            FlickStrength = Mathf.Clamp01(strength);
            Flicks++;
            SinceFlick = 0f;
            PauseT = 0f;
            Flicked?.Invoke(FlickStrength);
        }
    }

    /// <summary>
    /// How well the equipped lure is being worked (Docs/lures_legend_spec.md 1.4): action quality <see cref="Q"/> 0..1,
    /// whether a following fish may strike now (<see cref="StrikeOpen"/>, with a new <see cref="WindowId"/> each time a
    /// strike roll comes due) and a short feedback word (<see cref="Feedback"/>, non-null on the frame it changes).
    /// <para>Steady lures (감기): the instant score is 1 winding inside the reel band, falling to 0 over +-0.3 rev/s outside,
    /// 0 when paused; a flick costs 0.2 once; Q follows it with a 2 s time constant.</para>
    /// <para>Cycle lures (everything else): a cycle is work (a flick burst, or a wind run no longer than runMax) and then a
    /// rest, scored when the next work starts or the rest has lasted rest.y + 1 s: 1 when the flick count, every gap, the
    /// rest, every flick's strength (and for bottom lures, the rest spent on the bottom) are all inside their windows, 0.5
    /// when exactly one is off by no more than half its window's width, else 0.15; then Q += (score - Q) / 2.</para>
    /// </summary>
    public class LureRhythm
    {
        public const float StartQ = 0.15f;
        public float Q { get; private set; } = StartQ;
        public bool StrikeOpen { get; private set; }
        /// <summary>Changes each time a strike roll comes due: a cycle lure's window opening, every 0.5 s of a steady lure's.</summary>
        public int WindowId { get; private set; }
        /// <summary>The word to show this frame (null = nothing new).</summary>
        public string Feedback { get; private set; }
        public bool FeedbackGood { get; private set; }
        /// <summary>The last cycle's score (-1 = none yet) and how many cycles were scored.</summary>
        public float LastScore { get; private set; } = -1f;
        public int Cycles { get; private set; }
        /// <summary>Steady lures: winding inside the band right now.</summary>
        public bool InBand { get; private set; }
        public string Phase => phase.ToString();

        enum Ph { Idle, Work, Rest }
        Ph phase;
        BaitDef bait;
        float t;
        // the cycle being worked
        bool runWork;                 // this cycle's work is a wind run (else a flick burst)
        int burstFlicks;
        float lastFlickT, maxStrength, worstGap, runLen, runSpeedSum;
        bool runOn;
        float runOffT;
        float restStart, restBottom, lastWorkEnd;
        // steady
        float inBandT, outFastT, outSlowT, sinceBand = 99f;
        bool strikeWas;
        float windowT;
        int perfectRun;
        bool saidPerfect;
        float qBefore;
        // feedback
        string lastWord;
        float lastSayT = -99f;

        public void Reset(BaitDef b)
        {
            bait = b;
            Q = StartQ;
            t = 0f;
            phase = Ph.Idle;
            StrikeOpen = strikeWas = false;
            Feedback = null;
            LastScore = -1f;
            Cycles = 0;
            perfectRun = 0;
            inBandT = outFastT = outSlowT = 0f;
            sinceBand = 99f;
            runOn = false;
            lastWorkEnd = 0f;
            lastWord = null;
            lastSayT = -99f;
            saidPerfect = false;
        }

        /// <summary>A natural entry (a bank shot, a drop under the branches: Docs/obstacles_spec.md 8.3) starts the lure better worked.</summary>
        public void Boost(float dq) => Q = Mathf.Clamp01(Q + dq);

        float GapLimit => bait.gap.y > 0f ? bait.gap.y + 0.5f * (bait.gap.y - bait.gap.x) : 0.6f;
        bool DragWork => bait.work == Work.Flick && bait.runMax > 0f && bait.reelBand.y > 0f;

        /// <summary>
        /// The current along the line at the lure (m/s, + = the water runs away from him past it, i.e. he winds against it;
        /// Docs/time_currents_spec.md 9.4), set by <see cref="Update"/>.
        /// </summary>
        public float CAlong { get; private set; }
        /// <summary>A steady lure not wound for 0.5 s with the current against it (c_along >= 0.15): it hangs and works in the flow.</summary>
        public bool Hanging { get; private set; }
        /// <summary>Flick lures: the flick's effect x this (clamp(1 + c_along, 0.5, 1.5): a light 톡 is enough against the current).</summary>
        public float FlickMult => Mathf.Clamp(1f + CAlong, 0.5f, 1.5f);
        float reelRetrieve = 1f;

        public void Update(BaitDef b, LureInput input, Tackle tackle, float dt, float cAlong = 0f)
        {
            if (b != bait) Reset(b);
            Feedback = null;
            CAlong = cAlong;
            Hanging = false;
            if (b == null || b.action == LureAction.None) return;
            reelRetrieve = Game.I != null && Game.I.Reel != null ? Mathf.Max(0.05f, Game.I.Reel.retrieve) : 1f;
            t += dt;
            qBefore = Q;
            if (b.IsSteady) UpdateSteady(input, dt);
            else UpdateCycle(input, tackle, dt);
            if (qBefore < 0.75f && Q >= 0.75f) Say("좋아요!", true);
            // a strike roll comes due each time a window opens (steady: every 0.5 s while open)
            if (StrikeOpen && (!strikeWas || (b.IsSteady && t - windowT >= 0.5f)))
            {
                WindowId++;
                windowT = t;
            }
            strikeWas = StrikeOpen;
        }

        // ------------------------------------------------------------------ steady (감기)
        void UpdateSteady(LureInput input, float dt)
        {
            var band = bait.reelBand;
            // the current along the line works the lure too: wound against it, it runs faster through the water (the band
            // is met at a slower wind); not wound for 0.5 s with the current against it, it hangs and works in the flow
            float sp = input.Speed, s = 0f;
            bool winding = input.Winding;
            if (winding) sp += CAlong / reelRetrieve;
            else if (CAlong >= 0.15f && input.PauseT >= 0.5f)
            {
                Hanging = true;
                winding = true;
                sp = CAlong / reelRetrieve;
            }
            if (input.FlickNow) Q = Mathf.Max(0f, Q - 0.2f);
            if (winding)
            {
                if (sp < band.x) s = Mathf.Clamp01(1f - (band.x - sp) / 0.3f);
                else if (sp > band.y) s = Mathf.Clamp01(1f - (sp - band.y) / 0.3f);
                else s = 1f;
            }
            InBand = winding && sp >= band.x && sp <= band.y;
            Q += (s - Q) * (1f - Mathf.Exp(-dt / 2f));
            Q = Mathf.Clamp01(Q);
            inBandT = InBand ? inBandT + dt : 0f;
            sinceBand = InBand ? 0f : sinceBand + dt;
            outFastT = winding && sp > band.y ? outFastT + dt : 0f;
            outSlowT = (winding && sp < band.x) || (!winding && input.PauseT > 1.5f) ? outSlowT + dt : 0f;
            if (inBandT >= 4f && !saidPerfect)
            {
                saidPerfect = true;
                Say("완벽!", true);
            }
            if (!InBand) saidPerfect = false;
            if (outFastT > 0.6f) Say("너무 빨라요", false);
            else if (outSlowT > 0.6f) Say("너무 느려요", false);
            // a spoon flutters down for a moment after a stop: fish hit it then too
            StrikeOpen = InBand || (bait.pauseStrike > 0f && !winding && sinceBand < bait.pauseStrike);
        }

        // ------------------------------------------------------------------ cycles (저킹 · 수면 · 바닥 · 수직)
        void UpdateCycle(LureInput input, Tackle tackle, float dt)
        {
            bool flickWork = bait.work == Work.Flick;
            // winding a flick lure: slow circles to take up slack are fine; fast ones spoil it
            if (flickWork && input.Winding && input.Speed > 0.8f)
            {
                Q = Mathf.Max(0f, Q - 0.3f * dt);
                Say("멈춰요", false);
            }
            if (flickWork && input.FlickNow)
            {
                // (the current along the line scales what a 톡 does: against it a light one is enough)
                float strength = Mathf.Clamp01(input.FlickStrength * FlickMult);
                if (phase == Ph.Work && !runWork && t - lastFlickT <= GapLimit)
                {
                    // the burst goes on
                    burstFlicks++;
                    worstGap = WorseGap(worstGap, t - lastFlickT);
                    lastFlickT = t;
                    maxStrength = Mathf.Max(maxStrength, strength);
                }
                else StartWork(false, strength, tackle);
            }
            // wind runs: the frog's work, the soft worm's slow drag
            bool windWork = !flickWork || DragWork;
            bool winding = input.Winding && (!flickWork || input.Speed <= 0.8f);
            if (windWork)
            {
                if (winding)
                {
                    runOffT = 0f;
                    if (!runOn)
                    {
                        runOn = true;
                        if (!(phase == Ph.Work && !runWork)) StartWork(true, 0f, tackle); // not in the middle of a flick burst
                    }
                    if (phase == Ph.Work && runWork)
                    {
                        runLen += dt;
                        runSpeedSum += input.Speed * dt;
                        // wound far too long: score it as a spoiled cycle and start over
                        if (runLen > bait.runMax * 1.5f + bait.rest.y)
                        {
                            Score(RestFrom(t), 0f, true);
                            phase = Ph.Idle;
                            runOn = false;
                        }
                    }
                }
                else if (runOn)
                {
                    runOffT += dt;
                    if (runOffT > 0.2f)
                    {
                        runOn = false;
                        if (phase == Ph.Work && runWork) BeginRest(t - runOffT, tackle);
                    }
                }
            }
            // a flick burst ends when no further flick comes in time: the rest starts at its last flick
            if (phase == Ph.Work && !runWork && t - lastFlickT > GapLimit) BeginRest(lastFlickT, tackle);
            if (phase == Ph.Rest)
            {
                if (tackle != null && tackle.OnBottom) restBottom += dt;
                if (t - restStart >= bait.rest.y + 1f)
                {
                    Score(t - restStart, restBottom, false);
                    phase = Ph.Idle;
                    lastWorkEnd = restStart;
                }
            }
            // idle: nothing worked for a while, the lure just hangs there
            if (phase != Ph.Work && t - lastWorkEnd > bait.rest.y + 2f)
            {
                Q = Mathf.MoveTowards(Q, 0.1f, 0.3f * dt);
                Say(flickWork ? "톡 당겨요" : "살살 감아요", false);
            }
            // strike windows
            bool open = false;
            float restT = phase == Ph.Rest ? t - restStart : -1f;
            if (bait.restStrike >= 0f && restT >= bait.restStrike && restT <= bait.rest.y + 1f) open = true;
            if (bait.burstStrike && phase == Ph.Work && !runWork) open = true;
            if (tackle != null)
            {
                if (bait.fallStrike.x >= 0f && tackle.Falling && tackle.FallT >= bait.fallStrike.x && tackle.FallT <= bait.fallStrike.y) open = true;
                if (bait.landStrike > 0f && tackle.SinceTouch <= bait.landStrike) open = true;
            }
            StrikeOpen = open;
        }

        float RestFrom(float now) => phase == Ph.Rest ? now - restStart : 0f;

        void StartWork(bool run, float strength, Tackle tackle)
        {
            if (phase == Ph.Rest)
            {
                float r = t - restStart;
                // a bottom lure worked before it got back down
                if (bait.needBottom && r > 0.2f && restBottom < 0.3f * r) Say("바닥까지 기다려요", false);
                Score(r, restBottom, false);
            }
            phase = Ph.Work;
            runWork = run;
            burstFlicks = run ? 0 : 1;
            lastFlickT = t;
            maxStrength = strength;
            worstGap = -1f;
            runLen = 0f;
            runSpeedSum = 0f;
            if (!run && strength > bait.maxStrength) Say("너무 세요", false);
        }

        void BeginRest(float at, Tackle tackle)
        {
            phase = Ph.Rest;
            restStart = at;
            restBottom = 0f;
            lastWorkEnd = at;
        }

        /// <summary>Of two gaps, the one further outside the gap window.</summary>
        float WorseGap(float a, float b) => a < 0f ? b : Off(a, bait.gap) >= Off(b, bait.gap) ? a : b;

        /// <summary>How far a value lies outside a window, in window widths (0 = inside).</summary>
        static float Off(float v, Vector2 w)
        {
            float width = Mathf.Max(0.1f, w.y - w.x);
            return v < w.x ? (w.x - v) / width : v > w.y ? (v - w.y) / width : 0f;
        }

        // 0 = inside, 1 = a little off (<= 50 % of the window's width), 2 = off
        static int Grade(float off) => off <= 0f ? 0 : off <= 0.5f ? 1 : 2;

        void Score(float rest, float onBottom, bool spoiled)
        {
            int slight = 0, bad = spoiled ? 1 : 0;
            void Add(int g)
            {
                if (g == 1) slight++;
                else if (g == 2) bad++;
            }
            if (runWork)
            {
                float mean = runLen > 0f ? runSpeedSum / runLen : 0f;
                float max = Mathf.Max(0.1f, bait.runMax);
                Add(runLen <= max ? 0 : runLen <= max * 1.5f ? 1 : 2);
                if (bait.reelBand.y > 0f) Add(Grade(Off(mean, bait.reelBand) * Mathf.Max(0.1f, bait.reelBand.y - bait.reelBand.x) / 0.6f));
            }
            else
            {
                int n = burstFlicks;
                Add(n >= bait.flicks.x && n <= bait.flicks.y ? 0 : Mathf.Abs(n < bait.flicks.x ? bait.flicks.x - n : n - bait.flicks.y) == 1 ? 1 : 2);
                if (n >= 2 && bait.gap.y > 0f) Add(Grade(Off(worstGap, bait.gap)));
                if (bait.maxStrength < 1f) Add(maxStrength <= bait.maxStrength ? 0 : maxStrength <= bait.maxStrength + 0.2f ? 1 : 2);
            }
            if (bait.rest.y > 0f) Add(Grade(Off(rest, bait.rest)));
            if (bait.needBottom)
            {
                float frac = rest > 0.01f ? onBottom / rest : 0f;
                Add(frac >= 0.6f ? 0 : frac >= 0.3f ? 1 : 2);
            }
            float score = bad == 0 && slight == 0 ? 1f : bad == 0 && slight == 1 ? 0.5f : 0.15f;
            Q = Mathf.Clamp01(Q + (score - Q) * 0.5f);
            LastScore = score;
            Cycles++;
            if (score >= 1f)
            {
                perfectRun++;
                if (perfectRun >= 3)
                {
                    perfectRun = 0;
                    Say("완벽!", true);
                }
            }
            else perfectRun = 0;
        }

        /// <summary>At most one word per 2 s, and a word only when it changes (or 8 s after it last showed).</summary>
        void Say(string word, bool good)
        {
            if (t - lastSayT < 2f) return;
            if (word == lastWord && t - lastSayT < 8f) return;
            Feedback = word;
            FeedbackGood = good;
            lastWord = word;
            lastSayT = t;
        }
    }
}
