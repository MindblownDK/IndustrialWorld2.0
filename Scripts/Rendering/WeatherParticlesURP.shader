// Soft alpha-blended material for the runtime-authored weather particle systems
// (rain streaks, snow flakes, splash puffs). The particle colour over lifetime
// rides the vertex colour; _TintColor is the per-system base tint. Particle shape
// is drawn procedurally from the billboard UV (same approach as SpaceDustURP, the
// project's proven particle path): _ShapeMode 0 draws a soft round dot (snow /
// splash), 1 draws a vertical streak (rain). No texture sampling, so there is
// nothing to mis-bind. Fog is applied so distant rain fades into the storm haze.
Shader "VoxelEngine/WeatherParticlesURP"
{
    Properties
    {
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _ShapeMode ("Shape (0 = dot, 1 = streak)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "WeatherParticles"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _ShapeMode;
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
                float fogCoord : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Billboard UV spans 0..1 across the quad; remap so 0,0 is the centre.
                float2 p = input.uv * 2.0 - 1.0;

                // Soft round dot (snow / splash).
                float radius = length(p);
                float dotAlpha = pow(saturate(1.0 - radius), 1.8);

                // Vertical streak (rain): bright spine, soft horizontal edges, soft tips.
                float edgeX = 1.0 - smoothstep(0.0, 0.4, abs(p.x));
                float tipY  = 1.0 - smoothstep(0.7, 1.0, abs(p.y));
                float streakAlpha = edgeX * tipY;

                float shape = lerp(dotAlpha, streakAlpha, saturate(_ShapeMode));
                half alpha = shape * input.color.a * _TintColor.a;
                clip(alpha - 0.003);

                half3 rgb = input.color.rgb * _TintColor.rgb;
                rgb = MixFog(rgb, input.fogCoord);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
