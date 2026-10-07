// Assets/Scripts/VoxelEngine/Fauna/PassiveAnimalSpawner.cs
//
// Spawns passive livestock near the player as TOP-LEVEL objects and auto-creates
// itself at runtime (DontDestroyOnLoad so it survives the MainMenu -> Game scene
// transition). Loads every prefab under Resources/Livestock (Cow / Sheep / Pig),
// caps the live population, and despawns animals that wander too far away.
//
// NOTE: livestock ALSO spawn via temperate biome scatter (Step 25 injects them into
// Forest/Plains/Steppes) — that is the primary, reliable spawn path (same as the
// Ghoul). This near-player spawner is a population-capped supplement.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Environment;

namespace VoxelEngine.Fauna
{
    public class PassiveAnimalSpawner : MonoBehaviour
    {
        public GameObject[] animalPrefabs;
        public float spawnInterval = 14f;
        public int   maxAlive      = 8;
        public float spawnNearMin  = 16f;
        public float spawnNearMax  = 40f;
        public float despawnRange  = 95f;
        public float startGrace    = 6f;

        private float _nextSpawn;
        private static readonly List<PassiveAnimal> _alive = new List<PassiveAnimal>();
        private static bool _autoCreated;

        private bool LoadPrefabs()
        {
            if (animalPrefabs != null && animalPrefabs.Length > 0) return true;
            animalPrefabs = Resources.LoadAll<GameObject>("Livestock");
            return animalPrefabs != null && animalPrefabs.Length > 0;
        }

        private void Awake()
        {
            LoadPrefabs();
            _nextSpawn = Time.time + startGrace;
        }

        private void Update()
        {
            // 14.54.0 - the sync pump rides the spawner's heartbeat: hosts
            // stream the herd, guests ease their replicas. Runs either way.
            VoxelEngine.Networking.AnimalSync.Pump();

            var player = VoxelEngine.Player.PlayerStats.Instance;
            if (player == null) return;

            // Guests never spawn, cull or despawn fauna - the host's stream
            // is the only herd that exists on this machine.
            if (VoxelEngine.Networking.NetworkSession.Mode
                == VoxelEngine.Networking.SessionMode.Client) return;
            Vector3 ppos = player.transform.position;

            bool vacuum = VoxelEngine.GridSystem.AtmosphereManager.IsInSpace(ppos)
                          || VoxelEngine.Cosmos.GravityProvider.ActiveBody == null;
            EcologyReading ecology = EcologyPressure.Sample(ppos);
            if (vacuum || !ecology.SupportsLivestock)
            {
                // Conventional farm animals are intentionally absent from vacuum,
                // toxic, frozen and otherwise incompatible themed bodies.
                for (int i = _alive.Count - 1; i >= 0; i--)
                {
                    var a = _alive[i];
                    if (a != null) Destroy(a.gameObject);
                    _alive.RemoveAt(i);
                }
                return;
            }

            // Cull dead + despawn far.
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                var a = _alive[i];
                if (a == null) { _alive.RemoveAt(i); continue; }
                if (Vector3.Distance(a.transform.position, ppos) > despawnRange)
                {
                    Destroy(a.gameObject);
                    _alive.RemoveAt(i);
                }
            }

            if (Time.time < _nextSpawn) return;
            _nextSpawn = Time.time + spawnInterval / Mathf.Max(0.12f, ecology.PassiveActivity01);

            if (!LoadPrefabs())
            {
                Debug.LogWarning("[LivestockSpawner] No prefabs found in Resources/Livestock — run Step 25 first.");
                return;
            }

            int ecologicalCap = Mathf.FloorToInt(maxAlive * ecology.PassiveActivity01);
            if (_alive.Count >= ecologicalCap) return;

            // 14.64.0 — herds are SURFACE life (same rule the EnemySpawner got in
            // 14.60.0): no streamed world, no meaningful gravity, or no real ground
            // under the spawn point means no spawn. Low partial gravity high above a
            // planet otherwise seeded animals in open space next to ships.
            if (VoxelEngine.Core.ActiveWorld.Current == null) return;
            if (VoxelEngine.Cosmos.GravityProvider.GetGravity(ppos).magnitude < 0.5f) return;

            // Spawn near the player on the tangent plane (radial gravity settles it down).
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ppos);
            Vector3 rand = Random.onUnitSphere;
            Vector3 tangent = rand - Vector3.Project(rand, up);
            if (tangent.sqrMagnitude < 0.001f) return;
            tangent = tangent.normalized * Random.Range(spawnNearMin, spawnNearMax);
            Vector3 spawnPos = ppos + tangent + up * 2.5f;

            // Ground check: the spawn point must sit over real footing (and never a
            // ship hull) — a player flying high above the surface no longer seeds
            // animals in midair around him.
            if (!Physics.Raycast(spawnPos + up * 2f, -up, out var groundHit, 12f,
                    ~0, QueryTriggerInteraction.Ignore)) return;
            if (groundHit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null) return;
            spawnPos = groundHit.point + up * 1.2f;

            var prefab = animalPrefabs[Random.Range(0, animalPrefabs.Length)];
            var go = Instantiate(prefab, spawnPos, Quaternion.LookRotation(-tangent, up));
            var animal = go.GetComponent<PassiveAnimal>();
            if (animal != null) _alive.Add(animal);
            else
            {
                Debug.LogError("[LivestockSpawner] Prefab has no PassiveAnimal component!");
                Destroy(go);
            }
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_autoCreated) return;
            _autoCreated = true;
            if (UnityEngine.Object.FindAnyObjectByType<PassiveAnimalSpawner>() == null)
            {
                var go = new GameObject("PassiveAnimalSpawner");
                go.AddComponent<PassiveAnimalSpawner>();
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
        }
    }
}
