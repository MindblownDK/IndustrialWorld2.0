// Assets/Scripts/VoxelEngine/Combat/BombProjectile.cs
//
// A thrown bomb (grenade). Launched with an initial velocity, arcs under RADIAL gravity
// (correct on spherical worlds), and detonates when its fuse runs out. Detonation is
// delegated to the centralized Explosion helper: AoE damage to creatures/player/blocks,
// a voxel-terrain crater, distance-based camera shake, and a multi-layer VFX. Bombs
// caught in a blast chain-detonate.

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class BombProjectile : MonoBehaviour
    {
        public float fuse   = 1.6f;
        public float radius = 5f;
        public float damage = 80f;
        public float voxelDamageRadius = 2.5f;
        public Material explosionMaterial;

        private Rigidbody _rb;
        private float _timer;
        private bool _detonated;
        private GameObject _owner;

        public static BombProjectile Spawn(Vector3 pos, Vector3 velocity, GameObject owner, Material mat,
                                           float radius, float damage, float fuse, float voxelDamageRadius, Material bodyMat)
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
            bp.radius = radius; bp.damage = damage; bp.fuse = fuse; bp.voxelDamageRadius = voxelDamageRadius;
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

            // Chain-detonate any other bombs caught in the blast first.
            var cols = Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
            {
                var other = c.GetComponentInParent<BombProjectile>();
                if (other != null && other != this) other.Detonate();
            }

            // Full explosion (damage + voxel crater + camera shake + VFX).
            Explosion.Detonate(pos, radius, damage, _owner, voxelDamageRadius, explosionMaterial);
            Destroy(gameObject);
        }
    }
}
