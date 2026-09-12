// Assets/Scripts/VoxelEngine/Building/BridgeSpan.cs
//
// THE BRIDGE SPAN — the structure a water crossing belongs to.
//
// A `RoadRun` is a wear ledger: it groups cells that share a surface and bills them as one. A span
// is a different thing and deliberately not the same object. It groups the cells that cross ONE gap
// and owns what a crossing has and a wear pool does not — how high the deck stands above the water,
// where the piers go, and whether the deck can open for a ship.
//
// Both exist on the same cells at once, and that is not duplication: a viaduct of three crossings is
// one road (one run, one wear curve, one repair gesture) and three spans (three deck heights, three
// sets of piers, three things a barge can hit). Merging them would mean either a road that wears per
// crossing or a crossing that cannot be repaired as part of the road it carries.
//
// WHAT A SPAN IS MADE OF
//   • A DECK, which is just the bridge cells themselves — `AsphaltRoad` in its deck mode, flat
//     rather than draped, because a deck that follows the riverbed is a ford.
//   • PIERS, built down from the deck to whatever is under it at a regular spacing, so a long
//     crossing reads as a structure and not as a floating slab. They are visual and physical: a
//     pier has a collider, so a boat hits it.
//   • A STRUCTURE KIND. A culvert is a short crossing that hugs the ground and lets a stream run
//     under an arch; a bridge stands off on piers; a drawbridge is a bridge whose deck can open.
//
// THE DRAWBRIDGE
//   Opening does not move the placed cells. Moving a `PlacedBlock` means moving its collider, its
//   save position and its run membership, which is a lot of machinery to hang off a hinge — and a
//   cell that has moved is a cell the save no longer describes. Instead the span disables its cells
//   and raises two leaf meshes about hinges at the abutments. Open means there is genuinely no road:
//   nothing to drive on, nothing to collide with, and a clear channel underneath. Shut means the
//   cells come back exactly as they were.
//
// THE AUTOMATION (9.44.2-dev)
//   A drawbridge is a machine, and a machine is paid for. The swing demands watts from a
//   `PowerConsumer` seated on the abutment cell — a cable run to the bridge is what makes the deck
//   move, exactly the way a refuel pad's hose only pumps on watts that exist. The span watches its
//   channel on a half-second cadence and opens for a hull that is actually in the water (the same
//   `WaterProbeSystem` the boats themselves float on), holding until the water is clear. A deck
//   that is about to move announces itself like every real crossing does: a low horn on the swing,
//   and red beacons at the abutments that flash while the deck is in motion — including while it is
//   stalled for want of power, which is also the only state in which the automation is loud about
//   its own failure.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Environment;   // RoadSurfaceUtility, to find the decks a restored cell joins
using VoxelEngine.FX;           // SfxLibrary + AudioManager, the procedural horn
using VoxelEngine.GridSystem;   // GridEntity, the vessel half of the automation query
using VoxelEngine.Maritime;     // WaterProbeSystem, the water half of the automation query
using VoxelEngine.Power;        // PowerConsumer, the bill for moving the deck
using VoxelEngine.UI;           // BuildFeedbackHud, the near-field stall notice

namespace VoxelEngine.Building
{
    /// <summary>What a crossing is. Appended-order enum: a save stores it as an int.</summary>
    public enum BridgeStructure
    {
        /// <summary>Short and low. Hugs the ground, lets a stream run under. Cheapest.</summary>
        Culvert,
        /// <summary>Stands off on piers over a real gap. Always shut.</summary>
        Fixed,
        /// <summary>A bridge whose deck opens for traffic on the water.</summary>
        Drawbridge
    }

    public sealed class BridgeSpan
    {
        // ════════════════════════════════════════════════════════════════
        //  AUTHORED TUNING — STRUCTURE
        // ════════════════════════════════════════════════════════════════

        /// <summary>Piers per metre of deck. At one per 8 m a 24 m crossing gets three, which reads
        /// as a structure without turning a small culvert into a colonnade.</summary>
        public const float METRES_PER_PIER = 8f;

        /// <summary>Below this deck clearance a crossing is a culvert rather than a bridge: there is
        /// not enough room under it for a pier to mean anything.</summary>
        public const float CULVERT_CLEARANCE = 1.5f;

        /// <summary>Seconds for a drawbridge to swing fully open or shut. Slow enough that a player
        /// on the approach can see it happening and stop.</summary>
        public const float SWING_SECONDS = 4f;

        /// <summary>Clearance assumed when the ground probe finds no bottom at all.
        /// `AsphaltRoad.ProbeGround` reaches 4 m down and no further, so a deck over a deep channel
        /// gets no reading whatsoever. Treating "no bottom found" as zero clearance would classify a
        /// genuine bridge as a culvert and leave it hanging with no piers — the exact opposite of
        /// what the missing reading means. Absence of ground is deep water, not shallow water.</summary>
        public const float NO_BOTTOM_CLEARANCE = 6f;

        /// <summary>How far a drawbridge leaf lifts, in degrees. 70 degrees leaves a clear channel
        /// and reads as a bascule rather than a ramp.</summary>
        public const float LEAF_ANGLE = 70f;

        // ════════════════════════════════════════════════════════════════
        //  AUTHORED TUNING — AUTOMATION (9.44.2-dev)
        // ════════════════════════════════════════════════════════════════

        /// <summary>Watts the swing demands while the deck is in motion. Between a small machine
        /// (500 W) and a workshop drill (400 W): moving two steel leaves is a motor, not a city. The
        /// demand is zero while the span sits still, so a wired bridge costs nothing to own.</summary>
        public const float SWING_WATTS = 450f;

