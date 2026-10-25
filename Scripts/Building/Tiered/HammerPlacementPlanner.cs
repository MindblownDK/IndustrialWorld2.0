// Assets/Scripts/VoxelEngine/Building/Tiered/HammerPlacementPlanner.cs
//
// Turns a hammer press, the current snapped aim, and the selected placement pattern into
// module-exact cell transforms. This planner is pure geometry: BuildSystemV2 remains the
// authority for socket compatibility, overlap, affordability, and structural support.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Module-grid plan for the Building Hammer. The preview and commit share this list.</summary>
    public static class HammerPlacementPlanner
    {
        private const float Module = 7.5f;
        private const float Storey = 5.625f;
        private const float HalfWallHeight = 2.8f;
        private const float FirstCellThreshold = 0.34f;

        /// <summary>Whether a family has a useful, connected meaning for this pattern.</summary>
        public static bool Supports(BuildFamily family, PlacementPattern pattern)
        {
            if (pattern == PlacementPattern.Single) return true;

            switch (family)
            {
                case BuildFamily.Foundation:
                case BuildFamily.Floor:
                case BuildFamily.FloorHatch:
                case BuildFamily.Roof:
                    return pattern == PlacementPattern.Span || pattern == PlacementPattern.Deck;

                case BuildFamily.Wall:
                case BuildFamily.HalfWall:
                case BuildFamily.Doorway:
                case BuildFamily.Window:
                case BuildFamily.WallFrame:
                    return pattern == PlacementPattern.Span || pattern == PlacementPattern.Pillar
                        || pattern == PlacementPattern.Wall;

                case BuildFamily.TriangularWall:
                case BuildFamily.TriangularWallInverted:
                case BuildFamily.CompoundWall:
                    // These pieces have side joints but no authored top socket; a
                    // connected horizontal run is supported, a vertical sheet is not.
                    return pattern == PlacementPattern.Span;

                case BuildFamily.Pillar:
                    return pattern == PlacementPattern.Pillar;

                case BuildFamily.Stairs:
                case BuildFamily.SlantedRoof:
                case BuildFamily.SlantedTriangularRoof:
                    return pattern == PlacementPattern.Span || pattern == PlacementPattern.Deck
                        || pattern == PlacementPattern.Pillar;

                case BuildFamily.TriangularRoof:
                case BuildFamily.CornerRoof:
                case BuildFamily.SlantedCornerRoofInverted:
                case BuildFamily.PyramidRoof:
                    return pattern == PlacementPattern.Span || pattern == PlacementPattern.Deck;

                default:
                    // Fittings, railings, gate frames, orbital station pieces and all
                    // other authored families keep their one-piece placement contract.
                    return false;
            }
        }

        /// <summary>Human-readable reason for the single-click fallback.</summary>
        public static string UnsupportedReason(BuildFamily family, PlacementPattern pattern)
        {
            if (pattern == PlacementPattern.Single) return string.Empty;
            if (family == BuildFamily.Roof)
                return "Flat roof pieces use SPAN or DECK; they do not form a vertical wall or stack.";
            if (family == BuildFamily.Stairs || BuildFamilyInfo.IsRoofPanel(family))
                return "This piece only chains along its authored slope or span.";
            if (family == BuildFamily.TriangularWall || family == BuildFamily.TriangularWallInverted
                || family == BuildFamily.CompoundWall)
                return "This piece has side joints only; use SPAN for a connected run.";
            if (family == BuildFamily.WallFrame)
                return "Wall Frames join at their authored side and top sockets; use SPAN, WALL or PILLAR.";
            if (family == BuildFamily.Pillar)
                return "Pillars stack upward; use PILLAR to extend the support chain.";
            if (family == BuildFamily.Foundation || BuildFamilyInfo.IsDeck(family))
                return "Flat pieces use SPAN or DECK; choose a horizontal placement pattern.";
            if (StructuralLoadState.IsVerticalSupport(family))
                return "This wall family supports SPAN, WALL or PILLAR placement.";
            return "This family is placed one piece at a time.";
        }

        /// <summary>Initial wall run choice: face the player and let the drag turn it later.</summary>
        public static bool InitialWallRunIsDepth(Quaternion rotation, Vector3 up, Vector3 cameraForward)
        {
            Vector3 side, depth;
            ResolveHorizontalAxes(rotation, up, out side, out depth);
            Vector3 forward = Vector3.ProjectOnPlane(cameraForward, up);
            if (forward.sqrMagnitude < 0.04f) return false;
            forward.Normalize();
            return Mathf.Abs(Vector3.Dot(forward, side)) > Mathf.Abs(Vector3.Dot(forward, depth));
        }

        /// <summary>Hysteretic view switch, so a wall never flickers on the diagonal.</summary>
        public static bool UpdateWallRunIsDepth(bool currentDepth, Quaternion rotation,
            Vector3 up, Vector3 cameraForward)
        {
            Vector3 side, depth;
            ResolveHorizontalAxes(rotation, up, out side, out depth);
            Vector3 forward = Vector3.ProjectOnPlane(cameraForward, up);
            if (forward.sqrMagnitude < 0.04f) return currentDepth;
            forward.Normalize();

            float sideScore = Mathf.Abs(Vector3.Dot(forward, depth));
            float depthScore = Mathf.Abs(Vector3.Dot(forward, side));
            float current = currentDepth ? depthScore : sideScore;
            float other = currentDepth ? sideScore : depthScore;
            return other > current + 0.18f && other > 0.55f ? !currentDepth : currentDepth;
        }

        /// <summary>
        /// Fills anchor-first cells. A budget of one still gives the anchor; zero means no
        /// affordable plan and clears the list. Module sockets, not the cosmetic lattice
        /// step, define the distance between structural pieces.
        /// </summary>
        public static int Build(BuildFamily family, PlacementPattern pattern,
            Vector3 anchor, Quaternion rotation, Vector3 target, Vector3 up,
            bool wallRunIsDepth, float pillarHeight, int cellBudget, List<PlacementCell> cells,
            out Vector2Int size, out string planeLabel)
        {
            size = Vector2Int.zero;
            planeLabel = string.Empty;
            if (cells == null) return 0;
            cells.Clear();
            if (cellBudget <= 0 || pattern == PlacementPattern.Single) return 0;

            cellBudget = Mathf.Min(cellBudget, PlacementOptions.MaxCells);
            cells.Add(new PlacementCell { position = anchor, rotation = rotation });
            size = Vector2Int.one;
            if (cellBudget == 1)
            {
                bool singleCellSlopeFamily = family == BuildFamily.Stairs
                    || family == BuildFamily.SlantedRoof
                    || family == BuildFamily.SlantedTriangularRoof;
                planeLabel = pattern switch
                {
                    PlacementPattern.Deck => singleCellSlopeFamily ? "SLOPE · WIDTH + RISE" : "FLAT · SIDE + DEPTH",
                    PlacementPattern.Wall => wallRunIsDepth ? "VERTICAL · DEPTH + UP" : "VERTICAL · SIDE + UP",
                    PlacementPattern.Pillar => singleCellSlopeFamily ? "CHAIN · UP THE SLOPE" : "STACK · UP",
                    _ => "LINE",
                };
                return 1;
            }

            if (up.sqrMagnitude < 0.0001f) up = GravityProvider.GetUp(anchor);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 right, forward;
            ResolveHorizontalAxes(rotation, up, out right, out forward);
            Vector3 delta = target - anchor;

            bool slopeFamily = family == BuildFamily.Stairs
                || family == BuildFamily.SlantedRoof
                || family == BuildFamily.SlantedTriangularRoof;
            Vector3 slopeStep = forward * Module + up * Storey;
            float slopeLength = slopeStep.magnitude;
            Vector3 slopeAxis = slopeLength > 0.0001f ? slopeStep / slopeLength : forward;
            float verticalPitch = family == BuildFamily.HalfWall ? HalfWallHeight : Storey;

            if (pattern == PlacementPattern.Deck || pattern == PlacementPattern.Wall)
            {
                Vector3 axisA;
                Vector3 axisB;
                float pitchA;
                float pitchB;
                string label;

                if (pattern == PlacementPattern.Wall)
                {
                    axisA = wallRunIsDepth ? forward : right;
                    axisB = up;
                    pitchA = Module;
                    pitchB = verticalPitch;
                    label = wallRunIsDepth
                        ? "VERTICAL · DEPTH + UP"
                        : "VERTICAL · SIDE + UP";
                }
                else if (slopeFamily)
                {
                    axisA = right;
                    axisB = slopeAxis;
                    pitchA = Module;
                    pitchB = slopeLength;
                    label = "SLOPE · WIDTH + RISE";
                }
                else
                {
                    axisA = right;
                    axisB = forward;
                    pitchA = Module;
                    pitchB = Module;
                    label = "FLAT · SIDE + DEPTH";
                }

                int maxPerAxis = PlacementOptions.MaxDeckSide - 1;
                int countA = CountAlong(delta, axisA, pitchA, maxPerAxis);
                int countB = CountAlong(delta, axisB, pitchB, maxPerAxis);
                int columns = Mathf.Abs(countA);
                int rows = Mathf.Abs(countB);
                int aimedColumns = columns;
                int aimedRows = rows;

                // Keep the rectangle complete and preserve the proportions of the aim.
                while ((columns + 1) * (rows + 1) > cellBudget && (columns > 0 || rows > 0))
                {
                    if (columns == 0) { rows--; continue; }
                    if (rows == 0) { columns--; continue; }
                    float pastColumns = aimedColumns > 0 ? columns / (float)aimedColumns : 0f;
                    float pastRows = aimedRows > 0 ? rows / (float)aimedRows : 0f;
                    if (pastColumns >= pastRows) columns--;
                    else rows--;
                }

                size = new Vector2Int(columns + 1, rows + 1);
                planeLabel = label;
                EmitRectangle(cells, anchor, rotation, axisA, axisB, pitchA, pitchB,
                    countA, countB, columns, rows, cellBudget);
                return cells.Count;
            }

            Vector3 chosenAxis;
            float chosenPitch;
            string chosenLabel;
            if (pattern == PlacementPattern.Pillar)
            {
                if (slopeFamily)
                {
                    chosenAxis = slopeAxis;
                    chosenPitch = slopeLength;
                    chosenLabel = "CHAIN · UP THE SLOPE";
                }
                else
                {
                    chosenAxis = up;
                    chosenPitch = family == BuildFamily.Pillar
                        ? Mathf.Max(0.5f, pillarHeight) : verticalPitch;
                    chosenLabel = "STACK · UP";
                }
            }
            else
            {
                ChooseSpanAxis(family, slopeFamily, right, forward, up, slopeAxis,
                    slopeLength, verticalPitch, delta, out chosenAxis, out chosenPitch, out chosenLabel);
            }

            int run = CountAlong(delta, chosenAxis, chosenPitch,
                Mathf.Min(PlacementOptions.MaxRun, cellBudget - 1));
            int cellsAlong = Mathf.Min(Mathf.Abs(run), cellBudget - 1);
            size = new Vector2Int(cellsAlong + 1, 1);
            planeLabel = chosenLabel;
            Vector3 step = chosenAxis * (Mathf.Sign(run) * chosenPitch);
            for (int i = 1; i <= cellsAlong; i++)
                cells.Add(new PlacementCell { position = anchor + step * i, rotation = rotation });
            return cells.Count;
        }

        private static void ChooseSpanAxis(BuildFamily family, bool slopeFamily,
            Vector3 right, Vector3 forward, Vector3 up, Vector3 slopeAxis,
            float slopeLength, float verticalPitch, Vector3 delta,
            out Vector3 chosenAxis, out float chosenPitch, out string label)
        {
            chosenAxis = right;
            chosenPitch = Module;
            label = "LINE · SIDE";
            float best = -1f;

            if (family == BuildFamily.Foundation || family == BuildFamily.Floor
                || family == BuildFamily.FloorHatch || family == BuildFamily.Roof
                || family == BuildFamily.TriangularRoof || family == BuildFamily.CornerRoof
                || family == BuildFamily.SlantedCornerRoofInverted || family == BuildFamily.PyramidRoof)
            {
                ConsiderSpanAxis(right, Module, "LINE · SIDE", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
                ConsiderSpanAxis(forward, Module, "LINE · DEPTH", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
            }
            else if (family == BuildFamily.TriangularWall
                || family == BuildFamily.TriangularWallInverted
                || family == BuildFamily.WallFrame || family == BuildFamily.CompoundWall)
            {
                ConsiderSpanAxis(right, Module, "LINE · SIDE", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
            }
            else if (family == BuildFamily.Pillar)
            {
                ConsiderSpanAxis(up, Storey, "LINE · UP", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
            }
            else if (slopeFamily)
            {
                ConsiderSpanAxis(right, Module, "LINE · WIDTH", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
                ConsiderSpanAxis(slopeAxis, slopeLength, "LINE · UP THE SLOPE", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
            }
            else
            {
                ConsiderSpanAxis(right, Module, "LINE · SIDE", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
                ConsiderSpanAxis(forward, Module, "LINE · DEPTH", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
                ConsiderSpanAxis(up, verticalPitch, "LINE · UP", delta,
                    ref best, ref chosenAxis, ref chosenPitch, ref label);
            }
        }

        private static void ConsiderSpanAxis(Vector3 axis, float pitch, string axisLabel,
            Vector3 delta, ref float best, ref Vector3 chosenAxis, ref float chosenPitch,
            ref string label)
        {
            if (pitch <= 0.001f || axis.sqrMagnitude < 0.0001f) return;
            float score = Mathf.Abs(Vector3.Dot(delta, axis.normalized)) / pitch;
            if (score <= best) return;
            best = score;
            chosenAxis = axis;
            chosenPitch = pitch;
            label = axisLabel;
        }

        private static void EmitRectangle(List<PlacementCell> cells, Vector3 anchor,
            Quaternion rotation, Vector3 axisA, Vector3 axisB, float pitchA, float pitchB,
            int signedColumns, int signedRows, int columns, int rows, int budget)
        {
            float signA = signedColumns < 0 ? -1f : 1f;
            float signB = signedRows < 0 ? -1f : 1f;
            for (int ring = 1; ring <= columns + rows; ring++)
            {
                for (int b = 0; b <= rows; b++)
                {
                    int a = ring - b;
                    if (a < 0 || a > columns) continue;
                    if (a == 0 && b == 0) continue;
                    if (cells.Count >= budget) return;
                    Vector3 position = anchor + axisA * (signA * a * pitchA)
                                                 + axisB * (signB * b * pitchB);
                    cells.Add(new PlacementCell { position = position, rotation = rotation });
                }
            }
        }

        private static int CountAlong(Vector3 delta, Vector3 axis, float pitch, int cap)
        {
            if (cap <= 0 || pitch <= 0.001f || axis.sqrMagnitude < 0.0001f) return 0;
            float distance = Vector3.Dot(delta, axis.normalized);
            float cells = distance / pitch;
            int count = Mathf.RoundToInt(cells);
            if (count == 0 && Mathf.Abs(cells) > FirstCellThreshold)
                count = distance < 0f ? -1 : 1;
            return Mathf.Clamp(count, -cap, cap);
        }

        public static void ResolveHorizontalAxes(Quaternion rotation, Vector3 up,
            out Vector3 right, out Vector3 forward)
        {
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            right = Vector3.ProjectOnPlane(rotation * Vector3.right, up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.ProjectOnPlane(Vector3.right, up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.Cross(Vector3.forward, up);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();

            forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, up);
            forward = Vector3.ProjectOnPlane(forward, right);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
        }
    }
}
