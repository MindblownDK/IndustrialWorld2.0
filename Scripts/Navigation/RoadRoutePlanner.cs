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

        private static AsphaltRoad FindEndpoint(Vector3 point, List<AsphaltRoad> scratch)
        {
            RoadSurfaceUtility.QueryNearby(point, EndpointReach, scratch);
            AsphaltRoad best = null;
            float distance = EndpointReach * EndpointReach;
            foreach (var road in scratch)
            {
                if (IsBlocked(road)) continue;
                float candidate = (point - road.transform.position).sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate;
                best = road;
            }
            return best;
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
                reason = "Both endpoints need an available vehicle road within 8 m.";
                return false;
            }
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
                    reason = "Road route ready. Manual driving only.";
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
