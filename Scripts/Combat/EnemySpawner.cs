// Assets/Scripts/VoxelEngine/Combat/EnemySpawner.cs
//
// Spawns enemy Ghouls near the player as TOP-LEVEL objects. Auto-creates at runtime.
// Spawns near the player as a top-level object so radial physics works on spheres.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Environment;

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
        }

        private void Update()
        {
            // 14.55.0 - the hostile sync pump rides the spawner's heartbeat:
            // hosts stream the horde, guests ease their replicas. Runs always.
            VoxelEngine.Networking.HostileSync.Pump();

            var player = VoxelEngine.Player.PlayerStats.Instance;
            if (player == null) return;

            // Guests never spawn, cull or despawn hostiles - the host's
            // stream is the only horde that exists on this machine.
            if (VoxelEngine.Networking.NetworkSession.Mode
                == VoxelEngine.Networking.SessionMode.Client) return;
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

            if (VoxelEngine.Cosmos.GravityProvider.ActiveBody == null
                || VoxelEngine.GridSystem.AtmosphereManager.IsInSpace(ppos)) return;

            EcologyReading ecology = EcologyPressure.Sample(ppos);
            if (Time.time < _nextSpawn) return;
            _nextSpawn = Time.time + spawnInterval / ecology.HostilePressureMultiplier;

            if (ghoulPrefab == null)
            {
                ghoulPrefab = Resources.Load<GameObject>("Enemies/Ghoul");
                if (ghoulPrefab == null)
                {
                    Debug.LogWarning("[EnemySpawner] Ghoul prefab not found in Resources/Enemies/Ghoul! Run Step 23 first.");
                    return;
                }
            }

            int ecologicalCap = Mathf.CeilToInt(maxAlive * ecology.HostilePressureMultiplier);
            if (_alive.Count >= ecologicalCap)
            {
                return;
            }

            // 14.60.0 - ghouls are SURFACE hunters: no world streamed here, no
            // meaningful gravity, or no ground under the spawn point means no
            // spawn. This is what put ghouls in open space next to ships.
            if (VoxelEngine.Core.ActiveWorld.Current == null) return;
            if (VoxelEngine.Cosmos.GravityProvider.GetGravity(ppos).magnitude < 0.5f) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ppos);
            Vector3 rand = Random.onUnitSphere;
            Vector3 tangent = rand - Vector3.Project(rand, up);
            if (tangent.sqrMagnitude < 0.001f) return;
            tangent = tangent.normalized * Random.Range(spawnNearMin, spawnNearMax);
            Vector3 spawnPos = ppos + tangent + up * 2.5f;

            // Ground check: the spawn point must sit over real footing (and not a
            // ship hull) - a player flying high no longer seeds ghouls in midair.
            if (!Physics.Raycast(spawnPos + up * 2f, -up, out var groundHit, 12f,
                    ~0, QueryTriggerInteraction.Ignore)) return;
            if (groundHit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null) return;
            spawnPos = groundHit.point + up * 1.2f;

            var go = Instantiate(ghoulPrefab, spawnPos, Quaternion.LookRotation(-tangent, up));
            var ghoul = go.GetComponent<EnemyGhoul>();
            if (ghoul != null)
            {
                _alive.Add(ghoul);
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
            if (UnityEngine.Object.FindAnyObjectByType<EnemySpawner>() == null)
            {
                var go = new GameObject("EnemySpawner");
                // 14.55.1 - AfterSceneLoad fires ONCE per app run, in whatever
                // scene loads first. Entering the game FROM the main menu used
                // to destroy the spawner with the menu scene and never recreate
                // it - no ghouls, and no HostileSync pump. Persist it instead
                // (PassiveAnimalSpawner has done this all along).
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<EnemySpawner>();
            }
        }
    }
}
