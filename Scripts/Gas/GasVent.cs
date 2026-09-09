// Assets/Scripts/VoxelEngine/Gas/GasVent.cs
//
// GAS VENT — the far end of a gas run: the place gas leaves the ship.
//
// Until now a gas pipe could only be connected to a tank, which turned every produced
// gas into a storage obligation. An engine's exhaust, a boiler's steam or a hydrogen
// skimmer's surplus all had to be absorbed by a vessel big enough to hold it, and a run
// that reached no free tank simply stopped taking gas at the source. This block is the
// way out: bolt it to the end of a line and whatever arrives is destroyed.
//
//   OPEN       the louvres are up. UNPOWERED it is a sleeve with nothing behind it and
//              gas drifts out at the draft rate, so a stack still clears its line while
//              the plant runs on its own. With grid power the extractor spins and the
//              run is cleared at the forced rate. A brownout drops it back to draft by
//              itself — a power cut must never trap gas in the engine room.
//   SHUT       the louvres close. The run backs up exactly as it would against a full
//              tank, which is how you hold exhaust in the line for a purge.
//
// A vent is a one-way sink: it accepts gas and never supplies it, so drawing from a run
// that ends in a vent loses nothing to the vent. Set into the hull it goes overboard; set
// into a compartment it moves gas from the pipes into that room's air instead, which is
// what a cabin blower is supposed to do.
//
// It deliberately shares no logic with the AIR VENT (Pressure/GridAirVent): that one
// charges a room with oxygen, this one throws gas away.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Pressure;

namespace VoxelEngine.Gas
{
    public class GasVent : GridBlock, IGridDataProvider, IAirtightBlock
    {
        [Header("Vent")]
        [Tooltip("Shut closes the louvres: the run still fills, nothing leaves.")]
        public bool open = true;

        [Tooltip("Litres per second the open sleeve destroys by draft alone — no power, no fan.")]
        [Range(0f, 500f)] public float draftFlowLitresPerSecond = 40f;

        [Tooltip("Litres per second the extractor moves while the grid is supplying it.")]
        [Range(1f, 12000f)] public float forcedFlowLitresPerSecond = 1400f;

        [Tooltip("Cap the sleeve against the compartment it opens into, so blowing a whole engine room full of exhaust in one second cannot outrun the room model. On the hull, where there is no room, it changes nothing.")]
        public bool autoScaleFlow = true;

        [Header("Power")]
        [Tooltip("Standby draw for the louvre actuator and the flame screen.")]
        public float idleWatts = 3f;
        public float activeWatts = 150f;

        [Header("Airtight")]
        [Tooltip("The sleeve seals the cell it sits in, so a hull lined with vents stays pressurable.")]
        public bool airtight = true;

        /// <summary>Litres destroyed per second over the last tick (smoothed for the readout).</summary>
        public float CurrentFlow { get; private set; }
        /// <summary>Every litre thrown overboard since the world was loaded. The save layer
        /// writes it back on load (it lives in a different assembly), so the setter is open
        /// even though nothing at runtime should treat a counter as a knob.</summary>
        public float TotalDumped { get; set; }
        /// <summary>The last gas it handled — what a technician reads off the plate.</summary>
        public GasType LastGas { get; private set; } = GasType.None;
        /// <summary>0 idle, 1 extractor at full speed. Drives the fan blade.</summary>
        public float Flow01 { get; private set; }
        public string Status { get; private set; } = "Draft";

        public bool SealsAir => airtight;
        public bool HasPower => Enabled && Grid != null && Grid.HasPower;
        public bool IsOpen => Enabled && open;
        public bool IsExtracting => IsOpen && HasPower;
        public override float PowerDraw => !Enabled ? 0f : (IsExtracting ? activeWatts : idleWatts);

        /// <summary>Flow the vent can burn right now, in litres per second.</summary>
        public float RatedFlow => HasPower ? forcedFlowLitresPerSecond
                                           : Mathf.Max(0f, draftFlowLitresPerSecond);

        /// <summary>The sealed compartment this sleeve opens into, if any. On the hull the
        /// gas is simply gone, so there is nothing to scale against.</summary>
        public GridRoom RoomSide => GridPressureSystem.ConcealedRoom(this);

        /// <summary>Flow the vent really moves this tick: the rated figure, held down to what
        /// the compartment it feeds can absorb per the shared ventilation rules.</summary>
        public float EffectiveFlow => ResolveFlow(RoomSide);

        private float ResolveFlow(GridRoom room)
        {
            float rated = RatedFlow;
            if (!autoScaleFlow || room == null || !room.IsSealed) return rated;
            float cap = room.CapacityLitres * (VentilationRules.CeilingAirChangesPerMinute / 60f);
            return cap > 0.0001f ? Mathf.Min(rated, cap) : rated;
        }

        /// <summary>Air changes per minute this sleeve would produce in the room it feeds.</summary>
        public float RoomAirChangesPerMinute(GridRoom room)
            => room == null || !room.IsSealed
                ? 0f
                : VentilationRules.AirChangesPerMinute(EffectiveFlow, room.CapacityLitres);

