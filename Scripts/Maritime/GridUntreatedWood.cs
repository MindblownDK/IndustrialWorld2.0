// Assets/Scripts/VoxelEngine/Maritime/GridUntreatedWood.cs

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridUntreatedWood : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 0.85f;
            waterproof = false;
            maxWaterlogging = 40f;
            soakRate = 1.5f;
            healthMultiplier = 1f;
            BlockMass = 80f;
            base.OnPlaced();
            blockName = "Untreated Wood Hull";
        }
    }
}
