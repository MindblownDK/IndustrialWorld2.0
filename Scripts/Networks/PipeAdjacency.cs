// Assets/Scripts/VoxelEngine/Networks/PipeAdjacency.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   PIPE ADJACENCY — shared cardinal-neighbour predicate           ║
// ║                                                                  ║
// ║  Used by GasNetwork, ItemPipeNetwork and FluidNetworkManager     ║
// ║  to decide whether two placed pipes should snap into a single    ║
// ║  network. Without this check, two pipes diagonally 2 m apart     ║
// ║  on different floors would still be "within radius", so the      ║
// ║  visual builder grew arms toward empty air. Pipe links remain    ║
// ║  on one cardinal axis and may use either immediate-neighbour or  ║
// ║  explicitly bounded multi-cell connection rules.                 ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// Shared connectivity predicates used by every pipe network to ensure
    /// pipes link only along a cardinal axis — never diagonally or merely because
    /// another pipe happens to be inside a broad search radius.
    /// </summary>
    public static class PipeAdjacency
    {
        /// <summary>Default 1m build-grid cell size.</summary>
        public const float DefaultGridSize = 1f;

        /// <summary>
        /// Tolerance when comparing positions. Generous (0.35) so two pipes
        /// placed via the BuildSystem on slightly different surface heights
        /// (eg the same row of dirt but one sits 0.1m higher because of a
        /// scattered stone underneath) still register as cardinal neighbours.
        /// The fail-safe is the "one-axis-step + others zero" requirement —
        /// even with the wider tolerance, diagonal pipes never connect.
        /// </summary>
        public const float DefaultTolerance = 0.35f;

        /// <summary>
        /// True when <paramref name="a"/> and <paramref name="b"/> are exactly ONE
        /// grid step apart on EXACTLY one cardinal axis (±X / ±Y / ±Z) and not
        /// offset on the other two. This is the same predicate used by the wire
        /// renderer, so cables, pipes and all conduits share one visual language.
        /// </summary>
        public static bool IsCardinalNeighbour(Vector3 a, Vector3 b,
                                                float gridSize = DefaultGridSize,
                                                float tolerance = DefaultTolerance)
        {
            Vector3 d = b - a;
            float gs  = gridSize  > 0 ? gridSize  : DefaultGridSize;
            float tol = tolerance > 0 ? tolerance : DefaultTolerance;
            float dx = Mathf.Abs(d.x), dy = Mathf.Abs(d.y), dz = Mathf.Abs(d.z);

            // Pick the dominant axis. If the dominant axis is within (gs/2 .. 2*gs)
            // AND the other two axes are within `tol` of zero, this is a cardinal
            // neighbour. This is much more forgiving than the strict "exactly 1
            // step ± tol" test and handles pipes placed on slightly uneven
            // terrain or via the build ghost on non-integer grid surfaces.
            float maxAxis = Mathf.Max(dx, Mathf.Max(dy, dz));
            if (maxAxis < gs * 0.5f) return false; // too close (same cell)
            if (maxAxis > gs * 1.6f) return false; // too far  (gap between cells)

            int dominant =
                (dx >= dy && dx >= dz) ? 0 :
                (dy >= dx && dy >= dz) ? 1 : 2;

            // Ensure the OTHER two axes are within tolerance of zero (so pipes
            // on the same row connect even if their Y values differ slightly).
            float other1 = dominant == 0 ? dy : dx;
            float other2 = dominant == 2 ? dy : dz;
            return other1 <= tol && other2 <= tol;
        }

        /// <summary>
        /// True when two pipes share one cardinal axis and are no farther apart than
        /// <paramref name="maxCells"/> cells. Used by long pipe links while retaining
        /// the same strict no-diagonal rule as immediate neighbours.
        /// </summary>
        public static bool IsCardinalLink(Vector3 a, Vector3 b,
                                           float gridSize = DefaultGridSize,
                                           float maxCells = 5f,
                                           float tolerance = DefaultTolerance)
            => IsCardinalLinkDelta(b - a, gridSize, maxCells, tolerance);

        public static bool IsCardinalLinkDelta(Vector3 delta,
                                                float gridSize = DefaultGridSize,
                                                float maxCells = 5f,
                                                float tolerance = DefaultTolerance)
        {
            Vector3 d = delta;
            float gs = gridSize > 0f ? gridSize : DefaultGridSize;
            float tol = tolerance > 0f ? tolerance : DefaultTolerance;
            float dx = Mathf.Abs(d.x), dy = Mathf.Abs(d.y), dz = Mathf.Abs(d.z);
            float along = Mathf.Max(dx, Mathf.Max(dy, dz));
            if (along < gs * 0.5f || along > gs * Mathf.Max(1f, maxCells) + tol) return false;

            int dominant = (dx >= dy && dx >= dz) ? 0 : (dy >= dx && dy >= dz) ? 1 : 2;
            float other1 = dominant == 0 ? dy : dx;
            float other2 = dominant == 2 ? dy : dz;
            return other1 <= tol && other2 <= tol;
        }

        /// <summary>
        /// Returns B relative to A in the shared Grid's local frame. Pipe object
        /// rotation is intentionally ignored. World pipes use world-space delta.
        /// </summary>
        public static Vector3 ConnectionDelta(Component a, Component b)
        {
            if (a == null || b == null) return Vector3.zero;
            Vector3 worldDelta = b.transform.position - a.transform.position;
            var blockA = a.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            var blockB = b.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
                return blockA.Grid.transform.InverseTransformVector(worldDelta);
            return worldDelta;
        }

        /// <summary>
        /// Relaxed cardinal check used when one endpoint is a multi-voxel machine
        /// (tank, electrolyser, etc.) whose centre may be 1.5–2.5 m from the pipe.
        /// </summary>
        public static bool IsAxisAlignedWithin(Vector3 a, Vector3 b,
                                                float gridSize = DefaultGridSize,
                                                float maxStepsAway = 2.5f,
                                                float tolerance = DefaultTolerance)
            => IsAxisAlignedWithinDelta(b - a, gridSize, maxStepsAway, tolerance);

        public static bool IsAxisAlignedWithinDelta(Vector3 delta,
                                                     float gridSize = DefaultGridSize,
                                                     float maxStepsAway = 2.5f,
                                                     float tolerance = DefaultTolerance)
        {
            Vector3 d = delta;
            float gs  = gridSize  > 0 ? gridSize  : DefaultGridSize;
            float tol = tolerance > 0 ? tolerance : DefaultTolerance;
            float dx = Mathf.Abs(d.x), dy = Mathf.Abs(d.y), dz = Mathf.Abs(d.z);

            int activeAxes = 0;
            if (dx > tol) activeAxes++;
            if (dy > tol) activeAxes++;
            if (dz > tol) activeAxes++;
            if (activeAxes != 1) return false;

            float along = Mathf.Max(dx, dy, dz);
            return along <= gs * maxStepsAway + tol;
        }

        // ── Cardinal endpoint probes (tanks / containers at lattice range) ──
        private static readonly Vector3[] s_probeAxesWorld =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };
        private static readonly Vector3[] s_probeAxesGrid = new Vector3[6];

        /// <summary>
        /// The "five lattice cells in a valid direction" rule: step along each of the
        /// six cardinal axes (in <paramref name="gridFrame"/>'s frame when supplied,
        /// world axes otherwise) for up to <paramref name="maxCells"/> lattice cells,
        /// OverlapSphere-probing every cell centre. <paramref name="visit"/> runs per
        /// hit collider and returns true to stop early. Used by the gas/liquid/item
        /// endpoint lookups so tanks and containers join a pipe run without sitting
        /// physically on top of it — but never diagonally or off-axis.
        /// </summary>
        public static void ProbeCardinal(Vector3 origin, Transform gridFrame, float step,
            int maxCells, Collider[] buffer, System.Func<Collider, bool> visit)
        {
            if (visit == null || buffer == null || buffer.Length == 0) return;
            step = step > 0.0001f ? step : DefaultGridSize;
            maxCells = Mathf.Max(1, maxCells);
            float radius = step * 0.45f;

            Vector3[] axes = s_probeAxesWorld;
            if (gridFrame != null)
            {
                s_probeAxesGrid[0] = gridFrame.right;
                s_probeAxesGrid[1] = -gridFrame.right;
                s_probeAxesGrid[2] = gridFrame.up;
                s_probeAxesGrid[3] = -gridFrame.up;
                s_probeAxesGrid[4] = gridFrame.forward;
                s_probeAxesGrid[5] = -gridFrame.forward;
                axes = s_probeAxesGrid;
            }

            for (int a = 0; a < 6; a++)
            {
                Vector3 dir = axes[a];
                for (int k = 1; k <= maxCells; k++)
                {
                    Vector3 centre = origin + dir * (step * k);
                    int count = Physics.OverlapSphereNonAlloc(centre, radius, buffer,
                        ~0, QueryTriggerInteraction.Collide);
                    for (int i = 0; i < count; i++)
                    {
                        var col = buffer[i];
                        if (col == null) continue;
                        buffer[i] = null;
                        if (visit(col)) return;
                    }
                }
            }
        }
    }
}
