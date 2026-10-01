using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// Renders the world camera into a low-resolution render texture (16 px per unit, ~480x270)
    /// and shows it full screen with point filtering, so every sprite, line and rotation snaps
    /// to the same chunky pixel grid. UI canvases are drawn on top at native resolution.
    /// The display can show a crop of the target (<see cref="Zoom"/>: the fishing zoom); the screen / world mappings
    /// below go through it.
    /// </summary>
    public class PixelView : MonoBehaviour
    {
        public const int PPU = 16;
        public const int BaseHeight = 270;
        public const int BaseWidth = 480;

        public Camera WorldCamera { get; private set; }
        public RenderTexture Target { get; private set; }
        public static PixelView Current { get; private set; }
        /// <summary>The display's zoom / pan (a crop of <see cref="Target"/>; 1x unless a director zooms it).</summary>
        public ViewZoom Zoom { get; private set; }

        RawImage display;
        Canvas canvas;
        int lastW, lastH;
        Vector2 shake;
        float shakeTime, shakeAmp;
        Vector3 basePos;

        public static PixelView Create(Color clear)
        {
            var go = new GameObject("PixelView");
            var pv = go.AddComponent<PixelView>();
            pv.Init(clear);
            return pv;
        }

        void Init(Color clear)
        {
            Current = this;
            var cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cgo.AddComponent<Camera>();
            }
            WorldCamera = cam;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.transform.position = new Vector3(0, 0, -10);
            basePos = cam.transform.position;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            // A second camera clears the back buffer (the world camera renders into the RT only).
            var clearCam = new GameObject("ScreenClear").AddComponent<Camera>();
            clearCam.transform.SetParent(transform);
            clearCam.cullingMask = 0;
            clearCam.clearFlags = CameraClearFlags.SolidColor;
            clearCam.backgroundColor = Color.black;
            clearCam.depth = -100;

            var cgo2 = new GameObject("PixelCanvas");
            cgo2.transform.SetParent(transform);
            canvas = cgo2.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1000;
            var rgo = new GameObject("Display");
            rgo.transform.SetParent(cgo2.transform, false);
            display = rgo.AddComponent<RawImage>();
            display.raycastTarget = false;
            var rt = display.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Rebuild();
            Zoom = gameObject.AddComponent<ViewZoom>();
            Zoom.Init(this);
        }

        int baseH = BaseHeight;

        /// <summary>
        /// A scene's own view height (px at 16:9; the aquarium's bigger tanks): the width and the aspect caps scale with
        /// it. The default is <see cref="BaseHeight"/>.
        /// </summary>
        public void SetBaseHeight(int h)
        {
            h = Mathf.Max(BaseHeight, h);
            if (h == baseH) return;
            baseH = h;
            Rebuild();
        }

        void Rebuild()
        {
            lastW = Screen.width;
            lastH = Screen.height;
            float aspect = Mathf.Max(0.5f, (float)lastW / Mathf.Max(1, lastH));
            float k = baseH / (float)BaseHeight;
            int h, w;
            if (aspect >= 16f / 9f)
            {
                h = baseH;
                w = Mathf.Min(Mathf.RoundToInt(640 * k), Mathf.RoundToInt(h * aspect));
            }
            else
            {
                w = Mathf.RoundToInt(BaseWidth * k);
                h = Mathf.Min(Mathf.RoundToInt(400 * k), Mathf.RoundToInt(w / aspect));
            }
            if (Target != null)
            {
                WorldCamera.targetTexture = null;
                Target.Release();
                Destroy(Target);
            }
            Target = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "PixelRT" };
            Target.Create();
            WorldCamera.targetTexture = Target;
            WorldCamera.orthographicSize = h / 2f / PPU;
            display.texture = Target;
        }

        void LateUpdate()
        {
            if (Screen.width != lastW || Screen.height != lastH) Rebuild();
            if (shakeTime > 0)
            {
                shakeTime -= Time.unscaledDeltaTime;
                float a = shakeAmp * Mathf.Clamp01(shakeTime * 4f);
                shake = new Vector2(Random.Range(-a, a), Random.Range(-a, a));
            }
            else shake = Vector2.zero;
            PlaceCamera();
        }

        /// <summary>The camera at its centre, the shake and the pan, snapped to whole pixels so the scene never shimmers.</summary>
        void PlaceCamera()
        {
            Vector3 p = basePos + (Vector3)shake + new Vector3(pan.x, pan.y, 0f) / PPU;
            p.x = Mathf.Round(p.x * PPU) / PPU;
            p.y = Mathf.Round(p.y * PPU) / PPU;
            WorldCamera.transform.position = p;
        }

        public void SetCenter(Vector2 c) => basePos = new Vector3(c.x, c.y, -10);

        /// <summary>The camera's centre without the shake or the pan (the home view).</summary>
        public Vector2 BaseCenter => basePos;

        Vector2Int pan;

        /// <summary>
        /// The camera's offset from its centre in whole game px (<see cref="ViewZoom"/>: the fishing view panned over the
        /// stage art beyond the home view, following a fish or a rig that went past it; zero everywhere else).
        /// </summary>
        public Vector2Int Pan => pan;

        /// <summary>Moves the camera to this pan now (set by <see cref="ViewZoom"/> with the crop it shows this frame).</summary>
        internal void SetPan(Vector2Int p)
        {
            if (p == pan) return;
            pan = p;
            PlaceCamera();
        }

        /// <summary>The part of the target the display shows (set by <see cref="ViewZoom"/>).</summary>
        internal void SetDisplayUV(Rect r)
        {
            if (display != null && display.uvRect != r) display.uvRect = r;
        }

        Rect ShownUV => Zoom != null ? Zoom.UV : new Rect(0f, 0f, 1f, 1f);

        public void Shake(float amp = 0.25f, float time = 0.3f)
        {
            shakeAmp = amp;
            shakeTime = time;
        }

        /// <summary>World-space size of the visible area.</summary>
        public Vector2 ViewSize => new Vector2(WorldCamera.orthographicSize * 2f * WorldCamera.aspect, WorldCamera.orthographicSize * 2f);

        /// <summary>A screen point -> the pixel-view world point shown there (through the zoom's crop).</summary>
        public Vector2 ScreenToWorld(Vector2 screen)
        {
            var uv = ShownUV;
            var vp = new Vector3(uv.x + screen.x / Screen.width * uv.width, uv.y + screen.y / Screen.height * uv.height, 10f);
            return WorldCamera.ViewportToWorldPoint(vp);
        }

        /// <summary>A pixel-view world point -> where it shows on screen (through the zoom's crop).</summary>
        public Vector2 WorldToScreen(Vector2 world)
        {
            var uv = ShownUV;
            var vp = WorldCamera.WorldToViewportPoint(world);
            return new Vector2((vp.x - uv.x) / uv.width * Screen.width, (vp.y - uv.y) / uv.height * Screen.height);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (Target != null)
            {
                if (WorldCamera != null) WorldCamera.targetTexture = null;
                Target.Release();
                Destroy(Target);
            }
        }
    }
}
