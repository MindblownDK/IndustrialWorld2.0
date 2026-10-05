// Assets/Scripts/Editor/Items/ItemScriptHealer.cs
//
// 14.61.0 - the "Wireless Terminal / Crusader Shield lost its script" guard,
// the ScriptableObject sibling of StoragePrefabHealer.
//
// Same root cause: a script's identity is the GUID in its .meta file. When a
// .meta is not committed, every machine invents a new GUID for the same
// script, and any ITEM ASSET that referenced the old GUID wakes up as
// "Missing (Mono Script)" - the item vanishes from recipes, equipment slots
// and the terminal. The lasting cure is committing the .meta files; this
// healer is the belt to that suspender: on editor load it checks the known
// victims and, when the typed load fails, swaps ONLY the m_Script GUID in
// the asset's YAML back to the current script - every serialized field the
// team has tuned survives untouched. Silent when there is nothing to do.

using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    [InitializeOnLoad]
    public static class ItemScriptHealer
    {
        private const string WirelessTerminalAsset =
            "Assets/VoxelEngineAssets/Storage/Network/Items/Item_WirelessTerminal.asset";
        private const string CrusaderShieldAsset =
            "Assets/VoxelEngineAssets/Combat/Banners/Items/Item_CrusaderShield.asset";

        static ItemScriptHealer()
        {
            // Delay one tick: the asset database is not ready inside the
            // InitializeOnLoad constructor itself.
            EditorApplication.delayCall += HealAll;
        }

        private static void HealAll()
        {
            Heal<VoxelEngine.Storage.WirelessTerminalItem>(WirelessTerminalAsset, "WirelessTerminalItem");
            Heal<VoxelEngine.Combat.ShieldItem>(CrusaderShieldAsset, "ShieldItem");
        }

        /// <summary>When the asset exists but no longer loads as T (dead script
        /// GUID), rewrite its m_Script reference to the current script. All other
        /// serialized data in the asset is preserved byte for byte.</summary>
        private static void Heal<T>(string assetPath, string scriptClass) where T : ScriptableObject
        {
            if (!System.IO.File.Exists(assetPath)) return;                    // setup never ran here
            if (AssetDatabase.LoadAssetAtPath<T>(assetPath) != null) return;  // healthy

            string scriptGuid = null;
            foreach (var guid in AssetDatabase.FindAssets(scriptClass + " t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == scriptClass)
                {
                    scriptGuid = guid;
                    break;
                }
            }
            if (string.IsNullOrEmpty(scriptGuid)) return;   // script itself is gone - nothing we can do

            string text = System.IO.File.ReadAllText(assetPath);
            string healed = System.Text.RegularExpressions.Regex.Replace(text,
                @"m_Script: \{fileID: 11500000, guid: [0-9a-fA-F]+, type: 3\}",
                "m_Script: {fileID: 11500000, guid: " + scriptGuid + ", type: 3}");
            if (healed == text) return;                     // not the shape we expected - leave it alone

            System.IO.File.WriteAllText(assetPath, healed);
            AssetDatabase.ImportAsset(assetPath);
            Debug.Log($"[ItemHealer] '{assetPath}' had a dead script reference - re-pointed to {scriptClass}. " +
                      "All item data preserved. Commit the script's .meta file to stop this from recurring.");
        }
    }
}
