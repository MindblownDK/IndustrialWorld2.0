// Assets/Scripts/VoxelEngine/GridSystem/GridEntity.cs
//
// The root component of a ship/vehicle. Manages the 3D block grid, physics,
// power distribution, thrust, and player control.
//
// Architecture:
//   GridEntity (Rigidbody, root GO)
//   └── GridBlock children at integer grid positions
//
// Power is grid-wide (no cables needed on ships) — all generators/consumers
// share a single power pool. Gas (hydrogen) still needs gas pipes.
//
// Built on 0.14.1-dev audio/UI base.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;
using VoxelEngine.Maritime;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(0)] // runs AFTER MaritimePropulsionSystem (-20) so maritime power values are fresh
    public class GridEntity : MonoBehaviour
    {
        [Header("Grid")]
        public GridSize gridSize = GridSize.Large;

        [Header("Physics")]
        [Tooltip("Extra gravity scale for grid physics. Ships are large/heavy objects and need a slightly stronger fall to feel grounded at game scale.")]
        public float gravityScale = 1.35f;

        // ── Block storage ──────────────────────────────────────────
        private readonly Dictionary<Vector3Int, GridBlock> _blocks = new();
        public IReadOnlyDictionary<Vector3Int, GridBlock> Blocks => _blocks;
        public GridPrecisionAttachmentLayer PrecisionAttachments => GetComponent<GridPrecisionAttachmentLayer>();
        public int BlockCount => _blocks.Count + (PrecisionAttachments != null ? PrecisionAttachments.Count : 0);

        /// <summary>All blocks on the unified construct, regardless of authored block scale.</summary>
        public IEnumerable<GridBlock> AllBlocks
        {
            get
            {
                foreach (var block in _blocks.Values)
                    if (block != null) yield return block;
                var precision = PrecisionAttachments;
                if (precision == null) yield break;
                foreach (var block in precision.Blocks.Values)
                    if (block != null) yield return block;
            }
        }

        // ── Physics ────────────────────────────────────────────────
        private Rigidbody _rb;
        // Save restore keeps physics asleep for two fixed ticks so the restored
        // rotation cannot be overwritten by gravity, docking contact, or surface
        // alignment while blocks/colliders are still being reconstructed.
        private int _restorePoseTicks;
        private Vector3 _restorePosition;
        private Quaternion _restoreRotation;
        private Vector3 _restoreVelocity;
        private Vector3 _restoreAngularVelocity;
        // A short post-load clearance window keeps unanchored grids from settling a
        // few centimetres into terrain while physics contacts/colliders warm up.
        private int _restoreGroundClearanceTicks;
        private static readonly RaycastHit[] s_restoreGroundHits = new RaycastHit[24];
        private bool _touchingIce;
        private float _lastIceContactTime = -999f;
        private const float IceGridBrakeMultiplier = 0.18f;
        private const float IceContactGravityGrace = 2.0f;
        private const float IceRecoveryGravityMultiplier = 1.75f;
        public Rigidbody Body => _rb;

        // ── Power (grid-wide, no cables) ───────────────────────────
        public float PowerGenerated { get; private set; }
        public float PowerConsumed  { get; private set; }
        public float PowerBalance   => PowerGenerated - PowerConsumed;
        public bool  HasPower       => PowerBalance >= -0.1f;
        /// <summary>Fraction of non-battery grid demand supplied during the latest fixed tick.</summary>
        public float PowerAvailability01 { get; private set; } = 1f;
        /// <summary>Requested non-battery watts that could not be supplied this fixed tick.</summary>
        public float UnservedPowerWatts { get; private set; }
        private readonly List<GridBattery> _batteryScratch = new(8);
        private readonly List<GridElectricalPropeller> _electricalPropellerScratch = new(8);

        // ── Gas storage (shared across grid) ───────────────────────
        public float HydrogenStored { get; set; }
        public float HydrogenCapacity { get; private set; }
        public float OxygenStored   { get; set; }

        // ── Thrust ─────────────────────────────────────────────────
        public Vector3 ThrustInput { get; set; }
        public float   RotationYaw { get; set; }
        public float   RotationPitch { get; set; }
        public float   RotationRoll { get; set; }
        public bool    DampenersOn { get; set; } = true;
        /// <summary>True while an unpiloted, unlocked grid is autonomously cancelling drift.</summary>
        public bool AutonomousDampenersActive { get; private set; }
        /// <summary>True while an occupied cockpit is actively cancelling all grid velocity.</summary>
        public bool PilotDampenerHoldActive { get; private set; }
        /// <summary>Current local atmosphere/space state for HUDs and future flight systems.</summary>
        public AtmosphereSample CurrentAtmosphere => AtmosphereManager.Sample(transform.position);

        // ── Thrust feel ────────────────────────────────────────────
        // Thrust doesn't hit instantly; it spools up/down so heavy ships feel weighty.
        private Vector3 _smoothedThrustInput;
        private const float THRUST_SPOOL_RATE = 3.5f;

        // ── Camera feedback ────────────────────────────────────────
        private Vector3 _prevVelocity;

        /// <summary>Total max thrust (N) available along each of the 6 local directions —
        /// Fwd, Back, Right, Left, Up, Down — for the cockpit HUD readout.</summary>
        public (float fwd, float back, float right, float left, float up, float down) GetThrustByDirection()
        {
            float fwd=0,back=0,right=0,left=0,up=0,down=0;
            foreach (var block in AllBlocks)
            {
                if (!(block is GridThruster t)) continue;
                // Thruster pushes the ship along its local forward. Atmospheric engines
                // report their actual pressure-limited authority instead of full vacuum thrust.
                Vector3 d = transform.InverseTransformDirection(t.transform.forward);
                float available = t.maxThrustN * (t.thrusterType == ThrusterType.Atmospheric
                    ? t.AtmosphericEfficiency
                    : 1f);
                if (d.z >  0.5f) fwd   += available;
                if (d.z < -0.5f) back  += available;
                if (d.x >  0.5f) right += available;
                if (d.x < -0.5f) left  += available;
                if (d.y >  0.5f) up    += available;
                if (d.y < -0.5f) down  += available;
            }
            return (fwd,back,right,left,up,down);
        }

        /// <summary>Tool groups the cockpit can select. Every drill on the
        /// ship acts as ONE "Drill" group and every weapon as ONE "Weapon" group — so the toolbar
        /// shows a single entry per type and firing it activates ALL blocks of that type at once.</summary>
        public enum ToolGroup { None = 0, Drill = 1, Weapon = 2 }

        /// <summary>Index of the currently selected tool group. Cycled in the cockpit with the
        /// scroll wheel; only blocks belonging to the selected group activate on click.</summary>
        public int SelectedToolIndex { get; set; }

        /// <summary>LMB = collect mined resources; RMB = "void" mode (faster mining, ore is
        /// destroyed instead of stored). Set by the cockpit each frame while the Drill group fires.</summary>
        public bool DrillVoidMode { get; set; }

        /// <summary>The distinct tool GROUPS present on this grid, in a stable order
        /// (Drill before Weapon). Drives the cockpit toolbar + scroll cycling.</summary>
        public System.Collections.Generic.List<ToolGroup> GetToolGroups()
        {
            bool hasDrill = false, hasWeapon = false;
            foreach (var block in AllBlocks)
            {
                if (block is GridDrill)  hasDrill  = true;
                else if (block is GridWeapon) hasWeapon = true;
            }
            var list = new System.Collections.Generic.List<ToolGroup>();
            if (hasDrill)  list.Add(ToolGroup.Drill);
            if (hasWeapon) list.Add(ToolGroup.Weapon);
            return list;
        }

        /// <summary>The currently-selected tool group (None if the ship has no tools).</summary>
        public ToolGroup SelectedGroup
        {
            get
            {
                var groups = GetToolGroups();
                if (groups.Count == 0) return ToolGroup.None;
                int idx = ((SelectedToolIndex % groups.Count) + groups.Count) % groups.Count;
                return groups[idx];
            }
        }

        /// <summary>Number of distinct tool groups — kept for the cockpit scroll cycle.</summary>
        public int ToolGroupCount => GetToolGroups().Count;

        /// <summary>Is the given drill/weapon block part of the player's currently-selected group?</summary>
        public bool IsSelectedTool(GridBlock b)
        {
            var g = SelectedGroup;
            if (g == ToolGroup.Drill)  return b is GridDrill;
            if (g == ToolGroup.Weapon) return b is GridWeapon;
            return false;
        }

        // Backwards-compat shim for older callers (HUD) that asked for the raw tool blocks.
        public System.Collections.Generic.List<GridBlock> GetFireTools()
        {
            var list = new System.Collections.Generic.List<GridBlock>();
            foreach (var block in AllBlocks)
                if (block is GridDrill || block is GridWeapon) list.Add(block);
            return list;
        }

        /// <summary>
        /// Re-express this grid's velocity when the scene reference frame changes
        /// (leaving/entering a planet's gravity well). Cosmic velocity is conserved;
        /// this is the real-space frame switch the SpaceOrigin applies to every body.
        /// </summary>
        public void AddFrameVelocityDelta(Vector3 deltaMps)
        {
            if (_rb != null)
            {
                _rb.linearVelocity += deltaMps;
                if (_restorePoseTicks > 0) _restoreVelocity += deltaMps;
            }
        }

        /// <summary>Set by the piloting cockpit each frame to drive thrusters + gyros.</summary>
        public void SetFlightInput(Vector3 thrust, float yaw, float pitch, float roll)
        {
            ThrustInput   = thrust;
            RotationYaw   = yaw;
            RotationPitch = pitch;
            RotationRoll  = roll;
        }

        // ── Control Seats ──────────────────────────────────────────
        public GridCockpit ActiveCockpit { get; set; }
        public Transform ActiveControlFrame { get; private set; }
        public Player.PlayerController ActiveControlPilot { get; private set; }
        public bool IsControlled => (ActiveCockpit != null && ActiveCockpit.Pilot != null)
                                 || (ActiveControlFrame != null && ActiveControlPilot != null);

        public void BeginExternalControl(Transform controlFrame, Player.PlayerController pilot)
        {
            ActiveControlFrame = controlFrame != null ? controlFrame : transform;
            ActiveControlPilot = pilot;
        }

        public void EndExternalControl(Transform controlFrame)
        {
            if (controlFrame != null && ActiveControlFrame != controlFrame) return;
            ActiveControlFrame = null;
            ActiveControlPilot = null;
            SetFlightInput(Vector3.zero, 0f, 0f, 0f);
            DrillVoidMode = false;
        }

        private Transform CurrentControlFrame => ActiveControlFrame != null
            ? ActiveControlFrame
            : (ActiveCockpit != null ? ActiveCockpit.transform : transform);

        // ── Lifecycle ──────────────────────────────────────────────
        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            // Gravity is applied manually through ApplyGravity() so dampeners can cancel
            // the exact same acceleration and hold a ship perfectly still in hover.
            // Leaving Unity's built-in gravity enabled doubled gravity and made hovering drift down.
            _rb.useGravity = false;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 1.5f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Auto-attach the maritime propulsion system so EVERY grid gets buoyancy
            // + water interaction for free (harmless for ships in space — zero submergence = zero force).
            if (GetComponent<VoxelEngine.Maritime.MaritimePropulsionSystem>() == null)
                gameObject.AddComponent<VoxelEngine.Maritime.MaritimePropulsionSystem>();

            _prevVelocity = _rb.linearVelocity;
        }

        private void FixedUpdate()
        {
            if (_restorePoseTicks > 0 && _rb != null)
            {
                _rb.isKinematic = true;
                _rb.position = _restorePosition;
                _rb.rotation = _restoreRotation;
                transform.SetPositionAndRotation(_restorePosition, _restoreRotation);
                if (_restoreGroundClearanceTicks > 0)
                {
                    ResolvePersistentGroundClearance();
                    _restoreGroundClearanceTicks--;
                }
                _restorePoseTicks--;
                if (_restorePoseTicks == 0)
                {
                    _rb.isKinematic = false;
                    _rb.linearVelocity = _restoreVelocity;
                    _rb.angularVelocity = _restoreAngularVelocity;
                }
                return;
            }

            // Belt-and-braces: generated/loaded grids should never use Unity's built-in
            // gravity because this class applies planet gravity manually.
            if (_rb != null) _rb.useGravity = false;

            UpdatePower();
            _touchingIce = DetectIceContact();
            if (_touchingIce) _lastIceContactTime = Time.time;
            UpdateThrust();
            ApplyAtmosphericDrag();
            ApplyWeatherWind();
            UpdateDampeners();
            UpdateWheels();
            ApplyGravity();
            StabilizeGroundAlignment();
            if (_restoreGroundClearanceTicks > 0)
            {
                ResolvePersistentGroundClearance();
                _restoreGroundClearanceTicks--;
            }

            // Camera screenshake/FOV warp from the previous physics step's net acceleration.
            // Only fire when a player is actually controlling this grid so walking around or
            // watching another ship doesn't shake the camera. Subtract gravity so free-fall
            // doesn't constantly shake the camera — only thrust impulses and impacts create the
            // "power" feel.
            if (_rb != null && IsControlled)
            {
                Vector3 acceleration = (_rb.linearVelocity - _prevVelocity) / Time.fixedDeltaTime
                                     - CurrentGravityAcceleration();
                VoxelEngine.Player.CameraFeedback.Impulse(acceleration);
                _prevVelocity = _rb.linearVelocity;
            }
            else if (_rb != null)
            {
                _prevVelocity = _rb.linearVelocity;
            }
        }

        private void Update()
        {
            RefreshContentMass();
        }

        private Vector3 CurrentGravityAcceleration()
        {
            // Use the radial gravity system when a celestial body is active.
            // GravityProvider.GetGravity() returns the proper toward-core vector
            // with inverse-square falloff for spherical planets.
            if (GravityProvider.IsRadial)
            {
                return GravityProvider.GetGravity(transform.position) * Mathf.Max(0f, gravityScale);
            }
            // Flat-world fallback: classic downward gravity with atmosphere falloff.
            return Physics.gravity * AtmosphereManager.GetGravityMultiplier(transform.position) * Mathf.Max(0f, gravityScale);
        }

        /// <summary>
        /// Applies a light, mass-correct aerodynamic drag force only while atmospheric gas exists.
        /// This makes high-altitude coast behaviour naturally transition from air resistance to
        /// inertial vacuum drift without changing the deliberate dampener model.
        /// </summary>
        private void ApplyAtmosphericDrag()
        {
            if (_rb == null || _rb.isKinematic) return;

            AtmosphereSample atmosphere = CurrentAtmosphere;
            if (!atmosphere.HasAtmosphere || atmosphere.AirDensity <= 0.0001f) return;

            // Drag resists the grid's OWN motion through the air. The wind's push is applied
            // separately in ApplyWeatherWind (at a sail centre above the centre of mass, so a
            // gale can heel a hull); folding wind in here as well would double-count it.
            Vector3 relativeVelocity = _rb.linearVelocity;
            float speedSq = relativeVelocity.sqrMagnitude;
            if (speedSq <= 0.0025f) return;

            // A compact block-count area estimate gives every hull a stable drag profile without
            // expensive per-collider projections. ForceMode.Force correctly makes mass matter.
            float cellSize = gridSize.CellSize();
            float baseArea = Mathf.Max(1f, cellSize * cellSize * 0.55f);
            float frontalArea = baseArea * Mathf.Max(1f, BlockCount * 0.32f);
            const float DragCoefficient = 0.85f;
            float dragForce = 0.5f * atmosphere.AirDensity * speedSq * DragCoefficient * frontalArea;
            _rb.AddForce(-relativeVelocity.normalized * dragForce, ForceMode.Force);
        }

        /// <summary>
        /// Weather wind pushing on the hull. A storm shoves anything that is not tied down —
        /// but the force is a real aerodynamic force, so <see cref="ForceMode.Force"/> divides
        /// it by the grid's mass: a light scout skitters across the pad while a loaded freighter
        /// barely leans. Locked landing gear or a docked port anchors the grid completely.
        ///
        /// The force is applied at a sail centre ABOVE the centre of mass, so wind also heels
        /// and slowly weathervanes a hull instead of sliding it like a brick, and a floating
        /// grid additionally rides the wind-driven surface current.
        /// </summary>
        private void ApplyWeatherWind()
        {
            if (_rb == null || _rb.isKinematic) return;
            if (HasStationaryLock()) return;                      // tied down: the storm cannot move it

            var wind = WindField.Instance;
            if (wind == null) return;

            AtmosphereSample atmosphere = CurrentAtmosphere;
            if (!atmosphere.HasAtmosphere || atmosphere.AirDensity <= 0.0001f) return;

            // Wind blows ACROSS the surface: strip the radial component so a gust never
            // lifts or slams a grid straight down on a spherical world.
            Vector3 up = GravityProvider.GetUp(transform.position);
            Vector3 windVelocity = Vector3.ProjectOnPlane(wind.Current, up) * atmosphere.Density01;
            float windSpeed = windVelocity.magnitude;
            if (windSpeed < 0.25f) return;

            Vector3 windDir = windVelocity / windSpeed;
            float cellSize = gridSize.CellSize();

            // Sail area from the block budget — the same stable estimate the drag model uses,
            // so a big ship catches proportionally more wind than a two-block hopper.
            float sailArea = Mathf.Max(1f, cellSize * cellSize * 0.32f) * Mathf.Max(1f, BlockCount * 0.26f);
            const float SailCoefficient = 0.9f;
            float force = 0.5f * atmosphere.AirDensity * windSpeed * windSpeed * SailCoefficient * sailArea;

            // Hard ceiling at a fraction of the grid's weight: weather can rock, drag and drift
            // a ship, but a gust must never fling one into orbit.
            force = Mathf.Min(force, _rb.mass * 4.5f);

            Vector3 sailCentre = _rb.worldCenterOfMass + up * (cellSize * 0.55f);
            _rb.AddForceAtPosition(windDir * force, sailCentre, ForceMode.Force);

            // Gust buffeting: a light, bounded torque so a parked ship visibly shivers and
            // slowly weathervanes in a storm instead of standing perfectly still.
            float gust = Mathf.Clamp01((windSpeed - 4f) / 12f);
            if (gust > 0.001f)
            {
                Vector3 buffet = Vector3.Cross(up, windDir) * (force * cellSize * 0.05f * gust);
                _rb.AddTorque(buffet, ForceMode.Force);
            }

            // Floating grids also ride the wind-driven surface current — this is what makes a
            // moored-but-unlocked boat drift downwind on a stormy sea.
            float submergence = VoxelEngine.Maritime.WaterProbeSystem.GetSubmergence(
                _rb.worldCenterOfMass - up * (cellSize * 0.5f), cellSize * 0.6f);
            if (submergence > 0.05f)
            {
                float current = Mathf.Min(force * 0.6f * submergence, _rb.mass * 2.5f);
                _rb.AddForce(windDir * current, ForceMode.Force);
            }
        }

        private bool HasManualThrustInput()
        {
            return IsControlled && ThrustInput.sqrMagnitude > 0.01f;
        }

        private bool DetectIceContact()
        {
            Vector3 up = GravityProvider.GetUp(transform.position);
            int sampled = 0;
            foreach (var block in AllBlocks)
            {
                if (block == null) continue;
                sampled++;
                if (IceFrictionUtility.IsIceBelow(block.transform.position + up * 0.15f, up,
                        Mathf.Max(block.EffectiveCellSize * 0.75f, 0.75f)))
                    return true;
                if (sampled >= 16) break; // deterministic cheap sample cap for large ships
            }
            return IceFrictionUtility.IsIceBelow(transform.position + up * 0.15f, up, 1.25f);
        }

        private bool IsRecoveringFromIceContact()
        {
            return Time.time - _lastIceContactTime <= IceContactGravityGrace;
        }

        /// <summary>
        /// An occupied cockpit's dampeners are an explicit station-keeping command:
        /// no translation input means cancel the entire velocity vector, even on a
        /// planet. This is intentionally separate from unpiloted/hover authority.
        /// </summary>
        private bool ShouldPilotDampenerHold()
        {
            return DampenersOn && IsControlled && !HasManualThrustInput();
        }

        private bool ShouldDampenerHoldHover()
        {
            // A grid that has just skidded/tilted off ice must keep falling back
            // toward the planet. Hover-hold dampeners cancelling gravity here made
            // tilted grids drift upward and hang in the air.
            if (IsRecoveringFromIceContact()) return false;
            return DampenersOn && !HasManualThrustInput() && HasHoverAuthority();
        }

        private bool HasHoverAuthority()
        {
            Vector3 gravity = CurrentGravityAcceleration();
            if (gravity.sqrMagnitude < 0.0001f || _rb == null) return false;

            Vector3 antiGravity = -gravity.normalized;
            float availableLift = 0f;
            foreach (var block in AllBlocks)
            {
                if (block is not GridThruster thruster || !thruster.Enabled || !thruster.IsOperational) continue;
                float alignment = Vector3.Dot(thruster.PushDirection.normalized, antiGravity);
                if (alignment <= 0.35f) continue;

                float environmentalAuthority = thruster.thrusterType == ThrusterType.Atmospheric
                    ? thruster.AtmosphericEfficiency
                    : 1f;
                availableLift += thruster.maxThrustN * environmentalAuthority * alignment;
            }

            // Do not let a barely-operating atmospheric engine claim magical hover at
            // high altitude. A small reserve prevents an exact-force threshold from jittering.
            float requiredLift = _rb.mass * gravity.magnitude * 1.03f;
            return availableLift >= requiredLift;
        }

        /// <summary>True if a landing gear or docking port already owns a hard stationary lock.</summary>
        private bool HasStationaryLock()
        {
            foreach (var block in AllBlocks)
            {
                if (block is GridLandingGear gear && gear.IsLocked) return true;
                if (block is GridDockingPort dock && dock.IsDocked) return true;
            }
            return false;
        }

        /// <summary>
        /// Checks stored electricity/hydrogen or live generation for autonomous
        /// dampeners. An unpowered, fuel-less abandoned ship intentionally drifts.
        /// </summary>
        public bool HasAutonomousDampenerEnergy()
        {
            if (PowerGenerated > 0.01f) return true;
            foreach (var block in AllBlocks)
            {
                switch (block)
                {
                    case GridBattery battery when battery.storedWh > 0.1f:
                        return true;
                    case GridGasTank tank when tank.gasType == Gas.GasType.Hydrogen && tank.stored > 0.1f:
                        return true;
                    case GridHydrogenEngine hydrogenEngine when hydrogenEngine.internalHydrogen > 0.1f:
                        return true;
                    case GridH2O2Generator h2o2 when h2o2.h2Stored > 0.1f:
                        return true;
                }
            }
            return false;
        }

        /// <summary>Stops a restored ship from retaining stale saved drift when its dampeners have energy.</summary>
        public void StabilizeRestoredVelocityIfPossible()
        {
            if (_rb == null || !DampenersOn || HasStationaryLock() || !HasAutonomousDampenerEnergy()) return;
            _restoreVelocity = Vector3.zero;
            _restoreAngularVelocity = Vector3.zero;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        private void ApplyGravity()
        {
            if (_rb == null) return;

            // A seated pilot's dampeners explicitly hold station: no translation input
            // means no gravity-axis drift on planets and no residual drift in space.
            if (ShouldPilotDampenerHold() || ShouldDampenerHoldHover()) return;

            Vector3 gravity = CurrentGravityAcceleration();
            if (IsRecoveringFromIceContact() && !IsControlled)
                gravity *= IceRecoveryGravityMultiplier;
            _rb.AddForce(gravity, ForceMode.Acceleration);
        }

        // ── Block Management ───────────────────────────────────────
        public bool CanPlace(Vector3Int gridPos) => !_blocks.ContainsKey(gridPos);

        /// <summary>True if any of the 6 face-neighbours of <paramref name="gridPos"/> is occupied
        /// (so a new block there actually connects to the existing structure).</summary>
        public bool HasNeighbor(Vector3Int gridPos)
        {
            if (_blocks.Count == 0) return true; // first block always "connects"
            return _blocks.ContainsKey(gridPos + Vector3Int.right)
                || _blocks.ContainsKey(gridPos + Vector3Int.left)
                || _blocks.ContainsKey(gridPos + Vector3Int.up)
                || _blocks.ContainsKey(gridPos + Vector3Int.down)
                || _blocks.ContainsKey(gridPos + new Vector3Int(0, 0, 1))
                || _blocks.ContainsKey(gridPos + new Vector3Int(0, 0, -1));
        }

        public void AddBlock(Vector3Int gridPos, GridBlock block)
        {
            if (_blocks.ContainsKey(gridPos)) return;
            _blocks[gridPos] = block;
            block.GridPos = gridPos;
            block.Grid = this;

            block.transform.SetParent(transform, true);
            float cs = gridSize.CellSize();
            block.transform.localPosition = new Vector3(gridPos.x, gridPos.y, gridPos.z) * cs;
            // Removed rotation reset - handled by the caller (GridBuilder)
            // block.transform.localRotation = Quaternion.identity;

            if (block.GetComponentInChildren<Collider>(true) == null)
            {
                var box = block.gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one * cs;
            }

            RecalculateMass();
            block.OnPlaced();

            // Notify the maritime propulsion graph that the ship changed
            // (it lazily rebuilds next FixedUpdate — zero per-block work).
            NotifyMaritimeDirty();
            // A newly placed parallel shaft may sit inside an existing mechanical
            // belt run and therefore become an additional live take-off point.
            GetComponent<VoxelEngine.Maritime.MechanicalBeltNetwork>()?.NotifyGridTopologyChanged();
            // Tell pipe visuals the topology changed so they rebuild their arms
            // (event-driven — no continuous polling).
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged(block.transform.position);
        }

        public void RemoveBlock(Vector3Int gridPos)
        {
            if (!_blocks.TryGetValue(gridPos, out var block)) return;
            Vector3 formerPosition = block != null ? block.transform.position : GridToWorld(gridPos);
            _blocks.Remove(gridPos);
            block.OnRemoved();
            Destroy(block.gameObject);
            RecalculateMass();

            NotifyMaritimeDirty();
            // Remove belt links whose pulley was just dismantled and refresh any
            // remaining belt take-offs before the next drivetrain graph rebuild.
            GetComponent<VoxelEngine.Maritime.MechanicalBeltNetwork>()?.NotifyGridTopologyChanged();
            VoxelEngine.Networks.PipeVisualBuilder.NotifyTopologyChanged(formerPosition);

            if (_blocks.Count == 0 && (PrecisionAttachments == null || PrecisionAttachments.Count == 0))
                Destroy(gameObject);
        }

        public GridBlock GetBlock(Vector3Int gridPos)
        {
            _blocks.TryGetValue(gridPos, out var b);
            return b;
        }

        // ── Maritime propulsion (buoyancy + mechanical network) ───────
        // Lazily cached; null if the ship has no MaritimePropulsionSystem.
        private MaritimePropulsionSystem _maritime;
        /// <summary>The maritime simulation for this ship (null if none attached).</summary>
        public MaritimePropulsionSystem Maritime
        {
            get
            {
                if (_maritime == null) _maritime = GetComponent<MaritimePropulsionSystem>();
                return _maritime;
            }
        }

        /// <summary>Tell the maritime graph the block layout changed (cheap if absent).</summary>
        private void NotifyMaritimeDirty()
        {
            if (_maritime == null) _maritime = GetComponent<MaritimePropulsionSystem>();
            _maritime?.MarkDirty();
        }

        public Vector3Int WorldToGrid(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            float cs = gridSize.CellSize();
            return new Vector3Int(
                Mathf.RoundToInt(local.x / cs),
                Mathf.RoundToInt(local.y / cs),
                Mathf.RoundToInt(local.z / cs));
        }

        public Vector3 GridToWorld(Vector3Int gridPos)
        {
            float cs = gridSize.CellSize();
            return transform.TransformPoint(new Vector3(gridPos.x, gridPos.y, gridPos.z) * cs);
        }

        /// <summary>
        /// Applies a saved movable-grid pose atomically. Physics stays kinematic for
        /// two fixed ticks while child blocks/colliders finish restoring, preventing
        /// a saved ship from snapping upright or receiving an unwanted torque.
        /// </summary>
        public void RestorePersistentPose(Vector3 position, Quaternion rotation,
            Vector3 velocity, Vector3 angularVelocity)
        {
            float rotationLengthSqr = rotation.x * rotation.x + rotation.y * rotation.y
                                    + rotation.z * rotation.z + rotation.w * rotation.w;
            if (!IsFiniteQuaternion(rotation) || rotationLengthSqr < 0.0001f)
                rotation = GravityProvider.IsRadial
                    ? GravityProvider.GetSurfaceRotation(position)
                    : Quaternion.identity;
            rotation = rotation.normalized;

            _restorePosition = position;
            _restoreRotation = rotation;
            _restoreVelocity = velocity;
            _restoreAngularVelocity = angularVelocity;
            _restorePoseTicks = 2;
            _restoreGroundClearanceTicks = 12;

            transform.SetPositionAndRotation(position, rotation);
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.position = position;
                _rb.rotation = rotation;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Runs once after a saved grid's blocks and colliders have been recreated.
        /// It preserves the saved pose but lifts a small interpenetration out of the
        /// supporting terrain/dock, preventing restored grids from settling visibly
        /// into the ground on the first post-load physics frame.
        /// </summary>
        public void ResolvePersistentGroundClearance()
        {
            if (_rb == null || BlockCount <= 0) return;

            Vector3 posePosition = _restorePoseTicks > 0 ? _restorePosition : _rb.position;
            Vector3 up = GravityProvider.GetUp(posePosition);
            if (up.sqrMagnitude < 0.0001f)
                up = Physics.gravity.sqrMagnitude > 0.0001f ? -Physics.gravity.normalized : Vector3.up;
            else
                up.Normalize();

            Physics.SyncTransforms();
            float desiredClearance = Mathf.Max(0.025f, gridSize.CellSize() * 0.015f);
            float maxLift = Mathf.Max(0.15f, gridSize.CellSize() * 0.42f);
            float neededLift = 0f;
            int samples = 0;

            foreach (var block in AllBlocks)
            {
                if (block == null) continue;
                // Large machinery frequently has multiple fitted colliders. Probe all
                // of them so a high upper hull collider cannot hide a lower skid/block
                // that is actually entering the ground.
                foreach (var collider in block.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;

                    Bounds bounds = collider.bounds;
                    float supportExtent = Mathf.Abs(up.x) * bounds.extents.x
                        + Mathf.Abs(up.y) * bounds.extents.y
                        + Mathf.Abs(up.z) * bounds.extents.z;
                    Vector3 origin = bounds.center + up * (supportExtent + block.EffectiveCellSize * 0.90f);
                    int hitCount = Physics.RaycastNonAlloc(origin, -up, s_restoreGroundHits,
                        supportExtent + block.EffectiveCellSize * 3.0f, ~0, QueryTriggerInteraction.Ignore);

                    float nearestDistance = float.PositiveInfinity;
                    RaycastHit nearest = default;
                    for (int i = 0; i < hitCount; i++)
                    {
                        var hit = s_restoreGroundHits[i];
                        if (hit.collider == null || !hit.collider.enabled) continue;
                        if (hit.collider.transform.IsChildOf(transform)) continue;
                        if (VoxelEngine.Player.PlayerRaycastFilter.IsOwnPlayerCollider(hit.collider, transform)) continue;
                        if (hit.distance >= nearestDistance) continue;
                        nearestDistance = hit.distance;
                        nearest = hit;
                    }
                    if (nearest.collider == null) continue;

                    float bottomAlongUp = Vector3.Dot(bounds.center, up) - supportExtent;
                    float supportAlongUp = Vector3.Dot(nearest.point, up);
                    float clearance = bottomAlongUp - supportAlongUp;
                    if (clearance < desiredClearance)
                        neededLift = Mathf.Max(neededLift, desiredClearance - clearance);

                    if (++samples >= 64) break;
                }
                if (samples >= 64) break;
            }

            if (neededLift <= 0.0005f || neededLift > maxLift) return;
            Vector3 correctedPosition = posePosition + up * neededLift;
            if (_restorePoseTicks > 0) _restorePosition = correctedPosition;
            transform.SetPositionAndRotation(correctedPosition, _restoreRotation);
            _rb.position = correctedPosition;
            _rb.rotation = _restoreRotation;
            // Do not carry a downward contact impulse into the freshly corrected pose.
            if (_restorePoseTicks <= 0)
            {
                float downward = Vector3.Dot(_rb.linearVelocity, -up);
                if (downward > 0f) _rb.linearVelocity += up * downward;
            }
        }

        private static bool IsFiniteQuaternion(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y)
                && !float.IsNaN(value.z) && !float.IsNaN(value.w)
                && !float.IsInfinity(value.x) && !float.IsInfinity(value.y)
                && !float.IsInfinity(value.z) && !float.IsInfinity(value.w);
        }

        /// <summary>
        /// Approximate visual center of the grid (average of all block world positions).
        /// Used by the cockpit third-person camera to pivot around the ship.
        /// </summary>
        public Vector3 GetGridCenter()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var block in AllBlocks)
            {
                sum += block.transform.position;
                count++;
            }
            return count > 0 ? sum / count : transform.position;
        }

        /// <summary>Sum of every block's structural mass + its current content mass
        /// (cargo items, stored fluids, ammo). Drives the rigidbody mass so a loaded
        /// ship genuinely flies heavier.</summary>
        public float TotalMass { get; private set; }

        public void RecalculateMass()
        {
            if (_rb == null) return;
            float mass = 0f;
            Vector3 weightedPosSum = Vector3.zero;
            float totalWeightForCOM = 0f;

            foreach (var kv in _blocks)
            {
                if (kv.Value == null) continue;
                float m = Mathf.Max(kv.Value.TotalMass, MinimumRuntimeBlockMass(kv.Value));
                mass += m;
                weightedPosSum += kv.Value.transform.localPosition * m;
                totalWeightForCOM += m;
            }

            var precisionLayer = PrecisionAttachments;
            if (precisionLayer != null)
            {
                foreach (var kv in precisionLayer.Blocks)
                {
                    if (kv.Value == null) continue;
                    float m = Mathf.Max(1f, kv.Value.TotalMass);
                    mass += m;
                    weightedPosSum += kv.Value.transform.localPosition * m;
                    totalWeightForCOM += m;
                }
            }

            TotalMass = mass;
            _rb.mass = Mathf.Max(1f, mass);
            // Center of mass at average block local position prevents tipping on
            // uneven builds and keeps planet-surface alignment stable.
            if (totalWeightForCOM > 0.01f)
                _rb.centerOfMass = weightedPosSum / totalWeightForCOM;
            else
                _rb.centerOfMass = Vector3.zero;
        }

        private float MinimumRuntimeBlockMass(GridBlock block)
        {
            if (block == null) return 1f;
            bool small = gridSize == GridSize.Small;
            if (block is GridArmorBlock) return small ? 450f : 10000f;
            if (block is GridCockpit) return small ? 700f : 12000f;
            if (block is GridCargoContainer) return small ? 900f : 8500f;
            if (block is GridWheel wheel)
            {
                if (wheel.wheelSizeCells >= 5) return 12000f;
                if (wheel.wheelSizeCells >= 3) return 6500f;
                return 3200f;
            }
            return small ? 180f : 2500f;
        }

        // Content mass changes constantly (items dragged in/out, fluids filling).
        // Refresh at a light cadence so the ship's handling reflects its load.
        private float _massTimer;
        private void RefreshContentMass()
        {
            _massTimer += Time.deltaTime;
            if (_massTimer < 0.5f) return;
            _massTimer = 0f;
            RecalculateMass();
        }

        // ── Grid-Wide Power ────────────────────────────────────────
        private void UpdatePower()
        {
            // Power is resolved in one central pass so battery modes become deterministic:
            //   • explicit Discharge batteries can power loads immediately,
            //   • Recharge / Auto batteries can absorb genuine surplus,
            //   • a Discharge battery can intentionally feed a Recharge / Auto battery.
            //
            // MaritimePropulsionSystem runs before this method ([DefaultExecutionOrder(-20)])
            // so maritime generators' ApplyResults() has written GeneratedWatts/CurrentRPM
            // and the MaritimePropulsionSystem.ElectricityGenerated/ElectricityDemand totals
            // reflect the latest mechanical-simulation tick.
            float generatedWatts = 0f;
            float consumedWatts = 0f;
            float h2Cap = 0f;
            float h2Stored = 0f;
            float o2Stored = 0f;
            var batteries = _batteryScratch;
            batteries.Clear();
            var electricalPropellers = _electricalPropellerScratch;
            electricalPropellers.Clear();

            foreach (var block in AllBlocks)
            {
                if (block == null) continue;
                if (!block.Enabled)
                {
                    if (block is GridBattery disabledBattery) disabledBattery.BeginPowerTick();
                    continue;
                }

                if (block is GridBattery battery)
                {
                    battery.BeginPowerTick();
                    batteries.Add(battery);
                    continue;
                }

                generatedWatts += Mathf.Max(0f, block.PowerOutput);
                consumedWatts += Mathf.Max(0f, block.PowerDraw);

                if (block is GridElectricalPropeller electricalPropeller)
                    electricalPropellers.Add(electricalPropeller);

                if (block is GridGasTank gasTank)
                {
                    if (gasTank.gasType == Gas.GasType.Hydrogen)
                    {
                        h2Cap += gasTank.capacity;
                        h2Stored += gasTank.stored;
                    }
                    else if (gasTank.gasType == Gas.GasType.Oxygen)
                    {
                        o2Stored += gasTank.stored;
                    }
                }
                else if (block is GridHydrogenEngine hydrogenEngine)
                {
                    h2Stored += hydrogenEngine.internalHydrogen;
                }
                else if (block is GridH2O2Generator h2o2)
                {
                    h2Stored += h2o2.h2Stored;
                    o2Stored += h2o2.o2Stored;
                }
            }

            // ── Maritime power ledger ─────────────────────────────────
            // Maritime generator output and electrical-propeller demand are already
            // exposed through each GridBlock's PowerOutput / PowerDraw and were
            // counted above. The propulsion-system totals remain telemetry only;
            // adding them again here would double-bill both sides of the power bus.
            var maritime = Maritime;

            // Log the independent block ledger beside the mechanical telemetry so
            // a validation run can spot any future accounting mismatch without
            // changing the actual power calculation.
            if ((Time.frameCount & 127) == 0 && Debug.isDebugBuild)
            {
                Debug.Log($"[{gameObject.name}] GRID POWER: gen={generatedWatts:F1}W draw={consumedWatts:F1}W " +
                    $"mechanicalTelemetry gen={maritime?.ElectricityGenerated ?? 0f:F1}W " +
                    $"demand={maritime?.ElectricityDemand ?? 0f:F1}W " +
                    $"batteries={_batteryScratch.Count}");
            }

            float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            float remainingDemand = Mathf.Max(0f, consumedWatts - generatedWatts);
            float totalBatteryDischarge = 0f;
            float totalBatteryCharge = 0f;

            // 1) Meet real grid load.
            totalBatteryDischarge += SupplyDemandFromBatteries(batteries, GridBatteryMode.Discharge, ref remainingDemand, dt);
            totalBatteryDischarge += SupplyDemandFromBatteries(batteries, GridBatteryMode.Auto, ref remainingDemand, dt);

            // Resolve actual service level before any surplus is diverted into
            // battery charging. Electrical propellers use this next tick as their
            // delivered-power fraction, preventing a no-power/full-power oscillation.
            float suppliedConsumerWatts = Mathf.Max(0f, consumedWatts - remainingDemand);
            PowerAvailability01 = consumedWatts > 0.01f
                ? Mathf.Clamp01(suppliedConsumerWatts / consumedWatts)
                : 1f;
            UnservedPowerWatts = Mathf.Max(0f, remainingDemand);
            for (int i = 0; i < electricalPropellers.Count; i++)
                electricalPropellers[i]?.SetGridPowerAvailability(PowerAvailability01);

            // 2) Work out how much surplus can be used for charging.
            float externalSurplus = Mathf.Max(0f, generatedWatts - consumedWatts);
            float chargeDemand = BatteryChargeDemand(batteries, preferRechargeOnly: false, dt);

            // 3) Explicit Discharge batteries are allowed to push spare energy onto the bus
            //    specifically to charge other batteries.
            float forcedTransferDemand = Mathf.Max(0f, chargeDemand - externalSurplus);
            if (forcedTransferDemand > 0.01f)
            {
                float forcedTransfer = forcedTransferDemand;
                totalBatteryDischarge += SupplyDemandFromBatteries(batteries, GridBatteryMode.Discharge, ref forcedTransfer, dt);
                externalSurplus += forcedTransferDemand - forcedTransfer;
            }

            // 4) Prefer explicit Recharge batteries, then Auto batteries that did not already discharge.
            float availableChargeWatts = externalSurplus;
            totalBatteryCharge += ChargeBatteries(batteries, GridBatteryMode.Recharge, ref availableChargeWatts, dt);
            totalBatteryCharge += ChargeAutoBatteries(batteries, ref availableChargeWatts, dt);

            PowerGenerated = generatedWatts + totalBatteryDischarge;
            PowerConsumed = consumedWatts + totalBatteryCharge;
            HydrogenCapacity = h2Cap;
            HydrogenStored = h2Stored;
            OxygenStored = o2Stored;
        }

        private static float SupplyDemandFromBatteries(List<GridBattery> batteries, GridBatteryMode mode,
            ref float remainingDemand, float dt)
        {
            if (remainingDemand <= 0.01f || batteries == null || batteries.Count == 0) return 0f;

            // Fair-share discharge: the demand is spread across every battery that can
            // still deliver, so packs drain TOGETHER instead of the first battery in the
            // list doing all the work while the rest sit idle.
            float delivered = 0f;
            var saturated = new HashSet<int>(batteries.Count);
            for (int round = 0; round < 8 && remainingDemand > 0.01f; round++)
            {
                int candidates = 0;
                for (int i = 0; i < batteries.Count; i++)
                {
                    var battery = batteries[i];
                    if (battery == null || battery.mode != mode || saturated.Contains(i)) continue;
                    if (battery.AvailableDischargeWatts(dt) > 0.01f) candidates++;
                    else saturated.Add(i);
                }
                if (candidates == 0) break;

                float share = remainingDemand / candidates;
                float deliveredThisRound = 0f;
                for (int i = 0; i < batteries.Count; i++)
                {
                    var battery = batteries[i];
                    if (battery == null || battery.mode != mode || saturated.Contains(i)) continue;
                    float sent = battery.DischargeToBus(share, dt);
                    deliveredThisRound += sent;
                    if (sent < share - 0.01f) saturated.Add(i); // rate-limited or nearly empty
                }
                delivered += deliveredThisRound;
                remainingDemand -= deliveredThisRound;
                if (deliveredThisRound < 0.01f) break;
            }

            return delivered;
        }

        private static float BatteryChargeDemand(List<GridBattery> batteries, bool preferRechargeOnly, float dt)
        {
            if (batteries == null || batteries.Count == 0) return 0f;

            float demand = 0f;
            for (int i = 0; i < batteries.Count; i++)
            {
                var battery = batteries[i];
                if (battery == null) continue;
                if (preferRechargeOnly)
                {
                    if (battery.mode != GridBatteryMode.Recharge) continue;
                }
                else if (battery.mode == GridBatteryMode.Discharge)
                {
                    continue;
                }

                demand += battery.AvailableChargeWatts(dt);
            }

            return demand;
        }

        private static float ChargeBatteries(List<GridBattery> batteries, GridBatteryMode mode,
            ref float availableWatts, float dt)
            => ChargeBatteriesFairShare(batteries, mode, skipDischargingAuto: false, ref availableWatts, dt);

        private static float ChargeAutoBatteries(List<GridBattery> batteries, ref float availableWatts, float dt)
            => ChargeBatteriesFairShare(batteries, GridBatteryMode.Auto, skipDischargingAuto: true, ref availableWatts, dt);

        /// <summary>
        /// Fair-share charging: surplus watts are split EQUALLY between every battery
        /// that can still accept charge — so a grid with several batteries tops them all
        /// up together instead of greedily charging only the first one in the list.
        /// Water-filling rounds re-offer leftover watts from full/rate-limited packs to
        /// the batteries that can still take more.
        /// </summary>
        private static float ChargeBatteriesFairShare(List<GridBattery> batteries, GridBatteryMode mode,
            bool skipDischargingAuto, ref float availableWatts, float dt)
        {
            if (availableWatts <= 0.01f || batteries == null || batteries.Count == 0) return 0f;

            float acceptedTotal = 0f;
            var saturated = new HashSet<int>(batteries.Count);
            for (int round = 0; round < 8 && availableWatts > 0.01f; round++)
            {
                int candidates = 0;
                for (int i = 0; i < batteries.Count; i++)
                {
                    var battery = batteries[i];
                    if (battery == null || battery.mode != mode || saturated.Contains(i)) continue;
                    if (skipDischargingAuto && battery.IsDischarging) { saturated.Add(i); continue; }
                    if (battery.AvailableChargeWatts(dt) > 0.01f) candidates++;
                    else saturated.Add(i);
                }
                if (candidates == 0) break;

                float share = availableWatts / candidates;
                float acceptedThisRound = 0f;
                for (int i = 0; i < batteries.Count; i++)
                {
                    var battery = batteries[i];
                    if (battery == null || battery.mode != mode || saturated.Contains(i)) continue;
                    float taken = battery.ChargeFromBus(share, dt);
                    acceptedThisRound += taken;
                    availableWatts -= taken;
                    if (taken < share - 0.01f) saturated.Add(i); // nearly full or rate-limited
                }
                acceptedTotal += acceptedThisRound;
                if (acceptedThisRound < 0.01f) break;
            }

            return acceptedTotal;
        }

        // Multiplier on raw thruster Newtons so SI-balanced ships fly responsively.
        private const float THRUST_GAIN = 1.0f;

        // ── Thrust Application ─────────────────────────────────────
        //
        // PER-DIRECTION thrust (grid systems style). The pilot's WASD/Space/Ctrl
        // input is expressed in COCKPIT-LOCAL axes (x=right, y=up, z=forward). For each
        // requested axis we sum ONLY the thrusters that actually push the ship that way,
        // then apply real force (N) → the ship moves exactly where the cockpit is facing
        // and is limited by how many thrusters point that way.
        //
        //   A thruster's flame ("particles") comes out its LOCAL -forward, so it PUSHES
        //   the ship along its LOCAL +forward. We classify each thruster by its push
        //   direction in the cockpit's frame.
        private void UpdateThrust()
        {
            // Reset all thruster visual/audio fractions; only the ones that actually fire
            // this frame will get a new value below.
            foreach (var block in AllBlocks)
                if (block is GridThruster t) t.ThrustFraction = 0f;

            if (!IsControlled)
            {
                // Decay any remaining smoothed input so the ship doesn't lurch when re-entered.
                _smoothedThrustInput = Vector3.MoveTowards(_smoothedThrustInput, Vector3.zero, THRUST_SPOOL_RATE * 2f * Time.fixedDeltaTime);
                ApplyAutonomousDampenerThrust();
                return;
            }

            AutonomousDampenersActive = false;

            // Control-seat local frame (so "forward" = where the pilot is looking).
            Transform frame = CurrentControlFrame;

            // Smooth the pilot's binary key input so thrust ramps up/down instead of
            // snapping instantly. This is the core of "feeling the mass" of the ship.
            Vector3 input = Vector3.MoveTowards(_smoothedThrustInput, ThrustInput, THRUST_SPOOL_RATE * Time.fixedDeltaTime);
            _smoothedThrustInput = input;

            // Accumulate world-space force from the thrusters that push each requested way.
            Vector3 worldForce = Vector3.zero;
            foreach (var block in AllBlocks)
            {
                if (!(block is GridThruster thruster) || !thruster.IsOperational) continue;

                // The direction this thruster pushes the ship, in the cockpit's local frame.
                Vector3 pushLocal = frame.InverseTransformDirection(thruster.PushDirection);

                // Does the pilot want thrust along this thruster's push axis?
                float want =
                      pushLocal.x * Mathf.Clamp(input.x, -1f, 1f)
                    + pushLocal.y * Mathf.Clamp(input.y, -1f, 1f)
                    + pushLocal.z * Mathf.Clamp(input.z, -1f, 1f);

                if (want <= 0.05f) continue; // this thruster doesn't help the requested move

                float fraction = Mathf.Clamp01(want);
                thruster.ThrustFraction = fraction;

                // Consume this thruster's fuel/power + get its usable thrust (N), then push
                // the ship along the thruster's real push direction (so it stays balanced).
                float thrustN = thruster.AvailableThrust(input, this, fraction) * fraction;
                worldForce += thruster.PushDirection * thrustN;
            }

            if (worldForce.sqrMagnitude > 0.0001f)
            {
                // Real force in Newtons → ForceMode.Force divides by mass, so a heavy or
                // lightly-thrusted ship genuinely struggles (no more "too much thrust").
                _rb.AddForce(worldForce * THRUST_GAIN, ForceMode.Force);
            }

            Vector3 rotInput = new Vector3(RotationPitch, RotationYaw, RotationRoll);
            // Rotational authority comes from installed, powered-on gyroscopes.
            // Bigger/heavier ships turn slower; adding more gyros restores authority.
            float gyroTorque = 0f;
            foreach (var block in AllBlocks)
                if (block is GridGyroscope gy && gy.Enabled) gyroTorque += gy.torquePower;
            if (gyroTorque > 0f && rotInput.sqrMagnitude > 0.0001f)
            {
                Vector3 worldTorque = frame.TransformDirection(rotInput);
                float massFactor = 10000f / Mathf.Max(10000f, _rb.mass);
                worldTorque *= gyroTorque * 0.0005f * massFactor;
                _rb.AddTorque(worldTorque, ForceMode.Acceleration);
            }
            // Angular damping lets the ship stop spinning cleanly when the pilot releases input.
            // On ice keep it lower so landed grids can skid/rotate instead of feeling glued.
            _rb.angularDamping = _touchingIce ? 0.6f : 3f;
        }

        /// <summary>
        /// Uses installed reverse-facing thrusters as visible, resource-consuming
        /// braking authority whenever an unpiloted ship is left unlocked with
        /// dampeners enabled. Residual velocity is finished by UpdateDampeners.
        /// </summary>
        private void ApplyAutonomousDampenerThrust()
        {
            AutonomousDampenersActive = false;
            if (_rb == null || !DampenersOn || HasStationaryLock() || !HasAutonomousDampenerEnergy()) return;

            Vector3 velocity = _rb.linearVelocity;
            Vector3 gravity = CurrentGravityAcceleration();
            bool preserveGravityAxis = gravity.sqrMagnitude > 0.0001f && !ShouldDampenerHoldHover();
            Vector3 brakeVelocity = preserveGravityAxis
                ? Vector3.ProjectOnPlane(velocity, gravity.normalized)
                : velocity;
            float speed = brakeVelocity.magnitude;
            if (speed < 0.005f)
            {
                AutonomousDampenersActive = true;
                return;
            }

            Vector3 desiredDirection = -brakeVelocity / speed;
            float demand01 = Mathf.Clamp01(speed / 3f);
            Vector3 brakingForce = Vector3.zero;
            foreach (var block in AllBlocks)
            {
                if (block is not GridThruster thruster || !thruster.IsOperational) continue;
                float alignment = Vector3.Dot(thruster.PushDirection.normalized, desiredDirection);
                if (alignment <= 0.15f) continue;
                float fraction = Mathf.Clamp01(alignment * demand01);
                thruster.ThrustFraction = Mathf.Max(thruster.ThrustFraction, fraction);
                float thrust = thruster.AvailableThrust(Vector3.zero, this, fraction) * fraction;
                brakingForce += thruster.PushDirection * thrust;
            }

            if (brakingForce.sqrMagnitude > 0.0001f)
                _rb.AddForce(brakingForce * THRUST_GAIN, ForceMode.Force);
            AutonomousDampenersActive = true;
        }

        // ── Inertia Dampeners ──────────────────────────────────────
        private void UpdateDampeners()
        {
            PilotDampenerHoldActive = false;
            if (!DampenersOn || _rb == null) return;

            // An unpiloted grid only receives damping authority when it has stored
            // power/hydrogen or live generation. A seated pilot deliberately commands
            // station keeping, so pilot hold is reliable both over a planet and in vacuum.
            bool autonomous = !IsControlled;
            if (autonomous && !AutonomousDampenersActive) return;

            bool isThrusting = HasManualThrustInput();
            bool pilotHold = IsControlled && !isThrusting;
            PilotDampenerHoldActive = pilotHold;

            Vector3 vel = _rb.linearVelocity;
            if (!isThrusting && vel.sqrMagnitude > 0.0001f)
            {
                float massFactor = 10000f / Mathf.Max(10000f, _rb.mass);
                // Piloted hold is intentionally decisive: it must actually settle at
                // velocity zero rather than preserving the vertical gravity component.
                float brake = pilotHold ? 30f : autonomous ? 12f : 2.5f * massFactor;
                bool iceRecovery = IsRecoveringFromIceContact();
                if (iceRecovery && !pilotHold) brake *= IceGridBrakeMultiplier;

                Vector3 gravity = CurrentGravityAcceleration();
                Vector3 dampedVelocity = vel;
                bool hasGravity = gravity.sqrMagnitude > 0.0001f;
                bool hoverHold = ShouldDampenerHoldHover();
                bool fullStop = pilotHold || hoverHold;

                // Preserve a natural gravity-axis fall only for unattended/non-hover
                // grids. The cockpit hold requested by the pilot cancels all axes.
                if (hasGravity && !fullStop && (iceRecovery || !hoverHold))
                    dampedVelocity = Vector3.ProjectOnPlane(vel, gravity.normalized);

                float speed = dampedVelocity.magnitude;
                float settle = pilotHold ? 1f : Mathf.Clamp01(speed / 0.5f);
                if (speed > 0.0001f)
                    _rb.AddForce(-dampedVelocity * brake * settle, ForceMode.Acceleration);

                // Snap the final residual so the held craft genuinely reads 0.0 m/s.
                float snapThreshold = pilotHold ? 0.08f : 0.03f;
                if (speed < snapThreshold)
                {
                    if (fullStop && !iceRecovery)
                        _rb.linearVelocity = Vector3.zero;
                    else if (hasGravity)
                        _rb.linearVelocity = Vector3.Project(vel, gravity.normalized);
                    else
                        _rb.linearVelocity = Vector3.zero;
                }
            }

            Vector3 angularVelocity = _rb.angularVelocity;
            if (!isThrusting && angularVelocity.sqrMagnitude > 0.0001f)
            {
                float angularBrake = _touchingIce && !pilotHold ? 0.75f : pilotHold ? 18f : autonomous ? 10f : 4f;
                _rb.angularVelocity = Vector3.Lerp(angularVelocity, Vector3.zero, angularBrake * Time.fixedDeltaTime);
                if ((pilotHold || autonomous) && _rb.angularVelocity.sqrMagnitude < 0.0004f)
                    _rb.angularVelocity = Vector3.zero;
            }
        }

        // ── Planet surface alignment for grounded grids ──────────────
        // Grids placed on a spherical planet should stay aligned to the local
        // surface normal instead of slowly tipping over. When the grid is grounded
        // (landing gear locked or low velocity near surface) and not piloted, we
        // gently slerp its up toward planet up.
        private float _alignTimer;
        private void StabilizeGroundAlignment()
        {
            if (!GravityProvider.IsRadial) return;
            if (IsControlled) return;
            if (_rb == null) return;

            // Only stabilize when grounded-ish: landing gear locked, wheels grounded,
            // or low vertical velocity near surface
            bool anyLocked = false;
            bool anyGrounded = false;
            foreach (var block in AllBlocks)
            {
                if (block is GridLandingGear lg)
                {
                    if (lg.IsLocked) anyLocked = true;
                }
                if (block is GridWheel wh && wh.IsGrounded) anyGrounded = true;
            }

            float vertSpeed = 0f;
            Vector3 grav = CurrentGravityAcceleration();
            if (grav.sqrMagnitude > 0.0001f)
                vertSpeed = Mathf.Abs(Vector3.Dot(_rb.linearVelocity, grav.normalized));

            bool nearGround = vertSpeed < 0.5f && _rb.linearVelocity.magnitude < 1.5f;
            if (!anyLocked && !anyGrounded && !nearGround) return;

            // Throttle alignment to 4 Hz to avoid fighting physics
            _alignTimer += Time.fixedDeltaTime;
            if (_alignTimer < 0.25f) return;
            _alignTimer = 0f;

            Vector3 planetUp = GravityProvider.GetUp(transform.position);
            if (planetUp.sqrMagnitude < 0.0001f) return;

            Vector3 currentUp = transform.up;
            float angle = Vector3.Angle(currentUp, planetUp);
            if (angle < 0.5f) return; // already aligned
            if (angle > 45f) return; // too far, don't snap (likely in flight)

            // Slerp toward surface-aligned rotation
            Vector3 currentForward = transform.forward;
            Vector3 desiredForward = Vector3.ProjectOnPlane(currentForward, planetUp);
            if (desiredForward.sqrMagnitude < 0.001f)
                desiredForward = Vector3.ProjectOnPlane(Vector3.forward, planetUp);
            if (desiredForward.sqrMagnitude < 0.001f)
                desiredForward = Vector3.ProjectOnPlane(Vector3.right, planetUp);
            desiredForward.Normalize();

            Quaternion desiredRot = Quaternion.LookRotation(desiredForward, planetUp);
            // Gentle slerp, preserve position
            Quaternion newRot = Quaternion.Slerp(transform.rotation, desiredRot, 0.08f);
            _rb.MoveRotation(newRot);

            // Damp angular velocity that would tip it over
            if (_rb.angularVelocity.magnitude > 0.1f)
                _rb.angularVelocity *= 0.85f;
        }

        // ── Wheels ─────────────────────────────────────────────────
        private void UpdateWheels()
        {
            foreach (var block in AllBlocks)
            {
                if (block is GridWheel wheel)
                    wheel.UpdateWheel(this);
            }
        }

        public static GridEntity Create(Vector3 position, GridSize size)
        {
            var go = new GameObject($"Grid_{size}_{Time.frameCount}");
            go.transform.position = position;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.useGravity = false;
            rb.linearDamping = 0f;
            rb.angularDamping = 1.5f;
            var entity = go.AddComponent<GridEntity>();
            entity.gridSize = size;
            return entity;
        }
    }
}