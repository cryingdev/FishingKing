using System;
using UnityEngine;

namespace FishingKing
{
    /// <summary>The four periods of the game day (Docs/time_currents_spec.md 2).</summary>
    public enum Period { Dawn, Day, Evening, Night }

    /// <summary>
    /// Where the clock stands between two periods' looks: <see cref="From"/> / <see cref="To"/> and the cross-fade
    /// <see cref="F"/> (0 = all From). Outside a cross-fade From == To and F == 0.
    /// </summary>
    public struct PeriodBlend
    {
        public Period From, To;
        public float F;
        public Period Dominant => F < 0.5f ? From : To;

        /// <summary>How much of this period's look is in the blend (0..1).</summary>
        public float Weight(Period p) => (From == p ? 1f - F : 0f) + (To == p ? F : 0f);

        public bool Same(PeriodBlend o) => From == o.From && To == o.To && Mathf.Abs(F - o.F) < 1e-5f;
        public override string ToString() => From == To ? GameClock.Id(From) : $"{GameClock.Id(From)}>{GameClock.Id(To)} {F:0.00}";
    }

    public enum TideKind { Flood, Ebb, High, Low }

    /// <summary>The sea's tide at a moment (spec 7.1): level h (-1 low .. +1 high), rate r (+1 peak flood, -1 peak ebb), flow s = |r|^0.8.</summary>
    public struct TideState
    {
        public float H, R, S;
        public TideKind Kind;
        public string Word => GameClock.TideWord(Kind);
    }

    /// <summary>
    /// The game clock (Docs/time_currents_spec.md 1): runs only while a fishing stage is open (the controller ticks it:
    /// not on the catch card, not under a dialog, not in the background), 1 real second = <see cref="Scale"/> game
    /// minutes (1 by default: one game day = 24 real minutes). Stored in the save (clockMin / clockDay). Also the sea's
    /// tide, which follows it (semi-diurnal, low water 03:30 / 15:30, high water 09:30 / 21:30).
    /// </summary>
    public static class GameClock
    {
        public const float DayMin = 1440f;
        /// <summary>Period boundaries in game minutes: 05:00 dawn, 08:00 day, 17:00 evening, 20:00 night.</summary>
        static readonly float[] Bounds = { 300f, 480f, 1020f, 1200f };
        const float HalfFade = 15f;

        /// <summary>Game minutes per real second (-fktimescale; 0 = frozen).</summary>
        public static float Scale = 1f;
        /// <summary>True while the controller ticks it this frame (for the HUD / tests).</summary>
        public static bool Running { get; private set; }
        /// <summary>-fktide: a fixed tide phase 0..1 (0 low water, 0.25 peak flood, 0.5 high water, 0.75 peak ebb), or null.</summary>
        public static float? TidePhase;
        /// <summary>-fkclocklog: a [CLOCK] line once a real second.</summary>
        public static bool Log;

        /// <summary>Raised when a cross-fade passes its middle (the new period begins), with the new period.</summary>
        public static event Action<Period> PeriodBegan;

        static float saveAcc;

        static SaveData D => Game.I != null ? Game.Data : null;

        /// <summary>Game minutes since 00:00 (0 &lt;= Min &lt; 1440).</summary>
        public static float Min
        {
            get => D != null ? D.clockMin : 600f;
            set { if (D != null) D.clockMin = Wrap(value); }
        }

        public static int Day
        {
            get => D != null ? D.clockDay : 1;
            set { if (D != null) D.clockDay = Mathf.Max(1, value); }
        }

        public static float Wrap(float m)
        {
            m %= DayMin;
            if (m < 0f) m += DayMin;
            return m;
        }

        public static Period Now => PeriodAt(Min);
        public static PeriodBlend Look => BlendAt(Min);
        public static TideState Tide => TideAt(Min);

