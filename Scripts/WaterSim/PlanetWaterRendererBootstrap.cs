using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Owns the native water presentation for spherical worlds: voxel-backed pools and
    /// lakes, a camera-local curved ocean shell, radial shader context, wakes, buoyancy,
    /// pumps, and the shared liquid simulation. No external ocean renderer is required.
    /// </summary>
    public class PlanetWaterRendererBootstrap : MonoBehaviour
    {
        [Tooltip("Optional shared ocean profile. If null, the native water materials use their safe defaults.")]
        public PlanetOceanProfile oceanProfile;

        [Header("Native Surface Budget")]
        [Tooltip("How many queued voxel-liquid chunks may rebuild their detailed meshes per frame.")]
        [Range(1, 24)] public int meshBuildBudgetPerFrame = 4;
        [Tooltip("Render detailed voxel liquid surfaces for lakes, rivers, buckets, and oil pools.")]
        public bool renderVoxelLiquidSurfaces = true;
        [Tooltip("Render the in-house camera-local curved ocean shell around the active spherical body.")]
        public bool renderNativeSphericalOceanPatch = true;
        [Range(64f, 1024f)] public float nativeOceanSearchRadius = 384f;
        [Range(4f, 32f)] public float nativeOceanTileSize = 12f;

        [Tooltip("Schedules nearby generated liquid chunks in case a scene was previously saved with liquid surfaces disabled.")]
        public bool rescheduleVisibleLiquidSurfaces = true;
        [Range(1, 8)] public int liquidRescheduleChunkRadius = 3;
        [Range(0.25f, 5f)] public float liquidRescheduleInterval = 1.0f;

        [Header("Native Materials")]
        [Tooltip("Optional setup-authored water material using VoxelEngine/VoxelWaterURP.")]
        public Material waterMaterialOverride;
        [Tooltip("Optional setup-authored viscous crude-oil material using VoxelEngine/VoxelWaterURP.")]
        public Material oilMaterialOverride;

        private float _nextLiquidReschedule;
        private ProceduralWaterPatchRenderer _nativeOcean;

        private void Awake()
        {
            FluidManager.EnsureInstance();
            NativeWaterWakeSystem.EnsureInstance();
            ApplyMaterialOverrides();
            EnsureNativeOceanPatch();
            PublishSphericalShaderContext();
            ApplyProfile();
        }

        private void Update()
        {
            ApplyMaterialOverrides();
            EnsureNativeOceanPatch();
            PublishSphericalShaderContext();
            if (renderVoxelLiquidSurfaces)
            {
                ScheduleNearbyLiquidChunks();
                WaterMeshBuilder.Pump(Mathf.Max(1, meshBuildBudgetPerFrame));
            }
            ApplyProfile();
        }

        private void EnsureNativeOceanPatch()
        {
            bool shouldRender = renderNativeSphericalOceanPatch && ActiveWorld.Current is SphereWorld;
            if (_nativeOcean == null)
                _nativeOcean = GetComponent<ProceduralWaterPatchRenderer>();

            if (!shouldRender)
            {
                if (_nativeOcean != null) _nativeOcean.enabled = false;
                return;
            }

            if (_nativeOcean == null)
                _nativeOcean = gameObject.AddComponent<ProceduralWaterPatchRenderer>();

            _nativeOcean.enabled = true;
            _nativeOcean.viewpoint = ActiveWorld.Current?.Viewer;
            _nativeOcean.searchRadius = nativeOceanSearchRadius;
            _nativeOcean.tileSize = nativeOceanTileSize;
            _nativeOcean.fastSphericalOceanPatch = true;
            _nativeOcean.waterMaterial = waterMaterialOverride != null
                ? waterMaterialOverride
                : WaterMeshBuilder.GetWaterMaterial();
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

                world.CompleteGenJobForChunk(chunk);
                world.CompleteMeshJobForChunk(chunk);
                if (ChunkHasVisibleLiquid(chunk)) WaterMeshBuilder.Schedule(chunk);
            }
        }

        private static bool ChunkHasVisibleLiquid(Chunk chunk)
        {
            const int size = VoxelConstants.CHUNK_SIZE;
            for (int z = 0; z < size; z += 2)
            for (int y = 0; y < size; y += 2)
            for (int x = 0; x < size; x += 2)
                if (FluidMaterialUtility.IsFluid(chunk.GetVoxelLocal(x, y, z))) return true;
            return false;
        }

        private void ApplyMaterialOverrides()
        {
            WaterMeshBuilder.RenderingEnabled = renderVoxelLiquidSurfaces;
            // The native curved patch owns open sea. Voxel topology owns every finite body.
            WaterMeshBuilder.SkipVoxelWaterAtOrBelowSeaLevel = renderNativeSphericalOceanPatch
                && ActiveWorld.Current is SphereWorld;
            WaterMeshBuilder.SetMaterialOverrides(waterMaterialOverride, oilMaterialOverride);
        }

        private static void PublishSphericalShaderContext()
        {
            if (ActiveWorld.Current is SphereWorld sphere && sphere.body != null)
            {
                Vector3 center = sphere.body.transform.position;
                Shader.SetGlobalVector("_VoxelWaterBodyCenter", new Vector4(center.x, center.y, center.z, 1f));
                Shader.SetGlobalFloat("_VoxelWaterIsPlanet", 1f);
            }
            else
            {
                Shader.SetGlobalVector("_VoxelWaterBodyCenter", Vector4.zero);
                Shader.SetGlobalFloat("_VoxelWaterIsPlanet", 0f);
            }
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
