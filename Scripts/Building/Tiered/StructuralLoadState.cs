using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Runtime load path for newly placed suspended Floors and Roofs.</summary>
    public sealed class StructuralLoadState : MonoBehaviour
    {
        public int spanFromSupport;
        public bool armed;
        private float _nextCheck;

        public void Arm(int span)
        {
            spanFromSupport = Mathf.Max(1, span);
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
                if (IsVerticalSupport(other) && ReachesLevel(block, family)) return true;
                if (family != BuildFamily.Roof && other == BuildFamily.Foundation
                    && ReachesLevel(block, family)) return true;

                var load = block.GetComponent<StructuralLoadState>();
                bool compatibleDeck = family == BuildFamily.Roof
                    ? other == BuildFamily.Roof
                    : other == BuildFamily.Floor || other == BuildFamily.FloorHatch;
                if (compatibleDeck && load != null && load.armed && load.spanFromSupport < spanFromSupport)
                    return true;
            }
            return false;
        }

        private bool ReachesLevel(PlacedTieredBlock support, BuildFamily suspendedFamily)
        {
            float height = support.definition.family == BuildFamily.Foundation
                ? 1.125f
                : support.definition.family == BuildFamily.HalfWall ? 2.8f : 5.625f;
            Vector3 top = support.transform.position + support.transform.up * height;
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
