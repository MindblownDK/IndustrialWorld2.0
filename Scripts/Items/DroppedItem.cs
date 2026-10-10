// Assets/Scripts/VoxelEngine/Items/DroppedItem.cs
//
// Physical world-drop item. ALWAYS visible — uses a hardcoded bright material.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;

namespace VoxelEngine.Items
{
    public class DroppedItem : MonoBehaviour
    {
        private static Material _sharedDropMaterial;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly MaterialPropertyBlock Properties = new();
        private static readonly Collider[] SpawnOverlapProbe = new Collider[32];
        private const float SpawnLift = 0.33f;
        private const float SpawnBoxHalfExtent = 0.26f;
        private const float SpawnClearanceStep = 0.10f;
        private const int SpawnClearanceAttempts = 8;
        private const float SpawnEscapeStep = 0.50f;
        private const int SpawnEscapeAttempts = 24;

        public ItemStack stack;
        public float lifetime = 300f;

        /// <summary>Total item units currently represented by physical world drops.
        /// Conveyor packets are a separate simulation and never contribute here.</summary>
        public static int ActivePhysicalItemCount { get; private set; }
        private static float _lastLimitNoticeTime = -999f;
        private int _registeredItemCount;

        private float _spawnTime;
        // Brief physics-frame grace prevents the same drop from re-entering an
        // inventory before its owner-exit guard is applied, while still allowing
        // contact pickup to feel immediate.
        private float _pickupDelay = 0.12f;
        private float _bobPhase;
        private Rigidbody _rb;
        private bool _settled;
        private const float NormalLinearDamping = 3f;
        private const float IceLinearDamping = 0.18f;
        private const float NormalAngularDamping = 4f;
        private const float IceAngularDamping = 0.45f;
        // A manually dropped stack must not immediately re-enter the same inventory
        // through the large pickup trigger while it is still beside the player.
        private Inventory _dropOwner;
        private bool _ownerLeftPickupRange;

        // ── Network identity (14.11.0) ──────────────────────────────────────
        // Wire id of this drop, assigned fresh on every (pooled) spawn so a
        // reused entity can never leak a stale identity. Non-owned drops are
        // remote copies: they never announce their own settle or expiry, but
        // picking one up announces its removal like any other.
        internal string NetId;
        internal bool NetOwned;
        internal bool IsSettled => _settled;

        public static DroppedItem Spawn(ItemStack stack, Vector3 position, Vector3 tossDir)
            => SpawnInternal(stack, position, tossDir, applySpawnLift: true);

        /// <summary>Network replicas receive the owner's already-resolved spawn pose.
        /// They still get penetration clearance, but do not apply the local toss offset twice.</summary>
        internal static DroppedItem SpawnReplicated(ItemStack stack, Vector3 position, Vector3 tossDir)
            => SpawnInternal(stack, position, tossDir, applySpawnLift: false);

        private static DroppedItem SpawnInternal(ItemStack stack, Vector3 position, Vector3 tossDir, bool applySpawnLift)
        {
            if (stack == null || stack.IsEmpty || stack.item == null) return null;

            int requestedCount = stack.count;
            int capacity = AvailablePhysicalCapacity;
            if (capacity <= 0)
            {
                ShowLimitNotice("Drop limit reached", $"{MaximumPhysicalItemCount:N0} physical items active · stack was not spawned", true);
                return null;
            }

            if (requestedCount > capacity)
            {
                ShowLimitNotice("Drop limit capped stack", $"Spawned {capacity:N0}/{requestedCount:N0} before the world-drop limit", true);
            }
            else
            {
                WarnIfNearLimit(capacity - requestedCount);
            }

            var di = DroppedItemPool.Get();
            var go = di.gameObject;
            go.name = $"Drop_{stack.item.displayName}";
            // Active drops belong to the current world scene; only inactive entities
            // live under the persistent pool root.
            go.transform.SetParent(null, false);
            // Configure the complete reused entity while inactive. Activating it only
            // after its stack, timer, owner, and physics are reset prevents an old
            // pooled Update/trigger lifecycle from treating it as expired.
            // Bigger drop cube (0.5m vs 0.35m) so it remains visible at normal
            // viewing distances. Spawn in local gravity-up, then move it clear of
            // any static collider if a caller supplied a point inside the ground.
            Vector3 up = GravityProvider.GetUp(position);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();
            go.transform.position = ResolveSpawnPosition(position, up, applySpawnLift);
            go.transform.localScale = Vector3.one * 0.5f;
            go.layer = 0;

            // Reuse one visible material and apply the item tint with a property block.
            // Pooling therefore avoids both GameObject churn and per-drop material allocations.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Color c = stack.item != null ? stack.item.iconTint : Color.white;
                if (c.a < 0.5f || c.r + c.g + c.b < 0.15f)
                    c = new Color(0.72f, 0.72f, 0.78f, 1f);
                c.a = 1f;
                mr.sharedMaterial = GetSharedDropMaterial();
                Properties.Clear();
                Properties.SetColor(BaseColorId, c);
                Properties.SetColor(ColorId, c);
                mr.SetPropertyBlock(Properties);
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = true;
            }

