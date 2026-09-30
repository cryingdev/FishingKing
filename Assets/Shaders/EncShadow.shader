// The legend as a shadow under the surface (EncounterView's top view: a topwater legend's tease seen from above). The
// 3D fish layer, rendered straight down by the fish camera (ActorToon: body alpha 1, outline 254/255, empty 0), is drawn
// as a murky dark shape: its coverage blurred by a radius that grows with depth and its opacity falling with depth.
// Both run along the body from the head (_Head) to the tail (_Tail), given in render-target pixels, so a fish pitched
// head-up shows a dark, crisp head near the surface and a faint, soft tail deep down. The soft edge is posterised to
// quarter steps with a 4 x 4 ordered dither (it stays pixel art); where the body is, a little of the fish's own colour
// (_Detail, darkened) shows through the tint. Output premultiplied (Blend One OneMinusSrcAlpha).
Shader "FishingKing/EncShadow"
{
    Properties
    {
        _MainTex ("Fish layer", 2D) = "black" {}
        _Tint ("Shadow colour", Color) = (0.063, 0.094, 0.055, 1)
        _Head ("Head (xy: render-target px, z: opacity, w: blur px)", Vector) = (0, 0, 0.6, 1)
        _Tail ("Tail (xy: render-target px, z: opacity, w: blur px)", Vector) = (0, 0, 0.3, 2)
        _Detail ("Fish colour showing through", Float) = 0
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
            fixed4 _Tint;
            float4 _Head, _Tail;
            float _Detail;

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

            static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float Cov (float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0, 0)).a > 0.002 ? 1.0 : 0.0; }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 px = i.uv * _MainTex_TexelSize.zw;
                // how far along the body (0 at the head, 1 at the tail)
                float2 ht = _Tail.xy - _Head.xy;
                float t = saturate(dot(px - _Head.xy, ht) / max(dot(ht, ht), 1.0));
                float opacity = lerp(_Head.z, _Tail.z, t);
                float r = lerp(_Head.w, _Tail.w, t);
                fixed4 c = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
                float body = c.a > 0.002 ? 1.0 : 0.0;
                float cov = body;
                if (r > 0.3)
                {
                    // a disc blur: the pixel, 8 taps at half the radius and 8 at the radius
                    float s = body * 2.0, n = 2.0;
                    [unroll]
                    for (int k = 0; k < 8; k++)
                    {
                        float an = k * 0.7853982 + 0.3926991;
                        float2 d = float2(cos(an), sin(an)) * _MainTex_TexelSize.xy;
                        s += Cov(i.uv + d * r) + 1.5 * Cov(i.uv + d * (r * 0.5));
                        n += 2.5;
                    }
                    cov = s / n;
                }
                if (cov < 0.02) return fixed4(0, 0, 0, 0);
                float2 q = fmod(floor(px), 4.0);
                float b = (Bayer[(int)q.y * 4 + (int)q.x] + 0.5) / 16.0;
                float a = opacity * saturate(floor(cov * 4.0 + b) * 0.25);
                if (a <= 0.001) return fixed4(0, 0, 0, 0);
                fixed3 col = lerp(_Tint.rgb, c.rgb * 0.6, _Detail * body);
                return fixed4(col * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
