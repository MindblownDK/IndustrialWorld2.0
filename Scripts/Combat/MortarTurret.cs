// Assets/Scripts/VoxelEngine/Combat/MortarTurret.cs
//
// Placeable mortar turret — indirect fire over walls and terrain. Auto-targets by
// faction filter, elevates its tube, and lobs arcing shells (Explosive / Smoke /
// Illumination) loaded via the shared defense panel. Unlike direct-fire turrets it
// does NOT require line of sight — it only needs the target to be in range.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Player;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Combat
{
    public enum MortarShellType { Explosive, Smoke, Illumination }

    public class MortarTurret : Damageable, IItemConsumer, IDirectItemPortEndpoint, IInventoryInterface, IDefenseFirePolicy, IDefenseEngagement
    {
        [Header("Combat")]
        public float range = 55f;
        public float minRange = 8f;
        public float fireCooldown = 3.2f;
        public float damage = 45f;
        public float explosionRadius = 7f;
        public float shellSpeed = 22f;
        public float lobHeight = 18f;
        public Material shellMat;
        public Material explosionMat;
        public Material smokeMat;
        public Material flareMat;

        [Header("Turret")]
        public Transform head;
        public Transform muzzle;
        public float aimSpeed = 2.5f;
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
                    _shellMag = new ItemContainer("Mortar Shells", 3);
                    _shellMag.AcceptFilter = (item, wanted) =>
                        item != null && item.itemId != null && item.itemId.StartsWith("item_mortar")
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

            maxHealth = Mathf.Max(maxHealth, 120f);
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
                    _fxMat = new Material(sh) { color = new Color(1f, 0.75f, 0.3f) };
                    if (_fxMat.HasProperty("_BaseColor"))
                        _fxMat.SetColor("_BaseColor", new Color(1f, 0.75f, 0.3f));
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
                _retargetAt = Time.time + 0.45f;
                FindTarget();
            }
            if (_targetT == null) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 aimPoint = _targetT.position;
            Vector3 origin = muzzle != null ? muzzle.position : (head != null ? head.position : transform.position);
            Vector3 to = aimPoint - origin;
            Vector3 flat = Vector3.ProjectOnPlane(to, up);

            // Elevate the tube toward a lob heading (yaw on target, pitch up).
            if (head != null && flat.sqrMagnitude > 0.0001f)
            {
                float elev = Mathf.Clamp(35f + flat.magnitude * 0.35f, 40f, 72f);
                Vector3 dir = (flat.normalized + up * Mathf.Tan(elev * Mathf.Deg2Rad)).normalized;
                Quaternion look = Quaternion.LookRotation(dir, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, aimSpeed * Time.deltaTime);
            }

            if (Time.time >= _nextFire)
            {
                _nextFire = Time.time + fireCooldown;
                Fire(aimPoint, up);
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

        private bool ConsumeShell(out ItemDefinition item, out MortarShellType type)
        {
            item = null; type = MortarShellType.Explosive;
            for (int i = 0; i < ShellMagazine.Slots.Count; i++)
            {
                var s = ShellMagazine.GetSlot(i);
                if (s == null || s.IsEmpty) continue;
                item = s.item;
                s.count--;
                ShellMagazine.SetSlot(i, s.count <= 0 ? new ItemStack() : s);
                type = ShellTypeFromItem(item);
                if (!HasAmmo()) DefenseStatus.NotifyEmpty("Mortar Turret");
                return true;
            }
            return false;
        }

        private static MortarShellType ShellTypeFromItem(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return MortarShellType.Explosive;
            if (item.itemId.Contains("smoke")) return MortarShellType.Smoke;
            if (item.itemId.Contains("illum") || item.itemId.Contains("flare")) return MortarShellType.Illumination;
            return MortarShellType.Explosive;
        }

        // Indirect fire: range-gated only (no LOS). Min range avoids dropping on itself.
        private bool Valid(Transform t)
        {
            if (t == null) return false;
            float sqr = (t.position - transform.position).sqrMagnitude;
            float eng = EngagementRange;
            if (sqr > eng * eng || sqr < minRange * minRange) return false;
            return DefenseEngagement.IsInEngagement(this, t.position);
        }

        private void FindTarget()
        {
            _targetT = null; _targetD = null; _targetP = null;
            float bestSqr = EngagementRange * EngagementRange;
            float minSqr = minRange * minRange;

            var cols = Physics.OverlapSphere(transform.position, EngagementRange, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Damageable>();
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<Damageable>();
                if (d == null || d == (Damageable)this || !d.IsAlive || !seen.Add(d)) continue;
                TargetFilter ff = d is VoxelEngine.Fauna.PassiveAnimal ? TargetFilter.Passive
                                : (d.GetType().Name.StartsWith("Enemy") ? TargetFilter.Enemies : TargetFilter.None);
                if ((filter & ff) == TargetFilter.None) continue;
                float s = (d.transform.position - transform.position).sqrMagnitude;
                if (s < minSqr || s > bestSqr) continue;
                // Prefer targets closer to mid-range so the lob is comfortable.
                float score = Mathf.Abs(s - (EngagementRange * 0.45f) * (EngagementRange * 0.45f));
                float bestScore = Mathf.Abs(bestSqr - (EngagementRange * 0.45f) * (EngagementRange * 0.45f));
                if (_targetT == null || score < bestScore || s < bestSqr)
                {
                    bestSqr = s;
                    _targetT = d.transform; _targetD = d; _targetP = null;
                }
            }

            if ((filter & TargetFilter.Players) != 0)
            {
                var ps = PlayerStats.Instance;
                if (ps != null && ps.Health > 0)
                {
                    float s = (ps.transform.position - transform.position).sqrMagnitude;
                    if (s >= minSqr && s <= EngagementRange * EngagementRange && (_targetT == null || s < bestSqr))
                    {
                        _targetT = ps.transform; _targetD = null; _targetP = ps;
                    }
                }
            }
        }

        private void Fire(Vector3 aimPoint, Vector3 up)
        {
            if (!ConsumeShell(out _, out var shellType)) return;

            Vector3 origin = muzzle != null ? muzzle.position : transform.position + up * 1.2f;
            Vector3 velocity = ComputeLobVelocity(origin, aimPoint, up);

            MortarShell.Spawn(origin, velocity, gameObject, shellMat,
                explosionRadius, damage, explosionMat, smokeMat, flareMat, shellType);

            // Muzzle flash + brief tube recoil visual.
            Flash(origin);
            if (head != null)
                head.localPosition -= head.forward * 0.04f;
        }

        /// <summary>
        /// High-arc lob toward the aim point under radial gravity. Falls back to a
        /// simple elevated push if the analytic solution is degenerate.
        /// </summary>
        private Vector3 ComputeLobVelocity(Vector3 origin, Vector3 target, Vector3 up)
        {
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(origin);
            float g = Mathf.Max(0.1f, grav.magnitude);
            Vector3 flat = Vector3.ProjectOnPlane(target - origin, up);
            float dist = flat.magnitude;
            float heightDiff = Vector3.Dot(target - origin, up);

            // Aim for a peak roughly lobHeight above the higher of origin/target.
            float peak = Mathf.Max(lobHeight, heightDiff + 6f);
            // Time to peak + time down (v = sqrt(2gh)).
            float tUp = Mathf.Sqrt(2f * peak / g);
            float drop = peak - heightDiff;
            float tDown = drop > 0f ? Mathf.Sqrt(2f * drop / g) : tUp * 0.5f;
            float t = Mathf.Clamp(tUp + tDown, 0.8f, 6f);

            Vector3 vFlat = dist > 0.01f ? flat / t : Vector3.zero;
            float vUp = g * tUp; // reach peak at tUp
            Vector3 vel = vFlat + up * vUp;

            // Clamp overall speed so shells don't leave the system.
            float maxSpd = shellSpeed * 2.2f;
            if (vel.magnitude > maxSpd) vel = vel.normalized * maxSpd;
            if (vel.magnitude < 4f)
                vel = (flat.normalized * 0.55f + up * 0.85f).normalized * shellSpeed;
            return vel;
        }

        private static void Flash(Vector3 pos)
        {
            var go = new GameObject("MortarFlash");
            go.transform.position = pos;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(go.transform, false);
            s.transform.localScale = Vector3.one * 0.22f;
            s.GetComponent<Renderer>().sharedMaterial = FxMat;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.65f, 0.25f); l.range = 6f; l.intensity = 5f;
            Object.Destroy(go, 0.08f);
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
}
