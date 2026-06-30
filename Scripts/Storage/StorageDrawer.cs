// Assets/Scripts/VoxelEngine/Storage/StorageDrawer.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlacedBlock))]
    public class StorageDrawer : MonoBehaviour, IItemContainer, VoxelEngine.Building.IPlacedBlockPayloadReceiver, VoxelEngine.Building.ICustomBlockDrop
    {
        public const int DefaultBaseStackSize = 2000;

        [Header("Drawer")]
        public int baseStackSize = DefaultBaseStackSize;
        public ItemDefinition storedItem;
        [Min(0)] public int storedCount;

        [Header("Upgrades")]
        public int maxUpgradeSlots = 12;
        public ItemContainer upgradeSlots;

        [Header("Front Display")]
        public SpriteRenderer itemIconRenderer;
        public TextMesh amountText;
        public Renderer fillRenderer;

        private readonly List<ItemStack> _slotView = new(1) { new ItemStack() };
        private BlockItem _originalBlockItem;

        public string Name => "Storage Drawer";
        public IReadOnlyList<ItemStack> Slots { get { SyncSlotView(); return _slotView; } }
        public event Action OnChanged;

        public int Capacity => Mathf.Max(1, baseStackSize) * StackMultiplier;
        public int FreeSpace => Mathf.Max(0, Capacity - storedCount);
        public bool HasVoidUpgrade => CountUpgrades(StorageDrawerUpgradeKind.Void) > 0;

        public int StackMultiplier
        {
            get
            {
                int best = 1;
                EnsureContainers();
                for (int i = 0; i < upgradeSlots.Size; i++)
                {
                    var s = upgradeSlots.GetSlot(i);
                    if (!s.IsEmpty && s.item is StorageDrawerUpgradeItem up && up.upgradeKind == StorageDrawerUpgradeKind.StackLimit)
                        best = Mathf.Max(best, Mathf.Max(1, up.stackMultiplier));
                }
                return best;
            }
        }

        private void Awake()
        {
            _originalBlockItem = GetComponent<PlacedBlock>()?.Item;
            RemoveLegacyPipeConfig();
            EnsureContainers();
            RefreshDisplay();
        }

        private void OnEnable()
        {
            EnsureContainers();
            if (upgradeSlots != null) upgradeSlots.OnChanged += HandleUpgradeChanged;
            RefreshDisplay();
        }

        private void OnDisable()
        {
            if (upgradeSlots != null) upgradeSlots.OnChanged -= HandleUpgradeChanged;
        }

        private void RemoveLegacyPipeConfig()
        {
            var routing = GetComponent<VoxelEngine.Transport.ItemPortRouting>();
            if (routing != null) Destroy(routing);
            var ports = GetComponent<VoxelEngine.Transport.PortConfig>();
            if (ports != null) Destroy(ports);
        }

        public void EnsureContainers()
        {
            int size = Mathf.Max(1, maxUpgradeSlots);
            if (upgradeSlots == null) upgradeSlots = new ItemContainer("Drawer Upgrades", size);
            else upgradeSlots.Resize(size);
            upgradeSlots.AcceptFilter = (item, wanted) => item is StorageDrawerUpgradeItem ? wanted : 0;
        }

        private void HandleUpgradeChanged()
        {
            if (storedCount > Capacity)
            {
                int overflow = storedCount - Capacity;
                storedCount = Capacity;
                if (!HasVoidUpgrade && storedItem != null)
                    DroppedItem.Spawn(new ItemStack(storedItem, overflow), transform.position + Vector3.up * 0.8f, Vector3.up);
            }
            RaiseChanged();
        }

        public ItemStack Insert(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty) return null;
            int accepted = InsertItems(stack.item, stack.count);
            int left = stack.count - accepted;
            return left > 0 ? new ItemStack(stack.item, left) { durability = stack.durability, payload = stack.payload } : null;
        }

        public int InsertItems(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            if (storedItem != null && storedItem != item) return 0;
            if (storedItem == null) storedItem = item;

            int accepted = Mathf.Min(count, FreeSpace);
            storedCount += accepted;

            int overflow = count - accepted;
            if (overflow > 0 && HasVoidUpgrade) accepted += overflow;

            if (storedCount <= 0) storedItem = null;
            RaiseChanged();
            return accepted;
        }

        public int Remove(ItemDefinition item, int count)
        {
            if (item == null || item != storedItem || count <= 0) return 0;
            int take = Mathf.Min(count, storedCount);
            storedCount -= take;
            if (storedCount <= 0) { storedCount = 0; storedItem = null; }
            if (take > 0) RaiseChanged();
            return take;
        }

        public int CountOf(ItemDefinition item) => item != null && item == storedItem ? storedCount : 0;

        public void SetSlot(int index, ItemStack stack)
        {
            if (index != 0) return;
            if (stack == null || stack.IsEmpty)
            {
                storedItem = null;
                storedCount = 0;
            }
            else
            {
                storedItem = stack.item;
                storedCount = Mathf.Min(stack.count, Capacity);
            }
            RaiseChanged();
        }

        public ItemStack GetSlot(int index)
        {
            if (index != 0 || storedItem == null || storedCount <= 0) return new ItemStack();
            return new ItemStack(storedItem, storedCount);
        }

        public bool TryPlayerInsert(Inventory inventory, bool allMatching)
        {
            if (inventory == null || inventory.container == null) return false;
            var active = inventory.ActiveStack;
            if (active.IsEmpty) return false;
            ItemDefinition target = active.item;
            int moved = 0;

            if (allMatching)
            {
                for (int i = 0; i < inventory.container.Size; i++)
                {
                    var s = inventory.container.GetSlot(i);
                    if (s.IsEmpty || s.item != target) continue;
                    int before = s.count;
                    int accepted = InsertItems(target, before);
                    if (accepted <= 0) break;
                    inventory.container.Remove(target, Mathf.Min(before, accepted));
                    moved += accepted;
                }
            }
            else
            {
                int accepted = InsertItems(target, active.count);
                if (accepted > 0)
                {
                    inventory.container.Remove(target, Mathf.Min(active.count, accepted));
                    moved += accepted;
                }
            }
            return moved > 0;
        }

        public bool TryPlayerExtract(Inventory inventory, int amount = 1)
        {
            if (inventory == null || inventory.container == null || storedItem == null || storedCount <= 0) return false;
            int want = Mathf.Clamp(amount, 1, storedCount);
            var item = storedItem;
            var leftover = inventory.container.Insert(new ItemStack(item, want));
            int accepted = want - (leftover?.count ?? 0);
            if (accepted <= 0) return false;
            Remove(item, accepted);
            return true;
        }

        public void ApplyPlacedPayload(ItemStack sourceStack)
        {
            if (sourceStack?.payload is DrawerItemPayload payload)
            {
                storedItem = payload.storedItem;
                storedCount = Mathf.Max(0, payload.storedCount);
                _originalBlockItem = payload.originalItem;
                EnsureContainers();
                if (payload.upgrades != null)
                {
                    for (int i = 0; i < upgradeSlots.Size && i < payload.upgrades.Count; i++)
                        upgradeSlots.SetSlot(i, payload.upgrades[i]?.Clone() ?? new ItemStack());
                }
                RefreshDisplay();
            }
        }

        public ItemStack CreateBlockDrop(BlockItem originalItem)
        {
            bool hasUpgrades = false;
            EnsureContainers();
            for (int i = 0; i < upgradeSlots.Size; i++)
            {
                if (!upgradeSlots.GetSlot(i).IsEmpty) { hasUpgrades = true; break; }
            }

            // No contents and no drawer-specific state: return the normal stackable block item.
            if ((storedItem == null || storedCount <= 0) && !hasUpgrades)
                return new ItemStack(_originalBlockItem != null ? _originalBlockItem : originalItem, 1);

            var payload = new DrawerItemPayload
            {
                instanceId = System.Guid.NewGuid().ToString("N"),
                originalItem = _originalBlockItem != null ? _originalBlockItem : originalItem,
                storedItem = storedItem,
                storedCount = storedCount,
                upgrades = new List<ItemStack>()
            };
            for (int i = 0; i < upgradeSlots.Size; i++)
                payload.upgrades.Add(upgradeSlots.GetSlot(i).Clone());

            return CreatePackedDrawerStack(payload.originalItem != null ? payload.originalItem : originalItem, payload);
        }

        public static ItemStack CreatePackedDrawerStack(BlockItem baseItem, DrawerItemPayload payload)
        {
            if (payload == null) return baseItem != null ? new ItemStack(baseItem, 1) : new ItemStack();
            if (string.IsNullOrWhiteSpace(payload.instanceId)) payload.instanceId = System.Guid.NewGuid().ToString("N");
            payload.originalItem = payload.originalItem != null ? payload.originalItem : baseItem;

            var packedItem = ScriptableObject.CreateInstance<BlockItem>();
            packedItem.itemId = (baseItem != null ? baseItem.itemId : "storage_drawer") + "_packed_" + payload.instanceId;
            packedItem.displayName = baseItem != null ? baseItem.displayName + " (Packed)" : "Packed Storage Drawer";
            packedItem.description = "Packed drawer carrying its stored item contents and upgrades.";
            packedItem.icon = baseItem != null ? baseItem.icon : null;
            packedItem.iconTint = baseItem != null ? baseItem.iconTint : iconTintFallback;
            packedItem.maxStack = 1;
            packedItem.massPerUnit = baseItem != null ? baseItem.massPerUnit : 4f;
            packedItem.category = baseItem != null ? baseItem.category : "Storage";
            packedItem.placedPrefab = baseItem != null ? baseItem.placedPrefab : null;
            packedItem.gridSize = baseItem != null ? baseItem.gridSize : Vector3Int.one;
            packedItem.allowStacking = baseItem != null && baseItem.allowStacking;
            packedItem.blockHealth = baseItem != null ? baseItem.blockHealth : 350;
            packedItem.miningTier = baseItem != null ? baseItem.miningTier : 1;
            packedItem.placedMaterial = baseItem != null ? baseItem.placedMaterial : null;
            packedItem.texture = baseItem != null ? baseItem.texture : null;
            return new ItemStack(packedItem, 1) { payload = payload };
        }

        private static readonly Color iconTintFallback = new Color(0.22f, 0.30f, 0.30f);

        [Serializable]
        public class DrawerItemPayload
        {
            public string instanceId;
            public BlockItem originalItem;
            public ItemDefinition storedItem;
            public int storedCount;
            public List<ItemStack> upgrades = new();
        }

        private int CountUpgrades(StorageDrawerUpgradeKind kind)
        {
            int n = 0;
            EnsureContainers();
            for (int i = 0; i < upgradeSlots.Size; i++)
            {
                var s = upgradeSlots.GetSlot(i);
                if (!s.IsEmpty && s.item is StorageDrawerUpgradeItem up && up.upgradeKind == kind)
                    n += Mathf.Max(1, s.count);
            }
            return n;
        }

        private void SyncSlotView()
        {
            _slotView[0] = GetSlot(0);
        }

        private void RaiseChanged()
        {
            SyncSlotView();
            RefreshDisplay();
            OnChanged?.Invoke();
        }

        public void RefreshDisplay()
        {
            if (itemIconRenderer != null)
            {
                itemIconRenderer.sprite = storedItem != null ? storedItem.icon : null;
                itemIconRenderer.color = storedItem != null ? storedItem.iconTint : new Color(0.12f, 0.14f, 0.16f, 0.65f);
                itemIconRenderer.enabled = storedItem != null || itemIconRenderer.sprite != null;
            }
            if (amountText != null)
            {
                amountText.text = storedItem == null ? "EMPTY" : FormatAmount(storedCount);
                amountText.color = storedItem == null ? new Color(0.55f,0.62f,0.66f) : Color.white;
            }
            if (fillRenderer != null)
            {
                float t = Capacity <= 0 ? 0f : Mathf.Clamp01(storedCount / (float)Capacity);
                fillRenderer.material.color = Color.Lerp(new Color(0.08f,0.12f,0.14f), new Color(0.10f,0.78f,0.65f), t);
            }
        }

        public static string FormatAmount(int count)
        {
            if (count >= 1_000_000) return (count / 1_000_000f).ToString("0.#") + "M";
            if (count >= 1_000) return (count / 1_000f).ToString("0.#") + "K";
            return count.ToString();
        }
    }
}
