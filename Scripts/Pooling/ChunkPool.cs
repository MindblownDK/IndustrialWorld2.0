// Assets/Scripts/VoxelEngine/Pooling/ChunkPool.cs
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.Pooling
{
    /// <summary>
    /// Reuses chunk GameObjects, meshes, and NativeArrays.
    /// Recycled chunks avoid GC and dramatically reduce hitches when streaming.
    /// </summary>
    public class ChunkPool
    {
        private readonly Stack<Chunk> _pool = new Stack<Chunk>(64);
        private readonly Transform    _parent;
        private readonly Material     _material;

        public ChunkPool(Transform parent, Material material)
        {
            _parent   = parent;
            _material = material;
        }

        public Chunk Rent(Vector3Int coord)
        {
            Chunk c;
            if (_pool.Count > 0)
            {
                c = _pool.Pop();
                c.go.SetActive(true);
            }
            else
            {
                c = CreateNew();
            }

            c.coord = coord;
            // A pooled Chunk object can still be referenced by an old streaming queue entry.
            // Advance its lease before it is exposed again so stale work is rejected safely.
            c.streamEpoch = c.streamEpoch == int.MaxValue ? 1 : c.streamEpoch + 1;
            c.go.name = $"Chunk_{coord.x}_{coord.y}_{coord.z}";
            c.go.transform.position = c.WorldOrigin;
            c.isGenerated = false;
            c.isScattered = false;
            c.isDirty = true;
            c.isModified = false;
            // Reset water state on reuse — the new chunk may have no water at all,
            // but we keep the allocation around so PlaceWater doesn't realloc.
            // Fully destroy FluidGrid and water mesh from previous chunk use.
            if (c.fluidGrid != null) { c.fluidGrid.Dispose(); c.fluidGrid = null; }
            c.oilGrid = null;
            if (c.waterMeshGO != null)
            {
                Object.Destroy(c.waterMeshGO);
                c.waterMeshGO = null; c.waterMeshFilter = null; c.waterMeshRenderer = null;
            }
            if (c.waterMesh != null) { Object.Destroy(c.waterMesh); c.waterMesh = null; }

            // Allocate / reset native voxel storage
            if (!c.isAllocated)
            {
                c.voxels = new NativeArray<Voxel>(VoxelConstants.VOXELS_PER_CHUNK_P,
                    Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                c.isAllocated = true;
            }
            return c;
        }

        public void Return(Chunk c)
        {
            if (c == null) return;
            c.go.SetActive(false);
            c.meshFilter.sharedMesh = null;
            c.meshCollider.sharedMesh = null;

            // Destroy scatter children (trees/rocks from previous use of this pooled chunk).
            var scatter = c.go.transform.Find("__scatter");
            if (scatter != null) Object.Destroy(scatter.gameObject);

            // Destroy water mesh child.
            if (c.waterMeshGO != null) { Object.Destroy(c.waterMeshGO); c.waterMeshGO = null; }
            c.waterMeshFilter = null;
            c.waterMeshRenderer = null;
            if (c.waterMesh != null) { Object.Destroy(c.waterMesh); c.waterMesh = null; }

            // Dispose fluid grid.
            if (c.fluidGrid != null) { c.fluidGrid.Dispose(); c.fluidGrid = null; }
            c.oilGrid = null;

            // Reset state flags.
            c.isGenerated = false;
            c.isScattered = false;
            c.isModified  = false;
            c.isDirty     = false;

            _pool.Push(c);
        }

        public void DisposeAll(IEnumerable<Chunk> active)
        {
            foreach (var c in active) DisposeChunk(c);
            while (_pool.Count > 0) DisposeChunk(_pool.Pop());
        }

        private void DisposeChunk(Chunk c)
        {
            if (c.isAllocated && c.voxels.IsCreated) c.voxels.Dispose();
            if (c.fluidGrid != null) { c.fluidGrid.Dispose(); c.fluidGrid = null; }
            if (c.waterMesh != null) { UnityEngine.Object.Destroy(c.waterMesh); c.waterMesh = null; }
            if (c.mesh != null) Object.Destroy(c.mesh);
            if (c.go   != null) Object.Destroy(c.go);
        }

        private Chunk CreateNew()
        {
            var go = new GameObject("Chunk_pooled");
            go.transform.SetParent(_parent, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mc = go.AddComponent<MeshCollider>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            var mesh = new Mesh { name = "ChunkMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();

            return new Chunk
            {
                go = go,
                meshFilter = mf,
                meshRenderer = mr,
                meshCollider = mc,
                mesh = mesh
            };
        }
    }
}
