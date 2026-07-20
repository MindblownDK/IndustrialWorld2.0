// Assets/Scripts/VoxelEngine/Simulation/ConveyorSplitter.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CONVEYOR SPLITTER BLOCK                     ║
// ║  Accepts items from one input direction and distributes them     ║
// ║  evenly across multiple output belts in round-robin fashion.     ║
// ║                                                                  ║
// ║  MK1: 2 outputs | MK2: 3 outputs | MK3: 4 outputs               ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Storage;

namespace VoxelEngine.Simulation
{
    /// <summary>Splitter tier determines the number of output lanes.</summary>
    public enum SplitterTier { Mk1, Mk2, Mk3 }

    /// <summary>How the splitter chooses among eligible output lanes.</summary>
    public enum SplitterRoutingMode { RoundRobin, NearestFirst }

    /// <summary>
    /// Directional splitter that accepts items from one input belt and
    /// distributes them round-robin across 2–4 output belts.
    /// Place inline on a belt line; items enter from the back (local -Z)
    /// and split forward, left, right, and (Mk3) backward-wrap outputs.
    /// </summary>
    public class ConveyorSplitter : MonoBehaviour, IItemConsumer, IItemProvider
    {
        [Header("Splitter Configuration")]
        public SplitterTier tier = SplitterTier.Mk1;

        [Tooltip("How the splitter chooses among valid output lanes.")]
        public SplitterRoutingMode routingMode = SplitterRoutingMode.RoundRobin;

        [Tooltip("Seconds between each transfer attempt.")]
        public float transferInterval = 0.25f;

        [Tooltip("Maximum items buffered inside the splitter.")]
        public int bufferSize = 8;

        [Header("Directions")]
        [Tooltip("Local direction items enter from.")]
        public Vector3 inputDirection = Vector3.back;

        [Tooltip("Local directions items can exit to. Populated automatically from tier.")]
        public List<Vector3> outputDirections = new();

        [Header("Connections (auto-detected)")]
        public MonoBehaviour inputSource;
        public List<MonoBehaviour> outputTargets = new();

        // ── Runtime ───────────────────────────────────────────────────

        private static Material _inputArrowMaterial;
        private static Material _outputArrowMaterial;

        private readonly List<ConveyorItem> _buffer = new(8);
        private readonly List<ItemDefinition> _outputFilterItems = new(4);
        private readonly List<FilterSlotContainer> _filterSlots = new(4);
        private float _transferTimer;
        private float _scanTimer;
        private int _roundRobinIndex;
        private bool _initialized;

        /// <summary>Number of output lanes for the current tier.</summary>
        public int OutputCount => tier switch
        {
            SplitterTier.Mk3 => 3,
            SplitterTier.Mk2 => 3,
            _ => 2
        };

        public SplitterRoutingMode RoutingMode => routingMode;

        /// <summary>Current buffered conveyor packets inside the splitter.</summary>
        public IReadOnlyList<ConveyorItem> BufferItems => _buffer;

        /// <summary>Number of buffered packets currently waiting inside the splitter.</summary>
        public int BufferedCount => _buffer.Count;

        /// <summary>How many output lanes are currently connected to consumers.</summary>
        public int ConnectedOutputCount
        {
            get
            {
                int count = 0;
                if (outputTargets == null) return 0;
                for (int i = 0; i < outputTargets.Count; i++)
                    if (outputTargets[i] != null) count++;
                return count;
            }
        }

        /// <summary>Current round-robin lane index for persistence/debugging.</summary>
        public int RoundRobinIndex => _roundRobinIndex;

        public IItemContainer GetOutputFilterSlot(int outputIndex)
        {
            EnsureOutputDirections();
            EnsureOutputFilterSlots();
            return outputIndex >= 0 && outputIndex < _filterSlots.Count ? _filterSlots[outputIndex] : null;
        }

        public ItemDefinition GetOutputFilterItem(int outputIndex)
        {
            EnsureOutputFilterSlots();
            return outputIndex >= 0 && outputIndex < _outputFilterItems.Count ? _outputFilterItems[outputIndex] : null;
        }

        public void SetOutputFilterItem(int outputIndex, ItemDefinition item)
        {
            EnsureOutputFilterSlots();
            if (outputIndex < 0 || outputIndex >= _outputFilterItems.Count) return;
            _outputFilterItems[outputIndex] = item;
            if (outputIndex < _filterSlots.Count) _filterSlots[outputIndex]?.RaiseChanged();
        }

