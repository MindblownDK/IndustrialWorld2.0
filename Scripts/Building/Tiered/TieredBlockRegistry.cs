// Assets/Scripts/VoxelEngine/Building/Tiered/TieredBlockRegistry.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    [CreateAssetMenu(menuName = "Voxel Engine/Building/Tiered Block Registry", fileName = "TieredBlockRegistry")]
    public class TieredBlockRegistry : ScriptableObject
    {
        public List<TieredBlockDefinition> definitions = new();

        public TieredBlockDefinition Get(BuildFamily f)
        {
            foreach (var d in definitions)
                if (d != null && d.family == f) return d;
            return null;
        }
    }
}
