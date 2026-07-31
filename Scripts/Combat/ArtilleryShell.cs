// Assets/Scripts/VoxelEngine/Combat/ArtilleryShell.cs
//
// A heavy artillery shell (Cannon / Gustav). Arcs under radial gravity, detonates on
// impact. Three shell types: Standard (direct blast), Explosive (bigger blast + voxel
// crater), Scatter (cluster — bursts into sub-munitions that each detonate).

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class ArtilleryShell : MonoBehaviour
    {
        private Rigidbody _rb;
        private GameObject _owner;
        private float _explosionRadius, _damage;
        private Material _explosionMat, _bodyMat;
        private ShellType _shellType;
        private bool _detonated;
        private float _life, _maxLife;

        public static ArtilleryShell Spawn(Vector3 pos, Vector3 velocity, GameObject owner, Material bodyMat,
                                           float explosionRadius, float damage, Material explosionMat,
                                           ShellType shellType, float maxLife = 12f)
        {
            var go = new GameObject("ArtilleryShell");
            go.transform.position = pos;
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Shell";
            body.transform.SetParent(go.transform, false);
            float sz = shellType == ShellType.Scatter ? 0.15f : 0.3f; // sub-munitions smaller
            body.transform.localScale = Vector3.one * sz;
            var ren = body.GetComponent<Renderer>(); if (bodyMat != null) ren.sharedMaterial = bodyMat;
            var col = go.AddComponent<SphereCollider>(); col.radius = sz;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = velocity;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var shell = go.AddComponent<ArtilleryShell>();
            shell._rb = rb; shell._owner = owner;
            shell._explosionRadius = explosionRadius; shell._damage = damage;
            shell._explosionMat = explosionMat; shell._bodyMat = bodyMat;
            shell._shellType = shellType; shell._maxLife = maxLife;
            return shell;
        }

        private void FixedUpdate()
        {
            if (_detonated) return;
            _life += Time.fixedDeltaTime;
            if (_life > _maxLife) { Detonate(transform.position); return; }

            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            _rb.linearVelocity += grav * Time.fixedDeltaTime;

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

            switch (_shellType)
            {
                default:
                case ShellType.Standard:
                    Explosion.Detonate(at, _explosionRadius, _damage, _owner, 0f, _explosionMat);
                    break;

                case ShellType.Explosive:
                    // Bigger blast + voxel crater.
                    Explosion.Detonate(at, _explosionRadius * 1.5f, _damage * 1.5f, _owner,
                                       _explosionRadius * 0.5f, _explosionMat);
                    break;

                case ShellType.Scatter:
                    // Cluster burst: spawn sub-munitions that spread + each detonate.
                    Explosion.Detonate(at, _explosionRadius * 0.4f, _damage * 0.3f, _owner, 0f, _explosionMat);
                    int sub = 8;
                    for (int i = 0; i < sub; i++)
                    {
                        Vector3 spread = Random.onUnitSphere * Random.Range(4f, 9f);
                        ArtilleryShell.Spawn(at + spread * 0.2f, spread, _owner, _bodyMat,
                            _explosionRadius * 0.35f, _damage * 0.35f, _explosionMat,
                            ShellType.Standard, 0.6f);
                    }
                    break;
            }

            Destroy(gameObject);
        }
    }
}
