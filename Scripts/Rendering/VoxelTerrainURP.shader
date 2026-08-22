// Assets/Scripts/VoxelEngine/Rendering/VoxelTerrainURP.shader
//
// Voxel terrain shader. Per-vertex color tints the material (set by SurfaceNetsJob).
// Optional triplanar _BaseMap: when supplied, sampled by world-space position projected on
// the three cardinal planes, blended by normal direction. Lets you give the whole terrain
// a tileable rock/dirt texture without UVs (Surface Nets meshes have no UVs).
// 9.17.0: per-material SURFACE TEXTURES — the mesher carries each vertex's dominant
// material id in vertex-colour alpha and VoxelSurfaceTextures.hlsl renders it (stone
// strata/cracks, sand ripples, ore glints, grass-to-soil slopes, crystal facets…).
Shader "VoxelEngine/VoxelTerrainURP"
{
    Properties
    {
        _BaseColor   ("Base Color",      Color) = (1,1,1,1)
        _BaseMap     ("Base Map",        2D)    = "white" {}
        _BaseMap_ST  ("Tiling/Offset",   Vector) = (0.2, 0.2, 0, 0)   // x = tiles per metre
        _TexBlend    ("Texture Strength",Range(0,1)) = 0.6           // 0 = pure vertex color, 1 = pure texture * vertex
        _SurfaceTexStrength ("Surface Texturing", Range(0,1)) = 1.0
        _Smoothness  ("Smoothness",      Range(0,1)) = 0.2
        _Metallic    ("Metallic",        Range(0,1)) = 0.0
        // Single-surface handshake (9.3.0): 1 on the GPU LOD-skin material clone.
        _BubbleCutout  ("Bubble Cutout",    Float) = 0
        // Radial deflation (m) applied to the LOD skin so it sits under the bubble
        // surface instead of z-fighting with it.
        _LodRadialBias ("LOD Radial Bias",  Float) = 0
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
                float4 _BaseMap_ST;
                float  _TexBlend;
                float  _SurfaceTexStrength;
                float  _Smoothness;
                float  _Metallic;
                float  _BubbleCutout;
                float  _LodRadialBias;
            CBUFFER_END

            // ── 9.17.0 per-material surface texturing (shared with VoxelTerrainEnhanced) ──
            #include "VoxelSurfaceTextures.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 _VoxelTerrainBodyCenter;
            float _VoxelTerrainIsPlanet;

            // ── Single-surface handshake (9.1.0) ──────────────────────────
            // The GPU quadtree LOD skin sets _BubbleCutout = 1 on its material clone.
            // Inside the gameplay bubble's meshed+collider ball (globals published by
            // SphereWorld) its fragments are clipped, so mined holes and tunnels show
            // the REAL edited voxels — never a phantom LOD surface behind them.
            float4 _VoxelBubbleCenterWS;       // global
            float  _VoxelBubbleCutoutRadius;   // global (0 = disabled)

            void ApplyBubbleCutout(float3 positionWS)
            {
                if (_BubbleCutout > 0.5)
                {
                    float3 d = positionWS - _VoxelBubbleCenterWS.xyz;
                    clip(dot(d, d) - _VoxelBubbleCutoutRadius * _VoxelBubbleCutoutRadius);
                }
            }

            float3 TerrainMappingPosition(float3 worldPos)
            {
                return lerp(worldPos, worldPos - _VoxelTerrainBodyCenter.xyz, saturate(_VoxelTerrainIsPlanet));
            }

            // Local "up" for surface-plane textures: radial on planets, Y-up otherwise.
            float3 TerrainUp(float3 worldPos)
            {
                float3 radial = worldPos - _VoxelTerrainBodyCenter.xyz;
                float lenSq = dot(radial, radial);
                radial = lenSq > 0.0001 ? radial * rsqrt(lenSq) : float3(0, 1, 0);
                return normalize(lerp(float3(0, 1, 0), radial, saturate(_VoxelTerrainIsPlanet)));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vn = GetVertexNormalInputs(IN.normalOS);

                // LOD-skin deflation: sink the whole skin slightly toward the core so the
                // gameplay bubble's surface always renders ON TOP (no coincident z-fighting).
                float3 posWS = vp.positionWS;
                if (_LodRadialBias > 0.0001)
                {
                    float3 upWS = normalize(posWS - _VoxelTerrainBodyCenter.xyz);
                    posWS -= upWS * _LodRadialBias;
                    vp.positionCS = TransformWorldToHClip(posWS);
                }

                OUT.positionCS = vp.positionCS;
                OUT.positionWS = posWS;
                OUT.normalWS   = vn.normalWS;
                OUT.color      = IN.color;
                OUT.fogCoord   = ComputeFogFactor(vp.positionCS.z);
                return OUT;
            }

            // Triplanar sampling — projects the world position onto 3 cardinal planes and
            // blends by normal direction, giving a UV-free texture mapping.
            float3 SampleTriplanar(float3 worldPos, float3 worldNormal, float2 tiling)
            {
                float3 blend = abs(worldNormal);
                blend = blend / max(0.0001, blend.x + blend.y + blend.z);
                float2 uvX = worldPos.zy * tiling;
                float2 uvY = worldPos.xz * tiling;
                float2 uvZ = worldPos.xy * tiling;
                float3 cX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb;
                float3 cY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb;
                float3 cZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb;
                return cX * blend.x + cY * blend.y + cZ * blend.z;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ApplyBubbleCutout(IN.positionWS);

                float3 worldNormal = normalize(IN.normalWS);
                float3 terrainUp   = TerrainUp(IN.positionWS);

                float3 baseTint = _BaseColor.rgb * IN.color.rgb;
                float3 tex = SampleTriplanar(TerrainMappingPosition(IN.positionWS), worldNormal, _BaseMap_ST.xy);
                float3 albedo = lerp(baseTint, baseTint * tex, _TexBlend);

                // ── PER-MATERIAL SURFACE TEXTURES (9.17.0) — material id rides the
                // vertex-colour alpha; see VoxelSurfaceTextures.hlsl. Legacy meshes
                // (alpha 255) fall back to the restrained generic grain. ──
                float camDist   = distance(_WorldSpaceCameraPos, IN.positionWS);
                float detailFade = saturate(1.0 - camDist / 140.0);
                uint   matId        = (uint)round(IN.color.a * 255.0);
                float3 vsxAlbedo    = float3(1, 1, 1);
                float2 vsxGrad      = float2(0, 0);
                float  vsxSmoothAdd = 0.0;
                float  vsxMetalAdd  = 0.0;
                float3 vsxEmission  = float3(0, 0, 0);
                VsxSurface(matId, TerrainMappingPosition(IN.positionWS), terrainUp, worldNormal,
                           detailFade, _SurfaceTexStrength, albedo,
                           vsxAlbedo, vsxGrad, vsxSmoothAdd, vsxMetalAdd, vsxEmission);
                albedo *= vsxAlbedo;
                worldNormal = VsxApplyRelief(worldNormal, vsxGrad, terrainUp);

                InputData inputData = (InputData)0;
                inputData.positionWS        = IN.positionWS;
                inputData.normalWS          = worldNormal;
                inputData.viewDirectionWS   = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord       = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord          = IN.fogCoord;
                inputData.bakedGI           = SampleSH(worldNormal);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo     = albedo;
                surface.metallic   = saturate(_Metallic + vsxMetalAdd);
                surface.smoothness = saturate(_Smoothness + vsxSmoothAdd);
                surface.emission   = vsxEmission;
                surface.alpha      = 1.0;
                surface.occlusion  = 1.0;
                surface.normalTS   = float3(0,0,1);

                half4 finalColor = UniversalFragmentPBR(inputData, surface);
                finalColor.rgb = MixFog(finalColor.rgb, IN.fogCoord);
                return finalColor;
            }
            ENDHLSL
        }

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
                float4 _BaseMap_ST;
                float  _TexBlend;
                float  _SurfaceTexStrength;
                float  _Smoothness;
                float  _Metallic;
                float  _BubbleCutout;
                float  _LodRadialBias;
            CBUFFER_END

            float4 _VoxelTerrainBodyCenter;
            float3 _LightDirection;
            float3 _LightPosition;

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
                float4 clip  = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
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
                float4 _BaseMap_ST;
                float  _TexBlend;
                float  _SurfaceTexStrength;
                float  _Smoothness;
                float  _Metallic;
                float  _BubbleCutout;
                float  _LodRadialBias;
            CBUFFER_END

            float4 _VoxelTerrainBodyCenter;

            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
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
