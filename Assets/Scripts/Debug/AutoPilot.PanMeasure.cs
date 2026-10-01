using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto panmeasure: how often and how far fought fish (and cast rigs) leave the 1x frame, per stage.
    /// <code>
    /// -fkfresh -fkrich -fkgear -fksave pan -fkscene Fishing -fkstage lake -fkauto panmeasure [-fkpanstages sea,ocean] [-fkpanfights 8] [-fkpanspeed 3]
    /// </code>
    /// On every stage (or those named): the cast fan's corners (the longest rod's reach at +-<see cref="FlickCast.YawMax"/>
    /// from both ends of the walk) against the 1x frame, then <c>-fkpanfights</c> fish of the stage's own species hooked
    /// where a cast lands (seeded: distance and aim spread over the fan) and fought like the fish scenario (winding while
    /// the tension is low, letting line go when it is high) at <c>-fkpanspeed</c> x speed. Every frame the fish (where it
    /// shows) is compared with the home 1x frame (the render target with the camera at the scene origin) and with the
    /// stage's art (widthPx x heightPx): [PANM] FIGHT and [PANM] STAGE lines with the frames outside and the farthest
    /// overshoot per side in game px.
    /// </summary>
    public partial class AutoPilot
    {
        static readonly CultureInfo CIp = CultureInfo.InvariantCulture;

        class PanTally
        {
            public int frames, outView, outArt, outSide, pastReach;
            public float left, right, top, bottom, artOver;
            public float maxLine;

            public void Add(Vector2 px, float W, float H, float artW, float artH)
            {
                frames++;
                float l = -px.x, r = px.x - W, b = -px.y, t = px.y - H;
                if (l > 0f || r > 0f || b > 0f || t > 0f) outView++;
                left = Mathf.Max(left, l);
                right = Mathf.Max(right, r);
                bottom = Mathf.Max(bottom, b);
                top = Mathf.Max(top, t);
                float ax = (artW - W) * 0.5f, ay = (artH - H) * 0.5f;
                float over = Mathf.Max(Mathf.Max(l - ax, r - ax), Mathf.Max(b - ay, t - ay));
                if (over > 0f) outArt++;
                artOver = Mathf.Max(artOver, over);
                // sideways only (the view pans sideways): past the home frame, and past what the art can show with the
                // fish its 20 px keep margin inside
                float side = Mathf.Max(l, r);
                if (side > 0f) outSide++;
                if (side > ax - 20f) pastReach++;
            }

            public void Merge(PanTally o)
            {
                frames += o.frames;
                outView += o.outView;
                outArt += o.outArt;
                outSide += o.outSide;
                pastReach += o.pastReach;
                left = Mathf.Max(left, o.left);
                right = Mathf.Max(right, o.right);
                top = Mathf.Max(top, o.top);
                bottom = Mathf.Max(bottom, o.bottom);
                artOver = Mathf.Max(artOver, o.artOver);
                maxLine = Mathf.Max(maxLine, o.maxLine);
            }

            public override string ToString() => string.Format(CIp,
                "{0} frames, outside the 1x frame {1} ({2:0.0}%; sideways {10}, of which past the art's reach with a 20 px margin {11}), beyond: left {3:0} right {4:0} top {5:0} bottom {6:0} px; beyond the art {7} frames (by {8:0} px); line up to {9:0.0} m",
                frames, outView, frames > 0 ? 100f * outView / frames : 0f, left, right, top, bottom, outArt, artOver, maxLine, outSide, pastReach);
        }

        /// <summary>A pixel-view world point -> px on the home 1x frame (the render target with the camera at the origin).</summary>
        static Vector2 HomePx(Vector2 world)
        {
            var rt = PixelView.Current.Target;
            return world * PixelView.PPU + new Vector2(rt.width * 0.5f, rt.height * 0.5f);
        }

        IEnumerator PanMeasure()
        {
            PointerInput.SimActive = true;   // (the real pointer is ignored from the start: other windows may share the desktop)
            yield return new WaitForSeconds(2f);
            PointerInput.SimDpi = 0f;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            var sd = Game.Data;
            sd.sweepHint = sd.tideHint = sd.driftHint = sd.mendHint = sd.sideHint = sd.timeHint = true;
            LegendWatch.DebugMode = null;
            var ids = (Arg("-fkpanstages") ?? "lake,stream,sea,swamp,ice,ocean,cave").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            int fights = int.TryParse(Arg("-fkpanfights"), out int nf) ? Mathf.Clamp(nf, 1, 60) : 8;
            float speed = Mathf.Clamp(ArgF("-fkpanspeed") ?? 3f, 1f, 6f);
            var all = new PanTally();
            foreach (var id in ids)
            {
                yield return GoStage(id, 3f);
                var ctl = FindAnyObjectByType<FishingController>();
                if (ctl == null || ctl.Stage.Def.id != id)
                {
                    Log($"[PANM] {id}: no scene");
                    continue;
                }
                ctl.Watch?.DebugLurk(null);
                FishingController.NoBites = true;
                var L = ctl.Stage.L;
                var P = ctl.Stage.P;
                var rt = PixelView.Current.Target;
                float W = rt.width, H = rt.height, artW = L.widthPx > 0 ? L.widthPx : 640, artH = L.heightPx > 0 ? L.heightPx : 400;
                bool ocean = id == "ocean", ice = L.IsIce;
                SteerGear(ocean ? "rod_biggame" : "rod_surf", ocean ? "reel_baitcast" : "reel_highgear", ocean ? "line_pe8" : "line_pe3");
                var (_, _, bait) = OccFish(id);
                yield return ToReady(ctl);
                EquipTest(bait, ctl);
                yield return null;
                float reach = Game.I.Rod.castDist;
                var range = ctl.Angler.Range;

                // the cast fan's corners from both ends of the walk
                var fan = new PanTally();
                if (!ice)
                    foreach (float ax in new[] { range.x, 0f, range.y })
                    foreach (float yaw in new[] { -FlickCast.YawMax, 0f, FlickCast.YawMax })
                    foreach (float d in new[] { L.zNear + 2.5f, reach })
                    {
                        float r = yaw * Mathf.Deg2Rad;
                        var p = new Vector3(Mathf.Clamp(ax + Mathf.Sin(r) * d, -L.xLim + 0.6f, L.xLim - 0.6f), 0f, Mathf.Clamp(Mathf.Cos(r) * d, L.zNear + 1.2f, L.zFar - 1f));
                        fan.Add(HomePx(P.To2D(p)), W, H, artW, artH);
                    }
                Log(string.Format(CIp, "[PANM] {0} target {1}x{2} art {3}x{4}, walk {5:0.0}..{6:0.0} m, reach {7:0} m: cast fan corners {8}",
                    id, W, H, artW, artH, range.x, range.y, reach, ice ? "- (the hole)" : fan.ToString()));

                var rnd = new System.Random(1616 + id.GetHashCode() % 1000);
                var spawns = ctl.Stage.Def.spawns.Where(kv => GameDatabase.GetFish(kv.Key)?.encounter == null).ToList();
                float wsum = spawns.Sum(kv => kv.Value);
                var stage = new PanTally();
                for (int k = 0; k < fights; k++)
                {
                    // the species by the stage's weights, a size in the upper part of its range, a spot where a cast lands
                    double pick = rnd.NextDouble() * wsum;
                    string fid = spawns[^1].Key;
                    foreach (var kv in spawns)
                        if ((pick -= kv.Value) <= 0) { fid = kv.Key; break; }
                    var sp = GameDatabase.GetFish(fid);
                    float cm = Mathf.Lerp(sp.minCm, sp.maxCm, 0.3f + 0.6f * (float)rnd.NextDouble());
                    float dist = Mathf.Lerp(L.zNear + 4f, reach, 0.35f + 0.65f * (float)rnd.NextDouble());
                    float yawR = ((float)rnd.NextDouble() * 2f - 1f) * FlickCast.YawMax * Mathf.Deg2Rad;
                    float ax = ctl.Angler.X;
                    var at = ice ? new Vector3(L.holeX, 0f, L.holeZ)
                        : new Vector3(Mathf.Clamp(ax + Mathf.Sin(yawR) * dist, -L.xLim + 0.6f, L.xLim - 0.6f), 0f, Mathf.Clamp(Mathf.Cos(yawR) * dist, L.zNear + 1.2f, L.zFar - 1f));
                    yield return ToReady(ctl);
                    if (!ctl.DebugPlaceRig(at))
                    {
                        Log($"[PANM] {id} #{k}: no rig ({ctl.State})");
                        continue;
                    }
                    for (float w = 0f; w < 3f && ctl.State != FishingController.S.Waiting; w += Time.deltaTime) yield return null;
                    yield return new WaitForSeconds(0.3f);
                    var land = ctl.Tackle.Surface;
                    if (!ctl.DebugHook(sp, cm, 2000 + k, new Vector3(land.x, -1.2f, land.z)))
                    {
                        Log($"[PANM] {id} #{k}: no hook ({ctl.State} {ctl.Tackle.State})");
                        continue;
                    }
                    var one = new PanTally();
                    yield return PanFight(ctl, one, speed, W, H, artW, artH);
                    Log(string.Format(CIp, "[PANM] FIGHT {0} #{1} {2} {3:0}cm at ({4:0.0}, {5:0.0}) -> {6}: {7}",
                        id, k, fid, cm, land.x, land.z, ctl.State, one));
                    stage.Merge(one);
                    if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
                    yield return ToReady(ctl);
                }
                Log($"[PANM] STAGE {id}: {stage}");
                all.Merge(stage);
            }
            Time.timeScale = 1f;
            FishingController.NoBites = false;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Log($"[PANM] ALL: {all}");
            Log("[PANM] done");
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        /// <summary>A fight like the fish scenario's (wind while the tension is low, give line when it is high), at <paramref name="speed"/> x, up to 70 s of game time.</summary>
        IEnumerator PanFight(FishingController ctl, PanTally tally, float speed, float W, float H, float artW, float artH)
        {
            var c = Scr(0.72f, 0.4f);
            float r = Screen.height * 0.13f, ang = 0f, t = 0f, ws = CircleGesture.Reversed ? -1f : 1f;
            bool winding = true;
            Time.timeScale = speed;
            PointerInput.SimActive = true;
            while (ctl.State == FishingController.S.Fighting && ctl.Hooked != null && t < 70f)
            {
                float dt = Time.deltaTime;
                t += dt;
                var f = ctl.Fight;
                if (winding && (f.TensionRatio > 0.8f || f.Jumping)) winding = false;
                else if (!winding && f.TensionRatio < 0.55f && !f.Jumping) winding = true;
                if (winding) ang -= ws * dt * 2.3f * Mathf.PI * 2f;
                else if (f.TensionRatio > 0.95f) ang += ws * dt * 1.2f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                yield return null;
                if (ctl.State != FishingController.S.Fighting || ctl.Hooked == null) break;
                tally.Add(HomePx(ctl.Fish2D(ctl.Hooked)), W, H, artW, artH);
                tally.maxLine = Mathf.Max(tally.maxLine, f.Line);
            }
            PointerInput.SimDown = false;
            Time.timeScale = 1f;
        }
        // (SimActive stays on between the fights: the real pointer never reaches the game in a run)
    }
}
