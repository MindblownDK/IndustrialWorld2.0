// Assets/Scripts/VoxelEngine/Combat/ArmorStation.cs
//
// The Armor Station — a CraftingStation whose tier is higher than the Assembler,
// so standing at it grants every bench/furnace/assembler recipe (armour, jetpacks,
// upgrades). Its unique role: applying upgrade modules to the worn armour piece
// (RMB with a module equipped in the hotbar while looking at the station).
//
// Because this derives from CraftingStation it automatically opens the shared
// crafting UI on interaction, and is found by Crafter.MaxAccessibleStation.

using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.Combat
{
    [DisallowMultipleComponent]
    public class ArmorStation : CraftingStation
    {
        private void Awake()
        {
            // Keep the tier authoritative so an orphaned prefab still ranks above
            // the Assembler even if the wizard didn't stamp it.
            tier = StationTier.ArmorStation;
            if (string.IsNullOrEmpty(displayName)) displayName = "Armor Station";
        }
    }
}
