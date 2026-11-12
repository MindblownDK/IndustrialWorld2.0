// Assets/Scripts/VoxelEngine/Editor/Crafting/MachineModuleSetup.cs
//
// 18.15.0-dev - STEP 127: author the universal machine MODULE ROLES and the
// Machine Yield Module.
//
// Step 75 authored two universal modules (Speed, Efficiency) as FurnaceUpgradeItem
// assets, but that class had no notion of what a module DOES. Every machine read both
// multipliers off every module, so the roles only existed in the two display names.
// 18.15.0-dev adds a real role field, a role filter on the slots, and a third role -
// bonus batch yield - which needs its own item, recipe, icon and research unlock.
//
// What this step does:
//   * repairs the role on the two modules step 75 authored, matched by itemId, and
//     leaves every tuned multiplier alone;
//   * creates the Machine Yield Module (item, recipe, icon) if it is missing, and
//     repairs lost links on an existing one without touching its numbers or costs;
//   * appends all three recipes to the Advanced Manufacturing node and to the recipe
//     registry, and puts all three items in the save catalogue.
//
// What it never does:
//   * delete, rename, re-class or re-cost anything;
//   * overwrite an existing role, multiplier, stack size, mass, icon or description;
//   * touch research costs, tiers or prerequisites.
//
// Editor-only. Run: Tools > Voxel Engine > Setup Wizard.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.EditorTools;
using VoxelEngine.Items;
using VoxelEngine.Research;
using Object = UnityEngine.Object;

namespace IndustrialWorld.EditorTools
{
    public static class MachineModuleSetup
    {
        private const string Root        = "Assets/VoxelEngineAssets";
        private const string Folder      = Root + "/Factory/Upgrades";
        private const string IconFolder  = Folder + "/Icons";
        private const string NodePath    = Root + "/Research/Nodes/res_adv_manufacturing.asset";
        private const string CatalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        private const string SpeedId      = "upgrade_machine_speed";
        private const string EfficiencyId = "upgrade_machine_efficiency";
        private const string YieldId      = "upgrade_machine_yield";

        public static void RunStep127()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Machine Module Roles", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var steel    = RequireItem("Item_SteelPlate");
                var circuit  = RequireItem("Item_Circuit");
                var gear     = RequireItem("Item_IronGear");
                var wire     = RequireItem("Item_CopperWire");
                var registry = Require<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                var node     = LoadChecked<ResearchNode>(NodePath);
                var catalog  = LoadChecked<ItemPersistenceCatalog>(CatalogPath);

                EnsureFolder(IconFolder);

                // ── 1. Repair the roles of the modules step 75 authored ────────────
                var repaired = new List<string>();
                foreach (var module in AllUniversalModules())
                {
                    MachineModuleKind? wanted = module.itemId switch
                    {
                        SpeedId => MachineModuleKind.Speed,
                        EfficiencyId => MachineModuleKind.Efficiency,
                        YieldId => MachineModuleKind.Yield,
                        _ => null,
                    };
                    if (wanted == null || module.moduleKind == wanted.Value) continue;
                    Debug.Log($"[MachineModuleSetup] Step 127 - role on '{module.itemId}' was {module.moduleKind}; set to {wanted.Value}.");
                    module.moduleKind = wanted.Value;
                    EditorUtility.SetDirty(module);
                    repaired.Add(module.itemId);
                }

                // ── 2. The Yield module ───────────────────────────────────────────
                var yieldModule = ResolveYieldModule();
                bool newItem = yieldModule.Item == null;

                if (newItem)
                {
                    yieldModule.Item = ScriptableObject.CreateInstance<FurnaceUpgradeItem>();
                    yieldModule.Item.itemId = YieldId;
                    yieldModule.Item.displayName = "Machine Yield Module";
                    yieldModule.Item.description =
                        "A universal machine module. Fits any machine whose upgrade slots finish recipe batches - the "
                        + "Crusher, Assembler, Electric Furnace, Metallurgical Foundry, Ore Washer, Oil Refinery, Chemical "
                        + "Plant, Distillation Plant and Catalytic Cracker. Every finished batch has a 12% chance per "
                        + "module of yielding one extra unit of the recipe's main product. Machines that grow or press on "
                        + "their own cycle (the Automatic Tree Farm and the Biomass Press) accept speed and efficiency "
                        + "modules only, and refuse this one at the slot.";
                    yieldModule.Item.maxStack = 1;
                    yieldModule.Item.massPerUnit = 2f;
                    yieldModule.Item.category = "Upgrades";
                    yieldModule.Item.speedMultiplier = 1f;
                    yieldModule.Item.efficiencyMultiplier = 1f;
                    yieldModule.Item.moduleKind = MachineModuleKind.Yield;
                    yieldModule.Item.yieldBonusChance = 0.12f;
                    yieldModule.Item.iconTint = new Color(0.36f, 0.80f, 0.44f);
                    AssetDatabase.CreateAsset(yieldModule.Item, yieldModule.ItemPath);
                }

