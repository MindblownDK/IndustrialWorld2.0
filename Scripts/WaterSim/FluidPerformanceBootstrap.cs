using UnityEngine;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// v3.20 – automatically applies Crest-optimized performance defaults
    /// to FluidManager and disables legacy voxel water mesh rendering.
    /// Eliminates lag spikes and chunk-gap artifacts.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class FluidPerformanceBootstrap : MonoBehaviour
    {
        [Header("Crest Mode")]
        public bool enableCrestMode = true;

        [Header("Tuning")]
        [Range(4f, 12f)] public float tickRate = 8f;
        [Range(2, 12)] public int maxChunksPerTick = 6;
        public int computeFrameSkip = 2;

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        [ContextMenu("Apply Now")]
        public void Apply()
        {
            // Disable legacy voxel surface – Crest is authoritative visual in v3.20
            WaterMeshBuilder.RenderingEnabled = false;

            var fm = FluidManager.Instance;
            if (fm == null)
            {
                FluidManager.EnsureInstance();
                fm = FluidManager.Instance;
            }
            if (fm == null) return;

            fm.tickRate = tickRate;
            fm.maxChunksPerTick = maxChunksPerTick;
            fm.computeIterationsPerFrame = 1;
            fm.computeFrameSkip = computeFrameSkip;
            // Use reflection to set useCrestVisualMode = enableCrestMode
            var t = typeof(FluidManager);
            var f = t.GetField("useCrestVisualMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null) f.SetValue(fm, enableCrestMode);

            Debug.Log($"[FluidPerformanceBootstrap] CrestMode={enableCrestMode} tick={tickRate} chunks/tick={maxChunksPerTick} – lag optimized (v3.20.0)");
        }
    }
}
