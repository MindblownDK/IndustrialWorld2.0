#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using IndustrialWorld.Navigation;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;
using Object = UnityEngine.Object;

namespace IndustrialWorld.EditorTools
{
    /// <summary>Step 74: create missing pilot content; preserve existing identities, models and tuning.</summary>
    public static class AutoRunPilotSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string Folder = Root + "/GridSystem/AutoRunPilot";
        private const string NodePath = Root + "/Research/Nodes/res_auto_run_piloting.asset";
        private const string CatalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";

        public static void RunStep74()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorUtility.DisplayDialog("Auto-Run Pilot", "Exit Play Mode before running setup.", "OK"); return; }
            try
            {
                // Resolve mandatory dependencies before writing anything. Never silently omit costs.
                var iron = RequireItem("Item_IronPlate");
                var steel = RequireItem("Item_SteelPlate");
                var circuit = RequireItem("Item_Circuit");
                var glass = RequireItem("Item_Glass");
                var science1 = RequireItem("Item_ScienceT1") as ScienceItem;
                var science2 = RequireItem("Item_ScienceT2") as ScienceItem;
                var registry = Require<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                var tree = Require<ResearchTree>(Root + "/Research/ResearchTree.asset");
                var prerequisite = Require<ResearchNode>(Root + "/Research/Nodes/res_grid_utilities.asset");
                if (science1 == null || science2 == null) throw new InvalidOperationException("Science T1/T2 must be ScienceItem assets.");
                var large = ResolveSlot("Large", GridSize.Large, "gitem_auto_run_pilot_large");
                var small = ResolveSlot("Small", GridSize.Small, "gitem_auto_run_pilot_small");
                var node = LoadChecked<ResearchNode>(NodePath);
                foreach (string guid in AssetDatabase.FindAssets("t:ResearchNode"))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<ResearchNode>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate == null || candidate.nodeId != "res_auto_run_piloting") continue;
                    if (node != null && node != candidate) throw new InvalidOperationException("Duplicate Auto-Run Piloting research identity.");
                    node = candidate;
                }
                ValidateRecipe(large.Recipe, 4);
                ValidateRecipe(small.Recipe, 3);
                var catalog = LoadChecked<ItemPersistenceCatalog>(CatalogPath);
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("No supported material shader found.");

                EnsureFolder(Folder + "/Materials");
                EnsureFolder(Folder + "/Icons");
                var casing = EnsureMaterial("Casing", shader, new Color(0.08f, 0.11f, 0.14f), false);
                var trim = EnsureMaterial("Trim", shader, new Color(0.34f, 0.4f, 0.46f), false);
                var screen = EnsureMaterial("Screen", shader, new Color(0.025f, 0.09f, 0.12f), false);
                var cyan = EnsureMaterial("Cyan", shader, new Color(0.15f, 0.85f, 0.93f), true);
                var green = EnsureMaterial("Green", shader, new Color(0.32f, 0.85f, 0.5f), true);
                Sprite icon = EnsureIcon();
                Author(large, new[] { Ingredient(steel, 4), Ingredient(iron, 2), Ingredient(circuit, 3), Ingredient(glass, 1) },
                    90f, 350f, 40f, 10f, icon, casing, trim, screen, cyan, green);
                Author(small, new[] { Ingredient(iron, 2), Ingredient(circuit, 2), Ingredient(glass, 1) },
                    20f, 120f, 15f, 6f, icon, casing, trim, screen, cyan, green);

                if (node == null)
                {
                    EnsureFolder(Path.GetDirectoryName(NodePath).Replace('\\', '/'));
                    node = ScriptableObject.CreateInstance<ResearchNode>();
                    node.nodeId = "res_auto_run_piloting";
                    node.displayName = "Auto-Run Piloting";
                    node.description = "Dedicated control blocks for unattended one-way road vehicles. Includes saved parking and explicit restart controls.";
                    node.category = ResearchCategory.Environment;
                    node.subCategory = ResearchSubCategory.Logistics;
                    node.tier = 4;
                    node.column = 8;
                    node.researchSeconds = 60f;
                    node.prerequisites = new[] { prerequisite };
                    node.cost = new[] { new ResearchNode.ScienceCost { pack = science1, count = 20 },
                        new ResearchNode.ScienceCost { pack = science2, count = 10 } };
                    AssetDatabase.CreateAsset(node, NodePath);
                }
                var unlocks = new List<RecipeDefinition>(node.unlocksRecipes ?? new RecipeDefinition[0]);
                if (!unlocks.Contains(large.Recipe)) unlocks.Add(large.Recipe);
                if (!unlocks.Contains(small.Recipe)) unlocks.Add(small.Recipe);
                node.unlocksRecipes = unlocks.ToArray();
                if (tree.nodes == null) tree.nodes = new List<ResearchNode>();
                if (!tree.nodes.Contains(prerequisite)) tree.nodes.Add(prerequisite);
                if (!tree.nodes.Contains(node)) tree.nodes.Add(node);
                if (registry.recipes == null) registry.recipes = new List<RecipeDefinition>();
                if (!registry.recipes.Contains(large.Recipe)) registry.recipes.Add(large.Recipe);
                if (!registry.recipes.Contains(small.Recipe)) registry.recipes.Add(small.Recipe);
                if (catalog == null)
                {
                    EnsureFolder("Assets/Resources/VoxelEngine");
                    catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }
                if (catalog.items == null) catalog.items = new List<ItemDefinition>();
                if (!catalog.items.Contains(large.Item)) catalog.items.Add(large.Item);
                if (!catalog.items.Contains(small.Item)) catalog.items.Add(small.Item);
                foreach (var asset in new Object[] { node, tree, registry, catalog }) EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[AutoRunPilotSetup] Step 74 complete: Large/Small pilot prefabs, items, icons, recipes, research and persistence links verified.");
                EditorUtility.DisplayDialog("Step 74 — Auto-Run Pilot", "Auto-Run Pilot (Large) and Auto-Run Pilot (Small) are ready.\n\n"
                    + "Research Auto-Run Piloting under Logistics, then search crafting for Auto-Run Pilot near an Assembler station.\n"
                    + "Place one on your vehicle and right-click it. Plan the road route, then confirm START WHEEL RUN.\n\n"
                    + "No separate Route Recorder required. Existing tuning, custom visuals, recipe quantities and research costs are preserved.\n"
                    + "Do not rerun Step 65 for these blocks.", "OK");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("Auto-Run Pilot setup stopped", error.Message
                    + "\n\nResolve the reported prerequisite through Voxel Engine Setup, or share the error with Thomas. "
                    + "This step does not delete existing content. Missing content created before an error is retained for a safe rerun.", "OK");
            }
        }

        private sealed class Slot
        {
            public string Label, Id, PrefabPath, ItemPath, RecipePath;
            public GridSize Size;
            public GridBlockItem Item;
            public RecipeDefinition Recipe;
        }

        private static Slot ResolveSlot(string label, GridSize size, string id)
        {
            var slot = new Slot { Label = label, Size = size, Id = id,
                PrefabPath = Folder + "/AutoRunPilot_" + label + ".prefab",
                ItemPath = Folder + "/GItem_AutoRunPilot_" + label + ".asset",
                RecipePath = Folder + "/Recipe_AutoRunPilot_" + label + ".asset" };
            slot.Item = LoadChecked<GridBlockItem>(slot.ItemPath);
            // Preserve moved item identities instead of generating another item with the same save key.
            foreach (string guid in AssetDatabase.FindAssets("t:GridBlockItem"))
            {
                var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.itemId != id) continue;
                if (slot.Item != null && slot.Item != item) throw new InvalidOperationException("Duplicate pilot item identity: " + id);
                slot.Item = item;
                slot.ItemPath = AssetDatabase.GetAssetPath(item);
            }
            slot.Recipe = LoadChecked<RecipeDefinition>(slot.RecipePath);
            if (slot.Recipe == null && slot.Item != null)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition"))
                {
                    var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (recipe == null || recipe.outputItem != slot.Item) continue;
                    if (slot.Recipe != null) throw new InvalidOperationException("Multiple existing pilot recipes; cannot choose one without overwriting intent.");
                    slot.Recipe = recipe;
                    slot.RecipePath = AssetDatabase.GetAssetPath(recipe);
                }
            }
            var prefab = slot.Item != null && slot.Item.blockPrefab != null
                ? slot.Item.blockPrefab : LoadChecked<GameObject>(slot.PrefabPath);
            if (prefab != null)
            {
                slot.PrefabPath = AssetDatabase.GetAssetPath(prefab);
                if (!slot.PrefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Pilot item must reference a prefab asset: " + slot.ItemPath);
                foreach (var block in prefab.GetComponents<GridBlock>())
                    if (!(block is AutoRunPilot)) throw new InvalidOperationException("Incompatible existing GridBlock at " + slot.PrefabPath + ". No component was replaced.");
            }
            return slot;
        }

        private static void Author(Slot slot, RecipeIngredient[] ingredients, float mass, float hp,
            float watts, float seconds, Sprite icon, params Material[] materials)
        {
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(slot.PrefabPath) != null;
            GameObject root = existing ? PrefabUtility.LoadPrefabContents(slot.PrefabPath) : new GameObject("AutoRunPilot_" + slot.Label);
            GameObject prefab;
            try
            {
                var pilot = root.GetComponent<AutoRunPilot>();
                if (pilot == null)
                {
                    pilot = root.AddComponent<AutoRunPilot>();
                    pilot.blockName = "Auto-Run Pilot (" + slot.Label + ")";
                    pilot.BlockMass = mass;
                    pilot.maxHP = hp;
                    pilot.controlWatts = watts;
                }
                var collider = root.GetComponent<Collider>();
                if (collider == null)
                {
                    var box = root.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.92f, 0.95f, 0.9f) * slot.Size.CellSize();
                }
                var generated = root.transform.Find("Generated_AutoRunPilot");
                // Existing custom renderers are authoritative. Only repair our own named hierarchy.
                if (generated != null || root.GetComponentsInChildren<Renderer>(true).Length == 0)
                {
                    if (generated == null)
                    {
                        generated = new GameObject("Generated_AutoRunPilot").transform;
                        generated.SetParent(root.transform, false);
                        generated.localScale = Vector3.one * slot.Size.CellSize();
                    }
                    BuildVisuals(generated, materials);
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, slot.PrefabPath);
                if (prefab == null) throw new InvalidOperationException("Could not save pilot prefab: " + slot.PrefabPath);
            }
            finally
            {
                if (existing) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            bool newItem = slot.Item == null;
            if (newItem)
            {
                slot.Item = ScriptableObject.CreateInstance<GridBlockItem>();
                slot.Item.itemId = slot.Id;
                slot.Item.displayName = "Auto-Run Pilot (" + slot.Label + ")";
                slot.Item.description = "Placeable unattended road-vehicle controller. Right-click to plan, start, stop and park. No separate Route Recorder needed. Cyan arrow supplies forward direction when no cockpit is installed.";
                slot.Item.gridSize = slot.Size;
                slot.Item.blockMass = mass;
                slot.Item.blockHP = hp;
                slot.Item.massPerUnit = slot.Size == GridSize.Large ? 1.5f : 0.4f;
                slot.Item.maxStack = 99;
                slot.Item.category = "Grid";
                slot.Item.iconTint = new Color(0.15f, 0.85f, 0.93f);
                AssetDatabase.CreateAsset(slot.Item, slot.ItemPath);
            }
            if (string.IsNullOrWhiteSpace(slot.Item.itemId)) slot.Item.itemId = slot.Id;
            if (slot.Item.icon == null) slot.Item.icon = icon;
            slot.Item.blockPrefab = prefab;
            bool newRecipe = slot.Recipe == null;
            if (newRecipe)
            {
                slot.Recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                slot.Recipe.displayName = "Auto-Run Pilot (" + slot.Label + ")";
                slot.Recipe.requiredStation = StationTier.Assembler;
                slot.Recipe.craftSeconds = seconds;
                slot.Recipe.outputCount = 1;
                slot.Recipe.unlockedByDefault = false;
                AssetDatabase.CreateAsset(slot.Recipe, slot.RecipePath);
            }
            slot.Recipe.outputItem = slot.Item;
            if (slot.Recipe.inputs == null || slot.Recipe.inputs.Length == 0) slot.Recipe.inputs = ingredients;
            else if (slot.Recipe.inputs.Length == ingredients.Length)
            {
                for (int i = 0; i < ingredients.Length; i++)
                {
                    if (slot.Recipe.inputs[i].item != null) continue;
                    var repaired = slot.Recipe.inputs[i];
                    repaired.item = ingredients[i].item;
                    slot.Recipe.inputs[i] = repaired; // reconnect only; keep the existing quantity
                }
            }
            EditorUtility.SetDirty(slot.Item);
            EditorUtility.SetDirty(slot.Recipe);
        }

        private static void BuildVisuals(Transform root, Material[] m)
        {
            Part(root, "Base", new Vector3(0,-.32f,0), new Vector3(.86f,.2f,.8f), m[0]);
            Part(root, "Rim", new Vector3(0,-.2f,0), new Vector3(.8f,.06f,.74f), m[1]);
            Part(root, "Screen", new Vector3(0,-.155f,.04f), new Vector3(.65f,.035f,.48f), m[2]);
            Part(root, "ForwardStem", new Vector3(0,-.128f,.025f), new Vector3(.045f,.015f,.22f), m[3]);
            Part(root, "ForwardLeft", new Vector3(-.055f,-.128f,.15f), new Vector3(.035f,.015f,.16f), m[3], 45f);
            Part(root, "ForwardRight", new Vector3(.055f,-.128f,.15f), new Vector3(.035f,.015f,.16f), m[3], -45f);
            Part(root, "Antenna", new Vector3(-.29f,.08f,-.29f), new Vector3(.045f,.48f,.045f), m[1]);
            Part(root, "AntennaTip", new Vector3(-.29f,.335f,-.29f), new Vector3(.07f,.05f,.07f), m[3]);
            for (int i = 0; i < 5; i++)
                Part(root, "CoolingFin_" + i, new Vector3(-.18f+i*.09f,-.1f,-.3f), new Vector3(.04f,.16f,.11f), m[1]);
            for (int i = 0; i < 3; i++)
                Part(root, "StatusLamp_" + i, new Vector3(-.13f+i*.13f,-.15f,.32f), new Vector3(.07f,.035f,.06f), i == 0 ? m[4] : m[3]);
        }

        private static void Part(Transform root, string name, Vector3 position, Vector3 scale, Material material, float yaw = 0f)
        {
            var part = root.Find(name);
            if (part != null)
            {
                var renderer = part.GetComponent<Renderer>();
                if (renderer == null) renderer = part.gameObject.AddComponent<MeshRenderer>();
                if (renderer is MeshRenderer)
                {
                    var filter = part.GetComponent<MeshFilter>();
                    if (filter == null) filter = part.gameObject.AddComponent<MeshFilter>();
                    if (filter.sharedMesh == null)
                    {
                        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        try { filter.sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh; }
                        finally { Object.DestroyImmediate(cube); }
                    }
                }
                if (renderer.sharedMaterial == null) renderer.sharedMaterial = material;
                return;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material EnsureMaterial(string name, Shader shader, Color colour, bool emission)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            var material = LoadChecked<Material>(path);
            if (material != null) return material;
            material = new Material(shader) { name = "AutoRunPilot_" + name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .45f);
            if (emission && material.HasProperty("_EmissionColor"))
            { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", colour * 1.4f); }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Sprite EnsureIcon()
        {
            string path = Folder + "/Icons/AutoRunPilot.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(96, 96, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color[96*96];
                    for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++)
                    {
                        Color c = Color.clear;
                        if (x >= 10 && x < 86 && y >= 14 && y < 80) c = new Color(.12f,.18f,.22f);
                        if (x >= 17 && x < 79 && y >= 23 && y < 72) c = new Color(.025f,.08f,.1f);
                        if ((x >= 44 && x <= 51 && y >= 31 && y < 57)
                            || (y >= 48 && y <= 64 && Mathf.Abs(x-48) <= 64-y)) c = new Color(.15f,.9f,.95f);
                        if (y >= 17 && y <= 20 && x >= 66 && x <= 77) c = new Color(.35f,.9f,.5f);
                        pixels[y*96+x] = c;
                    }
                    texture.SetPixels(pixels); texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(texture); }
                AssetDatabase.ImportAsset(path);
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Could not import pilot icon: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 96;
            importer.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Pilot icon has no sprite after import.");
            return sprite;
        }

        private static void ValidateRecipe(RecipeDefinition recipe, int expectedSlots)
        {
            if (recipe == null || recipe.inputs == null || recipe.inputs.Length == 0
                || recipe.inputs.Length == expectedSlots) return;
            foreach (var input in recipe.inputs)
                if (input.item == null) throw new InvalidOperationException(
                    "Custom pilot recipe has an unresolved ingredient; cannot infer its intended item without changing the custom recipe.");
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