            // Reuse the pooled physics and pickup components.
            var rb = go.GetComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.linearDamping = NormalLinearDamping;
            rb.angularDamping = NormalAngularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.isKinematic = false;
            rb.useGravity = false; // radial/flat gravity is applied from GravityProvider in FixedUpdate
            rb.linearVelocity = ResolveTossDirection(tossDir, up) * 2.5f + up * 3f;
            rb.angularVelocity = Vector3.zero;

            var trigger = go.GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 2.5f;

            di.stack = stack.Clone();
            di.stack.count = Mathf.Min(stack.count, capacity);
            di._registeredItemCount = di.stack.count;
            ActivePhysicalItemCount += di._registeredItemCount;
            di._spawnTime = Time.time;
            di._bobPhase = Random.value * Mathf.PI * 2f;
            di._rb = rb;
            di._settled = false;
            di._dropOwner = null;
            di._ownerLeftPickupRange = false;
            di.NetId = VoxelEngine.Networking.DropSync.NewId();
            di.NetOwned = true;
            go.SetActive(true);

            // Replicate the drop (14.11.0) - no-op while a remote spawn applies,
            // which then re-tags the entity with the sender's wire id.
            VoxelEngine.Networking.DropSync.AnnounceSpawned(di, tossDir);

            Debug.Log($"[DroppedItem] Spawned {stack.item.displayName} x{stack.count} at {go.transform.position}");
            return di;
        }

        private static Vector3 ResolveSpawnPosition(Vector3 origin, Vector3 up, bool applySpawnLift)
        {
            Vector3 candidate = origin + (applySpawnLift ? up * SpawnLift : Vector3.zero);
            Vector3 halfExtents = Vector3.one * SpawnBoxHalfExtent;

            // Preserve the precise local drop point where possible, but first make a
            // fine-grained escape from floor seams and slight terrain penetration.
            for (int attempt = 0; attempt <= SpawnClearanceAttempts; attempt++)
            {
                if (IsSpawnPositionClear(candidate, halfExtents)) return candidate;
                candidate += up * SpawnClearanceStep;
            }

            // Several overflow and machine-recovery callers provide a machine/root
            // position, not a surface point. Continue upward in coarse steps rather
            // than returning a cube embedded inside a tall collider or underground.
            for (int attempt = 0; attempt < SpawnEscapeAttempts; attempt++)
            {
                candidate += up * SpawnEscapeStep;
                if (IsSpawnPositionClear(candidate, halfExtents)) return candidate;
            }

            Debug.LogWarning("[DroppedItem] Could not find a clear spawn point within "
                + $"{SpawnLift + (SpawnClearanceAttempts + 1) * SpawnClearanceStep + SpawnEscapeAttempts * SpawnEscapeStep:0.0} m of the requested pose; keeping the final local-up fallback.");
            return candidate;
        }

