using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The legend encounter as a pure phase machine with the interest gauge (Docs/lures_legend_spec.md 2.3, 2.8, 2.9;
    /// Docs/legends_rollout.md 1.1, 1.2), like <see cref="FightModel"/>: the controller feeds it the lure verbs each frame
    /// (<see cref="Input"/>), the view and the HUD read <see cref="Ph"/>, <see cref="Gauge"/>, <see cref="Mood"/> and
    /// follow its events.
    /// <code>
    /// Omen 0.8 > Open 0.5 > Eyes 3.0 (repeat 1.2) > Approach 3.0 > Tease (max teaseLimit)
    ///   Tease: gauge 100 (or the time limit with gauge >= timeoutStrike) > NoseIn (a fake-out mid-way for most legends;
    ///          the last `tell` s = the tell) > Lunge (lungeT) > HookWindow (the jaws close on entry; hookWindow +
    ///          rod.hookBonus) > Hooked 0.8 > Surface 0.45 > Done (success)
    ///   gauge 0 / the time limit below timeoutStrike / the hook window running out > TurnAway 1.2 > Close 0.3 > Done (fail)
    ///   an input in NoseIn (the fake-out too) = too early: gauge = earlyGauge, back to Tease (its clock runs on)
    /// </code>
    /// In Tease the gauge decays by decay/s and each mood (경계 / 호기심 / 흥분, chosen by the gauge with hysteresis)
    /// rewards its own verb: a wind in a band, a light flick then a pause, holding still, or a short wind run then a pause.
    /// A penalty of one kind hits (flinch, red flash, caption) at most once per 0.8 s; continuous ones keep costing while
    /// they last.
    /// </summary>
    public class LegendEncounter
    {
        public enum Phase { Omen, Open, Eyes, Approach, Tease, NoseIn, Lunge, HookWindow, Hooked, Surface, TurnAway, Close, Done }

        /// <summary>What went wrong (penalties, and the fail's worst mistake).</summary>
        public enum Kind { Fast, WaryFlick, Strong, Hurry, Circle, Bored, ExcitedFlick, ExcitedMove, Early, Missed, Slow, OverRun }

        /// <summary>The lure verbs this frame (<see cref="LureInput"/>), plus a fresh world press (a tap sets the hook).</summary>
        public struct Input
        {
            public bool flick;          // a flick this frame
            public float strength;      // its strength 0..1
            public bool winding;        // reel circles >= LureInput.WindMin rev/s
            public float speed;         // rev/s
            public float windRunT;      // how long the winding has gone on
            public float pauseT;        // since the last flick or wind
            public bool press;          // a press on the world this frame
            public bool Any => flick || winding || press;
        }

        public const float PenaltyGap = 0.8f;
        public const float OmenT = 0.8f, OpenT = 0.5f, EyesT = 3.0f, EyesRepeatT = 1.2f, ApproachT = 3.0f,
            HookedT = 0.8f, SurfaceT = 0.45f, TurnAwayT = 1.2f, CloseT = 0.3f, FakeOutT = 0.45f;
        /// <summary>A RunPause run ends after this long without winding (a circle's hitch is no pause).</summary>
        const float RunGap = 0.15f;

        public readonly FishSpecies Sp;
        public readonly EncounterDef Def;
        /// <summary>The legend was seen before (a short eyes beat, its name on the card straight away).</summary>
        public readonly bool Repeat;
        public readonly float HookWindowT;

        public Phase Ph { get; private set; } = Phase.Omen;
        /// <summary>Seconds into the current phase, and its length (Tease: the time limit).</summary>
        public float PhaseT { get; private set; }
        public float PhaseLen { get; private set; } = OmenT;
        public float Gauge { get; private set; }
        /// <summary>0 경계, 1 호기심, 2 흥분.</summary>
        public int Mood { get; private set; }
        public MoodDef MoodDef => Def.moods[Mood];
        /// <summary>Seconds spent in Tease (not reset by an early tap).</summary>
        public float TeaseT { get; private set; }
        public float MoodT { get; private set; }
        /// <summary>The gauge is below turnAwayBelow: the legend is about to turn away.</summary>
        public bool Leaving => Ph == Phase.Tease && Gauge < Def.turnAwayBelow;
        /// <summary>In the last tell seconds of NoseIn.</summary>
        public bool InTell => Ph == Phase.NoseIn && PhaseT >= PhaseLen - Def.tell;
        /// <summary>When this NoseIn's fake-out starts (s into the phase; -1 = none).</summary>
        public float FakeAt { get; private set; } = -1f;
        /// <summary>In the fake-out (the carp tastes, the sturgeon feels, the marlin slashes, the great white bumps).</summary>
        public bool InFakeOut => Ph == Phase.NoseIn && FakeAt >= 0f && PhaseT >= FakeAt && PhaseT < FakeAt + FakeOutT;
        /// <summary>0..1 through the fake-out (0 outside it).</summary>
        public float FakeOutK => InFakeOut ? (PhaseT - FakeAt) / FakeOutT : 0f;
        public bool Success { get; private set; }
        public bool Perfect { get; private set; }
        /// <summary>A perfect hook set after a tease with no penalty at all: the size is rolled twice.</summary>
        public bool PerfectLure => Perfect && Penalties == 0;
        public int Penalties { get; private set; }
        public string FailText { get; private set; }
        public string FailTip { get; private set; }
        /// <summary>The fail's cause: "gauge", "timeout" or "missed" (null while none).</summary>
        public string FailReason { get; private set; }
        /// <summary>Seconds after the jaws closed that the hook was set (-1 = buffered before the close).</summary>
        public float HookAt { get; private set; } = -99f;
        /// <summary>Credits so far (the marlin's stripes flash on each).</summary>
        public int Credits { get; private set; }

        /// <summary>The RunPause state (for the -fkencdebug log).</summary>
        public string DebugRun => string.Format(System.Globalization.CultureInfo.InvariantCulture, "run {0} len {1:0.00} band {2:0.00} gap {3:0.00} ended {4} good {5}", runOn, runLen, runBand, runGapT, runEnded, runGood);

        public event Action<Phase> PhaseChanged;
        public event Action<int> MoodChanged;
        public event Action<Kind, string> Penalty;
        public event Action<string> Credit;
        /// <summary>The tell (the last seconds of NoseIn): eyes and fins flare, a "ting".</summary>
        public event Action Tell;
        /// <summary>The fake-out starts / ends (no gold flash, no "ting").</summary>
        public event Action FakeOut, FakeOutEnd;
        /// <summary>The jaws close on the lure (the start of the hook window).</summary>
        public event Action Closed;
        /// <summary>The hook is set (perfect or not).</summary>
        public event Action<bool> HookSet;
        /// <summary>An input in NoseIn: the legend flinches, the gauge drops to earlyGauge.</summary>
        public event Action Early;

        readonly System.Random rnd;
        readonly Dictionary<Kind, float> lastHit = new Dictionary<Kind, float>();
        readonly Dictionary<Kind, float> cost = new Dictionary<Kind, float>();
        readonly float[] moodCost = new float[3];
        float clock;                         // Tease clock (penalty spacing, flick timing)
        float lastFlick = -99f, lastStrength;
        bool credited, heldCredit, windCredit, tellFired, buffered, hookDone, fakeFired, fakeEnded;
        float windCreditT = -99f;
        // RunPause: the run in progress / the last one
        bool runOn, runEnded, runGood;
        float runLen, runBand, runGapT, lastRunEnd = -99f;

        public LegendEncounter(FishSpecies sp, int pity, bool repeat, float hookBonus, int seed)
        {
            Sp = sp;
            Def = sp.encounter;
            Repeat = repeat;
            HookWindowT = Def.hookWindow + hookBonus;
            rnd = new System.Random(seed);
            Gauge = Mathf.Clamp(Def.gaugeStart + pity, 0, 99);
            Mood = MoodFor(Gauge, 0);
        }

        int MoodFor(float g, int cur)
        {
            int want = g >= Def.excitedAt ? 2 : g >= Def.curiousAt ? 1 : 0;
            if (want >= cur) return want;
            // dropping a mood takes going hysteresis below its threshold
            if (cur == 2 && g >= Def.excitedAt - Def.hysteresis) return 2;
            if (cur >= 1 && g >= Def.curiousAt - Def.hysteresis) return Mathf.Max(want, 1);
            return want;
        }

        void Go(Phase p)
        {
            Ph = p;
            PhaseT = 0f;
            switch (p)
            {
                case Phase.Open: PhaseLen = OpenT; break;
                case Phase.Eyes: PhaseLen = Repeat ? EyesRepeatT : EyesT; break;
                case Phase.Approach: PhaseLen = ApproachT; break;
                case Phase.Tease: PhaseLen = Def.teaseLimit; break;
                case Phase.NoseIn:
                {
                    float len = Mathf.Lerp(Def.noseIn.x, Def.noseIn.y, (float)rnd.NextDouble());
                    PhaseLen = len;
                    FakeAt = -1f;
                    fakeFired = fakeEnded = false;
                    if (Def.fakeOuts > 0)
                    {
                        // the fake-out 35-60 % into the nose-in, over before the tell
                        PhaseLen = len + FakeOutT;
                        float at = Mathf.Lerp(0.35f, 0.6f, (float)rnd.NextDouble()) * len;
                        FakeAt = Mathf.Clamp(at, 0.1f, Mathf.Max(0.1f, PhaseLen - Def.tell - FakeOutT - 0.1f));
                    }
                    tellFired = false;
                    buffered = false;
                    break;
                }
                case Phase.Lunge: PhaseLen = Def.lungeT; break;
                case Phase.HookWindow: PhaseLen = HookWindowT; hookDone = false; break;
                case Phase.Hooked: PhaseLen = HookedT; break;
                case Phase.Surface: PhaseLen = SurfaceT; break;
                case Phase.TurnAway: PhaseLen = TurnAwayT; break;
                case Phase.Close: PhaseLen = CloseT; break;
                default: PhaseLen = 0f; break;
            }
            PhaseChanged?.Invoke(p);
            if (p == Phase.HookWindow)
            {
                Closed?.Invoke();
                if (buffered) SetHook(-1f);
            }
        }

        /// <summary>Advances the encounter by dt with this frame's input.</summary>
        public void Tick(float dt, Input input)
        {
            if (Ph == Phase.Done) return;
            PhaseT += dt;
            switch (Ph)
            {
                case Phase.Omen: if (PhaseT >= OmenT) Go(Phase.Open); break;
                case Phase.Open: if (PhaseT >= PhaseLen) Go(Phase.Eyes); break;
                case Phase.Eyes: if (PhaseT >= PhaseLen) Go(Phase.Approach); break;
                case Phase.Approach:
                    if (PhaseT >= PhaseLen)
                    {
                        MoodT = 0f;
                        Go(Phase.Tease);
                    }
                    break;
                case Phase.Tease: TickTease(dt, input); break;
                case Phase.NoseIn:
                    if (input.Any)
                    {
                        // too early (the fake-out too): the legend flinches back and the tease goes on from the curious band
                        Hit(Kind.Early, 0f, "너무 일찍 챘어요!", true);
                        Gauge = Def.earlyGauge;
                        SetMood(MoodFor(Gauge, 1));
                        Early?.Invoke();
                        Go(Phase.Tease);
                        PhaseT = TeaseT;
                        break;
                    }
                    if (FakeAt >= 0f && !fakeFired && PhaseT >= FakeAt)
                    {
                        fakeFired = true;
                        FakeOut?.Invoke();
                    }
                    if (fakeFired && !fakeEnded && PhaseT >= FakeAt + FakeOutT)
                    {
                        fakeEnded = true;
                        FakeOutEnd?.Invoke();
                    }
                    if (!tellFired && PhaseT >= PhaseLen - Def.tell)
                    {
                        tellFired = true;
                        Tell?.Invoke();
                    }
                    if (PhaseT >= PhaseLen) Go(Phase.Lunge);
                    break;
                case Phase.Lunge:
                    // an input while the jaws come at the lure is held and counts at the close
                    if (input.Any) buffered = true;
                    if (PhaseT >= PhaseLen) Go(Phase.HookWindow);
                    break;
                case Phase.HookWindow:
                    if (!hookDone && input.Any) SetHook(PhaseT);
                    if (hookDone && PhaseT >= 0.12f) Go(Phase.Hooked);
                    else if (!hookDone && PhaseT >= PhaseLen)
                    {
                        Add(Kind.Missed, 1f);
                        Fail("missed", "뱉어버렸다…");
                    }
                    break;
                case Phase.Hooked: if (PhaseT >= PhaseLen) Go(Phase.Surface); break;
                case Phase.Surface:
                    if (PhaseT >= PhaseLen)
                    {
                        Success = true;
                        Go(Phase.Done);
                    }
                    break;
                case Phase.TurnAway: if (PhaseT >= PhaseLen) Go(Phase.Close); break;
                case Phase.Close: if (PhaseT >= PhaseLen) Go(Phase.Done); break;
            }
        }

        void SetHook(float at)
        {
            hookDone = true;
            HookAt = at;
            Perfect = at <= Def.perfectT;
            HookSet?.Invoke(Perfect);
        }

        void SetMood(int m)
        {
            if (m == Mood) return;
            Mood = m;
            MoodT = 0f;
            credited = heldCredit = windCredit = false;
            runOn = runEnded = false;
            MoodChanged?.Invoke(m);
        }

        void Credited(string text)
        {
            Credits++;
            Credit?.Invoke(text);
        }

        void TickTease(float dt, Input input)
        {
            TeaseT += dt;
            MoodT += dt;
            clock += dt;
            Gauge -= Def.decay * dt;
            var m = MoodDef;
            switch (m.verb)
            {
                case Verb.Wind:
                    if (input.flick) Hit(Kind.WaryFlick, m.flickLoss, "놀랐어요!", true);
                    else if (input.winding)
                    {
                        if (input.speed >= m.lo && input.speed <= m.hi)
                        {
                            Gauge += m.gain * dt;
                            if (!windCredit || clock - windCreditT > 4f)
                            {
                                windCredit = true;
                                windCreditT = clock;
                                Credited(m.creditText ?? "좋아요");
                            }
                        }
                        else if (input.speed > m.tooFast) Hit(Kind.Fast, m.fastLoss * dt, m.fastText ?? "너무 빨라요!", false);
                        else if (m.slowLoss > 0f && input.speed < m.tooSlow && input.windRunT > 0.4f) Hit(Kind.Slow, m.slowLoss * dt, m.slowText ?? "너무 느려요!", false);
                    }
                    // (the marlin / great white must keep going: stopping costs too)
                    else if (m.slowLoss > 0f && input.pauseT > m.slowAfter && MoodT > m.slowAfter) Hit(Kind.Slow, m.slowLoss * dt, m.slowText ?? "너무 느려요!", false);
                    break;
                case Verb.FlickPause:
                    if (input.flick)
                    {
                        if (clock - lastFlick < m.lo) Hit(Kind.Hurry, m.hurryLoss, "너무 서둘러요", true);
                        else if (input.strength > m.strongAt) Hit(Kind.Strong, m.strongLoss, "너무 세요!", true);
                        lastFlick = clock;
                        lastStrength = input.strength;
                        credited = false;
                    }
                    else if (!credited && lastFlick > clock - MoodT - 0.001f && !input.winding && input.pauseT >= m.lo && input.pauseT <= m.hi)
                    {
                        // a light flick (in this mood), then a pause long enough: one credit per flick
                        credited = true;
                        if (lastStrength <= m.maxStrength)
                        {
                            Gauge += m.gain;
                            Credited(m.creditText ?? "관심을 보여요!");
                        }
                    }
                    if (input.winding && Mathf.Min(input.windRunT, MoodT) > m.circleAfter) Hit(Kind.Circle, m.circleLoss * dt, "감지 말고 톡!", false);
                    if (clock - Mathf.Max(lastFlick, clock - MoodT) > m.boredAfter) Hit(Kind.Bored, m.boredLoss * dt, m.boredText ?? "지루해해요…", false);
                    break;
                case Verb.RunPause:
                    TickRunPause(dt, input, m);
                    break;
                case Verb.Hold:
                    if (input.flick) Hit(Kind.ExcitedFlick, m.flickLoss, "놀랐어요!", true);
                    else if (input.winding && Mathf.Min(input.windRunT, MoodT) > m.circleGrace) Hit(Kind.ExcitedMove, m.circleLoss * dt, "움직이면 안 돼요!", false);
                    else if (!input.winding && input.pauseT >= m.lo)
                    {
                        Gauge += m.gain * dt;
                        if (!heldCredit)
                        {
                            heldCredit = true;
                            Credited(m.creditText ?? "입을 벌린다…!");
                        }
                    }
                    break;
            }
            Gauge = Mathf.Clamp(Gauge, 0f, 100f);
            int next = MoodFor(Gauge, Mood);
            // (a mood just entered is held a moment: its first cycle may take longer than the hysteresis lasts)
            if (next < Mood && MoodT < Def.moodHold) next = Mood;
            SetMood(next);
            if (Gauge >= 100f)
            {
                Go(Phase.NoseIn);
                return;
            }
            if (Gauge <= 0f)
            {
                Fail("gauge", EncounterDef.Named(Def.lostText, Sp.name));
                return;
            }
            if (TeaseT >= Def.teaseLimit)
            {
                if (Gauge >= Def.timeoutStrike) Go(Phase.NoseIn);
                else Fail("timeout", "흥미를 잃고 돌아갔다…");
            }
        }

        /// <summary>
        /// A short wind run, then a pause (Docs/legends_rollout.md 1.2): a run of runMin..runMax s spent >= 70 % inside
        /// lo..hi rev/s, followed by a pause of pauseMin, gives +gain once. Winding above tooFast, a run longer than
        /// overRun and no run for boredAfter cost per second; a flick costs flickLoss once.
        /// </summary>
        void TickRunPause(float dt, Input input, MoodDef m)
        {
            if (input.flick) Hit(Kind.WaryFlick, m.flickLoss, "놀랐어요!", true);
            // the reel's measured speed rises and falls smoothly around a burst: a run is winding at half the band's
            // bottom or more, and its ramps (down to 3/4 of the bottom) count as in the band
            bool run = input.winding && input.speed >= m.lo * 0.5f;
            if (run)
            {
                if (!runOn)
                {
                    runOn = true;
                    runEnded = false;
                    runLen = runBand = 0f;
                }
                runLen += dt;
                runGapT = 0f;
                if (input.speed >= m.lo * 0.75f && input.speed <= m.hi) runBand += dt;
                lastRunEnd = clock;
                if (input.speed > m.tooFast) Hit(Kind.Fast, m.fastLoss * dt, m.fastText ?? "너무 빨라요!", false);
                if (runLen > m.overRun) Hit(Kind.OverRun, m.overRunLoss * dt, m.overRunText ?? "멈춰요!", false);
            }
            else
            {
                runGapT += dt;
                if (runOn && runGapT >= RunGap)
                {
                    runOn = false;
                    runEnded = true;
                    runGood = runLen >= m.runMin && runLen <= m.runMax && runBand >= 0.7f * runLen;
                }
            }
            if (runEnded && !run && runGapT >= m.pauseMin)
            {
                runEnded = false;
                if (runGood)
                {
                    Gauge += m.gain;
                    Credited(m.creditText ?? "좋아요");
                }
            }
            // no run for a while (counted from the mood's start)
            float since = clock - Mathf.Max(lastRunEnd, clock - MoodT);
            if (!run && since > m.boredAfter) Hit(Kind.Bored, m.boredLoss * dt, m.boredText ?? "지루해해요…", false);
        }

        /// <summary>
        /// A penalty: <paramref name="loss"/> off the gauge. A discrete one (a flick) is skipped entirely within
        /// <see cref="PenaltyGap"/> of the last of its kind; a continuous one keeps costing but only shows (flinch, caption)
        /// once per gap.
        /// </summary>
        void Hit(Kind k, float loss, string text, bool discrete)
        {
            bool recent = lastHit.TryGetValue(k, out float at) && clock - at < PenaltyGap;
            if (discrete && recent) return;
            Gauge -= loss;
            Add(k, discrete ? Mathf.Max(loss, 1f) : loss);
            if (recent) return;
            lastHit[k] = clock;
            Penalties++;
            Penalty?.Invoke(k, text);
        }

        void Add(Kind k, float amount)
        {
            cost.TryGetValue(k, out float c);
            cost[k] = c + amount;
            if (k != Kind.Early && k != Kind.Missed) moodCost[Mathf.Clamp(Mood, 0, 2)] += amount;
        }

        void Fail(string reason, string text)
        {
            FailReason = reason;
            FailText = text;
            FailTip = WorstTip();
            Go(Phase.TurnAway);
        }

        /// <summary>The tip for the single worst mistake (the kind that cost the most), or null.</summary>
        string WorstTip()
        {
            float early = C(Kind.Early) * 20f, late = C(Kind.Missed) * 20f;
            string lateTip = $"다음엔: '{Def.biteText}' 하면 바로 챔질!";
            if (Def.failTips != null && Def.failTips.Length >= 4)
            {
                // the rollout legends: by the mood the mistakes were made in (Docs/legends_rollout.md 1.1)
                float best0 = Mathf.Max(Mathf.Max(moodCost[0], moodCost[1]), Mathf.Max(Mathf.Max(moodCost[2], early), late));
                if (best0 <= 0.5f) return null;
                if (best0 == late) return lateTip;
                if (best0 == early) return Def.failTips[3];
                if (best0 == moodCost[0]) return Def.failTips[0];
                if (best0 == moodCost[1]) return Def.failTips[1];
                return Def.failTips[2];
            }
            // grouped by what the player should do differently
            float wary = C(Kind.Fast) + C(Kind.WaryFlick), strong = C(Kind.Strong) + C(Kind.Hurry),
                still = C(Kind.ExcitedFlick) + C(Kind.ExcitedMove);
            float best = Mathf.Max(Mathf.Max(wary, strong), Mathf.Max(Mathf.Max(still, early), late));
            if (best <= 0.5f) return null;
            if (best == late) return lateTip;
            if (best == early) return "다음엔: 입을 벌리기 전엔 챔질 참기";
            if (best == wary) return "다음엔: 경계할 땐 아주 천천히!";
            if (best == strong) return "다음엔: 너무 세게 당기지 않기";
            return "다음엔: 흥분하면 멈추기!";
        }

        float C(Kind k) => cost.TryGetValue(k, out float c) ? c : 0f;

        /// <summary>The word with its subject particle (이 / 가, by whether its last syllable has a final consonant).</summary>
        public static string Ga(string w)
        {
            if (string.IsNullOrEmpty(w)) return w;
            char c = w[w.Length - 1];
            bool batchim = c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0;
            return w + (batchim ? "이" : "가");
        }

        public static string MoodName(int m) => m == 2 ? "흥분" : m == 1 ? "호기심" : "경계";
    }
}
