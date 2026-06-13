// Assets/Scripts/VoxelEngine/GridSystem/GridWheel.cs
//
// Space-Engineers-style powered wheel/suspension block. Uses robust ground
// probing, powered drive force, steering, lateral tyre friction, visual wheel
// spin, and authored wheel sizes (2x2, 3x3, 5x5).

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridWheel : GridBlock
    {
        [Header("Wheel Size")]
        [Tooltip("Visual wheel diameter in large-grid cells: 2, 3, or 5.")]
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

        public override float PowerDraw => Enabled && IsGrounded ? powerDrawWatts : 0f;
        public bool IsGrounded { get; private set; }

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
            if (!Enabled || grid == null || grid.Body == null)
            {
                IsGrounded = false;
                return;
            }

            if (_visualPivot == null) CacheVisuals();

            bool powered = grid.HasPower;
            Vector3 wheelPos = transform.position;
            float radius = WheelRadius;

            float steer = isSteerable ? grid.ThrustInput.x * steerAngle : 0f;
            Quaternion steerRot = Quaternion.AngleAxis(steer, transform.up);
            Vector3 forward = steerRot * transform.forward;

            if (TryFindGround(grid, radius, out var hit, out var castDir))
            {
                IsGrounded = true;
                Vector3 supportDir = -castDir.normalized;
                float currentLength = Mathf.Clamp(hit.distance - radius, 0f, suspensionLength);
                float compression = suspensionLength > 0f ? (suspensionLength - currentLength) / suspensionLength : 0f;

                if (powered)
                {
                    float springVelocity = (_lastSpringLength - currentLength) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                    float force = (compression * springForce * suspensionStrength) + (springVelocity * damping);
                    force = Mathf.Max(0f, force);
                    grid.Body.AddForceAtPosition(supportDir * force, wheelPos, ForceMode.Force);

                    float throttle = grid.ThrustInput.z;
                    if (Mathf.Abs(throttle) > 0.01f)
                    {
                        Vector3 driveDir = Vector3.ProjectOnPlane(forward, hit.normal).normalized;
                        if (driveDir.sqrMagnitude > 0.0001f)
                            grid.Body.AddForceAtPosition(driveDir * throttle * driveForce, wheelPos, ForceMode.Force);
                    }

                    Vector3 pointVelocity = grid.Body.GetPointVelocity(wheelPos);
                    Vector3 lateral = Vector3.Project(pointVelocity, transform.right);
                    float friction = Mathf.Clamp(grid.Body.mass * 2.2f, 2500f, 45000f);
                    grid.Body.AddForceAtPosition(-lateral * friction, wheelPos, ForceMode.Force);
                }

                _lastSpringLength = currentLength;
                UpdateVisuals(steer, grid.Body.GetPointVelocity(wheelPos), forward, radius, currentLength);
            }
            else
            {
                IsGrounded = false;
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
