// Assets/Scripts/VoxelEngine/Cosmos/SpaceAsteroidField.cs
//
// Procedural asteroid spawning in DEEP SPACE — the belt between and beyond planetary
// orbits. While the player is outside every planet/moon gravity well (solar frame),
// this spawner populates the surrounding volume with real, minable voxel-style rocks
// (SpaceAsteroid): they appear as you travel, are deterministic
// per cosmic region, and despawn when you leave.
//
// Design decisions:
//   • Spawn positions are seeded from the player's COSMIC CELL (4096 km hash), so the
//     same region of space always offers the same rocks — no blink-in/out on revisits.
//   • Only spawns when the scene frame is the star frame (outside planet/moon orbits),
//     per the feature brief.
//   • All asteroids are destroyed when the reference frame changes (entering a planet's
//     well); the new region re-populates on the next deep-space stretch.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using Random = Unity.Mathematics.Random;

namespace VoxelEngine.Cosmos
{
    public class SpaceAsteroidField : MonoBehaviour
    {
        [Header("Spawning")]
        [Tooltip("Maximum live asteroids around the player.")]
        public int maxAsteroids = 56;

        [Tooltip("Seconds between spawn attempts (in open space).")]
        public float spawnIntervalSeconds = 2.5f;

        [Tooltip("Asteroids spawn in this ring around the player (metres).")]
        public Vector2 spawnRingMeters = new Vector2(1200f, 14000f);

        [Tooltip("Asteroids beyond this distance are culled (metres).")]
        public float despawnDistanceMeters = 30000f;

        [Tooltip("Minimum clearance between spawned asteroids (metres).")]
        public float minSeparationMeters = 450f;

        [Tooltip("Asteroid radius range (metres).")]
        public Vector2 asteroidRadiusMeters = new Vector2(8f, 140f);

        [Tooltip("Minimum altitude (m) above a body's surface before rocks appear while inside its frame — keeps the sky over bases clean while making high orbit and transfers feel populated.")]
        public float minOrbitAltitudeMeters = 12000f;

        [Tooltip("Cosmic cell size (km) used to seed deterministic regions.")]
        public double regionCellKm = 4096d;

        [Header("Clusters")]
        [Tooltip("Chance (0..1) that a spawn creates a CLUSTER of rocks instead of a lone rock — real belts form in families.")]
        [Range(0f, 1f)] public float clusterChance = 0.55f;

        [Tooltip("Extra rocks spawned per cluster (the cluster centre is the first rock).")]
        public Vector2Int clusterExtraRocks = new Vector2Int(2, 5);

        [Tooltip("Cluster radius range (metres) — members scatter inside this shell.")]
        public Vector2 clusterRadiusMeters = new Vector2(250f, 900f);

        [Tooltip("Chance a cluster member shares the cluster's material (ore family).")]
        [Range(0f, 1f)] public float clusterSharedMaterialChance = 0.6f;

        [Header("Drift")]
        [Tooltip("Each rock drifts through the field at a speed in this range (m/s) — slow, alive, never orbital.")]
        public Vector2 driftSpeedMetersPerSec = new Vector2(0.4f, 1.2f);

        [Header("Ore Catalogue")]
        [Tooltip("Ore material pool drawn per asteroid (bias order).")]
        public MaterialId[] orePool =
        {
            MaterialId.Iron, MaterialId.Iron, MaterialId.Nickel, MaterialId.Silicon,
            MaterialId.Cobalt, MaterialId.Gold, MaterialId.Platinum, MaterialId.Ice, MaterialId.Ice,
        };

        private readonly List<SpaceAsteroid> _live = new List<SpaceAsteroid>();
        private float _spawnTimer;
        private uint _attemptNonce;
        private bool _wasDeepSpace;
        private readonly Dictionary<MaterialId, ItemDefinition[]> _dropCache = new Dictionary<MaterialId, ItemDefinition[]>();

        private void OnEnable()
        {
            SpaceOrigin.OnFrameChanged += OnFrameChanged;
            _wasDeepSpace = IsOpenSpaceNow();
        }

        private void OnDisable()
        {
            SpaceOrigin.OnFrameChanged -= OnFrameChanged;
        }

        private void OnDestroy()
        {
            ClearAsteroids();
        }

        private void OnFrameChanged(CelestialBody frameBody)
        {
            // Reference-frame changes (entering/leaving a gravity well) invalidate the
            // local rock population — clear it so the next deep-space stretch re-populates
            // deterministically for its region.
            ClearAsteroids();
            _wasDeepSpace = IsOpenSpaceNow();
        }

