using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Scales a pillar from its grounded root to a floor directly above.</summary>
    public sealed class AdjustablePillar : MonoBehaviour
    {
        private const float AuthoredHeight = 5.625f;
        public float currentHeight = AuthoredHeight;

        private void Start()
        {
            // Additive persistence: restored pieces carry their grounded root, so
            // a non-standard pillar can recover its height from the floor above
            // without adding a new save field.
            if (!Mathf.Approximately(currentHeight, AuthoredHeight)) return;
            var hits = Physics.RaycastAll(transform.position + transform.up * 0.05f,
                transform.up, 40f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var block = hits[i].collider != null
                    ? hits[i].collider.GetComponentInParent<PlacedTieredBlock>()
                    : null;
                if (block == null || block == GetComponent<PlacedTieredBlock>() || block.definition == null) continue;
                if (block.definition.family != BuildFamily.Floor && block.definition.family != BuildFamily.FloorHatch) continue;
                Configure(hits[i].distance - 0.05f);
                return;
            }
        }

        public void Configure(float height)
        {
            currentHeight = Mathf.Clamp(height, 0.5f, 40f);
            Vector3 scale = transform.localScale;
            scale.y = currentHeight / AuthoredHeight;
            transform.localScale = scale;
        }
    }
}
