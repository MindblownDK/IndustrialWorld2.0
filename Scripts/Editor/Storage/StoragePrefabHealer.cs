// Assets/Scripts/Editor/Storage/StoragePrefabHealer.cs
//
// 14.51.0 - the "External Storage lost its script again" guard.
//
// Root cause of the recurring loss: a script's identity in Unity is the
// GUID in its .meta file. When a .meta is not committed, every machine
// (and every fresh checkout) invents a NEW guid for the same script - and
// any prefab that referenced the old guid wakes up with a Missing Script.
// The lasting cure is committing the .meta files; this healer is the belt
// to that suspender: on every editor load it checks the storage prefabs
// that are known to carry runtime-added components, and quietly re-adds
// anything missing. Editor-only, non-destructive (create if missing,
// never touch what exists), and silent when there is nothing to do.

using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    [InitializeOnLoad]
    public static class StoragePrefabHealer
    {
        private const string ExternalStoragePrefab =
            "Assets/VoxelEngineAssets/Storage/Network/Prefabs/ExternalStorage.prefab";

        static StoragePrefabHealer()
        {
            // Delay one tick: the asset database is not ready inside the
            // InitializeOnLoad constructor itself.
            EditorApplication.delayCall += HealAll;
        }

        private static void HealAll()
        {
            Heal<VoxelEngine.Storage.ExternalStorageBlock>(ExternalStoragePrefab);
        }

        /// <summary>Re-add component T to the prefab at path when it is
        /// missing - including the "Missing Script" case, whose dead
        /// reference is removed first so the prefab ends up clean.</summary>
        private static void Heal<T>(string path) where T : Component
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return;                 // setup never ran here - nothing to heal
            if (asset.GetComponent<T>() != null) return;   // healthy

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int dead = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                if (root.GetComponent<T>() == null) root.AddComponent<T>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[StorageHealer] '{path}' was missing {typeof(T).Name} " +
                          $"(removed {dead} dead script slot{(dead == 1 ? "" : "s")}) - re-attached. " +
                          "Commit the script's .meta file to stop this from recurring.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
