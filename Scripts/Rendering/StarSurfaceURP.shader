// Assets/Scripts/VoxelEngine/Rendering/StarSurfaceURP.shader
//
// PROCEDURAL STAR SURFACE (9.3.0) — the sun is a real body, not a glow ball.
//
// Animated multi-octave plasma turbulence (granulation cells), slow-drifting
// dark starspots, limb darkening toward the edge and a hot fresnel rim make the
// star read as a burning surface from orbit AND from a distance. Fully
// procedural — no textures, radius-agnostic, tinted by the body's authored
// glow colour.
Shader "VoxelEngine/StarSurfaceURP"
{
    Properties
    {
        _StarColor     ("Star Color",      Color) = (1.0, 0.72, 0.35, 1)
        _HotColor      ("Hot Cell Color",  Color) = (1.0, 0.97, 0.82, 1)
        _SpotColor     ("Spot Color",      Color) = (0.55, 0.18, 0.05, 1)
        _Emission      ("Emission Boost",  Range(0.2, 8)) = 2.6
        _Turbulence    ("Turbulence Scale",Range(0.5, 12)) = 4.0
        _FlowSpeed     ("Flow Speed",      Range(0, 2)) = 0.35
        _SpotStrength  ("Spot Strength",   Range(0, 1)) = 0.55
        _LimbDarkening ("Limb Darkening",  Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "StarForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _StarColor;
                float4 _HotColor;
                float4 _SpotColor;
                float  _Emission;
                float  _Turbulence;
                float  _FlowSpeed;
                float  _SpotStrength;
                float  _LimbDarkening;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS      : TEXCOORD0;   // object-space unit direction (stable on the sphere)
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
            };

            // ── Compact 3D value-noise fbm (cheap, fully procedural) ──
            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    v += Noise3(p) * a;
                    p = p * 2.07 + 19.19;
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vp.positionCS;
                OUT.dirOS = normalize(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(vp.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dirOS);
                float t = _Time.y * _FlowSpeed;

                // Domain-warped plasma turbulence: convecting granulation cells.
                float3 p = dir * _Turbulence;
                float3 warp = float3(
                    Fbm(p + float3(0.0, t * 0.7, 0.0)),
                    Fbm(p + float3(5.2, t * 0.6, 1.3)),
                    Fbm(p + float3(9.7, 2.8, t * 0.8)));
                float plasma = Fbm(p * 2.0 + warp * 1.6 + t * 0.15);
                float cells  = Fbm(p * 5.0 - warp * 0.8 - t * 0.22);

                // Dark starspots: slow, large, sparse.
                float spots = Fbm(dir * 1.6 + t * 0.03);
                float spotMask = smoothstep(0.62, 0.75, spots) * _SpotStrength;

                // Compose surface colour: base plasma → hot cell cores → dark spots.
                float heat = saturate(plasma * 0.65 + cells * 0.55);
                float3 col = lerp(_StarColor.rgb * 0.55, _StarColor.rgb, heat);
                col = lerp(col, _HotColor.rgb, smoothstep(0.62, 0.95, heat));
                col = lerp(col, _SpotColor.rgb, spotMask);

                // Limb darkening + hot fresnel rim (thin bright edge just inside the limb).
                float ndv = saturate(dot(normalize(IN.normalWS), normalize(IN.viewDirWS)));
                float limb = lerp(1.0, pow(ndv, 0.65), _LimbDarkening);
                float rim = pow(1.0 - ndv, 3.0) * 0.9;
                col = col * limb + _HotColor.rgb * rim;

                return half4(col * _Emission, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
