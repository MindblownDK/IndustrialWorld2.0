// Assets/Scripts/VoxelEngine/Building/PlacementPatternPlanner.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      PLACEMENT PATTERN PLANNER — anchor, aim, list of cells      ║
// ║                                                                  ║
// ║  Turns "where the run started" and "where the ghost is now" into  ║
// ║  the exact cells a click will build. Pure maths, no scene state, ║
// ║  so the preview and the commit walk the same list - a run can    ║
// ║  never draw ten cells and place nine.                            ║
// ║                                                                  ║
// ║  Direction selection is deliberate: the axis is whichever of     ║
// ║  the frame's three axes the aim has moved furthest along, so a   ║
// ║  run is always axis-aligned and always predictable. A diagonal   ║
// ║  drag builds the dominant axis, not a ragged staircase.          ║
// ║                                                                  ║
// ║  Caps live in PlacementOptions: ten for a run or a pillar, four  ║
// ║  per edge for a deck, sixteen cells for any single commit.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>One block of a run: exactly what the commit will build.</summary>
    public struct PlacementCell
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>Builds the cell list for the current pattern. Allocation-free once warm.</summary>
    public static class PlacementPatternPlanner
    {
        /// <summary>
        /// Fills <paramref name="cells"/> with the run from <paramref name="anchor"/> toward
        /// <paramref name="target"/>, anchor first. Returns the number of cells written
        /// (always at least one when the pattern is valid).
        /// </summary>
        public static int Build(PlacementPattern pattern, in PlacementFrame frame,
                                Vector3 anchorPosition, Quaternion anchorRotation,
                                Vector3 targetPosition, float step, Vector3 pitch, int cellBudget,
                                List<PlacementCell> cells)
        {
            if (cells == null) return 0;
            cells.Clear();

            if (step <= 0.0001f) step = 1f;

            // 17.44.1-dev: the lattice step says where a cell starts, the pitch says how
            // much room the block standing on it needs. A machine wider than one metre
            // gets pitched by its own footprint so a run never lays it into its neighbour.
            if (pitch.x <= 0.0001f) pitch.x = step;
            if (pitch.y <= 0.0001f) pitch.y = step;
            if (pitch.z <= 0.0001f) pitch.z = step;

            int budget = Mathf.Clamp(cellBudget, 1, PlacementOptions.MaxCells);

            // Slot 0 is always the anchor: a run is never empty, and a click with no drag
            // is still one block.
            cells.Add(new PlacementCell { position = anchorPosition, rotation = anchorRotation });
            if (pattern == PlacementPattern.Single || budget <= 1) return cells.Count;

            Vector3 delta = targetPosition - anchorPosition;
            Vector3 local = frame.valid
                ? frame.ToLocal(targetPosition) - frame.ToLocal(anchorPosition)
                : delta;

            if (pattern == PlacementPattern.Deck)
            {
                // 17.44.11-dev: a deck is counted per lattice step and spaced by the LARGER of
                // the step and the block, so the gesture is always "one metre of drag adds a
                // row or a column" while neighbouring cells stay apart. Counting on the
                // footprint made a four-by-four deck need ten metres of drag; spacing on the
                // footprint alone made a deck of wide machines sprawl across fifteen.
                int alongRight = Mathf.Clamp(Mathf.RoundToInt(local.x / step), -(PlacementOptions.MaxDeckSide - 1), PlacementOptions.MaxDeckSide - 1);
                int alongForward = Mathf.Clamp(Mathf.RoundToInt(local.z / step), -(PlacementOptions.MaxDeckSide - 1), PlacementOptions.MaxDeckSide - 1);
                if (alongRight == 0 && alongForward == 0) return cells.Count;

                Vector3 axisRight = frame.valid ? frame.right : Vector3.right;
                Vector3 axisForward = frame.valid ? frame.forward : Vector3.forward;
                int rows = Mathf.Abs(alongForward);
                int columns = Mathf.Abs(alongRight);
                float signForward = Mathf.Sign(alongForward);
                float signRight = Mathf.Sign(alongRight);

                for (int f = 0; f <= rows; f++)
                {
                    // A drag with no depth is a run, not a one-cell-wide deck.
                    if (rows == 0 && f > 0) break;
                    for (int r = 0; r <= columns; r++)
                    {
                        if (f == 0 && r == 0) continue;
                        if (columns == 0 && r > 0) break;
                        if (cells.Count >= budget) return cells.Count;

                        Vector3 offset = axisRight * (signRight * r * pitch.x)
                                       + axisForward * (signForward * f * pitch.z);
                        cells.Add(new PlacementCell { position = anchorPosition + offset, rotation = anchorRotation });
                    }
                }
                return cells.Count;
            }

            // SPAN and PILLAR: one axis, the dominant one.
            Vector3 axis;
            float pitchAlong;
            if (pattern == PlacementPattern.Pillar)
            {
                // 17.44.11-dev: a pillar is VERTICAL, always - straight up from the anchor
                // along gravity. It used to take the frame's up, and a frame borrowed from a
                // neighbouring machine can be rolled, so a "pillar" came out lying on the
                // ground beside the block it started from.
                axis = VoxelEngine.Cosmos.GravityProvider.GetUp(anchorPosition);
                if (axis.sqrMagnitude < 0.0001f) axis = Vector3.up;
                axis.Normalize();
                pitchAlong = pitch.y;
            }
            else
            {
                // 17.44.3-dev: SPAN follows the drag in ANY of the frame's three axes - a
                // wall laid along a face, a row across the ground, or a column built up a
                // slope. It used to consider only right and forward, so a vertical drag
                // could not produce a vertical run at all; PILLAR remains the way to force
                // a column when the drag is ambiguous.
                float ax = Mathf.Abs(local.x);
                float ay = Mathf.Abs(local.y);
                float az = Mathf.Abs(local.z);
                Vector3 axisRight = frame.valid ? frame.right : Vector3.right;
                Vector3 axisUp = frame.valid ? frame.up : Vector3.up;
                Vector3 axisForward = frame.valid ? frame.forward : Vector3.forward;

                if (ay > ax && ay >= az)
                {
                    axis = axisUp;
                    pitchAlong = pitch.y;
                }
                else if (ax >= az)
                {
                    axis = axisRight;
                    pitchAlong = pitch.x;
                }
                else
                {
                    axis = axisForward;
                    pitchAlong = pitch.z;
                }
            }

            // How far the aim has moved ALONG the chosen axis. A drag across the run's
            // own axis contributes nothing, which is what keeps the run dead straight.
            float distance = Vector3.Dot(delta, axis);

            // 17.44.9-dev: a pillar could never be dragged at all. The aim rides the ground,
            // so its travel along the vertical is zero by construction, the count came out
            // zero every time and PILLAR did nothing whatever the player did. When the
            // vertical part of the drag does not dominate, the planar drag drives the stack:
            // drag away from you to build up, back toward you to build down. The cells are
            // still spaced by the block's height, so a stack never interpenetrates.
            if (pattern == PlacementPattern.Pillar)
            {
                float planar = Mathf.Sqrt(local.x * local.x + local.z * local.z);
                if (Mathf.Abs(distance) <= planar * 0.5f)
                {
                    Vector3 planarReference = frame.valid ? frame.forward : Vector3.forward;
                    distance = Vector3.Dot(delta, planarReference) >= 0f ? planar : -planar;
                }
            }

            int count = Mathf.RoundToInt(distance / pitchAlong);

            int cap = Mathf.Min(PlacementOptions.MaxRun, budget - 1);
            count = Mathf.Clamp(count, -cap, cap);
            if (count == 0)
            {
                // A short drag still reads as a run: the first cell in the dragged
                // direction, so the preview always answers the gesture.
                float raw = Mathf.Abs(distance / pitchAlong);
                if (raw > 0.25f) count = distance > 0f ? 1 : -1;
            }

            for (int i = 1; i <= Mathf.Abs(count) && cells.Count < budget; i++)
            {
                cells.Add(new PlacementCell
                {
                    position = anchorPosition + axis * (Mathf.Sign(count) * i * pitchAlong),
                    rotation = anchorRotation,
                });
            }
            return cells.Count;
        }

        /// <summary>
        /// How many cells of a run are actually buildable: the plan, the cap and what the
        /// player is holding all have a vote.
        /// </summary>
        public static int Affordable(int planned, int held) => Mathf.Clamp(Mathf.Min(planned, held), 0, PlacementOptions.MaxCells);
    }
}
