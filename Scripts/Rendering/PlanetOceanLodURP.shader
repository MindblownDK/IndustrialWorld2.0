// Ocean-only full planet LOD. Triangles are emitted exclusively for real terrain-defined ocean basins.
Shader "VoxelEngine/PlanetOceanLodURP"
{
    Properties
    {
        _DeepColor ("Deep Ocean", Color) = (0.015,0.07,0.24,0.95)
        _ShallowColor ("Shallow Ocean", Color) = (0.10,0.45,0.78,0.82)
        _BodyCenter ("Body Center", Vector) = (0,0,0,1)
        _ViewerPosition ("Viewer Position", Vector) = (0,0,0,1)
        _CutoutRadius ("Local Water Cutout", Float) = 256
        _WaveAmplitude ("Wave Amplitude", Range(0,2)) = 0.55
        _WaveTime ("Wave Time", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "OceanLod"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // Ocean triangles use verified outward icosphere winding; keep back-face culling
            // so the far side cannot blend through the transparent near-side ocean.
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float4 _BodyCenter;
                float4 _ViewerPosition;
                float _CutoutRadius;
                float _WaveAmplitude;
                float _WaveTime;
            CBUFFER_END

            // Weather → sea state (global, published by WeatherSeaState): the distant ocean
            // swells with the same storm that is churning the water at your feet.
            float _WeatherSeaState;

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

            float3 BodyUp(float3 worldPos)
            {
                float3 radial = worldPos - _BodyCenter.xyz;
                float lenSq = dot(radial, radial);
                return lenSq > 0.0001 ? radial * rsqrt(lenSq) : float3(0,1,0);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                float3 up = BodyUp(worldPos);
                float seaAmp = 1.0 + saturate(_WeatherSeaState) * 1.45;
                float wave = sin(dot(worldPos - _BodyCenter.xyz, float3(0.017, 0.023, 0.013))
                                 + _WaveTime * (0.55 + saturate(_WeatherSeaState) * 0.25)) * _WaveAmplitude * seaAmp;
                worldPos += up * wave;
                output.positionWS = worldPos;
                output.normalWS = up;
                output.color = input.color;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // The streamed voxel ocean owns the local playable area. This cutout prevents
                // overlap while the real water mesh fills around the player.
                clip(distance(input.positionWS, _ViewerPosition.xyz) - _CutoutRadius);
                float depth = saturate(input.color.r);
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(_WorldSpaceCameraPos - input.positionWS);
                Light sun = GetMainLight();
                float diffuse = saturate(dot(normal, sun.direction)) * 0.55 + 0.35;
                float fresnel = pow(saturate(1.0 - dot(view, normal)), 3.5);
                float3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth) * diffuse;
                color = lerp(color, SampleSH(normal), fresnel * 0.35);
                color = MixFog(color, input.fogCoord);
                return half4(color, lerp(_ShallowColor.a, _DeepColor.a, depth));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
