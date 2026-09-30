using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// A real-time 3D layer inside the 2D pixel scene. A perspective camera identical to the stage camera
    /// (<see cref="Persp"/>: same position and pitch, focal length focalPx in render-target pixels, the principal point
    /// on the 2D world origin as the pixel view's camera sees it, shake / centre offsets included, through an
    /// off-centre projection) renders one Unity layer into its own transparent render texture of exactly the pixel
    /// view's size (point filtered, no MSAA). A quad shows that texture 1:1 over the pixel view at a sorting order,
    /// so a 3D actor is one layer in the 2D sort order and lands on the same pixel grid as the sprites.
    /// The quad adds the stage's sun rim (ActorRim.shader, <see cref="ActorArt.RimFor"/>): a 1 px lit edge on the outer
    /// silhouette's sun side just inside the outline, fading down the figure over <see cref="RimSpan"/> like the sprites.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after PixelView (camera shake / snap) and Angler (posing) in LateUpdate
    public class ActorLayer : MonoBehaviour
    {
        public const int AnglerLayer = 29, ReelLayer = 30;

        /// <summary>Whole-pixel 2D world offset of the image (the ocean deck bob).</summary>
        public Vector2 Offset;

        /// <summary>
        /// Game-space bottom and top of the figure (the feet, the hat) the rim fades over, bright at the top and
        /// fading towards the feet; null = no fade (the reel).
        /// </summary>
        public (Vector3 bottom, Vector3 top)? RimSpan;

        public int Layer { get; private set; }
        public Camera Cam { get; private set; }
        public RenderTexture Target { get; private set; }

        public int SortingOrder
        {
            get => quad.sortingOrder;
            set => quad.sortingOrder = value;
        }

        StageLayout L;
        Persp P;
        MeshRenderer quad;
        MeshFilter quadMesh;
        Material mat;
        bool hasRim;
        static readonly int RimSpanId = Shader.PropertyToID("_RimSpan");
        const float Near = 0.3f, Far = 250f;

        public static ActorLayer Create(string name, int layer, int order, StageView stage, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<ActorLayer>();
            a.Init(layer, order, stage);
            return a;
        }

        void Init(int layer, int order, StageView stage)
        {
            Layer = layer;
            L = stage.L;
            P = stage.P;
            var pv = PixelView.Current;
            if (pv != null) pv.WorldCamera.cullingMask &= ~(1 << layer);

            var cgo = new GameObject(name + "Cam");
            cgo.transform.SetParent(transform, false);
            Cam = cgo.AddComponent<Camera>();
            Cam.enabled = false; // rendered by hand in LateUpdate
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0, 0, 0, 0);
            Cam.cullingMask = 1 << layer;
            Cam.orthographic = false;
            Cam.nearClipPlane = Near;
            Cam.farClipPlane = Far;
            Cam.allowMSAA = false;
            Cam.allowHDR = false;
            Cam.allowDynamicResolution = false;
            Cam.useOcclusionCulling = false;
            Cam.renderingPath = RenderingPath.Forward;
            Cam.depthTextureMode = DepthTextureMode.None;

            var qgo = new GameObject(name + "Quad");
            qgo.transform.SetParent(transform, false);
            quadMesh = qgo.AddComponent<MeshFilter>();
            quad = qgo.AddComponent<MeshRenderer>();
            // the rim material shows the layer with the stage's sun rim; without it the plain sprite material (no rim)
            mat = ActorArt.RimMaterial(stage.Def.id) ?? new Material(Angler.LineMaterial);
            mat.name = name + "Mat";
            hasRim = mat.HasProperty(RimSpanId);
            // (the rim, the tint and the key light follow the stage's period look: ActorArt.ApplyLook)
            if (hasRim) ActorArt.Track(mat);
            quad.sharedMaterial = mat;
            quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.receiveShadows = false;
            quad.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            quad.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            quad.sortingOrder = order;
            quad.enabled = false;
        }

        void EnsureTarget(int w, int h)
        {
            if (Target != null && Target.width == w && Target.height == h) return;
            Release();
            Target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                name = name + "RT",
            };
            Target.Create();
            Cam.targetTexture = Target;
            mat.mainTexture = Target;
            // a quad exactly the size of the pixel view (w x h pixels at 16 px per unit), centred on its camera
            float hw = w * 0.5f / PixelView.PPU, hh = h * 0.5f / PixelView.PPU;
            var m = quadMesh.sharedMesh != null ? quadMesh.sharedMesh : new Mesh { name = name + "Quad" };
            m.Clear();
            m.vertices = new[] { new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(-hw, hh, 0), new Vector3(hw, hh, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(hw * 2f, hh * 2f, 1f));
            quadMesh.sharedMesh = m;
        }

        /// <summary>
        /// Off-centre OpenGL-style projection: a view-space point (x, y, -d) lands on render-target pixel
        /// (w/2 + f x/d - cx*16, h/2 + f y/d - cy*16), i.e. exactly where the 2D camera at (cx, cy) shows Persp.To2D.
        /// </summary>
        Matrix4x4 Projection(int w, int h, Vector2 cam2D)
        {
            float f = L.focalPx, ppu = PixelView.PPU;
            var m = Matrix4x4.zero;
            m[0, 0] = 2f * f / w;
            m[0, 2] = 2f * cam2D.x * ppu / w;
            m[1, 1] = 2f * f / h;
            m[1, 2] = 2f * cam2D.y * ppu / h;
            m[2, 2] = -(Far + Near) / (Far - Near);
            m[2, 3] = -2f * Far * Near / (Far - Near);
            m[3, 2] = -1f;
            return m;
        }

        void LateUpdate()
        {
            var pv = PixelView.Current;
            if (pv == null || pv.Target == null) { quad.enabled = false; return; }
            int w = pv.Target.width, h = pv.Target.height;
            EnsureTarget(w, h);
            pv.WorldCamera.cullingMask &= ~(1 << Layer); // the 2D camera never draws the 3D actors themselves
            var wc = pv.WorldCamera.transform.position;
            Cam.transform.SetPositionAndRotation(P.CameraPos, Quaternion.Euler(L.pitch, 0f, 0f));
            Cam.projectionMatrix = Projection(w, h, new Vector2(wc.x, wc.y) - Offset);
            quad.transform.position = new Vector3(wc.x, wc.y, 0f);
            if (hasRim)
            {
                // the fade span as uv.y on the render target (a game point lands at To2D - camera + offset)
                float V(Vector3 p) => ((P.To2D(p).y - wc.y + Offset.y) * PixelView.PPU + h * 0.5f) / h;
                mat.SetVector(RimSpanId, RimSpan.HasValue ? new Vector4(V(RimSpan.Value.top), V(RimSpan.Value.bottom), 0, 0) : Vector4.zero);
            }
            Cam.Render();
            quad.enabled = true;
        }

        void Release()
        {
            if (Target == null) return;
            if (Cam != null) Cam.targetTexture = null;
            Target.Release();
            Destroy(Target);
            Target = null;
        }

        void OnDestroy()
        {
            Release();
            if (mat != null) Destroy(mat);
            if (quadMesh != null && quadMesh.sharedMesh != null) Destroy(quadMesh.sharedMesh);
        }
    }
}
