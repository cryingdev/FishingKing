using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// How active each species is by the time of day (Docs/time_currents_spec.md 6): a per period (새벽 · 낮 · 저녁 · 밤).
    /// It scales how often the species is stocked (FishSpawner) and, as sqrt(a), how readily it comes to the bait; a = 0
    /// never stocks it (쏘가리, 메기 and 모캐 only come at night). For a legend it scales its encounter meter's fill rate.
    /// Unknown ids: 1 in every period.
    /// </summary>
    public static class TimeActivity
    {
        static readonly Dictionary<string, float[]> table = new Dictionary<string, float[]>
        {
            // lake
            { "crucian_carp", new[] { 1.3f, 0.8f, 1.3f, 0.9f } },
            { "bluegill", new[] { 1.0f, 1.3f, 1.0f, 0.4f } },
            { "carp", new[] { 1.2f, 0.7f, 1.2f, 1.4f } },
            { "largemouth_bass", new[] { 1.5f, 0.6f, 1.5f, 0.7f } },
            { "golden_carp", new[] { 1.4f, 0.7f, 1.4f, 1.0f } },
            // stream
            { "pale_chub", new[] { 1.0f, 1.3f, 1.0f, 0.3f } },
            { "cherry_salmon", new[] { 1.5f, 0.8f, 1.4f, 0.4f } },
            { "rainbow_trout", new[] { 1.4f, 0.8f, 1.4f, 0.6f } },
            { "mandarin_fish", new[] { 0f, 0f, 0f, 1.8f } },
            { "lenok", new[] { 1.6f, 0.5f, 1.6f, 0.8f } },
            // sea
            { "horse_mackerel", new[] { 1.3f, 1.0f, 1.3f, 1.1f } },
            { "mackerel", new[] { 1.5f, 1.1f, 1.4f, 0.4f } },
            { "rockfish", new[] { 1.0f, 0.6f, 1.2f, 1.6f } },
            { "flounder", new[] { 1.3f, 0.9f, 1.2f, 0.8f } },
            { "black_porgy", new[] { 1.4f, 0.6f, 1.3f, 1.3f } },
            { "red_seabream", new[] { 1.6f, 0.9f, 1.3f, 0.5f } },
            // swamp
            { "piranha", new[] { 0.9f, 1.4f, 1.0f, 0.5f } },
            { "catfish", new[] { 0f, 0f, 0f, 2.0f } },
            { "snakehead", new[] { 1.5f, 0.8f, 1.5f, 0.6f } },
            { "arowana", new[] { 1.2f, 1.3f, 1.0f, 0.3f } },
            { "arapaima", new[] { 1.3f, 1.1f, 1.0f, 0.6f } },
            // ice
            { "smelt", new[] { 1.3f, 1.2f, 1.0f, 0.5f } },
            { "burbot", new[] { 0f, 0f, 0f, 2.0f } },
            { "northern_pike", new[] { 1.4f, 1.0f, 1.3f, 0.3f } },
            { "arctic_char", new[] { 1.3f, 0.7f, 1.3f, 0.9f } },
            { "sturgeon", new[] { 0.8f, 0.6f, 1.0f, 1.6f } },
            // ocean
            { "yellowtail", new[] { 1.5f, 1.0f, 1.2f, 0.4f } },
            { "mahi_mahi", new[] { 1.1f, 1.4f, 1.0f, 0.3f } },
            { "bluefin_tuna", new[] { 1.6f, 0.8f, 1.3f, 0.5f } },
            { "ocean_sunfish", new[] { 0.6f, 1.8f, 0.6f, 0f } },
            { "blue_marlin", new[] { 1.3f, 1.2f, 0.9f, 0.4f } },
            { "great_white", new[] { 1.1f, 0.6f, 1.4f, 1.5f } },
            // cave
            { "cave_tetra", new[] { 1.0f, 1.0f, 1.0f, 1.0f } },
            { "crystal_koi", new[] { 1.0f, 1.3f, 1.0f, 0.8f } },
            { "anglerfish", new[] { 1.0f, 0.8f, 1.0f, 1.4f } },
            { "coelacanth", new[] { 1.0f, 0.8f, 1.0f, 1.4f } },
        };

        /// <summary>The species' activity in one period.</summary>
        public static float A(string speciesId, Period p) =>
            speciesId != null && table.TryGetValue(speciesId, out var a) ? a[(int)p] : 1f;

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
