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

        public int SwitchSelection => _switchSelection;

        /// <summary>Moves the points to the next available route. Wraps.</summary>
        public void CycleSwitch()
        {
            if (pieceKind != RailPieceKind.Switch || _links.Count == 0) return;
            _switchSelection = (_switchSelection + 1) % _links.Count;
        }

        public void SetSwitchSelection(int index)
        {
            if (_links.Count == 0) { _switchSelection = 0; return; }
            _switchSelection = Mathf.Clamp(index, 0, _links.Count - 1);
        }

        /// <summary>
        /// The cell a train leaves onto, given the cell it arrived from. On plain track this
        /// is simply "the other one"; on a switch it is whichever way the points are set.
        /// Returns null at a buffer or a dead end.
        /// </summary>
        public RailTrack NextFrom(RailTrack arrivedFrom)
        {
            if (_links.Count == 0) return null;

            if (pieceKind == RailPieceKind.Switch)
            {
                var chosen = _links[Mathf.Clamp(_switchSelection, 0, _links.Count - 1)];
                // Never send a train straight back the way it came because the points happen
                // to be set at the entry leg; take any other route instead.
                if (chosen == arrivedFrom)
                {
                    for (int i = 0; i < _links.Count; i++)
                        if (_links[i] != arrivedFrom) return _links[i];
                    return null;
                }
                return chosen;
            }

            for (int i = 0; i < _links.Count; i++)
                if (_links[i] != arrivedFrom) return _links[i];

            return null;
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

            _switchSelection = _links.Count == 0 ? 0 : Mathf.Clamp(_switchSelection, 0, _links.Count - 1);

            if (!propagate) return;
            for (int i = 0; i < _neighbourScratch.Count; i++)
            {
                var candidate = _neighbourScratch[i];
                if (candidate != null && candidate != this) candidate.ClampSelection();
            }
        }

        private void ClampSelection()
        {
            _switchSelection = _links.Count == 0 ? 0 : Mathf.Clamp(_switchSelection, 0, _links.Count - 1);
        }

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
    }
}
