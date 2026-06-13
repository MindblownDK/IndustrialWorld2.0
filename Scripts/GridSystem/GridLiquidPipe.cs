// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidPipe.cs
//
// Liquid pipe (grid only). Like the gas pipe, it's a passive connector — the
// grid's liquid tanks are already pooled entity-wide by GridLiquidNetwork, so a
// machine (Ship Refinery / Chemical Plant) draws from / fills any connected
// Liquid Tank automatically. The pipe lets players lay out a visible network.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridLiquidPipe : GridBlock
    {
        [Tooltip("Litres per second this pipe can pass (cosmetic / future throttling).")]
        public float throughput = 50f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Liquid Pipe";
        }
    }
}
