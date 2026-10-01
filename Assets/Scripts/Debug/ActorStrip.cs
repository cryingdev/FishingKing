using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Test switches for the real-time 3D angler / reel (<see cref="Angler3D"/>, <see cref="Reel3D"/>):
    /// <code>
    /// -fkreel &lt;id&gt;            equip this reel once the fishing scene is up (e.g. reel_baitcast)
    /// -fkactorstrip &lt;dir&gt;     save frame strips of the pixel view around the angler (1:1 pixels, no UI) while the game
    ///                         runs (use with -fkauto fish): ready, waiting, fight, landing
    /// -fkstripn &lt;n&gt;           frames per strip (default 6)
    /// -fkstripk &lt;frames&gt;      rendered frames between strip frames (default 2)
    /// -fkactors2d             keep the 2D pose sprites (see Angler), for comparisons
    /// -fkcrank &lt;dir&gt;          at the ready: ready.png (whole screen) and crank_ready crops, then per reeling pose (reel,
    ///                         fight, idle) one crank turn in 8 frames (crank_&lt;tag&gt;_&lt;pose&gt;_&lt;i&gt;.png 1:1 crops and a 5x
    ///                         crank_&lt;tag&gt;_&lt;pose&gt;_strip.png of the upper body, the 9th tile tracing the knob path) with
    ///                         the right elbow's interior angle per frame, its min / max and the knob path's on-screen
    ///                         ellipse (axis ratio, major axis angle vs the rod's) in the log ([Crank]); quits when done
    /// -fkcrankposes a,b       the poses for -fkcrank (default reel,fight,idle; "none" = the ready shots only)
    /// -fkcrankface f          -fkcrank: he faces L / R (far left / right), hole (the ice hole, line in it) or x:z
    /// -fkholdaudit            -fkcrank: per variant also the hold audit (rod clear of the hat at the walk ends, facings,
    ///                         poses; see Audit)
    /// -fkarmtune "v;v;..."    variants for -fkcrank, tried one after the other: comma separated key=value (see Tune):
    ///                         name, h / hi / hr / hf (hold hand x:y:z, all / idle / reel / fight, "none" = the sprite's),
    ///                         reels, scale, roll (reel), lean (rod), turn, rsh, poler, polel (Angler3D reeling hold),
    ///                         hc (the middle hold's hand x:y:z), crsh, cpoler, cpolel (its posture; Angler.RodCentre)
    /// -fkwindup &lt;dir&gt;         the rod following the finger in the flick wind-up (see WindUpRun): a grid of finger
    ///                         offsets (windup_&lt;stage&gt;_strip.png), a clearance sweep, a flick's cast swing and a
    ///                         wind-up called off, with the rod's pitch / lean and hat clearance in the log ([Windup]);
    ///                         quits when done
    /// </code>
    /// Per strip: actor_&lt;tag&gt;_&lt;i&gt;.png crops and actor_&lt;tag&gt;_strip.png (crops side by side, 2 px magenta gaps); the
    /// log lists per frame the reel revolutions and where the knob / right fist / left fist land on the render target.
    /// </summary>
    public class ActorStrip : MonoBehaviour
    {
        string dir;
        int frames = 6, every = 2;
        const int CropW = 112, CropH = 124;

        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string reel = Arg("-fkreel");
            string dir = Arg("-fkactorstrip");
            string crank = Arg("-fkcrank");
            string wind = Arg("-fkwindup");
            if (string.IsNullOrEmpty(reel) && string.IsNullOrEmpty(dir) && string.IsNullOrEmpty(crank) && string.IsNullOrEmpty(wind)) return;
            var go = new GameObject("[ActorStrip]");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<ActorStrip>();
            s.dir = dir;
            if (int.TryParse(Arg("-fkstripn"), out int n)) s.frames = Mathf.Clamp(n, 1, 32);
            if (int.TryParse(Arg("-fkstripk"), out int k)) s.every = Mathf.Clamp(k, 1, 60);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (!string.IsNullOrEmpty(crank)) Directory.CreateDirectory(crank);
            if (!string.IsNullOrEmpty(wind)) Directory.CreateDirectory(wind);
            s.StartCoroutine(s.Run(reel, crank, wind));
        }

        IEnumerator Run(string reel, string crank, string wind)
        {
            FishingController ctl = null;
            while (ctl == null)
            {
                ctl = FindAnyObjectByType<FishingController>();
                yield return null;
            }
            if (!string.IsNullOrEmpty(reel))
            {
                var r = GameDatabase.GetItem<ReelDef>(reel);
                if (r != null)
                {
                    if (!Game.I.Owns(r.id)) Game.Data.ownedItems.Add(r.id);
                    Game.I.Equip(r);
                    Debug.Log("[ActorStrip] equipped " + r.id);
                }
                else Debug.Log("[ActorStrip] no reel " + reel);
            }
            if (!string.IsNullOrEmpty(crank))
            {
                yield return Crank(ctl, crank);
                yield break;
            }
            if (!string.IsNullOrEmpty(wind))
            {
                yield return WindUpRun(ctl, wind);
                yield break;
            }
            if (string.IsNullOrEmpty(dir)) yield break;
            yield return new WaitForSeconds(1.2f);
            yield return Strip(ctl, "ready");
            bool waiting = false, fight = false, landing = false, hook = false;
            float fightT = 0f;
            while (ctl != null && !(fight && landing))
            {
                if (!hook && ctl.State == FishingController.S.Biting)
                {
                    hook = true;
                    yield return HookBurst(ctl);
                }
                if (!waiting && ctl.State == FishingController.S.Waiting)
                {
                    waiting = true;
                    yield return new WaitForSeconds(0.8f);
                    yield return Strip(ctl, "waiting");
                }
                if (ctl.State == FishingController.S.Fighting)
                {
                    fightT += Time.deltaTime;
                    if (!fight && fightT > 1.6f)
                    {
                        fight = true;
                        yield return Strip(ctl, "fight");
                    }
                }
                if (!landing && ctl.State == FishingController.S.Landing)
                {
                    landing = true;
                    yield return Strip(ctl, "landing");
                }
                yield return null;
            }
            Debug.Log("[ActorStrip] done");
        }

        /// <summary>
        /// Every rendered frame (whole render target) from the bite until 8 frames into the fight: the hook set shakes
        /// the camera, so the 3D layers must shift exactly like the painted pier (actor_hook_&lt;i&gt;.png + camera offsets in the log).
        /// </summary>
        IEnumerator HookBurst(FishingController ctl)
        {
            var pv = PixelView.Current;
            if (pv == null) yield break;
            int after = 0;
            for (int i = 0; i < 40 && after < 8; i++)
            {
                yield return new WaitForEndOfFrame();
                if (ctl.State != FishingController.S.Biting) after++;
                var rt = pv.Target;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var f = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                f.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                f.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, $"actor_hook_{i:00}.png"), f.EncodeToPNG());
                Destroy(f);
                var c = pv.WorldCamera.transform.position;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[ActorStrip] hook {0} state={1} cam=({2:0.0000},{3:0.0000}) pose={4}", i, ctl.State, c.x, c.y, ctl.Angler.Pose));
            }
        }

        IEnumerator Strip(FishingController ctl, string tag) => Strip(ctl, dir, tag, frames, every);

        /// <summary>
        /// A strip of <paramref name="frames"/> crops of the pixel view around the angler's feet (wherever he stands),
        /// <paramref name="every"/> rendered frames apart, into <paramref name="dir"/> (also used by the autopilot).
        /// </summary>
        public static IEnumerator Strip(FishingController ctl, string dir, string tag, int frames, int every)
        {
            var pv = PixelView.Current;
            var a = ctl.Angler;
            if (pv == null || a == null) yield break;
            var P = ctl.Stage.P;
            Texture2D strip = null;
            for (int i = 0; i < frames; i++)
            {
                for (int k = 0; k < (i == 0 ? 1 : every); k++) yield return new WaitForEndOfFrame();
                var rt = pv.Target;
                int w = rt.width, h = rt.height;
                var cam = pv.WorldCamera.transform.position;
                Vector2 Px(Vector3 game)
                {
                    var p2 = P.To2D(game) + ctl.Stage.DeckBob;
                    return new Vector2((p2.x - cam.x) * PixelView.PPU + w * 0.5f, (p2.y - cam.y) * PixelView.PPU + h * 0.5f);
                }
                var feet = Px(a.Feet);
                int x0 = Mathf.Clamp(Mathf.RoundToInt(feet.x) - CropW / 2, 0, w - CropW);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(feet.y) - 14, 0, h - CropH);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var f = new Texture2D(CropW, CropH, TextureFormat.RGB24, false);
                f.ReadPixels(new Rect(x0, y0, CropW, CropH), 0, 0, false);
                f.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, $"actor_{tag}_{i}.png"), f.EncodeToPNG());
                if (strip == null)
                {
                    strip = new Texture2D(frames * (CropW + 2) - 2, CropH, TextureFormat.RGB24, false);
                    var fill = new Color32[strip.width * strip.height];
                    for (int q = 0; q < fill.Length; q++) fill[q] = new Color32(255, 0, 255, 255);
                    strip.SetPixels32(fill);
                }
                strip.SetPixels(i * (CropW + 2), 0, CropW, CropH, f.GetPixels());
                Destroy(f);
                string info = $"[ActorStrip] {tag} {i} t={Time.time:0.000} pose={a.Pose} revs={a.ReelRevs:0.000} x={a.X:0.000} crop=({x0},{y0}) feet={Fmt(feet)} hand={Fmt(Px(a.Hand))}";
                if (a.Model3D != null)
                {
                    var m = a.Model3D;
                    info += $" body={m.BodyYaw:0.0} head={m.HeadYaw:0.0} walk={m.Walk:0.00} steps={m.StepCount}" +
                            $" ankleL=({m.LeftFootPos.x - a.X:0.00},{m.LeftFootPos.y - a.Feet.y:0.00},{m.LeftFootPos.z:0.00})" +
                            $" ankleR=({m.RightFootPos.x - a.X:0.00},{m.RightFootPos.y - a.Feet.y:0.00},{m.RightFootPos.z:0.00})";
                }
                if (a.Reel3D != null) info += $" knob={Fmt(Px(a.Reel3D.Knob))} spool={Fmt(Px(a.Reel3D.Spool))}";
                if (a.Model3D != null)
                    info += $" gripR={Fmt(Px(a.Model3D.RightGripPos))} gripL={Fmt(Px(a.Model3D.LeftGripPos))}" +
                            $" slideR={(a.Model3D.RightAim - (a.Reel3D != null ? a.Reel3D.Knob : a.Model3D.RightAim)).magnitude:0.000}" +
                            $" slideL={(a.Model3D.LeftAim - a.Hand).magnitude:0.000}";
                Debug.Log(info);
            }
            if (strip != null)
            {
                strip.Apply(false);
                File.WriteAllBytes(Path.Combine(dir, $"actor_{tag}_strip.png"), strip.EncodeToPNG());
                Destroy(strip);
            }
        }

        static string Fmt(Vector2 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.0},{1:0.0})", v.x, v.y);

        // ------------------------------------------------------------------ -fkcrank: one crank turn per reeling pose
        const int CrankFrames = 8, Zoom = 5;
        // the 5x strip shows the upper body: this box (pixels) relative to the feet
        const int UpX0 = -40, UpW = 64, UpY0 = 22, UpH = 66;

        // forced every frame after the controller's Update (Forcer): pose + crank angle, and what he faces
        string forcePose, forceFace;
        float forceRevs;

        IEnumerator Forcer(FishingController ctl)
        {
            while (true)
            {
                yield return null;
                var a = ctl != null ? ctl.Angler : null;
                if (a == null) continue;
                if (forcePose != null)
                {
                    a.SetPose(forcePose);
                    a.ReelRevs = forceRevs;
                }
                if (forceFace != null)
                {
                    var L = ctl.Stage.L;
                    var feet = a.Feet;
                    Vector3? t = null, line = null;
                    switch (forceFace)
                    {
                        case "none": break;
                        case "L": t = feet + new Vector3(-20f, 0f, 10f); break;
                        case "R": t = feet + new Vector3(20f, 0f, 10f); break;
                        case "hole":
                            t = L.IsIce ? new Vector3(L.holeX, 0f, L.holeZ) : feet + new Vector3(0f, 0f, 10f);
                            line = t;
                            break;
                        default:
                            var p = forceFace.Split(':');
                            if (p.Length == 2 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float fx) &&
                                float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fz)) t = new Vector3(fx, 0f, fz);
                            break;
                    }
                    a.FaceTarget = t;
                    a.LineTarget = line;
                    a.ShowDangle = line == null;
                }
            }
        }

        IEnumerator Crank(FishingController ctl, string cdir)
        {
            while (ctl.State != FishingController.S.Ready) yield return null;
            StartCoroutine(Forcer(ctl));
            yield return new WaitForSeconds(1.5f);
            var a = ctl.Angler;
            string face = Arg("-fkcrankface");       // the ready shots facing L / R / hole / x:z
            if (!string.IsNullOrEmpty(face))
            {
                forceFace = face;
                yield return new WaitForSeconds(0.8f);
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(cdir, "ready.png"));
            yield return null;
            yield return Strip(ctl, cdir, "ready", 1, 1);
            var poses = (Arg("-fkcrankposes") ?? "reel,fight,idle").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            bool audit = Array.IndexOf(Environment.GetCommandLineArgs(), "-fkholdaudit") >= 0;
            Debug.Log($"[Crank] ready: x={a.X:0.000} range {a.Range.x:0.00}..{a.Range.y:0.00} screen {Screen.width}x{Screen.height} stage {ctl.Stage.Def.id}");
            if (a.Model3D != null)
            {
                var variants = (Arg("-fkarmtune") ?? "").Split(';');
                foreach (var v in variants)
                {
                    string tag = Tune(v);
                    foreach (var pose in poses) if (pose != "none") yield return CrankStrip(ctl, cdir, tag, pose);
                    if (audit) yield return Audit(ctl, cdir, tag);
                }
            }
            else Debug.Log("[Crank] 2D sprites: no crank strips");
            Debug.Log("[Crank] done");
            Application.Quit();
        }

        IEnumerator CrankStrip(FishingController ctl, string cdir, string tag, string pose)
        {
            var a = ctl.Angler;
            var m = a.Model3D;
            // hold the pose with the crank at 0 until the pose switch and the posture blend have settled
            forcePose = pose;
            forceRevs = 0f;
            yield return new WaitForSeconds(0.6f);
            float mn = 999f, mx = 0f, slideMax = 0f;
            Texture2D strip = null, first = null;
            var knobs = new Vector2[CrankFrames];
            Vector2 feet0 = default;
            for (int i = 0; i < CrankFrames; i++)
            {
                forceRevs = i / (float)CrankFrames;
                for (int k = 0; k < 3; k++) yield return null;   // the Forcer applies it after the controller's Update
                yield return new WaitForEndOfFrame();
                var crop = Crop(ctl, out var feetLocal);
                File.WriteAllBytes(Path.Combine(cdir, $"crank_{tag}_{pose}_{i}.png"), crop.EncodeToPNG());
                // upper body, 5x, side by side (2 px magenta gaps at 1x); a 9th tile traces the knob path on frame 0
                int fx = Mathf.RoundToInt(feetLocal.x), fy = Mathf.RoundToInt(feetLocal.y);
                if (strip == null)
                {
                    strip = new Texture2D((CrankFrames + 1) * (UpW + 2) * Zoom - 2 * Zoom, UpH * Zoom, TextureFormat.RGB24, false);
                    var fill = new Color32[strip.width * strip.height];
                    for (int q = 0; q < fill.Length; q++) fill[q] = new Color32(255, 0, 255, 255);
                    strip.SetPixels32(fill);
                }
                strip.SetPixels32(i * (UpW + 2) * Zoom, 0, UpW * Zoom, UpH * Zoom, UpperBody(crop, fx, fy));
                if (i == 0) { first = crop; feet0 = feetLocal; }
                else Destroy(crop);
                float el = m.RightElbowDeg;
                float slide = a.Reel3D != null ? (m.RightAim - a.Reel3D.Knob).magnitude : 0f;
                mn = Mathf.Min(mn, el);
                mx = Mathf.Max(mx, el);
                slideMax = Mathf.Max(slideMax, slide);
                var P = ctl.Stage.P;
                var pv = PixelView.Current;
                var cam = pv.WorldCamera.transform.position;
                Vector2 Px(Vector3 g)
                {
                    var p2 = P.To2D(g) + ctl.Stage.DeckBob;
                    return new Vector2((p2.x - cam.x) * PixelView.PPU + pv.Target.width * 0.5f, (p2.y - cam.y) * PixelView.PPU + pv.Target.height * 0.5f);
                }
                var feetPx = Px(a.Feet);
                if (a.Reel3D != null) knobs[i] = Px(a.Reel3D.Knob) - feetPx;
                string knob = a.Reel3D != null ? Fmt(knobs[i]) : "-";
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[Crank] {0} {1} {2} revs={3:0.000} elbowR={4:0.0} slideR={5:0.000} knob={6} gripR={7} elbowPx={8} gripL={9} hand={10}",
                    tag, pose, i, a.ReelRevs, el, slide, knob, Fmt(Px(m.RightGripPos) - feetPx), Fmt(Px(m.RightElbowPos) - feetPx),
                    Fmt(Px(m.LeftGripPos) - feetPx), Fmt(Px(a.Hand) - feetPx)));
            }
            forcePose = null;
            // the knob path on screen: p(th) = c + A cos th + B sin th over the crank angle (first harmonic of the 8
            // samples); its axes from the eigenvalues of A A^T + B B^T. Angles from vertical, + = the top leans left.
            Vector2 c = Vector2.zero, A = Vector2.zero, B = Vector2.zero;
            for (int i = 0; i < CrankFrames; i++)
            {
                float th = 2f * Mathf.PI * i / CrankFrames;
                c += knobs[i] / CrankFrames;
                A += knobs[i] * (2f * Mathf.Cos(th) / CrankFrames);
                B += knobs[i] * (2f * Mathf.Sin(th) / CrankFrames);
            }
            float a11 = A.x * A.x + B.x * B.x, a22 = A.y * A.y + B.y * B.y, a12 = A.x * A.y + B.x * B.y;
            float tr = a11 + a22, disc = Mathf.Sqrt(Mathf.Max(0f, tr * tr * 0.25f - (a11 * a22 - a12 * a12)));
            float l1 = tr * 0.5f + disc, l2 = Mathf.Max(0f, tr * 0.5f - disc);
            var major = Mathf.Abs(a12) > 1e-6f ? new Vector2(a12, l1 - a11) : (a11 >= a22 ? Vector2.right : Vector2.up);
            if (major.y < 0f) major = -major;
            float axisDeg = Mathf.Atan2(-major.x, major.y) * Mathf.Rad2Deg;
            var rd = a.RodScreenDir;
            float rodDeg = Mathf.Atan2(-rd.x, rd.y) * Mathf.Rad2Deg;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[Crank] {0} {1} ellipse ratio {2:0.00} axis {3:0.0} rod {4:0.0} (off {5:0.0}) major {6:0.0} px minor {7:0.0} px elbowR min {8:0.0} max {9:0.0} slideR max {10:0.000}",
                tag, pose, Mathf.Sqrt(l2 / Mathf.Max(1e-6f, l1)), axisDeg, rodDeg, axisDeg - rodDeg, 2f * Mathf.Sqrt(l1), 2f * Mathf.Sqrt(l2), mn, mx, slideMax));
            if (strip != null && first != null)
            {
                // 9th tile: frame 0 with the knob path traced (fitted ellipse yellow, the 8 knob positions red)
                int fx = Mathf.RoundToInt(feet0.x), fy = Mathf.RoundToInt(feet0.y);
                var tile = UpperBody(first, fx, fy);
                void Dot(Vector2 k, int r, Color32 col)
                {
                    // k: pixels from the feet; tile pixel = (crop px - box origin) * Zoom, pixel centres
                    float px = (feet0.x + k.x - (fx + UpX0)) * Zoom, py = (feet0.y + k.y - (fy + UpY0)) * Zoom;
                    for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = Mathf.RoundToInt(px) + dx, y = Mathf.RoundToInt(py) + dy;
                        if (x >= 0 && y >= 0 && x < UpW * Zoom && y < UpH * Zoom) tile[y * UpW * Zoom + x] = col;
                    }
                }
                for (int s = 0; s < 96; s++)
                {
                    float th = 2f * Mathf.PI * s / 96f;
                    Dot(c + A * Mathf.Cos(th) + B * Mathf.Sin(th), 0, new Color32(255, 230, 0, 255));
                }
                for (int i = 0; i < CrankFrames; i++) Dot(knobs[i], 1, new Color32(255, 40, 40, 255));
                strip.SetPixels32(CrankFrames * (UpW + 2) * Zoom, 0, UpW * Zoom, UpH * Zoom, tile);
                Destroy(first);
            }
            if (strip != null)
            {
                strip.Apply(false);
                File.WriteAllBytes(Path.Combine(cdir, $"crank_{tag}_{pose}_strip.png"), strip.EncodeToPNG());
                Destroy(strip);
            }
        }

        /// <summary>The upper-body box of a 112 x 124 crop (feet at fx, fy), Zoom x.</summary>
        static Color32[] UpperBody(Texture2D crop, int fx, int fy)
        {
            var src = crop.GetPixels32();
            var dst = new Color32[UpW * Zoom * UpH * Zoom];
            for (int y = 0; y < UpH * Zoom; y++)
            for (int x = 0; x < UpW * Zoom; x++)
            {
                int sx = Mathf.Clamp(fx + UpX0 + x / Zoom, 0, CropW - 1), sy = Mathf.Clamp(fy + UpY0 + y / Zoom, 0, CropH - 1);
                dst[y * UpW * Zoom + x] = src[sy * CropW + sx];
            }
            return dst;
        }

        // ------------------------------------------------------------------ -fkholdaudit: rod clear of the hat, elbow
        /// <summary>
        /// For the current hold: at the walk range's left end, 0 and right end, facing straight / far left / far right
        /// (and the ice hole, with the line in it), in idle / reel / fight / aim: the rod's clearance from the hat on
        /// screen (px between the rod's axis and the hat's outline hull, negative = the rod crosses the hat) and the right
        /// elbow; saves the idle and aim crops (audit_&lt;tag&gt;_&lt;end&gt;_&lt;face&gt;_&lt;pose&gt;.png).
        /// </summary>
        IEnumerator Audit(FishingController ctl, string cdir, string tag)
        {
            var a = ctl.Angler;
            var m = a.Model3D;
            string st = ctl.Stage.Def.id;
            float startX = a.X;
            var ends = new[] { ("L", a.Range.x), ("C", Mathf.Clamp(0f, a.Range.x, a.Range.y)), ("R", a.Range.y) };
            var faces = ctl.Stage.L.IsIce ? new[] { "none", "L", "R", "hole" } : new[] { "none", "L", "R" };
            var poses = new[] { "idle", "reel", "fight", "aim" };
            float worst = 999f, worstAim = 999f, elMin = 999f, elMax = 0f;
            string worstAt = "", worstAimAt = "";
            foreach (var (en, x) in ends)
            {
                forceFace = "none";
                forcePose = "idle";
                forceRevs = 0f;
                a.DebugPlace(x);
                yield return new WaitForSeconds(1.0f);
                foreach (var face in faces)
                foreach (var pose in poses)
                {
                    forceFace = face;
                    forcePose = pose;
                    yield return new WaitForSeconds(0.45f);
                    yield return new WaitForEndOfFrame();
                    float clr = Clearance(ctl);
                    float el = pose == "aim" ? -1f : m.RightElbowDeg;
                    if (pose != "aim")
                    {
                        elMin = Mathf.Min(elMin, el);
                        elMax = Mathf.Max(elMax, el);
                        if (clr < worst) { worst = clr; worstAt = $"{en} x={a.X:0.00} face {face} {pose}"; }
                    }
                    else if (clr < worstAim) { worstAim = clr; worstAimAt = $"{en} x={a.X:0.00} face {face}"; }
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Audit] {0} {1} {2} x={3:0.00} face={4} pose={5} clear={6:0.0}px elbowR={7:0.0} body={8:0.0}",
                        tag, st, en, a.X, face, pose, clr, el, m.BodyYaw));
                    if (pose == "idle" || pose == "aim")
                    {
                        var crop = Crop(ctl, out _);
                        File.WriteAllBytes(Path.Combine(cdir, $"audit_{tag}_{en}_{face}_{pose}.png"), crop.EncodeToPNG());
                        Destroy(crop);
                    }
                }
            }
            // back where he started, facing straight (the next variant's crank strips are measured there)
            forceFace = "none";
            forcePose = "idle";
            a.DebugPlace(startX);
            yield return new WaitForSeconds(1.0f);
            forceFace = null;
            forcePose = null;
            a.FaceTarget = null;
            a.LineTarget = null;
            a.ShowDangle = true;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Audit] {0} {1} SUMMARY hold poses clear min {2:0.0}px at {3}; aim clear min {6:0.0}px at {7}; elbowR (idle/reel/fight) {4:0.0}..{5:0.0}",
                tag, st, worst, worstAt, elMin, elMax, worstAim, worstAimAt));
        }

        // ------------------------------------------------------------------ -fkwindup: the rod following the finger
        /// <summary>
        /// The flick wind-up with the simulated pointer (pressed mid-screen, 0.6 H up; mouse-like): armed, then held at each
        /// finger offset of a grid (0 / 0.12 / 0.25 H below the press point and a mid-flick 0.08 H above it, times -0.25 / 0
        /// / +0.25 W to the side): windup_&lt;stage&gt;_&lt;i&gt;.png crops and windup_&lt;stage&gt;_strip.png (+ a 3x copy), per cell the
        /// rod's pitch / lean, drawing order and clearance from the hat. Then a sweep over the whole range (0.1 H above to
        /// 0.3 H below, 0.3 W either side; clearance every frame), a flick straight up from the far right pulled all the way
        /// back (every frame of the flick and the cast swing: windup_&lt;stage&gt;_cast_strip.png) and a wind-up at the far left
        /// let go without a flick (back to the hold: windup_&lt;stage&gt;_cancel_strip.png). Quits when done.
        /// </summary>
        IEnumerator WindUpRun(FishingController ctl, string wdir)
        {
            while (ctl.State != FishingController.S.Ready) yield return null;
            yield return new WaitForSeconds(1.5f);
            var a = ctl.Angler;
            bool is3d = a.Model3D != null;
            string st = ctl.Stage.Def.id + (is3d ? "" : "_2d");
            float W = Screen.width, H = Screen.height;
            var press = new Vector2(W * 0.5f, H * 0.6f);
            Vector2 At(float lat, float down) => press + new Vector2(lat * W, -down * H);
            float Clear() => is3d ? Clearance(ctl) : 999f;
            string Line(string tag, int i, float clr)
            {
                var w = a.WindUp ?? new Vector2(float.NaN, float.NaN);
                string s = FormattableString.Invariant(
                    $"[Windup] {st} {tag} {i} state={ctl.State} pose={a.Pose} finger=({w.x:+0.000;-0.000;0.000},{w.y:+0.000;-0.000;0.000}) pitch={a.RodAngles.x:+0.0;-0.0;0.0} lean={a.RodAngles.y:+0.0;-0.0;0.0} front={(a.RodFront ? 1 : 0)} clear={(is3d ? clr.ToString("0.0", CultureInfo.InvariantCulture) + "px" : "-")}");
                if (is3d)
                {
                    var m = a.Model3D;
                    s += FormattableString.Invariant($" slideL={(m.LeftAim - a.Hand).magnitude:0.000} gripL-hand={(m.LeftGripPos - a.Hand).magnitude:0.000} body={m.BodyYaw:0.0}");
                }
                return s;
            }
            float minAll = 999f;
            string minAllAt = "";
            void Track(float c, string where)
            {
                if (c < minAll) { minAll = c; minAllAt = where; }
            }

            PointerInput.SimDpi = 0f;
            PointerInput.SimActive = true;
            PointerInput.SimPos = press;
            PointerInput.SimDown = true;
            yield return null;
            yield return null;
            var cur = At(0f, 0.15f);
            yield return Glide(press, cur, 0.2f);     // armed: the aim pose

            // the grid
            var tiles = new List<Texture2D>();
            float gridMin = 999f;
            string gridMinAt = "";
            int idx = 0;
            foreach (float d in new[] { 0f, 0.12f, 0.25f, -0.08f })
            foreach (float l in new[] { -0.25f, 0f, 0.25f })
            {
                var to = At(l, d);
                yield return Glide(cur, to, 0.25f);
                cur = to;
                yield return new WaitForSeconds(0.35f);
                yield return new WaitForEndOfFrame();
                float c = Clear();
                string at = FormattableString.Invariant($"down={d:+0.00;-0.00;0.00}H lat={l:+0.00;-0.00;0.00}W pitch={a.RodAngles.x:0.0} lean={a.RodAngles.y:0.0}");
                if (c < gridMin) { gridMin = c; gridMinAt = at; }
                Track(c, "grid " + at);
                var crop = Crop(ctl, out _);
                File.WriteAllBytes(Path.Combine(wdir, $"windup_{st}_{idx}.png"), crop.EncodeToPNG());
                tiles.Add(crop);
                Debug.Log(Line(FormattableString.Invariant($"grid down={d:+0.00;-0.00;0.00} lat={l:+0.00;-0.00;0.00}"), idx, c));
                idx++;
            }
            SaveStrip(tiles, Path.Combine(wdir, $"windup_{st}_strip"));

            // the sweep: every frame over the whole range, standing here and at both ends of the walk range (the
            // perspective shifts the rod against the hat)
            float sweepMin = 999f, x0 = a.X;
            foreach (float x in new[] { x0, a.Range.x, a.Range.y })
            {
                a.DebugPlace(x);
                yield return new WaitForSeconds(0.3f);
                float endMin = 999f;
                string endMinAt = "";
                for (int row = 0; row <= 8; row++)
                {
                    float d = -0.10f + row * 0.05f;
                    var from = At(-0.3f, d);
                    yield return Glide(cur, from, 0.15f);
                    var to = At(0.3f, d);
                    for (float t = 0f; t < 1f;)
                    {
                        t = Mathf.Min(1f, t + Time.deltaTime / 0.7f);
                        PointerInput.SimPos = Vector2.Lerp(from, to, t);
                        yield return new WaitForEndOfFrame();
                        float c = Clear();
                        if (c < endMin)
                        {
                            var w = a.WindUp ?? Vector2.zero;
                            endMin = c;
                            endMinAt = FormattableString.Invariant($"x={a.X:0.00} finger=({w.x:+0.000;-0.000},{w.y:+0.000;-0.000}) pitch={a.RodAngles.x:0.0} lean={a.RodAngles.y:0.0}");
                        }
                    }
                    cur = to;
                }
                sweepMin = Mathf.Min(sweepMin, endMin);
                Track(endMin, "sweep " + endMinAt);
                Debug.Log(FormattableString.Invariant($"[Windup] {st} sweep x={a.X:0.00} clear min {endMin:0.0}px at {endMinAt}"));
            }
            a.DebugPlace(x0);
            yield return new WaitForSeconds(0.3f);

            // the cast swing: pulled all the way back at the far right, flicked straight up at 3 H/s over 0.3 H, let go
            // while still moving; every frame until 0.3 s after the release
            var back = At(0.25f, 0.25f);
            yield return Glide(cur, back, 0.25f);
            yield return new WaitForSeconds(0.4f);
            yield return new WaitForEndOfFrame();
            tiles = new List<Texture2D>();
            float swingMin = Clear();
            tiles.Add(Crop(ctl, out _));
            Debug.Log(Line("cast", 0, swingMin));
            float t0 = Time.unscaledTime, tRel = -1f;
            for (int f = 1; f < 60; f++)
            {
                if (tRel < 0f)
                {
                    float s = 3f * H * (Time.unscaledTime - t0 + 1f / 60f);
                    bool last = s >= 0.3f * H;
                    PointerInput.SimPos = back + Vector2.up * (last ? 0.3f * H : s);
                    if (last)
                    {
                        PointerInput.SimDown = false;   // lifts on the frame of the last move
                        tRel = Time.unscaledTime;
                    }
                }
                else if (Time.unscaledTime - tRel > 0.3f) break;
                yield return new WaitForEndOfFrame();
                float c = Clear();
                swingMin = Mathf.Min(swingMin, c);
                tiles.Add(Crop(ctl, out _));
                Debug.Log(Line(tRel < 0f ? "cast" : "cast+" + (Time.unscaledTime - tRel).ToString("0.000", CultureInfo.InvariantCulture), f, c));
            }
            SaveStrip(tiles, Path.Combine(wdir, $"windup_{st}_cast_strip"));
            Track(swingMin, "cast swing");
            Debug.Log(FormattableString.Invariant($"[Windup] {st} cast swing clear min {swingMin:0.0}px; flick: {FlickCast.Describe(ctl.LastFlick)}"));

            // back to the ready (wind the rig in), then a wind-up let go without a flick: back to the hold
            PointerInput.SimDown = false;
            for (float w = 0f; w < 40f && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
            {
                if (ctl.State == FishingController.S.Waiting || ctl.State == FishingController.S.Snagged) ctl.Retrieve();   // (snagged: 끊기)
                yield return null;
            }
            float cancelMin = 999f;
            if (ctl.State == FishingController.S.Ready)
            {
                yield return new WaitForSeconds(0.8f);
                PointerInput.SimPos = press;
                PointerInput.SimDown = true;
                yield return null;
                yield return null;
                var left = At(-0.25f, 0.2f);
                yield return Glide(press, left, 0.25f);
                yield return new WaitForSeconds(0.4f);
                tiles = new List<Texture2D>();
                PointerInput.SimDown = false;
                for (int f = 0; f < 12; f++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    float c = Clear();
                    cancelMin = Mathf.Min(cancelMin, c);
                    tiles.Add(Crop(ctl, out _));
                    Debug.Log(Line("cancel", f, c));
                }
                SaveStrip(tiles, Path.Combine(wdir, $"windup_{st}_cancel_strip"));
                Track(cancelMin, "cancel");
            }
            else Debug.Log("[Windup] not back to ready: no cancel test");
            PointerInput.SimActive = false;
            Debug.Log(FormattableString.Invariant(
                $"[Windup] {st} SUMMARY clear min {minAll:0.0}px at {minAllAt}; grid {gridMin:0.0}px at {gridMinAt}; sweep {sweepMin:0.0}px; cast swing {swingMin:0.0}px; cancel {cancelMin:0.0}px; screen {Screen.width}x{Screen.height} rod {Game.I.Rod.id}"));
            Debug.Log("[Windup] done");
            Application.Quit();
        }

        static IEnumerator Glide(Vector2 from, Vector2 to, float time)
        {
            PointerInput.SimActive = true;
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / time);
                PointerInput.SimPos = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                PointerInput.SimDown = true;
                yield return null;
            }
        }

        /// <summary>Crops side by side (2 px magenta gaps) as &lt;path&gt;.png and a 3x copy &lt;path&gt;_x3.png; destroys the crops.</summary>
        static void SaveStrip(List<Texture2D> tiles, string path)
        {
            if (tiles.Count == 0) return;
            int w = tiles.Count * (CropW + 2) - 2;
            var strip = new Texture2D(w, CropH, TextureFormat.RGB24, false);
            var fill = new Color32[w * CropH];
            for (int q = 0; q < fill.Length; q++) fill[q] = new Color32(255, 0, 255, 255);
            strip.SetPixels32(fill);
            for (int i = 0; i < tiles.Count; i++)
            {
                strip.SetPixels(i * (CropW + 2), 0, CropW, CropH, tiles[i].GetPixels());
                Destroy(tiles[i]);
            }
            strip.Apply(false);
            File.WriteAllBytes(path + ".png", strip.EncodeToPNG());
            const int Z = 3;
            var src = strip.GetPixels32();
            var big = new Texture2D(w * Z, CropH * Z, TextureFormat.RGB24, false);
            var dst = new Color32[w * Z * CropH * Z];
            for (int y = 0; y < CropH * Z; y++)
            for (int x = 0; x < w * Z; x++)
                dst[y * w * Z + x] = src[(y / Z) * w + x / Z];
            big.SetPixels32(dst);
            big.Apply(false);
            File.WriteAllBytes(path + "_x3.png", big.EncodeToPNG());
            Destroy(strip);
            Destroy(big);
        }

        /// <summary>
        /// Screen px between the rod's axis and the hat (brim ring + crown ring hull); negative inside. The pose sprites
        /// (no 3D head): from the sprite hat's middle (<see cref="Angler.HatPos2D"/>) minus a <see cref="SpriteHatR"/> px
        /// radius.
        /// </summary>
        internal static float Clearance(FishingController ctl)
        {
            var a = ctl.Angler;
            var P = ctl.Stage.P;
            var bob = ctl.Stage.DeckBob;
            if (a.Model3D == null)
            {
                var hat = a.HatPos2D * PixelView.PPU;
                var g2 = new Angler.HatGapAcc();
                foreach (var q in a.RodPaint.Axis) g2.Take((q - hat).magnitude - SpriteHatR);
                return g2.Result(a.RodCentre);
            }
            var head = a.Model3D.HeadPos;
            var pts = new List<Vector2>();
            for (int i = 0; i < 16; i++)
            {
                float t = i * Mathf.PI * 2f / 16f;
                var o = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                pts.Add((P.To2D(head + Vector3.up * 0.05f + o * 0.19f) + bob) * PixelView.PPU);
                pts.Add((P.To2D(head + Vector3.up * 0.22f + o * 0.11f) + bob) * PixelView.PPU);
            }
            var hull = Hull(pts);
            // (held in the middle: a rod crossing the hat counts by its deepest point inside, Angler.HatGapAcc)
            var g = new Angler.HatGapAcc();
            foreach (var q in a.RodPaint.Axis) g.Take(SignedDist(hull, q));
            if (Detail != null)
            {
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (var h in hull) { minX = Mathf.Min(minX, h.x); maxX = Mathf.Max(maxX, h.x); minY = Mathf.Min(minY, h.y); maxY = Mathf.Max(maxY, h.y); }
                var ax = a.RodPaint.Axis;
                float inMinY = float.MaxValue, inMaxY = float.MinValue, near = 999f;
                Vector2 nearAt = default;
                foreach (var q in ax)
                {
                    float d = SignedDist(hull, q);
                    if (d < 0f) { inMinY = Mathf.Min(inMinY, q.y); inMaxY = Mathf.Max(inMaxY, q.y); }
                    if (Mathf.Abs(d) < Mathf.Abs(near)) { near = d; nearAt = q; }
                }
                Detail = string.Format(CultureInfo.InvariantCulture, "hat x {0:0.0}..{1:0.0} y {2:0.0}..{3:0.0}; rod {4} .. {5} ({6} pts), inside y {7:0.0}..{8:0.0}, nearest edge {9:+0.0;-0.0} at {10}; hand {11}",
                    minX, maxX, minY, maxY, Fmt(ax[0]), Fmt(ax[ax.Count - 1]), ax.Count, inMinY, inMaxY, near, Fmt(nearAt), Fmt((P.To2D(a.Hand) + bob) * PixelView.PPU));
            }
            return g.Result(a.RodCentre);
        }

        /// <summary>Set non-null to have <see cref="Clearance"/> describe the hat and the rod on screen (px) in it.</summary>
        internal static string Detail;

        const float SpriteHatR = 8f;   // px: the pose sprites' hat, brim to brim ~16 px

        static List<Vector2> Hull(List<Vector2> p)
        {
            p.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            var h = new List<Vector2>();
            float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    var v = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], v) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(v);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;   // counter-clockwise
        }

        static float SignedDist(List<Vector2> hull, Vector2 q)
        {
            bool inside = true;
            float d = float.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                var u = hull[i];
                var v = hull[(i + 1) % hull.Count];
                var e = v - u;
                if (e.x * (q.y - u.y) - e.y * (q.x - u.x) < 0f) inside = false;
                float t = Mathf.Clamp01(Vector2.Dot(q - u, e) / Mathf.Max(1e-6f, e.sqrMagnitude));
                d = Mathf.Min(d, (u + e * t - q).magnitude);
            }
            return inside ? -d : d;
        }

        /// <summary>The 112 x 124 pixel-view crop around the angler's feet (as <see cref="Strip"/>), feet position in it.</summary>
        static Texture2D Crop(FishingController ctl, out Vector2 feetLocal)
        {
            var pv = PixelView.Current;
            var a = ctl.Angler;
            var P = ctl.Stage.P;
            var rt = pv.Target;
            int w = rt.width, h = rt.height;
            var cam = pv.WorldCamera.transform.position;
            var p2 = P.To2D(a.Feet) + ctl.Stage.DeckBob;
            var feet = new Vector2((p2.x - cam.x) * PixelView.PPU + w * 0.5f, (p2.y - cam.y) * PixelView.PPU + h * 0.5f);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(feet.x) - CropW / 2, 0, w - CropW);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(feet.y) - 14, 0, h - CropH);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var f = new Texture2D(CropW, CropH, TextureFormat.RGB24, false);
            f.ReadPixels(new Rect(x0, y0, CropW, CropH), 0, 0, false);
            f.Apply(false);
            RenderTexture.active = prev;
            feetLocal = feet - new Vector2(x0, y0);
            return f;
        }

        /// <summary>Applies one -fkarmtune variant (key=value,...) to the reeling hold; returns its tag.</summary>
        static string Tune(string spec)
        {
            string tag = "base";
            foreach (var kv in spec.Split(','))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                string k = kv.Substring(0, eq).Trim().ToLowerInvariant(), v = kv.Substring(eq + 1).Trim();
                float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                Vector3 V3(string s)
                {
                    var p = s.Split(':');
                    return new Vector3(F(p[0]), F(p[1]), F(p[2]));
                }
                Vector3? H(string s) => s == "none" ? (Vector3?)null : V3(s);
                switch (k)
                {
                    case "name": tag = v; break;
                    case "h": Angler.HoldIdle = Angler.HoldReel = Angler.HoldFight = H(v); break;
                    case "hi": Angler.HoldIdle = H(v); break;
                    case "hr": Angler.HoldReel = H(v); break;
                    case "hf": Angler.HoldFight = H(v); break;
                    case "reels": Angler.ReelS3D = F(v); break;
                    case "lean": Angler.HoldRodLean = F(v); break;
                    case "scale": Angler.ReelScale = F(v); break;
                    case "roll": Angler.ReelRoll = F(v); break;
                    case "turn": Angler3D.HoldTurn = F(v); break;
                    case "rsh": Angler3D.HoldRsh = V3(v); break;
                    case "poler": Angler3D.HoldPoleR = V3(v); break;
                    case "polel": Angler3D.HoldPoleL = V3(v); break;
                    // the middle hold (Angler.RodCentre): the rod hand, the cranking shoulder, the elbow poles
                    case "hc": Angler.HoldCentre = V3(v); break;
                    case "crsh": Angler3D.CentreRsh = V3(v); break;
                    case "cpoler": Angler3D.CentrePoleR = V3(v); break;
                    case "cpolel": Angler3D.CentrePoleL = V3(v); break;
                    default: Debug.Log("[Crank] unknown tune key " + k); break;
                }
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Crank] variant {0}: reelS {10} hold idle {1} reel {2} fight {3} scale {4} roll {5} lean {11} turn {6} rsh {7} poleR {8} poleL {9}",
                tag, Angler.HoldIdle, Angler.HoldReel, Angler.HoldFight, Angler.ReelScale, Angler.ReelRoll, Angler3D.HoldTurn,
                Angler3D.HoldRsh.ToString("F3"), Angler3D.HoldPoleR.ToString("F2"), Angler3D.HoldPoleL.ToString("F2"), Angler.ReelS3D, Angler.HoldRodLean));
            return tag;
        }
    }
}
