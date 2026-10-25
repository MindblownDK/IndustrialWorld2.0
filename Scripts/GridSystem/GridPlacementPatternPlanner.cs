// Assets/Scripts/VoxelEngine/GridSystem/GridPlacementPatternPlanner.cs
//
// Cell-space plans for ordinary, one-cell grid blocks. Coordinates stay on the
// native GridEntity lattice from aim through preview and release validation.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.GridSystem
{
    /// <summary>Builds a bounded list of native-grid cells for the shared placement patterns.</summary>
    public static class GridPlacementPatternPlanner
    {
        public static int Build(PlacementPattern pattern, Vector3Int anchor, Vector3Int target,
            int cellBudget, List<Vector3Int> cells, out Vector2Int dimensions, out string planeLabel)
        {
            dimensions = Vector2Int.one;
            planeLabel = string.Empty;
            if (cells == null) return 0;

            cells.Clear();
            int budget = Mathf.Clamp(cellBudget, 1, PlacementOptions.MaxCells);
            cells.Add(anchor);
            if (pattern == PlacementPattern.Single || budget == 1) return cells.Count;

            Vector3Int delta = target - anchor;
            switch (pattern)
            {
                case PlacementPattern.Span:
                    BuildSpan(anchor, delta, budget, cells, out dimensions, out planeLabel);
                    break;

                case PlacementPattern.Pillar:
                    BuildPillar(anchor, delta.y, budget, cells, out dimensions, out planeLabel);
                    break;

                case PlacementPattern.Deck:
                    BuildRectangle(anchor, Vector3Int.right, delta.x, Vector3Int.forward, delta.z,
                        budget, cells, out dimensions);
                    planeLabel = "FLAT · X + Z";
                    break;

                case PlacementPattern.Wall:
                    Vector3Int runAxis = Mathf.Abs(delta.x) >= Mathf.Abs(delta.z)
                        ? Vector3Int.right : Vector3Int.forward;
                    int runDelta = runAxis == Vector3Int.right ? delta.x : delta.z;
                    BuildRectangle(anchor, runAxis, runDelta, Vector3Int.up, delta.y,
                        budget, cells, out dimensions);
                    planeLabel = runAxis == Vector3Int.right ? "X RUN + UP" : "Z RUN + UP";
                    break;

                default:
                    dimensions = Vector2Int.one;
                    planeLabel = string.Empty;
                    break;
            }

            return cells.Count;
        }

        private static void BuildSpan(Vector3Int anchor, Vector3Int delta, int budget,
            List<Vector3Int> cells, out Vector2Int dimensions, out string planeLabel)
        {
            int ax = Mathf.Abs(delta.x);
            int ay = Mathf.Abs(delta.y);
            int az = Mathf.Abs(delta.z);
            Vector3Int axis;
            int signedDistance;
            if (ay > ax && ay >= az)
            {
                axis = Vector3Int.up;
                signedDistance = delta.y;
            }
            else if (ax >= az)
            {
                axis = Vector3Int.right;
                signedDistance = delta.x;
            }
            else
            {
                axis = Vector3Int.forward;
                signedDistance = delta.z;
            }

            int steps = Mathf.Min(Mathf.Min(Mathf.Abs(signedDistance), PlacementOptions.MaxRun - 1), budget - 1);
            int sign = signedDistance < 0 ? -1 : 1;
            for (int i = 1; i <= steps; i++)
                cells.Add(anchor + axis * (sign * i));

            dimensions = new Vector2Int(cells.Count, 1);
            planeLabel = axis == Vector3Int.right ? "X RUN"
                : axis == Vector3Int.up ? "GRID UP" : "Z RUN";
        }

        private static void BuildPillar(Vector3Int anchor, int verticalDelta, int budget,
            List<Vector3Int> cells, out Vector2Int dimensions, out string planeLabel)
        {
            int steps = Mathf.Min(Mathf.Min(Mathf.Abs(verticalDelta), PlacementOptions.MaxRun - 1), budget - 1);
            int sign = verticalDelta < 0 ? -1 : 1;
            for (int i = 1; i <= steps; i++)
                cells.Add(anchor + Vector3Int.up * (sign * i));

            dimensions = new Vector2Int(1, cells.Count);
            planeLabel = "GRID UP";
        }

        private static void FitRectangleToBudget(ref int columns, ref int rows, int budget)
        {
            int aimedColumns = columns;
            int aimedRows = rows;
            int bestColumns = 0;
            int bestRows = 0;
            int bestArea = 0;
            float bestImbalance = float.MaxValue;

            for (int candidateColumns = 0; candidateColumns <= aimedColumns; candidateColumns++)
            {
                for (int candidateRows = 0; candidateRows <= aimedRows; candidateRows++)
                {
                    int area = (candidateColumns + 1) * (candidateRows + 1);
                    if (area > budget) continue;

                    float progressColumns = aimedColumns > 0
                        ? candidateColumns / (float)aimedColumns : 1f;
                    float progressRows = aimedRows > 0
                        ? candidateRows / (float)aimedRows : 1f;
                    float imbalance = Mathf.Abs(progressColumns - progressRows);
                    if (area > bestArea || area == bestArea && imbalance < bestImbalance)
                    {
                        bestArea = area;
                        bestColumns = candidateColumns;
                        bestRows = candidateRows;
                        bestImbalance = imbalance;
                    }
                }
            }

            columns = bestColumns;
            rows = bestRows;
        }

        private static void BuildRectangle(Vector3Int anchor, Vector3Int axisA, int deltaA,
            Vector3Int axisB, int deltaB, int budget, List<Vector3Int> cells,
            out Vector2Int dimensions)
        {
            int columns = Mathf.Clamp(Mathf.Abs(deltaA), 0, PlacementOptions.MaxDeckSide - 1);
            int rows = Mathf.Clamp(Mathf.Abs(deltaB), 0, PlacementOptions.MaxDeckSide - 1);
            FitRectangleToBudget(ref columns, ref rows, budget);

            int signA = deltaA < 0 ? -1 : 1;
            int signB = deltaB < 0 ? -1 : 1;
            for (int ring = 1; ring <= columns + rows; ring++)
            {
                for (int alongB = 0; alongB <= rows; alongB++)
                {
                    int alongA = ring - alongB;
                    if (alongA < 0 || alongA > columns) continue;
                    if (alongA == 0 && alongB == 0) continue;
                    if (cells.Count >= budget) break;

                    cells.Add(anchor
                        + axisA * (signA * alongA)
                        + axisB * (signB * alongB));
                }
            }

            dimensions = new Vector2Int(columns + 1, rows + 1);
        }
    }
}
