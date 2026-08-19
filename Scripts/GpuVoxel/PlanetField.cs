// Assets/Scripts/VoxelEngine/GpuVoxel/PlanetField.cs
//
// THE planetary density field — single source of truth for terrain shape (9.0.0).
//
// This C# implementation is the exact mirror of Resources/PlanetFieldGpu.compute.
// The GPU evaluates it for the whole-planet quadtree surface; the CPU version is
// consumed by SphereDensity (the 1 m gameplay bubble, scatter, waterfalls, sky
// proxies, ocean cut-outs …) so EVERY system agrees on the same surface with no
// gaps and no LOD mismatch. If you change a constant here you MUST change the
// same constant in PlanetFieldGpu.compute — the two files are kept in lockstep.
//
// Design (gap-free by construction):
//   The field is a function of DIRECTION on the unit sphere plus RADIUS only.
//   surfaceRadius(dir) is continuous everywhere (domain-warped fBm continents,
//   ridged-multifractal mountains masked to continental interiors, mid-frequency
//   hills, smooth shelf→basin ocean floors), so the iso-surface
//   density(p) = surfaceRadius(dir) − |p| − caveCarve(p) is a closed 2-manifold:
//   it CANNOT have holes, cracks or stacked-slab artifacts at any resolution.
using Unity.Burst;
using Unity.Mathematics;

namespace VoxelEngine.GpuVoxel
{
    [BurstCompile]
    public static class PlanetField
    {
        // ── Field constants (mirrored in PlanetFieldGpu.compute) ─────────────
        public const float WarpFrequency   = 1.31f;
        public const float WarpStrength    = 0.18f;
        public const float HillAmplitude   = 9f;
        public const float MountainBase    = 62f;
        public const float LandPlateau     = 2f;
        public const float ShelfDepth      = 5f;

        /// <summary>Deterministic per-seed/channel offset — identical to the hash in
        /// SphereDensity and in the compute shader.</summary>
        public static float SeedOffset(int seed, int channel)
        {
            uint h = (uint)(seed ^ (channel * 0x9E3779B9));
            h = (h ^ (h >> 16)) * 0x85EBCA6Bu;
            h = (h ^ (h >> 13)) * 0xC2B2AE35u;
            h = h ^ (h >> 16);
            return ((h & 0xFFFF) / 65535f - 0.5f) * 1000f;
        }

        /// <summary>Standard fBm over simplex noise, normalised to ≈[-1, 1].</summary>
        public static float Fbm(int seed, in float3 p, float frequency, int octaves, int seedChannel)
        {
            float amp = 1f, freq = frequency, sum = 0f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum  += noise.snoise(p * freq + SeedOffset(seed, seedChannel + o)) * amp;
                norm += amp;
                amp  *= 0.5f;
                freq *= 2.02f;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal (sharp mountain crests), normalised to ≈[0, 1].</summary>
        public static float Ridged(int seed, in float3 p, float frequency, int octaves, int seedChannel)
        {
            float amp = 1f, freq = frequency, sum = 0f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                float n = 1f - math.abs(noise.snoise(p * freq + SeedOffset(seed, seedChannel + o)));
                n = n * n;
                sum  += n * amp;
                norm += amp;
                amp  *= 0.55f;
                freq *= 2.13f;
            }
            return sum / norm;
        }

        /// <summary>Ocean basin depth (metres below sea) for a planet radius.</summary>
        public static float BasinDepth(float radiusWorld)
            => math.min(44f, 18f + radiusWorld * 0.0022f);

        /// <summary>Analytic upper bound of terrain elevation above the mean surface.</summary>
        public static float MaxElevation(float mountainScale)
            => LandPlateau + HillAmplitude + MountainBase * math.max(0.05f, mountainScale) + 3f;

        /// <summary>Analytic lower bound of terrain elevation below the mean surface.</summary>
        public static float MinElevation(float radiusWorld)
            => -(BasinDepth(radiusWorld) + ShelfDepth + HillAmplitude + 3f);

