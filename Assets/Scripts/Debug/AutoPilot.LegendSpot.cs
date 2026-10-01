using System.Collections;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto legendspot (Docs/lures_legend_spec.md 2.2.1), with -fkencounter natural on a stage with a legend (default
    /// the lake's golden carp): the legend's blinking spot and the cast it asks for.
    /// <list type="number">
    /// <item>The first spot: inside the home view, on open water, within his cast; shot at both blink phases
    /// (legspot_1_blink_on / _2_blink_off); left alone it times out after its window: no encounter, a miss.</item>
    /// <item>Late: a cast into where it was, after it went: no claim, no build-up, no encounter.</item>
    /// <item>The retry: the next spot comes spotRetry s after the miss; the rig lying in it from before does not claim it.</item>
    /// <item>Outside: a cast beside it (radius + 2.5 m): a miss at once (legspot_4_miss), no encounter.</item>
    /// <item>A wrong rig (a bait no legend here wants) into it: a miss, no encounter.</item>
    /// <item>The key into it in time: claimed (legspot_3_land at the landing) and the encounter starts; left to turn away,
    /// it ends back in Waiting. No cooldown or pity was spent by the misses.</item>
    /// </list>
    /// "[SPOT] CHECK" lines, then "legend spot test done: N failed".
    /// </summary>
    public partial class AutoPilot
    {
        int spotFails;

        void SpotCheck(string what, bool ok)
        {
            if (!ok) spotFails++;
            Log($"[SPOT] CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        void LogSpotDraw(FishingController ctl, string what)
        {
            var w = ctl.Watch;
            var fx = ctl.Stage.Water;
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[SPOT] {0}: blink {1} at {2:0.00}s, drawn {3} ({4} runs), spot px {5}",
                what, w.SpotBlinkOn ? "on" : "off", w.SpotT, fx != null && fx.SpotDrawnOn ? "on" : "off", fx != null ? fx.SpotDrawnSegs : -1, w.Spot2D));
        }

        IEnumerator LegendSpotTest()
        {
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Watch == null)
            {
                Log("[SPOT] CHECK FAIL no fishing stage with a legend (use -fkscene Fishing -fkstage lake -fkencounter natural)");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            var w = ctl.Watch;
            encKey = Game.I.Bait;
            encId = LegendWatch.DebugLegend ?? w.Legend.id;
            var sp = GameDatabase.GetFish(encId);
            var rec = Game.I.Legend(encId);
            int pity0 = rec.pity;
            var water = ctl.Stage.Water;
            var L = ctl.Stage.L;
            Log($"legend spot test on {ctl.Stage.Def.id}: legend {encId}, key {encKey.id}, debug {LegendWatch.DebugMode ?? "off"}, " +
                $"window {sp.encounter.spotWindow:0}s radius {sp.encounter.spotRadius:0.0}m retry {sp.encounter.spotRetry:0}s, rod {Game.I.Rod.id} ({Game.I.Rod.castDist:0} m)");
            EncEquip(ctl);

            // ---- 1. the first spot: where it is, its blink, left alone
            yield return WaitSpot(ctl, 40f, 6f);
            SpotCheck($"a spot came up (offers {w.SpotOffers})", w.SpotOn);
            if (!w.SpotOn)
            {
                Log($"legend spot test done: {spotFails} failed");
                Application.Quit();
                yield break;
            }
            var s = w.Spot;
            float r = w.SpotRadius;
            float reach = new Vector2(s.x - ctl.Angler.X, s.z).magnitude;
            bool open = water == null || (water.OpenWater(s.x, s.z) && water.OpenWater(s.x - r, s.z) && water.OpenWater(s.x + r, s.z));
            SpotCheck(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "spot inside the home view on open water ({0:0.00}, {1:0.00}) r {2:0.0}: open {3}, {4:0.0} m from him (cast {5:0}), depth {6:0.0} m, {7:0.0} m from the lurk point",
                s.x, s.z, r, open, reach, Game.I.Rod.castDist, L.DepthAt(s.z), new Vector2(s.x - w.Lurk.x, s.z - w.Lurk.z).magnitude),
                open && reach <= Game.I.Rod.castDist && s.z > L.zNear);
            // (the water effects draw a frame behind the watch: wait for the drawn phase, a few frames into it)
            for (float t = 0f; t < 3f && !(water != null && water.SpotDrawnOn && w.SpotT > 0.5f); t += Time.deltaTime) yield return null;
            for (int f = 0; f < 3; f++) yield return null;
            LogSpotDraw(ctl, "blink_on");
            yield return Shot("legspot_1_blink_on");
            for (float t = 0f; t < 3f && water != null && water.SpotDrawnOn; t += Time.deltaTime) yield return null;
            for (int f = 0; f < 3; f++) yield return null;
            yield return null;
            LogSpotDraw(ctl, "blink_off");
            yield return Shot("legspot_2_blink_off");
            float offerAt = Time.time - w.SpotT;
            while (w.SpotOn && Time.time - offerAt < 40f)
            {
                if (ctl.State == FishingController.S.Encounter) break;
                yield return null;
            }
            float lasted = Time.time - offerAt;
            SpotCheck($"ignored spot timed out after {lasted:0.0}s (window {w.SpotWindow:0}s): end '{w.SpotEnd}', misses {w.SpotMisses}, state {ctl.State}",
                w.SpotEnd == "timeout" && Mathf.Abs(lasted - w.SpotWindow) <= 0.6f && ctl.State != FishingController.S.Encounter);
            float missAt = w.SpotEndedAt;

            // ---- 2. late: into where it was, after it went
            yield return CastAt(ctl, s);
            yield return new WaitForSeconds(0.3f);
            bool late = !w.SpotClaimed && !w.SpotOn;
            float tEnc = 0f;
            for (; tEnc < 4f && ctl.State == FishingController.S.Waiting && !w.SpotOn; tEnc += Time.deltaTime) yield return null;
            SpotCheck($"a late cast into the old spot claims nothing (claimed {w.SpotClaimed}, blocked '{w.Blocked}', meter {w.Meter:0.00}, state {ctl.State})",
                late && ctl.State == FishingController.S.Waiting && w.Meter <= 0.001f);

            // ---- 3. the retry, and the rig already lying there
            yield return WaitSpot(ctl, 20f, 1f);
            float retry = Time.time - w.SpotT - missAt;
            SpotCheck($"the next spot came {retry:0.0}s after the miss (spotRetry {sp.encounter.spotRetry:0}s)", w.SpotOn && Mathf.Abs(retry - sp.encounter.spotRetry) <= 0.6f);
            float lieD = new Vector2(ctl.Tackle.Surface.x - w.Spot.x, ctl.Tackle.Surface.z - w.Spot.z).magnitude;
            for (float t = 0f; t < 4f && w.SpotOn; t += Time.deltaTime) yield return null;
            SpotCheck($"the rig already lying in the water ({lieD:0.0} m from the new spot) does not claim it (claimed {w.SpotClaimed}, meter {w.Meter:0.00}, state {ctl.State})",
                // (an ordinary fish may bite the lying bait meanwhile: only no claim and no encounter matter)
                !w.SpotClaimed && w.Meter <= 0.001f && ctl.State != FishingController.S.Encounter);

            // ---- 4. outside: beside it
            yield return BackToReady(ctl);
            yield return WaitSpot(ctl, 40f);
            s = w.Spot;
            r = w.SpotRadius;
            var beside = s + new Vector3(r + 2.5f, 0f, 0f);
            if (water != null && !water.OpenWater(beside.x, beside.z)) beside = s - new Vector3(r + 2.5f, 0f, 0f);
            if (water != null && !water.OpenWater(beside.x, beside.z)) beside = s - new Vector3(0f, 0f, r + 2.5f);
            int miss0 = w.SpotMisses;
            yield return CastAt(ctl, beside);
            for (float t = 0f; t < 0.25f; t += Time.deltaTime) yield return null;
            yield return Shot("legspot_4_miss");
            float landD = new Vector2(ctl.Tackle.Surface.x - s.x, ctl.Tackle.Surface.z - s.z).magnitude;
            for (float t = 0f; t < 4f && ctl.State == FishingController.S.Waiting; t += Time.deltaTime) yield return null;
            SpotCheck($"a cast {landD:0.0} m from the spot (radius {r:0.0}) misses it: end '{w.SpotEnd}', misses {miss0} -> {w.SpotMisses}, claimed {w.SpotClaimed}, state {ctl.State}",
                w.SpotEnd == "outside" && w.SpotMisses == miss0 + 1 && !w.SpotClaimed && ctl.State == FishingController.S.Waiting);

            // ---- 5. a wrong rig into it
            yield return BackToReady(ctl);
            var wrong = new[] { "bait_paste", "bait_worm", "bait_shrimp", "bait_spinner", "bait_minnow" }
                .Select(id => GameDatabase.GetItem<BaitDef>(id))
                .FirstOrDefault(b => b != null && !w.Legends.Any(f => f.encounter.KeyWeight(b.id) > 0f));
            if (wrong != null)
            {
                if (wrong.isLure) { if (!Game.I.Owns(wrong.id)) Game.Data.ownedItems.Add(wrong.id); }
                else if (!wrong.infinite && Game.I.BaitCount(wrong.id) < 5) Game.I.AddBait(wrong.id, 10);
                ctl.EquipBait(wrong);
                yield return WaitSpot(ctl, 40f);
                miss0 = w.SpotMisses;
                yield return CastAt(ctl, w.Spot);
                landD = new Vector2(ctl.Tackle.Surface.x - w.Spot.x, ctl.Tackle.Surface.z - w.Spot.z).magnitude;
                for (float t = 0f; t < 4f && ctl.State == FishingController.S.Waiting; t += Time.deltaTime) yield return null;
                SpotCheck($"a wrong rig ({wrong.id}) {landD:0.0} m from the spot misses it: end '{w.SpotEnd}', misses {miss0} -> {w.SpotMisses}, claimed {w.SpotClaimed}, state {ctl.State}",
                    w.SpotEnd == "rig" && w.SpotMisses == miss0 + 1 && !w.SpotClaimed && ctl.State == FishingController.S.Waiting);
            }
            else Log("[SPOT] no wrong bait to try");
            SpotCheck($"the misses spent no cooldown or pity ({encId}: pity {pity0} -> {rec.pity}, {LegendWatch.Remaining(encId)}s away)",
                rec.pity == pity0 && LegendWatch.Remaining(encId) == 0 && !w.AwayOf(sp));

            // ---- 6. the key into it in time: claimed, then the encounter
            yield return BackToReady(ctl);
            EncEquip(ctl);
            yield return WaitSpot(ctl, 40f);
            s = w.Spot;
            float castAtT = w.SpotT;
            yield return CastAt(ctl, s);
            // (the claim's bright ring a moment into its swell, the splash still there)
            for (float t = 0f; t < 0.15f; t += Time.deltaTime) yield return null;
            yield return Shot("legspot_3_land");
            landD = new Vector2(ctl.Tackle.Surface.x - s.x, ctl.Tackle.Surface.z - s.z).magnitude;
            SpotCheck($"the key ({Game.I.Bait.id}) {landD:0.0} m from the spot, thrown {castAtT:0.0}s in, claims it (end '{w.SpotEnd}', claims {w.SpotClaims})",
                w.SpotClaimed && w.SpotEnd == "claim");
            float tw = 0f;
            for (; tw < 30f && ctl.State == FishingController.S.Waiting; tw += Time.deltaTime) yield return null;
            SpotCheck($"the encounter starts {tw:0.0}s after the claim (state {ctl.State}, blocked '{w.Blocked}')", ctl.State == FishingController.S.Encounter);
            if (ctl.State == FishingController.S.Encounter)
            {
                // hands off: it turns away
                PointerInput.SimDown = false;
                for (float t = 0f; t < 60f && ctl.State == FishingController.S.Encounter; t += Time.deltaTime) yield return null;
                SpotCheck($"left alone the encounter ends back in Waiting (state {ctl.State}, end '{w.SpotEnd}')", ctl.State == FishingController.S.Waiting);
            }
            Log($"legend spot test done: {spotFails} failed");
            yield return new WaitForSeconds(0.5f);
            PointerInput.SimActive = false;
            Application.Quit();
        }
    }
}
