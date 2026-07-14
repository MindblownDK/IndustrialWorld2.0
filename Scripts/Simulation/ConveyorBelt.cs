// Assets/Scripts/VoxelEngine/Simulation/ConveyorBelt.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CONVEYOR BELT BLOCK                         ║
// ║  Visual belt that carries items from one end to the other.      ║
// ║  Items ride on top of the belt mesh and are visually animated   ║
// ║  along the belt direction. Snaps to grid and to machine ports.  ║
// ║                                                                  ║
// ║  Speed tiers: Basic (2 items/s) → Fast (5) → Express (10).      ║
// ║  Runtime shapes: Straight, Corner, Ramp Up, Ramp Down.          ║
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
        private static readonly Vector3[] LocalSocketDirections =
        {
            Vector3.back, Vector3.forward, Vector3.left, Vector3.right
        };
        private const float SocketToleranceSqr = 0.09f;

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
            RefreshNearby();
            Invoke(nameof(RefreshNearby), 0.02f);
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(RefreshNearby));
            ConveyorNetwork.Instance?.Unregister(this);

            // Do not rebuild this belt's visuals while Unity is disabling the
            // GameObject (ghost hide, scene close, destroy). Creating/parenting
            // MeshRoot during activation/deactivation triggers Unity warnings and
            // leaked MeshRoot objects. Only active neighbours need a refresh.
            NotifyNearbyBelts();
        }

        private void RefreshNearby()
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return;
            NotifyNearbyBelts();
            RefreshShape();
        }

        internal void RefreshTopologyImmediate()
        {
            RefreshNearby();
        }

        private void NotifyNearbyBelts()
        {
            var hits = Physics.OverlapSphere(transform.position, 1.5f);
            foreach (var hit in hits)
            {
                var belt = hit.GetComponentInParent<ConveyorBelt>();
                if (belt != null && belt != this && belt.isActiveAndEnabled && belt.gameObject.activeInHierarchy)
                    belt.RefreshShape();
            }
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
            switch (shape)
            {
                case ConveyorShape.RampUp:
                    entryDirection = new Vector3(0f, -0.5f, -1f).normalized;
                    exitDirection = new Vector3(0f, 0.5f, 1f).normalized;
                    break;
                case ConveyorShape.RampDown:
                    entryDirection = new Vector3(0f, 0.5f, -1f).normalized;
                    exitDirection = new Vector3(0f, -0.5f, 1f).normalized;
                    break;
                case ConveyorShape.Straight:
                    if (entryDirection.sqrMagnitude < 0.01f || exitDirection.sqrMagnitude < 0.01f
                        || Vector3.Dot(entryDirection.normalized, exitDirection.normalized) > -0.9f)
                    {
                        entryDirection = Vector3.back;
                        exitDirection = Vector3.forward;
                    }
                    break;
            }

            travelDirection = (exitDirection - entryDirection).normalized;
            if (travelDirection.sqrMagnitude < 0.01f) travelDirection = Vector3.forward;
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

            int remaining = count;
            int extracted = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var ci = _items[i];
                if (ci.item != item || ci.progress < 0.9f) continue;

                int take = Mathf.Min(remaining, ci.count);
                ci.count -= take;
                remaining -= take;
                extracted += take;

                if (ci.count <= 0)
                    _items.RemoveAt(i);
                else
                    _items[i] = ci;

                if (remaining <= 0) break;
            }
            return extracted;
        }

        // ── Connections ───────────────────────────────────────────────

        private void ScanConnections()
        {
            // Find downstream consumer at the exit end of this belt.
            Vector3 worldExitDir = GetExitDirection();
            Vector3 exitWorld = transform.position + worldExitDir * 1.2f;
            Vector3 exitSocket = transform.position + worldExitDir * 0.5f;
            downstreamTarget = FindConsumerAt(exitWorld, exitSocket, -worldExitDir);

            // Find upstream provider at the entry end of this belt.
            Vector3 worldEntryDir = GetEntryDirection();
            Vector3 entryWorld = transform.position + worldEntryDir * 1.2f;
            Vector3 entrySocket = transform.position + worldEntryDir * 0.5f;
            upstreamSource = FindProviderAt(entryWorld, entrySocket, -worldEntryDir);
        }

        public void RefreshShape()
        {
            int incomingCount = 0;
            int outgoingCount = 0;
            Vector3 incomingDirection = Vector3.back;
            Vector3 outgoingDirection = Vector3.forward;

            foreach (var localDirection in LocalSocketDirections)
            {
                Vector3 worldDirection = transform.TransformDirection(localDirection).normalized;
                Vector3 neighbourCenter = transform.position + worldDirection;
                Vector3 localSocket = transform.position + worldDirection * 0.5f;

                if (FindConnectedBeltProvider(neighbourCenter, localSocket, -worldDirection) != null)
                {
                    incomingCount++;
                    incomingDirection = localDirection;
                }

                if (FindConnectedBeltConsumer(neighbourCenter, localSocket, -worldDirection) != null)
                {
                    outgoingCount++;
                    outgoingDirection = localDirection;
                }
            }

            // Shape only when the topology is unambiguous: one belt enters and one
            // belt leaves. Junction attempts or loose ends stay straight so parallel
            // lanes and temporary placement states never create surprise corners.
            if (incomingCount == 1 && outgoingCount == 1
                && Vector3.Dot(incomingDirection, outgoingDirection) < -0.9f)
            {
                shape = ConveyorShape.Straight;
                entryDirection = incomingDirection;
                exitDirection = outgoingDirection;
            }
            else if (incomingCount == 1 && outgoingCount == 1
                     && Mathf.Abs(Vector3.Dot(incomingDirection, outgoingDirection)) < 0.1f)
            {
                shape = ConveyorShape.Corner;
                entryDirection = incomingDirection;
                exitDirection = outgoingDirection;
            }
            else
            {
                shape = ConveyorShape.Straight;
                entryDirection = Vector3.back;
                exitDirection = Vector3.forward;
            }

            UpdateTravelDirection();
            ScanConnections();
            if (_visuals != null && isActiveAndEnabled && gameObject.activeInHierarchy)
                _visuals.RebuildMesh();
        }

        private ConveyorBelt FindConnectedBeltProvider(Vector3 probePosition, Vector3 receiverSocket, Vector3 expectedOutputDirection)
        {
            var hits = Physics.OverlapSphere(probePosition, 0.7f);
            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var belt = col.GetComponentInParent<ConveyorBelt>();
                if (belt == null || belt == this) continue;

                Vector3 candidateDirection = belt.GetExitDirection();
                Vector3 candidateSocket = belt.transform.position + candidateDirection * 0.5f;
                if (Vector3.Dot(candidateDirection, expectedOutputDirection.normalized) < 0.9f) continue;
                if ((candidateSocket - receiverSocket).sqrMagnitude > SocketToleranceSqr) continue;
                return belt;
            }
            return null;
        }

        private ConveyorBelt FindConnectedBeltConsumer(Vector3 probePosition, Vector3 providerSocket, Vector3 expectedInputDirection)
        {
            var hits = Physics.OverlapSphere(probePosition, 0.7f);
            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var belt = col.GetComponentInParent<ConveyorBelt>();
                if (belt == null || belt == this) continue;

                Vector3 candidateDirection = belt.GetEntryDirection();
                Vector3 candidateSocket = belt.transform.position + candidateDirection * 0.5f;
                if (Vector3.Dot(candidateDirection, expectedInputDirection.normalized) < 0.9f) continue;
                if ((candidateSocket - providerSocket).sqrMagnitude > SocketToleranceSqr) continue;
                return belt;
            }
            return null;
        }

        private MonoBehaviour FindConsumerAt(Vector3 worldPos, Vector3 providerSocket, Vector3 expectedInputDirection)
        {
            var hits = Physics.OverlapSphere(worldPos, 0.8f);
            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null || behaviour == this || !(behaviour is IItemConsumer)) continue;
                    if (IsSocketCompatible(behaviour, providerSocket, expectedInputDirection, provider: false))
                        return behaviour;
                }
            }
            return null;
        }

        private MonoBehaviour FindProviderAt(Vector3 worldPos, Vector3 receiverSocket, Vector3 expectedOutputDirection)
        {
            var hits = Physics.OverlapSphere(worldPos, 0.8f);
            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null || behaviour == this || !(behaviour is IItemProvider)) continue;
                    if (IsSocketCompatible(behaviour, receiverSocket, expectedOutputDirection, provider: true))
                        return behaviour;
                }
            }
            return null;
        }

        private static bool IsSocketCompatible(MonoBehaviour behaviour, Vector3 localSocket, Vector3 expectedOutwardDirection, bool provider)
        {
            Vector3 candidateDirection;
            Vector3 candidateSocket;

            if (behaviour is ConveyorBelt belt)
            {
                candidateDirection = provider ? belt.GetExitDirection() : belt.GetEntryDirection();
                candidateSocket = belt.transform.position + candidateDirection * 0.5f;
            }
            else if (behaviour is Funnel funnel)
            {
                if (provider && funnel.Mode != FunnelMode.Export) return false;
                if (!provider && funnel.Mode != FunnelMode.Import) return false;
                Vector3 localBeltDirection = funnel.beltDirection.sqrMagnitude > 0.01f
                    ? funnel.beltDirection.normalized
                    : Vector3.forward;
                candidateDirection = funnel.transform.TransformDirection(localBeltDirection).normalized;
                candidateSocket = funnel.transform.position + candidateDirection * funnel.portOffset;
            }
            else if (behaviour is ConveyorChute chute)
            {
                candidateDirection = provider ? -chute.transform.up : chute.transform.up;
                candidateSocket = chute.transform.position + candidateDirection * 0.5f;
            }
            else
            {
                return true;
            }

            if (Vector3.Dot(candidateDirection, expectedOutwardDirection.normalized) < 0.85f) return false;
            return (candidateSocket - localSocket).sqrMagnitude <= SocketToleranceSqr;
        }

        private bool TryHandOff(ref ConveyorItem ci)
        {
            if (downstreamTarget == null) return false;

            Vector3 exitDirectionWorld = GetExitDirection();
            Vector3 exitSocket = transform.position + exitDirectionWorld * 0.5f;
            if (!IsSocketCompatible(downstreamTarget, exitSocket, -exitDirectionWorld, provider: false))
            {
                downstreamTarget = null;
                return false;
            }

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

            Vector3 entryDirectionWorld = GetEntryDirection();
            Vector3 entrySocket = transform.position + entryDirectionWorld * 0.5f;
            if (!IsSocketCompatible(upstreamSource, entrySocket, -entryDirectionWorld, provider: true))
            {
                upstreamSource = null;
                return;
            }

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
            Vector3 localPosition;
            Vector3 localTangent;

            if (shape == ConveyorShape.Corner)
            {
                Vector3 start = SafeLocalDirection(entryDirection, Vector3.back) * 0.5f;
                Vector3 end = SafeLocalDirection(exitDirection, Vector3.forward) * 0.5f;
                Vector3 control = Vector3.zero;
                float inverse = 1f - t;
                localPosition = inverse * inverse * start + 2f * inverse * t * control + t * t * end;
                localTangent = 2f * inverse * (control - start) + 2f * t * (end - control);
                localPosition.y = 0.52f;
                localTangent.y = 0f;
            }
            else
            {
                Vector3 start = SafeLocalDirection(entryDirection, Vector3.back) * 0.5f;
                Vector3 end = SafeLocalDirection(exitDirection, Vector3.forward) * 0.5f;
                localPosition = Vector3.Lerp(start, end, t) + Vector3.up * 0.52f;
                localTangent = end - start;
            }

            Vector3 localSide = Vector3.Cross(Vector3.up, localTangent.normalized);
            if (localSide.sqrMagnitude < 0.01f) localSide = Vector3.right;
            localPosition += localSide.normalized * lateralOffset;
            return transform.TransformPoint(localPosition);
        }

        /// <summary>Returns the world-space direction from the belt centre to its output.</summary>
        public Vector3 GetExitDirection()
        {
            return transform.TransformDirection(SafeLocalDirection(exitDirection, Vector3.forward)).normalized;
        }

        /// <summary>Returns the world-space direction from the belt centre to its input.</summary>
        public Vector3 GetEntryDirection()
        {
            return transform.TransformDirection(SafeLocalDirection(entryDirection, Vector3.back)).normalized;
        }

        private static Vector3 SafeLocalDirection(Vector3 direction, Vector3 fallback)
        {
            return direction.sqrMagnitude >= 0.01f ? direction.normalized : fallback;
        }
    }
}
