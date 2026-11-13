// One flame card: noise-driven tongues, white-yellow core to red tips. Six crossed cards make the fire (StoryFire). Cards seen
// edge-on fade out instead of showing as thin lines. Port of the prototype's flameMaterial (Ideation/src/shaders.js).
Shader "HypaStories/Flame"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1
        _Seed ("Seed", Float) = 0
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

            float _Intensity, _Seed;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float facing : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                o.facing = abs(dot(n, normalize(_WorldSpaceCameraPos - w)));
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float x = (i.uv.x - 0.5) * 2.0, y = i.uv.y, t = _Time.y * 1.5 + _Seed;
                // sway grows with height; two noise scales give broad lobes plus fast licking edges
                float n1 = fbm(float2(x * 1.8 + _Seed, y * 1.6 - t * 1.5));
                float n2 = fbm(float2(x * 4.6 - _Seed, y * 3.2 - t * 2.6));
                float xx = x + (n1 - 0.5) * (0.25 + y * 1.1);
                // each lobe reaches a different height, so the top breaks into separate tongues
                float lobe = n2 * 0.6 + n1 * 0.4;
                float reach = smoothstep(1.0, 0.12, y + (0.62 - lobe) * 0.9);
                float width = lerp(0.92, 0.03, pow(y, 0.75));
                float body = smoothstep(width + 0.04, width - 0.22, abs(xx));
                float a = body * reach * smoothstep(0.0, 0.05, y);
                // temperature: white-yellow core low and central, orange mid, deep red + soot at the tips
                float heat = saturate(1.0 - y * 1.25 + (n2 - 0.5) * 0.5 - abs(xx) * 1.1);
                float3 c = lerp(float3(0.55, 0.06, 0.0), float3(1.0, 0.42, 0.06), smoothstep(0.0, 0.45, heat));
                c = lerp(c, float3(1.0, 0.78, 0.28), smoothstep(0.45, 0.8, heat));
                c = lerp(c, float3(1.0, 0.93, 0.7), smoothstep(0.82, 1.0, heat) * 0.7);
                c = lerp(c, float3(0.12, 0.02, 0.0), smoothstep(0.55, 0.95, y) * 0.55);
                float k = min(_Intensity, 1.25) * smoothstep(0.1, 0.55, i.facing);
                return fixed4(ToOutput(c) * k * 1.05, a * (0.45 + 0.55 * heat) * k);
            }
            ENDCG
        }
    }
}
