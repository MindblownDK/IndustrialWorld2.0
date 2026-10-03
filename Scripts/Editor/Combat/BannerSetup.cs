#if UNITY_EDITOR
// Assets/Scripts/Editor/Combat/BannerSetup.cs
//
// Step 106 - Team Banners & Crusader Shield (14.37.0).
// Authors, non-destructively:
//   - the Crusader Shield item (ShieldItem) + recipe
//   - the Team Banner placed block (BannerDisplay prefab + BlockItem) + recipe
//   - the Ship Banner grid block (GridBannerBlock prefab + GridBlockItem) + recipe
// Create if missing, reconnect if existing; authored balance values are
// never overwritten. The banner visuals themselves (gold frame, swallow-
// tail cloth, text lines) are built at runtime by BannerCloth, so the
// prefabs stay tiny: a base, a collider and the component.

using UnityEngine;
using UnityEditor;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class BannerSetup
    {
        public static void RunStep106()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 106 - Team Banners & Crusader Shield setup started.");

            const string ASSET_ROOT = "Assets/VoxelEngineAssets";
            const string ROOT = ASSET_ROOT + "/Combat/Banners";
            const string ITEMS = ROOT + "/Items";
            const string RECIPES = ROOT + "/Recipes";
            const string PREFABS = ROOT + "/Prefabs";
            const string MATS = PREFABS + "/Mats";

            foreach (var folder in new[] { ASSET_ROOT + "/Combat", ROOT, ITEMS, RECIPES, PREFABS, MATS })
                EnsureFolder(folder);

            int created = 0, preserved = 0;

            Material GetMat(string name, Color color, float metallic = 0f)
            {
                string path = MATS + "/" + name + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) { preserved++; return existing; }
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { name = name, color = color };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", metallic > 0.5f ? 0.6f : 0.35f);
                AssetDatabase.CreateAsset(mat, path);
                created++;
                return mat;
            }

            var goldMat = GetMat("Mat_BannerGold", new Color(0.85f, 0.68f, 0.21f), metallic: 0.75f);
            var stoneMat = GetMat("Mat_BannerBase", new Color(0.38f, 0.38f, 0.42f));

            var registry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var ironPlate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_IronPlate.asset");
            var steelPlate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Industrial/Items/Item_SteelPlate.asset");
            var plank = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ASSET_ROOT + "/Tiered/Items/Item_WoodenPlank.asset");

            // ── shared helpers (GridScreenSetup pattern) ──────────────────

            TItem GetItem<TItem>(string assetName) where TItem : ItemDefinition
            {
                string path = ITEMS + "/" + assetName + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<TItem>(path);
                if (item != null) { preserved++; return item; }
                item = ScriptableObject.CreateInstance<TItem>();
                AssetDatabase.CreateAsset(item, path);
                created++;
                return item;
            }

            void EnsureRecipe(string assetName, ItemDefinition output, string displayName,
                (ItemDefinition item, int count)[] inputs, float seconds)
            {
                string path = RECIPES + "/" + assetName + ".asset";
                var recipe = AssetDatabase.LoadAssetAtPath<VoxelEngine.Crafting.RecipeDefinition>(path);
                bool isNew = recipe == null;
                if (isNew)
                {
                    recipe = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                    AssetDatabase.CreateAsset(recipe, path);
                    created++;
                }
                else preserved++;

                if (string.IsNullOrWhiteSpace(recipe.displayName)) recipe.displayName = displayName;
                recipe.outputItem = output;
                if (recipe.outputCount <= 0) recipe.outputCount = 1;
                if (isNew)
                {
                    recipe.requiredStation = VoxelEngine.Crafting.StationTier.CraftingBench;
                    recipe.craftSeconds = seconds;
                    recipe.unlockedByDefault = true;
                }
                else if (recipe.craftSeconds <= 0f) recipe.craftSeconds = seconds;

                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    var list = new System.Collections.Generic.List<VoxelEngine.Crafting.RecipeIngredient>();
                    foreach (var (item, count) in inputs)
                        if (item != null) list.Add(new VoxelEngine.Crafting.RecipeIngredient { item = item, count = count });
                    recipe.inputs = list.ToArray();
                }
                EditorUtility.SetDirty(recipe);
                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }

            GameObject LoadOrNewPrefabRoot(string prefabPath, string rootName, out bool existed)
            {
                existed = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
                var root = existed ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(rootName);
                root.name = rootName;
                // Regenerate only our own children; custom additions survive.
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                        Object.DestroyImmediate(child.gameObject);
                }
                return root;
            }

            void AddGeneratedPart(GameObject root, string name, PrimitiveType type,
                Vector3 pos, Vector3 scale, Material mat)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pos;
                go.transform.localScale = scale;
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = mat;
                var collider = go.GetComponent<Collider>();
                if (collider != null) Object.DestroyImmediate(collider);
            }

            void SavePrefab(GameObject root, string prefabPath, bool existed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (existed) { PrefabUtility.UnloadPrefabContents(root); preserved++; }
                else { Object.DestroyImmediate(root); created++; }
            }

            // ── 1. Crusader Shield (held item) ────────────────────────────

            var shield = GetItem<VoxelEngine.Combat.ShieldItem>("Item_CrusaderShield");
            if (string.IsNullOrWhiteSpace(shield.itemId)) shield.itemId = "crusader_shield";
            if (string.IsNullOrWhiteSpace(shield.displayName)) shield.displayName = "Crusader Shield";
            if (string.IsNullOrWhiteSpace(shield.description))
                shield.description = "Hold RIGHT MOUSE to raise it: blocks most incoming damage while raised, " +
                                     "at the cost of durability. The face flies your team's banner.";
            if (shield.iconTint == Color.white) shield.iconTint = new Color(0.85f, 0.68f, 0.21f);
            if (string.IsNullOrWhiteSpace(shield.category) || shield.category == "Misc") shield.category = "Combat";
            shield.toolType = ToolType.Other;
            if (shield.maxDurability <= 0 || shield.maxDurability == 150) shield.maxDurability = 400;
            if (shield.massPerUnit <= 0f || Mathf.Approximately(shield.massPerUnit, 1f)) shield.massPerUnit = 6f;
            if (shield.strength <= 0f || Mathf.Approximately(shield.strength, 60f)) shield.strength = 12f;
            if (shield.blockReduction <= 0f) shield.blockReduction = 0.65f;
            if (shield.durabilityPerBlockedHit <= 0) shield.durabilityPerBlockedHit = 2;
            EditorUtility.SetDirty(shield);

            EnsureRecipe("Recipe_CrusaderShield", shield, "Crusader Shield",
                new[] { (plank, 6), (ironPlate, 4) }, seconds: 5f);

            // ── 2. Team Banner (static placed block) ──────────────────────

            var bannerRoot = LoadOrNewPrefabRoot(PREFABS + "/TeamBannerPole.prefab", "TeamBannerPole", out bool bannerExisted);
            // A stone footing so the pole reads as "planted" - the frame and
            // cloth are built at runtime by BannerDisplay/BannerCloth.
            AddGeneratedPart(bannerRoot, "Generated_Footing", PrimitiveType.Cube,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.42f, 0.16f, 0.42f), stoneMat);
            AddGeneratedPart(bannerRoot, "Generated_FootingTrim", PrimitiveType.Cube,
                new Vector3(0f, 0.17f, 0f), new Vector3(0.30f, 0.05f, 0.30f), goldMat);
            var bannerCol = bannerRoot.GetComponent<BoxCollider>();
            if (bannerCol == null) bannerCol = bannerRoot.AddComponent<BoxCollider>();
            bannerCol.center = new Vector3(0f, 1.3f, 0f);
            bannerCol.size = new Vector3(0.42f, 2.6f, 0.42f);
            if (bannerRoot.GetComponent<VoxelEngine.Combat.BannerDisplay>() == null)
                bannerRoot.AddComponent<VoxelEngine.Combat.BannerDisplay>();
            SavePrefab(bannerRoot, PREFABS + "/TeamBannerPole.prefab", bannerExisted);
            var bannerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "/TeamBannerPole.prefab");

            var bannerItem = GetItem<BlockItem>("Item_TeamBanner");
            if (string.IsNullOrWhiteSpace(bannerItem.itemId)) bannerItem.itemId = "team_banner";
            if (string.IsNullOrWhiteSpace(bannerItem.displayName)) bannerItem.displayName = "Team Banner";
            if (string.IsNullOrWhiteSpace(bannerItem.description))
                bannerItem.description = "Plants your team's banner. It mirrors the banner edited under " +
                                         "PAUSE > TEAMS and updates everywhere the moment it changes.";
            if (bannerItem.iconTint == Color.white) bannerItem.iconTint = new Color(0.82f, 0.18f, 0.18f);
            if (string.IsNullOrWhiteSpace(bannerItem.category) || bannerItem.category == "Misc") bannerItem.category = "Building";
            if (bannerItem.maxStack <= 1) bannerItem.maxStack = 20;
            if (bannerItem.massPerUnit <= 0f || Mathf.Approximately(bannerItem.massPerUnit, 1f)) bannerItem.massPerUnit = 8f;
            bannerItem.placedPrefab = bannerPrefab;
            if (bannerItem.gridSize == Vector3Int.one) bannerItem.gridSize = new Vector3Int(1, 3, 1);
            if (bannerItem.blockHealth <= 0 || bannerItem.blockHealth == 100) bannerItem.blockHealth = 150;
            EditorUtility.SetDirty(bannerItem);

            EnsureRecipe("Recipe_TeamBanner", bannerItem, "Team Banner",
                new[] { (plank, 4), (ironPlate, 1) }, seconds: 3f);

            // ── 3. Ship Banner (grid block) ───────────────────────────────

            var gridRoot = LoadOrNewPrefabRoot(PREFABS + "/GridBannerBlock.prefab", "GridBannerBlock", out bool gridExisted);
            AddGeneratedPart(gridRoot, "Generated_Mount", PrimitiveType.Cube,
                new Vector3(0f, -1.17f, 0f), new Vector3(0.5f, 0.16f, 0.5f), stoneMat);
            AddGeneratedPart(gridRoot, "Generated_MountTrim", PrimitiveType.Cube,
                new Vector3(0f, -1.07f, 0f), new Vector3(0.36f, 0.05f, 0.36f), goldMat);
            var gridCol = gridRoot.GetComponent<BoxCollider>();
            if (gridCol == null) gridCol = gridRoot.AddComponent<BoxCollider>();
            gridCol.center = new Vector3(0f, 0.05f, 0f);
            gridCol.size = new Vector3(0.5f, 2.4f, 0.5f);
            var gridBanner = gridRoot.GetComponent<VoxelEngine.GridSystem.GridBannerBlock>();
            if (gridBanner == null) gridBanner = gridRoot.AddComponent<VoxelEngine.GridSystem.GridBannerBlock>();
            if (string.IsNullOrWhiteSpace(gridBanner.blockName) || gridBanner.blockName == "Armor Block")
                gridBanner.blockName = "Ship Banner";
            if (gridBanner.BlockMass <= 0f || Mathf.Approximately(gridBanner.BlockMass, 100f)) gridBanner.BlockMass = 60f;
            if (gridBanner.maxHP <= 0f || Mathf.Approximately(gridBanner.maxHP, 200f)) gridBanner.maxHP = 150f;
            SavePrefab(gridRoot, PREFABS + "/GridBannerBlock.prefab", gridExisted);
            var gridPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "/GridBannerBlock.prefab");

            var gridItem = GetItem<VoxelEngine.GridSystem.GridBlockItem>("Item_GridTeamBanner");
            if (string.IsNullOrWhiteSpace(gridItem.itemId)) gridItem.itemId = "grid_team_banner";
            if (string.IsNullOrWhiteSpace(gridItem.displayName)) gridItem.displayName = "Ship Banner";
            if (string.IsNullOrWhiteSpace(gridItem.description))
                gridItem.description = "Your team's banner as a ship block - plant your colours on the hull. " +
                                       "Mirrors the banner edited under PAUSE > TEAMS.";
            if (gridItem.iconTint == Color.white) gridItem.iconTint = new Color(0.82f, 0.18f, 0.18f);
            if (gridItem.maxStack <= 0) gridItem.maxStack = 50;
            if (gridItem.massPerUnit <= 0f) gridItem.massPerUnit = 2f;
            if (string.IsNullOrWhiteSpace(gridItem.category)) gridItem.category = "Grid";
            gridItem.gridSize = VoxelEngine.GridSystem.GridSize.Large;
            gridItem.blockPrefab = gridPrefab;
            if (gridItem.blockMass <= 0f) gridItem.blockMass = 60f;
            if (gridItem.blockHP <= 0f) gridItem.blockHP = 150f;
            EditorUtility.SetDirty(gridItem);

            EnsureRecipe("Recipe_GridTeamBanner", gridItem, "Ship Banner",
                new[] { (plank, 4), (steelPlate, 1) }, seconds: 3f);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Step 106] Team Banners & Crusader Shield complete - {created} created, {preserved} preserved. " +
                      "Banner editing lives under PAUSE > TEAMS; grid screens gain the 'Team Banner' mode automatically.");
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