        private static bool IsSpawnPositionClear(Vector3 candidate, Vector3 halfExtents)
        {
            int count = Physics.OverlapBoxNonAlloc(candidate, halfExtents, SpawnOverlapProbe,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < count; i++)
            {
                var collider = SpawnOverlapProbe[i];
                SpawnOverlapProbe[i] = null;
                if (IsStaticSpawnBlocker(collider)) blocked = true;
            }

            // The non-allocating buffer is normally plenty; preserve correctness if a
            // dense machine pile fills it and a blocking collider landed past it.
            if (!blocked && count >= SpawnOverlapProbe.Length)
            {
                foreach (var collider in Physics.OverlapBox(candidate, halfExtents,
                             Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (IsStaticSpawnBlocker(collider)) return false;
                }
            }
            return !blocked;
        }

        private static bool IsStaticSpawnBlocker(Collider collider)
        {
            if (collider == null || collider.isTrigger) return false;
            var body = collider.attachedRigidbody;
            return body == null || body.isKinematic;
        }

        private static Vector3 ResolveTossDirection(Vector3 requested, Vector3 localUp)
        {
            if (requested.sqrMagnitude < 0.0001f) return localUp;
            Vector3 direction = requested.normalized;
            if (Vector3.Dot(direction, Vector3.up) > 0.995f) return localUp;
            if (Vector3.Dot(direction, Vector3.down) > 0.995f) return -localUp;
            return direction;
        }

        public static int MaximumPhysicalItemCount
        {
            get
            {
                var session = VoxelEngine.Menu.WorldSession.Instance;
                return session != null ? Mathf.Max(1, session.maxDroppedItems) : VoxelEngine.Menu.WorldSession.DefaultMaxDroppedItems;
            }
        }

        public static int AvailablePhysicalCapacity => Mathf.Max(0, MaximumPhysicalItemCount - ActivePhysicalItemCount);

        private static void WarnIfNearLimit(int remainingAfterSpawn)
        {
            int max = MaximumPhysicalItemCount;
            if (max <= 0) return;
            float usedAfter = max - Mathf.Max(0, remainingAfterSpawn);
            float fill = usedAfter / Mathf.Max(1f, max);
            if (fill >= 0.95f)
                ShowLimitNotice("Drop limit critical", $"{usedAfter:N0}/{max:N0} physical item units active", true);
            else if (fill >= 0.80f)
                ShowLimitNotice("Drop limit nearing", $"{usedAfter:N0}/{max:N0} physical item units active", false);
        }

        private static void ShowLimitNotice(string title, string detail, bool critical)
        {
            if (Time.unscaledTime - _lastLimitNoticeTime < 2.5f) return;
            _lastLimitNoticeTime = Time.unscaledTime;
            VoxelEngine.UI.BuildFeedbackHud.Show(title, detail, null,
                critical ? new Color(0.95f, 0.25f, 0.18f) : new Color(1f, 0.72f, 0.22f));
        }

        /// <summary>Marks the inventory that intentionally dropped this stack.
        /// That inventory must leave the pickup trigger before it can collect it again.</summary>
        public void SetDropOwner(Inventory owner)
        {
            _dropOwner = owner;
            _ownerLeftPickupRange = owner == null;
        }

        private void FixedUpdate()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb == null || _settled || _rb.isKinematic) return;

