using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// A lily pad's wobble (Docs/obstacles_spec.md 5.1, 10.4) when a lure lands on it, is pulled across it (a 톡's hop, every
    /// <see cref="CrawlGap"/> s of a crawl), drops off it, or a fish strikes through it. The pads are painted in the stage's
    /// front layer, so the pad is cut out of it (the front pixels whose water point lies in its footprint) into a small
    /// overlay over the front layer: the water under it (the back layer) where the pad was, and the pad shifted 1 px to one
    /// side, then the other, for 2 frames of <see cref="FrameTime"/>. The cut is kept per pad and period.
    /// </summary>
    public class PadWobble : MonoBehaviour
    {
        public const float FrameTime = 0.06f;
        /// <summary>Seconds between wobbles while a lure crawls across a pad.</summary>
        public const float CrawlGap = 0.35f;
        static readonly Vector2Int[] Shifts = { new Vector2Int(1, 0), new Vector2Int(-1, 0) };

        class Cut
        {
            public Sprite front, erase, pad;
            public Vector2 at;
            public int pixels;
        }

        StageView stage;
        Persp P;
        Obstacles obs;
        int W, H;
        SpriteRenderer eraseSr, padSr;
        readonly Dictionary<string, Cut> cuts = new Dictionary<string, Cut>();
        readonly Dictionary<Texture2D, Color32[]> texels = new Dictionary<Texture2D, Color32[]>();
        Cut cur;
        float t = -1f;

        /// <summary>Wobbles played, the pad pixels of the last one, and the pad's shift this frame (px; zero: none) (for the tests).</summary>
        public int Wobbles { get; private set; }
        public int LastPixels { get; private set; }
        public Vector2Int ShiftNow { get; private set; }

        public static PadWobble Create(StageView s)
        {
            var go = new GameObject("PadWobble");
            go.transform.SetParent(s.transform, false);
            var w = go.AddComponent<PadWobble>();
            w.stage = s;
            w.P = s.P;
            w.obs = s.Obstacles;
            w.W = s.L.widthPx > 0 ? s.L.widthPx : 640;
            w.H = s.L.heightPx > 0 ? s.L.heightPx : 400;
            // (over the front layer and its incoming period, under the lure and the float on the pad: StageView.OrderLight)
            w.eraseSr = w.Layer("PadErase", -0.02f);
            w.padSr = w.Layer("Pad", -0.03f);
            return w;
        }

        SpriteRenderer Layer(string name, float z)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3(0f, 0f, z);
            sr.sortingOrder = StageView.OrderFront;
            sr.enabled = false;
            return sr;
        }

        /// <summary>The pad wobbles now (a wobble already playing starts over).</summary>
        public void Wobble(Obstacle pad)
        {
            if (pad == null) return;
            var c = CutOf(pad);
            if (c == null || c.pixels == 0) return;
            cur = c;
            t = 0f;
            Wobbles++;
            LastPixels = c.pixels;
            eraseSr.sprite = c.erase;
            padSr.sprite = c.pad;
            Place();
        }

        void Update()
        {
            if (t < 0f) return;
            t += Time.deltaTime;
            Place();
        }

        void Place()
        {
            int f = Mathf.FloorToInt(t / FrameTime);
            bool on = cur != null && f < Shifts.Length;
            eraseSr.enabled = padSr.enabled = on;
            ShiftNow = on ? Shifts[f] : Vector2Int.zero;
            if (!on)
            {
                t = -1f;
                return;
            }
            var at = cur.at;
            eraseSr.transform.localPosition = new Vector3(at.x, at.y, -0.02f);
            padSr.transform.localPosition = new Vector3(at.x + ShiftNow.x / (float)PixelView.PPU, at.y + ShiftNow.y / (float)PixelView.PPU, -0.03f);
        }

        /// <summary>The pad cut out of this period's front layer (made once per pad and period; null: no art).</summary>
        Cut CutOf(Obstacle pad)
        {
            var fs = stage.FrontA != null ? stage.FrontA.sprite : null;
            var bs = stage.BackA != null ? stage.BackA.sprite : null;
            if (fs == null || bs == null || fs.texture.width != W || fs.texture.height != H || bs.texture.width != W || bs.texture.height != H) return null;
            if (cuts.TryGetValue(pad.id, out var c) && c.front == fs) return c;
            var front = Texels(fs.texture);
            var back = Texels(bs.texture);
            // the pad's footprint on the canvas (+2 px)
            float halfW = W / 2f, halfH = H / 2f;
            float c0 = float.MaxValue, c1 = float.MinValue, r0 = float.MaxValue, r1 = float.MinValue;
            foreach (var v in pad.Poly)
            {
                var q = P.ToPixel(new Vector3(v.x, pad.top, v.y), out _);
                c0 = Mathf.Min(c0, halfW + q.x);
                c1 = Mathf.Max(c1, halfW + q.x);
                r0 = Mathf.Min(r0, halfH + q.y);
                r1 = Mathf.Max(r1, halfH + q.y);
            }
            int x0 = Mathf.Clamp(Mathf.FloorToInt(c0) - 2, 0, W - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(c1) + 2, 0, W - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(r0) - 2, 0, H - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(r1) + 2, 0, H - 1);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            // its pixels: painted in the front layer, their water point in the footprint (+0.12 m: the rim)
            var mine = new bool[w * h];
            int n = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y0 + y) * W + x0 + x;
                if (front[i].a <= 8) continue;
                var s2 = new Vector2((x0 + x + 0.5f - halfW) / PixelView.PPU, (y0 + y + 0.5f - halfH) / PixelView.PPU);
                if (!P.ToPlane(s2, pad.top, out var hit) || !obs.Inside(pad, new Vector2(hit.x, hit.z), 0.12f)) continue;
                mine[y * w + x] = true;
                n++;
            }
            var erase = new Color32[w * h];
            var padPx = new Color32[w * h];
            for (int i = 0; i < w * h; i++)
            {
                int g = (y0 + i / w) * W + x0 + i % w;
                if (!mine[i]) continue;
                erase[i] = back[g];
                erase[i].a = 255;
                padPx[i] = front[g];
            }
            if (c == null)
            {
                c = new Cut();
                cuts[pad.id] = c;
            }
            else
            {
                if (c.erase != null) Destroy(c.erase.texture);
                if (c.pad != null) Destroy(c.pad.texture);
            }
            c.front = fs;
            c.pixels = n;
            c.at = new Vector2((x0 - halfW) / PixelView.PPU, (y0 - halfH) / PixelView.PPU);
            c.erase = Make(erase, w, h, "pad_erase_" + pad.id);
            c.pad = Make(padPx, w, h, "pad_" + pad.id);
            return c;
        }

        Color32[] Texels(Texture2D tex)
        {
            if (!texels.TryGetValue(tex, out var px)) texels[tex] = px = WaterFx.ReadTexture(tex);
            return px;
        }

        static Sprite Make(Color32[] px, int w, int h, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            t.SetPixels32(px);
            t.Apply(false);
            return Sprite.Create(t, new Rect(0, 0, w, h), Vector2.zero, PixelView.PPU);
        }

        void OnDestroy()
        {
            foreach (var c in cuts.Values)
            {
                if (c.erase != null) Destroy(c.erase.texture);
                if (c.pad != null) Destroy(c.pad.texture);
            }
        }
    }
}
