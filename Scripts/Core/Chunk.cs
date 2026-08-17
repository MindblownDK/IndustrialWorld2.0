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

        /// <summary>
        /// True when this chunk intersects the terrain surface and therefore MUST have a
        /// mesh to count as visually covered (set by SphereWorld.FinalizeGen). Air/interior
        /// chunks are false — they are "covered" with no mesh at all. Consumed by the
        /// single-surface handshake's meshed-bubble scan (9.1.x).
        /// </summary>
        public bool needsSurfaceMesh;

        // Incremented every time this pooled Chunk is rented. SphereWorld records this value
        // with queued work so an old queue entry can never generate/mesh a later coordinate
        // after fast movement has recycled the same Chunk object.
        public int streamEpoch;

        public VoxelEngine.Fluids.FluidGrid fluidGrid;
        public VoxelEngine.Fluids.OilGrid oilGrid;      // lazy-allocated when oil is placed
        public UnityEngine.GameObject waterMeshGO;       // child GO holding the water-surface mesh
        public UnityEngine.MeshFilter  waterMeshFilter;
        public UnityEngine.MeshRenderer waterMeshRenderer;
        public UnityEngine.Mesh        waterMesh;
        public float genCompletedTime;       // Time.time when isGenerated became true
        public Mesh mesh;                   // assigned & owned by this chunk

        /// <summary>
        /// Surface flow velocity field — one Vector2 per horizontal column (CHUNK_SIZE²).
        /// Computed each fluid tick from pressure gradients; consumed by the water mesh
        /// builder and encoded into UV2 so the shader can flow-map normals and foam.
        /// Null when the chunk has never contained fluid.
        /// </summary>
        public Vector2[] flowField;

        /// <summary>Read the flow vector for column (x, z). Returns zero if no flow data.</summary>
        public Vector2 GetFlow(int x, int z)
        {
            if (flowField == null) return Vector2.zero;
            const int S = VoxelConstants.CHUNK_SIZE;
            if (x < 0 || x >= S || z < 0 || z >= S) return Vector2.zero;
            return flowField[x + z * S];
        }

        /// <summary>Write the flow vector for column (x, z). Allocates the array lazily.</summary>
        public void SetFlow(int x, int z, Vector2 v)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            if (flowField == null) flowField = new Vector2[S * S];
            if (x < 0 || x >= S || z < 0 || z >= S) return;
            flowField[x + z * S] = v;
        }

        /// <summary>Ensure the flow field array exists, zeroed.</summary>
        public void EnsureFlowField()
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            if (flowField == null || flowField.Length != S * S)
                flowField = new Vector2[S * S];
        }

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
