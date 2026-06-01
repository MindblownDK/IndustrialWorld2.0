// Assets/Scripts/VoxelEngine/Core/Chunk.cs
using Unity.Collections;
using UnityEngine;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Runtime data + GameObject for a single voxel chunk.
    /// Voxel data is stored in a *padded* NativeArray of size CHUNK_SIZE_P^3 so the
    /// meshing job can sample one-voxel neighbours without locking adjacent chunks.
    /// </summary>
    public class Chunk
    {
        public Vector3Int   coord;          // chunk-space coordinate
        public GameObject   go;             // owning GameObject (with MeshFilter, MeshRenderer, MeshCollider)
        public MeshFilter   meshFilter;
        public MeshRenderer meshRenderer;
        public MeshCollider meshCollider;

        public NativeArray<Voxel> voxels;   // padded grid (CHUNK_SIZE_P^3)
        public bool isAllocated;
        public bool isDirty;                // needs remesh
        public bool isGenerated;            // density data filled
        public bool isScattered;            // tree/rock scatter has been placed
        public bool isModified;             // player has edited this chunk -> needs persistence
        public VoxelEngine.Fluids.FluidGrid fluidGrid;
        public VoxelEngine.Fluids.OilGrid oilGrid;      // lazy-allocated when oil is placed
        public UnityEngine.GameObject waterMeshGO;       // child GO holding the water-surface mesh
        public UnityEngine.MeshFilter  waterMeshFilter;
        public UnityEngine.MeshRenderer waterMeshRenderer;
        public UnityEngine.Mesh        waterMesh;
        public float genCompletedTime;       // Time.time when isGenerated became true
        public Mesh mesh;                   // assigned & owned by this chunk

        public Vector3 WorldOrigin =>
            new Vector3(coord.x, coord.y, coord.z) *
            (VoxelConstants.CHUNK_SIZE * VoxelConstants.VOXEL_SIZE);

        /// <summary>Index into the padded voxel array. Pass non-padded local coords (0..CHUNK_SIZE-1).</summary>
        public static int LocalToPaddedIndex(int x, int y, int z)
        {
            const int S = VoxelConstants.CHUNK_SIZE_P;
            return (x + 1) + (y + 1) * S + (z + 1) * S * S;
        }

        public Voxel GetVoxelLocal(int x, int y, int z) => voxels[LocalToPaddedIndex(x, y, z)];
        public void  SetVoxelLocal(int x, int y, int z, Voxel v) { voxels[LocalToPaddedIndex(x, y, z)] = v; isDirty = true; }
    }
}
