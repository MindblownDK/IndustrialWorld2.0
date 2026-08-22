// Assets/Scripts/VoxelEngine/Fluids/FluidSimManager.cs
// Compatibility facade over the unified voxel-liquid simulation.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;

namespace VoxelEngine.Fluids
{
    public class FluidSimManager : MonoBehaviour
    {
        public static FluidSimManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            WaterSim.FluidManager.EnsureInstance();
        }

        public static void EnsureInstance()
        {
            if (Instance != null) { WaterSim.FluidManager.EnsureInstance(); return; }
            var go = new GameObject("FluidSimManager");
            Instance = go.AddComponent<FluidSimManager>();
            DontDestroyOnLoad(go);
        }

        public void MarkDirty(Vector3Int c)
        {
            WaterSim.FluidManager.EnsureInstance();
            WaterSim.FluidManager.Instance?.MarkActive(c);
        }

        public bool TryDrainWaterAt(Vector3Int v)
        {
            WaterSim.FluidManager.EnsureInstance();
            return WaterSim.FluidManager.Instance?.DrainWater(v) ?? false;
        }

        public void PlaceWater(Vector3Int v, byte l = 255)
        {
            WaterSim.FluidManager.EnsureInstance();
            WaterSim.FluidManager.Instance?.PlaceWater(v, l);
        }

        /// <summary>Place any of the 7 liquids (9.16.0 — the universal bucket uses this).</summary>
        public void PlaceLiquidAt(Vector3Int v, VoxelEngine.Items.LiquidType liquid, byte l = 255)
        {
            WaterSim.FluidManager.EnsureInstance();
            WaterSim.FluidManager.Instance?.PlaceLiquid(v, liquid, l);
        }

        /// <summary>Drain any of the 7 liquids at a voxel (true when anything was taken).</summary>
        public bool TryDrainLiquidAt(Vector3Int v, VoxelEngine.Items.LiquidType liquid)
        {
            WaterSim.FluidManager.EnsureInstance();
            return WaterSim.FluidManager.Instance != null
                && WaterSim.FluidManager.Instance.DrainLiquid(v, liquid, 255) > 0;
        }

        /// <summary>Drain up to <paramref name="maxLevel"/> of a liquid at a voxel (9.16.0 — the
        /// liquid canister scoops 13 levels per 500 ml click). True when anything was taken.</summary>
        public bool TryDrainLiquidLevelAt(Vector3Int v, VoxelEngine.Items.LiquidType liquid, byte maxLevel)
        {
            WaterSim.FluidManager.EnsureInstance();
            return WaterSim.FluidManager.Instance != null
                && WaterSim.FluidManager.Instance.DrainLiquid(v, liquid, maxLevel) > 0;
        }

        /// <summary>The liquid present at a voxel (Water when dry — check the level).</summary>
        public VoxelEngine.Items.LiquidType LiquidAt(Vector3Int v)
        {
            WaterSim.FluidManager.EnsureInstance();
            return WaterSim.FluidManager.Instance != null
                ? WaterSim.FluidManager.Instance.GetLiquidType(v)
                : VoxelEngine.Items.LiquidType.Water;
        }

        /// <summary>Liquid level at a voxel for any of the 7 liquids.</summary>
        public byte LiquidLevelAt(Vector3Int v, VoxelEngine.Items.LiquidType liquid)
        {
            WaterSim.FluidManager.EnsureInstance();
            return WaterSim.FluidManager.Instance != null
                ? WaterSim.FluidManager.Instance.GetLiquidLevel(v, liquid)
                : (byte)0;
        }

        public bool IsOilAt(Vector3Int v)
        {
            WaterSim.FluidManager.EnsureInstance();
            return WaterSim.FluidManager.Instance?.GetLiquidLevel(v, LiquidType.CrudeOil) > 0;
        }

        public bool TryDrainOilAt(Vector3Int v)
        {
            WaterSim.FluidManager.EnsureInstance();
            return WaterSim.FluidManager.Instance?.DrainOil(v) ?? false;
        }

        public void PlaceOil(Vector3Int v, byte l = 255)
        {
            WaterSim.FluidManager.EnsureInstance();
            WaterSim.FluidManager.Instance?.PlaceOil(v, l);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
