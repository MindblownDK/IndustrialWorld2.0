#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Research;
using Object = UnityEngine.Object;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 75 (9.58.0-dev): author the UNIVERSAL MACHINE UPGRADE MODULES.
    ///
    /// Machines that carry universal upgrade slots — the Electric Furnace and the Oil
    /// Refinery — read <see cref="FurnaceUpgradeItem"/> for their Speed / Efficiency
    /// multipliers, but that item class had NO assets in the project: nothing to craft,
    /// nothing in RecipeRegistry, nothing in the research tree. The slots were therefore
    /// impossible to fill, and only the Quarry's own modules (Range / Speed / Efficiency)
    /// and the maritime engine modules existed — hence the report "the Oil Refinery shows
    /// only quarry upgrades and engine modules, no universal upgrades".
    ///
    /// Non-destructive by construction: an existing module keeps every value the player
    /// tuned (multipliers, stack size, mass, icon, description), an existing recipe keeps
    /// its quantities, and only MISSING content is created. Lost links (a null icon, a
    /// recipe that lost its output item) are reconnected. Nothing is ever deleted.
    /// </summary>
    public static class UniversalUpgradeSetup
    {
        private const string Root        = "Assets/VoxelEngineAssets";
        private const string Folder      = Root + "/Factory/Upgrades";
        private const string IconFolder  = Folder + "/Icons";
        private const string NodePath    = Root + "/Research/Nodes/res_adv_manufacturing.asset";
        private const string CatalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        public static void RunStep75()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorUtility.DisplayDialog("Universal Upgrade Modules", "Exit Play Mode before running setup.", "OK"); return; }
            try
            {
                // ── Mandatory dependencies are resolved before anything is written, so a
                //    missing ingredient stops the step instead of producing a module the
                //    player could never craft.
                var steel   = RequireItem("Item_SteelPlate");
                var circuit = RequireItem("Item_Circuit");
                var gear    = RequireItem("Item_IronGear");
                var wire    = RequireItem("Item_CopperWire");
                var registry = Require<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                var tree     = Require<ResearchTree>(Root + "/Research/ResearchTree.asset");
                var node     = Require<ResearchNode>(NodePath);
                var catalog  = LoadChecked<ItemPersistenceCatalog>(CatalogPath);

                EnsureFolder(IconFolder);

                var speed = Resolve("Machine Speed Module", "upgrade_machine_speed", "Upgrade_MachineSpeed",
                    "A universal machine module. Fits any machine with upgrade slots — the Electric Furnace and the Oil Refinery. Multiplies throughput by x1.25 per module, stacking with every module in the machine.",
                    speedMultiplier: 1.25f, efficiencyMultiplier: 1f,
                    tint: new Color(0.88f, 0.62f, 0.16f),
                    bolt: false);

                var efficiency = Resolve("Machine Efficiency Module", "upgrade_machine_efficiency", "Upgrade_MachineEfficiency",
                    "A universal machine module. Fits any machine with upgrade slots — the Electric Furnace and the Oil Refinery. Draws 20% less power per module (x0.8), stacking with every module in the machine.",
                    speedMultiplier: 1f, efficiencyMultiplier: 0.8f,
                    tint: new Color(0.30f, 0.78f, 0.52f),
                    bolt: true);

                // Assembler-tier costs, in the same shape as the Quarry module recipes.
                Author(speed, new[]
                {
                    Ingredient(steel, 6), Ingredient(circuit, 4), Ingredient(gear, 3), Ingredient(wire, 6)
                });
                Author(efficiency, new[]
                {
                    Ingredient(steel, 8), Ingredient(circuit, 6), Ingredient(gear, 4), Ingredient(wire, 8)
                });

                // ── Research: Advanced Manufacturing unlocks both modules ──
                var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? new RecipeDefinition[0]);
                if (!unlocks.Contains(speed.Recipe))      unlocks.Add(speed.Recipe);
                if (!unlocks.Contains(efficiency.Recipe)) unlocks.Add(efficiency.Recipe);
                node.unlocksRecipes = unlocks.ToArray();
                if (tree.nodes == null) tree.nodes = new List<ResearchNode>();
                if (!tree.nodes.Contains(node)) tree.nodes.Add(node);

                // ── Registry + save catalogue ──
                if (registry.recipes == null) registry.recipes = new List<RecipeDefinition>();
                if (!registry.recipes.Contains(speed.Recipe))      registry.recipes.Add(speed.Recipe);
                if (!registry.recipes.Contains(efficiency.Recipe)) registry.recipes.Add(efficiency.Recipe);
                if (catalog == null)
                {
                    EnsureFolder("Assets/Resources/VoxelEngine");
                    catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }
                if (catalog.items == null) catalog.items = new List<ItemDefinition>();
                if (!catalog.items.Contains(speed.Item))      catalog.items.Add(speed.Item);
                if (!catalog.items.Contains(efficiency.Item)) catalog.items.Add(efficiency.Item);

                foreach (var asset in new Object[] { speed.Item, speed.Recipe, efficiency.Item, efficiency.Recipe, node, tree, registry, catalog })
                    EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[UniversalUpgradeSetup] Step 75 complete: universal Machine Speed / Efficiency Modules, recipes, icons, research unlock and save-catalog links verified.");
                EditorUtility.DisplayDialog("Step 75 — Universal Machine Upgrade Modules",
                    "Machine Speed Module and Machine Efficiency Module are ready.\n\n"
                    + "Where they go: the upgrade slots of the Electric Furnace and the Oil Refinery (the refinery panel now says so).\n"
                    + "How to get them: craft at an Assembler once Advanced Manufacturing is researched.\n"
                    + "What they do: x1.25 speed per Speed Module, x0.8 power draw per Efficiency Module — two slots per machine, so they stack.\n\n"
                    + "Existing multipliers, costs, icons and research settings were preserved. Nothing was deleted.", "OK");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("Universal upgrade module setup stopped", error.Message
                    + "\n\nResolve the reported prerequisite through Voxel Engine Setup, or share the error with Thomas. "
                    + "This step does not delete existing content. Missing content created before an error is retained for a safe rerun.", "OK");
            }
        }

        private sealed class Module
        {
            public string Label, Id, ItemPath, RecipePath, Description;
            public FurnaceUpgradeItem Item;
            public RecipeDefinition Recipe;
            public float SpeedMultiplier, EfficiencyMultiplier;
            public Color Tint;
            public bool Bolt;
        }

        private static Module Resolve(string label, string id, string assetStem, string description,
            float speedMultiplier, float efficiencyMultiplier, Color tint, bool bolt)
        {
            var module = new Module
            {
                Label = label, Id = id,
                ItemPath = Folder + "/" + assetStem + ".asset",
                RecipePath = Folder + "/Recipe_" + assetStem + ".asset",
                Description = description,
                SpeedMultiplier = speedMultiplier, EfficiencyMultiplier = efficiencyMultiplier,
                Tint = tint, Bolt = bolt
            };

            // An identity that was moved or renamed is followed by itemId, never duplicated:
            // two assets sharing a save key would restore as the wrong module.
            module.Item = LoadChecked<FurnaceUpgradeItem>(module.ItemPath);
            foreach (string guid in AssetDatabase.FindAssets("t:FurnaceUpgradeItem"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<FurnaceUpgradeItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate == null || candidate.itemId != id) continue;
                if (module.Item != null && module.Item != candidate)
                    throw new InvalidOperationException("Duplicate universal module identity: " + id);
                module.Item = candidate;
                module.ItemPath = AssetDatabase.GetAssetPath(candidate);
            }

            module.Recipe = LoadChecked<RecipeDefinition>(module.RecipePath);
            if (module.Recipe == null && module.Item != null)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
                {
                    var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (recipe == null || recipe.outputItem != module.Item) continue;
                    if (module.Recipe != null)
                        throw new InvalidOperationException("Several existing recipes make " + label + "; cannot choose one without overwriting intent.");
                    module.Recipe = recipe;
                    module.RecipePath = AssetDatabase.GetAssetPath(recipe);
                }
            }
            return module;
        }

        private static void Author(Module module, RecipeIngredient[] ingredients)
        {
            bool newItem = module.Item == null;
            if (newItem)
            {
                module.Item = ScriptableObject.CreateInstance<FurnaceUpgradeItem>();
                module.Item.itemId = module.Id;
                module.Item.displayName = module.Label;
                module.Item.description = module.Description;
                module.Item.maxStack = 1;
                module.Item.massPerUnit = 2f;
                module.Item.category = "Upgrades";
                module.Item.speedMultiplier = module.SpeedMultiplier;
                module.Item.efficiencyMultiplier = module.EfficiencyMultiplier;
                module.Item.iconTint = module.Tint;
                AssetDatabase.CreateAsset(module.Item, module.ItemPath);
            }
            // Keep the tuned identity, reconnect what is missing.
            if (string.IsNullOrWhiteSpace(module.Item.itemId)) module.Item.itemId = module.Id;
            if (string.IsNullOrWhiteSpace(module.Item.displayName)) module.Item.displayName = module.Label;
            if (string.IsNullOrWhiteSpace(module.Item.description)) module.Item.description = module.Description;
            if (module.Item.icon == null) module.Item.icon = EnsureIcon(module.Label, module.Tint, module.Bolt);

            bool newRecipe = module.Recipe == null;
            if (newRecipe)
            {
                module.Recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                module.Recipe.displayName = module.Label;
                module.Recipe.requiredStation = StationTier.Assembler;
                module.Recipe.craftSeconds = 0f;
                module.Recipe.outputCount = 1;
                module.Recipe.unlockedByDefault = false;
                AssetDatabase.CreateAsset(module.Recipe, module.RecipePath);
            }
            if (string.IsNullOrWhiteSpace(module.Recipe.displayName)) module.Recipe.displayName = module.Label;
            module.Recipe.outputItem = module.Item;
            if (module.Recipe.outputCount <= 0) module.Recipe.outputCount = 1;
            if (module.Recipe.inputs == null || module.Recipe.inputs.Length == 0) module.Recipe.inputs = ingredients;
            else if (module.Recipe.inputs.Length == ingredients.Length)
            {
                for (int i = 0; i < ingredients.Length; i++)
                {
                    if (module.Recipe.inputs[i].item != null) continue;   // existing quantity and item stay untouched
                    var repaired = module.Recipe.inputs[i];
                    repaired.item = ingredients[i].item;
                    module.Recipe.inputs[i] = repaired;
                }
            }
            EditorUtility.SetDirty(module.Item);
            EditorUtility.SetDirty(module.Recipe);
        }

        /// <summary>
        /// Procedural module icon: a slate card with either a stack of chevrons (speed) or a
        /// bolt (power). Generated so the step does not depend on any icon asset existing,
        /// and skipped entirely once the player has put their own icon on the module.
        /// </summary>
        private static Sprite EnsureIcon(string label, Color tint, bool bolt)
        {
            string name = label.Replace(" ", string.Empty) + ".png";
            string path = IconFolder + "/" + name;
            if (!File.Exists(path))
            {
                const int S = 64;
                var texture = new Texture2D(S, S, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color[S * S];
                    var dark = new Color(0.15f, 0.17f, 0.21f, 1f);
                    var edge = new Color(tint.r, tint.g, tint.b, 0.85f);
                    for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        Color c = Color.clear;
                        if (InRoundedRect(x, y, 9, 7, 55, 57, 9))
                        {
                            bool border = !InRoundedRect(x, y, 12, 10, 52, 54, 7);
                            c = border ? edge : dark;
                        }
                        if (!bolt)
                        {
                            // Three chevrons sweeping right, drawn over the card face.
                            for (int k = -1; k <= 1; k++)
                            {
                                float cx = 32f + k * 11f;
                                bool arm = DistanceToSegment(x, y, cx - 8f, 20f, cx, 32f) < 2.4f
                                        || DistanceToSegment(x, y, cx, 32f, cx - 8f, 44f) < 2.4f;
                                if (arm) c = tint;
                            }
                        }
                        else if (InPolygon(x, y, BoltPolygon))
                        {
                            c = tint;
                        }
                        pixels[y * S + x] = c;
                    }
                    texture.SetPixels(pixels);
                    texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(texture); }
                AssetDatabase.ImportAsset(path);
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Could not import module icon: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 64;
            importer.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Module icon has no sprite after import: " + path);
            return sprite;
        }

        private static readonly Vector2[] BoltPolygon =
        {
            new Vector2(33f, 52f), new Vector2(45f, 52f), new Vector2(38f, 36f),
            new Vector2(47f, 36f), new Vector2(25f, 12f), new Vector2(32f, 30f), new Vector2(22f, 30f)
        };

        private static bool InRoundedRect(int x, int y, int x0, int y0, int x1, int y1, int radius)
        {
            if (x < x0 || x > x1 || y < y0 || y > y1) return false;
            int cx = x < x0 + radius ? x0 + radius : (x > x1 - radius ? x1 - radius : x);
            int cy = y < y0 + radius ? y0 + radius : (y > y1 - radius ? y1 - radius : y);
            int dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= radius * radius + radius;
        }

        private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float lengthSq = vx * vx + vy * vy;
            float t = lengthSq <= 0.0001f ? 0f : Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / lengthSq);
            float dx = px - (ax + t * vx), dy = py - (ay + t * vy);
            return Mathf.Sqrt(dx * dx + dy * dy);
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

        private static RecipeIngredient Ingredient(ItemDefinition item, int count) => new RecipeIngredient { item = item, count = count };

        private static T Require<T>(string path) where T : Object => LoadChecked<T>(path)
            ?? throw new InvalidOperationException("Missing foundational asset: " + path + ". Run the corresponding existing setup step first.");

        private static T LoadChecked<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null && !(asset is T)) throw new InvalidOperationException("Incompatible asset at " + path + "; nothing will replace it.");
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