        /// <summary>
        /// True when the player is in OPEN SPACE — either the deep-space star frame, or
        /// inside a body's frame but well above its surface/atmosphere (high orbit,
        /// transfer trajectories). 9.2.0: rocks are no longer exclusive to deep space —
        /// space feels populated the whole journey, exactly like planets, just smaller.
        /// </summary>
        private bool IsOpenSpaceNow()
        {
            var origin = SpaceOrigin.Instance;
            if (origin == null) return false;
            if (origin.IsDeepSpace) return true;

            var frame = origin.FrameBody;
            if (frame == null || origin.viewer == null) return false;
            float altitude = Vector3.Distance(origin.viewer.position, frame.transform.position)
                             - frame.SurfaceRadius;
            float floor = Mathf.Max(minOrbitAltitudeMeters, frame.AtmosphereHeight * 1.25f);
            return altitude > floor;
        }

        private void Update()
        {
            var origin = SpaceOrigin.Instance;
            var registry = CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady) return;

            bool openSpace = IsOpenSpaceNow();
            if (openSpace != _wasDeepSpace)
            {
                if (!openSpace) ClearAsteroids();
                _wasDeepSpace = openSpace;
            }

            // Cull rocks that drifted out of range (or into a gravity well).
            Vector3 viewerPos = origin.viewer != null ? origin.viewer.position : transform.position;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var rock = _live[i];
                if (rock == null)
                {
                    _live.RemoveAt(i);
                    continue;
                }
                float d = Vector3.Distance(rock.transform.position, viewerPos);
                if (d > despawnDistanceMeters || !openSpace)
                {
                    Destroy(rock.gameObject);
                    _live.RemoveAt(i);
                }
            }

