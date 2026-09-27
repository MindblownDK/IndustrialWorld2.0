// Assets/Scripts/VoxelEngine/GridSystem/GridWheel.cs
//
// THE WHEEL HUB — suspension, steering, drive. The rubber is a separate part.
//
// 13.0.0 splits the old one-piece wheel into a hub block that bolts to the grid and
// a tire that snaps onto the hub's mount socket. The hub is still the class every
// other system talks to (autopilot, terminal, pressure, road wear), so nothing
// downstream had to learn a new type — but a hub with no tire fitted is a bare
// stub: it carries no load, makes no torque and reports itself as such.
//
// Physics is hand-solved in WheelSuspensionSolver. No UnityEngine.WheelCollider is
// used anywhere: this rig lives under a Rigidbody that is rebuilt whenever a block
// is welded on, and a WheelCollider's hidden substepper does not survive that.
//
// Runtime hierarchy (created by the mesh builder, healed here if a prefab lacks it):
//   HubRoot                 — fills its grid cell, bolts to the frame
//     SteerPivot            — Y-axis steering angle, rate-limited
//       SuspensionCarrier   — slides along local -Y over the spring travel
//         TireSocket        — the tire parents here, exactly where the ghost snapped
//       UpperArm / LowerArm — visual linkages that aim at the moving carrier

using UnityEngine;
using VoxelEngine.Environment;

namespace VoxelEngine.GridSystem
{
    /// <summary>Which side of the hub the tire hangs off.</summary>
    public enum WheelMountSide { Right = 0, Left = 1 }

    public class GridWheel : GridBlock
    {
        [Header("Hub Size")]
        [Tooltip("Suspension frame class. Sets spring rate, travel, torque and steering authority.")]
        public WheelSizeClass sizeClass = WheelSizeClass.Size_3x3;
        [Tooltip("Legacy cell span (2, 3 or 5). Kept in sync with the size class.")]
        public int wheelSizeCells = 3;
        [Tooltip("Side the tire mounts on. Flip instead of rotating the whole hub.")]
        public WheelMountSide mountSide = WheelMountSide.Right;

        [Header("Suspension")]
        [Tooltip("Spring force per metre of compression (N/m).")]
        public float springForce = 300000f;
        [Tooltip("Damper force per m/s of suspension velocity (N per m/s).")]
        public float damping = 34000f;
        [Tooltip("Ride height the spring settles to, in metres.")]
        public float restLength = 1.25f;
        [Tooltip("Hard bump stop — the shortest the strut can get.")]
        public float minTravel = 0.22f;
        [Tooltip("Full droop — the longest the strut can get.")]
        public float suspensionLength = 1.8f;
        [Tooltip("Global stiffness trim from the terminal (0.05 soft .. 1 rigid).")]
        [Range(0.05f, 1f)] public float suspensionStrength = 0.55f;

        [Header("Drive")]
        [Tooltip("Axle torque at full throttle (N·m).")]
        public float motorTorque = 2000000f;
        [Tooltip("Service brake torque (N·m).")]
        public float brakeTorque = 2750000f;
        [Tooltip("Parking brake torque (N·m). Holds a loaded rig on a grade.")]
        public float handbrakeTorque = 4300000f;
        [Tooltip("Electrical draw while powered and driving (W).")]
        public float powerDrawWatts = 900f;
        [Tooltip("Legacy linear drive force (N). Derived from torque and tire radius.")]
        public float driveForce = 520000f;

        [Header("Steering")]
        [Tooltip("Set true for steered axles.")]
        public bool isSteerable = true;
        [Tooltip("Maximum steering angle in degrees.")]
        public float steerAngle = 32f;
        [Tooltip("Degrees per second toward the commanded angle.")]
        public float steerSpeed = 92f;
        [Tooltip("Degrees per second back to centre when the command releases.")]
        public float steerReturnSpeed = 135f;

        [Header("Tire Fitting")]
        [Tooltip("Tire size classes this hub frame accepts. Empty accepts every size.")]
        public WheelSizeClass[] acceptedTireSizes = new WheelSizeClass[0];

