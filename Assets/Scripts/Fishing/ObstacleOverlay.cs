using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The obstacles on screen (Docs/obstacles_spec.md 9, 12):
    /// <list type="bullet">
    /// <item>while winding up a cast (the fan showing): faint dotted outlines of the UNDERWATER snag and weed zones within
    /// the rod's cast + 2 m, at their top (refracted), 1 px dashed 2 on / 2 off, snag #d8f0ff, weed #b8e8a0; they fade in
    /// with the fan (0.15 s) and out at the release (0.12 s), at the fan's alpha (0.22 pulled, 0.32 armed) with its shimmer.
    /// Solids, pads and covers are painted: never outlined. The layer sorts under the fan dots and under the front layer, so
    /// no outline shows over a painted prop;</item>
    /// <item>-fkobstacles show: every obstacle in every state, colour-coded like the Blender overlay (solid tiers #ffe040
    /// with a 20 % fill, snag #60e0ff dashed, weed #a8f070 dashed, pad #70f0a0, cover #ff70d8 dashed with a 7 px cross at the
    /// hold point, rim #ffffff), over the front layer.</item>
    /// </list>
    /// Both are static 640x400 point-filtered textures placed like the stage's layers (centred on the scene origin, 16 px
    /// per unit); the aim one is rebuilt only when he has walked or changed rods since it was drawn.
    /// </summary>
    public class ObstacleOverlay : MonoBehaviour
    {
        public const int OrderAim = Fx.OrderRipple + 1, OrderShow = Fx.OrderSplash - 1;
        static readonly Color32 SnagCol = new Color32(0xd8, 0xf0, 0xff, 0xff), WeedCol = new Color32(0xb8, 0xe8, 0xa0, 0xff);

        FishingController ctl;
        StageView stage;
        Persp P;
        StageLayout L;
        Obstacles obs;
        SpriteRenderer aimSr, showSr;
        Texture2D aimTex, showTex;
        int W, H;
        float alpha, builtX = float.NaN, builtCast = -1f;
        bool want, armed;

        /// <summary>Zones outlined in the aim layer as last built, and its alpha now (for the tests).</summary>
        public int ZonesDrawn { get; private set; }
        public float Alpha => alpha;
        public bool ShowDrawn => showSr != null && showSr.enabled;

        public static ObstacleOverlay Create(FishingController c)
        {
            var go = new GameObject("ObstacleOverlay");
            go.transform.SetParent(c.Stage.transform, false);
            var o = go.AddComponent<ObstacleOverlay>();
            o.ctl = c;
            o.stage = c.Stage;
            o.P = c.Stage.P;
            o.L = c.Stage.L;
            o.obs = c.Stage.Obstacles;
            o.W = o.L.widthPx > 0 ? o.L.widthPx : 640;
            o.H = o.L.heightPx > 0 ? o.L.heightPx : 400;
            o.aimSr = o.Layer("Aim", OrderAim);
            return o;
        }

        SpriteRenderer Layer(string name, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sortingOrder = order;
            sr.enabled = false;
            return sr;
        }

        /// <summary>The wind-up's state this frame (FishingController.DrawFan): shown, armed, where he stands, the rod's cast.</summary>
        public void Aim(bool show, bool isArmed, float anchorX, float castDist)
        {
            want = show && obs != null && !obs.Empty;
            armed = isArmed;
            if (want && (float.IsNaN(builtX) || Mathf.Abs(anchorX - builtX) > 0.5f || Mathf.Abs(castDist - builtCast) > 0.01f)) BuildAim(anchorX, castDist);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float target = want ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, target, dt / (want ? 0.15f : 0.12f));
            if (aimSr != null)
            {
                aimSr.enabled = alpha > 0.001f && aimTex != null;
                if (aimSr.enabled)
                {
                    // the fan's own levels and shimmer
                    float baseA = armed ? 0.32f : 0.22f;
                    float wave = armed ? 0.08f * Mathf.Max(0f, Mathf.Sin(Time.time * 5f)) : 0f;
                    aimSr.color = new Color(1f, 1f, 1f, alpha * (baseA + wave));
                }
            }
            bool show = Obstacles.Show && obs != null && !obs.Empty;
            if (show && showTex == null) BuildShow();
            if (showSr != null) showSr.enabled = show;
            // (the ocean's front layer bobs with the swell: the hull zone with it)
            transform.localPosition = stage.DeckBob;
        }

        // ------------------------------------------------------------------ drawing
        Color32[] px;

        void Clear()
        {
            px = new Color32[W * H];
        }

        Sprite Commit(ref Texture2D tex)
        {
            if (tex == null) tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), PixelView.PPU);
        }

        /// <summary>A game point on the stage canvas (pixels, y up from the bottom), false behind the camera.</summary>
        bool Canvas(Vector3 p, out Vector2 c)
        {
            var q = P.ToPixel(p, out float depth);
            c = new Vector2(W * 0.5f + q.x, H * 0.5f + q.y);
            return depth > 0.1f;
        }

        void Plot(int c, int r, Color32 col)
        {
            if (c < 0 || c >= W || r < 0 || r >= H) return;
            px[r * W + c] = col;
        }

        void Blend(int c, int r, Color32 col)
        {
            if (c < 0 || c >= W || r < 0 || r >= H) return;
            int i = r * W + c;
            if (px[i].a < col.a) px[i] = col;
        }

        /// <summary>The polygon at height y (refracted when under water) as canvas points, the edges cut into <= 0.25 m steps.</summary>
        List<Vector2> Project(Vector2[] poly, float y, bool apparent)
        {
            var list = new List<Vector2>();
            int n = poly.Length;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 0.25f));
                for (int k = 0; k < steps; k++)
                {
                    var w = Vector2.Lerp(a, b, k / (float)steps);
                    var p3 = new Vector3(w.x, y, w.y);
                    if (apparent && y < 0f) p3 = P.Apparent(p3);
                    if (!Canvas(p3, out var c)) return null;
                    list.Add(c);
                }
            }
            return list;
        }

        static float Span(List<Vector2> pts)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var p in pts)
            {
                x0 = Mathf.Min(x0, p.x);
                x1 = Mathf.Max(x1, p.x);
                y0 = Mathf.Min(y0, p.y);
                y1 = Mathf.Max(y1, p.y);
            }
            return Mathf.Max(x1 - x0, y1 - y0);
        }

        /// <summary>A closed outline through the canvas points, 1 px, dashed 2 on / 2 off along the perimeter (or solid).</summary>
        void Outline(List<Vector2> pts, Color32 col, bool dashed)
        {
            int k = 0, lastC = int.MinValue, lastR = int.MinValue;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))));
                for (int s = 0; s < steps; s++)
                {
                    var q = Vector2.Lerp(a, b, s / (float)steps);
                    int c = Mathf.FloorToInt(q.x), r = Mathf.FloorToInt(q.y);
                    if (c == lastC && r == lastR) continue;
                    lastC = c;
                    lastR = r;
                    if (!dashed || (k & 3) < 2) Plot(c, r, col);
                    k++;
                }
            }
        }

        /// <summary>Fills the convex hull of the canvas points (at the colour's alpha).</summary>
        void Fill(List<Vector2> pts, Color32 col)
        {
            var hull = Obstacles.Hull(pts);
            if (hull.Length < 3) return;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var p in hull)
            {
                x0 = Mathf.Min(x0, p.x);
                x1 = Mathf.Max(x1, p.x);
                y0 = Mathf.Min(y0, p.y);
                y1 = Mathf.Max(y1, p.y);
            }
            for (int r = Mathf.Max(0, Mathf.FloorToInt(y0)); r <= Mathf.Min(H - 1, Mathf.CeilToInt(y1)); r++)
            for (int c = Mathf.Max(0, Mathf.FloorToInt(x0)); c <= Mathf.Min(W - 1, Mathf.CeilToInt(x1)); c++)
                if (Obstacles.InPoly(hull, new Vector2(c + 0.5f, r + 0.5f))) Blend(c, r, col);
        }

        /// <summary>The aim layer: the snag and weed zones within the cast + 2 m of where he stands.</summary>
        void BuildAim(float anchorX, float castDist)
        {
            builtX = anchorX;
            builtCast = castDist;
            Clear();
            int n = 0;
            var anchor = new Vector2(anchorX, 0f);
            void Draw(Obstacle o, Color32 col)
            {
                if (Obstacles.Dist(o, anchor) > castDist + 2f) return;
                var pts = Project(o.Poly, o.top, true);
                if (pts == null || pts.Count < 3 || Span(pts) < 4f) return;
                Outline(pts, col, true);
                n++;
            }
            foreach (var o in obs.Snags) Draw(o, SnagCol);
            foreach (var o in obs.Weeds) Draw(o, WeedCol);
            ZonesDrawn = n;
            var old = aimSr.sprite;
            aimSr.sprite = Commit(ref aimTex);
            if (old != null) Destroy(old);
            Obstacles.Say(string.Format(System.Globalization.CultureInfo.InvariantCulture, "aim outlines: {0} zones within {1:0.0} m of x {2:0.00}", n, castDist + 2f, anchorX));
        }

        /// <summary>-fkobstacles show: everything, colour-coded.</summary>
        void BuildShow()
        {
            Clear();
            var solid = new Color32(0xff, 0xe0, 0x40, 0xff);
            var solidFill = new Color32(0xff, 0xe0, 0x40, 0x33);
            foreach (var o in obs.Solids)
            {
                if (o.TierPoly == null) continue;
                for (int k = 0; k < o.TierPoly.Length; k++)
                {
                    var lo = Project(o.TierPoly[k], o.TierY0[k], false);
                    var hi = Project(o.TierPoly[k], o.TierY1[k], false);
                    if (lo == null || hi == null) continue;
                    var all = new List<Vector2>(lo);
                    all.AddRange(hi);
                    Fill(all, solidFill);
                    Outline(new List<Vector2>(Obstacles.Hull(all)), solid, false);
                }
            }
            void Zone(IEnumerable<Obstacle> list, Color32 col, bool dashed)
            {
                foreach (var o in list)
                {
                    var pts = Project(o.Poly, Mathf.Min(0f, o.top), true);
                    if (pts == null || pts.Count < 3) continue;
                    Outline(pts, col, dashed);
                }
            }
            Zone(obs.Snags, new Color32(0x60, 0xe0, 0xff, 0xff), true);
            Zone(obs.Weeds, new Color32(0xa8, 0xf0, 0x70, 0xff), true);
            Zone(obs.Pads, new Color32(0x70, 0xf0, 0xa0, 0xff), false);
            Zone(obs.Rims, new Color32(0xff, 0xff, 0xff, 0xff), false);
            var cov = new Color32(0xff, 0x70, 0xd8, 0xff);
            Zone(obs.Covers, cov, true);
            foreach (var o in obs.Covers)
            {
                var hp = new Vector3(o.hx, o.top, o.hz);
                if (!Canvas(P.Apparent(hp), out var c)) continue;
                int cx = Mathf.FloorToInt(c.x), cy = Mathf.FloorToInt(c.y);
                for (int d = -3; d <= 3; d++)
                {
                    Plot(cx + d, cy, cov);
                    Plot(cx, cy + d, cov);
                }
            }
            if (showSr == null) showSr = Layer("Show", OrderShow);
            showSr.sprite = Commit(ref showTex);
            Debug.Log($"[OBST] {L.id}: {obs.Solids.Count} solid, {obs.Pads.Count} pad, {obs.Snags.Count} snag, {obs.Weeds.Count} weed, {obs.Covers.Count} cover, {obs.Rims.Count} rim (shown)");
        }
    }
}
