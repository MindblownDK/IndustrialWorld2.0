using UnityEngine;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Experimental bridge placeholder kept only so old scenes that contain this
    /// component do not throw missing-component errors. The active visual path is
    /// currently generated voxel/chunk water; this component intentionally does no
    /// runtime mesh work until the proper spherical Crest patch bridge is rebuilt.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralWaterPatchRenderer : MonoBehaviour
    {
        [Header("Deprecated / Disabled")]
        [Tooltip("This experimental renderer is disabled. Generated voxel water is the active visual path.")]
        public bool disabledUntilCrestPatchBridge = true;

        [Header("Legacy Settings (unused)")]
        public Transform viewpoint;
        [UnityEngine.Range(64f, 1024f)] public float searchRadius = 512f;
        [UnityEngine.Range(4f, 64f)] public float tileSize = 16f;
        [UnityEngine.Range(8, 96)] public int maxTilesPerAxis = 48;
        [UnityEngine.Range(0.1f, 3f)] public float rebuildInterval = 0.35f;
        public float waterHeightOffset = 0.03f;
        public float shallowDepth = 2.5f;
        public float deepDepth = 24f;
        [UnityEngine.Range(0f, 2f)] public float flowVisualStrength = 1f;
        public Material waterMaterial;

        private void Awake()
        {
            DisableRenderers();
            enabled = false;
        }

        private void OnEnable()
        {
            DisableRenderers();
            enabled = false;
        }

        private void DisableRenderers()
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.enabled = false;
        }
    }
}
