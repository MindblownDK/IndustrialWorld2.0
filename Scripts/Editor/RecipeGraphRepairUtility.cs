// Assets/Scripts/VoxelEngine/Editor/RecipeGraphRepairUtility.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Simulation;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive recipe-link repair pass used after the validator finds
    /// missing ScriptableObject references from old setup generations. It only
    /// fills missing/empty recipe fields and creates missing base ore items that
    /// the current factory recipes require.
    /// </summary>
    public static class RecipeGraphRepairUtility
    {
        private const string Root = "Assets/VoxelEngineAssets";

        [MenuItem("Tools/Voxel Engine/Repair Missing Recipe Links")]
        public static void RepairMissingRecipeLinks()
        {
            int repaired = 0;
            int created = 0;

            var items = LoadAssets<ItemDefinition>();
            EnsureBaseResource(ref items, ref created, "Industrial/Items/Item_IronOre.asset", "iron_ore", "Iron Ore", new Color(0.65f, 0.45f, 0.32f));
            EnsureBaseResource(ref items, ref created, "Industrial/Items/Item_CopperOre.asset", "copper_ore", "Copper Ore", new Color(0.74f, 0.40f, 0.22f));
            EnsureBaseResource(ref items, ref created, "Industrial/Items/Item_Sand.asset", "sand", "Sand", new Color(0.82f, 0.72f, 0.50f));
            EnsureBaseResource(ref items, ref created, "Nuclear/Items/Item_Cobalt.asset", "cobalt", "Cobalt", new Color(0.22f, 0.40f, 0.88f));

            var byPath = items.ToDictionary(AssetDatabase.GetAssetPath, item => item);
            var byName = BuildItemNameIndex(items);

            repaired += CopyValidDuplicateRecipeLinks();
            repaired += RepairKnownCraftingRecipes(byPath, byName);
            repaired += RepairKnownSmeltingRecipes(byPath, byName);
            repaired += RepairKnownMachineRecipes(byPath, byName);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RecipeGraphRepair] Complete. Repaired links: {repaired}. Created base items: {created}. Run the Recipe Graph Validator again.");
            EditorUtility.DisplayDialog("Recipe Graph Repair", $"Repair complete.\n\nRepaired links: {repaired}\nCreated base items: {created}\n\nRun the Recipe Graph Validator again.", "OK");
        }

        private static List<T> LoadAssets<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<T>(path))
                .Where(asset => asset != null)
                .Distinct()
                .ToList();
        }

        private static Dictionary<string, ItemDefinition> BuildItemNameIndex(IEnumerable<ItemDefinition> items)
        {
            var map = new Dictionary<string, ItemDefinition>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (item == null) continue;
                Add(item.name, item);
                Add(item.itemId, item);
                Add(item.displayName, item);
                Add(AssetDatabase.GetAssetPath(item).Split('/').Last().Replace(".asset", string.Empty), item);
            }
            return map;

            void Add(string key, ItemDefinition item)
            {
                if (string.IsNullOrWhiteSpace(key) || map.ContainsKey(key)) return;
                map.Add(key.Trim(), item);
            }
        }

        private static void EnsureBaseResource(ref List<ItemDefinition> items, ref int created, string relativePath, string itemId, string display, Color tint)
        {
            string path = $"{Root}/{relativePath}";
            var item = AssetDatabase.LoadAssetAtPath<ResourceItem>(path);
            bool wasCreated = item == null;
            if (wasCreated)
            {
                EnsureFolder(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'));
                item = ScriptableObject.CreateInstance<ResourceItem>();
                AssetDatabase.CreateAsset(item, path);
                created++;
            }

            bool changed = false;
            if (wasCreated || string.IsNullOrWhiteSpace(item.itemId) || item.itemId == "iron_ore") { item.itemId = itemId; changed = true; }
            if (wasCreated || string.IsNullOrWhiteSpace(item.displayName) || item.displayName == "Iron Ore") { item.displayName = display; changed = true; }
            changed |= SetIfEmpty(ref item.description, $"Base production resource: {display}.");
            if (item.maxStack <= 0) { item.maxStack = 999; changed = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 1f; changed = true; }
            if (wasCreated || string.IsNullOrWhiteSpace(item.category)) { item.category = "Resource"; changed = true; }
            if (wasCreated || item.iconTint == default) { item.iconTint = tint; changed = true; }
            if (changed) EditorUtility.SetDirty(item);
            if (!items.Contains(item)) items.Add(item);
        }

        private static bool SetIfEmpty(ref string field, string value)
        {
            if (!string.IsNullOrWhiteSpace(field)) return false;
            field = value;
            return true;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(folder);
            EnsureFolder(parent);
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        private static int CopyValidDuplicateRecipeLinks()
        {
            int repaired = 0;
            var recipes = LoadAssets<RecipeDefinition>();
            var groups = recipes.GroupBy(r => r.name).Where(g => g.Count() > 1);
            foreach (var group in groups)
            {
                var template = group.FirstOrDefault(IsCraftingRecipeComplete);
                if (template == null) continue;
                foreach (var recipe in group)
                {
                    if (recipe == template || IsCraftingRecipeComplete(recipe)) continue;
                    repaired += FillCraftingRecipeFromTemplate(recipe, template);
                }
            }
            return repaired;
        }

        private static bool IsCraftingRecipeComplete(RecipeDefinition recipe)
        {
            return recipe != null
                && recipe.outputItem != null
                && recipe.outputCount > 0
                && recipe.inputs != null
                && recipe.inputs.Length > 0
                && recipe.inputs.All(input => input.item != null && input.count > 0);
        }

        private static int FillCraftingRecipeFromTemplate(RecipeDefinition target, RecipeDefinition template)
        {
            int repaired = 0;
            if (target.outputItem == null && template.outputItem != null) { target.outputItem = template.outputItem; repaired++; }
            if (target.outputCount <= 0 && template.outputCount > 0) { target.outputCount = template.outputCount; repaired++; }
            if (NeedsInputRepair(target.inputs)) { target.inputs = template.inputs.ToArray(); repaired++; }
            if (string.IsNullOrWhiteSpace(target.displayName)) target.displayName = template.displayName;
            EditorUtility.SetDirty(target);
            return repaired;
        }

        private static bool NeedsInputRepair(RecipeIngredient[] inputs)
        {
            return inputs == null || inputs.Length == 0 || inputs.Any(input => input.item == null || input.count <= 0);
        }

        private static int RepairKnownCraftingRecipes(Dictionary<string, ItemDefinition> byPath, Dictionary<string, ItemDefinition> byName)
        {
            int repaired = 0;
            ItemDefinition Item(string key) => byName.TryGetValue(key, out var item) ? item : null;
            ItemDefinition PathItem(string path) => byPath.TryGetValue($"{Root}/{path}", out var item) ? item : null;

            void Repair(string recipeRelativePath, ItemDefinition output, int outputCount, params (ItemDefinition item, int count)[] inputs)
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>($"{Root}/{recipeRelativePath}");
                if (recipe == null) return;
                bool changed = false;
                if (recipe.outputItem == null && output != null) { recipe.outputItem = output; changed = true; repaired++; }
                if (recipe.outputCount <= 0 && outputCount > 0) { recipe.outputCount = outputCount; changed = true; repaired++; }
                if (NeedsInputRepair(recipe.inputs))
                {
                    var valid = inputs.Where(i => i.item != null && i.count > 0)
                        .Select(i => new RecipeIngredient { item = i.item, count = i.count })
                        .ToArray();
                    if (valid.Length > 0) { recipe.inputs = valid; changed = true; repaired++; }
                }
                if (changed) EditorUtility.SetDirty(recipe);
            }

            var woodLog = Item("Item_WoodLog") ?? Item("Wood Log");
            var plank = Item("Item_WoodenPlank") ?? Item("Wooden Plank");
            var stone = Item("Item_Stone") ?? Item("Stone");
            var iron = Item("Item_IronIngot") ?? Item("Iron Ingot");
            var copper = Item("Item_CopperIngot") ?? Item("Copper Ingot");
            var steel = Item("Item_SteelIngot") ?? Item("Steel Ingot");
            var glass = Item("Item_Glass") ?? Item("Glass");
            var ironPlate = Item("Item_IronPlate") ?? Item("Iron Plate");
            var copperPlate = Item("Item_CopperPlate") ?? Item("Copper Plate");
            var steelPlate = Item("Item_SteelPlate") ?? Item("Steel Plate");
            var ironGear = Item("Item_IronGear") ?? Item("Iron Gear");
            var copperWire = Item("Item_CopperWire") ?? Item("Copper LV Wire") ?? Item("Copper Wire");
            var circuit = Item("Item_Circuit") ?? Item("Electronic Circuit");
            var science1 = Item("Item_ScienceT1") ?? Item("Science Pack I");
            var science2 = Item("Item_ScienceT2") ?? Item("Science Pack II");
            var science3 = Item("Item_ScienceT3") ?? Item("Science Pack III");
            var cobalt = Item("Item_Cobalt") ?? Item("Cobalt");

            Repair("Recipes/Recipe_Plank.asset", plank, 2, (woodLog, 1));
            Repair("Recipes/Recipe_PickWood.asset", PathItem("Tools/Tool_WoodPickaxe.asset") ?? Item("Wooden Pickaxe"), 1, (woodLog, 3), (plank, 2));
            Repair("Recipes/Recipe_AxeWood.asset", PathItem("Tools/Tool_WoodAxe.asset") ?? Item("Wooden Axe"), 1, (woodLog, 3), (plank, 2));
            Repair("Recipes/Recipe_PickStone.asset", PathItem("Tools/Tool_StonePickaxe.asset") ?? Item("Stone Pickaxe"), 1, (stone, 5), (plank, 2));
            Repair("Recipes/Recipe_AxeIron.asset", PathItem("Tools/Tool_IronAxe.asset") ?? Item("Iron Axe"), 1, (iron, 3), (plank, 2));
            Repair("Recipes/Recipe_PickIron.asset", PathItem("Tools/Tool_IronPickaxe.asset") ?? Item("Iron Pickaxe"), 1, (iron, 3), (plank, 2));
            Repair("Recipes/Recipe_GrinderTool.asset", PathItem("Tools/Tool_Grinder.asset") ?? Item("Grinder"), 1, (iron, 4), (plank, 1));
            Repair("Recipes/Recipe_LevelingTool.asset", PathItem("Tools/Tool_LevelingTool.asset") ?? Item("Leveling Tool"), 1, (iron, 2), (plank, 4));
            Repair("Recipes/Recipe_CraftingBench.asset", PathItem("Blocks/Block_CraftingBench.asset") ?? Item("Crafting Bench"), 1, (woodLog, 4), (plank, 4));
            Repair("Recipes/Recipe_Furnace.asset", PathItem("Blocks/Block_Furnace.asset") ?? Item("Furnace"), 1, (stone, 8));
            Repair("Recipes/Recipe_Chest.asset", PathItem("Blocks/Block_Chest.asset") ?? Item("Chest"), 1, (plank, 8));
            Repair("Recipes/Recipe_Bed.asset", PathItem("Blocks/Block_Bed.asset") ?? Item("Bed"), 1, (plank, 6), (woodLog, 2));
            Repair("Recipes/Recipe_WaterBucket.asset", PathItem("Items/Tool_WaterBucket.asset") ?? Item("Water Bucket"), 1, (iron, 3));

            Repair("Research/Recipes/Recipe_ScienceT1.asset", science1, 1, (woodLog, 1), (stone, 1));
            Repair("Research/Recipes/Recipe_ScienceT2.asset", science2, 1, (iron, 1), (copper, 1));
            Repair("Research/Recipes/Recipe_ScienceT3.asset", science3, 1, (steel, 1), (copper, 2));
            Repair("Recipes/Recipe_ScienceT1.asset", science1, 1, (woodLog, 1), (stone, 1));
            Repair("Recipes/Recipe_ScienceT2.asset", science2, 1, (iron, 1), (copper, 1));
            Repair("Recipes/Recipe_ScienceT3.asset", science3, 1, (steel, 1), (copper, 2));

            Repair("Nuclear/Recipes/Recipe_ControlRod.asset", PathItem("Nuclear/Items/Item_ControlRod.asset") ?? Item("Control Rod Assembly"), 1, (steel, 5), (cobalt, 3));

            Repair("Maritime/Recipes/Recipe_MUntreatedWood.asset", PathItem("Maritime/Items/MItem_UntreatedWood.asset") ?? Item("Untreated Wood Hull"), 1, (woodLog, 4));
            Repair("Maritime/Recipes/Recipe_MTarPlank.asset", PathItem("Maritime/Items/MItem_TarPlank.asset") ?? Item("Tar-Coated Plank"), 1, (plank, 3), (iron, 1));
            Repair("Maritime/Recipes/Recipe_MBalsaWood.asset", PathItem("Maritime/Items/MItem_BalsaWood.asset") ?? Item("Balsa Wood"), 1, (woodLog, 2));
            Repair("Maritime/Recipes/Recipe_MWaterwheel.asset", PathItem("Maritime/Items/MItem_Waterwheel.asset") ?? Item("Waterwheel"), 1, (iron, 4), (woodLog, 8), (ironGear, 2));
            Repair("Maritime/Recipes/Recipe_MPropSmall.asset", PathItem("Maritime/Items/MItem_PropellerSmall.asset") ?? Item("Small Propeller"), 1, (iron, 3), (copper, 1));
            Repair("Maritime/Recipes/Recipe_MEngineSmall.asset", PathItem("Maritime/Items/MItem_EngineSmall.asset") ?? Item("Crude Engine"), 1, (iron, 6), (ironGear, 4), (copperWire, 4));
            Repair("Maritime/Recipes/Recipe_MHelm.asset", PathItem("Maritime/Items/MItem_Helm.asset") ?? Item("Helm"), 1, (plank, 6), (iron, 2), (ironGear, 2));

            foreach (var family in new[] { "Foundation", "Wall", "Floor", "Doorway", "Door", "Window", "Stairs", "Roof", "Pillar", "HalfWall" })
                Repair($"Recipes/Recipe_Tok_{family}.asset", PathItem($"Tiered/Tokens/Token_{family}.asset") ?? Item($"Token_{family}"), 1, (woodLog, 1));

            return repaired;
        }

        private static int RepairKnownSmeltingRecipes(Dictionary<string, ItemDefinition> byPath, Dictionary<string, ItemDefinition> byName)
        {
            int repaired = 0;
            ItemDefinition Item(string key) => byName.TryGetValue(key, out var item) ? item : null;
            void Repair(string path, ItemDefinition input, ItemDefinition output, int inputCount = 1, int outputCount = 1)
            {
                var recipe = AssetDatabase.LoadAssetAtPath<SmeltingRecipe>($"{Root}/{path}");
                if (recipe == null) return;
                bool changed = false;
                if (recipe.input == null && input != null) { recipe.input = input; recipe.inputCount = Mathf.Max(1, inputCount); changed = true; repaired++; }
                if (recipe.output == null && output != null) { recipe.output = output; recipe.outputCount = Mathf.Max(1, outputCount); changed = true; repaired++; }
                if (changed) EditorUtility.SetDirty(recipe);
            }

            Repair("Recipes/Smelt_Iron.asset", Item("Item_IronOre") ?? Item("Iron Ore"), Item("Item_IronIngot") ?? Item("Iron Ingot"));
            Repair("Recipes/Smelt_Copper.asset", Item("Item_CopperOre") ?? Item("Copper Ore"), Item("Item_CopperIngot") ?? Item("Copper Ingot"));
            Repair("Recipes/Smelt_Steel.asset", Item("Item_IronIngot") ?? Item("Iron Ingot"), Item("Item_SteelIngot") ?? Item("Steel Ingot"), 2, 1);
            Repair("Industrial/Recipes/Smelt_Glass.asset", Item("Item_Sand") ?? Item("Sand"), Item("Item_Glass") ?? Item("Glass"));
            return repaired;
        }

        private static int RepairKnownMachineRecipes(Dictionary<string, ItemDefinition> byPath, Dictionary<string, ItemDefinition> byName)
        {
            int repaired = 0;
            ItemDefinition Item(string key) => byName.TryGetValue(key, out var item) ? item : null;
            void Repair(string path, params (ItemDefinition item, int count)[] inputs)
            {
                var recipe = AssetDatabase.LoadAssetAtPath<MachineRecipe>($"{Root}/{path}");
                if (recipe == null || !NeedsMachineInputRepair(recipe.inputs)) return;
                var valid = inputs.Where(i => i.item != null && i.count > 0)
                    .Select(i => new MachineRecipeSlot { item = i.item, count = i.count })
                    .ToArray();
                if (valid.Length == 0) return;
                recipe.inputs = valid;
                EditorUtility.SetDirty(recipe);
                repaired++;
            }

            Repair("Factory/MachineRecipes/MachineRecipe_CrushIronOre.asset", (Item("Item_IronOre") ?? Item("Iron Ore"), 1));
            Repair("Factory/MachineRecipes/MachineRecipe_CrushCopperOre.asset", (Item("Item_CopperOre") ?? Item("Copper Ore"), 1));
            Repair("Factory/MachineRecipes/MachineRecipe_CrushStone.asset", (Item("Item_Stone") ?? Item("Stone"), 1));
            return repaired;
        }

        private static bool NeedsMachineInputRepair(MachineRecipeSlot[] inputs)
        {
            return inputs == null || inputs.Length == 0 || inputs.Any(input => input.item == null || input.count <= 0);
        }
    }
}
#endif
