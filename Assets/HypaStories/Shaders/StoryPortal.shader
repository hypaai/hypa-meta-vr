// The portal to the hut (ticket 3L): a doorway of warm light with a glass-like skin that ripples and pulls as you come near.
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
        _Glow ("Frame colour", Color) = (1, 0.55, 0.18, 1)
        _WindowL ("Hut, left eye", 2D) = "black" {}
        _WindowR ("Hut, right eye", 2D) = "black" {}
        _UseWindow ("Use the live view", Range(0, 1)) = 0
        _Hint ("Faint frame while shut (0 none .. 1 shown)", Range(0, 1)) = 0
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
            float _Margin, _Open, _Pull, _UseWindow, _Hint, _EnvironmentDepthBias;
            fixed4 _Glow;
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

                // opening: an iris from the middle
                float reveal = smoothstep(0.0, 0.25, _Open * 1.7 - r);
                // shut but armed: only a faint ember outline, so you know the door is live (it brightens as you come near)
                float hint = _Hint * (1.0 - _Open) * (0.55 + 0.25 * sin(t * 2.0) + 0.5 * _Pull);
                if (reveal <= 0.001 && (hint <= 0.001 || inside > 0.0)) discard;

                // the skin: slow ripples, stronger as you come near
                float amp = 0.25 + 0.75 * _Pull;
                float ripple = sin(r * 9.0 - t * (2.2 + 3.0 * _Pull) + noise(q * 3.0 + t * 0.3) * 3.0) * 0.5 + 0.5;

                // the painted glimpse of the hut: dusk above, a warm hearth glow low in the middle
                float3 dusk = lerp(float3(0.30, 0.17, 0.08), float3(0.05, 0.08, 0.18), saturate(p.y / H));
                float hearth = exp(-dot((p - float2(0.0, H * 0.3)) * float2(1.3, 1.0), (p - float2(0.0, H * 0.3)) * float2(1.3, 1.0)) * 2.2);
                float3 glimpse = dusk + float3(1.0, 0.55, 0.2) * hearth * 0.9;
                glimpse *= 0.85 + 0.3 * ripple * amp;

                // the live view, when there is one (one picture per eye)
                float2 suv = i.scr.xy / max(i.scr.w, 1e-4);
                suv += (ripple - 0.5) * 0.006 * amp; // the glass bends the view
                float3 live = unity_StereoEyeIndex == 0 ? tex2D(_WindowL, suv).rgb : tex2D(_WindowR, suv).rgb;
                float3 view = lerp(glimpse, live, _UseWindow);

                // pulled toward you: streaks of light run in from the edge, and the glass brightens
                float ang = atan2(q.y, q.x);
                float streak = pow(abs(sin(ang * 14.0 + t * 2.0 + noise(float2(ang * 3.0, t)) * 4.0)), 10.0) * smoothstep(0.2, 1.0, r);
                float3 col = view + float3(1.0, 0.72, 0.4) * (streak * _Pull * 0.9 + _Pull * 0.25);

                // the edge of the opening glows into the frame
                float edge = exp(-max(inside, 0.0) * 9.0);
                col += _Glow.rgb * edge * (0.7 + 0.5 * ripple);
                float alpha = inside > 0.0 ? lerp(0.9, 1.0, _Pull) : 0.0;

                // the frame: a band of ember light just outside the opening, with sparks
                float band = saturate(1.0 + inside / m); // 1 at the opening's edge .. 0 at the margin
                float spark = step(0.965, noise(p * 38.0 + float2(0.0, -t * 1.5)));
                float3 frame = _Glow.rgb * (pow(band, 2.2) * (0.9 + 0.4 * sin(t * 3.0 + p.y * 5.0)) + spark * 1.2);
                if (inside <= 0.0) { col = frame; alpha = saturate(pow(band, 1.6) + spark) * (p.y <= H + m ? 1.0 : 0.0); }

                fixed4 o = fixed4(col, alpha * (inside > 0.0 ? reveal : max(reveal, hint)));
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(i, o, _EnvironmentDepthBias); // a real chair in front of the doorway hides it
                return o;
            }
            ENDCG
        }
    }
}
