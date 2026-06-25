// Assets/Scripts/VoxelEngine/Rendering/QuasarAccretionDisc.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    QUASAR ACCRETION DISC                              ║
// ║                                                                       ║
// ║  A fully procedural accretion disc shader for the most striking       ║
// ║  deep-space backdrop possible. Features:                              ║
// ║                                                                       ║
// ║  • Swirling FBM noise that rotates + shears over time                 ║
// ║  • Temperature gradient: white-hot inner → orange → deep red outer    ║
// ║  • Relativistic DOPPLER BEAMING (one side dramatically brighter)      ║
// ║  • Photon ring (Einstein ring) — bright thin halo at the ISCO         ║
// ║  • Black hole shadow (pure darkness at the centre)                    ║
// ║  • Turbulent spiral arms that evolve organically                      ║
// ║  • Additive blending so it GLOWS against the skybox                   ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/QuasarAccretionDisc"
{
    Properties
    {
        _TimeScale   ("Animation Speed",  Range(0, 5))   = 0.3
        _DiscTilt    ("Disc Tilt (0=face-on, 1=edge-on)", Range(0, 1)) = 0.35
        _CoreColor   ("Inner Core Color",  Color) = (1.0, 0.95, 0.8, 1)
        _MidColor    ("Mid Disc Color",    Color) = (1.0, 0.55, 0.15, 1)
        _OuterColor  ("Outer Disc Color",  Color) = (0.7, 0.12, 0.05, 1)
        _DopplerStrength ("Doppler Beaming", Range(0, 3)) = 1.8
        _Brightness  ("Overall Brightness", Range(0, 5)) = 1.5
        _PhotonRingWidth ("Photon Ring Width", Range(0.001, 0.05)) = 0.012
        _NoiseScale  ("Noise Detail",      Range(1, 20))  = 8.0
        _SpiralTight ("Spiral Tightness",  Range(0, 10))  = 3.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
        Blend One One          // additive — GLOWS
        ZWrite Off
        ZTest LEqual
        Cull Off               // double-sided so tilt works from any angle

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _TimeScale;
                float  _DiscTilt;
                float4 _CoreColor;
                float4 _MidColor;
                float4 _OuterColor;
                float  _DopplerStrength;
                float  _Brightness;
                float  _PhotonRingWidth;
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

                // Centre the UV at (0,0) and convert to polar coordinates.
                float2 centered = IN.uv * 2.0 - 1.0;          // -1..1

                // Apply disc tilt (squash vertically so the disc looks angled).
                centered.y /= max(0.05, 1.0 - _DiscTilt * 0.75);

                float radius = length(centered);
                float angle  = atan2(centered.y, centered.x);  // -PI..PI

                // ── Black hole shadow: pure darkness inside the event horizon ──
                float eventHorizon = 0.18;
                if (radius < eventHorizon)
                    return half4(0, 0, 0, 0);

                // ── Photon ring (Einstein ring) — bright thin halo at the ISCO ──
                float photonRingRadius = eventHorizon + 0.015;
                float photonRing = exp(-pow((radius - photonRingRadius) / _PhotonRingWidth, 2.0));
                photonRing = saturate(photonRing * 2.5);

                // ── Spiral swirl: rotate the noise sampling by angle + radius ──
                // This creates the characteristic spiral arm pattern of accretion discs.
                float swirlAngle = angle + radius * _SpiralTight + _Time.y * _TimeScale;
                float2 swirlUV = float2(cos(swirlAngle), sin(swirlAngle)) * radius * _NoiseScale;

                // Layered FBM for turbulent detail. Two layers at different scales for richness.
                float n1 = fbm(swirlUV + _Time.y * _TimeScale * 0.5);
                float n2 = fbm(swirlUV * 2.5 - float2(5.0, 3.0) + _Time.y * _TimeScale * 0.7);
                float turbulence = n1 * 0.65 + n2 * 0.35;

                // ── Disc falloff: bright near the centre, fading outward ──
                float discFalloff = smoothstep(1.0, eventHorizon, radius);  // 1 near center, 0 at edge
                discFalloff = pow(discFalloff, 1.8);

                // Combine turbulence with falloff for the disc intensity.
                float discIntensity = discFalloff * (0.3 + turbulence * 0.9);

                // ── Temperature gradient: inner = white-hot, outer = deep red ──
                float tempT = smoothstep(eventHorizon, 1.0, radius);  // 0 inner, 1 outer
                float3 discColor = lerp(_CoreColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.5, tempT));
                discColor = lerp(discColor, _OuterColor.rgb, smoothstep(0.4, 1.0, tempT));

                // ── Relativistic DOPPLER BEAMING ──
                // The side of the disc rotating TOWARD the viewer is dramatically brighter
                // (blueshifted + relativistic beaming). The receding side is dimmer (redshifted).
                // Use the horizontal component of the angle to determine approaching vs receding.
                float dopplerAngle = cos(angle);  // 1 = approaching (right), -1 = receding (left)
                float doppler = 1.0 + dopplerAngle * _DopplerStrength * 0.5;
                doppler = max(0.15, doppler);     // never fully dark
                discIntensity *= doppler;

                // Blueshift the approaching side slightly, redshift the receding side.
                float3 blueshift = float3(0.08, 0.05, 0.0);   // toward blue
                float3 redshift  = float3(0.05, -0.02, -0.05); // toward red
                discColor += lerp(redshift, blueshift, dopplerAngle * 0.5 + 0.5) * _DopplerStrength * 0.15;

                // ── Combine everything ──
                float3 finalColor = discColor * discIntensity * _Brightness;
                finalColor += _CoreColor.rgb * photonRing * _Brightness * 2.0;  // photon ring glow

                // Outer edge fade (soft disc boundary).
                float edgeFade = smoothstep(1.0, 0.85, radius);
                finalColor *= edgeFade;

                return half4(finalColor, max(max(finalColor.r, finalColor.g), finalColor.b));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
