using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The tank's dirt as the player sees it (the model: <see cref="AquaTank"/>; the art: Tools/Blender/fk_aquaclean.py):
    /// the algae film on the front glass and the dirt on the gravel are drawn pixel by pixel from the per-pixel maps
    /// (a pixel shows once its local level reaches its grow-map value, coloured by the stage of that level), floating
    /// debris bits drift in the water (one per 1/20 of the debris level), the water turns murky and the fish / decorations
    /// dull with the filth. The cleaning tools stand on the cabinet ledge beside the feed (AquaClean.Tools.cs): the sponge
    /// wipes the glass where it is rubbed, the net scoops the bits it passes, the siphon cleans the gravel under its
    /// path; the whole tank clean again: "반짝반짝!".
    /// </summary>
    public partial class AquaClean : MonoBehaviour
    {
        public static AquaClean Current { get; private set; }

        const float Ppu = 16f;
        // the glass opening and the gravel strip of the tank in use (AquaLayout; the 중형 수조: -13.625..13.625 x
        // -4.625..5.625, the strip's centre -3.75, the surface row 5.1875)
        static AquaLayout L => AquaLayout.Active;
        public static float GlassL => L.glassL;
        public static float GlassR => L.glassR;
        public static float GlassB => L.glassB;
        public static float GlassT => L.glassT;
        static float DirtCentreY => L.dirtCentreY;
        static float SurfaceFx => L.surfaceFx;    // the surface row (a surface bit's centre, a splash)
        const int SnapPx = 90;                    // a stroke leaving no more than this many algae pixels showing: all clean

        PixelView pv;
        AquariumData box;
        List<TankFish> fish;
        RectTransform ui;
        Transform root;

        // ---- overlays
        Texture2D algaeTex, dirtTex;
        SpriteRenderer algaeSr, dirtSr, murkSr;
        byte[] algaeGrow, dirtGrow;
        Color32[][] algaeStage, dirtStage;
        Color32[] algaePx, dirtPx;
        Sprite[] murkS;
        int renderedVersion = -1, murkLevel = -1;
        float murkFade = 1f;

        /// <summary>Algae / dirt pixels showing (the autopilot, the all-clean check).</summary>
        public int VisibleAlgae { get; private set; }
        public int VisibleDirt { get; private set; }

        // ---- debris
        class Bit
        {
            public SpriteRenderer sr;
            public Sprite[] f;
            public string kind;
            public Vector2 pos, catchFrom;
            public float homeY, t, phase, swap, fade, vx, catchT;
            public int frame;
            public bool scum, caught, gone;
        }

        static readonly (string kind, float w)[] BitKinds =
        {
            ("leaf", 2f), ("deadleaf", 2f), ("fibre", 1f), ("hair", 1f), ("mulm", 2f), ("speck", 2f), ("food", 1f), ("scale", 1f), ("scum", 1.5f),
        };

        readonly List<Bit> bits = new List<Bit>();
        readonly Dictionary<string, Sprite[]> bitS = new Dictionary<string, Sprite[]>();
        Color tint = Color.white;

        // ---- read by the test autopilot
        public int Celebrations { get; private set; }
        public int BitsAlive => bits.Count(b => !b.caught && !b.gone);
        public IEnumerable<Vector2> BitPositions => bits.Where(b => !b.caught && !b.gone && b.fade > 0.5f).Select(b => b.pos);
        public int MurkLevel => murkLevel;

        public void Init(PixelView view, AquariumData data, List<TankFish> tankFish, RectTransform canvas)
        {
            Current = this;
            pv = view;
            box = data;
            fish = tankFish;
            ui = canvas;
            root = transform;
            AquaTank.Ensure(Game.Data);

            // the algae film on the glass, the dirt on the gravel: textures redrawn from the maps
            // (the art is drawn for the 436 x 164 glass / 436 x 28 strip: other tanks take it fitted edge to edge,
            // pixel for pixel: the edges kept, the middle mirrored or cut)
            int aw = AquaTank.AW, ah = AquaTank.AH, bw = AquaTank.BW, bh = AquaTank.BH;
            algaeGrow = FitArt(Grow(Art.Get("World/clean_algae_grow")), SrcW, SrcAH, aw, ah);
            dirtGrow = FitArt(Grow(Art.Get("World/clean_dirt_grow")), SrcW, SrcBH, bw, bh);
            algaeStage = new Color32[4][];
            for (int i = 0; i < 4; i++) algaeStage[i] = FitArt(Pixels(Art.Get("World/clean_algae_" + (i + 1)), SrcW, SrcAH), SrcW, SrcAH, aw, ah);
            dirtStage = new Color32[3][];
            for (int i = 0; i < 3; i++) dirtStage[i] = FitArt(Pixels(Art.Get("World/clean_dirt_" + (i + 1)), SrcW, SrcBH), SrcW, SrcBH, bw, bh);
            algaePx = new Color32[aw * ah];
            dirtPx = new Color32[bw * bh];
            algaeTex = NewTex(aw, ah, "AlgaeMap");
            dirtTex = NewTex(bw, bh, "DirtMap");
            algaeSr = NewSprite("Algae", Sprite.Create(algaeTex, new Rect(0, 0, aw, ah), new Vector2(0.5f, 0.5f), Ppu), 35);
            algaeSr.transform.position = L.GlassCentre;
            dirtSr = NewSprite("Dirt", Sprite.Create(dirtTex, new Rect(0, 0, bw, bh), new Vector2(0.5f, 0.5f), Ppu), 1);
            dirtSr.transform.position = new Vector3(0f, DirtCentreY, 0f);
            murkS = new[] { Art.Get("World/clean_murk_1"), Art.Get("World/clean_murk_2"), Art.Get("World/clean_murk_3") };
            murkSr = NewSprite("Murk", null, 33);
            murkSr.transform.position = L.GlassCentre;
            // (a soft haze: stretched over the glass)
            murkSr.transform.localScale = new Vector3(L.GlassScale.x, L.GlassScale.y, 1f);
            foreach (var (k, _) in BitKinds) bitS[k] = new[] { Art.Get($"World/clean_debris_{k}_f0"), Art.Get($"World/clean_debris_{k}_f1") };
            Render();
            UpdateMurk(0f, true);
            // the bits already floating when the scene opens
            int n = AquaTank.DebrisCount(Game.Data);
            for (int i = 0; i < n; i++) Spawn(true);
            InitTools();
            AquaCare.Log($"clean scene: {AquaTank.Describe(Game.Data)}, bits {n}, algae px {VisibleAlgae}, dirt px {VisibleDirt}");
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            AquaTank.Commit();
            if (Game.I != null) Game.I.Save();
            if (algaeTex != null) Destroy(algaeTex);
            if (dirtTex != null) Destroy(dirtTex);
        }

        static Texture2D NewTex(int w, int h, string name) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };

        SpriteRenderer NewSprite(string name, Sprite s, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            sr.sprite = s;
            sr.sortingOrder = order;
            return sr;
        }

        static Vector2 Snap(Vector2 v) => new Vector2(Mathf.Round(v.x * Ppu) / Ppu, Mathf.Round(v.y * Ppu) / Ppu);

        static void Place(Component c, Vector2 p) => c.transform.position = Snap(p);

        Vector2 ToCanvas(Vector2 world) => pv.WorldToScreen(world) * UIKit.CanvasPerScreenPx;

        /// <summary>
        /// A sprite's pixels (texture order), CPU-readable ones directly (PixelArtImporter marks the clean_* maps
        /// readable), others through a GPU read-back.
        /// </summary>
        static Color32[] Pixels(Sprite s, int w = 0, int h = 0)
        {
            if (s == null) return new Color32[Mathf.Max(1, w * h)];
            var t = s.texture;
            if (t.isReadable) return t.GetPixels32();
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tmp.GetPixels32();
            Destroy(tmp);
            return px;
        }

        /// <summary>A grow map: the level (1..255) at which each pixel first shows, 0 = never.</summary>
        static byte[] Grow(Sprite s)
        {
            var px = Pixels(s);
            var g = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) g[i] = px[i].a == 0 ? (byte)0 : (byte)Mathf.Max(1, (int)px[i].r);
            return g;
        }

        const int SrcW = 436, SrcAH = 164, SrcBH = 28;   // the clean_* art's size (the 중형 수조's glass / strip)

        /// <summary>Source column / row for a target one: both ends kept, the middle mirrored back and forth (or cut).</summary>
        static int FitIndex(int x, int dst, int src)
        {
            if (dst == src) return x;
            int q = src / 4;
            if (dst <= src) return x < dst / 2 ? x : src - (dst - x);
            if (x < q) return x;
            if (x >= dst - q) return src - (dst - x);
            int len = src - 2 * q, u = (x - q) % (2 * len);
            return q + (u < len ? u : 2 * len - 1 - u);
        }

        /// <summary>The art made for the 중형 수조 fitted to another tank's size, pixel for pixel (texture order).</summary>
        static T[] FitArt<T>(T[] src, int sw, int sh, int dw, int dh)
        {
            if (src == null || src.Length < sw * sh) return new T[dw * dh];
            if (sw == dw && sh == dh) return src;
            var dst = new T[dw * dh];
            var cx = new int[dw];
            for (int x = 0; x < dw; x++) cx[x] = Mathf.Clamp(FitIndex(x, dw, sw), 0, sw - 1);
            for (int y = 0; y < dh; y++)
            {
                int sy = Mathf.Clamp(FitIndex(y, dh, sh), 0, sh - 1);
                for (int x = 0; x < dw; x++) dst[y * dw + x] = src[sy * sw + cx[x]];
            }
            return dst;
        }

        /// <summary>A brush's alpha (0..1, texture order).</summary>
        static float[] Brush(Sprite s, out int w, out int h)
        {
            w = s != null ? (int)s.rect.width : 1;
            h = s != null ? (int)s.rect.height : 1;
            var px = Pixels(s, w, h);
            var b = new float[px.Length];
            for (int i = 0; i < px.Length; i++) b[i] = px[i].a / 255f;
            return b;
        }

        // ------------------------------------------------------------------ overlays
        void Render()
        {
            renderedVersion = AquaTank.Version;
            var a = AquaTank.AlgaeMap;
            var clear = new Color32(0, 0, 0, 0);
            int vis = 0;
            for (int i = 0; i < a.Length; i++)
            {
                byte g = algaeGrow[i];
                float v = a[i];
                if (g == 0 || v * 255f < g)
                {
                    algaePx[i] = clear;
                    continue;
                }
                // the stage of this level, ceil(4 v): each stage holds every pixel of the ones before
                var c = algaeStage[v > 0.75f ? 3 : v > 0.5f ? 2 : v > 0.25f ? 1 : 0][i];
                algaePx[i] = c;
                if (c.a > 0) vis++;
            }
            algaeTex.SetPixels32(algaePx);
            algaeTex.Apply(false);
            VisibleAlgae = vis;
            var b = AquaTank.DirtMap;
            vis = 0;
            for (int i = 0; i < b.Length; i++)
            {
                byte g = dirtGrow[i];
                float v = b[i];
                if (g == 0 || v * 255f < g)
                {
                    dirtPx[i] = clear;
                    continue;
                }
                var c = dirtStage[v > 2f / 3f ? 2 : v > 1f / 3f ? 1 : 0][i];
                dirtPx[i] = c;
                if (c.a > 0) vis++;
            }
            dirtTex.SetPixels32(dirtPx);
            dirtTex.Apply(false);
            VisibleDirt = vis;
        }

        /// <summary>The algae film shows at this world point (the autopilot).</summary>
        public bool AlgaeAt(Vector2 w)
        {
            int x = Mathf.FloorToInt((w.x - GlassL) * Ppu), y = Mathf.FloorToInt((w.y - GlassB) * Ppu);
            if (x < 0 || y < 0 || x >= AquaTank.AW || y >= AquaTank.AH) return false;
            if (renderedVersion != AquaTank.Version) Render();
            return algaePx[y * AquaTank.AW + x].a > 0;
        }

        /// <summary>Algae pixels showing in a world rect (the autopilot: the wiped half against the other).</summary>
        public float AlgaeCover(Rect w)
        {
            if (renderedVersion != AquaTank.Version) Render();
            int x0 = Mathf.Clamp(Mathf.FloorToInt((w.xMin - GlassL) * Ppu), 0, AquaTank.AW), x1 = Mathf.Clamp(Mathf.FloorToInt((w.xMax - GlassL) * Ppu), 0, AquaTank.AW);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((w.yMin - GlassB) * Ppu), 0, AquaTank.AH), y1 = Mathf.Clamp(Mathf.FloorToInt((w.yMax - GlassB) * Ppu), 0, AquaTank.AH);
            int n = 0, on = 0;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                n++;
                if (algaePx[y * AquaTank.AW + x].a > 0) on++;
            }
            return n > 0 ? on / (float)n : 0f;
        }

        /// <summary>The gravel's dirt level under a world x span (the autopilot).</summary>
        public float DirtUnder(float x0, float x1)
        {
            var b = AquaTank.DirtMap;
            int c0 = Mathf.Clamp(Mathf.FloorToInt((x0 - GlassL) * Ppu), 0, AquaTank.BW - 1), c1 = Mathf.Clamp(Mathf.FloorToInt((x1 - GlassL) * Ppu), c0 + 1, AquaTank.BW);
            double s = 0;
            int n = 0;
            for (int y = 0; y < AquaTank.BH; y++)
            for (int x = c0; x < c1; x++)
            {
                s += b[y * AquaTank.BW + x];
                n++;
            }
            return n > 0 ? (float)(s / n) : 0f;
        }

        /// <summary>The water clouds over with the filth: none under 25 %, then the three murk layers at 25 / 50 / 75 %.</summary>
        void UpdateMurk(float dt, bool instant = false)
        {
            float f = AquaTank.Filth(Game.Data);
            int k = f >= 0.75f ? 3 : f >= 0.5f ? 2 : f >= 0.25f ? 1 : 0;
            if (k != murkLevel)
            {
                murkLevel = k;
                murkFade = instant ? 1f : 0f;
                murkSr.sprite = k > 0 ? murkS[k - 1] : null;
            }
            murkFade = Mathf.Min(1f, murkFade + dt / 0.6f);
            murkSr.enabled = k > 0;
            murkSr.color = new Color(1f, 1f, 1f, murkFade);
        }

        // ------------------------------------------------------------------ debris
        Bit Spawn(bool atOnce)
        {
            float total = BitKinds.Sum(k => k.w), r = Random.value * total;
            string kind = BitKinds[BitKinds.Length - 1].kind;
            foreach (var (k, wt) in BitKinds)
            {
                r -= wt;
                if (r <= 0f)
                {
                    kind = k;
                    break;
                }
            }
            var b = new Bit
            {
                kind = kind, f = bitS[kind], scum = kind == "scum", phase = Random.Range(0f, 6.28f), t = Random.value * 5f,
                swap = Random.Range(0.4f, 0.9f), fade = atOnce ? 1f : 0f, vx = Random.Range(-0.12f, 0.12f),
            };
            // near the surface or in mid-water; scum lies on the surface row
            b.homeY = b.scum ? SurfaceFx : Mathf.Lerp(L.gravelTopY + 2f, box.surfaceY - 0.5f, Mathf.Sqrt(Random.value));
            b.pos = new Vector2(Random.Range(GlassL + 1.2f, GlassR - 1.2f), b.homeY);
            b.sr = NewSprite("Bit_" + kind, b.f[0], 30);
            b.sr.flipX = Random.value < 0.5f;
            Place(b.sr, b.pos);
            bits.Add(b);
            return b;
        }

        void UpdateDebris(float dt)
        {
            int want = AquaTank.DebrisCount(Game.Data);
            int live = BitsAlive;
            while (live < want)
            {
                Spawn(false);
                live++;
            }
            for (int i = 0; i < bits.Count && live > want; i++)
                if (!bits[i].caught && !bits[i].gone)
                {
                    bits[i].gone = true;
                    live--;
                }
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var b = bits[i];
                b.t += dt;
                if (b.caught)
                {
                    // into the net's bag, then it is in there (the net shows full)
                    b.catchT += dt / 0.14f;
                    float k = Mathf.Clamp01(b.catchT);
                    b.pos = Vector2.Lerp(b.catchFrom, NetBag, k * k);
                    b.sr.color = new Color(tint.r, tint.g, tint.b, 1f - k * 0.3f);
                    Place(b.sr, b.pos);
                    if (k >= 1f)
                    {
                        Destroy(b.sr.gameObject);
                        bits.RemoveAt(i);
                    }
                    continue;
                }
                if (b.gone)
                {
                    b.fade -= dt / 0.5f;
                    if (b.fade <= 0f)
                    {
                        Destroy(b.sr.gameObject);
                        bits.RemoveAt(i);
                        continue;
                    }
                }
                else b.fade = Mathf.Min(1f, b.fade + dt / 0.7f);
                // a lazy drift: along the water, bobbing (scum stays on the surface)
                float drift = (b.scum ? 0.06f : 0.14f) * Mathf.Sin(b.t * 0.23f + b.phase) + b.vx;
                b.pos.x += drift * dt;
                if (b.pos.x < GlassL + 0.825f || b.pos.x > GlassR - 0.825f)
                {
                    b.pos.x = Mathf.Clamp(b.pos.x, GlassL + 0.825f, GlassR - 0.825f);
                    b.vx = -b.vx;
                    b.phase += Mathf.PI;
                }
                b.pos.y = b.scum ? SurfaceFx : b.homeY + 0.22f * Mathf.Sin(b.t * 0.5f + b.phase * 2f);
                b.swap -= dt;
                if (b.swap <= 0f)
                {
                    b.swap = Random.Range(0.45f, 0.95f);
                    b.frame ^= 1;
                    b.sr.sprite = b.f[b.frame];
                }
                b.sr.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(b.fade));
                Place(b.sr, b.pos);
            }
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            float dt = Time.deltaTime;
            tint = AquaTank.LightTint;
            UpdateInput();
            UpdateTools(dt);
            if (AquaTank.Version != renderedVersion) Render();
            UpdateDebris(dt);
            UpdateMurk(dt);
            UpdateUI(dt);
        }

        // ------------------------------------------------------------------ all clean
        /// <summary>
        /// After a stroke / a release: a part cleaned down to a few specks is cleaned (the last pixels go); the whole tank
        /// clean when it was not before: "반짝반짝!".
        /// </summary>
        /// <returns>The part just cleaned ("유리가 깨끗해졌어요"), or null (nothing, or the whole tank: the big sparkle).</returns>
        string CheckDone(Vector2 at)
        {
            var t = Game.Data.tank;
            if (renderedVersion != AquaTank.Version) Render();
            bool glass = GlassClean, bottom = BottomClean, water = WaterClean;
            // the last specks go with the part
            if (glass && t.algae > 0f)
            {
                AquaTank.ZeroAlgae();
                AquaCare.Log("glass clean");
            }
            if (bottom && t.dirt > 0f)
            {
                AquaTank.ZeroDirt();
                AquaCare.Log("gravel clean");
            }
            if (water && t.debris > 0f) t.debris = 0f;
            if (renderedVersion != AquaTank.Version) Render();
            if (glass && bottom && water && !cleanBefore)
            {
                Celebrate();
                return null;
            }
            // one part done: a little sparkle where the tool is
            string done = glass && !glassBefore ? "유리가 깨끗해졌어요" : bottom && !bottomBefore ? "바닥이 깨끗해졌어요" : water && !waterBefore ? "물이 맑아졌어요" : null;
            if (done != null)
            {
                Fx.Frames(burstS, Snap(at), 0.07f, 50, Color.white);
                Sfx.Play(CleanSfx.Sparkle, 0.5f, 1.1f);
                AquaCare.Log("part clean: " + done);
            }
            return done;
        }

        // a part counts as clean with at most a few specks showing (the growth since does not make it dirty at once)
        bool GlassClean => VisibleAlgae <= SnapPx && Game.Data.tank.algae < 0.06f;
        bool BottomClean => VisibleDirt <= SnapPx / 3 && Game.Data.tank.dirt < 0.06f;
        bool WaterClean => AquaTank.DebrisCount(Game.Data) == 0 && BitsAlive == 0;

        bool cleanBefore, glassBefore, bottomBefore, waterBefore;

        void NoteBefore()
        {
            if (renderedVersion != AquaTank.Version) Render();
            glassBefore = GlassClean;
            bottomBefore = BottomClean;
            waterBefore = WaterClean;
            cleanBefore = glassBefore && bottomBefore && waterBefore;
        }

        void Celebrate()
        {
            Celebrations++;
            var t = Game.Data.tank;
            t.cleans++;
            AquaTank.Commit();
            Game.I.Save();
            StartCoroutine(Bursts());
            Sfx.Play(AquaSfx.Chime, 0.8f);
            Sfx.Play(CleanSfx.Sparkle, 0.8f);
            UIKit.PlateLabel(ui, "반짝반짝!", 24, UIKit.Gold, out var plate);
            plate.name = "SparklePop";
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = Vector2.zero;
            plate.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            plate.rectTransform.anchoredPosition = ToCanvas(L.GlassCentre + new Vector2(0f, 0.7f));
            Tween.Pop(plate.transform, 0.5f, 0.25f);
            Tween.FloatUp(plate.rectTransform, 30f, 2.2f);
            AquaCare.Log($"반짝반짝! the whole tank clean (cleans {t.cleans}): {AquaTank.Describe(Game.Data)}");
        }

        /// <summary>The "clean!" bursts over the glass, one after another.</summary>
        IEnumerator Bursts()
        {
            Vector2[] at =
            {
                new Vector2(-9f, 3f), new Vector2(-2.5f, 0.5f), new Vector2(4.5f, 3.4f), new Vector2(10f, 0f), new Vector2(-6f, -1.8f),
                new Vector2(1.5f, 4.2f), new Vector2(7f, -2.2f), new Vector2(-11f, -0.5f),
            };
            // (placed for the 중형 수조's glass: spread over this tank's)
            var c = L.GlassCentre;
            var k = L.GlassScale;
            foreach (var p in at)
            {
                var q = c + new Vector2(p.x * k.x, (p.y - 0.5f) * k.y);
                Fx.Frames(burstS, Snap(q), 0.08f, 50, Color.white);
                yield return new WaitForSeconds(0.07f);
            }
        }

        /// <summary>The whole tank's state for the hint at the top ("" = clean enough).</summary>
        public static string DirtHint()
        {
            var d = Game.Data;
            var t = d.tank;
            if (t == null) return "";
            if (t.algae >= 0.35f && t.algae >= t.dirt && t.algae >= t.debris) return "유리에 이끼가 꼈어요 · 스펀지로 문질러 닦아요";
            if (t.debris >= 0.35f && t.debris >= t.dirt)
                return AquaTank.Owns(AquaTank.NetId) ? "떠다니는 찌꺼기를 뜰채로 건져요" : "찌꺼기가 떠다녀요 · 상점에서 뜰채를 사요";
            if (t.dirt >= 0.35f)
                return AquaTank.Owns(AquaTank.SiphonId) ? "바닥에 때가 쌓였어요 · 사이펀으로 훑어요" : "바닥에 때가 쌓였어요 · 상점에서 사이펀을 사요";
            return "";
        }
    }

    /// <summary>The cleaning's little sounds, synthesized like <see cref="AquaSfx"/>'s.</summary>
    public static class CleanSfx
    {
        const int Rate = 22050;
        static AudioClip squeak, gurgle, swish, sparkle;

        /// <summary>A rubbery squeak: a quick rising chirp with a wobble.</summary>
        public static AudioClip Squeak => squeak != null ? squeak : squeak = Make("clean_squeak", 0.07f, (t, i) =>
        {
            float f = Mathf.Lerp(1250f, 2100f, t) * (1f + 0.07f * Mathf.Sin(i / (float)Rate * 2f * Mathf.PI * 38f));
            return f;
        }, (t, ph) => (Mathf.Sin(ph) + 0.35f * Mathf.Sin(2f * ph) + 0.15f * Mathf.Sin(3f * ph)) * Mathf.Min(1f, t / 0.08f) * Mathf.Pow(1f - t, 1.2f));

        /// <summary>The siphon: low bubbly gulps.</summary>
        public static AudioClip Gurgle
        {
            get
            {
                if (gurgle != null) return gurgle;
                int n = (int)(0.28f * Rate);
                var d = new float[n];
                var r = new System.Random(97);
                double ph = 0;
                float y = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / n;
                    float blip = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 5f));
                    ph += (140f + 160f * blip) / Rate;
                    y += 0.25f * ((float)r.NextDouble() * 2f - 1f - y);
                    d[i] = (Mathf.Sin((float)ph * 2f * Mathf.PI) * 0.7f + y * 0.5f) * blip * Mathf.Sin(t * Mathf.PI);
                }
                return gurgle = Clip("clean_gurgle", d);
            }
        }

        /// <summary>The net through the water: a soft filtered whoosh.</summary>
        public static AudioClip Swish
        {
            get
            {
                if (swish != null) return swish;
                int n = (int)(0.22f * Rate);
                var d = new float[n];
                var r = new System.Random(101);
                float y = 0f, lo = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / n;
                    y += Mathf.Lerp(0.15f, 0.5f, Mathf.Sin(t * Mathf.PI)) * ((float)r.NextDouble() * 2f - 1f - y);
                    lo += 0.05f * (y - lo);
                    d[i] = (y - lo) * Mathf.Sin(t * Mathf.PI);
                }
                return swish = Clip("clean_swish", d);
            }
        }

        /// <summary>반짝반짝: a quick rising arpeggio of glassy blips.</summary>
        public static AudioClip Sparkle
        {
            get
            {
                if (sparkle != null) return sparkle;
                int n = (int)(0.6f * Rate);
                var d = new float[n];
                float[] f = { 1568f, 1976f, 2349f, 3136f };
                for (int k = 0; k < f.Length; k++)
                {
                    int s0 = (int)(k * 0.07f * Rate);
                    double ph = 0;
                    for (int i = s0; i < n; i++)
                    {
                        float t = (float)(i - s0) / (n - s0);
                        ph += f[k] / Rate;
                        d[i] += (Mathf.Sin((float)ph * 2f * Mathf.PI) + 0.3f * Mathf.Sin((float)ph * 4f * Mathf.PI)) * Mathf.Pow(1f - t, 3f) * 0.35f;
                    }
                }
                return sparkle = Clip("clean_sparkle", d);
            }
        }

        static AudioClip Make(string name, float len, System.Func<float, int, float> freq, System.Func<float, float, float> wave)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                ph += freq(t, i) / Rate * 2.0 * Mathf.PI;
                d[i] = wave(t, (float)ph);
            }
            return Clip(name, d);
        }

        static AudioClip Clip(string name, float[] d)
        {
            float peak = 0.0001f;
            foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            float g = Mathf.Min(1f, 0.9f / peak);
            for (int i = 0; i < d.Length; i++) d[i] *= g;
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
