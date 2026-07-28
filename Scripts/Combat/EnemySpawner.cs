// Assets/Scripts/VoxelEngine/Combat/EnemySpawner.cs
//
// Spawns enemy Ghouls near the player as TOP-LEVEL objects (not parented to chunks),
// which is required because a Rigidbody enemy on a rotating spherical planet must live
// in world/physics space — the static biome-scatter pipeline parents to chunks and
// breaks dynamic physics bodies. Auto-creates itself at runtime; loads the Ghoul prefab
// from Resources. Capped population + far-despawn so the world never floods.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Combat
{
    public class EnemySpawner : MonoBehaviour
    {
        [Header("Spawning")]
        public GameObject ghoulPrefab;
        public float spawnInterval = 14f;
        public int   maxAlive      = 5;
        public float spawnNearMin  = 20f;
        public float spawnNearMax  = 42f;
        public float despawnRange  = 95f;
        public float startGrace    = 6f;

        private float _nextSpawn;
        private static readonly List<EnemyGhoul> _alive = new List<EnemyGhoul>();

        private void Awake()
        {
            if (ghoulPrefab == null) ghoulPrefab = Resources.Load<GameObject>("Enemies/Ghoul");
            _nextSpawn = Time.time + startGrace;
        }

        private void Update()
        {
            var player = VoxelEngine.Player.PlayerStats.Instance;
            if (player == null) return;
            Vector3 ppos = player.transform.position;

            // Cull dead + despawn ones that wandered too far.
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
                if (ghoulPrefab == null) return;
            }
            if (_alive.Count >= maxAlive) return;

            SpawnNearPlayer(ppos);
        }

        private void SpawnNearPlayer(Vector3 ppos)
        {
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ppos);
            // Pick a random direction in the local tangent plane (perpendicular to radial up).
            Vector3 rand = Random.onUnitSphere;
            Vector3 tangent = rand - Vector3.Project(rand, up);
            if (tangent.sqrMagnitude < 0.001f) return;
            tangent = tangent.normalized * Random.Range(spawnNearMin, spawnNearMax);

            // Raycast "down" (toward the core) to find the surface, then place slightly above it.
            Vector3 sample = ppos + tangent + up * 6f;
            if (Physics.Raycast(sample, -up, out var hit, 14f, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 spawnPos = hit.point + up * 0.6f;
                var go = Instantiate(ghoulPrefab, spawnPos, Quaternion.LookRotation(-tangent, up));
                var g = go.GetComponent<EnemyGhoul>();
                if (g != null) _alive.Add(g);
            }
        }

        // Auto-create one spawner per session (persists for the loaded scene).
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EnemySpawner>() == null)
            {
                var go = new GameObject("EnemySpawner");
                go.AddComponent<EnemySpawner>();
            }
        }
    }
}
