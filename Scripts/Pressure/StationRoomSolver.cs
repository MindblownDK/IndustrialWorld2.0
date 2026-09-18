// Assets/Scripts/VoxelEngine/Pressure/StationRoomSolver.cs
//
// THE WORLD ROOM SOLVER — pressurisation for hammer-built stations.
//
// 11.22.0 shipped the Orbital Station family and deliberately did NOT claim pressure,
// because the existing simulation (`GridPressureSystem`, `GridRoom`) operates on
// `GridBlock` and is a SHIP system: it walks a grid's integer block dictionary and knows
// nothing about world-placed objects. This is the missing half.
//
// WHY A SEPARATE SOLVER RATHER THAN EXTENDING THE GRID ONE
// The two live in genuinely different coordinate systems. A ship has an authoritative
// integer lattice with one block per cell; a hammer station is loose world objects at
// arbitrary positions and rotations. Forcing world pieces into the grid solver would
// mean inventing a fake grid for them, and every future change to ship pressure would
// have to keep that fiction working. Instead this shares the ALGORITHM - a bounded
// flood fill with an escape shell, the approach already proven in the grid solver - and
// keeps its own coordinate handling.
//
// THE ESCAPE SHELL IS THE WHOLE TRICK
// A flood fill that reaches the bounding shell around the structure has found a way
// out, so that volume is open space, not a room. This is what makes "sealed" mean
// something: the player cannot get a pressurised compartment by drawing three walls and
// hoping. It also means opening an airlock genuinely vents the compartment, because the
// fill escapes through it on the next solve.
//
// COST CONTROL
// Solving is event-driven and coalesced, never per-frame: placing a piece marks the
// solver dirty and the actual fill runs at most a few times a second. A fill that
// exceeds the cell budget is treated as open rather than allowed to run away, which
// bounds the worst case on an enormous station.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.Pressure
{
    /// <summary>One sealed volume inside a hammer-built station.</summary>
    public sealed class StationRoom
    {
        /// <summary>Lattice cells this room occupies.</summary>
        public readonly HashSet<Vector3Int> Cells = new();

        /// <summary>Lowest cell, used as a stable identity across re-solves.</summary>
        public Vector3Int Anchor { get; internal set; }

        /// <summary>Volume in cubic metres.</summary>
        public float VolumeM3 { get; internal set; }

        /// <summary>Oxygen currently held, in litres.</summary>
        public float OxygenLitres;

        /// <summary>Centre of the room in world space, for proximity tests.</summary>
        public Vector3 Centre { get; internal set; }

        public float CapacityLitres => Mathf.Max(1f, VolumeM3 * PressureRules.LitresPerCubicMetre);

        /// <summary>0..1 how full of breathable air this room is.</summary>
        public float Fill01 => Mathf.Clamp01(OxygenLitres / CapacityLitres);

        /// <summary>True when there is enough air to breathe without a helmet.</summary>
        public bool IsBreathable => Fill01 >= 0.35f;

        public string StatusLabel =>
            Fill01 >= 0.95f ? "PRESSURISED"
            : Fill01 >= 0.35f ? "PARTIAL"
            : Fill01 > 0.02f ? "LOW"
            : "VACUUM";
    }

    /// <summary>
    /// Solves and holds the sealed volumes of every hammer-built station in the world.
    /// A single scene-wide service rather than a component per station, because station
    /// pieces are loose world objects with no owning container to hang it on.
    /// </summary>
    public static class StationRoomSolver
    {
        /// <summary>Lattice pitch in metres. Matches the station piece footprint.</summary>
        public const float CellSize = 2f;

        /// <summary>
        /// Largest volume a single room may occupy. A fill that exceeds it is treated as
        /// open: on a huge station this bounds the cost, and a compartment that big is
        /// not something the player is trying to pressurise anyway.
        /// </summary>
        private const int MaxRoomCells = 4096;

        /// <summary>Hard ceiling on the lattice span, so a stray far-away piece cannot
        /// explode the search volume.</summary>
        private const int MaxSpan = 64;

        private static readonly List<StationRoom> _rooms = new();
        private static readonly Dictionary<Vector3Int, StationRoom> _cellToRoom = new();

        /// <summary>Oxygen carried across a re-solve, keyed by anchor.</summary>
        private static readonly Dictionary<Vector3Int, float> _carried = new();

        private static bool _dirty = true;
        private static float _timer;

        public static IReadOnlyList<StationRoom> Rooms => _rooms;
        public static int RoomCount => _rooms.Count;

        /// <summary>Marks the solver dirty. Called when a station piece is placed or removed.</summary>
        public static void MarkDirty() => _dirty = true;

        /// <summary>Drops every room and forgets carried air. Used on world teardown.</summary>
        public static void Clear()
        {
            _rooms.Clear();
            _cellToRoom.Clear();
            _carried.Clear();
            _dirty = true;
        }

        /// <summary>
        /// Advances the solver. Coalesced: a burst of placements causes one solve, not one
        /// per piece, which is what keeps building a wall cheap.
        /// </summary>
        public static void Tick(float deltaTime)
        {
            if (!_dirty) return;

            _timer -= deltaTime;
            if (_timer > 0f) return;

            _timer = 0.35f;
            Solve();
        }

        /// <summary>
        /// Restores saved air into the rooms that currently exist, matched by anchor cell.
        /// A charge whose room no longer exists is dropped: the player demolished that
        /// compartment, and its air should not reappear in whatever replaced it.
        /// </summary>
        public static void ApplyCharges(Dictionary<Vector3Int, float> charges)
        {
            if (charges == null || charges.Count == 0) return;

            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room == null) continue;
                if (!charges.TryGetValue(room.Anchor, out float litres)) continue;
                room.OxygenLitres = Mathf.Clamp(litres, 0f, room.CapacityLitres);
            }
        }

        /// <summary>The sealed room containing a world position, or null when outside.</summary>
        public static StationRoom RoomAt(Vector3 worldPosition)
        {
            if (_rooms.Count == 0) return null;
            var cell = ToCell(worldPosition);
            return _cellToRoom.TryGetValue(cell, out var room) ? room : null;
        }

        /// <summary>True when the position is inside a room with breathable air.</summary>
        public static bool IsBreathableAt(Vector3 worldPosition)
        {
            var room = RoomAt(worldPosition);
            return room != null && room.IsBreathable;
        }

        // ════════════════════════════════════════════════════════════════
        //  SOLVE
        // ════════════════════════════════════════════════════════════════

        private static readonly Vector3Int[] Neighbours =
        {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };

        public static void Solve()
        {
            _dirty = false;

            CarryForward();
            _rooms.Clear();
            _cellToRoom.Clear();

            var sealing = BuildSealingSet(out Vector3Int min, out Vector3Int max);
            if (sealing.Count == 0) return;

            // One-cell shell around the structure. Reaching it means the fill escaped.
            min -= Vector3Int.one;
            max += Vector3Int.one;

            // Refuse a pathological span rather than allocating an enormous search.
            if (max.x - min.x > MaxSpan || max.y - min.y > MaxSpan || max.z - min.z > MaxSpan)
            {
                Debug.LogWarning("[StationRooms] Station spans more than " + MaxSpan +
                                 " cells on an axis; pressurisation skipped for this structure.");
                return;
            }

            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();

            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                var start = new Vector3Int(x, y, z);
                if (sealing.Contains(start) || !visited.Add(start)) continue;

                var cells = new List<Vector3Int>();
                bool open = false;

                queue.Clear();
                queue.Enqueue(start);
                cells.Add(start);

                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    if (cells.Count > MaxRoomCells) { open = true; break; }

                    for (int d = 0; d < 6; d++)
                    {
                        var next = cell + Neighbours[d];
                        if (sealing.Contains(next)) continue;

                        if (next.x < min.x || next.x > max.x
                            || next.y < min.y || next.y > max.y
                            || next.z < min.z || next.z > max.z)
                        {
                            open = true;   // reached the shell: this volume is outside
                            continue;
                        }

                        if (!visited.Add(next)) continue;
                        cells.Add(next);
                        queue.Enqueue(next);
                    }
                }

                // An open fill IS the outside world, not a room.
                if (open || cells.Count == 0) continue;

                var room = new StationRoom();
                var anchor = cells[0];
                Vector3 sum = Vector3.zero;

                for (int i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    room.Cells.Add(c);
                    if (c.x < anchor.x || (c.x == anchor.x && c.y < anchor.y)
                        || (c.x == anchor.x && c.y == anchor.y && c.z < anchor.z)) anchor = c;
                    sum += ToWorld(c);
                }

                room.Anchor = anchor;
                room.VolumeM3 = cells.Count * CellSize * CellSize * CellSize;
                room.Centre = sum / cells.Count;

                // A room that was sealed before keeps its charge, so editing one wall does
                // not vent an unrelated compartment. A newly sealed volume starts empty:
                // a station is built in vacuum, and air has to be produced, not assumed.
                room.OxygenLitres = _carried.TryGetValue(anchor, out float carried)
                    ? Mathf.Clamp(carried, 0f, room.CapacityLitres)
                    : 0f;

                _rooms.Add(room);
                foreach (var c in room.Cells) _cellToRoom[c] = room;
            }
        }

        private static void CarryForward()
        {
            _carried.Clear();
            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room == null) continue;
                _carried[room.Anchor] = room.OxygenLitres;
            }
        }

        /// <summary>
        /// Every lattice cell occupied by a sealing station piece, plus the bounds of the
        /// whole structure.
        /// </summary>
        private static HashSet<Vector3Int> BuildSealingSet(out Vector3Int min, out Vector3Int max)
        {
            var sealing = new HashSet<Vector3Int>();
            min = Vector3Int.zero;
            max = Vector3Int.zero;
            bool first = true;

            var pieces = StationPiece.All;
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece == null) continue;

                var cell = ToCell(piece.transform.position);

                if (first) { min = max = cell; first = false; }
                else
                {
                    min = Vector3Int.Min(min, cell);
                    max = Vector3Int.Max(max, cell);
                }

                // A dock collar is deliberately open, so it does NOT seal. That is what
                // makes a dock a hole in the hull until a ship mates into it.
                if (piece.SealsPressure) sealing.Add(cell);
            }

            return sealing;
        }

        public static Vector3Int ToCell(Vector3 world) => new(
            Mathf.RoundToInt(world.x / CellSize),
            Mathf.RoundToInt(world.y / CellSize),
            Mathf.RoundToInt(world.z / CellSize));

        public static Vector3 ToWorld(Vector3Int cell) => new(
            cell.x * CellSize, cell.y * CellSize, cell.z * CellSize);
    }
}
