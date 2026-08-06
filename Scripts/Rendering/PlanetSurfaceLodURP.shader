// Native full-planet LOD shader. Vertex colour is the sampled spherical terrain/ocean map.
Shader "VoxelEngine/PlanetSurfaceLodURP"
{
    Properties
    {
        _Tint ("Global Tint / Fade", Color) = (1,1,1,1)
        _AtmosphereRim ("Atmosphere Rim", Color) = (0.18,0.42,0.78,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.16
        _SurfaceDetailStrength ("Surface Detail Strength", Range(0,0.35)) = 0.12
        _SurfaceDetailScale ("Surface Detail Scale", Range(1,256)) = 96
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PlanetSurfaceLod"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // The icosphere has verified outward winding; back-face culling avoids rendering
            // the far side through the transparent proxy and halves full-screen overdraw.
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _AtmosphereRim;
                float _RimStrength;
                float _SurfaceDetailStrength;
                float _SurfaceDetailScale;
                // Per-material body core (set by each PlanetLodImpostor). Falls back to
                // the global streaming-body center below when unset, so the surface-detail
                // noise is correct for EVERY body in the system, not just the active one.
                float4 _BodyCenter;
            CBUFFER_END

            // Published by SphereWorld. The proxy remains fully body-relative even when
            // the authored planet core is moved to place the player on its surface.
            float4 _VoxelTerrainBodyCenter;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float fogCoord : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normalize(normal.normalWS);
                output.color = input.color;
                output.fogCoord = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            float Hash31(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            float SurfaceDetail(float3 radial)
            {
                float scale = max(1.0, _SurfaceDetailScale);
                float low = Hash31(floor(radial * scale));
                float mid = Hash31(floor(radial * scale * 2.37 + 19.17));
                float fine = Hash31(floor(radial * scale * 5.11 - 7.41));
                return (low * 0.55 + mid * 0.30 + fine * 0.15) * 2.0 - 1.0;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(_WorldSpaceCameraPos - input.positionWS);
                Light sun = GetMainLight();
                float diffuse = saturate(dot(normal, sun.direction)) * 0.72 + 0.28;
                float rim = pow(saturate(1.0 - dot(view, normal)), 3.0) * _RimStrength;
                float3 bodyCenter = length(_BodyCenter.xyz) > 0.0001 ? _BodyCenter.xyz : _VoxelTerrainBodyCenter.xyz;
                float3 radial = normalize(input.positionWS - bodyCenter);
                // Shader-side macro/fine variation preserves a rich continental read from
                // orbit without forcing dense local voxel chunks or a huge proxy mesh.
                float detail = SurfaceDetail(radial) * _SurfaceDetailStrength;
                float3 color = input.color.rgb * _Tint.rgb * diffuse;
                color *= 1.0 + detail;
                color += _AtmosphereRim.rgb * rim;
                color = MixFog(color, input.fogCoord);
                float distToCamera = length(_WorldSpaceCameraPos - input.positionWS);
                // Inset full-planet LOD is occluded by nearby detailed voxel chunks.
                // Clip any LOD triangles within 240m of camera so low-poly LOD never
                // clips through real terrain or cave interiors. Beyond 320m, the entire
                // planet surface remains visible from any distance.
                clip(distToCamera - 240.0);
                float nearFade = smoothstep(240.0, 320.0, distToCamera);
                float alpha = input.color.a * _Tint.a * nearFade;
                if (alpha < 0.01) discard;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
