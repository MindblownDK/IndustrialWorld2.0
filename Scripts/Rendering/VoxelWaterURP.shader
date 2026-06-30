Shader "VoxelEngine/VoxelWaterURP"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow", Color) = (0.08, 0.52, 0.82, 0.92)
        _DeepColor    ("Deep",    Color) = (0.01, 0.06, 0.22, 0.97)
        _FoamColor    ("Foam",    Color) = (0.92, 0.96, 1.00, 0.88)

        [Header(Planet Waves)]
        _DeepWaveAmplitude ("Deep Wave Amplitude", Range(0, 2)) = 0.55
        _DeepWaveFrequency ("Deep Wave Frequency", Range(0.01, 2)) = 0.16
        _DeepWaveSpeed     ("Deep Wave Speed", Range(0, 3)) = 0.45
        _SecondaryWaveAmplitude ("Secondary Wave Amplitude", Range(0, 1)) = 0.18
        _SecondaryWaveFrequency ("Secondary Wave Frequency", Range(0.01, 4)) = 0.32
        _SecondaryWaveSpeed     ("Secondary Wave Speed", Range(0, 3)) = 0.72
        _ShallowWaveAmplitude   ("Shallow Wave Amplitude", Range(0, 0.5)) = 0.08
        _ShallowWaveFrequency   ("Shallow Wave Frequency", Range(0.1, 6)) = 1.25
        _ShallowWaveSpeed       ("Shallow Wave Speed", Range(0, 4)) = 1.25
        _WaveChop  ("Wave Chop", Range(0, 1)) = 0.12
        _PlanetWaveBlend ("Planet Radial Wave Blend", Range(0, 1)) = 1
        _TideStrength ("Moon Tide Strength", Range(0, 0.6)) = 0.14
        _ShoreBlendDistance ("Shore Blend Distance", Range(0.1, 8)) = 3.2

        [Header(Surface Detail)]
        _NormalScale        ("Normal Strength", Range(0, 3)) = 0.7
        _Gloss              ("Gloss", Range(0, 1)) = 0.94
        _FresnelPower       ("Fresnel Power", Range(1, 8)) = 4.0
        _RefractionStrength ("Refraction", Range(0, 0.08)) = 0.012
        _CausticsIntensity  ("Caustics", Range(0, 1)) = 0.08

        [Header(Depth Coloring)]
        _DepthFade ("Depth Fade Dist", Range(0.1, 20)) = 4.0

        [Header(Shore Absorption)]
        _ShoreOpaqueDepth ("Shore Opaque Depth", Range(0.1, 5)) = 1.2
        _ShoreFoamWidth   ("Shore Foam Width", Range(0.1, 5)) = 1.6
        _ShoreFoamIntensity ("Shore Foam Intensity", Range(0, 2)) = 0.8

        [Header(Subsurface Scattering)]
        _SSSIntensity ("SSS Intensity", Range(0, 1)) = 0.22

        [Header(Flow Mapping)]
        _FlowNormalStrength ("Flow Normal Strength", Range(0, 2)) = 0.45
        _FlowFoamStrength   ("Flow Foam Strength", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLiquid"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _FoamColor;
                float  _DeepWaveAmplitude, _DeepWaveFrequency, _DeepWaveSpeed;
                float  _SecondaryWaveAmplitude, _SecondaryWaveFrequency, _SecondaryWaveSpeed;
                float  _ShallowWaveAmplitude, _ShallowWaveFrequency, _ShallowWaveSpeed;
                float  _WaveChop, _PlanetWaveBlend, _TideStrength, _ShoreBlendDistance;
                float  _NormalScale;
                float  _Gloss, _FresnelPower, _RefractionStrength, _CausticsIntensity;
                float  _DepthFade;
                float  _ShoreOpaqueDepth, _ShoreFoamWidth, _ShoreFoamIntensity;
                float  _SSSIntensity;
                float  _FlowNormalStrength, _FlowFoamStrength;
            CBUFFER_END

            float _PlanetOceanSeaLevelOffset;
            float _PlanetOceanDeepWaveAmplitude;
            float _PlanetOceanDeepWaveFrequency;
            float _PlanetOceanDeepWaveSpeed;
            float _PlanetOceanSecondaryWaveAmplitude;
            float _PlanetOceanSecondaryWaveFrequency;
            float _PlanetOceanSecondaryWaveSpeed;
            float _PlanetOceanWaveChop;
            float _PlanetOceanShoreAttenuationDistance;
            float _PlanetOceanShallowRippleAmplitude;
            float _PlanetOceanShallowRippleFrequency;
            float _PlanetOceanShallowRippleSpeed;
            float _PlanetOceanTidalWaveBoost;
            float _PlanetOceanTidalHeightBoost;
            float4 _PlanetOceanShallowColor;
            float4 _PlanetOceanDeepColor;
            float4 _PlanetOceanFoamColor;
            float _PlanetOceanRefractionStrength;
            float _PlanetOceanFresnelPower;
            float _PlanetOceanSSS;

            struct A2V
            {
                float4 posOS  : POSITION;
                float3 normOS : NORMAL;
                float2 uv     : TEXCOORD0;
                float2 uv2    : TEXCOORD1;
                float4 color  : COLOR;
            };

            struct V2F
            {
                float4 posCS  : SV_POSITION;
                float3 posWS  : TEXCOORD0;
                float3 normWS : TEXCOORD1;
                float  fog    : TEXCOORD2;
                float4 scrPos : TEXCOORD3;
                float2 flowUV : TEXCOORD4;
                float4 data   : TEXCOORD5;
            };

            float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1,0)), f.x), lerp(Hash21(i + float2(0,1)), Hash21(i + float2(1,1)), f.x), f.y);
            }
            float FBM(float2 p) { float v = 0; float a = 0.5; [unroll] for (int i = 0; i < 4; i++) { v += ValueNoise(p) * a; p = p * 2.03 + 17.1; a *= 0.5; } return v; }

            float3 Gerstner(float2 xz, float2 dir, float amp, float freq, float speed, float chop, float t)
            {
                dir = normalize(dir);
                float phase = dot(xz, dir) * freq + t * speed;
                float s, c; sincos(phase, s, c);
                return float3(dir.x * amp * c * chop, amp * s, dir.y * amp * c * chop);
            }

            float3 FlowMappedNormal(float2 worldXZ, float2 flowDir, float flowSpeed, float t)
            {
                float2 dir = flowDir;
                float speed = length(dir);
                dir = speed > 0.001f ? normalize(dir) : float2(0.04, 0.03);
                float flowTime = t * (0.15 + speed * 0.6);
                float2 uv1 = worldXZ * 0.04 + dir * flowTime * 0.35;
                float2 uv2 = worldXZ * 0.09 - dir * flowTime * 0.18 + float2(5.3, 7.1);
                float h = FBM(uv1 * 4.0) * 0.7 + FBM(uv2 * 7.0) * 0.3;
                float eps = 0.05;
                float hx = FBM((uv1 + float2(eps, 0)) * 4.0) * 0.7 + FBM((uv2 + float2(eps, 0)) * 7.0) * 0.3;
                float hz = FBM((uv1 + float2(0, eps)) * 4.0) * 0.7 + FBM((uv2 + float2(0, eps)) * 7.0) * 0.3;
                float strength = _NormalScale * (1.0 + speed * _FlowNormalStrength);
                return normalize(float3((h - hx) * strength, 1.0, (h - hz) * strength));
            }

            float3 PlanetWave(float3 worldPos, float3 radialUp, float2 flow, float shoreAtten, float tideMask, float t)
            {
                float3 tangentA = cross(radialUp, float3(0,1,0));
                if (dot(tangentA, tangentA) < 0.001) tangentA = cross(radialUp, float3(0,0,1));
                tangentA = normalize(tangentA);
                float3 tangentB = normalize(cross(radialUp, tangentA));
                float2 uv = float2(dot(worldPos, tangentA), dot(worldPos, tangentB));

                float deepAmp = max(_DeepWaveAmplitude, _PlanetOceanDeepWaveAmplitude);
                float deepFreq = _PlanetOceanDeepWaveFrequency > 0 ? _PlanetOceanDeepWaveFrequency : _DeepWaveFrequency;
                float deepSpeed = _PlanetOceanDeepWaveSpeed > 0 ? _PlanetOceanDeepWaveSpeed : _DeepWaveSpeed;
                float secAmp = max(_SecondaryWaveAmplitude, _PlanetOceanSecondaryWaveAmplitude);
                float secFreq = _PlanetOceanSecondaryWaveFrequency > 0 ? _PlanetOceanSecondaryWaveFrequency : _SecondaryWaveFrequency;
                float secSpeed = _PlanetOceanSecondaryWaveSpeed > 0 ? _PlanetOceanSecondaryWaveSpeed : _SecondaryWaveSpeed;
                float waveChop = max(_WaveChop, _PlanetOceanWaveChop);
                float shallowAmp = max(_ShallowWaveAmplitude, _PlanetOceanShallowRippleAmplitude);
                float shallowFreq = _PlanetOceanShallowRippleFrequency > 0 ? _PlanetOceanShallowRippleFrequency : _ShallowWaveFrequency;
                float shallowSpeed = _PlanetOceanShallowRippleSpeed > 0 ? _PlanetOceanShallowRippleSpeed : _ShallowWaveSpeed;
                float tideBoost = 1.0 + tideMask * (_TideStrength + _PlanetOceanTidalWaveBoost * 0.5);

                float3 swell = 0;
                swell += Gerstner(uv, float2(1.0, 0.15), deepAmp * tideBoost * shoreAtten, deepFreq, deepSpeed, waveChop, t);
                swell += Gerstner(uv, float2(-0.35, 0.92), secAmp * tideBoost * shoreAtten, secFreq, secSpeed, waveChop, t);
                swell += Gerstner(uv, normalize(flow + float2(0.18, -0.26)), shallowAmp * (1.0 - shoreAtten * 0.45), shallowFreq, shallowSpeed, waveChop * 0.4, t);

                return radialUp * swell.y + tangentA * swell.x + tangentB * swell.z;
            }

            V2F vert(A2V i)
            {
                V2F o = (V2F)0;
                float3 posOS = i.posOS.xyz;
                float3 worldPos = TransformObjectToWorld(posOS);
                float3 radialUp = normalize(worldPos);
                float topFacing = saturate(dot(normalize(i.normOS), radialUp));
                float shoreDepthMask = saturate(i.color.r);
                float tideMask = saturate(i.color.g);
                float t = _Time.y;

                if (topFacing > 0.55)
                {
                    float shoreAtten = saturate(shoreDepthMask + 0.15);
                    float3 wave = PlanetWave(worldPos, radialUp, i.uv2, shoreAtten, tideMask, t);
                    posOS += wave;
                    worldPos = TransformObjectToWorld(posOS);
                }

                o.posWS = worldPos;
                o.posCS = TransformWorldToHClip(worldPos);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                o.flowUV = i.uv2;
                o.data = float4(shoreDepthMask, tideMask, topFacing, 0);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.posWS);
                float3 geoN = normalize(i.normWS);
                float2 flowDir = i.flowUV;
                float flowSpeed = length(flowDir);
                float shoreDepthMask = i.data.x;
                float tideMask = i.data.y;
                float topFacing = i.data.z;
                bool isSideFace = topFacing < 0.4;

                float3 detailN = FlowMappedNormal(i.posWS.xz, flowDir, flowSpeed, _Time.y);
                float3 N = normalize(lerp(geoN, detailN, isSideFace ? 0.08 : 0.45));

                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float2 refractUV = screenUV + N.xz * max(_RefractionStrength, _PlanetOceanRefractionStrength) * (isSideFace ? 0.2 : 1.0);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                bool hasValidDepth = rawDepth > 0.00001f && rawDepth < 0.99999f;
                float depthDiff = hasValidDepth ? max(0, sceneEyeDepth - waterEyeDepth) : 12.0f;
                float deep01 = saturate(depthDiff / _DepthFade);

                float4 shallowCol = any(_PlanetOceanShallowColor.rgb > 0.001) ? _PlanetOceanShallowColor : _ShallowColor;
                float4 deepCol = any(_PlanetOceanDeepColor.rgb > 0.001) ? _PlanetOceanDeepColor : _DeepColor;
                float4 foamCol = any(_PlanetOceanFoamColor.rgb > 0.001) ? _PlanetOceanFoamColor : _FoamColor;

                float3 refracted = SampleSceneColor(refractUV).rgb;
                if (length(refracted) < 0.001f) refracted = deepCol.rgb;

                float3 waterCol = lerp(shallowCol.rgb, deepCol.rgb, deep01);
                waterCol = lerp(waterCol, waterCol * float3(0.84, 0.94, 1.06), tideMask * 0.06);

                float shoreFoam = saturate(1.0 - depthDiff / _ShoreFoamWidth) * _ShoreFoamIntensity * (1.0 - shoreDepthMask);
                float crest = saturate((FBM(i.posWS.xz * 0.15 + _Time.y * 0.03) - 0.62) * 2.2) * 0.25;
                float foam = isSideFace ? 0.0 : saturate(shoreFoam + crest + flowSpeed * _FlowFoamStrength * 0.15);

                float NdV = saturate(dot(V, N));
                float fresnel = pow(1.0 - NdV, max(_FresnelPower, _PlanetOceanFresnelPower));

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(V + L);
                float spec = pow(saturate(dot(N, H)), lerp(80.0, 400.0, _Gloss)) * (isSideFace ? 0.18 : 0.45);
                float sss = pow(saturate(dot(V, -L)), 2.5) * (1.0 - deep01) * max(_SSSIntensity, _PlanetOceanSSS) * (isSideFace ? 0.0 : 1.0);

                float refractWeight = (1.0 - deep01) * (1.0 - fresnel) * (isSideFace ? 0.1 : 0.35);
                float3 col = lerp(waterCol, refracted, refractWeight);
                float3 sky = SampleSH(N) * 0.8 + mainLight.color.rgb * 0.08;
                col = lerp(col, sky, fresnel * (isSideFace ? 0.12 : 0.28));
                col += mainLight.color.rgb * spec * saturate(mainLight.distanceAttenuation);
                col += mainLight.color.rgb * sss * float3(0.08, 0.45, 0.38);
                col = lerp(col, foamCol.rgb, foam * foamCol.a);

                float alpha = isSideFace ? lerp(0.4, 0.72, deep01) : lerp(shallowCol.a, deepCol.a, deep01 * 0.75);
                alpha = max(alpha, isSideFace ? 0.38 : 0.54);
                alpha = lerp(alpha, min(alpha + 0.16, 0.96), fresnel * 0.35);
                alpha = lerp(alpha, min(alpha + 0.2, 0.98), foam * 0.5);

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
