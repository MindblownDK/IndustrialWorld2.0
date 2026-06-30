using UnityEngine;

namespace VoxelEngine.WaterSim
{
    [CreateAssetMenu(fileName = "PlanetOceanProfile", menuName = "IndustrialWorld/Water/Planet Ocean Profile")]
    public class PlanetOceanProfile : ScriptableObject
    {
        [Header("Sea Level")]
        [Tooltip("Radius offset in voxels added to world sea level for visual ocean rendering.")]
        public float seaLevelRadiusOffsetVoxels = 0f;

        [Header("Deep Ocean Waves")]
        public float deepWaveAmplitude = 0.85f;
        public float deepWaveFrequency = 0.22f;
        public float deepWaveSpeed = 0.55f;
        public float secondaryWaveAmplitude = 0.35f;
        public float secondaryWaveFrequency = 0.47f;
        public float secondaryWaveSpeed = 0.91f;
        public float chop = 0.28f;

        [Header("Shore Blend")]
        [Tooltip("World-space metres over which deep ocean waves attenuate near terrain.")]
        public float shoreAttenuationDistance = 2.5f;
        public float shallowRippleAmplitude = 0.16f;
        public float shallowRippleFrequency = 1.65f;
        public float shallowRippleSpeed = 1.8f;

        [Header("Moon Tide")]
        public float tidalWaveBoost = 0.35f;
        public float tidalHeightBoost = 0.25f;

        [Header("Optics")]
        public Color shallowColor = new Color(0.08f, 0.52f, 0.82f, 0.92f);
        public Color deepColor = new Color(0.01f, 0.06f, 0.22f, 0.97f);
        public Color foamColor = new Color(0.92f, 0.96f, 1.00f, 0.88f);
        public float refractionStrength = 0.032f;
        public float fresnelPower = 3.2f;
        public float subsurfaceScattering = 0.35f;
    }
}
