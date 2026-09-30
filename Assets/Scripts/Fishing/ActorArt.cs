using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The real-time 3D actors' assets (Resources/Models, built by Tools/Blender/variants/hybrid/hyb_actors3d.py):
    /// loads a model, puts it on an <see cref="ActorLayer"/> and swaps its FBX materials (matched by name) for toon
    /// materials (Assets/Shaders/ActorToon.shader) built from the palette json: a dark / mid / light ramp and a
    /// hue-shifted outline colour per material.
    /// </summary>
    public static class ActorArt
    {
        public struct Tone
        {
            public Color dark, mid, light, outline;
        }

        // the sprite's key light, in world space: from the right, lifted, slightly towards the camera
        public static readonly Vector3 KeyDir = new Vector3(0.752f, 0.647f, -0.125f);

        /// <summary>A stage's sun rim on the actor layers (ActorRim.shader): colour, strength and pixel directions towards the light.</summary>
        public struct Rim
        {
            public Color col;
            public float strength;          // s of hyb_core.rim_fn (preset strength x RimScale)
            public float edge;              // 0..1: how far the sun-side outline turns from dark ink to the lit body colour
            public Vector3[] dirs;          // (dx, dy, weight): a pixel step towards the light (y up) and its weight
        }

        // hyb_character.RIM_SCALE: the sprites use a subtle share of each preset's rim strength
        const float RimScale = 0.62f;
        // The sprites (lit by the lake preset) have no dark outline on the sun side (hyb_core.outer_outline, lit_steps 1):
        // the edge pixel is the rim pixel one step darker. Measured on the lake sprites: rim pixel -> edge pixel is about
        // -0.10 OKLab L (vest 9e9f65 -> 7a7e53, trousers 838286 -> 606877, hat ffcfa0 -> df9f74). Other stages take
        // the lit edge in proportion to their preset's rim strength (lake 0.55 = all of it), so the weak swamp rim keeps
        // most of the ink.
        const float RimEdgeDrop = 0.1f, LakeRim = 0.55f;
        static readonly Vector3 R = new Vector3(1, 0, 0), TR = new Vector3(1, 1, 0), T = new Vector3(0, 1, 0),
            TL = new Vector3(-1, 1, 0), L = new Vector3(-1, 0, 0);
        static Vector3 W(Vector3 d, float w) => new Vector3(d.x, d.y, w);

        /// <summary>
        /// The stage's rim, from Tools/Blender/variants/hybrid/hyb_core.py PRESETS[stage].rim (col, strength, dirs):
        /// lake warm from the right, stream / sea / ice / ocean from the left, swamp weak from the top, cave cyan from the top.
        /// </summary>
        public static Rim RimFor(string stageId)
        {
            Rim M(string hex, float strength, params Vector3[] dirs) => new Rim
            {
                col = Art.Hex(hex), strength = strength * RimScale, edge = Mathf.Clamp01(strength / LakeRim), dirs = dirs,
            };
            switch (stageId)
            {
                case "stream": return M("#fff2c8", 0.4f, W(L, 1f), W(TL, 0.8f), W(T, 0.5f));
                case "sea": return M("#fff4de", 0.3f, W(L, 1f), W(TL, 0.8f), W(T, 0.5f));
                case "swamp": return M("#d8c088", 0.2f, W(T, 1f), W(TL, 0.6f), W(TR, 0.6f));
                case "ice": return M("#ffc8d0", 0.3f, W(L, 1f), W(TL, 0.8f), W(T, 0.4f));
                case "ocean": return M("#ffd8b0", 0.5f, W(L, 1f), W(TL, 0.85f), W(T, 0.55f));
                case "cave": return M("#86dcff", 0.45f, W(T, 1f), W(TL, 0.8f), W(TR, 0.8f));
                default: return M("#ffcf8c", 0.55f, W(R, 1f), W(TR, 0.85f), W(T, 0.55f)); // lake
            }
        }

        static Material rimBase;
        static bool rimTried;

        // ---- the time of day (Docs/time_currents_spec.md 4): the stage's blended look drives the actor layers' rim, the
        // toon materials' key light and the actor tint (StageView.ApplyLook -> ApplyLook)
        static Rim? lookRim;
        static string lookStage;
        static Color lookTint = Color.white;
        static Vector3 lookKey = KeyDir;
        static readonly List<Material> tracked = new List<Material>();
        static readonly HashSet<Material> stageToon = new HashSet<Material>();
        static readonly int TintId = Shader.PropertyToID("_Tint"), KeyId = Shader.PropertyToID("_KeyDir");

        /// <summary>The rim of a period's look: its colour, preset strength (x <see cref="RimScale"/>) and directions.</summary>
        public static Rim RimOfLook(Color col, float strength, Vector3[] dirs) => new Rim
        {
            col = col, strength = strength * RimScale, edge = Mathf.Clamp01(strength / LakeRim),
            dirs = dirs != null && dirs.Length > 0 ? dirs : new[] { W(T, 1f) },
        };

        /// <summary>A stage actor layer's rim material (the angler, the reel): follows the period look from now on.</summary>
        public static void Track(Material m)
        {
            if (m == null || !m.HasProperty("_RimStrength")) return;
            tracked.RemoveAll(x => x == null);
            if (!tracked.Contains(m)) tracked.Add(m);
        }

        /// <summary>
        /// The stage's blended period look (StageView): the rim of the tracked actor layers (colour and strength follow the
        /// blend, the directions its dominant period), their tint (<c>_Tint</c>: body and outline x tint, sRGB) and the key
        /// light of the angler's and reels' toon materials.
        /// </summary>
        public static void ApplyLook(string stageId, LookNow now)
        {
            lookStage = stageId;
            lookRim = RimOfLook(now.RimCol, now.RimStrength, now.RimDirs);
            lookTint = now.ActorTint;
            lookKey = now.Key;
            tracked.RemoveAll(x => x == null);
            var rim = Tuned(lookRim.Value, out float drop, false);
            foreach (var m in tracked) SetRim(m, rim, drop);
            stageToon.RemoveWhere(x => x == null);
            foreach (var m in stageToon) m.SetVector(KeyId, lookKey.normalized);
        }

        static void SetRim(Material m, Rim rim, float drop)
        {
            m.SetColor("_RimColor", rim.col);
            m.SetFloat("_RimStrength", rim.strength);
            m.SetFloat("_RimEdge", rim.edge);
            m.SetFloat("_RimEdgeDrop", drop);
            for (int i = 0; i < 3; i++)
                m.SetVector("_RimDir" + i, i < rim.dirs.Length ? new Vector4(rim.dirs[i].x, rim.dirs[i].y, rim.dirs[i].z, 0) : Vector4.zero);
            if (m.HasProperty(TintId)) m.SetColor(TintId, lookTint);
        }

        /// <summary>-fkrimtune gain:edge:drop (test switch): rim strength x gain, the lit edge x edge, its L drop.</summary>
        static Rim Tuned(Rim rim, out float drop, bool log, string stageId = null)
        {
            drop = RimEdgeDrop;
            var args = System.Environment.GetCommandLineArgs();
            int ti = System.Array.IndexOf(args, "-fkrimtune");
            if (ti >= 0 && ti + 1 < args.Length)
            {
                var p = args[ti + 1].Split(':');
                float F(int i, float d) => i < p.Length && float.TryParse(p[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
                rim.strength *= F(0, 1f);
                rim.edge = Mathf.Clamp01(rim.edge * F(1, 1f));
                drop = F(2, drop);
                if (log) Debug.Log($"[ActorArt] rim tune {stageId}: s {rim.strength:0.000} edge {rim.edge:0.00} drop {drop:0.00}");
            }
            return rim;
        }

        /// <summary>
        /// A new rim material with its own rim (an encounter backdrop's, Docs/legends_rollout.md 4.1): colour, the preset's
        /// strength (x <see cref="RimScale"/> like the stages') and pixel directions towards the light; null without the shader.
        /// </summary>
        public static Material RimMaterial(string hex, float strength, Vector3[] dirs)
        {
            var m = RimMaterial("cave");
            if (m == null) return null;
            var rim = new Rim { col = Art.Hex(hex), strength = strength * RimScale, edge = Mathf.Clamp01(strength / LakeRim), dirs = dirs };
            m.SetColor("_RimColor", rim.col);
            m.SetFloat("_RimStrength", rim.strength);
            m.SetFloat("_RimEdge", rim.edge);
            for (int i = 0; i < 3; i++)
                m.SetVector("_RimDir" + i, i < rim.dirs.Length ? new Vector4(rim.dirs[i].x, rim.dirs[i].y, rim.dirs[i].z, 0) : Vector4.zero);
            if (m.HasProperty(TintId)) m.SetColor(TintId, Color.white);
            return m;
        }

        /// <summary>
        /// A new material for showing an actor layer with the stage's rim (ActorRim.shader), or null when the shader is
        /// missing: the rim of the stage's period look in use (<see cref="ApplyLook"/>), else the stage's own
        /// (<see cref="RimFor"/>).
        /// </summary>
        public static Material RimMaterial(string stageId)
        {
            if (!rimTried)
            {
                rimTried = true;
                rimBase = Resources.Load<Material>("Models/ActorRim");
                if (rimBase == null || rimBase.shader == null || !rimBase.shader.isSupported)
                {
                    var sh = Shader.Find("FishingKing/ActorRim");
                    rimBase = sh != null && sh.isSupported ? new Material(sh) : null;
                }
                if (rimBase == null) Debug.LogWarning("[ActorArt] rim shader missing: actor layers without the sun rim");
            }
            if (rimBase == null) return null;
            bool look = lookRim.HasValue && lookStage == stageId;
            var rim = Tuned(look ? lookRim.Value : RimFor(stageId), out float drop, true, stageId);
            var m = new Material(rimBase);
            m.SetColor("_RimColor", rim.col);
            m.SetFloat("_RimStrength", rim.strength);
            m.SetFloat("_RimEdge", rim.edge);
            m.SetFloat("_RimEdgeDrop", drop);
            for (int i = 0; i < 3; i++)
                m.SetVector("_RimDir" + i, i < rim.dirs.Length ? new Vector4(rim.dirs[i].x, rim.dirs[i].y, rim.dirs[i].z, 0) : Vector4.zero);
            m.SetVector("_RimSpan", Vector4.zero);
            if (m.HasProperty(TintId)) m.SetColor(TintId, look ? lookTint : Color.white);
            return m;
        }

        static Shader toon;
        static bool toonTried;
        static readonly Dictionary<string, Dictionary<string, Tone>> palettes = new Dictionary<string, Dictionary<string, Tone>>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static Shader Toon
        {
            get
            {
                if (toonTried) return toon;
                toonTried = true;
                var baseMat = Resources.Load<Material>("Models/ActorToon");
                toon = baseMat != null ? baseMat.shader : null;
                if (toon == null || !toon.isSupported) toon = Shader.Find("FishingKing/ActorToon");
                if (toon == null) Debug.LogWarning("[ActorArt] toon shader missing");
                return toon;
            }
        }

        /// <summary>The model prefab under Resources/Models, or null (the game then keeps the 2D sprites).</summary>
        public static GameObject Model(string name) => Toon != null ? Resources.Load<GameObject>("Models/" + name) : null;

        /// <summary>Instantiates a model on a Unity layer with toon materials from the palette json (Models/&lt;palette&gt;).</summary>
        public static GameObject Spawn(GameObject prefab, string palette, int layer, Transform parent)
        {
            var go = Object.Instantiate(prefab, parent, false);
            go.name = prefab.name;
            var pal = Palette(palette);
            // the angler and the reels are lit by the stage's period look (the legends by their encounter set)
            bool stageActor = palette == "angler_palette" || palette == "reel_palette";
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    ms[i] = Material(ms[i] != null ? ms[i].name : "", pal);
                    if (stageActor && ms[i] != null)
                    {
                        stageToon.Add(ms[i]);
                        ms[i].SetVector(KeyId, lookKey.normalized);
                    }
                }
                r.sharedMaterials = ms;
            }
            return go;
        }

        /// <summary>A transform anywhere under root by name (the FBX root node is merged into the prefab root).</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var t = Find(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        static Material Material(string name, Dictionary<string, Tone> pal)
        {
            name = name.Replace(" (Instance)", "").Trim();
            if (mats.TryGetValue(name, out var m) && m != null) return m;
            if (!pal.TryGetValue(name, out var tone))
            {
                Debug.LogWarning("[ActorArt] no palette entry for material " + name);
                tone = new Tone { dark = new Color(0.25f, 0.25f, 0.3f), mid = new Color(0.5f, 0.5f, 0.55f), light = new Color(0.75f, 0.75f, 0.8f), outline = Art.Hex("#272639") };
            }
            m = new Material(Toon) { name = "toon_" + name };
            m.SetColor("_Dark", tone.dark);
            m.SetColor("_Mid", tone.mid);
            m.SetColor("_Light", tone.light);
            m.SetColor("_OutlineColor", tone.outline);
            m.SetVector("_KeyDir", KeyDir.normalized);
            m.SetFloat("_OutlinePx", 1f);
            // the sprite materials (hyb_character.mats): band thresholds and patterns per role
            var bands = new Vector4(0.45f, 0.74f, 0, 0);
            var stripes = Vector4.zero;
            float noise = 0f;
            switch (name)
            {
                case "vest": bands = new Vector4(0.55f, 0.8f, 0, 0); break;
                case "skin": bands = new Vector4(0.3f, 0.72f, 0, 0); break;   // fists read as skin, not as the lit shirt tone
                case "shirt": stripes = new Vector4(0.075f, 0.075f, 0.28f, 0.16f); break;   // plaid
                case "bag": stripes = new Vector4(0f, 0.045f, 0.34f, 0.5f); break;          // wicker
                case "trousers": noise = 0.04f; break;
            }
            m.SetVector("_Bands", bands);
            m.SetVector("_Stripes", stripes);
            m.SetFloat("_Noise", noise);
            if (name.EndsWith("_glow"))
            {
                // emissive (Poseidon gem, electric LCD): bright, unlit, no outline
                m.SetFloat("_Unlit", 1f);
                m.SetFloat("_OutlinePx", 0f);
            }
            mats[name] = m;
            return m;
        }

        /// <summary>Parses Models/&lt;name&gt;.json: { "material": { "ramp": [dark, mid, light], "outline": "#hex" }, ... }.</summary>
        public static Dictionary<string, Tone> Palette(string name)
        {
            if (palettes.TryGetValue(name, out var p)) return p;
            p = new Dictionary<string, Tone>();
            var ta = Resources.Load<TextAsset>("Models/" + name);
            if (ta == null) Debug.LogWarning("[ActorArt] missing palette " + name);
            else
            {
                foreach (Match e in Regex.Matches(ta.text, "\"([^\"]+)\"\\s*:\\s*\\{([^{}]*)\\}"))
                {
                    var body = e.Groups[2].Value;
                    var ramp = Regex.Match(body, "\"ramp\"\\s*:\\s*\\[([^\\]]*)\\]");
                    var outline = Regex.Match(body, "\"outline\"\\s*:\\s*\"(#?[0-9a-fA-F]{6})\"");
                    if (!ramp.Success) continue;
                    var hexes = Regex.Matches(ramp.Groups[1].Value, "\"(#?[0-9a-fA-F]{6})\"");
                    if (hexes.Count < 3) continue;
                    p[e.Groups[1].Value] = new Tone
                    {
                        dark = Art.Hex(hexes[0].Groups[1].Value),
                        mid = Art.Hex(hexes[1].Groups[1].Value),
                        light = Art.Hex(hexes[2].Groups[1].Value),
                        outline = outline.Success ? Art.Hex(outline.Groups[1].Value) : Art.Hex("#272639"),
                    };
                }
            }
            palettes[name] = p;
            return p;
        }
    }
}
