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
    ///
    /// V6: Fluid materials (WaterVoxel, WaterLiquid, CrudeOil) are treated as empty
    ///     for density mask + iso-surface computation so the terrain mesh never generates
    ///     faces inside fluid volumes. This eliminates the "double water layer" artifact
    ///     where terrain mesh faces visible through semi-transparent water looked like
    ///     a second water surface. The water mesh (WaterMeshBuilder) is the sole renderer
    ///     of all fluid surfaces.
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

        // Fluid material IDs — these are treated as EMPTY for terrain mesh generation
        private const byte WaterVoxelMat = 5;  // MaterialId.WaterVoxel  (solid form)
        private const byte WaterLiquidMat = 6;  // MaterialId.WaterLiquid (sim form)
        private const byte OilMat         = 18; // MaterialId.CrudeOil

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

                // Read actual densities
                int d000 = voxels[i000].density;
                int d100 = voxels[i100].density;
                int d010 = voxels[i010].density;
                int d110 = voxels[i110].density;
                int d001 = voxels[i001].density;
                int d101 = voxels[i101].density;
                int d011 = voxels[i011].density;
                int d111 = voxels[i111].density;

                // Compute virtual densities: fluid materials are treated as empty (density < 0).
                // This prevents the terrain mesh from generating faces inside fluid volumes,
                // eliminating the "double water layer" where terrain faces were visible through
                // semi-transparent water and appeared as a second water surface.
                int vd000 = IsFluidMat(voxels[i000].material) ? -1 : d000;
                int vd100 = IsFluidMat(voxels[i100].material) ? -1 : d100;
                int vd010 = IsFluidMat(voxels[i010].material) ? -1 : d010;
                int vd110 = IsFluidMat(voxels[i110].material) ? -1 : d110;
                int vd001 = IsFluidMat(voxels[i001].material) ? -1 : d001;
                int vd101 = IsFluidMat(voxels[i101].material) ? -1 : d101;
                int vd011 = IsFluidMat(voxels[i011].material) ? -1 : d011;
                int vd111 = IsFluidMat(voxels[i111].material) ? -1 : d111;

                // Mask uses VIRTUAL densities — fluid materials are empty
                int mask = 0;
                if (vd000 > 0) mask |= 1;
                if (vd100 > 0) mask |= 2;
                if (vd010 > 0) mask |= 4;
                if (vd110 > 0) mask |= 8;
                if (vd001 > 0) mask |= 16;
                if (vd101 > 0) mask |= 32;
                if (vd011 > 0) mask |= 64;
                if (vd111 > 0) mask |= 128;

                if (mask == 0 || mask == 255) continue;

                float3 sum = float3.zero;
                int n = 0;

                // Edge interpolation uses VIRTUAL densities so the iso-surface
                // is correctly placed at the terrain-fluid boundary
                AddEdge(vd000, vd100, 0,0,0, 1,0,0, ref sum, ref n);
                AddEdge(vd010, vd110, 0,1,0, 1,0,0, ref sum, ref n);
                AddEdge(vd001, vd101, 0,0,1, 1,0,0, ref sum, ref n);
                AddEdge(vd011, vd111, 0,1,1, 1,0,0, ref sum, ref n);

                AddEdge(vd000, vd010, 0,0,0, 0,1,0, ref sum, ref n);
                AddEdge(vd100, vd110, 1,0,0, 0,1,0, ref sum, ref n);
                AddEdge(vd001, vd011, 0,0,1, 0,1,0, ref sum, ref n);
                AddEdge(vd101, vd111, 1,0,1, 0,1,0, ref sum, ref n);

                AddEdge(vd000, vd001, 0,0,0, 0,0,1, ref sum, ref n);
                AddEdge(vd100, vd101, 1,0,0, 0,0,1, ref sum, ref n);
                AddEdge(vd010, vd011, 0,1,0, 0,0,1, ref sum, ref n);
                AddEdge(vd110, vd111, 1,1,0, 0,0,1, ref sum, ref n);

                if (n == 0) continue;

                // Dominant material vote — skip ALL fluid materials so the terrain
                // mesh never gets painted with water/oil colors. Only non-fluid
                // solid materials (Stone, Sand, etc.) participate in the vote.
                for (int m = 0; m < 256; m++) matVotes[m] = 0;
                if ((mask & 1)   != 0) { byte mt = voxels[i000].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                if ((mask & 2)   != 0) { byte mt = voxels[i100].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                if ((mask & 4)   != 0) { byte mt = voxels[i010].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                if ((mask & 8)   != 0) { byte mt = voxels[i110].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                if ((mask & 16)  != 0) { byte mt = voxels[i001].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                if ((mask & 32)  != 0) { byte mt = voxels[i101].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                if ((mask & 64)  != 0) { byte mt = voxels[i011].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                if ((mask & 128) != 0) { byte mt = voxels[i111].material; if (!IsFluidMat(mt)) matVotes[mt]++; }
                int dominantMat = 0, dominantCount = 0;
                for (int m = 1; m < 256; m++)
                    if (matVotes[m] > dominantCount) { dominantCount = matVotes[m]; dominantMat = m; }

                float3 local = sum / n + new float3(cx - 1, cy - 1, cz - 1);
                vertexScratch[vertexCount] = local;

                // Gradient uses VIRTUAL densities for correct normals at terrain-fluid boundaries
                float gx = (vd100 + vd110 + vd101 + vd111) - (vd000 + vd010 + vd001 + vd011);
                float gy = (vd010 + vd110 + vd011 + vd111) - (vd000 + vd100 + vd001 + vd101);
                float gz = (vd001 + vd101 + vd011 + vd111) - (vd000 + vd100 + vd010 + vd110);
                float3 nrm = -math.normalizesafe(new float3(gx, gy, gz), new float3(0, 1, 0));
                normalScratch[vertexCount] = nrm;
                colorScratch[vertexCount]  = ApplyTerrainShading(materialColors[dominantMat], local, nrm, cx, cy, cz);

                cellVertexIndex[CellId(cx, cy, cz)] = vertexCount;
                bbMin = math.min(bbMin, local);
                bbMax = math.max(bbMax, local);
                vertexCount++;
            }

            // ---- Pass 2: stitch quads on sign-changing edges ----
            // Uses VIRTUAL density sign (fluid = empty) for consistent face generation
            for (int z = 1; z < S + 1; z++)
            for (int y = 1; y < S + 1; y++)
            for (int x = 1; x < S + 1; x++)
            {
                int idx = Idx(x, y, z);
                bool s0 = IsTerrainSolid(voxels[idx]);

                // +X
                int idxX = Idx(x + 1, y, z);
                bool sX = IsTerrainSolid(voxels[idxX]);
                if (s0 != sX)
                {
                    int v00 = cellVertexIndex[CellId(x, y - 1, z - 1)];
                    int v10 = cellVertexIndex[CellId(x, y,     z - 1)];
                    int v01 = cellVertexIndex[CellId(x, y - 1, z    )];
                    int v11 = cellVertexIndex[CellId(x, y,     z    )];
                    if (v00 >= 0 && v10 >= 0 && v01 >= 0 && v11 >= 0)
                        EmitQuad(v00, v10, v11, v01, s0, ref indexCount);
                }
                // +Y
                int idxY = Idx(x, y + 1, z);
                bool sY = IsTerrainSolid(voxels[idxY]);
                if (s0 != sY)
                {
                    int v00 = cellVertexIndex[CellId(x - 1, y, z - 1)];
                    int v10 = cellVertexIndex[CellId(x,     y, z - 1)];
                    int v01 = cellVertexIndex[CellId(x - 1, y, z    )];
                    int v11 = cellVertexIndex[CellId(x,     y, z    )];
                    if (v00 >= 0 && v10 >= 0 && v01 >= 0 && v11 >= 0)
                        EmitQuad(v00, v01, v11, v10, s0, ref indexCount);
                }
                // +Z
                int idxZ = Idx(x, y, z + 1);
                bool sZ = IsTerrainSolid(voxels[idxZ]);
                if (s0 != sZ)
                {
                    int v00 = cellVertexIndex[CellId(x - 1, y - 1, z)];
                    int v10 = cellVertexIndex[CellId(x,     y - 1, z)];
                    int v01 = cellVertexIndex[CellId(x - 1, y,     z)];
                    int v11 = cellVertexIndex[CellId(x,     y,     z)];
                    if (v00 >= 0 && v10 >= 0 && v01 >= 0 && v11 >= 0)
                        EmitQuad(v00, v10, v11, v01, s0, ref indexCount);
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

        /// <summary>
        /// Returns true if the voxel is solid terrain (not a fluid material).
        /// Fluid materials (WaterVoxel, WaterLiquid, CrudeOil) are treated as empty
        /// so the terrain mesh never generates faces inside fluid volumes.
        /// </summary>
        private static bool IsTerrainSolid(Voxel v)
        {
            return v.density > 0 && !IsFluidMat(v.material);
        }

        /// <summary>
        /// Returns true if the material is a fluid that should be excluded from
        /// terrain mesh generation. WaterVoxel(5) is included because old saves
        /// may contain solid water blocks that would otherwise generate blue
        /// terrain faces indistinguishable from the water surface.
        /// </summary>
        private static bool IsFluidMat(byte mat)
        {
            return mat == WaterVoxelMat || mat == WaterLiquidMat || mat == OilMat;
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

        /// <summary>
        /// Phase 4: terrain shading. Adds three realism layers to the flat material color:
        ///   1. VERTEX AO — darken vertices in concave pockets (surrounded by solid) for depth.
        ///   2. NOISE VARIATION — subtle per-vertex color jitter so surfaces aren't flat solid.
        ///   3. SLOPE DARKENING — steep faces slightly darker than flat (enhances relief).
        /// All done with cheap math — no texture sampling, works on any pipeline.
        /// </summary>
        private Color32 ApplyTerrainShading(Color32 baseColor, float3 localPos, float3 normal, int cx, int cy, int cz)
        {
            float r = baseColor.r / 255f;
            float g = baseColor.g / 255f;
            float b = baseColor.b / 255f;

            // 1. Noise variation: hash the vertex position for a stable per-vertex jitter.
            // Adds organic speckle so terrain doesn't look like a flat painted surface.
            float h = noise.snoise(localPos * 0.7f) * 0.5f + 0.5f;  // 0..1
            float jitter = (h - 0.5f) * 0.12f;  // ±6% brightness
            r = math.saturate(r + jitter);
            g = math.saturate(g + jitter);
            b = math.saturate(b + jitter);

            // 2. Slope darkening: flat terrain (normal pointing up) is brighter; steep = darker.
            // This makes hills and mountains read as 3D even without textures.
            float upDot = math.abs(normal.y);  // 1 = flat, 0 = vertical
            float slopeShade = math.lerp(0.75f, 1.0f, math.saturate(upDot));
            r *= slopeShade;
            g *= slopeShade;
            b *= slopeShade;

            // 3. Vertex AO: estimate occlusion by sampling neighbor cell densities.
            // Vertices surrounded by more solid voxels = more occluded = darker.
            int solidCount = 0;
            int checked_ = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                int nx = cx + dx, ny = cy + dy, nz = cz + dz;
                if (nx < 0 || ny < 0 || nz < 0 || nx > VoxelConstants.CHUNK_SIZE || ny > VoxelConstants.CHUNK_SIZE || nz > VoxelConstants.CHUNK_SIZE) continue;
                var nv = voxels[Idx(nx, ny, nz)];
                if (IsTerrainSolid(nv)) solidCount++;
                checked_++;
            }
            float ao = checked_ > 0 ? math.lerp(1.0f, 0.65f, (float)solidCount / checked_) : 1f;
            r *= ao;
            g *= ao;
            b *= ao;

            return new Color32(
                (byte)math.clamp(r * 255f, 0, 255),
                (byte)math.clamp(g * 255f, 0, 255),
                (byte)math.clamp(b * 255f, 0, 255),
                baseColor.a);
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
