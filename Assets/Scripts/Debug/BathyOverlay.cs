using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The generated bed made visible for the tests (Docs/terrain_depth_spec.md 13), never in normal play:
    /// <list type="bullet">
    /// <item>-fkbathy show: the bed's contours on the water (every 4-neighbour node edge crossing a whole metre from 1 to
    /// 8 m: a 1 px dot where it crosses, cream at 1 m .. deep blue at 8 m), the drop-off-beside-weed nodes (orange dots)
    /// and the legend's lurk candidates (gold 3 px crosses), on a canvas-sized point-filtered layer at sorting order 39,
    /// under the front layer: the prototype of the phase-3 radar;</item>
    /// <item>-fkbathy dump: a top-down picture of the grid (BathyGen.DumpPixels, 4 px a node) written once per build as
    /// bathy_&lt;stage&gt;_&lt;world seed&gt;.png into -fkshots (else persistentDataPath/shots).</item>
    /// </list>
    /// </summary>
    public class BathyOverlay : MonoBehaviour
    {
        public const int Order = 39;

        static readonly HashSet<uint> dumped = new HashSet<uint>();

        /// <summary>Where the dumps go: -fkshots, else persistentDataPath/shots.</summary>
        static string ShotsDir()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fkshots");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "shots");
        }

        /// <summary>-fkbathy dump: the top-down picture of this grid (once per grid).</summary>
        public static void WriteDump(Bathymetry b, string name = null)
        {
            if (b == null || (name == null && !dumped.Add(b.Hash))) return;
            var px = BathyGen.DumpPixels(b, TerrainRecipes.For(b.StageId), Obstacles.ReadSet(b.StageId), 4, out int w, out int h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false);
            string dir = ShotsDir();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, (name ?? $"bathy_{b.StageId}_{b.WorldSeed}") + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[BATHY] dump " + path);
        }

        StageView stage;
        SpriteRenderer sr;
        Texture2D tex;
        int W, H;

        /// <summary>-fkbathy show: the overlay over the stage (StageView.Init).</summary>
        public static BathyOverlay Create(StageView s)
        {
            var go = new GameObject("BathyOverlay");
            go.transform.SetParent(s.transform, false);
            var o = go.AddComponent<BathyOverlay>();
            o.stage = s;
            o.Build();
            return o;
        }

        /// <summary>Contour dots drawn in the last build (for the test).</summary>
        public int Dots { get; private set; }

        void Build()
        {
            var L = stage.L;
            var b = L.Bathy;
            if (b == null) return;
            W = L.widthPx > 0 ? L.widthPx : 640;
            H = L.heightPx > 0 ? L.heightPx : 400;
            var px = new Color32[W * H];
            var P = stage.P;
            bool Canvas(float x, float z, out int c, out int r)
            {
                var q = P.ToPixel(new Vector3(x, 0f, z), out float depth);
                c = Mathf.FloorToInt(W * 0.5f + q.x);
                r = Mathf.FloorToInt(H * 0.5f + q.y);
                return depth > 0.1f && c >= 0 && c < W && r >= 0 && r < H;
            }
            Color32 Level(int m)
            {
                float t = Mathf.InverseLerp(1f, 8f, m);
                return Color.Lerp(new Color(1f, 0.95f, 0.78f), new Color(0.12f, 0.25f, 0.85f), t);
            }
            // the drop-off nodes beside weed (orange, every other node: a band, not a fill) and the lurk candidates (gold
            // crosses: 4.5 m+, z 14-30); the contours over them
            var orange = new Color32(255, 150, 40, 255);
            var gold = new Color32(255, 214, 64, 255);
            for (int k = 0; k < b.NodeCount; k++)
            {
                if ((b.NodeFlags(k) & BedFlag.WeedEdge) == 0) continue;
                var p = b.NodePos(k);
                if (!Canvas(p.x, p.y, out int c, out int r)) continue;
                bool lurk = b.NodeDepth(k) >= 4.5f && p.y >= 14f && p.y <= 30f;
                if (!lurk)
                {
                    if (((k % b.Nx) & 1) == 0 && ((k / b.Nx) & 1) == 0) px[r * W + c] = orange;
                    continue;
                }
                foreach (var (dx, dy) in new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    int cc = c + dx, rr = r + dy;
                    if (cc >= 0 && cc < W && rr >= 0 && rr < H) px[rr * W + cc] = gold;
                }
            }
            int dots = 0;
            void Cross(int k, int k2)
            {
                float d0 = b.NodeDepth(k), d1 = b.NodeDepth(k2);
                int lo = Mathf.FloorToInt(Mathf.Min(d0, d1)) + 1, hi = Mathf.FloorToInt(Mathf.Max(d0, d1));
                for (int m = Mathf.Max(1, lo); m <= Mathf.Min(8, hi); m++)
                {
                    float t = Mathf.Abs(d1 - d0) > 1e-5f ? (m - d0) / (d1 - d0) : 0.5f;
                    var p = Vector2.Lerp(b.NodePos(k), b.NodePos(k2), t);
                    if (p.y < L.zNear) continue;
                    if (!Canvas(p.x, p.y, out int c, out int r)) continue;
                    px[r * W + c] = Level(m);
                    dots++;
                }
            }
            for (int j = 0; j < b.Nz; j++)
            for (int i = 0; i < b.Nx; i++)
            {
                int k = j * b.Nx + i;
                if (i < b.Nx - 1) Cross(k, k + 1);
                if (j < b.Nz - 1) Cross(k, k + b.Nx);
            }
            Dots = dots;
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false);
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), PixelView.PPU);
            sr.sortingOrder = Order;
            Debug.Log($"[BATHY] show: {dots} contour dots over {W}x{H}");
        }

        void LateUpdate()
        {
            if (stage != null) transform.localPosition = stage.DeckBob;
        }

        void OnDestroy()
        {
            if (tex != null) Destroy(tex);
        }
    }
}
