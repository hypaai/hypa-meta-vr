// Additive textured glow (fire halo, light pools on the ground, the portal ring). No fog: light shouldn't turn fog-coloured.
Shader "HypaStories/Additive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Float) = 1
        _EnvironmentDepthBias ("Environment Depth Bias (room)", Float) = 0.06
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Opacity, _EnvironmentDepthBias;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; META_DEPTH_VERTEX_OUTPUT(1) UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(o, v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 c = fixed4(_Color.rgb * tex.rgb, tex.a * _Color.a * _Opacity);
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(i, c, _EnvironmentDepthBias); // a real table or body in front hides the glow (room mode)
                return c;
            }
            ENDCG
        }
    }
}
