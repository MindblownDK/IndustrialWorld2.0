// Assets/Scripts/VoxelEngine/Crafting/CraftingStation.cs
using UnityEngine;

namespace VoxelEngine.Crafting
{
    /// <summary>
    /// Component on a placed workstation prefab. The player's nearby-stations check
    /// looks for these inside an interaction radius.
    /// </summary>
    public class CraftingStation : MonoBehaviour
    {
        public StationTier tier = StationTier.CraftingBench;
        [Tooltip("Display name shown in the crafting UI title.")]
        public string displayName = "Crafting Bench";
        [Tooltip("When enabled, this station lists only recipes that require exactly its tier instead of every recipe up to it.")]
        public bool exclusiveRecipes = false;
    }
}
