// Assets/Scripts/VoxelEngine/Combat/EnemySpawner.cs
//
// Spawns supplemental Ghoul scouts near players or active static industry.
// Top-level instances keep radial physics correct on spherical worlds.

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

        [Header("Industrial Attraction")]
        [Tooltip("Strongest active static pollution source inside this radius becomes the scout objective.")]
        public float industrialAttractionRange = 48f;
        [Range(0f, 1f)] public float minimumIndustrialPressure = 0.03f;
        [Min(5f)] public float threatNoticeCooldown = 30f;

        private float _nextSpawn;
        private float _nextThreatNotice;
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

            // Guests never spawn, cull or despawn hostiles - the host's stream is the only
            // horde that exists on this machine. A dedicated host may have no local PlayerStats,
            // so a replicated player avatar is also a valid population focus.
            if (VoxelEngine.Networking.NetworkSession.Mode
                == VoxelEngine.Networking.SessionMode.Client) return;
            if (!TryGetSpawnFocus(out Vector3 ppos)) return;

            // Cull dead scouts and those no longer close to any connected player.
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                var g = _alive[i];
                if (g == null) { _alive.RemoveAt(i); continue; }
                if (DistanceToNearestPlayer(g.transform.position) > despawnRange)
                {
                    Destroy(g.gameObject);
                    _alive.RemoveAt(i);
                }
            }

            if (VoxelEngine.Cosmos.GravityProvider.ActiveBody == null
                || VoxelEngine.GridSystem.AtmosphereManager.IsInSpace(ppos)) return;

            if (!IndustrialWorld.Simulation.EcologyProfiles.AllowsGhoul(
                    VoxelEngine.Cosmos.GravityProvider.ActiveBody.settings)) return;

            EcologyReading ecology = EcologyPressure.Sample(ppos);
            if (Time.time < _nextSpawn) return;
            _nextSpawn = Time.time + spawnInterval / ecology.HostilePressureMultiplier;

            if (ghoulPrefab == null)
            {
                ghoulPrefab = Resources.Load<GameObject>("Enemies/Ghoul");
                if (ghoulPrefab == null)
                {
                    Debug.LogWarning("[EnemySpawner] Ghoul prefab not found in Resources/Enemies/Ghoul! Run Voxel Engine Setup -> Creatures, Enemies & Bosses -> Build the Ghoul enemy.");
                    return;
                }
            }

            int ecologicalCap = Mathf.CeilToInt(maxAlive * ecology.HostilePressureMultiplier);
            if (_alive.Count >= ecologicalCap)
            {
                return;
            }

            // Active static machinery becomes the objective once its local pollution has
            // registered. A stopped or filtered machine ceases to attract new scouts.
            PollutionEmitter industrialSource = null;
            if (ecology.Pressure01 >= minimumIndustrialPressure
                && PollutionEmitter.TryFindStrongestActiveNear(ppos, industrialAttractionRange,
                    out PollutionEmitter candidate, surfaceSourcesOnly: true)
                && candidate.GetComponentInParent<VoxelEngine.Building.PlacedBlock>() != null)
            {
                industrialSource = candidate;
            }
            Vector3 spawnAnchor = industrialSource != null
                ? industrialSource.GetComponentInParent<VoxelEngine.Building.PlacedBlock>().transform.position
                : ppos;

            // Ghouls are surface hunters: no streamed world, meaningful gravity or real
            // footing means no spawn. Grid hulls are not accepted as terrain footing.
            if (VoxelEngine.Core.ActiveWorld.Current == null) return;
            if (VoxelEngine.Cosmos.GravityProvider.GetGravity(spawnAnchor).magnitude < 0.5f) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(spawnAnchor);
            Vector3 rand = Random.onUnitSphere;
            Vector3 tangent = rand - Vector3.Project(rand, up);
            if (tangent.sqrMagnitude < 0.001f) return;
            tangent = tangent.normalized * Random.Range(spawnNearMin, spawnNearMax);
            Vector3 spawnPos = spawnAnchor + tangent + up * 2.5f;

            if (!Physics.Raycast(spawnPos + up * 2f, -up, out var groundHit, 12f,
                    ~0, QueryTriggerInteraction.Ignore)) return;
            if (groundHit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null) return;
            spawnPos = groundHit.point + up * 1.2f;

            var go = Instantiate(ghoulPrefab, spawnPos, Quaternion.LookRotation(-tangent, up));
            var ghoul = go.GetComponent<EnemyGhoul>();
            if (ghoul != null)
            {
                if (industrialSource != null)
                {
                    ghoul.SetIndustrialTarget(industrialSource);
                    ShowIndustrialThreatNotice(industrialSource);
                }
                _alive.Add(ghoul);
            }
            else
            {
                Debug.LogError("[EnemySpawner] Prefab has no EnemyGhoul component!");
                Destroy(go);
            }
        }

        private static bool TryGetSpawnFocus(out Vector3 position)
        {
            var local = VoxelEngine.Player.PlayerStats.Instance;
            if (local != null)
            {
                position = local.transform.position;
                return true;
            }
            foreach (var avatar in VoxelEngine.Networking.PlayerAvatar.All)
            {
                if (avatar == null) continue;
                position = avatar.transform.position;
                return true;
            }
            position = default;
            return false;
        }

        private static float DistanceToNearestPlayer(Vector3 position)
        {
            float bestSq = float.MaxValue;
            var local = VoxelEngine.Player.PlayerStats.Instance;
            if (local != null) bestSq = (local.transform.position - position).sqrMagnitude;
            foreach (var avatar in VoxelEngine.Networking.PlayerAvatar.All)
            {
                if (avatar == null) continue;
                float distanceSq = (avatar.transform.position - position).sqrMagnitude;
                if (distanceSq < bestSq) bestSq = distanceSq;
            }
            return bestSq < float.MaxValue ? Mathf.Sqrt(bestSq) : float.MaxValue;
        }

        private void ShowIndustrialThreatNotice(PollutionEmitter source)
        {
            if (source == null || Time.time < _nextThreatNotice
                || VoxelEngine.Player.PlayerStats.Instance == null) return;
            _nextThreatNotice = Time.time + Mathf.Max(5f, threatNoticeCooldown);
            VoxelEngine.UI.BuildFeedbackHud.Show("INDUSTRIAL SCENT",
                source.DisplayName + " has attracted a Ghoul scout.", null,
                new Color(0.92f, 0.42f, 0.16f));
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
