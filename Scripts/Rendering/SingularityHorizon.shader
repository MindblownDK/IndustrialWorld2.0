// Assets/Scripts/VoxelEngine/Rendering/SingularityHorizon.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                 EVENT HORIZON — REAL BODY (Phase 5)                  ║
// ║                                                                      ║
// ║  The black sphere at the heart of a singularity: pure void that      ║
// ║  writes depth (the far side of the accretion disc passes behind it)  ║
// ║  and shows only a thin FRESNEL RIM — lensed light wrapping the       ║
// ║  horizon. The rim tint comes from the surrounding disc glow.         ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/SingularityHorizon"
{
    Properties
    {
        _RimColor    ("Rim Colour",       Color) = (1.0, 0.55, 0.25, 1)
        _RimStrength ("Rim Strength",     Range(0, 3)) = 1.1
        _RimPower    ("Rim Sharpness",    Range(0.5, 8)) = 2.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "Horizon"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float  _RimStrength;
                float  _RimPower;
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
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                float fresnel = pow(1.0 - saturate(dot(IN.normalWS, viewDir)), _RimPower);
                float3 rim = _RimColor.rgb * fresnel * _RimStrength;

                // The body of the horizon is pure void; only the lensed rim shows.
                return half4(rim, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
