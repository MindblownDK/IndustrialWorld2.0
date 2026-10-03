#if UNITY_EDITOR
// Assets/Scripts/Editor/Networking/DedicatedServerTools.cs
//
// 14.45.0-dev - Milestone 12, part 1: editor conveniences for the dedicated
// server. No assets are authored here (nothing to author - the server is
// pure code), so this is a plain menu, not a setup-wizard step.
//
//   Tools -> Voxel Engine -> Dedicated Server -> Test In Play Mode
//       Sets IW_DEDICATED=1 for THIS editor process and enters play mode:
//       the main menu resolves server_config.json, skips its UI and boots
//       the configured world headless (local player stripped, server-only
//       FishNet). Join it from a second editor/build at localhost. The flag
//       is cleared automatically when play mode ends, so the next normal
//       play session is a normal play session.
//
//   Tools -> Voxel Engine -> Dedicated Server -> Write Template Config
//       Creates server_config.json next to the project (or shows the one
//       that already exists).

using System;
using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    [InitializeOnLoad]
    public static class DedicatedServerTools
    {
        private const string EnvFlag = "IW_DEDICATED";

        static DedicatedServerTools()
        {
            // Environment variables survive domain reloads (same process),
            // so the test flag MUST be swept when play mode ends or every
            // later play session would boot headless.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode &&
                    System.Environment.GetEnvironmentVariable(EnvFlag) == "1")
                {
                    System.Environment.SetEnvironmentVariable(EnvFlag, null);
                    Debug.Log("[Server] Dedicated test flag cleared - the next play session is a normal one.");
                }
            };
        }

        [MenuItem("Tools/Voxel Engine/Dedicated Server/Test In Play Mode")]
        private static void TestInPlayMode()
        {
            string path = VoxelEngine.Networking.DedicatedServer.WriteTemplateConfig();
            System.Environment.SetEnvironmentVariable(EnvFlag, "1");
            Debug.Log($"[Server] Dedicated test armed (config: {path}). Entering play mode - " +
                      "open a second editor or a player build and join localhost to test.");
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/Voxel Engine/Dedicated Server/Test In Play Mode", validate = true)]
        private static bool TestInPlayModeValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Tools/Voxel Engine/Dedicated Server/Write Template Config")]
        private static void WriteTemplate()
        {
            string path = VoxelEngine.Networking.DedicatedServer.WriteTemplateConfig();
            EditorUtility.RevealInFinder(path);
            Debug.Log($"[Server] Config: {path}");
        }
    }
}
#endif
