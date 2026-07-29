// Assets/Scripts/VoxelEngine/Combat/EnemyManticore.cs
//
// A mythical predator (Combat Phase 3e): a lion-bodied beast with a humanoid face and
// a venomous scorpion tail. Wanders, detects the player, chases, fires volleys of toxic
// tail spikes at range, and claws in melee. Spikes apply an armor-bypassing poison DoT.
// Radial-gravity aligned (spherical worlds) and detaches from the chunk-scatter parent
// on Awake so Rigidbody physics is correct on rotating planets. Reuses the Damageable
// health/loot contract so player weapons kill it.

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyManticore : Damageable
    {
        [Header("Manticore AI")]
        public float detectRange = 18f;
        public float chaseSpeed  = 4.5f;
        public float wanderSpeed = 1.4f;
        public float accel       = 12f;
        public float wanderRadius = 7f;
        public float wanderPause  = 3f;

        [Header("Melee")]
        public float meleeRange    = 2.6f;
        public float meleeDamage   = 14f;
        public float meleeCooldown = 1.0f;

        [Header("Ranged — tail spikes")]
        public float rangedRange    = 15f;
        public float rangedMinRange = 4.5f;
        public float spikeDamage        = 12f;
        public float spikePoisonDps     = 4f;
        public float spikePoisonDuration = 3f;
        public float rangedCooldown = 1.8f;
        public int   spikesPerVolley = 3;
        public float spikeSpread    = 0.12f;
        [Tooltip("Material applied to the spawned spike projectiles. Assigned by the setup wizard.")]
        public Material spikeMaterial;

        private Rigidbody _rb;
        private Transform _player;
        private Vector3 _home, _wanderTarget;
        private float _nextWanderAt, _nextMeleeAt, _nextRangedAt;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 80f);
            base.Awake();

            // Detach from the chunk-scatter parent (it moves with the rotating planet, but Rigidbody
            // physics is world-space). Root-level = correct physics on spherical worlds. Mirrors EnemyGhoul.
            if (transform.parent != null) transform.SetParent(null, true);

            _home = transform.position;
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.freezeRotation = true;
            PickWander();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            Vector3 up   = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(pos);
            EnsurePlayer();

            Vector3 flatToPlayer = (_player != null) ? Vector3.ProjectOnPlane(_player.position - pos, up) : Vector3.zero;
            float distP = flatToPlayer.magnitude;
            bool aggro = _player != null && distP <= detectRange;

            Vector3 moveDir;
            float spd;
            if (aggro)
            {
                Vector3 toP = flatToPlayer.sqrMagnitude > 0.0001f ? flatToPlayer.normalized
                                                                  : Vector3.ProjectOnPlane(transform.forward, up).normalized;
                moveDir = toP;
                if (distP > rangedRange)        spd = chaseSpeed;   // close the gap
                else if (distP > rangedMinRange) spd = 0f;           // firing band — hold + loose spikes
                else                             spd = chaseSpeed;   // close in for melee

                // Ranged volley (firing band).
                if (distP <= rangedRange && distP >= rangedMinRange && Time.time >= _nextRangedAt)
                {
                    _nextRangedAt = Time.time + rangedCooldown;
                    FireSpikeVolley(up);
                }
                // Melee claws.
                if (distP <= meleeRange && Time.time >= _nextMeleeAt)
                {
                    _nextMeleeAt = Time.time + meleeCooldown;
                    MeleeAttack();
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
                }
                else
                {
                    moveDir = flatW.normalized;
                    spd = wanderSpeed;
                }
            }

            // Tangent velocity toward the target + radial gravity integration.
            Vector3 v = _rb.linearVelocity;
            Vector3 radial = Vector3.Project(v, up);
            Vector3 tangent = v - radial;
            tangent = Vector3.MoveTowards(tangent, moveDir * spd, accel * dt);
            radial += grav * dt;
            _rb.linearVelocity = tangent + radial;

            // Face the player while aggro (to aim spikes), else face travel direction.
            Vector3 face;
            if (aggro && flatToPlayer.sqrMagnitude > 0.0001f) face = flatToPlayer.normalized;
            else if (spd > 0.01f && moveDir != Vector3.zero) face = moveDir;
            else face = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (face.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(face, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 0.12f));
            }
        }

        private void FireSpikeVolley(Vector3 up)
        {
            if (_player == null) return;
            Vector3 from = transform.position + up * 1.25f + transform.forward * 0.7f;   // tail/stinger origin
            Vector3 baseDir = (_player.position + up * 1.0f) - from;
            if (baseDir.sqrMagnitude < 0.001f) return;
            Quaternion aim = Quaternion.LookRotation(baseDir.normalized, up);

            for (int i = 0; i < spikesPerVolley; i++)
            {
                Vector3 spread = new Vector3(
                    Random.Range(-spikeSpread, spikeSpread),
                    Random.Range(-spikeSpread * 0.5f, spikeSpread * 0.5f),
                    1f).normalized;
                ManticoreSpike.Spawn(from, aim * spread, gameObject, spikeMaterial,
                    spikeDamage, spikePoisonDps, spikePoisonDuration);
            }
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Manticore", "Looses tail spikes!", null, new Color(0.55f, 0.85f, 0.25f));
        }

        private void MeleeAttack()
        {
            var ps = (_player != null) ? _player.GetComponent<VoxelEngine.Player.PlayerStats>() : null;
            if (ps != null) ps.TakeDamage(meleeDamage);
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Manticore", "Claws you!", null, new Color(0.9f, 0.25f, 0.2f));
        }

        private void PickWander()
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
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
    }
}
