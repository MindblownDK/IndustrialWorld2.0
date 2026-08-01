// Assets/Scripts/VoxelEngine/Combat/AntiAirTurret.cs
//
// Placeable anti-air turret. Fast dual-barrel tracking aimed at aerial threats
// (Griffins, Rocs, and anything well above the local ground). Fires proximity-burst
// flak rounds that detonate near the target. Load AA Rounds via the defense panel.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Player;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Combat
{
    public class AntiAirTurret : Damageable, IItemConsumer, IDirectItemPortEndpoint, IInventoryInterface
    {
        [Header("Combat")]
        public float range = 55f;
        public float fireCooldown = 0.28f;
        public float damage = 14f;
        public float proximityRadius = 3.2f;
        public float shellSpeed = 55f;
        public float minAltitude = 2.2f;   // prefer targets this far above local ground
        public Material shellMat;
        public Material explosionMat;
        public Material tracerMat;

        [Header("Turret")]
        public Transform head;
        public Transform muzzle;
        public float aimSpeed = 9f;
        public TargetFilter filter = TargetFilter.Enemies;
        public bool autoMode = true;
        public bool preferAerialOnly = true; // when true, ignore grounded fodder if any aerial is available

        [SerializeField] private ItemContainer _ammoMag;
        [SerializeField] private int _burstLeft;
        private float _nextFire, _retargetAt, _nextBurstAt;
        private Transform _targetT;
        private Damageable _targetD;
        private PlayerStats _targetP;
        private int _barrelFlip;
        private static Material _fxMat;

        public ItemContainer AmmoMagazine
        {
            get
            {
                if (_ammoMag == null)
                {
                    _ammoMag = new ItemContainer("AA Rounds", 3);
                    _ammoMag.AcceptFilter = (item, wanted) =>
                        item != null && item.itemId != null &&
                        (item.itemId == "item_aa_rounds" || item.itemId == "item_bullets")
                            ? Mathf.Min(wanted, item.maxStack) : 0;
                }
                return _ammoMag;
            }
        }

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 140f);
            base.Awake();
            _ = AmmoMagazine;
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
                    _fxMat = new Material(sh) { color = new Color(0.55f, 0.95f, 1f) };
                    if (_fxMat.HasProperty("_BaseColor"))
                        _fxMat.SetColor("_BaseColor", new Color(0.55f, 0.95f, 1f));
                }
                return _fxMat;
            }
        }

        private void Update()
        {
            if (!autoMode) return;
            if (!HasAmmo()) return;

            if (Time.time >= _retargetAt || !Valid(_targetT))
            {
                _retargetAt = Time.time + 0.2f;
                FindTarget();
            }
            if (_targetT == null) return;

            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            Vector3 origin = muzzle != null ? muzzle.position : (head != null ? head.position : transform.position);
            // Lead the target slightly using current rigidbody velocity when available.
            Vector3 aimPoint = LeadTarget(_targetT, origin, shellSpeed, up);

            Vector3 to = aimPoint - origin;
            if (head != null && to.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, up);
                head.rotation = Quaternion.Slerp(head.rotation, look, aimSpeed * Time.deltaTime);
            }

            // 3-round bursts with a short pause.
            if (_burstLeft <= 0)
            {
                if (Time.time < _nextBurstAt) return;
                _burstLeft = 3;
            }

            if (Time.time >= _nextFire)
            {
                _nextFire = Time.time + fireCooldown;
                Fire(aimPoint);
                _burstLeft--;
                if (_burstLeft <= 0) _nextBurstAt = Time.time + 0.55f;
            }
        }

        private static Vector3 LeadTarget(Transform t, Vector3 origin, float speed, Vector3 up)
        {
            Vector3 pos = t.position + up * 0.4f;
            var rb = t.GetComponentInParent<Rigidbody>();
            if (rb == null || speed < 1f) return pos;
            float dist = Vector3.Distance(origin, pos);
            float eta = dist / speed;
            return pos + rb.linearVelocity * Mathf.Clamp(eta, 0f, 1.2f);
        }

        private bool HasAmmo()
        {
            for (int i = 0; i < AmmoMagazine.Slots.Count; i++)
            {
                var s = AmmoMagazine.GetSlot(i);
                if (s != null && !s.IsEmpty) return true;
            }
            return false;
        }

        private bool ConsumeAmmo()
        {
            for (int i = 0; i < AmmoMagazine.Slots.Count; i++)
            {
                var s = AmmoMagazine.GetSlot(i);
                if (s == null || s.IsEmpty) continue;
                s.count--;
                AmmoMagazine.SetSlot(i, s.count <= 0 ? new ItemStack() : s);
                if (!HasAmmo()) DefenseStatus.NotifyEmpty("Anti-Air Turret");
                return true;
            }
            return false;
        }

        private bool Valid(Transform t)
        {
            if (t == null) return false;
            if ((t.position - transform.position).sqrMagnitude > range * range) return false;
            Vector3 from = muzzle != null ? muzzle.position : transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(t.position);
            return HasLOS(from, t.position + up * 0.4f, t);
        }

        private bool IsAerial(Damageable d, Vector3 up)
        {
            if (d == null) return false;
            string n = d.GetType().Name;
            if (n.Contains("Griffin") || n.Contains("Roc") || n.Contains("Wyvern") ||
                n.Contains("Drake") || n.Contains("Djinn") || n.Contains("Ifrit") ||
                n.Contains("Ray") || n.Contains("Flyer") || n.Contains("Drone"))
                return true;

            // Height heuristic: target well above local ground.
            Vector3 pos = d.transform.position;
            if (Physics.Raycast(pos + up * 0.5f, -up, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                float alt = Vector3.Dot(pos - hit.point, up);
                if (alt >= minAltitude) return true;
            }
            return false;
        }

        private void FindTarget()
        {
            _targetT = null; _targetD = null; _targetP = null;
            float bestScore = float.MaxValue;
            Vector3 from = muzzle != null ? muzzle.position : transform.position;

            bool anyAerial = false;
            var candidates = new List<(Transform t, Damageable d, PlayerStats p, float score, bool aerial)>();

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
                Vector3 tp = d.transform.position + up * 0.4f;
                if (!HasLOS(from, tp, d)) continue;

                bool aerial = IsAerial(d, up);
                if (aerial) anyAerial = true;
                float dist = (d.transform.position - transform.position).magnitude;
                // Prefer aerial + closer.
                float score = dist + (aerial ? 0f : 40f);
                candidates.Add((d.transform, d, null, score, aerial));
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
                        // Player is not aerial unless jetpacking high — treat as ground.
                        candidates.Add((ps.transform, null, ps, dist + 35f, false));
                    }
                }
            }

            foreach (var c in candidates)
            {
                if (preferAerialOnly && anyAerial && !c.aerial) continue;
                if (c.score < bestScore)
                {
                    bestScore = c.score;
                    _targetT = c.t; _targetD = c.d; _targetP = c.p;
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
            if (!ConsumeAmmo()) return;

            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            // Alternate dual-barrel offset for visual flair.
            if (head != null)
            {
                Vector3 right = head.right;
                origin += right * (_barrelFlip == 0 ? -0.12f : 0.12f);
                _barrelFlip = 1 - _barrelFlip;
            }

            Vector3 dir = (aimPoint - origin).normalized;
            FlakRound.Spawn(origin, dir * shellSpeed, gameObject, shellMat != null ? shellMat : FxMat,
                explosionMat != null ? explosionMat : FxMat, damage, proximityRadius, range * 1.05f);

            Tracer(origin, origin + dir * Mathf.Min(12f, range * 0.35f));
            Flash(origin);
        }

        private void Flash(Vector3 pos)
        {
            var go = new GameObject("AAFlash");
            go.transform.position = pos;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(go.transform, false);
            s.transform.localScale = Vector3.one * 0.14f;
            s.GetComponent<Renderer>().sharedMaterial = tracerMat != null ? tracerMat : FxMat;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.6f, 0.9f, 1f); l.range = 5f; l.intensity = 4f;
            Object.Destroy(go, 0.05f);
        }

        private void Tracer(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from; float len = dir.magnitude;
            if (len < 0.01f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = new Vector3(0.035f, 0.035f, len);
            go.GetComponent<Renderer>().sharedMaterial = tracerMat != null ? tracerMat : FxMat;
            Object.Destroy(go, 0.06f);
        }
    
        // ── Factory logistics ──
        public ItemContainer GetInputContainer() => AmmoMagazine;
        public ItemContainer GetOutputContainer() => null;
        public bool HasOutputReady => false;
        public bool CanAcceptInput => true;
        public int GetInputCapacity(ItemDefinition item)
            => DefenseLogistics.GetMagazineCapacity(AmmoMagazine, item);
        public int TryInsert(ItemDefinition item, int count)
            => DefenseLogistics.InsertIntoMagazine(AmmoMagazine, item, count);
        public bool IsFaceConnectable(Vector3 fromWorldPos) => true;
        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
            => TryInsert(item, count);

}

    /// <summary>
    /// Fast flak round with a proximity fuse. Detonates when near any Damageable
    /// (or on impact / max range), dealing a small burst of kinetic damage.
    /// </summary>
    public class FlakRound : MonoBehaviour
    {
        private Vector3 _vel;
        private GameObject _owner;
        private float _damage, _proxRadius, _maxDist, _travelled;
        private Material _boomMat;
        private bool _detonated;
        private float _life;

        public static FlakRound Spawn(Vector3 pos, Vector3 velocity, GameObject owner, Material bodyMat,
                                     Material boomMat, float damage, float proxRadius, float maxDist)
        {
            var go = new GameObject("FlakRound");
            go.transform.position = pos;
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Round";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * 0.12f;
            Object.Destroy(body.GetComponent<Collider>());
            if (bodyMat != null) body.GetComponent<Renderer>().sharedMaterial = bodyMat;

            var fr = go.AddComponent<FlakRound>();
            fr._vel = velocity;
            fr._owner = owner;
            fr._damage = damage;
            fr._proxRadius = proxRadius;
            fr._maxDist = maxDist;
            fr._boomMat = boomMat;
            return fr;
        }

        private void Update()
        {
            if (_detonated) return;
            float dt = Time.deltaTime;
            _life += dt;
            if (_life > 3.5f) { Detonate(transform.position); return; }

            // Mild radial gravity so long shots arc a little.
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(transform.position);
            _vel += grav * (0.15f * dt);

            Vector3 step = _vel * dt;
            float dist = step.magnitude;
            _travelled += dist;
            if (_travelled >= _maxDist) { Detonate(transform.position); return; }

            if (dist > 0.0001f && Physics.Raycast(transform.position, step, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (_owner == null || !hit.collider.transform.IsChildOf(_owner.transform))
                {
                    Detonate(hit.point);
                    return;
                }
            }

            transform.position += step;

            // Proximity fuse — scan near the round for living targets.
            var cols = Physics.OverlapSphere(transform.position, _proxRadius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
            {
                if (_owner != null && c.transform.IsChildOf(_owner.transform)) continue;
                var d = c.GetComponentInParent<Damageable>();
                if (d != null && d.IsAlive) { Detonate(transform.position); return; }
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null && ps.Health > 0f) { Detonate(transform.position); return; }
            }
        }

        private void Detonate(Vector3 at)
        {
            if (_detonated) return;
            _detonated = true;

            // Small flak burst (no big crater — AA shouldn't terraform the base).
            Explosion.Detonate(at, _proxRadius, _damage, _owner, 0f, _boomMat);

            // Extra spark puff so flak reads in the sky.
            var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "FlakPuff";
            Object.Destroy(puff.GetComponent<Collider>());
            puff.transform.position = at;
            puff.transform.localScale = Vector3.one * (_proxRadius * 0.55f);
            if (_boomMat != null) puff.GetComponent<Renderer>().sharedMaterial = _boomMat;
            var l = puff.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.85f, 0.45f); l.range = 6f; l.intensity = 3.5f;
            Object.Destroy(puff, 0.12f);

            Destroy(gameObject);
        }
    }
}
