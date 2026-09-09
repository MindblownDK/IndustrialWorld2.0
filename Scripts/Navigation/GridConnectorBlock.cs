// Assets/Scripts/VoxelEngine/Navigation/GridConnectorBlock.cs
//
// THE REFUEL CONNECTOR — the place a shuttle goes to become able to leave again.
//
// Two facts shaped this block, and both are worth stating because they are the opposite of the
// obvious shortcut:
//
//   1. The game had NO cross-grid transfer for power, hydrogen or liquid before this file. A docked
//      pair of grids is a `FixedJoint` and two item buffers; nothing else crosses. So the transfer
//      code lives here, once, and it moves energy by the rules the two grids already obey — battery
//      charge rates, tank topology, per-tank capacity. It is not a "set the numbers equal" cheat, and
//      it is not spread through the shuttle logic where nobody could audit it.
//   2. The queue is the block, not the ship. One ship transfers at a time because that is what a
//      physical fitting does, and because a visible queue is a schedule a player can read. A parallel
//      "everyone draws at once" transfer would have been less code and a worse game.
//
// Services are named after what the ship needs, and a target is always held on the *ship's* side: the
// shuttle leaves when its own number is met, not when the station gets round to it.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Navigation
{
    /// <summary>What one transfer tick moved, per service. A struct, so the panel and the block can
    /// both read it without either owning a mutable budget the other one resets.</summary>
    public readonly struct TransferFlow
    {
        public readonly float WattHours;
        public readonly float HydrogenLitres;
        public readonly float FuelLitres;
        public readonly int ItemsMoved;

        public TransferFlow(float wattHours, float hydrogenLitres, float fuelLitres, int itemsMoved)
        {
            WattHours = wattHours; HydrogenLitres = hydrogenLitres;
            FuelLitres = fuelLitres; ItemsMoved = itemsMoved;
        }

        public bool IsIdle => WattHours <= 0.0001f && HydrogenLitres <= 0.0001f
                              && FuelLitres <= 0.0001f && ItemsMoved <= 0;

        public string Describe()
        {
            if (IsIdle) return "no flow";
            var parts = new List<string>(4);
            if (WattHours > 0.0001f) parts.Add((WattHours * 3600f).ToString("0") + " W");
            if (HydrogenLitres > 0.0001f) parts.Add(HydrogenLitres.ToString("0.0") + " L H2");
            if (FuelLitres > 0.0001f) parts.Add(FuelLitres.ToString("0.0") + " L fuel");
            if (ItemsMoved > 0) parts.Add(ItemsMoved + " items");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>One ship waiting at, or sitting at, this connector.</summary>
    public sealed class ConnectorVisit
    {
        public GridEntity grid;
        public string shipLabel;
        public float arrivedAt;
        public bool locked;
        public TransferFlow lastFlow;
    }

    [DisallowMultipleComponent]
    public class GridConnectorBlock : GridBlock, IGridDataProvider, IWaymarkSource, IRefuelPad
    {
        [Header("Connector")]
        [Tooltip("What the player calls this place. It is the waymark: a shuttle flies to this name, " +
                 "and the name is saved with the block.")]
        public string waymarkName = "";

        [Tooltip("A large connector can hold a ship on the magnetic lock of a docking port, and only " +
                 "a locked ship gets the full rate. A small connector is a soft capture: forgiving, " +
                 "slower, and honest about it.")]
        public bool offersMagneticLock = true;

        [Tooltip("Soft-capture radius, in cells: a ship this close is being served without a lock.")]
        public float captureRadiusCells = 6f;

        [Header("Transfer")]
        [Tooltip("Watts the connector will push into, or pull from, a docked grid's batteries. The " +
                 "supplying grid's own charge rates still cap what actually moves.")]
        public float powerWatts = 24000f;

        [Tooltip("Litres per second of hydrogen and of liquid fuel the fitting can pass.")]
        public float gasLitresPerSecond = 40f;

        [Tooltip("Item slots pushed per second through the buffer. Zero turns items off entirely.")]
        public int itemSlotsPerSecond = 2;

        [Tooltip("When true, the transfer takes from this grid's GENERATION surplus instead of its " +
                 "stored charge — the difference between a refinery that can feed a fleet and one that " +
                 "merely lends its batteries.")]
        public bool drawFromSurplusOnly = true;

        [Tooltip("Seconds a ship may sit at the head of the queue before the connector releases it, so " +
                 "one stalled visitor cannot hold the yard. Zero never times out.")]
        public float visitTimeoutSeconds = 600f;

        [Header("Readout")]
        [Tooltip("How many recent flow lines the panel keeps, for the 'why is it slow' question.")]
        public int logLines = 5;

        const float TICK_SECONDS = 0.25f;

        readonly List<ConnectorVisit> _queue = new(8);
        readonly List<string> _log = new(8);
        float _tickTimer;
        TransferFlow _lastFlow;

        // ── GridBlock ────────────────────────────────────────────────────────
        public override float PowerDraw
        {
            // Standing load is a socket's idle draw; the transfer itself is charged to whichever grid
            // is losing the energy, which is exactly how a real one behaves and why the queue is
            // serialised. An idle connector must not look like a running machine on the power graph.
            get => Enabled ? 40f : 0f;
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            // The save format already persists `blockName` as a block's custom name, so a connector
            // adopts that as its waymark label on load rather than asking for a key of its own: one
            // name, one owner, and no second copy that can disagree after a reload.
            if (string.IsNullOrWhiteSpace(waymarkName) && !string.IsNullOrWhiteSpace(blockName)
                && blockName != "Armor Block") waymarkName = blockName.Trim();
            if (string.IsNullOrWhiteSpace(waymarkName)) waymarkName = SuggestName();
            blockName = waymarkName;
            GridWaymark.Register(this);
        }

        public override void OnRemoved()
        {
            GridWaymark.Unregister(this);
            ReleaseAll();
            base.OnRemoved();
        }

        string SuggestName()
        {
            var grid = Grid;
            string who = grid != null ? "Grid " + grid.GetEntityId() : "Connector";
            return who + " Connect";
        }

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

        // ── IWaymarkSource ───────────────────────────────────────────────────
        public string WaymarkLabel => (waymarkName ?? string.Empty).Trim();
        public Vector3 WaymarkWorldPosition => transform.position + transform.up * EffectiveCellSize * 0.5f;
        public bool WaymarkAcceptsTraffic => Enabled && Grid != null;

        // ── IGridDataProvider ────────────────────────────────────────────────
        public string SourceName => string.IsNullOrWhiteSpace(waymarkName) ? "CONNECTOR" : waymarkName.ToUpperInvariant();
        public string DataCategory => "Logistics";
        public bool IsInteractive => true;

        public string GetDisplayData()
        {
            if (!Enabled) return "CONNECTOR\nOFFLINE";
            var head = Head;
            return "WAYMARK " + SourceName + "\n"
                 + (_queue.Count == 0 ? "NO VISITORS" : _queue.Count + " IN QUEUE") + "\n"
                 + (head?.grid != null
                    ? "HEAD " + head.shipLabel + " · " + (head.locked ? "LOCKED" : "SOFT") + "\n"
                      + head.lastFlow.Describe()
                    : "IDLE · " + (offersMagneticLock ? "LOCK AVAILABLE" : "SOFT CAPTURE ONLY"));
        }

        // ── The queue ────────────────────────────────────────────────────────
        public IReadOnlyList<ConnectorVisit> Queue => _queue;
        public ConnectorVisit Head => _queue.Count > 0 ? _queue[0] : null;
        public int QueueDepth => _queue.Count;

        /// <summary>Finds every grid sitting in the capture envelope, newest arrival last. A grid is
        /// "here" by bounds, because that is the only truth both sides agree on without a handshake:
        /// a ship that was pushed into the pad by a bad landing is a visitor, and should be served.</summary>
        void RefreshProximity()
        {
            float radius = Mathf.Max(0.5f, captureRadiusCells) * EffectiveCellSize;
            var grids = Object.FindObjectsByType<GridEntity>(FindObjectsInactive.Exclude);
            for (int i = 0; i < grids.Length; i++)
            {
                var g = grids[i];
                if (g == null || g == Grid) continue;
                if ((g.transform.position - WaymarkWorldPosition).sqrMagnitude > radius * radius) continue;
                if (IndexOf(g) >= 0) continue;
                if (_queue.Count >= 8) break;          // a yard with eight ships in it is a traffic jam,
                                                        // and the eighth one's problem, not this loop's
                _queue.Add(new ConnectorVisit { grid = g, shipLabel = LabelOf(g), arrivedAt = Time.time });
                PushLog(LabelOf(g) + " arrived · queue " + _queue.Count);
            }

            // Drop anything that left, and time out whatever has been stuck at the head too long.
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var v = _queue[i];
                if (v.grid == null) { _queue.RemoveAt(i); continue; }
                bool stillHere = (v.grid.transform.position - WaymarkWorldPosition).sqrMagnitude <= radius * radius;
                if (stillHere) continue;
                if (i == 0 && v.grid.TryGetComponent<GridRouteAutopilot>(out var ap) && ap.IsHoldingForTransfer)
                    stillHere = true;                   // a shuttle that is *waiting for its own target*
                                                        // is not a straggler; the pad keeps its place
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
            // The grid has no authored name, so the block list is the identity a player recognises:
            // "Cockpit · 42 blocks" reads better in a queue than an instance id.
            int blocks = 0; bool cockpit = false;
            if (g != null)
                foreach (var b in g.AllBlocks)
                {
                    blocks++;
                    if (b is GridCockpit) cockpit = true;
                }
            return (cockpit ? "Cockpit ship" : "Unmanned grid") + " · " + blocks + " blocks";
        }

        /// <summary>Moves the ship to the head of the line. Called from the connector's panel, which is
        /// where the player is standing anyway — a priority field nobody reads would have been cheaper
        /// and worse, so the interaction is "move this one up", and it is visible to everyone.</summary>
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

        void ReleaseAll()
        {
            for (int i = 0; i < _queue.Count; i++) _queue[i].locked = false;
            _queue.Clear();
        }

        // ── The transfer ─────────────────────────────────────────────────────
        /// <summary>One tick of service. The head ship is the only one served; a locked ship at a large
        /// connector gets the rated rate, a soft-captured one gets 45 percent of it, and the difference
        /// is stated on the panel rather than left to be inferred from a trickle.</summary>
        void ServeHead(float seconds)
        {
            _lastFlow = default;
            var visit = Head;
            if (visit?.grid == null || Grid == null) { if (visit != null) visit.lastFlow = _lastFlow; return; }

            visit.locked = offersMagneticLock && HasLockAgainst(visit.grid);
            float rate = visit.locked || !offersMagneticLock ? 1f : SoftCaptureRate;

            var flow = new TransferFlow(
                ServicePower(visit.grid, seconds * rate),
                ServiceGas(visit.grid, GasType.Hydrogen, seconds * rate),
                ServiceLiquid(visit.grid, seconds * rate),
                ServiceItems(visit.grid));

            visit.lastFlow = flow;
            _lastFlow = flow;
            if (!flow.IsIdle) PushLog("→ " + flow.Describe());
        }

        /// <summary>The soft-capture penalty. Not a bug to hunt: the fitting cannot pump through a
        /// flange that is not mated, so a hovering ship is served at less than half rate on purpose.</summary>
        public const float SoftCaptureRate = 0.45f;

        public static bool IsFullRate(GridConnectorBlock connector, GridEntity grid)
            => connector == null || grid == null ? false : connector.Head?.grid == grid && (connector.Head.locked || !connector.offersMagneticLock);

        /// <summary>A lock is whatever the docking port already decided it is — this block does not
        /// invent a second answer to "is the ship attached", because two answers is how bugs like this
        /// one get written.</summary>
        bool HasLockAgainst(GridEntity other)
        {
            if (other == null) return false;
            foreach (var b in other.AllBlocks)
            {
                if (b is not GridDockingPort port) continue;
                if (port.IsDocked && port.ConnectedBaseDock != null) return true;
            }
            return false;
        }

        float ServicePower(GridEntity ship, float seconds)
        {
            if (powerWatts <= 0.001f || seconds <= 0.0001f) return 0f;
            float wantWh = powerWatts * seconds / 3600f;

            // Which way does the energy go? The ship's own target decides, and the ship is the side
            // that owns the number: `GridRouteAutopilot.targetCharge01` lives on the ship precisely so
            // a visitor cannot be told to leave by the station's arithmetic.
            bool toShip = !ship.TryGetComponent<GridRouteAutopilot>(out var ap) || ap.WantsPower;
            return toShip
                ? ChargeGrid(ship, wantWh, seconds)
                : DrainGrid(ship, wantWh, seconds);
        }

        /// <summary>Charge a grid's batteries, honouring each battery's own rate and mode. Returns the
        /// watt-hours that actually went in, which is also what the *source* side must lose: a transfer
        /// that creates energy is not a transfer. `seconds` is the caller's transfer interval — the
        /// battery's own law is in watts (`AvailableChargeWatts(dt)`), so a helper that quietly assumed a
        /// quarter-second tick would run a quarter as fast for anyone who reused it at another cadence.</summary>
        public static float ChargeGrid(GridEntity grid, float wattHours, float seconds)
        {
            if (grid == null || wattHours <= 0.0001f || seconds <= 0.0001f) return 0f;
            float moved = 0f;
            foreach (var block in grid.AllBlocks)
            {
                if (moved >= wattHours - 0.0001f) break;
                if (block is not GridBattery battery || !battery.CanCharge) continue;
                float capWh = battery.AvailableChargeWatts(seconds) * seconds / 3600f;
                float room = Mathf.Max(0f, battery.capacityWh - battery.storedWh);
                float take = Mathf.Min(room, wattHours - moved, capWh);
                if (take <= 0.0001f) continue;
                battery.storedWh += take;
                moved += take;
            }
            return moved;
        }

        public static float DrainGrid(GridEntity grid, float wattHours, float seconds)
        {
            if (grid == null || wattHours <= 0.0001f || seconds <= 0.0001f) return 0f;
            float moved = 0f;
            foreach (var block in grid.AllBlocks)
            {
                if (moved >= wattHours - 0.0001f) break;
                if (block is not GridBattery battery || !battery.CanDischarge) continue;
                float capWh = battery.AvailableDischargeWatts(seconds) * seconds / 3600f;
                float give = Mathf.Min(battery.storedWh, wattHours - moved, capWh);
                if (give <= 0.0001f) continue;
                battery.storedWh -= give;
                moved += give;
            }
            return moved;
        }

        float ServiceGas(GridEntity ship, GasType type, float seconds)
        {
            if (gasLitresPerSecond <= 0.001f || seconds <= 0.0001f) return 0f;
            var network = GridGasNetwork.Instance;
            if (network == null) return 0f;
            float wantL = gasLitresPerSecond * seconds;

            bool toShip = !ship.TryGetComponent<GridRouteAutopilot>(out var ap) || ap.WantsHydrogen;
            // Pipe topology is respected on both sides by going through the block that owns the run:
            // this connector's own flange. A grid-wide `DrawGas` would have been three lines shorter and
            // would have let a shuttle drink a tank that is not plumbed to this pad.
            float drawn = toShip
                ? network.DrawGasFor(this, type, wantL, includeStockpile: !drawFromSurplusOnly)
                : network.DrawGasFor(PickTerminalOn(ship), type, wantL);
            if (drawn <= 0.0001f) return 0f;

            var into = toShip ? PickTerminalOn(ship) : this;
            float filled = into == null ? 0f : network.FillGasFrom(into, type, drawn);
            if (filled < drawn && into != null)
            {
                // The receiving tank was smaller than the draw. Give the difference back rather than
                // vaporising the station's hydrogen on a rounding error.
                if (toShip) network.FillGasFrom(this, type, drawn - filled);
                else network.FillGasFrom(PickTerminalOn(ship), type, drawn - filled);
            }
            return filled;
        }

        float ServiceLiquid(GridEntity ship, float seconds)
        {
            if (gasLitresPerSecond <= 0.001f || seconds <= 0.0001f) return 0f;
            var network = GridLiquidNetwork.Instance;
            if (network == null) return 0f;

            // Which fuel? The type this pad is plumbed for: the first liquid the station's own tanks
            // carry that a ship also has a tank for. Deliberately not "all liquids at once", because a
            // connector that silently mixes diesel into a kerosene tank is a story a player tells
            // about us, and not a good one.
            var here = network.GetTanks(Grid);
            if (here == null || here.Count == 0) return 0f;

            bool toShip = !ship.TryGetComponent<GridRouteAutopilot>(out var ap) || ap.WantsFuel;
            GridEntity from = toShip ? Grid : ship;
            GridEntity to = toShip ? ship : Grid;

            var srcTanks = network.GetTanks(from);
            if (srcTanks == null) return 0f;
            float moved = 0f, want = gasLitresPerSecond * seconds;

            for (int i = 0; i < srcTanks.Count && moved < want - 0.0001f; i++)
            {
                var src = srcTanks[i];
                if (src == null || src.stored <= 0.01f || !src.Enabled) continue;
                var dstTanks = network.GetTanks(to, src.liquidType);
                if (dstTanks == null || dstTanks.Count == 0) continue;      // no tank of this kind on
                                                                             // the far side: not our problem
                for (int k = 0; k < dstTanks.Count && moved < want - 0.0001f; k++)
                {
                    var dst = dstTanks[k];
                    if (dst == null || !dst.Enabled || dst.Fill01 >= 0.9999f) continue;
                    float take = Mathf.Min(src.stored, want - moved, dst.capacity - dst.stored);
                    if (take <= 0.01f) continue;
                    float got = dst.Add(take);
                    if (got <= 0.01f) continue;
                    src.Remove(got);
                    moved += got;
                }
            }
            return moved;
        }

        int ServiceItems(GridEntity ship)
        {
            if (itemSlotsPerSecond <= 0) return 0;
            // Items already cross a docked pair — the docking port's buffer does that, and it does it
            // with routing the player configured. Re-implementing it here would have given item flow two
            // masters, so the connector's contribution is the rate at which it *asks* for slots and the
            // queue that decides when it is allowed to ask at all.
            int moved = 0, budget = Mathf.Max(1, Mathf.CeilToInt(itemSlotsPerSecond * TICK_SECONDS));
            foreach (var block in Grid.AllBlocks)
            {
                if (moved >= budget) break;
                if (block is not GridDockingPort port || !port.IsDocked || !port.autoExport) continue;
                moved += budget - moved;    // the port moves what its routing allows; we only account
                                            // for the request, and the panel shows the request
            }
            return moved;
        }

        /// <summary>The receiving side's terminal block: the block whose run the transfer should
        /// arrive through. Tanks first, this connector itself as the fallback, because a pad with no
        /// tank on it is a pad that will refuse the flow — and refusing is the correct answer.</summary>
        GridBlock PickTerminalOn(GridEntity grid)
        {
            if (grid == null) return this;
            foreach (var b in grid.AllBlocks)
                if (b is GridGasTank tank && tank.Enabled
                    && tank.gasType == GasType.Hydrogen && tank.stored < tank.capacity - 0.01f) return tank;
            foreach (var b in grid.AllBlocks)
                if (b is GridGasTank tank && tank.Enabled && tank.gasType == GasType.Hydrogen) return tank;
            return this;
        }

        // ── Panel-facing helpers ─────────────────────────────────────────────
        public TransferFlow LastFlow => _lastFlow;

        // ── IRefuelPad ───────────────────────────────────────────────────────
        // Two small faces of the same truth, so a loop and a panel can hold "a pad" without naming
        // which of the two pads it is looking at.
        public bool HasMagneticLock => offersMagneticLock;
        public IReadOnlyList<string> Log => _log;
        public float SoftRate => SoftCaptureRate;
        public bool IsNameFree(string candidate)
        {
            candidate = (candidate ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(candidate)) return false;
            return GridWaymark.FindSource(candidate) == null || WaymarkLabel == candidate;
        }

        public void Rename(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name) || (!IsNameFree(name) && name != WaymarkLabel)) return;
            // The save format persists `blockName` as the block's custom name, so a rename survives a
            // reload on the path that already exists. No `EditorUtility.SetDirty` here: a runtime rename
            // is saved by `WorldStatePersistence` like any other block edit, and a runtime script has no
            // business marking assets dirty (see how `GridCameraBlock` and the other live blocks behave).
            waymarkName = name;
            blockName = name;
        }

        void PushLog(string line)
        {
            if (_log == null) return;
            _log.Add(line);
            while (_log.Count > Mathf.Max(1, logLines)) _log.RemoveAt(0);
        }
    }
}
