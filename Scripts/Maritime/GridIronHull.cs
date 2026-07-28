// Assets/Scripts/VoxelEngine/Maritime/GridIronHull.cs

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridIronHull : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 0.0f;
            waterproof = true;
            maxWaterlogging = 0f;
            soakRate = 0f;
            healthMultiplier = 5f;
            BlockMass = 400f;
            base.OnPlaced();
            blockName = "Iron Hull";
        }
    }
}