        public static Period PeriodAt(float m)
        {
            m = Wrap(m);
            if (m >= 300f && m < 480f) return Period.Dawn;
            if (m >= 480f && m < 1020f) return Period.Day;
            if (m >= 1020f && m < 1200f) return Period.Evening;
            return Period.Night;
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The look blend at clock minute <paramref name="m"/>: a 30-minute cross-fade centred on each boundary.</summary>
        public static PeriodBlend BlendAt(float m)
        {
            m = Wrap(m);
            foreach (float b in Bounds)
            {
                float d = m - b;
                if (d > DayMin * 0.5f) d -= DayMin;
                if (d < -DayMin * 0.5f) d += DayMin;
                if (Mathf.Abs(d) < HalfFade)
                    return new PeriodBlend { From = PeriodAt(b - 1f), To = PeriodAt(b + 1f), F = Smooth((d + HalfFade) / (2f * HalfFade)) };
            }
            var p = PeriodAt(m);
            return new PeriodBlend { From = p, To = p, F = 0f };
        }

        /// <summary>
        /// One frame of running (the controller calls it only while it may run): advances the clock, raises
        /// <see cref="PeriodBegan"/> at a boundary and saves every 30 real seconds of running.
        /// </summary>
        public static void Tick(float realDt)
        {
            Running = true;
            if (D == null) return;
            realDt = Mathf.Clamp(realDt, 0f, 0.1f);
            float prev = Min, next = prev + realDt * Scale;
            if (next >= DayMin)
            {
                next -= DayMin;
                Day = Day + 1;
            }
            D.clockMin = next;
            if (realDt * Scale > 0f)
                foreach (float b in Bounds)
                    if (Crossed(prev, prev + realDt * Scale, b)) PeriodBegan?.Invoke(PeriodAt(b + 1f));
            saveAcc += realDt;
            if (saveAcc >= 30f)
            {
                saveAcc = 0f;
                Game.I.Save();
            }
            if (Log) LogLine(realDt);
        }

        /// <summary>The clock was not ticked this frame (the HUD's "running" flag).</summary>
        public static void Stopped() => Running = false;

        static bool Crossed(float a, float b, float mark)
        {
            // (a < b, b may run past midnight)
            if (a < mark && b >= mark) return true;
            return b >= DayMin && mark + DayMin > a && mark + DayMin <= b;
        }

        /// <summary>Sets the clock (tests); a jump across a boundary raises <see cref="PeriodBegan"/> for the new period.</summary>
        public static void Set(float m, bool announce = false)
        {
            var before = Now;
            Min = m;
            if (announce && Now != before) PeriodBegan?.Invoke(Now);
        }

        // ------------------------------------------------------------------ the tide (sea)
        public static TideState TideAt(float m)
        {
            float phi = TidePhase.HasValue ? 2f * Mathf.PI * TidePhase.Value : 2f * Mathf.PI * (m / 60f - 3.5f) / 12f;
            var t = new TideState { H = -Mathf.Cos(phi), R = Mathf.Sin(phi) };
            t.S = Mathf.Pow(Mathf.Abs(t.R), 0.8f);
            t.Kind = t.R >= 0.26f ? TideKind.Flood : t.R <= -0.26f ? TideKind.Ebb : t.H > 0f ? TideKind.High : TideKind.Low;
            return t;
        }

        public static string TideWord(TideKind k) => k switch
        {
            TideKind.Flood => "들물",
            TideKind.Ebb => "날물",
            TideKind.High => "만조",
            _ => "간조",
        };

        // ------------------------------------------------------------------ names, colours
        public static string Id(Period p) => p switch { Period.Dawn => "dawn", Period.Day => "day", Period.Evening => "evening", _ => "night" };
        public static string Name(Period p) => p switch { Period.Dawn => "새벽", Period.Day => "낮", Period.Evening => "저녁", _ => "밤" };
        /// <summary>The period's name with its subject particle (새벽이 / 낮이 / 저녁이 / 밤이).</summary>
        public static string NameI(Period p) => Name(p) + "이";
        public static Color TextColor(Period p) => Art.Hex(p switch { Period.Dawn => "#f8c8d0", Period.Day => "#fff4d0", Period.Evening => "#ffc890", _ => "#b8c8ff" });
        /// <summary>The toast when a period begins on a stage.</summary>
        public static string BeginText(Period p) => p switch
        {
            Period.Dawn => "새벽이 밝아와요",
            Period.Day => "해가 높이 떴어요",
            Period.Evening => "노을이 지기 시작해요",
            _ => "밤이 되었어요",
        };
        /// <summary>The centre of a period (tests): 06:30, 12:30, 18:30, 00:30.</summary>
        public static float Centre(Period p) => p switch { Period.Dawn => 390f, Period.Day => 750f, Period.Evening => 1110f, _ => 30f };

        public static bool TryParse(string s, out Period p)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "dawn": p = Period.Dawn; return true;
                case "day": p = Period.Day; return true;
                case "evening": p = Period.Evening; return true;
                case "night": p = Period.Night; return true;
            }
            p = Period.Day;
            return false;
        }

