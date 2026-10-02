using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// Test autopilot, only active with the -fkauto command-line switch. Drives the simulated
    /// pointer (flick-cast: pull down then flick up; on the ice pull down and let go; tap to hook, draw circles to reel)
    /// and walk keys, logs what happens and saves screenshots into -fkshots &lt;dir&gt;. Scenarios: fish, tour, walk, flick.
    /// <code>
    /// -fkflick &lt;speed&gt;[:&lt;deg&gt;] fish / walk: the casts' flick, window heights per second (the simulated pointer moves
    ///                         like a mouse) and degrees from straight up on screen (right +); default 3.4 H/s (~84%
    ///                         power: mid / far water) straight up
    /// -fkaim &lt;-1..1&gt;         fish: cast this far left / right (-1..1 = the widest throw, the flick aimed through the camera)
    /// -fkfightshots &lt;n&gt;      fish: n fight shots 2.5 s apart (default 1), each with a crop around the angler
    /// </code>
    /// The flick scenario (-fkauto flick) throws weak / medium / strong / max, short, left / straight / right / wide, a
    /// "hook" flick that turns right just before the release, a straight flick after walking to the right end, and
    /// weak / medium / strong as a touchscreen (cm/s), plus releases that must not cast (no motion, too slow, flick then
    /// back down, flick then hold, no wind-up) or that call it off quietly (slowly back up to the press point); per case
    /// it logs the measured speed, angle, power, yaw, distance and where the rig landed (and at what angle on screen),
    /// then checks them ([AUTO] CHECK). On the ice it checks the depth drag instead.
    /// The windup scenario (-fkauto windup) shoots the wind-up's guide full screen: see <see cref="WindupShots"/>.
    /// The steer scenario (-fkauto steer) tests the rod sweep and side pressure: see <see cref="SteerTest"/>.
    /// </summary>
    public partial class AutoPilot : MonoBehaviour
    {
        string shots;
        int shotIndex;

        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static float? ArgF(string key) =>
            float.TryParse(Arg(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : (float?)null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fkauto");
            if (i < 0) return;
            var go = new GameObject("[AutoPilot]");
            DontDestroyOnLoad(go);
            var ap = go.AddComponent<AutoPilot>();
            // the simulated pointer from the start, not pressed: a real mouse on a shared desktop never reaches the game
            // (a scenario may still hand it back)
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            int s = Array.IndexOf(args, "-fkshots");
            ap.shots = s >= 0 && s + 1 < args.Length ? args[s + 1] : Path.Combine(Application.persistentDataPath, "shots");
            Directory.CreateDirectory(ap.shots);
            string scenario = i + 1 < args.Length ? args[i + 1] : "fish";
            // -fkoccwatch: the per-frame occlusion detector watches the whole run (the occlusion scenario always has it)
            if (Array.IndexOf(args, "-fkoccwatch") >= 0 || scenario == "occlusion") OcclusionWatch.Ensure(ap.shots, true);
            // the scenarios that need a fish to come when they put the rig by it: every fish in reach is feeding (the
            // lake's per-encounter roll off, Docs/lake_phase2_spec.md A5)
            if (scenario == "breaks" || scenario == "obstacles" || scenario == "lure" || scenario == "hold" || scenario == "steer"
                || scenario == "pan" || scenario == "occlusion" || scenario == "zoom") FishingController.FeedAll = true;
            ap.StartCoroutine(scenario == "tour" ? ap.Tour() : scenario == "walk" ? ap.Walk() : scenario == "flick" ? ap.FlickTest()
                : scenario == "windup" ? ap.WindupShots() : scenario == "lure" ? ap.LureTest() : scenario == "encounter" ? ap.EncounterTest()
                : scenario == "steer" ? ap.SteerTest() : scenario == "periods" ? ap.PeriodsTest() : scenario == "current" ? ap.CurrentTest()
                : scenario == "tidebites" ? ap.TideBitesTest()
                : scenario == "obstacles" ? ap.ObstaclesTest() : scenario == "occlusion" ? ap.OcclusionTest()
                : scenario == "zoom" ? ap.ZoomTest() : scenario == "legcool" ? ap.LegendCoolTest()
                : scenario == "panmeasure" ? ap.PanMeasure() : scenario == "pan" ? ap.PanTest()
                : scenario == "breaks" ? ap.BreaksTest() : scenario == "legendspot" ? ap.LegendSpotTest()
                : scenario == "music" ? ap.MusicTest() : scenario == "hold" ? ap.HoldTest()
                : scenario == "depth" ? ap.DepthTest() : scenario == "economy" ? ap.EconomySoakTest()
                : scenario == "species" ? ap.SpeciesTest() : ap.Fish());
        }

        /// <summary>-fkflick &lt;speed&gt;[:&lt;deg&gt;]: the flick of the fish / walk scenarios' casts (angle null = not given).</summary>
        static (float speed, float? angle) FlickArg()
        {
            float speed = 3.4f;
            float? angle = null;
            var a = Arg("-fkflick");
            if (!string.IsNullOrEmpty(a))
            {
                var p = a.Split(':');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out float s) && s > 0f) speed = s;
                if (p.Length > 1 && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out float d)) angle = d;
            }
            return (speed, angle);
        }

        // ------------------------------------------------------------------ casting like a player
        /// <summary>Presses at the usual spot and pulls down by <paramref name="depth"/> screen heights (the wind-up); stays down.</summary>
        IEnumerator WindUp(float depth = 0.25f, float time = 0.35f)
        {
            var start = Scr(0.5f, 0.55f);
            PointerInput.SimActive = true;
            PointerInput.SimPos = start;
            PointerInput.SimDown = true;
            yield return null;
            if (depth > 0f) yield return Move(start, start - new Vector2(0f, Screen.height * depth), time, true);
            yield return new WaitForSeconds(0.12f);
        }

        static int flickClockLogs;

        /// <summary>
        /// Flicks the (down) simulated pointer from where it is and lets go: speeds up in 40 ms to <paramref name="speed"/>
        /// screen heights per second along <paramref name="angle"/> (degrees from straight up, right +), holds that speed,
        /// eases to 60% over the last 30 ms (a natural flick slows a little before the finger lifts) and lets go after
        /// <paramref name="travel"/> screen heights. <paramref name="turnTo"/>: the direction swings to this angle between
        /// 35% and 60% of the way (the throw must follow the direction at the release, not the start). <paramref name="hold"/>:
        /// stays still this long before letting go; <paramref name="back"/>: then comes back down this far first. Otherwise
        /// it lets go on the frame of the last move, still moving, as a finger does. Positions follow the unscaled clock, fixed
        /// at 1/60 s a frame while the stroke runs, so the pointer's speed does not depend on the frame rate or a slow frame.
        /// </summary>
        IEnumerator Flick(float speed, float angle, float travel = 0.25f, float turnTo = float.NaN, float hold = 0f, float back = 0f)
        {
            float H = Screen.height, v = Mathf.Max(0.01f, speed) * H, total = Mathf.Max(0.001f, travel) * H;
            float ramp = 0.04f, ease = 0.03f;
            float need = 0.5f * v * ramp + 0.8f * v * ease;
            if (need > total)
            {
                // a short stroke: the same shape, squeezed (never reaches the full speed for long)
                ramp *= total / need;
                ease *= total / need;
            }
            float dRamp = 0.5f * v * ramp, dEase = 0.8f * v * ease;
            float cruise = Mathf.Max(0f, (total - dRamp - dEase) / v);
            float T = ramp + cruise + ease;
            float S(float t)
            {
                if (t < ramp) return 0.5f * v * t * t / ramp;
                if (t < ramp + cruise) return dRamp + v * (t - ramp);
                float u = Mathf.Min(t - ramp - cruise, ease);
                return dRamp + v * cruise + v * u - 0.2f * v * u * u / ease;
            }
            PointerInput.SimActive = true;
            var p = PointerInput.SimPos;
            // The stroke runs on a fixed 1/60 s clock (Time.captureDeltaTime): the pointer's samples are taken when the frame
            // polls, one frame after the position was set, so a slow frame in the middle of a stroke (a screenshot, a GC)
            // used to read as a burst of speed (a 톡 at strength 1.00 instead of 0.39, the lure cycle spoiled at random).
            float capWas = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            float t0 = Time.unscaledTime, done = 0f;
            try
            {
                while (true)
                {
                    if (flickClockLogs < 2 && Time.unscaledTime > t0)
                    {
                        flickClockLogs++;
                        Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "flick clock: unscaled dt {0:0.00000} s, dt {1:0.00000} s (fixed 1/60 while the stroke runs)", Time.unscaledDeltaTime, Time.deltaTime));
                    }
                    float t = Mathf.Min(Time.unscaledTime - t0, T);
                    float s = S(t);
                    float a = float.IsNaN(turnTo) ? angle : Mathf.Lerp(angle, turnTo, Mathf.InverseLerp(0.35f, 0.6f, s / total));
                    float ar = a * Mathf.Deg2Rad;
                    p += new Vector2(Mathf.Sin(ar), Mathf.Cos(ar)) * (s - done);
                    done = s;
                    PointerInput.SimPos = p;
                    bool last = t >= T;
                    // the last position comes with the release (the finger lifts while moving), unless it holds / comes back
                    PointerInput.SimDown = !(last && hold <= 0f && back <= 0f);
                    if (last) break;
                    yield return null;
                }
                // (the release frame too: the lift's sample is taken on the next poll)
                yield return null;
            }
            finally
            {
                Time.captureDeltaTime = capWas;
            }
            if (hold > 0f || back > 0f)
            {
                if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
                if (back > 0f) yield return Move(p, p - new Vector2(0f, back * H), 0.12f, true);
                PointerInput.SimDown = false;
                yield return null;
            }
        }

        /// <summary>
        /// A cast the way a player makes it: on the ice pull down (the depth) and let go; elsewhere wind up and flick
        /// (<paramref name="speed"/> H/s along <paramref name="angle"/> degrees). <paramref name="windShot"/>: a shot while
        /// winding up.
        /// </summary>
        IEnumerator Cast(FishingController ctl, float speed, float angle, string windShot = null)
        {
            yield return WindUp();
            if (windShot != null)
            {
                yield return new WaitForSeconds(0.3f);
                yield return Shot(windShot);
            }
            if (ctl.Stage.L.IsIce)
            {
                PointerInput.SimDown = false;
                yield return null;
            }
            else yield return Flick(speed, angle);
        }

        /// <summary>Winds the rig in (or waits out a bite) until the controller is ready to cast again.</summary>
        IEnumerator BackToReady(FishingController ctl, float timeout = 30f)
        {
            PointerInput.SimDown = false;
            for (float w = 0; w < timeout && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
            {
                if (ctl.State == FishingController.S.Waiting || ctl.State == FishingController.S.Snagged) ctl.Retrieve();   // (snagged: 끊기)
                yield return null;
            }
        }

        void Log(string m) => Debug.Log("[AUTO] " + m);

        IEnumerator Shot(string name)
        {
            yield return AutoShot.Frame();
            string p = Path.Combine(shots, $"{shotIndex++:00}_{name}.png");
            AutoShot.Save(p);
            Log("shot " + p);
            yield return null;
        }

        static Vector2 Scr(float x, float y) => new Vector2(Screen.width * x, Screen.height * y);

        IEnumerator Move(Vector2 from, Vector2 to, float time, bool down)
        {
            PointerInput.SimActive = true;
            float t = 0;
            while (t < 1)
            {
                t += Time.deltaTime / time;
                PointerInput.SimPos = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, t));
                PointerInput.SimDown = down;
                yield return null;
            }
        }

        IEnumerator Tap(Vector2 at)
        {
            PointerInput.SimActive = true;
            PointerInput.SimPos = at;
            PointerInput.SimDown = true;
            yield return null;
            yield return null;
            PointerInput.SimDown = false;
            yield return null;
        }

        static void Click(string label)
        {
            var b = FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.interactable && x.GetComponentInChildren<Text>() != null && x.GetComponentInChildren<Text>().text.Contains(label));
            if (b != null) b.onClick.Invoke();
            else Debug.Log("[AUTO] button not found: " + label);
        }

        // points of a jump (0..1) to shoot at
        static float[] JumpMarks(FightModel.JumpKind k) => k switch
        {
            FightModel.JumpKind.Shake => new[] { 0.12f, 0.3f, 0.42f, 0.55f, 0.68f, 0.88f },
            FightModel.JumpKind.TailWalk => new[] { 0.06f, 0.25f, 0.45f, 0.65f, 0.85f, 0.95f },
            _ => new[] { 0.15f, 0.5f, 0.85f },
        };

        static bool HasButton(string label) => FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Any(x => x.interactable && x.GetComponentInChildren<Text>() != null && x.GetComponentInChildren<Text>().text.Contains(label));

        /// <summary>A shot during a jump; logs where the hooked fish is on screen so the frames can be cropped around it.</summary>
        IEnumerator JumpShot(FishingController ctl, string name)
        {
            var fish = ctl.Hooked;
            if (fish != null && PixelView.Current != null)
            {
                var s = PixelView.Current.WorldToScreen(ctl.Stage.P.To2D(new Vector3(fish.Pos.x, Mathf.Max(0, fish.Pos.y), fish.Pos.z)));
                Log($"jumpshot {shotIndex:00}_{name}.png {s.x:0} {s.y:0}");
                Log($"jumpshot {shotIndex + 1:00}_{name}_clean.png {s.x:0} {s.y:0}");
            }
            yield return Shot(name);
            // the same moment without the HUD (the world is drawn by PixelCanvas, which stays on)
            float ts = Time.timeScale;
            Time.timeScale = 0f;
            var hud = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.enabled && c.isRootCanvas && c.name != "PixelCanvas").ToList();
            foreach (var c in hud) c.enabled = false;
            yield return Shot(name + "_clean");
            foreach (var c in hud) if (c != null) c.enabled = true;
            Time.timeScale = ts;
        }

        // ------------------------------------------------------------------ -fkfloatshots: the float in a fight
        static bool FloatShotsOn => Array.IndexOf(Environment.GetCommandLineArgs(), "-fkfloatshots") >= 0;
        int floatDone;   // the float moments shot in this fight (bits)

        /// <summary>
        /// -fkfloatshots (fish / steer arrow, float rigs): the float moment to shoot now in this fight, each once: riding the
        /// surface dragged across by a run (its wake; on the ice: riding in the hole), pulled under, just popped back up
        /// (its ring), in the air (a jump). Null = none now.
        /// </summary>
        string FloatMoment(FishingController ctl, float t)
        {
            var tk = ctl.Tackle;
            if (!FloatShotsOn || tk.FloatFight != Tackle.FightFloat.Line) return null;
            string st = ctl.Stage.Def.id;
            bool ice = ctl.Stage.L.IsIce;
            if ((floatDone & 1) == 0 && t > 1.2f && tk.FloatRiding && (ice ? t > 2f : ctl.FishRun != 0 && tk.FightRelSpeed > 0.35f))
            {
                floatDone |= 1;
                return "float_ride_" + st;
            }
            if ((floatDone & 2) == 0 && tk.FloatUnder > 0.45f)
            {
                floatDone |= 2;
                return "float_under_" + st;
            }
            if ((floatDone & 4) == 0 && (floatDone & 2) != 0 && tk.SincePop > 0.06f && tk.SincePop < 0.25f && tk.FloatRiding)
            {
                floatDone |= 4;
                return "float_pop_" + st;
            }
            if ((floatDone & 8) == 0 && tk.FloatInAir)
            {
                floatDone |= 8;
                return "float_air_" + st;
            }
            var ar = ctl.PushArrow;
            if ((floatDone & 16) == 0 && ar != null && ar.Visible && ar.AnchorKind == "float" && ar.Alpha >= 0.95f)
            {
                floatDone |= 16;
                return "float_arrow_" + st;
            }
            return null;
        }

        /// <summary>A shot with the float's screen spot logged ("floatshot" lines: screen px, y down) for the review crops.</summary>
        IEnumerator FloatShot(FishingController ctl, string name)
        {
            yield return AutoShot.Frame();
            var tk = ctl.Tackle;
            var pv = PixelView.Current;
            if (pv != null)
            {
                var s = pv.WorldToScreen(tk.FloatShown2D);
                var ar = ctl.PushArrow;
                Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "floatshot {0:00}_{1}.png float {2:0} {3:0} under {4:0.00} riding {5} air {6} taut {7:0.00} rel {8:0.00} run {9:+0;-0;0} state {10} tackle {11} arrow {12} {13}",
                    shotIndex, name, s.x, Screen.height - s.y, tk.FloatUnder, tk.FloatRiding, tk.FloatInAir, tk.FightTaut, tk.FightRelSpeed,
                    ctl.FishRun, ctl.State, tk.State, ar != null && ar.Visible, ar != null ? ar.AnchorKind : "-"));
            }
            string p = Path.Combine(shots, $"{shotIndex++:00}_{name}.png");
            AutoShot.Save(p);
            Log("shot " + p);
            yield return null;
        }

        // ------------------------------------------------------------------ fishing scenario
        IEnumerator Fish()
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
                yield return Shot("tutorial");
                Click("알겠어요");
                yield return new WaitForSeconds(0.5f);
            }
            yield return Shot("ready");
            int caught = 0;
            var argv = Environment.GetCommandLineArgs();
            int ci = Array.IndexOf(argv, "-fkcount");
            int want = ci >= 0 && ci + 1 < argv.Length ? int.Parse(argv[ci + 1]) : 2;
            var aimLat = ArgF("-fkaim");
            var (flickSpeed, flickAngle) = FlickArg();
            float castAngle = flickAngle ?? (aimLat.HasValue ? ctl.FlickAngleFor(Mathf.Clamp(aimLat.Value, -1f, 1f) * FlickCast.YawMax) : 0f);
            int fightShots = int.TryParse(Arg("-fkfightshots"), out int fs) ? Mathf.Clamp(fs, 1, 12) : 1;
            Log($"angler x {ctl.Angler.X:0.00} (range {ctl.Angler.Range.x:0.00}..{ctl.Angler.Range.y:0.00}); flick {flickSpeed:0.00} H/s at {castAngle:0.0} deg");
            for (int attempt = 0; attempt < want * 4 && caught < want; attempt++)
            {
                // cast: press, pull down (the wind-up), flick up and let go (on the ice: pull down and let go)
                yield return Cast(ctl, flickSpeed, castAngle, attempt == 0 ? "aim" : null);
                if (ctl.State == FishingController.S.Ready)
                {
                    Log($"cast #{attempt} did not throw: {FlickCast.Describe(ctl.LastFlick)}");
                    yield return new WaitForSeconds(0.3f);
                    continue;
                }
                Log($"cast #{attempt} -> {ctl.State}");
                float wait = 0;
                while (ctl.State != FishingController.S.Waiting && wait < 5f) { wait += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(1.2f);
                if (attempt == 0) yield return Shot("waiting");
                wait = 0;
                while (ctl.State == FishingController.S.Waiting && wait < 45f) { wait += Time.deltaTime; yield return null; }
                Log($"after wait {wait:0.0}s state {ctl.State}");
                if (ctl.State != FishingController.S.Biting)
                {
                    // (a legend encounter may be on: it ends back in Waiting, so the retrieve is asked for again)
                    while (ctl.State != FishingController.S.Ready)
                    {
                        if (ctl.State == FishingController.S.Waiting || ctl.State == FishingController.S.Snagged) ctl.Retrieve();   // (snagged: 끊기)
                        yield return null;
                    }
                    continue;
                }
                if (caught == 0) yield return Shot("bite");
                Log($"biting: {ctl.Tackle.Bait.id}");
                yield return new WaitForSeconds(0.15f);
                yield return Tap(Scr(0.5f, 0.6f));
                yield return null;
                Log($"hook -> {ctl.State}");
                if (ctl.State != FishingController.S.Fighting) continue;
                floatDone = 0;
                // reel with circles, easing off when the tension gets high
                var c = Scr(0.72f, 0.4f);
                float r = Screen.height * 0.13f, ang = 0, t = 0;
                bool winding = true, shotFight = false;
                int fightShot = 0;
                // with -fkjump: shoot a burst through the first two jumps, winding slowly so the fish stays out
                bool jumpBurst = FightModel.ForceJump.HasValue && caught == 0;
                int jumpsShot = 0, mark = 0;
                bool inJump = false;
                // screen direction that winds in: clockwise, unless reversed in the settings
                float windSign = CircleGesture.Reversed ? -1f : 1f;
                // with -fkgive: after the fight shot, give line for a moment and shoot the give-line arrows
                bool giveTest = Array.IndexOf(Environment.GetCommandLineArgs(), "-fkgive") >= 0 && caught == 0;
                float giveT = -1f;
                bool shotGive = false;
                PointerInput.SimActive = true;
                while (ctl.State == FishingController.S.Fighting && t < 90f)
                {
                    float dt = Time.deltaTime;
                    t += dt;
                    var f = ctl.Fight;
                    if (f != null)
                    {
                        if (winding && (f.TensionRatio > 0.8f || f.Jumping)) winding = false;
                        else if (!winding && f.TensionRatio < 0.55f && !f.Jumping) winding = true;
                    }
                    if (jumpBurst && jumpsShot < 2 && f != null)
                    {
                        var marks = JumpMarks(f.Jump);
                        if (f.Jumping && !inJump) { inJump = true; mark = 0; }
                        if (inJump && f.Jumping && mark < marks.Length && f.JumpT >= marks[mark])
                            yield return JumpShot(ctl, $"jump{jumpsShot}_{f.Jump}_{mark++}");
                        else if (inJump && !f.Jumping)
                        {
                            inJump = false;
                            yield return new WaitForSeconds(0.1f);
                            yield return JumpShot(ctl, $"jump{jumpsShot}_{f.Jump}_{mark}_splash");
                            jumpsShot++;
                        }
                    }
                    // bring the fish within ~12 m first so its jumps are not behind the fight panel
                    float windRate = jumpBurst && jumpsShot < 2 && f != null && f.Line < 12f ? 0.6f : 2.3f;
                    if (giveT >= 0f && giveT < 1.4f)
                    {
                        giveT += dt;
                        ang += windSign * dt * 1.6f * Mathf.PI * 2f; // give line
                    }
                    else if (winding) ang -= windSign * dt * windRate * Mathf.PI * 2f; // wind in
                    else if (f != null && f.TensionRatio > 0.95f) ang += windSign * dt * 1.2f * Mathf.PI * 2f; // give line
                    PointerInput.SimDown = true;
                    PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    // -fkfloatshots: the float riding the line, pulled under, popping up, in the air
                    var fm = FloatMoment(ctl, t);
                    if (fm != null) yield return FloatShot(ctl, fm);
                    if (!shotFight && t > 2.5f)
                    {
                        shotFight = true;
                        if (caught == 0) yield return Shot("fight");
                        if (giveTest) giveT = 0f;
                        fightShot = 1;
                    }
                    else if (caught == 0 && fightShot > 0 && fightShot < fightShots && t > 2.5f * (fightShot + 1))
                    {
                        var fish = ctl.Hooked;
                        float body = ctl.Angler.Model3D != null ? ctl.Angler.Model3D.BodyYaw : 0f;
                        if (fish != null) Log($"fight{fightShot}: fish ({fish.Pos.x:0.0}, {fish.Pos.y:0.0}, {fish.Pos.z:0.0}) body {body:0.0}");
                        yield return Shot("fight" + fightShot);
                        yield return ActorStrip.Strip(ctl, shots, "fight" + fightShot, 1, 1);
                        fightShot++;
                    }
                    if (!shotGive && giveT > 0.9f)
                    {
                        shotGive = true;
                        yield return Shot("give");
                    }
                    yield return null;
                }
                PointerInput.SimDown = false;
                Log($"fight ended after {t:0.0}s -> {ctl.State}");
                if (FloatShotsOn && ctl.State == FishingController.S.Retrieving)
                {
                    // the fish came off: the float stays on the water where it was, then is wound in
                    yield return new WaitForSeconds(0.3f);
                    yield return FloatShot(ctl, "float_letgo_" + ctl.Stage.Def.id);
                }
                string fst = ctl.Stage.Def.id;
                if (caught == 0 && ctl.State == FishingController.S.Landing)
                {
                    yield return new WaitForSeconds(0.5f);
                    yield return FloatShotsOn ? FloatShot(ctl, "float_landing_" + fst) : Shot("landing");
                }
                while (ctl.State == FishingController.S.Landing) yield return null;
                if (ctl.State == FishingController.S.Result)
                {
                    // the landed fish hangs swaying on the line for a second before the catch card comes up
                    if (caught == 0)
                        for (int i = 0; i < 3; i++)
                        {
                            yield return new WaitForSeconds(0.25f);
                            yield return FloatShotsOn ? FloatShot(ctl, $"float_hang{i}_{fst}") : Shot("hang" + i);
                        }
                    for (float w = 0; w < 3f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                    yield return new WaitForSeconds(0.4f);
                    if (caught < 3) yield return Shot("catch_" + caught);
                    Log($"caught #{caught}: coins {Game.Data.coins} lv {Game.Data.level} xp {Game.Data.xp}");
                    Click(caught % 3 == 1 && !Game.I.AquariumFull ? "보관" : "판매");
                    caught++;
                    yield return new WaitForSeconds(0.6f);
                }
                yield return new WaitForSeconds(0.5f);
            }
            Log($"done, caught {caught}, coins {Game.Data.coins}, aquarium {Game.Data.aquarium.Count}");
            yield return Shot("end");
            PointerInput.SimActive = false;
            Application.Quit();
        }

        // ------------------------------------------------------------------ walk scenario
        /// <summary>
        /// Walks with the simulated keys to the left and then the right end of the standing area (a strip of crops around
        /// the feet on the way and while stopping, a full shot at each end), back to the middle, then holds an aim to
        /// the left, centre and right (shot, not cast) so the body and rod turn can be checked.
        /// </summary>
        IEnumerator Walk()
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
            var a = ctl.Angler;
            string id = ctl.Stage.Def.id;
            Log($"walk {id}: range {a.Range.x:0.00}..{a.Range.y:0.00}, starts at {a.X:0.00}");
            yield return Shot(id + "_start");
            yield return WalkToEnd(ctl, -1, id + "_left");
            yield return WalkToEnd(ctl, 1, id + "_right");
            yield return WalkTo(ctl, (a.Range.x + a.Range.y) * 0.5f);
            yield return new WaitForSeconds(0.6f);
            foreach (var (lat, tag) in new[] { (-0.9f, "aimL"), (0f, "aimC"), (0.9f, "aimR") })
                yield return AimHold(ctl, lat, id + "_" + tag);
            Log("walk done");
            PointerInput.SimActive = false;
            Application.Quit();
        }

        static void SetWalk(int side)
        {
            PointerInput.SimLeft = side < 0;
            PointerInput.SimRight = side > 0;
        }

        IEnumerator WalkToEnd(FishingController ctl, int side, string tag)
        {
            var a = ctl.Angler;
            SetWalk(side);
            float t = 0;
            bool strip = false;
            while (t < 8f && (side < 0 ? a.X > a.Range.x + 0.005f : a.X < a.Range.y - 0.005f))
            {
                t += Time.deltaTime;
                if (!strip && t > 0.2f)
                {
                    strip = true;
                    yield return ActorStrip.Strip(ctl, shots, tag + "_walk", 8, 5); // ~0.65 s: about two steps
                    continue;
                }
                yield return null;
            }
            SetWalk(0);
            Log($"{tag}: x {a.X:0.000} after {t:0.00}s");
            yield return ActorStrip.Strip(ctl, shots, tag + "_stop", 6, 3);
            yield return new WaitForSeconds(0.4f);
            yield return Shot(tag);
        }

        IEnumerator WalkTo(FishingController ctl, float x)
        {
            var a = ctl.Angler;
            float t = 0;
            while (t < 8f && Mathf.Abs(a.X - x) > 0.03f)
            {
                SetWalk(a.X < x ? 1 : -1);
                t += Time.deltaTime;
                yield return null;
            }
            SetWalk(0);
        }

        /// <summary>
        /// Shows him turned to one side (lateral -1..1 = left..right): he faces forward while winding up and turns with
        /// the throw, so off the ice this flick-casts that way and shoots him while the cast goes out (then winds in); on the
        /// ice it pulls an aim and holds it for the shot (he faces the hole), letting go with no pull (no cast).
        /// </summary>
        IEnumerator AimHold(FishingController ctl, float lateral, string tag)
        {
            if (!ctl.Stage.L.IsIce)
            {
                var (speed, _) = FlickArg();
                yield return WindUp();
                yield return Flick(speed, ctl.FlickAngleFor(lateral * FlickCast.YawMax));
                yield return new WaitForSeconds(0.6f);
                var m3 = ctl.Angler.Model3D;
                Log($"{tag}: state {ctl.State} yaw {ctl.LastFlick.yaw:0.0} body {(m3 != null ? m3.BodyYaw : 0f):0.0} head {(m3 != null ? m3.HeadYaw : 0f):0.0}");
                yield return Shot(tag);
                yield return ActorStrip.Strip(ctl, shots, tag, 1, 1);
                yield return BackToReady(ctl);
                yield return new WaitForSeconds(0.5f);
                yield break;
            }
            var start = Scr(0.5f, 0.55f);
            PointerInput.SimActive = true;
            PointerInput.SimPos = start;
            PointerInput.SimDown = true;
            yield return null;
            var pull = Scr(0.5f - lateral * 0.3f, 0.3f);
            yield return Move(start, pull, 0.4f, true);
            yield return new WaitForSeconds(0.5f);
            var m = ctl.Angler.Model3D;
            Log($"{tag}: state {ctl.State} body {(m != null ? m.BodyYaw : 0f):0.0} head {(m != null ? m.HeadYaw : 0f):0.0}");
            yield return Shot(tag);
            yield return ActorStrip.Strip(ctl, shots, tag, 1, 1);
            // back to the press point: no power, so letting go cancels the cast
            yield return Move(pull, start, 0.3f, true);
            PointerInput.SimDown = false;
            for (float w = 0; w < 2f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.5f);
        }

        // ------------------------------------------------------------------ flick scenario
        class FlickCase
        {
            public string name, shotWind, shotAfter;
            public float speed, angle, travel = 0.25f, turnTo = float.NaN, hold, back, shotDelay = 0.25f;
            public float touchCm = 0f;      // > 0: a touchscreen flick of this many cm/s (speed is then worked out from it)
            public bool walked;             // first walk him to the right end of his standing area
            public bool wind = true, expectCast = true, expectQuiet;
        }

        struct FlickOut
        {
            public bool cast;
            public float dist, bearing, screenAngle, anglerX;
            public FlickCast.Result r;
        }

        /// <summary>The simulated touchscreen's dpi in the flick test (a phone: 1080 px high = 6.9 cm).</summary>
        const float TestDpi = 400f;

        /// <summary>The angle on screen (degrees from straight up, right +) from the water under his feet to a water point.</summary>
        static float ScreenAngle(FishingController ctl, float anglerX, Vector3 to)
        {
            var P = ctl.Stage.P;
            var pv = PixelView.Current;
            Vector2 a = P.To2D(new Vector3(anglerX, 0f, 0f)), b = P.To2D(new Vector3(to.x, 0f, to.z));
            if (pv != null)
            {
                a = pv.WorldToScreen(a);
                b = pv.WorldToScreen(b);
            }
            return Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// -fkauto flick: flick casts of known speed and direction, each logged with what the controller measured and where
        /// the rig landed, releases that must not cast, then the checks ([AUTO] CHECK ...). On the ice: the depth drag.
        /// </summary>
        IEnumerator FlickTest()
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
            var L = ctl.Stage.L;
            Log($"flick test {ctl.Stage.Def.id}: screen {Screen.width}x{Screen.height} rod {Game.I.Rod.id} castDist {Game.I.Rod.castDist:0.0} " +
                $"(distance {L.zNear + 2.5f:0.0}..{Game.I.Rod.castDist:0.0} m) angler x {ctl.Angler.X:0.00}");
            if (L.IsIce)
            {
                yield return IceTest(ctl);
                PointerInput.SimActive = false;
                Application.Quit();
                yield break;
            }
            var cases = new[]
            {
                new FlickCase { name = "none", speed = 0f, expectCast = false, shotWind = "windup_guide", shotAfter = "cancel_hint" },
                new FlickCase { name = "slow", speed = 0.25f, travel = 0.12f, expectCast = false },
                new FlickCase { name = "updown", speed = 3.0f, travel = 0.12f, back = 0.1f, expectCast = false },
                new FlickCase { name = "late", speed = 3.4f, hold = 0.45f, expectCast = false },
                new FlickCase { name = "nowind", speed = 3.4f, wind = false, expectCast = false },
                new FlickCase { name = "return", speed = 0.2f, travel = 0.25f, expectCast = false, expectQuiet = true, shotAfter = "return_quiet" },
                new FlickCase { name = "weak", speed = 1.2f },
                new FlickCase { name = "medium", speed = 2.2f, shotAfter = "power_medium" },
                new FlickCase { name = "strong", speed = 3.2f },
                new FlickCase { name = "max", speed = 5.5f, travel = 0.35f, shotAfter = "power_max" },
                new FlickCase { name = "short", speed = 4.0f, travel = 0.05f },
                new FlickCase { name = "left", speed = 3.4f, angle = -25f },
                new FlickCase { name = "straight", speed = 3.4f, angle = 2f },
                new FlickCase { name = "right", speed = 3.4f, angle = 25f, shotAfter = "throw_right", shotDelay = 0.5f },
                new FlickCase { name = "wideL", speed = 3.4f, angle = -65f },
                new FlickCase { name = "hook", speed = 3.4f, angle = 0f, turnTo = 20f },
                new FlickCase { name = "walked", speed = 3.4f, angle = 0f, walked = true, shotAfter = "walked_straight", shotDelay = 0.5f },
                new FlickCase { name = "touchWeak", touchCm = 15f, travel = 0.35f },
                new FlickCase { name = "touchMedium", touchCm = 32f, travel = 0.35f },
                new FlickCase { name = "touchStrong", touchCm = 50f, travel = 0.35f },
            };
            var outs = new System.Collections.Generic.Dictionary<string, FlickOut>();
            float homeX = ctl.Angler.X;
            foreach (var c in cases)
            {
                int before = ctl.FlickCount;
                ctl.Angler.DebugPlace(c.walked ? ctl.Angler.Range.y : homeX);
                PointerInput.SimDpi = c.touchCm > 0f ? TestDpi : 0f;
                if (c.touchCm > 0f) c.speed = c.touchCm * (TestDpi / 2.54f) / Screen.height;
                yield return null;
                if (c.wind) yield return WindUp();
                else
                {
                    PointerInput.SimActive = true;
                    PointerInput.SimPos = Scr(0.5f, 0.45f);
                    PointerInput.SimDown = true;
                    yield return null;
                    yield return new WaitForSeconds(0.12f);
                }
                if (c.shotWind != null)
                {
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot(c.shotWind);
                }
                if (c.speed <= 0f)
                {
                    PointerInput.SimDown = false;
                    yield return null;
                }
                else yield return Flick(c.speed, c.angle, c.travel, c.turnTo, c.hold, c.back);
                for (float w = 0; w < 1f && ctl.FlickCount == before && ctl.State == FishingController.S.Aiming; w += Time.deltaTime) yield return null;
                var o = new FlickOut { r = ctl.LastFlick, cast = ctl.State == FishingController.S.Casting, anglerX = ctl.Angler.X };
                if (ctl.FlickCount == before) Log($"flick {c.name}: no release seen (state {ctl.State})");
                if (c.shotAfter != null)
                {
                    yield return new WaitForSeconds(c.shotDelay);
                    yield return Shot(c.shotAfter);
                }
                string sent = string.Format(System.Globalization.CultureInfo.InvariantCulture, "sent {0:0.00} H/s{1} at {2:+0.0;-0.0;0.0} deg{3}{4}",
                    c.speed, c.touchCm > 0f ? $" (touch {c.touchCm:0} cm/s at {TestDpi:0} dpi)" : "", c.angle,
                    float.IsNaN(c.turnTo) ? "" : $" turning to {c.turnTo:+0;-0} deg", c.walked ? $" from x {o.anglerX:0.00}" : "");
                if (o.cast)
                {
                    for (float w = 0; w < 6f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
                    var s = ctl.Tackle.Surface;
                    var rel = new Vector2(s.x - o.anglerX, s.z);
                    o.dist = rel.magnitude;
                    o.bearing = Mathf.Atan2(rel.x, rel.y) * Mathf.Rad2Deg;
                    o.screenAngle = ScreenAngle(ctl, o.anglerX, s);
                    Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "flick {0}: {1} -> measured {2:0.0} {3} ({4:0.00} H/s) angle {5:+0.0;-0.0;0.0} (stroke {6:+0.0;-0.0;0.0}) aim {7:+0.0;-0.0;0.0} power {8:0.00} yaw {9:+0.0;-0.0;0.0} dist {10:0.0} m -> landed ({11:0.00}, {12:0.00}) {13:0.0} m bearing {14:+0.0;-0.0;0.0} deg, on screen {15:+0.0;-0.0;0.0} deg",
                        c.name, sent, o.r.speed, o.r.unit, o.r.speedH, o.r.angle, o.r.strokeAngle, o.r.aim, o.r.power, o.r.yaw, o.r.dist,
                        s.x, s.z, o.dist, o.bearing, o.screenAngle));
                }
                else
                    Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "flick {0}: {1}{2} -> CANCELLED ({3}{4}) measured {5:0.00} {6} (whole stroke {7:0.00}) travel {8:0.000} H wind {9:0.000} H release {10:+0.000;-0.000} H armed {11} -> state {12}",
                        c.name, sent, c.wind ? "" : " (no wind-up)", string.IsNullOrEmpty(o.r.why) ? (o.r.armed ? "?" : "no wind-up") : (o.r.armed ? o.r.why : "no wind-up"),
                        o.r.quiet ? ", quiet" : "", o.r.speed, o.r.unit, o.r.speedAny, o.r.travel, o.r.windUp, o.r.releaseDrop, o.r.armed, ctl.State));
                outs[c.name] = o;
                yield return BackToReady(ctl);
                yield return new WaitForSeconds(0.5f);
            }
            PointerInput.SimDpi = 0f;
            ctl.Angler.DebugPlace(homeX);

            // ---- checks
            int fails = 0;
            void Check(string what, bool ok)
            {
                if (!ok) fails++;
                Log($"CHECK {(ok ? "PASS" : "FAIL")} {what}");
            }
            foreach (var c in cases) Check($"{c.name} {(c.expectCast ? "casts" : "does not cast")}", outs[c.name].cast == c.expectCast);
            foreach (var c in cases)
                if (!c.expectCast && c.wind)
                    Check($"{c.name}: {(c.expectQuiet ? "called off quietly (no hint, no buzz)" : "a missed flick (hint)")}", outs[c.name].r.quiet == c.expectQuiet);
            float D(string n) => outs[n].cast ? outs[n].dist : -1f;
            Check($"weak < medium < strong <= max distance ({D("weak"):0.0} < {D("medium"):0.0} < {D("strong"):0.0} <= {D("max"):0.0})",
                D("weak") > 0f && D("weak") < D("medium") && D("medium") < D("strong") && D("strong") <= D("max"));
            Check($"the powers spread over the range: weak {outs["weak"].r.power:0.00} < 0.4, medium {outs["medium"].r.power:0.00} in 0.35..0.7, strong {outs["strong"].r.power:0.00} in 0.6..0.95",
                outs["weak"].r.power < 0.4f && outs["medium"].r.power > 0.35f && outs["medium"].r.power < 0.7f && outs["strong"].r.power > 0.6f && outs["strong"].r.power < 0.95f);
            Check($"max flick = full power ({outs["max"].r.power:0.00})", outs["max"].r.power >= 0.99f);
            Check($"short stroke weaker than the same speed's full stroke ({outs["short"].r.power:0.00} < {outs["max"].r.power:0.00})",
                outs["short"].cast && outs["short"].r.power < outs["max"].r.power);
            float B(string n) => outs[n].bearing;
            Check($"left lands left ({B("left"):+0.0;-0.0} deg), straight centre ({B("straight"):+0.0;-0.0}), right lands right ({B("right"):+0.0;-0.0})",
                B("left") < -5f && Mathf.Abs(B("straight")) < 1f && B("right") > 5f);
            // the rig lands on the line the finger flicked along, as seen on screen (from the water under his feet)
            foreach (var n in new[] { "left", "right", "hook", "walked" })
            {
                var o = outs[n];
                Check($"{n}: lands on the flicked line on screen ({o.screenAngle:+0.0;-0.0} deg vs aim {o.r.aim:+0.0;-0.0}; world yaw {o.r.yaw:+0.0;-0.0})",
                    o.cast && Mathf.Abs(o.screenAngle - o.r.aim) < 1.5f);
            }
            Check($"straight flick (2 deg) inside the dead zone: aim {outs["straight"].r.aim:0.0}, yaw {outs["straight"].r.yaw:0.0}",
                outs["straight"].r.aim == 0f && Mathf.Abs(outs["straight"].r.yaw) < 0.01f);
            Check($"walked to x {outs["walked"].anglerX:0.00}: a straight-up flick lands straight up the screen, not towards the vanishing point (yaw {outs["walked"].r.yaw:+0.0;-0.0})",
                outs["walked"].anglerX > 0.5f && outs["walked"].r.yaw > 1f);
            Check($"wide flick (-65 deg) clamped to {FlickCast.YawMax} deg: yaw {outs["wideL"].r.yaw:0.0}", Mathf.Abs(outs["wideL"].r.yaw + FlickCast.YawMax) < 0.01f);
            var hk = outs["hook"].r;
            Check($"hook: the throw follows the direction at the release ({hk.angle:+0.0;-0.0}, aim {hk.aim:+0.0;-0.0}), not the stroke's ({hk.strokeAngle:+0.0;-0.0})",
                hk.angle > 15f && hk.aim > hk.strokeAngle + 3f);
            var tw = outs["touchWeak"].r;
            var tm = outs["touchMedium"].r;
            var ts = outs["touchStrong"].r;
            Check($"touch measured in cm/s ({tw.unit}): weak {tw.speed:0} < medium {tm.speed:0} < strong {ts.speed:0} cm/s -> power {tw.power:0.00} < {tm.power:0.00} < {ts.power:0.00}",
                tw.unit == "cm/s" && tw.power > 0f && tw.power < tm.power && tm.power < ts.power && ts.power < 0.99f);
            Log($"flick test done: {fails} failed");
            PointerInput.SimActive = false;
            Application.Quit();
        }

        /// <summary>
        /// The ice keeps the drag: pull down and let go drops the jig into the hole at that depth (no flick needed), and a
        /// pull then flick up does what it always did (the release point is above the press: no depth, no cast).
        /// </summary>
        IEnumerator IceTest(FishingController ctl)
        {
            var L = ctl.Stage.L;
            int flicks = ctl.FlickCount;
            yield return WindUp();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("ice_drag");
            float power = ctl.AimPower, depth = ctl.AimDepth;
            PointerInput.SimDown = false;
            yield return null;
            yield return null;
            var st = ctl.State;
            for (float w = 0; w < 5f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
            float want = Mathf.Lerp(0.6f, L.DepthAt(L.holeX, L.holeZ) - 0.3f, 0.25f / 0.3f);
            Log($"ice drag: aim power {power:0.00} depth {depth:0.00} m (expected {want:0.00}) -> {st} -> {ctl.State}, float depth {ctl.Tackle.FloatDepth:0.00} m");
            Log($"CHECK {(st == FishingController.S.Casting && Mathf.Abs(depth - want) < 0.05f && Mathf.Abs(ctl.Tackle.FloatDepth - depth) < 0.01f ? "PASS" : "FAIL")} ice: pull down + let go drops the jig at the drag's depth");
            yield return BackToReady(ctl);
            yield return new WaitForSeconds(0.5f);
            yield return WindUp();
            yield return Flick(2.0f, 0f, 0.4f);
            yield return null;
            Log($"ice pull + flick up: -> {ctl.State} (the release is above the press point: no depth, no cast, as before)");
            Log($"CHECK {(ctl.State == FishingController.S.Ready && ctl.FlickCount == flicks ? "PASS" : "FAIL")} ice: no flick casting (release above the press = no cast; flick releases counted {ctl.FlickCount - flicks})");
            yield return BackToReady(ctl);
        }

        // ------------------------------------------------------------------ wind-up guide shots
        /// <summary>
        /// -fkauto windup: full-screen shots (&lt;stage&gt;_&lt;i&gt;_&lt;name&gt;.png) of the flick wind-up with its guide (the arrow
        /// over his head, the rod following the finger): barely pulled (prearm: the arrow dim), armed with the finger
        /// straight down (down, then each frame of one loop of the arrow's glint: loop_f1..f7, loop_f0), down-left, down-right, and mid-flick (the finger on its way up past the press point,
        /// still down); then the flick is let go and it checks that the arrow went with it. Per shot it logs the arrow's
        /// state and where the hat and the arrow are on screen ([AUTO] windup shot ..., for crops). Quits when done.
        /// </summary>
        IEnumerator WindupShots()
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
            for (float w = 0; w < 5f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.5f);
            string st = ctl.Stage.Def.id + (ctl.Angler.Uses3D ? "" : "_2d");
            float W = Screen.width, H = Screen.height;
            var press = Scr(0.5f, 0.6f);
            Vector2 At(float lat, float down) => press + new Vector2(lat * W, -down * H);
            Log($"windup shots {st}: screen {Screen.width}x{Screen.height} rod {Game.I.Rod.id} angler x {ctl.Angler.X:0.00} ice {ctl.Stage.L.IsIce}");
            PointerInput.SimDpi = 0f;
            PointerInput.SimActive = true;
            PointerInput.SimPos = press;
            PointerInput.SimDown = true;
            yield return null;
            yield return null;
            yield return Move(press, At(0f, 0.04f), 0.15f, true);
            yield return new WaitForSeconds(0.3f);
            yield return WindupShot(ctl, st, "prearm");
            yield return Move(At(0f, 0.04f), At(0f, 0.2f), 0.25f, true);
            yield return new WaitForSeconds(0.6f);
            yield return WindupShot(ctl, st, "down");
            // one armed loop of the arrow, a shot of each frame as it is drawn (loop_f1..f7: the glint from the tail behind
            // his head over the top to the head in front; loop_f0: the rest), logging when each came up
            float lt0 = Time.unscaledTime;
            for (int k = 1; k <= 8 && Time.unscaledTime - lt0 < 8f;)
            {
                yield return AutoShot.Frame();
                int want = k % 8;
                if (ctl.Arrow.Frame != want) continue;
                Log($"windup loop frame {want}: t={Time.time:0.000}");
                yield return WindupShot(ctl, st, $"loop_f{want}", true);
                k++;
            }
            yield return Move(At(0f, 0.2f), At(-0.22f, 0.2f), 0.3f, true);
            yield return new WaitForSeconds(0.5f);
            yield return WindupShot(ctl, st, "downleft");
            yield return Move(At(-0.22f, 0.2f), At(0.22f, 0.2f), 0.45f, true);
            yield return new WaitForSeconds(0.5f);
            yield return WindupShot(ctl, st, "downright");
            yield return Move(At(0.22f, 0.2f), At(0f, 0.22f), 0.3f, true);
            yield return new WaitForSeconds(0.4f);
            // the flick: straight up at 2.5 H/s on the unscaled clock, a shot as the finger passes 0.05 H above the press
            // point, let go (still moving) 0.12 H above it
            float y0 = At(0f, 0.22f).y, top = press.y + 0.12f * H, t0 = Time.unscaledTime;
            bool mid = false;
            while (true)
            {
                float y = Mathf.Min(top, y0 + 2.5f * H * (Time.unscaledTime - t0));
                PointerInput.SimPos = new Vector2(press.x, y);
                bool last = y >= top;
                PointerInput.SimDown = !last;
                if (last) break;
                if (!mid && y >= press.y + 0.05f * H)
                {
                    mid = true;
                    yield return WindupShot(ctl, st, "midflick");
                }
                else yield return null;
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            bool gone = !ctl.Arrow.Visible;
            Log($"windup release: state {ctl.State} arrow visible {ctl.Arrow.Visible} flick {FlickCast.Describe(ctl.LastFlick)}");
            Log($"CHECK {(gone ? "PASS" : "FAIL")} windup: the arrow is gone on the release frame");
            PointerInput.SimActive = false;
            for (float w = 0; w < 6f && ctl.State == FishingController.S.Casting; w += Time.deltaTime) yield return null;
            Log($"windup shots {st} done (state {ctl.State}, arrow visible {ctl.Arrow.Visible})");
            Application.Quit();
        }

        /// <summary>
        /// This frame, whole screen (after it is drawn), with the arrow's state and the hat / arrow screen positions (y down);
        /// <paramref name="atEnd"/> = already at the end of the frame (take it now).
        /// </summary>
        IEnumerator WindupShot(FishingController ctl, string st, string name, bool atEnd = false)
        {
            if (!atEnd) yield return AutoShot.Frame();
            var tex = AutoShot.Texture();
            string p = Path.Combine(shots, $"{st}_{shotIndex++:00}_{name}.png");
            File.WriteAllBytes(p, tex.EncodeToPNG());
            Destroy(tex);
            var a = ctl.Angler;
            var ar = ctl.Arrow;
            var pv = PixelView.Current;
            Vector2 S(Vector2 world)
            {
                var s = pv.WorldToScreen(world);
                return new Vector2(s.x, Screen.height - s.y);
            }
            var w = a.WindUp ?? new Vector2(float.NaN, float.NaN);
            var hat = S(a.HatPos2D);
            var arrow = S(ar.Pos);
            var feet = S(ctl.Stage.P.To2D(a.Feet) + ctl.Stage.DeckBob);
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "windup shot {0} {1}: state={2} armed={3} pose={4} finger=({5:+0.000;-0.000;0.000},{6:+0.000;-0.000;0.000}) pitch={7:+0.0;-0.0;0.0} lean={8:+0.0;-0.0;0.0} arrow visible={9} alpha={10:0.00} bright={11} hat=({12:0},{13:0}) arrowc=({14:0},{15:0}) feet=({16:0},{17:0})",
                name, Path.GetFileName(p), ctl.State, ctl.WindUpArmed, a.Pose, w.x, w.y, a.RodAngles.x, a.RodAngles.y, ar.Visible, ar.Alpha, ar.Bright,
                hat.x, hat.y, arrow.x, arrow.y, feet.x, feet.y));
        }

        // ------------------------------------------------------------------ screen tour
        IEnumerator Tour()
        {
            yield return new WaitForSeconds(1.5f);
            yield return Shot("title");
            SettingsUI.Open();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("settings");
            CloseDialogs();
            SceneFlow.Go("Map");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("map");
            var canvas = GameObject.Find("MapUI").transform;
            // stage info dialog (lake marker)
            var marker = GameObject.Find("Marker_lake");
            if (marker != null) marker.GetComponentInChildren<Button>().onClick.Invoke();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("stage_info");
            CloseDialogs();
            ShopUI.Open(canvas);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("shop");
            ShopUI.Open(canvas, ItemKind.Reel);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("shop_reel");
            ShopUI.Open(canvas, ItemKind.Line);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("shop_line");
            ShopUI.Open(canvas, ItemKind.Bait);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("shop_bait");
            ShopUI.Open(canvas, ItemKind.Tank);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("shop_tank");
            CollectionUI.Open(canvas);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("collection");
            var tile = FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.transform.parent != null && b.transform.parent.name.StartsWith("Grid_"));
            if (tile != null)
            {
                tile.onClick.Invoke();
                yield return new WaitForSeconds(0.6f);
                yield return Shot("collection_detail");
                CloseDialogs();
            }
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(1.8f);
            yield return Shot("aquarium");
            var fishInTank = FindObjectsByType<TankFish>(FindObjectsSortMode.None).FirstOrDefault();
            if (fishInTank != null && PixelView.Current != null)
            {
                yield return Tap(PixelView.Current.WorldToScreen(fishInTank.Pos));
                yield return new WaitForSeconds(0.6f);
                yield return Shot("aquarium_fish");
                CloseDialogs();
            }
            PointerInput.SimActive = false;
            foreach (var st in GameDatabase.Stages)
            {
                SceneFlow.PendingStage = st.id;
                SceneFlow.Go("Fishing");
                yield return new WaitForSeconds(2.2f);
                yield return Shot("stage_" + st.id);
                if (st.id == "lake")
                {
                    var bait = FindObjectsByType<Button>(FindObjectsSortMode.None)
                        .FirstOrDefault(b => b.transform.parent != null && b.transform.parent.name == "Tackle");
                    if (bait != null)
                    {
                        bait.onClick.Invoke();
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot("bait_picker");
                        CloseDialogs();
                    }
                }
            }
            Application.Quit();
        }

        static void CloseDialogs()
        {
            var d = GameObject.Find("[Dialogs]");
            if (d == null) return;
            for (int i = d.transform.childCount - 1; i >= 0; i--) Destroy(d.transform.GetChild(i).gameObject);
        }
    }
}
