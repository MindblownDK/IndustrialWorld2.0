// Assets/Scripts/VoxelEngine/Cosmos/OreDeposit.cs
using System;
using UnityEngine;
using VoxelEngine.Generation;   // OreLayer (existing struct consumed by ChunkGenJob)
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// A single ore/mineral deposit definition, authored per celestial body.
    /// Converts losslessly to the existing <see cref="OreLayer"/> POD so the current
    /// Burst <c>ChunkGenJob</c> can consume it without modification.
    /// </summary>
    [Serializable]
    public struct OreDeposit
    {
        [Tooltip("Voxel material this deposit produces when mined.")]
        public MaterialId material;

        [Tooltip("Which crust stratum this deposit belongs to (drives default depth bands).")]
        public OreTier tier;

        [Range(0.01f, 0.5f)]
        [Tooltip("Noise frequency — higher = smaller, denser veins.")]
        public float scale;

        [Range(0f, 1f)]
        [Tooltip("Richness cut-off. Lower = more abundant.")]
        public float threshold;

        [Range(0, 250)]
        [Tooltip("Shallowest depth (voxels below surface) at which this spawns.")]
        public int minDepth;

        [Range(0, 250)]
        [Tooltip("Deepest depth (voxels below surface) at which this spawns.")]
        public int maxDepth;

        [Range(0f, 2f)]
        [Tooltip("Multiplier on vein size/rarity. Used by the Phase 1 generator to shape pockets.")]
        public float abundance;

        /// <summary>Lossless conversion to the existing job-friendly POD struct.</summary>
        public OreLayer ToOreLayer() => new OreLayer
        {
            material   = material,
            scale      = scale,
            threshold  = threshold,
            minDepth   = minDepth,
            maxDepth   = maxDepth,
        };
    }
}
