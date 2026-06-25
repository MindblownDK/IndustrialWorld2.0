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
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

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
            CBUFFER_END

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
                float3 worldPos = IN.positionWS;
                float3 worldNormal = normalize(IN.normalWS);

                // ── Base colour from vertex colour (material ID → colour) ──
                float3 baseColor = _BaseColor.rgb * IN.color.rgb;

                // ── Procedural micro-detail: multi-octave noise for surface texture ──
                // This gives the terrain a rocky/grainy appearance without textures.
                float3 noisePos = worldPos * _NoiseFreq;
                float detail = fbm3(noisePos);
                float detail2 = fbm3(noisePos * 3.2 + 10.0);
                float microDetail = detail * 0.6 + detail2 * 0.4;

                // Apply detail as brightness modulation (darker in noise valleys).
                float detailMod = lerp(1.0, 0.65 + microDetail * 0.7, _DetailStrength);
                baseColor *= detailMod;

                // ── Slope-aware shading: steep = darker (enhances relief) ──
                float upDot = abs(worldNormal.y);
                float slopeFactor = lerp(1.0 - _SlopeDarken, 1.0, saturate(upDot * 1.5));
                baseColor *= slopeFactor;

                // ── Specular variation: some surfaces shinier (wet rock look) ──
                float specVar = fbm3(worldPos * _DetailScale * 0.5);
                float smoothness = _Smoothness + specVar * _SpecularVar * 0.3;
                smoothness = saturate(smoothness);

                // ── Full PBR lighting ──
                InputData inputData = (InputData)0;
                inputData.positionWS        = worldPos;
                inputData.normalWS          = worldNormal;
                inputData.viewDirectionWS   = GetWorldSpaceNormalizeViewDir(worldPos);
                inputData.shadowCoord       = TransformWorldToShadowCoord(worldPos);
                inputData.fogCoord          = IN.fogCoord;
                inputData.bakedGI           = SampleSH(worldNormal);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo     = baseColor;
                surface.metallic   = _Metallic;
                surface.smoothness = smoothness;
                surface.alpha      = 1.0;
                surface.occlusion  = 1.0;
                surface.normalTS   = float3(0,0,1);

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
            CBUFFER_END

            float3 _LightDirection;

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vertShadow(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
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
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A IN) { V OUT; UNITY_SETUP_INSTANCE_ID(IN); UNITY_TRANSFER_INSTANCE_ID(IN,OUT); OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz); return OUT; }
            half4 frag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
