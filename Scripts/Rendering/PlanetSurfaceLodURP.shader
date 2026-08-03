// Native full-planet LOD shader. Vertex colour is the sampled spherical terrain/ocean map.
Shader "VoxelEngine/PlanetSurfaceLodURP"
{
    Properties
    {
        _Tint ("Global Tint / Fade", Color) = (1,1,1,1)
        _AtmosphereRim ("Atmosphere Rim", Color) = (0.18,0.42,0.78,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.16
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
                float diffuse = saturate(dot(normal, sun.direction)) * 0.72 + 0.28;
                float rim = pow(saturate(1.0 - dot(view, normal)), 3.0) * _RimStrength;
                float3 color = input.color.rgb * _Tint.rgb * diffuse;
                color += _AtmosphereRim.rgb * rim;
                color = MixFog(color, input.fogCoord);
                return half4(color, input.color.a * _Tint.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
