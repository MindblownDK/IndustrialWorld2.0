// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// Simple, clean water surface. TOP FACES ONLY — no sides, no shore extensions.
// Just flat quads at water surface height with smoothed heights.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    public static class WaterMeshBuilder
    {
        private static readonly Queue<Chunk> _queue = new();
        private static readonly HashSet<Chunk> _queued = new();
        private static Material _mat;

        public static void Schedule(Chunk c)
        {
            if (c != null && _queued.Add(c)) _queue.Enqueue(c);
        }

        public static void Pump(int budget)
        {
            EnsureMat();
            int done = 0;
            while (done < budget && _queue.Count > 0)
            {
                var c = _queue.Dequeue(); _queued.Remove(c);
                if (c == null || !c.isGenerated) continue;
                Build(c);
                done++;
            }
        }

        private static void EnsureMat()
        {
            if (_mat != null) return;
            var sh = Shader.Find("VoxelEngine/VoxelWaterURP")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");
            _mat = new Material(sh) { name = "VoxelWater" };
            // Shader Properties block has all defaults — just set blend mode.
            _mat.SetOverrideTag("RenderType", "Transparent");
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_ZWrite", 0);
            _mat.renderQueue = 3000;
        }

        private static void Build(Chunk c)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            EnsureGO(c);

            // Find the water surface height in each column.
            float[,] surfY = new float[S, S];
            bool[,] hasW = new bool[S, S];
            bool any = false;

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                for (int y = S - 1; y >= 0; y--)
                {
                    var v = c.GetVoxelLocal(x, y, z);
                    if (v.waterLevel == 0 || v.IsSolid) continue;
                    if (y + 1 < S) { var a = c.GetVoxelLocal(x, y + 1, z); if (a.waterLevel > 0 && !a.IsSolid) continue; }
                    surfY[x, z] = y + v.WaterFill;
                    hasW[x, z] = true;
                    any = true;
                    break;
                }
            }

            if (!any) { ClearGO(c); return; }

            // Smooth: average each cell's height with its neighbours.
            float[,] smooth = new float[S, S];
            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                if (!hasW[x, z]) continue;
                float sum = surfY[x, z]; int cnt = 1;
                if (x > 0 && hasW[x-1, z]) { sum += surfY[x-1, z]; cnt++; }
                if (x < S-1 && hasW[x+1, z]) { sum += surfY[x+1, z]; cnt++; }
                if (z > 0 && hasW[x, z-1]) { sum += surfY[x, z-1]; cnt++; }
                if (z < S-1 && hasW[x, z+1]) { sum += surfY[x, z+1]; cnt++; }
                smooth[x, z] = sum / cnt;
            }

            // Build simple top-face quads. NO sides, NO shore extensions.
            var verts = new List<Vector3>(S * S * 4);
            var tris = new List<int>(S * S * 6);
            var norms = new List<Vector3>(S * S * 4);
            var uvs = new List<Vector2>(S * S * 4);

            float wX = c.coord.x * S;
            float wZ = c.coord.z * S;

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                if (!hasW[x, z]) continue;
                float h = smooth[x, z];

                // Extend slightly into adjacent solid terrain to connect water to land.
                float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;
                if (x > 0 && !hasW[x-1, z] && c.GetVoxelLocal(x-1, (int)h, z).IsSolid) x0 -= 0.3f;
                if (x < S-1 && !hasW[x+1, z] && c.GetVoxelLocal(x+1, (int)h, z).IsSolid) x1 += 0.3f;
                if (z > 0 && !hasW[x, z-1] && c.GetVoxelLocal(x, (int)h, z-1).IsSolid) z0 -= 0.3f;
                if (z < S-1 && !hasW[x, z+1] && c.GetVoxelLocal(x, (int)h, z+1).IsSolid) z1 += 0.3f;

                int i = verts.Count;
                verts.Add(new Vector3(x0, h, z0));
                verts.Add(new Vector3(x1, h, z0));
                verts.Add(new Vector3(x1, h, z1));
                verts.Add(new Vector3(x0, h, z1));

                norms.Add(Vector3.up); norms.Add(Vector3.up);
                norms.Add(Vector3.up); norms.Add(Vector3.up);

                uvs.Add(new Vector2(wX + x, wZ + z));
                uvs.Add(new Vector2(wX + x + 1, wZ + z));
                uvs.Add(new Vector2(wX + x + 1, wZ + z + 1));
                uvs.Add(new Vector2(wX + x, wZ + z + 1));

                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }

            if (verts.Count == 0) { ClearGO(c); return; }

            if (c.waterMesh == null) c.waterMesh = new Mesh { name = "WaterSurface" };
            c.waterMesh.Clear();
            c.waterMesh.indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            c.waterMesh.SetVertices(verts);
            c.waterMesh.SetTriangles(tris, 0);
            c.waterMesh.SetNormals(norms);
            c.waterMesh.SetUVs(0, uvs);
            c.waterMesh.RecalculateBounds();
            c.waterMeshFilter.sharedMesh = c.waterMesh;
            c.waterMeshRenderer.sharedMaterial = _mat;
            c.waterMeshGO.SetActive(true);
        }

        private static void ClearGO(Chunk c)
        {
            if (c.waterMeshGO != null) c.waterMeshGO.SetActive(false);
        }

        private static void EnsureGO(Chunk c)
        {
            if (c.waterMeshGO != null)
            {
                foreach (var col in c.waterMeshGO.GetComponents<Collider>()) Object.Destroy(col);
                return;
            }
            c.waterMeshGO = new GameObject("WaterSurface");
            c.waterMeshGO.transform.SetParent(c.go.transform, false);
            c.waterMeshFilter = c.waterMeshGO.AddComponent<MeshFilter>();
            c.waterMeshRenderer = c.waterMeshGO.AddComponent<MeshRenderer>();
            c.waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            c.waterMeshRenderer.receiveShadows = false;
        }
    }
}
