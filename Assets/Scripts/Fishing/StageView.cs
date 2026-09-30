using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Builds a stage in the 2D pixel scene: the Blender back/front layers plus ambient life
    /// (drifting clouds, birds, water glints, fireflies, snow, cave motes) and the water movement (<see cref="WaterFx"/>).
    /// <para>Time of day (Docs/time_currents_spec.md 2, 5): every stage has one render per period (새벽 · 낮 · 저녁 · 밤,
    /// Sprites/Stages/&lt;stage&gt;_&lt;period&gt;_back / _front, falling back to &lt;stage&gt;_back / _front) and a look json
    /// (<see cref="PeriodLook"/>). The layers follow the game clock's blend (<see cref="GameClock.Look"/>): the outgoing
    /// period's layer draws normally, the incoming one over it through the PeriodDissolve material (an ordered 4x4
    /// dither, 1/16 of the pixels at a time). The blended look (<see cref="Now"/>) sets the water tint, the WaterFx
    /// colours, the glints, birds and fireflies, the actor layers' rim / key light / tint and the animated lights.</para>
    /// <para>The moving water (spec 8): <see cref="Current"/>, ticked here.</para>
    /// </summary>
    public class StageView : MonoBehaviour
    {
        public const int OrderBack = 0, OrderCloud = 1, OrderBird = 2, OrderFront = 40, OrderLight = 41;

        public StageDef Def { get; private set; }
        public StageLayout L { get; private set; }
        public Persp P { get; private set; }
        public Color WaterTint { get; private set; }
        public Color WaterDeep { get; private set; }

        /// <summary>Per-stage water movement (waves, ripples, foam), see <see cref="WaterFx"/>.</summary>
        public WaterFx Water { get; private set; }

        /// <summary>The stage's current and wind (Docs/time_currents_spec.md 8).</summary>
        public CurrentField Current { get; private set; }

        /// <summary>The stage's obstacles (Docs/obstacles_spec.md): empty without data or with -fkobstacles off.</summary>
        public Obstacles Obstacles { get; private set; }

        /// <summary>The stage's four period looks (by <see cref="Period"/>).</summary>
        public PeriodLook[] Looks { get; private set; }
        /// <summary>The blended look shown now.</summary>
        public LookNow Now { get; private set; }
        /// <summary>Multiplies the actors, the float, the line (night: moonlit blue).</summary>
        public Color ActorTint => Now.ActorTint;

        /// <summary>
        /// Swell bob of the boat deck (ocean only, zero elsewhere): world units, whole pixels only (0 or +-1/16 in y),
        /// following the swell under the boat, very slowly (one swell period ~10 s). Already applied to the front layer
        /// (the boat); anything standing on the deck (the angler, the rod, the line's rod end) should add it to its
        /// position as well so it stays on the deck.
        /// </summary>
        public Vector2 DeckBob { get; private set; }

        SpriteRenderer backA, backB, frontA, frontB;
        /// <summary>The front layer (the outgoing period's; the occlusion watch reads where it is drawn).</summary>
        internal SpriteRenderer FrontA => frontA;
        readonly Sprite[] backs = new Sprite[4], fronts = new Sprite[4];
        Material dissolve;
        static readonly int F16Id = Shader.PropertyToID("_F16");
        PeriodBlend shown;
        bool lookInit;
        float appliedF = -1f;
        const float BZ = -0.01f;   // the incoming layer draws over the outgoing one (same sorting order, nearer the camera)

        readonly List<Transform> clouds = new List<Transform>();
        readonly List<Glint> glints = new List<Glint>();
        readonly List<Mote> motes = new List<Mote>();
        readonly List<Lamp> lamps = new List<Lamp>();
        Sprite[] birdFrames;
        float birdTimer = 3f;
        float horizonY;
        int glintOn = 26;
        bool anyFireflies;

        class Glint { public SpriteRenderer sr; public float t, life; }
        class Mote { public SpriteRenderer sr; public Vector2 pos, vel; public float phase; public Vector3 world; public Color col; public string kind; }
        class Lamp { public SpriteRenderer sr; public float x, y, r; public string blink; public bool[] inPeriod = new bool[4]; }

        public static StageView Build(string stageId, Transform parent = null)
        {
            var go = new GameObject("Stage_" + stageId);
            if (parent) go.transform.SetParent(parent, false);
            var v = go.AddComponent<StageView>();
            v.Init(stageId);
            return v;
        }

        void Init(string id)
        {
            Def = GameDatabase.GetStage(id);
            L = Art.Layout(id);
            P = new Persp(L);
            WaterTint = Art.Hex(L.waterTint);
            WaterDeep = Art.Hex(L.waterDeep);
            StageLayout.TideOffset = 0f;
            Current = new CurrentField(L);
            Obstacles = FishingKing.Obstacles.Load(L);
            if (Current.K == CurrentField.Kind.Stream) CurrentField.AlignRocks(Obstacles.PocketRocks());
            horizonY = L.focalPx * Mathf.Tan(L.pitch * Mathf.Deg2Rad) / PixelView.PPU;
            LoadPeriods(id);
            backA = Layer(backs[0], OrderBack, "Back");
            backB = Layer(backs[0], OrderBack, "BackIn");
            frontA = Layer(fronts[0], OrderFront, "Front");
            frontB = Layer(fronts[0], OrderFront, "FrontIn");
            backB.transform.localPosition = new Vector3(0f, 0f, BZ);
            frontB.transform.localPosition = new Vector3(0f, 0f, BZ);
            dissolve = DissolveMaterial();
            if (dissolve != null) backB.sharedMaterial = frontB.sharedMaterial = dissolve;
            backB.enabled = frontB.enabled = false;
            // what is drawn over the front layer is hidden where the front layer is nearer (one map for every period: bound
            // once for all four, so a cross-fade never re-binds)
            FrontOcclusion.Bind(this, id, fronts);
            PlaceOcclusion();
            if (L.clouds) SpawnClouds();
            birdFrames = MakeBird();
            for (int i = 0; i < 26; i++) glints.Add(NewGlint());
            anyFireflies = L.fireflies;
            foreach (var lk in Looks) anyFireflies |= lk.fireflies;
            if (anyFireflies) for (int i = 0; i < 22; i++) motes.Add(NewMote(new Color(0.85f, 1f, 0.45f), true));
            if (L.sparkles) for (int i = 0; i < 30; i++) motes.Add(NewMote(new Color(0.45f, 0.95f, 1f), true, "Sparkle"));
            if (L.snow) for (int i = 0; i < 70; i++) motes.Add(NewMote(Color.white, false));
            BuildLamps();
            Water = WaterFx.Create(this);
            Sfx.Ambience(L.ambient);
            ApplyLook(true);
        }

        void OnDestroy()
        {
            StageLayout.TideOffset = 0f;
            Sfx.AmbienceVolume(1f);
            if (dissolve != null) Destroy(dissolve);
            FrontOcclusion.Unbind(this);
        }

        /// <summary>The front layer's rect in the world (the deck's bob included) for the occlusion test (<see cref="FrontOcclusion"/>).</summary>
        void PlaceOcclusion()
        {
            if (frontA == null || frontA.sprite == null) return;
            var b = frontA.sprite.bounds;
            var t = frontA.transform;
            Vector2 min = t.TransformPoint(b.min), max = t.TransformPoint(b.max);
            FrontOcclusion.Place(this, min, max - min);
        }

        // ------------------------------------------------------------------ period layers
        void LoadPeriods(string id)
        {
            Looks = new PeriodLook[4];
            var back0 = Art.Stage(id + "_back");
            var front0 = Art.Stage(id + "_front");
            for (int i = 0; i < 4; i++)
            {
                var p = (Period)i;
                Looks[i] = PeriodLook.Load(id, p);
                string n = id + "_" + GameClock.Id(p);
                var b = Resources.Load<Sprite>("Sprites/Stages/" + n + "_back");
                var f = Resources.Load<Sprite>("Sprites/Stages/" + n + "_front");
                if (b == null || f == null) Debug.Log($"[Period] {n} render missing: native art");
                backs[i] = b != null ? b : back0;
                fronts[i] = f != null ? f : front0;
            }
        }

        static Material dissolveBase;
        static bool dissolveTried;

        /// <summary>The PeriodDissolve material (Resources/Models/PeriodDissolve), or null: the incoming layer then fades in translucently.</summary>
        static Material DissolveMaterial()
        {
            if (!dissolveTried)
            {
                dissolveTried = true;
                dissolveBase = Resources.Load<Material>("Models/PeriodDissolve");
                if (dissolveBase == null || dissolveBase.shader == null || !dissolveBase.shader.isSupported)
                {
                    var sh = Shader.Find("FishingKing/PeriodDissolve");
                    dissolveBase = sh != null && sh.isSupported ? new Material(sh) : null;
                }
                if (dissolveBase == null) Debug.LogWarning("[Period] dissolve shader missing: cross-fades are translucent");
            }
            return dissolveBase != null ? new Material(dissolveBase) : null;
        }

        /// <summary>
        /// The layers and the look follow the clock's blend: A shows From, B dissolves in To; the look is re-applied when
        /// the blend changes by at least 1/64 (or its periods change).
        /// </summary>
        void ApplyLook(bool force)
        {
            var b = GameClock.Look;
            bool periodsChanged = !lookInit || b.From != shown.From || b.To != shown.To;
            if (!force && !periodsChanged && Mathf.Abs(b.F - appliedF) < 1f / 64f && !(b.F == 0f && appliedF != 0f)) return;
            lookInit = true;
            shown = b;
            appliedF = b.F;
            backA.sprite = backs[(int)b.From];
            frontA.sprite = fronts[(int)b.From];
            bool fading = b.From != b.To && b.F > 0f;
            backB.enabled = frontB.enabled = fading;
            if (fading)
            {
                backB.sprite = backs[(int)b.To];
                frontB.sprite = fronts[(int)b.To];
                float f16 = Mathf.Floor(b.F * 16f) / 16f;
                if (dissolve != null) dissolve.SetFloat(F16Id, f16);
                else backB.color = frontB.color = new Color(1f, 1f, 1f, f16);
            }
            var now = LookNow.Of(Looks, b);
            Now = now;
            WaterTint = now.WaterTint;
            WaterDeep = now.WaterDeep;
            if (Water != null) Water.SetLook(now.WaterTint, now.WaterDeep, now.FxAlpha, now.Night);
            glintOn = Mathf.Clamp(Mathf.RoundToInt(26f * now.GlintDensity), 0, glints.Count);
            for (int i = 0; i < glints.Count; i++)
                if (i >= glintOn) glints[i].sr.enabled = false;
                else if (!L.IsIce && !glints[i].sr.enabled) RespawnGlint(glints[i], true);
            ActorArt.ApplyLook(Def.id, now);
            Sfx.AmbienceVolume(1f - 0.2f * now.Night);
        }

        // ------------------------------------------------------------------ animated lights (spec 5.6)
        void BuildLamps()
        {
            for (int p = 0; p < 4; p++)
            {
                foreach (var l in Looks[p].lights)
                {
                    var lamp = lamps.Find(o => Mathf.Abs(o.x - l.x) < 1.5f && Mathf.Abs(o.y - l.y) < 1.5f && o.blink == l.blink);
                    if (lamp == null)
                    {
                        lamp = new Lamp { x = l.x, y = l.y, r = Mathf.Max(1f, l.r), blink = l.blink ?? "fixed" };
                        var sr = Layer(LampSprite(Mathf.RoundToInt(lamp.r)), OrderLight, "Lamp");
                        float cx = Mathf.Floor(l.x) + 0.5f, cy = Mathf.Floor(l.y) + 0.5f;
                        float W = L.widthPx > 0 ? L.widthPx : 640, H = L.heightPx > 0 ? L.heightPx : 400;
                        sr.transform.localPosition = new Vector3((cx - W * 0.5f) / PixelView.PPU, (H * 0.5f - cy) / PixelView.PPU, 0f);
                        var c = Art.Hex(l.col);
                        c.a = 0f;
                        sr.color = c;
                        lamp.sr = sr;
                        lamps.Add(lamp);
                    }
                    lamp.inPeriod[p] = true;
                }
            }
        }

        static readonly Dictionary<int, Sprite> lampSprites = new Dictionary<int, Sprite>();

        /// <summary>A hard dithered glow disc of radius r px (odd size, centred on a pixel), tinted in code.</summary>
        static Sprite LampSprite(int r)
        {
            if (lampSprites.TryGetValue(r, out var s)) return s;
            int d = 2 * r + 1;
            var t = new Texture2D(d, d, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < d; y++)
            for (int x = 0; x < d; x++)
            {
                float dx = x - r, dy = y - r, q = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(1f, r + 0.5f);
                bool checker = ((x + y) & 1) == 0;
                float a = q < 0.38f ? 0.55f : q < 0.62f ? 0.35f : q < 0.82f ? (checker ? 0.3f : 0f) : q < 1f ? (checker && (x & 1) == 0 ? 0.25f : 0f) : 0f;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            return lampSprites[r] = Sprite.Create(t, new Rect(0, 0, d, d), new Vector2(0.5f, 0.5f), PixelView.PPU);
        }

        static float Blink(string kind, float t)
        {
            switch (kind)
            {
                case "flicker": return 0.8f + 0.2f * (0.6f * Mathf.Sin(2f * Mathf.PI * 7.3f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 11.1f * t + 1.7f));
                case "flash1": return Mathf.Repeat(t, 4f) < 0.4f ? 1f : 0.15f;
                case "flash2":
                {
                    float u = Mathf.Repeat(t, 6f);
                    return u < 0.3f || (u >= 0.8f && u < 1.1f) ? 1f : 0.1f;
                }
                default: return 1f;
            }
        }

        void UpdateLamps()
        {
            var b = shown;
            float t = Time.time;
            foreach (var l in lamps)
            {
                float w = 0f;
                for (int p = 0; p < 4; p++) if (l.inPeriod[p]) w += b.Weight((Period)p);
                var c = l.sr.color;
                c.a = Mathf.Clamp01(w) * Blink(l.blink, t);
                l.sr.color = c;
                l.sr.enabled = c.a > 0.004f;
            }
        }

        /// <summary>Called by <see cref="WaterFx"/> when the swell moves the deck: moves the front layer (the boat).</summary>
        internal void ApplyDeckBob(Vector2 bob)
        {
            DeckBob = bob;
            if (frontA != null) frontA.transform.localPosition = bob;
            if (frontB != null) frontB.transform.localPosition = new Vector3(bob.x, bob.y, BZ);
            PlaceOcclusion();   // (the depth map moves with the boat)
        }

        SpriteRenderer Layer(Sprite s, int order, string name)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = s;
            sr.sortingOrder = order;
            return sr;
        }

        // ------------------------------------------------------------------ clouds & birds
        void SpawnClouds()
        {
            for (int i = 0; i < 5; i++)
            {
                var sr = Layer(Art.Stage("cloud_" + (i % 3 + 1)), OrderCloud, "Cloud");
                sr.transform.position = new Vector3(Random.Range(-22f, 22f), horizonY + Random.Range(0.8f, 3.5f), 0);
                sr.color = new Color(1, 1, 1, 0.92f);
                clouds.Add(sr.transform);
            }
        }

        static Sprite[] MakeBird()
        {
            var frames = new Sprite[2];
            string[][] rows =
            {
                new[] { "X.....X", ".X...X.", "..X.X..", "...X..." },
                new[] { ".......", "XXX.XXX", "...X...", "......." },
            };
            for (int f = 0; f < 2; f++)
            {
                var t = new Texture2D(7, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 7; x++)
                    t.SetPixel(x, 3 - y, rows[f][y][x] == 'X' ? new Color(0.18f, 0.2f, 0.26f) : Color.clear);
                t.Apply();
                frames[f] = Sprite.Create(t, new Rect(0, 0, 7, 4), new Vector2(0.5f, 0.5f), 16);
            }
            return frames;
        }

        System.Collections.IEnumerator Bird()
        {
            var sr = Layer(birdFrames[0], OrderBird, "Bird");
            float dir = Random.value < 0.5f ? 1 : -1;
            var pos = new Vector3(-dir * 22f, horizonY + Random.Range(0.3f, 2.5f), 0);
            float speed = Random.Range(2.5f, 4f), t = 0;
            while (Mathf.Abs(pos.x) < 23f && sr != null)
            {
                t += Time.deltaTime;
                pos.x += dir * speed * Time.deltaTime;
                pos.y += Mathf.Sin(t * 2.2f) * 0.004f;
                sr.transform.position = pos;
                sr.sprite = birdFrames[(int)(t * 6) % 2];
                yield return null;
            }
            if (sr != null) Destroy(sr.gameObject);
        }

        // ------------------------------------------------------------------ water glints
        Glint NewGlint()
        {
            var sr = Layer(Art.Pixel, Fx.OrderRipple - 1, "Glint");
            var g = new Glint { sr = sr };
            RespawnGlint(g, true);
            return g;
        }

        void RespawnGlint(Glint g, bool randomAge)
        {
            // pick a random point of the visible water, denser in the distance like real glints
            float z = Mathf.Lerp(L.zNear + 1f, Mathf.Min(L.zFar, 90f), Mathf.Pow(Random.value, 0.8f));
            float half = Mathf.Min(L.xLim, P.VisibleHalfWidth(z, 640));
            var w = new Vector3(Random.Range(-half, half), L.IsIce ? -10f : 0f, z);
            if (L.IsIce) { g.sr.enabled = false; return; }
            g.sr.enabled = true;
            var p2 = P.To2D(w);
            g.sr.transform.position = new Vector3(Mathf.Round(p2.x * 16) / 16, Mathf.Round(p2.y * 16) / 16, 0);
            float ppm = P.PixelsPerMetre(w);
            float len = Mathf.Clamp(Mathf.Round(ppm * Random.Range(0.12f, 0.3f)), 1, 5);
            g.sr.transform.localScale = new Vector3(len, 1, 1);
            g.life = Random.Range(0.6f, 1.6f);
            g.t = randomAge ? Random.Range(0, g.life) : 0;
        }

        // ------------------------------------------------------------------ fireflies / snow / motes
        Mote NewMote(Color c, bool floaty, string name = null)
        {
            var sr = Layer(Art.Pixel, Fx.OrderSparkle - 1, "Mote");
            sr.color = c;
            var m = new Mote { sr = sr, phase = Random.value * 10f, col = c };
            if (floaty)
            {
                float z = Random.Range(L.zNear + 1, 30f);
                m.world = new Vector3(Random.Range(-10f, 10f), Random.Range(0.3f, 2.5f), z);
                m.vel = new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(-0.1f, 0.2f));
            }
            else
            {
                m.pos = new Vector2(Random.Range(-21f, 21f), Random.Range(-13f, 13f));
                m.vel = new Vector2(Random.Range(-0.4f, 0.1f), Random.Range(-1.4f, -0.8f));
            }
            if (floaty) m.pos = P.To2D(m.world);
            m.sr.transform.position = m.pos;
            m.sr.gameObject.name = m.kind = name ?? (floaty ? "Firefly" : "Snow");
            return m;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Current.Tick(Time.unscaledDeltaTime);
            StageLayout.TideOffset = L.id == "sea" ? 0.4f * GameClock.Tide.H : 0f;
            ApplyLook(false);
            PlaceOcclusion();   // (after the look: the front sprite of this frame's period)
            UpdateLamps();
            var now = Now;
            foreach (var c in clouds)
            {
                c.position += new Vector3(dt * 0.12f, 0, 0);
                if (c.position.x > 24f) c.position = new Vector3(-24f, horizonY + Random.Range(0.8f, 3.5f), 0);
            }
            if (now.Birds)
            {
                birdTimer -= dt;
                if (birdTimer <= 0)
                {
                    birdTimer = Random.Range(6f, 14f);
                    StartCoroutine(Bird());
                }
            }
            var gc = now.GlintCol;
            for (int i = 0; i < glintOn && i < glints.Count; i++)
            {
                var g = glints[i];
                if (!g.sr.enabled) continue;
                g.t += dt;
                float k = g.t / g.life;
                g.sr.color = new Color(gc.r, gc.g, gc.b, Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * 0.75f);
                if (g.t >= g.life) RespawnGlint(g, false);
            }
            var tint = now.ActorTint;
            foreach (var m in motes)
            {
                m.phase += dt;
                string kind = m.kind;   // (not sr.name: that allocates a new string every call)
                if (kind == "Firefly" || kind == "Sparkle")
                {
                    m.world += new Vector3(m.vel.x, m.vel.y, 0) * dt + new Vector3(Mathf.Sin(m.phase * 1.3f), Mathf.Cos(m.phase), 0) * 0.15f * dt;
                    if (m.world.y < 0.2f || m.world.y > 3f) m.vel.y = -m.vel.y;
                    if (Mathf.Abs(m.world.x) > 12f) m.vel.x = -m.vel.x;
                    var p = P.To2D(m.world);
                    m.sr.transform.position = new Vector3(Mathf.Round(p.x * 16) / 16, Mathf.Round(p.y * 16) / 16, 0);
                    var c = m.col;
                    float w = kind == "Firefly" ? now.Fireflies : 1f;
                    c.a = (0.35f + 0.65f * Mathf.Max(0, Mathf.Sin(m.phase * 2.1f))) * w;
                    m.sr.color = c;
                    m.sr.enabled = c.a > 0.01f;
                }
                else
                {
                    m.pos += (m.vel + new Vector2(Mathf.Sin(m.phase * 1.7f) * 0.3f, 0)) * dt;
                    if (m.pos.y < -13f) m.pos = new Vector2(Random.Range(-21f, 21f), 13f);
                    m.sr.transform.position = new Vector3(Mathf.Round(m.pos.x * 16) / 16, Mathf.Round(m.pos.y * 16) / 16, 0);
                    m.sr.color = new Color(m.col.r * tint.r, m.col.g * tint.g, m.col.b * tint.b, m.col.a);
                }
            }
        }

        /// <summary>
        /// The map's rect from the front sprite's final transform of this frame (the deck bob, the period look: both set in
        /// Update), whatever order the Updates ran in: the shader and the CPU tests of the LateUpdates see where it is drawn.
        /// </summary>
        void LateUpdate() => PlaceOcclusion();

        /// <summary>Colour of an underwater object at the given depth (tint towards deep water).</summary>
        public Color UnderwaterTint(float depth, float baseAlpha = 1f)
        {
            float k = Mathf.Clamp01(depth / 7f);
            var c = Color.Lerp(Color.Lerp(Color.white, WaterTint, 0.35f), WaterDeep, k * 0.75f);
            c.a = baseAlpha * Mathf.Lerp(0.95f, 0.6f, k) * (L.IsIce ? 0.6f : 1f);
            return c;
        }

        /// <summary>Perceived brightness of an sRGB colour (0..1).</summary>
        public static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // a fish shadow shows at least this much darker (or lighter) than the water once blended:
        // 30 % on bright water, up to 50 % on dark water (cave, swamp), where the same ratio reads weaker
        static float ShadowContrast(float water) => Mathf.Lerp(0.5f, 0.3f, Mathf.InverseLerp(0.25f, 0.45f, water));

        /// <summary>
        /// Tint for a fish shadow whose sprite has average brightness <paramref name="spriteLum"/>: the underwater
        /// tint, pressed darker whenever the tinted fish would blend into the water around it (a pale fish on
        /// dark water, a faint shadow under the ice). A shallow pale fish that shows clearly lighter keeps its colours.
        /// </summary>
        public Color FishShadowTint(float depth, float spriteLum, float baseAlpha = 1f)
        {
            var c = UnderwaterTint(depth, baseAlpha);
            float water = Lum(WaterTint), a = Mathf.Max(0.05f, c.a), m = ShadowContrast(water);
            float seen = spriteLum * Lum(c);      // the fish's own brightness through the tint
            float dark = water * (1f - m / a);    // blended over the water this is m darker
            float light = water * (1f + m / a);   // ... and this m lighter
            if (seen > dark && seen < light)
            {
                float k = Mathf.Max(0f, dark) / Mathf.Max(1e-4f, seen);
                c.r *= k;
                c.g *= k;
                c.b *= k;
            }
            return c;
        }

        /// <summary>The fishing line seen through the water: its colour dimmed towards the deep water.</summary>
        public Color UnderwaterLine(Color line)
        {
            var c = Color.Lerp(line, WaterDeep, 0.5f);
            c.a = L.IsIce ? 0.45f : 0.8f;
            return c;
        }
    }
}
