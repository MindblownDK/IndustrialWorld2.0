// Assets/Scripts/VoxelEngine/Pressure/IAirtightBlock.cs
//
// Sealing contract used by the Pressure & Airtight Service. Any grid block can opt
// into (or out of) sealing a room. Blocks that do NOT implement this interface fall
// back to PressureRules.DefaultSeals(), so existing ships keep working untouched.

namespace VoxelEngine.Pressure
{
    /// <summary>Implemented by blocks whose sealing state can change at runtime
    /// (airtight doors, hatches, retractable hangar shields).</summary>
    public interface IAirtightBlock
    {
        /// <summary>True while this block forms an airtight wall for room detection.</summary>
        bool SealsAir { get; }
    }
}
