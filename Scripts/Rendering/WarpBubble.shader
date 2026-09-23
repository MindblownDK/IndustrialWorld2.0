// Assets/Scripts/VoxelEngine/Rendering/WarpBubble.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    WARP BUBBLE (jump drive)                           ║
// ║                                                                       ║
// ║  The energy envelope that swallows the ship when the jump drive      ║
// ║  fires: a fresnel rim shell with scrolling latitude bands and a      ║
// ║  slow power pulse, driven by WarpFx (intensity swells through the    ║
// ║  pre-phase, holds through transit, pops on arrival).                 ║
// ║                                                                       ║
// ║  • Fresnel rim — the shell reads from any angle                      ║
// ║  • Scrolling bands — energy visibly circulating pole to pole         ║
// ║  • Additive blending so it glows against deep space                  ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/WarpBubble"
{
    Properties
    {
        _RimColor  ("Rim Color",     Color) = (0.45, 0.85, 1.0, 1)
        _CoreColor ("Core Tint",     Color) = (0.12, 0.40, 0.75, 1)
        _Intensity ("Intensity",     Range(0, 4)) = 1.0
        _PulseSpeed("Pulse Speed",   Range(0, 4)) = 0.9
        _BandCount ("Latitude Bands",Range(0, 12)) = 6
        _BandSpeed ("Band Scroll",   Range(-2, 2)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        // Far shell only: the near wall would smear over the hull (the ship must
        // stay crisp inside the bubble). The far wall still reads around the
        // silhouette — and from the cockpit, all around the view.
        Cull Front

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float4 _CoreColor;
                float  _Intensity;
                float  _PulseSpeed;
                float  _BandCount;
                float  _BandSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float3 posOS      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.viewDirWS = GetCameraPositionWS() - wp;
                OUT.posOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);
                float fres = pow(1.0 - abs(dot(n, v)), 2.0);

                // Latitude bands scrolling pole to pole — circulating energy.
                float band = 0.5 + 0.5 * sin(IN.posOS.y * _BandCount * 6.28318 + _Time.y * _BandSpeed * 6.28318);
                band = smoothstep(0.55, 1.0, band) * 0.6;

                // Slow power pulse.
                float pulse = 1.0 + 0.25 * sin(_Time.y * _PulseSpeed * 6.28318);

                float glow = (fres * 1.2 + band * (0.25 + fres) + 0.03) * pulse * _Intensity;
                float3 color = lerp(_CoreColor.rgb, _RimColor.rgb, saturate(fres + band * 0.5));

                return half4(color * glow, glow);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
