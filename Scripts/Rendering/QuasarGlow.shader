// Assets/Scripts/VoxelEngine/Rendering/QuasarGlow.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    QUASAR OUTER HALO / GLOW                          ║
// ║                                                                       ║
// ║  A soft radial glow that surrounds the entire quasar structure.      ║
// ║  Creates the "breathing" atmospheric bloom that makes the quasar     ║
// ║  feel immense and alive, even from across the galaxy.                ║
// ║                                                                       ║
// ║  • Smooth radial falloff                                              ║
// ║  • Slow pulsing brightness (the quasar "breathes")                    ║
// ║  • Asymmetric colour (warm core, cool outer halo)                    ║
// ║  • Additive blending for maximum glow impact                         ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/QuasarGlow"
{
    Properties
    {
        _InnerColor  ("Inner Glow Color", Color) = (0.9, 0.8, 0.6, 1)
        _OuterColor  ("Outer Glow Color", Color) = (0.2, 0.35, 0.65, 1)
        _Brightness  ("Brightness",       Range(0, 3)) = 0.8
        _PulseSpeed  ("Pulse Speed",      Range(0, 2)) = 0.15
        _PulseAmount ("Pulse Amount",     Range(0, 1)) = 0.2
        _CoreSize    ("Core Size",        Range(0.05, 0.5)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend One One
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
                float4 _InnerColor;
                float4 _OuterColor;
                float  _Brightness;
                float  _PulseSpeed;
                float  _PulseAmount;
                float  _CoreSize;
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

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float2 centered = IN.uv * 2.0 - 1.0;
                float dist = length(centered);

                // Radial falloff — soft exponential glow.
                float glow = exp(-dist * 3.5);
                glow = saturate(glow);

                // Bright core hotspot.
                float core = exp(-dist * dist / (_CoreSize * _CoreSize));
                core = saturate(core);

                // Slow pulse — the quasar "breathes".
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed * 6.28318) * _PulseAmount;

                // Colour blend: warm inner, cool outer.
                float3 color = lerp(_InnerColor.rgb, _OuterColor.rgb, smoothstep(0.0, 0.6, dist));

                float intensity = (glow * 0.6 + core * 1.4) * pulse * _Brightness;

                return half4(color * intensity, intensity * 0.7);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
