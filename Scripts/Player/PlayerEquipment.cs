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

        public bool HasUsableJetpack => GetBestJetpackStack(requireFuel: true) != null;

        /// <summary>Best equipped pack definition (may be empty of fuel).</summary>
        public JetpackItem GetBestJetpack() => GetBestJetpackStack(requireFuel: false)?.item as JetpackItem;

        /// <summary>Best equipped pack that still has fuel/charge (or needs none).</summary>
        public ItemStack GetBestJetpackStack(bool requireFuel = true)
        {
            EnsureContainers();
            EnsureAllJetpackFuelInitialized();
            ItemStack best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var stack = _jetpackSlots.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item is not JetpackItem pack) continue;
                EnsureJetpackFuel(stack);
                if (requireFuel && NeedsFuel(pack) && stack.durability <= 0) continue;
                float score = pack.flightSpeedMultiplier + pack.boostMultiplier * 0.25f;
                // Prefer packs with more remaining fuel when scores are close.
                score += Mathf.Clamp01(stack.durability / (float)Mathf.Max(1, pack.FuelCapacity)) * 0.05f;
                if (score > bestScore) { bestScore = score; best = stack; }
            }
            return best;
        }

        public float FlightSpeedMultiplier
        {
            get
            {
                var s = GetBestJetpackStack(requireFuel: true);
                var pack = s?.item as JetpackItem;
                return pack != null ? Mathf.Max(0.1f, pack.flightSpeedMultiplier) : 1f;
            }
        }

        public float BoostMultiplier
        {
            get
            {
                var s = GetBestJetpackStack(requireFuel: true);
                var pack = s?.item as JetpackItem;
                return pack != null ? Mathf.Max(1f, pack.boostMultiplier) : 1f;
            }
        }

        public static bool NeedsFuel(JetpackItem pack)
            => pack != null && (pack.usesHydrogen || pack.usesPower);

        public static void EnsureJetpackFuel(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || stack.item is not JetpackItem pack) return;
            int cap = pack.FuelCapacity;
            // Durability is authoritative fuel (saved). Clamp only — never refill empties
            // here, or load-from-save would top up drained packs (payload is NonSerialized).
            // New stacks are filled in ItemStack's constructor via JetpackItem.FuelCapacity.
            if (stack.durability > cap) stack.durability = cap;
            if (stack.durability < 0) stack.durability = 0;
        }

        public void EnsureAllJetpackFuelInitialized()
        {
            EnsureContainers();
            for (int i = 0; i < _jetpackSlots.Size; i++)
                EnsureJetpackFuel(_jetpackSlots.GetSlot(i));
        }

        /// <summary>
        /// Drain fuel from the best usable pack. Returns false if no fuel remains
        /// (caller should cut flight). Auto-siphons cells from inventory when empty.
        /// </summary>
        public bool TryConsumeFlightFuel(float dt, bool boosting)
        {
            if (dt <= 0f) return HasUsableJetpack;
            EnsureContainers();
            TryAutoRefuelFromInventory();

            int slotIndex = -1;
            ItemStack stack = null;
            JetpackItem pack = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var s = _jetpackSlots.GetSlot(i);
                if (s == null || s.IsEmpty || s.item is not JetpackItem p) continue;
                EnsureJetpackFuel(s);
                if (NeedsFuel(p) && s.durability <= 0) continue;
                float score = p.flightSpeedMultiplier + p.boostMultiplier * 0.25f;
                if (score > bestScore) { bestScore = score; slotIndex = i; stack = s; pack = p; }
            }
            if (stack == null || pack == null) return false;
            if (!NeedsFuel(pack)) return true;

            float drain = Mathf.Max(0f, pack.drainPerSecond);
            if (boosting) drain += Mathf.Max(0f, pack.boostDrainPerSecond);
            float cost = drain * dt;
            if (cost <= 0f) return true;

            var box = stack.payload as JetpackFuelBox;
            if (box == null) { box = new JetpackFuelBox(); stack.payload = box; }
            box.frac += cost;
            int whole = Mathf.FloorToInt(box.frac);
            if (whole > 0)
            {
                box.frac -= whole;
                stack.durability = Mathf.Max(0, stack.durability - whole);
                _jetpackSlots.SetSlot(slotIndex, stack);
            }
            return stack.durability > 0;
        }

        /// <summary>Remaining fuel 0..1 for the best pack (1 if unfuelled type).</summary>
        public float BestJetpackFuel01
        {
            get
            {
                var stack = GetBestJetpackStack(requireFuel: false);
                if (stack == null || stack.item is not JetpackItem pack) return 0f;
                EnsureJetpackFuel(stack);
                if (!NeedsFuel(pack)) return 1f;
                return Mathf.Clamp01(stack.durability / (float)pack.FuelCapacity);
            }
        }

        public int BestJetpackFuelUnits
        {
            get
            {
                var stack = GetBestJetpackStack(requireFuel: false);
                if (stack == null) return 0;
                EnsureJetpackFuel(stack);
                return Mathf.Max(0, stack.durability);
            }
        }

        public int BestJetpackFuelCapacity
        {
            get
            {
                var pack = GetBestJetpack();
                return pack != null ? pack.FuelCapacity : 0;
            }
        }

        /// <summary>Siphon hydrogen/charged cells from player inventory into equipped packs.</summary>
        public int TryAutoRefuelFromInventory()
        {
            EnsureContainers();
            if (_inventory == null) _inventory = GetComponent<Inventory>();
            if (_inventory == null || _inventory.container == null) return 0;
            int total = 0;
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var stack = _jetpackSlots.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item is not JetpackItem pack) continue;
                EnsureJetpackFuel(stack);
                if (!NeedsFuel(pack)) continue;
                int space = pack.FuelCapacity - stack.durability;
                if (space <= 0) continue;
                total += RefuelStackFromInventory(i, stack, pack, space);
            }
            return total;
        }

        private int RefuelStackFromInventory(int slotIndex, ItemStack stack, JetpackItem pack, int space)
        {
            int restored = 0;
            var inv = _inventory.container;
            for (int i = 0; i < inv.Size && space > 0; i++)
            {
                var s = inv.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                if (!pack.AcceptsFuelItem(s.item)) continue;
                int per = pack.RefuelAmountFor(s.item);
                if (per <= 0) continue;
                // Consume one cell at a time.
                int got = inv.Remove(s.item, 1);
                if (got <= 0) continue;
                int add = Mathf.Min(space, per);
                stack.durability += add;
                space -= add;
                restored += add;
            }
            if (restored > 0)
            {
                if (stack.payload == null) stack.payload = new JetpackFuelBox();
                _jetpackSlots.SetSlot(slotIndex, stack);
            }
            return restored;
        }

        /// <summary>Fractional fuel accumulator (not serialized — durability is).</summary>
        private sealed class JetpackFuelBox { public float frac; }

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
