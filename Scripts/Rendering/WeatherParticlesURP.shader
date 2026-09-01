// Soft alpha-blended material for the runtime-authored weather particle systems
// (rain streaks, snow flakes, splash puffs). The particle colour over lifetime
// rides the vertex colour; _TintColor is the per-system base tint. Particle shape
// is drawn procedurally from the billboard UV (same approach as SpaceDustURP, the
// project's proven particle path): _ShapeMode 0 draws a soft round dot (snow /
// splash), 1 draws a rain streak. No texture sampling, so there is nothing to
// mis-bind. Fog is applied so distant rain fades into the storm haze.
//
// RAIN DIRECTION: the quad stays a plain camera-facing billboard and the STREAK is
// drawn along the screen projection of the global world-space fall direction
// (_WeatherFallDir, published by WeatherParticles every frame). Unity's own
// velocity alignment orients the quad's X axis along the velocity while this shader
// draws its streak along V — a 90° mismatch that rendered rain as horizontal
// slashes. Projecting the fall vector ourselves removes the ambiguity completely:
// the streak is always along the true fall direction on screen, and it foreshortens
// to a dot when the rain falls straight toward or away from the camera, which is
// exactly what real rain does when you look along it.
Shader "VoxelEngine/WeatherParticlesURP"
{
    Properties
    {
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _ShapeMode ("Shape (0 = dot, 1 = streak)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "WeatherParticles"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _ShapeMode;
            CBUFFER_END

            // Global (Shader.SetGlobalVector from WeatherParticles): world-space unit vector
            // the precipitation is falling along. Deliberately outside the per-material buffer.
            float4 _WeatherFallDir;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Billboard UV spans 0..1 across the quad; remap so 0,0 is the centre.
                float2 p = input.uv * 2.0 - 1.0;

                // Soft round dot (snow / splash).
                float radius = length(p);
                float dotAlpha = pow(saturate(1.0 - radius), 1.8);

                // Rain streak: drawn along the screen projection of the world fall direction.
                float3 fall = _WeatherFallDir.xyz;
                float3 camRight = UNITY_MATRIX_V[0].xyz;   // view-matrix rows are the camera basis
                float3 camUp    = UNITY_MATRIX_V[1].xyz;
                float2 proj = float2(dot(fall, camRight), dot(fall, camUp));
                float projLen = length(proj);
                float2 dir = projLen > 1e-4 ? proj / projLen : float2(0.0, 1.0);
                float2 perp = float2(-dir.y, dir.x);

                float along = dot(p, dir);
                float across = dot(p, perp);

                // Looking along the fall axis foreshortens the streak toward a short mark.
                float halfLen = max(0.22, projLen);
                float edgeX = 1.0 - smoothstep(0.0, 0.16, abs(across));
                float tipY  = 1.0 - smoothstep(halfLen * 0.55, halfLen, abs(along));
                float streakAlpha = edgeX * tipY;

                float shape = lerp(dotAlpha, streakAlpha, saturate(_ShapeMode));
                half alpha = shape * input.color.a * _TintColor.a;
                clip(alpha - 0.003);

                half3 rgb = input.color.rgb * _TintColor.rgb;
                rgb = MixFog(rgb, input.fogCoord);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
