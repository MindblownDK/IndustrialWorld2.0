// Assets/Scripts/VoxelEngine/Rendering/SingularityHorizon.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                 EVENT HORIZON — REAL BODY (Phase 5)                  ║
// ║                                                                      ║
// ║  The black sphere at the heart of a singularity: pure void that      ║
// ║  writes depth (the far side of the accretion disc passes behind it)  ║
// ║  and shows GRAVITATIONAL LENSING — a bent-light photon ring that     ║
// ║  hugs the silhouette, strongest where the disc plane crosses the     ║
// ║  horizon (light from the far side of the disc bent around the hole,  ║
// ║  exactly like the real deal). Plus a faint fresnel rim wrapping the  ║
// ║  sphere.                                                             ║
// ║                                                                      ║
// ║  URP notes: mirrors the proven QuasarGlow/QuasarJet template —       ║
// ║  NO HDR blend modes and NO LOD line (the magenta-shader combo on     ║
// ║  this project's URP version).                                        ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/SingularityHorizon"
{
    Properties
    {
        _RimColor     ("Rim Colour",      Color) = (1.0, 0.55, 0.25, 1)
        _RimStrength  ("Rim Strength",    Range(0, 3)) = 0.35
        _RimPower     ("Rim Sharpness",   Range(0.5, 8)) = 2.2
        _LensColor    ("Lensed Ring Colour", Color) = (1.0, 0.95, 0.85, 1)
        _LensStrength ("Lensing Strength", Range(0, 6)) = 1.8
        _LensWidth    ("Lensing Band Width", Range(0.02, 0.6)) = 0.12
        _DiscAxisOS   ("Disc Axis (object space)", Vector) = (0, 1, 0, 0)
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
                float4 _LensColor;
                float  _LensStrength;
                float  _LensWidth;
                float4 _DiscAxisOS;
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
                float3 positionOS : TEXCOORD2;
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
                // The mesh is a unit sphere centred at its pivot — position IS the
                // object-space direction (the horizon GameObject is never rotated).
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // _WorldSpaceCameraPos is the project-standard camera global (the
                // field-tested terrain shader uses the same one).
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float fresnel = pow(1.0 - saturate(dot(IN.normalWS, viewDir)), _RimPower);

                // ── Gravitational lensing: light bent around the hole ──
                // The lensed ring is the silhouette rim weighted by how close the
                // fragment lies to the ACCRETION DISC PLANE: the far side of the disc,
                // bent over and under the horizon, reads as a bright ring hugging the
                // hole's equator — the classic "black spot with light bending around".
                float3 dirOS = normalize(IN.positionOS);
                float3 axisOS = normalize(_DiscAxisOS.xyz + 1e-4);
                float plane = dot(dirOS, axisOS);                 // -1..1, 0 = disc plane
                float lens = exp(-(plane * plane) / max(1e-4, _LensWidth * _LensWidth));

                float3 col = _RimColor.rgb * fresnel * _RimStrength
                           + _LensColor.rgb * fresnel * _LensStrength * lens;

                // The body of the horizon is pure void; only the lensed light shows.
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Diffuse"
}
