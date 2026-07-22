// Assets/Scripts/VoxelEngine/GridSystem/GridCameraBlock.cs
//
// A camera block that captures live video and feeds it to linked GridScreenBlocks.
// Place on a grid, right-click a screen, choose Camera mode, and select the camera source.
// v5.51.2-dev — Unique feed camera/texture and camera identity normalization.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridCameraBlock : GridBlock, IGridCameraFeedProvider
    {
        [Header("Camera")]
        public float fieldOfView = 70f;
        public float cameraRange = 100f;
        public Vector3 cameraOffset = new Vector3(0f, 0.58f, -2.40f);
        public Vector3 cameraRotation = Vector3.zero;
        [Tooltip("Generated camera prefabs place the lens on local -Z. Keep this enabled so the feed looks out through the lens.")]
        public bool lensLooksAlongNegativeZ = true;
        [Range(128, 2048)] public int feedResolution = 512;
        [Range(1, 10)] public int renderIntervalFrames = 2;

        [Header("Status LED")]
        public Color feedInUseColor = new Color(0.18f, 0.95f, 0.38f);
        public Color onlineIdleColor = new Color(1.00f, 0.74f, 0.18f);
        public Color offlineColor = new Color(0.95f, 0.12f, 0.08f);

        private readonly Dictionary<EntityId, int> _feedConsumers = new();
        private Camera _captureCamera;
        private RenderTexture _renderTexture;
        private Renderer _statusLedRenderer;
        private Light _statusLedLight;
        private MaterialPropertyBlock _ledPropertyBlock;
        private int _lastRenderedFrame = -1000;
        private bool _initialized;

        public RenderTexture FeedTexture
        {
            get
            {
                if (!_initialized) InitializeCamera();
                if (IsOnline && _captureCamera != null && _lastRenderedFrame != Time.frameCount)
                    RenderFeed();
                return _renderTexture;
            }
        }

        public bool IsOnline => Enabled && Grid != null && Grid.HasPower;
        public bool IsFeedInUse
        {
            get
            {
                PruneExpiredConsumers();
                return _feedConsumers.Count > 0;
            }
        }

        public string SourceName => IsBadCameraName(blockName) ? "Camera Block" : blockName;
        public string DataCategory => "Camera";

        public string GetDisplayData()
        {
            string state = !IsOnline ? "OFFLINE" : IsFeedInUse ? "LIVE FEED" : "ONLINE / IDLE";
            return $"CAMERA\n{state}\nFOV {(int)fieldOfView}°\nRange {(int)cameraRange}m";
        }

        public override float PowerDraw => Enabled ? 30f : 0f;

        private void Awake()
        {
            NormalizeCameraIdentityAndDefaults();
            CacheStatusLed();
        }

        private void Start()
        {
            NormalizeCameraIdentityAndDefaults();
            CacheStatusLed();
            UpdateStatusLed();
        }

        private void NormalizeCameraIdentityAndDefaults()
        {
            if (IsBadCameraName(blockName))
                blockName = "Camera Block";

            if (cameraOffset == Vector3.zero || Vector3.Distance(cameraOffset, new Vector3(0f, 0.3f, 0f)) < 0.001f)
                cameraOffset = new Vector3(0f, 0.58f, -2.40f);
        }

        private static bool IsBadCameraName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                   || value == "Armor Block"
                   || value == "Iron Ore"
                   || value == "iron_ore";
        }

        public void RegisterFeedConsumer(GridScreenBlock screen)
        {
            if (screen == null) return;
            _feedConsumers[screen.GetEntityId()] = Time.frameCount;
        }

        private void InitializeCamera()
        {
            _initialized = true;

            int resolution = Mathf.Clamp(feedResolution, 128, 2048);
            _renderTexture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "CameraBlock_Feed_" + GetEntityId(),
                autoGenerateMips = false,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1
            };
            _renderTexture.Create();

            var camGo = new GameObject("CameraBlock_FeedCamera_" + GetEntityId());
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = cameraOffset;
            Quaternion localRotation = Quaternion.Euler(cameraRotation);
            if (lensLooksAlongNegativeZ)
                localRotation *= Quaternion.Euler(0f, 180f, 0f);
            camGo.transform.localRotation = localRotation;
            camGo.hideFlags = HideFlags.HideAndDontSave;

            _captureCamera = camGo.AddComponent<Camera>();
            _captureCamera.fieldOfView = Mathf.Clamp(fieldOfView, 10f, 140f);
            _captureCamera.farClipPlane = Mathf.Max(5f, cameraRange);
            _captureCamera.nearClipPlane = 0.05f;
            _captureCamera.targetTexture = _renderTexture;
            _captureCamera.cullingMask = ~0;
            _captureCamera.clearFlags = CameraClearFlags.Skybox;
            _captureCamera.allowHDR = false;
            _captureCamera.enabled = false;
        }

        private void Update()
        {
            PruneExpiredConsumers();
            UpdateStatusLed();

            if (!IsOnline)
            {
                if (_captureCamera != null) _captureCamera.enabled = false;
                return;
            }

            if (!IsFeedInUse)
            {
                if (_captureCamera != null) _captureCamera.enabled = false;
                return;
            }

            if (!_initialized) InitializeCamera();
            if (_captureCamera == null) return;

            int interval = Mathf.Clamp(renderIntervalFrames, 1, 10);
            if (interval == 1 || Time.frameCount % interval == 0)
                RenderFeed();
        }

        private void RenderFeed()
        {
            if (_captureCamera == null) return;

            _captureCamera.transform.localPosition = cameraOffset;
            Quaternion localRotation = Quaternion.Euler(cameraRotation);
            if (lensLooksAlongNegativeZ)
                localRotation *= Quaternion.Euler(0f, 180f, 0f);
            _captureCamera.transform.localRotation = localRotation;

            _captureCamera.fieldOfView = Mathf.Clamp(fieldOfView, 10f, 140f);
            _captureCamera.farClipPlane = Mathf.Max(5f, cameraRange);
            // Keep the capture camera enabled while a screen is sampling it. In URP this is
            // more reliable than relying only on Camera.Render(), and the target texture
            // retains its previous frame when the camera is later disabled.
            _captureCamera.enabled = true;
            _lastRenderedFrame = Time.frameCount;
        }

        private void PruneExpiredConsumers()
        {
            if (_feedConsumers.Count == 0) return;

            int staleBeforeFrame = Time.frameCount - 8;
            s_expiredConsumerIds.Clear();
            foreach (var kv in _feedConsumers)
            {
                if (kv.Value < staleBeforeFrame)
                    s_expiredConsumerIds.Add(kv.Key);
            }

            for (int i = 0; i < s_expiredConsumerIds.Count; i++)
                _feedConsumers.Remove(s_expiredConsumerIds[i]);
        }

        private static readonly List<EntityId> s_expiredConsumerIds = new();

        private void CacheStatusLed()
        {
            if (_statusLedRenderer == null)
            {
                Transform led = transform.Find("Generated_StatusLED");
                if (led != null) _statusLedRenderer = led.GetComponent<Renderer>();
            }

            if (_statusLedLight == null)
            {
                Transform light = transform.Find("Generated_StatusLED_Light");
                if (light != null) _statusLedLight = light.GetComponent<Light>();
            }

            _ledPropertyBlock ??= new MaterialPropertyBlock();
        }

        private void UpdateStatusLed()
        {
            CacheStatusLed();

            Color stateColor = !IsOnline ? offlineColor : IsFeedInUse ? feedInUseColor : onlineIdleColor;
            float pulse = !IsOnline ? 0.65f : IsFeedInUse ? 1.0f + Mathf.Sin(Time.realtimeSinceStartup * 4.5f) * 0.10f : 0.82f;
            Color emission = stateColor * Mathf.Max(0.1f, pulse);

            if (_statusLedRenderer != null)
            {
                _statusLedRenderer.GetPropertyBlock(_ledPropertyBlock);
                _ledPropertyBlock.SetColor("_Color", stateColor);
                _ledPropertyBlock.SetColor("_BaseColor", stateColor);
                _ledPropertyBlock.SetColor("_EmissionColor", emission);
                _statusLedRenderer.SetPropertyBlock(_ledPropertyBlock);
            }

            if (_statusLedLight != null)
            {
                _statusLedLight.enabled = true;
                _statusLedLight.color = stateColor;
                _statusLedLight.intensity = !IsOnline ? 0.45f : IsFeedInUse ? 1.65f : 0.95f;
            }
        }

        private void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            if (_captureCamera != null)
                Destroy(_captureCamera.gameObject);
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Camera Block";
            NormalizeCameraIdentityAndDefaults();
            CacheStatusLed();
            UpdateStatusLed();
        }
    }
}
