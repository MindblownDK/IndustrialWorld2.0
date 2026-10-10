// Assets/Scripts/VoxelEngine/Networking/HostileSync.cs
//
// 14.55.0 - the monsters go over the wire, completing what 14.54.0 started
// for livestock. Until now every machine bred its own private nightmares:
// ghouls only the host could see, a Roc a guest "killed" that kept circling
// on everyone else's screen, and enemies that only ever hunted the local
// player because guest avatars carry no PlayerStats.
//
// Model - host-authoritative, the same law as AnimalSync:
//   - The HOST simulates every hostile (AI, casts, death) and streams
//     spawn / pose / health / removal. Spawns re-announce every few seconds
//     (idempotent late-join snapshot), stale replicas are culled.
//   - GUESTS never breed hostiles: locally-born enemies (spawner or biome
//     scatter) self-destruct at Awake; replicas built from the same
//     Resources/Enemies prefabs are kinematic puppets - AI component off,
//     eased to the streamed pose, colliders kept so weapons can aim.
//   - Damage TO a hostile is an intent: a guest's hit on a replica files
//     EnemyHit (with the hitter's id so Karkadann's frontal armor can judge
//     the angle); the host applies the real TakeDamage. Loot rides DropSync.
//   - Damage FROM a hostile is victim-side, like fall and collision damage:
//     host AI picks the NEAREST player (local player or any avatar) and
//     strikes through one funnel - a local victim takes it directly, a
//     remote victim gets an EnemyStrike that its own machine applies
//     (damage + poison/burn/petrify + death cause + hit feedback).
//   - CASTS replicate as visuals: fireball volleys, spike volleys, fire
//     walls and wing gusts re-play on guests from the replica's own prefab
//     fields (materials, counts, radii), flagged visual-only so they never
//     double-apply damage.
//
// Accepted gaps (documented): remote victims take gust damage without the
// gust knockback; replayed volleys roll their own spread (cosmetic); an
// enemy may briefly stalk the avatar of a player who just died.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Combat;

namespace VoxelEngine.Networking
{
    public static class HostileSync
    {
        private const float PoseInterval = 0.25f;     // host pose stream cadence
        private const float ReannounceInterval = 5f;  // spawn self-heal / late join
        private const float ReplicaStaleSeconds = 20f;// no news that long = gone
        private const float MaxHitAmount = 200f;      // intent validation ceiling

        // Effect bytes carried by EnemyStrike.
        public const byte EffectNone    = 0;
        public const byte EffectPoison  = 1;   // A = dps, B = duration
        public const byte EffectBurn    = 2;   // A = dps, B = duration
        public const byte EffectPetrify = 3;   // A = slow fraction, B = duration

        // Cast bytes carried by EnemyCast.
        public const byte CastFireballs = 0;
        public const byte CastSpikes    = 1;
        public const byte CastFirewall  = 2;
        public const byte CastGust      = 3;

        // ── host side ────────────────────────────────────────────────
        private static readonly Dictionary<int, Damageable> _hostEnemies = new();
        private static readonly Dictionary<Damageable, int> _idOf = new();
        private static readonly Dictionary<int, float> _lastSentHealth = new();
        private static int _nextId = 1;
        private static float _nextPoseAt;
        private static float _nextReannounceAt;

        // ── guest side ───────────────────────────────────────────────
        private sealed class Replica
        {
            public Damageable Enemy;
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public float LastSeen;
        }
        private static readonly Dictionary<int, Replica> _replicas = new();
        private static readonly Dictionary<Damageable, int> _replicaIdOf = new();

        /// <summary>True while a replica prefab is being instantiated, so the
        /// enemy's Awake keeps it instead of culling guest-born hostiles.</summary>
        public static bool SpawningReplica { get; private set; }

        private static bool HostOnline =>
            NetworkSession.Mode == SessionMode.Host
            && NetworkBootstrap.Instance != null && !NetworkBootstrap.Instance.WorldMismatch;

        // ─────────────────── enemy kind table ───────────────────

        private static int KindOf(Damageable enemy) => enemy switch
        {
            EnemyGhoul _     => 0,
            EnemyBasilisk _  => 1,
            EnemyGriffin _   => 2,
            EnemyIfrit _     => 3,
            EnemyKarkadann _ => 4,
            EnemyManticore _ => 5,
            EnemyRoc _       => 6,
            _ => -1,
        };

