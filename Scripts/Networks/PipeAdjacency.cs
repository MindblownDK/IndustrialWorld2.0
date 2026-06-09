// Assets/Scripts/VoxelEngine/Networks/PipeAdjacency.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   PIPE ADJACENCY — shared cardinal-neighbour predicate           ║
// ║                                                                  ║
// ║  Used by GasNetwork, ItemPipeNetwork and FluidNetworkManager     ║
// ║  to decide whether two placed pipes should snap into a single    ║
// ║  network. Without this check, two pipes diagonally 2 m apart     ║
// ║  on different floors would still be "within radius", so the      ║
// ║  visual builder grew arms toward empty air. Now pipes only       ║
// ║  connect when they sit ONE grid step apart on a single axis      ║
// ║  (±X, ±Y or ±Z) — the exact same rule the wire renderer uses.   ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// Shared connectivity predicates used by every pipe network to ensure
    /// pipes only auto-link to true cardinal neighbours — never to diagonal
    /// or vertically-offset blocks that just happen to be inside the search
    /// radius. Mirrors the rule already used by PowerCable / DataCable.
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
        /// Relaxed cardinal check used when one endpoint is a multi-voxel machine
        /// (tank, electrolyser, etc.) whose centre may be 1.5–2.5 m from the pipe.
        /// We accept any neighbour that lies on a single cardinal AXIS (the other
        /// two components are near-zero) AND within <paramref name="maxStepsAway"/>
        /// grid steps. Pipes still self-link strictly via <see cref="IsCardinalNeighbour"/>.
        /// </summary>
        public static bool IsAxisAlignedWithin(Vector3 a, Vector3 b,
                                                float gridSize = DefaultGridSize,
                                                float maxStepsAway = 2.5f,
                                                float tolerance = DefaultTolerance)
        {
            Vector3 d = b - a;
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
    }
}
