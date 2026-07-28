// Assets/Scripts/VoxelEngine/Editor/MaterialDefinitionAuthoring.cs
//
// Ensures every MaterialId that is used as an ore/mineral has a VoxelMaterialDefinition
// asset registered in MaterialRegistry. Run after adding a new MaterialId (e.g. Lithium)
// so the new material renders, mines and drops correctly without hand-authoring YAML.
using UnityEditor;
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.EditorTools
{
    public static class MaterialDefinitionAuthoring
    {
        private const string RegistryPath = "Assets/VoxelEngineAssets/MaterialRegistry.asset";
        private const string MaterialsDir = "Assets/VoxelEngineAssets/Materials";

        [MenuItem("Tools/Voxel Engine/Ensure Material Definitions (incl. Lithium)")]
        public static void EnsureMaterialDefinitions()
        {
            EnsureFolder(MaterialsDir);

            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(RegistryPath);
            if (registry == null)
            {
                Debug.LogError("[Materials] MaterialRegistry.asset not found at " + RegistryPath);
                return;
            }

            registry.Build();
            int created = 0;

            // Every solid, mineable id we care about. Air, legacy floor values, and fluids are skipped.
            var ids = new[]
            {
                MaterialId.Iron, MaterialId.Copper, MaterialId.Coal, MaterialId.Nickel,
                MaterialId.Silicon, MaterialId.Cobalt, MaterialId.Silver, MaterialId.Gold,
                MaterialId.Magnesium, MaterialId.Platinum, MaterialId.Uranium, MaterialId.Lithium,
                MaterialId.Ice, MaterialId.CrudeOil, MaterialId.Stone, MaterialId.Sand, MaterialId.Clay,
            };

            foreach (var id in ids)
            {
                if (registry.Get(id) != null) continue;

                string name = id.ToString();
                string path = MaterialsDir + "/Mat_" + name + ".asset";
                path = AssetDatabase.GenerateUniqueAssetPath(path);

                var def = ScriptableObject.CreateInstance<VoxelMaterialDefinition>();
                def.id           = id;
                def.displayName  = name;
                def.color        = MaterialRegistry.DefaultColor(id);
                def.hardness     = DefaultHardness(id);
                def.miningTier   = DefaultMiningTier(id);
                def.dropAmount   = 1;
                def.isMineable   = id != MaterialId.Ice && id != MaterialId.CrudeOil;
                def.isFluid      = id == MaterialId.CrudeOil;

                AssetDatabase.CreateAsset(def, path);
                registry.definitions.Add(def);
                created++;
            }

            if (created > 0)
            {
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Materials] Created " + created + " missing VoxelMaterialDefinition(s) and registered them.");
            }
            else
            {
                Debug.Log("[Materials] All material definitions already present. Nothing to do.");
            }

            Selection.activeObject = registry;
        }

        /// <summary>
        /// Fixes EXISTING material definitions so soft surface blocks (Clay, Sand, Ice, Coal) are
        /// hand-mineable (tier 0). The create-tool above is idempotent and skips existing defs, so
        /// if your Clay/Sand were created at tier 1, bare hands could not mine them. This re-applies
        /// the soft-block tier to any existing definitions of those materials.
        /// </summary>
        [MenuItem("Tools/Voxel Engine/Normalize Soft Material Tiers (Hand-Mineable)")]
        public static void NormalizeSoftTiers()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(RegistryPath);
            if (registry == null)
            {
                Debug.LogError("[Materials] MaterialRegistry.asset not found at " + RegistryPath);
                return;
            }
            registry.Build();
            int fixedCount = 0;
            var soft = new[]
            {
                MaterialId.Clay, MaterialId.Sand, MaterialId.Ice, MaterialId.Coal,
            };
            foreach (var id in soft)
            {
                var def = registry.Get(id);
                if (def == null) continue;
                if (def.miningTier != 0)
                {
                    def.miningTier = 0;
                    EditorUtility.SetDirty(def);
                    fixedCount++;
                    Debug.Log("[Materials] " + id + ": miningTier -> 0 (hand-mineable).");
                }
            }
            if (fixedCount > 0)
            {
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                Debug.Log("[Materials] Normalized " + fixedCount + " soft material(s) to tier 0. " +
                          "You can now dig Clay/Sand/Ice/Coal with bare hands.");
            }
            else
            {
                Debug.Log("[Materials] All soft materials are already tier 0. Nothing to fix.");
            }
            Selection.activeObject = registry;
        }

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

        private static int DefaultMiningTier(MaterialId id)
        {
            switch (id)
            {
                // Soft, hand-mineable surface materials (dirt/sand/clay equivalents). Bare hands
                // (tier 0) can dig these — standard for every voxel game.
                case MaterialId.Clay:     return 0;
                case MaterialId.Sand:     return 0;
                case MaterialId.Ice:      return 0;
                case MaterialId.Coal:     return 0;
                // Celestial surface materials (Phase 2)
                case MaterialId.MartianDust: return 0;
                case MaterialId.VenusAsh:    return 0;
                case MaterialId.AcidBog:     return 0;
                // Ores — require progressively better tools.
                case MaterialId.Uranium:  return 4;
                case MaterialId.Platinum: return 4;
                case MaterialId.Gold:     return 3;
                case MaterialId.Silver:   return 3;
                case MaterialId.Cobalt:   return 2;
                case MaterialId.Nickel:   return 2;
                case MaterialId.Lithium:  return 2;
                default:                  return 1;   // Stone, etc. — needs at least a wood pickaxe
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
