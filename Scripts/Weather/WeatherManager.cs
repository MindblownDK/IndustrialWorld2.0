// Assets/Scripts/VoxelEngine/Weather/WeatherManager.cs
//
// Central weather controller. Drives rain/snow particles, ambient audio,
// surface-hit sounds, and lighting changes. Attaches to a manager GO in the scene.
//
// Weather cycles through Clear → Overcast → Rain → HeavyRain → Clear etc.
// In tundra/snowy biomes, precipitation becomes snow instead of rain.

using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Core;

namespace VoxelEngine.Weather
{
    public enum WeatherState { Clear, Overcast, LightRain, HeavyRain, Snow, Blizzard }

    public class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }

        [Header("Timing")]
        [Tooltip("Minimum seconds a weather state lasts.")]
        public float minStateDuration = 60f;
        [Tooltip("Maximum seconds a weather state lasts.")]
        public float maxStateDuration = 300f;
        [Tooltip("Seconds to blend between weather states.")]
        public float transitionDuration = 15f;

        [Header("References")]
        [Tooltip("The player's camera transform (particles follow this).")]
        public Transform playerCamera;

        // Current state
        public WeatherState CurrentState { get; private set; } = WeatherState.Clear;
        public WeatherState TargetState  { get; private set; } = WeatherState.Clear;

        /// <summary>0 = fully previous state, 1 = fully target state.</summary>
        public float TransitionProgress { get; private set; } = 1f;

        /// <summary>Current precipitation intensity (0 = none, 1 = max).</summary>
        public float Intensity { get; private set; }

        /// <summary>True if the current biome is a cold/snowy biome.</summary>
        public bool IsSnowBiome { get; private set; }

        /// <summary>True if it's currently precipitating (rain or snow).</summary>
        public bool IsPrecipitating => Intensity > 0.05f;

        private float _stateTimer;
        private float _nextStateChange;
        private float _biomeCheckTimer;

        // Sub-systems (created as children)
        private WeatherParticles _particles;
        private WeatherAudio _audio;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (playerCamera == null)
            {
                var cam = Camera.main;
                if (cam != null) playerCamera = cam.transform;
            }

            // Create sub-systems.
            _particles = gameObject.AddComponent<WeatherParticles>();
            _audio = gameObject.AddComponent<WeatherAudio>();

            _nextStateChange = Random.Range(minStateDuration, maxStateDuration);
        }

        private void Update()
        {
            // Follow camera.
            if (playerCamera != null)
                transform.position = playerCamera.position;

            // Check biome every 2 seconds.
            _biomeCheckTimer += Time.deltaTime;
            if (_biomeCheckTimer >= 2f)
            {
                _biomeCheckTimer = 0f;
                CheckBiome();
            }

            // Transition between states.
            if (TransitionProgress < 1f)
            {
                TransitionProgress += Time.deltaTime / Mathf.Max(0.1f, transitionDuration);
                if (TransitionProgress >= 1f)
                {
                    TransitionProgress = 1f;
                    CurrentState = TargetState;
                }
            }

            // Update intensity based on current/target blend.
            float fromIntensity = GetIntensity(CurrentState);
            float toIntensity = GetIntensity(TargetState);
            Intensity = Mathf.Lerp(fromIntensity, toIntensity, TransitionProgress);

            // State timer — pick next weather.
            _stateTimer += Time.deltaTime;
            if (_stateTimer >= _nextStateChange)
            {
                _stateTimer = 0f;
                _nextStateChange = Random.Range(minStateDuration, maxStateDuration);
                PickNextState();
            }
        }

        private void CheckBiome()
        {
            var world = VoxelWorld.Instance;
            if (world == null || world.viewer == null) return;
            var pos = world.viewer.position;
            int wx = Mathf.FloorToInt(pos.x);
            int wz = Mathf.FloorToInt(pos.z);

            // Sample temperature from biome noise.
            var climate = BiomePicker.SampleClimate(
                world.planet != null ? world.planet.seed : 0, wx, wz);
            IsSnowBiome = climate.x < 0.25f; // cold biomes
        }

        private void PickNextState()
        {
            CurrentState = TargetState;
            TransitionProgress = 0f;

            float roll = Random.value;
            if (IsSnowBiome)
            {
                // Snow biomes: clear → snow → blizzard cycle.
                if (CurrentState == WeatherState.Clear || CurrentState == WeatherState.Overcast)
                    TargetState = roll < 0.4f ? WeatherState.Snow : (roll < 0.7f ? WeatherState.Overcast : WeatherState.Clear);
                else if (CurrentState == WeatherState.Snow)
                    TargetState = roll < 0.3f ? WeatherState.Blizzard : (roll < 0.6f ? WeatherState.Clear : WeatherState.Snow);
                else
                    TargetState = roll < 0.5f ? WeatherState.Snow : WeatherState.Clear;
            }
            else
            {
                // Normal biomes: clear → overcast → rain cycle.
                if (CurrentState == WeatherState.Clear)
                    TargetState = roll < 0.35f ? WeatherState.Overcast : (roll < 0.55f ? WeatherState.LightRain : WeatherState.Clear);
                else if (CurrentState == WeatherState.Overcast)
                    TargetState = roll < 0.4f ? WeatherState.LightRain : (roll < 0.6f ? WeatherState.Clear : WeatherState.Overcast);
                else if (CurrentState == WeatherState.LightRain)
                    TargetState = roll < 0.35f ? WeatherState.HeavyRain : (roll < 0.65f ? WeatherState.Overcast : WeatherState.LightRain);
                else
                    TargetState = roll < 0.5f ? WeatherState.LightRain : WeatherState.Overcast;
            }
        }

        private float GetIntensity(WeatherState state) => state switch
        {
            WeatherState.Clear      => 0f,
            WeatherState.Overcast   => 0f,
            WeatherState.LightRain  => 0.4f,
            WeatherState.HeavyRain  => 1.0f,
            WeatherState.Snow       => 0.5f,
            WeatherState.Blizzard   => 1.0f,
            _ => 0f
        };

        /// <summary>Force a specific weather state (for testing / console commands).</summary>
        public void ForceWeather(WeatherState state)
        {
            CurrentState = state;
            TargetState = state;
            TransitionProgress = 1f;
            Intensity = GetIntensity(state);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
