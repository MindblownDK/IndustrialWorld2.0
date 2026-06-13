// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidPipe.cs
//
// Liquid pipe (grid only). Registers with GridLiquidNetwork so liquid tanks and
// processors only share fluids when the ship has an authored pipe network.

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
            if (Grid != null && GridLiquidNetwork.Instance != null)
                GridLiquidNetwork.Instance.RegisterPipe(Grid, this);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Grid != null && GridLiquidNetwork.Instance != null)
                GridLiquidNetwork.Instance.UnregisterPipe(Grid, this);
        }
    }
}
