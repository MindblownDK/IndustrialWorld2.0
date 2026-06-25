// Assets/Scripts/VoxelEngine/Fluids/OilGrid.cs
// Kept for OilReservoirDecorator and Chunk.cs compatibility.
using VoxelEngine.Core;

namespace VoxelEngine.Fluids
{
    public class OilGrid
    {
        private readonly bool[] _data = new bool[VoxelConstants.CHUNK_SIZE_P * VoxelConstants.CHUNK_SIZE_P * VoxelConstants.CHUNK_SIZE_P];
        public bool hasAnyOil;

        private static int Idx(int x, int y, int z)
        {
            const int S = VoxelConstants.CHUNK_SIZE_P;
            return (x + 1) + (y + 1) * S + (z + 1) * S * S;
        }

        public bool Get(int x, int y, int z) => _data[Idx(x, y, z)];
        public void Set(int x, int y, int z, bool v) { _data[Idx(x, y, z)] = v; if (v) hasAnyOil = true; }
    }
}
