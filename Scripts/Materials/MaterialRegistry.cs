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

        /// <summary>
        /// Built-in display name for every known MaterialId. Used when no
        /// VoxelMaterialDefinition asset resolves for a voxel (inspection HUD and any
        /// other name lookup), so a material is NEVER anonymous — 9.16.0 field round.
        /// </summary>
        public static string DefaultDisplayName(MaterialId id) => id switch
        {
            MaterialId.Air              => "Air",
            MaterialId.Stone            => "Stone",
            MaterialId.Sand             => "Sand",
            MaterialId.Clay             => "Clay",
            MaterialId.Ice              => "Ice",
            MaterialId.WaterVoxel       => "Frozen Water",
            MaterialId.WaterLiquid      => "Water",
            MaterialId.Iron             => "Iron Ore",
            MaterialId.Copper           => "Copper Ore",
            MaterialId.Coal             => "Coal",
            MaterialId.Nickel           => "Nickel Ore",
            MaterialId.Silicon          => "Silicon Ore",
            MaterialId.Cobalt           => "Cobalt Ore",
            MaterialId.Silver           => "Silver Ore",
            MaterialId.Gold             => "Gold Ore",
            MaterialId.Magnesium        => "Magnesium Ore",
            MaterialId.Platinum         => "Platinum Ore",
            MaterialId.Uranium          => "Uranium Ore",
            MaterialId.CrudeOil         => "Crude Oil",
            MaterialId.Wood             => "Wood",
            MaterialId.LegacySolidFloor => "Stone",
            MaterialId.Lithium          => "Lithium Ore",
            MaterialId.Grass            => "Grass",
            MaterialId.MartianDust      => "Martian Dust",
            MaterialId.VenusAsh         => "Venus Ash",
            MaterialId.AcidBog          => "Acid Bog",
            MaterialId.VolcanicBasalt   => "Volcanic Basalt",
            MaterialId.CrystalGeode     => "Crystal Geode",
            MaterialId.RefinedOilLiquid => "Refined Oil",
            MaterialId.LiquidFuelLiquid => "Liquid Fuel",
            MaterialId.HeavyFuelOilLiquid => "Heavy Fuel Oil",
            MaterialId.MarineGasOilLiquid => "Marine Gas Oil",
            MaterialId.CoolantLiquid    => "Engine Coolant",
            _                           => id.ToString(),
        };

        /// <summary>Hardness fallback mirroring the editor's authored defaults (MaterialDefinitionAuthoring).</summary>
        private static float DefaultHardness(MaterialId id)
        {
            switch (id)
            {
                case MaterialId.Coal:     return 0.8f;
                case MaterialId.Uranium:  return 4.0f;
                case MaterialId.Platinum: return 3.5f;
                case MaterialId.Gold:     return 3.0f;
                case MaterialId.Silver:   return 2.5f;
                case MaterialId.Cobalt:   return 2.5f;
                case MaterialId.Nickel:   return 2.2f;
                case MaterialId.Lithium:  return 1.8f;
                default:                  return 1.5f;
            }
        }

        /// <summary>Public hardness fallback (0 for fluids) mirroring the authored defaults.</summary>
        public static float DefaultHardnessSafe(MaterialId id)
            => IsFluidId(id) || id == MaterialId.Air ? 0f : DefaultHardness(id);

        /// <summary>Public mining-tier fallback (0 for fluids) mirroring the authored defaults.</summary>
        public static int DefaultMiningTierSafe(MaterialId id)
            => IsFluidId(id) || id == MaterialId.Air ? 0 : DefaultMiningTier(id);

        /// <summary>Mining tier fallback mirroring the editor's authored defaults.</summary>
        private static int DefaultMiningTier(MaterialId id)
        {
            switch (id)
            {
                case MaterialId.Clay:
                case MaterialId.Sand:
                case MaterialId.Ice:
                case MaterialId.Coal:
                case MaterialId.MartianDust:
                case MaterialId.VenusAsh:
                case MaterialId.AcidBog:
                    return 0;
                case MaterialId.Uranium:
                case MaterialId.Platinum:
                    return 4;
                case MaterialId.Gold:
                case MaterialId.Silver:
                    return 3;
                case MaterialId.Cobalt:
                case MaterialId.Nickel:
                case MaterialId.Lithium:
                    return 2;
                default:
                    return 1;
            }
        }

        private static bool IsFluidId(MaterialId id)
            => id == MaterialId.WaterLiquid || id == MaterialId.CrudeOil
            || id == MaterialId.RefinedOilLiquid || id == MaterialId.LiquidFuelLiquid
            || id == MaterialId.HeavyFuelOilLiquid || id == MaterialId.MarineGasOilLiquid
            || id == MaterialId.CoolantLiquid;

        /// <summary>
        /// 9.16.0 field round — builds a registry carrying a runtime definition for EVERY
        /// known MaterialId (names, colours, hardness and mining tiers mirror the authored
        /// defaults). Used when the authored MaterialRegistry asset cannot be resolved, so
        /// the world keeps working exactly as authored instead of silently degrading.
        /// </summary>
        public static MaterialRegistry CreateRuntimeFallback()
        {
            var registry = ScriptableObject.CreateInstance<MaterialRegistry>();
            registry.definitions = new System.Collections.Generic.List<VoxelMaterialDefinition>();

            foreach (MaterialId id in (MaterialId[])System.Enum.GetValues(typeof(MaterialId)))
            {
                if (id == MaterialId.Air) continue;               // air is never described
                var def = ScriptableObject.CreateInstance<VoxelMaterialDefinition>();
                def.id          = id;
                def.displayName = DefaultDisplayName(id);
                def.color       = DefaultColor(id);
                def.hardness    = IsFluidId(id) ? 0f : DefaultHardness(id);
                def.miningTier  = IsFluidId(id) ? 0 : DefaultMiningTier(id);
                def.dropAmount  = 1;
                def.isFluid     = IsFluidId(id);
                def.isMineable  = !IsFluidId(id) && id != MaterialId.Ice && id != MaterialId.CrudeOil;
                registry.definitions.Add(def);
            }
            registry.Build();
            return registry;
        }
    }
}
