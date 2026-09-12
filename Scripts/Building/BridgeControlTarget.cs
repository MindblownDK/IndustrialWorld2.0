using UnityEngine;
using VoxelEngine.Building;

namespace IndustrialWorld.Building
{
    /// <summary>Runtime control on the fixed barrier motor post, reachable with the deck raised.</summary>
    public sealed class BridgeControlTarget : MonoBehaviour
    {
        public BridgeSpan Span { get; internal set; }

        public static BridgeSpan Resolve(Collider collider)
        {
            if (collider == null) return null;
            var post = collider.GetComponentInParent<BridgeControlTarget>();
            if (post != null && post.Span != null && post.Span.CanOpen) return post.Span;
            var deck = collider.GetComponentInParent<AsphaltRoad>();
            return deck != null ? deck.Span : null;
        }
    }
}
