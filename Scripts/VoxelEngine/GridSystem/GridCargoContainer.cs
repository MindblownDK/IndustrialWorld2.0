// Assets/Scripts/VoxelEngine/GridSystem/GridCargoContainer.cs
//
// Storage block for ships/vehicles. Holds items like a chest, but capacity is
// limited by MASS (kg) rather than a fixed slot count — a Small container holds
// 100 t, a Large holds 1 000 t. Its current content mass feeds the grid's total
// mass so a loaded ship flies heavier.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridCargoContainer : GridBlock
    {
        [Header("Cargo")]
        [Tooltip("How many visual slots the UI exposes (mass is the real limit).")]
        public int slots = 24;

        [Tooltip("Maximum cargo mass in kilograms. Small = 100 000 kg, Large = 1 000 000 kg.")]
        public float maxMassKg = 100_000f;

        public ItemContainer container;

        /// <summary>Current mass (kg) of stored items.</summary>
        public float CurrentMassKg => MassUtil.ContainerMass(container);

        /// <summary>0..1 fill fraction by mass.</summary>
        public float Fill01 => maxMassKg <= 0f ? 0f : Mathf.Clamp01(CurrentMassKg / maxMassKg);

        // Stored items add their mass to the ship.
        public override float ContentMass => CurrentMassKg;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (container == null) container = new ItemContainer("Cargo", slots);
            else container.Resize(slots);

            // Register with the grid item network so the master terminal & pipes see us.
            if (Grid != null && GridItemNetwork.Instance != null)
                GridItemNetwork.Instance.RegisterContainer(Grid, this);
        }

        /// <summary>True if adding this stack would stay within the mass cap.</summary>
        public bool CanAcceptMass(ItemDefinition item, int count)
            => CurrentMassKg + MassUtil.StackMass(item, count) <= maxMassKg;
    }
}
