// Assets/Scripts/VoxelEngine/Pressure/GridExhaustScrubber.cs
//
// EXHAUST SCRUBBER — the cure for roadmap 5.1 item 14. A concealed space keeps
// everything pumped into it: the waste heat of a running diesel and the exhaust of a
// stack that terminates inside the hull. This block moves the compartment's air.
//
//   SCRUB   air is pulled off the room, its heat and foul gas go overboard, and the
//           vacated volume is replaced from the grid gas network through the vent
//           ports. A room with piped oxygen is held at pressure while it cools; a
//           room without it is vented to space as well as cooled, because nothing is
//           free.
//   IDLE    holds the compartment exactly as it is, drawing standby power only.
//
// Throughput is expressed in air changes per minute, so a closet is cleared in seconds
// and a hangar is a genuine industrial job. Both chassis share this logic:
//   PANEL SCRUBBER (recessed) — a grille set into the bulkhead, services the room it faces.
//   FULL-BLOCK SCRUBBER      — a complete cell of plant, services every compartment
//                              touching the block and carries the capture line.
//
// Heat is only destroyed while the unit has power. An unpowered scrubber is a hole in
// the wall that leaks a little air, not a fan.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Thermal;

namespace VoxelEngine.Pressure
{
    public enum ScrubMode { Scrub, Idle }

    public class GridExhaustScrubber : GridBlock, IGridDataProvider, IAirtightBlock
    {
        [Header("Scrubber")]
        [Tooltip("Scrubbing pulls the compartment's heat and foul gas overboard and replaces the volume from the gas network.")]
        public ScrubMode mode = ScrubMode.Scrub;

        [Tooltip("Complete air changes per compartment per minute at full flow.")]
        [Range(1f, 60f)] public float airChangesPerMinute = 12f;

        [Tooltip("Fraction of the moving air that is scrubbed clean rather than merely circulated.")]
        [Range(0.2f, 1f)] public float scrubEfficiency = 0.85f;

        [Tooltip("Share of the extracted heat that leaks back into the room instead of going overboard. Zero is an ideal radiator; the value exists so a scrubber can be authored as a heat pump.")]
        [Range(0f, 0.5f)] public float heatReturnFraction = 0f;

        [Header("Power")]
        public float idleWatts = 6f;
        public float activeWatts = 110f;

        [Header("Airtight")]
        [Tooltip("A scrubber set into a wall seals that wall.")]
        public bool airtight = true;

        [Header("Chassis")]
        [Tooltip("Full-block units fill an entire cell: much higher throughput, they service " +
                 "every compartment touching the block, and they run the capture line.")]
        public bool fullBlock;

        [Tooltip("Litres of compartment air banked into the gas network as ExhaustGas per second while scrubbing.")]
        public float exhaustCaptureLitresPerSecond = 18f;

        public bool SealsAir => airtight;
        public bool HasPower => Enabled && Grid != null && Grid.HasPower;
        public override float PowerDraw => !Enabled ? 0f : (IsWorking ? activeWatts : idleWatts);

        /// <summary>True while air actually moved out of a compartment this tick.</summary>
        public bool IsWorking { get; private set; }
        public string Status { get; private set; } = "Idle";

        /// <summary>Primary sealed compartment this unit is clearing (null when it faces open space).</summary>
        public GridRoom ServicedRoom { get; private set; }

        /// <summary>Every compartment this unit moves air for. A panel unit serves one;
        /// a full-block plant serves every room touching the block.</summary>
        public IReadOnlyList<GridRoom> ServicedRooms => _servicedRooms;

        /// <summary>°C per second this unit is currently able to pull out of the compartment.</summary>
        public float HeatExtractionCPerSecond { get; private set; }

        private readonly List<GridRoom> _servicedRooms = new(6);
        private Transform _fanHub;
        private float _fanSpeed;
        private GridPressureSystem _pressure;
        private float _repathTimer;
        private float _captureDebtLitres;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block")
                blockName = fullBlock ? "Exhaust Scrubber" : "Scrubber Grille";
            _pressure = GridPressureSystem.For(Grid);
            _pressure?.MarkDirty();
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _pressure?.MarkDirty();
        }

