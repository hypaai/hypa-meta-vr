// The fall through the portal (ticket 3L): a tunnel of light all round you, rushing upward as you drop, with a warm glow far below.
// Drawn on a sphere that follows your head (never turns with it), so looking around shows the tunnel from every side.
Shader "HypaStories/Fall"
{
    Properties
    {
        _Amount ("Amount (0 gone .. 1 all round you)", Range(0, 1)) = 0
        _Phase ("How far you have fallen", Float) = 0
        _Rush ("Rush (0 drifting .. 1 flat out)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Front ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Amount, _Phase, _Rush;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            // noise that repeats every `per` cells across x, so there is no seam where the angle wraps round the shaft
            float noiseT(float2 p, float per)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float x0 = fmod(i.x + per * 8.0, per), x1 = fmod(i.x + 1.0 + per * 8.0, per);
                return lerp(lerp(hash(float2(x0, i.y)), hash(float2(x1, i.y)), f.x), lerp(hash(float2(x0, i.y + 1.0)), hash(float2(x1, i.y + 1.0)), f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float radial = max(sqrt(1.0 - d.y * d.y), 0.04);   // 0 straight down or up the shaft, 1 level with you
                float along = d.y / radial;                         // how far up or down the wall this direction meets (tunnel of radius 1)
                float theta = atan2(d.z, d.x);
                float z = along - _Phase;                           // the wall rushes up past you as you fall
                float z2 = along - _Phase * 0.55;                   // a second, slower layer: parallax, so it feels deep
                float tau = 6.2831853;

                // thin speed lines stretched along the fall (fine round the shaft, very long along it), two layers
                float n1 = noiseT(float2(theta / tau * 90.0 + 45.0, z * (0.10 - 0.07 * _Rush)), 90.0);
                float n2 = noiseT(float2(theta / tau * 45.0 + 22.5, z2 * 0.18), 45.0);
                float lines = pow(n1, 9.0) * 3.2 + pow(n2, 7.0) * 1.8;
                // rings of light flashing past; spacing tightens and they brighten as you speed up
                float rings = pow(abs(sin(z * (1.6 + 1.6 * _Rush))), 30.0) * (0.6 + 1.2 * _Rush);
                float flecks = step(0.965, noiseT(float2(theta / tau * 70.0 + 35.0, z * 0.5), 70.0)) * 1.6;

                // deep dusk wall; a hot glow far below (where you are headed), a cold pin of light far above (where you came from)
                float3 wall = lerp(float3(0.02, 0.015, 0.06), float3(0.16, 0.07, 0.04), saturate(0.5 - 0.5 * d.y));
                float below = pow(saturate(-d.y), 10.0);
                float above = pow(saturate(d.y), 60.0);
                float edge = smoothstep(1.0, 0.0, radial);          // toward the poles: the rushing centre
                float3 light = float3(1.0, 0.66, 0.3);
                float3 col = wall + light * (lines + rings + flecks) * (0.35 + 0.9 * radial)
                           + light * below * (1.3 + 1.2 * _Rush) + float3(0.6, 0.7, 1.0) * above;
                // the periphery darkens and the lines pile up toward the glow: the tunnel narrows ahead of you
                col *= 0.55 + 0.9 * _Rush + 0.4 * edge;
                col += light * pow(edge, 3.0) * _Rush * 0.5;
                return fixed4(col, _Amount);
            }
            ENDCG
        }
    }
}
