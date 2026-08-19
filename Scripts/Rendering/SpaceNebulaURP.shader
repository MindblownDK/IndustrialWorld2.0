// Soft additive nebula billboard. Particles supply a large world-facing quad;
// the shader paints a wispy cloud from UV + a slow swirl so the field feels
// like a galactic veil rather than a hard sprite.
Shader "VoxelEngine/SpaceNebulaURP"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.35, 0.22, 0.62, 0.35)
        _Primary ("Primary", Color) = (0.42, 0.22, 0.72, 1)
        _Secondary ("Secondary", Color) = (0.16, 0.40, 0.72, 1)
        _Opacity ("Opacity", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SpaceNebula"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Primary;
                float4 _Secondary;
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

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                UNITY_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    v += a * noise(p);
                    p = p * 2.07 + 13.17;
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * 2.0 - 1.0;
                float r = length(uv);
                if (r > 1.0) discard;

                float2 swirl = uv;
                float ang = 0.35 * _Time.y;
                float s = sin(ang);
                float c = cos(ang);
                swirl = float2(c * swirl.x - s * swirl.y, s * swirl.x + c * swirl.y);

                float n = fbm(swirl * 2.4 + 8.0);
                float falloff = saturate(1.0 - r);
                falloff *= falloff;
                float mask = saturate(n * 1.35) * falloff;

                float3 col = lerp(_Primary.rgb, _Secondary.rgb, saturate(n * 1.2));
                col = lerp(col, _BaseColor.rgb, 0.25);
                float alpha = mask * input.color.a * _BaseColor.a * _Opacity;
                if (alpha < 0.004) discard;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
