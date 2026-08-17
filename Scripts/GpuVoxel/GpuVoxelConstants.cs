// Assets/Scripts/VoxelEngine/GpuVoxel/GpuVoxelConstants.cs
//
// Compile-time constants for the GPU-driven voxel engine (9.0.0).
//
// A quadtree NODE is a curved shell volume: 64×64 cells across the cube-face
// tile (u,v) and 64 cells across a radial band that hugs the terrain surface.
// The GPU evaluates the density field at the node's CORNER grid, padded by one
// ghost cell on every side so Dual Contouring can stitch neighbouring nodes of
// the same depth watertight (ghost vertices are bit-identical to the
// neighbour's own rim vertices because both sample the same global field).
namespace VoxelEngine.GpuVoxel
{
    public static class GpuVoxelConstants
    {
        /// <summary>Cells per node axis (tangential u, v and radial r).</summary>
        public const int NODE_CELLS = 64;

        /// <summary>Cells actually meshed per axis: footprint + 1 ghost cell each side.</summary>
        public const int MESH_CELLS = NODE_CELLS + 2;               // 66

        /// <summary>Corner samples per axis: MESH_CELLS + 1.</summary>
        public const int GRID_P = MESH_CELLS + 1;                   // 67

        /// <summary>Corner samples per node (GPU buffer length).</summary>
        public const int CORNERS_PER_NODE = GRID_P * GRID_P * GRID_P;   // 300,763

        /// <summary>Columns per node (surface-cache buffer length).</summary>
        public const int COLUMNS_PER_NODE = GRID_P * GRID_P;            // 4,489

        /// <summary>Hard cap for vertices / quads produced by one Dual Contour job.</summary>
        public const int MAX_VERTICES = 49152;
        public const int MAX_INDICES  = MAX_VERTICES * 6;

        /// <summary>Climate → biome material lookup resolution (temp × humidity).</summary>
        public const int CLIMATE_LUT_SIZE = 8;
        public const int CLIMATE_LUT_ENTRIES = CLIMATE_LUT_SIZE * CLIMATE_LUT_SIZE; // 64
    }
}