        // ── Runtime state ───────────────────────────────────────────────────
        public bool IsGrounded { get; private set; }
        public VoxelEngine.Building.AsphaltRoad GroundRoad { get; private set; }
        public Vector3 GroundPoint { get; private set; }
        public float GroundGrip { get; private set; } = 1f;
        /// <summary>0..1 combined drive + lateral slip. Dust FX and skid audio read this.</summary>
        public float WheelSlip { get; private set; }
        /// <summary>Human-readable surface under the contact patch ("Sand", "Asphalt").</summary>
        public string SurfaceName { get; private set; } = "—";
        /// <summary>Profile the contact patch resolved to. Slip FX and audio read it.</summary>
        public SurfaceProfile CurrentSurfaceProfile => _surface.Profile;
        public float SuspensionCompression01 { get; private set; }
        public float NormalLoad { get; private set; }
        public float SteerAngleCurrent { get; private set; }
        public GridWheelTire Tire { get; private set; }
        public bool HasTire => Tire != null;
        /// <summary>Item the fitted tire came from, so a save can refit it exactly.</summary>
        public VoxelEngine.Items.ItemDefinition MountedTireItem { get; private set; }

        /// <summary>Tire mass rides with the hub so the grid's centre of mass stays honest.</summary>
        public override float ContentMass => Tire != null ? Tire.BlockMass : 0f;

        public override float PowerDraw => Enabled && IsGrounded && HasTire && Grid != null
            ? powerDrawWatts * Mathf.Abs(Grid.WheelThrottle) * Mathf.Clamp01(suspensionStrength)
            : 0f;

        private Transform _steerPivot;
        private Transform _carrier;
        private Transform _socket;
        private Transform _upperArm;
        private Transform _lowerArm;
        private WheelSolveState _solveState;
        private SurfaceSample _surface = SurfaceSample.Default;
        private float _cellSize = WheelTuning.ReferenceCellSize;

        public float TireRadius => Tire != null ? Tire.Radius : WheelTuning.For(sizeClass, _cellSize).TireRadius;
        public WheelPreset Preset => WheelTuning.For(sizeClass, _cellSize);
        /// <summary>Socket the placement ghost snaps a tire to.</summary>
        public Transform TireSocket { get { EnsureRig(); return _socket; } }

        // ════════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ════════════════════════════════════════════════════════════════════

        public override void OnPlaced()
        {
            base.OnPlaced();
            _cellSize = EffectiveCellSize;
            SyncSizeClass();
            if (blockName == "Armor Block" || string.IsNullOrEmpty(blockName))
                blockName = $"Wheel Hub {sizeClass.Label()}";
            EnsureRig();
            ApplyPresetIfUnset();
            // Slip plumes are part of the wheel, not an authoring step: a hub placed from
            // any source (build, blueprint, save restore) gets the same visual feedback.
            if (GetComponent<WheelSlipFx>() == null) gameObject.AddComponent<WheelSlipFx>();
        }

