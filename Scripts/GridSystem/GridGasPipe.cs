// Assets/Scripts/VoxelEngine/GridSystem/GridGasPipe.cs
//
// Gas pipe (grid only). Pipes themselves are passive connectors — on a grid the
// gas pool is shared entity-wide, so any Gas Tank feeding the pool is
// automatically available to every Hydrogen Thruster, Space-Engineers style.
// The pipe exists so players lay out a visible distribution network and so a
// grid with NO gas pipes can be made to require them (see GridGasNetwork).

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGasPipe : GridBlock
    {
        [Tooltip("Litres of gas this pipe segment can pass per second (cosmetic / future throttling).")]
        public float throughput = 50f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Gas Pipe";
            if (Grid != null && GridGasNetwork.Instance != null)
                GridGasNetwork.Instance.RegisterPipe(Grid, this);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Grid != null && GridGasNetwork.Instance != null)
                GridGasNetwork.Instance.UnregisterPipe(Grid, this);
        }
    }
}
