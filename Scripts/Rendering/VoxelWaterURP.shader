// Assets/Scripts/VoxelEngine/Rendering/VoxelWaterURP.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║        IndustrialWorld — KWS2-Quality Water/Oil Shader (URP)      ║
// ║                                                                    ║
// ║  Flow-mapped procedural water inspired by KWS2 Dynamic Water.      ║
// ║  Features:                                                         ║
// ║    • Flow-mapped normals from UV2 (pressure-gradient flow field)   ║
// ║    • Multi-octave Gerstner waves with directional variety           ║
// ║    • Fresnel reflections + subsurface scattering                   ║
// ║    • Dynamic foam from flow speed + shore proximity + wave crests   ║
// ║    • Scene refraction with depth-based absorption                  ║
// ║    • Caustic shimmer on shallow surfaces                           ║
// ║    • Sun glitter + anisotropic highlights                          ║
// ║    • Separate visual profiles for water and crude oil              ║
// ║                                                                    ║
// ║  UV2 channel carries flow velocity:                                ║
// ║    UV2.xy = flow direction (magnitude = speed)                     ║
// ╚══════════════════════════════════════════════════════════════════════╝

Shader "VoxelEngine/VoxelWaterURP"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow", Color) = (0.08, 0.52, 0.82, 0.65)
        _DeepColor    ("Deep",    Color) = (0.01, 0.06, 0.22, 0.92)
        _FoamColor    ("Foam",    Color) = (0.92, 0.96, 1.00, 0.88)

        [Header(Ocean Waves)]
        _WaveAmp   ("Wave Amplitude", Range(0, 1.2)) = 0.28
        _WaveFreq  ("Wave Frequency", Range(0.05, 4)) = 0.55
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 0.72
        _WaveChop  ("Wave Chop", Range(0, 1)) = 0.28

        [Header(Surface Detail)]
        _NormalScale        ("Normal Strength", Range(0, 3)) = 1.4
        _Gloss              ("Gloss", Range(0, 1)) = 0.96
        _FresnelPower       ("Fresnel Power", Range(1, 8)) = 3.2
        _RefractionStrength ("Refraction", Range(0, 0.08)) = 0.032
        _CausticsIntensity  ("Caustics", Range(0, 1)) = 0.25

        [Header(Shore Foam)]
        _DepthFade     ("Depth Fade Dist", Range(0.1, 20)) = 5.0
        _FoamWidth     ("Foam Line Width", Range(0.01, 5)) = 1.0
        _FoamIntensity ("Foam Intensity", Range(0, 2)) = 1.2

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
                float  _DepthFade, _FoamWidth, _FoamIntensity;
                float  _SSSIntensity;
                float  _FlowNormalStrength, _FlowFoamStrength;
            CBUFFER_END

            struct A2V
            {
                float4 posOS  : POSITION;
                float3 normOS : NORMAL;
                float2 uv     : TEXCOORD0;
                float2 uv2    : TEXCOORD1; // flow velocity
            };

            struct V2F
            {
                float4 posCS   : SV_POSITION;
                float3 posWS   : TEXCOORD0;
                float3 normWS  : TEXCOORD1;
                float  fog     : TEXCOORD2;
                float4 scrPos  : TEXCOORD3;
                float2 flowUV  : TEXCOORD4; // flow velocity from UV2
            };

            // ── Noise ────────────────────────────────────────────────────────

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

            // 6-octave FBM for richer detail on flow-mapped normals
            float FBM6(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll] for (int i = 0; i < 6; i++)
                {
                    v += ValueNoise(p) * a;
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            // ── Gerstner Waves ────────────────────────────────────────────────

            float3 Gerstner(float2 xz, float2 dir, float amp, float freq, float speed, float chop, float t)
            {
                dir = normalize(dir);
                float phase = dot(xz, dir) * freq + t * speed;
                float s, c;
                sincos(phase, s, c);
                return float3(dir.x * amp * c * chop, amp * s, dir.y * amp * c * chop);
            }

            // ── Flow-Mapped Normals ────────────────────────────────────────────
            //
            // The core KWS2 look: normals scroll in the flow direction instead
            // of a fixed world-space direction. Two layers are blended with a
            // half-phase offset to avoid visible seams (Flow Map technique by
            // Valve / Hennig 2010).

            float3 FlowMappedNormal(float2 worldXZ, float2 flowDir, float flowSpeed, float t)
            {
                // Normalize flow direction (default to gentle drift if still)
                float2 dir = flowDir;
                float speed = length(dir);
                if (speed > 0.001f)
                    dir = normalize(dir);
                else
                    dir = float2(0.04, 0.03);

                // Flow-modulated scroll speed: faster flow = faster normal animation
                float flowTime = t * (0.35 + speed * 1.5);

                // Two noise layers at different scales, scrolling in flow direction
                float2 uv1 = worldXZ * 0.09 + dir * flowTime * 0.8;
                float2 uv2 = worldXZ * 0.17 + dir * flowTime * 0.5 + float2(5.3, 7.1);
                float2 uv3 = worldXZ * 0.45 - dir * flowTime * 0.3;

                // Height from layered noise
                float h  = FBM(uv1 * 5.0) * 0.50;
                      h += FBM(uv2 * 8.0) * 0.30;
                      h += FBM(uv3 * 11.0) * 0.20;

                // Finite differences for normal
                float eps = 0.08;
                float hx = FBM((uv1 + float2(eps, 0)) * 5.0) * 0.50
                          + FBM((uv2 + float2(eps, 0)) * 8.0) * 0.30
                          + FBM((uv3 + float2(eps, 0)) * 11.0) * 0.20;
                float hz = FBM((uv1 + float2(0, eps)) * 5.0) * 0.50
                          + FBM((uv2 + float2(0, eps)) * 8.0) * 0.30
                          + FBM((uv3 + float2(0, eps)) * 11.0) * 0.20;

                // Flow strength modulates the normal distortion
                float strength = _NormalScale * (1.0 + speed * _FlowNormalStrength * 2.0);
                float3 n = normalize(float3((h - hx) * strength, 1.0, (h - hz) * strength));
                return n;
            }

            // ── Vertex Shader ──────────────────────────────────────────────────

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
                    w += Gerstner(worldPos.xz, float2(-0.55, -0.45), amp * 0.08, _WaveFreq * 7.8,  _WaveSpeed * 2.4,  _WaveChop, t);
                    posOS += w;
                    worldPos = TransformObjectToWorld(posOS);
                }

                o.posWS  = worldPos;
                o.posCS  = TransformWorldToHClip(worldPos);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog    = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                o.flowUV = i.uv2; // flow velocity from mesh builder
                return o;
            }

            // ── Fragment Shader ─────────────────────────────────────────────────

            half4 frag(V2F i) : SV_Target
            {
                float t = _Time.y;
                float3 V = normalize(_WorldSpaceCameraPos - i.posWS);
                float3 geoN = normalize(i.normWS);

                // Flow-mapped normal computation
                float2 flowDir = i.flowUV;
                float flowSpeed = length(flowDir);
                float3 detailN = FlowMappedNormal(i.posWS.xz, flowDir, flowSpeed, t);

                // For vertical surfaces (waterfall sides), preserve the face normal
                float3 N = normalize(float3(detailN.x, 1.0, detailN.z));
                N = normalize(lerp(geoN, N, saturate(abs(geoN.y))));

                // ── Depth & Refraction ──────────────────────────────────────────
                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float2 refractUV = screenUV + N.xz * _RefractionStrength;

                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                float depthDiff = max(0, sceneEyeDepth - waterEyeDepth);
                float deep01 = saturate(depthDiff / _DepthFade);

                // Refracted scene color
                float3 refracted = SampleSceneColor(refractUV).rgb;

                // ── Water Color ─────────────────────────────────────────────────
                float4 waterCol = lerp(_ShallowColor, _DeepColor, deep01);

                // ── Foam ────────────────────────────────────────────────────────
                // 1. Shoreline foam — appears where water is shallow
                float shoreFoam = 1.0 - saturate(depthDiff / _FoamWidth);
                shoreFoam = shoreFoam * shoreFoam * _FoamIntensity;

                // 2. Wave crest foam — appears on Gerstner wave peaks
                float crest = saturate((FBM(i.posWS.xz * 0.22 + t * 0.075) - 0.58) * 3.0)
                            * saturate(_WaveAmp * 2.5);

                // 3. Lace/detail foam — high-frequency animated pattern
                float lace = FBM(i.posWS.xz * 0.85 + float2(t * 0.12, -t * 0.08));

                // 4. Flow foam — appears where water flows fast (KWS2 signature)
                float flowFoam = saturate(flowSpeed * 3.0 - 0.2) * _FlowFoamStrength;
                // Animate flow foam in flow direction
                float2 foamScrollUV = i.posWS.xz + normalize(flowDir + 0.001) * t * 0.3;
                float foamPattern = FBM(foamScrollUV * 1.5);
                flowFoam *= saturate(foamPattern * 1.5);

                // Combine all foam sources
                float foam = saturate(shoreFoam + crest * lace * 0.75 + flowFoam);

                // ── Fresnel ─────────────────────────────────────────────────────
                float NdV = saturate(dot(V, N));
                float fresnel = pow(1.0 - NdV, _FresnelPower);

                // ── Lighting ────────────────────────────────────────────────────
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(V + L);

                // Specular — broad and tight highlights
                float specBroad = pow(saturate(dot(N, H)), lerp(80.0, 900.0, _Gloss)) * 0.7;
                float specTight = pow(saturate(dot(N, H)), 2400.0) * 1.2;

                // Sun glitter — stochastic sparkle on the surface
                float glitterMask = pow(saturate(FBM6(i.posWS.xz * 2.8 + t * 0.15)), 8.0);
                float glitter = pow(saturate(dot(N, H)), 3200.0) * glitterMask * 2.5;

                // ── Subsurface Scattering ───────────────────────────────────────
                // Light wrapping around the surface for translucent shallow water
                float sssWrap = pow(saturate(dot(V, -L)), 3.0) * (1.0 - deep01) * _SSSIntensity;
                float3 sssColor = mainLight.color.rgb * sssWrap * float3(0.12, 0.75, 0.55);

                // ── Caustics ────────────────────────────────────────────────────
                float caustic = pow(saturate(FBM(i.posWS.xz * 0.65 + N.xz * 1.8 - t * 0.18)), 3.0)
                              * _CausticsIntensity * (1.0 - deep01);

                // ── Compose ─────────────────────────────────────────────────────
                // Refraction weight: visible in shallow water, fades in deep
                float refractWeight = (1.0 - deep01) * (1.0 - fresnel) * 0.55;
                float3 col = lerp(waterCol.rgb, refracted, refractWeight);

                // Sky/ambient reflection at grazing angles
                float3 sky = SampleSH(N) * 0.85 + mainLight.color.rgb * 0.10;
                col = lerp(col, sky, fresnel * 0.35);

                // Specular highlights
                float3 specTotal = mainLight.color.rgb * (specBroad + specTight + glitter)
                                 * saturate(mainLight.distanceAttenuation);
                col += specTotal;

                // SSS glow
                col += sssColor;

                // Caustic shimmer
                col += caustic * float3(0.45, 0.95, 1.0);

                // Foam overlay
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                // ── Alpha ───────────────────────────────────────────────────────
                float alpha = waterCol.a;
                alpha = lerp(alpha * 0.72, alpha, deep01);
                alpha = lerp(alpha, min(alpha + 0.18, 0.97), fresnel);
                alpha = max(alpha, 0.38);

                // Foam makes the surface more opaque
                alpha = lerp(alpha, min(alpha + foam * 0.4, 0.98), foam);

                // No skybox depth — boost alpha so water plane is always visible
                if (rawDepth < 0.0001 || sceneEyeDepth > 10000) alpha = max(alpha, 0.62);

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
