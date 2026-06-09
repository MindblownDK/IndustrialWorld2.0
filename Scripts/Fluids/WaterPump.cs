// Assets/Scripts/VoxelEngine/Fluids/WaterPump.cs
// Kept for fluid network compatibility.
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Fluids
{
    [RequireComponent(typeof(PowerConsumer))]
    public class WaterPump : FluidNode
    {
        public override FluidNodeKind Kind => FluidNodeKind.Pump;
        public float pumpLps = 20f;
        public float reach = 3f;
    }
}
