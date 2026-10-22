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
                                Vector3 targetPosition, float step, int cellBudget,
                                List<PlacementCell> cells)
        {
            if (cells == null) return 0;
            cells.Clear();

            if (step <= 0.0001f) step = 1f;
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

                        Vector3 offset = axisRight * (signRight * r * step)
                                       + axisForward * (signForward * f * step);
                        cells.Add(new PlacementCell { position = anchorPosition + offset, rotation = anchorRotation });
                    }
                }
                return cells.Count;
            }

            // SPAN and PILLAR: one axis, the dominant one.
            Vector3 axis = pattern == PlacementPattern.Pillar
                ? (frame.valid ? frame.up : Vector3.up)
                : DominantPlanarAxis(local, frame);

            // How far the aim has moved ALONG the chosen axis. A drag across the run's
            // own axis contributes nothing, which is what keeps the run dead straight.
            float distance = Vector3.Dot(delta, axis);
            int count = Mathf.RoundToInt(distance / step);

            int cap = Mathf.Min(PlacementOptions.MaxRun, budget - 1);
            count = Mathf.Clamp(count, -cap, cap);
            if (count == 0)
            {
                // A short drag still reads as a run: the first cell in the dragged
                // direction, so the preview always answers the gesture.
                float raw = Mathf.Abs(distance / step);
                if (raw > 0.25f) count = distance > 0f ? 1 : -1;
            }

            for (int i = 1; i <= Mathf.Abs(count) && cells.Count < budget; i++)
            {
                cells.Add(new PlacementCell
                {
                    position = anchorPosition + axis * (Mathf.Sign(count) * i * step),
                    rotation = anchorRotation,
                });
            }
            return cells.Count;
        }

        /// <summary>The frame axis (right / forward) the drag has moved furthest along.</summary>
        private static Vector3 DominantPlanarAxis(Vector3 localDelta, in PlacementFrame frame)
        {
            Vector3 right = frame.valid ? frame.right : Vector3.right;
            Vector3 forward = frame.valid ? frame.forward : Vector3.forward;
            return Mathf.Abs(localDelta.x) >= Mathf.Abs(localDelta.z) ? right : forward;
        }

        /// <summary>
        /// How many cells of a run are actually buildable: the plan, the cap and what the
        /// player is holding all have a vote.
        /// </summary>
        public static int Affordable(int planned, int held) => Mathf.Clamp(Mathf.Min(planned, held), 0, PlacementOptions.MaxCells);
    }
}
