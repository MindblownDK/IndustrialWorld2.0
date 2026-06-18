// Assets/Scripts/VoxelEngine/Maritime/GridShippingContainer.cs
//
// Maritime shipping container. A high-capacity cargo block styled after real
// intermodal containers and unlocked through maritime research.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    public class GridShippingContainer : GridCargoContainer
    {
        [Header("Shipping Container")]
        [Tooltip("Large cargo container equivalent capacity multiplier.")]
        public float largeContainerMultiplier = 5f;

        public override void OnPlaced()
        {
            blockName = "Shipping Container";
            slots = Mathf.Max(slots, 60);
            maxMassKg = Mathf.Max(maxMassKg, 1_000_000f * largeContainerMultiplier);
            BlockMass = Mathf.Max(BlockMass, 1800f);
            maxHP = Mathf.Max(maxHP, 1200f);
            base.OnPlaced();
            if (container == null) container = new ItemContainer("Shipping Container", slots);
            else container.Resize(slots);
            ApplyFilter();
        }
    }
}
