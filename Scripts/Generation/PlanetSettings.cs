// Assets/Scripts/VoxelEngine/Generation/PlanetSettings.cs
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Materials;

namespace VoxelEngine.Generation
{
    /// <summary>
    /// Global planet settings. All biome-specific shape (mountains, hills, oceans, ridges, etc.)
    /// is now expressed via BiomeDefinition assets in 'biomeRegistry'.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Planet Settings", fileName = "Planet_Earthlike")]
    public class PlanetSettings : ScriptableObject
    {
        [Header("World Seed")]
        public int seed = 1337;

        [Header("Reference Heights")]
        [Tooltip("Sea level in voxels (water fills below this).")]
        public int seaLevel = 96;
        [Tooltip("Average ground height that biome 'heightOffset' is added to.")]
        public int baseHeight = 100;

        [Header("Continent Shaping")]
        [Tooltip("Lower = larger continents, higher = many small islands.")]
        public float continentScale = 0.0015f;

        [Header("Crust depths")]
        [Tooltip("How deep the rocky crust extends before pure stone takes over (ores spawn here).")]
        public int crustDepth = 40;

        [Header("Biomes")]
        public BiomeRegistry biomeRegistry;

        [Header("Sub-surface ores (common)")]
        public OreLayer iron     = new OreLayer { material = MaterialId.Iron,     scale = 0.06f, threshold = 0.45f, minDepth = 4,  maxDepth = 80 };
        public OreLayer copper   = new OreLayer { material = MaterialId.Copper,   scale = 0.07f, threshold = 0.55f, minDepth = 6,  maxDepth = 70 };
        public OreLayer coal     = new OreLayer { material = MaterialId.Coal,     scale = 0.05f, threshold = 0.50f, minDepth = 4,  maxDepth = 60 };
        public OreLayer nickel   = new OreLayer { material = MaterialId.Nickel,   scale = 0.08f, threshold = 0.60f, minDepth = 20, maxDepth = 120 };
        public OreLayer silicon  = new OreLayer { material = MaterialId.Silicon,  scale = 0.06f, threshold = 0.55f, minDepth = 4,  maxDepth = 90 };
        public OreLayer cobalt   = new OreLayer { material = MaterialId.Cobalt,   scale = 0.09f, threshold = 0.65f, minDepth = 30, maxDepth = 140 };
        public OreLayer magnesium= new OreLayer { material = MaterialId.Magnesium,scale = 0.08f, threshold = 0.62f, minDepth = 15, maxDepth = 110 };

        [Header("Deep-core ores (rare)")]
        public OreLayer silver   = new OreLayer { material = MaterialId.Silver,   scale = 0.10f, threshold = 0.72f, minDepth = 60, maxDepth = 200 };
        public OreLayer gold     = new OreLayer { material = MaterialId.Gold,     scale = 0.11f, threshold = 0.78f, minDepth = 80, maxDepth = 220 };
        public OreLayer platinum = new OreLayer { material = MaterialId.Platinum, scale = 0.12f, threshold = 0.80f, minDepth = 100,maxDepth = 240 };
        public OreLayer uranium  = new OreLayer { material = MaterialId.Uranium,  scale = 0.13f, threshold = 0.82f, minDepth = 120,maxDepth = 250 };

        [Header("Specials")]
        public OreLayer crudeOil = new OreLayer { material = MaterialId.CrudeOil, scale = 0.04f, threshold = 0.70f, minDepth = 25, maxDepth = 90 };
        public OreLayer ice      = new OreLayer { material = MaterialId.Ice,      scale = 0.05f, threshold = 0.65f, minDepth = 0,  maxDepth = 12 };
    }

    [System.Serializable]
    public struct OreLayer
    {
        public MaterialId material;
        public float scale;
        public float threshold;
        public int   minDepth;
        public int   maxDepth;
    }
}
