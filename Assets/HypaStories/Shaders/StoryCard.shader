// A story card: ONE quad, ONE shader (port of the prototype's card.js). _Theme 0 = lantern in the pines, 1 = noir crime scene.
// The pinch ray's hit point drives _Pointer / _Hover (ripples, parallax, sheen), _Press is the ring burst on Begin,
// _Dim fades the cards that aren't focused and _Reveal grows the card out of its light source.
Shader "HypaStories/StoryCard"
{
    Properties
    {
        _Theme ("Theme", Float) = 0
        _Hover ("Hover", Range(0, 1)) = 0
        _Press ("Press", Range(0, 1)) = 0
        _Reveal ("Reveal", Range(0, 1)) = 1
        _Dim ("Dim", Range(0, 1)) = 0
        _Aspect ("Aspect (w/h)", Float) = 0.6667
        _Pointer ("Pointer uv", Vector) = (0.5, 0.35, 0, 0)
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "HypaStoriesCommon.cginc"

            float _Theme, _Hover, _Press, _Reveal, _Dim, _Aspect;
            float4 _Pointer;

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

            float pines(float x, float seed, float sharp)
            {
                float c = abs(frac(x * sharp + seed) - 0.5) * 2.0;
                float v = abs(frac(x * sharp * 3.17 + seed * 2.0) - 0.5) * 2.0;
                return c * 0.075 + v * 0.03 + sin(x * 5.0 + seed * 9.0) * 0.03;
            }

            float3 lanternArt(float2 w, float2 par, float t, float2 asp, float2 lp)
            {
                float2 q = float2(fbm(w * 3.0 + t * 0.04), fbm(w * 3.0 + float2(5.2, 1.3) - t * 0.05));
                float f = fbm(w * 3.0 + 3.5 * q + par * 2.0 + t * 0.03);
                float3 ember = float3(1.0, 0.55, 0.2);
                float3 col = lerp(float3(0.015, 0.05, 0.07), float3(0.08, 0.28, 0.31), smoothstep(0.25, 0.85, f) * (0.6 + 0.4 * w.y));
                col += ember * 0.12 * smoothstep(0.55, 0.95, f) * (0.3 + _Hover * 0.7);
                float2 l2 = lp + par * 1.5 + float2(0.0, sin(t * 0.7) * 0.005);
                float ld = length((w - l2) * asp);
                float flick = 0.9 + 0.1 * sin(t * 9.0) * sin(t * 3.7);
                col += ember * (exp(-ld * 11.0) * 0.9 + exp(-ld * 3.5) * 0.25) * flick * (0.75 + _Hover * 0.5 + _Press * 0.8);
                col += float3(1.0, 0.9, 0.7) * smoothstep(0.024, 0.0, ld) * 1.1;
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float fk = (float)k;
                    float top = 0.56 - fk * 0.07 + pines(w.x + par.x * (0.6 + fk * 0.9), fk * 3.1, 6.0 - fk);
                    float m = smoothstep(0.004, -0.004, w.y - top);
                    float3 lc = lerp(float3(0.03, 0.1, 0.12), float3(0.006, 0.012, 0.016), fk / 2.0);
                    lc += ember * exp(-length((w - l2) * asp) * 4.0) * 0.05 * (1.0 - fk * 0.3);
                    col = lerp(col, lc, m);
                    col = lerp(col, float3(0.10, 0.24, 0.27), 0.16 * (1.0 - fk / 2.0) * smoothstep(0.0, 0.35, w.y) * m * vn(w * 4.0 + t * 0.05));
                }
                return col;
            }

            float3 noirArt(float2 w, float2 par, float t, float2 asp)
            {
                float f = fbm(w * float2(2.0, 3.0) + float2(t * 0.03, -t * 0.02));
                float3 col = lerp(float3(0.012, 0.016, 0.03), float3(0.06, 0.08, 0.13), smoothstep(0.2, 0.95, f) * w.y);
                float pr = pow(max(0.0, sin(t * 3.0)), 4.0), pb = pow(max(0.0, -sin(t * 3.0)), 4.0);
                float3 RED = float3(1.0, 0.1, 0.16), BLU = float3(0.12, 0.34, 1.0);
                col += RED * pr * exp(-length((w - float2(-0.05, 0.8)) * asp) * 2.6) * 0.55 * (0.8 + _Hover * 0.6);
                col += BLU * pb * exp(-length((w - float2(1.05, 0.8)) * asp) * 2.6) * 0.55 * (0.8 + _Hover * 0.6);
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float fk = (float)k;
                    float x = (w.x + par.x * (0.5 + fk * 0.9)) * (7.0 + fk * 3.5) + fk * 11.0;
                    float id = floor(x), fr = frac(x);
                    float top = 0.66 - fk * 0.05 + 0.12 * h21(float2(id, fk + 3.0));
                    float body = step(0.05, fr) * step(w.y, top);
                    float3 bc = lerp(float3(0.035, 0.045, 0.07), float3(0.006, 0.008, 0.012), fk / 2.0);
                    bc += RED * pr * 0.05 + BLU * pb * 0.05;
                    float2 cell = floor(float2(x * 5.0, w.y * 46.0));
                    float win = step(0.9, h21(cell + fk * 7.0)) * step(0.25, frac(x * 5.0)) * step(0.35, frac(w.y * 46.0)) * step(0.63, w.y);
                    col = lerp(col, bc + float3(1.0, 0.78, 0.4) * win * 0.55 * (1.0 - fk * 0.25), body);
                }
                float2 sl = float2(0.28, 0.84) + par * 1.2;
                float on = step(0.18, h21(float2(floor(t * 9.0), 1.0))) * (0.7 + 0.3 * sin(t * 40.0));
                float sd = length((w - sl) * asp);
                col += float3(1.0, 0.72, 0.35) * (exp(-sd * 13.0) * 0.9 + exp(-sd * 4.0) * 0.2) * on * (0.7 + _Hover * 0.5 + _Press * 0.8);
                col += float3(1.0, 0.9, 0.7) * smoothstep(0.02, 0.0, sd) * on;
                float ground = smoothstep(0.62, 0.45, w.y);
                float streak = vn(float2(w.x * 26.0, w.y * 1.4 + t * 0.1));
                col += (RED * pr + BLU * pb) * streak * ground * 0.4;
                float2 tp = w - float2(0.5, 0.0);
                float ang = -0.14 + sin(t * 0.8) * 0.012 * (1.0 + _Hover);
                float2 r = float2(cos(ang) * tp.x - sin(ang) * tp.y, sin(ang) * tp.x + cos(ang) * tp.y);
                float band = smoothstep(0.022, 0.016, abs(r.y - 0.73 + 0.02 * sin(r.x * 3.0 + t)));
                float stripe = step(0.5, frac(r.x * 10.0 + r.y * 10.0));
                col = lerp(col, lerp(float3(0.04, 0.035, 0.01), float3(1.0, 0.8, 0.08), stripe) * 0.75, band);
                float2 rp = float2(w.x * 70.0 + w.y * 14.0, w.y * 3.0 - t * 3.4);
                float rc = h21(floor(rp * float2(1.0, 6.0)));
                float rain = step(0.93, rc) * smoothstep(0.5, 0.0, abs(frac(rp.x) - 0.5)) * step(0.25, frac(rp.y * 6.0));
                col += float3(0.55, 0.7, 0.9) * rain * 0.09 * (1.0 - ground * 0.5);
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv, asp = float2(_Aspect, 1.0), p = (uv - 0.5) * asp;
                float t = _Time.y;
                float noir = step(0.5, _Theme);

                float d = sdRound(p, float2(_Aspect * 0.5, 0.5) - 0.006, 0.05) + (vn(uv * 6.0 + t * 0.3) - 0.5) * 0.005 * (0.4 + _Hover);
                float a = smoothstep(0.003, -0.003, d);

                float2 dv = (uv - _Pointer.xy) * asp; float dist = length(dv);
                float rip = sin(dist * 38.0 - t * 5.0) * exp(-dist * 5.5) * _Hover;
                float2 lp = noir > 0.5 ? float2(0.28, 0.84) : float2(0.6, 0.74);
                float2 bv = (uv - lp) * asp; float br = length(bv);
                float burst = sin(br * 30.0 - (1.0 - _Press) * 22.0) * exp(-br * 4.0) * _Press;
                float2 w = uv + normalize(dv + 1e-5) * rip * 0.012 + normalize(bv + 1e-5) * burst * 0.02;
                float2 par = (_Pointer.xy - 0.5) * _Hover * 0.04;

                float3 col = noir > 0.5 ? noirArt(w, par, t, asp) : lanternArt(w, par, t, asp, lp);
                float3 accent = noir > 0.5 ? float3(1.0, 0.25, 0.3) : float3(1.0, 0.55, 0.2);

                col *= lerp(0.6, 1.0, smoothstep(0.0, 0.5, uv.y));
                col += float3(1.0, 0.88, 0.7) * exp(-dist * 6.0) * _Hover * 0.12;
                float edge = smoothstep(0.008, 0.0, abs(d + 0.003));
                col += accent * edge * (0.12 + 0.6 * _Hover) * (0.6 + 0.4 * sin(atan2(dv.y, dv.x) + t));
                col *= 1.0 - smoothstep(-0.09, 0.0, d) * 0.35;
                col *= 1.0 - _Dim * 0.5;
                // the lower part sits under the title and description: darken it so the text stays readable
                col *= lerp(0.45, 1.0, smoothstep(0.08, 0.62, uv.y));

                float R = _Reveal * 1.5;
                float rv = 1.0 - smoothstep(R - 0.3, R, length((uv - lp) * asp * 1.4));
                return fixed4(ToOutput(col), a * rv * saturate(_Reveal * 1.6) * (1.0 - _Dim * 0.2));
            }
            ENDCG
        }
    }
}
