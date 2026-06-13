// Assets/Scripts/VoxelEngine/Biomes/BiomeDefinition.cs
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Biomes
{
    /// <summary>
    /// Designer-authored biome (Plains, Forest, Desert, etc.).
    /// Right-click in Project ▸ Create ▸ Voxel Engine ▸ Biome Definition.
    ///
    /// Biomes are picked by sampling two climate noises:
    ///   • temperature (cold ↔ hot)   range 0..1
    ///   • humidity    (dry  ↔ wet)   range 0..1
    /// The biome whose climate window contains (T, H) wins. Ties are broken by priority.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Biome Definition", fileName = "Biome_New")]
    public class BiomeDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string biomeName = "Plains";
        public Color  debugColor = Color.green;

        [Header("Climate window (0..1)")]
        [Range(0,1)] public float minTemperature = 0.30f;
        [Range(0,1)] public float maxTemperature = 0.65f;
        [Range(0,1)] public float minHumidity    = 0.30f;
        [Range(0,1)] public float maxHumidity    = 0.65f;
        [Tooltip("Higher priority wins overlapping climate windows.")]
        public int priority = 0;

        [Header("Terrain shape")]
        [Tooltip("Voxels added/subtracted from the planet's base height in the middle of the biome.")]
        public float heightOffset = 0f;
        [Tooltip("Amplitude of detail noise for this biome (rolling hills, jagged peaks, …).")]
        public float heightAmplitude = 12f;
        [Tooltip("Frequency of biome detail noise. Lower = wider hills, higher = jagged.")]
        public float heightFrequency = 0.02f;
        [Range(0,1)] public float ridgedness = 0f; // 0=smooth FBM, 1=jagged ridged

        [Header("Surface materials (top-down)")]
        public MaterialId surfaceMaterial   = MaterialId.Clay;   // grass-equivalent
        public int        surfaceDepth      = 1;
        public MaterialId subsurfaceMaterial= MaterialId.Clay;   // dirt
        public int        subsurfaceDepth   = 4;
        [Tooltip("If set, beaches use sand near sea level regardless of biome.")]
        public bool       allowBeach        = true;

        [Header("Underwater")]
        public bool isOceanic = false;        // pull terrain below sea level

        [Header("Scatter (vegetation, rocks)")]
        public ScatterEntry[] scatter;

        [System.Serializable]
        public struct ScatterEntry
        {
            public GameObject prefab;          // tree, rock, bush — must have MeshRenderer for GPU Resident Drawer
            [Range(0,1)] public float density; // probability per surface voxel
            public float minScale;
            public float maxScale;
            public float minHeight;            // y range where it can spawn
            public float maxHeight;
        }
    }
}
