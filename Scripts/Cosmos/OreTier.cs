// Assets/Scripts/VoxelEngine/Cosmos/OreTier.cs
namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Stratification of mineable deposits. Two gameplay tiers plus specials.
    /// Mirrors the existing PlanetSettings split (common sub-surface vs rare deep-core)
    /// but makes it a first-class, data-driven concept shared by every celestial body.
    /// </summary>
    public enum OreTier
    {
        /// <summary>Common, shallow-to-mid crust deposits (Iron, Copper, Coal, …).</summary>
        SubSurface = 0,

        /// <summary>Rare, deep deposits requiring advanced mining (Silver, Gold, Platinum, Uranium).</summary>
        DeepCore = 1,

        /// <summary>Non-standard fluids/solids: crude oil, ice, etc.</summary>
        Special = 2,
    }
}