        private Transform _fanHub;
        private float _fanSpeed;
        private float _pending;
        private float _window;

        // ── The one call the network makes ─────────────────────────────────
        /// <summary>
        /// Destroys up to <paramref name="litres"/> and returns how much it took. Anything
        /// it cannot pass this tick is simply gone from the run, so unlike a tank it never
        /// pushes gas back at the producer.
        /// </summary>
        public float Accept(GasType type, float litres)
        {
            if (litres <= 0.0001f || !IsOpen || type == GasType.None) return 0f;
            float capacity = RatedFlow * Time.deltaTime;
            if (capacity <= 0.0001f) { Status = "Blocked"; return 0f; }

            // A closed or stalled vent still has to report the gas it is holding back,
            // so the remainder is what actually left.
            float taken = Mathf.Min(litres, capacity);
            if (taken <= 0.0001f) return 0f;

            TotalDumped += taken;
            LastGas = type;
            _pending += taken;

            // Into the sealed compartment the vent sits in, otherwise straight overboard —
            // and overboard is a no-op by definition, so nothing is recorded outdoors.
            var room = GridPressureSystem.ConcealedRoom(this);
            if (room != null)
            {
                // A room-side sleeve is metered by what the compartment can absorb, so the
                // pipe flow and the room flow are the same number and gas never piles up as
                // an unhandled remainder. Overboard it is destroyed outright, which is the
                // whole point of the block, so no cap applies there.
                float roomTaken = Mathf.Min(taken, room.IsSealed ? ResolveFlow(room) * Time.deltaTime : taken);
                if (roomTaken > 0.0001f)
                {
                    if (type == GasType.Oxygen) room.AddOxygen(roomTaken);
                    else if (type == GasType.ExhaustGas) room.ReportExhaust(ExhaustStreamTemperatureC);
                }
            }

            return taken;
        }

        /// <summary>An arbitrary gas temperature for the foul-air report; a real exhaust is
        /// far hotter than a hull sleeve, but the room only cares that it is unpleasant.</summary>
        private const float ExhaustStreamTemperatureC = 180f;

        /// <summary>True when this vent is open and would take that gas.</summary>
        public bool Accepts(GasType type) => IsOpen && type != GasType.None;

        private void Awake()
        {
            if (_fanHub == null)
                _fanHub = transform.Find("Generated_Visuals/Generated_VentFanHub")
                          ?? transform.Find("Generated_VentFanHub");
            if (_fanHub == null) return;   // prefab has no fan spine authored
        }

        private void Update()
        {
            if (!Enabled)
            {
                CurrentFlow = 0f; Flow01 = 0f; Status = "Disabled";
                _pending = 0f; _window = 0f;
                return;
            }

            float dt = Time.deltaTime;
            _window += dt;
            if (_window >= 0.25f)
            {
                CurrentFlow = _pending / _window;
                _pending = 0f; _window = 0f;
                Status = !open ? "Shut"
                    : RatedFlow <= 0.001f ? "Blocked"
                    : HasPower ? "Extracting" : "Draft";
            }
            Flow01 = Mathf.Clamp01(CurrentFlow / Mathf.Max(1f, Mathf.Min(forcedFlowLitresPerSecond, Mathf.Max(1f, EffectiveFlow))));

            float target = IsExtracting ? Mathf.Lerp(90f, 900f, Flow01) : 0f;
            _fanSpeed = Mathf.Lerp(_fanSpeed, target, 1f - Mathf.Exp(-2.5f * dt));
            if (_fanHub != null && _fanSpeed > 0.5f)
                _fanHub.Rotate(0f, _fanSpeed * dt, 0f, Space.Self);
        }

        /// <summary>Set true when a block breaks so the flow visual can be cleared.</summary>
        public override void OnRemoved()
        {
            base.OnRemoved();
            CurrentFlow = 0f; Flow01 = 0f;
        }

        // ── Readout ────────────────────────────────────────────────────────
        public string SourceName => "Gas Vent";
        public string DataCategory => "Gas";

        public string GetDisplayData()
        {
            string state = !Enabled ? "DISABLED" : !open ? "SHUT"
                : HasPower ? "EXTRACTING" : RatedFlow > 0.001f ? "DRAFT ONLY" : "BLOCKED";
            var room = RoomSide;
            float cap = room != null && room.IsSealed ? room.CapacityLitres : 0f;
            return $"VENT {state}\n" +
                   $"OUT {CurrentFlow:0} L/s · {TotalDumped:0} L\n" +
                   (cap > 0f ? $"ROOM SIDE {EffectiveFlow:0} L/s · {VentilationRules.AirChangesPerMinute(EffectiveFlow, cap):0.0} ACP\n"
                             : "OVERBOARD — no compartment\n") +
                   (LastGas != GasType.None ? $"LAST {LastGas}\n" : string.Empty) +
                   (HasPower ? "FAN ONLINE" : "NO POWER — LOUVRES OPEN");
        }
    }
}
