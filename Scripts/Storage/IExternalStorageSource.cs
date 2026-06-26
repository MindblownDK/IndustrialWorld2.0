// Assets/Scripts/VoxelEngine/Storage/IExternalStorageSource.cs
using System.Collections.Generic;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    /// <summary>Optional physical storage provider surfaced through a ServerRack.</summary>
    public interface IExternalStorageSource
    {
        bool IsAvailable { get; }
        int Priority { get; }
        int Insert(ItemDefinition item, int count);
        int Extract(string itemId, int count);
        int CountOf(string itemId);
        void AppendAllItems(Dictionary<string, StoredItemEntry> merged);
    }
}
