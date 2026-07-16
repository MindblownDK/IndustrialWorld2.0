// Assets/Scripts/VoxelEngine/GridSystem/GridCargoContainer.cs
//
// Storage block for ships/vehicles.
// v5.43.0-dev — Implements IGridDataProvider for screen display.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridCargoContainer : GridBlock, IGridItemStore, IGridDataProvider
    {
        [Header("Cargo")]
        [Tooltip("How many visual slots the UI exposes (mass is the real limit).")]
        public int slots = 24;

        [Tooltip("Maximum cargo mass in kilograms. Small = 100 000 kg, Large = 1 000 000 kg.")]
        public float maxMassKg = 100_000f;

        [Tooltip("Optional item/category filter. Empty accepts everything.")]
        public string itemFilter = "";

        public ItemContainer container;

        public float CurrentMassKg => MassUtil.ContainerMass(container);
        public float Fill01 => maxMassKg <= 0f ? 0f : Mathf.Clamp01(CurrentMassKg / maxMassKg);

        public override float ContentMass => CurrentMassKg;

        // -- IGridItemStore ---------------------------------------------
        public ItemContainer ItemStore => container;
        public string StoreLabel => blockName == "Armor Block" ? "Cargo Container" : blockName;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (blockName == "Armor Block") blockName = "Cargo Container";
            if (container == null) container = new ItemContainer("Cargo", slots);
            else container.Resize(slots);
            ApplyFilter();
            if (Grid != null && GridItemNetwork.Instance != null)
                GridItemNetwork.Instance.RegisterContainer(Grid, this);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Grid != null && GridItemNetwork.Instance != null)
                GridItemNetwork.Instance.UnregisterStore(Grid, this);
        }

        public void SetItemFilter(string filter)
        {
            itemFilter = filter ?? "";
            ApplyFilter();
        }

        public void ApplyFilter()
        {
            if (container == null) return;
            container.AcceptFilter = MaxAcceptable;
        }

        private int MaxAcceptable(ItemDefinition item, int wanted)
        {
            if (!MatchesFilter(item)) return 0;
            if (item == null || item.massPerUnit <= 0f) return wanted;
            float free = maxMassKg - CurrentMassKg;
            if (free <= 0f) return 0;
            return Mathf.Clamp(Mathf.FloorToInt(free / item.massPerUnit), 0, wanted);
        }

        private bool MatchesFilter(ItemDefinition item)
        {
            if (item == null) return false;
            string q = (itemFilter ?? "").Trim();
            if (q.Length == 0) return true;
            return Contains(item.itemId, q)
                || Contains(item.displayName, q)
                || Contains(item.category, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public bool CanAcceptMass(ItemDefinition item, int count)
            => MatchesFilter(item) && CurrentMassKg + MassUtil.StackMass(item, count) <= maxMassKg;

        // -- IGridDataProvider -----------------------------------------
        public string SourceName => blockName;
        public string DataCategory => "Inventory";
        public string GetDisplayData()
        {
            int itemCount = 0;
            if (container != null)
            {
                for (int i = 0; i < container.Size; i++)
                {
                    var s = container.GetSlot(i);
                    if (s != null && !s.IsEmpty) itemCount++;
                }
            }
            return $"CARGO\n{Fill01 * 100f:0}% full\n{CurrentMassKg / 1000f:0.0} / {maxMassKg / 1000f:0.0} t\n{itemCount} item types";
        }
    }
}
