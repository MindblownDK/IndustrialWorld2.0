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
        public int maxAsteroids = 28;

        [Tooltip("Seconds between spawn attempts (in open space).")]
        public float spawnIntervalSeconds = 5f;

        [Tooltip("Asteroids spawn in this ring around the player (metres).")]
        public Vector2 spawnRingMeters = new Vector2(900f, 6000f);

        [Tooltip("Asteroids beyond this distance are culled (metres).")]
        public float despawnDistanceMeters = 12000f;

        [Tooltip("Minimum clearance between spawned asteroids (metres).")]
        public float minSeparationMeters = 650f;

        [Tooltip("Asteroid radius range (metres).")]
        public Vector2 asteroidRadiusMeters = new Vector2(12f, 90f);

        [Tooltip("Minimum altitude (m) above a body's surface before rocks appear while inside its frame — keeps the sky over bases clean while making high orbit and transfers feel populated.")]
        public float minOrbitAltitudeMeters = 12000f;

        [Tooltip("Cosmic cell size (km) used to seed deterministic regions.")]
        public double regionCellKm = 4096d;

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
            while (deficit > 0 && attempts < 8)
            {
                attempts++;
                float distance = rng.NextFloat(spawnRingMeters.x, spawnRingMeters.y);
                double3 dir = RandomUnit(ref rng);
                Vector3 pos = origin.GetScenePos(viewerCosmic + dir * distance);

                if (HasRockNear(pos, minSeparationMeters)) continue;
                if (IsInsidePlanet(origin, registry, viewerCosmic + dir * distance)) continue;

                MaterialId material = orePool.Length > 0
                    ? orePool[rng.NextInt(0, orePool.Length)]
                    : MaterialId.Stone;
                float radius = rng.NextFloat(asteroidRadiusMeters.x, asteroidRadiusMeters.y);
                int rockSeed = rng.NextInt(1, int.MaxValue);

                var asteroid = SpaceAsteroid.Spawn(pos, radius, material,
                    ResolveDrops(material), rockSeed);
                asteroid.transform.SetParent(transform, false);
                origin.RegisterRoot(asteroid.transform);
                _live.Add(asteroid);
                deficit--;
            }
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
