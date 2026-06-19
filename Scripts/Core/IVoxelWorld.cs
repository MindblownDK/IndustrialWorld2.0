// Assets/Scripts/VoxelEngine/Core/IVoxelWorld.cs
//
// Shared interface for anything that owns a voxel terrain the player can mine/build on.
// Implemented by both the flat VoxelWorld AND the spherical SphereWorld. The mining tools
// (PickaxeTool, PlayerInteractionTool) target ActiveWorld.Current, so they automatically mine
// whichever world the player is currently standing in.
using UnityEngine;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Minimal voxel-world contract: read/write individual voxels + look up chunks.
    /// Both the flat VoxelWorld and the spherical SphereWorld implement this.
    /// </summary>
    public interface IVoxelWorld
    {
        Voxel GetVoxelWorld(Vector3Int worldVoxel);
        void SetVoxelWorld(Vector3Int worldVoxel, Voxel v, bool remesh = true);
        bool TryGetChunk(Vector3Int coord, out Chunk chunk);
        Vector3Int WorldToVoxel(Vector3 worldPos);
        Vector3Int WorldToChunk(Vector3 worldPos);
    }

    /// <summary>
    /// Static pointer to the world the player is currently interacting with. Set by the scene
    /// bootstrap (flat world sets VoxelWorld.Instance; CosmosBootstrap sets the SphereWorld).
    /// Mining/building tools read Current to target whichever world the player is in.
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
