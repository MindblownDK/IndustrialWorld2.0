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

        [Header("Visuals")]
        [Tooltip("Show a physical drone flying the route. Purely cosmetic - the delivery is " +
                 "identical either way, so this can be switched off on a busy base.")]
        public bool showDrone = true;

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

        // ── Upgrades ────────────────────────────────────────────────────────
        // The port reuses the SAME universal modules the machines take
        // (FurnaceUpgradeItem, authored by setup step 75), so the player does not learn a
        // second upgrade economy for one block. The two multipliers are simply read against
        // what a drone cares about:
        //
        //   Speed Module      -> speedMultiplier      -> the drone flies faster
        //   Efficiency Module -> efficiencyMultiplier -> the drone carries more per trip
        //
        // Efficiency is inverted on purpose. On a machine it is a power multiplier below 1
        // (x0.8 = draws less), so for capacity it is read as "how much less does each item
        // cost you", i.e. capacity scales by 1/efficiency. A x0.8 module therefore gives
        // x1.25 payload, which matches the speed module's feel.

        [Tooltip("Upgrade modules fitted to this port. Accepts the universal Machine Speed and " +
                 "Machine Efficiency modules.")]
        public ItemContainer upgrades;

        [Tooltip("How many upgrade modules this port can hold.")]
        [Min(1)] public int upgradeSlots = 2;

        /// <summary>Flight-speed multiplier from fitted modules. 1 when nothing is fitted.</summary>
        public float SpeedMultiplier { get; private set; } = 1f;

        /// <summary>Payload multiplier from fitted modules. 1 when nothing is fitted.</summary>
        public float CapacityMultiplier { get; private set; } = 1f;

        /// <summary>Speed modules fitted, for the panel readout and the save.</summary>
        public int speedLevel;

        /// <summary>Capacity (efficiency) modules fitted, for the panel readout and the save.</summary>
        public int capacityLevel;

        /// <summary>The effective flight speed after upgrades.</summary>
        public float EffectiveSpeed => Mathf.Max(1f, droneSpeed * SpeedMultiplier);

        /// <summary>The effective payload after upgrades.</summary>
        public int EffectivePayload => Mathf.Max(1, Mathf.RoundToInt(payloadPerTrip * CapacityMultiplier));

        /// <summary>Create the upgrade container if it does not exist yet.</summary>
        public void EnsureContainers()
        {
            if (upgrades == null) upgrades = new ItemContainer("Upgrades", upgradeSlots);
            else if (upgrades.Size != upgradeSlots) upgrades.Resize(upgradeSlots);
        }

        /// <summary>
        /// Recompute the multipliers from the fitted modules. Mirrors the furnace: each module
        /// multiplies, and a stack of them multiplies per unit.
        /// </summary>
        public void RecalculateUpgrades()
        {
            EnsureContainers();

            float speed = 1f, eff = 1f;
            int speedCount = 0, capCount = 0;

            for (int i = 0; i < upgrades.Size; i++)
            {
                var slot = upgrades.GetSlot(i);
                if (slot.IsEmpty) continue;
                if (slot.item is not FurnaceUpgradeItem u) continue;

                speed *= Mathf.Pow(u.speedMultiplier, slot.count);
                eff   *= Mathf.Pow(u.efficiencyMultiplier, slot.count);

                if (u.speedMultiplier > 1f)      speedCount += slot.count;
                if (u.efficiencyMultiplier < 1f) capCount   += slot.count;
            }

            SpeedMultiplier = speed;
            // Below-1 efficiency means "costs less", which for cargo means "carries more".
            CapacityMultiplier = eff > 0.01f ? 1f / eff : 1f;
            speedLevel    = speedCount;
            capacityLevel = capCount;
        }

        /// <summary>Restore the counts a save recorded, then recompute from the real contents.</summary>
        public void RestoreUpgrades(int savedSpeedLevel, int savedCapacityLevel)
        {
            speedLevel    = savedSpeedLevel;
            capacityLevel = savedCapacityLevel;
            RecalculateUpgrades();   // the container is the truth; the counts are a readout
        }

        /// <summary>Restore lifetime counters from a save.</summary>
        public void RestoreTotals(int trips, int items)
        {
            TripsCompleted = Mathf.Max(0, trips);
            ItemsDelivered = Mathf.Max(0, items);
        }

        /// <summary>
        /// Put a saved flight back in the air. The cargo left its source chests before the
        /// save was written, so without this the reload would destroy real items.
        /// </summary>
        public void RestoreFlight(DronePort partner, ItemDefinition item, int count,
                                  float remaining, float total)
        {
            if (item == null || count <= 0) return;

            CurrentPartner  = partner;
            CargoItem       = item;
            CargoCount      = count;
            FlightTotal     = Mathf.Max(0.1f, total);
            FlightRemaining = Mathf.Clamp(remaining, 0.1f, FlightTotal);
            // A save taken after touchdown must not deliver the same payload a second time.
            CargoDelivered  = FlightRemaining <= FlightTotal * 0.5f;
            SyncPowerDraw();

            CargoFrom = transform.position;
            CargoTo   = partner != null ? partner.NetworkPosition : transform.position;

            RetireDrone();
            if (partner != null)
                _drone = TransportDrone.Spawn(this, CargoFrom, CargoTo, item);
        }

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

        /// <summary>
        /// How far through the round trip the drone is, 0 to 1. The visual drone reads this
        /// rather than keeping its own clock, so the model can never drift from the delivery.
        /// </summary>
        public float FlightProgress =>
            FlightTotal > 0f ? Mathf.Clamp01(1f - (FlightRemaining / FlightTotal)) : 0f;

        /// <summary>The visual drone for the current trip, when one is being shown.</summary>
        private TransportDrone _drone;

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
            EnsureContainers();
            RecalculateUpgrades();
        }

        private void OnEnable()
        {
            DroneNetwork.EnsureInstance();
            DroneNetwork.Instance?.Register(this);
            LastKnownPosition = transform.position;
        }

        // NOTE: deliberately NOT unregistering in OnDisable.
        //
        // The world streams: chunks outside the view distance (32 m per chunk, 6-8 chunks,
        // so roughly 192-256 m) are unloaded, which disables the blocks inside them. A port
        // 400 m away is therefore disabled almost all the time. Dropping it from the network
        // on disable meant the long link the block advertises could never actually exist --
        // the far port vanished from the list long before 400 m.
        //
        // Registration now lasts until the block is genuinely destroyed, so a route survives
        // the far end being streamed out. A port that cannot currently reach its chests is
        // reported as dormant rather than deleted.
        /// <summary>
        /// Where this port is, remembered from the last time it was loaded. Distance checks
        /// use it so a streamed-out port keeps a meaningful position instead of reading as
        /// wherever its unloaded transform happens to sit.
        /// </summary>
        public Vector3 LastKnownPosition { get; private set; }

        /// <summary>The port's position, valid whether or not its chunk is currently loaded.</summary>
        public Vector3 NetworkPosition =>
            isActiveAndEnabled ? transform.position : LastKnownPosition;

        /// <summary>
        /// True when the port's chunk is loaded, so it can actually see its chests and fly.
        /// A dormant port keeps its link registered but cannot load or unload cargo.
        /// </summary>
        public bool IsLoaded => isActiveAndEnabled;

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
            float distance = Vector3.Distance(NetworkPosition, other.NetworkPosition);
            return (distance * 2f) / EffectiveSpeed + handlingSeconds * 2f;
        }

        /// <summary>
        /// Launch a drone carrying <paramref name="count"/> of <paramref name="item"/> to
        /// <paramref name="destination"/>. The stock has already been taken out of the source
        /// chests by the network, so the cargo lives on the drone until it lands — it is never
        /// duplicated and never silently lost.
        /// </summary>
        public void Dispatch(DronePort destination, ItemDefinition item, int count, float tripSeconds,
                             Vector3? visualFrom = null, Vector3? visualTo = null)
        {
            CurrentPartner   = destination;
            CargoItem        = item;
            CargoCount       = count;
            FlightTotal      = Mathf.Max(0.1f, tripSeconds);
            FlightRemaining  = FlightTotal;
            CargoDelivered   = false;
            SyncPowerDraw();

            // Cosmetic only, and allowed to fail: if the visual cannot be created the
            // delivery is completely unaffected.
            // The drone flies the CARGO's route (chest to chest) when the network supplies it,
            // falling back to the ports when it cannot name a chest.
            CargoFrom = visualFrom ?? transform.position;
            CargoTo   = visualTo   ?? (destination != null ? destination.NetworkPosition : transform.position);

            RetireDrone();
            if (destination != null)
                _drone = TransportDrone.Spawn(this, CargoFrom, CargoTo, item);
        }

        /// <summary>Where the current payload was picked up. Visual only.</summary>
        public Vector3 CargoFrom { get; private set; }

        /// <summary>Where the current payload is being taken. Visual only.</summary>
        public Vector3 CargoTo { get; private set; }

        /// <summary>
        /// True once the cargo has been handed over at the destination. The drone still has
        /// its return leg to fly, so the flight is not finished — this only stops the payload
        /// being delivered twice.
        /// </summary>
        public bool CargoDelivered { get; private set; }

        /// <summary>
        /// Advance the flight. Returns true on the tick the drone TOUCHES DOWN at the far
        /// end, which is the moment the items should appear in the destination chest — not
        /// when the drone gets home. The timer covers the whole round trip, so the outbound
        /// leg ends at the halfway mark, exactly where the visual drone releases its crate.
        /// Delivering at the end of the timer instead made the items land while the drone was
        /// already most of the way back, which is the out-of-sync transfer.
        ///
        /// Unpowered ports still fly a drone that is already airborne.
        /// </summary>
        public bool TickFlight(float dt)
        {
            if (!IsInFlight) return false;

            FlightRemaining -= dt;

            // Touchdown at the far end: half the round trip has elapsed.
            if (!CargoDelivered && FlightRemaining <= FlightTotal * 0.5f)
            {
                CargoDelivered = true;
                return true;
            }

            if (FlightRemaining > 0f) return false;

            // Home again with an empty hold: end the trip quietly.
            FlightRemaining = 0f;
            SyncPowerDraw();
            if (!CargoDelivered) { CargoDelivered = true; return true; }   // safety net

            CompleteTrip(0);
            return false;
        }

        /// <summary>
        /// The outbound leg is done and the cargo has been handed over; let the drone fly
        /// home empty instead of ending the trip on the spot.
        /// </summary>
        public void BeginReturnLeg(int delivered)
        {
            TripsCompleted++;
            ItemsDelivered += delivered;
            CargoItem  = null;
            CargoCount = 0;
            // CurrentPartner and the timer are deliberately left alone: the drone still has
            // to fly back, and FlightRemaining is what carries it there.
            if (FlightRemaining <= 0f) CompleteTrip(0);
        }

        /// <summary>
        /// The drone landed but <paramref name="stuck"/> units had nowhere to go at either
        /// end. Keep them on the manifest and retry shortly, rather than deleting real items.
        /// The trip is not counted as completed because the cargo has not been delivered.
        /// </summary>
        public void HoldCargo(int stuck)
        {
            CargoCount = Mathf.Max(0, stuck);
            if (CargoCount <= 0) { CompleteTrip(0); return; }

            // Re-arm the touchdown test: the retry window is treated as a fresh outbound leg
            // so TickFlight fires again when it elapses. Without clearing the flag the drone
            // would sit holding the cargo forever.
            CargoDelivered  = false;
            FlightTotal     = RetrySeconds * 2f;
            FlightRemaining = FlightTotal;
            SyncPowerDraw();
        }

        /// <summary>Remove the visual drone, if there is one.</summary>
        private void RetireDrone()
        {
            if (_drone != null) { _drone.Retire(); _drone = null; }
        }

        /// <summary>A port torn down mid-flight must not leave its drone behind.</summary>
        private void OnDestroy()
        {
            DroneNetwork.Instance?.Unregister(this);
            RetireDrone();
        }

        /// <summary>How long a drone waits before retrying an unloadable payload.</summary>
        public const float RetrySeconds = 5f;

        /// <summary>True when the drone has landed but is still holding cargo it could not unload.</summary>
        public bool IsBlocked => CargoCount > 0 && CargoItem != null && FlightRemaining > 0f && CurrentPartner != null;

        /// <summary>Clear the manifest once the cargo has been handed over.</summary>
        public void CompleteTrip(int delivered)
        {
            RetireDrone();
            CargoDelivered = false;
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
