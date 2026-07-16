// Assets/Scripts/VoxelEngine/GridSystem/IGridDataProvider.cs
//
// Interface for any grid block that can expose live data to attached screens.
// v5.43.0-dev — Grid Screens & Displays.

namespace VoxelEngine.GridSystem
{
    /// <summary>
    /// Implement on any GridBlock subclass that wants to expose live data
    /// to nearby GridScreenBlocks. The screen queries this via the grid entity.
    /// </summary>
    public interface IGridDataProvider
    {
        /// <summary>Display name of this data source (e.g. "Main Battery").</summary>
        string SourceName { get; }

        /// <summary>Category tag for filtering in the screen config UI.</summary>
        string DataCategory { get; }

        /// <summary>Live data lines to display on the screen.</summary>
        string GetDisplayData();
    }
}
