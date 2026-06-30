using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Shared contract for the active spherical voxel terrain runtime.
    /// Systems that mine, build, simulate fluids, or query terrain target this
    /// interface through ActiveWorld.Current.
    /// </summary>
    public interface IVoxelWorld
    {
        Voxel GetVoxelWorld(Vector3Int worldVoxel);
        void SetVoxelWorld(Vector3Int worldVoxel, Voxel v, bool remesh = true);
        bool TryGetChunk(Vector3Int coord, out Chunk chunk);
        Vector3Int WorldToVoxel(Vector3 worldPos);
        Vector3Int WorldToChunk(Vector3 worldPos);
        void ScheduleMeshJob(Chunk chunk);
        void CompleteGenJobForChunk(Chunk chunk);
        void CompleteMeshJobForChunk(Chunk chunk);

        MaterialRegistry MaterialRegistry { get; }
        Transform Viewer { get; }
        int SeaLevel { get; }
        int Seed { get; }
    }

    /// <summary>
    /// Static pointer to the spherical voxel world the player is currently interacting with.
    /// It is assigned by the spherical bootstrap during scene startup.
    /// </summary>
    public static class ActiveWorld
    {
        private static IVoxelWorld _current;

        public static IVoxelWorld Current
        {
            get => _current;
            set => _current = value;
        }
    }
}
