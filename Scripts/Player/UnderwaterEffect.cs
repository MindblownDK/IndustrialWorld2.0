// Assets/Scripts/VoxelEngine/Player/UnderwaterEffect.cs
//
// Underwater VFX — ALWAYS active when camera is below water surface.
// Rain makes the fog denser/darker. Clean binary on/off with reliable restore.
// State saved ONCE on entry, always restored on exit. No stuck states.

using UnityEngine;

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(Camera))]
    public class UnderwaterEffect : MonoBehaviour
    {
        [Header("Base Underwater Fog")]
        public Color underwaterTint = new Color(0.03f, 0.14f, 0.35f);
        public float baseFogDensity = 0.045f;
        public float underwaterFarClip = 40f;

        [Header("Rain Boost")]
        [Tooltip("Extra fog density added during heavy rain.")]
        public float maxRainFogBoost = 0.06f;
        [Tooltip("Darker tint during heavy rain.")]
        public Color rainTint = new Color(0.015f, 0.06f, 0.18f);

        public bool IsUnderwater { get; private set; }

        private Camera _cam;
        private bool _applied;
        private bool _saved;

        private Color _sBg;
        private CameraClearFlags _sF;
        private float _sFar;
        private bool _sFog;
        private Color _sFC;
        private float _sFD;
        private FogMode _sFM;

        private PlayerWaterState _waterState;

        void Awake() { _cam = GetComponent<Camera>(); }

        void LateUpdate()
        {
            _waterState = GetComponentInParent<PlayerWaterState>();
            IsUnderwater = false;

            if (_waterState != null && _waterState.IsHeadUnderwater) IsUnderwater = true;

            if (!IsUnderwater)
            {
                var world = VoxelEngine.Core.ActiveWorld.Current;
                if (world != null)
                {
                    var vp = world.WorldToVoxel(transform.position);
                    var v = world.GetVoxelWorld(vp);
                    // A planet's sea radius is only a broad ocean shell. Camera FX
                    // must be driven by a real local liquid voxel so mountains, dry
                    // coasts, and the far side of an offset planet never look submerged.
                    if (VoxelEngine.WaterSim.FluidMaterialUtility.IsFluid(v) || (v.waterLevel > 10 && !v.IsSolid))
                        IsUnderwater = true;
                    else if (!VoxelEngine.WaterSim.PlanetWaterUtility.IsPlanetWorld
                        && _waterState != null && _waterState.WaterSurfaceY > transform.position.y)
                        IsUnderwater = true;
                }
            }

            if (IsUnderwater)
            {
                // Save original state ONCE
                if (!_saved)
                {
                    _sBg  = _cam.backgroundColor;
                    _sF   = _cam.clearFlags;
                    _sFar = _cam.farClipPlane;
                    _sFog = RenderSettings.fog;
                    _sFC  = RenderSettings.fogColor;
                    _sFD  = RenderSettings.fogDensity;
                    _sFM  = RenderSettings.fogMode;
                    _saved = true;
                }

                // Rain intensity boost
                float rainIntensity = 0f;
                var weather = Weather.WeatherManager.Instance;
                if (weather != null && weather.IsPrecipitating && !weather.IsSnowBiome)
                    rainIntensity = weather.Intensity;

                float totalDensity = baseFogDensity + maxRainFogBoost * rainIntensity;
                Color tint = Color.Lerp(underwaterTint, rainTint, rainIntensity * 0.6f);

                _cam.backgroundColor  = tint;
                _cam.clearFlags       = CameraClearFlags.SolidColor;
                _cam.farClipPlane     = underwaterFarClip;
                RenderSettings.fog    = true;
                RenderSettings.fogMode    = FogMode.Exponential;
                RenderSettings.fogColor   = tint;
                RenderSettings.fogDensity = totalDensity;
                Shader.SetGlobalFloat("_UnderwaterCA", 1.0f);
                Shader.SetGlobalColor("_UnderwaterFogColor", tint);
                _applied = true;
            }
            else if (_applied && _saved)
            {
                Restore();
            }
        }

        private void Restore()
        {
            Shader.SetGlobalFloat("_UnderwaterCA", 0.0f);
            _cam.backgroundColor = _sBg;
            _cam.clearFlags      = _sF;
            _cam.farClipPlane    = _sFar;

            if (Weather.WeatherManager.Instance == null)
            {
                RenderSettings.fog        = _sFog;
                RenderSettings.fogColor   = _sFC;
                RenderSettings.fogDensity = _sFD;
                RenderSettings.fogMode    = _sFM;
            }
            _saved   = false;
            _applied = false;
        }

        void OnDisable()
        {
            if (_applied && _saved) Restore();
        }
    }
}