        /// <summary>How far the abutment cell's power node reaches for a cable, in metres. The
        /// consumer sits on the bank end of the crossing, so a pole or relay placed on the approach
        /// road is inside this radius — the same leniency the refuel pad grants its forecourt.</summary>
        public const float POWER_CONNECT_RADIUS = 8f;

        /// <summary>Half-extent, in metres, of the water the span watches for arriving hulls,
        /// measured along the channel from the deck's centre line. A ship inside this reach is a
        /// ship the deck should already be opening for.</summary>
        public const float APPROACH_METRES = 22f;

        /// <summary>How much wider than the approach reach the HOLD volume is. The deck stays up
        /// until a vessel has cleared this, so a hull that has passed under is not dropped on.</summary>
        public const float HOLD_FACTOR = 1.6f;

        /// <summary>Seconds the water must be clear of the hold volume before an automatically
        /// opened deck shuts again. Dwell, not instant — a bobbing hull at the reach edge must not
        /// flicker the deck, and a boat that noses out and back is still a boat.</summary>
        public const float CLEAR_GRACE_SECONDS = 4f;

        /// <summary>Cadence of the vessel scan, in seconds. Half a second is far below the swing
        /// time, so the deck's decision can never lag the ship by a meaningful distance.</summary>
        public const float SCAN_SECONDS = 0.5f;

        /// <summary>How far the horn carries, in metres. A drawbridge is a crossing on a working
        /// waterway: the horn must reach the helm of the ship that asked for the channel.</summary>
        public const float HORN_RANGE = 140f;

        /// <summary>How far from the span a player still sees the no-power stall notice, in metres.
        /// Farther and it becomes a popup about somebody else's bridge.</summary>
        public const float STALL_NOTICE_RANGE = 55f;

        /// <summary>Height of the box above the deck that must be empty before the automation shuts
        /// the deck. A stalled lorry mid-deck postpones the shut; it must never be closed around.</summary>
        public const float DECK_OCCUPIED_HEIGHT = 2.6f;

        /// <summary>Half-period of the warning beacon's blink, in seconds.</summary>
        public const float BEACON_BLINK_SECONDS = 0.45f;

        /// <summary>Colliders the two physics probes may return per scan. Generous on purpose: a
        /// ship is many colliders and the queries dedupe by grid.</summary>
        private const int PROBE_LIMIT = 64;

        // ════════════════════════════════════════════════════════════════
        //  STATE
        // ════════════════════════════════════════════════════════════════

        /// <summary>Who asked for the deck's current swing. The automation opens on a fresh arrival
        /// and shuts what it opened; a player's command is never overridden by either. Not saved:
        /// a restored deck classifies itself in `ApplySavedState`.</summary>
        private enum OpenOwner { None, Player, Auto }

        private enum BeaconMode { Dark, Flash, Steady }

        private readonly List<AsphaltRoad> _cells = new List<AsphaltRoad>(16);
        private readonly List<GameObject> _piers = new List<GameObject>(8);
        private GameObject _leafNear, _leafFar;
        private Material _leafMaterial;

        private Beacon _beaconNear, _beaconFar;
        private BeaconMode _beaconMode;
        private bool _beaconLit;
        private float _beaconTimer;

        private PowerConsumer _consumer;
        private GameObject _powerHost;

        private OpenOwner _openedBy;
        private float _scanTimer = SCAN_SECONDS * 0.5f;   // first scan soon after the span exists
        private bool _prevHoldOccupied;
        private float _holdUntil;
        private bool _stalled;
        private bool _hornPending;
        private bool _stallNoticeShown;

        public BridgeStructure Structure { get; set; } = BridgeStructure.Fixed;

        /// <summary>Metres of clear water under the deck. Drives the culvert/bridge split and the
        /// pier height, and is what the player is really paying for.</summary>
        public float DeckClearance { get; set; }

        /// <summary>0 = deck down and drivable, 1 = fully open. Only meaningful on a drawbridge.</summary>
        public float Open01 { get; private set; }

        /// <summary>Where the span is heading. A shut span sits at 0 and an open one at 1; the value
        /// in between is the animation, not a state.</summary>
        public bool WantsOpen { get; private set; }

        public int CellCount => _cells.Count;
        public IReadOnlyList<AsphaltRoad> Cells => _cells;

        /// <summary>Deck length in metres, measured ALONG THE CROSSING. Every lane of a wide deck
        /// is a cell in this span, so counting cells would call a three-lane crossing three times
        /// longer than it is — the leaves would fly 3× their hinge-to-hinge length and the piers
        /// would arrive in colonnades. The frame below projects the cells onto the crossing's own
        /// axis instead, which is correct for any width.</summary>
        public float DeckLength
        {
            get
            {
                var f = default(SpanFrame);
                return TryComputeFrame(ref f) ? f.Length : 0f;
            }
        }

        public bool CanOpen => Structure == BridgeStructure.Drawbridge && _cells.Count >= 2;

