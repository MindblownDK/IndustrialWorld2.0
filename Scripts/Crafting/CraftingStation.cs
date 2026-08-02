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
        [Tooltip("When true this station only lists recipes that require EXACTLY its tier " +
                 "(e.g. the Armor Station only lists armour recipes), instead of every recipe " +
                 "up to its tier.")]
        public bool exclusiveRecipes = false;
    }
}
