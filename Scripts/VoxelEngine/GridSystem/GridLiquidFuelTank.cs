// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidFuelTank.cs
//
// Liquid Fuel Tank for the complex Kerosene + LiqH2 + LiqCH4 mix.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridLiquidFuelTank : GridBlock
    {
        [Header("Liquid Fuel Tank")]
        public float capacity = 300f;
        public float fuelStored;

        public float FillRatio => capacity > 0 ? fuelStored / capacity : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            fuelStored = capacity * 0.1f;
        }

        public bool TryConsume(float amount)
        {
            if (fuelStored >= amount)
            {
                fuelStored -= amount;
                return true;
            }
            return false;
        }

        public void AddFuel(float amount)
        {
            fuelStored = Mathf.Min(capacity, fuelStored + amount);
        }
    }
}