using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The line parting (a fight's break, a snag forced or cut): its freed end whips back over ~0.4 s, a slack piece of
    /// line curling as it goes, so a break never looks like a fish merely getting off (then the line stays on the rig).
    /// <para><see cref="Kind.Home"/>: the whole rig is gone (a lure, or a float rig whose line parted above the float): the
    /// line's free end flies from where it met the water (or the fish in the air) back up to the rod tip, sagging and
    /// whipping, and is gone there (the bait dangles again at the tip from then on).</para>
    /// <para><see cref="Kind.Recoil"/>: a float rig whose line parted on the hook's side of the float: under the water the
    /// line from the float springs back from the break point, the bare hook on its end, to the hook's place under the
    /// float (<see cref="Tackle.HookOverride"/> carries the hook meanwhile).</para>
    /// Updated before the Tackle draws (its order 90: the hook's place) and after the Angler has posed the rod (50: the tip).
    /// </summary>
    [DefaultExecutionOrder(85)]
    public class LineSnap : MonoBehaviour
    {
        public enum Kind { Home, Recoil }

        /// <summary>The whip's length (s).</summary>
        public const float Duration = 0.42f;
        const int Points = 24;

        StageView stage;
        Persp P;
        Angler angler;
        Tackle tackle;
        LineRenderer air, under;
        Vector3 from, freeEnd;
        float t;
        bool on;

        /// <summary>A snap is playing.</summary>
        public bool Active => on;
        public Kind Playing { get; private set; }
        /// <summary>0..1 through the current snap (1 when none plays).</summary>
        public float Progress => on ? Mathf.Clamp01(t / Duration) : 1f;
        /// <summary>For the tests: snaps started, frames drawn in all of them, the kind and frames of the last one.</summary>
        public int Plays { get; private set; }
        public int FramesDrawn { get; private set; }
        public int LastFrames { get; private set; }
        public Kind LastKind { get; private set; }
        /// <summary>The free end now (game space; for the tests).</summary>
        public Vector3 FreeEnd => freeEnd;
        /// <summary>Where the last snap started (the break point; game space; for the tests).</summary>
        public Vector3 From => from;

        public static LineSnap Create(StageView s, Angler a, Tackle tk)
        {
            var go = new GameObject("LineSnap");
            var ls = go.AddComponent<LineSnap>();
            ls.stage = s;
            ls.P = s.P;
            ls.angler = a;
            ls.tackle = tk;
            ls.air = ls.MakeLine("SnapAir", Angler.OrderLine);
            FrontOcclusion.Use(ls.air);   // (in the air over the front layer: hidden where the pier / rocks are nearer, as the line)
            ls.under = ls.MakeLine("SnapUnder", Angler.OrderUnderLine);
            return ls;
        }

        LineRenderer MakeLine(string name, int order)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.material = Angler.LineMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = Points;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.sortingOrder = order;
            lr.textureMode = LineTextureMode.Stretch;
            lr.widthMultiplier = 1f / PixelView.PPU;
            lr.enabled = false;
            return lr;
        }

        /// <summary>The whole rig is gone: the line's free end flies back from <paramref name="end"/> to the rod tip.</summary>
        public void Home(Vector3 end) => Begin(Kind.Home, end);

        /// <summary>The line parted on the hook's side of the float: the hook springs back from <paramref name="breakAt"/> to under the float.</summary>
        public void Recoil(Vector3 breakAt) => Begin(Kind.Recoil, breakAt);

        void Begin(Kind k, Vector3 at)
        {
            if (on) Stop();
            Playing = LastKind = k;
            from = at;
            freeEnd = at;
            t = 0f;
            on = true;
            Plays++;
            LastFrames = 0;
            if (k == Kind.Recoil) tackle.HookOverride = at;
        }

        public void Stop()
        {
            if (!on) return;
            on = false;
            air.enabled = under.enabled = false;
            if (Playing == Kind.Recoil) tackle.HookOverride = null;
        }

        /// <summary>The free end's way: fast at first (the line's stretch let go), easing in; through the air it arcs up a little.</summary>
        Vector3 EndAt(float u, Vector3 to)
        {
            float k = 1f - Mathf.Pow(1f - u, 2.4f);
            var p = Vector3.Lerp(from, to, k);
            if (Playing == Kind.Home) p.y += Vector3.Distance(from, to) * 0.18f * Mathf.Sin(Mathf.PI * k);
            return p;
        }

        Vector3 Anchor => Playing == Kind.Home ? angler.RodTip : new Vector3(tackle.Surface.x, 0f, tackle.Surface.z);

        void Update()
        {
            if (!on) return;
            if (Playing == Kind.Recoil && tackle.State != Tackle.Mode.Water)
            {
                Stop();
                return;
            }
            t += Time.deltaTime;
            if (t >= Duration)
            {
                Stop();
                return;
            }
            var to = Playing == Kind.Home ? angler.RodTip : tackle.HookPos;
            freeEnd = EndAt(t / Duration, to);
            if (Playing == Kind.Recoil) tackle.HookOverride = freeEnd;
        }

        void LateUpdate()
        {
            if (!on) return;
            float u = Mathf.Clamp01(t / Duration);
            var a = Anchor;
            var e = freeEnd;
            bool home = Playing == Kind.Home;
            var lr = home ? air : under;
            (home ? under : air).enabled = false;
            lr.enabled = true;
            // the line's colour (as the angler's: the period tint; under the water its dimmed blue)
            var tint = stage.ActorTint;
            var line = Game.I.Line.color;
            var lc = line * new Color(tint.r, tint.g, tint.b, 1f);
            lc.a = line.a * (u < 0.7f ? 1f : Mathf.Clamp01((1f - u) / 0.3f));
            if (!home) lc = stage.UnderwaterLine(lc);
            lr.startColor = lr.endColor = lc;
            // a slack piece: sagging (in the air) and whipping (a wave running out to the free end, growing towards it)
            float len = Vector3.Distance(a, e);
            Vector2 a2 = Proj(a, home, out _), e2 = Proj(e, home, out _);
            var d2 = e2 - a2;
            var perp = d2.sqrMagnitude > 1e-8f ? new Vector2(-d2.y, d2.x).normalized : Vector2.right;
            float amp = Mathf.Clamp(d2.magnitude * 0.16f, 2f / PixelView.PPU, 14f / PixelView.PPU) * (1f - 0.5f * u);
            var bob = home ? stage.DeckBob : Vector2.zero;
            for (int i = 0; i < Points; i++)
            {
                float s = i / (Points - 1f);
                var p = Vector3.Lerp(a, e, s);
                if (home) p.y = Mathf.Max(p.y - len * 0.12f * Mathf.Sin(Mathf.PI * s) * (0.4f + 0.6f * u), angler.SurfaceUnder(p)); // (it sags onto the water or the ice, never through it)
                var p2 = Proj(p, home, out float pd);
                p2 += perp * (amp * Mathf.Pow(s, 1.4f) * Mathf.Sin(Mathf.PI * 2f * (1.6f * s - 2.4f * u)));
                p2 += bob * (1f - s);
                lr.SetPosition(i, home ? FrontOcclusion.Point(p2, pd) : (Vector3)p2);
            }
            LastFrames++;
            FramesDrawn++;
        }

        /// <summary>On screen (pixel scene): in the air as it is, under the water where it is seen (refracted).</summary>
        Vector2 Proj(Vector3 p, bool home, out float depth)
        {
            if (!home && p.y < 0f)
            {
                depth = 0f;
                return P.To2D(P.Apparent(p));
            }
            return P.To2D(p, out depth);
        }

        void OnDestroy()
        {
            if (tackle != null && on && Playing == Kind.Recoil) tackle.HookOverride = null;
        }
    }
}
