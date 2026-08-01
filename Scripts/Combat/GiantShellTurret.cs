// Assets/Scripts/VoxelEngine/Combat/GiantShellTurret.cs
//
// Placeable heavy siege turret. Tracks slowly, demands line of sight, and fires
// factory-built Giant Shells one at a time. Prefers high-HP targets (bosses /
// elites) when several enemies are in range. Load shells via the defense panel.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Player;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Combat
{
    public class GiantShellTurret : Damageable, IItemConsumer, IDirectItemPortEndpoint, IInventoryInterface, IDefenseFirePolicy, IDefenseEngagement
    {
        [Header("Combat")]
        public float range = 90f;
        public float fireCooldown = 8f;
        public float damage = 280f;
        public float explosionRadius = 14f;
        public float shellSpeed = 42f;
        public float aimConeDegrees = 8f;   // must be roughly on-target before firing
        public Material shellMat;
        public Material explosionMat;

        [Header("Turret")]
        public Transform head;
        public Transform muzzle;
        public float aimSpeed = 1.4f;
        public TargetFilter filter = TargetFilter.Enemies;
        public bool autoMode = true;

        
        [Header("Ammo Policy")]
        [Tooltip("When enabled, auto-fire stops once stock reaches the reserve.")]
        public bool conserveAmmo = false;
        [Tooltip("Units kept in reserve while Conserve Ammo is on (magazine count / bullets).")]
        public int reserveStock = 0;

        public bool ConserveAmmo { get => conserveAmmo; set => conserveAmmo = value; }
        public int ReserveStock { get => DefenseFirePolicy.ClampReserve(reserveStock); set => reserveStock = DefenseFirePolicy.ClampReserve(value); }
        public int CurrentStock => DefenseStatus.CountMagazine(ShellMagazine);


        [Header("Engagement")]
        [Tooltip("Max distance auto-fire will engage. Capped by the weapon's physical range.")]
        public float engagementRange = -1f;
        [Tooltip("Horizontal firing arc in degrees (360 = all around). Centred on placed facing.")]
        public float firingArcDegrees = 360f;

        public float MaxRange => range;
        public float EngagementRange
        {
            get => engagementRange < 0f ? range : DefenseEngagement.ClampRange(engagementRange, range);
            set => engagementRange = DefenseEngagement.ClampRange(value, range);
        }
        public float FiringArcDegrees
        {
            get => DefenseEngagement.ClampArc(firingArcDegrees <= 0f ? 360f : firingArcDegrees);
            set => firingArcDegrees = DefenseEngagement.ClampArc(value);
        }

[SerializeField] private ItemContainer _shellMag;

        public ItemContainer ShellMagazine
        {
            get
            {
                if (_shellMag == null)
                {
                    _shellMag = new ItemContainer("Giant Shells", 2);
                    _shellMag.AcceptFilter = (item, wanted) =>
                        item != null && item.itemId != null &&
                        (item.itemId == "item_giant_shell" || item.itemId.StartsWith("item_giant_shell"))
                            ? Mathf.Min(wanted, item.maxStack) : 0;
                }
                return _shellMag;
            }
        }

        private float _nextFire, _retargetAt;
        private Transform _targetT;
        private Damageable _targetD;
        private PlayerStats _targetP;
        private static Material _fxMat;

        protected override void Awake()
        {
            if (engagementRange < 0f) engagementRange = range;

            maxHealth = Mathf.Max(maxHealth, 280f);
            base.Awake();
            _ = ShellMagazine;
        }

        private static Material FxMat
        {
            get
            {
                if (_fxMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("Sprites/Default");
                    _fxMat = new Material(sh) { color = new Color(1f, 0.7f, 0.25f) };
                    if (_fxMat.HasProperty("_BaseColor"))
                        _fxMat.SetColor("_BaseColor", new Color(1f, 0.7f, 0.25f));
                }
                return _fxMat;
            }
        }

        private void Update()
        {
            if (!autoMode) return;
            if (!DefenseFirePolicy.CanAutoSpend(this)) return;
            if (!HasAmmo()) return;

            if (Time.time >= _retargetAt || !Valid(_targetT))
            {
                _retargetAt = Time.time + 0.5f;
                FindTarget();
            }
            if (_targetT == null) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 origin = muzzle != null ? muzzle.position : (head != null ? head.position : transform.position);
            Vector3 aimPoint = _targetT.position + up * 0.8f;
            Vector3 to = aimPoint - origin;

            if (head != null && to.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, aimSpeed * Time.deltaTime);
            }

            // Only fire when the barrel is roughly aligned.
            Vector3 forward = head != null ? head.forward : to.normalized;
            if (Vector3.Angle(forward, to) > aimConeDegrees) return;

            if (Time.time >= _nextFire)
            {
                _nextFire = Time.time + fireCooldown;
                Fire(aimPoint, forward);
            }
        }

        private bool HasAmmo()
        {
            for (int i = 0; i < ShellMagazine.Slots.Count; i++)
            {
                var s = ShellMagazine.GetSlot(i);
                if (s != null && !s.IsEmpty) return true;
            }
            return false;
        }

        private bool ConsumeShell()
        {
            for (int i = 0; i < ShellMagazine.Slots.Count; i++)
            {
                var s = ShellMagazine.GetSlot(i);
                if (s == null || s.IsEmpty) continue;
                s.count--;
                ShellMagazine.SetSlot(i, s.count <= 0 ? new ItemStack() : s);
                if (!HasAmmo()) DefenseStatus.NotifyEmpty("Giant Shell Turret");
                return true;
            }
            return false;
        }

        private bool Valid(Transform t)
        {
            if (t == null) return false;
            if (!DefenseEngagement.IsInEngagement(this, t.position)) return false;
            Vector3 from = muzzle != null ? muzzle.position : transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(t.position);
            Vector3 to = t.position + up * 0.5f;
            return HasLOS(from, to, t);
        }

        private void FindTarget()
        {
            _targetT = null; _targetD = null; _targetP = null;
            float bestScore = float.MaxValue;
            Vector3 from = muzzle != null ? muzzle.position : transform.position;

            var cols = Physics.OverlapSphere(transform.position, EngagementRange, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Damageable>();
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<Damageable>();
                if (d == null || d == (Damageable)this || !d.IsAlive || !seen.Add(d)) continue;
                TargetFilter ff = d is VoxelEngine.Fauna.PassiveAnimal ? TargetFilter.Passive
                                : (d.GetType().Name.StartsWith("Enemy") ? TargetFilter.Enemies : TargetFilter.None);
                if ((filter & ff) == TargetFilter.None) continue;

                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(d.transform.position);
                Vector3 tp = d.transform.position + up * 0.5f;
                if (!HasLOS(from, tp, d)) continue;

                float dist = (d.transform.position - transform.position).magnitude;
                // Prefer high-HP targets (bosses / elites) — lower score is better.
                // Heavy HP outweighs distance so a Roc at 40 m beats a Ghoul at 10 m.
                float hpWeight = 1f / Mathf.Max(1f, d.maxHealth);
                float score = dist * 0.15f + hpWeight * 400f;
                // Slight bonus for names that look like bosses.
                string n = d.GetType().Name;
                if (n.Contains("Roc") || n.Contains("Boss") || n.Contains("Matriarch") ||
                    n.Contains("Sultan") || n.Contains("Queen") || n.Contains("Sovereign") ||
                    n.Contains("Leviathan") || n.Contains("Pontiff") || n.Contains("Warden"))
                    score -= 30f;

                if (score < bestScore)
                {
                    bestScore = score;
                    _targetT = d.transform; _targetD = d; _targetP = null;
                }
            }

            if ((filter & TargetFilter.Players) != 0)
            {
                var ps = PlayerStats.Instance;
                if (ps != null && ps.Health > 0)
                {
                    float dist = (ps.transform.position - transform.position).magnitude;
                    Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ps.transform.position);
                    Vector3 tp = ps.transform.position + up * 0.5f;
                    if (dist <= range && HasLOS(from, tp, ps))
                    {
                        float score = dist * 0.15f + 2f; // players are mid priority
                        if (_targetT == null || score < bestScore)
                        {
                            _targetT = ps.transform; _targetD = null; _targetP = ps;
                        }
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
                if (ignore != null && hit.collider.GetComponentInParent(ignore.GetType()) != null) return true;
                // Also accept if we hit the same Damageable / PlayerStats hierarchy.
                if (ignore != null)
                {
                    var dHit = hit.collider.GetComponentInParent<Damageable>();
                    var dIgn = ignore as Damageable ?? ignore.GetComponentInParent<Damageable>();
                    if (dHit != null && dIgn != null && dHit == dIgn) return true;
                    var pHit = hit.collider.GetComponentInParent<PlayerStats>();
                    var pIgn = ignore as PlayerStats ?? ignore.GetComponentInParent<PlayerStats>();
                    if (pHit != null && pIgn != null && pHit == pIgn) return true;
                }
                return false;
            }
            return true;
        }

        private void Fire(Vector3 aimPoint, Vector3 forward)
        {
            if (!ConsumeShell()) return;

            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            // Mild ballistic drop: aim slightly above so the heavy shell still lands near target.
            Vector3 dir = (aimPoint - origin).normalized;
            dir = (dir + VoxelEngine.Cosmos.GravityProvider.GetUp(origin) * 0.06f).normalized;

            GiantShell.Spawn(origin, dir * shellSpeed, gameObject, shellMat,
                explosionRadius, damage, explosionMat);
            Flash(origin);

            // Visible barrel recoil.
            if (head != null)
                head.localPosition -= forward * 0.08f;
        }

        private static void Flash(Vector3 pos)
        {
            var go = new GameObject("GiantShellFlash");
            go.transform.position = pos;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(go.transform, false);
            s.transform.localScale = Vector3.one * 0.45f;
            s.GetComponent<Renderer>().sharedMaterial = FxMat;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.2f); l.range = 12f; l.intensity = 10f;
            Object.Destroy(go, 0.12f);
        }
    
        // ── Factory logistics ──
        public ItemContainer GetInputContainer() => ShellMagazine;
        public ItemContainer GetOutputContainer() => null;
        public bool HasOutputReady => false;
        public bool CanAcceptInput => true;
        public int GetInputCapacity(ItemDefinition item)
            => DefenseLogistics.GetMagazineCapacity(ShellMagazine, item);
        public int TryInsert(ItemDefinition item, int count)
            => DefenseLogistics.InsertIntoMagazine(ShellMagazine, item, count);
        public bool IsFaceConnectable(Vector3 fromWorldPos) => true;
        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
            => TryInsert(item, count);

}

    /// <summary>
    /// Massive siege shell. Arcs gently under radial gravity, detonates on impact
    /// with a large blast + voxel crater. One shell is a boss-class event.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GiantShell : MonoBehaviour
    {
        private Rigidbody _rb;
        private GameObject _owner;
        private float _explosionRadius, _damage;
        private Material _explosionMat;
        private bool _detonated;
        private float _life, _maxLife = 10f;

        public static GiantShell Spawn(Vector3 pos, Vector3 velocity, GameObject owner, Material bodyMat,
                                      float explosionRadius, float damage, Material explosionMat)
        {
            var go = new GameObject("GiantShell");
            go.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Shell";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * 0.55f;
            var ren = body.GetComponent<Renderer>();
            if (bodyMat != null) ren.sharedMaterial = bodyMat;

            // Nose cap for readable silhouette.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nose.name = "Nose";
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0f, 0.28f);
            nose.transform.localScale = Vector3.one * 0.35f;
            Object.Destroy(nose.GetComponent<Collider>());
            if (bodyMat != null) nose.GetComponent<Renderer>().sharedMaterial = bodyMat;

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.3f;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = velocity;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.mass = 8f;

            var shell = go.AddComponent<GiantShell>();
            shell._rb = rb;
            shell._owner = owner;
            shell._explosionRadius = explosionRadius;
            shell._damage = damage;
            shell._explosionMat = explosionMat;
            return shell;
        }

        private void FixedUpdate()
        {
            if (_detonated) return;
            _life += Time.fixedDeltaTime;
            if (_life > _maxLife) { Detonate(transform.position); return; }

            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            _rb.linearVelocity += grav * (0.55f * Time.fixedDeltaTime); // partial gravity — heavy but still punchy

            if (_rb.linearVelocity.sqrMagnitude > 0.05f)
            {
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
                transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized, up);
            }

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
            // Large blast + substantial voxel crater — this is a siege round.
            Explosion.Detonate(at, _explosionRadius, _damage, _owner,
                _explosionRadius * 0.45f, _explosionMat);
            Destroy(gameObject);
        }
    }
}
