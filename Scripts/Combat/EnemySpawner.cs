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

        // 17.17.2-dev: recruitment heat - a local, non-serialized slice of
        // pollution history. Serialized inspector fields keep their authored
        // values, so the sustained-activity requirement lives here instead:
        // heat builds while the spawner keeps seeing an active industrial
        // source (about four minutes to full) and bleeds off when industry
        // goes quiet (about ninety seconds to cold). Organized packs, ambushes
        // and logistics raids only answer at full heat; ordinary near-player
        // scouts are unaffected.
        private float _recruitHeat;
        private float _lastRecruitTick = -1f;

        // 17.18.0-dev: organized-event cadence. Waves and sieges answer the
        // slow residual-load history (PollutionThreat), and each keeps its own
        // cooldown so a qualifying district lives under threat pressure rather
        // than under a spawn-loop machine gun. Local, non-serialized state -
        // authored scene values stay untouched.
        private float _nextWaveAt;
        private float _nextSiegeAt;

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

            // 17.17.2-dev: update recruitment heat on every spawn tick and gate
            // organized industrial responses behind it. One furnace that just
            // started no longer calls packs within minutes; sustained industry
            // does. A lone scout may still answer immediately.
            float tickDt = _lastRecruitTick < 0f ? 0f : Mathf.Min(Time.time - _lastRecruitTick, 30f);
            _lastRecruitTick = Time.time;
            if (industrialSource != null)
                _recruitHeat = Mathf.Clamp01(_recruitHeat + tickDt / 240f);
            else
                _recruitHeat = Mathf.Clamp01(_recruitHeat - tickDt / 90f);
            if (industrialSource != null && _recruitHeat < 1f) industrialSource = null;

            Vector3 spawnAnchor = placed != null ? placed.transform.position : ppos;

            // Ghouls are surface hunters: no streamed world, meaningful gravity or real
            // footing means no spawn. Grid hulls are not accepted as terrain footing.
            if (VoxelEngine.Core.ActiveWorld.Current == null) return;
            if (VoxelEngine.Cosmos.GravityProvider.GetGravity(spawnAnchor).magnitude < 0.5f) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(spawnAnchor);

            // 17.18.0-dev: resolve the industrial threat tier from live readings.
            // The residual climate load is sampled at the outlet: it is the
            // district's own history (hours-scale, persisted in the pollution
            // sidecar), not the cell the player happens to stand in. The heat
            // gate has already run, so any surviving source means sustained
            // activity; tiers then decide whether that activity has earned an
            // organized answer.
            float residual = industrialSource != null
                ? PollutionService.SampleClimate01(industrialSource.ReleasePoint)
                : 0f;
            IndustrialThreatTier tier = PollutionThreat.ResolveTier(
                industrialSource != null, sourcePressure, residual);

            int wanted;
            int ambushBudget;
            bool raidLogistics;
            bool waveEvent = false;
            bool siegeEvent = false;
            if (tier == IndustrialThreatTier.Siege && Time.time >= _nextSiegeAt
                && slots >= 1 + PollutionThreat.SiegeEscortSize)
            {
                // Siege: one brute and its escort, all hunting the factory itself.
                siegeEvent = true;
                wanted = Mathf.Min(slots, 1 + PollutionThreat.SiegeEscortSize);
                ambushBudget = PollutionScentRules.AmbushCount(sourcePressure, wanted);
                raidLogistics = true;
            }
            else if (tier == IndustrialThreatTier.Wave && Time.time >= _nextWaveAt
                && slots >= PollutionThreat.MinWaveSlots)
            {
                // Organized wave: an elite spearhead, up to two flank ambushers
                // and every member on logistics targets. Grown by the residual.
                waveEvent = true;
                wanted = Mathf.Min(slots, PollutionThreat.WaveSize(residual));
                ambushBudget = Mathf.Min(wanted - 1, PollutionThreat.WaveAmbushers);
                raidLogistics = true;
            }
            else
            {
                wanted = industrialSource != null
                    ? Mathf.Min(slots, PollutionScentRules.PackSize(sourcePressure))
                    : 1;
                ambushBudget = industrialSource != null
                    ? PollutionScentRules.AmbushCount(sourcePressure, wanted)
                    : 0;
                raidLogistics = industrialSource != null && PollutionScentRules.RaidsLogistics(sourcePressure);
            }

            float baseAngle = Random.Range(0f, 360f);
            int spawned = 0;
            int ambushes = 0;
            for (int member = 0; member < wanted; member++)
            {
                // Tier leaders: the siege brute (member 0) and one elite escort;
                // a wave is led by a single elite. Leaders march openly from the
                // source anchor - only ordinary members take flank ambush points.
                GhoulVariant variant = GhoulVariant.Normal;
                if (siegeEvent)
                {
                    if (member == 0) variant = GhoulVariant.SiegeBrute;
                    else if (member == 1) variant = GhoulVariant.Elite;
                }
                else if (waveEvent && member == 0)
                {
                    variant = GhoulVariant.Elite;
                }

                if (variant == GhoulVariant.Normal
                    && ambushes < ambushBudget
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
                        ambush: false, raidLogistics: raidLogistics, variant: variant))
                    spawned++;
            }

            // Organized events keep their own cadence; a partially spawned wave
            // still spent itself, so the cooldown starts either way.
            if (spawned > 0)
            {
                if (siegeEvent) _nextSiegeAt = Time.time + PollutionThreat.SiegeCooldownSeconds;
                else if (waveEvent) _nextWaveAt = Time.time + PollutionThreat.WaveCooldownSeconds;

                if (industrialSource != null)
                {
                    if (siegeEvent || waveEvent)
                        ShowOrganizedThreatNotice(industrialSource, spawned, siegeEvent);
                    else
                        ShowIndustrialThreatNotice(industrialSource, spawned, ambushes);
                }
            }
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
            PollutionEmitter industrialSource, float sourcePressure, bool ambush, bool raidLogistics,
            GhoulVariant variant = GhoulVariant.Normal)
        {
            Vector3 north = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (north.sqrMagnitude < 0.001f) north = Vector3.ProjectOnPlane(Vector3.right, up);
            if (north.sqrMagnitude < 0.001f) return false;
            north.Normalize();
            Vector3 east = Vector3.Cross(up, north).normalized;
            float radians = angleDegrees * Mathf.Deg2Rad;
            Vector3 tangent = (north * Mathf.Cos(radians) + east * Mathf.Sin(radians)) * distance;
            if (tangent.sqrMagnitude < 0.001f) return false;
            return TrySpawnAt(anchor + tangent, up, -tangent, industrialSource, sourcePressure, ambush, raidLogistics, variant);
        }

        private bool TrySpawnAt(Vector3 desired, Vector3 up, Vector3 faceDirection,
            PollutionEmitter industrialSource, float sourcePressure, bool ambush, bool raidLogistics,
            GhoulVariant variant = GhoulVariant.Normal)
        {
            Vector3 face = Vector3.ProjectOnPlane(faceDirection, up);
            if (face.sqrMagnitude < 0.001f) return false;
            face.Normalize();

            // 17.27.0-dev - MOBS SPAWNING UNDER THE SURFACE.
            //
            // The old probe was a single 12 m ray from 4.5 m above the anchor, and it
            // trusted whatever it hit first: a cave ceiling, a hull, a placed block or a
            // ledge above the anchor all became "the ground". The mob was then placed
            // 1.2 m along the radial up from that point - which on a slope, a cliff face
            // or a seam between chunk meshes is a spot INSIDE the terrain, so the capsule
            // spawned embedded, the solver pushed it out the only way it could (down) and
            // the ghoul fell through the surface it was meant to be standing on. It also
            // stopped at the first hit, so a cave floor under a surface was a valid
            // answer whenever the surface itself was not within the ray's reach.
            //
            // The probe now: casts from well above the anchor and takes the SHALLOWEST
            // hit whose surface is walkable (up-ish), rejects anything that is not the
            // world's own terrain to within 20 cm, requires a spot the mob's whole body
            // fits in clear of geometry, and only then stands the prefab on it with its
            // own collider bottom 0.35 m clear of the ground.
            if (!CreatureSpawnGround.TryResolve(desired, up, out var surfacePoint, out var surfaceNormal)) return false;
            if (!CreatureSpawnGround.TryFindClearStandPoint(ghoulPrefab, surfacePoint, up, out var spawnPos)) return false;

            var go = Instantiate(ghoulPrefab, spawnPos, Quaternion.LookRotation(face, surfaceNormal));
            var ghoul = go.GetComponent<EnemyGhoul>();
            if (ghoul == null)
            {
                Debug.LogError("[EnemySpawner] Prefab has no EnemyGhoul component!");
                Destroy(go);
                return false;
            }

            // 17.18.0-dev: promote tiered ghouls right after birth. The Awake
            // announce carried the base stats, so push the scaled health and the
            // variant byte to guests immediately instead of at the next
            // five-second self-heal pass.
            if (variant != GhoulVariant.Normal)
            {
                ghoul.ApplyVariant(variant);
                VoxelEngine.Networking.HostileSync.ReannounceNow(ghoul);
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

        /// <summary>17.18.0-dev: waves and sieges announce themselves as events -
        /// their own cooldowns already rate-limit them, so the ordinary notice
        /// cooldown only shields the follow-up pack chatter.</summary>
        private void ShowOrganizedThreatNotice(PollutionEmitter source, int count, bool siege)
        {
            var player = VoxelEngine.Player.PlayerStats.Instance;
            if (source == null || count <= 0 || player == null) return;
            _nextThreatNotice = Time.time + Mathf.Max(5f, threatNoticeCooldown);
            string bearing = PollutionScentRules.FormatDirection(
                player.transform.position, source.ReleasePoint, "nearby");
            if (siege)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("SIEGE BRUTE",
                    source.DisplayName + " (" + bearing + ") has called a siege brute and "
                    + (count - 1) + " escorts. It is coming for the machines - stand in its "
                    + "way and it will bite through you too.",
                    null, new Color(0.85f, 0.22f, 0.14f));
            }
            else
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("ORGANIZED WAVE",
                    "The ecology around " + source.DisplayName + " (" + bearing
                    + ") has organized a wave of " + count + ", led by an elite ghoul.",
                    null, new Color(0.92f, 0.32f, 0.18f));
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
