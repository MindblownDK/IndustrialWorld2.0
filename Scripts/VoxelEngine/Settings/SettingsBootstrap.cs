// Assets/Scripts/VoxelEngine/Settings/SettingsBootstrap.cs
using UnityEngine;

namespace VoxelEngine.Settings
{
    /// <summary>
    /// Runs before any scene loads — applies persisted settings (quality, vsync,
    /// resolution, fullscreen mode, audio volume) at game startup.
    /// </summary>
    public static class SettingsBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            GameSettings.ApplyAll();
        }
    }
}
