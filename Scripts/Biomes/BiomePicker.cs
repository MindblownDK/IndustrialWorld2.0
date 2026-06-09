// Assets/Scripts/VoxelEngine/Biomes/BiomePicker.cs
//
// Picks biomes for a (worldX, worldZ) column and computes a smoothly-blended surface
// height. Burst-friendly. Heavy multi-tap blur eliminates "biome cliff" artefacts.

using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Generation;

namespace VoxelEngine.Biomes
{
    [BurstCompile]
    public static class BiomePicker
    {
        // Lower SOFT = wider, smoother biome borders.
        private const float SOFT = 2.5f;
        // Wider blur kernel = smoother transitions but slightly slower per-voxel.
        private const int   BLUR_RADIUS = 3;     // 7x7 window (49 taps including 0,0)

        /// <summary>Sample temperature & humidity at world position. Range 0..1.</summary>
        public static float2 SampleClimate(int seed, int wx, int wz)
        {
            // Very low frequency = climate changes over hundreds of voxels.
            float t = noise.snoise(new float2(wx + seed * 0.137f, wz - seed * 0.241f) * 0.00035f);
            float h = noise.snoise(new float2(wx - seed * 0.319f, wz + seed * 0.451f) * 0.00040f);
            return new float2(t * 0.5f + 0.5f, h * 0.5f + 0.5f);
        }

        /// <summary>Round (euclidean) climate-window score with priority bias.</summary>
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
        /// Picks the dominant biome and returns a heavily-blurred surface height for the column.
        /// Uses a 7x7 gaussian-ish blur of single-sample heights so adjacent biomes never
        /// produce a >1-voxel step from one column to the next.
        /// </summary>
        public static void EvaluateColumn(
            int seed,
            int wx, int wz,
            int baseHeight, int seaLevel,
            float continentScale,
            in NativeArray<BiomeData> biomes,
            out int   bestBiomeIndex,
            out float blendedHeight)
        {
            // Get dominant biome from the centre column.
            SampleHeight(seed, wx, wz, baseHeight, seaLevel, continentScale, biomes, out bestBiomeIndex);

            // Gaussian-ish blur over a 7x7 footprint.
            float total = 0f;
            float weightSum = 0f;
            for (int dz = -BLUR_RADIUS; dz <= BLUR_RADIUS; dz++)
            for (int dx = -BLUR_RADIUS; dx <= BLUR_RADIUS; dx++)
            {
                float r2 = dx * dx + dz * dz;
                // sigma = BLUR_RADIUS / 2 => natural roll-off
                float w = math.exp(-r2 / (2f * (BLUR_RADIUS * 0.5f) * (BLUR_RADIUS * 0.5f)));
                float h = SampleHeight(seed, wx + dx, wz + dz, baseHeight, seaLevel, continentScale, biomes, out _);
                total     += h * w;
                weightSum += w;
            }
            blendedHeight = total / weightSum;
        }

        private static float SampleHeight(
            int seed, int wx, int wz,
            int baseHeight, int seaLevel,
            float continentScale,
            in NativeArray<BiomeData> biomes,
            out int bestBiomeIndex)
        {
            float2 climate = SampleClimate(seed, wx, wz);

            float continent = noise.snoise(new float2(wx + seed * 0.91f, wz - seed * 0.13f) * continentScale);
            float landMask  = math.saturate(continent * 0.5f + 0.5f);

            // Find the best biome.
            float bestScore = -1e9f;
            bestBiomeIndex = 0;
            for (int i = 0; i < biomes.Length; i++)
            {
                float s = Score(biomes[i], climate);
                if (s > bestScore) { bestScore = s; bestBiomeIndex = i; }
            }

            // Softmax-blend ALL biomes' heights.
            float weightSum = 0f;
            float heightSum = 0f;
            for (int i = 0; i < biomes.Length; i++)
            {
                var b = biomes[i];
                float s = Score(b, climate);
                float w = math.exp((s - bestScore) * SOFT);
                if (w < 0.0005f) continue;

                float detail;
                if (b.ridgedness > 0.01f)
                {
                    float n = 1f - math.abs(noise.snoise(new float2(wx, wz) * b.heightFrequency));
                    detail = math.lerp(noise.snoise(new float2(wx, wz) * b.heightFrequency), n * n, b.ridgedness);
                }
                else
                {
                    detail = noise.snoise(new float2(wx, wz) * b.heightFrequency);
                }

                float biomeHeight = baseHeight + b.heightOffset + detail * b.heightAmplitude;

                if (b.isOceanic == 1)
                {
                    float depth = math.lerp(40f, 5f, landMask);
                    biomeHeight = seaLevel - depth + detail * (b.heightAmplitude * 0.5f);
                }

                heightSum += biomeHeight * w;
                weightSum += w;
            }

            float h2 = weightSum > 0f ? heightSum / weightSum : baseHeight;

            float coastPull  = math.smoothstep(0f, 0.45f, landMask);
            float oceanFloor = seaLevel - 25f;
            return math.lerp(oceanFloor, h2, coastPull);
        }
    }
}
