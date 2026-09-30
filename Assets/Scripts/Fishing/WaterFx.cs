using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FishingKing
{
    /// <summary>
    /// Per-stage water movement on top of the painted water ("물결 / 파도"):
    /// <list type="bullet">
    /// <item>lake   - slow drifting wind-ripple dashes (light crest + dark trough) and an occasional small rise ring;</item>
    /// <item>stream - flow streaks running down the channel towards the camera, fast short riffles below the rocks and
    ///                flickering white foam where the water meets the rocks / banks;</item>
    /// <item>sea    - broken wave-crest lines rolling towards the breakwater, foam at the tetrapods / buoy as a crest arrives;</item>
    /// <item>ocean  - long slow swells with occasional whitecaps; the swell also bobs the boat: <see cref="StageView.DeckBob"/>;</item>
    /// <item>swamp  - rare bubbles popping into small rings, a few drifting specks;</item>
    /// <item>ice    - nothing on the ice, a tiny ripple inside the fishing hole now and then;</item>
    /// <item>cave   - drips falling from the ceiling into the still water, faint rings.</item>
    /// </list>
    /// Everything lives in game space (metres, water surface y = 0), is projected with <see cref="Persp"/> (sizes from
    /// the pixels per metre, rings squashed by <see cref="Persp.Foreshorten"/>, faded with distance) and drawn as 1 px
    /// runs snapped to the pixel grid, clipped per pixel to the painted play water. The water mask is built once from the
    /// stage art: the dominant water colours of the back layer (sampled over the play area), minus the front layer,
    /// inside |x| &lt;= xLim and zNear..zFar, below the horizon. Runs are pooled sprite renderers (no per-frame allocation).
    /// Sorting orders 25-28: above the fish shadows (10-19) and the underwater line (20), below the glints (29),
    /// the float (31) and the front layer (40).
    /// Test switches: -fkwatermask tints the mask (water magenta, front-layer edges yellow, back-layer edges cyan);
    /// -fkwatervivid draws every effect opaque in a loud colour (crests / light red, dark blue, foam yellow, riffles and
    /// drops magenta, specks green) to check placement;
    /// motion strips of every stage: <see cref="WaterStrip"/> (-fkwaterstrip &lt;dir&gt;).
    /// <para>The moving water (Docs/time_currents_spec.md 10), from the stage's <see cref="CurrentField"/>: the stream's
    /// streaks and riffles run at the local current (a surge is a brighter, faster band sliding down; the pockets behind
    /// the rocks show slow dark curling dashes), leaves and foam flecks drift down the lane; the sea shows flow lines with
    /// the tide, glassy slicks at slack water, weed tufts leaning with the flow, a wet band on the near tetrapods at low
    /// water and the buoy's wake; on the ocean the swell pattern and patches of flotsam slide with the drift; on the lake
    /// and the swamp a gust sweeps a cat's paw across the water. A float moving through the water trails a wake. The
    /// period look (<see cref="SetLook"/>) recolours every effect and dims it (fxAlpha). -fkcurrentvivid draws the field.</para>
    /// </summary>
    public class WaterFx : MonoBehaviour
    {
        public const int OrderWave = 25, OrderCrest = 26, OrderFoam = 27, OrderRing = 28, OrderWake = 30, OrderOverFront = 41;

        /// <summary>The rig in the water (set by the controller): its float trails a wake.</summary>
        public Tackle Rig;
        CurrentField cur;
        float fxAlpha = 1f, night;

        enum Kind { None, Lake, Stream, Sea, Ocean, Swamp, Ice, Cave }

        // mask bits per stage-canvas pixel (row 0 = bottom)
        const byte MWater = 1, MFront = 2, MPainted = 4, MSeen = 8, MIsland = 16;

        StageView stage;
        StageLayout L;
        Persp P;
        Kind kind;
        int W, H;
        float halfW, halfH;
        byte[] mask;
        int horizonRow;

        // foam candidates: water pixels next to a front-layer prop (or a painted obstacle)
        int edgeN;
        int[] edgePix;
        float[] edgeX, edgeZ, edgeH;
        byte[] edgeKind;                  // 1 front prop below (upstream side), 2 front prop at the side, 3 painted obstacle

        Color lit, lighter, dark, foam, crest, speck;
        float fade0 = 20f, fade1 = 70f;   // distance fade (metres)
        int maxDash = 10;                 // longest dash (px)

        // visible part of the stage canvas this frame
        int vx0, vx1, vy0, vy1, rtW = 480;

        // ---- particles
        struct Dash { public float x, z, vx, vz, len, age, life, alpha, k; public byte kind; }
        const byte DLight = 0, DDark = 1, DRiffle = 2, DPair = 3, DSpeck = 4, DFlow = 5;
        Dash[] dashes = new Dash[0];
        int riffleFrom;                   // dashes[riffleFrom..] are stream riffles

        struct Ring { public float x, z, age, life, r0, r1, alpha, pre, fallH; public byte preKind; public bool on; }
        const byte PreNone = 0, PreBubble = 1, PreDrop = 2;
        readonly Ring[] rings = new Ring[12];
        float ringTimer = 1.5f;

        // ---- swell (sea, ocean)
        struct Swell
        {
            public float lambda, speed, cell, fill, lenMin, lenMax, wobA, wobK, slope, zMin, zMax, shadowZ, crestA, shadowA;
            public bool whitecaps;
        }
        bool hasSwell;
        Swell sw;

        // ---- pooled 1 px runs
        static Sprite dot;
        readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
        int used, prevUsed;
        const int MaxSegs = 900;

        // ---- pooled effect sprites (Sprites/World/fx_*, Tools/Blender/fk_tod.py): drifting debris, wakes, flow lines
        readonly List<SpriteRenderer> sprPool = new List<SpriteRenderer>();
        int sprUsed, sprPrev;
        const int MaxSprs = 120;
        static readonly Dictionary<string, Sprite> fxSprites = new Dictionary<string, Sprite>();

        // ---- drifting things: stream leaves / foam flecks, ocean flotsam
        struct Drifter { public float x, z, phase, age; public byte kind, size; public bool on; }
        const byte KLeafYellow = 0, KLeafBrown = 1, KLeafGreen = 2, KFleck = 3, KSargassum = 4, KDriftwood = 5, KFoamPatch = 6;
        Drifter[] drift = new Drifter[0];

        // ---- sea: slack slicks, weed tufts, the wet band, the buoy
        struct Slick { public float x, z; public bool big; }
        readonly Slick[] slicks = new Slick[4];
        float slickA;
        bool slicksPlaced;
        int[] tuftPix = new int[0];
        float spotMeanX;
        readonly SpriteRenderer[] wetBand = new SpriteRenderer[3];   // the wet band's rows above the waterline
        readonly int[] wetCount = new int[3];
        int buoyC = -1, buoyR = -1;

        // ---- lake / swamp: the gust's cat's paw
        readonly Vector3[] paw = new Vector3[14];   // (dx, dz, length m) around the patch centre
        float pawGustT = -1f, pawX0, pawZ0;

        // ---- ocean: the drift the swell pattern slides with
        float driftX, driftZ;
        float wakeT;

        /// <summary>Swell bob of the boat (world units, whole pixels); zero on every stage but the ocean.</summary>
        public Vector2 DeckBob { get; private set; }

        /// <summary>For test logs: runs drawn last frame, live rings and dashes.</summary>
        public string Stats
        {
            get
            {
                int r = 0, d = 0;
                foreach (var g in rings) if (g.on) r++;
                foreach (var x in dashes) if (x.alpha > 0f) d++;
                return $"{kind}: {prevUsed} runs, {r} rings, {d} dashes, bob {DeckBob.y * PixelView.PPU:0}";
            }
        }

        public static WaterFx Create(StageView s)
        {
            var go = new GameObject("WaterFx");
            go.transform.SetParent(s.transform, false);
            var fx = go.AddComponent<WaterFx>();
            fx.Init(s);
            return fx;
        }

        void Init(StageView s)
        {
            stage = s;
            L = s.L;
            P = s.P;
            cur = s.Current;
            W = L.widthPx > 0 ? L.widthPx : 640;
            H = L.heightPx > 0 ? L.heightPx : 400;
            halfW = W / 2f;
            halfH = H / 2f;
            horizonRow = Mathf.FloorToInt(halfH + L.focalPx * Mathf.Tan(L.pitch * Mathf.Deg2Rad)) - 2;
            Configure();
            try { BuildMask(); }
            catch (Exception e)
            {
                Debug.LogWarning("[WaterFx] no water mask: " + e.Message);
                mask = new byte[W * H];
                edgeN = 0;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fkwatermask") >= 0) ShowMask();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fkwatervivid") >= 0)
            {
                // placement check: every effect in a loud flat colour
                vivid = true;
                lit = crest = Color.red;
                lighter = Color.magenta;
                dark = Color.blue;
                foam = Color.yellow;
                speck = Color.green;
            }
            ViewBounds();
            for (int i = 0; i < dashes.Length; i++)
            {
                SpawnDash(i);
                dashes[i].age = Random.Range(0f, dashes[i].life);
            }
            for (int i = 0; i < 120; i++) pool.Add(NewSeg());
            foreach (var sr in pool) sr.enabled = false;
            if (mask != null)
            {
                if (kind == Kind.Sea) InitSea();
                if (kind == Kind.Stream) drift = new Drifter[16];
                if (kind == Kind.Ocean) drift = new Drifter[8];
                for (int i = 0; i < drift.Length; i++) SpawnDrifter(i, true);
            }
            if (kind == Kind.Lake || kind == Kind.Swamp)
                for (int i = 0; i < paw.Length; i++)
                {
                    var o = Random.insideUnitCircle;
                    paw[i] = new Vector3(o.x, o.y * 0.6f, Random.Range(0.3f, 0.6f));
                }
        }

        // ------------------------------------------------------------------ per-stage setup
        /// <summary>The effect colours from the water's tint / deep (Configure's formulas).</summary>
        void Colours(Color tint, Color deep)
        {
            lit = Color.Lerp(tint, Color.white, 0.38f);
            lighter = Color.Lerp(tint, Color.white, 0.58f);
            dark = Color.Lerp(tint, deep, 0.45f);
            foam = Color.Lerp(Color.white, tint, 0.12f);
            crest = Color.Lerp(tint, Color.white, 0.45f);
            speck = Color.Lerp(tint, Color.white, 0.3f);
            if (kind == Kind.Swamp) speck = Color.Lerp(tint, new Color(0.8f, 0.82f, 0.5f), 0.55f);
            if (kind == Kind.Ice) lit = Color.Lerp(deep, tint, 0.75f);
        }

        /// <summary>
        /// The period look (StageView, on every blend change; spec 5.3): the effect colours from its water, foam and crests
        /// cooled towards moonlight at night, every effect's alpha x <paramref name="alpha"/>.
        /// </summary>
        public void SetLook(Color tint, Color deep, float alpha, float nightW)
        {
            fxAlpha = Mathf.Clamp01(alpha);
            night = Mathf.Clamp01(nightW);
            if (vivid) return;
            Colours(tint, deep);
            var moon = new Color32(0xc8, 0xd4, 0xf0, 0xff);
            foam = Color.Lerp(foam, moon, 0.3f * night);
            crest = Color.Lerp(crest, moon, 0.3f * night);
        }

        void Configure()
        {
            Color tint = stage.WaterTint, deep = stage.WaterDeep;
            Colours(tint, deep);
            switch (L.id)
            {
                case "lake": kind = Kind.Lake; break;
                case "stream": kind = Kind.Stream; break;
                case "sea": kind = Kind.Sea; break;
                case "ocean": kind = Kind.Ocean; break;
                case "swamp": kind = Kind.Swamp; break;
                case "ice": kind = Kind.Ice; break;
                case "cave": kind = Kind.Cave; break;
                default: kind = L.IsIce ? Kind.Ice : L.mode == "boat" ? Kind.Ocean : Kind.Lake; break;
            }
            switch (kind)
            {
                case Kind.Lake:
                    dashes = new Dash[26];
                    fade0 = 16f; fade1 = 60f; maxDash = 10;
                    break;
                case Kind.Stream:
                    dashes = new Dash[46 + 18];
                    riffleFrom = 46;
                    fade0 = 24f; fade1 = 70f; maxDash = 13;
                    break;
                case Kind.Sea:
                    dashes = new Dash[20];   // the tide's flow lines
                    hasSwell = true;
                    sw = new Swell
                    {
                        lambda = 7f, speed = 1.1f, cell = 3.2f, fill = 0.6f, lenMin = 1f, lenMax = 2.6f, wobA = 0.35f, wobK = 0.35f,
                        slope = 0.03f, zMin = L.zNear - 1f, zMax = 75f, shadowZ = 30f, crestA = 0.85f, shadowA = 0.6f,
                    };
                    fade0 = 26f; fade1 = 80f;
                    break;
                case Kind.Ocean:
                    hasSwell = true;
                    sw = new Swell
                    {
                        lambda = 15f, speed = 1.5f, cell = 4f, fill = 0.7f, lenMin = 1.5f, lenMax = 3.4f, wobA = 0.9f, wobK = 0.16f,
                        slope = -0.06f, zMin = L.zNear, zMax = 100f, shadowZ = 45f, crestA = 0.75f, shadowA = 0.6f, whitecaps = true,
                    };
                    fade0 = 22f; fade1 = 95f;
                    break;
                case Kind.Swamp:
                    dashes = new Dash[10];
                    fade0 = 14f; fade1 = 40f; maxDash = 2;
                    // duckweed-ish specks: the water lifted towards a pale yellow-green
                    speck = Color.Lerp(tint, new Color(0.8f, 0.82f, 0.5f), 0.55f);
                    break;
                case Kind.Ice:
                    // the hole shows the deep water: a ripple a little lighter than it
                    lit = Color.Lerp(deep, tint, 0.75f);
                    fade0 = 30f; fade1 = 60f;
                    break;
                case Kind.Cave:
                    fade0 = 20f; fade1 = 60f;
                    break;
            }
        }

        // ------------------------------------------------------------------ water mask
        /// <summary>A texture's pixels (rows from the bottom), through a blit: the stage art is not readable.</summary>
        internal static Color32[] ReadTexture(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            t.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Destroy(t);
            return px;
        }

        static int Key(Color32 c) => (c.r << 16) | (c.g << 8) | c.b;

        bool CanvasPixel(Vector3 p, out int c, out int r)
        {
            var q = P.ToPixel(p, out _);
            c = Mathf.FloorToInt(halfW + q.x);
            r = Mathf.FloorToInt(halfH + q.y);
            return c >= 0 && c < W && r >= 0 && r < H;
        }

        void BuildMask()
        {
            mask = new byte[W * H];
            var backS = Art.Stage(L.id + "_back");
            var frontS = Art.Stage(L.id + "_front");
            if (backS == null || backS.texture.width != W || backS.texture.height != H) throw new Exception("back layer missing or not " + W + "x" + H);
            var back = ReadTexture(backS.texture);
            Color32[] front = frontS != null && frontS.texture.width == W && frontS.texture.height == H ? ReadTexture(frontS.texture) : null;
            if (front != null)
                for (int i = 0; i < front.Length; i++)
                    if (front[i].a > 8) mask[i] |= MFront;

            // 1. the dominant water colours of the play area (for the ice: of the fishing hole)
            var counts = new Dictionary<int, int>();
            int total = 0;
            void Sample(Vector3 p)
            {
                if (!CanvasPixel(p, out int c, out int r)) return;
                int i = r * W + c;
                if ((mask[i] & MFront) != 0) return;
                int k = Key(back[i]);
                counts.TryGetValue(k, out int n);
                counts[k] = n + 1;
                total++;
            }
            if (kind == Kind.Ice)
            {
                float rr = L.holeR * 0.8f;
                for (int i = 0; i <= 24; i++)
                for (int j = 0; j <= 24; j++)
                {
                    float dx = (i / 24f * 2f - 1f) * rr, dz = (j / 24f * 2f - 1f) * rr;
                    if (dx * dx + dz * dz <= rr * rr) Sample(new Vector3(L.holeX + dx, 0, L.holeZ + dz));
                }
            }
            else
            {
                float z1 = Mathf.Min(L.zFar, 60f);
                for (int zi = 0; zi < 60; zi++)
                for (int xi = 0; xi <= 40; xi++)
                {
                    float z = L.zNear + (z1 - L.zNear) * Mathf.Pow(zi / 59f, 1.5f);
                    Sample(new Vector3(-L.xLim + 2f * L.xLim * xi / 40f, 0, z));
                }
            }
            var keys = new HashSet<int>();
            var sb = new System.Text.StringBuilder();
            foreach (var kv in counts)
                if (kv.Value >= 0.02f * total)
                {
                    keys.Add(kv.Key);
                    sb.Append($" #{kv.Key:x6}");
                }

            // 2. painted water: those colours below the horizon, plus small or thin (<= 2 rows) specks inside it
            //    (painted dashes, glitter), which effects may run over
            int top = Mathf.Clamp(horizonRow, 0, H);
            for (int r = 0; r < top; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c;
                if ((mask[i] & MFront) == 0 && keys.Contains(Key(back[i]))) mask[i] |= MPainted;
            }
            var queue = new int[W * top + 1];
            for (int seed = 0; seed < W * top; seed++)
            {
                if ((mask[seed] & (MPainted | MFront | MSeen)) != 0) continue;
                int head = 0, tail = 0, rMin = int.MaxValue, rMax = -1, cMin = int.MaxValue, cMax = -1;
                bool touchesFront = false;
                queue[tail++] = seed;
                mask[seed] |= MSeen;
                while (head < tail)
                {
                    int i = queue[head++];
                    int r = i / W, c = i - r * W;
                    if (r < rMin) rMin = r;
                    if (r > rMax) rMax = r;
                    if (c < cMin) cMin = c;
                    if (c > cMax) cMax = c;
                    for (int d = 0; d < 4; d++)
                    {
                        int cc = c + (d == 0 ? -1 : d == 1 ? 1 : 0), rr = r + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (cc < 0 || cc >= W || rr < 0 || rr >= top) continue;
                        int j = rr * W + cc;
                        byte m = mask[j];
                        if ((m & MFront) != 0) touchesFront = true;
                        if ((m & (MPainted | MFront | MSeen)) != 0) continue;
                        mask[j] |= MSeen;
                        queue[tail++] = j;
                    }
                }
                if (tail <= 12 || (rMax - rMin < 2 && !touchesFront))
                    for (int q = 0; q < tail; q++) mask[queue[q]] |= MPainted;
                else if (tail <= 4000 && rMax < top - 4 && cMin > 0 && cMax < W - 1)
                    for (int q = 0; q < tail; q++) mask[queue[q]] |= MIsland;   // a painted rock / buoy standing in the water
            }

            // 3. play water: painted water inside the fishable area (the ice: inside the hole)
            int water = 0;
            float hr2 = L.holeR * L.holeR * 0.9f;
            for (int r = 0; r < top; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c;
                mask[i] &= unchecked((byte)~MSeen);
                if ((mask[i] & MPainted) == 0) continue;
                var s2 = new Vector2((c + 0.5f - halfW) / PixelView.PPU, (r + 0.5f - halfH) / PixelView.PPU);
                if (!P.ToPlane(s2, 0f, out var hit)) continue;
                bool inside = kind == Kind.Ice
                    ? (hit.x - L.holeX) * (hit.x - L.holeX) + (hit.z - L.holeZ) * (hit.z - L.holeZ) <= hr2
                    : hit.z >= L.zNear && hit.z <= L.zFar && Mathf.Abs(hit.x) <= L.xLim;
                if (inside)
                {
                    mask[i] |= MWater;
                    water++;
                }
            }

            // 4. foam candidates (stream, sea)
            if (kind == Kind.Stream || kind == Kind.Sea) FindEdges(top);
            Debug.Log($"[WaterFx] {L.id}: {kind}, water colours{sb}, {water} water px, {edgeN} foam spots");
        }

        bool Front(int c, int r) => c >= 0 && c < W && r >= 0 && r < H && (mask[r * W + c] & MFront) != 0;

        // a painted obstacle next to the water (sea: only islands like the buoy, not the far water's painted patches)
        bool Obstacle(int c, int r) => c >= 0 && c < W && r >= 0 && r < horizonRow && (mask[r * W + c] & (MFront | MPainted)) == 0
                                       && (kind != Kind.Sea || (mask[r * W + c] & MIsland) != 0);

        void FindEdges(int top)
        {
            var pix = new List<int>();
            var kinds = new List<byte>();
            for (int r = 1; r < top - 1; r++)
            for (int c = 2; c < W - 2; c++)
            {
                int i = r * W + c;
                if ((mask[i] & MWater) == 0) continue;
                byte k = 0;
                if (Front(c, r - 1) || Front(c, r - 2)) k = 1;
                else if (Front(c - 1, r) || Front(c + 1, r) || Front(c, r + 1)) k = 2;
                else if (Obstacle(c - 1, r) || Obstacle(c + 1, r) || Obstacle(c, r - 1) || Obstacle(c, r + 1)) k = 3;
                if (k == 0) continue;
                pix.Add(i);
                kinds.Add(k);
            }
            const int Max = 520;
            int step = Mathf.Max(1, Mathf.CeilToInt(pix.Count / (float)Max));
            int n = (pix.Count + step - 1) / step;
            edgePix = new int[n];
            edgeX = new float[n];
            edgeZ = new float[n];
            edgeH = new float[n];
            edgeKind = new byte[n];
            edgeN = 0;
            for (int q = 0; q < pix.Count && edgeN < n; q += step)
            {
                int i = pix[q], r = i / W, c = i - r * W;
                var s2 = new Vector2((c + 0.5f - halfW) / PixelView.PPU, (r + 0.5f - halfH) / PixelView.PPU);
                if (!P.ToPlane(s2, 0f, out var hit)) continue;
                edgePix[edgeN] = i;
                edgeX[edgeN] = hit.x;
                edgeZ[edgeN] = hit.z;
                edgeH[edgeN] = Hash(i, 91);
                edgeKind[edgeN] = kinds[q];
                edgeN++;
            }
        }

        void ShowMask()
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++)
                px[i] = (mask[i] & MWater) != 0 ? new Color32(255, 0, 255, 90) : new Color32(0, 0, 0, 0);
            for (int e = 0; e < edgeN; e++)
                px[edgePix[e]] = edgeKind[e] == 3 ? new Color32(0, 255, 255, 230) : new Color32(255, 255, 0, 230);
            t.SetPixels32(px);
            t.Apply();
            var sr = new GameObject("WaterMask").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), PixelView.PPU);
            sr.sortingOrder = OrderWave - 1;
        }

        // ------------------------------------------------------------------ helpers
        static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u + 0x9E3779B9u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>
        /// Alpha in quarter steps (x the period look's fxAlpha): the effects blend in a few flat levels, never a smooth
        /// gradient.
        /// </summary>
        Color A(Color c, float a)
        {
            c.a = Mathf.Round(Mathf.Clamp01(a * fxAlpha) * 4f) / 4f;
            if (vivid && c.a > 0f) c.a = 1f;
            return c;
        }

        static bool vivid;

        float Fade(float z) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fade0, fade1, z));

        /// <summary>Stage-canvas position (pixels, fractional) and pixels per metre of a point on the water.</summary>
        void Proj(float x, float y, float z, out float c, out float r, out float ppm)
        {
            var q = P.ToPixel(new Vector3(x, y, z), out float d);
            c = halfW + q.x;
            r = halfH + q.y;
            ppm = L.focalPx / d;
        }

        bool WaterPx(int c, int r) => c >= vx0 && c < vx1 && r >= vy0 && r < vy1 && (mask[r * W + c] & MWater) != 0;

        bool IsWaterAt(float x, float z)
        {
            Proj(x, 0, z, out float c, out float r, out _);
            return WaterPx(Mathf.FloorToInt(c), Mathf.FloorToInt(r));
        }

        /// <summary>
        /// A water point hidden behind the painted front layer (a bank, a rock, the deck): Docs/obstacles_spec.md 4.5, a
        /// landing there perches on the bank. Every pixel within <paramref name="rPx"/> of it must be front (a small stamp on
        /// the water, a lily flower or a speck of duckweed, is no bank). Off the canvas (or without a mask): false.
        /// </summary>
        public bool BehindFront(float x, float z, int rPx = 2)
        {
            if (mask == null) return false;
            Proj(x, 0, z, out float cf, out float rf, out _);
            int c = Mathf.FloorToInt(cf), r = Mathf.FloorToInt(rf);
            if (c < 0 || c >= W || r < 0 || r >= H) return false;
            for (int dr = -rPx; dr <= rPx; dr++)
            for (int dc = -rPx; dc <= rPx; dc++)
            {
                int cc = Mathf.Clamp(c + dc, 0, W - 1), rr = Mathf.Clamp(r + dr, 0, H - 1);
                if ((mask[rr * W + cc] & MFront) == 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Whether the moving water (or the rod sweep) may carry a rig to (x, z) (Tackle.StepCurrent, Tackle.Sideways): in
        /// view (<see cref="InView"/>) and not behind the front layer (the stream's rocks, the tetrapods, the boat). True
        /// without a water mask.
        /// </summary>
        public bool DriftOpen(float x, float z)
        {
            if (mask == null) return true;
            if (!InView(x, z)) return false;
            Proj(x, 0, z, out float cf, out float rf, out _);
            return (mask[Mathf.FloorToInt(rf) * W + Mathf.FloorToInt(cf)] & MFront) == 0;
        }

        /// <summary>The water point (x, z) is in view: 6 px inside the pixel view's sides and top, above its bottom. True without a water mask.</summary>
        public bool InView(float x, float z)
        {
            if (mask == null) return true;
            Proj(x, 0, z, out float cf, out float rf, out _);
            int c = Mathf.FloorToInt(cf), r = Mathf.FloorToInt(rf);
            const int M = 6;
            return c >= vx0 + M && c < vx1 - M && r >= vy0 && r < vy1 - M;
        }

        /// <summary>The front layer covers this point of the pixel scene (scene units; false off the canvas or without a mask).</summary>
        public bool FrontAt(Vector2 scene)
        {
            if (mask == null) return false;
            int c = Mathf.FloorToInt(halfW + scene.x * PixelView.PPU), r = Mathf.FloorToInt(halfH + scene.y * PixelView.PPU);
            return c >= 0 && c < W && r >= 0 && r < H && (mask[r * W + c] & MFront) != 0;
        }

        bool RandomWaterPoint(float zMin, float zMax, float bias, out float x, out float z)
        {
            zMin = Mathf.Max(zMin, L.zNear);
            zMax = Mathf.Min(zMax, L.zFar);
            for (int i = 0; i < 8; i++)
            {
                z = Mathf.Lerp(zMin, zMax, Mathf.Pow(Random.value, bias));
                float half = Mathf.Min(L.xLim, P.VisibleHalfWidth(z, rtW));
                x = Random.Range(-half, half);
                if (IsWaterAt(x, z)) return true;
            }
            x = z = 0;
            return false;
        }

        // ------------------------------------------------------------------ pooled runs
        static Sprite Dot
        {
            get
            {
                if (dot != null) return dot;
                var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                t.SetPixel(0, 0, Color.white);
                t.Apply();
                // pivot at the bottom-left corner: placed on whole-pixel corners it covers exactly its pixels
                return dot = Sprite.Create(t, new Rect(0, 0, 1, 1), Vector2.zero, PixelView.PPU);
            }
        }

        SpriteRenderer NewSeg()
        {
            var sr = new GameObject("w").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = Dot;
            return sr;
        }

        void Seg(int c, int r, int len, Color col, int order)
        {
            if (used >= MaxSegs) return;
            SpriteRenderer sr;
            if (used < pool.Count) sr = pool[used];
            else pool.Add(sr = NewSeg());
            used++;
            if (!sr.enabled) sr.enabled = true;
            var tr = sr.transform;
            tr.localPosition = new Vector3((c - halfW) / PixelView.PPU, (r - halfH) / PixelView.PPU, 0);
            tr.localScale = new Vector3(len, 1, 1);
            sr.color = col;
            sr.sortingOrder = order;
        }

        /// <summary>Horizontal run of pixels c0..c1 on row r, cut to the play water.</summary>
        void Run(int r, int c0, int c1, Color col, int order)
        {
            if (col.a <= 0.01f || r < vy0 || r >= vy1) return;
            if (c0 < vx0) c0 = vx0;
            if (c1 >= vx1) c1 = vx1 - 1;
            int row = r * W, start = -1;
            for (int c = c0; c <= c1; c++)
            {
                if ((mask[row + c] & MWater) != 0)
                {
                    if (start < 0) start = c;
                }
                else if (start >= 0)
                {
                    Seg(start, r, c - start, col, order);
                    start = -1;
                }
            }
            if (start >= 0) Seg(start, r, c1 + 1 - start, col, order);
        }

        static int HalfWidth(int dy, int rx, int ry)
        {
            float k = dy / (ry + 0.5f);
            return Mathf.RoundToInt(rx * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k)));
        }

        /// <summary>1 px ellipse outline around (cx, cy); the near (lower) half in <paramref name="nearCol"/>.</summary>
        void Ellipse(int cx, int cy, float rx, float ry, Color col, Color nearCol, int order)
        {
            int Rx = Mathf.Max(1, Mathf.RoundToInt(rx)), Ry = Mathf.Max(1, Mathf.RoundToInt(ry));
            for (int dy = 0; dy <= Ry; dy++)
            {
                int xo = HalfWidth(dy, Rx, Ry);
                int inner = dy < Ry ? Mathf.Min(xo, HalfWidth(dy + 1, Rx, Ry) + 1) : 0;
                for (int s = 0; s < (dy == 0 ? 1 : 2); s++)
                {
                    int r = s == 0 ? cy + dy : cy - dy;
                    var cc = s == 0 ? col : nearCol;
                    if (inner == 0) Run(r, cx - xo, cx + xo, cc, order);
                    else
                    {
                        Run(r, cx - xo, cx - inner, cc, order);
                        Run(r, cx + inner, cx + xo, cc, order);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ pooled effect sprites
        /// <summary>An effect sprite from Sprites/World (null when missing: the caller falls back to runs).</summary>
        static Sprite FxS(string name)
        {
            if (fxSprites.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("Sprites/World/" + name);
            fxSprites[name] = s;
            return s;
        }

        static readonly Dictionary<string, Sprite[]> fxSeq = new Dictionary<string, Sprite[]>();

        /// <summary>
        /// The effect sprite <paramref name="prefix"/> + <paramref name="i"/> (i in 0 .. n - 1; null when missing), cached per
        /// prefix so no sprite name is built every frame (the prefixes are literals).
        /// </summary>
        static Sprite FxI(string prefix, int i, int n)
        {
            if (!fxSeq.TryGetValue(prefix, out var arr))
            {
                arr = new Sprite[n];
                for (int k = 0; k < n; k++) arr[k] = FxS(prefix + k);
                fxSeq[prefix] = arr;
            }
            return i >= 0 && i < arr.Length ? arr[i] : null;
        }

        static readonly string[] SargassumPre = { "fx_sargassum_s_f", "fx_sargassum_m_f", "fx_sargassum_l_f" };
        static readonly string[] FoamPatchPre = { "fx_foampatch_s_f", "fx_foampatch_m_f", "fx_foampatch_l_f" };
        static readonly string[] FleckPre = { "fx_fleck_1_f", "fx_fleck_2_f", "fx_fleck_3_f" };

        /// <summary>
        /// Draws an effect sprite centred at stage-canvas (c, r) (pixels, fractional; snapped: an even size on a pixel
        /// corner, an odd size on a pixel centre), unclipped. False when the pool is full or the sprite is missing.
        /// </summary>
        bool Spr(Sprite s, float c, float r, Color col, int order, bool flipX = false)
        {
            if (s == null || col.a <= 0.01f || sprUsed >= MaxSprs) return s != null;
            SpriteRenderer sr;
            if (sprUsed < sprPool.Count) sr = sprPool[sprUsed];
            else
            {
                sr = new GameObject("fx").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(transform, false);
                sprPool.Add(sr);
            }
            sprUsed++;
            if (!sr.enabled) sr.enabled = true;
            int w = Mathf.RoundToInt(s.rect.width), h = Mathf.RoundToInt(s.rect.height);
            float x = (w & 1) == 0 ? Mathf.Round(c) : Mathf.Floor(c) + 0.5f;
            float y = (h & 1) == 0 ? Mathf.Round(r) : Mathf.Floor(r) + 0.5f;
            sr.sprite = s;
            sr.flipX = flipX;
            sr.color = col;
            sr.sortingOrder = order;
            sr.transform.localPosition = new Vector3((x - halfW) / PixelView.PPU, (y - halfH) / PixelView.PPU, 0);
            return true;
        }

        /// <summary>A colour with a quarter-step alpha x fxAlpha (for tinted effect sprites).</summary>
        Color SprCol(Color c, float a) => A(c, a);

        /// <summary>Own-coloured debris (leaves, weed, sargassum): x the period look's actor tint (moonlit at night).</summary>
        Color DebrisCol(float a)
        {
            var t = stage.ActorTint;
            return A(new Color(t.r, t.g, t.b, 1f), a);
        }

        // ------------------------------------------------------------------ frame
        /// <summary>The part of the stage canvas the pixel view shows (its render texture, centred on the origin).</summary>
        void ViewBounds()
        {
            var pv = PixelView.Current;
            int w = 480, h = 270;
            if (pv != null && pv.Target != null)
            {
                w = pv.Target.width;
                h = pv.Target.height;
            }
            rtW = w;
            vx0 = Mathf.Max(0, Mathf.FloorToInt(halfW) - w / 2);
            vx1 = Mathf.Min(W, Mathf.FloorToInt(halfW) - w / 2 + w);
            vy0 = Mathf.Max(0, Mathf.FloorToInt(halfH) - h / 2);
            vy1 = Mathf.Min(H, Mathf.FloorToInt(halfH) - h / 2 + h);
        }

        void Update()
        {
            if (mask == null) return;
            float dt = Time.deltaTime, t = Time.time;
            ViewBounds();
            used = 0;
            sprUsed = 0;
            if (cur != null && kind == Kind.Ocean)
            {
                driftX = cur.Drift.x;
                driftZ = cur.Drift.y;
            }

            for (int i = 0; i < dashes.Length; i++) StepDash(i, dt);
            StepRings(dt);
            for (int i = 0; i < drift.Length; i++) StepDrifter(i, dt);
            if (hasSwell) DrawSwell(t);
            if (edgeN > 0) DrawFoam(t);
            for (int i = 0; i < dashes.Length; i++) DrawDash(ref dashes[i]);
            DrawRings();
            for (int i = 0; i < drift.Length; i++) DrawDrifter(ref drift[i], t);
            if (kind == Kind.Sea) DrawSea(dt, t);
            if (kind == Kind.Lake || kind == Kind.Swamp) DrawCatsPaw();
            DrawWake(dt);
            if (CurrentField.Vivid) DrawVivid();
            if (kind == Kind.Ocean) Bob(t);

            for (int i = used; i < prevUsed && i < pool.Count; i++) pool[i].enabled = false;
            prevUsed = used;
            for (int i = sprUsed; i < sprPrev && i < sprPool.Count; i++) sprPool[i].enabled = false;
            sprPrev = sprUsed;
        }

        // ------------------------------------------------------------------ dashes: wind ripples, flow, riffles, specks
        void SpawnDash(int i)
        {
            ref var d = ref dashes[i];
            d.age = 0;
            d.vx = d.vz = 0;
            d.k = 0f;
            bool ok;
            switch (kind)
            {
                case Kind.Lake:
                    ok = RandomWaterPoint(L.zNear + 1.5f, 55f, 1.35f, out d.x, out d.z);
                    d.len = Random.Range(0.25f, 0.55f);
                    d.vx = Random.Range(0.12f, 0.3f);       // a light breeze from the left
                    d.vz = Random.Range(-0.04f, 0.04f);
                    d.life = Random.Range(2.5f, 5f);
                    d.alpha = Random.Range(0.55f, 0.85f);
                    d.kind = Random.value < 0.6f ? DPair : DLight;
                    break;
                case Kind.Stream when i >= riffleFrom:
                    ok = false;
                    d.x = d.z = 0;
                    if (edgeN > 0 && Random.value < 0.65f)
                    {
                        // just downstream (nearer) of a rock
                        int e = Random.Range(0, edgeN);
                        if (edgeKind[e] != 3)
                        {
                            d.x = edgeX[e] + Random.Range(-0.35f, 0.35f);
                            d.z = edgeZ[e] - Random.Range(0.3f, 1.6f);
                            ok = IsWaterAt(d.x, d.z);
                        }
                    }
                    if (!ok) ok = RandomWaterPoint(L.zNear + 0.5f, 40f, 1.2f, out d.x, out d.z);
                    d.len = Random.Range(0.1f, 0.25f);
                    d.k = 5.5f;                               // the local current x 5.5 (StepDash)
                    d.vz = -Random.Range(2.6f, 3.6f);
                    d.life = Random.Range(0.3f, 0.6f);
                    d.alpha = Random.Range(0.7f, 1f);
                    d.kind = DRiffle;
                    break;
                case Kind.Stream:
                {
                    // 70 % in the fast lane, the rest anywhere; the local current x 3.2 carries them (StepDash)
                    ok = false;
                    d.x = d.z = 0f;
                    if (Random.value < 0.7f)
                        for (int k = 0; k < 6 && !ok; k++)
                        {
                            d.z = Mathf.Lerp(L.zNear + 0.5f, 65f, Mathf.Pow(Random.value, 1.1f));
                            d.x = CurrentField.LaneX(d.z) + Random.Range(-3.2f, 3.2f);
                            ok = IsWaterAt(d.x, d.z);
                        }
                    if (!ok) ok = RandomWaterPoint(L.zNear + 0.5f, 65f, 1.1f, out d.x, out d.z);
                    d.len = Random.Range(0.3f, 0.7f);
                    d.k = 3.2f * Random.Range(0.85f, 1.15f);
                    d.vz = -Random.Range(1.5f, 2.5f);         // downstream = towards the camera
                    d.life = Random.Range(1.4f, 3f);
                    float pick = Random.value;
                    d.kind = pick < 0.45f ? DPair : pick < 0.75f ? DLight : DDark;
                    // in a pocket behind a rock: dark, slow, curling dashes only (the eddy)
                    if (cur != null && cur.Zone(d.x, d.z) > 0) d.kind = DDark;
                    d.alpha = d.kind == DDark ? Random.Range(0.5f, 0.7f) : Random.Range(0.6f, 0.9f);
                    break;
                }
                case Kind.Sea:
                    // the tide's flow lines along x (drawn only while the water moves)
                    ok = RandomWaterPoint(6f, 45f, 1.1f, out d.x, out d.z);
                    d.len = Random.Range(0.6f, 1.4f);
                    d.k = 2.5f;
                    d.life = Random.Range(2f, 4f);
                    d.alpha = Random.Range(0.8f, 1f);
                    d.kind = DFlow;
                    break;
                case Kind.Swamp:
                    ok = RandomWaterPoint(L.zNear + 1f, 32f, 1.2f, out d.x, out d.z);
                    d.len = 0.05f;
                    float a = Random.Range(0f, Mathf.PI * 2f), v = Random.Range(0.03f, 0.08f);
                    d.vx = Mathf.Cos(a) * v + 0.03f;
                    d.vz = Mathf.Sin(a) * v;
                    d.life = Random.Range(14f, 30f);
                    d.alpha = Random.Range(0.65f, 0.9f);
                    d.kind = DSpeck;
                    break;
                default:
                    ok = false;
                    break;
            }
            if (!ok) d.life = Random.Range(0.2f, 0.6f);   // try again shortly
            d.alpha = ok ? d.alpha : 0f;
        }

        void StepDash(int i, float dt)
        {
            ref var d = ref dashes[i];
            d.age += dt;
            float vx = d.vx, vz = d.vz;
            if (cur != null)
            {
                if ((kind == Kind.Stream || kind == Kind.Sea) && d.k > 0f)
                {
                    // streaks, riffles and flow lines slide at the local current (x a readability gain)
                    var w = cur.Water(d.x, d.z);
                    vx = w.x * d.k;
                    vz = w.y * d.k;
                }
                else if (kind == Kind.Lake)
                {
                    // the breeze ripples hurry along in a gust
                    float g = 1f + 2f * cur.Gust01;
                    vx *= g;
                    vz *= g;
                }
                else if (kind == Kind.Swamp)
                {
                    // the specks drift with the wind
                    vx += cur.Wind.x;
                    vz += cur.Wind.y;
                }
            }
            d.x += vx * dt;
            d.z += vz * dt;
            if (d.age >= d.life || (d.alpha > 0f && !IsWaterAt(d.x, d.z) && d.age > 0.1f)) SpawnDash(i);
        }

        void DrawDash(ref Dash d)
        {
            if (d.alpha <= 0f) return;
            Proj(d.x, 0, d.z, out float cc, out float rr, out float ppm);
            float k = d.age / d.life;
            float env = d.kind == DSpeck ? Mathf.Clamp01(Mathf.Min(k, 1f - k) * 12f) : Mathf.Clamp01(Mathf.Min(k, 1f - k) * 3.5f);
            float a = d.alpha * env * Fade(d.z);
            // a surge passing: a brighter band sliding down the stream
            if (kind == Kind.Stream && cur != null && d.kind != DDark && cur.Surge(d.z) >= 1.2f) a += 0.25f * env;
            if (d.kind == DFlow)
            {
                DrawFlow(ref d, cc, rr, ppm, a);
                return;
            }
            if (a < 0.125f) return;
            int len = d.kind == DSpeck ? (ppm > 26f ? 2 : 1) : Mathf.Clamp(Mathf.RoundToInt(d.len * ppm), 1, maxDash);
            int c0 = Mathf.RoundToInt(cc - len * 0.5f), r = Mathf.FloorToInt(rr);
            switch (d.kind)
            {
                case DLight:
                    Run(r, c0, c0 + len - 1, A(lit, a), OrderWave);
                    break;
                case DDark:
                    Run(r, c0, c0 + len - 1, A(dark, a), OrderWave);
                    break;
                case DRiffle:
                    Run(r, c0, c0 + len - 1, A(lighter, a), OrderCrest);
                    break;
                case DPair:
                    // a tiny wave: lit crest over its shaded front
                    Run(r, c0, c0 + len - 1, A(lit, a), OrderCrest);
                    if (len >= 3) Run(r - 1, c0 + 1, c0 + len - 2, A(dark, a * 0.8f), OrderWave);
                    break;
                case DSpeck:
                    Run(r, c0, c0 + len - 1, A(speck, a), OrderWave);
                    break;
            }
        }

        // ------------------------------------------------------------------ rings: rises, bubbles, drips, the ice hole
        void AddRing(float x, float z, float delay, byte pre, float life, float r0, float r1, float alpha, float fallH = 0f)
        {
            for (int i = 0; i < rings.Length; i++)
            {
                if (rings[i].on) continue;
                rings[i] = new Ring { x = x, z = z, age = -delay, pre = delay, preKind = pre, life = life, r0 = r0, r1 = r1, alpha = alpha, fallH = fallH, on = true };
                return;
            }
        }

        void StepRings(float dt)
        {
            for (int i = 0; i < rings.Length; i++)
            {
                if (!rings[i].on) continue;
                rings[i].age += dt;
                if (rings[i].age >= rings[i].life) rings[i].on = false;
            }
            if (kind == Kind.Stream || kind == Kind.Sea || kind == Kind.Ocean || kind == Kind.None) return;
            ringTimer -= dt;
            if (ringTimer > 0f) return;
            float x, z;
            switch (kind)
            {
                case Kind.Lake:
                    // a fish rising: a small double ring
                    ringTimer = Random.Range(3f, 7f);
                    if (!RandomWaterPoint(L.zNear + 3f, 36f, 1.1f, out x, out z)) break;
                    AddRing(x, z, 0.12f, PreBubble, 1.6f, 0.05f, 0.5f, 0.75f);
                    AddRing(x, z, 0.55f, PreNone, 1.3f, 0.04f, 0.3f, 0.55f);
                    break;
                case Kind.Swamp:
                    // gas bubble: sits a moment, then pops into a small ring
                    ringTimer = Random.Range(2.5f, 6.5f);
                    if (!RandomWaterPoint(L.zNear + 2f, 24f, 1.2f, out x, out z)) break;
                    AddRing(x, z, Random.Range(0.5f, 1.1f), PreBubble, 1.1f, 0.03f, 0.32f, 0.6f);
                    if (Random.value < 0.35f)
                    {
                        float bx = x + Random.Range(-0.3f, 0.3f), bz = z + Random.Range(-0.2f, 0.2f);
                        if (IsWaterAt(bx, bz)) AddRing(bx, bz, Random.Range(1.2f, 1.8f), PreBubble, 0.9f, 0.03f, 0.2f, 0.4f);
                    }
                    break;
                case Kind.Ice:
                    ringTimer = Random.Range(2.5f, 5f);
                    x = L.holeX + Random.Range(-0.15f, 0.15f);
                    z = L.holeZ + Random.Range(-0.12f, 0.12f);
                    AddRing(x, z, 0f, PreNone, 1.3f, 0.04f, L.holeR * 0.6f, 0.9f);
                    AddRing(x, z, 0.4f, PreNone, 1f, 0.03f, L.holeR * 0.4f, 0.6f);
                    break;
                case Kind.Cave:
                    // a drip from the ceiling: the drop falls the last metre, then two faint rings
                    ringTimer = Random.Range(1.2f, 3.2f);
                    if (!RandomWaterPoint(L.zNear + 2f, 30f, 1.3f, out x, out z)) break;
                    AddRing(x, z, 0.45f, PreDrop, 1.4f, 0.03f, 0.5f, 0.75f, 1f);
                    AddRing(x, z, 0.8f, PreNone, 1.1f, 0.03f, 0.3f, 0.5f);
                    break;
            }
        }

        void DrawRings()
        {
            for (int i = 0; i < rings.Length; i++)
            {
                ref var g = ref rings[i];
                if (!g.on) continue;
                float fade = Fade(g.z);
                if (g.age < 0f)
                {
                    float q = 1f + g.age / Mathf.Max(0.01f, g.pre);   // 0 -> 1 through the pre-phase
                    if (g.preKind == PreBubble)
                    {
                        Proj(g.x, 0, g.z, out float bc, out float br, out float bppm);
                        int n = bppm > 30f ? 2 : 1;
                        int c0 = Mathf.RoundToInt(bc - n * 0.5f);
                        Run(Mathf.FloorToInt(br), c0, c0 + n - 1, A(lighter, g.alpha * fade * (q < 0.2f ? 0.5f : 1f)), OrderRing);
                    }
                    else if (g.preKind == PreDrop)
                    {
                        float y = g.fallH * (1f - q * q);
                        Proj(g.x, y, g.z, out float dc, out float dr, out _);
                        int c = Mathf.FloorToInt(dc), r = Mathf.FloorToInt(dr);
                        var col = A(lighter, 0.75f * fade);
                        Run(r, c, c, col, OrderRing);
                        Run(r + 1, c, c, A(lighter, 0.5f * fade), OrderRing);
                    }
                    continue;
                }
                float k = g.age / g.life;
                float rad = Mathf.Lerp(g.r0, g.r1, 1f - (1f - k) * (1f - k));
                float a = g.alpha * (1f - k) * fade;
                if (a < 0.125f) continue;
                var p = new Vector3(g.x, 0, g.z);
                Proj(g.x, 0, g.z, out float cc, out float rr, out float ppm);
                float rx = rad * ppm, ry = rx * Mathf.Clamp(P.Foreshorten(p) * 1.3f, 0.12f, 1f);
                int cx = Mathf.RoundToInt(cc), cy = Mathf.FloorToInt(rr);
                if (rx < 1.2f)
                {
                    Run(cy, cx, cx, A(lit, a), OrderRing);
                    continue;
                }
                Ellipse(cx, cy, rx, ry, A(lit, a), A(lit, a * 0.7f), OrderRing);
            }
        }

        // ------------------------------------------------------------------ swell: sea waves, ocean swells
        // (the ocean: the crest pattern lives in the drifting water, offset by the drift so far: driftX / driftZ)
        float CrestZ(int n, float x, float s) =>
            n * sw.lambda - s + driftZ + sw.wobA * Mathf.Sin((x - driftX) * sw.wobK + n * 1.7f) + sw.slope * (x - driftX);

        void DrawSwell(float t)
        {
            float s = t * sw.speed;
            float slack = sw.wobA + Mathf.Abs(sw.slope) * L.xLim + 1f;
            int n0 = Mathf.FloorToInt((sw.zMin + s - driftZ - slack) / sw.lambda), n1 = Mathf.CeilToInt((sw.zMax + s - driftZ + slack) / sw.lambda);
            for (int n = n0; n <= n1; n++)
            {
                float zb = n * sw.lambda - s + driftZ;
                float half = Mathf.Min(L.xLim, P.VisibleHalfWidth(Mathf.Max(zb, sw.zMin), rtW) + sw.cell);
                int j0 = Mathf.FloorToInt((-half - driftX) / sw.cell), j1 = Mathf.CeilToInt((half - driftX) / sw.cell);
                for (int j = j0; j <= j1; j++)
                {
                    if (Hash(n, j) > sw.fill) continue;
                    float len = Mathf.Lerp(sw.lenMin, sw.lenMax, Hash(n, j + 5000));
                    float xa = j * sw.cell + Hash(n, j + 9000) * Mathf.Max(0f, sw.cell - len) + driftX;
                    float z = CrestZ(n, xa + len * 0.5f, s);
                    if (z < sw.zMin || z > sw.zMax) continue;
                    // a crest grows out of the far water and flattens out right at the near edge
                    float a = Fade(z) * Mathf.Clamp01((z - sw.zMin) / 3f);
                    if (a <= 0.05f) continue;
                    // the stretch follows the crest's own curve: when its two ends fall on different rows it steps
                    Proj(xa, 0, CrestZ(n, xa, s), out float c0f, out float rA, out float ppm);
                    Proj(xa + len, 0, CrestZ(n, xa + len, s), out float c1f, out float rB, out _);
                    int ra = Mathf.FloorToInt(rA), rb = Mathf.FloorToInt(rB), c0 = Mathf.RoundToInt(c0f), c1 = Mathf.RoundToInt(c1f) - 1;
                    if (c1 < c0) c1 = c0;
                    int cm = (c0 + c1) / 2;
                    a *= 0.6f + 0.4f * Hash(n, j + 23000);   // some stretches of a crest catch more light
                    bool shade = z < sw.shadowZ;
                    if (ra == rb || c1 - c0 < 3) CrestPiece(ra, c0, c1, a, ppm, shade);
                    else
                    {
                        CrestPiece(ra, c0, cm, a, ppm, shade);
                        CrestPiece(rb, cm + 1, c1, a, ppm, shade);
                    }
                    if (!sw.whitecaps || Hash(n, j + 13000) > 0.16f) continue;
                    // whitecap: a short burst of foam riding the crest for about a second
                    float ph = Frac(t / 6.5f + Hash(n, j + 17000));
                    if (ph > 0.16f) continue;
                    float e = Mathf.Sin(ph / 0.16f * Mathf.PI);
                    int wl = Mathf.Clamp(Mathf.RoundToInt(0.8f * ppm * (0.4f + 0.6f * e)), 1, 6);
                    int wc = Mathf.RoundToInt(Mathf.Lerp(c0, c1, 0.2f + 0.6f * Hash(n, j + 21000)) - wl * 0.5f);
                    int r = wc + wl / 2 <= cm ? ra : rb;
                    Run(r, wc, wc + wl - 1, A(foam, e * a * 1.1f), OrderFoam);
                    if (ph < 0.08f && ppm > 8f) Run(r + 1, wc + wl / 2, wc + wl / 2, A(foam, e * a * 0.8f), OrderFoam);
                }
            }
        }

        /// <summary>One stretch of a crest: the lit crest row, (ocean) the fainter back slope above it, the shaded front below.</summary>
        void CrestPiece(int r, int c0, int c1, float a, float ppm, bool shade)
        {
            Run(r, c0, c1, A(crest, sw.crestA * a), OrderCrest);
            if (sw.whitecaps && ppm > 14f && c1 - c0 >= 6) Run(r + 1, c0 + 3, c1 - 3, A(lit, sw.crestA * a * 0.5f), OrderCrest);
            if (!shade || c1 - c0 < 2) return;
            Run(r - 1, c0 + 1, c1 - 1, A(dark, sw.shadowA * a), OrderWave);
            if (ppm > 26f && c1 - c0 >= 6) Run(r - 2, c0 + 3, c1 - 3, A(dark, sw.shadowA * a * 0.6f), OrderWave);
        }

        // ------------------------------------------------------------------ foam at the rocks / tetrapods
        void DrawFoam(float t)
        {
            float s = t * sw.speed;
            // the sea's tidal stream: more foam where it meets the tetrapods (the up-current side), less in their lee
            float tideS = 0f, flowX = 0f;
            if (kind == Kind.Sea && cur != null)
            {
                var w = cur.Water(0f, 20f);
                tideS = Mathf.Clamp01(w.magnitude / Mathf.Max(0.01f, cur.Ref));
                flowX = w.x;
            }
            for (int i = 0; i < edgeN; i++)
            {
                float x = edgeX[i], z = edgeZ[i], h = edgeH[i], f;
                if (hasSwell)
                {
                    // a crest arriving at this spot of the tetrapods: foam builds up, then drains away
                    int n = Mathf.RoundToInt((z + s - driftZ) / sw.lambda);
                    float dz = z - CrestZ(n, x, s);
                    f = dz < 0f ? 1f + dz / 0.4f : 1f - dz / 1.5f;
                    if (f <= 0f || Hash(i, n) > 0.85f) continue;
                    if (tideS > 0.15f && Mathf.Abs(flowX) > 0.01f)
                        f *= (x - spotMeanX) * flowX < 0f ? 1f + 0.5f * tideS : 1f - 0.2f * tideS;
                }
                else
                {
                    // running water: foam flickers against the rocks, mostly on their upstream side (and more in a surge)
                    float gate = edgeKind[i] == 1 ? 0.75f : edgeKind[i] == 2 ? 0.45f : 0.15f;
                    if (cur != null && cur.Surge(z) >= 1.2f) gate += 0.2f;
                    if (h > gate) continue;
                    float ph = Frac(t * (0.8f + h) + h * 13.7f);
                    if (ph > 0.5f) continue;
                    f = 1.2f * Mathf.Sin(ph / 0.5f * Mathf.PI);
                }
                f *= Fade(z);
                if (f < 0.125f) continue;
                int pix = edgePix[i], r = pix / W, c = pix - r * W;
                float hl = Hash(i, 7);
                int len = hl < 0.25f ? 3 : hl < 0.6f ? 2 : 1;
                Run(r, c, c + len - 1, A(foam, f), OrderFoam);
            }
        }

        // ------------------------------------------------------------------ the sea: the tide's flow lines
        /// <summary>A flow line (fx_flow_&lt;len&gt;_&lt;strength&gt;, flowing right, flipped for left): alpha 0.35 + 0.45 s, off below s 0.15.</summary>
        void DrawFlow(ref Dash d, float cc, float rr, float ppm, float a)
        {
            if (cur == null) return;
            float s = Mathf.Clamp01(GameClock.Tide.S * CurrentField.Mult);
            if (s < 0.15f) return;
            a *= 0.35f + 0.45f * s;
            if (a < 0.125f) return;
            var w = cur.Water(d.x, d.z);
            Proj(d.x + w.x, 0, d.z + w.y, out float c2, out _, out _);
            bool left = c2 < cc;
            int lenPx = Mathf.Clamp(Mathf.RoundToInt(d.len * ppm), 2, 16);
            if (!IsWaterAt(d.x - d.len * 0.5f, d.z) || !IsWaterAt(d.x + d.len * 0.5f, d.z)) return;
            string pre = lenPx <= 5 ? "fx_flow_4_" : lenPx <= 8 ? "fx_flow_7_" : lenPx <= 12 ? "fx_flow_10_" : "fx_flow_14_";
            int str = s < 0.4f ? 1 : s < 0.75f ? 2 : 3;
            if (Spr(FxI(pre, str, 4), cc, rr, SprCol(lit, a), OrderCrest, left)) return;
            int c0 = Mathf.RoundToInt(cc - lenPx * 0.5f);
            Run(Mathf.FloorToInt(rr), c0, c0 + lenPx - 1, A(lit, a), OrderCrest);
        }

        // ------------------------------------------------------------------ the sea: slicks, weed, the wet band, the buoy
        void InitSea()
        {
            float sum = 0f;
            for (int i = 0; i < edgeN; i++) sum += edgeX[i];
            spotMeanX = edgeN > 0 ? sum / edgeN : 0f;
            // one in five foam spots under a tetrapod carries a weed tuft (on the tetrapod, just above the water)
            var tufts = new List<int>();
            for (int i = 0; i < edgeN; i++)
            {
                if (edgeKind[i] == 3) continue;
                int pix = edgePix[i], r = pix / W, c = pix - r * W;
                if (!Front(c, r + 1) || Hash(i, 3) >= 0.2f) continue;
                tufts.Add(pix);
            }
            tuftPix = tufts.ToArray();
            // the wet band: the tetrapods' waterline in front of the water (a front pixel right above a water pixel), z < 20
            var rows = new SortedDictionary<int, List<int>>[3];
            for (int k = 0; k < 3; k++) rows[k] = new SortedDictionary<int, List<int>>();
            int top = Mathf.Clamp(horizonRow, 0, H - 4);
            for (int r = 0; r < top; r++)
            for (int c = 0; c < W; c++)
            {
                if ((mask[r * W + c] & MWater) == 0 || !Front(c, r + 1)) continue;
                var s2 = new Vector2((c + 0.5f - halfW) / PixelView.PPU, (r + 0.5f - halfH) / PixelView.PPU);
                if (!P.ToPlane(s2, 0f, out var hit) || hit.z >= 20f) continue;
                for (int k = 0; k < 3; k++)
                {
                    if (!Front(c, r + 1 + k)) break;
                    if (!rows[k].TryGetValue(r + 1 + k, out var list)) rows[k][r + 1 + k] = list = new List<int>();
                    list.Add(c);
                }
            }
            // one overlay per row above the waterline (a sprite each: the band grows a row at a time as the tide falls)
            var wet = new Color32(5, 26, 10, 64);   // (0.02, 0.1, 0.04) at 0.25: the tetrapod x ~0.75, a touch greener
            for (int k = 0; k < 3; k++)
            {
                if (rows[k].Count == 0) continue;
                var px = new Color32[W * H];
                foreach (var kv in rows[k])
                    foreach (int c in kv.Value) px[kv.Key * W + c] = wet;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(px);
                tex.Apply(false);
                var sr = new GameObject("WetBand" + k).AddComponent<SpriteRenderer>();
                sr.transform.SetParent(transform, false);
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), PixelView.PPU);
                sr.sortingOrder = OrderOverFront;
                sr.enabled = false;
                wetBand[k] = sr;
                foreach (var l in rows[k].Values) wetCount[k] += l.Count;
            }
            // the buoy: below its lamp (the night look's flash1 light) the first water pixel is its waterline
            foreach (var lk in stage.Looks)
                foreach (var li in lk.lights)
                {
                    if (li.blink != "flash1" || buoyC >= 0) continue;
                    int c = Mathf.FloorToInt(li.x), r0 = H - 1 - Mathf.FloorToInt(li.y);
                    for (int r = r0; r > r0 - 40 && r >= 0; r--)
                        if (c >= 0 && c < W && (mask[r * W + c] & MWater) != 0) { buoyC = c; buoyR = r; break; }
                }
            Debug.Log($"[WaterFx] sea: {tuftPix.Length} weed tufts, wet band {wetCount[0]}/{wetCount[1]}/{wetCount[2]} px, buoy {(buoyC >= 0 ? $"({buoyC},{buoyR})" : "none")}");
        }

        void DrawSea(float dt, float t)
        {
            var tide = GameClock.Tide;
            float s = Mathf.Clamp01(tide.S * CurrentField.Mult);
            var w = cur != null ? cur.Water(0f, 20f) : Vector2.zero;
            Proj(0f, 0f, 20f, out float c0, out _, out _);
            Proj(w.x, 0f, 20f + w.y, out float c1, out _, out _);
            bool flowLeft = c1 < c0;

            // slack water: glassy slicks fade in (and out again as the stream picks up)
            slickA = Mathf.MoveTowards(slickA, s < 0.3f ? 1f : 0f, dt / 3f);
            if (slickA <= 0f) slicksPlaced = false;
            else
            {
                if (!slicksPlaced)
                {
                    slicksPlaced = true;
                    for (int i = 0; i < slicks.Length; i++)
                    {
                        RandomWaterPoint(8f, 40f, 1.2f, out float x, out float z);
                        slicks[i] = new Slick { x = x, z = z, big = Random.value < 0.5f };
                    }
                }
                foreach (var sl in slicks)
                {
                    if (!IsWaterAt(sl.x, sl.z)) continue;
                    Proj(sl.x, 0f, sl.z, out float c, out float r, out float ppm);
                    var spr = FxS(sl.big && ppm >= 10f ? "fx_slick_40" : "fx_slick_24");
                    if (!Spr(spr, c, r, SprCol(lighter, slickA * Fade(sl.z)), OrderWave))
                    {
                        int len = Mathf.Clamp(Mathf.RoundToInt(2f * ppm), 3, 30), cc = Mathf.RoundToInt(c - len * 0.5f), rr = Mathf.FloorToInt(r);
                        for (int k = 0; k < 3; k++) Run(rr + k - 1, cc + k, cc + len - 1 - k, A(lighter, 0.25f * slickA), OrderWave);
                    }
                }
            }

            // weed on the tetrapods: upright at slack, the top leaning with the flow, combed flat in a strong one
            int lean = s < 0.3f ? 0 : s < 0.7f ? 1 : 2;
            var tuft = FxI("fx_tuft_", lean, 3);
            var tc = DebrisCol(1f);
            foreach (int pix in tuftPix)
            {
                int r = pix / W, c = pix - r * W;
                if (c < vx0 || c >= vx1 || r < vy0 || r >= vy1 - 2) continue;
                // (the root, column 1 of 4 on the bottom row, on the pixel above the water)
                if (tuft != null) Spr(tuft, flowLeft ? c : c + 1, r + 2, tc, OrderOverFront, flowLeft);
                else
                {
                    Seg(c, r + 1, 1, new Color(0.235f * tc.r, 0.353f * tc.g, 0.173f * tc.b, 1f), OrderOverFront);
                    Seg(c + (lean == 0 ? 0 : flowLeft ? -1 : 1), r + 2, 1, new Color(0.353f * tc.r, 0.478f * tc.g, 0.227f * tc.b, 1f), OrderOverFront);
                }
            }

            // the tide's wet band on the near tetrapods: 0 rows at high water .. 3 at low water, darker and greener
            int n = Mathf.Clamp(Mathf.RoundToInt(3f * (1f - tide.H) / 2f), 0, 3);
            for (int k = 0; k < 3; k++)
                if (wetBand[k] != null && wetBand[k].enabled != k < n) wetBand[k].enabled = k < n;

            // the buoy's wake, trailing down-current
            if (buoyC >= 0 && s >= 0.1f)
            {
                int f = (int)(t * 8f) % 4;
                var spr = FxI("fx_wake_side_s_f", f, 4);
                if (!Spr(spr, buoyC + 0.5f, buoyR + 0.5f, SprCol(lit, 0.5f * s), OrderCrest, flowLeft))
                {
                    int dir = flowLeft ? -1 : 1, len = 3 + Mathf.RoundToInt(3f * s);
                    Run(buoyR + 1, buoyC + dir * 2, buoyC + dir * (1 + len), A(lit, 0.5f * s), OrderCrest);
                    Run(buoyR - 1, buoyC + dir * 2, buoyC + dir * (1 + len), A(lit, 0.5f * s), OrderCrest);
                }
            }
        }

        // ------------------------------------------------------------------ drifting debris: stream leaves / flecks, ocean flotsam
        void SpawnDrifter(int i, bool initial)
        {
            ref var d = ref drift[i];
            d.on = false;
            d.age = 0f;
            d.phase = Random.Range(0f, 10f);
            if (kind == Kind.Stream)
            {
                d.kind = i < 6 ? (byte)Random.Range(0, 3) : KFleck;
                for (int k = 0; k < 8 && !d.on; k++)
                {
                    d.z = initial ? Random.Range(L.zNear + 2f, 65f) : Random.Range(45f, 65f);
                    d.x = CurrentField.LaneX(d.z) + Random.Range(-1.6f, 1.6f);
                    d.on = IsWaterAt(d.x, d.z);
                }
            }
            else if (kind == Kind.Ocean)
            {
                float r = Random.value;
                d.kind = r < 0.5f ? KSargassum : r < 0.7f ? KDriftwood : KFoamPatch;
                d.size = (byte)Random.Range(0, 3);
                var w = cur != null ? cur.Water(0f, 0f) : Vector2.right;
                for (int k = 0; k < 8 && !d.on; k++)
                {
                    if (initial) d.on = RandomWaterPoint(4f, 40f, 1.2f, out d.x, out d.z);
                    else
                    {
                        // up-drift, at the edge of the view
                        d.z = Random.Range(4f, 40f);
                        float half = P.VisibleHalfWidth(d.z, rtW);
                        d.x = Mathf.Abs(w.x) >= 0.01f ? -Mathf.Sign(w.x) * (half + 0.5f) : Random.Range(-half, half);
                        d.on = true;
                    }
                }
            }
        }

        void StepDrifter(int i, float dt)
        {
            ref var d = ref drift[i];
            if (!d.on)
            {
                d.age += dt;
                if (d.age > 0.5f) SpawnDrifter(i, false);
                return;
            }
            d.age += dt;
            var w = cur != null ? cur.Water(d.x, d.z) : Vector2.zero;
            d.x += w.x * dt;
            d.z += w.y * dt;
            bool gone;
            if (kind == Kind.Stream) gone = d.z < L.zNear + 0.5f || (d.age > 0.5f && !IsWaterAt(d.x, d.z));
            else
            {
                float half = P.VisibleHalfWidth(d.z, rtW);
                gone = Mathf.Abs(d.x) > half + 2f || d.z < L.zNear || d.z > 45f;
            }
            if (gone) SpawnDrifter(i, false);
        }

        void DrawDrifter(ref Drifter d, float t)
        {
            if (!d.on) return;
            float x = d.x;
            if (kind == Kind.Stream) x += 0.1f * Mathf.Sin(2f * Mathf.PI * 1.3f * t + d.phase);   // bobbing along
            if (!IsWaterAt(x, d.z)) return;
            Proj(x, 0f, d.z, out float c, out float r, out float ppm);
            float fade = Fade(d.z);
            if (fade < 0.2f) return;
            bool near = ppm >= 18f;
            switch (d.kind)
            {
                case KLeafYellow:
                case KLeafBrown:
                case KLeafGreen:
                {
                    string pre = d.kind == KLeafYellow ? (near ? "fx_leaf_yellow_f" : "fx_leaf_yellow_s_f")
                        : d.kind == KLeafBrown ? (near ? "fx_leaf_brown_f" : "fx_leaf_brown_s_f") : (near ? "fx_leaf_green_f" : "fx_leaf_green_s_f");
                    var spr = near ? FxI(pre, (int)(t * 3f + d.phase) % 4, 4) : FxI(pre, (int)(t * 1.5f + d.phase) % 2, 2);
                    if (!Spr(spr, c, r, DebrisCol(fade), OrderCrest))
                    {
                        var lc = d.kind == KLeafYellow ? new Color32(0xc8, 0xa0, 0x40, 0xff) : d.kind == KLeafBrown ? new Color32(0x8a, 0x6a, 0x2c, 0xff) : new Color32(0x6a, 0x8a, 0x3a, 0xff);
                        int cc = Mathf.RoundToInt(c), rr = Mathf.FloorToInt(r);
                        Run(rr, cc, cc, A(lc, fade), OrderCrest);
                        Run(rr, cc + 1, cc + 1, A((Color)lc * 0.8f, fade), OrderCrest);
                    }
                    break;
                }
                case KFleck:
                {
                    int size = ppm < 12f ? 1 : ppm < 20f ? 2 : 3;
                    var spr = FxI(FleckPre[size - 1], (int)(t / 0.4f + d.phase) % 2, 2);
                    if (!Spr(spr, c, r, SprCol(foam, 0.9f * fade), OrderFoam))
                    {
                        int cc = Mathf.RoundToInt(c), rr = Mathf.FloorToInt(r);
                        Run(rr, cc, cc + (size > 1 ? 1 : 0), A(foam, 0.9f * fade), OrderFoam);
                    }
                    break;
                }
                default:
                {
                    // ocean flotsam: sargassum, a plank, a foam patch
                    int sz = d.size == 0 || ppm < 10f ? 0 : d.size == 1 || ppm < 20f ? 1 : 2;   // s / m / l
                    int f = (int)(t / 0.7f + d.phase) % 2;
                    Sprite spr = d.kind == KSargassum ? FxI(SargassumPre[sz], f, 2) : d.kind == KDriftwood ? FxS("fx_driftwood") : FxI(FoamPatchPre[sz], f, 2);
                    var col = d.kind == KFoamPatch ? SprCol(foam, 0.85f * fade) : DebrisCol(fade);
                    if (!Spr(spr, c, r, col, d.kind == KFoamPatch ? OrderFoam : OrderCrest))
                    {
                        int n = 3 + d.size, cc = Mathf.RoundToInt(c - n * 0.5f), rr = Mathf.FloorToInt(r);
                        var fc = d.kind == KFoamPatch ? A(foam, 0.85f * fade) : A(new Color32(0xa8, 0x8a, 0x3a, 0xff), fade);
                        Run(rr, cc, cc + n - 1, fc, OrderCrest);
                        if (n > 3) Run(rr + 1, cc + 1, cc + n - 2, fc, OrderCrest);
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ lake / swamp: a gust's cat's paw
        void DrawCatsPaw()
        {
            if (cur == null || cur.GustT < 0f)
            {
                pawGustT = -1f;
                return;
            }
            bool lake = kind == Kind.Lake;
            if (pawGustT < 0f || cur.GustT < pawGustT)
            {
                // a new gust: its patch comes in from the up-wind edge of the view
                pawZ0 = Random.Range(L.zNear + 6f, lake ? 32f : 20f);
                float half = Mathf.Min(L.xLim, P.VisibleHalfWidth(pawZ0, rtW));
                pawX0 = -Mathf.Sign(cur.GustDir.x == 0f ? 1f : cur.GustDir.x) * half * 0.85f;
            }
            pawGustT = cur.GustT;
            float e = cur.Gust01;
            if (e < 0.05f) return;
            float speed = lake ? 2.5f : 1.5f, radius = lake ? 8f : 5f;
            var ctr = new Vector2(pawX0, pawZ0) + cur.GustDir * (speed * cur.GustT);
            // the patch's ruffled water (fx_catspaw: dark ripples with glints, tinted lit), a few across it
            for (int i = 0; i < 4; i++)
            {
                var o = paw[i];
                float x = ctr.x + o.x * radius * 0.7f, z = ctr.y + o.y * radius * 0.7f;
                if (z < L.zNear + 2f || !IsWaterAt(x, z)) continue;
                float a = e * Fade(z);
                if (a < 0.125f) continue;
                Proj(x, 0f, z, out float pc, out float pr, out _);
                Spr(FxI(lake ? "fx_catspaw_" : "fx_catspaw_s_", i & 1, 2), pc, pr, SprCol(lit, a), OrderWave);
            }
            foreach (var o in paw)
            {
                float x = ctr.x + o.x * radius, z = ctr.y + o.y * radius;
                if (z < L.zNear + 1f) continue;
                float a = 0.55f * e * Fade(z);
                if (a < 0.125f) continue;
                Proj(x, 0f, z, out float c, out float r, out float ppm);
                int len = Mathf.Clamp(Mathf.RoundToInt(o.z * ppm), 1, maxDash);
                int c0 = Mathf.RoundToInt(c - len * 0.5f);
                Run(Mathf.FloorToInt(r), c0, c0 + len - 1, A(dark, a), OrderWave);
            }
        }

        // ------------------------------------------------------------------ the float's wake
        /// <summary>
        /// A float moving through the water (dragged by a bowed line, wound in, pushed by a gust; in a fight dragged across by
        /// the fish's run on the line or held against the stream: Tackle.WakeNow) trails a V.
        /// </summary>
        void DrawWake(float dt)
        {
            var tk = Rig;
            if (tk == null || L.IsIce || !tk.WakeNow(out var s, out var rel)) return;
            Proj(s.x, 0f, s.z, out float c, out float r, out float ppm);
            Proj(s.x + rel.x, 0f, s.z + rel.y, out float c2, out float r2, out _);
            float dx = c2 - c, dy = r2 - r;
            string pre;
            bool flip = false, big = ppm >= 18f;
            if (Mathf.Abs(dy) >= Mathf.Abs(dx)) pre = dy > 0f ? (big ? "fx_wake_down_f" : "fx_wake_down_s_f") : (big ? "fx_wake_up_f" : "fx_wake_up_s_f");
            else
            {
                pre = big ? "fx_wake_side_f" : "fx_wake_side_s_f";
                flip = dx > 0f;
            }
            int f = (int)(Time.time * 8f) % 4;
            var spr = FxI(pre, f, 4);
            if (Spr(spr, c, r, SprCol(lit, 0.75f), OrderWake, flip)) return;
            // (no sprite: a V of two dashes behind the float, opening as it goes)
            wakeT += dt;
            int open = 1 + (int)(Mathf.Repeat(wakeT, 0.6f) / 0.1f);
            var back = new Vector2(-dx, -dy).normalized;
            int bc = Mathf.RoundToInt(c + back.x * 2f), br = Mathf.FloorToInt(r + back.y * 2f);
            Run(br + open / 2, bc - open, bc - open + 1, A(lit, 0.75f), OrderWake);
            Run(br - open / 2, bc + open - 1, bc + open, A(lit, 0.75f), OrderWake);
        }

        // ------------------------------------------------------------------ -fkcurrentvivid
        /// <summary>The current field as arrows every 2 m in x and 4 m in z (stream lane red, pockets cyan, eddies magenta; sea / ocean yellow).</summary>
        void DrawVivid()
        {
            if (cur == null || !cur.Moving) return;
            for (float z = L.zNear + 2f; z <= Mathf.Min(L.zFar, 44f); z += 4f)
            {
                float half = Mathf.Min(L.xLim, P.VisibleHalfWidth(z, rtW));
                float step = Mathf.Max(2f, half / 8f);
                for (float x = -Mathf.Floor(half / step) * step; x <= half; x += step)
                {
                    if (!IsWaterAt(x, z)) continue;
                    var w = cur.Water(x, z);
                    float sp = w.magnitude;
                    if (sp < 0.005f) continue;
                    int zone = cur.Zone(x, z);
                    var col = kind != Kind.Stream ? Color.yellow : zone == 2 ? Color.magenta : zone == 1 ? Color.cyan : Color.red;
                    float len = 0.6f + 1.2f * Mathf.Min(sp / cur.Ref, 1.5f);
                    var dn = w / sp;
                    Proj(x, 0f, z, out float ca, out float ra, out _);
                    Proj(x + dn.x * len, 0f, z + dn.y * len, out float cb, out float rb, out _);
                    int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(Mathf.Abs(cb - ca), Mathf.Abs(rb - ra))));
                    for (int k = 0; k <= n; k++)
                    {
                        float u = k / (float)n;
                        Seg(Mathf.FloorToInt(Mathf.Lerp(ca, cb, u)), Mathf.FloorToInt(Mathf.Lerp(ra, rb, u)), 1, k == n ? Color.white : col, OrderOverFront);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ ocean: the boat rides the swell
        void Bob(float t)
        {
            // swell height under the boat (z ~ 0): +1 on a crest, -1 in a trough; one pixel either way, a whole swell period long
            float phase = Frac(t * sw.speed / sw.lambda);
            int px = Mathf.RoundToInt(Mathf.Cos(phase * Mathf.PI * 2f));
            var bob = new Vector2(0f, px / (float)PixelView.PPU);
            if (bob == DeckBob) return;
            DeckBob = bob;
            stage.ApplyDeckBob(bob);
        }
    }
}
