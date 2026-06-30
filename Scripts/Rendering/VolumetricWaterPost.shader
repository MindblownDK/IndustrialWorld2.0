Shader "VoxelEngine/VolumetricWaterPost"
{
    Properties
    {
        _UnderwaterFogColor ("Underwater Fog Color", Color) = (0.03, 0.14, 0.35, 1)
        _UnderwaterPostStrength ("Underwater Post Strength", Range(0, 1)) = 0
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "PlanetWaterPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _UnderwaterFogColor;
                float _UnderwaterPostStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            float3 ApplyWaterFog(float2 uv, float3 source)
            {
                float rawDepth = SampleSceneDepth(uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float depthFog = saturate(eyeDepth / 36.0) * saturate(_UnderwaterPostStrength);

                float2 centered = uv * 2.0 - 1.0;
                float vignette = saturate(dot(centered, centered) * 0.22);
                float distortion = sin((uv.y + _Time.y * 0.15) * 80.0) * 0.0015 * _UnderwaterPostStrength;
                float3 fogged = lerp(source, _UnderwaterFogColor.rgb, saturate(depthFog + vignette * 0.12));
                fogged.rgb += distortion;
                fogged.g *= 1.02;
                fogged.b *= 1.05;
                return fogged;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float3 col = ApplyWaterFog(input.uv, src.rgb);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
