// Assets/Scripts/VoxelEngine/Power/Wind/WindSystem.cs
// Global wind simulation. Realistic, queryable wind speed + direction + obstruction support.
// Stationary windmills use this for efficiency. MAX EFFORT: smooth, varied, direction-aware.

using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    public class WindSystem : MonoBehaviour
    {
        public static WindSystem Instance { get; private set; }

        /// <summary>Guarantees a live WindSystem — turbines call this on spawn so
        /// wind simulation works even in scenes authored before wind existed.</summary>
        public static WindSystem EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("WindSystem");
                Instance = go.AddComponent<WindSystem>();
            }
            return Instance;
        }

        [Header("Global Wind Settings")]
        [Tooltip("Average wind speed in m/s (realistic 8-15 for coastal)")]
        public float baseWindSpeed = 11.5f;
        [Tooltip("Variation amplitude")]
        public float windVariation = 4.5f;
        [Tooltip("How fast wind changes")]
        public float variationSpeed = 0.08f;

        [Tooltip("Base wind direction (world space)")]
        public Vector3 windDirection = new Vector3(1, 0, 0.3f).normalized;

        [Header("Obstruction")]
        [Tooltip("LayerMask for wind obstruction checks (buildings, terrain, other mills)")]
        public LayerMask obstructionLayers = ~0;

        private float _currentWindSpeed;
        private Vector3 _currentWindDir;
        private float _timer;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }

            _currentWindSpeed = baseWindSpeed;
            _currentWindDir = windDirection.normalized;
        }

        private void Update()
        {
            _timer += Time.deltaTime * variationSpeed;

            // Smooth realistic wind speed oscillation + gusts
            float baseSpeed = baseWindSpeed + Mathf.Sin(_timer * 0.7f) * windVariation * 0.6f;
            float gust = Mathf.PerlinNoise(_timer * 0.3f, 12.4f) * windVariation * 1.2f - windVariation * 0.3f;
            _currentWindSpeed = Mathf.Clamp(baseSpeed + gust, 2f, 28f);

            // Slight direction drift (realistic)
            float dirDrift = Mathf.Sin(_timer * 0.15f) * 12f;
            _currentWindDir = Quaternion.Euler(0, dirDrift, 0) * windDirection.normalized;
        }

        public float GetWindSpeed() => _currentWindSpeed;
        public Vector3 GetWindDirection() => _currentWindDir;

        /// <summary>
        /// Returns true if wind is significantly obstructed at this world position (upward ray or volume check).
        /// Used by windmills for efficiency penalty.
        /// </summary>
        public bool IsObstructed(Vector3 worldPos, float checkDistance = 65f)
        {
            // Upward check for blocking structures
            if (Physics.Raycast(worldPos + Vector3.up * 1.5f, Vector3.up, out var hitUp, checkDistance, obstructionLayers))
            {
                if (hitUp.collider != null && !hitUp.collider.isTrigger)
                    return true;
            }

            // Forward into wind direction volume check (spherecast for large objects)
            Vector3 windVec = _currentWindDir * 0.6f;
            if (Physics.SphereCast(worldPos + Vector3.up * 4f, 3.5f, windVec, out var hitWind, checkDistance * 0.7f, obstructionLayers))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns a wind multiplier (0-1.35) for a given height, wind, obstruction.
        /// </summary>
        public float GetWindEfficiencyMultiplier(float height, bool obstructed, float maxHeight = 220f)
        {
            float hMult = Mathf.Clamp01(height / Mathf.Max(10f, maxHeight)) * 0.65f + 0.35f;
            float speedFactor = Mathf.Pow(Mathf.Clamp(_currentWindSpeed, 3f, 25f) / 12f, 2.85f);
            float obs = obstructed ? 0.42f : 1f;
            return Mathf.Clamp(hMult * speedFactor * obs, 0.05f, 1.38f);
        }
    }
}
