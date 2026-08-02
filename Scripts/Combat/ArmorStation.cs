// Assets/Scripts/VoxelEngine/Combat/ArmorStation.cs
//
// Dedicated crafting station for Crusader armor and upgrade modules. It is kept
// exclusive so its premium focused panel never turns into a copy of the general
// crafting catalogue.

using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.Combat
{
    [DisallowMultipleComponent]
    public sealed class ArmorStation : CraftingStation
    {
        private void Awake()
        {
            EnsureIdentity();
        }

        private void OnValidate()
        {
            EnsureIdentity();
        }

        private void EnsureIdentity()
        {
            tier = StationTier.ArmorStation;
            exclusiveRecipes = true;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = "Armor Station";
        }
    }
}
