using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Environment;

namespace IndustrialWorld.Navigation
{
    /// <summary>Explicit two-end run on a complete loaded, uniform-width corridor. Lateral
    /// lane cells form rows, so a normal multi-lane road is not mistaken for a junction.
    /// v9.56: lateral grouping uses rendered surface centres, and saved-network validation
    /// uses footprint identity instead of arbitrary centre-tile distance checks.</summary>
    public static class RoadNetworkRun
    {
        public static bool TryPlan(Vector3 position, List<AsphaltRoad> approach, List<AsphaltRoad> across, out string reason)
        {
            return TryPlanInternal(position, null, null, approach, across, out reason);
        }

        /// <summary>
        /// Resolves a previously saved two-end network run.
        /// The saved endpoint footprints (world positions) identify the corridor;
        /// validation checks they remain in opposite end rows and that the vehicle
        /// belongs to the same loaded component. No arbitrary 8m centre-tile matching.
        /// </summary>
        public static bool TryResolveSavedNetwork(Vector3 vehiclePosition, Vector3 savedA, Vector3 savedB,
            List<AsphaltRoad> approach, List<AsphaltRoad> across, out string reason)
        {
            return TryPlanInternal(vehiclePosition, savedA, savedB, approach, across, out reason);
        }

        private static bool TryPlanInternal(Vector3 vehiclePos, Vector3? savedA, Vector3? savedB,
            List<AsphaltRoad> approach, List<AsphaltRoad> across, out string reason)
        {
            approach.Clear(); across.Clear();
            var scratch = new List<AsphaltRoad>();
            var seed = RoadRoutePlanner.FindEndpoint(vehiclePos, scratch);
            if (seed == null) { reason = "No available road surface under/near the vehicle. Check tyre contacts."; return false; }

            // Build full connected component
            var roads = new List<AsphaltRoad> { seed };
            var indices = new Dictionary<AsphaltRoad, int> { [seed] = 0 };
            var links = new List<List<int>>();
            for (int cursor = 0; cursor < roads.Count; cursor++)
            {
                var road = roads[cursor];
                var neighbours = new List<int>();
                links.Add(neighbours);
                RoadSurfaceUtility.QueryNearby(RoadNavigationAnchor.SurfaceCentre(road), Mathf.Min(16f, Mathf.Max(1f, road.cellSize * 1.8f)), scratch);
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

            // Lateral grouping using rendered surface centres, not construction pivots
            var parent = new int[roads.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            for (int i = 0; i < roads.Count; i++)
            {
                Vector3 centreI = RoadNavigationAnchor.SurfaceCentre(roads[i]);
                foreach (int j in links[i])
                {
                    Vector3 centreJ = RoadNavigationAnchor.SurfaceCentre(roads[j]);
                    Vector3 delta = centreJ - centreI;
                    // Use each road's own forward/right for lateral test, but delta is surface-based
                    bool lateralA = Mathf.Abs(Vector3.Dot(delta, roads[i].transform.forward)) < Mathf.Abs(Vector3.Dot(delta, roads[i].transform.right)) * 0.5f;
                    bool lateralB = Mathf.Abs(Vector3.Dot(delta, roads[j].transform.forward)) < Mathf.Abs(Vector3.Dot(delta, roads[j].transform.right)) * 0.5f;
                    if (lateralA && lateralB) parent[Root(parent, j)] = Root(parent, i);
                }
            }

            var rows = new Dictionary<int, List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                int root = Root(parent, i);
                if (!rows.TryGetValue(root, out var row)) rows[root] = row = new List<int>();
                row.Add(i);
            }

            // Build row neighbour sets and centres
            var rowNeighbours = new Dictionary<int, HashSet<int>>();
            var rowCentres = new Dictionary<int, Vector3>();
            foreach (var pair in rows)
            {
                var neighbours = new HashSet<int>();
                Vector3 centre = Vector3.zero;
                foreach (int idx in pair.Value)
                {
                    centre += RoadNavigationAnchor.SurfaceCentre(roads[idx]);
                    foreach (int j in links[idx])
                    {
                        int rootJ = Root(parent, j);
                        if (rootJ != pair.Key) neighbours.Add(rootJ);
                    }
                }
                centre /= Mathf.Max(1, pair.Value.Count);
                rowNeighbours[pair.Key] = neighbours;
                rowCentres[pair.Key] = centre;
            }

            int width = -1;
            var ends = new List<AsphaltRoad>(2);
            var endRowRoots = new List<int>(2);
            foreach (var pair in rows)
            {
                if (width < 0) width = pair.Value.Count;
                if (pair.Value.Count != width) { reason = "Network branches, changes lane width or has an irregular junction. Choose an explicit destination."; return false; }
                var neighbours = rowNeighbours[pair.Key];
                Vector3 centre = rowCentres[pair.Key];
                if (neighbours.Count > 2) { reason = "Branched network: choose a destination instead of guessing its other end."; return false; }
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
                endRowRoots.Add(pair.Key);
            }

            if (ends.Count != 2) { reason = "This loaded network does not have exactly two ends (loop, junction or too short). Choose a destination."; return false; }

            // If we are resolving a saved network, validate footprints
            if (savedA.HasValue && savedB.HasValue)
            {
                AsphaltRoad roadA = RoadRoutePlanner.FindEndpoint(savedA.Value, scratch);
                AsphaltRoad roadB = RoadRoutePlanner.FindEndpoint(savedB.Value, scratch);

                if (roadA == null || roadB == null)
                {
                    float da = roadA == null ? Vector3.Distance(savedA.Value, RoadNavigationAnchor.SurfaceCentre(ends[0])) : 0f;
                    float db = roadB == null ? Vector3.Distance(savedB.Value, RoadNavigationAnchor.SurfaceCentre(ends[1])) : 0f;
                    reason = $"Saved network anchors unavailable or removed. Measured offsets: A {(roadA == null ? da.ToString("0.0") : "ok")} m, B {(roadB == null ? db.ToString("0.0") : "ok")} m. Prepare a new network run.";
                    return false;
                }

                if (!indices.ContainsKey(roadA) || !indices.ContainsKey(roadB))
                {
                    reason = "Saved network anchors are on another loaded network or disconnected. Vehicle is on another network or road changed. Prepare a new network run.";
                    return false;
                }

                if (RoadRoutePlanner.IsBlocked(roadA) || RoadRoutePlanner.IsBlocked(roadB))
                {
                    reason = "Saved network endpoint is blocked (drawbridge closed or road removed). Prepare a new network run.";
                    return false;
                }

                int rootA = Root(parent, indices[roadA]);
                int rootB = Root(parent, indices[roadB]);

                if (rootA == rootB)
                {
                    reason = "Saved anchors are in the same end row; network topology changed. Prepare a new network run.";
                    return false;
                }

                if (!rowNeighbours.TryGetValue(rootA, out var nA) || nA.Count != 1 ||
                    !rowNeighbours.TryGetValue(rootB, out var nB) || nB.Count != 1)
                {
                    reason = "Saved anchors are no longer at opposite ends of this network. Ends changed or road extended. Prepare a new network run.";
                    return false;
                }

                // Ensure they are opposite ends (different roots, both ends, and they belong to the two detected ends)
                // The two detected end row roots are endRowRoots[0] and endRowRoots[1]
                bool matchesOpposite = (rootA == endRowRoots[0] && rootB == endRowRoots[1]) ||
                                       (rootA == endRowRoots[1] && rootB == endRowRoots[0]);
                if (!matchesOpposite)
                {
                    // Allow if they are still end rows but not exactly the cap we picked (e.g., multi-cell row)
                    // Check that both are end rows and they are not connected to each other via same side?
                    // For safety, require opposite ends, but if both are ends and different, accept.
                    bool bothEnds = nA.Count == 1 && nB.Count == 1 && rootA != rootB;
                    if (!bothEnds)
                    {
                        reason = "Network ends changed or vehicle is on another network. Prepare a new network run.";
                        return false;
                    }
                }

                // Plan using the saved footprint roads as exact targets
                var otherApproach = new List<AsphaltRoad>();
                if (!RoadRoutePlanner.TryPlan(seed, roadA, approach, out reason)
                    || !RoadRoutePlanner.TryPlan(seed, roadB, otherApproach, out reason)) return false;

                bool first = Length(approach) <= Length(otherApproach);
                AsphaltRoad nearer = first ? roadA : roadB;
                AsphaltRoad farther = first ? roadB : roadA;
                if (!first) { approach.Clear(); approach.AddRange(otherApproach); }

                if (!RoadRoutePlanner.TryPlan(nearer, farther, across, out reason)) return false;

                reason = $"Saved network resolved: approach {Length(approach):0} m to nearer end, then {Length(across):0} m end-to-end. Low-speed reverse may be used.";
                return across.Count >= 2;
            }
            else
            {
                // Original behaviour: plan to the two detected ends
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
