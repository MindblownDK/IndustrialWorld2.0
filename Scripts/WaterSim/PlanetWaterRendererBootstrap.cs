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

        private void Awake()
        {
            FluidManager.EnsureInstance();
            ApplyProfile();
        }

        private void Update()
        {
            WaterMeshBuilder.Pump(Mathf.Max(1, meshBuildBudgetPerFrame));
            ApplyProfile();
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
