// URP HLSL, inverted-hull outline. Added as an extra material on hovered Grabbables.
Shader "Fractured_Final/HoverOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.3, 0.9, 1.0, 1)
        _Width ("Width (m)", Range(0.0, 0.05)) = 0.006
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                // width given in world metres: convert along the world-space normal
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                posWS += nWS * _Width;
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
