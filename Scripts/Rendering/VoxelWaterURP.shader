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

        [Header(Thin-Film Iridescence - 9.16.0)]
        _IridescenceStrength ("Iridescence Strength", Range(0, 1)) = 0.0
        _IridescenceScale    ("Iridescence Hue Cycle", Range(0, 4)) = 1.0

        [Header(Emission - 9.16.0)]
        _EmissionColor ("Emission Colour", Color) = (0, 0, 0, 1)
        _EmissionStrength    ("Emission Strength", Range(0, 4)) = 0.0

        [Header(Surface Texture - 9.16.0)]
        _Patchiness      ("Colour Patchiness", Range(0, 1)) = 0.3
        _DetailStrength  ("Fine Ripple Strength", Range(0, 2)) = 0.4
        _DetailScale     ("Fine Ripple Scale", Range(0.5, 12)) = 4.0
        _SparkleStrength ("Sparkle Strength", Range(0, 2)) = 1.0
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
            Cull Off

            HLSLPROGRAM
            // The fixed wake arrays, scene-depth sampling and repeated FBM passes exceed
            // the default Shader Model 2.5 limits. Without an explicit target Unity can
            // import this shader as unsupported even on Direct3D 11/12.
            #pragma target 4.5
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
                float  _IridescenceStrength, _IridescenceScale;
                float4 _EmissionColor;
                float  _EmissionStrength;
                float  _Patchiness, _DetailStrength, _DetailScale, _SparkleStrength;
            CBUFFER_END

            // ── Weather → sea state (globals published by WeatherSeaState) ──
            // 0 = glass calm, 1 = storm; wind bearing is projected onto each local tangent plane.
            float  _WeatherSeaState;
            float4 _WeatherWindDirWS;

            float SeaAmp()   { return 1.0 + saturate(_WeatherSeaState) * 1.45; }   // wave height
            float SeaSpeed() { return 2.0 + saturate(_WeatherSeaState) * 0.5; }   // faster visible crest travel
            float SeaChop(float chop) { return saturate(chop * (1.0 + saturate(_WeatherSeaState) * 1.10)); }
            float SeaFoam()  { return 1.0 + saturate(_WeatherSeaState) * 1.80; }   // whitecaps


            // Native spherical-water context + a compact wake registry. These globals are
            // written by PlanetWaterRendererBootstrap / NativeWaterWakeSystem and deliberately
            // avoid a dependency on an external ocean renderer or flat XZ wake texture.
            #define VOXEL_WAKE_MAX 16
            float4 _VoxelWaterBodyCenter;
            float _VoxelWaterIsPlanet;
            int _VoxelWakeCount;
            float4 _VoxelWakePositions[VOXEL_WAKE_MAX];
            float4 _VoxelWakeDirections[VOXEL_WAKE_MAX];
            float4 _VoxelWakeData[VOXEL_WAKE_MAX];

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
                float3 flowUV : TEXCOORD4;
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

            float3 NativeWaterUp(float3 worldPos)
            {
                float3 radial = worldPos - _VoxelWaterBodyCenter.xyz;
                float radialLengthSq = dot(radial, radial);
                radial = radialLengthSq > 0.0001 ? radial * rsqrt(radialLengthSq) : float3(0, 1, 0);
                return normalize(lerp(float3(0, 1, 0), radial, saturate(_VoxelWaterIsPlanet)));
            }

            float NativeWakeFoam(float3 worldPos, float3 waterUp)
            {
                float foam = 0.0;
                [unroll]
                for (int wi = 0; wi < VOXEL_WAKE_MAX; wi++)
                {
                    if (wi >= _VoxelWakeCount) continue;
                    float4 wakePos = _VoxelWakePositions[wi];
                    float4 wakeDir = _VoxelWakeDirections[wi];
                    float4 wakeData = _VoxelWakeData[wi];
                    float life = wakeData.z;
                    if (life <= 0.001) continue;

                    float3 direction = wakeDir.xyz;
                    float directionLengthSq = dot(direction, direction);
                    if (directionLengthSq <= 0.0001) continue;
                    direction *= rsqrt(directionLengthSq);

                    float3 delta = worldPos - wakePos.xyz;
                    delta -= waterUp * dot(delta, waterUp);
                    float forward = dot(delta, direction);
                    float trail = max(0.0, -forward);
                    float wakeLength = max(0.1, wakeData.x);
                    if (trail > wakeLength) continue;

                    float lateral = length(delta - direction * forward);
                    float width = max(0.2, wakeDir.w);
                    float spread = width + trail * 0.24;
                    float lineWidth = lerp(0.22, max(0.5, width * 0.55), saturate(trail / wakeLength));
                    float vLine = exp(-abs(lateral - spread) / lineWidth);
                    float propWash = exp(-lateral / max(0.4, width * 0.75)) * exp(-trail / max(0.4, wakeLength * 0.25));
                    float tailFade = saturate(1.0 - trail / wakeLength);
                    foam = max(foam, (vLine * 0.95 + propWash * 0.7) * tailFade * wakeData.y * life);
                }
                return saturate(foam);
            }

            float3 DirectionalWaveNormal(float3 surfaceCoord, float3 radialUp,
                float shoreWaveFade, float tideMask, float t, out float crestFoam)
            {
                float seaState = saturate(_WeatherSeaState);
                float3 globalWind = _WeatherWindDirWS.xyz;
                float globalWindLengthSq = dot(globalWind, globalWind);
                if (globalWindLengthSq > 0.0001)
                    globalWind *= rsqrt(globalWindLengthSq);
                else
                    globalWind = normalize(float3(0.83, 0.0, 0.56));

                // Keep phase axes body-centred (not tangent-projected positions, which
                // collapse to zero on a sphere); project only their derivatives for normals.
                float globalAxisBlend = smoothstep(0.82, 0.98, abs(globalWind.y));
                float3 globalAxis = normalize(lerp(float3(0, 1, 0), float3(1, 0, 0), globalAxisBlend));
                float3 globalCross = normalize(cross(globalAxis, globalWind));
                float3 longWaveDir = normalize(globalWind + globalCross * 0.18);
                float3 crossWaveDir = normalize(globalCross - globalWind * 0.20);
                float3 detailWaveDir = normalize(globalWind * 0.82 - globalCross * 0.57);
                float3 longTangent = longWaveDir - radialUp * dot(longWaveDir, radialUp);
                float3 crossTangent = crossWaveDir - radialUp * dot(crossWaveDir, radialUp);
                float3 detailTangent = detailWaveDir - radialUp * dot(detailWaveDir, radialUp);

                // All wave bands and associated crests fade out in shallow/intersecting
                // water; leaving a short-wave floor here caused the persistent beach rings.
                float tide = 1.0 + saturate(tideMask) * max(_TideStrength, 0.0);
                float waveEnergy = SeaAmp() * tide;
                float deepAmp = max(_DeepWaveAmplitude, 0.0) * waveEnergy * shoreWaveFade;
                float crossAmp = max(_SecondaryWaveAmplitude, 0.0) * waveEnergy * shoreWaveFade;
                float detailAmp = max(_ShallowWaveAmplitude, 0.0) * tide
                    * (1.0 + seaState * 0.35) * shoreWaveFade;

                float deepFreq = max(_DeepWaveFrequency, 0.001);
                float crossFreq = max(_SecondaryWaveFrequency, 0.001);
                float detailFreq = max(_ShallowWaveFrequency, 0.001);
                // Body-centred phases are identical on duplicated chunk-edge vertices.
                // Minus time makes crests travel forward along the wind bearing.
                float deepPhase = dot(surfaceCoord, longWaveDir) * deepFreq
                    - t * max(_DeepWaveSpeed, 0.0) * SeaSpeed();
                float crossPhase = dot(surfaceCoord, crossWaveDir) * crossFreq
                    - t * max(_SecondaryWaveSpeed, 0.0) * SeaSpeed();
                float detailPhase = dot(surfaceCoord, detailWaveDir) * detailFreq
                    - t * max(_ShallowWaveSpeed, 0.0) * SeaSpeed();

                float deepSin, deepCos, crossSin, crossCos, detailSin, detailCos;
                sincos(deepPhase, deepSin, deepCos);
                sincos(crossPhase, crossSin, crossCos);
                sincos(detailPhase, detailSin, detailCos);
                float chop = SeaChop(max(_WaveChop, 0.0));
                float deepSlope = deepAmp * deepFreq
                    * (deepCos + chop * 0.38 * (2.0 * deepCos * deepCos - 1.0));
                float crossSlope = crossAmp * crossFreq
                    * (crossCos + chop * 0.24 * (2.0 * crossCos * crossCos - 1.0));
                float detailSlope = detailAmp * detailFreq
                    * (detailCos + chop * 0.12 * (2.0 * detailCos * detailCos - 1.0));
                float3 slope = (longTangent * deepSlope + crossTangent * crossSlope + detailTangent * detailSlope)
                    * max(_NormalScale, 0.0);

                // Keep strong authored storm values readable rather than turning them
                // into near-vertical facets; this affects shading only, never the mesh.
                float slopeLengthSq = dot(slope, slope);
                float maxSlope = lerp(0.72, 1.05, seaState);
                if (slopeLengthSq > maxSlope * maxSlope)
                    slope *= maxSlope * rsqrt(slopeLengthSq);

                float crestSignal = deepSin + 0.32 * crossSin + 0.14 * detailSin;
                float crestThreshold = lerp(0.98, 0.58, seaState);
                float crestMask = smoothstep(crestThreshold, crestThreshold + 0.28,
                    crestSignal + chop * 0.12);
                crestFoam = saturate(crestMask * lerp(0.06, 0.48, seaState) * SeaFoam())
                    * shoreWaveFade;

                return normalize(radialUp - slope);
            }

            V2F vert(A2V i)
            {
                V2F o = (V2F)0;
                float3 posOS = i.posOS.xyz;
                float3 worldPos = TransformObjectToWorld(posOS);

                float3 radialUp = NativeWaterUp(worldPos);
                float shoreDepthMask = saturate(i.color.r);
                float tideMask = i.color.g;

                // Topology is voxel-owned. Per-triangle depth and normals MUST NOT move
                // duplicate edge vertices apart; surface motion is normal-only.
                o.posWS  = worldPos;
                o.posCS  = TransformWorldToHClip(worldPos);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog    = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                o.flowUV = float3(i.uv2, i.color.a - 2.0);
                // data.z gates solver-flow crests. Native voxel-liquid meshes encode snapshot
                // time in the otherwise-unused vertex alpha; the procedural ocean patch opts out.
                // data.w is geometry-authored water depth, including the patch's value when scene depth is absent.
                o.data = float4(shoreDepthMask, tideMask, step(1.5, i.color.a), saturate(i.color.b));
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float t = _Time.y;
                float3 V = normalize(_WorldSpaceCameraPos - i.posWS);
                float3 geoN = normalize(i.normWS);
                float2 flowDir = i.flowUV.xy;
                float flowSpeed = length(flowDir);
                float2 flowDirectionUV = flowSpeed > 0.0001 ? flowDir / flowSpeed : float2(0.0, 0.0);
                // Flow snapshot time is forwarded through flowUV.z; stale crests fade without
                // additional simulation ticks or per-frame mesh updates.
                float flowAge = max(0.0, t - i.flowUV.z);
                float flowFreshness = 1.0 - smoothstep(1.0, 2.2, flowAge);
                float flowActivity = smoothstep(0.04, 0.4, flowSpeed) * saturate(i.data.z) * flowFreshness;
                float flowSpeedClamped = min(flowSpeed, 2.2);
                float shoreDepthMask = i.data.x;
                float tideMask = i.data.y;
                float geometryDepth01 = saturate(i.data.w);

                float3 radialUp = NativeWaterUp(i.posWS);
                float3 tanA = cross(radialUp, float3(0,1,0));
                if (dot(tanA, tanA) < 0.001) tanA = cross(radialUp, float3(0,0,1));
                tanA = normalize(tanA);
                float3 tanB = normalize(cross(radialUp, tanA));
                float3 surfaceCoord = i.posWS - _VoxelWaterBodyCenter.xyz;
                float3 surfaceAxisA = float3(0.73, 0.39, 0.56);
                float3 surfaceAxisB = float3(-0.42, 0.86, 0.28);
                float2 surfUV = float2(dot(surfaceCoord, surfaceAxisA), dot(surfaceCoord, surfaceAxisB));
                float2 flowPerpUV = float2(-flowDirectionUV.y, flowDirectionUV.x);
                bool isSideFace = dot(geoN,radialUp) < 0.3;

                // Use both voxel-authored bank thickness and the camera's actual terrain
                // intersection. The latter catches sloped beaches where a cube-local bank
                // sample reports deep water despite sand immediately below the surface.
                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                bool hasValidDepth = rawDepth > 0.00001f && rawDepth < 0.99999f;
                float depthDiff = hasValidDepth ? max(0, sceneEyeDepth - waterEyeDepth) : 15.0f;
                float screenDepth01 = saturate(depthDiff / _DepthFade);
                float deep01 = geometryDepth01;
                float shoreAtten = saturate(shoreDepthMask);
                float bankFadeEnd = max(0.07, _ShoreBlendDistance / 4.0);
                float bankWaveFade = smoothstep(0.025, bankFadeEnd, geometryDepth01);
                float sceneWaveFade = hasValidDepth ? smoothstep(0.25, 2.0, depthDiff) : 1.0;
                float shoreWaveFade = min(bankWaveFade, sceneWaveFade);
                // Preserve some flow readability in shallow water while ordinary swell stays calm at banks.
                float flowShoreFade = lerp(0.42, 1.0, shoreWaveFade);
                float flowWarp = sin(dot(surfUV, flowPerpUV) * 0.55 + t * 0.25) * 0.18;
                float flowPhase = dot(surfUV, flowDirectionUV) * 1.8 + flowWarp
                    - t * (0.22 + flowSpeedClamped * 1.65);
                float flowSin, flowCos;
                sincos(flowPhase, flowSin, flowCos);
                float flowCrestMask = smoothstep(0.58, 0.93, flowSin * 0.5 + 0.5);
                float3 flowTangentWS = flowDirectionUV.x * surfaceAxisA + flowDirectionUV.y * surfaceAxisB;
                flowTangentWS -= radialUp * dot(flowTangentWS, radialUp);
                float flowTangentLengthSq = dot(flowTangentWS, flowTangentWS);
                flowTangentWS = flowTangentLengthSq > 0.0001
                    ? flowTangentWS * rsqrt(flowTangentLengthSq) : float3(0.0, 0.0, 0.0);

                float waveCrestFoam;
                float3 waveN = DirectionalWaveNormal(surfaceCoord, radialUp, shoreWaveFade,
                    tideMask, t, waveCrestFoam);
                float3 N = normalize(lerp(radialUp, waveN, saturate(_FlowNormalStrength)));
                if (isSideFace) N = geoN;
                else N = normalize(N - flowTangentWS * flowCos * flowActivity * flowShoreFade
                    * saturate(_FlowNormalStrength) * 0.11);
                float3 detailN = normalize(float3(dot(N,tanA), dot(N,radialUp), dot(N,tanB)));

                float2 refractUV = screenUV + N.xz * _RefractionStrength;
                float3 refracted = SampleSceneColor(refractUV).rgb;
                if (length(refracted) < 0.001f) refracted = _DeepColor.rgb;

                float shoreFactor = saturate(1.0 - depthDiff / _ShoreOpaqueDepth);
                float sideDeepBoost = 0.0;
                float tidalTint = saturate(tideMask) * 0.08;
                float4 waterCol = lerp(_ShallowColor, _DeepColor, saturate(deep01 + sideDeepBoost));
                waterCol.rgb = lerp(waterCol.rgb, waterCol.rgb * float3(0.82, 0.92, 1.08), tidalTint);
                // -- Colour patchiness (9.16.0): slow large-scale brightness variation so
                // broad surfaces never look like one flat fill. --
                waterCol.rgb *= 1.0 + (FBM(surfUV * 0.07) - 0.5) * min(_Patchiness,0.12);

                float validDepth = step(0.05, depthDiff);
                float shoreFoamFade = saturate(1.0 - depthDiff / (_ShoreFoamWidth * 0.7));
                float shoreFoam = shoreFoamFade * validDepth * _ShoreFoamIntensity * saturate(1.0 - shoreAtten) * 0.45;
                // Crest foam is phase-locked to the same wind-driven swell as the normals,
                // so whitecaps travel with the waves instead of crawling as unrelated noise.
                float crestFoam = waveCrestFoam * 0.34;
                // A separate, gently warped crest band travels with the existing solver flow
                // vector; its visual strength follows the solver's smoothed flow decay.
                float2 foamScrollUV = surfUV - flowDirectionUV * t * (0.07 + flowSpeedClamped * 0.12);
                float flowFoam = flowCrestMask * flowActivity * flowShoreFade * _FlowFoamStrength * 0.52;
                flowFoam *= saturate(FBM(foamScrollUV * 1.5 + flowPerpUV * flowWarp) * 1.5);
                float wakeFoam = NativeWakeFoam(i.posWS, radialUp);
                float foam = saturate(shoreFoam + crestFoam + flowFoam + wakeFoam);

                float NdV = saturate(dot(V, N));
                float fresnel = pow(1.0 - NdV, _FresnelPower);

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(V + L);
                float specBroad = pow(saturate(dot(N, H)), lerp(80.0, 900.0, _Gloss)) * 0.7;
                float specTight = pow(saturate(dot(N, H)), 2400.0) * 1.2;
                float glitterMask = 0.0;
                float glitter = pow(saturate(dot(N, H)), 3200.0) * glitterMask * 2.5 * _SparkleStrength;
                float sssWrap = pow(saturate(dot(V, -L)), 3.0) * (1.0 - deep01) * _SSSIntensity;
                float3 sssColor = mainLight.color.rgb * sssWrap * float3(0.12, 0.75, 0.55);
                float caustic = pow(saturate(FBM(surfUV * 0.65 + N.xz * 1.8 - t * 0.18)), 3.0)
                    * _CausticsIntensity * (1.0 - deep01) * shoreWaveFade;

                float refractWeight = (1.0 - deep01) * (1.0 - fresnel) * 0.22;
                refractWeight *= (1.0 - shoreFactor * 0.9);
                float3 col = lerp(waterCol.rgb, refracted, refractWeight);
                float3 sky = SampleSH(N) * 0.85 + mainLight.color.rgb * 0.10;
                sky=max(sky,float3(0.16,0.24,0.3));
                col = lerp(col, sky, fresnel * 0.35);
                col += mainLight.color.rgb * (specBroad + specTight + glitter) * saturate(mainLight.distanceAttenuation);
                col += sssColor;
                col += caustic * float3(0.45, 0.95, 1.0);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                // -- Thin-film iridescence (9.16.0): the rainbow sheen of refined
                // products. The hue cycles with the view angle + surface detail so
                // oil and fuel pools shimmer exactly like real spills. --
                float iridT = fresnel * _IridescenceScale + detailN.y * 0.35f + screenDepth01 * 0.3f;
                float3 irid = 0.5 + 0.5 * cos(6.28318 * (iridT + float3(0.0, 0.33, 0.67)));
                col = lerp(col, col * irid, _IridescenceStrength * saturate(fresnel * 1.4f));

                // -- Emission (9.16.0): glowing liquids (engine coolant) read even
                // in the dark, with a slow breathing pulse. --
                col += _EmissionColor.rgb * _EmissionStrength
                     * (0.85 + 0.15 * sin(t * 2.1 + surfUV.x * 2.6));

                float alpha = waterCol.a;
                // Voxel-authored shallow water is intentionally clearer so beaches
                // and lake beds remain visible. Deep water keeps the dense ocean body.
                alpha = lerp(0.72, alpha, saturate(deep01 * 1.35));
                // Do not force shallow shore intersections to opaque black bands.
                alpha = lerp(alpha, min(alpha + 0.12, 0.99), fresnel);
                alpha = max(alpha, lerp(0.48, 0.94, deep01));
                // Soft intersection, not a bright opaque jagged band at the bank.
                alpha *= hasValidDepth ? saturate(depthDiff / 0.18) : 1.0;
                // Scene depth can be unavailable on some render paths, so also use the
                // voxel-authored water-to-bank thickness. Keep a visible opacity floor to
                // avoid transparent pinholes while blending the last few metres into shore.
                float bankFade = smoothstep(0.05, 0.35, geometryDepth01);
                float bankOpacity = lerp(0.32, 1.0, bankFade);
                alpha *= bankOpacity;
                alpha = lerp(alpha, min(alpha + foam * 0.3, 0.99), foam);

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