        public string StatusLabel
        {
            get
            {
                switch (Structure)
                {
                    case BridgeStructure.Culvert: return "Culvert";
                    case BridgeStructure.Drawbridge:
                        if (Open01 <= 0.01f)
                            return _stalled ? "Drawbridge - waiting for power" : "Drawbridge - closed to traffic";
                        if (Open01 >= 0.99f) return "Drawbridge - open to water";
                        return _stalled ? "Drawbridge - swinging (no power)" : "Drawbridge - swinging";
                    default: return "Bridge";
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  THE FRAME — one projection, every geometry question answered
        // ════════════════════════════════════════════════════════════════

        /// <summary>The span's own axes and the deck's extent on them, taken from the live cells.
        /// Everything geometric — pier stations, leaf hinges, beacon posts, the automation's channel
        /// and deck-occupied volumes — reads from one frame, so no two parts of the span can disagree
        /// about where the crossing is.</summary>
        private struct SpanFrame
        {
            public Vector3 Origin, Forward, Right, Up;
            public Quaternion Rotation;
            public float AxisMin, AxisMax;      // deck extent along Forward (the road's direction)
            public float AcrossMin, AcrossMax;  // deck extent along Right (the lanes)
            public float Cell;                  // largest cell edge present, used for the end caps

            public float Length => (AxisMax - AxisMin) + Cell;
            public float Width => (AcrossMax - AcrossMin) + Cell;
            public float AcrossMid => (AcrossMin + AcrossMax) * 0.5f;

            public Vector3 NearAbutment => Origin + Forward * AxisMin + Right * AcrossMid;
            public Vector3 FarAbutment => Origin + Forward * AxisMax + Right * AcrossMid;
            public Vector3 Centre => Origin + Forward * ((AxisMin + AxisMax) * 0.5f) + Right * AcrossMid;
        }

        private AsphaltRoad FirstLiveCell()
        {
            for (int i = 0; i < _cells.Count; i++)
                if (_cells[i] != null) return _cells[i];
            return null;
        }

        private bool TryComputeFrame(ref SpanFrame f)
        {
            var first = FirstLiveCell();
            if (first == null) return false;
            var t = first.transform;
            f.Origin = t.position;
            f.Forward = t.forward;
            f.Right = t.right;
            f.Up = t.up.sqrMagnitude > 0.0001f ? t.up.normalized : Vector3.up;
            f.Rotation = t.rotation;
            f.Cell = first.cellSize;
            f.AxisMin = 0f; f.AxisMax = 0f;
            f.AcrossMin = 0f; f.AcrossMax = 0f;

            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell == null) continue;
                if (cell.cellSize > f.Cell) f.Cell = cell.cellSize;
                Vector3 d = cell.transform.position - f.Origin;
                float a = Vector3.Dot(d, f.Forward);
                float c = Vector3.Dot(d, f.Right);
                if (a < f.AxisMin) f.AxisMin = a;
                if (a > f.AxisMax) f.AxisMax = a;
                if (c < f.AcrossMin) f.AcrossMin = c;
                if (c > f.AcrossMax) f.AcrossMax = c;
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════
        //  MEMBERSHIP
        // ════════════════════════════════════════════════════════════════

        /// <summary>Attaches a cell to this span.</summary>
        public void Adopt(AsphaltRoad cell)
        {
            if (cell == null || _cells.Contains(cell)) return;
            _cells.Add(cell);
            cell.AttachToSpan(this);
        }

        /// <summary>Detaches a cell without re-solving the span. Used when the cell is going away.</summary>
        public void Release(AsphaltRoad cell)
        {
            if (cell == null) return;
            _cells.Remove(cell);
        }

        public bool Contains(AsphaltRoad cell) => cell != null && _cells.Contains(cell);

        /// <summary>
        /// Joins the cell to the largest span among its bridge neighbours, absorbing the rest, or
        /// starts a new span. Same shape as `RoadRun.JoinOrCreate`, because a crossing that gets
        /// extended from either bank has to become one structure rather than two butted together.
        /// </summary>
        public static BridgeSpan JoinOrCreate(AsphaltRoad cell, List<AsphaltRoad> bridgeNeighbours)
        {
            BridgeSpan surviving = null;
            for (int i = 0; bridgeNeighbours != null && i < bridgeNeighbours.Count; i++)
            {
                var span = bridgeNeighbours[i]?.Span;
                if (span == null) continue;
                if (surviving == null || span.CellCount > surviving.CellCount) surviving = span;
            }
            if (surviving == null) surviving = new BridgeSpan();

            for (int i = 0; bridgeNeighbours != null && i < bridgeNeighbours.Count; i++)
            {
                var span = bridgeNeighbours[i]?.Span;
                if (span == null || span == surviving) continue;
                surviving.Absorb(span);
            }
            surviving.Adopt(cell);
            return surviving;
        }

        private void Absorb(BridgeSpan other)
        {
            if (other == null || other == this) return;
            for (int i = 0; i < other._cells.Count; i++)
            {
                var cell = other._cells[i];
                if (cell == null) continue;
                _cells.Add(cell);
                cell.AttachToSpan(this);
            }
            other._cells.Clear();
            // The longer structure's deck height and kind win: absorbing a culvert into a viaduct
            // must not lower the viaduct to the culvert.
            if (other.DeckClearance > DeckClearance) DeckClearance = other.DeckClearance;
            if (other.Structure > Structure) Structure = other.Structure;
            other.Teardown();
        }

        /// <summary>Drops the piers, leaves, beacons and the power tap. Called when the span stops
        /// being a span (absorbed) — and via `DestroyAutomation`, when a span stops being a
        /// drawbridge. The cells themselves are untouched: their saved positions, run membership and
        /// wear are not this object's to take.</summary>
        public void Teardown()
        {
            for (int i = 0; i < _piers.Count; i++)
                if (_piers[i] != null) Object.Destroy(_piers[i]);
            _piers.Clear();
            if (_leafNear != null) Object.Destroy(_leafNear);
            if (_leafFar != null) Object.Destroy(_leafFar);
            _leafNear = null; _leafFar = null;
            DestroyBeacons();
            DestroyPowerTap();
        }

        // ════════════════════════════════════════════════════════════════
        //  STRUCTURE — classification, piers, leaves, beacons
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Classifies the span from what it actually spans and rebuilds its furniture. Called after
        /// the cells settle, so a crossing extended from three cells to thirty becomes a bridge on
        /// piers rather than staying a culvert.
        /// </summary>
        /// <param name="allowDrawbridge">Lets the span classify itself as a drawbridge. Off when the
        /// player has not asked for one, so a road that happens to cross a river does not silently
        /// become something that can open under a lorry.</param>
        public void Rebuild(bool allowDrawbridge, Material deckMaterial)
        {
            if (_cells.Count == 0) { Teardown(); return; }

            DeckClearance = MeasureClearance();
            var wanted = DeckClearance < CULVERT_CLEARANCE ? BridgeStructure.Culvert
                       : allowDrawbridge ? BridgeStructure.Drawbridge
                       : BridgeStructure.Fixed;
            // Never downgrade a span the player already opened, or reloading a save would shut a
            // drawbridge that a ship was on its way through.
            if (Structure == BridgeStructure.Drawbridge && wanted == BridgeStructure.Fixed)
                wanted = BridgeStructure.Drawbridge;
            Structure = wanted;

            BuildPiers();
            if (Structure == BridgeStructure.Drawbridge)
            {
                BuildLeaves(deckMaterial);
                BuildBeacons();
            }
            else
            {
                if (_leafNear != null || _leafFar != null)
                {
                    Object.Destroy(_leafNear); Object.Destroy(_leafFar);
                    _leafNear = null; _leafFar = null;
                }
                // A culvert and a fixed bridge are structures, not machines: no beacons, no power
                // tap, no automation state. Their cells come back exactly as placed.
                DestroyBeacons();
                DestroyPowerTap();
                _stalled = false; _openedBy = OpenOwner.None; _hornPending = false;
            }
            ApplyOpen(Open01);
        }

        /// <summary>How far the deck stands above what is under it. A culvert over a streambed reads
        /// low; a bridge over a channel reads high; a deck with no bottom inside probe range reads
        /// as deep, because that is what a missing reading over water actually means.</summary>
        private float MeasureClearance()
        {
            float best = 0f;
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell == null) continue;
                Vector3 up = cell.transform.up;
                if (!AsphaltRoad.ProbeGround(cell.transform.position - up * 0.2f, up, out float below))
                {
                    if (NO_BOTTOM_CLEARANCE > best) best = NO_BOTTOM_CLEARANCE;
                    continue;
                }
                // ProbeGround reports ground ABOVE the sample point as positive, so ground below the
                // deck comes back negative and its magnitude is the clearance.
                if (-below > best) best = -below;
            }
            return best;
        }

        private void BuildPiers()
        {
            for (int i = 0; i < _piers.Count; i++)
                if (_piers[i] != null) Object.Destroy(_piers[i]);
            _piers.Clear();

            if (Structure == BridgeStructure.Culvert || _cells.Count == 0) return;

            var f = default(SpanFrame);
            if (!TryComputeFrame(ref f)) return;

            // Stations are fractions of the CROSSING, not of the cell list: a three-lane deck holds
            // three cells abreast, and indexing cells by fraction of count would triple the piers
            // and stack them under whichever lane was placed first.
            int piers = Mathf.FloorToInt(f.Length / Mathf.Max(1f, METRES_PER_PIER));
            if (piers <= 0) return;

            for (int p = 1; p <= piers; p++)
            {
                float along = Mathf.Lerp(f.AxisMin, f.AxisMax, p / (float)(piers + 1));
                Vector3 at = f.Origin + f.Forward * along + f.Right * f.AcrossMid;

                // No bottom inside probe range is deep water, not "nothing under it": the pier still
                // gets built, to the assumed depth, so a bridge over a channel does not read as a
                // slab floating in the air.
                float drop = AsphaltRoad.ProbeGround(at, f.Up, out float ground)
                    ? -ground : NO_BOTTOM_CLEARANCE;
                if (drop <= 0.2f) continue;      // genuinely nothing to stand on and nothing to span

                float width = Mathf.Max(0.35f, f.Cell * 0.22f);
                var pier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pier.name = "BridgePier";
                Object.Destroy(pier.GetComponent<Collider>());
                var box = pier.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                pier.transform.position = at - f.Up * (drop * 0.5f);
                pier.transform.rotation = f.Rotation;
                pier.transform.localScale = new Vector3(width, drop, width);
                _piers.Add(pier);
            }
        }

        private void BuildLeaves(Material deckMaterial)
        {
            if (_cells.Count < 2) return;
            _leafMaterial = deckMaterial;
            // Two leaves hinged at the abutments, meeting in the middle: the classic bascule. Built
            // as plain quads rather than as the cells themselves, so opening never moves a block the
            // save has a position for.
            _leafNear ??= MakeLeaf("BridgeLeaf_Near");
            _leafFar ??= MakeLeaf("BridgeLeaf_Far");
        }

        private GameObject MakeLeaf(string name)
        {
            var go = new GameObject(name);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            if (_leafMaterial != null) renderer.sharedMaterial = _leafMaterial;
            var mesh = new Mesh { name = name + " (runtime)" };
            mesh.hideFlags = HideFlags.DontSave;
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 1f), new Vector3(-0.5f, 0f, 1f)
            };
            // Both windings: a raised leaf is seen from the water as often as from the road, and a
            // single-sided quad vanishes from below at exactly the moment a helmsman is looking up
            // at it to judge the channel.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2,   0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            return go;
        }

