using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Paints the rod pixel by pixel along its bent curve every frame, in the layout of its shop icon: rear grip, reel
    /// seat, fore grip with a winding check, a two-tone blank (lit top, shaded underside) tapering from 2 px to 1 px,
    /// thread wraps with line guides hanging on the underside, a tip-top ring, and per rod: bamboo nodes, a gimbal butt
    /// (big-game) or a glowing tip and pommel (dragon rod). The pixels land on the pixel view's grid, so the rod reads
    /// like the other pixel sprites however it bends.
    /// </summary>
    public class RodPainter
    {
        const int Size = 256;                        // texture side (px); the rod rarely spans more than ~180 px
        const float Ppu = PixelView.PPU;
        // layout along the rod in metres from the hand (negative = the butt behind the hand): rear grip from the butt,
        // the reel seat centred on ReelS (where Angler hangs the reel), then the fore grip
        const float ButtS = -0.18f, SeatHalf = 0.09f, ForeLen = 0.24f;

        /// <summary>The reel seat's middle along the rod (Angler.ReelS2D / ReelS3D: where the reel hangs).</summary>
        public float ReelS = Angler.ReelS2D;
        float SeatS0 => ReelS - SeatHalf;
        float SeatS1 => ReelS + SeatHalf;
        float ForeS1 => SeatS1 + ForeLen;

        public readonly SpriteRenderer Sr;
        /// <summary>World positions of the line guides (hand to tip) and the tip top, after <see cref="Paint"/>.</summary>
        public readonly List<Vector2> Guides = new List<Vector2>();
        /// <summary>The rod's axis, butt to tip, in world units x 16 (pixels), after <see cref="Paint"/>.</summary>
        public IReadOnlyList<Vector2> Axis => pts;

        readonly Texture2D tex;
        readonly Color32[] px = new Color32[Size * Size];
        readonly List<Vector2> pts = new List<Vector2>();
        readonly List<float> ss = new List<float>();
        readonly float[] guideS = new float[5];
        Color32 blank, blankHi, blank2, grip, gripHi, grip2, seat, seatHi, wrap, metal, glow;
        bool nodes, gimbal, thick, hasGlow;

        public RodPainter(Transform parent)
        {
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Sr = new GameObject("Rod").AddComponent<SpriteRenderer>();
            Sr.transform.SetParent(parent, false);
            Sr.sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), Vector2.zero, Ppu);
        }

        public void SetRod(RodDef r)
        {
            blank = r.blank;
            blankHi = Color.Lerp(r.blank, Color.white, 0.3f);
            blank2 = r.blank2;
            grip = r.grip;
            gripHi = Color.Lerp(r.grip, Color.white, 0.22f);
            grip2 = Color.Lerp(r.grip, Color.black, 0.35f);
            seat = r.seat;
            seatHi = Color.Lerp(r.seat, Color.white, 0.4f);
            wrap = r.wrap;
            metal = new Color32(0xd8, 0xdc, 0xe4, 0xff);
            glow = r.glow;
            hasGlow = r.glow.a > 0f;
            nodes = r.nodes;
            gimbal = r.gimbal;
            thick = r.thick;
        }

        /// <param name="at">world 2D position of the rod axis at s metres from the hand (negative: behind the hand)</param>
        /// <param name="len">rod length (metres)</param>
        public void Paint(Func<float, Vector2> at, float len)
        {
            // sample the axis densely enough (<= 0.5 px apart) for gap-free pixels
            float approx = 0f;
            var prev = at(ButtS);
            for (int i = 1; i <= 24; i++)
            {
                var p = at(Mathf.Lerp(ButtS, len, i / 24f));
                approx += (p - prev).magnitude * Ppu;
                prev = p;
            }
            int n = Mathf.Clamp(Mathf.CeilToInt(approx * 2.2f), 16, 1200);
            pts.Clear();
            ss.Clear();
            var min = new Vector2(float.MaxValue, float.MaxValue);
            for (int i = 0; i <= n; i++)
            {
                float s = Mathf.Lerp(ButtS, len, i / (float)n);
                var p = at(s) * Ppu;
                pts.Add(p);
                ss.Add(s);
                min = Vector2.Min(min, p);
            }
            var origin = new Vector2(Mathf.Floor(min.x) - 4, Mathf.Floor(min.y) - 4);
            Array.Clear(px, 0, px.Length);

            float blank2Until = ForeS1 + (len - ForeS1) * (thick ? 0.7f : 0.45f);
            // guides: five like the shop icon, from just past the fore grip to near the tip, then the tip top
            for (int k = 0; k < 5; k++) guideS[k] = ForeS1 + 0.2f + (len - ForeS1 - 0.35f) * k / 4f;

            Guides.Clear();
            int nextGuide = 0;
            for (int i = 0; i <= n; i++)
            {
                float s = ss[i];
                var p = pts[i] - origin;
                var tan = pts[Mathf.Min(i + 1, n)] - pts[Mathf.Max(i - 1, 0)];
                var nrm = tan.sqrMagnitude > 1e-6f ? new Vector2(-tan.y, tan.x).normalized : Vector2.down;
                if (nrm.y > 0f || (Mathf.Abs(nrm.y) < 0.2f && nrm.x < 0f)) nrm = -nrm; // +nrm = the rod's underside on screen

                // body: 3 px through grips and seat, then the blank
                Color32 top, mid, bot;
                int w;
                if (s < SeatS0 || (s >= SeatS1 && s < ForeS1)) { w = 3; top = gripHi; mid = grip; bot = grip2; }
                else if (s < SeatS1) { w = 3; top = seatHi; mid = seat; bot = seat; }
                else if (s < blank2Until) { w = 2; top = blankHi; mid = blank; bot = blank2; }
                else { w = 1; top = mid = bot = blank; }
                // bands across the rod
                bool band = false;
                Color32 bandCol = default;
                if (s < ButtS + 0.05f) { band = true; bandCol = gimbal ? seat : grip2; }             // butt cap / gimbal
                if (Near(s, ForeS1, len, n)) { band = true; bandCol = seat; }                       // winding check
                if (nodes && s > ForeS1 + 0.1f && Mathf.Repeat(s - ForeS1, 0.3f) < (len - ButtS) / n * 1.2f) { band = true; bandCol = blank2; }
                for (int k = 0; k < 5; k++) if (Mathf.Abs(s - guideS[k]) < 0.035f) { band = true; bandCol = wrap; }
                if (band) top = mid = bot = bandCol;

                if (w == 3) { Plot(p - nrm, top); Plot(p, mid); Plot(p + nrm, bot); }
                else if (w == 2) { Plot(p, mid); Plot(p + nrm, bot); }
                else Plot(p, mid);

                // guide rings hang on the underside: bigger near the reel, smaller towards the tip
                if (nextGuide < 5 && s >= guideS[nextGuide])
                {
                    float off = w >= 2 ? 2f : 1.5f;
                    var ring = p + nrm * off;
                    Plot(ring, metal);
                    if (nextGuide < 2) Plot(ring + nrm, metal);
                    Guides.Add((ring + nrm * (nextGuide < 2 ? 1f : 0.5f) + origin) / Ppu);
                    nextGuide++;
                }
            }
            // tip top and the dragon rod's glow
            var tip = pts[n] - origin;
            Plot(tip, metal);
            Guides.Add((tip + origin) / Ppu);
            if (hasGlow)
            {
                bool on = Mathf.Repeat(Time.time * 1.2f, 1f) < 0.5f;
                var g = on ? glow : (Color32)Color.Lerp(glow, Color.white, 0.4f);
                Plot(tip, g);
                var butt = pts[0] - origin;
                Plot(butt, g);
                Plot(butt + Vector2.right, g);
            }

            tex.SetPixels32(px);
            tex.Apply(false);
            Sr.transform.position = origin / Ppu;
        }

        bool Near(float s, float at, float len, int n) => Mathf.Abs(s - at) < (len - ButtS) / n * 1.5f;

        void Plot(Vector2 p, Color32 c)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            if ((uint)x >= Size || (uint)y >= Size) return;
            px[y * Size + x] = c;
        }
    }
}
