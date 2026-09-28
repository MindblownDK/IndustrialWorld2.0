// Assets/Scripts/Editor/Networking/NetworkSetup.cs
//
// 14.1.0-dev - Step 105: Multiplayer bootstrap.
//
// Authors everything the Fish-Net bridge needs to run:
//   1. The NetworkPlayerAvatar prefab (NetworkObject + NetworkTransform +
//      PlayerAvatar, capsule body, visor, nameplate) under
//      VoxelEngineAssets/Networking.
//   2. A "Network" object in the OPEN scene carrying NetworkManager, the
//      Tugboat transport and NetworkBootstrap, with the avatar prefab wired.
//
// Non-destructive and re-runnable: existing assets and scene objects are
// repaired and connected, never replaced. Run this in the main game scene.
// Fish-Net regenerates its DefaultPrefabObjects collection automatically
// when the prefab is created, so no manual registration is needed.

#if UNITY_EDITOR
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoxelEngine.Networking;

namespace VoxelEngine.EditorTools
{
    public static class NetworkSetup
    {
        private const string AssetRoot     = "Assets/VoxelEngineAssets";
        private const string NetFolder     = AssetRoot + "/Networking";
        private const string PrefabPath    = NetFolder + "/NetworkPlayerAvatar.prefab";
        private const string BodyMatPath   = NetFolder + "/M_AvatarBody.mat";
        private const string VisorMatPath  = NetFolder + "/M_AvatarVisor.mat";

        private static readonly Color BodyColor  = new(0.30f, 0.42f, 0.30f);   // hazmat green, matches the Code Lock family
        private static readonly Color VisorColor = new(0.18f, 0.72f, 0.88f);   // cyan glass

        public static void RunStep105()
        {
            EnsureFolder(NetFolder);

            var bodyMat  = EnsureMaterial(BodyMatPath, BodyColor);
            var visorMat = EnsureMaterial(VisorMatPath, VisorColor);
            var prefabNob = EnsureAvatarPrefab(bodyMat, visorMat);
            if (prefabNob == null) return;

            bool sceneTouched = EnsureSceneNetworkObject(prefabNob);

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Voxel Engine - Step 105",
                "Multiplayer bootstrap is ready:\n" +
                "  \u2022 NetworkPlayerAvatar prefab authored/repaired\n" +
                "  \u2022 'Network' object in this scene carries NetworkManager + Tugboat + NetworkBootstrap\n" +
                (sceneTouched ? "\nThe scene was modified - save it (Ctrl+S)." : "\nScene was already wired."),
                "OK");
        }

        // ─────────────────────────── avatar prefab ───────────────────────────

        private static NetworkObject EnsureAvatarPrefab(Material bodyMat, Material visorMat)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                RepairPrefab();
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<NetworkObject>();
            }

            var root = new GameObject("NetworkPlayerAvatar");
            try
            {
                root.AddComponent<NetworkObject>();
                root.AddComponent<NetworkTransform>();
                var avatar = root.AddComponent<PlayerAvatar>();

                // Body: player-sized capsule, pivot at the feet like the real rig.
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.925f, 0f);
                body.transform.localScale = new Vector3(0.70f, 0.925f, 0.70f);   // 1.85 m tall, matches PlayerController
                body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

                // Visor: shows which way the player is looking.
                var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visor.name = "Visor";
                Object.DestroyImmediate(visor.GetComponent<Collider>());
                visor.transform.SetParent(root.transform, false);
                visor.transform.localPosition = new Vector3(0f, 1.55f, 0.28f);
                visor.transform.localScale = new Vector3(0.38f, 0.12f, 0.10f);
                visor.GetComponent<MeshRenderer>().sharedMaterial = visorMat;

                // Nameplate: billboarded by PlayerAvatar at runtime.
                var plateGo = new GameObject("Nameplate");
                plateGo.transform.SetParent(root.transform, false);
                plateGo.transform.localPosition = new Vector3(0f, 2.25f, 0f);
                var plate = plateGo.AddComponent<TextMesh>();
                plate.text = "Player";
                plate.characterSize = 0.12f;
                plate.fontSize = 64;
                plate.anchor = TextAnchor.LowerCenter;
                plate.alignment = TextAlignment.Center;
                plate.color = new Color(0.92f, 0.94f, 0.97f);
                avatar.nameplate = plate;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return saved != null ? saved.GetComponent<NetworkObject>() : null;
        }

        /// <summary>Reconnects missing components/references on an existing
        /// prefab without touching anything that is already set up. Missing
        /// scripts (e.g. after a Fish-Net reimport changed script GUIDs) are
        /// stripped first - Unity refuses to save a prefab containing them -
        /// and the required components are re-added right after.</summary>
        private static void RepairPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                bool dirty = RemoveMissingScripts(root);
                if (root.GetComponent<NetworkObject>() == null) { root.AddComponent<NetworkObject>(); dirty = true; }
                if (root.GetComponent<NetworkTransform>() == null) { root.AddComponent<NetworkTransform>(); dirty = true; }
                var avatar = root.GetComponent<PlayerAvatar>();
                if (avatar == null) { avatar = root.AddComponent<PlayerAvatar>(); dirty = true; }
                if (avatar.nameplate == null)
                {
                    avatar.nameplate = root.GetComponentInChildren<TextMesh>(true);
                    if (avatar.nameplate != null) dirty = true;
                }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Removes every component whose script no longer resolves,
        /// on the object and all children. Returns true if any were removed.</summary>
        private static bool RemoveMissingScripts(GameObject root)
        {
            int removed = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (removed > 0)
                Debug.Log($"[NetworkSetup] Removed {removed} missing-script component(s) from '{root.name}' and re-added the required ones.");
            return removed > 0;
        }

        // ─────────────────────────── scene wiring ───────────────────────────

        private static bool EnsureSceneNetworkObject(NetworkObject prefabNob)
        {
            bool dirty = false;

            // Prefer the real component; fall back to the object by name so a
            // GUID breakage never leaves a broken 'Network' object behind and
            // a duplicate beside it.
            var manager = Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
            GameObject go = manager != null ? manager.gameObject : GameObject.Find("Network");
            if (go == null)
            {
                go = new GameObject("Network");
                dirty = true;
            }

            if (RemoveMissingScripts(go)) dirty = true;
            if (go.GetComponent<NetworkManager>() == null) { go.AddComponent<NetworkManager>(); dirty = true; }
            if (go.GetComponent<Tugboat>() == null) { go.AddComponent<Tugboat>(); dirty = true; }

            var bootstrap = go.GetComponent<NetworkBootstrap>();
            if (bootstrap == null) { bootstrap = go.AddComponent<NetworkBootstrap>(); dirty = true; }
            if (bootstrap.avatarPrefab == null && prefabNob != null)
            {
                bootstrap.avatarPrefab = prefabNob;
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            return dirty;
        }

        // ─────────────────────────── helpers ───────────────────────────

        private static Material EnsureMaterial(string path, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;   // never repaint an existing material

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
#endif
