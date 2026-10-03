using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Builds a stage's <see cref="Bathymetry"/> from its <see cref="TerrainRecipe"/> and a world seed
    /// (Docs/terrain_depth_spec.md 4, 5; Docs/lake_phase2_spec.md A1): a free base drawn from the seed's character (shallow
    /// or deep, weedy or rocky), flats (set-to blobs, more on weedy lakes), the lane carved, an old creek, holes, humps and
    /// noise; the distance profile P* derived from the result (the rows' medians), the grid's edges feathered back to it;
    /// the pins from the fixed art clamp it (twice, round a slope limit), then it is quantized to whole cm and its kinds,
    /// materials, flags, distances, zones and statistics derived. Every attempt is validated (V1-V11); after
    /// <see cref="TerrainRecipe.attempts"/> failures the fallback (the middle character, no features) is used.
    /// <para>Deterministic on every platform: SplitMix64 streams per feature family seeded by FNV-1a / Murmur fmix32,
    /// integer-lattice value noise, polynomial smoothsteps, Catmull-Rom meanders; no sin / cos / exp / pow, no
    /// UnityEngine.Random, System.Random or string.GetHashCode; every smoothing pass is Jacobi (double-buffered).</para>
    /// </summary>
    public static class BathyGen
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ the determinism kit (spec 4.1)
        public struct SplitMix64
        {
            ulong s;

            public SplitMix64(ulong seed) { s = seed; }

            public ulong Next()
            {
                ulong z = s += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }

            public uint NextU32() => (uint)(Next() >> 32);
            public float Next01() => (Next() >> 40) * (1f / 16777216f);
            public float Range(float a, float b) => a + (b - a) * Next01();
            public float Range(Vector2 r) => Range(r.x, r.y);
        }

        public static uint Fnv1a(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        public static uint Mix(uint a, uint b)
        {
            uint h = a ^ (b * 0x9E3779B9u);
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }

        static SplitMix64 Stream(uint attemptSeed, string name)
        {
            uint m = Mix(attemptSeed, Fnv1a(name));
            return new SplitMix64(((ulong)m << 32) | (m ^ 0xA5A5A5A5u));
        }

        static float Corner(int i, int j, uint salt) => ((Mix(Mix((uint)i, (uint)j), salt) >> 8) * (2f / 16777216f)) - 1f;
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Value noise in [-1, 1] on the integer lattice (quintic fade, bilinear blend).</summary>
        public static float Noise(float x, float z, uint salt)
        {
            int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(z);
            float u = Fade(x - i), v = Fade(z - j);
            float a = Corner(i, j, salt), b = Corner(i + 1, j, salt), c = Corner(i, j + 1, salt), d = Corner(i + 1, j + 1, salt);
            float ab = a + (b - a) * u, cd = c + (d - c) * u;
            return ab + (cd - ab) * v;
        }

        /// <summary>Polynomial smoothstep t^2 (3 - 2t), t = clamp01((x - e0) / (e1 - e0)) (also with e0 &gt; e1).</summary>
        public static float S(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float l2 = ab.x * ab.x + ab.y * ab.y;
            float t = l2 > 1e-10f ? Mathf.Clamp01(((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / l2) : 0f;
            float dx = p.x - (a.x + ab.x * t), dz = p.y - (a.y + ab.y * t);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Even-odd point in a (possibly concave) polygon.</summary>
        static bool InPolyEO(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Signed distance to a polygon's boundary (negative inside).</summary>
        static float SignedDist(Vector2[] poly, Vector2 p)
        {
            float d = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) d = Mathf.Min(d, SegDist(p, poly[j], poly[i]));
            return InPolyEO(poly, p) ? -d : d;
        }

        /// <summary>Distance from a point to a convex footprint (0 inside).</summary>
        static float PolyDist(Vector2[] poly, Vector2 p) => Obstacles.InPoly(poly, p) ? 0f : Obstacles.EdgeDist(poly, p, out _, out _);

        static float RectDist(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(r.xMin - p.x, p.x - r.xMax));
            float dz = Mathf.Max(0f, Mathf.Max(r.yMin - p.y, p.y - r.yMax));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ the camera's visible half width, trig-free per call
        /// <summary>The camera's visible half width at z for a view this wide (Persp.VisibleHalfWidth, its pitch constants rounded to 1e-6).</summary>
        sealed class View
        {
            readonly float cosP, sinP, camBack, camY, f;

            public View(StageLayout L)
            {
                float a = L.pitch * Mathf.Deg2Rad;
                cosP = Mathf.Round(Mathf.Cos(a) * 1e6f) / 1e6f;
                sinP = Mathf.Round(Mathf.Sin(a) * 1e6f) / 1e6f;
                camBack = L.camBack;
                camY = L.standH + L.camUp;
                f = L.focalPx > 1f ? L.focalPx : 520f;
            }

            public float Half(float z, float widthPx)
            {
                float d = Mathf.Max(0.05f, (z + camBack) * cosP + camY * sinP);
                return widthPx * 0.5f * d / f;
            }
        }

        // ------------------------------------------------------------------ the pins (spec 5.1)
        sealed class PinRt
        {
            public Pin p;
            public string name;
            public readonly List<Vector2[]> polys = new List<Vector2[]>();
            public readonly List<float> los = new List<float>();
            /// <summary>Each footprint's bounding circle (the broad phase).</summary>
            public readonly List<Vector3> circles = new List<Vector3>();
            public bool reed;

            public void Add(Vector2[] poly, float lo)
            {
                polys.Add(poly);
                los.Add(lo);
                circles.Add(Circle(poly));
            }
        }

        /// <summary>A polygon's bounding circle round its vertices' mean: (x, z, r).</summary>
        static Vector3 Circle(Vector2[] poly)
        {
            var m = Vector2.zero;
            foreach (var p in poly) m += p;
            m /= poly.Length;
            float r = 0f;
            foreach (var p in poly) r = Mathf.Max(r, (p - m).magnitude);
            return new Vector3(m.x, m.y, r);
        }

        struct Ring
        {
            public float lo, hi, t, blend;
            public bool profile;
        }

        // ------------------------------------------------------------------ the static part of a stage (every attempt shares it)
        sealed class Ctx
        {
            public StageLayout L;
            public TerrainRecipe r;
            public View view;
            public int nx, nz, n;
            public float x0, z0, x1, z1;
            /// <summary>The authored profile (the pier's window and ring, V3); the side factor's reach 0.9 x half(z, 640) per row; the shore's depth.</summary>
            public float[] authD, laneSd, sideH;
            public float shoreD;
            /// <summary>Per row: the median window (node columns) of the derived profile, |x| within min(half(z, 600), X1 - feather).</summary>
            public int[] rowI0, rowI1;
            /// <summary>R_ref: z in [zNear + 1.5, 50], |x| within half(z, 600); 1 / (2 hw) a node.</summary>
            public int[] refNodes;
            public float[] refU;
            public bool[] inLane, fixedNode, spotBlocked;
            public float[] coreLo, coreHi;   // NaN: not a core node
            public int[] ringStart, ringCount;
            public Ring[] rings;
            public float[] reedT;            // m to a reed pin's core (0 inside, 99 beyond its ring)
            public List<PinRt> pins = new List<PinRt>();
            public List<int> pierCore = new List<int>();
            public List<int> laneCore = new List<int>();
            /// <summary>V6: per band zone (down to the bed) and bed solid, the least water it needs and its footprint's nodes.</summary>
            public List<(float need, int[] nodes)> bandNodes = new List<(float, int[])>();
            // blobs' static distances: per blob, the nodes of its box and their distance to its segment
            public int[][] blobNodes;
            public float[][] blobSeg;

            public float[] nodeX, nodeZ;
            public float X(int k) => nodeX[k];
            public float Z(int k) => nodeZ[k];
        }

        static Ctx Prepare(StageLayout L, TerrainRecipe r, ObstacleSet raw)
        {
            var c = new Ctx { L = L, r = r, view = new View(L), nx = r.nx, nz = r.nz, x0 = r.x0, z0 = r.z0 };
            c.n = c.nx * c.nz;
            c.x1 = c.x0 + (c.nx - 1) * Bathymetry.Cell;
            c.z1 = c.z0 + (c.nz - 1) * Bathymetry.Cell;
            int n = c.n;
            c.nodeX = new float[n];
            c.nodeZ = new float[n];
            for (int k = 0; k < n; k++)
            {
                c.nodeX[k] = c.x0 + (k % c.nx) * Bathymetry.Cell;
                c.nodeZ[k] = c.z0 + (k / c.nx) * Bathymetry.Cell;
            }
            c.authD = new float[n];
            c.laneSd = new float[n];
            c.inLane = new bool[n];
            c.fixedNode = new bool[n];
            c.spotBlocked = new bool[n];
            c.coreLo = new float[n];
            c.coreHi = new float[n];
            c.reedT = new float[n];
            c.shoreD = L.AuthoredMeanDepth(c.z0);
            c.sideH = new float[c.nz];
            c.rowI0 = new int[c.nz];
            c.rowI1 = new int[c.nz];
            var refN = new List<int>();
            var refW = new List<float>();
            float zRef0 = L.zNear + 1.5f, zRef1 = 50f;
            for (int j = 0; j < c.nz; j++)
            {
                float z = c.z0 + j * Bathymetry.Cell;
                c.sideH[j] = 0.9f * c.view.Half(z, 640f);
                float hw = Mathf.Min(c.view.Half(z, 600f), c.x1 - r.feather);
                c.rowI0[j] = Mathf.Max(0, Mathf.CeilToInt((-hw - c.x0) / Bathymetry.Cell - 1e-4f));
                c.rowI1[j] = Mathf.Min(c.nx - 1, Mathf.FloorToInt((hw - c.x0) / Bathymetry.Cell + 1e-4f));
                if (z < zRef0 - 1e-4f || z > zRef1 + 1e-4f) continue;
                float h6 = c.view.Half(z, 600f);
                float u = 1f / (2f * h6);
                for (int i = 0; i < c.nx; i++)
                {
                    float x = c.x0 + i * Bathymetry.Cell;
                    if (Mathf.Abs(x) > h6) continue;
                    refN.Add(j * c.nx + i);
                    refW.Add(u);
                }
            }
            c.refNodes = refN.ToArray();
            c.refU = refW.ToArray();
            for (int k = 0; k < n; k++)
            {
                float x = c.X(k), z = c.Z(k);
                c.authD[k] = L.AuthoredMeanDepth(z);
                c.coreLo[k] = c.coreHi[k] = float.NaN;
                c.reedT[k] = 99f;
                var p = new Vector2(x, z);
                c.laneSd[k] = r.lane != null && r.lane.Length >= 3 ? SignedDist(r.lane, p) : 99f;
                c.inLane[k] = c.laneSd[k] < 0f;
                int i = k % c.nx, j = k / c.nx;
                // (the grid's far edges stay the profile: the feather ends there, nothing moves them)
                c.fixedNode[k] = i == 0 || i == c.nx - 1 || j == c.nz - 1;
            }

            // ---- the pins' footprints from the raw obstacle data (the same with -fkobstacles off)
            var obs = raw?.obstacles ?? new Obstacle[0];
            var byId = new Dictionary<string, Obstacle>();
            foreach (var o in obs) if (o != null && o.id != null && !byId.ContainsKey(o.id)) byId[o.id] = o;
            var matched = new HashSet<Obstacle>();
            bool Sel(Pin p, Obstacle o)
            {
                if (o == null || string.IsNullOrEmpty(o.kind)) return false;
                switch (p.sel)
                {
                    case PinSel.Id: return o.id == p.id;
                    case PinSel.Kind: return o.kind == p.kind;
                    case PinSel.KindTag: return o.kind == p.kind && o.Has(p.tag);
                    case PinSel.StemOfPad: return o.kind == "weed" && o.parent != null && byId.TryGetValue(o.parent, out var par) && par.kind == "pad";
                    default: return false;
                }
            }
            var sorted = new List<Pin>(r.pins ?? new Pin[0]);
            sorted.Sort((a, b) => a.prio.CompareTo(b.prio));
            foreach (var p in sorted)
            {
                var rt = new PinRt { p = p, name = p.id ?? p.kind ?? p.sel.ToString() };
                if (p.sel == PinSel.Rect || p.sel == PinSel.Lane)
                {
                    c.pins.Add(rt);
                    continue;
                }
                if (p.sel == PinSel.GenericBand) continue;   // (after every other pin has claimed its obstacles)
                foreach (var o in obs)
                {
                    if (!Sel(p, o)) continue;
                    var poly = Obstacles.Footprint(o);
                    if (poly == null || poly.Length < 3) continue;
                    rt.Add(poly, p.lo);
                    matched.Add(o);
                    if (p.sel == PinSel.KindTag && p.tag == "reed") rt.reed = true;
                }
                c.pins.Add(rt);
            }
            foreach (var p in sorted)
            {
                if (p.sel != PinSel.GenericBand) continue;
                var rt = new PinRt { p = p, name = "generic" };
                foreach (var o in obs)
                {
                    if (o == null || matched.Contains(o) || (o.kind != "snag" && o.kind != "weed") || o.bot > -98f || o.top >= -0.5f) continue;
                    var poly = Obstacles.Footprint(o);
                    if (poly == null || poly.Length < 3) continue;
                    rt.Add(poly, -o.top + p.lo);
                }
                c.pins.Add(rt);
            }
            // (no inverted band: every zone down to the bed; bed solids standing on it)
            foreach (var o in obs)
            {
                if (o == null) continue;
                bool band = (o.kind == "snag" || o.kind == "weed" || o.kind == "cover") && o.bot <= -98f, bedSolid = o.kind == "solid" && o.bed;
                if (!band && !bedSolid) continue;
                var poly = Obstacles.Footprint(o);
                if (poly == null || poly.Length < 3) continue;
                var bc = Circle(poly);
                var nodes = new List<int>();
                int i0 = Mathf.Max(0, Mathf.FloorToInt((bc.x - bc.z - c.x0) / Bathymetry.Cell)), i1 = Mathf.Min(c.nx - 1, Mathf.CeilToInt((bc.x + bc.z - c.x0) / Bathymetry.Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((bc.y - bc.z - c.z0) / Bathymetry.Cell)), j1 = Mathf.Min(c.nz - 1, Mathf.CeilToInt((bc.y + bc.z - c.z0) / Bathymetry.Cell));
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int k = j * c.nx + i;
                    if (Obstacles.InPoly(poly, new Vector2(c.X(k), c.Z(k)))) nodes.Add(k);
                }
                c.bandNodes.Add((band ? -o.top + 0.15f : 0.3f, nodes.ToArray()));
            }

            // ---- per node: the core window (the intersection; empty: the highest priority's) and the rings (ascending priority)
            var ringLists = new List<Ring>[n];
            var coreWins = new List<(float lo, float hi, int prio)>();
            for (int k = 0; k < n; k++)
            {
                float x = c.X(k), z = c.Z(k);
                var p = new Vector2(x, z);
                coreWins.Clear();
                foreach (var rt in c.pins)
                {
                    var pin = rt.p;
                    float d;     // distance outside the core (<= 0: in it)
                    float lo = pin.lo, hi = pin.hi;
                    if (pin.profile) lo = hi = c.authD[k];
                    if (pin.sel == PinSel.Rect) d = RectDist(pin.rect, p);
                    else if (pin.sel == PinSel.Lane) d = Mathf.Max(0f, c.laneSd[k]);
                    else
                    {
                        d = float.MaxValue;
                        int best = -1;
                        for (int q = 0; q < rt.polys.Count; q++)
                        {
                            var bc = rt.circles[q];
                            float far = new Vector2(p.x - bc.x, p.y - bc.y).magnitude - bc.z - pin.grow;
                            if (far >= pin.blend || far >= d) continue;
                            float dq = PolyDist(rt.polys[q], p) - pin.grow;
                            if (dq < d)
                            {
                                d = dq;
                                best = q;
                            }
                        }
                        if (best < 0) continue;
                        lo = rt.los[best];
                        if (d < 0f) d = 0f;
                    }
                    bool core = pin.sel == PinSel.Lane ? c.inLane[k] : d <= 0f;
                    if (core)
                    {
                        coreWins.Add((lo, hi, pin.prio));
                        if (rt.reed) c.reedT[k] = 0f;
                        if (pin.sel == PinSel.Rect) c.pierCore.Add(k);
                        if (pin.sel == PinSel.Lane) c.laneCore.Add(k);
                    }
                    else if (d < pin.blend)
                    {
                        (ringLists[k] ??= new List<Ring>()).Add(new Ring { lo = lo, hi = hi, t = d, blend = pin.blend, profile = pin.profile });
                        if (rt.reed) c.reedT[k] = Mathf.Min(c.reedT[k], d);
                    }
                }
                if (coreWins.Count > 0)
                {
                    float lo = float.MinValue, hi = float.MaxValue;
                    int top = 0;
                    for (int q = 0; q < coreWins.Count; q++)
                    {
                        lo = Mathf.Max(lo, coreWins[q].lo);
                        hi = Mathf.Min(hi, coreWins[q].hi);
                        if (coreWins[q].prio >= coreWins[top].prio) top = q;
                    }
                    if (lo > hi)
                    {
                        lo = coreWins[top].lo;
                        hi = coreWins[top].hi;
                    }
                    c.coreLo[k] = lo;
                    c.coreHi[k] = hi;
                    c.fixedNode[k] = true;
                }
            }
            c.ringStart = new int[n];
            c.ringCount = new int[n];
            var all = new List<Ring>();
            for (int k = 0; k < n; k++)
            {
                c.ringStart[k] = all.Count;
                if (ringLists[k] == null || !float.IsNaN(c.coreLo[k])) continue;
                all.AddRange(ringLists[k]);
                c.ringCount[k] = ringLists[k].Count;
            }
            c.rings = all.ToArray();

            // ---- the spot's clearances (spec 5.4, V8): 0.9 m from a pad, 1.0 m from a solid
            var pads = new List<(Vector2[] poly, Vector3 c)>();
            var solids = new List<(Vector2[] poly, Vector3 c)>();
            foreach (var o in obs)
            {
                if (o == null) continue;
                var fp = o.kind == "pad" || (o.kind == "solid" && !o.Has("near")) ? Obstacles.Footprint(o) : null;
                if (fp == null || fp.Length < 3) continue;
                if (o.kind == "pad") pads.Add((fp, Circle(fp)));
                else solids.Add((fp, Circle(fp)));
            }
            for (int k = 0; k < n; k++)
            {
                var p = new Vector2(c.X(k), c.Z(k));
                if (p.y > 45f) continue;
                foreach (var q in pads)
                    if (new Vector2(p.x - q.c.x, p.y - q.c.y).magnitude - q.c.z < 0.9f && PolyDist(q.poly, p) < 0.9f) c.spotBlocked[k] = true;
                foreach (var q in solids)
                    if (new Vector2(p.x - q.c.x, p.y - q.c.y).magnitude - q.c.z < 1.0f && PolyDist(q.poly, p) < 1.0f) c.spotBlocked[k] = true;
            }

            // ---- the blobs' segment distances over their boxes (scale <= 1.12 x the weed's largest radius factor, wobble <= 0.8)
            int nb = r.blobs?.Length ?? 0;
            c.blobNodes = new int[nb][];
            c.blobSeg = new float[nb][];
            float rMax = 1.12f * Mathf.Max(1f, Mathf.Max(r.character.flatRadius.x, r.character.flatRadius.y));
            for (int b = 0; b < nb; b++)
            {
                var bl = r.blobs[b];
                float reach = bl.r * rMax + 0.85f;
                float bx0 = Mathf.Min(bl.a.x, bl.b.x) - reach, bx1 = Mathf.Max(bl.a.x, bl.b.x) + reach;
                float bz0 = Mathf.Min(bl.a.y, bl.b.y) - reach, bz1 = Mathf.Max(bl.a.y, bl.b.y) + reach;
                int i0 = Mathf.Max(0, Mathf.FloorToInt((bx0 - c.x0) / Bathymetry.Cell)), i1 = Mathf.Min(c.nx - 1, Mathf.CeilToInt((bx1 - c.x0) / Bathymetry.Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((bz0 - c.z0) / Bathymetry.Cell)), j1 = Mathf.Min(c.nz - 1, Mathf.CeilToInt((bz1 - c.z0) / Bathymetry.Cell));
                var nodes = new List<int>();
                var seg = new List<float>();
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int k = j * c.nx + i;
                    float d = SegDist(new Vector2(c.X(k), c.Z(k)), bl.a, bl.b);
                    if (d > reach) continue;
                    nodes.Add(k);
                    seg.Add(d);
                }
                c.blobNodes[b] = nodes.ToArray();
                c.blobSeg[b] = seg.ToArray();
            }
            return c;
        }

        static readonly Dictionary<string, Ctx> ctxCache = new Dictionary<string, Ctx>();

        // ------------------------------------------------------------------ one attempt
        sealed class Work
        {
            public float[] D, D2, wFlat, shoalW, chanC, humpC, humpRho, holeC;
            public sbyte[] flatId, humpIdx, holeIdx;
            /// <summary>This attempt's flats: the recipe's blobs, then the extra weed flats (<see cref="nFl"/> in all).</summary>
            public Blob[] fl;
            public int nFl;
            public float[] blobScale, blobWob, blobE, blobT;
            public uint[] blobSalt;
            public float shelfBase;
            public uint shelfSalt, flatNoiseSalt, matSalt;
            public bool fallback;
            public int holes, humps;
            public readonly List<(Vector2 c, float R)> features = new List<(Vector2, float)>();
            public string featLog = "";
            /// <summary>The character (step 0a), the derived profile (step 5b, whole cm a row) and the materials' thresholds.</summary>
            public BedCharacter ch;
            public float[] pStar, envLo, envHi;
            public float slopeGravel, channelGravel, openGravel, flatSand, shelfWeed;

            public float ZShelf(float x) => fallback ? shelfBase : shelfBase + 1.5f * Noise(x / 9f, 0f, shelfSalt);
        }

        static float FlatSdf(Ctx c, Work w, int b, float segDist, float x, float z, float laneSd)
        {
            var bl = w.fl[b];
            float sdf = segDist - bl.r * w.blobScale[b] + (w.blobWob[b] > 0f ? w.blobWob[b] * Noise(x / 6f, z / 6f, w.blobSalt[b]) : 0f);
            if (bl.gap > 0f) sdf = Mathf.Max(sdf, bl.gap - laneSd);
            return sdf;
        }

        /// <summary>The least flat sdf at a point (for the features' clearances).</summary>
        static float MinFlatSdf(Ctx c, Work w, Vector2 p)
        {
            float lane = c.r.lane != null && c.r.lane.Length >= 3 ? SignedDist(c.r.lane, p) : 99f;
            float m = 99f;
            for (int b = 0; b < w.nFl; b++)
            {
                var bl = w.fl[b];
                m = Mathf.Min(m, FlatSdf(c, w, b, SegDist(p, bl.a, bl.b), p.x, p.y, lane));
            }
            return m;
        }

        /// <summary>The least distance from a point to a pin's footprints (the lane left out; 0 inside).</summary>
        static float PinDist(Ctx c, Vector2 p)
        {
            float m = 99f;
            foreach (var rt in c.pins)
            {
                if (rt.p.sel == PinSel.Lane) continue;
                if (rt.p.sel == PinSel.Rect) m = Mathf.Min(m, RectDist(rt.p.rect, p));
                foreach (var q in rt.polys) m = Mathf.Min(m, PolyDist(q, p));
            }
            return m;
        }

        static float Bilinear(Ctx c, float[] D, float x, float z)
        {
            float fx = Mathf.Clamp((x - c.x0) / Bathymetry.Cell, 0f, c.nx - 1), fz = Mathf.Clamp((z - c.z0) / Bathymetry.Cell, 0f, c.nz - 1);
            int i = Mathf.Min((int)fx, c.nx - 2), j = Mathf.Min((int)fz, c.nz - 2);
            float tx = fx - i, tz = fz - j;
            int k = j * c.nx + i;
            float a = D[k] + (D[k + 1] - D[k]) * tx, b = D[k + c.nx] + (D[k + c.nx + 1] - D[k + c.nx]) * tx;
            return a + (b - a) * tz;
        }

        static Vector2 Axis(ref SplitMix64 rng)
        {
            for (int t = 0; t < 4; t++)
            {
                float ax = rng.Range(-1f, 1f), az = rng.Range(-1f, 1f);
                float l = Mathf.Sqrt(ax * ax + az * az);
                if (l >= 0.2f) return new Vector2(ax / l, az / l);
            }
            return new Vector2(1f, 0f);
        }

        static float Mid(Vector2 r) => 0.5f * (r.x + r.y);

        /// <summary>
        /// Step 0a (Docs/lake_phase2_spec.md A1; stream "character", a new one every attempt, so a retry draws a new lake):
        /// the four u (depth, weed, rock, side), then the shelf's end, the drop's width, the side axis, the far rise, the
        /// noise's scale and the lane's carve. The fallback takes the middle of every range (every u 0.5).
        /// </summary>
        static void DrawCharacter(Ctx c, Work w, uint attemptSeed)
        {
            var cs = c.r.character;
            var st = Stream(attemptSeed, "character");
            float ud = st.Next01(), uw = st.Next01(), ur = st.Next01(), us = st.Next01();
            float zs = st.Range(cs.shelfEnd), ws = st.Range(cs.dropWidth), xc = st.Range(cs.sideAxis), phi = st.Next01() * cs.farRiseMax;
            float noise = st.Range(cs.noiseAmp), lane = st.Range(cs.laneScale);
            if (w.fallback)
            {
                ud = uw = ur = us = 0.5f;
                zs = Mid(cs.shelfEnd);
                ws = Mid(cs.dropWidth);
                xc = Mid(cs.sideAxis);
                phi = 0.5f * cs.farRiseMax;
                noise = Mid(cs.noiseAmp);
                lane = Mid(cs.laneScale);
            }
            float dm = CharacterSpec.Lerp(cs.mainDepth, ud);
            w.ch = new BedCharacter
            {
                uDepth = ud, uWeed = uw, uRock = ur, uSide = us,
                mainDepth = dm, shelfEnd = zs, dropWidth = ws, side = CharacterSpec.Lerp(cs.side, us), sideX = xc, farRise = phi, noise = noise,
                laneT = Mathf.Max(cs.laneMin, dm * lane), label = BedCharacter.Label(ud, uw, ur),
            };
            w.slopeGravel = CharacterSpec.Lerp(cs.slopeGravel, ur);
            w.channelGravel = CharacterSpec.Lerp(cs.channelGravel, ur);
            w.openGravel = CharacterSpec.Lerp(cs.openGravel, ur);
            w.flatSand = CharacterSpec.Lerp(cs.flatSand, uw);
            w.shelfWeed = CharacterSpec.Lerp(cs.shelfWeed, uw);
        }

        static void Run(Ctx c, Work w, uint attemptSeed)
        {
            var r = c.r;
            var cs = r.character;
            int n = c.n, nb = r.blobs?.Length ?? 0;
            bool laneOk = r.lane != null && r.lane.Length >= 3;
            float[] D = w.D;
            Array.Clear(w.wFlat, 0, n);
            Array.Clear(w.shoalW, 0, n);
            Array.Clear(w.chanC, 0, n);
            Array.Clear(w.humpC, 0, n);
            Array.Clear(w.holeC, 0, n);
            for (int k = 0; k < n; k++)
            {
                w.flatId[k] = w.humpIdx[k] = w.holeIdx[k] = -1;
                w.humpRho[k] = 9f;
            }
            w.features.Clear();
            w.holes = w.humps = 0;
            w.featLog = "";

            var prof = Profile ? System.Diagnostics.Stopwatch.StartNew() : null;
            void Lap(string what) { if (prof != null) w.featLog += string.Format(CI, " [{0} {1:0.0}]", what, prof.Elapsed.TotalMilliseconds); }
            // ---- step 0a: the character; step 0b: the base (the shore's 1.2 m at z 0, a shelf to zs, a drop over ws to the main
            // depth; the side and far factors take from the excess only, so the shore stays the shore)
            DrawCharacter(c, w, attemptSeed);
            var ch = w.ch;
            for (int k = 0; k < n; k++)
            {
                float x = c.X(k), z = c.Z(k);
                float side = 1f - ch.side * S(0f, c.sideH[k / c.nx], Mathf.Abs(x - ch.sideX));
                float far = 1f - ch.farRise * S(cs.farZ.x, cs.farZ.y, z);
                D[k] = c.shoreD + (ch.mainDepth - c.shoreD) * S(ch.shelfEnd, ch.shelfEnd + ch.dropWidth, z) * side * far;
            }
            w.featLog += string.Format(CI, " character d {0:0.00} w {1:0.00} r {2:0.00} s {3:0.00} Dm {4:0.00} zs {5:0.0} ws {6:0.0}", ch.uDepth, ch.uWeed, ch.uRock, ch.uSide, ch.mainDepth, ch.shelfEnd, ch.dropWidth);

            // ---- step 1: the flats (stream "flats": 4 values per blob, in table order), their radius and depth by the weed;
            // then the extra weed flats (stream "weedflats")
            var flats = Stream(attemptSeed, "flats");
            float rMul = CharacterSpec.Lerp(cs.flatRadius, ch.uWeed), bias = CharacterSpec.Lerp(cs.flatBias, ch.uWeed);
            for (int b = 0; b < nb; b++)
            {
                var bl = r.blobs[b];
                float scale = flats.Range(1.0f, 1.12f), wob = flats.Range(0.4f, 0.8f), e = flats.Range(bl.edge), t = flats.Range(bl.depth);
                if (w.fallback)
                {
                    scale = 1.06f;
                    wob = 0f;
                    e = (bl.edge.x + bl.edge.y) * 0.5f;
                    t = (bl.depth.x + bl.depth.y) * 0.5f;
                }
                w.fl[b] = bl;
                w.blobScale[b] = scale * rMul;
                w.blobWob[b] = wob;
                w.blobE[b] = e;
                w.blobT[b] = t + bias;
                w.blobSalt[b] = Mix(attemptSeed, Fnv1a(bl.id));
            }
            w.nFl = nb;
            int extras = w.fallback ? 0 : Mathf.Min(cs.extraMax, (int)(3.99f * ch.uWeed * ch.uWeed));
            var wf = Stream(attemptSeed, "weedflats");
            for (int e = 0; e < extras; e++)
            {
                float R = wf.Range(cs.extraR), t = wf.Range(cs.extraDepth), edge = wf.Range(cs.extraEdge), wob = wf.Range(0.4f, 0.8f);
                bool ok = false;
                Vector2 cen = default;
                for (int tr = 0; tr < 40; tr++)
                {
                    float z = wf.Range(cs.extraZ);
                    float x = wf.Range(-1f, 1f) * Mathf.Max(0f, c.view.Half(z, 640f) - R);
                    cen = new Vector2(x, z);
                    if ((laneOk ? SignedDist(r.lane, cen) : 99f) < R + 3f || PinDist(c, cen) < R + 2f) continue;
                    bool clear = true;
                    for (int q = nb; q < w.nFl; q++) if ((w.fl[q].a - cen).magnitude < w.fl[q].r + R + 3f) clear = false;
                    if (!clear) continue;
                    ok = true;
                    break;
                }
                if (!ok) continue;
                int b = w.nFl++;
                int no = b - nb + 1;
                w.fl[b] = new Blob { id = "W" + no, flat = "weed" + no, a = cen, b = cen, r = R, depth = new Vector2(t, t), edge = new Vector2(edge, edge) };
                w.blobScale[b] = 1f;
                w.blobWob[b] = wob;
                w.blobE[b] = edge;
                w.blobT[b] = t;
                w.blobSalt[b] = Mix(attemptSeed, Fnv1a(w.fl[b].id));
                w.featLog += string.Format(CI, " weed{0} ({1:0.0},{2:0.0}) r {3:0.0} d {4:0.00}", no, cen.x, cen.y, R, t);
            }
            var shelf = Stream(attemptSeed, "shelf");
            w.shelfBase = w.fallback ? (r.shelfZ.x + r.shelfZ.y) * 0.5f : shelf.Range(r.shelfZ);
            w.shelfSalt = Mix(attemptSeed, Fnv1a("shelf"));
            w.flatNoiseSalt = Mix(attemptSeed, Fnv1a("flatnoise"));
            w.matSalt = Mix(attemptSeed, Fnv1a("mat"));
            // sum of w_b t_b and w_b, the largest w, per node (only the flats' boxes)
            var sumW = w.D2;   // (scratch)
            var sumWT = new float[n];
            Array.Clear(sumW, 0, n);
            var flatBest = new float[n];
            void Accum(int b, int k, float segDist)
            {
                float sdf = FlatSdf(c, w, b, segDist, c.X(k), c.Z(k), c.laneSd[k]);
                float wb = 1f - S(-w.blobE[b], 0f, sdf);
                if (wb <= 0f) return;
                sumW[k] += wb;
                sumWT[k] += wb * w.blobT[b];
                if (wb > w.wFlat[k]) w.wFlat[k] = wb;
                if (wb > flatBest[k])
                {
                    flatBest[k] = wb;
                    w.flatId[k] = wb >= 0.5f ? (sbyte)b : (sbyte)-1;
                }
                if (w.fl[b].shoal && wb > w.shoalW[k]) w.shoalW[k] = wb;
            }
            for (int b = 0; b < w.nFl; b++)
            {
                if (b < nb)
                {
                    var nodes = c.blobNodes[b];
                    var seg = c.blobSeg[b];
                    for (int q = 0; q < nodes.Length; q++) Accum(b, nodes[q], seg[q]);
                    continue;
                }
                var bl = w.fl[b];
                float reach = bl.r * w.blobScale[b] + w.blobWob[b] + 0.05f;
                int i0 = Mathf.Max(0, Mathf.FloorToInt((bl.a.x - reach - c.x0) / Bathymetry.Cell)), i1 = Mathf.Min(c.nx - 1, Mathf.CeilToInt((bl.a.x + reach - c.x0) / Bathymetry.Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((bl.a.y - reach - c.z0) / Bathymetry.Cell)), j1 = Mathf.Min(c.nz - 1, Mathf.CeilToInt((bl.a.y + reach - c.z0) / Bathymetry.Cell));
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int k = j * c.nx + i;
                    float d = SegDist(new Vector2(c.X(k), c.Z(k)), bl.a, bl.b);
                    if (d <= reach) Accum(b, k, d);
                }
            }
            for (int k = 0; k < n; k++)
            {
                if (sumW[k] <= 0f) continue;
                float t = sumWT[k] / sumW[k] + (w.fallback ? 0f : 0.12f * Noise(c.X(k) / 4f, c.Z(k) / 4f, w.flatNoiseSalt));
                D[k] = D[k] + (t - D[k]) * w.wFlat[k];
            }
            // ---- step 1c: the lane carved towards its target (deepened only)
            if (laneOk)
                for (int k = 0; k < n; k++)
                {
                    float wl = S(cs.laneEdge.x, cs.laneEdge.y, c.laneSd[k]);
                    if (wl > 0f) D[k] = Mathf.Max(D[k], D[k] + (ch.laneT - D[k]) * wl);
                }

            Lap("flats");
            if (!w.fallback)
            {
                // ---- step 2: the old creek (stream "channel")
                var chs = Stream(attemptSeed, "channel");
                var cspec = r.channel;
                if (cspec != null)
                {
                    var s0 = new Vector2(chs.Range(cspec.startX), chs.Range(cspec.startZ));
                    float len = chs.Range(cspec.len), A = chs.Range(cspec.amp);
                    int m = Mathf.CeilToInt(len / cspec.step);
                    var pts = new List<Vector2> { s0 };
                    for (int q = 1; q <= m; q++)
                    {
                        float zq = s0.y + cspec.step * q;
                        float lim = c.view.Half(zq, 640f) - 3f;
                        pts.Add(new Vector2(Mathf.Clamp(pts[q - 1].x + chs.Range(-A, A), -lim, lim), zq));
                    }
                    float hw = chs.Range(cspec.halfWidth), extra = chs.Range(cspec.extra);
                    // a uniform Catmull-Rom through them (the end points doubled), sampled every step / 16
                    var line = new List<Vector2>();
                    for (int q = 0; q < pts.Count - 1; q++)
                    {
                        var p0 = pts[Mathf.Max(0, q - 1)];
                        var p1 = pts[q];
                        var p2 = pts[q + 1];
                        var p3 = pts[Mathf.Min(pts.Count - 1, q + 2)];
                        for (int s = 0; s < 16; s++)
                        {
                            float t = s / 16f, t2 = t * t, t3 = t2 * t;
                            line.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                        }
                    }
                    line.Add(pts[pts.Count - 1]);
                    var dmin = new float[n];
                    for (int k = 0; k < n; k++) dmin[k] = 99f;
                    for (int q = 0; q < line.Count - 1; q++)
                    {
                        var a = line[q];
                        var b = line[q + 1];
                        int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - hw - c.x0) / Bathymetry.Cell));
                        int i1 = Mathf.Min(c.nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + hw - c.x0) / Bathymetry.Cell));
                        int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - hw - c.z0) / Bathymetry.Cell));
                        int j1 = Mathf.Min(c.nz - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + hw - c.z0) / Bathymetry.Cell));
                        for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            int k = j * c.nx + i;
                            float d = SegDist(new Vector2(c.X(k), c.Z(k)), a, b);
                            if (d < dmin[k]) dmin[k] = d;
                        }
                    }
                    for (int k = 0; k < n; k++)
                    {
                        if (dmin[k] >= hw) continue;
                        float cc = extra * S(hw, 0f, dmin[k]) * (1f - w.wFlat[k]);
                        D[k] += cc;
                        w.chanC[k] = cc / extra;
                    }
                    w.featLog += string.Format(CI, " channel ({0:0.0},{1:0.0}) len {2:0} amp {3:0.0} hw {4:0.0} extra {5:0.00}", s0.x, s0.y, len, A, hw, extra);
                }

                Lap("channel");
                // ---- step 3: holes (stream "holes"): the second one more likely on a deep lake
                var hs = Stream(attemptSeed, "holes");
                var hspec = r.holes;
                if (hspec != null)
                {
                    float p2 = CharacterSpec.Lerp(cs.hole2, ch.uDepth);
                    int count = hspec.countMin + (hspec.countMax > hspec.countMin && hs.Next01() < p2 ? 1 : 0);
                    for (int h = 0; h < count; h++)
                    {
                        float R = hs.Range(hspec.radius), asp = hs.Range(hspec.aspect);
                        var u = Axis(ref hs);
                        float extra = hs.Range(hspec.amount);
                        bool ok = false;
                        Vector2 cen = default;
                        for (int tr = 0; tr < hspec.tries; tr++)
                        {
                            float z = h == 0 ? hs.Range(hspec.z1) : hs.Range(hspec.zN);
                            float xr = h == 0 ? hspec.x1 : hspec.xNFrac * c.view.Half(z, 600f);
                            float x = hs.Range(-1f, 1f) * xr;
                            cen = new Vector2(x, z);
                            if (MinFlatSdf(c, w, cen) < R + 1.5f || PinDist(c, cen) < R + 2f) continue;
                            bool clear = true;
                            foreach (var f in w.features) if ((f.c - cen).magnitude < f.R + R + 3f) clear = false;
                            if (!clear) continue;
                            ok = true;
                            break;
                        }
                        if (!ok) continue;
                        Stamp(c, w, cen, R, asp, u, (k, rho) =>
                        {
                            float cc = extra * S(1f, 0.3f, rho) * (1f - w.wFlat[k]);
                            if (cc <= 0f) return;
                            D[k] += cc;
                            if (cc / extra > w.holeC[k])
                            {
                                w.holeC[k] = cc / extra;
                                w.holeIdx[k] = (sbyte)w.holes;
                            }
                        });
                        w.features.Add((cen, R));
                        w.holes++;
                        w.featLog += string.Format(CI, " hole{0} ({1:0.0},{2:0.0}) R {3:0.0} extra {4:0.00}", w.holes, cen.x, cen.y, R, extra);
                    }
                }

                Lap("holes");
                // ---- step 4: humps (stream "humps"): 1 + floor(3.99 u_rock), each top relative to the water where it stands
                var us = Stream(attemptSeed, "humps");
                var uspec = r.humps;
                if (uspec != null)
                {
                    us.Next01();   // (the old count's draw: the stream's later values stay where they were)
                    int count = Mathf.Min(uspec.countMax, uspec.countMin + (int)(3.99f * ch.uRock));
                    for (int h = 0; h < count; h++)
                    {
                        float R = us.Range(uspec.radius), asp = us.Range(uspec.aspect);
                        var u = Axis(ref us);
                        float frac = us.Range(cs.humpTop);
                        bool ok = false;
                        Vector2 cen = default;
                        float hgt = 0f;
                        for (int tr = 0; tr < uspec.tries; tr++)
                        {
                            float z = h == 0 ? us.Range(uspec.z1) : us.Range(uspec.zN);
                            float xr = Mathf.Max(0f, c.view.Half(z, 640f) - R);
                            float x = us.Range(-1f, 1f) * xr;
                            cen = new Vector2(x, z);
                            float lane = laneOk ? SignedDist(r.lane, cen) : 99f;
                            if (lane < R + 1f || MinFlatSdf(c, w, cen) < R + 1f || PinDist(c, cen) < R + 2f) continue;
                            bool clear = true;
                            foreach (var f in w.features) if ((f.c - cen).magnitude < f.R + R + 3f) clear = false;
                            if (!clear) continue;
                            float dloc = Bilinear(c, D, cen.x, cen.y);
                            float top = Mathf.Clamp(dloc * frac - cs.humpRock * ch.uRock, cs.humpClamp.x, cs.humpClamp.y);
                            hgt = dloc - top;
                            if (hgt < Mathf.Max(1f, 0.3f * dloc)) continue;
                            ok = true;
                            break;
                        }
                        if (!ok) continue;
                        hgt = Mathf.Min(hgt, 1.3f * 0.55f * R);
                        int idx = w.humps;
                        Stamp(c, w, cen, R, asp, u, (k, rho) =>
                        {
                            float s = S(1f, 0.45f, rho);
                            if (s <= 0f) return;
                            D[k] -= hgt * s;
                            if (s > w.humpC[k])
                            {
                                w.humpC[k] = s;
                                w.humpRho[k] = rho;
                                w.humpIdx[k] = (sbyte)idx;
                            }
                        });
                        w.features.Add((cen, R));
                        w.humps++;
                        w.featLog += string.Format(CI, " hump{0} ({1:0.0},{2:0.0}) R {3:0.0} h {4:0.00}", w.humps, cen.x, cen.y, R, hgt);
                    }
                }

                Lap("humps");
                // ---- step 5: noise (stream "noise": its two salts), x the character's scale
                var ns = Stream(attemptSeed, "noise");
                uint sa = ns.NextU32(), sb = ns.NextU32();
                for (int k = 0; k < n; k++)
                {
                    float x = c.X(k), z = c.Z(k);
                    D[k] += ch.noise * (r.noiseA * Noise(x / r.noiseScaleA, z / r.noiseScaleA, sa) + r.noiseB * Noise(x / r.noiseScaleB, z / r.noiseScaleB, sb)) * (1f - r.noiseFlatDamp * w.wFlat[k]);
                }
            }
            ch.holes = w.holes;
            ch.humps = w.humps;
            ch.weedFlats = w.nFl - nb;
            w.ch = ch;

            Lap("noise");
            // ---- step 5b: the provisional profile P0: per row the median over its window of the pre-feather bed with the
            // pins' first pass and the slope limit's envelope applied (on a copy: the pier, the lane, the pads, the reeds, the
            // log and the slopes they force are part of the lake's profile), smoothed along z ([1 4 6 4 1] / 16), within
            // [0.6, 8.8], whole cm; the final P* is the finished rows' medians (step 9b)
            var pinned = w.D2;   // (scratch: SlopeLimit's buffer, free until step 8)
            Array.Copy(D, pinned, n);
            ApplyPins(c, pinned, true);
            for (int k = 0; k < n; k++) pinned[k] = Mathf.Clamp(pinned[k], r.minDepth, r.maxDepth);
            Envelope(c, pinned, null, w.envLo ??= new float[n], w.envHi ??= new float[n]);
            RowMedians(c, pinned, w.pStar, true);
            // ---- step 6: feather to P0 at the far edges (not the shore's)
            for (int k = 0; k < n; k++)
            {
                float x = c.X(k), z = c.Z(k);
                float p = w.pStar[k / c.nx];
                float edge = Mathf.Min(x - c.x0, Mathf.Min(c.x1 - x, c.z1 - z));
                D[k] = p + (D[k] - p) * S(0f, r.feather, edge);
            }

            // ---- steps 7-9: pins, the slope limit, pins again, the global limits
            Lap("feather");
            // (pass B clamps the cores only: blending the rings again after the limit would steepen their edges past it)
            ApplyPins(c, D, true);
            for (int k = 0; k < n; k++) D[k] = Mathf.Clamp(D[k], r.minDepth, r.maxDepth);
            Lap("pins");
            SlopeLimit(c, w, MaxLimitPasses);
            Lap("limit");
            w.featLog += " limit " + LastLimitPasses + " passes";
            ApplyPins(c, D, false);
            for (int k = 0; k < n; k++) D[k] = Mathf.Clamp(D[k], r.minDepth, r.maxDepth);
            // ---- step 9b: P* = the final rows' medians (unsmoothed: the slope limit keeps them within 0.75 m a row); the
            // side and far edges re-set to it, the slope limit and the pins' cores again (only the edge band moves)
            RowMedians(c, D, w.pStar, false);
            SlopeLimit(c, w, MaxLimitPasses);
            w.featLog += " / " + LastLimitPasses + " passes";
            ApplyPins(c, D, false);
            for (int k = 0; k < n; k++) D[k] = Mathf.Clamp(D[k], r.minDepth, r.maxDepth);
            Lap("profile");
        }

        /// <summary>
        /// Per row the median of <paramref name="D"/> over the row's window (|x| within min(half(z, 600), X1 - feather)),
        /// optionally smoothed along z ([1 4 6 4 1] / 16, the ends repeated), within [0.6, 8.8], whole cm.
        /// </summary>
        static void RowMedians(Ctx c, float[] D, float[] into, bool smooth)
        {
            var med = new float[c.nz];
            var buf = new float[c.nx];
            for (int j = 0; j < c.nz; j++)
            {
                int i0 = c.rowI0[j], m = c.rowI1[j] - i0 + 1;
                for (int i = 0; i < m; i++) buf[i] = D[j * c.nx + i0 + i];
                Array.Sort(buf, 0, m);
                med[j] = (m & 1) == 1 ? buf[m / 2] : 0.5f * (buf[m / 2 - 1] + buf[m / 2]);
            }
            for (int j = 0; j < c.nz; j++)
            {
                float s = !smooth ? med[j]
                    : (med[Mathf.Max(0, j - 2)] + 4f * med[Mathf.Max(0, j - 1)] + 6f * med[j] + 4f * med[Mathf.Min(c.nz - 1, j + 1)] + med[Mathf.Min(c.nz - 1, j + 2)]) / 16f;
                into[j] = Mathf.RoundToInt(Mathf.Clamp(s, 0.6f, 8.8f) * 100f) * 0.01f;
            }
        }

        /// <summary>The slope limit's cap on passes (it stops as soon as every edge is within the limit).</summary>
        public const int MaxLimitPasses = 60;

        /// <summary>Calls <paramref name="f"/>(node, rho) for every node of an ellipse (R along u x asp, R across).</summary>
        static void Stamp(Ctx c, Work w, Vector2 cen, float R, float asp, Vector2 u, Action<int, float> f)
        {
            float reach = R * asp + 0.5f;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((cen.x - reach - c.x0) / Bathymetry.Cell)), i1 = Mathf.Min(c.nx - 1, Mathf.CeilToInt((cen.x + reach - c.x0) / Bathymetry.Cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((cen.y - reach - c.z0) / Bathymetry.Cell)), j1 = Mathf.Min(c.nz - 1, Mathf.CeilToInt((cen.y + reach - c.z0) / Bathymetry.Cell));
            var v = new Vector2(-u.y, u.x);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int k = j * c.nx + i;
                float dx = c.X(k) - cen.x, dz = c.Z(k) - cen.y;
                float a = (dx * u.x + dz * u.y) / (R * asp), b = (dx * v.x + dz * v.y) / R;
                float rho = Mathf.Sqrt(a * a + b * b);
                if (rho < 1f) f(k, rho);
            }
        }

        /// <summary>The pins (spec 5.1): ring nodes blended towards each window (ascending priority), core nodes clamped into theirs.</summary>
        static void ApplyPins(Ctx c, float[] D, bool rings)
        {
            for (int k = 0; k < c.n; k++)
            {
                if (!float.IsNaN(c.coreLo[k]))
                {
                    D[k] = Mathf.Clamp(D[k], c.coreLo[k], c.coreHi[k]);
                    continue;
                }
                if (!rings) continue;
                int s = c.ringStart[k], e = s + c.ringCount[k];
                for (int q = s; q < e; q++)
                {
                    var rg = c.rings[q];
                    float cl = Mathf.Clamp(D[k], rg.lo, rg.hi);
                    D[k] = cl + (D[k] - cl) * S(0f, rg.blend, rg.t);
                }
            }
        }

        /// <summary>The slope limit's Jacobi passes in the last build (for the log).</summary>
        public static int LastLimitPasses;
        /// <summary>With <see cref="Profile"/>: the last attempt's run / finish / validate times.</summary>
        public static string PhaseMs = "";
        /// <summary>Test switch (the harness): a line every 10 Jacobi passes.</summary>
        public static bool DebugLimit;
        /// <summary>Test switch (the harness): the attempt's phases timed into its log line.</summary>
        public static bool Profile;

        /// <summary>Two raster sweeps: the largest function under <paramref name="a"/> rising at most <paramref name="s"/> per 4-neighbour step (exact for the grid's L1 steps).</summary>
        static void MinPlus(float[] a, int nx, int nz, float s)
        {
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                if (i > 0 && a[k - 1] + s < a[k]) a[k] = a[k - 1] + s;
                if (j > 0 && a[k - nx] + s < a[k]) a[k] = a[k - nx] + s;
            }
            for (int j = nz - 1; j >= 0; j--)
            for (int i = nx - 1; i >= 0; i--)
            {
                int k = j * nx + i;
                if (i < nx - 1 && a[k + 1] + s < a[k]) a[k] = a[k + 1] + s;
                if (j < nz - 1 && a[k + nx] + s < a[k]) a[k] = a[k + nx] + s;
            }
        }

        /// <summary>Two raster sweeps: the smallest function over <paramref name="a"/> falling at most <paramref name="s"/> per 4-neighbour step.</summary>
        static void MaxPlus(float[] a, int nx, int nz, float s)
        {
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                if (i > 0 && a[k - 1] - s > a[k]) a[k] = a[k - 1] - s;
                if (j > 0 && a[k - nx] - s > a[k]) a[k] = a[k - nx] - s;
            }
            for (int j = nz - 1; j >= 0; j--)
            for (int i = nx - 1; i >= 0; i--)
            {
                int k = j * nx + i;
                if (i < nx - 1 && a[k + 1] - s > a[k]) a[k] = a[k + 1] - s;
                if (j < nz - 1 && a[k + nx] - s > a[k]) a[k] = a[k + nx] - s;
            }
        }

        /// <summary>
        /// The slope limit's step (1): every node's window (a pin core's, the side and far edges' <paramref name="edges"/>
        /// value per row (null: free), elsewhere the global depth limits) spread at the limit slope by min / max-plus sweeps,
        /// and <paramref name="a"/> clamped into it (the mean where the spread windows cross).
        /// </summary>
        static void Envelope(Ctx c, float[] a, float[] edges, float[] lo, float[] hi)
        {
            float step = c.r.maxSlope * Bathymetry.Cell - 0.002f;
            int nx = c.nx, nz = c.nz, n = c.n;
            for (int k = 0; k < n; k++)
            {
                int i = k % nx, j = k / nx;
                if (edges != null && (i == 0 || i == nx - 1 || j == nz - 1)) lo[k] = hi[k] = edges[j];
                else if (!float.IsNaN(c.coreLo[k]))
                {
                    lo[k] = Mathf.Max(c.r.minDepth, c.coreLo[k]);
                    hi[k] = Mathf.Min(c.r.maxDepth, c.coreHi[k]);
                }
                else
                {
                    lo[k] = c.r.minDepth;
                    hi[k] = c.r.maxDepth;
                }
            }
            MaxPlus(lo, nx, nz, step);
            MinPlus(hi, nx, nz, step);
            for (int k = 0; k < n; k++) a[k] = lo[k] > hi[k] ? 0.5f * (lo[k] + hi[k]) : Mathf.Clamp(a[k], lo[k], hi[k]);
        }

        /// <summary>
        /// The slope limit (spec 4.2 step 8): every 4-neighbour edge at most maxSlope x 0.5 m, the pins' cores inside their
        /// windows, the side and far edges on the derived profile P*. (1) The windows' own limits (every node's [lo, hi]: a
        /// core's window, the edges' P*, elsewhere the global depth limits) spread at the limit slope: the deep water beside a reed
        /// core held at 1.2 m comes up in a cone round it. (2) Relaxed Jacobi passes (double-buffered, only near an edge
        /// over the limit; each node half way into the window its neighbours allow, cores clamped into theirs) up to
        /// <paramref name="maxPasses"/>, which give the drop-offs their full slope. (3) What is left over is closed exactly:
        /// the mean of the largest limited function under it and the smallest over it, clamped into (1). Every step is
        /// order-independent in its result.
        /// </summary>
        static void SlopeLimit(Ctx c, Work w, int maxPasses)
        {
            float step = c.r.maxSlope * Bathymetry.Cell - 0.002f;
            var sw0 = Profile ? System.Diagnostics.Stopwatch.StartNew() : null;
            int nx = c.nx, nz = c.nz, n = c.n;
            float[] a = w.D, b = w.D2;
            // (1) the windows' envelopes
            var lo = new float[n];
            var hi = new float[n];
            Envelope(c, a, w.pStar, lo, hi);
            if (sw0 != null) w.featLog += string.Format(CI, " {{env {0:0.0}}}", sw0.Elapsed.TotalMilliseconds);
            // (2) Jacobi passes near the edges over the limit (only the nodes round an edge over it; after a pass only the
            // edges of the nodes that moved can have gone over)
            var stamp = new int[n];
            var act = new List<int>();
            var moved = new List<int>();
            int pass = 1;
            void Near(int k)
            {
                int i = k % nx, j = k / nx;
                for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || ii >= nx || jj < 0 || jj >= nz) continue;
                    int q = jj * nx + ii;
                    if (stamp[q] == pass) continue;
                    stamp[q] = pass;
                    act.Add(q);
                }
            }
            bool Over(int k, int m) => Mathf.Abs(a[m] - a[k]) > step + 0.0005f;
            void Edges(int k)
            {
                int i = k % nx, j = k / nx;
                if (i < nx - 1 && Over(k, k + 1)) { Near(k); Near(k + 1); }
                if (i > 0 && Over(k, k - 1)) { Near(k); Near(k - 1); }
                if (j < nz - 1 && Over(k, k + nx)) { Near(k); Near(k + nx); }
                if (j > 0 && Over(k, k - nx)) { Near(k); Near(k - nx); }
            }
            for (int k = 0; k < n; k++) Edges(k);
            int passes = 0;
            while (act.Count > 0 && passes < maxPasses)
            {
                passes++;
                foreach (int k in act)
                {
                    int i = k % nx, j = k / nx;
                    float d = a[k];
                    if (i == 0 || i == nx - 1 || j == nz - 1)
                    {
                        b[k] = d;
                        continue;
                    }
                    float wl = float.MinValue, wh = float.MaxValue;
                    if (i > 0) { wl = Mathf.Max(wl, a[k - 1] - step); wh = Mathf.Min(wh, a[k - 1] + step); }
                    if (i < nx - 1) { wl = Mathf.Max(wl, a[k + 1] - step); wh = Mathf.Min(wh, a[k + 1] + step); }
                    if (j > 0) { wl = Mathf.Max(wl, a[k - nx] - step); wh = Mathf.Min(wh, a[k - nx] + step); }
                    if (j < nz - 1) { wl = Mathf.Max(wl, a[k + nx] - step); wh = Mathf.Min(wh, a[k + nx] + step); }
                    float cl = wl > wh ? (wl + wh) * 0.5f : Mathf.Clamp(d, wl, wh);
                    float v = 0.5f * d + 0.5f * cl;
                    if (!float.IsNaN(c.coreLo[k])) v = Mathf.Clamp(v, c.coreLo[k], c.coreHi[k]);
                    b[k] = v;
                }
                moved.Clear();
                foreach (int k in act)
                {
                    if (b[k] != a[k]) moved.Add(k);
                    a[k] = b[k];
                }
                act.Clear();
                pass++;
                foreach (int k in moved) Edges(k);
                if (DebugLimit && (passes % 10 == 0 || act.Count == 0))
                {
                    int cnt = 0;
                    float ex = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        int i = k % nx, j = k / nx;
                        if (i < nx - 1) { float e = Mathf.Abs(a[k + 1] - a[k]) - step; if (e > 0.0005f) { cnt++; ex = Mathf.Max(ex, e); } }
                        if (j < nz - 1) { float e = Mathf.Abs(a[k + nx] - a[k]) - step; if (e > 0.0005f) { cnt++; ex = Mathf.Max(ex, e); } }
                    }
                    Debug.Log(string.Format(CI, "  limit pass {0}: {1} edges over, worst +{2:0.0000}, active {3}", passes, cnt, ex, act.Count));
                }
            }
            bool more = act.Count > 0;
            LastLimitPasses = passes;
            if (sw0 != null) w.featLog += string.Format(CI, " {{jacobi {0:0.0}}}", sw0.Elapsed.TotalMilliseconds);
            // (3) the rest closed exactly
            if (more)
            {
                var lw = (float[])a.Clone();
                var up = (float[])a.Clone();
                MinPlus(lw, nx, nz, step);
                MaxPlus(up, nx, nz, step);
                for (int k = 0; k < n; k++)
                {
                    float m = 0.5f * (lw[k] + up[k]);
                    a[k] = lo[k] > hi[k] ? 0.5f * (lo[k] + hi[k]) : Mathf.Clamp(m, lo[k], hi[k]);
                }
            }
        }

        // ------------------------------------------------------------------ the build
        /// <summary>The spec's rods' cast distances (V8).</summary>
        public static readonly float[] CastDists = { 16f, 20f, 24f, 27f, 34f, 36f };
        /// <summary>The legend spot's centre: its disc radius (0.6 x the golden carp's spotRadius) and depth (depthMin + 0.3).</summary>
        public const float SpotDisc = 0.9f, SpotDepth = 4.3f, LurkDepth = 4.5f, LurkNear = 6f;
        /// <summary>Where he stands on the lake (the middle of Angler.WalkRange: (-0.25 + 1.0) / 2).</summary>
        public static float AnchorX(string stageId) => stageId == "lake" ? 0.375f : 0f;

        public static Bathymetry Build(StageLayout L, TerrainRecipe r, int worldSeed, ObstacleSet raw)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string ck = L.id + "/" + r.version;
            if (!ctxCache.TryGetValue(ck, out var c) || c.L != L)
            {
                c = Prepare(L, r, raw);
                ctxCache[ck] = c;
            }
            uint stageSeed = Mix((uint)worldSeed, Fnv1a(L.id) ^ ((uint)r.version * 0x9E3779B9u));
            var w = NewWork(c);
            var checks = new System.Text.StringBuilder();
            Bathymetry best = null;
            for (int k = 0; k < r.attempts; k++)
            {
                uint seed = k == 0 ? stageSeed : Mix(stageSeed, (uint)k);
                w.fallback = false;
                double t0 = sw.Elapsed.TotalMilliseconds;
                Run(c, w, seed);
                double t1 = sw.Elapsed.TotalMilliseconds;
                var b = Finish(c, w, L, worldSeed, stageSeed, k, false);
                double t2 = sw.Elapsed.TotalMilliseconds;
                var fails = Validate(c, b, out string report);
                if (Profile) PhaseMs = string.Format(CI, "run {0:0.0} finish {1:0.0} validate {2:0.0}", t1 - t0, t2 - t1, sw.Elapsed.TotalMilliseconds - t2);
                checks.Append("attempt ").Append(k).Append(':').Append(w.featLog).Append('\n').Append(report);
                if (fails == 0)
                {
                    best = b;
                    break;
                }
            }
            if (best == null)
            {
                w.fallback = true;
                Run(c, w, stageSeed);
                best = Finish(c, w, L, worldSeed, stageSeed, r.attempts, true);
                Validate(c, best, out string report);
                checks.Append("fallback:\n").Append(report);
                Debug.LogWarning($"[BATHY] WARN fallback: {L.id} world seed {worldSeed} failed {r.attempts} attempts");
            }
            best.Checks = checks.ToString();
            best.BuildMs = (float)sw.Elapsed.TotalMilliseconds;
            if (Bathymetry.Log) foreach (var line in best.Checks.Split('\n')) if (line.Length > 0) Debug.Log("[BATHY] CHECK " + line);
            return best;
        }

        /// <summary>Test hook (-fkauto depth): the fallback recipe alone.</summary>
        public static Bathymetry BuildFallback(StageLayout L, TerrainRecipe r, int worldSeed, ObstacleSet raw, out int fails, out string report)
        {
            var c = Prepare(L, r, raw);
            var w = NewWork(c);
            w.fallback = true;
            uint stageSeed = Mix((uint)worldSeed, Fnv1a(L.id) ^ ((uint)r.version * 0x9E3779B9u));
            Run(c, w, stageSeed);
            var b = Finish(c, w, L, worldSeed, stageSeed, r.attempts, true);
            fails = Validate(c, b, out report);
            return b;
        }

        static Work NewWork(Ctx c)
        {
            int n = c.n, nb = (c.r.blobs?.Length ?? 0) + Mathf.Max(0, c.r.character.extraMax);
            return new Work
            {
                D = new float[n], D2 = new float[n], wFlat = new float[n], shoalW = new float[n], chanC = new float[n], humpC = new float[n],
                humpRho = new float[n], holeC = new float[n], flatId = new sbyte[n], humpIdx = new sbyte[n], holeIdx = new sbyte[n],
                fl = new Blob[nb], blobScale = new float[nb], blobWob = new float[nb], blobE = new float[nb], blobT = new float[nb], blobSalt = new uint[nb],
                pStar = new float[c.nz],
            };
        }

        // ------------------------------------------------------------------ steps 10-11: quantize, derive
        static Bathymetry Finish(Ctx c, Work w, StageLayout L, int worldSeed, uint stageSeed, int attempt, bool fallback)
        {
            int n = c.n, nx = c.nx, nz = c.nz;
            var b = new Bathymetry
            {
                StageId = L.id, WorldSeed = worldSeed, StageSeed = stageSeed, Attempt = attempt, Fallback = fallback,
                X0 = c.x0, Z0 = c.z0, X1 = c.x1, Z1 = c.z1, Nx = nx, Nz = nz, L = L,
                cm = new ushort[n], kind = new byte[n], mat = new byte[n], zone = new byte[n], flags = new byte[n],
                edgeCm = new ushort[n], weedEdgeCm = new ushort[n],
            };
            var D = new float[n];
            double sum = 0;
            float dmin = float.MaxValue, dmax = 0f;
            for (int k = 0; k < n; k++)
            {
                int v = Mathf.Clamp(Mathf.RoundToInt(w.D[k] * 100f), 0, 65535);
                b.cm[k] = (ushort)v;
                D[k] = v * 0.01f;
                sum += D[k];
                dmin = Mathf.Min(dmin, D[k]);
                dmax = Mathf.Max(dmax, D[k]);
            }
            b.DepthMin = dmin;
            b.DepthMax = dmax;
            b.DepthMean = (float)(sum / n);
            // the node slope (central differences; one-sided at the borders)
            var slope = new float[n];
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                float gx = i == 0 ? (D[k + 1] - D[k]) / Bathymetry.Cell : i == nx - 1 ? (D[k] - D[k - 1]) / Bathymetry.Cell : (D[k + 1] - D[k - 1]) / (2f * Bathymetry.Cell);
                float gz = j == 0 ? (D[k + nx] - D[k]) / Bathymetry.Cell : j == nz - 1 ? (D[k] - D[k - nx]) / Bathymetry.Cell : (D[k + nx] - D[k - nx]) / (2f * Bathymetry.Cell);
                slope[k] = Mathf.Sqrt(gx * gx + gz * gz);
            }
            // kinds
            for (int k = 0; k < n; k++)
            {
                float x = c.X(k), z = c.Z(k), d = D[k];
                BedKind kd;
                if (w.humpIdx[k] >= 0 && w.humpRho[k] < 1f && w.humpC[k] >= 0.3f) kd = BedKind.Hump;
                else if (w.holeC[k] >= 0.3f) kd = BedKind.Hole;
                else if (slope[k] >= 0.35f && d >= 1.0f && d <= 8.5f) kd = BedKind.Dropoff;
                else if (w.chanC[k] >= 0.3f) kd = BedKind.Channel;
                else if (w.shoalW[k] >= 0.5f) kd = BedKind.Shoal;
                else if (w.wFlat[k] >= 0.5f && z > w.ZShelf(x)) kd = BedKind.Flat;
                else if (z <= w.ZShelf(x) && (w.wFlat[k] >= 0.5f || d <= 2.6f)) kd = BedKind.Shelf;
                else if (d >= 5.5f) kd = BedKind.Basin;
                else kd = BedKind.Open;
                b.kind[k] = (byte)kd;
            }
            // materials (the thresholds by the character: weedy flats and shelves, rocky slopes, channels and open water)
            for (int k = 0; k < n; k++)
            {
                float x = c.X(k), z = c.Z(k), d = D[k];
                float n3 = Noise(x / 3f, z / 3f, w.matSalt);
                var kd = (BedKind)b.kind[k];
                BedMat m;
                if (kd == BedKind.Hump) m = w.humpRho[k] < 0.6f ? BedMat.Gravel : BedMat.Sand;
                else if (kd == BedKind.Hole) m = BedMat.Mud;
                else if (kd == BedKind.Channel) m = n3 > w.channelGravel ? BedMat.Gravel : BedMat.Sand;
                else if (slope[k] >= w.slopeGravel) m = BedMat.Gravel;
                else if (kd == BedKind.Flat || kd == BedKind.Shoal) m = kd == BedKind.Flat && n3 > w.flatSand ? BedMat.Sand : BedMat.Weed;
                else if (kd == BedKind.Shelf) m = c.reedT[k] <= w.shelfWeed ? BedMat.Weed : n3 < -0.35f ? BedMat.Mud : BedMat.Sand;
                else if (d >= 5.5f) m = BedMat.Mud;
                else if (n3 > w.openGravel) m = BedMat.Gravel;
                else m = n3 > 0.2f ? BedMat.Sand : BedMat.Mud;
                b.mat[k] = (byte)m;
            }
            // flags and distances
            var weedSrc = new bool[n];
            var edgeSrc = new bool[n];
            for (int k = 0; k < n; k++)
            {
                var kd = (BedKind)b.kind[k];
                weedSrc[k] = kd == BedKind.Flat || kd == BedKind.Shoal || b.mat[k] == (byte)BedMat.Weed;
                edgeSrc[k] = slope[k] >= 0.35f;
            }
            var weedDist = Chamfer(weedSrc, nx, nz);
            var weSrc = new bool[n];
            for (int k = 0; k < n; k++)
            {
                BedFlag f = BedFlag.None;
                if (edgeSrc[k]) f |= BedFlag.Edge;
                if ((BedKind)b.kind[k] == BedKind.Dropoff && D[k] >= 2.0f && weedDist[k] <= 2.5f)
                {
                    f |= BedFlag.WeedEdge;
                    weSrc[k] = true;
                }
                if (!float.IsNaN(c.coreLo[k])) f |= BedFlag.Pinned;
                if (c.inLane[k]) f |= BedFlag.Lane;
                b.flags[k] = (byte)f;
            }
            var edgeDist = Chamfer(edgeSrc, nx, nz);
            var weDist = Chamfer(weSrc, nx, nz);
            for (int k = 0; k < n; k++)
            {
                b.edgeCm[k] = (ushort)Mathf.Clamp(Mathf.RoundToInt(edgeDist[k] * 100f), 0, 65535);
                b.weedEdgeCm[k] = (ushort)Mathf.Clamp(Mathf.RoundToInt(weDist[k] * 100f), 0, 65535);
            }
            // zones (first match)
            var ids = new List<string>();
            var zoneOf = new Dictionary<string, int>();
            int ZoneIndex(string id)
            {
                if (zoneOf.TryGetValue(id, out int zi)) return zi;
                zoneOf[id] = ids.Count;
                ids.Add(id);
                return ids.Count - 1;
            }
            for (int k = 0; k < n; k++)
            {
                var kd = (BedKind)b.kind[k];
                string id;
                if (kd == BedKind.Hump) id = "hump" + (w.humpIdx[k] + 1);
                else if (kd == BedKind.Hole && w.holeIdx[k] >= 0) id = "hole" + (w.holeIdx[k] + 1);
                else if (kd == BedKind.Channel) id = "channel";
                else if (kd == BedKind.Shoal) id = "shoal";
                else if (w.flatId[k] >= 0 && w.wFlat[k] >= 0.5f && IsFlatZone(w.fl[w.flatId[k]].flat))
                    id = w.fl[w.flatId[k]].flat;
                else if (kd == BedKind.Shelf) id = "shelf";
                else if (c.inLane[k]) id = "lane";
                else if (D[k] >= 5.5f) id = "basin";
                else id = "open";
                b.zone[k] = (byte)ZoneIndex(id);
            }
            var lists = new List<int>[ids.Count];
            for (int q = 0; q < ids.Count; q++) lists[q] = new List<int>();
            for (int k = 0; k < n; k++) lists[b.zone[k]].Add(k);
            for (int q = 0; q < ids.Count; q++)
            {
                var nodes = lists[q];
                float sx = 0f, sz = 0f, mn = float.MaxValue, mx = 0f, bx0 = float.MaxValue, bz0 = float.MaxValue, bx1 = float.MinValue, bz1 = float.MinValue;
                var kinds = new int[9];
                foreach (int k in nodes)
                {
                    float x = c.X(k), z = c.Z(k);
                    sx += x;
                    sz += z;
                    mn = Mathf.Min(mn, D[k]);
                    mx = Mathf.Max(mx, D[k]);
                    bx0 = Mathf.Min(bx0, x);
                    bx1 = Mathf.Max(bx1, x);
                    bz0 = Mathf.Min(bz0, z);
                    bz1 = Mathf.Max(bz1, z);
                    kinds[b.kind[k]]++;
                }
                int top = 0;
                for (int t = 1; t < 9; t++) if (kinds[t] > kinds[top]) top = t;
                b.zones.Add(new BedZone
                {
                    id = ids[q], name = ZoneName(ids[q]), kind = (BedKind)top, c = new Vector2(sx / nodes.Count, sz / nodes.Count),
                    area = nodes.Count * Bathymetry.Cell * Bathymetry.Cell, minD = mn, maxD = mx, box = Rect.MinMaxRect(bx0, bz0, bx1, bz1), nodes = nodes.ToArray(),
                });
            }
            // ---- step 11b: the derived profile and the lake's statistics over R_ref (Docs/lake_phase2_spec.md A1)
            b.pStar = (float[])w.pStar.Clone();
            b.rowI0 = c.rowI0;
            b.rowI1 = c.rowI1;
            b.refNodes = c.refNodes;
            b.refU = c.refU;
            b.Character = w.ch;
            Stats(b);
            // the hash (FNV-1a over cm, mat, zone, kind, then P*'s rows in cm)
            uint h = 2166136261u;
            void Byte(byte v)
            {
                h ^= v;
                h *= 16777619u;
            }
            for (int k = 0; k < n; k++)
            {
                Byte((byte)(b.cm[k] & 0xff));
                Byte((byte)(b.cm[k] >> 8));
            }
            for (int k = 0; k < n; k++) Byte(b.mat[k]);
            for (int k = 0; k < n; k++) Byte(b.zone[k]);
            for (int k = 0; k < n; k++) Byte(b.kind[k]);
            for (int j = 0; j < nz; j++)
            {
                int pc = Mathf.RoundToInt(b.pStar[j] * 100f);
                Byte((byte)(pc & 0xff));
                Byte((byte)(pc >> 8));
            }
            b.Hash = h;
            return b;
        }

        /// <summary>
        /// R_ref's weighted depth distribution (every row weighing the same): the depth at each whole percentile, every
        /// node's mid-rank (F(&lt; d) + F(&lt;= d)) / 2 in per mille, the shares of the materials and kinds. Doubles, a fixed order.
        /// </summary>
        static void Stats(Bathymetry b)
        {
            int maxCm = 0;
            for (int k = 0; k < b.cm.Length; k++) if (b.cm[k] > maxCm) maxCm = b.cm[k];
            var hist = new double[maxCm + 1];
            var mat = new double[4];
            var kind = new double[9];
            double tot = 0;
            for (int q = 0; q < b.refNodes.Length; q++)
            {
                int k = b.refNodes[q];
                double u = b.refU[q];
                hist[b.cm[k]] += u;
                mat[b.mat[k]] += u;
                kind[b.kind[k]] += u;
                tot += u;
            }
            if (tot <= 0) tot = 1;
            var le = new double[maxCm + 1];
            double acc = 0;
            for (int c = 0; c <= maxCm; c++)
            {
                acc += hist[c];
                le[c] = acc / tot;
            }
            b.rankOfCm = new float[maxCm + 1];
            for (int c = 0; c <= maxCm; c++) b.rankOfCm[c] = (float)(le[c] - 0.5 * hist[c] / tot);
            b.quantCm = new ushort[101];
            int first = 0;
            while (first < maxCm && hist[first] <= 0) first++;
            int last = maxCm;
            while (last > 0 && hist[last] <= 0) last--;
            b.quantCm[0] = (ushort)first;
            int cc = first;
            for (int p = 1; p <= 100; p++)
            {
                double want = p / 100.0 - 1e-9;
                while (cc < last && le[cc] < want) cc++;
                b.quantCm[p] = (ushort)cc;
            }
            b.quantCm[100] = (ushort)last;
            b.rankPm = new ushort[b.cm.Length];
            for (int k = 0; k < b.cm.Length; k++) b.rankPm[k] = (ushort)Mathf.Clamp(Mathf.RoundToInt(1000f * b.rankOfCm[b.cm[k]]), 0, 1000);
            for (int m = 0; m < 4; m++) b.matShare[m] = (float)(mat[m] / tot);
            for (int k = 0; k < 9; k++) b.kindShare[k] = (float)(kind[k] / tot);
        }

        static bool IsFlatZone(string flat) => flat == "flatL" || flat == "flatR" || (flat != null && flat.StartsWith("weed"));

        static string ZoneName(string id)
        {
            if (id.StartsWith("hump")) return "수중 둔덕 " + id.Substring(4);
            if (id.StartsWith("hole")) return "깊은 웅덩이 " + id.Substring(4);
            if (id.StartsWith("weed")) return "수초 평지 " + id.Substring(4);
            return id switch
            {
                "shelf" => "얕은 턱",
                "flatL" => "왼쪽 수초 평지",
                "flatR" => "오른쪽 수초 평지",
                "shoal" => "수초 둔덕",
                "lane" => "잔교 앞 깊은 골",
                "channel" => "옛 물골",
                "basin" => "깊은 바닥",
                _ => "열린 바닥",
            };
        }

        /// <summary>Two-pass 8-neighbour chamfer distance (m) to the source nodes (0.5 / 0.7071 steps).</summary>
        static float[] Chamfer(bool[] src, int nx, int nz)
        {
            const float A = 0.5f, B = 0.70710678f;
            int n = nx * nz;
            var d = new float[n];
            for (int k = 0; k < n; k++) d[k] = src[k] ? 0f : 1e6f;
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                float v = d[k];
                if (i > 0) v = Mathf.Min(v, d[k - 1] + A);
                if (j > 0)
                {
                    v = Mathf.Min(v, d[k - nx] + A);
                    if (i > 0) v = Mathf.Min(v, d[k - nx - 1] + B);
                    if (i < nx - 1) v = Mathf.Min(v, d[k - nx + 1] + B);
                }
                d[k] = v;
            }
            for (int j = nz - 1; j >= 0; j--)
            for (int i = nx - 1; i >= 0; i--)
            {
                int k = j * nx + i;
                float v = d[k];
                if (i < nx - 1) v = Mathf.Min(v, d[k + 1] + A);
                if (j < nz - 1)
                {
                    v = Mathf.Min(v, d[k + nx] + A);
                    if (i < nx - 1) v = Mathf.Min(v, d[k + nx + 1] + B);
                    if (i > 0) v = Mathf.Min(v, d[k + nx - 1] + B);
                }
                d[k] = v;
            }
            return d;
        }

        // ------------------------------------------------------------------ step 12: validation (spec 5.4, Docs/lake_phase2_spec.md A1)
        /// <summary>The V10 bounds on R_ref's depths (m) and the V11 floors on its shares.</summary>
        public const float Q10Min = 0.6f, Q50Min = 1.8f, Q50Max = 5.5f, Q90Min = 3.4f, Q90Max = 8.6f, Spread9010 = 1.8f, Spread5010 = 0.6f, Spread9050 = 0.6f;
        public const float WeedMin = 0.05f, GravelMin = 0.02f, SandMin = 0.05f, MudMin = 0.05f, FlatShoalMin = 0.06f, DropoffMin = 0.03f, HoleChannelMin = 0.02f, HumpMin = 0.005f;

        /// <summary>
        /// V1-V11 (V9 logged): the failures, with a line per check in <paramref name="report"/>. The fallback (no channel,
        /// holes or humps) is not held to V11's Hole + Channel and Hump floors.
        /// </summary>
        static int Validate(Ctx c, Bathymetry b, out string report)
        {
            var sb = new System.Text.StringBuilder();
            int fails = 0;
            int n = c.n, nx = c.nx, nz = c.nz;
            float Dk(int k) => b.cm[k] * 0.01f;
            void Check(string id, bool ok, string what)
            {
                if (!ok) fails++;
                sb.Append(id).Append(ok ? " PASS " : " FAIL ").Append(what).Append('\n');
            }
            // V1 pins, V2 limits, V3 pier, V4 far edges
            int v1 = 0, v2 = 0, v3 = 0, v4 = 0, v7 = 0;
            string v1At = "", v4At = "";
            for (int k = 0; k < n; k++)
            {
                float d = Dk(k);
                if (!float.IsNaN(c.coreLo[k]) && (d < c.coreLo[k] - 0.0051f || d > c.coreHi[k] + 0.0051f))
                {
                    if (v1 == 0) v1At = string.Format(CI, " first ({0:0.0},{1:0.0}) {2:0.00} not in [{3:0.00},{4:0.00}]", c.X(k), c.Z(k), d, c.coreLo[k], c.coreHi[k]);
                    v1++;
                }
                if (d < c.r.minDepth - 1e-4f || d > c.r.maxDepth + 1e-4f) v2++;
                int i = k % nx, j = k / nx;
                if ((i == 0 || i == nx - 1 || j == nz - 1) && Mathf.Abs(d - b.pStar[j]) > 0.01f)
                {
                    if (v4 == 0) v4At = string.Format(CI, "; first ({0:0.0},{1:0.0}) {2:0.00} not {3:0.00}", c.X(k), c.Z(k), d, b.pStar[j]);
                    v4++;
                }
            }
            foreach (int k in c.pierCore) if (Mathf.Abs(Dk(k) - c.authD[k]) > 0.01f) v3++;
            foreach (int k in c.laneCore) if (Dk(k) < 4.5f - 0.005f) v7++;
            Check("V1", v1 == 0, $"pin cores in their windows ({v1} out{v1At})");
            Check("V2", v2 == 0, string.Format(CI, "every node in [{0:0.00}, {1:0.00}] ({2} out; min {3:0.00} max {4:0.00})", c.r.minDepth, c.r.maxDepth, v2, b.DepthMin, b.DepthMax));
            Check("V3", v3 == 0, $"pier core = the authored profile within 1 cm ({v3} off of {c.pierCore.Count})");
            Check("V4", v4 == 0, $"side and far edges = the derived profile P* within 1 cm ({v4} off{v4At})");
            // V5 slopes
            int v5 = 0;
            float worst = 0f;
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                bool coreK = !float.IsNaN(c.coreLo[k]);
                if (i < nx - 1)
                {
                    bool both = coreK && !float.IsNaN(c.coreLo[k + 1]);
                    int dc = Mathf.Abs(b.cm[k + 1] - b.cm[k]);
                    if (!both)
                    {
                        worst = Mathf.Max(worst, dc * 0.01f / Bathymetry.Cell);
                        if (dc > 76) v5++;   // (1.52 m/m over 0.5 m, in whole cm)
                    }
                }
                if (j < nz - 1)
                {
                    bool both = coreK && !float.IsNaN(c.coreLo[k + nx]);
                    int dc = Mathf.Abs(b.cm[k + nx] - b.cm[k]);
                    if (!both)
                    {
                        worst = Mathf.Max(worst, dc * 0.01f / Bathymetry.Cell);
                        if (dc > 76) v5++;
                    }
                }
            }
            Check("V5", v5 == 0, string.Format(CI, "slopes <= 1.52 m/m ({0} edges over; steepest {1:0.00})", v5, worst));
            // V6 bands
            int v6 = 0;
            foreach (var (need, nodes) in c.bandNodes)
                foreach (int k in nodes)
                    if (Dk(k) < need - 0.005f) v6++;
            Check("V6", v6 == 0, $"no inverted band, bed solids in >= 0.3 m ({v6} nodes off)");
            Check("V7", v7 == 0, $"lane core >= 4.5 ({v7} shallower of {c.laneCore.Count})");
            // V8 the legend's spot for every rod (over this grid's own water: off it its own P*)
            var v8 = new System.Text.StringBuilder();
            bool v8ok = true;
            foreach (float cd in CastDists)
            {
                int spots = LegendSpots(c, b, cd, 6);
                v8.Append(string.Format(CI, " {0:0}:{1}", cd, spots));
                if (spots < 6) v8ok = false;
            }
            Check("V8", v8ok, "legend spot centres per cast distance (>= 6):" + v8);
            // V9 (information): P* against the authored profile every 5 m; the final rows' medians against P*
            var v9 = new System.Text.StringBuilder();
            for (int j = 0; j < nz; j += 10) v9.Append(string.Format(CI, " {0:0}:{1:0.00}/{2:0.00}", c.z0 + j * Bathymetry.Cell, b.pStar[j], c.authD[j * nx]));
            float rowOff = 0f;
            for (int j = 0; j < nz; j++) rowOff = Mathf.Max(rowOff, Mathf.Abs(b.FinalRowMedian(j) - b.pStar[j]));
            sb.Append("V9 INFO P* / authored per 5 m:").Append(v9).Append(string.Format(CI, "; final row medians - P* at most {0:0.00} m\n", rowOff));
            // V10 R_ref's depths
            float q10 = b.Quantile(10f), q50 = b.Quantile(50f), q90 = b.Quantile(90f);
            Check("V10", q10 >= Q10Min - 1e-4f && q50 >= Q50Min - 1e-4f && q50 <= Q50Max + 1e-4f && q90 >= Q90Min - 1e-4f && q90 <= Q90Max + 1e-4f
                         && q90 - q10 >= Spread9010 - 1e-4f && q50 - q10 >= Spread5010 - 1e-4f && q90 - q50 >= Spread9050 - 1e-4f,
                string.Format(CI, "R_ref depths Q10 {0:0.00} (>= {3:0.0}), Q50 {1:0.00} (in [{4:0.0}, {5:0.0}]), Q90 {2:0.00} (in [{6:0.0}, {7:0.0}]); Q90 - Q10 {8:0.00} (>= {9:0.0}), Q50 - Q10 {10:0.00}, Q90 - Q50 {11:0.00} (>= {12:0.0})",
                    q10, q50, q90, Q10Min, Q50Min, Q50Max, Q90Min, Q90Max, q90 - q10, Spread9010, q50 - q10, q90 - q50, Spread5010));
            // V11 R_ref's shares (the fallback: no channel, holes or humps to share)
            float weed = b.MatShare(BedMat.Weed), gravel = b.MatShare(BedMat.Gravel), sand = b.MatShare(BedMat.Sand), mud = b.MatShare(BedMat.Mud);
            float flatShoal = b.KindShare(BedKind.Flat) + b.KindShare(BedKind.Shoal), drop = b.KindShare(BedKind.Dropoff);
            float holeChan = b.KindShare(BedKind.Hole) + b.KindShare(BedKind.Channel), hump = b.KindShare(BedKind.Hump);
            bool v11 = weed >= WeedMin && gravel >= GravelMin && sand >= SandMin && mud >= MudMin && flatShoal >= FlatShoalMin && drop >= DropoffMin
                       && (b.Fallback || (holeChan >= HoleChannelMin && hump >= HumpMin));
            Check("V11", v11, string.Format(CI, "R_ref shares weed {0:0.0}% gravel {1:0.0}% sand {2:0.0}% mud {3:0.0}% (>= 5 / 2 / 5 / 5); flat + shoal {4:0.0}% (>= 6), drop-off {5:0.0}% (>= 3), hole + channel {6:0.0}% (>= 2){8}, hump {7:0.0}% (>= 0.5){8}",
                100f * weed, 100f * gravel, 100f * sand, 100f * mud, 100f * flatShoal, 100f * drop, 100f * holeChan, 100f * hump, b.Fallback ? " waived (fallback)" : ""));
            report = sb.ToString();
            return fails;
        }

        /// <summary>
        /// The spot centres the legend could use with this cast distance (V8), up to <paramref name="enough"/>: deep over a
        /// 0.9 m disc, in reach of (AnchorX, 0), clear of pads and solids, within 6 m of a lurk candidate.
        /// </summary>
        static int LegendSpots(Ctx c, Bathymetry b, float castDist, int enough)
        {
            var lurks = LurkCandidates(b, c.view, castDist);
            if (lurks.Count == 0) return 0;
            // (the nodes within 6 m of one: each candidate's disc stamped)
            var near = new bool[c.n];
            int rr = Mathf.CeilToInt(LurkNear / Bathymetry.Cell);
            foreach (var q in lurks)
            {
                int qi = Mathf.RoundToInt((q.x - c.x0) / Bathymetry.Cell), qj = Mathf.RoundToInt((q.y - c.z0) / Bathymetry.Cell);
                for (int dj = -rr; dj <= rr; dj++)
                for (int di = -rr; di <= rr; di++)
                {
                    int ii = qi + di, jj = qj + dj;
                    if (ii < 0 || ii >= c.nx || jj < 0 || jj >= c.nz) continue;
                    int k = jj * c.nx + ii;
                    if (!near[k] && (new Vector2(c.X(k), c.Z(k)) - q).sqrMagnitude <= LurkNear * LurkNear) near[k] = true;
                }
            }
            var a = new Vector2(AnchorX(b.StageId), 0f);
            int count = 0;
            float zMin = c.L.zNear + 3f;
            for (int k = 0; k < c.n && count < enough; k++)
            {
                if (!near[k] || c.spotBlocked[k]) continue;
                var p = new Vector2(c.X(k), c.Z(k));
                if (p.y < zMin || p.y > castDist || (p - a).magnitude > castDist - 0.5f) continue;
                if (b.MinDepthDisc(p.x, p.y, SpotDisc) < SpotDepth) continue;
                count++;
            }
            return count;
        }

        /// <summary>The lurk candidates (spec 5.4 V8 / 10): WeedEdge nodes at least 4.5 m deep, z 14 .. min(30, cast + 5), in view.</summary>
        static List<Vector2> LurkCandidates(Bathymetry b, View view, float castDist)
        {
            var list = new List<Vector2>();
            float zMax = Mathf.Min(30f, castDist + 5f);
            for (int k = 0; k < b.NodeCount; k++)
            {
                if ((b.flags[k] & (byte)BedFlag.WeedEdge) == 0 || b.cm[k] < 450) continue;
                var p = b.NodePos(k);
                if (p.y < 14f || p.y > zMax || Mathf.Abs(p.x) > view.Half(p.y, 640f) - 1f) continue;
                list.Add(p);
            }
            return list;
        }

        // ------------------------------------------------------------------ the debug picture (-fkbathy dump)
        /// <summary>
        /// A top-down picture of the grid, <paramref name="scale"/> px per node (row 0 at the bottom: z = Z0): the depth
        /// ramp, 1 m contours, the kinds' tints, zone borders, pin cores hatched, the lane's outline, the obstacles' footprints
        /// from the raw data and the lurk candidates (gold).
        /// </summary>
        public static Color32[] DumpPixels(Bathymetry b, TerrainRecipe r, ObstacleSet raw, int scale, out int width, out int height)
        {
            int W = b.Nx * scale, H = b.Nz * scale;
            width = W;
            height = H;
            var px = new Color32[W * H];
            Color32 Ramp(float d)
            {
                float t = Mathf.Clamp01(d / 9f);
                // cream (shallow) -> teal -> deep blue
                var a = new Color(0.93f, 0.89f, 0.72f);
                var m = new Color(0.30f, 0.62f, 0.66f);
                var z = new Color(0.06f, 0.14f, 0.32f);
                var col = t < 0.4f ? Color.Lerp(a, m, t / 0.4f) : Color.Lerp(m, z, (t - 0.4f) / 0.6f);
                return col;
            }
            Color KindTint(BedKind k) => k switch
            {
                BedKind.Shelf => new Color(0.95f, 0.85f, 0.4f),
                BedKind.Flat => new Color(0.4f, 0.8f, 0.3f),
                BedKind.Shoal => new Color(0.2f, 0.65f, 0.2f),
                BedKind.Dropoff => new Color(0.95f, 0.45f, 0.2f),
                BedKind.Hump => new Color(0.75f, 0.55f, 0.35f),
                BedKind.Hole => new Color(0.35f, 0.1f, 0.45f),
                BedKind.Channel => new Color(0.2f, 0.4f, 0.95f),
                BedKind.Basin => new Color(0.1f, 0.1f, 0.25f),
                _ => new Color(0.5f, 0.5f, 0.5f),
            };
            for (int j = 0; j < b.Nz; j++)
            for (int i = 0; i < b.Nx; i++)
            {
                int k = j * b.Nx + i;
                float d = b.cm[k] * 0.01f;
                Color col = Ramp(d);
                col = Color.Lerp(col, KindTint((BedKind)b.kind[k]), 0.3f);
                bool contour = false;
                int lv = Mathf.FloorToInt(d);
                if (i < b.Nx - 1 && Mathf.FloorToInt(b.cm[k + 1] * 0.01f) != lv) contour = true;
                if (j < b.Nz - 1 && Mathf.FloorToInt(b.cm[k + b.Nx] * 0.01f) != lv) contour = true;
                bool border = (i < b.Nx - 1 && b.zone[k + 1] != b.zone[k]) || (j < b.Nz - 1 && b.zone[k + b.Nx] != b.zone[k]);
                bool pinned = (b.flags[k] & (byte)BedFlag.Pinned) != 0;
                for (int dy = 0; dy < scale; dy++)
                for (int dx = 0; dx < scale; dx++)
                {
                    Color pc = col;
                    if (pinned && ((dx + dy) % 3 == 0)) pc = Color.Lerp(pc, Color.white, 0.45f);
                    if (border && (dx == scale - 1 || dy == scale - 1)) pc = Color.Lerp(pc, new Color(1f, 0.2f, 0.6f), 0.6f);
                    if (contour && dx == 0 && dy == 0) pc = Color.Lerp(pc, Color.black, 0.6f);
                    px[(j * scale + dy) * W + i * scale + dx] = pc;
                }
            }
            void Dot(float x, float z, Color32 col)
            {
                int c = Mathf.FloorToInt((x - b.X0) / Bathymetry.Cell * scale + scale * 0.5f), rr = Mathf.FloorToInt((z - b.Z0) / Bathymetry.Cell * scale + scale * 0.5f);
                if (c >= 0 && c < W && rr >= 0 && rr < H) px[rr * W + c] = col;
            }
            void Outline(Vector2[] poly, Color32 col)
            {
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[j];
                    var e = poly[i];
                    int steps = Mathf.Max(1, Mathf.CeilToInt((e - a).magnitude / (Bathymetry.Cell / scale)));
                    for (int s = 0; s <= steps; s++)
                    {
                        var p = Vector2.Lerp(a, e, s / (float)steps);
                        Dot(p.x, p.y, col);
                    }
                }
            }
            if (r?.lane != null) Outline(r.lane, new Color32(255, 255, 255, 255));
            if (raw?.obstacles != null)
                foreach (var o in raw.obstacles)
                {
                    if (o == null || o.kind == "cover") continue;
                    var poly = Obstacles.Footprint(o);
                    if (poly == null || poly.Length < 3) continue;
                    var col = o.kind == "solid" ? new Color32(255, 224, 64, 255) : o.kind == "pad" ? new Color32(112, 240, 160, 255)
                        : o.kind == "snag" ? new Color32(96, 224, 255, 255) : new Color32(168, 240, 112, 255);
                    Outline(poly, col);
                }
            for (int k = 0; k < b.NodeCount; k++)
            {
                if ((b.flags[k] & (byte)BedFlag.WeedEdge) == 0 || b.cm[k] < 450) continue;
                var p = b.NodePos(k);
                if (p.y < 14f || p.y > 30f) continue;
                Dot(p.x, p.y, new Color32(255, 200, 40, 255));
            }
            return px;
        }
    }
}
