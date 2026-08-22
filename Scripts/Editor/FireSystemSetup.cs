// Assets/Scripts/VoxelEngine/Editor/FireSystemSetup.cs
//
// Step 57 (9.16.0): FIRE SYSTEM WIRING — non-destructive authoring for the fire
// system (Liquids Overhaul, Part 2):
//
//   • Creates the IGNITER tool (flint-and-steel sparks that light flammable
//     liquid pools) — created only when missing; an already-authored asset is
//     never overwritten.
//   • Creates its Crafting Bench recipe (2 iron + 1 copper) — created fully when
//     missing; designer-authored inputs are preserved when present.
//
// The fire simulation, visuals, lights and the procedural FireURP shader are all
// runtime — no assets needed beyond the igniter.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class FireSystemSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string ITEMS      = ASSET_ROOT + "/Items";
        private const string RECIPES    = ASSET_ROOT + "/Recipes";

        public static void RunStep57()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 57 — Fire System Wiring started.");

            EnsureFolder(ITEMS);
            EnsureFolder(RECIPES);

            // ── 1) Igniter tool (create when missing — designer edits survive) ──
            string igniterPath = ITEMS + "/Tool_FireIgniter.asset";
            var igniter = AssetDatabase.LoadAssetAtPath<FireIgniter>(igniterPath);
            bool created = igniter == null;
            if (created)
            {
                igniter = ScriptableObject.CreateInstance<FireIgniter>();
                AssetDatabase.CreateAsset(igniter, igniterPath);
                igniter.itemId = "fire_igniter";
                igniter.displayName = "Igniter";
                igniter.description =
                    "Flint-and-steel sparks that set flammable liquids alight. RMB a pool of liquid fuel, " +
                    "refined oil, MGO, crude oil or heavy fuel oil to start a fire — fires spread, burn the " +
                    "fuel down, glow and crackle, and water or coolant puts them out. 64 uses.";
                igniter.iconTint = new Color(0.95f, 0.55f, 0.15f);
                igniter.maxStack = 1;
                igniter.maxDurability = 64;
                igniter.toolType = ToolType.Other;
                igniter.category = "Tools";
            }
            EditorUtility.SetDirty(igniter);

            // ── 2) Recipe (created fully when missing; inputs preserved when authored) ──
            var iron   = LoadItem(ITEMS + "/Item_IronIngot.asset");
            var copper = LoadItem(ITEMS + "/Item_CopperIngot.asset");

            var recipe = GetOrCreateAsset<RecipeDefinition>(RECIPES + "/Recipe_FireIgniter.asset");
            recipe.displayName = "Igniter";
            recipe.outputItem = igniter;
            recipe.outputCount = 1;
            recipe.requiredStation = StationTier.CraftingBench;
            if (recipe.craftSeconds <= 0f) recipe.craftSeconds = 2f;
            recipe.unlockedByDefault = true;
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                if (iron   != null) inputs.Add(new RecipeIngredient { item = iron,   count = 2 });
                if (copper != null) inputs.Add(new RecipeIngredient { item = copper, count = 1 });
                recipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(recipe);

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            if (registry != null && !registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
            }

            // ── 3) Material registry self-repair (9.16.0 field round) ──
            // A missing/deleted MaterialRegistry asset (or a stale scene reference after
            // an asset GUID change) is what silently degrades material name resolution —
            // the inspection HUD falls back to enum names and hardness/mineability reads
            // lose their authored values. Recreate the asset when missing, top up any
            // missing definition assets, and force-rebind scene SphereWorlds whose
            // reference broke (house pattern — non-destructive, idempotent).
            const string materialRegistryPath = "Assets/VoxelEngineAssets/MaterialRegistry.asset";
            var matRegistry = AssetDatabase.LoadAssetAtPath<VoxelEngine.Materials.MaterialRegistry>(materialRegistryPath);
            if (matRegistry == null)
            {
                matRegistry = ScriptableObject.CreateInstance<VoxelEngine.Materials.MaterialRegistry>();
                AssetDatabase.CreateAsset(matRegistry, materialRegistryPath);
                Debug.LogWarning("[FireSystemSetup] MaterialRegistry asset was missing — recreated. Topping up definitions…");
            }
            VoxelEngine.EditorTools.MaterialDefinitionAuthoring.EnsureMaterialDefinitions();

            int bound = 0;
            var worlds = Object.FindObjectsByType<VoxelEngine.Cosmos.SphereWorld>(FindObjectsInactive.Include);
            foreach (var sphere in worlds)
            {
                if (sphere == null) continue;
                if (sphere.materialRegistry == null)
                {
                    sphere.materialRegistry = matRegistry;
                    EditorUtility.SetDirty(sphere);
                    bound++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Fire System",
                "Fire System Wiring (non-destructive):\n\n" +
                "• The IGNITER is crafted at the Crafting Bench (2 iron + 1 copper).\n" +
                "• RMB any flammable liquid pool to set it alight: liquid fuel, refined oil,\n" +
                "  MGO, crude oil, heavy fuel oil.\n" +
                "• Fires burn the fuel down, spread across pools, glow and flicker, and burn\n" +
                "  players who walk in. Water and coolant extinguish them.\n" +
                "• Ifrit fireballs and fire walls also ignite fuel pools.\n" +
                "• The sim, visuals, lights and FireURP shader are runtime — no assets needed.\n" +
                "• Material registry self-repair included: missing registry recreated,\n" +
                "  missing material definitions topped up" + (bound > 0
                    ? $", {bound} scene world(s) re-bound to the registry."
                    : " (all scene worlds already bound)."),
                "OK");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static ItemDefinition LoadItem(string path)
        {
            return AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        }

        private static T GetOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
#endif
