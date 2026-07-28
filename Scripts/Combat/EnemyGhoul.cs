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
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;       // custom radial gravity
            _rb.freezeRotation = true;    // we align manually
            _home = transform.position;
            PickWander();
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
                if (distP <= attackRange && Time.time >= _nextAttackAt)
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
            if (_player != null) return;
            var ps = VoxelEngine.Player.PlayerStats.Instance;
            if (ps != null) { _player = ps.transform; return; }
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) _player = go.transform;
        }

        private void AttackPlayer()
        {
            var ps = (_player != null) ? _player.GetComponent<VoxelEngine.Player.PlayerStats>() : null;
            if (ps != null) ps.TakeDamage(attackDamage);
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Ghoul", "Attacked!", null, new Color(0.9f, 0.2f, 0.2f));
        }
    }
}
