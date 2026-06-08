// Assets/Scripts/VoxelEngine/Core/BuildVersion.cs
namespace VoxelEngine.Core
{
    /// <summary>
    /// Single source of truth for the project's semantic version.
    /// Follows Semantic Versioning 2.0.0 — MAJOR.MINOR.PATCH.
    ///
    ///   MAJOR — breaking changes (new chunk format, new save schema, removed core systems)
    ///   MINOR — new save-compatible system / feature
    ///   PATCH — bug fixes, balance tweaks, visual polish
    ///
    /// Bump this whenever <c>CHANGELOG.md</c> gets a new entry.
    /// </summary>
    public static class BuildVersion
    {
        public const int Major = 0;
        public const int Minor = 6;
        public const int Patch = 3;

        public static string Full => $"{Major}.{Minor}.{Patch}";
        public static string Display => $"v{Full}";
    }
}
