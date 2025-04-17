Shader "Custom/Outline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Range(0.0, 0.1)) = 0.005
    }

    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" }
        
        // First pass - render the outline
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            float _OutlineWidth;
            fixed4 _OutlineColor;

            v2f vert(appdata v)
            {
                v2f o;
                float3 normal = normalize(v.normal);
                float3 outlineOffset = normal * _OutlineWidth;
                float3 position = v.vertex + outlineOffset;
                o.pos = UnityObjectToClipPos(position);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
} 