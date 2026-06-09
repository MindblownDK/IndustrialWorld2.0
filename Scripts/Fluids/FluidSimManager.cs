// Assets/Scripts/VoxelEngine/Fluids/FluidSimManager.cs
// Legacy stub — redirects to new WaterSim.FluidManager.
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.Fluids
{
    public class FluidSimManager : MonoBehaviour
    {
        public static FluidSimManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void MarkDirty(Vector3Int c)
        {
            WaterSim.FluidManager.Instance?.MarkActive(c);
        }

        public bool TryDrainWaterAt(Vector3Int v)
        {
            return WaterSim.FluidManager.Instance?.DrainWater(v) ?? false;
        }

        public void PlaceWater(Vector3Int v, byte l = 255)
        {
            WaterSim.FluidManager.Instance?.PlaceWater(v, l);
        }

        public bool IsOilAt(Vector3Int v) => false;
        public bool TryDrainOilAt(Vector3Int v) => false;
        public void PlaceOil(Vector3Int v, byte l = 255) { }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
