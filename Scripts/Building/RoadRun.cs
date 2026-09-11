// Assets/Scripts/VoxelEngine/Building/RoadRun.cs
//
// THE ROAD RUN — one wear pool per connected strip of asphalt.
//
// Wear is tracked PER RUN, not per block (settled for this round): a connected strip shares
// one wear number, so a busy corridor wears as one surface and a repair is one action rather
// than a hundred clicks. Per-run is also the cheap side of the simulation — the accrual cost
// scales with the number of runs, not the number of cells, and a 400-cell highway is one run.
//
// The trade is honest and stated where it binds: a half-repaired road cannot show one good
// half and one bad half. It shows one number. If the team later wants the granular read, the
// seam is this class — `AsphaltRoad` already talks to the run through a single property.
//
// THIS IS NOT THE ROAD NETWORK OBJECT. The roadmap holds the network back on purpose: no
// name, no connected-run trace, no traffic readout, no player-facing register. A run is pure
// bookkeeping for wear — it exists so that pavement has a service life and so that repairing
// it is one gesture. It arrives with the routing it feeds, not before.
//
// Merge takes the WORSE wear of the two runs. You cannot un-wear a road by connecting it to
// a fresh one, and a player who discovers otherwise has found an exploit rather than a feature.
// Split gives every piece the parent's wear, for the same reason: pavement does not heal
// because somebody lifted a cell out of the middle of it.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    public class RoadRun
    {
        private readonly List<AsphaltRoad> _blocks = new List<AsphaltRoad>(16);

        /// <summary>0 = freshly laid, 1 = worn out and broken up. Drives every road bonus.</summary>
        public float Wear01 { get; private set; }

        /// <summary>Weighted metres travelled across this run since it was last repaired.</summary>
        public float TrafficMetres { get; private set; }

        public int BlockCount => _blocks.Count;
        public IReadOnlyList<AsphaltRoad> Blocks => _blocks;

        private float _pavedArea;
        private bool  _areaDirty = true;

        /// <summary>Total paved area of the run in square metres. Wear is billed against this, so a
        /// run of wide cells and a run of small cells covering the same ground wear at the same
        /// rate. Cached and invalidated on mutation rather than summed per call: `AddTraffic` runs
        /// once per wheel per physics tick, and a highway can be hundreds of cells long.</summary>
        public float PavedArea
        {
            get
            {
                if (_areaDirty)
                {
                    float total = 0f;
                    for (int i = 0; i < _blocks.Count; i++)
                    {
                        var block = _blocks[i];
                        if (block == null) continue;
                        float cell = Mathf.Max(0.25f, block.cellSize);
                        total += cell * cell;
                    }
                    _pavedArea = Mathf.Max(1f, total);
                    _areaDirty = false;
                }
                return _pavedArea;
            }
        }

        private void InvalidateArea() => _areaDirty = true;

        /// <summary>Membership test. Exposed here rather than left to the caller because
        /// `Blocks` is an `IReadOnlyList<T>`, whose `Contains` is a LINQ extension — a scan
        /// over the backing list is the same work with no `System.Linq` dependency and no
        /// enumerator allocation on a path that runs whenever a cell re-shapes.</summary>
        public bool Contains(AsphaltRoad block)
        {
            if (block == null) return false;
            for (int i = 0; i < _blocks.Count; i++)
                if (ReferenceEquals(_blocks[i], block)) return true;
            return false;
        }

        /// <summary>Metres of traffic per cell of road before the run wears out completely.
        /// Raised by the run's own length: a long road spreads the same traffic over more
        /// pavement, which is the whole reason wear is per run and not per block.</summary>
        /// <summary>Loaded metres of traffic one square metre of pavement survives before the run
        /// reads as broken. Per AREA rather than per cell: a 4 m wide cell carries sixteen times the
        /// pavement of a 1 m cell, and billing both the same would make the wide road — the one the
        /// player built for heavy traffic — wear out sixteen times faster per square metre.</summary>
        public const float WEAR_METRES_PER_SQUARE_METRE = 22000f;

        public RoadRun(float wear01 = 0f)
        {
            Wear01 = Mathf.Clamp01(wear01);
        }

        // ════════════════════════════════════════════════════════════════
        //  WHAT WEAR IS WORTH — the single source of truth for both consumers
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// On-foot speed multiplier over this run. 1.30 on fresh asphalt, back to 1.00 by
        /// heavy wear, and a real penalty once the surface has broken up — a potholed road
        /// is worse than the dirt it replaced, which is what makes maintenance a decision
        /// rather than a chore with no downside.
        /// </summary>
        public float WalkSpeedMultiplier
        {
            get
            {
                if (Wear01 >= 1f) return 0.88f;
                if (Wear01 <= 0.35f) return 1.30f;
                if (Wear01 <= 0.70f) return Mathf.Lerp(1.30f, 1.00f, Mathf.InverseLerp(0.35f, 0.70f, Wear01));
                return Mathf.Lerp(1.00f, 0.88f, Mathf.InverseLerp(0.70f, 1f, Wear01));
            }
        }

        /// <summary>
        /// Drive-traction multiplier for a wheel on this run. Fresh asphalt grips; a broken
        /// run spins. Applied to the wheel's drive force, so the bonus is felt as acceleration
        /// and hill-climbing rather than as a flat top-speed cheat.
        /// </summary>
        public float TractionMultiplier
        {
            get
            {
                if (Wear01 >= 1f) return 0.85f;
                if (Wear01 <= 0.35f) return 1.25f;
                if (Wear01 <= 0.70f) return Mathf.Lerp(1.25f, 1.00f, Mathf.InverseLerp(0.35f, 0.70f, Wear01));
                return Mathf.Lerp(1.00f, 0.85f, Mathf.InverseLerp(0.70f, 1f, Wear01));
            }
        }

        /// <summary>
        /// Lateral-grip multiplier for a wheel on this run. A paved surface stops a rig
        /// sliding out on a corner and holds it on a slope — the "better traction on slopes"
        /// the design asks for, expressed through the friction the wheel already applies.
        /// </summary>
        public float GripMultiplier
        {
            get
            {
                if (Wear01 >= 1f) return 0.92f;
                if (Wear01 <= 0.35f) return 1.40f;
                if (Wear01 <= 0.70f) return Mathf.Lerp(1.40f, 1.00f, Mathf.InverseLerp(0.35f, 0.70f, Wear01));
                return Mathf.Lerp(1.00f, 0.92f, Mathf.InverseLerp(0.70f, 1f, Wear01));
            }
        }

        /// <summary>0..1 damage-style read used by the pothole decals and the inspection card.</summary>
        public float Roughness01 => Mathf.Clamp01(Mathf.InverseLerp(0.55f, 1f, Wear01));

        /// <summary>One-line condition word for the inspection overlay.</summary>
        public string ConditionLabel
        {
            get
            {
                if (Wear01 >= 1f)    return "BROKEN UP";
                if (Wear01 >= 0.70f) return "POTHOLED";
                if (Wear01 >= 0.35f) return "WORN";
                return "GOOD";
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  TRAFFIC & WEAR
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Bills the run for distance travelled over it. <paramref name="loadFactor"/> is the
        /// agent's weight in the wear sense: feet are light, a loaded rig is not. Called by
        /// movement systems with the distance they actually covered this frame, so a parked
        /// vehicle wears nothing and a shuttle on a paved corridor wears it properly.
        /// </summary>
        public void AddTraffic(float metres, float loadFactor)
        {
            if (metres <= 0f || loadFactor <= 0f) return;
            TrafficMetres += metres * loadFactor;

            float wearDelta = (metres * loadFactor) / (PavedArea * WEAR_METRES_PER_SQUARE_METRE);
            float previous = Wear01;
            Wear01 = Mathf.Clamp01(Wear01 + wearDelta);

            // Only push a visual refresh when the condition band actually changed, so a
            // highway under constant traffic is not rebuilding its pothole meshes per frame.
            if (BandOf(previous) != BandOf(Wear01)) NotifyConditionChanged();
        }

        /// <summary>Repairs the whole run to fresh. Material cost is the caller's business.</summary>
        public void Repair()
        {
            Wear01 = 0f;
            TrafficMetres = 0f;
            NotifyConditionChanged();
        }

        /// <summary>Restores a wear value read from a save without resetting the traffic ledger.</summary>
        public void RestoreWear(float wear01)
        {
            float previous = Wear01;
            Wear01 = Mathf.Clamp01(wear01);
            if (BandOf(previous) != BandOf(Wear01)) NotifyConditionChanged();
        }

        private static int BandOf(float wear)
        {
            if (wear >= 1f)    return 3;
            if (wear >= 0.70f) return 2;
            if (wear >= 0.35f) return 1;
            return 0;
        }

        private void NotifyConditionChanged()
        {
            for (int i = 0; i < _blocks.Count; i++)
                _blocks[i]?.ApplyWearVisuals();
        }

        // ════════════════════════════════════════════════════════════════
        //  MEMBERSHIP
        // ════════════════════════════════════════════════════════════════

        /// <summary>The surface this run is paved with, read from its cells.</summary>
        public RoadSurfaceKind Kind
        {
            get
            {
                for (int i = 0; i < _blocks.Count; i++)
                    if (_blocks[i] != null) return _blocks[i].surfaceKind;
                return RoadSurfaceKind.Asphalt;
            }
        }

        internal void Adopt(AsphaltRoad road)
        {
            if (road == null || _blocks.Contains(road)) return;
            _blocks.Add(road);
            InvalidateArea();
        }

        internal void Release(AsphaltRoad road)
        {
            if (road == null) return;
            _blocks.Remove(road);
            InvalidateArea();
        }

        /// <summary>
        /// Joins a newly laid road to the run that owns it, merging any runs it bridges.
        /// Returns the surviving run.
        /// </summary>
        public static RoadRun JoinOrCreate(AsphaltRoad road, List<AsphaltRoad> connectedNeighbours)
        {
            RoadRun surviving = null;
            float worstWear = 0f;

            for (int i = 0; i < connectedNeighbours.Count; i++)
            {
                var run = connectedNeighbours[i]?.Run;
                if (run == null) continue;
                if (run.Wear01 > worstWear) worstWear = run.Wear01;
                if (surviving == null || run.BlockCount > surviving.BlockCount) surviving = run;
            }

            if (surviving == null) surviving = new RoadRun();

            // Absorb every other run that this cell just bridged.
            for (int i = 0; i < connectedNeighbours.Count; i++)
            {
                var run = connectedNeighbours[i]?.Run;
                if (run == null || run == surviving) continue;
                surviving.Absorb(run);
            }

            surviving.Adopt(road);
            // Merging cannot make pavement younger than the worst surface in the merge.
            if (worstWear > surviving.Wear01) surviving.Wear01 = worstWear;
            return surviving;
        }

        /// <summary>Two surfaces may sit next to each other but may not become one run: they cost
        /// differently, wear differently and hand out different bonuses, so a single wear ledger
        /// over both would be meaningless.</summary>
        internal static bool SameKind(AsphaltRoad a, AsphaltRoad b)
        {
            if (a == null || b == null) return false;
            return a.surfaceKind == b.surfaceKind;
        }

        private void Absorb(RoadRun other)
        {
            if (other == null || other == this) return;
            // A roadway and a pathway that touch stay two runs.
            if (other._blocks.Count > 0 && _blocks.Count > 0 && !SameKind(other._blocks[0], _blocks[0])) return;
            for (int i = 0; i < other._blocks.Count; i++)
            {
                var block = other._blocks[i];
                if (block == null) continue;
                _blocks.Add(block);
                InvalidateArea();
                block.AttachToRun(this);
            }
            other._blocks.Clear();
            other.InvalidateArea();
            TrafficMetres += other.TrafficMetres;
            if (other.Wear01 > Wear01) Wear01 = other.Wear01;
        }

        /// <summary>
        /// Re-solves the runs around a lifted cell. Every neighbour flood-fills its own
        /// component; components that reach each other stay one run, and each piece keeps
        /// the parent's wear because pavement does not heal when a cell is removed.
        /// </summary>
        public static void ResplitAround(List<AsphaltRoad> formerNeighbours, float inheritedWear)
        {
            var assigned = new HashSet<AsphaltRoad>();
            for (int i = 0; i < formerNeighbours.Count; i++)
            {
                var seed = formerNeighbours[i];
                if (seed == null || assigned.Contains(seed)) continue;

                var run = new RoadRun(inheritedWear);
                FloodFill(seed, run, assigned);
            }
        }

        private static void FloodFill(AsphaltRoad seed, RoadRun run, HashSet<AsphaltRoad> assigned)
        {
            var stack = new Stack<AsphaltRoad>();
            stack.Push(seed);
            assigned.Add(seed);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;
                // Leave the pre-split run before joining the new one, or the old run keeps counting
                // cells it no longer owns and bills wear against a length it does not have.
                current.Run?.Release(current);
                run.Adopt(current);
                current.AttachToRun(run);

                var neighbours = current.CollectConnectedNeighbours();
                for (int i = 0; i < neighbours.Count; i++)
                {
                    var next = neighbours[i];
                    if (next == null || assigned.Contains(next)) continue;
                    if (!SameKind(seed, next)) continue;
                    assigned.Add(next);
                    stack.Push(next);
                }
            }
        }

        /// <summary>
        /// Raises the run's wear to a value read from a save. Never lowers it: runs are
        /// re-solved from scratch on load, and the worst surface any of its cells reported
        /// is the honest condition of the strip. Wear is persisted per block rather than per
        /// run id, because run ids are session-scoped and a strip can merge or split between
        /// the save and the load.
        /// </summary>
        public void RaiseWearToSaved(float savedWear)
        {
            if (savedWear <= Wear01) return;
            RestoreWear(savedWear);
        }
    }
}
