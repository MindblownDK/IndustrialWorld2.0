#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 81 (11.5.1-dev): repair the assets that stole the legacy default identity.
    ///
    /// Before the defaults were blanked, a fresh ItemDefinition arrived pre-filled with
    /// "iron_ore" / "Iron Ore". Any asset authored back then whose id was never typed in
    /// kept those values and now genuinely claims to BE iron ore. Three of them survive:
    /// Item_Gravel, Block_StationaryRadarBeacon and Tool_FireIgniter. The result was a
    /// search for "iron ore" listing four identical rows, of which only the real ore worked.
    ///
    /// This step finds every asset holding the legacy id that is NOT the canonical owner and
    /// gives it an identity derived from its own asset name: Item_Gravel becomes "gravel" /
    /// "Gravel". The same sweep repairs display names that were left on the default while the
    /// id was authored correctly, which is what made Mat_Iron and Mat_Copper read as ore.
    ///
    /// Non-destructive: nothing is deleted, no reference is repointed, and an asset that
    /// already carries an authored identity of its own is left exactly as it is. Only the
    /// stolen id and the stolen display name are overwritten. Safe to re-run — a second pass
    /// finds nothing to do. Every change logs with [Setup 81].
    /// </summary>
    public static class StolenIdentityRepairSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";

        /// <summary>The assets that legitimately own the legacy ids — never touched.</summary>
        private static readonly string[] CanonicalOwners =
        {
            Root + "/Industrial/Items/Item_IronOre.asset",
            Root + "/Industrial/Items/Item_CopperOre.asset",
        };

        private const string LegacyId   = "iron_ore";
        private const string LegacyName = "Iron Ore";

        public static void RunStep81()
        {
            int idFixed = 0, nameFixed = 0, skipped = 0;
            var report = new List<string>();

            var guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { Root });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (CanonicalOwners.Contains(path)) continue;

                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null) continue;

                bool stolenId   = string.Equals(item.itemId, LegacyId, StringComparison.OrdinalIgnoreCase);
                bool stolenName = string.Equals(item.displayName, LegacyName, StringComparison.OrdinalIgnoreCase);
                if (!stolenId && !stolenName) continue;

                // Derive the honest identity from the asset's own file name.
                string bare    = StripPrefix(item.name);
                string newId   = ToSnake(bare);
                string newName = ToTitle(bare);

                if (string.IsNullOrEmpty(newId))
                {
                    Debug.LogWarning($"[Setup 81] Cannot derive an id for '{path}' from its name — left untouched. Give it an id by hand.");
                    skipped++;
                    continue;
                }

                bool changed = false;
                Undo.RecordObject(item, "Repair stolen item identity");

                if (stolenId)
                {
                    Debug.Log($"[Setup 81] '{item.name}' claimed id '{item.itemId}' — now '{newId}'.  ({path})");
                    item.itemId = newId;
                    idFixed++; changed = true;
                }

                // Only rewrite a display name that is still the stolen one. An asset that was
                // properly named keeps whatever the author wrote.
                if (string.Equals(item.displayName, LegacyName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[Setup 81] '{item.name}' displayed as '{item.displayName}' — now '{newName}'.  ({path})");
                    item.displayName = newName;
                    nameFixed++; changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(item);
                    report.Add($"{item.name}  ->  {item.itemId} / {item.displayName}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary =
                $"[Setup 81] Stolen identity repair complete.\n" +
                $"  Ids repaired:           {idFixed}\n" +
                $"  Display names repaired: {nameFixed}\n" +
                $"  Skipped (undecidable):  {skipped}";
            if (report.Count > 0) summary += "\n\n" + string.Join("\n", report);
            Debug.Log(summary);

            EditorUtility.DisplayDialog(
                "Step 81 - Repair Stolen Item Identity",
                (idFixed + nameFixed == 0
                    ? "Nothing to repair. No asset is impersonating Iron Ore."
                    : $"Repaired {idFixed} stolen id(s) and {nameFixed} stolen display name(s).\n\n" +
                      "The item search will now show one row per item.") +
                (skipped > 0 ? $"\n\n{skipped} asset(s) could not be named automatically - see the Console." : ""),
                "OK");
        }

        /// <summary>Drops the conventional asset-name prefix: "Item_Gravel" -> "Gravel".</summary>
        private static string StripPrefix(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return "";
            foreach (var p in new[] { "Item_", "Block_", "Tool_", "Mat_", "GItem_", "Food_", "Seed_" })
                if (assetName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return assetName.Substring(p.Length);
            return assetName;
        }

        /// <summary>"StationaryRadarBeacon" -> "stationary_radar_beacon".</summary>
        private static string ToSnake(string bare)
        {
            if (string.IsNullOrEmpty(bare)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bare.Length; i++)
            {
                char c = bare[i];
                if (c == ' ' || c == '-' || c == '_')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    continue;
                }
                if (char.IsUpper(c) && i > 0 && sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>"StationaryRadarBeacon" -> "Stationary Radar Beacon".</summary>
        private static string ToTitle(string bare)
        {
            if (string.IsNullOrEmpty(bare)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bare.Length; i++)
            {
                char c = bare[i];
                if (c == '_' || c == '-') { sb.Append(' '); continue; }
                if (char.IsUpper(c) && i > 0 && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString().Trim();
        }
    }
}
#endif
