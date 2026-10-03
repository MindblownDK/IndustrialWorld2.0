// Assets/Scripts/VoxelEngine/Storage/StorageNetwork.cs
//
// 14.40.0 - The digital storage network resolver.
//
// One rule, no radius magic: two storage devices belong to the same network
// when they are PIPED together (Data Pipe runs between them) or when they
// are physically TOUCHING. That's it. A terminal across the room shows
// NO LINK until someone runs a pipe to it.
//
// Implementation: every second the resolver collects all storage devices
// and data pipes, builds an adjacency graph from
//   (a) ConnectionAnchor.connections - the Data Pipe links (pipe<->pipe and
//       pipe<->device, maintained by DataCable's own scanner), and
//   (b) expanded-AABB overlap between device colliders - "touching",
// then floods it into components. Each component elects ONE controller
// (Server Controller) deterministically by world position, so every
// machine in a multiplayer session agrees on the same election without a
// single network message.
//
// Everything downstream asks this class instead of running its own
// FindObjectsByType-radius search: terminals, importers, exporters, NAS
// shelves, power stations, wireless transmitters and security blocks.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Storage
{
    public static class StorageNetwork
    {
        private const float REFRESH_SECONDS = 1.0f;
        private const float TOUCH_EPSILON = 0.08f;

        private static float _lastBuildTime = -999f;
        private static int _lastBuildFrame = -1;

        // node -> component id, and component id -> members / controller.
        private static readonly Dictionary<Component, int> _componentOf = new();
        private static readonly List<List<Component>> _members = new();
        private static readonly List<ServerRack> _controller = new();

        // scratch
        private static readonly List<Component> _nodes = new();
        private static readonly Dictionary<GameObject, Component> _nodeByRoot = new();
        private static readonly List<Bounds> _bounds = new();
        private static readonly List<int> _unionParent = new();

        // ───────────────────────── public API ─────────────────────────

        /// <summary>The elected Server Controller of the network this device
        /// belongs to, or null when its component has none. Never returns a
        /// conflicting (non-elected) rack.</summary>
        public static ServerRack ControllerOf(Component device)
        {
            if (device == null) return null;
            EnsureFresh();
            var node = NodeFor(device);
            if (node == null || !_componentOf.TryGetValue(node, out int comp)) return null;
            return comp >= 0 && comp < _controller.Count ? _controller[comp] : null;
        }

        /// <summary>True when this rack lost the controller election of its own
        /// network - a second controller was piped in. It must stand down.</summary>
        public static bool IsConflicting(ServerRack rack)
        {
            if (rack == null) return false;
            EnsureFresh();
            if (!_componentOf.TryGetValue(rack, out int comp)) return false;
            return _controller[comp] != null && !ReferenceEquals(_controller[comp], rack);
        }

        /// <summary>All members of the given controller's network that are of
        /// type T, in deterministic (position-sorted) order.</summary>
        public static void MembersOf<T>(ServerRack controller, List<T> results) where T : Component
        {
            results.Clear();
            if (controller == null) return;
            EnsureFresh();
            if (!_componentOf.TryGetValue(controller, out int comp)) return;
            var list = _members[comp];
            for (int i = 0; i < list.Count; i++)
                if (list[i] is T t) results.Add(t);
        }

        /// <summary>Force a rebuild on the next query (e.g. right after placing
        /// or removing a storage block, so UIs don't lag a second behind).</summary>
        public static void Invalidate() => _lastBuildTime = -999f;

        // ───────────────────────── build ─────────────────────────

        private static void EnsureFresh()
        {
            // At most one rebuild per frame; at least one per REFRESH_SECONDS.
            if (Time.frameCount == _lastBuildFrame) return;
            if (Time.time - _lastBuildTime < REFRESH_SECONDS) return;
            _lastBuildFrame = Time.frameCount;
            _lastBuildTime = Time.time;
            Rebuild();
        }

        private static Component NodeFor(Component device)
        {
            if (device == null) return null;
            // Devices register by their root GameObject so a child collider or a
            // multi-component block still resolves to the same node.
            return _nodeByRoot.TryGetValue(device.transform.root.gameObject, out var n) ? n : null;
        }

        private static void Collect<T>(List<Component> into) where T : Behaviour
        {
            var found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            foreach (var f in found)
                if (f != null && f.isActiveAndEnabled) into.Add(f);
        }

        private static void Rebuild()
        {
            _nodes.Clear(); _nodeByRoot.Clear(); _bounds.Clear();
            _componentOf.Clear(); _members.Clear(); _controller.Clear();
            _unionParent.Clear();

            // 1) Collect nodes: one per block root. Device components first so a
            //    root carrying both (e.g. rack + pipe child) registers as device.
            Collect<ServerRack>(_nodes);
            Collect<NASBlock>(_nodes);
            Collect<Powerstation>(_nodes);
            Collect<StorageTerminal>(_nodes);
            Collect<CraftingTerminal>(_nodes);
            Collect<PatternTerminal>(_nodes);
            Collect<StorageImporter>(_nodes);
            Collect<StorageExporter>(_nodes);
            Collect<DiskManipulator>(_nodes);
            Collect<StorageDrawerController>(_nodes);
            Collect<ExternalStorageBlock>(_nodes);
            Collect<WirelessTransmitter>(_nodes);
            Collect<SecurityBlock>(_nodes);
            Collect<Networks.DataCable>(_nodes);

            // Deduplicate by root GameObject (deterministic keep-first).
            for (int i = 0; i < _nodes.Count; i++)
            {
                var root = _nodes[i].transform.root.gameObject;
                if (_nodeByRoot.ContainsKey(root)) { _nodes.RemoveAt(i); i--; continue; }
                _nodeByRoot[root] = _nodes[i];
            }

            // Hidden wireless proxy terminals (HideAndDontSave) must never join.
            for (int i = _nodes.Count - 1; i >= 0; i--)
                if ((_nodes[i].gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
                {
                    _nodeByRoot.Remove(_nodes[i].transform.root.gameObject);
                    _nodes.RemoveAt(i);
                }

            int n = _nodes.Count;
            for (int i = 0; i < n; i++)
            {
                _unionParent.Add(i);
                _bounds.Add(NodeBounds(_nodes[i]));
            }

            // 2a) Pipe edges: every ConnectionAnchor connection whose two owners
            //     both resolve to nodes unions them. DataCable maintains these.
            for (int i = 0; i < n; i++)
            {
                var anchors = _nodes[i].transform.root.GetComponentsInChildren<Networks.ConnectionAnchor>(false);
                foreach (var a in anchors)
                {
                    if (a == null || a.networkType != Networks.NetworkType.Data) continue;
                    foreach (var c in a.connections)
                    {
                        if (c == null) continue;
                        var other = NodeFor(c);
                        if (other == null) continue;
                        int j = _nodes.IndexOf(other);
                        if (j >= 0) Union(i, j);
                    }
                }
            }

            // 2b) Touching edges: expanded-AABB overlap. O(n^2) over storage
            //     blocks only - these counts stay small per base.
            for (int i = 0; i < n; i++)
            {
                var bi = _bounds[i];
                bi.Expand(TOUCH_EPSILON * 2f);
                for (int j = i + 1; j < n; j++)
                    if (bi.Intersects(_bounds[j])) Union(i, j);
            }

            // 3) Components + deterministic controller election.
            var compIndex = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int rootSet = Find(i);
                if (!compIndex.TryGetValue(rootSet, out int comp))
                {
                    comp = _members.Count;
                    compIndex[rootSet] = comp;
                    _members.Add(new List<Component>());
                    _controller.Add(null);
                }
                _members[comp].Add(_nodes[i]);
                _componentOf[_nodes[i]] = comp;
            }

            for (int c = 0; c < _members.Count; c++)
            {
                ServerRack elected = null;
                foreach (var m in _members[c])
                {
                    if (m is not ServerRack rack) continue;
                    if (elected == null || PositionBefore(rack.transform.position, elected.transform.position))
                        elected = rack;
                }
                _controller[c] = elected;
            }
        }

        /// <summary>Deterministic world-position ordering - identical on every
        /// machine in a session because block positions replicate exactly.</summary>
        private static bool PositionBefore(Vector3 a, Vector3 b)
        {
            if (!Mathf.Approximately(a.x, b.x)) return a.x < b.x;
            if (!Mathf.Approximately(a.y, b.y)) return a.y < b.y;
            return a.z < b.z;
        }

        private static Bounds NodeBounds(Component node)
        {
            var cols = node.transform.root.GetComponentsInChildren<Collider>(false);
            Bounds b = default;
            bool has = false;
            foreach (var c in cols)
            {
                if (c == null || c.isTrigger) continue;
                if (!has) { b = c.bounds; has = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!has) b = new Bounds(node.transform.position, Vector3.one);
            return b;
        }

        private static int Find(int i)
        {
            while (_unionParent[i] != i)
            {
                _unionParent[i] = _unionParent[_unionParent[i]];
                i = _unionParent[i];
            }
            return i;
        }

        private static void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _unionParent[rb] = ra;
        }
    }
}
