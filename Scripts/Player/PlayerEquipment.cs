// Assets/Scripts/VoxelEngine/Player/PlayerEquipment.cs
//
// Lightweight player equipment container. Roadmap 11.3 starts with two dedicated
// jetpack equipment slots and a quick-equip path from the active inventory item.
// Full armor UI/oxygen/fuel persistence can build on this without changing the
// PlayerController flight contract.

using UnityEngine;
using VoxelEngine.Combat;
using VoxelEngine.Items;

namespace VoxelEngine.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerEquipment : MonoBehaviour
    {
        public const int JetpackSlotCount = 2;
        public const int HelmetSlotCount = 1;
        public const int OxygenTankSlotCount = 1;
        public const int ArmorSlotCount      = 1;

        [SerializeField] private ItemContainer _jetpackSlots;
        [SerializeField] private ItemContainer _helmetSlots;
        [SerializeField] private ItemContainer _oxygenTankSlots;
        [SerializeField] private ItemContainer _armorSlots;
        private Inventory _inventory;

        public ItemContainer JetpackSlots
        {
            get { EnsureContainers(); return _jetpackSlots; }
        }

        public ItemContainer HelmetSlots
        {
            get { EnsureContainers(); return _helmetSlots; }
        }

        public ItemContainer OxygenTankSlots
        {
            get { EnsureContainers(); return _oxygenTankSlots; }
        }

        public ItemContainer ArmorSlots
        {
            get { EnsureContainers(); return _armorSlots; }
        }

        private void Awake()
        {
            _inventory = GetComponent<Inventory>();
            EnsureContainers();
        }

        private void EnsureContainers()
        {
            if (_jetpackSlots == null) _jetpackSlots = new ItemContainer("Jetpack Slots", JetpackSlotCount);
            else _jetpackSlots.Resize(JetpackSlotCount);
            _jetpackSlots.AcceptFilter = (item, wanted) => item is JetpackItem ? Mathf.Min(1, wanted) : 0;

            if (_helmetSlots == null) _helmetSlots = new ItemContainer("Helmet Slot", HelmetSlotCount);
            else _helmetSlots.Resize(HelmetSlotCount);
            _helmetSlots.AcceptFilter = (item, wanted) => item is SpaceHelmetItem ? Mathf.Min(1, wanted) : 0;

            if (_oxygenTankSlots == null) _oxygenTankSlots = new ItemContainer("Oxygen Tank Slot", OxygenTankSlotCount);
            else _oxygenTankSlots.Resize(OxygenTankSlotCount);
            _oxygenTankSlots.AcceptFilter = (item, wanted) => item is OxygenTankItem ? Mathf.Min(1, wanted) : 0;

            if (_armorSlots == null) _armorSlots = new ItemContainer("Armor Slot", ArmorSlotCount);
            else _armorSlots.Resize(ArmorSlotCount);
            _armorSlots.AcceptFilter = (item, wanted) => item is ArmorItem ? Mathf.Min(1, wanted) : 0;
            // Keep PlayerStats.equippedArmor (read by TakeDamage) in lock-step with the slot
            // so drag-equip / shift-click / the legacy RMB path all agree on what's worn.
            _armorSlots.OnChanged -= SyncEquippedArmor;
            _armorSlots.OnChanged += SyncEquippedArmor;
        }

        public bool HasUsableJetpack => GetBestJetpack() != null;

        public JetpackItem GetBestJetpack()
        {
            EnsureContainers();
            JetpackItem best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var stack = _jetpackSlots.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item is not JetpackItem pack) continue;
                float score = pack.flightSpeedMultiplier + pack.boostMultiplier * 0.25f;
                if (score > bestScore) { bestScore = score; best = pack; }
            }
            return best;
        }

        public float FlightSpeedMultiplier => GetBestJetpack() != null
            ? Mathf.Max(0.1f, GetBestJetpack().flightSpeedMultiplier)
            : 1f;

        public float BoostMultiplier => GetBestJetpack() != null
            ? Mathf.Max(1f, GetBestJetpack().boostMultiplier)
            : 1f;

        public SpaceHelmetItem EquippedHelmet
        {
            get
            {
                EnsureContainers();
                var stack = _helmetSlots.GetSlot(0);
                return stack != null && !stack.IsEmpty ? stack.item as SpaceHelmetItem : null;
            }
        }

        public OxygenTankItem EquippedOxygenTank
        {
            get
            {
                EnsureContainers();
                var stack = _oxygenTankSlots.GetSlot(0);
                return stack != null && !stack.IsEmpty ? stack.item as OxygenTankItem : null;
            }
        }

        /// <summary>Currently worn Crusader armor (drives PlayerStats damage mitigation).</summary>
        public ArmorItem EquippedArmor
        {
            get
            {
                EnsureContainers();
                var stack = _armorSlots.GetSlot(0);
                return stack != null && !stack.IsEmpty ? stack.item as ArmorItem : null;
            }
        }

        private void SyncEquippedArmor()
        {
            var ps = PlayerStats.Instance;
            if (ps != null) ps.equippedArmor = EquippedArmor;
        }

        public bool HasBreathingKit => EquippedHelmet != null && EquippedHelmet.sealedHelmet && EquippedOxygenTank != null;
        public float BonusOxygen => HasBreathingKit ? Mathf.Max(0f, EquippedOxygenTank.bonusOxygen) : 0f;
        public float OxygenDrainMultiplier => HasBreathingKit
            ? Mathf.Clamp(EquippedOxygenTank.drainMultiplier * EquippedHelmet.oxygenEfficiency, 0.05f, 1f)
            : 1f;

        /// <summary>
        /// If the active hotbar stack is a JetpackItem, move one into the first free
        /// jetpack slot. Returns true when an item was equipped this call.
        /// </summary>
        public bool TryQuickEquipActiveJetpack()
        {
            EnsureContainers();
            if (_inventory == null) _inventory = GetComponent<Inventory>();
            if (_inventory == null || _inventory.container == null) return false;
            var active = _inventory.ActiveStack;
            if (active == null || active.IsEmpty || active.item is not JetpackItem pack) return false;

            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var slot = _jetpackSlots.GetSlot(i);
                if (slot != null && !slot.IsEmpty) continue;
                _jetpackSlots.SetSlot(i, new ItemStack { item = pack, count = 1 });
                _inventory.container.Remove(pack, 1);
                VoxelEngine.UI.BuildFeedbackHud.Show("Jetpack Equipped", pack.displayName, pack.icon, pack.iconTint);
                return true;
            }

            VoxelEngine.UI.BuildFeedbackHud.Show("Jetpack Slots Full", "Two jetpack slots are already occupied", pack.icon, Color.yellow);
            return false;
        }
    }
}
