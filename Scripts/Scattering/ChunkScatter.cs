// Assets/Scripts/VoxelEngine/Scattering/ChunkScatter.cs
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Materials;

namespace VoxelEngine.Scattering
{
    /// <summary>
    /// Scatters biome scatter prefabs (trees, rocks, bushes) on top of a chunk's surface.
    /// Fixed: checks FluidGrid for water (OceanSeeder converts WaterVoxel to Air + fluid),
    /// uses world-space parenting to avoid floating-then-dropping visual glitch.
    /// </summary>
    public static class ChunkScatter
    {
        public static void Populate(IChunkScatterWorld world, Chunk chunk, BiomeRegistry registry, int seed)
        {
            if (registry == null || registry.biomes == null || registry.biomes.Count == 0) return;
            if (chunk?.go == null) return;

            // Clear previously-spawned scatter.
            var existing = chunk.go.transform.Find("__scatter");
            if (existing != null) Object.Destroy(existing.gameObject);

            const int S = VoxelConstants.CHUNK_SIZE;

            // Use world-space parenting so scatter doesn't appear in wrong position
            // while chunk GO transform is being set up.
            var holder = new GameObject("__scatter");
            holder.transform.SetParent(chunk.go.transform, worldPositionStays: true);

            int chunkSeed = seed
                          ^ (chunk.coord.x * 73856093)
                          ^ (chunk.coord.y * 19349663)
                          ^ (chunk.coord.z * 83492791);
            var rng = new Unity.Mathematics.Random((uint)math.max(1, chunkSeed));

            // Pre-compute the world Y origin for this chunk.
            int chunkWorldY = chunk.coord.y * S;
            bool isSphere = world is SphereWorld;

            for (int x = 0; x < S; x++)
            for (int y = 0; y < S; y++)
            for (int z = 0; z < S; z++)
            {
                var v = chunk.GetVoxelLocal(x, y, z);
                if (v.density <= 0) continue;

                int worldX = chunk.coord.x * S + x;
                int worldY = chunk.coord.y * S + y;
                int worldZ = chunk.coord.z * S + z;

                float3 wPos = new float3(worldX, worldY, worldZ);
                float3 normalDir = isSphere ? math.normalizesafe(wPos) : new float3(0, 1, 0);

                int dx = (int)math.round(normalDir.x);
                int dy = (int)math.round(normalDir.y);
                int dz = (int)math.round(normalDir.z);
                if (dx == 0 && dy == 0 && dz == 0) dy = 1;

                int nx = x + dx;
                int ny = y + dy;
                int nz = z + dz;
                bool aboveSolid = false;

                if (nx >= 0 && nx < S && ny >= 0 && ny < S && nz >= 0 && nz < S)
                {
                    aboveSolid = chunk.GetVoxelLocal(nx, ny, nz).density > 0;
                }
                else
                {
                    int cx = chunk.coord.x + (nx < 0 ? -1 : (nx >= S ? 1 : 0));
                    int cy = chunk.coord.y + (ny < 0 ? -1 : (ny >= S ? 1 : 0));
                    int cz = chunk.coord.z + (nz < 0 ? -1 : (nz >= S ? 1 : 0));
                    
                    if (world.TryGetChunk(new Vector3Int(cx, cy, cz), out var neighbor) && neighbor.isGenerated)
                    {
                        aboveSolid = neighbor.GetVoxelLocal((nx % S + S) % S, (ny % S + S) % S, (nz % S + S) % S).density > 0;
                    }
                    else
                    {
                        aboveSolid = true; // Assume solid if ungenerated
                    }
                }

                if (aboveSolid) continue;

                byte topMat = v.material;

                // 3) Skip water material voxels.
                if (topMat == (byte)MaterialId.WaterVoxel ||
                    topMat == (byte)MaterialId.WaterLiquid ||
                    topMat == (byte)MaterialId.CrudeOil) continue;

                // 4) Check FluidGrid for water at or above this position.
                if (!isSphere && nx >= 0 && nx < S && ny >= 0 && ny < S && nz >= 0 && nz < S)
                {
                    if (HasFluidAbove(chunk, world, nx, ny, nz))
                        continue;
                }

                // 5) Skip stone (looks weird with trees on bare stone).
                if (topMat == (byte)MaterialId.Stone) continue;

                float altitude = isSphere ? math.length(wPos) : worldY;

                // 6) Skip if below or at sea level (catches near-shore positions).
                if (altitude <= world.SeaLevel)
                    continue;

                // Map sphere altitude back to flat-world scale so authored min/max heights work
                float effectiveAltitude = isSphere ? (altitude - world.SeaLevel + 96f) : altitude;

                // Climate sampling: use the world position as a 3D direction (sphere-correct).
                // The flat-world BiomePicker.SampleClimate uses 2D snoise; on a sphere we need
                // 3D direction sampling so trees spawn in the RIGHT biome (not random).
                float3 dir = math.normalizesafe(new float3(worldX, worldY, worldZ), new float3(0, 1, 0));
                float2 climate = isSphere ? SphereClimateSample(seed, dir) : VoxelEngine.Biomes.BiomePicker.SampleClimate(seed, worldX, worldZ);
                BiomeDefinition biome = PickBiome(registry, climate);
                if (biome == null || biome.scatter == null || biome.scatter.Length == 0) continue;

                foreach (var entry in biome.scatter)
                {
                    if (entry.prefab == null || entry.density <= 0f) continue;
                    if (effectiveAltitude < entry.minHeight || effectiveAltitude > entry.maxHeight) continue;
                    if (rng.NextFloat() > entry.density) continue;

                    Vector3 posCenter = new Vector3(worldX + 0.5f, worldY + 0.5f, worldZ + 0.5f) * VoxelConstants.VOXEL_SIZE;
                    Vector3 upDir = isSphere ? posCenter.normalized : Vector3.up;
                    
                    Vector3 tangentX = Vector3.Cross(upDir, Vector3.up);
                    if (tangentX.sqrMagnitude < 0.1f) tangentX = Vector3.right;
                    tangentX.Normalize();
                    Vector3 tangentZ = Vector3.Cross(upDir, tangentX).normalized;
                    
                    Vector3 localBodyPos = posCenter + tangentX * rng.NextFloat(-0.4f, 0.4f) + tangentZ * rng.NextFloat(-0.4f, 0.4f);

                    Quaternion randomYaw = isSphere
                        ? Quaternion.AngleAxis(rng.NextFloat(0f, 360f), upDir)
                        : Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);
                    Quaternion localBodyRot = isSphere
                        ? randomYaw * Quaternion.FromToRotation(Vector3.up, upDir)
                        : randomYaw;

                    // Convert body-local pos/rot to world space for Instantiate
                    Transform rootTransform = chunk.go.transform.parent;
                    Vector3 worldPos = rootTransform != null ? rootTransform.TransformPoint(localBodyPos) : localBodyPos;
                    Quaternion worldRot = rootTransform != null ? rootTransform.rotation * localBodyRot : localBodyRot;

                    float scale = rng.NextFloat(entry.minScale, entry.maxScale);

                    // Overlap check
                    float clearRadius = Mathf.Max(0.6f, scale * 0.7f);
                    bool blocked = false;
                    var hits = Physics.OverlapSphere(worldPos + (rootTransform != null ? rootTransform.TransformDirection(upDir) : upDir) * 0.5f, clearRadius);
                    foreach (var col in hits)
                    {
                        if (col == null) continue;
                        if (col.GetComponentInParent<VoxelEngine.Trees.Tree>() != null ||
                            col.GetComponentInParent<VoxelEngine.Building.PlacedBlock>() != null ||
                            col.GetComponentInParent<VoxelEngine.Building.Tiered.PlacedTieredBlock>() != null)
                        { blocked = true; break; }
                    }
                    if (blocked) break;

                    var go = Object.Instantiate(entry.prefab, worldPos, worldRot, holder.transform);
                    go.transform.localScale = Vector3.one * scale;
                    break;
                }
            }
        }

