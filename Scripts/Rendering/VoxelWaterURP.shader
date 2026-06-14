// Assets/Scripts/VoxelEngine/Rendering/VoxelWaterURP.shader
//
// V5: No depth-based shore foam (geometry foam from WaterMeshBuilder handles it).
// This eliminates ALL chunk-boundary foam artifacts.

Shader "VoxelEngine/VoxelWaterURP"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow", Color) = (0.08, 0.52, 0.82, 0.65)
        _DeepColor    ("Deep",    Color) = (0.01, 0.06, 0.22, 0.92)
        _FoamColor    ("Foam",    Color) = (0.92, 0.96, 1.00, 0.88)

        [Header(Ocean Waves)]
        _WaveAmp   ("Wave Amplitude", Range(0, 1.2)) = 0.35
        _WaveFreq  ("Wave Frequency", Range(0.05, 4)) = 0.55
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 0.72
        _WaveChop  ("Wave Chop", Range(0, 1)) = 0.28

        [Header(Surface Detail)]
        _NormalScale        ("Normal Strength", Range(0, 3)) = 1.4
        _Gloss              ("Gloss", Range(0, 1)) = 0.96
        _FresnelPower       ("Fresnel Power", Range(1, 8)) = 3.2
        _RefractionStrength ("Refraction", Range(0, 0.08)) = 0.032
        _CausticsIntensity  ("Caustics", Range(0, 1)) = 0.25

        [Header(Depth Coloring)]
        _DepthFade ("Depth Fade Dist", Range(0.1, 20)) = 5.0

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
                float  _WaveAmp, _WaveFreq, _WaveSpeed, _WaveChop;
                float  _NormalScale;
                float  _Gloss, _FresnelPower, _RefractionStrength, _CausticsIntensity;
                float  _DepthFade;
                float  _SSSIntensity;
                float  _FlowNormalStrength, _FlowFoamStrength;
            CBUFFER_END

            struct A2V
            {
                float4 posOS  : POSITION;
                float3 normOS : NORMAL;
                float2 uv     : TEXCOORD0;
                float2 uv2    : TEXCOORD1;
            };

            struct V2F
            {
                float4 posCS  : SV_POSITION;
                float3 posWS  : TEXCOORD0;
                float3 normWS : TEXCOORD1;
                float  fog    : TEXCOORD2;
                float4 scrPos : TEXCOORD3;
                float2 flowUV : TEXCOORD4;
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
            { dir = normalize(dir); float phase = dot(xz, dir) * freq + t * speed; float s, c; sincos(phase, s, c); return float3(dir.x * amp * c * chop, amp * s, dir.y * amp * c * chop); }

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

                // Gerstner waves only on top-facing surfaces
                if (i.normOS.y > 0.5)
                {
                    float t = _Time.y; float amp = _WaveAmp; float3 w = 0;
                    w += Gerstner(worldPos.xz, float2( 1.00,  0.23), amp,        _WaveFreq,        _WaveSpeed,        _WaveChop, t);
                    w += Gerstner(worldPos.xz, float2(-0.42,  0.91), amp * 0.52, _WaveFreq * 1.7,  _WaveSpeed * 1.31, _WaveChop, t);
                    w += Gerstner(worldPos.xz, float2( 0.18, -0.98), amp * 0.24, _WaveFreq * 3.1,  _WaveSpeed * 0.76, _WaveChop, t);
                    w += Gerstner(worldPos.xz, float2( 0.72,  0.69), amp * 0.12, _WaveFreq * 5.4,  _WaveSpeed * 1.9,  _WaveChop, t);
                    w += Gerstner(worldPos.xz, float2(-0.55, -0.45), amp * 0.08, _WaveFreq * 7.8,  _WaveSpeed * 2.4,  _WaveChop, t);
                    posOS += w;
                    worldPos = TransformObjectToWorld(posOS);
                }

                o.posWS  = worldPos;
                o.posCS  = TransformWorldToHClip(worldPos);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog    = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                o.flowUV = i.uv2;
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float t = _Time.y;
                float3 V = normalize(_WorldSpaceCameraPos - i.posWS);
                float3 geoN = normalize(i.normWS);
                float2 flowDir = i.flowUV;
                float flowSpeed = length(flowDir);

                // Detail normals
                float3 detailN = FlowMappedNormal(i.posWS.xz, flowDir, flowSpeed, t);
                float3 N = normalize(float3(detailN.x, 1.0, detailN.z));
                N = normalize(lerp(geoN, N, saturate(abs(geoN.y))));

                // Depth & refraction
                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float2 refractUV = screenUV + N.xz * _RefractionStrength;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                float depthDiff = max(0, sceneEyeDepth - waterEyeDepth);
                float deep01 = saturate(depthDiff / _DepthFade);
                float3 refracted = SampleSceneColor(refractUV).rgb;

                // Water color
                float4 waterCol = lerp(_ShallowColor, _DeepColor, deep01);

                // Foam — NO depth-based shore foam (geometry foam from mesh builder instead)
                // Only crest foam and flow foam here
                float crest = saturate((FBM(i.posWS.xz * 0.22 + t * 0.075) - 0.58) * 3.0) * saturate(_WaveAmp * 2.5);
                float lace = FBM(i.posWS.xz * 0.85 + float2(t * 0.12, -t * 0.08));
                float flowFoam = saturate(flowSpeed * 3.0 - 0.2) * _FlowFoamStrength;
                float2 foamScrollUV = i.posWS.xz + normalize(flowDir + 0.001) * t * 0.3;
                flowFoam *= saturate(FBM(foamScrollUV * 1.5) * 1.5);
                float foam = saturate(crest * lace * 0.75 + flowFoam);

                // Fresnel
                float NdV = saturate(dot(V, N));
                float fresnel = pow(1.0 - NdV, _FresnelPower);

                // Lighting
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(V + L);
                float specBroad = pow(saturate(dot(N, H)), lerp(80.0, 900.0, _Gloss)) * 0.7;
                float specTight = pow(saturate(dot(N, H)), 2400.0) * 1.2;
                float glitterMask = pow(saturate(FBM6(i.posWS.xz * 2.8 + t * 0.15)), 8.0);
                float glitter = pow(saturate(dot(N, H)), 3200.0) * glitterMask * 2.5;

                // SSS
                float sssWrap = pow(saturate(dot(V, -L)), 3.0) * (1.0 - deep01) * _SSSIntensity;
                float3 sssColor = mainLight.color.rgb * sssWrap * float3(0.12, 0.75, 0.55);

                // Caustics
                float caustic = pow(saturate(FBM(i.posWS.xz * 0.65 + N.xz * 1.8 - t * 0.18)), 3.0) * _CausticsIntensity * (1.0 - deep01);

                // Compose
                float refractWeight = (1.0 - deep01) * (1.0 - fresnel) * 0.55;
                float3 col = lerp(waterCol.rgb, refracted, refractWeight);
                float3 sky = SampleSH(N) * 0.85 + mainLight.color.rgb * 0.10;
                col = lerp(col, sky, fresnel * 0.35);
                col += mainLight.color.rgb * (specBroad + specTight + glitter) * saturate(mainLight.distanceAttenuation);
                col += sssColor;
                col += caustic * float3(0.45, 0.95, 1.0);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                // Alpha — more opaque overall so terrain below doesn't look like a "second layer"
                float alpha = waterCol.a;
                alpha = lerp(alpha * 0.72, alpha, deep01);
                alpha = lerp(alpha, min(alpha + 0.18, 0.97), fresnel);
                alpha = max(alpha, 0.42);
                alpha = lerp(alpha, min(alpha + foam * 0.4, 0.98), foam);

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