            if (!openSpace) return;

            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer > 0f) return;
            _spawnTimer = spawnIntervalSeconds;

            // Keep the volume populated: when below half the cap, spawn a burst.
            int deficit = maxAsteroids - _live.Count;
            if (deficit <= 0) return;

            // Seeded per cosmic region (same corner of space → same style of field) with a
            // per-attempt nonce so consecutive spawn attempts never re-roll the same rock.
            double3 viewerCosmic = origin.ViewerCosmicKm;
            long cellX = (long)math.floor(viewerCosmic.x / regionCellKm);
            long cellY = (long)math.floor(viewerCosmic.y / regionCellKm);
            long cellZ = (long)math.floor(viewerCosmic.z / regionCellKm);
            uint regionHash = (uint)((cellX * 73856093L) ^ (cellY * 19349663L) ^ (cellZ * 83492791L)) ^ 0x5EEDC0DEu;
            _attemptNonce++;
            uint seed = (regionHash ^ (_attemptNonce * 2654435761u)) | 1u;
            var rng = new Random(seed);

            int attempts = 0;
            while (deficit > 0 && attempts < 10)
            {
                attempts++;
                float distance = rng.NextFloat(spawnRingMeters.x, spawnRingMeters.y);
                double3 dir = RandomUnit(ref rng);
                // Cosmic coordinates are KM — the ring distance is METRES (9.3.0 fix:
                // metres were added as km, spawning every rock 900–6,000 km away where
                // it was culled immediately — "no asteroids ever appear").
                double3 spawnCosmicKm = viewerCosmic + dir * (distance / 1000d);
                Vector3 pos = origin.GetScenePos(spawnCosmicKm);
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) continue;

                if (HasRockNear(pos, minSeparationMeters)) continue;
                if (IsInsidePlanet(origin, registry, spawnCosmicKm)) continue;

                MaterialId material = orePool.Length > 0
                    ? orePool[rng.NextInt(0, orePool.Length)]
                    : MaterialId.Stone;
                float radius = rng.NextFloat(asteroidRadiusMeters.x, asteroidRadiusMeters.y);
                int rockSeed = rng.NextInt(1, int.MaxValue);

                var asteroid = SpaceAsteroid.Spawn(pos, radius, material,
                    ResolveDrops(material), rockSeed, RandomDrift(ref rng));
                asteroid.transform.SetParent(transform, false);
                origin.RegisterRoot(asteroid.transform);
                _live.Add(asteroid);
                deficit--;
                if (_live.Count == 1)
                    Debug.Log($"[SpaceAsteroidField] Rocks populating open space ({Vector3.Distance(pos, viewerPos):0} m out).");

                // ── Cluster spawn (9.15.0): real belts form in families. ──
                if (rng.NextFloat() < clusterChance)
                {
                    int extra = rng.NextInt(clusterExtraRocks.x, clusterExtraRocks.y + 1);
                    for (int m = 0; m < extra && deficit > 0; m++)
                    {
                        float cDist = rng.NextFloat(clusterRadiusMeters.x, clusterRadiusMeters.y);
                        Vector3 cPos = pos + RandomUnitVector(ref rng) * cDist;
                        if (HasRockNear(cPos, minSeparationMeters * 0.6f)) continue;
                        double3 cCosmicKm = viewerCosmic + (double3)(float3)(cPos - viewerPos) / 1000d;
                        if (IsInsidePlanet(origin, registry, cCosmicKm)) continue;

                        MaterialId cMaterial = rng.NextFloat() < clusterSharedMaterialChance && orePool.Length > 0
                            ? material
                            : (orePool.Length > 0 ? orePool[rng.NextInt(0, orePool.Length)] : MaterialId.Stone);
                        float cRadius = rng.NextFloat(asteroidRadiusMeters.x,
                            Mathf.Max(asteroidRadiusMeters.x, radius * 0.8f));
                        int cSeed = rng.NextInt(1, int.MaxValue);

                        var member = SpaceAsteroid.Spawn(cPos, cRadius, cMaterial,
                            ResolveDrops(cMaterial), cSeed, RandomDrift(ref rng));
                        member.transform.SetParent(transform, false);
                        origin.RegisterRoot(member.transform);
                        _live.Add(member);
                        deficit--;
                    }
                }
            }
        }

        private Vector3 RandomDrift(ref Random rng)
        {
            float speed = rng.NextFloat(driftSpeedMetersPerSec.x, driftSpeedMetersPerSec.y);
            return RandomUnitVector(ref rng) * speed;
        }

        private Vector3 RandomUnitVector(ref Random rng)
        {
            // Uniform direction on the sphere (no polar pinching).
            float z = rng.NextFloat(-1f, 1f);
            float a = rng.NextFloat(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
        }

        private bool HasRockNear(Vector3 pos, float minDistance)
        {
            float minSq = minDistance * minDistance;
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i] == null) continue;
                if ((_live[i].transform.position - pos).sqrMagnitude < minSq) return true;
            }
            return false;
        }

        private static bool IsInsidePlanet(SpaceOrigin origin, CosmicRegistry registry, double3 cosmicKm)
        {
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                var b = registry.Bodies[i];
                if (b == null || b.settings == null) continue;
                double3 abs = registry.CosmicPositionOf(b);
                double d = math.length(abs - cosmicKm);
                double r = b.settings.radiusKm * 2d; // generous margin — never spawn inside a body
                if (d < r) return true;
            }
            return false;
        }

        private static double3 RandomUnit(ref Random rng)
        {
            double3 v = new double3(rng.NextDouble() * 2d - 1d,
                                    rng.NextDouble() * 2d - 1d,
                                    rng.NextDouble() * 2d - 1d);
            double len = math.length(v);
            return len < 1e-6 ? new double3(1d, 0d, 0d) : v / len;
        }

        /// <summary>
        /// Resolve the ore ItemDefinitions for a material. The catalogue mirrors the
        /// save system's item cache: Resources-visible assets first, then any loaded
        /// asset in the project (editor play mode).
        /// </summary>
        private ItemDefinition[] ResolveDrops(MaterialId material)
        {
            if (_dropCache.TryGetValue(material, out var cached) && cached != null) return cached;

            var list = new List<ItemDefinition>();
            string itemId = material == MaterialId.Ice ? "ice"
                : material == MaterialId.Iron ? "iron"
                : material == MaterialId.Nickel ? "nickel"
                : material == MaterialId.Silicon ? "silicon"
                : material == MaterialId.Cobalt ? "cobalt"
                : material == MaterialId.Silver ? "silver"
                : material == MaterialId.Gold ? "gold"
                : material == MaterialId.Platinum ? "platinum"
                : material == MaterialId.Uranium ? "uranium"
                : "stone";

            foreach (var item in Resources.LoadAll<ItemDefinition>(""))
            {
                if (item == null) continue;
                if (string.Equals(item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                    list.Add(item);
            }
#if UNITY_EDITOR
            if (list.Count == 0)
            {
                foreach (var item in Resources.FindObjectsOfTypeAll<ItemDefinition>())
                {
                    if (item == null) continue;
                    if (string.Equals(item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(item);
                        break;
                    }
                }
            }
#endif
            _dropCache[material] = list.ToArray();
            return _dropCache[material];
        }

        private void ClearAsteroids()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i] != null) Destroy(_live[i].gameObject);
            }
            _live.Clear();
        }
    }
}
