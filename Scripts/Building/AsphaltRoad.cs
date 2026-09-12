// Assets/Scripts/VoxelEngine/Building/AsphaltRoad.cs
//
// THE ASPHALT ROAD — a paved surface the player lays on terrain.
//
// A road is the difference between walking a route and being able to run a schedule on it. This
// block is the surface half of that sentence: one 1 m cell of hot mix, draped over the ground it
// was laid on, that auto-shapes itself from the cells around it and wears out under traffic.
//
// WHAT IT IS
//   • A `PlacedBlock` like every other world block, so mining, damage, painting, saves and the
//     inspection overlay all already work on it. Nothing here re-implements a placed block.
//   • A DRAPED surface. The slab is built through five ground samples per cell (centre plus four
//     corners, plus the shared subdivision points), so a road follows a gully or a terrace
//     instead of floating over it or burying itself in it. Ramps therefore need no special case —
//     the drape IS the ramp.
//   • AUTO-SHAPED from a neighbour mask. Two opposite neighbours read as a straight, two adjacent
//     as a bend, three as a junction, four as a crossing, none as an isolated patch. What the mask
//     actually changes is the aggregate shoulder: an unconnected edge gets a raised kerb, a
//     connected edge does not, so a run reads as one continuous strip rather than a row of butted
//     slabs. There is no shape the player picks and no shape prefab to author.
//   • A member of a `RoadRun`, which owns the wear. Wear is per run, so a corridor wears as one
//     surface and a repair is one gesture.
//
// WHAT IT IS WORTH — the bonuses live in `RoadRun`, not here, so the player's feet and a rig's
// tyres read the same curve and cannot drift apart. On foot it is a walk-speed multiplier; for a
// wheel it is drive traction and lateral grip, which is felt as acceleration and hill-holding
// rather than as a flat top-speed cheat.
//
// WHAT KEEPS IT FROM BEING FREE
//   • Grading. Rough ground costs double; ground rougher than `maxGradeRoughness` is refused until
//     the player levels the strip. The ground truth of the voxel terrain is not bypassed.
//   • Wear. Traffic bills the run, a worn run loses its bonus, and a broken-up run is worse than
//     the dirt it replaced. That is what turns paving into an upkeep economy.
//   • Volume. Underwater cells and cells buried inside a wall are refused: a road does not run
//     through a body or a wall without a culvert or a bridge.
//
// DELIBERATELY NOT HERE — the road network object (a name, a connected-run trace, a traffic
// readout) and the routing it feeds. The roadmap holds that back until autopilot and drone
// scheduling exist to consume it; a `RoadRun` is a wear pool, not a register.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;        // ActiveWorld + Voxel, for the underwater / buried grade refusals
using VoxelEngine.Environment;

namespace VoxelEngine.Building
{
    /// <summary>What the cell is paved with. They cost differently and behave differently: asphalt
    /// carries vehicles, cobble carries feet, and a bridge deck carries both across something that
    /// is not ground.
    ///
    /// `Bridge` is APPENDED, never inserted: a save stores this as an int, so every surface a
    /// player has already laid keeps its meaning across the upgrade.</summary>
    public enum RoadSurfaceKind { Asphalt, Pathway, Bridge }

    /// <summary>The surface the paver will lay next. Held here rather than on the tool so the
    /// choice survives switching hotbar slots, the way the conveyor shape wheel does.</summary>
    public static class RoadSurfaceSelection
    {
        public static RoadSurfaceKind Kind = RoadSurfaceKind.Asphalt;
    }
    [DisallowMultipleComponent, RequireComponent(typeof(PlacedBlock))]
    public class AsphaltRoad : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════
        //  AUTHORED TUNING
        // ════════════════════════════════════════════════════════════════

        [Header("Surface")]
        /// <summary>Asphalt carries wheels; Pathway is cobble for feet. Drives the bonus this cell
        /// hands out and whether wheels are allowed to wear it down.</summary>
        public RoadSurfaceKind surfaceKind = RoadSurfaceKind.Asphalt;

        [Header("Curve Frame")]
        /// <summary>Cross-section direction of the route where it enters and leaves this cell, in
        /// local space, plus the mitre stretch at each joint. Zero on a straight, on a junction and
        /// on anything hand-placed: those are square. Set by the planner on bent cells so a chain
        /// of them tiles into one continuous arc instead of a row of notched squares.</summary>
        [HideInInspector] public Vector3 curveInRight;
        [HideInInspector] public Vector3 curveOutRight;
        [HideInInspector] public float curveInMitre = 1f;
        [HideInInspector] public float curveOutMitre = 1f;

        [Header("Explicit Footprint")]
        /// <summary>Set by the corridor solver (`RoadCorridor`) on cells it laid from a shared
        /// station grid. When true the four corners below ARE the cell's footprint, in local space,
        /// and the curve frame above is ignored.
        ///
        /// This exists because a lane of a curved carriageway cannot be described by two chords
        /// centred on the cell's own axis: its entry and exit faces are chords of the whole
        /// carriageway's cross-sections, so they sit off-axis and skew against the cell. Giving the
        /// solver four explicit corners lets every lane of every cell meet its neighbours exactly.
        ///
        /// Legacy and hand-placed cells leave this false and take the curve-frame path unchanged.</summary>
        [HideInInspector] public bool hasExplicitFootprint;
        [HideInInspector] public Vector3 quadSW, quadSE, quadNE, quadNW;

