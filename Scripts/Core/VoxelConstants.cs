// Assets/Scripts/VoxelEngine/Core/VoxelConstants.cs
namespace VoxelEngine.Core
{
    /// <summary>
    /// Global compile-time constants for the voxel engine.
    /// Tweak CHUNK_SIZE carefully — it affects memory & meshing cost cubically.
    /// </summary>
    public static class VoxelConstants
    {
        // Chunk dimensions (cubic). 32 is a good balance between draw-call count and meshing cost.
        public const int CHUNK_SIZE   = 32;
        public const int CHUNK_SIZE_P = CHUNK_SIZE + 2; // padded with 1-voxel border for neighbour sampling
        public const int VOXELS_PER_CHUNK = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;
        public const int VOXELS_PER_CHUNK_P = CHUNK_SIZE_P * CHUNK_SIZE_P * CHUNK_SIZE_P;

        // World height in chunks (vertical). 8 chunks * 32 voxels = 256 voxel tall world.
        public const int WORLD_HEIGHT_CHUNKS = 8;
        public const int WORLD_HEIGHT_VOXELS = WORLD_HEIGHT_CHUNKS * CHUNK_SIZE;

        // Voxel size in world units (1 = 1m).
        public const float VOXEL_SIZE = 1.0f;

        // Density above this value = solid (signed-distance / iso-surface threshold).
        public const sbyte ISO_LEVEL = 0;

        // Render distance in chunks (horizontal radius around player).
        public const int DEFAULT_VIEW_DISTANCE = 6;
    }
}
