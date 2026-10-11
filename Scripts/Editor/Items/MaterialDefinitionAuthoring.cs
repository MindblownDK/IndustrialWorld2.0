// Assets/Scripts/Editor/Items/MaterialDefinitionAuthoring.cs
//
// Ensures mineable terrain MaterialIds have registered VoxelMaterialDefinitions, canonical
// drop links, and Resources persistence-catalog entries. Missing links are repaired without
// replacing populated item references or authored yield values.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.EditorTools
{
    public static class MaterialDefinitionAuthoring
    {
        private const string RegistryPath = "Assets/VoxelEngineAssets/MaterialRegistry.asset";
        private const string MaterialsDir = "Assets/VoxelEngineAssets/Materials";
        private const string ItemCatalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        public static void EnsureMaterialDefinitions()
        {
            EnsureFolder(MaterialsDir);

            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(RegistryPath);
            if (registry == null)
            {
                Debug.LogError("[Materials] MaterialRegistry.asset not found at " + RegistryPath);
                return;
            }
            if (registry.definitions == null)
                registry.definitions = new List<VoxelMaterialDefinition>();

            registry.Build();
            int created = 0;
            int existingDefinitionsRegistered = 0;
            int dropLinksRepaired = 0;
            int warnings = 0;
            bool registryChanged = false;
            var canonicalDropsForCatalog = new HashSet<ItemDefinition>();

            // Air and water/liquid voxel IDs are excluded. Crude Oil keeps its authored
            // item link; the legacy floor value is repaired as Stone for old worlds.
            // Existing material balance and populated drop links are preserved.
            var ids = new[]
            {
                MaterialId.Iron, MaterialId.Copper, MaterialId.Coal, MaterialId.Nickel,
                MaterialId.Silicon, MaterialId.Cobalt, MaterialId.Silver, MaterialId.Gold,
                MaterialId.Magnesium, MaterialId.Platinum, MaterialId.Uranium, MaterialId.Lithium,
                MaterialId.Ice, MaterialId.CrudeOil, MaterialId.Stone, MaterialId.Sand, MaterialId.Clay, MaterialId.Grass,
                MaterialId.Wood, MaterialId.LegacySolidFloor,
            };

            foreach (var id in ids)
            {
                var def = registry.Get(id);
                if (def == null)
                {
                    string assetPath = MaterialsDir + "/Mat_" + id + ".asset";
                    def = AssetDatabase.LoadAssetAtPath<VoxelMaterialDefinition>(assetPath);
                    if (def != null && def.id != id) def = null;
                    if (def == null)
                    {
                        string path = AssetDatabase.LoadMainAssetAtPath(assetPath) == null
                            ? assetPath
                            : AssetDatabase.GenerateUniqueAssetPath(assetPath);
                        def = ScriptableObject.CreateInstance<VoxelMaterialDefinition>();
                        def.id           = id;
                        def.displayName  = MaterialRegistry.DefaultDisplayName(id);
                        def.color        = MaterialRegistry.DefaultColor(id);
                        def.hardness     = DefaultHardness(id);
                        def.miningTier   = DefaultMiningTier(id);
                        def.dropAmount   = 1;
                        def.isMineable   = id != MaterialId.CrudeOil;
                        def.isFluid      = id == MaterialId.CrudeOil;
                        AssetDatabase.CreateAsset(def, path);
                        created++;
                    }
                    else
                    {
                        existingDefinitionsRegistered++;
                    }
                    registry.definitions.Add(def);
                    registryChanged = true;
                }

                ItemDefinition canonicalDrop = LoadCanonicalDropItem(id);
                if (canonicalDrop == null)
                {
                    if (def.isMineable)
                    {
                        warnings++;
                        Debug.LogWarning("[Materials] No canonical drop item was found for mineable material " + id
                            + ". The material was left unchanged; restore or review its canonical item asset before rerunning this repair.");
                    }
                    continue;
                }
                canonicalDropsForCatalog.Add(canonicalDrop);

                if (def.dropItem == null)
                {
                    def.dropItem = canonicalDrop;
                    EditorUtility.SetDirty(def);
                    dropLinksRepaired++;
                }
                else if (def.dropItem != canonicalDrop)
                {
                    // Populated links may be intentional or may reference one of the
                    // manually managed duplicate items. Preserve them for review.
                    warnings++;
                    Debug.LogWarning("[Materials] Preserved the populated drop link '" + def.dropItem.name
                        + "' on " + id + "; expected canonical asset '" + canonicalDrop.name + "'.");
                }

                if (def.isMineable && def.dropAmount <= 0)
                {
                    warnings++;
                    Debug.LogWarning("[Materials] " + id + " has non-positive dropAmount=" + def.dropAmount
                        + ". Its authored amount was preserved; review it in the material asset.");
                }
            }

            registry.Build();
            int catalogLinksAdded = EnsureDropItemsInCatalog(canonicalDropsForCatalog,
                out bool catalogChanged, out bool catalogWarning);
            if (catalogWarning) warnings++;
            if (registryChanged || dropLinksRepaired > 0)
                EditorUtility.SetDirty(registry);
            if (registryChanged || dropLinksRepaired > 0 || catalogChanged)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            Debug.Log("[Materials] Definitions created: " + created + "; existing definitions registered: "
                + existingDefinitionsRegistered + "; missing drop links repaired: " + dropLinksRepaired
                + "; item-catalog links added: " + catalogLinksAdded
                + "; warnings: " + warnings + ". Populated links and authored drop amounts were preserved.");
            Selection.activeObject = registry;
        }

        private static int EnsureDropItemsInCatalog(HashSet<ItemDefinition> dropItems,
            out bool changed, out bool warning)
        {
            changed = false;
            warning = false;
            EnsureFolder("Assets/Resources/VoxelEngine");
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(ItemCatalogPath);
            if (catalog == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(ItemCatalogPath) != null)
                {
                    warning = true;
                    Debug.LogWarning("[Materials] The ItemPersistenceCatalog path is occupied by another asset type; no catalog links were changed.");
                    return 0;
                }
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                catalog.items = new List<ItemDefinition>();
                AssetDatabase.CreateAsset(catalog, ItemCatalogPath);
                changed = true;
            }

            var original = catalog.items;
            var previouslyRegistered = new HashSet<ItemDefinition>();
            var merged = new List<ItemDefinition>();
            if (original != null)
            {
                foreach (var existing in original)
                {
                    if (existing == null || merged.Contains(existing)) continue;
                    merged.Add(existing);
                    previouslyRegistered.Add(existing);
                }
            }

            // Keep this catalog complete, not just a mining-item subset: it is also the
            // runtime persistence lookup for every inventory item in the game.
            var guids = new HashSet<string>(AssetDatabase.FindAssets("t:ItemDefinition"));
            foreach (var filter in new[] { "t:PortableBatteryItem", "t:HydrogenCanisterItem", "t:JetpackItem" })
                foreach (var guid in AssetDatabase.FindAssets(filter)) guids.Add(guid);
            var orderedGuids = new List<string>(guids);
            orderedGuids.Sort((left, right) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(left), AssetDatabase.GUIDToAssetPath(right)));
            foreach (var guid in orderedGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null && !merged.Contains(item)) merged.Add(item);
            }

            var orderedDropItems = new List<ItemDefinition>(dropItems);
            orderedDropItems.Sort((left, right) => string.CompareOrdinal(
                left != null ? left.name : string.Empty, right != null ? right.name : string.Empty));
            int added = 0;
            foreach (var item in orderedDropItems)
            {
                if (item == null) continue;
                if (!previouslyRegistered.Contains(item)) added++;
                if (!merged.Contains(item)) merged.Add(item);
            }

            bool listChanged = original == null || original.Count != merged.Count;
            if (!listChanged)
            {
                for (int i = 0; i < merged.Count; i++)
                {
                    if (original[i] == merged[i]) continue;
                    listChanged = true;
                    break;
                }
            }
            if (listChanged) changed = true;
            if (changed)
            {
                catalog.items = merged;
                EditorUtility.SetDirty(catalog);
            }
            return added;
        }

        private static ItemDefinition LoadCanonicalDropItem(MaterialId id)
        {
            string[] paths = id switch
            {
                MaterialId.Iron => new[] { "Assets/VoxelEngineAssets/Industrial/Items/Item_IronOre.asset" },
                MaterialId.Copper => new[] { "Assets/VoxelEngineAssets/Industrial/Items/Item_CopperOre.asset" },
                MaterialId.Clay or MaterialId.Grass => new[] { "Assets/VoxelEngineAssets/Items/Item_Clay.asset" },
                MaterialId.Wood => new[] { "Assets/VoxelEngineAssets/Items/Item_WoodLog.asset" },
                MaterialId.LegacySolidFloor or MaterialId.MartianDust or MaterialId.VenusAsh
                    or MaterialId.AcidBog or MaterialId.VolcanicBasalt or MaterialId.CrystalGeode
                    => new[] { "Assets/VoxelEngineAssets/Items/Item_Stone.asset" },
                _ => new[] { "Assets/VoxelEngineAssets/Items/Item_" + id + ".asset" },
            };

            foreach (string path in paths)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null) return item;
            }
            return null;
        }

        /// <summary>
        /// Fixes EXISTING material definitions so soft surface blocks (Clay, Sand, Ice, Coal) are
        /// hand-mineable (tier 0). The create-tool above is idempotent and skips existing defs, so
        /// if your Clay/Sand were created at tier 1, bare hands could not mine them. This re-applies
        /// the soft-block tier to any existing definitions of those materials.
        /// </summary>
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
