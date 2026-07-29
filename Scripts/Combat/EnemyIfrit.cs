// Assets/Scripts/VoxelEngine/Combat/EnemyIfrit.cs
//
// A mythical spellcaster (Combat Phase 3h): a high-tier fire spirit. It KITES at range,
// cycles three abilities — hurls FIREBALLS, TELEPORT-BLINKS to reposition around you,
// and raises FIRE WALLS (lingering AoE) at your feet — and ignites an armor-escalating
// BURN. Fragile but deadly. Reuses the Damageable health/loot contract; radial-gravity
// aligned; detaches from the chunk parent on Awake so Rigidbody physics is correct on
// rotating planets.

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyIfrit : Damageable
    {
        [Header("Ifrit AI")]
        public float detectRange = 20f;
        public float castRange   = 11f;     // kites to this distance
        public float chaseSpeed  = 4.0f;
        public float wanderSpeed = 1.3f;
        public float accel       = 12f;
        public float wanderRadius = 7f;
        public float wanderPause  = 3f;

        [Header("Fireballs")]
        public float fireballCooldown = 1.4f;
        public int   fireballsPerCast = 2;
        public float fireballDamage   = 14f;
        public float fireballBurnDps     = 6f;
        public float fireballBurnDuration = 3f;
        public float fireballSpread = 0.1f;
        public Material fireballMaterial;

        [Header("Teleport")]
        public float teleportCooldown = 5f;
        public float teleportRange    = 10f;   // blinks to ~this far from the player

        [Header("Fire Wall")]
        public float firewallCooldown  = 7f;
        public float firewallDuration  = 5f;
        public float firewallBurnDps    = 7f;
        public float firewallRadius     = 1.9f;
        public Material firewallMaterial;

        private Rigidbody _rb;
        private Transform _player;
        private Vector3 _home, _wanderTarget;
        private float _nextWanderAt, _nextFireball, _nextTeleport, _nextFirewall;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 50f);
            base.Awake();

            if (transform.parent != null) transform.SetParent(null, true); // detach chunk (spheres)

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
                if (distP > castRange)            { moveDir = toP;  spd = chaseSpeed; }          // approach
                else if (distP < castRange * 0.6f){ moveDir = -toP; spd = chaseSpeed * 0.7f; }   // kite back
                else                              { moveDir = toP;  spd = 0f; }                  // hold + cast

                if (Time.time >= _nextFireball)  CastFireballs(up);
                if (Time.time >= _nextTeleport)  Teleport(up);
                if (Time.time >= _nextFirewall)  CastFireWall(up);
            }
            else
            {
                if (Time.time >= _nextWanderAt) PickWander();
                Vector3 flatW = Vector3.ProjectOnPlane(_wanderTarget - pos, up);
                if (flatW.magnitude < 0.6f) { moveDir = Vector3.ProjectOnPlane(transform.forward, up).normalized; spd = 0f; }
                else { moveDir = flatW.normalized; spd = wanderSpeed; }
            }

            // Tangent velocity + radial gravity.
            Vector3 v = _rb.linearVelocity;
            Vector3 radial = Vector3.Project(v, up);
            Vector3 tangent = v - radial;
            tangent = Vector3.MoveTowards(tangent, moveDir * spd, accel * dt);
            radial += grav * dt;
            _rb.linearVelocity = tangent + radial;

            // Face the player while aggro.
            Vector3 face = (aggro && flatToPlayer.sqrMagnitude > 0.0001f) ? flatToPlayer.normalized
                                                                          : Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (face.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(face, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 0.12f));
            }
        }

        private void CastFireballs(Vector3 up)
        {
            _nextFireball = Time.time + fireballCooldown;
            if (_player == null) return;
            Vector3 from = transform.position + up * 1.3f;
            Vector3 baseDir = (_player.position + up * 1.2f) - from;
            if (baseDir.sqrMagnitude < 0.001f) return;
            for (int i = 0; i < fireballsPerCast; i++)
            {
                Vector3 spread = new Vector3(Random.Range(-fireballSpread, fireballSpread),
                                             Random.Range(-fireballSpread * 0.5f, fireballSpread * 0.5f), 1f).normalized;
                Fireball.Spawn(from, baseDir.normalized + spread, gameObject, fireballMaterial,
                    fireballDamage, fireballBurnDps, fireballBurnDuration);
            }
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Ifrit", "Hurls fireballs!", null, new Color(1.0f, 0.5f, 0.1f));
        }

        private void Teleport(Vector3 up)
        {
            _nextTeleport = Time.time + teleportCooldown;
            if (_player == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(_player.position - transform.position, up);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd = fwd.normalized;
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dest = _player.position + (fwd * Mathf.Cos(a) + right * Mathf.Sin(a)) * teleportRange;
            transform.position = dest + up * 0.1f;     // blink to a flanking position near surface level
            _rb.linearVelocity = Vector3.zero;
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Ifrit", "Vanishes in smoke!", null, new Color(0.6f, 0.2f, 0.7f));
        }

        private void CastFireWall(Vector3 up)
        {
            _nextFirewall = Time.time + firewallCooldown;
            if (_player == null) return;
            FireWallHazard.Spawn(_player.position + up * 0.05f, up, firewallMaterial,
                firewallDuration, firewallBurnDps, firewallRadius);
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Ifrit", "Raises a wall of fire!", null, new Color(1.0f, 0.4f, 0.1f));
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
