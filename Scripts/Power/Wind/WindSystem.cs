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
            // Weather couples into turbine wind: a storm (WeatherManager.WindMultiplier > 1)
            // spins turbines faster and produces more power; calm weather leaves wind as-is.
            _currentWindSpeed = Mathf.Clamp(
                (baseSpeed + gust) * Weather.WeatherManager.WindMultiplier, 2f, 45f);

            // Predictable planet-locked prevailing wind at the observer's position, with only
            // a tiny ±2.5° micro-sway so players can reliably design factories around wind & pollution.
            Vector3 refPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Vector3 prevailing = GetPrevailingWorldDirection(refPos, out Vector3 surfaceUp);
            float microSway = Mathf.Sin(_timer * 0.25f) * 2.5f;
            _currentWindDir = (Quaternion.AngleAxis(microSway, surfaceUp) * prevailing).normalized;
        }

        public float GetWindSpeed() => _currentWindSpeed;
        public Vector3 GetWindDirection() => _currentWindDir;

        /// <summary>
        /// Planet-locked prevailing wind direction in body-local space at a given body-local position.
        /// Uses a steady zonal (eastward tangent) flow around the planet's local Y axis so wind direction
        /// at any factory location on the planet remains constant across day/night rotation.
        /// </summary>
        public static Vector3 GetPrevailingLocalDirection(Vector3 localPos)
        {
            if (localPos.sqrMagnitude < 0.0001f)
                return new Vector3(1f, 0f, 0.3f).normalized;
            Vector3 localUp = localPos.normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, localUp);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(Vector3.forward, localUp);
            return tangent.normalized;
        }

        /// <summary>
        /// Planet-locked prevailing wind direction in world space at <paramref name="worldPos"/>.
        /// </summary>
        public static Vector3 GetPrevailingWorldDirection(Vector3 worldPos, out Vector3 surfaceUp)
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body != null)
            {
                Vector3 localPos = body.transform.InverseTransformPoint(worldPos);
                Vector3 localUp = localPos.sqrMagnitude > 0.0001f ? localPos.normalized : Vector3.up;
                Vector3 localDir = GetPrevailingLocalDirection(localPos);
                surfaceUp = body.transform.TransformDirection(localUp).normalized;
                return body.transform.TransformDirection(localDir).normalized;
            }
            surfaceUp = Vector3.up;
            Vector3 fallback = Instance != null ? Instance.windDirection : new Vector3(1f, 0f, 0.3f);
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.right;
        }

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
