// Assets/Scripts/VoxelEngine/Exploration/BlueprintUnlockManager.cs
//
// Ruins & Blueprint Unlock System (4.9.0)
// Rare ruined structures contain Damaged Blueprint Data Cores that unlock
// recipes gated behind exploration (e.g. wind turbine nacelle, gearbox).
// This manager persists unlocked blueprint recipes and integrates with
// ResearchManager.IsRecipeUnlocked check.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoxelEngine.Crafting;

namespace VoxelEngine.Exploration
{
    public class BlueprintUnlockManager : MonoBehaviour
    {
        public static BlueprintUnlockManager Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("BlueprintUnlockManager");
            Instance = go.AddComponent<BlueprintUnlockManager>();
            DontDestroyOnLoad(go);
            Instance.Load();
        }

        private readonly HashSet<string> _unlockedRecipeIds = new();
        private const string FileName = "blueprint_unlocks.json";

        [Serializable]
        private class SaveFile
        {
            public List<string> unlocked = new();
        }

        private string SavePath
        {
            get
            {
                var session = Menu.WorldSession.Instance;
                if (session == null) return null;
                string folder = session.WorldFolderPath(session.worldName);
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, FileName);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        public bool IsUnlocked(string recipeAssetName)
        {
            if (string.IsNullOrEmpty(recipeAssetName)) return false;
            return _unlockedRecipeIds.Contains(recipeAssetName);
        }

        public bool IsUnlocked(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            return _unlockedRecipeIds.Contains(recipe.name);
        }

        public bool Unlock(string recipeAssetName)
        {
            if (string.IsNullOrEmpty(recipeAssetName)) return false;
            if (_unlockedRecipeIds.Contains(recipeAssetName)) return false;
            _unlockedRecipeIds.Add(recipeAssetName);
            Save();
            Debug.Log($"[Blueprint] Unlocked recipe: {recipeAssetName}");
            VoxelEngine.UI.BuildFeedbackHud.Show("Blueprint Restored", $"{recipeAssetName} unlocked", null, new Color(0.45f, 0.85f, 1f));
            return true;
        }

        public bool Unlock(RecipeDefinition recipe)
        {
            if (recipe == null) return false;
            return Unlock(recipe.name);
        }

        public void Save()
        {
            try
            {
                string path = SavePath;
                if (string.IsNullOrEmpty(path)) return;
                var file = new SaveFile { unlocked = new List<string>(_unlockedRecipeIds) };
                string json = JsonUtility.ToJson(file, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex) { Debug.LogWarning("[Blueprint] Save failed: " + ex.Message); }
        }

        public void Load()
        {
            try
            {
                string path = SavePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string json = File.ReadAllText(path);
                var file = JsonUtility.FromJson<SaveFile>(json);
                if (file?.unlocked == null) return;
                _unlockedRecipeIds.Clear();
                foreach (var id in file.unlocked)
                    if (!string.IsNullOrEmpty(id)) _unlockedRecipeIds.Add(id);
                Debug.Log($"[Blueprint] Loaded { _unlockedRecipeIds.Count } unlocked blueprint recipes");
            }
            catch (Exception ex) { Debug.LogWarning("[Blueprint] Load failed: " + ex.Message); }
        }

        public void Clear()
        {
            _unlockedRecipeIds.Clear();
            Save();
        }
    }
}
