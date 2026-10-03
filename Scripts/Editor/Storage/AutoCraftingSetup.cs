#if UNITY_EDITOR
// Assets/Scripts/Editor/Storage/AutoCraftingSetup.cs
//
// Step 111 - Network auto-crafting (14.43.0).
// Authors, non-destructively:
//   - the Blank Pattern item (physical carrier of encoded recipes)
//     + its Assembler recipe, registered in the recipe registry
//   - adds the item to the runtime ItemPersistenceCatalog so saved
//     encoded patterns always resolve their base item
//   - guarantees the AutoCrafter component on the Server Controller
//     prefab (older prefabs may predate it)
//   - refreshed descriptions for the Pattern and Crafting Terminal
//     blocks teaching the encode -> file -> request flow
// Create if missing, reconnect if existing; user tweaks survive.

using UnityEngine;
using UnityEditor;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class AutoCraftingSetup
    {
        public static void RunStep111()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 111 - Network auto-crafting setup started.");

            const string ASSET_ROOT = "Assets/VoxelEngineAssets";
            const string ROOT    = ASSET_ROOT + "/Storage/Network";
            const string ITEMS   = ROOT + "/Items";
            const string RECIPES = ROOT + "/Recipes";

            foreach (var folder in new[] { ASSET_ROOT + "/Storage", ROOT, ITEMS, RECIPES })
                EnsureFolder(folder);

            int created = 0, preserved = 0;

            var registry   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var plastic    = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Plastic.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_CopperWire.asset");

            // ══════════════════════════════════════════════════════════
            //  1) BLANK PATTERN - the physical recipe carrier
            // ══════════════════════════════════════════════════════════
            string blankPath = ITEMS + "/Item_BlankPattern.asset";
            var blank = AssetDatabase.LoadAssetAtPath<ItemDefinition>(blankPath);
            if (blank == null)
            {
                blank = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(blank, blankPath);
                created++;
            }
            else preserved++;

            if (string.IsNullOrWhiteSpace(blank.itemId)) blank.itemId = VoxelEngine.Storage.PatternItems.BlankItemId;
            if (string.IsNullOrWhiteSpace(blank.displayName)) blank.displayName = "Blank Pattern";
            blank.description = "Writable crystal matrix for the mass-storage network. Take it to a " +
                                "Pattern Terminal to encode any unlocked recipe onto it, then file the " +
                                "encoded pattern in a Server Controller's pattern bank (capacity = " +
                                "installed RAM units). The network can auto-craft every filed recipe - " +
                                "as long as the required crafting station is linked to the network.";
            if (blank.iconTint == Color.white) blank.iconTint = VoxelEngine.Storage.PatternItems.PatternTint;
            if (blank.maxStack == 900) blank.maxStack = 64;
            if (blank.massPerUnit <= 0f || Mathf.Approximately(blank.massPerUnit, 1f)) blank.massPerUnit = 0.1f;
            if (string.IsNullOrWhiteSpace(blank.category) || blank.category == "Misc") blank.category = "Storage";
            EditorUtility.SetDirty(blank);

            // Recipe: cheap enough to encode freely, Assembler for the theme.
            {
                string recipePath = RECIPES + "/Recipe_BlankPattern.asset";
                var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(recipePath);
                bool isNew = recipe == null;
                if (isNew)
                {
                    recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                    AssetDatabase.CreateAsset(recipe, recipePath);
                    created++;
                }
                else preserved++;

                if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = "Blank Pattern";
                recipe.outputItem = blank;
                if (isNew)
                {
                    recipe.outputCount = 2;
                    recipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                    recipe.craftSeconds = 2f;
                    recipe.unlockedByDefault = true;
                }
                else if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 2f;
                if (recipe.outputCount <= 0) recipe.outputCount = 2;

                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    var list = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                    if (plastic != null)    list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = plastic, count = 1 });
                    if (copperWire != null) list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = copperWire, count = 1 });
                    recipe.inputs = list.ToArray();
                }
                EditorUtility.SetDirty(recipe);
                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }

            // ══════════════════════════════════════════════════════════
            //  2) PERSISTENCE CATALOG - saved patterns must resolve
            // ══════════════════════════════════════════════════════════
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(
                    "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset");
                if (catalog != null)
                {
                    catalog.items ??= new System.Collections.Generic.List<ItemDefinition>();
                    if (!catalog.items.Contains(blank))
                    {
                        catalog.items.Add(blank);
                        EditorUtility.SetDirty(catalog);
                    }
                    preserved++;
                }
                else Debug.LogWarning("[Step 111] ItemPersistenceCatalog not found - run step 1 " +
                                      "(or any step that authors it) so saved patterns resolve in builds.");
            }

            // ══════════════════════════════════════════════════════════
            //  3) SERVER CONTROLLER PREFAB - guarantee the AutoCrafter
            // ══════════════════════════════════════════════════════════
            {
                var rackItem = AssetDatabase.LoadAssetAtPath<BlockItem>(
                    ASSET_ROOT + "/Survival/StorageBlocks/Block_ServerRack.asset");
                if (rackItem != null && rackItem.placedPrefab != null)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(rackItem.placedPrefab);
                    var root = PrefabUtility.LoadPrefabContents(prefabPath);
                    try
                    {
                        if (root.GetComponentInChildren<VoxelEngine.Storage.AutoCrafter>(true) == null)
                        {
                            root.AddComponent<VoxelEngine.Storage.AutoCrafter>();
                            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        }
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    preserved++;
                }
                else Debug.LogWarning("[Step 111] Block_ServerRack.asset missing - run the storage steps first.");
            }

            // ══════════════════════════════════════════════════════════
            //  4) TERMINAL DESCRIPTIONS - teach the new flow
            // ══════════════════════════════════════════════════════════
            void Describe(string path, string description)
            {
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (it == null) { Debug.LogWarning($"[Step 111] Missing asset: {path}"); return; }
                it.description = description;
                EditorUtility.SetDirty(it);
                preserved++;
            }

            Describe(ASSET_ROOT + "/Survival/StorageBlocks/Block_PatternTerminal.asset",
                "Encoding bench of the mass-storage network. Bring Blank Patterns, pick any " +
                "unlocked recipe and encode it; the pattern is filed in the Server Controller's " +
                "pattern bank (capacity = installed RAM units). Patterns are physical - eject " +
                "one here to carry the recipe to another network.");

            Describe(ASSET_ROOT + "/Survival/StorageBlocks/Block_CraftingTerminal.asset",
                "Order desk of the mass-storage network. Request any filed pattern and the " +
                "Server Controller crafts it from stored materials - CPU sets the speed, an " +
                "active craft draws extra watts, and recipes that need a station (Assembler, " +
                "Furnace...) only run while one is linked to the network. Missing intermediates " +
                "with their own pattern are crafted first, automatically.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Step 111] Network auto-crafting complete - {created} created, {preserved} touched. " +
                      "Flow: craft Blank Patterns (Assembler) -> encode recipes at the Pattern Terminal -> " +
                      "request crafts at the Crafting Terminal. Stations join the network by touch or Data Pipe.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
