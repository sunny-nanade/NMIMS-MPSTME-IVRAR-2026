// Screen-space overlay drawn in clip space so both eyes see identical coverage (no stereo parallax).
// _Aperture: radius (0..1 of the view) that stays clear. Use a negative value for a uniform tint.
Shader "RescueSim/ComfortVignette"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0, 0, 0, 1)
        _Aperture ("Aperture", Range(-1, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Vignette"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Aperture;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 ndc : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float2 xy = input.positionOS.xy * 2.0; // built-in Quad spans -0.5..0.5
                output.positionCS = float4(xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                output.ndc = xy;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float r = length(input.ndc);
                half mask = smoothstep(_Aperture, _Aperture + 0.35, r);
                return half4(_BaseColor.rgb, _BaseColor.a * mask);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
