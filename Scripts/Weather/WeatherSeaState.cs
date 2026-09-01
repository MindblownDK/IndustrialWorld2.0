// Assets/Scripts/VoxelEngine/Weather/WeatherSeaState.cs
//
// Weather → water coupling. Publishes two globals that every liquid surface reads:
//
//   _WeatherSeaState   0 = glass calm … 1 = full storm sea (wave height, chop, whitecaps)
//   _WeatherWindDirWS  world-space wind direction the swell should run with
//
// Seas do not build or die with the gust — they lag it by minutes. A squall that has just
// arrived still finds a calm sea, and the swell keeps rolling long after the wind drops.
// That inertia is modelled here with separate build and decay rates, so the ocean always
// feels like it has weight.
//
// Nothing else needs wiring: the water shaders multiply their wave amplitude, chop, speed
// and crest foam by these globals, so pools, lakes, coastal water and the distant ocean LOD
// all answer to the same storm.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Publishes the global sea-state used by every water shader. Created automatically by
    /// <see cref="WeatherManager"/> — no prefab or setup step required.
    /// </summary>
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherSeaState : MonoBehaviour
    {
        private static readonly int IdSeaState = Shader.PropertyToID("_WeatherSeaState");
        private static readonly int IdWindDir  = Shader.PropertyToID("_WeatherWindDirWS");

        [Header("Sea Inertia (seconds to full build / full calm)")]
        [Tooltip("Seconds for a flat sea to build to a full storm sea.")]
        public float buildSeconds = 90f;
        [Tooltip("Seconds for a storm sea to settle back to calm — always slower than the build.")]
        public float calmSeconds = 150f;

        [Header("Response")]
        [Tooltip("Sea state contributed by precipitation intensity alone.")]
        [Range(0f, 1f)] public float precipitationWeight = 0.55f;
        [Tooltip("Sea state contributed by the storm wind multiplier.")]
        [Range(0f, 1f)] public float windWeight = 0.65f;

        /// <summary>Live sea state, 0 (calm) … 1 (storm). Read by the water shaders.</summary>
        public static float SeaState { get; private set; }

        private WeatherManager _wm;
        private float _state;

        private void OnEnable()
        {
            _wm = GetComponent<WeatherManager>();
            Publish(0f, Vector3.right);
        }

        private void OnDisable()
        {
            // Never leave a stale storm sea behind when the controller is torn down.
            _state = 0f;
            Publish(0f, Vector3.right);
        }

        private void Update()
        {
            if (_wm == null) _wm = WeatherManager.Instance;

            float target = 0f;
            if (_wm != null && _wm.IsWeatherActive)
            {
                // The sea answers to the PLANET's weather, not to the player's altitude: an
                // ocean is still stormy while you watch it from a mountain or from orbit.
                float precipitation = Mathf.Clamp01(_wm.Intensity) * precipitationWeight;
                float gale = Mathf.Clamp01((WeatherManager.WindMultiplier - 1f) / 2.5f) * windWeight;
                target = Mathf.Clamp01(precipitation + gale);
            }

            float rate = target > _state
                ? 1f / Mathf.Max(1f, buildSeconds)
                : 1f / Mathf.Max(1f, calmSeconds);
            _state = Mathf.MoveTowards(_state, target, rate * Time.deltaTime);

            Vector3 windDir = Vector3.right;
            var wind = WindField.Instance;
            if (wind != null)
            {
                Vector3 up = GravityProvider.GetUp(transform.position);
                Vector3 flat = Vector3.ProjectOnPlane(wind.Direction, up);
                if (flat.sqrMagnitude > 1e-4f) windDir = flat.normalized;
            }

            Publish(_state, windDir);
        }

        private static void Publish(float state, Vector3 windDir)
        {
            SeaState = state;
            Shader.SetGlobalFloat(IdSeaState, state);
            Shader.SetGlobalVector(IdWindDir, new Vector4(windDir.x, windDir.y, windDir.z, 0f));
        }
    }
}
