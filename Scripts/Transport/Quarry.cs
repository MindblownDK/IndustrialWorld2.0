// Assets/Scripts/VoxelEngine/Transport/Quarry.cs
//
// 17.19.0-dev - SPHERICAL-WORLD REWRITE.
// The quarry was built for a flat world: it mined the voxel column under its
// own WORLD Y, laid its tape/frame/ghost on world-XZ rectangles and stood its
// posts along Vector3.up. On a spherical planet "down" is radial, so away from
// the equator the old quarry mined sideways through the crust (or into the sky)
// and its visuals leaned off the surface.
//
// Everything now happens in the block's own SURFACE FRAME: the placed rotation
// (surface-aligned, yaw-stepped) defines local X = right, Y = up, Z = forward.
// The mining area is a rectangle in that tangent plane, each column probes the
// ground band around the block's own plane for its start height, and every
// mined cell is resolved local -> world -> voxel, so the excavation follows
// radial down on any world. Visuals (tape, frame, drill, ghost, placement
// preview) are children of the block/rig transform and inherit the surface
// alignment for free. Flat worlds behave exactly as before: there the surface
// frame IS the world frame.
//
// Also new: the quarry clears the biome scatter (trees, rocks, props) standing
// inside its mining area at startup and on every area resize - the area is now
// free of decorations before the first layer is cut. Tree stumps are recorded
// through their own OnDestroy harvest path, so they stay gone across save/load.
//
// 17.26.0-dev - THE MINING AREA IS IN FRONT OF THE MACHINE.
// The old rectangle was CENTRED laterally and merely offset forward, so with the
// default 16 m area six of its metres lay BEHIND the machine and the block sat
// inside its own excavation - "the mining square is around the block, which makes
// no sense". The area now starts at `forwardOffset` metres in front of the
// machine's centre plane and extends `EffSize` metres further forward, exactly
// like the machine this design follows: the box is cleared ground in front of the
// quarry, the frame is built around that box, and the drill walks it layer by
// layer from the top down. Nothing is mined under or behind the machine.
//
// Public API, serialized tuning fields and the save record (depth, cursor,
// phase, upgrade levels) are unchanged.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Power;

namespace VoxelEngine.Transport
{
    public enum QuarryPhase { Idle, TapeFrame, BuildingFrame, Mining, Complete }

