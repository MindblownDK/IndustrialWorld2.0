// Assets/Scripts/VoxelEngine/Rendering/VoxelSurfaceTextures.hlsl
//
// +======================================================================+
// |              PER-MATERIAL SURFACE TEXTURING (9.17.0)                 |
// |                                                                       |
// |  Shared procedural texturing library for BOTH terrain shaders        |
// |  (VoxelTerrainEnhanced -- the live material -- and VoxelTerrainURP).   |
// |                                                                       |
// |  The meshers (SurfaceNetsJob + GpuDualContourJob) encode the         |
// |  dominant MATERIAL ID of every surface vertex into the vertex        |
// |  colour ALPHA channel. This library turns that id into a real,       |
// |  material-appropriate surface texture -- computed procedurally,       |
// |  zero texture samples, budgeted noise, camera-distance faded:        |
// |                                                                       |
// |    Stone   -- strata banding, hairline cracks, granite speckle        |
// |    Sand    -- wind ripples with bright crests + ripple normals        |
// |    Clay    -- soft mottle + rare drying cracks                        |
// |    Ice     -- crystalline facets, glossy sheen                        |
// |    Ores    -- rock matrix + glinting metallic flecks (Uranium glows)  |
// |    Coal    -- matte organic streaks                                   |
// |    OilRock -- dark soaked mottle with wet gloss streaks               |
// |    Wood    -- vertical grain                                          |
// |    Grass   -- organic clumps, blade streaks, dry patches,             |
// |              and steep slopes blend to exposed soil                  |
// |    Dust    -- fine wind ripples + broad colour drifts                 |
// |    Ash     -- soft settled granular layers                            |
// |    Bog     -- sickly mottle with wet gloss pools                      |
// |    Basalt  -- angular columnar fractures + faint warm veins           |
// |    Crystal -- hard facets, high gloss, faint inner glow               |
// |    Generic -- restrained grain (unknown / future materials)           |
// |                                                                       |
// |  Everything derives its tint from the material's own vertex colour -- |
// |  no game palette is hardcoded, custom material definitions keep      |
// |  working, and alpha=255 (legacy meshes) falls back to Generic.       |
// +======================================================================+

#ifndef VOXEL_SURFACE_TEXTURES_INCLUDED
#define VOXEL_SURFACE_TEXTURES_INCLUDED

// -- Material classes --------------------------------------------------
#define VSX_GENERIC 0.0
#define VSX_STONE   1.0
#define VSX_SAND    2.0
#define VSX_CLAY    3.0
#define VSX_ICE     4.0
#define VSX_ORE     5.0
#define VSX_COAL    6.0
#define VSX_OILROCK 7.0
#define VSX_WOOD    8.0
#define VSX_GRASS   9.0
#define VSX_DUST    10.0
#define VSX_ASH     11.0
#define VSX_BOG     12.0
#define VSX_BASALT  13.0
#define VSX_CRYSTAL 14.0

// MaterialId byte values (must mirror VoxelEngine.Materials.MaterialId).
// NOTE: deliberately PURE FLOAT end to end -- these shaders compile without an
// explicit #pragma target (legacy level_9_x profiles forbid integer arithmetic),
// and integral ids 0..255 compare exactly in float32.
float VsxClass(float id)
{
    // Ores first (the big family), then singles.
    if (id == 7.0  || id == 8.0  || id == 10.0 || id == 11.0 || id == 12.0 ||
        id == 13.0 || id == 14.0 || id == 15.0 || id == 16.0 || id == 17.0 || id == 21.0) return VSX_ORE;
    if (id == 1.0  || id == 20.0) return VSX_STONE;   // Stone (+ legacy floor reads as stone)
    if (id == 2.0)  return VSX_SAND;
    if (id == 3.0)  return VSX_CLAY;
    if (id == 4.0)  return VSX_ICE;
    if (id == 9.0)  return VSX_COAL;
    if (id == 18.0) return VSX_OILROCK;               // solid crude = oil-soaked rock
    if (id == 19.0) return VSX_WOOD;
    if (id == 22.0) return VSX_GRASS;
    if (id == 23.0) return VSX_DUST;
    if (id == 24.0) return VSX_ASH;
    if (id == 25.0) return VSX_BOG;
    if (id == 26.0) return VSX_BASALT;
    if (id == 27.0) return VSX_CRYSTAL;
    return VSX_GENERIC;                               // air never meshes; 255 = legacy -> grain
}

