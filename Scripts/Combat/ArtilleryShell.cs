// Assets/Scripts/VoxelEngine/Combat/ArtilleryShell.cs
//
// A heavy artillery shell (Cannon / Gustav). Launched with an initial velocity, arcs
// under radial gravity, and detonates on impact via the centralized Explosion (the
// Gustav shell carries a colossal blast radius). Clean raycast collision on spheres.

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class ArtilleryShell : MonoBehaviour
    {
        private Rigidbody _rb;
        private GameObject _owner;
        private float _explosionRadius, _damage;
        private Material _explosionMat;
        private bool _detonated;
        private float _life;

        public static ArtilleryShell Spawn(Vector3 pos, Vector3 velocity, GameObject owner, Material bodyMat,
                                           float explosionRadius, float damage, Material explosionMat)
        {
            var go = new GameObject("ArtilleryShell");
            go.transform.position = pos;
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Shell";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * 0.3f;
            var ren = body.GetComponent<Renderer>(); if (bodyMat != null) ren.sharedMaterial = bodyMat;
            var col = go.AddComponent<SphereCollider>(); col.radius = 0.3f;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = velocity;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var shell = go.AddComponent<ArtilleryShell>();
            shell._rb = rb; shell._owner = owner;
            shell._explosionRadius = explosionRadius; shell._damage = damage; shell._explosionMat = explosionMat;
            return shell;
        }

        private void FixedUpdate()
        {
            if (_detonated) return;
            _life += Time.fixedDeltaTime;
            if (_life > 12f) { Destroy(gameObject); return; }

            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            _rb.linearVelocity += grav * Time.fixedDeltaTime;

            // Raycast the frame's motion so fast shells don't tunnel.
            Vector3 step = _rb.linearVelocity * Time.fixedDeltaTime;
            float dist = step.magnitude;
            if (dist > 0.0001f && Physics.Raycast(transform.position, step, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (_owner == null || !hit.collider.transform.IsChildOf(_owner.transform))
                    Detonate(hit.point);
            }
        }

        private void Detonate(Vector3 at)
        {
            if (_detonated) return;
            _detonated = true;
            Explosion.Detonate(at, _explosionRadius, _damage, _owner, 0f, _explosionMat);
            Destroy(gameObject);
        }
    }
}