        [Header("Cell")]
        [Tooltip("Edge length of one road cell in metres. Matches the BlockItem's gridSize so a " +
                 "paved strip lands on the same lattice as every other placed block.")]
        public float cellSize = 1f;

        [Tooltip("Slab thickness in metres. Thin on purpose: a road is a surface, not a foundation, " +
                 "and a thick slab reads as a wall from the side.")]
        public float thickness = 0.08f;

        [Tooltip("How far the raised aggregate kerb reaches in from an unconnected edge.")]
        public float shoulderWidth = 0.09f;

        [Tooltip("How proud of the asphalt the aggregate kerb stands.")]
        public float shoulderRise = 0.022f;

        [Header("Grading")]
        [Tooltip("Ground height spread across a cell (metres) that still counts as smooth and costs " +
                 "one unit of material.")]
        public float maxGradeSmooth = 0.22f;

        [Tooltip("Ground height spread that costs double material and drapes as a bumpy patch. " +
                 "Anything rougher than this is refused until the strip is levelled.")]
        public float maxGradeRoughness = 0.50f;

        [Header("Wear")]
        [Tooltip("Wear weight of a pair of feet crossing one cell. A rig bills the run far harder; " +
                 "see `RegisterTraffic`.")]
        public float footTrafficLoad = 1f;

        [Tooltip("Wear weight of one wheel crossing one cell. Scales with the grid's mass so a loaded " +
                 "lorry shreds a road and a light buggy does not.")]
        public float wheelTrafficLoad = 14f;

        [Header("Maintenance")]
        [Tooltip("How often this cell re-checks the ground under it and its neighbours, in seconds. " +
                 "Slow and phase-staggered: terrain can be mined out from under a road, but a " +
                 "400-cell highway must not cost 400 raycasts a second to notice.")]
        public float recheckSeconds = 6f;

        // ════════════════════════════════════════════════════════════════
        //  RUNTIME STATE
        // ════════════════════════════════════════════════════════════════

        /// <summary>The wear pool this cell belongs to. Never null once enabled.</summary>
        public RoadRun Run { get; private set; }

        /// <summary>The water crossing this cell is part of, or null for ordinary pavement. A cell
        /// has both a run and a span on purpose: the run is the wear ledger for the whole road, the
        /// span is the structure over one gap. See `BridgeSpan`.</summary>
        public BridgeSpan Span { get; private set; }

        /// <summary>Connected edges, resolved from the neighbours actually present.</summary>
        public RoadEdgeMask Edges { get; private set; } = RoadEdgeMask.None;

        /// <summary>Human-readable shape word for the inspection card.</summary>
        public string ShapeLabel => DescribeShape(Edges);

        /// <summary>True while the cell has solid ground under it. An unsupported cell is rubble
        /// and gives nothing.</summary>
        public bool IsSupported { get; private set; }

        private MeshFilter _surfaceFilter;
        private MeshRenderer _surfaceRenderer;
        private MeshCollider _surfaceCollider;
        private MeshRenderer _wearRenderer;
        private MeshFilter _wearFilter;
        private Mesh _surfaceMesh;
        private Mesh _wearMesh;
        private AsphaltRoadMesh.HeightSampler _sampler;
        private MaterialPropertyBlock _surfaceProperties;
        private float _recheckTimer;
        private bool _refreshQueued;

        private static readonly List<AsphaltRoad> _neighbourScratch = new List<AsphaltRoad>(8);
        private static readonly List<AsphaltRoad> _queryScratch = new List<AsphaltRoad>(8);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Fresh hot mix is near-black and slightly blue-grey from the bitumen film. As it oxidises
        // the binder recedes and the pale aggregate shows, so the surface FADES toward grey — that
        // is the cue a driver actually reads, and it is why the ramp lightens rather than darkens.
        private static readonly Color FreshTint  = new Color(1.00f, 1.00f, 1.00f);
        private static readonly Color WornTint   = new Color(1.16f, 1.15f, 1.12f);
        private static readonly Color BrokenTint = new Color(1.34f, 1.31f, 1.24f);

        // Cobble ages the other way. Asphalt BLEACHES as the binder oxidises and the aggregate
        // pales; hand-laid stone goes down in value as mud, moss and loose grit work into the
        // joints, so a worn pathway darkens and greens rather than lightening.
        private static readonly Color CobbleWornTint   = new Color(0.88f, 0.89f, 0.84f);
        private static readonly Color CobbleBrokenTint = new Color(0.74f, 0.77f, 0.68f);

        // ════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ════════════════════════════════════════════════════════════════

        private void Awake()
        {
            Run ??= new RoadRun();
            _surfaceProperties = new MaterialPropertyBlock();
            EnsureChildren();
        }

        private void OnEnable()
        {
            // A road enabling means the world is live again: clear the teardown guard, which would
            // otherwise stay set across an Editor play-session boundary when domain reload is off.
            _worldTearingDown = false;
            IsSupported = SampleGroundHeights() != null;
            RoadSurfaceUtility.Register(this);
            RebuildMesh();
            // Phase-stagger the periodic recheck so a long strip does not spike on one frame.
            _recheckTimer = Random.Range(0f, Mathf.Max(0.5f, recheckSeconds));
            // Resolve on the next frame as well as now. A hand-placed or paved cell gets
            // `RefreshAfterPlacement` immediately, but a cell restored from a save does not, and
            // without this a loaded highway would sit as isolated patches for up to `recheckSeconds`
            // before snapping into shape. One frame of latency is invisible; six seconds is not.
            _refreshQueued = true;
            ScheduleNeighbourRefresh();
        }

