// Assets/Scripts/VoxelEngine/GridSystem/GridSize.cs
namespace VoxelEngine.GridSystem
{
    /// <summary>Grid block size. Small = 0.5m (detailed), Large = 2.5m (structural).</summary>
    public enum GridSize { Small, Large }

    public static class GridSizeExt
    {
        public static float CellSize(this GridSize s) => s == GridSize.Small ? 0.5f : 2.5f;
    }
}
