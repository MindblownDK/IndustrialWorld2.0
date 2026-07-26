// Assets/Scripts/VoxelEngine/Simulation/FactoryPartAnimator.cs
//
// Small reusable runtime animator for procedurally generated factory prefabs.
// It keeps setup-wizard prefabs lightweight while still giving funnels, chutes,
// conveyors, and machines subtle motion once they are placed in the world.

using UnityEngine;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Animates one rotating child, one bobbing child, and one pulsing light.
    /// All references are resolved by child name so the setup wizard can generate
    /// polished prefabs without requiring hand-wired scene references.
    /// </summary>
    public sealed class FactoryPartAnimator : MonoBehaviour
    {
        [Header("Rotation")]
        public string rotatingChildName;
        public Vector3 rotationAxis = Vector3.up;
        public float rotationDegreesPerSecond = 90f;

        [Header("Bobbing")]
        public string bobbingChildName;
        public Vector3 bobAxis = Vector3.up;
        public float bobAmplitude = 0.03f;
        public float bobFrequency = 2f;

        [Header("Light Pulse")]
        public string pulseLightName;
        public float pulseAmplitude = 0.35f;
        public float pulseFrequency = 2f;

        private Transform _rotatingChild;
        private Transform _bobbingChild;
        private Vector3 _bobbingBasePosition;
        private Light _pulseLight;
        private float _baseLightIntensity;
        private float _time;

        private void Awake()
        {
            _rotatingChild = FindChild(rotatingChildName);
            _bobbingChild = FindChild(bobbingChildName);
            if (_bobbingChild != null) _bobbingBasePosition = _bobbingChild.localPosition;

            var lightTransform = FindChild(pulseLightName);
            if (lightTransform != null) _pulseLight = lightTransform.GetComponent<Light>();
            if (_pulseLight != null) _baseLightIntensity = _pulseLight.intensity;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            _time += deltaTime;

            if (_rotatingChild != null && rotationAxis.sqrMagnitude > 0.0001f && Mathf.Abs(rotationDegreesPerSecond) > 0.001f)
            {
                _rotatingChild.Rotate(rotationAxis.normalized, rotationDegreesPerSecond * deltaTime, Space.Self);
            }

            if (_bobbingChild != null && bobAxis.sqrMagnitude > 0.0001f && bobAmplitude > 0f && bobFrequency > 0f)
            {
                float offset = Mathf.Sin(_time * Mathf.PI * 2f * bobFrequency) * bobAmplitude;
                _bobbingChild.localPosition = _bobbingBasePosition + bobAxis.normalized * offset;
            }

            if (_pulseLight != null && pulseFrequency > 0f && pulseAmplitude > 0f)
            {
                float pulse = 1f + Mathf.Sin(_time * Mathf.PI * 2f * pulseFrequency) * pulseAmplitude;
                _pulseLight.intensity = Mathf.Max(0f, _baseLightIntensity * pulse);
            }
        }

        private Transform FindChild(string childName)
        {
            if (string.IsNullOrWhiteSpace(childName)) return null;
            var children = GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                if (child != null && child.name == childName) return child;
            }
            return null;
        }
    }
}
