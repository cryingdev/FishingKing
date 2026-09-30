using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Tiny pooled sprite particles in the 2D pixel scene (splashes, ripples, glints). A particle given a depth (metres of
    /// camera depth where it happens, <see cref="Persp.DepthOf"/>: the spray of a fish beside the pier) is hidden where the
    /// front layer is nearer (<see cref="FrontOcclusion"/>); without one (0) it never is.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        public const int OrderRipple = 30, OrderSplash = 44, OrderSparkle = 60;

        class P
        {
            public SpriteRenderer sr;
            public Vector2 vel;
            public float life, max, grav, grow, startScale, squash, spin;
            public Color col;
            public bool fade;
            public Sprite[] frames;   // a frame animation (Frames): one sprite per frameTime, no fade
            public float frameTime;
        }

        static Fx I;
        readonly List<P> live = new List<P>();
        readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();

        public static void Ensure()
        {
            if (I != null) return;
            I = new GameObject("Fx").AddComponent<Fx>();
        }

        SpriteRenderer Get()
        {
            SpriteRenderer sr;
            if (pool.Count > 0)
            {
                sr = pool.Pop();
                sr.gameObject.SetActive(true);
            }
            else
            {
                sr = new GameObject("p").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(transform, false);
                FrontOcclusion.Use(sr);
            }
            sr.transform.rotation = Quaternion.identity;
            return sr;
        }

        static P Spawn(Sprite s, Vector2 pos, Color c, float life, int order, float scale = 1f, float depth = 0f)
        {
            Ensure();
            var sr = I.Get();
            FrontOcclusion.SetDepth(sr, depth);
            sr.sprite = s;
            sr.color = c;
            sr.sortingOrder = order;
            sr.transform.position = pos;
            sr.transform.localScale = Vector3.one * scale;
            var p = new P { sr = sr, life = life, max = life, col = c, startScale = scale, squash = 1f, fade = true };
            I.live.Add(p);
            return p;
        }

        /// <summary>Droplets flying up from the water surface; <paramref name="strength"/> follows the perspective (size and spread).</summary>
        public static void Splash(Vector2 pos, float strength, Color water, int count = 10, float depth = 0f)
        {
            var light = Color.Lerp(water, Color.white, 0.7f);
            float size = Mathf.Clamp(strength, 0.45f, 1.2f);
            for (int i = 0; i < count; i++)
            {
                var p = Spawn(Art.Circle, pos + new Vector2(Random.Range(-0.3f, 0.3f) * strength, 0),
                    Random.value < 0.5f ? Color.white : light, Random.Range(0.35f, 0.7f), OrderSplash, Random.Range(0.35f, 0.7f) * size, depth);
                p.vel = new Vector2(Random.Range(-1.4f, 1.4f) * strength, Random.Range(1.8f, 4.2f) * strength);
                p.grav = 12f;
            }
        }

        /// <summary>One droplet falling off something wet (a jumping fish); <paramref name="size"/> follows the perspective.</summary>
        public static void Drip(Vector2 pos, Color water, float size = 1f, float depth = 0f)
        {
            var p = Spawn(Art.Circle, pos + new Vector2(Random.Range(-0.2f, 0.2f), 0), Color.Lerp(water, Color.white, 0.75f),
                Random.Range(0.3f, 0.55f), OrderSplash, Random.Range(0.25f, 0.45f) * Mathf.Clamp(size, 0.45f, 1.2f), depth);
            p.vel = new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0f, 1.2f)) * size;
            p.grav = 12f;
        }

        /// <summary>Droplets thrown off in all directions (a fish shaking its head in the air).</summary>
        public static void Fling(Vector2 pos, Color water, float size = 1f, int count = 6, float depth = 0f)
        {
            var light = Color.Lerp(water, Color.white, 0.75f);
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var p = Spawn(Art.Circle, pos, Random.value < 0.5f ? Color.white : light, Random.Range(0.3f, 0.55f), OrderSplash,
                    Random.Range(0.25f, 0.5f) * Mathf.Clamp(size, 0.45f, 1.2f), depth);
                p.vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f + 0.5f) * Random.Range(1.5f, 3.2f) * size;
                p.grav = 10f;
            }
        }

        /// <summary>Expanding ring on the water, squashed by the perspective.</summary>
        public static void Ripple(Vector2 pos, float size, float squash, Color c, float life = 0.9f)
        {
            var p = Spawn(Art.Ring, pos, c, life, OrderRipple, size * 0.3f);
            p.grow = size;
            p.squash = Mathf.Clamp(squash, 0.2f, 1f);
        }

        public static void Sparkle(Vector2 pos, Color c, float life = 0.5f, float scale = 1f, int order = OrderSparkle, float depth = 0f)
        {
            var p = Spawn(Art.Pixel, pos, c, life, order, scale, depth);
            p.spin = 0;
        }

        /// <summary>
        /// A small puff of silt / bubbles under water (a lure touching the bottom, a kona's bubble trail): pixel specks
        /// drifting out and up, drawn at <paramref name="order"/> (under the surface effects).
        /// </summary>
        public static void Puff(Vector2 pos, Color c, int n = 4, float speed = 0.6f, int order = 13, float rise = 0.4f, float depth = 0f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var p = Spawn(Art.Pixel, pos, c, Random.Range(0.35f, 0.7f), order, Random.value < 0.4f ? 2f : 1f, depth);
                p.vel = new Vector2(Mathf.Cos(a) * speed, Mathf.Abs(Mathf.Sin(a)) * speed * 0.6f + rise) * Random.Range(0.5f, 1f);
                p.grav = 0f;
            }
        }

        public static void Burst(Vector2 pos, Color c, int n = 14, float speed = 3f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n + Random.Range(-0.2f, 0.2f);
                var p = Spawn(Art.Circle, pos, c, Random.Range(0.4f, 0.8f), OrderSparkle, Random.Range(0.3f, 0.55f));
                p.vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * Random.Range(0.6f, 1.1f);
                p.grav = 4f;
            }
        }

        /// <summary>
        /// A frame animation (the obstacle FX: a cast's hit, chips, line-rub sparks, the frog landing on a pad): the frames
        /// in turn, <paramref name="frameTime"/> s each, then gone; tinted, optionally flipped, drifting with
        /// <paramref name="vel"/> (and falling with <paramref name="grav"/>).
        /// </summary>
        public static void Frames(Sprite[] frames, Vector2 pos, float frameTime, int order, Color tint, bool flip = false, Vector2 vel = default, float grav = 0f, float depth = 0f)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return;
            var p = Spawn(frames[0], pos, tint, frameTime * frames.Length, order, 1f, depth);
            p.frames = frames;
            p.frameTime = Mathf.Max(0.01f, frameTime);
            p.fade = false;
            p.vel = vel;
            p.grav = grav;
            p.sr.flipX = flip;
        }

        /// <summary>The frames &lt;name&gt;0 .. &lt;name&gt;&lt;n-1&gt; (Sprites/World/...), cached; null when the first is missing.</summary>
        public static Sprite[] Load(string name, int n)
        {
            if (frameCache.TryGetValue(name, out var f)) return f;
            f = new Sprite[n];
            for (int i = 0; i < n; i++) f[i] = Resources.Load<Sprite>("Sprites/World/" + name + i);
            if (f[0] == null) f = null;
            frameCache[name] = f;
            return f;
        }

        static readonly Dictionary<string, Sprite[]> frameCache = new Dictionary<string, Sprite[]>();

        /// <summary>The live particles' renderers (the occlusion watch).</summary>
        internal static void LiveRenderers(List<SpriteRenderer> into)
        {
            into.Clear();
            if (I == null) return;
            foreach (var p in I.live)
                if (p.sr != null && p.sr.gameObject.activeInHierarchy) into.Add(p.sr);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                p.life -= dt;
                if (p.life <= 0 || p.sr == null)
                {
                    if (p.sr != null)
                    {
                        p.sr.flipX = false;
                        p.sr.gameObject.SetActive(false);
                        pool.Push(p.sr);
                    }
                    live.RemoveAt(i);
                    continue;
                }
                float k = 1f - p.life / p.max;
                p.vel.y -= p.grav * dt;
                var t = p.sr.transform;
                t.position += (Vector3)(p.vel * dt);
                float s = p.startScale + p.grow * k;
                t.localScale = new Vector3(s, s * p.squash, 1);
                if (p.frames != null)
                {
                    int fi = Mathf.Clamp(Mathf.FloorToInt((p.max - p.life) / p.frameTime), 0, p.frames.Length - 1);
                    if (p.frames[fi] != null) p.sr.sprite = p.frames[fi];
                }
                if (p.fade)
                {
                    var c = p.col;
                    c.a *= 1f - k * k;
                    p.sr.color = c;
                }
            }
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }
    }
}
