// Assets/Scripts/VoxelEngine/GridSystem/GridWheel.cs
//
// grid-terminal powered wheel/suspension block. Uses robust ground
// probing, powered drive force, steering, lateral tyre friction, visual wheel
// spin, and authored wheel sizes (2x2, 3x3, 5x5).

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridWheel : GridBlock
    {
        [Header("Wheel Size")]
        [Tooltip("Visual wheel diameter in Structural cells: 2, 3, or 5.")]
        public int wheelSizeCells = 3;

        [Header("Suspension")]
        [Tooltip("Suspension travel distance in metres.")]
        public float suspensionLength = 1.4f;
        [Tooltip("Suspension spring force at full compression.")]
        public float springForce = 140000f;
        [Tooltip("Suspension damping.")]
        public float damping = 18000f;
        [Range(0.05f, 1f)] public float suspensionStrength = 0.55f;

        [Header("Drive")]
        [Tooltip("Drive force applied when throttle input is active.")]
        public float driveForce = 75000f;
        [Tooltip("Power consumed while powered and grounded.")]
        public float powerDrawWatts = 250f;
        [Tooltip("Steering angle in degrees for steerable wheels.")]
        public float steerAngle = 30f;
        [Tooltip("Set true for front/steering wheels.")]
        public bool isSteerable = true;

        public override float PowerDraw => Enabled && IsGrounded && Grid != null
            ? powerDrawWatts * Mathf.Abs(Grid.WheelThrottle) * Mathf.Clamp01(suspensionStrength)
            : 0f;
        public bool IsGrounded { get; private set; }
        public VoxelEngine.Building.AsphaltRoad GroundRoad { get; private set; }
        public Vector3 GroundPoint { get; private set; }
        public float GroundGrip { get; private set; } = 1f;

        private float _currentThrottle;
        private float _lastSpringLength;
        private float _spinDegrees;
        private Transform _visualPivot;
        private Transform _spinPivot;

        private float WheelRadius => Mathf.Max(0.25f, wheelSizeCells * GridSize.Large.CellSize() * 0.5f);

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (blockName == "Armor Block" || string.IsNullOrEmpty(blockName))
                blockName = $"Wheel Suspension {wheelSizeCells}x{wheelSizeCells}";
            CacheVisuals();
            ConfigureForSize();
        }

        private void CacheVisuals()
        {
            _visualPivot = transform.Find("WheelVisualPivot");
            _spinPivot = transform.Find("WheelVisualPivot/TireSpinPivot");
        }

        private void ConfigureForSize()
        {
            wheelSizeCells = Mathf.Clamp(wheelSizeCells, 2, 5);
            if (wheelSizeCells <= 2)
            {
                suspensionLength = Mathf.Max(suspensionLength, 1.0f);
                springForce = Mathf.Max(springForce, 90000f);
                damping = Mathf.Max(damping, 12000f);
                driveForce = Mathf.Max(driveForce, 45000f);
                powerDrawWatts = Mathf.Max(powerDrawWatts, 150f);
            }
            else if (wheelSizeCells >= 5)
            {
                suspensionLength = Mathf.Max(suspensionLength, 2.2f);
                springForce = Mathf.Max(springForce, 260000f);
                damping = Mathf.Max(damping, 32000f);
                driveForce = Mathf.Max(driveForce, 145000f);
                powerDrawWatts = Mathf.Max(powerDrawWatts, 650f);
            }
        }

        public void UpdateWheel(GridEntity grid)
        {
            GroundRoad = null;
            if (!Enabled || grid == null || grid.Body == null)
            {
                IsGrounded = false;
                _currentThrottle = 0f;
                return;
            }

            if (_visualPivot == null) CacheVisuals();

            bool powered = grid.HasPower;
            Vector3 wheelPos = transform.position;
            float radius = WheelRadius;
            _currentThrottle = Mathf.Clamp(grid.WheelThrottle, -1f, 1f);

            Transform frame = grid.WheelFrame;
            Vector3 baseForward = frame != null ? frame.forward : transform.forward;
            Vector3 steeringAxis = frame != null ? frame.up : transform.up;
            float steer = grid.WheelSteering(this);
            Quaternion steerRot = Quaternion.AngleAxis(steer, steeringAxis);
            Vector3 forward = steerRot * baseForward;

            if (TryFindGround(grid, radius, out var hit, out var castDir))
            {
                IsGrounded = true;
                Vector3 supportDir = -castDir.normalized;
                float currentLength = Mathf.Clamp(hit.distance - radius, 0f, suspensionLength);
                float compression = suspensionLength > 0f ? (suspensionLength - currentLength) / suspensionLength : 0f;

                float springVelocity = (_lastSpringLength - currentLength) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                float force = (compression * springForce * suspensionStrength) + (springVelocity * damping);
                force = Mathf.Max(0f, force);
                grid.Body.AddForceAtPosition(supportDir * force, wheelPos, ForceMode.Force);

                // Asphalt under the tyre (9.41.0). Read off the collider the suspension already hit,
                // so there is no extra probe per wheel per tick. The multipliers live on the road's
                // run, which is the same object the player's feet read, so a worn strip loses its
                // grip for a rig and its stride for a walker on the same curve.
                var road = hit.collider != null ? hit.collider.GetComponentInParent<VoxelEngine.Building.AsphaltRoad>() : null;
                if (road != null && !road.IsSupported) road = null;
                float traction = road != null ? road.TractionMultiplier : 1f;
                float grip     = road != null ? road.GripMultiplier     : 1f;
                GroundRoad = road;
                GroundPoint = hit.point;
                GroundGrip = grip;

                if (powered && Mathf.Abs(_currentThrottle) > 0.01f)
                {
                    Vector3 driveDir = Vector3.ProjectOnPlane(forward, hit.normal).normalized;
                    if (driveDir.sqrMagnitude > 0.0001f)
                        grid.Body.AddForceAtPosition(driveDir * _currentThrottle * driveForce * traction, wheelPos, ForceMode.Force);
                }

                Vector3 pointVelocity = grid.Body.GetPointVelocity(wheelPos);
                if (grid.WheelBrake > 0f)
                {
                    // Mechanical tyre brake, including bounded hill holding; never acts airborne.
                    int supports = Mathf.Max(1, grid.GroundedWheelCount);
                    Vector3 acceleration = -Vector3.ProjectOnPlane(pointVelocity, hit.normal)
                        / Mathf.Max(0.001f, Time.fixedDeltaTime)
                        - Vector3.ProjectOnPlane(grid.WheelGravity, hit.normal);
                    float share = grid.Body.mass / supports;
                    float cap = Mathf.Min(driveForce, share * IndustrialWorld.Navigation.RoadWheelMath.BrakeAcceleration)
                        * Mathf.Max(0f, grip);
                    grid.Body.AddForceAtPosition(Vector3.ClampMagnitude(acceleration * share, cap)
                        * Mathf.Clamp01(grid.WheelBrake), wheelPos, ForceMode.Force);
                }
                Vector3 lateral = Vector3.Project(pointVelocity, transform.right);
                float friction = Mathf.Clamp(grid.Body.mass * 2.2f, 2500f, 45000f) * grip;
                grid.Body.AddForceAtPosition(-lateral * friction, wheelPos, ForceMode.Force);

                if (road != null)
                {
                    // Bill the run for what this tyre actually rolled, weighted by the grid's mass:
                    // a loaded hauler shreds a road and a light buggy does not, which is the whole
                    // reason "a road under heavy traffic wears faster" is a rule and not a flavour line.
                    float rolled = Mathf.Abs(Vector3.Dot(pointVelocity, forward.normalized)) * Time.fixedDeltaTime;
                    if (rolled > 0f && rolled < 4f)
                        road.RegisterTraffic(rolled, road.WheelLoadFor(grid.Body.mass), true);
                }

                _lastSpringLength = currentLength;
                UpdateVisuals(steer, grid.Body.GetPointVelocity(wheelPos), forward, radius, currentLength);
            }
            else
            {
                IsGrounded = false;
                _currentThrottle = 0f;
                _lastSpringLength = suspensionLength;
                UpdateVisuals(steer, Vector3.zero, forward, radius, suspensionLength);
            }
        }

        private bool TryFindGround(GridEntity grid, float radius, out RaycastHit bestHit, out Vector3 bestDir)
        {
            bestHit = default;
            bestDir = Vector3.down;

            float castRadius = Mathf.Clamp(radius * 0.22f, 0.25f, 1.1f);
            float castDistance = suspensionLength + radius + 0.75f;
            Vector3 origin = transform.position + transform.up * 0.25f;

            Vector3[] dirs =
            {
                -transform.up,
                Vector3.down,
                -(transform.up + Vector3.up).normalized,
                -(transform.up - Vector3.up).normalized
            };

            float bestDistance = float.MaxValue;
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector3 dir = dirs[d];
                if (dir.sqrMagnitude < 0.0001f) continue;
                dir.Normalize();

                var hits = Physics.SphereCastAll(origin, castRadius, dir, castDistance, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0) continue;

                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    if (hit.collider == null) continue;
                    var hitGrid = hit.collider.GetComponentInParent<GridEntity>();
                    if (hitGrid == grid) continue; // never ground against our own ship
                    if (hit.distance < bestDistance)
                    {
                        bestDistance = hit.distance;
                        bestHit = hit;
                        bestDir = dir;
                    }
                }
            }

            return bestDistance < float.MaxValue;
        }

        private void UpdateVisuals(float steer, Vector3 velocity, Vector3 forward, float radius, float suspensionTravel)
        {
            if (_visualPivot != null)
            {
                float extension = Mathf.Clamp(suspensionTravel, 0f, suspensionLength);
                _visualPivot.localRotation = Quaternion.Euler(0f, steer, 0f);
                _visualPivot.localPosition = new Vector3(0f, -extension - radius * 0.45f, 0f);
            }

            float forwardSpeed = Vector3.Dot(velocity, forward.normalized);
            if (Mathf.Abs(forwardSpeed) > 0.01f && radius > 0.01f)
                _spinDegrees += (forwardSpeed / radius) * Mathf.Rad2Deg * Time.fixedDeltaTime;

            if (_spinPivot != null)
                _spinPivot.localRotation = Quaternion.AngleAxis(_spinDegrees, Vector3.right);
        }
    }
}
