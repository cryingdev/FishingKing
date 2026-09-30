// Things drawn over the stage's front layer (StageView.OrderFront = 40) that live in the scene - the jumping / landed
// fish, splashes, the line above the water, the float in the air, the bait - hidden per pixel where the front layer
// (the pier, the rocks, the quay, the boat, the snowbanks) is nearer the camera than they are (FrontOcclusion.cs).
// The front layer's depth map (Sprites/Stages/<stage>_front_depth, Tools/Blender/variants/hybrid/hyb_frontdepth.py):
// A = 255 an occluder, R * 256 + G = depth code (near + code * step metres, Persp camera depth), B = 255 a flat thing
// floating on the water (lily pads, duckweed: never in front of anything above the water, so it never hides).
// The fragment's own depth: _OccDepth (per renderer, MaterialPropertyBlock; below 0: no depth) or, when 0 (a line: no
// per-renderer value), its world z x _FKOccZ (the line's vertices carry their depth in z). A fragment with no depth is
// never hidden. Otherwise Sprites/Default.
Shader "FishingKing/FrontOcclude"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [PerRendererData] _OccDepth ("Depth (m) for the front-layer test (0: world z)", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
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
            #include "UnitySprites.cginc"

            float _OccDepth;
            sampler2D _FKOccTex;   // the front depth map: point sampled, raw bytes
            float4 _FKOccRect;     // the front layer in the world: min x, min y, 1 / width, 1 / height
            float4 _FKOccDec;      // near (m), step (m per code), bias (m), on (1) / off (0)
            float _FKOccZ;         // metres of depth per world z unit (the line's vertices)

            struct v2o
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 world : TEXCOORD1;
            };

            v2o vert (appdata_t IN)
            {
                v2o OUT;
                float4 p = UnityFlipSprite(IN.vertex.xyz, _Flip);
                OUT.world = mul(unity_ObjectToWorld, p).xyz;
                OUT.vertex = UnityObjectToClipPos(p);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color * _RendererColor;
                return OUT;
            }

            fixed4 frag (v2o IN) : SV_Target
            {
                // (a sprite always has its _OccDepth: below 0 = "no depth", never hidden; only the lines, which have no
                // per-renderer value, use their vertices' world z)
                float d = _OccDepth > 0.0 ? _OccDepth : (_OccDepth < 0.0 ? 0.0 : IN.world.z * _FKOccZ);
                if (_FKOccDec.w > 0.5 && d > 0.0)
                {
                    // the front layer's texel under this pixel (the same point the front sprite samples here)
                    float2 uv = (IN.world.xy - _FKOccRect.xy) * _FKOccRect.zw;
                    if (uv.x >= 0.0 && uv.y >= 0.0 && uv.x < 1.0 && uv.y < 1.0)
                    {
                        float4 t = tex2Dlod(_FKOccTex, float4(uv, 0.0, 0.0));
                        if (t.a > 0.5 && t.b < 0.5)
                        {
                            float code = floor(t.r * 255.0 + 0.5) * 256.0 + floor(t.g * 255.0 + 0.5);
                            clip(_FKOccDec.x + code * _FKOccDec.y + _FKOccDec.z - d);
                        }
                    }
                }
                fixed4 c = SampleSpriteTexture(IN.texcoord) * IN.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
