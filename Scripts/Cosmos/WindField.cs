// Assets/Scripts/VoxelEngine/Cosmos/WindField.cs
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Settings;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Global wind provider for the currently active celestial body.
    ///
    /// Wind ebbs and flows like real weather: a low-frequency 3D noise field is drifted
    /// through slowly over time, giving a continuously-flowing base direction, on top of
    /// which a smooth gust envelope breathes the magnitude up and down. It NEVER snaps —
    /// every frame is a continuous derivative of the previous one.
    ///
    /// Consumers (GPU grass shader, particles, audio) read <see cref="Current"/>. The
    /// active body's BodySettings drives <see cref="strength"/> and <see cref="gustiness"/>.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WindField : MonoBehaviour
    {
        public static WindField Instance { get; private set; }

        [Tooltip("Base wind magnitude (m/s flavour). Set from the active BodySettings.windStrength.")]
        public float strength = 1f;

        [Tooltip("How strongly the wind surges. Set from BodySettings.windGustiness.")]
        [Range(0f, 1f)] public float gustiness = 0.4f;

        /// <summary>Current world-space wind vector (normalised direction × magnitude).</summary>
        public Vector3 Current { get; private set; } = Vector3.zero;

        /// <summary>Current normalised direction only (handy for shaders).</summary>
        public Vector3 Direction => _dir;

        private Vector3 _dir = new Vector3(1f, 0f, 0f);
        private float _time;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            Current = Sample(_time) * strength * Weather.WeatherManager.WindMultiplier;
        }

        /// <summary>
        /// Continuous wind sample at time t. Uses the planet-locked prevailing wind direction
        /// (shared with WindSystem and PollutionService) with only a subtle ±2.5° micro-sway
        /// so visual wind lines, grass, and pollution spread always match and stay predictable.
        /// </summary>
        private Vector3 Sample(float t)
        {
            Vector3 refPos = Camera.main != null ? Camera.main.transform.position : transform.position;
            Vector3 prevailing = VoxelEngine.Power.Wind.WindSystem.GetPrevailingWorldDirection(refPos, out Vector3 surfaceUp);
            float microSway = Mathf.Sin(t * 0.22f) * 2.5f;
            Vector3 baseDir = Quaternion.AngleAxis(microSway, surfaceUp) * prevailing;
            if (baseDir.sqrMagnitude < 1e-5f) baseDir = _dir;
            _dir = baseDir.normalized;

            // Gust envelope: a slow swell plus a faster flutter, eased so it's organic.
            float swell   = Mathf.Sin(t * 0.27f) * 0.5f + 0.5f;                 // 0..1
            float flutter = Mathf.PerlinNoise(t * 0.14f, 11.3f) * 2f - 1f;      // -1..1
            float gust    = 1f + gustiness * (swell - 0.5f + flutter * 0.5f);
            gust = Mathf.Clamp(gust, 0.2f, 2.2f);

            return _dir * gust;
        }

        /// <summary>
        /// Apply the active body's wind personality. Called by the cosmic bootstrap when a
        /// body becomes the player's home world.
        /// </summary>
        public void ApplyBody(BodySettings body)
        {
            if (body == null) return;
            strength   = Mathf.Max(0f, body.windStrength);
            gustiness  = Mathf.Clamp01(body.windGustiness);
        }

        // Optional: let the wind respect a global weather/quality multiplier later.
        private void OnEnable() => GameSettings.OnChanged += OnSettingsChanged;
        private void OnDisable() => GameSettings.OnChanged -= OnSettingsChanged;
        private void OnSettingsChanged() { /* hook reserved for wind-density quality scaling */ }
    }
}
