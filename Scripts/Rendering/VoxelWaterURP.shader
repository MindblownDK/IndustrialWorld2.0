Shader "VoxelEngine/VoxelWaterURP"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow", Color) = (0.08, 0.52, 0.82, 0.92)
        _DeepColor    ("Deep",    Color) = (0.01, 0.06, 0.22, 0.97)
        _FoamColor    ("Foam",    Color) = (0.92, 0.96, 1.00, 0.88)

        [Header(Planet Waves)]
        _DeepWaveAmplitude ("Deep Wave Amplitude", Range(0, 2)) = 0.85
        _DeepWaveFrequency ("Deep Wave Frequency", Range(0.01, 2)) = 0.22
        _DeepWaveSpeed     ("Deep Wave Speed", Range(0, 3)) = 0.55
        _SecondaryWaveAmplitude ("Secondary Wave Amplitude", Range(0, 1)) = 0.35
        _SecondaryWaveFrequency ("Secondary Wave Frequency", Range(0.01, 4)) = 0.47
        _SecondaryWaveSpeed     ("Secondary Wave Speed", Range(0, 3)) = 0.91
        _ShallowWaveAmplitude   ("Shallow Wave Amplitude", Range(0, 0.5)) = 0.16
        _ShallowWaveFrequency   ("Shallow Wave Frequency", Range(0.1, 6)) = 1.65
        _ShallowWaveSpeed       ("Shallow Wave Speed", Range(0, 4)) = 1.8
        _WaveChop  ("Wave Chop", Range(0, 1)) = 0.28
        _PlanetWaveBlend ("Planet Radial Wave Blend", Range(0, 1)) = 1
        _TideStrength ("Moon Tide Strength", Range(0, 0.6)) = 0.22
        _ShoreBlendDistance ("Shore Blend Distance", Range(0.1, 8)) = 2.5

        [Header(Surface Detail)]
        _NormalScale        ("Normal Strength", Range(0, 3)) = 1.4
        _Gloss              ("Gloss", Range(0, 1)) = 0.96
        _FresnelPower       ("Fresnel Power", Range(1, 8)) = 3.2
        _RefractionStrength ("Refraction", Range(0, 0.08)) = 0.032
        _CausticsIntensity  ("Caustics", Range(0, 1)) = 0.25

        [Header(Depth Coloring)]
        _DepthFade ("Depth Fade Dist", Range(0.1, 20)) = 2.5

        [Header(Shore Absorption)]
        _ShoreOpaqueDepth ("Shore Opaque Depth", Range(0.1, 5)) = 1.5
        _ShoreFoamWidth   ("Shore Foam Width", Range(0.1, 5)) = 2.0
        _ShoreFoamIntensity ("Shore Foam Intensity", Range(0, 2)) = 1.2

        [Header(Subsurface Scattering)]
        _SSSIntensity ("SSS Intensity", Range(0, 1)) = 0.35

        [Header(Flow Mapping)]
        _FlowNormalStrength ("Flow Normal Strength", Range(0, 2)) = 1.0
        _FlowFoamStrength   ("Flow Foam Strength", Range(0, 2)) = 0.8
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
            float FBM6(float2 p) { float v = 0; float a = 0.5; [unroll] for (int i = 0; i < 6; i++) { v += ValueNoise(p) * a; p = p * 2.03 + 17.1; a *= 0.5; } return v; }

            float3 Gerstner(float2 xz, float2 dir, float amp, float freq, float speed, float chop, float t)
            {
                dir = normalize(dir);
                float phase = dot(xz, dir) * freq + t * speed;
                float s, c; sincos(phase, s, c);
                return float3(dir.x * amp * c * chop, amp * s, dir.y * amp * c * chop);
            }

            float3 PlanetWave(float3 worldPos, float3 radialUp, float2 flow, float deepAmp, float shoreAtten, float tideMask, float t)
            {
                float3 tangentA = cross(radialUp, float3(0,1,0));
                if (dot(tangentA, tangentA) < 0.001) tangentA = cross(radialUp, float3(0,0,1));
                tangentA = normalize(tangentA);
                float3 tangentB = normalize(cross(radialUp, tangentA));
                float2 uv = float2(dot(worldPos, tangentA), dot(worldPos, tangentB));
                float tide = 1.0 + tideMask * _TideStrength;

                float3 deep = 0;
                deep += Gerstner(uv, float2( 1.00,  0.23), deepAmp * tide, _DeepWaveFrequency, _DeepWaveSpeed, _WaveChop, t);
                deep += Gerstner(uv, float2(-0.42,  0.91), _SecondaryWaveAmplitude * tide, _SecondaryWaveFrequency, _SecondaryWaveSpeed, _WaveChop, t);
                deep += Gerstner(uv, float2( 0.18, -0.98), _SecondaryWaveAmplitude * 0.45 * tide, _SecondaryWaveFrequency * 2.4, _SecondaryWaveSpeed * 0.9, _WaveChop, t);

                float shallowPhase = dot(uv, normalize(float2(0.7, -0.3))) * _ShallowWaveFrequency + t * _ShallowWaveSpeed;
                float shallow = sin(shallowPhase) * _ShallowWaveAmplitude;
                float3 shallowVec = radialUp * shallow;

                float3 local = lerp(shallowVec, deep, shoreAtten);
                return radialUp * local.y + tangentA * local.x + tangentB * local.z;
            }

            float3 FlowMappedNormal(float2 worldXZ, float2 flowDir, float flowSpeed, float t)
            {
                float2 dir = flowDir; float speed = length(dir);
                dir = speed > 0.001f ? normalize(dir) : float2(0.04, 0.03);
                float flowTime = t * (0.35 + speed * 1.5);
                float2 uv1 = worldXZ * 0.09 + dir * flowTime * 0.8;
                float2 uv2 = worldXZ * 0.17 + dir * flowTime * 0.5 + float2(5.3, 7.1);
                float2 uv3 = worldXZ * 0.45 - dir * flowTime * 0.3;
                float h  = FBM(uv1 * 5.0) * 0.50 + FBM(uv2 * 8.0) * 0.30 + FBM(uv3 * 11.0) * 0.20;
                float eps = 0.08;
                float hx = FBM((uv1 + float2(eps, 0)) * 5.0) * 0.50 + FBM((uv2 + float2(eps, 0)) * 8.0) * 0.30 + FBM((uv3 + float2(eps, 0)) * 11.0) * 0.20;
                float hz = FBM((uv1 + float2(0, eps)) * 5.0) * 0.50 + FBM((uv2 + float2(0, eps)) * 8.0) * 0.30 + FBM((uv3 + float2(0, eps)) * 11.0) * 0.20;
                float strength = _NormalScale * (1.0 + speed * _FlowNormalStrength * 2.0);
                return normalize(float3((h - hx) * strength, 1.0, (h - hz) * strength));
            }

            V2F vert(A2V i)
            {
                V2F o = (V2F)0;
                float3 posOS = i.posOS.xyz;
                float3 worldPos = TransformObjectToWorld(posOS);

                float3 radialUp = normalize(worldPos);
                float topFacing = saturate(max(i.normOS.y, dot(normalize(i.normOS), radialUp)));
                float shoreDepthMask = saturate(i.color.r);
                float tideMask = i.color.g;
                float shoreAtten = saturate(shoreDepthMask * (_ShoreBlendDistance / max(_ShoreBlendDistance, 0.0001)));

                if (topFacing > 0.45)
                {
                    float t = _Time.y;
                    float deepAmp = _DeepWaveAmplitude * topFacing;
                    float3 flatW = 0;
                    flatW += Gerstner(worldPos.xz, float2( 1.00,  0.23), deepAmp, _DeepWaveFrequency, _DeepWaveSpeed, _WaveChop, t);
                    flatW += Gerstner(worldPos.xz, float2(-0.42,  0.91), _SecondaryWaveAmplitude, _SecondaryWaveFrequency, _SecondaryWaveSpeed, _WaveChop, t);
                    flatW += Gerstner(worldPos.xz, float2( 0.18, -0.98), _SecondaryWaveAmplitude * 0.45, _SecondaryWaveFrequency * 2.4, _SecondaryWaveSpeed * 0.9, _WaveChop, t);
                    float3 planetW = PlanetWave(worldPos, radialUp, i.uv2, deepAmp, shoreAtten, tideMask, t);
                    float3 w = lerp(flatW, planetW, _PlanetWaveBlend);
                    posOS += w;
                    worldPos = TransformObjectToWorld(posOS);
                }

                o.posWS  = worldPos;
                o.posCS  = TransformWorldToHClip(worldPos);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog    = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                o.flowUV = i.uv2;
                o.data = float4(shoreDepthMask, tideMask, topFacing, 0);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float t = _Time.y;
                float3 V = normalize(_WorldSpaceCameraPos - i.posWS);
                float3 geoN = normalize(i.normWS);
                float2 flowDir = i.flowUV;
                float flowSpeed = length(flowDir);
                float shoreDepthMask = i.data.x;
                float tideMask = i.data.y;

                float radialFacing = abs(dot(geoN, normalize(i.posWS)));
                bool isSideFace = abs(geoN.y) < 0.5 && radialFacing < 0.5;

                float3 detailN = FlowMappedNormal(i.posWS.xz, flowDir, flowSpeed, t);
                float3 N = normalize(float3(detailN.x, 1.0, detailN.z));
                float blendFactor = isSideFace ? 0.15 : saturate(abs(geoN.y));
                N = normalize(lerp(geoN, N, blendFactor));

                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float2 refractUV = screenUV + N.xz * _RefractionStrength;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                bool hasValidDepth = rawDepth > 0.00001f && rawDepth < 0.99999f;
                float depthDiff = hasValidDepth ? max(0, sceneEyeDepth - waterEyeDepth) : 15.0f;
                float deep01 = saturate(depthDiff / _DepthFade);
                float shoreAtten = saturate(shoreDepthMask);
                float3 refracted = SampleSceneColor(refractUV).rgb;
                if (length(refracted) < 0.001f) refracted = _DeepColor.rgb;

                float shoreFactor = saturate(1.0 - depthDiff / _ShoreOpaqueDepth);
                float sideDeepBoost = isSideFace ? 0.4 : 0.0;
                float tidalTint = saturate(tideMask) * 0.08;
                float4 waterCol = lerp(_ShallowColor, _DeepColor, saturate(deep01 + sideDeepBoost));
                waterCol.rgb = lerp(waterCol.rgb, waterCol.rgb * float3(0.82, 0.92, 1.08), tidalTint);

                float foam = 0.0;
                if (!isSideFace)
                {
                    float validDepth = step(0.08, depthDiff);
                    float shoreFoamFade = saturate(1.0 - depthDiff / _ShoreFoamWidth);
                    float shoreFoam = shoreFoamFade * validDepth * _ShoreFoamIntensity * (1.0 - shoreAtten + 0.15);
                    float crest = saturate((FBM(i.posWS.xz * 0.22 + t * 0.075) - 0.58) * 3.0) * saturate(_DeepWaveAmplitude * 1.75);
                    float lace = FBM(i.posWS.xz * 0.85 + float2(t * 0.12, -t * 0.08));
                    float crestFoam = crest * lace * 0.6;
                    float flowFoam = saturate(flowSpeed * 3.0 - 0.2) * _FlowFoamStrength;
                    float2 foamScrollUV = i.posWS.xz + normalize(flowDir + 0.001) * t * 0.3;
                    flowFoam *= saturate(FBM(foamScrollUV * 1.5) * 1.5);
                    foam = saturate(shoreFoam + crestFoam + flowFoam);
                }

                float NdV = saturate(dot(V, N));
                float fresnel = pow(1.0 - NdV, _FresnelPower);

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(V + L);
                float specBroad = pow(saturate(dot(N, H)), lerp(80.0, 900.0, _Gloss)) * 0.7;
                float specTight = pow(saturate(dot(N, H)), 2400.0) * 1.2;
                float glitterMask = isSideFace ? 0.0 : pow(saturate(FBM6(i.posWS.xz * 2.8 + t * 0.15)), 8.0);
                float glitter = pow(saturate(dot(N, H)), 3200.0) * glitterMask * 2.5;
                float sssWrap = isSideFace ? 0.0 : pow(saturate(dot(V, -L)), 3.0) * (1.0 - deep01) * _SSSIntensity;
                float3 sssColor = mainLight.color.rgb * sssWrap * float3(0.12, 0.75, 0.55);
                float caustic = isSideFace ? 0.0 : pow(saturate(FBM(i.posWS.xz * 0.65 + N.xz * 1.8 - t * 0.18)), 3.0) * _CausticsIntensity * (1.0 - deep01);

                float refractWeight = (1.0 - deep01) * (1.0 - fresnel) * 0.55;
                refractWeight *= (1.0 - shoreFactor * 0.9);
                float3 col = lerp(waterCol.rgb, refracted, refractWeight);
                float3 sky = SampleSH(N) * 0.85 + mainLight.color.rgb * 0.10;
                col = lerp(col, sky, fresnel * 0.35);
                col += mainLight.color.rgb * (specBroad + specTight + glitter) * saturate(mainLight.distanceAttenuation);
                col += sssColor;
                col += caustic * float3(0.45, 0.95, 1.0);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                float alpha;
                if (isSideFace)
                {
                    alpha = lerp(0.88, 0.97, deep01);
                    alpha = lerp(alpha, 0.99, shoreFactor * 0.7);
                }
                else
                {
                    alpha = waterCol.a;
                    alpha = lerp(alpha, 0.99, shoreFactor * 0.85);
                    alpha = lerp(alpha, min(alpha + 0.12, 0.99), fresnel);
                    alpha = max(alpha, 0.82);
                    alpha = lerp(alpha, min(alpha + foam * 0.3, 0.99), foam);
                }

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
