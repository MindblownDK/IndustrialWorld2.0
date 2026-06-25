// Assets/Scripts/VoxelEngine/Maritime/GridTarCoatedPlank.cs

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridTarCoatedPlank : GridHullBlock
    {
        public override void OnPlaced()
        {
            buoyancyFactor = 0.9f;
            waterproof = true;
            maxWaterlogging = 0f;
            soakRate = 0f;
            healthMultiplier = 1.3f;
            BlockMass = 60f;
            base.OnPlaced();
            blockName = "Tar-Coated Plank";
        }
    }
}
