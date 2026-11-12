// A drifting band of mist: scrolling fbm, soft edges. Port of the prototype's mistMaterial. Two bands at most on Quest (overdraw).
Shader "HypaStories/Mist"
{
    Properties
    {
        _Color ("Colour", Color) = (0.36, 0.56, 0.63, 1)
        _Alpha ("Alpha", Float) = 0.12
        _Seed ("Seed", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "HypaStoriesCommon.cginc"

            fixed4 _Color;
            float _Alpha, _Seed;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float n = fbm(float2(i.uv.x * 7.0 + t * 0.025 + _Seed, i.uv.y * 2.2 - t * 0.01));
                float edge = smoothstep(0.0, 0.4, i.uv.y) * smoothstep(1.0, 0.5, i.uv.y) * smoothstep(0.0, 0.12, i.uv.x) * smoothstep(1.0, 0.88, i.uv.x);
                return fixed4(_Color.rgb, smoothstep(0.3, 0.85, n) * edge * _Alpha);
            }
            ENDCG
        }
    }
}
