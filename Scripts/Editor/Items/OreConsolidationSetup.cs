#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Simulation;
using VoxelEngine.Materials;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 80 (11.3.0-dev): consolidate the duplicate ore items.
    ///
    /// Iron and copper ore each existed as two assets. The bare
    /// <c>Items/Item_Iron</c> and <c>Items/Item_Copper</c> duplicates carried no icon, no
    /// resource category and a <c>Misc</c> classification, but the smelting recipes pointed
    /// at them; meanwhile the persistence catalogue handed the player the Resource-typed
    /// <c>Industrial/Items/Item_IronOre</c> and <c>Item_CopperOre</c>. The two never matched,
    /// so the furnaces refused ore the player was plainly holding.
    ///
    /// This step makes the Industrial assets canonical: every reference to a retired
    /// duplicate is repointed, the voxel material drop is repointed so mining yields the
    /// canonical ore, and the retired assets are then deleted. Saves are unaffected — the
    /// retired ids are aliased to their replacements in <see cref="ItemIdAliases"/>, so an
    /// existing stack of "iron" loads back as Iron Ore.
    ///
    /// Safe to re-run: once the duplicates are gone there is nothing left to repoint, and
    /// the step reports that it found nothing to do. Every change logs with [Setup 80].
    /// </summary>
    public static class OreConsolidationSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";

        /// <summary>One consolidation: the asset that survives and the duplicate it replaces.</summary>
        private readonly struct Merge
        {
            public readonly string Label;
            public readonly string CanonicalPath;
            public readonly string RetiredPath;

            public Merge(string label, string canonicalPath, string retiredPath)
            {
                Label = label;
                CanonicalPath = canonicalPath;
                RetiredPath = retiredPath;
            }
        }

        private static readonly Merge[] Merges =
        {
            new("Iron Ore",   Root + "/Industrial/Items/Item_IronOre.asset",   Root + "/Items/Item_Iron.asset"),
            new("Copper Ore", Root + "/Industrial/Items/Item_CopperOre.asset", Root + "/Items/Item_Copper.asset"),
        };

        public static void RunStep80()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ore Consolidation",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                // Resolve every pair first: nothing is written until the whole plan is valid.
                var plan = new List<(Merge merge, ItemDefinition canonical, ItemDefinition retired)>();
                foreach (var merge in Merges)
                {
                    var canonical = AssetDatabase.LoadAssetAtPath<ItemDefinition>(merge.CanonicalPath);
                    var retired   = AssetDatabase.LoadAssetAtPath<ItemDefinition>(merge.RetiredPath);

                    if (canonical == null)
                    {
                        Debug.LogError("[Setup 80] The canonical asset for " + merge.Label + " is missing at " +
                                       merge.CanonicalPath + ". Run step 10 (Build Industrial Content) first.");
                        EditorUtility.DisplayDialog("Ore Consolidation",
                            "The canonical " + merge.Label + " asset is missing.\n\n" +
                            "Run step 10 (Build Industrial Content) first, then run this step again.", "OK");
                        return;
                    }
                    plan.Add((merge, canonical, retired));
                }

                int repointed = 0, deleted = 0, alreadyClean = 0;
                foreach (var (merge, canonical, retired) in plan)
                {
                    if (retired == null)
                    {
                        alreadyClean++;
                        Debug.Log("[Setup 80] " + merge.Label + " is already consolidated — no duplicate at " +
                                  merge.RetiredPath + ".");
                        continue;
                    }

                    int changes = RepointAll(retired, canonical, merge.Label);
                    repointed += changes;

                    AssetDatabase.SaveAssets();
                    if (AssetDatabase.DeleteAsset(merge.RetiredPath))
                    {
                        deleted++;
                        Debug.Log("[Setup 80] Deleted the retired " + merge.Label + " duplicate at " + merge.RetiredPath +
                                  " after repointing " + changes + " reference(s).");
                    }
                    else
                    {
                        Debug.LogWarning("[Setup 80] Repointed " + changes + " reference(s) for " + merge.Label +
                                         " but could not delete " + merge.RetiredPath + ". Delete it by hand.");
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string summary = alreadyClean == Merges.Length
                    ? "Both ores are already consolidated. Nothing to do."
                    : repointed + " reference(s) repointed to the canonical ore assets, and " +
                      deleted + " retired duplicate(s) deleted.\n\n" +
                      "Iron Ore  -> Industrial/Items/Item_IronOre\n" +
                      "Copper Ore -> Industrial/Items/Item_CopperOre\n\n" +
                      "Existing saves are safe: the retired ids resolve to the surviving items, " +
                      "so stacks already in the world come back as the canonical ore.";

                Debug.Log("[Setup 80] Ore consolidation complete: " + repointed + " reference(s) repointed, " +
                          deleted + " duplicate(s) deleted, " + alreadyClean + " already clean.");

                EditorUtility.DisplayDialog("Ore Consolidation", summary, "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Ore Consolidation",
                    "Consolidation stopped: " + ex.Message + "\n\nRe-run the step once the cause is fixed.", "OK");
            }
        }

        /// <summary>
        /// Repoint every asset that references <paramref name="retired"/> at
        /// <paramref name="canonical"/>. Returns how many individual references changed.
        /// </summary>
        private static int RepointAll(ItemDefinition retired, ItemDefinition canonical, string label)
        {
            int changes = 0;
            changes += RepointSmeltingRecipes(retired, canonical, label);
            changes += RepointCraftingRecipes(retired, canonical, label);
            changes += RepointMachineRecipes(retired, canonical, label);
            changes += RepointVoxelMaterials(retired, canonical, label);
            changes += RepointPersistenceCatalogs(retired, canonical, label);
            return changes;
        }

        private static int RepointSmeltingRecipes(ItemDefinition retired, ItemDefinition canonical, string label)
        {
            int changes = 0;
            foreach (var recipe in LoadAll<SmeltingRecipe>())
            {
                bool dirty = false;
                if (recipe.input == retired)  { recipe.input = canonical;  dirty = true; changes++; }
                if (recipe.output == retired) { recipe.output = canonical; dirty = true; changes++; }
                if (dirty)
                {
                    EditorUtility.SetDirty(recipe);
                    Debug.Log("[Setup 80] " + recipe.name + " now points at the canonical " + label + ".");
                }
            }
            return changes;
        }

        private static int RepointCraftingRecipes(ItemDefinition retired, ItemDefinition canonical, string label)
        {
            int changes = 0;
            foreach (var recipe in LoadAll<RecipeDefinition>())
            {
                bool dirty = false;
                if (recipe.outputItem == retired) { recipe.outputItem = canonical; dirty = true; changes++; }
                if (recipe.inputs != null)
                {
                    for (int i = 0; i < recipe.inputs.Length; i++)
                    {
                        if (recipe.inputs[i].item != retired) continue;
                        recipe.inputs[i] = new RecipeIngredient { item = canonical, count = recipe.inputs[i].count };
                        dirty = true; changes++;
                    }
                }
                if (dirty)
                {
                    EditorUtility.SetDirty(recipe);
                    Debug.Log("[Setup 80] " + recipe.name + " now points at the canonical " + label + ".");
                }
            }
            return changes;
        }

        private static int RepointMachineRecipes(ItemDefinition retired, ItemDefinition canonical, string label)
        {
            int changes = 0;
            foreach (var recipe in LoadAll<MachineRecipe>())
            {
                bool dirty = false;
                if (recipe.outputItem == retired)    { recipe.outputItem = canonical;    dirty = true; changes++; }
                if (recipe.byproductItem == retired) { recipe.byproductItem = canonical; dirty = true; changes++; }
                if (recipe.inputs != null)
                {
                    // MachineRecipeSlot is a struct: mutate through the array index, not a
                    // foreach copy, or the change is written to a temporary and lost.
                    for (int i = 0; i < recipe.inputs.Length; i++)
                    {
                        if (recipe.inputs[i].item != retired) continue;
                        recipe.inputs[i] = new MachineRecipeSlot { item = canonical, count = recipe.inputs[i].count };
                        dirty = true; changes++;
                    }
                }
                if (dirty)
                {
                    EditorUtility.SetDirty(recipe);
                    Debug.Log("[Setup 80] " + recipe.name + " now points at the canonical " + label + ".");
                }
            }
            return changes;
        }

        /// <summary>
        /// The voxel material's drop item is what mining actually yields, so this is the
        /// reference that decides which ore ends up in the player's hands.
        /// </summary>
        private static int RepointVoxelMaterials(ItemDefinition retired, ItemDefinition canonical, string label)
        {
            int changes = 0;
            foreach (var material in LoadAll<VoxelMaterialDefinition>())
            {
                if (material.dropItem != retired) continue;
                material.dropItem = canonical;
                EditorUtility.SetDirty(material);
                changes++;
                Debug.Log("[Setup 80] Voxel material " + material.name + " now drops the canonical " + label + ".");
            }
            return changes;
        }

        /// <summary>
        /// The persistence catalogue is what lets a saved stack resolve at load. The
        /// canonical item must be in it and the retired one must not.
        /// </summary>
        private static int RepointPersistenceCatalogs(ItemDefinition retired, ItemDefinition canonical, string label)
        {
            int changes = 0;
            foreach (var catalog in LoadAll<ItemPersistenceCatalog>())
            {
                if (catalog.items == null) continue;
                bool dirty = false;

                if (catalog.items.Remove(retired))
                {
                    dirty = true; changes++;
                    Debug.Log("[Setup 80] Removed the retired " + label + " from " + catalog.name + ".");
                }
                if (!catalog.items.Contains(canonical))
                {
                    catalog.items.Add(canonical);
                    dirty = true; changes++;
                    Debug.Log("[Setup 80] Added the canonical " + label + " to " + catalog.name + ".");
                }
                if (dirty) EditorUtility.SetDirty(catalog);
            }
            return changes;
        }

        private static List<T> LoadAll<T>() where T : UnityEngine.Object
        {
            var result = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) result.Add(asset);
            }
            return result;
        }
    }
}
#endif
