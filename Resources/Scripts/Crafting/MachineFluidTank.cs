// Assets/Scripts/VoxelEngine/Crafting/MachineFluidTank.cs
//
// A simple internal fluid buffer used by stationary processing machines
// (Oil Refinery, Chemical Plant). Each tank carries one configurable liquid
// type up to a litre capacity. Mirrors the grid GridLiquidTank behaviour so
// fluid recipes work identically on the ground and on a ship.

using System;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    [Serializable]
    public class MachineFluidTank
    {
        public string     label = "Fluid";
        public LiquidType  liquid = LiquidType.Water;
        public float       capacity = 1000f;
        public float       stored;

        [Tooltip("If true the liquid type auto-locks to whatever first fills it (true for input tanks).")]
        public bool autoType = true;

        public MachineFluidTank() { }
        public MachineFluidTank(string label, float capacity, LiquidType liquid = LiquidType.Water, bool autoType = true)
        {
            this.label = label; this.capacity = capacity; this.liquid = liquid; this.autoType = autoType;
        }

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(stored / capacity) : 0f;
        public bool  IsEmpty => stored <= 0.001f;

        /// <summary>How much of a given liquid this tank can still accept.</summary>
        public float SpaceFor(LiquidType type)
        {
            if (!IsEmpty && liquid != type) return 0f;     // mismatched liquid
            return Mathf.Max(0f, capacity - stored);
        }

        /// <summary>Add liquid (auto-adopts type if empty + autoType). Returns litres accepted.</summary>
        public float Add(LiquidType type, float litres)
        {
            if (litres <= 0) return 0f;
            if (IsEmpty && autoType) liquid = type;
            if (liquid != type) return 0f;
            float take = Mathf.Min(capacity - stored, litres);
            stored += take;
            return take;
        }

        public bool Has(LiquidType type, float litres) => liquid == type && stored >= litres;

        /// <summary>Remove up to <paramref name="litres"/>. Returns litres drawn.</summary>
        public float Remove(float litres)
        {
            float take = Mathf.Min(stored, Mathf.Max(0f, litres));
            stored -= take;
            return take;
        }

        public void Drain() => stored = 0f;
    }
}
