// Scanned props (rocks, logs, the stump) on Quest: Lambert with the base colour texture only, instead of the glTF PBR shader
// (normal, metallic-roughness and occlusion maps per pixel). The fire and other point lights are added per vertex (noforwardadd),
// so each prop is one draw call. Swapped in by PropDiet when the camp spawns a prop.
Shader "HypaStories/Prop"
{
    Properties
    {
        _MainTex ("Base colour", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        CGPROGRAM
        #pragma surface surf Lambert noforwardadd noshadow nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb;
        }
        ENDCG
    }
    FallBack "Mobile/VertexLit"
}
