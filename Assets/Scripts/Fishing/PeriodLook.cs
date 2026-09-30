using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>A light of a period's look that Unity animates (canvas px, y down; see StageView).</summary>
    [Serializable]
    public class PeriodLight
    {
        public float x, y, r;
        public string col, blink;
    }

    /// <summary>
    /// One stage's look in one period (Docs/time_currents_spec.md 5.1), written by the Blender period run
    /// (Resources/Data/Periods/&lt;stage&gt;_&lt;period&gt;.json): water tint / deep, the actor's rim and key light, the actor
    /// tint, glints, birds / fireflies and the animated lights.
    /// </summary>
    [Serializable]
    public class PeriodLook
    {
        public string stage, period;
        public bool native;
        public string waterTint, waterDeep;
        public string rimCol;
        public float rimStrength;
        public float[] rimDirs;        // (dx, dy, w) x 1..3
        public float[] keyDir;         // Unity axes, towards the light
        public string keyCol;
        public string skyHorizon, skyZenith;
        public string actorTint, glintCol;
        public float glintDensity = 1f, fxAlpha = 1f;
        public bool birds, fireflies;
        public PeriodLight[] lights;

        /// <summary>Whether the file was found (false: the native art with the period's default values).</summary>
        [NonSerialized] public bool rendered = true;

        static readonly Dictionary<string, PeriodLook> cache = new Dictionary<string, PeriodLook>();

        static PeriodLook Read(string stageId, Period p)
        {
            var ta = Resources.Load<TextAsset>($"Data/Periods/{stageId}_{GameClock.Id(p)}");
            if (ta == null) return null;
            try { return JsonUtility.FromJson<PeriodLook>(ta.text); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Period] {stageId}_{GameClock.Id(p)} unreadable: {e.Message}");
                return null;
            }
        }

        /// <summary>The look of a stage in a period; a period not rendered yet gets the native look's rim with the period's defaults.</summary>
        public static PeriodLook Load(string stageId, Period p)
        {
            string key = stageId + "_" + GameClock.Id(p);
            if (cache.TryGetValue(key, out var l)) return l;
            l = Read(stageId, p);
            if (l == null)
            {
                Debug.Log($"[Period] {key} missing: native art");
                // the native look's rim / key (whichever period it is), today's water, the period's default tints
                PeriodLook nat = null;
                for (int i = 0; i < 4 && nat == null; i++)
                {
                    var o = Read(stageId, (Period)i);
                    if (o != null && o.native) nat = o;
                }
                var lay = Art.Layout(stageId);
                l = nat != null ? (PeriodLook)nat.MemberwiseClone() : new PeriodLook { rimCol = "#ffffff", rimStrength = 0f };
                l.stage = stageId;
                l.period = GameClock.Id(p);
                l.native = nat == null;
                l.rendered = false;
                l.keyDir = null;   // (the native art: today's key light)
                l.waterTint = lay?.waterTint ?? l.waterTint;
                l.waterDeep = lay?.waterDeep ?? l.waterDeep;
                if (!l.native)
                {
                    switch (p)
                    {
                        case Period.Dawn: l.actorTint = "#e8e2ee"; l.glintCol = "#ffe6e0"; l.glintDensity = 0.6f; l.fxAlpha = 0.9f; break;
                        case Period.Day: l.actorTint = "#ffffff"; l.glintCol = "#ffffff"; l.glintDensity = 1f; l.fxAlpha = 1f; break;
                        case Period.Evening: l.actorTint = "#ffffff"; l.glintCol = "#ffe0b0"; l.glintDensity = 0.8f; l.fxAlpha = 0.9f; break;
                        default: l.actorTint = "#8e9ac0"; l.glintCol = "#c8d4f0"; l.glintDensity = 0.3f; l.fxAlpha = 0.7f; break;
                    }
                    l.lights = new PeriodLight[0];
                }
            }
            if (string.IsNullOrEmpty(l.actorTint)) l.actorTint = "#ffffff";
            if (string.IsNullOrEmpty(l.glintCol)) l.glintCol = "#ffffff";
            l.lights ??= new PeriodLight[0];
            cache[key] = l;
            return l;
        }

        // parsed values (cached)
        [NonSerialized] Color? cTint, cDeep, cRim, cActor, cGlint, cKey, cHor, cZen;
        public Color WaterTint => cTint ??= Art.Hex(waterTint);
        public Color WaterDeep => cDeep ??= Art.Hex(waterDeep);
        public Color RimColor => cRim ??= Art.Hex(rimCol);
        public Color ActorTint => cActor ??= Art.Hex(actorTint);
        public Color GlintColor => cGlint ??= Art.Hex(glintCol);
        public Color KeyColor => cKey ??= Art.Hex(string.IsNullOrEmpty(keyCol) ? "#ffffff" : keyCol);
        public Color SkyHorizon => cHor ??= Art.Hex(string.IsNullOrEmpty(skyHorizon) ? "#808080" : skyHorizon);
        public Color SkyZenith => cZen ??= Art.Hex(string.IsNullOrEmpty(skyZenith) ? "#404040" : skyZenith);

        /// <summary>The toon materials' key light for this look (Unity axes, towards the light): today's for the native look.</summary>
        public Vector3 ActorKey
        {
            get
            {
                if (native || keyDir == null || keyDir.Length < 3) return ActorArt.KeyDir.normalized;
                return new Vector3(keyDir[0], Mathf.Max(keyDir[1], 0.55f), keyDir[2]).normalized;
            }
        }

        /// <summary>The rim directions as (dx, dy, weight) steps, strongest first (3, 6 or 9 floats in the file).</summary>
        public Vector3[] RimDirs
        {
            get
            {
                int n = rimDirs != null ? rimDirs.Length / 3 : 0;
                var d = new Vector3[n];
                for (int i = 0; i < n; i++) d[i] = new Vector3(rimDirs[3 * i], rimDirs[3 * i + 1], rimDirs[3 * i + 2]);
                return d;
            }
        }
    }

    /// <summary>
    /// The look in use right now: the blend of two periods' looks (colours lerped in sRGB, floats lerped, flags and rim
    /// directions from the dominant period; spec 5.1).
    /// </summary>
    public struct LookNow
    {
        public PeriodBlend Blend;
        public Color WaterTint, WaterDeep, RimCol, ActorTint, GlintCol, SkyHorizon;
        public float RimStrength, GlintDensity, FxAlpha;
        public Vector3 Key;
        public Vector3[] RimDirs;
        public bool Birds;
        /// <summary>The weight of the looks with fireflies, and the Night period's weight.</summary>
        public float Fireflies, Night;

        public static LookNow Of(PeriodLook[] looks, PeriodBlend b)
        {
            var a = looks[(int)b.From];
            var c = looks[(int)b.To];
            float f = b.F;
            var dom = f < 0.5f ? a : c;
            var ka = a.ActorKey;
            var kc = c.ActorKey;
            var key = Vector3.Lerp(ka, kc, f);
            return new LookNow
            {
                Blend = b,
                WaterTint = Color.Lerp(a.WaterTint, c.WaterTint, f),
                WaterDeep = Color.Lerp(a.WaterDeep, c.WaterDeep, f),
                RimCol = Color.Lerp(a.RimColor, c.RimColor, f),
                ActorTint = Color.Lerp(a.ActorTint, c.ActorTint, f),
                GlintCol = Color.Lerp(a.GlintColor, c.GlintColor, f),
                SkyHorizon = Color.Lerp(a.SkyHorizon, c.SkyHorizon, f),
                RimStrength = Mathf.Lerp(a.rimStrength, c.rimStrength, f),
                GlintDensity = Mathf.Lerp(a.glintDensity, c.glintDensity, f),
                FxAlpha = Mathf.Lerp(a.fxAlpha, c.fxAlpha, f),
                Key = key.sqrMagnitude > 1e-6f ? key.normalized : ActorArt.KeyDir.normalized,
                RimDirs = dom.RimDirs,
                Birds = dom.birds,
                Fireflies = (a.fireflies ? 1f - f : 0f) + (c.fireflies ? f : 0f),
                Night = b.Weight(Period.Night),
            };
        }
    }
}
