// Assets/Scripts/VoxelEngine/Networks/PowerTier.cs
namespace VoxelEngine.Networks
{
    /// <summary>Power cable tiers. Connecting high → low without a transformer = short circuit.</summary>
    public enum PowerTier
    {
        Low    = 0,  // 1,000 W
        Medium = 1,  // 10,000 W
        High   = 2   // 100,000 W
    }

    public static class PowerTierExt
    {
        public static float MaxWatts(this PowerTier t) => t switch
        {
            PowerTier.Low    => 1_000f,
            PowerTier.Medium => 10_000f,
            PowerTier.High   => 100_000f,
            _ => 1_000f
        };
    }
}