    /// <summary>17.27.0-dev - why a quarry has no power, in three words on the machine
    /// itself: no cable reached it, a cable reached it but the network cannot carry the
    /// load, or it is being served.</summary>
    public enum QuarryPowerState { Unwired, Starved, Served }

    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Quarry : MonoBehaviour, IItemPortHost
    {
        [Header("Area")]
        public int defaultSize = 16;
        public float forwardOffset = 2f;

        [Header("Mining")]
        public float baseMineInterval = 0.5f;
        public int minePerTick = 1;
        [Range(0, 4)] public int quarryTier = 3;
        [Tooltip("Maximum number of voxel layers this quarry may excavate. The quarry completes when this depth is reached.")]
        [Range(8, 256)] public int maximumMiningDepth = 64;

        [Header("Power")]
        public float basePowerDraw = 500f;
        public float powerPerPerfUpgrade = 25f;
        public float powerSavePerEffUpgrade = 35f;

        [Header("Frame")]
        public float frameBuildInterval = 0.06f;
        public Color frameColor = new(0.18f, 0.19f, 0.22f);
        public Color frameAccentColor = new(0.92f, 0.52f, 0.08f, 0.85f);

        [Header("Tape")]
        public float tapePreviewDuration = 2.5f;
        public Color tapeColor1 = new(0.95f, 0.55f, 0.05f, 0.75f);
        public Color tapeColor2 = new(0.92f, 0.82f, 0.08f, 0.75f);

        [Header("Output")]
        public int outputSlots = 6;
        public const int UPGRADE_SLOTS = 3;
        public ItemContainer upgradeC;

        public QuarryPhase Phase { get; private set; } = QuarryPhase.Idle;
        public int CurrentDepth { get; private set; }
        public int MaxDepth { get; private set; }
        public bool IsMining => Phase == QuarryPhase.Mining;
        public float MineProgress01 => _mt / Mathf.Max(0.01f, EffInterval);

        /// <summary>How far the WHOLE job is: the survey, then the frame, then the pit.
        /// This is what the machine panel shows, because `MineProgress01` is one cell's bite
        /// - it fills and resets twice a second and reads as a bar that never gets anywhere
        /// (17.27.4-dev).</summary>
        public float JobProgress01 => Phase switch
        {
            QuarryPhase.TapeFrame     => tapePreviewDuration > 0f ? Mathf.Clamp01(_tt / tapePreviewDuration) : 0f,
            QuarryPhase.BuildingFrame => _fp.Count > 0 ? Mathf.Clamp01((float)_fi / _fp.Count) : 0f,
            QuarryPhase.Mining        => MaxDepth > 0 ? Mathf.Clamp01((float)CurrentDepth / MaxDepth) : 0f,
            QuarryPhase.Complete      => 1f,
            _                         => 0f
        };

        /// <summary>The bar's caption: the phase in words with the number that matters.</summary>
        public string JobProgressLabel => Phase switch
        {
            QuarryPhase.TapeFrame     => "SURVEYING THE AREA",
            QuarryPhase.BuildingFrame => $"BUILDING THE FRAME  {_fi} / {Mathf.Max(1, _fp.Count)}",
            QuarryPhase.Mining        => $"EXCAVATING  {CurrentDepth} / {MaxDepth}",
            QuarryPhase.Complete      => "PIT COMPLETE",
            _                         => "IDLE - WAITING FOR POWER"
        };
        public ItemContainer Output { get { EnsureOutput(); return _out; } }
        public int AreaX { get; private set; }
        public int AreaZ { get; private set; }
        public Vector3Int AreaMin { get; private set; }
        public Vector3Int AreaMax { get; private set; }
        public int CursorX => _cx;
        public int CursorZ => _cz;
        public bool IsOutputFull { get; private set; }

        public int InstalledRangeLevel { get; private set; }
        public int InstalledSpeedLevel { get; private set; }
        public int InstalledEfficiencyLevel { get; private set; }
        public const int MaxRangeLevel = 10;
        public const int MaxSpeedLevel = 10;
        public const int MaxEfficiencyLevel = 5;

        public int EffSize => defaultSize + InstalledRangeLevel;
        public float EffInterval => Mathf.Max(0.05f, baseMineInterval - InstalledSpeedLevel * 0.04f);
        public float EffPowerDraw => Mathf.Max(10f,
            basePowerDraw + (InstalledRangeLevel + InstalledSpeedLevel) * powerPerPerfUpgrade
                          - InstalledEfficiencyLevel * powerSavePerEffUpgrade);

        // ── 17.27.0-dev: the machine's own electrical readout ───────────────
        // The front multimeter and the machine panel both read these, so the two can
        // never disagree about what the drill is doing or what it is costing.

        /// <summary>Watts the drill is pulling right now: the rated load while a phase
        /// is actually running, and nothing at all while it is stopped, dead or done.
        /// A meter that reads 0 W on an unpowered machine is telling the truth; the
        /// machine's appetite is <see cref="MaxPowerDrawW"/>.</summary>
        public float LivePowerDrawW
        {
            get
            {
                if (!IsPowered) return 0f;
                if (Phase == QuarryPhase.Idle || Phase == QuarryPhase.Complete) return 0f;
                return EffPowerDraw;
            }
        }

        /// <summary>The highest load this build can ever ask for: the base draw plus
        /// every performance module fitted, before efficiency modules shave it back.
        /// This is the red end of the meter, and it does not move when the drill idles.</summary>
        public float MaxPowerDrawW => Mathf.Max(10f,
            basePowerDraw + (MaxRangeLevel + MaxSpeedLevel) * powerPerPerfUpgrade);

        /// <summary>Rated load of the drill as currently built (efficiency included).</summary>
        public float RatedPowerDrawW => EffPowerDraw;

        /// <summary>True when the drill is connected to at least one other power node.</summary>
        public bool IsWired => _pc != null && _pc.neighbours != null && _pc.neighbours.Count > 0;

        /// <summary>Power health, in the words the panel and the meter both use:
        /// UNWIRED (no cable found), STARVED (wired, network cannot serve the load) or
        /// SERVED (wired and running on real supply).</summary>
        public QuarryPowerState PowerState
        {
            get
            {
                if (_pc == null) return QuarryPowerState.Served;   // unpowered by design
                if (_pc.IsPowered) return QuarryPowerState.Served;
                return IsWired ? QuarryPowerState.Starved : QuarryPowerState.Unwired;
            }
        }

        public bool IsPowered => _pc == null || _pc.IsPowered;

        /// <summary>One-line status the way the drill itself would print it.</summary>
        public string StatusLabel => PowerState switch
        {
            QuarryPowerState.Unwired => "NO CABLE",
            QuarryPowerState.Starved => "NO SUPPLY",
            _ => Phase switch
            {
                QuarryPhase.TapeFrame     => "SURVEYING",
                QuarryPhase.BuildingFrame => "BUILDING",
                QuarryPhase.Mining        => IsOutputFull ? "OUTPUT FULL" : "DRILLING",
                QuarryPhase.Complete      => "COMPLETE",
                _                         => "IDLE"
            }
        };

        // Ground-probe band around the block's own plane (local metres). A column's
        // surface is the highest solid voxel inside this band, and the pit is cut down
        // from there - which is why the band has to be generous:
        //
        //   17.26.0-dev widened it below the plane, because the mining area lies IN FRONT
        //   of the machine and that ground is regularly downhill of it.
        //   17.27.3-dev widened it above the plane as well. It used to be 2 m up and 8 m
        //   down from the machine's OWN plane, which is fine on flat ground and wrong
        //   everywhere else: park a quarry at the foot of a slope and the terrain in the
        //   pit area is above the band, so the column reports no surface at all, the pit
        //   starts below the machine instead of at the ground the player can see, and the
        //   drill begins already buried. The band now spans the sky and the bedrock a
        //   quarry will ever meet, and the scan is two-pass - CoarseStep down to the first
        //   solid sample, then ProbeStep back UP to the top of it - so a wide search costs
        //   a third of the lookups of one fine sweep and still lands within 25 cm.
        //
        // A column that finds no ground in the band (bare sky, or a hole deeper than the
        // band) keeps the -0.5 fallback, which is the block plane and the historical
        // behaviour; already-empty cells are skipped by the miner until the depth counter
        // reaches the hole floor again.
        private const float ProbeUp = 24.0f;
        private const float ProbeDown = 32.0f;
        private const float ProbeStep = 0.25f;
        private const float ProbeCoarseStep = 1.0f;

        private float _mbY = -1.2f, _mtY = 1.2f;   // model bottom/top in LOCAL y
        private ItemContainer _out;
        private float _mt, _fbt, _tt;
        private int _cx, _cz, _ls;
        private VoxelEngine.Core.IVoxelWorld _w;
        private MaterialRegistry _mr;
        private PowerConsumer _pc;
        private bool _ok;
        private float[] _colTop = System.Array.Empty<float>();   // local ground height per column

        // Ghost persists from placement until power is applied
        private GameObject _ghost;
        private GameObject _ta, _dr, _bm;
        private List<GameObject> _fs = new();
        private int _fi;
        private List<FS> _fp = new();
        private float _tft;
        private bool _tfs;
        private struct FS { public Vector3 p, s; public bool a; }

        void Awake() { EnsureOutput(); EnsureUpgrades(); }

        void Start()
        {
            _w = VoxelEngine.Core.ActiveWorld.Current; _mr = _w?.MaterialRegistry;
            _pc = GetComponent<PowerConsumer>();
            if (_w == null) { enabled = false; return; }
            _mbY = -1.2f; _mtY = 1.2f;
            RecomputeArea(); _ls = EffSize; CalculateMaxDepth();
            RegisterUpgradeListener();
            StartCoroutine(CaptureModelBounds());
            _ok = true;
            ClearMiningAreaScatter();
            // Show ghost on placement (before power)
            CreateGhost();
        }

        // ── surface-frame area ──────────────────────────────────────────
        // Local coordinates: X = right, Y = up, Z = forward (the placed,
        // surface-aligned, yaw-stepped block rotation). The area is a rectangle
        // centred laterally and lying WHOLLY IN FRONT of the machine: its first
        // column starts at `forwardOffset` metres past the machine's centre plane
        // (17.26.0-dev), so the block itself is never inside its own excavation.

        private Vector3 CellLocal(int a, int b, int depth)
        {
            float x = (a + 0.5f) - AreaX * 0.5f;
            float z = forwardOffset + (b + 0.5f);
            return new Vector3(x, ColTop(a, b) - depth - 0.5f, z);
        }

        private Vector3Int CellVoxel(int a, int b, int depth)
            => _w.WorldToVoxel(transform.TransformPoint(CellLocal(a, b, depth)));

        void RecomputeArea()
        {
            int sz = Mathf.Max(1, EffSize);
            AreaX = sz; AreaZ = sz;
            _colTop = new float[sz * sz];

            // Probe each column's surface, top-down, over the whole band. The first solid
            // coarse sample is walked back UP in fine steps, so a column's top is the
            // highest solid voxel in it - the surface the player is looking at - and the
            // pit therefore starts at the top of the terrain inside the designated area,
            // not at whatever height the machine happens to stand at.
            for (int a = 0; a < sz; a++)
            for (int b = 0; b < sz; b++)
            {
                float x = (a + 0.5f) - sz * 0.5f;
                float z = forwardOffset + (b + 0.5f);
                float top = -0.5f;
                for (float y = ProbeUp; y >= -ProbeDown; y -= ProbeCoarseStep)
                {
                    if (!SolidAt(x, y, z)) continue;
                    top = y;
                    for (int g = 0; g < 8; g++)
                    {
                        float up = top + ProbeStep;
                        if (up > ProbeUp || !SolidAt(x, up, z)) break;
                        top = up;
                    }
                    break;
                }
                _colTop[a * sz + b] = top;
            }

            // Compatibility bounds in voxel space (debug/UI only): the four corners
            // of the front rectangle at their probed ground heights.
            bool any = false;
            Vector3Int lo = Vector3Int.zero, hi = Vector3Int.zero;
            for (int a = 0; a < sz; a += Mathf.Max(1, sz - 1))
            for (int b = 0; b < sz; b += Mathf.Max(1, sz - 1))
            {
                Vector3 world = transform.TransformPoint(new Vector3(
                    (a + 0.5f) - sz * 0.5f,
                    _colTop[a * sz + b],
                    forwardOffset + (b + 0.5f)));
                Vector3Int vox = _w.WorldToVoxel(world);
                if (!any) { lo = hi = vox; any = true; }
                else { lo = Vector3Int.Min(lo, vox); hi = Vector3Int.Max(hi, vox); }
            }
            AreaMin = lo; AreaMax = hi;
        }

        /// <summary>Is the voxel at this machine-local point solid? The raw material of the
        /// column scan above.</summary>
        bool SolidAt(float x, float y, float z)
        {
            Vector3 world = transform.TransformPoint(new Vector3(x, y, z));
            return _w.GetVoxelWorld(_w.WorldToVoxel(world)).density > VoxelConstants.ISO_LEVEL;
        }

        void CalculateMaxDepth() { MaxDepth = Mathf.Clamp(maximumMiningDepth, 8, 256); }

        /// <summary>17.19.0-dev: remove trees/rocks/props standing in the mining
        /// area so the frame, tape and drill never intersect decoration - and so
        /// the area mines cleanly. Trees record their harvest on the way out and
        /// stay felled across save/load. 17.26.0-dev: the box follows the area's
        /// new in-front footprint, so a machine no longer clears the ground it is
        /// standing on.</summary>
        void ClearMiningAreaScatter()
        {
            if (_w == null) return;
            int sz = Mathf.Max(1, EffSize);
            Vector3 center = transform.TransformPoint(
                new Vector3(0f, 2f, forwardOffset + sz * 0.5f));
            Vector3 halfExtents = new Vector3(sz * 0.5f + 0.5f, 6f, sz * 0.5f + 0.5f);
            VoxelEngine.Scattering.ScatterClear.ClearInBox(center, halfExtents, transform.rotation);
        }

        IEnumerator CaptureModelBounds()
        {
            yield return null;
            var col = GetComponentInChildren<BoxCollider>(true);
            if (col != null && TryLocalYRange(col.bounds, out float b0, out float t0)) { _mbY = b0; _mtY = t0; _boundsCaptured = true; yield break; }
            var r = GetComponentInChildren<MeshRenderer>(true);
            if (r != null && TryLocalYRange(r.bounds, out float b1, out float t1)) { _mbY = b1; _mtY = t1; _boundsCaptured = true; yield break; }
            var anyCol = GetComponentInChildren<Collider>(true);
            if (anyCol != null && TryLocalYRange(anyCol.bounds, out float b2, out float t2)) { _mbY = b2; _mtY = t2; }
            _boundsCaptured = true;   // measured or not, the outline may stop waiting for it
        }

        /// <summary>World bounds -> local-Y range of the eight corners. The frame
        /// visuals span model bottom to model top, which must stay true on tilted
        /// surfaces where a world AABB says nothing about local height.</summary>
        bool TryLocalYRange(Bounds worldBounds, out float bottom, out float top)
        {
            bottom = float.MaxValue; top = float.MinValue;
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = worldBounds.center + Vector3.Scale(worldBounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                float y = transform.InverseTransformPoint(corner).y;
                if (y < bottom) bottom = y;
                if (y > top) top = y;
                any = true;
            }
            if (!any || top - bottom < 0.05f) { bottom = -1.2f; top = 1.2f; return false; }
            return true;
        }

        void Update()
        {
            if (!_ok) return;
            // 17.27.0-dev: a finished drill does not hold a claim on the grid. While it
            // still has work - including waiting for power - it asks for its full rated
            // draw so the network can decide whether it can be served; once it is
            // COMPLETE there is nothing left to power, and releasing the demand lets a
            // finished machine stop loading the base (the front meter reads 0 W with it).
            if (_pc) _pc.wattsPerSecond = Phase == QuarryPhase.Complete ? 0f : EffPowerDraw;
            bool hp = _pc == null || _pc.IsPowered;

            // Destroy ghost when power comes on (real frame is about to build)
            if (hp && _ghost != null) { DestroyGhost(); }

            if (EffSize != _ls) { _ls = EffSize; RecomputeArea(); CalculateMaxDepth(); ClearMiningAreaScatter();
                if (Phase == QuarryPhase.Mining) { DA(); _cx=_cz=0; CurrentDepth=0; Phase=QuarryPhase.Idle; }
                else if (Phase != QuarryPhase.Idle && Phase != QuarryPhase.Complete) { DA(); Phase = QuarryPhase.Idle; }
                else CreateGhostIfUnpowered(); }

            // 17.27.6-dev: complete a restore's outline request - but only after the size check
            // above, because a machine whose area just changed has been reset to Idle and must
            // not build an outline for a phase it no longer has. This is where the request
            // actually lands on a machine that was spawned for the load, since - unlike
            // RestoreState - Update runs after Start.
            TryResumeOutline();

            SpinCutter(Time.deltaTime, hp);

            switch (Phase)
            {
                case QuarryPhase.Idle: if (hp) ETP(); break;
                case QuarryPhase.TapeFrame: if (!hp) return; _tt += Time.deltaTime; TkT(); if (_tt>=tapePreviewDuration){DTp();EFP();} break;
                case QuarryPhase.BuildingFrame: if (!hp) return; _fbt += Time.deltaTime; if (_fbt>=frameBuildInterval){_fbt=0f; if(_fi<_fp.Count)PS(_fp[_fi++]); else{Phase=QuarryPhase.Mining;CDr();}} break;
                case QuarryPhase.Mining:
                    if (!hp) return;
                    // 17.26.0-dev: a quarry restored mid-mining never ran CDr() (that
                    // only happens on the BUILD -> MINE transition), so it excavated
                    // with no drill head on screen. The head is rebuilt on demand.
                    if (_dr == null) CDr();
                    _mt += Time.deltaTime;
                    if (_mt>=EffInterval){_mt-=EffInterval; for(int i=0;i<minePerTick;i++)MO();}
                    TkD();
                    break;
                case QuarryPhase.Complete: if(_dr){Destroy(_dr);_dr=null;} if(_bm){Destroy(_bm);_bm=null;} break;
            }
        }

        // ═══ CUTTER WHEEL ═══════════════════════════════════════════
        // The machine's own cutter wheel - the one authored into the prefab under
        // "CutterWheelPivot" - turns while the drill works. It spins up with the work and
        // coasts down when the phase does, at drill speed while mining and a slow turn
        // while surveying and building the frame, so the machine reads as a machine from
        // across the pit instead of a parked hulk. The wheel is looked up on demand: a
        // quarry restored mid-mine, or built from an older prefab, simply has no pivot and
        // this does nothing.
        private Transform _cw;
        private bool _cwMissing;
        private float _cwSpeed;

        // 17.27.6-dev: the tape and the pit frame are runtime objects - nothing about them is
        // saved - so a machine restored mid-job has to put its own outline back. RestoreState
        // raises the request; the rebuild happens on the first frame where the machine is LIVE
        // and its measured height is known (the frame spans model bottom to model top).
        private bool _outlineResumePending;
        private bool _boundsCaptured;
        private const float CutterSpinMining = 165f;    // degrees/second while cutting
        private const float CutterSpinWorking = 26f;    // degrees/second while surveying/building
        private const float CutterSpinAccel = 240f;     // degrees/second per second

        void SpinCutter(float dt, bool powered)
        {
            if (_cw == null && !_cwMissing)
            {
                // Direct child first (how the wizard authors it), then anywhere in the body:
                // a prefab that hangs the wheel under a sub-frame must still turn.
                _cw = transform.Find("CutterWheelPivot") ?? FindDeep(transform, "CutterWheelPivot");
                if (_cw == null)
                {
                    _cwMissing = true;
                    // One clear line instead of a silently parked machine: a quarry built
                    // from a prefab older than the spinning wheel has no pivot to turn.
                    Debug.LogWarning("[Quarry] '" + name + "' has no CutterWheelPivot, so its cutter " +
                                     "wheel cannot turn. Run Tools > Voxel Engine > Voxel Engine Setup > " +
                                     "Factory, Industry & Power > 'Rebuild the Quarry machine...' to author " +
                                     "the current machine.", this);
                }
            }
            if (_cw == null) return;

            float target = !powered ? 0f
                : Phase == QuarryPhase.Mining ? CutterSpinMining
                : (Phase == QuarryPhase.TapeFrame || Phase == QuarryPhase.BuildingFrame) ? CutterSpinWorking
                : 0f;

            _cwSpeed = Mathf.MoveTowards(_cwSpeed, target, CutterSpinAccel * dt);
            if (_cwSpeed != 0f) _cw.Rotate(Vector3.right, _cwSpeed * dt, Space.Self);
        }

        /// <summary>PUT THE OUTLINE BACK (17.27.6-dev).
        ///
        /// The tape and the pit frame exist only in memory: they are spawned as the machine
        /// works and none of them is saved, so a quarry restored mid-job comes back as a working
        /// drill with an invisible mining area. This puts back exactly what the build phase put
        /// there - the same PF() piece list, placed by the same PS() - and leaves the machine in
        /// the phase it was saved in, with its depth and cursor untouched.
        ///
        /// It waits for two things, and that wait is the whole point of it existing: the machine
        /// must be LIVE (Start has run, `_w` and the power node are bound) and its measured height
        /// must be known, because the frame spans model bottom to model top. Rebuilding any
        /// earlier - as the previous attempt did, inside RestoreState, which runs before Start on
        /// a freshly spawned machine - built nothing at all.</summary>
        void TryResumeOutline()
        {
            if (!_outlineResumePending || !_ok || !_boundsCaptured) return;
            _outlineResumePending = false;

            if (Phase == QuarryPhase.TapeFrame)
            {
                if (_ta == null) CTp();
                return;
            }
            if (Phase != QuarryPhase.BuildingFrame && Phase != QuarryPhase.Mining) return;
            if (_fs.Count > 0) return;                       // a frame is already standing

            PF();
            foreach (var piece in _fp) PS(piece);
            // The build phase counts its own pieces out one at a time. A frame that is restored
            // whole must not be laid down a second time on top of itself when that counter
            // resumes, so the counter is moved to the end of the list.
            _fi = _fp.Count;
        }

        /// <summary>Depth-first search for a child by name, for prefabs whose hierarchy
        /// differs from the one the setup tool authors.</summary>
        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name) return child;
                var hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

