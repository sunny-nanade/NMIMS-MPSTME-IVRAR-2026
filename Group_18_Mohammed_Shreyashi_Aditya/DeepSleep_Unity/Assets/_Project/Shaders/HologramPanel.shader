Shader "Fractured_Final/HologramPanel"
{
    // Unlit additive-looking holographic panel for the guide HUD: tinted glass, scanlines, bright edge, soft flicker, _Reveal fades the whole panel in and out.
    Properties
    {
        _Color ("Tint", Color) = (0.2, 0.8, 1.0, 1)
        _Alpha ("Panel Alpha", Range(0,1)) = 0.28
        _Reveal ("Reveal", Range(0,1)) = 1
        _Scan ("Scanline Count", Float) = 90
        _Edge ("Edge Width", Range(0.001,0.1)) = 0.012
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Holo"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
                half _Reveal;
                float _Scan;
                float _Edge;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;
                // reveal sweeps from the centre line outwards
                float reach = abs(uv.y - 0.5) * 2.0;
                float visible = saturate((_Reveal * 1.3 - reach * 0.3) );
                float scan = 0.65 + 0.35 * sin((uv.y * _Scan - t * 1.4) * 6.2831853);
                float flick = 0.94 + 0.06 * sin(t * 37.0) * sin(t * 11.0);
                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float edge = 1.0 - smoothstep(0.0, _Edge, min(edgeX * 0.55, edgeY));
                float vign = 0.55 + 0.45 * (1.0 - reach * reach);
                float a = (_Alpha * scan * vign + edge * 0.85) * flick * visible;
                half3 col = _Color.rgb * (0.7 + edge * 1.4);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
