using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Keeps each liquid-surface chunk on the correct visual LOD as the camera moves.
    /// The mesh builder remains chunk-local and save-compatible; this component only
    /// schedules a rebuild when a surface crosses a distance band.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterSurfaceLodController : MonoBehaviour
    {
        private const float CheckIntervalSeconds = 0.45f;

        private Chunk _chunk;
        private int _lastStride = -1;
        private float _nextCheckTime;

        public void Configure(Chunk chunk)
        {
            _chunk = chunk;
            _lastStride = WaterMeshBuilder.GetChunkLodStride(chunk);
            _nextCheckTime = Time.unscaledTime + Random.Range(0f, CheckIntervalSeconds);
        }

        private void OnEnable()
        {
            _nextCheckTime = Time.unscaledTime + Random.Range(0f, CheckIntervalSeconds);
        }

        private void Update()
        {
            if (_chunk == null || Time.unscaledTime < _nextCheckTime) return;
            _nextCheckTime = Time.unscaledTime + CheckIntervalSeconds;

            int stride = WaterMeshBuilder.GetChunkLodStride(_chunk);
            if (stride == _lastStride) return;

            _lastStride = stride;
            WaterMeshBuilder.Schedule(_chunk);
        }
    }
}
