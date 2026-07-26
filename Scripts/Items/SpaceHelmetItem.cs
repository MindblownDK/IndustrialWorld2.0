// Assets/Scripts/VoxelEngine/Items/SpaceHelmetItem.cs
// Sealed helmet equipment foundation for underwater/vacuum life support.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Space Helmet Item", fileName = "SpaceHelmet_New")]
    public class SpaceHelmetItem : ItemDefinition
    {
        [Header("Life Support")]
        public bool sealedHelmet = true;
        [Tooltip("Additional drain multiplier while sealed. Stacks with the oxygen tank multiplier.")]
        [Range(0.1f, 1f)] public float oxygenEfficiency = 0.85f;

        public override bool IsStackable => false;
    }
}
