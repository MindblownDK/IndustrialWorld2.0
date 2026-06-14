// Assets/Scripts/VoxelEngine/WaterSim/WaterMeshBuilder.cs
//
// Rebuilt V3: Smooth continuous water surface with shoreline curtain geometry.
//
// Key improvements:
//   • Pre-smoothed height field (Gaussian blur) — surface looks like a
//     continuous sheet, not a grid of flat squares
//   • Shoreline curtain geometry — vertical faces from water surface down to
//     terrain height at every water/terrain boundary, eliminating the gap
//   • No foam or shore effects at chunk boundaries — only where water
//     actually meets solid terrain inside the chunk
//   • UV2 encodes flow direction + speed for KWS2-quality shader flow mapping
//
// Mesh layout:
//   submesh 0 = water (clear, animated, flow-mapped, foamy)
//   submesh 1 = crude oil (dark, viscous, slow waves)

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
        //  MAIN BUILD
        // ═══════════════════════════════════════════════════════════════════════

        private static void Build(Chunk c)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            EnsureGO(c);

            // Update flow field from current voxel pressure state
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

            // ── Phase 3: build mesh ─────────────────────────────────────────
            var verts  = new List<Vector3>(S * S * 16);
            var norms  = new List<Vector3>(S * S * 16);
            var uvs    = new List<Vector2>(S * S * 16);
            var uv2s   = new List<Vector2>(S * S * 16);
            var waterTris = new List<int>(S * S * 8);
            var oilTris   = new List<int>(S * S * 8);

            float wX = c.coord.x * S;
            float wZ = c.coord.z * S;

            for (int z = 0; z < S; z++)
            for (int x = 0; x < S; x++)
            {
                var cell = cells[x, z];
                if (!cell.has) continue;

                var tris = cell.liquid == LiquidType.CrudeOil ? oilTris : waterTris;
                AddTop(c, cells, x, z, wX, wZ, verts, norms, uvs, uv2s, tris);
                AddCurtain(c, cells, x, z, wX, wZ, verts, norms, uvs, uv2s, tris);
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
            // Write smoothed height into cell.smoothH using a 5×5 Gaussian-like kernel.
            // Only blend between cells of the SAME liquid type.
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

                    // Gaussian-like weight: 1 at center, decaying outward
                    float d2 = dx * dx + dz * dz;
                    float w = 1f / (1f + d2 * 0.5f);   // sigma ~1.4
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
        //  TOP SURFACE
        // ═══════════════════════════════════════════════════════════════════════

        private static void AddTop(Chunk c, SurfaceCell[,] cells, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            var cell = cells[x, z];

            // Corner heights from the SMOOTHED field — averages over same-liquid neighbors
            float h00 = SmoothedCornerHeight(cells, x, z, cell.liquid, -1, -1, cell.smoothH);
            float h10 = SmoothedCornerHeight(cells, x, z, cell.liquid,  1, -1, cell.smoothH);
            float h11 = SmoothedCornerHeight(cells, x, z, cell.liquid,  1,  1, cell.smoothH);
            float h01 = SmoothedCornerHeight(cells, x, z, cell.liquid, -1,  1, cell.smoothH);

            // ── Shore tuck: extend toward SOLID terrain (not chunk edges) ────
            float shoreTuck = cell.liquid == LiquidType.CrudeOil ? 0.20f : 0.65f;
            float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;

            // Only tuck toward actual solid terrain INSIDE the chunk.
            // At chunk boundaries, use a tiny seam overlap only.
            if (TerrainSolidNear(c, x - 1, cell.y, z)) x0 -= shoreTuck;
            else if (x == 0) x0 -= 0.06f;  // tiny seam, not a full tuck

            if (TerrainSolidNear(c, x + 1, cell.y, z)) x1 += shoreTuck;
            else if (x == VoxelConstants.CHUNK_SIZE - 1) x1 += 0.06f;

            if (TerrainSolidNear(c, x, cell.y, z - 1)) z0 -= shoreTuck;
            else if (z == 0) z0 -= 0.06f;

            if (TerrainSolidNear(c, x, cell.y, z + 1)) z1 += shoreTuck;
            else if (z == VoxelConstants.CHUNK_SIZE - 1) z1 += 0.06f;

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
        //  SHORELINE CURTAIN — vertical faces where water meets terrain
        //
        //  This is the KEY fix: the water surface is a flat sheet sitting at a
        //  fixed height, while terrain is a smooth surface-net mesh that slopes.
        //  At the shoreline there is a visible GAP between the two surfaces.
        //
        //  The curtain extends the water geometry DOWN from the water surface
        //  to the terrain height at the contact point.  This creates a seamless
        //  visual connection — the water "touches" the terrain.
        //
        //  Only added where water borders actual SOLID terrain INSIDE the chunk.
        //  NEVER at chunk boundaries (adjacent chunk owns its side).
        // ═══════════════════════════════════════════════════════════════════════

        private static void AddCurtain(Chunk c, SurfaceCell[,] cells, int x, int z, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            var cell = cells[x, z];
            const int S = VoxelConstants.CHUNK_SIZE;

            // For each of the 4 cardinal directions, check if terrain is adjacent
            TryCurtainFace(c, cells, x, z, cell, -1,  0, wX, wZ, verts, norms, uvs, uv2s, tris);
            TryCurtainFace(c, cells, x, z, cell,  1,  0, wX, wZ, verts, norms, uvs, uv2s, tris);
            TryCurtainFace(c, cells, x, z, cell,  0, -1, wX, wZ, verts, norms, uvs, uv2s, tris);
            TryCurtainFace(c, cells, x, z, cell,  0,  1, wX, wZ, verts, norms, uvs, uv2s, tris);
        }

        private static void TryCurtainFace(Chunk c, SurfaceCell[,] cells, int x, int z, SurfaceCell cell,
            int dx, int dz, float wX, float wZ,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Vector2> uv2s, List<int> tris)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            int nx = x + dx, nz = z + dz;

            // ── NEVER add curtain at chunk boundaries ──
            // The adjacent chunk builds its own water surface + curtain.
            if (nx < 0 || nx >= S || nz < 0 || nz >= S) return;

            var neighbour = cells[nx, nz];

            // If the neighbour is the same liquid, no curtain needed
            if (neighbour.has && neighbour.liquid == cell.liquid)
            {
                // Only add curtain if there's a significant height drop (waterfall)
                if (cell.smoothH - neighbour.smoothH < 0.85f) return;
                // Waterfall curtain — handled by AddTop's smoothing + wave shader
                return;
            }

            // If neighbour is NOT solid terrain, no curtain (it's air/empty)
            if (!TerrainSolidNear(c, nx, cell.y, nz)) return;

            // ── We have a water→terrain boundary. Build the curtain. ──

            float topY = cell.smoothH;

            // Sample terrain height at the contact point.
            // Find the highest solid voxel at (nx, ?, nz) near the water surface.
            float terrainY = SampleTerrainHeight(c, nx, nz, cell.y);

            // Extend the curtain at least 1.5 voxels below the water surface
            // to handle sloped terrain that may dip below.
            float minCurtainDepth = 1.8f;
            float curtainBottom = Mathf.Min(terrainY, topY - minCurtainDepth);

            // Don't build a curtain shorter than a tiny sliver
            if (topY - curtainBottom < 0.05f) return;

            // Build the quad.  The curtain extends slightly into the terrain
            // (shoreTuck overlap) so there's no crack between water and rock.
            float shoreTuck = cell.liquid == LiquidType.CrudeOil ? 0.12f : 0.35f;
            float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;

            Vector3 a, b, c0, d;
            Vector3 normal;

            if (dx < 0)       { a = new Vector3(x0, curtainBottom, z1); b = new Vector3(x0, curtainBottom, z0); c0 = new Vector3(x0 - shoreTuck, topY, z0); d = new Vector3(x0 - shoreTuck, topY, z1); normal = Vector3.left; }
            else if (dx > 0)  { a = new Vector3(x1, curtainBottom, z0); b = new Vector3(x1, curtainBottom, z1); c0 = new Vector3(x1 + shoreTuck, topY, z1); d = new Vector3(x1 + shoreTuck, topY, z0); normal = Vector3.right; }
            else if (dz < 0)  { a = new Vector3(x1, curtainBottom, z0); b = new Vector3(x0, curtainBottom, z0); c0 = new Vector3(x0 - shoreTuck, topY, z0); d = new Vector3(x1 + shoreTuck, topY, z0); normal = Vector3.back; }
            else              { a = new Vector3(x0, curtainBottom, z1); b = new Vector3(x1, curtainBottom, z1); c0 = new Vector3(x1 + shoreTuck, topY, z1); d = new Vector3(x0 - shoreTuck, topY, z1); normal = Vector3.forward; }

            Vector2 flow = cell.flow;

            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c0); verts.Add(d);
            // Bottom two vertices have face normal; top two have upward bias
            // for seamless blending with the surface mesh.
            norms.Add(normal); norms.Add(normal);
            norms.Add(Vector3.Slerp(normal, Vector3.up, 0.5f));
            norms.Add(Vector3.Slerp(normal, Vector3.up, 0.5f));

            uvs.Add(new Vector2(wX + a.x, wZ + a.z));
            uvs.Add(new Vector2(wX + b.x, wZ + b.z));
            uvs.Add(new Vector2(wX + c0.x, wZ + c0.z));
            uvs.Add(new Vector2(wX + d.x, wZ + d.z));
            for (int n = 0; n < 4; n++) uv2s.Add(flow);

            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        /// <summary>
        /// Find the highest solid terrain voxel at (wx, ?, wz) near the water
        /// surface level.  Used to determine how far down the curtain extends.
        /// </summary>
        private static float SampleTerrainHeight(Chunk c, int lx, int lz, int waterY)
        {
            // Search a vertical range around the water surface level.
            // Terrain at the shoreline is usually at or slightly below water level.
            const int S = VoxelConstants.CHUNK_SIZE;

            for (int dy = 0; dy <= 6; dy++)
            {
                // Check below first (most common case: terrain is below water)
                int yBelow = waterY - dy;
                if (yBelow >= 0 && yBelow < S && c.GetVoxelLocal(lx, yBelow, lz).IsSolid)
                    return yBelow + 1f; // terrain surface is ~1 voxel above the solid center

                // Then check above (raised terrain at water line)
                int yAbove = waterY + dy;
                if (yAbove >= 0 && yAbove < S && c.GetVoxelLocal(lx, yAbove, lz).IsSolid)
                    return yAbove + 1f;
            }

            // Fallback: assume terrain is 1 voxel below water
            return waterY;
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
            // Wider kernel: also sample 2 cells out for extra smoothing
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
        /// Check if there is solid terrain NEAR a position (wider vertical range).
        /// Used for shore tuck decisions — only extend water toward actual terrain.
        /// </summary>
        private static bool TerrainSolidNear(Chunk c, int x, int y, int z)
        {
            const int S = VoxelConstants.CHUNK_SIZE;

            // Out of chunk bounds — this is a chunk boundary, NOT terrain.
            // Return false so we never tuck/foam at chunk edges.
            if (x < 0 || x >= S || z < 0 || z >= S) return false;

            // Generous vertical search: terrain slopes can be well above or
            // below the exact water voxel Y.
            for (int yy = y + 2; yy >= y - 5; yy--)
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

        private static bool NeighbourIsSolid(Chunk c, int x, int y, int z)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            if (x < 0 || x >= S || z < 0 || z >= S || y < 0 || y >= S) return false;
            return c.GetVoxelLocal(x, y, z).IsSolid;
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
