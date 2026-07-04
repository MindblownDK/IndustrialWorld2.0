using Crest;
using Crest.Spline;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Samples Crest flow splines and exposes them to IndustrialWorld systems.
    /// This keeps visual river/current authoring inside Crest while machines,
    /// waterwheels and maritime blocks can still ask the existing WaterProbeSystem
    /// for one unified flow velocity.
    /// </summary>
    public static class CrestFlowSampler
    {
        private const float DefaultBlendRadius = 6f;
        private const float MinSegmentLength = 0.001f;

        private static Spline[] _splines = System.Array.Empty<Spline>();
        private static int _cachedFrame = -1;

        public static bool TrySampleFlow(Vector3 worldPosition, out float3 flow)
        {
            flow = float3.zero;
            RefreshCache();

            if (_splines.Length == 0) return false;

            Vector3 bestFlow = Vector3.zero;
            float bestWeight = 0f;

            for (int s = 0; s < _splines.Length; s++)
            {
                var spline = _splines[s];
                if (spline == null || !spline.isActiveAndEnabled) continue;

                var points = spline.GetComponentsInChildren<SplinePoint>(includeInactive: false);
                if (points == null || points.Length < 2) continue;

                float radius = Mathf.Max(0.25f, spline.Radius > 0f ? spline.Radius : DefaultBlendRadius);
                int segmentCount = spline._closed ? points.Length : points.Length - 1;

                for (int i = 0; i < segmentCount; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Length];
                    if (a == null || b == null) continue;

                    Vector3 pa = a.transform.position;
                    Vector3 pb = b.transform.position;
                    Vector3 ab = pb - pa;
                    ab.y = 0f;
                    float len = ab.magnitude;
                    if (len < MinSegmentLength) continue;

                    Vector3 pointFlat = worldPosition;
                    pointFlat.y = pa.y;
                    float t = Mathf.Clamp01(Vector3.Dot(pointFlat - pa, ab) / (len * len));
                    Vector3 closest = pa + ab * t;
                    float distance = Vector2.Distance(new Vector2(worldPosition.x, worldPosition.z), new Vector2(closest.x, closest.z));
                    if (distance > radius) continue;

                    float speedA = GetPointSpeed(a);
                    float speedB = GetPointSpeed(b);
                    float speed = Mathf.Lerp(speedA, speedB, t);
                    Vector3 direction = ab / len;
                    float weight = 1f - Mathf.Clamp01(distance / radius);
                    weight *= weight;

                    bestFlow += direction * speed * weight;
                    bestWeight += weight;
                }
            }

            if (bestWeight <= 0.0001f) return false;

            Vector3 result = bestFlow / bestWeight;
            flow = new float3(result.x, 0f, result.z);
            return math.lengthsq(flow) > 0.0001f;
        }

        private static float GetPointSpeed(SplinePoint point)
        {
            if (point != null && point.TryGetComponent<SplinePointDataFlow>(out var flowData))
                return flowData.FlowVelocity;
            return SplinePointDataFlow.k_defaultSpeed;
        }

        private static void RefreshCache()
        {
            int frame = Time.frameCount;
            if (_cachedFrame == frame) return;
            _cachedFrame = frame;
            _splines = Object.FindObjectsByType<Spline>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }
    }
}
