// Assets/Scripts/Editor/Crafting/ScienceRecipeRepair.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Collapses duplicate science-pack recipes onto one craftable recipe per tier.
    /// Token items, the hammer, and unrelated recipes are not touched.
    /// </summary>
    public static class ScienceRecipeRepair
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string CanonicalFolder = Root + "/Recipes";

        public static void Run()
        {
            int deleted = Repair(out int kept, out int rewired);
            AssetDatabase.SaveAssets();
            string message =
                "Science pack recipes repaired.\n\n" +
                $"Canonical recipes kept: {kept}\n" +
                $"Duplicate recipe assets deleted: {deleted}\n" +
                $"Research costs rewired: {rewired}\n\n" +
                "Pack I crafts in hand. Pack II needs a Crafting Bench. Pack III needs an Assembler.";
            Debug.Log("[ScienceRecipeRepair] " + message.Replace("\n", " "));
            EditorUtility.DisplayDialog("Voxel Engine Setup", message, "OK");
        }

        public static int Repair(out int kept, out int rewired)
        {
            kept = 0;
            rewired = 0;
            EnsureFolder(CanonicalFolder);

            var items = LoadAssets<ItemDefinition>();
            var packs = new ScienceItem[4];
            for (int tier = 1; tier <= 3; tier++)
                packs[tier] = FindPack(items, tier);

            var wood = FindItem(items, Root + "/Items/Item_WoodLog.asset", "wood_log", "Wood Log");
            var stone = FindItem(items, Root + "/Items/Item_Stone.asset", "stone", "Stone");
            var iron = FindItem(items, Root + "/Items/Item_IronIngot.asset", "iron_ingot", "Iron Ingot");
            var copper = FindItem(items, Root + "/Items/Item_CopperIngot.asset", "copper_ingot", "Copper Ingot");
            var steel = FindItem(items, Root + "/Items/Item_SteelIngot.asset", "steel_ingot", "Steel Ingot");

            var byTier = new List<RecipeDefinition>[4];
            for (int tier = 1; tier <= 3; tier++) byTier[tier] = new List<RecipeDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
                if (recipe == null) continue;
                int tier = ScienceTier(recipe);
                if (tier >= 1 && tier <= 3) byTier[tier].Add(recipe);
            }

            var canonical = new RecipeDefinition[4];
            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
            for (int tier = 1; tier <= 3; tier++)
            {
                if (packs[tier] == null) continue;
                canonical[tier] = WriteCanonical(registry, tier, packs[tier], byTier[tier], wood, stone, iron, copper, steel);
                if (canonical[tier] != null) kept++;
            }

            var doomed = new List<RecipeDefinition>();
            for (int tier = 1; tier <= 3; tier++)
            {
                foreach (var recipe in byTier[tier])
                {
                    if (canonical[tier] != null && recipe == canonical[tier]) continue;
                    doomed.Add(recipe);
                }
            }

            foreach (var reg in LoadAssets<RecipeRegistry>())
            {
                if (reg.recipes == null) continue;
                int before = reg.recipes.Count;
                reg.recipes.RemoveAll(recipe => recipe == null || doomed.Contains(recipe) || IsDuplicateScience(recipe, canonical));
                for (int tier = 1; tier <= 3; tier++)
                {
                    if (canonical[tier] != null && !reg.recipes.Contains(canonical[tier]))
                        reg.recipes.Add(canonical[tier]);
                }
                if (reg.recipes.Count != before || doomed.Count > 0)
                    EditorUtility.SetDirty(reg);
            }

            foreach (var node in LoadAssets<ResearchNode>())
            {
                if (node.cost == null) continue;
                bool changed = false;
                for (int i = 0; i < node.cost.Length; i++)
                {
                    var cost = node.cost[i];
                    if (cost.pack == null) continue;
                    int tier = cost.pack.tier;
                    if (tier < 1 || tier > 3 || packs[tier] == null || cost.pack == packs[tier]) continue;
                    cost.pack = packs[tier];
                    node.cost[i] = cost;
                    changed = true;
                    rewired++;
                }
                if (changed) EditorUtility.SetDirty(node);
            }

            int deleted = 0;
            foreach (var recipe in doomed)
            {
                string path = AssetDatabase.GetAssetPath(recipe);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.DeleteAsset(path)) deleted++;
            }
            return deleted;
        }

        private static RecipeDefinition WriteCanonical(
            RecipeRegistry registry, int tier, ScienceItem pack, List<RecipeDefinition> existing,
            ItemDefinition wood, ItemDefinition stone, ItemDefinition iron,
            ItemDefinition copper, ItemDefinition steel)
        {
            string path = $"{CanonicalFolder}/Recipe_ScienceT{tier}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, path);
            }

            recipe.displayName = string.IsNullOrEmpty(pack.displayName) ? $"Science Pack {tier}" : pack.displayName;
            recipe.outputItem = pack;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            recipe.unlockedByDefault = true;
            recipe.requiredStation = tier switch
            {
                1 => StationTier.None,
                2 => StationTier.CraftingBench,
                _ => StationTier.Assembler
            };
            if (!HasCraftableInputs(recipe))
            {
                RecipeDefinition donor = null;
                int donorScore = int.MinValue;
                if (existing != null)
                {
                    foreach (var other in existing)
                    {
                        if (other == null || other == recipe || !HasCraftableInputs(other)) continue;
                        int score = other.inputs.Length;
                        if (score > donorScore)
                        {
                            donor = other;
                            donorScore = score;
                        }
                    }
                }
                if (donor != null)
                    recipe.inputs = CloneInputs(donor.inputs);
                else
                {
                    recipe.inputs = tier switch
                    {
                        1 => Ingredients((wood, 1), (stone, 1)),
                        2 => Ingredients((iron, 1), (copper, 1)),
                        _ => Ingredients((steel, 1), (copper, 2))
                    };
                }
            }
            EditorUtility.SetDirty(recipe);
            if (registry != null && registry.recipes != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }
            return recipe;
        }

        private static RecipeIngredient[] CloneInputs(RecipeIngredient[] inputs)
        {
            if (inputs == null) return System.Array.Empty<RecipeIngredient>();
            var copy = new RecipeIngredient[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
                copy[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
            return copy;
        }

        private static bool HasCraftableInputs(RecipeDefinition recipe)
        {
            if (recipe.inputs == null || recipe.inputs.Length == 0) return false;
            foreach (var input in recipe.inputs)
                if (input.item != null && input.count > 0) return true;
            return false;
        }

        private static RecipeIngredient[] Ingredients(params (ItemDefinition item, int count)[] inputs)
        {
            var list = new List<RecipeIngredient>();
            foreach (var input in inputs)
            {
                if (input.item == null || input.count <= 0) continue;
                list.Add(new RecipeIngredient { item = input.item, count = input.count });
            }
            return list.ToArray();
        }

        private static bool IsDuplicateScience(RecipeDefinition recipe, RecipeDefinition[] canonical)
        {
            int tier = ScienceTier(recipe);
            return tier >= 1 && tier <= 3 && canonical[tier] != null && recipe != canonical[tier];
        }

        private static int ScienceTier(RecipeDefinition recipe)
        {
            if (recipe == null) return 0;
            if (recipe.outputItem is ScienceItem pack && pack.tier >= 1 && pack.tier <= 3)
                return pack.tier;
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(recipe));
            if (name == "Recipe_ScienceT1") return 1;
            if (name == "Recipe_ScienceT2") return 2;
            if (name == "Recipe_ScienceT3") return 3;
            return 0;
        }

        private static ScienceItem FindPack(List<ItemDefinition> items, int tier)
        {
            string path = $"{Root}/Items/Item_ScienceT{tier}.asset";
            var direct = AssetDatabase.LoadAssetAtPath<ScienceItem>(path);
            if (direct != null) return direct;
            ScienceItem fallback = null;
            foreach (var item in items)
            {
                if (item is not ScienceItem pack || pack.tier != tier) continue;
                if (string.Equals(pack.itemId, "item_sciencet" + tier, StringComparison.OrdinalIgnoreCase))
                    return pack;
                fallback ??= pack;
            }
            return fallback;
        }

        private static ItemDefinition FindItem(List<ItemDefinition> items, string path, string itemId, string displayName)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (direct != null) return direct;
            ItemDefinition byName = null;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (string.Equals(item.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                    return item;
                if (byName == null && string.Equals(item.displayName, displayName, StringComparison.OrdinalIgnoreCase))
                    byName = item;
            }
            return byName;
        }

        private static List<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string name = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
