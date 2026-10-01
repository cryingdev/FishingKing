using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto obstacles started on the ocean (-fkstage ocean): its open-water structure (Docs/obstacles_spec.md 3.7) instead
    /// of the stream / lake / swamp set. [OBST] CHECK lines:
    /// <list type="bullet">
    /// <item>ocean_aim: wound up, the reef and the weed mat are outlined in open water (their dashes off the painted bow),
    /// the hull's only under it; the aim layer sorts under the front layer;</item>
    /// <item>ocean_snag_reef / ocean_snag_kelp / ocean_snag_open: a spoon wound through the reef 3 m down and a minnow
    /// through the weed mat snag on them (SnagMult 999, as the stream's test), the same spoon in open water does not; a
    /// 톡 frees the reef snag;</item>
    /// <item>ocean_bites: the real BiteMult of the fish about with the hook in the reef's / the mat's cover against open
    /// water (x1.35 for the species that hold there, x1.15 the others);</item>
    /// <item>ocean_cover: 방어 near the reef picks reef.cover, 만새기 near the mat kelp.cover; a hooked 방어 whose run is sent
    /// to the reef rubs the line on it (바위);</item>
    /// <item>ocean_drift: a float in open water still drifts on the ocean's current; ocean_lurk: the structure lies short
    /// of the ocean legends' lurk band.</item>
    /// </list>
    /// Shots: obst_ocean_aim, obst_ocean_snag, obst_ocean_rub, obst_ocean_show.
    /// </summary>
    public partial class AutoPilot
    {
        /// <summary>The canvas pixels of the ocean's front layer that are painted (the bow), like WaterFx's mask.</summary>
        static bool[] FrontOpaque(string stageId, int W, int H)
        {
            var s = Art.Stage(stageId + "_front");
            if (s == null || s.texture.width != W || s.texture.height != H) return null;
            var tex = s.texture;
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            t.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Destroy(t);
            var m = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) m[i] = px[i].a > 8;
            return m;
        }

        IEnumerator OceanObstacles(FishingController ctl)
        {
            var obs = ctl.Stage.Obstacles;
            var L = ctl.Stage.L;
            var reef = obs.Get("reef");
            var kelp = obs.Get("kelp");
            var reefC = obs.Get("reef.cover");
            var kelpC = obs.Get("kelp.cover");
            if (reef == null || kelp == null || reefC == null || kelpC == null)
            {
                OCheck("ocean_data", false, "reef / kelp / their covers missing in obstacles_ocean.json");
                yield break;
            }
            Log(string.Format(CIb, "[OBST] ocean: {0} snag, {1} weed, {2} cover", obs.Snags.Count, obs.Weeds.Count, obs.Covers.Count));
            FishingController.NoBites = true;

            // ---- 1. the aim outlines
            yield return ToReady(ctl);
            EquipTest("bait_spinner", ctl);
            yield return null;
            yield return WindUp();
            yield return new WaitForSeconds(1f);
            var ov = ctl.Overlay;
            yield return NamedShot("obst_ocean_aim");
            int W = L.widthPx > 0 ? L.widthPx : 640, H = L.heightPx > 0 ? L.heightPx : 400;
            var front = FrontOpaque(L.id, W, H);
            var seen = new Dictionary<string, (int all, int open)>();
            if (ov != null)
                foreach (var kv in ov.AimPixels)
                {
                    int open = 0;
                    foreach (int i in kv.Value)
                        if (front == null || !front[i]) open++;
                    seen[kv.Key] = (kv.Value.Count, open);
                }
            (int all, int open) Seen(string id) => seen.TryGetValue(id, out var v) ? v : (0, 0);
            var sR = Seen("reef");
            var sK = Seen("kelp");
            var sH = Seen("hull");
            var sHs = Seen("hull.skirt");
            float a = ov != null ? ov.Alpha : 0f;
            int order = ov != null ? ov.AimOrder : 999;
            bool aimOk = ctl.State == FishingController.S.Aiming && ctl.WindUpArmed && a >= 0.99f && front != null
                         && sR.open >= 20 && sR.open == sR.all && sK.open >= 20 && sK.open == sK.all && order < StageView.OrderFront;
            OCheck("ocean_aim", aimOk,
                string.Format(CIb, "{0} zones outlined, alpha {1:0.00}; dash px in open water / drawn: reef {2}/{3}, kelp {4}/{5}, hull {6}/{7}, hull.skirt {8}/{9}; aim order {10} < front {11}",
                    ov != null ? ov.ZonesDrawn : 0, a, sR.open, sR.all, sK.open, sK.all, sH.open, sH.all, sHs.open, sHs.all, order, StageView.OrderFront));
            yield return Move(PointerInput.SimPos, Scr(0.5f, 0.55f), 2.2f, true);
            PointerInput.SimDown = false;
            yield return null;
            for (float w = 0f; w < 2f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.4f);

            // ---- 2. snags: the reef (a spoon 3 m down), the weed mat (a minnow 0.8 m down), open water (none)
            var reefAt = new Vector3(reef.x, 0f, reef.z + 0.6f);
            int s0 = ctl.SnagCount;
            yield return SnagHere(ctl, "bait_spoon", reefAt, 3.0f, 0.6f);
            var sn = ctl.Tackle.Snag;
            bool reefSnag = ctl.State == FishingController.S.Snagged && ctl.SnagCount > s0 && sn != null && sn.zone == reef;
            string reefWhere = sn != null ? string.Format(CIb, "{0} at ({1:0.00}, {2:0.00}, {3:0.00})", sn.zone.id, sn.at.x, sn.at.y, sn.at.z) : "-";
            if (reefSnag)
            {
                yield return new WaitForSeconds(0.6f);
                yield return NamedShot("obst_ocean_snag");
                yield return new WaitForSeconds(1.1f);
                for (int k = 0; k < 4 && ctl.State == FishingController.S.Snagged; k++)
                {
                    yield return LureFlick(2.2f, 0.12f);
                    for (float w = 0f; w < 0.5f && ctl.State == FishingController.S.Snagged; w += Time.deltaTime) yield return null;
                }
            }
            OCheck("ocean_snag_reef", reefSnag && ctl.State == FishingController.S.Waiting && ctl.LastFreeWay == "tok",
                $"spoon wound through the reef 3 m down: snagged {reefSnag} on {reefWhere}, freed by '{ctl.LastFreeWay}', state {ctl.State}");
            yield return ToReady(ctl);
            s0 = ctl.SnagCount;
            yield return SnagHere(ctl, "bait_minnow", new Vector3(kelp.x + 1.2f, 0f, kelp.z + 0.4f), 0.8f, 0.6f);
            sn = ctl.Tackle.Snag;
            bool kelpSnag = ctl.State == FishingController.S.Snagged && ctl.SnagCount > s0 && sn != null && sn.zone == kelp;
            OCheck("ocean_snag_kelp", kelpSnag && sn.kind == "weed",
                $"minnow wound through the weed mat 0.8 m down: snagged {kelpSnag} on {(sn != null ? sn.zone.id + " (" + sn.kind + ")" : "-")}");
            yield return ToReady(ctl);
            s0 = ctl.SnagCount;
            var openAt = new Vector3(0f, 0f, 20f);
            yield return SnagHere(ctl, "bait_spoon", openAt, 3.0f, 0.6f);
            OCheck("ocean_snag_open", ctl.SnagCount == s0 && ctl.State != FishingController.S.Snagged,
                $"the same spoon wound 2 s in open water at (0, 20) with SnagMult 999: snags {ctl.SnagCount - s0}, state {ctl.State}");
            yield return ToReady(ctl);

            // ---- 3. bites near the structure: the real BiteMult with the hook in a cover vs open water
            float snagWas = Obstacles.SnagMult;
            Obstacles.SnagMult = 0f;
            EquipTest("bait_jig", ctl);
            yield return null;
            var spots = new[] { ("reef.cover", new Vector3(reefC.hx, 0f, reefC.hz)), ("kelp.cover", new Vector3(kelpC.hx, 0f, kelpC.hz)), ("open", openAt) };
            var mults = new Dictionary<FishAgent, float[]>();
            for (int k = 0; k < spots.Length; k++)
            {
                yield return ToReady(ctl);
                ctl.DebugPlaceRig(spots[k].Item2);
                yield return new WaitForSeconds(0.3f);
                ctl.Tackle.Surface = spots[k].Item2;
                ctl.Tackle.Depth = 4f;
                foreach (var f in ctl.Spawner.Fish)
                {
                    if (!mults.TryGetValue(f, out var arr)) mults[f] = arr = new[] { -1f, -1f, -1f };
                    arr[k] = ctl.BiteMult(f);
                }
            }
            Obstacles.SnagMult = snagWas;
            bool bitesOk = true;
            int matched = 0, checkedN = 0;
            var lines = new List<string>();
            foreach (var kv in mults)
            {
                var f = kv.Key;
                var m = kv.Value;
                if (m[2] <= 0.25f + 1e-3f || m[0] < 0f || m[1] < 0f) continue;       // (not about now, or clamped)
                for (int k = 0; k < 2; k++)
                {
                    var c = k == 0 ? reefC : kelpC;
                    float want = f.Sp.coverSeek >= 0.3f && Obstacles.CoverMatch(c, f.Sp) ? 1.35f : 1.15f;
                    float got = m[k] / m[2];
                    bool clamp = m[k] >= 2f - 1e-3f;
                    if (!clamp && Mathf.Abs(got - want) > 0.01f) bitesOk = false;
                    if (want > 1.3f && !clamp) matched++;
                    checkedN++;
                    if (lines.Count < 8) lines.Add(string.Format(CIb, "{0} {1} x{2:0.00} (want {3:0.00})", f.Sp.name, c.id, got, want));
                }
            }
            var yt = GameDatabase.GetFish("yellowtail");
            var mh = GameDatabase.GetFish("mahi_mahi");
            var hookReef = new Vector3(reefC.hx, -4f, reefC.hz);
            var hookKelp = new Vector3(kelpC.hx, -4f, kelpC.hz);
            var hookOpen = new Vector3(openAt.x, -4f, openAt.z);
            float ytR = obs.StructureMult(hookReef, yt), mhK = obs.StructureMult(hookKelp, mh), mhR = obs.StructureMult(hookReef, mh), ytO = obs.StructureMult(hookOpen, yt);
            bitesOk &= Mathf.Approximately(ytR, 1.35f) && Mathf.Approximately(mhK, 1.35f) && Mathf.Approximately(mhR, 1.15f) && Mathf.Approximately(ytO, 1f);
            OCheck("ocean_bites", bitesOk && checkedN > 0,
                string.Format(CIb, "structure mult: 방어 in reef.cover x{0:0.00}, 만새기 in kelp.cover x{1:0.00}, 만새기 in reef.cover x{2:0.00}, open water x{3:0.00}; BiteMult / open for the {4} fish about ({5} cover-species pairs at x1.35): {6}",
                    ytR, mhK, mhR, ytO, mults.Count, matched, string.Join(", ", lines)));
            yield return ToReady(ctl);

            // ---- 4. cover runs: the nearest cover of each species, and a 방어 sent to the reef rubs the line there
            float Yaw(Vector2 hold) => Mathf.Atan2(hold.x - ctl.Angler.X, hold.y);
            var nY = obs.NearestCover(new Vector3(reef.x + 2f, -4f, reef.z - 3f), yt, yt.coverReach, h => Mathf.Abs(Yaw(h)) <= 0.8f);
            var nM = obs.NearestCover(new Vector3(kelp.x - 2f, -3f, kelp.z - 2f), mh, mh.coverReach, h => Mathf.Abs(Yaw(h)) <= 0.8f);
            EquipTest("bait_jig", ctl);
            yield return null;
            // (hooked just beyond the reef's hold point: the run takes it there at once; from farther off the fight's line
            // length keeps a 방어 on -fkgear's drag short of it)
            var hookAt = new Vector3(reefC.hx - 0.3f, -5f, reefC.hz + 0.8f);
            Obstacles.SnagMult = 0f;
            ctl.DebugPlaceRig(new Vector3(hookAt.x, 0f, hookAt.z));
            yield return new WaitForSeconds(0.3f);
            Obstacles.SnagMult = snagWas;
            int runs0 = ctl.CoverRuns, holds0 = ctl.CoverHolds;
            FishingController.DebugCover = "reef.cover";
            bool hooked = ctl.DebugHook(yt, 70f, 7171, hookAt);
            bool rubbed = false;
            string rubOn = "-";
            var rubIds = new List<string>();
            float t = 0f;
            if (hooked)
            {
                // (no winding: the run takes the fish to the reef and it digs in there; with -fkgear's reel a wound 방어
                // comes to the boat before it gets there)
                var fm = ctl.Fight;
                PointerInput.SimDown = false;
                for (; t < 30f && ctl.State == FishingController.S.Fighting && ctl.Fight == fm; t += Time.deltaTime)
                {
                    if (!(fm.CoverRun || fm.CoverHold) && FishingController.DebugCover == null) FishingController.DebugCover = "reef.cover";
                    // (which structure the line rubs on: the same query as the fight's; a slack line entering the water
                    // under the bow also touches the hull's skirt)
                    if (ctl.Rubbing && ctl.Hooked != null)
                    {
                        var e = ctl.Angler.LineUnderwater ? ctl.Angler.WaterEntry : ctl.Angler.RodTip;
                        if (obs.Rub(new Vector2(e.x, e.z), ctl.Hooked.Pos, out var ro, out _))
                        {
                            if (!rubIds.Contains(ro.id)) rubIds.Add(ro.id);
                            if (ro == reef)
                            {
                                rubbed = true;
                                rubOn = ro.id + " (" + ro.Name + ")";
                                yield return new WaitForSeconds(0.4f);
                                yield return NamedShot("obst_ocean_rub");
                                break;
                            }
                        }
                    }
                    yield return null;
                }
            }
            FishingController.DebugCover = null;
            int holds = ctl.CoverHolds - holds0;
            string lastCover = ctl.LastCover != null ? ctl.LastCover.id : "-";
            OCheck("ocean_cover", nY == reefC && nM == kelpC && hooked && ctl.CoverRuns > runs0 && lastCover == "reef.cover" && rubbed,
                string.Format(CIb, "nearest cover: 방어 by the reef {0}, 만새기 by the mat {1}; hooked 방어 70 cm: cover runs {2} to {3} (holds {9}), rubbing on {4} {5} after {6:0.0} s (abrasion {7:0.00}; rubbed on: {8})",
                    nY != null ? nY.id : "-", nM != null ? nM.id : "-", ctl.CoverRuns - runs0, lastCover, rubOn, rubbed, t,
                    ctl.Fight != null ? ctl.Fight.Abrasion : ctl.LastAbrasion, string.Join(" ", rubIds), holds));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(2f);
            // (a fight that ended in a catch: sell it)
            while (ctl.State == FishingController.S.Landing) yield return null;
            if (ctl.State == FishingController.S.Result)
            {
                for (float w = 0f; w < 4f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(0.2f);
                Click("판매");
                yield return new WaitForSeconds(0.6f);
            }
            yield return ToReady(ctl);

            // ---- 4b. a line entering the water over the hull's skirt by the bow, the fish out ahead: no rub. The query
            // (the fish 5.8 m past the bow, 8 m down, against just past the skirt / under the hull), the taut gate, then a
            // fight with the fish held out ahead and no winding (slack and taut phases both)
            yield return BowSlack(ctl, obs, yt);

            // ---- 5. the ocean's drift still carries a float in open water; the legends' lurk band is clear of the structure
            EquipTest("bait_worm", ctl);
            yield return null;
            ctl.DebugPlaceRig(new Vector3(-1f, 0f, 22f));
            yield return new WaitForSeconds(0.8f);
            var p0 = ctl.Tackle.Surface;
            yield return new WaitForSeconds(3f);
            var p1 = ctl.Tackle.Surface;
            float moved = new Vector2(p1.x - p0.x, p1.z - p0.z).magnitude;
            var flow = ctl.Stage.Current != null ? ctl.Stage.Current.At(new Vector3(p0.x, -0.5f, p0.z)) : Vector2.zero;
            OCheck("ocean_drift", moved >= 0.2f && ctl.State == FishingController.S.Waiting,
                string.Format(CIb, "float at ({0:0.00}, {1:0.00}) drifted {2:0.00} m in 3 s (current ({3:0.00}, {4:0.00}) m/s), state {5}", p0.x, p0.z, moved, flow.x, flow.y, ctl.State));
            yield return ToReady(ctl);
            float zMaxStruct = Mathf.Max(ZMax(reef), ZMax(kelp));
            float lurkMin = 999f;
            var names = new List<string>();
            foreach (var f in GameDatabase.Fish)
                if (f.encounter != null && f.encounter.backdrop == "ocean")
                {
                    lurkMin = Mathf.Min(lurkMin, f.encounter.lurkZMin);
                    names.Add(f.id);
                }
            OCheck("ocean_lurk", names.Count > 0 && zMaxStruct < lurkMin,
                string.Format(CIb, "reef / kelp reach z {0:0.00}; the lurk band of {1} starts at z {2:0.0}", zMaxStruct, string.Join(", ", names), lurkMin));

            // ---- 6. -fkobstacles show on the ocean
            Obstacles.Show = true;
            yield return new WaitForSeconds(1f);
            yield return NamedShot("obst_ocean_show");
            Obstacles.Show = false;
            FishingController.NoBites = false;
        }

        /// <summary>
        /// ocean_bow_slack: a line entering the water over the hull's skirt by the bow with the fish out ahead raises no rub
        /// (the query: no hull rub with the fish 5.8 m past the bow, a hull rub with it 0.7 m past the skirt or under the
        /// hull; the taut gate: a slack line is no rub; a 6 s fight with the fish held out ahead, no winding: no rub frame).
        /// </summary>
        IEnumerator BowSlack(FishingController ctl, Obstacles obs, FishSpecies sp)
        {
            var skirt = obs.Get("hull.skirt");
            if (skirt == null)
            {
                OCheck("ocean_bow_slack", false, "hull.skirt missing");
                yield break;
            }
            float bowZ = ZMax(skirt);
            var entry = new Vector2(0f, bowZ - 0.4f);
            bool Hull(Vector3 fish) => obs.Rub(entry, fish, out var o, out _) && o.Mat.id == "hull";
            bool qAhead = Hull(new Vector3(0.5f, -8f, bowZ + 5.8f));
            bool qBy = Hull(new Vector3(0.3f, -6f, bowZ + 0.7f));
            bool qUnder = Hull(new Vector3(0.3f, -1f, 0f));
            // (a 5 kgf fish on PE 3호 (limit ~22): slack at 1 kgf, taut at 2.5; a 30 kgf fish on 6 kgf line: taut at 2)
            bool gate = !FishingController.RubTautNow(1f, 22f, 5f) && FishingController.RubTautNow(2.5f, 22f, 5f)
                        && FishingController.RubTautNow(2f, 6f, 30f);

            EquipTest("bait_jig", ctl);
            yield return null;
            // (2 m past the skirt, 10 m down: the line drops steeply and enters the water by the bow)
            var hold = new Vector3(ctl.Angler.X + 0.5f, -10f, bowZ + 2f);
            float snagWas = Obstacles.SnagMult;
            Obstacles.SnagMult = 0f;
            ctl.DebugPlaceRig(new Vector3(hold.x, 0f, hold.z));
            yield return new WaitForSeconds(0.3f);
            Obstacles.SnagMult = snagWas;
            int frames = 0, overSkirt = 0, rubF = 0, slackF = 0;
            float ezMin = float.MaxValue, ezMax = float.MinValue;
            float tMin = float.MaxValue, tMax = 0f;
            bool hooked = ctl.DebugHook(sp, 70f, 7272, hold);
            if (hooked)
            {
                var fm = ctl.Fight;
                PointerInput.SimDown = false;
                for (float t = 0f; t < 6f && ctl.State == FishingController.S.Fighting && ctl.Fight == fm; t += Time.deltaTime)
                {
                    ctl.DebugFishHold = hold;
                    var e = ctl.Angler.LineUnderwater ? ctl.Angler.WaterEntry : ctl.Angler.RodTip;
                    frames++;
                    if (obs.Inside(skirt, new Vector2(e.x, e.z))) overSkirt++;
                    ezMin = Mathf.Min(ezMin, e.z);
                    ezMax = Mathf.Max(ezMax, e.z);
                    if (ctl.Rubbing) rubF++;
                    if (!FishingController.RubTautNow(fm.Tension, fm.LineLimit, fm.Power)) slackF++;
                    tMin = Mathf.Min(tMin, fm.TensionRatio);
                    tMax = Mathf.Max(tMax, fm.TensionRatio);
                    yield return null;
                }
                ctl.DebugFishHold = null;
            }
            OCheck("ocean_bow_slack", !qAhead && qBy && qUnder && gate && hooked && rubF == 0,
                string.Format(CIb, "query: fish 5.8 m past the bow hull rub {0} (want False), 0.7 m past the skirt {1}, under the hull {2}; taut gate {3}; held-out fight (2 m past the skirt, 10 m down) {4} frames: line entry over the skirt {5} (z {10:0.00}..{11:0.00}, the skirt to {12:0.00}), slack {6}, tension ratio {7:0.00}..{8:0.00}, rub frames {9}",
                    qAhead, qBy, qUnder, gate, frames, overSkirt, slackF, tMin == float.MaxValue ? 0f : tMin, tMax, rubF, ezMin, ezMax, bowZ));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(2f);
            yield return ToReady(ctl);
        }

        static float ZMax(Obstacle o)
        {
            float z = float.MinValue;
            foreach (var p in o.Poly) z = Mathf.Max(z, p.y);
            return z;
        }
    }
}
