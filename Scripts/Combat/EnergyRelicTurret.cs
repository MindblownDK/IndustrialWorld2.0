// Assets/Scripts/VoxelEngine/Combat/EnergyRelicTurret.cs
//
// Late-tier placeable energy / relic turret. Fires hitscan Electrical beams that
// punch through light cover less than kinetic rounds care about LOS-wise (still
// needs clear LOS). Consumes Charged Cells from a magazine; optional Relic
// Capacitors deliver a heavier charged shot. Configure via the shared defense panel.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Player;

namespace VoxelEngine.Combat
{
    public class EnergyRelicTurret : Damageable
    {
        [Header("Combat")]
        public float range = 48f;
        public float fireCooldown = 0.55f;
        public float cellDamage = 28f;
        public float relicDamage = 70f;
        public float beamWidth = 0.08f;
        public float beamLife = 0.1f;
        public Material beamMat;
        public Material coreMat;
        public Material muzzleMat;

        [Header("Turret")]
        public Transform head;
        public Transform muzzle;
        public Transform crystal;          // optional spinning relic crystal
        public float aimSpeed = 5.5f;
        public float crystalSpin = 90f;
        public TargetFilter filter = TargetFilter.Enemies;
        public bool autoMode = true;

        [SerializeField] private ItemContainer _cellMag;
        private float _nextFire, _retargetAt;
        private Transform _targetT;
        private Damageable _targetD;
        private PlayerStats _targetP;
        private static Material _fxMat;
        private Light _coreLight;

        public ItemContainer CellMagazine
        {
            get
            {
                if (_cellMag == null)
                {
                    _cellMag = new ItemContainer("Cells", 3);
                    _cellMag.AcceptFilter = (item, wanted) =>
                        item != null && item.itemId != null && IsCellItem(item.itemId)
                            ? Mathf.Min(wanted, item.maxStack) : 0;
                }
                return _cellMag;
            }
        }

        private static bool IsCellItem(string id) =>
            id == "item_charged_cell" || id == "item_relic_capacitor";

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 180f);
            base.Awake();
            _ = CellMagazine;

