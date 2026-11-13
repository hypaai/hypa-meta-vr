// Stories UI surface: a rounded rectangle drawn with a signed distance field, so edges stay crisp at any distance in the headset.
// Vertical gradient fill, hairline border, soft drop shadow, and an outer glow for hover / focus. Premultiplied alpha.
// The quad is larger than the rectangle by _Margin on every side (room for the shadow and glow). Sizes are in metres.
Shader "HypaStories/UIPanel"
{
    Properties
    {
        _Size ("Quad size (m)", Vector) = (0.5, 0.3, 0, 0)
        _Margin ("Margin (m)", Float) = 0.025
        _Radius ("Corner radius (m)", Float) = 0.03
        _Top ("Fill top", Color) = (0.09, 0.075, 0.065, 0.9)
        _Bottom ("Fill bottom", Color) = (0.05, 0.04, 0.035, 0.9)
        _Border ("Border", Color) = (1, 0.85, 0.66, 0.16)
        _BorderWidth ("Border width (m)", Float) = 0.0012
        _Glow ("Glow", Color) = (1, 0.6, 0.25, 0)
        _Shadow ("Shadow", Float) = 0.35
        _Opacity ("Opacity", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Size, _Top, _Bottom, _Border, _Glow;
            float _Margin, _Radius, _BorderWidth, _Shadow, _Opacity;

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

            float sdRound(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }

            // "over" in premultiplied alpha
            void Over(inout float4 dst, float3 c, float a) { dst.rgb = c * a + dst.rgb * (1.0 - a); dst.a = a + dst.a * (1.0 - a); }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _Size.xy;
                float2 b = _Size.xy * 0.5 - _Margin;
                float r = min(_Radius, min(b.x, b.y));
                float d = sdRound(p, b, r);
                float aa = max(fwidth(d), 1e-5);
                float4 o = 0;

                float ds = sdRound(p - float2(0, -0.005), b, r);
                Over(o, 0, (1.0 - smoothstep(-0.004, _Margin * 0.8, ds)) * _Shadow * 0.6);
                float glow = exp(-max(d, 0.0) / (_Margin * 0.35)) * step(0.0, d);
                Over(o, _Glow.rgb, glow * _Glow.a);

                float inside = 1.0 - smoothstep(-aa, aa, d);
                float4 fill = lerp(_Bottom, _Top, i.uv.y);
                Over(o, fill.rgb, fill.a * inside);
                float ring = 1.0 - smoothstep(-aa, aa, abs(d + _BorderWidth * 0.5) - _BorderWidth * 0.5);
                float4 border = lerp(_Border, float4(_Glow.rgb, max(_Border.a, _Glow.a)), _Glow.a);
                Over(o, border.rgb, border.a * ring);
                return o * _Opacity;
            }
            ENDCG
        }
    }
}
