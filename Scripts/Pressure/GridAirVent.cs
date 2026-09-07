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

using UnityEngine;
using VoxelEngine.GridSystem;

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

        public bool SealsAir => airtight;

        public bool HasPower => Enabled && Grid != null && Grid.HasPower;
        public override float PowerDraw => !Enabled ? 0f : (IsWorking ? activeWatts : idleWatts);

        /// <summary>True while oxygen actually moved this tick.</summary>
        public bool IsWorking { get; private set; }
        public string Status { get; private set; } = "Idle";

        /// <summary>The sealed room this vent services (null when it faces open space).</summary>
        public GridRoom ServicedRoom { get; private set; }

        private GridPressureSystem _pressure;
        private float _repathTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block") blockName = "Air Vent";
            _pressure = GridPressureSystem.For(Grid);
            _pressure?.MarkDirty();
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _pressure?.MarkDirty();
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
        }

        private void TickPressurise(float dt)
        {
            var room = ServicedRoom;
            float target = Mathf.Clamp(targetPressureAtm, 0.2f, 1.2f) * room.CapacityLitres;
            float deficit = target - room.OxygenLitres;
            if (deficit <= 0.01f) { Status = "Pressurised"; return; }

            float wanted = Mathf.Min(deficit, Mathf.Max(0f, flowLitresPerSecond) * dt);
            var network = GridGasNetwork.Instance;
            float drawn = network != null
                ? network.DrawGasFor(this, Gas.GasType.Oxygen, wanted)
                : 0f;

            if (drawn <= 0.0001f) { Status = "No O₂ Supply"; return; }

            float accepted = room.AddOxygen(drawn);
            // Anything the room could not take goes straight back to the tanks so the
            // vent can never destroy oxygen through rounding.
            float surplus = drawn - accepted;
            if (surplus > 0.0001f && network != null)
                network.FillGasFrom(this, Gas.GasType.Oxygen, surplus);

            IsWorking = accepted > 0.0001f;
            Status = IsWorking ? "Pressurising" : "Room Full";
        }

        private void TickDepressurise(float dt)
        {
            var room = ServicedRoom;
            if (room.OxygenLitres <= 0.01f) { Status = "Depressurised"; return; }

            float wanted = Mathf.Min(room.OxygenLitres, Mathf.Max(0f, flowLitresPerSecond) * dt);
            var network = GridGasNetwork.Instance;
            float stored = network != null
                ? network.FillGasFrom(this, Gas.GasType.Oxygen, wanted)
                : 0f;

            if (stored <= 0.0001f) { Status = "Tanks Full"; return; }

            room.RemoveOxygen(stored);
            IsWorking = true;
            Status = "Depressurising";
        }

        /// <summary>Prefers the room the vent's intake faces, then any room touching it.</summary>
        private GridRoom ResolveRoom()
        {
            if (_pressure == null || Grid == null) return null;

            Vector3 face = transform.position + transform.forward * Grid.gridSize.CellSize();
            var room = _pressure.RoomAtWorld(face);
            if (room != null) return room;

            for (int d = 0; d < Offsets.Length; d++)
            {
                var probe = Grid.GridToWorld(GridPos + Offsets[d]);
                room = _pressure.RoomAtWorld(probe);
                if (room != null) return room;
            }
            return null;
        }

        private static readonly Vector3Int[] Offsets =
        {
            new(0, 1, 0), new(0, -1, 0),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 0, 1), new(0, 0, -1),
        };

        // ── Screen telemetry ───────────────────────────────────────────────────
        public string SourceName => string.IsNullOrWhiteSpace(blockName) ? "Air Vent" : blockName;
        public string DataCategory => "Life Support";

        public string GetDisplayData()
        {
            if (!Enabled) return "AIR VENT\nDISABLED";
            var room = ServicedRoom;
            if (room == null) return "AIR VENT\nNO SEALED ROOM";

            return "AIR VENT\n"
                 + Status + "\n"
                 + "Pressure " + (room.PressureAtm * 100f).ToString("0") + "%\n"
                 + "Volume " + room.VolumeM3.ToString("0") + " m³\n"
                 + "O₂ " + room.OxygenLitres.ToString("0") + " / " + room.CapacityLitres.ToString("0") + " L";
        }
    }
}
