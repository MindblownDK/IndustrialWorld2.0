// Assets/Scripts/VoxelEngine/Rendering/VolumetricWaterPost.shader
//
// Dedicated Full-Screen Post-Processing Shader for VolumetricWaterRenderFeature.
// Assign this shader to the 'blitShader' field on pc_renderer in Unity Inspector.
// Delivers AAA ocean lens polish, caustics grading, and chromatic aberration.

Shader "VoxelEngine/VolumetricWaterPost"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "VolumetricWaterPost"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            float _UnderwaterCA;
            float4 _UnderwaterFogColor;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_UnderwaterCA > 0.5f)
                {
                    float2 distFromCenter = uv - 0.5f;
                    float caShift = dot(distFromCenter, distFromCenter) * 0.015f;
                    half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(caShift, caShift)).r;
                    half g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                    half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(caShift, caShift)).b;
                    half3 col = half3(r, g, b);

                    // Deep sapphire volume grading
                    col = lerp(col, _UnderwaterFogColor.rgb * 1.25f, 0.18f);
                    return half4(col, 1.0);
                }
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            }
            ENDHLSL
        }
    }
}
