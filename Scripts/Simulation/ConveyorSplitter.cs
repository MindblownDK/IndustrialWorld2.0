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

namespace VoxelEngine.Simulation
{
    /// <summary>Splitter tier determines the number of output lanes.</summary>
    public enum SplitterTier { Mk1, Mk2, Mk3 }

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

        private readonly List<ConveyorItem> _buffer = new(8);
        private float _transferTimer;
        private float _scanTimer;
        private int _roundRobinIndex;
        private bool _initialized;

        /// <summary>Number of output lanes for the current tier.</summary>
        public int OutputCount => tier switch
        {
            SplitterTier.Mk3 => 4,
            SplitterTier.Mk2 => 3,
            _ => 2
        };

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            EnsureOutputDirections();
        }

        private void OnEnable()
        {
            EnsureOutputDirections();
            ScanConnections();
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
            outputDirections.Add(Vector3.forward);  // always forward

            if (OutputCount >= 3)
                outputDirections.Add(Vector3.left);

            if (OutputCount >= 4)
                outputDirections.Add(Vector3.right);

            if (OutputCount >= 2 && OutputCount < 3)
                outputDirections.Add(Vector3.right); // MK1: forward + right

            // For MK3 with 4 outputs, the 4th is backward-wrap (behind the splitter)
            if (OutputCount >= 4 && outputDirections.Count < 4)
                outputDirections.Add(Vector3.back);

            // Trim to exact count
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

            // Try each output starting from the current round-robin index.
            int outputsTried = 0;
            int maxAttempts = outputTargets.Count;

            while (outputsTried < maxAttempts && _buffer.Count > 0)
            {
                int idx = _roundRobinIndex % outputTargets.Count;
                var target = outputTargets[idx];

                if (target != null)
                {
                    var consumer = target as IItemConsumer;
                    if (consumer != null)
                    {
                        var frontItem = _buffer[0];
                        int cap = consumer.GetInputCapacity(frontItem.item);
                        if (cap > 0)
                        {
                            int sent = consumer.TryInsert(frontItem.item, 1);
                            if (sent > 0)
                            {
                                _buffer.RemoveAt(0);
                                _roundRobinIndex = (idx + 1) % outputTargets.Count;
                                break; // One item sent, advance round-robin
                            }
                        }
                    }
                }

                _roundRobinIndex = (idx + 1) % outputTargets.Count;
                outputsTried++;
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
    }
}
