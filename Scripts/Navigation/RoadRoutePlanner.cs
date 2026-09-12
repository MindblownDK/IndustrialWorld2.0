using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Environment;

namespace IndustrialWorld.Navigation
{
    /// <summary>Bounded A* over loaded pavement. No terrain shortcuts or flight commands.</summary>
    public static class RoadRoutePlanner
    {
        public const int NodeBudget = 4096;
        public const float EndpointReach = 8f;

        private readonly struct Entry
        {
            public readonly AsphaltRoad Road;
            public readonly float Score;
            public readonly int Order;
            public Entry(AsphaltRoad road, float score, int order)
            { Road = road; Score = score; Order = order; }
        }

        private sealed class EntryComparer : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b)
            {
                int score = a.Score.CompareTo(b.Score);
                return score != 0 ? score : a.Order.CompareTo(b.Order);
            }
        }

        public static bool IsVehicleRoad(AsphaltRoad road) => road != null
            && road.isActiveAndEnabled && road.surfaceKind != RoadSurfaceKind.Pathway
            && (road.IsSupported || road.Span != null);

        public static bool IsBlocked(AsphaltRoad road) => !IsVehicleRoad(road)
            || (road.Span != null && road.Span.RoadTrafficBlocked);

        public static bool AreConnected(AsphaltRoad a, AsphaltRoad b)
        {
            if (!IsVehicleRoad(a) || !IsVehicleRoad(b)) return false;
            if (a.hasExplicitFootprint && b.hasExplicitFootprint)
            {
                // Corridor bends share quad edges, not a global square lattice.
                float tolerance = Mathf.Max(0.03f, Mathf.Min(a.cellSize, b.cellSize) * 0.08f);
                for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    if (SameCorner(a, Corner(a, i), Corner(b, (j + 1) % 4), tolerance)
                        && SameCorner(a, Corner(a, (i + 1) % 4), Corner(b, j), tolerance)) return true;
                return false;
            }
            // Reciprocal checks reject stacked roads and mismatched cell-size joins.
            return FitsSlot(a, b) && FitsSlot(b, a);
        }

        private static Vector3 Corner(AsphaltRoad road, int index)
        {
            Vector3 local = index == 0 ? road.quadSW : index == 1 ? road.quadSE
                : index == 2 ? road.quadNE : road.quadNW;
            return road.transform.TransformPoint(local);
        }

        private static bool SameCorner(AsphaltRoad road, Vector3 a, Vector3 b, float tolerance)
        {
            Vector3 delta = b - a;
            float height = Vector3.Dot(delta, road.transform.up);
            return Mathf.Abs(height) <= road.cellSize * 0.95f
                && (delta - road.transform.up * height).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool FitsSlot(AsphaltRoad a, AsphaltRoad b)
        {
            var t = a.transform;
            return RoadSurfaceUtility.IsNeighbourSlot(a, b, t.forward, t.up, a.cellSize)
                || RoadSurfaceUtility.IsNeighbourSlot(a, b, -t.forward, t.up, a.cellSize)
                || RoadSurfaceUtility.IsNeighbourSlot(a, b, t.right, t.up, a.cellSize)
                || RoadSurfaceUtility.IsNeighbourSlot(a, b, -t.right, t.up, a.cellSize);
        }

        public static AsphaltRoad FindEndpoint(Vector3 point, List<AsphaltRoad> scratch, bool includeBlocked = false)
        {
            RoadSurfaceUtility.QueryNearby(point, 16f, scratch);
            var best = PickSurface(point, scratch, includeBlocked);
            if (best != null && (ClosestSurfacePoint(best, point) - point).sqrMagnitude < 0.0001f) return best;
            RoadSurfaceUtility.CopyRegistered(scratch, NodeBudget * 2);
            var fallback = PickSurface(point, scratch, includeBlocked);
            if (best == null) return fallback;
            return fallback != null && (ClosestSurfacePoint(fallback, point) - point).sqrMagnitude
                < (ClosestSurfacePoint(best, point) - point).sqrMagnitude ? fallback : best;
        }

        private static AsphaltRoad PickSurface(Vector3 point, List<AsphaltRoad> roads, bool includeBlocked)
        {
            AsphaltRoad best = null;
            float distance = EndpointReach * EndpointReach + 0.001f;
            foreach (var road in roads)
            {
                if (!IsVehicleRoad(road) || (!includeBlocked && IsBlocked(road))) continue;
                Vector3 nearest = ClosestSurfacePoint(road, point);
                float candidate = (point - nearest).sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate; best = road;
            }
            return best;
        }

        public static Vector3 ClosestSurfacePoint(AsphaltRoad road, Vector3 point)
        {
            float offset = road.SurfaceOffset(point, road.transform.up);
            if (!float.IsNaN(offset) && !float.IsInfinity(offset)) return point - road.transform.up * offset;
            Vector3 local = road.transform.InverseTransformPoint(point);
            if (!road.hasExplicitFootprint)
            {
                float half = road.cellSize * 0.5f;
                local.x = Mathf.Clamp(local.x, -half, half); local.z = Mathf.Clamp(local.z, -half, half);
            }
            else
            {
                Vector3 best = road.quadSW; float distance = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = i == 0 ? road.quadSW : i == 1 ? road.quadSE : i == 2 ? road.quadNE : road.quadNW;
                    Vector3 b = i == 0 ? road.quadSE : i == 1 ? road.quadNE : i == 2 ? road.quadNW : road.quadSW;
                    Vector3 edge = b - a; edge.y = 0f;
                    Vector3 delta = local - a; delta.y = 0f;
                    Vector3 candidate = a + edge * Mathf.Clamp01(Vector3.Dot(delta, edge) / Mathf.Max(0.0001f, edge.sqrMagnitude));
                    float d = (delta - edge * Mathf.Clamp01(Vector3.Dot(delta, edge) / Mathf.Max(0.0001f, edge.sqrMagnitude))).sqrMagnitude;
                    if (d < distance) { distance = d; best = candidate; }
                }
                // Step a hair inside the quad so winding round-off does not reject an edge.
                local = Vector3.Lerp(best, (road.quadSW + road.quadSE + road.quadNE + road.quadNW) * 0.25f, 0.001f);
            }
            return RoadNavigationAnchor.SurfacePoint(road, road.transform.TransformPoint(local));
        }

        public static bool TryPlan(Vector3 from, Vector3 to, List<AsphaltRoad> result,
            out string reason)
        {
            result.Clear();
            var nearby = new List<AsphaltRoad>(32);
            var start = FindEndpoint(from, nearby);
            var goal = FindEndpoint(to, nearby);
            if (start == null || goal == null)
            {
                reason = start == null ? "Vehicle/start point has no available loaded road SURFACE within 8 m. Check grounded tyres and route origin."
                    : "Destination point has no available loaded road SURFACE within 8 m. Old pivot-based recordings may need re-recording.";
                return false;
            }
            return TryPlan(start, goal, result, out reason);
        }

        public static bool TryPlan(AsphaltRoad start, AsphaltRoad goal, List<AsphaltRoad> result, out string reason)
        {
            result.Clear();
            if (IsBlocked(start) || IsBlocked(goal)) { reason = "Start/end road is unavailable."; return false; }
            var nearby = new List<AsphaltRoad>(32);
            var frontier = new SortedSet<Entry>(new EntryComparer());
            var costs = new Dictionary<AsphaltRoad, float>();
            var parents = new Dictionary<AsphaltRoad, AsphaltRoad>();
            var visited = new HashSet<AsphaltRoad>();
            int order = 0;
            costs[start] = 0f;
            frontier.Add(new Entry(start, Vector3.Distance(start.transform.position, goal.transform.position), order++));
            while (frontier.Count > 0)
            {
                var entry = frontier.Min;
                frontier.Remove(entry);
                var current = entry.Road;
                if (!visited.Add(current)) continue;
                if (current == goal)
                {
                    for (var at = goal; at != null; at = parents.TryGetValue(at, out var parent) ? parent : null)
                        result.Add(at);
                    result.Reverse();
                    reason = "Loaded road route ready.";
                    return true;
                }
                RoadSurfaceUtility.QueryNearby(current.transform.position,
                    Mathf.Min(16f, Mathf.Max(1f, current.cellSize * 1.8f)), nearby);
                foreach (var next in nearby)
                {
                    if (IsBlocked(next) || visited.Contains(next) || !AreConnected(current, next)) continue;
                    float cost = costs[current] + Vector3.Distance(current.transform.position, next.transform.position);
                    if (costs.TryGetValue(next, out float old) && old <= cost) continue;
                    if (!costs.ContainsKey(next) && costs.Count >= NodeBudget)
                    {
                        reason = "Road search reached its 4096-cell limit. Choose a closer waymark.";
                        return false;
                    }
                    costs[next] = cost;
                    parents[next] = current;
                    frontier.Add(new Entry(next, cost + Vector3.Distance(next.transform.position,
                        goal.transform.position), order++));
                }
            }
            reason = "No connected, available loaded-road route. Check gaps, crossings and road loading.";
            return false;
        }
    }
}
