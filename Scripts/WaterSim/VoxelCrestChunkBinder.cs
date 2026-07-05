// Assets/Scripts/VoxelEngine/WaterSim/VoxelCrestChunkBinder.cs
//
// v3.12.0 — Voxel ↔ Crest LOD binder.
//
// Attached to every procedural water chunk GameObject. Emulates what
// Crest.OceanChunkRenderer does for its own infinite ocean tiles so the
// Crest/Ocean shader receives the per-instance data it needs to sample the
// LOD cascades (animated waves, foam, flow, shadow, sea-floor depth).
//
// Without this component, a MeshRenderer using the Crest/Ocean shader has
// no _LD_SliceIndex bound and the shader falls back to a flat, wave-less
// surface — which is exactly the "dark blue polygon" symptom we had.

using System.Reflection;
using UnityEngine;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Binds a voxel water chunk's MeshRenderer to the Crest ocean LOD system
    /// by choosing an appropriate LOD slice each frame and pushing it through
    /// a MaterialPropertyBlock. Works entirely via reflection so it compiles
    /// even if Crest is missing (component simply no-ops).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class VoxelCrestChunkBinder : MonoBehaviour
    {
        private static readonly int _spSliceIndex = Shader.PropertyToID("_LD_SliceIndex");
        private static readonly int _spReflection = Shader.PropertyToID("_ReflectionTex");
        private static readonly int _spMeshScaleLerp = Shader.PropertyToID("_MeshScaleLerp");
        private static readonly int _spGeometryData = Shader.PropertyToID("_GeometryData");
        private static readonly int _spFarNormalsWeight = Shader.PropertyToID("_FarNormalsWeight");
        private static readonly int _spChunkGeometryData = Shader.PropertyToID("_ChunkGeometryData");

        private static System.Type _oceanRendererType;
        private static PropertyInfo _oceanInstanceProp;
        private static PropertyInfo _oceanLodCountProp;
        private static PropertyInfo _oceanScaleProp;
        private static bool _reflectionResolved;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private int _lastFrame = -1;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            ResolveReflection();
        }

        private static void ResolveReflection()
        {
            if (_reflectionResolved) return;
            _reflectionResolved = true;

            _oceanRendererType = System.Type.GetType("Crest.OceanRenderer, Crest");
            if (_oceanRendererType == null) return;

            _oceanInstanceProp = _oceanRendererType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            _oceanLodCountProp = _oceanRendererType.GetProperty("CurrentLodCount",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _oceanScaleProp = _oceanRendererType.GetProperty("Scale",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private void LateUpdate()
        {
            // Throttle: bind once per frame per chunk – shader values only change once per frame.
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;

            if (_renderer == null || _oceanRendererType == null) return;

            object oceanInstance = _oceanInstanceProp != null
                ? _oceanInstanceProp.GetValue(null)
                : null;
            if (oceanInstance == null) return;

            int lodCount = _oceanLodCountProp != null
                ? (int)_oceanLodCountProp.GetValue(oceanInstance)
                : 8;
            if (lodCount <= 0) lodCount = 8;

            int slice = ChooseLodSlice(oceanInstance, lodCount);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetInt(_spSliceIndex, slice);
            _mpb.SetFloat(_spMeshScaleLerp, slice == 0 ? 0f : 1f);
            _mpb.SetFloat(_spFarNormalsWeight, 1f);
            // Reasonable defaults for _GeometryData (used for normal fade / anti-alias).
            // XY = chunk res / world size, Z = 1/geom size, W = normal fade factor.
            _mpb.SetVector(_spGeometryData, new Vector4(1f, 0.05f, 32f, 1f));
            _mpb.SetVector(_spChunkGeometryData, new Vector4(32f, 0f, 0f, 0f));
            if (_renderer.sharedMaterial != null &&
                !_renderer.sharedMaterial.HasProperty(_spReflection))
            {
                // Some Crest builds don't declare _ReflectionTex on the ocean mat, skip.
            }
            else
            {
                _mpb.SetTexture(_spReflection, Texture2D.blackTexture);
            }
            _renderer.SetPropertyBlock(_mpb);
        }

        private int ChooseLodSlice(object oceanInstance, int lodCount)
        {
            // Pick the LOD slice whose world-space grid best matches the distance
            // from the viewer to this chunk. Slice 0 is finest / smallest, higher
            // slices double their footprint each level.
            Camera cam = Camera.main != null ? Camera.main : Camera.current;
            if (cam == null) return 0;

            float scale = 1f;
            if (_oceanScaleProp != null)
            {
                object rawScale = _oceanScaleProp.GetValue(oceanInstance);
                if (rawScale is float f) scale = Mathf.Max(0.001f, f);
            }

            Bounds b = _renderer.bounds;
            float dist = Vector3.Distance(cam.transform.position, b.center);

            // Slice n covers roughly `scale * 2^n * baseTileSize`.
            const float baseTile = 32f; // matches VoxelConstants.CHUNK_SIZE
            float ratio = Mathf.Max(1f, dist / (baseTile * scale));
            int slice = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(ratio, 2f)), 0, lodCount - 1);
            return slice;
        }
    }
}
