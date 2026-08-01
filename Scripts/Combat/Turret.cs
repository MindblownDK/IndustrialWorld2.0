// Assets/Scripts/VoxelEngine/Combat/Turret.cs
//
// A placeable automated turret. Auto-targets based on a faction filter (Enemies /
// Players / Passive), fires hitscan shots, reloadable with Bullets. The filter +
// auto-mode are configurable via the shared defense panel (same UI as Artillery / Flamethrower).

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Combat
{
    public class Turret : Damageable, IItemConsumer, IDirectItemPortEndpoint
    {
        [Header("Turret")]
        public float range        = 22f;
        public float fireCooldown = 0.4f;
        public float damage       = 20f;
        public int   maxAmmo      = 100;
        public int   ammo         = 0;
        public TargetFilter filter = TargetFilter.Enemies;
        public bool autoMode      = true;
        [Tooltip("Rotating head (yaws toward the target).")]
        public Transform head;
        [Tooltip("Barrel muzzle (tracer/flash origin).")]
        public Transform muzzle;

        private float _nextFire, _retargetAt;
        private Transform _target;
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
            if (!autoMode || ammo <= 0) return;

            if (Time.time >= _retargetAt || _target == null || !InRange(_target))
            {
                _retargetAt = Time.time + 0.35f;
                _target = FindTarget();
            }
            if (_target == null) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            if (head != null)
            {
                Vector3 to = _target.position - head.position;
                Quaternion look = Quaternion.LookRotation(Vector3.ProjectOnPlane(to, up).normalized, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, 0.2f);
            }

            if (Time.time >= _nextFire)
            {
                _nextFire = Time.time + fireCooldown;
                Fire(_target.position + up * 0.6f);
            }
        }

        private bool InRange(Transform t) => t != null && (t.position - transform.position).sqrMagnitude <= range * range;

        private Transform FindTarget()
        {
            Transform best = null;
            float bestSqr = range * range;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 from = (muzzle != null ? muzzle.position : transform.position) + up * 0.2f;

            var cols = Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Damageable>();
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<Damageable>();
                if (d == null || !d.IsAlive || !seen.Add(d)) continue;
                TargetFilter ff = d is VoxelEngine.Fauna.PassiveAnimal ? TargetFilter.Passive
                                : (d.GetType().Name.StartsWith("Enemy") ? TargetFilter.Enemies : TargetFilter.None);
                if ((filter & ff) == TargetFilter.None) continue;
                Vector3 to = d.transform.position + up * 0.5f;
                Vector3 dir = to - from;
                if (Physics.Raycast(from, dir.normalized, out var hit, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.GetComponentInParent<Damageable>() != d) continue;
                float sqr = (d.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = d.transform; }
            }

            if ((filter & TargetFilter.Players) != 0)
            {
                var ps = VoxelEngine.Player.PlayerStats.Instance;
                if (ps != null && ps.Health > 0)
                {
                    float s = (ps.transform.position - transform.position).sqrMagnitude;
                    Vector3 to = ps.transform.position + up * 0.5f;
                    Vector3 dir = to - from;
                    if (s <= range * range && s < bestSqr &&
                        (!Physics.Raycast(from, dir.normalized, out var hit2, dir.magnitude, ~0, QueryTriggerInteraction.Ignore)
                         || hit2.collider.GetComponentInParent<VoxelEngine.Player.PlayerStats>() == ps))
                    {
                        best = ps.transform;
                    }
                }
            }
            return best;
        }

        private void Fire(Vector3 aimPoint)
        {
            ammo--;
            if (ammo <= 0) DefenseStatus.NotifyEmpty("Auto Turret");
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            Vector3 dir = (aimPoint - origin).normalized;
            Vector3 end;
            if (Physics.Raycast(origin, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var d = hit.collider.GetComponentInParent<Damageable>();
                if (d != null && d.IsAlive)
                    d.TakeDamage(new DamageEvent { amount = damage, type = DamageType.Kinetic, point = hit.point, direction = dir, source = gameObject });
                var ps = hit.collider.GetComponentInParent<VoxelEngine.Player.PlayerStats>();
                if (ps != null) ps.TakeDamage(damage);
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
    
        // ── Factory logistics (bullets → integer magazine counter) ──
        public int GetInputCapacity(ItemDefinition item)
            => DefenseLogistics.GetBulletCapacity(ammo, maxAmmo, item);

        public int TryInsert(ItemDefinition item, int count)
            => DefenseLogistics.InsertBullets(ref ammo, maxAmmo, item, count);

        public bool IsFaceConnectable(Vector3 fromWorldPos) => true;
        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
            => TryInsert(item, count);

}
}
