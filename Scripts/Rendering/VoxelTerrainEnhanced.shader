// Assets/Scripts/VoxelEngine/Rendering/VoxelTerrainEnhanced.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║              ENHANCED VOXEL TERRAIN SHADER                            ║
// ║                                                                       ║
// ║  Visual polish pass: makes voxel terrain look REAL instead of flat    ║
// ║  solid color. Features:                                               ║
// ║                                                                       ║
// ║  • Procedural micro-detail noise (no textures needed — all generated) ║
// ║  • Slope-aware shading: flat = brighter, steep = darker               ║
// ║  • Procedural vertex AO baked into color for depth                    ║
// ║  • Distance fog blend for atmospheric depth                           ║
// ║  • Subtle specular variation (wet rocks vs dry dirt)                  ║
// ║  • Full PBR lighting + shadows                                         ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/VoxelTerrainEnhanced"
{
    Properties
    {
        _BaseColor   ("Base Color",       Color) = (1,1,1,1)
        _DetailScale ("Detail Scale",     Range(0.5, 20)) = 4.0
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.35
        _SlopeDarken ("Slope Darkening",  Range(0, 1)) = 0.35
        _NoiseFreq   ("Noise Frequency",  Range(1, 30))  = 8.0
        _Smoothness  ("Smoothness",       Range(0, 1))   = 0.15
        _Metallic    ("Metallic",         Range(0, 1))   = 0.0
        _SpecularVar ("Specular Variation", Range(0, 1)) = 0.3
        // Single-surface handshake (9.3.0): 1 on the GPU LOD-skin material clone.
        _BubbleCutout  ("Bubble Cutout",   Float) = 0
        _LodRadialBias ("LOD Radial Bias", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            // Spherical surface-net quads can cross Cartesian chunk faces with opposite
            // winding. Two-sided terrain prevents far-side/radial seam holes; normals are
            // explicitly oriented away from the body core in SurfaceNetsJob.
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _DetailScale;
                float  _DetailStrength;
                float  _SlopeDarken;
                float  _NoiseFreq;
                float  _Smoothness;
                float  _Metallic;
                float  _SpecularVar;
                float  _BubbleCutout;
                float  _LodRadialBias;
            CBUFFER_END

            // Published by SphereWorld. Body-local coordinates keep terrain detail,
            // slope shading, and material variation wrapped around offset planets.
            float4 _VoxelTerrainBodyCenter;
            float _VoxelTerrainIsPlanet;

            // Single-surface handshake — see VoxelTerrainURP for details.
            float4 _VoxelBubbleCenterWS;
            float  _VoxelBubbleCutoutRadius;

            void ApplyBubbleCutout(float3 positionWS)
            {
                if (_BubbleCutout > 0.5)
                {
                    float3 d = positionWS - _VoxelBubbleCenterWS.xyz;
                    clip(dot(d, d) - _VoxelBubbleCutoutRadius * _VoxelBubbleCutoutRadius);
                }
            }

            float3 TerrainCoordinate(float3 worldPos)
            {
                return lerp(worldPos, worldPos - _VoxelTerrainBodyCenter.xyz, saturate(_VoxelTerrainIsPlanet));
            }

            float3 TerrainUp(float3 worldPos)
            {
                float3 radial = worldPos - _VoxelTerrainBodyCenter.xyz;
                float lenSq = dot(radial, radial);
                radial = lenSq > 0.0001 ? radial * rsqrt(lenSq) : float3(0, 1, 0);
                return normalize(lerp(float3(0, 1, 0), radial, saturate(_VoxelTerrainIsPlanet)));
            }

            // ── Procedural noise (hash-based value noise + FBM) ──
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(hash31(i + float3(0,0,0)), hash31(i + float3(1,0,0)), f.x),
                         lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), f.x),
                         lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            float fbm3(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * vnoise3(p);
                    p *= 2.07;
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                if (_LodRadialBias > 0.0001)
                {
                    float3 biasedWS = vp.positionWS
                        - normalize(vp.positionWS - _VoxelTerrainBodyCenter.xyz) * _LodRadialBias;
                    vp.positionWS = biasedWS;
                    vp.positionCS = TransformWorldToHClip(biasedWS);
                }
                VertexNormalInputs   vn = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.normalWS   = vn.normalWS;
                OUT.color      = IN.color;
                OUT.fogCoord   = ComputeFogFactor(vp.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ApplyBubbleCutout(IN.positionWS);
                float3 worldPos = IN.positionWS;
                float3 worldNormal = normalize(IN.normalWS);
                float3 terrainCoord = TerrainCoordinate(worldPos);
                float3 terrainUp = TerrainUp(worldPos);

                // ── Base colour from vertex colour (material ID → colour) ──
                float3 baseColor = _BaseColor.rgb * IN.color.rgb;

                // ── Procedural micro-detail: multi-octave noise for surface texture ──
                // This gives the terrain a rocky/grainy appearance without textures.
                // 9.9.0: detail FADES with distance (no far shimmer) and perturbs the
                // normal near the camera for tactile relief in direct sunlight.
                float camDist = distance(_WorldSpaceCameraPos, worldPos);
                float detailFade = saturate(1.0 - camDist / 140.0);

                float3 noisePos = terrainCoord * _NoiseFreq;
                float detail = fbm3(noisePos);
                float detail2 = fbm3(noisePos * 3.2 + 10.0);
                float microDetail = detail * 0.6 + detail2 * 0.4;

                // Apply detail as brightness modulation (darker in noise valleys).
                float detailMod = lerp(1.0, 0.65 + microDetail * 0.7, _DetailStrength * detailFade);
                baseColor *= detailMod;

                // ── Macro variation (9.9.0): ~55 m patches break up uniform fields ──
                float macro = fbm3(terrainCoord * 0.018);
                baseColor *= lerp(0.93, 1.07, macro);
                baseColor = lerp(baseColor, baseColor * float3(1.045, 1.0, 0.94), (macro - 0.5) * 0.55);

                // ── Procedural detail normals (9.9.0): near-camera relief ──
                if (detailFade > 0.01)
                {
                    float e = 0.35;
                    float hX = fbm3(noisePos + float3(e, 0.0, 0.0));
                    float hZ = fbm3(noisePos + float3(0.0, 0.0, e));
                    float3 grad = float3(hX - detail, 0.0, hZ - detail);
                    worldNormal = normalize(worldNormal + grad * (0.55 * _DetailStrength * detailFade));
                }

                // ── Slope-aware shading: steep = darker (enhances relief) ──
                // Support both flat world (Y-up) and spherical planets (radial-up from center)
                float radialUpDot = abs(dot(worldNormal, terrainUp));
                float flatUpDot = abs(worldNormal.y);
                float upDot = lerp(flatUpDot, radialUpDot, saturate(_VoxelTerrainIsPlanet));
                float slopeFactor = lerp(1.0 - _SlopeDarken, 1.0, saturate(upDot * 1.5));
                baseColor *= slopeFactor;

                // ── Specular variation: some surfaces shinier (wet rock look) ──
                float specVar = fbm3(terrainCoord * _DetailScale * 0.5);
                float smoothness = _Smoothness + specVar * _SpecularVar * 0.3;
                smoothness = saturate(smoothness);

                // ── Wet waterline band (9.9.0): darker, glossier sand right at the
                // shoreline — published by SphereWorld as _VoxelSeaRadius. ──
                if (_VoxelSeaRadius > 1.0 && _VoxelTerrainIsPlanet > 0.5)
                {
                    float rWS = distance(worldPos, _VoxelTerrainBodyCenter.xyz);
                    float wet = saturate(1.0 - abs(rWS - _VoxelSeaRadius - 0.1) / 1.6);
                    baseColor *= 1.0 - wet * 0.18;
                    smoothness = saturate(smoothness + wet * 0.35);
                }

                // ── Full PBR lighting ──
                InputData inputData = (InputData)0;
                inputData.positionWS        = worldPos;
                inputData.normalWS          = worldNormal;
                inputData.viewDirectionWS   = GetWorldSpaceNormalizeViewDir(worldPos);
                inputData.shadowCoord       = TransformWorldToShadowCoord(worldPos);
                inputData.fogCoord          = IN.fogCoord;
                inputData.bakedGI           = SampleSH(worldNormal);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo              = baseColor;
                surface.specular            = float3(0, 0, 0);
                surface.metallic            = _Metallic;
                surface.smoothness          = smoothness;
                surface.normalTS            = float3(0, 0, 1);
                surface.emission            = float3(0, 0, 0);
                surface.occlusion           = 1.0;
                surface.alpha               = 1.0;
                surface.clearCoatMask       = 0.0;
                surface.clearCoatSmoothness = 0.0;

                half4 finalColor = UniversalFragmentPBR(inputData, surface);
                finalColor.rgb = MixFog(finalColor.rgb, IN.fogCoord);
                return finalColor;
            }
            ENDHLSL
        }

        // ── Shadow caster pass ──
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _DetailScale;
                float  _DetailStrength;
                float  _SlopeDarken;
                float  _NoiseFreq;
                float  _Smoothness;
                float  _Metallic;
                float  _SpecularVar;
                float  _BubbleCutout;
                float  _LodRadialBias;
            CBUFFER_END

            float4 _VoxelTerrainBodyCenter;
            float3 _LightDirection;

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vertShadow(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                if (_LodRadialBias > 0.0001)
                    posWS -= normalize(posWS - _VoxelTerrainBodyCenter.xyz) * _LodRadialBias;
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 clip = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = clip;
                return OUT;
            }
            half4 fragShadow(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Depth only pass ──
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _DetailScale;
                float  _DetailStrength;
                float  _SlopeDarken;
                float  _NoiseFreq;
                float  _Smoothness;
                float  _Metallic;
                float  _SpecularVar;
                float  _BubbleCutout;
                float  _LodRadialBias;
            CBUFFER_END

            float4 _VoxelTerrainBodyCenter;

            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A IN)
            {
                V OUT; UNITY_SETUP_INSTANCE_ID(IN); UNITY_TRANSFER_INSTANCE_ID(IN,OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                if (_LodRadialBias > 0.0001)
                    posWS -= normalize(posWS - _VoxelTerrainBodyCenter.xyz) * _LodRadialBias;
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }
            half4 frag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
