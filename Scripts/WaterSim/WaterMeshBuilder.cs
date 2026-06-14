// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// High-fidelity chunk-local liquid surface builder for water + crude oil.
// Builds one combined mesh with two material submeshes:
//   submesh 0 = water (clear, animated, foamy)
//   submesh 1 = crude oil (dark, viscous, slower waves)
//
// Compared with the old top-face-only mesh, this adds:
//   • material-aware water/oil rendering
//   • smoothed corner heights for less blocky pool surfaces
//   • vertical side skirts so waterfalls and cut-open pools look continuous
//   • shore overlap to hide cracks against voxel terrain

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    public static class WaterMeshBuilder
    {
        private static readonly Queue<Chunk> _queue = new();
        private static readonly HashSet<Chunk> _queued = new();
        private static Material _waterMat;
        private static Material _oilMat;

        private struct SurfaceCell
        {
            public bool has;
            public LiquidType liquid;
            public float h;
            public int y;
        }

        public static void Schedule(Chunk c)
        {
            if (c != null && _queued.Add(c)) _queue.Enqueue(c);
        }

        public static void Pump(int budget)
        {
            EnsureMats();
            int done = 0;
            while (done < budget && _queue.Count > 0)
            {
                var c = _queue.Dequeue(); _queued.Remove(c);
                if (c == null || !c.isGenerated) continue;
                Build(c);
                done++;
            }
        }

        private static void EnsureMats()
        {
            if (_waterMat != null && _oilMat != null) return;

            var sh = Shader.Find("VoxelEngine/VoxelWaterURP")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");

            if (_waterMat == null)
            {
                _waterMat = new Material(sh) { name = "VoxelWater_Realistic" };
                ConfigureTransparent(_waterMat);
                _waterMat.SetColor("_ShallowColor", new Color(0.12f, 0.58f, 0.86f, 0.68f));
                _waterMat.SetColor("_DeepColor",    new Color(0.02f, 0.10f, 0.28f, 0.88f));
                _waterMat.SetColor("_FoamColor",    new Color(0.86f, 0.94f, 1.00f, 0.82f));
                _waterMat.SetFloat("_WaveAmp", 0.20f);
                _waterMat.SetFloat("_WaveFreq", 0.62f);
                _waterMat.SetFloat("_WaveSpeed", 0.78f);
                _waterMat.SetFloat("_WaveChop", 0.32f);
                _waterMat.SetFloat("_NormalScale", 1.25f);
                _waterMat.SetFloat("_Gloss", 0.98f);
                _waterMat.SetFloat("_RefractionStrength", 0.026f);
                _waterMat.SetFloat("_FoamIntensity", 1.05f);
                _waterMat.SetFloat("_DepthFade", 4.2f);
                _waterMat.SetFloat("_FoamWidth", 0.85f);
            }

            if (_oilMat == null)
            {
                _oilMat = new Material(sh) { name = "VoxelCrudeOil_Viscous" };
                ConfigureTransparent(_oilMat);
                _oilMat.SetColor("_ShallowColor", new Color(0.10f, 0.075f, 0.045f, 0.86f));
                _oilMat.SetColor("_DeepColor",    new Color(0.012f, 0.010f, 0.008f, 0.96f));
                _oilMat.SetColor("_FoamColor",    new Color(0.32f, 0.23f, 0.11f, 0.45f));
                _oilMat.SetFloat("_WaveAmp", 0.055f);
                _oilMat.SetFloat("_WaveFreq", 0.48f);
                _oilMat.SetFloat("_WaveSpeed", 0.18f);
                _oilMat.SetFloat("_WaveChop", 0.08f);
                _oilMat.SetFloat("_NormalScale", 0.58f);
                _oilMat.SetFloat("_Gloss", 1.0f);
                _oilMat.SetFloat("_RefractionStrength", 0.006f);
                _oilMat.SetFloat("_FoamIntensity", 0.18f);
                _oilMat.SetFloat("_DepthFade", 2.0f);
                _oilMat.SetFloat("_FoamWidth", 0.22f);
            }

        }

        private static void ConfigureTransparent(Material mat)
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
        }

        private static void Build(Chunk c)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            EnsureGO(c);

            var cells = new SurfaceCell[S, S];
            bool any = false;

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                for (int y = S - 1; y >= 0; y--)
                {
                    var v = c.GetVoxelLocal(x, y, z);
                    if (!FluidMaterialUtility.IsFluid(v)) continue;
                    if (y + 1 < S)
                    {
                        var above = c.GetVoxelLocal(x, y + 1, z);
                        if (FluidMaterialUtility.IsFluid(above) && FluidMaterialUtility.LiquidFromVoxel(above) == FluidMaterialUtility.LiquidFromVoxel(v))
                            continue;
                    }

                    var liquid = FluidMaterialUtility.LiquidFromVoxel(v);
                    cells[x, z] = new SurfaceCell
                    {
                        has = true,
                        liquid = liquid,
                        // Visual surface is deliberately flatter than the simulation byte.
                        // The CA solver may leave tiny level differences between cells;
                        // rendering those literally creates visible "water layers". Real
                        // ocean/pool water reads as one continuous sheet, with movement
                        // coming from the shader waves instead.
                        h = VisualSurfaceHeight(y, v.waterLevel, liquid),
                        y = y
                    };
                    any = true;
                    break;
                }
            }

            if (!any) { ClearGO(c); return; }

            var verts = new List<Vector3>(S * S * 8);
            var norms = new List<Vector3>(S * S * 8);
            var uvs = new List<Vector2>(S * S * 8);
            var waterTris = new List<int>(S * S * 6);
            var oilTris = new List<int>(S * S * 6);

            float wX = c.coord.x * S;
            float wZ = c.coord.z * S;

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                var cell = cells[x, z];
                if (!cell.has) continue;

                var tris = cell.liquid == LiquidType.CrudeOil ? oilTris : waterTris;
                AddTop(c, cells, x, z, wX, wZ, verts, norms, uvs, tris);
                // Vertical side sheets caused visible double-layer slabs at shorelines.
                // Keep liquid as a clean continuous top surface; shader depth foam handles shore contact.
            }

            if (verts.Count == 0) { ClearGO(c); return; }

            if (c.waterMesh == null) c.waterMesh = new Mesh { name = "LiquidSurface" };
            c.waterMesh.Clear();
            c.waterMesh.indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            c.waterMesh.SetVertices(verts);
            c.waterMesh.SetNormals(norms);
            c.waterMesh.SetUVs(0, uvs);
            c.waterMesh.subMeshCount = 2;
            c.waterMesh.SetTriangles(waterTris, 0);
            c.waterMesh.SetTriangles(oilTris, 1);
            c.waterMesh.RecalculateBounds();

            c.waterMeshFilter.sharedMesh = c.waterMesh;
            c.waterMeshRenderer.sharedMaterials = new[] { _waterMat, _oilMat };
            c.waterMeshGO.SetActive(true);
        }

        private static void AddTop(Chunk c, SurfaceCell[,] cells, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            var cell = cells[x, z];
            float h00 = CornerHeight(cells, x, z, cell.liquid, -1, -1, cell.h);
            float h10 = CornerHeight(cells, x, z, cell.liquid,  1, -1, cell.h);
            float h11 = CornerHeight(cells, x, z, cell.liquid,  1,  1, cell.h);
            float h01 = CornerHeight(cells, x, z, cell.liquid, -1,  1, cell.h);

            // Shoreline connection: voxel terrain is smooth/rounded while water is a
            // grid surface, so an exact 0..1 quad leaves visible cracks. Extend only
            // toward nearby solid terrain, far enough to clip under the shore but not
            // as far as the failed foam slabs.
            float shoreTuck = cell.liquid == LiquidType.CrudeOil ? 0.16f : 0.58f;
            float seamOverlap = 0.035f;
            float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;
            if (ShoreSolidNear(c, x - 1, cell.y, z)) x0 -= shoreTuck; else if (x == 0) x0 -= seamOverlap;
            if (ShoreSolidNear(c, x + 1, cell.y, z)) x1 += shoreTuck; else if (x == VoxelConstants.CHUNK_SIZE - 1) x1 += seamOverlap;
            if (ShoreSolidNear(c, x, cell.y, z - 1)) z0 -= shoreTuck; else if (z == 0) z0 -= seamOverlap;
            if (ShoreSolidNear(c, x, cell.y, z + 1)) z1 += shoreTuck; else if (z == VoxelConstants.CHUNK_SIZE - 1) z1 += seamOverlap;

            int i = verts.Count;
            verts.Add(new Vector3(x0, h00, z0));
            verts.Add(new Vector3(x1, h10, z0));
            verts.Add(new Vector3(x1, h11, z1));
            verts.Add(new Vector3(x0, h01, z1));
            for (int n = 0; n < 4; n++) norms.Add(Vector3.up);
            uvs.Add(new Vector2(wX + x0, wZ + z0));
            uvs.Add(new Vector2(wX + x1, wZ + z0));
            uvs.Add(new Vector2(wX + x1, wZ + z1));
            uvs.Add(new Vector2(wX + x0, wZ + z1));
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static void AddShoreFoam(Chunk c, SurfaceCell cell, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            if (cell.liquid != LiquidType.Water) return;
            float y = cell.h + 0.035f;
            const float inner = 0.18f;
            const float outer = 0.46f;

            if (NeighbourIsSolid(c, x - 1, cell.y, z)) AddFoamQuad(new Vector3(x - outer, y, z),     new Vector3(x + inner, y, z),     new Vector3(x + inner, y, z + 1), new Vector3(x - outer, y, z + 1), wX, wZ, verts, norms, uvs, tris);
            if (NeighbourIsSolid(c, x + 1, cell.y, z)) AddFoamQuad(new Vector3(x + 1 - inner, y, z), new Vector3(x + 1 + outer, y, z), new Vector3(x + 1 + outer, y, z + 1), new Vector3(x + 1 - inner, y, z + 1), wX, wZ, verts, norms, uvs, tris);
            if (NeighbourIsSolid(c, x, cell.y, z - 1)) AddFoamQuad(new Vector3(x, y, z - outer),     new Vector3(x + 1, y, z - outer), new Vector3(x + 1, y, z + inner), new Vector3(x, y, z + inner), wX, wZ, verts, norms, uvs, tris);
            if (NeighbourIsSolid(c, x, cell.y, z + 1)) AddFoamQuad(new Vector3(x, y, z + 1 - inner), new Vector3(x + 1, y, z + 1 - inner), new Vector3(x + 1, y, z + 1 + outer), new Vector3(x, y, z + 1 + outer), wX, wZ, verts, norms, uvs, tris);
        }

        private static void AddFoamQuad(Vector3 a, Vector3 b, Vector3 c0, Vector3 d, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c0); verts.Add(d);
            for (int n = 0; n < 4; n++) norms.Add(Vector3.up);
            uvs.Add(new Vector2(wX + a.x, wZ + a.z));
            uvs.Add(new Vector2(wX + b.x, wZ + b.z));
            uvs.Add(new Vector2(wX + c0.x, wZ + c0.z));
            uvs.Add(new Vector2(wX + d.x, wZ + d.z));
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static void AddWaterfallSides(Chunk c, SurfaceCell[,] cells, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            var cell = cells[x, z];
            AddWaterfallSideIfNeeded(c, cells, x, z, cell,  1,  0, new Vector3(1, 0, 0), wX, wZ, verts, norms, uvs, tris);
            AddWaterfallSideIfNeeded(c, cells, x, z, cell, -1,  0, new Vector3(-1, 0, 0), wX, wZ, verts, norms, uvs, tris);
            AddWaterfallSideIfNeeded(c, cells, x, z, cell,  0,  1, new Vector3(0, 0, 1), wX, wZ, verts, norms, uvs, tris);
            AddWaterfallSideIfNeeded(c, cells, x, z, cell,  0, -1, new Vector3(0, 0, -1), wX, wZ, verts, norms, uvs, tris);
        }

        private static void AddWaterfallSideIfNeeded(Chunk c, SurfaceCell[,] cells, int x, int z, SurfaceCell cell, int dx, int dz, Vector3 normal,
            float wX, float wZ, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            int nx = x + dx, nz = z + dz;

            // Never build vertical sheets at chunk borders. Neighbour chunks build
            // their own surfaces and a tiny top overlap handles the seam. This removes
            // the big rectangular "layer" planes visible from the shoreline.
            if (nx < 0 || nx >= VoxelConstants.CHUNK_SIZE || nz < 0 || nz >= VoxelConstants.CHUNK_SIZE)
                return;

            var neighbour = cells[nx, nz];
            if (neighbour.has && neighbour.liquid == cell.liquid)
            {
                // Same liquid beside us: only build a side if this is a genuine
                // waterfall/drop. Small level differences are smoothed by top corners.
                if (cell.h - neighbour.h < 0.85f) return;
            }
            else if (NeighbourIsSolid(c, nx, cell.y, nz))
            {
                // Shoreline/rock wall: do not draw a vertical skirt. The surface laps
                // under/onto terrain instead, which looks far more natural and hides gaps.
                return;
            }

            float top = cell.h;
            float bottom = neighbour.has && neighbour.liquid == cell.liquid
                ? Mathf.Max(neighbour.h, top - 1.25f)
                : top - 0.75f;

            float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;
            Vector3 a, b, c0, d;
            if (dx > 0) { a = new Vector3(x1, bottom, z0); b = new Vector3(x1, bottom, z1); c0 = new Vector3(x1, top, z1); d = new Vector3(x1, top, z0); }
            else if (dx < 0) { a = new Vector3(x0, bottom, z1); b = new Vector3(x0, bottom, z0); c0 = new Vector3(x0, top, z0); d = new Vector3(x0, top, z1); }
            else if (dz > 0) { a = new Vector3(x1, bottom, z1); b = new Vector3(x0, bottom, z1); c0 = new Vector3(x0, top, z1); d = new Vector3(x1, top, z1); }
            else { a = new Vector3(x0, bottom, z0); b = new Vector3(x1, bottom, z0); c0 = new Vector3(x1, top, z0); d = new Vector3(x0, top, z0); }

            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c0); verts.Add(d);
            // Bottom side vertices stay stable, top side vertices receive the same
            // wave displacement as the surface to avoid shoreline/waterfall cracks.
            norms.Add(normal); norms.Add(normal); norms.Add(Vector3.up); norms.Add(Vector3.up);
            uvs.Add(new Vector2(wX + a.x, wZ + a.z));
            uvs.Add(new Vector2(wX + b.x, wZ + b.z));
            uvs.Add(new Vector2(wX + c0.x, wZ + c0.z));
            uvs.Add(new Vector2(wX + d.x, wZ + d.z));
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static float VisualSurfaceHeight(int y, byte level, LiquidType liquid)
        {
            // Render filled/near-filled cells as a clean continuous sheet. This hides
            // byte-level CA stepping while preserving visibly shallow puddles/streams.
            if (level >= 16) return y + (liquid == LiquidType.CrudeOil ? 0.94f : 0.995f);
            return y + Mathf.Clamp(level / 255f, 0.08f, 0.985f);
        }

        private static float CornerHeight(SurfaceCell[,] cells, int x, int z, LiquidType liquid, int sx, int sz, float fallback)
        {
            float sum = fallback;
            int cnt = 1;
            TryAdd(cells, x + sx, z, liquid, ref sum, ref cnt);
            TryAdd(cells, x, z + sz, liquid, ref sum, ref cnt);
            TryAdd(cells, x + sx, z + sz, liquid, ref sum, ref cnt);
            return sum / cnt;
        }

        private static void TryAdd(SurfaceCell[,] cells, int x, int z, LiquidType liquid, ref float sum, ref int cnt)
        {
            if (x < 0 || x >= VoxelConstants.CHUNK_SIZE || z < 0 || z >= VoxelConstants.CHUNK_SIZE) return;
            var c = cells[x, z];
            if (!c.has || c.liquid != liquid) return;
            sum += c.h;
            cnt++;
        }

        private static bool NeighbourIsSolid(Chunk c, int x, int y, int z)
        {
            if (x < 0 || x >= VoxelConstants.CHUNK_SIZE || z < 0 || z >= VoxelConstants.CHUNK_SIZE || y < 0 || y >= VoxelConstants.CHUNK_SIZE)
                return false;
            return c.GetVoxelLocal(x, y, z).IsSolid;
        }

        private static bool ShoreSolidNear(Chunk c, int x, int y, int z)
        {
            if (x < 0 || x >= VoxelConstants.CHUNK_SIZE || z < 0 || z >= VoxelConstants.CHUNK_SIZE)
                return false;

            // Smooth voxel terrain can intersect water above/below the exact water
            // voxel Y. Sample a generous vertical range so the water tucks under
            // sloped beaches and rounded banks instead of leaving air gaps.
            for (int yy = y + 2; yy >= y - 5; yy--)
            {
                if (yy < 0 || yy >= VoxelConstants.CHUNK_SIZE) continue;
                if (c.GetVoxelLocal(x, yy, z).IsSolid) return true;
            }
            return false;
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
            c.waterMeshGO = new GameObject("LiquidSurface");
            c.waterMeshGO.transform.SetParent(c.go.transform, false);
            c.waterMeshFilter = c.waterMeshGO.AddComponent<MeshFilter>();
            c.waterMeshRenderer = c.waterMeshGO.AddComponent<MeshRenderer>();
            c.waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            c.waterMeshRenderer.receiveShadows = false;
        }
    }
}
