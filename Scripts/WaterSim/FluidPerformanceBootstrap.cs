using UnityEngine;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// v3.23.0 – Applies fluid simulation performance defaults to FluidManager.
    ///
    /// HISTORY: v3.20 also force-disabled `WaterMeshBuilder.RenderingEnabled`
    /// under the belief that Crest was the authoritative water visual. In
    /// v3.23.0 voxel water is authoritative again (Crest's infinite plane is
    /// hidden), so this component MUST NOT disable water rendering — doing so
    /// makes water invisible everywhere.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class FluidPerformanceBootstrap : MonoBehaviour
    {
        [Header("Crest Mode (legacy)")]
        [Tooltip("v3.23.0 – Only controls FluidManager.useCrestVisualMode (skips heavy GPU fluid compute). Does NOT toggle water mesh rendering. Safe to leave ON — voxel water still renders because forceWaterMeshRendering is separate.")]
        public bool enableCrestMode = true;

        [Header("Tuning")]
        [Range(4f, 12f)] public float tickRate = 8f;
        [Range(2, 12)] public int maxChunksPerTick = 6;
        public int computeFrameSkip = 2;

        [Header("Water Rendering (v3.23.0)")]
        [Tooltip("Force WaterMeshBuilder.RenderingEnabled to this value on apply. LEAVE ON. Setting this false hides all voxel water.")]
        public bool forceWaterMeshRendering = true;

        private void Awake()  { Apply(); }
        private void OnEnable() { Apply(); }

        [ContextMenu("Apply Now")]
        public void Apply()
        {
            // v3.23.0 – Voxel water is authoritative. Never leave rendering off.
            if (forceWaterMeshRendering)
                WaterMeshBuilder.RenderingEnabled = true;

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

            var t = typeof(FluidManager);
            var f = t.GetField("useCrestVisualMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null) f.SetValue(fm, enableCrestMode);

            Debug.Log($"[FluidPerformanceBootstrap] v3.23.0 CrestMode={enableCrestMode} tick={tickRate} chunks/tick={maxChunksPerTick} waterMeshRender={forceWaterMeshRendering}");
        }
    }
}
