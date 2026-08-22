// Assets/Scripts/VoxelEngine/Rendering/FireURP.shader
//
// 9.16.0 fire system (Liquids Overhaul, Part 2) - procedural flame shader for the
// FireRenderer's crossed-quad columns. No textures, no particles:
//
//   * World-space value-noise FBM shapes wispy flame tongues that rise and sway.
//   * A dense bright base thins into flickering licks toward the tip.
//   * Colour ramps from a white-hot core through orange to deep red tips.
//   * Per-column flicker phase rides in the vertex colour alpha (seeded per cell),
//     heat rides in the vertex colour rgb (embers burn dim, fresh flames blaze).
//   * Additive blend (the project-standard glow template, QuasarGlow) with the
//     vertex alpha kept for the shape so additive stacks never blow out.
Shader "VoxelEngine/FireURP"
{
    Properties
    {
        _BaseColor      ("Base Color",       Color) = (1.00, 0.45, 0.08, 1)
        _CoreColor      ("Core Color",       Color) = (1.00, 0.86, 0.38, 1)
        _TipColor       ("Tip Color",        Color) = (0.55, 0.07, 0.02, 1)
        _Brightness     ("Brightness",       Range(0, 4)) = 1.5
        _FlameSpeed     ("Flame Speed",      Range(0, 3)) = 1.1
        _NoiseScale     ("Noise Scale",      Range(0.2, 6)) = 1.7
        _FlickerSpeed   ("Flicker Speed",    Range(0, 10)) = 6.0
        _FlickerAmount  ("Flicker Amount",   Range(0, 1)) = 0.35
        _Sway           ("Sway",             Range(0, 0.6)) = 0.20
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _CoreColor;
                float4 _TipColor;
                float  _Brightness;
                float  _FlameSpeed;
                float  _NoiseScale;
                float  _FlickerSpeed;
                float  _FlickerAmount;
                float  _Sway;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : TEXCOORD1;
                float3 worldPos   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // ---- procedural value noise (hash + trilinear smooth) ----
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i + float3(0.0, 0.0, 0.0));
                float n100 = hash31(i + float3(1.0, 0.0, 0.0));
                float n010 = hash31(i + float3(0.0, 1.0, 0.0));
                float n110 = hash31(i + float3(1.0, 1.0, 0.0));
                float n001 = hash31(i + float3(0.0, 0.0, 1.0));
                float n101 = hash31(i + float3(1.0, 0.0, 1.0));
                float n011 = hash31(i + float3(0.0, 1.0, 1.0));
                float n111 = hash31(i + float3(1.0, 1.0, 1.0));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    v += a * vnoise(p);
                    p = p * 2.13 + 11.7;
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float seed = IN.color.a;            // per-column flicker phase
                float tint = IN.color.r;            // heat baked in by the renderer

                float t = _Time.y * _FlameSpeed;
                float flicker = 1.0 - _FlickerAmount
                    * (0.5 + 0.5 * sin(_Time.y * _FlickerSpeed + seed * 6.28318));

                float2 uv = IN.uv;
                float h = saturate(uv.y);

                // Sway: the whole column leans and dances, more at the tip.
                float n1 = fbm(float3(IN.worldPos.x * 0.35 + seed * 17.3, t * 0.8, IN.worldPos.y * 0.35));
                float sway = (h * h) * _Sway * (n1 - 0.5) * 2.0;
                float2 nuv = float2(uv.x + sway, uv.y);

                // Edge falloff: flames taper to nothing at the sides.
                float edge = 1.0 - abs(nuv.x - 0.5) * 2.0;
                edge = smoothstep(0.0, 0.3, edge);

                // Vertical shape: dense bright base + wispy rising tongues.
                float noise = fbm(float3(nuv.x * 3.0 * _NoiseScale,
                                         nuv.y * _NoiseScale + t * 0.9,
                                         seed * 13.7));
                float column = smoothstep(h, h + 0.3, noise);
                float body = smoothstep(0.0, 0.35, h) * (1.0 - smoothstep(0.3, 1.0, h));
                float shape = max(body, column) * edge * flicker;

                // Colour ramp: white-hot core low, orange mid, deep red tips.
                float3 col = lerp(_BaseColor.rgb, _CoreColor.rgb,
                                  saturate((1.0 - h) * 1.8) * (0.55 + 0.45 * noise));
                col = lerp(col, _TipColor.rgb, saturate((h - 0.3) * (1.3 + noise)));

                float3 rgb = col * tint * shape * _Brightness;
                return half4(rgb, saturate(shape * 0.9));
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
