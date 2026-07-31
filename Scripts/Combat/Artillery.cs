// Assets/Scripts/VoxelEngine/Combat/Artillery.cs
//
// Heavy artillery — Minigun, Cannon, and Schwerer Gustav. Auto-targets based on a
// faction filter (Enemies / Players / Passive, any combination), aims its head, and
// fires: Minigun = rapid hitscan + tracer; Cannon/Gustav = arcing shells that detonate
// (Gustav = colossal blast). Reloadable with Bullets (RMB). Placeable; Damageable.
// Manual cockpit control (first/third person) comes in a follow-up pass.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Combat
{
    [System.Flags]
    public enum TargetFilter { None = 0, Enemies = 1, Players = 2, Passive = 4 }

    public enum ArtilleryVariant { Minigun, Cannon, Gustav }

    public enum ShellType { Standard, Explosive, Scatter }

    public class Artillery : Damageable
    {
        [Header("Variant")]
        public ArtilleryVariant variant = ArtilleryVariant.Cannon;
        public TargetFilter filter = TargetFilter.Enemies;
        public bool autoMode = true;

        [Header("Combat")]
        public float range = 70f;
        public float fireCooldown = 2.5f;
        public float damage = 60f;             // shell/explosive damage (minigun overrides via minigunDamage)
        public float minigunDamage = 6f;
        public float explosionRadius = 8f;     // cannon/gustav shell blast (0 for minigun)
        public float shellSpeed = 35f;
        public ShellType shellType = ShellType.Standard;
        public int maxAmmo = 30;
        public int ammo = 0;
        public Material shellMat;
        public Material explosionMat;

        [Header("Turret")]
        public Transform head;    // rotates to aim (yaw + pitch)
        public Transform muzzle;  // shell/tracer origin
        public float aimSpeed = 3f;

        private float _nextFire, _retargetAt;
        private Transform _targetT;
        private Damageable _targetD;
        private VoxelEngine.Player.PlayerStats _targetP;
        private static Material _fxMat;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 200f);
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
            if (!autoMode) return;
            if (ammo <= 0) return;

            if (Time.time >= _retargetAt || !Valid(_targetT))
            {
                _retargetAt = Time.time + 0.4f;
                FindTarget();
            }
            if (_targetT == null) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            if (head != null)
            {
                Vector3 to = _targetT.position - head.position;
                Quaternion look = Quaternion.LookRotation(to.normalized, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, aimSpeed * Time.deltaTime);
            }

            if (Time.time >= _nextFire)
            {
                _nextFire = Time.time + fireCooldown;
                Fire(_targetT.position + up * 0.6f);
            }
        }

        private bool Valid(Transform t) => t != null && (t.position - transform.position).sqrMagnitude <= range * range;

        private void FindTarget()
        {
            _targetT = null; _targetD = null; _targetP = null;
            float bestSqr = range * range;
            Vector3 from = (muzzle != null ? muzzle.position : transform.position);

            var cols = Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Damageable>();
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<Damageable>();
                if (d == null || !d.IsAlive || !seen.Add(d)) continue;
                TargetFilter ff = d is VoxelEngine.Fauna.PassiveAnimal ? TargetFilter.Passive
                                : (d.GetType().Name.StartsWith("Enemy") ? TargetFilter.Enemies : TargetFilter.None);
                if ((filter & ff) == TargetFilter.None) continue;
                Vector3 tp = d.transform.position + VoxelEngine.Cosmos.GravityProvider.GetUp(d.transform.position) * 0.5f;
                if (!HasLOS(from, tp, d)) continue;
                float s = (d.transform.position - transform.position).sqrMagnitude;
                if (s < bestSqr) { bestSqr = s; _targetT = d.transform; _targetD = d; _targetP = null; }
            }

            if ((filter & TargetFilter.Players) != 0)
            {
                var ps = VoxelEngine.Player.PlayerStats.Instance;
                if (ps != null && ps.Health > 0)
                {
                    float s = (ps.transform.position - transform.position).sqrMagnitude;
                    Vector3 tp = ps.transform.position + VoxelEngine.Cosmos.GravityProvider.GetUp(ps.transform.position) * 0.5f;
                    if (s <= range * range && s < bestSqr && HasLOS(from, tp, ps))
                    {
                        _targetT = ps.transform; _targetD = null; _targetP = ps;
                    }
                }
            }
        }

        private bool HasLOS(Vector3 from, Vector3 to, Component ignore)
        {
            Vector3 dir = to - from;
            float mag = dir.magnitude;
            if (mag < 0.01f) return true;
            if (Physics.Raycast(from, dir.normalized, out var hit, mag, ~0, QueryTriggerInteraction.Ignore))
            {
                // Hit is OK if it's the target itself (or part of it).
                if (ignore != null && hit.collider.GetComponentInParent(ignore.GetType()) != null) return true;
                return false; // something else blocks
            }
            return true;
        }

        private void Fire(Vector3 aimPoint)
        {
            ammo--;
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);

            if (variant == ArtilleryVariant.Minigun)
            {
                Vector3 dir = (aimPoint - origin).normalized;
                Vector3 end;
                if (Physics.Raycast(origin, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    ApplyHit(hit.collider, minigunDamage, hit.point, dir);
                }
                else end = origin + dir * range;
                Tracer(origin, end);
                Flash(origin);
            }
            else
            {
                Vector3 dir = (aimPoint - origin).normalized;
                ArtilleryShell.Spawn(origin, dir * shellSpeed, gameObject, shellMat, explosionRadius, damage, explosionMat, shellType);
                Flash(origin);
            }
        }

        private void ApplyHit(Collider col, float dmg, Vector3 point, Vector3 dir)
        {
            var d = col.GetComponentInParent<Damageable>();
            if (d != null && d.IsAlive)
                d.TakeDamage(new DamageEvent { amount = dmg, type = DamageType.Kinetic, point = point, direction = dir, source = gameObject });
            var ps = col.GetComponentInParent<VoxelEngine.Player.PlayerStats>();
            if (ps != null) ps.TakeDamage(dmg);
        }

        public int Load(int count) { int add = Mathf.Min(count, maxAmmo - ammo); ammo += add; return add; }

        private static void Flash(Vector3 pos)
        {
            var go = new GameObject("ArtilleryFlash"); go.transform.position = pos;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(go.transform, false); s.transform.localScale = Vector3.one * 0.18f;
            s.GetComponent<Renderer>().sharedMaterial = FxMat;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.35f); l.range = 7f; l.intensity = 6f;
            Object.Destroy(go, 0.07f);
        }

        private static void Tracer(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from; float len = dir.magnitude;
            if (len < 0.01f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = new Vector3(0.05f, 0.05f, len);
            go.GetComponent<Renderer>().sharedMaterial = FxMat;
            Object.Destroy(go, 0.06f);
        }
    }
}
