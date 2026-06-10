// Assets/Scripts/VoxelEngine/Transport/ItemPortRouting.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   ITEM PORT ROUTING — shared per-face logistics for ANY machine ║
// ║                                                                  ║
// ║   Attach to any GameObject that also has an IItemPortHost (Chest,║
// ║   Furnace, Processor, …) + a PortConfig. For every face the host ║
// ║   marks Input/Output it:                                         ║
// ║     • routes the face to a chosen INTERNAL container (dropdown),  ║
// ║     • applies a per-face item whitelist (searchable filter),      ║
// ║     • OUTPUT → pushes that container's items into adjacent pipes, ║
// ║     • INPUT  → accepts pipe-pushed items into that container.     ║
// ║                                                                  ║
// ║   One component replaces the bespoke logistics that used to live  ║
// ║   inside Chest, so adding ports to a new machine is trivial.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>How a face's item list is interpreted.</summary>
    public enum FilterMode { Whitelist, Blacklist }

    /// <summary>How competing OUTPUT faces share the source container.</summary>
    public enum DistributionMode { RoundRobin, Priority }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(PortConfig))]
    public class ItemPortRouting : MonoBehaviour
    {
        [Tooltip("Seconds between automatic OUTPUT extraction ticks.")]
        public float pushInterval = 0.5f;
        [Tooltip("Max items moved out per OUTPUT face per tick.")]
        public int pushPerTick = 8;
        [Tooltip("Distance at which OUTPUT faces locate adjacent ItemPipes.")]
        public float pipeConnectRadius = 1.6f;

        [Tooltip("How OUTPUT faces share the container when several compete:\n" +
                 "Round-Robin = rotate who gets first dibs each tick (fair split).\n" +
                 "Priority = always serve faces in fixed order (top face first).")]
        public DistributionMode distribution = DistributionMode.RoundRobin;

        // Rotating cursor for round-robin so each output face leads in turn.
        private int _rrCursor;

        // Per-face routing: which host container index the face maps to.
        private readonly Dictionary<CubeFace, int> _faceContainer = new();
        // Per-face item list (whitelist OR blacklist depending on the mode below).
        private readonly Dictionary<CubeFace, List<ItemDefinition>> _faceFilters = new();
        // Per-face filter mode. Whitelist = only listed items pass. Blacklist =
        // everything passes EXCEPT listed items. Empty list = pass everything.
        private readonly Dictionary<CubeFace, FilterMode> _faceFilterMode = new();

        private PortConfig _ports;
        private IItemPortHost _host;
        private float _pushTimer;

        public PortConfig Ports { get { EnsurePorts(); return _ports; } }

        private void Awake()  => EnsurePorts();
        private void OnEnable() => EnsurePorts();

        private void EnsurePorts()
        {
            if (_ports == null) _ports = GetComponent<PortConfig>();
            if (_ports != null) _ports.EnsureAllFaces();
            if (_host == null) _host = GetComponent<IItemPortHost>();
        }

        private void Update()
        {
            EnsurePorts();
            if (_ports == null || _host == null) return;
            // _host is an interface ref — a destroyed Unity object reads as C#-non-null
            // through it, so verify the underlying Component is still alive.
            if (_host is UnityEngine.Object hostObj && hostObj == null) return;
            if (!_ports.HasAnyOutput()) return;

            _pushTimer += Time.deltaTime;
            if (_pushTimer < pushInterval) return;
            _pushTimer -= pushInterval;
            ExtractToPipes();
        }

        // ── Container routing API ───────────────────────────────────────────

        /// <summary>The container index a face routes to (defaults to a sensible one).</summary>
        public int GetFaceContainer(CubeFace face)
        {
            if (_faceContainer.TryGetValue(face, out var idx)) return idx;
            return DefaultContainerFor(face);
        }

        /// <summary>Route a face to a specific host container index.</summary>
        public void SetFaceContainer(CubeFace face, int containerIndex)
        {
            _faceContainer[face] = Mathf.Max(0, containerIndex);
        }

        /// <summary>
        /// Pick a reasonable default container for a face based on its direction:
        /// OUTPUT faces prefer the first output-capable container, INPUT faces the
        /// first input-capable one.
        /// </summary>
        private int DefaultContainerFor(CubeFace face)
        {
            EnsurePorts();
            var list = _host?.GetPortContainers();
            if (list == null || list.Count == 0) return 0;
            var dir = _ports != null ? _ports.GetDirection(face) : PortDirection.None;
            for (int i = 0; i < list.Count; i++)
            {
                if (dir == PortDirection.Output && list[i].CanOutput) return i;
                if (dir == PortDirection.Input  && list[i].CanInput)  return i;
            }
            return 0;
        }

        // ── Filter API ──────────────────────────────────────────────────────
        public IReadOnlyList<ItemDefinition> GetFilter(CubeFace face)
            => _faceFilters.TryGetValue(face, out var l) ? l : (IReadOnlyList<ItemDefinition>)Array.Empty<ItemDefinition>();

        public bool HasFilter(CubeFace face)
            => _faceFilters.TryGetValue(face, out var l) && l != null && l.Count > 0;

        public void AddFilter(CubeFace face, ItemDefinition item)
        {
            if (item == null) return;
            if (!_faceFilters.TryGetValue(face, out var l) || l == null)
            { l = new List<ItemDefinition>(); _faceFilters[face] = l; }
            if (!l.Contains(item)) l.Add(item);
        }

        public void RemoveFilter(CubeFace face, ItemDefinition item)
        {
            if (item != null && _faceFilters.TryGetValue(face, out var l) && l != null) l.Remove(item);
        }

        public void ClearFilter(CubeFace face)
        {
            if (_faceFilters.TryGetValue(face, out var l) && l != null) l.Clear();
        }

        /// <summary>Whitelist (only listed pass) or Blacklist (listed are rejected).</summary>
        public FilterMode GetFilterMode(CubeFace face)
            => _faceFilterMode.TryGetValue(face, out var m) ? m : FilterMode.Whitelist;

        public void SetFilterMode(CubeFace face, FilterMode mode) => _faceFilterMode[face] = mode;

        public bool PassesFilter(CubeFace face, ItemDefinition item)
        {
            if (item == null) return false;
            bool listed = _faceFilters.TryGetValue(face, out var l) && l != null && l.Contains(item);
            bool hasAny = _faceFilters.TryGetValue(face, out var l2) && l2 != null && l2.Count > 0;
            if (!hasAny) return true; // empty filter → everything passes
            return GetFilterMode(face) == FilterMode.Whitelist ? listed : !listed;
        }

        // ── Connection gate (used by ItemPipe to draw arms) ─────────────────
        public bool IsFaceConnectable(Vector3 fromWorldPos)
        {
            EnsurePorts();
            if (_ports == null) return false;
            var face = FaceTowards(fromWorldPos);
            if (!face.HasValue) return false;
            return _ports.IsFaceEnabled(face.Value) && _ports.GetDirection(face.Value) != PortDirection.None;
        }

        private CubeFace? FaceTowards(Vector3 worldPos)
        {
            Vector3 to = worldPos - transform.position;
            if (to.sqrMagnitude < 1e-4f) return null;
            Vector3 dir = to.normalized;
            CubeFace best = CubeFace.PosX; float bestDot = -1f;
            for (int i = 0; i < 6; i++)
            {
                var f = (CubeFace)i;
                float dot = Vector3.Dot(dir, _ports.FaceNormal(f));
                if (dot > bestDot) { bestDot = dot; best = f; }
            }
            return bestDot >= 0.5f ? best : (CubeFace?)null;
        }

        // ── Logistics ───────────────────────────────────────────────────────
        // Reusable buffer of this tick's active OUTPUT faces (avoids per-tick alloc).
        private readonly List<CubeFace> _activeOut = new(6);

        private void ExtractToPipes()
        {
            var list = _host.GetPortContainers();
            if (list == null || list.Count == 0) return;

            // Collect enabled OUTPUT faces in fixed face order.
            _activeOut.Clear();
            foreach (var port in _ports.ports)
                if (port.enabled && port.direction == PortDirection.Output)
                    _activeOut.Add(port.face);
            if (_activeOut.Count == 0) return;

            // ROUND-ROBIN: rotate the starting face each tick so every competing
            // output gets first dibs in turn (fair split). PRIORITY: keep fixed
            // order (the face listed first always served first).
            int start = 0;
            if (distribution == DistributionMode.RoundRobin)
            {
                start = _rrCursor % _activeOut.Count;
                _rrCursor = (_rrCursor + 1) % _activeOut.Count;
            }

            for (int n = 0; n < _activeOut.Count; n++)
            {
                CubeFace face = _activeOut[(start + n) % _activeOut.Count];

                int ci = Mathf.Clamp(GetFaceContainer(face), 0, list.Count - 1);
                var src = list[ci];
                if (!src.CanOutput || src.Container == null) continue;
                if (ContainerEmpty(src.Container)) continue;

                var pipe = FindPipeOnFace(face);
                if (pipe == null) continue;

                int budget = pushPerTick;
                for (int i = 0; i < src.Container.Size && budget > 0; i++)
                {
                    var stack = src.Container.GetSlot(i);
                    if (stack.IsEmpty) continue;
                    if (!PassesFilter(face, stack.item)) continue;

                    int cap = pipe.GetInputCapacity(stack.item);
                    if (cap <= 0) continue;

                    int want     = Mathf.Min(budget, Mathf.Min(cap, stack.count));
                    int accepted = pipe.TryInsert(stack.item, want);
                    if (accepted <= 0) continue;

                    int removed = src.Container.Remove(stack.item, accepted);
                    budget -= removed;
                }
            }
        }

        private ItemPipe FindPipeOnFace(CubeFace face)
        {
            Vector3 facePoint = transform.position + _ports.FaceNormal(face);
            var hits = Physics.OverlapSphere(facePoint, pipeConnectRadius * 0.5f);
            ItemPipe best = null; float bestDist = float.MaxValue;
            foreach (var col in hits)
            {
                var pipe = col.GetComponentInParent<ItemPipe>();
                if (pipe == null) continue;
                float d = Vector3.SqrMagnitude(pipe.transform.position - facePoint);
                if (d < bestDist) { bestDist = d; best = pipe; }
            }
            return best;
        }

        /// <summary>
        /// Accept items pushed by a pipe through the INPUT face nearest the pipe.
        /// Routes into that face's chosen container, honouring the filter.
        /// </summary>
        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
        {
            EnsurePorts();
            if (_ports == null || _host == null || item == null || count <= 0) return 0;

            var match = _ports.GetMatchingFace(pipeWorldPos, PortDirection.Input);
            if (!match.HasValue) return 0;
            var face = match.Value.face;
            if (!PassesFilter(face, item)) return 0;

            var list = _host.GetPortContainers();
            if (list == null || list.Count == 0) return 0;
            int ci = Mathf.Clamp(GetFaceContainer(face), 0, list.Count - 1);
            var dst = list[ci];
            if (!dst.CanInput || dst.Container == null) return 0;

            int before = dst.Container.CountOf(item);
            dst.Container.Insert(new ItemStack(item, count));
            return dst.Container.CountOf(item) - before;
        }

        private static bool ContainerEmpty(ItemContainer c)
        {
            if (c == null) return true;
            for (int i = 0; i < c.Size; i++) if (!c.GetSlot(i).IsEmpty) return false;
            return true;
        }

        // ── Persistence ─────────────────────────────────────────────────────
        public ItemPortSnapshot CaptureSnapshot()
        {
            EnsurePorts();
            var snap = new ItemPortSnapshot();
            if (_ports == null) return snap;
            foreach (var p in _ports.ports)
            {
                var e = new ItemPortSnapshot.FaceEntry
                {
                    face = (int)p.face,
                    direction = (int)p.direction,
                    enabled = p.enabled,
                    containerIndex = GetFaceContainer(p.face),
                    filterMode = (int)GetFilterMode(p.face)
                };
                foreach (var def in GetFilter(p.face))
                    if (def != null) e.filterItemIds.Add(def.itemId);
                snap.faces.Add(e);
            }
            return snap;
        }

        public void ApplySnapshot(ItemPortSnapshot snap, Func<string, ItemDefinition> resolve)
        {
            EnsurePorts();
            if (snap == null || _ports == null) return;
            foreach (var e in snap.faces)
            {
                var face = (CubeFace)e.face;
                _ports.SetFaceEnabled(face, e.enabled);
                _ports.SetDirection(face, (PortDirection)e.direction);
                SetFaceContainer(face, e.containerIndex);
                SetFilterMode(face, (FilterMode)e.filterMode);
                ClearFilter(face);
                if (e.filterItemIds != null && resolve != null)
                    foreach (var id in e.filterItemIds)
                    {
                        var def = resolve(id);
                        if (def != null) AddFilter(face, def);
                    }
            }
            _ports.RefreshIndicators();
        }
    }

    /// <summary>Serializable snapshot of a machine's item-port config (faces + routing + filters).</summary>
    [Serializable]
    public class ItemPortSnapshot
    {
        [Serializable]
        public class FaceEntry
        {
            public int face;
            public int direction;
            public bool enabled;
            public int containerIndex;
            public int filterMode;   // 0 = Whitelist, 1 = Blacklist
            public List<string> filterItemIds = new();
        }

        public List<FaceEntry> faces = new();
        public bool HasData => faces != null && faces.Count > 0;
    }
}