        void CreateGhostIfUnpowered()
        {
            bool hp = _pc == null || _pc.IsPowered;
            if (!hp && _ghost == null && Phase == QuarryPhase.Idle) CreateGhost();
        }

        void DA() { DTp(); foreach(var fb in _fs)if(fb)Destroy(fb); _fs.Clear();_fp.Clear(); if(_dr){Destroy(_dr);_dr=null;} if(_bm){Destroy(_bm);_bm=null;} }
        void ETP() { Phase=QuarryPhase.TapeFrame; _tt=0f; CTp(); }
        void EFP() { Phase=QuarryPhase.BuildingFrame; PF(); _fi=0; _fbt=0f; }

        // ── local rectangle corners (tape, ghost, frame) ────────────────
        // 17.26.0-dev: FwdMin is the machine's own front plane, so the whole
        // rectangle - tape, frame, ghost, drill targets and the placement preview -
        // sits in front of the quarry and never straddles it.
        private float LatMin => -AreaX * 0.5f;
        private float LatMax => AreaX * 0.5f;
        private float FwdMin => forwardOffset;
        private float FwdMax => forwardOffset + AreaZ;
        private float GroundY()
        {
            if (_colTop == null || _colTop.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < _colTop.Length; i++) sum += _colTop[i];
            return sum / _colTop.Length;
        }

