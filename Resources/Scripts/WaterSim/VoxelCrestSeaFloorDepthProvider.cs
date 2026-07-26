using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Feeds voxel-derived sea floor depth into Crest's global shader system.
    /// Enables proper shallow water tinting and shoreline foam without a baked depth cache.
    /// v3.20.0
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class VoxelCrestSeaFloorDepthProvider : MonoBehaviour
    {
        [Header("Sampling")]
        public Transform sampleCenter;
        [Range(8f, 128f)] public float sampleRadius = 48f;
        [Range(4, 24)] public int samplesPerAxis = 12;
        [Range(0.1f, 1f)] public float updateInterval = 0.25f;

        [Header("Depth Remap")]
        public float shallowDepth = 2.2f;
        public float deepDepth = 24f;

        private float _nextUpdate;
        private static readonly int _CrestDepthID = Shader.PropertyToID("_CrestWaterDepth");
        private static readonly int _CrestDepthShallowID = Shader.PropertyToID("_CrestWaterDepthShallow");
        private static readonly int _CrestDepthDeepID = Shader.PropertyToID("_CrestWaterDepthDeep");

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + updateInterval;
            SampleAndPush();
        }

        private void SampleAndPush()
        {
            var world = ActiveWorld.Current;
            if (world == null) return;
            var centerT = sampleCenter != null ? sampleCenter : (world.Viewer != null ? world.Viewer : Camera.main != null ? Camera.main.transform : transform);
            if (centerT == null) return;

            Vector3 center = centerT.position;
            float step = (sampleRadius * 2f) / Mathf.Max(1, samplesPerAxis - 1);
            float depthSum = 0f;
            int hits = 0;
            float minDepth = float.MaxValue;
            float maxDepth = 0f;

            for (int z = 0; z < samplesPerAxis; z++)
            for (int x = 0; x < samplesPerAxis; x++)
            {
                float ox = (x - samplesPerAxis * 0.5f) * step;
                float oz = (z - samplesPerAxis * 0.5f) * step;
                Vector3 samplePos = center + new Vector3(ox, 0f, oz);

                if (VoxelWaterDepthSampler.TrySampleDepth(samplePos, out float depth, out _))
                {
                    depthSum += depth;
                    hits++;
                    if (depth < minDepth) minDepth = depth;
                    if (depth > maxDepth) maxDepth = depth;
                }
            }

            float avgDepth = hits > 0 ? depthSum / hits : deepDepth;
            if (minDepth == float.MaxValue) minDepth = avgDepth;

            // Push to Crest + our voxel water shaders
            Shader.SetGlobalFloat(_CrestDepthID, avgDepth);
            Shader.SetGlobalFloat(_CrestDepthShallowID, shallowDepth);
            Shader.SetGlobalFloat(_CrestDepthDeepID, deepDepth);
            Shader.SetGlobalFloat("_VoxelWaterDepth", avgDepth);
            Shader.SetGlobalFloat("_VoxelWaterDepthMin", minDepth);
            Shader.SetGlobalFloat("_VoxelWaterDepthMax", maxDepth);

            // Help Crest shoreline foam: expose an approximate sea floor height
            float seaLevel = world.SeaLevel * VoxelConstants.VOXEL_SIZE;
            Shader.SetGlobalFloat("_CrestSeaFloorMin", seaLevel - maxDepth);
        }
    }
}
