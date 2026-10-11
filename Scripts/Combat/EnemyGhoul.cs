// Assets/Scripts/VoxelEngine/Combat/EnemyGhoul.cs
//
// First hostile enemy. A shambling ghoul that wanders, detects the player, chases
// across the spherical surface (radial gravity + upright alignment), and melees on
// contact. Uses the shared Damageable health/loot contract so player weapons kill it.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Environment;

namespace VoxelEngine.Combat
{
    /// <summary>17.18.0-dev: threat-tier skin over the one ghoul rig. Normal is
    /// every scout and pack member that has ever spawned; Elite leads organized
    /// waves; SiegeBrute is the walking demolition answer to a factory district
    /// that has been dirtying the ecology for hours. The byte values ride the
    /// existing hostile spawn broadcast, so keep them stable.</summary>
    public enum GhoulVariant : byte
    {
        Normal = 0,
        Elite = 1,
        SiegeBrute = 2,
    }

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
        // 17.19.0-dev: visible windup before every bite - the ghoul rears back
        // with flaring eyes, THEN snaps. 0 = none, 1 = player, 2 = logistics,
        // 3 = industrial source.
        private float _windupUntil;
        private int _pendingAttack;
        private EnemyRigAnimator _anim;
        public const float AttackWindupSeconds = 0.45f;
        private PollutionEmitter _industrialTarget;
        private PlacedBlock _industrialTargetBlock;
        private PlacedBlock _logisticsTarget;
        private bool _ambushHold;
        private bool _raidLogistics;
        private float _ambushUntil;
        private float _nextLogisticsScan;

        // 17.18.0-dev: threat-tier variant state. The host applies stats AND
        // visuals at spawn; guests build the replica from the streamed variant
        // byte and apply visuals only (stats arrive through the health stream).
        private GhoulVariant _visualVariant = GhoulVariant.Normal;
        private bool _siegePriority;

        /// <summary>The threat tier this ghoul was promoted to. Normal for every
        /// scout and pack member; set once, never re-rolled.</summary>
        public GhoulVariant Variant { get; private set; } = GhoulVariant.Normal;

        // ── variant tuning (17.18.0-dev) ──────────────────────────────
        // Elites are the wave's spearhead: tough, hard-hitting, faster and with
        // keener eyes. Siege brutes trade speed for demolition mass - they walk
        // PAST the player to dismantle the machines, and only bite back when
        // something steps inside their reach.
        public const float EliteHealthScale = 2.5f;
        public const float EliteDamageScale = 1.5f;
        public const float EliteSpeedScale = 1.15f;
        public const float EliteDetectScale = 1.25f;
        public const float EliteVisualScale = 1.25f;

        public const float SiegeHealthScale = 6f;
        public const float SiegeDamageScale = 2.2f;
        public const float SiegeSpeedScale = 0.8f;
        public const float SiegeDetectScale = 1.2f;
        public const float SiegeVisualScale = 1.9f;
        public const float SiegeBuildingScale = 2f;

        /// <summary>
        /// Host-side promotion, called right after instantiation and before the
        /// first re-announce: scales stats, tops health back up to the new
        /// maximum, enriches the loot roll and applies the visual tier. Idempotent
        /// per variant. Guests never call this - they use ApplyVariantVisual.
        /// </summary>
        public void ApplyVariant(GhoulVariant variant)
        {
            if (variant == GhoulVariant.Normal || variant == Variant) return;
            Variant = variant;

            if (variant == GhoulVariant.Elite)
            {
                maxHealth *= EliteHealthScale;
                attackDamage *= EliteDamageScale;
                chaseSpeed *= EliteSpeedScale;
                wanderSpeed *= EliteSpeedScale;
                detectRange *= EliteDetectScale;
                maxDrops += 1;
            }
            else
            {
                maxHealth *= SiegeHealthScale;
                attackDamage *= SiegeDamageScale;
                chaseSpeed *= SiegeSpeedScale;
                wanderSpeed *= SiegeSpeedScale;
                detectRange *= SiegeDetectScale;
                minDrops += 1;
                maxDrops += 2;
                dropCount += 1;
                _siegePriority = true;
                // The brute is a demolition engine: it always hunts the factory
                // itself, machinery and exposed logistics alike.
                _raidLogistics = true;
            }
            // Health is a protected-set property on Damageable; a derived class
            // may top it up to the new maximum after the Awake assignment.
            Health = maxHealth;
            ApplyVariantVisual(variant);
        }

