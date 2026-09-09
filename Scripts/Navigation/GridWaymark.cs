// Assets/Scripts/VoxelEngine/Navigation/GridWaymark.cs
//
// A WAYMARK — a destination that is not a planet.
//
// The route book could already say "go to Europa" and "go to the place I flew past at 03:00". What
// it could not say was "go to the connector at the smelter", because a connector is a block on a grid
// in the dirt, not a body in the star map. This file closes that gap with the smallest honest model:
// a name, a live source while the thing exists, and a frozen position when it does not.
//
// There is deliberately no global waymark registry and no save key. A waymark *is* a label: the
// connector names itself, the grid saves the label (the block's own custom name, which the save
// format already carries), and the list is built by asking the live blocks. A waymark whose block was
// destroyed therefore does not leave a dangling record to clean up — it stops existing, and the pin it
// once wrote down keeps working as a pin. That is less code, less state, and less that can rot.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Navigation
{
    /// <summary>Anything a waymark can be attached to. A connector implements this; so, later, can a
    /// beacon or a named base block, without the waymark model changing at all.</summary>
    public interface IWaymarkSource
    {
        /// <summary>The name the player typed. Lookups are by this string, trimmed, and it is the
        /// connector's block name — the save system already persists that as a custom name.</summary>
        string WaymarkLabel { get; }

        /// <summary>Where the thing is in the world right now, in metres.</summary>
        Vector3 WaymarkWorldPosition { get; }

        /// <summary>Whether this source is currently worth flying to: a connector that is off, gutted
        /// or unpowered is still a place, but it is not a place with a purpose.</summary>
        bool WaymarkAcceptsTraffic { get; }
    }

    /// <summary>What an auto-run loop needs from a place that serves ships. Both refuel surfaces — the
    /// grid connector of 9.35.0-dev and the static pad of 9.36.0-dev — implement it, and both keep
    /// their own supply-side code, because a pad bolted to a hull draws from batteries and a pad
    /// bolted to the ground draws from a wire network. Sharing the queue is real reuse; sharing the
    /// pumping would have been one class with two entirely different bodies.</summary>
    public interface IRefuelPad : IWaymarkSource
    {
        System.Collections.Generic.IReadOnlyList<ConnectorVisit> Queue { get; }
        ConnectorVisit Head { get; }
        int QueueDepth { get; }
        TransferFlow LastFlow { get; }
        System.Collections.Generic.IReadOnlyList<string> Log { get; }

        /// <summary>Whether this fitting can hold a visitor still. A ground pad cannot, and a panel that
        /// hides that fact is a panel that hides the reason its queue moves slowly.</summary>
        bool HasMagneticLock { get; }

        /// <summary>The rate a not-fully-mated visitor gets, 1 when there is no such thing as unmated.</summary>
        float SoftRate { get; }

        void BumpToFront(GridSystem.GridEntity grid);
        void Eject(GridSystem.GridEntity grid);
    }

    /// <summary>A named destination: live while its source lives, frozen after it does.</summary>
    [System.Serializable]
    public struct GridWaymark
    {
        public string name;

        // Frozen form — a point the player pinned, or a source that has since gone away.
        public bool isPinned;
        public double3 pinnedKm;
        public Vector3 pinnedWorld;

        /// <summary>True when the waymark resolves to something that is actually there and switched
        /// on. A frozen pin is always "there"; a named connector is only there if a block answers.</summary>
        public bool IsLive => isPinned || FindSource(name) != null;

        /// <summary>World position, metres. Returns false and leaves `out` alone when the waymark has
        /// no answer at all (a name that was never pinned and no longer exists).</summary>
        public bool TryResolveWorld(out Vector3 world)
        {
            var src = isPinned ? null : FindSource(name);
            if (src != null) { world = src.WaymarkWorldPosition; return true; }
            world = pinnedWorld;
            return isPinned;
        }

        /// <summary>Cosmic position, kilometres — the frame the route model lives in. Falls back to
        /// the frozen record when the source is gone, which is what lets a saved route stay a route.</summary>
        public double3 ResolvedPositionKm(CosmicRegistry registry)
        {
            var src = isPinned ? null : FindSource(name);
            if (src != null)
            {
                var origin = SpaceOrigin.Instance;
                if (origin != null && registry != null)
                    return origin.GetCosmicKm(src.WaymarkWorldPosition);
            }
            return pinnedKm;
        }

        /// <summary>The live source behind this name, if any. Public because the autopilot has to ask
        /// the same object the waymark points at — "fly to the name" and "dock with the block" must
        /// never be allowed to disagree about which connector that was.</summary>
        public static IWaymarkSource FindSource(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            string wanted = label.Trim();

            // Live blocks answer first: no scan, no allocation, and a destroyed block is out of the
            // list the moment Unity destroys it.
            var live = Registry;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var src = live[i];
                if (src == null) { live.RemoveAt(i); continue; }          // prune as we go
                if (src is not Object u || u == null) { live.RemoveAt(i); continue; }
                if (src.WaymarkLabel == wanted) return src;
            }
            return null;
        }

        /// <summary>All live sources, for the destination picker. Rebuilt only when something
        /// registered or unregistered, so opening a panel is not a scene scan every frame.</summary>
        public static IReadOnlyList<IWaymarkSource> AllLive => _all;
        static readonly List<IWaymarkSource> _all = new(32);
        static readonly List<IWaymarkSource> Registry = new(32);
        static int _revision;
        static int _allBuiltAtRevision = -1;

        public static void Register(IWaymarkSource src)
        {
            if (src == null || Registry.Contains(src)) return;
            Registry.Add(src);
            _revision++;
        }

        public static void Unregister(IWaymarkSource src)
        {
            if (src == null) return;
            if (Registry.Remove(src)) _revision++;
        }

        public static void RefreshLive()
        {
            if (_allBuiltAtRevision == _revision) return;
            _all.Clear();
            for (int i = 0; i < Registry.Count; i++)
            {
                var src = Registry[i];
                if (src == null) continue;
                if (src is Object u && u == null) continue;
                _all.Add(src);
            }
            _allBuiltAtRevision = _revision;
        }

        /// <summary>Names, sorted so a picker reads the same way every time it opens.</summary>
        public static List<string> CollectNames()
        {
            RefreshLive();
            var names = new List<string>(_all.Count);
            for (int i = 0; i < _all.Count; i++)
            {
                string n = _all[i]?.WaymarkLabel;
                if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n)) names.Add(n);
            }
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>How far, in km, from a cosmic point. float.MaxValue when the waymark has no
        /// answer, so a picker sorts the dead ones to the bottom instead of inventing a distance.</summary>
        public static float RangeKmFrom(string label, double3 fromKm, CosmicRegistry registry)
        {
            var src = FindSource(label);
            double3 to;
            if (src != null)
            {
                var origin = SpaceOrigin.Instance;
                if (origin == null) return float.MaxValue;
                to = origin.GetCosmicKm(src.WaymarkWorldPosition);
            }
            else
            {
                // Not a live source: the caller may still hold a pinned record. The picker only uses
                // this for display, so an absent answer is fine and an invented one is not.
                to = fromKm;
                if (registry == null) return float.MaxValue;
            }
            double dx = to.x - fromKm.x, dy = to.y - fromKm.y, dz = to.z - fromKm.z;
            return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static GridWaymark Pin(string label, Vector3 world, CosmicRegistry registry)
        {
            var origin = SpaceOrigin.Instance;
            return new GridWaymark
            {
                name = (label ?? string.Empty).Trim(),
                isPinned = true,
                pinnedWorld = world,
                pinnedKm = origin != null ? origin.GetCosmicKm(world) : double3.zero,
            };
        }
    }
}
