using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Stage layout exported by Tools/Blender/fk_stages.py. Game space is metres:
    /// x right, y up (water surface = 0), z forward from the angler's feet.
    /// </summary>
    [Serializable]
    public class StageLayout
    {
        public string id;
        public float standH;                  // feet height above the water
        public int widthPx, heightPx, ppu;
        public float camBack, camUp, pitch, focalPx;
        public float[] depthZ, depthV;        // water depth as a function of distance
        public string mode;                   // shore | boat | ice
        public float zNear, zFar, xLim;       // fishable water
        public string waterTint, waterDeep;
        public string ambient;                // day | mist | snow | cave
        public bool clouds, birds, fireflies, snow, sparkles;
        public float holeX, holeZ, holeR, iceY;

        public bool IsIce => mode == "ice";

        /// <summary>
        /// The tide's change of the water depth (m, + deeper): the sea's 0.4 x the tide level (StageView sets it on the
        /// sea, 0 on every other stage; Docs/time_currents_spec.md 7.3).
        /// </summary>
        public static float TideOffset;

        public float DepthAt(float z) => BaseDepthAt(z) + TideOffset;

        float BaseDepthAt(float z)
        {
            if (depthZ == null || depthZ.Length == 0) return 6f;
            if (z <= depthZ[0]) return depthV[0];
            for (int i = 1; i < depthZ.Length; i++)
            {
                if (z <= depthZ[i])
                    return Mathf.Lerp(depthV[i - 1], depthV[i], Mathf.InverseLerp(depthZ[i - 1], depthZ[i], z));
            }
            return depthV[depthV.Length - 1];
        }

        public bool InWater(float x, float z) => z >= zNear && z <= zFar && Mathf.Abs(x) <= xLim;
    }

    [Serializable]
    public class PoseAnchor
    {
        public string name;
        public float handX, handY;   // sprite pixels (origin bottom-left of the 96x112 frame)
        public float hx, hy, hz;     // rod hand, metres relative to the feet (x right, y up, z forward)
        public float rx, ry, rz;     // rod direction

        public Vector3 Hand => new Vector3(hx, hy, hz);
        public Vector3 RodDir => new Vector3(rx, ry, rz).normalized;
    }

    [Serializable]
    public class CharacterData
    {
        public int cropW, cropH, feetPx;
        public PoseAnchor[] poses;

        public PoseAnchor Get(string pose)
        {
            foreach (var p in poses) if (p.name == pose) return p;
            return poses[0];
        }
    }

    [Serializable]
    public class MapMarker
    {
        public string id;
        public float x, y;
    }

    [Serializable]
    public class MapData
    {
        public int widthPx, heightPx, ppu;
        public MapMarker[] markers;
    }

    [Serializable]
    public class AquariumData
    {
        public float swimMinX, swimMaxX, swimMinY, swimMaxY, surfaceY;
        public string waterTop, waterDeep;
    }

    /// <summary>Cached access to the Blender-rendered sprites and exported layout data in Resources.</summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, object> json = new Dictionary<string, object>();

        public static Sprite Get(string path)
        {
            if (cache.TryGetValue(path, out var s)) return s;
            s = Resources.Load<Sprite>("Sprites/" + path);
            if (s == null) Debug.LogWarning("[Art] missing sprite: " + path);
            cache[path] = s;
            return s;
        }

        public static Sprite Fish(string id, int frame = 0) => Get($"Fish/{id}_{frame}");
        public static Sprite Item(string id) => Get(id.StartsWith("tank_") ? $"Items/tank_{Mathf.Max(1, int.Parse(id.Substring(5)))}" : $"Items/{id}");
        public static Sprite WorldBait(string id) => Get($"World/{id}_w");
        public static Sprite UI(string name) => Get($"UI/{name}");
        public static Sprite Stage(string name) => Get($"Stages/{name}");
        public static Sprite Pose(string pose) => Get($"Character/angler_{pose}");

        public static T Data<T>(string name) where T : class
        {
            if (json.TryGetValue(name, out var o)) return (T)o;
            var ta = Resources.Load<TextAsset>("Data/" + name);
            T v = ta != null ? JsonUtility.FromJson<T>(ta.text) : null;
            if (v == null) Debug.LogError("[Art] missing data: " + name);
            json[name] = v;
            return v;
        }

        public static StageLayout Layout(string stageId) => Data<StageLayout>("stage_" + stageId);
        public static CharacterData Character => Data<CharacterData>("character");
        public static MapData Map => Data<MapData>("map");
        public static AquariumData Aquarium => Data<AquariumData>("aquarium");

        public static Color Hex(string hex, float a = 1f)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c))
            {
                c.a = a;
                return c;
            }
            return new Color(1, 0, 1, a);
        }

        // ---- tiny procedural sprites (white, tinted at runtime)
        static Sprite pixel, circle, softCircle, ring, arrowHead;

        /// <summary>Pixel arrow "→" (9x7 px: a 1 px shaft and a 4 px deep head), for the reel direction arrows.</summary>
        public static Sprite Arrow
        {
            get
            {
                if (arrowHead != null) return arrowHead;
                const int w = 9, h = 7;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int dy = Mathf.Abs(y - 3);
                    bool head = x >= 5 && x <= 8 - dy;  // head: columns 5..8, narrowing to the tip
                    bool shaft = y == 3 && x < 5;
                    t.SetPixel(x, y, head || shaft ? Color.white : Color.clear);
                }
                t.Apply();
                return arrowHead = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16);
            }
        }

        /// <summary>1px-thick ring (64px), for the reel gesture indicator.</summary>
        public static Sprite Ring
        {
            get
            {
                if (ring != null) return ring;
                const int d = 64;
                var t = new Texture2D(d, d, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dx = x + 0.5f - d / 2f, dy = y + 0.5f - d / 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = r > d / 2f - 3f && r < d / 2f - 0.5f ? 1f : 0f;
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                t.Apply();
                return ring = Sprite.Create(t, new Rect(0, 0, d, d), new Vector2(0.5f, 0.5f), 16);
            }
        }

        public static Sprite Pixel
        {
            get
            {
                if (pixel != null) return pixel;
                var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                t.SetPixel(0, 0, Color.white);
                t.Apply();
                return pixel = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 16);
            }
        }

        /// <summary>Hard-edged pixel circle (diameter in px).</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle != null) return circle;
                const int d = 5;
                var t = new Texture2D(d, d, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dx = x - 2, dy = y - 2;
                    bool on = dx * dx + dy * dy <= 4.5f;
                    t.SetPixel(x, y, on ? Color.white : Color.clear);
                }
                t.Apply();
                return circle = Sprite.Create(t, new Rect(0, 0, d, d), new Vector2(0.5f, 0.5f), 16);
            }
        }

        public static Sprite SoftCircle
        {
            get
            {
                if (softCircle != null) return softCircle;
                const int d = 32;
                var t = new Texture2D(d, d, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dx = (x + 0.5f - d / 2f) / (d / 2f), dy = (y + 0.5f - d / 2f) / (d / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    t.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
                t.Apply();
                return softCircle = Sprite.Create(t, new Rect(0, 0, d, d), new Vector2(0.5f, 0.5f), 32);
            }
        }

        /// <summary>Vertical gradient sprite (top colour -> bottom colour), banded like the backgrounds.</summary>
        public static Sprite Gradient(Color top, Color bottom, int height = 64, int bands = 8)
        {
            var t = new Texture2D(1, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
            {
                float f = (float)y / (height - 1);
                f = Mathf.Floor(f * bands) / (bands - 1);
                t.SetPixel(0, y, Color.Lerp(bottom, top, Mathf.Clamp01(f)));
            }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 1, height), new Vector2(0.5f, 1f), 1);
        }
    }
}
