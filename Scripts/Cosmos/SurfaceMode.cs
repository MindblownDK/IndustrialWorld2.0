// Assets/Scripts/VoxelEngine/Cosmos/SurfaceMode.cs
namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// How a body's surface is composed. Drives the radial density generator (Phase 1+)
    /// so designers can author exotic worlds (ocean worlds, oil worlds) from the inspector.
    /// </summary>
    public enum SurfaceMode
    {
        /// <summary>Normal terrain — solid crust, optional oceans, biomes as authored.</summary>
        SolidSurface = 0,

        /// <summary>No dry land: a liquid shell on top with a solid (mineable) crust underneath.</summary>
        WaterOnlyWithSubsurface = 1,

        /// <summary>The whole surface ocean is crude oil instead of water (e.g. Titan-like).</summary>
        OilOnly = 2,
    }
}
