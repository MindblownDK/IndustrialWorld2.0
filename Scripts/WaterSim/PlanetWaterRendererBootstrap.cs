using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Ensures the planet water runtime is alive in every game scene without changing
    /// any existing save/runtime contract. This keeps the simulation, mesh generation,
    /// buoyancy probes, pumps, and generators active together.
    /// </summary>
    public class PlanetWaterRendererBootstrap : MonoBehaviour
    {
        [Tooltip("Optional shared ocean profile. If null, the runtime defaults baked into the water materials are used.")]
        public PlanetOceanProfile oceanProfile;

        [Tooltip("How many queued liquid chunks may rebuild their water meshes per frame.")]
        [Range(1, 24)] public int meshBuildBudgetPerFrame = 4;

        [Tooltip("When Crest is used as the scene water renderer, leave voxel liquid data active for pumps/buoyancy but stop rendering the old chunk-local liquid surface meshes.")]
        public bool renderVoxelLiquidSurfaces = false; // v3.20 Crest default

        [Tooltip("Schedules nearby generated liquid chunks in case a scene was previously saved with liquid surfaces disabled.")]
        public bool rescheduleVisibleLiquidSurfaces = true;

        [Range(1, 8)] public int liquidRescheduleChunkRadius = 3;
        [Range(0.25f, 5f)] public float liquidRescheduleInterval = 1.0f;

        private float _nextLiquidReschedule;

        [Header("Visual Materials")]
        [Tooltip("Optional imported/stylized water material. Simulation, pumps and buoyancy keep using voxel liquid data.")]
        public Material waterMaterialOverride;

        [Tooltip("Optional imported/stylized oil material. If left empty, the built-in viscous oil material is used.")]
        public Material oilMaterialOverride;

        private void Awake()
        {
            FluidManager.EnsureInstance();
            ApplyMaterialOverrides();
            ApplyProfile();
        }

        private void Update()
        {
            ApplyMaterialOverrides();
            if (renderVoxelLiquidSurfaces)
            {
                ScheduleNearbyLiquidChunks();
                WaterMeshBuilder.Pump(Mathf.Max(1, meshBuildBudgetPerFrame));
            }
            ApplyProfile();
        }

        private void ScheduleNearbyLiquidChunks()
        {
            if (!rescheduleVisibleLiquidSurfaces || Time.unscaledTime < _nextLiquidReschedule) return;
            _nextLiquidReschedule = Time.unscaledTime + liquidRescheduleInterval;

            var world = ActiveWorld.Current;
            var viewer = world?.Viewer != null ? world.Viewer : (Camera.main != null ? Camera.main.transform : null);
            if (world == null || viewer == null) return;

            Vector3Int center = world.WorldToChunk(viewer.position);
            int radius = Mathf.Max(1, liquidRescheduleChunkRadius);
            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                var coord = center + new Vector3Int(x, y, z);
                if (!world.TryGetChunk(coord, out var chunk) || chunk == null || !chunk.isGenerated) continue;
                if (ChunkHasVisibleLiquid(chunk))
                    WaterMeshBuilder.Schedule(chunk);
            }
        }

        private static bool ChunkHasVisibleLiquid(Chunk chunk)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            for (int z = 0; z < S; z += 2)
            for (int y = 0; y < S; y += 2)
            for (int x = 0; x < S; x += 2)
            {
                var v = chunk.GetVoxelLocal(x, y, z);
                if (FluidMaterialUtility.IsFluid(v)) return true;
            }
            return false;
        }

        private void ApplyMaterialOverrides()
        {
            WaterMeshBuilder.RenderingEnabled = renderVoxelLiquidSurfaces;
            WaterMeshBuilder.SetMaterialOverrides(waterMaterialOverride, oilMaterialOverride);
        }

        private void ApplyProfile()
        {
            if (oceanProfile == null) return;

            Shader.SetGlobalFloat("_PlanetOceanDeepWaveAmplitude", oceanProfile.deepWaveAmplitude);
            Shader.SetGlobalFloat("_PlanetOceanDeepWaveFrequency", oceanProfile.deepWaveFrequency);
            Shader.SetGlobalFloat("_PlanetOceanDeepWaveSpeed", oceanProfile.deepWaveSpeed);
            Shader.SetGlobalFloat("_PlanetOceanSecondaryWaveAmplitude", oceanProfile.secondaryWaveAmplitude);
            Shader.SetGlobalFloat("_PlanetOceanSecondaryWaveFrequency", oceanProfile.secondaryWaveFrequency);
            Shader.SetGlobalFloat("_PlanetOceanSecondaryWaveSpeed", oceanProfile.secondaryWaveSpeed);
            Shader.SetGlobalFloat("_PlanetOceanWaveChop", oceanProfile.chop);
            Shader.SetGlobalFloat("_PlanetOceanShoreAttenuationDistance", oceanProfile.shoreAttenuationDistance);
            Shader.SetGlobalFloat("_PlanetOceanShallowRippleAmplitude", oceanProfile.shallowRippleAmplitude);
            Shader.SetGlobalFloat("_PlanetOceanShallowRippleFrequency", oceanProfile.shallowRippleFrequency);
            Shader.SetGlobalFloat("_PlanetOceanShallowRippleSpeed", oceanProfile.shallowRippleSpeed);
            Shader.SetGlobalFloat("_PlanetOceanTidalWaveBoost", oceanProfile.tidalWaveBoost);
            Shader.SetGlobalFloat("_PlanetOceanTidalHeightBoost", oceanProfile.tidalHeightBoost);
            Shader.SetGlobalColor("_PlanetOceanShallowColor", oceanProfile.shallowColor);
            Shader.SetGlobalColor("_PlanetOceanDeepColor", oceanProfile.deepColor);
            Shader.SetGlobalColor("_PlanetOceanFoamColor", oceanProfile.foamColor);
            Shader.SetGlobalFloat("_PlanetOceanRefractionStrength", oceanProfile.refractionStrength);
            Shader.SetGlobalFloat("_PlanetOceanFresnelPower", oceanProfile.fresnelPower);
            Shader.SetGlobalFloat("_PlanetOceanSSS", oceanProfile.subsurfaceScattering);
        }
    }
}