        private static readonly string[] _prefabPaths =
        {
            "Enemies/Ghoul", "Enemies/Basilisk", "Enemies/Griffin", "Enemies/Ifrit",
            "Enemies/Karkadann", "Enemies/Manticore", "Enemies/Roc",
        };

        private static readonly GameObject[] _prefabCache = new GameObject[7];

        // ─────────────────── lifecycle (called from each enemy's Awake) ───────────────────

        /// <summary>One call at the end of every hostile Awake. Returns false
        /// when the enemy was culled (guest-born, no business existing) - the
        /// caller must return immediately. Replicas and host/offline enemies
        /// return true; host enemies are registered for streaming here.</summary>
        public static bool OnHostileAwake(Damageable enemy)
        {
            if (enemy == null || KindOf(enemy) < 0) return true;
            if (SpawningReplica) return true;   // configured by MakeReplica right after
            if (NetworkSession.Mode == SessionMode.Client)
            {
                Object.Destroy(enemy.gameObject);
                return false;
            }
            if (_idOf.ContainsKey(enemy)) return true;
            int id = _nextId++;
            _hostEnemies[id] = enemy;
            _idOf[enemy] = id;
            if (HostOnline) AnnounceSpawn(id, enemy);
            return true;
        }

        /// <summary>Damageable.OnDestroy hook: host-side despawn (cull, scene
        /// change) announces a silent removal; replicas just unhook.</summary>
        public static void NotifyDestroyed(Damageable enemy)
        {
            if (enemy == null) return;
            HostileSleepManager.Forget(enemy);
            if (_idOf.TryGetValue(enemy, out int id))
            {
                _idOf.Remove(enemy);
                _hostEnemies.Remove(id);
                _lastSentHealth.Remove(id);
                if (HostOnline) NetworkBootstrap.Instance.SendEnemyRemoved(id, died: false);
            }
            if (_replicaIdOf.TryGetValue(enemy, out int rid))
            {
                _replicaIdOf.Remove(enemy);
                _replicas.Remove(rid);
            }
        }

        /// <summary>Damageable.Die hook: announce the death BEFORE the loot
        /// roll so removal and DropSync spawns leave in order, then forget the
        /// enemy so OnDestroy stays silent. No-op for anything unregistered.</summary>
        public static void NotifyDied(Damageable enemy)
        {
            if (enemy == null) return;
            HostileSleepManager.Forget(enemy);
            if (!_idOf.TryGetValue(enemy, out int id)) return;
            if (HostOnline) NetworkBootstrap.Instance.SendEnemyRemoved(id, died: true);
            _idOf.Remove(enemy);
            _hostEnemies.Remove(id);
            _lastSentHealth.Remove(id);
        }

        /// <summary>Damageable.TakeDamage hook: a replica never bleeds locally.
        /// Returns true (swallow the hit) after filing the intent - the host's
        /// health stream and removal broadcast are the only outcome.</summary>
        public static bool InterceptReplicaDamage(Damageable enemy, DamageEvent e)
        {
            if (enemy == null || !_replicaIdOf.TryGetValue(enemy, out int id)) return false;
            if (NetworkBootstrap.Instance != null)
                NetworkBootstrap.Instance.SendEnemyHit(id, Mathf.Clamp(e.amount, 0f, MaxHitAmount),
                    e.point, e.direction, PlayerIdentity.LocalId);
            return true;
        }

        // ─────────────────── targeting (host AI) ───────────────────

        /// <summary>The nearest player this hostile can hunt: the local player
        /// or any replicated avatar. Re-evaluated every call so enemies switch
        /// to whoever is closest and recover when a target disconnects.</summary>
        public static void AcquireTarget(Vector3 pos, ref Transform target)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            var ps = VoxelEngine.Player.PlayerStats.Instance;
            if (ps != null)
            {
                best = ps.transform;
                bestD = (ps.transform.position - pos).sqrMagnitude;
            }
            foreach (var av in PlayerAvatar.All)
            {
                if (av == null) continue;
                float d = (av.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = av.transform; }
            }
            target = best;
        }

        // ─────────────────── strikes (one funnel for all hostile damage) ───────────────────

