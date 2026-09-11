// Assets/Scripts/VoxelEngine/Building/RoadCorridor.cs
//
// THE CORRIDOR GEOMETRY — where a clicked polyline becomes a carriageway that actually tiles.
//
// 9.42.0 laid each leg independently and stitched the joints with a mitred cross-section and a
// diagonal pocket cell. That worked for one lane. It could not work for three, because every lane
// of a leg was stepped by the same distance along the same centreline direction: on a bend the
// outside lane is longer than the centreline and the inside lane is shorter, so the outer lane
// opened gaps and the inner lane overlapped itself, and the single mitre chord at the joint could
// not describe either. This is the roadmap's "lanes currently fan off the centreline mitre".
//
// THE FIX IS ONE IDEA: a road is a set of cross-sections, not a set of legs.
//
//   1. The clicked waypoints become a CENTRELINE with a fillet arc at every turn, so a corner has
//      a radius instead of a chamfer. The radius is clamped so the inside lane can never collapse
//      (see `MinimumRadius`).
//   2. That centreline is cut into STATIONS at even arc-length spacing. A station is one line
//      straight across the whole carriageway.
//   3. Every cell is the quad between two consecutive stations, restricted to one lane's band.
//
// Because lanes share their stations, the tiling is exact in BOTH directions by construction
// rather than by tolerance: the cell ahead starts on the very chord this cell ends on, and the
// cell beside it ends on the very chord this cell ends on. There is no seam to fill, so the wedge
// and corner passes that used to paper over the seams are gone.
//
// The quads are handed to the cells as four explicit local corners. A cell on a straight produces
// the same square it always did; a cell on a curve is the trapezoid the curve actually wants.
//
// Everything here is pure maths on a tangent plane. It never touches the scene, never raycasts and
// never allocates once its buffers are warm, so it can be rebuilt every frame under the ghost.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>One cross-section of the carriageway: where the centreline is, which way the road
    /// runs there, and which way is across it.</summary>
    public struct RoadStation
    {
        public Vector3 centre;
        public Vector3 tangent;
        public Vector3 right;
    }

    /// <summary>One paving cell of the corridor, fully resolved: where it goes, how it is turned,
    /// and the four corners of its footprint in its own local space.</summary>
    public sealed class RoadCellFrame
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 sw, se, ne, nw;
        /// <summary>Set on the cells that sit either side of a filleted turn. Kept so the wear and
        /// crack passes can treat a curve differently from a straight if they ever need to.</summary>
        public bool curved;
    }

    /// <summary>
    /// Reusable scratch for one corridor solve. The planner owns one and rebuilds it every frame
    /// while a plan is open, so it must not allocate per rebuild.
    /// </summary>
    public sealed class RoadCorridorBuffers
    {
        public readonly List<Vector3> centreline = new List<Vector3>(128);
        public readonly List<float> arc = new List<float>(128);
        public readonly List<RoadStation> stations = new List<RoadStation>(128);
        public readonly List<RoadCellFrame> cells = new List<RoadCellFrame>(256);
        /// <summary>Cells along one lane. Every lane carries the same count, because lanes share
        /// their stations — that shared count is what makes the lateral seams exact.</summary>
        public int cellsPerLane;
        public float step;
        public float cornerRadius;
        /// <summary>Set when a turn is too tight to carry a fillet at this width — the legs are
        /// shorter than the radius the carriageway needs. The solve produces no cells; the paver
        /// reads this and tells the player to widen the turn or narrow the road.</summary>
        public bool cornerTooTight;

        private readonly List<Vector3> _arcTangent = new List<Vector3>(128);

        internal List<Vector3> ArcTangent => _arcTangent;

        internal void Clear()
        {
            centreline.Clear(); arc.Clear(); stations.Clear(); _arcTangent.Clear();
            cellsPerLane = 0; step = 0f;
        }

        // Cells are pooled by index so a per-frame rebuild reuses the frames it made last frame
        // instead of allocating a fresh list of them. `Next` hands out the rented one; `EndCells`
        // trims whatever the previous, longer plan left behind.
        private int _rented;

        internal void BeginCells() { _rented = 0; }

        internal RoadCellFrame Next()
        {
            RoadCellFrame frame;
            if (_rented < cells.Count) frame = cells[_rented];
            else { frame = new RoadCellFrame(); cells.Add(frame); }
            _rented++;
            return frame;
        }

        internal void EndCells()
        {
            if (cells.Count > _rented) cells.RemoveRange(_rented, cells.Count - _rented);
        }

        /// <summary>Drops every pooled cell without solving. Used when a route degenerates, so a
        /// caller iterating `cells` never sees the previous solve's leftovers.</summary>
        internal void DiscardCells()
        {
            _rented = 0;
            cells.Clear();
        }
    }

    public static class RoadCorridor
    {
        /// <summary>Degrees of turn per fillet sample. Fine enough that a 1 m cell on the tightest
        /// legal radius still gets at least two samples, coarse enough that a long highway does not
        /// build a thousand station points.</summary>
        private const float ARC_SAMPLE_DEGREES = 7.5f;

        /// <summary>
        /// The smallest corner radius a carriageway of this width can carry, in metres.
        ///
        /// Not a taste decision, and not the first guess either. The inside lane of a bend has a turn
        /// radius of `radius - halfSpan * cell`, so as the radius shrinks the inside lane's cells get
        /// shorter while staying a full cell wide — they become slivers, then fold concave. The first
        /// version of this rule used `(halfSpan + 1) * cell`, which keeps the inside lane's radius
        /// above one cell but still let a ten-wide strip emit cells whose shortest edge was 9% of
        /// their longest.
        ///
        /// So the constant was measured instead: sweeping radius per width and taking the point where
        /// no cell falls below a 0.35 edge ratio gives 1.25, 1.75, 2.50, 3.25, 4.00, 4.75, 6.25, 7.75
        /// cells for widths 1, 2, 3, 4, 5, 6, 8, 10 — a straight line in halfSpan, fitted as
        /// `cell * (1.25 + 1.5 * halfSpan)`. A road cannot turn inside its own width, which is true
        /// of real carriageways and was not true of this one before.
        /// </summary>
        public static float MinimumRadius(int width, float cell)
        {
            float halfSpan = Mathf.Max(0, width - 1) * 0.5f;
            return (1.25f + 1.5f * halfSpan) * Mathf.Max(0.05f, cell);
        }

        /// <summary>
        /// Solves the corridor. Waypoints are projected onto the tangent plane through
        /// <paramref name="waypoints"/>[0] with normal <paramref name="up"/>, which is the same
        /// assumption the rest of the paver makes: a corridor is short against a planet, and every
        /// cell is re-dropped onto the real ground by `PlaceCell` afterwards anyway.
        /// </summary>
        public static void Build(RoadCorridorBuffers buf, List<Vector3> waypoints, Vector3 up,
                                 int width, float cell, float requestedRadius)
        {
            if (buf == null) return;
            buf.Clear();
            if (waypoints == null || waypoints.Count == 0) { buf.DiscardCells(); return; }

            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();
            cell = Mathf.Max(0.05f, cell);
            width = Mathf.Max(1, width);

            buf.cornerRadius = Mathf.Max(requestedRadius, MinimumRadius(width, cell));
            buf.cornerTooTight = false;

            BuildCentreline(buf, waypoints, up, buf.cornerRadius, MinimumRadius(width, cell));
            // A corner cannot be built at all when the legs are too short to carry the radius this
            // width needs. Emitting it anyway produces zero-area slivers and concave cells, so the
            // solve refuses instead and the paver says why.
            if (buf.cornerTooTight) { buf.DiscardCells(); return; }
            // A degenerate route (every waypoint on top of the last) solves to nothing. Drop the
            // pooled cells too, or the emitter would lay whatever the previous solve left behind.
            if (buf.centreline.Count < 2) { buf.DiscardCells(); return; }

            ArcParameterise(buf);

            float total = buf.arc[buf.arc.Count - 1];
            // Cells are stepped by EXACTLY one cell, never stretched to land on the far waypoint.
            // Stretching (`step = total / n`) makes every slab a slightly different size, which
            // shows up as a seam where a new strip meets an old one that was laid on the standard
            // lattice — precisely the connect-and-extend case. `Sample` clamps at the corridor end,
            // so an overshoot produces one short closure cell rather than a row of long ones.
            int n = Mathf.Max(1, Mathf.RoundToInt(total / cell));
            buf.step = cell;
            buf.cellsPerLane = n;

            BuildStations(buf, up, n);
            BuildCells(buf, up, width, cell);
        }

        // ════════════════════════════════════════════════════════════════
        //  1. CENTRELINE — waypoints with a fillet at every turn
        // ════════════════════════════════════════════════════════════════

        private static void BuildCentreline(RoadCorridorBuffers buf, List<Vector3> waypoints,
                                            Vector3 up, float radius, float minRadius)
        {
            buf.centreline.Add(waypoints[0]);

            for (int i = 1; i + 1 < waypoints.Count; i++)
            {
                Vector3 a = waypoints[i - 1], p = waypoints[i], b = waypoints[i + 1];
                Vector3 d1 = Flatten(p - a, up);
                Vector3 d2 = Flatten(b - p, up);
                if (d1.sqrMagnitude < 1e-8f || d2.sqrMagnitude < 1e-8f) continue;
                d1.Normalize(); d2.Normalize();

                float turn = Vector3.Angle(d1, d2);
                if (turn < 0.5f) { buf.centreline.Add(p); continue; }   // straight through

                // A fillet of radius r is tangent to both legs at distance r*tan(turn/2) from the
                // vertex. That distance has to fit inside both legs, or the arc would swallow the
                // next corner — so the radius is clamped to 45% of the shorter leg.
                float legIn = (p - a).magnitude, legOut = (b - p).magnitude;
                float r = radius;
                // Half the shorter leg is as much arc as a corner may take; more and the fillet
                // starts eating the leg it is supposed to join.
                float maxTangent = 0.5f * Mathf.Min(legIn, legOut);
                float maxR = maxTangent / Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad);
                if (r > maxR) r = maxR;
                // The leg clamp is allowed to shrink the radius, but never past the point where the
                // inside lane of the carriageway collapses. Past that there is no corner to build:
                // the turn needs more room or the road needs to be narrower.
                if (r < minRadius - 1e-3f) { buf.cornerTooTight = true; return; }
                if (r < 1e-4f) { buf.centreline.Add(p); continue; }

                AppendFillet(buf, p, d1, d2, turn, r, up);
            }

            buf.centreline.Add(waypoints[waypoints.Count - 1]);
        }

        private static void AppendFillet(RoadCorridorBuffers buf, Vector3 vertex, Vector3 d1, Vector3 d2,
                                         float turn, float radius, Vector3 up)
        {
            float t = radius * Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad);
            Vector3 entry = vertex - d1 * t;
            Vector3 exit = vertex + d2 * t;

            Vector3 bisector = d2 - d1;
            if (bisector.sqrMagnitude < 1e-8f) { buf.centreline.Add(vertex); return; }
            bisector.Normalize();
            Vector3 centre = vertex + bisector * (radius / Mathf.Cos(turn * 0.5f * Mathf.Deg2Rad));

            // Which way the arc sweeps: the sign of the turn about the plane normal.
            float signed = Vector3.SignedAngle(d1, d2, up);
            float sweep = Mathf.Sign(signed) * turn;

            int samples = Mathf.Max(2, Mathf.RoundToInt(Mathf.Abs(sweep) / ARC_SAMPLE_DEGREES));
            Vector3 arm = entry - centre;

            buf.centreline.Add(entry);
            for (int s = 1; s <= samples; s++)
            {
                float angle = sweep * (s / (float)samples);
                buf.centreline.Add(centre + Quaternion.AngleAxis(angle, up) * arm);
            }
            // The last sample IS the exit tangent point; overwrite it rather than append a
            // duplicate, because a zero-length segment would poison the arc parameterisation.
            buf.centreline[buf.centreline.Count - 1] = exit;
        }

        // ════════════════════════════════════════════════════════════════
        //  2. ARC PARAMETERISATION
        // ════════════════════════════════════════════════════════════════

        private static void ArcParameterise(RoadCorridorBuffers buf)
        {
            var pts = buf.centreline;
            var tan = buf.ArcTangent;
            buf.arc.Clear(); tan.Clear();

            buf.arc.Add(0f);
            for (int i = 1; i < pts.Count; i++)
                buf.arc.Add(buf.arc[i - 1] + (pts[i] - pts[i - 1]).magnitude);

            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d;
                if (i == 0) d = pts[1] - pts[0];
                else if (i == pts.Count - 1) d = pts[i] - pts[i - 1];
                else d = pts[i + 1] - pts[i - 1];       // central difference: smooths the fillet joints
                tan.Add(d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.forward);
            }
        }

        /// <summary>Point and tangent at an arc-length position, by binary search then lerp.</summary>
        private static void Sample(RoadCorridorBuffers buf, float at, out Vector3 point, out Vector3 tangent)
        {
            var pts = buf.centreline;
            float total = buf.arc[buf.arc.Count - 1];
            if (at <= 0f) { point = pts[0]; tangent = buf.ArcTangent[0]; return; }
            if (at >= total) { point = pts[pts.Count - 1]; tangent = buf.ArcTangent[pts.Count - 1]; return; }

            int lo = 0, hi = buf.arc.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (buf.arc[mid] <= at) lo = mid; else hi = mid;
            }
            float seg = buf.arc[hi] - buf.arc[lo];
            float f = seg < 1e-6f ? 0f : (at - buf.arc[lo]) / seg;

            point = Vector3.Lerp(pts[lo], pts[hi], f);
            Vector3 t = Vector3.Lerp(buf.ArcTangent[lo], buf.ArcTangent[hi], f);
            tangent = t.sqrMagnitude > 1e-10f ? t.normalized : buf.ArcTangent[lo];
        }

        private static void BuildStations(RoadCorridorBuffers buf, Vector3 up, int n)
        {
            buf.stations.Clear();
            for (int k = 0; k <= n; k++)
            {
                Sample(buf, k * buf.step, out Vector3 c, out Vector3 t);
                Vector3 tangent = Flatten(t, up);
                if (tangent.sqrMagnitude < 1e-8f) tangent = buf.stations.Count > 0
                    ? buf.stations[buf.stations.Count - 1].tangent : Vector3.forward;
                tangent.Normalize();
                // Cross(up, tangent) is the right of travel in Unity's basis, which is what
                // Quaternion.LookRotation(tangent, up) puts on local +X. Matching that sign is what
                // makes the explicit corners land on the same side the mesh expects.
                buf.stations.Add(new RoadStation { centre = c, tangent = tangent, right = Vector3.Cross(up, tangent).normalized });
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  3. CELLS — one quad per lane per station pair
        // ════════════════════════════════════════════════════════════════

        private static void BuildCells(RoadCorridorBuffers buf, Vector3 up, int width, float cell)
        {
            buf.BeginCells();
            float halfSpan = (width - 1) * 0.5f;

            for (int w = 0; w < width; w++)
            {
                float inner = (w - halfSpan - 0.5f) * cell;
                float outer = (w - halfSpan + 0.5f) * cell;

                for (int k = 0; k < buf.cellsPerLane; k++)
                {
                    RoadStation s0 = buf.stations[k], s1 = buf.stations[k + 1];

                    Vector3 a0 = s0.centre + s0.right * inner;
                    Vector3 b0 = s0.centre + s0.right * outer;
                    Vector3 b1 = s1.centre + s1.right * outer;
                    Vector3 a1 = s1.centre + s1.right * inner;

                    var frame = buf.Next();
                    frame.position = (a0 + b0 + b1 + a1) * 0.25f;

                    Vector3 axis = Flatten((s0.tangent + s1.tangent) * 0.5f, up);
                    if (axis.sqrMagnitude < 1e-8f) axis = s0.tangent;
                    axis.Normalize();
                    frame.rotation = Quaternion.LookRotation(axis, up);

                    Quaternion toLocal = Quaternion.Inverse(frame.rotation);
                    frame.sw = toLocal * (a0 - frame.position);
                    frame.se = toLocal * (b0 - frame.position);
                    frame.ne = toLocal * (b1 - frame.position);
                    frame.nw = toLocal * (a1 - frame.position);
                    frame.curved = Vector3.Angle(s0.tangent, s1.tangent) > 0.25f;
                }
            }
            buf.EndCells();
        }

        private static Vector3 Flatten(Vector3 v, Vector3 up) => v - up * Vector3.Dot(v, up);

        /// <summary>Convenience index: the cell of <paramref name="lane"/> at
        /// <paramref name="index"/>, or null when the corridor did not solve.</summary>
        public static RoadCellFrame CellAt(RoadCorridorBuffers buf, int lane, int index)
        {
            if (buf == null || buf.cellsPerLane <= 0) return null;
            int i = lane * buf.cellsPerLane + index;
            return i >= 0 && i < buf.cells.Count ? buf.cells[i] : null;
        }
    }
}
