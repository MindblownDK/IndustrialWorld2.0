// Assets/Scripts/VoxelEngine/Building/Tiered/BuildEnums.cs
namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// Tier of a placed block. Higher tier = more HP, requires higher-tier pickaxe to break.
    /// Order matters; do NOT reorder.
    /// </summary>
    public enum BuildTier { Wood = 0, Stone = 1, Iron = 2, Steel = 3 }

    /// <summary>
    /// Family of a placed block. Each family has 4 tier prefabs.
    /// </summary>
    public enum BuildFamily
    {
        Foundation = 0,
        Wall       = 1,
        Doorway    = 2,
        Window     = 3,
        Floor      = 4,
        Stairs     = 5,
        Roof       = 6,
        Pillar     = 7,
        HalfWall   = 8,
        Door       = 9,

        // ── Orbital Station family (11.22.0-dev) ──
        // APPENDED, never inserted: a save stores this as an int, so every piece a player
        // has already placed keeps its meaning. These are pressurised habitat pieces that
        // only appear on the hammer wheel once Orbital Construction is researched.
        StationHull      = 10,
        StationFloor     = 11,
        StationCorridor  = 12,
        StationJunction  = 13,
        StationWindow    = 14,
        StationAirlock   = 15,
        StationDock      = 16,
        StationDome      = 17
    }

    /// <summary>
    /// Which hammer family group a build family belongs to. The wheel shows one group at
    /// a time, so the station set does not bury the everyday building pieces under two
    /// extra pages the moment it unlocks.
    /// </summary>
    public enum BuildFamilyGroup
    {
        Structural = 0,
        OrbitalStation = 1,
    }

    public static class BuildFamilyInfo
    {
        /// <summary>Station pieces are gated behind research; structural pieces never are.</summary>
        public static BuildFamilyGroup GroupOf(BuildFamily family)
            => family >= BuildFamily.StationHull
                ? BuildFamilyGroup.OrbitalStation
                : BuildFamilyGroup.Structural;

        /// <summary>
        /// Research node that unlocks a group, or null when it needs none. Matched by id so
        /// the enum does not have to hold an asset reference.
        /// </summary>
        public static string RequiredResearchId(BuildFamilyGroup group)
            => group == BuildFamilyGroup.OrbitalStation ? "orbital_construction" : null;

        /// <summary>
        /// True when a station piece seals a room. Every station family is pressurised
        /// except the dock frame, which is an open collar a ship mates into.
        /// </summary>
        public static bool SealsPressure(BuildFamily family)
            => GroupOf(family) == BuildFamilyGroup.OrbitalStation
               && family != BuildFamily.StationDock;

        public static string DisplayName(BuildFamily family) => family switch
        {
            BuildFamily.StationHull     => "HULL",
            BuildFamily.StationFloor    => "DECK",
            BuildFamily.StationCorridor => "CORRIDOR",
            BuildFamily.StationJunction => "JUNCTION",
            BuildFamily.StationWindow   => "VIEWPORT",
            BuildFamily.StationAirlock  => "AIRLOCK",
            BuildFamily.StationDock     => "DOCK",
            BuildFamily.StationDome     => "DOME",
            _ => family.ToString().ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Side of a modular construction piece where another piece can attach. Used by BuildSocket.
    /// </summary>
    public enum SocketSide
    {
        Top, Bottom, North, South, East, West, Center,
        TopNorth, TopSouth, TopEast, TopWest
    }
}
