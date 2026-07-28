// Assets/Scripts/VoxelEngine/Rendering/QuasarJet.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    QUASAR RELATIVISTIC JET                           ║
// ║                                                                       ║
// ║  Volumetric-looking polar jet shader. Features:                       ║
// ║                                                                       ║
// ║  • Flowing turbulence that streams AWAY from the core                 ║
// ║  • Bright central spine fading to translucent edges                   ║
// ║  • Knotted structure (density variations along the jet axis)          ║
// ║  • Blue-white relativistic glow                                       ║
// ║  • Length-based fade (brightest near the core, fading to nothing)     ║
// ║  • Additive blending so the jet GLOWS                                 ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/QuasarJet"
{
    Properties
    {
        _TimeScale   ("Flow Speed",       Range(0, 3))   = 0.5
        _CoreColor   ("Jet Core Color",   Color) = (0.5, 0.7, 1.0, 1)
        _EdgeColor   ("Jet Edge Color",   Color) = (0.2, 0.35, 0.7, 1)
        _Brightness  ("Brightness",       Range(0, 5))   = 1.8
        _SpineWidth  ("Core Spine Width", Range(0.01, 0.5)) = 0.12
        _NoiseScale  ("Turbulence Scale", Range(1, 30))  = 12.0
        _KnotIntensity ("Knot Brightness", Range(0, 2))  = 0.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
        Blend One One          // additive
        ZWrite Off
        ZTest LEqual
        Cull Off

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
                float4 _EdgeColor;
                float  _Brightness;
                float  _SpineWidth;
                float  _NoiseScale;
                float  _KnotIntensity;
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

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * vnoise(p);
                    p = p * 2.1 + float2(3.7, 1.2);
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // UV: x = along the jet (0 = core, 1 = tip), y = across (0..1).
                float along = IN.uv.x;        // 0 at core, 1 at tip
                float across = IN.uv.y * 2.0 - 1.0;  // -1..1 across the jet width

                // ── Spine: brightest at the centre, fading to edges ──
                float spine = exp(-across * across / (_SpineWidth * 2.0));

                // ── Flowing turbulence: noise that streams along the jet axis ──
                float2 flowUV = float2(
                    along * _NoiseScale - _Time.y * _TimeScale * 3.0,  // flows AWAY from core
                    across * _NoiseScale * 0.5);
                float turbulence = fbm(flowUV);

                // ── Knots: density variations along the jet (brighter blobs) ──
                float knots = sin(along * 18.0 + _Time.y * _TimeScale * 2.0 + turbulence * 6.0);
                knots = smoothstep(0.2, 1.0, knots) * _KnotIntensity;

                // ── Length fade: brightest near the core, fading to nothing ──
                float lengthFade = pow(1.0 - along, 1.5);

                // ── Width expansion: jet gets wider toward the tip ──
                float widthExpansion = lerp(0.3, 1.0, along);
                float widthMask = 1.0 - smoothstep(widthExpansion * 0.8, widthExpansion, abs(across));

                // ── Combine ──
                float intensity = spine * lengthFade * widthMask;
                intensity *= (0.4 + turbulence * 0.8 + knots * 0.5);
                intensity = saturate(intensity);

                // Colour: blue-white core, darker blue edges.
                float3 color = lerp(_CoreColor.rgb, _EdgeColor.rgb, abs(across));

                float3 finalColor = color * intensity * _Brightness;

                return half4(finalColor, max(max(finalColor.r, finalColor.g), finalColor.b));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
