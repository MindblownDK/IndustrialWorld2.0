// Assets/Scripts/VoxelEngine/Combat/Turret.cs
//
// A placeable automated turret. Scans for hostile creatures (any Damageable whose type
// name starts with "Enemy") within range and line of sight, rotates its head to track the
// nearest one, and fires hitscan shots (with a tracer + muzzle flash). Runs on an internal
// ammo magazine reloaded by the player (RMB the turret while holding Bullets). Radial-aware
// (aims in the tangent plane of the sphere). Extends Damageable so it can be destroyed.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Combat
{
    public class Turret : Damageable
    {
        [Header("Turret")]
        public float range        = 22f;
        public float fireCooldown = 0.4f;
        public float damage       = 20f;
        public int   maxAmmo      = 100;
        public int   ammo         = 0;
        [Tooltip("Rotating head (yaws toward the target).")]
        public Transform head;
        [Tooltip("Barrel muzzle (tracer/flash origin).")]
        public Transform muzzle;

        private float _nextFire, _retargetAt;
        private Damageable _target;
        private static Material _fxMat;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 80f);
            base.Awake();
        }

        private static Material FxMat
        {
            get
            {
                if (_fxMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                    _fxMat = new Material(sh) { color = new Color(1f, 0.85f, 0.35f) };
                    if (_fxMat.HasProperty("_BaseColor")) _fxMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.35f));
                }
                return _fxMat;
            }
        }

        private void Update()
        {
            // Re-target periodically or if the current target is gone/out of range.
            if (Time.time >= _retargetAt || _target == null || !_target.IsAlive || !InRange(_target))
            {
                _retargetAt = Time.time + 0.35f;
                _target = FindTarget();
            }

            if (_target == null) return;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);

            if (head != null)
            {
                Vector3 to = _target.transform.position - head.position;
                Quaternion look = Quaternion.LookRotation(Vector3.ProjectOnPlane(to, up).normalized, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, 0.2f);
            }

            if (ammo > 0 && Time.time >= _nextFire)
            {
                _nextFire = Time.time + fireCooldown;
                Fire(_target.transform.position + up * 0.6f, up);
            }
        }

        private bool InRange(Damageable d) => d != null && (d.transform.position - transform.position).sqrMagnitude <= range * range;

        private static bool IsHostile(Damageable d) => d != null && d.GetType().Name.StartsWith("Enemy");

        private Damageable FindTarget()
        {
            Damageable best = null;
            float bestSqr = range * range;
            var cols = Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Damageable>();
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 from = (muzzle != null ? muzzle.position : transform.position) + up * 0.2f;
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<Damageable>();
                if (d == null || !d.IsAlive || !seen.Add(d) || !IsHostile(d)) continue;
                Vector3 to = d.transform.position + up * 0.5f;
                Vector3 dir = to - from;
                // Line of sight: skip if terrain blocks the shot.
                if (Physics.Raycast(from, dir.normalized, out var hit, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.GetComponentInParent<Damageable>() != d) continue;
                float sqr = (d.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = d; }
            }
            return best;
        }

        private void Fire(Vector3 aimPoint, Vector3 up)
        {
            ammo--;
            Vector3 origin = muzzle != null ? muzzle.position : transform.position + up * 1.2f;
            Vector3 dir = (aimPoint - origin).normalized;
            Vector3 end;
            if (Physics.Raycast(origin, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var d = hit.collider.GetComponentInParent<Damageable>();
                if (d != null && d.IsAlive)
                    d.TakeDamage(new DamageEvent { amount = damage, type = DamageType.Kinetic, point = hit.point, direction = dir, source = gameObject });
            }
            else end = origin + dir * range;

            Tracer(origin, end);
            Flash(origin);
        }

        private static void Flash(Vector3 pos)
        {
            var go = new GameObject("TurretFlash");
            go.transform.position = pos;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(go.transform, false);
            s.transform.localScale = Vector3.one * 0.12f;
            s.GetComponent<Renderer>().sharedMaterial = FxMat;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.35f); l.range = 5f; l.intensity = 4f;
            Object.Destroy(go, 0.06f);
        }

        private static void Tracer(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = new Vector3(0.04f, 0.04f, len);
            go.GetComponent<Renderer>().sharedMaterial = FxMat;
            Object.Destroy(go, 0.08f);
        }
    }
}
