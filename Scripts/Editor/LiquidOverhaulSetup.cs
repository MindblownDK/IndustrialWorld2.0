// Assets/Scripts/VoxelEngine/Editor/LiquidOverhaulSetup.cs
//
// Step 56 (9.16.0): LIQUIDS OVERHAUL WIRING — non-destructive authoring for the
// seven-liquid world:
//
//   • Renames the Liquid Bucket / Water Bucket item to "Liquid Canister" (only
//     when it still carries a known bucket name — designer renames survive).
//   • Rewrites the description to the canister rules (only when it still holds
//     a known bucket-era description).
//   • Flags industrial planet templates (`body.industrialWorld = true`) whose
//     body name matches industrial keywords — those worlds then generate
//     natural refined-product lakes (refined oil / liquid fuel / heavy fuel
//     oil / marine gas oil) instead of water lakes.
//
// The liquids themselves (materials, sim physics, mesh submeshes, shader
// profiles) are runtime — no assets needed beyond this wiring.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class LiquidOverhaulSetup
    {
        private static readonly string[] IndustrialKeywords =
        {
            "Industrial", "Factory", "Refinery", "Forge", "Foundry", "Plant",
        };

        public static void RunStep56()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 56 — Liquids Overhaul Wiring started.");
            int flagged = 0, renamed = 0;

            // ── 1) The liquid canister (the bucket's 9.16.0 replacement) ──
            string bucketPath = "Assets/VoxelEngineAssets/Items/Tool_WaterBucket.asset";
            var bucket = AssetDatabase.LoadAssetAtPath<LiquidCanister>(bucketPath);
            if (bucket == null)
                bucket = FindFirst<LiquidCanister>("Tool_WaterBucket");
            if (bucket != null)
            {
                if (bucket.displayName == "Water Bucket" || bucket.displayName == "Liquid Bucket")
                {
                    bucket.displayName = "Liquid Canister";
                    renamed++;
                }
                if (bucket.description != null
                    && (bucket.description.IndexOf("water or crude oil",
                            System.StringComparison.OrdinalIgnoreCase) >= 0
                        || bucket.description.IndexOf("bucket",
                            System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    bucket.description =
                        "A 10 L canister that holds ONE liquid at a time. RMB on a liquid pool to scoop " +
                        "0.5 L per click (again and again until full). LMB pours 0.5 L into the world. RMB a " +
                        "liquid tank or water pump to pour the liquid in, and RMB an infinity jack pump to " +
                        "fill the canister with crude oil.";
                }
                if (bucket.itemId == "water_bucket") { /* keep the stable id — saves stay valid */ }
                // Serialized capacity must match the canister (legacy bucket assets carry 1).
                if (bucket.maxDurability != LiquidCanister.CapacityMl)
                    bucket.maxDurability = LiquidCanister.CapacityMl;
                EditorUtility.SetDirty(bucket);
            }
            else
            {
                Debug.LogWarning("[LiquidOverhaulSetup] Liquid Canister item not found — run the earlier items step first.");
            }

            // ── 2) Industrial planet templates ─────────────────────
            var guids = AssetDatabase.FindAssets("t:PlanetTemplate");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var template = AssetDatabase.LoadAssetAtPath<PlanetTemplate>(path);
                if (template == null || template.body == null) continue;

                string name = template.body.bodyName?.Trim() ?? string.Empty;
                if (name.Length == 0) continue;

                bool industrial = false;
                for (int k = 0; k < IndustrialKeywords.Length; k++)
                {
                    if (name.IndexOf(IndustrialKeywords[k], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        industrial = true;
                        break;
                    }
                }
                if (industrial && !template.body.industrialWorld)
                {
                    template.body.industrialWorld = true;
                    EditorUtility.SetDirty(template);
                    flagged++;
                    Debug.Log($"[LiquidOverhaulSetup] Industrial world flagged: {name} ({path}) — fuel/refined-product lakes enabled.");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string message = "Liquids Overhaul Wiring (non-destructive):\n\n"
                + (renamed > 0
                    ? "• The bucket is now the LIQUID CANISTER — 10 L, one liquid at a time. RMB a liquid to scoop (0.5 L per click), LMB to pour into the world, RMB a tank/pump to pour in, RMB an infinity jack pump to fill with crude oil.\n"
                    : "• Canister item already renamed by a designer — left untouched.\n")
                + (flagged > 0
                    ? $"• {flagged} industrial planet template(s) flagged — their lakes now generate as refined oil / liquid fuel / heavy fuel oil / marine gas oil pools.\n"
                    : "• No templates matched industrial keywords — flag any planet via the new BodySettings 'industrialWorld' toggle to give it natural fuel lakes.\n")
                + "\nAll 7 liquids are now placeable in the world with real per-liquid visuals and physics.";
            EditorUtility.DisplayDialog("Voxel Engine — Liquids Overhaul", message, "OK");
        }

        private static T FindFirst<T>(string assetName) where T : Object
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == assetName)
                    return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return null;
        }
    }
}
#endif
