using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto breaks (Docs/testing.md): every way a fish comes off the line, forced one by one on the lake (test hooks:
    /// DebugHook / DebugPartLine / DebugSnag / DebugRelease, FishAgent.ForceBite), each checked on the screen state (the
    /// rig, the drawn hook, the line-snap whip, the loss toast) and the inventory. [BREAK] CHECK &lt;name&gt; PASS|FAIL lines.
    /// <code>
    /// -fkfresh -fkrich -fkgear -fksave lb_breaks -fkscene Fishing -fkstage lake -fkauto breaks -fkobstlog -fkshots &lt;dir&gt;
    /// </code>
    /// Cases: a lure parted by the tension; a float rig hooked for real (the bite's hook set) and parted by the tension;
    /// a float rig whose spool empties; a rub above the float and one below it; a float rig snagged and forced until the
    /// line parts; the 끊기 button on a snagged lure; a fish shaking a lure off; the bait thief; a pad tearing the bait off;
    /// the spent retrieve after a break against an intact rig's (snag checks, bites); a bare hook / a spent rig freed from a snag
    /// and waiting again (no fish, no legend soak; an intact rig as the control); a float rig worn through on real structure
    /// (the lake's boat below the float, the ice's rim); a toast stacked under the loss toast; a legend's key lure parted on the
    /// legend. Shots: breaks_snap_mid (+ _zoom), breaks_toast_lure, breaks_barehook (+ _zoom), breaks_toast_float,
    /// breaks_lure_home (+ _zoom), breaks_toast_legend.
    /// </summary>
    public partial class AutoPilot
    {
        int brFails;
        static readonly CultureInfo CIk = CultureInfo.InvariantCulture;

        void BCheck(string name, bool ok, string numbers)
        {
            if (!ok) brFails++;
            Debug.Log($"[BREAK] CHECK {name} {(ok ? "PASS" : "FAIL")} {numbers}");
        }

        static string Fk(float v) => v.ToString("0.00", CIk);

        /// <summary>What a rig did on its way home after the line parted / the fish got off (see <see cref="WatchHome"/>).</summary>
        class RigWatch
        {
            public int frames, baitFrames, bareFrames, floatFrames, retrieving, snagChecks, bites, approaches, snagged;
            public float z0 = -1f, zEnd, secs;
            public bool home;
            public string Brief => string.Format(CIk, "{0} frames ({1} retrieving, {2:0.0} s): bait sprite {3}, bare hook {4}, float {5}; z {6:0.0} -> {7:0.0}, home {8}; snag checks {9}, snagged {10}, bites {11}, approaches {12}",
                frames, retrieving, secs, baitFrames, bareFrames, floatFrames, z0, zEnd, home, snagChecks, snagged, bites, approaches);
        }

        IEnumerator BreaksTest()
        {
            yield return new WaitForSeconds(2f);
            Obstacles.Rnd = new System.Random(20261001);
            Random.InitState(1001);
            Obstacles.Log = true;
            PointerInput.SimDpi = 0f;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            var sd = Game.Data;
            sd.sweepHint = sd.tideHint = sd.driftHint = sd.mendHint = sd.sideHint = sd.timeHint = sd.pinHint = true;
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Stage.Def.id != "lake")
            {
                yield return GoStage("lake", 3f);
                ctl = FindAnyObjectByType<FishingController>();
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            if (ctl == null)
            {
                BCheck("scene", false, "no lake scene");
                Application.Quit();
                yield break;
            }
            FishingController.NoBites = true;
            Log($"breaks test on {ctl.Stage.Def.id}: rod {Game.I.Rod.id} reel {Game.I.Reel.id} line {Game.I.Line.id} angler x {Fk(ctl.Angler.X)}");
            yield return BrLureTension(ctl);
            yield return BrFloatTension(ctl);
            yield return BrSpool(ctl);
            yield return BrRub(ctl, true);
            yield return BrRub(ctl, false);
            yield return BrSnagFloat(ctl);
            yield return BrCutLure(ctl);
            yield return BrShakeOff(ctl);
            yield return BrThief(ctl);
            yield return BrPadTear(ctl);
            yield return BrSpentRetrieve(ctl);
            yield return BrBareWaiting(ctl, false);
            yield return BrBareWaiting(ctl, true);
            yield return BrRealRub(ctl, "real_rub_lake", "largemouth_bass", 32f, new Vector3(-6f, -1.2f, 14f), "boat.cover", 6f, 7808, 6f, true);   // (the float set 6 m up: the hull wears the line below it)
            yield return BrLegendKey(ctl);
            yield return GoStage("ice", 3f);
            ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || !ctl.Stage.L.IsIce) BCheck("real_rub_ice", false, "no ice scene");
            else
            {
                var L = ctl.Stage.L;
                yield return BrRealRub(ctl, "real_rub_ice", "northern_pike", 70f, new Vector3(L.holeX, -3f, L.holeZ), null, 1.5f, 7909, 10f, false);
            }
            FishingController.NoBites = false;
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Debug.Log($"[BREAK] breaks test done: {brFails} failed");
            Application.Quit();
        }

        // ------------------------------------------------------------------ helpers
        Vector3 BrAt(FishingController ctl, float dx, float y, float z) => new Vector3(ctl.Angler.X + dx, y, z);

        /// <summary>Back to the ready, this bait on (a float at <paramref name="floatDepth"/>), its rig put on the water at <paramref name="at"/>.</summary>
        IEnumerator BrPlace(FishingController ctl, string bait, Vector3 at, float floatDepth = 2f)
        {
            yield return ToReady(ctl);
            EquipTest(bait, ctl);
            ctl.FloatDepthChanged(floatDepth);
            yield return null;
            ctl.DebugPlaceRig(new Vector3(at.x, 0f, at.z));
            yield return new WaitForSeconds(0.9f);
        }

        /// <summary>A fish of this species hooked at <paramref name="at"/> (its depth in y), then wound a moment.</summary>
        IEnumerator BrHook(FishingController ctl, string fishId, float cm, Vector3 at, int seed, float wind = 0.6f, float rps = 1.5f)
        {
            if (!ctl.DebugHook(GameDatabase.GetFish(fishId), cm, seed, at))
            {
                Log($"[BREAK] no hook ({ctl.State}, tackle {ctl.Tackle.State})");
                yield break;
            }
            yield return BrWind(ctl, wind, rps);
        }

        IEnumerator BrWind(FishingController ctl, float secs, float rps)
        {
            float windSign = CircleGesture.Reversed ? -1f : 1f;
            var centre = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.12f;
            PointerInput.SimActive = true;
            for (float t = 0f; t < secs && (ctl.State == FishingController.S.Fighting || ctl.State == FishingController.S.Snagged); t += Time.deltaTime)
            {
                circleAng -= windSign * Time.deltaTime * rps * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * r;
                yield return null;
            }
            PointerInput.SimDown = false;
            yield return null;
        }

        /// <summary>A wandering fish made to bite the waiting rig now (the real bite: OnBite, Biting). Null: no fish about.</summary>
        IEnumerator BrForceBite(FishingController ctl, System.Action<FishAgent> got)
        {
            FishAgent f = null;
            for (float w = 0f; w < 8f && f == null; w += Time.deltaTime)
            {
                f = ctl.Spawner.Fish.FirstOrDefault(x => x != null && x.State == FishAgent.St.Wander && x.Sp.encounter == null);
                if (f == null) yield return null;
            }
            if (f != null && ctl.State == FishingController.S.Waiting) f.ForceBite();
            got(f);
            yield return null;
        }

        /// <summary>Watches the rig on its way home until he is ready (each frame: the drawn hook / bait, the float, z).</summary>
        IEnumerator WatchHome(FishingController ctl, BaitDef bait, RigWatch w, float timeout = 30f, string shot = null, float shotAt = 1.0f)
        {
            var tk = ctl.Tackle;
            var baitSprite = Art.WorldBait(bait.id);
            int sc0 = ctl.SnagChecks, b0 = ctl.BiteCount, a0 = ctl.Approaches;
            bool shotDone = shot == null;
            float t = 0f;
            while (t < timeout && ctl.State != FishingController.S.Ready)
            {
                t += Time.deltaTime;
                w.frames++;
                var br = tk.BaitR;
                if (br.enabled && br.sprite == baitSprite) w.baitFrames++;
                if (br.enabled && br.sprite != null && br.sprite.name == "hook_bare_w") w.bareFrames++;
                if (tk.FloatR.enabled) w.floatFrames++;
                if (ctl.State == FishingController.S.Snagged)
                {
                    w.snagged++;
                    ctl.Retrieve();   // (끊기: the old code could catch the spent rig again)
                }
                if (ctl.State == FishingController.S.Retrieving)
                {
                    w.retrieving++;
                    if (w.z0 < 0f) w.z0 = tk.Surface.z;
                    w.zEnd = tk.Surface.z;
                }
                if (!shotDone && t >= shotAt && ctl.State == FishingController.S.Retrieving && tk.State == Tackle.Mode.Water)
                {
                    shotDone = true;
                    yield return NamedShot(shot);
                    yield return BrCrop(shot + "_zoom", ctl.Stage.P.To2D(ctl.Stage.P.Apparent(tk.HookPos)), 240, 160, 4);
                    continue;
                }
                yield return null;
            }
            w.secs = t;
            w.home = ctl.State == FishingController.S.Ready;
            w.snagChecks = ctl.SnagChecks - sc0;
            w.bites = ctl.BiteCount - b0;
            w.approaches = ctl.Approaches - a0;
        }

        /// <summary>A crop of the screen around a scene point, scaled up (nearest) for review.</summary>
        IEnumerator BrCrop(string name, Vector2 world2D, int w, int h, int scale)
        {
            yield return AutoShot.Frame();
            var pv = PixelView.Current;
            if (pv == null) yield break;
            var c = pv.WorldToScreen(world2D);
            var tex = AutoShot.Texture();
            w = Mathf.Min(w, tex.width);
            h = Mathf.Min(h, tex.height);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(c.x) - w / 2, 0, tex.width - w), y0 = Mathf.Clamp(Mathf.RoundToInt(c.y) - h / 2, 0, tex.height - h);
            var px = tex.GetPixels(x0, y0, w, h);
            var big = new Color[w * scale * h * scale];
            for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
                big[y * w * scale + x] = px[(y / scale) * w + x / scale];
            var outTex = new Texture2D(w * scale, h * scale, TextureFormat.RGB24, false);
            outTex.SetPixels(big);
            outTex.Apply(false);
            string p = Path.Combine(shots, name + ".png");
            File.WriteAllBytes(p, outTex.EncodeToPNG());
            Destroy(tex);
            Destroy(outTex);
            Log(string.Format(CIk, "shot {0} (crop {1}x{2} at screen ({3:0}, {4:0}) x{5})", p, w, h, c.x, c.y, scale));
            yield return null;
        }

        /// <summary>The loss toast: shown, its texts, its item icons, a gold (highlighted) text and the legend's eye.</summary>
        static void ToastParts(FishingController.LossReport r, out bool shown, out int icons, out bool gold, out bool eye)
        {
            shown = r != null && r.toast != null && r.toast.gameObject.activeInHierarchy;
            icons = 0;
            gold = eye = false;
            if (!shown) return;
            icons = r.toast.GetComponentsInChildren<Image>().Count(i => i.name == "Icon" && i.sprite != null);
            gold = r.toast.GetComponentsInChildren<Text>().Any(t => t.color == UIKit.Gold && r.lure != null && t.text.Contains(r.lure.name));
            eye = r.toast.GetComponentsInChildren<Image>().Any(i => i.name == "Badge" && i.sprite != null);
        }

        // ------------------------------------------------------------------ 1. a lure parted by the tension
        IEnumerator BrLureTension(FishingController ctl)
        {
            var lure = GameDatabase.GetItem<BaitDef>("bait_minnow");
            yield return BrPlace(ctl, lure.id, BrAt(ctl, 1.5f, 0f, 16f));
            yield return BrHook(ctl, "carp", 50f, BrAt(ctl, 1.5f, -1.5f, 16f), 7101);
            var snap = ctl.SnapFx;
            int plays0 = snap.Plays, breaks0 = ctl.Breaks;
            bool owned0 = Game.I.Owns(lure.id);
            bool parted = ctl.DebugPartLine("tension");
            var tk = ctl.Tackle;
            bool gone = ctl.State == FishingController.S.Ready && tk.State == Tackle.Mode.Hidden;
            // the whip a third of the way back to the tip
            while (snap.Active && snap.Progress < 0.33f) yield return null;
            if (snap.Active)
            {
                var mid = Vector2.Lerp(ctl.Stage.P.To2D(ctl.Angler.RodTip), ctl.Stage.P.To2D(snap.FreeEnd), 0.5f);
                Log(string.Format(CIk, "[BREAK] snap at {0:0.00} of the way: free end ({1:0.00}, {2:0.00}, {3:0.00})", snap.Progress, snap.FreeEnd.x, snap.FreeEnd.y, snap.FreeEnd.z));
                yield return NamedShot("breaks_snap_mid");
                yield return BrCrop("breaks_snap_mid_zoom", mid, 360, 240, 3);
            }
            for (float w = 0f; w < 1f && snap.Active; w += Time.deltaTime) yield return null;
            var r = ctl.LastLoss;
            bool afterWhip = r != null && r.toastPending;   // (the toast waits for the whip to be over)
            yield return new WaitForSeconds(0.3f);
            bool dangle = ctl.Angler.DangleR.enabled;
            yield return NamedShot("breaks_toast_lure");
            ToastParts(r, out bool shown, out int icons, out _, out _);
            BCheck("lure_tension", parted && gone && owned0 && !Game.I.Owns(lure.id) && Game.I.Bait.id == GameDatabase.StarterBait,
                $"parted {parted}, rig gone at once {gone} (state {ctl.State}, tackle {tk.State}), {lure.id} owned before {owned0} / after {Game.I.Owns(lure.id)}, equipped {Game.I.Bait.id}");
            BCheck("lure_tension_toast", ctl.Breaks == breaks0 + 1 && r != null && r.lure == lure && afterWhip && shown && icons >= 1 && r.items.Any(s => s.Contains(lure.name)),
                $"toast shown {shown} (still waiting while the line whipped back {afterWhip}) with {icons} icon(s): '{(r != null ? string.Join(" | ", r.items) : "-")}'");
            BCheck("lure_tension_snap", snap.Plays == plays0 + 1 && snap.LastKind == LineSnap.Kind.Home && snap.LastFrames >= 8 && !snap.Active && dangle,
                $"snaps {snap.Plays - plays0} ({snap.LastKind}), drawn {snap.LastFrames} frames over {LineSnap.Duration:0.00} s, the bait dangling at the tip after it {dangle}");
            yield return new WaitForSeconds(3.2f);   // (the toast gone before the next)
        }

        // ------------------------------------------------------------------ 2. a float rig hooked for real, parted by the tension
        IEnumerator BrFloatTension(FishingController ctl)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            yield return BrPlace(ctl, bait.id, BrAt(ctl, 1f, 0f, 15f));
            int n0 = Game.I.BaitCount(bait.id);
            FishAgent f = null;
            yield return BrForceBite(ctl, x => f = x);
            bool biting = ctl.State == FishingController.S.Biting;
            yield return new WaitForSeconds(0.15f);
            yield return Tap(Scr(0.5f, 0.6f));
            for (float w = 0f; w < 0.5f && ctl.State != FishingController.S.Fighting; w += Time.deltaTime) yield return null;
            bool hooked = ctl.State == FishingController.S.Fighting;
            int nHook = Game.I.BaitCount(bait.id);
            yield return BrWind(ctl, 0.6f, 1.5f);
            var snap = ctl.SnapFx;
            int plays0 = snap.Plays, breaks0 = ctl.Breaks;
            bool parted = ctl.DebugPartLine("tension");
            var tk = ctl.Tackle;
            bool kept = ctl.State == FishingController.S.Retrieving && tk.State == Tackle.Mode.Water && tk.BareHook && ctl.SpentRetrieve;
            var r = ctl.LastLoss;
            var w2 = new RigWatch();
            yield return WatchHome(ctl, bait, w2, 30f, "breaks_barehook", 1.1f);
            int n1 = Game.I.BaitCount(bait.id);
            Log($"[BREAK] float tension: {w2.Brief}");
            BCheck("float_tension_hook", f != null && biting && hooked && nHook == n0 - 1,
                $"fish {(f != null ? f.Sp.id : "none")} bit {biting}, hooked by the tap {hooked}, {bait.id} {n0} -> {nHook} at the hook set");
            bool noToast = r != null && r.toast == null && !r.toastPending && r.items.Count == 0;
            BCheck("float_tension_kept", parted && kept && r != null && r.off == FishingController.Off.AtHook && !r.floatLost && noToast && ctl.Breaks == breaks0 + 1,
                $"parted at the hook: float kept and wound in spent with the bare hook {kept}, off {(r != null ? r.off.ToString() : "-")}, nothing lost (no toast) {noToast}");
            BCheck("float_tension_home", w2.home && w2.floatFrames > 0 && w2.bareFrames > 0 && w2.baitFrames == 0 && w2.z0 > w2.zEnd + 5f,
                w2.Brief);
            BCheck("float_tension_bait", n1 == n0 - 1, $"{bait.id} {n0} -> {n1} for the bite (-1 at the hook set, nothing more at the break)");
            BCheck("float_tension_snap", snap.Plays == plays0 + 1 && snap.LastKind == LineSnap.Kind.Recoil && snap.LastFrames >= 8,
                $"snaps {snap.Plays - plays0} ({snap.LastKind}), drawn {snap.LastFrames} frames");
        }

        // ------------------------------------------------------------------ 3. a float rig's spool emptied
        IEnumerator BrSpool(FishingController ctl)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            yield return BrPlace(ctl, bait.id, BrAt(ctl, -1f, 0f, 15f));
            int n0 = Game.I.BaitCount(bait.id);
            yield return BrHook(ctl, "carp", 50f, BrAt(ctl, -1f, -1.5f, 15f), 7202);
            var snap = ctl.SnapFx;
            int plays0 = snap.Plays;
            bool parted = ctl.DebugPartLine("spool");
            var tk = ctl.Tackle;
            bool gone = ctl.State == FishingController.S.Ready && tk.State == Tackle.Mode.Hidden && !tk.FloatR.enabled;
            int retrieving = 0, floatShown = 0;
            bool shot = false;
            for (float w = 0f; w < 1.5f; w += Time.deltaTime)
            {
                if (ctl.State == FishingController.S.Retrieving) retrieving++;
                if (tk.FloatR.enabled) floatShown++;
                if (!shot && w >= 0.55f)
                {
                    shot = true;
                    yield return NamedShot("breaks_toast_float");
                    continue;
                }
                yield return null;
            }
            var r = ctl.LastLoss;
            ToastParts(r, out bool shown, out int icons, out _, out _);
            BCheck("spool", parted && gone && retrieving == 0 && floatShown == 0 && r != null && r.off == FishingController.Off.Above && r.floatLost,
                $"the whole rig hidden at once {gone} (state {ctl.State}), retrieving frames {retrieving}, float drawn {floatShown} frames after, off {(r != null ? r.off.ToString() : "-")}");
            BCheck("spool_toast", shown && icons >= 1 && r.items.Contains("찌") && Game.I.BaitCount(bait.id) == n0 && snap.Plays == plays0 + 1 && snap.LastKind == LineSnap.Kind.Home,
                $"toast '{(r != null ? string.Join(" | ", r.items) : "-")}' ({icons} icon(s)), {bait.id} {n0} -> {Game.I.BaitCount(bait.id)} (a test hook: no bite), snap {snap.LastKind}");
            // another toast while the loss toast is up (as the bait picker's refusal while the rig is wound in) goes under it
            var next = shown ? Toast.Show("지금은 미끼를 바꿀 수 없어요", null, 1.2f) : null;
            float lossBottom = shown ? r.toast.anchoredPosition.y - r.toast.sizeDelta.y : 0f;
            bool clear = next != null && next.anchoredPosition.y <= lossBottom - 1f;
            yield return new WaitForSeconds(0.3f);
            if (next != null) yield return NamedShot("breaks_toast_stack");
            BCheck("toast_stack", clear, string.Format(CIk, "loss toast y {0:0} .. {1:0}, the next toast's top at {2:0}",
                shown ? r.toast.anchoredPosition.y : 0f, lossBottom, next != null ? next.anchoredPosition.y : 0f));
            yield return new WaitForSeconds(2.2f);
        }

        // ------------------------------------------------------------------ 4. worn through above / below the float
        IEnumerator BrRub(FishingController ctl, bool above)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            string tag = above ? "rub_above" : "rub_below";
            var tk = ctl.Tackle;
            // a deep fish wound in against until the float is pulled under along the taut line, the line in the water (a
            // fish that came up first, worn out, or jumped: let go and hooked again, up to 3 times)
            bool under = false;
            int tries = 0;
            for (; tries < 3 && !under; tries++)
            {
                yield return BrPlace(ctl, bait.id, BrAt(ctl, 1f, 0f, 19f), 1.2f);
                yield return BrHook(ctl, "carp", 60f, BrAt(ctl, 1f, -4f, 21f), 7303 + tries, 0.2f);
                float windSign = CircleGesture.Reversed ? -1f : 1f;
                var centre = Scr(0.72f, 0.42f);
                float rr = Screen.height * 0.12f;
                PointerInput.SimActive = true;
                for (float t = 0f; t < 5f && ctl.State == FishingController.S.Fighting && !under; t += Time.deltaTime)
                {
                    circleAng -= windSign * Time.deltaTime * 1.6f * Mathf.PI * 2f;
                    PointerInput.SimDown = true;
                    PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * rr;
                    yield return null;
                    under = ctl.State == FishingController.S.Fighting && tk.FloatUnder >= 0.35f && ctl.Angler.LineUnderwater && ctl.Fight != null && !ctl.Fight.Jumping;
                }
                PointerInput.SimDown = false;
                if (!under && ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            }
            if (!under || ctl.State != FishingController.S.Fighting || ctl.Hooked == null)
            {
                BCheck(tag, false, $"the float never went under along the line in {tries} fights ({ctl.State})");
                yield return ToReady(ctl);
                yield break;
            }
            var entry = ctl.Angler.WaterEntry;
            var mouth = ctl.Hooked.MouthPos;
            float lu = Vector3.Distance(entry, mouth), dF = Vector3.Distance(entry, tk.FloatAt);
            // the rub point: halfway from the entry to the float (above it), or halfway from the float to the mouth (below)
            float k = lu > 1e-3f ? dF / lu : 0f;
            float rubT = above ? 0.5f * k : 0.5f * (k + 1f);
            int n0 = Game.I.BaitCount(bait.id);
            float underAt = tk.FloatUnder;   // (before the break: letting the float go zeroes it)
            bool parted = ctl.DebugPartLine("rub", rubT);
            var r = ctl.LastLoss;
            string geo = string.Format(CIk, "float under {0:0.00} m, {1:0.00} m down the line from its entry (line {2:0.00} m), rub at {3:0.00} of it: rub {4:0.00} m / float {5:0.00} m from the hook",
                underAt, dF, lu, rubT, r != null ? r.rubFromHook : -1f, r != null ? r.floatFromHook : -1f);
            if (above)
            {
                bool gone = ctl.State == FishingController.S.Ready && tk.State == Tackle.Mode.Hidden;
                yield return new WaitForSeconds(0.6f);
                ToastParts(r, out bool shown, out _, out _, out _);
                BCheck(tag, parted && k > 0.05f && r != null && r.cause == "rub" && r.off == FishingController.Off.Above && r.floatLost && gone && shown && r.items.Contains("찌"),
                    $"{geo}; off {(r != null ? r.off.ToString() : "-")}, float lost {r != null && r.floatLost}, rig gone {gone}, toast '{(r != null ? string.Join(" | ", r.items) : "-")}'");
                yield return new WaitForSeconds(3.2f);
            }
            else
            {
                bool kept = ctl.State == FishingController.S.Retrieving && tk.State == Tackle.Mode.Water && tk.BareHook;
                // (the hook springs back from where the line wore through, not from the mouth)
                var sn = ctl.SnapFx;
                float whipOff = r != null ? Vector3.Distance(sn.From, r.breakAt) : 99f, whipMouth = r != null ? Vector3.Distance(sn.From, r.mouth) : 0f;
                bool whipFromRub = sn.LastKind == LineSnap.Kind.Recoil && whipOff < 0.01f && whipMouth > 0.05f;
                var w = new RigWatch();
                yield return WatchHome(ctl, bait, w);
                bool noToast = r != null && r.toast == null && !r.toastPending && r.items.Count == 0;
                BCheck(tag, parted && r != null && r.cause == "rub" && r.off == FishingController.Off.AtHook && !r.floatLost && noToast && kept && w.home && w.baitFrames == 0
                            && Game.I.BaitCount(bait.id) == n0 && whipFromRub,
                    $"{geo}; off {(r != null ? r.off.ToString() : "-")}, float kept and wound in {kept}, no toast {noToast}; {w.Brief}; the hook whipped back from the rub point {whipFromRub} ({whipOff:0.000} m off it, {whipMouth:0.00} m from the mouth)");
            }
        }

        // ------------------------------------------------------------------ 5. a float rig snagged and forced until the line parts
        IEnumerator BrSnagFloat(FishingController ctl)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            yield return BrPlace(ctl, bait.id, BrAt(ctl, 1f, 0f, 14f));
            int n0 = Game.I.BaitCount(bait.id), b0 = ctl.SnagBreaks;
            bool snagged = ctl.DebugSnag();
            yield return new WaitForSeconds(0.3f);
            var snap = ctl.SnapFx;
            int plays0 = snap.Plays;
            yield return BrWind(ctl, 6f, 2f);
            var tk = ctl.Tackle;
            var r = ctl.LastLoss;
            bool kept = ctl.State == FishingController.S.Retrieving && tk.State == Tackle.Mode.Water && tk.BareHook && ctl.SpentRetrieve;
            var w = new RigWatch();
            yield return WatchHome(ctl, bait, w);
            ToastParts(r, out bool shown, out int icons, out _, out _);   // (shown after the whip, for 3.2 s)
            int n1 = Game.I.BaitCount(bait.id);
            BCheck("snag_float", snagged && ctl.SnagBreaks == b0 + 1 && r != null && r.cause == "snag" && kept && w.home && w.floatFrames > 0 && w.bareFrames > 0 && w.baitFrames == 0,
                $"snagged {snagged}, forced until it parted {ctl.SnagBreaks - b0}: float kept, wound in spent with the bare hook {kept}; {w.Brief}");
            BCheck("snag_float_bait", n1 == n0 - 1 && r != null && r.baitN == 1 && shown && icons >= 1 && r.items.Any(s => s == bait.name),
                $"{bait.id} {n0} -> {n1} (the unused bait once), toast '{(r != null ? string.Join(" | ", r.items) : "-")}' ({icons} icon(s))");
            BCheck("snag_float_snap", snap.Plays == plays0 + 1 && snap.LastKind == LineSnap.Kind.Recoil && snap.LastFrames >= 8,
                $"snaps {snap.Plays - plays0} ({snap.LastKind}), drawn {snap.LastFrames} frames");
            yield return new WaitForSeconds(1f);
        }

        // ------------------------------------------------------------------ 6. the 끊기 button on a snagged lure
        IEnumerator BrCutLure(FishingController ctl)
        {
            var lure = GameDatabase.GetItem<BaitDef>("bait_spinner");
            yield return BrPlace(ctl, lure.id, BrAt(ctl, -1f, 0f, 15f));
            bool snagged = ctl.DebugSnag();
            yield return new WaitForSeconds(0.5f);
            bool button = HasButton("끊기");
            var snap = ctl.SnapFx;
            int plays0 = snap.Plays, b0 = ctl.SnagBreaks;
            Click("끊기");
            yield return null;
            var r = ctl.LastLoss;
            var tk = ctl.Tackle;
            bool gone = ctl.State == FishingController.S.Ready && tk.State == Tackle.Mode.Hidden;
            yield return new WaitForSeconds(0.6f);
            ToastParts(r, out bool shown, out int icons, out _, out _);
            BCheck("cut_button", snagged && button && ctl.SnagBreaks == b0 + 1 && r != null && r.cause == "cut" && gone && !Game.I.Owns(lure.id) && Game.I.Bait.id == GameDatabase.StarterBait,
                $"snagged {snagged}, 끊기 shown {button}, cut {ctl.SnagBreaks - b0}: rig gone {gone}, {lure.id} owned {Game.I.Owns(lure.id)}, equipped {Game.I.Bait.id}");
            BCheck("cut_button_toast", shown && icons >= 1 && r.lure == lure && r.items.Any(s => s.Contains(lure.name)) && snap.Plays == plays0 + 1 && snap.LastKind == LineSnap.Kind.Home,
                $"toast '{(r != null ? string.Join(" | ", r.items) : "-")}' ({icons} icon(s)), snap {snap.LastKind}");
            yield return new WaitForSeconds(3.2f);
        }

        // ------------------------------------------------------------------ 7. a fish shaking the lure off
        IEnumerator BrShakeOff(FishingController ctl)
        {
            var lure = GameDatabase.GetItem<BaitDef>("bait_minnow");
            yield return BrPlace(ctl, lure.id, BrAt(ctl, 2f, 0f, 17f));
            yield return BrHook(ctl, "carp", 45f, BrAt(ctl, 2f, -1.5f, 17f), 7505, 0.5f);
            if (ctl.State != FishingController.S.Fighting || ctl.Hooked == null)
            {
                BCheck("shake_off", false, $"not fighting ({ctl.State})");
                yield break;
            }
            var snap = ctl.SnapFx;
            int plays0 = snap.Plays, breaks0 = ctl.Breaks;
            var fishAt = ctl.Hooked.MouthPos;
            ctl.DebugRelease();
            var tk = ctl.Tackle;
            var at = tk.Surface;
            float off = new Vector2(at.x - fishAt.x, at.z - fishAt.z).magnitude;
            bool kept = ctl.State == FishingController.S.Retrieving && tk.State == Tackle.Mode.Water && ctl.SpentRetrieve;
            var w = new RigWatch();
            yield return WatchHome(ctl, lure, w, 30f, "breaks_lure_home", 1.4f);
            Log($"[BREAK] shake-off: {w.Brief}");
            BCheck("shake_off", kept && off < 1f && at.z > ctl.Stage.L.zNear + 5f && Game.I.Owns(lure.id) && Game.I.Bait.id == lure.id,
                string.Format(CIk, "the lure stays on the line where the fish let it go {0} ({1:0.00} m from its mouth at ({2:0.0}, {3:0.0})), {4} kept {5}, equipped {6}",
                    kept, off, fishAt.x, fishAt.z, lure.id, Game.I.Owns(lure.id), Game.I.Bait.id));
            BCheck("shake_off_home", w.home && w.retrieving > 0 && w.baitFrames > 0 && w.z0 > w.zEnd + 5f && ctl.Breaks == breaks0 && snap.Plays == plays0,
                $"wound home from there (no snap {snap.Plays == plays0}, no loss toast {ctl.Breaks == breaks0}); {w.Brief}");
        }

        // ------------------------------------------------------------------ 8. the bait thief
        IEnumerator BrThief(FishingController ctl)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            yield return BrPlace(ctl, bait.id, BrAt(ctl, 0f, 0f, 14f));
            int n0 = Game.I.BaitCount(bait.id), th0 = ctl.BaitThefts;
            FishAgent f = null;
            yield return BrForceBite(ctl, x => f = x);
            for (float w = 0f; w < 3f && ctl.State == FishingController.S.Biting; w += Time.deltaTime) yield return null;
            var tk = ctl.Tackle;
            bool bare = ctl.State == FishingController.S.Retrieving && tk.BareHook;
            var rw = new RigWatch();
            yield return WatchHome(ctl, bait, rw);
            int n1 = Game.I.BaitCount(bait.id);
            BCheck("bait_thief", f != null && ctl.BaitThefts == th0 + 1 && bare && n1 == n0 - 1 && rw.home && rw.bareFrames > 0 && rw.baitFrames == 0,
                $"fish {(f != null ? f.Sp.id : "none")} took the bait, not struck: thefts {ctl.BaitThefts - th0}, wound in with the bare hook {bare}, {bait.id} {n0} -> {n1}; {rw.Brief}");
        }

        // ------------------------------------------------------------------ 9. a pad tears the bait off
        IEnumerator BrPadTear(FishingController ctl)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            var pad = ctl.Stage.Obstacles.Get("pad-7.5_16.4");
            if (pad == null)
            {
                BCheck("pad_tear", false, "no pad-7.5_16.4");
                yield break;
            }
            yield return BrPlace(ctl, bait.id, new Vector3(pad.x, 0f, pad.z));
            var sn = ctl.Tackle.Snag;
            bool padSnag = ctl.State == FishingController.S.Snagged && sn != null && sn.kind == "pad";
            int n0 = Game.I.BaitCount(bait.id), t0 = ctl.PadTears;
            yield return BrWind(ctl, 5f, 1f);
            var tk = ctl.Tackle;
            bool bare = ctl.State == FishingController.S.Retrieving && tk.BareHook;
            var rw = new RigWatch();
            yield return WatchHome(ctl, bait, rw);
            int n1 = Game.I.BaitCount(bait.id);
            BCheck("pad_tear", padSnag && ctl.PadTears == t0 + 1 && bare && n1 == n0 - 1 && rw.home && rw.bareFrames > 0 && rw.baitFrames == 0,
                $"pad snag {padSnag}, wound until it tore {ctl.PadTears - t0}: wound in with the bare hook {bare}, {bait.id} {n0} -> {n1}; {rw.Brief}");
        }

        // ------------------------------------------------------------------ 10. the spent retrieve against an intact rig's
        IEnumerator BrSpentRetrieve(FishingController ctl)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            // beyond the weed bed (3, 14) as he sees it: the way home runs through the weed, every check there snags at x999
            var obs = ctl.Stage.Obstacles;
            var bed = obs.Get("weedbed");
            var spot = bed != null ? new Vector3(bed.x + (bed.x - ctl.Angler.X) * 0.35f, 0f, bed.z + 5f) : BrAt(ctl, 3.5f, 0f, 19f);
            float snagWas = Obstacles.SnagMult;
            // the control: an intact rig wound in from there (the 회수 button)
            yield return BrPlace(ctl, bait.id, spot);
            Obstacles.SnagMult = 999f;
            int c0 = ctl.SnagChecks, s0 = ctl.SnagCount;
            ctl.Retrieve();
            for (float w = 0f; w < 20f && ctl.State == FishingController.S.Retrieving; w += Time.deltaTime) yield return null;
            int ctlChecks = ctl.SnagChecks - c0, ctlSnags = ctl.SnagCount - s0;
            var ctlState = ctl.State;
            Obstacles.SnagMult = snagWas;
            yield return ToReady(ctl);
            // the same rig hooked there and parted at the hook: wound home spent (fish free to bite)
            yield return BrPlace(ctl, bait.id, spot);
            yield return BrHook(ctl, "carp", 50f, new Vector3(spot.x, -2f, spot.z), 7606, 0.3f);
            FishingController.NoBites = false;
            Obstacles.SnagMult = 999f;
            s0 = ctl.SnagCount;
            bool parted = ctl.DebugPartLine("tension");
            bool fresh = !ctl.SnagRefSet;
            var w2 = new RigWatch();
            yield return WatchHome(ctl, bait, w2);
            Obstacles.SnagMult = snagWas;
            FishingController.NoBites = true;
            BCheck("spent_control", ctlChecks > 0, $"an intact rig wound in from ({Fk(spot.x)}, {Fk(spot.z)}) at x999: {ctlChecks} snag checks, {ctlSnags} snag(s), ended {ctlState}");
            BCheck("spent_retrieve", parted && fresh && w2.home && w2.snagChecks == 0 && w2.snagged == 0 && ctl.SnagCount == s0 && w2.bites == 0 && w2.approaches == 0,
                $"after the break: the snag reference reset {fresh}; {w2.Brief}");
        }

        /// <summary>Up to 3 wandering ordinary fish put 1.5-2.5 m from the hook, at its depth (to be sensed at once).</summary>
        int BrFishNear(FishingController ctl, out FishAgent first)
        {
            var hook = ctl.Tackle.HookPos;
            first = null;
            int n = 0;
            foreach (var x in ctl.Spawner.Fish)
            {
                if (x == null || x.State != FishAgent.St.Wander || x.Sp.encounter != null) continue;
                float a = n * 2.1f;
                float d = 1.5f + 0.5f * n;
                x.Pos = new Vector3(hook.x + Mathf.Cos(a) * d, -Mathf.Max(0.5f, -hook.y), hook.z + Mathf.Sin(a) * d);
                if (first == null) first = x;
                if (++n >= 3) break;
            }
            return n;
        }

        // ------------------------------------------------------------------ 10b. a spent rig / a bare hook back in the water, waiting
        /// <summary>
        /// A float rig with the golden carp's key bait (황금 떡밥) left waiting with nothing to catch on: a bare hook (the
        /// bait thief took it) or a spent rig (the line parted at the hook), caught on a snag on the way home and freed, so
        /// it is waiting again. No fish may take it (CanFishEngage, approaches, bites over 4 s with fish put by it), the
        /// legend's soak must not run (even under -fkencounter now's 1 s trigger) and no bait is taken. The control: the same
        /// bait intact, waiting, fish by it: engaged, soaking, approached.
        /// </summary>
        IEnumerator BrBareWaiting(FishingController ctl, bool spent)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_golden");
            string tag = spent ? "spent_waiting" : "bare_waiting";
            var spot = BrAt(ctl, 0.5f, 0f, 15f);
            var watch = ctl.Watch;
            // the control: the intact rig
            if (!spent)
            {
                yield return BrPlace(ctl, bait.id, spot);
                FishingController.NoBites = false;
                int nNear = BrFishNear(ctl, out var near);
                bool engage = near != null && ctl.CanFishEngage(near);
                float soak0 = watch != null ? watch.Soak : 0f;
                int a0 = ctl.Approaches, b0 = ctl.BiteCount;
                float t = 0f;
                for (; t < 10f && ctl.Approaches == a0 && ctl.BiteCount == b0 && ctl.State == FishingController.S.Waiting; t += Time.deltaTime) yield return null;
                float soaked = watch != null ? watch.Soak - soak0 : 0f;
                BCheck("waiting_control", engage && soaked > 0f && ctl.Approaches + ctl.BiteCount > a0 + b0,
                    string.Format(CIk, "an intact {0} rig waiting, {1} fish put by it: engaged {2}, legend soak +{3:0.000} s, approached {4} / bit {5} within {6:0.0} s",
                        bait.id, nNear, engage, soaked, ctl.Approaches - a0, ctl.BiteCount - b0, t));
                FishingController.NoBites = true;
                yield return ToReady(ctl);
            }
            yield return BrPlace(ctl, bait.id, spot);
            var tk = ctl.Tackle;
            int n0 = Game.I.BaitCount(bait.id);
            if (spent)
            {
                yield return BrHook(ctl, "carp", 45f, new Vector3(spot.x, -1.5f, spot.z), 7616, 0.3f);
                ctl.DebugPartLine("tension");
            }
            else
            {
                FishAgent f = null;
                yield return BrForceBite(ctl, x => f = x);
                for (float w = 0f; w < 3f && ctl.State == FishingController.S.Biting; w += Time.deltaTime) yield return null;
            }
            bool coming = ctl.State == FishingController.S.Retrieving && tk.BareHook && ctl.SpentRetrieve == spent;
            yield return new WaitForSeconds(0.9f);   // (past the let-go's wait: wound in)
            bool snagged = ctl.DebugSnag();
            yield return null;
            bool freed = ctl.DebugFreeSnag();
            yield return null;
            int n1 = Game.I.BaitCount(bait.id);
            bool waiting = ctl.State == FishingController.S.Waiting && tk.State == Tackle.Mode.Water && tk.BareHook && ctl.SpentRetrieve == spent;
            FishingController.NoBites = false;
            int nFish = BrFishNear(ctl, out var near2);
            bool engage2 = near2 != null && ctl.CanFishEngage(near2), wants2 = near2 != null && ctl.WantsToApproach(near2);
            float soakB = watch != null ? watch.Soak : 0f;
            int a1 = ctl.Approaches, b1 = ctl.BiteCount;
            var modeWas = LegendWatch.DebugMode;
            LegendWatch.DebugMode = "now";   // (an encounter 1 s into a soak, whatever the meter: none may start here)
            bool encounter = false;
            for (float w = 0f; w < 4f && ctl.State == FishingController.S.Waiting; w += Time.deltaTime)
            {
                yield return null;
                encounter |= ctl.State == FishingController.S.Encounter;
            }
            encounter |= ctl.State == FishingController.S.Encounter;
            LegendWatch.DebugMode = modeWas;
            float soakedB = watch != null ? watch.Soak - soakB : 0f;
            int n2 = Game.I.BaitCount(bait.id);
            BCheck(tag, coming && snagged && freed && waiting && !engage2 && !wants2 && ctl.Approaches == a1 && ctl.BiteCount == b1 && !encounter && soakedB < 1e-4f && n2 == n1,
                string.Format(CIk, "{0} coming home {1}, snagged on the way {2}, freed {3} -> waiting again with the bare hook {4} ({5}); {6} fish put by it: engaged {7}, wants to approach {8}, approaches {9}, bites {10} in 4 s; legend soak +{11:0.00} s, encounter {12}; {13} {14} -> {15} -> {16}",
                    spent ? "spent rig" : "bare hook", coming, snagged, freed, waiting, ctl.State, nFish, engage2, wants2, ctl.Approaches - a1, ctl.BiteCount - b1, soakedB, encounter, bait.id, n0, n1, n2));
            if (ctl.State == FishingController.S.Encounter) yield return new WaitForSeconds(8f);
            FishingController.NoBites = true;
            yield return ToReady(ctl);
        }

        // ------------------------------------------------------------------ 10c. a float rig worn through on real structure
        /// <summary>
        /// A float rig's fish run against structure (the lake: forced to <paramref name="cover"/> and left there; the ice: its
        /// own rim runs) with the wear sped up <paramref name="rubMult"/> times, until FightModel parts the line by abrasion.
        /// The real rub point (FightObstacles: the obstacle's contact, the rim's y -0.1 edge) decides: the test measures it
        /// against the float itself (from the hook) and checks the outcome matches (above: the float lost, the rig gone; below:
        /// the float kept, the bare hook whipping back from the rub point). <paramref name="wantBelow"/>: the geometry must be
        /// a rub below the float. The ice: the rub point must be on the hole's rim.
        /// </summary>
        IEnumerator BrRealRub(FishingController ctl, string tag, string fishId, float cm, Vector3 at, string cover, float floatDepth, int seed, float rubMult, bool wantBelow)
        {
            var bait = GameDatabase.GetItem<BaitDef>("bait_worm");
            string rod = Game.I.Rod.id, reel = Game.I.Reel.id, line = Game.I.Line.id;
            if (cover != null) SteerGear("rod_glass", "reel_basic", "line_nylon2");
            else SteerGear("rod_carbon", "reel_highgear", "line_pe3");
            yield return BrPlace(ctl, bait.id, new Vector3(at.x, 0f, at.z), floatDepth);
            var tk = ctl.Tackle;
            var snap = ctl.SnapFx;
            int breaks0 = ctl.Breaks, runs0 = ctl.CoverRuns, plays0 = snap.Plays;
            FishingController.DebugRubMult = rubMult;
            FishingController.DebugCover = cover;
            if (!ctl.DebugHook(GameDatabase.GetFish(fishId), cm, seed, at))
            {
                BCheck(tag, false, $"no hook ({ctl.State})");
                FishingController.DebugCover = null;
                FishingController.DebugRubMult = 1f;
                SteerGear(rod, reel, line);
                yield break;
            }
            var f = ctl.Fight;
            var centre = Scr(0.72f, 0.42f);
            float rr = Screen.height * 0.13f, windSign = CircleGesture.Reversed ? -1f : 1f, t = 0f;
            PointerInput.SimActive = true;
            while (ctl.State == FishingController.S.Fighting && ctl.Fight == f && t < 60f)
            {
                t += Time.deltaTime;
                circleAng -= windSign * Time.deltaTime * 0.3f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * rr;
                // ignored: back to the cover whenever it is out of it
                if (cover != null && !(f.CoverRun || f.CoverHold) && FishingController.DebugCover == null) FishingController.DebugCover = cover;
                yield return null;
            }
            PointerInput.SimDown = false;
            FishingController.DebugCover = null;
            FishingController.DebugRubMult = 1f;
            var r = ctl.LastLoss;
            bool broke = ctl.Breaks == breaks0 + 1 && r != null && r.cause == "rub" && ctl.LastSnapCause == FightModel.Cause.Abrasion;
            var state = ctl.State;
            var mode = tk.State;
            bool bare = tk.BareHook;
            string where = "-";
            bool ok = false;
            if (broke)
            {
                // the test's own reading of the real rub point against the float, along the line from the hook
                float dR = Vector3.Distance(r.breakAt, r.mouth), dF = Vector3.Distance(r.floatAt, r.mouth);
                bool aboveSeen = dR > dF + 0.05f;
                bool outcome = aboveSeen
                    ? r.off == FishingController.Off.Above && r.floatLost && state == FishingController.S.Ready && mode == Tackle.Mode.Hidden
                    : r.off == FishingController.Off.AtHook && !r.floatLost && state == FishingController.S.Retrieving && mode == Tackle.Mode.Water && bare
                      && snap.Plays == plays0 + 1 && snap.LastKind == LineSnap.Kind.Recoil && Vector3.Distance(snap.From, r.breakAt) < 0.01f;
                bool geometry = !wantBelow || !aboveSeen;
                bool onRim = true;
                if (ctl.Stage.L.IsIce)
                {
                    var L = ctl.Stage.L;
                    float hd = new Vector2(r.breakAt.x - L.holeX, r.breakAt.z - L.holeZ).magnitude;
                    onRim = Mathf.Abs(hd - L.holeR) < 0.05f && Mathf.Abs(r.breakAt.y + 0.1f) < 0.01f;
                    where = string.Format(CIk, "{0:0.00} m from the hole's centre (rim {1:0.00}), y {2:0.00}", hd, L.holeR, r.breakAt.y);
                }
                else where = string.Format(CIk, "({0:0.00}, {1:0.00}, {2:0.00})", r.breakAt.x, r.breakAt.y, r.breakAt.z);
                ok = outcome && geometry && onRim;
                where += string.Format(CIk, "; rub {0:0.00} m / float {1:0.00} m from the hook -> {2} (report {3:0.00} / {4:0.00}); outcome as it should be {5}, on the rim {6}; snap {7} from {8:0.00} m off the rub",
                    dR, dF, aboveSeen ? "above" : "below", r.rubFromHook, r.floatFromHook, outcome, onRim, snap.LastKind, Vector3.Distance(snap.From, r.breakAt));
            }
            BCheck(tag, broke && ok,
                string.Format(CIk, "{0} {1:0}cm on {2} ({3}, wear x{4:0}): cover runs {5}, worn through {6} after {7:0.0} s ({8}, cause {9}); off {10}, float lost {11}, {12} / tackle {13}, bare hook {14}; rub at {15}",
                    fishId, cm, Game.I.Line.id, ctl.Stage.Def.id, rubMult, ctl.CoverRuns - runs0, broke, t, state, ctl.LastSnapCause,
                    r != null ? r.off.ToString() : "-", r != null && r.floatLost, state, mode, bare, where));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(0.6f);
            yield return ToReady(ctl);
            SteerGear(rod, reel, line);
            yield return new WaitForSeconds(2.8f);   // (the toast gone before the next)
        }

        // ------------------------------------------------------------------ 11. a legend's key lure parted on the legend
        IEnumerator BrLegendKey(FishingController ctl)
        {
            var lure = GameDatabase.GetItem<BaitDef>("bait_softworm");
            var legend = GameDatabase.GetFish("golden_carp");
            yield return BrPlace(ctl, lure.id, BrAt(ctl, 1f, 0f, 15f));
            if (!ctl.DebugHook(legend, 70f, 7707, BrAt(ctl, 1f, -1.5f, 15f)))
            {
                BCheck("legend_key", false, $"no hook ({ctl.State})");
                yield break;
            }
            yield return new WaitForSeconds(0.7f);
            bool parted = ctl.DebugPartLine("tension");
            yield return new WaitForSeconds(0.8f);
            yield return NamedShot("breaks_toast_legend");
            var r = ctl.LastLoss;
            ToastParts(r, out bool shown, out int icons, out bool gold, out bool eye);
            BCheck("legend_key", parted && r != null && r.lure == lure && r.lureKey && r.Highlighted && shown && icons >= 1 && gold && eye && !Game.I.Owns(lure.id),
                $"{legend.id} on {lure.id} ({UIKit.Num(lure.price)} coins): key {r != null && r.lureKey}, expensive {r != null && r.lureExpensive}, toast shown {shown}, {icons} icon(s), gold text {gold}, eye {eye}, '{(r != null ? string.Join(" | ", r.items) : "-")}'");
            yield return new WaitForSeconds(3f);
        }
    }
}
