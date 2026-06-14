// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// V4: Smooth continuous water surface — no double layers, no chunk-edge artifacts.
//
// Core strategy:
//   • Water is a single thin sheet that overlaps UNDER the terrain mesh.
//     Since terrain is opaque and rendered first, it covers the overlap.
//     The water surface visually "touches" the terrain edge seamlessly.
//   • NO curtain geometry (eliminates double-layer problem).
//   • NO shore tuck / overlap at chunk boundaries (eliminates chunk-edge foam).
//   • Gaussian-smoothed height field for a continuous organic surface.
//   • At terrain contact points INSIDE the chunk, water quads extend well
//     past the terrain boundary (0.8f+) so the intersection is hidden.
//   • UV2 encodes flow direction + speed for shader flow mapping.

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
            public float h;        // raw visual surface height
            public float smoothH;  // Gaussian-smoothed height
            public int y;
            public Vector2 flow;
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
                _waterMat.SetColor("_ShallowColor", new Color(0.08f, 0.52f, 0.82f, 0.65f));
                _waterMat.SetColor("_DeepColor",    new Color(0.01f, 0.06f, 0.22f, 0.92f));
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
                _waterMat.SetFloat("_FoamIntensity", 1.2f);
                _waterMat.SetFloat("_DepthFade", 5.0f);
                _waterMat.SetFloat("_FoamWidth", 1.0f);
                _waterMat.SetFloat("_SSSIntensity", 0.35f);
                _waterMat.SetFloat("_FlowNormalStrength", 1.0f);
                _waterMat.SetFloat("_FlowFoamStrength", 0.8f);
            }

            if (_oilMat == null)
            {
                _oilMat = new Material(sh) { name = "VoxelCrudeOil_Viscous" };
                ConfigureTransparent(_oilMat);
                _oilMat.SetColor("_ShallowColor", new Color(0.12f, 0.085f, 0.05f, 0.88f));
                _oilMat.SetColor("_DeepColor",    new Color(0.02f, 0.015f, 0.01f, 0.97f));
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
                _oilMat.SetFloat("_FoamIntensity", 0.12f);
                _oilMat.SetFloat("_DepthFade", 1.8f);
                _oilMat.SetFloat("_FoamWidth", 0.15f);
                _oilMat.SetFloat("_SSSIntensity", 0.0f);
                _oilMat.SetFloat("_FlowNormalStrength", 0.3f);
                _oilMat.SetFloat("_FlowFoamStrength", 0.2f);
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

        // ═══════════════════════════════════════════════════════════════════════
        //  BUILD
        // ═══════════════════════════════════════════════════════════════════════

        private static void Build(Chunk c)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            EnsureGO(c);
            FlowFieldManager.UpdateFlowField(c);

            // ── Phase 1: scan for surface cells ─────────────────────────────
            var cells = new SurfaceCell[S, S];
            bool any = false;

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                for (int y = S - 1; y >= 0; y--)
                {
                    var v = c.GetVoxelLocal(x, y, z);
                    if (!FluidMaterialUtility.IsFluid(v)) continue;

                    // Skip cells submerged under same liquid — only render the TOP surface
                    if (y + 1 < S)
                    {
                        var above = c.GetVoxelLocal(x, y + 1, z);
                        if (FluidMaterialUtility.IsFluid(above) &&
                            FluidMaterialUtility.LiquidFromVoxel(above) == FluidMaterialUtility.LiquidFromVoxel(v))
                            continue;
                    }
                    if (HasSolidAbove(c, x, y + 1, z)) break;

                    var liquid = FluidMaterialUtility.LiquidFromVoxel(v);
                    cells[x, z] = new SurfaceCell
                    {
                        has    = true,
                        liquid = liquid,
                        h      = VisualSurfaceHeight(y, v.waterLevel, liquid),
                        y      = y,
                        flow   = c.GetFlow(x, z)
                    };
                    any = true;
                    break;
                }
            }

            if (!any) { ClearGO(c); return; }

            // ── Phase 2: Gaussian-smooth the height field ───────────────────
            SmoothHeightField(cells, S);

            // ── Phase 3: build mesh (top surface only — no curtains!) ───────
            var verts     = new List<Vector3>(S * S * 8);
            var norms     = new List<Vector3>(S * S * 8);
            var uvs       = new List<Vector2>(S * S * 8);
            var uv2s      = new List<Vector2>(S * S * 8);
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
            }

            if (verts.Count == 0) { ClearGO(c); return; }

            if (c.waterMesh == null) c.waterMesh = new Mesh { name = "LiquidSurface" };
            c.waterMesh.Clear();
            c.waterMesh.indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            c.waterMesh.SetVertices(verts);
            c.waterMesh.SetNormals(norms);
            c.waterMesh.SetUVs(0, uvs);
            c.waterMesh.SetUVs(1, uv2s);
            c.waterMesh.subMeshCount = 2;
            c.waterMesh.SetTriangles(waterTris, 0);
            c.waterMesh.SetTriangles(oilTris, 1);
            c.waterMesh.RecalculateBounds();

            c.waterMeshFilter.sharedMesh = c.waterMesh;
            c.waterMeshRenderer.sharedMaterials = new[] { _waterMat, _oilMat };
            c.waterMeshGO.SetActive(true);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  HEIGHT FIELD SMOOTHING
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
                if (tempW[x, z] > 0f)
                    cells[x, z].smoothH = tempH[x, z];
                else
                    cells[x, z].smoothH = cells[x, z].h;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  TOP SURFACE — the ONLY geometry we build
        //
        //  Key insight: we do NOT build curtain/side faces. Instead, the water
        //  quad aggressively overlaps UNDER the terrain mesh. Since terrain is
        //  opaque and rendered before transparent water, the terrain covers the
        //  overlapping portion. The visible edge of the water surface naturally
        //  meets the terrain edge with no gap.
        //
        //  At chunk boundaries, we use a tiny seam overlap only (0.06f) so the
        //  adjacent chunk's water connects. No terrain tuck at boundaries.
        // ═══════════════════════════════════════════════════════════════════════

        private static void AddTop(Chunk c, SurfaceCell[,] cells, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            var cell = cells[x, z];

            // Corner heights from the SMOOTHED field
            float h00 = SmoothedCornerHeight(cells, x, z, cell.liquid, -1, -1, cell.smoothH);
            float h10 = SmoothedCornerHeight(cells, x, z, cell.liquid,  1, -1, cell.smoothH);
            float h11 = SmoothedCornerHeight(cells, x, z, cell.liquid,  1,  1, cell.smoothH);
            float h01 = SmoothedCornerHeight(cells, x, z, cell.liquid, -1,  1, cell.smoothH);

            float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;

            // ── Terrain overlap (inside chunk only) ─────────────────────────
            // Where water borders solid terrain, extend the quad well into the
            // terrain so the intersection is hidden behind the opaque terrain mesh.
            float terrainTuck = cell.liquid == LiquidType.CrudeOil ? 0.30f : 0.85f;

            if (TerrainSolidNear(c, x - 1, cell.y, z)) x0 -= terrainTuck;
            if (TerrainSolidNear(c, x + 1, cell.y, z)) x1 += terrainTuck;
            if (TerrainSolidNear(c, x, cell.y, z - 1)) z0 -= terrainTuck;
            if (TerrainSolidNear(c, x, cell.y, z + 1)) z1 += terrainTuck;

            // ── Chunk boundary — NO overlap ──────────────────────────────────
            // The adjacent chunk renders its own water up to the same boundary
            // from its side, so the surfaces meet exactly. No overlap = no
            // depth-based foam artifacts at chunk boundaries.
            // (We intentionally do NOT extend past chunk edges at all.)

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
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        private static float SmoothedCornerHeight(SurfaceCell[,] cells, int x, int z, LiquidType liquid, int sx, int sz, float fallback)
        {
            float sum = fallback;
            int cnt = 1;
            TryAddSmoothed(cells, x + sx, z, liquid, ref sum, ref cnt);
            TryAddSmoothed(cells, x, z + sz, liquid, ref sum, ref cnt);
            TryAddSmoothed(cells, x + sx, z + sz, liquid, ref sum, ref cnt);
            TryAddSmoothed(cells, x + sx * 2, z, liquid, ref sum, ref cnt);
            TryAddSmoothed(cells, x, z + sz * 2, liquid, ref sum, ref cnt);
            return sum / cnt;
        }

        private static void TryAddSmoothed(SurfaceCell[,] cells, int x, int z, LiquidType liquid, ref float sum, ref int cnt)
        {
            if (x < 0 || x >= VoxelConstants.CHUNK_SIZE || z < 0 || z >= VoxelConstants.CHUNK_SIZE) return;
            var c = cells[x, z];
            if (!c.has || c.liquid != liquid) return;
            sum += c.smoothH;
            cnt++;
        }

        private static Vector2 AverageFlow(SurfaceCell[,] cells, int x, int z, LiquidType liquid)
        {
            Vector2 sum = cells[x, z].flow;
            int count = 1;
            TryAddFlow(cells, x + 1, z, liquid, ref sum, ref count);
            TryAddFlow(cells, x - 1, z, liquid, ref sum, ref count);
            TryAddFlow(cells, x, z + 1, liquid, ref sum, ref count);
            TryAddFlow(cells, x, z - 1, liquid, ref sum, ref count);
            TryAddFlow(cells, x + 1, z + 1, liquid, ref sum, ref count);
            TryAddFlow(cells, x - 1, z - 1, liquid, ref sum, ref count);
            TryAddFlow(cells, x + 1, z - 1, liquid, ref sum, ref count);
            TryAddFlow(cells, x - 1, z + 1, liquid, ref sum, ref count);
            return sum / count;
        }

        private static void TryAddFlow(SurfaceCell[,] cells, int x, int z, LiquidType liquid, ref Vector2 sum, ref int count)
        {
            if (x < 0 || x >= VoxelConstants.CHUNK_SIZE || z < 0 || z >= VoxelConstants.CHUNK_SIZE) return;
            var c = cells[x, z];
            if (!c.has || c.liquid != liquid) return;
            sum += c.flow;
            count++;
        }

        private static float VisualSurfaceHeight(int y, byte level, LiquidType liquid)
        {
            if (level >= 16) return y + (liquid == LiquidType.CrudeOil ? 0.94f : 0.995f);
            return y + Mathf.Clamp(level / 255f, 0.08f, 0.985f);
        }

        /// <summary>
        /// True if there is solid terrain NEAR a position. Used to decide where
        /// to tuck the water quad under the terrain.
        /// Returns FALSE for out-of-bounds (chunk boundaries) so we never
        /// tuck or foam at chunk edges.
        /// </summary>
        private static bool TerrainSolidNear(Chunk c, int x, int y, int z)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            if (x < 0 || x >= S || z < 0 || z >= S) return false;

            for (int yy = y + 3; yy >= y - 5; yy--)
            {
                if (yy < 0 || yy >= S) continue;
                if (c.GetVoxelLocal(x, yy, z).IsSolid) return true;
            }
            return false;
        }

        private static bool HasSolidAbove(Chunk c, int x, int startY, int z)
        {
            for (int y = startY; y <= VoxelConstants.CHUNK_SIZE; y++)
            {
                if (c.GetVoxelLocal(x, y, z).IsSolid) return true;
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