        // ════════════════════════════════════════════════════════════════
        //  OPENING
        // ════════════════════════════════════════════════════════════════

        /// <summary>Asks the span to open or shut. Returns false when it cannot, with the reason.
        /// A bridge whose power tap has no cable in reach is refused here and now — that much is
        /// synchronous and certain. A bridge that is wired but whose network cannot currently
        /// afford the swing is NOT refused: power may free up, so the request is accepted and the
        /// deck stalls honestly, the same contract every machine on a power network keeps.</summary>
        public bool ToggleOpen(out string refusal)
        {
            refusal = null;
            if (Structure != BridgeStructure.Drawbridge)
            { refusal = "That crossing cannot open"; return false; }
            if (_cells.Count < 2)
            { refusal = "A drawbridge needs at least two deck cells"; return false; }

            EnsurePowerTap();
            if (_consumer != null && _consumer.network != null && _consumer.network.nodes.Count <= 1)
            { refusal = "No cable reaches this bridge - wire the deck before it can swing"; return false; }

            WantsOpen = !WantsOpen;
            _openedBy = WantsOpen ? OpenOwner.Player : OpenOwner.None;
            _hornPending = true;
            return true;
        }

        /// <summary>
        /// Restores a saved structure and swing. Called as each deck cell comes back rather than in
        /// a pass over the finished world, for the same reason `RoadRun` re-forms from adjacency:
        /// a save then needs no list of spans, only each cell's structure and how far open it was.
        /// </summary>
        public void ApplySavedState(BridgeStructure structure, float open01, Material deckMaterial)
        {
            // Set before the rebuild, not after: `Rebuild` refuses to downgrade a span that is
            // already a drawbridge, so writing the saved kind first is what stops a reloaded
            // drawbridge being reclassified down to a fixed bridge by its own clearance.
            Structure = structure;
            Rebuild(allowDrawbridge: structure == BridgeStructure.Drawbridge, deckMaterial: deckMaterial);
            SetOpenImmediate(open01);
            // An open deck belongs to the automation: it opens decks for ships, so it is the one
            // that should shut this one when the water is clear again. A deck restored mid-swing
            // simply finishes the move its save caught it in.
            _openedBy = open01 > 0.5f ? OpenOwner.Auto : OpenOwner.None;
            _prevHoldOccupied = false;
            _holdUntil = 0f;
            _stallNoticeShown = false;
        }

