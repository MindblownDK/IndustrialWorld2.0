// Assets/Scripts/VoxelEngine/Combat/EnemyGriffin.cs
//
// A mythical aerial harasser (Combat Phase 3f): a heraldic lion-eagle. Flies — it
// holds an altitude above the player, CIRCLES overhead, then DIVE-BOMBS to strike
// with its talons before climbing back. Reuses the Damageable health/loot contract so
// player weapons kill it. Radial-aware (orbits relative to radial "up") and detaches
// from the chunk-scatter parent on Awake so it flies freely on spherical worlds.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyGriffin : Damageable
    {
        [Header("Griffin Flight AI")]
        public float detectRange = 22f;
        public float hoverHeight = 6f;     // altitude above the player while circling
        public float orbitRadius = 5f;
        public float orbitSpeed  = 60f;     // deg/s around the player
        public float engageSpeed = 8f;
        public float diveSpeed   = 16f;
        public float wanderSpeed = 4f;
        public float accel       = 14f;

        [Header("Attack")]
        public float meleeRange   = 2.8f;
        public float attackDamage = 16f;
        public float diveCooldown = 3.5f;
        [Range(0f, 1f)] public float heartDropChance = 0.2f;
        [Tooltip("Rare drop rolled on death. Assigned by the setup wizard.")]
        public ItemDefinition griffinHeart;

        private Rigidbody _rb;
        private Transform _player;
        private Vector3 _home, _wanderTarget;
        private float _orbitAngle, _nextWanderAt, _nextDiveAt;
        private bool _diving;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 55f);
            base.Awake();

            // Detach from the chunk-scatter parent so we fly freely (Rigidbody physics is
            // world-space; a chunk parent moves with the rotating planet). Mirrors the Ghoul.
            if (transform.parent != null) transform.SetParent(null, true);

            _home = transform.position;
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;          // the griffin flies under its own control
            _rb.freezeRotation = true;
            PickWander();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);
            EnsurePlayer();

            bool aggro = _player != null && Vector3.Distance(pos, _player.position) <= detectRange;
            Vector3 desiredPos;
            float spd;

            if (aggro)
            {
                if (_diving)
                {
                    // Swoop down to the player's level and strike on contact.
                    desiredPos = _player.position + up * 1.6f;
                    spd = diveSpeed;
                    if (Vector3.Distance(pos, _player.position) < meleeRange)
                    {
                        Attack();
                        _diving = false;
                        _nextDiveAt = Time.time + diveCooldown;
                    }
                }
                else
                {
                    // Circle the player at hover altitude.
                    _orbitAngle += orbitSpeed * dt;
                    Vector3 fwd = Vector3.ProjectOnPlane(_player.position - pos, up);
                    if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(transform.forward, up);
                    fwd = fwd.normalized;
                    Vector3 right = Vector3.Cross(up, fwd).normalized;
                    float a = _orbitAngle * Mathf.Deg2Rad;
                    Vector3 circleOffset = (fwd * Mathf.Cos(a) + right * Mathf.Sin(a)) * orbitRadius;
                    desiredPos = _player.position + up * hoverHeight + circleOffset;
                    spd = engageSpeed;
                    if (Time.time >= _nextDiveAt) _diving = true;
                }
            }
            else
            {
                // Patrol: drift between points at hover altitude above the spawn.
                if (Time.time >= _nextWanderAt) PickWander();
                desiredPos = _wanderTarget;
                spd = wanderSpeed;
            }

            // Steer velocity toward the desired position (no gravity — we fly).
            Vector3 toDesired = desiredPos - pos;
            if (toDesired.sqrMagnitude > 0.0001f)
            {
                Vector3 targetVel = toDesired.normalized * spd;
                _rb.linearVelocity = Vector3.MoveTowards(_rb.linearVelocity, targetVel, accel * dt);
            }

            // Face the dive target while diving, else face travel direction.
            Vector3 faceDir = (_diving && _player != null)
                ? Vector3.ProjectOnPlane(_player.position - pos, up)
                : (_rb.linearVelocity.sqrMagnitude > 0.25f ? Vector3.ProjectOnPlane(_rb.linearVelocity, up)
                                                     : Vector3.ProjectOnPlane(transform.forward, up));
            if (faceDir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(faceDir, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 0.12f));
            }
        }

        private void Attack()
        {
            var ps = (_player != null) ? _player.GetComponent<VoxelEngine.Player.PlayerStats>() : null;
            if (ps != null) ps.TakeDamage(attackDamage);
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Griffin", "Dive-bombs you!", null, new Color(0.90f, 0.70f, 0.20f));
        }

        // Drop feathers/talons (via base) plus a rare Griffin Heart.
        protected override void Die(DamageEvent e)
        {
            Vector3 dropPos = transform.position + Vector3.up * 0.6f;
            bool dropHeart = griffinHeart != null && Random.value <= heartDropChance;
            base.Die(e);
            if (dropHeart) DroppedItem.Spawn(new ItemStack(griffinHeart, 1), dropPos, Vector3.up);
        }

        private void PickWander()
        {
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(_home);
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(up, fwd.normalized).normalized;
            Vector2 r = Random.insideUnitCircle * 10f;
            _wanderTarget = _home + up * hoverHeight + right * r.x + fwd.normalized * r.y;
            _nextWanderAt = Time.time + Random.Range(2f, 5f);
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
