// Assets/Scripts/VoxelEngine/Rendering/VoxelWaterURP.shader
//
// Beautiful water shader:
//   - World-space vertex displacement (no chunk seams)
//   - Scene depth shore fade (smooth alpha transition at coastline)
//   - Foam line from depth difference
//   - Fresnel reflection
//   - Scrolling normal perturbation for shimmer
//   - Gerstner wave vertex animation

Shader "VoxelEngine/VoxelWaterURP"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow", Color) = (0.15, 0.55, 0.78, 0.75)
        _DeepColor    ("Deep",    Color) = (0.03, 0.12, 0.30, 0.90)
        _FoamColor    ("Foam",    Color) = (0.85, 0.92, 0.97, 0.80)

        [Header(Waves)]
        _WaveAmp  ("Wave Amplitude", Range(0, 0.15)) = 0.04
        _WaveFreq ("Wave Frequency", Range(0.3, 4))  = 1.2
        _WaveSpeed("Wave Speed",     Range(0, 2))    = 0.5

        [Header(Surface)]
        _NormalScale  ("Normal Strength", Range(0, 2))   = 0.6
        _NormalSpeed1 ("Normal Scroll 1", Vector)        = (0.08, 0.06, 0, 0)
        _NormalSpeed2 ("Normal Scroll 2", Vector)        = (-0.05, 0.07, 0, 0)
        _Gloss        ("Gloss",           Range(0, 1))   = 0.94
        _FresnelPower ("Fresnel Power",   Range(1, 8))   = 3.0

        [Header(Shore)]
        _DepthFade    ("Shore Fade Dist",  Range(0.1, 8)) = 0.8
        _FoamWidth    ("Foam Line Width",  Range(0.01, 2)) = 0.25
        _FoamIntensity("Foam Intensity",   Range(0, 1))   = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _FoamColor;
                float  _WaveAmp, _WaveFreq, _WaveSpeed;
                float  _NormalScale;
                float4 _NormalSpeed1, _NormalSpeed2;
                float  _Gloss, _FresnelPower;
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
            };

            // ── Gerstner wave (world-space, no chunk dependency) ──

            float3 GerstnerWave(float2 worldXZ, float2 dir, float amp, float freq, float spd, float t)
            {
                float phase = dot(worldXZ, dir) * freq + t * spd;
                float s, c;
                sincos(phase, s, c);
                return float3(dir.x * amp * c, amp * s, dir.y * amp * c);
            }

            // ── Scrolling procedural normal (replaces texture sampling) ──

            float3 ScrollNormal(float2 worldXZ, float t)
            {
                // Two opposing scroll directions for interference pattern.
                float2 uv1 = worldXZ * 0.15 + _NormalSpeed1.xy * t;
                float2 uv2 = worldXZ * 0.22 + _NormalSpeed2.xy * t;

                // Procedural normal via sin/cos (no texture needed).
                float3 n1 = float3(
                    cos(uv1.x * 6.28 + sin(uv1.y * 4.0)) * 0.5,
                    1.0,
                    sin(uv1.y * 6.28 + cos(uv1.x * 3.5)) * 0.5);

                float3 n2 = float3(
                    cos(uv2.x * 5.1 - sin(uv2.y * 3.2)) * 0.5,
                    1.0,
                    sin(uv2.y * 4.8 + cos(uv2.x * 2.7)) * 0.5);

                // Blend the two normals.
                float3 blended = normalize(n1 + n2);
                blended.xz *= _NormalScale;
                return normalize(blended);
            }

            V2F vert(A2V i)
            {
                V2F o = (V2F)0;
                float3 posOS = i.posOS.xyz;

                // Get world position for wave calculation (absolute, chunk-independent).
                float3 worldPos = TransformObjectToWorld(posOS);

                // Apply Gerstner waves using WORLD XZ — identical across all chunks.
                if (i.normOS.y > 0.5)
                {
                    float t = _Time.y;
                    float3 w1 = GerstnerWave(worldPos.xz, float2(1.0, 0.3), _WaveAmp, _WaveFreq, _WaveSpeed, t);
                    float3 w2 = GerstnerWave(worldPos.xz, float2(-0.5, 0.8), _WaveAmp * 0.6, _WaveFreq * 1.5, _WaveSpeed * 1.3, t);
                    float3 w3 = GerstnerWave(worldPos.xz, float2(0.3, -0.7), _WaveAmp * 0.3, _WaveFreq * 2.2, _WaveSpeed * 0.8, t);
                    posOS.y += w1.y + w2.y + w3.y;
                    posOS.xz += (w1.xz + w2.xz + w3.xz) * 0.15;
                }

                o.posWS = TransformObjectToWorld(posOS);
                o.posCS = TransformWorldToHClip(o.posWS);
                o.normWS = TransformObjectToWorldNormal(i.normOS);
                o.fog = ComputeFogFactor(o.posCS.z);
                o.scrPos = ComputeScreenPos(o.posCS);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.posWS);

                // ── Scrolling normal ──
                float3 N = ScrollNormal(i.posWS.xz, _Time.y);
                // Blend with geometry normal.
                N = normalize(float3(N.x, 1.0, N.z));

                // ── Scene depth for shore fade + foam ──
                float2 screenUV = i.scrPos.xy / max(i.scrPos.w, 0.0001);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = i.scrPos.w;
                float depthDiff = max(0, sceneEyeDepth - waterEyeDepth);

                // Shore alpha fade: transparent at shore, opaque in deep water.
                float shoreFade = saturate(depthDiff / _DepthFade);

                // Foam line: bright fringe where water meets terrain.
                float foam = 1.0 - saturate(depthDiff / _FoamWidth);
                foam = foam * foam * _FoamIntensity; // sharpen

                // ── Fresnel ──
                float NdV = saturate(dot(V, N));
                float fresnel = pow(1.0 - NdV, _FresnelPower);

                // ── Color composition ──
                // Depth-based shallow → deep gradient.
                float4 col = lerp(_ShallowColor, _DeepColor, shoreFade);

                // Mix in foam at shore edges.
                col.rgb = lerp(col.rgb, _FoamColor.rgb, foam * _FoamColor.a);

                // Fresnel increases opacity and adds sky reflection.
                col.a = lerp(col.a, min(col.a + 0.20, 0.95), fresnel * 0.5);

                // Shore fade on alpha — gentle transition near beach.
                col.a *= saturate(shoreFade * 1.5 + 0.4);

                // Minimum opacity.
                col.a = max(col.a, 0.45);

                // If depth texture is unavailable (rawDepth near 0), use safe fallback.
                if (rawDepth < 0.0001 || sceneEyeDepth > 10000)
                    col.a = max(col.a, 0.60);

                // ── Specular ──
                Light mainLight = GetMainLight();
                float3 H = normalize(V + mainLight.direction);
                float spec = pow(saturate(dot(N, H)), _Gloss * 300.0);
                float sparkle = pow(saturate(dot(N, H)), 1200.0) * 1.5; // sun glints
                col.rgb += mainLight.color.rgb * (spec * 0.25 + sparkle * 0.10);

                // ── Ambient + sky reflection ──
                float3 ambient = SampleSH(N);
                col.rgb += ambient * col.rgb * 0.06;
                // Fresnel sky reflection.
                col.rgb = lerp(col.rgb, ambient * 0.8 + mainLight.color.rgb * 0.1, fresnel * 0.15);

                // ── Fog ──
                col.rgb = MixFog(col.rgb, i.fog);

                return col;
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
