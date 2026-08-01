// Assets/Scripts/VoxelEngine/Combat/Artillery.cs
//
// Heavy artillery — Minigun, Cannon, and Schwerer Gustav. Auto-targets by faction
// filter, aims its head, and fires: Minigun = rapid hitscan; Cannon/Gustav = arcing
// shells (Standard / Explosive / Scatter). Shells are ITEMS loaded into a magazine
// (drag-drop via the defense panel). Placeable; Damageable.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Combat
{
    [System.Flags]
    public enum TargetFilter { None = 0, Enemies = 1, Players = 2, Passive = 4 }
    public enum ArtilleryVariant { Minigun, Cannon, Gustav }
    public enum ShellType { Standard, Explosive, Scatter }

    public class Artillery : Damageable, IItemConsumer, IDirectItemPortEndpoint, IInventoryInterface
    {
        [Header("Variant")]
        public ArtilleryVariant variant = ArtilleryVariant.Cannon;
        public TargetFilter filter = TargetFilter.Enemies;
        public bool autoMode = true;

        [Header("Combat")]
        public float range = 70f;
        public float fireCooldown = 2.5f;
        public float damage = 60f;
        public float minigunDamage = 6f;
        public float explosionRadius = 8f;
        public float shellSpeed = 35f;
        public Material shellMat;
        public Material explosionMat;

        [Header("Turret")]
        public Transform head;
        public Transform muzzle;
        public float aimSpeed = 3f;

        [SerializeField] private ItemContainer _shellMag;

        public ItemContainer ShellMagazine
        {
            get
            {
                if (_shellMag == null)
                {
                    _shellMag = new ItemContainer("Shells", 3);
                    _shellMag.AcceptFilter = (item, wanted) =>
                        item != null && item.itemId != null &&
                        (item.itemId.StartsWith("item_shell") || item.itemId == "item_bullets")
                            ? Mathf.Min(wanted, item.maxStack) : 0;
                }
                return _shellMag;
            }
        }

        private float _nextFire, _retargetAt;
        private VoxelEngine.Player.PlayerController _pilot;
        private Transform _origParent;
        private bool _firstPerson = true;
        public static Artillery ActiveArtilleryCockpit { get; private set; }
        private Transform _targetT;
        private Damageable _targetD;
        private VoxelEngine.Player.PlayerStats _targetP;
        private static Material _fxMat;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 200f);
            base.Awake();
            // Touch the magazine so the AcceptFilter is set even before the UI opens.
            _ = ShellMagazine;
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
            if (_pilot != null) { CockpitUpdate(); return; }
            if (!autoMode) return;

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

            if (Time.time >= _nextFire && HasAmmo())
            {
                _nextFire = Time.time + fireCooldown;
                Fire(_targetT.position + up * 0.6f);
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

        /// <summary>Consume one shell from the magazine. Returns the item consumed + its ShellType.</summary>
        private bool ConsumeShell(out ItemDefinition item, out ShellType type)
        {
            item = null; type = ShellType.Standard;
            for (int i = 0; i < ShellMagazine.Slots.Count; i++)
            {
                var s = ShellMagazine.GetSlot(i);
                if (s == null || s.IsEmpty) continue;
                item = s.item;
                s.count--;
                ShellMagazine.SetSlot(i, s.count <= 0 ? new ItemStack() : s);
                type = ShellTypeFromItem(item);
                if (!HasAmmo()) DefenseStatus.NotifyEmpty(variant.ToString());
                return true;
            }
            return false;
        }

        private static ShellType ShellTypeFromItem(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return ShellType.Standard;
            if (item.itemId.Contains("explosive")) return ShellType.Explosive;
            if (item.itemId.Contains("scatter")) return ShellType.Scatter;
            return ShellType.Standard;
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
                if (ignore != null && hit.collider.GetComponentInParent(ignore.GetType()) != null) return true;
                return false;
            }
            return true;
        }

        private void Fire(Vector3 aimPoint)
        {
            if (!ConsumeShell(out var item, out var shellType)) return;

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
                ArtilleryShell.Spawn(origin, dir * shellSpeed, gameObject, shellMat,
                    explosionRadius, damage, explosionMat, shellType);
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

        // ── COCKPIT (manual control) ───────────────────────────
        // Uses the horse-mount pattern: IsMounted suspends locomotion but keeps
        // mouse-look alive so the player aims the turret with the mouse.

        public void EnterCockpit(VoxelEngine.Player.PlayerController player)
        {
            if (_pilot != null) return;
            _pilot = player;
            ActiveArtilleryCockpit = this;

            player.ResetVelocity();
            player.IsMounted = true;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            _origParent = player.transform.parent;
            player.transform.SetParent(transform, worldPositionStays: true);
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            player.transform.position = transform.position + up * 1.8f - head.forward * 1.5f;

            VoxelEngine.UI.BuildFeedbackHud.Show(variant.ToString(),
                "Mouse: aim   LMB: fire   G: view   F: exit", null, new Color(0.5f, 0.8f, 1f));
        }

        public void ExitCockpit()
        {
            if (_pilot == null) return;
            var player = _pilot;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);

            player.transform.SetParent(_origParent, worldPositionStays: true);
            player.transform.position = transform.position + up * 1.5f + head.right * 2.5f;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            player.ResetVelocity();
            player.IsMounted = false;
            _pilot = null;
            ActiveArtilleryCockpit = null;
        }

        private void CockpitUpdate()
        {
            var player = _pilot;
            if (player == null) return;

            if (GameSettings.WasPressed(InputAction.ExitCockpit)) { ExitCockpit(); return; }
            if (GameSettings.WasPressed(InputAction.BuildToggleGrid)) _firstPerson = !_firstPerson;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);

            // The turret head follows where the player looks.
            if (head != null)
            {
                Quaternion look = Quaternion.LookRotation(player.transform.forward, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, aimSpeed * 2f * Time.deltaTime);
            }

            // Fire (LMB).
            if (GameSettings.IsHeld(InputAction.Mine) && Time.time >= _nextFire)
            {
                if (HasAmmo())
                {
                    _nextFire = Time.time + fireCooldown;
                    Vector3 aimPoint = (muzzle != null ? muzzle.position : head.position) + head.forward * range;
                    Fire(aimPoint);
                }
                else
                {
                    _nextFire = Time.time + 2f;   // throttle the "empty" warning
                    VoxelEngine.UI.BuildFeedbackHud.Show("No Shells", "Exit (F) and load via the panel (H)", null, Color.yellow);
                }
            }
        }

        private void LateUpdate()
        {
            // First-person gunsight camera (overrides the player's eye position).
            if (_pilot != null && _firstPerson && _pilot.playerCamera != null)
            {
                _pilot.playerCamera.transform.position = muzzle != null ? muzzle.position : head.position;
                _pilot.playerCamera.transform.rotation = head.rotation;
            }
        }
    
        // ── Factory logistics (belts / chutes / funnels / item pipes) ──
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
