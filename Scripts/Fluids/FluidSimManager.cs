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
