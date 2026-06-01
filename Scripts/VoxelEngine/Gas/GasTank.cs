// Assets/Scripts/VoxelEngine/Gas/GasTank.cs
//
// Stores a single type of gas. Has configurable input/output.
// Player can interact (RMB) to see contents and configure.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Gas
{
    [RequireComponent(typeof(PlacedBlock))]
    public class GasTank : MonoBehaviour
    {
        [Header("Tank")]
        [Tooltip("Maximum gas units this tank can store.")]
        public float capacity = 1000f;

        [Tooltip("What gas is stored (set automatically when gas enters).")]
        public GasType storedGasType = GasType.None;

        [Tooltip("Current amount of gas stored.")]
        public float storedAmount;

        [Header("I/O")]
        public bool acceptInput = true;
        public bool allowOutput = true;

        /// <summary>Fill fraction 0-1.</summary>
        public float Fill01 => capacity > 0 ? Mathf.Clamp01(storedAmount / capacity) : 0f;

        /// <summary>Try to add gas. Returns amount actually added.</summary>
        public float TryAdd(GasType type, float amount)
        {
            if (!acceptInput) return 0f;
            if (storedGasType != GasType.None && storedGasType != type) return 0f; // can't mix
            storedGasType = type;
            float space = capacity - storedAmount;
            float add = Mathf.Min(space, amount);
            storedAmount += add;
            return add;
        }

        /// <summary>Try to take gas. Returns amount actually taken.</summary>
        public float TryTake(GasType type, float amount)
        {
            if (!allowOutput) return 0f;
            if (storedGasType != type) return 0f;
            float take = Mathf.Min(storedAmount, amount);
            storedAmount -= take;
            if (storedAmount <= 0f) storedGasType = GasType.None;
            return take;
        }
    }
}
