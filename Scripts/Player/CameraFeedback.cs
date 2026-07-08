// Assets/Scripts/VoxelEngine/Player/CameraFeedback.cs
//
// Runtime camera "juice" for the player camera: screenshake + temporary FOV offset
// driven by ship acceleration. Attached automatically to the player camera by
// PlayerController so it works both on-foot and while piloting a cockpit.

using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(Camera))]
    public class CameraFeedback : MonoBehaviour
    {
        public static CameraFeedback Instance { get; private set; }

        /// <summary>
        /// The cockpit sets this while the local player is seated. Screenshake/FOV warp
        /// is suppressed the rest of the time so walking around or standing still feels calm.
        /// </summary>
        public static bool IsPiloting { get; set; }

        [Header("Shake")]
        [Tooltip("Maximum positional shake in metres at 1 G of acceleration.")]
        public float maxPosShake = 0.018f;
        [Tooltip("Maximum rotational shake in degrees at 1 G of acceleration.")]
        public float maxRotShake = 0.22f;
        [Tooltip("How fast the shake decays to zero when acceleration stops.")]
        public float shakeDecay = 4.0f;
        [Tooltip("Perlin noise scroll speed for the shake pattern.")]
        public float shakeSpeed = 18.0f;

        [Header("FOV")]
        [Tooltip("Maximum FOV offset in degrees at 1 G of acceleration.")]
        public float maxFovOffset = 1.5f;
        [Tooltip("How fast the FOV catches up to acceleration.")]
        public float fovResponse = 3.5f;

        private Camera _cam;
        private float _baseFov;
        private float _impulse;      // current shake magnitude (0..1 scaled by G)
        private float _impulseTarget;
        private float _fovOffset;
        private Vector3 _noisePosition; // perlin offset so patterns don't repeat
        private float _noiseTime;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            _cam = GetComponent<Camera>();
            _baseFov = _cam != null ? _cam.fieldOfView : 60f;
            _noisePosition = Random.insideUnitSphere * 100f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_cam == null) return;

            // Base FOV can change in settings; track it each frame.
            _baseFov = GameSettings.Fov;

            // Not piloting? Damp everything to zero so walking around or standing still is calm.
            if (!IsPiloting)
            {
                _impulse = 0f;
                _impulseTarget = 0f;
                _fovOffset = Mathf.MoveTowards(_fovOffset, 0f, fovResponse * Time.deltaTime);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                _cam.fieldOfView = _baseFov + _fovOffset;
                return;
            }

            // Smoothly decay / approach the impulse target.
            _impulse = Mathf.MoveTowards(_impulse, _impulseTarget, shakeDecay * Time.deltaTime);
            _impulseTarget = Mathf.MoveTowards(_impulseTarget, 0f, shakeDecay * Time.deltaTime);
            _noiseTime += Time.deltaTime * shakeSpeed;

            // Generate non-repeating shake from 3D Perlin noise.
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (_impulse > 0.001f)
            {
                float s = _impulse;
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

            // FOV "speed warp" — stretches the view slightly under acceleration.
            _fovOffset = Mathf.MoveTowards(_fovOffset, _impulse * maxFovOffset, fovResponse * Time.deltaTime);
            _cam.fieldOfView = _baseFov + _fovOffset;
        }

        /// <summary>
        /// Fire a screenshake/FOV impulse proportional to an acceleration (m/s²).
        /// 1 G ≈ 9.81 m/s² gives roughly the configured max values.
        /// </summary>
        public static void Impulse(Vector3 acceleration)
        {
            if (Instance == null) return;
            float g = acceleration.magnitude / 9.81f;
            // Dead zone: a stationary ship can have tiny residual physics jitter; ignore it.
            if (g < 0.15f) return;
            // Gentler square-law so powerful thrust rumbles but idle drift is silent.
            Instance._impulseTarget = Mathf.Clamp01(g * g * 0.22f);
        }
    }
}