        private static readonly List<AsphaltRoad> _restoreNeighbours = new List<AsphaltRoad>(8);
        private static readonly List<AsphaltRoad> _restoreQuery = new List<AsphaltRoad>(8);

        /// <summary>
        /// Groups a restored deck cell with the bridge cells already back in the world and rebuilds
        /// the crossing. Probes the four face-neighbour slots rather than scanning the scene: a
        /// crossing is contiguous by definition, so anything it belongs to is one cell away.
        /// </summary>
        public static void RestoreCell(AsphaltRoad cell, BridgeStructure structure, float open01,
                                       Material deckMaterial)
        {
            if (cell == null) return;
            _restoreNeighbours.Clear();
            var t = cell.transform;
            float step = Mathf.Max(0.25f, cell.cellSize);
            Vector3 fwd = t.forward * step, right = t.right * step;
            for (int i = 0; i < 4; i++)
            {
                Vector3 probe = i == 0 ? t.position + fwd
                              : i == 1 ? t.position - fwd
                              : i == 2 ? t.position + right
                              : t.position - right;
                RoadSurfaceUtility.QueryAt(probe, _restoreQuery);
                for (int q = 0; q < _restoreQuery.Count; q++)
                {
                    var other = _restoreQuery[q];
                    if (other == null || other == cell) continue;
                    if (other.surfaceKind != RoadSurfaceKind.Bridge) continue;
                    if (!_restoreNeighbours.Contains(other)) _restoreNeighbours.Add(other);
                }
            }
            JoinOrCreate(cell, _restoreNeighbours).ApplySavedState(structure, open01, deckMaterial);
        }

        /// <summary>Restores a saved open state without animating through it.</summary>
        public void SetOpenImmediate(float open01)
        {
            Open01 = Mathf.Clamp01(open01);
            WantsOpen = Open01 > 0.5f;
            _hornPending = false;         // a reloaded bridge does not greet the world
            _stallNoticeShown = false;
            ApplyOpen(Open01);
        }

        /// <summary>Advances the swing and runs the automation. Called from the owning cell's update,
        /// which already runs on a stagger, so a span costs one enum test while it sits still and
        /// nothing at all when it is not a drawbridge.</summary>
        public void Tick(float deltaTime)
        {
            if (Structure != BridgeStructure.Drawbridge) return;

            EnsurePowerTap();

            // The scan is the automation's eyes, and a SHUT bridge is exactly the one that needs
            // them — the whole point is that the deck is already moving by the time the helm does.
            _scanTimer -= deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = SCAN_SECONDS;
                ScanForVessels();
            }

            float target = WantsOpen ? 1f : 0f;
            bool settled = Mathf.Approximately(Open01, target);

            if (settled)
            {
                _stalled = false;
                _stallNoticeShown = false;
                if (_consumer != null) _consumer.wattsPerSecond = 0f;   // a still deck is a free deck
                TickBeacon(Open01 >= 0.99f ? BeaconMode.Steady : BeaconMode.Dark, deltaTime);
                return;
            }