        private void OnDisable()
        {
            RoadSurfaceUtility.Unregister(this);
        }

        private void OnDestroy()
        {
            // A mined road must re-split its run exactly like a scraped one, or the run keeps
            // counting a cell that is gone and bills wear against a length it no longer has.
            // Skipped during teardown, where every neighbour is going away anyway.
            if (!_worldTearingDown) DetachFromRun();
            RoadSurfaceUtility.Unregister(this);
            if (_surfaceMesh != null) Destroy(_surfaceMesh);
            if (_wearMesh != null) Destroy(_wearMesh);
            _surfaceMesh = null;
            _wearMesh = null;
        }

        private void Update()
        {
            if (_refreshQueued)
            {
                _refreshQueued = false;
                ResolveTopology(rebuild: true);
            }

            TickSpan();

            _recheckTimer -= Time.deltaTime;
            if (_recheckTimer > 0f) return;
            _recheckTimer = Mathf.Max(1f, recheckSeconds);

            // Terrain can be mined out from under a road, and a neighbour can arrive through a
            // path that did not notify us. Re-drape only when something actually moved.
            bool wasSupported = IsSupported;
            IsSupported = SampleGroundHeights() != null;
            RoadEdgeMask previous = Edges;
            ResolveTopology(rebuild: false);
            if (IsSupported != wasSupported || Edges != previous) RebuildMesh();

            // The run only notifies its cells when the CONDITION BAND changes, which is four times
            // over a road's whole life. Cracks are quantised finer than that, so poll here — this
            // tick already runs on a stagger, and `ApplyWearVisuals` is a no-op unless a rebuild
            // step has actually been crossed.
            else ApplyWearVisuals();
        }

        /// <summary>Creates the two child objects that carry the surface and the wear decals.
        /// Authored on the prefab by Setup Step 72 and re-created here so a hand-built or legacy
        /// prefab still works.</summary>
        private void EnsureChildren()
        {
            var visuals = transform.Find("Visuals");
            if (visuals == null)
            {
                var go = new GameObject("Visuals");
                go.transform.SetParent(transform, false);
                visuals = go.transform;
            }

            var surface = visuals.Find("Surface");
            if (surface == null)
            {
                var go = new GameObject("Surface");
                go.transform.SetParent(visuals, false);
                surface = go.transform;
            }
            _surfaceFilter   = surface.GetComponent<MeshFilter>();
            if (_surfaceFilter == null) _surfaceFilter = surface.gameObject.AddComponent<MeshFilter>();
            _surfaceRenderer = surface.GetComponent<MeshRenderer>();
            if (_surfaceRenderer == null) _surfaceRenderer = surface.gameObject.AddComponent<MeshRenderer>();
            _surfaceCollider = surface.GetComponent<MeshCollider>();
            if (_surfaceCollider == null) _surfaceCollider = surface.gameObject.AddComponent<MeshCollider>();
            _surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _surfaceRenderer.receiveShadows = true;

            var wear = visuals.Find("WearPatches");
            if (wear == null)
            {
                var go = new GameObject("WearPatches");
                go.transform.SetParent(visuals, false);
                wear = go.transform;
            }
            _wearFilter   = wear.GetComponent<MeshFilter>();
            if (_wearFilter == null) _wearFilter = wear.gameObject.AddComponent<MeshFilter>();
            _wearRenderer = wear.GetComponent<MeshRenderer>();
            if (_wearRenderer == null) _wearRenderer = wear.gameObject.AddComponent<MeshRenderer>();
            _wearRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _wearRenderer.receiveShadows = true;
        }

        // ════════════════════════════════════════════════════════════════
        //  TOPOLOGY
        // ════════════════════════════════════════════════════════════════

        /// <summary>Asks every road within reach to re-resolve. Called after a placement or a
        /// removal so a strip reshapes itself the moment its topology changes.</summary>
        public void ScheduleNeighbourRefresh()
        {
            RoadSurfaceUtility.QueryAt(transform.position, _queryScratch);
            for (int i = 0; i < _queryScratch.Count; i++)
            {
                var other = _queryScratch[i];
                if (other == null || other == this) continue;
                if (Vector3.Distance(other.transform.position, transform.position) > cellSize * 1.75f) continue;
                other._refreshQueued = true;
            }
            _refreshQueued = false;
        }

        /// <summary>Fills the scratch list with the roads this cell is actually connected to.</summary>
        public List<AsphaltRoad> CollectConnectedNeighbours()
        {
            _neighbourScratch.Clear();
            Vector3 up = transform.up;
            RoadSurfaceUtility.QueryAt(transform.position, _queryScratch);
            for (int i = 0; i < _queryScratch.Count; i++)
            {
                var other = _queryScratch[i];
                if (other == null || other == this) continue;
                if (FindNeighbourSlot(other, up) != RoadEdgeMask.None && !_neighbourScratch.Contains(other))
                    _neighbourScratch.Add(other);
            }
            return _neighbourScratch;
        }

