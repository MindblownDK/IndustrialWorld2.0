// Assets/Scripts/VoxelEngine/Networking/AnimalSync.cs
//
// 14.54.0 - the herd goes over the wire. Until now every machine spawned its
// own private livestock: the host saw cows the guests could not, guests
// "killed" sheep that kept grazing on everyone else's screen, and no horse
// existed in the same place twice.
//
// Model - host-authoritative convergence, the same law as everything else:
//   - The HOST simulates every animal (wander, flee, husbandry, death) and
//     streams spawn / pose / health / removal. Spawns are re-announced every
//     few seconds, so late joiners catch the herd without join plumbing and
//     a lost packet self-heals.
//   - GUESTS never grow local fauna: locally-born animals (scatter or
//     spawner) self-destruct at Awake, and replicas - spawned here from the
//     same Resources/Livestock prefabs - are kinematic puppets that ease
//     toward the streamed pose. AI, physics and husbandry all stay off.
//   - Damage is an INTENT: a guest hitting a replica sends AnimalHit; the
//     host applies it to the real animal, which flees, bleeds and dies
//     authoritatively. Loot drops replicate through DropSync like any drop.
//   - RIDING transfers the reins: a mount announce suspends the host's AI
//     and every other machine glues that animal under the rider's replicated
//     avatar - the rider's own machine simulates the steering for real. The
//     dismount announce carries the final pose; the host snaps the animal
//     there and resumes AI. No second pose channel: the avatar IS the pose.
//
// Accepted gaps (documented): two riders mounting the same horse in the same
// instant race on the wire (last announce wins the glue); a guest's view of
// a fleeing animal eases rather than darts (pose interval 0.25 s).

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Fauna;

namespace VoxelEngine.Networking
{
    public static class AnimalSync
    {
        private const float PoseInterval = 0.25f;     // host pose stream cadence
        private const float ReannounceInterval = 5f;  // spawn self-heal / late join
        private const float ReplicaStaleSeconds = 20f;// no news that long = gone
        private const float MaxHitAmount = 200f;      // intent validation ceiling

        // ── host side ────────────────────────────────────────────────
        private static readonly Dictionary<int, PassiveAnimal> _hostAnimals = new();
        private static readonly Dictionary<PassiveAnimal, int> _idOf = new();
        private static readonly Dictionary<int, float> _lastSentHealth = new();
        private static readonly Dictionary<int, string> _remoteRidden = new();   // id -> guest rider
        private static int _nextId = 1;
        private static float _nextPoseAt;
        private static float _nextReannounceAt;

        // ── guest side ───────────────────────────────────────────────
        private sealed class Replica
        {
            public PassiveAnimal Animal;
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public float LastSeen;
            public string RiderId;        // non-null while someone rides it
            public bool LocallyRidden;    // OUR player holds the reins - full local sim
        }
        private static readonly Dictionary<int, Replica> _replicas = new();
        private static readonly Dictionary<PassiveAnimal, int> _replicaIdOf = new();

        /// <summary>True while a replica prefab is being instantiated, so
        /// PassiveAnimal.Awake keeps it instead of culling guest-born fauna.</summary>
        public static bool SpawningReplica { get; private set; }

        private static bool HostOnline =>
            NetworkSession.Mode == SessionMode.Host
            && NetworkBootstrap.Instance != null && !NetworkBootstrap.Instance.WorldMismatch;

        // ─────────────────── registration (host/offline) ───────────────────

        public static void Register(PassiveAnimal animal)
        {
            if (animal == null || _idOf.ContainsKey(animal)) return;
            int id = _nextId++;
            _hostAnimals[id] = animal;
            _idOf[animal] = id;
            if (HostOnline) AnnounceSpawn(id, animal);
        }

