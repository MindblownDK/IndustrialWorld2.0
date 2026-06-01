// Assets/Scripts/VoxelEngine/Farming/CropDefinition.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// ScriptableObject defining a crop type: wheat, corn, carrots, etc.
    /// Right-click > Create > Voxel Engine > Farming > Crop Definition.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Farming/Crop Definition", fileName = "Crop_New")]
    public class CropDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string cropName = "Wheat";
        public Sprite icon;

        [Header("Growth")]
        [Tooltip("Total seconds from seed to harvestable (without bonuses).")]
        public float growthTime = 120f;
        [Tooltip("Number of visual growth stages (changes scale/color).")]
        public int growthStages = 4;

        [Header("Water")]
        [Tooltip("If true, crop grows faster when irrigated. If false, it dies without water.")]
        public bool requiresWater = true;
        [Tooltip("Growth speed multiplier when irrigated (2 = twice as fast).")]
        public float irrigatedSpeedMultiplier = 2.0f;
        [Tooltip("Seconds without water before the crop starts wilting (0 = immediate).")]
        public float droughtToleranceSec = 30f;

        [Header("Harvest")]
        [Tooltip("Item dropped when harvested.")]
        public ItemDefinition harvestItem;
        [Tooltip("How many items per harvest.")]
        public int harvestAmount = 3;
        [Tooltip("Seeds dropped on harvest (for replanting). Can be the same CropSeed item.")]
        public ItemDefinition seedItem;
        [Tooltip("Seeds returned on harvest.")]
        public int seedReturnAmount = 1;
        [Tooltip("Hunger restored when eating the raw harvest item.")]
        public float foodValue = 15f;

        [Header("Visuals")]
        [Tooltip("Color tint at each growth stage (seedling→mature). Array length should match growthStages.")]
        public Color[] stageColors = { new Color(0.4f, 0.6f, 0.2f), new Color(0.5f, 0.7f, 0.3f),
                                       new Color(0.6f, 0.8f, 0.3f), new Color(0.8f, 0.9f, 0.2f) };
        [Tooltip("Y-scale at each stage. Index 0 = seedling (small), last = full grown.")]
        public float[] stageScales = { 0.2f, 0.4f, 0.7f, 1.0f };

        [Header("Wild Spawn")]
        [Tooltip("Prefab for wild crop scatter (placed by ChunkScatter on biome surfaces).")]
        public GameObject wildPrefab;
    }
}
