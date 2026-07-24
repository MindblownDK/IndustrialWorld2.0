// Assets/Scripts/VoxelEngine/Items/DroppedItem.cs
//
// Physical world-drop item. ALWAYS visible — uses a hardcoded bright material.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.Items
{
    public class DroppedItem : MonoBehaviour
    {
        private static Material _sharedDropMaterial;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly MaterialPropertyBlock Properties = new();

        public ItemStack stack;
        public float lifetime = 300f;

        /// <summary>Total item units currently represented by physical world drops.
        /// Conveyor packets are a separate simulation and never contribute here.</summary>
        public static int ActivePhysicalItemCount { get; private set; }
        private static float _lastLimitNoticeTime = -999f;
        private int _registeredItemCount;

        private float _spawnTime;
        // Longer pickup grace so a player who drops an item can SEE it before
        // walking forward and instantly re-picking it (the previous 0.8s was
        // shorter than the typical post-drop step).
        private float _pickupDelay = 1.5f;
        private float _bobPhase;
        private Rigidbody _rb;
        private bool _settled;
        // A manually dropped stack must not immediately re-enter the same inventory
        // through the large pickup trigger while it is still beside the player.
        private Inventory _dropOwner;
        private bool _ownerLeftPickupRange;

        public static DroppedItem Spawn(ItemStack stack, Vector3 position, Vector3 tossDir)
        {
            if (stack == null || stack.IsEmpty) return null;

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
            // Bigger drop cube (0.5m vs 0.35m) so it's clearly visible at typical
            // player viewing distances. Also lift the spawn just above the toss
            // position so it never embeds in the floor / player capsule.
            go.transform.position = position + Vector3.up * 0.25f;
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
            rb.linearDamping = 3f;
            rb.angularDamping = 4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.isKinematic = false;
            rb.linearVelocity = tossDir.normalized * 2.5f + Vector3.up * 3f;
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
            go.SetActive(true);

            Debug.Log($"[DroppedItem] Spawned {stack.item.displayName} x{stack.count} at {position}");
            return di;
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

        private void Update()
        {
            if (Time.time - _spawnTime > lifetime) { Despawn(); return; }

            if (_rb != null && !_settled && _rb.linearVelocity.sqrMagnitude < 0.1f &&
                Time.time - _spawnTime > 1.5f)
            {
                _settled = true;
                _rb.isKinematic = true;
            }

            if (_settled)
            {
                float bob = Mathf.Sin(Time.time * 2.5f + _bobPhase) * 0.06f;
                transform.position += Vector3.up * bob * Time.deltaTime;
                transform.Rotate(Vector3.up, 50f * Time.deltaTime, Space.World);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (Time.time - _spawnTime < _pickupDelay) return;
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
            return false;
        }

        public bool TryPickup(Inventory inv)
        {
            if (inv == null || stack == null || stack.IsEmpty) return false;
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

        /// <summary>Returns this physical item entity to the shared pool.</summary>
        private void Despawn()
        {
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