        /// <summary>Hostile damage lands victim-side, like fall and collision
        /// damage: a local victim takes it directly, a remote victim's machine
        /// gets an EnemyStrike and applies it to its own PlayerStats.</summary>
        public static void StrikePlayer(Transform target, float amount, string source,
            byte effect = EffectNone, float effectA = 0f, float effectB = 0f)
        {
            if (target == null) return;
            var ps = target.GetComponent<VoxelEngine.Player.PlayerStats>();
            if (ps != null)
            {
                ApplyStrikeLocal(amount, source, effect, effectA, effectB);
                return;
            }
            var avatar = target.GetComponentInParent<PlayerAvatar>();
            if (avatar != null) StrikeAvatar(avatar, amount, source, effect, effectA, effectB);
        }

        /// <summary>Route a strike to a remote player's machine.</summary>
        public static void StrikeAvatar(PlayerAvatar avatar, float amount, string source,
            byte effect = EffectNone, float effectA = 0f, float effectB = 0f)
        {
            if (avatar == null || NetworkBootstrap.Instance == null) return;
            string victim = avatar.PlayerId;
            if (string.IsNullOrEmpty(victim)) return;
            NetworkBootstrap.Instance.SendEnemyStrike(victim, amount, source, effect, effectA, effectB);
        }

        /// <summary>Victim-side application: damage, effect, death cause and
        /// the hit feedback all land on the machine that owns the health bar.</summary>
        public static void ApplyStrikeLocal(float amount, string source,
            byte effect, float effectA, float effectB)
        {
            var ps = VoxelEngine.Player.PlayerStats.Instance;
            if (ps == null) return;
            if (amount > 0f)
            {
                VoxelEngine.Player.PlayerStats.SetDeathCause(
                    "SLAIN BY " + (string.IsNullOrEmpty(source) ? "A CREATURE" : "A " + source.ToUpperInvariant()));
                ps.TakeDamage(Mathf.Clamp(amount, 0f, MaxHitAmount));
            }
            switch (effect)
            {
                case EffectPoison: ps.ApplyPoison(effectA, effectB); break;
                case EffectBurn:   ps.ApplyBurn(effectA, effectB); break;
                case EffectPetrify:
                    var pc = ps.GetComponent<VoxelEngine.Player.PlayerController>();
                    if (pc != null) pc.ApplyPetrify(effectA, effectB);
                    break;
            }
            // Toast on real hits and on petrify; silent for recurring
            // effect-only ticks (fire-wall burns) - they'd read as spam.
            if (string.IsNullOrEmpty(source)) return;
            if (effect == EffectPetrify && amount <= 0f)
                VoxelEngine.UI.BuildFeedbackHud.Show(source,
                    "Petrifying gaze - you feel like stone!", null, new Color(0.6f, 0.8f, 0.4f));
            else if (amount > 0f)
                VoxelEngine.UI.BuildFeedbackHud.Show(source,
                    "Attacks you!", null, new Color(0.9f, 0.25f, 0.2f));
        }

        // ─────────────────── casts (visual replication) ───────────────────

        /// <summary>Host: a hostile cast something with a visible body
        /// (fireballs, spikes, fire wall, wing gust) - replay it on guests.</summary>
        public static void AnnounceCast(Damageable caster, byte kind, Vector3 from, Vector3 dir)
        {
            if (!HostOnline || caster == null) return;
            if (!_idOf.TryGetValue(caster, out int id)) return;
            NetworkBootstrap.Instance.SendEnemyCast(id, kind, from, dir);
        }

