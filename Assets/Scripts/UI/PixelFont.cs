using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// Galmuri pixel fonts (SIL OFL 1.1, © Lee Minseo — see Resources/Fonts/Galmuri-LICENSE.txt).
    /// Each face is drawn on a fixed pixel grid (Galmuri7 = 8px, 9 = 10px, 11 = 12px, 14 = 15px).
    /// Text stays hard-edged only when it is rasterised at a whole multiple of that grid, so a requested
    /// size is snapped to 2·grid·k canvas units: with the integer UI scale (<see cref="UIKit.ScaleFactor"/>
    /// = artScale / 2) that always lands on grid·k·artScale screen pixels.
    /// </summary>
    public static class PixelFonts
    {
        public struct Pick
        {
            public Font font;
            public int size;    // canvas units
            public int gridPx;  // design pixel size of the face (0 = not a pixel font)
        }

        struct Face
        {
            public string name;
            public int grid;
            public Font font;
        }

        static Face[] faces;
        static Font custom, os;
        static bool loaded;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            custom = Resources.Load<Font>("Fonts/UIFont"); // optional override: any font dropped here wins
            var list = new List<Face>();
            foreach (var (name, grid) in new[] { ("Galmuri7", 8), ("Galmuri9", 10), ("Galmuri11", 12), ("Galmuri14", 15) })
            {
                var f = Resources.Load<Font>("Fonts/" + name);
                if (f != null) list.Add(new Face { name = name, grid = grid, font = f });
            }
            faces = list.ToArray();
            if (faces.Length > 0) Font.textureRebuilt += OnTextureRebuilt;
            if (custom == null && faces.Length == 0)
            {
                os = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic",
                    "Droid Sans Fallback", "Arial Unicode MS", "Arial",
                }, 32);
                if (os == null) os = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        public static bool IsPixel
        {
            get
            {
                Load();
                return custom == null && faces.Length > 0;
            }
        }

        /// <summary>Chooses the face and snapped size closest to the requested canvas size.</summary>
        public static Pick For(int requested)
        {
            Load();
            if (custom != null) return new Pick { font = custom, size = requested };
            if (faces.Length == 0) return new Pick { font = os, size = requested };
            Pick best = default;
            float bestCost = float.MaxValue;
            foreach (var f in faces)
            {
                for (int k = 1; k <= 2; k++)
                {
                    int size = 2 * f.grid * k;
                    // closeness first; doubled small faces only when nothing native fits; ties go to the larger face
                    float cost = Mathf.Abs(size - requested) + (k - 1) * 3f - f.grid * 0.01f;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = new Pick { font = f.font, size = size, gridPx = f.grid };
                    }
                }
            }
            PointFilter(best.font);
            return best;
        }

        static void OnTextureRebuilt(Font f)
        {
            foreach (var face in faces)
                if (face.font == f) PointFilter(f);
        }

        static void PointFilter(Font f)
        {
            if (f != null && f.material != null && f.material.mainTexture != null)
                f.material.mainTexture.filterMode = FilterMode.Point;
        }
    }

    /// <summary>
    /// UI text that keeps rich text pixel-safe: &lt;size=N&gt; is snapped to the face's grid multiples and
    /// &lt;b&gt;/&lt;i&gt; are dropped (a synthesised bold/italic smears pixel glyphs).
    /// </summary>
    public class PixelText : Text
    {
        static readonly Regex SizeTag = new Regex(@"<size=(\d+)>", RegexOptions.Compiled);
        static readonly Regex StyleTag = new Regex(@"</?[bi]>", RegexOptions.Compiled);
        int grid;

        public void Setup(int gridPx) => grid = gridPx;

        public override string text
        {
            get => base.text;
            set => base.text = Sanitize(value);
        }

        string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s) || grid <= 0 || s.IndexOf('<') < 0) return s;
            s = StyleTag.Replace(s, "");
            int step = grid * 2;
            return SizeTag.Replace(s, m =>
            {
                int n = int.Parse(m.Groups[1].Value);
                int snapped = Mathf.Max(step, Mathf.RoundToInt((float)n / step) * step);
                return $"<size={snapped}>";
            });
        }
    }

    /// <summary>
    /// 1-pixel outline in all 8 directions (UGUI's Outline only offsets diagonally). The tiny Galmuri7 face has
    /// 1-px counters that a full outline would fill, so it gets a down-right drop shadow instead.
    /// </summary>
    public class PixelOutline : BaseMeshEffect
    {
        public Color color = new Color(0.08f, 0.05f, 0.1f, 0.95f);
        public float distance = UIKit.Px;
        public bool shadowOnly;

        static readonly List<UIVertex> src = new List<UIVertex>();
        static readonly List<UIVertex> dst = new List<UIVertex>();
        static readonly Vector2[] Dirs =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1),
        };
        static readonly Vector2[] ShadowDirs = { new Vector2(1, 0), new Vector2(0, -1), new Vector2(1, -1) };

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            src.Clear();
            vh.GetUIVertexStream(src);
            dst.Clear();
            foreach (var d in shadowOnly ? ShadowDirs : Dirs)
            {
                var off = new Vector3(d.x * distance, d.y * distance, 0);
                for (int i = 0; i < src.Count; i++)
                {
                    var v = src[i];
                    v.position += off;
                    var c = color;
                    c.a *= v.color.a / 255f;
                    v.color = c;
                    dst.Add(v);
                }
            }
            dst.AddRange(src);
            vh.Clear();
            vh.AddUIVertexTriangleStream(dst);
        }
    }

    /// <summary>Keeps a canvas at a whole-number art-pixel scale (see <see cref="UIKit.ScaleFactor"/>).</summary>
    [RequireComponent(typeof(CanvasScaler))]
    public class PixelCanvasScaler : MonoBehaviour
    {
        CanvasScaler cs;
        int w = -1, h = -1;

        void Awake()
        {
            cs = GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            Apply();
        }

        void Update()
        {
            if (Screen.width != w || Screen.height != h) Apply();
        }

        void Apply()
        {
            w = Screen.width;
            h = Screen.height;
            cs.scaleFactor = UIKit.ScaleFactor;
        }
    }
}