        public void ClearOutputFilter(int outputIndex)
        {
            SetOutputFilterItem(outputIndex, null);
        }

        public string GetOutputLabel(int outputIndex)
        {
            EnsureOutputDirections();
            if (outputIndex < 0 || outputIndex >= outputDirections.Count) return $"Output {outputIndex + 1}";
            Vector3 dir = outputDirections[outputIndex];
            if (Vector3.Dot(dir, Vector3.forward) > 0.9f) return "Forward";
            if (Vector3.Dot(dir, Vector3.back) > 0.9f) return "Back";
            if (Vector3.Dot(dir, Vector3.left) > 0.9f) return "Left";
            if (Vector3.Dot(dir, Vector3.right) > 0.9f) return "Right";
            return $"Output {outputIndex + 1}";
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            EnsureOutputDirections();
            EnsureOutputFilterSlots();
            RefreshDirectionVisuals();
        }

        /// <summary>Restores additive runtime state without changing authored tuning.</summary>
        public void RestorePersistentState(IEnumerable<ConveyorItem> savedItems, int roundRobinIndex, SplitterRoutingMode restoredMode, IList<ItemDefinition> restoredFilters)
        {
            EnsureOutputDirections();
            EnsureOutputFilterSlots();
            _buffer.Clear();
            if (savedItems != null)
            {
                foreach (var saved in savedItems)
                {
                    if (saved.item == null || saved.count <= 0 || _buffer.Count >= bufferSize) continue;
                    var restored = saved;
                    restored.progress = Mathf.Clamp01(restored.progress);
                    _buffer.Add(restored);
                }
            }

            routingMode = restoredMode;
            for (int i = 0; i < _outputFilterItems.Count; i++)
                _outputFilterItems[i] = restoredFilters != null && i < restoredFilters.Count ? restoredFilters[i] : null;
            for (int i = 0; i < _filterSlots.Count; i++)
                _filterSlots[i]?.RaiseChanged();

            _roundRobinIndex = OutputCount > 0
                ? Mathf.Abs(roundRobinIndex) % OutputCount
                : 0;
        }

        private void OnEnable()
        {
            EnsureOutputDirections();
            EnsureOutputFilterSlots();
            ScanConnections();
            RefreshDirectionVisuals();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            _scanTimer += dt;
            if (_scanTimer >= 0.5f)
            {
                _scanTimer = 0f;
                ScanConnections();
                EnsureOutputDirections();
                EnsureOutputFilterSlots();
            }

            // Pull from upstream into buffer.
            PullFromUpstream();

            // Push from buffer to outputs in round-robin.
            _transferTimer += dt;
            if (_transferTimer >= transferInterval)
            {
                _transferTimer -= transferInterval;
                PushToOutputs();
            }
        }

        // ── Output Direction Setup ────────────────────────────────────

        private void EnsureOutputDirections()
        {
            if (_initialized && outputDirections.Count == OutputCount) return;

            outputDirections.Clear();
            outputDirections.Add(Vector3.forward);

            // Factory scope: one splitter family, no shape variants. Output lanes are:
            // Mk1 = forward + one side lane (right preferred, left fallback in ScanConnections)
            // Mk2 = forward + left + right
            // Mk3 = forward + left + right + back
            if (OutputCount == 2)
            {
                outputDirections.Add(Vector3.right);
            }
            else if (OutputCount == 3)
            {
                outputDirections.Add(Vector3.left);
                outputDirections.Add(Vector3.right);
            }
            else if (OutputCount >= 4)
            {
                outputDirections.Add(Vector3.left);
                outputDirections.Add(Vector3.right);
                outputDirections.Add(Vector3.back);
            }

            while (outputDirections.Count > OutputCount)
                outputDirections.RemoveAt(outputDirections.Count - 1);

            _initialized = true;
        }

        // ── Connection Scanning ───────────────────────────────────────