        public static void Unregister(PassiveAnimal animal)
        {
            if (animal == null) return;
            if (_idOf.TryGetValue(animal, out int id))
            {
                _idOf.Remove(animal);
                _hostAnimals.Remove(id);
                _lastSentHealth.Remove(id);
                _remoteRidden.Remove(id);
                if (HostOnline) NetworkBootstrap.Instance.SendAnimalRemoved(id, died: false);
            }
            if (_replicaIdOf.TryGetValue(animal, out int rid))
            {
                _replicaIdOf.Remove(animal);
                _replicas.Remove(rid);
            }
        }

        /// <summary>Host: the animal died (Damageable.Die). Announce before the
        /// object goes, then forget it so OnDestroy stays silent.</summary>
        public static void AnnounceDied(PassiveAnimal animal)
        {
            if (animal == null || !_idOf.TryGetValue(animal, out int id)) return;
            if (HostOnline) NetworkBootstrap.Instance.SendAnimalRemoved(id, died: true);
            _idOf.Remove(animal);
            _hostAnimals.Remove(id);
            _lastSentHealth.Remove(id);
            _remoteRidden.Remove(id);
        }

        // ─────────────────── the pump (one caller: the spawner) ───────────────────

        /// <summary>Called every frame by PassiveAnimalSpawner. Hosts stream
        /// state on their intervals; guests ease replicas and cull stale ones.</summary>
        public static void Pump()
        {
            if (NetworkSession.Mode == SessionMode.Client) { PumpReplicas(); return; }
            if (!HostOnline) return;

            // Remote-ridden animals glue under their rider's avatar every
            // frame - the avatar is the authoritative pose while mounted.
            foreach (var pair in _remoteRidden)
            {
                if (!_hostAnimals.TryGetValue(pair.Key, out var animal) || animal == null) continue;
                GlueToRider(animal, pair.Value);
            }

            float now = Time.time;
            if (now >= _nextPoseAt)
            {
                _nextPoseAt = now + PoseInterval;
                foreach (var pair in _hostAnimals)
                {
                    var animal = pair.Value;
                    if (animal == null) continue;
                    bool ridden = _remoteRidden.ContainsKey(pair.Key)
                        || (animal is RideableAnimal r && r.Rider != null);
                    if (!ridden)
                        NetworkBootstrap.Instance.SendAnimalPose(pair.Key,
                            animal.transform.position, animal.transform.rotation);

                    // Health piggybacks on the pose tick, only when it moved.
                    if (!_lastSentHealth.TryGetValue(pair.Key, out float sent)
                        || Mathf.Abs(sent - animal.Health) > 0.01f)
                    {
                        _lastSentHealth[pair.Key] = animal.Health;
                        NetworkBootstrap.Instance.SendAnimalHealth(pair.Key, animal.Health);
                    }
                }
            }

            if (now >= _nextReannounceAt)
            {
                _nextReannounceAt = now + ReannounceInterval;
                foreach (var pair in _hostAnimals)
                    if (pair.Value != null) AnnounceSpawn(pair.Key, pair.Value);
            }
        }

        private static void AnnounceSpawn(int id, PassiveAnimal animal)
        {
            NetworkBootstrap.Instance.SendAnimalSpawn(id, (byte)animal.species,
                animal is RideableAnimal, animal.transform.position,
                animal.transform.rotation, animal.Health);
        }

        private static void PumpReplicas()
        {
            float now = Time.time;
            float dt = Time.deltaTime;
            float ease = 1f - Mathf.Exp(-8f * dt);
            List<int> stale = null;

            foreach (var pair in _replicas)
            {
                var rep = pair.Value;
                if (rep.Animal == null) { (stale ??= new List<int>()).Add(pair.Key); continue; }
                if (rep.LocallyRidden) continue;   // our hands on the reins - full local sim

                var t = rep.Animal.transform;
                if (!string.IsNullOrEmpty(rep.RiderId))
                {
                    GlueToRider(rep.Animal, rep.RiderId);   // someone rides it - avatar wins
                    rep.LastSeen = now;                     // a ridden animal is never stale
                    continue;
                }
                if (now - rep.LastSeen > ReplicaStaleSeconds)
                {
                    Object.Destroy(rep.Animal.gameObject);
                    (stale ??= new List<int>()).Add(pair.Key);
                    continue;
                }
                // Ease toward the stream; a big jump (teleport, correction) snaps.
                if ((rep.TargetPos - t.position).sqrMagnitude > 64f)
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
                    if (_replicas.TryGetValue(id, out var rep) && rep.Animal != null)
                        _replicaIdOf.Remove(rep.Animal);
                    _replicas.Remove(id);
                }
        }

