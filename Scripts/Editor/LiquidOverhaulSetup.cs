// Assets/Scripts/VoxelEngine/Editor/LiquidOverhaulSetup.cs
//
// Step 56 (9.16.0): LIQUIDS OVERHAUL WIRING — non-destructive authoring for the
// seven-liquid world:
//
//   • Renames the Water Bucket item to "Liquid Bucket" (only when it still
//     carries the default name/description — designer renames survive).
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

            // ── 1) The universal bucket ────────────────────────────
            string bucketPath = "Assets/VoxelEngineAssets/Items/Tool_WaterBucket.asset";
            var bucket = AssetDatabase.LoadAssetAtPath<WaterBucket>(bucketPath);
            if (bucket == null)
                bucket = FindFirst<WaterBucket>("Tool_WaterBucket");
            if (bucket != null)
            {
                if (bucket.displayName == "Water Bucket")
                {
                    bucket.displayName = "Liquid Bucket";
                    renamed++;
                }
                if (bucket.description != null && bucket.description.IndexOf("water or crude oil",
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bucket.description =
                        "LMB scoops any liquid (water, crude oil, refined oil, liquid fuel, heavy fuel oil, " +
                        "marine gas oil or engine coolant) into the bucket. RMB places the carried liquid into " +
                        "the voxel simulation. Right-click a liquid tank to fill the bucket or pour it in.";
                }
                if (bucket.itemId == "water_bucket") { /* keep the stable id — saves stay valid */ }
                EditorUtility.SetDirty(bucket);
            }
            else
            {
                Debug.LogWarning("[LiquidOverhaulSetup] Liquid Bucket item not found — run the earlier items step first.");
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
                    ? "• The bucket is now the UNIVERSAL Liquid Bucket — scoops and places all 7 liquids; right-click a liquid tank to fill/pour it.\n"
                    : "• Bucket item already renamed by a designer — left untouched.\n")
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
