using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Environment;

namespace IndustrialWorld.Navigation
{
    /// <summary>Explicit two-end run on a complete loaded, uniform-width corridor. Lateral
    /// lane cells form rows, so a normal multi-lane road is not mistaken for a junction.</summary>
    public static class RoadNetworkRun
    {
        public static bool TryPlan(Vector3 position, List<AsphaltRoad> approach, List<AsphaltRoad> across, out string reason)
        {
            approach.Clear(); across.Clear();
            var scratch = new List<AsphaltRoad>();
            var seed = RoadRoutePlanner.FindEndpoint(position, scratch);
            if (seed == null) { reason = "No available road surface under/near the vehicle. Check tyre contacts."; return false; }
            var roads = new List<AsphaltRoad> { seed };
            var indices = new Dictionary<AsphaltRoad, int> { [seed] = 0 };
            var links = new List<List<int>>();
            for (int cursor = 0; cursor < roads.Count; cursor++)
            {
                var road = roads[cursor]; var neighbours = new List<int>(); links.Add(neighbours);
                RoadSurfaceUtility.QueryNearby(road.transform.position, Mathf.Min(16f, Mathf.Max(1f, road.cellSize * 1.8f)), scratch);
                foreach (var next in scratch)
                {
                    if (next == road || !RoadRoutePlanner.AreConnected(road, next)) continue;
                    if (RoadRoutePlanner.IsBlocked(next)) { reason = "A crossing/road in this network is unavailable. Wait or select a specific destination."; return false; }
                    if (!indices.TryGetValue(next, out int id))
                    {
                        if (roads.Count >= RoadRoutePlanner.NodeBudget) { reason = "Network exceeds the 4096-cell budget."; return false; }
                        id = roads.Count; indices[next] = id; roads.Add(next);
                    }
                    neighbours.Add(id);
                }
            }
            var parent = new int[roads.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            for (int i = 0; i < roads.Count; i++)
                foreach (int j in links[i])
                {
                    Vector3 delta = roads[j].transform.position - roads[i].transform.position;
                    bool lateralA = Mathf.Abs(Vector3.Dot(delta, roads[i].transform.forward)) < Mathf.Abs(Vector3.Dot(delta, roads[i].transform.right)) * 0.5f;
                    bool lateralB = Mathf.Abs(Vector3.Dot(delta, roads[j].transform.forward)) < Mathf.Abs(Vector3.Dot(delta, roads[j].transform.right)) * 0.5f;
                    if (lateralA && lateralB) parent[Root(parent, j)] = Root(parent, i);
                }
            var rows = new Dictionary<int, List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                int root = Root(parent, i);
                if (!rows.TryGetValue(root, out var row)) rows[root] = row = new List<int>();
                row.Add(i);
            }
            int width = -1;
            var ends = new List<AsphaltRoad>(2);
            foreach (var pair in rows)
            {
                if (width < 0) width = pair.Value.Count;
                if (pair.Value.Count != width) { reason = "Network branches, changes lane width or has an irregular junction. Choose an explicit destination."; return false; }
                var neighbours = new HashSet<int>(); Vector3 centre = Vector3.zero;
                foreach (int i in pair.Value)
                {
                    centre += RoadNavigationAnchor.SurfaceCentre(roads[i]);
                    foreach (int j in links[i]) if (Root(parent, j) != pair.Key) neighbours.Add(Root(parent, j));
                }
                if (neighbours.Count > 2) { reason = "Branched network: choose a destination instead of guessing its other end."; return false; }
                centre /= pair.Value.Count;
                foreach (int i in pair.Value)
                    if (Vector3.Distance(RoadNavigationAnchor.SurfaceCentre(roads[i]), centre) > 8f)
                    { reason = "Network row is too wide or misaligned; choose a specific destination."; return false; }
                if (neighbours.Count != 1) continue;
                AsphaltRoad cap = null; float best = float.MaxValue;
                foreach (int i in pair.Value)
                {
                    float d = (RoadNavigationAnchor.SurfaceCentre(roads[i]) - centre).sqrMagnitude;
                    if (d < best) { best = d; cap = roads[i]; }
                }
                ends.Add(cap);
            }
            if (ends.Count != 2) { reason = "This loaded network does not have exactly two ends (loop, junction or too short). Choose a destination."; return false; }
            var otherApproach = new List<AsphaltRoad>();
            if (!RoadRoutePlanner.TryPlan(seed, ends[0], approach, out reason)
                || !RoadRoutePlanner.TryPlan(seed, ends[1], otherApproach, out reason)) return false;
            bool first = Length(approach) <= Length(otherApproach);
            if (!first) { approach.Clear(); approach.AddRange(otherApproach); }
            if (!RoadRoutePlanner.TryPlan(first ? ends[0] : ends[1], first ? ends[1] : ends[0], across, out reason)) return false;
            reason = "Near-end approach " + Length(approach).ToString("0") + " m, then end-to-end " + Length(across).ToString("0")
                + " m. Low-speed reversing may be used; no off-road approach or U-turn is invented.";
            return across.Count >= 2;
        }

        private static int Root(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }
        private static float Length(List<AsphaltRoad> path)
        {
            float distance = 0f;
            for (int i = 1; i < path.Count; i++) distance += Vector3.Distance(RoadNavigationAnchor.SurfaceCentre(path[i - 1]), RoadNavigationAnchor.SurfaceCentre(path[i]));
            return distance;
        }
    }
}