// -- Budgeted noise (one source of truth for both terrain shaders) -----
float VsxHash13(float3 p)
{
    p = frac(p * 0.1031 + 0.33);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
float VsxHash21(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}
float VsxVnoise3(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(VsxHash13(i),                   VsxHash13(i + float3(1,0,0)), f.x),
             lerp(VsxHash13(i + float3(0,1,0)),   VsxHash13(i + float3(1,1,0)), f.x), f.y),
        lerp(lerp(VsxHash13(i + float3(0,0,1)),   VsxHash13(i + float3(1,0,1)), f.x),
             lerp(VsxHash13(i + float3(0,1,1)),   VsxHash13(i + float3(1,1,1)), f.x), f.y),
        f.z);
}
// Two octaves -- the deliberate cost ceiling for hot fragment work.
float VsxNoise3(float3 p)
{
    return VsxVnoise3(p) * 0.65 + VsxVnoise3(p * 2.13 + 7.7) * 0.35;
}
// One octave where only broad drift is needed.
float VsxBroad(float3 p) { return VsxVnoise3(p); }

// Plane coordinates on the surface tangent frame (for ripples / cracks /
// facets that must lie ON the ground, wrapped correctly on spheres).
void VsxTangentFrame(float3 up, out float3 t1, out float3 t2)
{
    float3 a = abs(up.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
    t1 = normalize(cross(a, up));
    t2 = cross(up, t1);
}
float2 VsxPlaneCoord(float3 p, float3 up)
{
    float3 t1, t2; VsxTangentFrame(up, t1, t2);
    return float2(dot(p, t1), dot(p, t2));
}

// 2D voronoi (3x3) -- F1 distance + its cell hash. Cracks, facets, columns.
void VsxVoronoi2(float2 p, out float f1, out float cellHash)
{
    float2 cell = floor(p);
    f1 = 8.0; cellHash = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float2 offset = float2(x, y);
        float h = VsxHash21(cell + offset);
        float2 site = cell + offset + float2(h, frac(h * 7.31));
        float d = length(p - site);
        if (d < f1) { f1 = d; cellHash = h; }
    }
}

