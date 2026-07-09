// Assets/Scripts/VoxelEngine/Simulation/IPowerProducer.cs
//
// Lightweight read interface for anything that feeds power into a network.

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Exposes how much power a generator is currently producing so the
    /// simulation tick loop and UI can read it without tight coupling.
    /// </summary>
    public interface IPowerProducer
    {
        /// <summary>Watts currently being fed into the power network.</summary>
        float CurrentOutput { get; }

        /// <summary>Maximum watts this producer can supply.</summary>
        float MaxOutput { get; }
    }
}
