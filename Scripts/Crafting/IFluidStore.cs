// Assets/Scripts/VoxelEngine/Crafting/IFluidStore.cs
//
// Abstraction over a place fluids can be drawn from / pushed into, so the same
// recipe executor works for stationary machines (MachineFluidTank list) and
// grid machines (connected GridLiquidTank network).

using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    public interface IFluidStore
    {
        /// <summary>Total litres of a given liquid available to draw.</summary>
        float Available(LiquidType type);
        /// <summary>Total litres of free space able to accept a given liquid.</summary>
        float SpaceFor(LiquidType type);
        /// <summary>Draw up to <paramref name="litres"/>. Returns litres actually drawn.</summary>
        float Draw(LiquidType type, float litres);
        /// <summary>Push up to <paramref name="litres"/>. Returns litres actually stored.</summary>
        float Fill(LiquidType type, float litres);
    }
}
