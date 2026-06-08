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
        // Stable, order-independent hash for an unordered GameObject pair.
        // Using GetInstanceID keeps the key small and avoids holding strong
        // references that would survive scene unloads.
        private static readonly HashSet<long> _blocked = new();

        private static long PairKey(GameObject a, GameObject b)
        {
            if (a == null || b == null) return 0L;
#pragma warning disable CS0618
            int ia = a.GetInstanceID();
            int ib = b.GetInstanceID();
#pragma warning restore CS0618
            int lo = Mathf.Min(ia, ib);
            int hi = Mathf.Max(ia, ib);
            // Pack into a long so the order never matters.
            return ((long)(uint)lo << 32) | (uint)hi;
        }

        /// <summary>True if these two GameObjects have been wrenched apart.</summary>
        public static bool IsBlocked(GameObject a, GameObject b)
        {
            long key = PairKey(a, b);
            return key != 0L && _blocked.Contains(key);
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
            long key = PairKey(a, b);
            if (key != 0L) _blocked.Add(key);
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
            long key = PairKey(a, b);
            if (key != 0L) _blocked.Remove(key);
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
#pragma warning disable CS0618
            int id = go.GetInstanceID();
#pragma warning restore CS0618
            // Walk the set and drop every key whose hi OR lo half matches.
            // Using ToArray() so we can mutate the underlying set safely.
            long[] snapshot = new long[_blocked.Count];
            int i = 0;
            foreach (var key in _blocked) snapshot[i++] = key;
            foreach (var key in snapshot)
            {
                int lo = (int)(key & 0xFFFFFFFF);
                int hi = (int)((key >> 32) & 0xFFFFFFFF);
                if (lo == id || hi == id) _blocked.Remove(key);
            }
        }

        /// <summary>How many pairs are currently blocked (for debug UIs).</summary>
        public static int Count => _blocked.Count;
    }
}
