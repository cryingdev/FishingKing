using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto steer: the rod sweep and side pressure test (the simulated pointer moves like a mouse; no fish engage the
    /// rig meanwhile, <see cref="FishingController.NoBites"/>):
    /// <list type="number">
    /// <item>lure path: casts a lure, winds straight, slides LEFT and winds, slides RIGHT and winds, logging the lure's
    /// path ([STEER] path) and its sideways drift per metre wound; shots with the path traced (steer_left / steer_right);</item>
    /// <item>strokes: slides (fast, slow, sloped, a thumb's arc) must sweep the rod and must not wind the reel, 톡 or tap; a
    /// short slide, a 45 degree stroke, circles (from the top moving sideways: the worst case), a 톡 and a tap must not be
    /// slides; slide - hold - circle with one finger keeps the sweep; A / D hold it (and he does not walk);</item>
    /// <item>float: a float rig dragged right then left (at most ~1.5 m per sweep, its distance from him unchanged), and
    /// the 회수 retrieve bending with the rod swept (shot float_drag);</item>
    /// <item>clearance: the rod's clearance from the hat (<see cref="ActorStrip.Clearance"/>) at the walk range's ends and
    /// centre, the rig far left / ahead / right, the rod swept left / centred / right, idle and reeling;</item>
    /// <item>fights: the same fish (species, size, seed, spot) fought three times for 14 s with the same reeling: side
    /// pressure none / against every run / with every run (the A / D keys): stamina drain per second of run, run lengths
    /// and turn times, tension; shots fight_side_good / fight_side_bad.</item>
    /// </list>
    /// Then a grid of fights hooked far left / ahead / far right (13 m) and close in (6 m) at both walk ends, the rod leant
    /// left / centred / right: leaning must never bring the rod onto the hat (Angler keeps it off, KeepOffHat).
    /// On the ice: the sweep must stay off (slide, keys, a fight). -fksteer clear: the clearance part only (e.g. with
    /// -fkactors2d for the pose sprites); -fksteer fights: the fights only (then the arrow); -fksteer arrow: only the fight's
    /// side-pressure arrow with a float rig and a lure (<see cref="SteerArrow"/>); -fksweepraise &lt;deg&gt;: try the rod coming up
    /// this much at a full sweep to the right (Angler.SweepRaiseRight, default 0). Ends with [AUTO] CHECK lines and
    /// "steer test done: N failed".
    /// </summary>
    public partial class AutoPilot
    {
        int steerFails;
        readonly List<SpriteRenderer> trail = new List<SpriteRenderer>();
        Text caption;
        Image captionPlate;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        void SCheck(string what, bool ok)
        {
            if (!ok) steerFails++;
            Log($"CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        static string N(float v, string fmt = "0.00") => v.ToString(fmt, CI);

        IEnumerator SteerTest()
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
            string st = ctl.Stage.Def.id;
            bool clearOnly = Arg("-fksteer") == "clear", fightsOnly = Arg("-fksteer") == "fights", arrowOnly = Arg("-fksteer") == "arrow";
            var raise = ArgF("-fksweepraise");
            if (raise.HasValue) Angler.SweepRaiseRight = raise.Value;
            Log($"sweep raise right {N(Angler.SweepRaiseRight, "0.0")} deg");
            Log($"steer test {st}: screen {Screen.width}x{Screen.height} 3d {ctl.Angler.Uses3D} angler x {N(ctl.Angler.X)} range {N(ctl.Angler.Range.x)}..{N(ctl.Angler.Range.y)}");
            if (ctl.Stage.L.IsIce) yield return SteerIce(ctl);
            else
            {
                // a lure rod that casts far enough for three stretches of winding
                SteerGear("rod_carbon", "reel_highgear", null);
                if (!clearOnly && !fightsOnly && !arrowOnly)
                {
                    yield return SteerLure(ctl);
                    yield return SteerStrokes(ctl);
                    yield return SteerFloat(ctl);
                }
                if (!fightsOnly && !arrowOnly) yield return SteerClearance(ctl);
                if (!clearOnly && !arrowOnly) yield return SteerFights(ctl);
                // the side-pressure arrow over the line's entry, with a float rig and a lure (AutoPilot.SideArrow.cs)
                if (!clearOnly) yield return SteerArrow(ctl);
            }
            FishingController.NoBites = false;
            PointerInput.SimLeft = PointerInput.SimRight = false;
            Caption(null);
            Log($"steer test done: {steerFails} failed");
            PointerInput.SimActive = false;
            Application.Quit();
        }

        // ------------------------------------------------------------------ helpers
        static void SteerGear(string rod, string reel, string line)
        {
            foreach (var id in new[] { rod, reel, line })
            {
                var it = GameDatabase.GetItem(id);
                if (it == null) continue;
                if (!Game.I.Owns(it.id)) Game.Data.ownedItems.Add(it.id);
                Game.I.Equip(it);
            }
        }

        /// <summary>Back to the ready, this bait / lure on, cast straight out (the flick scenarios' default) until it is in the water.</summary>
        IEnumerator SteerCast(FishingController ctl, string baitId, float x = float.NaN)
        {
            PointerInput.SimLeft = PointerInput.SimRight = false;
            PointerInput.SimDown = false;
            // a fight that ended in a catch (the fish tired out and came in): sell it
            while (ctl.State == FishingController.S.Landing) yield return null;
            if (ctl.State == FishingController.S.Result)
            {
                for (float w = 0f; w < 4f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(0.2f);
                Click("판매");
                yield return new WaitForSeconds(0.6f);
            }
            yield return BackToReady(ctl);
            yield return new WaitForSeconds(0.3f);
            if (!float.IsNaN(x)) ctl.Angler.DebugPlace(x);
            var b = GameDatabase.GetItem<BaitDef>(baitId);
            if (b != null && Game.I.Bait.id != b.id)
            {
                if (b.isLure) { if (!Game.I.Owns(b.id)) Game.Data.ownedItems.Add(b.id); }
                else Game.I.AddBait(b.id, 99);
                ctl.EquipBait(b);
                yield return null;
            }
            for (int tries = 0; tries < 3 && ctl.State != FishingController.S.Waiting; tries++)
            {
                yield return CastLure(ctl);
                for (float w = 0f; w < 6f && ctl.State != FishingController.S.Waiting && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
                    yield return null;
            }
        }

        /// <summary>
        /// One stroke of the simulated pointer along <paramref name="path"/> (0..1) over <paramref name="time"/> s from a fresh
        /// press; <paramref name="hold"/>: then stays still this long; <paramref name="lift"/>: then lets go.
        /// </summary>
        IEnumerator SimStroke(Func<float, Vector2> path, float time, float hold = 0f, bool lift = true)
        {
            if (PointerInput.SimDown)
            {
                PointerInput.SimDown = false;
                yield return null;
            }
            PointerInput.SimActive = true;
            PointerInput.SimPos = path(0f);
            PointerInput.SimDown = true;
            yield return null;
            float t0 = Time.unscaledTime;
            while (true)
            {
                float u = Mathf.Clamp01((Time.unscaledTime - t0) / Mathf.Max(0.001f, time));
                PointerInput.SimPos = path(u);
                yield return null;
                if (u >= 1f) break;
            }
            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
            if (lift)
            {
                PointerInput.SimDown = false;
                yield return null;
            }
        }

        /// <summary>A straight slide from (x0, y) to (x1, y) (screen fractions), <paramref name="slopeDeg"/> down to the right.</summary>
        IEnumerator SimSlide(float x0, float x1, float y, float time, float slopeDeg = 0f, float hold = 0f, bool lift = true)
        {
            var a = Scr(x0, y);
            var b = Scr(x1, y) - new Vector2(0f, Mathf.Tan(slopeDeg * Mathf.Deg2Rad) * Mathf.Abs(x1 - x0) * Screen.width);
            yield return SimStroke(u => Vector2.Lerp(a, b, u), time, hold, lift);
        }

        /// <summary>
        /// Reel circles (the finger down) for <paramref name="time"/> s at <paramref name="rps"/> from
        /// <paramref name="startDeg"/> (0 = the right-most point, 90 = the top), dropping trail dots of the rig's path in
        /// <paramref name="c"/> (clear = none) and a shot (<paramref name="shot"/>) at 80 % of the way; lets go at the end.
        /// </summary>
        IEnumerator SteerWind(FishingController ctl, float rps, float time, Color c, string shot = null, float startDeg = 0f,
            float radius = 0.12f, bool lift = true)
        {
            var centre = Scr(0.72f, 0.42f);
            float r = Screen.height * radius, ws = CircleGesture.Reversed ? -1f : 1f, dotT = 0f;
            circleAng = startDeg * Mathf.Deg2Rad;
            bool shotDone = false;
            PointerInput.SimActive = true;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                circleAng -= ws * Time.deltaTime * rps * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(circleAng), Mathf.Sin(circleAng)) * r;
                dotT -= Time.deltaTime;
                if (c.a > 0f && dotT <= 0f && ctl.Tackle.State == Tackle.Mode.Water)
                {
                    dotT = 0.1f;
                    TrailDot(ctl, ctl.Tackle.Surface, c);
                }
                if (shot != null && !shotDone && t >= time * 0.8f)
                {
                    shotDone = true;
                    yield return Shot(shot);
                    continue;
                }
                yield return null;
            }
            if (lift)
            {
                PointerInput.SimDown = false;
                yield return null;
            }
        }

        void TrailDot(FishingController ctl, Vector3 surf, Color c, int size = 2)
        {
            var d = new GameObject("SteerTrail").AddComponent<SpriteRenderer>();
            d.sprite = Art.Pixel;
            d.sortingOrder = Fx.OrderRipple + 3;
            var p = ctl.Stage.P.To2D(new Vector3(surf.x, 0f, surf.z)) + ctl.Stage.DeckBob;
            d.transform.position = new Vector3(Mathf.Round(p.x * PixelView.PPU) / PixelView.PPU, Mathf.Round(p.y * PixelView.PPU) / PixelView.PPU, 0f);
            d.transform.localScale = new Vector3(size, size, 1f);
            d.color = c;
            trail.Add(d);
        }

        void ClearTrail()
        {
            foreach (var d in trail) if (d != null) Destroy(d.gameObject);
            trail.Clear();
        }

        /// <summary>A caption for the shots, bottom left (over the sky-free corner the tackle panel leaves); null hides it.</summary>
        void Caption(string text)
        {
            if (caption == null)
            {
                if (text == null) return;
                var cv = UIKit.CreateCanvas("[SteerCaption]", 15000);
                caption = UIKit.PlateLabel(cv.transform, "", 20, UIKit.Gold, out captionPlate);
                captionPlate.rectTransform.anchorMin = captionPlate.rectTransform.anchorMax = new Vector2(0f, 1f);
                captionPlate.rectTransform.pivot = new Vector2(0f, 1f);
                captionPlate.rectTransform.anchoredPosition = new Vector2(12f, -150f);
            }
            caption.text = text ?? "";
            UIKit.FitPlate(caption, captionPlate);
        }

        string SweepState(FishingController ctl) => string.Format(CI, "slide {0:+0.00;-0.00;0.00} shown {1:+0.00;-0.00;0.00} sweep {2:+0.0;-0.0;0.0} eff {3:+0.0;-0.0;0.0} deg",
            ctl.Slide.Value, ctl.Slide.Shown, ctl.Angler.Sweep, ctl.Angler.SweepEff);

        // ------------------------------------------------------------------ 1. the lure's path
        IEnumerator SteerLure(FishingController ctl)
        {
            yield return SteerCast(ctl, "bait_minnow");
            if (ctl.State != FishingController.S.Waiting)
            {
                SCheck($"lure cast for the sweep test ({ctl.State})", false);
                yield break;
            }
            yield return new WaitForSeconds(1.0f);
            var tk = ctl.Tackle;
            var g = ctl.Gesture;
            float retrieve = Game.I.Reel.retrieve;
            ClearTrail();
            Log(string.Format(CI, "[STEER] lure {0} at ({1:0.00}, {2:0.00}) angler x {3:0.00}, reel {4} {5:0.00} m/rev", tk.Bait.id, tk.Surface.x, tk.Surface.z, ctl.Angler.X, Game.I.Reel.id, retrieve));
            var pathLog = StartCoroutine(PathLog(ctl, "lure"));

            // straight
            float d0 = tk.SweepDrift, r0 = g.TotalRevs;
            var s0 = tk.Surface;
            yield return SteerWind(ctl, 1.3f, 2f, new Color(1f, 1f, 1f, 0.9f));
            float mA = (g.TotalRevs - r0) * retrieve, dA = tk.SweepDrift - d0;
            Log(string.Format(CI, "[STEER] straight: wound {0:0.00} m, drift {1:+0.00;-0.00;0.00} m, x {2:0.00} -> {3:0.00} ({4})", mA, dA, s0.x, tk.Surface.x, SweepState(ctl)));

            // slide LEFT, then wind with one finger (the sweep stays)
            yield return SimSlide(0.64f, 0.40f, 0.55f, 0.25f);
            yield return new WaitForSeconds(0.35f);
            Log($"[STEER] after the left slide: {SweepState(ctl)} slides {ctl.Slide.Slides}");
            SCheck($"slide LEFT sweeps the rod left and it stays after the finger lifts ({SweepState(ctl)})",
                ctl.Slide.Value <= -0.99f && ctl.Angler.SweepEff < -25f);
            Caption("대기 · 왼쪽으로 밀고 감기 (루어 궤적)");
            d0 = tk.SweepDrift;
            r0 = g.TotalRevs;
            s0 = tk.Surface;
            yield return SteerWind(ctl, 1.3f, 3.5f, new Color(1f, 0.82f, 0.2f, 1f), $"steer_left_{ctl.Stage.Def.id}");
            float mB = (g.TotalRevs - r0) * retrieve, dB = tk.SweepDrift - d0;
            Log(string.Format(CI, "[STEER] left: wound {0:0.00} m, drift {1:+0.00;-0.00;0.00} m ({2:+0.000;-0.000} per m; expected {3:+0.000;-0.000}), x {4:0.00} -> {5:0.00}, sweep still {6:+0.00}",
                mB, dB, mB > 0f ? dB / mB : 0f, -Tackle.SweepLateral * Mathf.Sin(Angler.SweepMax * Mathf.Deg2Rad), s0.x, tk.Surface.x, ctl.Slide.Value));

            // slide RIGHT (all the way over), wind
            yield return SimSlide(0.30f, 0.72f, 0.55f, 0.35f);
            yield return new WaitForSeconds(0.35f);
            Log($"[STEER] after the right slide: {SweepState(ctl)} slides {ctl.Slide.Slides}");
            SCheck($"slide RIGHT (back past the centre) sweeps the rod right ({SweepState(ctl)})", ctl.Slide.Value >= 0.99f && ctl.Angler.SweepEff > 25f);
            Caption("대기 · 오른쪽으로 밀고 감기 (루어 궤적)");
            d0 = tk.SweepDrift;
            r0 = g.TotalRevs;
            s0 = tk.Surface;
            yield return SteerWind(ctl, 1.3f, 3.5f, new Color(0.35f, 0.95f, 1f, 1f), $"steer_right_{ctl.Stage.Def.id}");
            float mC = (g.TotalRevs - r0) * retrieve, dC = tk.SweepDrift - d0;
            Log(string.Format(CI, "[STEER] right: wound {0:0.00} m, drift {1:+0.00;-0.00;0.00} m ({2:+0.000;-0.000} per m), x {3:0.00} -> {4:0.00}", mC, dC, mC > 0f ? dC / mC : 0f, s0.x, tk.Surface.x));
            StopCoroutine(pathLog);
            Caption(null);
            float want = Tackle.SweepLateral * Mathf.Sin(Angler.SweepMax * Mathf.Deg2Rad);
            SCheck($"straight retrieve does not drift ({N(dA, "+0.00;-0.00;0.00")} m over {N(mA)} m)", Mathf.Abs(dA) < 0.02f);
            SCheck($"swept left the lure's path bends left: {N(dB, "+0.00;-0.00")} m over {N(mB)} m wound ({N(mB > 0 ? dB / mB : 0, "+0.000;-0.000")}/m, want -{N(want, "0.000")})",
                mB > 1f && dB < 0f && Mathf.Abs(dB / mB + want) < 0.06f);
            SCheck($"swept right the lure's path bends right: {N(dC, "+0.00;-0.00")} m over {N(mC)} m wound ({N(mC > 0 ? dC / mC : 0, "+0.000;-0.000")}/m, want +{N(want, "0.000")})",
                mC > 1f && dC > 0f && Mathf.Abs(dC / mC - want) < 0.06f);
            yield return new WaitForSeconds(0.2f);
            ClearTrail();
            yield return BackToReady(ctl);
            yield return new WaitForSeconds(0.4f);
            SCheck($"the sweep is back to the centre once the rig is home ({SweepState(ctl)})",
                ctl.Slide.Value == 0f && ctl.Angler.Sweep == 0f && Mathf.Abs(ctl.Angler.SweepEff) < 0.5f);
        }

        /// <summary>Logs the rig's path ([STEER] path) every 0.25 s while it runs.</summary>
        IEnumerator PathLog(FishingController ctl, string tag)
        {
            float t = 0f;
            while (true)
            {
                var s = ctl.Tackle.Surface;
                Log(string.Format(CI, "[STEER] path {0} t={1:0.00} x={2:0.000} z={3:0.000} drift={4:+0.000;-0.000;0.000} sweep={5:+0.0;-0.0;0.0} state={6}",
                    tag, t, s.x, s.z, ctl.Tackle.SweepDrift, ctl.Angler.SweepEff, ctl.State));
                yield return new WaitForSeconds(0.25f);
                t += 0.25f;
            }
        }

        // ------------------------------------------------------------------ 2. strokes: slides vs circles / 톡 / taps
        class StrokeOut
        {
            public int slides, flicks, taps, cancels;
            public float revs, valueBefore, valueAfter, liveMax;
            public string reject, lureReject;
        }

        IEnumerator SteerStrokes(FishingController ctl)
        {
            yield return SteerCast(ctl, "bait_minnow");
            if (ctl.State != FishingController.S.Waiting)
            {
                SCheck($"lure cast for the stroke test ({ctl.State})", false);
                yield break;
            }
            yield return new WaitForSeconds(0.8f);
            var li = ctl.LureIn;
            var sl = ctl.Slide;
            var g = ctl.Gesture;
            int taps = 0;
            Action onTap = () => taps++;
            li.Tapped += onTap;
            StrokeOut o = null;
            // runs one stroke, measuring what it did (and the biggest live lean away from the sticky value meanwhile)
            IEnumerator Run(string name, IEnumerator stroke)
            {
                o = new StrokeOut { valueBefore = sl.Value };
                int s0 = sl.Slides, f0 = li.Flicks, t0 = taps, c0 = sl.Cancels;
                float r0 = g.TotalRevs;
                bool done = false;
                IEnumerator Watch()
                {
                    while (!done)
                    {
                        o.liveMax = Mathf.Max(o.liveMax, Mathf.Abs(sl.Shown - o.valueBefore));
                        yield return null;
                    }
                }
                var w = StartCoroutine(Watch());
                yield return stroke;
                yield return null;
                yield return null;
                done = true;
                StopCoroutine(w);
                o.slides = sl.Slides - s0;
                o.flicks = li.Flicks - f0;
                o.taps = taps - t0;
                o.cancels = sl.Cancels - c0;
                o.revs = g.TotalRevs - r0;
                o.valueAfter = sl.Value;
                o.reject = sl.LastReject;
                o.lureReject = li.LastReject;
                Log(string.Format(CI, "[STEER] stroke {0}: slides +{1} (reject '{2}') 톡 +{3} (lure reject '{4}') taps +{5} revs {6:+0.00;-0.00;0.00} sweep {7:+0.00;-0.00;0.00} -> {8:+0.00;-0.00;0.00} live lean max {9:0.00} cancels +{10}",
                    name, o.slides, o.reject, o.flicks, o.lureReject, o.taps, o.revs, o.valueBefore, o.valueAfter, o.liveMax, o.cancels));
                yield return new WaitForSeconds(0.35f);
            }
            bool IsSlide(StrokeOut s) => s.slides == 1 && s.flicks == 0 && s.taps == 0 && Mathf.Abs(s.revs) < 0.05f;
            bool NoSlide(StrokeOut s) => s.slides == 0 && Mathf.Abs(s.valueAfter - s.valueBefore) < 1e-4f;

            // slides that must count (and wind nothing, 톡 nothing, tap nothing)
            yield return Run("fastR", SimSlide(0.40f, 0.66f, 0.55f, 0.15f));
            SCheck($"fast slide right: a slide, no circle / 톡 / tap (sweep -> {N(o.valueAfter, "+0.00;-0.00;0.00")})", IsSlide(o) && o.valueAfter > 0.99f);
            yield return Run("slowL", SimSlide(0.66f, 0.22f, 0.50f, 0.8f));
            SCheck($"slow slide left: a slide, no circle / 톡 / tap (sweep -> {N(o.valueAfter, "+0.00;-0.00;0.00")})", IsSlide(o) && o.valueAfter < -0.99f);
            yield return Run("slopedR", SimSlide(0.40f, 0.62f, 0.60f, 0.3f, slopeDeg: 18f));
            SCheck($"slide right sloping 18 deg down: a slide, no 톡 (sweep -> {N(o.valueAfter, "+0.00;-0.00;0.00")}: back to the centre)", IsSlide(o) && Mathf.Abs(o.valueAfter) < 1e-4f);
            {
                // a thumb's arc: pivoting round a point well below the screen, 40 degrees to the left
                var pivot = Scr(0.62f, 0.55f) - new Vector2(0f, Screen.height * 0.5f);
                float R = Screen.height * 0.5f;
                yield return Run("arcL", SimStroke(u =>
                {
                    float a = (90f + 40f * u) * Mathf.Deg2Rad;
                    return pivot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
                }, 0.3f));
                SCheck($"a thumb's arc to the left (R 0.5 H, 40 deg): a slide (sweep -> {N(o.valueAfter, "+0.00;-0.00;0.00")})", IsSlide(o) && o.valueAfter < -0.5f);
            }
            {
                // a finger landing with a wobble: 2 px back to the left over the first frames, then a slide right
                var a0 = Scr(0.36f, 0.55f);
                var a1 = a0 - new Vector2(2f, 0f);
                var b = Scr(0.70f, 0.55f);
                yield return Run("wobbleR", SimStroke(u => u < 0.1f ? Vector2.Lerp(a0, a1, u / 0.1f) : Vector2.Lerp(a1, b, (u - 0.1f) / 0.9f), 0.3f));
                SCheck($"a slide right that starts with a 2 px wobble back: a slide, leaning live (live max {N(o.liveMax)}; sweep -> {N(o.valueAfter, "+0.00;-0.00;0.00")})",
                    IsSlide(o) && o.valueAfter > 0.5f && o.liveMax > 0.5f);
            }
            // strokes that must not be slides
            yield return Run("short", SimSlide(0.50f, 0.54f, 0.55f, 0.15f));
            SCheck($"a short sideways nudge (0.04 W) is no slide (reject '{o.reject}') and no tap / 톡", NoSlide(o) && o.flicks == 0 && o.taps == 0);
            yield return Run("diag45", SimSlide(0.42f, 0.56f, 0.62f, 0.6f, slopeDeg: 45f));
            SCheck($"a 45 degree stroke is no slide (reject '{o.reject}')", NoSlide(o));
            yield return Run("circleTop", SteerWind(ctl, 2f, 1.5f, Color.clear, startDeg: 90f, radius: 0.13f));
            SCheck($"circles from the top moving sideways (r 0.13 H): winds {N(o.revs)} rev, no slide (live lean max {N(o.liveMax)}, cancels {o.cancels})", NoSlide(o) && o.revs > 1.5f);
            yield return Run("circleBig", SteerWind(ctl, 1.5f, 1.5f, Color.clear, startDeg: 90f, radius: 0.25f));
            SCheck($"big circles from the top (r 0.25 H): winds {N(o.revs)} rev, no slide (live lean max {N(o.liveMax)}, cancels {o.cancels})", NoSlide(o) && o.revs > 1f);
            yield return Run("circleSide", SteerWind(ctl, 2f, 1.5f, Color.clear, startDeg: 0f, radius: 0.13f));
            SCheck($"circles from the side (the usual): winds {N(o.revs)} rev, no slide", NoSlide(o) && o.revs > 1.5f);
            yield return Run("tok", LureFlick(1.0f, 0.10f));
            SCheck($"a 톡 (short pull down) is a 톡 and no slide", NoSlide(o) && o.flicks == 1);
            yield return Run("tap", Tap(Scr(0.5f, 0.55f)));
            SCheck($"a tap is a tap and no slide", NoSlide(o) && o.taps == 1);
            // one finger: slide, stop, then circle without lifting: the sweep holds
            yield return Run("slideHoldCircle", SlideHoldCircle(ctl));
            SCheck($"slide right, hold, circle without lifting: the sweep is kept ({N(o.valueBefore, "+0.00;-0.00;0.00")} -> {N(o.valueAfter, "+0.00;-0.00;0.00")}) and the circles wind ({N(o.revs)} rev)",
                o.slides == 1 && o.valueAfter > 0.99f && o.revs > 1f);
            li.Tapped -= onTap;

            // the keys hold the sweep (and he does not walk)
            float x0 = ctl.Angler.X, v0 = sl.Value;
            PointerInput.SimLeft = true;
            yield return new WaitForSeconds(0.6f);
            float kSweep = ctl.Angler.Sweep, kEff = ctl.Angler.SweepEff, kx = ctl.Angler.X;
            PointerInput.SimLeft = false;
            yield return new WaitForSeconds(0.4f);
            Log(string.Format(CI, "[STEER] key A held: sweep {0:+0.0;-0.0} eff {1:+0.0;-0.0}, x {2:0.000} -> {3:0.000}; released: {4}", kSweep, kEff, x0, kx, SweepState(ctl)));
            SCheck($"A held in Waiting sweeps the rod full left ({N(kSweep, "+0.0;-0.0")} deg) and he does not walk (x {N(x0, "0.000")} -> {N(kx, "0.000")}); let go, back to the slide's {N(v0, "+0.00;-0.00")}",
                kSweep <= -29.9f && Mathf.Abs(kx - x0) < 1e-4f && Mathf.Abs(ctl.Slide.Shown - v0) < 1e-4f);
            // A still held as the rig comes home: no walking until it is let go and pressed again
            PointerInput.SimLeft = true;
            yield return BackToReady(ctl);
            float hx0 = ctl.Angler.X;
            yield return new WaitForSeconds(0.5f);
            float hx1 = ctl.Angler.X;
            PointerInput.SimLeft = false;
            yield return null;
            yield return null;
            PointerInput.SimLeft = true;
            yield return new WaitForSeconds(0.3f);
            float hx2 = ctl.Angler.X;
            PointerInput.SimLeft = false;
            Log(string.Format(CI, "[STEER] A held into the ready: x {0:0.000} -> {1:0.000} (held on), {2:0.000} (pressed again)", hx0, hx1, hx2));
            SCheck($"A held from the sweep into the ready walks nothing until pressed again (x {N(hx0, "0.000")} -> {N(hx1, "0.000")}, then {N(hx2, "0.000")})",
                Mathf.Abs(hx1 - hx0) < 1e-4f && (Mathf.Abs(hx2 - hx1) > 0.05f || hx1 <= ctl.Angler.Range.x + 0.01f));
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator SlideHoldCircle(FishingController ctl)
        {
            // a long slide right, held for 0.35 s with a resting finger's jitter (+-1.5 px: commits), then circles with the
            // finger still down
            yield return SimSlide(0.30f, 0.72f, 0.50f, 0.25f, lift: false);
            var rest = PointerInput.SimPos;
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                PointerInput.SimPos = rest + new Vector2(Mathf.Sin(t * 37f), Mathf.Cos(t * 23f)) * 1.5f;
                yield return null;
            }
            var from = PointerInput.SimPos;
            var centre = from - new Vector2(Screen.height * 0.12f, 0f);
            float r = Screen.height * 0.12f, ws = CircleGesture.Reversed ? -1f : 1f, ang = 0f;
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                ang -= ws * Time.deltaTime * 2f * Mathf.PI * 2f;
                PointerInput.SimPos = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                PointerInput.SimDown = true;
                yield return null;
            }
            PointerInput.SimDown = false;
            yield return null;
        }

        // ------------------------------------------------------------------ 3. the float dragged sideways
        IEnumerator SteerFloat(FishingController ctl)
        {
            yield return SteerCast(ctl, "bait_paste");
            if (ctl.State != FishingController.S.Waiting || !ctl.Tackle.UsesFloat)
            {
                SCheck($"float cast for the drag test ({ctl.State}, float {ctl.Tackle.UsesFloat})", false);
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            var tk = ctl.Tackle;
            var anchor = new Vector3(ctl.Angler.X, 0f, 0f);
            float Dist() => new Vector2(tk.Surface.x - anchor.x, tk.Surface.z - anchor.z).magnitude;
            ClearTrail();
            var s0 = tk.Surface;
            float r0 = Dist();
            TrailDot(ctl, s0, new Color(1f, 0.3f, 0.3f, 1f), 3);
            Log(string.Format(CI, "[STEER] float at ({0:0.00}, {1:0.00}), {2:0.00} m from him", s0.x, s0.z, r0));
            var pathLog = StartCoroutine(PathLog(ctl, "float"));
            IEnumerator Drift(float secs, Color c, string shot)
            {
                float dotT = 0f;
                bool shotDone = false;
                for (float t = 0f; t < secs && ctl.State == FishingController.S.Waiting; t += Time.deltaTime)
                {
                    dotT -= Time.deltaTime;
                    if (dotT <= 0f)
                    {
                        dotT = 0.15f;
                        TrailDot(ctl, tk.Surface, c);
                    }
                    if (shot != null && !shotDone && t > secs * 0.8f)
                    {
                        shotDone = true;
                        yield return Shot(shot);
                        continue;
                    }
                    yield return null;
                }
            }
            // slide right: the float comes over slowly, at most ~1.5 m
            yield return SimSlide(0.40f, 0.66f, 0.55f, 0.25f);
            float d0 = tk.SweepDrift, x0 = tk.Surface.x, tStart = Time.time;
            Caption("찌 · 오른쪽으로 밀기 (감지 않아도 천천히 끌려옴)");
            float half = -1f;
            IEnumerator Watch()
            {
                while (true)
                {
                    if (half < 0f && tk.SweepDrift - d0 >= Tackle.SweepDragMax * 0.5f) half = Time.time - tStart;
                    yield return null;
                }
            }
            var wt = StartCoroutine(Watch());
            yield return Drift(7f, new Color(1f, 0.82f, 0.2f, 1f), $"float_drag_{ctl.Stage.Def.id}");
            StopCoroutine(wt);
            float dR = tk.SweepDrift - d0, r1 = Dist();
            Log(string.Format(CI, "[STEER] float right: drift {0:+0.00;-0.00} m in 7 s (half-way after {1:0.00} s), x {2:0.00} -> {3:0.00}, distance {4:0.00} -> {5:0.00} m ({6})",
                dR, half, x0, tk.Surface.x, r0, r1, SweepState(ctl)));
            SCheck($"float swept right drags right {N(dR, "+0.00;-0.00")} m (~{N(Tackle.SweepDragMax, "0.0")} m max, slowly: half-way after {N(half)} s) and the line does not lengthen ({N(r0)} -> {N(r1)} m)",
                dR > 1.2f && dR <= Tackle.SweepDragMax + 0.05f && half > 1.5f && r1 <= r0 + 0.02f);
            // slide left all the way: a new sweep, the other way
            yield return SimSlide(0.72f, 0.30f, 0.55f, 0.35f);
            d0 = tk.SweepDrift;
            yield return Drift(7f, new Color(0.35f, 0.95f, 1f, 1f), null);
            float dL = tk.SweepDrift - d0;
            Log(string.Format(CI, "[STEER] float left: drift {0:+0.00;-0.00} m in 7 s, distance {1:0.00} m ({2})", dL, Dist(), SweepState(ctl)));
            SCheck($"float swept left drags left {N(dL, "+0.00;-0.00")} m (a new sweep: up to {N(Tackle.SweepDragMax, "0.0")} m again)", dL < -1.2f && dL >= -Tackle.SweepDragMax - 0.05f);
            // 회수 with the rod still swept left: the retrieve bends left too
            d0 = tk.SweepDrift;
            var sR = tk.Surface;
            ctl.Retrieve();
            for (float w = 0f; w < 1.5f && ctl.State == FishingController.S.Retrieving; w += Time.deltaTime) yield return null;
            float dRet = tk.SweepDrift - d0;
            float wound = new Vector2(sR.x - tk.Surface.x, sR.z - tk.Surface.z).magnitude;
            Log(string.Format(CI, "[STEER] 회수 swept left: drift {0:+0.00;-0.00} m over ~{1:0.00} m ({2})", dRet, wound, SweepState(ctl)));
            SCheck($"the 회수 retrieve bends towards the swept side too (drift {N(dRet, "+0.00;-0.00")} m)", dRet < -0.1f);
            StopCoroutine(pathLog);
            Caption(null);
            ClearTrail();
            yield return BackToReady(ctl);

            // near the bank: a float wound in close and swept round the circle towards the shore stops short of it
            yield return SteerCast(ctl, "bait_paste");
            if (ctl.State != FishingController.S.Waiting)
            {
                SCheck($"float cast for the near-bank drag ({ctl.State})", false);
                yield break;
            }
            yield return new WaitForSeconds(1f);
            var L = ctl.Stage.L;
            {
                var centre = Scr(0.72f, 0.42f);
                float cr = Screen.height * 0.12f, ws = CircleGesture.Reversed ? -1f : 1f, ang = 0f;
                PointerInput.SimActive = true;
                for (float w = 0f; w < 20f && ctl.State == FishingController.S.Waiting && tk.Surface.z > L.zNear + 0.7f; w += Time.deltaTime)
                {
                    ang -= ws * Time.deltaTime * 1.5f * Mathf.PI * 2f;
                    PointerInput.SimDown = true;
                    PointerInput.SimPos = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * cr;
                    yield return null;
                }
                PointerInput.SimDown = false;
                yield return new WaitForSeconds(0.3f);
            }
            var n0 = tk.Surface;
            float floorZ = L.zNear + Tackle.SweepNearZ, minZ = n0.z;
            yield return SimSlide(0.40f, 0.66f, 0.55f, 0.25f);
            for (float t = 0f; t < 7f && ctl.State == FishingController.S.Waiting; t += Time.deltaTime)
            {
                minZ = Mathf.Min(minZ, tk.Surface.z);
                yield return null;
            }
            Log(string.Format(CI, "[STEER] float near the bank: ({0:0.00}, {1:0.00}) -> ({2:0.00}, {3:0.00}), min z {4:0.00} (floor {5:0.00}, waterline {6:0.00}), {7}",
                n0.x, n0.z, tk.Surface.x, tk.Surface.z, minZ, floorZ, L.zNear, ctl.State));
            SCheck($"a float swept round towards the bank stays in the water (min z {N(minZ)} >= {N(floorZ)}; waterline {N(L.zNear)}) and is not taken home ({ctl.State})",
                n0.z <= L.zNear + 0.75f && minZ >= floorZ - 0.01f && ctl.State == FishingController.S.Waiting && tk.Surface.x - n0.x > 0.2f);
            yield return BackToReady(ctl);
        }

        // ------------------------------------------------------------------ 4. rod-to-hat clearance at the sweep extremes
        IEnumerator SteerClearance(FishingController ctl)
        {
            yield return SteerCast(ctl, "bait_minnow");
            if (ctl.State != FishingController.S.Waiting)
            {
                SCheck($"lure cast for the clearance test ({ctl.State})", false);
                yield break;
            }
            var a = ctl.Angler;
            var tk = ctl.Tackle;
            float x0 = a.X;
            var ends = new[] { ("L", a.Range.x), ("C", Mathf.Clamp(0f, a.Range.x, a.Range.y)), ("R", a.Range.y) };
            float baseMin = 999f, sweptMin = 999f;
            string baseAt = "", sweptAt = "";
            foreach (var (en, x) in ends)
            {
                a.DebugPlace(x);
                foreach (float yaw in new[] { -35f, 0f, 25f })
                foreach (int side in new[] { -1, 0, 1 })
                foreach (bool reel in new[] { false, true })
                {
                    float yr = yaw * Mathf.Deg2Rad;
                    tk.Surface = new Vector3(a.X + Mathf.Sin(yr) * 15f, 0f, Mathf.Cos(yr) * 15f);
                    PointerInput.SimLeft = side < 0;
                    PointerInput.SimRight = side > 0;
                    if (reel)
                    {
                        yield return SteerWind(ctl, 1.5f, 0.5f, Color.clear, lift: false);
                        tk.Surface = new Vector3(a.X + Mathf.Sin(yr) * 15f, 0f, Mathf.Cos(yr) * 15f);
                        yield return SteerWind(ctl, 1.5f, 0.1f, Color.clear, lift: false);
                    }
                    else yield return new WaitForSeconds(0.45f);
                    yield return new WaitForEndOfFrame();
                    float c = ActorStrip.Clearance(ctl);
                    var m = a.Model3D;
                    string at = string.Format(CI, "{0} x={1:0.00} rig {2:+0;-0;0} deg sweep {3:+0;-0;0} (eff {4:+0.0;-0.0;0.0}) {5} body {6:0.0} (guard: gap {7:0.0} tilt {8:+0.0;-0.0;0.0})",
                        en, a.X, yaw, side * Angler.SweepMax, a.SweepEff, a.Pose, m != null ? m.BodyYaw : 0f, a.HatGapNow, a.HatTilt);
                    Log(string.Format(CI, "[STEER] clear {0}: {1:0.0}px", at, c));
                    if (side == 0) { if (c < baseMin) { baseMin = c; baseAt = at; } }
                    else if (c < sweptMin) { sweptMin = c; sweptAt = at; }
                    if (reel)
                    {
                        PointerInput.SimDown = false;
                        yield return null;
                    }
                }
            }
            PointerInput.SimLeft = PointerInput.SimRight = false;
            a.DebugPlace(x0);
            Log(string.Format(CI, "[STEER] clearance SUMMARY {0}: unswept min {1:0.0}px at {2}; swept min {3:0.0}px at {4}", a.Uses3D ? "3d" : "2d", baseMin, baseAt, sweptMin, sweptAt));
            SCheck($"rod clear of the hat at the sweep extremes ({(a.Uses3D ? "3D" : "2D")}): swept min {N(sweptMin, "0.0")} px (unswept {N(baseMin, "0.0")} px)",
                sweptMin > 0f && sweptMin >= Mathf.Min(baseMin, 2f) - 0.5f);
            yield return BackToReady(ctl);
        }

        // ------------------------------------------------------------------ 5. side pressure: the same fish three times
        class FightStats
        {
            public string name;
            public float t, runTime, drain, tension, mult, tmult, stam0, stam1, clrMin = 999f, tensionAll, tiltT, tiltMax;
            public string clrAt = "";
            public int runs, turned;
            public readonly List<float> durs = new List<float>(), turns = new List<float>(), turnedDurs = new List<float>();
            public float MeanTurnedRun => turnedDurs.Count > 0 ? turnedDurs.Average() : -1f;
            public float DrainRate => runTime > 0f ? drain / runTime : 0f;
            public float TensionRun => runTime > 0f ? tension / runTime : 0f;
            public float MultRun => runTime > 0f ? mult / runTime : 0f;
            public float TMultRun => runTime > 0f ? tmult / runTime : 0f;
            public float MeanRun => durs.Count > 0 ? durs.Average() : 0f;
            public float MeanTurn => turns.Count > 0 ? turns.Average() : -1f;
        }

        IEnumerator SteerFights(FishingController ctl)
        {
            bool ocean = ctl.Stage.Def.id == "ocean";
            string fishId = ocean ? "yellowtail" : "carp";
            float cm = ocean ? 90f : 70f;
            if (ocean) SteerGear("rod_biggame", "reel_baitcast", "line_pe3");
            else SteerGear("rod_glass", "reel_light", "line_nylon4");
            var sp = GameDatabase.GetFish(fishId);
            float homeX = Mathf.Clamp(0f, ctl.Angler.Range.x, ctl.Angler.Range.y);
            Log($"[SIDE] fights: {fishId} {cm:0}cm, rod {Game.I.Rod.id} reel {Game.I.Reel.id} (drag {Game.I.Reel.dragMax}) line {Game.I.Line.id} ({Game.I.Line.strength} kg), angler x {N(homeX)}");
            var all = new List<FightStats>();
            bool shotGood = false, shotBad = false;
            foreach (string mode in new[] { "none", "opposite", "same" })
            {
                yield return SteerCast(ctl, "bait_minnow", homeX);
                if (ctl.State != FishingController.S.Waiting)
                {
                    SCheck($"cast for the {mode} fight ({ctl.State})", false);
                    continue;
                }
                var at = new Vector3(homeX + 1f, -1.2f, 16f);
                if (!ctl.DebugHook(sp, cm, 4242, at))
                {
                    SCheck($"hook for the {mode} fight", false);
                    continue;
                }
                var s = new FightStats { name = mode };
                all.Add(s);
                var f = ctl.Fight;
                s.stam0 = f.Stamina;
                Action<int, float, bool> onRun = (side, dur, turned) =>
                {
                    s.runs++;
                    s.durs.Add(dur);
                    // (a turned run runs on after its turn: the turn's time is LastTurnT, the run's whole length dur)
                    if (turned) { s.turned++; s.turns.Add(ctl.LastTurnT); s.turnedDurs.Add(dur); }
                    Log(string.Format(CI, "[SIDE] {0} run {1} ended after {2:0.00}s{3} (stamina {4:0.000})", mode, side > 0 ? "R" : "L", dur,
                        turned ? string.Format(CI, " TURNED at {0:0.00}s", ctl.LastTurnT) : "", ctl.Fight != null ? ctl.Fight.Stamina : -1f));
                };
                ctl.RunEnded += onRun;
                Caption(mode == "opposite" ? "파이트 · 달리는 반대쪽으로 기울임 (사이드 프레셔)" : mode == "same" ? "파이트 · 달리는 쪽으로 기울임 (잘못)" : "파이트 · 기울이지 않음");
                var c = Scr(0.72f, 0.42f);
                float r = Screen.height * 0.13f, ang = 0f, ws = CircleGesture.Reversed ? -1f : 1f, goodT = 0f, badT = 0f;
                bool prevRun = false;
                float prevStam = f.Stamina;
                PointerInput.SimActive = true;
                while (ctl.State == FishingController.S.Fighting && s.t < 14f && !f.Exhausted)
                {
                    float dt = Time.deltaTime;
                    s.t += dt;
                    int run = ctl.FishRun;
                    // the same reeling every time: wind while it rests (tension under 0.8), stop while it runs, give line near the break
                    bool running = run != 0 || f.State == FightModel.Phase.Burst;
                    if (f.TensionRatio > 0.95f) ang += ws * dt * 1.0f * Mathf.PI * 2f;
                    else if (!running && !f.Jumping && f.TensionRatio < 0.8f) ang -= ws * dt * 1.6f * Mathf.PI * 2f;
                    PointerInput.SimDown = true;
                    PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    // side pressure with the keys: against the run / with it / none
                    int lean = mode == "opposite" ? -run : mode == "same" ? run : 0;
                    PointerInput.SimLeft = lean < 0;
                    PointerInput.SimRight = lean > 0;
                    yield return null;
                    if (ctl.Fight == null) break;
                    // what the frame's step did (the fight updated after the keys were read)
                    if (prevRun)
                    {
                        s.runTime += dt;
                        s.drain += prevStam - f.Stamina;
                        s.tension += f.TensionRatio * dt;
                        s.mult += f.SideDrainMult * dt;
                        s.tmult += f.SideTensionMult * dt;
                    }
                    s.tensionAll += f.TensionRatio * dt;
                    prevRun = ctl.FishRun != 0;
                    prevStam = f.Stamina;
                    float clrNow = ActorStrip.Clearance(ctl);
                    if (Mathf.Abs(ctl.Angler.HatTilt) > 0.5f) s.tiltT += dt;
                    s.tiltMax = Mathf.Max(s.tiltMax, Mathf.Abs(ctl.Angler.HatTilt));
                    if (clrNow < s.clrMin)
                    {
                        s.clrMin = clrNow;
                        var fp = ctl.Hooked != null ? ctl.Hooked.Pos : Vector3.zero;
                        var a3 = ctl.Angler.Model3D;
                        s.clrAt = string.Format(CI, "fish ({0:0.0}, {1:0.0}) bearing {2:+0;-0;0} deg, sweep eff {3:+0.0;-0.0;0.0}, rod pitch {4:0.0} lean {5:+0.0;-0.0;0.0}, body {6:+0.0;-0.0;0.0}, tension {7:0.00}, pose {8}, hat tilt {9:+0.0;-0.0;0.0}",
                            fp.x, fp.z, Mathf.Atan2(fp.x - ctl.Angler.X, Mathf.Max(0.5f, fp.z)) * Mathf.Rad2Deg, ctl.Angler.SweepEff,
                            ctl.Angler.RodAngles.x, ctl.Angler.RodAngles.y, a3 != null ? a3.BodyYaw : 0f, f.TensionRatio, ctl.Angler.Pose, ctl.Angler.HatTilt);
                    }
                    // (for the shots: the fish out on the side it runs to, so its run, the rod and the line read at a glance)
                    var hk = ctl.Hooked;
                    bool outThere = hk != null && ctl.FishRun != 0 && (hk.Pos.x - ctl.Angler.X) * ctl.FishRun > 0.5f;
                    goodT = ctl.SideNow > 0.6f && outThere ? goodT + dt : 0f;
                    badT = ctl.SideNow < -0.6f && outThere ? badT + dt : 0f;
                    if (mode == "opposite" && !shotGood && goodT >= 0.2f)
                    {
                        shotGood = true;
                        LogFightShot(ctl, "fight_side_good");
                        yield return Shot($"fight_side_good_{ctl.Stage.Def.id}");
                    }
                    if (mode == "same" && !shotBad && badT >= 0.4f)
                    {
                        shotBad = true;
                        LogFightShot(ctl, "fight_side_bad");
                        yield return Shot($"fight_side_bad_{ctl.Stage.Def.id}");
                    }
                }
                PointerInput.SimLeft = PointerInput.SimRight = false;
                PointerInput.SimDown = false;
                if (ctl.Fight != null) s.stam1 = ctl.Fight.Stamina;
                ctl.RunEnded -= onRun;
                Log(string.Format(CI,
                    "[SIDE] {0}: {1:0.0}s fought, stamina {2:0.000} -> {3:0.000}; runs {4} (turned {5}), running {6:0.0}s: drain {7:0.0000}/s of run (model x{8:0.00}), tension {9:0.000} of the line during runs ({10:0.000} overall); mean run {11:0.00}s, mean turn {12:0.00}s; rod-hat clear min {13:0.0}px",
                    s.name, s.t, s.stam0, s.stam1, s.runs, s.turned, s.runTime, s.DrainRate, s.MultRun, s.TensionRun, s.t > 0 ? s.tensionAll / s.t : 0f, s.MeanRun, s.MeanTurn, s.clrMin)
                    + " at " + s.clrAt + string.Format(CI, "; kept off the hat {0:0.0}s (tilt max {1:0.0} deg)", s.tiltT, s.tiltMax));
                ctl.DebugRelease();
                for (float w = 0f; w < 3f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(0.6f);
            }
            Caption(null);
            var none = all.FirstOrDefault(x => x.name == "none");
            var opp = all.FirstOrDefault(x => x.name == "opposite");
            var same = all.FirstOrDefault(x => x.name == "same");
            if (none == null || opp == null || same == null)
            {
                SCheck("three side-pressure fights", false);
                yield break;
            }
            float ro = opp.DrainRate / Mathf.Max(1e-6f, none.DrainRate), rs = same.DrainRate / Mathf.Max(1e-6f, none.DrainRate);
            Log(string.Format(CI, "[SIDE] SUMMARY {0}: drain/s of run none {1:0.0000} opposite {2:0.0000} (x{3:0.00}) same {4:0.0000} (x{5:0.00}); tension in runs none {6:0.000} opposite {7:0.000} (x{8:0.00}) same {9:0.000} (x{10:0.00}); mean run none {11:0.00}s opposite {12:0.00}s same {13:0.00}s; turned opposite {14}/{15} (mean {16:0.00}s)",
                ctl.Stage.Def.id, none.DrainRate, opp.DrainRate, ro, same.DrainRate, rs, none.TensionRun, opp.TensionRun, opp.TensionRun / Mathf.Max(1e-6f, none.TensionRun),
                same.TensionRun, same.TensionRun / Mathf.Max(1e-6f, none.TensionRun), none.MeanRun, opp.MeanRun, same.MeanRun, opp.turned, opp.runs, opp.MeanTurn)
                + string.Format(CI, "; model drain x{0:0.00}/x{1:0.00}/x{2:0.00} load x{3:0.000}/x{4:0.000}/x{5:0.000} (none/opposite/same); turned runs last {6:0.00}s",
                    none.MultRun, opp.MultRun, same.MultRun, none.TMultRun, opp.TMultRun, same.TMultRun, opp.MeanTurnedRun));
            SCheck($"against the run tires it faster: drain x{N(ro)} of none's (model x{N(opp.MultRun)}; want ~x{N(FightModel.SideDrainGood)})", ro > 1.25f && ro < 1.6f);
            SCheck($"with the run tires it slower: drain x{N(rs)} of none's (model x{N(same.MultRun)}; want ~x{N(FightModel.SideDrainBad)})", rs > 0.65f && rs < 0.9f);
            SCheck($"against the run turns it: {opp.turned} of {opp.runs} runs turned after {N(opp.MeanTurn)} s (none's runs last {N(none.MeanRun)} s, none turned {none.turned})",
                opp.turned > 0 && opp.MeanTurn < none.MeanRun && none.turned == 0 && same.turned == 0);
            SCheck($"a turned run runs on (its head come round) and is cut short: turned runs last {N(opp.MeanTurnedRun)} s (turned at {N(opp.MeanTurn)} s; none's runs {N(none.MeanRun)} s)",
                opp.MeanTurnedRun > opp.MeanTurn + 0.15f && opp.MeanTurnedRun < none.MeanRun);
            SCheck($"with the run it runs longer: mean run {N(same.MeanRun)} s vs none's {N(none.MeanRun)} s", same.MeanRun > none.MeanRun);
            // (the turned runs are short, so their mean tension also depends on how far the pull had built up: the model's
            // load factor is the direct measure; the longer runs leant with it show it in the tension too)
            SCheck($"either lean loads the line ~10 % more: model load x{N(opp.TMultRun, "0.000")} (against) / x{N(same.TMultRun, "0.000")} (with) / x{N(none.TMultRun, "0.000")} (none); measured tension in runs x{N(same.TensionRun / Mathf.Max(1e-6f, none.TensionRun))} (with) / x{N(opp.TensionRun / Mathf.Max(1e-6f, none.TensionRun))} (against) of none's",
                opp.TMultRun > 1.05f && same.TMultRun > 1.05f && Mathf.Abs(none.TMultRun - 1f) < 1e-4f && same.TensionRun > none.TensionRun);
            float clr = Mathf.Min(none.clrMin, Mathf.Min(opp.clrMin, same.clrMin));
            SCheck($"the rod stays off the hat through the fights (min {N(clr, "0.0")} px: none {N(none.clrMin, "0.0")} / against {N(opp.clrMin, "0.0")} / with {N(same.clrMin, "0.0")}; kept off by tilting {N(none.tiltT + opp.tiltT + same.tiltT, "0.0")} s, max {N(Mathf.Max(none.tiltMax, Mathf.Max(opp.tiltMax, same.tiltMax)), "0.0")} deg)",
                clr > 0f);
            SCheck($"shots of right / wrong side pressure taken (good {shotGood}, bad {shotBad})", shotGood && shotBad);
            yield return SteerFightGrid(ctl, sp, cm);
        }

        /// <summary>
        /// The rod's clearance from the hat in a fight (the fight pose, the rod bent by the line) with the fish hooked far
        /// left / ahead / far right, at both ends of the walk range, the rod leant left / centred / right: the leant rod must
        /// be no closer to the hat than the centred one at the same spot (or clear by 1 px).
        /// </summary>
        IEnumerator SteerFightGrid(FishingController ctl, FishSpecies sp, float cm)
        {
            var a = ctl.Angler;
            float worstBase = 999f, worstLean = 999f, worstDiff = 999f, worstRead = 999f;
            string atBase = "", atLean = "", atDiff = "", atRead = "";
            foreach (float x in new[] { a.Range.x, a.Range.y })
            foreach (var (bearing, dist) in new[] { (-40f, 13f), (0f, 13f), (40f, 13f), (-25f, 6f), (25f, 6f) })
            {
                float baseClr = 999f;
                foreach (int lean in new[] { 0, -1, 1 })
                {
                    yield return SteerCast(ctl, "bait_minnow", x);
                    if (ctl.State != FishingController.S.Waiting) continue;
                    float br = bearing * Mathf.Deg2Rad;
                    if (!ctl.DebugHook(sp, cm, 777, new Vector3(a.X + Mathf.Sin(br) * dist, -1.2f, Mathf.Cos(br) * dist))) continue;
                    // (a frame with the keys up first: the rig landed this very frame, and keys already held as the sweep
                    // comes on sweep nothing until let go, SideSlide)
                    yield return null;
                    PointerInput.SimLeft = lean < 0;
                    PointerInput.SimRight = lean > 0;
                    float mn = 999f, leanRead = 0f;
                    string at = "";
                    for (float t = 0f; t < 1.3f && ctl.State == FishingController.S.Fighting; t += Time.deltaTime)
                    {
                        yield return new WaitForEndOfFrame();
                        float c = ActorStrip.Clearance(ctl);
                        if (t > 0.3f) leanRead = Mathf.Max(leanRead, lean * ctl.Lean);   // (the lean counts in full, the fish far out or not)
                        if (t > 0.3f && c < mn)
                        {
                            mn = c;
                            at = string.Format(CI, "x={0:0.00} fish bearing {1:+0;-0;0} at {6:0} m lean {2:+0;-0;0} (eff {3:+0.0;-0.0;0.0}) tension {4:0.00} pose {5}",
                                a.X, bearing, lean, a.SweepEff, ctl.Fight != null ? ctl.Fight.TensionRatio : 0f, a.Pose, dist);
                        }
                        yield return null;
                    }
                    PointerInput.SimLeft = PointerInput.SimRight = false;
                    Log(string.Format(CI, "[STEER] fight clear {0}: {1:0.0}px", at, mn));
                    if (lean == 0)
                    {
                        baseClr = mn;
                        if (mn < worstBase) { worstBase = mn; atBase = at; }
                    }
                    else
                    {
                        if (leanRead < worstRead) { worstRead = leanRead; atRead = string.Format(CI, "x={0:0.00} fish bearing {1:+0;-0;0} at {2:0} m lean {3:+0;-0;0}", a.X, bearing, dist, lean); }
                        if (mn < worstLean) { worstLean = mn; atLean = at; }
                        // (how much the lean takes from the clearance, counting only down to 1 px: a lean may come closer
                        // than the centred rod as long as it stays clear)
                        float d = mn - Mathf.Min(baseClr, 1f);
                        if (d < worstDiff) { worstDiff = d; atDiff = at; }
                    }
                    ctl.DebugRelease();
                    for (float w = 0f; w < 3f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
                }
            }
            Log(string.Format(CI, "[STEER] fight clearance SUMMARY: centred min {0:0.0}px at {1}; leant min {2:0.0}px at {3}; leant below min(centred, 1 px) by at worst {4:+0.0;-0.0;0.0}px at {5}",
                worstBase, atBase, worstLean, atLean, worstDiff, atDiff));
            SCheck($"fight: leaning never brings the rod onto the hat (leant min {N(worstLean, "0.0")} px, centred min {N(worstBase, "0.0")} px; leant vs min(centred, 1 px) worst {N(worstDiff, "+0.0;-0.0;0.0")} px)",
                worstDiff >= -0.5f);
            SCheck($"fight: a lean counts in full wherever the fish is, beyond the rod's drawn yaw limits too (weakest {N(worstRead)} at {atRead})",
                worstRead >= 0.9f);
        }

        void LogFightShot(FishingController ctl, string name)
        {
            var f = ctl.Fight;
            var fish = ctl.Hooked;
            var hud = FindAnyObjectByType<FishingHUD>();
            Log(string.Format(CI, "fightshot {0:00}_{1}: run {2:+0;-0;0} lean {3:+0.00;-0.00;0.00} side {4:+0.00;-0.00;0.00} tension {5:0.00} stamina {6:0.00} fish ({7:0.0}, {8:0.0}) canvas scale {9:0.00}",
                shotIndex, name, ctl.FishRun, ctl.Lean, ctl.SideNow, f != null ? f.TensionRatio : 0f, f != null ? f.Stamina : 0f,
                fish != null ? fish.Pos.x : 0f, fish != null ? fish.Pos.z : 0f, hud != null ? hud.Canvas.scaleFactor : 0f));
        }

        // ------------------------------------------------------------------ the ice: no sweep
        IEnumerator SteerIce(FishingController ctl)
        {
            yield return SteerCast(ctl, Game.I.Bait.id);
            if (ctl.State != FishingController.S.Waiting)
            {
                SCheck($"jig down the hole for the ice test ({ctl.State})", false);
                yield break;
            }
            yield return new WaitForSeconds(1f);
            var tk = ctl.Tackle;
            var s0 = tk.Surface;
            int slides0 = ctl.Slide.Slides;
            yield return SimSlide(0.40f, 0.66f, 0.55f, 0.25f);
            yield return new WaitForSeconds(0.3f);
            string afterSlide = SweepState(ctl);
            PointerInput.SimRight = true;
            float maxEff = 0f;
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                maxEff = Mathf.Max(maxEff, Mathf.Abs(ctl.Angler.SweepEff), Mathf.Abs(ctl.Angler.Sweep));
                yield return null;
            }
            Caption("얼음 구멍 · 밀기/D키에도 낚싯대 그대로");
            yield return Shot("ice_nosweep");
            yield return SteerWind(ctl, 1f, 0.6f, Color.clear);
            PointerInput.SimRight = false;
            Caption(null);
            var s1 = tk.Surface;
            Log($"[STEER] ice: after a slide {afterSlide}; D held: max |sweep| {N(maxEff, "0.0")} deg; rig x {N(s0.x, "0.000")} -> {N(s1.x, "0.000")}, slides {ctl.Slide.Slides - slides0}");
            SCheck($"ice: no sweep from a slide or the keys (max {N(maxEff, "0.0")} deg, slides {ctl.Slide.Slides - slides0}) and the rig stays in the hole (x {N(s0.x, "0.000")} -> {N(s1.x, "0.000")})",
                maxEff < 0.01f && ctl.Slide.Slides == slides0 && Mathf.Abs(s1.x - s0.x) < 1e-4f);
            // a fight through the hole: no side pressure either
            if (ctl.State == FishingController.S.Waiting)
            {
                var L = ctl.Stage.L;
                if (ctl.DebugHook(GameDatabase.GetFish("burbot"), 60f, 4242, new Vector3(L.holeX, -3f, L.holeZ)))
                {
                    int runs = 0;
                    float side = 0f, eff = 0f;
                    var hud = FindAnyObjectByType<FishingHUD>();
                    var strip = hud != null ? hud.Canvas.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(x => x.name == "Side") : null;
                    PointerInput.SimLeft = true;
                    for (float t = 0f; t < 4f && ctl.State == FishingController.S.Fighting; t += Time.deltaTime)
                    {
                        if (ctl.FishRun != 0) runs++;
                        side = Mathf.Max(side, Mathf.Abs(ctl.SideNow));
                        eff = Mathf.Max(eff, Mathf.Abs(ctl.Angler.SweepEff));
                        yield return null;
                    }
                    PointerInput.SimLeft = false;
                    bool stripOn = strip == null || strip.gameObject.activeInHierarchy;
                    Log($"[STEER] ice fight: run frames {runs}, max side {N(side)}, max sweep {N(eff, "0.0")} deg, side strip shown {stripOn}");
                    SCheck($"ice fight: no side pressure (run frames {runs}, side {N(side)}, sweep {N(eff, "0.0")}) and no side strip ({(strip == null ? "not found" : stripOn ? "shown" : "hidden")})",
                        runs == 0 && side == 0f && eff < 0.01f && strip != null && !stripOn);
                    ctl.DebugRelease();
                }
                else SCheck("ice fight hook", false);
            }
            yield return BackToReady(ctl);
            if (FloatShotsOn) yield return IceFloatFight(ctl);
        }

        /// <summary>
        /// -fkfloatshots on the ice: a float rig down the hole takes a burbot 3 m down and the fight is fought (reeling while
        /// the line is easy, easing off when it is tight, a rest every 5 s; up to 30 s) for the float's moments in the hole
        /// (riding, pulled under, popping up); landed: its landing and hang; still on after 30 s: let go (the float stays in
        /// the hole).
        /// </summary>
        IEnumerator IceFloatFight(FishingController ctl)
        {
            SteerGear("rod_glass", "reel_light", "line_nylon4");   // (the default 3 kg line snaps at once on a burbot)
            yield return SteerCast(ctl, "bait_paste");
            var L = ctl.Stage.L;
            if (ctl.State != FishingController.S.Waiting || !ctl.Tackle.UsesFloat
                || !ctl.DebugHook(GameDatabase.GetFish("burbot"), Mathf.Clamp(ArgF("-fkicecm") ?? 45f, 20f, 90f), 777, new Vector3(L.holeX, -Mathf.Clamp(ArgF("-fkicedepth") ?? 3f, 1f, 8f), L.holeZ)))
            {
                SCheck($"ice float fight: hooked ({ctl.State}, float {ctl.Tackle.UsesFloat})", false);
                yield break;
            }
            Log($"[STEER] ice float fight: float depth {N(ctl.Tackle.FloatDepth)} m");
            floatDone = 0;
            var c = Scr(0.72f, 0.42f);
            float r = Screen.height * 0.13f, ang = 0f, ws = CircleGesture.Reversed ? -1f : 1f;
            bool winding = true;
            PointerInput.SimActive = true;
            float t = 0f;
            while (ctl.State == FishingController.S.Fighting && t < 30f)
            {
                t += Time.deltaTime;
                var f = ctl.Fight;
                if (f != null)
                {
                    if (winding && (f.TensionRatio > 0.8f || f.Jumping)) winding = false;
                    else if (!winding && f.TensionRatio < 0.4f && !f.Jumping) winding = true;
                }
                // (the reel rests for 1.5 s every 5 s: the line goes slack and the float bobs up)
                bool rest = Mathf.Repeat(t, 5f) > 3.5f;
                if (winding && !rest) ang -= ws * Time.deltaTime * 1.6f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                var fm = FloatMoment(ctl, t + 4f);
                if (fm != null) yield return FloatShot(ctl, fm);
                yield return null;
            }
            PointerInput.SimDown = false;
            Log($"[STEER] ice float fight: {N(t, "0.0")}s -> {ctl.State}");
            if (ctl.State == FishingController.S.Landing)
            {
                yield return new WaitForSeconds(0.45f);
                yield return FloatShot(ctl, "float_landing_" + ctl.Stage.Def.id);
            }
            while (ctl.State == FishingController.S.Landing) yield return null;
            if (ctl.State == FishingController.S.Result)
            {
                yield return new WaitForSeconds(0.5f);
                yield return FloatShot(ctl, "float_hang1_" + ctl.Stage.Def.id);
                for (float w = 0; w < 3f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                yield return new WaitForSeconds(0.3f);
                Click("판매");
                yield return new WaitForSeconds(0.5f);
            }
            else if (ctl.State == FishingController.S.Fighting || ctl.State == FishingController.S.Retrieving)
            {
                // still on after 30 s: let go; off already (it broke the line, threw the hook): where the float lies
                if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
                yield return new WaitForSeconds(0.3f);
                yield return FloatShot(ctl, "float_letgo_" + ctl.Stage.Def.id);
            }
            yield return BackToReady(ctl);
        }
    }
}
