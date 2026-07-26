// Assets/Scripts/VoxelEngine/Items/OxygenTankItem.cs
// Equipment item for player life support. Current pass gives the player an
// extended oxygen reserve when paired with a sealed SpaceHelmetItem.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Oxygen Tank Item", fileName = "OxygenTank_New")]
    public class OxygenTankItem : ItemDefinition
    {
        [Header("Life Support")]
        [Tooltip("Extra oxygen reserve added to the player's oxygen bar when equipped with a sealed helmet.")]
        public float bonusOxygen = 180f;
        [Tooltip("Multiplier applied to underwater/vacuum oxygen drain. Lower is better.")]
        [Range(0.1f, 1f)] public float drainMultiplier = 0.55f;

        public override bool IsStackable => false;
    }
}
