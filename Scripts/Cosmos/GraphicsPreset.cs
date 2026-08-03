// Assets/Scripts/VoxelEngine/Cosmos/GraphicsPreset.cs
//
// Low / Mid / High / Ultra graphics presets for the open-world game.
//
// Per the design brief: Mid-PC baseline, scalable down to Low and up to Ultra. This is the
// single source of truth that every visual system reads from — view distance, grass density,
// LOD resolution, post-FX — so the player picks ONE preset and everything cascades.
using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.Cosmos
{
    public enum GraphicsTier { Low, Mid, High, Ultra }

    /// <summary>
    /// Centralized graphics budget. Read by GpuGrassRenderer, PlanetLodImpostor, SphereWorld,
    /// and the post-FX bootstrap. Maps the Unity QualitySettings level to our 4 tiers.
    /// </summary>
    public static class GraphicsPreset
    {
        // ── Quality level → tier mapping ──
        public static GraphicsTier Current
        {
            get
            {
                int q = QualitySettings.GetQualityLevel();
                // Unity's default has 6 levels (0..5). Map to our 4 tiers.
                if (q <= 1) return GraphicsTier.Low;
                if (q <= 3) return GraphicsTier.Mid;
                if (q == 4) return GraphicsTier.High;
                return GraphicsTier.Ultra;
            }
        }

        // ── Per-tier budgets ──

        /// <summary>Sphere streaming view distance (chunk radius) per tier.</summary>
        public static int ViewDistance => Current switch
        {
            // This is the editable collider/chunk bubble, not visual draw distance: the
            // continuous planet LOD covers everything beyond it. Keep it bounded so Ultra does
            // not allocate thousands of 3D voxel chunks at spawn.
            GraphicsTier.Low  => 3,
            GraphicsTier.Mid  => 4,
            GraphicsTier.High => 5,
            _                 => 6,
        };

        /// <summary>Grass density multiplier (0 = off) per tier.</summary>
        public static float GrassDensityMul => Current switch
        {
            GraphicsTier.Low  => 0f,
            GraphicsTier.Mid  => 0.6f,
            GraphicsTier.High => 1.2f,
            _                 => 1.8f,
        };

        /// <summary>LOD icosphere resolution per tier (higher = smoother from space).</summary>
        public static int LodResolution => Current switch
        {
            // The full surface is always present; local voxel chunks own close detail. Keep
            // the runtime proxy within a stable frame budget rather than rebuilding a 40k-vertex
            // shell/ocean pair on every high-quality spawn.
            GraphicsTier.Low  => 642,
            GraphicsTier.Mid  => 2562,
            GraphicsTier.High => 10242,
            _                 => 10242,
        };

        /// <summary>Waterfall scan range (metres) per tier.</summary>
        public static float WaterfallRange => Current switch
        {
            GraphicsTier.Low  => 0f,    // off on Low (perf)
            GraphicsTier.Mid  => 60f,
            GraphicsTier.High => 100f,
            _                 => 140f,
        };

        /// <summary>Max chunks to generate per frame per tier.</summary>
        public static int JobsPerFrame => Current switch
        {
            GraphicsTier.Low  => 2,
            GraphicsTier.Mid  => 4,
            GraphicsTier.High => 6,
            _                 => 8,
        };

        /// <summary>Whether atmospheric fog/post-FX are enabled per tier.</summary>
        public static bool PostFxEnabled => Current != GraphicsTier.Low;

        /// <summary>Human-readable name for the UI.</summary>
        public static string TierName => Current.ToString();
    }
}