        /// <summary>
        /// Check if there's fluid (water) at or above a local voxel position.
        /// This catches cells where OceanSeeder converted WaterVoxel to Air+FluidGrid.
        /// </summary>
        private static bool HasFluidAbove(Chunk chunk, IChunkScatterWorld world, int lx, int ly, int lz)
        {
            const int S = VoxelConstants.CHUNK_SIZE;

            // Check this cell and the cell directly above for fluid.
            if (chunk.fluidGrid != null)
            {
                if (chunk.fluidGrid.Get(lx, ly, lz) > 0) return true;
                if (ly + 1 < S && chunk.fluidGrid.Get(lx, ly + 1, lz) > 0) return true;
            }

            // Also check the chunk above if we're at the top of this chunk.
            if (ly >= S - 1)
            {
                var aboveCoord = chunk.coord + new Vector3Int(0, 1, 0);
                if (world.TryGetChunk(aboveCoord, out var aboveChunk) &&
                    aboveChunk.fluidGrid != null &&
                    aboveChunk.fluidGrid.Get(lx, 0, lz) > 0)
                    return true;
            }

            return false;
        }

        private static BiomeDefinition PickBiome(BiomeRegistry registry, float2 climate)
        {
            BiomeDefinition best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var b in registry.biomes)
            {
                if (b == null) continue;
                float tCenter = (b.minTemperature + b.maxTemperature) * 0.5f;
                float tHalf   = math.max(0.001f, (b.maxTemperature - b.minTemperature) * 0.5f);
                float tDist   = math.abs(climate.x - tCenter) / tHalf;

                float hCenter = (b.minHumidity + b.maxHumidity) * 0.5f;
                float hHalf   = math.max(0.001f, (b.maxHumidity - b.minHumidity) * 0.5f);
                float hDist   = math.abs(climate.y - hCenter) / hHalf;

                float fit = 1f - math.max(tDist, hDist) + b.priority * 0.05f;
                if (fit > bestScore) { bestScore = fit; best = b; }
            }
            return best;
        }

        /// <summary>
        /// 3D direction-based climate sampling (mirrors SphereDensity.SampleClimate so scatter
        /// picks the same biome the terrain generator used). Uses latitude-based temperature
        /// + noise blend so scatter matches the real biome distribution.
        /// </summary>
        private static float2 SphereClimateSample(int seed, float3 dir)
        {
            float3 p = dir;
            float tNoise = noise.snoise(p * 1.7f + (seed * 0.073f + 47.3f)) * 0.5f + 0.5f;
            float hNoise = noise.snoise(p * 2.1f + (seed * 0.149f + 91.7f)) * 0.5f + 0.5f;
            float lat = math.abs(dir.y);
            float tLat = math.saturate(1f - lat * 1.25f);
            float hLat = math.cos(lat * 3.0f) * 0.3f + 0.55f;
            float t = math.lerp(tNoise, tLat, 0.55f);
            float h = math.lerp(hNoise, math.saturate(hLat), 0.385f);
            return new float2(t, h);
        }
    }
}
