using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto occlusion: what is drawn over the front layer is hidden where the stand is nearer (<see cref="FrontOcclusion"/>).
    /// <code>
    /// -fkfresh -fkrich -fksave occ -fkscene Fishing -fkauto occlusion -fkshots &lt;dir&gt; [-fkoccstages lake,stream,...]
    /// </code>
    /// Per stage (lake, stream, ocean, sea, swamp, ice, cave): the idle (the bait dangling at the rod tip), a hooked fish held
    /// at the stand's foot at the end of a fight (the closest the fight lets it come: the line runs down behind the pier /
    /// rock / boat / quay), a jump beside the stand (rising, at the top), the landing (swung up from there; hanging at the rod
    /// tip), plus on the lake the dawn -> day cross-fade and on the ocean a second deck bob. Each moment is frozen and drawn
    /// twice, hiding off (before) then on (after): occ_&lt;stage&gt;_&lt;moment&gt;_off / _on.png (crops of the 480x270 view) and
    /// an [OCC] SHOT line; [OCC] CHECK lines: every pixel that changed lies on a solid front-layer texel (nothing hidden where
    /// the front layer has nothing), something is hidden where it must be (the line behind the stand) and nothing where
    /// nothing is behind the stand (the idle, the fish hanging at the rod tip).
    /// <para>Every frame in between is watched too (<see cref="OcclusionWatch"/>: not only the frozen moments): the held
    /// moments as one window (&lt;stage&gt;_held), then a live fight close in off to his right (&lt;stage&gt;_live: the
    /// fight model runs, the rod is leant right / none / left in turn so the bent rod comes past the hat, two jumps, then
    /// the landing from close in). [OCC] CHECK &lt;stage&gt;_held_perframe / _live_perframe / _perframe: no frame exposed
    /// (nothing shown over a nearer part of the front layer, the line or the fish never over it for only 1-2 frames, no
    /// state error); &lt;stage&gt;_tip_steady: the rod tip never jumps for 1-2 frames and back. Strips of flagged frames and of
    /// the live fight every 1.5 s: occw_*.png; every watched frame: occwatch.csv.</para>
    /// </summary>
    public partial class AutoPilot
    {
        int occFails;
        static readonly CultureInfo CIx = CultureInfo.InvariantCulture;
        const int OccCropW = 160, OccCropH = 100;

        void XCheck(string name, bool ok, string numbers)
        {
            if (!ok) occFails++;
            Debug.Log($"[OCC] CHECK {name} {(ok ? "PASS" : "FAIL")} {numbers}");
        }

        IEnumerator OcclusionTest()
        {
            yield return new WaitForSeconds(2f);
            UnityEngine.Random.InitState(4242);
            PointerInput.SimDpi = 0f;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            var sd = Game.Data;
            sd.sweepHint = sd.tideHint = sd.driftHint = sd.mendHint = sd.sideHint = sd.timeHint = true;
            FishingController.NoBites = true;
            var watch = OcclusionWatch.Ensure(shots, true);
            var list = (Arg("-fkoccstages") ?? "lake,stream,ocean,sea,swamp,ice,cave").Split(',');
            foreach (var id in list)
            {
                yield return GoStage(id, 3f);
                var ctl = FindAnyObjectByType<FishingController>();
                if (ctl == null || ctl.Stage.Def.id != id)
                {
                    XCheck(id + "_scene", false, "no scene");
                    continue;
                }
                yield return OccStage(ctl);
            }
            FishingController.NoBites = false;
            FrontOcclusion.Enabled = true;
            Time.timeScale = 1f;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            watch.Flush();
            Debug.Log($"[OCC] occlusion test done: {occFails} failed");
            Application.Quit();
        }

        static (string fish, float cm, string bait) OccFish(string id) => id switch
        {
            "lake" => ("largemouth_bass", 45f, "bait_worm"),
            "stream" => ("rainbow_trout", 50f, "bait_spinner"),
            "sea" => ("black_porgy", 45f, "bait_worm"),
            "swamp" => ("catfish", 60f, "bait_worm"),
            "ice" => ("burbot", 50f, "bait_worm"),
            "ocean" => ("yellowtail", 90f, "bait_jig"),
            "cave" => ("crystal_koi", 50f, "bait_worm"),
            _ => ("carp", 50f, "bait_worm"),
        };

        IEnumerator OccStage(FishingController ctl)
        {
            var st = ctl.Stage;
            string id = st.Def.id;
            var L = st.L;
            var a = ctl.Angler;
            bool ice = L.IsIce;
            XCheck(id + "_map", FrontOcclusion.StageId == id && FrontOcclusion.Ready, $"bound {FrontOcclusion.StageId ?? "-"} ready {FrontOcclusion.Ready}");
            var watch = OcclusionWatch.I;
            watch?.Begin(id + "_held");

            // 0. the idle: the bait dangling at the rod tip is in front of everything (nothing hidden)
            yield return ToReady(ctl);
            yield return new WaitForSeconds(0.6f);
            yield return OccShot(ctl, "idle", () => st.P.To2D(a.RodTip) + st.DeckBob + new Vector2(0f, -0.8f), 0);

            // 0b. the bait perched on a front-layer prop's top (tested a little nearer than where it rests): the top it sits
            // on must not hide it
            var (fishId0, _, bait0) = OccFish(id);
            string perchId = id switch { "lake" => "boat", "stream" => "fl2.0", "sea" => "tet18", "swamp" => "postR", "cave" => "Chunk8", _ => null };
            var po = perchId != null && st.Obstacles != null ? st.Obstacles.Get(perchId) : null;
            if (po != null)
            {
                EquipTest(bait0, ctl);
                yield return null;
                if (ctl.DebugPlaceRig(new Vector3(a.X + 0.5f, 0f, L.zNear + 4f)))
                {
                    yield return new WaitForSeconds(0.5f);
                    var at = new Vector3(po.C.x, po.top, po.C.y);
                    ctl.Tackle.Perch(at, po);
                    yield return new WaitForSeconds(0.6f);
                    yield return OccShot(ctl, "perch", () => st.P.To2D(at) + st.DeckBob, -1, 4);
                }
                yield return ToReady(ctl);
            }

            // a hooked fish, held (the fight model stands still)
            bool ocean = id == "ocean";
            SteerGear(ocean ? "rod_biggame" : "rod_carbon", ocean ? "reel_baitcast" : "reel_highgear", ocean ? "line_pe3" : "line_nylon4");
            var (fishId, cm, bait) = OccFish(id);
            EquipTest(bait, ctl);
            yield return null;
            float x = a.X;
            var rig = ice ? new Vector3(L.holeX, 0f, L.holeZ) : new Vector3(x + 0.5f, 0f, L.zNear + 4f);
            if (!ctl.DebugPlaceRig(rig))
            {
                XCheck(id + "_rig", false, $"state {ctl.State}");
                yield break;
            }
            yield return new WaitForSeconds(0.7f);
            if (!ctl.DebugHook(GameDatabase.GetFish(fishId), cm, 4242, new Vector3(rig.x, -1f, rig.z)))
            {
                XCheck(id + "_hook", false, $"state {ctl.State} tackle {ctl.Tackle.State}");
                yield break;
            }
            ctl.Fight.Hold(9999f);
            var fish = ctl.Hooked;

            // 1. the end of the fight: at the stand's foot (the fight's closest, zNear + 0.2), the line running down to it
            var under = ice ? new Vector3(L.holeX, -1.2f, L.holeZ) : new Vector3(x + 0.35f, -0.7f, L.zNear + 0.2f);
            ctl.DebugFishHold = under;
            yield return new WaitForSeconds(1.2f);
            Vector2 bob0 = st.DeckBob;
            if (ocean)
            {
                // (the boat bobs on the swell: shot with the deck up or down a pixel, the map must move with it)
                for (float w = 0f; w < 12f && st.DeckBob == Vector2.zero; w += Time.deltaTime) yield return null;
                bob0 = st.DeckBob;
            }
            int expectUnder = ice ? -1 : 1;
            yield return OccShot(ctl, "under", () => st.P.To2D(a.WaterEntry), expectUnder);
            if (ocean)
            {
                for (float w = 0f; w < 14f && st.DeckBob == bob0; w += Time.deltaTime) yield return null;
                yield return OccShot(ctl, "under_bob", () => st.P.To2D(a.WaterEntry), 1);
            }
            if (id == "lake")
            {
                // the period cross-fade (08:00: dawn -> day, F 0.5; the map is the same for every period)
                GameClock.Min = 480f;
                yield return new WaitForSeconds(1.2f);
                yield return OccShot(ctl, "under_fade", () => st.P.To2D(a.WaterEntry), 1);
                GameClock.Min = GameClock.Centre(Period.Day);
                yield return new WaitForSeconds(1.2f);
            }

            // 2. a jump beside the stand: rising (its lower half behind the stand's edge) and at the top
            if (!ice)
            {
                ctl.DebugFishHold = new Vector3(x + 1.1f, -0.3f, L.zNear + 0.3f);
                yield return new WaitForSeconds(0.8f);
                if (ctl.DebugJump(1.6f))
                {
                    for (float w = 0f; w < 3f && fish != null && fish.JumpT < 0.12f; w += Time.deltaTime) yield return null;
                    yield return OccShot(ctl, "jump_low", () => st.P.To2D(new Vector3(fish.Pos.x, Mathf.Max(0f, fish.Pos.y), fish.Pos.z)), -1);
                    for (float w = 0f; w < 3f && fish != null && fish.JumpT >= 0f && fish.JumpT < 0.5f; w += Time.deltaTime) yield return null;
                    yield return OccShot(ctl, "jump_top", () => st.P.To2D(new Vector3(fish.Pos.x, Mathf.Max(0f, fish.Pos.y), fish.Pos.z)), -1);
                    for (float w = 0f; w < 3f && fish != null && fish.JumpT >= 0f; w += Time.deltaTime) yield return null;
                }
                else XCheck(id + "_jump", false, "no jump");
            }

            // 3. the landing from the stand's foot: swung up (hidden until clear of the stand), then hanging at the rod tip
            ctl.DebugFishHold = under;
            yield return new WaitForSeconds(0.8f);
            if (ctl.DebugLand())
            {
                float t0 = Time.time;
                for (float w = 0f; w < 2f && fish != null && fish.Pos.y < 0.35f && Time.time - t0 < 0.3f; w += Time.deltaTime) yield return null;
                yield return OccShot(ctl, "land_low", () => st.P.To2D(new Vector3(fish.Pos.x, Mathf.Max(0f, fish.Pos.y), fish.Pos.z)), -1);
                for (float w = 0f; w < 4f && ctl.State != FishingController.S.Result; w += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(0.4f);
                XCheck(id + "_land_clear", fish != null && fish.ClearOfFront, $"clear {(fish != null && fish.ClearOfFront)} bottom {(fish != null ? fish.AirBottom : 0f).ToString("0.00", CIx)} stand {L.standH.ToString("0.00", CIx)}");
                yield return OccShot(ctl, "land_hang", () => st.P.To2D(fish.Pos) + st.DeckBob, 0);
                for (float w = 0f; w < 4f && !HasButton("놓아주기"); w += Time.deltaTime) yield return null;
                Click("놓아주기");
                yield return new WaitForSeconds(0.6f);
            }
            else XCheck(id + "_land", false, $"state {ctl.State}");
            ctl.DebugFishHold = null;
            yield return ToReady(ctl);
            if (watch != null)
            {
                var th = watch.End();
                XCheck(id + "_held_perframe", th.exposed == 0 && th.stateFrames == 0, th.ToString());
            }

            // 4. a live fight close in (every frame watched), then the whole stage's frames
            if (!ice) yield return OccLive(ctl);
            if (watch != null)
            {
                var ts = watch.StageTally(id);
                XCheck(id + "_perframe", ts.exposed == 0 && ts.stateFrames == 0, ts.ToString());
                XCheck(id + "_tip_steady", ts.tipFlips == 0, $"{ts.tipFlips} rod tip flips (4+ px for 1-2 frames and back), {ts.hatFlips} hat side flips");
            }
        }

        /// <summary>
        /// A live fight close in, off to his right (where the rod bent towards the line comes near the hat): the fight model
        /// runs (not held), the autopilot winds while the fish rests, leans the rod right / none / left in turn (side
        /// pressure: swept right the bent rod comes past the hat); 8 s with the fish ~2 m out (never landing it by winding),
        /// then 12 s right under the stand's edge (the line kept just longer than the landing's, wound against: the steep,
        /// taut line of the end of a fight, where the hat guard works hardest), three jumps; then it is landed from close in.
        /// Every frame is watched (&lt;stage&gt;_live); a strip every 1.5 s.
        /// </summary>
        IEnumerator OccLive(FishingController ctl)
        {
            var st = ctl.Stage;
            var L = st.L;
            var a = ctl.Angler;
            string id = st.Def.id;
            var watch = OcclusionWatch.I;
            var (fishId, cm, _) = OccFish(id);
            yield return ToReady(ctl);
            var rig = new Vector3(Mathf.Clamp(a.X + 2.5f, -L.xLim + 1f, L.xLim - 1f), 0f, L.zNear + 3.5f);
            if (!ctl.DebugPlaceRig(rig))
            {
                XCheck(id + "_live_rig", false, $"state {ctl.State}");
                yield break;
            }
            yield return new WaitForSeconds(0.7f);
            if (!ctl.DebugHook(GameDatabase.GetFish(fishId), cm, 4243, new Vector3(rig.x, -0.8f, rig.z)))
            {
                XCheck(id + "_live_hook", false, $"state {ctl.State} tackle {ctl.Tackle.State}");
                yield break;
            }
            watch?.Begin(id + "_live");
            var c = Scr(0.72f, 0.4f);
            float r = Screen.height * 0.13f, ang = 0f, ws = CircleGesture.Reversed ? -1f : 1f;
            int jumps = 0, snaps = 0;
            bool giving = false, winding = true;
            PointerInput.SimActive = true;
            // (the line never wound in as short as the landing's, which would end the fight: first ~1.5 m longer, then kept
            // just longer than it: the fish right under the stand's edge, the line steeply down)
            float keep = Vector3.Distance(a.RodTip, new Vector3(a.X, 0f, L.zNear + 0.4f)) + 0.9f;
            for (float t = 0f; t < 20f && ctl.State == FishingController.S.Fighting; t += Time.deltaTime)
            {
                var f = ctl.Fight;
                var hk = ctl.Hooked;
                if (f == null || hk == null) break;
                float dt = Time.deltaTime;
                bool running = ctl.FishRun != 0 || f.State == FightModel.Phase.Burst;
                bool close = t >= 8f;   // the first 8 s ~2 m out, then close in under the stand
                bool far = close || hk.Pos.z > L.zNear + 2.2f || Mathf.Abs(hk.Pos.x - a.X) > 3.5f;
                // (as a hand would, not switching every other frame: gives line from 0.95 of the line's strength down to
                // 0.85, stops winding above 0.8 until it is back under 0.55, as the fish scenario does)
                if (f.TensionRatio > 0.95f) giving = true;
                else if (f.TensionRatio < 0.85f) giving = false;
                if (f.TensionRatio > 0.8f) winding = false;
                else if (f.TensionRatio < 0.55f) winding = true;
                if (giving) ang += ws * dt * 1.0f * Mathf.PI * 2f;                                               // give line
                else if (far && winding && !running && !f.Jumping) ang -= ws * dt * 1.6f * Mathf.PI * 2f;       // wind
                f.Line = close ? Mathf.Max(keep, Mathf.MoveTowards(f.Line, keep, dt * 2f)) : Mathf.Max(keep + 1.5f, f.Line);
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                int lean = Mathf.FloorToInt(t / (close ? 1f : 1.75f)) % 3;   // right, none, left
                PointerInput.SimRight = lean == 0;
                PointerInput.SimLeft = lean == 2;
                if ((jumps == 0 && t > 4f) || (jumps == 1 && t > 10f) || (jumps == 2 && t > 15f))
                    if (ctl.DebugJump(0.9f)) jumps++;
                if (snaps < 12 && t > 1.5f * (snaps + 1))
                {
                    snaps++;
                    watch?.Snap($"live_{id}_{snaps}", Vector2.Lerp(ctl.RodTip2D, st.P.To2D(a.LineUnderwater ? a.WaterEntry : hk.Pos), 0.6f));
                }
                yield return null;
            }
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            // landed from close in: lifted past the stand's edge, then hanging at the rod tip
            if (ctl.State == FishingController.S.Fighting) ctl.DebugLand();
            for (float w = 0f; w < 4f && ctl.State != FishingController.S.Result; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.6f);
            if (watch != null)
            {
                var tl = watch.End();
                XCheck(id + "_live_perframe", tl.exposed == 0 && tl.stateFrames == 0, tl.ToString());
            }
            for (float w = 0f; w < 4f && !HasButton("놓아주기"); w += Time.deltaTime) yield return null;
            Click("놓아주기");
            yield return new WaitForSeconds(0.6f);
            yield return ToReady(ctl);
        }

        /// <summary>
        /// Freezes the moment and draws it with hiding off, then on: crops around <paramref name="focus"/> (pixel scene), an
        /// [OCC] SHOT line and the checks. <paramref name="expect"/>: 1 something must be hidden, 0 nothing may be, -1 either.
        /// </summary>
        IEnumerator OccShot(FishingController ctl, string name, Func<Vector2> focus, int expect, int keepR = 0)
        {
            string id = ctl.Stage.Def.id;
            float ts = Time.timeScale;
            // (hiding goes off on purpose here: the per-frame watch pauses)
            var watch = OcclusionWatch.I;
            if (watch != null) watch.Paused = true;
            Time.timeScale = 0f;
            yield return null;
            FrontOcclusion.Enabled = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            var off = GrabRT();
            FrontOcclusion.Enabled = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            var on = GrabRT();
            // off once more: what changes by itself between frames while frozen (the ocean's swell drifts with the current,
            // which runs on the real clock) is not the hiding's doing
            FrontOcclusion.Enabled = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            var off2 = GrabRT();
            FrontOcclusion.Enabled = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            var on2 = GrabRT();
            var pv = PixelView.Current;
            if (off == null || on == null || off2 == null || on2 == null || pv == null)
            {
                XCheck($"{id}_{name}_grab", false, "no render target");
                Time.timeScale = ts;
                if (watch != null) watch.Paused = false;
                yield break;
            }
            Vector2 cam = pv.WorldCamera.transform.position;
            int w = off.width, h = off.height;
            Vector2 World(int px, int py) => new Vector2(cam.x + (px + 0.5f - w * 0.5f) / PixelView.PPU, cam.y + (py + 0.5f - h * 0.5f) / PixelView.PPU);
            var a = off.GetPixels32();
            var b = on.GetPixels32();
            var a2 = off2.GetPixels32();
            var b2 = on2.GetPixels32();
            int diff = 0, onFront = 0, offFront = 0, noise = 0;
            var offAt = new System.Text.StringBuilder();
            static bool Same(Color32 p, Color32 q) => p.r == q.r && p.g == q.g && p.b == q.b;
            for (int i = 0; i < a.Length; i++)
            {
                if (!Same(a[i], a2[i]) || !Same(b[i], b2[i]))
                {
                    noise++;
                    continue;
                }
                if (Same(a[i], b[i])) continue;
                diff++;
                var wp = World(i % w, i / w);
                if (float.IsPositiveInfinity(FrontOcclusion.OccluderAt(wp, out var tx)))
                {
                    offFront++;
                    if (offFront <= 4)
                    {
                        // (how far the nearest solid texel is: 1 = on the front layer's edge)
                        int near = 9;
                        for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            if (!float.IsPositiveInfinity(FrontOcclusion.OccluderAt(wp + new Vector2(dx, dy) / PixelView.PPU, out _)))
                                near = Mathf.Min(near, Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)));
                        offAt.Append($" px({i % w},{h - 1 - i / w}) texel({tx.x},{tx.y}) edge {near}");
                    }
                }
                else onFront++;
            }
            // the line above the water as the CPU sees it: samples hidden behind the front layer
            var an = ctl.Angler;
            var P = ctl.Stage.P;
            int lineN = 0, lineHidden = 0;
            for (int i = 0; i <= 40; i++)
            {
                if (!an.LineAt(i / 40f, out var p, out var lb)) break;
                lineN++;
                var p2 = P.To2D(p, out float d) + lb;
                if (d > FrontOcclusion.OccluderAt(p2, out _) + FrontOcclusion.Bias) lineHidden++;
            }
            // crops (texture rows are bottom-up; the crop is centred on the focus)
            var f = focus();
            int fx = Mathf.FloorToInt((f.x - cam.x) * PixelView.PPU + w * 0.5f), fy = Mathf.FloorToInt((f.y - cam.y) * PixelView.PPU + h * 0.5f);
            int x0 = Mathf.Clamp(fx - OccCropW / 2, 0, w - OccCropW), y0 = Mathf.Clamp(fy - OccCropH / 2, 0, h - OccCropH);
            SaveCrop(off, x0, y0, $"occ_{id}_{name}_off.png");
            SaveCrop(on, x0, y0, $"occ_{id}_{name}_on.png");
            // what the hiding removed (the frame-to-frame changes left out), for the sheet
            var mask = new Texture2D(OccCropW, OccCropH, TextureFormat.RGB24, false);
            var mp = new Color32[OccCropW * OccCropH];
            for (int my = 0; my < OccCropH; my++)
            for (int mx = 0; mx < OccCropW; mx++)
            {
                int i = (y0 + my) * w + x0 + mx;
                bool hid = Same(a[i], a2[i]) && Same(b[i], b2[i]) && !Same(a[i], b[i]);
                mp[my * OccCropW + mx] = hid ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            }
            mask.SetPixels32(mp);
            mask.Apply(false);
            File.WriteAllBytes(Path.Combine(shots, $"occ_{id}_{name}_mask.png"), mask.EncodeToPNG());
            Destroy(mask);
            if (name == "under") File.WriteAllBytes(Path.Combine(shots, $"occ_{id}_{name}_full.png"), on.EncodeToPNG());
            var bob = ctl.Stage.DeckBob * PixelView.PPU;
            Debug.Log(string.Format(CIx, "[OCC] SHOT {0} {1} crop {2} {3} {4} {5} focus {6} {7} diff {8} onFront {9} offFront {10} line {11}/{12} bob {13:0} {14:0} noise {17} state {15} fish {16}",
                id, name, x0, h - y0 - OccCropH, OccCropW, OccCropH, fx, h - 1 - fy, diff, onFront, offFront, lineHidden, lineN, bob.x, bob.y, ctl.State,
                ctl.Hooked != null ? string.Format(CIx, "({0:0.00}, {1:0.00}, {2:0.00}) jump {3:0.00}", ctl.Hooked.Pos.x, ctl.Hooked.Pos.y, ctl.Hooked.Pos.z, ctl.Hooked.JumpT) : "-", noise));
            XCheck($"{id}_{name}_on_front", offFront == 0, $"{offFront} of {diff} changed pixels off the front layer's solid texels{offAt}");
            if (keepR > 0)
            {
                // nothing hidden within keepR px of the focus (the perched bait)
                int kept = 0;
                for (int py = Mathf.Max(0, fy - keepR); py <= Mathf.Min(h - 1, fy + keepR); py++)
                for (int px = Mathf.Max(0, fx - keepR); px <= Mathf.Min(w - 1, fx + keepR); px++)
                {
                    int i = py * w + px;
                    if (Same(a[i], a2[i]) && Same(b[i], b2[i]) && !Same(a[i], b[i])) kept++;
                }
                XCheck($"{id}_{name}_shown", kept == 0, $"{kept} px hidden within {keepR} px of it");
            }
            if (expect == 1) XCheck($"{id}_{name}_hidden", diff > 0, $"{diff} px hidden (line samples behind the stand {lineHidden}/{lineN})");
            if (expect == 0) XCheck($"{id}_{name}_nothing_hidden", diff == 0, $"{diff} px changed (line samples behind the stand {lineHidden}/{lineN})");
            Destroy(off);
            Destroy(on);
            Destroy(off2);
            Destroy(on2);
            Time.timeScale = ts;
            yield return null;
            if (watch != null) watch.Paused = false;
        }

        void SaveCrop(Texture2D src, int x0, int y0, string file)
        {
            var c = new Texture2D(OccCropW, OccCropH, TextureFormat.RGB24, false);
            c.SetPixels(src.GetPixels(x0, y0, OccCropW, OccCropH));
            c.Apply(false);
            File.WriteAllBytes(Path.Combine(shots, file), c.EncodeToPNG());
            Destroy(c);
        }
    }
}
