// Tiny soft additive motes used by the sparse vacuum dust field.
Shader "VoxelEngine/SpaceDustURP"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.62, 0.72, 0.86, 1)
        _Opacity ("Opacity", Range(0,1)) = 0.24
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SpaceDust"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float soft = pow(saturate(1.0 - radius), 2.2);
                float core = 1.0 - smoothstep(0.0, 0.18, radius);
                float alpha = (soft * 0.72 + core * 0.28) * _Opacity * input.color.a;
                clip(alpha - 0.002);
                return half4(_Tint.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
