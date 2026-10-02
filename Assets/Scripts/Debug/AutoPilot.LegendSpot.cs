using System.Collections;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto legendspot (Docs/lures_legend_spec.md 2.2.1), with -fkencounter natural on a stage with a legend (default
    /// the lake's golden carp): the legend's splashing spot and the cast it asks for.
    /// <list type="number">
    /// <item>The first spot (the natural first cue's look let finish, then one offered now from the home view): the look
    /// at it (<see cref="WatchLook"/>: legspot_look_0_before / _1_peak / _2_home) and its cue's shadow gliding by (legspot_shadow_1 /
    /// _2, mid-glide); inside the home view, on open water,
    /// within his cast; shot at a burst of spray (legspot_1_splash) and in its last seconds, the bursts quicker
    /// (legspot_2_late); left alone it times out after its window: no encounter, a miss.</item>
    /// <item>Late: a cast into where it was, after it went: no claim, no build-up, no encounter.</item>
    /// <item>The retry: the next spot comes spotRetry s after the miss (looked at from the zoomed wait); the rig lying in
    /// it from before does not claim it.</item>
    /// <item>The look cut by a wind-up 0.3 s in (cut within 2 frames, 1x home within ZoomAimOut); with 끔 a spot gets no
    /// look and the view never moves.</item>
    /// <item>Outside: a cast beside it (radius + 2.5 m): a miss at once (legspot_4_miss), no encounter.</item>
    /// <item>A wrong rig (a bait no legend here wants) into it: a miss, no encounter (its spot, the quick retry after the
    /// outside miss, gets no look).</item>
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
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[SPOT] {0}: spray {1} at {2:0.00}s (burst {6}, every {7:0.00}s), drawn {3} droplets ({4} runs), spot px {5}",
                what, w.SpotSplashing ? "up" : "down", w.SpotT, fx != null ? fx.SpotDrawnSpray : -1, fx != null ? fx.SpotDrawnSegs : -1, w.Spot2D, w.SpotBursts, w.SpotBurstGap));
        }

        /// <summary>A cue's shadow gliding by: shot at 30 % and 55 % of its glide (legspot_shadow_1 / _2), drawn and moved between them.</summary>
        IEnumerator ShadeShots(LegendWatch w)
        {
            for (float t = 0f; t < 3f && w.ShadeK < 0f; t += Time.deltaTime) yield return null;
            var pos = new Vector2[2];
            var shown = new bool[2];
            for (int i = 0; i < 2; i++)
            {
                float at = i == 0 ? 0.3f : 0.55f;
                for (float t = 0f; t < 3f && w.ShadeK >= 0f && w.ShadeK < at; t += Time.deltaTime) yield return null;
                shown[i] = w.ShadeShown;
                pos[i] = w.CueShade2D;
                Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[SPOT] shadow {0}: k {1:0.00}, shown {2}, at {3}", i + 1, w.ShadeK, shown[i], SV(pos[i])));
                yield return Shot($"legspot_shadow_{i + 1}");
            }
            float moved = (pos[1] - pos[0]).magnitude * PixelView.PPU;
            SpotCheck(string.Format(System.Globalization.CultureInfo.InvariantCulture, "the cue's shadow is drawn mid-glide ({0}, {1}) and glides {2:0.0} px between the shots", shown[0], shown[1], moved),
                shown[0] && shown[1] && moved >= 2f);
        }

        static string SV(Vector2 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0})", v.x, v.y);

        /// <summary>
        /// Follows one spot look (FishingController.SpotLook) from the frame its spot was offered to its end: it starts
        /// within 0.2 s, reaches the step <paramref name="askedBefore"/> + LookSteps pixel exact with the spot centred (within
        /// 2 px of where the crop can centre it) for its hold, the camera no faster than ViewZoom.MaxCameraSpeed, takes
        /// LookIn + LookHold + at most LookBackMax, and leaves the view home (the ready) / the rod tip and the rig in frame
        /// (waiting), the step back to the mode's. <paramref name="shots"/>: the peak (a burst's spray up) and home shots.
        /// </summary>
        IEnumerator WatchLook(FishingController ctl, string label, int askedBefore, bool shots)
        {
            var z = ZoomNow;
            var w = ctl.Watch;
            var water = ctl.Stage.Water;
            int looks0 = ctl.LookCount;
            for (float t = 0f; t < 0.3f && !ctl.SpotLooking && ctl.LookCount == looks0; t += Time.deltaTime) yield return null;
            bool started = ctl.SpotLooking || ctl.LookCount > looks0;
            SpotCheck($"look_{label}: the look starts {ctl.LookDelay:0.000}s after the spot (<= 0.2; skip '{ctl.LookSkip}' for offer {ctl.LookSkipOffer}, offers {w.SpotOffers})",
                started && ctl.LookDelay <= 0.2f);
            if (!started) yield break;
            float t0 = Time.time - ctl.LookDelay;
            float lookLen = 0f, backLen = 0f, maxSpeed = 0f;
            Vector2 last = z.PanPx;
            float peakErr = 99f, peakRaw = 99f, peakLevel = 0f, centredFor = 0f;
            int peakStep = 0;
            bool peakExact = false, peakShot = !shots;
            while (ctl.SpotLooking)
            {
                float dt = Mathf.Max(1e-4f, Time.deltaTime);
                maxSpeed = Mathf.Max(maxSpeed, (z.PanPx - last).magnitude / dt);
                last = z.PanPx;
                peakErr = (z.PanPx - z.PanFor(w.Spot2D)).magnitude;
                peakRaw = (z.PanPx - z.WorldToPx(w.Spot2D)).magnitude;
                peakLevel = z.Level;
                peakStep = z.StepPx.y;
                peakExact = z.PixelExact;
                if (z.Level >= 1f && peakErr <= 2f) centredFor += Time.deltaTime;
                if (!peakShot && Time.time - t0 >= FishingController.LookIn + 0.4f && w.SpotSplashing && water != null && water.SpotDrawnSpray > 0)
                {
                    peakShot = true;
                    Log($"[SPOT] look peak: {ZDesc(z)}");
                    yield return Shot("legspot_look_1_peak");
                    continue;
                }
                yield return null;
            }
            lookLen = Time.time - t0;
            string why = ctl.LookEnd;
            float tb = Time.time;
            while (ctl.SpotLookBack)
            {
                float dt = Mathf.Max(1e-4f, Time.deltaTime);
                maxSpeed = Mathf.Max(maxSpeed, (z.PanPx - last).magnitude / dt);
                last = z.PanPx;
                yield return null;
            }
            backLen = Time.time - tb;
            float total = Time.time - t0;
            SpotCheck(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "look_{0}: at its peak level {1:0.000} step {2} (asked before {3} + {4}), pixel exact {5}, spot {6:0.0} px from where the crop centres it ({7:0.0} px from the spot itself), centred {8:0.00}s",
                label, peakLevel, peakStep, askedBefore, FishingController.LookSteps, peakExact, peakErr, peakRaw, centredFor),
                peakLevel >= 1f && peakStep == askedBefore + FishingController.LookSteps && peakExact && peakErr <= 2f && centredFor >= 0.8f);
            float lo = FishingController.LookIn + FishingController.LookHold;
            SpotCheck(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "look_{0}: on the spot {1:0.00}s, back {2:0.00}s, total {3:0.00}s (within {4:0.0}..{5:0.0}), ended '{6}', fastest pan {7:0} px/s (max {8:0})",
                label, lookLen, backLen, total, lo - 0.05f, lo + FishingController.LookBackMax + 0.15f, ctl.LookEnd, maxSpeed, ViewZoom.MaxCameraSpeed(z.OverscanPx)),
                total >= lo - 0.05f && total <= lo + FishingController.LookBackMax + 0.15f && ctl.LookEnd == "back" && maxSpeed <= ViewZoom.MaxCameraSpeed(z.OverscanPx));
            var tip = z.WorldToPx(ctl.RodTip2D);
            var crop = z.CropPx;
            bool tipIn = crop.Contains(tip);
            bool home;
            string where;
            if (ctl.State == FishingController.S.Waiting)
            {
                var rig = z.WorldToPx(ctl.RigShown2D);
                home = tipIn && crop.Contains(rig) && z.StepPx.y <= askedBefore && (z.Level <= 0f || z.Level >= 1f);
                where = $"rig {SV(rig)} in {crop.Contains(rig)}";
            }
            else
            {
                home = tipIn && z.Level <= 0f && z.AtHome;
                where = $"at home {z.AtHome}";
            }
            SpotCheck($"look_{label}: after it ({ctl.State}) the rod tip {SV(tip)} in the crop {tipIn}, {where}, step {z.StepPx.y} (asked {z.StepPxAsked}): {ZDesc(z)}", home);
            if (shots) yield return Shot("legspot_look_2_home");
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
            if (Arg("-fkzoommode") != null) Game.Data.zoomMode = (int)ZoomModeArg();
            var mode0 = FishingController.ZoomSetting;
            var z = ZoomNow;
            Log($"[SPOT] zoom mode {mode0}");

            // ---- 1. the first spot: where it is, its splashing, left alone (and the look at it from the ready)
            // (the natural lurk's first cue comes 1.5 s in: its look let finish, then a spot of our own from the home view)
            for (float t = 0f; t < 6f && (ctl.SpotLooking || ctl.SpotLookBack || z.Level > 0f || !z.AtHome); t += Time.deltaTime) yield return null;
            Log($"[SPOT] before the look: {ctl.State}, {ZDesc(z)}");
            yield return Shot("legspot_look_0_before");
            int askedReady = z.StepPxAsked;
            w.DebugSpotNow();
            // (the cue with it: its shadow shot twice mid-glide, alongside the look)
            StartCoroutine(ShadeShots(w));
            yield return WaitSpot(ctl, 40f, 6f);
            if (w.SpotOn && mode0 != ZoomMode.Off) yield return WatchLook(ctl, "ready", askedReady, true);
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
                s.x, s.z, r, open, reach, Game.I.Rod.castDist, L.DepthAt(s.x, s.z), new Vector2(s.x - w.Lurk.x, s.z - w.Lurk.z).magnitude),
                open && reach <= Game.I.Rod.castDist && s.z > L.zNear);
            if (L.Terrain)
            {
                // the generated bed (Docs/terrain_depth_spec.md 10): deep enough over 0.6 x its radius for the legend's rule
                float need = w.Def.depthMin + 0.3f, disc = L.Bathy.MinDepthDisc(s.x, s.z, 0.6f * r);
                SpotCheck(string.Format(System.Globalization.CultureInfo.InvariantCulture, "the bed under the spot: least water {0:0.00} m over 0.6 r (>= {1:0.00}); lurk ({2:0.0}, {3:0.0}) on {4} {5:0.00} m",
                    disc, need, w.Lurk.x, w.Lurk.z, L.Bathy.KindAt(w.Lurk.x, w.Lurk.z), L.DepthAt(w.Lurk.x, w.Lurk.z)), disc >= need);
            }
            // (a new burst, its droplets well up: the water effects draw a frame behind the watch)
            int b0 = w.SpotBursts;
            for (float t = 0f; t < 3f && w.SpotOn && w.SpotBursts == b0; t += Time.deltaTime) yield return null;
            for (float t = 0f; t < 0.18f; t += Time.deltaTime) yield return null;
            LogSpotDraw(ctl, "splash");
            int spray1 = water != null ? water.SpotDrawnSpray : 0;
            float gap1 = w.SpotBurstGap;
            yield return Shot("legspot_1_splash");
            SpotCheck($"a burst of spray is drawn ({spray1} droplets in the air, {(water != null ? water.SpotDrawnSegs : -1)} runs), the bursts {gap1:0.00}s apart",
                spray1 > 0 && Mathf.Abs(gap1 - 1.5f) <= 0.05f);
            // the window's last seconds: the bursts come quicker
            for (float t = 0f; t < 20f && w.SpotOn && w.SpotWindow - w.SpotT > 2.5f; t += Time.deltaTime) yield return null;
            b0 = w.SpotBursts;
            for (float t = 0f; t < 2f && w.SpotOn && w.SpotBursts == b0; t += Time.deltaTime) yield return null;
            for (float t = 0f; t < 0.15f; t += Time.deltaTime) yield return null;
            LogSpotDraw(ctl, "late");
            int spray2 = water != null ? water.SpotDrawnSpray : 0;
            yield return Shot("legspot_2_late");
            float offerAt = Time.time - w.SpotT, gapEnd = w.SpotBurstGap;
            int bursts = w.SpotBursts;
            while (w.SpotOn && Time.time - offerAt < 40f)
            {
                if (ctl.State == FishingController.S.Encounter) break;
                gapEnd = w.SpotBurstGap;
                bursts = w.SpotBursts;
                yield return null;
            }
            SpotCheck($"the bursts quicken at the end: {bursts} in {w.SpotWindow:0}s, the last {gapEnd:0.00}s apart (first {gap1:0.00}s), late shot {spray2} droplets",
                gapEnd < 0.8f && spray2 > 0);
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

            // ---- 3. the retry, and the rig already lying there (and the look at it from the zoomed wait)
            int askedWait = z.StepPxAsked;
            // (no ordinary fish on the lying bait until that look is over: a bite cuts it, as any change of state does, at
            // random; the claim check after it still lets one bite)
            bool noBites0 = FishingController.NoBites;
            FishingController.NoBites = true;
            foreach (var f in ctl.Spawner.Fish)
                if (f.State == FishAgent.St.Approach || f.State == FishAgent.St.Nibble) f.LoseInterest();
            yield return WaitSpot(ctl, 20f, 1f);
            float retry = Time.time - w.SpotT - missAt;
            SpotCheck($"the next spot came {retry:0.0}s after the miss (spotRetry {sp.encounter.spotRetry:0}s)", w.SpotOn && Mathf.Abs(retry - sp.encounter.spotRetry) <= 0.6f);
            float lieD = new Vector2(ctl.Tackle.Surface.x - w.Spot.x, ctl.Tackle.Surface.z - w.Spot.z).magnitude;
            if (w.SpotOn && mode0 != ZoomMode.Off && ctl.State == FishingController.S.Waiting) yield return WatchLook(ctl, "waiting", askedWait, false);
            FishingController.NoBites = noBites0;
            for (float t = 0f; t < 4f && w.SpotOn; t += Time.deltaTime) yield return null;
            SpotCheck($"the rig already lying in the water ({lieD:0.0} m from the new spot) does not claim it (claimed {w.SpotClaimed}, meter {w.Meter:0.00}, state {ctl.State})",
                // (an ordinary fish may bite the lying bait meanwhile: only no claim and no encounter matter)
                !w.SpotClaimed && w.Meter <= 0.001f && ctl.State != FishingController.S.Encounter);

            // ---- 3b. the look cut by a wind-up; with 끔 no look at all
            yield return BackToReady(ctl);
            for (float t = 0f; t < 4f && (ctl.SpotLooking || ctl.SpotLookBack || z.Level > 0f || !z.AtHome); t += Time.deltaTime) yield return null;
            if (mode0 != ZoomMode.Off)
            {
                int looks0 = ctl.LookCount;
                w.DebugSpotNow();
                for (float t = 0f; t < 1f && ctl.LookCount == looks0; t += Time.deltaTime) yield return null;
                for (float t = 0f; t < 0.3f; t += Time.deltaTime) yield return null;
                float lvCut = z.Level;
                bool lookingCut = ctl.SpotLooking;
                // a wind-up starts (pressed on the water)
                PointerInput.SimActive = true;
                PointerInput.SimPos = Scr(0.5f, 0.55f);
                PointerInput.SimDown = true;
                float tPress = Time.time;
                yield return null;
                int cutFrames = 1;
                for (float t = 0f; t < 0.5f && ctl.SpotLooking; t += Time.deltaTime, cutFrames++) yield return null;
                float cutAfter = Time.time - tPress;
                var stCut = ctl.State;
                string endCut = ctl.LookEnd;
                for (float t = 0f; t < 1f && !(z.Level <= 0f && z.AtHome); t += Time.deltaTime) yield return null;
                float homeAfter = Time.time - tPress;
                PointerInput.SimDown = false;
                for (float t = 0f; t < 1f && ctl.State != FishingController.S.Ready; t += Time.deltaTime) yield return null;
                SpotCheck(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "look_windup: a wind-up {0:0.00}s into the look (level {1:0.00}, looking {2}) cuts it {3:0.000}s ({4} frames) after the press ({5}, ended '{6}'), 1x home {7:0.000}s after (<= {8:0.00} + 3 frames); let go: {9}",
                    0.3f, lvCut, lookingCut, cutAfter, cutFrames, stCut, endCut, homeAfter, 0.15f, ctl.State),
                    lookingCut && endCut == "windup" && cutFrames <= 2 && homeAfter <= 0.15f + 3f * Mathf.Max(1f / 60f, Time.smoothDeltaTime) && z.Level <= 0f && z.AtHome);
            }
            // 끔: the spot comes up, the view stays put
            Game.Data.zoomMode = (int)ZoomMode.Off;
            yield return new WaitForSeconds(0.3f);
            {
                int looks0 = ctl.LookCount;
                w.DebugSpotNow();
                for (float t = 0f; t < 1f && !w.SpotOn; t += Time.deltaTime) yield return null;
                float maxLv = 0f;
                bool moved = false;
                for (float t = 0f; t < 1.5f; t += Time.deltaTime)
                {
                    maxLv = Mathf.Max(maxLv, z.Level);
                    moved |= !z.AtHome;
                    yield return null;
                }
                SpotCheck($"look_off: with 끔 the spot (offer {w.SpotOffers}) gets no look (looks {looks0} -> {ctl.LookCount}, skip '{ctl.LookSkip}' for offer {ctl.LookSkipOffer}), level at most {maxLv:0.000}, view moved {moved}",
                    ctl.LookCount == looks0 && ctl.LookSkip == "off" && ctl.LookSkipOffer == w.SpotOffers && maxLv <= 0f && !moved);
            }
            Game.Data.zoomMode = (int)mode0;

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
                yield return null;
                // (the quick retry after his own cast missed: no look)
                SpotCheck($"look_retry: the spot {w.SpotOffers} {w.SpotT:0.0}s up, {Time.time - w.SpotEndedAt:0.0}s after the 'outside' miss, gets no look (skip '{ctl.LookSkip}' for offer {ctl.LookSkipOffer}, looking {ctl.SpotLooking})",
                    mode0 == ZoomMode.Off || (ctl.LookSkip == "retry" && ctl.LookSkipOffer == w.SpotOffers && !ctl.SpotLooking));
                miss0 = w.SpotMisses;
                // (no ordinary fish on the wrong rig while it is watched: a bite on it is not what this checks, and on the
                // lake's bed the crucians gather where it lands)
                bool noBitesW = FishingController.NoBites;
                FishingController.NoBites = true;
                foreach (var f in ctl.Spawner.Fish)
                    if (f.State == FishAgent.St.Approach || f.State == FishAgent.St.Nibble) f.LoseInterest();
                yield return CastAt(ctl, w.Spot);
                landD = new Vector2(ctl.Tackle.Surface.x - w.Spot.x, ctl.Tackle.Surface.z - w.Spot.z).magnitude;
                for (float t = 0f; t < 4f && ctl.State == FishingController.S.Waiting; t += Time.deltaTime) yield return null;
                SpotCheck($"a wrong rig ({wrong.id}) {landD:0.0} m from the spot misses it: end '{w.SpotEnd}', misses {miss0} -> {w.SpotMisses}, claimed {w.SpotClaimed}, state {ctl.State}",
                    w.SpotEnd == "rig" && w.SpotMisses == miss0 + 1 && !w.SpotClaimed && ctl.State == FishingController.S.Waiting);
                FishingController.NoBites = noBitesW;
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
            // (the claim's wide splash ring a moment into its swell, its spray up)
            for (float t = 0f; t < 0.3f; t += Time.deltaTime) yield return null;
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
