// Volumetric-looking weather cloud deck for the weather system.
//
// The deck is NOT a small disc floating overhead any more: the mesh is a
// horizon-fitted layer (see WeatherClouds.GenerateLayerMesh) whose rim drops
// BELOW the eye line, so the sky reads as a real overcast ceiling that runs all
// the way out to the haze instead of a round patch.
//
// What this pass does per pixel:
//   • samples a tileable billow-noise mass and a second, faster-scrolling detail
//     octave that ERODES it, so the shapes never repeat visibly,
//   • remaps density through a COVERAGE threshold (wisps → solid storm ceiling)
//     instead of just fading the whole layer's opacity (which is what made the
//     deck look like flat translucent paper),
//   • derives a normal from the density gradient and lights it with the URP main
//     light, so the lumps have dark rain-bellies and bright sunlit crowns,
//   • lets thin edges glow (forward scattering) for believable ragged borders,
//   • melts into the fog/haze colour toward the horizon so there is never a
//     visible geometric edge.
//
// Deliberately NOT fogged by the engine: storm fog lives at ground level, the
// deck does its own horizon blend with the colour the weather fog is using.
//
// uv0   = planar (metres × uvPerMetre) cloud coordinates — no polar pinch.
// uv1.x = horizon parameter: 0 at the zenith, 1 at the rim.
Shader "VoxelEngine/WeatherCloudsURP"
{
    Properties
    {
        _MainTex ("Cloud Noise (A mass, R detail, G variance)", 2D) = "white" {}
        _TintColor ("Storm Tint", Color) = (1, 1, 1, 1)
        _BaseColor ("Cloud Underside", Color) = (0.30, 0.32, 0.38, 1)
        _TopColor ("Cloud Lit Crown", Color) = (1.00, 0.99, 0.97, 1)
        _HorizonColor ("Horizon Haze", Color) = (0.55, 0.58, 0.64, 1)
        _Coverage ("Coverage", Range(0, 1)) = 0.5
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Flash ("Lightning Flash", Range(0, 4)) = 0
        _DetailScale ("Detail Tiling", Float) = 4.3
        _DetailOffset ("Detail Scroll", Vector) = (0, 0, 0, 0)
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.6)) = 0.22
        _DensityBoost ("Horizon Density Boost", Range(1, 4)) = 2.1
        _Relief ("Relief Strength", Range(0, 40)) = 14
        _Puff ("Vertical Puff (m)", Float) = 34
        _HorizonBlend ("Horizon Blend Start", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "WeatherClouds"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TintColor;
                float4 _BaseColor;
                float4 _TopColor;
                float4 _HorizonColor;
                float4 _DetailOffset;
                float _Coverage;
                float _Opacity;
                float _Flash;
                float _DetailScale;
                float _EdgeSoftness;
                float _DensityBoost;
                float _Relief;
                float _Puff;
                float _HorizonBlend;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 hz : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uvDetail : TEXCOORD1;
                float hz : TEXCOORD2;
            };

            // Coverage → density threshold. High coverage lets almost everything
            // through (solid storm ceiling), low coverage keeps only the densest
            // cores (a few lazy wisps).
            float CoverageThreshold(float coverage)
            {
                return lerp(0.86, 0.06, saturate(coverage));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float2 uv = TRANSFORM_TEX(input.uv, _MainTex);
                float2 uvD = input.uv * _DetailScale + _DetailOffset.xy;

                // Puff the underside along the layer normal so the ceiling is lumpy
                // rather than a perfectly flat plane. Sampled at a coarse mip: this is
                // silhouette-scale shaping, not detail.
                float mass = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 3).a;
                float3 posOS = input.positionOS.xyz;
                posOS.y += (mass - 0.5) * _Puff * (1.0 - input.hz.x * 0.85);

                output.positionCS = TransformObjectToHClip(posOS);
                output.uv = uv;
                output.uvDetail = uvD;
                output.hz = input.hz.x;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 mass = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half detail = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uvDetail).r;

                // Erode the big masses with the fast detail octave: this is what turns a
                // smooth blob field into torn, believable rain-cloud silhouettes.
                half density = saturate(mass.a - detail * 0.30 * (1.0 - mass.a * 0.4));

                float thr = CoverageThreshold(_Coverage);
                // Thicker apparent deck toward the horizon (longer sight line through it).
                float boost = lerp(1.0, _DensityBoost, input.hz * input.hz);
                half alpha = saturate(smoothstep(thr, thr + _EdgeSoftness, density * boost));
                if (alpha <= 0.001) return half4(0, 0, 0, 0);

                // ── Relief normal from the density gradient (2 extra taps) ──
                float2 texStep = float2(0.0045, 0.0);
                half dX = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texStep.xy).a - mass.a;
                half dY = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texStep.yx).a - mass.a;
                float3 nOS = normalize(float3(-dX * _Relief, 1.0, -dY * _Relief));
                float3 nWS = TransformObjectToWorldNormal(nOS);

                float3 sunDir = normalize(_MainLightPosition.xyz);
                // Wrapped lambert: overcast light is soft, the belly never goes pitch black.
                half lit = saturate(dot(nWS, sunDir) * 0.55 + 0.45);

                // Thin borders scatter light forward — ragged edges glow instead of
                // fading out as grey mush.
                half thin = 1.0 - saturate((density - thr) / max(_EdgeSoftness * 2.2, 0.001));

                half3 col = lerp(_BaseColor.rgb, _TopColor.rgb, lit * lit);
                col = lerp(col, _TopColor.rgb * 1.12, thin * 0.45);
                col *= _TintColor.rgb;
                col += _Flash * half3(0.80, 0.85, 1.00);

                // Melt into the haze so the deck has no visible geometric rim.
                half hazeMix = smoothstep(_HorizonBlend, 1.0, input.hz) * 0.9;
                col = lerp(col, _HorizonColor.rgb, hazeMix);

                return half4(col, alpha * _Opacity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
