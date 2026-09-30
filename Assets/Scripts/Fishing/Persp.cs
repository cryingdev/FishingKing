using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The behind-the-angler pinhole camera used to render the stage backgrounds in Blender
    /// (Tools/Blender/fk_persp.py). Game logic lives in metres (x right, y up with the water
    /// surface at 0, z forward from the feet); this maps those points onto the 2D pixel scene
    /// (16 px per Unity unit, background image centred on the origin).
    /// </summary>
    public class Persp
    {
        const float WaterIndex = 1.333f;

        public readonly float StandH;
        readonly float f, sin, cos, feetPpm;
        readonly Vector3 cam;
        const float PPU = PixelView.PPU;

        public Persp(StageLayout l)
        {
            StandH = l.standH;
            f = l.focalPx;
            float a = l.pitch * Mathf.Deg2Rad;
            sin = Mathf.Sin(a);
            cos = Mathf.Cos(a);
            cam = new Vector3(0, l.standH + l.camUp, -l.camBack);
            feetPpm = PixelsPerMetre(Feet);
        }

        public Vector3 Feet => new Vector3(0, StandH, 0);
        public Vector3 CameraPos => cam;
        /// <summary>The horizon's height in the 2D pixel scene (where the water plane vanishes).</summary>
        public float HorizonY => f * sin / cos / PPU;

        /// <summary>Pixel offset from the image centre plus camera-space depth.</summary>
        public Vector2 ToPixel(Vector3 p, out float depth)
        {
            float rx = p.x - cam.x, ry = p.y - cam.y, rz = p.z - cam.z;
            depth = Mathf.Max(0.05f, rz * cos - ry * sin);
            float yc = ry * cos + rz * sin;
            return new Vector2(f * rx / depth, f * yc / depth);
        }

        /// <summary>World-space position of a game point in the 2D pixel scene.</summary>
        public Vector2 To2D(Vector3 p) => ToPixel(p, out _) / PPU;

        public Vector2 To2D(Vector3 p, out float depth) => ToPixel(p, out depth) / PPU;

        /// <summary>Camera depth of a game point (metres along the view axis; what the front layer's depth map holds).</summary>
        public float DepthOf(Vector3 p)
        {
            ToPixel(p, out float d);
            return d;
        }

        /// <summary>Screen pixels per metre at a point (for scaling sprites).</summary>
        public float PixelsPerMetre(Vector3 p)
        {
            ToPixel(p, out float d);
            return f / d;
        }

        /// <summary>
        /// Perspective scale relative to the angler's feet: props (float, bait) are drawn at their native pixel
        /// size where the angler stands and shrink with distance like the painted stage.
        /// </summary>
        public float ScaleAt(Vector3 p) => PixelsPerMetre(p) / feetPpm;

        /// <summary><see cref="ScaleAt(Vector3)"/>, but never smaller than <paramref name="minPx"/> pixels for a sprite <paramref name="spritePx"/> long.</summary>
        public float ScaleAt(Vector3 p, float spritePx, float minPx) => Mathf.Max(ScaleAt(p), minPx / Mathf.Max(1f, spritePx));

        /// <summary>
        /// Where an underwater point appears from the camera: refraction at the flat surface lifts it straight up,
        /// the more the flatter the view (apparent depth = depth * tan(refracted angle) / tan(incident angle)).
        /// </summary>
        public Vector3 Apparent(Vector3 p)
        {
            if (p.y >= 0) return p;
            float ti = new Vector2(p.x - cam.x, p.z - cam.z).magnitude / cam.y; // tan of the incidence angle (from the vertical)
            float st = ti / Mathf.Sqrt(1 + ti * ti) / WaterIndex;                 // sin of the refracted angle
            float tt = st / Mathf.Sqrt(1 - st * st);
            return new Vector3(p.x, p.y * tt / Mathf.Max(1e-4f, ti), p.z);
        }

        /// <summary>
        /// Vertical squash for a fish shadow lying flat under the water: flatter with distance like the true
        /// foreshortening, but held back and floored so a far fish never collapses into a line or a dot.
        /// </summary>
        public float ShadowSquash(Vector3 p) => Mathf.Clamp(Foreshorten(p) * 1.3f + 0.1f, 0.3f, 0.8f);

        /// <summary>How squashed a flat object lying on the water looks at this point (0..1).</summary>
        public float Foreshorten(Vector3 p)
        {
            var a = ToPixel(p, out _);
            var bx = ToPixel(p + new Vector3(0.1f, 0, 0), out _);
            var bz = ToPixel(p + new Vector3(0, 0, 0.1f), out _);
            float lx = (bx - a).magnitude, lz = (bz - a).magnitude;
            return lx > 1e-4f ? Mathf.Clamp01(lz / lx) : 1f;
        }

        /// <summary>Casts a ray through a 2D scene point onto the horizontal plane y = planeY.</summary>
        public bool ToPlane(Vector2 scene2D, float planeY, out Vector3 hit)
        {
            float px = scene2D.x * PPU / f, py = scene2D.y * PPU / f;
            // direction = right*px + up*py + forward
            var dir = new Vector3(px, cos * py - sin, sin * py + cos);
            hit = default;
            if (Mathf.Abs(dir.y) < 1e-5f) return false;
            float t = (planeY - cam.y) / dir.y;
            if (t <= 0) return false;
            hit = cam + dir * t;
            return true;
        }

        /// <summary>Half width (metres) of the visible water at distance z (for a view width in px).</summary>
        public float VisibleHalfWidth(float z, float viewWidthPx)
        {
            ToPixel(new Vector3(0, 0, z), out float d);
            return viewWidthPx * 0.5f * d / f;
        }
    }
}
