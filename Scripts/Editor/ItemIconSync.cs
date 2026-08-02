// Assets/Scripts/VoxelEngine/Editor/ItemIconSync.cs
//
// Self-healing icon binder. Assigns the premium sticker icons from
//   Assets/VoxelEngineAssets/ItemIcons/<Category>/<itemId>.png
// to EVERY ItemDefinition asset whose serialized icon reference is missing —
// any folder, including duplicate/legacy definitions.
//
// WHY AUTO: icon bindings are stored as sprite GUID references inside each
// ItemDefinition. If a PNG ever gets re-imported without its companion .meta
// (fresh clone, partial copy, manual file drop), Unity regenerates a new GUID
// and every reference silently goes null — the item still displays its name
// and description, so the crafting UI shows coloured fallback boxes with no
// obvious error. This sync runs automatically after every editor load and
// re-binds by itemId whenever the reference is missing, so the project heals
// itself no matter how the files arrived.
//
// Non-destructive: items whose icon already resolves are never touched, and
// items without a generated PNG are left alone (reported in the log summary).

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    public static class ItemIconSync
    {
        private const string IconRoot = "Assets/VoxelEngineAssets/ItemIcons";
        private const string SessionKey = "IW.ItemIconSync.Ran";

        /// <summary>Runs once per editor session, right after the initial import
        /// pipeline settles. Cheap when everything is already bound (the common
        /// case) — it only writes when it actually fixed something.</summary>
        [InitializeOnLoadMethod]
        private static void AutoSync()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            // Delay until AssetDatabase is fully ready after a domain reload.
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    Sync(auto: true);
            };
        }

        [MenuItem("Tools/Voxel Engine/Sync Item Icons (ItemIcons folder)")]
        private static void SyncMenu() => Sync(auto: false);

        public static void Sync(bool auto = false)
        {
            var guids = AssetDatabase.FindAssets("t:ItemDefinition");
            int assigned = 0, already = 0, noIcon = 0;
            var misses = new List<string>();

            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var def = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>(path);
                if (def == null || string.IsNullOrEmpty(def.itemId)) continue;

                // Healthy binding — leave it exactly as the pipeline wrote it.
                if (def.icon != null) { already++; continue; }

                var sprite = LoadIcon(def.itemId);
                if (sprite == null) { noIcon++; misses.Add($"{def.itemId}  ({path})"); continue; }

                var so = new SerializedObject(def);
                var prop = so.FindProperty("icon");
                if (prop == null) continue;
                prop.objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                assigned++;
            }

            if (assigned > 0) AssetDatabase.SaveAssets();

            // Log only when useful: always on manual run, on auto only if it fixed
            // something or found PNG-less items (i.e. the project was actually sick).
            if (!auto || assigned > 0 || noIcon > 0)
            {
                string summary =
                    $"[ItemIconSync] re-bound={assigned}  already-ok={already}  no-png={noIcon}" +
                    (misses.Count > 0 && !auto ? "\nMissing PNG for:\n  " + string.Join("\n  ", misses) : "");
                if (assigned > 0) Debug.LogWarning(summary);
                else Debug.Log(summary);
            }
        }

        /// <summary>Finds the generated sticker sprite for an itemId anywhere
        /// under the ItemIcons tree — category folder may vary, so we search by
        /// file name and require an exact <itemId>.png match. Falls back to a
        /// couple of friendly legacy name variants just in case.</summary>
        private static Sprite LoadIcon(string itemId)
        {
            string[] candidates = { itemId, itemId.Replace("item_", ""), itemId.Replace("gitem_", "") };
            foreach (var c in candidates)
            {
                foreach (var g in AssetDatabase.FindAssets($"{c} t:Sprite"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (!p.Replace('\\', '/').Contains("/ItemIcons/")) continue;
                    if (Path.GetFileNameWithoutExtension(p) != c) continue;
                    var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                    if (s != null) return s;
                }
            }
            return null;
        }
    }
}