        private void ScanConnections()
        {
            // Find upstream provider at input side.
            Vector3 inputWorldDir = transform.TransformDirection(inputDirection.normalized);
            Vector3 inputPos = transform.position + inputWorldDir * 0.8f;
            inputSource = FindProviderAt(inputPos);

            // Find downstream consumers at each output side.
            outputTargets.Clear();
            foreach (var localDir in outputDirections)
            {
                Vector3 worldDir = transform.TransformDirection(localDir.normalized);
                Vector3 outputPos = transform.position + worldDir * 0.8f;
                var consumer = FindConsumerAt(outputPos);
                outputTargets.Add(consumer);
            }

            // Mk1 quality-of-life: if the preferred right lane is empty but the player
            // built the second lane on the left side, adopt that left lane instead of
            // leaving the splitter half-disconnected.
            if (OutputCount == 2 && outputTargets.Count >= 2 && outputTargets[1] == null)
            {
                Vector3 fallbackDir = transform.TransformDirection(Vector3.left);
                Vector3 fallbackPos = transform.position + fallbackDir * 0.8f;
                var fallbackConsumer = FindConsumerAt(fallbackPos);
                if (fallbackConsumer != null)
                {
                    outputDirections[1] = Vector3.left;
                    outputTargets[1] = fallbackConsumer;
                }
                else
                {
                    outputDirections[1] = Vector3.right;
                }
            }

            RefreshDirectionVisuals();
        }

