// Assets/Scripts/VoxelEngine/Combat/EnemyGhoul.cs
//
// First hostile enemy. A shambling ghoul that wanders, detects the player, chases
// across the spherical surface (radial gravity + upright alignment), and melees on
// contact. Uses the shared Damageable health/loot contract so player weapons kill it.

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyGhoul : Damageable
    {
        [Header("Ghoul AI")]
        public float detectRange   = 16f;
        public float attackRange   = 2.0f;
        public float wanderSpeed   = 1.6f;
        public float chaseSpeed    = 4.2f;
        public float accel         = 12f;
        public float attackDamage  = 9f;
        public float attackCooldown = 1.1f;
        public float wanderRadius  = 6f;
        public float wanderPause   = 2.5f;

        private Rigidbody _rb;
        private Transform _player;
        private Vector3 _home;
        private Vector3 _wanderTarget;
        private float _nextWanderAt;
        private float _nextAttackAt;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 35f);
            base.Awake();

            // CRITICAL: detach from any parent (the biome-scatter system instantiates us
            // under chunk/__scatter, which moves with the rotating planet — but Rigidbody
            // physics is world-space, so a chunk-parented body gets flung off the sphere).
            // Root-level = correct physics on spherical worlds.
            if (transform.parent != null)
                transform.SetParent(null, true);

            _home = transform.position;
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.freezeRotation = true;
            PickWander();

            // 14.55.0 multiplayer: hostiles exist once, on the host. Guests
            // cull locally-born ones here and keep only streamed replicas.
            if (!VoxelEngine.Networking.HostileSync.OnHostileAwake(this)) return;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            Vector3 up   = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(pos);
            EnsurePlayer();

            // Detect / chase / wander — all in the local tangent plane (perpendicular to radial up).
            Vector3 flatToPlayer = (_player != null) ? Vector3.ProjectOnPlane(_player.position - pos, up) : Vector3.zero;
            float distP = flatToPlayer.magnitude;
            bool chasing = _player != null && distP <= detectRange;

            Vector3 moveDir;
            float spd;
            if (chasing)
            {
                moveDir = flatToPlayer.sqrMagnitude > 0.0001f ? flatToPlayer.normalized
                                                              : Vector3.ProjectOnPlane(transform.forward, up).normalized;
                spd = distP > attackRange ? chaseSpeed : 0f;
                // 14.60.0 - the bite needs TRUE range, not the tangent-plane
                // projection: distP ignores height, so a player hovering far
                // above read as "in reach" and was mauled from the sky.
                float trueDist = Vector3.Distance(_player.position, pos);
                if (distP <= attackRange && trueDist <= attackRange + 0.8f
                    && Time.time >= _nextAttackAt)
                {
                    _nextAttackAt = Time.time + attackCooldown;
                    AttackPlayer();
                }
            }
            else
            {
                if (Time.time >= _nextWanderAt) PickWander();
                Vector3 flatW = Vector3.ProjectOnPlane(_wanderTarget - pos, up);
                if (flatW.magnitude < 0.6f)
                {
                    moveDir = Vector3.ProjectOnPlane(transform.forward, up).normalized;
                    spd = 0f;
                    _nextWanderAt = Time.time + wanderPause;
                }
                else
                {
                    moveDir = flatW.normalized;
                    spd = wanderSpeed;
                }
            }

            // Velocity: accelerate the tangent component toward the target, integrate radial gravity.
            Vector3 v = _rb.linearVelocity;
            Vector3 radial = Vector3.Project(v, up);
            Vector3 tangent = v - radial;
            tangent = Vector3.MoveTowards(tangent, moveDir * spd, accel * dt);
            radial += grav * dt;
            _rb.linearVelocity = tangent + radial;

            // Stand on the surface + face travel direction.
            Vector3 face = (spd > 0.01f && moveDir != Vector3.zero) ? moveDir : Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (face.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(face, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 0.15f));
            }
        }

        private void PickWander()
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * wanderRadius;
            _wanderTarget = _home + new Vector3(r.x, 0f, r.y);
            _nextWanderAt = Time.time + wanderPause;
        }

        private void EnsurePlayer()
        {
            // 14.55.0 - hunt the NEAREST player: the local one or any remote
            // avatar. Re-evaluated every call, so the target can switch and
            // a disconnected victim is forgotten.
            VoxelEngine.Networking.HostileSync.AcquireTarget(transform.position, ref _player);
        }

        private void AttackPlayer()
        {
            // 14.55.0 - one funnel for hostile damage: local victims take it
            // directly, remote victims get the strike on their own machine.
            VoxelEngine.Networking.HostileSync.StrikePlayer(_player, attackDamage, "Ghoul");
        }
    }
}