        /// <summary>
        /// Continent land mask (0 = open ocean, 1 = continental interior) — the exact
        /// mask <see cref="SurfaceRadius"/> composes with. Used to gate generated sea
        /// water: only genuine ocean regions flood, never land dips behind a beach.
        /// </summary>
        public static float LandMask01(int seed, in float3 dir, float continentScaleDir)
        {
            float wx = noise.snoise(dir * WarpFrequency + SeedOffset(seed, 40));
            float wy = noise.snoise(dir * WarpFrequency + SeedOffset(seed, 41));
            float wz = noise.snoise(dir * WarpFrequency + SeedOffset(seed, 42));
            float3 wd = math.normalizesafe(dir + WarpStrength * new float3(wx, wy, wz), dir);
            float contFreq = math.max(0.25f, continentScaleDir);
            float cont = Fbm(seed, wd, contFreq, 4, 50);
            return math.smoothstep(-0.06f, 0.14f, cont);
        }

        /// <summary>
        /// Terrain surface radius (metres from core) for a unit direction.
        /// Continuous across the whole sphere — the heart of the field.
        /// </summary>
        public static float SurfaceRadius(
            int seed, in float3 dir,
            float radiusWorld, float baseHeight, float seaRadius,
            float continentScaleDir, float mountainScale)
        {
            // 1 ── gentle domain warp: organic coastlines, no grid alignment.
            float wx = noise.snoise(dir * WarpFrequency + SeedOffset(seed, 40));
            float wy = noise.snoise(dir * WarpFrequency + SeedOffset(seed, 41));
            float wz = noise.snoise(dir * WarpFrequency + SeedOffset(seed, 42));
            float3 wd = math.normalizesafe(dir + WarpStrength * new float3(wx, wy, wz), dir);

            // 2 ── continents: low-frequency fBm land mask with a soft shoreline.
            float contFreq = math.max(0.25f, continentScaleDir);
            float cont = Fbm(seed, wd, contFreq, 4, 50);
            float land = math.smoothstep(-0.06f, 0.14f, cont);

            // 3 ── ocean floor: continental shelf easing into a deep basin.
            float basin = BasinDepth(radiusWorld);
            float shelf = math.smoothstep(-0.32f, -0.06f, cont);
            float ocean = -math.lerp(basin, ShelfDepth, shelf);

            // 4 ── rolling hills (mid frequency, radius-agnostic feature size ≈ 260 m).
            float hillFreq = math.max(3f, radiusWorld / 260f);
            float hills = Fbm(seed, wd, hillFreq, 4, 60) * HillAmplitude;

            // 5 ── mountains: ridged crests, masked to continental uplift zones.
            float mountFreq = math.max(1.6f, radiusWorld / 950f);
            float uplift = math.smoothstep(0.15f, 0.75f, Fbm(seed, wd, contFreq * 2.6f, 3, 70));
            float mountains = Ridged(seed, wd, mountFreq, 5, 80) * uplift
                              * (MountainBase * math.max(0.05f, mountainScale));

            // 6 ── compose: shore blend between the ocean floor and the land stack.
            float elevation = math.lerp(ocean, LandPlateau + hills + mountains, land);
            return radiusWorld + baseHeight + elevation;
        }

        /// <summary>
        /// Cave carve amount (metres subtracted from density). Sealed beneath a
        /// protective crust and disabled under the sea so ocean floors stay solid.
        /// Identical maths on the GPU.
        /// </summary>
        public static float CaveCarve(
            in float3 worldPos, float surfaceRadius, float radius,
            float seaRadius, float protectedCrust)
        {
            float depth = surfaceRadius - radius;
            if (depth < protectedCrust + 2f) return 0f;
            if (surfaceRadius < seaRadius + 2f) return 0f;

            float gate = math.smoothstep(protectedCrust + 2f, protectedCrust + 8f, depth);
            float n = noise.snoise(worldPos * 0.045f) * 0.5f + 0.5f;
            n += noise.snoise(worldPos * 0.09f + 50f) * 0.25f;
            return math.max(0f, n - 0.68f) * 90f * gate;
        }

        /// <summary>
        /// Signed density (metres, &gt;0 = solid) at a body-relative position —
        /// the exact function the compute shader evaluates per corner.
        /// </summary>
        public static float Density(
            int seed, in float3 worldPos,
            float radiusWorld, float baseHeight, float seaRadius,
            float continentScaleDir, float mountainScale, float protectedCrust)
        {
            float r = math.length(worldPos);
            float3 dir = math.normalizesafe(worldPos, new float3(1f, 0f, 0f));
            float surf = SurfaceRadius(seed, dir, radiusWorld, baseHeight, seaRadius,
                                       continentScaleDir, mountainScale);
            return surf - r - CaveCarve(worldPos, surf, r, seaRadius, protectedCrust);
        }
    }
}
