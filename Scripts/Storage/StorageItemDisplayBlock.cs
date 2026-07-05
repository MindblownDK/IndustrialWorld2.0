// Assets/Scripts/VoxelEngine/Storage/StorageItemDisplayBlock.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlacedBlock))]
    public class StorageItemDisplayBlock : MonoBehaviour
    {
        [Header("Connection")]
        public float rackRadius = 16f;
        public float refreshInterval = 0.5f;

        [Header("Filter")]
        public ItemDefinition filterItem;

        [Header("Front Display")]
        public SpriteRenderer itemIconRenderer;
        public TextMesh amountText;
        public Renderer statusRenderer;

        public ServerRack ConnectedRack { get; private set; }
        public IItemContainer FilterSlot => _filterSlot;

        private FilterSlotContainer _filterSlot;
        private float _timer;

        private void Awake()
        {
            _filterSlot = new FilterSlotContainer(this);
            RefreshVisuals();
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < refreshInterval) return;
            _timer = 0f;
            FindRack();
            RefreshVisuals();
        }

        public void SetFilter(ItemDefinition item)
        {
            filterItem = item;
            _filterSlot?.RaiseChanged();
            RefreshVisuals();
        }

        private void FindRack()
        {
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ServerRack best = null;
            float bestD = rackRadius * rackRadius;
            foreach (var rack in racks)
            {
                if (rack == null || !rack.IsOnline) continue;
                float d = (rack.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = rack; }
            }
            ConnectedRack = best;
        }

        public int CurrentCount => ConnectedRack != null && filterItem != null ? ConnectedRack.NetworkCount(filterItem.itemId) : 0;

        public void RefreshVisuals()
        {
            if (itemIconRenderer != null)
            {
                itemIconRenderer.sprite = filterItem != null ? filterItem.icon : null;
                itemIconRenderer.color = filterItem != null ? filterItem.iconTint : new Color(0.12f,0.14f,0.16f,0.65f);
                itemIconRenderer.enabled = filterItem != null || itemIconRenderer.sprite != null;
            }
            if (amountText != null)
                amountText.text = filterItem == null ? "NO FILTER" : StorageDrawer.FormatAmount(CurrentCount);
            if (statusRenderer != null)
                statusRenderer.material.color = ConnectedRack != null ? new Color(0.10f,0.78f,0.65f) : new Color(0.75f,0.20f,0.16f);
        }

        [Serializable]
        private sealed class FilterSlotContainer : IItemFilterSlot
        {
            private readonly StorageItemDisplayBlock _owner;
            private readonly List<ItemStack> _slots = new() { new ItemStack() };
            public string Name => "Display Filter";
            public IReadOnlyList<ItemStack> Slots { get { Sync(); return _slots; } }
            public event Action OnChanged;
            public FilterSlotContainer(StorageItemDisplayBlock owner) => _owner = owner;
            public ItemStack Insert(ItemStack stack)
            {
                if (stack == null || stack.IsEmpty) return null;
                _owner.SetFilter(stack.item);
                return stack;
            }
            public int Remove(ItemDefinition item, int count)
            {
                if (_owner.filterItem == item) { _owner.SetFilter(null); return 0; }
                return 0;
            }
            public int CountOf(ItemDefinition item) => _owner.filterItem == item ? 1 : 0;
            public void SetSlot(int index, ItemStack stack) => _owner.SetFilter(stack == null || stack.IsEmpty ? null : stack.item);
            public void ApplyFilter(ItemDefinition item) => _owner.SetFilter(item);
            public ItemStack GetSlot(int index) => _owner.filterItem != null ? new ItemStack(_owner.filterItem, 1) : new ItemStack();
            public void RaiseChanged() { Sync(); OnChanged?.Invoke(); }
            private void Sync() => _slots[0] = GetSlot(0);
        }
    }
}
