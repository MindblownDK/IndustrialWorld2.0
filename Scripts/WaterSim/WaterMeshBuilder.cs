// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// Unified 3D column-stitched fluid mesher for spherical planets and flat worlds.
//
// Generates continuous, connected heightfield meshes from voxel liquid data
// (WaterLiquid and CrudeOil) without any disconnected blocky quads or gaps.
// Shares corner heights across adjacent columns and tucks coastal boundaries
// underneath sloping terrain for smooth organic coastlines.

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
        private static Material _externalWaterMat;
        private static Material _externalOilMat;
        private static readonly HashSet<Vector3Int> _sphereSurfaceCells = new();

        public static bool RenderingEnabled { get; set; } = true;

        private const byte WaterVoxelMat  = (byte)MaterialId.WaterVoxel;
        private const byte WaterLiquidMat = (byte)MaterialId.WaterLiquid;
        private const byte OilMat         = (byte)MaterialId.CrudeOil;

        private struct SurfaceCell
        {
            public bool has;
            public LiquidType liquid;
            public float h;
            public int y;
            public Vector2 flow;
            public bool bordersTerrain;
            public float terrainH;
        }

        private readonly struct VertexKey : System.IEquatable<VertexKey>
        {
            private readonly ushort _u;
            private readonly ushort _v;
            private readonly byte _liquid;

            public VertexKey(int u, int v, LiquidType liquid)
            {
                _u = (ushort)Mathf.Clamp(u, 0, VoxelConstants.CHUNK_SIZE);
                _v = (ushort)Mathf.Clamp(v, 0, VoxelConstants.CHUNK_SIZE);
                _liquid = (byte)liquid;
            }

            public bool Equals(VertexKey other) => _u == other._u && _v == other._v && _liquid == other._liquid;
            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode() => (_u * 73856093) ^ (_v * 19349663) ^ (_liquid * 83492791);
        }

        public static void ResetForNewWorld()
        {
            _queue.Clear();
            _queued.Clear();
            _sphereSurfaceCells.Clear();
            if (_waterMat != null && _waterMat != _externalWaterMat) { if (Application.isPlaying) Object.Destroy(_waterMat); else Object.DestroyImmediate(_waterMat); }
            if (_oilMat != null && _oilMat != _externalOilMat) { if (Application.isPlaying) Object.Destroy(_oilMat); else Object.DestroyImmediate(_oilMat); }
            _waterMat = _externalWaterMat;
            _oilMat = _externalOilMat;
        }

        public static void Schedule(Chunk c)
        {
            if (!RenderingEnabled) return;
            if (c != null && _queued.Add(c)) _queue.Enqueue(c);
        }

        public static void Pump(int budget)
        {
            if (!RenderingEnabled)
            {
                _queue.Clear();
                _queued.Clear();
                return;
            }
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

        public static void SetMaterialOverrides(Material waterMaterial, Material oilMaterial)
        {
            var previousExternalWater = _externalWaterMat;
            var previousExternalOil = _externalOilMat;

            _externalWaterMat = waterMaterial;
            _externalOilMat = oilMaterial;

            if (waterMaterial != null)
                _waterMat = waterMaterial;
            else if (previousExternalWater != null && _waterMat == previousExternalWater)
                _waterMat = null;

            if (oilMaterial != null)
                _oilMat = oilMaterial;
            else if (previousExternalOil != null && _oilMat == previousExternalOil)
                _oilMat = null;
        }

        private static void EnsureMats()
        {
            if (_externalWaterMat != null) _waterMat = _externalWaterMat;
            if (_externalOilMat != null) _oilMat = _externalOilMat;
            if (_waterMat != null && _oilMat != null) return;

            var sh = Shader.Find("VoxelEngine/VoxelWaterURP")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");

            if (_waterMat == null)
            {
                _waterMat = new Material(sh) { name = "VoxelWater_KWS2" };
                ConfigureTransparent(_waterMat);
                _waterMat.SetColor("_ShallowColor", new Color(0.08f, 0.52f, 0.82f, 0.97f));
                _waterMat.SetColor("_DeepColor",    new Color(0.01f, 0.06f, 0.22f, 0.995f));
                _waterMat.SetColor("_FoamColor",    new Color(0.92f, 0.96f, 1.00f, 0.88f));
                _waterMat.SetFloat("_DeepWaveAmplitude", 0.85f);
                _waterMat.SetFloat("_DeepWaveFrequency", 0.22f);
                _waterMat.SetFloat("_DeepWaveSpeed", 0.55f);
                _waterMat.SetFloat("_SecondaryWaveAmplitude", 0.35f);
                _waterMat.SetFloat("_SecondaryWaveFrequency", 0.47f);
                _waterMat.SetFloat("_SecondaryWaveSpeed", 0.91f);
                _waterMat.SetFloat("_ShallowWaveAmplitude", 0.16f);
                _waterMat.SetFloat("_ShallowWaveFrequency", 1.65f);
                _waterMat.SetFloat("_ShallowWaveSpeed", 1.8f);
                _waterMat.SetFloat("_ShoreBlendDistance", 2.5f);
                _waterMat.SetFloat("_WaveChop", 0.28f);
                _waterMat.SetFloat("_NormalScale", 2.4f);
                _waterMat.SetFloat("_Gloss", 0.94f);
                _waterMat.SetFloat("_FresnelPower", 3.5f);
                _waterMat.SetFloat("_RefractionStrength", 0.045f);
                _waterMat.SetFloat("_CausticsIntensity", 0.45f);
                _waterMat.SetFloat("_DepthFade", 2.5f);
                _waterMat.SetFloat("_ShoreOpaqueDepth", 1.5f);
                _waterMat.SetFloat("_ShoreFoamWidth", 2.0f);
                _waterMat.SetFloat("_ShoreFoamIntensity", 1.2f);
                _waterMat.SetFloat("_SSSIntensity", 0.45f);
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
                _oilMat.SetFloat("_DeepWaveAmplitude", 0.04f);
                _oilMat.SetFloat("_DeepWaveFrequency", 0.40f);
                _oilMat.SetFloat("_DeepWaveSpeed", 0.12f);
                _oilMat.SetFloat("_SecondaryWaveAmplitude", 0.025f);
                _oilMat.SetFloat("_SecondaryWaveFrequency", 0.85f);
                _oilMat.SetFloat("_SecondaryWaveSpeed", 0.08f);
                _oilMat.SetFloat("_ShallowWaveAmplitude", 0.015f);
                _oilMat.SetFloat("_ShallowWaveFrequency", 1.1f);
                _oilMat.SetFloat("_ShallowWaveSpeed", 0.16f);
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

        public static Material GetWaterMaterial()
        {
            EnsureMats();
            return _waterMat;
        }

        private static void ConfigureTransparent(Material mat)
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetFloat("_Surface", 1.0f);
            mat.SetFloat("_Blend", 0.0f);
            mat.SetColor("_BaseColor", new Color(0.08f, 0.52f, 0.82f, 0.88f));
            mat.SetColor("_Color", new Color(0.08f, 0.52f, 0.82f, 0.88f));
        }

        private static void Build(Chunk c)
        {
            BuildSphere(c);
        }

        private static void BuildSphere(Chunk c)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            EnsureGO(c);
            FlowFieldManager.UpdateFlowField(c);

            Vector3 chunkCenterWorld = ((Vector3)c.coord + Vector3.one * 0.5f) * S;
            Vector3 chunkUp = PlanetWaterUtility.LocalUp(chunkCenterWorld * VoxelConstants.VOXEL_SIZE);
            Vector3Int dom = DominantAxis(chunkUp);

            var cells = new SurfaceCell[S, S];
            bool any = false;

            var world = ActiveWorld.Current;
            float seaRad = (world != null ? world.SeaLevel : 96) * VoxelConstants.VOXEL_SIZE;
            Vector3 chunkVoxel = (Vector3)(c.coord * S);

            for (int v = 0; v < S; v++)
            for (int u = 0; u < S; u++)
            {
                for (int h = S - 1; h >= 0; h--)
                {
                    Vector3Int local = ToXYZ(u, v, h, dom, S);
                    var vox = c.GetVoxelLocal(local.x, local.y, local.z);
                    if (!FluidMaterialUtility.IsFluid(vox)) continue;

                    if (h + 1 < S)
                    {
                        Vector3Int aboveLocal = ToXYZ(u, v, h + 1, dom, S);
                        var aboveVox = c.GetVoxelLocal(aboveLocal.x, aboveLocal.y, aboveLocal.z);
                        if (FluidMaterialUtility.IsFluid(aboveVox) &&
                            FluidMaterialUtility.LiquidFromVoxel(aboveVox) == FluidMaterialUtility.LiquidFromVoxel(vox))
                            continue;
                    }
                    else
                    {
                        Vector3 centerV = chunkVoxel + (Vector3)local + Vector3.one * 0.5f;
                        if (IsCoveredBySameLiquid(c, local, vox, centerV.magnitude, seaRad))
                            continue;
                    }

                    var liquid = FluidMaterialUtility.LiquidFromVoxel(vox);
                    float baseH = h + (vox.waterLevel / 255f);

                    bool bordersTerrain = false;
                    float terrainH = baseH;
                    float tH;
                    if (TryGetTerrainH(c, u - 1, v, h, dom, S, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }
                    if (TryGetTerrainH(c, u + 1, v, h, dom, S, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }
                    if (TryGetTerrainH(c, u, v - 1, h, dom, S, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }
                    if (TryGetTerrainH(c, u, v + 1, h, dom, S, out tH)) { bordersTerrain = true; terrainH = Mathf.Min(terrainH, tH); }

                    float finalH = baseH;
                    if (bordersTerrain) finalH = Mathf.Min(baseH, terrainH + 0.12f);

                    cells[u, v] = new SurfaceCell
                    {
                        has = true,
                        liquid = liquid,
                        h = finalH,
                        y = h,
                        flow = c.GetFlow(local.x, local.z),
                        bordersTerrain = bordersTerrain,
                        terrainH = terrainH
                    };
                    any = true;
                    break;
                }
            }

            if (!any) { ClearGO(c); return; }

            SmoothHeightField(cells, S);

            var verts     = new List<Vector3>(S * S * 6);
            var norms     = new List<Vector3>(S * S * 6);
            var uvs       = new List<Vector2>(S * S * 6);
            var uv2s      = new List<Vector2>(S * S * 6);
            var cols      = new List<Color>(S * S * 6);
            var waterTris = new List<int>(S * S * 6);
            var oilTris   = new List<int>(S * S * 6);
            var topVertexCache = new Dictionary<VertexKey, int>(S * S * 2);

            int lodStride = Mathf.Clamp(GetChunkLodStride(c), 1, 8);
            for (int v = 0; v < S; v += lodStride)
            for (int u = 0; u < S; u += lodStride)
            {
                var cell = lodStride == 1 ? cells[u, v] : SampleLodCell(cells, u, v, lodStride, S);
                if (!cell.has) continue;

                var tris = cell.liquid == LiquidType.CrudeOil ? oilTris : waterTris;
                if (lodStride == 1)
                {
                    AddTopConnected(c, cells, u, v, cell, dom, S, chunkVoxel, chunkUp, topVertexCache, verts, norms, uvs, uv2s, cols, tris);
                    // Visual liquid is now top-surface only. The simulation still uses
                    // voxel volumes, but rendering side curtains made shore/chunk edges
                    // look like vertical water walls with stylized water materials.
                }
                else
                {
                    AddTopSpherical(c, cells, u, v, Mathf.Min(u + lodStride, S), Mathf.Min(v + lodStride, S), cell, dom, S, chunkVoxel, chunkUp, seaRad, verts, norms, uvs, uv2s, cols, tris);
                }
            }

            if (verts.Count == 0) { ClearGO(c); return; }

            if (c.waterMesh == null) c.waterMesh = new Mesh { name = "LiquidSurface" };
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

        public static int GetChunkLodStride(Chunk c)
        {
            // Quality-first mode: keep generated liquid surfaces fully connected.
            // Coarse geometry LOD caused visible square patches with stylized/transparent
            // water shaders, so distance LOD is intentionally disabled until the next
            // dedicated ocean-tile renderer pass.
            return 1;
        }

        private static SurfaceCell SampleLodCell(SurfaceCell[,] cells, int startU, int startV, int stride, int S)
        {
            int waterCount = 0, oilCount = 0;
            float waterH = 0f, oilH = 0f;
            int waterY = 0, oilY = 0;
            Vector2 waterFlow = Vector2.zero, oilFlow = Vector2.zero;
            bool waterBorder = false, oilBorder = false;
            float waterTerrain = float.MaxValue, oilTerrain = float.MaxValue;

            int endU = Mathf.Min(startU + stride, S);
            int endV = Mathf.Min(startV + stride, S);
            for (int v = startV; v < endV; v++)
            for (int u = startU; u < endU; u++)
            {
                var c = cells[u, v];
                if (!c.has) continue;

                if (c.liquid == LiquidType.CrudeOil)
                {
                    oilCount++; oilH += c.h; oilY += c.y; oilFlow += c.flow;
                    oilBorder |= c.bordersTerrain; oilTerrain = Mathf.Min(oilTerrain, c.terrainH);
                }
                else
                {
                    waterCount++; waterH += c.h; waterY += c.y; waterFlow += c.flow;
                    waterBorder |= c.bordersTerrain; waterTerrain = Mathf.Min(waterTerrain, c.terrainH);
                }
            }

            if (waterCount == 0 && oilCount == 0) return default;
            bool useOil = oilCount > waterCount;
            int count = useOil ? oilCount : waterCount;
            return new SurfaceCell
            {
                has = true,
                liquid = useOil ? LiquidType.CrudeOil : LiquidType.Water,
                h = (useOil ? oilH : waterH) / Mathf.Max(1, count),
                y = Mathf.RoundToInt((useOil ? oilY : waterY) / Mathf.Max(1f, count)),
                flow = (useOil ? oilFlow : waterFlow) / Mathf.Max(1, count),
                bordersTerrain = useOil ? oilBorder : waterBorder,
                terrainH = useOil ? oilTerrain : waterTerrain
            };
        }

        private static void SmoothHeightField(SurfaceCell[,] cells, int S)
        {
            var tempH = new float[S, S];
            var tempW = new float[S, S];

            for (int v = 0; v < S; v++)
            for (int u = 0; u < S; u++)
            {
                var cell = cells[u, v];
                if (!cell.has) continue;

                if (u == 0 || u == S - 1 || v == 0 || v == S - 1)
                {
                    tempH[u, v] = cell.h;
                    tempW[u, v] = 1f;
                    continue;
                }

                float sumH = 0f, sumW = 0f;
                for (int dv = -2; dv <= 2; dv++)
                for (int du = -2; du <= 2; du++)
                {
                    int nu = u + du, nv = v + dv;
                    if (nu < 0 || nu >= S || nv < 0 || nv >= S) continue;
                    var n = cells[nu, nv];
                    if (!n.has || n.liquid != cell.liquid) continue;
                    float d2 = du * du + dv * dv;
                    float w = 1f / (1f + d2 * 0.5f);
                    sumH += n.h * w;
                    sumW += w;
                }
                tempH[u, v] = sumW > 0f ? sumH / sumW : cell.h;
                tempW[u, v] = sumW;
            }

            for (int v = 0; v < S; v++)
            for (int u = 0; u < S; u++)
            {
                if (tempW[u, v] > 0f) cells[u, v].h = tempH[u, v];
            }
        }

        private static void AddTopConnected(Chunk c, SurfaceCell[,] cells, int u, int v, SurfaceCell cell, Vector3Int dom, int S, Vector3 chunkVoxel, Vector3 chunkUp,
            Dictionary<VertexKey, int> cache, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<Color> cols, List<int> tris)
        {
            int i00 = GetOrCreateTopVertex(c, cells, u,     v,     cell, dom, S, chunkVoxel, chunkUp, cache, verts, norms, uvs, uv2s, cols);
            int i10 = GetOrCreateTopVertex(c, cells, u + 1, v,     cell, dom, S, chunkVoxel, chunkUp, cache, verts, norms, uvs, uv2s, cols);
            int i11 = GetOrCreateTopVertex(c, cells, u + 1, v + 1, cell, dom, S, chunkVoxel, chunkUp, cache, verts, norms, uvs, uv2s, cols);
            int i01 = GetOrCreateTopVertex(c, cells, u,     v + 1, cell, dom, S, chunkVoxel, chunkUp, cache, verts, norms, uvs, uv2s, cols);

            tris.Add(i00); tris.Add(i11); tris.Add(i10);
            tris.Add(i00); tris.Add(i01); tris.Add(i11);
        }

        private static int GetOrCreateTopVertex(Chunk c, SurfaceCell[,] cells, int cornerU, int cornerV, SurfaceCell owner, Vector3Int dom, int S, Vector3 chunkVoxel, Vector3 chunkUp,
            Dictionary<VertexKey, int> cache, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<Color> cols)
        {
            var key = new VertexKey(cornerU, cornerV, owner.liquid);
            if (cache.TryGetValue(key, out int existing)) return existing;

            CornerData(cells, cornerU, cornerV, owner.liquid, owner.h, owner.flow, out float h, out Vector2 flow, out bool shallow);
            Vector3 localPt = ToXYZFloat(cornerU, cornerV, h, dom, S);
            Vector3 worldPt = localPt + chunkVoxel;
            Vector3 norm = worldPt.sqrMagnitude > 0.001f ? worldPt.normalized : chunkUp;

            int index = verts.Count;
            verts.Add(localPt);
            norms.Add(norm);
            uvs.Add(new Vector2(worldPt.x * 0.37f + worldPt.y * 0.19f, worldPt.z + worldPt.y * 0.19f));
            float tide = owner.liquid == LiquidType.Water ? PlanetWaterUtility.MoonWaveEnergy(worldPt) : 0.35f;
            uv2s.Add(flow + new Vector2(tide - 1f, 1f - tide) * 0.15f);
            cols.Add(new Color(shallow ? 0.35f : 1f, 1f, 1f, 1f));
            cache[key] = index;
            return index;
        }

        private static void CornerData(SurfaceCell[,] cells, int cornerU, int cornerV, LiquidType liquid, float fallbackH, Vector2 fallbackFlow,
            out float height, out Vector2 flow, out bool shallow)
        {
            float sumH = 0f;
            Vector2 sumFlow = Vector2.zero;
            int count = 0;
            bool anyBorder = false;

            AccumulateCornerCell(cells, cornerU - 1, cornerV - 1, liquid, ref sumH, ref sumFlow, ref count, ref anyBorder);
            AccumulateCornerCell(cells, cornerU,     cornerV - 1, liquid, ref sumH, ref sumFlow, ref count, ref anyBorder);
            AccumulateCornerCell(cells, cornerU - 1, cornerV,     liquid, ref sumH, ref sumFlow, ref count, ref anyBorder);
            AccumulateCornerCell(cells, cornerU,     cornerV,     liquid, ref sumH, ref sumFlow, ref count, ref anyBorder);

            if (count == 0)
            {
                height = fallbackH;
                flow = fallbackFlow;
                shallow = true;
                return;
            }

            height = sumH / count;
            flow = sumFlow / count;
            shallow = anyBorder;
        }

        private static void AccumulateCornerCell(SurfaceCell[,] cells, int u, int v, LiquidType liquid, ref float sumH, ref Vector2 sumFlow, ref int count, ref bool anyBorder)
        {
            if (u < 0 || u >= VoxelConstants.CHUNK_SIZE || v < 0 || v >= VoxelConstants.CHUNK_SIZE) return;
            var c = cells[u, v];
            if (!c.has || c.liquid != liquid) return;
            sumH += c.h;
            sumFlow += c.flow;
            count++;
            anyBorder |= c.bordersTerrain;
        }

        private static void AddTopSpherical(Chunk c, SurfaceCell[,] cells, int u, int v, int uEnd, int vEnd, SurfaceCell cell, Vector3Int dom, int S, Vector3 chunkVoxel, Vector3 chunkUp, float seaRad,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<Color> cols, List<int> tris)
        {
            float h00 = CornerHeight(cells, u, v, cell.liquid, -1, -1, cell.h);
            float h10 = CornerHeight(cells, Mathf.Max(uEnd - 1, u), v, cell.liquid,  1, -1, cell.h);
            float h11 = CornerHeight(cells, Mathf.Max(uEnd - 1, u), Mathf.Max(vEnd - 1, v), cell.liquid,  1,  1, cell.h);
            float h01 = CornerHeight(cells, u, Mathf.Max(vEnd - 1, v), cell.liquid, -1,  1, cell.h);

            float u0 = u, u1 = uEnd, v0 = v, v1 = vEnd;
            float tuck = cell.liquid == LiquidType.CrudeOil ? 0.30f : 0.85f;

            if (TryGetTerrainH(c, u - 1, v, cell.y, dom, S, out _)) u0 -= tuck;
            if (TryGetTerrainH(c, uEnd, v, cell.y, dom, S, out _)) u1 += tuck;
            if (TryGetTerrainH(c, u, v - 1, cell.y, dom, S, out _)) v0 -= tuck;
            if (TryGetTerrainH(c, u, vEnd, cell.y, dom, S, out _)) v1 += tuck;

            Vector3 local00 = ToXYZFloat(u0, v0, h00, dom, S);
            Vector3 local10 = ToXYZFloat(u1, v0, h10, dom, S);
            Vector3 local11 = ToXYZFloat(u1, v1, h11, dom, S);
            Vector3 local01 = ToXYZFloat(u0, v1, h01, dom, S);

            int i = verts.Count;
            verts.Add(local00); verts.Add(local10); verts.Add(local11); verts.Add(local01);

            for (int n = 0; n < 4; n++)
            {
                Vector3 localPt = verts[i + n];
                Vector3 worldPt = localPt + chunkVoxel;
                Vector3 norm = worldPt.sqrMagnitude > 0.001f ? worldPt.normalized : chunkUp;
                norms.Add(norm);
                uvs.Add(new Vector2(worldPt.x * 0.37f + worldPt.y * 0.19f, worldPt.z + worldPt.y * 0.19f));
                float tide = cell.liquid == LiquidType.Water ? PlanetWaterUtility.MoonWaveEnergy(worldPt) : 0.35f;
                uv2s.Add(cell.flow + new Vector2(tide - 1f, 1f - tide) * 0.15f);
                
                float depthToTerrain = 1.0f;
                Vector3Int checkBelow = Vector3Int.RoundToInt(localPt - norm * 1.5f);
                if (checkBelow.x >= 0 && checkBelow.x < S && checkBelow.y >= 0 && checkBelow.y < S && checkBelow.z >= 0 && checkBelow.z < S)
                {
                    if (c.GetVoxelLocal(checkBelow.x, checkBelow.y, checkBelow.z).IsSolid) depthToTerrain = 0.35f;
                }
                cols.Add(new Color(depthToTerrain, 1f, 1f, 1f));
            }

            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static void AddSideCurtainsSpherical(Chunk c, SurfaceCell[,] cells, int u, int v, Vector3Int dom, int S, Vector3 chunkVoxel, Vector3 chunkUp, float seaRad,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<Color> cols, List<int> tris)
        {
            var cell = cells[u, v];
            float hTop = cell.h;
            Vector2 flow = cell.flow;

            bool terrainNegU = u > 0 ? TryGetTerrainH(c, u - 1, v, cell.y, dom, S, out _) : false;
            if (terrainNegU)
            {
                float tH;
                float hBot = (u > 0 && TryGetTerrainH(c, u - 1, v, cell.y, dom, S, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuad(u, v + 1, v, hTop, hBot, dom, S, chunkVoxel, chunkUp, flow, verts, norms, uvs, uv2s, cols, tris);
            }
            bool terrainPosU = u < S - 1 ? TryGetTerrainH(c, u + 1, v, cell.y, dom, S, out _) : false;
            if (terrainPosU)
            {
                float tH;
                float hBot = (u < S - 1 && TryGetTerrainH(c, u + 1, v, cell.y, dom, S, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuad(u + 1, v, v + 1, hTop, hBot, dom, S, chunkVoxel, chunkUp, flow, verts, norms, uvs, uv2s, cols, tris);
            }
            bool terrainNegV = v > 0 ? TryGetTerrainH(c, u, v - 1, cell.y, dom, S, out _) : false;
            if (terrainNegV)
            {
                float tH;
                float hBot = (v > 0 && TryGetTerrainH(c, u, v - 1, cell.y, dom, S, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuadV(u, u + 1, v, hTop, hBot, dom, S, chunkVoxel, chunkUp, flow, verts, norms, uvs, uv2s, cols, tris);
            }
            bool terrainPosV = v < S - 1 ? TryGetTerrainH(c, u, v + 1, cell.y, dom, S, out _) : false;
            if (terrainPosV)
            {
                float tH;
                float hBot = (v < S - 1 && TryGetTerrainH(c, u, v + 1, cell.y, dom, S, out tH)) ? tH : cell.y;
                if (hTop - hBot > 0.02f)
                    AddCurtainQuadV(u + 1, u, v + 1, hTop, hBot, dom, S, chunkVoxel, chunkUp, flow, verts, norms, uvs, uv2s, cols, tris);
            }
        }

        private static void AddCurtainQuad(float cu, float vA, float vB, float hTop, float hBot, Vector3Int dom, int S, Vector3 chunkVoxel, Vector3 chunkUp, Vector2 flow,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<Color> cols, List<int> tris)
        {
            int i = verts.Count;
            Vector3 pt0 = ToXYZFloat(cu, vA, hTop, dom, S);
            Vector3 pt1 = ToXYZFloat(cu, vB, hTop, dom, S);
            Vector3 pt2 = ToXYZFloat(cu, vB, hBot, dom, S);
            Vector3 pt3 = ToXYZFloat(cu, vA, hBot, dom, S);
            verts.Add(pt0); verts.Add(pt1); verts.Add(pt2); verts.Add(pt3);
            for (int n = 0; n < 4; n++)
            {
                Vector3 pt = verts[i + n] + chunkVoxel;
                Vector3 norm = pt.sqrMagnitude > 0.001f ? pt.normalized : chunkUp;
                norms.Add(norm);
                uvs.Add(new Vector2(pt.x * 0.37f + pt.y * 0.19f, pt.z + pt.y * 0.19f));
                uv2s.Add(flow);
                cols.Add(Color.white);
            }
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static void AddCurtainQuadV(float uA, float uB, float cv, float hTop, float hBot, Vector3Int dom, int S, Vector3 chunkVoxel, Vector3 chunkUp, Vector2 flow,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<Color> cols, List<int> tris)
        {
            int i = verts.Count;
            Vector3 pt0 = ToXYZFloat(uA, cv, hTop, dom, S);
            Vector3 pt1 = ToXYZFloat(uB, cv, hTop, dom, S);
            Vector3 pt2 = ToXYZFloat(uB, cv, hBot, dom, S);
            Vector3 pt3 = ToXYZFloat(uA, cv, hBot, dom, S);
            verts.Add(pt0); verts.Add(pt1); verts.Add(pt2); verts.Add(pt3);
            for (int n = 0; n < 4; n++)
            {
                Vector3 pt = verts[i + n] + chunkVoxel;
                Vector3 norm = pt.sqrMagnitude > 0.001f ? pt.normalized : chunkUp;
                norms.Add(norm);
                uvs.Add(new Vector2(pt.x * 0.37f + pt.y * 0.19f, pt.z + pt.y * 0.19f));
                uv2s.Add(flow);
                cols.Add(Color.white);
            }
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static float CornerHeight(SurfaceCell[,] cells, int u, int v, LiquidType liquid, int su, int sv, float fallback)
        {
            float sum = fallback; int cnt = 1;
            TryAddH(cells, u + su, v, liquid, ref sum, ref cnt);
            TryAddH(cells, u, v + sv, liquid, ref sum, ref cnt);
            TryAddH(cells, u + su, v + sv, liquid, ref sum, ref cnt);
            TryAddH(cells, u + su * 2, v, liquid, ref sum, ref cnt);
            TryAddH(cells, u, v + sv * 2, liquid, ref sum, ref cnt);
            return sum / cnt;
        }

        private static void TryAddH(SurfaceCell[,] cells, int u, int v, LiquidType liquid, ref float sum, ref int cnt)
        {
            if (u < 0 || u >= VoxelConstants.CHUNK_SIZE || v < 0 || v >= VoxelConstants.CHUNK_SIZE) return;
            var c = cells[u, v];
            if (!c.has || c.liquid != liquid) return;
            sum += c.h; cnt++;
        }

        private static Vector3Int ToXYZ(int u, int v, int h, Vector3Int dom, int S)
        {
            if (dom.y > 0) return new Vector3Int(u, h, v);
            if (dom.y < 0) return new Vector3Int(u, S - 1 - h, v);
            if (dom.x > 0) return new Vector3Int(h, u, v);
            if (dom.x < 0) return new Vector3Int(S - 1 - h, u, v);
            if (dom.z > 0) return new Vector3Int(u, v, h);
            return new Vector3Int(u, v, S - 1 - h);
        }

        private static Vector3 ToXYZFloat(float u, float v, float h, Vector3Int dom, int S)
        {
            if (dom.y > 0) return new Vector3(u, h, v);
            if (dom.y < 0) return new Vector3(u, S - 1f - h, v);
            if (dom.x > 0) return new Vector3(h, u, v);
            if (dom.x < 0) return new Vector3(S - 1f - h, u, v);
            if (dom.z > 0) return new Vector3(u, v, h);
            return new Vector3(u, v, S - 1f - h);
        }

        private static bool TryGetTerrainH(Chunk c, int u, int v, int h, Vector3Int dom, int S, out float terrainH)
        {
            terrainH = 0f;
            if (u < 0 || u >= S || v < 0 || v >= S) return false;
            for (int checkH = h + 2; checkH >= h - 2; checkH--)
            {
                if (checkH < 0 || checkH >= S) continue;
                Vector3Int loc = ToXYZ(u, v, checkH, dom, S);
                if (IsTerrainVoxel(c.GetVoxelLocal(loc.x, loc.y, loc.z)))
                {
                    terrainH = checkH;
                    return true;
                }
            }
            return false;
        }

        private static bool IsCoveredBySameLiquid(Chunk c, Vector3Int local, Voxel voxel, float distFromCenter, float seaRad)
        {
            if (FluidMaterialUtility.LiquidFromVoxel(voxel) == LiquidType.Water)
            {
                Vector3Int worldCell = c.coord * VoxelConstants.CHUNK_SIZE + local;
                Vector3 up = PlanetWaterUtility.LocalUp(((Vector3)worldCell + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE);
                Vector3Int radialOut = DominantAxis(up);
                if (radialOut == Vector3Int.zero) radialOut = Vector3Int.up;
                Vector3Int next = worldCell + radialOut;
                var world = ActiveWorld.Current;
                if (world == null) return false;
                Voxel neighbour = world.GetVoxelWorld(next);
                if (!FluidMaterialUtility.IsFluid(neighbour) || FluidMaterialUtility.LiquidFromVoxel(neighbour) != LiquidType.Water)
                    return false;

                float nextDist = ((Vector3)next + Vector3.one * 0.5f).magnitude * VoxelConstants.VOXEL_SIZE;
                return (nextDist > distFromCenter + 0.45f);
            }
            else
            {
                Vector3Int worldCell = c.coord * VoxelConstants.CHUNK_SIZE + local;
                Vector3 up = PlanetWaterUtility.LocalUp(((Vector3)worldCell + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE);
                Vector3Int radialOut = DominantAxis(up);
                if (radialOut == Vector3Int.zero) radialOut = Vector3Int.up;
                Vector3Int next = worldCell + radialOut;
                var world = ActiveWorld.Current;
                Voxel neighbour = world != null ? world.GetVoxelWorld(next) : Voxel.Empty;
                return FluidMaterialUtility.IsFluid(neighbour) &&
                       FluidMaterialUtility.LiquidFromVoxel(neighbour) == FluidMaterialUtility.LiquidFromVoxel(voxel);
            }
        }

        private static Vector3Int DominantAxis(Vector3 direction)
        {
            Vector3 a = new(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3Int(direction.x >= 0f ? 1 : -1, 0, 0);
            if (a.y >= a.z) return new Vector3Int(0, direction.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, direction.z >= 0f ? 1 : -1);
        }

        private static bool IsTerrainVoxel(Voxel v)
        {
            if (v.density <= 0) return false;
            byte mat = v.material;
            return mat != WaterVoxelMat && mat != WaterLiquidMat && mat != OilMat;
        }

        private static void ClearGO(Chunk c) { if (c.waterMeshGO != null) c.waterMeshGO.SetActive(false); }

        private static void EnsureGO(Chunk c)
        {
            if (c.waterMeshGO != null)
            {
                foreach (var col in c.waterMeshGO.GetComponents<Collider>()) Object.Destroy(col);
                var existingLod = c.waterMeshGO.GetComponent<WaterSurfaceLodController>();
                if (existingLod != null) existingLod.Configure(c);
                return;
            }
            c.waterMeshGO = new GameObject("LiquidSurface");
            c.waterMeshGO.transform.SetParent(c.go.transform, false);
            c.waterMeshFilter = c.waterMeshGO.AddComponent<MeshFilter>();
            c.waterMeshRenderer = c.waterMeshGO.AddComponent<MeshRenderer>();
            c.waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            c.waterMeshRenderer.receiveShadows = false;
            c.waterMeshGO.AddComponent<WaterSurfaceLodController>().Configure(c);
        }
    }
}
