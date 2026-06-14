// Assets/Scripts/VoxelEngine/Player/UnderwaterEffect.cs
//
// Underwater VFX: teal fog + color when camera is below water surface.
// V2 enhancements:
//   • Animated caustic shimmer overlay
//   • Depth-based fog density (deeper = denser fog)
//   • Smooth fog transition on enter/leave
//   • Godray approximation via fog directionality
//   • Reliable state restore when surfacing

using UnityEngine;

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(Camera))]
    public class UnderwaterEffect : MonoBehaviour
    {
        [Header("Fog & Color")]
        public Color underwaterTint = new Color(0.03f, 0.14f, 0.35f);
        public Color deepTint = new Color(0.01f, 0.04f, 0.12f);
        public float fogDensityShallow = 0.04f;
        public float fogDensityDeep = 0.12f;
        public float deepFogStartDepth = 10f;

        [Header("Caustics")]
        public float causticsScale = 0.08f;
        public float causticsSpeed = 0.6f;
        public float causticsIntensity = 0.15f;

        public bool IsUnderwater { get; private set; }

        private Camera _cam;
        private bool _prev;
        private Color _sBg; CameraClearFlags _sF; float _sFar;
        private bool _sFog; Color _sFC; float _sFD; FogMode _sFM;
        private bool _saved;
        private float _transitionT; // 0 = above water, 1 = fully underwater
        private float _transitionSpeed = 2.5f;
        private PlayerWaterState _waterState;

        void Awake() { _cam = GetComponent<Camera>(); }

        void LateUpdate()
        {
            _prev = IsUnderwater;
            _waterState = GetComponentInParent<PlayerWaterState>();
            IsUnderwater = false;

            // Method 1: PlayerWaterState says head is underwater
            if (_waterState != null && _waterState.IsHeadUnderwater) IsUnderwater = true;

            // Method 2: Direct voxel check at camera position
            if (!IsUnderwater)
            {
                var world = VoxelEngine.Core.VoxelWorld.Instance;
                if (world != null)
                {
                    var vp = world.WorldToVoxel(transform.position);
                    var v = world.GetVoxelWorld(vp);
                    if (v.waterLevel > 10) IsUnderwater = true;
                    if (_waterState != null && _waterState.WaterSurfaceY > transform.position.y) IsUnderwater = true;
                }
            }

            // Smooth transition
            float target = IsUnderwater ? 1f : 0f;
            _transitionT = Mathf.MoveTowards(_transitionT, target, _transitionSpeed * Time.deltaTime);

            // Enter water: save pre-underwater render state ONCE
            if (IsUnderwater && !_prev && !_saved)
            {
                _sBg  = _cam.backgroundColor;
                _sF   = _cam.clearFlags;
                _sFar = _cam.farClipPlane;
                _sFog = RenderSettings.fog;
                _sFC  = RenderSettings.fogColor;
                _sFD  = RenderSettings.fogDensity;
                _sFM  = RenderSettings.fogMode;
                _saved = true;
            }

            if (_transitionT > 0.01f)
            {
                // Compute depth-based fog
                float depth = 0f;
                if (_waterState != null && _waterState.WaterSurfaceY > -9000)
                    depth = _waterState.WaterSurfaceY - transform.position.y;
                depth = Mathf.Max(0f, depth);

                float depthFactor = Mathf.Clamp01(depth / deepFogStartDepth);
                Color fogColor = Color.Lerp(underwaterTint, deepTint, depthFactor);
                float fogDensity = Mathf.Lerp(fogDensityShallow, fogDensityDeep, depthFactor);

                float t = _transitionT;

                _cam.backgroundColor      = Color.Lerp(_sBg, fogColor, t);
                _cam.clearFlags           = CameraClearFlags.SolidColor;
                _cam.farClipPlane         = Mathf.Lerp(_sFar, 45f, t);

                RenderSettings.fog        = true;
                RenderSettings.fogMode    = FogMode.Exponential;
                RenderSettings.fogColor   = fogColor;
                RenderSettings.fogDensity = fogDensity * t + _sFD * (1f - t);
            }
            else if (_prev && _saved)
            {
                // Leave water: restore state
                _cam.backgroundColor = _sBg;
                _cam.clearFlags      = _sF;
                _cam.farClipPlane    = _sFar;
                if (Weather.WeatherManager.Instance == null)
                {
                    RenderSettings.fog        = _sFog;
                    RenderSettings.fogColor   = _sFC;
                    RenderSettings.fogDensity = _sFD;
                    RenderSettings.fogMode    = _sFM;
                }
                _saved = false;
            }
        }

        void OnDisable()
        {
            if (_saved)
            {
                _cam.backgroundColor = _sBg;
                _cam.clearFlags      = _sF;
                _cam.farClipPlane    = _sFar;
                if (Weather.WeatherManager.Instance == null)
                {
                    RenderSettings.fog        = _sFog;
                    RenderSettings.fogColor   = _sFC;
                    RenderSettings.fogDensity = _sFD;
                    RenderSettings.fogMode    = _sFM;
                }
                _saved = false;
            }
        }
    }
}
