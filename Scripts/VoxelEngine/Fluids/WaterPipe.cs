// Assets/Scripts/VoxelEngine/Fluids/WaterPipe.cs
// Kept for fluid network compatibility.
namespace VoxelEngine.Fluids
{
    public class WaterPipe : FluidNode
    {
        public override FluidNodeKind Kind => FluidNodeKind.Pipe;
        public float maxFlowLps = 50f;
        public bool isGlass = false;
    }
}
