using System.Collections;
using System.Globalization;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto obstacles (Docs/obstacles_spec.md 14): one capture set with [OBST] CHECK &lt;name&gt; PASS|FAIL &lt;numbers&gt; lines.
    /// <code>
    /// -fkfresh -fkrich -fkgear -fksave obst -fkscene Fishing -fkstage stream -fkauto obstacles -fkobstlog -fkshots &lt;dir&gt;
    /// </code>
    /// The stream: the aiming outlines (a spinner wound up), a cast at md25.0 that bounces off it into the water beside it
    /// (a bank shot), a spoon snagged in md13.5's skirt freed by a 톡, a crank snagged there freed by the sweep to the free
    /// side (after a wrong sweep shows the arrow), a spoon forced until the line breaks. The lake: a frog on a lily pad
    /// (sits, wound off the edge: the pad-drop window), a worm float on a pad (snagged, a 톡 frees it), a hooked bass that
    /// runs for the boat (ignored: the line rubs and breaks; with side pressure against the run: pulled out), and the bites
    /// near cover against open water. Then the swamp's aiming outlines and every stage with data, -fkobstacles show. Rolls
    /// are seeded (Obstacles.Rnd). Shots: obst_aim, obst_bounce, obst_bankshot, obst_snag, obst_snag_free, obst_snag_arrow,
    /// obst_pad, obst_pad_snag, obst_rub, obst_break, obst_pullout, obst_aim_swamp, obst_show_&lt;stage&gt;.
    /// </summary>
    public partial class AutoPilot
    {
        int obFails;
        static readonly CultureInfo CIb = CultureInfo.InvariantCulture;

        void OCheck(string name, bool ok, string numbers)
        {
            if (!ok) obFails++;
            Debug.Log($"[OBST] CHECK {name} {(ok ? "PASS" : "FAIL")} {numbers}");
        }

        static string F2(float v) => v.ToString("0.00", CIb);

        IEnumerator ObstaclesTest()
        {
            yield return new WaitForSeconds(2f);
            if (Arg("-fkobstseed") == null) Obstacles.Rnd = new System.Random(20260930);
            Random.InitState(4242);
            Obstacles.Log = true;
            PointerInput.SimDpi = 0f;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            // (the one-time hints would cover the captures' flashes)
            var sd = Game.Data;
            sd.sweepHint = sd.tideHint = sd.driftHint = sd.mendHint = sd.sideHint = sd.timeHint = true;
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl != null && ctl.Stage.Def.id == "ocean")
            {
                // (-fkstage ocean: the ocean's open-water structure instead: AutoPilot.ObstaclesOcean.cs)
                if (Dialog.Open)
                {
                    Click("알겠어요");
                    yield return new WaitForSeconds(0.5f);
                }
                yield return OceanObstacles(ctl);
                PointerInput.SimLeft = PointerInput.SimRight = false;
                PointerInput.SimDown = false;
                PointerInput.SimActive = false;
                Debug.Log($"[OBST] obstacles test (ocean) done: {obFails} failed");
                Application.Quit();
                yield break;
            }
            if (ctl == null || ctl.Stage.Def.id != "stream")
            {
                yield return GoStage("stream");
                ctl = FindAnyObjectByType<FishingController>();
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            FishingController.NoBites = true;
            yield return ObAim(ctl);
            yield return ObBounce(ctl);
            yield return ObSnagTok(ctl);
            yield return ObSnagSweep(ctl);
            yield return ObSnagBreak(ctl);
            // the lake: pads, the bass and its cover, the bites
            FishSpawner.OnlySpecies = GameDatabase.GetFish("largemouth_bass");
            yield return GoStage("lake", 3f);
            ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null) OCheck("lake", false, "no scene");
            else
            {
                yield return ObPads(ctl);
                yield return ObCoverFights(ctl);
                yield return ObBites(ctl);
            }
            FishSpawner.OnlySpecies = null;
            // the swamp's aiming outlines (its weed beds, root tangles and sunken logs)
            yield return GoStage("swamp", 3f);
            ctl = FindAnyObjectByType<FishingController>();
            if (ctl != null) yield return ObAim(ctl, "obst_aim_swamp", "aim_swamp");
            FishingController.NoBites = false;
            yield return ObShowAll();
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Debug.Log($"[OBST] obstacles test done: {obFails} failed");
            Application.Quit();
        }

        // ------------------------------------------------------------------ 1. the aim outlines
        IEnumerator ObAim(FishingController ctl, string shot = "obst_aim", string check = "aim")
        {
            yield return ToReady(ctl);
            EquipTest("bait_spinner", ctl);
            yield return null;
            yield return WindUp();
            yield return new WaitForSeconds(1f);
            var ov = ctl.Overlay;
            yield return NamedShot(shot);
            int zones = ov != null ? ov.ZonesDrawn : 0;
            float a = ov != null ? ov.Alpha : 0f;
            OCheck(check, ctl.State == FishingController.S.Aiming && ctl.WindUpArmed && zones >= 5 && a >= 0.99f,
                $"outlines {zones} snag/weed zones in range, alpha {F2(a)}, armed {ctl.WindUpArmed}");
            // (called off: slowly back up to the press point)
            yield return Move(PointerInput.SimPos, Scr(0.5f, 0.55f), 2.2f, true);
            PointerInput.SimDown = false;
            yield return null;
            for (float w = 0f; w < 2f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.4f);
            OCheck(check + "_gone", (ov == null || ov.Alpha <= 0.01f) && ctl.State == FishingController.S.Ready, $"alpha after the release {F2(ov != null ? ov.Alpha : 0f)} state {ctl.State}");
        }

        // ------------------------------------------------------------------ 2. a cast into md25.0: bounce, bank shot
        IEnumerator ObBounce(FishingController ctl)
        {
            yield return ToReady(ctl);
            var obs = ctl.Stage.Obstacles;
            var rock = obs.Get("md25.0");
            if (rock == null)
            {
                OCheck("bounce", false, "no md25.0");
                yield break;
            }
            int c0 = ctl.ObstContacts, b0 = ctl.BankShots;
            var target = new Vector3(rock.x, 0f, rock.z + 0.3f);
            ctl.DebugCastTo(target);
            bool shot = false;
            for (float w = 0f; w < 5f && ctl.State == FishingController.S.Casting; w += Time.deltaTime)
            {
                if (!shot && ctl.ObstContacts > c0)
                {
                    shot = true;
                    yield return NamedShot("obst_bounce");
                    continue;
                }
                yield return null;
            }
            // (the 뱅크샷! flash over the rig in the water beside the rock)
            yield return new WaitForSeconds(0.15f);
            if (ctl.BankShots > b0) yield return NamedShot("obst_bankshot");
            yield return new WaitForSeconds(0.15f);
            var l = ctl.LastLanding;
            float dist = Obstacles.Dist(rock, new Vector2(l.at.x, l.at.z));
            bool hit = ctl.ObstContacts > c0 && ctl.LastContact == rock;
            OCheck("bounce", hit && !l.Perched && dist <= 2f && ctl.BankShots > b0,
                string.Format(CIb, "contacts {0} on {1}, landed ({2:0.00}, {3:0.00}) {4:0.00} m from md25.0 perched {5}, bank shots {6}",
                    ctl.ObstContacts - c0, ctl.LastContact != null ? ctl.LastContact.id : "-", l.at.x, l.at.z, dist, l.Perched, ctl.BankShots - b0));
            yield return ToReady(ctl);
        }

        /// <summary>A point in the snag skirt of md13.5 on his side of the rock (outside the rock itself), on the water.</summary>
        static Vector3 SkirtPoint(FishingController ctl, string rockId)
        {
            var obs = ctl.Stage.Obstacles;
            var rock = obs.Get(rockId);
            var skirt = obs.Get(rockId + ".skirt");
            var shore = new Vector2(ctl.Angler.X, ctl.Stage.L.zNear);
            var dir = (shore - rock.C).normalized;
            for (float d = 0.2f; d < 3f; d += 0.05f)
            {
                var q = rock.C + dir * d;
                if (!obs.Inside(rock, q, 0.2f) && obs.Inside(skirt, q)) return new Vector3(q.x, 0f, q.y);
            }
            return new Vector3(rock.C.x + dir.x, 0f, rock.C.y + dir.y);
        }

        /// <summary>A rig at <paramref name="at"/> (wound through the skirt at once with SnagMult 999) until it snags.</summary>
        IEnumerator SnagHere(FishingController ctl, string bait, Vector3 at, float depth, float rps)
        {
            yield return ToReady(ctl);
            EquipTest(bait, ctl);
            yield return null;
            ctl.DebugPlaceRig(at);
            yield return null;
            if (depth > 0f) ctl.Tackle.Depth = depth;
            else yield return new WaitForSeconds(0.9f);   // (a sinking lure goes down first)
            Obstacles.SnagMult = 999f;
            var centre = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.12f, windSign = CircleGesture.Reversed ? -1f : 1f;
            PointerInput.SimActive = true;
            for (float t = 0f; t < 2f && ctl.State == FishingController.S.Waiting; t += Time.deltaTime)
            {
                circleAng -= windSign * Time.deltaTime * rps * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * r;
                if (depth > 0f && t < 0.2f) ctl.Tackle.Depth = Mathf.Max(ctl.Tackle.Depth, depth);
                yield return null;
            }
            PointerInput.SimDown = false;
            Obstacles.SnagMult = 1f;
            yield return null;
        }

        // ------------------------------------------------------------------ 3. a spoon snagged, freed by a 톡
        IEnumerator ObSnagTok(FishingController ctl)
        {
            var at = SkirtPoint(ctl, "md13.5");
            int s0 = ctl.SnagCount;
            yield return SnagHere(ctl, "bait_spoon", at, 0f, 1f);
            bool snagged = ctl.State == FishingController.S.Snagged && ctl.SnagCount > s0;
            var sn = ctl.Tackle.Snag;
            Log(string.Format(CIb, "snag (spoon) at ({0:0.00}, {1:0.00}): {2} {3}", at.x, at.z, ctl.State, sn != null ? sn.zone.id : "-"));
            if (!snagged)
            {
                OCheck("snag_tok", false, $"not snagged ({ctl.State})");
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            yield return NamedShot("obst_snag");
            // let the line go slack (1.5 s without winding: x1.5), then 톡 (up to 4)
            yield return new WaitForSeconds(1.1f);
            int tries = 0;
            for (; tries < 4 && ctl.State == FishingController.S.Snagged; tries++)
            {
                yield return LureFlick(2.2f, 0.12f);
                for (float w = 0f; w < 0.5f && ctl.State == FishingController.S.Snagged; w += Time.deltaTime) yield return null;
            }
            // (빠졌다!)
            if (ctl.State == FishingController.S.Waiting) yield return NamedShot("obst_snag_free");
            OCheck("snag_tok", ctl.State == FishingController.S.Waiting && ctl.LastFreeWay == "tok",
                $"snagged on {(sn != null ? sn.zone.id : "-")}, freed by '{ctl.LastFreeWay}' after {tries} 톡, state {ctl.State}");
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ 4. a crank snagged, freed by the sweep
        IEnumerator ObSnagSweep(FishingController ctl)
        {
            var at = SkirtPoint(ctl, "md13.5");
            int s0 = ctl.SnagCount;
            yield return SnagHere(ctl, "bait_crank", at, 1.0f, 0.5f);
            var sn = ctl.Tackle.Snag;
            if (ctl.State != FishingController.S.Snagged || ctl.SnagCount == s0 || sn == null)
            {
                OCheck("snag_sweep", false, $"the crank did not snag ({ctl.State})");
                yield break;
            }
            int free = sn.freeSide;
            // the wrong way first: after 1 s the arrow shows the free side
            PointerInput.SimActive = true;
            PointerInput.SimLeft = free > 0;
            PointerInput.SimRight = free < 0;
            float w = 0f;
            for (; w < 1.6f && ctl.State == FishingController.S.Snagged && !(ctl.PushArrow.Visible && ctl.PushArrow.Alpha > 0.9f); w += Time.deltaTime) yield return null;
            bool arrow = ctl.PushArrow.Visible && ctl.PushArrow.Side == free;
            OCheck("snag_arrow", arrow && ctl.State == FishingController.S.Snagged,
                $"the wrong sweep for {F2(w)} s: arrow visible {ctl.PushArrow.Visible} pointing {ctl.PushArrow.Side:+0;-0} (free side {free:+0;-0})");
            yield return NamedShot("obst_snag_arrow");
            PointerInput.SimLeft = PointerInput.SimRight = false;
            yield return new WaitForSeconds(0.4f);
            // the right way: timed from the rod's lean reaching half
            PointerInput.SimLeft = free < 0;
            PointerInput.SimRight = free > 0;
            float leanAt = -1f, t = 0f;
            for (; t < 3f && ctl.State == FishingController.S.Snagged; t += Time.deltaTime)
            {
                if (leanAt < 0f && Mathf.Abs(ctl.Lean) >= 0.5f && Mathf.Sign(ctl.Lean) == free) leanAt = t;
                yield return null;
            }
            PointerInput.SimLeft = PointerInput.SimRight = false;
            float took = leanAt >= 0f ? t - leanAt : -1f;
            OCheck("snag_sweep", ctl.State == FishingController.S.Waiting && ctl.LastFreeWay == "sweep" && took >= 0f && took <= 0.8f,
                string.Format(CIb, "crank on {0}, free side {1:+0;-0}: freed by '{2}' {3:0.00} s after the lean reached 0.5", sn.zone.id, free, ctl.LastFreeWay, took));
            yield return new WaitForSeconds(0.3f);
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ 5. forcing it breaks the line (the spoon is lost)
        IEnumerator ObSnagBreak(FishingController ctl)
        {
            var at = SkirtPoint(ctl, "md13.5");
            int s0 = ctl.SnagCount, b0 = ctl.SnagBreaks;
            yield return SnagHere(ctl, "bait_spoon", at, 0f, 1f);
            if (ctl.State != FishingController.S.Snagged || ctl.SnagCount == s0)
            {
                OCheck("snag_break", false, $"not snagged ({ctl.State})");
                yield break;
            }
            yield return new WaitForSeconds(0.3f);
            var centre = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.12f, windSign = CircleGesture.Reversed ? -1f : 1f, maxR = 0f, t = 0f;
            PointerInput.SimActive = true;
            for (; t < 5f && ctl.State == FishingController.S.Snagged; t += Time.deltaTime)
            {
                circleAng -= windSign * Time.deltaTime * 2f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * r;
                if (ctl.Tackle.Snag != null) maxR = Mathf.Max(maxR, ctl.Tackle.Snag.r);
                yield return null;
            }
            PointerInput.SimDown = false;
            bool owns = Game.I.Owns("bait_spoon");
            OCheck("snag_break", ctl.SnagBreaks > b0 && !owns && ctl.State == FishingController.S.Ready,
                string.Format(CIb, "wound at 2 rev/s: tension ratio up to {0:0.00}, broke after {1:0.00} s, spoon lost {2}, state {3}", maxR, t, !owns, ctl.State));
            yield return new WaitForSeconds(1.5f);
        }

        // ------------------------------------------------------------------ 8. lily pads (the lake)
        IEnumerator ObPads(FishingController ctl)
        {
            var obs = ctl.Stage.Obstacles;
            var pad = obs.Get("pad+7.5_12.8");
            var pad2 = obs.Get("pad-7.5_16.4");
            if (pad == null || pad2 == null)
            {
                OCheck("frog_pad", false, "no pads");
                yield break;
            }
            // the frog sits on the pad, is wound off its edge: 퐁 and the strike window
            yield return ToReady(ctl);
            EquipTest("bait_frog", ctl);
            yield return null;
            int d0 = ctl.PadDrops;
            ctl.DebugPlaceRig(new Vector3(pad.x, 0f, pad.z));
            yield return new WaitForSeconds(1f);
            bool sat = ctl.Tackle.OnPad == pad;
            yield return NamedShot("obst_pad");
            yield return new WaitForSeconds(1f);
            bool still = ctl.Tackle.OnPad == pad;
            var centre = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.12f, windSign = CircleGesture.Reversed ? -1f : 1f, t = 0f;
            bool window = false;
            PointerInput.SimActive = true;
            for (; t < 8f && ctl.State == FishingController.S.Waiting && ctl.PadDrops == d0; t += Time.deltaTime)
            {
                circleAng -= windSign * Time.deltaTime * 0.5f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * r;
                yield return null;
            }
            PointerInput.SimDown = false;
            window = ctl.PadDropUntil > Time.time;
            OCheck("frog_pad", sat && still && ctl.PadDrops > d0 && window,
                $"sat on {pad.id} {sat}, still there after 2 s {still}, wound off the edge after {F2(t)} s (drops {ctl.PadDrops - d0}), window open {window}");
            yield return ToReady(ctl);
            // a worm float on a pad: a pad snag, a 톡 frees it
            EquipTest("bait_worm", ctl);
            yield return null;
            int s0 = ctl.SnagCount;
            ctl.DebugPlaceRig(new Vector3(pad2.x, 0f, pad2.z));
            yield return new WaitForSeconds(0.8f);
            var sn = ctl.Tackle.Snag;
            bool padSnag = ctl.State == FishingController.S.Snagged && ctl.SnagCount > s0 && sn != null && sn.kind == "pad";
            yield return NamedShot("obst_pad_snag");
            int tries = 0;
            for (; tries < 3 && ctl.State == FishingController.S.Snagged; tries++)
            {
                yield return LureFlick(2.2f, 0.12f);
                yield return new WaitForSeconds(0.5f);
            }
            OCheck("pad_snag", padSnag && ctl.State == FishingController.S.Waiting && ctl.LastFreeWay == "tok" && ctl.Tackle.State == Tackle.Mode.Water,
                $"worm on {pad2.id}: pad snag {padSnag}, freed by '{ctl.LastFreeWay}' after {tries} 톡, rig in the water {ctl.Tackle.State == Tackle.Mode.Water}");
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ 6 / 7. the bass and its cover
        /// <summary>
        /// A 32 cm bass hooked at (-6, 1.2 deep, 14) with its run forced to the boat (boat.cover); <paramref name="side"/>:
        /// leant against the cover run (side pressure). Reels at 0.8 rev/s throughout; ignored, every new run is sent back
        /// to the boat too (the fish keeps diving into it). Returns when the fight ends (or after 60 s).
        /// </summary>
        IEnumerator BassFight(FishingController ctl, bool side, string shot, float shotAt)
        {
            int pull0 = ctl.PullOuts;
            yield return ToReady(ctl);
            SteerGear("rod_glass", "reel_basic", "line_nylon2");
            EquipTest("bait_minnow", ctl);
            yield return null;
            ctl.DebugPlaceRig(new Vector3(-6f, 0f, 14f));
            yield return new WaitForSeconds(0.3f);
            FishingController.DebugCover = "boat.cover";
            if (!ctl.DebugHook(GameDatabase.GetFish("largemouth_bass"), 32f, 5151, new Vector3(-6f, -1.2f, 14f)))
            {
                OCheck(side ? "pullout" : "rub_break", false, $"no hook ({ctl.State})");
                FishingController.DebugCover = null;
                yield break;
            }
            var f = ctl.Fight;
            var centre = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.13f, windSign = CircleGesture.Reversed ? -1f : 1f, t = 0f;
            bool shotDone = false;
            PointerInput.SimActive = true;
            while (ctl.State == FishingController.S.Fighting && ctl.Fight == f && t < 60f)
            {
                t += Time.deltaTime;
                circleAng -= windSign * Time.deltaTime * 0.8f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * r;
                bool cover = f.CoverRun || f.CoverHold;
                int lean = side && cover ? -ctl.FishRun : 0;
                PointerInput.SimLeft = lean < 0;
                PointerInput.SimRight = lean > 0;
                // ignored: back to the boat whenever it is out of it
                if (!side && !cover && FishingController.DebugCover == null) FishingController.DebugCover = "boat.cover";
                if (!shotDone && shot != null && (side ? ctl.PullOuts > pull0 : f.Abrasion >= shotAt && ctl.Rubbing))
                {
                    shotDone = true;
                    yield return NamedShot(shot);
                    if (side) break;
                    continue;
                }
                yield return null;
            }
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            FishingController.DebugCover = null;
        }

        IEnumerator ObCoverFights(FishingController ctl)
        {
            // ignored: the run to the boat, the line rubbing on it until it breaks
            int runs0 = ctl.CoverRuns, holds0 = ctl.CoverHolds;
            // (shot early in the hold: the first-rub warning, the 쓸림 meter; then the break's message)
            yield return BassFight(ctl, false, "obst_rub", 0.06f);
            bool broke = ctl.State != FishingController.S.Fighting && ctl.LastSnapCause == FightModel.Cause.Abrasion;
            if (broke) yield return NamedShot("obst_break");
            OCheck("rub_break", ctl.CoverRuns > runs0 && ctl.RubbedThisFight && broke,
                string.Format(CIb, "cover runs {0} ({1}), holds {2}, rubbed {3} on {4}, abrasion {5:0.00}, break cause {6} ({7})",
                    ctl.CoverRuns - runs0, ctl.LastCover != null ? ctl.LastCover.id : "-", ctl.CoverHolds - holds0, ctl.RubbedThisFight, ctl.LastBreakName,
                    ctl.LastAbrasion, ctl.LastSnapCause, ctl.State));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(2f);
            // side pressure against the run: pulled out before the line wears through
            int p0 = ctl.PullOuts;
            yield return BassFight(ctl, true, "obst_pullout", 0f);
            float a = ctl.Fight != null ? ctl.Fight.Abrasion : ctl.LastAbrasion;
            OCheck("pullout", ctl.PullOuts > p0 && a < 0.6f,
                string.Format(CIb, "pulled out {0} time(s) with the abrasion at {1:0.00} (state {2})", ctl.PullOuts - p0, a, ctl.State));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(1.5f);
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ bites near cover > open water
        IEnumerator ObBites(FishingController ctl)
        {
            FishingController.NoBites = false;
            var obs = ctl.Stage.Obstacles;
            var cov = obs.Get("weedbed.cover");
            var spots = new[] { ("cover", cov != null ? new Vector3(cov.x, 0f, cov.z) : new Vector3(3f, 0f, 14f)), ("open", new Vector3(1.5f, 0f, 26f)) };
            yield return ToReady(ctl);
            EquipTest("bait_worm", ctl);
            yield return null;
            float snagWas = Obstacles.SnagMult;
            Obstacles.SnagMult = 0f;   // (the float soaks in the weed bed: no snags in this count)
            var mult = new float[2];
            var appr = new int[2];
            var rolls = new int[2];
            for (int k = 0; k < 2; k++)
            {
                var (name, at) = spots[k];
                yield return ToReady(ctl);
                ctl.DebugPlaceRig(at);
                yield return new WaitForSeconds(0.5f);
                // the appetite multiplier for the bass here (the real BiteMult)
                float m = 0f;
                int n = 0;
                foreach (var f in ctl.Spawner.Fish)
                {
                    m += ctl.BiteMult(f);
                    n++;
                }
                mult[k] = n > 0 ? m / n : 0f;
                int a0 = ctl.Approaches, r0 = ctl.ApproachRolls;
                for (float t = 0f; t < 75f; t += Time.deltaTime)
                {
                    foreach (var f in ctl.Spawner.Fish)
                        if (f.State == FishAgent.St.Approach || f.State == FishAgent.St.Nibble || f.State == FishAgent.St.Bite) f.LoseInterest();
                    if (ctl.State != FishingController.S.Waiting)
                    {
                        yield return ToReady(ctl);
                        ctl.DebugPlaceRig(at);
                    }
                    else if ((ctl.Tackle.Surface - at).magnitude > 1.5f) ctl.Tackle.Surface = at;
                    yield return null;
                }
                appr[k] = ctl.Approaches - a0;
                rolls[k] = ctl.ApproachRolls - r0;
                Log(string.Format(CIb, "[OBST] bites {0} at ({1:0.0}, {2:0.0}): bite mult {3:0.00}, {4} approaches in {5} rolls (75 s)", name, at.x, at.z, mult[k], appr[k], rolls[k]));
            }
            Obstacles.SnagMult = snagWas;
            FishingController.NoBites = true;
            OCheck("bites_structure", mult[1] > 0f && mult[0] / mult[1] >= 1.3f,
                string.Format(CIb, "bite mult in weedbed.cover {0:0.00} vs open water {1:0.00} (x{2:0.00})", mult[0], mult[1], mult[1] > 0f ? mult[0] / mult[1] : 0f));
            OCheck("bites_observed", appr[0] > appr[1],
                string.Format(CIb, "approaches near cover {0} ({1} rolls) vs open water {2} ({3} rolls) in 75 s each (statistical)", appr[0], rolls[0], appr[1], rolls[1]));
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ 9. -fkobstacles show on every stage with data
        IEnumerator ObShowAll()
        {
            foreach (var st in GameDatabase.Stages)
            {
                if (Resources.Load<TextAsset>("Data/obstacles_" + st.id) == null)
                {
                    Log($"[OBST] show {st.id}: no data");
                    continue;
                }
                yield return GoStage(st.id, 2.5f);
                Obstacles.Show = true;
                yield return new WaitForSeconds(1f);
                yield return NamedShot("obst_show_" + st.id);
                Obstacles.Show = false;
                yield return null;
            }
        }
    }
}
