// Assets/Scripts/VoxelEngine/Items/ItemPersistenceCatalog.cs
//
// Runtime-safe item asset catalog. WorldStatePersistence uses it before loading
// a save so non-Resources equipment assets (portable batteries, hydrogen tanks,
// jetpacks, and other inventory items) always resolve by itemId instead of being
// silently dropped from a saved inventory.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Persistence Catalog", fileName = "ItemPersistenceCatalog")]
    public sealed class ItemPersistenceCatalog : ScriptableObject
    {
        [Tooltip("Item assets included in player/world persistence lookup at startup.")]
        public List<ItemDefinition> items = new();
    }
}
