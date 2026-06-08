// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidFuelTank.cs
//
// Liquid Fuel Tank for the complex fuel mix (Kerosene + Liquid Hydrogen + Liquid Methane).
// Used by LiquidFuel thrusters. Part of the full production chain.

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
            fuelStored = capacity * 0.1f; // start low
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