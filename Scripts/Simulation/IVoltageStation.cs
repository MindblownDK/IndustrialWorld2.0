using UnityEngine;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Interface for any building that can be connected to the high voltage grid.
    /// Transmission Towers, Transformers, Substations, etc.
    /// </summary>
    public interface IVoltageStation
    {
        Vector3 ConnectionPoint { get; }
        Transform StationTransform { get; }
        bool CanConnectMore { get; }
        void AddConnection(IVoltageStation other);
        void AddConnection(IVoltageStation other, float capacity);
        void RemoveConnection(IVoltageStation other);
        
        // Power stats for UI
        float TotalProduced { get; }
        float TotalConsumed { get; }
        float MaxCapacity { get; }
        float CurrentPower { get; }
        bool IsHighVoltage { get; }
    }
}
