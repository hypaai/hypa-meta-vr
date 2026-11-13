// Rain streaks animated entirely in the vertex shader; the volume follows the camera and the story's rain value thins it out
// (each drop has a seed and shows only when seed < intensity). Port of the prototype's rain.js, as thin camera-facing quads
// (1-pixel lines look poor in a headset). uv0.x = side (0/1), uv0.y = 0 top, 1 bottom; uv1.x = seed.
Shader "HypaStories/Rain"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 1
        _Height ("Volume height", Float) = 8
        _Speed ("Fall speed", Float) = 6.5
        _Width ("Streak width (m)", Float) = 0.005
        _MaxFade ("Fade distance", Float) = 26
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "HypaStoriesCommon.cginc"

            float _Intensity, _Height, _Speed, _Width, _MaxFade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 seed : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float a : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float s = v.seed.x;
                float3 p = v.vertex.xyz;
                p.y = fmod(p.y - _Time.y * (_Speed + s * 2.5), _Height);
                if (p.y < 0.0) p.y += _Height;
                p += v.uv.y * float3(0.02, -0.28, 0.0);
                float3 view = UnityObjectToViewPos(float4(p, 1.0));
                view.x += (v.uv.x - 0.5) * _Width;
                o.pos = mul(UNITY_MATRIX_P, float4(view, 1.0));
                o.a = ((1.0 - v.uv.y) * 0.55 + 0.2) * step(s, _Intensity) * smoothstep(_MaxFade, 4.0, -view.z) * smoothstep(0.6, 1.6, -view.z); // none right at the eyes
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(ToOutput(float3(0.55, 0.68, 0.85) * 0.7), i.a * 0.5);
            }
            ENDCG
        }
    }
}
