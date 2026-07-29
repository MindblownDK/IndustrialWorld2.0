// Assets/Scripts/VoxelEngine/Combat/EnemyBasilisk.cs
//
// A mythical "Basilisk-class" creature (Combat Phase 3j): a large serpentine beast.
// Its signature is a PETRIFYING GAZE — a forward cone that slows the player (turning
// them toward stone); circle-strafe to break the cone, or break line of sight. Also
// delivers a venomous bite (poison). Reuses Damageable + CreatureHealthBar; radial-
// gravity aligned; detaches from the chunk parent on Awake.

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyBasilisk : Damageable
    {
        [Header("Basilisk AI")]
        public float detectRange = 18f;
        public float chaseSpeed  = 3.6f;
        public float wanderSpeed = 1.2f;
        public float accel       = 11f;
        public float wanderRadius = 7f;
        public float wanderPause  = 3f;

        [Header("Petrifying Gaze")]
        public float gazeCooldown  = 3f;
        public float gazeRange     = 14f;
        public float gazeHalfAngle = 28f;       // cone half-angle (deg)
        public float petrifySlow      = 0.6f;   // 60% movement slow
        public float petrifyDuration  = 2.5f;

        [Header("Venom Bite")]
        public float biteRange    = 2.8f;
        public float biteDamage   = 12f;
        public float bitePoisonDps     = 5f;
        public float bitePoisonDuration = 3f;
        public float biteCooldown  = 1.2f;

        private Rigidbody _rb;
        private Transform _player;
        private Vector3 _home, _wanderTarget;
        private float _nextWanderAt, _nextGazeAt, _nextBiteAt;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 90f);
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
                moveDir = flatToPlayer.sqrMagnitude > 0.0001f ? flatToPlayer.normalized
                                                              : Vector3.ProjectOnPlane(transform.forward, up).normalized;
                spd = chaseSpeed;

                if (Time.time >= _nextGazeAt)
                {
                    _nextGazeAt = Time.time + gazeCooldown;
                    PetrifyingGaze(pos, up);
                }
                if (distP <= biteRange && Time.time >= _nextBiteAt)
                {
                    _nextBiteAt = Time.time + biteCooldown;
                    VenomBite();
                }
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

            // Face the player while aggro (aims the gaze cone); else face travel direction.
            Vector3 face = (aggro && flatToPlayer.sqrMagnitude > 0.0001f) ? flatToPlayer.normalized
                                                                          : Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (face.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(face, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 0.10f));
            }
        }

        // Forward cone of petrification: slows the player if they're in front + in range.
        // Dodge by breaking the cone (circle-strafe to its flank/rear) or outranging it.
        // (The Basilisk turns slowly, so a fast or sprinting player can escape the cone.)
        private void PetrifyingGaze(Vector3 pos, Vector3 up)
        {
            if (_player == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 toP = Vector3.ProjectOnPlane(_player.position - pos, up);
            float dist = toP.magnitude;
            if (dist > gazeRange) return;
            if (Vector3.Dot(forward, toP.normalized) < Mathf.Cos(gazeHalfAngle * Mathf.Deg2Rad)) return;

            var pc = _player.GetComponent<VoxelEngine.Player.PlayerController>();
            if (pc != null) pc.ApplyPetrify(petrifySlow, petrifyDuration);
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Basilisk", "Petrifying gaze — you feel like stone!", null, new Color(0.6f, 0.8f, 0.4f));
        }

        private void VenomBite()
        {
            var ps = (_player != null) ? _player.GetComponent<VoxelEngine.Player.PlayerStats>() : null;
            if (ps != null)
            {
                ps.TakeDamage(biteDamage);
                ps.ApplyPoison(bitePoisonDps, bitePoisonDuration);
            }
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Basilisk", "Venomous bite!", null, new Color(0.5f, 0.8f, 0.2f));
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