        /// <summary>Which edge of this cell <paramref name="other"/> sits on, or None when it is
        /// not a neighbour at all.</summary>
        private RoadEdgeMask FindNeighbourSlot(AsphaltRoad other, Vector3 up)
        {
            if (RoadSurfaceUtility.IsNeighbourSlot(this, other, transform.forward, up, cellSize)) return RoadEdgeMask.North;
            if (RoadSurfaceUtility.IsNeighbourSlot(this, other, transform.right,    up, cellSize)) return RoadEdgeMask.East;
            if (RoadSurfaceUtility.IsNeighbourSlot(this, other, -transform.forward, up, cellSize)) return RoadEdgeMask.South;
            if (RoadSurfaceUtility.IsNeighbourSlot(this, other, -transform.right,   up, cellSize)) return RoadEdgeMask.West;
            return RoadEdgeMask.None;
        }

        private readonly List<AsphaltRoad> _connectedScratch = new List<AsphaltRoad>(4);

        private void ResolveTopology(bool rebuild)
        {
            Vector3 up = transform.up;
            RoadSurfaceUtility.QueryAt(transform.position, _queryScratch);

            var mask = RoadEdgeMask.None;
            var connected = _connectedScratch;
            connected.Clear();
            for (int i = 0; i < _queryScratch.Count; i++)
            {
                var other = _queryScratch[i];
                if (other == null || other == this) continue;
                var slot = FindNeighbourSlot(other, up);
                if (slot == RoadEdgeMask.None) continue;
                mask |= slot;
                if (!connected.Contains(other)) connected.Add(other);
            }

            bool maskChanged = mask != Edges;
            // Only a GAINED connection may re-join runs. A lost one is handled by `DetachFromRun`,
            // which splits the run apart deliberately; letting the survivors merge back into the
            // largest neighbour would undo that split the frame after it happened.
            bool gained = (mask & ~Edges) != RoadEdgeMask.None;
            Edges = mask;

            if (Run == null || gained)
                Run = RoadRun.JoinOrCreate(this, connected);
            else if (!Run.Contains(this))
                Run.Adopt(this);

            if (rebuild && maskChanged) RebuildMesh();
            ApplyWearVisuals();
        }

        /// <summary>Called by `BuildSystem` and the paver right after a cell is committed.</summary>
        public void RefreshAfterPlacement()
        {
            IsSupported = SampleGroundHeights() != null;
            ResolveTopology(rebuild: true);
            ScheduleNeighbourRefresh();
        }

        /// <summary>
        /// Called when this cell is lifted. Leaves the registry FIRST, so the re-split below cannot
        /// see the cell that is going away and stitch two runs back together through it, then hands
        /// the former neighbours whatever runs they now form.
        /// </summary>
        public void DetachFromRun()
        {
            RoadSurfaceUtility.Unregister(this);
            DetachFromSpan();
            var run = Run;
            var former = new List<AsphaltRoad>(CollectConnectedNeighbours());
            Run = null;
            run?.Release(this);
            // Only a cell that was actually holding two or more neighbours together can split a run,
            // so the flood fill is skipped for the common cases (an end cell, a lone patch). That is
            // what keeps lifting one cell out of a 400-cell highway from costing a full re-solve.
            if (former.Count < 2) return;
            RoadRun.ResplitAround(former, run?.Wear01 ?? 0f);
            for (int i = 0; i < former.Count; i++) former[i]?.ScheduleNeighbourRefresh();
        }

        // ════════════════════════════════════════════════════════════════
        //  SPAN (bridge cells only)
        // ════════════════════════════════════════════════════════════════

        internal void AttachToSpan(BridgeSpan span)
        {
            Span = span;
        }

        /// <summary>Leaves the span when the cell is lifted. Does not re-solve it: a crossing that
        /// loses a cell is still the crossing it was, just shorter.</summary>
        internal void DetachFromSpan()
        {
            var span = Span;
            Span = null;
            span?.Release(this);
        }

        /// <summary>Takes the deck out of service while a drawbridge is open. Disables the collider
        /// so a vehicle cannot drive into the channel, and the renderer so the player can see that
        /// there is no road there. The block itself is untouched: its saved position, its run
        /// membership and its wear all survive the swing, which is the whole reason the leaves are
        /// separate meshes rather than the cells being moved.</summary>
        public void SetDeckPassable(bool passable)
        {
            if (surfaceKind != RoadSurfaceKind.Bridge) return;
            if (_surfaceCollider != null && _surfaceCollider.enabled == passable) return;
            if (_surfaceCollider != null) _surfaceCollider.enabled = passable;
            if (_surfaceRenderer != null) _surfaceRenderer.enabled = passable;
        }

        /// <summary>Drives the span's swing. Rides on the cell's own staggered update rather than
        /// adding a second per-frame tick, and only for the one cell that owns the animation, so a
        /// ten-cell drawbridge animates once and not ten times.</summary>
        private void TickSpan()
        {
            var span = Span;
            if (span == null || surfaceKind != RoadSurfaceKind.Bridge) return;
            // Only the first cell of the span drives it. Every cell ticks on its own stagger, so
            // without this guard a ten-cell drawbridge would advance its swing ten times a frame.
            var cells = span.Cells;
            if (cells.Count == 0 || !ReferenceEquals(cells[0], this)) return;
            span.Tick(Time.deltaTime);
        }

        internal void AttachToRun(RoadRun run)
        {
            Run = run;
            ApplyWearVisuals();
        }

