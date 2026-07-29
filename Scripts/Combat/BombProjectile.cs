// Assets/Scripts/VoxelEngine/Combat/BombProjectile.cs
//
// A thrown bomb (grenade). Launched with an initial velocity, arcs under RADIAL gravity
// (correct on spherical worlds), and detonates when its fuse runs out — dealing
// Explosive damage to every Damageable (and the player) within the radius, and
// chain-detonating any other bombs caught in the blast. Includes a self-expanding
// explosion VFX.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class BombProjectile : MonoBehaviour
    {
        public float fuse   = 1.6f;
        public float radius = 5f;
        public float damage = 80f;
        public Material explosionMaterial;

        private Rigidbody _rb;
        private float _timer;
        private bool _detonated;
        private GameObject _owner;

        public static BombProjectile Spawn(Vector3 pos, Vector3 velocity, GameObject owner, Material mat,
                                           float radius, float damage, float fuse, Material bodyMat)
        {
            var go = new GameObject("Bomb");
            go.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "BombBody";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * 0.28f;
            var bodyRen = body.GetComponent<Renderer>(); if (bodyMat != null) bodyRen.sharedMaterial = bodyMat;

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.28f;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = velocity;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var bp = go.AddComponent<BombProjectile>();
            bp._rb = rb;
            bp.radius = radius; bp.damage = damage; bp.fuse = fuse;
            bp.explosionMaterial = mat; bp._owner = owner; bp._timer = fuse;
            return bp;
        }

        private void FixedUpdate()
        {
            if (_detonated) return;
            _timer -= Time.fixedDeltaTime;
            // Integrate radial gravity so the bomb arcs correctly on spheres.
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            _rb.linearVelocity += grav * Time.fixedDeltaTime;
            if (_timer <= 0f) Detonate();
        }

        private void Detonate()
        {
            if (_detonated) return;
            _detonated = true;
            Vector3 pos = transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);

            // AoE: every Damageable in the radius takes Explosive damage. Chain-detonate other bombs.
            var cols = Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore);
            var damaged = new HashSet<IDamageable>();
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<IDamageable>();
                if (d != null && d.IsAlive) damaged.Add(d);

                var other = c.GetComponentInParent<BombProjectile>();
                if (other != null && other != this) other.Detonate();   // chain reaction
            }
            foreach (var d in damaged)
                d.TakeDamage(new DamageEvent { amount = damage, type = DamageType.Explosive,
                    point = pos, direction = up, source = _owner });

            // Self-damage risk: the player is not an IDamageable, so check directly.
            var ps = PlayerStats.Instance;
            if (ps != null && Vector3.Distance(pos, ps.transform.position) <= radius)
                ps.TakeDamage(damage);

            ExplosionVFX.Spawn(pos, explosionMaterial, radius * 0.7f, 0.35f);
            Destroy(gameObject);
        }
    }

    // Quick expanding sphere used as the explosion visual.
    public class ExplosionVFX : MonoBehaviour
    {
        private float _t, _dur, _maxRadius;

        public static void Spawn(Vector3 pos, Material mat, float maxRadius, float dur)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Explosion";
            go.transform.position = pos;
            go.transform.localScale = Vector3.zero;
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            var ren = go.GetComponent<Renderer>(); if (mat != null) ren.sharedMaterial = mat;
            var vfx = go.AddComponent<ExplosionVFX>();
            vfx._dur = Mathf.Max(0.05f, dur); vfx._maxRadius = maxRadius;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _dur);
            transform.localScale = Vector3.one * (_maxRadius * 2f * k);   // grow to diameter
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