                // Repair lost links only. A tuned role, chance, icon or description stays.
                if (string.IsNullOrWhiteSpace(yieldModule.Item.itemId)) yieldModule.Item.itemId = YieldId;
                if (string.IsNullOrWhiteSpace(yieldModule.Item.displayName)) yieldModule.Item.displayName = "Machine Yield Module";
                if (string.IsNullOrWhiteSpace(yieldModule.Item.description))
                    yieldModule.Item.description = "A universal machine module. Gives every finished machine batch a chance at one extra unit of the main product.";
                if (yieldModule.Item.moduleKind != MachineModuleKind.Yield) yieldModule.Item.moduleKind = MachineModuleKind.Yield;
                if (yieldModule.Item.yieldBonusChance <= 0f) yieldModule.Item.yieldBonusChance = 0.12f;
                if (yieldModule.Item.maxStack < 1) yieldModule.Item.maxStack = 1;
                if (yieldModule.Item.massPerUnit <= 0f) yieldModule.Item.massPerUnit = 2f;
                if (yieldModule.Item.icon == null)
                    yieldModule.Item.icon = EnsureIcon("MachineYieldModule", new Color(0.36f, 0.80f, 0.44f), new Color(0.12f, 0.35f, 0.20f));

                // ── 3. Recipe ─────────────────────────────────────────────────────
                bool newRecipe = yieldModule.Recipe == null;
                if (newRecipe)
                {
                    yieldModule.Recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                    yieldModule.Recipe.displayName = "Machine Yield Module";
                    yieldModule.Recipe.requiredStation = StationTier.Assembler;
                    yieldModule.Recipe.outputCount = 1;
                    // Gated exactly like the other two modules: nothing in this project is free.
                    yieldModule.Recipe.unlockedByDefault = false;
                    AssetDatabase.CreateAsset(yieldModule.Recipe, yieldModule.RecipePath);
                }
                if (string.IsNullOrWhiteSpace(yieldModule.Recipe.displayName))
                    yieldModule.Recipe.displayName = "Machine Yield Module";
                yieldModule.Recipe.outputItem = yieldModule.Item;
                if (yieldModule.Recipe.outputCount <= 0) yieldModule.Recipe.outputCount = 1;
                if (yieldModule.Recipe.inputs == null || yieldModule.Recipe.inputs.Length == 0)
                {
                    // Costs sit just above the Speed module: extra product is the strongest of
                    // the three roles, so it is the most expensive to build.
                    yieldModule.Recipe.inputs = new[]
                    {
                        Ingredient(steel, 7), Ingredient(circuit, 5), Ingredient(gear, 3), Ingredient(wire, 7),
                    };
                }
                else if (yieldModule.Recipe.inputs.Length == 4)
                {
                    // Repair a broken ingredient link without touching a stocked quantity.
                    var repair = new[] { steel, circuit, gear, wire };
                    for (int i = 0; i < yieldModule.Recipe.inputs.Length; i++)
                    {
                        if (yieldModule.Recipe.inputs[i].item != null) continue;
                        var entry = yieldModule.Recipe.inputs[i];
                        entry.item = repair[i];
                        yieldModule.Recipe.inputs[i] = entry;
                        Debug.Log($"[MachineModuleSetup] Step 127 - repaired recipe ingredient {i} on the Machine Yield Module.");
                    }
                }
                if (yieldModule.Recipe.craftSeconds <= 0f && yieldModule.Recipe.requiredStation != StationTier.None)
                    yieldModule.Recipe.craftSeconds = CraftTimeDefaults.Suggest(yieldModule.Recipe);

                EditorUtility.SetDirty(yieldModule.Item);
                EditorUtility.SetDirty(yieldModule.Recipe);

