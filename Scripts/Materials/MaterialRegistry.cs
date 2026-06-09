// Assets/Scripts/VoxelEngine/Materials/MaterialRegistry.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Materials
{
    /// <summary>
    /// Central registry that maps MaterialId -> VoxelMaterialDefinition.
    /// Drop every VoxelMaterialDefinition asset into the 'definitions' list in the inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Material Registry", fileName = "MaterialRegistry")]
    public class MaterialRegistry : ScriptableObject
    {
        public List<VoxelMaterialDefinition> definitions = new List<VoxelMaterialDefinition>();

        // Fast lookup table built at runtime.
        private VoxelMaterialDefinition[] _lookup;

        public void Build()
        {
            _lookup = new VoxelMaterialDefinition[256];
            foreach (var def in definitions)
            {
                if (def == null) continue;
                _lookup[(byte)def.id] = def;
            }
        }

        public VoxelMaterialDefinition Get(MaterialId id) => Get((byte)id);

        public VoxelMaterialDefinition Get(byte id)
        {
            if (_lookup == null) Build();
            return _lookup[id];
        }

        public Color GetColor(byte id)
        {
            var def = Get(id);
            return def != null ? def.color : Color.magenta;
        }

        public bool IsMineable(byte id)
        {
            var def = Get(id);
            return def != null && def.isMineable;
        }
    }
}