            // Unity's built-in gravity is world-down. Dropped items instead use the
            // same radial or flat field as the player, and naturally float in deep space.
            if (_rb.useGravity) _rb.useGravity = false;
            Vector3 gravity = GravityProvider.GetGravity(transform.position);
            if (gravity.sqrMagnitude > 0.000001f)
                _rb.AddForce(gravity, ForceMode.Acceleration);
        }

        private void Update()
        {
            if (Time.time - _spawnTime > lifetime) { Despawn(); return; }

            bool onIce = false;
            if (_rb != null && !_settled)
            {
                Vector3 up = GravityProvider.GetUp(transform.position);
                onIce = IceFrictionUtility.IsIceBelow(transform.position + up * 0.1f, up, 0.65f);
                _rb.linearDamping = onIce ? IceLinearDamping : NormalLinearDamping;
                _rb.angularDamping = onIce ? IceAngularDamping : NormalAngularDamping;
            }

            if (_rb != null && !_settled && _rb.linearVelocity.sqrMagnitude < (onIce ? 0.015f : 0.1f) &&
                Time.time - _spawnTime > (onIce ? 4.0f : 1.5f))
            {
                _settled = true;
                _rb.linearDamping = NormalLinearDamping;
                _rb.angularDamping = NormalAngularDamping;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.useGravity = false;
                _rb.isKinematic = true;
                // Converge the rest position everywhere - remote copies simulate
                // their own toss physics and may have drifted a little (14.11.0).
                VoxelEngine.Networking.DropSync.AnnounceSettled(this);
            }

            if (_settled)
            {
                Vector3 up = GravityProvider.GetUp(transform.position);
                if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
                up.Normalize();
                float bob = Mathf.Sin(Time.time * 2.5f + _bobPhase) * 0.06f;
                transform.position += up * bob * Time.deltaTime;
                transform.rotation = Quaternion.AngleAxis(50f * Time.deltaTime, up) * transform.rotation;
            }
        }

        private void OnTriggerEnter(Collider other) => TryCollectOnContact(other);

        private void OnTriggerStay(Collider other) => TryCollectOnContact(other);

        private void TryCollectOnContact(Collider other)
        {
            if (other == null || Time.time - _spawnTime < _pickupDelay) return;
            if (stack == null || stack.IsEmpty) return;

            var belt = other.GetComponentInParent<VoxelEngine.Simulation.ConveyorBelt>();
            if (belt != null && TryInsertIntoConveyor(belt)) return;

            var inv = other.GetComponentInParent<Inventory>();
            if (inv != null && (inv != _dropOwner || _ownerLeftPickupRange)) TryPickup(inv);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_dropOwner == null || _ownerLeftPickupRange) return;
            var inv = other.GetComponentInParent<Inventory>();
            if (inv == _dropOwner) _ownerLeftPickupRange = true;
        }

        private bool TryInsertIntoConveyor(VoxelEngine.Simulation.ConveyorBelt belt)
        {
            if (belt == null || stack == null || stack.IsEmpty || stack.item == null) return false;
            int capacity = belt.GetInputCapacity(stack.item);
            if (capacity <= 0) return false;

            int moved = belt.TryInsert(stack.item, Mathf.Min(stack.count, capacity));
            if (moved <= 0) return false;

            stack.count -= moved;
            _registeredItemCount = Mathf.Max(0, _registeredItemCount - moved);
            ActivePhysicalItemCount = Mathf.Max(0, ActivePhysicalItemCount - moved);
            UI.BuildFeedbackHud.Show($"Loaded {stack.item.displayName}", $"→ belt x{moved}", stack.item.icon, stack.item.iconTint);
            if (stack.count <= 0)
            {
                Despawn();
                return true;
            }
            VoxelEngine.Networking.DropSync.AnnounceUpdated(this);
            return false;
        }

        public bool TryPickup(Inventory inv)
        {
            if (inv == null || inv.container == null || stack == null || stack.IsEmpty) return false;
            var leftover = inv.container.Insert(stack.Clone());
            if (leftover == null || leftover.count <= 0)
            {
                UI.BuildFeedbackHud.Show($"Picked up {stack.item.displayName}",
                    $"+{stack.count}", stack.item.icon, new Color(0.30f, 0.75f, 0.40f));
                FX.AudioManager.PlayUI(FX.SfxLibrary.Get(FX.Sfx.Pickup), 0.45f,
                    UnityEngine.Random.Range(0.97f, 1.06f));
                Despawn();
                return true;
            }
            int picked = stack.count - leftover.count;
            if (picked > 0)
            {
                UI.BuildFeedbackHud.Show($"Picked up {stack.item.displayName}",
                    $"+{picked}", stack.item.icon, new Color(0.30f, 0.75f, 0.40f));
                FX.AudioManager.PlayUI(FX.SfxLibrary.Get(FX.Sfx.Pickup), 0.45f,
                    UnityEngine.Random.Range(0.97f, 1.06f));
                int removed = stack.count - leftover.count;
                stack.count = leftover.count;
                _registeredItemCount = Mathf.Max(0, _registeredItemCount - removed);
                ActivePhysicalItemCount = Mathf.Max(0, ActivePhysicalItemCount - removed);
                VoxelEngine.Networking.DropSync.AnnounceUpdated(this);
            }
            return false;
        }
        private static Material GetSharedDropMaterial()
        {
            if (_sharedDropMaterial != null) return _sharedDropMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Hidden/InternalErrorShader");
            _sharedDropMaterial = new Material(shader);
            if (_sharedDropMaterial.HasProperty("_Surface")) _sharedDropMaterial.SetFloat("_Surface", 0f);
            if (_sharedDropMaterial.HasProperty("_Blend")) _sharedDropMaterial.SetFloat("_Blend", 0f);
            _sharedDropMaterial.renderQueue = -1;
            return _sharedDropMaterial;
        }

        /// <summary>Remote convergence: adopt the owner's rest position and freeze
        /// exactly the way local settling does (14.11.0).</summary>
        internal void ForceSettle(Vector3 position)
        {
            transform.position = position;
            _settled = true;
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.linearDamping = NormalLinearDamping;
                _rb.angularDamping = NormalAngularDamping;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.useGravity = false;
                _rb.isKinematic = true;
            }
        }

        /// <summary>Remote convergence: adopt a new stack count after a partial
        /// pickup or belt insert on another machine, keeping the world-drop
        /// budget honest. Reaching zero despawns.</summary>
        internal void NetSetCount(int count)
        {
            if (stack == null || stack.IsEmpty) return;
            int clamped = Mathf.Max(0, count);
            int delta = stack.count - clamped;
            if (delta == 0) return;
            stack.count = clamped;
            _registeredItemCount = Mathf.Max(0, _registeredItemCount - delta);
            ActivePhysicalItemCount = Mathf.Max(0, ActivePhysicalItemCount - delta);
            if (clamped <= 0) Despawn();
        }

        /// <summary>Returns this physical item entity to the shared pool.</summary>
        internal void Despawn()
        {
            // Every consumption path funnels through here - expiry, full pickup,
            // belt insert, remote removal - so this is the one removal seam (14.11.0).
            VoxelEngine.Networking.DropSync.HandleDespawn(this);
            if (_registeredItemCount > 0)
                ActivePhysicalItemCount = Mathf.Max(0, ActivePhysicalItemCount - _registeredItemCount);
            _registeredItemCount = 0;
            stack = null;
            _dropOwner = null;
            _ownerLeftPickupRange = false;
            DroppedItemPool.Return(this);
        }

        private void OnDestroy()
        {
            if (_registeredItemCount > 0)
                ActivePhysicalItemCount = Mathf.Max(0, ActivePhysicalItemCount - _registeredItemCount);
            _registeredItemCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPhysicalItemCount() => ActivePhysicalItemCount = 0;
    }
    /// <summary>Reusable physical world-item entities. Pooling avoids allocation and
    /// destruction spikes when mining, conveyors, or inventory overflow create drops.</summary>
    internal static class DroppedItemPool
    {
        private const int InitialCapacity = 24;
        private static readonly Stack<DroppedItem> Available = new(InitialCapacity);
        private static Transform _root;

        public static DroppedItem Get()
        {
            EnsureRoot();
            if (Available.Count > 0) return Available.Pop();
            return Create();
        }

        public static void Return(DroppedItem item)
        {
            if (item == null) return;
            EnsureRoot();
            item.transform.SetParent(_root, false);
            item.gameObject.SetActive(false);
            Available.Push(item);
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;
            var root = new GameObject("DroppedItemPool");
            Object.DontDestroyOnLoad(root);
            _root = root.transform;
            for (int i = 0; i < InitialCapacity; i++) Available.Push(Create());
        }

        private static DroppedItem Create()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PooledDrop";
            go.transform.SetParent(_root, false);
            go.layer = 0;
            go.AddComponent<Rigidbody>();
            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 2.5f;
            var item = go.AddComponent<DroppedItem>();
            go.SetActive(false);
            return item;
        }
    }

}
