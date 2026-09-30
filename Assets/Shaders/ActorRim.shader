// Shows a 3D actor layer's render target 1:1 over the pixel view (see ActorLayer.cs) and adds the stage's sun rim, the
// way the hybrid sprites got it (Tools/Blender/variants/hybrid/hyb_core.py rim_amount / rim_fn / rim_light, as used by
// hyb_character.post): a crisp 1 px rim on the OUTER silhouette's sun side only, and, like the sprites' selective outline
// (hyb_core.outer_outline, lit_steps = 1), no dark ink on that side: the outline pixel there becomes the lit colour of
// the body pixel it borders (its rim colour one step darker: OKLab L - _RimEdgeDrop), mixed in by _RimEdge (1 = the
// lake sprites; weaker presets keep part of the ink). The shadow side keeps the dark outline.
//
// The toon shader writes body pixels with alpha 1 and its outline hull with alpha 254/255 (a marker), the clear is
// alpha 0. A body pixel gets the rim when, looking one pixel at a time towards the light (_RimDir0..2: pixel step and
// weight), it reaches empty space past at most two outline pixels (so the rim sits just inside the dark outline; an
// inner outline, where another body part lies behind it, stays unlit) and the pixel behind it (away from the light)
// is body too (parts only 1 px thick stay unlit, min_thick = 2). Weight = max over the directions, times the sprite's
// fade down the figure (1.2 - 0.95 t clamped to 0.3..1, t = 0 at _RimSpan.x, 1 at _RimSpan.y, in uv.y); lit where
// the weight is >= 0.45 (levels = 1). A lit pixel's OKLab lightness moves towards the rim colour's by 0.75 s (at least
// +0.05) and its hue / chroma by 0.5 s (s = _RimStrength). Every drawn pixel comes out opaque.
Shader "FishingKing/ActorRim"
{
    Properties
    {
        _MainTex ("Actor layer", 2D) = "black" {}
        _RimColor ("Rim colour", Color) = (1, 0.81, 0.55, 1)
        _RimStrength ("Rim strength (s)", Float) = 0
        _RimDir0 ("Rim direction 0 (xy: pixel step towards the light, z: weight)", Vector) = (1, 0, 1, 0)
        _RimDir1 ("Rim direction 1", Vector) = (0, 0, 0, 0)
        _RimDir2 ("Rim direction 2", Vector) = (0, 0, 0, 0)
        _RimSpan ("Fade span (x: uv.y of the top, y: uv.y of the bottom; x <= y: no fade)", Vector) = (0, 0, 0, 0)
        _RimEdge ("Sun-side outline: 0 dark ink .. 1 lit body colour", Float) = 0
        _RimEdgeDrop ("Lit outline: OKLab L below the rim colour", Float) = 0.1
        _Tint ("Actor tint (the period look: body and outline x tint before the rim)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _RimColor, _Tint;
            float _RimStrength, _RimEdge, _RimEdgeDrop;
            float4 _RimDir0, _RimDir1, _RimDir2, _RimSpan;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // alpha classes: 0 empty, 254/255 outline, 1 body
            static const float EmptyA = 0.5 / 255.0, BodyA = 254.5 / 255.0;

            float A (float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0, 0)).a; }

            // 1 when the body pixel at uv is on the outer silhouette's side facing step d (pixels)
            float Edge (float2 uv, float2 d)
            {
                if (d.x == 0 && d.y == 0) return 0;
                float2 st = d * _MainTex_TexelSize.xy;
                if (A(uv - st) < BodyA) return 0;          // 1 px thick along d: no rim
                [unroll]
                for (int j = 1; j <= 3; j++)
                {
                    float a = A(uv + st * j);
                    if (a >= BodyA) return 0;               // another body part: an inner outline
                    if (a < EmptyA)                         // empty space, and at least 2 px of it (no narrow slits)
                        return A(uv + st * (j + 1)) < EmptyA ? 1 : 0;
                }
                return 0;
            }

            float3 S2L (float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
            float3 L2S (float3 c) { c = saturate(c); return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }
            float3 Cbrt (float3 x) { return sign(x) * pow(abs(x), 1.0 / 3.0); }

            float3 ToLab (float3 srgb)
            {
                float3 c = S2L(srgb);
                float3 lms = Cbrt(float3(
                    0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b,
                    0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b,
                    0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b));
                return float3(
                    0.2104542553 * lms.x + 0.7936177850 * lms.y - 0.0040720468 * lms.z,
                    1.9779984951 * lms.x - 2.4285922050 * lms.y + 0.4505937099 * lms.z,
                    0.0259040371 * lms.x + 0.7827717662 * lms.y - 0.8086757660 * lms.z);
            }

            float3 FromLab (float3 lab)
            {
                float3 lms = float3(
                    lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z,
                    lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z,
                    lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z);
                lms = lms * lms * lms;
                return L2S(float3(
                    4.0767416621 * lms.x - 3.3077115913 * lms.y + 0.2309699292 * lms.z,
                    -1.2684380046 * lms.x + 2.6097574011 * lms.y - 0.3413193965 * lms.z,
                    -0.0041960863 * lms.x - 0.7034186147 * lms.y + 1.7076147010 * lms.z));
            }

            // hyb_core.rim_fn in OKLab: lightness towards the rim colour's by 0.75 s (at least +0.05), hue / chroma by 0.5 s
            float3 RimLab (float3 srgb)
            {
                float3 lab = ToLab(srgb), rim = ToLab(_RimColor.rgb);
                float s = _RimStrength;
                float3 o;
                o.x = max(lab.x + (rim.x - lab.x) * s * 0.75, lab.x + 0.05);
                o.yz = lab.yz + (rim.yz - lab.yz) * s * 0.5;
                return o;
            }

            // the sprite's fade down the figure (1 without a span)
            float Fade (float2 uv)
            {
                if (_RimSpan.x <= _RimSpan.y) return 1;
                float t = (_RimSpan.x - uv.y) / (_RimSpan.x - _RimSpan.y);
                return clamp(1.2 - 0.95 * t, 0.3, 1.0);
            }

            // outline pixel on the outer silhouette's side facing step dw.xy: towards the light, past at most one more
            // outline pixel, empty space (2 px of it), and a body pixel behind it, past at most one more outline pixel
            // (the hull is up to 2 px thick along a diagonal or where two parts' outlines meet): keeps the best weight
            // and where that body pixel is
            void LitOutline (float2 uv, float4 dw, inout float best, inout float2 bodyOff)
            {
                if ((dw.x == 0 && dw.y == 0) || dw.z <= best) return;
                float2 st = dw.xy * _MainTex_TexelSize.xy;
                float o1 = A(uv + st);
                if (o1 >= BodyA) return;
                float2 out1 = o1 < EmptyA ? st : st * 2;
                if (A(uv + out1) >= EmptyA || A(uv + out1 + st) >= EmptyA) return;
                float a1 = A(uv - st);
                if (a1 >= BodyA) { best = dw.z; bodyOff = -st; }
                else if (a1 >= EmptyA && A(uv - st * 2) >= BodyA) { best = dw.z; bodyOff = -st * 2; }
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
                if (c.a < EmptyA) return fixed4(0, 0, 0, 0);
                // the period look's tint (sRGB multiply: the project is in Gamma space); the rim colour is not tinted
                c.rgb *= _Tint.rgb;
                if (_RimStrength <= 0) return fixed4(c.rgb, 1);
                if (c.a < BodyA)
                {
                    // outline: on the sun side the dark ink gives way to the lit body colour (one step below its rim)
                    if (_RimEdge <= 0) return fixed4(c.rgb, 1);
                    float best = 0;
                    float2 bodyOff = 0;
                    LitOutline(i.uv, _RimDir0, best, bodyOff);
                    LitOutline(i.uv, _RimDir1, best, bodyOff);
                    LitOutline(i.uv, _RimDir2, best, bodyOff);
                    if (best * Fade(i.uv) < 0.45) return fixed4(c.rgb, 1);
                    float3 o = RimLab(tex2Dlod(_MainTex, float4(i.uv + bodyOff, 0, 0)).rgb * _Tint.rgb);
                    o.x -= _RimEdgeDrop;
                    return fixed4(lerp(c.rgb, FromLab(o), _RimEdge), 1);
                }
                float k = max(Edge(i.uv, _RimDir0.xy) * _RimDir0.z,
                          max(Edge(i.uv, _RimDir1.xy) * _RimDir1.z, Edge(i.uv, _RimDir2.xy) * _RimDir2.z));
                if (k * Fade(i.uv) < 0.45) return fixed4(c.rgb, 1);
                return fixed4(FromLab(RimLab(c.rgb)), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
