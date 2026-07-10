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
using VoxelEngine.Maritime;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(Rigidbody))]
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
        public int BlockCount => _blocks.Count;

        // ── Physics ────────────────────────────────────────────────
        private Rigidbody _rb;
        public Rigidbody Body => _rb;

        // ── Power (grid-wide, no cables) ───────────────────────────
        public float PowerGenerated { get; private set; }
        public float PowerConsumed  { get; private set; }
        public float PowerBalance   => PowerGenerated - PowerConsumed;
        public bool  HasPower       => PowerBalance >= -0.1f;

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
            foreach (var kv in _blocks)
            {
                if (!(kv.Value is GridThruster t)) continue;
                // Thruster pushes the ship along its local forward.
                Vector3 d = transform.InverseTransformDirection(t.transform.forward);
                if (d.z >  0.5f) fwd   += t.maxThrustN;
                if (d.z < -0.5f) back  += t.maxThrustN;
                if (d.x >  0.5f) right += t.maxThrustN;
                if (d.x < -0.5f) left  += t.maxThrustN;
                if (d.y >  0.5f) up    += t.maxThrustN;
                if (d.y < -0.5f) down  += t.maxThrustN;
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
            foreach (var kv in _blocks)
            {
                if (kv.Value is GridDrill)  hasDrill  = true;
                else if (kv.Value is GridWeapon) hasWeapon = true;
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
            foreach (var kv in _blocks)
                if (kv.Value is GridDrill || kv.Value is GridWeapon) list.Add(kv.Value);
            return list;
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
            // Belt-and-braces: generated/loaded grids should never use Unity's built-in
            // gravity because this class applies planet gravity manually.
            if (_rb != null) _rb.useGravity = false;

            UpdatePower();
            UpdateThrust();
            UpdateDampeners();
            UpdateWheels();
            ApplyGravity();

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
            return Physics.gravity * AtmosphereManager.GetGravityMultiplier(transform.position) * Mathf.Max(0f, gravityScale);
        }

        private bool HasManualThrustInput()
        {
            return IsControlled && ThrustInput.sqrMagnitude > 0.01f;
        }

        private bool ShouldDampenerHoldHover()
        {
            return DampenersOn && !HasManualThrustInput() && HasHoverAuthority();
        }

        private bool HasHoverAuthority()
        {
            Vector3 gravity = CurrentGravityAcceleration();
            if (gravity.sqrMagnitude < 0.0001f) return false;

            Vector3 antiGravity = -gravity.normalized;
            foreach (var kv in _blocks)
            {
                if (!(kv.Value is GridThruster thruster)) continue;
                if (!thruster.Enabled || !thruster.IsOperational) continue;
                if (Vector3.Dot(thruster.PushDirection.normalized, antiGravity) > 0.35f)
                    return true;
            }

            return false;
        }

        private void ApplyGravity()
        {
            if (_rb == null) return;

            // Dampeners are a true hover-hold assist: when the pilot is not asking for
            // translation, the ship should not continue falling through atmosphere gravity.
            if (ShouldDampenerHoldHover()) return;

            _rb.AddForce(CurrentGravityAcceleration(), ForceMode.Acceleration);
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

            if (block.GetComponent<Collider>() == null)
            {
                var box = block.gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one * cs;
            }

            RecalculateMass();
            block.OnPlaced();

            // Notify the maritime propulsion graph that the ship changed
            // (it lazily rebuilds next FixedUpdate — zero per-block work).
            NotifyMaritimeDirty();
        }

        public void RemoveBlock(Vector3Int gridPos)
        {
            if (!_blocks.TryGetValue(gridPos, out var block)) return;
            _blocks.Remove(gridPos);
            block.OnRemoved();
            Destroy(block.gameObject);
            RecalculateMass();

            NotifyMaritimeDirty();

            if (_blocks.Count == 0)
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
        /// Approximate visual center of the grid (average of all block world positions).
        /// Used by the cockpit third-person camera to pivot around the ship.
        /// </summary>
        public Vector3 GetGridCenter()
        {
            if (_blocks.Count == 0) return transform.position;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var kv in _blocks)
            {
                if (kv.Value == null) continue;
                sum += kv.Value.transform.position;
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
            foreach (var kv in _blocks)
            {
                if (kv.Value == null) continue;
                mass += Mathf.Max(kv.Value.TotalMass, MinimumRuntimeBlockMass(kv.Value));
            }
            TotalMass = mass;
            _rb.mass = Mathf.Max(1f, mass);
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
            // Sum live GENERATORS (reactors, solar, …) and CONSUMERS first. Batteries are
            // handled separately below so they fill any deficit — this avoids the per-frame
            // ordering flicker where a drill turning on would briefly read HasPower=false
            // (the battery's discharge lagged a frame behind the new load).
            float gen = 0, con = 0, h2Cap = 0, h2Stored = 0, o2Stored = 0, batteryReserve = 0;
            foreach (var kv in _blocks)
            {
                var b = kv.Value;
                if (b == null || !b.Enabled) continue;
                if (b is GridBattery bat)
                {
                    batteryReserve += bat.AvailableDischargeWatts; // what it COULD supply this frame
                    continue;
                }
                gen += b.PowerOutput;
                con += b.PowerDraw;
                if (b is GridGasTank gt)
                {
                    if (gt.gasType == Gas.GasType.Hydrogen)
                    {
                        h2Cap += gt.capacity;
                        h2Stored += gt.stored;
                    }
                    else if (gt.gasType == Gas.GasType.Oxygen)
                    {
                        o2Stored += gt.stored;
                    }
                }
                else if (b is GridHydrogenEngine he)
                {
                    h2Stored += he.internalHydrogen;
                }
                else if (b is GridH2O2Generator h2o2)
                {
                    h2Stored += h2o2.h2Stored;
                    o2Stored += h2o2.o2Stored;
                }
            }

            // Batteries top up the generation up to the demand (never beyond what they can give).
            float deficit = Mathf.Max(0f, con - gen);
            float fromBatteries = Mathf.Min(deficit, batteryReserve);

            PowerGenerated = gen + fromBatteries;
            PowerConsumed = con;
            HydrogenCapacity = h2Cap;
            HydrogenStored = h2Stored;
            OxygenStored = o2Stored;
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
            foreach (var kv in _blocks)
                if (kv.Value is GridThruster t) t.ThrustFraction = 0f;

            if (!IsControlled)
            {
                // Decay any remaining smoothed input so the ship doesn't lurch when re-entered.
                _smoothedThrustInput = Vector3.MoveTowards(_smoothedThrustInput, Vector3.zero, THRUST_SPOOL_RATE * 2f * Time.fixedDeltaTime);
                return;
            }

            // Control-seat local frame (so "forward" = where the pilot is looking).
            Transform frame = CurrentControlFrame;

            // Smooth the pilot's binary key input so thrust ramps up/down instead of
            // snapping instantly. This is the core of "feeling the mass" of the ship.
            Vector3 input = Vector3.MoveTowards(_smoothedThrustInput, ThrustInput, THRUST_SPOOL_RATE * Time.fixedDeltaTime);
            _smoothedThrustInput = input;

            // Accumulate world-space force from the thrusters that push each requested way.
            Vector3 worldForce = Vector3.zero;
            foreach (var kv in _blocks)
            {
                if (!(kv.Value is GridThruster thruster) || !thruster.IsOperational) continue;

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
                float thrustN = thruster.AvailableThrust(input, this) * fraction;
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
            foreach (var kv in _blocks)
                if (kv.Value is GridGyroscope gy && gy.Enabled) gyroTorque += gy.torquePower;
            if (gyroTorque > 0f && rotInput.sqrMagnitude > 0.0001f)
            {
                Vector3 worldTorque = frame.TransformDirection(rotInput);
                float massFactor = 10000f / Mathf.Max(10000f, _rb.mass);
                worldTorque *= gyroTorque * 0.0005f * massFactor;
                _rb.AddTorque(worldTorque, ForceMode.Acceleration);
            }
            // Angular damping so the ship stops spinning when you let go (SE-style).
            _rb.angularDamping = 3f;
        }

        // ── Inertia Dampeners ──────────────────────────────────────
        private void UpdateDampeners()
        {
            if (!DampenersOn) return;

            // Only dampen when the pilot isn't actively asking for thrust.
            bool isThrusting = HasManualThrustInput();

            Vector3 vel = _rb.linearVelocity;
            if (!isThrusting && vel.sqrMagnitude > 0.0001f)
            {
                // Mass-aware braking: heavier ships take longer to cancel drift, so the
                // dampeners feel like they're wrestling real inertia.
                float massFactor = 10000f / Mathf.Max(10000f, _rb.mass);
                float brake = 2.5f * massFactor;

                // Soften the brake at very low speeds so the ship coasts gently to a stop
                // instead of slamming on the brakes.
                float speed = vel.magnitude;
                float settle = Mathf.Clamp01(speed / 0.5f);
                _rb.AddForce(-vel * brake * settle, ForceMode.Acceleration);

                // Hard snap only when almost stopped, so the ship doesn't drift forever.
                if (speed < 0.03f)
                    _rb.linearVelocity = Vector3.zero;
            }

            Vector3 angVel = _rb.angularVelocity;
            if (!isThrusting && angVel.sqrMagnitude > 0.01f)
            {
                _rb.angularVelocity = Vector3.Lerp(angVel, Vector3.zero, 4f * Time.fixedDeltaTime);
            }
        }

        // ── Wheels ─────────────────────────────────────────────────
        private void UpdateWheels()
        {
            foreach (var kv in _blocks)
            {
                if (kv.Value is GridWheel wheel)
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