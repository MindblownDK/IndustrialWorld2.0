// Assets/Scripts/VoxelEngine/GridSystem/GridCameraBlock.cs
//
// A camera block that captures live video and feeds it to nearby GridScreenBlocks.
// Place on a grid, right-click to configure which screen(s) display the feed.
// v5.48.0-dev — Camera feed for grid screens.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridCameraBlock : GridBlock, IGridDataProvider
    {
        [Header("Camera")]
        public float fieldOfView = 70f;
        public float cameraRange = 100f;
        public Vector3 cameraOffset = new Vector3(0f, 0.3f, 0f);
        public Vector3 cameraRotation = Vector3.zero;

        private Camera _captureCamera;
        private RenderTexture _renderTexture;
        private bool _initialized;

        public RenderTexture FeedTexture
        {
            get
            {
                if (!_initialized) InitializeCamera();
                return _renderTexture;
            }
        }

        public string SourceName => blockName;
        public string DataCategory => "Camera";
        public string GetDisplayData()
        {
            return $"CAMERA\nFeed: {(int)(fieldOfView)}°\n{cameraRange}m range\n{(Enabled && Grid != null && Grid.HasPower ? "ACTIVE" : "OFFLINE")}";
        }

        // Power draw for camera
        public override float PowerDraw => Enabled ? 30f : 0f;

        private void InitializeCamera()
        {
            _initialized = true;

            // Create render texture
            _renderTexture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32)
            {
                name = "CameraBlock_Feed",
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp
            };
            _renderTexture.Create();

            // Create a hidden camera
            var camGo = new GameObject("CameraBlock_FeedCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = cameraOffset;
            camGo.transform.localRotation = Quaternion.Euler(cameraRotation);
            camGo.hideFlags = HideFlags.HideAndDontSave;

            _captureCamera = camGo.AddComponent<Camera>();
            _captureCamera.fieldOfView = fieldOfView;
            _captureCamera.farClipPlane = cameraRange;
            _captureCamera.nearClipPlane = 0.1f;
            _captureCamera.targetTexture = _renderTexture;
            _captureCamera.cullingMask = ~0; // render everything
            _captureCamera.enabled = false; // we render manually
        }

        private void Update()
        {
            if (!Enabled || Grid == null || !Grid.HasPower)
            {
                if (_captureCamera != null) _captureCamera.enabled = false;
                return;
            }

            if (!_initialized) InitializeCamera();
            if (_captureCamera == null) return;

            // Manually render the camera every few frames for performance
            if (Time.frameCount % 3 == 0)
            {
                _captureCamera.Render();
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
            blockName = "Camera";
        }
    }
}
