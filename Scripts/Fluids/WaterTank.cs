// Assets/Scripts/VoxelEngine/Fluids/WaterTank.cs
// Kept for nuclear reactor, sprinkler, and steam turbine compatibility.
using UnityEngine;

namespace VoxelEngine.Fluids
{
    public class WaterTank : FluidNode
    {
        public override FluidNodeKind Kind => FluidNodeKind.Tank;

        public float capacityLitres = 1000f;
        public float water = 0f;
        public bool isGlass = false;

        public float Fill01 => capacityLitres > 0 ? Mathf.Clamp01(water / capacityLitres) : 0f;

        public bool TryAdd(float litres)
        {
            float space = capacityLitres - water;
            if (litres > space) return false;
            water += litres;
            return true;
        }

        public float AddSome(float litres)
        {
            float space = capacityLitres - water;
            float add = Mathf.Min(space, litres);
            water += add;
            return add;
        }

        public float TakeSome(float litres)
        {
            float take = Mathf.Min(water, litres);
            water -= take;
            return take;
        }
    }
}
