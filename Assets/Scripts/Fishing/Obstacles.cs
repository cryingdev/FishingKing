using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishingKing
{
    // Docs/obstacles_spec.md 2: the obstacle data exported from the Blender stage scenes (Tools/Blender/variants/hybrid/
    // hyb_obstacles.py) into Assets/Resources/Data/obstacles_<stage>.json, in game space (metres, plan = (x, z)).

    [Serializable]
    public class ObstacleTier
    {
        public float y0, y1;
        public float[] pts;
    }

    [Serializable]
    public class Obstacle
    {
        public string id, kind, mat, shape;
        public float x, z, r, rad, x0, z0, x1, z1;
        public float[] pts;
        public float bot, top;
        public bool bed;
        public ObstacleTier[] tiers;
        public float grabK = 1f, roughK = 1f;
        public string[] tags, coverFor;
        public float hx, hz;
        public bool holdCross;
        public string layer, parent;
        public string[] src;

        // ---- runtime, built on load
        /// <summary>The footprint at the waterline (solids) / the zone's plan outline: convex, counter-clockwise in (x, z).</summary>
        [NonSerialized] public Vector2[] Poly;
        /// <summary>Solids: the stacked prisms (bottom to top) and their y ranges.</summary>
        [NonSerialized] public Vector2[][] TierPoly;
        [NonSerialized] public float[] TierY0, TierY1;
        /// <summary>Overhangs: the plan hull of every tier (the shade under the branches).</summary>
        [NonSerialized] public Vector2[] Shade;
        [NonSerialized] public ObstacleMat Mat;
        [NonSerialized] public Vector2 C;
        /// <summary>Broad-phase radius round <see cref="C"/> over every point of the footprint and the tiers.</summary>
        [NonSerialized] public float R;
        [NonSerialized] public bool Abrasive, Near, Overhang, Standing;
        [NonSerialized] public Obstacle Parent;
        /// <summary>The name used in the texts (바위, 말뚝, 통나무, 배 밑 ...).</summary>
        [NonSerialized] public string Name;

        public bool Has(string tag)
        {
            if (tags != null) foreach (var t in tags) if (t == tag) return true;
            return false;
        }

        public Vector2 Hold => new Vector2(hx, hz);
        public bool Hard => kind == "snag";
        public bool IsWeed => kind == "weed";
    }

    [Serializable]
    public class ObstacleCamera
    {
        public float standH, camBack, camUp, pitch, focalPx, xLim, zNear, zFar;
        public int widthPx, heightPx;
    }

    [Serializable]
    public class ObstacleSet
    {
        public int version;
        public string stage, generator, source;
        public ObstacleCamera camera;
        public Obstacle[] obstacles;
    }

    /// <summary>A contact of the flying rig with a solid (<see cref="Obstacles.SweepSolid"/>).</summary>
    public struct ObstacleHit
    {
        public Obstacle o;
        public int tier;
        public Vector3 at, normal;
        public bool top;
        public float t;
    }

    /// <summary>
    /// A material's constants (Docs/obstacles_spec.md 1.2): e = restitution of the normal speed, keep = the tangential
    /// speed kept at a contact, grab = snag factor, rough = abrasion factor, freeK = how readily a snag there comes free.
    /// </summary>
    public struct ObstacleMat
    {
        public string id;
        public float e, keep, grab, rough, freeK;
        public string word;     // the contact word (탁! 딱! 톡! 퉁! 쨍! 사삭)
        public string name;     // the name in the texts
        public Color fx;        // the contact puff's colour
        public int sound;       // 0 none, 1 Tock, 2 Knock, 3 Thunk, 4 Ting, 5 Rustle, 6 Tear

        static ObstacleMat M(string id, float e, float keep, int sound, string word, float grab, float rough, float freeK, string name, string fx) =>
            new ObstacleMat { id = id, e = e, keep = keep, sound = sound, word = word, grab = grab, rough = rough, freeK = freeK, name = name, fx = Art.Hex(fx) };

        public static ObstacleMat Get(string mat) => mat switch
        {
            "concrete" => M(mat, 0.40f, 0.50f, 1, "딱!", 1.4f, 1.3f, 0.7f, "테트라포드", "#c8c8c0"),
            "wood" => M(mat, 0.30f, 0.50f, 2, "톡!", 1.2f, 0.7f, 0.8f, "나무", "#8a6a48"),
            "root" => M(mat, 0.25f, 0.45f, 2, "톡!", 1.3f, 0.8f, 0.7f, "뿌리", "#8a6a48"),
            "hull" => M(mat, 0.35f, 0.60f, 3, "퉁!", 0.3f, 0.6f, 1.2f, "배", "#e8e8e8"),
            "crystal" => M(mat, 0.55f, 0.70f, 4, "쨍!", 0.8f, 1.6f, 1.0f, "수정", "#aef0ff"),
            "leaf" => M(mat, 0.10f, 0.20f, 5, "사삭", 0f, 0.2f, 1f, "나뭇가지", "#6a8a4a"),
            "reed" => M(mat, 0f, 0f, 5, "", 0.9f, 0.3f, 1.3f, "갈대", "#8a9a5a"),
            "weed" => M(mat, 0f, 0f, 0, "", 1.0f, 0f, 1.4f, "수초", "#6a8a4a"),
            "pad" => M(mat, 0f, 0f, 6, "", 0f, 0f, 1.6f, "연잎", "#6a9a4a"),
            "ice" => M(mat, 0.30f, 0.60f, 0, "", 0f, 0.9f, 1f, "얼음 구멍 가장자리", "#e8f4ff"),
            "gravel" => M(mat, 0f, 0f, 0, "", 0.8f, 0.6f, 1.1f, "돌바닥", "#a0a098"),
            _ => M("rock", 0.45f, 0.55f, 1, "탁!", 1.0f, 1.0f, 1.0f, "바위", "#a0a098"),
        };

        public AudioClip Sound => sound switch
        {
            1 => Sfx.Tock,
            2 => Sfx.Knock,
            3 => Sfx.Thunk,
            4 => Sfx.Ting,
            5 => Sfx.Rustle,
            6 => Sfx.Tear,
            _ => null,
        };
    }

    /// <summary>
    /// A stage's obstacles (Docs/obstacles_spec.md 2.5), loaded from Resources/Data/obstacles_&lt;stage&gt;.json: the painted
    /// props above the water (solids: casts hit them, rigs in the water stop against them, lines rub on them), lily pads,
    /// the underwater snag and weed zones (밑걸림) and the fish's covers. A stage without the file (or -fkobstacles off) gets
    /// an empty set: every query is false / 1 and the game plays as before. All queries reject by the bounding circle first.
    /// </summary>
    public class Obstacles
    {
        /// <summary>Test switches (section 12): -fkobstacles off / show, -fkobstlog, -fksnag &lt;x&gt;.</summary>
        public static bool Off, Show, Log;
        public static float SnagMult = 1f;
        /// <summary>The obstacle rolls (snags, freeing, pad catches, cover runs); the autopilot seeds it.</summary>
        public static System.Random Rnd = new System.Random();
        public static float Roll() => (float)Rnd.NextDouble();

        public readonly StageLayout L;
        public readonly List<Obstacle> All = new List<Obstacle>(), Solids = new List<Obstacle>(), Pads = new List<Obstacle>(),
            Snags = new List<Obstacle>(), Weeds = new List<Obstacle>(), Covers = new List<Obstacle>(), Rims = new List<Obstacle>();
        readonly Dictionary<string, Obstacle> byId = new Dictionary<string, Obstacle>();

        public bool Empty => All.Count == 0;
        public Obstacle Get(string id) => id != null && byId.TryGetValue(id, out var o) ? o : null;

        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        /// <summary>An [OBST] line with -fkobstlog.</summary>
        public static void Say(string msg)
        {
            if (Log) Debug.Log("[OBST] " + msg);
        }

        Obstacles(StageLayout l) { L = l; }

        public static Obstacles Load(StageLayout l)
        {
            var set = new Obstacles(l);
            if (l == null) return set;
            if (Off)
            {
                Debug.Log($"[OBST] {l.id}: obstacles off");
                return set;
            }
            var ta = Resources.Load<TextAsset>("Data/obstacles_" + l.id);
            if (ta == null)
            {
                Debug.Log($"[OBST] {l.id}: no obstacle data");
                return set;
            }
            ObstacleSet data = null;
            try { data = JsonUtility.FromJson<ObstacleSet>(ta.text); }
            catch (Exception e) { Debug.LogWarning($"[OBST] obstacles_{l.id}.json unreadable: {e.Message}"); }
            if (data?.obstacles == null) return set;
            var c = data.camera;
            if (c != null)
            {
                bool Off1(float a, float b) => Mathf.Abs(a - b) > 0.001f;
                if (Off1(c.standH, l.standH) || Off1(c.camBack, l.camBack) || Off1(c.camUp, l.camUp) || Off1(c.pitch, l.pitch) || Off1(c.focalPx, l.focalPx)
                    || Off1(c.xLim, l.xLim) || Off1(c.zNear, l.zNear) || Off1(c.zFar, l.zFar) || (l.widthPx > 0 && c.widthPx != l.widthPx) || (l.heightPx > 0 && c.heightPx != l.heightPx))
                    Debug.LogWarning($"[OBST] obstacles_{l.id}.json was exported for another camera: re-export");
            }
            foreach (var o in data.obstacles) set.Add(o);
            foreach (var o in set.All) set.Resolve(o);
            Debug.Log($"[OBST] {l.id}: {set.Solids.Count} solid, {set.Pads.Count} pad, {set.Snags.Count} snag, {set.Weeds.Count} weed, {set.Covers.Count} cover, {set.Rims.Count} rim");
            return set;
        }

        void Add(Obstacle o)
        {
            if (o == null || string.IsNullOrEmpty(o.kind)) return;
            o.Poly = BuildPoly(o.pts, o);
            if (o.Poly.Length < 3) return;
            o.C = new Vector2(o.x, o.z);
            o.Mat = ObstacleMat.Get(o.mat);
            o.Abrasive = o.Has("abrasive");
            o.Overhang = o.Has("overhang");
            o.Standing = o.kind == "solid" && o.bot <= 0.05f && !o.Overhang;
            if (o.grabK <= 0f && o.kind != "solid") o.grabK = 1f;
            if (o.roughK <= 0f) o.roughK = 1f;
            float R = o.r;
            foreach (var p in o.Poly) R = Mathf.Max(R, (p - o.C).magnitude);
            if (o.kind == "solid")
            {
                int n = o.tiers != null && o.tiers.Length > 0 ? o.tiers.Length : 1;
                o.TierPoly = new Vector2[n][];
                o.TierY0 = new float[n];
                o.TierY1 = new float[n];
                var all = new List<Vector2>();
                for (int k = 0; k < n; k++)
                {
                    var t = o.tiers != null && o.tiers.Length > 0 ? o.tiers[k] : null;
                    o.TierPoly[k] = t != null ? BuildPoly(t.pts, o) : o.Poly;
                    if (o.TierPoly[k].Length < 3) o.TierPoly[k] = o.Poly;
                    o.TierY0[k] = t != null ? t.y0 : o.bot;
                    o.TierY1[k] = t != null ? t.y1 : o.top;
                    foreach (var p in o.TierPoly[k])
                    {
                        R = Mathf.Max(R, (p - o.C).magnitude);
                        all.Add(p);
                    }
                }
                if (o.Overhang) o.Shade = Hull(all);
            }
            o.R = R + 0.02f;
            All.Add(o);
            if (!byId.ContainsKey(o.id ?? "")) byId[o.id ?? ""] = o;
            switch (o.kind)
            {
                case "solid": Solids.Add(o); break;
                case "pad": Pads.Add(o); break;
                case "snag": Snags.Add(o); break;
                case "weed": Weeds.Add(o); break;
                case "cover": Covers.Add(o); break;
                case "rim": Rims.Add(o); break;
            }
        }

        void Resolve(Obstacle o)
        {
            o.Parent = !string.IsNullOrEmpty(o.parent) ? Get(o.parent) : null;
            o.Near = o.Has("near") || (o.Parent != null && o.Parent.Has("near"));
            // the name in the texts: the material's, by the tags of the prop (a derived zone: its parent's)
            var named = o.Parent ?? o;
            string n = o.Mat.name;
            if (o.Mat.id == "wood") n = named.Has("post") ? "말뚝" : named.Has("log") ? "통나무" : named.Has("stump") ? "그루터기" : "나무";
            if (o.Mat.id == "hull" && L.id == "ocean") n = "배 밑";
            o.Name = n;
        }

        static Vector2[] BuildPoly(float[] p, Obstacle o)
        {
            Vector2[] v;
            if (p != null && p.Length >= 6)
            {
                v = new Vector2[p.Length / 2];
                for (int i = 0; i < v.Length; i++) v[i] = new Vector2(p[2 * i], p[2 * i + 1]);
            }
            else
            {
                // (no points: the circle / capsule parameters)
                float rad = o.rad > 0f ? o.rad : o.r;
                var list = new List<Vector2>();
                var a = o.shape == "capsule" ? new Vector2(o.x0, o.z0) : new Vector2(o.x, o.z);
                var b = o.shape == "capsule" ? new Vector2(o.x1, o.z1) : a;
                for (int i = 0; i < 16; i++)
                {
                    float ang = i / 16f * Mathf.PI * 2f;
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    list.Add(a + d);
                    list.Add(b + d);
                }
                return Hull(list);
            }
            float area = 0f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++) area += v[j].x * v[i].y - v[i].x * v[j].y;
            if (area < 0f) Array.Reverse(v);
            return v;
        }

        /// <summary>The convex hull (counter-clockwise) of a point set (monotone chain).</summary>
        public static Vector2[] Hull(List<Vector2> pts)
        {
            if (pts.Count < 3) return pts.ToArray();
            var s = new List<Vector2>(pts);
            s.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new Vector2[s.Count * 2];
            int k = 0;
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int i = 0; i < s.Count; i++)
            {
                while (k >= 2 && Cross(h[k - 2], h[k - 1], s[i]) <= 0f) k--;
                h[k++] = s[i];
            }
            for (int i = s.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(h[k - 2], h[k - 1], s[i]) <= 0f) k--;
                h[k++] = s[i];
            }
            var r = new Vector2[Mathf.Max(0, k - 1)];
            Array.Copy(h, r, r.Length);
            return r;
        }

        // ------------------------------------------------------------------ geometry
        /// <summary>Point in a convex CCW polygon: every edge's cross product >= 0.</summary>
        public static bool InPoly(Vector2[] poly, Vector2 p)
        {
            int n = poly.Length;
            if (n < 3) return false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = poly[j];
                var b = poly[i];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < -1e-6f) return false;
            }
            return true;
        }

        /// <summary>The distance from a point to a polygon's boundary, the nearest boundary point and that edge's outward normal.</summary>
        public static float EdgeDist(Vector2[] poly, Vector2 p, out Vector2 nearest, out Vector2 normal)
        {
            float best = float.MaxValue;
            nearest = p;
            normal = Vector2.down;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = poly[j];
                var ab = poly[i] - a;
                float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                var q = a + ab * t;
                float d = (p - q).magnitude;
                if (d < best)
                {
                    best = d;
                    nearest = q;
                    normal = l2 > 1e-10f ? new Vector2(ab.y, -ab.x).normalized : Vector2.down;
                }
            }
            return best;
        }

        /// <summary>Plan distance from a point to the obstacle's footprint (0 inside).</summary>
        public static float Dist(Obstacle o, Vector2 xz) => InPoly(o.Poly, xz) ? 0f : EdgeDist(o.Poly, xz, out _, out _);

        public float Bed(float z) => -L.DepthAt(z);
        /// <summary>The obstacle's lowest y at this z (bot = -99: down to the bed).</summary>
        public float BotAt(Obstacle o, float z) => o.bot <= -98f ? Bed(z) : o.bot;

        /// <summary>The footprint (+ <paramref name="grow"/> m) holds this plan point.</summary>
        public bool Inside(Obstacle o, Vector2 xz, float grow = 0f)
        {
            float rr = o.R + grow + 0.01f;
            if ((xz - o.C).sqrMagnitude > rr * rr) return false;
            if (InPoly(o.Poly, xz)) return true;
            return grow > 0f && EdgeDist(o.Poly, xz, out _, out _) <= grow;
        }

        /// <summary>The tier of a solid holding this point (-1: none).</summary>
        public static int TierAt(Obstacle o, Vector3 p)
        {
            if (o.TierPoly == null) return -1;
            var xz = new Vector2(p.x, p.z);
            for (int k = 0; k < o.TierPoly.Length; k++)
                if (p.y >= o.TierY0[k] - 1e-4f && p.y <= o.TierY1[k] + 1e-4f && InPoly(o.TierPoly[k], xz)) return k;
            return -1;
        }

        /// <summary>A point inside a solid (the ones at his feet, tagged near, are left out: the rod tip is above and beyond them).</summary>
        public bool InSolid(Vector3 p, out Obstacle o, out int tier)
        {
            var xz = new Vector2(p.x, p.z);
            foreach (var s in Solids)
            {
                if (s.Near) continue;
                float rr = s.R + 0.05f;
                if ((xz - s.C).sqrMagnitude > rr * rr || p.y < s.bot - 0.01f || p.y > s.top + 0.01f) continue;
                int k = TierAt(s, p);
                if (k >= 0)
                {
                    o = s;
                    tier = k;
                    return true;
                }
            }
            o = null;
            tier = -1;
            return false;
        }

        /// <summary>
        /// The first contact of the segment a-b with a solid (spec 4.3): sub-steps of at most 0.15 m, the first hit refined
        /// by 4 bisections; a top contact (coming down onto a tier from above, inside it) has the normal up, a side contact
        /// the outward normal of the tier's nearest edge (a circle: radial). <see cref="ObstacleHit.at"/> is the last point
        /// outside.
        /// </summary>
        public bool SweepSolid(Vector3 a, Vector3 b, out ObstacleHit hit)
        {
            hit = default;
            if (Solids.Count == 0) return false;
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.15f));
            var prev = a;
            for (int i = 1; i <= n; i++)
            {
                var q = Vector3.Lerp(a, b, i / (float)n);
                if (InSolid(q, out var o, out int tier))
                {
                    Vector3 lo = prev, hi = q;
                    for (int k = 0; k < 4; k++)
                    {
                        var mid = (lo + hi) * 0.5f;
                        if (TierAt(o, mid) >= 0) hi = mid;
                        else lo = mid;
                    }
                    int th = TierAt(o, hi);
                    if (th >= 0) tier = th;
                    var loXZ = new Vector2(lo.x, lo.z);
                    bool top = lo.y >= o.TierY1[tier] - 1e-3f && InPoly(o.TierPoly[tier], loXZ);
                    Vector3 nrm;
                    if (top) nrm = Vector3.up;
                    else
                    {
                        var n2 = SideNormal(o, tier, new Vector2(hi.x, hi.z));
                        nrm = new Vector3(n2.x, 0f, n2.y);
                    }
                    hit = new ObstacleHit { o = o, tier = tier, at = lo, normal = nrm, top = top, t = len > 1e-6f ? Vector3.Distance(a, lo) / len : 0f };
                    return true;
                }
                prev = q;
            }
            return false;
        }

        static Vector2 SideNormal(Obstacle o, int tier, Vector2 xz)
        {
            if (o.shape == "circle")
            {
                var d = xz - o.C;
                if (d.sqrMagnitude > 1e-8f) return d.normalized;
            }
            else if (o.shape == "capsule")
            {
                var a = new Vector2(o.x0, o.z0);
                var ab = new Vector2(o.x1, o.z1) - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(xz - a, ab) / ab.sqrMagnitude) : 0f;
                var d = xz - (a + ab * t);
                if (d.sqrMagnitude > 1e-8f) return d.normalized;
            }
            EdgeDist(o.TierPoly[Mathf.Clamp(tier, 0, o.TierPoly.Length - 1)], xz, out _, out var nrm);
            return nrm;
        }

        /// <summary>The pad under this plan point (null: none).</summary>
        public Obstacle PadAt(Vector2 xz)
        {
            foreach (var p in Pads) if (Inside(p, xz)) return p;
            return null;
        }

        /// <summary>
        /// The snag (<paramref name="hard"/>) or weed zone holding the hook at <paramref name="p"/>: its plan footprint holds
        /// p.xz and its band (bed..top) holds p.y. A topwater lure on the film (<paramref name="film"/>) is only in zones
        /// reaching the surface (top >= -0.1: reeds, surface weeds).
        /// </summary>
        public Obstacle ZoneAt(Vector3 p, bool hard, bool weed, bool film = false)
        {
            var xz = new Vector2(p.x, p.z);
            if (hard)
                foreach (var o in Snags)
                    if (InBand(o, p, film) && Inside(o, xz)) return o;
            if (weed)
                foreach (var o in Weeds)
                    if (InBand(o, p, film) && Inside(o, xz)) return o;
            return null;
        }

        bool InBand(Obstacle o, Vector3 p, bool film)
        {
            if (film) return o.top >= -0.1f;
            return p.y <= o.top + 1e-3f && p.y >= BotAt(o, p.z) - 0.3f;
        }

        /// <summary>Inside a standing solid's waterline footprint (+ grow): no rig in the water goes there (spec 4.9).</summary>
        public bool BlockedAtSurface(Vector2 xz, float grow, out Obstacle o)
        {
            foreach (var s in Solids)
            {
                if (!s.Standing || s.Near) continue;
                if (Inside(s, xz, grow))
                {
                    o = s;
                    return true;
                }
            }
            o = null;
            return false;
        }

        public bool BlockedAtSurface(Vector2 xz, float grow = 0.05f) => BlockedAtSurface(xz, grow, out _);
        /// <summary>A plan point pushed out of any standing solid's footprint (+ grow) along its nearest edge's normal.</summary>
        public Vector2 PushOut(Vector2 xz, float grow = 0.05f)
        {
            for (int i = 0; i < 4 && BlockedAtSurface(xz, grow, out var o); i++)
            {
                EdgeDist(o.Poly, xz, out var q, out var nrm);
                xz = q + nrm * (grow + 0.02f);
            }
            return xz;
        }

        /// <summary>
        /// The water point is hidden from the camera by a prop (the line of sight up to it passes through a solid): the
        /// painted front layer covers it, but it is water behind the prop, not the bank.
        /// </summary>
        public bool Occluded(Vector3 p, Vector3 cam) => Occluded(p, cam, out _);

        /// <summary><see cref="Occluded(Vector3, Vector3)"/> and the prop that hides it (<paramref name="by"/>).</summary>
        public bool Occluded(Vector3 p, Vector3 cam, out Obstacle by)
        {
            by = null;
            if (Solids.Count == 0) return false;
            var d = cam - p;
            float len = d.magnitude;
            if (len < 1e-3f) return false;
            d /= len;
            if (d.y <= 1e-4f) return false;
            float maxTop = 0f;
            foreach (var s in Solids) maxTop = Mathf.Max(maxTop, s.top);
            float maxT = Mathf.Min(len, (maxTop + 0.05f - p.y) / d.y);
            // (only the solids near the stretch of the sight line that can meet one: the sea / the swamp have 120+)
            var e = p + d * maxT;
            var a2 = new Vector2(p.x, p.z);
            var b2 = new Vector2(e.x, e.z);
            occl.Clear();
            foreach (var s in Solids)
                if (SegDist(a2, b2, s.C) <= s.R + 0.05f) occl.Add(s);
            if (occl.Count == 0) return false;
            for (float t = 0.04f; t <= maxT; t += 0.08f)
            {
                var q = p + d * t;
                var xz = new Vector2(q.x, q.z);
                foreach (var s in occl)
                {
                    float rr = s.R + 0.05f;
                    if ((xz - s.C).sqrMagnitude > rr * rr || q.y < s.bot - 0.01f || q.y > s.top + 0.01f) continue;
                    if (TierAt(s, q) >= 0)
                    {
                        by = s;
                        return true;
                    }
                }
            }
            return false;
        }

        readonly List<Obstacle> occl = new List<Obstacle>();

        /// <summary>Under an overhang's shade (the hull of its tiers): the overhang, else null.</summary>
        public Obstacle UnderOverhang(Vector2 xz)
        {
            foreach (var s in Solids)
            {
                if (!s.Overhang || s.Shade == null || s.Shade.Length < 3) continue;
                float rr = s.R + 0.01f;
                if ((xz - s.C).sqrMagnitude > rr * rr) continue;
                if (InPoly(s.Shade, xz)) return s;
            }
            return null;
        }

        // ------------------------------------------------------------------ fights (spec 7)
        /// <summary>
        /// Cyrus-Beck: the part [s0, s1] of the segment e-f inside the convex polygon grown by <paramref name="grow"/> m
        /// (each edge's line pushed out); false when it misses.
        /// </summary>
        public static bool Clip(Vector2 e, Vector2 f, Vector2[] poly, float grow, out float s0, out float s1)
        {
            s0 = 0f;
            s1 = 1f;
            var d = f - e;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = poly[j];
                var ab = poly[i] - a;
                if (ab.sqrMagnitude < 1e-12f) continue;
                var nrm = new Vector2(ab.y, -ab.x).normalized;
                float num = grow - Vector2.Dot(nrm, e - a);
                float den = Vector2.Dot(nrm, d);
                if (Mathf.Abs(den) < 1e-9f)
                {
                    if (num < 0f) return false;
                    continue;
                }
                float s = num / den;
                if (den > 0f) s1 = Mathf.Min(s1, s);
                else s0 = Mathf.Max(s0, s);
                if (s0 > s1) return false;
            }
            return s0 <= s1;
        }

        /// <summary>
        /// Whether the line rubs on structure this fight frame (spec 7.4): the plan line from its water entry
        /// <paramref name="entry"/> to the fish crosses the footprint (+0.10 m) of an abrasive solid standing in the water, or
        /// an abrasive snag zone whose band holds the line's depth where it crosses (its deepest point there); or the fish
        /// itself is inside an abrasive snag zone (grinding the line on a rock's base). The props at his feet (near) are left
        /// out. Of several, the roughest counts; <paramref name="at"/> is the contact point.
        /// </summary>
        /// <summary>The line rubs on a hull's under-boat zone only with the fish within this (m, plan) of it.</summary>
        public const float HullRubReach = 1.0f;

        public bool Rub(Vector2 entry, Vector3 fish, out Obstacle o, out Vector3 at)
        {
            o = null;
            at = Vector3.zero;
            if (Empty) return false;
            float best = 0f;
            var f2 = new Vector2(fish.x, fish.z);
            float dF = Mathf.Max(0f, -fish.y);
            var seg = f2 - entry;
            float segLen = seg.magnitude;
            foreach (var s in Solids)
            {
                if (!s.Abrasive || !s.Standing || s.Near) continue;
                if (SegDist(entry, f2, s.C) > s.R + 0.12f) continue;
                if (!Clip(entry, f2, s.Poly, 0.10f, out float s0, out _)) continue;
                float k = s.Mat.rough * s.roughK;
                if (k <= best) continue;
                best = k;
                o = s;
                var p = entry + seg * s0;
                at = new Vector3(p.x, -dF * s0, p.y);
            }
            foreach (var s in Snags)
            {
                if (!s.Abrasive || s.Near) continue;
                float k = s.Mat.rough * s.roughK;
                if (k <= best) continue;
                // the fish inside the zone, its depth in the band
                if (Inside(s, f2) && -dF <= s.top + 1e-3f && -dF >= BotAt(s, fish.z) - 0.3f)
                {
                    best = k;
                    o = s;
                    at = fish;
                    continue;
                }
                // a hull (under the boat): only with the fish under it or right by it, not a line merely entering the
                // water over its skirt by the bow
                if (s.Mat.id == "hull" && !Inside(s, f2, HullRubReach)) continue;
                if (segLen < 0.05f || SegDist(entry, f2, s.C) > s.R + 0.05f) continue;
                if (!Clip(entry, f2, s.Poly, 0f, out float c0, out float c1)) continue;
                var deep = entry + seg * c1;
                float y = -dF * c1;
                if (y > s.top + 1e-3f || y < BotAt(s, deep.y) - 0.3f) continue;
                best = k;
                o = s;
                var p = entry + seg * c0;
                at = new Vector3(p.x, -dF * c0, p.y);
            }
            return o != null;
        }

        static float SegDist(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>The cover zone counts as one of the species' cover types.</summary>
        public static bool CoverMatch(Obstacle c, FishSpecies sp)
        {
            if (c.coverFor == null || sp.coverFor == null) return false;
            foreach (var a in c.coverFor)
            foreach (var b in sp.coverFor)
                if (a == b) return true;
            return false;
        }

        /// <summary>
        /// The nearest cover of the species' types whose hold point lies within <paramref name="reach"/> m (plan) of the fish
        /// and passes <paramref name="ok"/> (reachable in the fight). Null: none.
        /// </summary>
        public Obstacle NearestCover(Vector3 fish, FishSpecies sp, float reach, Func<Vector2, bool> ok)
        {
            Obstacle best = null;
            float bd = float.MaxValue;
            var f2 = new Vector2(fish.x, fish.z);
            foreach (var c in Covers)
            {
                if (!CoverMatch(c, sp)) continue;
                float d = (c.Hold - f2).magnitude;
                if (d > reach || d >= bd) continue;
                if (ok != null && !ok(c.Hold)) continue;
                bd = d;
                best = c;
            }
            return best;
        }

        /// <summary>
        /// Bites near structure (spec 8.1): the hook in a cover zone of the species' types (a cover seeker, seek >= 0.3)
        /// x1.35; in any cover x1.15; else in a snag / weed zone x1.1; else 1. (The cover is taken in plan: a surface lure
        /// over it counts.)
        /// </summary>
        public float StructureMult(Vector3 hook, FishSpecies sp)
        {
            if (Empty || sp == null) return 1f;
            var xz = new Vector2(hook.x, hook.z);
            float m = 1f;
            foreach (var c in Covers)
            {
                if (!Inside(c, xz)) continue;
                m = Mathf.Max(m, sp.coverSeek >= 0.3f && CoverMatch(c, sp) ? 1.35f : 1.15f);
            }
            if (m <= 1f && ZoneAt(hook, true, true, !(hook.y < -0.12f)) != null) m = 1.1f;
            return m;
        }

        /// <summary>A random cover of the species' types with its hold point short of <paramref name="zMax"/> (null: none).</summary>
        public Obstacle RandomCover(FishSpecies sp, float zMax, Func<Obstacle, bool> also = null)
        {
            Obstacle pick = null;
            int n = 0;
            foreach (var c in Covers)
            {
                if (!CoverMatch(c, sp) || c.hz > zMax || c.hz < L.zNear + 1f) continue;
                if (also != null && !also(c)) continue;
                n++;
                if (UnityEngine.Random.Range(0, n) == 0) pick = c;
            }
            return pick;
        }

        /// <summary>A random plan point inside the zone (rejection sampling in its box; the centroid when that fails).</summary>
        public static Vector2 RandomPoint(Obstacle o)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var p in o.Poly)
            {
                x0 = Mathf.Min(x0, p.x);
                x1 = Mathf.Max(x1, p.x);
                z0 = Mathf.Min(z0, p.y);
                z1 = Mathf.Max(z1, p.y);
            }
            for (int i = 0; i < 24; i++)
            {
                var q = new Vector2(UnityEngine.Random.Range(x0, x1), UnityEngine.Random.Range(z0, z1));
                if (InPoly(o.Poly, q)) return q;
            }
            return o.C;
        }

        /// <summary>Stream pockets (spec 3.2): the centres of the rocks tagged pocket, for CurrentField.</summary>
        public List<Vector2> PocketRocks()
        {
            var r = new List<Vector2>();
            foreach (var s in Solids) if (s.Has("pocket")) r.Add(s.C);
            return r;
        }

        public static string F(float v) => v.ToString("0.00", CI);
    }
}