// Desaturate helper (grass -> soil blend keeps the world palette coherent).
float VsxDesat(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

// -- The pass ----------------------------------------------------------
// albedoMul  : multiplies the current base colour.
// gradT      : tangent-frame gradient for normal relief (albedo-driven cost only).
// smoothAdd  : adds to smoothness.  metalAdd: adds to metallic.
// emissionAdd: adds to emission (uranium / crystal / basalt veins -- tiny values).
void VsxSurface(float  matId,
                float3 terrainCoord, float3 terrainUp,
                float3 normalWS,
                float  fade,          // 0 far -> 1 near (caller's distance fade)
                float  strength,      // user strength 0..1
                float3 baseRgb,
                out float3 albedoMul,
                out float2 gradT,
                out float  smoothAdd,
                out float  metalAdd,
                out float3 emissionAdd)
{
    albedoMul = float3(1, 1, 1); gradT = float2(0, 0); smoothAdd = 0.0; metalAdd = 0.0; emissionAdd = float3(0, 0, 0);
    if (fade <= 0.01 || strength <= 0.001) return;

    float3 p = terrainCoord;
    float3 up = terrainUp;
    float cls = VsxClass(matId);
    // Local surface frame: pc = on-ground plane coords, h = height along local up.
    // Every "vertical" stretch (grain, streaks, blades, strata) uses h -- NOT p.y --
    // so textures stay aligned with gravity all the way around a planet.
    float2 pc = VsxPlaneCoord(p, up);
    float  h  = dot(p, up);

    if (cls == VSX_STONE)
    {
        // Strata: gently warped bands along the up axis.
        float warp  = VsxBroad(p * 0.09);
        float band  = sin(dot(p, up) * 0.85 + warp * 5.0);
        albedoMul  *= 0.92 + 0.08 * band;
        // Hairline cracks on the exposure plane.
        float f1, ch; VsxVoronoi2(pc * 0.55, f1, ch);
        float crack = smoothstep(0.10, 0.02, f1) * smoothstep(0.35, 0.65, ch);
        albedoMul  *= 1.0 - crack * 0.28;
        gradT      += float2(0.0, -crack * 0.9);
        // Granite speckle.
        albedoMul  *= 0.95 + 0.10 * VsxNoise3(p * 3.1);
    }
    else if (cls == VSX_SAND || cls == VSX_DUST)
    {
        // Wind ripples: parallel ridges, direction meandering over a few metres.
        float drift  = VsxBroad(p * 0.045);
        float ang    = drift * 6.2831;
        float2 dir   = float2(cos(ang), sin(ang));
        float qpar   = dot(pc, dir);          // coordinate along the ripple direction
        float warp   = VsxBroad(p * 0.35);
        float ripple = qpar * 2.4 + warp * 3.5;
        float rp     = sin(ripple);
        float crest = smoothstep(0.30, 0.95, rp);
        float trough = 1.0 - smoothstep(-0.95, -0.30, rp);
        float gain  = (cls == VSX_SAND) ? 1.0 : 0.7;
        albedoMul  *= 1.0 + (crest * 0.14 - trough * 0.10) * gain;
        gradT      += dir * cos(ripple) * crest * 0.55 * gain;
        smoothAdd  += crest * 0.06;
        if (cls == VSX_DUST)
        {
            // Martian-style broad colour drift between rust tones.
            float driftCol = VsxBroad(p * 0.012);
            albedoMul *= lerp(float3(1, 1, 1), float3(1.10, 0.94, 0.80), driftCol * 0.45);
        }
        else albedoMul *= 0.97 + 0.06 * VsxNoise3(p * 5.0);   // loose grain
    }
    else if (cls == VSX_CLAY)
    {
        float m = VsxNoise3(p * 1.4);
        albedoMul *= 0.93 + 0.13 * m;
        float f1, ch; VsxVoronoi2(pc * 0.40, f1, ch);
        float dry = smoothstep(0.06, 0.015, f1) * smoothstep(0.55, 0.8, ch);
        albedoMul *= 1.0 - dry * 0.20;                    // rare drying cracks
        gradT    += float2(0.0, -dry * 0.5);
    }
    else if (cls == VSX_ICE || cls == VSX_CRYSTAL)
    {
        // Crystalline facets: one stable tilt per cell + hard gloss.
        float f1, ch; VsxVoronoi2(pc * 0.30, f1, ch);
        float edge = smoothstep(0.35, 0.02, f1);          // bright cell borders
        float ang  = ch * 6.2831;
        float2 tilt= float2(cos(ang), sin(ang)) * (0.35 + 0.4 * ch);
        gradT     += tilt;
        albedoMul *= 0.96 + 0.06 * VsxBroad(p * 0.8);
        if (cls == VSX_CRYSTAL)
        {
            smoothAdd  += 0.40;
            albedoMul  *= 1.0 + edge * 0.35;
            emissionAdd += baseRgb * (0.03 + edge * 0.05); // faint inner glow
        }
        else
        {
            smoothAdd  += 0.32;
            albedoMul  *= 1.0 + edge * 0.18;
        }
    }
    else if (cls == VSX_ORE)
    {
        // Ore body: darker granular matrix + metallic flecks that glint.
        float m = VsxNoise3(p * 2.2);
        albedoMul *= 0.80 + 0.28 * m;
        float fl = step(0.72, VsxVnoise3(p * 6.5));
        if (fl > 0.0)
        {
            // Brighten toward the ore's own saturated colour -> glint reads as the ore.
            float3 tint = normalize(max(baseRgb, float3(0.04, 0.04, 0.04))) * (0.45 + 0.55 * m);
            albedoMul   = lerp(albedoMul, albedoMul * (0.55 + tint * 1.55), fl * 0.85);
            smoothAdd  += fl * 0.45;
            metalAdd   += fl * 0.65;
            if (matId == 17.0) emissionAdd += float3(0.02, 0.09, 0.02) * fl;   // uranium breathes green
        }
    }
    else if (cls == VSX_COAL)
    {
        // Matte, streaky, organic -- never glints.
        float streak = VsxNoise3(float3(pc.x * 2.8, h * 0.22, pc.y * 2.8));
        albedoMul *= 0.82 + 0.34 * streak;
        albedoMul *= 0.97 + 0.06 * VsxBroad(p * 4.0);
        smoothAdd -= 0.04;
    }
    else if (cls == VSX_OILROCK)
    {
        // Soaked rock: dark mottle + gravity-stretched wet gloss streaks.
        float m = VsxNoise3(p * 1.8);
        albedoMul *= 0.85 + 0.24 * m;
        float wet = smoothstep(0.45, 0.75, VsxVnoise3(float3(pc.x * 1.6, h * 0.15, pc.y * 1.6)));
        smoothAdd += wet * 0.30;
        albedoMul *= 1.0 - wet * 0.10;
    }
    else if (cls == VSX_WOOD)
    {
        // Vertical grain stretched along the trunk axis (local up).
        float g = VsxNoise3(float3(pc.x * 2.6, h * 0.16, pc.y * 2.6));
        albedoMul *= 0.84 + 0.32 * g;
        float knot = smoothstep(0.74, 0.95, VsxBroad(p * 1.1));
        albedoMul *= 1.0 - knot * 0.16;
    }
    else if (cls == VSX_GRASS)
    {
        // Organic clumps + dry patches + fine blade streaks very near.
        float clump = VsxNoise3(p * 1.15);
        albedoMul *= 0.90 + 0.20 * clump;
        float dry = VsxBroad(p * 0.014);
        albedoMul = lerp(albedoMul, albedoMul * float3(1.22, 1.06, 0.72), dry * 0.35);
        float blade = VsxVnoise3(float3(pc.x * 3.2, h * 0.10, pc.y * 3.2)) * fade;
        albedoMul *= 0.96 + 0.10 * blade;
        gradT += float2(blade - 0.5, 0.0) * 0.25;

        // Steep slopes shed their turf: blend toward exposed soil (desaturated,
        // darkened, fixed earthy anchor) so cliffs read as dirt/rock.
        float slope = 1.0 - saturate(dot(normalWS, up));
        float soil  = smoothstep(0.30, 0.62, slope);
        float lum   = VsxDesat(baseRgb) * 0.72;
        float3 soilTone = lerp(float3(lum, lum, lum), float3(0.34, 0.26, 0.18), float3(0.55, 0.55, 0.55));
        // Recolour: replace hue with soil tone where steep (baseRgb-relative so
        // custom grass colours still set the overall mood).
        float3 soilMul = soilTone / max(max(baseRgb.r, max(baseRgb.g, baseRgb.b)), 0.06);
        soilMul = saturate(soilMul);
        albedoMul = lerp(albedoMul, soilMul * (0.92 + 0.16 * clump), soil);
        // Exposed soil gets a light granular grain.
        albedoMul *= lerp(1.0, 0.96 + 0.09 * VsxVnoise3(p * 4.5), soil);
    }
    else if (cls == VSX_ASH)
    {
        float layer = VsxNoise3(float3(pc.x * 1.3, h * 0.30, pc.y * 1.3));
        albedoMul *= 0.92 + 0.15 * layer;
        albedoMul *= 0.98 + 0.05 * VsxBroad(p * 3.2);
    }
    else if (cls == VSX_BOG)
    {
        float m = VsxNoise3(p * 1.0);
        albedoMul *= 0.88 + 0.22 * m;
        albedoMul = lerp(albedoMul, albedoMul * float3(0.92, 1.05, 0.88), (m - 0.5) * 0.6);
        float pool = smoothstep(0.55, 0.8, VsxVnoise3(p * 0.9));
        smoothAdd += pool * 0.35;                          // wet gloss pools
        albedoMul *= 1.0 - pool * 0.12;
    }
    else if (cls == VSX_BASALT)
    {
        // Columnar fractures: anisotropic voronoi on the exposure plane.
        float f1, ch; VsxVoronoi2(pc * float2(0.30, 0.85), f1, ch);
        float crack = smoothstep(0.14, 0.02, f1);
        albedoMul  *= 1.0 - crack * 0.30;
        gradT      += float2(0.0, -crack * 0.8);
        albedoMul  *= 0.94 + 0.10 * VsxNoise3(p * 2.6);
        float vein = smoothstep(0.80, 0.97, VsxBroad(p * 0.7));
        emissionAdd += float3(0.055, 0.016, 0.004) * vein; // faint residual heat
    }
    else // VSX_GENERIC -- restrained grain so unknown materials still sit quietly.
    {
        float g = VsxNoise3(p * 3.0);
        albedoMul *= 0.93 + 0.14 * g;
    }

    // Fold user strength through everything we produced.
    float s = strength * fade;
    albedoMul   = lerp(float3(1, 1, 1), albedoMul, saturate(s));
    gradT      *= s;
    smoothAdd  *= s;
    metalAdd   *= s;
    emissionAdd *= s;
}

// Relief: apply a tangent-frame gradient to the world normal (shared by both
// shaders so ripples / cracks / facets catch the sun identically).
float3 VsxApplyRelief(float3 normalWS, float2 gradT, float3 up)
{
    float3 t1, t2; VsxTangentFrame(up, t1, t2);
    return normalize(normalWS + t1 * gradT.x + t2 * gradT.y);
}

#endif // VOXEL_SURFACE_TEXTURES_INCLUDED
