// Assets/Scripts/VoxelEngine/Combat/FlamethrowerTurret.cs
//
// Placeable close-range flamethrower turret. Auto-targets by faction filter, aims its
// nozzle, and sprays a continuous cone of fire while fuel lasts. Fuel is loaded as
// Flame Canisters (or Coal as a weaker fallback) via the shared defense panel.
// Leaves short-lived ground-fire patches for area denial.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Player;

namespace VoxelEngine.Combat
{
    public class FlamethrowerTurret : Damageable
    {
        [Header("Combat")]
        public float range = 11f;
        public float coneHalfAngle = 28f;
        public float tickInterval = 0.18f;
        public float damagePerTick = 7f;
        public float burnDps = 5f;
        public float burnDuration = 2.5f;
        public float fuelSecondsPerCanister = 8f;
        public float coalSecondsBonus = 2.5f;
        public float groundFireChance = 0.22f;
        public Material flameMat;
        public Material groundFireMat;

        [Header("Turret")]
        public Transform head;
        public Transform muzzle;
        public float aimSpeed = 5f;
        public TargetFilter filter = TargetFilter.Enemies;
        public bool autoMode = true;

        [SerializeField] private ItemContainer _fuelMag;
        [SerializeField] private float _fuelSeconds;

        public float FuelSeconds => _fuelSeconds;
        public float MaxFuelDisplay => fuelSecondsPerCanister * 4f;

        public ItemContainer FuelMagazine
        {
            get
            {
                if (_fuelMag == null)
                {
                    _fuelMag = new ItemContainer("Fuel", 3);
                    _fuelMag.AcceptFilter = (item, wanted) =>
                        item != null && item.itemId != null && IsFuelItem(item.itemId)
                            ? Mathf.Min(wanted, item.maxStack) : 0;
                }
                return _fuelMag;
            }
        }

