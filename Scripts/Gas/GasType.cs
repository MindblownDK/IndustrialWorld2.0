// Assets/Scripts/VoxelEngine/Gas/GasType.cs
namespace VoxelEngine.Gas
{
    /// <summary>Types of gas that can flow through gas pipes and be stored in gas tanks.</summary>
    public enum GasType
    {
        None = 0,
        Steam = 1,      // from boiling water in reactor
        Hydrogen = 2,   // from electrolysis of ice
        Oxygen = 3,     // from electrolysis of ice
    }
}
