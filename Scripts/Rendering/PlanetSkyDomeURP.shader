// Camera-relative inverted sky dome. Paints a planet-specific zenith / horizon /
// sunset gradient plus optional aurora bands and dust haze. SpaceBlend fades the
// whole dome toward deep vacuum so the starfield and nebulae take over.
Shader "VoxelEngine/PlanetSkyDomeURP"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.18, 0.42, 0.78, 1)
        _Horizon ("Horizon", Color) = (0.72, 0.84, 0.92, 1)
        _Ground ("Ground Haze", Color) = (0.70, 0.80, 0.90, 1)
        _Night ("Night", Color) = (0.02, 0.03, 0.08, 1)
        _Sunset ("Sunset", Color) = (1.00, 0.55, 0.25, 1)
        _SunDir ("Sun Direction", Vector) = (0, 1, 0, 0)
        _RadialUp ("Radial Up", Vector) = (0, 1, 0, 0)
        _SpaceBlend ("Space Blend", Range(0,1)) = 0
        _DayFactor ("Day Factor", Range(0,1)) = 1
        _Haze ("Haze", Range(0,1)) = 0.28
        _Aurora ("Aurora", Range(0,1)) = 0
        _AuroraColorA ("Aurora A", Color) = (0.25, 0.95, 0.72, 1)
        _AuroraColorB ("Aurora B", Color) = (0.78, 0.28, 0.92, 1)
        _Dust ("Dust", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PlanetSkyDome"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Front
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith;
                float4 _Horizon;
                float4 _Ground;
                float4 _Night;
                float4 _Sunset;
                float4 _SunDir;
                float4 _RadialUp;
                float _SpaceBlend;
                float _DayFactor;
                float _Haze;
                float _Aurora;
                float4 _AuroraColorA;
                float4 _AuroraColorB;
                float _Dust;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.viewDir = normalize(world - _WorldSpaceCameraPos);
                return output;
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDir);
                float3 up = normalize(_RadialUp.xyz);
                float3 sun = normalize(_SunDir.xyz);

                float vertical = dot(dir, up);
                float skyHeight = saturate(vertical);
                float zenithMix = pow(smoothstep(0.015, 0.82, skyHeight), 0.78);
                float horizonWidth = lerp(3.8, 1.65, saturate(_Haze));
                float horizonBand = saturate(1.0 - abs(vertical) * horizonWidth);
                float ground = saturate(-vertical * 1.8);

                // At vertical == 0 the result is the authored horizon colour exactly.
                // The previous hemisphere remap put the horizon halfway toward zenith,
                // which let the generic blue skybox look survive around every planet.
                float3 color = lerp(_Horizon.rgb, _Zenith.rgb, zenithMix);
                color = lerp(color, _Ground.rgb, ground * (0.48 + _Haze * 0.22));
                float horizonMist = horizonBand * _Haze * 0.24;
                color = lerp(color, lerp(_Horizon.rgb, _Ground.rgb, 0.38), horizonMist);

                // Night retains the world's palette instead of snapping to one shared
                // Unity sky. Sunset is applied below on top of this local night grade.
                float night = 1.0 - saturate(_DayFactor);
                float nightAmount = night * lerp(0.90, 0.72, horizonBand);
                color = lerp(color, _Night.rgb * lerp(0.58, 1.25, horizonBand), nightAmount);

                float sunFacing = saturate(dot(dir, sun));
                float sunsetGate = saturate(1.0 - abs(dot(sun, up)) * 2.6);
                float sunsetGlow = pow(sunFacing, 6.0) * horizonBand * sunsetGate;
                color += _Sunset.rgb * sunsetGlow * (0.55 + _Haze * 0.45);

                float sunDisc = pow(sunFacing, 280.0) * (1.0 - _SpaceBlend);
                color += _Sunset.rgb * sunDisc * 1.6;

                if (_Dust > 0.01)
                {
                    float dust = horizonBand * _Dust * (0.25 + 0.35 * sunFacing);
                    color = lerp(color, _Horizon.rgb * 0.85, dust);
                }

                if (_Aurora > 0.01)
                {
                    float lat = abs(dot(dir, up));
                    float belt = saturate(1.0 - abs(lat - 0.62) * 7.0);
                    float az = atan2(dir.z, dir.x);
                    float wave = 0.55 + 0.45 * sin(az * 5.0 + _Time.y * 0.35);
                    float flicker = 0.7 + 0.3 * hash21(dir.xz * 8.0 + _Time.y * 0.05);
                    float aurora = belt * wave * flicker * _Aurora * (1.0 - _SpaceBlend);
                    float3 auroraCol = lerp(_AuroraColorA.rgb, _AuroraColorB.rgb, saturate(0.5 + 0.5 * sin(az * 2.0 + _Time.y * 0.2)));
                    color += auroraCol * aurora * 0.55;
                }

                float3 vacuum = float3(0.002, 0.004, 0.012);
                color = lerp(color, vacuum, saturate(_SpaceBlend));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
