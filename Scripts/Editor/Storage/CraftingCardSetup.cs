#if UNITY_EDITOR
// Assets/Scripts/Editor/Storage/CraftingCardSetup.cs
//
// Step 112 - Keep-stocked logistics (14.44.0).
// Authors, non-destructively:
//   - the Crafting Card upgrade item (goes in the Storage Exporter's
//     third upgrade slot: whitelist shortfalls are ordered from the
//     Server Controller's auto-crafter) + its Assembler recipe
//   - adds the card to the runtime ItemPersistenceCatalog
//   - refreshed descriptions for the Storage Importer and Exporter
//     blocks teaching the editable filter and the keep-stocked target
// Create if missing, reconnect if existing; user tweaks survive.

using UnityEngine;
using UnityEditor;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class CraftingCardSetup
    {
        public static void RunStep112()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 112 - Keep-stocked logistics setup started.");

            const string ASSET_ROOT = "Assets/VoxelEngineAssets";
            const string ROOT    = ASSET_ROOT + "/Storage/Network";
            const string ITEMS   = ROOT + "/Items";
            const string RECIPES = ROOT + "/Recipes";

            foreach (var folder in new[] { ASSET_ROOT + "/Storage", ROOT, ITEMS, RECIPES })
                EnsureFolder(folder);

            int created = 0, preserved = 0;

            var registry   = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var circuit    = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_Circuit.asset");
            var copperWire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_CopperWire.asset");
            var blank      = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ITEMS + "/Item_BlankPattern.asset");
            if (blank == null)
                Debug.LogWarning("[Step 112] Item_BlankPattern.asset missing - run step 111 first; " +
                                 "the card recipe will author without it.");

            // ══════════════════════════════════════════════════════════
            //  1) CRAFTING CARD - exporter upgrade
            // ══════════════════════════════════════════════════════════
            string cardPath = ITEMS + "/Item_CraftingCard.asset";
            var card = AssetDatabase.LoadAssetAtPath<ItemDefinition>(cardPath);
            if (card == null)
            {
                card = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(card, cardPath);
                created++;
            }
            else preserved++;

            if (string.IsNullOrWhiteSpace(card.itemId))
                card.itemId = VoxelEngine.Storage.StorageExporter.CraftingCardItemId;
            if (string.IsNullOrWhiteSpace(card.displayName)) card.displayName = "Crafting Card";
            card.description = "Exporter upgrade. When the storage network cannot supply a " +
                               "whitelisted item the exporter wants to move, the shortfall is " +
                               "ordered from the Server Controller's auto-crafter - the recipe " +
                               "must have a filed pattern. Pair it with the exporter's KEEP " +
                               "STOCKED target and a shelf refills itself from raw materials.";
            if (card.iconTint == Color.white) card.iconTint = new Color(0.58f, 0.38f, 0.85f);
            if (card.maxStack == 900) card.maxStack = 8;
            if (card.massPerUnit <= 0f || Mathf.Approximately(card.massPerUnit, 1f)) card.massPerUnit = 0.2f;
            if (string.IsNullOrWhiteSpace(card.category) || card.category == "Misc") card.category = "Storage";
            EditorUtility.SetDirty(card);

            // Recipe: a blank pattern teaches the card how to ask.
            {
                string recipePath = RECIPES + "/Recipe_CraftingCard.asset";
                var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(recipePath);
                bool isNew = recipe == null;
                if (isNew)
                {
                    recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                    AssetDatabase.CreateAsset(recipe, recipePath);
                    created++;
                }
                else preserved++;

                if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = "Crafting Card";
                recipe.outputItem = card;
                if (isNew)
                {
                    recipe.outputCount = 1;
                    recipe.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                    recipe.craftSeconds = 4f;
                    recipe.unlockedByDefault = true;
                }
                else if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 4f;
                if (recipe.outputCount <= 0) recipe.outputCount = 1;

                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    var list = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                    if (blank != null)      list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = blank, count = 1 });
                    if (circuit != null)    list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = circuit, count = 1 });
                    if (copperWire != null) list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = copperWire, count = 2 });
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
            //  2) PERSISTENCE CATALOG
            // ══════════════════════════════════════════════════════════
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(
                    "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset");
                if (catalog != null)
                {
                    catalog.items ??= new System.Collections.Generic.List<ItemDefinition>();
                    if (!catalog.items.Contains(card))
                    {
                        catalog.items.Add(card);
                        EditorUtility.SetDirty(catalog);
                    }
                    preserved++;
                }
                else Debug.LogWarning("[Step 112] ItemPersistenceCatalog not found - run an earlier " +
                                      "step that authors it so the card resolves in builds.");
            }

            // ══════════════════════════════════════════════════════════
            //  3) IMPORTER / EXPORTER DESCRIPTIONS - teach the new flow
            // ══════════════════════════════════════════════════════════
            void Describe(string path, string description)
            {
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (it == null) { Debug.LogWarning($"[Step 112] Missing asset: {path}"); return; }
                it.description = description;
                EditorUtility.SetDirty(it);
                preserved++;
            }

            Describe(ASSET_ROOT + "/Survival/StorageBlocks/Block_StorageExporter.asset",
                "Pushes items from the storage network into adjacent chests. The filter is " +
                "edited right in its panel (whitelist or blacklist), the KEEP STOCKED target " +
                "fills each container up to a set count and then idles, and a Crafting Card " +
                "in the third upgrade slot orders whitelist shortfalls from the auto-crafter. " +
                "Connect with Data Pipes or by touching the system.");

            Describe(ASSET_ROOT + "/Survival/StorageBlocks/Block_StorageImporter.asset",
                "Pulls items from adjacent chests into the storage network. The filter is " +
                "edited right in its panel - blacklist mode imports everything except the " +
                "listed items, whitelist mode only the listed ones. Connect with Data Pipes " +
                "or by touching the system.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Step 112] Keep-stocked logistics complete - {created} created, {preserved} touched. " +
                      "Flow: whitelist items on the exporter -> set KEEP STOCKED -> slot a Crafting Card " +
                      "and the shelf reorders its own refills from the pattern bank.");
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
