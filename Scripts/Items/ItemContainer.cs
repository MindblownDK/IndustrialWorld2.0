// Assets/Scripts/VoxelEngine/Items/ItemContainer.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// Generic fixed-slot container. Used for player inventory, chests,
    /// furnace input/output, assembler queues, etc.
    /// </summary>
    [Serializable]
    public class ItemContainer : IItemContainer
    {
        [SerializeField] private string         _name = "Container";
        [SerializeField] private List<ItemStack> _slots;
        [SerializeField] private int             _minSize = 1;   // expected size; used by self-heal

        public string Name => _name;
        // IMPORTANT: any access through this getter triggers a self-heal so stale or
        // pre-feature serialized containers can never throw IndexOutOfRange.
        public IReadOnlyList<ItemStack> Slots { get { EnsureValid(); return _slots; } }
        public event Action OnChanged;

        /// <summary>Optional gate consulted before accepting items — return how many of
        /// <c>item</c> may currently be added (e.g. a cargo container caps by mass).
        /// Null means "no extra limit". Set by the owning block.</summary>
        [NonSerialized] public Func<ItemDefinition, int, int> AcceptFilter;

        private int Allowed(ItemDefinition item, int wanted)
            => AcceptFilter == null ? wanted : Mathf.Clamp(AcceptFilter(item, wanted), 0, wanted);

        public ItemContainer() { }   // for Unity deserialization
        public ItemContainer(string name, int size)
        {
            _name = name;
            _minSize = Mathf.Max(1, size);
            _slots = new List<ItemStack>(size);
            for (int i = 0; i < size; i++) _slots.Add(new ItemStack());
        }

        /// <summary>
        /// Self-heal: if _slots is null or smaller than _minSize, pad with empty stacks.
        /// Called automatically by every public accessor. Never shrinks (would lose items).
        /// </summary>
        public void EnsureValid()
        {
            if (_slots == null) _slots = new List<ItemStack>();
            int need = Mathf.Max(1, _minSize);
            while (_slots.Count < need) _slots.Add(new ItemStack());
        }

        /// <summary>Explicitly grow/shrink the slot list. Use to repair a container whose minSize wasn't set.</summary>
        public void Resize(int newSize)
        {
            _minSize = Mathf.Max(1, newSize);
            if (_slots == null) _slots = new List<ItemStack>();
            while (_slots.Count < _minSize) _slots.Add(new ItemStack());
            while (_slots.Count > _minSize) _slots.RemoveAt(_slots.Count - 1);
        }

        public int Size { get { EnsureValid(); return _slots.Count; } }

        public ItemStack GetSlot(int i)
        {
            EnsureValid();
            if (i < 0 || i >= _slots.Count) return new ItemStack();   // never throw
            return _slots[i];
        }

        public void SetSlot(int i, ItemStack stack)
        {
            EnsureValid();
            if (i < 0 || i >= _slots.Count) return;
            _slots[i] = stack ?? new ItemStack();
            OnChanged?.Invoke();
        }

        public ItemStack Insert(ItemStack stack)
        {
            EnsureValid();
            if (stack == null || stack.IsEmpty) return null;
            return InsertRange(stack, 0, _slots.Count);
        }

        /// <summary>
        /// Insert <paramref name="stack"/> ONLY into slots [start .. start+count).
        /// Used by the hotbar quick-transfer so shift-clicking a hotbar slot
        /// pushes items into the BACKPACK range without bouncing right back
        /// into the (empty) hotbar slot we just emptied.
        /// </summary>
        public ItemStack InsertRange(ItemStack stack, int start, int count)
        {
            EnsureValid();
            if (stack == null || stack.IsEmpty) return null;
            int end = Mathf.Min(_slots.Count, start + count);
            start = Mathf.Max(0, start);
            if (end <= start) return stack;

            // Honour an optional accept gate (e.g. cargo mass cap). Anything the
            // gate refuses is held back and returned to the caller as leftover.
            int heldBack = 0;
            if (AcceptFilter != null)
            {
                int allow = Allowed(stack.item, stack.count);
                heldBack = stack.count - allow;
                if (allow <= 0) return stack;        // nothing fits
                stack.count = allow;
            }

            // Pass 1: merge into existing partial stacks inside the range.
            if (stack.item.IsStackable)
            {
                for (int i = start; i < end && stack.count > 0; i++)
                {
                    var s = _slots[i];
                    if (s.IsEmpty || s.item != stack.item) continue;
                    int space = stack.item.maxStack - s.count;
                    if (space <= 0) continue;
                    int add = Mathf.Min(space, stack.count);
                    s.count += add;
                    stack.count -= add;
                }
            }
            // Pass 2: place into the first empty slot inside the range.
            for (int i = start; i < end && stack.count > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;
                int add = stack.item.IsStackable ? Mathf.Min(stack.item.maxStack, stack.count) : 1;
                _slots[i] = new ItemStack
                {
                    item       = stack.item,
                    count      = add,
                    durability = stack.durability
                };
                stack.count -= add;
            }
            OnChanged?.Invoke();
            // Re-add anything the accept gate held back so the caller keeps it.
            stack.count += heldBack;
            return stack.count > 0 ? stack : null;
        }

        public int Remove(ItemDefinition item, int count)
        {
            EnsureValid();
            int removed = 0;
            for (int i = 0; i < _slots.Count && removed < count; i++)
            {
                var s = _slots[i];
                if (s.IsEmpty || s.item != item) continue;
                int take = Mathf.Min(s.count, count - removed);
                s.count -= take;
                removed += take;
                if (s.count <= 0) _slots[i] = new ItemStack();
            }
            if (removed > 0) OnChanged?.Invoke();
            return removed;
        }

        public int CountOf(ItemDefinition item)
        {
            EnsureValid();
            int n = 0;
            foreach (var s in _slots) if (!s.IsEmpty && s.item == item) n += s.count;
            return n;
        }

        public bool HasSpace(ItemDefinition item, int count)
        {
            EnsureValid();
            int free = 0;
            foreach (var s in _slots)
            {
                if (s.IsEmpty)                free += item.maxStack;
                else if (s.item == item && item.IsStackable) free += item.maxStack - s.count;
                if (free >= count) return true;
            }
            return free >= count;
        }

        /// <summary>Sort slots: group by item, merge partial stacks, push empties to end.</summary>
        public void Sort()
        {
            EnsureValid();
            // 1) Merge partial stacks of the same item.
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsEmpty) continue;
                for (int j = i + 1; j < _slots.Count; j++)
                {
                    if (_slots[j].IsEmpty) continue;
                    if (_slots[i].item != _slots[j].item) continue;
                    if (!_slots[i].item.IsStackable) continue;
                    int space = _slots[i].item.maxStack - _slots[i].count;
                    if (space <= 0) break;
                    int move = System.Math.Min(space, _slots[j].count);
                    _slots[i].count += move;
                    _slots[j].count -= move;
                    if (_slots[j].count <= 0) _slots[j] = new ItemStack();
                }
            }
            // 2) Sort: non-empty first, alphabetical by displayName, then by count descending.
            _slots.Sort((a, b) =>
            {
                if (a.IsEmpty && b.IsEmpty) return 0;
                if (a.IsEmpty) return 1;
                if (b.IsEmpty) return -1;
                int cmp = string.Compare(a.item.displayName, b.item.displayName, System.StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                return b.count.CompareTo(a.count);
            });
            OnChanged?.Invoke();
        }

        /// <summary>Sort only slots in range [start, end). Leaves other slots untouched.</summary>
        public void SortRange(int start, int end)
        {
            EnsureValid();
            end = System.Math.Min(end, _slots.Count);
            if (start >= end) return;

            // 1) Merge partial stacks within range.
            for (int i = start; i < end; i++)
            {
                if (_slots[i].IsEmpty) continue;
                for (int j = i + 1; j < end; j++)
                {
                    if (_slots[j].IsEmpty || _slots[i].item != _slots[j].item || !_slots[i].item.IsStackable) continue;
                    int space = _slots[i].item.maxStack - _slots[i].count;
                    if (space <= 0) break;
                    int move = System.Math.Min(space, _slots[j].count);
                    _slots[i].count += move; _slots[j].count -= move;
                    if (_slots[j].count <= 0) _slots[j] = new ItemStack();
                }
            }

            // 2) Extract range, sort, put back.
            var sub = new System.Collections.Generic.List<ItemStack>();
            for (int i = start; i < end; i++) sub.Add(_slots[i]);
            sub.Sort((a, b) => {
                if (a.IsEmpty && b.IsEmpty) return 0;
                if (a.IsEmpty) return 1; if (b.IsEmpty) return -1;
                int cmp = string.Compare(a.item.displayName, b.item.displayName, System.StringComparison.Ordinal);
                return cmp != 0 ? cmp : b.count.CompareTo(a.count);
            });
            for (int i = 0; i < sub.Count; i++) _slots[start + i] = sub[i];
            OnChanged?.Invoke();
        }

        public void RaiseChanged() => OnChanged?.Invoke();
    }
}
