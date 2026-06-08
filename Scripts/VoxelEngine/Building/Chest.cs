// Assets/Scripts/VoxelEngine/Building/Chest.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Building
{
    /// <summary>
    /// Storage container with ADVANCED PORT CONFIGURATION.
    ///
    /// • Press the Interact key while looking at it to open the player's inventory
    ///   alongside this chest's container — and its per-face Port panel.
    /// • Each of the six cube faces can be set to None / Input / Output and given
    ///   an optional item whitelist:
    ///       OUTPUT face → the chest PUSHES matching items into adjacent ItemPipes.
    ///       INPUT  face → the chest ACCEPTS matching items pushed in by pipes.
    ///   Faces with no filter pass everything; a populated filter passes only the
    ///   listed items.
    ///
    /// The chest is the ACTIVE logistics endpoint (implements <see cref="IInventoryInterface"/>):
    /// it extracts its own contents into pipes each tick, while <see cref="ItemPipe"/>
    /// honours the chest's INPUT faces + filters when inserting.
    /// </summary>
    [RequireComponent(typeof(PortConfig))]
    public class Chest : MonoBehaviour, IInventoryInterface
    {
        [Tooltip("Number of slots inside this chest.")]
        public int size = 30;
        [Tooltip("Display name shown above the panel.")]
        public string displayName = "Chest";

        [Header("Logistics")]
        [Tooltip("Seconds between automatic OUTPUT extraction ticks.")]
        public float pushInterval = 0.5f;
        [Tooltip("Max items moved out per OUTPUT face per tick.")]
        public int pushPerTick = 8;
        [Tooltip("Distance at which OUTPUT faces locate adjacent ItemPipes.")]
        public float pipeConnectRadius = 1.6f;

        public ItemContainer container;

        /// <summary>
        /// Per-face item whitelists, keyed by face. An empty / missing list means
        /// "no filter" (everything passes). Only the item identity matters, so we
        /// store <see cref="ItemDefinition"/> references directly.
        /// </summary>
        [System.NonSerialized] private readonly Dictionary<CubeFace, List<ItemDefinition>> _faceFilters = new();

        private PortConfig _ports;
        private float _pushTimer;

        // ── IInventoryInterface ────────────────────────────────────────────
        public ItemContainer GetOutputContainer() => container;
        public ItemContainer GetInputContainer()  => container;
        public bool HasOutputReady => container != null && _ports != null && _ports.HasAnyOutput() && !ContainerEmpty();
        public bool CanAcceptInput => container != null && _ports != null && _ports.HasAnyInput();

        // ── Lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (container == null) container = new ItemContainer(displayName, size);
            else container.Resize(size);

            _ports = GetComponent<PortConfig>();
            if (_ports == null) _ports = gameObject.AddComponent<PortConfig>();
            _ports.EnsureAllFaces();

            // Chests are item-only endpoints — lock every face's network filter to
            // "Any" item transport. (The PortConfig network-type concept is reused
            // by cables/pipes; for chests we drive transport via this component.)
        }

        private void Update()
        {
            if (_ports == null || container == null) return;
            if (!_ports.HasAnyOutput()) return;

            _pushTimer += Time.deltaTime;
            if (_pushTimer < pushInterval) return;
            _pushTimer -= pushInterval;

            ExtractToPipes();
        }

        // ── Per-face item filter API (used by UI + persistence) ─────────────

        /// <summary>Read-only snapshot of a face's whitelist (never null).</summary>
        public IReadOnlyList<ItemDefinition> GetFilter(CubeFace face)
        {
            return _faceFilters.TryGetValue(face, out var list) ? list : System.Array.Empty<ItemDefinition>();
        }

        /// <summary>Does this face currently restrict to a whitelist?</summary>
        public bool HasFilter(CubeFace face)
            => _faceFilters.TryGetValue(face, out var list) && list != null && list.Count > 0;

        /// <summary>Add an item to a face's whitelist (no duplicates).</summary>
        public void AddFilter(CubeFace face, ItemDefinition item)
        {
            if (item == null) return;
            if (!_faceFilters.TryGetValue(face, out var list) || list == null)
            {
                list = new List<ItemDefinition>();
                _faceFilters[face] = list;
            }
            if (!list.Contains(item)) list.Add(item);
        }

        /// <summary>Remove an item from a face's whitelist.</summary>
        public void RemoveFilter(CubeFace face, ItemDefinition item)
        {
            if (item == null) return;
            if (_faceFilters.TryGetValue(face, out var list) && list != null)
                list.Remove(item);
        }

        /// <summary>Clear a face's whitelist entirely (passes everything again).</summary>
        public void ClearFilter(CubeFace face)
        {
            if (_faceFilters.TryGetValue(face, out var list) && list != null)
                list.Clear();
        }

        /// <summary>
        /// True if <paramref name="item"/> is allowed through <paramref name="face"/>.
        /// An empty whitelist allows everything; otherwise only listed items pass.
        /// </summary>
        public bool PassesFilter(CubeFace face, ItemDefinition item)
        {
            if (item == null) return false;
            if (!_faceFilters.TryGetValue(face, out var list) || list == null || list.Count == 0)
                return true;
            return list.Contains(item);
        }

        // ── Logistics ───────────────────────────────────────────────────────

        /// <summary>
        /// For every enabled OUTPUT face, locate the adjacent ItemPipe(s) and push
        /// matching items into them, respecting the per-face whitelist.
        /// </summary>
        private void ExtractToPipes()
        {
            if (ContainerEmpty()) return;

            foreach (var port in _ports.ports)
            {
                if (!port.enabled || port.direction != PortDirection.Output) continue;

                var pipe = FindPipeOnFace(port.face);
                if (pipe == null) continue;

                int budget = pushPerTick;
                for (int i = 0; i < container.Size && budget > 0; i++)
                {
                    var stack = container.GetSlot(i);
                    if (stack.IsEmpty) continue;
                    if (!PassesFilter(port.face, stack.item)) continue;

                    int cap = pipe.GetInputCapacity(stack.item);
                    if (cap <= 0) continue;

                    int want     = Mathf.Min(budget, Mathf.Min(cap, stack.count));
                    int accepted = pipe.TryInsert(stack.item, want);
                    if (accepted <= 0) continue;

                    int removed = container.Remove(stack.item, accepted);
                    budget -= removed;
                }
            }
        }

        /// <summary>
        /// Returns the ItemPipe sitting on the world-space cell just off the given
        /// face, or null if none. Uses a tight overlap test so only the directly
        /// adjacent pipe (not diagonal neighbours) is matched.
        /// </summary>
        private ItemPipe FindPipeOnFace(CubeFace face)
        {
            Vector3 facePoint = transform.position + _ports.FaceNormal(face);
            var hits = Physics.OverlapSphere(facePoint, pipeConnectRadius * 0.5f);
            ItemPipe best = null;
            float bestDist = float.MaxValue;
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
        /// Insert items arriving via a pipe through the face nearest the pipe's
        /// world position. Honours INPUT faces + their whitelists. Returns the
        /// number of items actually accepted.
        /// </summary>
        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
        {
            if (container == null || item == null || count <= 0) return 0;
            if (_ports == null) return 0;

            // Which INPUT face faces the pipe?
            var match = _ports.GetMatchingFace(pipeWorldPos, PortDirection.Input);
            if (!match.HasValue) return 0;
            if (!PassesFilter(match.Value.face, item)) return 0;

            int before = container.CountOf(item);
            container.Insert(new ItemStack(item, count));
            int after  = container.CountOf(item);
            return after - before;
        }

        private bool ContainerEmpty()
        {
            if (container == null) return true;
            for (int i = 0; i < container.Size; i++)
                if (!container.GetSlot(i).IsEmpty) return false;
            return true;
        }

        // ── Persistence bridge (used by WorldStatePersistence) ──────────────

        /// <summary>Flatten the port config + filters for saving.</summary>
        public ChestPortSnapshot CapturePortSnapshot()
        {
            var snap = new ChestPortSnapshot();
            if (_ports == null) return snap;
            _ports.EnsureAllFaces();
            foreach (var p in _ports.ports)
            {
                var entry = new ChestPortSnapshot.FaceEntry
                {
                    face      = (int)p.face,
                    direction = (int)p.direction,
                    enabled   = p.enabled
                };
                foreach (var def in GetFilter(p.face))
                    if (def != null) entry.filterItemIds.Add(def.itemId);
                snap.faces.Add(entry);
            }
            return snap;
        }

        /// <summary>Restore a saved port config + filters.</summary>
        public void ApplyPortSnapshot(ChestPortSnapshot snap, System.Func<string, ItemDefinition> resolveItem)
        {
            if (snap == null || _ports == null) return;
            _ports.EnsureAllFaces();
            foreach (var entry in snap.faces)
            {
                var face = (CubeFace)entry.face;
                _ports.SetFaceEnabled(face, entry.enabled);
                _ports.SetDirection(face, (PortDirection)entry.direction);
                ClearFilter(face);
                if (entry.filterItemIds != null && resolveItem != null)
                    foreach (var id in entry.filterItemIds)
                    {
                        var def = resolveItem(id);
                        if (def != null) AddFilter(face, def);
                    }
            }
            _ports.RefreshIndicators();
        }
    }

    /// <summary>
    /// Serializable snapshot of a chest's six-face port config + item whitelists.
    /// Lives at namespace scope so the persistence layer can embed it.
    /// </summary>
    [System.Serializable]
    public class ChestPortSnapshot
    {
        [System.Serializable]
        public class FaceEntry
        {
            public int face;
            public int direction;
            public bool enabled;
            public List<string> filterItemIds = new();
        }

        public List<FaceEntry> faces = new();
        public bool HasData => faces != null && faces.Count > 0;
    }
}
