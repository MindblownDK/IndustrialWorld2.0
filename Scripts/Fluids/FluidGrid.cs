// Assets/Scripts/VoxelEngine/Fluids/FluidGrid.cs
// Legacy stub — kept for Chunk.cs field reference compatibility.
using Unity.Collections;
using VoxelEngine.Core;

namespace VoxelEngine.Fluids
{
    public class FluidGrid
    {
        public const byte MAX_LEVEL = 8;
        public NativeArray<byte> levels;
        public bool isDirty;
        public bool hasAnyWater;

        public FluidGrid()
        {
            levels = new NativeArray<byte>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (levels.IsCreated) levels.Dispose();
        }

        public byte Get(int x, int y, int z) => 0;
        public void Set(int x, int y, int z, byte v) { }
    }
}
