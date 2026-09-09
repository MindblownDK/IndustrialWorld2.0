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
using VoxelEngine.Thermal;

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

        /// <summary>How often the concealed-atmosphere solve runs (matches the thermal tick).</summary>
        private const float ThermalInterval = 0.25f;

        /// <summary>Air that leaks through a hull by itself, as a fraction of the banked rise per second.</summary>
        private const float AtmosphereLeakPerSecond = 0.05f;

        private GridEntity _grid;
        private readonly List<GridRoom> _rooms = new();
        private readonly Dictionary<Vector3Int, GridRoom> _cellToRoom = new();
        private readonly Dictionary<Vector3Int, float> _carriedOxygen = new();
        private readonly Dictionary<Vector3Int, float> _carriedHeat = new();
        private readonly Dictionary<Vector3Int, float> _carriedExhaust = new();
        private bool _dirty = true;
        private float _dirtyAt;
        private float _ambientTimer;
        private float _occupancyTimer;
        private float _thermalTimer;

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
            TickAtmosphere(dt);
        }

        /// <summary>Re-samples the planet outside. A ship that flies from a breathable
        /// world into orbit must see its open rooms lose ambient air as it climbs.</summary>
        private void RefreshAmbient()
        {
            if (_rooms.Count == 0) return;
            var ambient = PressureRules.SampleAmbient(transform.position);
            float exterior = ThermalRules.AmbientTemperatureC(transform.position);
            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room == null) continue;
                room.Ambient = ambient;
                room.ExteriorTemperatureC = exterior;
                if (!room.IsSealed)
                {
                    // Open to the sky: the air inside simply IS the air outside.
                    room.TemperatureC = exterior;
                    room.HeatLoadC = 0f;
                    room.ExhaustHeatC = 0f;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CONCEALED ATMOSPHERE — heat and exhaust trapped inside a volume
        //  (roadmap 5.1 item 14). Sources restate their contribution every round;
        //  the solve turns that into a temperature the hull can argue with.
        // ══════════════════════════════════════════════════════════════════════
        private void TickAtmosphere(float dt)
        {
            _thermalTimer -= dt;
            if (_thermalTimer > 0f || _rooms.Count == 0) return;
            _thermalTimer = ThermalInterval;

            var thermal = _grid != null ? _grid.GetComponent<GridThermalSystem>() : null;
            float exterior = ThermalRules.AmbientTemperatureC(transform.position);
            float step = Mathf.Min(dt, ThermalInterval);

            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room == null) continue;
                room.ExteriorTemperatureC = exterior;
                if (!room.IsSealed) continue;

                // Where did the room's waste heat go? Into its own hull, and out
                // through whatever leaks the pressure solver measured.
                float hullLoss = HullAverageTemperature(room, thermal);
                float lossKJperS = Mathf.Max(0f, room.TemperatureC - hullLoss)
                                   * ThermalRules.RoomHullLossKJperSPerK;
                float leakKJperS = room.HeatLoadC * AtmosphereLeakPerSecond * room.HeatCapacityKJperK;

                // Bake the trapped heat in. Rising is capped so a runaway engine room
                // settles at its damage band instead of climbing forever.
                room.HeatLoadC = Mathf.Clamp(
                    room.HeatLoadC + (room.PendingWasteKJperS - lossKJperS - leakKJperS) * step
                    / room.HeatCapacityKJperK,
                    0f, ThermalRules.RoomMaxRiseC);

                // Air temperature = trapped heat, plus the rise the working machinery is
                // driving right now. Interior blocks are coupled to the second part at a
                // fraction, so the loop can never lift itself indefinitely.
                float riseTarget = room.PendingWasteKJperS
                                   / (ThermalRules.RoomHullLossKJperSPerK * 0.5f
                                      + room.HeatCapacityKJperK * 0.02f);
                float target = exterior + room.HeatLoadC
                               + Mathf.Min(Mathf.Max(0f, riseTarget), ThermalRules.RoomMaxRiseC);
                room.TemperatureC = Mathf.Max(exterior + room.HeatLoadC,
                    Mathf.Lerp(room.TemperatureC, target,
                        1f - Mathf.Exp(-ThermalRules.RoomAirCouplingPerSecond * step)));

                // Foul gas saturates toward what the sources are holding it at, and falls
                // back as soon as they stop: the room is never told what the sources are not saying.
                // An idle volume with no source in it is not told anything, so it simply
                // falls back toward clean air under the natural leak below.
                float exhaustTarget = room.ExhaustReporters > 0
                    ? room.ExhaustSumC / Mathf.Max(1, room.ExhaustReporters)
                    : 0f;
                room.ExhaustHeatC = Mathf.Lerp(room.ExhaustHeatC, exhaustTarget,
                    1f - Mathf.Exp(-ThermalRules.RoomExhaustFillRate * step));
                room.ExhaustSumC = 0f;
                room.ExhaustReporters = 0;
                // Trapped gas also dissipates on its own through whatever the hull leaks.
                room.ExhaustHeatC = Mathf.Max(0f, room.ExhaustHeatC
                    - room.ExhaustHeatC * AtmosphereLeakPerSecond * step);
                room.ExhaustHeatC = Mathf.Clamp(room.ExhaustHeatC, 0f, ThermalRules.RoomExhaustReferenceC);

                // Sources restate every round; the average of what they said is the
                // room's load until the next round says otherwise.
                room.PendingWasteKJperS = room.ReportedWasteKJperS;
                room.ReportedWasteKJperS = 0f;
                room.ReportedSources = 0;
            }

            // A grid with no sealed volume still needs its rooms tracked against the sky.
            if (thermal != null) thermal.PublishRoomWorst(_rooms);
        }

        /// <summary>Mean temperature of the blocks enclosing a volume: what the air can
        /// actually shed heat to. Falls back to exterior air when nothing is hot yet.</summary>
        private float HullAverageTemperature(GridRoom room, GridThermalSystem thermal)
        {
            if (thermal == null || room == null) return room.ExteriorTemperatureC;

            float sum = 0f;
            int count = 0;
            foreach (var cell in room.Cells)
            {
                for (int d = 0; d < Neighbours.Length; d++)
                {
                    if (room.Contains(cell + Neighbours[d])) continue;
                    if (!_grid.Blocks.TryGetValue(cell + Neighbours[d], out var wall)) continue;
                    sum += thermal.TemperatureOf(wall);
                    count++;
                }
            }

            return count > 0
                ? sum / count
                : room.ExteriorTemperatureC;
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

        /// <summary>Room covering a grid cell, or null when the cell is not inside a volume.</summary>
        public GridRoom RoomAtCell(Vector3Int cell)
        {
            if (_dirty) Solve();
            return _cellToRoom.TryGetValue(cell, out var room) ? room : null;
        }

        /// <summary>
        /// The sealed volume a block is standing in, when there is one. Sources use this
        /// to decide whether their waste heat has anywhere to go.
        /// </summary>
        public static GridRoom ConcealedRoom(GridBlock block)
        {
            if (block == null || block.Grid == null) return null;
            var system = block.Grid.GetComponent<GridPressureSystem>();
            if (system == null) return null;
            var room = system.RoomAtWorld(block.transform.position);
            return room != null && room.IsSealed ? room : null;
        }

        /// <summary>
        /// Pushes waste heat (kJ/s) into the concealed volume at this position. A no-op
        /// outdoors, which is exactly the point: the sky is the cheapest heatsink there is.
        /// </summary>
        public void InjectWasteHeat(Vector3 worldPosition, float kilojoulesPerSecond)
        {
            if (_dirty) Solve();
            if (_cellToRoom.TryGetValue(_grid.WorldToGrid(worldPosition), out var room))
                room.ReportWasteHeat(kilojoulesPerSecond);
        }

        /// <summary>
        /// Restates the hot gas a stack is releasing in the concealed volume at this
        /// position. Sources call it every round even with nothing to report, so the
        /// room's foul air decays honestly instead of holding the last engine's number.
        /// </summary>
        public void InjectExhaust(Vector3 worldPosition, float streamTemperatureC)
        {
            if (_dirty) Solve();
            if (_cellToRoom.TryGetValue(_grid.WorldToGrid(worldPosition), out var room))
                room.ReportExhaust(streamTemperatureC);
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

                // Atmosphere carries over on the same anchor rule as the oxygen charge:
                // a hull edit somewhere else must not undo an engine room that is
                // already hot, but a volume that stopped being sealed starts clean.
                if (_carriedHeat.TryGetValue(room.Anchor, out float carriedHeat))
                {
                    room.HeatLoadC = Mathf.Clamp(carriedHeat, 0f, ThermalRules.RoomMaxRiseC);
                    room.TemperatureC = Mathf.Max(room.TemperatureC, room.ExteriorTemperatureC + room.HeatLoadC);
                }
                if (_carriedExhaust.TryGetValue(room.Anchor, out float carriedExhaust))
                    room.ExhaustHeatC = Mathf.Clamp(carriedExhaust, 0f, ThermalRules.RoomExhaustReferenceC);
                room.ExteriorTemperatureC = ThermalRules.AmbientTemperatureC(transform.position);

                _rooms.Add(room);
                foreach (var c in room.Cells) _cellToRoom[c] = room;
            }
        }

        private void CarryOxygenForward()
        {
            _carriedOxygen.Clear();
            _carriedHeat.Clear();
            _carriedExhaust.Clear();
            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room.OxygenLitres > 0f) _carriedOxygen[room.Anchor] = room.OxygenLitres;
                // Only sealed volumes bank heat and foul air. A compartment that is no
                // longer sealed had its atmosphere vented, and that is the end of it.
                if (!room.IsSealed) continue;
                if (room.HeatLoadC > 0.01f) _carriedHeat[room.Anchor] = room.HeatLoadC;
                if (room.ExhaustHeatC > 0.01f) _carriedExhaust[room.Anchor] = room.ExhaustHeatC;
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
