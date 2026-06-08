// Assets/Scripts/VoxelEngine/GridSystem/GridWaterTank.cs
//
// Water Tank block for grids. Stores liquid water for H2/O2 generators and other processes.
// Separate from gas tanks. Simple storage with capacity.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridWaterTank : GridBlock
    {
        [Header("Water Tank")]
        public float capacity = 500f;
        public float waterStored;

        public float WaterFillRatio => capacity > 0 ? waterStored / capacity : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            waterStored = capacity * 0.25f; // start partially filled
        }

        public bool TryConsumeWater(float amount)
        {
            if (waterStored >= amount)
            {
                waterStored -= amount;
                return true;
            }
            return false;
        }

        public void AddWater(float amount)
        {
            waterStored = Mathf.Min(capacity, waterStored + amount);
        }
    }
}