                // ── 4. Research, registry and save catalogue (append-only) ────────
                int unlocked = 0;
                if (node != null)
                {
                    foreach (var module in AllUniversalModules())
                    {
                        var recipe = FindRecipeFor(module);
                        if (recipe != null && ResearchAuthoring.AddRecipeUnlock(node, recipe)) unlocked++;
                    }
                }
                else
                {
                    Debug.LogWarning($"[MachineModuleSetup] Step 127 - {NodePath} is missing; the modules stay unauthored in research. "
                                   + "Run the universal-upgrade step (75) first, then this one.");
                }

                int registered = 0;
                if (registry != null)
                {
                    registry.recipes ??= new List<RecipeDefinition>();
                    foreach (var module in AllUniversalModules())
                    {
                        var recipe = FindRecipeFor(module);
                        if (recipe == null || registry.recipes.Contains(recipe)) continue;
                        registry.recipes.Add(recipe);
                        registered++;
                    }
                    EditorUtility.SetDirty(registry);
                }

                int catalogued = 0;
                if (catalog == null)
                {
                    EnsureFolder("Assets/Resources/VoxelEngine");
                    catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }
                catalog.items ??= new List<ItemDefinition>();
                foreach (var module in AllUniversalModules())
                {
                    if (module == null || catalog.items.Contains(module)) continue;
                    catalog.items.Add(module);
                    catalogued++;
                }
                EditorUtility.SetDirty(catalog);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string report = $"3 universal modules verified ({AllUniversalModules().Count} on disk), "
                              + $"{repaired.Count} role(s) repaired, {unlocked} research unlock(s) appended, "
                              + $"{registered} registry link(s) added, {catalogued} save-catalogue entry/entries added.";
                Debug.Log("[MachineModuleSetup] Step 127 complete: " + report);