        private void Update()
        {
            // Laminar fan motion: the unit visibly reacts to actually clearing air,
            // easing toward its target speed instead of snapping (project rule: nothing
            // appears or disappears instantly).
            if (_fanHub == null)
            {
                _fanHub = transform.Find("Generated_Visuals/Generated_VentFanHub")
                          ?? transform.Find("Generated_VentFanHub");
                if (_fanHub == null) return;   // prefab has no fan spine authored
            }

            float target = IsWorking && HasPower ? (fullBlock ? 860f : 520f) : 0f;
            _fanSpeed = Mathf.Lerp(_fanSpeed, target, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            if (_fanSpeed > 0.5f)
                _fanHub.Rotate(0f, _fanSpeed * Time.deltaTime, 0f, Space.Self);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            IsWorking = false;
            HeatExtractionCPerSecond = 0f;

            if (!Enabled) { Status = "Disabled"; return; }
            if (Grid == null) { Status = "No Grid"; return; }

            if (_pressure == null) _pressure = GridPressureSystem.For(Grid);
            if (_pressure == null) { Status = "No Pressure Service"; return; }

            // Re-resolve the serviced compartments a few times a second — cheap, and it
            // keeps the unit correct when a bulkhead is welded shut next to it.
            _repathTimer -= dt;
            if (_repathTimer <= 0f || ServicedRoom == null)
            {
                _repathTimer = 0.4f;
                ServicedRoom = ResolveRooms();
            }

            if (_servicedRooms.Count == 0) { Status = "No Sealed Room"; return; }
            if (!HasPower) { Status = "No Power"; return; }
            if (mode == ScrubMode.Idle) { Status = "Idle"; return; }

            TickScrub(dt);
        }

        /// <summary>
        /// One round of work: move the compartment's air, dump what was in it overboard,
        /// and refill the volume. Heat and foul gas are removed together because the only
        /// thing carrying them out of here is air.
        /// </summary>
        private void TickScrub(float dt)
        {
            float efficiency = Mathf.Clamp01(scrubEfficiency);
            float flow01 = Mathf.Clamp01(Mathf.Max(0f, airChangesPerMinute) / 60f) * efficiency;

            // Rated capacity of this unit, in °C of compartment air it can clear per second.
            float maxRiseRateC = flow01 * ThermalRules.RoomMaxRiseC;
            HeatExtractionCPerSecond = maxRiseRateC;

            bool anyWork = false;
            float moved = 0f;
            for (int i = 0; i < _servicedRooms.Count; i++)
            {
                var room = _servicedRooms[i];
                if (room == null || !room.IsSealed) continue;

                // Nothing to clear is not a reason to run: a cold, clean compartment
                // costs the ship nothing but standby.
                if (room.RoomRiseC < 2f && room.ExhaustHeatC < 5f) continue;

                float removed = room.ExtractAtmosphere(
                    Mathf.Min(room.RoomRiseC, maxRiseRateC * dt),
                    room.ExhaustHeatC * flow01 * dt);

                // A fraction of what was pulled out leaks back in (a heat pump, not a
                // perfect radiator, when an author asks for one).
                if (heatReturnFraction > 0f && removed > 0f)
                    room.HeatLoadC += removed * Mathf.Clamp01(heatReturnFraction);

                // Air volume leaves with the heat. Refill it from the network; whatever
                // the network cannot give stays gone, which is a real cost on an airless
                // world and exactly why the vent block still matters.
                float litresMoved = room.CapacityLitres * flow01 * dt;
                moved += litresMoved;
                RefillRoom(room, litresMoved);

                if (removed > 0.0001f)
                    anyWork = true;
            }

            if (!anyWork) { Status = "Room Clear"; return; }

            IsWorking = true;
            // Over-cap capture never destroys gas: whatever the pipes could not take is
            // simply banked for the next round.
            if (fullBlock && moved > 0f) _captureDebtLitres += moved;
            TickCapture();
            Status = "Scrubbing";
        }

        /// <summary>Replaces the volume of air this unit moved out of a compartment.</summary>
        private void RefillRoom(GridRoom room, float litres)
        {
            if (litres <= 0.0001f) return;
            var network = GridGasNetwork.Instance;
            if (network == null || !network.HasPipes(Grid)) return;

            float space = Mathf.Max(0f, room.CapacityLitres - room.OxygenLitres);
            float want = Mathf.Min(space, litres);
            if (want <= 0.0001f) return;

            float drawn = network.DrawGasFor(this, GasType.Oxygen, want);
            if (drawn > 0.0001f) room.AddOxygen(drawn);
        }

        /// <summary>Full-block units hand the foul gas they removed to the capture line,
        /// so a scrubbed engine room can still be a source of industrial ExhaustGas.</summary>
        private void TickCapture()
        {
            if (!fullBlock || _captureDebtLitres <= 0.01f) return;
            var network = GridGasNetwork.Instance;
            if (network == null || !network.HasPipes(Grid)) return;

            float want = Mathf.Min(_captureDebtLitres, Mathf.Max(0f, exhaustCaptureLitresPerSecond) * Time.fixedDeltaTime);
            if (want <= 0.0001f) return;

            float stored = network.FillGasFrom(this, GasType.ExhaustGas, want);
            // Gas that had nowhere to go is not lost, it just stays in the room.
            _captureDebtLitres = stored > 0.0001f ? Mathf.Max(0f, _captureDebtLitres - want) : _captureDebtLitres;
        }

        /// <summary>
        /// Rebuilds the serviced-room set. A panel unit prefers the room its face points at
        /// and falls back to any neighbour; a full-block unit collects every distinct room
        /// touching the block, so one plant can clear a whole deck.
        /// </summary>
        private GridRoom ResolveRooms()
        {
            _servicedRooms.Clear();
            if (_pressure == null || Grid == null) return null;

            if (fullBlock)
            {
                for (int d = 0; d < Offsets.Length; d++)
                {
                    var probe = Grid.GridToWorld(GridPos + Offsets[d]);
                    var neighbour = _pressure.RoomAtWorld(probe);
                    if (neighbour != null && !_servicedRooms.Contains(neighbour))
                        _servicedRooms.Add(neighbour);
                }
                return _servicedRooms.Count > 0 ? _servicedRooms[0] : null;
            }

            Vector3 face = transform.position + transform.forward * Grid.gridSize.CellSize();
            var room = _pressure.RoomAtWorld(face);
            if (room == null)
            {
                for (int d = 0; d < Offsets.Length; d++)
                {
                    var probe = Grid.GridToWorld(GridPos + Offsets[d]);
                    room = _pressure.RoomAtWorld(probe);
                    if (room != null) break;
                }
            }
            if (room != null) _servicedRooms.Add(room);
            return room;
        }

        private static readonly Vector3Int[] Offsets =
        {
            new(0, 1, 0), new(0, -1, 0),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 0, 1), new(0, 0, -1),
        };

        // ── Screen telemetry ───────────────────────────────────────────────────
        public string SourceName => string.IsNullOrWhiteSpace(blockName)
            ? (fullBlock ? "Exhaust Scrubber" : "Scrubber Grille") : blockName;
        public string DataCategory => "Engine Room";

        public string GetDisplayData()
        {
            var room = ServicedRoom;
            if (room == null)
                return "SCRUBBER " + (Enabled ? "ONLINE" : "OFFLINE") + "\nNO SEALED ROOM";

            return "SCRUBBER " + (IsWorking ? "SCRUBBING" : HasPower ? "ROOM CLEAR" : "NO POWER") + "\n"
                 + "AIR " + room.AirTemperatureC.ToString("0") + " C (" + VoxelEngine.Thermal.ThermalRules.BandLabel(room.Band) + ")\n"
                 + "EXHAUST " + (room.ExhaustLoad01 * 100f).ToString("0") + "%\n"
                 + "O2 " + (room.Fill01 * 100f).ToString("0") + "%\n"
                 + "FLOW " + airChangesPerMinute.ToString("0") + " ACP";
        }
    }
}
