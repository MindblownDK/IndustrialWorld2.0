// Assets/Scripts/VoxelEngine/GridSystem/GridItemPipe.cs
//
// ItemPipe for grid conveyor functionality using ResourceNetwork.

using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.GridSystem
{
    public class GridItemPipe : GridBlock
    {
        [Header("Item Pipe")]
        public float transferRate = 10f;

        private ResourceNetwork<float> _network;

        public override void OnPlaced()
        {
            base.OnPlaced();
            // Future: full ResourceNetwork integration
        }
    }
}