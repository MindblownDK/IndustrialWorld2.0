// Assets/Scripts/VoxelEngine/GpuVoxel/GpuDualContourJob.cs
//
// DUAL CONTOURING mesher for GPU-evaluated density grids (9.0.0).
//
// Consumes the float density + material corner grid read back from
// PlanetFieldGpu.compute and produces one vertex per surface-crossing cell,
// placed by a QEF particle relaxation (Schmitz) over the cell's Hermite data —
// far fewer triangles than Marching Cubes, smooth hills AND sharp ridges.
//
// WATERTIGHT STITCHING: every node meshes one GHOST CELL beyond its footprint
// on all sides. Ghost vertices are computed from the same global field at the
// same resolution as the neighbour's own rim vertices, so they are identical —
// and each surface quad is emitted by exactly ONE owner node (its min corner
// must lie inside the footprint), so equal-depth neighbours join with no
// cracks, no overlaps and no z-fighting. Depth transitions are covered by
// radial skirts (double-sided ribbons dropped toward the core).
//
// Output goes straight into a Mesh.MeshData snapshot for zero-copy upload via
// Mesh.ApplyAndDisposeWritableMeshData — the modern Unity 6 path.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GpuVoxel
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = false)]
    public struct GpuDualContourJob : IJob
    {
        private const int GP = GpuVoxelConstants.GRID_P;         // 67 corners per axis
        private const int MC = GpuVoxelConstants.MESH_CELLS;     // 66 cells per axis
        private const int N  = GpuVoxelConstants.NODE_CELLS;     // 64 footprint cells

        // ── Inputs (read back from the GPU) ──
        [ReadOnly] public NativeArray<float> density;    // GP³ signed metres, >0 solid
        [ReadOnly] public NativeArray<uint>  material;   // GP³ material ids
        [ReadOnly] public NativeArray<Color32> materialColors;               // 256 LUT
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> vertexAttributes;

        // ── Node mapping ──
        public int    face;
        public float2 uvMin;      // face-space uv at footprint corner c = 0
        public float2 cellUv;     // uv per cell
        public float  rLo;        // radius at radial corner c = 0
        public float  dr;         // metres per radial cell
        public float3 anchor;     // vertices are emitted relative to this body-local point
        public float  skirtDepth; // radial skirt drop (m)

        // ── Outputs ──
        public Mesh.MeshData meshData;
        public NativeArray<Bounds> boundsOut;   // [0]
        public NativeArray<int>    counts;      // [0]=verts, [1]=indices

        // ── Scratch (persistent, owned by the streaming slot) ──
        public NativeArray<int>     cellVertexIndex;   // MC³
        public NativeArray<float3>  vertScratch;       // MAX_VERTICES
        public NativeArray<float3>  normScratch;
        public NativeArray<Color32> colScratch;
        public NativeArray<int>     idxScratch;        // MAX_INDICES

        private struct VertexLayout
        {
            public float3  pos;
            public float3  normal;
            public Color32 color;
        }

        private static int CornerIdx(int gx, int gy, int gz) => gx + gy * GP + gz * GP * GP;
        private static int CellIdx(int mx, int my, int mz) => mx + my * MC + mz * MC * MC;

        /// <summary>Map continuous corner-grid coords (g-space) to body-local metres.</summary>
        private float3 MapToWorld(float3 g)
        {
            float cu = g.x - 1f, cv = g.y - 1f, ck = g.z - 1f;
            float u = uvMin.x + cu * cellUv.x;
            float v = uvMin.y + cv * cellUv.y;
            float3 dir = CubeSphere.FaceDirection(face, u, v);
            return dir * (rLo + ck * dr);
        }

        private float3 GradientAt(int gx, int gy, int gz)
        {
            int xm = math.max(gx - 1, 0), xp = math.min(gx + 1, GP - 1);
            int ym = math.max(gy - 1, 0), yp = math.min(gy + 1, GP - 1);
            int zm = math.max(gz - 1, 0), zp = math.min(gz + 1, GP - 1);
            return new float3(
                density[CornerIdx(xp, gy, gz)] - density[CornerIdx(xm, gy, gz)],
                density[CornerIdx(gx, yp, gz)] - density[CornerIdx(gx, ym, gz)],
                density[CornerIdx(gx, gy, zp)] - density[CornerIdx(gx, gy, zm)]);
        }

        public void Execute()
        {
            for (int i = 0; i < cellVertexIndex.Length; i++) cellVertexIndex[i] = -1;

            int vertexCount = 0;
            int indexCount  = 0;
            float3 bbMin = new float3(float.MaxValue);
            float3 bbMax = new float3(float.MinValue);

            // Grid→world handedness: if the (u, v, radial) image is left-handed the
            // canonical quad winding must be mirrored (cube faces differ in parity).
            float3 gc = new float3(GP * 0.5f);
            float3 tu = MapToWorld(gc + new float3(1, 0, 0)) - MapToWorld(gc - new float3(1, 0, 0));
            float3 tv = MapToWorld(gc + new float3(0, 1, 0)) - MapToWorld(gc - new float3(0, 1, 0));
            float3 tr = MapToWorld(gc + new float3(0, 0, 1)) - MapToWorld(gc - new float3(0, 0, 1));
            bool flipGlobal = math.dot(math.cross(tu, tv), tr) < 0f;

            // ── Pass 1: one QEF vertex per sign-change cell (incl. ghost cells) ──
            for (int mz = 0; mz < MC; mz++)
            for (int my = 0; my < MC; my++)
            for (int mx = 0; mx < MC; mx++)
            {
                float d000 = density[CornerIdx(mx,     my,     mz)];
                float d100 = density[CornerIdx(mx + 1, my,     mz)];
                float d010 = density[CornerIdx(mx,     my + 1, mz)];
                float d110 = density[CornerIdx(mx + 1, my + 1, mz)];
                float d001 = density[CornerIdx(mx,     my,     mz + 1)];
                float d101 = density[CornerIdx(mx + 1, my,     mz + 1)];
                float d011 = density[CornerIdx(mx,     my + 1, mz + 1)];
                float d111 = density[CornerIdx(mx + 1, my + 1, mz + 1)];

                int mask = 0;
                if (d000 > 0f) mask |= 1;
                if (d100 > 0f) mask |= 2;
                if (d010 > 0f) mask |= 4;
                if (d110 > 0f) mask |= 8;
                if (d001 > 0f) mask |= 16;
                if (d101 > 0f) mask |= 32;
                if (d011 > 0f) mask |= 64;
                if (d111 > 0f) mask |= 128;
                if (mask == 0 || mask == 255) continue;
                if (vertexCount >= GpuVoxelConstants.MAX_VERTICES) break;

                // Hermite data: edge crossings (position + field gradient), g-space.
                float3 massPoint = float3.zero;
                int crossings = 0;
                // stack storage for up to 12 crossings
                float3 p0 = default, p1 = default, p2 = default, p3 = default,
                       p4 = default, p5 = default, p6 = default, p7 = default,
                       p8 = default, p9 = default, p10 = default, p11 = default;
                float3 n0 = default, n1 = default, n2 = default, n3 = default,
                       n4 = default, n5 = default, n6 = default, n7 = default,
                       n8 = default, n9 = default, n10 = default, n11 = default;

                for (int e = 0; e < 12; e++)
                {
                    int ax0, ay0, az0, ax1, ay1, az1;
                    EdgeCorners(e, out ax0, out ay0, out az0, out ax1, out ay1, out az1);
                    int ga = CornerIdx(mx + ax0, my + ay0, mz + az0);
                    int gb = CornerIdx(mx + ax1, my + ay1, mz + az1);
                    float da = density[ga], db = density[gb];
                    if ((da > 0f) == (db > 0f)) continue;

                    float t = da / (da - db);
                    float3 cp = new float3(mx + ax0, my + ay0, mz + az0)
                              + t * new float3(ax1 - ax0, ay1 - ay0, az1 - az0);
                    float3 ga3 = GradientAt(mx + ax0, my + ay0, mz + az0);
                    float3 gb3 = GradientAt(mx + ax1, my + ay1, mz + az1);
                    float3 nrm = math.normalizesafe(math.lerp(ga3, gb3, t), new float3(0, 0, 1));

                    switch (crossings)
                    {
                        case 0:  p0 = cp; n0 = nrm; break;
                        case 1:  p1 = cp; n1 = nrm; break;
                        case 2:  p2 = cp; n2 = nrm; break;
                        case 3:  p3 = cp; n3 = nrm; break;
                        case 4:  p4 = cp; n4 = nrm; break;
                        case 5:  p5 = cp; n5 = nrm; break;
                        case 6:  p6 = cp; n6 = nrm; break;
                        case 7:  p7 = cp; n7 = nrm; break;
                        case 8:  p8 = cp; n8 = nrm; break;
                        case 9:  p9 = cp; n9 = nrm; break;
                        case 10: p10 = cp; n10 = nrm; break;
                        default: p11 = cp; n11 = nrm; break;
                    }
                    massPoint += cp;
                    crossings++;
                }
                if (crossings == 0) continue;
                massPoint /= crossings;

                // QEF minimisation — Schmitz particle relaxation. Keeps smooth
                // terrain smooth and pulls the vertex onto sharp creases.
                float3 x = massPoint;
                float step = 0.35f / crossings;
                for (int it = 0; it < 10; it++)
                {
                    float3 force = float3.zero;
                    for (int ci = 0; ci < crossings; ci++)
                    {
                        float3 pi, ni;
                        switch (ci)
                        {
                            case 0:  pi = p0;  ni = n0;  break;
                            case 1:  pi = p1;  ni = n1;  break;
                            case 2:  pi = p2;  ni = n2;  break;
                            case 3:  pi = p3;  ni = n3;  break;
                            case 4:  pi = p4;  ni = n4;  break;
                            case 5:  pi = p5;  ni = n5;  break;
                            case 6:  pi = p6;  ni = n6;  break;
                            case 7:  pi = p7;  ni = n7;  break;
                            case 8:  pi = p8;  ni = n8;  break;
                            case 9:  pi = p9;  ni = n9;  break;
                            case 10: pi = p10; ni = n10; break;
                            default: pi = p11; ni = n11; break;
                        }
                        force += ni * math.dot(ni, pi - x);
                    }
                    x += force * step;
                    // Never leave the cell — guarantees a manifold, crack-free mesh.
                    x = math.clamp(x, new float3(mx, my, mz), new float3(mx + 1, my + 1, mz + 1));
                }

                // Material: majority vote among solid corners.
                uint mat = PickMaterial(mx, my, mz);
                float3 world = MapToWorld(x);
                float3 pos = world - anchor;

                // Stable per-vertex tint jitter (matches the legacy terrain look).
                float jitter = (noise.snoise(world * 0.7f) * 0.5f) * 0.12f;
                Color32 baseCol = materialColors[(int)(mat & 0xFF)];
                Color32 col = new Color32(
                    (byte)math.clamp(baseCol.r + (int)(jitter * 255f), 0, 255),
                    (byte)math.clamp(baseCol.g + (int)(jitter * 255f), 0, 255),
                    (byte)math.clamp(baseCol.b + (int)(jitter * 255f), 0, 255),
                    255);

                vertScratch[vertexCount] = pos;
                normScratch[vertexCount] = float3.zero;
                colScratch[vertexCount]  = col;
                cellVertexIndex[CellIdx(mx, my, mz)] = vertexCount;
                bbMin = math.min(bbMin, pos);
                bbMax = math.max(bbMax, pos);
                vertexCount++;
            }

            // ── Pass 2: quads — one per owned surface-crossing edge ──
            // Ownership: the edge's min corner (c-coords) lies in [0, 64)³, so every
            // boundary quad is emitted by exactly one node of each depth.
            for (int axis = 0; axis < 3; axis++)
            {
                int3 ea = int3.zero; ea[axis] = 1;
                int b = (axis + 1) % 3, c = (axis + 2) % 3;   // right-handed (a, b, c)
                int3 eb = int3.zero; eb[b] = 1;
                int3 ec = int3.zero; ec[c] = 1;

                for (int cz = 0; cz < N; cz++)
                for (int cy = 0; cy < N; cy++)
                for (int cx = 0; cx < N; cx++)
                {
                    int3 cc = new int3(cx, cy, cz);            // corner c-coords
                    int3 g0 = cc + 1;                           // corner grid index
                    int3 g1 = g0 + ea;
                    float da = density[CornerIdx(g0.x, g0.y, g0.z)];
                    float db = density[CornerIdx(g1.x, g1.y, g1.z)];
                    if ((da > 0f) == (db > 0f)) continue;

                    // The 4 cells sharing this edge (m-index = c-coord + 1).
                    int3 m11 = cc + 1;             // cell at c
                    int3 m01 = m11 - eb;
                    int3 m10 = m11 - ec;
                    int3 m00 = m11 - eb - ec;

                    int v00 = cellVertexIndex[CellIdx(m00.x, m00.y, m00.z)];
                    int v10 = cellVertexIndex[CellIdx(m10.x, m10.y, m10.z)];
                    int v11 = cellVertexIndex[CellIdx(m11.x, m11.y, m11.z)];
                    int v01 = cellVertexIndex[CellIdx(m01.x, m01.y, m01.z)];
                    if (v00 < 0 || v10 < 0 || v11 < 0 || v01 < 0) continue;
                    if (indexCount + 6 > GpuVoxelConstants.MAX_INDICES) break;

                    bool flip = (da <= 0f) ^ flipGlobal;
                    EmitQuad(v00, v10, v11, v01, flip, ref indexCount);
                }
            }

            // ── Normals: angle-free accumulation from the actual triangles ──
            for (int t = 0; t + 2 < indexCount; t += 3)
            {
                int ia = idxScratch[t], ib = idxScratch[t + 1], ic = idxScratch[t + 2];
                float3 fn = math.cross(vertScratch[ib] - vertScratch[ia],
                                       vertScratch[ic] - vertScratch[ia]);
                normScratch[ia] += fn;
                normScratch[ib] += fn;
                normScratch[ic] += fn;
            }
            for (int vi = 0; vi < vertexCount; vi++)
            {
                float3 fallback = math.normalizesafe(vertScratch[vi] + anchor, new float3(0, 1, 0));
                normScratch[vi] = math.normalizesafe(normScratch[vi], fallback);
            }

            // ── Pass 3: LOD skirts on the four tangential footprint sides ──
            // Double-sided radial ribbons that hide the sub-cell cracks where a
            // node borders a neighbour of different quadtree depth.
            EmitSkirts(ref vertexCount, ref indexCount, ref bbMin, ref bbMax);

            // ── Upload ──
            meshData.SetVertexBufferParams(math.max(vertexCount, 1), vertexAttributes);
            var verts = meshData.GetVertexData<VertexLayout>();
            for (int vi = 0; vi < vertexCount; vi++)
                verts[vi] = new VertexLayout
                {
                    pos = vertScratch[vi],
                    normal = normScratch[vi],
                    color = colScratch[vi]
                };

            meshData.SetIndexBufferParams(math.max(indexCount, 1), IndexFormat.UInt32);
            var idx = meshData.GetIndexData<int>();
            for (int ii = 0; ii < indexCount; ii++) idx[ii] = idxScratch[ii];

            var subBounds = vertexCount > 0
                ? new Bounds((Vector3)((bbMin + bbMax) * 0.5f), (Vector3)(bbMax - bbMin))
                : new Bounds(Vector3.zero, Vector3.zero);

            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount)
            {
                bounds = subBounds,
                vertexCount = vertexCount
            }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            boundsOut[0] = subBounds;
            counts[0] = vertexCount;
            counts[1] = indexCount;
        }

        private void EmitSkirts(ref int vertexCount, ref int indexCount,
                                ref float3 bbMin, ref float3 bbMax)
        {
            var planeSkirt = new NativeArray<int>(MC * MC, Allocator.Temp);
            var skirtSource = new NativeList<int2>(1024, Allocator.Temp);

            // side: 0 = u-min, 1 = u-max, 2 = v-min, 3 = v-max (footprint cells only).
            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < planeSkirt.Length; i++) planeSkirt[i] = -1;

                int fixedC = (side == 0 || side == 2) ? 0 : N - 1;   // rim cell c-coord
                bool uAxis = side < 2;

                for (int cr = 0; cr < N; cr++)        // radial cells
                for (int ct = 0; ct < N; ct++)        // tangential cells along the rim
                {
                    int3 cc = uAxis ? new int3(fixedC, ct, cr) : new int3(ct, fixedC, cr);
                    int vi = cellVertexIndex[CellIdx(cc.x + 1, cc.y + 1, cc.z + 1)];
                    if (vi < 0) continue;

                    if (vertexCount + 1 >= GpuVoxelConstants.MAX_VERTICES) continue;

                    // Skirt vertex: source vertex dropped toward the core.
                    float3 srcPos = vertScratch[vi];
                    float3 radial = math.normalizesafe(srcPos + anchor, new float3(0, 1, 0));
                    float3 sPos = srcPos - radial * skirtDepth;

                    int si = vertexCount;
                    vertScratch[si] = sPos;
                    normScratch[si] = float3.zero;
                    colScratch[si]  = colScratch[vi];
                    bbMin = math.min(bbMin, sPos);
                    bbMax = math.max(bbMax, sPos);
                    vertexCount++;

                    planeSkirt[ct + cr * MC] = si;
                    skirtSource.Add(new int2(si, vi));

                    // Connect to the previous cells along the rim (t− and r−).
                    if (ct > 0)
                        TryEmitSkirtQuad(planeSkirt, uAxis, fixedC, ct - 1, cr, ct, cr,
                                         ref indexCount);
                    if (cr > 0)
                        TryEmitSkirtQuad(planeSkirt, uAxis, fixedC, ct, cr - 1, ct, cr,
                                         ref indexCount);
                }
            }

            // Skirt shading: inherit the rim vertex normal (skirts are filler geometry).
            for (int i = 0; i < skirtSource.Length; i++)
                normScratch[skirtSource[i].x] = normScratch[skirtSource[i].y];

            planeSkirt.Dispose();
            skirtSource.Dispose();
        }

        private void TryEmitSkirtQuad(NativeArray<int> planeSkirt, bool uAxis, int fixedC,
                                      int tA, int rA, int tB, int rB, ref int indexCount)
        {
            int sA = planeSkirt[tA + rA * MC];
            int sB = planeSkirt[tB + rB * MC];
            if (sA < 0 || sB < 0) return;

            int3 ccA = uAxis ? new int3(fixedC, tA, rA) : new int3(tA, fixedC, rA);
            int3 ccB = uAxis ? new int3(fixedC, tB, rB) : new int3(tB, fixedC, rB);
            int vA = cellVertexIndex[CellIdx(ccA.x + 1, ccA.y + 1, ccA.z + 1)];
            int vB = cellVertexIndex[CellIdx(ccB.x + 1, ccB.y + 1, ccB.z + 1)];
            if (vA < 0 || vB < 0) return;
            if (indexCount + 12 > GpuVoxelConstants.MAX_INDICES) return;

            // Double-sided: skirts must be visible from any direction.
            EmitQuad(vA, vB, sB, sA, false, ref indexCount);
            EmitQuad(vA, vB, sB, sA, true,  ref indexCount);
        }

        private void EmitQuad(int a, int b, int c, int d, bool flip, ref int indexCount)
        {
            if (flip)
            {
                idxScratch[indexCount++] = a;
                idxScratch[indexCount++] = c;
                idxScratch[indexCount++] = b;
                idxScratch[indexCount++] = a;
                idxScratch[indexCount++] = d;
                idxScratch[indexCount++] = c;
            }
            else
            {
                idxScratch[indexCount++] = a;
                idxScratch[indexCount++] = b;
                idxScratch[indexCount++] = c;
                idxScratch[indexCount++] = a;
                idxScratch[indexCount++] = c;
                idxScratch[indexCount++] = d;
            }
        }

        private uint PickMaterial(int mx, int my, int mz)
        {
            uint best = 1;
            int bestCount = 0;
            for (int a = 0; a < 8; a++)
            {
                int gx = mx + (a & 1);
                int gy = my + ((a >> 1) & 1);
                int gz = mz + ((a >> 2) & 1);
                int gi = CornerIdx(gx, gy, gz);
                if (density[gi] <= 0f) continue;

                uint m = material[gi];
                if (m == 0) continue;
                // count matches among the remaining solid corners
                int count = 1;
                for (int b2 = a + 1; b2 < 8; b2++)
                {
                    int hx = mx + (b2 & 1);
                    int hy = my + ((b2 >> 1) & 1);
                    int hz = mz + ((b2 >> 2) & 1);
                    int hi = CornerIdx(hx, hy, hz);
                    if (density[hi] > 0f && material[hi] == m) count++;
                }
                if (count > bestCount) { bestCount = count; best = m; }
            }
            return best;
        }

        private static void EdgeCorners(int e,
            out int ax, out int ay, out int az, out int bx, out int by, out int bz)
        {
            switch (e)
            {
                case 0:  ax = 0; ay = 0; az = 0; bx = 1; by = 0; bz = 0; break;
                case 1:  ax = 0; ay = 1; az = 0; bx = 1; by = 1; bz = 0; break;
                case 2:  ax = 0; ay = 0; az = 1; bx = 1; by = 0; bz = 1; break;
                case 3:  ax = 0; ay = 1; az = 1; bx = 1; by = 1; bz = 1; break;
                case 4:  ax = 0; ay = 0; az = 0; bx = 0; by = 1; bz = 0; break;
                case 5:  ax = 1; ay = 0; az = 0; bx = 1; by = 1; bz = 0; break;
                case 6:  ax = 0; ay = 0; az = 1; bx = 0; by = 1; bz = 1; break;
                case 7:  ax = 1; ay = 0; az = 1; bx = 1; by = 1; bz = 1; break;
                case 8:  ax = 0; ay = 0; az = 0; bx = 0; by = 0; bz = 1; break;
                case 9:  ax = 1; ay = 0; az = 0; bx = 1; by = 0; bz = 1; break;
                case 10: ax = 0; ay = 1; az = 0; bx = 0; by = 1; bz = 1; break;
                default: ax = 1; ay = 1; az = 0; bx = 1; by = 1; bz = 1; break;
            }
        }
    }
}
