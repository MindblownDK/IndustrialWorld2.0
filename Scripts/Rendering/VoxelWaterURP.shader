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
            // 0 = glass calm, 1 = full storm sea. The swell also leans into the wind.
            float  _WeatherSeaState;
            float4 _WeatherWindDirWS;

            float SeaAmp()   { return 1.0 + saturate(_WeatherSeaState) * 1.45; }   // wave height
            float SeaSpeed() { return 1.0 + saturate(_WeatherSeaState) * 0.35; }   // waves run harder
            float SeaChop(float chop) { return saturate(chop * (1.0 + saturate(_WeatherSeaState) * 1.10)); }
            float SeaFoam()  { return 1.0 + saturate(_WeatherSeaState) * 1.80; }   // whitecaps

            /// Blends an authored wave bearing toward the wind as the sea builds.
            float2 SeaDir(float2 authored, float2 windDir)
            {
                float w = saturate(_WeatherSeaState) * 0.75;
                float2 d = lerp(normalize(authored), windDir, w);
                float len = length(d);
                return len > 0.0001 ? d / len : normalize(authored);
            }


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

            float3 Gerstner(float2 xz, float2 dir, float amp, float freq, float speed, float chop, float t)
            {
                dir = normalize(dir);
                float phase = dot(xz, dir) * freq + t * speed;
                float s, c; sincos(phase, s, c);
                return float3(dir.x * amp * c * chop, amp * s, dir.y * amp * c * chop);
            }

            float3 PlanetWave(float3 worldPos, float3 radialUp, float2 flow, float deepAmp, float shoreAtten, float tideMask, float t)
            {
                // Fixed body-centred 3D phase. Projecting the radial position on a
                // per-vertex tangent plane collapses the coordinates and creates streaks.
                float3 p = worldPos - _VoxelWaterBodyCenter.xyz;
                float tide = 1.0 + tideMask * _TideStrength;
                float3 d1 = normalize(float3(1.0, 0.23, 0.37));
                float3 d2 = normalize(float3(-0.42, 0.61, 0.91));
                float3 wind = _WeatherWindDirWS.xyz;
                if (dot(wind,wind)>0.001) d1=normalize(lerp(d1,normalize(wind),saturate(_WeatherSeaState)*0.6));
                float phase1 = dot(p,d1)*_DeepWaveFrequency + t*_DeepWaveSpeed*SeaSpeed();
                float phase2 = dot(p,d2)*_SecondaryWaveFrequency + t*_SecondaryWaveSpeed*SeaSpeed();
                float height = sin(phase1)*deepAmp + sin(phase2)*_SecondaryWaveAmplitude;
                height += sin(dot(p,normalize(float3(0.7,-0.3,0.5)))*_ShallowWaveFrequency+t*_ShallowWaveSpeed)*_ShallowWaveAmplitude;
                // Radial-only displacement keeps banks and shared intersections stable.
                return radialUp * clamp(height * tide * SeaAmp(), -0.2, 0.2) * shoreAtten;
            }

            float3 FlowMappedNormal(float2 uv, float2 flowDir, float flowSpeed, float t)
            {
                float2 p = uv - flowDir * t * 0.12;
                float a = dot(p,float2(0.35,0.17))+t*0.6;
                float b = dot(p,float2(-0.19,0.42))-t*0.45;
                float2 slope = float2(cos(a)*0.35-cos(b)*0.19,cos(a)*0.17+cos(b)*0.42);
                return normalize(float3(-slope.x*_NormalScale*0.14,1,-slope.y*_NormalScale*0.14));
            }

            V2F vert(A2V i)
            {
                V2F o = (V2F)0;
                float3 posOS = i.posOS.xyz;
                float3 worldPos = TransformObjectToWorld(posOS);

                float3 radialUp = NativeWaterUp(worldPos);
                float topFacing = saturate(dot(TransformObjectToWorldNormal(i.normOS), radialUp));
                float shoreDepthMask = saturate(i.color.r);
                float tideMask = i.color.g;
                float shoreAtten = saturate(shoreDepthMask * (_ShoreBlendDistance / max(_ShoreBlendDistance, 0.0001)));

                // Topology is voxel-owned. Per-triangle depth and normals MUST NOT move
                // duplicate edge vertices apart; surface motion is normal-only.
                o.posWS  = worldPos;
                o.posCS  = TransformWorldToHClip(worldPos);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog    = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                o.flowUV = i.uv2;
                // data.w carries geometry-authored water depth (0 shallow .. 1 deep).
                // The procedural patch renderer writes this from voxel water depth so
                // shallow beaches/lakes still look clear even when camera depth is unavailable.
                o.data = float4(shoreDepthMask, tideMask, topFacing, saturate(i.color.b));
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
                float geometryDepth01 = saturate(i.data.w);

                float3 radialUp = NativeWaterUp(i.posWS);
                float3 tanA = cross(radialUp, float3(0,1,0));
                if (dot(tanA, tanA) < 0.001) tanA = cross(radialUp, float3(0,0,1));
                tanA = normalize(tanA);
                float3 tanB = normalize(cross(radialUp, tanA));
                float3 surfaceCoord = i.posWS - _VoxelWaterBodyCenter.xyz;
                float2 surfUV = float2(dot(surfaceCoord,float3(0.73,0.39,0.56)),dot(surfaceCoord,float3(-0.42,0.86,0.28)));
                bool isSideFace = dot(geoN,radialUp) < 0.3;

                float3 detailN = FlowMappedNormal(surfUV, flowDir, flowSpeed, t);
                float3 worldDetailN = normalize(tanA * detailN.x + radialUp * detailN.y + tanB * detailN.z);
                float3 N = normalize(lerp(radialUp, worldDetailN, 0.92));

                // -- Fine ripple layer (9.16.0): a tighter animated noise octave riding on
                // the detail normal so every liquid reads as textured, never as glass. --
                float2 ripBase = surfUV * _DetailScale + float2(t * 0.34, -t * 0.21);
                if (isSideFace) N = geoN;

                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float2 refractUV = screenUV + N.xz * _RefractionStrength;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                bool hasValidDepth = rawDepth > 0.00001f && rawDepth < 0.99999f;
                float depthDiff = hasValidDepth ? max(0, sceneEyeDepth - waterEyeDepth) : 15.0f;
                float screenDepth01 = saturate(depthDiff / _DepthFade);
                float deep01 = geometryDepth01;
                float shoreAtten = saturate(shoreDepthMask);
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
                // Whitecaps: a storm sea breaks far more often than a calm one.
                float crestThreshold = lerp(0.62, 0.44, saturate(_WeatherSeaState));
                float crest = saturate((FBM(surfUV * 0.25 + t * 0.08) - crestThreshold) * 3.5)
                            * saturate(_DeepWaveAmplitude * 1.5 * SeaFoam());
                float lace = FBM(surfUV * 0.85 + float2(t * 0.12, -t * 0.08));
                float crestFoam = crest * lace * 0.35;
                float flowFoam = saturate(flowSpeed - 1.2) * _FlowFoamStrength * 0.4;
                float2 foamScrollUV = surfUV + normalize(flowDir + 0.001) * t * 0.3;
                flowFoam *= saturate(FBM(foamScrollUV * 1.5) * 1.5);
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
                float caustic = pow(saturate(FBM(surfUV * 0.65 + N.xz * 1.8 - t * 0.18)), 3.0) * _CausticsIntensity * (1.0 - deep01);

                float refractWeight = (1.0 - deep01) * (1.0 - fresnel) * 0.22;
                refractWeight *= (1.0 - shoreFactor * 0.9);
                float3 col = lerp(waterCol.rgb, refracted, refractWeight);
                float3 sky = SampleSH(N) * 0.85 + mainLight.color.rgb * 0.10;
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
                alpha = max(alpha, lerp(0.70, 0.94, deep01));
                alpha = lerp(alpha, min(alpha + foam * 0.3, 0.99), foam);

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
