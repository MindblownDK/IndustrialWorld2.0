// Assets/Scripts/VoxelEngine/Simulation/ReceivingChute.cs
//
// Stationary Large Receiving Chute / Catch Hopper (17.24.0-dev).
// A wide 3x2x3 flared industrial receiving hopper designed to catch physical
// DroppedItems dropped from hovering ships (via GridCargoDropHatch) or tossed
// from above. Features a generous capture funnel zone above its mouth, a 16-slot
// internal buffer, ItemPortRouting support, and automatic export to connected
// ConveyorBelts, ConveyorChutes, Funnels, or adjacent item containers.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Simulation
{
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class ReceivingChute : MonoBehaviour, IMachine, IItemProvider, IItemConsumer, IItemPortHost, IMachineProcessState
    {
        [Header("Hopper Buffer & Capture")]
        public int bufferSlots = 16;
        public float maxMassKg = 100_000f;
        public ItemContainer buffer;
        [Tooltip("Half-extents of the overhead item capture zone above the flared hopper mouth.")]
        public Vector3 captureHalfExtents = new Vector3(1.95f, 1.85f, 1.95f);
        public Vector3 captureCenterOffset = new Vector3(0f, 1.35f, 0f);
        public float captureScanInterval = 0.08f;
        public float exportInterval = 0.15f;
        public bool autoPushToNeighbors = true;

        private bool _userEnabled = true;
        private float _scanTimer;
        private float _exportTimer;
        private int _totalCapturedCount;
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;
        private static readonly Collider[] OverlapBuf = new Collider[64];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // ── IMachine ────────────────────────────────────────────────────────────
        public string MachineName => "Large Receiving Chute";
        public bool IsOnline => true;
        public bool IsActive => _userEnabled && buffer != null && !buffer.IsEmpty();
        public float Progress01 => Fill01;
        public float CurrentWattage => 0f;
        public float SpeedMultiplier { get => 1f; set { } }
        public float EfficiencyMultiplier { get => 1f; set { } }
        public bool UserEnabled { get => _userEnabled; set => _userEnabled = value; }
        public ItemContainer PrimaryContainer => buffer;

        public int TotalCapturedCount => _totalCapturedCount;

        public int BufferedItemCount
        {
            get
            {
                EnsureContainers();
                int total = 0;
                for (int i = 0; i < buffer.Size; i++)
                {
                    var st = buffer.GetSlot(i);
                    if (st != null && !st.IsEmpty) total += st.count;
                }
                return total;
            }
        }

        public float Fill01
        {
            get
            {
                EnsureContainers();
                int usedSlots = 0;
                for (int i = 0; i < buffer.Size; i++)
                {
                    var st = buffer.GetSlot(i);
                    if (st != null && !st.IsEmpty) usedSlots++;
                }
                return buffer.Size > 0 ? Mathf.Clamp01(usedSlots / (float)buffer.Size) : 0f;
            }
        }

        public void EnsureContainers()
        {
            if (buffer == null)
                buffer = new ItemContainer("Captured Items", Mathf.Max(1, bufferSlots));
            else if (buffer.Size != Mathf.Max(1, bufferSlots))
                buffer.Resize(Mathf.Max(1, bufferSlots));
            buffer.OverrideMaxWeightKg = maxMassKg;
        }

        private void Awake()
        {
            EnsureContainers();
            EnsureVisuals();
            EnsureCaptureTrigger();
            var ports = GetComponent<PortConfig>();
            if (ports != null) ports.EnsureAllFaces();
            SimulationTickManager.EnsureInstance();
        }

        private void OnEnable()
        {
            EnsureContainers();
            EnsureVisuals();
            EnsureCaptureTrigger();
            SimulationTickManager.Instance?.Register(this, this);
        }

        private void OnDisable()
        {
            SimulationTickManager.Instance?.Unregister(this);
        }

        public void SimulationTick(float dt)
        {
            if (!_userEnabled) return;
            EnsureContainers();

            _scanTimer += dt;
            if (_scanTimer >= Mathf.Max(0.04f, captureScanInterval))
            {
                _scanTimer = 0f;
                ScanAndCaptureDroppedItems();
            }

            if (autoPushToNeighbors && !buffer.IsEmpty())
            {
                _exportTimer += dt;
                if (_exportTimer >= Mathf.Max(0.05f, exportInterval))
                {
                    _exportTimer = 0f;
                    PushToConnectedOutputs();
                }
            }
        }

        private void ScanAndCaptureDroppedItems()
        {
            Vector3 center = transform.TransformPoint(captureCenterOffset);
            int count = Physics.OverlapBoxNonAlloc(
                center,
                captureHalfExtents,
                OverlapBuf,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                var col = OverlapBuf[i];
                OverlapBuf[i] = null;
                if (col == null) continue;
                var dropped = col.GetComponentInParent<DroppedItem>();
                if (dropped == null || dropped.stack == null || dropped.stack.IsEmpty) continue;

                // If the item is still high in the funnel cone, pull it toward the throat center;
                // once inside the hopper mouth, absorb it directly into the buffer.
                Vector3 localPos = transform.InverseTransformPoint(dropped.transform.position);
                if (localPos.y > 1.85f)
                {
                    var rb = dropped.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                    {
                        Vector3 throatWorld = transform.TransformPoint(new Vector3(0f, 0.85f, 0f));
                        Vector3 toThroat = (throatWorld - dropped.transform.position);
                        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, toThroat.normalized * 6.5f, 0.45f);
                    }
                }
                TryCollectDroppedItem(dropped);
            }
        }

        public bool TryCollectDroppedItem(DroppedItem dropped)
        {
            if (!_userEnabled || dropped == null || dropped.stack == null || dropped.stack.IsEmpty || dropped.stack.item == null)
                return false;

            EnsureContainers();
            var incoming = dropped.stack.Clone();
            var leftover = buffer.Insert(incoming);
            int accepted = dropped.stack.count - (leftover?.count ?? 0);
            if (accepted <= 0) return false;

            dropped.ConsumeCount(accepted);
            _totalCapturedCount += accepted;
            return true;
        }

        private void PushToConnectedOutputs()
        {
            if (buffer == null || buffer.IsEmpty()) return;

            // Scan perimeter cells around the 3x2x3 footprint for belts, chutes, funnels, or chests.
            int hits = Physics.OverlapBoxNonAlloc(
                transform.position + new Vector3(0f, 0.5f, 0f),
                new Vector3(2.15f, 1.25f, 2.15f),
                OverlapBuf,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits; i++)
            {
                var col = OverlapBuf[i];
                OverlapBuf[i] = null;
                if (col == null) continue;
                if (col.transform.IsChildOf(transform)) continue;

                var consumer = col.GetComponentInParent<IItemConsumer>();
                if (consumer != null && !ReferenceEquals(consumer, this))
                {
                    if (TryPushOneToConsumer(consumer)) return;
                }

                var chest = col.GetComponentInParent<Chest>();
                if (chest != null && chest.container != null)
                {
                    if (TryPushStackToContainer(chest.container)) return;
                }
            }
        }

        private bool TryPushOneToConsumer(IItemConsumer consumer)
        {
            for (int s = 0; s < buffer.Size; s++)
            {
                var st = buffer.GetSlot(s);
                if (st == null || st.IsEmpty || st.item == null) continue;
                int cap = consumer.GetInputCapacity(st.item);
                if (cap <= 0) continue;
                int toMove = Mathf.Min(st.count, Mathf.Min(cap, 8));
                int moved = consumer.TryInsert(st.item, toMove);
                if (moved <= 0) continue;
                st.count -= moved;
                buffer.SetSlot(s, st.count > 0 ? st : new ItemStack());
                return true;
            }
            return false;
        }

        private bool TryPushStackToContainer(ItemContainer dest)
        {
            for (int s = 0; s < buffer.Size; s++)
            {
                var st = buffer.GetSlot(s);
                if (st == null || st.IsEmpty || st.item == null) continue;
                int toMove = Mathf.Min(st.count, 16);
                var attempt = st.Clone();
                attempt.count = toMove;
                var leftover = dest.Insert(attempt);
                int moved = toMove - (leftover?.count ?? 0);
                if (moved <= 0) continue;
                st.count -= moved;
                buffer.SetSlot(s, st.count > 0 ? st : new ItemStack());
                return true;
            }
            return false;
        }

        // ── IItemConsumer & IItemProvider ───────────────────────────────────────
        public int GetInputCapacity(ItemDefinition item)
        {
            if (!_userEnabled || item == null) return 0;
            EnsureContainers();
            for (int i = 0; i < buffer.Size; i++)
            {
                var st = buffer.GetSlot(i);
                if (st == null || st.IsEmpty) return ItemStack.MaxItemsPerStack(item);
                if (st.item == item && st.count < ItemStack.MaxItemsPerStack(item))
                    return ItemStack.MaxItemsPerStack(item) - st.count;
            }
            return 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (!_userEnabled || item == null || count <= 0) return 0;
            EnsureContainers();
            var leftover = buffer.Insert(new ItemStack(item, count));
            int inserted = count - (leftover?.count ?? 0);
            if (inserted > 0) _totalCapturedCount += inserted;
            return inserted;
        }

        public ItemDefinition PeekOutput(out int count)
        {
            EnsureContainers();
            for (int i = 0; i < buffer.Size; i++)
            {
                var st = buffer.GetSlot(i);
                if (st != null && !st.IsEmpty && st.item != null)
                {
                    count = st.count;
                    return st.item;
                }
            }
            count = 0;
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            EnsureContainers();
            return buffer.Remove(item, count);
        }

        // ── IItemPortHost ───────────────────────────────────────────────────────
        public PortConfig PortConfig
        {
            get
            {
                if (_portConfig == null)
                {
                    _portConfig = GetComponent<PortConfig>();
                    if (_portConfig == null) _portConfig = gameObject.AddComponent<PortConfig>();
                    _portConfig.EnsureAllFaces();
                }
                return _portConfig;
            }
        }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureContainers();
            if (_portContainers == null || _portContainers.Length != 1)
                _portContainers = new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Captured Items", buffer, canInput: true, canOutput: true);
            return _portContainers;
        }

        // ── Persistence (IMachineProcessState) ──────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            state.SetExtra("userEnabled", _userEnabled ? 1f : 0f);
            state.SetExtra("autoPush", autoPushToNeighbors ? 1f : 0f);
            state.SetExtra("totalCaptured", _totalCapturedCount);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            _userEnabled = state.GetExtra("userEnabled", 1f) >= 0.5f;
            autoPushToNeighbors = state.GetExtra("autoPush", 1f) >= 0.5f;
            _totalCapturedCount = Mathf.Max(0, Mathf.RoundToInt(state.GetExtra("totalCaptured", 0f)));
        }

        // ── Procedural Wide Hopper Visuals & Trigger ────────────────────────────
        private void EnsureCaptureTrigger()
        {
            var trigChild = transform.Find("Generated_CaptureTrigger");
            if (trigChild == null)
            {
                var go = new GameObject("Generated_CaptureTrigger");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = captureCenterOffset;
                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = captureHalfExtents * 2f;
            }
        }

        public void EnsureVisuals()
        {
            if (transform.Find("Generated_ChuteVisuals") != null) return;

            var rootMr = GetComponent<MeshRenderer>();
            if (rootMr != null) rootMr.enabled = false;

            var root = new GameObject("Generated_ChuteVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color baseSteel = new Color(0.20f, 0.23f, 0.27f);
            Color hopperSteel = new Color(0.28f, 0.32f, 0.37f);
            Color throatDark = new Color(0.09f, 0.10f, 0.12f);
            Color hazardYellow = new Color(0.94f, 0.70f, 0.14f);
            Color accentCyan = new Color(0.18f, 0.78f, 0.92f);

            // Base discharge housing (1.8m x 0.75m x 1.8m)
            MakeBox(root, "BasePlinth", new Vector3(0f, -0.62f, 0f), new Vector3(2.2f, 0.72f, 2.2f), Vector3.zero, baseSteel);
            MakeBox(root, "DischargePort_N", new Vector3(0f, -0.65f, 1.12f), new Vector3(0.75f, 0.55f, 0.14f), Vector3.zero, throatDark);
            MakeBox(root, "DischargePort_S", new Vector3(0f, -0.65f, -1.12f), new Vector3(0.75f, 0.55f, 0.14f), Vector3.zero, throatDark);
            MakeBox(root, "DischargePort_E", new Vector3(1.12f, -0.65f, 0f), new Vector3(0.14f, 0.55f, 0.75f), Vector3.zero, throatDark);
            MakeBox(root, "DischargePort_W", new Vector3(-1.12f, -0.65f, 0f), new Vector3(0.14f, 0.55f, 0.75f), Vector3.zero, throatDark);

            // Corner support stanchions
            MakeBox(root, "Leg_NE", new Vector3(1.22f, -0.15f, 1.22f), new Vector3(0.22f, 1.65f, 0.22f), Vector3.zero, baseSteel);
            MakeBox(root, "Leg_NW", new Vector3(-1.22f, -0.15f, 1.22f), new Vector3(0.22f, 1.65f, 0.22f), Vector3.zero, baseSteel);
            MakeBox(root, "Leg_SE", new Vector3(1.22f, -0.15f, -1.22f), new Vector3(0.22f, 1.65f, 0.22f), Vector3.zero, baseSteel);
            MakeBox(root, "Leg_SW", new Vector3(-1.22f, -0.15f, -1.22f), new Vector3(0.22f, 1.65f, 0.22f), Vector3.zero, baseSteel);

            // Wide Flared Hopper Sloped Walls (3m x 3m top opening funneling down into the base)
            MakeBox(root, "Slope_North", new Vector3(0f, 0.30f, 1.08f), new Vector3(2.85f, 1.15f, 0.14f), new Vector3(-28f, 0f, 0f), hopperSteel);
            MakeBox(root, "Slope_South", new Vector3(0f, 0.30f, -1.08f), new Vector3(2.85f, 1.15f, 0.14f), new Vector3(28f, 0f, 0f), hopperSteel);
            MakeBox(root, "Slope_East", new Vector3(1.08f, 0.30f, 0f), new Vector3(0.14f, 1.15f, 2.85f), new Vector3(0f, 0f, 28f), hopperSteel);
            MakeBox(root, "Slope_West", new Vector3(-1.08f, 0.30f, 0f), new Vector3(0.14f, 1.15f, 2.85f), new Vector3(0f, 0f, -28f), hopperSteel);

            // Top Hazard Rim (easy-to-spot target from hovering ships!)
            MakeBox(root, "Rim_North", new Vector3(0f, 0.86f, 1.42f), new Vector3(2.96f, 0.14f, 0.18f), Vector3.zero, hazardYellow);
            MakeBox(root, "Rim_South", new Vector3(0f, 0.86f, -1.42f), new Vector3(2.96f, 0.14f, 0.18f), Vector3.zero, hazardYellow);
            MakeBox(root, "Rim_East", new Vector3(1.42f, 0.86f, 0f), new Vector3(0.18f, 0.14f, 2.68f), Vector3.zero, hazardYellow);
            MakeBox(root, "Rim_West", new Vector3(-1.42f, 0.86f, 0f), new Vector3(0.18f, 0.14f, 2.68f), Vector3.zero, hazardYellow);

            // Target Guidance Beacon Strips inside upper rim
            MakeBox(root, "Guide_N", new Vector3(0f, 0.94f, 1.42f), new Vector3(1.6f, 0.04f, 0.06f), Vector3.zero, accentCyan);
            MakeBox(root, "Guide_S", new Vector3(0f, 0.94f, -1.42f), new Vector3(1.6f, 0.04f, 0.06f), Vector3.zero, accentCyan);
            MakeBox(root, "Guide_E", new Vector3(1.42f, 0.94f, 0f), new Vector3(0.06f, 0.04f, 1.6f), Vector3.zero, accentCyan);
            MakeBox(root, "Guide_W", new Vector3(-1.42f, 0.94f, 0f), new Vector3(0.06f, 0.04f, 1.6f), Vector3.zero, accentCyan);

            // Interior dark throat & cushion grid bars
            MakeBox(root, "ThroatFloor", new Vector3(0f, -0.18f, 0f), new Vector3(1.55f, 0.08f, 1.55f), Vector3.zero, throatDark);
            MakeBox(root, "Baffle_1", new Vector3(-0.45f, 0.35f, 0f), new Vector3(0.08f, 0.08f, 2.1f), Vector3.zero, baseSteel);
            MakeBox(root, "Baffle_2", new Vector3(0.45f, 0.35f, 0f), new Vector3(0.08f, 0.08f, 2.1f), Vector3.zero, baseSteel);
        }

        private static void MakeBox(Transform parent, string name, Vector3 pos, Vector3 scale, Vector3 euler, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null && mr.sharedMaterial == null)
                    mr.sharedMaterial = new Material(shader);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
        }
    }
}
