// The doorway (ticket 3L): a plain opening onto the place it leads to, with dark wooden trim and a soft shadow on the wall. No glow, no effects.
// One quad, bottom-anchored: the opening is _Size.xy minus the frame margin; the frame glows just outside it.
// _Window: when set (the live view of the hut, one picture per eye), the glass shows it instead of the painted glimpse.
Shader "HypaStories/Portal"
{
    Properties
    {
        _Size ("Opening width, height", Vector) = (0.95, 2.05, 0, 0)
        _Margin ("Frame margin", Float) = 0.14
        _Open ("Open (0 shut .. 1 open)", Range(0, 1)) = 1
        _Pull ("Pull (0 far .. 1 at the glass)", Range(0, 1)) = 0
        _Glow ("Trim colour (dark wood)", Color) = (0.2, 0.13, 0.08, 1)
        _Edge ("Edge glow colour", Color) = (1, 0.62, 0.28, 1)
        _WindowL ("Hut, left eye", 2D) = "black" {}
        _WindowR ("Hut, right eye", 2D) = "black" {}
        _UseWindow ("Use the live view", Range(0, 1)) = 0
        _Hole ("In a place: the opening shows your room (1)", Range(0, 1)) = 0
        _EnvironmentDepthBias ("Environment Depth Bias (room)", Float) = 0.06
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
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "UnityCG.cginc"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

            float4 _Size;
            float _Margin, _Open, _Pull, _UseWindow, _Hole, _EnvironmentDepthBias;
            fixed4 _Glow, _Edge;
            sampler2D _WindowL, _WindowR;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 scr : TEXCOORD1; META_DEPTH_VERTEX_OUTPUT(2) UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.scr = ComputeScreenPos(o.pos);
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(o, v.vertex);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float W = _Size.x, H = _Size.y, m = _Margin, t = _Time.y;
                // position in the doorway: x across from the middle, y up from the floor
                float2 p = float2((i.uv.x - 0.5) * (W + 2.0 * m), i.uv.y * (H + m));
                float inside = min(W * 0.5 - abs(p.x), H - p.y);     // > 0 in the opening
                float2 c = float2(0.0, H * 0.5);
                float2 q = (p - c) / float2(W * 0.5, H * 0.5);
                float r = length(q);

                // opening: a plain fade in
                float reveal = smoothstep(0.0, 1.0, _Open);
                if (reveal <= 0.001) discard;
                float outside = max(-inside, 0.0);
                float inner = max(inside, 0.0);
                if (_Hole > 0.5 && inside > 0.0) discard;           // in a place, the opening is a hole onto your real room (the other shader)

                // the view: the live picture, one per eye (the painted dusk only if there is none)
                float3 dusk = lerp(float3(0.30, 0.17, 0.08), float3(0.05, 0.08, 0.18), saturate(p.y / H));
                float2 hp = (p - float2(0.0, H * 0.3)) * float2(1.3, 1.0);
                float3 glimpse = dusk + float3(1.0, 0.55, 0.2) * exp(-dot(hp, hp) * 2.2) * 0.6;
                float2 suv = i.scr.xy / max(i.scr.w, 1e-4);
                float3 live = unity_StereoEyeIndex == 0 ? tex2D(_WindowL, suv).rgb : tex2D(_WindowR, suv).rgb;
                float3 col = lerp(glimpse, live, _UseWindow);
                // a little shade just inside the edge, where the opening has thickness (the reveal of a real doorway)
                col *= 1.0 - 0.28 * exp(-inner * 22.0);
                float alpha = inside > 0.0 ? 1.0 : 0.0;

                // the edges glow softly: a warm rim just inside the opening and a halo just outside it, breathing slowly
                float breathe = 0.85 + 0.15 * sin(t * 1.6);
                float glowIn = exp(-inner * 14.0) * 1.35 * breathe;
                float glowOut = exp(-outside * 9.0) * 1.3 * breathe;
                col += _Edge.rgb * glowIn;

                // a thin dark trim right at the edge, so it still reads as a door
                const float trim = 0.03;
                if (inside <= 0.0)
                {
                    float grain = 0.88 + 0.12 * noise(float2((abs(p.x) > W * 0.5 ? p.y : p.x) * 7.0, (abs(p.x) > W * 0.5 ? p.x : p.y) * 3.0));
                    float inTrim = outside < trim ? 1.0 : 0.0;
                    float3 wood = _Glow.rgb * grain;
                    float fade = 1.0 - smoothstep(m * 0.35, m, outside);                                // gone by the quad's edge
                    col = wood * inTrim + _Edge.rgb * glowOut * 1.3;
                    alpha = saturate(max(inTrim, glowOut)) * fade;
                    if (p.y > H + m) alpha = 0.0;
                }

                fixed4 o = fixed4(col, alpha * reveal);
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(i, o, _EnvironmentDepthBias); // a real chair in front of the doorway hides it
                return o;
            }
            ENDCG
        }
    }
}
