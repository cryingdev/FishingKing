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
    /// runs for the boat and is pulled out by side pressure against the run, and the bites near cover against open water.
    /// The sea (high slack): a 감성돔 on PE 3호 ignored in the tetrapods: the line rubs until it wears through. Every fight
    /// that ends in a catch has its card closed (ToReady), so the scenario always goes on. Then the swamp's aiming
    /// outlines and every stage with data, -fkobstacles show. Rolls are seeded (Obstacles.Rnd). Shots: obst_aim,
    /// obst_bounce, obst_bankshot, obst_snag, obst_snag_free, obst_snag_arrow, obst_pad, obst_pad_snag, obst_pullout,
    /// obst_rub, obst_break (the sea), obst_aim_swamp, obst_show_&lt;stage&gt;.
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
            // the lake: pads, the bass pulled out of its cover, the bites
            FishSpawner.OnlySpecies = GameDatabase.GetFish("largemouth_bass");
            yield return GoStage("lake", 3f);
            ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null) OCheck("lake", false, "no scene");
            else
            {
                yield return ObPads(ctl);
                yield return ObPullOut(ctl);
                yield return ObBites(ctl);
            }
            FishSpawner.OnlySpecies = null;
            // the sea: a fish ignored in the tetrapods wears PE through
            yield return ObRubBreak();
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
                if (leanAt < 0f && Mathf.Abs(ctl.LeanReq) >= 0.5f && Mathf.Sign(ctl.LeanReq) == free) leanAt = t;
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
            yield return ObPadSlide(ctl, pad);
            yield return ToReady(ctl);
        }

        /// <summary>
        /// A soft worm cast onto a pad: the pad wobbles (2 frames of a 1 px shift of the pad cut from the front layer) and the
        /// worm slides off its edge over Tackle.PadSlideTime (0.25 s), not at once. Shots obst_pad_wobble, obst_pad_slide
        /// ("[OBST] padshot" logs the pad on screen).
        /// </summary>
        IEnumerator ObPadSlide(FishingController ctl, Obstacle pad)
        {
            yield return ToReady(ctl);
            EquipTest("bait_softworm", ctl);
            yield return null;
            var tk = ctl.Tackle;
            var wob = ctl.PadWobbleFx;
            var obs = ctl.Stage.Obstacles;
            int w0 = wob.Wobbles, shifted = 0, between = 0;
            float t0 = Time.time, took = -1f;
            bool wobShot = false, slideShot = false;
            var c = new Vector3(pad.x, 0f, pad.z);
            if (PixelView.Current != null)
            {
                var s = PixelView.Current.WorldToScreen(ctl.Stage.P.To2D(c));
                Debug.Log(string.Format(CIb, "[OBST] padshot {0} {1:0} {2:0}", pad.id, s.x, Screen.height - s.y));
            }
            ctl.DebugPlaceRig(c);
            bool slid0 = tk.PadSliding;
            var from = tk.Surface;
            for (float w = 0f; w < 1f && ctl.State == FishingController.S.Waiting; w += Time.deltaTime)
            {
                if (wob.ShiftNow != Vector2Int.zero) shifted++;
                if (tk.PadSliding)
                {
                    float u = (tk.Surface - from).magnitude;
                    if (u > 0.01f) between++;
                }
                else if (took < 0f) took = Time.time - t0;
                if (!wobShot && wob.ShiftNow != Vector2Int.zero)
                {
                    wobShot = true;
                    yield return NamedShot("obst_pad_wobble");
                    continue;
                }
                if (!slideShot && tk.PadSliding && Time.time - t0 >= 0.15f)
                {
                    slideShot = true;
                    yield return NamedShot("obst_pad_slide");
                    continue;
                }
                yield return null;
            }
            var e = tk.Surface;
            bool off = !obs.Inside(pad, new Vector2(e.x, e.z)) && tk.State == Tackle.Mode.Water && tk.OnPad == null;
            OCheck("pad_slide", slid0 && took >= 0.2f && took <= 0.34f && between >= 3 && off,
                string.Format(CIb, "soft worm on {0}: slides off over {1:0.00} s (want ~{2:0.00}), {3} frames on the way, off the pad at ({4:0.00}, {5:0.00}) {6}",
                    pad.id, took, Tackle.PadSlideTime, between, e.x, e.z, off));
            OCheck("pad_wobble", wob.Wobbles > w0 && shifted >= 2 && wob.LastPixels >= 20,
                $"the pad wobbled {wob.Wobbles - w0} time(s) at the landing, shifted in {shifted} frames, {wob.LastPixels} pad pixels cut from the front layer");
        }

        // ------------------------------------------------------------------ 6 / 7. a fish runs for its cover
        /// <summary>
        /// A <paramref name="cm"/> cm <paramref name="fishId"/> hooked at <paramref name="at"/> (fight seed
        /// <paramref name="seed"/>) with its run forced to <paramref name="cover"/>; <paramref name="side"/>: leant against
        /// the cover run (side pressure), else ignored: every new run is sent back to the cover too (the fish keeps diving
        /// into it). Reels at <paramref name="rps"/> rev/s throughout. The shot: at the pull-out (side), or once the abrasion
        /// reaches <paramref name="shotAt"/> while the line rubs. Returns when the fight ends (or after 60 s; side: at the
        /// pull-out).
        /// </summary>
        IEnumerator CoverFight(FishingController ctl, string fishId, float cm, int seed, Vector3 at, string cover, bool side, float rps,
            string shot, float shotAt, string check)
        {
            int pull0 = ctl.PullOuts;
            yield return ToReady(ctl);
            yield return null;
            ctl.DebugPlaceRig(new Vector3(at.x, 0f, at.z));
            yield return new WaitForSeconds(0.3f);
            FishingController.DebugCover = cover;
            if (!ctl.DebugHook(GameDatabase.GetFish(fishId), cm, seed, at))
            {
                OCheck(check, false, $"no hook ({ctl.State})");
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
                circleAng -= windSign * Time.deltaTime * rps * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * r;
                bool inCover = f.CoverRun || f.CoverHold;
                int lean = side && inCover ? -ctl.FishRun : 0;
                PointerInput.SimLeft = lean < 0;
                PointerInput.SimRight = lean > 0;
                // ignored: back to the cover whenever it is out of it
                if (!side && !inCover && FishingController.DebugCover == null) FishingController.DebugCover = cover;
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

        /// <summary>
        /// The lake: a 32 cm bass runs for the boat, leant against (side pressure): pulled out before the line wears through.
        /// Wound at 0.3 rev/s, under the 0.5 rev/s that drags a holding fish out ("horsing it"), so only the side pressure
        /// can pull it out; the way is read from the [OBST] pullout line.
        /// </summary>
        IEnumerator ObPullOut(FishingController ctl)
        {
            yield return ToReady(ctl);
            SteerGear("rod_glass", "reel_basic", "line_nylon2");
            EquipTest("bait_minnow", ctl);
            string way = null;
            Application.LogCallback grab = (msg, trace, type) =>
            {
                if (way == null && msg.StartsWith("[OBST] pullout ")) way = msg.Substring(15).Split(' ')[0];
            };
            Application.logMessageReceived += grab;
            int p0 = ctl.PullOuts, runs0 = ctl.CoverRuns;
            yield return CoverFight(ctl, "largemouth_bass", 32f, 5151, new Vector3(-6f, -1.2f, 14f), "boat.cover", true, 0.3f, "obst_pullout", 0f, "pullout");
            Application.logMessageReceived -= grab;
            float a = ctl.Fight != null ? ctl.Fight.Abrasion : ctl.LastAbrasion;
            OCheck("pullout", ctl.CoverRuns > runs0 && ctl.PullOuts > p0 && way == "side" && a < 0.6f,
                string.Format(CIb, "cover run to {0}, pulled out {1} time(s) by '{2}' with the abrasion at {3:0.00} (state {4})",
                    ctl.LastCover != null ? ctl.LastCover.id : "-", ctl.PullOuts - p0, way ?? "-", a, ctl.State));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(1.5f);
            yield return ToReady(ctl);
        }

        /// <summary>
        /// The sea at high slack (no current): a 50 cm 감성돔 on PE 3호 (tough 0.6: braid cuts on concrete, rough 1.3) runs
        /// for tet32's cover among the tetrapods and is ignored (no side pressure, a light 0.3 rev/s wind that cannot horse
        /// it out), sent back each run: the line rubs on the tetrapods until it wears through (cause Abrasion). (On the lake
        /// the boat's hull, rough 0.6, no longer wears nylon through before the fish tires: the wear rate was halved.)
        /// </summary>
        IEnumerator ObRubBreak()
        {
            yield return GoStage("sea", 3f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                OCheck("rub_break", false, "no sea scene");
                yield break;
            }
            var tideWas = GameClock.TidePhase;
            GameClock.TidePhase = 0.5f;
            yield return ToReady(ctl);
            SteerGear("rod_carbon", "reel_highgear", "line_pe3");
            EquipTest("bait_minnow", ctl);
            int runs0 = ctl.CoverRuns, holds0 = ctl.CoverHolds, p0 = ctl.PullOuts;
            yield return CoverFight(ctl, "black_porgy", 50f, 5151, new Vector3(4f, -2.5f, 12.5f), "tet32.cover", false, 0.3f, "obst_rub", 0.5f, "rub_break");
            bool broke = ctl.State != FishingController.S.Fighting && ctl.LastSnapCause == FightModel.Cause.Abrasion;
            if (broke) yield return NamedShot("obst_break");
            OCheck("rub_break", ctl.CoverRuns > runs0 && ctl.RubbedThisFight && broke && ctl.PullOuts == p0,
                string.Format(CIb, "{0} on {1}: cover runs {2} ({3}), holds {4}, rubbed {5} on {6}, abrasion {7:0.00}, break cause {8} ({9}), pulled out {10}",
                    Game.I.Line.id, ctl.Stage.Def.id, ctl.CoverRuns - runs0, ctl.LastCover != null ? ctl.LastCover.id : "-", ctl.CoverHolds - holds0, ctl.RubbedThisFight,
                    ctl.LastBreakName, ctl.LastAbrasion, ctl.LastSnapCause, ctl.State, ctl.PullOuts - p0));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(2f);
            yield return ToReady(ctl);
            GameClock.TidePhase = tideWas;
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
            float pairCover = 0f, pairOpen = 0f;
            int pairN = 0;
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
                // paired (deterministic): each bass in this same frame, the hook moved to the other spot and back
                if (k == 0)
                {
                    var keep = ctl.Tackle.Surface;
                    foreach (var f in ctl.Spawner.Fish)
                    {
                        ctl.Tackle.Surface = spots[0].Item2;
                        float mc = ctl.BiteMult(f);
                        ctl.Tackle.Surface = spots[1].Item2;
                        float mo = ctl.BiteMult(f);
                        ctl.Tackle.Surface = keep;
                        pairCover += mc;
                        pairOpen += mo;
                        pairN++;
                    }
                }
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
            // (the same fish, the same frame, only the hook moved: the per-spot means above also mix in which fish are near the
            // natural-entry bonus and how the float drifts, so they vary between runs)
            float pc = pairN > 0 ? pairCover / pairN : 0f, po = pairN > 0 ? pairOpen / pairN : 0f;
            OCheck("bites_structure", pairN > 0 && po > 0f && pc / po >= 1.3f,
                string.Format(CIb, "bite mult with the hook in weedbed.cover {0:0.00} vs open water {1:0.00} (x{2:0.00}), the same {3} bass in the same frame (per-spot means {4:0.00} / {5:0.00})",
                    pc, po, po > 0f ? pc / po : 0f, pairN, mult[0], mult[1]));
            // the live rolls: the game rolled approaches at both spots and the bass came to the float by the cover; which spot
            // got more in 75 s is statistical (logged: every let-go fish waits 3-6 s to come again, the counts saturate)
            OCheck("bites_observed", rolls[0] > 0 && rolls[1] > 0 && appr[0] > 0,
                string.Format(CIb, "approaches near cover {0} ({1} rolls) vs open water {2} ({3} rolls) in 75 s each (the comparison statistical, not checked)", appr[0], rolls[0], appr[1], rolls[1]));
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
