// Night sky on an inside-out sphere around the camp: horizon-to-zenith gradient, moon with halo, twinkling stars, lightning flash.
// Port of the prototype's sky quad (Ideation/src/shaders.js skyMaterial) to all directions, since in VR you can look anywhere.
Shader "HypaStories/Sky"
{
    Properties
    {
        _MoonDir ("Moon direction", Vector) = (0.25, 0.45, 1, 0)
        _Flash ("Lightning", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Front ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "HypaStoriesCommon.cginc"

            float4 _MoonDir;
            float _Flash;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float t = smoothstep(-0.08, 0.65, d.y);
                float3 c = lerp(float3(0.07, 0.17, 0.21), float3(0.01, 0.025, 0.06), t);

                float ang = degrees(acos(clamp(dot(d, normalize(_MoonDir.xyz)), -1.0, 1.0)));
                c += float3(0.25, 0.42, 0.5) * exp(-ang * 0.12) * 0.55;
                c = lerp(c, float3(0.86, 0.93, 0.95), smoothstep(1.9, 1.65, ang));

                float3 sp = d * 160.0;
                float3 id = floor(sp);
                float rnd = h31(id);
                float star = step(0.985, rnd) * smoothstep(0.42, 0.0, length(frac(sp) - 0.5));
                star *= 0.5 + 0.5 * sin(_Time.y * (1.0 + rnd * 3.0) + rnd * 60.0);
                c += star * smoothstep(0.08, 0.35, d.y) * float3(0.8, 0.9, 1.0);

                c += float3(0.55, 0.65, 1.0) * _Flash * (0.35 + 0.65 * t);
                return fixed4(ToOutput(c), 1.0);
            }
            ENDCG
        }
    }
}
