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
            for (int i = 0; i < hits.Length; i++)
            {
                var block = hits[i] != null ? hits[i].GetComponentInParent<PlacedTieredBlock>() : null;
                if (block == null || block == own || block.definition == null || !visited.Add(block)) continue;
                BuildFamily other = block.definition.family;
                // A variable-height pillar only counts once its chain touches
                // ground; a pillar still hanging in the air carries nothing.
                var adjustablePillar = block.GetComponent<AdjustablePillar>();
                bool verticalSupport = adjustablePillar != null
                    ? adjustablePillar.IsSupportGrounded()
                    : IsVerticalSupport(other);
                if (verticalSupport && ReachesLevel(block, family))
                {
                    AdoptDirectSupport(block);
                    return true;
                }
                if (family != BuildFamily.Roof && other == BuildFamily.Foundation
                    && ReachesLevel(block, family))
                {
                    AdoptDirectSupport(block);
                    return true;
                }

                var load = block.GetComponent<StructuralLoadState>();
                bool compatibleDeck = load != null && (loadFamily == BuildFamily.Roof
                    ? load.loadFamily == BuildFamily.Roof
                    : load.loadFamily == BuildFamily.Floor || load.loadFamily == BuildFamily.FloorHatch || load.loadFamily == BuildFamily.Stairs);
                if (compatibleDeck && load.armed && load.spanFromSupport < spanFromSupport)
                    return true;
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