            // The deck wants to move. The motor draws what it draws — asked one network tick early,
            // the same discipline the refuel pad pumps by, because `IsPowered` is the network's
            // verdict on the demand before it, not on the demand just pushed.
            if (_consumer != null) _consumer.wattsPerSecond = SWING_WATTS;
            if (_consumer == null || !_consumer.IsPowered)
            {
                _stalled = true;
                TickBeacon(BeaconMode.Flash, deltaTime);   // flashing includes "trying and failing"
                ReportStall();
                return;
            }
            _stalled = false;

            if (_hornPending)
            {
                // The horn sounds when the deck actually starts moving, not when the request was
                // made: a horn followed by nothing moving is a lie told at the water.
                _hornPending = false;
                var f = default(SpanFrame);
                if (TryComputeFrame(ref f))
                    AudioManager.PlayAt(SfxLibrary.Get(Sfx.BridgeHorn), f.Centre,
                                        volume: 0.85f, pitch: 1f, maxDistance: HORN_RANGE);
            }

            Open01 = Mathf.MoveTowards(Open01, target, deltaTime / Mathf.Max(0.25f, SWING_SECONDS));
            ApplyOpen(Open01);
            TickBeacon(BeaconMode.Flash, deltaTime);
        }

        // ════════════════════════════════════════════════════════════════
        //  AUTOMATION — the vessel scan
        // ════════════════════════════════════════════════════════════════

        private static readonly Collider[] _vesselProbe = new Collider[PROBE_LIMIT];
        private static readonly Collider[] _deckProbe = new Collider[16];

        /// <summary>
        /// Watches the channel for hulls and decides whether the deck should be open. One physics
        /// probe covers the HOLD volume (the reach times `HOLD_FACTOR`); each grid found is then
        /// classified by its distance along the channel axis, so the approach and hold verdicts come
        /// from the same sweep.
        ///
        /// A grid counts as a vessel only if it is genuinely IN the water — `WaterProbeSystem`, the
        /// same probe the boats float on — and below the deck line. A car on the deck is above both
        /// tests; a shuttle flying overhead is in neither. That is the "query into the maritime
        /// stack" the roadmap asked for, and it is one call, not a new system.
        /// </summary>
        private void ScanForVessels()
        {
            var f = default(SpanFrame);
            if (!TryComputeFrame(ref f)) return;

            float holdReach = APPROACH_METRES * HOLD_FACTOR;
            // The box hangs under the deck and reaches down the channel both ways. Its height covers
            // the water surface the hulls sit on; anything above the deck line is filtered out
            // below rather than by the box, because a box edge is a poor place to make decisions.
            Vector3 centre = f.Centre - f.Up * (DeckClearance * 0.55f);
            Vector3 half = new Vector3(holdReach,
                                       Mathf.Max(2.5f, DeckClearance * 0.55f + 1.2f),
                                       f.Width * 0.5f + 6f);
            int hits = Physics.OverlapBoxNonAlloc(centre, half, _vesselProbe, f.Rotation, ~0,
                                                  QueryTriggerInteraction.Ignore);

            bool approach = false, hold = false;
            for (int i = 0; i < hits; i++)
            {
                var col = _vesselProbe[i];
                if (col == null) continue;
                var grid = col.GetComponentInParent<GridEntity>();
                if (grid == null) continue;

                Vector3 pos = grid.Body != null ? grid.Body.worldCenterOfMass : grid.transform.position;
                if (WaterProbeSystem.GetSubmergence(pos, 1f) < 0.05f) continue;   // not floating
                if (Vector3.Dot(pos - f.Centre, f.Up) > -0.4f) continue;          // on/above the deck

                float alongChannel = Mathf.Abs(Vector3.Dot(pos - f.Centre, f.Right));
                if (alongChannel <= holdReach) hold = true;
                if (alongChannel <= APPROACH_METRES) approach = true;
                if (hold && approach) break;
            }

            if (hold) _holdUntil = Time.time + CLEAR_GRACE_SECONDS;

            if (_openedBy == OpenOwner.Auto && WantsOpen && !hold
                && Time.time >= _holdUntil && !DeckOccupied(f))
            {
                // Shut what the automation opened, once the water has been clear past the grace and
                // nothing is standing on the deck. The occupied check is the guard that makes the
                // automation safe to leave alone: a stalled lorry mid-deck postpones the shut, it is
                // never closed around.
                WantsOpen = false;
                _openedBy = OpenOwner.None;
                _hornPending = true;
            }
            else if (!_prevHoldOccupied && approach && !WantsOpen)
            {
                // Open on a FRESH arrival — the edge from "water clear" to "hull inside the approach"
                // — and on nothing else. A deck the player shut while a vessel idled downstream stays
                // shut until the water actually clears and somebody new comes up the channel; the
                // automation must never argue with the hand on the lever.
                WantsOpen = true;
                _openedBy = OpenOwner.Auto;
                _hornPending = true;
            }
            _prevHoldOccupied = hold;
        }

        /// <summary>Whether anything stands on the deck right now: a box just above the road
        /// surface, tall enough for a lorry. Road surfaces themselves are excluded — a neighbouring
        /// slab sits at the box's floor and is pavement, not an occupant.</summary>
        private static bool DeckOccupied(in SpanFrame f)
        {
            Vector3 centre = f.Centre + f.Up * (DECK_OCCUPIED_HEIGHT * 0.5f + 0.15f);
            Vector3 half = new Vector3(f.Width * 0.5f + 0.5f,
                                       DECK_OCCUPIED_HEIGHT * 0.5f,
                                       f.Length * 0.5f);
            int hits = Physics.OverlapBoxNonAlloc(centre, half, _deckProbe, f.Rotation, ~0,
                                                  QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                var col = _deckProbe[i];
                if (col == null || col.isTrigger) continue;
                if (col.GetComponentInParent<AsphaltRoad>() != null) continue;   // the road itself
                return true;
            }
            return false;
        }

        /// <summary>One honest sentence about a deck that cannot move, shown once per stall and only
        /// to someone close enough to be standing at the crossing. A popup about a distant bridge is
        /// noise; a silent bridge that ignores its lever is a mystery.</summary>
        private void ReportStall()
        {
            if (_stallNoticeShown) return;
            var cam = Camera.main;
            if (cam == null) return;
            var f = default(SpanFrame);
            if (!TryComputeFrame(ref f)) return;
            if ((cam.transform.position - f.Centre).sqrMagnitude > STALL_NOTICE_RANGE * STALL_NOTICE_RANGE) return;
            _stallNoticeShown = true;
            BuildFeedbackHud.Show("Drawbridge",
                "No power to swing - run a cable to the deck",
                null, new Color(1f, 0.62f, 0.35f));
        }

        // ════════════════════════════════════════════════════════════════
        //  POWER — the tap the swing is billed through
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Keeps exactly one `PowerConsumer` alive, seated on the abutment cell (the first live
        /// cell) where a cable from the bank can reach it. If that cell goes away — mined, lifted —
        /// the tap moves to the new abutment on the next tick, which is the same self-heal the
        /// refuel pad performs on its buffers. Idle demand is always zero; the swing sets it.</summary>
        private void EnsurePowerTap()
        {
            var host = FirstLiveCell();
            if (host == null) return;
            if (_consumer != null && _powerHost == host.gameObject) return;

            if (_consumer != null) { Object.Destroy(_consumer); _consumer = null; }
            _consumer = host.gameObject.GetComponent<PowerConsumer>();
            if (_consumer == null)
            {
                _consumer = host.gameObject.AddComponent<PowerConsumer>();
                _consumer.connectRadius = POWER_CONNECT_RADIUS;
            }
            _consumer.wattsPerSecond = 0f;
            _powerHost = host.gameObject;
        }

        private void DestroyPowerTap()
        {
            if (_consumer != null) Object.Destroy(_consumer);
            _consumer = null;
            _powerHost = null;
        }

        // ════════════════════════════════════════════════════════════════
        //  WARNINGS — the beacons and their states
        // ════════════════════════════════════════════════════════════════

        /// <summary>One abutment beacon: a post, a lens and a lamp. Dark while the deck is shut and
        /// idle, FLASHING while it moves or strains to, steady red while the channel is open. A
        /// silent deck that drops shut is a trap, and the flashing state deliberately covers the
        /// stalled swing too — "this bridge is trying to move" is the fact that matters at 30 m.</summary>
        private sealed class Beacon
        {
            public GameObject Root;
            public Renderer Lens;
            public Light Lamp;
        }

        private void BuildBeacons()
        {
            _beaconNear ??= MakeBeacon("BridgeBeacon_Near");
            _beaconFar ??= MakeBeacon("BridgeBeacon_Far");
        }

        private static Beacon MakeBeacon(string name)
        {
            var beacon = new Beacon { Root = new GameObject(name) };

            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(post.GetComponent<Collider>());   // furniture, not an obstacle
            post.name = "Post";
            post.transform.SetParent(beacon.Root.transform, false);
            post.transform.localScale = new Vector3(0.09f, 0.86f, 0.09f);
            post.transform.localPosition = new Vector3(0f, 0.43f, 0f);
            post.GetComponent<Renderer>().sharedMaterial = BeaconPostMaterial;

            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(lens.GetComponent<Collider>());
            lens.name = "Lens";
            lens.transform.SetParent(beacon.Root.transform, false);
            lens.transform.localScale = Vector3.one * 0.15f;
            lens.transform.localPosition = new Vector3(0f, 0.92f, 0f);
            beacon.Lens = lens.GetComponent<Renderer>();
            beacon.Lens.sharedMaterial = BeaconOffMaterial;

            var lamp = lens.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = BeaconRed;
            lamp.range = 7f;
            lamp.intensity = 2.2f;
            lamp.enabled = false;
            beacon.Lamp = lamp;

            return beacon;
        }

        private void DestroyBeacons()
        {
            if (_beaconNear != null) Object.Destroy(_beaconNear.Root);
            if (_beaconFar != null) Object.Destroy(_beaconFar.Root);
            _beaconNear = null; _beaconFar = null;
            _beaconMode = BeaconMode.Dark;
            _beaconLit = false;
        }

        private void TickBeacon(BeaconMode wanted, float deltaTime)
        {
            if (wanted != _beaconMode)
            {
                _beaconMode = wanted;
                _beaconLit = wanted == BeaconMode.Steady || wanted == BeaconMode.Flash;
                _beaconTimer = BEACON_BLINK_SECONDS;
                ApplyBeaconVisual(_beaconLit);
            }
            else if (_beaconMode == BeaconMode.Flash)
            {
                _beaconTimer -= deltaTime;
                if (_beaconTimer <= 0f)
                {
                    _beaconTimer = BEACON_BLINK_SECONDS;
                    _beaconLit = !_beaconLit;
                    ApplyBeaconVisual(_beaconLit);
                }
            }
        }

        private void ApplyBeaconVisual(bool lit)
        {
            SetLens(_beaconNear, lit);
            SetLens(_beaconFar, lit);
        }

        private static void SetLens(Beacon beacon, bool lit)
        {
            if (beacon == null || beacon.Lens == null) return;
            beacon.Lens.sharedMaterial = lit ? BeaconOnMaterial : BeaconOffMaterial;
            if (beacon.Lamp != null) beacon.Lamp.enabled = lit;
        }

        private static readonly Color BeaconRed = new Color(1f, 0.16f, 0.10f);
        private static Material _beaconPostMat, _beaconOffMat, _beaconOnMat;

        /// <summary>Shared, lazily built, and never instanced per span: every drawbridge in the world
        /// blinks through the same three materials, and the blink is a swap between two of them
        /// rather than a per-frame colour write.</summary>
        private static Material BeaconPostMaterial
        {
            get
            {
                if (_beaconPostMat != null) return _beaconPostMat;
                _beaconPostMat = new Material(BeaconShader) { name = "Mat_BridgeBeaconPost (runtime)" };
                SetMatColour(_beaconPostMat, new Color(0.16f, 0.17f, 0.19f));
                SetMatFloat(_beaconPostMat, "_Metallic", 0.8f);
                SetMatFloat(_beaconPostMat, "_Smoothness", 0.35f);
                SetMatFloat(_beaconPostMat, "_Glossiness", 0.35f);
                return _beaconPostMat;
            }
        }

        private static Material BeaconOffMaterial
        {
            get
            {
                if (_beaconOffMat != null) return _beaconOffMat;
                _beaconOffMat = new Material(BeaconShader) { name = "Mat_BridgeBeaconOff (runtime)" };
                SetMatColour(_beaconOffMat, new Color(0.30f, 0.06f, 0.05f));
                return _beaconOffMat;
            }
        }

        private static Material BeaconOnMaterial
        {
            get
            {
                if (_beaconOnMat != null) return _beaconOnMat;
                _beaconOnMat = new Material(BeaconShader) { name = "Mat_BridgeBeaconOn (runtime)" };
                SetMatColour(_beaconOnMat, BeaconRed);
                _beaconOnMat.EnableKeyword("_EMISSION");
                _beaconOnMat.SetColor("_EmissionColor", BeaconRed * 2.4f);
                return _beaconOnMat;
            }
        }

        private static Shader BeaconShader
            => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        private static void SetMatColour(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void SetMatFloat(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        // ════════════════════════════════════════════════════════════════
        //  PUSHING STATE INTO THE WORLD
        // ════════════════════════════════════════════════════════════════

        /// <summary>Pushes the open fraction into the world: leaves up, beacons placed, and the deck
        /// cells out of the way so nothing drives into a channel that is supposed to be open.</summary>
        private void ApplyOpen(float open01)
        {
            bool passable = open01 < 0.5f;
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell == null) continue;
                cell.SetDeckPassable(passable);
            }

            var f = default(SpanFrame);
            if (!TryComputeFrame(ref f) || _cells.Count < 2) return;

            // The leaves hinge at the abutments and meet in the middle. Both tilt UP by the swing
            // angle; the far leaf is simply mirrored (a 180° turn about the deck's up) so it extends
            // from the far abutment BACK toward the centre — an unmirrored far leaf points away
            // from its own crossing and dives at the water. Width is the deck's full width: a leaf
            // one lane wide on a three-lane deck would be a ribbon, not a road.
            float half = f.Length * 0.5f;
            float angle = open01 * LEAF_ANGLE;
            if (_leafNear != null)
                PlaceLeaf(_leafNear, f.NearAbutment, f.Rotation, f.Width, half, angle, mirror: false);
            if (_leafFar != null)
                PlaceLeaf(_leafFar, f.FarAbutment, f.Rotation, f.Width, half, angle, mirror: true);

            // The beacons stand on the APPROACH, just off the kerb and past the end of the deck:
            // a post on the deck itself would float over vanished cells while the span is open and
            // be swept through by the rising leaf, and a warning light nobody can see from the
            // water is not one.
            if (_beaconNear != null)
                PlaceBeacon(_beaconNear, f.NearAbutment, ref f, -1f);
            if (_beaconFar != null)
                PlaceBeacon(_beaconFar, f.FarAbutment, ref f, 1f);
        }

        private static void PlaceLeaf(GameObject leaf, Vector3 abutment, Quaternion baseRotation,
                                      float width, float length, float angle, bool mirror)
        {
            Quaternion tilt = Quaternion.Euler(-angle, 0f, 0f);
            leaf.transform.position = abutment;
            leaf.transform.rotation = mirror
                ? baseRotation * Quaternion.Euler(0f, 180f, 0f) * tilt
                : baseRotation * tilt;
            leaf.transform.localScale = new Vector3(Mathf.Max(0.2f, width), 1f,
                                                     Mathf.Max(0.2f, length));
        }

        /// <param name="side">-1 for the near (start) abutment, +1 for the far one — the direction
        /// the approach road continues past the crossing.</param>
        private static void PlaceBeacon(Beacon beacon, Vector3 abutment, ref SpanFrame f, float side)
        {
            if (beacon?.Root == null) return;
            Vector3 at = abutment
                       + f.Forward * side * (f.Cell * 0.5f + 0.35f)
                       + f.Right * (f.Width * 0.5f + 0.45f);
            beacon.Root.transform.SetPositionAndRotation(at, f.Rotation);
        }
    }
}
