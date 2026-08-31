// Planetary cloud shell — the clouds are a real spherical layer WRAPPED AROUND the
// body, not a deck that follows the camera. One shell per atmospheric planet/moon,
// parented to the body, so:
//   • from the surface you are INSIDE the shell → an overcast ceiling that curves to
//     the horizon with no rim, no edge and no stretched curtains,
//   • from orbit you are OUTSIDE it → cloud bands wrapped over the planet,
//   • flying up does NOT drag the clouds with you.
//
// Density comes from a tileable 3D noise volume sampled in BODY-LOCAL space, so there
// are no poles, no seams and no UV pinch anywhere on the sphere. A short view-ray
// march through the shell thickness gives real parallax/puffiness from both sides, a
// large-scale "weather cell" band decides where the sky is clear, overcast or a black
// storm, and the density gradient is lit by the URP main light for dark rain-bellies
// and bright sunlit crowns.
Shader "VoxelEngine/WeatherCloudsURP"
{
    Properties
    {
        _NoiseTex ("Cloud Volume (A mass, R detail)", 3D) = "white" {}
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _BellyColor ("Cloud Underside", Color) = (0.32, 0.34, 0.40, 1)
        _CrownColor ("Cloud Lit Crown", Color) = (1.00, 0.99, 0.97, 1)
        _HorizonColor ("Horizon Haze", Color) = (0.55, 0.58, 0.64, 1)
        _Coverage ("Coverage", Range(0, 1)) = 0.5
        _Storm ("Storm Intensity", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Flash ("Lightning Flash", Range(0, 4)) = 0
        _NoiseScale ("Mass Scale", Float) = 24
        _DetailScale ("Detail Scale", Float) = 5.7
        _CellScale ("Weather Cell Scale", Float) = 2.3
        _CellVariation ("Weather Cell Variation", Range(0, 1)) = 0.55
        _Offset ("Mass Drift", Vector) = (0, 0, 0, 0)
        _DetailOffset ("Detail Drift", Vector) = (0, 0, 0, 0)
        _CellOffset ("Cell Drift", Vector) = (0, 0, 0, 0)
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.6)) = 0.20
        _Relief ("Relief Strength", Range(0, 60)) = 22
        _ShellThickness ("Shell Thickness / Radius", Range(0.0005, 0.2)) = 0.02
        _BodyCenter ("Body Centre (world)", Vector) = (0, 0, 0, 0)
        _ShellRadius ("Shell Radius (world)", Float) = 1000
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "PlanetCloudShell"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float4 _BellyColor;
                float4 _CrownColor;
                float4 _HorizonColor;
                float4 _Offset;
                float4 _DetailOffset;
                float4 _CellOffset;
                float4 _BodyCenter;
                float _Coverage;
                float _Storm;
                float _Opacity;
                float _Flash;
                float _NoiseScale;
                float _DetailScale;
                float _CellScale;
                float _CellVariation;
                float _EdgeSoftness;
                float _Relief;
                float _ShellThickness;
                float _ShellRadius;
            CBUFFER_END

            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.dirOS = normalize(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half SampleMass(float3 dir)
            {
                return SAMPLE_TEXTURE3D(_NoiseTex, sampler_NoiseTex,
                                        dir * _NoiseScale + _Offset.xyz).a;
            }

            half SampleDetail(float3 dir)
            {
                return SAMPLE_TEXTURE3D(_NoiseTex, sampler_NoiseTex,
                                        dir * (_NoiseScale * _DetailScale) + _DetailOffset.xyz).r;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dirOS = normalize(input.dirOS);
                float3 rayWS = input.positionWS - _WorldSpaceCameraPos;
                float rayLen = max(length(rayWS), 0.0001);
                float3 viewWS = rayWS / rayLen;                       // camera → fragment
                float3 viewOS = normalize(TransformWorldToObjectDir(viewWS, false));

                // ── Weather cells: WHERE the sky is clear / overcast / stormy ──
                half cell = SAMPLE_TEXTURE3D(_NoiseTex, sampler_NoiseTex,
                                             dirOS * _CellScale + _CellOffset.xyz).a;
                float coverage = saturate(_Coverage + (cell - 0.5) * _CellVariation);
                float thr = lerp(0.88, 0.04, coverage);

                // ── Short march through the shell for parallax / puffiness ──
                float stepOS = _ShellThickness * 0.34;
                half mass = 0;
                half w = 0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float k = (float)i;
                    half weight = 1.0 - k * 0.18;
                    mass += SampleMass(dirOS + viewOS * (stepOS * k)) * weight;
                    w += weight;
                }
                mass /= max(w, 0.0001);

                half detail = SampleDetail(dirOS);
                half density = saturate(mass - detail * 0.28 * (1.0 - mass * 0.4));

                // Grazing sight lines cross more cloud (true on a sphere, both sides).
                float3 nSphere = normalize(TransformObjectToWorldNormal(dirOS));
                float facing = saturate(abs(dot(nSphere, viewWS)));
                float depthBoost = lerp(1.9, 1.0, saturate(facing * 1.6));

                // Opacity (not density) is boosted: a grazing line of sight through the same
                // cloud is more opaque, but it must never invent cloud where the sky is clear.
                half alpha = saturate(smoothstep(thr, thr + _EdgeSoftness, density) * depthBoost);
                if (alpha <= 0.002) return half4(0, 0, 0, 0);

                // ── Relief normal from the density gradient ──
                float3 t1 = normalize(cross(dirOS, abs(dirOS.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 t2 = cross(dirOS, t1);
                float eps = 0.6 / max(_NoiseScale, 1.0);
                half dX = SampleMass(dirOS + t1 * eps) - mass;
                half dY = SampleMass(dirOS + t2 * eps) - mass;
                float3 nOS = normalize(dirOS - (t1 * dX + t2 * dY) * _Relief * 0.05);
                float3 nWS = normalize(TransformObjectToWorldNormal(nOS));

                float3 sunDir = normalize(_MainLightPosition.xyz);
                half lit = saturate(dot(nWS, sunDir) * 0.55 + 0.45);

                // Storm cells go heavy and near-black underneath; calm cells stay bright.
                half stormHere = saturate(_Storm * saturate((cell - 0.35) * 2.2));
                half3 belly = lerp(_BellyColor.rgb, _BellyColor.rgb * 0.42, stormHere);

                // Are we under the deck (inside the shell) or above it?
                float camHeight = length(_WorldSpaceCameraPos - _BodyCenter.xyz);
                float inside = saturate((_ShellRadius - camHeight) * 0.02);

                // Thin borders scatter light forward — ragged glowing edges.
                half thin = 1.0 - saturate((density - thr) / max(_EdgeSoftness * 2.2, 0.001));

                half3 col = lerp(belly, _CrownColor.rgb, lit * lit);
                // Seen from below, the underside dominates; from above, the lit crown does.
                col = lerp(col, lerp(belly, _CrownColor.rgb, lit * 0.35), inside * 0.75);
                col = lerp(col, _CrownColor.rgb * 1.10, thin * 0.40 * (1.0 - inside * 0.5));
                col *= _TintColor.rgb;
                col += _Flash * half3(0.80, 0.85, 1.00);

                // ── Ground view: hide everything below the eye plane and melt into haze ──
                float3 camUp = normalize(_WorldSpaceCameraPos - _BodyCenter.xyz);
                float upDot = dot(camUp, viewWS);
                float horizon = smoothstep(-0.035, 0.06, upDot);
                alpha = lerp(alpha, alpha * horizon, inside);
                col = lerp(col, _HorizonColor.rgb, inside * (1.0 - smoothstep(0.0, 0.28, upDot)) * 0.85);

                // Never let the shell pop when the camera sits right on it.
                float nearFade = saturate(rayLen / max(_ShellRadius * 0.02, 1.0));

                return half4(col, alpha * _Opacity * nearFade);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
