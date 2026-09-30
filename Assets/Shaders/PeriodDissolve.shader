// The incoming period's stage layer (Docs/time_currents_spec.md 2.4): drawn over the outgoing one and dissolved in by
// an ordered 4x4 Bayer dither in the pixel view's render-target pixels, so the new look replaces 1/16 of the pixels at a
// time (never a translucent mix): a fragment shows when _F16 > (M[y%4][x%4] + 0.5) / 16, _F16 = floor(F * 16) / 16.
// Otherwise the built-in sprite shader (vertex colour, premultiplied alpha blending).
Shader "FishingKing/PeriodDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _F16 ("Dissolve (0..1 in sixteenths)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
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
            float _F16;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // M = {0,8,2,10},{12,4,14,6},{3,11,1,9},{15,7,13,5} (row y % 4, column x % 4), built from the 2x2 matrix
            // {0,2},{3,1}: M = 4 B2(x % 2, y % 2) + B2(x / 2 % 2, y / 2 % 2), B2(x, y) = 2x + 3y - 4xy (float maths only)
            float B2 (float2 a) { return 2.0 * a.x + 3.0 * a.y - 4.0 * a.x * a.y; }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = floor(i.pos.xy);
                float m = 4.0 * B2(fmod(p, 2.0)) + B2(fmod(floor(p * 0.5), 2.0));
                clip(_F16 - (m + 0.5) / 16.0);
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
