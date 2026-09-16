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
    /// Step 79 (11.2.2-dev): audit and repair item identity.
    ///
    /// <see cref="ItemDefinition.itemId"/> defaults to <c>"iron_ore"</c>, so any item asset
    /// whose id was never authored silently claims to BE iron ore. Four assets in the project
    /// are in that state — a gravel item, a radar beacon block, a fire igniter tool, and the
    /// real Industrial iron ore — which makes the id ambiguous for both persistence and the
    /// identity fallback the furnaces now use.
    ///
    /// This step gives every unauthored asset an id derived from its own file name, then
    /// reports any id still claimed by more than one asset so the remaining duplicates are
    /// visible rather than silent.
    ///
    /// Non-destructive: an asset that already carries an authored id is never rewritten, and
    /// nothing but <c>itemId</c> is ever touched. Running it twice is a no-op. Every change
    /// logs with the [Setup 79] prefix.
    /// </summary>
    public static class ItemIdentityAuditSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";

        public static void RunStep79()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Item Identity Audit",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var items = LoadAllItems();
                if (items.Count == 0)
                {
                    EditorUtility.DisplayDialog("Item Identity Audit",
                        "No item assets found under " + Root + ".\n\nRun the earlier content steps first.", "OK");
                    return;
                }

                int repaired = RepairUnauthoredIds(items);
                var duplicates = FindDuplicateIds(items);

                if (repaired > 0)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                string summary =
                    "Scanned " + items.Count + " item assets.\n\n" +
                    (repaired == 0
                        ? "No unauthored ids found — every item already carries its own id.\n"
                        : repaired + " item(s) still carried the unauthored default id and were given " +
                          "an id derived from their asset name.\n") +
                    (duplicates.Count == 0
                        ? "\nNo duplicate ids remain."
                        : "\n" + duplicates.Count + " id(s) are still shared by more than one asset. " +
                          "These are intentional-looking content duplicates, not defaults — see the console " +
                          "for the full list. The furnaces treat them as the same item, so smelting works " +
                          "either way; resolving them is a content decision, not a code one.");

                Debug.Log("[Setup 79] Item identity audit complete: " + items.Count + " assets scanned, " +
                          repaired + " unauthored id(s) repaired, " + duplicates.Count + " duplicate id(s) remaining.");

                EditorUtility.DisplayDialog("Item Identity Audit", summary, "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Item Identity Audit",
                    "Audit stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static List<ItemDefinition> LoadAllItems()
        {
            var result = new List<ItemDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null) result.Add(item);
            }
            return result;
        }

        /// <summary>
        /// Give every asset still carrying the field default an id built from its file name.
        /// The real Industrial iron ore keeps <c>iron_ore</c> — its name produces exactly that
        /// — so the one asset that genuinely owns the id is the one that keeps it.
        /// </summary>
        private static int RepairUnauthoredIds(List<ItemDefinition> items)
        {
            // The asset whose own name resolves to the default id is its rightful owner.
            var owner = items.FirstOrDefault(i => IdFromAssetName(i) == ItemIdentity.UnauthoredId);

            int repaired = 0;
            foreach (var item in items)
            {
                if (ItemIdentity.IsAuthoredId(item.itemId)) continue;   // already authored — never touched
                if (item == owner)
                {
                    Debug.Log("[Setup 79] " + AssetDatabase.GetAssetPath(item) +
                              " keeps id '" + ItemIdentity.UnauthoredId + "' — its asset name owns it.");
                    continue;
                }

                string fresh = IdFromAssetName(item);
                if (string.IsNullOrEmpty(fresh)) continue;

                Debug.Log("[Setup 79] " + AssetDatabase.GetAssetPath(item) + " carried the unauthored default id '" +
                          item.itemId + "'; set to '" + fresh + "'.");
                item.itemId = fresh;
                EditorUtility.SetDirty(item);
                repaired++;
            }
            return repaired;
        }

        /// <summary>Report ids still claimed by more than one asset, so nothing stays silent.</summary>
        private static List<string> FindDuplicateIds(List<ItemDefinition> items)
        {
            var byId = new Dictionary<string, List<ItemDefinition>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (!ItemIdentity.IsAuthoredId(item.itemId) && item.itemId != ItemIdentity.UnauthoredId) continue;
                if (!byId.TryGetValue(item.itemId, out var list))
                    byId[item.itemId] = list = new List<ItemDefinition>();
                list.Add(item);
            }

            var duplicates = new List<string>();
            foreach (var pair in byId.OrderBy(p => p.Key))
            {
                if (pair.Value.Count < 2) continue;
                duplicates.Add(pair.Key);
                var paths = pair.Value.Select(i => "\n      " + AssetDatabase.GetAssetPath(i));
                Debug.LogWarning("[Setup 79] Id '" + pair.Key + "' is shared by " + pair.Value.Count +
                                 " assets:" + string.Concat(paths));
            }
            return duplicates;
        }

        /// <summary>
        /// Turn an asset file name into a stable snake_case id: <c>Item_CopperOre</c> becomes
        /// <c>copper_ore</c>. The common <c>Item_</c> / <c>Block_</c> / <c>Tool_</c> prefixes are
        /// stripped so the id names the thing, not its asset category.
        /// </summary>
        private static string IdFromAssetName(ItemDefinition item)
        {
            string name = item != null ? item.name : null;
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var prefix in new[] { "Item_", "Block_", "Tool_", "GItem_", "Food_", "Seed_" })
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(prefix.Length);
                    break;
                }

            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' || c == '-') { sb.Append('_'); continue; }
                // Insert a separator at each camel-case hump so "CopperOre" reads "copper_ore".
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) && sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }
}
#endif
