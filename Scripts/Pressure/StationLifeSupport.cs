// Assets/Scripts/VoxelEngine/Pressure/StationLifeSupport.cs
//
// THE LIFE SUPPORT UNIT — what actually fills a station compartment with air.
//
// A sealed room is only half of pressurisation. Without a source, every compartment a
// player builds would read VACUUM forever and the feature would look broken. This is
// the source, and it is deliberately a placed machine rather than an automatic
// property of sealing a room:
//
//   • Air has to be PRODUCED. A station is built in vacuum, so a newly sealed volume
//     starts empty and stays empty until the player supplies it. Assuming a full charge
//     on seal would make the airlock and the hull decorative.
//   • It costs POWER continuously. Holding an atmosphere against vacuum is the ongoing
//     price of living up there, and it gives orbital power generation a real consumer.
//   • It LEAKS. A room slowly loses air, so life support is not a one-off switch the
//     player flips and forgets. Cut the power and the compartment goes stale.
//
// The leak rate is deliberately gentle - losing a compartment should take long enough
// that a player notices the warning and has time to react, not so fast that stepping
// away from a station is punished.

using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Pressure
{
    [DisallowMultipleComponent, RequireComponent(typeof(VoxelEngine.Building.PlacedBlock))]
    public class StationLifeSupport : MonoBehaviour
    {
        [Header("Output")]
        [Tooltip("Litres of oxygen produced per second while powered.")]
        public float litresPerSecond = 32f;

        [Tooltip("Power drawn while actively pressurising. Falls to a tenth when the " +
                 "room is already full, so a finished station is cheap to hold.")]
        public float powerDraw = 900f;

        [Header("Leakage")]
        [Tooltip("Fraction of a room's air lost per second with no life support running. " +
                 "Gentle on purpose: losing a compartment should give the player time to " +
                 "react, not punish them for stepping away.")]
        [Range(0f, 0.05f)] public float leakPerSecond = 0.004f;

        public string Status { get; private set; } = "No sealed room";
        public bool IsRunning { get; private set; }

        /// <summary>The room this unit serves, resolved from its own position.</summary>
        public StationRoom Room { get; private set; }

        private PowerConsumer _power;
        private float _rescanTimer;

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
            if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
        }

        private void OnEnable()
        {
            // A life support unit placed inside a half-built station should start working
            // the moment the last wall closes, so it asks for a re-solve on wake.
            StationRoomSolver.MarkDirty();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // The solver is ticked from here rather than from a manager component so the
            // feature needs no scene wiring: if there is a life support unit in the world,
            // rooms are being solved.
            StationRoomSolver.Tick(dt);

            _rescanTimer -= dt;
            if (_rescanTimer <= 0f)
            {
                _rescanTimer = 0.5f;
                Room = StationRoomSolver.RoomAt(transform.position);
            }

            if (Room == null)
            {
                Stop("Not inside a sealed room");
                return;
            }

            bool hasPower = _power == null || _power.IsPowered;
            if (!hasPower)
            {
                Stop("No power");
                ApplyLeak(Room, dt);
                return;
            }

            if (Room.OxygenLitres >= Room.CapacityLitres)
            {
                // Held, not idle: the unit still runs, just cheaply, and still replaces
                // what leaks away.
                IsRunning = true;
                if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
                Room.OxygenLitres = Room.CapacityLitres;
                Status = $"Pressurised  ·  {Room.VolumeM3:0} m3";
                return;
            }

            IsRunning = true;
            if (_power != null) _power.wattsPerSecond = powerDraw;

            Room.OxygenLitres = Mathf.Min(Room.CapacityLitres,
                Room.OxygenLitres + litresPerSecond * dt);

            Status = $"Pressurising  ·  {Room.Fill01 * 100f:0}%  ·  {Room.VolumeM3:0} m3";
        }

        private void Stop(string reason)
        {
            IsRunning = false;
            Status = reason;
            if (_power != null) _power.wattsPerSecond = powerDraw * 0.1f;
        }

        private void ApplyLeak(StationRoom room, float dt)
        {
            if (room == null || room.OxygenLitres <= 0f) return;
            room.OxygenLitres = Mathf.Max(0f,
                room.OxygenLitres - room.CapacityLitres * leakPerSecond * dt);
        }
    }
}