        // ═══ PLACEMENT PREVIEW (BuildSystem) ═══
        // The preview rig takes the ghost's surface-aligned pose, so the shown
        // box lies flat on the ground anywhere on a spherical world.
        static GameObject _pp;
        public static void ShowPlacementPreview(Vector3 wp, Quaternion rot, int sz, float fo)
        {
            HPP();
            _pp = new GameObject("QPP");
            _pp.transform.SetPositionAndRotation(wp, rot);
            // 17.26.0-dev: the preview shows what the machine will actually mine -
            // a square that starts at its front face and extends `sz` metres forward.
            float half = sz * 0.5f;
            float x0 = -half, x1 = half, z0 = fo, z1 = fo + sz;
            float yTop = 0.5f;          // surface
            float yBot = -1.2f;         // model bottom
            Color gc = new(1f, 0.8f, 0.2f, 0.55f);
            var v0=new Vector3(x0,yTop,z0);var v1=new Vector3(x1,yTop,z0);
            var v2=new Vector3(x1,yTop,z1);var v3=new Vector3(x0,yTop,z1);
            AL(v0,v1,gc);AL(v1,v2,gc);AL(v2,v3,gc);AL(v3,v0,gc);
            AL(v0,new Vector3(x0,yBot,z0),gc);AL(v1,new Vector3(x1,yBot,z0),gc);
            AL(v2,new Vector3(x1,yBot,z1),gc);AL(v3,new Vector3(x0,yBot,z1),gc);
            var b0=new Vector3(x0,yBot,z0);var b1=new Vector3(x1,yBot,z0);
            var b2=new Vector3(x1,yBot,z1);var b3=new Vector3(x0,yBot,z1);
            AL(b0,b1,gc);AL(b1,b2,gc);AL(b2,b3,gc);AL(b3,b0,gc);
        }
        public static void HidePlacementPreview()=>HPP();
        static void HPP(){if(_pp){Object.Destroy(_pp);_pp=null;}}
        static void AL(Vector3 a,Vector3 b,Color c)
        {
            var go=new GameObject("L");go.transform.SetParent(_pp.transform,false);
            var lr=go.AddComponent<LineRenderer>();lr.positionCount=2;lr.useWorldSpace=false;
            lr.SetPositions(new[]{a,b});lr.startWidth=lr.endWidth=0.06f;lr.startColor=lr.endColor=c;
            var sh=Shader.Find("Universal Render Pipeline/Particles/Unlit")??Shader.Find("Sprites/Default");
            lr.material=new Material(sh){color=c};lr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ═══ GHOST (persists on placed quarry until powered) ═══
        void CreateGhost()
        {
            if (_ghost) Destroy(_ghost);
            _ghost = new GameObject("QuarryGhost");
            _ghost.transform.SetParent(transform, false);
            float yTop=_mtY; // model top
            float yBot=_mbY; // model bottom
            float x0=LatMin,x1=LatMax,z0=FwdMin,z1=FwdMax;
            Color gc=new(1f,0.8f,0.2f,0.5f);
            var v0=new Vector3(x0,yTop,z0);var v1=new Vector3(x1,yTop,z0);
            var v2=new Vector3(x1,yTop,z1);var v3=new Vector3(x0,yTop,z1);
            GL(v0,v1,gc);GL(v1,v2,gc);GL(v2,v3,gc);GL(v3,v0,gc);
            GL(v0,new Vector3(x0,yBot,z0),gc);GL(v1,new Vector3(x1,yBot,z0),gc);
            GL(v2,new Vector3(x1,yBot,z1),gc);GL(v3,new Vector3(x0,yBot,z1),gc);
            var b0=new Vector3(x0,yBot,z0);var b1=new Vector3(x1,yBot,z0);
            var b2=new Vector3(x1,yBot,z1);var b3=new Vector3(x0,yBot,z1);
            GL(b0,b1,gc);GL(b1,b2,gc);GL(b2,b3,gc);GL(b3,b0,gc);
        }
        void GL(Vector3 a,Vector3 b,Color c)
        {
            var go=new GameObject("L");go.transform.SetParent(_ghost.transform,false);
            var lr=go.AddComponent<LineRenderer>();lr.positionCount=2;lr.useWorldSpace=false;
            lr.SetPositions(new[]{a,b});lr.startWidth=lr.endWidth=0.06f;lr.startColor=lr.endColor=c;
            var sh=Shader.Find("Universal Render Pipeline/Particles/Unlit")??Shader.Find("Sprites/Default");
            lr.material=new Material(sh){color=c};lr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        void DestroyGhost() { if (_ghost) { Destroy(_ghost); _ghost = null; } }

        // ═══ TAPE ════════════════════════════════════
        void CTp()
        {
            _ta=new GameObject("QT");_ta.transform.SetParent(transform,false);
            float y=GroundY()+0.1f;
            float x0=LatMin,x1=LatMax,z0=FwdMin,z1=FwdMax;
            var v0=new Vector3(x0,y,z0);var v1=new Vector3(x1,y,z0);
            var v2=new Vector3(x1,y,z1);var v3=new Vector3(x0,y,z1);
            TE(v0,v1);TE(v1,v2);TE(v2,v3);TE(v3,v0);
            float ph=2.5f;TP(v0,ph);TP(v1,ph);TP(v2,ph);TP(v3,ph);
        }
        void TE(Vector3 f,Vector3 t)
        {
            var d=(t-f);float l=d.magnitude;if(l<0.001f)return;d.Normalize();
            int sc=Mathf.CeilToInt(l/0.5f);float sl=l/sc;
            for(int i=0;i<sc;i++)
            {
                var p=f+d*(i*sl+sl*0.5f);
                var g=MK("T",p,_ta.transform);
                if(Mathf.Abs(d.x)>0.5f)g.transform.localScale=new Vector3(sl*0.95f,0.04f,0.08f);
                else g.transform.localScale=new Vector3(0.08f,0.04f,sl*0.95f);
                g.GetComponent<MeshRenderer>().material=ME((i%2)==0?tapeColor1:tapeColor2,0.6f);
            }
        }
        void TP(Vector3 bp,float h)
        {
            var g=MK("P",bp+new Vector3(0f,h*0.5f,0f),_ta.transform);
            g.transform.localScale=new Vector3(0.1f,h,0.1f);
            g.GetComponent<MeshRenderer>().material=ME(tapeColor1,0.8f);
            var o=MK("O",bp+new Vector3(0f,h,0f),_ta.transform);
            o.transform.localScale=Vector3.one*0.2f;
            o.GetComponent<MeshRenderer>().material=ME(new Color(0.95f,0.55f,0.05f,0.9f),0.9f);
        }
        static GameObject MK(string n,Vector3 localPos,Transform pt)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;
            g.transform.SetParent(pt,false);g.transform.localPosition=localPos;
            Object.Destroy(g.GetComponent<Collider>());return g;
        }
        void TkT(){if(!_ta)return;_tft+=Time.deltaTime;if(_tft>0.15f){_tft=0f;_tfs=!_tfs;float a=_tfs?1f:0.5f;foreach(var mr in _ta.GetComponentsInChildren<MeshRenderer>()){var c=mr.material.color;c.a=a*0.75f;mr.material.color=c;}}}
        void DTp(){if(_ta){Destroy(_ta);_ta=null;}}

        // ═══ 3D FRAME (model-top to model-bottom, surface-aligned) ═══
        void PF()
        {
            _fp.Clear();
            float yTop=_mtY;       // model top (= quarry block top)
            float yBot=_mbY;       // model bottom (= quarry block bottom)
            float x0=LatMin,x1=LatMax,z0=FwdMin,z1=FwdMax;
            var v00=new Vector3(x0,0,z0);var v10=new Vector3(x1,0,z0);
            var v11=new Vector3(x1,0,z1);var v01=new Vector3(x0,0,z1);
            float pH=yTop-yBot+0.3f,pCY=(yTop+yBot)*0.5f;
            Pi(v00,pCY,pH);Pi(v10,pCY,pH);Pi(v11,pCY,pH);Pi(v01,pCY,pH);
            Be(v00,v10,yTop);Be(v10,v11,yTop);Be(v11,v01,yTop);Be(v01,v00,yTop);
            Be(v00,v10,yBot);Be(v10,v11,yBot);Be(v11,v01,yBot);Be(v01,v00,yBot);
            Ac(v00,yTop);Ac(v10,yTop);Ac(v11,yTop);Ac(v01,yTop);
        }
        void Pi(Vector3 p,float cy,float h)=>_fp.Add(new FS{p=new Vector3(p.x,cy,p.z),s=new Vector3(0.16f,h,0.16f),a=false});
        void Be(Vector3 f,Vector3 t,float y){var m=(f+t)*0.5f;float l=Vector3.Distance(f,t);_fp.Add(new FS{p=new Vector3(m.x,y,m.z),s=(Mathf.Abs(f.x-t.x)>0.01f?new Vector3(l,0.12f,0.1f):new Vector3(0.1f,0.12f,l)),a=false});}
        void Ac(Vector3 p,float y)=>_fp.Add(new FS{p=new Vector3(p.x,y,p.z),s=new Vector3(0.22f,0.22f,0.22f),a=true});
        void PS(FS s)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=s.a?"A":"F";
            go.transform.SetParent(transform,false);
            go.transform.localPosition=s.p;go.transform.localScale=s.s;
            var r=go.GetComponent<MeshRenderer>();
            if(s.a)r.material=ME(frameAccentColor,0.7f);
            else{var sh=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");var m=new Material(sh){color=frameColor};m.SetColor("_BaseColor",frameColor);m.SetFloat("_Metallic",0.9f);m.SetFloat("_Smoothness",0.45f);r.material=m;}
            Destroy(go.GetComponent<BoxCollider>());_fs.Add(go);
        }

        // ═══ DRILL ═══════════════════════════════════
        void CDr()
        {
            _dr=GameObject.CreatePrimitive(PrimitiveType.Cylinder);_dr.name="H";
            _dr.transform.SetParent(transform,false);
            _dr.transform.localScale=new Vector3(0.5f,0.25f,0.5f);
            var c=_dr.GetComponent<CapsuleCollider>();if(c)Destroy(c);
            var sh=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            var m=new Material(sh);m.color=new Color(0.85f,0.55f,0.08f);m.SetColor("_BaseColor",m.color);m.SetFloat("_Metallic",0.8f);m.SetFloat("_Smoothness",0.5f);m.SetColor("_EmissionColor",new Color(0.85f,0.45f,0.05f)*0.4f);m.EnableKeyword("_EMISSION");
            _dr.GetComponent<MeshRenderer>().material=m;
            _bm=GameObject.CreatePrimitive(PrimitiveType.Cylinder);_bm.name="B";
            _bm.transform.SetParent(transform,false);
            _bm.transform.localScale=new Vector3(0.08f,0.5f,0.08f);
            var bc=_bm.GetComponent<CapsuleCollider>();if(bc)Destroy(bc);
            var bmMat=new Material(sh);bmMat.color=new Color(1f,0.55f,0.05f,0.6f);bmMat.SetColor("_BaseColor",bmMat.color);bmMat.SetColor("_EmissionColor",new Color(1f,0.5f,0f)*0.8f);bmMat.EnableKeyword("_EMISSION");
            _bm.GetComponent<MeshRenderer>().material=bmMat;
        }
        void TkD()
        {
            if(!_dr)return;
            Vector3 lp = CellLocal(_cx, _cz, CurrentDepth);
            _dr.transform.localPosition=lp;
            _dr.transform.Rotate(0f,220f*Time.deltaTime,0f);
            if(_bm){_bm.transform.localPosition=lp+Vector3.down*0.35f;_bm.transform.Rotate(0f,-180f*Time.deltaTime,0f);}
        }

        // ═══ MINING ══════════════════════════════════
        void MO()
        {
            if(CurrentDepth>=MaxDepth){Phase=QuarryPhase.Complete;if(_dr)Destroy(_dr);if(_bm)Destroy(_bm);_dr=_bm=null;return;}
            EnsureOutput();
            if(!HasOutputRoom()){IsOutputFull=true;return;}
            IsOutputFull=false;

            // 17.27.4-dev: THE CELL'S FOOTPRINT IS MINED, NOT ITS CENTRE VOXEL.
            //
            // The machine is surface-aligned, so on a sphere its own X/Z axes are not the
            // voxel grid's axes - they cut through the grid at whatever angle the ground
            // there happens to have. A single cell centre therefore rounds to a voxel that
            // its neighbour never touches, and the pit came out as strands of untouched
            // ground standing between the cuts. Every voxel the cell covers at this layer
            // is cut instead, from the real corners of the cell, so the floor is a floor.
            CellVoxelBox(_cx, _cz, CurrentDepth, out var lo, out var hi);
            bool room = true;
            for (int x = lo.x; x <= hi.x && room; x++)
            for (int y = lo.y; y <= hi.y && room; y++)
            for (int z = lo.z; z <= hi.z && room; z++)
                room = CutVoxel(new Vector3Int(x, y, z));
            if (!room) IsOutputFull = true;
            AD();
        }

        /// <summary>Is there a slot for one more drop? Checked BEFORE a voxel is cut, so a
        /// full output holds the machine up instead of voiding ore.</summary>
        bool HasOutputRoom()
        {
            EnsureOutput();
            for (int i = 0; i < _out.Size; i++) if (_out.GetSlot(i).IsEmpty) return true;
            return false;
        }

        /// <summary>Cut one voxel. Returns false when the drop could not be stored, which stops
        /// this tick's cutting (the voxel is left standing for the next one).</summary>
        bool CutVoxel(Vector3Int v)
        {
            var vox = _w.GetVoxelWorld(v);
            if(vox.material==(byte)MaterialId.LegacySolidFloor)vox.material=(byte)MaterialId.Stone;
            if(vox.density<=VoxelConstants.ISO_LEVEL)return true;               // already air
            var def=_mr?.Get(vox.material);
            if(def!=null&&def.isMineable&&quarryTier<def.miningTier)return true; // too hard for this tier
            if(def?.dropItem!=null&&def.dropAmount>0&&!HasOutputRoom())return false;
            _w.SetVoxelWorld(v,Voxel.Empty);
            if(def?.dropItem!=null&&def.dropAmount>0)OI(def.dropItem,def.dropAmount);
            return true;
        }

        /// <summary>The voxel box a cell occupies at one depth layer, taken from the cell's own
        /// eight corners: the layer a surface-aligned machine cuts is a slab that passes
        /// diagonally through the grid, and the corners are the only honest description of it.
        /// Clamped to a 3-voxel cube, because a one metre cell can never need more.</summary>
        void CellVoxelBox(int a, int b, int depth, out Vector3Int lo, out Vector3Int hi)
        {
            float x0 = a - AreaX * 0.5f, x1 = x0 + 1f;
            float z0 = forwardOffset + b,  z1 = z0 + 1f;
            float yTop = ColTop(a, b) - depth;
            float yBot = yTop - 1f;
            lo = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            hi = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var local = new Vector3((i & 1) == 0 ? x0 : x1,
                                        (i & 2) == 0 ? yBot : yTop,
                                        (i & 4) == 0 ? z0 : z1);
                var vox = _w.WorldToVoxel(transform.TransformPoint(local));
                lo = Vector3Int.Min(lo, vox);
                hi = Vector3Int.Max(hi, vox);
            }
            hi = Vector3Int.Min(hi, lo + new Vector3Int(2, 2, 2));
        }

