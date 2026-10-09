// Assets/Scripts/VoxelEngine/Combat/EnemySpawner.cs
//
// Spawns supplemental Ghoul scouts near players, or a pressure-scaled pack at an
// active static industrial source. Top-level instances keep radial physics correct
// on spherical worlds.

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
        [Tooltip("Base radius for the strongest active static source. Severe source pressure extends this up to twice as far. The authored value is never overwritten.")]
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
                if (ShouldCull(g))
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
            int slots = ecologicalCap - _alive.Count;
            if (slots <= 0) return;

            // A stopped or filtered machine ceases to attract new scouts. Inside the
            // authored radius, activity alone is enough for one scout. Cell pressure
            // extends that radius and grows the pack. The live cap still binds.
            PollutionEmitter industrialSource = null;
            float sourcePressure = 0f;
            if (PollutionEmitter.TryFindRecruitingSource(ppos, industrialAttractionRange,
                    minimumIndustrialPressure, out PollutionEmitter candidate, out sourcePressure,
                    surfaceSourcesOnly: true))
            {
                industrialSource = candidate;
            }

            var placed = industrialSource != null
                ? industrialSource.GetComponentInParent<VoxelEngine.Building.PlacedBlock>()
                : null;
            if (industrialSource != null && placed == null) industrialSource = null;
            Vector3 spawnAnchor = placed != null ? placed.transform.position : ppos;

            // Ghouls are surface hunters: no streamed world, meaningful gravity or real
            // footing means no spawn. Grid hulls are not accepted as terrain footing.
            if (VoxelEngine.Core.ActiveWorld.Current == null) return;
            if (VoxelEngine.Cosmos.GravityProvider.GetGravity(spawnAnchor).magnitude < 0.5f) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(spawnAnchor);
            int wanted = industrialSource != null
                ? Mathf.Min(slots, PollutionScentRules.PackSize(sourcePressure))
                : 1;
            int ambushBudget = industrialSource != null
                ? PollutionScentRules.AmbushCount(sourcePressure, wanted)
                : 0;
            bool raidLogistics = industrialSource != null && PollutionScentRules.RaidsLogistics(sourcePressure);
            float baseAngle = Random.Range(0f, 360f);
            int spawned = 0;
            int ambushes = 0;
            for (int member = 0; member < wanted; member++)
            {
                if (ambushes < ambushBudget
                    && PollutionScentRules.TryAmbushPoint(spawnAnchor, ppos, up, member, out Vector3 ambushPoint)
                    && TrySpawnAt(ambushPoint, up, ppos - ambushPoint, industrialSource, sourcePressure,
                        ambush: true, raidLogistics: raidLogistics))
                {
                    ambushes++;
                    spawned++;
                    continue;
                }

                float angle = baseAngle + member * (360f / wanted);
                float distance = Random.Range(spawnNearMin, spawnNearMax);
                if (TrySpawnGhoul(spawnAnchor, up, angle, distance, industrialSource, sourcePressure,
                        ambush: false, raidLogistics: raidLogistics))
                    spawned++;
            }
            if (spawned > 0 && industrialSource != null)
                ShowIndustrialThreatNotice(industrialSource, spawned, ambushes);
        }

        private bool ShouldCull(EnemyGhoul ghoul)
        {
            // A live industrial objective keeps the scout at the machine even when the
            // player has not walked into ordinary despawn range. Stopping the source
            // clears that leash on the ghoul; the player-distance cull then applies.
            if (ghoul.HasActiveIndustrialTarget
                && Vector3.Distance(ghoul.transform.position, ghoul.IndustrialTargetPosition) <= despawnRange)
                return false;
            return DistanceToNearestPlayer(ghoul.transform.position) > despawnRange;
        }

        private bool TrySpawnGhoul(Vector3 anchor, Vector3 up, float angleDegrees, float distance,
            PollutionEmitter industrialSource, float sourcePressure, bool ambush, bool raidLogistics)
        {
            Vector3 north = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (north.sqrMagnitude < 0.001f) north = Vector3.ProjectOnPlane(Vector3.right, up);
            if (north.sqrMagnitude < 0.001f) return false;
            north.Normalize();
            Vector3 east = Vector3.Cross(up, north).normalized;
            float radians = angleDegrees * Mathf.Deg2Rad;
            Vector3 tangent = (north * Mathf.Cos(radians) + east * Mathf.Sin(radians)) * distance;
            if (tangent.sqrMagnitude < 0.001f) return false;
            return TrySpawnAt(anchor + tangent, up, -tangent, industrialSource, sourcePressure, ambush, raidLogistics);
        }

        private bool TrySpawnAt(Vector3 desired, Vector3 up, Vector3 faceDirection,
            PollutionEmitter industrialSource, float sourcePressure, bool ambush, bool raidLogistics)
        {
            Vector3 face = Vector3.ProjectOnPlane(faceDirection, up);
            if (face.sqrMagnitude < 0.001f) return false;
            face.Normalize();

            Vector3 probe = desired + up * 2.5f;
            if (!Physics.Raycast(probe + up * 2f, -up, out var groundHit, 12f,
                    ~0, QueryTriggerInteraction.Ignore)) return false;
            if (groundHit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null) return false;
            if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld sphere)
            {
                Vector3 local = sphere.body.transform.InverseTransformPoint(groundHit.point);
                Vector3 normal = local.normalized;
                if (!sphere.TryGroundSurface(ref local, ref normal)) return false;
                Vector3 terrainPoint = sphere.body.transform.TransformPoint(local);
                if (Vector3.Distance(terrainPoint, groundHit.point) > 0.2f
                    || !sphere.IsDryFooting(terrainPoint)) return false;
            }

            Vector3 spawnPos = groundHit.point + up * 1.2f;
            var go = Instantiate(ghoulPrefab, spawnPos, Quaternion.LookRotation(face, up));
            var ghoul = go.GetComponent<EnemyGhoul>();
            if (ghoul == null)
            {
                Debug.LogError("[EnemySpawner] Prefab has no EnemyGhoul component!");
                Destroy(go);
                return false;
            }

            if (industrialSource != null)
            {
                ghoul.SetIndustrialTarget(industrialSource);
                // Ambushers keep the prefab detection range so they do not see the
                // player from the hiding point. Pack members walking to the source
                // still use the pressure-scaled range.
                if (!ambush)
                    ghoul.detectRange = PollutionScentRules.EscalatedDetectionRange(ghoul.detectRange, sourcePressure);
                if (raidLogistics) ghoul.EnableLogisticsRaid();
                if (ambush) ghoul.BeginAmbush(PollutionScentRules.AmbushHoldSeconds);
            }
            _alive.Add(ghoul);
            return true;
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

        private void ShowIndustrialThreatNotice(PollutionEmitter source, int count, int ambushes)
        {
            var player = VoxelEngine.Player.PlayerStats.Instance;
            if (source == null || count <= 0 || Time.time < _nextThreatNotice || player == null) return;
            _nextThreatNotice = Time.time + Mathf.Max(5f, threatNoticeCooldown);
            string bearing = PollutionScentRules.FormatDirection(
                player.transform.position, source.ReleasePoint, "nearby");
            string arrival = count == 1 ? "a Ghoul scout" : "a Ghoul pack of " + count;
            if (ambushes > 0) arrival += ", with an ambush on the approach";
            VoxelEngine.UI.BuildFeedbackHud.Show("INDUSTRIAL SCENT",
                source.DisplayName + " (" + bearing + ") has attracted " + arrival + ".", null,
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
