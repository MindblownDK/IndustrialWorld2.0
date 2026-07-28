// Assets/Scripts/VoxelEngine/Combat/EnemySpawner.cs
//
// Spawns enemy Ghouls near the player as TOP-LEVEL objects. Auto-creates at runtime.
// Comprehensive logging so spawn issues are immediately diagnosable in the Console.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Combat
{
    public class EnemySpawner : MonoBehaviour
    {
        public GameObject ghoulPrefab;
        public float spawnInterval = 10f;
        public int   maxAlive      = 5;
        public float spawnNearMin  = 18f;
        public float spawnNearMax  = 36f;
        public float despawnRange  = 90f;
        public float startGrace    = 4f;

        private float _nextSpawn;
        private static readonly List<EnemyGhoul> _alive = new List<EnemyGhoul>();
        private static bool _autoCreated;

        private void Awake()
        {
            if (ghoulPrefab == null) ghoulPrefab = Resources.Load<GameObject>("Enemies/Ghoul");
            _nextSpawn = Time.time + startGrace;
            Debug.Log($"[EnemySpawner] Awake — prefab={(ghoulPrefab != null ? "OK" : "NULL")}, grace={startGrace}s");
        }

        private void Update()
        {
            var player = VoxelEngine.Player.PlayerStats.Instance;
            if (player == null) return;
            Vector3 ppos = player.transform.position;

            // Cull dead + despawn far
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                var g = _alive[i];
                if (g == null) { _alive.RemoveAt(i); continue; }
                if (Vector3.Distance(g.transform.position, ppos) > despawnRange)
                {
                    Destroy(g.gameObject);
                    _alive.RemoveAt(i);
                }
            }

            if (Time.time < _nextSpawn) return;
            _nextSpawn = Time.time + spawnInterval;

            if (ghoulPrefab == null)
            {
                ghoulPrefab = Resources.Load<GameObject>("Enemies/Ghoul");
                if (ghoulPrefab == null)
                {
                    Debug.LogWarning("[EnemySpawner] Ghoul prefab not found in Resources/Enemies/Ghoul! Run Step 23 first.");
                    return;
                }
            }

            if (_alive.Count >= maxAlive)
            {
                Debug.Log($"[EnemySpawner] At cap ({maxAlive}). Skipping.");
                return;
            }

            // Spawn near the player — no raycast needed (radial gravity settles the ghoul onto the surface).
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ppos);
            Vector3 rand = Random.onUnitSphere;
            Vector3 tangent = rand - Vector3.Project(rand, up);
            if (tangent.sqrMagnitude < 0.001f) return;
            tangent = tangent.normalized * Random.Range(spawnNearMin, spawnNearMax);
            Vector3 spawnPos = ppos + tangent + up * 2.5f;

            var go = Instantiate(ghoulPrefab, spawnPos, Quaternion.LookRotation(-tangent, up));
            var ghoul = go.GetComponent<EnemyGhoul>();
            if (ghoul != null)
            {
                _alive.Add(ghoul);
                Debug.Log($"[EnemySpawner] Spawned ghoul #{_alive.Count} at {spawnPos} (dist from player: {tangent.magnitude:F1}m)");
            }
            else
            {
                Debug.LogError("[EnemySpawner] Prefab has no EnemyGhoul component!");
                Destroy(go);
            }
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_autoCreated) return;
            _autoCreated = true;
            Debug.Log("[EnemySpawner] RuntimeInitialize — checking for existing spawner...");
            if (UnityEngine.Object.FindAnyObjectByType<EnemySpawner>() == null)
            {
                var go = new GameObject("EnemySpawner");
                go.AddComponent<EnemySpawner>();
                Debug.Log("[EnemySpawner] Created new spawner GameObject.");
            }
            else
            {
                Debug.Log("[EnemySpawner] Spawner already exists.");
            }
        }
    }
}