        /// <summary>This column's probed surface height in local y, with the historical block
        /// plane as the fallback for a column that found no ground at all.</summary>
        float ColTop(int a, int b)
            => (_colTop != null && a * AreaZ + b < _colTop.Length) ? _colTop[a * AreaZ + b] : -0.5f;
        void AD(){_cx++;if(_cx>=AreaX){_cx=0;_cz++;if(_cz>=AreaZ){_cz=0;CurrentDepth++;}}}
        // Mined items go ONLY into the Output container. The shared
        // ItemPortRouting component then ejects them through configured OUTPUT
        // faces (same system every machine uses) — no bespoke pipe-push here.
        void OI(ItemDefinition it,int n){EnsureOutput();_out.Insert(new ItemStack(it,n));}
        void EnsureOutput(){if(_out==null)_out=new ItemContainer("Output",outputSlots);else _out.Resize(outputSlots);}
        public void EnsureOutputPublic()=>EnsureOutput();

        // ── Upgrades ────────────────────────────────────────────────────
        public void EnsureUpgrades() { if(upgradeC==null){upgradeC=new ItemContainer("Upgrades",UPGRADE_SLOTS);upgradeC.OnChanged+=RU;} else upgradeC.Resize(UPGRADE_SLOTS); }
        void RegisterUpgradeListener() { EnsureUpgrades(); upgradeC.OnChanged-=RU; upgradeC.OnChanged+=RU; RU(); }
        void RU() { InstalledRangeLevel=InstalledSpeedLevel=InstalledEfficiencyLevel=0; if(upgradeC==null)return; for(int i=0;i<upgradeC.Size;i++){var st=upgradeC.GetSlot(i); if(st.IsEmpty||!(st.item is QuarryUpgradeItem u))continue; int a=u.level*st.count; switch(u.upgradeKind){case QuarryUpgradeKind.Range:InstalledRangeLevel=Mathf.Min(MaxRangeLevel,a);break;case QuarryUpgradeKind.Speed:InstalledSpeedLevel=Mathf.Min(MaxSpeedLevel,a);break;case QuarryUpgradeKind.Efficiency:InstalledEfficiencyLevel=Mathf.Min(MaxEfficiencyLevel,a);break;}} }
        // 17.19.0-dev: Insert returns NULL when everything fit - the old
        // .IsEmpty-on-the-return crashed the successful install path.
        public bool TryInstallUpgrade(QuarryUpgradeItem item)
        {
            if(item==null)return false;
            EnsureUpgrades();
            var leftover = upgradeC.Insert(new ItemStack(item,1));
            return leftover == null || leftover.IsEmpty;
        }

