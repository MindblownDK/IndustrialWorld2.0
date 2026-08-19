// Procedural additive solar glare and restrained lens-ghost rings.
Shader "VoxelEngine/SolarGlareURP"
{
    Properties
    {
        _Tint ("Tint", Color) = (1.0, 0.9, 0.68, 1)
        _Opacity ("Opacity", Range(0,1)) = 1
        _Mode ("Mode", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SolarGlare"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Opacity;
                float _Mode;
            CBUFFER_END

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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float pulse = 0.992 + sin(_Time.y * 0.47) * 0.008;

                float halo = pow(saturate(1.0 - radius), 2.8);
                float core = 1.0 - smoothstep(0.018, 0.16, radius);
                float horizontal = pow(saturate(1.0 - abs(p.y)), 70.0)
                                 * pow(saturate(1.0 - abs(p.x)), 1.8);
                float vertical = pow(saturate(1.0 - abs(p.x)), 110.0)
                               * pow(saturate(1.0 - abs(p.y)), 3.2);
                float mainShape = halo * 0.54 + core * 1.7 + horizontal * 0.18 + vertical * 0.055;

                float ringOuter = 1.0 - smoothstep(0.72, 0.92, radius);
                float ringInner = smoothstep(0.28, 0.52, radius);
                float ghostDisc = pow(saturate(1.0 - radius), 1.5) * 0.30;
                float ghostShape = ringOuter * ringInner * 0.55 + ghostDisc;

                float shape = lerp(mainShape, ghostShape, step(0.5, _Mode));
                float alpha = saturate(shape * _Opacity * pulse);
                float3 color = _Tint.rgb * lerp(1.0, 0.72, radius) * (0.85 + core * 0.55);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
