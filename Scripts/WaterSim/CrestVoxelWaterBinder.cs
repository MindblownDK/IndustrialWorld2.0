using System.Reflection;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Binds Crest's imported ocean renderer to procedural voxel water.
    /// It enables Crest only near real generated water and aligns the visual water
    /// to the sampled voxel surface so it does not behave like a random plane in space.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrestVoxelWaterBinder : MonoBehaviour
    {
        public Transform viewpoint;
        public float waterHeightOffset = 0f;
        public bool alignToPlanetSurface = true;
        public bool followNearestProceduralWater = true;
        public float waterSearchRadius = 512f;
        public float waterSearchSpacing = 32f;

        [Header("Depth Debug")]
        [SerializeField] private bool proceduralWaterFound;
        [SerializeField] private float sampledWaterDepth;
        [SerializeField] private float sampledSurfaceOffset;

        private Component _oceanRenderer;
        private Behaviour _oceanBehaviour;
        private FieldInfo _viewpointField;
        private Transform _cachedViewpoint;
        private Renderer[] _renderers;

        private void Awake()
        {
            CacheOcean();
        }

        private void OnEnable()
        {
            CacheOcean();
        }

        private void LateUpdate()
        {
            var world = ActiveWorld.Current;
            var view = ResolveViewpoint();
            if (world == null || view == null) return;

            CacheOcean();
            if (_oceanRenderer != null && _viewpointField != null)
                _viewpointField.SetValue(_oceanRenderer, view);

            proceduralWaterFound = VoxelWaterDepthSampler.TryFindNearbyWater(
                view.position,
                waterSearchRadius,
                waterSearchSpacing,
                out Vector3 waterPosition,
                out float depth,
                out float surface);

            sampledWaterDepth = proceduralWaterFound ? depth : 0f;
            sampledSurfaceOffset = proceduralWaterFound ? surface : 0f;

            SetCrestVisualActive(proceduralWaterFound);
            if (!proceduralWaterFound) return;

            if (PlanetWaterUtility.IsPlanetWorld && alignToPlanetSurface)
                AlignPlanetPatch(world, waterPosition);
            else
                AlignFlatPatch(waterPosition, surface);
        }

        private void CacheOcean()
        {
            var oceanType = System.Type.GetType("Crest.OceanRenderer, Crest");
            if (oceanType != null && _oceanRenderer == null)
            {
                _oceanRenderer = GetComponent(oceanType);
                _oceanBehaviour = _oceanRenderer as Behaviour;
            }

            if (_oceanRenderer != null && _viewpointField == null)
                _viewpointField = _oceanRenderer.GetType().GetField("_viewpoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        private Transform ResolveViewpoint()
        {
            if (viewpoint != null) return viewpoint;
            if (_cachedViewpoint != null) return _cachedViewpoint;

            var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            _cachedViewpoint = cam != null ? cam.transform : null;
            return _cachedViewpoint;
        }

        private void SetCrestVisualActive(bool active)
        {
            if (_oceanBehaviour != null && _oceanBehaviour.enabled != active)
                _oceanBehaviour.enabled = active;

            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null && _renderers[i].enabled != active)
                    _renderers[i].enabled = active;
            }
        }

        private void AlignFlatPatch(Vector3 waterPosition, float surfaceHeight)
        {
            float seaY = surfaceHeight + waterHeightOffset;
            transform.SetPositionAndRotation(new Vector3(waterPosition.x, seaY, waterPosition.z), Quaternion.identity);
        }

        private void AlignPlanetPatch(IVoxelWorld world, Vector3 waterPosition)
        {
            Vector3 up = PlanetWaterUtility.WorldUp(waterPosition);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            float seaRadius = world.SeaLevel * VoxelConstants.VOXEL_SIZE + waterHeightOffset;
            Vector3 position = up * seaRadius;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