        private MonoBehaviour FindProviderAt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, 0.8f);
            MonoBehaviour nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
                foreach (var mb in behaviours)
                {
                    if (mb == null || mb == this) continue;
                    if (!(mb is IItemProvider)) continue;
                    float dist = (mb.transform.position - worldPos).sqrMagnitude;
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = mb;
                    }
                }
            }
            return nearest;
        }

        private MonoBehaviour FindConsumerAt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, 0.8f);
            MonoBehaviour nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
                foreach (var mb in behaviours)
                {
                    if (mb == null || mb == this) continue;
                    if (!(mb is IItemConsumer)) continue;
                    float dist = (mb.transform.position - worldPos).sqrMagnitude;
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = mb;
                    }
                }
            }
            return nearest;
        }

        // ── Transfer Logic ────────────────────────────────────────────

        private void EnsureOutputFilterSlots()
        {
            while (_outputFilterItems.Count < OutputCount) _outputFilterItems.Add(null);
            while (_outputFilterItems.Count > OutputCount) _outputFilterItems.RemoveAt(_outputFilterItems.Count - 1);

            while (_filterSlots.Count < OutputCount) _filterSlots.Add(new FilterSlotContainer(this, _filterSlots.Count));
            while (_filterSlots.Count > OutputCount) _filterSlots.RemoveAt(_filterSlots.Count - 1);
        }

        private void RefreshDirectionVisuals()
        {
            var backRim = transform.Find("Generated_OutputRimBack");
            if (backRim != null) backRim.gameObject.SetActive(OutputCount >= 4);

            var arrowsRoot = transform.Find("Runtime_IOArrows");
            if (arrowsRoot != null) Destroy(arrowsRoot.gameObject);
            arrowsRoot = new GameObject("Runtime_IOArrows").transform;
            arrowsRoot.SetParent(transform, false);

            CreateArrow(arrowsRoot, "Input", inputDirection.normalized, true, GetArrowMaterial(true));
            for (int i = 0; i < outputDirections.Count; i++)
                CreateArrow(arrowsRoot, $"Output_{i}", outputDirections[i].normalized, false, GetArrowMaterial(false));
        }

        private static Material GetArrowMaterial(bool input)
        {
            if (input)
            {
                if (_inputArrowMaterial == null)
                {
                    Color color = new Color(0.95f, 0.62f, 0.18f);
                    _inputArrowMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    _inputArrowMaterial.color = color;
                    if (_inputArrowMaterial.HasProperty("_BaseColor")) _inputArrowMaterial.SetColor("_BaseColor", color);
                    if (_inputArrowMaterial.HasProperty("_EmissionColor")) { _inputArrowMaterial.EnableKeyword("_EMISSION"); _inputArrowMaterial.SetColor("_EmissionColor", color * 0.8f); }
                }
                return _inputArrowMaterial;
            }

            if (_outputArrowMaterial == null)
            {
                Color color = new Color(0.18f, 0.72f, 0.88f);
                _outputArrowMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                _outputArrowMaterial.color = color;
                if (_outputArrowMaterial.HasProperty("_BaseColor")) _outputArrowMaterial.SetColor("_BaseColor", color);
                if (_outputArrowMaterial.HasProperty("_EmissionColor")) { _outputArrowMaterial.EnableKeyword("_EMISSION"); _outputArrowMaterial.SetColor("_EmissionColor", color * 0.8f); }
            }
            return _outputArrowMaterial;
        }

        private void CreateArrow(Transform parent, string name, Vector3 localDirection, bool input, Material mat)
        {
            if (localDirection.sqrMagnitude < 0.01f) localDirection = Vector3.forward;
            localDirection.Normalize();
            Vector3 basePos = localDirection * 0.42f + Vector3.up * 0.67f;
            Vector3 inward = input ? -localDirection : localDirection;
            Vector3 side = Vector3.Cross(Vector3.up, inward).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Quaternion rot = Quaternion.LookRotation(inward, Vector3.up);

            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stem.name = $"Runtime_IOArrow_{name}_Stem";
            stem.transform.SetParent(parent, false);
            stem.transform.localPosition = basePos - inward * 0.08f;
            stem.transform.localRotation = rot;
            stem.transform.localScale = new Vector3(0.06f, 0.02f, 0.18f);
            Destroy(stem.GetComponent<Collider>());
            stem.GetComponent<Renderer>().sharedMaterial = mat;

            GameObject headA = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headA.name = $"Runtime_IOArrow_{name}_HeadA";
            headA.transform.SetParent(parent, false);
            headA.transform.localPosition = basePos + inward * 0.06f + side * 0.045f;
            headA.transform.localRotation = Quaternion.LookRotation((inward - side * 0.65f).normalized, Vector3.up);
            headA.transform.localScale = new Vector3(0.045f, 0.02f, 0.12f);
            Destroy(headA.GetComponent<Collider>());
            headA.GetComponent<Renderer>().sharedMaterial = mat;

            GameObject headB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headB.name = $"Runtime_IOArrow_{name}_HeadB";
            headB.transform.SetParent(parent, false);
            headB.transform.localPosition = basePos + inward * 0.06f - side * 0.045f;
            headB.transform.localRotation = Quaternion.LookRotation((inward + side * 0.65f).normalized, Vector3.up);
            headB.transform.localScale = new Vector3(0.045f, 0.02f, 0.12f);
            Destroy(headB.GetComponent<Collider>());
            headB.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private bool OutputAcceptsItem(int outputIndex, ItemDefinition item)
        {
            ItemDefinition filter = GetOutputFilterItem(outputIndex);
            return filter == null || filter == item;
        }

        private float OutputDistanceSqr(int outputIndex)
        {
            if (outputIndex < 0 || outputIndex >= outputTargets.Count || outputTargets[outputIndex] == null) return float.MaxValue;
            Vector3 localDir = outputIndex < outputDirections.Count ? outputDirections[outputIndex] : Vector3.forward;
            Vector3 outputPos = transform.position + transform.TransformDirection(localDir.normalized) * 0.8f;
            return (outputTargets[outputIndex].transform.position - outputPos).sqrMagnitude;
        }

        private void PullFromUpstream()
        {
            if (_buffer.Count >= bufferSize || inputSource == null) return;

            var provider = inputSource as IItemProvider;
            if (provider == null) return;

            var item = provider.PeekOutput(out int available);
            if (item == null || available <= 0) return;

            int want = Mathf.Min(available, bufferSize - _buffer.Count);
            int got = provider.TryExtract(item, want);

            for (int i = 0; i < got; i++)
            {
                _buffer.Add(new ConveyorItem
                {
                    item = item,
                    count = 1,
                    progress = 0f,
                    lateralOffset = 0f
                });
            }
        }

        private void PushToOutputs()
        {
            if (_buffer.Count == 0 || outputTargets.Count == 0) return;
            if (_buffer[0].item == null) return;

            if (routingMode == SplitterRoutingMode.NearestFirst)
            {
                PushNearestFirst();
                return;
            }

            // Round-robin: try each eligible output starting from the current cursor.
            int outputsTried = 0;
            int maxAttempts = outputTargets.Count;

            while (outputsTried < maxAttempts && _buffer.Count > 0)
            {
                int idx = _roundRobinIndex % outputTargets.Count;
                var target = outputTargets[idx];
                var frontItem = _buffer[0];

                if (target is IItemConsumer consumer && OutputAcceptsItem(idx, frontItem.item))
                {
                    int cap = consumer.GetInputCapacity(frontItem.item);
                    if (cap > 0)
                    {
                        int sent = consumer.TryInsert(frontItem.item, 1);
                        if (sent > 0)
                        {
                            _buffer.RemoveAt(0);
                            _roundRobinIndex = (idx + 1) % outputTargets.Count;
                            return;
                        }
                    }
                }

                _roundRobinIndex = (idx + 1) % outputTargets.Count;
                outputsTried++;
            }
        }

        private void PushNearestFirst()
        {
            if (_buffer.Count == 0) return;

            var frontItem = _buffer[0];
            int bestIndex = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < outputTargets.Count; i++)
            {
                if (!(outputTargets[i] is IItemConsumer consumer)) continue;
                if (!OutputAcceptsItem(i, frontItem.item)) continue;
                if (consumer.GetInputCapacity(frontItem.item) <= 0) continue;

                float distance = OutputDistanceSqr(i);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return;
            if (outputTargets[bestIndex] is not IItemConsumer bestConsumer) return;

            int moved = bestConsumer.TryInsert(frontItem.item, 1);
            if (moved > 0)
            {
                _buffer.RemoveAt(0);
                _roundRobinIndex = outputTargets.Count > 0 ? bestIndex % outputTargets.Count : 0;
            }
        }

        // ── IItemConsumer ─────────────────────────────────────────────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            return Mathf.Max(0, bufferSize - _buffer.Count);
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int accepted = Mathf.Min(count, bufferSize - _buffer.Count);
            for (int i = 0; i < accepted; i++)
            {
                _buffer.Add(new ConveyorItem
                {
                    item = item,
                    count = 1,
                    progress = 0f,
                    lateralOffset = 0f
                });
            }
            return accepted;
        }

        // ── IItemProvider (for upstream pulling by belts) ─────────────

        public ItemDefinition PeekOutput(out int count)
        {
            count = 0;
            if (_buffer.Count == 0) return null;
            count = _buffer[0].count;
            return _buffer[0].item;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int remaining = count;
            int extracted = 0;
            for (int i = _buffer.Count - 1; i >= 0; i--)
            {
                if (_buffer[i].item != item) continue;
                int take = Mathf.Min(remaining, _buffer[i].count);
                var ci = _buffer[i];
                ci.count -= take;
                remaining -= take;
                extracted += take;
                if (ci.count <= 0)
                    _buffer.RemoveAt(i);
                else
                    _buffer[i] = ci;
                if (remaining <= 0) break;
            }
            return extracted;
        }

        [System.Serializable]
        private sealed class FilterSlotContainer : IItemFilterSlot
        {
            private readonly ConveyorSplitter _owner;
            private readonly int _outputIndex;
            private readonly List<ItemStack> _slots = new() { new ItemStack() };
            public string Name => $"Output {_outputIndex + 1} Filter";
            public IReadOnlyList<ItemStack> Slots { get { Sync(); return _slots; } }
            public event System.Action OnChanged;

            public FilterSlotContainer(ConveyorSplitter owner, int outputIndex)
            {
                _owner = owner;
                _outputIndex = outputIndex;
            }

            public ItemStack Insert(ItemStack stack)
            {
                if (stack == null || stack.IsEmpty) return null;
                ApplyFilter(stack.item);
                return stack;
            }

            public int Remove(ItemDefinition item, int count)
            {
                if (_owner.GetOutputFilterItem(_outputIndex) == item)
                {
                    _owner.ClearOutputFilter(_outputIndex);
                    RaiseChanged();
                }
                return 0;
            }

            public int CountOf(ItemDefinition item) => _owner.GetOutputFilterItem(_outputIndex) == item ? 1 : 0;
            public void SetSlot(int index, ItemStack stack) => ApplyFilter(stack == null || stack.IsEmpty ? null : stack.item);
            public void ApplyFilter(ItemDefinition item)
            {
                _owner.SetOutputFilterItem(_outputIndex, item);
            }
            public ItemStack GetSlot(int index)
            {
                var item = _owner.GetOutputFilterItem(_outputIndex);
                return item != null ? new ItemStack(item, 1) : new ItemStack();
            }
            public void RaiseChanged() { Sync(); OnChanged?.Invoke(); }
            private void Sync() => _slots[0] = GetSlot(0);
        }
    }
}
