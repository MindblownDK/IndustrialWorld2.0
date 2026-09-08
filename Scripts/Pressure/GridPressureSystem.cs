// Assets/Scripts/VoxelEngine/Pressure/GridPressureSystem.cs
//
// PRESSURE & AIRTIGHT SERVICE — detects sealed rooms inside a grid, tracks the
// oxygen charge of each room, and answers "can this world position be breathed in?"
//
// Design notes:
//   • The solver is a bounded flood fill over EMPTY grid cells. A fill that escapes
//     the hull bounding shell is a breach, so an unfinished room simply never holds
//     pressure — no error states, no half-built special cases.
//   • Rebuilds are event-driven and coalesced: hull edits and door state changes mark
//     the grid dirty, and at most one solve runs per settle window.
//   • Oxygen charge is carried across rebuilds by room anchor, so opening a hatch on
//     the far side of a ship never dumps the pressure of an unrelated compartment.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Pressure
{
    [RequireComponent(typeof(GridEntity))]
    [DisallowMultipleComponent]
    public sealed class GridPressureSystem : MonoBehaviour
    {
        /// <summary>Largest sealed volume the solver will accept, in cells.</summary>
        public const int MaxRoomCells = 4096;

        private const float SettleSeconds = 0.25f;
        private const float AmbientRefreshSeconds = 1.0f;
        private const float OccupancyRefreshSeconds = 0.5f;
        private const float EqualiseAtmPerSecond = 0.02f;

        private GridEntity _grid;
        private readonly List<GridRoom> _rooms = new();
        private readonly Dictionary<Vector3Int, GridRoom> _cellToRoom = new();
        private readonly Dictionary<Vector3Int, float> _carriedOxygen = new();
        private bool _dirty = true;
        private float _dirtyAt;
        private float _ambientTimer;
        private float _occupancyTimer;

        public IReadOnlyList<GridRoom> Rooms => _rooms;
        public int SealedRoomCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _rooms.Count; i++) if (_rooms[i].IsSealed) n++;
                return n;
            }
        }

        private void Awake() => _grid = GetComponent<GridEntity>();

        private void OnEnable() => RoomAtmosphereService.Register(this);
        private void OnDisable() => RoomAtmosphereService.Unregister(this);

        /// <summary>Attach (or fetch) the pressure service for a grid.</summary>
        public static GridPressureSystem For(GridEntity grid)
        {
            if (grid == null) return null;
            var svc = grid.GetComponent<GridPressureSystem>();
            if (svc == null) svc = grid.gameObject.AddComponent<GridPressureSystem>();
            return svc;
        }

        /// <summary>Queue a room re-solve (hull edit, door toggled, vent placed).</summary>
        public void MarkDirty()
        {
            if (!_dirty) _dirtyAt = Time.unscaledTime;
            _dirty = true;
        }

        /// <summary>Marks the pressure service of the grid owning this block, if any.</summary>
        public static void MarkDirty(GridBlock block)
        {
            if (block == null || block.Grid == null) return;
            block.Grid.GetComponent<GridPressureSystem>()?.MarkDirty();
        }

        private void Update()
        {
            if (_dirty && Time.unscaledTime - _dirtyAt >= SettleSeconds) Solve();

            float dt = Time.deltaTime;
            _ambientTimer -= dt;
            if (_ambientTimer <= 0f)
            {
                _ambientTimer = AmbientRefreshSeconds;
                RefreshAmbient();
            }

            TickOccupancy(dt);
        }

        /// <summary>Re-samples the planet outside. A ship that flies from a breathable
        /// world into orbit must see its open rooms lose ambient air as it climbs.</summary>
        private void RefreshAmbient()
        {
            if (_rooms.Count == 0) return;
            var ambient = PressureRules.SampleAmbient(transform.position);
            for (int i = 0; i < _rooms.Count; i++)
                if (_rooms[i] != null) _rooms[i].Ambient = ambient;
        }

        /// <summary>Counts the players breathing in each room and burns the matching
        /// oxygen. More occupants drain a compartment proportionally faster.</summary>
        private void TickOccupancy(float dt)
        {
            if (dt <= 0f || _rooms.Count == 0) return;

            _occupancyTimer -= dt;
            if (_occupancyTimer <= 0f)
            {
                _occupancyTimer = OccupancyRefreshSeconds;
                RecountOccupants();
            }

            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room == null || !room.IsSealed) continue;
                if (room.Occupants <= 0 || room.OxygenLitres <= 0f) continue;

                // Metabolic draw scales linearly with the number of people inside.
                room.RemoveOxygen(PressureRules.OxygenLitresPerOccupantPerSecond
                                  * room.Occupants * dt);
            }

            TickAmbientEqualisation(dt);
        }

        /// <summary>
        /// No hull is perfect. A sealed room slowly equalises toward the planet outside,
        /// which means a base on a breathable world stays livable on its own, while the
        /// same base in vacuum bleeds down and genuinely needs a vent keeping it charged.
        /// </summary>
        private void TickAmbientEqualisation(float dt)
        {
            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room == null || !room.IsSealed) continue;

                float targetLitres = room.Ambient.IsOxygenBearing
                    ? room.Ambient.PressureAtm * room.CapacityLitres
                    : 0f;

                float delta = targetLitres - room.OxygenLitres;
                if (Mathf.Abs(delta) < 0.01f) continue;

                float step = room.CapacityLitres * EqualiseAtmPerSecond * dt;
                room.OxygenLitres += Mathf.Clamp(delta, -step, step);
                room.OxygenLitres = Mathf.Clamp(room.OxygenLitres, 0f, room.CapacityLitres);
            }
        }

        private void RecountOccupants()
        {
            for (int i = 0; i < _rooms.Count; i++)
                if (_rooms[i] != null) _rooms[i].Occupants = 0;

            var players = Object.FindObjectsByType<VoxelEngine.Player.PlayerController>(FindObjectsInactive.Exclude);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                var room = RoomAtWorldNoSolve(players[i].transform.position);
                if (room != null) room.Occupants++;
            }
        }

        // ── Queries ────────────────────────────────────────────────────────────

        /// <summary>Room containing a world position, or null when outside the hull.</summary>
        public GridRoom RoomAtWorld(Vector3 worldPosition)
        {
            if (_grid == null) return null;
            if (_dirty) Solve();
            return _cellToRoom.TryGetValue(_grid.WorldToGrid(worldPosition), out var room) ? room : null;
        }

        /// <summary>Room lookup that never triggers a solve (used inside the tick loop).</summary>
        private GridRoom RoomAtWorldNoSolve(Vector3 worldPosition)
        {
            if (_grid == null) return null;
            return _cellToRoom.TryGetValue(_grid.WorldToGrid(worldPosition), out var room) ? room : null;
        }

        /// <summary>True when a sealed, charged room covers this world position.</summary>
        public bool IsBreathableAt(Vector3 worldPosition)
        {
            var room = RoomAtWorld(worldPosition);
            return room != null && room.IsBreathable;
        }

        /// <summary>Total oxygen litres missing across every sealed room.</summary>
        public float MissingOxygenLitres()
        {
            float missing = 0f;
            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room.IsSealed) missing += Mathf.Max(0f, room.CapacityLitres - room.OxygenLitres);
            }
            return missing;
        }

        // ── Solver ─────────────────────────────────────────────────────────────

        /// <summary>Rebuild every room in this grid. Safe to call at any time.</summary>
        public void Solve()
        {
            _dirty = false;
            if (_grid == null) _grid = GetComponent<GridEntity>();
            if (_grid == null) return;

            CarryOxygenForward();
            _rooms.Clear();
            _cellToRoom.Clear();

            var sealing = BuildSealingSet(out Vector3Int min, out Vector3Int max);
            if (sealing.Count == 0) return;

            // One-cell shell around the hull: escaping into it means the room is open.
            min -= Vector3Int.one;
            max += Vector3Int.one;

            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            float cellSize = _grid.gridSize.CellSize();
            var ambient = PressureRules.SampleAmbient(transform.position);
            _ambientTimer = AmbientRefreshSeconds;

            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                var start = new Vector3Int(x, y, z);
                if (sealing.Contains(start) || !visited.Add(start)) continue;

                var room = new GridRoom();
                bool open = false;
                queue.Clear();
                queue.Enqueue(start);
                room.Cells.Add(start);

                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    if (room.Cells.Count > MaxRoomCells) { open = true; break; }

                    for (int d = 0; d < 6; d++)
                    {
                        var next = cell + Neighbours[d];
                        if (sealing.Contains(next)) continue;
                        if (next.x < min.x || next.x > max.x
                            || next.y < min.y || next.y > max.y
                            || next.z < min.z || next.z > max.z)
                        {
                            open = true;   // reached the outside shell → breached
                            continue;
                        }
                        if (!visited.Add(next)) continue;
                        room.Cells.Add(next);
                        queue.Enqueue(next);
                    }
                }

                // An open fill IS the outside world, not a room. The player standing
                // there simply breathes the planet through the normal atmosphere path.
                if (open || room.Cells.Count == 0) continue;

                room.Reset(true, cellSize, ambient);

                // A room that was already sealed on the previous solve keeps its charge
                // (a hull edit elsewhere must not vent it). A room that is newly sealed
                // keeps whatever air it just trapped: on an oxygen world that is a full
                // planetary charge, in vacuum it is nothing. This is also what makes
                // opening a door vent the compartment — it stops being sealed, loses its
                // carry-forward entry, and re-seeds from ambient when shut again.
                if (_carriedOxygen.TryGetValue(room.Anchor, out float carried))
                    room.OxygenLitres = Mathf.Clamp(carried, 0f, room.CapacityLitres);
                else
                    room.SeedFromAmbient();

                _rooms.Add(room);
                foreach (var c in room.Cells) _cellToRoom[c] = room;
            }
        }

        private void CarryOxygenForward()
        {
            _carriedOxygen.Clear();
            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room.OxygenLitres > 0f) _carriedOxygen[room.Anchor] = room.OxygenLitres;
            }
        }

        private HashSet<Vector3Int> BuildSealingSet(out Vector3Int min, out Vector3Int max)
        {
            var sealing = new HashSet<Vector3Int>();
            min = Vector3Int.zero;
            max = Vector3Int.zero;
            bool first = true;

            foreach (var kv in _grid.Blocks)
            {
                var block = kv.Value;
                if (block == null) continue;
                var cell = kv.Key;

                if (first) { min = max = cell; first = false; }
                else
                {
                    min = Vector3Int.Min(min, cell);
                    max = Vector3Int.Max(max, cell);
                }

                if (PressureRules.Seals(block)) sealing.Add(cell);
            }

            return sealing;
        }

        private static readonly Vector3Int[] Neighbours =
        {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
    }
}
