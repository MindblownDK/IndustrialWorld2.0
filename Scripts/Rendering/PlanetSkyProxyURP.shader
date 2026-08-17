// Native sky-proxy shader for distant celestial bodies.
//
// The SpaceBodyRenderer's compressed "sky sphere" is now a REAL sampled terrain
// surface (vertex colours baked from the same SphereDensity field the voxel
// generator uses), so every planet in the sky shows its actual continents and
// oceans instead of a flat colored ball.
//
// Unlike the terrain surface shaders this one has NO camera-distance clip and NO
// dependency on the active body's global shader context: proxies are drawn at
// compressed positions (often within a few km of the camera) and must keep
// rendering no matter which body the streamer is following. Lighting comes from
// the real sun directional light so day/night sides match the local sky.
Shader "VoxelEngine/PlanetSkyProxyURP"
{
    Properties
    {
        _Tint ("Global Tint / Fade", Color) = (1,1,1,1)
        _AtmosphereRim ("Atmosphere Rim", Color) = (0.18,0.42,0.78,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PlanetSkyProxy"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // The icosphere has verified outward winding; back-face culling avoids
            // rendering the far side through the transparent proxy.
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
            CBUFFER_END

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

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(_WorldSpaceCameraPos - input.positionWS);
                Light sun = GetMainLight();
                float diffuse = saturate(dot(normal, sun.direction)) * 0.75 + 0.25;
                float rim = pow(saturate(1.0 - dot(view, normal)), 3.0) * _RimStrength;
                float3 color = input.color.rgb * _Tint.rgb * diffuse;
                color += _AtmosphereRim.rgb * rim;
                color = MixFog(color, input.fogCoord);
                float alpha = input.color.a * _Tint.a;
                if (alpha < 0.01) discard;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
