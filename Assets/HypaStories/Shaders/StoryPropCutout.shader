// Scanned foliage cards (ferns, grass) on Quest: alpha-tested Lambert with the base colour texture only, both sides, instead of the
// glTF PBR shader (the grass was alpha-blended: sorted, and every overlapping blade blended). Point lights per vertex (noforwardadd):
// one draw call per card mesh. Swapped in by PropDiet when the camp spawns a prop.
Shader "HypaStories/PropCutout"
{
    Properties
    {
        _MainTex ("Base colour", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "IgnoreProjector" = "True" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Lambert alphatest:_Cutoff noforwardadd noshadow nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
