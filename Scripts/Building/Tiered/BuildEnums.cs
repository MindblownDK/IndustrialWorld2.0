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
        StationDome      = 17,

        // ── Openings and access (13.4.0-dev) ──
        // Appended for the same reason the station set was: a save stores the
        // family as an int, so nothing already placed may be renumbered.
        /// <summary>Wide wall cutout that accepts a Garage Door or double doors.</summary>
        WallFrame  = 18,
        /// <summary>Roll-up shutter that fits a Wall Frame.</summary>
        GarageDoor = 19,
        /// <summary>Floor slab with a square opening. Takes a Hatch Lid.</summary>
        FloorHatch = 20,

        // ── Fittings (13.5.0-dev) ──
        // Openings hold their fitting rather than being born with it, exactly as a
        // Doorway holds a Door: the frame is one build, the thing that closes it
        // is another, and either can be upgraded or replaced on its own.
        /// <summary>Glazed pane that fits a Window frame.</summary>
        WindowPane = 21,
        /// <summary>Hinged lid with a fold-out ladder that fits a Floor Hatch.</summary>
        HatchLid = 22
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
        // Explicit range, not a "greater than" test: structural families are
        // appended after the station block, so an open-ended comparison would
        // silently file every new opening piece under Orbital Station.
        public static BuildFamilyGroup GroupOf(BuildFamily family)
            => family >= BuildFamily.StationHull && family <= BuildFamily.StationDome
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
            BuildFamily.WallFrame       => "WALL FRAME",
            BuildFamily.GarageDoor      => "GARAGE DOOR",
            BuildFamily.FloorHatch      => "FLOOR HATCH",
            BuildFamily.WindowPane      => "WINDOW PANE",
            BuildFamily.HatchLid        => "HATCH LID",
            _ => family.ToString().ToUpperInvariant(),
        };

        /// <summary>
        /// One line of flavour shown in the build wheel's hub while a piece is
        /// hovered. Kept here rather than on the ScriptableObject so a family
        /// always reads correctly even before its asset has been authored.
        /// </summary>
        public static string Description(BuildFamily family) => family switch
        {
            BuildFamily.Foundation      => "Every base starts with a foundation",
            BuildFamily.Wall            => "Keeps the weather and the wildlife out",
            BuildFamily.Floor           => "A ceiling below, a walkway above",
            BuildFamily.Doorway         => "A wall with a way through it",
            BuildFamily.Door            => "Fits a doorway and closes behind you",
            BuildFamily.Window          => "Daylight in, nothing else",
            BuildFamily.Stairs          => "The civilised way to the next storey",
            BuildFamily.Roof            => "Sheds the rain and caps the build",
            BuildFamily.Pillar          => "Carries the load for almost nothing",
            BuildFamily.HalfWall        => "Waist-high cover you can work over",
            BuildFamily.StationHull     => "Pressure-rated shell plating",
            BuildFamily.StationFloor    => "Decking with a magnetic tread",
            BuildFamily.StationCorridor => "Sealed run between two modules",
            BuildFamily.StationJunction => "Where four corridors meet",
            BuildFamily.StationWindow   => "Reinforced viewport onto the void",
            BuildFamily.StationAirlock  => "Two doors, never open at once",
            BuildFamily.StationDock     => "Open collar a ship mates into",
            BuildFamily.StationDome     => "A curved roof for the observation deck",
            BuildFamily.WallFrame       => "A wide opening a vehicle fits through",
            BuildFamily.GarageDoor      => "Rolls up into the drum above the frame",
            BuildFamily.FloorHatch      => "An opening down. Fit a lid to close it",
            BuildFamily.WindowPane      => "Glazes a window frame. Fit it yourself",
            BuildFamily.HatchLid        => "Folds open and drops a ladder through",
            _ => "Construction piece",
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
