// Procedural pines (StoryPines): vertex-coloured Lambert, swaying with the story's wind (global _StoryWind, 0..1).
// The sway grows with height, like the prototype's tree rotation.
Shader "HypaStories/Pine"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Sway ("Sway (rad)", Float) = 0.005
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert vertex:vert noshadow nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0

        fixed4 _Color;
        float _Sway, _StoryWind;

        struct Input { float4 color : COLOR; };

        void vert(inout appdata_full v)
        {
            float ph = dot(unity_ObjectToWorld._m03_m23, float2(0.37, 0.21));
            v.vertex.x += sin(_Time.y * (0.45 + _StoryWind * 0.9) + ph) * _Sway * (1.0 + _StoryWind * 4.0) * v.vertex.y * v.vertex.y * 0.15;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = IN.color.rgb * _Color.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
