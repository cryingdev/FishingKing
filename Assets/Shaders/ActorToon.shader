// Toon shader for the real-time 3D actors (angler, reels) rendered as pixel-art layers (see ActorLayer.cs).
// Built-in render pipeline, gamma colour space: the palette hex colours go straight to the render target.
//
// Pass 1: unlit 3-band ramp. f = 0.5 + 0.5 * dot(N, key) (half-lambert, key light fixed in world space like the
//         sprite renders), hard bands dark < _Bands.x <= mid < _Bands.y <= light. No specular, no AA. Optional
//         stripes on the "rest" UV (rest-pose position in metres: the shirt plaid, the creel wicker) and a coarse
//         noise (trousers) lower / shift f before banding, like the sprite materials did.
// Pass 2: inverted hull outline in the palette's outline colour, pushed out along the smoothed normal (UV3, written
//         by the model importer) by exactly _OutlinePx pixels of the render target, whatever the distance. Written
//         with alpha 254/255 (body pixels: 1) so the layer's rim pass (ActorRim.shader) can tell outline from body.
//
// Lure light (the legend encounter, EncounterView.cs): with _LurePos.w > 0 the only light is the lure at _LurePos.xyz;
//         lit = 1 - smoothstep(0.35 w, w, distance) and f = (0.5 + 0.5 dot(N, towards the lure)) * lit, f < 0.12 -> _Abyss;
//         the outline turns _FogOutline where lit <= 0.3. With w = 0 (every other material) nothing changes.
//         _SunMix (daylight sets, Docs/legends_rollout.md 1.5): the light direction is lerp(towards the lure, _KeyDir,
//         _SunMix), so the fish is shaded from above; the lure's radius stays a visibility sphere (outside it _Abyss, the
//         water colour). _SunMix = 0 is bit-identical to before.
Shader "FishingKing/ActorToon"
{
    Properties
    {
        _Dark ("Dark tone", Color) = (0.2, 0.2, 0.25, 1)
        _Mid ("Mid tone", Color) = (0.5, 0.5, 0.5, 1)
        _Light ("Light tone", Color) = (0.8, 0.8, 0.8, 1)
        _OutlineColor ("Outline", Color) = (0.153, 0.149, 0.224, 1)
        _OutlinePx ("Outline width (render-target px)", Float) = 1
        _Bands ("Band thresholds (x: dark|mid, y: mid|light)", Vector) = (0.45, 0.74, 0, 0)
        _KeyDir ("Key light (world, towards the light)", Vector) = (0.752, 0.647, -0.125, 0)
        _Stripes ("Stripes (x: period on u m, y: period on v m, z: duty, w: f drop)", Vector) = (0, 0, 0, 0)
        _Noise ("Noise on f (+-)", Float) = 0
        _Unlit ("Unlit / emissive (light tone)", Float) = 0
        _LurePos ("Lure light: xyz world, w radius m (0 = off)", Vector) = (0, 0, 0, 0)
        _Abyss ("Colour outside the lure light", Color) = (0.012, 0.031, 0.047, 1)
        _FogOutline ("Outline outside the lure light", Color) = (0.122, 0.373, 0.439, 1)
        _SunMix ("Lure light: share of the sun (_KeyDir) in the light direction", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TOON"
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Dark, _Mid, _Light, _Abyss;
            float4 _Bands, _KeyDir, _Stripes, _LurePos;
            float _Noise, _Unlit, _SunMix;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 rest : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
                float2 rest : TEXCOORD1;
                float3 wpos : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.nrm = UnityObjectToWorldNormal(v.normal);
                o.rest = v.rest;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash21 (float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_Unlit > 0.5) return fixed4(_Light.rgb, 1);
                float3 key = normalize(_KeyDir.xyz);
                float lit = 1;
                if (_LurePos.w > 0)
                {
                    // the legend encounter's darkness: the only light is the lure, fading out over its radius
                    float d = distance(i.wpos, _LurePos.xyz);
                    lit = 1 - smoothstep(0.35 * _LurePos.w, _LurePos.w, d);
                    float3 toLure = normalize(_LurePos.xyz - i.wpos);
                    if (_SunMix > 0)
                    {
                        // a daylight set: shaded from above, the lure's radius only a visibility sphere
                        float3 l = lerp(toLure, key, _SunMix);
                        key = dot(l, l) > 1e-6 ? normalize(l) : key;
                    }
                    else key = toLure;
                }
                float f = (0.5 + 0.5 * dot(normalize(i.nrm), key)) * lit;
                if (_LurePos.w > 0 && f < 0.12) return fixed4(_Abyss.rgb, 1);
                if (_Stripes.x > 0 && frac(i.rest.x / _Stripes.x) < _Stripes.z) f -= _Stripes.w;
                if (_Stripes.y > 0 && frac(i.rest.y / _Stripes.y) < _Stripes.z) f -= _Stripes.w;
                if (_Noise > 0) f += (hash21(floor(i.rest * 8.0)) * 2.0 - 1.0) * _Noise;
                fixed3 c = f < _Bands.x ? _Dark.rgb : (f < _Bands.y ? _Mid.rgb : _Light.rgb);
                return fixed4(c, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            ZTest LEqual
            Offset 1, 1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor, _FogOutline;
            float4 _LurePos;
            float _OutlinePx;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float3 smooth : TEXCOORD3;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 n = dot(v.smooth, v.smooth) > 0.01 ? v.smooth : v.normal;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 nw = UnityObjectToWorldNormal(n);
                float4 c0 = mul(UNITY_MATRIX_VP, float4(wp, 1));
                float4 c1 = mul(UNITY_MATRIX_VP, float4(wp + nw * 0.01, 1));
                // the normal's direction on screen, in render-target pixels
                float2 d = (c1.xy / c1.w - c0.xy / c0.w) * _ScreenParams.xy;
                float len = length(d);
                d = len > 1e-6 ? d / len : float2(0, 0);
                // _OutlinePx pixels = 2 * px / size in NDC, times w in clip space
                c0.xy += d * (2.0 * _OutlinePx) / _ScreenParams.xy * c0.w;
                o.pos = c0;
                o.wpos = wp;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // alpha 254/255 marks outline pixels for the rim pass (ActorRim.shader); shown as opaque
                fixed3 c = _OutlineColor.rgb;
                if (_LurePos.w > 0)
                {
                    // outside the lure light the contour is a dim teal, not the ink
                    float lit = 1 - smoothstep(0.35 * _LurePos.w, _LurePos.w, distance(i.wpos, _LurePos.xyz));
                    c = lit > 0.3 ? _OutlineColor.rgb : _FogOutline.rgb;
                }
                return fixed4(c, 254.0 / 255.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
