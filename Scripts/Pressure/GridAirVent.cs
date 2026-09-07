// Assets/Scripts/VoxelEngine/Pressure/GridAirVent.cs
//
// AIR VENT — pumps oxygen between the grid gas network and the sealed room it faces.
//
//   PRESSURISE   room pressure below target → draws O₂ from grid tanks into the room.
//   DEPRESSURISE room oxygen is recovered back into grid tanks (spacewalk prep).
//   IDLE         holds state, drawing only standby power.
//
// The vent reads the room in front of its own cell, so a vent set into a bulkhead
// charges the compartment it faces rather than the corridor behind it.
//
// Two chassis share this logic:
//   PANEL VENT (recessed)  — a thin wall grille, modest flow, cheap.
//   FULL-BLOCK VENT        — a complete cell of ventilation plant: much higher
//                            throughput, services every room touching the block
//                            instead of only the one it faces, and carries a suit
//                            oxygen-tank refill dock.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Pressure
{
    public enum VentMode { Pressurise, Depressurise, Idle }

    public class GridAirVent : GridBlock, IGridDataProvider, IAirtightBlock
    {
        [Header("Vent")]
        public VentMode mode = VentMode.Pressurise;

        [Tooltip("Target room pressure in atmospheres while pressurising.")]
        [Range(0.2f, 1.2f)] public float targetPressureAtm = 1.0f;

        [Tooltip("Oxygen litres moved per second at full flow.")]
        public float flowLitresPerSecond = 24f;

        [Header("Power")]
        public float idleWatts = 4f;
        public float activeWatts = 60f;

        [Header("Airtight")]
        [Tooltip("A vent set into a wall seals that wall.")]
        public bool airtight = true;

        [Header("Chassis")]
        [Tooltip("Full-block vents fill an entire cell: higher flow, they service every " +
                 "adjacent room at once, and they carry a suit oxygen refill dock.")]
        public bool fullBlock;

        [Tooltip("Litres/second pushed into a docked suit oxygen tank (full-block only).")]
        public float suitRefillLitresPerSecond = 40f;

        /// <summary>Suit oxygen tanks dropped in here are topped up from the pipe network.</summary>
        public ItemContainer SuitDock { get; private set; }

        public bool SealsAir => airtight;

        public bool HasPower => Enabled && Grid != null && Grid.HasPower;
        public override float PowerDraw => !Enabled ? 0f : (IsWorking ? activeWatts : idleWatts);

        /// <summary>True while oxygen actually moved this tick.</summary>
        public bool IsWorking { get; private set; }
        public string Status { get; private set; } = "Idle";

        /// <summary>The primary sealed room this vent services (null when it faces open space).</summary>
        public GridRoom ServicedRoom { get; private set; }

        /// <summary>
        /// Every room this vent moves gas for. A panel vent services exactly one; a
        /// full-block unit services every compartment touching the block, so a single
        /// plant can hold a whole deck at pressure.
        /// </summary>
        public IReadOnlyList<GridRoom> ServicedRooms => _servicedRooms;

        private readonly List<GridRoom> _servicedRooms = new(6);
        private Transform _fanHub;
        private float _fanSpeed;
        private GridPressureSystem _pressure;
        private float _repathTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block")
                blockName = fullBlock ? "Ventilation Unit" : "Air Vent";
            EnsureContainers();
            _pressure = GridPressureSystem.For(Grid);
            _pressure?.MarkDirty();
        }

        /// <summary>Creates the suit refill dock. Safe to call repeatedly.</summary>
        public void EnsureContainers()
        {
            if (!fullBlock) return;
            if (SuitDock == null) SuitDock = new ItemContainer("Suit O₂ Dock", 1);
            else SuitDock.Resize(1);
            SuitDock.AcceptFilter = (item, wanted) =>
                item is VoxelEngine.Items.OxygenTankItem ? Mathf.Min(1, wanted) : 0;
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _pressure?.MarkDirty();
        }

        private void Update()
        {
            // Fan spin-up/spin-down: the unit visibly reacts to actually moving gas,
            // easing toward its target speed rather than snapping.
            if (!fullBlock) return;
            if (_fanHub == null)
            {
                _fanHub = transform.Find("Generated_Visuals/Generated_VentFanHub")
                          ?? transform.Find("Generated_VentFanHub");
                if (_fanHub == null) return;   // prefab has no fan spine authored
            }

            float target = IsWorking && HasPower ? 720f : 0f;
            _fanSpeed = Mathf.Lerp(_fanSpeed, target, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            if (_fanSpeed > 0.5f)
                _fanHub.Rotate(0f, _fanSpeed * Time.deltaTime, 0f, Space.Self);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            IsWorking = false;

            if (!Enabled) { Status = "Disabled"; return; }
            if (Grid == null) { Status = "No Grid"; return; }

            if (_pressure == null) _pressure = GridPressureSystem.For(Grid);
            if (_pressure == null) { Status = "No Pressure Service"; return; }

            // Re-resolve the serviced room a few times a second — cheap, and it keeps
            // the vent correct when a bulkhead is welded shut next to it.
            _repathTimer -= dt;
            if (_repathTimer <= 0f || ServicedRoom == null)
            {
                _repathTimer = 0.4f;
                ServicedRoom = ResolveRoom();
            }

            if (ServicedRoom == null) { Status = "No Sealed Room"; return; }
            if (!HasPower) { Status = "No Power"; return; }

            switch (mode)
            {
                case VentMode.Pressurise:   TickPressurise(dt); break;
                case VentMode.Depressurise: TickDepressurise(dt); break;
                default:                    Status = "Idle"; break;
            }

            // A full-block unit also runs a suit refill dock off the same pipe feed.
            if (fullBlock) TickSuitDock(dt);
        }

        /// <summary>
        /// Tops up an oxygen tank left in the dock straight from the pipe network.
        /// This is the on-ship half of the refillable suit-tank loop.
        /// </summary>
        private void TickSuitDock(float dt)
        {
            EnsureContainers();
            if (SuitDock == null) return;

            var stack = SuitDock.GetSlot(0);
            if (!OxygenTankItem.IsOxygenTank(stack)) return;

            float space = OxygenTankItem.CapacityLitres(stack) - OxygenTankItem.StoredLitres(stack);
            if (space <= 0.01f) return;

            var network = GridGasNetwork.Instance;
            if (network == null || !network.HasPipes(Grid)) return;

            float want = Mathf.Min(space, Mathf.Max(0f, suitRefillLitresPerSecond) * dt);
            float drawn = network.DrawGasFor(this, Gas.GasType.Oxygen, want);
            if (drawn <= 0.0001f) return;

            float accepted = OxygenTankItem.AddLitres(stack, drawn);
            // Never destroy gas the tank could not take.
            float surplus = drawn - accepted;
            if (surplus > 0.0001f) network.FillGasFrom(this, Gas.GasType.Oxygen, surplus);

            if (accepted > 0.0001f)
            {
                IsWorking = true;
                // GetSlot hands back the live stack, so the litres are already applied;
                // SetSlot simply republishes the slot so UI gauges refresh.
                SuitDock.SetSlot(0, stack);
            }
        }

        private void TickPressurise(float dt)
        {
            // Total deficit across every serviced compartment.
            float deficit = 0f;
            for (int i = 0; i < _servicedRooms.Count; i++)
            {
                var r = _servicedRooms[i];
                if (r == null || !r.IsSealed) continue;
                deficit += Mathf.Max(0f,
                    Mathf.Clamp(targetPressureAtm, 0.2f, 1.2f) * r.CapacityLitres - r.OxygenLitres);
            }
            if (deficit <= 0.01f) { Status = "Pressurised"; return; }

            float wanted = Mathf.Min(deficit, Mathf.Max(0f, flowLitresPerSecond) * dt);
            var network = GridGasNetwork.Instance;
            if (network == null || !network.HasPipes(Grid)) { Status = "No Gas Pipe"; return; }

            // Strictly piped: DrawGasFor walks the gas-pipe topology out of this block's
            // own ports, so a vent with nothing plugged into it gets nothing.
            float drawn = network.DrawGasFor(this, Gas.GasType.Oxygen, wanted);
            if (drawn <= 0.0001f) { Status = "No Piped O₂"; return; }

            // Share the draw across rooms in proportion to how empty each one is, so a
            // full-block unit brings a whole deck up together instead of one room at a time.
            float accepted = 0f;
            float remaining = drawn;
            for (int i = 0; i < _servicedRooms.Count && remaining > 0.0001f; i++)
            {
                var r = _servicedRooms[i];
                if (r == null || !r.IsSealed) continue;
                float roomDeficit = Mathf.Max(0f,
                    Mathf.Clamp(targetPressureAtm, 0.2f, 1.2f) * r.CapacityLitres - r.OxygenLitres);
                if (roomDeficit <= 0.0001f) continue;

                float share = Mathf.Min(remaining, drawn * (roomDeficit / deficit));
                float took = r.AddOxygen(share);
                accepted += took;
                remaining -= took;
            }

            // Anything no room could take goes straight back to the tanks so the vent
            // can never destroy oxygen through rounding.
            float surplus = drawn - accepted;
            if (surplus > 0.0001f)
                network.FillGasFrom(this, Gas.GasType.Oxygen, surplus);

            IsWorking = accepted > 0.0001f;
            Status = IsWorking ? "Pressurising" : "Room Full";
        }

        private void TickDepressurise(float dt)
        {
            float available = 0f;
            for (int i = 0; i < _servicedRooms.Count; i++)
            {
                var r = _servicedRooms[i];
                if (r != null && r.IsSealed) available += Mathf.Max(0f, r.OxygenLitres);
            }
            if (available <= 0.01f) { Status = "Depressurised"; return; }

            float wanted = Mathf.Min(available, Mathf.Max(0f, flowLitresPerSecond) * dt);
            var network = GridGasNetwork.Instance;
            if (network == null || !network.HasPipes(Grid)) { Status = "No Gas Pipe"; return; }

            float stored = network.FillGasFrom(this, Gas.GasType.Oxygen, wanted);
            if (stored <= 0.0001f) { Status = "Tanks Full"; return; }

            // Pull the banked litres out of the fullest rooms first.
            float remaining = stored;
            for (int i = 0; i < _servicedRooms.Count && remaining > 0.0001f; i++)
            {
                var r = _servicedRooms[i];
                if (r == null || !r.IsSealed) continue;
                remaining -= r.RemoveOxygen(remaining);
            }

            IsWorking = true;
            Status = "Depressurising";
        }

        /// <summary>
        /// Rebuilds the serviced-room set. A panel vent prefers the room its intake
        /// faces and falls back to any neighbour; a full-block unit collects every
        /// distinct room touching the block.
        /// </summary>
        private GridRoom ResolveRoom()
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
            ? (fullBlock ? "Ventilation Unit" : "Air Vent") : blockName;
        public string DataCategory => "Life Support";

        public string GetDisplayData()
        {
            string title = fullBlock ? "VENTILATION UNIT" : "AIR VENT";
            if (!Enabled) return title + "\nDISABLED";

            var room = ServicedRoom;
            if (room == null) return title + "\nNO SEALED ROOM";

            // A full-block unit aggregates every compartment it serves.
            float oxygen = 0f, capacity = 0f, volume = 0f;
            for (int i = 0; i < _servicedRooms.Count; i++)
            {
                var r = _servicedRooms[i];
                if (r == null || !r.IsSealed) continue;
                oxygen += r.OxygenLitres;
                capacity += r.CapacityLitres;
                volume += r.VolumeM3;
            }
            if (capacity <= 0f) { oxygen = room.OxygenLitres; capacity = room.CapacityLitres; volume = room.VolumeM3; }

            string body = title + "\n"
                 + Status + "\n"
                 + "Pressure " + (capacity > 0f ? oxygen / capacity * 100f : 0f).ToString("0") + "%\n"
                 + "Volume " + volume.ToString("0") + " m³\n"
                 + "O₂ " + oxygen.ToString("0") + " / " + capacity.ToString("0") + " L";

            if (fullBlock)
            {
                body += "\nRooms " + SealedRoomsServed;
                var stack = SuitDock?.GetSlot(0);
                if (OxygenTankItem.IsOxygenTank(stack))
                    body += "\nSuit " + (OxygenTankItem.Fill01(stack) * 100f).ToString("0") + "%";
            }
            return body;
        }

        /// <summary>How many sealed compartments this unit is currently servicing.</summary>
        public int SealedRoomsServed
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _servicedRooms.Count; i++)
                    if (_servicedRooms[i] != null && _servicedRooms[i].IsSealed) n++;
                return n;
            }
        }
    }
}
