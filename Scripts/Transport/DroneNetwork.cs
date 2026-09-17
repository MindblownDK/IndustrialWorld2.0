// Assets/Scripts/VoxelEngine/Transport/DroneNetwork.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Owns the drone ports: pairing, dispatch and delivery.
    ///
    /// This is the long-distance half of logistics. <see cref="LogisticsNetwork"/> answers a
    /// request instantly if a provider is within 48 m; anything further away was previously
    /// unreachable. A pair of <see cref="DronePort"/>s bridges that gap by flying items over
    /// real time, so distance still costs something.
    ///
    /// The pass is deliberately demand-driven and runs from the DESTINATION end: a port looks
    /// at what the requesters around it still want after the local network has done its work,
    /// then asks the other ports whether their local providers can cover the difference. That
    /// ordering means a drone is only ever launched against a real shortfall, and stock that
    /// could have been supplied locally never takes a flight it did not need.
    ///
    /// Mirrors the LogisticsNetwork design: one singleton on a timer, explicit registration,
    /// and transfers metered rather than instantaneous.
    /// </summary>
    [DefaultExecutionOrder(61)]
    public class DroneNetwork : MonoBehaviour
    {
        public static DroneNetwork Instance { get; private set; }

        /// <summary>Seconds between dispatch passes. Slower than the chest network: a flight is an event, not a trickle.</summary>
        public const float TickInterval = 2f;

        private readonly List<DronePort> _ports = new();

        // Scratch lists reused every pass so a busy network does not allocate per tick.
        private readonly List<Chest> _localProviders  = new();
        private readonly List<Chest> _localRequesters = new();
        private readonly List<Chest> _remoteProviders = new();
        private readonly List<Chest> _remoteRequesters = new();

        /// <summary>Flights launched by the last pass. Read by the port UI.</summary>
        public int LastPassDispatched { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<DroneNetwork>();
            if (existing != null) { Instance = existing; return; }
            var go = new GameObject("DroneNetwork");
            Instance = go.AddComponent<DroneNetwork>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Registration ────────────────────────────────────────────────────

        public void Register(DronePort port)
        {
            if (port == null || _ports.Contains(port)) return;
            _ports.Add(port);
        }

        public void Unregister(DronePort port)
        {
            if (port == null) return;
            _ports.Remove(port);
        }

        public int PortCount { get { Prune(); return _ports.Count; } }

        /// <summary>
        /// Live view of the registered ports, for readers that need the whole network rather
        /// than a count - the logistics map draws every route from this. Pruned first so a
        /// reader never sees a destroyed port.
        /// </summary>
        public IReadOnlyList<DronePort> Ports { get { Prune(); return _ports; } }

        // ── Tick ────────────────────────────────────────────────────────────

        private float _timer;

        private void Update()
        {
            float dt = Time.deltaTime;

            // Flights advance every frame, independent of the dispatch timer, so a landing is
            // never delayed by up to a whole tick.
            AdvanceFlights(dt);

            _timer += dt;
            if (_timer < TickInterval) return;
            _timer = 0f;
            RunDispatchPass();
        }

        /// <summary>
        /// Move every airborne drone along, and unload the ones that land. Cargo is inserted
        /// into the destination's local requesters; anything they cannot take is returned to
        /// the destination's own providers so a payload is never destroyed.
        /// </summary>
        private void AdvanceFlights(float dt)
        {
            Prune();
            foreach (var port in _ports)
            {
                if (port == null || !port.TickFlight(dt)) continue;

                var destination = port.CurrentPartner;
                var item  = port.CargoItem;
                int count = port.CargoCount;

                if (destination == null || item == null || count <= 0)
                {
                    port.BeginReturnLeg(0);
                    continue;
                }

                int delivered = Deliver(destination, item, count);

                // Undeliverable remainder goes into any storage at the destination rather
                // than evaporating.
                int leftover = count - delivered;
                if (leftover > 0) delivered += StoreLeftover(destination, item, leftover);

                // Still stuck? Fly it home. The cargo was removed from the source chests at
                // takeoff, so dropping it here would destroy real items — the one outcome a
                // logistics system must never produce. Anything that cannot be placed at
                // EITHER end stays on the manifest and is retried next landing.
                leftover = count - delivered;
                if (leftover > 0)
                {
                    port.CollectLocalChests(_remoteProviders, _remoteRequesters);
                    int returned = StoreAcross(_remoteProviders, item, leftover);
                    if (returned < leftover) returned += StoreAcross(_remoteRequesters, item, leftover - returned);

                    if (returned < leftover)
                    {
                        Debug.LogWarning("[DroneNetwork] " + port.portName + " could not unload " +
                                         (leftover - returned) + " x " + item.displayName +
                                         " at either end; the drone holds it until space appears.");
                        port.HoldCargo(leftover - returned);
                        continue;
                    }
                }

                // Hand over at touchdown and let the drone fly the return leg empty, so the
                // items appear exactly when the visual drone releases them.
                port.BeginReturnLeg(delivered);
            }
        }

        /// <summary>Fill the destination's requesters with the payload, nearest first.</summary>
        private int Deliver(DronePort destination, ItemDefinition item, int count)
        {
            destination.CollectLocalChests(_remoteProviders, _remoteRequesters);

            int delivered = 0;
            foreach (var chest in _remoteRequesters)
            {
                if (delivered >= count) break;
                if (chest == null || chest.container == null) continue;

                int shortfall = chest.ShortfallOf(item);
                if (shortfall <= 0) continue;

                int want = Mathf.Min(shortfall, count - delivered);
                delivered += InsertCounted(chest, item, want);
            }
            return delivered;
        }

        /// <summary>Last resort at the destination: park the remainder in any local chest with room.</summary>
        private int StoreLeftover(DronePort destination, ItemDefinition item, int count)
            => StoreAcross(_remoteProviders, item, count);

        /// <summary>Push as much of an item as will fit into the given chests.</summary>
        private static int StoreAcross(List<Chest> chests, ItemDefinition item, int count)
        {
            int stored = 0;
            foreach (var chest in chests)
            {
                if (stored >= count) break;
                if (chest == null || chest.container == null) continue;
                stored += InsertCounted(chest, item, count - stored);
            }
            return stored;
        }

        /// <summary>
        /// Insert and report what was actually accepted, counted by identity. The container's
        /// own CountOf compares by reference, so it would miscount the moment two assets
        /// describe the same item.
        /// </summary>
        private static int InsertCounted(Chest chest, ItemDefinition item, int count)
        {
            if (count <= 0) return 0;
            int before = CountOf(chest.container, item);
            chest.container.Insert(new ItemStack(item, count));
            return Mathf.Max(0, CountOf(chest.container, item) - before);
        }

        // ── Dispatch pass ───────────────────────────────────────────────────

        /// <summary>
        /// Look for unmet demand at every idle port and launch a drone to cover it. Public so
        /// the UI can force a pass without waiting for the timer.
        /// </summary>
        public void RunDispatchPass()
        {
            Prune();
            LastPassDispatched = 0;
            if (_ports.Count < 2) return;

            foreach (var destination in _ports)
            {
                if (destination == null || destination.IsInFlight) continue;
                if (!destination.IsLoaded) continue;   // its chests are not in memory to fill

                destination.CollectLocalChests(_localProviders, _localRequesters);
                if (_localRequesters.Count == 0) continue;

                if (TryServeDemand(destination)) LastPassDispatched++;
            }
        }

        /// <summary>
        /// Find one item a destination's requesters still want that no local provider can
        /// supply, then launch it from the nearest port that can. Returns true if a drone
        /// left the ground.
        /// </summary>
        private bool TryServeDemand(DronePort destination)
        {
            foreach (var chest in _localRequesters)
            {
                if (chest == null) continue;

                foreach (var item in chest.Requests)
                {
                    if (item == null) continue;

                    int shortfall = chest.ShortfallOf(item);
                    if (shortfall <= 0) continue;

                    // The local wireless network gets first refusal. Only what it cannot
                    // cover is worth a flight.
                    var logistics = LogisticsNetwork.Instance;
                    if (logistics != null && logistics.AvailableFor(chest, item) > 0) continue;

                    if (TryLaunchFor(destination, item, shortfall)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Launch from the nearest in-range, idle, powered port whose local providers hold
        /// <paramref name="item"/>. The cargo is removed from the source chests at takeoff, so
        /// it exists in exactly one place for the whole flight.
        /// </summary>
        private bool TryLaunchFor(DronePort destination, ItemDefinition item, int wanted)
        {
            DronePort best = null;
            float bestSqr = float.MaxValue;
            Vector3 origin = destination.NetworkPosition;

            foreach (var source in _ports)
            {
                if (source == null || source == destination) continue;
                if (source.IsInFlight || !source.IsPowered) continue;
                // A streamed-out port cannot see its chests, so it cannot load a payload.
                // Its link stays registered; it simply cannot be the source right now.
                if (!source.IsLoaded) continue;

                float sqr = (source.NetworkPosition - origin).sqrMagnitude;
                float range = Mathf.Min(source.linkRange, destination.linkRange);
                if (sqr > range * range) continue;
                if (sqr >= bestSqr) continue;

                source.CollectLocalChests(_remoteProviders, _remoteRequesters);
                if (AvailableAcross(_remoteProviders, item) <= 0) continue;

                bestSqr = sqr;
                best = source;
            }

            if (best == null) return false;

            // The destination port pays the power cost of receiving, so it must be powered too.
            if (!destination.IsPowered) return false;

            int payload = Mathf.Min(wanted, best.EffectivePayload);
            best.CollectLocalChests(_remoteProviders, _remoteRequesters);
            int loaded = TakeFrom(_remoteProviders, item, payload, out var sourceChest);
            if (loaded <= 0) return false;

            // Fly the route the CARGO actually takes. The ports are the relay that makes the
            // trip legal, but the items leave a chest and arrive in a chest, so anchoring the
            // drone to the ports made it visibly start and end in the wrong place.
            Vector3 from = sourceChest != null ? sourceChest.transform.position : best.NetworkPosition;
            Vector3 to   = PredictDeliveryPoint(destination, item);

            best.Dispatch(destination, item, loaded, best.RoundTripSeconds(destination), from, to);
            return true;
        }

        /// <summary>Total of an item across a set of chests, matched by identity.</summary>
        private static int AvailableAcross(List<Chest> chests, ItemDefinition item)
        {
            int total = 0;
            foreach (var chest in chests)
            {
                if (chest == null || chest.container == null) continue;
                total += CountOf(chest.container, item);
            }
            return total;
        }

        /// <summary>
        /// Remove up to <paramref name="count"/> of an item from the given chests and report
        /// how much was actually taken. Each chest's OWN item instance is removed, because
        /// ItemContainer.Remove matches by reference.
        /// </summary>
        private static int TakeFrom(List<Chest> chests, ItemDefinition item, int count, out Chest firstSource)
        {
            firstSource = null;
            int taken = 0;
            foreach (var chest in chests)
            {
                if (taken >= count) break;
                if (chest == null || chest.container == null) continue;

                var stocked = FindStocked(chest.container, item);
                if (stocked == null) continue;

                int have = CountOf(chest.container, stocked);
                if (have <= 0) continue;

                int want = Mathf.Min(have, count - taken);
                chest.container.Remove(stocked, want);
                if (firstSource == null) firstSource = chest;   // where the drone visibly lifts off
                taken += want;
            }
            return taken;
        }

        /// <summary>
        /// The chest the payload will most likely land in, used as the drone's visual target.
        /// Only a prediction: the real delivery is decided on landing, so if the situation has
        /// changed by then the items still go wherever they fit. Falls back to the port.
        /// </summary>
        private Vector3 PredictDeliveryPoint(DronePort destination, ItemDefinition item)
        {
            destination.CollectLocalChests(_remoteProviders, _remoteRequesters);
            foreach (var chest in _remoteRequesters)
            {
                if (chest == null || chest.container == null) continue;
                if (chest.ShortfallOf(item) > 0) return chest.transform.position;
            }
            return destination.NetworkPosition;
        }

        /// <summary>The container's own asset instance for an item that matches by identity.</summary>
        private static ItemDefinition FindStocked(ItemContainer container, ItemDefinition item)
        {
            if (container == null || item == null) return null;
            for (int i = 0; i < container.Size; i++)
            {
                var slot = container.GetSlot(i);
                if (!slot.IsEmpty && ItemIdentity.Same(slot.item, item)) return slot.item;
            }
            return null;
        }

        /// <summary>Total count of an item in a container, matched by identity not reference.</summary>
        private static int CountOf(ItemContainer container, ItemDefinition item)
        {
            if (container == null || item == null) return 0;
            int total = 0;
            for (int i = 0; i < container.Size; i++)
            {
                var slot = container.GetSlot(i);
                if (slot.IsEmpty || !ItemIdentity.Same(slot.item, item)) continue;
                total += slot.count;
            }
            return total;
        }

        private void Prune()
        {
            for (int i = _ports.Count - 1; i >= 0; i--)
                if (_ports[i] == null) _ports.RemoveAt(i);
        }

        /// <summary>
        /// How much of an item could reach <paramref name="chest"/> by DRONE: stock held by
        /// providers around any port that links to a port serving this chest. The chest panel
        /// uses it so an item that is genuinely on its way is not reported as unavailable
        /// merely because no provider is within the 48 m wireless radius.
        /// </summary>
        public int AvailableByDroneFor(Chest chest, ItemDefinition item)
        {
            if (chest == null || item == null) return 0;
            Prune();
            if (_ports.Count < 2) return 0;

            float serviceSqr = DronePort.ServiceRadius * DronePort.ServiceRadius;
            int total = 0;

            foreach (var local in _ports)
            {
                if (local == null) continue;
                // Is this port close enough to serve the chest at all?
                if ((local.NetworkPosition - chest.transform.position).sqrMagnitude > serviceSqr) continue;

                foreach (var remote in _ports)
                {
                    if (remote == null || remote == local) continue;
                    float range = Mathf.Min(local.linkRange, remote.linkRange);
                    if ((remote.NetworkPosition - local.NetworkPosition).sqrMagnitude > range * range) continue;
                    if (!remote.IsLoaded) continue;   // its chests are not in memory to count

                    remote.CollectLocalChests(_remoteProviders, _remoteRequesters);
                    total += AvailableAcross(_remoteProviders, item);
                }
            }
            return total;
        }

        /// <summary>Ports within linking range of <paramref name="port"/>. Used by the UI.</summary>
        public int LinkedPortCount(DronePort port) => LinkStatus(port).total;

        /// <summary>
        /// A breakdown of the ports linked to <paramref name="port"/>: how many there are in
        /// total, how many of those have no power, and how many are currently streamed out.
        /// The UI needs the breakdown so "linked but nothing happens" can name its own cause
        /// instead of leaving the player guessing.
        /// </summary>
        public (int total, int unpowered, int dormant) LinkStatus(DronePort port)
        {
            if (port == null) return (0, 0, 0);
            Prune();

            int total = 0, unpowered = 0, dormant = 0;
            foreach (var other in _ports)
            {
                if (other == null || other == port) continue;
                float range = Mathf.Min(port.linkRange, other.linkRange);
                if ((other.NetworkPosition - port.NetworkPosition).sqrMagnitude > range * range) continue;

                total++;
                if (!other.IsPowered) unpowered++;
                else if (!other.IsLoaded) dormant++;
            }
            return (total, unpowered, dormant);
        }
    }
}
