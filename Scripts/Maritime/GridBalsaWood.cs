// Assets/Scripts/VoxelEngine/Maritime/GridBalsaWood.cs

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridBalsaWood : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 1.0f;
            waterproof = true;
            maxWaterlogging = 0f;
            soakRate = 0f;
            healthMultiplier = 0.4f;
            BlockMass = 25f;
            base.OnPlaced();
            blockName = "Balsa Wood";
        }
    }
}
