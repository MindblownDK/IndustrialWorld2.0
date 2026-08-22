// Assets/Scripts/VoxelEngine/Cosmos/QualityPresetApplier.cs
//
// Bridges the Unity QualitySettings level to the live Cosmos visual systems. When the player
// changes the quality in the Settings menu, this applies it instantly — no scene reload needed.
// Reads GraphicsPreset and pushes the per-tier budgets to every active visual system.
using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Watches for quality-setting changes and applies them to the live Cosmos visual systems.
    /// Attach anywhere in the scene (CosmosBootstrap spawns one automatically).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class QualityPresetApplier : MonoBehaviour
    {
        private int _lastQuality = -1;

        private void Awake()
        {
            GameSettings.OnChanged += ApplyPreset;
        }

        private void OnDestroy()
        {
            GameSettings.OnChanged -= ApplyPreset;
        }

        private void Start() => ApplyPreset();

        private void Update()
        {
            // Also check the raw quality level (covers changes via Unity's own UI).
            int q = QualitySettings.GetQualityLevel();
            if (q != _lastQuality)
            {
                _lastQuality = q;
                ApplyPreset();
            }
        }

        private void ApplyPreset()
        {
            var tier = GraphicsPreset.Current;
            _lastQuality = QualitySettings.GetQualityLevel();

            // SphereWorld: view distance + jobs per frame.
            var sphere = SphereWorld.Instance;
            if (sphere != null)
            {
                sphere.viewDistance = GraphicsPreset.ViewDistance;
                sphere.maxJobsPerFrame = GraphicsPreset.JobsPerFrame;
            }

            // GPU grass: density multiplier array.
            var grass = FindAnyObjectByType<GpuGrassRenderer>();
            if (grass != null)
            {
                grass.qualityDensityMul = new float[]
                {
                    GraphicsPreset.GrassDensityMul * 0.35f,  // Low - sparse but visible (9.18.0)
                    GraphicsPreset.GrassDensityMul * 0.6f,   // Mid
                    GraphicsPreset.GrassDensityMul * 1f,     // High
                    GraphicsPreset.GrassDensityMul * 1.5f,   // Ultra
                };
                // Force rebuild with the new density.
                grass.enabled = false;
                grass.enabled = true;
            }

            // GPU-driven planet surfaces: push the streaming budget to every body's
            // quadtree engine (node resolution resolves per body from the new tier).
            foreach (var engine in FindObjectsByType<VoxelEngine.GpuVoxel.GpuPlanetEngine>(FindObjectsInactive.Include))
            {
                if (engine == null) continue;
                engine.ApplyQualityBudget(GraphicsPreset.JobsPerFrame);
            }

            // Quadtree ocean uses the same distance budget as the terrain tier.
            var oceanGpu = FindAnyObjectByType<VoxelEngine.GpuVoxel.GpuOceanEngine>();
            if (oceanGpu != null) oceanGpu.ApplyQualityBudget(GraphicsPreset.LodResolution);

            // Waterfall range.
            var waterfalls = FindAnyObjectByType<WaterfallSystem>();
            if (waterfalls != null)
            {
                waterfalls.scanRange = GraphicsPreset.WaterfallRange;
            }

            Debug.Log($"[QualityPresetApplier] Applied tier '{tier}' — viewDist:{GraphicsPreset.ViewDistance} " +
                      $"grass:{GraphicsPreset.GrassDensityMul} LOD:{GraphicsPreset.LodResolution} " +
                      $"jobs:{GraphicsPreset.JobsPerFrame} waterfalls:{GraphicsPreset.WaterfallRange}");
        }
    }
}