        private void Awake() => EnsureRig();

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Tire != null)
            {
                var tire = Tire;
                Tire = null;
                tire.DetachFromHub();
                Destroy(tire.gameObject);
            }
        }

        /// <summary>Keeps the legacy cell-span field and the size class from drifting apart.</summary>
        private void SyncSizeClass()
        {
            if (wheelSizeCells != sizeClass.Cells())
            {
                // Whichever was authored last wins: a legacy prefab only sets the int.
                sizeClass = WheelTuning.FromCells(wheelSizeCells);
            }
            wheelSizeCells = sizeClass.Cells();
        }

        /// <summary>
        /// Fills unauthored tuning from the size table. Never overwrites a value a
        /// designer (or the setup tool) deliberately set, which is why each test is a
        /// "looks unset" test rather than a blanket assignment.
        /// </summary>
        private void ApplyPresetIfUnset()
        {
            var p = Preset;
            if (springForce <= 1f) springForce = p.SpringStrength;
            if (damping <= 1f) damping = p.DamperRate;
            if (restLength <= 0.05f) restLength = p.RestLength;
            if (minTravel <= 0.01f) minTravel = p.MinTravel;
            if (suspensionLength <= minTravel) suspensionLength = p.MaxTravel;
            if (motorTorque <= 1f) motorTorque = p.MotorTorque;
            if (brakeTorque <= 1f) brakeTorque = p.BrakeTorque;
            if (handbrakeTorque <= 1f) handbrakeTorque = p.HandbrakeTorque;
            if (powerDrawWatts <= 1f) powerDrawWatts = p.PowerDrawWatts;
            if (steerAngle <= 0.5f) steerAngle = p.MaxSteerAngle;
            if (steerSpeed <= 1f) steerSpeed = p.SteerSpeed;
            if (steerReturnSpeed <= 1f) steerReturnSpeed = p.SteerReturnSpeed;
            driveForce = motorTorque / Mathf.Max(0.25f, TireRadius);
        }

        /// <summary>Applies the whole preset, overwriting current tuning. Used by the size picker.</summary>
        public void ApplySizeClass(WheelSizeClass newClass)
        {
            sizeClass = newClass;
            wheelSizeCells = newClass.Cells();
            var p = Preset;
            springForce = p.SpringStrength;
            damping = p.DamperRate;
            restLength = p.RestLength;
            minTravel = p.MinTravel;
            suspensionLength = p.MaxTravel;
            motorTorque = p.MotorTorque;
            brakeTorque = p.BrakeTorque;
            handbrakeTorque = p.HandbrakeTorque;
            powerDrawWatts = p.PowerDrawWatts;
            steerAngle = p.MaxSteerAngle;
            steerSpeed = p.SteerSpeed;
            steerReturnSpeed = p.SteerReturnSpeed;
            driveForce = motorTorque / Mathf.Max(0.25f, TireRadius);
            EnsureRig();
            PlaceSocket(restLength);
        }

        /// <summary>Creates any rig transform a prefab is missing so the hub always works.</summary>
        private void EnsureRig()
        {
            if (_socket != null) return;
            _steerPivot = FindOrCreate(transform, "SteerPivot", Vector3.zero);
            _carrier = FindOrCreate(_steerPivot, "SuspensionCarrier", Vector3.zero);
            _socket = FindOrCreate(_carrier, "TireSocket", Vector3.zero);
            _upperArm = _steerPivot.Find("UpperArm");
            _lowerArm = _steerPivot.Find("LowerArm");
            PlaceSocket(restLength);
        }

        private static Transform FindOrCreate(Transform parent, string name, Vector3 localPosition)
        {
            var found = parent.Find(name);
            if (found != null) return found;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        /// <summary>Lateral offset of the mount, so the tire clears the hull it bolts to.</summary>
        public float MountOffsetX
        {
            get
            {
                float side = mountSide == WheelMountSide.Left ? -1f : 1f;
                float tireWidth = Tire != null ? Tire.Width : Preset.TireWidth;
                return side * (_cellSize * 0.5f + tireWidth * 0.55f);
            }
        }

        private void PlaceSocket(float springLength)
        {
            if (_carrier == null) return;
            _carrier.localPosition = new Vector3(MountOffsetX, -Mathf.Max(0f, springLength), 0f);
        }

        // ════════════════════════════════════════════════════════════════════
        //  TIRE FITTING
        // ════════════════════════════════════════════════════════════════════

        public bool AcceptsTireSize(WheelSizeClass tireSize)
        {
            if (acceptedTireSizes == null || acceptedTireSizes.Length == 0) return true;
            foreach (var accepted in acceptedTireSizes)
                if (accepted == tireSize) return true;
            return false;
        }

        /// <summary>Bolts a tire instance onto this hub. Returns false if one is already fitted.</summary>
        public bool MountTire(GridWheelTire tire, VoxelEngine.Items.ItemDefinition sourceItem = null)
        {
            if (tire == null || Tire != null) return false;
            if (!AcceptsTireSize(tire.sizeClass)) return false;

            EnsureRig();
            _cellSize = EffectiveCellSize;
            Tire = tire;
            MountedTireItem = sourceItem != null ? sourceItem : tire.SourceItem;
            tire.SourceItem = MountedTireItem;
            tire.AttachTo(this, _socket, _cellSize);
            PlaceSocket(restLength);
            driveForce = motorTorque / Mathf.Max(0.25f, TireRadius);
            Grid?.RecalculateMass();
            return true;
        }

        /// <summary>Unbolts the fitted tire and hands the caller the instance to dispose of.</summary>
        public GridWheelTire EjectTire()
        {
            var tire = Tire;
            if (tire == null) return null;
            Tire = null;
            MountedTireItem = null;
            tire.DetachFromHub();
            IsGrounded = false;
            WheelSlip = 0f;
            Grid?.RecalculateMass();
            return tire;
        }

        /// <summary>Called by a tire that was destroyed or removed behind the hub's back.</summary>
        internal void NotifyTireLost(GridWheelTire tire)
        {
            if (Tire != tire) return;
            Tire = null;
            MountedTireItem = null;
            IsGrounded = false;
            WheelSlip = 0f;
            Grid?.RecalculateMass();
        }

        /// <summary>
        /// Instantiates and fits the tire described by an item. Used by placement and by
        /// save restore, so both paths produce an identical rig.
        /// </summary>
        public bool MountTireFromItem(VoxelEngine.Items.ItemDefinition item)
        {
            if (item == null || Tire != null) return false;
            var blockItem = item as GridBlockItem;
            if (blockItem == null || blockItem.blockPrefab == null) return false;

            var instance = Instantiate(blockItem.blockPrefab);
            var tire = instance.GetComponent<GridWheelTire>();
            if (tire == null)
            {
                Destroy(instance);
                return false;
            }

            tire.blockName = blockItem.displayName;
            tire.BlockMass = blockItem.blockMass;
            tire.maxHP = blockItem.blockHP;
            tire.currentHP = blockItem.blockHP;
            tire.SourceItem = blockItem;
            tire.OnPlaced();
            if (!MountTire(tire, blockItem))
            {
                Destroy(instance);
                return false;
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  PHYSICS STEP
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Called once per FixedUpdate by the owning grid.</summary>
        public void UpdateWheel(GridEntity grid)
        {
            GroundRoad = null;

            // A tire shot to pieces blows off the rim rather than lingering at zero HP.
            if (Tire != null && Tire.currentHP <= 0f)
            {
                var blown = EjectTire();
                if (blown != null) Destroy(blown.gameObject);
            }
            if (!Enabled || grid == null || grid.Body == null || !HasTire)
            {
                IsGrounded = false;
                WheelSlip = 0f;
                SurfaceName = HasTire ? "—" : "No tire";
                UpdateSteerVisual(0f);
                PlaceSocket(suspensionLength);
                UpdateArms();
                return;
            }

            EnsureRig();
            _cellSize = EffectiveCellSize;
            float dt = Time.fixedDeltaTime;

            // ── Steering: rate-limited toward the command, surface-scaled ───
            float command = isSteerable ? Mathf.Clamp(grid.WheelSteering(this), -steerAngle, steerAngle) : 0f;
            command *= Mathf.Clamp(_surface.Steering, 0.2f, 1.6f);
            float rate = Mathf.Abs(command) > Mathf.Abs(SteerAngleCurrent) ? steerSpeed : steerReturnSpeed;
            SteerAngleCurrent = Mathf.MoveTowards(SteerAngleCurrent, command, rate * dt);
            UpdateSteerVisual(SteerAngleCurrent);

            // ── Frame vectors ───────────────────────────────────────────────
            Transform frame = grid.WheelFrame != null ? grid.WheelFrame : transform;
            Vector3 up = transform.up;
            Vector3 down = -up;
            Vector3 forward = Quaternion.AngleAxis(SteerAngleCurrent, up) * Vector3.ProjectOnPlane(frame.forward, up).normalized;
            if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            float mountRatio = Tire != null ? WheelTuning.MountRatio(sizeClass, Tire.sizeClass) : 1f;
            float radius = TireRadius;
            // The suspension top is the carrier's zero: travel is measured from there down.
            Vector3 originWorld = _steerPivot.TransformPoint(new Vector3(MountOffsetX, 0f, 0f));

            var input = new WheelSolveInput
            {
                Body = grid.Body,
                Origin = originWorld,
                SuspensionDown = down,
                Forward = forward,
                Right = right,
                GravityAccel = grid.WheelGravity,
                Radius = radius,
                CastRadius = Mathf.Clamp(Tire.Width * 0.35f, 0.1f, radius * 0.8f),
                RestLength = Mathf.Clamp(restLength * mountRatio, minTravel, suspensionLength),
                MinTravel = Mathf.Min(minTravel, suspensionLength * 0.5f),
                MaxTravel = suspensionLength * mountRatio,
                SpringStrength = springForce * Mathf.Clamp01(suspensionStrength) * 2f * mountRatio,
                DamperRate = damping * Mathf.Clamp(0.35f + suspensionStrength, 0.4f, 1.4f) * mountRatio,
                Throttle = grid.WheelThrottle,
                Brake = grid.WheelBrake,
                Handbrake = grid.WheelParkingBrake ? 1f : 0f,
                MotorTorque = motorTorque,
                BrakeTorque = brakeTorque,
                HandbrakeTorque = handbrakeTorque,
                Powered = grid.HasPower,
                StaticFriction = Tire.EffectiveStatic,
                DynamicFriction = Tire.EffectiveDynamic,
                LateralGrip = Tire.EffectiveLateral,
                SurfaceForward = _surface.Forward,
                SurfaceLateral = _surface.Lateral,
                SurfaceRolling = _surface.Rolling,
                MassShare = grid.Body.mass / Mathf.Max(1, grid.GroundedWheelCount),
                DeltaTime = dt,
                OwnerGrid = grid,
            };

            WheelSuspensionSolver.Solve(in input, ref _solveState, out var result);

            IsGrounded = result.Grounded;
            SuspensionCompression01 = result.Compression01;
            NormalLoad = result.NormalLoad;
            PlaceSocket(result.Grounded ? result.SpringLength : input.MaxTravel);
            UpdateArms();

            if (!result.Grounded)
            {
                WheelSlip = 0f;
                SurfaceName = "Airborne";
                GroundGrip = 1f;
                Tire.ApplySpin(_solveState.SpinDegrees);
                Tire.ReportSlip(0f, dt);
                return;
            }

            // ── Surface resolve (road wear, terrain layer, physics material, voxel) ──
            _surface = SurfaceSampler.Sample(in result.Hit, up, out var road);
            GroundRoad = road;
            GroundPoint = result.ContactPoint;
            GroundGrip = _surface.Lateral;
            SurfaceName = _surface.Name;

            grid.Body.AddForceAtPosition(result.SuspensionForce, originWorld, ForceMode.Force);
            grid.Body.AddForceAtPosition(result.TireForce, result.ContactPoint, ForceMode.Force);

            WheelSlip = result.Slip01;
            Tire.ApplySpin(_solveState.SpinDegrees);
            Tire.ReportSlip(WheelSlip, dt);

            // Road wear is billed by distance rolled and by how heavy the axle is: a
            // loaded hauler shreds asphalt and a light buggy does not.
            if (road != null)
            {
                float rolled = Mathf.Abs(result.ForwardSpeed) * dt;
                if (rolled > 0f && rolled < 4f)
                    road.RegisterTraffic(rolled, road.WheelLoadFor(grid.Body.mass), true);
            }
        }

        private void UpdateSteerVisual(float angle)
        {
            EnsureRig();
            if (_steerPivot != null)
                _steerPivot.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>
        /// Visual linkage: both arms pivot at the hub frame and point at the moving
        /// carrier, then stretch to reach it. This is the cue that sells the travel —
        /// a strut that slides without its wishbones following looks broken.
        /// </summary>
        private void UpdateArms()
        {
            if (_carrier == null) return;
            AimArm(_upperArm, _carrier.localPosition + new Vector3(0f, Mathf.Abs(MountOffsetX) * 0.12f, 0f));
            AimArm(_lowerArm, _carrier.localPosition);
        }

        private void AimArm(Transform arm, Vector3 targetLocal)
        {
            if (arm == null) return;
            Vector3 origin = arm.localPosition;
            Vector3 delta = targetLocal - origin;
            float length = delta.magnitude;
            if (length < 0.0005f) return;
            arm.localRotation = Quaternion.FromToRotation(Vector3.right, delta / length);
            var scale = arm.localScale;
            // Arms are authored one metre long on local X, so scale is the reach itself.
            arm.localScale = new Vector3(length, scale.y, scale.z);
        }
    }
}
