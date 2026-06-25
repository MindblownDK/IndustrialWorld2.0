// Assets/Scripts/VoxelEngine/Performance/PerformanceThrottle.cs
//
// Central performance budget manager. Monitors frame time and throttles expensive
// subsystems (water sim, map render, weather audio scans, farming checks) to keep
// the game at target FPS.
//
// Usage: Add to the same GO as VoxelWorld. All subsystems check PerformanceThrottle
// before doing expensive work.

using UnityEngine;

namespace VoxelEngine.Performance
{
    public class PerformanceThrottle : MonoBehaviour
    {
        public static PerformanceThrottle Instance { get; private set; }

        [Header("Target")]
        [Tooltip("Target FPS. Systems throttle when below this.")]
        public int targetFPS = 60;

        [Header("Budgets")]
        [Tooltip("Max water mesh rebuilds per frame.")]
        public int waterMeshBudget = 1;
        [Tooltip("Minimap render interval (seconds). Higher = less CPU.")]
        public float minimapInterval = 2.0f;
        [Tooltip("Weather audio scan interval (seconds).")]
        public float weatherScanInterval = 1.0f;
        [Tooltip("Max fluid sim chunks per tick.")]
        public int fluidSimBudget = 2;

        /// <summary>True if the last frame was under budget (game running fast enough).</summary>
        public bool IsUnderBudget { get; private set; }

        /// <summary>Current smoothed FPS.</summary>
        public float SmoothedFPS { get; private set; }

        private float _fpsAccum;
        private int _fpsFrames;
        private float _fpsTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Application.targetFrameRate = targetFPS;
        }

        private void Update()
        {
            // Smooth FPS calculation (update every 0.5s).
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                SmoothedFPS = _fpsFrames / _fpsAccum;
                _fpsAccum = 0f;
                _fpsFrames = 0;
                _fpsTimer = 0f;

                // Auto-adjust budgets based on performance.
                IsUnderBudget = SmoothedFPS >= targetFPS * 0.9f;

                if (SmoothedFPS < targetFPS * 0.5f)
                {
                    // Very bad FPS — aggressive throttle
                    waterMeshBudget = 1;
                    minimapInterval = 4.0f;
                    fluidSimBudget = 1;
                }
                else if (SmoothedFPS < targetFPS * 0.75f)
                {
                    // Below target — moderate throttle
                    waterMeshBudget = 1;
                    minimapInterval = 3.0f;
                    fluidSimBudget = 2;
                }
                else
                {
                    // Good FPS — normal budgets
                    waterMeshBudget = 2;
                    minimapInterval = 2.0f;
                    fluidSimBudget = 3;
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
