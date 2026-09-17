// Assets/Scripts/VoxelEngine/Building/RailNetwork.cs
//
// The rail graph: spatial registry plus the pathfinder that runs on it.
//
// Two jobs that belong together because they share the same index:
//
//   1. REGISTRY. A hash grid of track cells, mirroring RoadSurfaceUtility, so laying a
//      cell can find its neighbours without a scene sweep.
//   2. PATHFINDING. A* over the cell graph, which is the roadmap's "A* for trains on
//      rail graph" item.
//
// A* rather than the road system's corridor solver because a railway is already a
// sparse graph with explicit edges — exactly the shape A* wants — whereas roads are a
// dense surface that has to be traced first. Reusing the road planner here would mean
// rediscovering topology the rail graph already knows.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    public static class RailNetwork
    {
        // ════════════════════════════════════════════════════════════════
        //  REGISTRY
        // ════════════════════════════════════════════════════════════════

        /// <summary>Hash-grid cell size in metres. One bucket holds a small neighbourhood.</summary>
        private const float CELL_SIZE = 2f;
        private const float INV_CELL = 1f / CELL_SIZE;

        /// <summary>
        /// How far apart two cell origins may be and still count as adjacent. A little over
        /// one cell so a draped or slightly offset placement still joins, well under two so
        /// a gap in the line stays a gap.
        /// </summary>
        private const float ADJACENCY_RANGE = 1.45f;

        private static readonly Dictionary<long, List<RailTrack>> _cells = new(256);
        private static readonly HashSet<RailTrack> _registered = new();

        public static int TrackCount => _registered.Count;
        public static IReadOnlyCollection<RailTrack> AllTracks => _registered;

        public static void Register(RailTrack track)
        {
            if (track == null) return;
            _registered.Add(track);
            long key = KeyFor(track.transform.position);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<RailTrack>(4);
                _cells[key] = list;
            }
            if (!list.Contains(track)) list.Add(track);
        }

        public static void Unregister(RailTrack track)
        {
            if (track == null) return;
            _registered.Remove(track);
            long key = KeyFor(track.transform.position);
            if (_cells.TryGetValue(key, out var list))
            {
                list.Remove(track);
                if (list.Count == 0) _cells.Remove(key);
            }
        }

        public static void Clear()
        {
            _cells.Clear();
            _registered.Clear();
        }

        private static long KeyFor(Vector3 position)
        {
            long x = Mathf.FloorToInt(position.x * INV_CELL);
            long y = Mathf.FloorToInt(position.y * INV_CELL);
            long z = Mathf.FloorToInt(position.z * INV_CELL);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        /// <summary>
        /// Every track cell close enough to <paramref name="origin"/> to be a neighbour.
        /// Scans the 3x3x3 bucket neighbourhood, which fully covers the adjacency range.
        /// </summary>
        public static void QueryAdjacent(RailTrack origin, List<RailTrack> results)
        {
            results.Clear();
            if (origin == null) return;

            Vector3 centre = origin.transform.position;
            int cx = Mathf.FloorToInt(centre.x * INV_CELL);
            int cy = Mathf.FloorToInt(centre.y * INV_CELL);
            int cz = Mathf.FloorToInt(centre.z * INV_CELL);

            float rangeSq = ADJACENCY_RANGE * ADJACENCY_RANGE;

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                long key = ((long)(cx + dx) * 73856093L)
                         ^ ((long)(cy + dy) * 19349663L)
                         ^ ((long)(cz + dz) * 83492791L);
                if (!_cells.TryGetValue(key, out var list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var candidate = list[i];
                    if (candidate == null || candidate == origin) continue;
                    if ((candidate.transform.position - centre).sqrMagnitude > rangeSq) continue;
                    if (!results.Contains(candidate)) results.Add(candidate);
                }
            }

            // Nearest first, so a cell fills its limited link budget with the cells that are
            // genuinely its neighbours rather than whichever bucket happened to be scanned first.
            results.Sort((a, b) =>
                (a.transform.position - centre).sqrMagnitude
                .CompareTo((b.transform.position - centre).sqrMagnitude));
        }

        /// <summary>The track cell nearest a world position, within <paramref name="maxDistance"/>.</summary>
        public static RailTrack FindNearest(Vector3 position, float maxDistance = 6f)
        {
            RailTrack best = null;
            float bestSq = maxDistance * maxDistance;

            foreach (var track in _registered)
            {
                if (track == null) continue;
                float d = (track.transform.position - position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = track; }
            }
            return best;
        }

        // ════════════════════════════════════════════════════════════════
        //  PATHFINDING  (A* over the cell graph)
        // ════════════════════════════════════════════════════════════════

        private static readonly Dictionary<RailTrack, RailTrack> _cameFrom = new(256);
        private static readonly Dictionary<RailTrack, float> _gScore = new(256);
        private static readonly List<RailTrack> _open = new(128);
        private static readonly HashSet<RailTrack> _closed = new();

        /// <summary>Safety valve: a search that has expanded this many cells has gone wrong.</summary>
        private const int MAX_EXPANSIONS = 20000;

        /// <summary>
        /// Finds a route between two cells, writing it into <paramref name="path"/> from
        /// start to goal inclusive. Returns false when no connected route exists.
        ///
        /// Costs are real distance, and the heuristic is straight-line distance, which is
        /// admissible because no edge is ever shorter than the gap it spans. That keeps the
        /// result a genuine shortest path rather than merely a path.
        /// </summary>
        public static bool TryFindPath(RailTrack start, RailTrack goal, List<RailTrack> path)
        {
            path.Clear();
            if (start == null || goal == null) return false;
            if (start == goal) { path.Add(start); return true; }

            _cameFrom.Clear();
            _gScore.Clear();
            _open.Clear();
            _closed.Clear();

            _gScore[start] = 0f;
            _open.Add(start);

            int expansions = 0;

            while (_open.Count > 0)
            {
                if (++expansions > MAX_EXPANSIONS)
                {
                    Debug.LogWarning("[RailNetwork] Path search exceeded its expansion budget; " +
                                     "treating the route as unreachable.");
                    return false;
                }

                // Linear scan for the best open node. A railway is a sparse graph with a
                // handful of frontier cells, so a binary heap would cost more in complexity
                // than it saves in time at this scale.
                int bestIndex = 0;
                float bestF = float.MaxValue;
                for (int i = 0; i < _open.Count; i++)
                {
                    var node = _open[i];
                    float f = _gScore[node] + Heuristic(node, goal);
                    if (f < bestF) { bestF = f; bestIndex = i; }
                }

                var current = _open[bestIndex];
                if (current == goal) return Reconstruct(current, path);

                _open.RemoveAt(bestIndex);
                _closed.Add(current);

                var links = current.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    var next = links[i];
                    if (next == null || _closed.Contains(next)) continue;

                    float step = Vector3.Distance(current.transform.position, next.transform.position);

                    // Slow track costs more to cross, so the planner prefers good line even
                    // when it is slightly longer — which is what a real router would do.
                    float multiplier = Mathf.Max(0.1f, next.speedMultiplier);
                    float tentative = _gScore[current] + step / multiplier;

                    if (_gScore.TryGetValue(next, out float existing) && tentative >= existing) continue;

                    _cameFrom[next] = current;
                    _gScore[next] = tentative;
                    if (!_open.Contains(next)) _open.Add(next);
                }
            }

            return false;
        }

        private static float Heuristic(RailTrack from, RailTrack to)
            => Vector3.Distance(from.transform.position, to.transform.position);

        private static bool Reconstruct(RailTrack goal, List<RailTrack> path)
        {
            path.Clear();
            var node = goal;
            while (node != null)
            {
                path.Add(node);
                if (!_cameFrom.TryGetValue(node, out node)) break;
            }
            path.Reverse();
            return path.Count > 0;
        }

        /// <summary>Total length of a path in metres, for schedule and fuel estimates.</summary>
        public static float PathLength(IReadOnlyList<RailTrack> path)
        {
            if (path == null || path.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i - 1] == null || path[i] == null) continue;
                total += Vector3.Distance(path[i - 1].transform.position, path[i].transform.position);
            }
            return total;
        }
    }
}
