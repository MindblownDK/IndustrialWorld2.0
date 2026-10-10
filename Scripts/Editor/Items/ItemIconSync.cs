// Assets/Scripts/Editor/Items/ItemIconSync.cs
//
// Non-destructive item icon binder and audit. Missing references are restored by
// exact itemId from any category under Assets/VoxelEngineAssets/ItemIcons. The
// manually-run Setup action also reports assets with no matching PNG, ambiguous
// duplicate icon filenames, and bindings that do not match the item's exact id.
//
// Existing healthy references are preserved. Only the explicitly listed repair
// ids are rebound when their current sprite is missing or points at another
// filename; all other mismatches are reported for review, never rewritten.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    public static class ItemIconSync
    {
        private const string IconRoot = "Assets/VoxelEngineAssets/ItemIcons";
        private const string SessionKey = "IW.ItemIconSync.Ran";

        private static readonly HashSet<string> ExactIconRepairIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "dirt",
            "code_lock",
            "security_block",
            "team_banner",
            "grid_team_banner",
            "crusader_shield",
            "data_pipe",
            "wireless_terminal",
            "external_storage",
            "blank_pattern",
            "crafting_card",
            "welder_tool"
        };

        /// <summary>Runs once per editor session after the import pipeline settles.
        /// Writes only when a missing reference or an explicitly targeted bad link
        /// can be repaired from a matching ItemIcons sprite.</summary>
        [InitializeOnLoadMethod]
        private static void AutoSync()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    Sync(auto: true);
            };
        }

        /// <summary>
        /// Restores missing item icons and repairs only the explicitly requested
        /// ids. A manual run performs a full read-only audit of all other bindings.
        /// </summary>
        public static void Sync(bool auto = false)
        {
            var iconIndex = BuildIconIndex();
            var itemGuids = AssetDatabase.FindAssets("t:ItemDefinition");
            int assigned = 0;
            int alreadyExact = 0;
            int missingPng = 0;
            int missingReferenceWithoutPng = 0;
            int mismatchedBinding = 0;
            int ambiguousPng = 0;
            var missing = new List<string>();
            var mismatches = new List<string>();
            var ambiguities = new List<string>();

            foreach (var guid in itemGuids)
            {
                string itemPath = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>(itemPath);
                if (item == null || string.IsNullOrWhiteSpace(item.itemId)) continue;

                Sprite expected = ResolveIcon(item.itemId, item.icon, iconIndex, out string ambiguousPaths);
                if (expected == null)
                {
                    if (!string.IsNullOrEmpty(ambiguousPaths))
                    {
                        ambiguousPng++;
                        ambiguities.Add($"{item.itemId} ({itemPath}) -> {ambiguousPaths}");
                    }
                    else
                    {
                        missingPng++;
                        if (item.icon == null) missingReferenceWithoutPng++;
                        missing.Add($"{item.itemId}  ({itemPath})" +
                            (item.icon != null ? $"; current icon: {AssetDatabase.GetAssetPath(item.icon)}" : ""));
                    }
                    continue;
                }

                if (item.icon == expected)
                {
                    alreadyExact++;
                    continue;
                }

                bool forceExactRepair = ExactIconRepairIds.Contains(item.itemId);
                if (item.icon == null || forceExactRepair)
                {
                    string oldIconPath = item.icon != null
                        ? AssetDatabase.GetAssetPath(item.icon)
                        : "<missing>";
                    var serializedItem = new SerializedObject(item);
                    var iconProperty = serializedItem.FindProperty("icon");
                    if (iconProperty == null) continue;
                    iconProperty.objectReferenceValue = expected;
                    serializedItem.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(item);
                    assigned++;

                    if (forceExactRepair && oldIconPath != "<missing>")
                        mismatches.Add($"{item.itemId}  ({itemPath}) -> repaired {oldIconPath} to {AssetDatabase.GetAssetPath(expected)}");
                }
                else
                {
                    mismatchedBinding++;
                    mismatches.Add($"{item.itemId}  ({itemPath}) -> current {AssetDatabase.GetAssetPath(item.icon)}; expected {AssetDatabase.GetAssetPath(expected)}; left unchanged");
                }
            }

            if (assigned > 0) AssetDatabase.SaveAssets();

            // Automatic repair remains quiet when a healthy project has no missing
            // references. The manual Setup action always prints the complete audit.
            if (auto && assigned == 0 && missingReferenceWithoutPng == 0) return;

            var report = new StringBuilder()
                .Append("[ItemIconSync] rebound=").Append(assigned)
                .Append("  exact=").Append(alreadyExact)
                .Append("  no-matching-png=").Append(missingPng)
                .Append("  wrong-binding-left-alone=").Append(mismatchedBinding)
                .Append("  ambiguous-png=").Append(ambiguousPng);

            if (!auto && missing.Count > 0)
                AppendReportList(report, "Items without an exact itemId PNG:", missing);
            if (!auto && ambiguities.Count > 0)
                AppendReportList(report, "Ambiguous duplicate icon filenames (not rebound):", ambiguities);
            if (!auto && mismatches.Count > 0)
                AppendReportList(report, "Icon binding differences:", mismatches);

            if (assigned > 0 || missingPng > 0 || mismatchedBinding > 0 || ambiguousPng > 0)
                Debug.LogWarning(report.ToString());
            else
                Debug.Log(report.ToString());
        }

        private static Dictionary<string, List<Sprite>> BuildIconIndex()
        {
            var index = new Dictionary<string, List<Sprite>>(StringComparer.OrdinalIgnoreCase);
            if (!AssetDatabase.IsValidFolder(IconRoot)) return index;

            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { IconRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.StartsWith(IconRoot + "/", StringComparison.OrdinalIgnoreCase)) continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;

                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!index.TryGetValue(fileName, out var matches))
                {
                    matches = new List<Sprite>(1);
                    index.Add(fileName, matches);
                }
                matches.Add(sprite);
            }
            return index;
        }

        private static Sprite ResolveIcon(string itemId, Sprite current,
            Dictionary<string, List<Sprite>> iconIndex, out string ambiguousPaths)
        {
            ambiguousPaths = null;
            string[] candidates =
            {
                itemId,
                itemId.Replace("item_", ""),
                itemId.Replace("gitem_", "")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrEmpty(candidate) || !iconIndex.TryGetValue(candidate, out var matches))
                    continue;

                if (current != null)
                {
                    for (int j = 0; j < matches.Count; j++)
                        if (matches[j] == current) return current;
                }

                if (matches.Count == 1) return matches[0];

                var paths = new List<string>(matches.Count);
                for (int j = 0; j < matches.Count; j++)
                    paths.Add(AssetDatabase.GetAssetPath(matches[j]));
                ambiguousPaths = string.Join(", ", paths);
                return null;
            }
            return null;
        }

        private static void AppendReportList(StringBuilder report, string title, List<string> lines)
        {
            report.Append('\n').Append(title);
            for (int i = 0; i < lines.Count; i++)
                report.Append("\n  ").Append(lines[i]);
        }
    }
}
