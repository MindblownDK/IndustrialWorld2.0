// Assets/Scripts/VoxelEngine/Scattering/ChunkScatter.cs
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Materials;

namespace VoxelEngine.Scattering
{
    /// <summary>
    /// Places biome scatter on the actual exterior of streamed terrain.
    ///
    /// Spherical worlds use SphereWorld's sampled radial surface instead of a global-Y
    /// neighbour test. This prevents trees from being attached to cave walls, from sinking
    /// half a voxel into terrain, and from treating a newly streamed chunk edge as ground.
    /// Runtime reservations provide deterministic, cross-chunk spacing so tree canopies never
    /// intersect even while adjacent chunks finish their scatter pass on different frames.
    /// </summary>
    public static class ChunkScatter
    {
        private sealed class Reservation
        {
            public GameObject instance;
            public Vector3 position;
            public float radius;
        }

        private static readonly Dictionary<IChunkScatterWorld, List<Reservation>> s_reservations = new();
        private static readonly Collider[] s_overlapBuffer = new Collider[32];

        /// <summary>Release cached runtime reservations when a streamed world is destroyed.</summary>
        public static void ForgetWorld(IChunkScatterWorld world)
        {
            if (world != null) s_reservations.Remove(world);
        }

        public static void Populate(IChunkScatterWorld world, Chunk chunk, BiomeRegistry registry, int seed)
        {
            if (world == null || registry == null || registry.biomes == null || registry.biomes.Count == 0) return;
            if (chunk?.go == null || !chunk.isGenerated) return;

            // Clear previously spawned scatter when a chunk is explicitly repopulated. Pool
            // returns also disable the holder, and reservations are pruned before next use.
            var existing = chunk.go.transform.Find("__scatter");
            if (existing != null)
            {
                RemoveReservationsOwnedBy(world, existing);
                Object.Destroy(existing.gameObject);
            }

            const int S = VoxelConstants.CHUNK_SIZE;
            // Scatter is intentionally sampled on a 2 m lattice. Trees receive explicit
            // canopy separation below, so evaluating every one-metre solid voxel only creates
            // redundant rejected candidates and large streaming hitches.
            const int CandidateStride = 2;
            var holder = new GameObject("__scatter");
            // Chunk placement is complete before deferred scatter runs. Keep the holder at the
            // chunk origin so its children remain body-relative if the planet transform moves.
            holder.transform.SetParent(chunk.go.transform, worldPositionStays: false);

            int chunkSeed = seed
                          ^ (chunk.coord.x * 73856093)
                          ^ (chunk.coord.y * 19349663)
                          ^ (chunk.coord.z * 83492791);
            var rng = new Unity.Mathematics.Random((uint)math.max(1, chunkSeed));
            var sphere = world as SphereWorld;
            bool isSphere = sphere != null;

            for (int x = 0; x < S; x += CandidateStride)
            for (int y = 0; y < S; y += CandidateStride)
            for (int z = 0; z < S; z += CandidateStride)
            {
                Voxel voxel = chunk.GetVoxelLocal(x, y, z);
                if (!voxel.IsSolid || IsLiquid(voxel)) continue;

                int worldX = chunk.coord.x * S + x;
                int worldY = chunk.coord.y * S + y;
                int worldZ = chunk.coord.z * S + z;
                var localVoxel = new Vector3Int(worldX, worldY, worldZ);

                Vector3 localSurface;
                Vector3 upDir;
                if (isSphere)
                {
                    // Cheaply reject deep solid cells before invoking the exact density-column
                    // test. This turns a whole-chunk scan into a small exposed-surface scan
                    // instead of evaluating procedural terrain for every underground voxel.
                    if (!HasPotentialRadialExposure(world, chunk, x, y, z, localVoxel)) continue;
                    // This is the authoritative exterior test: cave walls and deep terrain
                    // cannot pass it, regardless of local mesh/collider timing.
                    if (!sphere.TryGetExteriorSurface(localVoxel, out localSurface, out upDir)) continue;
                }
                else
                {
                    if (!TryFindFlatSurface(world, chunk, x, y, z, out localSurface, out upDir)) continue;
                }

                byte topMat = voxel.material;
                if (topMat == (byte)MaterialId.Stone) continue;

                float altitude = isSphere ? localSurface.magnitude : worldY;
                if (altitude <= world.SeaLevel) continue;

                // Authored min/max scatter heights use the old terrain scale. Rebase the radial
                // distance around sea level so the same biome assets remain meaningful.
                float effectiveAltitude = isSphere ? altitude - world.SeaLevel + 96f : altitude;
                float2 climate = isSphere
                    ? SphereDensity.SampleClimate(seed, (float3)upDir)
                    : VoxelEngine.Biomes.BiomePicker.SampleClimate(seed, worldX, worldZ);
                BiomeDefinition biome = PickBiome(registry, climate);
                if (biome == null || biome.scatter == null || biome.scatter.Length == 0) continue;

                foreach (var entry in biome.scatter)
                {
                    if (entry.prefab == null || entry.density <= 0f) continue;
                    if (effectiveAltitude < entry.minHeight || effectiveAltitude > entry.maxHeight) continue;
                    if (rng.NextFloat() > entry.density) continue;

                    float scale = rng.NextFloat(entry.minScale, entry.maxScale);
                    if (scale <= 0f) continue;

                    Vector3 localBodyPos = localSurface;
                    if (!isSphere)
                    {
                        // Flat compatibility only. New planet work never uses this path.
                        localBodyPos += new Vector3(rng.NextFloat(-0.4f, 0.4f), 0f, rng.NextFloat(-0.4f, 0.4f));
                    }

                    Quaternion randomYaw = isSphere
                        ? Quaternion.AngleAxis(rng.NextFloat(0f, 360f), upDir)
                        : Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);
                    Quaternion localBodyRot = isSphere
                        ? randomYaw * Quaternion.FromToRotation(Vector3.up, upDir)
                        : randomYaw;

                    Transform rootTransform = chunk.go.transform.parent;
                    Vector3 worldPos = rootTransform != null ? rootTransform.TransformPoint(localBodyPos) : localBodyPos;
                    Vector3 worldUp = rootTransform != null ? rootTransform.TransformDirection(upDir).normalized : upDir;
                    Quaternion worldRot = rootTransform != null ? rootTransform.rotation * localBodyRot : localBodyRot;

                    bool isTree = IsTreePrefab(entry.prefab);
                    // Tree roots use a generous per-tree footprint, then reservation checks add
                    // the two radii together. Oak/pine canopies therefore retain a visible gap
                    // instead of merely avoiding trunk overlap.
                    float clearRadius = isTree
                        ? Mathf.Max(2.4f, scale * 2.25f)
                        : Mathf.Max(0.55f, scale * 0.70f);
                    if (IsBlocked(world, holder.transform, worldPos, worldUp, clearRadius)) continue;

                    GameObject instance = Object.Instantiate(entry.prefab, worldPos, worldRot, holder.transform);
                    instance.transform.localScale = Vector3.one * scale;
                    if (isTree && instance.GetComponentInChildren<VoxelEngine.Trees.Tree>() == null)
                    {
                        var t = instance.AddComponent<VoxelEngine.Trees.Tree>();
                        t.maxHp = 80;
                        t.hp = 80;
                        t.minLogs = 2;
                        t.maxLogs = 4;
                    }
                    if (instance.GetComponentInChildren<Collider>() == null)
                    {
                        var col = instance.AddComponent<CapsuleCollider>();
                        col.height = 4f;
                        col.radius = 0.6f;
                        col.center = new Vector3(0, 2f, 0);
                    }
                    Register(world, instance, worldPos, clearRadius);
                    break; // one authored scatter choice per validated surface point
                }
            }
        }

