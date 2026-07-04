using System.Reflection;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Aligns the imported Crest test-scene ocean to IndustrialWorld's procedural
    /// water datum. Crest remains the visual renderer; voxel water remains the
    /// gameplay source for pumps, depth, swimming and maritime logic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrestVoxelWaterBinder : MonoBehaviour
    {
        [Tooltip("Optional viewpoint. If empty, Camera.main is used.")]
        public Transform viewpoint;

        [Tooltip("Small vertical adjustment for art tuning.")]
        public float waterHeightOffset = 0f;

        [Tooltip("On spherical worlds, the Crest ocean becomes a local tangent patch under the viewer instead of a global plane.")]
        public bool alignToPlanetSurface = true;

        [Tooltip("For flat worlds, keep the ocean centered at world origin instead of following the camera horizontally.")]
        public bool keepFlatOceanAtWorldOrigin = true;

        [Header("Depth Debug")]
        [SerializeField] private float sampledWaterDepth;
        [SerializeField] private float sampledSurfaceOffset;

        private Component _oceanRenderer;
        private FieldInfo _viewpointField;
        private Transform _cachedViewpoint;

        private void Awake()
        {
            _oceanRenderer = GetComponent(System.Type.GetType("Crest.OceanRenderer, Crest"));
            CacheOceanFields();
        }

        private void LateUpdate()
        {
            var world = ActiveWorld.Current;
            var view = ResolveViewpoint();
            if (world == null || view == null) return;

            if (_oceanRenderer == null)
                _oceanRenderer = GetComponent(System.Type.GetType("Crest.OceanRenderer, Crest"));
            if (_oceanRenderer != null && _viewpointField != null)
                _viewpointField.SetValue(_oceanRenderer, view);

            if (PlanetWaterUtility.IsPlanetWorld && alignToPlanetSurface)
                AlignPlanetPatch(world, view.position);
            else
                AlignFlatOcean(world, view.position);

            if (VoxelWaterDepthSampler.TrySampleDepth(view.position, out float depth, out float surface))
            {
                sampledWaterDepth = depth;
                sampledSurfaceOffset = surface;
            }
            else
            {
                sampledWaterDepth = 0f;
                sampledSurfaceOffset = 0f;
            }
        }

        private void CacheOceanFields()
        {
            if (_oceanRenderer == null) return;
            var type = _oceanRenderer.GetType();
            _viewpointField = type.GetField("_viewpoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private Transform ResolveViewpoint()
        {
            if (viewpoint != null) return viewpoint;
            if (_cachedViewpoint != null) return _cachedViewpoint;

            var cam = Camera.main;
            if (cam != null)
            {
                _cachedViewpoint = cam.transform;
                return _cachedViewpoint;
            }

            var anyCamera = Object.FindFirstObjectByType<Camera>();
            if (anyCamera != null)
                _cachedViewpoint = anyCamera.transform;

            return _cachedViewpoint;
        }

        private void AlignFlatOcean(IVoxelWorld world, Vector3 viewerPosition)
        {
            float seaY = world.SeaLevel * VoxelConstants.VOXEL_SIZE + waterHeightOffset;
            Vector3 position = keepFlatOceanAtWorldOrigin
                ? new Vector3(0f, seaY, 0f)
                : new Vector3(viewerPosition.x, seaY, viewerPosition.z);

            transform.SetPositionAndRotation(position, Quaternion.identity);
        }

        private void AlignPlanetPatch(IVoxelWorld world, Vector3 viewerPosition)
        {
            Vector3 up = PlanetWaterUtility.WorldUp(viewerPosition);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;

            float seaRadius = world.SeaLevel * VoxelConstants.VOXEL_SIZE + waterHeightOffset;
            Vector3 position = up.normalized * seaRadius;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up.normalized);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