        // ════════════════════════════════════════════════════════════════
        //  TRAFFIC
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Bills this cell's run for distance travelled over it. Movement systems call this with
        /// the distance they actually covered this frame, so a parked rig wears nothing and a
        /// convoy wears the corridor properly.
        /// </summary>
        /// <param name="fromWheels">True for vehicles, false for feet. A cobble pathway is laid
        /// for walking: wheels neither help nor hurt it, so a rig rolling across one does not grind
        /// it down — and the pathway gives no traction back, which is why it is not a cheap road.</param>
        public void RegisterTraffic(float metres, float loadFactor, bool fromWheels = false)
        {
            if (Run == null || !IsSupported) return;
            if (fromWheels && surfaceKind == RoadSurfaceKind.Pathway) return;
            Run.AddTraffic(metres, loadFactor);
        }

        /// <summary>The wear load a footstep bills, taken from the block's own authored value so a
        /// content pass can retune it without touching movement code.</summary>
        public float FootLoad => footTrafficLoad;

        /// <summary>The wear load one wheel bills, scaled by the grid it belongs to so heavy
        /// traffic really does wear a road faster.</summary>
        public float WheelLoadFor(float gridMassKg)
        {
            // 12 t reads as 1x; a 60 t hauler reads as 5x. Clamped so a pathological mass cannot
            // wear a strip out in one crossing.
            float scale = Mathf.Clamp(gridMassKg / 12000f, 0.35f, 6f);
            return wheelTrafficLoad * scale;
        }

        // ════════════════════════════════════════════════════════════════
        //  GROUND SAMPLING & GRADING
        // ════════════════════════════════════════════════════════════════

        private float[] _heightCache;

        /// <summary>
        /// Samples the ground under this cell and returns the cached height field, or null when
        /// there is nothing to lay on. The field is a (SUBDIVISIONS+1)^2 grid over the cell plus
        /// the four corner samples, laid out row-major from -X/-Z to +X/+Z.
        /// </summary>
        private float[] SampleGroundHeights()
        {
            const int AXIS = AsphaltRoadMesh.SUBDIVISIONS + 1;
            if (_heightCache == null || _heightCache.Length != AXIS * AXIS)
                _heightCache = new float[AXIS * AXIS];

            // A bridge deck does not drape anything — that is the whole point of it. The cell's
            // origin IS the deck, so the height field is flat zero and the cell reports itself
            // supported without ever raycasting the riverbed forty metres below. Everything
            // downstream (mesh, collider, `SurfaceOffset`, the wear decals) reads this cache and so
            // needs no bridge-specific path at all.
            if (surfaceKind == RoadSurfaceKind.Bridge)
            {
                for (int i = 0; i < _heightCache.Length; i++) _heightCache[i] = 0f;
                return _heightCache;
            }

            Vector3 origin = transform.position;
            Vector3 up = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            float half = cellSize * 0.5f;

            bool anyHit = false;
            for (int iz = 0; iz < AXIS; iz++)
            {
                float lz = Mathf.Lerp(-half, half, iz / (float)AsphaltRoadMesh.SUBDIVISIONS);
                for (int ix = 0; ix < AXIS; ix++)
                {
                    float lx = Mathf.Lerp(-half, half, ix / (float)AsphaltRoadMesh.SUBDIVISIONS);
                    Vector3 world = origin + right * lx + forward * lz;
                    float height = ProbeGroundHeight(world, up, out bool hit);
                    _heightCache[iz * AXIS + ix] = height;
                    if (hit) anyHit = true;
                }
            }
            return anyHit ? _heightCache : null;
        }

        /// <summary>Height in LOCAL space (metres above the cell origin) of the ground under a
        /// world point. Falls back to flat when there is nothing to hit.</summary>
        private float LocalGroundHeight(float localX, float localZ)
        {
            const int AXIS = AsphaltRoadMesh.SUBDIVISIONS + 1;
            if (_heightCache == null || _heightCache.Length != AXIS * AXIS) return 0f;

            float half = cellSize * 0.5f;
            float fx = Mathf.Clamp01((localX + half) / cellSize) * AsphaltRoadMesh.SUBDIVISIONS;
            float fz = Mathf.Clamp01((localZ + half) / cellSize) * AsphaltRoadMesh.SUBDIVISIONS;

            // Bilinear read of the sampled grid: cheap, continuous, and never produces a facet
            // seam the collider and the renderer would disagree about.
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, AXIS - 1), z1 = Mathf.Min(z0 + 1, AXIS - 1);
            x0 = Mathf.Clamp(x0, 0, AXIS - 1);
            z0 = Mathf.Clamp(z0, 0, AXIS - 1);
            float tx = fx - Mathf.Floor(fx), tz = fz - Mathf.Floor(fz);

