// Assets/Scripts/VoxelEngine/Core/GameVersion.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                IndustrialWorld — VERSION CONSTANTS              ║
// ║                                                                  ║
// ║  Semantic Versioning 2.0.0 (https://semver.org):                ║
// ║                                                                  ║
// ║      MAJOR . MINOR . PATCH                                      ║
// ║                                                                  ║
// ║   • MAJOR — bumped when we ship a breaking change that requires ║
// ║             players to start a fresh save (new chunk format,    ║
// ║             new save schema, removed core systems, etc.).       ║
// ║   • MINOR — bumped when we add a new system or feature that is  ║
// ║             SAVE-COMPATIBLE with the previous version           ║
// ║             (a new block type, a new HUD panel, a new tool…).   ║
// ║   • PATCH — bumped for bug fixes, balance tweaks and visual     ║
// ║             polish that don't touch save data or systems.       ║
// ║                                                                  ║
// ║  Pre-release / build metadata is appended with a hyphen, e.g.   ║
// ║      0.4.0-dev.5   for a fifth in-progress dev build of 0.4.0.  ║
// ║                                                                  ║
// ║  Bump the constants here whenever you cut a new commit so the   ║
// ║  console / main-menu / save files all show the same version.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Single source of truth for the build's version string. Read from any
    /// system that wants to display or persist the version.
    /// </summary>
    public static class GameVersion
    {
        public const int    Major = 0;
        public const int    Minor = 4;
        public const int    Patch = 1;

        /// <summary>Optional channel suffix — "" for stable releases,
        /// "dev.N" for in-progress builds, "rc.N" for release candidates.</summary>
        public const string Channel = "dev";

        /// <summary>Human-readable "0.4.0-dev" / "1.0.0" string.</summary>
        public static string Display =>
            string.IsNullOrEmpty(Channel)
                ? $"{Major}.{Minor}.{Patch}"
                : $"{Major}.{Minor}.{Patch}-{Channel}";

        /// <summary>Logged once at startup so the console always shows the build.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LogVersion()
        {
            Debug.Log($"[IndustrialWorld] ✓ Game version {Display} — assembly loaded successfully.");
        }
    }
}