        private static void GlueToRider(PassiveAnimal animal, string riderId)
        {
            var avatar = PlayerAvatar.Find(riderId);
            if (avatar == null) return;
            Vector3 seat = animal is RideableAnimal r ? r.seatLocalPos : new Vector3(0f, 1.4f, 0f);
            var at = avatar.transform;
            animal.transform.SetPositionAndRotation(at.position - at.rotation * seat, at.rotation);
        }

        // ─────────────────── guest -> host intents ───────────────────

        /// <summary>A guest hit a replica: file the intent, change nothing local.
        /// The host's flee, health and death stream back as the outcome.</summary>
        public static void AnnounceHit(PassiveAnimal animal, VoxelEngine.Combat.DamageEvent e)
        {
            if (animal == null || NetworkBootstrap.Instance == null) return;
            if (!_replicaIdOf.TryGetValue(animal, out int id)) return;
            NetworkBootstrap.Instance.SendAnimalHit(id,
                Mathf.Clamp(e.amount, 0f, MaxHitAmount), e.point, e.direction);
        }

        /// <summary>Any rider machine, mounting or dismounting - announce it.</summary>
        public static void AnnounceMount(PassiveAnimal animal, bool mounted)
        {
            if (animal == null || NetworkSession.Mode == SessionMode.Offline
                || NetworkBootstrap.Instance == null) return;

            int id;
            if (_idOf.TryGetValue(animal, out id)) { /* host's own animal */ }
            else if (_replicaIdOf.TryGetValue(animal, out id))
            {
                // Guest rider: flip the replica between puppet and live sim.
                if (_replicas.TryGetValue(id, out var rep))
                {
                    rep.LocallyRidden = mounted;
                    rep.RiderId = mounted ? PlayerIdentity.LocalId : null;
                    rep.LastSeen = Time.time;
                    rep.TargetPos = animal.transform.position;
                    rep.TargetRot = animal.transform.rotation;
                }
                var rb = animal.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = !mounted;
                animal.enabled = mounted;   // enabled = MountedFixedUpdate runs; off = puppet again
            }
            else return;

            NetworkBootstrap.Instance.SendAnimalMount(id, PlayerIdentity.LocalId, mounted,
                animal.transform.position, animal.transform.rotation);
        }

        // ─────────────────── remote applies ───────────────────

        public static void ApplySpawned(int id, byte species, bool rideable,
            Vector3 pos, Quaternion rot, float health)
        {
            if (NetworkSession.Mode != SessionMode.Client) return;
            if (_replicas.TryGetValue(id, out var existing))
            {
                existing.TargetPos = pos;
                existing.TargetRot = rot;
                existing.LastSeen = Time.time;
                if (existing.Animal != null) existing.Animal.NetworkSetHealth(health);
                return;   // re-announce of a known animal = pose/health correction
            }

            var prefab = FindPrefab(species, rideable);
            if (prefab == null) return;   // no matching art on this build - skip, stay silent

            GameObject go;
            SpawningReplica = true;
            try { go = Object.Instantiate(prefab, pos, rot); }
            finally { SpawningReplica = false; }

            var animal = go.GetComponent<PassiveAnimal>();
            if (animal == null) { Object.Destroy(go); return; }
            animal.MakeReplica();
            animal.NetworkSetHealth(health);

            var rep = new Replica
            {
                Animal = animal, TargetPos = pos, TargetRot = rot, LastSeen = Time.time
            };
            _replicas[id] = rep;
            _replicaIdOf[animal] = id;
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
            if (!_replicas.TryGetValue(id, out var rep) || rep.Animal == null) return;
            rep.Animal.NetworkSetHealth(health);
        }

