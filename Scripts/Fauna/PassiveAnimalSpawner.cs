// Assets/Scripts/VoxelEngine/Fauna/PassiveAnimalSpawner.cs
//
// Spawns passive livestock near the player as TOP-LEVEL objects and auto-creates
// itself at runtime. Loads every prefab under Resources/Livestock (Cow / Sheep /
// Pig), caps the live population, and despawns animals that wander too far away.
// Same proven pattern as EnemySpawner — top-level + radial gravity settle the
// animals onto the spherical surface correctly.

using System.Collections.Generic;
using UnityEngine;

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

        private void Awake()
        {
            if (animalPrefabs == null || animalPrefabs.Length == 0)
                animalPrefabs = Resources.LoadAll<GameObject>("Livestock");
            _nextSpawn = Time.time + startGrace;
        }

        private void Update()
        {
            var player = VoxelEngine.Player.PlayerStats.Instance;
            if (player == null) return;
            Vector3 ppos = player.transform.position;

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
            _nextSpawn = Time.time + spawnInterval;

            if (animalPrefabs == null || animalPrefabs.Length == 0) return;
            if (_alive.Count >= maxAlive) return;

            // Spawn near the player on the tangent plane (radial gravity settles it down).
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ppos);
            Vector3 rand = Random.onUnitSphere;
            Vector3 tangent = rand - Vector3.Project(rand, up);
            if (tangent.sqrMagnitude < 0.001f) return;
            tangent = tangent.normalized * Random.Range(spawnNearMin, spawnNearMax);
            Vector3 spawnPos = ppos + tangent + up * 2.5f;

            var prefab = animalPrefabs[Random.Range(0, animalPrefabs.Length)];
            var go = Instantiate(prefab, spawnPos, Quaternion.LookRotation(-tangent, up));
            var animal = go.GetComponent<PassiveAnimal>();
            if (animal != null) _alive.Add(animal);
            else Destroy(go);
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
            }
        }
    }
}
