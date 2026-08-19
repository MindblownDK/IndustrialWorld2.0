// Assets/Scripts/VoxelEngine/Rendering/VoxelGrass.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    VOXEL GRASS — wind-animated blades                ║
// ║                                                                       ║
// ║  A GPU-instanced grass shader with proper wind animation. Each blade  ║
// ║  sways based on the global wind direction + a per-blade phase, giving ║
// ║  a natural flowing meadow effect.                                      ║
// ║                                                                       ║
// ║  • Vertex displacement: blades bend in the wind direction             ║
// ║  • Height-based colour gradient: darker root → brighter tip           ║
// ║  • Wind gusts: large-scale turbulence overlaid on base wind           ║
// ║  • GPU instancing compatible (one draw call for thousands of blades)  ║
// ║  • No textures — everything procedural                                ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/VoxelGrass"
{
    Properties
    {
        _BaseColor   ("Base Color (root)", Color) = (0.22, 0.40, 0.12, 1)
        _TipColor    ("Tip Color",         Color) = (0.45, 0.65, 0.22, 1)
        _WindStrength ("Wind Strength",    Range(0, 1))   = 0.4
        _WindSpeed   ("Wind Speed",        Range(0, 5))   = 1.5
        _WindDir     ("Wind Direction",    Vector) = (1, 0, 0.3, 0)
        _GustScale   ("Gust Scale",        Range(0.5, 10)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Off  // double-sided — grass blades are thin

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float  _WindStrength;
                float  _WindSpeed;
                float4 _WindDir;
                float  _GustScale;
            CBUFFER_END

            float4 _VoxelTerrainBodyCenter;
            float _VoxelTerrainIsPlanet;

            float3 GrassUp(float3 worldPos)
            {
                float3 radial = worldPos - _VoxelTerrainBodyCenter.xyz;
                float lenSq = dot(radial, radial);
                radial = lenSq > 0.0001 ? radial * rsqrt(lenSq) : float3(0, 1, 0);
                return normalize(lerp(float3(0, 1, 0), radial, saturate(_VoxelTerrainIsPlanet)));
            }

            // Unity's built-in time
            // We use _Time.y for animation.

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : NORMAL;
                float  heightFactor : TEXCOORD0;  // 0 at root, 1 at tip
                float  fogCoord   : TEXCOORD1;
                float3 color      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Simple hash for per-instance phase variation.
            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash(float3(i, 0));
                float b = hash(float3(i + float2(1, 0), 0));
                float c = hash(float3(i + float2(0, 1), 0));
                float d = hash(float3(i + float2(1, 1), 0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // The blade mesh is a quad with Y = height (0 at root, 1 at tip).
                // UV.y = 0 at root, 1 at tip.
                float heightFactor = IN.uv.y;
                OUT.heightFactor = heightFactor;

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normalOS);

                // ── Wind animation ──
                // Project the world wind onto the local tangent plane. Grass therefore bends
                // along the surface at every latitude instead of always toward global XZ.
                float3 grassUp = GrassUp(worldPos);
                float3 windTangent = _WindDir.xyz - grassUp * dot(_WindDir.xyz, grassUp);
                float windLenSq = dot(windTangent, windTangent);
                windTangent = windLenSq > 0.0001 ? windTangent * rsqrt(windLenSq) : float3(1, 0, 0);

                // Per-blade phase and gust remain body-centred for stable planet wrapping.
                float3 bodyCoord = lerp(worldPos, worldPos - _VoxelTerrainBodyCenter.xyz, saturate(_VoxelTerrainIsPlanet));
                float phase = hash(floor(bodyCoord * 10.0)) * 6.28;
                float2 gustUV = bodyCoord.xz * _GustScale * 0.01 + _Time.y * _WindSpeed * 0.3;
                float gust = vnoise(gustUV) * 2.0 - 1.0;

                float sway = sin(_Time.y * _WindSpeed + phase) * 0.5 + 0.5;
                sway = sway * gust * _WindStrength;
                float bendAmount = heightFactor * heightFactor * sway;
                worldPos += windTangent * bendAmount * 0.8;
                worldPos -= grassUp * bendAmount * 0.3;

                // ── Colour: gradient from root (dark) to tip (bright) ──
                float3 color = lerp(_BaseColor.rgb, _TipColor.rgb, heightFactor);
                // Add wind-driven brightness variation (gusts make grass flash brighter).
                color *= (0.85 + sway * 0.3);
                OUT.color = color;

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.normalWS = worldNormal;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Simple diffuse lighting + ambient.
                float3 normal = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                float NdotL = saturate(dot(normal, lightDir));

                float3 ambient = SampleSH(normal) * 0.6;
                float3 diffuse = mainLight.color * NdotL;

                float3 finalColor = IN.color * (ambient + diffuse);
                finalColor = MixFog(finalColor, IN.fogCoord);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
