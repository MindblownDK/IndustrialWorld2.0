// Assets/Scripts/VoxelEngine/Player/CameraFeedback.cs
//
// Runtime camera "juice" for the player camera: screenshake + temporary FOV offset.
// Attached automatically to the player camera by PlayerController. Two shake channels:
//   • Piloting shake — driven by ship acceleration while seated (cockpits).
//   • Event shake    — one-off impulses (explosions, impacts) that ALSO work on foot.
// Event shake is gated by GameSettings.ScreenShake so players can disable it.

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
        public float shakeDecay  = 4.0f;
        public float shakeSpeed  = 18.0f;
        [Tooltip("Multiplier on event-shake magnitude (explosions etc.) so it reads clearly.")]
        public float eventShakeScale = 1.0f;

        [Header("FOV")]
        public float maxFovOffset = 1.5f;
        public float fovResponse  = 3.5f;

        private Camera _cam;
        private float _baseFov;
        private float _impulse;          // piloting shake magnitude (0..1)
        private float _impulseTarget;
        private float _eventShake;       // one-off event shake (0..1)
        private float _eventShakeTarget;
        private float _fovOffset;
        private float _fovSqueeze;       // slow-push FOV modifier (hazards: black-hole crush, etc.)
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

            // ── Piloting shake (acceleration-driven, cockpit only). ──
            if (IsPiloting)
            {
                _impulse = Mathf.MoveTowards(_impulse, _impulseTarget, shakeDecay * dt);
                _impulseTarget = Mathf.MoveTowards(_impulseTarget, 0f, shakeDecay * dt);
            }
            else { _impulse = 0f; _impulseTarget = 0f; }

            // ── Event shake (explosions / impacts) — works on foot too, gated by the setting. ──
            if (GameSettings.ScreenShake)
            {
                _eventShake = Mathf.MoveTowards(_eventShake, _eventShakeTarget, shakeDecay * dt);
                _eventShakeTarget = Mathf.MoveTowards(_eventShakeTarget, 0f, shakeDecay * 0.6f * dt);
            }
            else { _eventShake = 0f; _eventShakeTarget = 0f; }

            float total = Mathf.Clamp01(Mathf.Max(_impulse, _eventShake));
            _noiseTime += dt * shakeSpeed;

            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (total > 0.001f)
            {
                float s = total;
                pos.x = (Mathf.PerlinNoise(_noisePosition.x + _noiseTime, 0f)   - 0.5f) * 2f * maxPosShake * s;
                pos.y = (Mathf.PerlinNoise(_noisePosition.y + _noiseTime, 100f)  - 0.5f) * 2f * maxPosShake * s;
                pos.z = (Mathf.PerlinNoise(_noisePosition.z + _noiseTime, 200f)  - 0.5f) * 2f * maxPosShake * s * 0.5f;
                float rx = (Mathf.PerlinNoise(_noisePosition.x + _noiseTime, 300f) - 0.5f) * 2f * maxRotShake * s;
                float ry = (Mathf.PerlinNoise(_noisePosition.y + _noiseTime, 400f) - 0.5f) * 2f * maxRotShake * s;
                float rz = (Mathf.PerlinNoise(_noisePosition.z + _noiseTime, 500f) - 0.5f) * 2f * maxRotShake * s;
                rot = Quaternion.Euler(rx, ry, rz);
            }
            transform.localPosition = pos;
            transform.localRotation = rot;

            // FOV warp: cockpit speed warp + a small event kick + hazard squeeze (black hole crush).
            float warp = (IsPiloting ? _impulse : 0f) * maxFovOffset + _eventShake * maxFovOffset * 0.8f;
            _fovSqueeze = Mathf.MoveTowards(_fovSqueeze, 0f, 8f * dt);
            _fovOffset = Mathf.MoveTowards(_fovOffset, warp + _fovSqueeze, fovResponse * dt);
            _cam.fieldOfView = _baseFov + _fovOffset;
        }

        /// <summary>Cockpit acceleration impulse (1 G ≈ max shake). Cockpit-only channel.</summary>
        public static void Impulse(Vector3 acceleration)
        {
            if (Instance == null) return;
            float g = acceleration.magnitude / 9.81f;
            if (g < 0.15f) return;
            Instance._impulseTarget = Mathf.Clamp01(g * g * 0.22f);
        }

        /// <summary>One-off event shake (explosions, heavy impacts). magnitude 0..1. Works on foot; respects GameSettings.ScreenShake.</summary>
        public static void AddShake(float magnitude)
        {
            if (Instance == null || magnitude <= 0f) return;
            Instance._eventShakeTarget = Mathf.Max(Instance._eventShakeTarget, Mathf.Clamp01(magnitude) * Instance.eventShakeScale);
        }

        /// <summary>
        /// Slow-push FOV squeeze (degrees, negative = narrower view) for hazard feedback —
        /// the black hole's tidal compression, quasar shear, etc. Locks onto the strongest
        /// request; decays smoothly when the hazard releases.
        /// </summary>
        public static void AddFovSqueeze(float degrees)
        {
            if (Instance == null) return;
            Instance._fovSqueeze = Mathf.Max(Instance._fovSqueeze, Mathf.Clamp(degrees, -14f, 14f));
        }
    }
}
