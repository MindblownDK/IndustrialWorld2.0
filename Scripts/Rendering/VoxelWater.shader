// Assets/Scripts/VoxelEngine/Rendering/VoxelWater.shader
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    VOXEL WATER — animated, translucent               ║
// ║                                                                       ║
// ║  A water shader for the spherical voxel terrain. Since water renders  ║
// ║  as solid voxels in the terrain mesh (following the sphere curvature),║
// ║  this shader is applied via material override on WaterLiquid voxels.  ║
// ║                                                                       ║
// ║  • Wave animation: gentle undulation via vertex displacement + noise  ║
// ║  • Depth-based colour: shallow = bright teal, deep = dark navy        ║
// ║  • Fresnel rim: edges glow when viewed at grazing angles             ║
// ║  • Specular sun glints (sun glitter on the surface)                  ║
// ║  • Transparency with depth fade                                       ║
// ║  • Subsurface scattering approximation (water glows from within)     ║
// ╚══════════════════════════════════════════════════════════════════════╝
Shader "VoxelEngine/VoxelWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.15, 0.55, 0.65, 0.85)
        _DeepColor    ("Deep Color",    Color) = (0.04, 0.12, 0.35, 0.92)
        _WaveHeight   ("Wave Height",   Range(0, 0.5)) = 0.08
        _WaveSpeed    ("Wave Speed",    Range(0, 3))   = 0.8
        _WaveScale    ("Wave Scale",    Range(0.5, 10)) = 2.5
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelColor ("Fresnel Color", Color) = (0.6, 0.85, 0.95, 1)
        _Smoothness   ("Smoothness",    Range(0, 1))   = 0.88
        _SunGlint     ("Sun Glint",     Range(0, 2))   = 1.2
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _WaveHeight;
                float  _WaveSpeed;
                float  _WaveScale;
                float  _FresnelPower;
                float4 _FresnelColor;
                float  _Smoothness;
                float  _SunGlint;
            CBUFFER_END

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
                float4 vertexColor: TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Hash-based noise for wave detail.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    v += a * vnoise(p);
                    p *= 2.1;
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

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);

                // ── Wave displacement: gentle vertical undulation ──
                float2 waveUV = worldPos.xz * _WaveScale + _Time.y * _WaveSpeed;
                float wave = fbm(waveUV);
                float wave2 = fbm(waveUV * 2.3 - _Time.y * _WaveSpeed * 0.7);
                float totalWave = (wave * 0.6 + wave2 * 0.4 - 0.5) * 2.0;

                // Displace along the world normal (so waves go outward on a sphere).
                float3 worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                worldPos += worldNormal * totalWave * _WaveHeight;

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.positionWS = worldPos;
                OUT.normalWS = worldNormal;
                OUT.vertexColor = IN.color;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 worldPos = IN.positionWS;
                float3 normal = normalize(IN.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(worldPos);

                // ── Depth-based colour blend ──
                // We don't have a depth buffer for voxel water, so we use the vertex colour
                // brightness as a depth proxy (the mesher already applies AO darkening).
                float depthProxy = saturate(IN.vertexColor.r + IN.vertexColor.g + IN.vertexColor.b) / 3.0;
                depthProxy = saturate(depthProxy * 1.5);
                float3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, depthProxy);

                // ── Fresnel: brighter at grazing angles ──
                float fresnel = pow(1.0 - saturate(dot(viewDir, normal)), _FresnelPower);
                waterColor = lerp(waterColor, _FresnelColor.rgb, fresnel * 0.5);

                // ── Sun glints: specular highlight from the main light ──
                Light mainLight = GetMainLight();
                float3 halfVec = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(normal, halfVec)), 120.0);
                waterColor += mainLight.color * spec * _SunGlint;

                // ── Wave-driven brightness variation ──
                float2 waveUV = worldPos.xz * _WaveScale + _Time.y * _WaveSpeed;
                float waveShimmer = fbm(waveUV * 3.0);
                waterColor *= (0.9 + waveShimmer * 0.2);

                // ── Lighting ──
                float NdotL = saturate(dot(normal, mainLight.direction));
                float3 ambient = SampleSH(normal) * 0.5;
                float3 diffuse = mainLight.color * NdotL * 0.7;
                float3 finalColor = waterColor * (ambient + diffuse);

                // ── Transparency: more transparent when looking straight down (shallow) ──
                float alpha = lerp(_DeepColor.a, _ShallowColor.a, depthProxy);
                alpha = lerp(alpha, 1.0, fresnel * 0.4);  // more opaque at grazing angles

                finalColor = MixFog(finalColor, IN.fogCoord);
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Transparent/Diffuse"
}
