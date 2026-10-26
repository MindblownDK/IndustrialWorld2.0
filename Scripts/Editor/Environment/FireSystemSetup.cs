// Assets/Scripts/Editor/Environment/FireSystemSetup.cs
//
// Step 57: FIRE SYSTEM & MATERIAL REGISTRY WIRING — non-destructive repair for
// the runtime fire system (Ifrit hazards, fuel-pool fire spread, procedural FireURP)
// and MaterialRegistry world bindings. The deprecated handheld Igniter tool and its
// recipe are removed in 18.0.0-dev.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.EditorTools
{
    public static class FireSystemSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";

        public static void RunStep57()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 57 — Fire System Wiring started.");

            // Retire legacy Fire Igniter assets if still present on disk.
            var retiredRecipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(ASSET_ROOT + "/Recipes/Recipe_FireIgniter.asset");
            if (retiredRecipe != null)
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
                if (registry != null && registry.recipes != null && registry.recipes.Remove(retiredRecipe))
                    EditorUtility.SetDirty(registry);
                AssetDatabase.DeleteAsset(ASSET_ROOT + "/Recipes/Recipe_FireIgniter.asset");
            }
            if (AssetDatabase.LoadMainAssetAtPath(ASSET_ROOT + "/Items/Tool_FireIgniter.asset") != null)
                AssetDatabase.DeleteAsset(ASSET_ROOT + "/Items/Tool_FireIgniter.asset");

            const string materialRegistryPath = ASSET_ROOT + "/MaterialRegistry.asset";
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
                "• Flammable liquid pools (liquid fuel, refined oil, MGO, crude oil, heavy fuel oil)\n" +
                "  burn, spread across pools, and are extinguished by water or coolant.\n" +
                "• Ifrit fireballs and fire walls ignite fuel pools at runtime.\n" +
                "• Deprecated handheld Fire Igniter assets stay retired.\n" +
                "• Material registry self-repair complete" + (bound > 0
                    ? $" ({bound} scene world(s) re-bound)."
                    : " (all scene worlds already bound)."),
                "OK");
        }
    }
}
#endif
