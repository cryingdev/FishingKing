using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto encounter (Docs/lures_legend_spec.md 4.5, Docs/legends_rollout.md 6): plays the legend encounter with the
    /// simulated pointer, with -fkencounter (now: it starts 1 s after the lure lands; natural: the key is worked the way
    /// the legend likes near the lurk point until the meter fills) and -fkstage / -fklegend for which legend.
    /// -fkencplay perfect | bad | early | both (default both: a perfect encounter, then a failed one). Each mood is played
    /// by its verb:
    /// <list type="bullet">
    /// <item>perfect: Wind the band midpoint (circles, radius 0.12 H); RunPause the band midpoint for the mid run length,
    /// then a pause of pauseMin + 0.3 s with the finger held still; FlickPause a downward 톡 at strength
    /// min(0.4, 0.7 maxStrength), then a pause at the window's midpoint; Hold hands off; in NoseIn it waits through the
    /// fake-out, and taps 0.1 s after the close;</item>
    /// <item>bad: the 경계 verb's wrong play (too fast; for a legend with no too-fast rule, stopping): it turns away;</item>
    /// <item>early: as perfect, but a tap on the fake-out (or 0.2 s into a nose-in without one): the gauge drops to 65,
    /// then on to the bite.</item>
    /// </list>
    /// Logs "[ENC] phase=.. t=.. gauge=.. mood=.." on every phase / mood change and checks: encounter triggered, reached
    /// NoseIn, hook perfect=..., fight species=&lt;the legend&gt;, and for bad "failed -> Waiting, lure kept". A hooked
    /// legend is fought out and sold. One capture set into -fkshots: enc_&lt;id&gt;_1_eyes, _2_approach, _3_pass (nearest the
    /// camera while curious), _4_excited, _5_bite_full, _5b_hookset, _6_wipe, _7_fight, _8_fail; a tease watched from
    /// above adds _2b_rise (through the waterline), _3a_top_wary (the lure swimming), _4c_nosein, and its _3_pass is the
    /// curious "퐁" (the spray up); its caption check also keeps captions off the top view's lure. The caption check
    /// (<see cref="EncCaptionCheck"/>) logs every captioned frame against the legend's face (with -fkcapshots also one
    /// cap_NN shot per caption); the run fails if any caption covers the face.
    /// </summary>
    public partial class AutoPilot
    {
        int encFails;
        readonly HashSet<string> encShots = new HashSet<string>();
        string encId = "legend";
        BaitDef encKey;

        IEnumerator EncounterTest()
        {
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("no FishingController (use -fkscene Fishing -fkstage <stage>)");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            string mode = Arg("-fkencplay");
            if (string.IsNullOrEmpty(mode) || mode.StartsWith("-")) mode = "both";
            var plays = mode == "both" ? new[] { "perfect", "bad" } : new[] { mode };
            PointerInput.SimDpi = 0f;
            encKey = Game.I.Bait;
            encId = LegendWatch.DebugLegend ?? (ctl.Watch != null ? ctl.Watch.Legend.id : "none");
            Log($"encounter test on {ctl.Stage.Def.id}: legend {encId}, plays {string.Join(", ", plays)}, debug {LegendWatch.DebugMode ?? "off"}, " +
                $"rod {Game.I.Rod.id} reel {Game.I.Reel.id} line {Game.I.Line.id} ({Game.I.Line.strength:0} kg) key {encKey.id}, " +
                $"watch {(ctl.Watch != null ? string.Join("+", ctl.Watch.Legends.Select(f => f.id)) : "none")}");
            if (ctl.Watch == null)
            {
                Log("CHECK FAIL no legend watch on this stage");
                Application.Quit();
                yield break;
            }
            StartCoroutine(EncCapture(ctl));
            StartCoroutine(EncCaptionCheck(ctl));
            foreach (var style in plays)
                yield return EncounterRun(ctl, style);
            Log($"[CAP] total: {capFrames} captioned frames, caption on the face {capHits}, overlays on the face {capOverlays}, spot changes while a caption showed {capMoves}" +
                $"; top view: {capTopFrames} captioned frames, caption on the lure {capFrogHits}, overlays on the lure {capFrogOverlays}");
            EncCheck($"no caption on the legend's face ({capHits} of {capFrames} captioned frames)", capFrames > 0 && capHits == 0);
            if (capTopFrames > 0) EncCheck($"no caption on the top view's lure ({capFrogHits} of {capTopFrames} captioned frames)", capFrogHits == 0);
            Log($"encounter test done ({encId}): {encFails} failed");
            yield return new WaitForSeconds(0.5f);
            PointerInput.SimActive = false;
            Application.Quit();
        }

        void EncCheck(string what, bool ok)
        {
            if (!ok) encFails++;
            Log($"CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        /// <summary>The key back on the rod (a natural bait topped up), the float set to the bottom for a bait.</summary>
        void EncEquip(FishingController ctl)
        {
            if (encKey == null) return;
            if (encKey.isLure) { if (!Game.I.Owns(encKey.id)) Game.Data.ownedItems.Add(encKey.id); }
            else if (Game.I.BaitCount(encKey.id) < 5) Game.I.AddBait(encKey.id, 20);
            if (Game.I.Bait.id != encKey.id) ctl.EquipBait(encKey);
            // "바닥 찍기": a bait's float set deeper than the water, so it lies on the bottom
            if (!encKey.isLure) ctl.FloatDepthChanged(8f);
        }

        IEnumerator EncounterRun(FishingController ctl, string style)
        {
            yield return BackToReady(ctl);
            EncEquip(ctl);
            yield return new WaitForSeconds(0.3f);
            Log($"[ENC] run {style}: casting");
            bool natural = LegendWatch.DebugMode == "natural";
            float flick = natural && ctl.Stage.L.mode == "boat" ? 4.2f : 3.4f;
            for (int tries = 0; tries < 4 && ctl.State == FishingController.S.Ready; tries++)
            {
                yield return Cast(ctl, flick, 0f);
                for (float w = 0f; w < 6f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
            }
            Log($"[ENC] cast -> {ctl.State} z {ctl.Tackle.Surface.z:0.0} depth {ctl.Tackle.Depth:0.0} bottom {ctl.Tackle.Bottom:0.0}");
            // wait for the encounter (natural: work the key the way the legend likes)
            float t = 0f;
            if (natural) StartCoroutine(EncTrigger(ctl));
            while (ctl.State == FishingController.S.Waiting && t < (natural ? 90f : 12f))
            {
                t += Time.deltaTime;
                yield return null;
            }
            encTriggerOn = false;
            PointerInput.SimDown = false;
            EncCheck($"encounter triggered ({style}, after {t:0.0}s)", ctl.State == FishingController.S.Encounter);
            if (ctl.State != FishingController.S.Encounter) yield break;
            var e = ctl.Encounter;
            Log($"[ENC] legend {e.Sp.id} (expected {encId})");
            var lastPh = e.Ph;
            int lastMood = e.Mood;
            float t0 = Time.time, ang = 0f, stepT = 0f;
            int step = 0, stepMood = -1;
            bool sawNoseIn = false, earlyDone = false, tapped = false;
            float windSign = CircleGesture.Reversed ? -1f : 1f;
            var centre = Scr(0.72f, 0.4f);
            float radius = Screen.height * 0.12f;
            PointerInput.SimActive = true;
            LogEnc(e, t0);
            while (ctl.State == FishingController.S.Encounter)
            {
                if (e.Ph != lastPh || e.Mood != lastMood)
                {
                    lastPh = e.Ph;
                    lastMood = e.Mood;
                    LogEnc(e, t0);
                }
                if (e.Ph == LegendEncounter.Phase.NoseIn) sawNoseIn = true;
                float dt = Time.deltaTime;
                if (e.Ph == LegendEncounter.Phase.Tease)
                {
                    var md = e.MoodDef;
                    if (e.Mood != stepMood)
                    {
                        stepMood = e.Mood;
                        step = 0;
                        stepT = 0f;
                    }
                    stepT += dt;
                    if (style == "bad" && e.Mood == 0)
                    {
                        // the wrong play: too fast (a legend with no too-fast rule: stopping)
                        float fast = md.tooFast < 90f ? md.tooFast + 0.8f : 0f;
                        if (fast > 0f) Circle(ref ang, fast, windSign, centre, radius, dt);
                        else PointerInput.SimDown = false;
                    }
                    else if (style == "bad") PointerInput.SimDown = false;
                    else
                    {
                        switch (md.verb)
                        {
                            case Verb.Wind:
                                Circle(ref ang, (md.lo + md.hi) * 0.5f, windSign, centre, radius, dt);
                                break;
                            case Verb.RunPause:
                            {
                                // a run at the band's midpoint for the mid run length, then the finger held still
                                float run = (md.runMin + md.runMax) * 0.5f, pause = md.pauseMin + 0.3f;
                                if (step == 0 && stepT >= run)
                                {
                                    step = 1;
                                    stepT = 0f;
                                }
                                else if (step == 1 && stepT >= pause)
                                {
                                    step = 0;
                                    stepT = 0f;
                                }
                                if (step == 0) Circle(ref ang, (md.lo + md.hi) * 0.5f, windSign, centre, radius, dt);
                                else PointerInput.SimDown = true;   // held still where it is
                                break;
                            }
                            case Verb.FlickPause:
                            {
                                // a light 톡 (a quick pull down), then a pause at the window's midpoint
                                float pause = (md.lo + md.hi) * 0.5f;
                                if (step == 0 || stepT >= pause)
                                {
                                    step = 1;
                                    float strength = Mathf.Min(0.4f, 0.7f * md.maxStrength);
                                    yield return LureFlick(FlickCast.MouseSpeedMin + (0.5f * FlickCast.MouseSpeedFull - FlickCast.MouseSpeedMin) * strength, 0.10f);
                                    stepT = 0f;
                                    continue;
                                }
                                PointerInput.SimDown = false;
                                break;
                            }
                            default:
                                PointerInput.SimDown = false;
                                break;
                        }
                    }
                }
                else if (e.Ph == LegendEncounter.Phase.NoseIn && style == "early" && !earlyDone
                         && (e.FakeAt >= 0f ? e.InFakeOut && e.FakeOutK > 0.3f : e.PhaseT > 0.2f))
                {
                    earlyDone = true;
                    Log($"[ENC] early tap in NoseIn ({(e.InFakeOut ? "on the fake-out" : "no fake-out")})");
                    yield return Tap(Scr(0.5f, 0.6f));
                    continue;
                }
                else if (e.Ph == LegendEncounter.Phase.HookWindow && !tapped && e.PhaseT >= 0.1f)
                {
                    tapped = true;
                    yield return Tap(Scr(0.5f, 0.6f));
                    continue;
                }
                else PointerInput.SimDown = false;
                yield return null;
            }
            PointerInput.SimDown = false;
            LogEnc(e, t0);
            if (style == "bad")
            {
                yield return null;
                bool ok = !e.Success && ctl.State == FishingController.S.Waiting && ctl.Tackle.State == Tackle.Mode.Water && Game.I.Bait.id == encKey.id;
                EncCheck($"failed ({e.FailReason}: '{e.FailText}' / '{e.FailTip}') → Waiting, lure kept (state {ctl.State}, rig {ctl.Tackle.State}, lure {Game.I.Bait.id})", ok);
                yield return new WaitForSeconds(1f);
                yield break;
            }
            EncCheck($"reached NoseIn ({style})", sawNoseIn);
            EncCheck($"hook perfect={e.Perfect} (hooked {e.Success}, at {e.HookAt:0.00}s after the close, penalties {e.Penalties}, perfect lure {e.PerfectLure})",
                e.Success && e.Perfect);
            var hooked = ctl.Hooked;
            EncCheck($"fight species={(hooked != null ? hooked.Sp.id : "none")} (state {ctl.State}, {(hooked != null ? hooked.Cm : 0f):0.0}cm)",
                ctl.State == FishingController.S.Fighting && hooked != null && hooked.Sp.id == encId);
            if (ctl.State == FishingController.S.Fighting) yield return LureFightOut(ctl);
        }

        /// <summary>One frame of simulated circles at <paramref name="rps"/> revolutions per second (the finger down).</summary>
        static void Circle(ref float ang, float rps, float windSign, Vector2 centre, float radius, float dt)
        {
            ang -= windSign * dt * rps * Mathf.PI * 2f;
            PointerInput.SimDown = true;
            PointerInput.SimPos = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
        }

        bool encTriggerOn;

        /// <summary>
        /// -fkencounter natural: works the key the way the legend likes until the encounter starts (rollout 6): a bait lies
        /// still on the bottom; the frog runs and pauses; the soft worm hops and rests on the bottom; the kona runs at 2.1
        /// rev/s (the marlin) or crawls at 0.8 (the great white); the egi is flicked up and let fall.
        /// </summary>
        IEnumerator EncTrigger(FishingController ctl)
        {
            encTriggerOn = true;
            float windSign = CircleGesture.Reversed ? -1f : 1f, ang = 0f, logT = 0f;
            var centre = Scr(0.72f, 0.4f);
            float radius = Screen.height * 0.12f;
            string key = encKey != null ? encKey.id : "";
            float cycle = 0f;
            while (encTriggerOn && ctl.State == FishingController.S.Waiting)
            {
                float dt = Time.deltaTime;
                cycle += dt;
                if ((logT -= dt) <= 0f)
                {
                    logT = 2f;
                    var w = ctl.Watch;
                    Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[ENC] soak {0:0.0}s meter {1:0.00} q {2:0.00} still {3:0.00} crawl {4:0.00} depth {5:0.00} z {6:0.0} lurk {7} blocked {8}",
                        w.Soak, w.Meter, ctl.Rhythm.Q, w.StillQ, w.CrawlQ, ctl.Tackle.Depth, ctl.Tackle.Surface.z, w.HasLurk ? w.Lurk.ToString("F1") : "-", w.Blocked));
                }
                switch (key)
                {
                    case "bait_frog":
                        // a run, then the finger held still (a lift would lose the circle)
                        if (cycle < 1.2f) Circle(ref ang, 0.55f, windSign, centre, radius, dt);
                        else PointerInput.SimDown = true;
                        if (cycle >= 2.4f) cycle = 0f;
                        break;
                    case "bait_kona":
                        Circle(ref ang, encId == "great_white" ? 0.8f : 2.1f, windSign, centre, radius, dt);
                        break;
                    case "bait_softworm":
                    case "bait_egi":
                    case "bait_jig":
                        PointerInput.SimDown = false;
                        if (ctl.Tackle.OnBottom && cycle >= 2.2f)
                        {
                            cycle = 0f;
                            yield return LureFlick(0.9f, 0.10f);
                            continue;
                        }
                        break;
                    default:
                        PointerInput.SimDown = false;   // a bait: lie still
                        break;
                }
                yield return null;
            }
            PointerInput.SimDown = false;
        }

        void LogEnc(LegendEncounter e, float t0) =>
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[ENC] phase={0} t={1:0.00} gauge={2:0} mood={3} tease={4:0.0} phaseT={5:0.00}",
                e.Ph, Time.time - t0, e.Gauge, LegendEncounter.MoodName(e.Mood), e.TeaseT, e.PhaseT));

        /// <summary>The capture set: each shot once, at its moment (the first success and the first fail).</summary>
        IEnumerator EncCapture(FishingController ctl)
        {
            float prevD = 99f;
            bool wasEnc = false, success = false;
            while (true)
            {
                var e = ctl.Encounter;
                var v = ctl.EncounterView;
                if (ctl.State == FishingController.S.Encounter && e != null)
                {
                    wasEnc = true;
                    var ph = e.Ph;
                    string shot = null;
                    bool topFlow = v != null && v.TopFlow;
                    if (ph == LegendEncounter.Phase.Eyes && e.PhaseT >= Mathf.Min(1.5f, e.PhaseLen - 0.15f)) shot = "1_eyes";
                    else if (ph == LegendEncounter.Phase.Approach && topFlow && v.RiseK >= 0.45f) shot = "2b_rise";
                    else if (ph == LegendEncounter.Phase.Approach && e.PhaseT >= 2.0f && (!topFlow || v.RiseK < 0f)) shot = "2_approach";
                    // from above: 경계 the lure swimming with its wake, 호기심 the "퐁" (its spray up), the nose-in's bulge
                    else if (topFlow && ph == LegendEncounter.Phase.Tease && e.Mood == 0 && e.MoodT >= 2.5f && ctl.LureIn.Winding && ctl.LureIn.WindRunT >= 0.6f) shot = "3a_top_wary";
                    else if (topFlow && ph == LegendEncounter.Phase.Tease && e.Mood == 1)
                    {
                        if (e.MoodT >= 1.5f && v.TopSplashAgo >= 0.07f && v.TopSplashAgo <= 0.2f) shot = "3_pass";
                    }
                    else if (topFlow && ph == LegendEncounter.Phase.NoseIn && e.PhaseT >= e.PhaseLen - 0.15f) shot = "4c_nosein";
                    else if (ph == LegendEncounter.Phase.Tease && e.Mood == 1 && v != null)
                    {
                        // the frame the fish is nearest the camera (one frame past the minimum)
                        float d = v.FishCamDist;
                        if (d > prevD + 0.002f && prevD < 2.2f * e.Def.camScale) shot = "3_pass";
                        prevD = d;
                    }
                    else if (ph == LegendEncounter.Phase.Tease && e.Mood == 2 && e.MoodT >= 1.0f) shot = "4_excited";
                    else if (ph == LegendEncounter.Phase.NoseIn && e.InFakeOut && e.FakeOutK >= 0.5f) shot = "4b_fakeout";
                    else if (ph == LegendEncounter.Phase.Lunge && e.PhaseT >= e.PhaseLen - 0.035f) shot = "5_bite_full";
                    else if (ph == LegendEncounter.Phase.Hooked && e.PhaseT >= 0.2f) shot = "5b_hookset";
                    else if (ph == LegendEncounter.Phase.Surface && e.PhaseT >= 0.2f) shot = "6_wipe";
                    else if (ph == LegendEncounter.Phase.TurnAway && e.PhaseT >= 0.5f) shot = "8_fail";
                    if (ph != LegendEncounter.Phase.Tease || e.Mood != 1) prevD = 99f;
                    if (ph == LegendEncounter.Phase.Surface) success = true;
                    if (shot != null && encShots.Add(shot)) yield return EncShot(ctl, $"enc_{encId}_{shot}");
                }
                else if (wasEnc && success && ctl.State == FishingController.S.Fighting)
                {
                    wasEnc = false;
                    if (encShots.Add("7_fight")) yield return EncShot(ctl, $"enc_{encId}_7_fight");
                }
                yield return null;
            }
        }

        int capFrames, capHits, capOverlays, capMoves, capTopFrames, capFrogHits, capFrogOverlays;

        static string R(Rect r) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0},{1:0},{2:0},{3:0})", r.xMin, r.yMin, r.xMax, r.yMax);

        /// <summary>
        /// The caption check, every frame of an encounter: a "[CAP]" line per captioned frame with the caption's plate, the
        /// legend's face (canvas units) and whether they intersect (they must never), a line for any prompt / gauge / name
        /// card on the face, and (with -fkcapshots) one shot per caption text 0.25 s after it comes up (cap_NN.png).
        /// </summary>
        IEnumerator EncCaptionCheck(FishingController ctl)
        {
            string last = null, lastSpot = null;
            int lastSerial = -1;
            float since = 0f;
            int n = 0;
            bool capShots = Array.IndexOf(Environment.GetCommandLineArgs(), "-fkcapshots") >= 0;
            var shotTexts = new HashSet<string>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var endOfFrame = new WaitForEndOfFrame();
            yield return endOfFrame;
            while (true)
            {
                var hud = EncounterHUD.Current;
                var e = ctl.Encounter;
                if (ctl.State == FishingController.S.Encounter && hud != null && e != null)
                {
                    bool face = hud.FaceNow(out var fr);
                    bool frog = hud.FrogNow(out var gr);
                    if (hud.CaptionShown)
                    {
                        string text = hud.CaptionNow, spot = hud.CaptionSpotName;
                        if (text != last || since == 0f || hud.CaptionSerial != lastSerial)
                        {
                            last = text;
                            lastSerial = hud.CaptionSerial;
                            since = 0f;
                        }
                        else if (spot != lastSpot) capMoves++;
                        lastSpot = spot;
                        since += Time.deltaTime;
                        var cr = hud.CaptionRect;
                        bool hit = face && cr.Overlaps(fr);
                        bool onFrog = frog && cr.Overlaps(gr);
                        capFrames++;
                        if (hit) capHits++;
                        if (frog) capTopFrames++;
                        if (onFrog) capFrogHits++;
                        string one = text.Replace("\n", " / ");
                        Log(string.Format(inv, "[CAP] ph={0} t={1:0.00} '{2}' spot={3} cap={4} face={5} hit={6}{7}", e.Ph, e.PhaseT, one,
                            hud.CaptionSpotName, R(cr), face ? R(fr) : "-", hit ? "YES" : "no",
                            frog ? $" lure={R(gr)} onLure={(onFrog ? "YES" : "no")}" : ""));
                        if (capShots && since >= 0.25f && shotTexts.Add(text))
                        {
                            n++;
                            string name = $"cap_{encId}_{n:00}";
                            Log($"[CAP] shot {name} = '{one}' ({e.Ph})");
                            StartCoroutine(EncShot(ctl, name));
                        }
                    }
                    else
                    {
                        last = null;
                        since = 0f;
                    }
                    if (face)
                    {
                        string o = "";
                        if (hud.PromptShown && hud.PromptRect.Overlaps(fr)) o += " prompt" + R(hud.PromptRect);
                        if (hud.GaugeShown && hud.GaugeRect.Overlaps(fr)) o += " gauge" + R(hud.GaugeRect);
                        if (hud.CardShown && hud.CardRect.Overlaps(fr)) o += " card" + R(hud.CardRect);
                        if (o.Length > 0)
                        {
                            capOverlays++;
                            Log(string.Format(inv, "[CAP] overlay on the face ph={0} t={1:0.00} face={2}:{3}", e.Ph, e.PhaseT, R(fr), o));
                        }
                    }
                    if (frog)
                    {
                        string o = "";
                        if (hud.PromptShown && hud.PromptRect.Overlaps(gr)) o += " prompt" + R(hud.PromptRect);
                        if (hud.GaugeShown && hud.GaugeRect.Overlaps(gr)) o += " gauge" + R(hud.GaugeRect);
                        if (hud.CardShown && hud.CardRect.Overlaps(gr)) o += " card" + R(hud.CardRect);
                        if (o.Length > 0)
                        {
                            capFrogOverlays++;
                            Log(string.Format(inv, "[CAP] overlay on the lure ph={0} t={1:0.00} lure={2}:{3}", e.Ph, e.PhaseT, R(gr), o));
                        }
                    }
                }
                // read after the HUD's LateUpdate: what this frame shows
                yield return endOfFrame;
            }
        }

        IEnumerator EncShot(FishingController ctl, string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string p = Path.Combine(shots, name + ".png");
            File.WriteAllBytes(p, tex.EncodeToPNG());
            Destroy(tex);
            var e = ctl.Encounter;
            var v = ctl.EncounterView;
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "shot {0} phase={1} gauge={2:0} mood={3} fishDist={4:0.00} crop={5}",
                p, e != null ? e.Ph.ToString() : "-", e != null ? e.Gauge : 0f, e != null ? LegendEncounter.MoodName(e.Mood) : "-",
                v != null ? v.FishCamDist : -1f, v != null ? v.Crop.ToString() : "-"));
        }
    }
}
