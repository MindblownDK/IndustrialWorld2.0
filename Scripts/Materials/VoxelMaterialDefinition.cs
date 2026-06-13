// Assets/Scripts/VoxelEngine/Materials/VoxelMaterialDefinition.cs
using UnityEngine;

namespace VoxelEngine.Materials
{
    /// <summary>
    /// ScriptableObject describing every property of a single voxel material.
    /// Right-click in Project ▸ Create ▸ Voxel Engine ▸ Material Definition.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Material Definition", fileName = "Mat_New")]
    public class VoxelMaterialDefinition : ScriptableObject
    {
        [Header("Identity")]
        public MaterialId id = MaterialId.Stone;
        public string displayName = "Stone";

        [Header("Visuals")]
        [Tooltip("Albedo / surface tint baked into vertex colours.")]
        public Color color = Color.gray;
        [Tooltip("Optional surface texture (sampled by triplanar shader).")]
        public Texture2D albedo;
        [Range(0, 1)] public float metallic  = 0f;
        [Range(0, 1)] public float smoothness = 0.2f;
        public Color emission = Color.black;

        [Header("Gameplay")]
        [Tooltip("How hard to mine. Lower = faster. Stone = 1.0, Uranium ≈ 4.0.")]
        public float hardness = 1f;
        [Tooltip("Item dropped when mined. Leave null to drop nothing.")]
        public Items.ItemDefinition dropItem;
        [Tooltip("Yield (item count) per fully removed voxel.")]
        public int dropAmount = 1;
        [Tooltip("Required pickaxe tier to mine this. 0=hands, 1=wood, 2=stone, 3=iron, 4=steel.")]
        [Range(0,4)] public int miningTier = 0;

        [Header("Physics")]
        public bool isFluid = false;
        public bool isMineable = true;
    }
}
