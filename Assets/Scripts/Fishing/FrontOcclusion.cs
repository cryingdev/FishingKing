using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Hides what is drawn over the stage's front layer (StageView.OrderFront) where the front layer is nearer the camera,
    /// pixel by pixel: the line running down behind the pier / the rocks / the quay / the boat, a fish jumping or lifted
    /// out beside it, the spray, the float in the air, the bait. Everything drawn over the front layer used to show on top
    /// of it wherever it was.
    /// <para>The front layer's depth map (Sprites/Stages/&lt;stage&gt;_front_depth, Data/frontdepth_&lt;stage&gt;.json from
    /// Tools/Blender/variants/hybrid/hyb_frontdepth.py) holds, per front pixel, the camera depth of the prop drawn there
    /// (<see cref="Persp.ToPixel(Vector3, out float)"/>'s depth; the same for every period). The FrontOcclude shader
    /// (Resources/Models/FrontOcclude) finds the texel under each fragment from its world position (the front layer's
    /// rect, the ocean deck's bob included; the camera shake moves the camera, not the scene) and drops the fragment when
    /// its own depth lies more than <see cref="Bias"/> behind it. Flat things floating on the water (lily pads, duckweed)
    /// never hide anything above the water, so they are left out.</para>
    /// <para>A sprite carries one depth (<see cref="SetDepth"/>: a per-renderer property that stays on it from
    /// <see cref="Use"/> on; "no depth" is <see cref="NoDepth"/>); a line carries one per vertex in its world z
    /// (<see cref="Point"/>, <see cref="ZPerMetre"/>), so a line crossing the pier's edge is cut exactly there.
    /// No depth (0) is never hidden. Without a map (or with -fkocclusion off) nothing is hidden: as before. The angler,
    /// the rod, the reel, the arrows, the HUD and the sparkles never use it.</para>
    /// <para>Within a frame: every depth is set where its thing is placed (the fish, the float and the bait in their
    /// LateUpdate / Update, the line's vertices in the Angler's LateUpdate, which runs after the fish's and before the
    /// Tackle's); the map's rect is placed by the StageView from the front sprite's final transform (after the deck bob and
    /// the period look) and again in its LateUpdate, so the shader always tests against where the front layer is drawn.</para>
    /// </summary>
    public static class FrontOcclusion
    {
        /// <summary>World z per metre of depth on the line's vertices (the shader reads depth = z / this); 1000 m = z 10.</summary>
        public const float ZPerMetre = 0.01f;
        /// <summary>Metres a fragment may lie behind the front surface under it and still show (the line lying on the water at the pier's foot).</summary>
        public const float Bias = 0.12f;
        /// <summary>
        /// Metres nearer than the point it rests on a sprite centred on a contact is tested at (the bait perched on a prop,
        /// a hit's flash on a face): its lower half lies over the very surface it touches, which must not hide it.
        /// </summary>
        public const float Resting = 0.6f;

        static readonly int TexId = Shader.PropertyToID("_FKOccTex"), RectId = Shader.PropertyToID("_FKOccRect"),
            DecId = Shader.PropertyToID("_FKOccDec"), ZId = Shader.PropertyToID("_FKOccZ"), DepthId = Shader.PropertyToID("_OccDepth");

        [System.Serializable]
        class Info
        {
            public float near, far;
            public int widthPx, heightPx;
        }

        static Material mat;
        static bool matTried;
        static MaterialPropertyBlock mpb;
        static Texture2D tex;
        static Color32[] texels;
        static float near, step;
        static Vector2 rectMin, rectSize = Vector2.one;
        static Object owner;
        static bool enabled = !OffArg();

        /// <summary>-fkocclusion off (or 0): start with hiding off.</summary>
        static bool OffArg()
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-fkocclusion");
            return i >= 0 && i + 1 < a.Length && (a[i + 1] == "off" || a[i + 1] == "0");
        }

        /// <summary>The stage whose map is bound (null: none).</summary>
        public static string StageId { get; private set; }

        /// <summary>A map is bound and the occluding material exists.</summary>
        public static bool Ready => tex != null && Material != null;

        /// <summary>Hiding on (a test switch: the before / after captures; -fkocclusion off starts with it off).</summary>
        public static bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                Push();
            }
        }

        /// <summary>The occluding sprite / line material (Resources/Models/FrontOcclude), or null: nothing is ever hidden.</summary>
        public static Material Material
        {
            get
            {
                if (matTried) return mat;
                matTried = true;
                mat = Resources.Load<Material>("Models/FrontOcclude");
                if (mat == null || mat.shader == null || !mat.shader.isSupported)
                {
                    var sh = Shader.Find("FishingKing/FrontOcclude");
                    mat = sh != null && sh.isSupported ? new Material(sh) { name = "FrontOcclude" } : null;
                }
                if (mat == null) Debug.LogWarning("[OCC] FrontOcclude shader missing: nothing is hidden behind the front layer");
                Shader.SetGlobalFloat(ZId, 1f / ZPerMetre);
                return mat;
            }
        }

        /// <summary>
        /// Gives the renderer the occluding material for good (it draws as before until it is given a depth: a sprite starts
        /// with "no depth"). The material and the property block are never taken off again: a state change only sets the
        /// depth (<see cref="SetDepth"/>), so there is no frame in which a renderer draws without its test.
        /// </summary>
        public static void Use(Renderer r)
        {
            var m = Material;
            if (m == null || r == null) return;
            r.sharedMaterial = m;
            if (!(r is LineRenderer)) SetDepth(r, 0f);
        }

        /// <summary>
        /// The sprite's depth for the test (metres of camera depth, <see cref="Persp.DepthOf"/>); 0 or less: never hidden
        /// (stored as <see cref="NoDepth"/> in the renderer's property block, which stays on it: it is never dropped, so the
        /// shader never falls back to the sprite's world z).
        /// </summary>
        public static void SetDepth(Renderer r, float depth)
        {
            if (r == null) return;
            mpb ??= new MaterialPropertyBlock();
            mpb.Clear();   // (the depth is the only per-renderer value set on these renderers)
            mpb.SetFloat(DepthId, depth > 0f ? depth : NoDepth);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>The property block's "no depth: never hidden" (the shader: below 0).</summary>
        public const float NoDepth = -1f;

        /// <summary>A line vertex at <paramref name="p2"/> (pixel scene) carrying its depth in z.</summary>
        public static Vector3 Point(Vector2 p2, float depth) => new Vector3(p2.x, p2.y, Mathf.Max(0f, depth) * ZPerMetre);

        // ------------------------------------------------------------------ the stage's map
        /// <summary>
        /// Binds the stage's map for its front layer, one map for every period's front (<paramref name="fronts"/>: the map
        /// lines up with each of them pixel for pixel, so a period cross-fade never needs another bind). False (nothing
        /// hidden) without the map, its json or the shader, or when any period's front differs in size from the map.
        /// </summary>
        public static bool Bind(Object by, string stageId, Sprite[] fronts)
        {
            owner = by;
            StageId = null;
            tex = null;
            texels = null;
            var t = Resources.Load<Texture2D>("Sprites/Stages/" + stageId + "_front_depth");
            var j = Resources.Load<TextAsset>("Data/frontdepth_" + stageId);
            if (t == null || j == null || Material == null)
            {
                Debug.Log($"[OCC] {stageId}: no front depth map{(t == null ? "" : j == null ? " json" : " shader")}: nothing hidden");
                Push();
                return false;
            }
            var info = JsonUtility.FromJson<Info>(j.text);
            int fw = 0, fh = 0;
            bool fits = fronts != null && fronts.Length > 0;
            if (fits)
                foreach (var front in fronts)
                {
                    fw = front != null ? Mathf.RoundToInt(front.rect.width) : 0;
                    fh = front != null ? Mathf.RoundToInt(front.rect.height) : 0;
                    if (t.width != fw || t.height != fh) { fits = false; break; }
                }
            if (info == null || info.far <= info.near || t.width != info.widthPx || t.height != info.heightPx || !fits)
            {
                Debug.LogWarning($"[OCC] {stageId}: depth map {t.width}x{t.height} does not fit the front layer {fw}x{fh}: nothing hidden");
                Push();
                return false;
            }
            tex = t;
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            near = info.near;
            step = (info.far - info.near) / 65535f;
            StageId = stageId;
            Shader.SetGlobalTexture(TexId, tex);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[OCC] {0}: front depth map {1}x{2}, {3:0.0}-{4:0.0} m, bias {5:0.00} m{6}",
                stageId, tex.width, tex.height, info.near, info.far, Bias, enabled ? "" : " (off)"));
            Push();
            return true;
        }

        public static void Unbind(Object by)
        {
            if (owner != by) return;
            owner = null;
            StageId = null;
            tex = null;
            texels = null;
            Push();
        }

        /// <summary>Where the front layer is drawn this frame (world: its lower left corner and size; the deck's bob moves it).</summary>
        public static void Place(Object by, Vector2 min, Vector2 size)
        {
            if (by != owner || (min == rectMin && size == rectSize)) return;
            rectMin = min;
            rectSize = new Vector2(Mathf.Max(1e-4f, size.x), Mathf.Max(1e-4f, size.y));
            Push();
        }

        static void Push()
        {
            Shader.SetGlobalVector(RectId, new Vector4(rectMin.x, rectMin.y, 1f / rectSize.x, 1f / rectSize.y));
            Shader.SetGlobalVector(DecId, new Vector4(near, step, Bias, tex != null && enabled ? 1f : 0f));
            Shader.SetGlobalFloat(ZId, 1f / ZPerMetre);
        }

        // ------------------------------------------------------------------ the same test on the CPU (the tests)
        /// <summary>
        /// The front layer's occluder under the world point (pixel scene): its depth, or +inf where nothing solid is drawn
        /// (no map, outside it, an empty texel or a floating pad). <paramref name="texel"/>: its column / row (row 0 = bottom).
        /// </summary>
        public static float OccluderAt(Vector2 world, out Vector2Int texel) => OccluderIn(world, rectMin, rectSize, out texel);

        /// <summary>Where the map is placed for the shader now (world: lower left corner and size).</summary>
        internal static Rect PlacedRect => new Rect(rectMin, rectSize);

        /// <summary>
        /// <see cref="OccluderAt"/> with the map placed at <paramref name="min"/> / <paramref name="size"/> (world) instead of
        /// where it was last placed (the occlusion watch: the front layer's rect as it is actually drawn).
        /// </summary>
        internal static float OccluderIn(Vector2 world, Vector2 min, Vector2 size, out Vector2Int texel)
        {
            texel = new Vector2Int(-1, -1);
            if (tex == null) return float.PositiveInfinity;
            var uv = new Vector2((world.x - min.x) / size.x, (world.y - min.y) / size.y);
            if (uv.x < 0f || uv.y < 0f || uv.x >= 1f || uv.y >= 1f) return float.PositiveInfinity;
            int c = Mathf.FloorToInt(uv.x * tex.width), r = Mathf.FloorToInt(uv.y * tex.height);
            texel = new Vector2Int(c, r);
            if (texels == null)
            {
                if (!tex.isReadable) return float.PositiveInfinity;
                texels = tex.GetPixels32();
            }
            var t = texels[r * tex.width + c];
            if (t.a < 128 || t.b >= 128) return float.PositiveInfinity;
            return near + (t.r * 256 + t.g) * step;
        }

        /// <summary>True where the shader hides a fragment of this depth at this world point (hiding on).</summary>
        public static bool Hidden(Vector2 world, float depth) => enabled && depth > 0f && depth > OccluderAt(world, out _) + Bias;

        /// <summary>
        /// True when some solid front texel inside the world rect (<paramref name="min"/>..<paramref name="max"/>, pixel
        /// scene) is nearer than a fragment of <paramref name="depth"/> may be: the shader hides part of a sprite of that
        /// depth drawn there. False without a (readable) map.
        /// </summary>
        public static bool AnyNearer(Vector2 min, Vector2 max, float depth)
        {
            if (tex == null || depth <= 0f) return false;
            if (texels == null)
            {
                if (!tex.isReadable) return false;
                texels = tex.GetPixels32();
            }
            int W = tex.width, H = tex.height;
            int c0 = Mathf.FloorToInt((min.x - rectMin.x) / rectSize.x * W), c1 = Mathf.FloorToInt((max.x - rectMin.x) / rectSize.x * W);
            int r0 = Mathf.FloorToInt((min.y - rectMin.y) / rectSize.y * H), r1 = Mathf.FloorToInt((max.y - rectMin.y) / rectSize.y * H);
            if (c1 < 0 || r1 < 0 || c0 >= W || r0 >= H) return false;
            c0 = Mathf.Max(0, c0); r0 = Mathf.Max(0, r0); c1 = Mathf.Min(W - 1, c1); r1 = Mathf.Min(H - 1, r1);
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                var t = texels[r * W + c];
                if (t.a >= 128 && t.b < 128 && depth > near + (t.r * 256 + t.g) * step + Bias) return true;
            }
            return false;
        }
    }
}
