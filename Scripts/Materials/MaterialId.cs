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

        // === Liquid overhaul (9.16.0) — save-compatible new fluid materials ===
        // Old saves only ever contain WaterLiquid (6) and CrudeOil (18); these new
        // values simply cannot exist in them, so the format is untouched.
        RefinedOilLiquid      = 28,   // amber refined product — lighter than water
        LiquidFuelLiquid      = 29,   // bright volatile fuel — lightest of all
        HeavyFuelOilLiquid    = 30,   // dark tar-like bunker fuel — dense, viscous
        MarineGasOilLiquid    = 31,   // pale distillate — light, thin
        CoolantLiquid         = 32,   // glowing engine coolant — slightly denser than water

        // === Add custom materials below this line ===
    }
}
