// Assets/Scripts/VoxelEngine/Power/Wind/WindSystem.cs
using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    public class WindSystem : MonoBehaviour
    {
        public static WindSystem Instance { get; private set; }

        [Header("Global Wind Settings")]
        public float baseWindSpeed = 12f; // m/s
        public float windVariation = 5f;
        public Vector3 windDirection = Vector3.forward;

        private float _currentWindSpeed;
        private float _timer;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            // Slowly oscillate wind speed for realism
            _currentWindSpeed = baseWindSpeed + Mathf.Sin(_timer * 0.1f) * windVariation;
        }

        public float GetWindSpeed() => _currentWindSpeed;
        public Vector3 GetWindDirection() => windDirection;
    }
}
