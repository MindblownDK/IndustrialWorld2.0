// Assets/Scripts/VoxelEngine/Storage/StorageDrawerController.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Storage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class StorageDrawerController : MonoBehaviour, IExternalStorageSource, IItemPortHost, IDirectItemPortEndpoint
    {
        public float drawerRadius = 12f;
        public float rackRadius = 16f;
        public float refreshInterval = 1f;
        public int priority = 100;

        [Header("Pipe Logistics")]
        public float pushInterval = 0.5f;
        public int pushPerTick = 64;
        public float pipeConnectRadius = 1.6f;

        public ServerRack ConnectedRack { get; private set; }
        public IReadOnlyList<StorageDrawer> Drawers => _drawers;
        public bool IsAvailable => isActiveAndEnabled && ConnectedRack != null;
        public int Priority => priority;
        public PortConfig PortConfig { get { EnsureRefs(); return _ports; } }
        public IReadOnlyList<ItemPortContainer> GetPortContainers() => _emptyPortContainers;

        private readonly List<StorageDrawer> _drawers = new();
        private readonly List<StorageDrawer> _scratch = new();
        private readonly ItemPortContainer[] _emptyPortContainers = Array.Empty<ItemPortContainer>();
        private PortConfig _ports;
        private ItemPortRouting _routing;
        private float _timer;
        private float _pushTimer;

        private void Awake() => EnsureRefs();

        private void OnEnable()
        {
            EnsureRefs();
            RefreshLinks();
        }

        private void OnDisable()
        {
            if (ConnectedRack != null) ConnectedRack.UnregisterExternalStorage(this);
            ConnectedRack = null;
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= refreshInterval)
            {
                _timer = 0f;
                RefreshLinks();
            }

            _pushTimer += Time.deltaTime;
            if (_pushTimer >= pushInterval)
            {
                _pushTimer -= pushInterval;
                PushToOutputPipes();
            }
        }

        private void EnsureRefs()
        {
            if (_ports == null)
            {
                _ports = GetComponent<PortConfig>();
                if (_ports == null) _ports = gameObject.AddComponent<PortConfig>();
                _ports.EnsureAllFaces();
            }
            if (_routing == null)
            {
                _routing = GetComponent<ItemPortRouting>();
                if (_routing == null) _routing = gameObject.AddComponent<ItemPortRouting>();
                _routing.enabled = false;
            }
        }

        public void RefreshLinks()
        {
            EnsureRefs();
            FindRack();
            FindDrawers();
            if (ConnectedRack != null) ConnectedRack.RegisterExternalStorage(this);
        }

        private void FindRack()
        {
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ServerRack best = null;
            float bestD = rackRadius * rackRadius;
            foreach (var rack in racks)
            {
                if (rack == null) continue;
                float d = (rack.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = rack; }
            }

            if (ConnectedRack != null && ConnectedRack != best)
                ConnectedRack.UnregisterExternalStorage(this);
            ConnectedRack = best;
        }

        private void FindDrawers()
        {
            _drawers.Clear();
            var hits = Physics.OverlapSphere(transform.position, drawerRadius);
            foreach (var col in hits)
            {
                var drawer = col.GetComponentInParent<StorageDrawer>();
                if (drawer == null || _drawers.Contains(drawer)) continue;
                _drawers.Add(drawer);
            }
        }

        public bool HasMatchingDrawer(ItemDefinition item)
        {
            if (item == null) return false;
            foreach (var drawer in _drawers)
                if (drawer != null && drawer.storedItem == item) return true;
            return false;
        }

        public int Insert(ItemDefinition item, int count) => InsertFrom(item, count, transform.position, false);

        public int InsertFrom(ItemDefinition item, int count, Vector3 sourcePosition, bool requireExistingMatch)
        {
            if (item == null || count <= 0) return count;
            int remaining = count;
            BuildSortedDrawers(sourcePosition);

            bool foundMatch = false;
            foreach (var drawer in _scratch)
            {
                if (drawer == null || drawer.storedItem != item) continue;
                foundMatch = true;
                int accepted = drawer.InsertItems(item, remaining);
                remaining -= accepted;
                if (remaining <= 0) return 0;
            }

            if (requireExistingMatch && !foundMatch) return count;

            foreach (var drawer in _scratch)
            {
                if (drawer == null || drawer.storedItem != null) continue;
                int accepted = drawer.InsertItems(item, remaining);
                remaining -= accepted;
                if (remaining <= 0) return 0;
            }
            return remaining;
        }

        public bool TryPlayerInsert(Inventory inventory, bool allMatching, Vector3 sourcePosition, bool requireExistingMatch = false)
        {
            if (inventory == null || inventory.container == null) return false;
            var active = inventory.ActiveStack;
            if (active.IsEmpty) return false;
            ItemDefinition item = active.item;
            int moved = 0;

            if (allMatching)
            {
                for (int i = 0; i < inventory.container.Size; i++)
                {
                    var stack = inventory.container.GetSlot(i);
                    if (stack.IsEmpty || stack.item != item) continue;
                    int before = stack.count;
                    int leftover = InsertFrom(item, before, sourcePosition, requireExistingMatch);
                    int accepted = before - leftover;
                    if (accepted <= 0) continue;
                    inventory.container.Remove(item, accepted);
                    moved += accepted;
                }
            }
            else
            {
                int before = active.count;
                int leftover = InsertFrom(item, before, sourcePosition, requireExistingMatch);
                int accepted = before - leftover;
                if (accepted > 0)
                {
                    inventory.container.Remove(item, accepted);
                    moved += accepted;
                }
            }

            return moved > 0;
        }

        public int Extract(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0) return 0;
            int extracted = 0;
            foreach (var drawer in _drawers)
            {
                if (drawer == null || drawer.storedItem == null || drawer.storedItem.itemId != itemId) continue;
                extracted += drawer.Remove(drawer.storedItem, count - extracted);
                if (extracted >= count) return extracted;
            }
            return extracted;
        }

        public int CountOf(string itemId)
        {
            int total = 0;
            foreach (var drawer in _drawers)
                if (drawer != null && drawer.storedItem != null && drawer.storedItem.itemId == itemId)
                    total += drawer.storedCount;
            return total;
        }

        public void AppendAllItems(Dictionary<string, StoredItemEntry> merged)
        {
            foreach (var drawer in _drawers)
            {
                if (drawer == null || drawer.storedItem == null || drawer.storedCount <= 0) continue;
                string id = drawer.storedItem.itemId;
                if (merged.TryGetValue(id, out var ex)) ex.count += drawer.storedCount;
                else merged[id] = new StoredItemEntry
                {
                    itemId = id,
                    displayName = drawer.storedItem.displayName,
                    count = drawer.storedCount
                };
            }
        }

        public Dictionary<string, StoredItemEntry> BuildItemSummary()
        {
            var merged = new Dictionary<string, StoredItemEntry>();
            AppendAllItems(merged);
            return merged;
        }

        public bool IsFaceConnectable(Vector3 fromWorldPos)
        {
            EnsureRefs();
            var face = FaceTowards(fromWorldPos);
            return face.HasValue && _ports.IsFaceEnabled(face.Value) && _ports.GetDirection(face.Value) != PortDirection.None;
        }

        public int TryAcceptFromPipe(Vector3 pipeWorldPos, ItemDefinition item, int count)
        {
            EnsureRefs();
            var match = _ports.GetMatchingFace(pipeWorldPos, PortDirection.Input);
            if (!match.HasValue || !PassesRoutingFilter(match.Value.face, item)) return 0;
            int before = count;
            int leftover = InsertFrom(item, count, pipeWorldPos, false);
            return before - leftover;
        }

        private void PushToOutputPipes()
        {
            EnsureRefs();
            if (_ports == null || !_ports.HasAnyOutput()) return;
            foreach (var p in _ports.ports)
            {
                if (!p.enabled || p.direction != PortDirection.Output) continue;
                var pipe = FindPipeOnFace(p.face);
                if (pipe == null) continue;

                int budget = pushPerTick;
                foreach (var drawer in _drawers)
                {
                    if (drawer == null || drawer.storedItem == null || drawer.storedCount <= 0) continue;
                    if (!PassesRoutingFilter(p.face, drawer.storedItem)) continue;
                    int cap = pipe.GetInputCapacity(drawer.storedItem);
                    if (cap <= 0) continue;
                    int want = Mathf.Min(budget, Mathf.Min(cap, drawer.storedCount));
                    int accepted = pipe.TryInsert(drawer.storedItem, want);
                    if (accepted <= 0) continue;
                    drawer.Remove(drawer.storedItem, accepted);
                    budget -= accepted;
                    if (budget <= 0) break;
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

        private bool PassesRoutingFilter(CubeFace face, ItemDefinition item)
        {
            return _routing == null || _routing.PassesFilter(face, item);
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

        private void BuildSortedDrawers(Vector3 sourcePosition)
        {
            _scratch.Clear();
            foreach (var drawer in _drawers)
                if (drawer != null) _scratch.Add(drawer);
            _scratch.Sort((a, b) =>
                Vector3.SqrMagnitude(a.transform.position - sourcePosition)
                    .CompareTo(Vector3.SqrMagnitude(b.transform.position - sourcePosition)));
        }

        public static StorageDrawerController FindNearest(Vector3 position, float range = 16f)
        {
            var controllers = FindObjectsByType<StorageDrawerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            StorageDrawerController best = null;
            float bestD = range * range;
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                float d = (controller.transform.position - position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = controller; }
            }
            return best;
        }
    }
}
