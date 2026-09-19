// Assets/Scripts/VoxelEngine/Building/RailTrack.cs
//
// THE RAIL TRACK — one cell of permanent way.
//
// A road lets a vehicle go anywhere slowly. A railway goes ONE way, very well: it
// is the bulk-haul answer to the belt's short-haul and the drone's point-to-point.
// That distinction is what keeps all three worth having, so this block is
// deliberately not "a road that trains use".
//
// WHAT IT IS
//   • A `PlacedBlock` like every other world block, so mining, damage, saves and the
//     inspection overlay already work on it. Nothing here re-implements that.
//   • A GRAPH NODE, not a surface. A road is queried by position ("what am I standing
//     on"); a rail is walked by topology ("what comes next"). Trains follow the graph,
//     which is why a train can run a route through unloaded chunks and a rover cannot.
//   • AUTO-CONNECTING to orthogonal neighbours, like the road's neighbour mask, so the
//     player lays cells and the line forms itself with no shape to pick.
//
// WHAT KEEPS IT FROM BEING FREE
//   • Gradient. Rail refuses a slope a road would happily drape over. A railway that
//     can climb anything is just an expensive road, and the survey refusal is what
//     makes players cut and fill and route around terrain.
//   • Degree. A cell may have at most two connections unless it is a switch, so the
//     network stays a set of lines rather than an undifferentiated mesh. That is what
//     makes pathfinding meaningful and signalling possible later.
//
// DELIBERATELY NOT HERE — train movement, schedules and signalling. This file is the
// permanent way and its graph, nothing else.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>
    /// What this piece of track is for. Appended, never reordered: a save stores this
    /// as an int, so every cell a player has already laid keeps its meaning.
    /// </summary>
    public enum RailPieceKind
    {
        /// <summary>Plain running line. At most two connections.</summary>
        Straight = 0,
        /// <summary>A junction: may hold three or four connections and pick between them.</summary>
        Switch = 1,
        /// <summary>A line end. Stops a train rather than letting it run off the railhead.</summary>
        Buffer = 2,
    }

    [DisallowMultipleComponent, RequireComponent(typeof(PlacedBlock))]
    public class RailTrack : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════
        //  AUTHORED TUNING
        // ════════════════════════════════════════════════════════════════

        [Header("Track")]
        public RailPieceKind pieceKind = RailPieceKind.Straight;

        [Tooltip("Edge length of one track cell in metres. Matches the BlockItem gridSize so " +
                 "rail lands on the same lattice as every other placed block.")]
        public float cellSize = 1f;

        [Header("Survey Limits")]
        [Tooltip("Greatest height change between two connected cells, in metres, that a train " +
                 "can still pull. Steeper than this and the cells refuse to connect: the " +
                 "player must cut, fill, or route around. This is the rule that stops a " +
                 "railway from being a road that ignores terrain.")]
        public float maxGradientMetres = 0.34f;

        [Tooltip("Speed multiplier applied to a train on this cell. Worn or rough track is " +
                 "slower; this is the hook a maintenance pass would drive.")]
        [Range(0.1f, 2f)] public float speedMultiplier = 1f;

        // ════════════════════════════════════════════════════════════════
        //  GRAPH
        // ════════════════════════════════════════════════════════════════

        /// <summary>Cells this one is directly connected to. Rebuilt on placement/removal.</summary>
        private readonly List<RailTrack> _links = new(4);
        public IReadOnlyList<RailTrack> Links => _links;

        /// <summary>How many connections this cell is allowed to hold.</summary>
        public int MaxLinks => pieceKind switch
        {
            RailPieceKind.Switch => 4,
            RailPieceKind.Buffer => 1,
            _ => 2,
        };

        /// <summary>True when the cell has room for another connection.</summary>
        public bool HasSpareLink => _links.Count < MaxLinks;

        /// <summary>A cell with one link is a railhead: a line ends here.</summary>
        public bool IsRailhead => _links.Count <= 1;

        /// <summary>A switch that is actually branching, rather than sitting in a plain line.</summary>
        public bool IsActiveJunction => pieceKind == RailPieceKind.Switch && _links.Count > 2;

        // ── Switch state ─────────────────────────────────────────────────────────

        /// <summary>
        /// Which link a switch currently routes onto, as an index into <see cref="Links"/>.
        /// Plain track ignores this. Stored as an index rather than a reference so it
        /// survives the link list being rebuilt when a neighbouring cell changes.
        /// </summary>
        [SerializeField] private int _switchSelection;

        /// <summary>
        /// True once a player has set the points by hand (console or cycle). Until then a
        /// junction routes STRAIGHT THROUGH: a crossing that appears because a line was laid
        /// across another must not silently turn trains sideways, which is the failure the
        /// auto-junction work was held back over for three releases.
        /// </summary>
        [SerializeField] private bool _pointsSetByPlayer;

        public int SwitchSelection => _switchSelection;
        public bool PointsSetByPlayer => _pointsSetByPlayer;

        /// <summary>Moves the points to the next available route. Wraps.</summary>
        public void CycleSwitch()
        {
            if (pieceKind != RailPieceKind.Switch || _links.Count == 0) return;
            _switchSelection = (_switchSelection + 1) % _links.Count;
            _pointsSetByPlayer = true;
        }

        public void SetSwitchSelection(int index)
        {
            if (_links.Count == 0) { _switchSelection = 0; return; }
            _switchSelection = Mathf.Clamp(index, 0, _links.Count - 1);
            _pointsSetByPlayer = true;
        }

        /// <summary>
        /// Restore path for saved junctions. Unlike <see cref="SetSwitchSelection"/> this does
        /// NOT mark the points as player-set unless the save says they were, so a junction the
        /// player never touched keeps routing straight through a reload.
        /// </summary>
        public void RestoreSwitchSelection(int index, bool pointsSetByPlayer)
        {
            if (_links.Count == 0) { _switchSelection = 0; }
            else _switchSelection = Mathf.Clamp(index, 0, _links.Count - 1);
            _pointsSetByPlayer = pointsSetByPlayer;
        }

        /// <summary>
        /// The cell a train leaves onto, given the cell it arrived from. On plain track this
        /// is simply "the other one"; on a switch it is whichever way the points are set -
        /// or, until a player sets them, the straightest continuation of the entry leg.
        /// Returns null at a buffer or a dead end.
        /// </summary>
        public RailTrack NextFrom(RailTrack arrivedFrom)
        {
            if (_links.Count == 0) return null;

            if (pieceKind == RailPieceKind.Switch)
            {
                if (!_pointsSetByPlayer) return StraightestOtherThan(arrivedFrom);

                var chosen = _links[Mathf.Clamp(_switchSelection, 0, _links.Count - 1)];
                // Never send a train straight back the way it came because the points happen
                // to be set at the entry leg; take the straightest other route instead.
                if (chosen == arrivedFrom) return StraightestOtherThan(arrivedFrom);
                return chosen;
            }

            for (int i = 0; i < _links.Count; i++)
                if (_links[i] != arrivedFrom) return _links[i];

            return null;
        }

        /// <summary>
        /// The link that continues the entry leg most nearly straight. This is what makes a
        /// fresh diamond crossing behave like two lines crossing rather than like a turn
        /// everything is forced into, and it is the safe fallback whenever the set route
        /// would bounce a train back the way it came.
        /// </summary>
        private RailTrack StraightestOtherThan(RailTrack arrivedFrom)
        {
            if (arrivedFrom == null)
            {
                for (int i = 0; i < _links.Count; i++)
                    if (_links[i] != null) return _links[i];
                return null;
            }

            Vector3 inDir = transform.position - arrivedFrom.transform.position;
            if (inDir.sqrMagnitude < 1e-8f) return null;
            inDir.Normalize();

            RailTrack best = null;
            float bestDot = -2f;
            for (int i = 0; i < _links.Count; i++)
            {
                var link = _links[i];
                if (link == null || link == arrivedFrom) continue;
                Vector3 outDir = link.transform.position - transform.position;
                if (outDir.sqrMagnitude < 1e-8f) continue;
                float dot = Vector3.Dot(outDir.normalized, inDir);
                if (dot > bestDot) { bestDot = dot; best = link; }
            }
            return best;
        }

        /// <summary>World position a train sits at when it occupies this cell.</summary>
        public Vector3 RailPosition => transform.position + transform.up * railHeight;

        [Tooltip("Height of the railhead above the cell origin, in metres.")]
        public float railHeight = 0.12f;

        // ════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ════════════════════════════════════════════════════════════════

        private static readonly List<RailTrack> _neighbourScratch = new(8);

        private void OnEnable()
        {
            RailNetwork.Register(this);
            RebuildLinks(true);
        }

        private void OnDisable()
        {
            // Tell the neighbours first: they must forget this cell before it leaves the
            // registry, or a removed cell would linger in their link lists.
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                var other = _links[i];
                if (other != null) other._links.Remove(this);
            }
            _links.Clear();
            RailNetwork.Unregister(this);
        }

        /// <summary>
        /// Rebuilds this cell's connections from the cells around it, and optionally asks
        /// those neighbours to rebuild too so the link is symmetric.
        /// </summary>
        public void RebuildLinks(bool propagate)
        {
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                var other = _links[i];
                if (other != null) other._links.Remove(this);
            }
            _links.Clear();

            RailNetwork.QueryAdjacent(this, _neighbourScratch);

            FillLinks();

            // SELF-PROMOTION (11.41.0). A cell that finds three or more arm directions around
            // itself IS a junction, whether it got there by a corridor commit, a hand-placed
            // cell completing a T, or a save reloading its graph from scratch. Promoting here
            // rather than only in the commit pass is what keeps a crossing a crossing after
            // a reload: the budget widens and the link fill runs again with four slots.
            if (pieceKind == RailPieceKind.Straight && ArmDirections(this) >= 3)
            {
                pieceKind = RailPieceKind.Switch;
                FillLinks();
            }

            _switchSelection = _links.Count == 0 ? 0 : Mathf.Clamp(_switchSelection, 0, _links.Count - 1);
            RefreshCurve();

            if (!propagate) return;

            // Copy before recursing: a neighbour's rebuild refills the SHARED query scratch,
            // and iterating a list the callee clears would skip or repeat neighbours.
            _propagateScratch.Clear();
            _propagateScratch.AddRange(_neighbourScratch);
            for (int i = 0; i < _propagateScratch.Count; i++)
            {
                var candidate = _propagateScratch[i];
                if (candidate == null || candidate == this) continue;
                // A FULL rebuild rather than a selection clamp: a neighbour that just gained
                // a junction arm needs its own link fill and its own promotion check, or the
                // new arm exists on one side of the edge only.
                candidate.RebuildLinks(propagate: false);
            }
            _propagateScratch.Clear();
        }

        private static readonly List<RailTrack> _propagateScratch = new(8);

        private void FillLinks()
        {
            for (int i = 0; i < _neighbourScratch.Count; i++)
            {
                var candidate = _neighbourScratch[i];
                if (candidate == null || candidate == this) continue;
                if (!HasSpareLink) break;
                if (!candidate.HasSpareLink) continue;
                if (!CanConnect(this, candidate)) continue;

                _links.Add(candidate);
                if (!candidate._links.Contains(this)) candidate._links.Add(this);
            }
        }

        /// <summary>
        /// How many distinct directions track leaves a cell in. Neighbours within 22.5
        /// degrees of each other are ONE arm: a junction is three arms, not three neighbours,
        /// which is what keeps a draped curve or a parallel line one metre to the side from
        /// reading as a branch.
        /// </summary>
        public static int ArmDirections(RailTrack cell)
        {
            if (cell == null) return 0;
            RailNetwork.QueryAdjacent(cell, _armScratchList);

            int arms = 0;
            for (int i = 0; i < _armScratchList.Count; i++)
            {
                var other = _armScratchList[i];
                if (other == null || other == cell) continue;
                Vector3 dir = other.transform.position - cell.transform.position;
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();

                bool merged = false;
                for (int a = 0; a < arms; a++)
                    if (Vector3.Dot(_armDirs[a], dir) > 0.92f) { merged = true; break; }
                if (merged) continue;
                if (arms < _armDirs.Length) _armDirs[arms++] = dir;
            }
            return arms;
        }

        private static readonly List<RailTrack> _armScratchList = new(8);
        private static readonly Vector3[] _armDirs = new Vector3[8];

        /// <summary>
        /// Whether two adjacent cells may be joined. The gradient rule lives here so the
        /// graph itself enforces it — a line that is too steep simply does not connect,
        /// rather than connecting and then failing mysteriously when a train tries it.
        /// </summary>
        public static bool CanConnect(RailTrack a, RailTrack b)
        {
            if (a == null || b == null || a == b) return false;

            Vector3 delta = b.transform.position - a.transform.position;
            Vector3 up = a.transform.up;
            float rise = Mathf.Abs(Vector3.Dot(delta, up));
            float limit = Mathf.Max(a.maxGradientMetres, b.maxGradientMetres);

            return rise <= limit;
        }

        /// <summary>Why these two cells will not join, for the survey readout. Null if they will.</summary>
        public static string ConnectionRefusal(RailTrack a, RailTrack b)
        {
            if (a == null || b == null) return "No track.";
            if (!a.HasSpareLink) return $"{a.pieceKind} is already fully connected.";
            if (!b.HasSpareLink) return $"{b.pieceKind} is already fully connected.";

            Vector3 delta = b.transform.position - a.transform.position;
            float rise = Mathf.Abs(Vector3.Dot(delta, a.transform.up));
            float limit = Mathf.Max(a.maxGradientMetres, b.maxGradientMetres);
            if (rise > limit)
                return $"Gradient too steep: {rise:0.00} m rise over one cell, limit {limit:0.00} m. " +
                       "Cut or fill the ground, or route around.";

            return null;
        }

        // ════════════════════════════════════════════════════════════════
        //  CURVE DEFORMATION — why a bend reads as a bend
        // ════════════════════════════════════════════════════════════════
        //
        // A track cell is a rigid box set: sleepers across, two rails along. On a straight
        // that tiles perfectly. On a curve it cannot: the outside of a bend is LONGER than
        // the inside, so rigid cells leave wedge gaps outboard and overlap inboard - the
        // dashed, kinked bend players kept reporting.
        //
        // Real track solves this two ways and so does this: sleepers are FANNED radially
        // (each one square to the tangent at its own station) and each rail is cut to the
        // arc length at its own offset from the centreline. Both fall out of one number -
        // the signed curvature of the line through this cell - so the deformation is
        // derived from the graph (the cells this one links to) rather than stored, which
        // means a save reloads and re-derives it for free and a junction re-link heals it.

        /// <summary>Snapshot of a child's authored local transform, taken once per instance.</summary>
        private struct ChildBase
        {
            public Transform transform;
            public Vector3 position;
            public Vector3 scale;
        }

        private List<ChildBase> _curveBases;
        private float _appliedCurvature = float.NaN;

        /// <summary>Signed curvature currently deforming this cell, rad/m. + bends to local +X.</summary>
        public float AppliedCurvature => float.IsNaN(_appliedCurvature) ? 0f : _appliedCurvature;

        /// <summary>Tightest radius the deformation will apply, in metres. Below this a bend is
        /// a kink the player should see refused rather than a curve to dress up.</summary>
        private const float MinDeformRadius = 1.2f;

        /// <summary>Beyond this radius the line is straight for every visual purpose.</summary>
        private const float MaxDeformRadius = 400f;

        /// <summary>
        /// Re-derives the bend from the link graph and deforms the children to match.
        /// Called at the end of every <see cref="RebuildLinks"/>, so placement, promotion
        /// and load all heal the shape through the same path.
        /// </summary>
        public void RefreshCurve()
        {
            float k = DeriveCurvature();
            if (k == _appliedCurvature) return;
            ApplyCurve(k);
        }

        /// <summary>
        /// Signed curvature of the through route at this cell, from the circumcircle of the
        /// two most opposite neighbours (the through pair on a junction, prev/next on plain
        /// line). One neighbour bends the cell half-way toward it, so a railhead curves into
        /// its line instead of standing stiff; none leaves the cell straight.
        /// </summary>
        private float DeriveCurvature()
        {
            if (_links.Count == 0) return 0f;

            Vector2 p = default, q = default;

            if (_links.Count == 1)
            {
                var only = _links[0];
                if (only == null) return 0f;
                Vector3 l = transform.InverseTransformPoint(only.transform.position);
                q = new Vector2(l.x, l.z);
                if (q.magnitude < 0.2f) return 0f;
                p = new Vector2(0f, -q.magnitude);   // virtual cell straight behind
            }
            else
            {
                // The through pair: the two neighbours pointing most nearly opposite ways.
                int bi = 0, bj = 1;
                float bestDot = 2f;
                for (int i = 0; i < _links.Count; i++)
                {
                    if (_links[i] == null) continue;
                    Vector3 di = (transform.InverseTransformPoint(_links[i].transform.position));
                    if (di.sqrMagnitude < 1e-6f) continue;
                    di.Normalize();
                    for (int j = i + 1; j < _links.Count; j++)
                    {
                        if (_links[j] == null) continue;
                        Vector3 dj = transform.InverseTransformPoint(_links[j].transform.position);
                        if (dj.sqrMagnitude < 1e-6f) continue;
                        dj.Normalize();
                        float dot = Vector3.Dot(di, dj);
                        if (dot < bestDot) { bestDot = dot; bi = i; bj = j; }
                    }
                }
                if (_links[bi] == null || _links[bj] == null) return 0f;
                Vector3 li = transform.InverseTransformPoint(_links[bi].transform.position);
                Vector3 lj = transform.InverseTransformPoint(_links[bj].transform.position);
                p = new Vector2(li.x, li.z);
                q = new Vector2(lj.x, lj.z);
            }

            // Circumcircle through the origin, p and q in the local (x, z) plane.
            float d = 2f * (p.x * q.y - p.y * q.x);
            if (Mathf.Abs(d) < 1e-4f) return 0f;

            float pSq = p.x * p.x + p.y * p.y;
            float qSq = q.x * q.x + q.y * q.y;
            float cx = (pSq * q.y - qSq * p.y) / d;
            float cy = (qSq * p.x - pSq * q.x) / d;
            float radius = Mathf.Sqrt(cx * cx + cy * cy);

            if (radius > MaxDeformRadius) return 0f;
            radius = Mathf.Max(radius, MinDeformRadius);

            // Centre on local +X means the line bends to +X; that is the sign the
            // deformation maths below is written against.
            return Mathf.Sign(cx) / radius;
        }

        /// <summary>
        /// Deforms the authored children onto an arc of signed curvature <paramref name="k"/>.
        ///
        /// Every child is carried along the arc by its own station (local Z) and offset
        /// across it by its own lateral (local X): sleepers fan to face the tangent, rails
        /// stretch to the arc length at their offset so consecutive cells meet end to end
        /// on the outside of a bend instead of gapping.
        /// </summary>
        private void ApplyCurve(float k)
        {
            if (_curveBases == null)
            {
                _curveBases = new List<ChildBase>(transform.childCount);
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    _curveBases.Add(new ChildBase
                    {
                        transform = child,
                        position = child.localPosition,
                        scale = child.localScale,
                    });
                }
            }

            _appliedCurvature = k;
            bool straight = Mathf.Abs(k) < 1e-5f;

            for (int i = 0; i < _curveBases.Count; i++)
            {
                var basis = _curveBases[i];
                var t = basis.transform;
                if (t == null) continue;

                if (straight)
                {
                    t.localPosition = basis.position;
                    t.localRotation = Quaternion.identity;
                    t.localScale = basis.scale;
                    continue;
                }

                float lateral = basis.position.x;
                float station = basis.position.z;
                float psi = station * k;                       // tangent angle at this station

                // Point on the arc at this station, then offset across by the lateral.
                float arcX = (1f - Mathf.Cos(psi)) / k;
                float arcZ = Mathf.Sin(psi) / k;
                t.localPosition = new Vector3(
                    arcX + lateral * Mathf.Cos(psi),
                    basis.position.y,
                    arcZ - lateral * Mathf.Sin(psi));
                t.localRotation = Quaternion.Euler(0f, psi * Mathf.Rad2Deg, 0f);

                // Arc length at this lateral against the centreline: (R - x) / R. Only the
                // member that runs ALONG the track stretches - sleepers keep their length.
                var scale = basis.scale;
                if (scale.z >= scale.x)
                {
                    float stretch = Mathf.Clamp(1f - lateral * k, 0.5f, 1.5f);
                    scale.z *= stretch;
                }
                t.localScale = scale;
            }
        }
    }
}
