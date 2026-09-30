using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto zoom -fkzoommode off|125|150|active: 설정 → 캐스팅 후 줌인 (<see cref="ZoomMode"/>). The settings row (picked
    /// through it, its layout checked, shot zoom_settings), the save's default, and per mode: 150 the step falling back to
    /// fit (<see cref="ZoomWide"/>), 125 the setting changed mid-wait (<see cref="ZoomLiveSwitch"/>), active
    /// (<see cref="ZoomActiveRun"/>), off (<see cref="ZoomOffRun"/>). See AutoPilot.Zoom.cs.
    /// </summary>
    public partial class AutoPilot
    {
        static readonly string[] ZoomChoiceNames = { "끔", "1.25배", "1.5배", "액티브" };
        static readonly ZoomMode[] ZoomChoiceModes = { ZoomMode.Off, ZoomMode.X125, ZoomMode.X150, ZoomMode.Active };

        static Rect ScreenRectOf(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);   // (overlay canvases: screen px)
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        static bool Holds(Rect outer, Rect inner, float tol = 0.5f) =>
            inner.xMin >= outer.xMin - tol && inner.yMin >= outer.yMin - tol && inner.xMax <= outer.xMax + tol && inner.yMax <= outer.yMax + tol;

        /// <summary>The whole screen px per game px of the 1.25x step (the default, 액티브's, 1.5배's smallest).</summary>
        static int Step125(ViewZoom z) => Mathf.Max(Mathf.RoundToInt(z.BaseScale.y * ViewZoom.Aim), Mathf.FloorToInt(z.BaseScale.y + 1e-3f) + 1);

        // ------------------------------------------------------------------ the settings row
        /// <summary>
        /// The save (an old one without the field and a new game read 1.25배; the field round-trips), then 설정: the row
        /// "캐스팅 후 줌인" with its four choices, 1.25배 lit on this fresh save; the panel inside the screen, the row inside
        /// the panel, the label clear of the choices, every choice's text inside its button, no other row's button over
        /// them; <paramref name="mode"/> picked by pressing its choice (saved, read by the fishing view, the only one lit).
        /// </summary>
        IEnumerator ZoomSettings(FishingController ctl, ZoomMode mode)
        {
            var old = JsonUtility.FromJson<SaveData>("{\"version\":1,\"coins\":123,\"soundOn\":true,\"reelRing\":true,\"reelReverse\":false}");
            var trip = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(new SaveData { zoomMode = (int)ZoomMode.X150 }));
            var fresh = SaveData.NewGame();
            ZCheck("save_default", old != null && (ZoomMode)old.zoomMode == ZoomMode.X125 && (ZoomMode)fresh.zoomMode == ZoomMode.X125
                                   && trip != null && (ZoomMode)trip.zoomMode == ZoomMode.X150 && (ZoomMode)Game.Data.zoomMode == ZoomMode.X125,
                $"an old save {(old != null ? ((ZoomMode)old.zoomMode).ToString() : "-")}, a new game {(ZoomMode)fresh.zoomMode}, round trip of 1.5배 " +
                $"{(trip != null ? ((ZoomMode)trip.zoomMode).ToString() : "-")}, this (fresh) save {(ZoomMode)Game.Data.zoomMode}");
            SettingsUI.Open();
            yield return new WaitForSeconds(0.6f);   // (the window's pop)
            var all = FindObjectsByType<Button>(FindObjectsSortMode.None);
            var btns = ZoomChoiceNames.Select(n => all.FirstOrDefault(x => x.name == "Choice_" + n)).ToArray();
            var label = FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(t => t.text == "캐스팅 후 줌인");
            bool found = btns.All(x => x != null) && label != null;
            var blue = Art.UI("btn_blue");
            string Lit() => string.Join(",", Enumerable.Range(0, btns.Length).Where(k => btns[k] != null && btns[k].GetComponent<Image>().sprite == blue).Select(k => ZoomChoiceNames[k]));
            string lit0 = found ? Lit() : "-";
            ZCheck("settings_row", found && lit0 == "1.25배",
                $"label {label != null}, choices {string.Join(" / ", ZoomChoiceNames.Select((n, k) => n + (btns[k] != null ? "" : " (missing)")))}, lit on the fresh save: {lit0}");
            if (!found)
            {
                Click("닫기");
                yield break;
            }
            var win = (RectTransform)btns[0].transform.parent;
            var winR = ScreenRectOf(win);
            var scr = new Rect(0f, 0f, Screen.width, Screen.height);
            float s = label.canvas.scaleFactor;
            var labelR = ScreenRectOf(label.rectTransform);
            float labelRight = labelR.xMin + label.preferredWidth * s;
            var bR = btns.Select(x => ScreenRectOf((RectTransform)x.transform)).ToArray();
            bool inside = Holds(scr, winR) && Holds(winR, labelR) && bR.All(r => Holds(winR, r));
            bool clear = labelRight <= bR.Min(r => r.xMin) - 4f && Enumerable.Range(1, bR.Length - 1).All(k => bR[k].xMin >= bR[k - 1].xMax);
            var texts = btns.Select(x => x.GetComponentInChildren<Text>()).ToArray();
            bool textIn = texts.All(t => t != null && t.preferredWidth * s <= ScreenRectOf(t.rectTransform).width + 0.5f);
            var others = all.Where(x => x != null && x.transform.parent == win && !x.name.StartsWith("Choice_")).Select(x => ScreenRectOf((RectTransform)x.transform)).ToArray();
            bool apart = others.Length >= 4 && others.All(o => bR.All(r => !o.Overlaps(r)));
            ZCheck("settings_fits", inside && clear && textIn && apart,
                string.Format(CIz, "screen {0}x{1}, canvas scale {2:0.00}: panel ({3:0},{4:0} {5:0}x{6:0}); label right {7:0} vs the first choice {8:0}; texts {9}; " +
                                   "inside {10}, clear {11}, texts in {12}, apart from the other {13} buttons {14}",
                    Screen.width, Screen.height, s, winR.x, winR.y, winR.width, winR.height, labelRight, bR.Min(r => r.xMin),
                    string.Join(", ", texts.Select(t => string.Format(CIz, "{0} {1:0}/{2:0}", t.text, t.preferredWidth * s, ScreenRectOf(t.rectTransform).width))),
                    inside, clear, textIn, others.Length, apart));
            // the mode under test, picked through the row
            int idx = Array.IndexOf(ZoomChoiceModes, mode);
            btns[idx].onClick.Invoke();
            yield return null;
            string lit1 = Lit();
            ZCheck("settings_pick", (ZoomMode)Game.Data.zoomMode == mode && FishingController.ZoomSetting == mode && lit1 == ZoomChoiceNames[idx],
                $"pressed {ZoomChoiceNames[idx]}: saved {(ZoomMode)Game.Data.zoomMode}, the fishing view reads {FishingController.ZoomSetting}, lit {lit1}");
            yield return new WaitForSeconds(0.2f);
            yield return ZShot(ctl, "settings");
            Click("닫기");
            yield return new WaitForSeconds(0.4f);
        }

        // ------------------------------------------------------------------ 125: the setting changed mid-wait
        /// <summary>
        /// Waiting zoomed at 1.25x: 1.5배 (the step eases up to the wide one at once, 0.6 s), 끔 (out at once), 액티브 (he
        /// waits: stays at 1x), 1.25배 (back in, 0.6 s, the 1.25x step again).
        /// </summary>
        IEnumerator ZoomLiveSwitch(FishingController ctl)
        {
            var z = ZoomNow;
            FishingController.NoBites = true;
            // (no snag while it waits through the switches: a drifting float may catch the lake's weed)
            float snag = Obstacles.SnagMult;
            Obstacles.SnagMult = 0f;
            EquipTest("bait_worm", ctl);
            yield return ZoomPlace(ctl, new Vector3(ctl.Angler.X + 1f, 0f, 14f), "switch");
            if (ctl.State != FishingController.S.Waiting)
            {
                Obstacles.SnagMult = snag;
                yield return ToReady(ctl);
                yield break;
            }
            int s0 = z.StepPx.y;
            Game.I.SetZoomMode(ZoomMode.X150);
            float t0 = Time.time, reach = -1f;
            int easing = 0;
            while (Time.time - t0 < 1.5f)
            {
                yield return new WaitForEndOfFrame();
                if (z.StepEasing) easing++;
                if (z.StepPx.y == z.StepPxAsked && z.StepPx.y > s0 && !z.StepEasing && z.Level >= 1f)
                {
                    reach = Time.time - t0;
                    break;
                }
            }
            ZCheck("switch_150", reach >= 0.3f && reach <= 0.75f && easing > 5 && z.PixelExact && InCrop(z, ctl.RodTip2D, 2f) && InCrop(z, ctl.RigShown2D, 2f),
                $"1.5배 while waiting at {s0} px: the step {z.StepPx.y} (asked {z.StepPxAsked}) at rest {Z2(reach)} s later, {easing} frames easing; {ZDesc(z)}");
            Game.I.SetZoomMode(ZoomMode.Off);
            yield return ZoomOutCheck(ctl, "switch_off");
            Game.I.SetZoomMode(ZoomMode.Active);
            int frames = 0, zoomed = 0;
            for (float w = 0f; w < 1f; w += Time.deltaTime)
            {
                yield return new WaitForEndOfFrame();
                frames++;
                if (z.Level > 0f || z.ZoomedIn) zoomed++;
            }
            ZCheck("switch_active_1x", frames > 20 && zoomed == 0 && ctl.State == FishingController.S.Waiting, $"액티브 while waiting: {frames} frames, {zoomed} zoomed; state {ctl.State}");
            Game.I.SetZoomMode(ZoomMode.X125);
            float took = -1f, wall = -1f;
            yield return WaitStep(2f, (a, b) => { took = a; wall = b; });
            yield return new WaitForEndOfFrame();
            ZCheck("switch_125_in", took >= 0.5f && took <= 0.75f && z.StepPx.y == s0 && z.PixelExact,
                $"1.25배 again: the step {z.StepPx.y} (was {s0}) {Z2(took)} s later (wall {Z2(wall)} s); {ZDesc(z)}");
            Obstacles.SnagMult = snag;
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ 150: falling back to the largest step that fits
        /// <summary>
        /// 1.5배: a carp held beside the rig (the wide step at rest), then drawn in towards the angler at 2.5 m/s (and out to
        /// the side if need be) until the wide frame no longer holds it with the rod tip: down to the largest step that does
        /// within <see cref="ViewZoom.StepDownTime"/> (+ a frame), the fish and the rod tip in frame every frame; held there
        /// 1.8 s (at rest, pixel exact, one change); then back to the rig: the wide step again only after it has held them
        /// with room to spare for <see cref="ViewZoom.StepUpHold"/> (and its 0.6 s ease). Shot zoom_wide_fallback.
        /// </summary>
        IEnumerator ZoomWide(FishingController ctl)
        {
            var z = ZoomNow;
            var L = ctl.Stage.L;
            FishingController.NoBites = true;
            EquipTest("bait_paste", ctl);
            var at = new Vector3(ctl.Angler.X + 2f, 0f, 14f);
            yield return ZoomPlace(ctl, at, "wide");
            if (ctl.State != FishingController.S.Waiting || !ctl.DebugHook(GameDatabase.GetFish("carp"), 50f, 99, new Vector3(at.x, -1.2f, at.z)))
            {
                ZCheck("wide_hook", false, $"state {ctl.State}");
                yield return ToReady(ctl);
                yield break;
            }
            ctl.Fight.Hold(9999f);
            var home = new Vector3(at.x, -1f, at.z);
            ctl.DebugFishHold = home;
            int asked = z.StepPxAsked, floor = Step125(z);
            yield return new WaitForSeconds(1.6f);
            yield return new WaitForEndOfFrame();
            ZCheck("wide_fight_rest", ctl.State == FishingController.S.Fighting && ctl.Hooked != null && z.StepPx.y == asked && z.PixelExact
                                      && InCrop(z, ctl.Fish2D(ctl.Hooked), 0f) && InCrop(z, ctl.RodTip2D, 0f),
                $"the fish held by the rig: step {z.StepPx.y} (asked {asked}, fits {z.FitStep}) fish {(ctl.Hooked != null ? ZV(z.WorldToPx(ctl.Fish2D(ctl.Hooked))) : "-")} tip {ZV(z.WorldToPx(ctl.RodTip2D))}; {ZDesc(z)}");
            // drawn in (then out to the side) until the wide frame no longer holds it
            int frames = 0, fishOut = 0, tipOut = 0, border = 0, changes = 0, last = z.StepPx.y;
            float dropT = -1f, restAfter = -1f, t0 = Time.time;
            var p = home;
            float zNear = L.zNear + 0.6f, side = Mathf.Sign(at.x - ctl.Angler.X + 1e-3f);
            string atDrop = "-";
            while (ctl.State == FishingController.S.Fighting && ctl.Hooked != null && Time.time - t0 < 14f)
            {
                float dt = Time.deltaTime;
                if (dropT < 0f)
                {
                    if (p.z > zNear) p.z = Mathf.Max(zNear, p.z - 2.5f * dt);
                    else if (Mathf.Abs(p.x) < L.xLim - 0.6f) p.x += side * 2.5f * dt;
                    else break;
                }
                ctl.DebugFishHold = p;
                yield return new WaitForEndOfFrame();
                if (ctl.Hooked == null) break;
                frames++;
                if (!InCrop(z, ctl.Fish2D(ctl.Hooked), 0f)) fishOut++;
                if (!InCrop(z, ctl.RodTip2D, 0f)) tipOut++;
                if (!CropInside(z)) border++;
                if (z.StepPx.y != last)
                {
                    changes++;
                    last = z.StepPx.y;
                }
                if (dropT < 0f && z.FitStep < asked)
                {
                    dropT = Time.time;
                    atDrop = string.Format(CIz, "fish at ({0:0.00}, {1:0.00}, {2:0.00}) m px {3}, tip px {4}, fits {5}", p.x, p.y, p.z,
                        ZV(z.WorldToPx(ctl.Fish2D(ctl.Hooked))), ZV(z.WorldToPx(ctl.RodTip2D)), z.FitStep);
                }
                if (dropT >= 0f && restAfter < 0f && z.StepPx.y < asked && !z.StepEasing) restAfter = Time.time - dropT;
                if (dropT >= 0f && Time.time - dropT >= 1.8f) break;
            }
            yield return new WaitForEndOfFrame();
            bool held = ctl.State == FishingController.S.Fighting && ctl.Hooked != null;
            string fishPx = held ? ZV(z.WorldToPx(ctl.Fish2D(ctl.Hooked))) : "-";
            ZCheck("wide_fallback", held && dropT >= 0f && restAfter >= 0f && restAfter <= ViewZoom.StepDownTime + 0.1f && z.StepPx.y < asked && z.StepPx.y >= floor
                                    && z.StepPx.y == z.FitStep && z.PixelExact && fishOut == 0 && tipOut == 0 && border == 0 && changes == 1,
                $"the wide frame stopped holding them ({atDrop}): down to {z.StepPx.y} px (the largest that fits: {z.FitStep}, 1.25x's {floor}) at rest {Z2(restAfter)} s later; " +
                $"{frames} frames: fish out {fishOut}, tip out {tipOut}, crop outside the target {border}, step changes {changes}; fish {fishPx}; {ZDesc(z)}");
            if (held) yield return ZShot(ctl, "wide_fallback");
            // back to the rig: the wide step again once it has held them with room to spare for StepUpHold
            frames = fishOut = tipOut = border = changes = 0;
            float back = -1f, fitAgain = -1f, upAt = -1f;
            t0 = Time.time;
            while (ctl.State == FishingController.S.Fighting && ctl.Hooked != null && Time.time - t0 < 10f)
            {
                p = Vector3.MoveTowards(p, home, 2.5f * Time.deltaTime);
                ctl.DebugFishHold = p;
                yield return new WaitForEndOfFrame();
                if (ctl.Hooked == null) break;
                frames++;
                if (!InCrop(z, ctl.Fish2D(ctl.Hooked), 0f)) fishOut++;
                if (!InCrop(z, ctl.RodTip2D, 0f)) tipOut++;
                if (!CropInside(z)) border++;
                if (z.StepPx.y != last)
                {
                    changes++;
                    last = z.StepPx.y;
                }
                if (back < 0f && p == home) back = Time.time - t0;
                if (fitAgain < 0f && z.FitStep >= asked) fitAgain = Time.time - t0;
                if (z.StepPx.y == asked && !z.StepEasing && z.Level >= 1f)
                {
                    upAt = Time.time - t0;
                    break;
                }
            }
            ZCheck("wide_back_up", upAt >= 0f && fitAgain >= 0f && upAt - fitAgain >= ViewZoom.StepUpHold - 0.05f && upAt - fitAgain <= ViewZoom.StepUpHold + ViewZoom.EaseTime + 1.5f
                                   && z.PixelExact && fishOut == 0 && tipOut == 0 && border == 0 && changes == 1,
                $"back by the rig {Z2(back)} s, the wide frame held it again {Z2(fitAgain)} s, the wide step at rest {Z2(upAt)} s (hold {ViewZoom.StepUpHold} s + ease {ViewZoom.EaseTime} s); " +
                $"{frames} frames: fish out {fishOut}, tip out {tipOut}, crop outside {border}, step changes {changes}; {ZDesc(z)}");
            ctl.DebugFishHold = null;
            ctl.DebugRelease();
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ active
        /// <summary>
        /// 액티브: at the ready and through a real cast, its landing and the wait (nibbles too) at 1x; a natural bite: in
        /// within 0.35-0.4 s on the float with the rod tip in frame (shot zoom_active_bite); the hook set: in through the
        /// fight (following the fish), out on the landing; a missed float bite and a missed lure bite: out again; the legend
        /// encounter at 1x, in for its fight; a snag on the stream at 1x. Shots zoom_active_wait, zoom_active_bite.
        /// </summary>
        IEnumerator ZoomActiveRun(FishingController ctl)
        {
            var z = ZoomNow;
            FishingController.NoBites = true;
            yield return ToReady(ctl);
            ctl.Angler.DebugPlace(0f);
            EquipTest("bait_worm", ctl);
            yield return new WaitForSeconds(0.8f);
            ZCheck("ready_1x", z.Level == 0f && !z.ZoomedIn && z.UV == new Rect(0f, 0f, 1f, 1f), ZDesc(z));
            int frames = 0, zoomed = 0;
            bool watching = true;
            IEnumerator WatchWait()
            {
                while (watching)
                {
                    yield return new WaitForEndOfFrame();
                    frames++;
                    if (z.Level > 0f || z.ZoomedIn) zoomed++;
                }
            }
            StartCoroutine(WatchWait());
            yield return Cast(ctl, 3.4f, 0f);
            for (float w = 0f; w < 6f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
            if (ctl.State != FishingController.S.Waiting)
            {
                watching = false;
                ZCheck("active_cast_landed", false, $"state {ctl.State}");
                yield return ToReady(ctl);
                yield break;
            }
            yield return new WaitForSeconds(2f);
            watching = false;
            yield return null;
            ZCheck("active_land_wait_1x", frames > 60 && zoomed == 0, $"{frames} frames from the wind-up through the landing and 2 s of waiting: {zoomed} zoomed; {ZDesc(z)}");
            yield return ZShot(ctl, "active_wait");
            // a natural bite (nibbles first: still 1x), then in quickly on the float
            FishingController.NoBites = false;
            frames = zoomed = 0;
            float t = 0f;
            while (ctl.State == FishingController.S.Waiting && t < 60f)
            {
                t += Time.deltaTime;
                frames++;
                if (z.Level > 0f || z.ZoomedIn) zoomed++;
                yield return null;
            }
            FishingController.NoBites = true;
            ZCheck("active_wait_1x", frames > 30 && zoomed == 0, $"{frames} frames waiting for the bite ({Z2(t)} s, nibbles too): {zoomed} zoomed");
            if (ctl.State != FishingController.S.Biting)
            {
                ZCheck("active_bite", false, $"no bite in {Z2(t)} s (state {ctl.State})");
                yield return ToReady(ctl);
            }
            else
            {
                yield return ActiveBiteIn(ctl, "active_bite", "active_bite");
                // the hook set: kept in through the fight, following the fish; out on the landing
                if (ctl.State == FishingController.S.Biting)
                {
                    yield return Tap(Scr(0.5f, 0.6f));
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                ZCheck("active_hooked_zoomed", ctl.State == FishingController.S.Fighting && z.Level >= 1f && z.ZoomedIn, $"state {ctl.State}; {ZDesc(z)}");
                if (ctl.State == FishingController.S.Fighting)
                {
                    var st = new ZFightStats();
                    yield return ZFight(ctl, "active_fight", 5f, st, 2.5f);
                    ZFightChecks("active_fight", st, false);
                    if (ctl.State == FishingController.S.Fighting) yield return ZoomLanding(ctl, "active_");
                    else
                    {
                        ZCheck("active_fight_land", false, $"the fight ended early ({ctl.State})");
                        yield return ToReady(ctl);
                    }
                }
                else yield return ToReady(ctl);
            }
            // bites that end without a hook: out again
            yield return ActiveMiss(ctl, "bait_worm", "active_miss_float");
            yield return ActiveMiss(ctl, "bait_spoon", "active_miss_lure");
            EquipTest("bait_worm", ctl);
            yield return ToReady(ctl);
            // the legend encounter: 1x through it, in for the fight it hooks
            yield return ZoomEncounter(ctl);
            // a snag on the stream: it stays at the zoom it found (1x, from the wait)
            yield return GoStage("stream", 3f);
            var c2 = FindAnyObjectByType<FishingController>();
            if (c2 == null || c2.Stage.Def.id != "stream")
            {
                ZCheck("stream_scene", false, "no stream");
                yield break;
            }
            c2.Watch?.DebugLurk(null);
            SteerGear("rod_carbon", "reel_highgear", "line_nylon4");
            yield return ToReady(c2);
            yield return ZoomSnag(c2);
            yield return ToReady(c2);
        }

        /// <summary>
        /// 액티브, a bite just begun (at 1x): the 1.25x step within 0.35-0.4 s (on the ease's own clock; the wall clock from
        /// the bite's frame no more than 0.4 s), then at rest on the float / lure with the rod tip in frame.
        /// </summary>
        IEnumerator ActiveBiteIn(FishingController ctl, string tag, string shot)
        {
            var z = ZoomNow;
            float lvl0 = z.Level;
            var st0 = ctl.State;
            float took = -1f, wall = -1f;
            yield return WaitStep(1f, (a, b) => { took = a; wall = b; });
            var rig = z.WorldToPx(ctl.RigShown2D);
            ZCheck(tag + "_zoom_in", st0 == FishingController.S.Biting && lvl0 == 0f && took >= 0.33f && took <= 0.4f && wall >= 0f && wall <= 0.4f,
                string.Format(CIz, "{0} at level {1:0.00}: the step {2:0.000} s on the ease's clock ({3:0.000} s on the wall clock from the bite's frame; asked {4} s); now {5}",
                    st0, lvl0, took, wall, FishingController.ZoomBiteIn, ctl.State));
            ZCheck(tag + "_frame", z.Level >= 1f && z.PixelExact && z.StepPx.y == Step125(z) && InCrop(z, ctl.RigShown2D, 2f) && InCrop(z, ctl.RodTip2D, 2f),
                string.Format(CIz, "{0}: float {1} tip {2}, the crop's centre {3} ({4:0.0} px from the float); {5}", ctl.State, ZV(rig), ZV(z.WorldToPx(ctl.RodTip2D)),
                    ZV(z.PanPx), (z.PanPx - rig).magnitude, ZDesc(z)));
            if (shot != null) yield return ZShot(ctl, shot);
        }

        /// <summary>
        /// 액티브: a bite on <paramref name="bait"/> (a float bait or a lure placed in front of him, 1x while he waits) that
        /// is never struck: in at the bite, and as it runs out (a float rig is wound in; a lure back to the wait) out to 1x
        /// again within 0.75 s; a lure stays at 1x as the wait goes on.
        /// </summary>
        IEnumerator ActiveMiss(FishingController ctl, string bait, string tag)
        {
            var z = ZoomNow;
            FishingController.NoBites = true;
            EquipTest(bait, ctl);
            yield return ToReady(ctl);
            if (!ctl.DebugPlaceRig(new Vector3(ctl.Angler.X + 1f, 0f, 13f)))
            {
                ZCheck(tag, false, $"state {ctl.State}");
                yield break;
            }
            int frames = 0, zoomed = 0;
            for (float w = 0f; w < 1.2f; w += Time.deltaTime)
            {
                yield return new WaitForEndOfFrame();
                frames++;
                if (z.Level > 0f || z.ZoomedIn) zoomed++;
            }
            var fish = ctl.Spawner.Fish.FirstOrDefault(f => f != null && f.Sp.encounter == null);
            ZCheck(tag + "_wait_1x", ctl.State == FishingController.S.Waiting && fish != null && zoomed == 0, $"{frames} frames waiting, {zoomed} zoomed; state {ctl.State}");
            if (ctl.State != FishingController.S.Waiting || fish == null)
            {
                yield return ToReady(ctl);
                yield break;
            }
            PointerInput.SimDown = false;
            ctl.OnBite(fish);
            yield return ActiveBiteIn(ctl, tag, null);
            for (float w = 0f; w < 3f && ctl.State == FishingController.S.Biting; w += Time.deltaTime) yield return null;
            var after = ctl.State;
            yield return ZoomOutCheck(ctl, tag);
            bool lure = GameDatabase.GetItem<BaitDef>(bait).isLure;
            ZCheck(tag + "_after", lure ? after == FishingController.S.Waiting : after == FishingController.S.Retrieving || after == FishingController.S.Ready,
                $"the bite ran out unstruck: {after} ({(lure ? "a lure: back to the wait" : "a float bait: wound in")})");
            if (lure && ctl.State == FishingController.S.Waiting)
            {
                frames = zoomed = 0;
                for (float w = 0f; w < 1.5f && ctl.State == FishingController.S.Waiting; w += Time.deltaTime)
                {
                    yield return new WaitForEndOfFrame();
                    frames++;
                    if (z.Level > 0f || z.ZoomedIn) zoomed++;
                }
                ZCheck(tag + "_then_1x", frames > 20 && zoomed == 0, $"the wait goes on: {frames} frames, {zoomed} zoomed");
            }
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ off
        /// <summary>
        /// 끔: a real cast, the wait, a natural bite, the hook set, a fight (shot zoom_off_fight_1), the landing; a running
        /// carp on a placed rig, the line broken; a snag on the stream: not one zoomed frame (the level, the target and the
        /// display's crop, every frame of the run).
        /// </summary>
        IEnumerator ZoomOffRun(FishingController ctl)
        {
            var full = new Rect(0f, 0f, 1f, 1f);
            int frames = 0, zoomed = 0;
            string first = null;
            bool mon = true;
            IEnumerator Monitor()
            {
                while (mon)
                {
                    yield return new WaitForEndOfFrame();
                    var zz = ZoomNow;
                    if (zz == null) continue;
                    frames++;
                    if (zz.Level > 0f || zz.ZoomedIn || zz.UV != full)
                    {
                        if (zoomed++ == 0) first = ZDesc(zz);
                    }
                }
            }
            StartCoroutine(Monitor());
            FishingController.NoBites = true;
            yield return ToReady(ctl);
            ctl.Angler.DebugPlace(0f);
            EquipTest("bait_worm", ctl);
            yield return new WaitForSeconds(0.5f);
            yield return Cast(ctl, 3.4f, 0f);
            for (float w = 0f; w < 6f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
            if (ctl.State == FishingController.S.Waiting)
            {
                yield return new WaitForSeconds(1.5f);
                yield return ZShot(ctl, "off_wait");
                FishingController.NoBites = false;
                float t = 0f;
                while (ctl.State == FishingController.S.Waiting && t < 60f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                FishingController.NoBites = true;
                if (ctl.State == FishingController.S.Biting)
                {
                    yield return new WaitForSeconds(0.15f);
                    yield return ZShot(ctl, "off_bite");
                    yield return Tap(Scr(0.5f, 0.6f));
                    yield return null;
                }
                ZCheck("off_hooked", ctl.State == FishingController.S.Fighting, $"a natural bite after {Z2(t)} s struck: {ctl.State}");
                if (ctl.State == FishingController.S.Fighting)
                {
                    var st = new ZFightStats();
                    yield return ZFight(ctl, "off_fight", 4f, st, 2f);
                    ZCheck("off_fight_1x", st.frames > 30 && st.notStep == st.frames, $"{st.frames} fight frames, {st.notStep} at 1x");
                    if (ctl.State == FishingController.S.Fighting) yield return ZoomLanding(ctl, "off_");
                }
            }
            else ZCheck("off_cast_landed", false, $"state {ctl.State}");
            yield return ToReady(ctl);
            // a running carp on a placed rig (no follow), the line broken
            EquipTest("bait_paste", ctl);
            yield return ToReady(ctl);
            var at = new Vector3(ctl.Angler.X + 1f, 0f, 16f);
            if (ctl.DebugPlaceRig(at))
            {
                yield return new WaitForSeconds(1f);
                if (ctl.DebugHook(GameDatabase.GetFish("carp"), 70f, 4242, new Vector3(at.x, -1.2f, at.z)))
                {
                    var st = new ZFightStats();
                    yield return ZFight(ctl, "off_run", 5f, st);
                    ZCheck("off_run_1x", st.frames > 30 && st.notStep == st.frames, $"{st.frames} fight frames, {st.notStep} at 1x, pan range {ZV(st.max - st.min)}");
                    ctl.DebugBreak();
                    yield return new WaitForSeconds(0.5f);
                }
            }
            yield return ToReady(ctl);
            // a snag on the stream
            yield return GoStage("stream", 3f);
            var c2 = FindAnyObjectByType<FishingController>();
            if (c2 != null && c2.Stage.Def.id == "stream")
            {
                c2.Watch?.DebugLurk(null);
                SteerGear("rod_carbon", "reel_highgear", "line_nylon4");
                yield return ToReady(c2);
                yield return ZoomSnag(c2);
                yield return ToReady(c2);
            }
            else ZCheck("stream_scene", false, "no stream");
            mon = false;
            yield return null;
            ZCheck("off_never_zoomed", frames > 600 && zoomed == 0, $"{frames} frames through the run: {zoomed} zoomed{(first != null ? " (first: " + first + ")" : "")}");
        }
    }
}
