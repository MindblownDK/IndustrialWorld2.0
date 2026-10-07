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
        [Range(2f, 12f)] public float tickRate = 6f;
        [Range(1, 12)] public int maxChunksPerTick = 3;
        public int computeFrameSkip = 2;

        private void Awake()
        {
            MigrateLegacyBudget();
            Apply();
        }

        private void OnEnable() => Apply();

        private void MigrateLegacyBudget()
        {
            // Preserve authored tuning. Upgrade only the recognizable legacy defaults:
            // 8/6 was the old burst-heavy setup; 4/2 was the previous low-throughput baseline.
            bool oldBurstBudget = Mathf.Approximately(tickRate, 8f) && maxChunksPerTick == 6;
            bool previousNativeBudget = Mathf.Approximately(tickRate, 4f) && maxChunksPerTick == 2;
            if (!oldBurstBudget && !previousNativeBudget) return;

            tickRate = 6f;
            maxChunksPerTick = 3;
        }

        [ContextMenu("Apply Native Water Defaults")]
        public void Apply()
        {
            WaterMeshBuilder.RenderingEnabled = renderNativeWater;
            // Real generated voxel fluid owns oceans, lakes, rivers, buckets, and oil.
            WaterMeshBuilder.SkipVoxelWaterAtOrBelowSeaLevel = false;

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
