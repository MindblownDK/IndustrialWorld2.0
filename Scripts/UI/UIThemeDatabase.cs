// Assets/Scripts/VoxelEngine/UI/UIThemeDatabase.cs
// Holds references to all 10 built-in theme definition assets and provides lookup.
// Used by setup wizard and runtime managers.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.UI
{
    [CreateAssetMenu(menuName = "Voxel Engine/UI/Theme Database", fileName = "UIThemeDatabase")]
    public class UIThemeDatabase : ScriptableObject
    {
        [Tooltip("All built-in theme definitions (10 expected). Order should match BuiltInUITheme enum.")]
        public List<UIThemeDefinition> themes = new();

        public UIThemeDefinition Get(BuiltInUITheme theme)
        {
            if (themes == null) return null;
            foreach (var t in themes)
                if (t != null && t.builtInTheme == theme) return t;
            // Fallback: try index
            int idx = (int)theme;
            if (idx >= 0 && idx < themes.Count) return themes[idx];
            return null;
        }

        public static UIThemeDatabase Load()
        {
            var db = Resources.Load<UIThemeDatabase>("UIThemeDatabase");
            if (db != null) return db;

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:UIThemeDatabase");
            foreach (var g in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeDatabase>(path);
                if (asset != null) return asset;
            }
#endif
            return null;
        }
    }
}
