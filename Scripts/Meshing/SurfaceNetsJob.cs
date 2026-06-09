// Assets/Scripts/VoxelEngine/Meshing/SurfaceNetsJob.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;

namespace VoxelEngine.Meshing
{
    /// <summary>
    /// Naive Surface Nets — produces smooth iso-surface meshes from signed-density voxels.
    /// Output is written directly into a Mesh.MeshData snapshot for zero-copy upload via
    /// Mesh.ApplyAndDisposeWritableMeshData (the modern Unity 6 path).
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public unsafe struct SurfaceNetsJob : IJob
    {
        // Input voxel grid (padded, CHUNK_SIZE_P^3).
        [ReadOnly] public NativeArray<Voxel> voxels;

        // Output writable mesh data (allocated by caller via Mesh.AllocateWritableMeshData).
        public Mesh.MeshData meshData;

        // Side product
        public NativeArray<Bounds> bounds;
        public NativeArray<int>    counts; // [0]=vertexCount, [1]=indexCount

        // Pre-allocated scratch buffers
        public NativeArray<float3>  vertexScratch;
        public NativeArray<float3>  normalScratch;
        public NativeArray<Color32> colorScratch;
        public NativeArray<int>     indexScratch;
        public NativeArray<int>     cellVertexIndex; // cellId -> vertex index, -1 if none

        // Material colour LUT (256 entries)
        [ReadOnly] public NativeArray<Color32> materialColors;

        // Pre-allocated vertex attribute descriptors (Burst can't `new` managed arrays).
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> vertexAttributes;

        public void Execute()
        {
            const int S  = VoxelConstants.CHUNK_SIZE;

            for (int i = 0; i < cellVertexIndex.Length; i++) cellVertexIndex[i] = -1;

            int vertexCount = 0;
            int indexCount  = 0;

            float3 bbMin = new float3(float.MaxValue);
            float3 bbMax = new float3(float.MinValue);

            // Local material vote buffer (stack allocated, no GC)
            int* matVotes = stackalloc int[256];

            // ---- Pass 1: place one vertex per cell whose 8 corners straddle iso ----
            for (int cz = 0; cz < S + 1; cz++)
            for (int cy = 0; cy < S + 1; cy++)
            for (int cx = 0; cx < S + 1; cx++)
            {
                int i000 = Idx(cx,     cy,     cz);
                int i100 = Idx(cx + 1, cy,     cz);
                int i010 = Idx(cx,     cy + 1, cz);
                int i110 = Idx(cx + 1, cy + 1, cz);
                int i001 = Idx(cx,     cy,     cz + 1);
                int i101 = Idx(cx + 1, cy,     cz + 1);
                int i011 = Idx(cx,     cy + 1, cz + 1);
                int i111 = Idx(cx + 1, cy + 1, cz + 1);

                int d000 = voxels[i000].density;
                int d100 = voxels[i100].density;
                int d010 = voxels[i010].density;
                int d110 = voxels[i110].density;
                int d001 = voxels[i001].density;
                int d101 = voxels[i101].density;
                int d011 = voxels[i011].density;
                int d111 = voxels[i111].density;

                int mask = 0;
                if (d000 > 0) mask |= 1;
                if (d100 > 0) mask |= 2;
                if (d010 > 0) mask |= 4;
                if (d110 > 0) mask |= 8;
                if (d001 > 0) mask |= 16;
                if (d101 > 0) mask |= 32;
                if (d011 > 0) mask |= 64;
                if (d111 > 0) mask |= 128;

                if (mask == 0 || mask == 255) continue;

                float3 sum = float3.zero;
                int n = 0;

                AddEdge(d000, d100, 0,0,0, 1,0,0, ref sum, ref n);
                AddEdge(d010, d110, 0,1,0, 1,0,0, ref sum, ref n);
                AddEdge(d001, d101, 0,0,1, 1,0,0, ref sum, ref n);
                AddEdge(d011, d111, 0,1,1, 1,0,0, ref sum, ref n);

                AddEdge(d000, d010, 0,0,0, 0,1,0, ref sum, ref n);
                AddEdge(d100, d110, 1,0,0, 0,1,0, ref sum, ref n);
                AddEdge(d001, d011, 0,0,1, 0,1,0, ref sum, ref n);
                AddEdge(d101, d111, 1,0,1, 0,1,0, ref sum, ref n);

                AddEdge(d000, d001, 0,0,0, 0,0,1, ref sum, ref n);
                AddEdge(d100, d101, 1,0,0, 0,0,1, ref sum, ref n);
                AddEdge(d010, d011, 0,1,0, 0,0,1, ref sum, ref n);
                AddEdge(d110, d111, 1,1,0, 0,0,1, ref sum, ref n);

                if (n == 0) continue;

                // dominant material vote
                for (int m = 0; m < 256; m++) matVotes[m] = 0;
                if ((mask & 1)   != 0) matVotes[voxels[i000].material]++;
                if ((mask & 2)   != 0) matVotes[voxels[i100].material]++;
                if ((mask & 4)   != 0) matVotes[voxels[i010].material]++;
                if ((mask & 8)   != 0) matVotes[voxels[i110].material]++;
                if ((mask & 16)  != 0) matVotes[voxels[i001].material]++;
                if ((mask & 32)  != 0) matVotes[voxels[i101].material]++;
                if ((mask & 64)  != 0) matVotes[voxels[i011].material]++;
                if ((mask & 128) != 0) matVotes[voxels[i111].material]++;
                int dominantMat = 0, dominantCount = 0;
                for (int m = 1; m < 256; m++)
                    if (matVotes[m] > dominantCount) { dominantCount = matVotes[m]; dominantMat = m; }

                float3 local = sum / n + new float3(cx - 1, cy - 1, cz - 1);
                vertexScratch[vertexCount] = local;

                float gx = (d100 + d110 + d101 + d111) - (d000 + d010 + d001 + d011);
                float gy = (d010 + d110 + d011 + d111) - (d000 + d100 + d001 + d101);
                float gz = (d001 + d101 + d011 + d111) - (d000 + d100 + d010 + d110);
                float3 nrm = -math.normalizesafe(new float3(gx, gy, gz), new float3(0, 1, 0));
                normalScratch[vertexCount] = nrm;
                colorScratch[vertexCount]  = materialColors[dominantMat];

                cellVertexIndex[CellId(cx, cy, cz)] = vertexCount;
                bbMin = math.min(bbMin, local);
                bbMax = math.max(bbMax, local);
                vertexCount++;
            }

            // ---- Pass 2: stitch quads on sign-changing edges ----
            for (int z = 1; z < S + 1; z++)
            for (int y = 1; y < S + 1; y++)
            for (int x = 1; x < S + 1; x++)
            {
                int idx = Idx(x, y, z);
                int d0  = voxels[idx].density;

                // +X
                int dX = voxels[Idx(x + 1, y, z)].density;
                if ((d0 > 0) != (dX > 0))
                {
                    int v00 = cellVertexIndex[CellId(x, y - 1, z - 1)];
                    int v10 = cellVertexIndex[CellId(x, y,     z - 1)];
                    int v01 = cellVertexIndex[CellId(x, y - 1, z    )];
                    int v11 = cellVertexIndex[CellId(x, y,     z    )];
                    if (v00 >= 0 && v10 >= 0 && v01 >= 0 && v11 >= 0)
                        EmitQuad(v00, v10, v11, v01, d0 > 0, ref indexCount);
                }
                // +Y
                int dY = voxels[Idx(x, y + 1, z)].density;
                if ((d0 > 0) != (dY > 0))
                {
                    int v00 = cellVertexIndex[CellId(x - 1, y, z - 1)];
                    int v10 = cellVertexIndex[CellId(x,     y, z - 1)];
                    int v01 = cellVertexIndex[CellId(x - 1, y, z    )];
                    int v11 = cellVertexIndex[CellId(x,     y, z    )];
                    if (v00 >= 0 && v10 >= 0 && v01 >= 0 && v11 >= 0)
                        EmitQuad(v00, v01, v11, v10, d0 > 0, ref indexCount);
                }
                // +Z
                int dZ = voxels[Idx(x, y, z + 1)].density;
                if ((d0 > 0) != (dZ > 0))
                {
                    int v00 = cellVertexIndex[CellId(x - 1, y - 1, z)];
                    int v10 = cellVertexIndex[CellId(x,     y - 1, z)];
                    int v01 = cellVertexIndex[CellId(x - 1, y,     z)];
                    int v11 = cellVertexIndex[CellId(x,     y,     z)];
                    if (v00 >= 0 && v10 >= 0 && v01 >= 0 && v11 >= 0)
                        EmitQuad(v00, v10, v11, v01, d0 > 0, ref indexCount);
                }
            }

            counts[0] = vertexCount;
            counts[1] = indexCount;
            bounds[0] = vertexCount > 0
                ? new Bounds((Vector3)((bbMin + bbMax) * 0.5f), (Vector3)(bbMax - bbMin))
                : new Bounds(Vector3.zero, Vector3.zero);

            // ---- Write into Mesh.MeshData (Unity 6 fast path) ----
            meshData.SetVertexBufferParams(math.max(vertexCount, 1), vertexAttributes);

            var verts = meshData.GetVertexData<VertexLayout>();
            for (int i = 0; i < vertexCount; i++)
                verts[i] = new VertexLayout
                {
                    pos    = vertexScratch[i] * VoxelConstants.VOXEL_SIZE,
                    normal = normalScratch[i],
                    color  = colorScratch[i]
                };

            meshData.SetIndexBufferParams(math.max(indexCount, 1), IndexFormat.UInt32);
            var idxBuf = meshData.GetIndexData<int>();
            for (int i = 0; i < indexCount; i++) idxBuf[i] = indexScratch[i];

            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount)
            {
                bounds = bounds[0],
                vertexCount = vertexCount
            }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        private static int Idx(int x, int y, int z)
        {
            const int S = VoxelConstants.CHUNK_SIZE_P;
            return x + y * S + z * S * S;
        }

        private static int CellId(int x, int y, int z)
        {
            const int SP1 = VoxelConstants.CHUNK_SIZE + 1;
            return x + y * SP1 + z * SP1 * SP1;
        }

        private void AddEdge(int da, int db,
                             int ax, int ay, int az,
                             int dx, int dy, int dz,
                             ref float3 sum, ref int n)
        {
            if ((da > 0) == (db > 0)) return;
            float t = da / (float)(da - db);
            sum += new float3(ax + dx * t, ay + dy * t, az + dz * t);
            n++;
        }

        private void EmitQuad(int a, int b, int c, int d, bool flip, ref int indexCount)
        {
            if (flip)
            {
                indexScratch[indexCount++] = a;
                indexScratch[indexCount++] = b;
                indexScratch[indexCount++] = c;
                indexScratch[indexCount++] = a;
                indexScratch[indexCount++] = c;
                indexScratch[indexCount++] = d;
            }
            else
            {
                indexScratch[indexCount++] = a;
                indexScratch[indexCount++] = c;
                indexScratch[indexCount++] = b;
                indexScratch[indexCount++] = a;
                indexScratch[indexCount++] = d;
                indexScratch[indexCount++] = c;
            }
        }

        private struct VertexLayout
        {
            public float3  pos;
            public float3  normal;
            public Color32 color;
        }
    }
}
