// Assets/Scripts/VoxelEngine/Simulation/IPowerConsumer.cs
//
// Lightweight read interface for the simulation layer to query power state
// without depending on the full PowerNode class hierarchy.

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Exposes the powered/unpowered state so the simulation tick loop can
    /// gate machine processing without casting to PowerConsumer.
    /// </summary>
    public interface IPowerConsumer
    {
        /// <summary>True when the power network is supplying enough watts.</summary>
        bool IsPowered { get; }

        /// <summary>Watts per second this block currently draws.</summary>
        float WattsPerSecond { get; set; }
    }
}
