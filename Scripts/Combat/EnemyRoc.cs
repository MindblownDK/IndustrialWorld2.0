// Assets/Scripts/VoxelEngine/Combat/EnemyRoc.cs
//
// A mythical MINI-BOSS (Combat Phase 3i): a colossal bird of prey. Boss-tier version
// of the Griffin flight AI — it CIRCLES overhead at altitude, DIVE-BOMBS with massive
// talons, and periodically beats its wings for a GUST (AoE damage + knockback + a dust
// ring). ENRAGES below 50% HP (faster, shorter cooldowns). Guaranteed boss loot.
// Reuses Damageable + CreatureHealthBar; radial-aware; detaches from the chunk on Awake.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyRoc : Damageable
    {
        [Header("Roc Flight AI")]
        public float detectRange = 30f;
        public float hoverHeight = 8f;
        public float orbitRadius = 7f;
        public float orbitSpeed  = 45f;
        public float engageSpeed = 9f;
        public float diveSpeed   = 20f;
        public float wanderSpeed = 5f;
        public float accel       = 12f;

        [Header("Dive attack")]
        public float diveCooldown = 4f;
        public float meleeRange   = 3.6f;
        public float diveDamage   = 30f;

        [Header("Wing gust (AoE)")]
        public float gustCooldown   = 6f;
        public float gustRadius     = 7f;
        public float gustDamage     = 18f;
        public float gustKnockback  = 14f;
        public Material dustMaterial;

        [Header("Boss")]
        public float enrageThreshold = 0.5f;   // enrage below this fraction of max HP
        public float enrageSpeedMul  = 1.3f;
        public float enrageCooldownMul = 0.6f;
        [Tooltip("Guaranteed boss drop, rolled on death. Assigned by the setup wizard.")]
        public ItemDefinition stormCore;

        private Rigidbody _rb;
        private Transform _player;
        private Vector3 _home, _wanderTarget;
        private float _orbitAngle, _nextWanderAt, _nextDiveAt, _nextGustAt;
        private bool _diving;
        private bool _enraged;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 350f);
            base.Awake();

            if (transform.parent != null) transform.SetParent(null, true); // detach chunk (spheres)

            _home = transform.position;
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.freezeRotation = true;
            PickWander();
        }

        private bool Enraged
        {
            get
            {
                if (!_enraged && Health <= maxHealth * enrageThreshold && Health > 0f)
                {
                    _enraged = true;
                    if (showHitFeedback)
                        VoxelEngine.UI.BuildFeedbackHud.Show("ROC", "Enrages! The colossal bird fights harder!", null, new Color(0.95f, 0.25f, 0.15f));
                }
                return _enraged;
            }
        }
        private float SpeedMul => Enraged ? enrageSpeedMul : 1f;
        private float CdMul => Enraged ? enrageCooldownMul : 1f;

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
                    desiredPos = _player.position + up * 1.8f;
                    spd = diveSpeed * SpeedMul;
                    if (Vector3.Distance(pos, _player.position) < meleeRange)
                    {
                        TalonStrike(up);
                        _diving = false;
                        _nextDiveAt = Time.time + diveCooldown * CdMul;
                    }
                }
                else
                {
                    _orbitAngle += orbitSpeed * SpeedMul * dt;
                    Vector3 fwd = Vector3.ProjectOnPlane(_player.position - pos, up);
                    if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(transform.forward, up);
                    fwd = fwd.normalized;
                    Vector3 right = Vector3.Cross(up, fwd).normalized;
                    float a = _orbitAngle * Mathf.Deg2Rad;
                    Vector3 circleOffset = (fwd * Mathf.Cos(a) + right * Mathf.Sin(a)) * orbitRadius;
                    desiredPos = _player.position + up * hoverHeight + circleOffset;
                    spd = engageSpeed * SpeedMul;

                    if (Time.time >= _nextDiveAt) _diving = true;
                    if (Time.time >= _nextGustAt) { WingGust(pos, up); _nextGustAt = Time.time + gustCooldown * CdMul; }
                }
            }
            else
            {
                if (Time.time >= _nextWanderAt) PickWander();
                desiredPos = _wanderTarget;
                spd = wanderSpeed;
            }

            Vector3 toDesired = desiredPos - pos;
            if (toDesired.sqrMagnitude > 0.0001f)
            {
                Vector3 targetVel = toDesired.normalized * spd;
                _rb.linearVelocity = Vector3.MoveTowards(_rb.linearVelocity, targetVel, accel * dt);
            }

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

        private void TalonStrike(Vector3 up)
        {
            var ps = (_player != null) ? _player.GetComponent<VoxelEngine.Player.PlayerStats>() : null;
            if (ps != null) ps.TakeDamage(diveDamage);
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("ROC", "Dive-bombs with massive talons!", null, new Color(0.95f, 0.5f, 0.15f));
        }

        private void WingGust(Vector3 pos, Vector3 up)
        {
            // AoE damage + knockback if the player is within gustRadius (tangent distance).
            var ps = VoxelEngine.Player.PlayerStats.Instance;
            if (ps != null)
            {
                Vector3 toP = Vector3.ProjectOnPlane(ps.transform.position - pos, up);
                if (toP.magnitude <= gustRadius)
                {
                    ps.TakeDamage(gustDamage);
                    var pc = ps.GetComponent<VoxelEngine.Player.PlayerController>();
                    if (pc != null) pc.ApplyImpulse(toP.normalized * gustKnockback + up * 3f);
                }
            }
            // Dust ring visual at the Roc.
            SpawnGustFlash(pos, up);
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("ROC", "Beats its wings — gust!", null, new Color(0.8f, 0.7f, 0.5f));
        }

        private void SpawnGustFlash(Vector3 pos, Vector3 up)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "RocGust";
            ring.transform.position = pos + up * 0.1f;
            ring.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            ring.transform.localScale = new Vector3(gustRadius, 0.18f, gustRadius);
            var col = ring.GetComponent<Collider>(); if (col != null) Destroy(col);
            var ren = ring.GetComponent<Renderer>(); if (dustMaterial != null) ren.sharedMaterial = dustMaterial;
            Destroy(ring, 0.45f);
        }

        // Guaranteed boss drops: base loot (pinions) + a guaranteed Storm Core.
        protected override void Die(DamageEvent e)
        {
            Vector3 dropPos = transform.position + Vector3.up * 0.8f;
            base.Die(e);
            if (stormCore != null) DroppedItem.Spawn(new ItemStack(stormCore, 1), dropPos, Vector3.up);
        }

        private void PickWander()
        {
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(_home);
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(up, fwd.normalized).normalized;
            Vector2 r = Random.insideUnitCircle * 12f;
            _wanderTarget = _home + up * hoverHeight + right * r.x + fwd.normalized * r.y;
            _nextWanderAt = Time.time + Random.Range(3f, 6f);
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
