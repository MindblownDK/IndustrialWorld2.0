// Assets/Scripts/VoxelEngine/Storage/CraftingPattern.cs
//
// Auto-crafting pattern. Stored in the server's RAM.
// When an item is requested that matches a pattern, the server
// auto-crafts it from materials in storage.

using System;
using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.Storage
{
    [Serializable]
    public class CraftingPattern
    {
        public RecipeDefinition recipe;
        public bool enabled = true;
    }
}
