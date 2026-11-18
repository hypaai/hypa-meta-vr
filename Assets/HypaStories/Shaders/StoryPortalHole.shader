// The doorway while you are in a place (ticket 3L): the opening is a hole in the picture, so the real room shows through it (passthrough runs
// underneath the app and the camera clears to see-through). Drawn on the same quad as the doorway, after its frame. The size comes from the same
// numbers as HypaStories/Portal.
Shader "HypaStories/PortalHole"
{
    Properties
    {
        _Size ("Opening width, height", Vector) = (0.95, 2.05, 0, 0)
        _Margin ("Frame margin", Float) = 0.3
        _Open ("Open (0 shut .. 1 open)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One Zero
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Size;
            float _Margin, _Open;

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

            fixed4 frag(v2f i) : SV_Target
            {
                float W = _Size.x, H = _Size.y, m = _Margin;
                float2 p = float2((i.uv.x - 0.5) * (W + 2.0 * m), i.uv.y * (H + m));
                float inside = min(W * 0.5 - abs(p.x), H - p.y);
                if (inside <= 0.0 || _Open < 0.5) discard;
                return fixed4(0, 0, 0, 0); // nothing drawn here, and see-through: the room
            }
            ENDCG
        }
    }
}
