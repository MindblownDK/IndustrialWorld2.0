// Assets/Scripts/VoxelEngine/Player/UnderwaterEffect.cs
//
// Underwater VFX: teal fog + color when camera is below water surface.

using UnityEngine;

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(Camera))]
    public class UnderwaterEffect : MonoBehaviour
    {
        public Color underwaterTint = new Color(0.04f, 0.18f, 0.40f);
        public float fogDensity = 0.06f;
        public bool IsUnderwater { get; private set; }

        private Camera _cam;
        private bool _prev;
        private Color _sBg; CameraClearFlags _sF; float _sFar;
        private bool _sFog; Color _sFC; float _sFD; FogMode _sFM;
        private bool _saved;

        private float _dbgTimer;
        void Awake() { _cam = GetComponent<Camera>(); }

        void LateUpdate()
        {
            _prev = IsUnderwater;
            var ws = GetComponentInParent<PlayerWaterState>();
            IsUnderwater = false;

            // Method 1: PlayerWaterState says head is underwater.
            if (ws != null && ws.IsHeadUnderwater) IsUnderwater = true;

            // Method 2: Direct voxel check at camera position.
            if (!IsUnderwater)
            {
                var world = VoxelEngine.Core.VoxelWorld.Instance;
                if (world != null)
                {
                    var vp = world.WorldToVoxel(transform.position);
                    var v = world.GetVoxelWorld(vp);
                    if (v.waterLevel > 10) IsUnderwater = true;
                    // Also check: is camera below water surface Y from PlayerWaterState?
                    if (ws != null && ws.WaterSurfaceY > transform.position.y) IsUnderwater = true;
                }
            }

            if (IsUnderwater && !_prev)
            {
                _sBg = _cam.backgroundColor; _sF = _cam.clearFlags; _sFar = _cam.farClipPlane;
                _sFog = RenderSettings.fog; _sFC = RenderSettings.fogColor;
                _sFD = RenderSettings.fogDensity; _sFM = RenderSettings.fogMode; _saved = true;
            }
            if (IsUnderwater)
            {
                _cam.backgroundColor = underwaterTint;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.farClipPlane = 40f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = underwaterTint;
                RenderSettings.fogDensity = fogDensity;
            }
            // Debug: uncomment to diagnose underwater detection.
            _dbgTimer += Time.deltaTime;
            if (_dbgTimer > 2f)
            {
                _dbgTimer = 0;
                var w = VoxelEngine.Core.VoxelWorld.Instance;
                if (w != null)
                {
                    var vp = w.WorldToVoxel(transform.position);
                    var v = w.GetVoxelWorld(vp);
                    float surfY = ws != null ? ws.WaterSurfaceY : -9999;
                    Debug.Log($"[UW] cam={transform.position.y:F1} voxelWater={v.waterLevel} surfY={surfY:F1} isUW={IsUnderwater} headUW={ws?.IsHeadUnderwater}");
                }
            }

            else if (_prev && _saved)
            {
                _cam.backgroundColor = _sBg; _cam.clearFlags = _sF; _cam.farClipPlane = _sFar;
                if (Weather.WeatherManager.Instance == null)
                { RenderSettings.fog = _sFog; RenderSettings.fogColor = _sFC;
                  RenderSettings.fogDensity = _sFD; RenderSettings.fogMode = _sFM; }
            }
        }

        void OnDisable() { if (_saved) { _cam.backgroundColor = _sBg; _cam.clearFlags = _sF; _cam.farClipPlane = _sFar; } }
    }
}
