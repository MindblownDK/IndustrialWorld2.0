// Assets/Scripts/VoxelEngine/Combat/MortarShell.cs
//
// Arcing mortar round under radial gravity. Three payloads:
//   Explosive   — blast via centralized Explosion (+ small voxel crater)
//   Smoke       — lingering smoke cloud (visual cover + light burn-free zone marker)
//   Illumination— bright falling flare that lights the battlefield

using UnityEngine;

namespace VoxelEngine.Combat
{
    [RequireComponent(typeof(Rigidbody))]
    public class MortarShell : MonoBehaviour
    {
        private Rigidbody _rb;
        private GameObject _owner;
        private float _explosionRadius, _damage;
        private Material _explosionMat, _bodyMat, _smokeMat, _flareMat;
        private MortarShellType _shellType;
        private bool _detonated;
        private float _life, _maxLife = 14f;

        public static MortarShell Spawn(Vector3 pos, Vector3 velocity, GameObject owner, Material bodyMat,
                                       float explosionRadius, float damage, Material explosionMat,
                                       Material smokeMat, Material flareMat, MortarShellType shellType)
        {
            var go = new GameObject("MortarShell");
            go.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Shell";
            body.transform.SetParent(go.transform, false);
            float sz = shellType == MortarShellType.Illumination ? 0.22f : 0.28f;
            body.transform.localScale = Vector3.one * sz;
            var ren = body.GetComponent<Renderer>();
            if (bodyMat != null) ren.sharedMaterial = bodyMat;

            var col = go.AddComponent<SphereCollider>();
            col.radius = sz;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = velocity;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var shell = go.AddComponent<MortarShell>();
            shell._rb = rb;
            shell._owner = owner;
            shell._explosionRadius = explosionRadius;
            shell._damage = damage;
            shell._explosionMat = explosionMat;
            shell._bodyMat = bodyMat;
            shell._smokeMat = smokeMat;
            shell._flareMat = flareMat;
            shell._shellType = shellType;
            return shell;
        }

        private void FixedUpdate()
        {
            if (_detonated) return;
            _life += Time.fixedDeltaTime;
            if (_life > _maxLife) { Detonate(transform.position); return; }

            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            _rb.linearVelocity += grav * Time.fixedDeltaTime;

            // Keep the shell pointed along its velocity for a readable arc.
            if (_rb.linearVelocity.sqrMagnitude > 0.05f)
                transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized,
                    VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position));

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

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(at);

            switch (_shellType)
            {
                case MortarShellType.Smoke:
                    // Soft pop + lingering smoke cloud.
                    Explosion.Detonate(at, _explosionRadius * 0.25f, _damage * 0.15f, _owner, 0f, _explosionMat);
                    MortarSmokeCloud.Spawn(at, up, _smokeMat != null ? _smokeMat : _bodyMat, 10f, 4.5f);
                    break;

                case MortarShellType.Illumination:
                    // Tiny pop + bright falling flare.
                    Explosion.Detonate(at, 1.2f, _damage * 0.1f, _owner, 0f, _explosionMat);
                    MortarFlare.Spawn(at + up * 1.5f, up, _flareMat != null ? _flareMat : _bodyMat, 14f);
                    break;

                default:
                case MortarShellType.Explosive:
                    Explosion.Detonate(at, _explosionRadius, _damage, _owner,
                        _explosionRadius * 0.35f, _explosionMat);
                    break;
            }

            Destroy(gameObject);
        }
    }

    /// <summary>Lingering smoke disc — pure visual cover marker (no damage).</summary>
    public class MortarSmokeCloud : MonoBehaviour
    {
        private float _life, _duration, _radius;
        private Transform _disk;

        public static MortarSmokeCloud Spawn(Vector3 pos, Vector3 up, Material mat, float duration, float radius)
        {
            var go = new GameObject("MortarSmoke");
            go.transform.position = pos + up * 0.4f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disk.name = "Cloud";
            disk.transform.SetParent(go.transform, false);
            disk.transform.localScale = new Vector3(radius, 0.6f, radius);
            var col = disk.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            var ren = disk.GetComponent<Renderer>();
            if (mat != null) ren.sharedMaterial = mat;

            // Soft ambient light so the cloud reads at night.
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.55f, 0.55f, 0.5f);
            l.range = radius * 1.6f;
            l.intensity = 0.6f;

            var cloud = go.AddComponent<MortarSmokeCloud>();
            cloud._duration = duration;
            cloud._radius = radius;
            cloud._disk = disk.transform;
            return cloud;
        }

        private void Update()
        {
            _life += Time.deltaTime;
            float t = Mathf.Clamp01(_life / Mathf.Max(0.01f, _duration));
            // Billow out then fade.
            float scale = Mathf.Lerp(0.7f, 1.35f, t);
            if (_disk != null)
                _disk.localScale = new Vector3(_radius * scale, 0.55f * (1f - t * 0.4f), _radius * scale);

            var l = GetComponent<Light>();
            if (l != null) l.intensity = Mathf.Lerp(0.6f, 0f, t);

            if (_life >= _duration) Destroy(gameObject);
        }
    }

    /// <summary>Bright illumination flare that slowly descends under radial gravity.</summary>
    public class MortarFlare : MonoBehaviour
    {
        private float _life, _duration;
        private Vector3 _vel;
        private Light _light;

        public static MortarFlare Spawn(Vector3 pos, Vector3 up, Material mat, float duration)
        {
            var go = new GameObject("MortarFlare");
            go.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Flare";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * 0.35f;
            var col = body.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            var ren = body.GetComponent<Renderer>();
            if (mat != null) ren.sharedMaterial = mat;

            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.92f, 0.7f);
            l.range = 28f;
            l.intensity = 8f;

            var flare = go.AddComponent<MortarFlare>();
            flare._duration = duration;
            flare._light = l;
            // Slow parachute descent along gravity.
            flare._vel = up * 1.5f;
            return flare;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            // Heavily damped fall so the flare hangs in the air.
            _vel += grav * (0.12f * dt);
            _vel *= (1f - 0.4f * dt);
            transform.position += _vel * dt;

            if (_light != null)
            {
                // Gentle flicker.
                _light.intensity = 7.5f + Mathf.Sin(Time.time * 18f) * 1.2f;
                float t = Mathf.Clamp01(_life / Mathf.Max(0.01f, _duration));
                _light.intensity *= (1f - t * 0.85f);
                _light.range = Mathf.Lerp(28f, 10f, t);
            }

            if (_life >= _duration) Destroy(gameObject);
        }
    }
}
