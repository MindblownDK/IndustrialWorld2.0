// Assets/Scripts/VoxelEngine/Crafting/MachineFluidStore.cs
//
// IFluidStore backed by a machine's own list of MachineFluidTanks. Used by the
// stationary Oil Refinery and Chemical Plant.

using System.Collections.Generic;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    public class MachineFluidStore : IFluidStore
    {
        private readonly IReadOnlyList<MachineFluidTank> _tanks;
        public MachineFluidStore(IReadOnlyList<MachineFluidTank> tanks) { _tanks = tanks; }

        public float Available(LiquidType type)
        {
            float n = 0f;
            foreach (var t in _tanks) if (t != null && t.liquid == type) n += t.stored;
            return n;
        }

        public float SpaceFor(LiquidType type)
        {
            float n = 0f;
            foreach (var t in _tanks) if (t != null) n += t.SpaceFor(type);
            return n;
        }

        public float Draw(LiquidType type, float litres)
        {
            float drawn = 0f;
            foreach (var t in _tanks)
            {
                if (drawn >= litres) break;
                if (t != null && t.liquid == type) drawn += t.Remove(litres - drawn);
            }
            return drawn;
        }

        public float Fill(LiquidType type, float litres)
        {
            float filled = 0f;
            foreach (var t in _tanks)
            {
                if (filled >= litres) break;
                if (t != null) filled += t.Add(type, litres - filled);
            }
            return filled;
        }
    }
}
