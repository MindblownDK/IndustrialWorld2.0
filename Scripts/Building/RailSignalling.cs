// Assets/Scripts/VoxelEngine/Building/RailSignalling.cs
//
// TRAIN SYSTEM V2, PHASE 4 — block occupancy, so trains stop instead of colliding.
//
// THE PROBLEM
// Two trains on one line currently drive straight through each other. Every other part
// of the rework is now in place - grids on rails, consists, drag-laid corridors - and
// the thing stopping a player running more than one train is that a second train is a
// guaranteed overlap, not a scheduling problem.
//
// WHAT A SIGNAL BLOCK IS
// The real-world answer, and the right one here: the line is divided into SECTIONS, and
// only one train may occupy a section at a time. A train approaching an occupied section
// stops at its boundary and waits. That single rule prevents every rear-end and head-on
// collision without any train needing to know about any other train.
//
// WHY SECTIONS ARE DERIVED, NOT PLACED
// The obvious design is a signal block the player places, which then owns the stretch of
// track after it. That has a failure mode: a line with no signals is one giant section,
// so the very first thing a new player builds cannot run two trains, and the feature is
// invisible until they learn it exists.
//
// Instead a section is DERIVED from the graph: the track between two junctions is one
// section, because a junction is exactly where routes can diverge and therefore exactly
// where a train's path becomes uncertain. That means signalling works the moment there
// is track, with nothing to place, and it automatically gets finer as the network grows
// more complex - which is precisely when it is needed.
//
// This mirrors the choice already made three times in this codebase: deep ore nodes,
// hazard zones and asteroid placement are all derived from existing state rather than
// spawned and stored. Derived state cannot desynchronise from the thing it describes.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>
    /// Tracks which train owns which stretch of line, and answers the only question a
    /// train needs to ask: "may I enter the track ahead of me?"
    /// </summary>
    public static class RailSignalling
    {
        /// <summary>
        /// Section id -> the claimant currently holding it. The claimant is an arbitrary
        /// object (the head bogie), compared by reference only.
        /// </summary>
        private static readonly Dictionary<int, object> _claims = new(64);

        /// <summary>Claimant -> every section it currently holds, so releasing is exact.</summary>
        private static readonly Dictionary<object, HashSet<int>> _held = new(16);

        /// <summary>Scratch reused by the section walk so a lookahead allocates nothing.</summary>
        private static readonly HashSet<RailTrack> _visited = new();
        private static readonly Queue<RailTrack> _frontier = new();

        /// <summary>
        /// How far ahead a train looks, in cells. Must exceed the longest braking distance
        /// or a train discovers an occupied section too late to stop before entering it.
        /// </summary>
        public const int LookaheadCells = 12;

        /// <summary>Largest section the walk will trace before giving up and allowing entry.</summary>
        private const int MaxSectionCells = 512;

        // ════════════════════════════════════════════════════════════════
        //  SECTION IDENTITY
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// The section a cell belongs to.
        ///
        /// A section runs between junctions, so the id is derived by walking outward from
        /// the cell until a junction or railhead is reached in both directions, and taking
        /// the lowest cell hash found. Lowest-hash is used rather than "first found"
        /// because two trains approaching the same section from opposite ends must compute
        /// the SAME id, or they would each think they owned a different section and both
        /// enter it.
        /// </summary>
        public static int SectionIdOf(RailTrack track)
        {
            if (track == null) return 0;

            // A junction is its own single-cell section. That is deliberate: it is the one
            // cell where two routes physically share metal, so it must be exclusive even
            // when the lines either side of it are clear.
            if (track.IsActiveJunction) return track.GetHashCode();

            _visited.Clear();
            _frontier.Clear();
            _frontier.Enqueue(track);
            _visited.Add(track);

            int lowest = track.GetHashCode();

            while (_frontier.Count > 0)
            {
                if (_visited.Count > MaxSectionCells) break;

                var cell = _frontier.Dequeue();
                int hash = cell.GetHashCode();
                if (hash < lowest) lowest = hash;

                var links = cell.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    var next = links[i];
                    if (next == null) continue;

                    // Stop AT a junction without crossing it: the junction is a section of
                    // its own, and walking through would merge every line that meets there
                    // into one enormous section.
                    if (next.IsActiveJunction) continue;
                    if (!_visited.Add(next)) continue;
                    _frontier.Enqueue(next);
                }
            }

            return lowest;
        }

        // ════════════════════════════════════════════════════════════════
        //  CLAIMS
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// True when <paramref name="claimant"/> may occupy the section containing
        /// <paramref name="track"/> - either because it is free, or because this claimant
        /// already holds it.
        /// </summary>
        public static bool CanEnter(RailTrack track, object claimant)
        {
            if (track == null || claimant == null) return true;

            int section = SectionIdOf(track);
            if (!_claims.TryGetValue(section, out var owner)) return true;

            // A dead claimant must never hold a section forever. Unity objects compare
            // false against null when destroyed, so this is checked explicitly.
            if (owner == null || (owner is Object unityOwner && unityOwner == null))
            {
                Release(section);
                return true;
            }

            return ReferenceEquals(owner, claimant);
        }

        /// <summary>
        /// Claims the section containing a cell. Returns false when another train holds it,
        /// in which case the caller must stop rather than proceed.
        /// </summary>
        public static bool TryClaim(RailTrack track, object claimant)
        {
            if (track == null || claimant == null) return true;
            if (!CanEnter(track, claimant)) return false;

            int section = SectionIdOf(track);
            _claims[section] = claimant;

            if (!_held.TryGetValue(claimant, out var set))
            {
                set = new HashSet<int>();
                _held[claimant] = set;
            }
            set.Add(section);
            return true;
        }

        /// <summary>
        /// Releases every section a claimant holds EXCEPT the ones it still occupies.
        ///
        /// Called as a train moves, so the line behind it reopens. Holding everything ever
        /// entered would mean one lap around a loop deadlocks the entire network against
        /// its own train.
        /// </summary>
        public static void ReleaseAllExcept(object claimant, int keepSectionA, int keepSectionB)
        {
            if (claimant == null) return;
            if (!_held.TryGetValue(claimant, out var set) || set.Count == 0) return;

            _releaseScratch.Clear();
            foreach (int section in set)
            {
                if (section == keepSectionA || section == keepSectionB) continue;
                _releaseScratch.Add(section);
            }

            for (int i = 0; i < _releaseScratch.Count; i++)
            {
                int section = _releaseScratch[i];
                set.Remove(section);
                if (_claims.TryGetValue(section, out var owner) && ReferenceEquals(owner, claimant))
                    _claims.Remove(section);
            }
        }

        private static readonly List<int> _releaseScratch = new(16);

        /// <summary>Drops every claim a train holds. Called when it stops, derails or dies.</summary>
        public static void ReleaseAll(object claimant)
        {
            if (claimant == null) return;
            if (!_held.TryGetValue(claimant, out var set)) return;

            foreach (int section in set)
                if (_claims.TryGetValue(section, out var owner) && ReferenceEquals(owner, claimant))
                    _claims.Remove(section);

            set.Clear();
            _held.Remove(claimant);
        }

        private static void Release(int section) => _claims.Remove(section);

        /// <summary>Clears every claim. Used on world teardown.</summary>
        public static void Clear()
        {
            _claims.Clear();
            _held.Clear();
        }

        // ════════════════════════════════════════════════════════════════
        //  LOOKAHEAD
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Walks the route ahead and reports the first cell the train may not enter.
        ///
        /// Returns null when the way is clear. The train stops when this returns non-null,
        /// which happens at the boundary of the occupied section rather than inside it -
        /// the difference between waiting for a platform and parking on top of another
        /// train.
        /// </summary>
        public static RailTrack FindBlockingCell(RailTrack from, RailTrack previous,
            object claimant, int cells)
        {
            if (from == null || claimant == null) return null;

            var cell = from;
            var behind = previous;

            for (int i = 0; i < cells; i++)
            {
                var next = cell.NextFrom(behind);
                if (next == null) return null;          // railhead: the bogie handles stopping

                if (!CanEnter(next, claimant)) return next;

                behind = cell;
                cell = next;
            }
            return null;
        }

        /// <summary>Number of sections currently claimed, for diagnostics.</summary>
        public static int ActiveClaims => _claims.Count;
    }
}
