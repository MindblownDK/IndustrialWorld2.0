// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// V7: Shore-absorption opacity + no geometry foam + cross-chunk terrain + no boundary smoothing.
//
// Strategy:
//   • Water is a SINGLE thin sheet on top + side curtains at shoreline
//   • The SHADER handles "double layer" via shore-absorption opacity boost
//     (water becomes opaque when terrain is close below)
//   • The SHADER handles shore foam via depth-based detection
//   • NO geometry foam quads — they caused chunk-edge foam artifacts
//   • Side curtains connect water surface to terrain visually
//   • Boundary cells are NOT smoothed to ensure consistent height across chunks
//   • Cross-chunk terrain detection enables tuck and curtains at chunk boundaries
//   • SurfaceNetsJob V6 treats fluid materials as empty → no water-colored terrain

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.WaterSim
{
    public static class WaterMeshBuilder
    {
        private static readonly Queue<Chunk> _queue = new();
        private static readonly HashSet<Chunk> _queued = new();
        private static Material _waterMat;
        private static Material _oilMat;
        private static readonly List<Vector3> _sphereCellCenters = new(32);
        private static readonly HashSet<Vector3Int> _sphereSurfaceCells = new();

        // Fluid material IDs — same as SurfaceNetsJob
        private const byte WaterVoxelMat  = (byte)MaterialId.WaterVoxel;
        private const byte WaterLiquidMat = (byte)MaterialId.WaterLiquid;
        private const byte OilMat         = (byte)MaterialId.CrudeOil;

        private struct SurfaceCell
        {
            public bool has;
            public LiquidType liquid;
            public float h;        // final surface height (after shoreline lowering)
            public int y;          // voxel Y
            public Vector2 flow;
            public bool bordersTerrain;
            public float terrainH;
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
                _waterMat = new Material(sh) { name = "VoxelWater_KWS2" };
                ConfigureTransparent(_waterMat);
                _waterMat.SetColor("_ShallowColor", new Color(0.08f, 0.52f, 0.82f, 0.92f));
                _waterMat.SetColor("_DeepColor",    new Color(0.01f, 0.06f, 0.22f, 0.97f));
                _waterMat.SetColor("_FoamColor",    new Color(0.92f, 0.96f, 1.00f, 0.88f));
                _waterMat.SetFloat("_WaveAmp", 0.35f);
                _waterMat.SetFloat("_WaveFreq", 0.55f);
                _waterMat.SetFloat("_WaveSpeed", 0.72f);
                _waterMat.SetFloat("_WaveChop", 0.28f);
                _waterMat.SetFloat("_NormalScale", 1.4f);
                _waterMat.SetFloat("_Gloss", 0.96f);
                _waterMat.SetFloat("_FresnelPower", 3.2f);
                _waterMat.SetFloat("_RefractionStrength", 0.032f);
                _waterMat.SetFloat("_CausticsIntensity", 0.25f);
                _waterMat.SetFloat("_DepthFade", 2.5f);
                _waterMat.SetFloat("_ShoreOpaqueDepth", 1.5f);
                _waterMat.SetFloat("_ShoreFoamWidth", 2.0f);
                _waterMat.SetFloat("_ShoreFoamIntensity", 1.2f);
                _waterMat.SetFloat("_SSSIntensity", 0.35f);
                _waterMat.SetFloat("_FlowNormalStrength", 1.0f);
                _waterMat.SetFloat("_FlowFoamStrength", 0.8f);
                _waterMat.SetFloat("_PlanetWaveBlend", 1.0f);
                _waterMat.SetFloat("_TideStrength", 0.22f);
            }

            if (_oilMat == null)
            {
                _oilMat = new Material(sh) { name = "VoxelCrudeOil_Viscous" };
                ConfigureTransparent(_oilMat);
                _oilMat.SetColor("_ShallowColor", new Color(0.12f, 0.085f, 0.05f, 0.90f));
                _oilMat.SetColor("_DeepColor",    new Color(0.02f, 0.015f, 0.01f, 0.98f));
                _oilMat.SetColor("_FoamColor",    new Color(0.35f, 0.25f, 0.12f, 0.40f));
                _oilMat.SetFloat("_WaveAmp", 0.04f);
                _oilMat.SetFloat("_WaveFreq", 0.40f);
                _oilMat.SetFloat("_WaveSpeed", 0.12f);
                _oilMat.SetFloat("_WaveChop", 0.06f);
                _oilMat.SetFloat("_NormalScale", 0.45f);
                _oilMat.SetFloat("_Gloss", 1.0f);
                _oilMat.SetFloat("_FresnelPower", 4.0f);
                _oilMat.SetFloat("_RefractionStrength", 0.004f);
                _oilMat.SetFloat("_CausticsIntensity", 0.0f);
                _oilMat.SetFloat("_DepthFade", 1.8f);
                _oilMat.SetFloat("_ShoreOpaqueDepth", 1.0f);
                _oilMat.SetFloat("_ShoreFoamWidth", 0.5f);
                _oilMat.SetFloat("_ShoreFoamIntensity", 0.1f);
                _oilMat.SetFloat("_SSSIntensity", 0.0f);
                _oilMat.SetFloat("_FlowNormalStrength", 0.3f);
                _oilMat.SetFloat("_FlowFoamStrength", 0.2f);
                _oilMat.SetFloat("_PlanetWaveBlend", 1.0f);
                _oilMat.SetFloat("_TideStrength", 0.04f);
            }
        }

        private static void ConfigureTransparent(Material mat)
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.SetFloat("_Surface", 1.0f);
            mat.SetFloat("_Blend", 0.0f);
            mat.SetColor("_BaseColor", new Color(0.08f, 0.52f, 0.82f, 0.88f));
            mat.SetColor("_Color", new Color(0.08f, 0.52f, 0.82f, 0.88f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  BUILD
        // ═══════════════════════════════════════════════════════════════════════

        private static void Build(Chunk c)
        {
            if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld) {
                BuildSphere(c);
                return;
            }

            const int S = VoxelConstants.CHUNK_SIZE;
            EnsureGO(c);
            FlowFieldManager.UpdateFlowField(c);

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
                        if (FluidMaterialUtility.IsFluid(above) &&
                            FluidMaterialUtility.LiquidFromVoxel(above) == FluidMaterialUtility.LiquidFromVoxel(v))
                            continue;
                    }
                    if (HasTerrainAbove(c, x, y + 1, z)) break;

                    var liquid = FluidMaterialUtility.LiquidFromVoxel(v);
                    float baseH = VisualSurfaceHeight(y, v.waterLevel, liquid);

                    bool bordersTerrain = false;
                    float terrainH = baseH;
                    float tH;

                    if (TryGetTerrainHeight(c, x - 1, y, z, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }
                    if (TryGetTerrainHeight(c, x + 1, y, z, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }
                    if (TryGetTerrainHeight(c, x, y, z - 1, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }
                    if (TryGetTerrainHeight(c, x, y, z + 1, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }

                    // Also check across chunk boundaries
                    if (!bordersTerrain && x == 0 && IsTerrainInAdjacentChunk(c, -1, y, z)) { bordersTerrain = true; }
                    if (!bordersTerrain && x == S - 1 && IsTerrainInAdjacentChunk(c, S, y, z)) { bordersTerrain = true; }
                    if (!bordersTerrain && z == 0 && IsTerrainInAdjacentChunk(c, x, y, -1)) { bordersTerrain = true; }
                    if (!bordersTerrain && z == S - 1 && IsTerrainInAdjacentChunk(c, x, y, S)) { bordersTerrain = true; }

                    float h = baseH;
                    if (bordersTerrain)
                    {
                        h = Mathf.Min(baseH, terrainH + 0.12f);
                    }

                    cells[x, z] = new SurfaceCell
                    {
                        has = true,
                        liquid = liquid,
                        h = h,
                        y = y,
                        flow = c.GetFlow(x, z),
                        bordersTerrain = bordersTerrain,
                        terrainH = terrainH
                    };
                    any = true;
                    break;
                }
            }

            if (!any) { ClearGO(c); return; }

            // Smooth height field — but NOT boundary cells (ensures consistent
            // height across chunk boundaries, preventing "foam at chunk edges")
            SmoothHeightField(cells, S);

            // Build mesh
            var verts     = new List<Vector3>(S * S * 10);
            var norms     = new List<Vector3>(S * S * 10);
            var uvs       = new List<Vector2>(S * S * 10);
            var uv2s      = new List<Vector2>(S * S * 10);
            var waterTris = new List<int>(S * S * 6);
            var oilTris   = new List<int>(S * S * 6);

            float wX = c.coord.x * S;
            float wZ = c.coord.z * S;

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                var cell = cells[x, z];
                if (!cell.has) continue;

                var tris = cell.liquid == LiquidType.CrudeOil ? oilTris : waterTris;
                AddTop(c, cells, x, z, wX, wZ, verts, norms, uvs, uv2s, tris);
                AddSideCurtains(c, cells, x, z, wX, wZ, verts, norms, uvs, uv2s, tris);
                // NO geometry foam — shader handles all foam via depth-based detection
            }

            if (verts.Count == 0) { ClearGO(c); return; }

            if (c.waterMesh == null) c.waterMesh = new Mesh { name = "LiquidSurface" };
            c.waterMesh.Clear();
            c.waterMesh.indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            c.waterMesh.SetVertices(verts);
            c.waterMesh.SetNormals(norms);
            c.waterMesh.SetUVs(0, uvs);
            c.waterMesh.SetUVs(1, uv2s);
            var flatCols = new List<Color>(verts.Count);
            for (int k = 0; k < verts.Count; k++) flatCols.Add(Color.white);
            c.waterMesh.SetColors(flatCols);
            c.waterMesh.subMeshCount = 2;
            c.waterMesh.SetTriangles(waterTris, 0);
            c.waterMesh.SetTriangles(oilTris, 1);
            c.waterMesh.RecalculateBounds();

            c.waterMeshFilter.sharedMesh = c.waterMesh;
            c.waterMeshRenderer.sharedMaterials = new[] { _waterMat, _oilMat };
            c.waterMeshGO.SetActive(true);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SMOOTHING — skip boundary cells for chunk consistency
        // ═══════════════════════════════════════════════════════════════════════

        private static void SmoothHeightField(SurfaceCell[,] cells, int S)
        {
            var tempH = new float[S, S];
            var tempW = new float[S, S];

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                var cell = cells[x, z];
                if (!cell.has) continue;

                // Skip boundary cells — their raw height must be consistent
                // with the adjacent chunk to prevent visible seams/foam lines
                if (x == 0 || x == S - 1 || z == 0 || z == S - 1)
                {
                    tempH[x, z] = cell.h;
                    tempW[x, z] = 1f;
                    continue;
                }

                float sumH = 0f, sumW = 0f;
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nx >= S || nz < 0 || nz >= S) continue;
                    var n = cells[nx, nz];
                    if (!n.has || n.liquid != cell.liquid) continue;
                    float d2 = dx * dx + dz * dz;
                    float w = 1f / (1f + d2 * 0.5f);
                    sumH += n.h * w;
                    sumW += w;
                }
                tempH[x, z] = sumW > 0f ? sumH / sumW : cell.h;
                tempW[x, z] = sumW;
            }

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                if (tempW[x, z] > 0f) cells[x, z].h = tempH[x, z];
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  TOP SURFACE
        // ═══════════════════════════════════════════════════════════════════════

        private static void AddTop(Chunk c, SurfaceCell[,] cells, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            var cell = cells[x, z];
            float h00 = CornerHeight(cells, x, z, cell.liquid, -1, -1, cell.h);
            float h10 = CornerHeight(cells, x, z, cell.liquid,  1, -1, cell.h);
            float h11 = CornerHeight(cells, x, z, cell.liquid,  1,  1, cell.h);
            float h01 = CornerHeight(cells, x, z, cell.liquid, -1,  1, cell.h);

            float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;

            // Terrain tuck: extend water UNDER the opaque terrain mesh
            float terrainTuck = cell.liquid == LiquidType.CrudeOil ? 0.30f : 0.85f;

            // Check inside chunk
            if (TerrainSolidNear(c, x - 1, cell.y, z)) x0 -= terrainTuck;
            if (TerrainSolidNear(c, x + 1, cell.y, z)) x1 += terrainTuck;
            if (TerrainSolidNear(c, x, cell.y, z - 1)) z0 -= terrainTuck;
            if (TerrainSolidNear(c, x, cell.y, z + 1)) z1 += terrainTuck;

            // Cross-chunk terrain tuck at chunk boundaries
            const int S = VoxelConstants.CHUNK_SIZE;
            if (x == 0 && IsTerrainInAdjacentChunk(c, -1, cell.y, z)) x0 -= terrainTuck;
            if (x == S - 1 && IsTerrainInAdjacentChunk(c, S, cell.y, z)) x1 += terrainTuck;
            if (z == 0 && IsTerrainInAdjacentChunk(c, x, cell.y, -1)) z0 -= terrainTuck;
            if (z == S - 1 && IsTerrainInAdjacentChunk(c, x, cell.y, S)) z1 += terrainTuck;

            Vector2 avgFlow = AverageFlow(cells, x, z, cell.liquid);

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
            for (int n = 0; n < 4; n++) uv2s.Add(avgFlow);

            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SIDE CURTAINS — vertical faces connecting water surface to terrain
        // ═══════════════════════════════════════════════════════════════════════

        private static void AddSideCurtains(Chunk c, SurfaceCell[,] cells, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            var cell = cells[x, z];
            const int S = VoxelConstants.CHUNK_SIZE;

            float hTop = cell.h;
            Vector2 flow = cell.flow;

            // -X face
            bool terrainNegX = x > 0 ? TerrainSolidNear(c, x - 1, cell.y, z) :
                IsTerrainInAdjacentChunk(c, -1, cell.y, z);
            if (terrainNegX)
            {
                float tH;
                float hBot = (x > 0 && TryGetTerrainHeight(c, x - 1, cell.y, z, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuad(x, hTop, hBot, z + 1, z, new Vector3(1, 0, 0),
                        wX, wZ, flow, verts, norms, uvs, uv2s, tris);
            }
            // +X face
            bool terrainPosX = x < S - 1 ? TerrainSolidNear(c, x + 1, cell.y, z) :
                IsTerrainInAdjacentChunk(c, S, cell.y, z);
            if (terrainPosX)
            {
                float tH;
                float hBot = (x < S - 1 && TryGetTerrainHeight(c, x + 1, cell.y, z, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuad(x + 1, hTop, hBot, z, z + 1, new Vector3(-1, 0, 0),
                        wX, wZ, flow, verts, norms, uvs, uv2s, tris);
            }
            // -Z face
            bool terrainNegZ = z > 0 ? TerrainSolidNear(c, x, cell.y, z - 1) :
                IsTerrainInAdjacentChunk(c, x, cell.y, -1);
            if (terrainNegZ)
            {
                float tH;
                float hBot = (z > 0 && TryGetTerrainHeight(c, x, cell.y, z - 1, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuadZ(x, x + 1, hTop, hBot, z, new Vector3(0, 0, 1),
                        wX, wZ, flow, verts, norms, uvs, uv2s, tris);
            }
            // +Z face
            bool terrainPosZ = z < S - 1 ? TerrainSolidNear(c, x, cell.y, z + 1) :
                IsTerrainInAdjacentChunk(c, x, cell.y, S);
            if (terrainPosZ)
            {
                float tH;
                float hBot = (z < S - 1 && TryGetTerrainHeight(c, x, cell.y, z + 1, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuadZ(x + 1, x, hTop, hBot, z + 1, new Vector3(0, 0, -1),
                        wX, wZ, flow, verts, norms, uvs, uv2s, tris);
            }
        }

        private static void AddCurtainQuad(float cx, float hTop, float hBot, float zA, float zB, Vector3 normal,
            float wX, float wZ, Vector2 flow,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            int i = verts.Count;
            verts.Add(new Vector3(cx, hTop, zA));
            verts.Add(new Vector3(cx, hTop, zB));
            verts.Add(new Vector3(cx, hBot, zB));
            verts.Add(new Vector3(cx, hBot, zA));
            for (int n = 0; n < 4; n++) norms.Add(normal);
            uvs.Add(new Vector2(wX + cx, wZ + zA));
            uvs.Add(new Vector2(wX + cx, wZ + zB));
            uvs.Add(new Vector2(wX + cx, wZ + zB));
            uvs.Add(new Vector2(wX + cx, wZ + zA));
            for (int n = 0; n < 4; n++) uv2s.Add(flow);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static void BuildSphere(Chunk c)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            EnsureGO(c);

            FlowFieldManager.UpdateFlowField(c);

            var verts     = new List<Vector3>(S * S * 4);
            var norms     = new List<Vector3>(S * S * 4);
            var uvs       = new List<Vector2>(S * S * 4);
            var uv2s      = new List<Vector2>(S * S * 4);
            var cols      = new List<Color>(S * S * 4);
            var waterTris = new List<int>(S * S * 6);
            var oilTris   = new List<int>(S * S * 6);

            Vector3 chunkVoxel = (Vector3)(c.coord * S);
            _sphereSurfaceCells.Clear();

            var world = ActiveWorld.Current;
            float seaRad = (world != null ? world.SeaLevel : 96) * VoxelConstants.VOXEL_SIZE;

            for (int x = 0; x < S; x++)
            for (int y = 0; y < S; y++)
            for (int z = 0; z < S; z++)
            {
                var v = c.GetVoxelLocal(x, y, z);
                if (!FluidMaterialUtility.IsFluid(v)) continue;

                Vector3 centerVoxel = chunkVoxel + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                float distFromCenter = centerVoxel.magnitude;
                LiquidType liquid = FluidMaterialUtility.LiquidFromVoxel(v);

                if (liquid == LiquidType.Water && distFromCenter < seaRad - 0.85f) continue;

                Vector3Int local = new(x, y, z);
                if (IsCoveredBySameLiquid(c, local, v)) continue;

                Vector3 up = PlanetWaterUtility.LocalUp(centerVoxel * VoxelConstants.VOXEL_SIZE);
                Vector3 surfaceCenter;
                if (liquid == LiquidType.Water && distFromCenter <= seaRad + 5f)
                {
                    surfaceCenter = up * seaRad - chunkVoxel;
                }
                else
                {
                    float fillOffset = (v.waterLevel / 255f - 0.5f) * 0.72f;
                    surfaceCenter = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) + up * fillOffset;
                }

                float depthToTerrain = 1.0f;
                if (c.GetVoxelLocal(x, y - 1, z).IsSolid || c.GetVoxelLocal(x + 1, y, z).IsSolid || c.GetVoxelLocal(x - 1, y, z).IsSolid || c.GetVoxelLocal(x, y, z + 1).IsSolid || c.GetVoxelLocal(x, y, z - 1).IsSolid)
                    depthToTerrain = 0.0f;
                else if (y >= 2 && c.GetVoxelLocal(x, y - 2, z).IsSolid)
                    depthToTerrain = 0.5f;

                Color colAttr = new Color(depthToTerrain, 1f, 1f, 1f);
                cols.Add(colAttr); cols.Add(colAttr); cols.Add(colAttr); cols.Add(colAttr);

                var tris = liquid == LiquidType.CrudeOil ? oilTris : waterTris;
                Vector2 flow = c.GetFlow(x, z);
                Vector3 tideDir = PlanetWaterUtility.CurrentTideDirectionLocal();
                float tideAlign = Vector3.Dot(up, tideDir);
                Vector2 swellFlow = flow + new Vector2(tideAlign * 0.75f, (1f - Mathf.Abs(tideAlign)) * 0.55f);

                AddSphereSurfacePatch(surfaceCenter, up, liquid, chunkVoxel, swellFlow, verts, norms, uvs, uv2s, tris);
                _sphereSurfaceCells.Add(local);
            }

            if (verts.Count == 0) { ClearGO(c); return; }

            if (c.waterMesh == null) c.waterMesh = new Mesh { name = "PlanetLockedLiquidSurface" };
            c.waterMesh.Clear();
            c.waterMesh.indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            c.waterMesh.SetVertices(verts);
            c.waterMesh.SetNormals(norms);
            c.waterMesh.SetUVs(0, uvs);
            c.waterMesh.SetUVs(1, uv2s);
            c.waterMesh.SetColors(cols);
            c.waterMesh.subMeshCount = 2;
            c.waterMesh.SetTriangles(waterTris, 0);
            c.waterMesh.SetTriangles(oilTris, 1);
            c.waterMesh.RecalculateBounds();

            c.waterMeshFilter.sharedMesh = c.waterMesh;
            c.waterMeshRenderer.sharedMaterials = new[] { _waterMat, _oilMat };
            c.waterMeshGO.SetActive(true);
        }

        private static bool IsCoveredBySameLiquid(Chunk c, Vector3Int local, Voxel voxel)
        {
            Vector3Int worldCell = c.coord * VoxelConstants.CHUNK_SIZE + local;
            Vector3 up = PlanetWaterUtility.LocalUp(((Vector3)worldCell + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE);
            Vector3Int radialOut = Vector3Int.RoundToInt(up);
            if (radialOut == Vector3Int.zero) radialOut = Vector3Int.up;
            Vector3Int next = worldCell + radialOut;
            var world = ActiveWorld.Current;
            Voxel neighbour = world != null ? world.GetVoxelWorld(next) : Voxel.Empty;
            return FluidMaterialUtility.IsFluid(neighbour) &&
                   FluidMaterialUtility.LiquidFromVoxel(neighbour) == FluidMaterialUtility.LiquidFromVoxel(voxel);
        }

        private static Vector3Int DominantAxis(Vector3 direction)
        {
            Vector3 a = new(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3Int(direction.x >= 0f ? 1 : -1, 0, 0);
            if (a.y >= a.z) return new Vector3Int(0, direction.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, direction.z >= 0f ? 1 : -1);
        }

        private static void AddSphereSurfacePatch(Vector3 center, Vector3 normal, LiquidType liquid, Vector3 chunkVoxel, Vector2 flow,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            Vector3 tangentA = Vector3.Cross(normal, Vector3.up);
            if (tangentA.sqrMagnitude < 0.001f) tangentA = Vector3.Cross(normal, Vector3.forward);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(normal, tangentA).normalized;

            float half = 0.54f;
            int i = verts.Count;
            Vector3 p0 = center - tangentA * half - tangentB * half;
            Vector3 p1 = center + tangentA * half - tangentB * half;
            Vector3 p2 = center + tangentA * half + tangentB * half;
            Vector3 p3 = center - tangentA * half + tangentB * half;

            verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
            for (int n = 0; n < 4; n++) norms.Add(normal);

            Vector3 world0 = p0 + chunkVoxel;
            Vector3 world1 = p1 + chunkVoxel;
            Vector3 world2 = p2 + chunkVoxel;
            Vector3 world3 = p3 + chunkVoxel;
            uvs.Add(new Vector2(world0.x + world0.y * 0.37f, world0.z + world0.y * 0.19f));
            uvs.Add(new Vector2(world1.x + world1.y * 0.37f, world1.z + world1.y * 0.19f));
            uvs.Add(new Vector2(world2.x + world2.y * 0.37f, world2.z + world2.y * 0.19f));
            uvs.Add(new Vector2(world3.x + world3.y * 0.37f, world3.z + world3.y * 0.19f));

            float tide = liquid == LiquidType.Water ? PlanetWaterUtility.MoonWaveEnergy(world0 * VoxelConstants.VOXEL_SIZE) : 0.35f;
            Vector2 encodedFlow = flow + new Vector2(tide - 1f, 1f - tide) * 0.15f;
            for (int n = 0; n < 4; n++) uv2s.Add(encodedFlow);

            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        private static void CheckAndAddSphereFace(Chunk c, int x, int y, int z, int dx, int dy, int dz, Vector3 normal, 
            float wX, float wY, float wZ, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            int nx = x + dx;
            int ny = y + dy;
            int nz = z + dz;
            
            Voxel neighbor;
            if (nx >= 0 && nx < S && ny >= 0 && ny < S && nz >= 0 && nz < S)
            {
                neighbor = c.GetVoxelLocal(nx, ny, nz);
            }
            else
            {
                // Edge of chunk - just assume air for simplicity to close the mesh, or fetch from active world
                var world = ActiveWorld.Current;
                if (world != null) {
                    var wPos = new Vector3Int(Mathf.FloorToInt(wX + nx), Mathf.FloorToInt(wY + ny), Mathf.FloorToInt(wZ + nz));
                    neighbor = world.GetVoxelWorld(wPos);
                } else {
                    neighbor = new Voxel(-1, 0, 0);
                }
            }

            // Only generate face against air (density <= 0) and non-fluid
            if (neighbor.density <= 0 && !FluidMaterialUtility.IsFluid(neighbor))
            {
                int i = verts.Count;
                
                // Base corner
                Vector3 p = new Vector3(x, y, z);
                
                Vector3 t1 = Vector3.zero;
                Vector3 t2 = Vector3.zero;
                
                if (dx != 0) { t1 = new Vector3(0, 1, 0); t2 = new Vector3(0, 0, 1); p.x += (dx > 0 ? 1 : 0); }
                else if (dy != 0) { t1 = new Vector3(1, 0, 0); t2 = new Vector3(0, 0, 1); p.y += (dy > 0 ? 1 : 0); }
                else if (dz != 0) { t1 = new Vector3(1, 0, 0); t2 = new Vector3(0, 1, 0); p.z += (dz > 0 ? 1 : 0); }

                verts.Add(p);
                verts.Add(p + t1);
                verts.Add(p + t1 + t2);
                verts.Add(p + t2);

                for (int n = 0; n < 4; n++) norms.Add(normal);
                for (int n = 0; n < 4; n++) {
                    Vector3 wp = verts[verts.Count - 4 + n] + new Vector3(wX, wY, wZ);
                    uvs.Add(new Vector2(wp.x + wp.y, wp.z + wp.y));
                    uv2s.Add(Vector2.zero);
                }

                // Ensure winding is correct based on normal direction
                Vector3 cross = Vector3.Cross(t1, t2);
                if (Vector3.Dot(cross, normal) < 0)
                {
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                    tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
                }
                else
                {
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                }
            }
        }

        private static void AddCurtainQuadZ(float xA, float xB, float hTop, float hBot, float cz, Vector3 normal,
            float wX, float wZ, Vector2 flow,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            int i = verts.Count;
            verts.Add(new Vector3(xA, hTop, cz));
            verts.Add(new Vector3(xB, hTop, cz));
            verts.Add(new Vector3(xB, hBot, cz));
            verts.Add(new Vector3(xA, hBot, cz));
            for (int n = 0; n < 4; n++) norms.Add(normal);
            uvs.Add(new Vector2(wX + xA, wZ + cz));
            uvs.Add(new Vector2(wX + xB, wZ + cz));
            uvs.Add(new Vector2(wX + xB, wZ + cz));
            uvs.Add(new Vector2(wX + xA, wZ + cz));
            for (int n = 0; n < 4; n++) uv2s.Add(flow);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        private static float CornerHeight(SurfaceCell[,] cells, int x, int z, LiquidType liquid, int sx, int sz, float fallback)
        {
            float sum = fallback; int cnt = 1;
            TryAddH(cells, x + sx, z, liquid, ref sum, ref cnt);
            TryAddH(cells, x, z + sz, liquid, ref sum, ref cnt);
            TryAddH(cells, x + sx, z + sz, liquid, ref sum, ref cnt);
            TryAddH(cells, x + sx * 2, z, liquid, ref sum, ref cnt);
            TryAddH(cells, x, z + sz * 2, liquid, ref sum, ref cnt);
            return sum / cnt;
        }

        private static void TryAddH(SurfaceCell[,] cells, int x, int z, LiquidType liquid, ref float sum, ref int cnt)
        {
            if (x < 0 || x >= VoxelConstants.CHUNK_SIZE || z < 0 || z >= VoxelConstants.CHUNK_SIZE) return;
            var c = cells[x, z];
            if (!c.has || c.liquid != liquid) return;
            sum += c.h; cnt++;
        }

        private static Vector2 AverageFlow(SurfaceCell[,] cells, int x, int z, LiquidType liquid)
        {
            Vector2 sum = cells[x, z].flow; int count = 1;
            TryAddFlow(cells, x + 1, z, liquid, ref sum, ref count);
            TryAddFlow(cells, x - 1, z, liquid, ref sum, ref count);
            TryAddFlow(cells, x, z + 1, liquid, ref sum, ref count);
            TryAddFlow(cells, x, z - 1, liquid, ref sum, ref count);
            return sum / count;
        }

        private static void TryAddFlow(SurfaceCell[,] cells, int x, int z, LiquidType liquid, ref Vector2 sum, ref int count)
        {
            if (x < 0 || x >= VoxelConstants.CHUNK_SIZE || z < 0 || z >= VoxelConstants.CHUNK_SIZE) return;
            var c = cells[x, z];
            if (!c.has || c.liquid != liquid) return;
            sum += c.flow; count++;
        }

        private static float VisualSurfaceHeight(int y, byte level, LiquidType liquid)
        {
            if (level >= 16) return y + (liquid == LiquidType.CrudeOil ? 0.94f : 0.875f);
            return y + Mathf.Clamp(level / 255f, 0.08f, 0.85f);
        }

        /// <summary>
        /// Try to get the terrain surface height at a neighbor cell (in-chunk only).
        /// </summary>
        private static bool TryGetTerrainHeight(Chunk c, int nx, int ny, int nz, out float terrainH)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            terrainH = ny;

            if (nx < 0 || nx >= S || nz < 0 || nz >= S) return false;

            for (int dy = 0; dy <= 4; dy++)
            {
                int yAbove = ny + dy;
                if (yAbove >= 0 && yAbove < S && IsTerrainVoxel(c.GetVoxelLocal(nx, yAbove, nz)))
                {
                    terrainH = yAbove + 0.6f;
                    return true;
                }
                int yBelow = ny - dy;
                if (yBelow >= 0 && yBelow < S && IsTerrainVoxel(c.GetVoxelLocal(nx, yBelow, nz)))
                {
                    terrainH = yBelow + 0.6f;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if there's terrain at a position in an adjacent chunk.
        /// dx/dz are offsets from the chunk edge: -1 = just outside the -X/Z face,
        /// S = just outside the +X/Z face.
        /// </summary>
        private static bool IsTerrainInAdjacentChunk(Chunk c, int dx, int dy, int dz)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return false;

            const int S = VoxelConstants.CHUNK_SIZE;

            // Convert to world voxel coordinates
            int wx = c.coord.x * S + dx;
            int wy = c.coord.y * S + dy;
            int wz = c.coord.z * S + dz;

            var adjCoord = new Vector3Int(
                Mathf.FloorToInt(wx / (float)S),
                Mathf.FloorToInt(wy / (float)S),
                Mathf.FloorToInt(wz / (float)S));

            if (!world.TryGetChunk(adjCoord, out var adjChunk) || !adjChunk.isGenerated) return false;

            int lx = wx - adjCoord.x * S;
            int ly = wy - adjCoord.y * S;
            int lz = wz - adjCoord.z * S;

            if (lx < 0 || lx >= S || ly < 0 || ly >= S || lz < 0 || lz >= S) return false;

            // Check a vertical range for terrain (same logic as TerrainSolidNear)
            for (int yy = ly + 3; yy >= ly - 5; yy--)
            {
                if (yy < 0 || yy >= S) continue;
                if (IsTerrainVoxel(adjChunk.GetVoxelLocal(lx, yy, lz))) return true;
            }
            return false;
        }

        /// <summary>
        /// True if there's non-fluid solid terrain near a position (in-chunk only).
        /// </summary>
        private static bool TerrainSolidNear(Chunk c, int x, int y, int z)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            if (x < 0 || x >= S || z < 0 || z >= S) return false;
            for (int yy = y + 3; yy >= y - 5; yy--)
            {
                if (yy < 0 || yy >= S) continue;
                if (IsTerrainVoxel(c.GetVoxelLocal(x, yy, z))) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the voxel is solid AND not a fluid material.
        /// </summary>
        private static bool IsTerrainVoxel(Voxel v)
        {
            if (v.density <= 0) return false;
            byte mat = v.material;
            return mat != WaterVoxelMat && mat != WaterLiquidMat && mat != OilMat;
        }

        /// <summary>
        /// True if there's non-fluid solid terrain above the given position.
        /// </summary>
        private static bool HasTerrainAbove(Chunk c, int x, int startY, int z)
        {
            for (int y = startY; y <= VoxelConstants.CHUNK_SIZE; y++)
                if (IsTerrainVoxel(c.GetVoxelLocal(x, y, z))) return true;
            return false;
        }

        private static void ClearGO(Chunk c) { if (c.waterMeshGO != null) c.waterMeshGO.SetActive(false); }

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
