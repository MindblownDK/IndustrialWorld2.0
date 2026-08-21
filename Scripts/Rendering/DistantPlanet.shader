// Assets/Scripts/VoxelEngine/Rendering/DistantPlanet.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║            DISTANT PLANET DISC — long-range body rendering           ║
// ║                                                                      ║
// ║  Renders a planet/moon the way it looks through a telescope: a       ║
// ║  sun-lit disc with a soft day/night TERMINATOR, a dark starlit       ║
// ║  night side, and a thin ATMOSPHERE rim glowing around the            ║
// ║  silhouette. Used by DistantBodyBeacons while a body is too far      ║
// ║  for its real LOD to carry the view.                                 ║
// ║                                                                      ║
// ║  URP notes: mirrors the proven QuasarGlow/QuasarJet template —       ║
// ║  NO HDR blend modes, NO LOD line, _WorldSpaceCameraPos.              ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/DistantPlanet"
{
    Properties
    {
        _BaseColor    ("Body Colour",           Color) = (0.62, 0.68, 0.76, 1)
        _SunDir       ("Sun Direction",         Vector) = (1, 0, 0, 0)
        _Terminator   ("Terminator Softness",   Range(0.05, 1)) = 0.24
        _NightColor   ("Night Colour",          Color) = (0.035, 0.045, 0.075, 1)
        _NightBright  ("Night Brightness",      Range(0, 0.5)) = 0.12
        _AtmoColor    ("Atmosphere Rim Colour", Color) = (0.36, 0.55, 0.90, 1)
        _AtmoStrength ("Atmosphere Rim Strength", Range(0, 3)) = 0.85
        _AtmoPower    ("Atmosphere Rim Sharpness", Range(0.5, 8)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SunDir;
                float  _Terminator;
                float4 _NightColor;
                float  _NightBright;
                float4 _AtmoColor;
                float  _AtmoStrength;
                float  _AtmoPower;
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

                float3 n = normalize(IN.normalWS);
                float3 sunDir = normalize(_SunDir.xyz + 1e-4);

                // ── Day / night terminator (soft band across the sun-facing side) ──
                float dayRaw = dot(n, sunDir);
                float day = saturate(dayRaw / max(0.05, _Terminator) * 0.5 + 0.5);
                day = day * day * (3.0 - 2.0 * day);      // smoothstep band
                float dayBright = 0.45 + 0.85 * day;      // limb-darkened day side

                // Sun-side tint (slightly warm) vs night tint (dark starlight).
                float3 col = _BaseColor.rgb * dayBright
                           + _NightColor.rgb * _NightBright;

                // ── Atmosphere rim: fresnel glow around the silhouette ──
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float fresnel = pow(1.0 - saturate(dot(n, viewDir)), _AtmoPower);
                col += _AtmoColor.rgb * (fresnel * _AtmoStrength) * (0.35 + 0.65 * day);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Diffuse"
}
