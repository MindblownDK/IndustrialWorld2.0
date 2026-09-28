using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Runtime load path for newly placed suspended Floors and Roofs.</summary>
    public sealed class StructuralLoadState : MonoBehaviour
    {
        public BuildFamily loadFamily;
        public int spanFromSupport;
        public Vector3 supportAnchor;
        public bool armed;
        private float _nextCheck;

        public void Arm(int span, Vector3 anchor)
        {
            spanFromSupport = Mathf.Max(1, span);
            supportAnchor = anchor;
            armed = true;
            _nextCheck = Time.time + 0.75f;
        }

        private void Update()
        {
            if (!armed || Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.75f;
            if (!HasLoadPath()) Destroy(gameObject);
        }

        private bool HasLoadPath()
        {
            var own = GetComponent<PlacedTieredBlock>();
            if (own == null || own.definition == null) return true;
            BuildFamily family = own.definition.family;
            float radius = 8.1f;
            var hits = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore);
            var visited = new HashSet<PlacedTieredBlock>();
            var blocks = new List<PlacedTieredBlock>(hits.Length);
            for (int i = 0; i < hits.Length; i++)
            {
                var block = hits[i] != null ? hits[i].GetComponentInParent<PlacedTieredBlock>() : null;
                if (block == null || block == own || block.definition == null || !visited.Add(block)) continue;
                blocks.Add(block);
            }

            // Pass 1: a direct grounded support is adopted and resets the span,
            // so it must win over any deck neighbour that merely relays its own
            // path. The old single pass returned through whichever block the
            // physics query happened to list first, which is why a freshly
            // grounded pillar under a span-two floor often never got adopted.
            // A wall standing on a suspended deck is remembered as a pass-through
            // carrier instead: it relays that deck's span but never resets ours.
            int bestCarrierSpan = int.MaxValue;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                BuildFamily other = block.definition.family;
                bool grounded;
                if (block.TryGetComponent<AdjustablePillar>(out var adjustablePillar))
                    grounded = adjustablePillar.IsSupportGrounded();
                else if (IsVerticalSupport(other))
                {
                    grounded = TryResolveSupportBase(block, out var carryingDeck);
                    if (!grounded && carryingDeck != null && carryingDeck.armed
                        && ReachesLevel(block, family))
                        bestCarrierSpan = Mathf.Min(bestCarrierSpan, carryingDeck.spanFromSupport);
                }
                else if (other == BuildFamily.Foundation && family != BuildFamily.Roof)
                    grounded = true;
                else
                    continue;
                if (grounded && ReachesLevel(block, family))
                {
                    AdoptDirectSupport(block);
                    return true;
                }
            }

            // Pass 2: no direct support in reach - survive through a compatible
            // neighbouring deck (or a wall pass-through) strictly closer to one.
            if (bestCarrierSpan < spanFromSupport) return true;
            for (int i = 0; i < blocks.Count; i++)
            {
                var load = blocks[i].GetComponent<StructuralLoadState>();
                bool compatibleDeck = load != null && (loadFamily == BuildFamily.Roof
                    ? load.loadFamily == BuildFamily.Roof
                    : load.loadFamily == BuildFamily.Floor || load.loadFamily == BuildFamily.FloorHatch || load.loadFamily == BuildFamily.Stairs);
                if (compatibleDeck && load.armed && load.spanFromSupport < spanFromSupport)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves what actually carries a wall-type support (Wall, Doorway,
        /// Window, Wall Frame, Half Wall, legacy Pillar). Solid ground, a
        /// Foundation or a grounded pillar chain under its base make it a true
        /// support (returns true). Standing on a suspended deck makes it a
        /// pass-through: the deck's load state comes back in
        /// <paramref name="carryingDeck"/> so the caller continues that span
        /// instead of resetting it - this closes the floor-floor-wall ladder
        /// that restarted the cantilever forever. An unarmed legacy/restored
        /// deck counts as stable so old saves keep building normally.
        /// </summary>
        public static bool TryResolveSupportBase(PlacedTieredBlock support,
            out StructuralLoadState carryingDeck, int depth = 6)
        {
            carryingDeck = null;
            if (support == null || depth <= 0) return false;
            if (support.TryGetComponent<AdjustablePillar>(out var pillar))
                return pillar.IsSupportGrounded();

            Transform root = support.transform;
            var hits = Physics.RaycastAll(root.position + root.up * 0.1f, -root.up,
                0.7f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(root)) continue;
                if (BuildSystemV2.IsDynamicBody(collider)) continue;

                var below = collider.GetComponentInParent<PlacedTieredBlock>();
                if (below == null) return true; // terrain or any static world surface
                if (below == support || below.definition == null) continue;
                BuildFamily family = below.definition.family;
                if (family == BuildFamily.Foundation) return true;
                if (below.TryGetComponent<AdjustablePillar>(out var pillarBelow))
                    return pillarBelow.IsSupportGrounded();
                if (IsVerticalSupport(family))
                    return TryResolveSupportBase(below, out carryingDeck, depth - 1);

                var load = below.GetComponent<StructuralLoadState>();
                if (load == null || !load.armed) return true; // legacy deck: stable
                carryingDeck = load;
                return false;
            }
            return false;
        }

        private void AdoptDirectSupport(PlacedTieredBlock support)
        {
            float height = support.definition.family == BuildFamily.Foundation
                ? 1.125f
                : support.definition.family == BuildFamily.HalfWall ? 2.8f : 5.625f;
            if (support.TryGetComponent<AdjustablePillar>(out var pillar)) height = pillar.currentHeight;
            spanFromSupport = 1;
            supportAnchor = support.transform.position + support.transform.up * height;
        }

        private bool ReachesLevel(PlacedTieredBlock support, BuildFamily suspendedFamily)
        {
            float height = support.definition.family == BuildFamily.Foundation
                ? 1.125f
                : support.definition.family == BuildFamily.HalfWall ? 2.8f : 5.625f;
            if (support.TryGetComponent<AdjustablePillar>(out var adjustablePillar))
                height = adjustablePillar.currentHeight;
            Vector3 top = support.transform.position + support.transform.up * height;
            if (suspendedFamily == BuildFamily.Stairs && support.TryGetComponent<AdjustablePillar>(out _))
            {
                float nearest = float.MaxValue;
                foreach (var collider in GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null) continue;
                    nearest = Mathf.Min(nearest, Vector3.Distance(collider.ClosestPoint(top), top));
                }
                if (nearest <= 0.8f) return true;
            }
            Vector3 delta = transform.position - top;
            float vertical = Mathf.Abs(Vector3.Dot(delta, support.transform.up));
            Vector3 planar = delta - support.transform.up * Vector3.Dot(delta, support.transform.up);
            float reach = suspendedFamily == BuildFamily.Roof ? 5.8f : 5.5f;
            return vertical < 0.85f && planar.sqrMagnitude <= reach * reach;
        }

        public static bool IsVerticalSupport(BuildFamily family)
            => family == BuildFamily.Wall || family == BuildFamily.Doorway
                || family == BuildFamily.Window || family == BuildFamily.WallFrame
                || family == BuildFamily.HalfWall || family == BuildFamily.Pillar;
    }
}