                EditorUtility.DisplayDialog("Step 127 - Universal Machine Modules",
                    "Machine Speed, Efficiency and Yield modules are ready.\n\n"
                    + report + "\n\n"
                    + "Where they go: the Modules slots on the Crusher, Assembler, Electric Furnace, Metallurgical Foundry, "
                    + "Ore Washer, Oil Refinery, Chemical Plant, Distillation Plant and Catalytic Cracker (all three roles), "
                    + "and the Automatic Tree Farm and Biomass Press (speed and efficiency only).\n\n"
                    + "What they do: x1.25 throughput, x0.8 power draw, and +12% chance of one bonus unit per finished batch.\n\n"
                    + "Nothing was deleted and no existing number was overwritten.", "OK");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("Machine module setup stopped", error.Message
                    + "\n\nResolve the reported prerequisite through Voxel Engine Setup. This step never deletes existing "
                    + "content; anything it created before stopping is kept for a safe rerun.", "OK");
            }
        }

        // ── Discovery ──────────────────────────────────────────────────────────

        /// <summary>
        /// Every universal module in the project, matched by ROLE CLASS rather than by asset
        /// path, so a module the player moved or renamed is still found instead of duplicated.
        /// Sorted by itemId for a report that reads the same on every run.
        /// </summary>
        private static List<FurnaceUpgradeItem> AllUniversalModules()
        {
            var found = new List<FurnaceUpgradeItem>();
            foreach (string guid in AssetDatabase.FindAssets("t:FurnaceUpgradeItem"))
            {
                var module = AssetDatabase.LoadAssetAtPath<FurnaceUpgradeItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (module != null) found.Add(module);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
            return found;
        }

        private static RecipeDefinition FindRecipeFor(FurnaceUpgradeItem module)
        {
            if (module == null) return null;
            string expected = AssetDatabase.GetAssetPath(module);
            string folder = Path.GetDirectoryName(expected)?.Replace('\\', '/');
            string stem = Path.GetFileNameWithoutExtension(expected);
            var direct = AssetDatabase.LoadAssetAtPath<RecipeDefinition>($"{folder}/Recipe_{stem}.asset");
            if (direct != null && direct.outputItem == module) return direct;

            RecipeDefinition found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (recipe == null || recipe.outputItem != module) continue;
                if (found != null) return found;   // ambiguous: keep the first, never guess twice
                found = recipe;
            }
            return found;
        }

        private sealed class YieldModulePaths
        {
            public string ItemPath, RecipePath;
            public FurnaceUpgradeItem Item;
            public RecipeDefinition Recipe;
        }

        private static YieldModulePaths ResolveYieldModule()
        {
            var paths = new YieldModulePaths
            {
                ItemPath = Folder + "/Upgrade_MachineYield.asset",
                RecipePath = Folder + "/Recipe_Upgrade_MachineYield.asset",
            };

            paths.Item = LoadChecked<FurnaceUpgradeItem>(paths.ItemPath);
            foreach (var candidate in AllUniversalModules())
            {
                if (candidate.itemId != YieldId) continue;
                if (paths.Item != null && paths.Item != candidate)
                    throw new InvalidOperationException("Two assets claim the itemId '" + YieldId + "'; resolve the duplicate before running this step.");
                paths.Item = candidate;
                paths.ItemPath = AssetDatabase.GetAssetPath(candidate);
            }

            if (paths.Item != null) paths.Recipe = FindRecipeFor(paths.Item);
            if (paths.Recipe == null) paths.Recipe = LoadChecked<RecipeDefinition>(paths.RecipePath);
            if (paths.Recipe != null) paths.RecipePath = AssetDatabase.GetAssetPath(paths.Recipe);
            return paths;
        }

        // ── Icon ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Procedural icon: a slate card with a rising three-facet crystal, tinted green to
        /// read apart from the gold chevrons (speed) and the green bolt (efficiency). Written
        /// once and then left alone, so the player can replace it with real art at any tune
        /// pass - an existing PNG is never overwritten. The importer meta is written as a
        /// single 64 px sprite before the asset is used, so no manual Inspector pass is needed.
        /// </summary>
        private static Sprite EnsureIcon(string fileName, Color tint, Color card)
        {
            string path = IconFolder + "/" + fileName + ".png";
            if (!File.Exists(path))
            {
                const int S = 64;
                var texture = new Texture2D(S, S, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color[S * S];
                    var edge = new Color(tint.r, tint.g, tint.b, 0.9f);
                    for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        Color c = Color.clear;
                        if (InRoundedRect(x, y, 9, 7, 55, 57, 9))
                        {
                            bool border = !InRoundedRect(x, y, 12, 10, 52, 54, 7);
                            c = border ? edge : card;
                        }
                        if (InPolygon(x, y, CrystalPolygon))
                            c = y > 30 ? tint : new Color(tint.r * 0.72f, tint.g * 0.72f, tint.b * 0.72f, 1f);
                        pixels[y * S + x] = c;
                    }
                    texture.SetPixels(pixels);
                    texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(texture); }
                AssetDatabase.ImportAsset(path);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }
            if (importer != null
                && (importer.textureType != TextureImporterType.Sprite
                 || importer.spriteImportMode != SpriteImportMode.Single
                 || !importer.alphaIsTransparency))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.spritePixelsPerUnit = 64;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Module icon could not be imported as a sprite: " + path);
            return sprite;
        }

        /// <summary>A tall hexagonal crystal: narrow foot, two shoulders, a point.</summary>
        private static readonly Vector2[] CrystalPolygon =
        {
            new Vector2(32f, 54f), new Vector2(43f, 40f), new Vector2(43f, 22f),
            new Vector2(32f, 15f), new Vector2(21f, 22f), new Vector2(21f, 40f),
        };

        private static bool InRoundedRect(int x, int y, int x0, int y0, int x1, int y1, int radius)
        {
            if (x < x0 || x > x1 || y < y0 || y > y1) return false;
            int cx = x < x0 + radius ? x0 + radius : (x > x1 - radius ? x1 - radius : x);
            int cy = y < y0 + radius ? y0 + radius : (y > y1 - radius ? y1 - radius : y);
            int dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= radius * radius + radius;
        }

        private static bool InPolygon(float x, float y, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > y) == (polygon[j].y > y)) continue;
                float crossX = (polygon[j].x - polygon[i].x) * (y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x;
                if (x < crossX) inside = !inside;
            }
            return inside;
        }

        // ── Asset plumbing ─────────────────────────────────────────────────────

        private static RecipeIngredient Ingredient(ItemDefinition item, int count) => new RecipeIngredient { item = item, count = count };

        private static T Require<T>(string path) where T : Object => LoadChecked<T>(path)
            ?? throw new InvalidOperationException("Missing foundational asset: " + path + ". Run the corresponding existing setup step first.");

        private static T LoadChecked<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null && asset is not T)
                throw new InvalidOperationException("Incompatible asset at " + path + "; nothing will replace it.");
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static ItemDefinition RequireItem(string name)
        {
            ItemDefinition found = null;
            foreach (string guid in AssetDatabase.FindAssets(name + " t:ItemDefinition", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != name) continue;
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null) continue;
                if (found != null && found != item) throw new InvalidOperationException("Ambiguous ingredient assets: " + name);
                found = item;
            }
            if (found == null) throw new InvalidOperationException("Missing ingredient: " + name + ". No partial recipe will be created.");
            return found;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