        // ── IItemPortHost ───────────────────────────────────────────────────
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

        public PortConfig PortConfig
        {
            get
            {
                if (_portConfig == null)
                {
                    _portConfig = GetComponent<PortConfig>();
                    if (_portConfig == null) _portConfig = gameObject.AddComponent<PortConfig>();
                    _portConfig.EnsureAllFaces();
                }
                return _portConfig;
            }
        }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureOutput();
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Output", Output, canInput: false, canOutput: true);
            return _portContainers;
        }

        public void RestoreState(int d,int cx,int cz,int ph,int rl,int sl,int el)
        {
            CurrentDepth=d;_cx=cx;_cz=cz;Phase=(QuarryPhase)ph;
            InstalledRangeLevel=rl;InstalledSpeedLevel=sl;InstalledEfficiencyLevel=el;
            // A restored machine must resume VISIBLY: the tape and frame are gone and
            // the drill head is a runtime object, so the mining state rebuilds its
            // head instead of excavating invisibly (17.26.0-dev).
            if(Phase==QuarryPhase.Mining && _ok && _dr==null) CDr();
            if(Phase==QuarryPhase.Complete){if(_dr)Destroy(_dr);if(_bm)Destroy(_bm);}
            if(_ok){_ls=EffSize;RecomputeArea();CalculateMaxDepth();CreateGhostIfUnpowered();}
            // 17.27.6-dev: the tape and the pit frame are runtime objects, so a reload used to
            // leave a working quarry with no outline at all - the mining area simply vanished
            // from the world. Raising a REQUEST is what fixes it: this method is called on a
            // machine that was just spawned for the load, so it runs before Start(), and the
            // earlier version's rebuild - guarded by the machine-is-live flag - was skipped in
            // silence every time. TryResumeOutline() completes the request once the machine is
            // live and its own height has been measured. The immediate attempt below covers a
            // restore that happens later in a session, when everything is already known.
            _outlineResumePending = Phase == QuarryPhase.TapeFrame
                || Phase == QuarryPhase.BuildingFrame
                || Phase == QuarryPhase.Mining;
            TryResumeOutline();
        }
        void OnDestroy(){DestroyGhost();DTp();HPP();foreach(var fb in _fs)if(fb)Destroy(fb);_fs.Clear();if(_dr)Destroy(_dr);if(_bm)Destroy(_bm);}
        static Material ME(Color c,float es){var s=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");var m=new Material(s);m.color=c;m.SetColor("_BaseColor",c);m.SetColor("_EmissionColor",c*es);m.EnableKeyword("_EMISSION");return m;}
    }
}