        /// <summary>
        /// Visual tier only: body scale and a per-instance tint. Safe on guest
        /// replicas (kinematic, AI off) and idempotent per variant. Tints multiply
        /// onto the prefab's authored colors, so the stone-green skin goes rusty
        /// red for elites and cold ash for brutes without touching shared assets.
        /// </summary>
        public void ApplyVariantVisual(GhoulVariant variant)
        {
            if (variant == _visualVariant) return;
            _visualVariant = variant;

            float scale = variant == GhoulVariant.Elite ? EliteVisualScale
                : variant == GhoulVariant.SiegeBrute ? SiegeVisualScale
                : 1f;
            transform.localScale = Vector3.one * scale;

            Color tint = variant == GhoulVariant.Elite ? new Color(1.35f, 0.62f, 0.50f)
                : variant == GhoulVariant.SiegeBrute ? new Color(0.70f, 0.72f, 0.80f)
                : Color.white;
            if (tint == Color.white) return;

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                // .material instantiates per renderer - a handful of elites on
                // screen never justifies sharing, and shared assets stay clean.
                var mat = renderers[i].material;
                if (mat != null) mat.color *= tint;
            }
        }

        public bool HasActiveIndustrialTarget => _industrialTarget != null
            && _industrialTargetBlock != null && _industrialTarget.IsActivelyEmitting;
        public Vector3 IndustrialTargetPosition => _industrialTargetBlock != null
            ? _industrialTargetBlock.transform.position
            : transform.position;

        /// <summary>
        /// Gives a pollution-attracted scout one host-only industrial objective. Player targets
        /// still take priority inside normal detection range; shutting down or filtering the
        /// source breaks the scent without deleting an enemy that already exists.
        /// </summary>
        public void SetIndustrialTarget(PollutionEmitter emitter)
        {
            _industrialTarget = emitter;
            _industrialTargetBlock = emitter != null
                ? emitter.GetComponentInParent<PlacedBlock>()
                : null;
            if (emitter == null)
            {
                _ambushHold = false;
                _raidLogistics = false;
                _logisticsTarget = null;
            }
        }

        /// <summary>
        /// Holds this recruit off the approach instead of walking straight to the
        /// source. Player contact or the hold expiring releases it. The source
        /// leash is unchanged.
        /// </summary>
        public void BeginAmbush(float holdSeconds)
        {
            _ambushHold = true;
            _ambushUntil = Time.time + Mathf.Max(5f, holdSeconds);
            _home = transform.position;
            _wanderTarget = _home;
            _nextWanderAt = Time.time + 1.5f;
        }

        public void EnableLogisticsRaid() => _raidLogistics = true;

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

            _anim = EnemyRigAnimator.Attach(this);   // 17.19.0-dev: walk cycle + telegraphs
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            Vector3 up   = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(pos);
            EnsurePlayer();

            // Player targets take priority. Outside normal detection range, a scout spawned by
            // an active industrial outlet follows that source instead of wandering near the player.
            Vector3 flatToPlayer = (_player != null)
                ? Vector3.ProjectOnPlane(_player.position - pos, up)
                : Vector3.zero;
            float distP = flatToPlayer.magnitude;
            bool chasingPlayer = _player != null && distP <= detectRange;
            if (_industrialTarget != null && !HasActiveIndustrialTarget)
                SetIndustrialTarget(null);
            // 17.18.0-dev: a siege brute walks PAST the player - the factory is
            // the objective. The player only becomes the focus by stepping
            // inside biting reach, or by the industrial objective dying.
            if (_siegePriority && chasingPlayer && distP > attackRange * 1.5f
                && HasActiveIndustrialTarget)
                chasingPlayer = false;
            if (chasingPlayer || (_ambushHold && Time.time >= _ambushUntil))
                _ambushHold = false;
            bool chasingIndustry = !chasingPlayer && !_ambushHold && HasActiveIndustrialTarget;

            // 17.19.0-dev: an expired windup lands whatever it telegraphed. The
            // player bite re-checks range with slack - the windup is the escape
            // window, so only getting clear by more than a body length saves you.
            if (_pendingAttack != 0 && Time.time >= _windupUntil)
            {
                int pending = _pendingAttack;
                _pendingAttack = 0;
                _anim?.Strike();
                if (pending == 1)
                {
                    if (_player != null && Vector3.Distance(_player.position, pos) <= attackRange + 1.6f)
                        AttackPlayer();
                }
                else if (pending == 2 && _logisticsTarget != null) AttackLogistics();
                else if (pending == 3) AttackIndustrialSource();
            }

            Vector3 moveDir;
            float spd;
            if (chasingPlayer)
            {
                moveDir = flatToPlayer.sqrMagnitude > 0.0001f ? flatToPlayer.normalized
                                                              : Vector3.ProjectOnPlane(transform.forward, up).normalized;
                spd = distP > attackRange ? chaseSpeed : 0f;
                // The bite needs TRUE range, not the tangent-plane projection: distP ignores
                // height, so a player hovering far above must not read as in reach.
                float trueDist = Vector3.Distance(_player.position, pos);
                if (_pendingAttack == 0
                    && distP <= attackRange && trueDist <= attackRange + 0.8f
                    && Time.time >= _nextAttackAt)
                {
                    _nextAttackAt = Time.time + attackCooldown;
                    _pendingAttack = 1;
                    _windupUntil = Time.time + AttackWindupSeconds;
                    _anim?.Telegraph(EnemyRigAnimator.TelegraphKind.Windup, AttackWindupSeconds);
                }
            }
            else if (chasingIndustry)
            {
                RefreshLogisticsTarget(pos, up);
                bool strikingLogistics = _logisticsTarget != null;
                Vector3 targetPosition = strikingLogistics
                    ? _logisticsTarget.transform.position
                    : IndustrialTargetPosition;
                Vector3 flatToIndustry = Vector3.ProjectOnPlane(targetPosition - pos, up);
                float tangentDistance = flatToIndustry.magnitude;
                moveDir = flatToIndustry.sqrMagnitude > 0.0001f
                    ? flatToIndustry.normalized
                    : Vector3.ProjectOnPlane(transform.forward, up).normalized;
                spd = tangentDistance > attackRange ? chaseSpeed * 0.85f : 0f;
                float trueDistance = Vector3.Distance(targetPosition, pos);
                if (_pendingAttack == 0
                    && tangentDistance <= attackRange && trueDistance <= attackRange + 1f
                    && Time.time >= _nextAttackAt)
                {
                    _nextAttackAt = Time.time + attackCooldown;
                    _pendingAttack = strikingLogistics ? 2 : 3;
                    _windupUntil = Time.time + AttackWindupSeconds;
                    _anim?.Telegraph(EnemyRigAnimator.TelegraphKind.Windup, AttackWindupSeconds);
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
            float radius = _ambushHold ? 1.5f : wanderRadius;
            Vector2 r = UnityEngine.Random.insideUnitCircle * radius;
            _wanderTarget = _home + new Vector3(r.x, 0f, r.y);
            _nextWanderAt = Time.time + wanderPause;
        }

        private void RefreshLogisticsTarget(Vector3 position, Vector3 up)
        {
            if (!_raidLogistics)
            {
                _logisticsTarget = null;
                return;
            }
            if (_logisticsTarget != null && (_logisticsTarget.Hp <= 0
                || Vector3.Distance(_logisticsTarget.transform.position, position) > ExposedLogistics.RaidRangeMetres + 2.5f))
                _logisticsTarget = null;
            if (_logisticsTarget != null || Time.time < _nextLogisticsScan) return;
            _nextLogisticsScan = Time.time + 0.4f;
            _logisticsTarget = ExposedLogistics.FindNearest(position, up, _industrialTargetBlock);
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
            // One funnel for hostile damage: local victims take it directly, remote victims
            // get the strike on their own machine.
            VoxelEngine.Networking.HostileSync.StrikePlayer(_player, attackDamage, "Ghoul");
        }

        private void AttackIndustrialSource()
        {
            if (!HasActiveIndustrialTarget) return;
            _industrialTargetBlock.Damage(IndustrialBiteDamage(), recipient: null);
        }

        private void AttackLogistics()
        {
            if (_logisticsTarget == null || _logisticsTarget.Hp <= 0) return;
            _logisticsTarget.Damage(IndustrialBiteDamage(), recipient: null);
        }

        private int IndustrialBiteDamage()
        {
            // 17.18.0-dev: siege brutes bite structures roughly twice as hard as
            // they bite people, so a factory left running in a dirty district has
            // real repair work to do, not just a scary walk.
            float buildingScale = Variant == GhoulVariant.SiegeBrute ? SiegeBuildingScale : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(attackDamage * 0.65f * buildingScale));
        }
    }
}
