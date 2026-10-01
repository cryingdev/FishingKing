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
    /// the spent retrieve after a break against an intact rig's (snag checks, bites); a legend's key lure parted on the
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
            yield return BrLegendKey(ctl);
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
            yield return new WaitForEndOfFrame();
            var pv = PixelView.Current;
            if (pv == null) yield break;
            var c = pv.WorldToScreen(world2D);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
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
            yield return new WaitForSeconds(2.5f);
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
                var w = new RigWatch();
                yield return WatchHome(ctl, bait, w);
                bool noToast = r != null && r.toast == null && !r.toastPending && r.items.Count == 0;
                BCheck(tag, parted && r != null && r.cause == "rub" && r.off == FishingController.Off.AtHook && !r.floatLost && noToast && kept && w.home && w.baitFrames == 0
                            && Game.I.BaitCount(bait.id) == n0,
                    $"{geo}; off {(r != null ? r.off.ToString() : "-")}, float kept and wound in {kept}, no toast {noToast}; {w.Brief}");
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
            BCheck("snag_float_bait", n1 == n0 - 1 && r != null && r.baitN == 1 && shown && icons >= 1 && r.items.Any(s => s.Contains(bait.name + " ×1")),
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
