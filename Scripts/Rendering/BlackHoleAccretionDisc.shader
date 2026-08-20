// Assets/Scripts/VoxelEngine/Rendering/BlackHoleAccretionDisc.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║              BLACK HOLE ACCRETION DISC — REAL BODY (Phase 5)         ║
// ║                                                                      ║
// ║  Built for the REAL flyable singularity bodies (not the backdrop).   ║
// ║  The mesh is an annulus with POLAR UVs:                              ║
// ║     u = radius 0 (inner edge, at the photon ring) → 1 (outer edge)   ║
// ║     v = angle 0 → 1 around the disc                                  ║
// ║                                                                      ║
// ║  • Swirling FBM turbulence that shears + rotates over time           ║
// ║  • Temperature gradient: white-hot inner → orange → deep red outer   ║
// ║  • Relativistic DOPPLER BEAMING (approaching side brighter/bluer)    ║
// ║  • Photon ring (Einstein ring) hugging the inner edge                ║
// ║  • Soft shadow falloff toward the event horizon hole                 ║
// ║  • Additive blending so it GLOWS against deep space                  ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/BlackHoleAccretionDisc"
{
    Properties
    {
        _TimeScale      ("Animation Speed",   Range(0, 5))   = 0.35
        _CoreColor      ("Inner Core Color",  Color) = (1.0, 0.96, 0.86, 1)
        _MidColor       ("Mid Disc Color",    Color) = (1.0, 0.55, 0.16, 1)
        _OuterColor     ("Outer Disc Color",  Color) = (0.62, 0.10, 0.04, 1)
        _DopplerStrength("Doppler Beaming",   Range(0, 3))   = 1.8
        _Brightness     ("Overall Brightness", Range(0, 5))  = 1.6
        _PhotonRingWidth("Photon Ring Width", Range(0.002, 0.12)) = 0.03
        _PhotonRingPower("Photon Ring Brightness", Range(0.5, 4)) = 2.2
        _NoiseScale     ("Noise Detail",      Range(1, 20))   = 8.0
        _SpiralTight    ("Spiral Tightness",  Range(0, 10))   = 3.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
        Blend One One          // additive — GLOWS
        ZWrite Off
        ZTest LEqual
        Cull Off               // double-sided: the far side of the disc must read through the hole

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _TimeScale;
                float4 _CoreColor;
                float4 _MidColor;
                float4 _OuterColor;
                float  _DopplerStrength;
                float  _Brightness;
                float  _PhotonRingWidth;
                float  _PhotonRingPower;
                float  _NoiseScale;
                float  _SpiralTight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // ── Hash-based value noise ──────────────────────────────
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Fractal Brownian Motion — layered noise for organic turbulence.
            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    v += a * vnoise(p);
                    p = p * 2.03 + float2(1.7, 9.2);
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float radius = IN.uv.x;                    // 0 at the photon ring → 1 at the rim
                float angle  = IN.uv.y * 6.2831853 - 3.14159265;  // -PI..PI around the disc

                // ── Spiral swirl: shear the noise field by radius so it forms arms ──
                float swirlAngle = angle + radius * _SpiralTight + _Time.y * _TimeScale;
                float2 swirlUV = float2(cos(swirlAngle), sin(swirlAngle)) * (0.12 + radius) * _NoiseScale;

                float n1 = fbm(swirlUV + _Time.y * _TimeScale * 0.5);
                float n2 = fbm(swirlUV * 2.5 - float2(5.0, 3.0) + _Time.y * _TimeScale * 0.7);
                float turbulence = n1 * 0.65 + n2 * 0.35;

                // ── Radial falloff: bright inside, fading toward the outer rim ──
                float discFalloff = pow(smoothstep(1.0, 0.0, radius), 1.9);
                float discIntensity = discFalloff * (0.30 + turbulence * 0.9);

                // ── Temperature gradient: inner = white-hot, outer = deep red ──
                float tempT = smoothstep(0.0, 1.0, radius);
                float3 discColor = lerp(_CoreColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.5, tempT));
                discColor = lerp(discColor, _OuterColor.rgb, smoothstep(0.4, 1.0, tempT));

                // ── Relativistic DOPPLER BEAMING: approaching side brighter + bluer ──
                float dopplerAngle = cos(angle);
                float doppler = max(0.15, 1.0 + dopplerAngle * _DopplerStrength * 0.5);
                discIntensity *= doppler;
                float3 blueshift = float3(0.08, 0.05, 0.0);
                float3 redshift  = float3(0.05, -0.02, -0.05);
                discColor += lerp(redshift, blueshift, dopplerAngle * 0.5 + 0.5) * _DopplerStrength * 0.15;

                // ── Photon ring: brilliant thin halo hugging the inner edge ──
                float photonRing = exp(-pow(radius / max(0.002, _PhotonRingWidth), 2.0));
                float3 ringColor = _CoreColor.rgb * (photonRing * _PhotonRingPower);

                // ── Event-horizon shadow: soft darkness right at the hole ──
                float holeShadow = smoothstep(0.0, 0.06, radius);
                discIntensity *= holeShadow;

                float3 finalColor = discColor * discIntensity * _Brightness + ringColor * _Brightness;
                finalColor = max(finalColor, 0.0);

                return half4(finalColor, max(max(finalColor.r, finalColor.g), finalColor.b));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
