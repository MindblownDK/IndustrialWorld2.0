// Assets/Scripts/VoxelEngine/Editor/ItemIconSync.cs
//
// One-click icon binder: assigns the premium PNG icons from
//   Assets/VoxelEngineAssets/GridSystem/Textures/ItemIcons/<itemId>.png
// to EVERY ItemDefinition asset with a matching itemId — any folder, including
// duplicate/legacy definitions, so the icon the player sees is always the right
// one. Non-destructive: items without a generated PNG are left untouched, and
// icons that already match are skipped.
//
// The YAML is already patched by the offline pipeline (icons bind on import);
// this menu item is the belt-and-braces in-engine re-sync + verification report.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    public static class ItemIconSync
    {
        private const string IconDir = "Assets/VoxelEngineAssets/GridSystem/Textures/ItemIcons";

        [MenuItem("Tools/Voxel Engine/Sync Item Icons (ItemIcons folder)")]
        public static void Sync()
        {
            var guids = AssetDatabase.FindAssets("t:ItemDefinition");
            int assigned = 0, already = 0, noIcon = 0;
            var misses = new List<string>();

            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var def = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.ItemDefinition>(path);
                if (def == null || string.IsNullOrEmpty(def.itemId)) continue;

                var sprite = LoadIcon(def.itemId);
                if (sprite == null) { noIcon++; misses.Add($"{def.itemId}  ({path})"); continue; }

                if (def.icon == sprite) { already++; continue; }

                var so = new SerializedObject(def);
                var prop = so.FindProperty("icon");
                if (prop == null) continue;
                prop.objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                assigned++;
            }

            if (assigned > 0) AssetDatabase.SaveAssets();

            string summary =
                $"[ItemIconSync] assigned={assigned}  already-ok={already}  no-png={noIcon}" +
                (misses.Count > 0 ? "\nMissing PNG for:\n  " + string.Join("\n  ", misses) : "");
            Debug.Log(summary);
        }

        /// <summary>Tries the exact id, then a few friendly variants (odds are
        /// the artist named the file by hand).</summary>
        private static Sprite LoadIcon(string itemId)
        {
            string[] candidates =
            {
                itemId,
                itemId.Replace("item_", ""),
                itemId.Replace("gitem_", ""),
            };
            foreach (var c in candidates)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconDir}/{c}.png");
                if (s != null) return s;
            }
            return null;
        }
    }
}
