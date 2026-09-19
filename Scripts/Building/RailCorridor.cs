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

        /// <summary>
        /// False when this cell cannot be built: underwater, buried in rock, or occupied
        /// by an existing block. Per-cell rather than per-run so the ghost can show the
        /// player exactly WHERE a route fails instead of just refusing all of it.
        /// </summary>
        public bool valid;

        /// <summary>Why this cell is invalid, for the readout. Empty when valid.</summary>
        public string problem;
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

        /// <summary>Cells the solver produced before grounding. Diagnostic.</summary>
        public int SolvedCells { get; internal set; }

        /// <summary>Cells that found no ground under them. Diagnostic.</summary>
        public int MissedGround { get; internal set; }

        /// <summary>Cells that are underwater, buried or obstructed.</summary>
        public int BlockedCells { get; internal set; }

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
            _twoPointScratch.Clear();
            _twoPointScratch.Add(start);
            _twoPointScratch.Add(end);
            return Plan(plan, _twoPointScratch, gauge, cellSize, maxGradientMetres, up);
        }

        private static readonly List<Vector3> _twoPointScratch = new(2);

        /// <summary>
        /// Plans a run through any number of waypoints, so a player can chain turns before
        /// committing. The corridor solver already fillets every interior corner, so a
        /// multi-leg route curves properly rather than forming hard angles.
        /// </summary>
        public static RailPlan Plan(RailPlan plan, List<Vector3> waypoints,
            int gauge, float cellSize, float maxGradientMetres, Vector3 up)
        {
            plan ??= new RailPlan();
            plan.Reset();

            gauge = Mathf.Clamp(gauge, 1, MaxGauge);
            cellSize = Mathf.Max(0.05f, cellSize);

            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();

            if (waypoints == null || waypoints.Count < 2)
            {
                plan.Refusal = "Need at least a start and an end.";
                return plan;
            }

            // Total route length across every leg, not just first-to-last: a long dog-leg
            // is a long run even when its endpoints are close together.
            float span = 0f;
            for (int i = 1; i < waypoints.Count; i++)
                span += Vector3.Distance(waypoints[i - 1], waypoints[i]);

            Vector3 start = waypoints[0];
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
            for (int i = 0; i < waypoints.Count; i++) _waypointScratch.Add(waypoints[i]);

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
            int missedGround = 0;
            int blockedCells = 0;
            plan.SolvedCells = _buffers.cellsPerLane * gauge;
            for (int lane = 0; lane < gauge; lane++)
            {
                for (int i = 0; i < _buffers.cellsPerLane; i++)
                {
                    var frame = RoadCorridor.CellAt(_buffers, lane, i);
                    if (frame == null) continue;

                    if (!DropToGround(frame.position, up, out Vector3 grounded))
                    {
                        // No ground under this cell at all - the run leaves the terrain.
                        // Refusing is right, but it must say THAT rather than pretending
                        // the route produced nothing.
                        missedGround++;
                        continue;
                    }

                    bool ok = EvaluateCell(grounded, up, out string problem);
                    if (!ok) blockedCells++;

                    plan.cells.Add(new RailPlanCell
                    {
                        position = grounded,
                        rotation = frame.rotation,
                        lane = lane,
                        valid = ok,
                        problem = problem,
                    });
                }
            }

            plan.MissedGround = missedGround;
            plan.BlockedCells = blockedCells;

            if (plan.cells.Count == 0)
            {
                // Name the actual failure with numbers. "No placeable cells" told the player
                // nothing and told me nothing either - two releases were spent guessing at
                // this because the message could not distinguish its own causes.
                plan.Refusal = missedGround > 0
                    ? $"No ground under that route ({missedGround} of {plan.SolvedCells} cells " +
                      "found nothing below them). Aim at solid ground you can see."
                    : $"The corridor solved {plan.SolvedCells} cells but none survived. " +
                      "This is a bug - please report the run you attempted.";
                return plan;
            }

            // Smooth the profile BEFORE judging it. Real track is laid on a graded
            // formation - the ground is cut and filled to suit the railway, not the other
            // way round. Refusing every natural slope made the tool unusable on terrain
            // that a real railway would simply grade flat.
            SmoothProfile(plan, gauge, maxGradientMetres, up);
            CheckGradient(plan, gauge, maxGradientMetres, up);

            // Any blocked cell refuses the RUN - a line with a gap in it is not a line -
            // but the cells survive so the ghost can show exactly which stretch is the
            // problem. Clearing them would hide the very information the player needs.
            if (plan.Refusal == null && blockedCells > 0)
            {
                var firstProblem = "obstructed";
                for (int i = 0; i < plan.cells.Count; i++)
                {
                    if (plan.cells[i].valid) continue;
                    firstProblem = plan.cells[i].problem;
                    break;
                }
                plan.Refusal = $"{blockedCells} of {plan.cells.Count} cells {firstProblem}. " +
                               "The red section shows where.";
            }

            return plan;
        }

        /// <summary>
        /// Eases the vertical profile so the line climbs at a rate a train can pull.
        ///
        /// WHY THIS EXISTS
        /// A rail corridor draped straight onto raw terrain inherits every bump, and a
        /// single step over the gradient limit failed the entire run. That is not how track
        /// is built: a railway grades its formation, cutting through high ground and filling
        /// low ground so the rails run smoothly.
        ///
        /// So the run is smoothed toward a gentle profile first, and only genuinely
        /// impossible terrain - a cliff the smoothing cannot absorb - is refused. The
        /// ballast bed placed underneath is what visually sells the fill.
        ///
        /// Each lane is smoothed independently along its own direction of travel, which is
        /// the axis a train actually experiences.
        /// </summary>
        private static void SmoothProfile(RailPlan plan, int gauge, float maxGradientMetres, Vector3 up)
        {
            int perLane = plan.cells.Count / Mathf.Max(1, gauge);
            if (perLane < 3) return;

            // Several light passes rather than one aggressive pass: a strong single pass
            // pulls the ends of the run away from the ground the player aimed at, which
            // makes the track visibly float at the point they clicked.
            const int passes = 6;

            for (int pass = 0; pass < passes; pass++)
            {
                for (int lane = 0; lane < gauge; lane++)
                {
                    int baseIndex = lane * perLane;

                    // Ends are pinned: the run must still start and finish where the player
                    // pointed, or a smoothed line drifts off its own endpoints.
                    for (int i = 1; i < perLane - 1; i++)
                    {
                        int prev = baseIndex + i - 1;
                        int cur = baseIndex + i;
                        int next = baseIndex + i + 1;
                        if (next >= plan.cells.Count) break;

                        var a = plan.cells[prev].position;
                        var c = plan.cells[cur].position;
                        var b = plan.cells[next].position;

                        // Only the component ALONG gravity is smoothed. Touching the
                        // horizontal component would pull the line off the route the
                        // corridor solved and undo the curve fitting.
                        float ha = Vector3.Dot(a, up);
                        float hc = Vector3.Dot(c, up);
                        float hb = Vector3.Dot(b, up);

                        float target = (ha + hb) * 0.5f;
                        float eased = Mathf.Lerp(hc, target, 0.5f);

                        var cell = plan.cells[cur];
                        cell.position = c + up * (eased - hc);
                        plan.cells[cur] = cell;
                    }
                }
            }
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
        /// <summary>
        /// Turns crossings into real junctions.
        ///
        /// WHY THIS IS NEEDED
        /// Plain track holds at most two links, so where a new line crosses an old one the
        /// adjacency pass simply cannot connect all four arms - the extra neighbours are
        /// silently dropped and the two lines pass through each other without joining. The
        /// player sees rails that visibly cross and a train that cannot take the turn.
        ///
        /// A cell with three or more neighbours IS a junction by definition, so any cell
        /// that ends up in that position is promoted and re-linked. Promotion widens its
        /// link budget from two to four, which is what lets the crossing actually connect.
        ///
        /// Only cells that genuinely have extra neighbours are promoted - a straight run
        /// stays straight track, so nothing becomes a switch by accident.
        /// </summary>
        private static int PromoteCrossingsToJunctions(List<RailTrack> laid)
        {
            int promoted = 0;

            for (int i = 0; i < laid.Count; i++)
            {
                var cell = laid[i];
                if (cell == null) continue;

                // Count every rail neighbour, not just the ones it managed to link: a cell
                // already full at two links is exactly the case that needs promoting, and
                // its Links list would not reveal the third arm.
                int neighbours = CountRailNeighbours(cell);
                if (neighbours < 3) continue;
                if (cell.pieceKind == RailPieceKind.Switch) continue;

                cell.pieceKind = RailPieceKind.Switch;
                promoted++;
            }

            if (promoted == 0) return 0;

            // Re-link AFTER every promotion. A cell promoted late would otherwise be linked
            // against neighbours that were still two-link limited when they were processed.
            for (int i = 0; i < laid.Count; i++)
                if (laid[i] != null) laid[i].RebuildLinks(propagate: true);

            // The existing line on the other side of the crossing also has to re-link, or it
            // keeps the two links it had before the junction appeared.
            for (int i = 0; i < laid.Count; i++)
            {
                var cell = laid[i];
                if (cell == null || cell.pieceKind != RailPieceKind.Switch) continue;

                RailNetwork.QueryAdjacent(cell, _neighbourScratch);
                for (int n = 0; n < _neighbourScratch.Count; n++)
                    if (_neighbourScratch[n] != null) _neighbourScratch[n].RebuildLinks(propagate: false);
            }

            return promoted;
        }

        private static int CountRailNeighbours(RailTrack cell)
        {
            RailNetwork.QueryAdjacent(cell, _neighbourScratch);
            int n = 0;
            for (int i = 0; i < _neighbourScratch.Count; i++)
                if (_neighbourScratch[i] != null && _neighbourScratch[i] != cell) n++;
            return n;
        }

        private static readonly List<RailTrack> _neighbourScratch = new(8);

        /// <summary>Junctions formed by the last commit, for the tool's readout.</summary>
        public static int LastJunctionsFormed { get; private set; }

        /// <summary>
        /// Decides whether one cell can actually be built, and says why not.
        ///
        /// This is what lets the ghost be honest per cell rather than per run: a route that
        /// clips one rock should show one red cell the player can nudge around, not a flat
        /// refusal of the whole line.
        /// </summary>
        private static bool EvaluateCell(Vector3 position, Vector3 up, out string problem)
        {
            problem = "";

            // ── Underwater ──
            // Track laid below the waterline is a line the player cannot use and cannot see.
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body != null)
            {
                float seaRadius = body.SeaRadius;
                if (seaRadius > 0.01f)
                {
                    float radius = Vector3.Distance(position, body.transform.position);
                    if (radius < seaRadius + WaterClearanceMetres)
                    {
                        problem = "underwater";
                        return false;
                    }
                }
            }

            // ── Buried, or occupied by something solid ──
            // Probed just above where the sleeper will sit. Anything solid there means the
            // route runs into a hillside or through a player's build.
            Vector3 probe = position + up * (RailRiseMetres + 0.5f);
            int count = Physics.OverlapSphereNonAlloc(probe, 0.42f, _overlapScratch,
                ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var c = _overlapScratch[i];
                if (c == null) continue;

                // Existing rail is fine - that is a crossing, and crossings become junctions.
                if (c.GetComponentInParent<RailTrack>() != null) continue;
                // Moving things are not obstructions to a plan; they will move.
                if (c.attachedRigidbody != null) continue;
                if (c.name.IndexOf("Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // A placed block is a real obstruction the player must clear first.
                if (c.GetComponentInParent<PlacedBlock>() != null)
                {
                    problem = "blocked by a placed block";
                    return false;
                }

                // Terrain this far above the formation means the line is inside a hillside.
                problem = "buried - the route runs into terrain";
                return false;
            }

            return true;
        }

        private static readonly Collider[] _overlapScratch = new Collider[16];

        /// <summary>How far above the waterline a formation must sit to count as dry.</summary>
        private const float WaterClearanceMetres = 0.6f;

        private static bool DropToGround(Vector3 point, Vector3 up, out Vector3 grounded)
        {
            // THE BUG (11.36.0): this probed 6 m up and 14 m down, and returned the input
            // point on a miss. The corridor solves on a FLAT PLANE through the start, so on
            // a curved planet - or any real slope - cells far from the start sit well above
            // or below the ground. The probe missed, those cells kept their plane position,
            // and the gradient check then measured the plane-vs-ground divergence as a
            // vertical cliff and refused the whole run. A dead straight drag on a hillside
            // reported "no placeable cells" on perfectly layable ground.
            //
            // The probe now starts far enough above and reaches far enough below to find
            // ground across the whole run, and a genuine miss is reported rather than
            // silently returning a point that is not on the ground.
            const float probeUp = 60f;
            const float probeLength = 220f;

            // Per-cell gravity, not the start point's. Over a long run on a small planet the
            // start's "up" is measurably wrong at the far end, which tilts the probe and can
            // walk it past the surface entirely.
            Vector3 localUp = VoxelEngine.Cosmos.GravityProvider.GetUp(point);
            if (localUp.sqrMagnitude < 1e-6f) localUp = up;

            // RaycastAll, not Raycast: the first thing hit may be the player, the ghost, a
            // train, or track already laid. Taking hit[0] blindly is how a probe "finds
            // ground" that is actually the player's own collider - or finds nothing usable
            // and reports the route as off-terrain.
            var hits = Physics.RaycastAll(point + localUp * probeUp, -localUp, probeLength,
                ~0, QueryTriggerInteraction.Ignore);

            if (hits.Length > 0)
            {
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                for (int i = 0; i < hits.Length; i++)
                {
                    var c = hits[i].collider;
                    if (c == null) continue;

                    // Skip anything that is not ground to build on.
                    if (c.GetComponentInParent<RailTrack>() != null) continue;
                    if (c.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null) continue;
                    if (c.attachedRigidbody != null) continue;          // players, vehicles, debris
                    if (c.name.IndexOf("Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    grounded = hits[i].point;
                    return true;
                }
            }

            grounded = point;
            return false;
        }

        /// <summary>Longest run a single drag may lay, in metres.</summary>
        public const float MaxRunMetres = 400f;

        /// <summary>
        /// Ballast block laid under every rail cell. Set by the tool before committing, so
        /// the corridor does not need to know how the player acquired it.
        /// </summary>
        public static BlockItem BallastBlock;

        /// <summary>
        /// How far below the draped ground the ballast slab's ORIGIN sits, in metres.
        ///
        /// The slab is 0.35 m tall and pivots at its own centre, so sinking it by half its
        /// height puts its top flush with the ground the corridor draped over. The rail
        /// then rises from that surface rather than floating above a gap.
        /// </summary>
        public const float BallastDropMetres = 0.175f;

        /// <summary>
        /// How far the rail is lifted above the draped ground, in metres.
        ///
        /// Real track sits on a raised bed. This is what makes a line read as a railway
        /// crossing terrain rather than a stripe painted on it, and it also stops sleepers
        /// clipping through ground that is slightly uneven between cells.
        /// </summary>
        public const float RailRiseMetres = 0.18f;

        /// <summary>Ballast cells laid by the last commit, for the tool's readout.</summary>
        public static int LastBallastPlaced { get; private set; }

        /// <summary>
        /// Commits a plan to the world, returning how many cells were actually placed.
        ///
        /// Skips any cell that already has track, so overlapping two runs extends a network
        /// instead of stacking duplicate rails inside each other.
        /// </summary>
        /// <summary>
        /// Commits a plan to the world. <paramref name="skipped"/> reports cells that were
        /// already occupied, so the caller can tell "nothing to do" apart from "it failed".
        /// </summary>
        public static int Commit(RailPlan plan, BlockItem trackBlock, float cellSize, out int skipped)
        {
            skipped = 0;
            if (plan == null || !plan.IsPlaceable) return 0;
            if (trackBlock == null || trackBlock.placedPrefab == null) return 0;

            int placed = 0;
            int ballastPlaced = 0;
            var laid = new List<RailTrack>(plan.cells.Count);

            for (int i = 0; i < plan.cells.Count; i++)
            {
                var cell = plan.cells[i];

                // Never stack track, but the test has to be much tighter than it was.
                //
                // THE BUG (11.35.0): this used a 0.45 m radius against cells spaced 1 m
                // apart, which sounds safe - but every cell is draped onto real ground, and
                // on a slope or a curve neighbouring cells pull well within half a metre of
                // each other. The run then rejected nearly all of its own cells, reported
                // "already had track", laid nothing, and charged nothing.
                //
                // A quarter of the cell spacing is the honest threshold: tight enough that
                // a genuinely duplicated cell is caught, loose enough that legitimately
                // adjacent draped cells are not.
                float dedupeRadius = Mathf.Max(0.05f, cellSize * 0.25f);
                if (RailNetwork.FindNearest(cell.position, dedupeRadius) != null)
                {
                    skipped++;
                    continue;
                }

                // Ballast first, so the sleeper sits ON the stone rather than inside it.
                // Real track is laid on a raised bed; without it the rails half-sink into
                // whatever ground the corridor draped over.
                if (BallastBlock != null && BallastBlock.placedPrefab != null)
                {
                    var bedPos = cell.position - (cell.rotation * Vector3.up) * BallastDropMetres;
                    var bed = Object.Instantiate(BallastBlock.placedPrefab, bedPos, cell.rotation);
                    bed.name = BallastBlock.displayName;

                    var bedBlock = bed.GetComponent<PlacedBlock>();
                    if (bedBlock == null) bedBlock = bed.AddComponent<PlacedBlock>();
                    bedBlock.Item = BallastBlock;
                    bedBlock.Hp = Mathf.Max(1, BallastBlock.blockHealth);

                    ballastPlaced++;
                }

                // Raise the rail onto the bed it now sits on.
                Vector3 railPos = cell.position + (cell.rotation * Vector3.up) * RailRiseMetres;
                var go = Object.Instantiate(trackBlock.placedPrefab, railPos, cell.rotation);
                go.name = trackBlock.displayName;

                var placedBlock = go.GetComponent<PlacedBlock>();
                if (placedBlock == null) placedBlock = go.AddComponent<PlacedBlock>();
                placedBlock.Item = trackBlock;
                placedBlock.Hp = Mathf.Max(1, trackBlock.blockHealth);

                var track = go.GetComponent<RailTrack>();
                if (track != null) laid.Add(track);

                placed++;
            }

            LastBallastPlaced = ballastPlaced;

            // Link the whole run AFTER every cell exists. Linking as we go would let each
            // cell fill its limited link budget with the one behind it before the one ahead
            // had even been created, leaving a line of disconnected pairs.
            for (int i = 0; i < laid.Count; i++)
                if (laid[i] != null) laid[i].RebuildLinks(propagate: true);

            LastJunctionsFormed = PromoteCrossingsToJunctions(laid);

            return placed;
        }
    }
}
