// Assets/Scripts/VoxelEngine/Networks/WrenchBlacklist.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   WRENCH BLACKLIST — persistent "do not auto-reconnect" set     ║
// ║                                                                  ║
// ║  Power cables, pipes and data cables all use distance-based     ║
// ║  auto-discovery. Without this blacklist, the moment the player  ║
// ║  wrenches a link the next topology rebuild silently restores    ║
// ║  it. The wrench now ADDS the (a, b) pair here and every network ║
// ║  manager consults this set inside its rebuild loop before       ║
// ║  pairing two nodes.                                             ║
// ║                                                                  ║
// ║  Re-wrenching the same pair (or placing a fresh cable through   ║
// ║  the gap) removes the entry so reconnects always feel intuitive.║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// Singleton-ish static set of unordered GameObject pairs that the
    /// player has explicitly wrenched apart. Topology rebuilders treat
    /// any pair listed here as "do not link". The list is intentionally
    /// stored at the GameObject level (not by ConnectionAnchor) because
    /// power cables / pipes don't always carry anchors.
    /// </summary>
    public static class WrenchBlacklist
    {
        // Stable, order-independent key for an unordered GameObject pair.
        // In Unity 6.4+, we use EntityId which is the modern replacement for InstanceID.
        // We store them as a tuple in a HashSet for maximum efficiency and clarity.
        private static readonly HashSet<(EntityId A, EntityId B)> _blocked = new();

        /// <summary>
        /// Generates a stable, order-independent key for two GameObjects.
        /// </summary>
        private static (EntityId A, EntityId B) PairKey(GameObject a, GameObject b)
        {
            if (a == null || b == null) return (default, default);
            
            EntityId idA = a.GetEntityId();
            EntityId idB = b.GetEntityId();
            
            // Ensure order independence by sorting based on the internal hash/value.
            // EntityId implements GetHashCode().
            return idA.GetHashCode() < idB.GetHashCode() ? (idA, idB) : (idB, idA);
        }

        /// <summary>True if these two GameObjects have been wrenched apart.</summary>
        public static bool IsBlocked(GameObject a, GameObject b)
        {
            var key = PairKey(a, b);
            return !key.A.Equals(default) && _blocked.Contains(key);
        }

        /// <summary>True if these two Components' GameObjects are blocked.</summary>
        public static bool IsBlocked(Component a, Component b)
        {
            if (a == null || b == null) return false;
            return IsBlocked(a.gameObject, b.gameObject);
        }

        /// <summary>Add a pair so future rebuilds skip them.</summary>
        public static void Block(GameObject a, GameObject b)
        {
            var key = PairKey(a, b);
            if (!key.A.Equals(default)) _blocked.Add(key);
        }

        /// <summary>Add a pair via components.</summary>
        public static void Block(Component a, Component b)
        {
            if (a == null || b == null) return;
            Block(a.gameObject, b.gameObject);
        }

        /// <summary>Remove a pair so future rebuilds reconnect them normally.</summary>
        public static void Unblock(GameObject a, GameObject b)
        {
            var key = PairKey(a, b);
            if (!key.A.Equals(default)) _blocked.Remove(key);
        }

        /// <summary>Forget every block. Called on scene unload.</summary>
        public static void Clear() => _blocked.Clear();

        /// <summary>
        /// Drop every blacklist entry that involves <paramref name="go"/>.
        /// Called by <c>PortConfig</c> whenever the player edits a face — the
        /// intent there is clearly "I want this machine to connect again",
        /// so we wipe any stale wrench disconnects pinned to it.
        /// </summary>
        public static void ClearForGameObject(GameObject go)
        {
            if (go == null || _blocked.Count == 0) return;
            EntityId id = go.GetEntityId();
            
            // Use RemoveWhere for efficient bulk removal in Unity 6.
            _blocked.RemoveWhere(pair => pair.A.Equals(id) || pair.B.Equals(id));
        }

        /// <summary>How many pairs are currently blocked (for debug UIs).</summary>
        public static int Count => _blocked.Count;
    }
}
