// Assets/Scripts/VoxelEngine/Biomes/BiomeRegistry.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Biomes
{
    /// <summary>
    /// Holds every BiomeDefinition the planet can use.
    /// Right-click ▸ Create ▸ Voxel Engine ▸ Biome Registry.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Biome Registry", fileName = "BiomeRegistry")]
    public class BiomeRegistry : ScriptableObject
    {
        public List<BiomeDefinition> biomes = new();
    }
}
