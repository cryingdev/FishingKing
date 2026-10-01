using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The moving water of a stage (Docs/time_currents_spec.md 8): the surface current <see cref="Water"/> (m/s on the
    /// water plane as (x, z)) and the wind drift of floating things <see cref="Wind"/>.
    /// <list type="bullet">
    /// <item>stream - a strong one-way flow down the channel towards the angler (-z), fastest in the meandering lane,
    ///                slack (and a slow back-eddy) behind the mid-stream rocks, surging every 6-9 s;</item>
    /// <item>sea    - the tidal stream along the breakwater (0.8 m/s x the tide's flow), to the harbour on the left on the
    ///                flood, out to sea on the right on the ebb; slack against the tetrapod piles;</item>
    /// <item>ocean  - the boat drifts: the water slides past it at ~0.18 m/s, its heading veering over hours;</item>
    /// <item>lake / swamp - still water; now and then a gust drifts floating things (the lake keeps a 0.02 m/s breeze);</item>
    /// <item>ice / cave - nothing.</item>
    /// </list>
    /// Created by <see cref="StageView"/> and ticked with the real frame time.
    /// </summary>
    public class CurrentField
    {
        public enum Kind { None, Stream, Sea, Ocean, Lake, Swamp }

        /// <summary>-fkcurrent: every current and gust x this (0 = still water).</summary>
        public static float Mult = 1f;
        /// <summary>-fkgust: the lake / swamp's first gust 2 s after the stage opens, then one every 12 s.</summary>
        public static bool DebugGust;
        /// <summary>-fkcurrentvivid: WaterFx draws the field as arrows.</summary>
        public static bool Vivid;
        /// <summary>Test hook (-fkauto zoom's frozen compares): the field's real-time clock stands still (gusts, surges, drift).</summary>
        public static bool DebugFreeze;

        public Kind K { get; }
        /// <summary>The stage's reference speed (m/s): the alignment of a fish's run with the current is measured against it.</summary>
        public float Ref { get; }
        /// <summary>The wind drift of floating things now (lake / swamp), uniform; zero elsewhere.</summary>
        public Vector2 Wind { get; private set; }
        /// <summary>Lake / swamp: the gust envelope 0..1.</summary>
        public float Gust01 { get; private set; }
        /// <summary>The direction of the last gust (unit, (x, z)).</summary>
        public Vector2 GustDir { get; private set; } = Vector2.right;
        /// <summary>Real seconds into the current gust (-1 = none).</summary>
        public float GustT { get; private set; } = -1f;
        /// <summary>Ocean: the drift accumulated since the stage opened (m, (x, z)): the swell pattern slides with it.</summary>
        public Vector2 Drift { get; private set; }
        /// <summary>Real seconds since the stage opened.</summary>
        public float T { get; private set; }

        readonly StageLayout L;
        // stream surges
        readonly System.Random rnd = new System.Random(1234);
        readonly List<float> surges = new List<float>();
        float nextSurge;
        // gusts
        float gustStart = -1f, gustLen, nextGust;
        readonly System.Random gustRnd = new System.Random();

        // the "Mid" boulders of hyb_stream.py (x, z, r): each has a slack pocket just downstream (nearer to the angler); the
        // centres are moved onto the exported rocks (the obstacle data's pocket-tagged solids, AlignRocks) when it loads
        static readonly Vector3[] Rocks =
        {
            new Vector3(-6.35f, 13.5f, 0.62f), new Vector3(6.3f, 25.0f, 0.78f), new Vector3(-6.1f, 44.0f, 1.0f),
            new Vector3(5.9f, 66.0f, 1.15f), new Vector3(-6.0f, 92.0f, 1.2f),
        };

        /// <summary>A stream pocket: centre (x, z) and semi-axes (ax, az).</summary>
        public struct PocketDef { public float x, z, ax, az; }

        public static PocketDef[] Pockets = BuildPockets();

        /// <summary>
        /// Keeps the stream's pockets on the painted rocks (Docs/obstacles_spec.md 3.2): each hard-coded rock takes the
        /// centre of the exported pocket rock nearest to it (within 0.5 m; they agree within ~0.05 m), its radius kept.
        /// </summary>
        public static void AlignRocks(System.Collections.Generic.List<Vector2> centres)
        {
            if (centres == null || centres.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Rocks.Length; i++)
            {
                var r = Rocks[i];
                int best = -1;
                float bd = 0.5f;
                for (int k = 0; k < centres.Count; k++)
                {
                    float d = (centres[k] - new Vector2(r.x, r.y)).magnitude;
                    if (d < bd) { bd = d; best = k; }
                }
                if (best < 0) continue;
                sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, " {0}:{1:0.000}m", i, bd));
                Rocks[i] = new Vector3(centres[best].x, centres[best].y, r.z);
            }
            Pockets = BuildPockets();
            Debug.Log("[OBST] stream pockets on the exported rocks:" + sb);
        }

        static PocketDef[] BuildPockets()
        {
            var p = new PocketDef[Rocks.Length];
            for (int i = 0; i < Rocks.Length; i++)
            {
                var r = Rocks[i];
                p[i] = new PocketDef { x = r.x, z = r.y - 2.2f * r.z, ax = 1.6f * r.z, az = 2.6f * r.z };
            }
            return p;
        }

        public CurrentField(StageLayout l)
        {
            L = l;
            switch (l.id)
            {
                case "stream": K = Kind.Stream; Ref = 0.55f; break;
                case "sea": K = Kind.Sea; Ref = 0.8f; break;
                case "ocean": K = Kind.Ocean; Ref = 0.25f; break;
                case "lake": K = Kind.Lake; Ref = 0.14f; break;
                case "swamp": K = Kind.Swamp; Ref = 0.06f; break;
                default: K = Kind.None; Ref = 1f; break;
            }
            if (K == Kind.Lake || K == Kind.Swamp) nextGust = DebugGust ? 2f : Gap();
        }

        /// <summary>Game hours (for the slow swell of the stream, the ocean's veer).</summary>
        static float Hours => GameClock.Min / 60f;

        // the sea's peak tidal flow (m/s, before the distance factor): 0.8 carried a float to the tetrapods in ~10 s
        const float SeaTide = 0.65f;

        // ---- the sea: the slack cushion against the tetrapod piles (Docs/time_currents_spec.md 8.3)
        /// <summary>Test hook (-fkauto tidebites' "before" runs): no cushion, the tide runs right up to the tetrapods as it did.</summary>
        public static bool DebugNoCushion;
        // the piles' tet solids (x, z, R) and their bounds grown by the cushion's width
        Vector3[] pile;
        Rect pileBox;
        // the tide dies away from CushionW m off a pile's footprint to CushionCore m off it
        const float CushionW = 3f, CushionCore = 0.4f;

        /// <summary>The sea: the tetrapod piles the tide runs slack against (<see cref="Obstacles.PileDiscs"/>).</summary>
        public void SetPile(List<Vector3> discs)
        {
            if (discs == null || discs.Count == 0) return;
            pile = discs.ToArray();
            float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
            foreach (var d in pile)
            {
                x0 = Mathf.Min(x0, d.x - d.z); x1 = Mathf.Max(x1, d.x + d.z);
                z0 = Mathf.Min(z0, d.y - d.z); z1 = Mathf.Max(z1, d.y + d.z);
            }
            pileBox = Rect.MinMaxRect(x0 - CushionW, z0 - CushionW, x1 + CushionW, z1 + CushionW);
        }

        /// <summary>
        /// The sea: the share of the running tide at (x, z), 1 in open water. Against the tetrapods the tide piles up and
        /// runs slack (the 반탄류 cushion): from 3 m off a pile's footprint (its nearest tet solid, hub or leg) it dies away
        /// (smoothstep) to nothing 0.4 m off it. 1 elsewhere, and with <see cref="DebugNoCushion"/>.
        /// </summary>
        public float Cushion(float x, float z)
        {
            if (K != Kind.Sea || pile == null || DebugNoCushion || !pileBox.Contains(new Vector2(x, z))) return 1f;
            float best = CushionW;
            foreach (var d in pile)
            {
                float dx = x - d.x, dz = z - d.y;
                if (Mathf.Abs(dx) - d.z >= best || Mathf.Abs(dz) - d.z >= best) continue;
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz) - d.z);
            }
            return SS(CushionCore, CushionW, best);
        }

        /// <summary>The sea: a rig at (x, z) is in the slack against the tetrapods (the water there under a third of the tide's).</summary>
        public bool Slack(float x, float z) => K == Kind.Sea && Cushion(x, z) < 0.35f;

        static float SS(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The depth factor for things under the surface: exp(-depth / 4).</summary>
        public static float Kd(float depth) => Mathf.Exp(-Mathf.Max(0f, depth) / 4f);

        public bool Moving => K == Kind.Stream || K == Kind.Sea || K == Kind.Ocean;
        public bool Windy => K == Kind.Lake || K == Kind.Swamp;

        public void Tick(float dt)
        {
            if (DebugFreeze) return;
            T += dt;
            switch (K)
            {
                case Kind.Stream:
                    while (T >= nextSurge)
                    {
                        surges.Add(nextSurge);
                        nextSurge += 6f + 3f * (float)rnd.NextDouble();
                    }
                    // (a surge takes at most 20 s to reach the angler and lasts 1.8 s)
                    surges.RemoveAll(s => T - s > 23f);
                    break;
                case Kind.Ocean:
                    Drift += Water(0f, 0f) * dt;
                    break;
                case Kind.Lake:
                case Kind.Swamp:
                    TickGust();
                    break;
            }
        }

        // ------------------------------------------------------------------ lake / swamp gusts
        float PeriodFactor()
        {
            var p = GameClock.Now;
            if (K == Kind.Lake) return p switch { Period.Dawn => 0.5f, Period.Day => 1.3f, Period.Evening => 0.6f, _ => 0.4f };
            return p == Period.Day ? 1f : 0.5f;
        }

        float Gap()
        {
            float g = K == Kind.Lake ? 20f + 25f * (float)gustRnd.NextDouble() : 35f + 35f * (float)gustRnd.NextDouble();
            return T + g / PeriodFactor();
        }

        void TickGust()
        {
            bool on = gustStart >= 0f && T < gustStart + gustLen;
            if (!on && T >= nextGust)
            {
                gustStart = T;
                gustLen = 6f + 4f * (float)gustRnd.NextDouble();
                float a = (-20f + 40f * (float)gustRnd.NextDouble()) * Mathf.Deg2Rad;
                GustDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                nextGust = DebugGust ? gustStart + 12f : gustStart + gustLen + (Gap() - T);
                on = true;
            }
            float e = 0f;
            if (on)
            {
                GustT = T - gustStart;
                float s = Mathf.Sin(Mathf.PI * GustT / gustLen);
                e = s * s;
            }
            else GustT = -1f;
            Gust01 = e;
            Wind = (K == Kind.Lake ? 0.02f + 0.12f * e : 0.06f * e) * Mult * GustDir;
        }

        // ------------------------------------------------------------------ stream geometry
        /// <summary>The thalweg (the fast lane's centre line) at z.</summary>
        public static float LaneX(float z) => 1.6f * Mathf.Sin(0.105f * z + 0.9f);
        static float LaneSlope(float z) => 0.168f * Mathf.Cos(0.105f * z + 0.9f);

        /// <summary>Stream: 1 in the middle of the lane, 0.35-0.4 at the banks; 1 elsewhere.</summary>
        public float Lane(float x, float z)
        {
            if (K != Kind.Stream) return 1f;
            float d = (x - LaneX(z)) / 3.2f;
            return 0.35f + 0.65f * Mathf.Exp(-d * d);
        }

        static float PocketD(int k, float x, float z)
        {
            var p = Pockets[k];
            float dx = (x - p.x) / p.ax, dz = (z - p.z) / p.az;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Stream: the nearest pocket's slack factor, 0.12 in a pocket's core .. 1 outside; 1 elsewhere.</summary>
        public float Pocket(float x, float z)
        {
            if (K != Kind.Stream) return 1f;
            float m = 1f;
            for (int k = 0; k < Pockets.Length; k++) m = Mathf.Min(m, 0.12f + 0.88f * SS(0.35f, 1f, PocketD(k, x, z)));
            return m;
        }

        /// <summary>Stream: the pocket this point is in (d &lt; 1), or -1.</summary>
        public int PocketIndex(float x, float z)
        {
            if (K != Kind.Stream) return -1;
            int best = -1;
            float bd = 1f;
            for (int k = 0; k < Pockets.Length; k++)
            {
                float d = PocketD(k, x, z);
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        /// <summary>Stream: the surge factor S at z (1 between surges, up to 1.45 in one); 1 elsewhere.</summary>
        public float Surge(float z)
        {
            if (K != Kind.Stream) return 1f;
            float g = 0f;
            float delay = Mathf.Clamp((60f - z) / 3f, 0f, 20f);
            foreach (float s in surges)
            {
                float tau = T - s - delay;
                if (tau < 0f || tau > 1.8f) continue;
                float v = Mathf.Sin(Mathf.PI * tau / 1.8f);
                g = Mathf.Max(g, v * v);
            }
            return 1f + 0.45f * g;
        }

        // ------------------------------------------------------------------ the current
        /// <summary>The water's surface current at (x, z) (m/s, (x, z)): stream / sea / ocean; zero elsewhere.</summary>
        public Vector2 Water(float x, float z)
        {
            switch (K)
            {
                case Kind.Stream:
                {
                    float u0 = 0.55f * (1f + 0.15f * Mathf.Sin(2f * Mathf.PI * Hours / 3f));
                    float slope = LaneSlope(z);
                    float dmin = 9f;
                    for (int k = 0; k < Pockets.Length; k++) dmin = Mathf.Min(dmin, PocketD(k, x, z));
                    // behind a rock the eddy runs back upstream, slowly
                    if (dmin < 0.5f) return 0.1f * u0 * Mult * new Vector2(slope, 1f).normalized;
                    float u = u0 * Surge(z) * Lane(x, z) * Pocket(x, z);
                    return u * Mult * new Vector2(-slope, -1f).normalized;
                }
                case Kind.Sea:
                {
                    var t = GameClock.Tide;
                    if (t.S <= 0f) return Vector2.zero;
                    var d = t.R >= 0f ? new Vector2(-1f, -0.15f).normalized : new Vector2(1f, 0.1f).normalized;
                    float k = (0.6f + 0.6f * SS(4f, 40f, z)) * (Mathf.Abs(x) > 5f && z < 8f ? 0.5f : 1f);
                    return SeaTide * t.S * k * Cushion(x, z) * Mult * d;
                }
                case Kind.Ocean:
                {
                    float h = Hours;
                    float v = 0.18f + 0.07f * Mathf.Sin(2f * Mathf.PI * h / 4f + 1f);
                    float th = (20f + 25f * Mathf.Sin(2f * Mathf.PI * h / 6f)) * Mathf.Deg2Rad;
                    return v * Mult * new Vector2(Mathf.Cos(th), Mathf.Sin(th));
                }
                default: return Vector2.zero;
            }
        }

        /// <summary>What drifts a floating thing at (x, z): the water plus the wind.</summary>
        public Vector2 Floating(float x, float z) => Water(x, z) + Wind;

        /// <summary>The current at an underwater point (the surface current x the depth factor).</summary>
        public Vector2 At(Vector3 p) => Water(p.x, p.z) * Kd(-p.y);

        /// <summary>For the vivid overlay: what kind of water this is (0 lane / open, 1 pocket, 2 eddy).</summary>
        public int Zone(float x, float z)
        {
            if (K != Kind.Stream) return 0;
            float dmin = 9f;
            for (int k = 0; k < Pockets.Length; k++) dmin = Mathf.Min(dmin, PocketD(k, x, z));
            return dmin < 0.5f ? 2 : dmin < 1f ? 1 : 0;
        }
    }
}
