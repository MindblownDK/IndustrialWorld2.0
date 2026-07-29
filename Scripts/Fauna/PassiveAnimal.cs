// Assets/Scripts/VoxelEngine/Fauna/PassiveAnimal.cs
//
// Passive livestock (Combat Phase 3c). Peaceful quadrupeds that graze and wander
// across the spherical surface and bolt away when hurt, dropping animal products
// (meat / hide / wool) on death. Shares the Damageable health + loot contract so
// player weapons can harvest them. Radial-gravity aligned for spherical worlds and
// detaches from the chunk-scatter parent on Awake so Rigidbody physics is correct on
// rotating planets. Designed as a clean base a future RideableAnimal can extend.

using UnityEngine;
using VoxelEngine.Combat;

namespace VoxelEngine.Fauna
{
    public enum AnimalSpecies { Cow, Sheep, Pig, Horse }

    [RequireComponent(typeof(Rigidbody))]
    public class PassiveAnimal : Damageable
    {
        [Header("Species")]
        public AnimalSpecies species = AnimalSpecies.Cow;

        [Header("Wander / Flee AI")]
        public float wanderSpeed    = 1.1f;
        public float fleeSpeed      = 5.5f;
        public float accel          = 10f;
        public float wanderRadius   = 8f;
        public float wanderPauseMin = 2f;
        public float wanderPauseMax = 5f;
        [Tooltip("How long the animal keeps running after being hit.")]
        public float fleeDuration   = 4f;

        protected Rigidbody _rb;
        private Vector3 _home;
        private Vector3 _wanderTarget;
        private float _nextWanderAt;
        private float _fleeUntil;

        protected override void Awake()
        {
            base.Awake();

            // Detach from any chunk-scatter parent (it moves with the rotating planet, but
            // Rigidbody physics is world-space — a chunk-parented body gets flung off the
            // sphere). Root-level = correct physics on spherical worlds. Mirrors EnemyGhoul.
            if (transform.parent != null) transform.SetParent(null, true);

            _home = transform.position;
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.freezeRotation = true;
            PickWander();
        }

        // Spook + run from the damage source instead of fighting back.
        public override void TakeDamage(DamageEvent e)
        {
            base.TakeDamage(e);
            if (!IsAlive) return;

            _fleeUntil = Time.time + fleeDuration;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 fromPos = (e.source != null) ? e.source.transform.position
                                                 : transform.position - e.direction;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - fromPos, up);
            if (away.sqrMagnitude < 0.0001f) away = Vector3.ProjectOnPlane(Random.insideUnitSphere, up);
            // Bolt past the normal wander range so the animal visibly flees.
            _wanderTarget = transform.position + away.normalized * wanderRadius * 1.5f;
        }

        protected virtual void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            Vector3 up   = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(pos);

            bool fleeing = Time.time < _fleeUntil;

            Vector3 moveDir;
            float spd;
            if (fleeing)
            {
                Vector3 flatF = Vector3.ProjectOnPlane(_wanderTarget - pos, up);
                moveDir = flatF.sqrMagnitude > 0.0001f
                    ? flatF.normalized
                    : Vector3.ProjectOnPlane(transform.forward, up).normalized;
                spd = fleeSpeed;
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

            // Stand on the surface + face the travel direction.
            Vector3 face = (spd > 0.01f && moveDir != Vector3.zero)
                ? moveDir
                : Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (face.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(face, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, fleeing ? 0.20f : 0.10f));
            }
        }

        private void PickWander()
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
            _wanderTarget = _home + new Vector3(r.x, 0f, r.y);
            _nextWanderAt = Time.time + Random.Range(wanderPauseMin, wanderPauseMax);
        }
    }
}
