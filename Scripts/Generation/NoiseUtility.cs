// Assets/Scripts/VoxelEngine/Generation/NoiseUtility.cs
using Unity.Burst;
using Unity.Mathematics;

namespace VoxelEngine.Generation
{
    /// <summary>
    /// Burst-friendly noise helpers wrapping Unity.Mathematics.noise (Simplex).
    /// </summary>
    [BurstCompile]
    public static class NoiseUtility
    {
        /// <summary>Fractal Brownian Motion (octaved simplex).</summary>
        public static float FBM(float3 p, int octaves, float lacunarity, float gain)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum  += amp * noise.snoise(p * freq);
                norm += amp;
                amp  *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal — sharp ridge-like peaks.</summary>
        public static float Ridged(float3 p, int octaves, float lacunarity, float gain)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - math.abs(noise.snoise(p * freq));
                n *= n;
                sum  += amp * n;
                norm += amp;
                amp  *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>3D worley-ish density used to carve ore pockets.</summary>
        public static float OreField(float3 p, float scale, float threshold)
        {
            float n = noise.snoise(p * scale);
            return n > threshold ? (n - threshold) / (1f - threshold) : 0f;
        }
    }
}