        /// <summary>HH:MM floored to 10 game minutes (the HUD ticks every 10 real seconds).</summary>
        public static string HHMM(float m, bool floor10 = true)
        {
            int mm = Mathf.FloorToInt(Wrap(m));
            if (floor10) mm = mm / 10 * 10;
            return $"{mm / 60:00}:{mm % 60:00}";
        }

        /// <summary>"새벽 05:40".</summary>
        public static string Label(float m) => $"{Name(PeriodAt(m))} {HHMM(m)}";

        // ------------------------------------------------------------------ test switches
        static float logT;

        static void LogLine(float dt)
        {
            logT += dt;
            if (logT < 1f) return;
            logT = 0f;
            var tide = Tide;
            var sv = UnityEngine.Object.FindAnyObjectByType<StageView>();
            string cur = "-";
            if (sv != null && sv.Current != null)
            {
                var ctl = UnityEngine.Object.FindAnyObjectByType<FishingController>();
                Vector3 at = ctl != null && ctl.Tackle != null && ctl.Tackle.State == Tackle.Mode.Water ? ctl.Tackle.Surface : new Vector3(0f, 0f, 15f);
                var w = sv.Current.Water(at.x, at.z) + sv.Current.Wind;
                cur = string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:+0.00;-0.00},{1:+0.00;-0.00}) {2:0.00} m/s bow {3:0.0}",
                    w.x, w.y, w.magnitude, ctl != null && ctl.Tackle != null ? ctl.Tackle.Bow : 0f);
            }
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[CLOCK] {0} {1} f {2:0.00} tide {3} r {4:+0.00;-0.00} h {5:+0.00;-0.00} cur {6}",
                HHMM(Min, false), Id(Look.Dominant), Look.F, Id(tide.Kind), tide.R, tide.H, cur));
        }

        static string Id(TideKind k) => k.ToString().ToLowerInvariant();

        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool Flag(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;

        static bool F(string s, out float v) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        /// <summary>
        /// Test switches (after the save has loaded): -fktime hh:mm, -fkperiod dawn|day|evening|night, -fktimescale x,
        /// -fktide low|flood|high|ebb|0..1, -fkcurrent x, -fkgust, -fkcurrentvivid, -fkclocklog.
        /// </summary>
        public static void DebugBoot()
        {
            string t = Arg("-fktime");
            if (!string.IsNullOrEmpty(t))
            {
                var p = t.Split(':');
                if (p.Length >= 1 && int.TryParse(p[0], out int hh))
                {
                    int mm = p.Length > 1 && int.TryParse(p[1], out int x) ? x : 0;
                    Min = hh * 60 + mm;
                }
            }
            if (TryParse(Arg("-fkperiod"), out var per)) Min = Centre(per);
            if (F(Arg("-fktimescale"), out float sc)) Scale = Mathf.Max(0f, sc);
            string tide = Arg("-fktide");
            if (!string.IsNullOrEmpty(tide))
            {
                switch (tide.ToLowerInvariant())
                {
                    case "low": TidePhase = 0f; break;
                    case "flood": TidePhase = 0.25f; break;
                    case "high": TidePhase = 0.5f; break;
                    case "ebb": TidePhase = 0.75f; break;
                    default: if (F(tide, out float ph)) TidePhase = Mathf.Repeat(ph, 1f); break;
                }
            }
            if (F(Arg("-fkcurrent"), out float cm)) CurrentField.Mult = Mathf.Max(0f, cm);
            CurrentField.DebugGust = Flag("-fkgust");
            CurrentField.Vivid = Flag("-fkcurrentvivid");
            Log = Flag("-fkclocklog");
            if (t != null || Arg("-fkperiod") != null || Arg("-fktimescale") != null || tide != null)
                Debug.Log($"[CLOCK] boot {HHMM(Min, false)} day {Day} scale {Scale} tide {(TidePhase.HasValue ? TidePhase.Value.ToString("0.00") : "clock")}");
        }
    }
}