            float h00 = _heightCache[z0 * AXIS + x0];
            float h10 = _heightCache[z0 * AXIS + x1];
            float h01 = _heightCache[z1 * AXIS + x0];
            float h11 = _heightCache[z1 * AXIS + x1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        private static readonly RaycastHit[] _probeHits = new RaycastHit[8];

        /// <summary>Raycasts down from above a world point and returns the ground height relative
        /// to this cell's origin. Roads never sample other roads, so a strip cannot drape onto
        /// itself.</summary>
        private float ProbeGroundHeight(Vector3 worldPoint, Vector3 up, out bool hit)
        {
            hit = false;
            const float PROBE_UP = 2.5f;
            const float PROBE_DOWN = 4.0f;

            int count = Physics.RaycastNonAlloc(worldPoint + up * PROBE_UP, -up, _probeHits,
                                                PROBE_UP + PROBE_DOWN, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var collider = _probeHits[i].collider;
                if (collider == null) continue;
                if (collider.GetComponentInParent<AsphaltRoad>() != null) continue;
                float distanceAlongUp = Vector3.Dot(worldPoint - _probeHits[i].point, up);
                if (distanceAlongUp < best)
                {
                    best = distanceAlongUp;
                    hit = true;
                }
            }
            return hit ? -best : 0f;
        }

        /// <summary>Centre plus four corners of a cell, in cell-local metres. Static so the site
        /// evaluation, which runs every frame under the build ghost, allocates nothing.</summary>
        private static readonly Vector2[] GradeOffsets =
        {
            new Vector2(0f, 0f), new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f)
        };

        /// <summary>Grade band of a candidate cell. Returned by the placement gate and shown to the
        /// player as the reason a paving attempt was refused.</summary>
        public enum GradeBand { Smooth, Rough, TooRough, NoGround, Underwater, Buried }

        /// <summary>
        /// Evaluates a candidate cell WITHOUT placing anything: what the ground under it looks like
        /// and whether a road may go there at all. Used by the ghost preview, the paver tool and
        /// `BuildSystem` so all three refuse for the same reason with the same words.
        /// </summary>
        public static GradeBand EvaluateSite(Vector3 position, Quaternion rotation, float cellSize,
                                             float maxSmooth, float maxRough, out float roughness)
        {
            roughness = 0f;
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;

            float lowest = float.MaxValue, highest = float.MinValue;
            int hits = 0;
            for (int i = 0; i < GradeOffsets.Length; i++)
            {
                // GradeOffsets are fractions of a cell (-0.5..0.5), so they scale with cellSize
                // rather than being baked to one metre.
                Vector3 world = position
                              + right   * (GradeOffsets[i].x * cellSize)
                              + forward * (GradeOffsets[i].y * cellSize);
                if (!ProbeStaticGround(world, up, out float height)) continue;
                hits++;
                if (height < lowest) lowest = height;
                if (height > highest) highest = height;
            }

            if (hits == 0) return GradeBand.NoGround;
            roughness = highest - lowest;
            // The tolerances are a SLOPE (rise over run), not an absolute rise in metres. Comparing
            // the raw spread would make a 4 m cell four times harder to pave than a 1 m cell on the
            // very same gradient, so the wide road could only ever be laid on flat ground. Dividing
            // by the cell span makes the verdict size-independent: 0.22 means 22% however wide the
            // slab is, which is also what the 1 m road was already doing by coincidence.
            float slope = roughness / Mathf.Max(0.25f, cellSize);

            // Volume discipline: no paving under a body of liquid and no paving inside a wall.
            // The same fluid test the rest of the engine uses, so an oil pond refuses a road for
            // exactly the reason a lake does.
            var voxelWorld = ActiveWorld.Current;
            if (voxelWorld != null)
            {
                var surfaceVoxel = voxelWorld.GetVoxelWorld(voxelWorld.WorldToVoxel(position - up * 0.25f));
                if (surfaceVoxel.HasWater || VoxelEngine.WaterSim.FluidMaterialUtility.IsFluid(surfaceVoxel))
                    return GradeBand.Underwater;

                var headVoxel = voxelWorld.GetVoxelWorld(voxelWorld.WorldToVoxel(position + up * 0.85f));
                if (headVoxel.IsSolid) return GradeBand.Buried;
            }

            if (slope > maxRough)   return GradeBand.TooRough;
            if (slope > maxSmooth)  return GradeBand.Rough;
            return GradeBand.Smooth;
        }

        /// <summary>
        /// Public ground probe: the signed height of the static ground under
        /// <paramref name="worldPoint"/>, measured along <paramref name="up"/> (positive means the
        /// ground is ABOVE the point). Road cells are excluded, so a strip can be aimed at an
        /// existing road without measuring the road itself. Used by the paver to drop a new cell
        /// exactly onto the surface no matter what the aim ray happened to hit.
        /// The default reach is the paving question ("is there ground within a cell of here?").
        /// Bridge work passes a longer <paramref name="down"/>: a pier has to find the riverbed,
        /// not just the first thing below the deck, and "no bottom within four metres" is a very
        /// different statement about a channel than "no bottom within forty".
        /// </summary>
        public static bool ProbeGround(Vector3 worldPoint, Vector3 up, out float heightAbovePoint,
                                        float down = 4f)
            => ProbeStaticGround(worldPoint, up, out heightAbovePoint, down);

