// Assets/Scripts/VoxelEngine/Combat/EnemyKarkadann.cs
//
// A mythical heavy brute (Combat Phase 3g): a massive armored horned beast. Its
// signature is a TELEGRAPHED STRAIGHT-LINE CHARGE — it paws the ground (windup),
// then sprints forward; dodge it or it tramples you for heavy damage + knockback.
// Heavy FRONTAL ARMOR reduces hits taken from the front (flank or rear it for full
// damage), except while it is stunned recovering from a missed charge. Reuses the
// Damageable health/loot contract; radial-gravity aligned; detaches from the chunk
// parent on Awake so Rigidbody physics is correct on rotating planets.

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyKarkadann : Damageable
    {
        private enum State { Wander, Approach, Windup, Charge, Recover }

        [Header("Karkadann AI")]
        public float detectRange   = 20f;
        public float approachSpeed = 2.6f;
        public float wanderSpeed   = 1.2f;
        public float accel         = 12f;
        public float wanderRadius  = 7f;
        public float wanderPause   = 3f;

        [Header("Charge")]
        public float chargeRange    = 16f;    // begin a charge when within this
        public float chargeWindup   = 1.0f;   // telegraph duration (paws the ground)
        public float chargeSpeed    = 13f;
        public float chargeDuration = 1.6f;   // how long the sprint lasts
        public float chargeDamage   = 34f;
        public float chargeKnockback = 12f;
        public float chargeCooldown = 4.0f;

        [Header("Armor")]
        [Range(0f, 0.9f)] public float frontalArmorReduction = 0.6f; // front hits reduced (except while Recovering)
        public float recoverTime = 1.6f;                              // stunned (full damage from all sides)

        private State _state = State.Wander;
        private Rigidbody _rb;
        private Transform _player;
        private Vector3 _home, _wanderTarget, _chargeDir;
        private float _nextWanderAt, _stateTimer, _nextChargeAt;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 140f);
            base.Awake();

            if (transform.parent != null) transform.SetParent(null, true); // detach chunk (spheres)

            _home = transform.position;
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.freezeRotation = true;
            PickWander();
        }

        // Heavy frontal armor: hits from the front are reduced (flank/rear for full damage).
        // Disabled while Recovering (the stunned window after a charge = a damage opportunity).
        public override void TakeDamage(DamageEvent e)
        {
            if (_state != State.Recover && e.source != null)
            {
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
                Vector3 toSource = Vector3.ProjectOnPlane(e.source.transform.position - transform.position, up).normalized;
                if (Vector3.Dot(forward, toSource) > 0.2f)
                    e = new DamageEvent { amount = e.amount * (1f - frontalArmorReduction), type = e.type,
                        point = e.point, direction = e.direction, source = e.source };
            }
            base.TakeDamage(e);
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
            Vector3 toPlayer = flatToPlayer.sqrMagnitude > 0.0001f ? flatToPlayer.normalized
                                                                  : Vector3.ProjectOnPlane(transform.forward, up).normalized;

            Vector3 moveDir = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            float spd = 0f;
            Vector3 faceDir = moveDir;

            switch (_state)
            {
                default:
                case State.Wander:
                    if (aggro) { _state = State.Approach; break; }
                    if (Time.time >= _nextWanderAt) PickWander();
                    Vector3 flatW = Vector3.ProjectOnPlane(_wanderTarget - pos, up);
                    if (flatW.magnitude < 0.6f) { moveDir = toPlayer; spd = 0f; }
                    else { moveDir = flatW.normalized; spd = wanderSpeed; faceDir = moveDir; }
                    break;

                case State.Approach:
                    if (!aggro) { _state = State.Wander; break; }
                    if (distP <= chargeRange && Time.time >= _nextChargeAt)
                    {
                        _state = State.Windup; _stateTimer = chargeWindup;
                        if (showHitFeedback)
                            VoxelEngine.UI.BuildFeedbackHud.Show("Karkadann", "Paws the ground...", null, new Color(0.90f, 0.50f, 0.20f));
                        break;
                    }
                    moveDir = toPlayer; spd = approachSpeed; faceDir = toPlayer;
                    break;

                case State.Windup:
                    _stateTimer -= dt;
                    spd = 0f; faceDir = toPlayer;          // stand still, track the player, telegraph
                    if (_stateTimer <= 0f)
                    {
                        _chargeDir = toPlayer;             // lock the charge line on the player's current position
                        _state = State.Charge; _stateTimer = chargeDuration;
                    }
                    break;

                case State.Charge:
                    _stateTimer -= dt;
                    moveDir = _chargeDir; spd = chargeSpeed; faceDir = _chargeDir;
                    if (_player != null && distP < 2.7f)
                    {
                        ChargeHit(up);
                        _state = State.Recover; _stateTimer = recoverTime; _nextChargeAt = Time.time + chargeCooldown;
                    }
                    else if (_stateTimer <= 0f)
                    {
                        _state = State.Recover; _stateTimer = recoverTime; _nextChargeAt = Time.time + chargeCooldown;
                    }
                    break;

                case State.Recover:
                    _stateTimer -= dt;
                    spd = 0f; faceDir = toPlayer;          // stunned, exposed
                    if (_stateTimer <= 0f) _state = aggro ? State.Approach : State.Wander;
                    break;
            }

            // Tangent velocity toward the target + radial gravity integration.
            Vector3 v = _rb.linearVelocity;
            Vector3 radial = Vector3.Project(v, up);
            Vector3 tangent = v - radial;
            tangent = Vector3.MoveTowards(tangent, moveDir * spd, accel * dt);
            radial += grav * dt;
            _rb.linearVelocity = tangent + radial;

            if (faceDir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(faceDir, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 0.12f));
            }
        }

        private void ChargeHit(Vector3 up)
        {
            var ps = (_player != null) ? _player.GetComponent<VoxelEngine.Player.PlayerStats>() : null;
            if (ps != null) ps.TakeDamage(chargeDamage);
            var pc = (_player != null) ? _player.GetComponent<VoxelEngine.Player.PlayerController>() : null;
            if (pc != null)
            {
                Vector3 shove = Vector3.ProjectOnPlane(_chargeDir, up).normalized * chargeKnockback + up * 4f;
                pc.ApplyImpulse(shove);
            }
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Karkadann", "Tramples you!", null, new Color(0.90f, 0.30f, 0.20f));
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
