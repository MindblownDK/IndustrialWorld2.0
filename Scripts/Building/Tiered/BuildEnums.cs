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
        HalfWall   = 8
    }

    /// <summary>
    /// Side of a 1-meter cell where a build can attach. Used by BuildSocket.
    /// </summary>
    public enum SocketSide { Top, Bottom, North, South, East, West, Center }
}
