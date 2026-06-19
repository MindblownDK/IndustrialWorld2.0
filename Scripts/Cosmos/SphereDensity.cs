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
// Phase 1: pure, Burst-compatible, unit-testable. The Phase-2 face streamer calls
// <see cref="EvaluateColumn"/> + <see cref="EvaluateVoxel"/> from its chunk-generation job.
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

        /// <summary>
        /// Sample the planet's climate field at a surface direction.
        /// Returns temperature &amp; humidity in 0..1 — the two axes biome selection keys on.
        /// </summary>
        public static float2 SampleClimate(int seed, in float3 dir)
        {
            float3 p = dir * 1f;
            float t = noise.snoise(p * TempScale   + (seed * 0.073f + TempOffset))  * 0.5f + 0.5f;
            float h = noise.snoise(p * HumidScale  + (seed * 0.149f + HumidOffset)) * 0.5f + 0.5f;
            return new float2(t, h);
        }

        /// <summary>
        /// A planet's "land mask" — 0 = deep ocean basin, 1 = continental interior. Drives the
        /// continent/ocean split that makes the planet read as Earth (large landmasses, big
        /// oceans, with continental shelves between).
        /// </summary>
        public static float LandMask(int seed, in float3 dir, float continentScaleDir)
        {
            // Two taps at different frequencies, combined so continents have lobes and bays.
            float3 p = dir + seed * 0.0031f;
            float coarse = noise.snoise(p * continentScaleDir)             * 0.5f + 0.5f;
            float shape  = noise.snoise(p * continentScaleDir * 2.3f + 13f) * 0.5f + 0.5f;
            float land   = coarse * 0.72f + shape * 0.28f;
            // Steepen the coast so shelves are narrow (realistic) but interiors are vast.
            return math.saturate(math.smoothstep(0.34f, 0.62f, land));
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
                    // Ridged: sharp peaks (mountains).
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

            // Coast transition: continental shelf slopes from ocean floor up to land.
            float coastPull  = math.smoothstep(0f, 0.45f, landMask);
            float oceanFloor = -25f; // metres below mean surface
            float terrainOffset = math.lerp(oceanFloor, blended, coastPull);

            surfaceRadius = prm.MeanSurfaceRadius + terrainOffset;
        }

        /// <summary>
        /// Full per-voxel evaluation. Returns the voxel (density byte + material + water level)
        /// for a body-relative cartesian position. This is what the Phase-2 chunk job calls per
        /// voxel; kept here so it's testable in isolation and shared with the authoring preview.
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

            // Solid if we are below the terrain surface radius.
            float density = surfaceRadius - radius;

            // Bedrock core: deepest shell is unbreakable.
            float coreRadius = prm.radiusWorld * 0.55f;
            if (radius <= coreRadius)
                return new Voxel(127, (byte)MaterialId.Bedrock, 0);

            // Caves: 3D noise in cartesian space, only in reasonably deep, non-oanic rock.
            int depth = (int)math.round(surfaceRadius - radius);
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

                // Beach band around sea level.
                if (biome.allowBeach == 1 &&
                    radius >= prm.seaRadius - 1.5f && radius <= prm.seaRadius + 2.5f && depth < 4)
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

                // Snow caps: cold high-altitude surfaces become ice (temperature-driven, set per body).
                // (Snow line tuned in Phase 3; here a placeholder altitude gate.)

                // Ore veins: clustered pockets via Worley. Deeper-than-surface only.
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
                // Below sea level and above terrain => water column.
                if (radius <= prm.seaRadius)
                    return new Voxel(-1, (byte)MaterialId.WaterLiquid, 255);

                return new Voxel((sbyte)math.clamp(density, -127f, -1f), (byte)MaterialId.Air, 0);
            }
        }
    }
}