        /// <summary>Guest: replay a cast using the replica's own prefab fields,
        /// flagged visual-only so nothing double-applies damage.</summary>
        public static void ApplyCast(int id, byte kind, Vector3 from, Vector3 dir)
        {
            if (!_replicas.TryGetValue(id, out var rep) || rep.Enemy == null) return;
            switch (kind)
            {
                case CastFireballs when rep.Enemy is EnemyIfrit ifrit:
                    for (int i = 0; i < ifrit.fireballsPerCast; i++)
                    {
                        Vector3 spread = new Vector3(
                            Random.Range(-ifrit.fireballSpread, ifrit.fireballSpread),
                            Random.Range(-ifrit.fireballSpread * 0.5f, ifrit.fireballSpread * 0.5f), 1f).normalized;
                        var fb = Fireball.Spawn(from, dir + spread, ifrit.gameObject, ifrit.fireballMaterial,
                            ifrit.fireballDamage, ifrit.fireballBurnDps, ifrit.fireballBurnDuration);
                        fb.visualOnly = true;
                    }
                    break;

                case CastSpikes when rep.Enemy is EnemyManticore manti:
                    Quaternion aim = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward);
                    for (int i = 0; i < manti.spikesPerVolley; i++)
                    {
                        Vector3 spread = new Vector3(
                            Random.Range(-manti.spikeSpread, manti.spikeSpread),
                            Random.Range(-manti.spikeSpread * 0.5f, manti.spikeSpread * 0.5f), 1f).normalized;
                        var sp = ManticoreSpike.Spawn(from, aim * spread, manti.gameObject, manti.spikeMaterial,
                            manti.spikeDamage, manti.spikePoisonDps, manti.spikePoisonDuration);
                        sp.visualOnly = true;
                    }
                    break;

                case CastFirewall when rep.Enemy is EnemyIfrit caster2:
                    FireWallHazard.Spawn(from, dir, caster2.firewallMaterial,
                        caster2.firewallDuration, caster2.firewallBurnDps, caster2.firewallRadius,
                        visualOnly: true);
                    break;

                case CastGust when rep.Enemy is EnemyRoc roc:
                    // The dust ring, exactly as the Roc draws it for the host.
                    var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    ring.name = "RocGust";
                    ring.transform.position = from + dir * 0.1f;
                    ring.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                    ring.transform.localScale = new Vector3(roc.gustRadius, 0.18f, roc.gustRadius);
                    var col = ring.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
                    var ren = ring.GetComponent<Renderer>();
                    if (roc.dustMaterial != null) ren.sharedMaterial = roc.dustMaterial;
                    Object.Destroy(ring, 0.45f);
                    break;
            }
        }

        // ─────────────────── the pump (driver: EnemySpawner.Update) ───────────────────

        public static void Pump()
        {
            if (NetworkSession.Mode == SessionMode.Client) { PumpReplicas(); return; }
            HostileSleepManager.Pump(_hostEnemies.Values);
            if (!HostOnline) return;

            float now = Time.time;
            if (now >= _nextPoseAt)
            {
                _nextPoseAt = now + PoseInterval;
                foreach (var pair in _hostEnemies)
                {
                    var enemy = pair.Value;
                    if (enemy == null) continue;
                    // A sleeping hostile is stationary. Keep health and the slower
                    // re-announce snapshot flowing, but avoid sending redundant pose
                    // packets at the active-AI cadence until it wakes.
                    if (!HostileSleepManager.IsSleeping(enemy))
                        NetworkBootstrap.Instance.SendEnemyPose(pair.Key,
                            enemy.transform.position, enemy.transform.rotation);

                    if (!_lastSentHealth.TryGetValue(pair.Key, out float sent)
                        || Mathf.Abs(sent - enemy.Health) > 0.01f)
                    {
                        _lastSentHealth[pair.Key] = enemy.Health;
                        NetworkBootstrap.Instance.SendEnemyHealth(pair.Key, enemy.Health);
                    }
                }
            }

            if (now >= _nextReannounceAt)
            {
                _nextReannounceAt = now + ReannounceInterval;
                foreach (var pair in _hostEnemies)
                    if (pair.Value != null) AnnounceSpawn(pair.Key, pair.Value);
            }
        }

        private static void AnnounceSpawn(int id, Damageable enemy)
        {
            NetworkBootstrap.Instance.SendEnemySpawn(id, (byte)KindOf(enemy),
                enemy.transform.position, enemy.transform.rotation, enemy.Health, enemy.maxHealth);
        }

        private static void PumpReplicas()
        {
            float now = Time.time;
            float ease = 1f - Mathf.Exp(-8f * Time.deltaTime);
            List<int> stale = null;

            foreach (var pair in _replicas)
            {
                var rep = pair.Value;
                if (rep.Enemy == null) { (stale ??= new List<int>()).Add(pair.Key); continue; }
                if (now - rep.LastSeen > ReplicaStaleSeconds)
                {
                    Object.Destroy(rep.Enemy.gameObject);
                    (stale ??= new List<int>()).Add(pair.Key);
                    continue;
                }
                var t = rep.Enemy.transform;
                if ((rep.TargetPos - t.position).sqrMagnitude > 100f)   // flyers dive fast - generous snap
                    t.SetPositionAndRotation(rep.TargetPos, rep.TargetRot);
                else
                {
                    t.position = Vector3.Lerp(t.position, rep.TargetPos, ease);
                    t.rotation = Quaternion.Slerp(t.rotation, rep.TargetRot, ease);
                }
            }
            if (stale != null)
                foreach (int id in stale)
                {
                    if (_replicas.TryGetValue(id, out var rep) && rep.Enemy != null)
                        _replicaIdOf.Remove(rep.Enemy);
                    _replicas.Remove(id);
                }
        }

        // ─────────────────── remote applies ───────────────────

        public static void ApplySpawned(int id, byte kind, Vector3 pos, Quaternion rot,
            float health, float maxHealth)
        {
            if (NetworkSession.Mode != SessionMode.Client) return;
            if (_replicas.TryGetValue(id, out var existing))
            {
                existing.TargetPos = pos;
                existing.TargetRot = rot;
                existing.LastSeen = Time.time;
                if (existing.Enemy != null) existing.Enemy.SetReplicatedHealth(health);
                return;   // re-announce of a known enemy = pose/health correction
            }

            if (kind >= _prefabPaths.Length) return;
            if (_prefabCache[kind] == null)
                _prefabCache[kind] = Resources.Load<GameObject>(_prefabPaths[kind]);
            var prefab = _prefabCache[kind];
            if (prefab == null) return;   // no matching art on this build - skip, stay silent

            GameObject go;
            SpawningReplica = true;
            try { go = Object.Instantiate(prefab, pos, rot); }
            finally { SpawningReplica = false; }

            var enemy = go.GetComponent<Damageable>();
            if (enemy == null || KindOf(enemy) < 0) { Object.Destroy(go); return; }
            enemy.maxHealth = maxHealth;
            enemy.SetReplicatedHealth(health);
            enemy.enabled = false;   // AI off - HostileSync moves the transform
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            _replicas[id] = new Replica
            {
                Enemy = enemy, TargetPos = pos, TargetRot = rot, LastSeen = Time.time
            };
            _replicaIdOf[enemy] = id;
        }

        public static void ApplyPose(int id, Vector3 pos, Quaternion rot)
        {
            if (!_replicas.TryGetValue(id, out var rep)) return;
            rep.TargetPos = pos;
            rep.TargetRot = rot;
            rep.LastSeen = Time.time;
        }

        public static void ApplyHealth(int id, float health)
        {
            if (!_replicas.TryGetValue(id, out var rep) || rep.Enemy == null) return;
            rep.Enemy.SetReplicatedHealth(health);
        }

        public static void ApplyRemoved(int id, bool died)
        {
            if (!_replicas.TryGetValue(id, out var rep)) return;
            if (rep.Enemy != null)
            {
                _replicaIdOf.Remove(rep.Enemy);
                Object.Destroy(rep.Enemy.gameObject);   // death loot arrives via DropSync
            }
            _replicas.Remove(id);
        }

        /// <summary>Host: a guest's weapon connected. Apply to the real enemy
        /// with the hitter's avatar as the source, so angle-sensitive defenses
        /// (Karkadann's frontal armor) judge the right direction.</summary>
        public static void HostApplyHit(int id, float amount, Vector3 point,
            Vector3 direction, string hitterId)
        {
            if (!_hostEnemies.TryGetValue(id, out var enemy) || enemy == null) return;
            var hitter = PlayerAvatar.Find(hitterId);
            enemy.TakeDamage(new DamageEvent
            {
                amount = Mathf.Clamp(amount, 0f, MaxHitAmount),
                type = DamageType.Melee,
                point = point,
                direction = direction,
                source = hitter != null ? hitter.gameObject : null,
            });
        }

        /// <summary>Session teardown: forget everything. Scene objects die with
        /// the scene; the registries must not outlive them.</summary>
        public static void ResetSession()
        {
            _hostEnemies.Clear();
            _idOf.Clear();
            _lastSentHealth.Clear();
            _replicas.Clear();
            _replicaIdOf.Clear();
            _nextId = 1;
        }
    }
}
