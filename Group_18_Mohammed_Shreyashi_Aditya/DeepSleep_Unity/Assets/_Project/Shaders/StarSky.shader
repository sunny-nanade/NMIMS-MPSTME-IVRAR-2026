Shader "Fractured_Final/StarSky"
{
    // Procedural deep-space skybox: hash-based stars with a slow twinkle and two soft nebula tints. No textures, so it costs no memory on Quest.
    Properties
    {
        _Deep ("Deep Colour", Color) = (0.004, 0.006, 0.02, 1)
        _NebulaA ("Nebula A", Color) = (0.05, 0.10, 0.28, 1)
        _NebulaB ("Nebula B", Color) = (0.22, 0.05, 0.12, 1)
        _StarDensity ("Star Density", Range(20, 400)) = 160
        _StarBrightness ("Star Brightness", Range(0, 4)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep;
                half4 _NebulaA;
                half4 _NebulaB;
                float _StarDensity;
                float _StarBrightness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x); float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 cell = d * _StarDensity;
                float3 id = floor(cell);
                float3 local = frac(cell) - 0.5;
                float h = Hash(id);
                float star = step(0.985, h) * smoothstep(0.35, 0.0, length(local));
                float tw = 0.75 + 0.25 * sin(_Time.y * (1.0 + h * 3.0) + h * 40.0);
                float neb = Noise(d * 2.2) * 0.6 + Noise(d * 5.0 + 7.0) * 0.4;
                half3 col = _Deep.rgb;
                col += _NebulaA.rgb * smoothstep(0.35, 0.85, neb);
                col += _NebulaB.rgb * smoothstep(0.55, 0.95, Noise(d * 1.7 + 13.0));
                col += star * tw * _StarBrightness * lerp(half3(0.8, 0.9, 1.0), half3(1.0, 0.85, 0.7), frac(h * 91.0));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
