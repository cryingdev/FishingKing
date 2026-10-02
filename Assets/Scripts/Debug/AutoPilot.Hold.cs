using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto hold: 설정 → 조작 (handedness 오른손 / 왼손, the rod 옆 / 가운데; Angler.ReadSettings), in the fishing scene
    /// (e.g. -fkfresh -fkrich -fkgear -fksave hd_hold -fkscene Fishing -fkstage lake -fkauto hold -fkshots &lt;dir&gt;):
    /// <list type="number">
    /// <item>the settings: 설정 (settings_main) → 조작 (settings_controls, the defaults on a fresh save), 왼손 + 가운데 picked
    /// by their buttons (settings_controls_changed): the save, the angler next frame and the save read back from disk
    /// follow; a save written before the fields reads 오른손 / 옆; back to the defaults the same way;</item>
    /// <item>the four holds (right / left x side / centre): at rest, and in a fight with the fish 10 deg beyond each rod yaw
    /// limit and the rod leant out to it (the rod at the limit, its clearance from the hat; hold_&lt;hold&gt;_rest /
    /// _fight_L / _fight_R full shots and one hold_grid.png (+ _x3) of 1:1 crops round the angler: a row per hold, rest /
    /// fight left / fight right);</item>
    /// <item>the hold changed mid-fight (the fight goes on, the rod hand changes side, the line stays on the tip), and a
    /// fish landed with the rod held in the middle left-handed;</item>
    /// <item>the wind-up mirrored: the finger to either side leans the rod's top towards it, left-handed the mirror image
    /// of right-handed (the rest lean out to the rod hand's side), and the same flick lands at the same yaw either hand
    /// (the aim fan is the same: drawn from the feet).</item>
    /// </list>
    /// "[HOLD] CHECK" lines, then "hold test done: N failed".
    /// </summary>
    public partial class AutoPilot
    {
        int holdFails;
        const int HoldCropW = 208, HoldCropH = 156;

        void HCheck(string what, bool ok)
        {
            if (!ok) holdFails++;
            Log($"[HOLD] CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        static string HoldTag(bool left, bool centre) => (left ? "left" : "right") + "_" + (centre ? "centre" : "side");

        IEnumerator HoldTest()
        {
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("no FishingController (use -fkscene Fishing)");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            FishingController.NoBites = true;
            PointerInput.SimDpi = 0f;
            var a = ctl.Angler;
            Log($"hold test {ctl.Stage.Def.id}: 3d {a.Uses3D}, screen {Screen.width}x{Screen.height}, save hand {(Game.Data.leftHanded ? "left" : "right")} rod {(Game.Data.rodCentre ? "centre" : "side")}");

            yield return HoldSettings(ctl);
            yield return HoldCombos(ctl);
            yield return HoldLive(ctl);
            yield return HoldWindUp(ctl);

            Game.I.SetLeftHanded(false);
            Game.I.SetRodCentre(false);
            FishingController.NoBites = false;
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            Log($"hold test done: {holdFails} failed");
            PointerInput.SimActive = false;
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit();
        }

        // ------------------------------------------------------------------ 1. the settings window
        IEnumerator HoldSettings(FishingController ctl)
        {
            var a = ctl.Angler;
            bool freshL = Game.Data.leftHanded, freshC = Game.Data.rodCentre;
            SettingsUI.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("settings_main");
            HCheck("설정 has the 조작 button", HasButton("조작"));
            Click("조작");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("settings_controls");
            HCheck($"조작 shows 손잡이 오른손 / 왼손 and 낚싯대 위치 옆 / 가운데 (save: {(freshL ? "왼손" : "오른손")}, {(freshC ? "가운데" : "옆")}; the defaults on a fresh save)",
                HasButton("오른손") && HasButton("왼손") && HasButton("옆") && HasButton("가운데") && (!Flag("-fkfresh") || (!freshL && !freshC)));
            HCheck($"the window fits the smallest canvas: {WindowFits("조작")}", WindowFits("조작") == "ok");
            Click("왼손");
            Click("가운데");
            yield return null;
            yield return null;
            HCheck($"왼손 + 가운데 picked: the save {Game.Data.leftHanded}/{Game.Data.rodCentre}, the angler {a.LeftHanded}/{a.RodCentre} (rod hand x {N(a.Hand.x - a.Feet.x, "+0.00;-0.00")} m off the feet, yaw limits {N(a.RodYawMin, "0")}..{N(a.RodYawMax, "+0")})",
                Game.Data.leftHanded && Game.Data.rodCentre && a.LeftHanded && a.RodCentre && Mathf.Abs(a.Hand.x - a.Feet.x) < 0.2f);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("settings_controls_changed");
            var disk = SaveSystem.Load();
            HCheck($"saved: read back from disk {disk.leftHanded}/{disk.rodCentre}", disk.leftHanded && disk.rodCentre);
            // a save from before the fields: both missing from its JSON
            string json = JsonUtility.ToJson(Game.Data);
            string old = Regex.Replace(json, "\"(leftHanded|rodCentre)\":(true|false),?", "");
            var od = SaveSystem.Sanitize(JsonUtility.FromJson<SaveData>(old));
            HCheck($"a save from before them reads 오른손 / 옆 (fields removed: {old.Length < json.Length}; read {od.leftHanded}/{od.rodCentre})", old.Length < json.Length && !od.leftHanded && !od.rodCentre);
            Click("오른손");
            yield return null;
            Click("옆");
            yield return null;
            yield return null;
            disk = SaveSystem.Load();
            HCheck($"back to 오른손 + 옆: the save {Game.Data.leftHanded}/{Game.Data.rodCentre}, the angler {a.LeftHanded}/{a.RodCentre}, disk {disk.leftHanded}/{disk.rodCentre} (rod hand x {N(a.Hand.x - a.Feet.x, "+0.00;-0.00")} m)",
                !Game.Data.leftHanded && !Game.Data.rodCentre && !a.LeftHanded && !a.RodCentre && !disk.leftHanded && !disk.rodCentre && a.Hand.x - a.Feet.x < -0.2f);
            for (int i = 0; i < 4 && Dialog.Open; i++)
            {
                Click("닫기");
                yield return new WaitForSecondsRealtime(0.2f);
            }
            HCheck("the windows closed", !Dialog.Open);
        }

        static bool Flag(string key) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), key) >= 0;

        /// <summary>
        /// The 조작 window (the parent of its note) with its title ribbon inside the smallest canvas, 540 units high, 4 clear
        /// each side: "ok" or its height in canvas units.
        /// </summary>
        static string WindowFits(string title)
        {
            var note = GameObject.Find("ControlsNote");
            var win = note != null ? note.transform.parent as RectTransform : null;
            var canvas = win != null ? win.GetComponentInParent<Canvas>() : null;
            if (win == null || canvas == null) return title + " window not found";
            float s = canvas.rootCanvas.transform.lossyScale.y;
            float top = float.MinValue, bottom = float.MaxValue;
            var cc = new Vector3[4];
            foreach (var t in win.GetComponentsInChildren<RectTransform>())
            {
                t.GetWorldCorners(cc);
                top = Mathf.Max(top, cc[1].y / s);
                bottom = Mathf.Min(bottom, cc[0].y / s);
            }
            float h = top - bottom;
            return h <= 540f - 8f ? "ok" : string.Format(CI, "{0:0} units high", h);
        }

        // ------------------------------------------------------------------ 2. the four holds
        IEnumerator HoldCombos(FishingController ctl)
        {
            var a = ctl.Angler;
            SteerGear("rod_glass", "reel_light", "line_nylon4");
            var sp = GameDatabase.GetFish("carp");
            float homeX = Mathf.Clamp(0f, a.Range.x, a.Range.y);
            var tiles = new List<Texture2D>();
            foreach (bool centre in new[] { false, true })
            foreach (bool left in new[] { false, true })
            {
                string tag = HoldTag(left, centre);
                Game.I.SetLeftHanded(left);
                Game.I.SetRodCentre(centre);
                yield return BackToReady(ctl);
                a.DebugPlace(homeX);
                yield return new WaitForSeconds(0.8f);
                yield return new WaitForEndOfFrame();
                float rest = ActorStrip.Clearance(ctl);
                tiles.Add(HoldCrop(ctl));
                Log(string.Format(CI, "[HOLD] {0} rest: hand ({1:+0.00;-0.00}, {2:0.00}, {3:0.00}) m off the feet, rod yaw {4:+0.0;-0.0;0.0} (limits {5:0}..{6:+0}), pitch {7:0.0} lean {8:+0.0;-0.0;0.0}, hat clear {9:0.0}px, tip ({10:0.0}, {11:0.0}) px off the feet",
                    tag, a.Hand.x - a.Feet.x, a.Hand.y - a.Feet.y, a.Hand.z - a.Feet.z, a.RodYaw, a.RodYawMin, a.RodYawMax, a.RodAngles.x, a.RodAngles.y, rest,
                    (ctl.Stage.P.To2D(a.RodTip).x - ctl.Stage.P.To2D(a.Feet).x) * PixelView.PPU, (ctl.Stage.P.To2D(a.RodTip).y - ctl.Stage.P.To2D(a.Feet).y) * PixelView.PPU));
                float side = left ? 1f : -1f;   // the rod hand's side (x of the hand off the feet)
                bool handOk = centre ? Mathf.Abs(a.Hand.x - a.Feet.x) < 0.15f && a.Hand.z - a.Feet.z > 0.12f : (a.Hand.x - a.Feet.x) * side > 0.2f;   // (3D 0.46 m, the pose sprites 0.28)
                HCheck($"{tag} at rest: the rod hand where the hold puts it ({N(a.Hand.x - a.Feet.x, "+0.00;-0.00")}, z {N(a.Hand.z - a.Feet.z, "0.00")} m) and the rod clear of the hat ({N(rest, "0.0")} px)",
                    handOk && rest > (centre ? 1f : 0f));
                yield return Shot($"hold_{tag}_rest");
                foreach (int dir in new[] { -1, 1 })
                {
                    yield return SteerCast(ctl, "bait_minnow", homeX);
                    if (ctl.State != FishingController.S.Waiting)
                    {
                        HCheck($"{tag} cast for the fight ({ctl.State})", false);
                        tiles.Add(null);
                        continue;
                    }
                    float lim = dir > 0 ? a.RodYawMax : a.RodYawMin;
                    float br = (lim + dir * 10f) * Mathf.Deg2Rad;
                    if (!ctl.DebugHook(sp, 70f, 777, new Vector3(a.X + Mathf.Sin(br) * 13f, -1.2f, Mathf.Cos(br) * 13f)))
                    {
                        HCheck($"{tag} hook", false);
                        tiles.Add(null);
                        continue;
                    }
                    yield return null;
                    PointerInput.SimLeft = dir < 0;
                    PointerInput.SimRight = dir > 0;
                    float mn = 999f, tilt = 0f, atYaw = 0f;
                    Texture2D tile = null;
                    for (float t = 0f; t < 1.5f && ctl.State == FishingController.S.Fighting; t += Time.deltaTime)
                    {
                        yield return new WaitForEndOfFrame();
                        if (t > 0.3f)
                        {
                            mn = Mathf.Min(mn, ActorStrip.Clearance(ctl));
                            tilt = Mathf.Max(tilt, Mathf.Abs(a.HatTilt));
                        }
                        if (tile == null && t > 1.1f)
                        {
                            atYaw = a.RodYaw;
                            tile = HoldCrop(ctl);
                            Log(string.Format(CI, "[HOLD] {0} fight {1}: rod yaw {2:+0.0;-0.0} at the limit {3:+0.0;-0.0}, lean {4:+0.00;-0.00;0.00}, sweep line {5:+0.0;-0.0;0.0}, hat tilt {6:+0.0;-0.0;0.0}, tension {7:0.00}",
                                tag, dir < 0 ? "L" : "R", a.RodYaw, lim, ctl.Lean, a.SweepLine, a.HatTilt, ctl.Fight != null ? ctl.Fight.TensionRatio : 0f));
                            yield return Shot($"hold_{tag}_fight_{(dir < 0 ? "L" : "R")}");
                        }
                        yield return null;
                    }
                    PointerInput.SimLeft = PointerInput.SimRight = false;
                    tiles.Add(tile);
                    HCheck($"{tag} fight {(dir < 0 ? "left" : "right")}: the rod at its yaw limit ({N(atYaw, "+0.0;-0.0")} of {N(lim, "+0.0;-0.0")}) and off the hat (min {N(mn, "0.0")} px, tilt up to {N(tilt, "0.0")} deg)",
                        tile != null && Mathf.Abs(atYaw - lim) < 0.6f && mn > (centre ? 1f : 0f));
                    ctl.DebugRelease();
                    for (float w = 0f; w < 20f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
                }
            }
            SaveGrid(tiles, 3, Path.Combine(shots, $"{shotIndex++:00}_hold_grid"));
            Log("shot hold_grid (rows: right side, left side, right centre, left centre; columns: rest, fight at the left limit, fight at the right limit)");
            Game.I.SetLeftHanded(false);
            Game.I.SetRodCentre(false);
        }

        // ------------------------------------------------------------------ 3. changed mid-fight, a landing
        IEnumerator HoldLive(FishingController ctl)
        {
            var a = ctl.Angler;
            var sp = GameDatabase.GetFish("carp");
            float homeX = Mathf.Clamp(0f, a.Range.x, a.Range.y);
            Game.I.SetLeftHanded(false);
            Game.I.SetRodCentre(false);
            yield return SteerCast(ctl, "bait_minnow", homeX);
            float br = 15f * Mathf.Deg2Rad;
            if (ctl.State != FishingController.S.Waiting || !ctl.DebugHook(sp, 60f, 99, new Vector3(a.X + Mathf.Sin(br) * 12f, -1.2f, Mathf.Cos(br) * 12f)))
            {
                HCheck($"hook for the live change ({ctl.State})", false);
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            float x0 = a.Hand.x - a.Feet.x;
            Game.I.SetLeftHanded(true);
            yield return null;
            yield return null;
            float x1 = a.Hand.x - a.Feet.x;
            bool line1 = a.LineTarget.HasValue;
            Game.I.SetRodCentre(true);
            yield return null;
            yield return null;
            float x2 = a.Hand.x - a.Feet.x;
            float mn = 999f;
            for (float t = 0f; t < 1f && ctl.State == FishingController.S.Fighting; t += Time.deltaTime)
            {
                yield return new WaitForEndOfFrame();
                if (t > 0.2f) mn = Mathf.Min(mn, ActorStrip.Clearance(ctl));
                yield return null;
            }
            HCheck($"changed mid-fight: the fight goes on ({ctl.State}), the rod hand {N(x0, "+0.00;-0.00")} -> {N(x1, "+0.00;-0.00")} m (왼손) -> {N(x2, "+0.00;-0.00")} m (가운데), the line on the tip ({line1}/{a.LineTarget.HasValue}), off the hat after (min {N(mn, "0.0")} px)",
                ctl.State == FishingController.S.Fighting && x0 < -0.2f && x1 > 0.2f && Mathf.Abs(x2) < 0.15f && line1 && a.LineTarget.HasValue && mn > 1f);
            // landed held in the middle, left-handed (the landing lift raises the rod straight up from the belly)
            bool landed = ctl.DebugLand();
            float lift = 0f;
            for (float w = 0f; w < 8f && ctl.State != FishingController.S.Result; w += Time.deltaTime)
            {
                lift = Mathf.Max(lift, a.RodLift01);
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            yield return Shot("hold_left_centre_landed");
            HCheck($"landed held in the middle left-handed (lift up to {N(lift)}, {ctl.State})", landed && ctl.State == FishingController.S.Result && lift > 0.5f);
            yield return SteerCast(ctl, "bait_minnow", homeX);   // (sells the catch, back to the ready)
            yield return BackToReady(ctl);
            Game.I.SetLeftHanded(false);
            Game.I.SetRodCentre(false);
        }

        // ------------------------------------------------------------------ 4. the wind-up mirrored
        IEnumerator HoldWindUp(FishingController ctl)
        {
            var a = ctl.Angler;
            float homeX = Mathf.Clamp(0f, a.Range.x, a.Range.y);
            var lean = new Dictionary<string, float>();
            var land = new Dictionary<bool, float>();
            foreach (bool left in new[] { false, true })
            {
                Game.I.SetLeftHanded(left);
                Game.I.SetRodCentre(false);
                yield return BackToReady(ctl);
                a.DebugPlace(homeX);
                yield return new WaitForSeconds(0.5f);
                yield return WindUp(0.25f);
                var p0 = PointerInput.SimPos;
                foreach (float off in new[] { 0.2f, -0.2f, 0f })
                {
                    var to = p0 + new Vector2(Screen.width * off, 0f);
                    yield return Move(PointerInput.SimPos, to, 0.2f, true);
                    yield return new WaitForSeconds(0.45f);
                    lean[(left ? "L" : "R") + off.ToString("+0.0;-0.0;0", CI)] = a.RodAngles.y;
                    Log(string.Format(CI, "[HOLD] wind-up {0}-handed finger {1:+0.0;-0.0;0} W: rod pitch {2:0.0} lean {3:+0.0;-0.0;0.0} (pose {4})", left ? "left" : "right", off, a.RodAngles.x, a.RodAngles.y, a.Pose));
                    if (off == 0.2f) yield return Shot($"hold_windup_{(left ? "left" : "right")}");
                }
                yield return Flick(3.4f, 15f);
                for (float w = 0f; w < 6f && ctl.State != FishingController.S.Waiting; w += Time.deltaTime) yield return null;
                var s = ctl.Tackle.Surface;
                land[left] = Mathf.Atan2(s.x - a.X, s.z) * Mathf.Rad2Deg;
                Log(string.Format(CI, "[HOLD] {0}-handed flick 15 deg landed at ({1:0.00}, {2:0.00}): yaw {3:+0.0;-0.0}", left ? "left" : "right", s.x, s.z, land[left]));
            }
            float worst = 0f;
            foreach (var off in new[] { "+0.2", "-0.2", "0" })
            {
                string mirror = off == "+0.2" ? "-0.2" : off == "-0.2" ? "+0.2" : "0";
                worst = Mathf.Max(worst, Mathf.Abs(lean["L" + off] + lean["R" + mirror]));
            }
            HCheck($"the wind-up mirrored: left-handed lean = -(right-handed lean for the mirrored finger), worst {N(worst, "0.00")} deg (right {N(lean["R+0.2"], "+0.0;-0.0")} / {N(lean["R0"], "+0.0;-0.0")} / {N(lean["R-0.2"], "+0.0;-0.0")}, left {N(lean["L+0.2"], "+0.0;-0.0")} / {N(lean["L0"], "+0.0;-0.0")} / {N(lean["L-0.2"], "+0.0;-0.0")}); the finger leans the top towards it either hand",
                worst < 1f && lean["R+0.2"] > lean["R-0.2"] && lean["L+0.2"] > lean["L-0.2"]);
            HCheck($"the same flick lands at the same yaw either hand ({N(land[false], "+0.0;-0.0")} / {N(land[true], "+0.0;-0.0")} deg): the throw and its fan are the feet's", Mathf.Abs(land[false] - land[true]) < 2f);
            yield return BackToReady(ctl);
        }

        // ------------------------------------------------------------------ the crops
        /// <summary>A 1:1 crop of the pixel view round the angler (wide enough for the rod at its limits).</summary>
        static Texture2D HoldCrop(FishingController ctl)
        {
            var pv = PixelView.Current;
            var rt = pv.Target;
            int w = rt.width, h = rt.height;
            var cam = pv.WorldCamera.transform.position;
            var p2 = ctl.Stage.P.To2D(ctl.Angler.Feet) + ctl.Stage.DeckBob;
            var feet = new Vector2((p2.x - cam.x) * PixelView.PPU + w * 0.5f, (p2.y - cam.y) * PixelView.PPU + h * 0.5f);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(feet.x) - HoldCropW / 2, 0, w - HoldCropW);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(feet.y) - 12, 0, h - HoldCropH);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var f = new Texture2D(HoldCropW, HoldCropH, TextureFormat.RGB24, false);
            AutoShot.Read(f, new Rect(x0, y0, HoldCropW, HoldCropH));
            f.Apply(false);
            RenderTexture.active = prev;
            return f;
        }

        /// <summary>Tiles in rows of <paramref name="cols"/> (2 px magenta gaps; a missing tile stays magenta), 1:1 and x3.</summary>
        static void SaveGrid(List<Texture2D> tiles, int cols, string path)
        {
            int rows = (tiles.Count + cols - 1) / cols;
            int W = cols * (HoldCropW + 2) - 2, H = rows * (HoldCropH + 2) - 2;
            var img = new Texture2D(W, H, TextureFormat.RGB24, false);
            var fill = new Color32[W * H];
            for (int q = 0; q < fill.Length; q++) fill[q] = new Color32(255, 0, 255, 255);
            img.SetPixels32(fill);
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == null) continue;
                int c = i % cols, r = i / cols;
                img.SetPixels(c * (HoldCropW + 2), H - (r + 1) * HoldCropH - r * 2, HoldCropW, HoldCropH, tiles[i].GetPixels());
                Destroy(tiles[i]);
            }
            img.Apply(false);
            File.WriteAllBytes(path + ".png", img.EncodeToPNG());
            const int Z = 3;
            var src = img.GetPixels32();
            var big = new Texture2D(W * Z, H * Z, TextureFormat.RGB24, false);
            var dst = new Color32[W * Z * H * Z];
            for (int y = 0; y < H * Z; y++)
            for (int x = 0; x < W * Z; x++)
                dst[y * W * Z + x] = src[(y / Z) * W + x / Z];
            big.SetPixels32(dst);
            big.Apply(false);
            File.WriteAllBytes(path + "_x3.png", big.EncodeToPNG());
            Destroy(img);
            Destroy(big);
        }
    }
}
