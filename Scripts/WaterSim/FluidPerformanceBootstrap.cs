using UnityEngine;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Applies performance-safe defaults for the in-house spherical voxel-water stack.
    /// The CPU liquid simulation remains authoritative for buckets, pumps, pools,
    /// pipes, buoyancy, and persistence; optional GPU density assist stays disabled
    /// unless a project explicitly enables it.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class FluidPerformanceBootstrap : MonoBehaviour
    {
        [Header("Native Water Mode")]
        [Tooltip("Render native voxel lakes/pools plus the curved spherical ocean patch.")]
        public bool renderNativeWater = true;
        [Tooltip("Optional GPU density-assist. Disable for the lowest-overhead fully native path.")]
        public bool useNativeVolumetricAssist;

        [Header("Tuning")]
        [Range(4f, 12f)] public float tickRate = 8f;
        [Range(2, 12)] public int maxChunksPerTick = 6;
        public int computeFrameSkip = 2;

        private void Awake() => Apply();
        private void OnEnable() => Apply();

        [ContextMenu("Apply Native Water Defaults")]
        public void Apply()
        {
            WaterMeshBuilder.RenderingEnabled = renderNativeWater;
            // Open-ocean water is rendered by ProceduralWaterPatchRenderer's curved shell;
            // detailed voxel meshes retain lakes, rivers, buckets, and all oil.
            WaterMeshBuilder.SkipVoxelWaterAtOrBelowSeaLevel = true;

            FluidManager.EnsureInstance();
            var manager = FluidManager.Instance;
            if (manager == null) return;

            manager.tickRate = tickRate;
            manager.maxChunksPerTick = maxChunksPerTick;
            manager.computeIterationsPerFrame = 1;
            manager.computeFrameSkip = Mathf.Max(1, computeFrameSkip);
            manager.useNativeVolumetricAssist = useNativeVolumetricAssist;
            NativeWaterWakeSystem.EnsureInstance();

            Debug.Log($"[FluidPerformanceBootstrap] NativeWater tick={tickRate:0.#} chunks/tick={maxChunksPerTick} " +
                      $"GPU-assist={useNativeVolumetricAssist}.");
        }
    }
}
