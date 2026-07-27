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
            if (_lookup == null || _lookup.Length < 256) Build();
            int idx = id;
            if (_lookup == null || idx < 0 || idx >= _lookup.Length) return null;
            return _lookup[idx];
        }

        public Color GetColor(byte id)
        {
            var def = Get(id);
            if (def != null) return def.color;
            // Built-in fallback so newly added MaterialIds render sensibly before a
            // designer authors a VoxelMaterialDefinition asset for them.
            return DefaultColor((MaterialId)id);
        }

        /// <summary>
        /// Sensible built-in colour for every known MaterialId. Used only when no
        /// VoxelMaterialDefinition asset exists for that id (keeps the "missing = magenta"
        /// debug signal for genuinely unknown ids like 255).
        /// </summary>
        public static Color DefaultColor(MaterialId id)
        {
            switch (id)
            {
                case MaterialId.Air:         return new Color(0f, 0f, 0f, 0f);
                case MaterialId.Stone:       return new Color(0.46f, 0.46f, 0.49f, 1f);
                case MaterialId.Sand:        return new Color(0.78f, 0.72f, 0.52f, 1f);
                case MaterialId.Clay:        return new Color(0.50f, 0.40f, 0.30f, 1f);
                case MaterialId.Ice:         return new Color(0.80f, 0.90f, 1.00f, 1f);
                case MaterialId.WaterVoxel:
                case MaterialId.WaterLiquid: return new Color(0.10f, 0.35f, 0.65f, 1f);  // solid blue (renders in terrain mesh)
                case MaterialId.Iron:        return new Color(0.56f, 0.46f, 0.41f, 1f);
                case MaterialId.Copper:      return new Color(0.72f, 0.46f, 0.31f, 1f);
                case MaterialId.Coal:        return new Color(0.14f, 0.14f, 0.16f, 1f);
                case MaterialId.Nickel:      return new Color(0.52f, 0.56f, 0.52f, 1f);
                case MaterialId.Silicon:     return new Color(0.62f, 0.57f, 0.52f, 1f);
                case MaterialId.Cobalt:      return new Color(0.27f, 0.37f, 0.62f, 1f);
                case MaterialId.Silver:      return new Color(0.85f, 0.85f, 0.90f, 1f);
                case MaterialId.Gold:        return new Color(0.86f, 0.70f, 0.26f, 1f);
                case MaterialId.Magnesium:   return new Color(0.62f, 0.62f, 0.57f, 1f);
                case MaterialId.Platinum:    return new Color(0.80f, 0.82f, 0.85f, 1f);
                case MaterialId.Uranium:     return new Color(0.46f, 0.70f, 0.30f, 1f);
                case MaterialId.Lithium:     return new Color(0.76f, 0.79f, 0.86f, 1f);
                case MaterialId.Grass:       return new Color(0.38f, 0.55f, 0.22f, 1f);   // natural green
                case MaterialId.CrudeOil:    return new Color(0.05f, 0.04f, 0.03f, 1f);
                case MaterialId.Wood:        return new Color(0.40f, 0.28f, 0.18f, 1f);
                case MaterialId.LegacySolidFloor:return new Color(0.46f, 0.46f, 0.49f, 1f);
                case MaterialId.MartianDust:    return new Color(0.62f, 0.34f, 0.20f, 1f); // rust-red dust
                case MaterialId.VenusAsh:       return new Color(0.72f, 0.62f, 0.26f, 1f); // sulfur-tan
                case MaterialId.AcidBog:        return new Color(0.40f, 0.52f, 0.24f, 1f); // sickly green
                case MaterialId.VolcanicBasalt: return new Color(0.14f, 0.12f, 0.13f, 1f); // black basalt
                case MaterialId.CrystalGeode:   return new Color(0.56f, 0.48f, 0.84f, 1f); // prismatic purple
                default:                     return Color.magenta; // genuine unknown — debug signal
            }
        }

        public bool IsMineable(byte id)
        {
            var def = Get(id);
            return def != null && def.isMineable;
        }
    }
}
