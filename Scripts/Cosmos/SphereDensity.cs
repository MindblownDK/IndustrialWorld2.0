// Assets/Scripts/VoxelEngine/Cosmos/SphereDensity.cs
//
// The radial density field for a spherical body.
//
// Everything is sampled in DIRECTION space (a point on the unit sphere) plus a RADIAL
// distance, so the same generator is correct at any planet radius. Continents, climate
// (temperature/humidity), biome blending and ore veins are all 3D on the sphere — they wrap
// seamlessly across the six cube faces with no seams (snoise is continuous on the sphere
// because we feed it the true normalised direction).
//
// Phase 3: LATITUDE-BASED CLIMATE (equator hot, poles cold → Earth-like biome distribution +
// polar ice caps), SLOPE-BASED ROCK (steep faces = stone, not grass-on-cliffs), and SNOW LINE
// (high altitude + cold = snow). These three make the planet read as Earth.
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Generation;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    [BurstCompile]
    public static class SphereDensity
    {
        // Climate noise scales in DIRECTION space (so they're radius-independent).
        private const float TempScale   = 1.7f;
        private const float HumidScale  = 2.1f;
        private const float TempOffset  = 47.3f;
        private const float HumidOffset = 91.7f;

        // Latitude blend: how strongly the equator/pole gradient dominates over noise.
        // 0 = pure noise (random climate), 1 = pure latitude (perfect bands). 0.65 = mostly
        // latitude with regional noise variation — looks like Earth.
        private const float LatitudeBlend = 0.55f;

        /// <summary>
        /// Sample the planet's climate field at a surface direction.
        /// Returns temperature &amp; humidity in 0..1.
        ///
        /// Phase 3: temperature now blends a LATITUDE factor (equator = hot, poles = cold) with
        /// regional noise, so biomes form Earth-like climate zones: tropical near the equator,
        /// temperate mid-latitudes, tundra/ice near the poles. This is what makes the planet
        /// read as Earth rather than a random noise ball.
        /// </summary>
        public static float2 SampleClimate(int seed, in float3 dir)
        {
            float3 p = dir * 1f;
            float tNoise = noise.snoise(p * TempScale   + (seed * 0.073f + TempOffset))  * 0.5f + 0.5f;
            float hNoise = noise.snoise(p * HumidScale  + (seed * 0.149f + HumidOffset)) * 0.5f + 0.5f;

            // Latitude: |dir.y| = 0 at equator (hot), 1 at poles (cold).
            float lat = math.abs(dir.y);                          // 0 = equator, 1 = pole
            float tLat = math.saturate(1f - lat * 1.25f);         // equator ~1, poles ~0

            // Humidity latitude pattern (like Earth):
            //   equator (lat 0)   = wet (tropical rainforest)
            //   ~30° (lat 0.5)    = dry (desert belts)
            //   ~50° (lat 0.75)   = moderate-wet (temperate forest)
            //   poles (lat 1)     = dry (tundra)
            float hLat = math.cos(lat * 3.0f) * 0.3f + 0.55f;     // oscillating: wet-dry-wet-dry
            hLat = math.saturate(hLat);

            // Blend latitude climate with regional noise.
            float t = math.lerp(tNoise, tLat, LatitudeBlend);
            float h = math.lerp(hNoise, hLat, LatitudeBlend * 0.7f);

            return new float2(t, h);
        }

        /// <summary>
        /// A planet's "land mask" — 0 = deep ocean basin, 1 = continental interior. Drives the
        /// continent/ocean split that makes the planet read as Earth (large landmasses, big
        /// oceans, with continental shelves between).
        /// </summary>
        public static float LandMask(int seed, in float3 dir, float continentScaleDir)
        {
            float3 p = dir + seed * 0.0031f;
            float coarse = noise.snoise(p * continentScaleDir)             * 0.5f + 0.5f;
            float shape  = noise.snoise(p * continentScaleDir * 2.3f + 13f) * 0.5f + 0.5f;
            float land   = coarse * 0.72f + shape * 0.28f;
            return math.saturate(math.smoothstep(0.34f, 0.62f, land));
        }

        /// <summary>
        /// Estimate the local terrain slope (0 = flat, 1 = vertical cliff) by comparing the
        /// surface radius at `dir` vs a small angular offset. Used to apply ROCK on steep faces
        /// (no grass growing on cliffs) — a key realism detail.
        /// </summary>
        public static float EstimateSlope(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in float3 dir)
        {
            // Sample the surface height at dir and at two perpendicular offsets.
            EvaluateColumn(prm, biomes, dir, out float h0, out _);

            // Build two perpendicular tangent directions on the sphere.
            float3 refVec = math.abs(dir.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0);
            float3 tangent1 = math.normalizesafe(math.cross(dir, refVec), new float3(1, 0, 0));
            float3 tangent2 = math.normalizesafe(math.cross(dir, tangent1), new float3(0, 0, 1));

            // Small angular offset (~1° in direction space).
            float3 d1 = math.normalizesafe(dir + tangent1 * 0.02f, dir);
            float3 d2 = math.normalizesafe(dir + tangent2 * 0.02f, dir);

            EvaluateColumn(prm, biomes, d1, out float h1, out _);
            EvaluateColumn(prm, biomes, d2, out float h2, out _);

            // Height difference → slope estimate. Large diff = steep.
            float dh = (math.abs(h1 - h0) + math.abs(h2 - h0)) * 0.5f;
            return math.saturate(dh / 8f);  // 8m height change over ~1° ≈ steep
        }

        /// <summary>
        /// Score a biome against the local climate (euclidean distance in climate space,
        /// biased by priority). Mirrors the flat-world BiomePicker so authored biomes port over.
        /// </summary>
        public static float Score(in BiomeData b, float2 climate)
        {
            float tCenter = (b.tempRange.x + b.tempRange.y) * 0.5f;
            float tHalf   = math.max(0.001f, (b.tempRange.y - b.tempRange.x) * 0.5f);
            float tDist   = (climate.x - tCenter) / tHalf;

            float hCenter = (b.humidRange.x + b.humidRange.y) * 0.5f;
            float hHalf   = math.max(0.001f, (b.humidRange.y - b.humidRange.x) * 0.5f);
            float hDist   = (climate.y - hCenter) / hHalf;

            float d = math.sqrt(tDist * tDist + hDist * hDist);
            return (1f - d) + b.priority * 0.05f;
        }

        /// <summary>
        /// Evaluate the surface radius (metres from core) and dominant biome index for a
        /// direction. Blends all biome heights via softmax so adjacent biomes never produce a
        /// cliff at the seam. Ocean biomes are pulled below sea level and damped by the land mask.
        /// </summary>
        public static void EvaluateColumn(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in float3 dir,
            out float surfaceRadius,
            out int  biomeIndex)
        {
            float2 climate = SampleClimate(prm.seed, dir);
            float landMask = LandMask(prm.seed, dir, prm.continentScaleDir);

            // Dominant biome.
            float bestScore = -1e9f;
            biomeIndex = 0;
            for (int i = 0; i < biomes.Length; i++)
            {
                float s = Score(biomes[i], climate);
                if (s > bestScore) { bestScore = s; biomeIndex = i; }
            }

            // Softmax-blend all biome heights.
            float weightSum = 0f, heightSum = 0f;
            for (int i = 0; i < biomes.Length; i++)
            {
                var b = biomes[i];
                float s = Score(b, climate);
                float w = math.exp((s - bestScore) * 2.5f);
                if (w < 0.0005f) continue;

                float detail;
                if (b.ridgedness > 0.01f)
                {
                    float n = 1f - math.abs(noise.snoise(dir * b.heightFrequency + i * 5.1f));
                    detail = math.lerp(noise.snoise(dir * b.heightFrequency + i * 5.1f), n * n, b.ridgedness);
                }
                else
                {
                    detail = noise.snoise(dir * b.heightFrequency + i * 5.1f);
                }

                float biomeHeight = b.heightOffset + detail * b.heightAmplitude * prm.mountainScale;

                if (b.isOceanic == 1)
                    biomeHeight = -math.lerp(40f, 5f, landMask) + detail * (b.heightAmplitude * 0.5f);

                heightSum += biomeHeight * w;
                weightSum += w;
            }

            float blended = weightSum > 0f ? heightSum / weightSum : 0f;

            float coastPull  = math.smoothstep(0f, 0.45f, landMask);
            float oceanFloor = -25f;
            float terrainOffset = math.lerp(oceanFloor, blended, coastPull);

            surfaceRadius = prm.MeanSurfaceRadius + terrainOffset;
        }

        /// <summary>
        /// Full per-voxel evaluation. Returns the voxel (density byte + material + water level)
        /// for a body-relative cartesian position.
        /// </summary>
        public static Voxel EvaluateVoxel(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in NativeArray<OreLayer> ores,
            in float3 worldPos)
        {
            float radius = math.length(worldPos);
            float3 dir   = math.normalizesafe(worldPos, new float3(1f, 0f, 0f));

            EvaluateColumn(prm, biomes, dir, out float surfaceRadius, out int biomeI);
            var biome = biomes[biomeI];

            float density = surfaceRadius - radius;

            float coreRadius = prm.radiusWorld * 0.55f;
            if (radius <= coreRadius)
                return new Voxel(127, (byte)MaterialId.Bedrock, 0);

            int depth = (int)math.round(surfaceRadius - radius);

            // Caves.
            if (depth > 6 && radius > coreRadius + 6f && surfaceRadius > prm.seaRadius - 1f)
            {
                float cave = noise.snoise(worldPos * 0.045f) * 0.5f + 0.5f;
                cave += noise.snoise(worldPos * 0.09f + 50f) * 0.25f;
                if (cave > 0.68f)
                    density -= (cave - 0.68f) * 90f;
            }

            if (density > 0f)
            {
                byte material = (byte)MaterialId.Stone;

                // ── Surface material selection (Phase 3: slope + snow + beach) ──
                float altitudeAboveSea = surfaceRadius - prm.seaRadius;  // metres above sea level
                float2 climate = SampleClimate(prm.seed, dir);
                bool atSurface = depth < biome.surfaceDepth;

                // Beach band: ONLY right at the waterline (±1m, top 2 voxels). The old band was
                // ±2.5m × 4 deep which covered the entire surface when terrain sat near sea level.
                if (biome.allowBeach == 1 &&
                    radius >= prm.seaRadius - 1f && radius <= prm.seaRadius + 1f && depth < 2)
                {
                    material = (byte)MaterialId.Sand;
                }
                else if (depth < biome.surfaceDepth)
                {
                    material = biome.surfaceMat;
                }
                else if (depth < biome.surfaceDepth + biome.subsurfaceDepth)
                {
                    material = biome.subsurfaceMat;
                }

                // ── SNOW LINE: high altitude + cold climate = snow/ice surface ──
                // Realistic snow caps on mountains and polar regions.
                if (atSurface && altitudeAboveSea > 35f && climate.x < 0.25f)
                {
                    material = (byte)MaterialId.Ice;
                }
                // Polar ice: very cold regions near poles get ice even at low altitude.
                else if (atSurface && climate.x < 0.1f)
                {
                    material = (byte)MaterialId.Ice;
                }

                // ── SLOPE-BASED ROCK: steep terrain = stone, no grass on cliffs ──
                // Only apply on genuinely steep slopes (large height difference over a small
                // angular step). Threshold tuned high (8m) so gentle hills keep their grass —
                // only cliffs and mountain faces become rock.
                if (depth < biome.surfaceDepth + biome.subsurfaceDepth && altitudeAboveSea > 5f)
                {
                    // Cheap slope estimate: sample height noise at an offset.
                    float3 refVec = math.abs(dir.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0);
                    float3 tangent = math.normalizesafe(math.cross(dir, refVec), new float3(1, 0, 0));
                    float3 dirOff = math.normalizesafe(dir + tangent * 0.03f, dir);
                    EvaluateColumn(prm, biomes, dirOff, out float surfaceOff, out _);
                    float heightDiff = math.abs(surfaceOff - surfaceRadius);
                    if (heightDiff > 8f)   // only genuine cliffs/mountain faces → rock
                        material = (byte)MaterialId.Stone;
                }

                // ── Ore veins ──
                if (depth >= biome.surfaceDepth + 1)
                {
                    for (int i = 0; i < ores.Length; i++)
                    {
                        var ore = ores[i];
                        if (depth < ore.minDepth || depth > ore.maxDepth) continue;
                        float rich = VeinNoise.DepositRichness(
                            worldPos, 1f / ore.scale, ore.threshold, 1f, (uint)(prm.seed + i * 31));
                        if (rich > 0.5f)
                        {
                            material = (byte)ore.material;
                            break;
                        }
                    }
                }

                sbyte densityByte = (sbyte)math.clamp(density, 1f, 127f);
                return new Voxel(densityByte, material, 0);
            }
            else
            {
                if (radius <= prm.seaRadius)
                {
                    // Water: render as SOLID in the terrain mesh (density=5) so it follows the
                    // sphere curvature. The flat-world WaterMeshBuilder creates horizontal planes
                    // which break on a sphere (floating discs, vertical walls). Making water a
                    // solid colored voxel lets SurfaceNetsJob mesh it correctly on ANY surface.
                    return new Voxel(5, (byte)MaterialId.WaterLiquid, 0);
                }
                return new Voxel((sbyte)math.clamp(density, -127f, -1f), (byte)MaterialId.Air, 0);
            }
        }
    }
}
