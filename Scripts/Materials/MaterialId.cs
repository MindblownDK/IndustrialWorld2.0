// Assets/Scripts/VoxelEngine/Materials/MaterialId.cs
namespace VoxelEngine.Materials
{
    /// <summary>
    /// Byte-sized material identifier. ORDER MATTERS — values are serialized in voxels.
    /// Add NEW materials at the END only. Up to 255 materials supported.
    /// </summary>
    public enum MaterialId : byte
    {
        Air        = 0,
        Stone      = 1,
        Sand       = 2,
        Clay       = 3,
        Ice        = 4,
        WaterVoxel = 5,   // solid voxel form (frozen lake / underground reservoir)
        WaterLiquid= 6,   // liquid simulation (treat as non-mineable fluid)
        Iron       = 7,
        Copper     = 8,
        Coal       = 9,
        Nickel     = 10,
        Silicon    = 11,
        Cobalt     = 12,
        Silver     = 13,
        Gold       = 14,
        Magnesium  = 15,
        Platinum   = 16,
        Uranium    = 17,
        CrudeOil   = 18,
        Wood       = 19,

        // Reserved legacy value. New worlds no longer generate an unbreakable
        // floor material; old value-20 voxels are treated as normal stone.
        LegacySolidFloor = 20,
        Lithium    = 21,
        Grass      = 22,   // green surface grass (Plains/Forest top layer)

        // === Celestial world surface materials (Phase 2) ===
        MartianDust    = 23,
        VenusAsh       = 24,
        AcidBog        = 25,
        VolcanicBasalt = 26,
        CrystalGeode   = 27,

        // === Add custom materials below this line ===
    }
}
