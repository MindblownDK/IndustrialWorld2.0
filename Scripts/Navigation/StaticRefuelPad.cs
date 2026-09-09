// Assets/Scripts/VoxelEngine/Navigation/StaticRefuelPad.cs
//
// THE STATIC REFUEL PAD — a refuel island for a base that has no grid to hang a connector on.
//
// 9.35.0-dev put a connector on a grid, which is right for a station that is itself a ship and wrong
// for the ordinary case: a strip of concrete beside a road, next to a tank farm, under a windmill. A
// ground base is `PowerNetwork` + `FluidNetwork` + `GasNetwork` + item pipes, and it has no
// `GridEntity` anywhere near them. So this block joins those four graphs as a member of each of them,
// and it is the only place in the game where the world's plumbing and a ship's plumbing meet on purpose.
//
// WHAT IT IS, NETWORK BY NETWORK — because "place it next to a tank" was the first draft and it was
// wrong: a block that has to be near something is not connected to it, and a player who ran a cable
// to a pad that ignored the cable would be right to call that a bug.
//
//   • POWER — it registers as a `PowerConsumer` (`PowerNode` → `PowerNetworkManager`), so any cable
//     or wire run touches it like any other machine; `PowerCable.CanLinkTo` explicitly lets a consumer
//     tap a nearby run. Its demand is the watts it is actually serving: 0 W idle, rated while pumping.
//     `PowerConsumer.IsPowered` is all-or-nothing by this engine's own rule (the manager marks a
//     consumer powered only when the network served its whole demand), and that is the honesty wanted
//     here — a base that cannot sustain 24 kW refuses the leg instead of trickle-charging a frigate.
//   • LIQUID FUEL — it carries its own `WaterTank` child, which IS a `FluidNode` and registers with
//     `FluidNetworkManager`. So base pumps fill the pad through the pipe graph, exactly as they fill a
//     tank farm, and the pad empties its own tank into a visitor. `LiquidTankClassicAdapter` is the
//     proof this works: that is how a grid tank got onto the same graph in the first place.
//   • HYDROGEN — it carries its own `GasTank` child with a small collider, because `GasPipe` finds
//     world endpoints by probing colliders (`GetComponentInParent<GasTank>()`) and `Electrolyser`
//     pushes into them with `TryAdd`. The pad's tank is therefore an ordinary endpoint of the gas run.
//   • ITEMS — it is a chest, in both of the two ways this engine moves items between blocks: as an
//     `IItemPortHost` with `PortConfig` + `ItemPortRouting`, which is how item pipes push into and pull
//     out of a machine's faces; and as an `IItemConsumer` + `IItemProvider`, which is how conveyor belts,
//     chutes and funnels find a machine by collider probe and hand it a stack. Both answers name the same
//     drum, so a belt line and a pipe run fill and empty one buffer rather than two. There is no bespoke
//     "suck the nearest chest" code in this file.
//
// The pad's own buffers are the contract: base plumbing fills them, a visitor empties or fills them,
// and the pad never reaches into somebody else's network to take what was not given to it.
//
// Ground vehicles are not a second system in this game: a car or a lorry is a `GridEntity` with
// `GridWheel`s, so a parked rig is served by exactly the code above, and a rig with no fuel tank asks
// for no fuel (`Fuel01` reports "full" when there is nothing to fill) rather than hanging forever at
// the head of a queue.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Fluids;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Navigation
{
    [DisallowMultipleComponent, RequireComponent(typeof(PlacedBlock))]
    public class StaticRefuelPad : MonoBehaviour, IWaymarkSource, IRefuelPad, IItemPortHost,
                                  IInventoryInterface, IItemConsumer, IItemProvider
    {
        [Header("Waymark")]
        [Tooltip("What the player calls this place. A shuttle flies or drives to this name, and it is " +
                 "saved with the pad by `WorldStatePersistence`, the same way a quarry's state is.")]
        public string waymarkName = "";

        [Header("Reach")]
        [Tooltip("Metres from the pad in which a grid counts as visiting. Three and a half is about one " +
                 "small-grid ship width, which is the point: a pad serves what is standing at it.")]
        public float captureRadiusMetres = 3.5f;

        [Header("Transfer")]
        [Tooltip("Watts the pad pushes into a visitor's batteries while it is pumping. The base's own " +
                 "network decides whether that is possible this tick — the pad only ever asks for what it " +
                 "is serving, and refuses the leg when the answer is no.")]
        public float powerWatts = 24000f;

        [Tooltip("Litres per second of hydrogen and of liquid fuel the fittings can pass, shared: the " +
                 "same number the grid connector is authored with, so a fleet does not learn two speeds.")]
        public float litresPerSecond = 40f;

        [Tooltip("Item slots the pad hands to a visiting grid's cargo containers per second. Nothing here " +
                 "takes items off a ship: unloading is a docked port's own export or the drum's Output face, " +
                 "and a pad that took cargo without being asked would be a pad that loses cargo.")]
        public int itemSlotsPerSecond = 2;

        [Tooltip("How many slots the drum (the item buffer the ports route through) has. Small on " +
                 "purpose: a pad is a buffer, not a warehouse.")]
        public int drumSlots = 6;

        [Tooltip("Litres of liquid fuel the pad's own tank holds, filled by the base's pumps through the " +
                 "pipe graph.")]
        public float tankCapacityLitres = 4000f;

        [Tooltip("Units of hydrogen the pad's own gas tank holds, filled by electrolysis through the gas " +
                 "run.")]
        public float gasCapacity = 4000f;

        [Tooltip("Seconds a visitor may sit at the head before the pad releases it, so one stalled rig " +
                 "cannot hold the yard. Zero never times out.")]
        public float visitTimeoutSeconds = 600f;

        [Header("Network nodes")]
        [Tooltip("Where the pad's liquid buffer node sits, relative to the block. It is a real node of the " +
                 "base's fluid network and pipes link to a node by proximity, so this is the knob that aims " +
                 "the pad at a pipe run. Centred is right for a normal forecourt.")]
        public Vector3 tankNodeOffset = new Vector3(0f, 0.55f, 0f);

        [Tooltip("Where the pad's hydrogen endpoint sits, and with it the nozzle the player sees. A world " +
                 "gas tank is found by pipes probing colliders, and the pipe then demands the endpoint be " +
                 "nearly in line with it and within about 1.65 m — so run the gas to the nozzle, or move the " +
                 "nozzle to the run. Right-clicking it fills a portable canister, as any gas tank does.")]
        public Vector3 gasNodeOffset = new Vector3(0.86f, 0.62f, -0.86f);

        [Header("Readout")]
        public int logLines = 5;

        const float TICK_SECONDS = 0.25f;
        const int PROBE_LIMIT = 24;
        const string TANK_NODE_NAME = "PadFuelTank";
        const string GAS_NODE_NAME = "PadGasTank";

        readonly List<ConnectorVisit> _queue = new(8);
        readonly List<string> _log = new(8);
        readonly Collider[] _probe = new Collider[PROBE_LIMIT];
        float _tickTimer, _lastHydrogen;
        TransferFlow _lastFlow;
        PlacedBlock _placed;
        PowerConsumer _consumer;
        WaterTank _tank;
        GasTank _gas;
        ItemContainer _drum;
        int _drumSlotsApplied;
        PortConfig _ports;
        ItemPortRouting _routing;
        ItemPortContainer[] _portContainers;

        // ── Unity ────────────────────────────────────────────────────────────
        void Awake()
        {
            _placed = GetComponent<PlacedBlock>();
            _consumer = GetComponent<PowerConsumer>();
            if (_consumer == null) _consumer = gameObject.AddComponent<PowerConsumer>();
            _consumer.wattsPerSecond = 0f;            // idle: a pad is not a machine until it pumps
            EnsureBuffers();
            if (string.IsNullOrWhiteSpace(waymarkName)) waymarkName = SuggestName();
            GridWaymark.Register(this);
        }

        void OnEnable() { GridWaymark.Register(this); }
        void OnDisable()
        {
            GridWaymark.Unregister(this);
            ReleaseAll();
            if (_consumer != null) _consumer.wattsPerSecond = 0f;
        }
        void OnDestroy() { GridWaymark.Unregister(this); }

        void Update()
        {
            if (!Enabled) return;
            GridWaymark.RefreshLive();

            _tickTimer -= Time.unscaledDeltaTime;
            if (_tickTimer > 0f) return;
            _tickTimer = TICK_SECONDS;
            RefreshProximity();
            ServeHead(TICK_SECONDS);
        }

        /// <summary>Whether the pad is switched on. Public because the panel's toggle writes the
        /// component's own `enabled` and reads this back — a private face would leave the panel unable to
        /// show the state it had just changed. A world block has no persisted `GridBlock.Enabled` flag to
        /// imitate, so `enabled` (and the hierarchy it sits in) is the whole truth here, and the save
        /// system writes it alongside the pad's name and ratings.</summary>
        public bool Enabled => enabled && gameObject.activeInHierarchy;

        /// <summary>The pad's three buffers, created if the prefab does not carry them — the same
        /// self-heal `Chest` and `Quarry` do for their own components, so a pad placed from an older or a
        /// hand-edited prefab still has a tank to fill and ports to route through.</summary>
        void EnsureBuffers()
        {
            bool madeTank = false, madeGas = false;
            if (_tank == null)
            {
                var t = transform.Find(TANK_NODE_NAME);
                var found = t != null ? t.GetComponent<WaterTank>() : null;
                if (found == null)
                {
                    var go = new GameObject(TANK_NODE_NAME);
                    go.transform.SetParent(transform, false);
                    found = go.AddComponent<WaterTank>();
                    madeTank = true;
                }
                _tank = found;
                if (madeTank) _tank.transform.localPosition = tankNodeOffset;
                // `WaterTank` is a pure node — the pad's own model is the visual, and the node is the
                // thing `FluidNode.OnEnable` hands to `FluidNetworkManager`, so where it sits is what the
                // run sees.
            }
            if (_gas == null)
            {
                var g = transform.Find(GAS_NODE_NAME);
                var found = g != null ? g.GetComponent<GasTank>() : null;
                if (found == null)
                {
                    var go = new GameObject(GAS_NODE_NAME);
                    go.transform.SetParent(transform, false);
                    // A collider, because `GasPipe` finds world endpoints by probing colliders and then
                    // asking each hit's parents for a `GasTank`. Without one the tank is a private jug.
                    var col = go.AddComponent<BoxCollider>();
                    col.center = Vector3.zero;
                    col.size = new Vector3(0.6f, 0.7f, 0.6f);
                    found = go.AddComponent<GasTank>();
                    madeGas = true;
                }
                _gas = found;
                if (madeGas) _gas.transform.localPosition = gasNodeOffset;
                // A world `GasTank` owns no network object: it is a member of a run because a `GasPipe`
                // probing its neighbourhood finds a collider whose parents include one. The collider above
                // is the entire registration, which is why the readout below says ENDPOINT and nothing more.
            }
            if (_tank.capacityLitres < tankCapacityLitres) _tank.capacityLitres = tankCapacityLitres;
            if (_gas.capacity < gasCapacity) _gas.capacity = gasCapacity;
            if (!_gas.IsHydrogenMode) _gas.TrySetSelectedGasType(GasType.Hydrogen);

            if (_drum == null)
            {
                _drum = new ItemContainer("Pad Drum", Mathf.Max(1, drumSlots));
                _drum.AcceptFilter = null;             // a drum takes what a belt brings
                _drumSlotsApplied = _drum.Size;
            }
            else if (_drum.Size != Mathf.Max(1, drumSlots)) _drum.Resize(Mathf.Max(1, drumSlots));

            if (_ports == null)
            {
                _ports = GetComponent<PortConfig>();
                if (_ports == null) _ports = gameObject.AddComponent<PortConfig>();
            }
            if (_routing == null)
            {
                _routing = GetComponent<ItemPortRouting>();
                if (_routing == null) _routing = gameObject.AddComponent<ItemPortRouting>();
            }
        }

        string SuggestName()
        {
            string who = _placed != null && _placed.Item != null ? _placed.Item.displayName : "Refuel Pad";
            Vector3 p = transform.position;
            return who + " " + Mathf.RoundToInt(p.x) + "/" + Mathf.RoundToInt(p.z);
        }

        // ── IWaymarkSource ───────────────────────────────────────────────────
        public string WaymarkLabel => (waymarkName ?? string.Empty).Trim();
        public Vector3 WaymarkWorldPosition => transform.position + Vector3.up * 0.6f;
        public bool WaymarkAcceptsTraffic => Enabled;

        // ── IRefuelPad ───────────────────────────────────────────────────────
        public IReadOnlyList<ConnectorVisit> Queue => _queue;
        public ConnectorVisit Head => _queue.Count > 0 ? _queue[0] : null;
        public int QueueDepth => _queue.Count;
        public TransferFlow LastFlow => _lastFlow;
        public IReadOnlyList<string> Log => _log;
        public bool HasMagneticLock => false;         // a pad on the ground has nothing to lock to, and
                                                      // saying so beats pretending a penalty that cannot happen
        public float SoftRate => 1f;                  // no soft capture: a parked rig is a parked rig

        public void BumpToFront(GridEntity g)
        {
            int i = IndexOf(g);
            if (i <= 0) return;
            var v = _queue[i];
            _queue.RemoveAt(i);
            _queue.Insert(0, v);
            PushLog(v.shipLabel + " bumped to the head");
        }

        public void Eject(GridEntity g)
        {
            int i = IndexOf(g);
            if (i < 0) return;
            PushLog(_queue[i].shipLabel + " released by the operator");
            _queue.RemoveAt(i);
        }

        public bool IsNameFree(string candidate)
        {
            candidate = (candidate ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(candidate)) return false;
            var found = GridWaymark.FindSource(candidate);
            return found == null || ReferenceEquals(found, this);
        }

        public void Rename(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name) || (!IsNameFree(name) && name != WaymarkLabel)) return;
            waymarkName = name;
            GridWaymark.Register(this);              // the label is the key, so a rename is a re-register
        }

        /// <summary>State the save format writes for this pad. Methods rather than public fields, so
        /// `WorldStatePersistence` does not have to know which numbers are meaningful.</summary>
        public string SavedName => waymarkName ?? string.Empty;
        public void RestoreFromSave(string name) { if (!string.IsNullOrWhiteSpace(name)) waymarkName = name.Trim(); }

        /// <summary>Litres the save format kept for this pad's tank. The pad's `WaterTank` child is a
        /// world tank like any other, so `SavedPlacedBlock` already restores its contents — this is the
        /// fallback for a pad placed before the prefab carried a tank node, and it deliberately fills only
        /// a tank that is still dry, because a second writer that added on top would double the litres.</summary>
        public void RestoreTank(float litres, LiquidType type)
        {
            EnsureBuffers();
            if (_tank == null || !_tank.IsEmpty) return;
            float want = Mathf.Max(0f, Mathf.Min(_tank.capacityLitres, litres));
            if (want <= 0.001f) return;
            _tank.liquidType = type;
            _tank.AddSome(type, want);
        }

        /// <summary>The self-heal, from outside: `WorldStatePersistence` has to be able to hand items to a
        /// container that may not exist yet on a pad restored from an older save. `Quarry` has the same
        /// door for the same reason (`EnsureOutputPublic`).</summary>
        public void EnsureBuffersPublic() => EnsureBuffers();

        // ── IItemPortHost / IInventoryInterface: the drum is a port endpoint ──
        // A chest, not a special case: one container, both directions, and the faces decide which is
        // which. That is what lets a conveyor belt and a pipe run both fill the pad and drain it without
        // this file containing a single line of bespoke belt logic.
        public PortConfig PortConfig { get { EnsureBuffers(); return _ports; } }
        public ItemPortRouting Routing { get { EnsureBuffers(); return _routing; } }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureBuffers();
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Drum", _drum, canInput: true, canOutput: true);
            return _portContainers;
        }

        public ItemContainer GetOutputContainer() { EnsureBuffers(); return _drum; }
        public ItemContainer GetInputContainer() { EnsureBuffers(); return _drum; }
        public bool HasOutputReady
        {
            get
            {
                EnsureBuffers();
                if (_drum == null || _ports == null) return false;
                for (int i = 0; i < _drum.Slots.Count; i++)
                    if (_drum.GetSlot(i) != null && !_drum.GetSlot(i).IsEmpty) return _ports.HasAnyOutput();
                return false;
            }
        }
        public bool CanAcceptInput
        {
            get
            {
                EnsureBuffers();
                if (_drum == null || _ports == null) return false;
                for (int i = 0; i < _drum.Slots.Count; i++)
                    if (_drum.GetSlot(i) != null && _drum.GetSlot(i).IsEmpty) return _ports.HasAnyInput();
                return false;
            }
        }

        // ── IItemConsumer / IItemProvider: belts, chutes and funnels ─────────
        // Conveyors do not route through port faces. `ConveyorBelt` probes the half-metre around its
        // socket, walks each hit's parents looking for a component that speaks `IItemConsumer` or
        // `IItemProvider`, and treats any machine it finds as socket-compatible by definition. So a block
        // that wants belts has to answer that API as well as the pipe one — and both answers point at the
        // same drum, which is the whole idea: one buffer, every channel the base has.
        public int GetInputCapacity(ItemDefinition item)
        {
            EnsureBuffers();
            if (item == null || _drum == null) return 0;
            if (_ports == null || !_ports.HasAnyInput()) return 0;   // no open input face, no delivery
            return _drum.HasSpace(item, 1) ? ItemStack.MaxItemsPerStack(item) : 0;
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            EnsureBuffers();
            if (item == null || count <= 0 || _drum == null) return 0;
            var leftover = _drum.Insert(new ItemStack(item, count));
            return count - (leftover?.count ?? 0);
        }

        public ItemDefinition PeekOutput(out int count)
        {
            EnsureBuffers();
            count = 0;
            if (_drum == null || _ports == null || !_ports.HasAnyOutput()) return null;
            for (int i = 0; i < _drum.Size; i++)
            {
                var st = _drum.GetSlot(i);
                if (st == null || st.IsEmpty || st.item == null) continue;
                count = st.count;
                return st.item;
            }
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            EnsureBuffers();
            if (item == null || count <= 0 || _drum == null) return 0;
            return _drum.Remove(item, count);
        }

        // ── The queue ────────────────────────────────────────────────────────
        void RefreshProximity()
        {
            float radius = Mathf.Max(1f, captureRadiusMetres);
            float sqr = radius * radius;
            // Physics.OverlapSphereNonAlloc is the engine's own idiom for "what is standing here"
            // (`GasNetwork` and `GasPipe` probe with it), and a pre-allocated buffer keeps a 4 Hz pad
            // off the GC.
            int hits = Physics.OverlapSphereNonAlloc(WaymarkWorldPosition, radius, _probe, ~0,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits; i++)
            {
                var col = _probe[i];
                if (col == null) continue;
                var g = col.GetComponentInParent<GridEntity>();
                if (g == null) continue;
                if ((g.transform.position - WaymarkWorldPosition).sqrMagnitude > sqr * 4f) continue;
                if (IndexOf(g) >= 0) continue;
                if (_queue.Count >= 8) break;
                _queue.Add(new ConnectorVisit { grid = g, shipLabel = LabelOf(g), arrivedAt = Time.time, locked = true });
                PushLog(LabelOf(g) + " pulled in · queue " + _queue.Count);
            }

            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var v = _queue[i];
                if (v.grid == null) { _queue.RemoveAt(i); continue; }
                bool stillHere = (v.grid.transform.position - WaymarkWorldPosition).sqrMagnitude <= sqr * 4f;
                if (!stillHere && i == 0 && v.grid.TryGetComponent<GridRouteAutopilot>(out var ap) && ap.IsHoldingForTransfer)
                    stillHere = true;                   // a shuttle waiting for its own target keeps its place
                if (stillHere) continue;
                _queue.RemoveAt(i);
                PushLog(v.shipLabel + " left · queue " + _queue.Count);
            }

            if (visitTimeoutSeconds > 0.01f && _queue.Count > 0 && Head != null
                && Time.time - Head.arrivedAt > visitTimeoutSeconds)
            {
                PushLog(Head.shipLabel + " timed out at the head");
                _queue.RemoveAt(0);
            }
        }

        int IndexOf(GridEntity g)
        {
            for (int i = 0; i < _queue.Count; i++) if (_queue[i].grid == g) return i;
            return -1;
        }

        static string LabelOf(GridEntity g)
        {
            int blocks = 0; bool cockpit = false, wheels = false;
            if (g != null)
                foreach (var b in g.AllBlocks)
                {
                    blocks++;
                    if (b is GridCockpit) cockpit = true;
                    if (b is GridWheel) wheels = true;
                }
            return (cockpit ? (wheels ? "Ground vehicle" : "Cockpit ship") : "Unmanned grid") + " · " + blocks + " blocks";
        }

        void ReleaseAll() { _queue.Clear(); }

        // ── The transfer ─────────────────────────────────────────────────────
        void ServeHead(float seconds)
        {
            _lastFlow = default;
            if (_consumer != null) _consumer.wattsPerSecond = 0f;   // ask for nothing until something moves
            var visit = Head;
            if (visit?.grid == null || !Enabled) { if (visit != null) visit.lastFlow = _lastFlow; return; }

            float litres = ServeFluids(visit.grid, seconds);
            int moved = ServeItems(visit.grid);
            float wattHours = ServePower(visit.grid, seconds);

            var flow = new TransferFlow(wattHours, _lastHydrogen, litres, moved);
            visit.lastFlow = flow;
            _lastFlow = flow;
            if (!flow.IsIdle) PushLog("→ " + flow.Describe());
        }

        float ServePower(GridEntity ship, float seconds)
        {
            if (_consumer == null || powerWatts <= 0.01f || seconds <= 0.0001f) return 0f;
            bool toShip = !ship.TryGetComponent<GridRouteAutopilot>(out var ap) || ap.WantsPower;
            if (!toShip) return 0f;

            // The pad asks for exactly what it is serving, one tick early, because `PowerConsumer.IsPowered`
            // is set by `PowerNetworkManager` on its own 0.25 s tick: a pad that pushed watts it had never
            // asked for would be a pad silently borrowing from a base that has none. The cost of the
            // one-tick lag is that a base which just ran out keeps its visitor busy for a quarter second
            // before the refusal lands — and the refusal lands, which is the part that matters.
            _consumer.wattsPerSecond = powerWatts;
            if (_consumer.IsPowered)
            {
                float moved = GridConnectorBlock.ChargeGrid(ship, powerWatts * seconds / 3600f, seconds);
                if (moved <= 0.0001f) _consumer.wattsPerSecond = 0f;
                return moved;
            }
            PushLog("BASE CANNOT SUSTAIN " + powerWatts.ToString("0") + " W · REFUSING");
            _consumer.wattsPerSecond = 0f;
            return 0f;
        }

        float ServeFluids(GridEntity ship, float seconds)
        {
            _lastHydrogen = 0f;
            if (litresPerSecond <= 0.01f || seconds <= 0.0001f) return 0f;
            float want = litresPerSecond * seconds;
            float moved = 0f;
            var ap = ship.TryGetComponent<GridRouteAutopilot>(out var a) ? a : null;

            // ── Hydrogen: the pad's own endpoint → the visitor's gas run ──────
            if ((ap == null || ap.WantsHydrogen) && _gas != null && _gas.storedAmount > 0.001f)
            {
                var terminal = ShipGasTerminal(ship);
                var net = GridGasNetwork.Instance;
                if (terminal == null || net == null)
                {
                    PushLog("VISITOR HAS NO HYDROGEN TANK THAT WILL TAKE ANY");
                }
                else
                {
                    float drawn = _gas.TryTake(GasType.Hydrogen, want);
                    if (drawn > 0.0001f)
                    {
                        // The ship's own run decides who is filled, exactly as it does across a docked
                        // pair: pushing into every tank on the grid regardless of plumbing would let a
                        // pad fill a tank that is not connected to the engine that needs it.
                        float filled = net.FillGasFrom(terminal, GasType.Hydrogen, drawn);
                        if (filled < drawn) _gas.TryAdd(GasType.Hydrogen, drawn - filled);
                        _lastHydrogen = filled;
                        moved += filled;
                    }
                }
            }

            // ── Liquid fuel: the pad's tank → the visitor's tanks of that type ─
            if ((ap == null || ap.WantsFuel) && _tank != null && !_tank.IsEmpty)
            {
                float remain = Mathf.Max(0f, want - _lastHydrogen);
                if (remain > 0.001f)
                {
                    LiquidType type = _tank.liquidType;
                    var tanks = ShipLiquidTanks(ship, type);
                    if (tanks == null || tanks.Count == 0)
                    {
                        PushLog("VISITOR HAS NO " + type + " TANK · NOT DRAINED");
                    }
                    else
                    {
                        float drawn = _tank.TakeSome(type, remain);
                        if (drawn > 0.0001f)
                        {
                            float filled = 0f;
                            for (int i = 0; i < tanks.Count && filled < drawn - 0.0001f; i++)
                            {
                                var t = tanks[i];
                                if (t == null || !t.Enabled || t.Fill01 >= 0.9999f) continue;
                                float got = t.Add(drawn - filled);
                                if (got > 0.0001f) filled += got;
                            }
                            if (filled < drawn) _tank.AddSome(type, drawn - filled);   // nothing evaporates
                            moved += filled;
                        }
                    }
                }
            }
            return moved;
        }

        static List<GridLiquidTank> ShipLiquidTanks(GridEntity ship, LiquidType type)
        {
            var network = GridLiquidNetwork.Instance;
            return network != null ? network.GetTanks(ship, type) : null;
        }

        /// <summary>The visitor's hydrogen run, as `GasPipe` and the docked-pair connector see it: the
        /// tank with space first, any hydrogen tank after that, and nothing at all if the grid has none —
        /// a pad with no terminal to hand refuses the flow, which is the correct answer.</summary>
        static GridBlock ShipGasTerminal(GridEntity ship)
        {
            if (ship == null) return null;
            GridBlock any = null;
            foreach (var b in ship.AllBlocks)
            {
                if (b is not GridGasTank tank || !tank.Enabled) continue;
                if (tank.gasType != GasType.Hydrogen) continue;
                if (tank.stored < tank.capacity - 0.01f) return tank;
                any = tank;
            }
            return any;
        }

        int ServeItems(GridEntity ship)
        {
            if (itemSlotsPerSecond <= 0 || _drum == null) return 0;
            int budget = Mathf.Max(1, Mathf.CeilToInt(itemSlotsPerSecond * TICK_SECONDS));
            int moved = 0;
            foreach (var b in ship.AllBlocks)
            {
                if (moved >= budget) break;
                if (b is not GridCargoContainer store || !store.Enabled) continue;
                moved += MoveFromDrumInto(store.container, budget - moved);
            }
            return moved;
        }

        int MoveFromDrumInto(ItemContainer to, int maxItems)
        {
            if (_drum == null || to == null || maxItems <= 0) return 0;
            int moved = 0;
            for (int i = 0; i < _drum.Slots.Count && moved < maxItems; i++)
            {
                var stack = _drum.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                int take = Mathf.Min(stack.count, maxItems - moved);
                var rest = to.Insert(new ItemStack(stack.item, take));
                int accepted = take - (rest == null || rest.IsEmpty ? 0 : rest.count);
                if (accepted <= 0) continue;
                _drum.Remove(stack.item, accepted);
                moved += accepted;
            }
            return moved;
        }

        void PushLog(string line)
        {
            if (_log == null) return;
            _log.Add(line);
            while (_log.Count > Mathf.Max(1, logLines)) _log.RemoveAt(0);
        }

        // ── Panel-facing readout ─────────────────────────────────────────────
        public WaterTank Tank { get { EnsureBuffers(); return _tank; } }
        public GasTank Gas { get { EnsureBuffers(); return _gas; } }
        public ItemContainer Drum { get { EnsureBuffers(); return _drum; } }

        /// <summary>Three true statements about three different graphs, each in the terms that graph
        /// itself uses: the fluid and power networks answer with a link count, and the gas network keeps no
        /// membership list to read — a world tank is an endpoint because a pipe can find its collider — so
        /// the pad says whether an endpoint exists and leaves the player to believe the rest. A pad never
        /// reports plumbing it hopes for.</summary>
        public int TankLinks => _tank?.neighbours?.Count ?? 0;
        public bool TankPlumbed => _tank != null && _tank.network != null && TankLinks > 0;
        public bool GasEndpointPresent => _gas != null && _gas.GetComponent<Collider>() != null;
        public int PowerLinks => _consumer?.neighbours?.Count ?? 0;
        public bool PowerConnected => _consumer != null && _consumer.network != null && PowerLinks > 0;
        public float TankLitres => _tank != null ? _tank.StoredLitres : 0f;
        public float TankFill01 => _tank != null ? _tank.Fill01 : 0f;
        public LiquidType TankType => _tank != null ? _tank.liquidType : LiquidType.Water;
        public float GasStored => _gas != null ? _gas.storedAmount : 0f;
        public float GasFill01 => _gas != null ? _gas.Fill01 : 0f;
        public int DrumItems
        {
            get
            {
                EnsureBuffers();
                int n = 0;
                if (_drum != null)
                    for (int i = 0; i < _drum.Slots.Count; i++)
                    {
                        var st = _drum.GetSlot(i);
                        if (st != null && !st.IsEmpty) n += st.count;
                    }
                return n;
            }
        }

        /// <summary>Whether the base can pay for this pad's rated draw right now — the answer the panel
        /// prints, and the pad's own gate before it pushes watts it has not been given.</summary>
        public bool BaseCanServe => _consumer != null && _consumer.IsPowered;

        /// <summary>One line on what the pad is actually attached to. The point of the phrasing is that it
        /// never implies a tank within reach counts: the only supply a pad knows about is a graph it is a
        /// member of.</summary>
        public string SupplyLine
        {
            get
            {
                EnsureBuffers();
                var sb = new System.Text.StringBuilder(64);
                sb.Append("fluid ").Append(TankLinks).Append(" link").Append(TankLinks == 1 ? "" : "s");
                sb.Append("  ·  gas endpoint ").Append(GasEndpointPresent ? "PRESENT" : "MISSING");
                sb.Append("  ·  power ").Append(PowerLinks).Append(" wire").Append(PowerLinks == 1 ? "" : "s");
                sb.Append("  ·  drum ").Append(_drum != null && _ports != null && _ports.HasAnyInput() ? "HAS INPUT FACE" : "NO INPUT FACE");
                return sb.ToString();
            }
        }
    }
}
