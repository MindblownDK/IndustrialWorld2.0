// Assets/Scripts/VoxelEngine/Core/IVoxelWorld.cs
//
// Shared interface for anything that owns a voxel terrain the player can mine/build on.
// Implemented by both the flat VoxelWorld AND the spherical SphereWorld. Systems that need to
// read/write terrain (pumps, drills, farms, map, weather, audio) target ActiveWorld.Current,
// so they work on whichever world the player is currently in.
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Voxel-world contract: read/write voxels, look up chunks, remesh, plus shared assets.
    /// Both the flat VoxelWorld and the spherical SphereWorld implement this.
    /// </summary>
    public interface IVoxelWorld
    {
        Voxel GetVoxelWorld(Vector3Int worldVoxel);
        void SetVoxelWorld(Vector3Int worldVoxel, Voxel v, bool remesh = true);
        bool TryGetChunk(Vector3Int coord, out Chunk chunk);
        Vector3Int WorldToVoxel(Vector3 worldPos);
        Vector3Int WorldToChunk(Vector3 worldPos);
        /// <summary>Force a chunk to rebuild its mesh (used by editing tools after voxel writes).</summary>
        void ScheduleMeshJob(Chunk chunk);
        /// <summary>Complete any in-flight gen job for this chunk (fluid sim safety).</summary>
        void CompleteGenJobForChunk(Chunk chunk);
        /// <summary>Complete any in-flight mesh job for this chunk (fluid sim safety).</summary>
        void CompleteMeshJobForChunk(Chunk chunk);

        /// <summary>Material registry (colors, hardness, mining tier) for this world.</summary>
        MaterialRegistry MaterialRegistry { get; }
        /// <summary>Transform the world streams around (usually the player).</summary>
        Transform Viewer { get; }
        /// <summary>Voxel-space sea level (water fills below this).</summary>
        int SeaLevel { get; }
        /// <summary>World generation seed (for deterministic biome/climate sampling).</summary>
        int Seed { get; }
    }

    /// <summary>
    /// Static pointer to the world the player is currently interacting with. Set by the scene
    /// bootstrap (flat world sets VoxelWorld.Instance; CosmosBootstrap sets the SphereWorld).
    /// Systems read Current to target whichever world the player is in.
    /// </summary>
    public static class ActiveWorld
    {
        private static IVoxelWorld _current;

        /// <summary>The active voxel world (flat or spherical). Null until a bootstrap sets it.</summary>
        public static IVoxelWorld Current
        {
            get => _current ?? (_current = VoxelWorld.Instance as IVoxelWorld);
            set => _current = value;
        }
    }
}