        /// <summary>Shared static ground probe used by the site evaluation. Roads and other road
        /// cells are ignored so a grade check on a strip never measures the strip.</summary>
        private static bool ProbeStaticGround(Vector3 worldPoint, Vector3 up, out float height,
                                               float down = 4f)
        {
            height = 0f;
            const float PROBE_UP = 2.5f;
            int count = Physics.RaycastNonAlloc(worldPoint + up * PROBE_UP, -up, _probeHits,
                                                PROBE_UP + down, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool hit = false;
            for (int i = 0; i < count; i++)
            {
                var collider = _probeHits[i].collider;
                if (collider == null) continue;
                if (collider.GetComponentInParent<AsphaltRoad>() != null) continue;
                float along = Vector3.Dot(worldPoint - _probeHits[i].point, up);
                if (along < best) { best = along; hit = true; }
            }
            if (!hit) return false;
            height = -best;
            return true;
        }

        /// <summary>One-line reason a site was refused, for the build feedback HUD.</summary>
        public static string DescribeSite(GradeBand band)
        {
            switch (band)
            {
                case GradeBand.Smooth:    return "Grade smooth";
                case GradeBand.Rough:     return "Grade rough - double material";
                case GradeBand.TooRough:  return "Grade too rough - level the strip first";
                case GradeBand.NoGround:  return "No ground to lay on";
                case GradeBand.Underwater: return "Cannot pave underwater - needs a culvert";
                case GradeBand.Buried:    return "Cannot pave inside a wall";
                default:                  return "Cannot pave here";
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  MESH
        // ════════════════════════════════════════════════════════════════

        /// <summary>Rebuilds the draped slab and the wear decals from the current topology and
        /// ground samples. Cheap enough to call on every topology change; never called per frame.</summary>
        public void RebuildMesh()
        {
            EnsureChildren();
            SampleGroundHeights();

            if (_surfaceMesh == null)
            {
                _surfaceMesh = new Mesh { name = "AsphaltRoad_Surface (runtime)" };
                _surfaceMesh.hideFlags = HideFlags.DontSave;
            }
            if (_wearMesh == null)
            {
                _wearMesh = new Mesh { name = "AsphaltRoad_Wear (runtime)" };
                _wearMesh.hideFlags = HideFlags.DontSave;
            }

            // Cached delegate: a rebuild is rare but a 400-cell strip placed in one drag is 400 of
            // them, and an allocation per cell here would show up as a hitch at the end of the drag.
            _sampler ??= LocalGroundHeight;
            AsphaltRoadMesh.BuildSurface(_surfaceMesh, cellSize, thickness, Edges, _sampler,
                                         shoulderWidth, shoulderRise,
                                         curveInRight, curveOutRight, curveInMitre, curveOutMitre,
                                         hasExplicitFootprint, quadSW, quadSE, quadNE, quadNW);
            BuildWearMesh(Run?.Wear01 ?? 0f);

            _surfaceFilter.sharedMesh = _surfaceMesh;
            _surfaceCollider.sharedMesh = null;      // clear before assigning: Unity ignores a re-assign otherwise
            _surfaceCollider.sharedMesh = _surfaceMesh;
            _wearFilter.sharedMesh = _wearMesh;

            ApplyWearVisuals();
        }

        /// <summary>Wear step that triggers a damage-mesh rebuild. Eighths give eight distinct
        /// crack densities across a run's life, which reads as progressive failure without
        /// rebuilding geometry often enough to matter.</summary>
        private const float WEAR_REBUILD_STEP = 0.125f;
        private float _wearMeshWear = -1f;

        /// <summary>Rebuilds the fracture/pothole mesh for a given wear value. Separate from
        /// `RebuildMesh` because the surface only changes when the ground or the neighbours do,
        /// while the damage changes as the run is used.</summary>
        private void BuildWearMesh(float wear)
        {
            if (_wearMesh == null || _wearFilter == null) return;
            _sampler ??= LocalGroundHeight;
            _wearMeshWear = wear;
            AsphaltRoadMesh.BuildWearPatches(_wearMesh, cellSize, thickness, _sampler,
                                             AsphaltRoadMesh.CellSeed(transform.position), wear);
            _wearFilter.sharedMesh = null;
            _wearFilter.sharedMesh = _wearMesh;
        }

        /// <summary>Pushes the run's wear into the surface tint and the damage geometry. Called by
        /// the run whenever the condition band changes, so this never runs per frame.</summary>
        public void ApplyWearVisuals()
        {
            if (_wearRenderer == null) return;
            float wear = Run?.Wear01 ?? 0f;
            bool supported = IsSupported;

            // Cracks and potholes are GEOMETRY, so the damage has to be rebuilt as the run wears
            // rather than scaled up — a scaled decal would stretch the cracks with it and read as
            // a texture sliding around. The rebuild is quantised to eighths: a highway under
            // constant traffic crosses a step every few minutes, not every frame.
            bool showWear = supported && wear > 0.02f;
            if (_wearRenderer.enabled != showWear) _wearRenderer.enabled = showWear;
            if (showWear && Mathf.Abs(wear - _wearMeshWear) >= WEAR_REBUILD_STEP) BuildWearMesh(wear);

            if (_surfaceRenderer == null) return;
            bool cobble = surfaceKind == RoadSurfaceKind.Pathway;
            Color worn   = cobble ? CobbleWornTint   : WornTint;
            Color broken = cobble ? CobbleBrokenTint : BrokenTint;
            Color tint = wear >= 1f ? broken : Color.Lerp(FreshTint, worn, Mathf.Clamp01(wear));
            if (!supported) tint = broken;
            _surfaceProperties.SetColor(BaseColorId, tint);
            _surfaceProperties.SetColor(ColorId, tint);
            _surfaceRenderer.SetPropertyBlock(_surfaceProperties);
        }

        // ════════════════════════════════════════════════════════════════
        //  MOVEMENT READ
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Signed distance from <paramref name="worldPoint"/> to this cell's actual paved surface,
        /// measured along <paramref name="up"/>: positive means the point is ABOVE the asphalt.
        /// Returns NaN when the point is outside the cell footprint.
        ///
        /// This is the honest test for "is this agent standing on me". Measuring against the cell
        /// ORIGIN instead would be wrong on a draped run, where the surface can sit half a metre
        /// above or below the origin across a single cell.
        /// </summary>
        public float SurfaceOffset(Vector3 worldPoint, Vector3 up)
        {
            Vector3 delta = worldPoint - transform.position;
            float lx = Vector3.Dot(delta, transform.right);
            float lz = Vector3.Dot(delta, transform.forward);
            float half = cellSize * 0.5f;

            if (hasExplicitFootprint)
            {
                // A corridor cell is a trapezoid, not a square, so the square test would report a
                // player standing in the wedge outside a curved lane as being on the road (and drop
                // one standing in the wedge inside it). Test the actual quad instead: inside when
                // the point keeps the same winding against all four edges.
                if (!InsideQuad(lx, lz)) return float.NaN;
            }
            else if (Mathf.Abs(lx) > half || Mathf.Abs(lz) > half) return float.NaN;

            float surface = LocalGroundHeight(lx, lz) + thickness;
            return Vector3.Dot(delta, up) - surface;
        }

        /// <summary>Local-space point-in-quad for the explicit footprint. Uses the same vertex
        /// order the mesh is wound in, so the winding test and the render agree.</summary>
        private bool InsideQuad(float lx, float lz)
        {
            bool positive = false, negative = false;
            Vector3 a = quadSW, b = quadSE, c = quadNE, d = quadNW;
            Cross2(a, b, lx, lz, ref positive, ref negative);
            Cross2(b, c, lx, lz, ref positive, ref negative);
            Cross2(c, d, lx, lz, ref positive, ref negative);
            Cross2(d, a, lx, lz, ref positive, ref negative);
            return !(positive && negative);
        }

        private static void Cross2(Vector3 p, Vector3 q, float lx, float lz,
                                   ref bool positive, ref bool negative)
        {
            float cross = (q.x - p.x) * (lz - p.z) - (q.z - p.z) * (lx - p.x);
            if (cross > 0f) positive = true;
            else if (cross < 0f) negative = true;
        }

        /// <summary>Walk-speed multiplier this cell offers right now. 1 when it offers nothing.
        /// Cobbles are uneven underfoot, so a pathway's bonus is gentler than a roadway's and a
        /// worn pathway turns actively hostile to boots sooner.</summary>
        public float WalkSpeedMultiplier
        {
            get
            {
                if (!IsSupported || Run == null) return 1f;
                if (surfaceKind != RoadSurfaceKind.Pathway) return Run.WalkSpeedMultiplier;
                float w = Run.Wear01;
                if (w >= 1f) return 0.85f;
                if (w <= 0.35f) return 1.18f;
                if (w <= 0.70f) return Mathf.Lerp(1.18f, 1.00f, Mathf.InverseLerp(0.35f, 0.70f, w));
                return Mathf.Lerp(1.00f, 0.85f, Mathf.InverseLerp(0.70f, 1f, w));
            }
        }

        /// <summary>Drive-traction multiplier for a wheel on this cell. Cobbles offer none: loose
        /// stone under a driven wheel is exactly what a paved yard is trying to avoid. A bridge deck
        /// is paved, so it hands out the same traction as the road it carries — the test is "not
        /// cobble", not "is asphalt".</summary>
        public float TractionMultiplier =>
            IsSupported && Run != null && surfaceKind != RoadSurfaceKind.Pathway ? Run.TractionMultiplier : 1f;

        /// <summary>Lateral-grip multiplier for a wheel on this cell. Cobbles offer none; a bridge
        /// deck grips like the road it continues.</summary>
        public float GripMultiplier =>
            IsSupported && Run != null && surfaceKind != RoadSurfaceKind.Pathway ? Run.GripMultiplier : 1f;

        // ════════════════════════════════════════════════════════════════
        //  SAVE HOOKS
        // ════════════════════════════════════════════════════════════════

        /// <summary>Wear written into a save. Per block rather than per run id, because run ids are
        /// session-scoped and a strip can merge or split between the save and the load.</summary>
        public float SavedWear => Run?.Wear01 ?? 0f;

        /// <summary>Restores a saved wear value onto this cell's run. Raises, never lowers.</summary>
        public void ApplySavedWear(float wear01)
        {
            Run ??= new RoadRun();
            Run.RaiseWearToSaved(wear01);
            ApplyWearVisuals();
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════

        private static string DescribeShape(RoadEdgeMask mask)
        {
            int count = CountBits(mask);
            switch (count)
            {
                case 0: return "PATCH";
                case 1: return "END";
                case 4: return "CROSSING";
                case 3: return "JUNCTION";
                default:
                    bool opposite = (mask & RoadEdgeMask.North) != 0 && (mask & RoadEdgeMask.South) != 0
                                 || (mask & RoadEdgeMask.East)  != 0 && (mask & RoadEdgeMask.West)  != 0;
                    return opposite ? "STRAIGHT" : "BEND";
            }
        }

        private static int CountBits(RoadEdgeMask mask)
        {
            int value = (int)mask;
            int count = 0;
            while (value != 0) { count += value & 1; value >>= 1; }
            return count;
        }

        private static bool _worldTearingDown;

        private void OnApplicationQuit()
        {
            _worldTearingDown = true;
            RoadSurfaceUtility.Clear();
        }
    }
}
