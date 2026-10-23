// URP HLSL, head-locked overlay quad: full-screen fade (_Fade) and radial comfort vignette (_Strength).
Shader "Fractured_Final/ComfortFade"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _Fade ("Fade", Range(0,1)) = 0
        _Strength ("Vignette Strength", Range(0,1)) = 0
        _Inner ("Vignette Inner Radius", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay+100" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Overlay"
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Fade;
                float _Strength;
                float _Inner;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 c = IN.uv - 0.5;
                float r = length(c) * 2.0;
                float vig = saturate((r - _Inner) / max(0.001, 1.0 - _Inner)) * _Strength;
                float a = saturate(max(_Fade, vig));
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