        private float _nextTick, _retargetAt, _fxTimer;
        private Transform _targetT;
        private Damageable _targetD;
        private PlayerStats _targetP;
        private static Material _fxMat;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 100f);
            base.Awake();
            _ = FuelMagazine;
        }

        private static bool IsFuelItem(string id) =>
            id == "item_flame_canister" || id == "coal" || id == "item_coal";

        private static Material FxMat
        {
            get
            {
                if (_fxMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("Sprites/Default");
                    _fxMat = new Material(sh) { color = new Color(1f, 0.45f, 0.12f) };
                    if (_fxMat.HasProperty("_BaseColor"))
                        _fxMat.SetColor("_BaseColor", new Color(1f, 0.45f, 0.12f));
                }
                return _fxMat;
            }
        }

        private void Update()
        {
            if (!autoMode) return;

            // Top up continuous fuel from the magazine when empty.
            if (_fuelSeconds <= 0f) TryConsumeFuelCanister();
            if (_fuelSeconds <= 0f) return;

            if (Time.time >= _retargetAt || !Valid(_targetT))
            {
                _retargetAt = Time.time + 0.3f;
                FindTarget();
            }
            if (_targetT == null) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 aimPoint = _targetT.position + up * 0.5f;
            Vector3 origin = muzzle != null ? muzzle.position : (head != null ? head.position : transform.position);
            Vector3 to = aimPoint - origin;

            if (head != null && to.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, aimSpeed * Time.deltaTime);
            }

            // Only spray when the target sits inside the nozzle cone.
            Vector3 forward = head != null ? head.forward : to.normalized;
            float angle = Vector3.Angle(forward, to);
            if (angle > coneHalfAngle) return;

            // Continuous fuel burn while engaged.
            _fuelSeconds -= Time.deltaTime;
            if (_fuelSeconds < 0f) _fuelSeconds = 0f;

            _fxTimer -= Time.deltaTime;
            if (_fxTimer <= 0f)
            {
                _fxTimer = 0.07f;
                SprayFx(origin, forward, up);
            }

            if (Time.time >= _nextTick)
            {
                _nextTick = Time.time + tickInterval;
                ApplyConeDamage(origin, forward, up);
                if (Random.value < groundFireChance)
                    SpawnGroundFire(origin + forward * Random.Range(2f, range * 0.85f), up);
            }
        }

        private void TryConsumeFuelCanister()
        {
            for (int i = 0; i < FuelMagazine.Slots.Count; i++)
            {
                var s = FuelMagazine.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                string id = s.item.itemId ?? "";
                float add = id == "item_flame_canister" ? fuelSecondsPerCanister
                          : (id == "coal" || id == "item_coal") ? coalSecondsBonus
                          : coalSecondsBonus;
                s.count--;
                FuelMagazine.SetSlot(i, s.count <= 0 ? new ItemStack() : s);
                _fuelSeconds += add;
                return;
            }
        }

        private bool Valid(Transform t) =>
            t != null && (t.position - transform.position).sqrMagnitude <= range * range;

        private void FindTarget()
        {
            _targetT = null; _targetD = null; _targetP = null;
            float bestSqr = range * range;
            Vector3 from = muzzle != null ? muzzle.position : transform.position;

            var cols = Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Damageable>();
            foreach (var c in cols)
            {
                var d = c.GetComponentInParent<Damageable>();
                if (d == null || d == (Damageable)this || !d.IsAlive || !seen.Add(d)) continue;
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
                var ps = PlayerStats.Instance;
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

        private void ApplyConeDamage(Vector3 origin, Vector3 forward, Vector3 up)
        {
            var cols = Physics.OverlapSphere(origin + forward * (range * 0.45f), range * 0.55f, ~0, QueryTriggerInteraction.Ignore);
            var hitD = new HashSet<Damageable>();
            bool hitPlayer = false;

            foreach (var c in cols)
            {
                Vector3 p = c.ClosestPoint(origin);
                Vector3 to = p - origin;
                float dist = to.magnitude;
                if (dist > range || dist < 0.15f) continue;
                if (Vector3.Angle(forward, to) > coneHalfAngle) continue;

                var d = c.GetComponentInParent<Damageable>();
                if (d != null && d != (Damageable)this && d.IsAlive && hitD.Add(d))
                {
                    TargetFilter ff = d is VoxelEngine.Fauna.PassiveAnimal ? TargetFilter.Passive
                                    : (d.GetType().Name.StartsWith("Enemy") ? TargetFilter.Enemies : TargetFilter.None);
                    if ((filter & ff) == TargetFilter.None) continue;
                    float falloff = 1f - Mathf.Clamp01(dist / range) * 0.45f;
                    d.TakeDamage(new DamageEvent
                    {
                        amount = damagePerTick * falloff,
                        type = DamageType.Fire,
                        point = p,
                        direction = to.normalized,
                        source = gameObject
                    });
                }

                if ((filter & TargetFilter.Players) != 0 && !hitPlayer)
                {
                    var ps = c.GetComponentInParent<PlayerStats>();
                    if (ps != null && ps.Health > 0f)
                    {
                        hitPlayer = true;
                        float falloff = 1f - Mathf.Clamp01(dist / range) * 0.45f;
                        ps.TakeDamage(damagePerTick * falloff);
                        ps.ApplyBurn(burnDps, burnDuration);
                    }
                }
            }

            // Direct tracked target always takes a tick if still in cone (handles no-collider edge cases).
            if (_targetD != null && _targetD.IsAlive)
            {
                Vector3 tp = _targetD.transform.position + up * 0.5f;
                Vector3 to = tp - origin;
                if (to.magnitude <= range && Vector3.Angle(forward, to) <= coneHalfAngle && hitD.Add(_targetD))
                {
                    _targetD.TakeDamage(new DamageEvent
                    {
                        amount = damagePerTick,
                        type = DamageType.Fire,
                        point = tp,
                        direction = to.normalized,
                        source = gameObject
                    });
                }
            }
            if (_targetP != null && _targetP.Health > 0f && !hitPlayer)
            {
                Vector3 tp = _targetP.transform.position + up * 0.5f;
                Vector3 to = tp - origin;
                if (to.magnitude <= range && Vector3.Angle(forward, to) <= coneHalfAngle)
                {
                    _targetP.TakeDamage(damagePerTick);
                    _targetP.ApplyBurn(burnDps, burnDuration);
                }
            }
        }

        private void SprayFx(Vector3 origin, Vector3 forward, Vector3 up)
        {
            Material mat = flameMat != null ? flameMat : FxMat;
            int n = 5;
            for (int i = 0; i < n; i++)
            {
                float t = (i + 1) / (float)(n + 1);
                Vector3 lateral = (Random.insideUnitSphere * 0.35f);
                lateral = Vector3.ProjectOnPlane(lateral, forward);
                Vector3 pos = origin + forward * (range * t * Random.Range(0.55f, 1f)) + lateral + up * Random.Range(0f, 0.25f);
                float sz = Mathf.Lerp(0.18f, 0.55f, t) * Random.Range(0.75f, 1.15f);

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "FlamePuff";
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * sz;
                var ren = go.GetComponent<Renderer>();
                if (ren != null) ren.sharedMaterial = mat;

                // Warm flicker light on the nearest puff only.
                if (i == 0)
                {
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.45f, 0.12f);
                    l.range = 4.5f;
                    l.intensity = 2.8f;
                }
                Object.Destroy(go, 0.12f + t * 0.1f);
            }
        }

        private void SpawnGroundFire(Vector3 pos, Vector3 up)
        {
            // Snap to ground along gravity so fire sits on the surface.
            Vector3 down = -up;
            Vector3 start = pos + up * 2f;
            if (Physics.Raycast(start, down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point + up * 0.05f;
            else
                pos = pos; // leave floating only if no ground

            Material mat = groundFireMat != null ? groundFireMat : (flameMat != null ? flameMat : FxMat);
            FireWallHazard.Spawn(pos, up, mat, dur: 3.2f, dps: burnDps * 0.85f, radius: 1.35f);
        }

        // ── Persistence helpers (used by WorldStatePersistence) ──────────
        public float CaptureFuelSeconds() => _fuelSeconds;
        public void RestoreFuelSeconds(float seconds) => _fuelSeconds = Mathf.Max(0f, seconds);
    }
}
