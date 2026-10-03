using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The graded hook set (챔질): a quick swipe UP the screen in a bite (the rod snapped up; the 톡 is the downward pull)
    /// sets the hook with the swipe's strength. Its timing (against the float's deepest pull, <see cref="StrikePeak"/> s into
    /// the bite) and its strength (against the hook's ideal, <see cref="HookDef.setStrength"/>; a lure's own hooks
    /// <see cref="LureSetStrength"/>) grade it BAD / GOOD / GREAT / PERFECT: the fight starts with the fish's stamina cut
    /// and the hook's hold scaled by the grade, and the line jerks tight with the strength (never past
    /// <see cref="StrikeTensionMax"/> of what it holds: the jerk alone does not part it). A tap or winding still sets the
    /// hook as before, ungraded.
    /// </summary>
    public partial class FishingController
    {
        public enum StrikeGrade { None, Bad, Good, Great, Perfect }

        /// <summary>The best moment (s into the bite: the float at its deepest) and the timing error (s) that is a full miss.</summary>
        public const float StrikePeak = 0.25f, StrikeTimeSpan = 0.35f;
        /// <summary>The strength error that is a full miss.</summary>
        public const float StrikeStrengthSpan = 0.4f;
        /// <summary>The ideal strength for a lure (its own hooks).</summary>
        public const float LureSetStrength = 0.6f;
        /// <summary>The grade's limits on the worse of the two errors (each 0 = spot on, 1 = a full miss).</summary>
        public const float PerfectErr = 0.2f, GreatErr = 0.45f, GoodErr = 0.75f;
        /// <summary>Stamina taken off at the fight's start, and the hold's factor, by grade (None, Bad, Good, Great, Perfect).</summary>
        public static readonly float[] StrikeStamina = { 0f, 0f, 0.12f, 0.25f, 0.35f };
        public static readonly float[] StrikeHold = { 1f, 0.8f, 1.1f, 1.25f, 1.4f };
        /// <summary>A full-strength strike jerks the line to this share of what it holds ...</summary>
        public const float StrikeTensionFull = 0.8f;
        /// <summary>... never past this share (the jerk alone does not break it).</summary>
        public const float StrikeTensionMax = 0.85f;

        /// <summary>The last graded strike (for the HUD and the tests): its grade, strength, time into the bite.</summary>
        public StrikeGrade LastStrike { get; private set; }
        public float LastStrikeStrength { get; private set; }
        public float LastStrikeAt { get; private set; }
        /// <summary>Seconds since the bite began.</summary>
        float biteAge;
        StrikeGrade pendingGrade;
        float pendingStrength;

        /// <summary>The grade of a strike <paramref name="at"/> s into the bite with <paramref name="strength"/>, the ideal <paramref name="ideal"/>.</summary>
        public static StrikeGrade GradeStrike(float at, float strength, float ideal)
        {
            float e = Mathf.Max(Mathf.Abs(at - StrikePeak) / StrikeTimeSpan, Mathf.Abs(strength - ideal) / StrikeStrengthSpan);
            return e <= PerfectErr ? StrikeGrade.Perfect : e <= GreatErr ? StrikeGrade.Great : e <= GoodErr ? StrikeGrade.Good : StrikeGrade.Bad;
        }

        public static string StrikeLabel(StrikeGrade g) =>
            g == StrikeGrade.Perfect ? "PERFECT!" : g == StrikeGrade.Great ? "GREAT!" : g == StrikeGrade.Good ? "GOOD" : g == StrikeGrade.Bad ? "BAD" : "";

        /// <summary>The ideal strike strength for the rig now: the hook's under a natural bait, a lure's own.</summary>
        public float IdealStrike => RigHook != null ? RigHook.setStrength : LureSetStrength;

        /// <summary>A graded strike now (the swipe's strength): graded and kept for the fight about to begin.</summary>
        void GradeNow(float strength)
        {
            pendingStrength = Mathf.Clamp01(strength);
            pendingGrade = GradeStrike(biteAge, pendingStrength, IdealStrike);
            LastStrike = pendingGrade;
            LastStrikeStrength = pendingStrength;
            LastStrikeAt = biteAge;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[STRIKE] {0} at {1:0.00} s (peak {2:0.00}) strength {3:0.00} (ideal {4:0.00}, {5})",
                pendingGrade, biteAge, StrikePeak, pendingStrength, IdealStrike, RigHook != null ? RigHook.id : "lure"));
        }

        /// <summary>The hold factor for the fight about to begin (1 ungraded).</summary>
        float PendingHold => StrikeHold[(int)pendingGrade];

        /// <summary>The fight just built: the grade's stamina cut and the line's jerk; the strike consumed.</summary>
        void ApplyStrike()
        {
            if (pendingGrade == StrikeGrade.None || Fight == null) return;
            Fight.Stamina = Mathf.Max(0f, Fight.Stamina - StrikeStamina[(int)pendingGrade]);
            float jerk = Mathf.Min(StrikeTensionMax, StrikeTensionFull * pendingStrength) * Fight.LineLimit;
            Fight.Tension = Mathf.Max(Fight.Tension, jerk);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[STRIKE] fight starts: stamina {0:0.00}, tension {1:0.00} of {2:0.00} kgf",
                Fight.Stamina, Fight.Tension, Fight.LineLimit));
            pendingGrade = StrikeGrade.None;
            pendingStrength = 0f;
        }
    }
}