            // Soft core glow so the turret reads as "powered" at night.
            if (crystal != null)
            {
                _coreLight = crystal.gameObject.GetComponent<Light>();
                if (_coreLight == null) _coreLight = crystal.gameObject.AddComponent<Light>();
                _coreLight.type = LightType.Point;
                _coreLight.color = new Color(0.55f, 0.85f, 1f);
                _coreLight.range = 6f;
                _coreLight.intensity = 1.6f;
            }
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
                    _fxMat = new Material(sh) { color = new Color(0.45f, 0.9f, 1f) };
                    if (_fxMat.HasProperty("_BaseColor"))
                        _fxMat.SetColor("_BaseColor", new Color(0.45f, 0.9f, 1f));
                }
                return _fxMat;
            }
        }

        private void Update()
        {
            if (crystal != null)
                crystal.Rotate(Vector3.up, crystalSpin * Time.deltaTime, Space.Self);

            if (_coreLight != null)
                _coreLight.intensity = 1.4f + Mathf.Sin(Time.time * 4f) * 0.35f;

            if (!autoMode) return;
            if (!HasAmmo()) return;

            if (Time.time >= _retargetAt || !Valid(_targetT))
            {
                _retargetAt = Time.time + 0.28f;
                FindTarget();
            }
            if (_targetT == null) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 origin = muzzle != null ? muzzle.position : (head != null ? head.position : transform.position);
            Vector3 aimPoint = _targetT.position + up * 0.55f;
            Vector3 to = aimPoint - origin;

            if (head != null && to.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, aimSpeed * Time.deltaTime);
            }

            if (Time.time >= _nextFire && Vector3.Angle(head != null ? head.forward : to.normalized, to) < 12f)
            {
                _nextFire = Time.time + fireCooldown;
                Fire(aimPoint);
            }
        }

        private bool HasAmmo()
        {
            for (int i = 0; i < CellMagazine.Slots.Count; i++)
            {
                var s = CellMagazine.GetSlot(i);
                if (s != null && !s.IsEmpty) return true;
            }
            return false;
        }

        private bool ConsumeCell(out bool isRelic)
        {
            isRelic = false;
            // Prefer relic capacitors first (heavier shot), then charged cells.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < CellMagazine.Slots.Count; i++)
                {
                    var s = CellMagazine.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null) continue;
                    string id = s.item.itemId ?? "";
                    bool relic = id == "item_relic_capacitor";
                    if (pass == 0 && !relic) continue;
                    if (pass == 1 && relic) continue;
                    s.count--;
                    CellMagazine.SetSlot(i, s.count <= 0 ? new ItemStack() : s);
                    isRelic = relic;
                    return true;
                }
            }
            return false;
        }

        private bool Valid(Transform t)
        {
            if (t == null) return false;
            if ((t.position - transform.position).sqrMagnitude > range * range) return false;
            Vector3 from = muzzle != null ? muzzle.position : transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(t.position);
            return HasLOS(from, t.position + up * 0.5f, t);
        }

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
                Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(d.transform.position);
                Vector3 tp = d.transform.position + up * 0.5f;
                if (!HasLOS(from, tp, d)) continue;
                float s = (d.transform.position - transform.position).sqrMagnitude;
                // Slight preference for higher-HP elites (energy weapons shine late-game).
                float score = s - d.maxHealth * 2f;
                float bestScore = bestSqr - (_targetD != null ? _targetD.maxHealth * 2f : 0f);
                if (_targetT == null || score < bestScore)
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
                    Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(ps.transform.position);
                    Vector3 tp = ps.transform.position + up * 0.5f;
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

        private void Fire(Vector3 aimPoint)
        {
            if (!ConsumeCell(out bool isRelic)) return;

            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            Vector3 dir = (aimPoint - origin).normalized;
            float dmg = isRelic ? relicDamage : cellDamage;
            Color beamCol = isRelic ? new Color(0.85f, 0.45f, 1f) : new Color(0.4f, 0.9f, 1f);
            float width = isRelic ? beamWidth * 1.7f : beamWidth;

            Vector3 end;
            if (Physics.Raycast(origin, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var d = hit.collider.GetComponentInParent<Damageable>();
                if (d != null && d.IsAlive && d != (Damageable)this)
                {
                    d.TakeDamage(new DamageEvent
                    {
                        amount = dmg,
                        type = DamageType.Electrical,
                        point = hit.point,
                        direction = dir,
                        source = gameObject
                    });
                }
                var ps = hit.collider.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(dmg);

                ImpactSpark(hit.point, beamCol, isRelic);
            }
            else end = origin + dir * range;

            Beam(origin, end, beamCol, width, isRelic);
            MuzzleFlash(origin, beamCol, isRelic);
        }

        private void Beam(Vector3 from, Vector3 to, Color col, float width, bool heavy)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 0.01f) return;

            Material mat = beamMat != null ? beamMat : FxMat;
            // Clone tint so concurrent beams don't fight over shared material colour.
            var inst = new Material(mat);
            inst.color = col;
            if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", col);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = heavy ? "RelicBeam" : "EnergyBeam";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = new Vector3(width, width, len);
            go.GetComponent<Renderer>().sharedMaterial = inst;
            Object.Destroy(inst, beamLife + 0.05f);
            Object.Destroy(go, beamLife);

            // Soft secondary glow beam.
            var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(glow.GetComponent<Collider>());
            glow.transform.position = go.transform.position;
            glow.transform.rotation = go.transform.rotation;
            glow.transform.localScale = new Vector3(width * 2.4f, width * 2.4f, len);
            var glowMat = new Material(inst) { color = new Color(col.r, col.g, col.b, 0.35f) };
            if (glowMat.HasProperty("_BaseColor")) glowMat.SetColor("_BaseColor", col);
            glow.GetComponent<Renderer>().sharedMaterial = glowMat;
            Object.Destroy(glowMat, beamLife + 0.05f);
            Object.Destroy(glow, beamLife * 0.85f);
        }

        private void MuzzleFlash(Vector3 pos, Color col, bool heavy)
        {
            var go = new GameObject("EnergyMuzzle");
            go.transform.position = pos;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(go.transform, false);
            s.transform.localScale = Vector3.one * (heavy ? 0.28f : 0.16f);
            var mat = muzzleMat != null ? muzzleMat : (coreMat != null ? coreMat : FxMat);
            var inst = new Material(mat) { color = col };
            if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", col);
            s.GetComponent<Renderer>().sharedMaterial = inst;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = col; l.range = heavy ? 10f : 6f; l.intensity = heavy ? 8f : 4.5f;
            Object.Destroy(inst, 0.12f);
            Object.Destroy(go, 0.08f);
        }

        private void ImpactSpark(Vector3 pos, Color col, bool heavy)
        {
            var go = new GameObject("EnergyImpact");
            go.transform.position = pos;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(go.transform, false);
            s.transform.localScale = Vector3.one * (heavy ? 0.45f : 0.22f);
            var mat = coreMat != null ? coreMat : FxMat;
            var inst = new Material(mat) { color = col };
            if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", col);
            s.GetComponent<Renderer>().sharedMaterial = inst;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = col; l.range = heavy ? 8f : 4f; l.intensity = heavy ? 6f : 3f;
            Object.Destroy(inst, 0.15f);
            Object.Destroy(go, 0.1f);
        }
    }
}
