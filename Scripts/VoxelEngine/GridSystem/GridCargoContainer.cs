// Assets/Scripts/VoxelEngine/GridSystem/GridCargoContainer.cs
//
// Storage block for ships/vehicles. Holds items like a chest.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridCargoContainer : GridBlock
    {
        [Header("Cargo")]
        public int slots = 20;
        public ItemContainer container;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (container == null) container = new ItemContainer("Cargo", slots);
            else container.Resize(slots);
        }
    }
}
