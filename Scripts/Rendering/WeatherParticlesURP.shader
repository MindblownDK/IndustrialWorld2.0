// Soft alpha-blended material for the runtime-authored weather particle systems
// (rain streaks, snow flakes, splash puffs). The particle colour over lifetime
// rides the vertex colour; _TintColor is the per-system base tint. Particle shape
// is drawn procedurally from the billboard UV (same approach as SpaceDustURP, the
// project's proven particle path): _ShapeMode 0 draws an antialiased snowflake /
// soft frost puff, 1 draws a directional rain streak. No texture sampling, so there
// is zero texture bandwidth overhead. Fog is applied so distant precipitation fades
// smoothly into the storm haze.
//
// RAIN DIRECTION: the quad stays a plain camera-facing billboard and the STREAK is
// drawn along the screen projection of the global world-space fall direction
// (_WeatherFallDir, published by WeatherParticles).
//
// PERFORMANCE: Early alpha-clip rejects invisible fragments before complex blending,
// maximizing GPU rasterization framerates during torrential downpours and blizzards.
Shader "VoxelEngine/WeatherParticlesURP"
{
    Properties
    {
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _ShapeMode ("Shape (0 = snowflake/puff, 1 = streak)", Float) = 0
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
            // the precipitation is falling along.
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
                float radius = length(p);

                // ── Procedural Antialiased Snowflake / Soft Frost Puff (ShapeMode 0) ──
                // Core crystal density + soft feathered perimeter for delicate fluttering flakes.
                float flakeCore = saturate(1.0 - radius * 1.5);
                float flakeSoft = pow(saturate(1.0 - radius), 1.6);
                float flakeAlpha = saturate(flakeSoft * 0.75 + flakeCore * 0.55);

                // ── Procedural Rain Streak (ShapeMode 1) ──
                // Drawn along the screen projection of the world fall direction.
                float3 fall = _WeatherFallDir.xyz;
                float3 camRight = UNITY_MATRIX_V[0].xyz;   // view-matrix rows are the camera basis
                float3 camUp    = UNITY_MATRIX_V[1].xyz;
                float2 proj = float2(dot(fall, camRight), dot(fall, camUp));
                float projLen = length(proj);
                float2 dir = projLen > 1e-4 ? proj / projLen : float2(0.0, 1.0);
                float2 perp = float2(-dir.y, dir.x);

                float along = dot(p, dir);
                float across = dot(p, perp);

                // Looking along the fall axis foreshortens the streak naturally toward a drop mark.
                float halfLen = max(0.22, projLen);
                float edgeX = 1.0 - smoothstep(0.0, 0.16, abs(across));
                float tipY  = 1.0 - smoothstep(halfLen * 0.55, halfLen, abs(along));
                float streakAlpha = edgeX * tipY;

                float shape = lerp(flakeAlpha, streakAlpha, saturate(_ShapeMode));
                half alpha = (half)(shape * input.color.a * _TintColor.a);

                // Early clip for maximum fillrate performance during heavy precipitation.
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
