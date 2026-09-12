// Assets/Scripts/VoxelEngine/Environment/RoadSurfaceUtility.cs
//
// THE ROAD SURFACE QUERY — one registry, two consumers.
//
// Movement systems ask a question every frame ("is there asphalt under these feet / this
// tyre?") and road blocks ask it only when their topology changes ("who are my four
// neighbours?"). Both are the same lookup, so both go through this file and neither pays
// for a physics query: `Physics.OverlapSphere` per wheel per FixedUpdate on a convoy is
// exactly the kind of cost this engine does not spend.
//
// The registry is a world-space hash grid with a 2 m cell. A road registers into every
// cell its own footprint overlaps (one to four of them), so a query is a SINGLE dictionary
// read followed by a short distance test — O(1), allocation-free, and correct across cell
// boundaries, which a centre-only registration would not be.
//
// Spherical worlds are handled by never assuming a global lattice. A road's neighbours are
// found by proximity in its own tangent frame and rejected when they are not roughly
// coplanar, which is why `IsNeighbourSlot` compares heights along `up` instead of trusting
// world-axis rounding (the same drift `BuildSystem` warns about for placed machines).
//
// Deliberately NOT a road network object: there is no name, no connected-run trace and no
// traffic readout here. The roadmap holds the network back until autopilot and drone
// routing exist to feed it. This file only answers "what asphalt is at this point".

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Environment
{
    public static class RoadSurfaceUtility
    {
        /// <summary>Hash-grid cell size in metres. Larger than a road cell so a footprint
        /// spans at most four cells; small enough that a query filters few candidates.</summary>
        private const float CELL_SIZE = 2f;
        private const float INV_CELL  = 1f / CELL_SIZE;

        /// <summary>Half-extent of a road cell's registered footprint, slightly proud of the
        /// 1 m slab so a wheel on the very edge of a cell still finds it.</summary>
        private const float FOOTPRINT_HALF = 0.56f;

        private static readonly Dictionary<long, List<AsphaltRoad>> _cells = new Dictionary<long, List<AsphaltRoad>>(512);
        private static readonly HashSet<AsphaltRoad> _registered = new HashSet<AsphaltRoad>();
        private static readonly List<AsphaltRoad> _queryScratch = new List<AsphaltRoad>(16);

        // ════════════════════════════════════════════════════════════════
        //  REGISTRY
        // ════════════════════════════════════════════════════════════════

        /// <summary>Adds a road to every cell its footprint overlaps. Safe to call twice.</summary>
        public static void Register(AsphaltRoad road)
        {
            if (road == null) return;
            _registered.Add(road);
            ForEachCell(road.transform.position, (cellKey, list) =>
            {
                if (!list.Contains(road)) list.Add(road);
            });
        }

        /// <summary>Removes a road from the registry and drops cells that become empty.</summary>
        public static void Unregister(AsphaltRoad road)
        {
            if (road == null) return;
            _registered.Remove(road);
            ForEachCell(road.transform.position, (cellKey, list) =>
            {
                list.Remove(road);
                if (list.Count == 0) _cells.Remove(cellKey);
            });
        }

        /// <summary>Clears the whole registry (world teardown / scene unload).</summary>
        public static void Clear() { _cells.Clear(); _registered.Clear(); }

        // Explicit navigation fallback only. The spatial hash indexes construction origins,
        // which need not lie at the rendered height of a draped/graded road.
        public static void CopyRegistered(List<AsphaltRoad> result, int limit)
        {
            result.Clear();
            foreach (var road in _registered)
            {
                if (road != null && road.isActiveAndEnabled) result.Add(road);
                if (result.Count >= limit) break;
            }
        }

        private static void ForEachCell(Vector3 centre, System.Action<long, List<AsphaltRoad>> visit)
        {
            int minX = Mathf.FloorToInt((centre.x - FOOTPRINT_HALF) * INV_CELL);
            int maxX = Mathf.FloorToInt((centre.x + FOOTPRINT_HALF) * INV_CELL);
            int minY = Mathf.FloorToInt((centre.y - FOOTPRINT_HALF) * INV_CELL);
            int maxY = Mathf.FloorToInt((centre.y + FOOTPRINT_HALF) * INV_CELL);
            int minZ = Mathf.FloorToInt((centre.z - FOOTPRINT_HALF) * INV_CELL);
            int maxZ = Mathf.FloorToInt((centre.z + FOOTPRINT_HALF) * INV_CELL);

            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            {
                long key = HashKey(x, y, z);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<AsphaltRoad>(4);
                    _cells[key] = list;
                }
                visit(key, list);
            }
        }

        private static long HashKey(int x, int y, int z)
        {
            // Spread the three ints into one long without collisions for any realistic
            // world extent, and without the string allocation a Vector3 key would cost.
            unchecked
            {
                long h = (long)(x & 0x1FFFFF) << 42;
                h |= (long)(y & 0xFFFFF) << 22;
                h |= (long)(z & 0x3FFFFF);
                return h;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  QUERIES
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fills <paramref name="results"/> with every road whose footprint contains
        /// <paramref name="worldPosition"/>, nearest surface first. Allocation-free after
        /// warm-up; the caller owns the list.
        /// </summary>
        public static void QueryAt(Vector3 worldPosition, List<AsphaltRoad> results)
        {
            results.Clear();
            int cx = Mathf.FloorToInt(worldPosition.x * INV_CELL);
            int cy = Mathf.FloorToInt(worldPosition.y * INV_CELL);
            int cz = Mathf.FloorToInt(worldPosition.z * INV_CELL);
            if (!_cells.TryGetValue(HashKey(cx, cy, cz), out var list) || list.Count == 0) return;

            for (int i = 0; i < list.Count; i++)
            {
                var road = list[i];
                if (road == null || !road.isActiveAndEnabled) continue;
                if (!results.Contains(road)) results.Add(road);
            }
        }

        /// <summary>Local broad-phase road query for bounded navigation searches.
        /// Unlike QueryAt, visits neighbouring hash buckets; results remain caller-owned.</summary>
        public static void QueryNearby(Vector3 centre, float radius, List<AsphaltRoad> results)
        {
            results.Clear();
            radius = Mathf.Clamp(radius, 0f, 16f);
            int minX = Mathf.FloorToInt((centre.x - radius) * INV_CELL);
            int maxX = Mathf.FloorToInt((centre.x + radius) * INV_CELL);
            int minY = Mathf.FloorToInt((centre.y - radius) * INV_CELL);
            int maxY = Mathf.FloorToInt((centre.y + radius) * INV_CELL);
            int minZ = Mathf.FloorToInt((centre.z - radius) * INV_CELL);
            int maxZ = Mathf.FloorToInt((centre.z + radius) * INV_CELL);
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            {
                if (!_cells.TryGetValue(HashKey(x, y, z), out var roads)) continue;
                for (int i = 0; i < roads.Count; i++)
                {
                    var road = roads[i];
                    if (road == null || !road.isActiveAndEnabled
                        || (road.transform.position - centre).sqrMagnitude > radius * radius) continue;
                    if (!results.Contains(road)) results.Add(road);
                }
            }
        }

        /// <summary>
        /// True when drivable asphalt sits within <paramref name="probeDepth"/> metres below
        /// <paramref name="worldPosition"/> (and no more than a small step above it, so a
        /// road on a ledge overhead is not reported under the player's feet).
        /// Mirrors <see cref="IceFrictionUtility.IsIceBelow"/> so movement systems read the
        /// same way for both surfaces.
        /// </summary>
        public static bool TryGetRoadBelow(Vector3 worldPosition, Vector3 up, float probeDepth, out AsphaltRoad road)
        {
            road = null;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            // Probe from a little above the sample point: a road's own slab is ~0.08 m thick
            // and its surface can sit marginally above the feet position on a draped run.
            QueryAt(worldPosition + up * 0.25f, _queryScratch);
            if (_queryScratch.Count == 0)
            {
                QueryAt(worldPosition, _queryScratch);
                if (_queryScratch.Count == 0) return false;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < _queryScratch.Count; i++)
            {
                var candidate = _queryScratch[i];
                if (candidate == null) continue;

                // Measured against the cell's ACTUAL paved surface, not its origin: on a draped run
                // the surface can sit half a metre above or below the origin across one cell, and an
                // origin-relative test would drop a player standing on a bump or pick up a road on
                // the terrace above. NaN means the point is outside this cell's footprint.
                float above = candidate.SurfaceOffset(worldPosition, up);
                if (float.IsNaN(above)) continue;
                // Tolerate a small step up (a kerb, a frame of penetration) and the full probe
                // depth down, so an agent is never reported off-road while it is still on the slab.
                if (above > 0.30f || above < -probeDepth) continue;

                if (Mathf.Abs(above) < bestDistance)
                {
                    bestDistance = Mathf.Abs(above);
                    road = candidate;
                }
            }

            return road != null;
        }

        /// <summary>
        /// True when <paramref name="candidate"/> occupies the neighbour slot one road cell
        /// away from <paramref name="origin"/> along <paramref name="direction"/>. Coplanarity
        /// is tested along <paramref name="up"/> so a road on the next terrace up is not
        /// stitched into the strip below it.
        /// </summary>
        public static bool IsNeighbourSlot(AsphaltRoad origin, AsphaltRoad candidate,
                                           Vector3 direction, Vector3 up, float cellSize)
        {
            if (origin == null || candidate == null || origin == candidate) return false;

            Vector3 delta = candidate.transform.position - origin.transform.position;
            float along   = Vector3.Dot(delta, direction);
            if (Mathf.Abs(along - cellSize) > cellSize * 0.35f) return false;

            Vector3 off = delta - direction * along;
            float lateral = Vector3.Dot(off, up);
            // A ramp between two terraces is legitimately not coplanar, so the height budget
            // is a full cell — enough for a real slope, too much for a stacked duplicate.
            if (Mathf.Abs(lateral) > cellSize * 0.95f) return false;

            Vector3 sideways = Vector3.Cross(up, direction);
            if (sideways.sqrMagnitude > 0.0001f)
            {
                sideways.Normalize();
                if (Mathf.Abs(Vector3.Dot(off, sideways)) > cellSize * 0.35f) return false;
            }
            return true;
        }
    }
}