        public static void ApplyRemoved(int id, bool died)
        {
            if (!_replicas.TryGetValue(id, out var rep)) return;
            if (rep.Animal != null)
            {
                _replicaIdOf.Remove(rep.Animal);
                Object.Destroy(rep.Animal.gameObject);   // death loot arrives via DropSync
            }
            _replicas.Remove(id);
        }

        /// <summary>Host: a guest's weapon connected. Apply to the real animal -
        /// it flees, bleeds and maybe dies, and all of that streams back out.</summary>
        public static void HostApplyHit(int id, float amount, Vector3 point, Vector3 direction)
        {
            if (!_hostAnimals.TryGetValue(id, out var animal) || animal == null) return;
            animal.TakeDamage(new VoxelEngine.Combat.DamageEvent
            {
                amount = Mathf.Clamp(amount, 0f, MaxHitAmount),
                type = VoxelEngine.Combat.DamageType.Melee,
                point = point,
                direction = direction
            });
        }

        /// <summary>Host: a guest mounted/dismounted the real animal - suspend or
        /// resume its AI. While suspended the pump glues it under the rider.</summary>
        public static void HostApplyMount(int id, string riderId, bool mounted,
            Vector3 pos, Quaternion rot)
        {
            if (!_hostAnimals.TryGetValue(id, out var animal) || animal == null) return;
            var rb = animal.GetComponent<Rigidbody>();
            if (mounted)
            {
                _remoteRidden[id] = riderId;
                animal.enabled = false;
                if (rb != null) rb.isKinematic = true;
            }
            else
            {
                _remoteRidden.Remove(id);
                animal.transform.SetPositionAndRotation(pos, rot);
                animal.enabled = true;
                if (rb != null) rb.isKinematic = false;
            }
        }

        /// <summary>Guest: some OTHER machine's rider mounted/dismounted - glue or
        /// release the local replica. Our own announce never comes back (relay
        /// excludes the sender), so no self-handling is needed here.</summary>
        public static void ApplyMountRemote(int id, string riderId, bool mounted,
            Vector3 pos, Quaternion rot)
        {
            if (!_replicas.TryGetValue(id, out var rep)) return;
            rep.RiderId = mounted ? riderId : null;
            rep.LastSeen = Time.time;
            if (!mounted)
            {
                rep.TargetPos = pos;
                rep.TargetRot = rot;
            }
        }

        /// <summary>Session teardown: forget everything. Scene objects die with
        /// the scene; the registries must not outlive them.</summary>
        public static void ResetSession()
        {
            _hostAnimals.Clear();
            _idOf.Clear();
            _lastSentHealth.Clear();
            _remoteRidden.Clear();
            _replicas.Clear();
            _replicaIdOf.Clear();
            _nextId = 1;
        }

        // ─────────────────── prefab lookup ───────────────────

        private static GameObject[] _prefabs;

        private static GameObject FindPrefab(byte species, bool rideable)
        {
            if (_prefabs == null || _prefabs.Length == 0)
                _prefabs = Resources.LoadAll<GameObject>("Livestock");
            if (_prefabs == null) return null;

            GameObject speciesMatch = null;
            foreach (var p in _prefabs)
            {
                if (p == null) continue;
                var a = p.GetComponent<PassiveAnimal>();
                if (a == null || (byte)a.species != species) continue;
                bool pr = a is RideableAnimal;
                if (pr == rideable) return p;        // exact: species AND saddle state
                if (speciesMatch == null) speciesMatch = p;
            }
            return speciesMatch;
        }
    }
}
