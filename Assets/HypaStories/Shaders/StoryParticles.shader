// Fireflies, motes and embers: one mesh of camera-facing quads per system, all motion in the vertex shader (no per-frame CPU),
// one draw call each. Port of the prototype's particleMaterial (Ideation/src/shaders.js). uv0 = quad corner, uv1 = per-particle seed.
Shader "HypaStories/Particles"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Size ("Size (m)", Float) = 0.05
        _Alpha ("Alpha", Float) = 1
        _Speed ("Speed", Float) = 1
        _Blink ("Blink", Float) = 0
        _Ember ("Ember mode", Float) = 0
        _Scale ("Drift scale", Float) = 0.5
        _Gather ("Gather to the fire", Range(0, 1)) = 0
        _Boost ("Fire boost", Float) = 1
        _Fire ("Gather centre (object space)", Vector) = (0, 0.6, 0, 0)
        _EmberBase ("Ember source (object space)", Vector) = (0, 0.15, 0, 0)
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

            fixed4 _Color;
            float _Size, _Alpha, _Speed, _Blink, _Ember, _Scale, _Gather, _Boost;
            float4 _Fire, _EmberBase;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 seed : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float a : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 s = v.seed;
                float3 p = v.vertex.xyz;
                float a = 1.0, t = _Time.y * _Speed, S = _Scale;
                if (_Ember > 0.5)
                {
                    float life = frac(t * 0.1 * (0.5 + s.w) * _Boost + s.y);
                    p = _EmberBase.xyz + float3((s.x - 0.5) * 0.7, 0.0, (s.z - 0.5) * 0.7) * S
                        + float3(sin(life * 6.0 + s.x * 20.0) * 0.6 * life, life * 5.5 * (0.6 + s.z), cos(life * 5.0 + s.z * 20.0) * 0.35 * life) * S;
                    a = (1.0 - life) * smoothstep(0.0, 0.06, life);
                }
                else
                {
                    p.x += (sin(t * 0.7 + s.x * 6.28) * 1.2 + sin(t * 0.31 + s.y * 20.0) * 0.8) * S;
                    p.y += sin(t * 0.9 + s.y * 6.28) * 0.6 * S;
                    p.z += cos(t * 0.5 + s.z * 6.28) * S;
                    p = lerp(p, _Fire.xyz + float3((s.x - 0.5) * 3.0, s.y * 2.4 + 0.2, (s.z - 0.5) * 3.0) * S, _Gather);
                    if (_Blink > 0.5) a = pow(max(0.0, sin(_Time.y * (0.6 + s.w) + s.z * 40.0)), 3.0) * 0.9 + 0.1;
                }
                float3 view = UnityObjectToViewPos(float4(p, 1.0));
                view.xy += (v.uv - 0.5) * _Size * (0.6 + s.w);
                o.pos = mul(UNITY_MATRIX_P, float4(view, 1.0));
                o.uv = v.uv;
                o.a = a;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv - 0.5);
                float a = pow(smoothstep(0.5, 0.0, d), 2.0);
                return fixed4(_Color.rgb, a * i.a * _Alpha);
            }
            ENDCG
        }
    }
}
