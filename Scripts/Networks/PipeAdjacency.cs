// Assets/Scripts/VoxelEngine/Networks/PipeAdjacency.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   PIPE ADJACENCY — shared cardinal-neighbour predicate           ║
// ║                                                                  ║
// ║  Used by GasNetwork, ItemPipeNetwork and FluidNetworkManager     ║
// ║  to decide whether two placed pipes should snap into a single    ║
// ║  network. Without this check, two pipes diagonally 2 m apart     ║
// ║  on different floors would still be \"within radius\", so the      ║
// ║  visual builder grew arms toward empty air. Pipe links remain    ║
// ║  on one cardinal axis and may use either immediate-neighbour or  ║
// ║  explicitly bounded multi-cell connection rules.                 ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Networks
{
    public static class PipeAdjacency
    {
        public const float DefaultGridSize = 1f;
        public const float DefaultTolerance = 0.35f;
        public const float VerticalTolerance = 0.65f; // extra slack for vertical shafts on uneven land

        public static bool IsCardinalNeighbour(Vector3 a, Vector3 b,
                                                float gridSize = DefaultGridSize,
                                                float tolerance = DefaultTolerance)
        {
            Vector3 d = b - a;
            float gs  = gridSize  > 0 ? gridSize  : DefaultGridSize;
            float tol = tolerance > 0 ? tolerance : DefaultTolerance;
            float dx = Mathf.Abs(d.x), dy = Mathf.Abs(d.y), dz = Mathf.Abs(d.z);

            float maxAxis = Mathf.Max(dx, Mathf.Max(dy, dz));
            if (maxAxis < gs * 0.5f) return false;
            if (maxAxis > gs * 1.6f) return false;

            int dominant =
                (dx >= dy && dx >= dz) ? 0 :
                (dy >= dx && dy >= dz) ? 1 : 2;

            float effectiveTol = (dominant == 1) ? Mathf.Max(tol, VerticalTolerance) : tol;

            float other1 = dominant == 0 ? dy : dx;
            float other2 = dominant == 2 ? dy : dz;
            // Use Euclidean distance of the non-dominant plane to prevent diagonal:
            // e.g. vertical pipe with dx=0.5, dz=0.5 has horiz dist 0.707 > 0.65 → blocked,
            // but dx=0.2, dz=0.1 has dist 0.22 → allowed (small drift from terrain normal).
            float otherDist = Mathf.Sqrt(other1 * other1 + other2 * other2);
            return otherDist <= effectiveTol;
        }

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
            float effectiveTol = (dominant == 1) ? Mathf.Max(tol, VerticalTolerance) : tol;
            float other1 = dominant == 0 ? dy : dx;
            float other2 = dominant == 2 ? dy : dz;
            float otherDist = Mathf.Sqrt(other1 * other1 + other2 * other2);
            return otherDist <= effectiveTol;
        }

        /// <summary>
        /// Strict physical pipe-to-pipe rule: one lattice step on exactly one axis.
        /// Unlike broad endpoint/corridor probes this deliberately does not apply the
        /// vertical terrain slack, because that slack let vertical detail pipes connect
        /// diagonally through a neighbouring plane.
        /// </summary>
        public static bool IsDirectPipeLinkDelta(Vector3 delta,
                                                  float gridSize = DefaultGridSize,
                                                  float tolerance = 0f)
        {
            float gs = gridSize > 0f ? gridSize : DefaultGridSize;
            float tol = tolerance > 0f ? tolerance : Mathf.Max(0.06f, gs * 0.18f);
            float dx = Mathf.Abs(delta.x), dy = Mathf.Abs(delta.y), dz = Mathf.Abs(delta.z);
            float along = Mathf.Max(dx, Mathf.Max(dy, dz));
            // Pipes live at cell centres: accept a small placement tolerance around
            // one direct neighbour, never the old multi-cell corridor range.
            if (along < gs * 0.70f || along > gs * 1.30f) return false;

            int dominant = (dx >= dy && dx >= dz) ? 0 : (dy >= dx && dy >= dz) ? 1 : 2;
            float other1 = dominant == 0 ? dy : dx;
            float other2 = dominant == 2 ? dy : dz;
            return Mathf.Sqrt(other1 * other1 + other2 * other2) <= tol;
        }

        public static bool IsDirectPipeLink(Vector3 a, Vector3 b,
                                             float gridSize = DefaultGridSize,
                                             float tolerance = 0f)
            => IsDirectPipeLinkDelta(b - a, gridSize, tolerance);

        /// <summary>
        /// Strict same-plane pipe rule with a bounded multi-cell reach. Pipes can span
        /// up to five small lattice cells along ONE axis, but never use the broad
        /// vertical terrain slack that caused diagonal/off-plane pipe joins.
        /// </summary>
        public static bool IsCoplanarPipeLinkDelta(Vector3 delta,
                                                    float gridSize = DefaultGridSize,
                                                    float maxCells = 5f,
                                                    float tolerance = 0f)
        {
            float gs = gridSize > 0f ? gridSize : DefaultGridSize;
            float tol = tolerance > 0f ? tolerance : Mathf.Max(0.06f, gs * 0.18f);
            float dx = Mathf.Abs(delta.x), dy = Mathf.Abs(delta.y), dz = Mathf.Abs(delta.z);
            float along = Mathf.Max(dx, Mathf.Max(dy, dz));
            float steps = along / gs;
            float nearestStep = Mathf.Round(steps);
            if (nearestStep < 1f || nearestStep > Mathf.Max(1f, maxCells)) return false;
            // A same-plane run must land on an actual lattice step, not merely be
            // somewhere within the old broad corridor radius.
            if (Mathf.Abs(steps - nearestStep) > Mathf.Max(0.12f, tol / gs)) return false;

            int dominant = (dx >= dy && dx >= dz) ? 0 : (dy >= dx && dy >= dz) ? 1 : 2;
            float other1 = dominant == 0 ? dy : dx;
            float other2 = dominant == 2 ? dy : dz;
            return Mathf.Sqrt(other1 * other1 + other2 * other2) <= tol;
        }

        public static bool IsCoplanarPipeLink(Vector3 a, Vector3 b,
                                               float gridSize = DefaultGridSize,
                                               float maxCells = 5f,
                                               float tolerance = 0f)
            => IsCoplanarPipeLinkDelta(b - a, gridSize, maxCells, tolerance);

        public static Vector3 ConnectionDelta(Component a, Component b)
        {
            if (a == null || b == null) return Vector3.zero;
            Vector3 worldDelta = b.transform.position - a.transform.position;
            var blockA = a.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            var blockB = b.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            if (blockA != null && blockB != null && blockA.Grid != null && blockA.Grid == blockB.Grid)
                return blockA.Grid.transform.InverseTransformVector(worldDelta);

            // Stationary pipes can be surface-aligned on spherical terrain or manually
            // rotated. Evaluate their link in the source pipe's own XYZ frame instead
            // of forcing world axes, so static pipe runs work in all three local planes.
            if (IsPipeComponent(a) && IsPipeComponent(b))
                return a.transform.InverseTransformVector(worldDelta);
            return worldDelta;
        }

        private static bool IsPipeComponent(Component component)
        {
            return component is VoxelEngine.Transport.ItemPipe
                || component is VoxelEngine.Gas.GasPipe
                || component is VoxelEngine.Fluids.WaterPipe;
        }

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

            // Determine dominant axis
            float max = Mathf.Max(dx, Mathf.Max(dy, dz));
            int dominant = (dx >= dy && dx >= dz) ? 0 : (dy >= dx && dy >= dz) ? 1 : 2;
            float effectiveTol = (dominant == 1) ? Mathf.Max(tol, VerticalTolerance) : tol;

            // Other axes must be within effectiveTol in Euclidean sense
            float other1 = dominant == 0 ? dy : dx;
            float other2 = dominant == 2 ? dy : dz;
            float otherDist = Mathf.Sqrt(other1 * other1 + other2 * other2);
            if (otherDist > effectiveTol) return false;

            float along = max;
            return along <= gs * maxStepsAway + effectiveTol;
        }

        private static readonly Vector3[] s_probeAxesWorld =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };
        private static readonly Vector3[] s_probeAxesGrid = new Vector3[6];

        public static void ProbeCardinal(Vector3 origin, Transform gridFrame, float step,
            int maxCells, Collider[] buffer, System.Func<Collider, bool> visit, float radiusScale = 0.45f)
        {
            if (visit == null || buffer == null || buffer.Length == 0) return;
            step = step > 0.0001f ? step : DefaultGridSize;
            maxCells = Mathf.Max(1, maxCells);
            float radius = step * Mathf.Max(0.05f, radiusScale);

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
