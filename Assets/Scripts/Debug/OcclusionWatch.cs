using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The per-frame occlusion detector (the tests: -fkauto occlusion runs it through every stage; any other autopilot run
    /// with -fkoccwatch). At the end of every frame it rebuilds on the CPU, pixel by pixel, what that frame drew over the
    /// stage's front layer (<see cref="FrontOcclusion"/>): the line above the water, the dangling bait, the fish in the air
    /// or landed, the float and the bait over the front layer, the spray.
    /// <para>What it counts, per frame (a frame is "exposed" when any of them is):</para>
    /// <para>- depth: a pixel the frame showed (the renderer's material, its depth, where the map was placed, hiding on) over
    /// a solid front texel that is nearer than the thing drawn there really is (its depth from the game state: the line's
    /// curve, the fish's point in the air; the rest as given). The resting contact is allowed: the line's end within 0.5 m
    /// of the perched bait. The truth takes the front layer's rect from where the front sprite is actually drawn.</para>
    /// <para>- transient: the line or the fish shown over the front layer for one or two frames only (its count of pixels
    /// over solid front texels jumps up for 1-2 frames and back: at least 6 px and half of it), unless it is just moving
    /// along (drawn in every frame and moving 2+ px a frame without coming back: the cast's line swinging out, a fish
    /// passing an edge). A flip goes there and back; a depth or state flicker does not move at all. The line is taken along
    /// its curve without the strain shiver (a deliberate +-1.6 px wobble), so only a real jump of the line counts.</para>
    /// <para>Also logged (not exposures): the rod tip jumping 4+ px on screen for 1-2 frames and back, suddenly (from rest or
    /// against its last step, within one state: the rod bowing and straightening over a few frames is not one, nor the rod
    /// moving as the landing, the cast or the hook set begins) (tip flips), the hat
    /// guard's tilt changing side (hat side flips) and state errors (no map bound, the map placed off the front layer, a
    /// renderer without the occluding material).</para>
    /// <para>[OCCW] EXPOSED lines per flagged frame (the first 300), [OCCW] SUMMARY per stage and window, occwatch.csv (one
    /// row per watched frame) and occw_*.png strips (the frames around a flagged one, 2x crops) in the shots folder;
    /// -fkoccwatchsnap t1,t2,...: the three frames up to each of these game times, whole (occw_at_*.png: the same moments
    /// of another build, for before / after comparisons).</para>
    /// </summary>
    public class OcclusionWatch : MonoBehaviour
    {
        public static OcclusionWatch I { get; private set; }

        /// <summary>Counting stops while set (hiding switched off on purpose); the history starts over afterwards.</summary>
        public bool Paused;

        const int SpikePx = 6;        // a transient: at least this many pixels over the front layer ...
        const float SpikeShare = 0.5f; // ... and this share of them gone again in the frames either side
        const float TipFlipPx = 4f;    // a tip flip: the rod tip jumps this far and comes back
        const float RestReach = 0.5f;  // m around the perched bait the line may be tested nearer (it lies on the prop)
        const int CropW = 120, CropH = 90, MaxShots = 12, MaxLines = 300;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static readonly int DepthId = Shader.PropertyToID("_OccDepth");

        public class Tally
        {
            public int frames, exposed, depthFrames, depthPx, transFrames, transEvents, tipFlips, hatFlips, stateFrames, maxLine, maxFish;
            public string firstState = "";

            public override string ToString() => string.Format(CI,
                "frames {0} exposed {1} (depth {2} frames / {3} px, transient {4} frames in {5} events) state {6}{7} tipFlips {8} hatSideFlips {9} maxOver line {10} fish {11}",
                frames, exposed, depthFrames, depthPx, transFrames, transEvents, stateFrames, firstState.Length > 0 ? " (" + firstState + ")" : "", tipFlips, hatFlips, maxLine, maxFish);
        }

        class Rec
        {
            public int frame;
            public float time;
            public string stage, window, state;
            public int lineOver, fishOver, e1Line, e1Fish, e1Other;
            public Vector2 lineAt, fishAt, e1At;
            public Vector2 tip;
            public float hat;
            public string stateErr = "";
            public int flags;          // 1 depth, 2 transient
            public string why = "";
            public bool tipFlip, hatFlip, evStart;
            // where the line (its curve's start, middle, end) and the fish in the air are drawn (render-target px): a count
            // going up and back down while these move steadily on is something moving along, not a flash
            public bool lineOn, fishOn;
            public Vector2 l0, l1, l2, f0;
            // (for the tip flips' log line)
            public float tension, jumpT = -1f;
            public Vector3 fishPos, target;
        }

        readonly Dictionary<string, Tally> tallies = new Dictionary<string, Tally>();
        readonly List<string> order = new List<string>();
        readonly List<Rec> hist = new List<Rec>();
        string window;
        string dir;
        bool shotsOn;
        int shotsSaved, linesLogged;
        StreamWriter csv;
        readonly HashSet<string> everBound = new HashSet<string>();

        // the last frames drawn (for the strips around a flagged frame)
        readonly RenderTexture[] ring = new RenderTexture[4];
        readonly int[] ringFrame = { -1, -1, -1, -1 };

        FishingController ctl;
        MaterialPropertyBlock blk;
        readonly List<SpriteRenderer> fxList = new List<SpriteRenderer>();
        readonly HashSet<int> setA = new HashSet<int>(), setB = new HashSet<int>(), setC = new HashSet<int>();
        Vector3[] pos = new Vector3[64];
        float[] trueD = new float[64];
        readonly Dictionary<Texture2D, Color32[]> texCache = new Dictionary<Texture2D, Color32[]>();

        /// <summary>The watch (created on first use, kept across scenes). <paramref name="shots"/>: save strips of flagged frames.</summary>
        public static OcclusionWatch Ensure(string shotsDir, bool shots)
        {
            if (I != null) return I;
            var go = new GameObject("[OcclusionWatch]");
            DontDestroyOnLoad(go);
            I = go.AddComponent<OcclusionWatch>();
            I.dir = shotsDir;
            I.shotsOn = shots;
            try
            {
                if (!string.IsNullOrEmpty(shotsDir))
                {
                    Directory.CreateDirectory(shotsDir);
                    I.csv = new StreamWriter(Path.Combine(shotsDir, "occwatch.csv"), false, new UTF8Encoding(false));
                    I.csv.WriteLine("frame,time,stage,window,state,lineOver,fishOver,depthLine,depthFish,depthOther,flags,tipX,tipY,hatTilt,stateErr,why");
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[OCCW] no csv: " + e.Message); }
            Debug.Log("[OCCW] per-frame occlusion watch on");
            return I;
        }

        void Awake()
        {
            blk = new MaterialPropertyBlock();
            // -fkoccwatchsnap t1,t2,...: the three frames drawn up to each of these game times (s), whole (the same moments
            // of another build, for a before / after comparison)
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-fkoccwatchsnap");
            if (i >= 0 && i + 1 < a.Length)
                foreach (var s in a[i + 1].Split(','))
                    if (float.TryParse(s, NumberStyles.Float, CI, out float t)) snapAt.Add(t);
            snapAt.Sort();
        }

        readonly List<float> snapAt = new List<float>();

        void Start() => StartCoroutine(Loop());

        IEnumerator Loop()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                try
                {
                    Frame();
                    while (snapAt.Count > 0 && Time.time >= snapAt[0])
                    {
                        SnapWhole(snapAt[0]);
                        snapAt.RemoveAt(0);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[OCCW] " + e);
                    Gap();
                }
            }
        }

        /// <summary>The last three frames drawn, whole (1x), side by side: occw_at_&lt;t&gt;_f&lt;frame&gt;.png.</summary>
        void SnapWhole(float t)
        {
            var pv = PixelView.Current;
            if (!shotsOn || pv == null || pv.Target == null) return;
            int f = Time.frameCount, W = pv.Target.width, H = pv.Target.height;
            SaveStrip(string.Format(CI, "occw_at_{0:0.000}_f{1}", t, f), new List<int> { f - 2, f - 1, f }, pv.WorldCamera.transform.position, pv.WorldCamera.transform.position, W, H, true);
        }

        /// <summary>The history starts over (a scene change, a test teleporting the fish or the rig).</summary>
        public void Gap()
        {
            while (hist.Count > 0) Finalise(0);
        }

        /// <summary>Starts a named window: its frames are tallied on their own too (until <see cref="End"/>).</summary>
        public void Begin(string name)
        {
            Gap();
            window = name;
            Get("win " + name);
        }

        /// <summary>Ends the window and returns its tally (logged).</summary>
        public Tally End()
        {
            Gap();
            var t = window != null ? Get("win " + window) : new Tally();
            if (window != null) Debug.Log($"[OCCW] SUMMARY win {window} {t}");
            window = null;
            return t;
        }

        /// <summary>Every frame watched on this stage so far (tallied up to now).</summary>
        public Tally StageTally(string stageId)
        {
            Gap();
            return Get("stage " + stageId);
        }

        Tally Get(string key)
        {
            if (!tallies.TryGetValue(key, out var t))
            {
                t = new Tally();
                tallies[key] = t;
                order.Add(key);
            }
            return t;
        }

        void OnApplicationQuit() => Flush();

        void OnDestroy()
        {
            Flush();
            foreach (var r in ring) if (r != null) r.Release();
            if (I == this) I = null;
        }

        bool flushed;

        /// <summary>Tallies what is left and logs every summary (once).</summary>
        public void Flush()
        {
            if (flushed) return;
            flushed = true;
            Gap();
            foreach (var k in order) Debug.Log($"[OCCW] SUMMARY {k} {tallies[k]}");
            var all = new Tally();
            foreach (var k in order)
            {
                if (!k.StartsWith("stage ")) continue;
                var t = tallies[k];
                all.frames += t.frames; all.exposed += t.exposed; all.depthFrames += t.depthFrames; all.depthPx += t.depthPx;
                all.transFrames += t.transFrames; all.transEvents += t.transEvents; all.tipFlips += t.tipFlips; all.hatFlips += t.hatFlips;
                all.stateFrames += t.stateFrames; all.maxLine = Mathf.Max(all.maxLine, t.maxLine); all.maxFish = Mathf.Max(all.maxFish, t.maxFish);
            }
            Debug.Log($"[OCCW] SUMMARY all {all}");
            try { csv?.Flush(); csv?.Dispose(); } catch { }
            csv = null;
        }

        // ================================================================== one frame
        void Frame()
        {
            var pv = PixelView.Current;
            if (ctl == null) ctl = FindAnyObjectByType<FishingController>();
            KeepFrame(pv);
            if (Paused || ctl == null || pv == null || pv.Target == null || ctl.Stage == null || Time.timeScale <= 0f || !FrontOcclusion.Enabled)
            {
                Gap();
                return;
            }
            var st = ctl.Stage;
            string id = st.Def.id;
            if (FrontOcclusion.StageId == id) everBound.Add(id);
            if (!everBound.Contains(id))
            {
                Gap();   // (no map on this stage: nothing is ever hidden, nothing to test)
                return;
            }
            if (hist.Count > 0 && hist[hist.Count - 1].stage != id) Gap();

            var rec = new Rec { frame = Time.frameCount, time = Time.time, stage = id, window = window, state = ctl.State.ToString() };
            var P = st.P;
            var an = ctl.Angler;
            var cam = (Vector2)pv.WorldCamera.transform.position;
            int W = pv.Target.width, H = pv.Target.height;
            const float PPU = PixelView.PPU;

            // the front layer as drawn, and the map as placed for the shader
            var fa = st.FrontA;
            if (fa == null || fa.sprite == null)
            {
                Gap();
                return;
            }
            var fb = fa.sprite.bounds;
            Vector2 tMin = fa.transform.TransformPoint(fb.min), tMax = fa.transform.TransformPoint(fb.max);
            var tSize = tMax - tMin;
            var placed = FrontOcclusion.PlacedRect;
            bool bound = FrontOcclusion.Ready && FrontOcclusion.StageId == id;
            if (!bound) rec.stateErr += "unbound ";
            if ((placed.min - tMin).sqrMagnitude > 1e-6f || (placed.size - tSize).sqrMagnitude > 1e-6f)
                rec.stateErr += string.Format(CI, "rect({0:0.000},{1:0.000} vs {2:0.000},{3:0.000}) ", placed.min.x, placed.min.y, tMin.x, tMin.y);
            var occMat = FrontOcclusion.Material;

            float OccT(Vector2 w) => FrontOcclusion.OccluderIn(w, tMin, tSize, out _);
            float OccR(Vector2 w) => FrontOcclusion.OccluderIn(w, placed.min, placed.size, out _);
            bool HiddenR(Vector2 w, float d, bool mat) => mat && bound && d > 0f && d > OccR(w) + FrontOcclusion.Bias;
            bool HiddenT(Vector2 w, float d) => d > 0f && d > OccT(w) + FrontOcclusion.Bias;
            int Px(Vector2 w, out int px, out int py)
            {
                px = Mathf.FloorToInt((w.x - cam.x) * PPU + W * 0.5f);
                py = Mathf.FloorToInt((w.y - cam.y) * PPU + H * 0.5f);
                return px < 0 || py < 0 || px >= W || py >= H ? -1 : py * W + px;
            }
            Vector2 Centre(int idx) => new Vector2(cam.x + (idx % W + 0.5f - W * 0.5f) / PPU, cam.y + (idx / W + 0.5f - H * 0.5f) / PPU);

            Vector2 e1Sum = Vector2.zero;
            int e1N = 0;

            // ---- the line above the water, as drawn (depth check) and along its curve (the transient count)
            var tk = ctl.Tackle;
            bool perched = tk != null && tk.State == Tackle.Mode.Perched;
            var lr = an.LineR;
            setA.Clear();
            setB.Clear();
            if (lr != null && lr.enabled && lr.positionCount > 1)
            {
                int n = lr.positionCount;
                if (pos.Length < n) { pos = new Vector3[n]; trueD = new float[n]; }
                lr.GetPositions(pos);
                bool mat = lr.sharedMaterial == occMat && occMat != null;
                if (!mat) rec.stateErr += "lineMat ";
                // each vertex's true depth: the curve's point (the drawn vertex without the shiver)
                for (int i = 0; i < n; i++)
                {
                    float t = i / (n - 1f);
                    if (an.LineAt(t, out var p3, out _))
                    {
                        float d = P.DepthOf(p3);
                        if (perched && (p3 - tk.PerchAt).magnitude <= RestReach) d = Mathf.Max(0.05f, d - FrontOcclusion.Resting);
                        trueD[i] = d;
                    }
                    else trueD[i] = pos[i].z / FrontOcclusion.ZPerMetre;
                }
                float lenPx = 0f;
                for (int i = 0; i + 1 < n; i++)
                {
                    var a = pos[i];
                    var b = pos[i + 1];
                    float segPx = ((Vector2)(b - a)).magnitude * PPU;
                    lenPx += segPx;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(segPx * 3f));
                    for (int s = 0; s <= steps; s++)
                    {
                        float k = s / (float)steps;
                        var q = Vector3.Lerp(a, b, k);
                        int idx = Px(q, out _, out _);
                        if (idx < 0 || !setA.Add(idx)) continue;
                        var c = Centre(idx);
                        float dr = q.z / FrontOcclusion.ZPerMetre;
                        if (HiddenR(c, dr, mat)) continue;
                        if (float.IsPositiveInfinity(OccT(c))) continue;
                        float dt = Mathf.Lerp(trueD[i], trueD[i + 1], k);
                        if (HiddenT(c, dt - 0.02f))
                        {
                            rec.e1Line++;
                            e1Sum += c;
                            e1N++;
                        }
                    }
                }
                // the curve (no shiver): the pixels it shows over solid front texels
                int ns = Mathf.Max(8, Mathf.CeilToInt(lenPx * 3f));
                Vector2 sum = Vector2.zero;
                for (int s = 0; s <= ns; s++)
                {
                    float t = s / (float)ns;
                    if (!an.LineAt(t, out var p3, out var lb)) break;
                    var q = P.To2D(p3, out float d) + lb;
                    if (perched && (p3 - tk.PerchAt).magnitude <= RestReach) d = Mathf.Max(0.05f, d - FrontOcclusion.Resting);
                    int idx = Px(q, out _, out _);
                    if (idx < 0 || !setB.Add(idx)) continue;
                    var c = Centre(idx);
                    if (float.IsPositiveInfinity(OccT(c)) || HiddenT(c, d)) continue;
                    rec.lineOver++;
                    sum += c;
                }
                if (rec.lineOver > 0) rec.lineAt = sum / rec.lineOver;
                if (an.LineAt(0f, out var q0, out var b0) && an.LineAt(0.5f, out var q1, out var b1) && an.LineAt(1f, out var q2, out var b2))
                {
                    rec.lineOn = true;
                    rec.l0 = (P.To2D(q0) + b0) * PPU;
                    rec.l1 = (P.To2D(q1) + b1) * PPU;
                    rec.l2 = (P.To2D(q2) + b2) * PPU;
                }
            }

            // ---- the dangling bait's line (idle): its vertices' own depths
            var dl = an.DangleR;
            if (dl != null && dl.enabled && dl.positionCount > 1 && dl.sortingOrder > StageView.OrderFront)
            {
                bool mat = dl.sharedMaterial == occMat && occMat != null;
                if (!mat) rec.stateErr += "dangleMat ";
                var a = dl.GetPosition(0);
                var b = dl.GetPosition(1);
                int steps = Mathf.Max(1, Mathf.CeilToInt(((Vector2)(b - a)).magnitude * PPU * 3f));
                setC.Clear();
                for (int s = 0; s <= steps; s++)
                {
                    var q = Vector3.Lerp(a, b, s / (float)steps);
                    int idx = Px(q, out _, out _);
                    if (idx < 0 || !setC.Add(idx)) continue;
                    var c = Centre(idx);
                    float d = q.z / FrontOcclusion.ZPerMetre;
                    if (!HiddenR(c, d, mat) && HiddenT(c, d)) { rec.e1Other++; e1Sum += c; e1N++; }
                }
            }

            // ---- the fish in the air / landed (the side sprite): its true depth is its point in the air
            setC.Clear();
            Vector2 fsum = Vector2.zero;
            void FishSprite(FishAgent f)
            {
                if (f == null) return;
                var sr = f.SideR;
                if (sr == null || !sr.enabled || sr.sprite == null || sr.sortingOrder <= StageView.OrderFront) return;
                bool mat = sr.sharedMaterial == occMat && occMat != null;
                if (!mat) rec.stateErr += "fishMat ";
                if (!rec.fishOn)
                {
                    rec.fishOn = true;
                    rec.f0 = (Vector2)sr.transform.position * PPU;
                }
                float dr = RenderDepth(sr);
                float dt = P.DepthOf(new Vector3(f.Pos.x, Mathf.Max(0f, f.Pos.y), f.Pos.z));
                Covered(sr, Px, Centre, W, H, idx =>
                {
                    var c = Centre(idx);
                    if (HiddenR(c, dr, mat)) return;
                    if (float.IsPositiveInfinity(OccT(c))) return;
                    if (setC.Add(idx))
                    {
                        rec.fishOver++;
                        fsum += c;
                    }
                    if (HiddenT(c, dt - 0.02f)) { rec.e1Fish++; e1Sum += c; e1N++; }
                });
            }
            if (ctl.Spawner != null) foreach (var f in ctl.Spawner.Fish) FishSprite(f);
            if (ctl.Hooked != null && (ctl.Spawner == null || !Contains(ctl.Spawner.Fish, ctl.Hooked))) FishSprite(ctl.Hooked);
            if (rec.fishOver > 0) rec.fishAt = fsum / rec.fishOver;

            // ---- the rest drawn over the front layer with the occluding material: their own depth is the truth, so only
            // a state error (the material, the map's place, the map) can expose them
            void Other(SpriteRenderer sr, string name)
            {
                if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy || sr.sprite == null || sr.sortingOrder <= StageView.OrderFront) return;
                bool mat = sr.sharedMaterial == occMat && occMat != null;
                float dr = RenderDepth(sr);
                if (!mat && dr > 0f) rec.stateErr += name + "Mat ";
                Covered(sr, Px, Centre, W, H, idx =>
                {
                    var c = Centre(idx);
                    if (!HiddenR(c, dr, mat) && HiddenT(c, dr)) { rec.e1Other++; e1Sum += c; e1N++; }
                });
            }
            Other(an.DangleBaitR, "dangleBait");
            if (tk != null)
            {
                Other(tk.FloatR, "float");
                Other(tk.BaitR, "bait");
                Other(tk.ChemiR, "chemi");
            }
            Fx.LiveRenderers(fxList);
            foreach (var sr in fxList) Other(sr, "fx");
            if (e1N > 0) rec.e1At = e1Sum / e1N;

            rec.tip = (P.To2D(an.RodTip) + st.DeckBob) * PPU;
            rec.hat = an.HatTilt;
            rec.tension = an.Tension01;
            rec.target = an.LineTarget ?? Vector3.zero;
            if (ctl.Hooked != null)
            {
                rec.fishPos = ctl.Hooked.Pos;
                rec.jumpT = ctl.Hooked.JumpT;
            }
            if (rec.e1Line + rec.e1Fish + rec.e1Other > 0)
            {
                rec.flags |= 1;
                rec.why += string.Format(CI, "depth line {0} fish {1} other {2} px at ({3:0.0},{4:0.0}); ", rec.e1Line, rec.e1Fish, rec.e1Other,
                    (rec.e1At.x - cam.x) * PPU + W * 0.5f, H - 1 - ((rec.e1At.y - cam.y) * PPU + H * 0.5f));
            }
            hist.Add(rec);
            Evaluate(cam, W, H);
            // (a depth exposure is reported where it starts: a run of them is one event)
            bool depthRun = hist.Count >= 2 && (hist[hist.Count - 2].flags & 1) != 0 && hist[hist.Count - 2].frame == rec.frame - 1;
            if ((rec.flags & 1) != 0 && !depthRun) Report(rec, "depth", rec.e1At, cam, W, H, 2);
            while (hist.Count > 4) Finalise(0);
        }

        static bool Contains(IReadOnlyList<FishAgent> list, FishAgent f)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == f) return true;
            return false;
        }

        /// <summary>The depth the shader tests a sprite at: its property block's, else its world z's (0: never hidden).</summary>
        float RenderDepth(Renderer r)
        {
            float od = 0f;
            if (r.HasPropertyBlock())
            {
                r.GetPropertyBlock(blk);
                od = blk.GetFloat(DepthId);
            }
            if (od > 0f) return od;
            if (od < 0f) return 0f;
            return r.transform.position.z / FrontOcclusion.ZPerMetre;
        }

        /// <summary>The render-target pixels the sprite covers (its opaque texels; the whole rect when its texture is not readable).</summary>
        void Covered(SpriteRenderer sr, PxFn px, System.Func<int, Vector2> centre, int W, int H, System.Action<int> each)
        {
            var b = sr.bounds;
            int x0, y0, x1, y1;
            if (px(b.min, out x0, out y0) < 0) { x0 = Mathf.Clamp(x0, 0, W - 1); y0 = Mathf.Clamp(y0, 0, H - 1); }
            if (px(b.max, out x1, out y1) < 0) { x1 = Mathf.Clamp(x1, 0, W - 1); y1 = Mathf.Clamp(y1, 0, H - 1); }
            x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0); x1 = Mathf.Min(W - 1, x1); y1 = Mathf.Min(H - 1, y1);
            if (x1 < x0 || y1 < y0) return;
            var spr = sr.sprite;
            var tex = spr.texture;
            Color32[] texels = null;
            if (tex != null && tex.isReadable && !texCache.TryGetValue(tex, out texels))
            {
                texels = tex.GetPixels32();
                texCache[tex] = texels;
            }
            var rect = spr.rect;
            float ppu = spr.pixelsPerUnit;
            var inv = sr.transform.worldToLocalMatrix;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int idx = y * W + x;
                var c = centre(idx);
                var l = inv.MultiplyPoint3x4(new Vector3(c.x, c.y, sr.transform.position.z));
                float lx = l.x * ppu, ly = l.y * ppu;
                if (sr.flipX) lx = -lx;
                if (sr.flipY) ly = -ly;
                float tx = spr.pivot.x + lx, ty = spr.pivot.y + ly;
                if (tx < 0f || ty < 0f || tx >= rect.width || ty >= rect.height) continue;
                if (texels != null)
                {
                    int ix = Mathf.FloorToInt(rect.x + tx), iy = Mathf.FloorToInt(rect.y + ty);
                    if (ix < 0 || iy < 0 || ix >= tex.width || iy >= tex.height || texels[iy * tex.width + ix].a < 128) continue;
                }
                each(idx);
            }
        }

        delegate int PxFn(Vector2 w, out int px, out int py);

        // ================================================================== over the frames
        void Evaluate(Vector2 cam, int W, int H)
        {
            int n = hist.Count;
            bool Consec(int from) { for (int i = from; i + 1 < n; i++) if (hist[i + 1].frame != hist[i].frame + 1) return false; return true; }
            // one frame: the one before last against its neighbours
            if (n >= 3 && Consec(n - 3))
            {
                Rec a = hist[n - 3], b = hist[n - 2], c = hist[n - 1];
                Spike1(a, b, c, r => r.lineOver, r => r.lineAt, true, "line", cam, W, H);
                Spike1(a, b, c, r => r.fishOver, r => r.fishAt, false, "fish", cam, W, H);
                float d1 = (b.tip - a.tip).magnitude, d2 = (c.tip - b.tip).magnitude, back = (c.tip - a.tip).magnitude;
                if (Mathf.Min(d1, d2) >= TipFlipPx && back <= 0.35f * Mathf.Max(d1, d2) && Sudden(n - 3, b.tip - a.tip) && a.state == b.state && b.state == c.state)
                {
                    b.tipFlip = true;
                    TipLog(a, b, c);
                }
                if (Mathf.Abs(b.hat) > 0.5f && Mathf.Abs(c.hat) > 0.5f && Mathf.Sign(b.hat) != Mathf.Sign(c.hat)) c.hatFlip = true;
            }
            // two frames
            if (n >= 4 && Consec(n - 4))
            {
                Rec a = hist[n - 4], b = hist[n - 3], c = hist[n - 2], d = hist[n - 1];
                Spike2(a, b, c, d, r => r.lineOver, r => r.lineAt, true, "line", cam, W, H);
                Spike2(a, b, c, d, r => r.fishOver, r => r.fishAt, false, "fish", cam, W, H);
                float j1 = (b.tip - a.tip).magnitude, hold = (c.tip - b.tip).magnitude, j2 = (d.tip - c.tip).magnitude, back = (d.tip - a.tip).magnitude;
                if (Mathf.Min(j1, j2) >= TipFlipPx && hold < 0.5f * Mathf.Min(j1, j2) && back <= 0.35f * Mathf.Max(j1, j2) && !b.tipFlip && !c.tipFlip
                    && Sudden(n - 4, b.tip - a.tip) && a.state == b.state && b.state == c.state && c.state == d.state)
                {
                    b.tipFlip = true;
                    TipLog(a, b, d);
                }
            }
        }

        int tipLogged;

        /// <summary>A tip flip's frames (before, the flip, after): what the rod, the line and the fish were doing.</summary>
        void TipLog(Rec a, Rec b, Rec c)
        {
            if (tipLogged++ >= 60) return;
            string R(Rec r) => string.Format(CI, "f{0} tip ({1:0.0},{2:0.0}) hat {3:+0.0;-0.0;0} tension {4:0.00} target ({5:0.00},{6:0.00},{7:0.00}) fish ({8:0.00},{9:0.00},{10:0.00}) jump {11:0.00}",
                r.frame, r.tip.x, r.tip.y, r.hat, r.tension, r.target.x, r.target.y, r.target.z, r.fishPos.x, r.fishPos.y, r.fishPos.z, r.jumpT);
            Debug.Log($"[OCCW] TIPFLIP stage={b.stage} win={b.window ?? "-"} state={b.state} | {R(a)} | {R(b)} | {R(c)}");
            // the first few as strips too (the frames around it, below the tip where the line runs down)
            var pv = PixelView.Current;
            if (shotsOn && tipShots < 4 && pv != null && pv.Target != null)
            {
                tipShots++;
                var frames = new List<int>();
                for (int i = a.frame; i <= c.frame; i++) frames.Add(i);
                var focus = (b.tip + new Vector2(0f, -30f)) / PixelView.PPU;
                SaveStrip($"occw_tipflip_{tipShots:00}_{b.stage}_f{b.frame}", frames, focus, pv.WorldCamera.transform.position, pv.Target.width, pv.Target.height);
            }
        }

        int tipShots;

        /// <summary>
        /// The tip's step <paramref name="step"/> out of frame <paramref name="i"/> is a jump, not the rod carrying on a
        /// motion it was already making (the frame before moved it the same way by much the same: the rod bowing and
        /// straightening smoothly over a few frames, which also comes back): a flip starts from rest or reverses the last step.
        /// </summary>
        bool Sudden(int i, Vector2 step)
        {
            if (i < 1 || hist[i].frame != hist[i - 1].frame + 1) return true;
            var before = hist[i].tip - hist[i - 1].tip;
            return Vector2.Dot(before, step) < 0.5f * step.sqrMagnitude;
        }

        /// <summary>How far the line (its curve's start, middle and end) or the fish moved on screen between two frames (px).</summary>
        static float Moved(Rec p, Rec q, bool line) => line
            ? Mathf.Max((q.l0 - p.l0).magnitude, Mathf.Max((q.l1 - p.l1).magnitude, (q.l2 - p.l2).magnitude))
            : (q.f0 - p.f0).magnitude;

        static bool On(Rec r, bool line) => line ? r.lineOn : r.fishOn;

        /// <summary>
        /// Moving along from <paramref name="a"/> through the middle frames to <paramref name="d"/>: drawn in all of them and
        /// moving on (2+ px a frame) without coming back (a flip goes there and back: the ends lie close together).
        /// </summary>
        static bool MovingAlong(Rec a, Rec b, Rec c, Rec d, bool line)
        {
            if (!On(a, line) || !On(b, line) || !On(c, line) || !On(d, line)) return false;
            float first = Moved(a, b, line), last = Moved(c, d, line), whole = Moved(a, d, line);
            return Mathf.Max(first, last) >= 2f && whole > 0.35f * Mathf.Max(first, last);
        }

        void Spike1(Rec a, Rec b, Rec c, System.Func<Rec, int> cnt, System.Func<Rec, Vector2> at, bool line, string what, Vector2 cam, int W, int H)
        {
            int c1 = cnt(b), side = Mathf.Max(cnt(a), cnt(c));
            if (c1 - side < Mathf.Max(SpikePx, SpikeShare * c1)) return;
            if (MovingAlong(a, b, b, c, line)) return;   // (a line swinging on, a fish passing an edge)
            b.flags |= 2;
            b.evStart = true;
            b.why += string.Format(CI, "transient {0} {1} px for 1 frame (before {2}, after {3}); ", what, c1, cnt(a), cnt(c));
            Report(b, "transient_" + what, at(b), cam, W, H, 1);
        }

        void Spike2(Rec a, Rec b, Rec c, Rec d, System.Func<Rec, int> cnt, System.Func<Rec, Vector2> at, bool line, string what, Vector2 cam, int W, int H)
        {
            int m = Mathf.Min(cnt(b), cnt(c)), side = Mathf.Max(cnt(a), cnt(d));
            if (m - side < Mathf.Max(SpikePx, SpikeShare * m)) return;
            if (MovingAlong(a, b, c, d, line)) return;
            if ((b.flags & 2) != 0 || (c.flags & 2) != 0) return;   // (already a one-frame spike)
            b.flags |= 2;
            b.evStart = true;
            c.flags |= 2;
            string w = string.Format(CI, "transient {0} {1}/{2} px for 2 frames (before {3}, after {4}); ", what, cnt(b), cnt(c), cnt(a), cnt(d));
            b.why += w;
            c.why += w;
            Report(b, "transient2_" + what, at(b), cam, W, H, 2);
        }

        void Report(Rec r, string kind, Vector2 focus, Vector2 cam, int W, int H, int back)
        {
            if (linesLogged < MaxLines)
            {
                linesLogged++;
                var fish = ctl != null ? ctl.Hooked : null;
                Debug.Log(string.Format(CI, "[OCCW] EXPOSED f={0} t={1:0.000} stage={2} win={3} state={4} kind={5} {6}tip=({7:0.0},{8:0.0}) hat={9:+0.0;-0.0;0.0} fish={10}",
                    r.frame, r.time, r.stage, r.window ?? "-", r.state, kind, r.why, r.tip.x, r.tip.y, r.hat,
                    fish != null ? string.Format(CI, "({0:0.00},{1:0.00},{2:0.00}) jump {3:0.00} {4}", fish.Pos.x, fish.Pos.y, fish.Pos.z, fish.JumpT, fish.State) : "-"));
            }
            if (shotsOn && shotsSaved < MaxShots)
            {
                shotsSaved++;
                int f = Time.frameCount;
                var frames = new List<int>();
                for (int i = r.frame - 1; i <= Mathf.Min(f, r.frame + 1); i++) frames.Add(i);
                SaveStrip($"occw_{shotsSaved:00}_{r.stage}_{kind}_f{r.frame}", frames, focus, cam, W, H);
            }
        }

        void Finalise(int i)
        {
            var r = hist[i];
            hist.RemoveAt(i);
            Add(Get("stage " + r.stage), r);
            if (r.window != null) Add(Get("win " + r.window), r);
            try
            {
                csv?.WriteLine(string.Format(CI, "{0},{1:0.0000},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11:0.0},{12:0.0},{13:0.00},{14},{15}",
                    r.frame, r.time, r.stage, r.window ?? "", r.state, r.lineOver, r.fishOver, r.e1Line, r.e1Fish, r.e1Other,
                    r.flags | (r.tipFlip ? 4 : 0) | (r.hatFlip ? 8 : 0), r.tip.x, r.tip.y, r.hat, r.stateErr.Trim(), r.why.Replace(',', ';').Trim()));
            }
            catch { }
        }

        static void Add(Tally t, Rec r)
        {
            t.frames++;
            if (r.flags != 0) t.exposed++;
            if ((r.flags & 1) != 0)
            {
                t.depthFrames++;
                t.depthPx += r.e1Line + r.e1Fish + r.e1Other;
            }
            if ((r.flags & 2) != 0)
            {
                t.transFrames++;
                if (r.evStart) t.transEvents++;
            }
            if (r.tipFlip) t.tipFlips++;
            if (r.hatFlip) t.hatFlips++;
            if (r.stateErr.Length > 0)
            {
                t.stateFrames++;
                if (t.firstState.Length == 0) t.firstState = $"f{r.frame} {r.stateErr.Trim()}";
            }
            t.maxLine = Mathf.Max(t.maxLine, r.lineOver);
            t.maxFish = Mathf.Max(t.maxFish, r.fishOver);
        }

        // ================================================================== strips
        void KeepFrame(PixelView pv)
        {
            if (!shotsOn || pv == null || pv.Target == null) return;
            var src = pv.Target;
            int k = Time.frameCount & 3;
            var r = ring[k];
            if (r == null || r.width != src.width || r.height != src.height)
            {
                if (r != null) r.Release();
                r = ring[k] = new RenderTexture(src.width, src.height, 0, src.format) { filterMode = FilterMode.Point, name = "OccWatchRing" };
                r.Create();
            }
            Graphics.CopyTexture(src, r);
            ringFrame[k] = Time.frameCount;
        }

        /// <summary>A strip of the last three frames drawn, cropped around <paramref name="focusWorld"/> (pixel scene), 2x.</summary>
        public void Snap(string name, Vector2 focusWorld)
        {
            var pv = PixelView.Current;
            if (!shotsOn || pv == null || pv.Target == null) return;
            int f = Time.frameCount;
            SaveStrip("occw_" + name, new List<int> { f - 2, f - 1, f }, focusWorld, pv.WorldCamera.transform.position, pv.Target.width, pv.Target.height);
        }

        void SaveStrip(string name, List<int> frames, Vector2 focus, Vector2 cam, int W, int H, bool whole = false)
        {
            if (string.IsNullOrEmpty(dir)) return;
            var got = new List<RenderTexture>();
            foreach (int fr in frames)
                for (int k = 0; k < 4; k++)
                    if (ringFrame[k] == fr && ring[k] != null) got.Add(ring[k]);
            if (got.Count == 0) return;
            int fx = Mathf.FloorToInt((focus.x - cam.x) * PixelView.PPU + W * 0.5f), fy = Mathf.FloorToInt((focus.y - cam.y) * PixelView.PPU + H * 0.5f);
            int cw = whole ? W : Mathf.Min(CropW, W), ch = whole ? H : Mathf.Min(CropH, H);
            int x0 = Mathf.Clamp(fx - cw / 2, 0, W - cw), y0 = Mathf.Clamp(fy - ch / 2, 0, H - ch);
            int S = whole ? 1 : 2;
            const int Gap = 2;
            int ow = got.Count * cw * S + (got.Count - 1) * Gap, oh = ch * S;
            var outT = new Texture2D(ow, oh, TextureFormat.RGB24, false);
            var fill = new Color32[ow * oh];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 255, 255, 255);
            var prev = RenderTexture.active;
            var tmp = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            for (int g = 0; g < got.Count; g++)
            {
                RenderTexture.active = got[g];
                AutoShot.Read(tmp, new Rect(x0, y0, cw, ch));
                tmp.Apply(false);
                var px = tmp.GetPixels32();
                int ox = g * (cw * S + Gap);
                for (int y = 0; y < ch * S; y++)
                for (int x = 0; x < cw * S; x++)
                    fill[y * ow + ox + x] = px[(y / S) * cw + x / S];
            }
            RenderTexture.active = prev;
            outT.SetPixels32(fill);
            outT.Apply(false);
            try { File.WriteAllBytes(Path.Combine(dir, name + ".png"), outT.EncodeToPNG()); }
            catch (System.Exception e) { Debug.LogWarning("[OCCW] strip: " + e.Message); }
            Destroy(tmp);
            Destroy(outT);
            Debug.Log(string.Format(CI, "[OCCW] strip {0}.png frames {1} crop {2} {3} {4} {5}", name, string.Join("/", frames), x0, H - y0 - ch, cw, ch));
        }
    }
}
