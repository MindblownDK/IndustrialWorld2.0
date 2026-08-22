// Assets/Scripts/VoxelEngine/Rendering/VoxelGrass.shader
//
// +----------------------------------------------------------------------+
// |                    VOXEL GRASS - wind-animated blades                |
// |                                                                      |
// |  A GPU-instanced grass shader with proper wind animation. Each blade |
// |  sways based on the global wind direction + a per-blade phase,       |
// |  giving a natural flowing meadow effect.                             |
// |                                                                      |
// |  9.18.0 REAL BLADES:                                                 |
// |   - Curved blades: the tapered blade mesh leans forward and the      |
// |     shader adds a height-squared bend along the blade facing.        |
// |   - Rounded shading: normals blend toward the surface up near the    |
// |     tip and flip toward the viewer in the fragment - blades read as  |
// |     solid curved stalks, not flat cards.                             |
// |   - Root ambient occlusion: darker roots ground each blade.          |
// |   - Per-blade hue variation from stable body-local hash.             |
// |   - Real main-light SHADOWS (grass in building shadow goes dark).    |
// |   - Edge fade: blades shrink into the ground at the field radius     |
// |     so the grass ring edge never pops.                               |
// |   - Vertex displacement: blades bend in the wind direction           |
// |   - Height-based colour gradient: darker root -> brighter tip        |
// |   - Wind gusts: large-scale turbulence overlaid on base wind         |
// |   - GPU instancing compatible (batched draw calls)                   |
// |   - No textures - everything procedural                             |
// +----------------------------------------------------------------------+
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
        _BladeLean   ("Blade Lean",        Range(0, 1))   = 0.35
        _FadeRange   ("Fade Range (m)",    Float) = 70
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Off  // double-sided - grass blades are thin

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float  _WindStrength;
                float  _WindSpeed;
                float4 _WindDir;
                float  _GustScale;
                float  _BladeLean;
                float  _FadeRange;
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : NORMAL;
                float  heightFactor : TEXCOORD1;  // 0 at root, 1 at tip
                float  fogCoord   : TEXCOORD2;
                float3 color      : TEXCOORD3;
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

                // The blade mesh is a tapered strip with Y = height (0 root, 1 tip).
                float heightFactor = IN.uv.y;
                OUT.heightFactor = heightFactor;

                float3 rootWS = TransformObjectToWorld(float3(IN.positionOS.x, 0.0, IN.positionOS.z));
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normalOS);

                float3 grassUp = GrassUp(worldPos);

                // -- Blade facing: the blade's local Z in world space (the lean direction).
                float3 facing = TransformObjectToWorld(float3(0, 0, 1)) - TransformObjectToWorld(float3(0, 0, 0));
                float faceLenSq = dot(facing, facing);
                facing = faceLenSq > 0.000001 ? facing * rsqrt(faceLenSq) : grassUp;

                // -- Wind animation --
                // Project the world wind onto the local tangent plane. Grass therefore bends
                // along the surface at every latitude instead of always toward global XZ.
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

                // -- Real blade motion: height-squared wind bend + a constant lean along the
                // facing so blades ARC instead of shearing as flat cards. --
                float bend = heightFactor * heightFactor;
                worldPos += windTangent * bend * sway * 0.8;
                worldPos -= grassUp * bend * sway * 0.3;
                worldPos += facing * (bend * _BladeLean * 0.35);
                worldPos -= grassUp * (bend * _BladeLean * 0.12);

                // -- Edge fade: blades curl back into the ground near the field radius so
                // the grass ring edge never pops as the viewer walks. --
                if (_FadeRange > 1.0)
                {
                    float camDist = distance(_WorldSpaceCameraPos, rootWS);
                    float edgeFade = saturate((_FadeRange - camDist) / 8.0);
                    worldPos = lerp(rootWS, worldPos, edgeFade);
                }

                // -- Rounded blade shading: normals ease toward the surface up along the
                // blade, so tips catch light like curved stalks. --
                OUT.normalWS = normalize(worldNormal + grassUp * (heightFactor * 0.55));

                // -- Colour: root-to-tip gradient, per-blade hue variation, root AO. --
                float3 color = lerp(_BaseColor.rgb, _TipColor.rgb, heightFactor);
                float hue = hash(floor(bodyCoord * 4.0)) - 0.5;          // stable per blade
                color = lerp(color, color * float3(1.14, 1.0, 0.72), hue * 0.5);  // dry-ish / lush variation
                color *= (0.85 + sway * 0.3);                            // gusts brighten
                color *= 0.72 + 0.28 * heightFactor;                     // roots sit in shade
                OUT.color = color;

                OUT.positionWS = worldPos;
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Two-sided blade: flip the normal toward the viewer so both faces shade.
                float3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float3 normal = normalize(IN.normalWS);
                if (dot(normal, viewDir) < 0.0) normal = -normal;

                // Main light WITH real shadow attenuation - grass under buildings and
                // inside tree shade now goes dark with the terrain around it.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float NdotL = saturate(dot(normal, mainLight.direction));

                float3 ambient = SampleSH(normal) * 0.6;
                float3 diffuse = mainLight.color * NdotL * mainLight.shadowAttenuation;

                float3 finalColor = IN.color * (ambient + diffuse);
                finalColor = MixFog(finalColor, IN.fogCoord);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
