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
    /// Centralized graphics budget. Read by GpuGrassRenderer, GpuPlanetEngine, SphereWorld,
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
            // This is the editable collider/chunk bubble around the player (the continuous
            // planet LOD covers everything beyond it). Raised so REAL voxel terrain reaches
            // further around the player (the whole planet stays solid via the LOD shell).
            GraphicsTier.Low  => 5,
            GraphicsTier.Mid  => 6,
            GraphicsTier.High => 7,
            _                 => 8,
        };

        /// <summary>Grass density multiplier per tier. 9.18.0: Low no longer disables grass
        /// outright - a sparse but visible field (0 = off caused "bald planet" reports).</summary>
        public static float GrassDensityMul => Current switch
        {
            GraphicsTier.Low  => 0.45f,
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

        /// <summary>
        /// Vertex budget for the ACTIVE body's full-planet surface (the body the player is
        /// on / approaching). Built progressively, so these can be large — the whole planet
        /// is one continuous sampled surface from ground to orbit.
        /// </summary>
        public static int ActiveBodyLodResolution => Current switch
        {
            GraphicsTier.Low  => 10242,
            GraphicsTier.Mid  => 40962,
            GraphicsTier.High => 163842,
            _                 => 163842,
        };

        /// <summary>
        /// Voxel size (metres) of the MID whole-planet real-voxel LOD level per tier.
        /// 32 m = ~768 chunks on an 8 km planet (High/Ultra); 64 m = ~192 (Mid);
        /// 128 m = ~48 (Low). Legacy note — the GPU quadtree engine now derives its own budgets; kept for the
        /// whole-planet chunk count bounded.
        /// </summary>
        public static float PlanetMidLodVoxelSize => Current switch
        {
            GraphicsTier.Low  => 128f,
            GraphicsTier.Mid  => 64f,
            GraphicsTier.High => 32f,
            _                 => 32f,
        };

        /// <summary>
        /// Voxel size (metres) of the FAR whole-planet real-voxel LOD level per tier
        /// (visible from space during the whole interplanetary crossing).
        /// </summary>
        public static float PlanetFarLodVoxelSize => Current switch
        {
            GraphicsTier.Low  => 512f,
            GraphicsTier.Mid  => 256f,
            GraphicsTier.High => 128f,
            _                 => 128f,
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
            GraphicsTier.Low  => 4,
            GraphicsTier.Mid  => 8,
            GraphicsTier.High => 10,
            _                 => 12,
        };

        /// <summary>Whether atmospheric fog/post-FX are enabled per tier.</summary>
        public static bool PostFxEnabled => Current != GraphicsTier.Low;

        /// <summary>Human-readable name for the UI.</summary>
        public static string TierName => Current.ToString();
    }
}
