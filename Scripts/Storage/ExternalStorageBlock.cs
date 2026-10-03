// Assets/Scripts/VoxelEngine/Storage/ExternalStorageBlock.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                   EXTERNAL STORAGE (14.41.0)                     ║
// ║  The bridge between the digital network and PHYSICAL containers. ║
// ║  Place it against a Chest or a lone Storage Drawer (or run a    ║
// ║  Data Pipe to it) and every touching container becomes part of  ║
// ║  the network: terminals see, count and (mode permitting) store  ║
// ║  and pull those items as if they lived on a disk.               ║
// ║                                                                  ║
// ║  • MODE: Insert + Extract / Extract only / Insert only          ║
// ║  • PRIORITY: ranks against NAS shelves and drawer controllers   ║
// ║  • Drawer Controllers do NOT need this block - they join the    ║
// ║    network directly by pipe or touch.                           ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    public enum ExternalStorageMode
    {
        InsertAndExtract = 0,
        ExtractOnly      = 1,
        InsertOnly       = 2
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlacedBlock))]
    public class ExternalStorageBlock : MonoBehaviour, IExternalStorageSource
    {
        [Header("Network")]
        [Tooltip("Higher priority sources are filled and drained first.")]
        public int priority = 50;
        [Tooltip("What the network may do with the attached containers.")]
        public ExternalStorageMode mode = ExternalStorageMode.InsertAndExtract;

        /// <summary>System watts this bridge draws from the network budget.</summary>
        public const float DRAW_WATTS = 6f;

        /// <summary>How far past this block's own bounds we look for touching
        /// containers. Matches the storage network's touch philosophy.</summary>
        private const float TOUCH_PADDING = 0.20f;
        private const float REFRESH_SECONDS = 1f;

        public ServerRack ConnectedRack { get; private set; }

        /// <summary>The physical containers currently bridged (chests and lone
        /// drawers touching this block).</summary>
        public IReadOnlyList<IItemContainer> Targets => _targets;
        /// <summary>Display names for the UI, index-aligned with Targets.</summary>
        public IReadOnlyList<string> TargetNames => _targetNames;

        private readonly List<IItemContainer> _targets = new();
        private readonly List<string> _targetNames = new();
        private readonly HashSet<object> _seen = new();
        private static readonly Collider[] s_probe = new Collider[64];
        private float _timer = REFRESH_SECONDS; // first Update refreshes instantly

        // ── IExternalStorageSource ─────────────────────────────────
        public bool IsAvailable => isActiveAndEnabled && ConnectedRack != null && _targets.Count > 0;
        public int Priority => priority;

        public void SetPriority(int value) => priority = Mathf.Clamp(value, -99, 999);
        public void SetMode(ExternalStorageMode m) => mode = m;

        public void CycleMode()
        {
            mode = mode switch
            {
                ExternalStorageMode.InsertAndExtract => ExternalStorageMode.ExtractOnly,
                ExternalStorageMode.ExtractOnly      => ExternalStorageMode.InsertOnly,
                _                                    => ExternalStorageMode.InsertAndExtract
            };
        }

        public string ModeLabel => mode switch
        {
            ExternalStorageMode.ExtractOnly => "EXTRACT ONLY",
            ExternalStorageMode.InsertOnly  => "INSERT ONLY",
            _                               => "INSERT + EXTRACT"
        };

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < REFRESH_SECONDS) return;
            _timer = 0f;
            RefreshLinks();
        }

        /// <summary>Re-resolve the controller and the touching containers.</summary>
        public void RefreshLinks()
        {
            ConnectedRack = StorageNetwork.ControllerOf(this);
            FindTargets();
        }

        private void FindTargets()
        {
            _targets.Clear();
            _targetNames.Clear();
            _seen.Clear();

            // Probe a box slightly larger than our own render/collider bounds.
            Bounds b = new Bounds(transform.position, Vector3.one * 0.5f);
            var cols = GetComponentsInChildren<Collider>();
            foreach (var c in cols)
                if (c != null && c.enabled && !c.isTrigger) b.Encapsulate(c.bounds);
            b.Expand(TOUCH_PADDING * 2f);

            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, s_probe,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = s_probe[i];
                s_probe[i] = null;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;

                // Chests: the classic physical container.
                var chest = hit.GetComponentInParent<Building.Chest>();
                if (chest != null && chest.container != null && _seen.Add(chest))
                {
                    _targets.Add(chest.container);
                    _targetNames.Add(chest.name.Replace("(Clone)", "").Trim());
                    continue;
                }

                // Lone Storage Drawers. Drawers already owned by a Drawer
                // Controller on THIS network are skipped - the controller
                // surfaces them itself and double-listing would double-count.
                var drawer = hit.GetComponentInParent<StorageDrawer>();
                if (drawer != null && _seen.Add(drawer))
                {
                    if (IsControllerOwned(drawer)) continue;
                    _targets.Add(drawer);
                    _targetNames.Add(drawer.name.Replace("(Clone)", "").Trim());
                }
            }
        }

        /// <summary>A drawer counts as controller-owned when ANY drawer
        /// controller on the same network already lists it - the controller
        /// surfaces its drawers itself and double-listing would double-count
        /// every stack.</summary>
        private bool IsControllerOwned(StorageDrawer drawer)
        {
            if (ConnectedRack == null) return false;
            var ctrls = FindObjectsByType<StorageDrawerController>(FindObjectsSortMode.None);
            foreach (var dc in ctrls)
            {
                if (dc == null || !dc.IsAvailable || dc.ConnectedRack != ConnectedRack) continue;
                var list = dc.Drawers;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] == drawer) return true;
            }
            return false;
        }

        // ── Storage operations ─────────────────────────────────────
        public int Insert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return count;
            if (mode == ExternalStorageMode.ExtractOnly) return count;
            int remaining = count;
            foreach (var t in _targets)
            {
                if (t == null || remaining <= 0) break;
                var leftover = t.Insert(new ItemStack { item = item, count = remaining });
                remaining = leftover == null ? 0 : leftover.count;
            }
            return remaining;
        }

        public int Extract(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0) return 0;
            if (mode == ExternalStorageMode.InsertOnly) return 0;
            int extracted = 0;
            foreach (var t in _targets)
            {
                if (t == null) continue;
                for (int i = 0; i < t.Slots.Count && extracted < count; i++)
                {
                    var s = t.GetSlot(i);
                    if (s.IsEmpty || s.item == null || s.item.itemId != itemId) continue;
                    int take = Mathf.Min(s.count, count - extracted);
                    s.count -= take;
                    t.SetSlot(i, s.count <= 0 ? new ItemStack() : s);
                    extracted += take;
                }
                if (extracted >= count) break;
            }
            return extracted;
        }

        public int CountOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            int total = 0;
            foreach (var t in _targets)
            {
                if (t == null) continue;
                for (int i = 0; i < t.Slots.Count; i++)
                {
                    var s = t.GetSlot(i);
                    if (!s.IsEmpty && s.item != null && s.item.itemId == itemId)
                        total += s.count;
                }
            }
            return total;
        }

        public void AppendAllItems(Dictionary<string, StoredItemEntry> merged)
        {
            foreach (var t in _targets)
            {
                if (t == null) continue;
                for (int i = 0; i < t.Slots.Count; i++)
                {
                    var s = t.GetSlot(i);
                    if (s.IsEmpty || s.item == null) continue;
                    if (merged.TryGetValue(s.item.itemId, out var e)) e.count += s.count;
                    else merged[s.item.itemId] = new StoredItemEntry
                    {
                        itemId = s.item.itemId,
                        displayName = s.item.displayName,
                        count = s.count,
                        massPerUnit = s.item.massPerUnit <= 0f ? 1f : s.item.massPerUnit
                    };
                }
            }
        }

        // ── UI helpers ─────────────────────────────────────────────
        /// <summary>(usedSlots, totalSlots) over all bridged containers.</summary>
        public (int used, int total) SlotStats()
        {
            int used = 0, total = 0;
            foreach (var t in _targets)
            {
                if (t == null) continue;
                total += t.Slots.Count;
                for (int i = 0; i < t.Slots.Count; i++)
                    if (!t.GetSlot(i).IsEmpty) used++;
            }
            return (used, total);
        }
    }
}
