// Assets/Scripts/VoxelEngine/Core/GameVersion.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                IndustrialWorld — VERSION CONSTANTS                ║
// ║                                                                  ║
// ║  THE single source of truth for the build's version. Read from   ║
// ║  any system (console banner, main-menu footer, save files…) so   ║
// ║  every surface always reports the exact same number.             ║
// ║                                                                  ║
// ║  Semantic Versioning 2.0.0 (https://semver.org):                 ║
// ║                                                                  ║
// ║      MAJOR . MINOR . PATCH                                        ║
// ║                                                                  ║
// ║   • MAJOR — breaking changes that require a fresh save           ║
// ║             (new chunk format, new save schema, removed core     ║
// ║             systems). Bumping this means old saves won't load.   ║
// ║   • MINOR — a new system / feature that IS save-compatible       ║
// ║             (a new block, a new tool, a new UI panel).           ║
// ║   • PATCH — bug fixes, balance tweaks, visual polish that don't  ║
// ║             touch save data or any public API.                   ║
// ║                                                                  ║
// ║  Pre-release / build metadata is appended with a hyphen, e.g.    ║
// ║      1.4.7-dev   for an in-progress dev build of 1.4.7.          ║
// ║                                                                  ║
// ║  ▶ Bump the three constants below whenever you cut a new commit  ║
// ║    so the console / main-menu / save files all stay in sync.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Single source of truth for the build's semantic version. Read this from
    /// any system that wants to display, log, or persist the version string.
    /// </summary>
    public static class GameVersion
    {
        // ── Bump these when you ship ──────────────────────────────────────
        public const int    Major = 9;
        public const int    Minor = 21;
        public const int    Patch = 0;

        /// <summary>
        /// Channel suffix appended after a hyphen. Use "" for a stable release,
        /// "dev" / "dev.N" for in-progress builds, "rc.N" for release candidates.
        /// </summary>
        public const string Channel = "dev";

        // ── Derived accessors (don't edit) ────────────────────────────────

        /// <summary>Bare "MAJOR.MINOR.PATCH" string, e.g. "1.4.7".</summary>
        public static string Full => $"{Major}.{Minor}.{Patch}";

        /// <summary>
        /// Full human-readable version with channel suffix, e.g. "1.4.7-dev"
        /// (or just "1.4.7" on a stable release).
        /// </summary>
        public static string Display =>
            string.IsNullOrEmpty(Channel) ? Full : $"{Full}-{Channel}";

        /// <summary>UI-friendly version with a leading "v", e.g. "v1.4.7-dev".</summary>
        public static string DisplayShort => $"v{Display}";

        /// <summary>
        /// Packed integer (MAJOR*10000 + MINOR*100 + PATCH) for fast, allocation-free
        /// comparisons — handy when validating save-file compatibility.
        /// </summary>
        public static int Numeric => Major * 10000 + Minor * 100 + Patch;

        /// <summary>Logged once at startup so the console always shows the build.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LogVersion()
        {
            Debug.Log($"[IndustrialWorld] ✓ Game version {Display} — assembly loaded successfully.");
        }
    }
}
