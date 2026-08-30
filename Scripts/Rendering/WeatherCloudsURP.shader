// Cloud dome for the weather system -- two nested domes scroll a tileable
// procedural noise texture high above the player (radially aligned, following
// the camera). Deliberately NOT fogged: the storm fog lives at ground level,
// the cloud deck is the sky itself and must stay visible through it.
// uv0  = tileable cloud texture coordinates (tiled + scrolled via _MainTex_ST).
// uv1.y = polar position: 0 at the zenith, 1 at the rim (used for the soft
//         horizon fade so the dome dissolves into the haze, not a hard circle).
Shader "VoxelEngine/WeatherCloudsURP"
{
    Properties
    {
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _MainTex ("Cloud Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "WeatherClouds"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _Opacity;
                float4 _MainTex_ST;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 polar : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float polarY : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHclip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.polarY = input.polar.y;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                // Soft rim fade: 1 high over the sky, easing to 0 at the dome edge.
                half rim = 1.0 - smoothstep(0.75, 1.0, input.polarY);
                half alpha = tex.a * _Opacity * input.color.a * rim;
                half3 rgb = tex.rgb * _TintColor.rgb * input.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
