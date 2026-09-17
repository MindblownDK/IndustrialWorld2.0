// Assets/Scripts/VoxelEngine/Transport/DronePort.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Simulation;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// A powered logistics relay that carries items BEYOND the wireless range.
    ///
    /// <see cref="LogisticsNetwork"/> deliberately has a hard radius (48 m): stock teleporting
    /// across a whole world would make distance meaningless. That left the roadmap's real
    /// problem open — an outpost further away than that is simply unreachable, and the player
    /// has to hand-carry everything.
    ///
    /// A Drone Port solves it without dissolving distance. Two ports pair up over a much
    /// longer range, and a drone physically flies the gap: it leaves with a payload, takes
    /// real time in transit, lands, unloads, and returns. So long-distance supply costs power,
    /// a round trip and a pair of blocks, rather than being free.
    ///
    /// How it fits the existing network:
    /// <list type="bullet">
    ///   <item>A port is not itself a chest. It serves the logistic chests already in ITS OWN
    ///         wireless range, which keeps one concept — a chest asks, the network answers —
    ///         and makes a port a bridge between two local networks rather than a third
    ///         kind of storage.</item>
    ///   <item>The port reads unmet demand from requesters near it, finds a paired port whose
    ///         local providers can cover it, and flies the difference over.</item>
    ///   <item>Power gates dispatch only. A drone already in the air completes its trip, so a
    ///         brown-out strands nothing permanently.</item>
    /// </list>
    ///
    /// Every port registers with <see cref="DroneNetwork"/>, which owns the pairing and the
    /// tick, mirroring how LogisticsNetwork owns the chest fulfilment pass.
    /// </summary>
    [DefaultExecutionOrder(60)]
    [RequireComponent(typeof(PowerConsumer))]
    public class DronePort : MonoBehaviour, IPowerConsumer
    {
        [Header("Identity")]
        [Tooltip("Shown in the port UI and used to label the route.")]
        public string portName = "Drone Port";

        [Header("Power")]
        [Tooltip("Watts drawn while the port is idle and listening.")]
        public float idleWatts = 20f;
        [Tooltip("Extra watts drawn while a drone of this port is in flight.")]
        public float flightWatts = 140f;

        [Header("Range and capacity")]
        [Tooltip("How far this port can reach another port, in metres. Far beyond the wireless chest range.")]
        public float linkRange = 400f;
        [Tooltip("Items carried per round trip.")]
        [Min(1)] public int payloadPerTrip = 64;
        [Tooltip("Metres per second the drone travels. The round trip is priced from this.")]
        [Min(1f)] public float droneSpeed = 18f;
        [Tooltip("Seconds spent loading at the source and unloading at the destination.")]
        [Min(0f)] public float handlingSeconds = 2f;

        /// <summary>The chest range this port serves, matching the wireless network's own radius.</summary>
        public const float ServiceRadius = LogisticsNetwork.DefaultRange;

        private PowerConsumer _power;

        // ── Flight state ────────────────────────────────────────────────────
        /// <summary>The port this drone is currently serving, or null when idle.</summary>
        public DronePort CurrentPartner { get; private set; }
        /// <summary>What the drone is carrying this trip.</summary>
        public ItemDefinition CargoItem { get; private set; }
        /// <summary>How many units are aboard.</summary>
        public int CargoCount { get; private set; }
        /// <summary>Seconds left before the drone lands and unloads.</summary>
        public float FlightRemaining { get; private set; }
        /// <summary>Total seconds this trip was priced at, so the UI can show progress.</summary>
        public float FlightTotal { get; private set; }
        /// <summary>True while a drone is away from this port.</summary>
        public bool IsInFlight => FlightRemaining > 0f;

        /// <summary>Round trips completed since the port was placed. Shown in the UI.</summary>
        public int TripsCompleted { get; private set; }
        /// <summary>Items delivered since the port was placed.</summary>
        public int ItemsDelivered { get; private set; }

        // ── IPowerConsumer ──────────────────────────────────────────────────
        public bool IsPowered => _power != null && _power.IsPowered;

        public float WattsPerSecond
        {
            get => IsInFlight ? idleWatts + flightWatts : idleWatts;
            set { idleWatts = value; if (_power != null) _power.wattsPerSecond = value; }
        }

        private void Awake()
        {
            EnsurePower();
        }

        private void OnEnable()
        {
            DroneNetwork.EnsureInstance();
            DroneNetwork.Instance?.Register(this);
        }

        private void OnDisable()
        {
            DroneNetwork.Instance?.Unregister(this);
        }

        private void EnsurePower()
        {
            if (_power == null)
            {
                _power = GetComponent<PowerConsumer>();
                if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            }
            _power.wattsPerSecond = idleWatts;
            _power.connectRadius = 4f;
        }

        /// <summary>Keep the power draw honest as the drone launches and lands.</summary>
        private void SyncPowerDraw()
        {
            if (_power == null) return;
            float want = IsInFlight ? idleWatts + flightWatts : idleWatts;
            if (!Mathf.Approximately(_power.wattsPerSecond, want)) _power.wattsPerSecond = want;
        }

        // ── Dispatch ────────────────────────────────────────────────────────

        /// <summary>
        /// Seconds a round trip to <paramref name="other"/> costs: out and back at
        /// <see cref="droneSpeed"/>, plus handling at both ends.
        /// </summary>
        public float RoundTripSeconds(DronePort other)
        {
            if (other == null) return 0f;
            float distance = Vector3.Distance(transform.position, other.transform.position);
            return (distance * 2f) / Mathf.Max(1f, droneSpeed) + handlingSeconds * 2f;
        }

        /// <summary>
        /// Launch a drone carrying <paramref name="count"/> of <paramref name="item"/> to
        /// <paramref name="destination"/>. The stock has already been taken out of the source
        /// chests by the network, so the cargo lives on the drone until it lands — it is never
        /// duplicated and never silently lost.
        /// </summary>
        public void Dispatch(DronePort destination, ItemDefinition item, int count, float tripSeconds)
        {
            CurrentPartner   = destination;
            CargoItem        = item;
            CargoCount       = count;
            FlightTotal      = Mathf.Max(0.1f, tripSeconds);
            FlightRemaining  = FlightTotal;
            SyncPowerDraw();
        }

        /// <summary>
        /// Advance the flight. Returns true on the tick the drone lands, so the network can
        /// hand the cargo over. Unpowered ports still fly a drone that is already airborne.
        /// </summary>
        public bool TickFlight(float dt)
        {
            if (!IsInFlight) return false;
            FlightRemaining -= dt;
            if (FlightRemaining > 0f) return false;

            FlightRemaining = 0f;
            SyncPowerDraw();
            return true;
        }

        /// <summary>
        /// The drone landed but <paramref name="stuck"/> units had nowhere to go at either
        /// end. Keep them on the manifest and retry shortly, rather than deleting real items.
        /// The trip is not counted as completed because the cargo has not been delivered.
        /// </summary>
        public void HoldCargo(int stuck)
        {
            CargoCount      = Mathf.Max(0, stuck);
            FlightRemaining = CargoCount > 0 ? RetrySeconds : 0f;
            FlightTotal     = Mathf.Max(FlightTotal, FlightRemaining);
            if (CargoCount <= 0) CompleteTrip(0);
            SyncPowerDraw();
        }

        /// <summary>How long a drone waits before retrying an unloadable payload.</summary>
        public const float RetrySeconds = 5f;

        /// <summary>True when the drone has landed but is still holding cargo it could not unload.</summary>
        public bool IsBlocked => CargoCount > 0 && CargoItem != null && FlightRemaining > 0f && CurrentPartner != null;

        /// <summary>Clear the manifest once the cargo has been handed over.</summary>
        public void CompleteTrip(int delivered)
        {
            TripsCompleted++;
            ItemsDelivered += delivered;
            CurrentPartner = null;
            CargoItem      = null;
            CargoCount     = 0;
            FlightTotal    = 0f;
            SyncPowerDraw();
        }

        // ── Local chest service ─────────────────────────────────────────────

        /// <summary>
        /// Every logistic chest within this port's service radius. The port bridges the two
        /// local networks rather than owning storage of its own.
        /// </summary>
        public void CollectLocalChests(List<Chest> providers, List<Chest> requesters)
        {
            providers?.Clear();
            requesters?.Clear();

            var net = LogisticsNetwork.Instance;
            if (net == null) return;

            float radiusSqr = ServiceRadius * ServiceRadius;
            Vector3 origin = transform.position;

            foreach (var chest in net.AllChests)
            {
                if (chest == null || chest.container == null) continue;
                if ((chest.transform.position - origin).sqrMagnitude > radiusSqr) continue;

                if (chest.SuppliesNetwork)     providers?.Add(chest);
                if (chest.RequestsFromNetwork) requesters?.Add(chest);
            }
        }
    }
}