        private static bool HasPotentialRadialExposure(IChunkScatterWorld world, Chunk chunk, int x, int y, int z, Vector3Int localVoxel)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            Vector3 radial = ((Vector3)localVoxel + Vector3.one * 0.5f).normalized;
            Vector3Int outward = DominantAxis(radial);
            int nx = x + outward.x;
            int ny = y + outward.y;
            int nz = z + outward.z;
            if (nx >= 0 && nx < S && ny >= 0 && ny < S && nz >= 0 && nz < S)
                return !chunk.GetVoxelLocal(nx, ny, nz).IsSolid;

            Vector3Int neighbourCoord = chunk.coord + new Vector3Int(
                nx < 0 ? -1 : nx >= S ? 1 : 0,
                ny < 0 ? -1 : ny >= S ? 1 : 0,
                nz < 0 ? -1 : nz >= S ? 1 : 0);
            if (!world.TryGetChunk(neighbourCoord, out Chunk neighbour) || neighbour == null || !neighbour.isGenerated)
                return true;
            int wrappedX = (nx % S + S) % S;
            int wrappedY = (ny % S + S) % S;
            int wrappedZ = (nz % S + S) % S;
            return !neighbour.GetVoxelLocal(wrappedX, wrappedY, wrappedZ).IsSolid;
        }

        private static Vector3Int DominantAxis(Vector3 direction)
        {
            Vector3 absolute = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
                return new Vector3Int(direction.x >= 0f ? 1 : -1, 0, 0);
            if (absolute.y >= absolute.z)
                return new Vector3Int(0, direction.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, direction.z >= 0f ? 1 : -1);
        }

        private static bool TryFindFlatSurface(IChunkScatterWorld world, Chunk chunk, int x, int y, int z,
            out Vector3 localSurface, out Vector3 upDir)
        {
            localSurface = Vector3.zero;
            upDir = Vector3.up;
            const int S = VoxelConstants.CHUNK_SIZE;
            int aboveY = y + 1;
            bool aboveSolid;
            if (aboveY < S)
            {
                aboveSolid = chunk.GetVoxelLocal(x, aboveY, z).IsSolid;
            }
            else
            {
                Vector3Int aboveCoord = chunk.coord + Vector3Int.up;
                aboveSolid = world.TryGetChunk(aboveCoord, out Chunk above)
                    && above != null
                    && above.isGenerated
                    && above.GetVoxelLocal(x, 0, z).IsSolid;
            }
            if (aboveSolid) return false;

            localSurface = new Vector3(
                chunk.coord.x * S + x + 0.5f,
                chunk.coord.y * S + y + 1f,
                chunk.coord.z * S + z + 0.5f) * VoxelConstants.VOXEL_SIZE;
            return true;
        }

        private static bool IsBlocked(IChunkScatterWorld world, Transform holder, Vector3 worldPos, Vector3 worldUp, float clearRadius)
        {
            if (s_reservations.TryGetValue(world, out List<Reservation> reservations))
            {
                for (int i = reservations.Count - 1; i >= 0; i--)
                {
                    Reservation reservation = reservations[i];
                    if (reservation.instance == null || !reservation.instance.activeInHierarchy)
                    {
                        reservations.RemoveAt(i);
                        continue;
                    }

                    float minimum = clearRadius + reservation.radius;
                    if ((reservation.position - worldPos).sqrMagnitude < minimum * minimum)
                        return true;
                }
            }

            // The reservation test is authoritative for scatter-vs-scatter. Physics additionally
            // protects player structures and legacy loaded tree instances that predate it.
            int hitCount = Physics.OverlapSphereNonAlloc(
                worldPos + worldUp * 0.5f, clearRadius, s_overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = s_overlapBuffer[i];
                if (hit == null) continue;
                if (holder != null && hit.transform.IsChildOf(holder)) continue;
                if (hit.GetComponentInParent<VoxelEngine.Trees.Tree>() != null ||
                    hit.GetComponentInParent<VoxelEngine.Building.PlacedBlock>() != null ||
                    hit.GetComponentInParent<VoxelEngine.Building.Tiered.PlacedTieredBlock>() != null)
                    return true;
            }

            return false;
        }

        private static void Register(IChunkScatterWorld world, GameObject instance, Vector3 worldPos, float radius)
        {
            if (!s_reservations.TryGetValue(world, out List<Reservation> reservations))
            {
                reservations = new List<Reservation>();
                s_reservations.Add(world, reservations);
            }
            reservations.Add(new Reservation { instance = instance, position = worldPos, radius = radius });
        }

        private static void RemoveReservationsOwnedBy(IChunkScatterWorld world, Transform holder)
        {
            if (holder == null || !s_reservations.TryGetValue(world, out List<Reservation> reservations)) return;
            for (int i = reservations.Count - 1; i >= 0; i--)
            {
                Reservation reservation = reservations[i];
                if (reservation.instance == null || reservation.instance.transform.IsChildOf(holder))
                    reservations.RemoveAt(i);
            }
        }

        private static bool IsTreePrefab(GameObject prefab)
        {
            if (prefab == null) return false;
            if (prefab.GetComponent<VoxelEngine.Trees.Tree>() != null) return true;
            return prefab.name.IndexOf("tree", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLiquid(Voxel voxel)
        {
            byte material = voxel.material;
            return material == (byte)MaterialId.WaterVoxel ||
                   material == (byte)MaterialId.WaterLiquid ||
                   material == (byte)MaterialId.CrudeOil ||
                   voxel.waterLevel > 0;
        }

        private static BiomeDefinition PickBiome(BiomeRegistry registry, float2 climate)
        {
            BiomeDefinition best = null;
            float bestScore = float.NegativeInfinity;
            foreach (BiomeDefinition biome in registry.biomes)
            {
                if (biome == null) continue;
                float tCenter = (biome.minTemperature + biome.maxTemperature) * 0.5f;
                float tHalf = math.max(0.001f, (biome.maxTemperature - biome.minTemperature) * 0.5f);
                float tDist = (climate.x - tCenter) / tHalf;
                float hCenter = (biome.minHumidity + biome.maxHumidity) * 0.5f;
                float hHalf = math.max(0.001f, (biome.maxHumidity - biome.minHumidity) * 0.5f);
                float hDist = (climate.y - hCenter) / hHalf;
                // Match SphereDensity.Score: scatter must select the same dominant biome as
                // the generated terrain column, not merely the nearest rectangular window.
                float fit = 1f - math.sqrt(tDist * tDist + hDist * hDist) + biome.priority * 0.05f;
                if (fit > bestScore)
                {
                    bestScore = fit;
                    best = biome;
                }
            }
            return best;
        }
    }
}
