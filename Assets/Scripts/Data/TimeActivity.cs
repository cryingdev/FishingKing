using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// How active each species is by the time of day (Docs/time_currents_spec.md 6): a per period (새벽 · 낮 · 저녁 · 밤).
    /// It scales how often the species is stocked (FishSpawner) and, as sqrt(a), how readily it comes to the bait; a = 0
    /// never stocks it (쏘가리, 메기 and 모캐 only come at night). For a legend it scales its encounter meter's fill rate.
    /// The values are each species file's "activity" (FishSpecies.activity, Docs/data_reference.md 2.4). Unknown ids: 1
    /// in every period.
    /// </summary>
    public static class TimeActivity
    {
        /// <summary>The species' activity in one period.</summary>
        public static float A(string speciesId, Period p) =>
            GameDatabase.GetFish(speciesId)?.activity is { Length: 4 } a ? a[(int)p] : 1f;

        /// <summary>The activity now: during a cross-fade the two periods' values blended.</summary>
        public static float A(string speciesId, PeriodBlend b) =>
            Mathf.Lerp(A(speciesId, b.From), A(speciesId, b.To), b.F);

        public static float Now(string speciesId) => A(speciesId, GameClock.Look);

        /// <summary>Only comes at night (a = 0 in every other period).</summary>
        public static bool NightOnly(string speciesId) =>
            A(speciesId, Period.Night) > 0f && A(speciesId, Period.Dawn) <= 0f && A(speciesId, Period.Day) <= 0f && A(speciesId, Period.Evening) <= 0f;

        /// <summary>
        /// The encyclopedia's line (spec 6.3): "활동: 새벽 · 저녁" (the periods with a >= 1.3), "밤에만" (night-only) or
        /// "하루 종일" (every a between 0.8 and 1.2); otherwise the period(s) it is most active in.
        /// </summary>
        public static string Describe(string speciesId)
        {
            if (NightOnly(speciesId)) return "밤에만";
            var hi = new List<string>();
            bool flat = true;
            float max = 0f;
            for (int i = 0; i < 4; i++)
            {
                float a = A(speciesId, (Period)i);
                if (a >= 1.3f) hi.Add(GameClock.Name((Period)i));
                if (a < 0.8f || a > 1.2f) flat = false;
                max = Mathf.Max(max, a);
            }
            if (hi.Count > 0) return "활동: " + string.Join(" · ", hi);
            if (flat) return "하루 종일";
            for (int i = 0; i < 4; i++)
                if (A(speciesId, (Period)i) >= max - 1e-4f) hi.Add(GameClock.Name((Period)i));
            return "활동: " + string.Join(" · ", hi);
        }
    }
}
