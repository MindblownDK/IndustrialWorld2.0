// Assets/Scripts/VoxelEngine/Power/TransmissionTower.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Power
{
    /// <summary>
    /// High-voltage transmission tower: the long-distance half of the power grid.
    ///
    /// Cables are deliberately short-range — they only link one grid step away, which is what
    /// keeps a base's wiring readable. That left the same gap logistics had before the drone
    /// ports: a remote site could not be powered without dragging a cable run across the whole
    /// world, one block at a time.
    ///
    /// A tower spans that gap. Two towers within <see cref="spanRange"/> of each other link
    /// automatically and carry the network between them, so a far outpost joins the home grid
    /// with a handful of blocks instead of hundreds of cables.
    ///
    /// Why it is a <see cref="PowerNode"/> rather than a new system: the power layer already
    /// merges everything reachable into one <see cref="PowerNetwork"/> and prices the result by
    /// its weakest link. A tower therefore needs to do exactly two things — be a node, and
    /// declare long links — and generation, storage, bottlenecks and the existing UI all keep
    /// working untouched.
    ///
    /// The span is capacity-rated like any other conductor. A tower line is wide but not
    /// infinite, so a base that tries to push its whole output down one line is throttled by
    /// the same bottleneck rule that governs cables.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class TransmissionTower : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Cable;

        [Header("Span")]
        [Tooltip("How far this tower can reach another tower, in metres.")]
        public float spanRange = 128f;

        [Tooltip("How many other towers this one may span to at once. Two is a line; three or " +
                 "more makes it a junction.")]
        [Min(1)] public int maxSpans = 3;

        [Tooltip("Watts the span can carry. The network is still limited by its weakest link, " +
                 "so a thin cable feeding the tower remains the bottleneck.")]
        public float spanCapacityWatts = 20000f;

        [Header("Local tap")]
        [Tooltip("Radius in which the tower also picks up ordinary cables and machines at its " +
                 "own base, so a line can be started without a separate connector block.")]
        public float localTapRadius = 4f;

        /// <summary>Towers currently spanned to. Rebuilt every topology pass.</summary>
        private readonly List<TransmissionTower> _spans = new();

        /// <summary>Read by the UI: how many spans this tower currently holds.</summary>
        public int SpanCount => _spans.Count;

        [Header("Visual")]
        [Tooltip("Draw a hanging cable between spanned towers.")]
        public bool showSpanCable = true;

        [Tooltip("Height above the tower's origin the span cable attaches at.")]
        public float cableAnchorHeight = 3.1f;

        /// <summary>Line renderers for the spans this tower OWNS the drawing of.</summary>
        private readonly List<LineRenderer> _cables = new();
        private static Material s_cableMat;

        /// <summary>Every tower in the world, so a span search never does a scene sweep.</summary>
        private static readonly List<TransmissionTower> s_all = new();
        public static IReadOnlyList<TransmissionTower> All => s_all;

        protected override void OnEnable()
        {
            // The tower taps its own base like a relay does, and spans are added on top as
            // manual links rather than by widening connectRadius. Widening it would make the
            // tower hoover up every machine within 128 m, which is not what a pylon does.
            connectRadius = Mathf.Max(connectRadius, localTapRadius);
            requireGridAlignedNeighbours = false;

            if (!s_all.Contains(this)) s_all.Add(this);
            base.OnEnable();
            // A new tower can change which pairs are nearest, and removing one frees a slot
            // on its partners, so the whole set is re-spanned rather than just this tower.
            RebuildAll();
        }

        protected override void OnDisable()
        {
            s_all.Remove(this);
            ClearSpans();
            base.OnDisable();
            RebuildAll();   // partners may now be able to reach someone else
        }

        /// <summary>
        /// Find the towers this one should span to and register them as manual links, which is
        /// the existing mechanism for an intentional long-range edge. Nearest first, capped at
        /// <see cref="maxSpans"/>, and always symmetric so both ends agree.
        /// </summary>
        public void RebuildSpans()
        {
            if (!s_rebuilding) ClearSpans();   // RebuildAll has already cleared everything

            // Nearest towers first, so a dense cluster links sensibly instead of by list order.
            var candidates = new List<(float sqr, TransmissionTower tower)>();
            foreach (var other in s_all)
            {
                if (other == null || other == this || !other.isActiveAndEnabled) continue;

                float reach = Mathf.Min(spanRange, other.spanRange);
                float sqr = (other.transform.position - transform.position).sqrMagnitude;
                if (sqr > reach * reach) continue;

                candidates.Add((sqr, other));
            }
            candidates.Sort((a, b) => a.sqr.CompareTo(b.sqr));

            foreach (var (_, other) in candidates)
            {
                if (_spans.Count >= maxSpans) break;
                // Respect the far tower's budget too: a full tower cannot accept a new span.
                if (other.SpanCount >= other.maxSpans && !other._spans.Contains(this)) continue;

                Link(other);
            }
        }

        /// <summary>
        /// Draw the spans. Only the tower with the smaller instance id draws a given pair, so
        /// a line is never rendered twice on top of itself.
        /// </summary>
        private void RefreshCables()
        {
            foreach (var lr in _cables) if (lr != null) Destroy(lr.gameObject);
            _cables.Clear();
            if (!showSpanCable) return;

            foreach (var other in _spans)
            {
                if (other == null) continue;
                if (GetInstanceID() > other.GetInstanceID()) continue;   // the other end draws it

                var go = new GameObject("SpanCable");
                go.transform.SetParent(transform, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.06f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                if (s_cableMat == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                    s_cableMat = new Material(sh) { color = new Color(0.10f, 0.10f, 0.12f) };
                    if (s_cableMat.HasProperty("_BaseColor"))
                        s_cableMat.SetColor("_BaseColor", new Color(0.10f, 0.10f, 0.12f));
                }
                lr.sharedMaterial = s_cableMat;

                // A shallow catenary reads as a hanging wire rather than a laser line.
                Vector3 a = transform.position + transform.up * cableAnchorHeight;
                Vector3 b = other.transform.position + other.transform.up * other.cableAnchorHeight;
                const int segments = 12;
                lr.positionCount = segments + 1;
                float sag = Vector3.Distance(a, b) * 0.04f;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    Vector3 p = Vector3.Lerp(a, b, t);
                    p -= transform.up * (Mathf.Sin(t * Mathf.PI) * sag);
                    lr.SetPosition(i, p);
                }

                _cables.Add(lr);
            }
        }

        private void Link(TransmissionTower other)
        {
            if (other == null || _spans.Contains(other)) return;

            _spans.Add(other);
            // Symmetric: the far tower records THIS tower, not itself.
            if (!other._spans.Contains(this)) other._spans.Add(this);

            float capacity = Mathf.Min(spanCapacityWatts, other.spanCapacityWatts);

            AddManualLink(this, other, capacity);
            AddManualLink(other, this, capacity);

            PowerNetworkManager.Instance?.SetDirty();
        }

        private static void AddManualLink(PowerNode from, PowerNode to, float capacity)
        {
            from.manualLinks ??= new List<PowerNode>();
            from.manualLinkCapacities ??= new Dictionary<PowerNode, float>();

            if (!from.manualLinks.Contains(to)) from.manualLinks.Add(to);
            from.manualLinkCapacities[to] = capacity;
        }

        private void ClearSpans()
        {
            foreach (var other in _spans)
            {
                if (other == null) continue;
                other._spans.Remove(this);
                other.manualLinks?.Remove(this);
                other.manualLinkCapacities?.Remove(this);
            }
            _spans.Clear();
            foreach (var lr in _cables) if (lr != null) Destroy(lr.gameObject);
            _cables.Clear();

            manualLinks?.Clear();
            manualLinkCapacities?.Clear();

            PowerNetworkManager.Instance?.SetDirty();
        }

        /// <summary>
        /// Re-span every tower. Called after one is placed or removed, because a new tower can
        /// change which pairs are nearest and a removed one frees a slot.
        /// </summary>
        public static void RebuildAll()
        {
            // RebuildSpans() -> Link() -> ... never calls back into RebuildAll, but OnEnable
            // and OnDisable both do, and a batch of towers streaming in would otherwise run
            // this once per tower. The guard makes a burst cost one pass.
            if (s_rebuilding) return;
            s_rebuilding = true;
            try
            {
                for (int i = s_all.Count - 1; i >= 0; i--)
                    if (s_all[i] == null) s_all.RemoveAt(i);

                // Clear every span first, so a full pass cannot be biased by whatever links
                // happened to survive from the previous topology.
                foreach (var tower in s_all)
                    if (tower != null) tower.ClearSpans();

                foreach (var tower in s_all)
                    if (tower != null && tower.isActiveAndEnabled) tower.RebuildSpans();
            }
            finally { s_rebuilding = false; }

            // Cables are refreshed after every span is settled, so a pair is drawn once.
            foreach (var tower in s_all)
                if (tower != null && tower.isActiveAndEnabled) tower.RefreshCables();
        }

        private static bool s_rebuilding;
    }
}
