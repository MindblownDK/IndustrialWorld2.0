// Assets/Scripts/VoxelEngine/Rendering/BlockDamageOverlayURP.shader
//
// 9.30.0 visible block damage & heat. A thin procedural shell drawn over any block
// mesh (grid hull, static placed block, tiered building piece). No textures:
//
//   * CRACKS  - world-space cellular (Voronoi edge) noise. As _Damage rises the
//               crack threshold widens so fine hairlines become open fractures,
//               and a second finer octave fills in between them.
//   * SCORCH  - soot darkening that follows the cracks first, then spreads across
//               the face at high damage / after the block has been burnt.
//   * GLOW    - incandescent emission driven by _Heat (0..1). Colour ramps from a
//               dull red through orange to white heat; cracks glow first because
//               the metal is thinnest there, then the whole face.
//   * SHIMMER - subtle time-varying flicker on the glow so hot steel looks alive.
//
// Alpha-blended, depth-tested against the block's own surface with a tiny offset so
// it never z-fights, and it never writes depth so it cannot occlude anything.
Shader "VoxelEngine/BlockDamageOverlayURP"
{
    Properties
    {
        _Damage      ("Damage 0..1",     Range(0, 1)) = 0
        _Heat        ("Heat 0..1",       Range(0, 1)) = 0
        _Scorch      ("Scorch 0..1",     Range(0, 1)) = 0
        _CrackScale  ("Crack Scale",     Range(0.2, 8)) = 1.6
        _Seed        ("Seed",            Float) = 0
        _GlowColor   ("Glow Color",      Color) = (1.0, 0.35, 0.08, 1)
        _GlowBoost   ("Glow Boost",      Range(0, 8)) = 2.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back
        Offset -1, -1

        Pass
        {
            Name "DamageOverlay"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Damage;
                float  _Heat;
                float  _Scorch;
                float  _CrackScale;
                float  _Seed;
                float4 _GlowColor;
                float  _GlowBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 worldNrm   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // ── Hashing / noise ───────────────────────────────────────────────
            float3 hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7,  74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123);
            }

            float hash31(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i + float3(0,0,0)); float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0)); float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1)); float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1)); float n111 = hash31(i + float3(1,1,1));
                float nx00 = lerp(n000, n100, f.x); float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x); float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y); float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            // Distance to the nearest Voronoi cell edge (F2 - F1). Small = on a crack.
            float cellEdge(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float f1 = 8.0, f2 = 8.0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float3 o = hash33(i + g + _Seed);
                    float3 r = g + o - f;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) { f2 = d; }
                }
                return sqrt(f2) - sqrt(f1);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                float3 worldNrm = TransformObjectToWorldNormal(IN.normalOS);
                // Push the shell a hair outward along the normal so it sits on top of
                // the block surface (Offset handles the remaining depth precision).
                worldPos += normalize(worldNrm) * 0.004;
                OUT.worldPos = worldPos;
                OUT.worldNrm = worldNrm;
                OUT.positionCS = TransformWorldToHClip(worldPos);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float damage = saturate(_Damage);
                float heat   = saturate(_Heat);
                float scorch = saturate(_Scorch);

                // Nothing to draw: keep the fragment fully transparent (early out).
                if (damage < 0.01 && heat < 0.01 && scorch < 0.01) return half4(0, 0, 0, 0);

                // Object-anchored coordinates so cracks ride with a moving ship.
                float3 objPos = TransformWorldToObject(IN.worldPos);
                float3 p = objPos * _CrackScale * 3.2 + _Seed;

                // ── Cracks ───────────────────────────────────────────────────
                float edgeCoarse = cellEdge(p);
                float edgeFine   = cellEdge(p * 2.7 + 17.0);

                // Crack width grows with damage: hairlines first, open fractures later.
                float widthCoarse = lerp(0.0, 0.16, damage * damage);
                float widthFine   = lerp(0.0, 0.07, saturate(damage - 0.35) / 0.65);

                float crackCoarse = 1.0 - smoothstep(widthCoarse * 0.35, widthCoarse, edgeCoarse);
                float crackFine   = 1.0 - smoothstep(widthFine * 0.35,   widthFine,   edgeFine);

                // Cracks only appear where a per-cell mask says the plate has failed, so a
                // lightly damaged block shows a few isolated fractures instead of a web.
                float3 cell = floor(p);
                float cellMask = step(1.0 - damage * 1.15, hash31(cell + 3.1));
                float crack = saturate(max(crackCoarse * cellMask, crackFine * step(0.45, damage)));
                crack *= step(0.02, damage);

                // ── Scorch / soot ────────────────────────────────────────────
                float sootNoise = valueNoise(p * 0.55 + 41.0);
                float sootSpread = saturate(scorch * 1.2 + damage * 0.35) * sootNoise;
                float soot = saturate(crack * 0.75 + sootSpread * sootSpread * 1.4);

                // ── Heat glow ────────────────────────────────────────────────
                // Cracks glow at lower heat (thin metal), faces catch up as heat rises.
                float shimmer = 0.92 + 0.08 * sin(_Time.y * 7.0 + dot(cell, float3(1.7, 2.3, 3.1)));
                float faceGlow  = smoothstep(0.18, 1.0, heat);
                float crackGlow = smoothstep(0.02, 0.55, heat) * crack;
                float glow = saturate(max(faceGlow, crackGlow)) * shimmer;

                // Fresnel lift so hot edges read from a distance.
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float fres = pow(1.0 - saturate(dot(normalize(IN.worldNrm), viewDir)), 2.0);
                glow = saturate(glow * (1.0 + 0.35 * fres));

                // ── Compose ──────────────────────────────────────────────────
                float3 crackColor = float3(0.02, 0.015, 0.012);
                float3 sootColor  = float3(0.05, 0.045, 0.04);

                float3 rgb = lerp(sootColor, crackColor, crack);
                float alpha = saturate(max(crack * 0.95, soot * 0.65));

                // Emission over-writes the dark layers: hot metal is bright, not sooty.
                float3 emission = _GlowColor.rgb * _GlowBoost * glow;
                rgb = lerp(rgb, emission, saturate(glow * 1.15));
                alpha = max(alpha, glow * 0.98);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
