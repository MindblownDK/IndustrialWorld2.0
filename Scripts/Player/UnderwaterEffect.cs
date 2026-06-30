// Assets/Scripts/VoxelEngine/Player/UnderwaterEffect.cs
//
// Underwater VFX — active only when the camera head is actually below the water volume.

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
        private Color _sAmbient;
        private float _sAmbientIntensity;

        private PlayerWaterState _waterState;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            Shader.SetGlobalFloat("_UnderwaterCA", 0.0f);
            Shader.SetGlobalFloat("_UnderwaterPostStrength", 0.0f);
        }

        void LateUpdate()
        {
            _waterState = GetComponentInParent<PlayerWaterState>();
            IsUnderwater = _waterState != null && _waterState.IsHeadUnderwater;

            if (IsUnderwater)
            {
                if (!_saved)
                {
                    _sBg = _cam.backgroundColor;
                    _sF = _cam.clearFlags;
                    _sFar = _cam.farClipPlane;
                    _sFog = RenderSettings.fog;
                    _sFC = RenderSettings.fogColor;
                    _sFD = RenderSettings.fogDensity;
                    _sFM = RenderSettings.fogMode;
                    _sAmbient = RenderSettings.ambientLight;
                    _sAmbientIntensity = RenderSettings.ambientIntensity;
                    _saved = true;
                }

                float rainIntensity = 0f;
                var weather = Weather.WeatherManager.Instance;
                if (weather != null && weather.IsPrecipitating && !weather.IsSnowBiome)
                    rainIntensity = weather.Intensity;

                float totalDensity = baseFogDensity + maxRainFogBoost * rainIntensity;
                Color tint = Color.Lerp(underwaterTint, rainTint, rainIntensity * 0.6f);

                _cam.backgroundColor = tint;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.farClipPlane = underwaterFarClip;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = tint;
                RenderSettings.fogDensity = totalDensity;
                RenderSettings.ambientLight = Color.Lerp(_sAmbient, tint, 0.35f);
                RenderSettings.ambientIntensity = Mathf.Min(_sAmbientIntensity, 0.45f);
                Shader.SetGlobalFloat("_UnderwaterCA", 1.0f);
                Shader.SetGlobalColor("_UnderwaterFogColor", tint);
                Shader.SetGlobalFloat("_UnderwaterPostStrength", 1.0f);
                _applied = true;
            }
            else if (_applied && _saved)
            {
                Restore();
            }
            else
            {
                Shader.SetGlobalFloat("_UnderwaterCA", 0.0f);
                Shader.SetGlobalFloat("_UnderwaterPostStrength", 0.0f);
            }
        }

        private void Restore()
        {
            Shader.SetGlobalFloat("_UnderwaterCA", 0.0f);
            Shader.SetGlobalFloat("_UnderwaterPostStrength", 0.0f);
            _cam.backgroundColor = _sBg;
            _cam.clearFlags = _sF;
            _cam.farClipPlane = _sFar;
            RenderSettings.fog = _sFog;
            RenderSettings.fogColor = _sFC;
            RenderSettings.fogDensity = _sFD;
            RenderSettings.fogMode = _sFM;
            RenderSettings.ambientLight = _sAmbient;
            RenderSettings.ambientIntensity = _sAmbientIntensity;
            _saved = false;
            _applied = false;
        }

        void OnDisable()
        {
            if (_applied && _saved) Restore();
            else
            {
                Shader.SetGlobalFloat("_UnderwaterCA", 0.0f);
                Shader.SetGlobalFloat("_UnderwaterPostStrength", 0.0f);
            }
        }
    }
}
