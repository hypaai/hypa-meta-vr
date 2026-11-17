Shader "HypaStories/DrumSurface"
{
    Properties
    {
        _MainTex ("Natural detail", 2D) = "gray" {}
        _Surface ("Food 0, wood 1, hide 2", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf BlinnPhong vertex:vert noshadow nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0
        sampler2D _MainTex;
        float _Surface;
        struct Input { float4 color : COLOR; float3 localPos; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.color = v.color;
            o.localPos = v.vertex.xyz;
        }
        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 p = IN.localPos;
            float2 uv = p.xz * 7.0;
            if (_Surface > 0.5 && _Surface < 1.5)
                uv = float2((p.x + p.z) * 3.0, p.y * 0.7);
            fixed3 detail = tex2D(_MainTex, uv).rgb;
            half variation = 0.88 + detail.r * 0.16 + detail.g * 0.04;
            if (_Surface > 0.5 && _Surface < 1.5)
                variation = 0.78 + detail.b * 0.25 + detail.g * 0.035;
            o.Albedo = IN.color.rgb * variation;
            o.Specular = 0.18;
            o.Gloss = _Surface > 1.5 ? 0.07 : 0.18;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
