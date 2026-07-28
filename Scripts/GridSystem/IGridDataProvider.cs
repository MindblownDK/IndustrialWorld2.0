// Assets/Scripts/VoxelEngine/GridSystem/IGridDataProvider.cs
//
// Interface for any grid block that can expose live data to attached screens.
// v5.51.0-dev — Adds optional live camera feed provider support for screen render textures.

using UnityEngine;

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

    /// <summary>
    /// Optional extension for data providers that can expose a live camera render texture.
    /// Screens use this only in Camera display mode, preserving the lightweight text-data path
    /// for every other provider type.
    /// </summary>
    public interface IGridCameraFeedProvider : IGridDataProvider
    {
        RenderTexture FeedTexture { get; }
        bool IsOnline { get; }
        bool IsFeedInUse { get; }
        void RegisterFeedConsumer(GridScreenBlock screen);
    }
}
