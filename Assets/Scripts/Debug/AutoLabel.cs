using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto only (never in normal play; -fknolabel turns it off): a line of pixel text at the bottom centre of the screen
    /// saying which test is running — "[&lt;scenario&gt; · &lt;stage&gt;] &lt;current step&gt; &lt;checks&gt;/&lt;total&gt;" — with the
    /// last CHECK result above it for a moment, red once any CHECK failed (with the failed count).
    /// The step and the checks come from the autopilots' own log lines ([AUTO], [PAN], [ZOOM] ... and every "CHECK" line),
    /// read through <see cref="Application.logMessageReceived"/>, so no scenario has to call it; <see cref="Step"/> /
    /// <see cref="Total"/> may set them explicitly. The total is the number of CHECK lines the last finished run of the same
    /// scenario logged (persistentDataPath/autolabel_checks.txt), or what <see cref="Total"/> says.
    /// It lives on its own screen-space overlay canvas above the HUD (no raycaster), so zoom / pan never move it.
    /// RULE: it must never appear in a capture — every screen capture / pixel read of the tests goes through
    /// <see cref="AutoShot"/> (see there).
    /// </summary>
    public class AutoLabel : MonoBehaviour
    {
        const int Order = 32000;         // above every HUD / popup canvas
        const float MaxWidth = 400f;     // canvas units: clear of the tackle panel (left) and the retrieve / walk buttons (right)
        const float CheckShow = 2.5f;    // s the last CHECK result stays up

        static AutoLabel I;
        static int hideUntil = -1;       // hidden for the frames up to and including this one (a capture)
        static int shownFrame = -1;      // the last frame the label was drawn in

        Canvas canvas;
        Text main, check;
        string scenario, stage = "-", step = "", lastCheck;
        bool lastOk;
        float lastCheckAt = -99f, nextStage;
        int checks, fails, total = -1, explicitTotal = -1;
        string shownMain, shownCheck;
        bool dirty = true;

        /// <summary>True when the label exists (-fkauto without -fknolabel).</summary>
        public static bool On => I != null;

        /// <summary>The label was drawn in this frame's render (a screen capture now would contain it).</summary>
        public static bool DrawnThisFrame => I != null && shownFrame == Time.frameCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fkauto");
            if (i < 0 || Array.IndexOf(args, "-fknolabel") >= 0 || Application.isBatchMode) return;
            var go = new GameObject("[AutoLabel]", typeof(RectTransform));
            DontDestroyOnLoad(go);
            var l = go.AddComponent<AutoLabel>();
            l.scenario = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "fish";
        }

        /// <summary>Sets the current step explicitly (a scenario's phase); log lines replace it as they come.</summary>
        public static void Step(string s)
        {
            if (I == null) return;
            I.step = s ?? "";
            I.dirty = true;
        }

        /// <summary>Sets the number of checks this run will make (n/total), when a scenario knows it.</summary>
        public static void Total(int n)
        {
            if (I == null) return;
            I.explicitTotal = n;
            I.dirty = true;
        }

        /// <summary>Hides the label for the render of this frame and the next (a capture coming up). See <see cref="AutoShot"/>.</summary>
        public static void HideForCapture()
        {
            if (I == null) return;
            hideUntil = Mathf.Max(hideUntil, Time.frameCount + 1);
            if (I.canvas != null) I.canvas.enabled = false;
        }

        void Awake()
        {
            I = this;
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = Order;
            canvas.pixelPerfect = true;
            var cs = gameObject.AddComponent<CanvasScaler>();
            cs.referencePixelsPerUnit = PixelView.PPU;
            gameObject.AddComponent<PixelCanvasScaler>();
            var root = (RectTransform)transform;
            check = Line(root, "Check", 22f);
            main = Line(root, "Main", 2f);
            total = LoadTotal(scenario ?? ArgScenario());
            Application.logMessageReceived += OnLog;
        }

        static string ArgScenario()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fkauto");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : "fish";
        }

        static Text Line(RectTransform root, string name, float y)
        {
            var t = UIKit.Label(root, "", 16, UIKit.Cream, TextAnchor.LowerCenter, true, name);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.supportRichText = false;
            var o = t.GetComponent<PixelOutline>();
            if (o != null) o.shadowOnly = false;   // a full outline: readable over bright water as well as the dark HUD
            t.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(MaxWidth, 20f), new Vector2(0.5f, 0f));
            return t;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (I == this) I = null;
        }

        void OnApplicationQuit()
        {
            // remembered as the next run's total (only a run that made checks)
            if (checks > 0) SaveTotal(scenario, checks);
        }

        static readonly HashSet<string> StepTags = new HashSet<string>
        {
            "AUTO", "PAN", "ZOOM", "BREAK", "OBST", "OCC", "HOLD", "SPOT", "CUR", "TIDE", "PANM", "AQUA", "SIDE", "CAP",
        };

        void OnLog(string msg, string trace, LogType type)
        {
            if (string.IsNullOrEmpty(msg) || msg[0] != '[') return;
            string first = null, rest = msg;
            while (rest.Length > 0 && rest[0] == '[')
            {
                int e = rest.IndexOf("] ", StringComparison.Ordinal);
                if (e < 0) break;
                first ??= rest.Substring(1, e - 1);
                rest = rest.Substring(e + 2).TrimStart();
            }
            if (first == null || first == "LABEL") return;
            int nl = rest.IndexOf('\n');
            if (nl >= 0) rest = rest.Substring(0, nl);
            if (rest.StartsWith("CHECK ", StringComparison.Ordinal))
            {
                // "CHECK PASS|FAIL what", "CHECK ok|FAIL what" (aquarium), "CHECK name PASS|FAIL numbers"
                var p = rest.Substring(6).Trim().Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) return;
                string verdict, name;
                if (IsVerdict(p[0])) { verdict = p[0]; name = string.Join(" ", p, 1, p.Length - 1); }
                else { verdict = p.Length > 1 ? p[1] : "?"; name = p[0]; }
                lastOk = verdict != "FAIL";
                checks++;
                if (!lastOk) fails++;
                lastCheck = name.Trim();
                lastCheckAt = Time.unscaledTime;
                dirty = true;
                return;
            }
            if (!StepTags.Contains(first)) return;
            if (rest.StartsWith("shot ", StringComparison.Ordinal)) rest = "shot " + Path.GetFileName(rest.Substring(5).Trim());
            step = rest;
            dirty = true;
        }

        static bool IsVerdict(string s) => s == "PASS" || s == "FAIL" || s == "ok";

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextStage)
            {
                nextStage = Time.unscaledTime + 0.5f;
                var ctl = FindAnyObjectByType<FishingController>();
                string st = ctl != null && ctl.Stage != null && ctl.Stage.Def != null ? ctl.Stage.Def.id : SceneManager.GetActiveScene().name;
                if (st != stage)
                {
                    stage = st;
                    dirty = true;
                }
            }
            bool checkUp = lastCheck != null && Time.unscaledTime - lastCheckAt < CheckShow;
            if (dirty || checkUp != (shownCheck != null && shownCheck.Length > 0))
            {
                dirty = false;
                Refresh(checkUp);
            }
            bool show = Time.frameCount > hideUntil;
            if (canvas.enabled != show) canvas.enabled = show;
            if (show) shownFrame = Time.frameCount;
        }

        void Refresh(bool checkUp)
        {
            int tot = explicitTotal >= 0 ? explicitTotal : total;
            string count = checks == 0 && tot <= 0 ? "" : tot > 0 && checks <= tot ? $"  {checks}/{tot}" : $"  {checks}";
            string tail = count + (fails > 0 ? $"  FAIL {fails}" : "");
            string head = $"[{scenario} · {stage}] ";
            main.color = fails > 0 ? UIKit.Bad : UIKit.Cream;
            main.text = shownMain = Fit(main, head, Clean(step), tail);
            check.text = shownCheck = checkUp ? Fit(check, lastOk ? "PASS " : "FAIL ", Clean(lastCheck), "") : "";
            check.color = lastOk ? UIKit.Good : UIKit.Bad;
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ');

        /// <summary>head + body + tail on one line no wider than <see cref="MaxWidth"/>, the body cut short with "..".</summary>
        static string Fit(Text t, string head, string body, string tail)
        {
            string s = head + body + tail;
            t.text = s;
            if (t.preferredWidth <= MaxWidth || body.Length == 0) return s;
            int lo = 0, hi = body.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                t.text = head + body.Substring(0, mid) + ".." + tail;
                if (t.preferredWidth <= MaxWidth) lo = mid;
                else hi = mid - 1;
            }
            return head + body.Substring(0, lo) + ".." + tail;
        }

        // ------------------------------------------------------------------ the remembered totals
        static string TotalsPath => Path.Combine(Application.persistentDataPath, "autolabel_checks.txt");

        static int LoadTotal(string scen)
        {
            try
            {
                if (!File.Exists(TotalsPath)) return -1;
                foreach (var line in File.ReadAllLines(TotalsPath))
                {
                    var p = line.Split('\t');
                    if (p.Length == 2 && p[0] == scen && int.TryParse(p[1], out int n)) return n;
                }
            }
            catch (Exception) { }
            return -1;
        }

        static void SaveTotal(string scen, int n)
        {
            try
            {
                var lines = new List<string>();
                if (File.Exists(TotalsPath))
                    foreach (var line in File.ReadAllLines(TotalsPath))
                        if (!line.StartsWith(scen + "\t", StringComparison.Ordinal)) lines.Add(line);
                lines.Add(scen + "\t" + n);
                File.WriteAllLines(TotalsPath, lines);
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// THE capture guard of the tests: the test label (<see cref="AutoLabel"/>) must never be in a screenshot or a pixel
    /// comparison. Every screen capture / pixel read in Assets/Scripts/Debug goes through here:
    /// <list type="bullet">
    /// <item><c>yield return AutoShot.Frame();</c> instead of <c>new WaitForEndOfFrame()</c> before reading the screen: hides
    /// the label for this frame's render and the next (call it before the frame is drawn, i.e. not only at its end), then
    /// waits for the end of the frame. Loops that capture at the end of a frame they already waited for use it for that wait.</item>
    /// <item><see cref="Save"/> / <see cref="Texture"/> instead of ScreenCapture.CaptureScreenshot / CaptureScreenshotAsTexture.</item>
    /// <item><see cref="Read"/> instead of Texture2D.ReadPixels (a render target never has the label: the overlay canvas only
    /// draws to the screen; a read of the screen needs <see cref="Frame"/> first like a capture).</item>
    /// </list>
    /// A capture of a frame the label was drawn in logs "[LABEL] ERROR" (a new capture path that skipped <see cref="Frame"/>).
    /// </summary>
    public static class AutoShot
    {
        static readonly WaitForEndOfFrame Eof = new WaitForEndOfFrame();
        static int warned;

        /// <summary>Hides the test label for the coming render, then waits for the end of the frame.</summary>
        public static WaitForEndOfFrame Frame()
        {
            AutoLabel.HideForCapture();
            return Eof;
        }

        /// <summary>ScreenCapture.CaptureScreenshot (written at the end of this frame or the next: both stay clean).</summary>
        public static void Save(string path)
        {
            Guard("CaptureScreenshot " + Path.GetFileName(path));
            AutoLabel.HideForCapture();
            ScreenCapture.CaptureScreenshot(path);
        }

        /// <summary>ScreenCapture.CaptureScreenshotAsTexture of this frame.</summary>
        public static Texture2D Texture()
        {
            Guard("CaptureScreenshotAsTexture");
            return ScreenCapture.CaptureScreenshotAsTexture();
        }

        /// <summary>Texture2D.ReadPixels from <see cref="RenderTexture.active"/> (null = the screen).</summary>
        public static void Read(Texture2D into, Rect r, int x = 0, int y = 0)
        {
            if (RenderTexture.active == null) Guard("ReadPixels (screen)");
            into.ReadPixels(r, x, y, false);
        }

        static void Guard(string what)
        {
            if (!AutoLabel.DrawnThisFrame || warned >= 20) return;
            warned++;
            Debug.LogError("[LABEL] ERROR " + what + " on a frame with the test label drawn: wait with AutoShot.Frame() first");
        }
    }
}
