// Assets/Scripts/VoxelEngine/Thermal/IHeatshieldBlock.cs
//
// Marker for blocks that ablate or reflect atmospheric entry heat. Implemented by
// GridHeatshield, but left as an interface so any future block (an ablative nose
// cone, a deployable shield) can shelter a hull without subclassing.

namespace VoxelEngine.Thermal
{
    public interface IHeatshieldBlock
    {
        /// <summary>Fraction of incident entry heat this block lets through (0..1).</summary>
        float HeatTransmission { get; }

        /// <summary>False once the shield has ablated away and no longer protects.</summary>
        bool ShieldIntact { get; }
    }
}
