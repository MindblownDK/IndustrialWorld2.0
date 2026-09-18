// Assets/Scripts/VoxelEngine/Building/RailCorridor.cs
//
// TRAIN SYSTEM V2, PHASE 3 — drag-to-lay track, at any gauge.
//
// THE PROBLEM
// Laying rail one cell at a time is the single most tedious thing in the game. A line
// between two bases is hundreds of clicks, every curve is stepped by hand, and a
// gradient mistake is only discovered when a train refuses to connect. Meanwhile the
// road system has had click-and-drag multi-lane corridors with smooth curves for a long
// time.
//
// WHY THIS REUSES THE ROAD SOLVER RATHER THAN WRITING A RAIL ONE
// `RoadCorridor` already solves exactly this geometry: a centreline through waypoints,
// fillet curves at corners, N parallel lanes with an explicit four-corner footprint per
// cell, and a refusal when a corner is too tight for the width. That is the whole job.
//
// Writing a second solver would mean two implementations of the same maths drifting
// apart, and the roadmap explicitly names `RoadCorridor` as the precedent to follow.
// What rail adds on top is the part roads do not care about:
//
//   * GRADIENT. Rail refuses a slope a road drapes over happily. The corridor is checked
//     against `RailTrack.maxGradientMetres` before anything is placed, and refused with
//     the offending rise rather than laid as track no train can use.
//   * GAUGE MEANING. A road's lanes are independent surfaces. A rail corridor's lanes are
//     PARALLEL TRACKS, so a 2-wide corridor is a double-track mainline, not one wide rail.
//
// WHAT THIS IS NOT
// It does not place switches where lines cross. Auto-junctioning is a separate problem -
// it needs to know which of two crossing routes is the through line - and guessing wrong
// would silently reroute a player's trains. Corridors that meet simply connect through
// the existing adjacency rules in `RailTrack.RebuildLinks`.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>One planned rail cell, before anything is committed to the world.</summary>
    public struct RailPlanCell
    {
        public Vector3 position;
        public Quaternion rotation;
        /// <summary>Which parallel track this cell belongs to, 0..gauge-1.</summary>
        public int lane;
    }

    /// <summary>
    /// The result of planning a run: either a placeable set of cells, or a refusal that
    /// names the reason. Never a partial success - laying half a line because the far end
    /// was too steep would leave the player with track that goes nowhere.
    /// </summary>
    public sealed class RailPlan
    {
        public readonly List<RailPlanCell> cells = new(256);

        /// <summary>Null when the plan is placeable; otherwise why it is not.</summary>
        public string Refusal { get; internal set; }

        /// <summary>Steepest rise found between adjacent cells, in metres.</summary>
        public float WorstGradient { get; internal set; }

        public bool IsPlaceable => Refusal == null && cells.Count > 0;
        public int CellCount => cells.Count;

        internal void Reset()
        {
            cells.Clear();
            Refusal = null;
            WorstGradient = 0f;
        }
    }

    public static class RailCorridor
    {
        /// <summary>Widest gauge the tool offers. Three parallel tracks is already a yard.</summary>
        public const int MaxGauge = 3;

        private static readonly RoadCorridorBuffers _buffers = new();
        private static readonly List<Vector3> _waypointScratch = new(8);

        /// <summary>
        /// Plans a run between two points at the given gauge.
        ///
        /// Returns a plan that is either fully placeable or fully refused. The caller is
        /// expected to show the refusal rather than place what it can.
        /// </summary>
        public static RailPlan Plan(RailPlan plan, Vector3 start, Vector3 end,
            int gauge, float cellSize, float maxGradientMetres, Vector3 up)
        {
            plan ??= new RailPlan();
            plan.Reset();

            gauge = Mathf.Clamp(gauge, 1, MaxGauge);
            cellSize = Mathf.Max(0.05f, cellSize);

            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();

            float span = Vector3.Distance(start, end);
            if (span < cellSize)
            {
                plan.Refusal = "Too short - drag further to lay a run.";
                return plan;
            }

            // A hard cap on run length. Without it a mis-drag across a planet would try to
            // solve and place tens of thousands of cells in one frame.
            if (span > MaxRunMetres)
            {
                plan.Refusal = $"Run too long ({span:0} m). Lay it in sections of {MaxRunMetres:0} m or less.";
                return plan;
            }

            _waypointScratch.Clear();
            _waypointScratch.Add(start);
            _waypointScratch.Add(end);

            // The road solver does the geometry: centreline, fillets, and one explicit
            // four-corner footprint per cell per lane.
            RoadCorridor.Build(_buffers, _waypointScratch, up, gauge, cellSize,
                RoadCorridor.MinimumRadius(gauge, cellSize));

            if (_buffers.cornerTooTight)
            {
                plan.Refusal = $"Corner too tight for a {gauge}-wide gauge. Widen the curve or lay a narrower run.";
                return plan;
            }

            if (_buffers.cellsPerLane <= 0)
            {
                plan.Refusal = "No route could be solved between those points.";
                return plan;
            }

            // Collect every lane's cells, dropped onto the real ground.
            for (int lane = 0; lane < gauge; lane++)
            {
                for (int i = 0; i < _buffers.cellsPerLane; i++)
                {
                    var frame = RoadCorridor.CellAt(_buffers, lane, i);
                    if (frame == null) continue;

                    Vector3 grounded = DropToGround(frame.position, up);
                    plan.cells.Add(new RailPlanCell
                    {
                        position = grounded,
                        rotation = frame.rotation,
                        lane = lane,
                    });
                }
            }

            if (plan.cells.Count == 0)
            {
                plan.Refusal = "No route could be solved between those points.";
                return plan;
            }

            CheckGradient(plan, gauge, maxGradientMetres, up);
            return plan;
        }

        /// <summary>
        /// Rejects a run that climbs faster than a train can pull.
        ///
        /// Checked PER LANE along the direction of travel, because that is the slope a
        /// train actually experiences. Comparing across lanes would measure the cant of the
        /// formation instead, which is not a gradient at all.
        /// </summary>
        private static void CheckGradient(RailPlan plan, int gauge, float maxGradientMetres, Vector3 up)
        {
            int perLane = plan.cells.Count / Mathf.Max(1, gauge);
            float worst = 0f;

            for (int lane = 0; lane < gauge; lane++)
            {
                int baseIndex = lane * perLane;
                for (int i = 1; i < perLane; i++)
                {
                    int a = baseIndex + i - 1;
                    int b = baseIndex + i;
                    if (a < 0 || b >= plan.cells.Count) continue;

                    Vector3 delta = plan.cells[b].position - plan.cells[a].position;
                    float rise = Mathf.Abs(Vector3.Dot(delta, up));
                    if (rise > worst) worst = rise;
                }
            }

            plan.WorstGradient = worst;

            if (worst > maxGradientMetres)
            {
                plan.Refusal =
                    $"Gradient too steep: {worst:0.00} m rise per cell, limit {maxGradientMetres:0.00} m. " +
                    "Cut or fill the ground, or route around the slope.";
                plan.cells.Clear();
            }
        }

        /// <summary>
        /// Finds the real ground under a planned point.
        ///
        /// The corridor solver works on a flat tangent plane - correct for the geometry,
        /// but a planet is not flat, so every cell has to be re-dropped. This mirrors what
        /// the road paver does for exactly the same reason.
        /// </summary>
        private static Vector3 DropToGround(Vector3 point, Vector3 up)
        {
            const float probeUp = 6f;
            const float probeLength = 14f;

            if (Physics.Raycast(point + up * probeUp, -up, out var hit, probeLength,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }
            return point;
        }

        /// <summary>Longest run a single drag may lay, in metres.</summary>
        public const float MaxRunMetres = 400f;

        /// <summary>
        /// Commits a plan to the world, returning how many cells were actually placed.
        ///
        /// Skips any cell that already has track, so overlapping two runs extends a network
        /// instead of stacking duplicate rails inside each other.
        /// </summary>
        public static int Commit(RailPlan plan, BlockItem trackBlock)
        {
            if (plan == null || !plan.IsPlaceable) return 0;
            if (trackBlock == null || trackBlock.placedPrefab == null) return 0;

            int placed = 0;
            var laid = new List<RailTrack>(plan.cells.Count);

            for (int i = 0; i < plan.cells.Count; i++)
            {
                var cell = plan.cells[i];

                // Never stack track. A second run crossing the first should join it, which
                // the adjacency rules already handle once both cells exist.
                if (RailNetwork.FindNearest(cell.position, 0.45f) != null) continue;

                var go = Object.Instantiate(trackBlock.placedPrefab, cell.position, cell.rotation);
                go.name = trackBlock.displayName;

                var placedBlock = go.GetComponent<PlacedBlock>();
                if (placedBlock == null) placedBlock = go.AddComponent<PlacedBlock>();
                placedBlock.Item = trackBlock;
                placedBlock.Hp = Mathf.Max(1, trackBlock.blockHealth);

                var track = go.GetComponent<RailTrack>();
                if (track != null) laid.Add(track);

                placed++;
            }

            // Link the whole run AFTER every cell exists. Linking as we go would let each
            // cell fill its limited link budget with the one behind it before the one ahead
            // had even been created, leaving a line of disconnected pairs.
            for (int i = 0; i < laid.Count; i++)
                if (laid[i] != null) laid[i].RebuildLinks(propagate: true);

            return placed;
        }
    }
}
