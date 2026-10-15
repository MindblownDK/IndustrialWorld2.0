// Assets/Scripts/VoxelEngine/Simulation/OreWasherMotionAnimator.cs

using UnityEngine;

namespace VoxelEngine.Simulation
{
    public sealed class OreWasherMotionAnimator : MonoBehaviour
    {
        public string trommelDrumChildName = "Generated_TrommelDrum";
        public string agitatorPaddleChildName = "Generated_AgitatorPaddle";
        public string waterSurfaceChildName = "Generated_WaterSurface";
        public string washedOreChildName = "Generated_WashedOre";
        public float drumSpinSpeed = 95f;
        public float paddleSpinSpeed = 160f;
        public float rippleAmplitude = 0.025f;

        private OreWasher _washer;
        private Transform _drum;
        private Transform _paddle;
        private Transform _waterSurface;
        private Transform _washedOre;
        private Vector3 _waterBasePos;
        private Vector3 _washedBasePos;
        private Renderer _waterRenderer;
        private Renderer _washedRenderer;

        private void Awake()
        {
            _washer = GetComponent<OreWasher>();
            _drum = transform.Find(trommelDrumChildName);
            _paddle = transform.Find(agitatorPaddleChildName);
            _waterSurface = transform.Find(waterSurfaceChildName);
            _washedOre = transform.Find(washedOreChildName);

            if (_waterSurface != null)
            {
                _waterBasePos = _waterSurface.localPosition;
                _waterRenderer = _waterSurface.GetComponent<Renderer>();
            }
            if (_washedOre != null)
            {
                _washedBasePos = _washedOre.localPosition;
                _washedRenderer = _washedOre.GetComponent<Renderer>();
            }
        }

        private void Update()
        {
            if (_washer == null) _washer = GetComponent<OreWasher>();
            bool active = _washer != null && _washer.IsActive;
            float progress = _washer != null ? _washer.Progress01 : 0f;
            float fill01 = _washer != null && _washer.waterTank != null ? _washer.waterTank.Fill01 : 0f;

            if (_drum != null && active)
                _drum.Rotate(0f, 0f, drumSpinSpeed * Time.deltaTime, Space.Self);

            if (_paddle != null && active)
                _paddle.Rotate(0f, paddleSpinSpeed * Time.deltaTime, 0f, Space.Self);

            if (_waterSurface != null)
            {
                if (_waterRenderer != null) _waterRenderer.enabled = fill01 > 0.02f;
                float ripple = active ? Mathf.Sin(Time.time * 9f) * rippleAmplitude : 0f;
                float levelOffset = Mathf.Lerp(-0.08f, 0.04f, fill01);
                _waterSurface.localPosition = _waterBasePos + Vector3.up * (levelOffset + ripple);
            }

            if (_washedOre != null)
            {
                if (_washedRenderer != null) _washedRenderer.enabled = active && progress >= 0.25f;
                float t = Mathf.Clamp01((progress - 0.25f) / 0.75f);
                _washedOre.localPosition = _washedBasePos + Vector3.forward * Mathf.Lerp(-0.22f, 0.26f, t);
            }
        }
    }
}
