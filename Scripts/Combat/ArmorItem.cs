// Assets/Scripts/VoxelEngine/Combat/ArmorItem.cs
//
// A full Crusader armor set. 6 tiers from Initiate's Gambeson (basic quilted cloth)
// to the Stellar Archon Plate (sealed void-metal, the ultimate). Higher tiers reduce
// a greater fraction of incoming damage. Equipped via RMB in the hotbar.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    public class ArmorItem : ItemDefinition
    {
        [Header("Armor")]
        [Range(1, 6)] public int tier = 1;
        [Tooltip("Fraction of incoming damage blocked (0 = none, 0.62 = best tier).")]
        [Range(0f, 0.9f)] public float damageReduction = 0.1f;

        public ArmorItem() { maxStack = 1; massPerUnit = 3f; }

        public override bool IsStackable => false;
    }
}
