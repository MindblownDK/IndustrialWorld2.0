// Assets/Scripts/VoxelEngine/Simulation/ConveyorBelt.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CONVEYOR BELT BLOCK                         ║
// ║  Visual belt that carries items from one end to the other.      ║
// ║  Items ride on top of the belt mesh and are visually animated   ║
// ║  along the belt direction. Snaps to grid and to machine ports.  ║
// ║                                                                  ║
// ║  Speed tiers: Basic (2 items/s) → Fast (5) → Express (10).      ║
// ║  Variants: Straight, Corner, Ramp, Vertical (future).           ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>Speed tier determines items-per-second throughput.</summary>
    public enum ConveyorSpeed { Basic, Fast, Express }

    /// <summary>Belt shape variant.</summary>
    public enum ConveyorShape { Straight, Corner, RampUp, RampDown }

    /// <summary>
    /// A single item riding on the belt, with its travel progress.
    /// </summary>
    [System.Serializable]
    public struct ConveyorItem
    {
        public ItemDefinition item;
        public int count;
        /// <summary>0 = entry end, 1 = exit end of this belt segment.</summary>
        public float progress;
        /// <summary>Visual offset from belt centre line (for wide belts).</summary>
        public float lateralOffset;
    }

    public class ConveyorBelt : MonoBehaviour, IItemConsumer, IItemProvider
    {
        // ── Inspector ─────────────────────────────────────────────────

        [Header("Conveyor Configuration")]
        public ConveyorSpeed speed = ConveyorSpeed.Basic;
        public ConveyorShape shape = ConveyorShape.Straight;

        [Header("Capacity")]
        [Tooltip("Maximum items that can ride this belt segment simultaneously.")]
        public int maxItems = 8;

        [Header("Direction")]
        [Tooltip("Local-space direction items travel. Set automatically from shape.")]
        public Vector3 travelDirection = Vector3.forward;

        /// <summary>Which local direction the belt receives items from.</summary>
        public Vector3 entryDirection = Vector3.back;
        /// <summary>Which local direction the belt sends items to.</summary>
        public Vector3 exitDirection = Vector3.forward;

        [Header("Connections")]
        [Tooltip("Auto-detected upstream provider (belt, chute, machine output).")]
        public MonoBehaviour upstreamSource;
        [Tooltip("Auto-detected downstream consumer (belt, chute, machine input).")]
        public MonoBehaviour downstreamTarget;

        // ── Runtime State ─────────────────────────────────────────────

        private readonly List<ConveyorItem> _items = new(16);
        private float _scanTimer;
        private BeltVisualController _visuals;

        /// <summary>Read-only view of items currently on this belt.</summary>
        public IReadOnlyList<ConveyorItem> Items => _items;

        /// <summary>Items per second throughput for the current speed tier.</summary>
        public float ItemsPerSecond => speed switch
        {
            ConveyorSpeed.Fast    => 5f,
            ConveyorSpeed.Express => 10f,
            _                     => 2f
        };

        /// <summary>World-space speed of items riding the belt (m/s).</summary>
        public float BeltSpeed => ItemsPerSecond * 1.2f;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            // Set travel direction based on shape.
            UpdateTravelDirection();

            // Create visual controller.
            _visuals = GetComponent<BeltVisualController>();
            if (_visuals == null) _visuals = gameObject.AddComponent<BeltVisualController>();
            _visuals.Initialize(this);
        }

        private void OnEnable()
        {
            ConveyorNetwork.EnsureInstance();
            ConveyorNetwork.Instance?.Register(this);
            Invoke(nameof(RefreshNearby), 0.1f);
        }

        private void OnDisable()
        {
            ConveyorNetwork.Instance?.Unregister(this);
            RefreshNearby();
        }

        private void RefreshNearby()
        {
            var hits = Physics.OverlapSphere(transform.position, 1.5f);
            foreach (var hit in hits)
            {
                var belt = hit.GetComponentInParent<ConveyorBelt>();
                if (belt != null && belt != this) belt.RefreshShape();
            }
            RefreshShape();
        }

        private void Update()
        {
            // Advance items along the belt.
            float dt = Time.deltaTime;
            float moveStep = BeltSpeed * dt;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var ci = _items[i];
                ci.progress += moveStep;

                // Item reached the end of the belt — try to hand off.
                if (ci.progress >= 1f)
                {
                    if (TryHandOff(ref ci))
                    {
                        _items.RemoveAt(i);
                        continue;
                    }
                    // Clamp at end — item waits for downstream to accept.
                    ci.progress = 1f;
                }

                _items[i] = ci;
            }

            // Periodically scan for upstream/downstream connections.
            _scanTimer += dt;
            if (_scanTimer >= 0.5f)
            {
                _scanTimer = 0f;
                ScanConnections();
            }

            // Pull from upstream if we have capacity.
            if (_items.Count < maxItems && upstreamSource != null)
            {
                TryPullFromUpstream();
            }

            // Update visuals.
            if (_visuals != null) _visuals.UpdateVisuals(_items);
        }

        private void UpdateTravelDirection()
        {
            travelDirection = shape switch
            {
                ConveyorShape.Corner   => Vector3.right,  // simplified — corner handled by visuals
                ConveyorShape.RampUp   => new Vector3(0, 0.5f, 1f).normalized,
                ConveyorShape.RampDown => new Vector3(0, -0.5f, 1f).normalized,
                _                      => Vector3.forward
            };
        }

        // ── IItemConsumer ─────────────────────────────────────────────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            int free = maxItems - _items.Count;
            return Mathf.Max(0, free);
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            if (_items.Count >= maxItems) return 0;

            // Each belt slot holds one "packet" of up to stack-size items.
            int accepted = Mathf.Min(count, maxItems - _items.Count);
            for (int i = 0; i < accepted; i++)
            {
                _items.Add(new ConveyorItem
                {
                    item = item,
                    count = 1,
                    progress = 0f,
                    lateralOffset = Random.Range(-0.15f, 0.15f)
                });
            }
            return accepted;
        }

        // ── IItemProvider ─────────────────────────────────────────────

        public ItemDefinition PeekOutput(out int count)
        {
            count = 0;
            if (_items.Count == 0) return null;

            // Peek at the item closest to the exit end.
            var front = _items[0];
            for (int i = 1; i < _items.Count; i++)
            {
                if (_items[i].progress > front.progress)
                    front = _items[i];
            }

            if (front.progress < 0.9f) { count = 0; return null; }
            count = front.count;
            return front.item;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var ci = _items[i];
                if (ci.item != item || ci.progress < 0.9f) continue;

                int take = Mathf.Min(count, ci.count);
                ci.count -= take;
                count -= take;

                if (ci.count <= 0)
                    _items.RemoveAt(i);
                else
                    _items[i] = ci;

                if (count <= 0) return take;
            }
            return 0;
        }

        // ── Connections ───────────────────────────────────────────────

        private void ScanConnections()
        {
            // Find downstream consumer at the exit end of this belt.
            Vector3 worldExitDir = GetExitDirection();
            Vector3 exitWorld = transform.position + worldExitDir * 1.2f;
            downstreamTarget = FindConsumerAt(exitWorld);

            if (downstreamTarget == null)
                downstreamTarget = FindFunnelAt(exitWorld);

            // Find upstream provider at the entry end of this belt.
            Vector3 worldEntryDir = GetEntryDirection();
            Vector3 entryWorld = transform.position + worldEntryDir * 1.2f;
            upstreamSource = FindProviderAt(entryWorld);

            if (upstreamSource == null)
                upstreamSource = FindFunnelAt(entryWorld);
        }

        public void RefreshShape()
        {
            var upFront = FindConsumerAt(transform.position + transform.forward * 1.0f + Vector3.up * 1.0f);
            var downFront = FindConsumerAt(transform.position + transform.forward * 1.0f + Vector3.down * 1.0f);

            if (upFront != null)
            {
                shape = ConveyorShape.RampUp;
                entryDirection = Vector3.back;
                exitDirection = new Vector3(0, 0.5f, 1).normalized;
            }
            else if (downFront != null)
            {
                shape = ConveyorShape.RampDown;
                entryDirection = Vector3.back;
                exitDirection = new Vector3(0, -0.5f, 1).normalized;
            }
            else
            {
                var left = FindProviderAt(transform.position - transform.right * 1.0f);
                var right = FindProviderAt(transform.position + transform.right * 1.0f);
                var back = FindProviderAt(transform.position - transform.forward * 1.0f);

                if (back != null || (left == null && right == null))
                {
                    shape = ConveyorShape.Straight;
                    entryDirection = Vector3.back;
                    exitDirection = Vector3.forward;
                }
                else if (left != null)
                {
                    shape = ConveyorShape.Corner;
                    entryDirection = Vector3.left;
                    exitDirection = Vector3.forward;
                }
                else if (right != null)
                {
                    shape = ConveyorShape.Corner;
                    entryDirection = Vector3.right;
                    exitDirection = Vector3.forward;
                }
            }

            UpdateTravelDirection();
            if (_visuals != null) _visuals.RebuildMesh();
        }

        private MonoBehaviour FindFunnelAt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, 0.8f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var funnel = col.GetComponentInParent<Funnel>();
                if (funnel != null) return funnel;
            }
            return null;
        }

        private MonoBehaviour FindConsumerAt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, 0.8f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var consumer = col.GetComponentInParent<MonoBehaviour>() as IItemConsumer;
                if (consumer != null) return consumer as MonoBehaviour;
            }
            return null;
        }

        private MonoBehaviour FindProviderAt(Vector3 worldPos)
        {
            var hits = Physics.OverlapSphere(worldPos, 0.8f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var provider = col.GetComponentInParent<MonoBehaviour>() as IItemProvider;
                if (provider != null) return provider as MonoBehaviour;
            }
            return null;
        }

        private bool TryHandOff(ref ConveyorItem ci)
        {
            if (downstreamTarget == null) return false;

            var consumer = downstreamTarget as IItemConsumer;
            if (consumer == null) return false;

            int cap = consumer.GetInputCapacity(ci.item);
            if (cap <= 0) return false;

            int sent = Mathf.Min(cap, ci.count);
            int accepted = consumer.TryInsert(ci.item, sent);
            ci.count -= accepted;
            return ci.count <= 0;
        }

        private void TryPullFromUpstream()
        {
            var provider = upstreamSource as IItemProvider;
            if (provider == null) return;

            var item = provider.PeekOutput(out int available);
            if (item == null || available <= 0) return;
            if (_items.Count >= maxItems) return;

            int want = Mathf.Min(available, maxItems - _items.Count);
            int got = provider.TryExtract(item, want);

            for (int i = 0; i < got; i++)
            {
                _items.Add(new ConveyorItem
                {
                    item = item,
                    count = 1,
                    progress = 0f,
                    lateralOffset = Random.Range(-0.15f, 0.15f)
                });
            }
        }

        // ── World-space position of a riding item (for visuals) ───────

        /// <summary>
        /// Returns the world-space position of an item at a given progress (0-1)
        /// along this belt segment. Handles straight, corner (90° curve),
        /// and slope (ramp up/down) shapes.
        /// </summary>
        public Vector3 GetWorldPosition(float progress, float lateralOffset = 0f)
        {
            float t = Mathf.Clamp01(progress);
            Vector3 localPos;

            switch (shape)
            {
                case ConveyorShape.Corner:
                    // 90° turn: items follow a quarter-circle arc from forward to right.
                    float angle = t * Mathf.PI * 0.5f; // 0 → 90°
                    float radius = 0.5f;
                    localPos = new Vector3(
                        Mathf.Sin(angle) * radius,
                        0.52f,
                        Mathf.Cos(angle) * radius
                    );
                    localPos += transform.right * lateralOffset;
                    break;

                case ConveyorShape.RampUp:
                    // Ramp going up: forward + upward.
                    localPos = new Vector3(
                        0f,
                        0.52f + t * 0.5f, // rises 0.5m over the belt length
                        -0.5f + t * 1.0f  // travels 1m forward
                    );
                    localPos += transform.right * lateralOffset;
                    break;

                case ConveyorShape.RampDown:
                    // Ramp going down: forward + downward.
                    localPos = new Vector3(
                        0f,
                        0.52f - t * 0.5f, // drops 0.5m over the belt length
                        -0.5f + t * 1.0f  // travels 1m forward
                    );
                    localPos += transform.right * lateralOffset;
                    break;

                default: // Straight
                    localPos = new Vector3(
                        lateralOffset,
                        0.52f,
                        -0.5f + t * 1.0f
                    );
                    break;
            }

            return transform.TransformPoint(localPos);
        }

        /// <summary>
        /// Returns the world-space EXIT direction for this belt shape.
        /// Used by ScanConnections to find the downstream target.
        /// </summary>
        public Vector3 GetExitDirection()
        {
            switch (shape)
            {
                case ConveyorShape.Corner:
                    return transform.right; // exits to the right
                case ConveyorShape.RampUp:
                    return transform.TransformDirection(new Vector3(0, 0.5f, 1f).normalized);
                case ConveyorShape.RampDown:
                    return transform.TransformDirection(new Vector3(0, -0.5f, 1f).normalized);
                default:
                    return transform.forward;
            }
        }

        /// <summary>
        /// Returns the world-space ENTRY direction for this belt shape.
        /// Used by ScanConnections to find the upstream source.
        /// </summary>
        public Vector3 GetEntryDirection()
        {
            switch (shape)
            {
                case ConveyorShape.Corner:
                    return -transform.forward; // enters from behind
                case ConveyorShape.RampUp:
                    return transform.TransformDirection(new Vector3(0, -0.5f, -1f).normalized);
                case ConveyorShape.RampDown:
                    return transform.TransformDirection(new Vector3(0, 0.5f, -1f).normalized);
                default:
                    return -transform.forward;
            }
        }
    }
}
