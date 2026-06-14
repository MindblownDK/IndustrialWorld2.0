// Assets/Scripts/VoxelEngine/Rendering/VoxelWaterURP.shader
//
// IndustrialWorld high-end procedural URP water/oil shader.
// Inspired by Sea of Thieves style ocean readability and Evan Wallace-style
// refractive water: layered world-space Gerstner waves, refracted opaque scene
// color, shoreline foam, sun glitter, Fresnel sky tint and procedural caustic
// shimmer. No texture assets required.

Shader "VoxelEngine/VoxelWaterURP"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow", Color) = (0.10, 0.78, 0.86, 0.62)
        _DeepColor    ("Deep",    Color) = (0.015, 0.12, 0.34, 0.90)
        _FoamColor    ("Foam",    Color) = (0.88, 0.96, 1.00, 0.88)

        [Header(Ocean Waves)]
        _WaveAmp   ("Wave Amplitude", Range(0, 1.2)) = 0.34
        _WaveFreq  ("Wave Frequency", Range(0.05, 4)) = 0.62
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 0.78
        _WaveChop  ("Wave Chop", Range(0, 1)) = 0.32

        [Header(Surface Detail)]
        _NormalScale        ("Normal Strength", Range(0, 3)) = 1.25
        _NormalSpeed1       ("Normal Scroll 1", Vector) = (0.055, 0.035, 0, 0)
        _NormalSpeed2       ("Normal Scroll 2", Vector) = (-0.035, 0.065, 0, 0)
        _Gloss              ("Gloss", Range(0, 1)) = 0.98
        _FresnelPower       ("Fresnel Power", Range(1, 8)) = 3.1
        _RefractionStrength ("Refraction", Range(0, 0.08)) = 0.026
        _CausticsIntensity  ("Caustics", Range(0, 1)) = 0.18

        [Header(Shore Foam)]
        _DepthFade     ("Depth Fade Dist", Range(0.1, 20)) = 4.2
        _FoamWidth     ("Foam Line Width", Range(0.01, 5)) = 0.85
        _FoamIntensity ("Foam Intensity", Range(0, 2)) = 1.05
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
            Cull Off

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
                float4 _NormalSpeed1, _NormalSpeed2;
                float  _Gloss, _FresnelPower, _RefractionStrength, _CausticsIntensity;
                float  _DepthFade, _FoamWidth, _FoamIntensity;
            CBUFFER_END

            struct A2V
            {
                float4 posOS  : POSITION;
                float3 normOS : NORMAL;
                float2 uv     : TEXCOORD0;
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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float FBM(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    v += ValueNoise(p) * a;
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            float3 Gerstner(float2 xz, float2 dir, float amp, float freq, float speed, float chop, float t)
            {
                dir = normalize(dir);
                float phase = dot(xz, dir) * freq + t * speed;
                float s, c;
                sincos(phase, s, c);
                return float3(dir.x * amp * c * chop, amp * s, dir.y * amp * c * chop);
            }

            float3 ProceduralNormal(float2 worldXZ, float t)
            {
                // Multi-scale ripples: broad swell + tight capillary shimmer.
                float2 uv1 = worldXZ * 0.090 + _NormalSpeed1.xy * t;
                float2 uv2 = worldXZ * 0.170 + _NormalSpeed2.xy * t;
                float2 uv3 = worldXZ * 0.430 + float2(-0.02, 0.025) * t;

                float h  = FBM(uv1 * 5.0) * 0.55;
                      h += FBM(uv2 * 8.0) * 0.32;
                      h += FBM(uv3 * 11.0) * 0.13;

                float eps = 0.075;
                float hx = FBM((uv1 + float2(eps, 0)) * 5.0) * 0.55 + FBM((uv2 + float2(eps, 0)) * 8.0) * 0.32 + FBM((uv3 + float2(eps, 0)) * 11.0) * 0.13;
                float hz = FBM((uv1 + float2(0, eps)) * 5.0) * 0.55 + FBM((uv2 + float2(0, eps)) * 8.0) * 0.32 + FBM((uv3 + float2(0, eps)) * 11.0) * 0.13;

                float3 n = normalize(float3((h - hx) * _NormalScale, 1.0, (h - hz) * _NormalScale));
                return n;
            }

            V2F vert(A2V i)
            {
                V2F o = (V2F)0;
                float3 posOS = i.posOS.xyz;
                float3 worldPos = TransformObjectToWorld(posOS);

                if (i.normOS.y > 0.35)
                {
                    float t = _Time.y;
                    float amp = _WaveAmp;
                    float3 w = 0;
                    w += Gerstner(worldPos.xz, float2( 1.00,  0.23), amp,        _WaveFreq,        _WaveSpeed,        _WaveChop, t);
                    w += Gerstner(worldPos.xz, float2(-0.42,  0.91), amp * 0.52, _WaveFreq * 1.7,  _WaveSpeed * 1.31, _WaveChop, t);
                    w += Gerstner(worldPos.xz, float2( 0.18, -0.98), amp * 0.24, _WaveFreq * 3.1,  _WaveSpeed * 0.76, _WaveChop, t);
                    w += Gerstner(worldPos.xz, float2( 0.72,  0.69), amp * 0.12, _WaveFreq * 5.4,  _WaveSpeed * 1.9,  _WaveChop, t);
                    // Chunks are authored unscaled/unrotated, so world-space wave offset
                    // maps directly to object-space and stays seamless across chunks.
                    posOS += w;
                    worldPos = TransformObjectToWorld(posOS);
                }

                o.posWS = worldPos;
                o.posCS = TransformWorldToHClip(worldPos);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                o.flowUV = i.uv;
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float t = _Time.y;
                float3 V = normalize(_WorldSpaceCameraPos - i.posWS);
                float3 geoN = normalize(i.normWS);
                float3 detailN = ProceduralNormal(i.posWS.xz, t);
                float3 N = normalize(float3(detailN.x, 1.0, detailN.z));
                N = normalize(lerp(geoN, N, saturate(abs(geoN.y))));

                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float2 refractUV = screenUV + N.xz * _RefractionStrength;

                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                float depthDiff = max(0, sceneEyeDepth - waterEyeDepth);
                float deep01 = saturate(depthDiff / _DepthFade);

                // Evan Wallace-like scene refraction. If opaque texture is disabled,
                // URP returns a harmless fallback and the shader still looks good.
                float3 refracted = SampleSceneColor(refractUV).rgb;

                float4 waterCol = lerp(_ShallowColor, _DeepColor, deep01);

                // Shoreline and breaking crest foam.
                float shoreFoam = 1.0 - saturate(depthDiff / _FoamWidth);
                shoreFoam = shoreFoam * shoreFoam * _FoamIntensity;
                float crest = saturate((FBM(i.posWS.xz * 0.22 + t * 0.075) - 0.62) * 3.0) * saturate(_WaveAmp * 2.0);
                float lace = FBM(i.posWS.xz * 0.85 + float2(t * 0.12, -t * 0.08));
                float foam = saturate(shoreFoam + crest * lace * 0.75);

                float NdV = saturate(dot(V, N));
                float fresnel = pow(1.0 - NdV, _FresnelPower);

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(V + L);
                float spec = pow(saturate(dot(N, H)), lerp(80.0, 900.0, _Gloss)) * 0.85;
                float glitterMask = pow(saturate(FBM(i.posWS.xz * 2.2 + t * 0.45)), 8.0);
                float glitter = pow(saturate(dot(N, H)), 1800.0) * glitterMask * 2.2;

                float caustic = pow(saturate(FBM(i.posWS.xz * 0.65 + N.xz * 1.5 - t * 0.18)), 3.0) * _CausticsIntensity * (1.0 - deep01);

                // Compose: shallow water reveals refracted scene, deep water becomes
                // saturated blue/green, grazing angles reflect sky/ambient.
                float refractWeight = (1.0 - deep01) * (1.0 - fresnel) * 0.55;
                float3 col = lerp(waterCol.rgb, refracted, refractWeight);
                float3 sky = SampleSH(N) * 0.85 + mainLight.color.rgb * 0.10;
                col = lerp(col, sky, fresnel * 0.32);
                col += mainLight.color.rgb * (spec + glitter) * saturate(mainLight.distanceAttenuation);
                col += caustic * float3(0.45, 0.95, 1.0);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                float alpha = waterCol.a;
                alpha = lerp(alpha * 0.72, alpha, deep01);
                alpha = lerp(alpha, min(alpha + 0.18, 0.97), fresnel);
                alpha = max(alpha, 0.38);
                if (rawDepth < 0.0001 || sceneEyeDepth > 10000) alpha = max(alpha, 0.62);

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
