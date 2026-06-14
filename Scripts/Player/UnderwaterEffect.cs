// Assets/Scripts/VoxelEngine/Player/UnderwaterEffect.cs
//
// Underwater VFX — simplified and robust.
// • When camera is underwater AND it's raining → apply fog tint
// • When camera surfaces or rain stops → fully restore original render settings
// • No gradual transitions that can get stuck — clean snap on/off
// • State is saved ONCE on first entry and always restored on exit

using UnityEngine;

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(Camera))]
    public class UnderwaterEffect : MonoBehaviour
    {
        [Header("Fog & Color")]
        public Color underwaterTint = new Color(0.03f, 0.14f, 0.35f);
        public float fogDensity = 0.06f;

        [Header("Rain Fog")]
        [Tooltip("Fog density multiplier based on rain intensity (0 = no extra, 1 = full extra).")]
        public float rainFogBoost = 0.08f;

        public bool IsUnderwater { get; private set; }

        private Camera _cam;
        private bool _applied;
        private bool _saved;

        // Saved pre-underwater state
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

            // Method 1: PlayerWaterState
            if (_waterState != null && _waterState.IsHeadUnderwater) IsUnderwater = true;

            // Method 2: Direct voxel check
            if (!IsUnderwater)
            {
                var world = VoxelEngine.Core.VoxelWorld.Instance;
                if (world != null)
                {
                    var vp = world.WorldToVoxel(transform.position);
                    var v = world.GetVoxelWorld(vp);
                    if (v.waterLevel > 10) IsUnderwater = true;
                    if (_waterState != null && _waterState.WaterSurfaceY > transform.position.y) IsUnderwater = true;
                }
            }

            // Check if it's raining
            bool isRaining = false;
            float rainIntensity = 0f;
            var weather = Weather.WeatherManager.Instance;
            if (weather != null && weather.IsPrecipitating && !weather.IsSnowBiome)
            {
                isRaining = true;
                rainIntensity = weather.Intensity;
            }

            // Only apply fog effect when underwater AND raining
            bool shouldApplyFog = IsUnderwater && isRaining;

            if (shouldApplyFog)
            {
                if (!_saved)
                {
                    // Save original state ONCE before we modify anything
                    _sBg  = _cam.backgroundColor;
                    _sF   = _cam.clearFlags;
                    _sFar = _cam.farClipPlane;
                    _sFog = RenderSettings.fog;
                    _sFC  = RenderSettings.fogColor;
                    _sFD  = RenderSettings.fogDensity;
                    _sFM  = RenderSettings.fogMode;
                    _saved = true;
                }

                float totalFogDensity = fogDensity + rainFogBoost * rainIntensity;

                _cam.backgroundColor  = underwaterTint;
                _cam.clearFlags       = CameraClearFlags.SolidColor;
                _cam.farClipPlane     = 40f;
                RenderSettings.fog    = true;
                RenderSettings.fogMode    = FogMode.Exponential;
                RenderSettings.fogColor   = underwaterTint;
                RenderSettings.fogDensity = totalFogDensity;
                _applied = true;
            }
            else if (_applied && _saved)
            {
                // Fully restore original state
                Restore();
            }
        }

        private void Restore()
        {
            _cam.backgroundColor = _sBg;
            _cam.clearFlags      = _sF;
            _cam.farClipPlane    = _sFar;

            // Only restore fog if WeatherManager isn't controlling it
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
