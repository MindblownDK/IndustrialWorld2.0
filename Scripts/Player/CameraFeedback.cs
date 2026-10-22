// Assets/Scripts/VoxelEngine/Player/CameraFeedback.cs
//
// Runtime camera feedback for the player camera:
//   - Piloting thrust impulse drives smooth FOV stretch only (never vibrates the cockpit
//     or grid hierarchy relative to the pilot's eyes).
//   - One-off event impacts (explosions, collisions) apply brief screen shake on foot only
//     when GameSettings.ScreenShake is enabled.

using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(Camera))]
    public class CameraFeedback : MonoBehaviour
    {
        public static CameraFeedback Instance { get; private set; }

        /// <summary>The cockpit sets this while the local player is seated.</summary>
        public static bool IsPiloting { get; set; }

        [Header("Shake")]
        public float maxPosShake = 0.018f;
        public float maxRotShake = 0.22f;
        public float shakeDecay = 4.0f;
        public float shakeSpeed = 18.0f;
        [Tooltip("Multiplier on event-shake magnitude (explosions etc.) so it reads clearly.")]
        public float eventShakeScale = 1.0f;

        [Header("FOV")]
        public float maxFovOffset = 1.5f;
        public float fovResponse = 3.5f;

        private Camera _cam;
        private float _baseFov;
        private float _impulse;
        private float _impulseTarget;
        private float _eventShake;
        private float _eventShakeTarget;
        private float _fovOffset;
        private float _fovSqueeze;
        private Vector3 _noisePosition;
        private float _noiseTime;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _cam = GetComponent<Camera>();
            _baseFov = _cam != null ? _cam.fieldOfView : 60f;
            _noisePosition = Random.insideUnitSphere * 100f;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (_cam == null) return;
            float dt = Time.deltaTime;
            _baseFov = GameSettings.Fov;

            // Piloting acceleration feeds smooth FOV warp only when ScreenShake is enabled,
            // never local transform jitter inside the cockpit.
            if (IsPiloting && GameSettings.ScreenShake)
            {
                _impulse = Mathf.MoveTowards(_impulse, _impulseTarget, shakeDecay * dt);
                _impulseTarget = Mathf.MoveTowards(_impulseTarget, 0f, shakeDecay * dt);
            }
            else
            {
                _impulse = 0f;
                _impulseTarget = 0f;
            }

            // Event shake (explosions / impacts): disabled while seated in a cockpit so
            // the cockpit frame and grid never vibrate relative to the camera.
            if (GameSettings.ScreenShake && !IsPiloting)
            {
                _eventShake = Mathf.MoveTowards(_eventShake, _eventShakeTarget, shakeDecay * dt);
                _eventShakeTarget = Mathf.MoveTowards(_eventShakeTarget, 0f, shakeDecay * 0.6f * dt);
            }
            else
            {
                _eventShake = 0f;
                _eventShakeTarget = 0f;
            }

            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (_eventShake > 0.001f)
            {
                _noiseTime += dt * shakeSpeed;
                float s = Mathf.Clamp01(_eventShake);
                pos.x = (Mathf.PerlinNoise(_noisePosition.x + _noiseTime, 0f) - 0.5f) * 2f * maxPosShake * s;
                pos.y = (Mathf.PerlinNoise(_noisePosition.y + _noiseTime, 100f) - 0.5f) * 2f * maxPosShake * s;
                pos.z = (Mathf.PerlinNoise(_noisePosition.z + _noiseTime, 200f) - 0.5f) * 2f * maxPosShake * s * 0.5f;
                float rx = (Mathf.PerlinNoise(_noisePosition.x + _noiseTime, 300f) - 0.5f) * 2f * maxRotShake * s;
                float ry = (Mathf.PerlinNoise(_noisePosition.y + _noiseTime, 400f) - 0.5f) * 2f * maxRotShake * s;
                float rz = (Mathf.PerlinNoise(_noisePosition.z + _noiseTime, 500f) - 0.5f) * 2f * maxRotShake * s;
                rot = Quaternion.Euler(rx, ry, rz);
            }
            transform.localPosition = pos;
            transform.localRotation = rot;

            float warp = (IsPiloting ? _impulse : 0f) * maxFovOffset + _eventShake * maxFovOffset * 0.8f;
            _fovSqueeze = Mathf.MoveTowards(_fovSqueeze, 0f, 8f * dt);
            _fovOffset = Mathf.MoveTowards(_fovOffset, warp + _fovSqueeze, fovResponse * dt);
            _cam.fieldOfView = _baseFov + _fovOffset;
        }

        /// <summary>Cockpit thrust acceleration impulse (cockpit-only FOV channel).</summary>
        public static void Impulse(Vector3 acceleration)
        {
            if (Instance == null || !GameSettings.ScreenShake) return;
            float g = acceleration.magnitude / 9.81f;
            if (g < 0.15f) return;
            Instance._impulseTarget = Mathf.Clamp01(g * g * 0.22f);
        }

        /// <summary>Drop leftover shake/FOV after entering, exiting or teleporting so the camera rests cleanly.</summary>
        public static void ResetTransient()
        {
            if (Instance == null) return;
            Instance._impulse = 0f;
            Instance._impulseTarget = 0f;
            Instance._eventShake = 0f;
            Instance._eventShakeTarget = 0f;
            Instance._fovSqueeze = 0f;
            Instance._fovOffset = 0f;
            if (Instance._cam != null)
            {
                Instance._cam.fieldOfView = Instance._baseFov;
                Instance.transform.localPosition = Vector3.zero;
                Instance.transform.localRotation = Quaternion.identity;
            }
        }

        public static void AddShake(float magnitude)
        {
            if (Instance == null || magnitude <= 0f || !GameSettings.ScreenShake) return;
            Instance._eventShakeTarget = Mathf.Max(Instance._eventShakeTarget, Mathf.Clamp01(magnitude) * Instance.eventShakeScale);
        }

        public static void AddFovSqueeze(float degrees)
        {
            if (Instance == null) return;
            Instance._fovSqueeze = Mathf.Max(Instance._fovSqueeze, Mathf.Clamp(degrees, -14f, 14f));
        }
    }
}
